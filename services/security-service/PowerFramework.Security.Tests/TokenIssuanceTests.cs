// ==================================================================================================
//  TokenIssuanceTests - C-01, the sole minter: claim fidelity, the clock seam, and fail-fast startup
//  ------------------------------------------------------------------------------------------------
//  WHAT THIS SUITE PROVES, IN ORDER OF SEVERITY
//
//    1. THE HOST REFUSES TO START WITHOUT USABLE SIGNING MATERIAL, AND NEVER SUBSTITUTES ANY. Absent,
//       empty, blank, structurally unusable and SYMMETRIC-SHAPED material each stop the host, one
//       enumerated case at a time. The symmetric row is the one that matters most in practice: the
//       operational instructions tell an operator to generate a signing value with
//       `openssl rand -base64 32`, which produces exactly a 32-byte value - a perfectly good HS256
//       key and not an asymmetric one at all. A service that quietly downgraded to HMAC there, or
//       generated an ephemeral pair of its own, would start cleanly and then invalidate every token
//       the other three services hold, differently on every restart. These rows are what make that
//       impossible to introduce silently.
//    2. EVERY CLAIM IS DERIVED FROM CONFIGURATION AND FROM THE REQUEST, NEVER WRITTEN DOWN. The
//       issuer identity, the audience roster, the key identifier, the algorithm and the lifetime are
//       all READ BACK from the options the booted host actually loaded and compared against what the
//       minted token carries. Restating a deployment value here would assert this file's beliefs about
//       the configuration rather than the configuration itself, and would keep passing after it changed.
//    3. THE CLOCK SEAM IS GENUINELY IN THE ISSUANCE PATH. Two issuances under one fixed instant are
//       BYTE-IDENTICAL, a fractional instant is truncated to a whole second, and advancing the seam
//       moves the claims by exactly the interval advanced. The last of those three is the permanent
//       form of the proof: an issuer reading an ambient clock would ignore the advance entirely, so
//       the property cannot be satisfied by accident.
//    4. THE MINT-THEN-VERIFY LOOP CLOSES AGAINST THE PUBLISHED KEY SET. A token minted by the host's
//       own issuer is verified against the material fetched ANONYMOUSLY from that same host's
//       /.well-known/jwks.json, rebuilt from the public modulus and exponent - never against a key
//       this file held separately, and never against private material. That is what proves Security is
//       one coherent sole issuer rather than two unrelated halves, and it is also exactly what a real
//       consumer's stock bearer handler does.
//    5. AN AUDIENCE OFF THE ROSTER MINTS NOTHING. The refusal is asserted to produce no token at all,
//       because a partial outcome on a credential-issuing operation is worse than a failure.
//    6. THE MINTER REFUSES ITS OWN UNUSABLE INPUTS TOO, one layer below the configuration checks: a
//       symmetric credential, an algorithm it does not produce, a key identifier disagreeing with the
//       published one, a blank issuer, an empty or blank-entried roster and a sub-second lifetime. Most
//       of those are unreachable through configuration because the options validator refuses the same
//       conditions first, which is exactly why they would otherwise sit permanently unexercised - and
//       why the verification entry point is exposed to this project by its own stated design. Together
//       with the rest of this suite they leave the issuance file with no unexercised branch at all.
//
//  NO USER RULES GOVERN THIS FILE. `review_rules` reports exactly one line - "No user rules provided."
//  Their absence is not licence to lower the bar, so this file is held to the enterprise-standard
//  baseline the migration record states (AAP section 0.7.2): warning-clean under warnings-as-errors,
//  correct nullable annotations, every disposable disposed, no secret in a fixture, and a hard
//  per-service coverage gate. The binding constraints are therefore the twelve non-rule constraints of
//  AAP section 0.7.3, and the four that bear hardest on this file are noted where they apply.
//
//  KEY HYGIENE IN THIS FILE, WHICH MATTERS AS MUCH AS IN THE CODE UNDER TEST (C-F)
//
//    EVERY KEY IS GENERATED AT RUN TIME AND EVERY TOKEN IS MINTED AT RUN TIME. No key, no armour
//    delimiter, no encoded run, no certificate and no token literal appears anywhere below, and
//    nothing is copied - transformed or otherwise - from any of the repository's hardcoded-secret
//    sites. The signing material comes from `SecurityAppFactory`, which generates a fresh pair per
//    host; the symmetric-shaped negative comes from that factory's own generator; and the two
//    structurally invalid negatives are obviously synthetic short words, never a truncated real key.
//
//    THE ANTI-PATTERN THIS SERVICE EXISTS TO REPLACE is a page that hardcodes a PEM RSA private key
//    and signs a JWS with it [tests/blink/test_jws.htm:L8-L22, consumed at :L23]. It is cited here by
//    LOCATOR ONLY: its contents are never reproduced, and this project never opens it - or any other
//    path under the read-only legacy tree - at test time (C-C). The same applies to the two base64-DER
//    private keys at ws_objects/pfw.demos.pbl.src/u_cst_tabpage_utility_crypto.sru:L466 and :L718 and
//    to the shared configuration key at ws_objects/pfw.tests.pbl.src/n_cst_appconfig.sru:L22-L23.
//    `SigningKeyProvider` reading its material exclusively from configuration is precisely what makes
//    all four of those sites unrepresentable in the .NET tree.
//
//    NO RESOLVED KEY MATERIAL IS EVER LOGGED OR ASSERTED ON. Where a startup failure is inspected, the
//    assertion is that the message NAMES the configuration key and CONTAINS NO FRAGMENT of what was
//    supplied - which is also why every row here can be written without the value being needed.
//
//  WHY RS256 IS NOT AN INVENTION, AND WHERE ITS PROVENANCE COMES FROM
//
//    The oracle already owns the signing primitives this service takes over: `RSASign` in its string
//    and blob arities [ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L70, :L71] and `VerifyRSASign` in
//    both [:L72, :L73], each parameterised by a digest selector drawn from the one hash-type set whose
//    own catalogue comment records that it governs hashing, signing AND verification
//    [ws_objects/pfw.shared.pbl.src/enums.sru:L927]. Its SHA-256 member is the value 2 at [:L930].
//    `TokenIssuer` reports the member its algorithm signs over, and the row below compares that report
//    against the PRESERVED KERNEL CONSTANT rather than against a number - so the correspondence is
//    checkable rather than merely asserted in prose. The constant is CONSUMED from
//    PowerFramework.Shared.Kernel and never re-spelled: the preserved screaming-case identifiers have
//    exactly one definition in the estate, and .editorconfig's naming suppressions are scoped to that
//    definition's own file and do not extend to a test project.
//
//  THE FAIL-FAST POSTURE DESCENDS FROM THE ORACLE AND IS NOT SOFTENED HERE
//
//    The legacy answers a structural fault by terminating: its system-error handler unpacks a
//    seven-field assert payload split on a line separator - gated on a count of exactly seven
//    [ws_objects/pfw.pbl.src/pfw.sra:L119] - renders it, and then executes `HALT CLOSE`
//    [:L143], inside the same application object whose open event pairs initialization with
//    finalization [:L91 against :L108]. The .NET equivalent is startup validation that refuses to
//    start, which the composition root expresses by binding with validate-on-start and then eagerly
//    resolving both the options contract and the minter before the pipeline is wired. Warning and
//    continuing, generating a key, or falling back to a symmetric one would each be a behavioural
//    change dressed as robustness, so the rows below assert the refusal rather than tolerate a
//    fallback.
//
//  WHAT THIS FILE DELIBERATELY DOES NOT ASSERT
//
//    NO KEY-SIZE FLOOR, NO STRONGER ALGORITHM, NO KEY ROTATION AND NO TOKEN REVOCATION (C-B). The
//    oracle keeps 1024-bit RSA a first-class legal size [ws_objects/pfw.shared.pbl.src/enums.sru:L965],
//    so a row demanding that a short key be refused would pin a silent correction of legacy behaviour;
//    the "too short" negatives below are therefore STRUCTURALLY INVALID values rather than short keys.
//    Nothing here names a deferred service, and no signing, verification or transport-credential
//    setting is scaffolded for one (C-D). Only this service is exercised - no other service's project
//    is referenced and no other service's host is booted (C-A).
//
//  HOW THE ISSUANCE ROUTE IS REACHED, AND WHY IT IS NOT REACHED WITH A BEARER TOKEN (C-G)
//
//    `POST /v1/tokens` is the system's one MUTUAL-TLS edge, and that is the contract rather than a
//    local decision: a caller cannot present a bearer token in order to obtain its first one, so the
//    published document applies `clientCredential` AND `mutualTls` to this operation as an OVERRIDE of
//    the document-level bearer requirement, as two ALTERNATIVES either of which satisfies it
//    [shared/PowerFramework.Contracts/OpenApi/security.v1.yaml]. The route's own policy requires one of
//    the two on the request - a shared secret as an HTTP `Basic` credential, or a client certificate on
//    the connection - and the handler checks for one unconditionally. THIS FILE EXERCISES THE
//    CERTIFICATE ALTERNATIVE, which is the one that cannot be reached without a handshake; the sibling
//    rows covering the `Basic` alternative live in `IssuanceRosterTests.cs`, beside the roster it is
//    authenticated against. Three consequences shape this file, and none of them is a workaround:
//
//      * THE SUBSTANCE IS ASSERTED WITHOUT HTTP AT ALL, against `TokenIssuer` resolved from the booted
//        host. That is where claim fidelity, the lifetime, determinism and the audience gate live.
//      * THE WIRE SHAPE IS ASSERTED BY CALLING THE HANDLER DIRECTLY over a connection carrying a
//        caller certificate. The handler is `internal` for exactly this purpose, by its own stated
//        design, because the in-process host terminates no TLS and so cannot produce the handshake the
//        operation requires.
//      * AUTHENTICATION IS NEVER DISABLED, STUBBED OR EXEMPTED. Nothing here calls `AllowAnonymous`,
//        replaces an authentication handler or relaxes a policy, and the closing row asserts that an
//        unauthenticated `POST /v1/tokens` is refused - so the boundary this file exercises is the
//        same boundary a caller meets.
//
//  INFRASTRUCTURE IS CONSUMED, NEVER CLONED. `SecurityAppFactory` supplies the host, the single
//  deterministic clock double and the token helper; `IssuanceFixture` supplies the caller certificate;
//  `StubTlsConnectionFeature` supplies the transport seam. No second factory and no second clock
//  double is declared here, and no package is added - the fixed clock is the hand-authored double,
//  because a time-testing package and every mocking library are deliberately absent from
//  Directory.Packages.props. Every factory constructed below boots a host and is disposed.
// ==================================================================================================

using System.Net;
using System.Net.Http.Json;
using System.Net.Mime;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

using PowerFramework.Security.Configuration;
using PowerFramework.Security.Endpoints;
using PowerFramework.Security.Tokens;
using PowerFramework.Shared.Kernel;

namespace PowerFramework.Security.Tests;

