// ==================================================================================================
//  TokenIssuancePermissionMatrixTests - WHO MAY ASK FOR WHAT, ON THE SOLE ISSUER
//  ------------------------------------------------------------------------------------------------
//  WHAT THIS FILE GUARDS, STATED AS THE DEFECT IT CLOSES
//
//  Issuance used to check two things and then grant everything: that the caller's certificate common
//  name matched the claimed subject, and that the requested audience was a member of the global
//  audience roster. Every requested scope was then granted verbatim. Since all four service identities
//  are on that roster, ANY caller whose certificate chained to the configured authority could mint
//  itself a token addressed to ANY service in the system, carrying ANY scope set it chose to name - a
//  confused deputy sitting in the middle of the token topology, and the exact opposite of what a sole
//  issuer exists for (CWE-862 missing authorization, CWE-863 incorrect authorization).
//
//  The issuer now consults a PERMISSION MATRIX - `Security:Callers` - with one grant per
//  (caller, audience) pair, and:
//
//    * refuses a pairing the deployment did not grant, with the SAME outcome as an audience this issuer
//      does not serve at all, so the response cannot be used to enumerate the roster; and
//    * grants the INTERSECTION of the requested scope set with that grant's permitted set, reporting
//      the granted value to the caller and stamping the same string into the token's scope claim.
//
//  WHY THE ROSTER IS A MATRIX AND NOT TWO LISTS, WHICH ONE ROW BELOW EXISTS ENTIRELY TO PROVE
//  ------------------------------------------------------------------------------------------------
//  DataServices addresses TWO audiences with entirely different scope sets - Persistence for reading and
//  writing, Security for the cryptographic surface. A flat per-caller scope list would therefore let it
//  carry `security.crypto` inside a Persistence-audience token. That is harmless only for as long as
//  every receiver also checks the audience, and harmful the moment two audiences share a scope name -
//  so the per-pair property is asserted directly rather than left resting on a receiver's diligence.
//
//  WHY THESE ROWS DRIVE THE REAL ISSUER AND NOT THE FIXTURE'S FORGE
//  ------------------------------------------------------------------------------------------------
//  SecurityAppFactory.IssueToken is a CREDENTIAL FORGE: it substitutes a roster admitting exactly what
//  was asked, so that receiver-side suites can be handed arbitrary credentials. Using it here would
//  assert nothing, because the forge's whole purpose is to bypass the decision this file is about. Every
//  row therefore resolves the host's OWN TokenIssuer and calls it directly.
//
//  CONSTRAINT COMPLIANCE
//  ------------------------------------------------------------------------------------------------
//  C-G   EVERY NEW BOUNDARY AUTHENTICATED, and on the issuer that also means AUTHORISED: a boundary
//        that authenticates a caller and then mints it anything it asks for has moved the hole rather
//        than closed it.
//  C-H   80 PER CENT LINE COVERAGE PER SERVICE. Named share: the caller-roster lookup, the scope
//        intersection and the empty-intersection arm of Tokens/TokenIssuer.cs.
//  C-I   EACH SERVICE BUILDS AND TESTS INDEPENDENTLY. Every host is in-memory; no port is bound.
//  NO CREDENTIAL VALUE APPEARS IN THIS FILE. Every value below is an identity, an audience or a scope -
//        all of them names. No key, certificate or token literal appears anywhere.
// ==================================================================================================

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PowerFramework.Security.Configuration;
using PowerFramework.Security.Tokens;

namespace PowerFramework.Security.Tests;

/// <summary>
/// Proves that the sole issuer refuses a (caller, audience) pairing the deployment did not grant, and
/// narrows a requested scope set to what that grant permits.
/// </summary>
public sealed class TokenIssuancePermissionMatrixTests
{
    /// <summary>The caller identity the shaped rosters below grant.</summary>
    private const string PermittedCaller = "powerframework-issuance-matrix-caller";

    /// <summary>A caller identity no shaped roster below lists.</summary>
    private const string UnlistedCaller = "powerframework-issuance-matrix-stranger";

    /// <summary>The audience the shaped rosters grant that caller.</summary>
    private const string GrantedAudience = "powerframework-issuance-matrix-granted";

