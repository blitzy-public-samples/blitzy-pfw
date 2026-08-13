// ==================================================================================================
//  CryptoAuthorizationTests - WHO MAY DRIVE CONTRACT C-02, NOT WHAT C-02 COMPUTES
//  ------------------------------------------------------------------------------------------------
//  WHY THIS FILE EXISTS AS ITS OWN SUITE
//
//  The sibling authorization suite proves that every C-02 address refuses a caller presenting NO
//  credential. That is a necessary assertion and it is not a sufficient one: a surface can refuse the
//  anonymous caller and still admit every authenticated one, which is precisely the posture this
//  service shipped with before the cryptographic group acquired a policy of its own. Any holder of any
//  token minted for this service's audience could drive keyed HMAC, symmetric encryption and
//  decryption, RSA signing and RSA key generation, whatever the credential had been obtained for
//  (CWE-862 missing authorization, CWE-863 incorrect authorization).
//
//  Closing that hole is only half the work. A policy that is never exercised by a test presenting an
//  AUTHENTICATED-BUT-INSUFFICIENT principal is a policy nothing proves is doing anything, and it would
//  keep passing after being silently loosened - the group's RequireAuthorization argument reduced to
//  the default policy, the scope check inverted, the roster lookup made to return true on an
//  unresolvable roster. Every row below therefore presents a VALID token and differs from the admitted
//  case in exactly one dimension.
//
//  THE TWO HALVES OF THE DECISION, AND WHY BOTH ARE ASSERTED SEPARATELY
//  ------------------------------------------------------------------------------------------------
//  The policy admits a caller only when BOTH hold:
//
//    * the credential CARRIES the cryptographic scope, read out of the space-delimited RFC 6749 scope
//      claim rather than compared against it whole; and
//    * the deployment's issuance grant matrix - Security:Callers - would have minted THAT caller THAT
//      scope for THE AUDIENCE the caller is presenting.
//
//  Either half alone leaves a hole. Scope without subject admits any caller the issuer serves as long
//  as it holds the scope; subject without scope lets the permitted caller reach the surface with a
//  credential obtained for something else. So there are rows for each half failing independently, and
//  one row where the two DISAGREE - a credential that still carries the scope after the roster stopped
//  granting it - because that is the only row that can tell the two checks apart.
//
//  WHAT THIS SUITE DELIBERATELY DOES NOT ASSERT
//  ------------------------------------------------------------------------------------------------
//    * CRYPTOGRAPHIC BEHAVIOUR. Every algorithm, mode, padding and preserved weak default belongs to
//      the parity suites. Exactly one operation is driven to success here, and it is chosen to be the
//      one that needs no key reference, no payload and no configured store: the identifier generator.
//      What its 200 proves is that the policy ADMITS, and nothing about what it generated.
//    * THE ANONYMOUS REFUSAL AS A TABLE. The sibling suite owns the complete protected/anonymous
//      partition of this service's surface. One row here presents no credential, and it is present to
//      fix the ORDERING - authentication before authorization, 401 rather than 403 - which is a
//      property of this policy's placement rather than of the partition.
//    * THE ISSUANCE DECISION. Which callers the issuer will mint for, and how it intersects a
//      requested scope set, belongs to the issuance suite. Here the issuer is a CREDENTIAL FORGE: the
//      factory mints exactly what a row asks for, precisely so that the RECEIVER can be handed a
//      credential the deployed roster would never have granted.
//
//  HOW THE ROSTER IS OBTAINED, WHICH IS TWO DIFFERENT WAYS ON PURPOSE
//  ------------------------------------------------------------------------------------------------
//  GROUP 1 runs against the SHIPPED settings file and DERIVES the identities from the options the host
//  actually bound - never restating a deployment fact. Those two rows are the ones that would fail if
//  the deployed grant matrix stopped expressing the C-02 topology at all.
//
//  GROUP 2 SHAPES a roster of its own, so that each dimension row is self-describing and stays true
//  when the deployment's roster changes for an unrelated reason. A row that had to be rewritten every
//  time an operator added a caller would eventually be rewritten wrongly.
//
//  CONSTRAINT COMPLIANCE
//  ------------------------------------------------------------------------------------------------
//  C-G   EVERY NEW BOUNDARY AUTHENTICATED. This file is the standing proof that the widest boundary in
//        this service is AUTHORIZED as well, which is the half of C-G that an authentication-only
//        assertion cannot reach.
//  C-H   80 PER CENT LINE COVERAGE PER SERVICE. This file's named share is the authorization policy at
//        the foot of Endpoints/CryptoEndpoints.cs - both arms of the scope check, all three arms of the
//        roster walk, and the grant-level scope check that no other suite reaches.
//  C-I   EACH SERVICE BUILDS AND TESTS INDEPENDENTLY. Every request below travels the in-memory
//        transport, so no port is bound and a parallel agent cannot fail a run for a reason unrelated
//        to the code under test.
//  .editorconfig  NO PRESERVED-SPELLING IDENTIFIER IS DECLARED HERE; the one return code asserted is
//        CONSUMED from the shared kernel by name.
// ==================================================================================================

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using PowerFramework.Security.Configuration;
using PowerFramework.Security.Endpoints;
using PowerFramework.Security.Tokens;
using PowerFramework.Shared.Kernel;