/// <summary>
/// The token-issuance suite for contract C-01: the claims a minted token carries, the lifetime and
/// clock seam that make those claims reproducible, the audience gate, the mint-then-verify loop
/// against the published key set, the published wire shape, and the startup refusals that keep this
/// service from ever running without usable signing material.
/// </summary>
/// <remarks>
/// See this file's header for the constraints that govern it, the provenance of the signing algorithm,
/// and the reason the issuance route is reached the way it is. Every row reads the values it compares
/// against from the options the booted host loaded, so nothing here restates a deployment fact.
/// </remarks>
public sealed class TokenIssuanceTests
{
    /// <summary>
    /// The instant the clock seam is pinned to wherever a row needs a literal one.
    /// </summary>
    /// <remarks>
    /// Obviously synthetic and deliberately not "now": a literal instant is what makes an assertion
    /// about <c>iat</c>, <c>nbf</c> and <c>exp</c> checkable rather than approximate. It is pinned ONLY
    /// on the rows that make no HTTP request, because the inbound bearer handler's clock-skew allowance
    /// is configured to zero - a token whose validity window sits away from real time is rejected by
    /// the very host that minted it, so the round-trip rows leave the seam at its default.
    /// </remarks>
    private static readonly DateTimeOffset PinnedInstant =
        new(2024, 1, 2, 3, 4, 5, TimeSpan.Zero);

    /// <summary>
    /// The registered caller these rows mint as: the one roster entry granted EVERY audience the
    /// deployment serves.
    /// </summary>
    /// <remarks>
    /// <para>
    /// CHOSEN FOR ITS GRANT BREADTH, WHICH IS WHAT KEEPS THESE ROWS ABOUT WHAT THEY WERE ALWAYS ABOUT.
    /// The issuer applies two gates - the deployment-wide audience roster, then the caller's own entry -
    /// so a row that mints for an arbitrary audience needs a subject permitted all of them. This entry
    /// grants all four, so rows that iterate <c>Security:Audiences</c> or take its first element keep
    /// asserting determinism, key agreement and rendering rather than accidentally asserting the
    /// permission model. Rows that DO assert the permission model name their own subject.
    /// </para>
    /// <para>
    /// Declared in the Development settings overlay as the end-to-end suite's identity, which is the
    /// environment a test host runs under.
    /// </para>
    /// </remarks>
    private const string RosteredCaller = "pfw-e2e-suite";

    /// <summary>A scope <see cref="RosteredCaller"/> is granted.</summary>
    /// <remarks>
    /// An issuance request must name at least one scope and the roster must grant every scope named, so
    /// this is a granted one rather than an arbitrary token. It is also the scope this service's
    /// authenticated probe requires, so a token minted with it is usable as well as issuable.
    /// </remarks>
    private const string GrantedScope = "ping";

    /// <summary>
    /// A scope set whose members are ordinary, distinct and free of white space, so the granted value
    /// can be reconstructed by joining them.
    /// </summary>
    /// <remarks>
    /// The issuance request type refuses an empty set, a blank member, a member carrying white space
    /// and a repeated member, so these rows exercise the accepted shape rather than the refused ones -
    /// which the sibling request-validation suite owns.
    /// </remarks>
    public static TheoryData<string, string, string[]> ClaimMatrix =>
        new()
        {
            // The gateway, asking for the DataServices audience with the two scopes its client requests
            // [services/gateway-service/PowerFramework.Gateway/Clients/DataServicesClient.cs:L1286-L1289].
            {
                "powerframework-gateway",
                "powerframework-dataservices",
                ["dataservices.datawindow", "dataservices.columnexpression"]
            },

            // DataServices, asking for the Persistence audience with the two scopes its client requests
            // [services/dataservices-service/PowerFramework.DataServices/Clients/PersistenceClient.cs:L438-L441].
            {
                "powerframework-dataservices",
                "powerframework-persistence",
                ["persistence.read", "persistence.write"]
            },

            // DataServices again, this time against Security itself - the one cross-service call that
            // needs a cryptographic scope
            // [services/dataservices-service/PowerFramework.DataServices/Clients/SecurityClient.cs:L2232,
            //  its CryptoScope constant].
            {
                "powerframework-dataservices",
                "powerframework-security",
                ["security.crypto"]
            },

            // A SUBJECT THAT IS NOT ITSELF AN AUDIENCE, which is the property this row exists for: a
            // caller identity and an audience identity are different namespaces, and nothing requires a
            // caller to be addressable. The suite identity is exactly such a caller.
            { RosteredCaller, "powerframework-gateway", ["capabilities", "datawindow"] },
        };

    /// <summary>
    /// Audience identities that no configured roster carries, each refused without minting.
    /// </summary>
    /// <remarks>
    /// Every row is asserted to be absent from the roster before its refusal is asserted, so a future
    /// roster that happened to adopt one of these names fails the guard loudly instead of turning the
    /// row into a vacuous pass.
    /// </remarks>
    public static TheoryData<string> UnlistedAudiences =>
        new()
        {
            "powerframework-elsewhere",
            "powerframework-gateway-staging",
            "gateway",
            "https://security-service/an-audience-nobody-configured",
        };

    /// <summary>
    /// Signing values that are present and well formed as text but are not asymmetric private keys, so
    /// each must stop the host.
    /// </summary>
    /// <remarks>
    /// <para>
    /// SYNTHETIC BY CONSTRUCTION AND SHORT ON PURPOSE, and neither is a truncated real key (C-F). They
    /// stand for the two ways a deployment gets this wrong other than leaving it blank: a placeholder
    /// somebody meant to replace, and a word typed in by hand.
    /// </para>
    /// <para>
    /// THE REFUSAL IS ABOUT STRUCTURE AND NOT ABOUT LENGTH. No key-size floor is asserted anywhere in
    /// this file, because the oracle keeps a smaller RSA size a first-class legal value on the surface
    /// where a caller supplies it [ws_objects/pfw.shared.pbl.src/enums.sru:L965]; these values fail
    /// because they are not a key in either accepted encoding, which is a different statement.
    /// </para>
    /// </remarks>
    public static TheoryData<string> UnusableSigningValues =>
        new()
        {
            "too-short",
            "replace-me-before-deploying",
        };

    /// <summary>
    /// Values that are present in configuration yet carry no content, so each must stop the host.
    /// </summary>
    /// <remarks>
    /// Enumerated separately from the absent case because the two reach the validator by different
    /// routes - the composition root's presence guard skips a blank value rather than assigning it, so
    /// a blank arrives as unset - and because a whitespace-only value is exactly the shape an
    /// environment file produces when a variable is declared and left unfilled.
    /// </remarks>
    public static TheoryData<string> BlankSigningValues =>
        new()
        {
            "",
            " ",
            "\t",
            "\r\n",
        };

    // ==============================================================================================
    //  AREA A - CLAIM FIDELITY. Every claim derived from the request or from configuration.
    // ==============================================================================================

    /// <summary>
    /// A token minted from a caller identity, an audience and a scope set carries all three, under the
    /// claim names the published contract uses.
    /// </summary>
    /// <param name="subject">The caller identity the token is minted for.</param>
    /// <param name="scopes">The requested scope set.</param>
    /// <remarks>
    /// <para>
    /// THE CLAIM NAMES ARE THE CONTRACT'S, READ FROM THE PAYLOAD BY NAME. <c>sub</c> and <c>scope</c>
    /// are asserted by their literal spellings because those spellings are what a recipient's stock
    /// bearer handler and any consumer reading the granted set will look for; the audience is read
    /// through the library's own accessor because the registered claim is permitted to be either a
    /// single string or an array and the accessor models both.
    /// </para>
    /// <para>
    /// THE GRANTED SET IS ONE VALUE, SPACE DELIMITED, and it is compared against the requested set
    /// joined the same way. That is the property the response half of the contract depends on: the
    /// granted scope reported to the caller and the scope claim inside the token are the same string,
    /// so a caller that reads one has read the other.
    /// </para>
    /// <para>
    /// THE AUDIENCE IS TAKEN FROM THE HOST'S OWN ROSTER rather than named here, so this row keeps
    /// working when the roster changes and cannot pass by agreeing with a stale copy of it.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(ClaimMatrix))]
    public async Task MintedTokenCarriesTheRequestedIdentityAudienceAndScopesAsync(
        string subject,
        string audience,
        string[] scopes)
    {
        ArgumentNullException.ThrowIfNull(scopes);

        await using SecurityAppFactory factory = new();

        // THE AUDIENCE IS A ROW PARAMETER RATHER THAN THE ROSTER'S FIRST ENTRY, because the issuer now
        // applies a per-caller gate as well as the deployment-wide one: each row names an audience its
        // own subject is granted, which is what keeps the row asserting the CLAIMS rather than the
        // permission model. That the deployment serves it at all is asserted here, once, for every row.
        Assert.Contains(audience, factory.ResolveSecurityOptions().Audiences);

        IssuedToken token = factory.IssueToken(subject, audience, scopes);
        JsonWebToken parsed = new(token.AccessToken);

        Assert.Equal(subject, ReadClaim(parsed, "sub"));
        Assert.Equal(audience, Assert.Single(parsed.Audiences));
        Assert.Equal(string.Join(' ', scopes), ReadClaim(parsed, "scope"));

        // The issuer reports the granted set as well, and the two cannot be allowed to differ: the
        // response projection carries this value while the recipient reads the claim above.
        Assert.Equal(string.Join(' ', scopes), token.GrantedScope);
    }

    /// <summary>
    /// The issuer identity, the key identifier and the signature scheme are the configured ones, and
    /// the scheme corresponds to the digest the oracle's own catalogue names.
    /// </summary>
    /// <remarks>
    /// <para>
    /// FOUR AGREEMENTS IN ONE ROW, because they are one statement about a single minted token: the
    /// <c>iss</c> claim is the configured identity that three other services validate against; the
    /// header's <c>kid</c> is the configured identifier a verifier selects a published key by; the
    /// header's <c>alg</c> is the configured scheme; and that scheme is asserted to be RS256
    /// specifically, through the library's own constant rather than a bare string.
    /// </para>
    /// <para>
    /// THE FOURTH ASSERTION IS THE PARITY ONE. `TokenIssuer` reports the legacy hash-type member its
    /// algorithm signs over, produced by the same total switch that admits the algorithm, so an
    /// accepted scheme and the member it maps to cannot fall out of step. Comparing that report against
    /// the preserved kernel constant is what makes the provenance recorded in this file's header
    /// checkable: the oracle's signature primitives take a digest selector from one shared hash-type
    /// set [ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L70-L73,
    /// ws_objects/pfw.shared.pbl.src/enums.sru:L927], whose SHA-256 member is the value 2 at [:L930].
    /// </para>
    /// </remarks>
    [Fact]
    public async Task MintedTokenNamesTheConfiguredIssuerKeyAndSchemeAsync()
    {
        await using SecurityAppFactory factory = new();

        SecurityOptions options = factory.ResolveSecurityOptions();

        IssuedToken token = factory.IssueToken();
        JsonWebToken parsed = new(token.AccessToken);

        Assert.Equal(options.Issuer, ReadClaim(parsed, "iss"));
        Assert.Equal(options.SigningKeyId, parsed.Kid);
        Assert.Equal(options.SigningKeyId, token.KeyId);
        Assert.Equal(options.SigningAlgorithm, parsed.Alg);
        Assert.Equal(SecurityAlgorithms.RsaSha256, parsed.Alg);

        Assert.Equal(
            Enums.CRYPTO_HASH_SHA256,
            factory.Services.GetRequiredService<TokenIssuer>().LegacySigningHashType);
    }

    // ==============================================================================================
    //  AREA B - THE SHORT LIFETIME AND THE CLOCK SEAM. Byte-identical claims under a fixed instant.
    // ==============================================================================================