    /// <summary>
    /// A second audience on the global roster that the shaped rosters do NOT grant that caller.
    /// </summary>
    /// <remarks>
    /// ON THE GLOBAL ROSTER AND OFF THE GRANT MATRIX IS THE WHOLE POINT OF THIS VALUE. It is what
    /// separates "this issuer does not serve that audience" from "this issuer serves it, and not to you" -
    /// the two cases whose indistinguishability one row below asserts.
    /// </remarks>
    private const string UngrantedAudience = "powerframework-issuance-matrix-ungranted";

    /// <summary>An audience on neither the global roster nor any grant.</summary>
    private const string UnlistedAudience = "powerframework-issuance-matrix-unknown";

    /// <summary>The first permitted scope.</summary>
    private const string FirstPermittedScope = "matrix.first";

    /// <summary>The second permitted scope.</summary>
    private const string SecondPermittedScope = "matrix.second";

    /// <summary>A well-formed scope no grant below permits.</summary>
    private const string UnpermittedScope = "matrix.unpermitted";

    /// <summary>The delimiter RFC 6749 separates a granted set with.</summary>
    private const string ScopeDelimiter = " ";

    // ==============================================================================================
    //  GROUP 1 - THE PAIRING IS REFUSED, AND REFUSED INDISTINGUISHABLY
    // ==============================================================================================

    /// <summary>
    /// An audience on the global roster that this caller holds no grant for mints nothing.
    /// </summary>
    /// <remarks>
    /// THE ROW THAT WOULD HAVE CAUGHT THE ORIGINAL DEFECT. Under the old check this request succeeded and
    /// was granted every scope it named, because the audience was on the global roster and the certificate
    /// identity matched the claimed subject. The absence of the token is asserted as well as the outcome,
    /// because a half-minted credential would be worse than a refusal.
    /// </remarks>
    [Fact]
    public void AnAudienceThisCallerHoldsNoGrantForMintsNothing()
    {
        using SecurityAppFactory factory = CreateFactory();

        TokenIssuanceResult refused = Issuer(factory).Issue(
            new TokenIssuanceRequest(PermittedCaller, UngrantedAudience, [FirstPermittedScope]));

        // THE MATRIX GATE, NAMED AS SUCH. The audience IS on the deployment-wide roster - the sibling row
        // asserts that explicitly - so the refusal comes from this caller's own grants, and the outcome
        // that says so is the one an operator's log needs. The RESPONSE cannot tell it from an unserved
        // audience, which is a different property and is pinned where a response exists to read.
        Assert.Equal(TokenIssuanceOutcome.CallerNotPermitted, refused.Outcome);
        Assert.Null(refused.Token);
    }

    /// <summary>
    /// A caller the matrix does not list at all mints nothing, even for a granted audience.
    /// </summary>
    /// <remarks>
    /// The complement of the row above: there the caller was known and the pairing was not, here the
    /// caller is unknown. Both must refuse, and a lookup that fell back to "any listed grant" would pass
    /// one and fail the other.
    /// </remarks>
    [Fact]
    public void ACallerTheMatrixDoesNotListMintsNothing()
    {
        using SecurityAppFactory factory = CreateFactory();

        TokenIssuanceResult refused = Issuer(factory).Issue(
            new TokenIssuanceRequest(UnlistedCaller, GrantedAudience, [FirstPermittedScope]));

        // The audience is granted to SOMEBODY, so the roster gate passes and this is the matrix gate.
        Assert.Equal(TokenIssuanceOutcome.CallerNotPermitted, refused.Outcome);
        Assert.Null(refused.Token);
    }

    /// <summary>
    /// A caller identity is compared ordinally, so a spelling differing only by case is a different
    /// caller.
    /// </summary>
    /// <remarks>
    /// A CALLER IDENTITY IS AN OPAQUE PROTOCOL IDENTIFIER, and the receivers compare it ordinally too. An
    /// issuer that folded case would mint a credential whose subject no receiver would then accept, which
    /// is the worst of the three possible behaviours: it looks like success and fails downstream.
    /// </remarks>
    [Fact]
    public void ACallerIdentityDifferingOnlyByCaseMintsNothing()
    {
        using SecurityAppFactory factory = CreateFactory();

        TokenIssuanceResult refused = Issuer(factory).Issue(
            new TokenIssuanceRequest(
                PermittedCaller.ToUpperInvariant(),
                GrantedAudience,
                [FirstPermittedScope]));

        // A different caller, so the matrix gate refuses - not the roster gate, which the audience passes.
        Assert.Equal(TokenIssuanceOutcome.CallerNotPermitted, refused.Outcome);
        Assert.Null(refused.Token);
    }