namespace PowerFramework.Security.Tests;

/// <summary>
/// Proves that the cryptographic surface of contract C-02 admits only the callers the deployment's
/// issuance grant matrix authorises for it, and that it refuses an authenticated caller that the matrix
/// does not.
/// </summary>
public sealed class CryptoAuthorizationTests
{
    /// <summary>
    /// The scope C-02 is served under, written out rather than consumed from the production constant.
    /// </summary>
    /// <remarks>
    /// RESTATED DELIBERATELY, AND RECONCILED BY ITS OWN ROW. Consuming the production constant would make
    /// every row below survive a rename of the scope - which is not a private implementation detail but a
    /// value DataServices requests by name on its own side of the edge, so a rename is a breaking change
    /// to the topology rather than a refactor. The literal here fixes the name; the reconciliation row
    /// proves the production constant still spells it the same way, so the two cannot drift silently.
    /// </remarks>
    private const string CryptographicScope = "security.crypto";

    /// <summary>The unkeyed digest operation - the hash family's representative.</summary>
    private const string HashRoute = "/v1/crypto/hash";

    /// <summary>The keyed digest operation - the keyed-hash family's representative.</summary>
    private const string HmacRoute = "/v1/crypto/hmac";

    /// <summary>The symmetric encryption operation - the symmetric family's representative.</summary>
    private const string SymmetricEncryptRoute = "/v1/crypto/symmetric/encrypt";

    /// <summary>The asymmetric signing operation - the RSA family's representative.</summary>
    private const string RsaSignRoute = "/v1/crypto/rsa/sign";

    /// <summary>
    /// The identifier generator - the random family's representative, and the one operation this suite
    /// drives to success.
    /// </summary>
    /// <remarks>
    /// CHOSEN BECAUSE IT NEEDS NOTHING. It takes no key reference, no payload and no configured store, so
    /// a 200 from it cannot be confused with a statement about key resolution, and a row asserting
    /// admission does not have to provision material it has no interest in.
    /// </remarks>
    private const string RandomGuidRoute = "/v1/crypto/random/guid";

    /// <summary>The text-to-binary conversion - the encoding family's representative.</summary>
    private const string EncodingStringToBlobRoute = "/v1/crypto/encoding/string-to-blob";

    /// <summary>The media type every refusal of this service carries.</summary>
    private const string ProblemMediaType = "application/problem+json";

    /// <summary>The media type a request body is offered under.</summary>
    private const string JsonMediaType = "application/json";