    /// <summary>
    /// The configured lifetime is honoured exactly, and the not-before instant is the issuance instant.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE LIFETIME IS OVERRIDDEN TO A VALUE THE SETTINGS DO NOT CARRY, deliberately. A row run against
    /// the default would still pass if the issuer ignored configuration and used a constant that
    /// happened to equal it; varying the value is what proves the configured span drives the claims.
    /// </para>
    /// <para>
    /// <c>nbf</c> IS ASSERTED EQUAL TO <c>iat</c> RATHER THAN MERELY NEAR IT. The issuer takes ONE
    /// clock reading and derives all three instants from it, so a token whose not-before differed from
    /// its issuance instant would mean a second reading had crept in - which is the defect that makes a
    /// characterization recording irreproducible.
    /// </para>
    /// <para>
    /// The arithmetic is done on the numeric claims rather than on the library's converted properties,
    /// because seconds since the epoch is what the wire carries and what a consumer computes with.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ConfiguredLifetimeIsHonouredExactlyAsync()
    {
        TimeSpan lifetime = TimeSpan.FromSeconds(45);

        await using SecurityAppFactory factory = new() { TokenLifetime = lifetime };

        factory.Clock.SetUtcNow(PinnedInstant);

        IssuedToken token = factory.IssueToken();
        JsonWebToken parsed = new(token.AccessToken);

        long issuedAt = ReadNumericClaim(parsed, "iat");
        long notBefore = ReadNumericClaim(parsed, "nbf");
        long expires = ReadNumericClaim(parsed, "exp");

        Assert.Equal(lifetime, factory.ResolveSecurityOptions().TokenLifetime);
        Assert.Equal((long)lifetime.TotalSeconds, expires - issuedAt);
        Assert.Equal(issuedAt, notBefore);

        Assert.Equal(PinnedInstant.ToUnixTimeSeconds(), issuedAt);
        Assert.Equal(PinnedInstant, token.IssuedAt);
        Assert.Equal(PinnedInstant + lifetime, token.ExpiresAt);
        Assert.Equal((long)lifetime.TotalSeconds, token.ExpiresInSeconds);
    }

    /// <summary>
    /// An instant carrying a fraction of a second is truncated, not rounded, before any claim is
    /// derived from it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// TRUNCATION IS WHAT MAKES BYTE-IDENTICAL CLAIMS POSSIBLE AT ALL, because the wire carries whole
    /// seconds: without it two readings inside the same second would produce the same claims by
    /// accident and two readings straddling a boundary would not, which is not a property a
    /// characterization comparison can rely on.
    /// </para>
    /// <para>
    /// ROUNDING IS THE WRONG DIRECTION AND IS RULED OUT HERE. Rounding an issuance instant up would
    /// place it in the future, and rounding an expiry up would let a token outlive its configured
    /// lifetime by a fraction of a second. The chosen fraction is above the half-second precisely so
    /// that a rounding implementation would move the value and fail this row.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task FractionalInstantIsTruncatedToAWholeSecondAsync()
    {
        DateTimeOffset fractional = PinnedInstant.AddMilliseconds(678).AddTicks(9012);

        await using SecurityAppFactory factory = new();

        factory.Clock.SetUtcNow(fractional);

        IssuedToken token = factory.IssueToken();
        JsonWebToken parsed = new(token.AccessToken);

        Assert.Equal(PinnedInstant, token.IssuedAt);
        Assert.Equal(PinnedInstant.ToUnixTimeSeconds(), ReadNumericClaim(parsed, "iat"));
        Assert.Equal(PinnedInstant.ToUnixTimeSeconds(), ReadNumericClaim(parsed, "nbf"));
    }

    /// <summary>
    /// Two issuances of the same request under one fixed instant are byte-identical.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE DETERMINISM PROPERTY THE CHARACTERIZATION MODEL DEPENDS ON, ASSERTED RATHER THAN MERELY
    /// ALLOWED. Paired recordings are only comparable when every non-deterministic value is maskable
    /// from both sides, and the clock is one of this service's two primary sources of one.
    /// </para>
    /// <para>
    /// THE WHOLE TOKEN IS COMPARED, NOT JUST THE INSTANTS, and that is a legitimate and stronger
    /// statement for this scheme: an RSA PKCS#1 v1.5 signature is deterministic, so identical claims
    /// signed by one key produce identical bytes. A row comparing only the three instants would still
    /// pass if the issuer had stamped an identifier or a nonce of its own into the payload.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task FixedClockProducesByteIdenticalIssuancesAsync()
    {
        await using SecurityAppFactory factory = new();

        factory.Clock.SetUtcNow(PinnedInstant);

        string audience = factory.ResolveSecurityOptions().Audiences[0];

        IssuedToken first = factory.IssueToken(RosteredCaller, audience, [GrantedScope]);
        IssuedToken second = factory.IssueToken(RosteredCaller, audience, [GrantedScope]);

        Assert.Equal(first.IssuedAt, second.IssuedAt);
        Assert.Equal(first.ExpiresAt, second.ExpiresAt);
        Assert.Equal(first.ExpiresInSeconds, second.ExpiresInSeconds);

        // COMPARED BY FINGERPRINT, NOT BY VALUE (C-F). Byte-identity is exactly the claim being made and
        // it survives the digest intact; what does not survive is the disclosure. Passing two live tokens
        // to Assert.Equal would render both into the failure message, and from there into the CI log and
        // the test report - see SensitiveValueAssertions.cs for the full reasoning.
        Assert.Equal(
            SensitiveValueAssertions.Fingerprint(first.AccessToken),
            SensitiveValueAssertions.Fingerprint(second.AccessToken));
    }

    /// <summary>
    /// Advancing the substituted clock moves the claims by exactly the interval advanced.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE PERMANENT PROOF THAT THE SEAM IS IN THE PATH, and the reason this row exists alongside the
    /// determinism row above. Two identical issuances would also agree if the issuer read an ambient
    /// clock twice quickly enough; nothing about an ambient clock, however, responds to an advance of
    /// the substituted one. An implementation that reached for the system clock would leave the third
    /// token sitting at real time and fail every assertion below.
    /// </para>
    /// <para>
    /// The advance is larger than the resolution of anything involved, so the row cannot pass by timing
    /// coincidence, and the resulting token is asserted to DIFFER from the first - the claims moved, so
    /// the bytes must have.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AdvancingTheClockSeamMovesEveryInstantAsync()
    {
        TimeSpan advance = TimeSpan.FromMinutes(37);

        await using SecurityAppFactory factory = new();

        factory.Clock.SetUtcNow(PinnedInstant);

        IssuedToken before = factory.IssueToken();

        factory.Clock.Advance(advance);

        IssuedToken after = factory.IssueToken();

        Assert.Equal(PinnedInstant + advance, after.IssuedAt);
        Assert.Equal(before.IssuedAt + advance, after.IssuedAt);
        Assert.Equal(before.ExpiresAt + advance, after.ExpiresAt);
        Assert.Equal(before.ExpiresInSeconds, after.ExpiresInSeconds);

        // DISTINCTNESS BY FINGERPRINT, for the same reason the determinism row above compares digests:
        // Assert.NotEqual renders the shared value when the two DO agree, which is precisely the failure
        // case here, so the defect and the disclosure would arrive together.
        Assert.NotEqual(
            SensitiveValueAssertions.Fingerprint(before.AccessToken),
            SensitiveValueAssertions.Fingerprint(after.AccessToken));
    }


    // ==============================================================================================
    //  AREA C - THE AUDIENCE GATE. The configured roster is the whole permission decision.
    // ==============================================================================================

    /// <summary>
    /// An audience the configured roster does not carry mints nothing at all.
    /// </summary>
    /// <param name="audience">The unlisted audience.</param>
    /// <remarks>
    /// <para>
    /// THE ABSENCE OF A TOKEN IS THE ASSERTION, not merely the outcome value. A partial outcome on a
    /// credential-issuing operation would be worse than a refusal: the issuer's own design refuses an
    /// unlisted audience BEFORE any cryptographic work begins, so there is no half-minted result to
    /// inspect, and this row pins that by asserting the token is absent as well.
    /// </para>
    /// <para>
    /// THE ROSTER IS CHECKED FIRST so the row cannot become vacuous. Were a future deployment to adopt
    /// one of these names, the guard fails loudly at the point of the change rather than leaving a row
    /// that asserts a refusal which no longer applies. The comparison is ordinal because that is how the
    /// issuer itself compares - it trims nothing and folds no case - so a row using any other comparison
    /// would be asking a different question than the code answers.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(UnlistedAudiences))]
    public async Task UnlistedAudienceMintsNothingAsync(string audience)
    {
        await using SecurityAppFactory factory = new();

        Assert.DoesNotContain(audience, factory.ResolveSecurityOptions().Audiences, StringComparer.Ordinal);

        TokenIssuanceResult result = factory.Services
            .GetRequiredService<TokenIssuer>()
            .Issue(new TokenIssuanceRequest(RosteredCaller, audience, [GrantedScope]));

        Assert.Equal(TokenIssuanceOutcome.AudienceNotPermitted, result.Outcome);
        Assert.Null(result.Token);
    }

    /// <summary>
    /// Every audience the configured roster carries mints a token addressed to it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE COMPLEMENT OF THE ROW ABOVE, AND ENUMERATED FROM THE HOST rather than from a list written
    /// here. Together the two prove that the ROSTER is the gate: everything on it is admitted and
    /// everything off it is refused, with nothing in this file deciding which is which. A hardcoded list
    /// would only prove that this file and the settings currently agree.
    /// </para>
    /// <para>
    /// A LOOP RATHER THAN A THEORY, because theory data must be static and the roster is only knowable
    /// once a host has booted and validated it. The roster is asserted non-empty first, so an empty one
    /// cannot turn the loop into a silent pass - the validator refuses an empty roster at startup, and
    /// this makes that refusal's effect visible here too.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task EveryConfiguredAudienceIsAdmittedAsync()
    {
        await using SecurityAppFactory factory = new();

        SecurityOptions options = factory.ResolveSecurityOptions();

        Assert.NotEmpty(options.Audiences);

        foreach (string audience in options.Audiences)
        {
            IssuedToken token = factory.IssueToken(
                RosteredCaller,
                audience,
                [GrantedScope]);

            Assert.Equal(audience, Assert.Single(new JsonWebToken(token.AccessToken).Audiences));
        }
    }

    // ==============================================================================================
    //  AREA D - THE MINT-THEN-VERIFY LOOP, CLOSED AGAINST THE PUBLISHED KEY SET.
    // ==============================================================================================