    /// <summary>
    /// An ungranted pairing and an entirely unlisted audience are two DISTINCT decisions, both refusing
    /// and neither minting - and the response a caller sees cannot tell them apart.
    /// </summary>
    /// <remarks>
    /// <para>
    /// AN ORACLE IS WORTH CLOSING EVEN WHEN THE THING IT LEAKS IS ONLY A LIST OF NAMES. Two different
    /// RESPONSES here would let a caller holding one certificate map the entire service topology by probing
    /// audience names and reading which refusal came back.
    /// </para>
    /// <para>
    /// WHERE THE SAMENESS BELONGS, AND WHY THIS ROW NO LONGER ASSERTS IT HERE. An earlier revision asserted
    /// the two OUTCOMES equal. The published document settles it the other way: it declares these as case 2
    /// and case 3 of four DISTINCT decisions that deliberately answer the same MESSAGE, and states that the
    /// sameness belongs to the response - "so an operator reading this service's own records can still tell a
    /// roster gap from a matrix gap - one is fixed on the audience roster and the other in the authorization
    /// matrix" [security.v1.yaml, the 403 on POST /v1/tokens]. Collapsing the two outcomes would satisfy a
    /// naive reading of "indistinguishable" while destroying the only diagnostic that sends an operator to
    /// the right setting, and the two misconfigurations it separates are the two most likely ones.
    /// </para>
    /// <para>
    /// SO THIS ROW PINS THE ATTRIBUTION, and the response-level indistinguishability is pinned where a
    /// response exists to read it:
    /// <see cref="CallerAuthorizationResponseTests.AnUnservedAudienceAndAnUnpermittedOneAnswerTheSameBodyAsync"/>.
    /// Split across the two levels, both properties are asserted; asserted at one level only, whichever it
    /// was, the other could regress unobserved.
    /// </para>
    /// </remarks>
    [Fact]
    public void AnUngrantedPairingAndAnUnlistedAudienceAreDistinctDecisionsThatBothRefuse()
    {
        using SecurityAppFactory factory = CreateFactory();
        TokenIssuer issuer = Issuer(factory);

        TokenIssuanceResult ungranted = issuer.Issue(
            new TokenIssuanceRequest(PermittedCaller, UngrantedAudience, [FirstPermittedScope]));
        TokenIssuanceResult unlisted = issuer.Issue(
            new TokenIssuanceRequest(PermittedCaller, UnlistedAudience, [FirstPermittedScope]));

        Assert.Equal(TokenIssuanceOutcome.CallerNotPermitted, ungranted.Outcome);
        Assert.Equal(TokenIssuanceOutcome.AudienceNotPermitted, unlisted.Outcome);

        Assert.NotEqual(ungranted.Outcome, unlisted.Outcome);

        Assert.Null(ungranted.Token);
        Assert.Null(unlisted.Token);
    }

    // ==============================================================================================
    //  GROUP 2 - THE SCOPE SET IS INTERSECTED
    // ==============================================================================================

    /// <summary>
    /// A granted pairing mints a token, and a request for exactly the permitted set is granted whole.
    /// </summary>
    /// <remarks>
    /// THE CONTROL ROW. Every narrowing and refusal row differs from this one in one dimension, so without
    /// it a refusal could not be attributed to the dimension it names.
    /// </remarks>
    [Fact]
    public void AGrantedPairingRequestingExactlyThePermittedSetIsGrantedWhole()
    {
        using SecurityAppFactory factory = CreateFactory();

        TokenIssuanceResult issued = Issuer(factory).Issue(
            new TokenIssuanceRequest(
                PermittedCaller,
                GrantedAudience,
                [FirstPermittedScope, SecondPermittedScope]));

        Assert.Equal(TokenIssuanceOutcome.Issued, issued.Outcome);
        Assert.NotNull(issued.Token);
        Assert.Equal(
            string.Concat(FirstPermittedScope, ScopeDelimiter, SecondPermittedScope),
            issued.Token.GrantedScope,
            StringComparer.Ordinal);
    }