    /// <summary>The status member of the single published problem shape.</summary>
    private const string ProblemStatusMember = "status";

    /// <summary>The single authentication scheme this service declares.</summary>
    private const string BearerScheme = "Bearer";

    /// <summary>
    /// The audience the shaped-roster rows mint for and validate against.
    /// </summary>
    /// <remarks>
    /// NOT ANY SERVICE'S REAL IDENTITY. It is declared as this host's inbound identity AND as its whole
    /// issuance roster, so the shaped rows are complete descriptions of a deployment rather than edits to
    /// the shipped one - which is what lets them stay true when an operator adds a caller for a reason
    /// this suite knows nothing about.
    /// </remarks>
    private const string ShapedAudience = "powerframework-crypto-receiver-tests";

    /// <summary>The permitted identity the shaped-roster rows grant the cryptographic scope to.</summary>
    private const string ShapedPermittedIdentity = "powerframework-crypto-receiver-permitted";

    /// <summary>An identity the shaped roster never lists.</summary>
    private const string ShapedUnlistedIdentity = "powerframework-crypto-receiver-unlisted";

    /// <summary>
    /// A scope set that is well-formed, real and irrelevant to this service.
    /// </summary>
    /// <remarks>
    /// TWO REGISTERED OPENID CONNECT SCOPES, and neither is one this service publishes an operation for.
    /// A non-empty set is required rather than convenient: an HTTP header with an empty value is not
    /// transmitted at all and the minter refuses a blank scope, so "holds no useful scope" has to be
    /// expressed by holding something else.
    /// </remarks>
    private static readonly string[] UnrelatedScopes = ["openid", "profile"];

    /// <summary>The cryptographic scope alone.</summary>
    private static readonly string[] CryptographicScopeOnly = [CryptographicScope];

    /// <summary>
    /// One representative of every declared family of the cryptographic surface.
    /// </summary>
    /// <remarks>
    /// SIX ENTRIES SO THAT NO FAMILY CAN HIDE. The contract's eighteen operations are mapped in family
    /// groups on a single route group, so an operation that lost the requirement would take its family
    /// with it and a table sampling one family would not notice.
    /// </remarks>
    private static readonly string[] FamilyRepresentativeRoutes =
    [
        HashRoute,
        HmacRoute,
        SymmetricEncryptRoute,
        RsaSignRoute,
        RandomGuidRoute,
        EncodingStringToBlobRoute,
    ];

    /// <summary>
    /// Every family representative, projected onto theory rows.
    /// </summary>
    /// <returns>One row per representative address.</returns>
    public static TheoryData<string> FamilyRepresentatives() => [.. FamilyRepresentativeRoutes];

    // ==============================================================================================
    //  GROUP 0 - THE RECONCILIATION
    // ==============================================================================================

    /// <summary>
    /// The scope this suite names is the scope the production policy enforces.
    /// </summary>
    /// <remarks>
    /// The one row that is allowed to consume the production constant, and the reason every other row may
    /// safely use the literal. If the policy's scope is ever renamed, this row fails and names the drift
    /// rather than leaving six refusal rows passing for the wrong reason.
    /// </remarks>
    [Fact]
    public void TheEnforcedScopeIsTheOneThisSuiteNames()
    {
        Assert.Equal(CryptographicScope, CryptoCallerAuthorization.Scope, StringComparer.Ordinal);
    }

    // ==============================================================================================
    //  GROUP 1 - THE DEPLOYED GRANT MATRIX, DERIVED RATHER THAN RESTATED
    // ==============================================================================================