    /// <summary>
    /// A token this host minted verifies against the material this host publishes anonymously.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// THE ASSERTION THAT PROVES SECURITY IS ONE COHERENT SOLE ISSUER RATHER THAN TWO UNRELATED HALVES.
    /// A verifier is handed nothing but the published key set, so a service that minted with one key and
    /// published another would look perfect from either side alone and verify nothing across the two.
    /// </para>
    /// <para>
    /// THE VERIFICATION KEY IS REBUILT FROM THE PUBLISHED MODULUS AND EXPONENT, WHICH IS EXACTLY WHAT A
    /// REAL CONSUMER DOES. Reaching for the private credential to build validation parameters would be
    /// fighting the design - that member is specified never to leave the process - and would prove
    /// nothing about what a consumer can actually verify with. The key set is fetched with the base
    /// client, presenting no credential, because the published material is one of this service's three
    /// anonymous routes; a verifier that needed a token to fetch the key that validates tokens would be
    /// circular.
    /// </para>
    /// <para>
    /// THE CLOCK SEAM IS LEFT AT ITS DEFAULT REAL INSTANT here, deliberately: lifetime validation is
    /// enabled and the clock-skew allowance is zero, so a pinned instant would make the host reject its
    /// own token for a reason that has nothing to do with the signature.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task MintedTokenVerifiesAgainstThePublishedKeySetAsync()
    {
        await using SecurityAppFactory factory = new();

        SecurityOptions options = factory.ResolveSecurityOptions();
        string audience = options.Audiences[0];

        IssuedToken token = factory.IssueToken(RosteredCaller, audience, [GrantedScope]);

        PublishedKeyMaterial published = await ReadPublishedKeyAsync(factory, options.JwksPath);

        // The identifier a verifier selects by must be the one the header carries, otherwise no key can
        // be chosen at all - a failure that looks like a signature problem and is not one.
        Assert.Equal(token.KeyId, published.KeyId);
        Assert.Equal(options.SigningKeyId, published.KeyId);
        Assert.Equal(options.SigningAlgorithm, published.Algorithm);

        using RSA verifier = RSA.Create();

        verifier.ImportParameters(
            new RSAParameters { Modulus = published.Modulus, Exponent = published.Exponent });

        TokenValidationResult result = await new JsonWebTokenHandler().ValidateTokenAsync(
            token.AccessToken,
            new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuers = [options.Issuer],
                ValidateAudience = true,
                ValidAudiences = [audience],
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new RsaSecurityKey(verifier) { KeyId = published.KeyId },
                ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            });

