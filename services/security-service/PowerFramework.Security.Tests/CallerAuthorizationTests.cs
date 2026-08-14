// ==================================================================================================
//  CallerAuthorizationTests.cs - WHO MAY ADDRESS WHOM, AND WITH WHAT, IS A BEHAVIOUR
//  ------------------------------------------------------------------------------------------------
//  WHAT THESE ROWS PROTECT
//  Before the matrix these rows exercise existed, authentication was the WHOLE of authorization on
//  POST /v1/tokens: any caller that established itself at the transport - one client certificate, any
//  common name the configured authority would sign - could obtain a token for EVERY audience on the
//  configured roster carrying ANY syntactically valid scope it cared to name. Two consequences, both
//  observable: the scope claim was decorative, because nothing ever compared a requested scope against
//  a permitted one; and a certificate issued for one caller was, in effect, issued for all of them,
//  because the audience it could request was unconstrained. The audience roster answered "does this
//  issuer serve that audience at all", which is a different question from "may THIS caller have it".
//
//  THE THREE LEVELS THESE ROWS WORK AT, AND WHY EACH IS NEEDED
//    1. THE OPTIONS VALIDATOR, driven directly. A configuration rule that is decidable from the values
//       alone belongs to the validator, and driving it directly is what proves a bad matrix fails a
//       BRING-UP rather than a request. A host that started on a contradictory matrix would refuse
//       callers for a reason no operator could see.
//    2. THE ISSUER, driven through a booted host's own minter. This is where the decision is made and
//       where the three outcomes are distinguishable. It is also the only level at which the ORDER of
//       the gates is observable, and the order matters: the roster is consulted before the matrix, so
//       an audience this deployment does not serve is reported as such rather than as a permission
//       failure that would send an operator to edit the wrong setting.
//    3. THE HTTP SURFACE, through a real request carrying a real client certificate. The published
//       document is authoritative for anything on the wire, and two of its properties are only
//       observable here: that a caller CANNOT tell an unserved audience from an unpermitted one, and
//       that a partially permitted scope set is a 200 rather than a refusal.
//
//  THE ONE PROPERTY THAT IS EASY TO GET BACKWARDS
//  A NARROWING IS A SUCCESS. security.v1.yaml states that the granted set "may be narrower" than the
//  requested one and that a caller must read the response rather than assume its request was honoured
//  in full. So a caller asking for one permitted scope and one unpermitted scope is ISSUED a token
//  carrying the permitted one - it is not refused, and the unpermitted name is not echoed back. Only an
//  EMPTY intersection refuses, and it refuses for a narrow reason: the response's `scope` member is
//  required, so there would be nothing truthful to report. Several rows below exist purely to pin that
//  asymmetry, because an implementation that refused every narrowing would satisfy a naive reading of
//  "enforce scopes" while contradicting the document this service publishes.
//
//  WHAT IS DELIBERATELY ABSENT
//  No row asserts that a refusal names the caller, the audience, the requested scope set or the
//  permitted set, and several assert the opposite. A refusal that enumerated any of those would let an
//  authenticated caller map the deployment's authorization configuration one request at a time. The
//  distinction an OPERATOR needs is kept in the issuer's log records, and the rows in Area C assert
//  that those three records are distinguishable from each other while the two responses are not.
//
//  NO CREDENTIAL LITERAL EXISTS IN THIS FILE. Every signing key and every certificate comes from the
//  shared fixture, which generates fresh material per run.
//
//  RULES
//  review_rules reports that no user rules were provided, so no user-specified rule governs this file.
//  The bar applied instead is the enterprise-standard baseline the migration plan states.
// ==================================================================================================

using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Net.Mime;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

using Microsoft.AspNetCore.Mvc.Testing;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using PowerFramework.Security.Configuration;
using PowerFramework.Security.Endpoints;
using PowerFramework.Security.Tokens;

namespace PowerFramework.Security.Tests;

// ==================================================================================================
//  AREA A - THE OPTIONS VALIDATOR. A CONTRADICTORY MATRIX FAILS THE BRING-UP.
// ==================================================================================================

/// <summary>
/// Every rule the authorization matrix must satisfy is decided at startup, from the values alone.
/// </summary>
/// <remarks>
/// Driven against the validator directly rather than through a booted host, because that is the layer
/// that owns a value-decidable rule and because a host is not needed to decide any of them. Each row
/// also asserts that the failure names its configuration key and echoes no configured value.
/// </remarks>
public sealed class CallerAuthorizationValidationTests
{
    /// <summary>The configuration key every failure in this area names.</summary>
    private const string MatrixKey = SecurityOptions.SectionName + ":CallerAuthorizations";

    /// <summary>An identity no row below intends the failure message to repeat.</summary>
    private const string DistinctiveCaller = "a-caller-whose-name-must-not-be-echoed";

    /// <summary>A scope no row below intends the failure message to repeat.</summary>
    private const string DistinctiveScope = "a scope with spaces that must not be echoed";

    /// <summary>
    /// THE POSITIVE ARM, FIRST. A matrix whose rows are all well formed raises no failure.
    /// </summary>
    /// <remarks>
    /// Without this row every other row in this class would pass against a validator that refused every
    /// matrix, and "fail-closed" would be indistinguishable from "unstartable".
    /// </remarks>
    [Fact]
    public void AWellFormedMatrixRaisesNoFailure()
    {
        SecurityOptions options = Bootable();
        options.CallerAuthorizations.Add(Row("powerframework-gateway", "powerframework-gateway", "s"));

        ValidateOptionsResult result = new SecurityOptionsValidator().Validate(name: null, options);

        Assert.True(result.Succeeded);
    }

    /// <summary>
    /// AN EMPTY MATRIX RAISES NO FAILURE EITHER, AND THAT IS THE POINT OF THE POSTURE.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An empty matrix authorises nothing, which is a coherent and occasionally wanted state: a host that
    /// serves the key set, the discovery document, health and the whole of C-02 while issuing no token.
    /// Refusing to start on it would make every such host unstartable - including a unit-test host and a
    /// local bring-up of the other three services against an issuer that mints nothing.
    /// </para>
    /// <para>
    /// The row is paired with <see cref="CallerAuthorizationGateTests.AnEmptyMatrixMintsNothingForAnyone"/>,
    /// which asserts the other half: that accepting the configuration is not the same as granting
    /// anything. Either row alone would be misread - this one as leniency, that one as a startup failure.
    /// </para>
    /// </remarks>
    [Fact]
    public void AnEmptyMatrixRaisesNoFailure()
    {
        SecurityOptions options = Bootable();

        Assert.Empty(options.CallerAuthorizations);

        ValidateOptionsResult result = new SecurityOptionsValidator().Validate(name: null, options);

        Assert.True(result.Succeeded);
    }

    /// <summary>A row naming no caller is refused, and the failure names the caller key.</summary>
    /// <remarks>
    /// A row authorising no caller authorises nothing while reading, to an operator scanning the file, as
    /// a grant. Accepting it would produce a matrix whose apparent size and effective size differ.
    /// </remarks>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ARowWithNoCallerIsRefused(string caller)
    {
        SecurityOptions options = Bootable();
        options.CallerAuthorizations.Add(Row(caller, "powerframework-gateway", "s"));

        AssertRefusedNaming(options, MatrixKey + "[0]:Caller");
    }

    /// <summary>A row naming no audience is refused, and the failure names the audience key.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ARowWithNoAudienceIsRefused(string audience)
    {
        SecurityOptions options = Bootable();
        options.CallerAuthorizations.Add(Row("powerframework-gateway", audience, "s"));

        AssertRefusedNaming(options, MatrixKey + "[0]:Audience");
    }