    /// <summary>
    /// The shipped grant matrix admits the caller it grants the cryptographic scope to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE ONLY ROW THAT ASSERTS THE DEPLOYMENT MEANS WHAT THE CONTRACT SAYS. C-02 states who the surface
    /// is for, and the settings file is where that intent becomes operative. Both the identity and the
    /// audience are READ from the options the host bound, so this row states no deployment fact of its own
    /// and keeps working when an operator renames a service identity - while still failing loudly if the
    /// deployment stops granting the cryptographic scope to anybody at all.
    /// </para>
    /// <para>
    /// SUCCESS IS ASSERTED AS 200 RATHER THAN AS "NOT REFUSED", because a not-refused assertion would also
    /// pass on a 500 and on a 404, and both of those would mean the surface was unreachable for a reason
    /// this row exists to rule out.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TheDeployedGrantMatrixAdmitsTheCallerItAuthorises()
    {
        using SecurityAppFactory factory = new();

        string audience = factory.ResolveInboundAudience();
        string permitted = RequirePermittedIdentity(factory.ResolveSecurityOptions(), audience);

        using HttpClient client = factory.CreateAuthenticatedClient(
            permitted,
            audience,
            CryptographicScopeOnly);

        using HttpResponseMessage answered = await PostAsync(client, RandomGuidRoute);

        Assert.Equal(HttpStatusCode.OK, answered.StatusCode);
    }

    /// <summary>
    /// The shipped grant matrix refuses an identity it lists for other audiences only, even when that
    /// identity presents a credential carrying the cryptographic scope.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE ROW THAT PROVES THE MATRIX IS READ PER (CALLER, AUDIENCE) PAIR rather than per caller. A
    /// receiver that asked only "is this identity known to the roster?" would admit every service in the
    /// trust domain, which is a materially weaker property that looks identical in a passing suite.
    /// </para>
    /// <para>
    /// THE CREDENTIAL IS FORGED WITH THE SCOPE THE DEPLOYMENT WOULD NOT HAVE GRANTED IT, which is the
    /// point: the real issuer would have intersected the request down to nothing, so this row hands the
    /// receiver something only a compromised or misconfigured issuer could have produced and requires the
    /// receiver to refuse it on its own.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TheDeployedGrantMatrixRefusesAnIdentityItAuthorisesElsewhereOnly()
    {
        using SecurityAppFactory factory = new();

        string audience = factory.ResolveInboundAudience();
        string elsewhere = RequireIdentityWithoutCryptographicGrant(
            factory.ResolveSecurityOptions(),
            audience);

        using HttpClient client = factory.CreateAuthenticatedClient(
            elsewhere,
            audience,
            CryptographicScopeOnly);

        using HttpResponseMessage refused = await PostAsync(client, RandomGuidRoute);

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);