    /// <summary>
    /// A request naming more than the grant permits is narrowed to the overlap.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A NARROWING IS A SUCCESS, NOT AN ERROR, and that is the published contract's own rule rather than a
    /// lenient reading of it: the response schema states the granted set may be narrower than the
    /// requested one and instructs a caller to read it. So the outcome is asserted to be an issuance AND
    /// the granted value is asserted to exclude the unpermitted member.
    /// </para>
    /// <para>
    /// THE REQUESTED ORDER IS PRESERVED in the granted value, which is why the expected string is spelled
    /// out rather than compared as a set. A caller reading the granted set back is entitled to a stable
    /// rendering, and reordering it would make two identical requests produce two different strings.
    /// </para>
    /// </remarks>
    [Fact]
    public void ARequestNamingMoreThanTheGrantPermitsIsNarrowedToTheOverlap()
    {
        using SecurityAppFactory factory = CreateFactory();

        TokenIssuanceResult issued = Issuer(factory).Issue(
            new TokenIssuanceRequest(
                PermittedCaller,
                GrantedAudience,
                [SecondPermittedScope, UnpermittedScope, FirstPermittedScope]));

        Assert.Equal(TokenIssuanceOutcome.Issued, issued.Outcome);
        Assert.NotNull(issued.Token);
        Assert.Equal(
            string.Concat(SecondPermittedScope, ScopeDelimiter, FirstPermittedScope),
            issued.Token.GrantedScope,
            StringComparer.Ordinal);
    }

    /// <summary>
    /// A request naming only unpermitted scopes is refused, and no token is produced.
    /// </summary>
    /// <remarks>
    /// THE PUBLISHED CONTRACT REFUSES HERE RATHER THAN MINTING, and the two rules only read exactly
    /// together. A PARTIALLY permitted set "SUCCEEDS with 200 and the response's scope member reports the
    /// narrower granted set", while the forbidden status "means that NOTHING was permitted, which is
    /// refused only because the granted scope member is required and there would be nothing truthful to
    /// report in it" [security.v1.yaml, the 403 on POST /v1/tokens]. Narrowing is therefore a success and
    /// narrowing to nothing is not, and the boundary between them is exactly the empty granted set. No
    /// enumeration oracle is opened by the refusal, because it is reachable only by a caller already
    /// authorised for this audience - which is the contract's own stated reason for giving this case its
    /// own message.
    /// </remarks>
    [Fact]
    public void ARequestNamingOnlyUnpermittedScopesIsRefused()
    {
        using SecurityAppFactory factory = CreateFactory();

        TokenIssuanceResult issued = Issuer(factory).Issue(
            new TokenIssuanceRequest(PermittedCaller, GrantedAudience, [UnpermittedScope]));

        Assert.Equal(TokenIssuanceOutcome.ScopesNotPermitted, issued.Outcome);
        Assert.Null(issued.Token);
    }

    /// <summary>
    /// A requested scope differing from a permitted one only by case is not granted.
    /// </summary>
    /// <remarks>
    /// RFC 6749 SCOPE TOKENS ARE CASE-SENSITIVE, and every receiver compares them ordinally. An issuer
    /// folding case would grant a spelling no receiver recognises, producing a credential that succeeds at
    /// issuance and fails at use.
    /// </remarks>
    [Fact]
    public void ARequestedScopeDifferingOnlyByCaseIsNotGranted()
    {
        using SecurityAppFactory factory = CreateFactory();

        TokenIssuanceResult issued = Issuer(factory).Issue(
            new TokenIssuanceRequest(
                PermittedCaller,
                GrantedAudience,
                [FirstPermittedScope.ToUpperInvariant()]));

        // AND THE REFUSAL IS THE PROOF THAT NO CASE FOLDING HAPPENED: a case-folding issuer would have
        // granted the differently-spelled name and minted a token here.
        Assert.Equal(TokenIssuanceOutcome.ScopesNotPermitted, issued.Outcome);
        Assert.Null(issued.Token);
    }