    /// <summary>
    /// A row naming an audience the roster does not carry is refused as a configuration contradiction.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The roster is what this issuer will mint for AT ALL, so a row authorising an audience absent from
    /// it grants a combination no request could ever be granted. Left unrefused it is worse than
    /// harmless: it reads as a grant that exists, so an operator diagnosing a refused caller would find
    /// the permission apparently present and look elsewhere.
    /// </para>
    /// <para>
    /// The row asserts the audience is genuinely off the roster before asserting the refusal, so a future
    /// roster that adopted this name fails the guard loudly instead of turning the row into a vacuous
    /// pass.
    /// </para>
    /// </remarks>
    [Fact]
    public void ARowNamingAnUnlistedAudienceIsRefused()
    {
        SecurityOptions options = Bootable();

        Assert.DoesNotContain(IssuanceFixture.UnlistedAudience, options.Audiences, StringComparer.Ordinal);

        options.CallerAuthorizations.Add(
            Row("powerframework-gateway", IssuanceFixture.UnlistedAudience, "s"));

        AssertRefusedNaming(options, MatrixKey + "[0]:Audience");
    }

    /// <summary>A row permitting no scope is refused, and the failure names the scope collection.</summary>
    /// <remarks>
    /// Indistinguishable in effect from an absent row - it refuses every request it appears to cover -
    /// so accepting it would let a configuration state a grant and deliver a refusal.
    /// </remarks>
    [Fact]
    public void ARowPermittingNoScopeIsRefused()
    {
        SecurityOptions options = Bootable();
        options.CallerAuthorizations.Add(
            new CallerAuthorizationOptions
            {
                Caller = "powerframework-gateway",
                Audience = "powerframework-gateway",
            });

        AssertRefusedNaming(options, MatrixKey + "[0]:Scopes");
    }

    /// <summary>
    /// A scope that is empty or carries white space of any kind is refused, naming its indexed key.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE SCOPE CLAIM IS SPACE DELIMITED, so a permitted scope containing a space would be granted as
    /// one entry and read back by a verifier as two - a silent widening produced by punctuation. Every
    /// white-space form is covered rather than just the space, because a tab or a newline inside a
    /// configured value survives a settings file and an environment variable intact.
    /// </para>
    /// <para>
    /// The indexed key is asserted in full - collection index and scope index both - because a matrix
    /// with several rows and several scopes each is exactly where a message naming only the setting would
    /// leave an operator to search.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("has a space")]
    [InlineData("has\ttab")]
    [InlineData("has\nnewline")]
    public void AnUnusableScopeIsRefused(string scope)
    {
        SecurityOptions options = Bootable();

        CallerAuthorizationOptions row = Row("powerframework-gateway", "powerframework-gateway", "fine");
        row.Scopes.Add(scope);

        options.CallerAuthorizations.Add(row);

        AssertRefusedNaming(options, MatrixKey + "[0]:Scopes[1]");
    }

    /// <summary>
    /// Two rows declaring one caller-and-audience pair are refused rather than merged or last-wins.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A PERMISSION MODEL WHOSE ANSWER DEPENDS ON ORDERING IS NOT A PERMISSION MODEL. Both plausible
    /// tolerant behaviours are wrong in the same direction: merging silently WIDENS the grant to the
    /// union, and taking the last silently discards a grant an operator wrote. Refusing is the only
    /// resolution that cannot surprise.
    /// </para>
    /// <para>
    /// The two rows carry DIFFERENT scope sets, so a merging implementation would produce an observably
    /// different grant rather than an identical one - which is what makes this row able to fail.
    /// </para>
    /// </remarks>
    [Fact]
    public void ADuplicateCallerAndAudiencePairIsRefused()
    {
        SecurityOptions options = Bootable();
        options.CallerAuthorizations.Add(Row("powerframework-gateway", "powerframework-gateway", "one"));
        options.CallerAuthorizations.Add(Row("powerframework-gateway", "powerframework-gateway", "two"));

        AssertRefusedNaming(options, MatrixKey + "[1]");
    }

    /// <summary>
    /// The same caller with a DIFFERENT audience is not a duplicate, and the same audience with a
    /// different caller is not either.
    /// </summary>
    /// <remarks>
    /// The complement of the row above, and it is what proves the key is the PAIR rather than either
    /// member. Without it a validator that refused a repeated caller outright would pass the duplicate
    /// row while making the ordinary multi-audience configuration - which the shipped settings file
    /// itself uses, DataServices addressing both Persistence and Security - unstartable.
    /// </remarks>
    [Fact]
    public void OneMemberRepeatingAcrossRowsIsNotADuplicate()
    {
        SecurityOptions options = Bootable();
        options.Audiences.Add("powerframework-dataservices");

        options.CallerAuthorizations.Add(Row("caller-a", "powerframework-gateway", "one"));
        options.CallerAuthorizations.Add(Row("caller-a", "powerframework-dataservices", "two"));
        options.CallerAuthorizations.Add(Row("caller-b", "powerframework-gateway", "three"));

        ValidateOptionsResult result = new SecurityOptionsValidator().Validate(name: null, options);

        Assert.True(result.Succeeded);
    }

    /// <summary>
    /// A pair that would collide only under a joined key is accepted, because the key is a pair.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE REGRESSION GUARD FOR A REAL DESIGN CHOICE. An implementation keying the matrix on
    /// caller-plus-separator-plus-audience must pick a separator no identity may contain, and there is no
    /// such character: every printable one is legal inside a service identity. The two rows below collide
    /// under a colon separator and under a hyphen separator, and are plainly distinct permissions - so a
    /// validator or issuer that accepted them here and refused them after a separator was introduced
    /// would be caught by this row.
    /// </para>
    /// <para>
    /// Asserted at BOTH levels, because the pair key exists twice - once in the validator's duplicate set
    /// and once in the issuer's frozen matrix - and a separator reintroduced in either place is a defect.
    /// </para>
    /// </remarks>
    [Fact]
    public void PairsThatWouldCollideUnderAJoinedKeyAreDistinct()
    {
        SecurityOptions options = Bootable();
        options.Audiences.Clear();
        options.Audiences.Add("b:c");
        options.Audiences.Add("c");

        // The roster follows the audience list, or the host fails on a rule this row is not about.
        Reroster(options);

        options.CallerAuthorizations.Add(Row("a", "b:c", "one"));
        options.CallerAuthorizations.Add(Row("a:b", "c", "two"));

        Assert.True(new SecurityOptionsValidator().Validate(name: null, options).Succeeded);

        // And the issuer freezes them as two entries rather than refusing a phantom duplicate.
        using RSA key = RSA.Create(SecurityAppFactory.DefaultSigningKeySizeInBits);
        options.SigningKey = key.ExportPkcs8PrivateKeyPem();

        TokenIssuer issuer = new(
            new SigningKeyProvider(Options.Create(options)),
            Options.Create(options),
            EmptyIssuanceRoster(options),
            TimeProvider.System,
            NullLogger<TokenIssuer>.Instance);

        Assert.Equal(
            TokenIssuanceOutcome.Issued,
            issuer.Issue(new TokenIssuanceRequest("a", "b:c", ["one"])).Outcome);
        Assert.Equal(
            TokenIssuanceOutcome.Issued,
            issuer.Issue(new TokenIssuanceRequest("a:b", "c", ["two"])).Outcome);

        // ... and neither row's permission leaks into the other's, which is the property a collision
        // would destroy while leaving both requests apparently working.
        Assert.Equal(
            TokenIssuanceOutcome.ScopesNotPermitted,
            issuer.Issue(new TokenIssuanceRequest("a", "b:c", ["two"])).Outcome);
    }