        Assert.True(result.IsValid, result.Exception?.Message);
        Assert.Equal(RosteredCaller, result.ClaimsIdentity.FindFirst("sub")?.Value);
        Assert.Equal(GrantedScope, result.ClaimsIdentity.FindFirst("scope")?.Value);
    }

    /// <summary>
    /// A token this host minted is accepted by this host's own inbound bearer handler.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// THE SAME LOOP CLOSED THROUGH THE STOCK HANDLER INSTEAD OF BY HAND, which is the stronger of the
    /// two statements about interoperability: nothing bespoke participates. The handler discovers the
    /// key, selects it by identifier, checks the signature, and validates the issuer, the audience and
    /// the lifetime - all framework code - so a token it accepts is a token any consumer configured the
    /// same way accepts.
    /// </para>
    /// <para>
    /// THIS ROW ASSERTS ONLY THE POSITIVE DIRECTION. The refusal contract for the authenticated liveness
    /// route belongs to the suites that own that route; duplicating it here would add nothing, and
    /// contradicting it would be worse. What this row adds is that a token from the SOLE MINTER is the
    /// credential that opens it.
    /// </para>
    /// <para>
    /// The client is created through the factory's helper, which mints through the host's real issuer and
    /// addresses the audience the host accepts inbound. It presents a genuine credential: nothing here
    /// disables, replaces or bypasses authentication (C-G).
    /// </para>
    /// </remarks>
    [Fact]
    public async Task MintedTokenIsAcceptedOnTheAuthenticatedRouteAsync()
    {
        await using SecurityAppFactory factory = new();
        using HttpClient client = factory.CreateAuthenticatedClient();

        using HttpResponseMessage response = await client.GetAsync(
            new Uri("/v1/ping", UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ==============================================================================================
    //  AREA E - THE PUBLISHED WIRE SHAPE OF POST /v1/tokens.
    // ==============================================================================================

    /// <summary>
    /// The issuance response carries the token, its lifetime, the granted scope and the issuance
    /// instant, exactly as the published response schema declares them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// REACHED BY CALLING THE HANDLER OVER A CONNECTION CARRYING A CALLER CERTIFICATE, because the
    /// operation authenticates with mutual TLS and the in-process host terminates no TLS. The handler is
    /// `internal` for exactly this purpose by its own stated design. Nothing is relaxed to get here: the
    /// certificate is real, the identity it establishes is reconciled against the claimed subject by the
    /// code under test, and the closing row of this file asserts what happens when no certificate is
    /// presented at all.
    /// </para>
    /// <para>
    /// THE KEY IDENTIFIER IS DELIBERATELY NOT EXPECTED IN THE BODY. The published response schema
    /// declares four required members and one optional one, closes the object against undeclared ones,
    /// and declares no key identifier - so a row demanding one would contradict the contract. The
    /// identifier is public metadata and is asserted where it belongs: in the token's own header and in
    /// the published key set, which the round-trip row above compares against each other.
    /// </para>
    /// <para>
    /// EVERY REPORTED VALUE IS CROSS-CHECKED AGAINST THE TOKEN'S OWN CLAIMS, because the projection
    /// exists precisely so the two cannot disagree: the issuance instant equals the <c>iat</c> claim, the
    /// lifetime equals the difference between <c>exp</c> and <c>iat</c>, and the granted scope equals the
    /// <c>scope</c> claim byte for byte. A caller is told to read the granted set rather than assume its
    /// request was honoured, so that value carrying something other than the claim would be a defect the
    /// caller could not detect.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task IssuanceResponseCarriesThePublishedMembersAsync()
    {
        // The claimed subject must be the identity the certificate establishes: the handler reconciles
        // the two ordinally, and a mismatch is a refusal rather than an issuance.
        const string caller = "powerframework-gateway";
        const string scope = "datawindow.read";

        // THE ISSUER'S PERMISSION MATRIX IS ON THE PATH, so this row declares the one grant it needs. The
        // shipped settings file grants each caller only the audiences it actually calls and only the scopes
        // it actually uses - deliberately, because an unused permission is still a permission - so a row
        // that mints for a different pairing declares that pairing rather than relying on a broad roster.
        // Everything else about the host stays exactly as the composition root configured it.
        await using SecurityAppFactory factory = new()
        {
            ShapeOptions = options =>
            {
                options.Callers.Clear();

                SecurityCallerOptions permitted = new() { Identity = caller };
                SecurityCallerGrantOptions grant = new() { Audience = options.Audiences[0] };

                grant.Scopes.Add(scope);
                permitted.Grants.Add(grant);
                options.Callers.Add(permitted);
            },
        };

        SecurityOptions options = factory.ResolveSecurityOptions();

        // READ BACK FROM THE HOST rather than restated, so the audience the row asks for is by
        // construction the one the grant above was written against.
        string audience = options.Audiences[0];

        using X509Certificate2 certificate = IssuanceFixture.CreateCallerCertificate(caller);

        IResult outcome = TokenEndpoints.IssueToken(
            IssuanceFixture.Body(subject: caller, audience: audience, scopes: [scope]),
            CreateConnection(certificate),
            factory.Services.GetRequiredService<TokenIssuer>(),
            factory.Services.GetRequiredService<ClientCertificateTrust>(),
            factory.Services.GetRequiredService<IssuanceClientRegistry>(),
            factory.Services.GetRequiredService<ILoggerFactory>());

        Ok<TokenIssuanceResponse> issued = Assert.IsType<Ok<TokenIssuanceResponse>>(outcome);

        Assert.NotNull(issued.Value);

        TokenIssuanceResponse body = issued.Value;

        Assert.Equal("Bearer", body.TokenType);
        Assert.NotEmpty(body.AccessToken);
        Assert.Equal(scope, body.Scope);
        Assert.Equal((long)options.TokenLifetime.TotalSeconds, body.ExpiresIn);

        JsonWebToken parsed = new(body.AccessToken);

        long issuedAt = ReadNumericClaim(parsed, "iat");

        Assert.Equal(issuedAt, body.IssuedAt);
        Assert.Equal(body.ExpiresIn, ReadNumericClaim(parsed, "exp") - issuedAt);
        Assert.Equal(body.Scope, ReadClaim(parsed, "scope"));
        Assert.Equal(caller, ReadClaim(parsed, "sub"));
        Assert.Equal(audience, Assert.Single(parsed.Audiences));
    }

    /// <summary>
    /// A malformed request is answered with the service's single problem shape, carrying the legacy
    /// return code.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE SHARED SHAPE IS ASSERTED RATHER THAN A SECOND ONE INVENTED. The published document states
    /// that every non-success response of this service carries one error shape, and the factory that
    /// produces it is owned by the liveness route's file and reused here - so this row reads the member
    /// name from that factory's own constant instead of spelling it, and a rename is a compile-time
    /// change rather than a silently passing row.
    /// </para>
    /// <para>
    /// SHAPE IS ANSWERED BEFORE IDENTITY, AND THIS ROW PROVES IT. The connection carries NO certificate,
    /// yet the answer is the bad request rather than the unauthorized one: a caller with a broken request
    /// is told what is broken rather than being told about its credentials, and a caller with no
    /// credentials learns nothing from its own payload about how this service authenticates. Neither arm
    /// reports a value, so neither leaks the other's information.
    /// </para>
    /// <para>
    /// AN ABSENT BODY IS THE CASE CHOSEN, because the handler accepts an absent body specifically so it
    /// can answer with a problem document carrying a return code - a framework-level refusal would carry
    /// a status and no body at all, which the contract does not permit.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task MalformedRequestCarriesTheSharedProblemShapeAsync()
    {
        await using SecurityAppFactory factory = new();

        IResult outcome = TokenEndpoints.IssueToken(
            request: null,
            CreateConnection(certificate: null),
            factory.Services.GetRequiredService<TokenIssuer>(),
            factory.Services.GetRequiredService<ClientCertificateTrust>(),
            factory.Services.GetRequiredService<IssuanceClientRegistry>(),
            factory.Services.GetRequiredService<ILoggerFactory>());

        ProblemHttpResult problem = Assert.IsType<ProblemHttpResult>(outcome);

        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Equal(MediaTypeNames.Application.ProblemJson, problem.ContentType);

        Assert.True(
            problem.ProblemDetails.Extensions.TryGetValue(
                ProblemResults.RetCodeExtensionMember,
                out object? retCode),
            $"The problem body carries no '{ProblemResults.RetCodeExtensionMember}' member.");

        Assert.Equal(RetCode.E_INVALID_ARGUMENT, Assert.IsType<long>(retCode));
    }

    /// <summary>
    /// An issuance request presenting no credential at all is refused by the boundary itself.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// THE STANDING PROOF THAT THIS FILE DID NOT WEAKEN THE BOUNDARY IT EXERCISES (C-G). Every other row
    /// above reaches the issuance logic without a handshake, which is unavoidable in an in-process host;
    /// this one goes through the real pipeline with the base client, presenting nothing, and asserts the
    /// refusal. Were a future change to disable authentication, exempt this route, or register an
    /// anonymous fallback to make some other row convenient, this row would fail.
    /// </para>
    /// <para>
    /// THE STATUS IS UNAUTHORIZED RATHER THAN FORBIDDEN because no principal was established: the route
    /// carries its own policy requiring a transport credential, and a caller that satisfies neither that
    /// policy nor authentication is challenged rather than refused on permissions. The distinction is one
    /// the published operation declares, and it is asserted here so the two cannot drift.
    /// </para>
    /// <para>
    /// The address is read from the host's own configuration rather than written down, for the same
    /// reason every other deployment value in this file is.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task UnauthenticatedIssuanceRequestIsRefusedAsync()
    {
        await using SecurityAppFactory factory = new();

        string path = factory.ResolveSecurityOptions().TokenEndpointPath;

        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            new Uri(path, UriKind.Relative),
            IssuanceFixture.Body(),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        string payload = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Nothing that could be mistaken for a credential is returned on the refusal path.
        Assert.DoesNotContain("access_token", payload, StringComparison.Ordinal);
    }

    // ==============================================================================================
    //  AREA E2 - THE PUBLISHED clientCredential SCHEME, WHICH WAS DECLARED AND UNREACHABLE
    //
    //  security.v1.yaml declares clientCredential - HTTP Basic - FIRST among this operation's accepted
    //  credentials, the roster implements it in full (fixed-time comparison against a per-instance decoy
    //  on the no-match path), this file's own reader parses it to RFC 7617, and the route's authorization
    //  predicate accepts it. The handler nevertheless read the CERTIFICATE directly and never called the
    //  two-scheme resolver, so `trust.Evaluate(null)` refused a correct Basic credential before its header
    //  was ever looked at: the declared primary scheme was dead code behind a certificate-only gate.
    //
    //  MEASURED CONSEQUENCE, NOT A THEORETICAL ONE. Every deployment topology WITHOUT caller certificates
    //  - a reverse proxy or a mesh sidecar terminating TLS ahead of this service, which is exactly the case
    //  the resolver's own remarks name - could not obtain a single token from the sole issuer, while the
    //  service reported healthy throughout. These rows are what make that unrepeatable.
    // ==============================================================================================

    /// <summary>
    /// A roster credential presented over HTTP Basic issues a token with no certificate at all.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// NO CERTIFICATE IS SUPPLIED, AND THAT IS THE WHOLE ASSERTION. The connection reports none, so a
    /// handler that reached for one would refuse - and this row would fail with the unauthorized problem
    /// instead of a token.
    /// </para>
    /// <para>
    /// THE SUBJECT IS THE ROSTER SUBJECT, unforgeably. The identity the handler reconciles the claimed
    /// subject against is the one the ROSTER carries for the authenticated client, so a caller can only
    /// ever obtain a token whose subject is its own - the same property the certificate scheme has, by the
    /// same reconciliation.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ARosterCredentialPresentedOverBasicIssuesATokenWithNoCertificateAsync()
    {
        await using SecurityAppFactory factory = new();

        // READ BACK FROM THE HOST rather than restated, so this row asserts against the configuration the
        // composition root actually bound - the credential from `Security:Clients` and the permission from
        // the grant matrix, which are two separate declarations and the second of which is the only one
        // that decides.
        (string subject, string audience, string scope) = factory.ResolveFirstGrant();

        IResult outcome = TokenEndpoints.IssueToken(
            IssuanceFixture.Body(subject: subject, audience: audience, scopes: [scope]),
            PresentBasic(subject, SecurityAppFactory.RosterSecret, certificate: null),
            factory.Services.GetRequiredService<TokenIssuer>(),
            factory.Services.GetRequiredService<ClientCertificateTrust>(),
            factory.Services.GetRequiredService<IssuanceClientRegistry>(),
            factory.Services.GetRequiredService<ILoggerFactory>());

        Ok<TokenIssuanceResponse> issued = Assert.IsType<Ok<TokenIssuanceResponse>>(outcome);

        Assert.NotNull(issued.Value);
        Assert.NotEmpty(issued.Value.AccessToken);

        JsonWebToken parsed = new(issued.Value.AccessToken);

        Assert.Equal(subject, ReadClaim(parsed, "sub"));
        Assert.Equal(audience, Assert.Single(parsed.Audiences));
        Assert.Equal(scope, ReadClaim(parsed, "scope"));
    }

    /// <summary>
    /// Every way of failing the credential scheme answers one indistinguishable refusal.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// INDISTINGUISHABILITY IS THE SECURITY PROPERTY, AND IT IS ASSERTED BETWEEN THE CASES RATHER THAN
    /// AGAINST A LITERAL. A wrong secret, an unregistered client, a garbled header and no credential at
    /// all must produce the same status, the same title, the same detail and the same return code -
    /// otherwise the issuance edge is an oracle for enumerating this deployment's client roster, which an
    /// unauthenticated party must not be able to do. Comparing the responses to EACH OTHER is what makes a
    /// future change that distinguishes any one of them fail here.
    /// </remarks>
    [Fact]
    public async Task EveryCredentialFailureAnswersOneIndistinguishableRefusalAsync()
    {
        await using SecurityAppFactory factory = new();

        (string subject, string audience, string scope) = factory.ResolveFirstGrant();

        TokenIssuanceRequestBody body = IssuanceFixture.Body(
            subject: subject,
            audience: audience,
            scopes: [scope]);

        HttpContext[] refused =
        [
            // A registered client, a wrong secret.
            PresentBasic(subject, "not-the-configured-secret", certificate: null),

            // An unregistered client, presenting the real secret of another.
            PresentBasic("a-client-this-deployment-never-registered", SecurityAppFactory.RosterSecret, certificate: null),

            // A header naming the scheme whose payload is not base64 at all.
            PresentRawAuthorization("Basic not-base64-%%%"),

            // A header naming the scheme whose decoded payload carries no colon.
            PresentRawAuthorization(
                "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("no-colon-at-all"))),

            // No credential under either scheme.
            CreateConnection(certificate: null),
        ];

        List<ProblemHttpResult> answers = [];

        foreach (HttpContext context in refused)
        {
            answers.Add(
                Assert.IsType<ProblemHttpResult>(
                    TokenEndpoints.IssueToken(
                        body,
                        context,
                        factory.Services.GetRequiredService<TokenIssuer>(),
                        factory.Services.GetRequiredService<ClientCertificateTrust>(),
                        factory.Services.GetRequiredService<IssuanceClientRegistry>(),
                        factory.Services.GetRequiredService<ILoggerFactory>())));
        }

        ProblemHttpResult first = answers[0];

        Assert.Equal(StatusCodes.Status401Unauthorized, first.StatusCode);
        Assert.Equal(MediaTypeNames.Application.ProblemJson, first.ContentType);

        foreach (ProblemHttpResult answer in answers)
        {
            Assert.Equal(first.StatusCode, answer.StatusCode);
            Assert.Equal(first.ProblemDetails.Title, answer.ProblemDetails.Title);
            Assert.Equal(first.ProblemDetails.Detail, answer.ProblemDetails.Detail);
            Assert.Equal(
                first.ProblemDetails.Extensions[ProblemResults.RetCodeExtensionMember],
                answer.ProblemDetails.Extensions[ProblemResults.RetCodeExtensionMember]);
        }
    }

    /// <summary>
    /// A Basic assertion is answered on its own terms and is never re-authenticated as the certificate.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// THE IDENTITY-SUBSTITUTION GUARD, and it is the reason the schemes are ordered rather than tried
    /// until one works. A caller that presented a WRONG secret must be refused even when a perfectly
    /// trusted certificate sits on the same connection - otherwise a deployment could not tell from the
    /// outside which credential had actually been honoured, and a garbled header would silently become an
    /// identity the caller never asserted.
    /// </para>
    /// <para>
    /// THE MIRROR CASE IS ASSERTED IN THE SAME ROW: the identical certificate, with NO Authorization
    /// header, still issues. So the refusal above is about the header rather than about the certificate
    /// path having been broken by wiring the second scheme in.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ABasicAssertionIsNeverReAuthenticatedAsTheCertificateIdentityAsync()
    {
        await using SecurityAppFactory factory = new();

        (string subject, string audience, string scope) = factory.ResolveFirstGrant();

        TokenIssuanceRequestBody body = IssuanceFixture.Body(
            subject: subject,
            audience: audience,
            scopes: [scope]);

        using X509Certificate2 trusted = IssuanceFixture.CreateCallerCertificate(subject);

        // A WRONG secret, alongside a certificate that WOULD have authenticated on its own.
        IResult substituted = TokenEndpoints.IssueToken(
            body,
            PresentBasic(subject, "not-the-configured-secret", trusted),
            factory.Services.GetRequiredService<TokenIssuer>(),
            factory.Services.GetRequiredService<ClientCertificateTrust>(),
            factory.Services.GetRequiredService<IssuanceClientRegistry>(),
            factory.Services.GetRequiredService<ILoggerFactory>());

        ProblemHttpResult problem = Assert.IsType<ProblemHttpResult>(substituted);

        Assert.Equal(StatusCodes.Status401Unauthorized, problem.StatusCode);

        // THE SAME CERTIFICATE, NO HEADER: still issues, so the certificate path is intact.
        IResult byCertificate = TokenEndpoints.IssueToken(
            body,
            CreateConnection(trusted),
            factory.Services.GetRequiredService<TokenIssuer>(),
            factory.Services.GetRequiredService<ClientCertificateTrust>(),
            factory.Services.GetRequiredService<IssuanceClientRegistry>(),
            factory.Services.GetRequiredService<ILoggerFactory>());

        _ = Assert.IsType<Ok<TokenIssuanceResponse>>(byCertificate);
    }

    /// <summary>Builds a request context presenting one Basic credential, and optionally a certificate.</summary>
    /// <param name="clientId">The user-id half of the credential.</param>
    /// <param name="secret">The password half.</param>
    /// <param name="certificate">A certificate to place on the connection, or none.</param>
    /// <returns>The context to hand to the operation.</returns>
    private static HttpContext PresentBasic(
        string clientId,
        string secret,
        X509Certificate2? certificate)
    {
        HttpContext context = CreateConnection(certificate);

        context.Request.Headers.Authorization = "Basic "
            + Convert.ToBase64String(Encoding.UTF8.GetBytes(clientId + ":" + secret));

        return context;
    }

    /// <summary>Builds a request context carrying one verbatim Authorization header and no certificate.</summary>
    /// <param name="header">The header value, exactly as a caller would send it.</param>
    /// <returns>The context to hand to the operation.</returns>
    private static HttpContext PresentRawAuthorization(string header)
    {
        HttpContext context = CreateConnection(certificate: null);

        context.Request.Headers.Authorization = header;

        return context;
    }

    // ==============================================================================================
    //  AREA F - FAIL-FAST STARTUP. One enumerated refusal per case, and no fallback of any kind.
    // ==============================================================================================

    /// <summary>
    /// With no signing material configured at all, the host refuses to start.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE FAILURE IS A STARTUP FAILURE AND IS ASSERTED THERE. The composition root binds the settings
    /// with validate-on-start and then eagerly resolves both the options contract and the minter before
    /// the pipeline is wired, so a deployment that forgot the one signing input in the whole system
    /// learns about it at bring-up rather than on the first request. A service that started and then
    /// refused every issuance would look correct from the outside for exactly as long as it took to page
    /// someone.
    /// </para>
    /// <para>
    /// THE MESSAGE IS ASSERTED TO NAME THE CONFIGURATION KEY, read from the options type's own constant
    /// rather than re-spelled here, because that spelling is fixed by the orchestration template and the
    /// compose manifest - and because an operator reading a bring-up failure needs the key to set, not a
    /// description of a category of fault.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AbsentSigningMaterialRefusesToStartTheHostAsync()
    {
        await using SecurityAppFactory factory = new() { SigningKeyMaterial = null };

        Assert.Contains(
            SecurityOptions.SigningKeyEnvironmentVariableName,
            CaptureStartupFailure(factory),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Signing material that is present but carries no content refuses to start the host.
    /// </summary>
    /// <param name="material">The empty or whitespace-only value.</param>
    /// <remarks>
    /// <para>
    /// ENUMERATED SEPARATELY FROM THE ABSENT CASE, AND FROM EACH OTHER, because each is a distinct
    /// deployment mistake and because the coverage gate for this service is measured over the signing
    /// path's branches rather than over one representative of them. Whitespace-only is the shape an
    /// environment file produces when a variable is declared and left unfilled, which is the case that
    /// actually happens.
    /// </para>
    /// <para>
    /// ALL OF THEM REACH THE SAME REFUSAL, and that convergence is the point rather than an accident: the
    /// composition root's presence guard treats a blank value as unset rather than assigning it, so a
    /// blank cannot displace material that arrived through another legitimate ingress, and the validator
    /// then refuses the absence. No arm of that path generates, defaults or substitutes anything.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(BlankSigningValues))]
    public async Task BlankSigningMaterialRefusesToStartTheHostAsync(string material)
    {
        await using SecurityAppFactory factory = new() { SigningKeyMaterial = material };

        Assert.Contains(
            SecurityOptions.SigningKeyEnvironmentVariableName,
            CaptureStartupFailure(factory),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Signing material that is present and well formed as text but is not a private key in either
    /// accepted encoding refuses to start the host.
    /// </summary>
    /// <param name="material">The structurally invalid value.</param>
    /// <remarks>
    /// <para>
    /// A DIFFERENT FAULT FROM ABSENCE, WITH A DIFFERENT FIX, AND THEREFORE A DIFFERENT MESSAGE. Absent
    /// material means the variable was never populated; unusable material means it was populated with the
    /// wrong thing. The validator reports the two separately, and this row asserts the second one's fixed
    /// sentence - taken from the validator's own constant so the two cannot drift.
    /// </para>
    /// <para>
    /// THIS IS A STRUCTURAL REFUSAL AND NOT A LENGTH ONE. No key-size floor is asserted anywhere in this
    /// file: the oracle keeps a smaller RSA size a first-class legal value on the surface where a caller
    /// supplies it [ws_objects/pfw.shared.pbl.src/enums.sru:L965], so demanding that a short key be
    /// refused would pin a silent correction of legacy behaviour. These values fail because they are not
    /// a key at all, which is a different statement, and they are obviously synthetic rather than
    /// truncated real material (C-F).
    /// </para>
    /// <para>
    /// THE MESSAGE IS ASSERTED NOT TO ECHO THE VALUE. A diagnostic that reported what was supplied - or a
    /// fragment of it, or even a measurement of it - would put the one secret in the system into a log
    /// line, and a bring-up log is the least private place in a deployment.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(UnusableSigningValues))]
    public async Task StructurallyInvalidSigningMaterialRefusesToStartTheHostAsync(string material)
    {
        await using SecurityAppFactory factory = new() { SigningKeyMaterial = material };

        string failure = CaptureStartupFailure(factory);

        Assert.Contains(
            SecurityOptions.SigningKeyEnvironmentVariableName,
            failure,
            StringComparison.Ordinal);

        Assert.Contains(
            SecurityOptionsValidator.SigningKeyUnusableMessage,
            failure,
            StringComparison.Ordinal);

        Assert.DoesNotContain(material, failure, StringComparison.Ordinal);
    }

    /// <summary>
    /// Material that would serve perfectly well as a symmetric signing key is refused rather than
    /// silently accepted by downgrading the scheme.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE MOST IMPORTANT ROW IN THIS FILE, AND THE ONE MOST LIKELY TO BE NEEDED IN PRACTICE. The
    /// operational instructions tell an operator to generate a signing value with
    /// <c>openssl rand -base64 32</c>, which produces exactly this shape: base64 of thirty-two random
    /// bytes. That is a perfectly good HS256 key and is not an asymmetric key at all. A service that
    /// accepted it by switching to a keyed-hash scheme would start cleanly, mint tokens no consumer could
    /// verify against the published key set, and expose a system in which every service holding the
    /// symmetric secret could mint - which is the sole-issuer topology inverted.
    /// </para>
    /// <para>
    /// THE REFUSAL IS ASSERTED THROUGH THE UNUSABLE-MATERIAL SENTENCE SPECIFICALLY, not merely as "some
    /// exception". That sentence names the two accepted asymmetric encodings and says in so many words
    /// that random bytes are not an asymmetric key, so asserting it is asserting that the material was
    /// rejected FOR BEING SYMMETRIC-SHAPED rather than incidentally.
    /// </para>
    /// <para>
    /// THE VALUE IS GENERATED, NEVER WRITTEN DOWN (C-F), through the factory's own generator - so this row
    /// introduces no literal, is never the same twice, and cannot be mistaken for a real credential.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task SymmetricShapedMaterialIsRefusedRatherThanDowngradedAsync()
    {
        string symmetric = SecurityAppFactory.CreateUnusableSigningMaterial();

        await using SecurityAppFactory factory = new() { SigningKeyMaterial = symmetric };

        string failure = CaptureStartupFailure(factory);

        Assert.Contains(
            SecurityOptionsValidator.SigningKeyUnusableMessage,
            failure,
            StringComparison.Ordinal);

        Assert.DoesNotContain(symmetric, failure, StringComparison.Ordinal);
    }

    /// <summary>
    /// With no signing material configured, no signing chain comes into existence at all - nothing is
    /// generated to stand in for it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE ROW THAT MAKES AN EPHEMERAL-KEY FALLBACK IMPOSSIBLE TO INTRODUCE QUIETLY. Generating a key
    /// when none is configured is the single most tempting change in this service, and it is also the
    /// worst: it trades a loud bring-up failure for a silent fleet-wide authentication outage in which
    /// every token the other three services hold stops verifying, and in which each restart invalidates
    /// the previous generation's tokens as well.
    /// </para>
    /// <para>
    /// THE MECHANISM IS THAT THE HOST NEVER STARTS, and asking for each layer by name is how that is
    /// stated as a property of the layers rather than of one resolution. A fallback could be introduced at
    /// either - the key layer could import a key it made up, or the minter could construct one of its own
    /// - and in both cases the host would start and one of these requests would SUCCEED, failing this row
    /// immediately. That is what a fail-fast test is for: a row that merely tolerated a successful start
    /// would let the behaviour in.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task NoSigningChainIsProducedWhenMaterialIsAbsentAsync()
    {
        await using SecurityAppFactory factory = new() { SigningKeyMaterial = null };

        Assert.ThrowsAny<Exception>(
            () => _ = factory.Services.GetRequiredService<SigningKeyProvider>());

        Assert.ThrowsAny<Exception>(
            () => _ = factory.Services.GetRequiredService<TokenIssuer>());
    }

    /// <summary>
    /// The startup failure names the offending configuration key and contains no fragment of the value
    /// that was supplied under it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// MESSAGE HYGIENE AS ITS OWN ROW, with a distinctive synthetic marker as the supplied value so that
    /// its absence from the diagnostic is a meaningful statement rather than a coincidence of short
    /// strings. A marker that appeared in the text would prove the diagnostic echoes its input, which for
    /// the one signing secret in the system is a disclosure and not a convenience.
    /// </para>
    /// <para>
    /// THE TWO HALVES ARE BOTH NECESSARY AND NEITHER IS SUFFICIENT. Naming the key without withholding
    /// the value would be a leak; withholding the value without naming the key would leave an operator
    /// with a failure and nowhere to look. The validator is specified to do both, and this row is where
    /// that specification is checked.
    /// </para>
    /// <para>
    /// The marker is obviously synthetic and is not a key, a fragment of one, or anything copied from a
    /// hardcoded-secret site in this repository (C-F).
    /// </para>
    /// </remarks>
    [Fact]
    public async Task StartupFailureNamesTheKeyAndNeverEchoesTheMaterialAsync()
    {
        const string marker = "powerframework-synthetic-marker-that-is-not-a-key";

        await using SecurityAppFactory factory = new() { SigningKeyMaterial = marker };

        string failure = CaptureStartupFailure(factory);

        Assert.Contains(
            SecurityOptions.SigningKeyEnvironmentVariableName,
            failure,
            StringComparison.Ordinal);

        Assert.DoesNotContain(marker, failure, StringComparison.Ordinal);
    }

    // ==============================================================================================
    //  AREA G - THE MINTER'S OWN REFUSALS. Every remaining fail-fast branch of the issuance file.
    //
    //  These are the refusals the sole minter makes about its OWN inputs, and they are unreachable
    //  through configuration alone: the options validator refuses a blank issuer, an empty roster and a
    //  non-positive lifetime before the minter is ever constructed, and the signing-key provider
    //  refuses anything but an asymmetric key with a permitted algorithm before it hands a credential
    //  over. Through the production path, therefore, most of the guards below are unreachable and would
    //  sit permanently unexercised - which is precisely why the verification entry point is exposed to
    //  this project by its own stated design. A defence nobody has ever seen fire is a defence nobody
    //  knows works, and C-H puts these branches in this file's share.
    //
    //  NOTHING BELOW WIDENS ANY SURFACE. No production type is modified, no visibility is changed, no
    //  policy is relaxed; each row hands the minter an input the composition root cannot produce and
    //  asserts that it refuses rather than proceeds.
    // ==============================================================================================

    /// <summary>
    /// An issuance request cannot be constructed in a shape the published request schema forbids.
    /// </summary>
    /// <param name="condition">The rule the row violates, carried for row identity.</param>
    /// <param name="scopes">The offending scope set.</param>
    /// <remarks>
    /// <para>
    /// THE REQUEST TYPE IS THE FIRST GATE AND THIS IS WHERE IT IS PROVED. Its whole design is that an
    /// instance is proof the request was well formed, which is what leaves the minter with no malformed
    /// case to handle; a constructor that let one of these through would move a client error into the
    /// issuance path, where it has no declared response.
    /// </para>
    /// <para>
    /// FOUR DISTINCT RULES, NOT ONE. An empty set authorises nothing; a blank scope carries no token; a
    /// scope containing white space could not be recovered from the space-delimited granted value and
    /// would silently become two scopes; and a repeated scope is refused ORDINALLY, because two
    /// spellings differing only by case are two different scopes and folding them would grant one the
    /// caller never asked for. Each is enumerated separately for exactly the reason the fail-fast rows
    /// above are.
    /// </para>
    /// <para>
    /// The parameter name is asserted as well as the type, because it is what tells a caller which of
    /// the three arguments to fix.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("the set is empty", new string[0])]
    [InlineData("a scope is blank", new[] { "datawindow.read", "" })]
    [InlineData("a scope carries white space", new[] { "datawindow read" })]
    [InlineData("a scope repeats", new[] { "datawindow.read", "datawindow.read" })]
    public void MalformedScopeSetCannotBecomeARequest(string condition, string[] scopes)
    {
        Assert.False(string.IsNullOrEmpty(condition));

        ArgumentException failure = Assert.Throws<ArgumentException>(
            () => new TokenIssuanceRequest(RosteredCaller, RosteredCaller, scopes));

        Assert.Equal("scopes", failure.ParamName);
    }

    /// <summary>
    /// Rendering an issued token discloses its public metadata and never the credential itself.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE RENDERING IS THE EASIEST ACCIDENTAL DISCLOSURE IN THE WHOLE SERVICE. Anything that writes a
    /// structured log record, formats an object into a diagnostic, or is inspected in a debugger reaches
    /// this method, so a rendering that printed the token would put a live credential into a log line
    /// with nobody having decided to log it. The override exists so that the inherited behaviour cannot
    /// later be replaced by a generated one that prints every member, and this row is what keeps that
    /// guarantee checked.
    /// </para>
    /// <para>
    /// THE SUBJECT AND THE SCOPE ARE ASSERTED ABSENT TOO, not just the token. Neither is a credential,
    /// but both are caller-supplied, and a rendering that carried them would make an object whose whole
    /// point is being safe to print into one that is merely less dangerous than the alternative.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task IssuedTokenRenderingCarriesNoCredentialAsync()
    {
        await using SecurityAppFactory factory = new();

        SecurityOptions options = factory.ResolveSecurityOptions();

        IssuedToken token = factory.IssueToken(
            RosteredCaller,
            options.Audiences[0],
            [GrantedScope]);

        string rendered = token.ToString();

        // EVERY ASSERTION BELOW IS EVALUATED BEFORE IT IS MADE (C-F). This row's haystack is the rendering
        // whose safety is the subject: if any of the three guards trips, `rendered` contains the very
        // material being guarded, so an overload that printed the haystack would publish the credential at
        // exactly the moment the defect appeared. The booleans carry the whole diagnostic - which guard
        // tripped is the useful fact, and the value adds nothing to it.
        bool carriesTheCredential = SensitiveValueAssertions.Carries(rendered, token.AccessToken);
        bool carriesTheGrantedScope = SensitiveValueAssertions.Carries(rendered, token.GrantedScope);
        bool carriesTheSubject = SensitiveValueAssertions.Carries(rendered, "powerframework-gateway");

        // The key identifier is published anonymously in the JWKS, so it is the one member that MUST be
        // present and the one whose absence is safe to describe. The rendering is still not shown, because
        // the reason this assertion would fail is that the rendering changed - and the changed rendering is
        // the thing that might now carry a credential.
        bool carriesTheKeyIdentifier = SensitiveValueAssertions.Carries(rendered, token.KeyId);

        Assert.True(
            carriesTheKeyIdentifier,
            "The rendering of IssuedToken no longer carries the key identifier, so it has stopped being "
                + "useful as a diagnostic. The rendering itself is deliberately not reproduced here.");

        Assert.False(
            carriesTheCredential,
            "The rendering of IssuedToken carried the access token. That is a live credential reaching "
                + "every log record, diagnostic and debugger view that formats this object.");

        Assert.False(
            carriesTheGrantedScope,
            "The rendering of IssuedToken carried the granted scope, which is caller-supplied.");

        Assert.False(
            carriesTheSubject,
            "The rendering of IssuedToken carried the requested subject, which is caller-supplied.");
    }

    /// <summary>
    /// A credential carrying a shared secret rather than an asymmetric key is refused by the minter.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE COMPANION TO THE SYMMETRIC-SHAPED CONFIGURATION ROW, ONE LAYER DOWN. That row proves the
    /// service will not IMPORT symmetric material; this one proves the minter will not SIGN with a
    /// symmetric credential even if one reached it, so the property holds whichever layer a future
    /// change touches. The refusal is structural and not a strength preference: the verification key set
    /// is published anonymously, so a shared-secret entry there would publish the signing key itself and
    /// make every verifier a co-signer - the sole-issuer topology inverted.
    /// </para>
    /// <para>
    /// THE SECRET IS GENERATED AT RUN TIME (C-F) and the message is asserted not to echo it. That
    /// message deliberately names no configuration key, because no setting can produce this fault - it
    /// can only mean the composition root supplied something other than the signing provider's
    /// credential - so this row asserts the exception rather than a key name.
    /// </para>
    /// </remarks>
    [Fact]
    public void SymmetricCredentialIsRefusedByTheMinter()
    {
        // The identifier AGREES with the one supplied as configured, so the key type is the only fault
        // in the credential - which is what makes this row prove the key-type guard fires FIRST, before
        // the identifier guard that would otherwise be the next thing to look at.
        const string keyId = "a-key-identifier";

        byte[] secret = RandomNumberGenerator.GetBytes(32);

        SigningCredentials credentials = new(
            new SymmetricSecurityKey(secret) { KeyId = keyId },
            SecurityAlgorithms.HmacSha256);

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(
            () => TokenIssuer.VerifyCredential(credentials, keyId));

        // The encoded form is what would appear if the guard echoed the credential, so it is computed once
        // and then used ONLY as a needle and as a redaction input - never as an assertion operand (C-F).
        string encodedSecret = Base64UrlEncoder.Encode(secret);

        bool messageEchoesTheSecret =
            SensitiveValueAssertions.Carries(failure.Message, encodedSecret);

        Assert.Contains(
            "asymmetric",
            SensitiveValueAssertions.Redact(failure.Message, encodedSecret),
            StringComparison.Ordinal);

        Assert.False(
            messageEchoesTheSecret,
            "The minter's refusal echoed the shared secret it was handed. A startup or minting fault "
                + "reaches the operator log, so the message may name the key type and may not reproduce "
                + "the key material.");
    }

    /// <summary>
    /// Every algorithm the options contract permits maps onto the digest member the oracle's own
    /// hash-type catalogue names.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE PARITY CLAIM EXTENDED FROM ONE ALGORITHM TO THE WHOLE PERMITTED SET, and read from the
    /// contract's own published set rather than from a list written here - so an algorithm added to or
    /// removed from that set changes what this row checks. The correspondence is stated independently on
    /// the expected side, using the preserved kernel constants, because a row that derived the
    /// expectation the same way the implementation does would assert nothing.
    /// </para>
    /// <para>
    /// THIS IS NOT A REQUEST FOR A STRONGER ALGORITHM (C-B). Nothing here prefers one member of the set
    /// over another, asserts a minimum, or changes which member a deployment gets: the configured
    /// default remains what the settings declare. The row asserts only that each already-permitted
    /// member is expressible in the oracle's catalogue, which is the property that makes the migration's
    /// signature scheme traceable to the legacy surface it replaces
    /// [ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L70-L73,
    /// ws_objects/pfw.shared.pbl.src/enums.sru:L927-L932].
    /// </para>
    /// </remarks>
    [Fact]
    public async Task EveryPermittedAlgorithmMapsToTheLegacyDigestSelectorAsync()
    {
        Dictionary<string, long> expected = new(StringComparer.Ordinal)
        {
            [SecurityAlgorithms.RsaSha256] = Enums.CRYPTO_HASH_SHA256,
            [SecurityAlgorithms.RsaSha384] = Enums.CRYPTO_HASH_SHA384,
            [SecurityAlgorithms.RsaSha512] = Enums.CRYPTO_HASH_SHA512,
        };

        await using SecurityAppFactory factory = new();

        string keyId = factory.ResolveSecurityOptions().SigningKeyId;
        SecurityKey key = factory.Services.GetRequiredService<SigningKeyProvider>().SigningCredentials.Key;

        // The published set is the subject: every member of it must be covered, and no member outside it
        // may be asserted, so the two collections are compared before any member is exercised.
        Assert.Equal(
            SecurityOptionsValidator.PermittedSigningAlgorithms.Order(StringComparer.Ordinal),
            expected.Keys.Order(StringComparer.Ordinal));

        foreach (string algorithm in SecurityOptionsValidator.PermittedSigningAlgorithms)
        {
            (string verifiedKeyId, long legacyHashType) =
                TokenIssuer.VerifyCredential(new SigningCredentials(key, algorithm), keyId);

            Assert.Equal(keyId, verifiedKeyId);
            Assert.Equal(expected[algorithm], legacyHashType);
        }
    }

    /// <summary>
    /// An algorithm outside the set this issuer produces is refused, and the refusal names the setting
    /// and the permitted set.
    /// </summary>
    /// <param name="algorithm">The unproducible algorithm.</param>
    /// <remarks>
    /// <para>
    /// THE TWO FAMILIES THE REFUSAL MESSAGE ITSELF NAMES ARE THE TWO ROWS HERE. The probabilistic
    /// signature family and the elliptic-curve family are both refused for the same stated reason - the
    /// legacy cryptographic surface declares no identifier for either, so neither is expressible in the
    /// hash-type catalogue the parity row above relies on. A silently accepted third family would mint
    /// tokens whose scheme has no counterpart in the oracle, which is exactly the kind of unrequested
    /// capability the migration forbids.
    /// </para>
    /// <para>
    /// THE KEY IS THE REAL ASYMMETRIC ONE, so the row reaches the algorithm guard rather than stopping
    /// at the key-type guard before it. The diagnostic is asserted to name the algorithm setting and to
    /// enumerate the permitted set, because those two together are what let an operator correct the
    /// configuration without reading the source.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(SecurityAlgorithms.RsaSsaPssSha256)]
    [InlineData(SecurityAlgorithms.EcdsaSha256)]
    public async Task UnproducibleAlgorithmIsRefusedByTheMinterAsync(string algorithm)
    {
        await using SecurityAppFactory factory = new();

        string keyId = factory.ResolveSecurityOptions().SigningKeyId;
        SecurityKey key = factory.Services.GetRequiredService<SigningKeyProvider>().SigningCredentials.Key;

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(
            () => TokenIssuer.VerifyCredential(new SigningCredentials(key, algorithm), keyId));

        Assert.Contains(
            $"{SecurityOptions.SectionName}:{nameof(SecurityOptions.SigningAlgorithm)}",
            failure.Message,
            StringComparison.Ordinal);

        foreach (string permitted in SecurityOptionsValidator.PermittedSigningAlgorithms)
        {
            Assert.Contains(permitted, failure.Message, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// A credential whose key identifier disagrees with the configured one is refused, and so is a blank
    /// configured identifier.
    /// </summary>
    /// <param name="credentialKeyId">The identifier the credential's key carries.</param>
    /// <param name="configuredKeyId">The identifier configuration declares.</param>
    /// <remarks>
    /// <para>
    /// THE AGREEMENT THAT MAKES A TOKEN VERIFIABLE AT ALL, asserted from the refusal side. The
    /// identifier is stamped into every header from the credential and published in the key set from
    /// configuration, and a verifier selects by it - so a disagreement mints tokens naming a key the
    /// published set does not describe, and every verifier fails to select a key. That failure looks
    /// like a signature problem and is not one, which is why it must be refused at construction rather
    /// than diagnosed later.
    /// </para>
    /// <para>
    /// THE BLANK ROW IS NOT REDUNDANT. Two blanks compare equal, so a blank configured identifier would
    /// otherwise pass the comparison and publish a key set with no selectable identifier at all; the
    /// same guard refuses it. The signing-key layer refuses a blank as well, so this restates that
    /// refusal one layer up rather than relaxing it - and the comparison is ordinal and untrimmed, which
    /// is how every other configured value in this service is compared.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("a-key-identifier", "a-different-key-identifier")]
    [InlineData("a-key-identifier", "")]
    [InlineData("a-key-identifier", "   ")]
    public void KeyIdentifierDisagreementIsRefusedByTheMinter(
        string credentialKeyId,
        string configuredKeyId)
    {
        // Both identifiers are synthetic and self-contained: the guard compares the two values it is
        // given, so this row needs no host and asserts no deployment fact. A row that reached for the
        // configured identifier would imply the refusal depended on which value a deployment chose.
        using RSA key = RSA.Create(SecurityAppFactory.DefaultSigningKeySizeInBits);

        SigningCredentials credentials = new(
            new RsaSecurityKey(key) { KeyId = credentialKeyId },
            SecurityAlgorithms.RsaSha256);

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(
            () => TokenIssuer.VerifyCredential(credentials, configuredKeyId));

        Assert.Contains(
            $"{SecurityOptions.SectionName}:{nameof(SecurityOptions.SigningKeyId)}",
            failure.Message,
            StringComparison.Ordinal);

        Assert.DoesNotContain(credentialKeyId, failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Configuration that cannot support issuance stops the minter from being constructed at all.
    /// </summary>
    /// <param name="fault">Which value the row breaks.</param>
    /// <remarks>
    /// <para>
    /// THE MINTER'S CONSTRUCTOR IS ITSELF A GATE, and these are its four remaining refusals. Through the
    /// production path each is unreachable because the options validator refuses the same conditions
    /// first, and that ordering is deliberate - a deployment that got a plain setting wrong should be
    /// told about the setting rather than about the credential. The guards still have to hold, because
    /// they are what makes the minter correct independently of who configured it, so they are exercised
    /// here by handing it an options instance the composition root would never produce.
    /// </para>
    /// <para>
    /// A BLANK ISSUER FAILS EVERYWHERE AND LATER RATHER THAN LOCALLY AND NOW if it is allowed through: it
    /// is stamped into every token and published as this issuer's identity, so three other services then
    /// reject every token with no obvious cause. AN EMPTY ROSTER means an issuer that may address no
    /// audience and can therefore mint nothing usable. A BLANK ROSTER ENTRY is worse than useless -
    /// it could be matched by a request and stamped into a token as though it were an identity. AND A
    /// LIFETIME BELOW ONE WHOLE SECOND cannot be reported at all, because the published response carries
    /// whole seconds and requires at least one; it is neither defaulted nor rounded up to compensate,
    /// which is why the sub-second row is separate from the zero row.
    /// </para>
    /// <para>
    /// EVERY OTHER VALUE IS THE HOST'S OWN, including the signing credential and the key identifier, so
    /// exactly one thing is wrong in each row and the refusal it produces is unambiguous. The clock is
    /// the factory's single double rather than an ambient one, even though the constructor never reads
    /// it, so that no reading of this file suggests a second clock exists.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("blank issuer")]
    [InlineData("whitespace issuer")]
    [InlineData("empty roster")]
    [InlineData("blank roster entry")]
    [InlineData("zero lifetime")]
    [InlineData("sub-second lifetime")]
    public async Task UnusableConfigurationStopsTheMinterFromBeingConstructedAsync(string fault)
    {
        await using SecurityAppFactory factory = new();

        SecurityOptions configured = factory.ResolveSecurityOptions();

        SecurityOptions broken = new()
        {
            Issuer = configured.Issuer,
            SigningKeyId = configured.SigningKeyId,
            SigningAlgorithm = configured.SigningAlgorithm,
            TokenLifetime = configured.TokenLifetime,
        };

        broken.Audiences.Add(configured.Audiences[0]);

        switch (fault)
        {
            case "blank issuer":
                broken.Issuer = string.Empty;
                break;

            case "whitespace issuer":
                broken.Issuer = "   ";
                break;

            case "empty roster":
                broken.Audiences.Clear();
                break;

            case "blank roster entry":
                broken.Audiences.Add("   ");
                break;

            case "zero lifetime":
                broken.TokenLifetime = TimeSpan.Zero;
                break;

            case "sub-second lifetime":
                broken.TokenLifetime = TimeSpan.FromMilliseconds(999);
                break;

            default:
                Assert.Fail($"The row '{fault}' names no fault this test knows how to apply.");
                break;
        }

        SigningKeyProvider keys = factory.Services.GetRequiredService<SigningKeyProvider>();

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(
            () => new TokenIssuer(
                keys,
                Options.Create(broken),
                factory.Services.GetRequiredService<IssuanceClientRegistry>(),
                factory.Clock,
                NullLogger<TokenIssuer>.Instance));

        Assert.Contains(
            $"{SecurityOptions.SectionName}:",
            failure.Message,
            StringComparison.Ordinal);
    }


    // ==============================================================================================
    //  HELPERS. Nothing here asserts a deployment fact; every value is read from the booted host.
    // ==============================================================================================

    /// <summary>
    /// Reads one issuance-roster entry out of the booted host's own registry.
    /// </summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="subject">The registered subject to read.</param>
    /// <returns>The entry.</returns>
    /// <remarks>
    /// READ FROM THE HOST RATHER THAN RESTATED, which is the same discipline every other helper in this
    /// file follows. The audiences and scopes a caller may ask for are a deployment fact declared in the
    /// settings file; a row that spelled them here would be a second copy able to drift from the first,
    /// and it would pass while the deployment was wrong.
    /// </remarks>
    private static RegisteredIssuanceClient ResolveRegisteredClient(
        SecurityAppFactory factory,
        string subject)
    {
        IssuanceClientRegistry registry =
            factory.Services.GetRequiredService<IssuanceClientRegistry>();

        Assert.True(
            registry.TryResolveSubject(subject, out RegisteredIssuanceClient? registered),
            $"The booted host's issuance roster registers no subject '{subject}'.");

        return registered;
    }

    /// <summary>
    /// Reads one textual claim from a parsed token by the name the published contract uses.
    /// </summary>
    /// <param name="token">The parsed token.</param>
    /// <param name="name">The claim name, spelled as the wire spells it.</param>
    /// <returns>The claim's value.</returns>
    /// <remarks>
    /// BY NAME AND NOT THROUGH A CONVENIENCE PROPERTY, deliberately. The library offers accessors that
    /// map several spellings onto one property, and a row that used them would still pass if the claim
    /// had been written under a name no consumer looks for. Failing loudly on an absent claim is part of
    /// the assertion: a missing claim must not read as an empty one.
    /// </remarks>
    private static string ReadClaim(JsonWebToken token, string name)
    {
        Assert.True(
            token.TryGetPayloadValue(name, out string? value),
            $"The token carries no '{name}' claim.");

        Assert.NotNull(value);

        return value;
    }

    /// <summary>
    /// Reads one numeric claim from a parsed token by the name the published contract uses.
    /// </summary>
    /// <param name="token">The parsed token.</param>
    /// <param name="name">The claim name, spelled as the wire spells it.</param>
    /// <returns>The claim's value, in seconds since the Unix epoch.</returns>
    /// <remarks>
    /// The registered time claims are numeric on the wire, and the arithmetic the lifetime rows perform
    /// is the arithmetic a consumer performs, so reading them as numbers rather than through converted
    /// date properties keeps the assertion in the units the contract publishes.
    /// </remarks>
    private static long ReadNumericClaim(JsonWebToken token, string name)
    {
        Assert.True(
            token.TryGetPayloadValue(name, out long value),
            $"The token carries no '{name}' claim.");

        return value;
    }

    /// <summary>
    /// Builds a request context whose connection reports the supplied client certificate, or none.
    /// </summary>
    /// <param name="certificate">The certificate the transport would have carried, or none.</param>
    /// <returns>The context to hand to the operation.</returns>
    /// <remarks>
    /// <para>
    /// THE SEAM THAT STANDS IN FOR A HANDSHAKE, and the only one this file needs. The in-process host
    /// terminates no TLS, so there is no handshake in which a client certificate could be presented; the
    /// transport-security feature is what the connection reads its certificate from, so supplying the
    /// feature is supplying the certificate. The feature type is the one the sibling suite already
    /// declares - reused rather than re-declared, so there is one such seam in this project.
    /// </para>
    /// <para>
    /// THIS IS NOT A WAY AROUND AUTHENTICATION. It substitutes the TRANSPORT and nothing else: no
    /// authentication handler is replaced, no policy is relaxed and no route is exempted, and the code
    /// under test performs its own unconditional certificate check and its own identity reconciliation
    /// against whatever this reports. Passing no certificate is how the refusal arms are reached.
    /// </para>
    /// </remarks>
    private static HttpContext CreateConnection(X509Certificate2? certificate)
    {
        DefaultHttpContext context = new();

        context.Features.Set<ITlsConnectionFeature>(new StubTlsConnectionFeature(certificate));

        return context;
    }

    /// <summary>
    /// Fetches the public verification material this host publishes, anonymously.
    /// </summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="jwksPath">The published address, read from the host's own configuration.</param>
    /// <returns>The single published key's identifier, scheme and public components.</returns>
    /// <remarks>
    /// <para>
    /// PRESENTS NO CREDENTIAL, BECAUSE A REAL CONSUMER CANNOT. The published key set is one of this
    /// service's three anonymous routes precisely because a verifier that needed a token in order to
    /// fetch the key that validates tokens would depend on this service's issuance being already live -
    /// a circular dependency. Fetching it the same way here keeps the round-trip row honest.
    /// </para>
    /// <para>
    /// EXACTLY ONE KEY IS EXPECTED. This service publishes one signing identity, so an enumeration
    /// returning anything else means the publication half changed and the selection a verifier performs
    /// is no longer unambiguous.
    /// </para>
    /// </remarks>
    private static async Task<PublishedKeyMaterial> ReadPublishedKeyAsync(
        SecurityAppFactory factory,
        string jwksPath)
    {
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(
            new Uri(jwksPath, UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string payload = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        using JsonDocument document = JsonDocument.Parse(payload);

        JsonElement key = document.RootElement.GetProperty("keys").EnumerateArray().Single();

        return new PublishedKeyMaterial(
            KeyId: key.GetProperty("kid").GetString() ?? string.Empty,
            Algorithm: key.GetProperty("alg").GetString() ?? string.Empty,
            Modulus: Base64UrlEncoder.DecodeBytes(key.GetProperty("n").GetString()),
            Exponent: Base64UrlEncoder.DecodeBytes(key.GetProperty("e").GetString()));
    }

    /// <summary>
    /// Starts the host, requires that it refuse, and renders every message in the resulting chain.
    /// </summary>
    /// <param name="factory">A factory whose configuration is deliberately unusable.</param>
    /// <returns>The rendered failure text.</returns>
    /// <remarks>
    /// <para>
    /// RESOLVING THE OPTIONS CONTRACT IS WHAT STARTS THE HOST, so both validation passes and the eager
    /// resolution of the signing chain happen inside this call. That is why a deliberately misconfigured
    /// factory refuses here rather than at a later request, and why this helper is the shape every
    /// fail-fast row takes.
    /// </para>
    /// <para>
    /// THE WHOLE CHAIN IS RENDERED RATHER THAN THE OUTERMOST MESSAGE, because the host machinery is free
    /// to wrap a startup fault and the validator reports its failures on the exception it raises. Reading
    /// every message makes the "names the key" assertion robust to that wrapping and makes the "never
    /// echoes the material" assertion STRICTER - it scans more text, so there is more for a leak to be
    /// caught in.
    /// </para>
    /// </remarks>
    private static string CaptureStartupFailure(SecurityAppFactory factory)
    {
        Exception failure = Assert.ThrowsAny<Exception>(() => _ = factory.ResolveSecurityOptions());

        StringBuilder messages = new();

        for (Exception? current = failure; current is not null; current = current.InnerException)
        {
            messages.AppendLine(current.Message);
        }

        string rendered = messages.ToString();

        Assert.NotEqual(string.Empty, rendered.Trim());

        return rendered;
    }

    /// <summary>
    /// The public half of the published signing identity, as a verifier reads it.
    /// </summary>
    /// <param name="KeyId">The identifier a verifier selects this key by.</param>
    /// <param name="Algorithm">The scheme the key signs with.</param>
    /// <param name="Modulus">The public modulus.</param>
    /// <param name="Exponent">The public exponent.</param>
    /// <remarks>
    /// PUBLIC COMPONENTS ONLY, WHICH IS THE WHOLE POINT. There is no member here for a private
    /// component, so a row cannot accidentally reach for one, and the round-trip row is forced to verify
    /// with what a consumer can actually obtain. The values are carried out of the fetch so the caller
    /// owns the lifetime of the cryptographic object built from them.
    /// </remarks>
    private sealed record PublishedKeyMaterial(
        string KeyId,
        string Algorithm,
        byte[] Modulus,
        byte[] Exponent);

}