    // ==============================================================================================
    //  GROUP 3 - THE MATRIX IS PER PAIR, WHICH IS WHY IT IS A MATRIX
    // ==============================================================================================

    /// <summary>
    /// A scope permitted on one audience is not carried in a token addressed to another, even when the
    /// same caller holds grants for both.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE ROW THIS FILE EXISTS FOR. It is the difference between a per-caller scope list and a
    /// per-(caller, audience) matrix, and the difference is invisible until two audiences share a scope
    /// name - at which point a flat list silently authorises the wrong service. The real topology has
    /// exactly this shape: DataServices addresses Persistence with read and write scopes and Security with
    /// the cryptographic scope, so a flat list would let a Persistence-addressed token carry the
    /// cryptographic one.
    /// </para>
    /// <para>
    /// BOTH DIRECTIONS ARE ASSERTED ON ONE HOST, because a lookup that returned the FIRST grant regardless
    /// of audience would pass whichever direction happened to be listed first and fail the other.
    /// </para>
    /// </remarks>
    [Fact]
    public void AScopePermittedOnOneAudienceIsNotCarriedInATokenForAnother()
    {
        using SecurityAppFactory factory = new()
        {
            InboundAudience = GrantedAudience,
            ShapeOptions = options =>
            {
                options.Audiences.Clear();
                options.Audiences.Add(GrantedAudience);
                options.Audiences.Add(UngrantedAudience);

                options.Callers.Clear();

                SecurityCallerOptions caller = new() { Identity = PermittedCaller };

                SecurityCallerGrantOptions first = new() { Audience = GrantedAudience };
                first.Scopes.Add(FirstPermittedScope);

                SecurityCallerGrantOptions second = new() { Audience = UngrantedAudience };
                second.Scopes.Add(SecondPermittedScope);

                caller.Grants.Add(first);
                caller.Grants.Add(second);
                options.Callers.Add(caller);
            },
        };

        TokenIssuer issuer = Issuer(factory);

        // The scope granted on the SECOND audience, requested against the FIRST.
        TokenIssuanceResult crossed = issuer.Issue(
            new TokenIssuanceRequest(PermittedCaller, GrantedAudience, [SecondPermittedScope]));

        Assert.Equal(TokenIssuanceOutcome.ScopesNotPermitted, crossed.Outcome);
        Assert.Null(crossed.Token);

        // And the reverse crossing, so a first-grant-wins lookup cannot pass by luck.
        TokenIssuanceResult reversed = issuer.Issue(
            new TokenIssuanceRequest(PermittedCaller, UngrantedAudience, [FirstPermittedScope]));

        Assert.Equal(TokenIssuanceOutcome.ScopesNotPermitted, reversed.Outcome);
        Assert.Null(reversed.Token);

        // While each scope IS granted on its own audience, which is what makes the two crossings above
        // statements about the pairing rather than about the scopes being unreachable everywhere.
        TokenIssuanceResult firstOnFirst = issuer.Issue(
            new TokenIssuanceRequest(PermittedCaller, GrantedAudience, [FirstPermittedScope]));
        TokenIssuanceResult secondOnSecond = issuer.Issue(
            new TokenIssuanceRequest(PermittedCaller, UngrantedAudience, [SecondPermittedScope]));

        Assert.NotNull(firstOnFirst.Token);
        Assert.NotNull(secondOnSecond.Token);
        Assert.Equal(FirstPermittedScope, firstOnFirst.Token.GrantedScope, StringComparer.Ordinal);
        Assert.Equal(SecondPermittedScope, secondOnSecond.Token.GrantedScope, StringComparer.Ordinal);
    }

    // ==============================================================================================
    //  GROUP 4 - THE DEPLOYED MATRIX IS THE EVIDENCED CALL GRAPH AND NOTHING WIDER
    // ==============================================================================================