    /// <summary>No failure in this area echoes a configured caller, audience or scope.</summary>
    /// <remarks>
    /// <para>
    /// The discipline the whole service applies to failure messages: name the configuration key, never
    /// the configured value. It matters more here than for most settings, because a matrix's values are
    /// the identities of the system's own callers and a startup failure is written to whatever collects
    /// the container's output.
    /// </para>
    /// <para>
    /// The values chosen are DISTINCTIVE rather than realistic, so that a message which did echo one
    /// could not pass by coincidental overlap with the key name or with another row's value.
    /// </para>
    /// </remarks>
    [Fact]
    public void NoFailureEchoesAConfiguredValue()
    {
        SecurityOptions options = Bootable();

        CallerAuthorizationOptions row = Row(DistinctiveCaller, IssuanceFixture.UnlistedAudience, "fine");
        row.Scopes.Add(DistinctiveScope);

        options.CallerAuthorizations.Add(row);

        ValidateOptionsResult result = new SecurityOptionsValidator().Validate(name: null, options);

        Assert.True(result.Failed);
        Assert.NotNull(result.Failures);

        foreach (string failure in result.Failures)
        {
            Assert.DoesNotContain(DistinctiveCaller, failure, StringComparison.Ordinal);
            Assert.DoesNotContain(DistinctiveScope, failure, StringComparison.Ordinal);
            Assert.DoesNotContain(IssuanceFixture.UnlistedAudience, failure, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The issuer re-screens the same rules and refuses to CONSTRUCT, not merely to mint.
    /// </summary>
    /// <remarks>
    /// <para>
    /// NOT REDUNDANT WITH THE VALIDATOR, AND THE REASON IS STRUCTURAL. The validator runs against the
    /// bound options instance, whereas the issuer can be constructed with any instance a caller assembles
    /// - which this test project does routinely, and which a future composition root could do too. The
    /// issuer already applies this discipline to the issuer identity, the roster, the lifetime and the key
    /// identifier, so the matrix following the same rule keeps one file internally consistent.
    /// </para>
    /// <para>
    /// The white-space-carrying scope is the case where re-screening is not merely tidy: a blank caller
    /// reaching the frozen matrix would be a key no request can match and therefore harmless, but a scope
    /// containing a space would be GRANTED as one entry and read back by a verifier as two.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("blank caller")]
    [InlineData("blank audience")]
    [InlineData("no scope")]
    [InlineData("spaced scope")]
    [InlineData("duplicate pair")]
    public void TheIssuerRefusesToConstructOnAContradictoryMatrix(string fault)
    {
        SecurityOptions options = Bootable();

        switch (fault)
        {
            case "blank caller":
                options.CallerAuthorizations.Add(Row("  ", "powerframework-gateway", "s"));
                break;

            case "blank audience":
                options.CallerAuthorizations.Add(Row("powerframework-gateway", "  ", "s"));
                break;

            case "no scope":
                options.CallerAuthorizations.Add(
                    new CallerAuthorizationOptions
                    {
                        Caller = "powerframework-gateway",
                        Audience = "powerframework-gateway",
                    });
                break;

            case "spaced scope":
                options.CallerAuthorizations.Add(
                    Row("powerframework-gateway", "powerframework-gateway", "has a space"));
                break;

            case "duplicate pair":
                options.CallerAuthorizations.Add(
                    Row("powerframework-gateway", "powerframework-gateway", "one"));
                options.CallerAuthorizations.Add(
                    Row("powerframework-gateway", "powerframework-gateway", "two"));
                break;

            default:
                Assert.Fail($"The row '{fault}' names no fault this test knows how to apply.");
                break;
        }

        using RSA key = RSA.Create(SecurityAppFactory.DefaultSigningKeySizeInBits);
        options.SigningKey = key.ExportPkcs8PrivateKeyPem();

        SigningKeyProvider keys = new(Options.Create(options));

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(
            () => new TokenIssuer(
                keys,
                Options.Create(options),
                EmptyIssuanceRoster(options),
                TimeProvider.System,
                NullLogger<TokenIssuer>.Instance));

        Assert.Contains(MatrixKey, failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// An issuance roster carrying whatever the supplied options declare, for a row that constructs the
    /// issuer directly.
    /// </summary>
    /// <param name="options">The options the row shaped.</param>
    /// <returns>The roster.</returns>
    /// <remarks>
    /// THE ROSTER IS A CONSTRUCTION DEPENDENCY AND NOT AN AUTHORIZATION ONE. The issuer requires it so
    /// that a roster naming an unresolvable secret refuses at startup rather than at first request, and it
    /// reads no permission from it - the permission matrix these rows shape is the only gate. So a row
    /// about the matrix supplies a roster built from its own options and an empty configuration: no entry
    /// names a secret, nothing has to resolve, and nothing about the row's subject matter changes.
    /// </remarks>
    private static IssuanceClientRegistry EmptyIssuanceRoster(SecurityOptions options) =>
        new(Options.Create(options), new ConfigurationBuilder().Build());

    /// <summary>Builds a row carrying one scope.</summary>
    /// <param name="caller">The caller identity.</param>
    /// <param name="audience">The audience.</param>
    /// <param name="scope">The single permitted scope.</param>
    /// <returns>The row.</returns>
    private static CallerAuthorizationOptions Row(string caller, string audience, string scope)
    {
        CallerAuthorizationOptions row = new() { Caller = caller, Audience = audience };
        row.Scopes.Add(scope);

        return row;
    }

    /// <summary>Asserts the options are refused and that some failure names the given key.</summary>
    /// <param name="options">The options to validate.</param>
    /// <param name="key">The configuration key the failure must name.</param>
    private static void AssertRefusedNaming(SecurityOptions options, string key)
    {
        ValidateOptionsResult result = new SecurityOptionsValidator().Validate(name: null, options);

        Assert.True(result.Failed);
        Assert.NotNull(result.Failures);
        Assert.Contains(
            result.Failures,
            failure => failure.Contains(key, StringComparison.Ordinal));
    }

    /// <summary>Builds an options instance that is otherwise valid.</summary>
    /// <returns>The instance, with an empty matrix.</returns>
    /// <remarks>
    /// Every member other than the matrix is populated so that a row's assertion is about ITS rule: a
    /// failure list carrying five unrelated entries would let a row pass on the wrong one. The signing
    /// key is generated, so no key literal exists in this file.
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

        Reroster(options);

        return options;
    }

    /// <summary>
    /// Replaces the issuance roster with one entry covering every audience currently on the roster.
    /// </summary>
    /// <param name="options">The instance to reroster.</param>
    /// <remarks>
    /// <para>
    /// AN ISSUANCE-ROSTER ENTRY IS REQUIRED BECAUSE AN EMPTY ROSTER IS ITSELF A REFUSAL.
    /// <c>Security:Clients</c> is the credential directory, and this service - the sole token issuer -
    /// refuses to start with none, since no caller could then authenticate while its readiness probe
    /// reported healthy. Every row in this class is about a DIFFERENT rule, so the entry exists only to keep
    /// the failure list free of an unrelated one. It names no secret key, which is the certificate-only
    /// shape a TLS-terminating deployment uses and is valid on its own.
    /// </para>
    /// <para>
    /// IT IS A METHOD RATHER THAN A LITERAL BLOCK so that a row reshaping the deployment can reshape the
    /// credential directory with it. The directory carries a SUBJECT and nothing else about permissions -
    /// what an identity may request is stated once, in the grant matrix - so this is deliberately short.
    /// </para>
    /// </remarks>
    private static void Reroster(SecurityOptions options)
    {
        options.Clients.Clear();
        options.Clients.Add(new SecurityClientOptions { Subject = "a-rostered-subject" });
    }
}

// ==================================================================================================
//  AREA B - THE ISSUER'S GATE. THREE OUTCOMES, ONE ORDER, AND A NARROWING THAT SUCCEEDS.
// ==================================================================================================

/// <summary>
/// The permission decision itself, driven through a booted host's own minter.
/// </summary>
/// <remarks>
/// Every row shapes the matrix explicitly rather than relying on the suite's default, because the
/// default authorises every test caller for every roster audience - which is what makes the rest of the
/// suite runnable and is exactly the opposite of what these rows need to observe.
/// </remarks>
public sealed class CallerAuthorizationGateTests
{
    /// <summary>The caller these rows authorise.</summary>
    private const string PermittedCaller = "powerframework-gateway";

    /// <summary>A caller these rows never authorise.</summary>
    private const string UnpermittedCaller = "powerframework-persistence";

    /// <summary>A scope these rows permit.</summary>
    private const string PermittedScope = "datawindow.read";

    /// <summary>A second scope these rows permit.</summary>
    private const string SecondPermittedScope = "datawindow.write";

    /// <summary>A scope these rows never permit.</summary>
    private const string UnpermittedScope = "datawindow.administer";

    /// <summary>
    /// THE POSITIVE ARM. An authorised caller asking for exactly its permitted set is issued all of it.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// First, and load-bearing: every refusal row below would also pass against an issuer that refused
    /// everything. The granted set is compared to the requested set as a WHOLE STRING, because that
    /// single value is what both the response and the token claim carry.
    /// </remarks>
    [Fact]
    public async Task AnAuthorisedCallerReceivesItsFullRequestedSetAsync()
    {
        await using SecurityAppFactory factory = Host();

        IssuedToken token = factory.IssueToken(
            PermittedCaller,
            factory.ResolveInboundAudience(),
            [PermittedScope, SecondPermittedScope]);

        Assert.Equal(PermittedScope + " " + SecondPermittedScope, token.GrantedScope);
    }

    /// <summary>A caller absent from the matrix is refused, and no token is produced.</summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// The audience is one the ROSTER carries, so this row can only pass because of the matrix - which is
    /// the whole distinction the finding was about. The null token is asserted alongside the outcome,
    /// because a refusal that also produced a token would be the more dangerous defect of the two.
    /// </remarks>
    [Fact]
    public async Task AnUnauthorisedCallerIsRefusedAsync()
    {
        await using SecurityAppFactory factory = Host();

        string audience = factory.ResolveInboundAudience();

        Assert.Contains(audience, factory.ResolveSecurityOptions().Audiences, StringComparer.Ordinal);

        TokenIssuanceResult result = Issue(factory, UnpermittedCaller, audience, [PermittedScope]);

        Assert.Equal(TokenIssuanceOutcome.CallerNotPermitted, result.Outcome);
        Assert.Null(result.Token);
    }

    /// <summary>
    /// An authorised caller asking for a DIFFERENT roster audience it holds no row for is refused.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// THE ROW THAT PROVES THE MATRIX IS TWO-DIMENSIONAL. Without it, an implementation that authorised a
    /// caller for EVERY roster audience once it appeared anywhere in the matrix would pass every other
    /// row in this class - and that implementation is precisely the escalation the finding describes: a
    /// certificate issued for one caller obtaining a token for every audience in the system.
    /// </para>
    /// <para>
    /// The second audience is added to the roster explicitly, so the refusal is proved to come from the
    /// matrix rather than from the roster check that precedes it.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AnAuthorisedCallerIsRefusedAnAudienceItHoldsNoRowForAsync()
    {
        const string OtherAudience = "powerframework-dataservices";

        await using SecurityAppFactory factory = new()
        {
            ShapeOptions = options =>
            {
                options.Audiences.Clear();
                options.Audiences.Add(IssuanceFixture.SelfAudience);
                options.Audiences.Add(OtherAudience);

                options.CallerAuthorizations.Clear();

                // BOTH SHAPES, BECAUSE THE ISSUER ENFORCES THEIR UNION. This block declares a COMPLETE matrix,
                // and the fixtures that run before it populate the nested `Security:Callers` shape as well as the
                // flat one - so clearing only the flat rows would leave a host whose matrix is partly this row's
                // and partly the fixture's, and every refusal this row exists to observe would be an issuance.
                options.Callers.Clear();
                options.CallerAuthorizations.Add(
                    Row(PermittedCaller, IssuanceFixture.SelfAudience, PermittedScope));
            },
        };

        Assert.Contains(
            OtherAudience,
            factory.ResolveSecurityOptions().Audiences,
            StringComparer.Ordinal);

        Assert.Equal(
            TokenIssuanceOutcome.Issued,
            Issue(factory, PermittedCaller, IssuanceFixture.SelfAudience, [PermittedScope]).Outcome);

        Assert.Equal(
            TokenIssuanceOutcome.CallerNotPermitted,
            Issue(factory, PermittedCaller, OtherAudience, [PermittedScope]).Outcome);
    }

    /// <summary>A request none of whose scopes is permitted is refused, and no token is produced.</summary>
    /// <returns>A task representing the assertion.</returns>
    [Fact]
    public async Task ARequestWithNoPermittedScopeIsRefusedAsync()
    {
        await using SecurityAppFactory factory = Host();

        TokenIssuanceResult result = Issue(
            factory,
            PermittedCaller,
            factory.ResolveInboundAudience(),
            [UnpermittedScope]);

        Assert.Equal(TokenIssuanceOutcome.ScopesNotPermitted, result.Outcome);
        Assert.Null(result.Token);
    }

    /// <summary>
    /// A PARTLY permitted scope set SUCCEEDS, carrying the intersection in the caller's own order.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// THE CONTRACT'S OWN NARROWING RULE, PINNED. security.v1.yaml states the granted set may be narrower
    /// than the requested one and that a narrowing is a successful outcome rather than an error. An
    /// implementation that refused this request would satisfy a naive reading of "enforce scopes" while
    /// contradicting the published document, and nothing else in the suite would catch it.
    /// </para>
    /// <para>
    /// THE ORDER IS THE CALLER'S, NOT THE MATRIX'S. The unpermitted name is placed FIRST in the request so
    /// that a filter preserving request order and one emitting matrix order produce different strings -
    /// which is what makes this row able to distinguish them. Preserving the caller's order is what makes
    /// the granted value recognisably its own request rather than a reordered echo.
    /// </para>
    /// <para>
    /// The refused name is asserted absent from the granted value, because a granted set that echoed a
    /// scope it did not grant would be read by a caller as an entitlement it does not hold.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task APartlyPermittedRequestIsIssuedWithTheIntersectionAsync()
    {
        await using SecurityAppFactory factory = Host();

        // THROUGH THE HOST'S OWN ISSUER AND DELIBERATELY NOT THROUGH THE FIXTURE'S IssueToken. That helper
        // is a CREDENTIAL FORGE: it substitutes a roster permitting precisely what was asked for, so the
        // intersection it performs is the identity function and the narrowing this row exists to observe
        // could never occur. Its purpose is to hand a RECEIVER an arbitrary token; a row about the
        // ISSUER'S DECISION has to resolve the production minter, which is what Issue does.
        IssuedToken? token = Issue(
            factory,
            PermittedCaller,
            factory.ResolveInboundAudience(),
            [UnpermittedScope, SecondPermittedScope, PermittedScope]).Token;

        Assert.NotNull(token);

        Assert.Equal(SecondPermittedScope + " " + PermittedScope, token.GrantedScope);
        Assert.DoesNotContain(UnpermittedScope, token.GrantedScope, StringComparison.Ordinal);
    }

    /// <summary>
    /// The narrowed granted set the response reports and the token's own scope claim are one value.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// A caller reads the granted set from the response; every verifier reads it from the claim. If the
    /// two could differ, a narrowing would be invisible to whichever side read the other - so this is the
    /// property that makes the narrowing rule safe rather than merely documented.
    /// </remarks>
    [Fact]
    public async Task TheNarrowedGrantAppearsIdenticallyInTheResponseAndTheClaimAsync()
    {
        await using SecurityAppFactory factory = Host();

        // The production minter, not the fixture's forge - see the sibling row above for why the
        // distinction decides this assertion rather than merely tidying it.
        IssuedToken? token = Issue(
            factory,
            PermittedCaller,
            factory.ResolveInboundAudience(),
            [PermittedScope, UnpermittedScope]).Token;

        Assert.NotNull(token);

        Microsoft.IdentityModel.JsonWebTokens.JsonWebToken parsed = new(token.AccessToken);

        Assert.Equal(PermittedScope, token.GrantedScope);
        Assert.Equal(
            token.GrantedScope,
            parsed.Claims.Single(claim => string.Equals(claim.Type, "scope", StringComparison.Ordinal))
                .Value);
    }

    /// <summary>An empty matrix mints nothing, for any caller, audience or scope.</summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// THE FAIL-CLOSED HALF OF THE POSTURE, and the pair to
    /// <see cref="CallerAuthorizationValidationTests.AnEmptyMatrixRaisesNoFailure"/>. Accepting the
    /// configuration is not the same as granting anything, and an implementation that treated an empty
    /// matrix as "unconfigured, therefore unrestricted" would be the original defect restored - with the
    /// added hazard of looking deliberate.
    /// </para>
    /// <para>
    /// Every roster audience is tried rather than one, so the row cannot pass because a single audience
    /// happened to be refused for an unrelated reason.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AnEmptyMatrixMintsNothingForAnyone()
    {
        await using SecurityAppFactory factory = new()
        {
            // BOTH SHAPES, so "empty matrix" means empty. The union of the two is what the issuer
            // enforces, so a residual nested grant would make this row assert the opposite of its name.
            ShapeOptions = options =>
            {
                options.CallerAuthorizations.Clear();
                options.Callers.Clear();
            },
        };

        SecurityOptions options = factory.ResolveSecurityOptions();

        Assert.Empty(options.CallerAuthorizations);
        Assert.NotEmpty(options.Audiences);

        foreach (string audience in options.Audiences)
        {
            Assert.Equal(
                TokenIssuanceOutcome.CallerNotPermitted,
                Issue(factory, PermittedCaller, audience, [PermittedScope]).Outcome);
        }
    }

    /// <summary>
    /// THE GATE ORDER: the roster is consulted before the matrix, so an unserved audience says so.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// The request is wrong in TWO ways at once - an audience no roster carries AND a caller no row
    /// authorises - and the reported outcome must be the ROSTER's. A status-only assertion could not tell
    /// these apart, because both refuse; the outcome is what carries the diagnosis, and the two
    /// diagnoses send an operator to different settings. Reporting the permission failure would have them
    /// editing the authorization matrix to fix an audience that is simply not configured.
    /// </para>
    /// <para>
    /// It is also the cheaper order: the roster is a single set lookup and the matrix a pair lookup, and
    /// neither reaches any signing material - so the ordering costs nothing and buys the right message.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TheRosterIsConsultedBeforeTheMatrixAsync()
    {
        await using SecurityAppFactory factory = Host();

        Assert.DoesNotContain(
            IssuanceFixture.UnlistedAudience,
            factory.ResolveSecurityOptions().Audiences,
            StringComparer.Ordinal);

        Assert.Equal(
            TokenIssuanceOutcome.AudienceNotPermitted,
            Issue(factory, UnpermittedCaller, IssuanceFixture.UnlistedAudience, [UnpermittedScope])
                .Outcome);
    }

    /// <summary>
    /// Both gates compare ORDINALLY: a differently cased caller is refused, and a differently cased
    /// scope is not granted.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// Every identity comparison in this service is ordinal - the roster, the key identifier, the
    /// certificate common-name reconciliation - and the matrix matching that is what keeps them
    /// consistent. Case folding here would grant a caller whose certificate establishes a differently
    /// spelled name, which is precisely the reconciliation the token endpoint performs deliberately and
    /// visibly rather than by a comparer's side effect.
    /// </para>
    /// <para>
    /// The scope half is the subtler of the two, because a case-folding scope comparison does not refuse:
    /// it GRANTS, and the granted value it reports back is the caller's spelling rather than the
    /// configured one - so a verifier comparing the claim against its own configured spelling would then
    /// refuse a token this issuer considered valid.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task BothGatesCompareOrdinallyAsync()
    {
        await using SecurityAppFactory factory = Host();

        string audience = factory.ResolveInboundAudience();

        Assert.Equal(
            TokenIssuanceOutcome.CallerNotPermitted,
            Issue(factory, PermittedCaller.ToUpperInvariant(), audience, [PermittedScope]).Outcome);

        Assert.Equal(
            TokenIssuanceOutcome.ScopesNotPermitted,
            Issue(factory, PermittedCaller, audience, [PermittedScope.ToUpperInvariant()]).Outcome);
    }

    /// <summary>
    /// A refusal reaches no signing material, which is observable as an unchanged clock and no record of
    /// an issuance.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// The claim made at the decision site is that a refusal "costs no signature". The cheapest honest
    /// observation of it is that the issuance log record - the one carrying the key identifier and the
    /// expiry, written only on the minting path - does not appear. A row asserting only the outcome would
    /// pass against an implementation that signed first and discarded the result, which is both wasteful
    /// and a needless use of the key.
    /// </para>
    /// <para>
    /// The refusal record IS asserted present, so the row cannot pass against an issuer that logged
    /// nothing at all.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ARefusalWritesNoIssuanceRecordAsync()
    {
        CapturedRecords captured = new();

        await using IssuanceHostFactory factory = new(captured: captured, matrix: SelfAudienceOnly);

        Assert.Equal(
            TokenIssuanceOutcome.CallerNotPermitted,
            Issue(factory, UnpermittedCaller, IssuanceFixture.SelfAudience, [PermittedScope]).Outcome);

        Assert.DoesNotContain(
            captured.Records,
            record => record.Contains("Issued a service token", StringComparison.Ordinal));

        // THE ISSUER'S OWN WORDING, not a paraphrase of it. The record is the only place a deployment can
        // tell a matrix gap from a roster gap - the two answer one caller-facing sentence by design - so the
        // fragment asserted is the one the issuer writes.
        Assert.Contains(
            captured.Records,
            record => record.Contains(
                "this caller is not permitted to address the requested audience",
                StringComparison.Ordinal));
    }

    /// <summary>
    /// THE OPERATOR-FACING RECORDS ARE DISTINGUISHABLE, EVEN THOUGH THE RESPONSES ARE NOT.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// The three refusals answer one status, and two of them answer one sentence, so that a caller cannot
    /// tell an unserved audience from an unpermitted one. That indistinguishability is deliberate and is
    /// asserted in Area C. It has a cost an operator must not pay: without a distinction SOMEWHERE, a
    /// deployment could not tell a caller pointed at the wrong service from a caller reaching past its
    /// permissions. This row is what proves the distinction is kept where it is safe.
    /// </para>
    /// <para>
    /// The three records are asserted PAIRWISE DISTINCT rather than merely present, because three
    /// warnings carrying one sentence would satisfy a presence check while being exactly as useless as no
    /// records at all.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TheThreeRefusalRecordsAreDistinguishableAsync()
    {
        CapturedRecords captured = new();

        await using IssuanceHostFactory factory = new(captured: captured, matrix: SelfAudienceOnly);

        _ = Issue(factory, PermittedCaller, IssuanceFixture.UnlistedAudience, [PermittedScope]);
        _ = Issue(factory, UnpermittedCaller, IssuanceFixture.SelfAudience, [PermittedScope]);
        _ = Issue(factory, PermittedCaller, IssuanceFixture.SelfAudience, [UnpermittedScope]);

        string[] refusals =
        [
            .. captured.Records.Where(record =>
                record.Contains("Refused a token request", StringComparison.Ordinal)),
        ];

        Assert.Equal(3, refusals.Length);
        Assert.Equal(3, refusals.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>No refusal record echoes the caller, the audience or the scope it refused.</summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// A refusal's inputs arrived from a caller - the audience and scopes from its request body, the
    /// caller name from a certificate - and writing unvalidated caller text into a log record is a hazard
    /// whether or not the text happens to be harmless. The distinctive values below cannot overlap with
    /// the record templates, so a record that did echo one is caught.
    /// </remarks>
    [Fact]
    public async Task NoRefusalRecordEchoesACallerSuppliedValueAsync()
    {
        const string DistinctiveCaller = "a-caller-that-must-not-appear-in-a-record";
        const string DistinctiveScope = "a.scope.that.must.not.appear.in.a.record";

        CapturedRecords captured = new();

        await using IssuanceHostFactory factory = new(captured: captured, matrix: SelfAudienceOnly);

        _ = Issue(factory, DistinctiveCaller, IssuanceFixture.SelfAudience, [PermittedScope]);
        _ = Issue(factory, PermittedCaller, IssuanceFixture.SelfAudience, [DistinctiveScope]);

        foreach (string record in captured.Records)
        {
            Assert.DoesNotContain(DistinctiveCaller, record, StringComparison.Ordinal);
            Assert.DoesNotContain(DistinctiveScope, record, StringComparison.Ordinal);
        }
    }

    /// <summary>Builds a host whose matrix authorises one caller for its own audience only.</summary>
    /// <returns>The factory.</returns>
    /// <remarks>
    /// The audience is the host's own identity, which is also the single audience its inbound handler
    /// accepts, so a row needing an authenticated client can use the same host without a second grant.
    /// </remarks>
    private static SecurityAppFactory Host() =>
        new()
        {
            ShapeOptions = options =>
            {
                options.CallerAuthorizations.Clear();

                // BOTH SHAPES, BECAUSE THE ISSUER ENFORCES THEIR UNION. This block declares a COMPLETE matrix,
                // and the fixtures that run before it populate the nested `Security:Callers` shape as well as the
                // flat one - so clearing only the flat rows would leave a host whose matrix is partly this row's
                // and partly the fixture's, and every refusal this row exists to observe would be an issuance.
                options.Callers.Clear();

                CallerAuthorizationOptions row = new()
                {
                    Caller = PermittedCaller,
                    Audience = IssuanceFixture.SelfAudience,
                };

                row.Scopes.Add(PermittedScope);
                row.Scopes.Add(SecondPermittedScope);

                options.CallerAuthorizations.Add(row);
            },
        };

    /// <summary>Issues through the host's own minter and returns the whole result, refusal included.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="caller">The caller identity.</param>
    /// <param name="audience">The requested audience.</param>
    /// <param name="scopes">The requested scopes.</param>
    /// <returns>The issuance result.</returns>
    /// <remarks>
    /// The factory's own <c>IssueToken</c> raises on a refusal, which is right for a row that needs a
    /// token and wrong for a row asserting the refusal. This returns the result instead.
    /// </remarks>
    private static TokenIssuanceResult Issue(
        WebApplicationFactory<Program> factory,
        string caller,
        string audience,
        string[] scopes) =>
        factory.Services
            .GetRequiredService<TokenIssuer>()
            .Issue(new TokenIssuanceRequest(caller, audience, scopes));

    /// <summary>
    /// Authorises one caller for the service's OWN audience with one scope, and nothing else.
    /// </summary>
    /// <param name="options">The options being shaped.</param>
    /// <remarks>
    /// The matrix the three log-record rows share. Narrow in all three dimensions at once so that one
    /// host reaches every refusal: the roster keeps its other audiences, so an audience refusal against
    /// it is proved to come from the matrix rather than the roster check preceding it.
    /// </remarks>
    private static void SelfAudienceOnly(SecurityOptions options)
    {
        options.CallerAuthorizations.Clear();

        // BOTH SHAPES, BECAUSE THE ISSUER ENFORCES THEIR UNION. This block declares a COMPLETE matrix,
        // and the fixtures that run before it populate the nested `Security:Callers` shape as well as the
        // flat one - so clearing only the flat rows would leave a host whose matrix is partly this row's
        // and partly the fixture's, and every refusal this row exists to observe would be an issuance.
        options.Callers.Clear();
        options.CallerAuthorizations.Add(
            Row(PermittedCaller, IssuanceFixture.SelfAudience, PermittedScope));
    }

    /// <summary>Builds a row carrying one scope.</summary>
    /// <param name="caller">The caller identity.</param>
    /// <param name="audience">The audience.</param>
    /// <param name="scope">The single permitted scope.</param>
    /// <returns>The row.</returns>
    private static CallerAuthorizationOptions Row(string caller, string audience, string scope)
    {
        CallerAuthorizationOptions row = new() { Caller = caller, Audience = audience };
        row.Scopes.Add(scope);

        return row;
    }
}

// ==================================================================================================
//  AREA C - THE HTTP SURFACE. THE PUBLISHED STATUS, AND WHAT THE BODY MUST NOT REVEAL.
// ==================================================================================================

/// <summary>
/// The refusals as a caller actually observes them: one status, two sentences, nothing enumerated.
/// </summary>
/// <remarks>
/// Driven through a real request carrying a real client certificate, because two of the properties here
/// are only observable on the wire - the indistinguishability of the audience and caller refusals, and
/// the fact that a partial narrowing is a 200.
/// </remarks>
public sealed class CallerAuthorizationResponseTests
{
    /// <summary>The scope the host below permits.</summary>
    private const string PermittedScope = IssuanceFixture.ReadScope;

    /// <summary>A scope the host below never permits.</summary>
    private const string UnpermittedScope = "datawindow.administer";

    /// <summary>An unauthorised caller is answered with the published forbidden status.</summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// FORBIDDEN AND NOT BAD REQUEST: the request violates no published schema - the schema enumerates
    /// neither audiences nor callers - so its refusal is a permission decision. And not UNAUTHORIZED
    /// either: the caller IS authenticated, having presented a certificate this service accepted, which
    /// is exactly the distinction between the two statuses.
    /// </remarks>
    [Fact]
    public async Task AnUnauthorisedCallerIsAnsweredForbiddenAsync()
    {
        using X509Certificate2 certificate =
            IssuanceFixture.CreateCallerCertificate(IssuanceFixture.CallerIdentity);

        await using IssuanceHostFactory factory = new(certificate, matrix: NarrowMatrix);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            new Uri(IssuanceFixture.IssuancePath, UriKind.Relative),
            IssuanceFixture.Body(audience: IssuanceFixture.SecondAudience, scopes: [PermittedScope]),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(
            "application/problem+json",
            response.Content.Headers.ContentType?.MediaType);
    }

    /// <summary>
    /// AN UNSERVED AUDIENCE AND AN UNPERMITTED ONE ARE INDISTINGUISHABLE TO A CALLER.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// THE DISCLOSURE PROPERTY, AND THE REASON THE TWO REFUSALS SHARE A SENTENCE. A caller able to tell
    /// "no such audience" from "not yours" could enumerate the deployment's audience configuration by
    /// asking for candidate names and reading which refusal came back - turning an authenticated caller
    /// into a map of the system's topology.
    /// </para>
    /// <para>
    /// THE COMPARISON EXCLUDES THE PER-REQUEST TRACE IDENTIFIER, which differs between any two requests
    /// and would make an otherwise byte-identical pair of bodies compare unequal. Every other member is
    /// compared, including the status, the title, the detail and the return code.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AnUnservedAudienceAndAnUnpermittedOneAnswerTheSameBodyAsync()
    {
        using X509Certificate2 certificate =
            IssuanceFixture.CreateCallerCertificate(IssuanceFixture.CallerIdentity);

        await using IssuanceHostFactory factory = new(certificate, matrix: NarrowMatrix);
        using HttpClient client = factory.CreateClient();

        string unserved = await RefusalBodyAsync(
            client,
            IssuanceFixture.Body(audience: IssuanceFixture.UnlistedAudience, scopes: [PermittedScope]));

        string unpermitted = await RefusalBodyAsync(
            client,
            IssuanceFixture.Body(audience: IssuanceFixture.SecondAudience, scopes: [PermittedScope]));

        Assert.Equal(unserved, unpermitted);
    }

    /// <summary>
    /// A scope refusal answers a DIFFERENT sentence, because it discloses nothing the caller lacks.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// The complement of the row above, and the reason the two are not simply collapsed into one
    /// response. A caller reaching this refusal IS authorised for the audience it named, so telling it
    /// which part of its request failed reveals nothing it does not already hold - and is the difference
    /// between a request it can fix and one it cannot. Answering the audience sentence here would send it
    /// to change the audience, which is the one part of the request that was right.
    /// </para>
    /// <para>
    /// The body is also asserted to state the narrowing rule, because a caller that received this refusal
    /// after asking for one bad scope among several good ones would otherwise have no way to learn that
    /// its assumption - all-or-nothing - is wrong.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AScopeRefusalAnswersItsOwnSentenceAsync()
    {
        using X509Certificate2 certificate =
            IssuanceFixture.CreateCallerCertificate(IssuanceFixture.CallerIdentity);

        await using IssuanceHostFactory factory = new(certificate, matrix: NarrowMatrix);
        using HttpClient client = factory.CreateClient();

        string audienceRefusal = await RefusalBodyAsync(
            client,
            IssuanceFixture.Body(audience: IssuanceFixture.SecondAudience, scopes: [PermittedScope]));

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            new Uri(IssuanceFixture.IssuancePath, UriKind.Relative),
            IssuanceFixture.Body(scopes: [UnpermittedScope]),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.NotEqual(audienceRefusal, Strip(body));
        Assert.Contains("nothing to grant", body, StringComparison.Ordinal);
        Assert.Contains("partly permitted request is NOT refused", body, StringComparison.Ordinal);
    }

    /// <summary>A PARTIAL narrowing is a 200 carrying the intersection, on the wire.</summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// The published rule asserted where a consumer meets it. The response's own granted member is read
    /// rather than the token's claim, because that member is what the contract tells a caller to read.
    /// </remarks>
    [Fact]
    public async Task APartialNarrowingIsAnsweredWithTwoHundredAndTheIntersectionAsync()
    {
        using X509Certificate2 certificate =
            IssuanceFixture.CreateCallerCertificate(IssuanceFixture.CallerIdentity);

        await using IssuanceHostFactory factory = new(certificate, matrix: NarrowMatrix);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            new Uri(IssuanceFixture.IssuancePath, UriKind.Relative),
            IssuanceFixture.Body(scopes: [UnpermittedScope, PermittedScope]),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(MediaTypeNames.Application.Json, response.Content.Headers.ContentType?.MediaType);

        using JsonDocument document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        Assert.Equal(
            PermittedScope,
            document.RootElement.GetProperty("scope").GetString());
    }

    /// <summary>No refusal body enumerates a roster, a caller, an audience or a permitted scope.</summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// The standing disclosure assertion for all three refusals. A body that named the requested audience
    /// would be an echo rather than a disclosure, but a body that named a CONFIGURED one - or the scope
    /// set a caller may hold - would let one request map the deployment. The distinctive values cannot
    /// coincide with any word in the published sentences.
    /// </remarks>
    [Fact]
    public async Task NoRefusalBodyEnumeratesConfigurationAsync()
    {
        using X509Certificate2 certificate =
            IssuanceFixture.CreateCallerCertificate(IssuanceFixture.CallerIdentity);

        await using IssuanceHostFactory factory = new(certificate, matrix: NarrowMatrix);
        using HttpClient client = factory.CreateClient();

        foreach (TokenIssuanceRequestBody request in new[]
        {
            IssuanceFixture.Body(audience: IssuanceFixture.UnlistedAudience, scopes: [PermittedScope]),
            IssuanceFixture.Body(audience: IssuanceFixture.SecondAudience, scopes: [PermittedScope]),
            IssuanceFixture.Body(scopes: [UnpermittedScope]),
        })
        {
            using HttpResponseMessage response = await client.PostAsJsonAsync(
                new Uri(IssuanceFixture.IssuancePath, UriKind.Relative),
                request,
                TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

            string body = await response.Content.ReadAsStringAsync(
                TestContext.Current.CancellationToken);

            // The permitted scope is configuration, and no refusal may name it.
            Assert.DoesNotContain(PermittedScope, body, StringComparison.Ordinal);

            // Nor may any refusal name a roster member the caller did not itself supply.
            Assert.DoesNotContain(IssuanceFixture.SelfAudience, body, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Authorises the fixture's caller for the host's own audience with one scope, and NOTHING else.
    /// </summary>
    /// <param name="options">The options being shaped.</param>
    /// <remarks>
    /// Deliberately narrow in all three dimensions at once, so one host drives every refusal in this
    /// class: the second roster audience is unpermitted for this caller, and the administer scope is
    /// unpermitted everywhere. The roster keeps BOTH audiences, so an audience refusal here is proved to
    /// come from the matrix rather than from the roster check preceding it.
    /// </remarks>
    private static void NarrowMatrix(SecurityOptions options)
    {
        options.CallerAuthorizations.Clear();

        // BOTH SHAPES, BECAUSE THE ISSUER ENFORCES THEIR UNION. This block declares a COMPLETE matrix,
        // and the fixtures that run before it populate the nested `Security:Callers` shape as well as the
        // flat one - so clearing only the flat rows would leave a host whose matrix is partly this row's
        // and partly the fixture's, and every refusal this row exists to observe would be an issuance.
        options.Callers.Clear();

        CallerAuthorizationOptions row = new()
        {
            Caller = IssuanceFixture.CallerIdentity,
            Audience = IssuanceFixture.CallerIdentity,
        };

        row.Scopes.Add(PermittedScope);

        options.CallerAuthorizations.Add(row);
    }

    /// <summary>Posts a request expected to be refused and returns its body with the trace removed.</summary>
    /// <param name="client">The client.</param>
    /// <param name="request">The request body.</param>
    /// <returns>The response body, minus the per-request trace identifier.</returns>
    private static async Task<string> RefusalBodyAsync(
        HttpClient client,
        TokenIssuanceRequestBody request)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            new Uri(IssuanceFixture.IssuancePath, UriKind.Relative),
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        return Strip(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Rebuilds a problem body from its members, excluding the per-request trace identifier.
    /// </summary>
    /// <param name="body">The response body.</param>
    /// <returns>A canonical rendering, comparable between two requests.</returns>
    /// <remarks>
    /// REBUILT RATHER THAN PATTERN-ERASED. Deleting the trace member with a text substitution would also
    /// silently succeed if the member were renamed or absent, which would weaken the comparison exactly
    /// where it is doing work. Rebuilding names every member it keeps, so a member ADDED to the body
    /// appears in the comparison rather than being dropped from it.
    /// </remarks>
    private static string Strip(string body)
    {
        using JsonDocument document = JsonDocument.Parse(body);

        return string.Join(
            '\n',
            document.RootElement
                .EnumerateObject()
                .Where(member => !string.Equals(member.Name, "traceId", StringComparison.Ordinal))
                .OrderBy(member => member.Name, StringComparer.Ordinal)
                .Select(member => string.Create(
                    CultureInfo.InvariantCulture,
                    $"{member.Name}={member.Value.ToString()}")));
    }
}

// ==================================================================================================
//  AREA D - THE SHIPPED MATRIX. CLOSED, AND WORKING.
// ==================================================================================================

/// <summary>
/// The matrix <c>appsettings.json</c> ships is exactly the outbound requests the code makes.
/// </summary>
/// <remarks>
/// <para>
/// WHY THIS AREA EXISTS. A fail-closed matrix that omits a row the shipped code depends on does not fail
/// at startup - it fails on the first request that service makes, in a different container, as an
/// authorization refusal whose cause is three files away. These rows tie the settings file to the
/// clients that consume it, so a scope renamed in a client and not in the matrix is caught here.
/// </para>
/// <para>
/// THE ROWS ARE READ FROM THE SHIPPED FILE, not from a copy of it, so the assertion is about what
/// deploys.
/// </para>
/// </remarks>
public sealed class ShippedCallerAuthorizationMatrixTests
{
    /// <summary>
    /// The settings file declares exactly three rows, in a fixed order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE ORDER AND THE COUNT ARE BOTH LOAD-BEARING, AND NOT FOR TIDINESS. The development overlay adds
    /// the end-to-end suite's row at index 3, and the configuration provider merges an array BY INDEX
    /// rather than by appending - so a fourth row added to THIS file would be wholly SHADOWED by the
    /// overlay's, silently, with the row count unchanged and nothing refused.
    /// </para>
    /// <para>
    /// There is no startup check that could catch it, because both configurations are individually valid;
    /// the collision produces a hybrid rather than a duplicate, so the issuer's duplicate-pair guard does
    /// not fire either. This row is the guard. If it fails because a row was legitimately added here, move
    /// the overlay's row to the next free index, then update the count here.
    /// </para>
    /// <para>
    /// THE HAZARD SPANS TWO SETTINGS FILES AND NO MORE, WHICH IS DELIBERATE. Injecting the same row from
    /// <c>orchestration/.env.example</c> and the Compose manifest through
    /// <c>Security:CallerAuthorizations</c> section-path variables at the same literal index would widen it
    /// across the whole repository - redundantly in <c>Development</c>, where this service's own overlay
    /// already states the row beside the credential entry the caller needs, and wrongly in
    /// <c>Production</c>, where no credential entry exists for it at all. Neither injection exists: the
    /// grant is stated once, where the caller is registered.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheShippedMatrixDeclaresExactlyThreeRowsInOrder()
    {
        IList<CallerAuthorizationOptions> rows = Shipped();

        Assert.Equal(3, rows.Count);

        Assert.Equal("powerframework-gateway", rows[0].Caller);
        Assert.Equal("powerframework-dataservices", rows[0].Audience);

        Assert.Equal("powerframework-dataservices", rows[1].Caller);
        Assert.Equal("powerframework-persistence", rows[1].Audience);

        Assert.Equal("powerframework-dataservices", rows[2].Caller);
        Assert.Equal("powerframework-security", rows[2].Audience);
    }

    /// <summary>
    /// Every shipped row's scope set is exactly what the client depending on it requests.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE SCOPE NAMES ARE THE FAILURE THAT WOULD OTHERWISE BE SILENT. A missing scope is not refused -
    /// the request succeeds with a narrower granted set - so a scope renamed in a client and not here
    /// surfaces as an authorization refusal at the RECIPIENT service, one hop away from its cause.
    /// </para>
    /// <para>
    /// The expected values are written out rather than read from the clients' own constants, and that is
    /// deliberate: those constants are private to services this project does not reference, so importing
    /// them is impossible - and a row that read them would in any case only prove the matrix agrees with
    /// itself. Written out, this row is a second independent statement of the same fact, which is what
    /// makes a divergence detectable. The locators are in the settings file's own comment block.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryShippedRowMatchesTheClientThatDependsOnIt()
    {
        IList<CallerAuthorizationOptions> rows = Shipped();

        Assert.Equal(
            ["dataservices.datawindow", "dataservices.columnexpression"],
            rows[0].Scopes);

        Assert.Equal(["persistence.read", "persistence.write"], rows[1].Scopes);

        // `ping` SITS BESIDE `security.crypto` ON THIS ROW BECAUSE OTHERWISE THIS SERVICE'S OWN
        // AUTHENTICATED PROBE CANNOT SUCCEED UNDER THE CONFIGURATION IT SHIPS WITH. Endpoints/
        // PingEndpoints.cs requires the `ping` scope for audience `powerframework-security`, and this is
        // the only shipped row addressing that audience - so with the scope absent the route answered
        // either a mint refusal or a 403 for every issuable combination. The scope is on the row for the
        // caller that ALREADY declares `ping` in its Clients entry and ALREADY addresses this audience,
        // so no row was added and the count above is unchanged.
        Assert.Equal(["security.crypto", "ping"], rows[2].Scopes);
    }

    /// <summary>
    /// This service's own authenticated probe is reachable: some shipped row grants the exact
    /// (audience, scope) pair <c>Endpoints/PingEndpoints.cs</c> requires.
    /// </summary>
    /// <remarks>
    /// <para>
    /// STATED AS A PROPERTY RATHER THAN AS A LITERAL, so it keeps holding if the grant is ever moved to a
    /// different caller. The failure it guards against is the one that was actually shipped: a route that
    /// declares a requirement no issuable token can satisfy. Nothing refuses that at startup - the matrix
    /// is individually valid and the route is individually valid - so the only place it can be caught is
    /// a row that asks the two questions together.
    /// </para>
    /// <para>
    /// The scope name is written out rather than imported: <c>SecurityScopes.Ping</c> is the wire value
    /// and this row is a second independent statement of it, which is what makes a rename detectable
    /// instead of self-consistent.
    /// </para>
    /// </remarks>
    [Fact]
    public void SomeShippedRowMakesThisServicesOwnPingRouteReachable()
    {
        Assert.Contains(
            Shipped(),
            row => string.Equals(row.Audience, "powerframework-security", StringComparison.Ordinal)
                && row.Scopes.Contains("ping", StringComparer.Ordinal));
    }

    /// <summary>Every shipped row names an audience the shipped roster carries.</summary>
    /// <remarks>
    /// The validator refuses a host on this, so a violation is a startup failure rather than a silent
    /// one - but a startup failure discovered during a container bring-up is discovered late and in the
    /// least convenient place. Asserting it here moves the discovery to the build.
    /// </remarks>
    [Fact]
    public void EveryShippedRowNamesARosterAudience()
    {
        SecurityOptions options = ShippedOptions();

        foreach (CallerAuthorizationOptions row in options.CallerAuthorizations)
        {
            Assert.Contains(row.Audience, options.Audiences, StringComparer.Ordinal);
        }
    }

    /// <summary>
    /// No shipped row authorises a Persistence caller, and none addresses Gateway.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE TOPOLOGY, ASSERTED AS AN ABSENCE. The plan's service graph is layered and acyclic: Gateway
    /// calls DataServices and Security, DataServices calls Persistence and Security, and Persistence has
    /// no outbound client at all - it holds verification material and reads the published key set
    /// anonymously. So a row authorising a Persistence caller would grant a capability nothing exercises,
    /// and a row addressing Gateway would grant an internal caller reach into the ingress.
    /// </para>
    /// <para>
    /// An absence is worth a row precisely because nothing else fails when it is violated: adding either
    /// grant would start cleanly, pass every other test, and quietly widen the system's reachability
    /// graph.
    /// </para>
    /// </remarks>
    [Fact]
    public void NoShippedRowContradictsTheServiceTopology()
    {
        IList<CallerAuthorizationOptions> rows = Shipped();

        Assert.DoesNotContain(
            rows,
            row => string.Equals(row.Caller, "powerframework-persistence", StringComparison.Ordinal));

        Assert.DoesNotContain(
            rows,
            row => string.Equals(row.Audience, "powerframework-gateway", StringComparison.Ordinal));
    }

    /// <summary>
    /// The shipped matrix does not authorise the end-to-end suite's identity.
    /// </summary>
    /// <remarks>
    /// A harness identity granted in the settings file would be granted in every deployment that reads
    /// it, which is why that row lives in <c>orchestration/.env.example</c> instead. Asserted so the
    /// separation is enforced rather than remembered.
    /// </remarks>
    [Fact]
    public void TheShippedMatrixDoesNotAuthoriseTheEndToEndSuite()
    {
        Assert.DoesNotContain(
            Shipped(),
            row => string.Equals(row.Caller, "pfw-e2e-suite", StringComparison.Ordinal));
    }

    /// <summary>Reads the shipped matrix from the service's own settings file.</summary>
    /// <returns>The rows, in file order.</returns>
    private static IList<CallerAuthorizationOptions> Shipped() =>
        ShippedOptions().CallerAuthorizations;

    /// <summary>Binds the shipped settings file's security section.</summary>
    /// <returns>The bound options.</returns>
    /// <remarks>
    /// Read through the same configuration stack the service uses, from the file the build copies beside
    /// the test assembly, so the assertion is about the shipped artifact rather than about a literal
    /// restated here. Comments are permitted in the file, which is why the JSON provider is used rather
    /// than a strict parser.
    /// </remarks>
    private static SecurityOptions ShippedOptions()
    {
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddJsonFile(
                Path.Combine(AppContext.BaseDirectory, "appsettings.json"),
                optional: false)
            .Build();

        SecurityOptions options = new();

        configuration.GetSection(SecurityOptions.SectionName).Bind(options);

        return options;
    }
}