        await AssertRefusalBodyAsync(refused, HttpStatusCode.Forbidden);
    }

    // ==============================================================================================
    //  GROUP 2 - THE DIMENSIONS, ON AN EXPLICITLY SHAPED GRANT MATRIX
    // ==============================================================================================

    /// <summary>
    /// Every declared family refuses an authenticated caller whose credential does not carry the
    /// cryptographic scope.
    /// </summary>
    /// <param name="route">The family representative.</param>
    /// <remarks>
    /// <para>
    /// 403 AND NOT 401 IS ITSELF THE ASSERTION. The caller is authenticated - its token is signed by this
    /// host, addressed to this host and within its validity window - so a 401 here would mean the
    /// credential was rejected rather than the permission, and the row would be reporting the sibling
    /// suite's property instead of this one.
    /// </para>
    /// <para>
    /// ONE HOST PER ROW, which is the cost of a row that names its own failure. A shared host would be
    /// faster and would also mean one row's failure left the rest reporting a consequence.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(FamilyRepresentatives))]
    public async Task EveryFamilyRefusesAnAuthenticatedCallerHoldingNoCryptographicScope(string route)
    {
        using SecurityAppFactory factory = CreateShapedFactory(CryptographicScopeOnly);

        using HttpClient client = factory.CreateAuthenticatedClient(
            ShapedPermittedIdentity,
            ShapedAudience,
            UnrelatedScopes);

        using HttpResponseMessage refused = await PostAsync(client, route);

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);

        await AssertRefusalBodyAsync(refused, HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// A credential carrying the cryptographic scope in a different case is refused.
    /// </summary>
    /// <remarks>
    /// RFC 6749 SCOPE TOKENS ARE CASE-SENSITIVE, so folding them would admit a spelling the issuer never
    /// mints and the deployment never wrote. This row is the guard on an ordinal comparison that is very
    /// easy to relax to a case-insensitive one while reviewing something else.
    /// </remarks>
    [Fact]
    public async Task ACryptographicScopeDifferingOnlyByCaseIsRefused()
    {
        using SecurityAppFactory factory = CreateShapedFactory(CryptographicScopeOnly);

        using HttpClient client = factory.CreateAuthenticatedClient(
            ShapedPermittedIdentity,
            ShapedAudience,
            [CryptographicScope.ToUpperInvariant()]);

        using HttpResponseMessage refused = await PostAsync(client, RandomGuidRoute);

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
    }

    /// <summary>
    /// A subject differing from the granted identity only by case is refused.
    /// </summary>
    /// <remarks>
    /// A CALLER IDENTITY IS AN OPAQUE PROTOCOL IDENTIFIER, so two spellings differing by case are two
    /// identities. Folding them together would admit a caller the deployment never authorised, and would
    /// do so silently because the folded spelling reads like the authorised one.
    /// </remarks>
    [Fact]
    public async Task ASubjectDifferingOnlyByCaseIsRefused()
    {
        using SecurityAppFactory factory = CreateShapedFactory(CryptographicScopeOnly);

        using HttpClient client = factory.CreateAuthenticatedClient(
            ShapedPermittedIdentity.ToUpperInvariant(),
            ShapedAudience,
            CryptographicScopeOnly);

        using HttpResponseMessage refused = await PostAsync(client, RandomGuidRoute);

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
    }

    /// <summary>
    /// An identity the grant matrix does not list at all is refused even when it holds the cryptographic
    /// scope.
    /// </summary>
    /// <remarks>
    /// The complement of the scope rows: this credential is sufficient in every respect except that no
    /// grant exists for the caller presenting it. Without this row a receiver that checked only the scope
    /// claim would pass the whole suite.
    /// </remarks>
    [Fact]
    public async Task AnUnlistedIdentityIsRefusedEvenHoldingTheCryptographicScope()
    {
        using SecurityAppFactory factory = CreateShapedFactory(CryptographicScopeOnly);

        using HttpClient client = factory.CreateAuthenticatedClient(
            ShapedUnlistedIdentity,
            ShapedAudience,
            CryptographicScopeOnly);

        using HttpResponseMessage refused = await PostAsync(client, RandomGuidRoute);

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
    }

    /// <summary>
    /// The cryptographic scope is recognised inside a space-delimited set rather than only as the whole
    /// claim.
    /// </summary>
    /// <remarks>
    /// THE ROW THAT RULES OUT A CLAIM-EQUALITY REQUIREMENT. RFC 6749 carries the granted set as ONE claim
    /// holding a space-delimited list, so a requirement comparing the claim whole would refuse the very
    /// credential this issuer mints for a caller granted more than one scope - a failure that only appears
    /// once a second scope is granted, long after the policy was reviewed.
    /// </remarks>
    [Fact]
    public async Task TheCryptographicScopeIsRecognisedInsideASpaceDelimitedSet()
    {
        using SecurityAppFactory factory = CreateShapedFactory(
            [UnrelatedScopes[0], CryptographicScope, UnrelatedScopes[1]]);

        using HttpClient client = factory.CreateAuthenticatedClient(
            ShapedPermittedIdentity,
            ShapedAudience,
            [UnrelatedScopes[0], CryptographicScope, UnrelatedScopes[1]]);

        using HttpResponseMessage answered = await PostAsync(client, RandomGuidRoute);

        Assert.Equal(HttpStatusCode.OK, answered.StatusCode);
    }

    /// <summary>
    /// A credential that still carries the cryptographic scope is refused once the grant matrix stops
    /// granting it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE ONE ROW THAT CAN TELL THE TWO CHECKS APART. Everywhere else the claim and the matrix agree, so
    /// a receiver consulting only one of them would pass. Here they disagree: the credential is valid and
    /// carries the scope, and the deployment no longer permits that pairing. Refusing is correct because a
    /// token minted before a grant was narrowed carries what it was granted THEN, while the matrix is what
    /// the deployment means NOW - and the alternative is a window, as long as the token lifetime, in which
    /// a revoked permission keeps working.
    /// </para>
    /// <para>
    /// THE GRANT IS RESHAPED RATHER THAN REMOVED, because a grant listing no scope at all is refused at
    /// startup - correctly, since it would produce credentials that authorise nothing - so the narrowing
    /// is expressed as a grant covering a different scope.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ANarrowedGrantRefusesACredentialThatStillCarriesTheScope()
    {
        using SecurityAppFactory factory = CreateShapedFactory(UnrelatedScopes);

        using HttpClient client = factory.CreateAuthenticatedClient(
            ShapedPermittedIdentity,
            ShapedAudience,
            CryptographicScopeOnly);

        using HttpResponseMessage refused = await PostAsync(client, RandomGuidRoute);

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);

        await AssertRefusalBodyAsync(refused, HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// A caller presenting no credential is refused before the permission is ever considered.
    /// </summary>
    /// <remarks>
    /// FIXES THE ORDERING, WHICH IS THIS POLICY'S PLACEMENT RATHER THAN ITS CONTENT. Authentication runs
    /// before authorization, so an absent credential is a 401 and never a 403; a service that evaluated
    /// the policy first would answer 403 and would tell an unauthenticated caller that its problem was
    /// permission. The sibling suite owns the complete anonymous-versus-protected partition - this row
    /// owns only the boundary between the two refusals on this one surface.
    /// </remarks>
    [Fact]
    public async Task AnAbsentCredentialIsRefusedBeforeThePermissionIsConsidered()
    {
        using SecurityAppFactory factory = CreateShapedFactory(CryptographicScopeOnly);
        using HttpClient anonymous = factory.CreateClient();

        Assert.Null(anonymous.DefaultRequestHeaders.Authorization);

        using HttpResponseMessage refused = await PostAsync(anonymous, RandomGuidRoute);

        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);

        await AssertRefusalBodyAsync(refused, HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// A credential minted for the permitted identity reaches the surface, and the same credential
    /// presented as an unsupported scheme does not.
    /// </summary>
    /// <remarks>
    /// GUARDS AGAINST A POLICY THAT ADMITS ON THE BASIS OF A PRINCIPAL NOBODY ESTABLISHED. The two halves
    /// share one host and one token so that the only difference between them is the scheme name, which is
    /// what makes the pair evidence rather than two unrelated observations.
    /// </remarks>
    [Fact]
    public async Task TheSameCredentialUnderAnUnsupportedSchemeIsRefused()
    {
        using SecurityAppFactory factory = CreateShapedFactory(CryptographicScopeOnly);

        IssuedToken token = factory.IssueToken(
            ShapedPermittedIdentity,
            ShapedAudience,
            CryptographicScopeOnly);

        using HttpClient bearer = factory.CreateClient();
        bearer.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(BearerScheme, token.AccessToken);

        using HttpResponseMessage admitted = await PostAsync(bearer, RandomGuidRoute);

        Assert.Equal(HttpStatusCode.OK, admitted.StatusCode);

        using HttpClient basic = factory.CreateClient();
        basic.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", token.AccessToken);

        using HttpResponseMessage refused = await PostAsync(basic, RandomGuidRoute);

        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
    }

    // ==============================================================================================
    //  HELPERS
    // ==============================================================================================

    /// <summary>
    /// Builds a host whose whole issuance roster, inbound identity and grant matrix are declared here.
    /// </summary>
    /// <param name="grantedScopes">The scopes the single grant covers.</param>
    /// <returns>The configured factory, which the caller owns and disposes.</returns>
    /// <remarks>
    /// <para>
    /// A COMPLETE DEPLOYMENT RATHER THAN AN EDIT TO THE SHIPPED ONE. The audience roster is replaced, the
    /// inbound identity is declared to be its single member, and the grant matrix is cleared before the
    /// one grant is added - so a row's outcome depends on nothing in the settings file except the parts of
    /// it no row touches. The shaping callback runs BEFORE validation, so the startup gate judges exactly
    /// what is assembled here.
    /// </para>
    /// <para>
    /// THE UNLISTED IDENTITY IS NEVER ADDED, which is what makes it unlisted; naming it as a constant
    /// rather than inventing one per row keeps the two roster rows describing the same roster.
    /// </para>
    /// </remarks>
    private static SecurityAppFactory CreateShapedFactory(IEnumerable<string> grantedScopes)
    {
        SecurityAppFactory factory = new()
        {
            InboundAudience = ShapedAudience,
        };

        factory.Audiences.Add(ShapedAudience);

        factory.ShapeOptions = options =>
        {
            options.Callers.Clear();

            SecurityCallerOptions caller = new() { Identity = ShapedPermittedIdentity };
            SecurityCallerGrantOptions grant = new() { Audience = ShapedAudience };

            foreach (string scope in grantedScopes)
            {
                grant.Scopes.Add(scope);
            }

            caller.Grants.Add(grant);
            options.Callers.Add(caller);
        };

        return factory;
    }

    /// <summary>
    /// Sends a request carrying the smallest well-formed body the surface accepts.
    /// </summary>
    /// <param name="client">The client to send through.</param>
    /// <param name="route">The address.</param>
    /// <returns>The response, which the caller owns.</returns>
    /// <remarks>
    /// AN EMPTY JSON OBJECT IS SENT RATHER THAN NO BODY AT ALL, and the reason is the opposite of the
    /// sibling suite's. There, sending nothing proves authorization precedes model binding. Here, the
    /// admitted rows have to get PAST binding to reach a 200, and the refusal rows must not be able to
    /// pass because a body was missing - so every row sends the same body and the only difference between
    /// an admitted row and a refused one is the credential.
    /// </remarks>
    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string route)
    {
        using StringContent body = new("{}", Encoding.UTF8, JsonMediaType);

        return await client.PostAsync(route, body, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Asserts that a refusal carries the single published problem shape, the expected status and the
    /// legacy access-denied return code.
    /// </summary>
    /// <param name="refused">The refusal.</param>
    /// <param name="expected">The status the body must restate.</param>
    /// <remarks>
    /// THE RETURN CODE IS SHARED BY BOTH REFUSALS, and that is the authored contract's own choice rather
    /// than an approximation here: the published document uses the access-denied code for an absent
    /// credential and for an insufficient one alike, so a consumer discriminates on the status and reads
    /// the code as the tie back to the behavioural oracle.
    /// </remarks>
    private static async Task AssertRefusalBodyAsync(
        HttpResponseMessage refused,
        HttpStatusCode expected)
    {
        Assert.Equal(ProblemMediaType, refused.Content.Headers.ContentType?.MediaType);

        string body = await refused.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        using JsonDocument problem = JsonDocument.Parse(body);

        Assert.Equal((int)expected, problem.RootElement.GetProperty(ProblemStatusMember).GetInt32());

        bool carriesReturnCode = problem.RootElement.TryGetProperty(
            ProblemResults.RetCodeExtensionMember,
            out JsonElement retCode);

        Assert.True(
            carriesReturnCode,
            "Every refusal must carry the legacy return code as an extension member of the single "
            + "published problem shape. Its absence means the customization that stamps it was bypassed, "
            + "and a consumer loses the only member tying this failure back to the behavioural oracle.");

        Assert.Equal(RetCode.E_ACCESS_DENIED, retCode.GetInt64());
    }

    /// <summary>
    /// Finds the identity the given grant matrix authorises for the cryptographic scope on the given
    /// audience.
    /// </summary>
    /// <param name="configured">The bound settings.</param>
    /// <param name="audience">The audience this host validates inbound tokens against.</param>
    /// <returns>The authorised identity.</returns>
    /// <remarks>
    /// A MISSING ANSWER IS A FAILURE OF THE DEPLOYMENT, NOT OF THE SEARCH. Contract C-02 states the
    /// surface is served to a caller, so a settings file granting the cryptographic scope to nobody has
    /// made the surface unreachable - which is worth failing loudly for rather than skipping over.
    /// </remarks>
    private static string RequirePermittedIdentity(SecurityOptions configured, string audience)
    {
        foreach ((string caller, string granted, IReadOnlyList<string> scopes)
            in IssuanceFixture.EffectiveGrants(configured))
        {
            if (GrantsCryptographicScope(granted, scopes, audience))
            {
                return caller;
            }
        }

        Assert.Fail(
            "The deployed grant matrix authorises no caller for the cryptographic scope on this "
            + "service's own inbound audience, so contract C-02 is unreachable by every identity the "
            + "issuer serves. Add a grant covering this service's audience and that scope to the "
            + "callers section of the settings file. No configured value is echoed here.");

        return string.Empty;
    }

    /// <summary>
    /// Finds an identity the grant matrix lists but does not authorise for the cryptographic scope on the
    /// given audience.
    /// </summary>
    /// <param name="configured">The bound settings.</param>
    /// <param name="audience">The audience this host validates inbound tokens against.</param>
    /// <returns>The unauthorised identity.</returns>
    /// <remarks>
    /// The topology has at least two callers by construction - the gateway addresses the data service and
    /// the data service addresses this one - so an absent answer means the matrix has collapsed to a
    /// single caller and the per-pair property this row exists to prove can no longer be observed. That is
    /// a failure rather than a skip, for the same reason as above.
    /// </remarks>
    private static string RequireIdentityWithoutCryptographicGrant(
        SecurityOptions configured,
        string audience)
    {
        foreach ((string caller, string granted, IReadOnlyList<string> scopes)
            in IssuanceFixture.EffectiveGrants(configured))
        {
            if (!GrantsCryptographicScope(granted, scopes, audience))
            {
                return caller;
            }
        }

        Assert.Fail(
            "Every caller in the deployed grant matrix is authorised for the cryptographic scope on this "
            + "service's own inbound audience, so there is no identity left to prove the matrix is "
            + "consulted per caller-and-audience pair rather than per caller. Either the matrix has "
            + "collapsed to one caller or a grant has been widened. No configured value is echoed here.");

        return string.Empty;
    }

    /// <summary>
    /// Reports whether one caller holds a grant covering both the given audience and the cryptographic
    /// scope.
    /// </summary>
    /// <param name="grantedAudience">The audience actually granted.</param>
    /// <param name="grantedScopes">The scope set actually granted, which may be narrower than the requested one.</param>
    /// <param name="audience">The audience to match.</param>
    /// <returns><see langword="true"/> when such a grant exists.</returns>
    /// <remarks>
    /// Ordinal throughout, matching the production comparison exactly: a helper that folded case would
    /// select an identity the policy then refused, and the row would fail for a reason that had nothing to
    /// do with the property under test.
    /// </remarks>
    private static bool GrantsCryptographicScope(
        string grantedAudience,
        IReadOnlyList<string> grantedScopes,
        string audience)
    {
        if (!string.Equals(grantedAudience, audience, StringComparison.Ordinal))
        {
            return false;
        }

        foreach (string scope in grantedScopes)
        {
            if (string.Equals(scope, CryptographicScope, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