    /// <summary>
    /// The shipped matrix grants no caller an audience the evidenced call graph does not contain.
    /// </summary>
    /// <remarks>
    /// <para>
    /// READ FROM THE SETTINGS FILE, NOT RESTATED. The assertion is a PROPERTY of the shipped matrix rather
    /// than a copy of it: every grant must name an audience on the global roster, and no caller may grant
    /// itself its own audience. The first is the validator's own rule, checked here from the outside so a
    /// validator regression is visible; the second is a topology fact - the layered, acyclic call graph has
    /// no self-edge, and a self-grant would let a service mint itself a credential for its own receivers.
    /// </para>
    /// <para>
    /// READ FROM THE FILE AND NOT FROM A BOOTED HOST, WHICH IS THE ONE THING THIS ROW CANNOT DO. Every host
    /// this suite builds installs the harness's own grants for the identities the deployment does not name,
    /// so a booted host's matrix is partly the harness's by construction - and a row asserting a property
    /// of the SHIPPED matrix would then be asserting it of a mixture. The file is read through the same
    /// configuration stack the service uses, from the copy the build places beside this assembly, so the
    /// subject is still the shipped artifact rather than a literal restated here.
    /// </para>
    /// <para>
    /// BOTH AUTHORING SHAPES ARE PROJECTED, because the issuer enforces their union: the assertion must
    /// hold of every grant the file states, in whichever shape it states it.
    /// </para>
    /// <para>
    /// WHAT THIS ROW DELIBERATELY DOES NOT DO IS ENUMERATE THE THREE GRANTS. A row listing them would fail
    /// every time an operator legitimately added a caller, and would be edited rather than considered. The
    /// invariants above hold for any correct deployment.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheShippedMatrixGrantsOnlyAudiencesOnTheGlobalRosterAndNoSelfEdge()
    {
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: false)
            .Build();

        SecurityOptions configured = new();

        configuration.GetSection(SecurityOptions.SectionName).Bind(configured);

        (string Caller, string Audience, IReadOnlyList<string> Scopes)[] shipped =
            [.. IssuanceFixture.EffectiveGrants(configured)];

        Assert.NotEmpty(shipped);

        foreach ((string identity, string audience, IReadOnlyList<string> scopes) in shipped)
        {
            Assert.Contains(audience, configured.Audiences, StringComparer.Ordinal);

            Assert.NotEqual(identity, audience, StringComparer.Ordinal);

            Assert.NotEmpty(scopes);
        }
    }

    // ==============================================================================================
    //  HELPERS
    // ==============================================================================================

    /// <summary>
    /// Builds a host whose global audience roster and grant matrix are both declared here.
    /// </summary>
    /// <returns>The configured factory, which the caller owns and disposes.</returns>
    /// <remarks>
    /// A COMPLETE DEPLOYMENT RATHER THAN AN EDIT TO THE SHIPPED ONE, so that a row's outcome depends on
    /// nothing in the settings file. Two audiences are declared and only ONE is granted, which is what
    /// makes the ungranted-pairing rows distinguishable from unlisted-audience rows at all. The shaping
    /// callback runs before validation, so the startup gate judges exactly what is assembled here.
    /// </remarks>
    private static SecurityAppFactory CreateFactory()
    {
        SecurityAppFactory factory = new() { InboundAudience = GrantedAudience };

        factory.ShapeOptions = options =>
        {
            options.Audiences.Clear();
            options.Audiences.Add(GrantedAudience);
            options.Audiences.Add(UngrantedAudience);

            options.Callers.Clear();

            SecurityCallerOptions caller = new() { Identity = PermittedCaller };
            SecurityCallerGrantOptions grant = new() { Audience = GrantedAudience };

            grant.Scopes.Add(FirstPermittedScope);
            grant.Scopes.Add(SecondPermittedScope);

            caller.Grants.Add(grant);
            options.Callers.Add(caller);
        };

        return factory;
    }

    /// <summary>
    /// Resolves the host's own minter.
    /// </summary>
    /// <param name="factory">The booted host.</param>
    /// <returns>The production issuer, over the production signing chain and clock.</returns>
    /// <remarks>
    /// THE PRODUCTION TYPE OVER THE PRODUCTION REGISTRATIONS. Resolving it is what makes these rows
    /// statements about the deployed decision rather than about a hand-assembled one - and it is
    /// specifically NOT the fixture's credential forge, whose entire purpose is to bypass this decision so
    /// that receiver-side suites can be handed arbitrary credentials.
    /// </remarks>
    private static TokenIssuer Issuer(SecurityAppFactory factory) =>
        factory.Services.GetRequiredService<TokenIssuer>();
}
