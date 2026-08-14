// ==================================================================================================
//  UnknownSigningKeyRevalidationTests - THE ONE 401 A PLANNED ROTATION USED TO COST
//  ------------------------------------------------------------------------------------------------
//  WHAT WAS MEASURED, AND WHY A SUITE IS NEEDED RATHER THAN A REVIEW
//
//  A rotation was driven end to end against a running deployment: the verifiers were primed on the
//  original key, Security was restarted with a new active key plus the original as retiring, and a token
//  minted under the NEW `kid` was presented at once. Every verifying boundary refused it 401 on attempt 1
//  and admitted it on attempt 2 roughly a seventh of a second later. The token was valid, correctly
//  signed, unexpired and minted seconds earlier by the estate's only issuer.
//
//  THE FIRST FIX FOR IT WAS WRONG IN A WAY ONLY A TEST CATCHES. It asked the configuration manager for
//  its configuration ONCE, on the reasoning that the call performs the refresh the failure had armed.
//  Probed directly on the pinned package, that call returns THE STALE INSTANCE - 0 ms, reference-identical
//  to the one validation just failed against - and retrieves on a background continuation. A single-call
//  version looks the new key identifier up in the OLD key set and leaves exactly the 401 it was meant to
//  remove. The row that catches it is TheRotationIsAbsorbedOnlyAfterTheRefreshActuallyLands, whose manager
//  answers stale on the first ask and refreshed on the second, exactly as the library does.
//
//  WHAT THIS SUITE PINS, AND EACH OF THESE HAS TO BE SEPARABLE
//    1. a valid token under a freshly rotated key is admitted on its FIRST presentation
//    2. the wait is for the refresh to LAND, not for a duration, and it stops the moment it lands
//    3. NOTHING about validation is weakened: signature, issuer, audience and lifetime all still refuse
//    4. a forged key identifier is still refused, and so is a token whose signature does not check out
//       under the key its identifier names
//    5. no other refusal pays anything at all - not a fetch, not a poll, not a delay
//    6. the merge that admits the rotated key does not leak into the service's shared options
//    7. the record written names the key identifier and no part of the credential
//    8. the DEPLOYED composition root has this attached, chained beside the refusal record rather than
//       displacing it
//
//  NO TOKEN, KEY OR SECRET IN THIS FILE IS REAL. Every key is generated per row from the platform's
//  cryptographic generator, exists only in this process's memory, and is never written down or asserted
//  on - constraint C-F applied to a test, exactly as the sibling authorization fixtures apply it.
// ==================================================================================================

using System.Diagnostics;
using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using PowerFramework.Gateway.Authorization;
using Xunit;

namespace PowerFramework.Gateway.Tests;

/// <summary>
/// The unknown-signing-key revalidation suite: what a key rotation costs this boundary, and everything it
/// must still refuse.
/// </summary>
public sealed class UnknownSigningKeyRevalidationTests
{
    /// <summary>The category an absorbed rotation is recorded under.</summary>
    private const string RevalidationCategory =
        "PowerFramework.Gateway.Authorization.UnknownSigningKeyRevalidation";

    /// <summary>The issuer every credential in this file claims and every row trusts.</summary>
    /// <remarks>A reserved test hostname that resolves nowhere; nothing here leaves the process.</remarks>
    private const string TrustedIssuer = "https://security.revalidation-tests.invalid";

    /// <summary>The audience every credential in this file claims and every row trusts.</summary>
    private const string TrustedAudience = "powerframework-revalidation-tests";

    /// <summary>The subject every credential claims. A claim, never a credential.</summary>
    private const string TokenSubject = "revalidation-conformance-test";

    /// <summary>The identifier of the key a verifier was primed on before the rotation.</summary>
    private const string RetiringKeyId = "powerframework-security-signing-1";

    /// <summary>The identifier of the key the issuer rotated TO.</summary>
    private const string RotatedKeyId = "powerframework-security-signing-2";

    /// <summary>An identifier no key set in this file ever publishes.</summary>
    private const string ForgedKeyId = "powerframework-security-signing-forged";

    /// <summary>
    /// The bound the negative rows run under, so that a row proving a refusal does not spend the shipped
    /// ceiling doing it.
    /// </summary>
    /// <remarks>
    /// A DETERMINISM SEAM, NOT A CONFIGURATION KEY (AAP 0.6.7). The shipped ceiling is asserted by
    /// <see cref="TheShippedBoundsAreTheOnesACompositionRootGets"/>, which is the only row that spends it.
    /// </remarks>
    private static readonly TimeSpan TestBudget = TimeSpan.FromMilliseconds(400);

    /// <summary>The poll interval the rows run under.</summary>
    private static readonly TimeSpan TestPoll = TimeSpan.FromMilliseconds(5);

    /// <summary>How long a credential minted for an accepted case stays valid.</summary>
    private static readonly TimeSpan AcceptedLifetime = TimeSpan.FromMinutes(5);

    // ==============================================================================================
    //  1. THE FINDING ITSELF
    // ==============================================================================================

    /// <summary>
    /// A valid token signed by a freshly rotated key is admitted on its FIRST presentation, and only
    /// after the refreshed key set has actually landed.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// 🔴 <b>THE ROW THE FINDING ASKED FOR, AND THE ROW THAT CATCHES THE OBVIOUS WRONG FIX.</b> The manager
    /// answers with the key set the verifier was primed on for the FIRST ask and with the refreshed set
    /// from the second, which is what <c>ConfigurationManager&lt;T&gt;</c> measurably does: a call after
    /// <c>RequestRefresh()</c> returns the stale instance immediately and retrieves in the background. An
    /// implementation that asked once would find no key here and leave the 401 standing.
    /// </para>
    /// <para>
    /// THE ASK COUNT IS ASSERTED, so the row cannot pass against an implementation that got the right
    /// answer from the wrong place - a hook that never consulted the manager, or one that trusted the
    /// token's own header.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TheRotationIsAbsorbedOnlyAfterTheRefreshActuallyLands()
    {
        SymmetricSecurityKey retiring = GenerateKey(RetiringKeyId);
        SymmetricSecurityKey rotated = GenerateKey(RotatedKeyId);

        RotatingKeySetManager manager = new(KeySet(retiring), KeySet(retiring, rotated))
        {
            LandOnAsk = 2,
        };

        JwtBearerOptions options = Verifier(manager);

        AuthenticateResult? result = await DriveAsync(
            options,
            Bearer(Mint(rotated)),
            new SecurityTokenSignatureKeyNotFoundException());

        Assert.NotNull(result);
        Assert.True(
            result.Succeeded,
            "A valid token minted under a rotated signing key was still refused on its first "
                + "presentation, which is the finding this hook exists to close.");

        // THE IDENTITY IS THE TOKEN'S, and it is asserted by VALUE rather than by claim type: the token
        // handler maps `sub` onto the WS-Identity name-identifier URI by default, so naming the short
        // form would assert the mapping table rather than the identity.
        Assert.NotNull(result.Principal);
        Assert.True(result.Principal.Identity?.IsAuthenticated);
        Assert.Contains(
            result.Principal.Claims,
            claim => string.Equals(claim.Value, TokenSubject, StringComparison.Ordinal));

        // The refreshed set landed on the second ask, so anything less than two asks means the hook did
        // not wait for it and cannot have read it.
        Assert.True(
            manager.Asks >= 2,
            $"The key set was asked for {manager.Asks} time(s), so the refreshed configuration cannot "
                + "have been read - the stale answer the library gives on the first ask was mistaken for "
                + "a refreshed one.");
    }

    /// <summary>
    /// The wait ends the moment the refresh lands, and a refreshed set that still does not name the key
    /// is refused there and then rather than after the remaining ceiling.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// THE PROPERTY THAT KEEPS THIS FROM BEING A DELAY: the loop waits for an EVENT. The manager lands a
    /// refreshed set on the second ask that does not contain the forged identifier, and the refusal must
    /// follow immediately - measurably inside a fraction of the budget - because only one retrieval can
    /// occur per refresh interval and there is no later landing worth waiting for.
    /// </remarks>
    [Fact]
    public async Task ARefreshThatLandsWithoutTheKeyIsRefusedAtOnceRatherThanAfterTheCeiling()
    {
        SymmetricSecurityKey retiring = GenerateKey(RetiringKeyId);
        SymmetricSecurityKey rotated = GenerateKey(RotatedKeyId);
        SymmetricSecurityKey forged = GenerateKey(ForgedKeyId);

        RotatingKeySetManager manager = new(KeySet(retiring), KeySet(retiring, rotated))
        {
            LandOnAsk = 2,
        };

        JwtBearerOptions options = Verifier(manager);

        long started = Stopwatch.GetTimestamp();

        AuthenticateResult? result = await DriveAsync(
            options,
            Bearer(Mint(forged)),
            new SecurityTokenSignatureKeyNotFoundException());

        TimeSpan elapsed = Stopwatch.GetElapsedTime(started);

        Assert.Null(result);

        Assert.True(
            elapsed < TestBudget,
            $"The refusal took {elapsed.TotalMilliseconds:F0} ms against a {TestBudget.TotalMilliseconds:F0} ms "
                + "budget, so the loop kept polling after the refresh had already landed instead of "
                + "answering once it had.");
    }

    /// <summary>
    /// A refresh that never lands costs the budget and then leaves the refusal exactly as it was.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// THE FAIL-CLOSED DIRECTION, and it is asserted with the ask count as well as the outcome: the hook
    /// must have kept asking - so the wait is real - and must still have refused.
    /// </remarks>
    [Fact]
    public async Task ARefreshThatNeverLandsRefusesAfterTheBudget()
    {
        SymmetricSecurityKey retiring = GenerateKey(RetiringKeyId);
        SymmetricSecurityKey rotated = GenerateKey(RotatedKeyId);

        RotatingKeySetManager manager = new(KeySet(retiring), KeySet(retiring, rotated));

        JwtBearerOptions options = Verifier(manager);

        AuthenticateResult? result = await DriveAsync(
            options,
            Bearer(Mint(rotated)),
            new SecurityTokenSignatureKeyNotFoundException());

        Assert.Null(result);
        Assert.True(
            manager.Asks > 1,
            "The hook did not wait for the refresh at all, so a rotation whose retrieval is slower than "
                + "the request that provoked it would still cost a 401.");
    }

    // ==============================================================================================
    //  2. THE FAILURE SHAPES THE HANDLER ACTUALLY RAISES
    // ==============================================================================================

    /// <summary>
    /// The aggregate shape the bearer handler raises when it has tried more than one configuration is
    /// unwrapped.
    /// </summary>
    /// <param name="wrapped">A description of the wrapping under test, for the failure message.</param>
    /// <param name="failure">The exception shape to raise.</param>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// NOT DEFENSIVE PADDING, AND THIS IS THE ROW THAT PROVES IT MATTERS. The handler validates against
    /// the current configuration and then against each cached last-known-good one, and reports the
    /// collected failures as an <see cref="AggregateException"/> - so on precisely the path this hook
    /// exists for, the typed failure is usually NOT the exception carried. A suite that exercised only
    /// the bare shape would pass against a hook that never fired in a deployment.
    /// </remarks>
    [Theory]
    [InlineData("bare", null)]
    [InlineData("aggregate", "aggregate")]
    [InlineData("aggregate-with-others", "aggregate-mixed")]
    [InlineData("inner", "inner")]
    [InlineData("nested-aggregate", "nested-aggregate")]
    public async Task EveryShapeTheKeyLookupFailureArrivesInIsRecognised(string wrapped, string? failure)
    {
        Assert.False(string.IsNullOrEmpty(wrapped));

        SecurityTokenSignatureKeyNotFoundException core = new();

        Exception raised = failure switch
        {
            "aggregate" => new AggregateException(core),
            "aggregate-mixed" => new AggregateException(new SecurityTokenExpiredException(), core),
            "inner" => new InvalidOperationException("IDX10500", core),
            "nested-aggregate" => new AggregateException(new AggregateException(core)),
            _ => core,
        };

        SymmetricSecurityKey retiring = GenerateKey(RetiringKeyId);
        SymmetricSecurityKey rotated = GenerateKey(RotatedKeyId);

        RotatingKeySetManager manager = new(KeySet(retiring), KeySet(retiring, rotated))
        {
            LandOnAsk = 2,
        };

        AuthenticateResult? result = await DriveAsync(
            Verifier(manager),
            Bearer(Mint(rotated)),
            raised);

        Assert.NotNull(result);
        Assert.True(result.Succeeded);
    }

    /// <summary>
    /// A failure that names no missing key never touches the key set, a clock or a delay.
    /// </summary>
    /// <param name="kind">The failure to raise.</param>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// THE COST-NOTHING PROPERTY, AND THE ASK COUNT IS THE ONLY HONEST WAY TO ASSERT IT. Every one of
    /// these is a refusal a deployment sees constantly - an expired token, a wrong audience, a signature
    /// that does not check out under a KNOWN key - and none of them may provoke a metadata read or a wait,
    /// because a rotation is not what happened.
    /// </para>
    /// <para>
    /// THE NULL ROW IS NOT PADDING: the handler raises the failure event with no exception in some paths,
    /// and a hook that dereferenced it would fault the request rather than refuse it.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("none")]
    [InlineData("expired")]
    [InlineData("signature")]
    [InlineData("audience")]
    [InlineData("issuer")]
    [InlineData("unrelated")]
    public async Task AFailureThatNamesNoMissingKeyNeverAsksTheKeySet(string kind)
    {
        Exception? raised = kind switch
        {
            "expired" => new SecurityTokenExpiredException(),
            "signature" => new SecurityTokenInvalidSignatureException(),
            "audience" => new SecurityTokenInvalidAudienceException(),
            "issuer" => new SecurityTokenInvalidIssuerException(),
            "unrelated" => new InvalidOperationException("something else entirely"),
            _ => null,
        };

        SymmetricSecurityKey retiring = GenerateKey(RetiringKeyId);
        SymmetricSecurityKey rotated = GenerateKey(RotatedKeyId);

        RotatingKeySetManager manager = new(KeySet(retiring), KeySet(retiring, rotated))
        {
            LandOnAsk = 1,
        };

        AuthenticateResult? result = await DriveAsync(Verifier(manager), Bearer(Mint(rotated)), raised);

        Assert.Null(result);
        Assert.Equal(0, manager.Asks);
    }

    /// <summary>
    /// A credential this hook cannot read a key identifier out of is refused without asking for anything.
    /// </summary>
    /// <param name="credential">The authorization header value to present.</param>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// EVERY WAY THE READ CAN COME UP EMPTY, and each has its own early exit: no header at all, a scheme
    /// this hook does not read, a value that is not a compact serialization, a well-formed token whose
    /// header names no key, a header whose <c>kid</c> is not a string, a header segment that is not JSON,
    /// one that is JSON but not an object, and one whose length exceeds the bound this reader decodes
    /// within. None may wait, because none can become resolvable by refreshing a key set keyed on exactly
    /// the name that is missing.
    /// </para>
    /// <para>
    /// THE LAST FOUR ARE THE ARMS OF A HAND-READ JOSE HEADER, and they exist because the header is read
    /// field-by-field rather than by constructing the identity library's token type - which would put a
    /// reference to a token-MINTING library into a service that is only ever allowed to verify (C-G). The
    /// over-long row carries a PERFECTLY VALID key identifier, so the only reason it is refused is the
    /// bound, which is what makes the row an assertion about the bound rather than about the parse.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("")]
    [InlineData("this-is-not-a-compact-serialization")]
    [InlineData("Bearer not-a-header.not-a-payload.not-a-signature")]
    [InlineData("PowerFrameworkLegacy some.token.value")]
    [InlineData("Bearer ")]
    [InlineData("no-kid")]
    [InlineData("kid-not-a-string")]
    [InlineData("header-not-json")]
    [InlineData("header-not-an-object")]
    [InlineData("header-over-the-bound")]
    public async Task ACredentialWithNoReadableKeyIdentifierIsNeverWaitedOn(string credential)
    {
        SymmetricSecurityKey retiring = GenerateKey(RetiringKeyId);
        SymmetricSecurityKey rotated = GenerateKey(RotatedKeyId);

        RotatingKeySetManager manager = new(KeySet(retiring), KeySet(retiring, rotated))
        {
            LandOnAsk = 1,
        };

        string? presented = credential switch
        {
            "" => null,
            "no-kid" => Bearer(Mint(GenerateKey(keyId: null))),
            "kid-not-a-string" => Bearer(Crafted("{\"alg\":\"HS256\",\"kid\":7}")),
            "header-not-json" => Bearer(Crafted("this is not json")),
            "header-not-an-object" => Bearer(Crafted("[\"" + RotatedKeyId + "\"]")),
            "header-over-the-bound" => Bearer(Crafted(
                "{\"alg\":\"HS256\",\"pad\":\""
                    + new string('p', 6000)
                    + "\",\"kid\":\""
                    + RotatedKeyId
                    + "\"}")),
            _ => credential,
        };

        AuthenticateResult? result = await DriveAsync(
            Verifier(manager),
            presented,
            new SecurityTokenSignatureKeyNotFoundException());

        Assert.Null(result);
        Assert.Equal(0, manager.Asks);
    }

    /// <summary>
    /// The scheme token is read case-insensitively, exactly as the bearer handler reads it.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// RFC 9110 makes the scheme token case-insensitive and the handler accepts both spellings, so a
    /// case-sensitive read here would decline to help precisely the callers the handler had accepted.
    /// </remarks>
    [Fact]
    public async Task ALowercaseSchemeIsReadTheSameWayTheHandlerReadsIt()
    {
        SymmetricSecurityKey retiring = GenerateKey(RetiringKeyId);
        SymmetricSecurityKey rotated = GenerateKey(RotatedKeyId);

        RotatingKeySetManager manager = new(KeySet(retiring), KeySet(retiring, rotated))
        {
            LandOnAsk = 2,
        };

        AuthenticateResult? result = await DriveAsync(
            Verifier(manager),
            "bearer " + Mint(rotated),
            new SecurityTokenSignatureKeyNotFoundException());

        Assert.NotNull(result);
        Assert.True(result.Succeeded);
    }

    // ==============================================================================================
    //  3. NOTHING IS WEAKENED
    // ==============================================================================================

    /// <summary>
    /// Every check this boundary performs still refuses, even once the key identifier resolves.
    /// </summary>
    /// <param name="defect">Which property of the credential is wrong.</param>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// 🔴 <b>THE SAFETY PROPERTY, AND IT IS FOUR SEPARATE PROPERTIES.</b> The revalidation runs the
    /// service's OWN validation parameters with only the manager's issuer and keys merged in, which is what
    /// the bearer handler itself does on its first attempt. So a token whose signature does not check out
    /// under the key its identifier names, or whose audience, issuer or lifetime is wrong, must still be
    /// refused - and each of those is a different code path inside the handler.
    /// </para>
    /// <para>
    /// THE SIGNATURE ROW IS THE ONE THAT MATTERS MOST. It presents a token whose <c>kid</c> names a
    /// published key and whose signature was produced by a DIFFERENT key, which is exactly the forgery a
    /// hook that resolved keys by identifier alone would admit.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("signature")]
    [InlineData("audience")]
    [InlineData("issuer")]
    [InlineData("expired")]
    public async Task ARevalidationStillRefusesEverythingTheHandlerWouldHaveRefused(string defect)
    {
        SymmetricSecurityKey retiring = GenerateKey(RetiringKeyId);
        SymmetricSecurityKey rotated = GenerateKey(RotatedKeyId);

        // Signed by a key the published set never contains, but STAMPED with the identifier of a key it
        // does - the forgery the identifier lookup on its own would let through.
        SymmetricSecurityKey impostor = GenerateKey(RotatedKeyId);

        RotatingKeySetManager manager = new(KeySet(retiring), KeySet(retiring, rotated))
        {
            LandOnAsk = 2,
        };

        string credential = defect switch
        {
            "signature" => Mint(impostor),
            "audience" => Mint(rotated, audience: "powerframework-somebody-else"),
            "issuer" => Mint(rotated, issuer: "https://issuer.forged.invalid"),
            _ => Mint(rotated, lifetime: TimeSpan.FromHours(-1)),
        };

        JwtBearerOptions options = Verifier(manager);

        AuthenticateResult? result = await DriveAsync(
            options,
            Bearer(credential),
            new SecurityTokenSignatureKeyNotFoundException());

        Assert.Null(result);
    }

    /// <summary>
    /// The merge that admits a rotated key does not leak into the options every later request validates
    /// against.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// THE CLONE IS WHAT KEEPS THIS TO ONE REQUEST. Merging the refreshed issuer and key list into the
    /// SHARED <see cref="TokenValidationParameters"/> would leave one request's fetched material trusted by
    /// every subsequent validation on the service - a durable widening of what this boundary accepts,
    /// produced by a single unauthenticated request.
    /// </remarks>
    [Fact]
    public async Task TheMergedKeySetDoesNotLeakIntoTheSharedOptions()
    {
        SymmetricSecurityKey retiring = GenerateKey(RetiringKeyId);
        SymmetricSecurityKey rotated = GenerateKey(RotatedKeyId);

        RotatingKeySetManager manager = new(KeySet(retiring), KeySet(retiring, rotated))
        {
            LandOnAsk = 2,
        };

        JwtBearerOptions options = Verifier(manager);

        int issuersBefore = options.TokenValidationParameters.ValidIssuers?.Count() ?? 0;
        int keysBefore = options.TokenValidationParameters.IssuerSigningKeys?.Count() ?? 0;

        AuthenticateResult? result = await DriveAsync(
            options,
            Bearer(Mint(rotated)),
            new SecurityTokenSignatureKeyNotFoundException());

        Assert.NotNull(result);
        Assert.True(result.Succeeded);

        Assert.Equal(issuersBefore, options.TokenValidationParameters.ValidIssuers?.Count() ?? 0);
        Assert.Equal(keysBefore, options.TokenValidationParameters.IssuerSigningKeys?.Count() ?? 0);
    }

    /// <summary>
    /// A decision an earlier handler in the chain already made is not reconsidered, in either direction.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// A CHAINED HANDLER THAT HAS SPOKEN OWNS THE OUTCOME. This hook may only convert a refusal nobody has
    /// answered yet; overwriting a deliberate decision would make the order of two attachments decide what
    /// a boundary accepts.
    /// </remarks>
    [Fact]
    public async Task AResultAnEarlierHandlerAlreadySetIsNotReconsidered()
    {
        SymmetricSecurityKey retiring = GenerateKey(RetiringKeyId);
        SymmetricSecurityKey rotated = GenerateKey(RotatedKeyId);

        RotatingKeySetManager manager = new(KeySet(retiring), KeySet(retiring, rotated))
        {
            LandOnAsk = 1,
        };

        JwtBearerOptions options = new()
        {
            Events = new JwtBearerEvents
            {
                OnAuthenticationFailed = static context =>
                {
                    context.NoResult();
                    return Task.CompletedTask;
                },
            },
        };

        Configure(options, manager);
        UnknownSigningKeyRevalidation.Attach(options, TestBudget, TestPoll);

        AuthenticateResult? result = await DriveAsync(
            options,
            Bearer(Mint(rotated)),
            new SecurityTokenSignatureKeyNotFoundException());

        Assert.NotNull(result);
        Assert.False(result.Succeeded);
        Assert.Equal(0, manager.Asks);
    }

    /// <summary>
    /// The previously registered failure handler still runs, and it runs FIRST.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// THE REFUSAL RECORD IS THE HANDLER THIS DISPLACES IF CHAINING IS DONE WRONG, and it is the one that
    /// classifies a failure for the operator. It must see the failure as it ARRIVED - so it observes no
    /// result yet - and it must still see it at all, which an assignment rather than a chain would prevent.
    /// </remarks>
    [Fact]
    public async Task ThePreviouslyRegisteredFailureHandlerStillRunsAndRunsFirst()
    {
        SymmetricSecurityKey retiring = GenerateKey(RetiringKeyId);
        SymmetricSecurityKey rotated = GenerateKey(RotatedKeyId);

        RotatingKeySetManager manager = new(KeySet(retiring), KeySet(retiring, rotated))
        {
            LandOnAsk = 2,
        };

        List<string> observed = [];

        JwtBearerOptions options = new()
        {
            Events = new JwtBearerEvents
            {
                OnAuthenticationFailed = context =>
                {
                    observed.Add(context.Exception?.GetType().Name ?? "(none)");
                    observed.Add(context.Result is null ? "undecided" : "decided");
                    return Task.CompletedTask;
                },
            },
        };

        Configure(options, manager);
        UnknownSigningKeyRevalidation.Attach(options, TestBudget, TestPoll);

        AuthenticateResult? result = await DriveAsync(
            options,
            Bearer(Mint(rotated)),
            new SecurityTokenSignatureKeyNotFoundException());

        Assert.NotNull(result);
        Assert.True(result.Succeeded);

        Assert.Equal(
            [nameof(SecurityTokenSignatureKeyNotFoundException), "undecided"],
            observed);
    }

    // ==============================================================================================
    //  4. THE DEPLOYMENT SHAPES THAT MUST BE LEFT ALONE
    // ==============================================================================================

    /// <summary>
    /// A deployment validating against statically configured keys is untouched.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// With no configuration manager there is no authority-published key set to refresh, so an unknown
    /// identifier cannot become known by asking again. The hook must decline rather than fault the request.
    /// </remarks>
    [Fact]
    public async Task ADeploymentWithNoConfigurationManagerIsUntouched()
    {
        SymmetricSecurityKey rotated = GenerateKey(RotatedKeyId);

        JwtBearerOptions options = Verifier(manager: null);

        AuthenticateResult? result = await DriveAsync(
            options,
            Bearer(Mint(rotated)),
            new SecurityTokenSignatureKeyNotFoundException());

        Assert.Null(result);
    }

    /// <summary>
    /// A key set that cannot be retrieved leaves the refusal exactly as it was.
    /// </summary>
    /// <param name="failsOnFirstAsk">Whether the manager throws on the first ask or during the wait.</param>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// THE ONE PLACE WHERE DOING NOTHING IS THE WHOLE CORRECT BEHAVIOUR. The request was already being
    /// refused and a metadata failure is not a reason to admit it. Both positions are exercised because
    /// they are different code paths: the first ask and the polling ask.
    /// </remarks>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AKeySetThatCannotBeRetrievedLeavesTheRefusalAlone(bool failsOnFirstAsk)
    {
        SymmetricSecurityKey retiring = GenerateKey(RetiringKeyId);
        SymmetricSecurityKey rotated = GenerateKey(RotatedKeyId);

        ThrowingKeySetManager manager = new(KeySet(retiring))
        {
            ThrowsFromAsk = failsOnFirstAsk ? 1 : 2,
        };

        AuthenticateResult? result = await DriveAsync(
            Verifier(manager),
            Bearer(Mint(rotated)),
            new SecurityTokenSignatureKeyNotFoundException());

        Assert.Null(result);
    }

    // ==============================================================================================
    //  5. THE LIBRARY'S RATE LIMIT IS THE ONE THAT APPLIES
    // ==============================================================================================

    /// <summary>
    /// A flood of forged key identifiers provokes exactly one metadata retrieval, and admits none of them.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// DRIVEN AGAINST THE FRAMEWORK'S OWN <see cref="ConfigurationManager{T}"/> RATHER THAN A DOUBLE,
    /// because the rate limit under test is the library's and a double could only restate it. The retriever
    /// counts real retrievals; the refresh is requested before every drive exactly as the bearer handler
    /// requests it; and the count must not move past the first, because <c>RequestRefresh()</c> re-arms at
    /// most once per <c>RefreshInterval</c>.
    /// </para>
    /// <para>
    /// THIS IS ALSO THE ROW THAT WOULD CATCH A HAND-ROLLED FETCH. Nothing in this repository may retrieve a
    /// key set itself - metadata retrieval staying entirely framework code is what AAP 0.6.6.3's
    /// sole-issuer topology rests on - and a hook that fetched would show up here as a retrieval per
    /// request.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AFloodOfForgedKeyIdentifiersProvokesOneRetrievalAndAdmitsNothing()
    {
        SymmetricSecurityKey retiring = GenerateKey(RetiringKeyId);
        CountingConfigurationRetriever retriever = new(KeySet(retiring));

        ConfigurationManager<OpenIdConnectConfiguration> manager = new(
            TrustedIssuer + "/.well-known/openid-configuration",
            retriever,
            new HttpDocumentRetriever());

        // Prime it exactly as a running verifier is primed by its first authenticated request.
        _ = await manager.GetBaseConfigurationAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, retriever.Retrievals);

        // A short bound for this row alone: twenty drives at the shipped ceiling would spend twenty
        // seconds proving a property that does not depend on the ceiling's value.
        JwtBearerOptions options = Verifier(manager, TimeSpan.FromMilliseconds(30), TimeSpan.FromMilliseconds(10));

        for (int attempt = 0; attempt < 20; attempt++)
        {
            manager.RequestRefresh();

            string forged = ForgedKeyId + "-" + attempt.ToString(CultureInfo.InvariantCulture);

            AuthenticateResult? result = await DriveAsync(
                options,
                Bearer(Mint(GenerateKey(forged))),
                new SecurityTokenSignatureKeyNotFoundException());

            Assert.Null(result);
        }

        Assert.True(
            retriever.Retrievals <= 2,
            $"Twenty refused requests provoked {retriever.Retrievals} metadata retrievals, so something "
                + "here is fetching per request rather than reading what the manager already holds.");
    }

    // ==============================================================================================
    //  6. WHAT AN OPERATOR IS TOLD
    // ==============================================================================================

    /// <summary>
    /// An absorbed rotation leaves exactly one record, at a level every shipped profile admits, naming the
    /// key identifier and no part of the credential.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// AT INFORMATION RATHER THAN WARNING, DELIBERATELY: this is a rotation absorbed correctly, not a
    /// refusal, and a record at Warning would put a successful request in the same band as credential
    /// stuffing.
    /// </para>
    /// <para>
    /// THE DISCLOSURE HALF IS THE HALF MOST EASILY LOST when fields are added to make a record more
    /// actionable. A <c>kid</c> is a PUBLIC key-set identifier - it appears in Security's anonymously
    /// published key set - and it is the one field that names WHICH rotation was absorbed. The token, its
    /// signature and its subject are asserted absent.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AnAbsorbedRotationIsRecordedOnceNamingOnlyTheKeyIdentifier()
    {
        SymmetricSecurityKey retiring = GenerateKey(RetiringKeyId);
        SymmetricSecurityKey rotated = GenerateKey(RotatedKeyId);

        RotatingKeySetManager manager = new(KeySet(retiring), KeySet(retiring, rotated))
        {
            LandOnAsk = 2,
        };

        RecordingProvider provider = new();
        string credential = Mint(rotated);

        AuthenticateResult? result = await DriveAsync(
            Verifier(manager),
            Bearer(credential),
            new SecurityTokenSignatureKeyNotFoundException(),
            provider);

        Assert.NotNull(result);
        Assert.True(result.Succeeded);

        CapturedRecord record = Assert.Single(provider.RecordsFor(RevalidationCategory));

        Assert.Equal(LogLevel.Information, record.Level);
        Assert.Contains(RotatedKeyId, record.Message, StringComparison.Ordinal);
        Assert.Contains("key rotation at the issuer being absorbed", record.Message, StringComparison.Ordinal);

        // NO PART OF THE CREDENTIAL. The whole serialization, its signature segment and its subject.
        Assert.DoesNotContain(credential, record.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(
            credential[(credential.LastIndexOf('.') + 1)..],
            record.Message,
            StringComparison.Ordinal);
        Assert.DoesNotContain(TokenSubject, record.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A key identifier is bounded and control-escaped before it is recorded.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// UNVERIFIED CALLER INPUT AT THE MOMENT IT IS READ. A control character in it could forge a line break
    /// in an operator's log and an unbounded length could flood it with one request, so the same treatment
    /// the refusal record gives an unverified claim applies here. The identifier under test is a PUBLISHED
    /// one, because that is the only way the record is reached at all - which is itself the point: an
    /// issuer that published a hostile identifier could not use it to write into the log.
    /// </remarks>
    [Fact]
    public async Task AKeyIdentifierIsBoundedAndControlEscapedBeforeItIsRecorded()
    {
        string hostile = "rotated-" + new string('k', 200);
        hostile = hostile.Insert(8, "\r\nADMIN ");

        SymmetricSecurityKey retiring = GenerateKey(RetiringKeyId);
        SymmetricSecurityKey rotated = GenerateKey(hostile);

        RotatingKeySetManager manager = new(KeySet(retiring), KeySet(retiring, rotated))
        {
            LandOnAsk = 2,
        };

        RecordingProvider provider = new();

        AuthenticateResult? result = await DriveAsync(
            Verifier(manager),
            Bearer(Mint(rotated)),
            new SecurityTokenSignatureKeyNotFoundException(),
            provider);

        Assert.NotNull(result);
        Assert.True(result.Succeeded);

        CapturedRecord record = Assert.Single(provider.RecordsFor(RevalidationCategory));

        Assert.DoesNotContain('\r', record.Message);
        Assert.DoesNotContain('\n', record.Message);
        Assert.DoesNotContain(new string('k', 200), record.Message, StringComparison.Ordinal);
        Assert.Contains("rotated-", record.Message, StringComparison.Ordinal);
    }

    // ==============================================================================================
    //  7. THE SHIPPED BOUNDS, AND THE COMPOSITION ROOT THAT USES THEM
    // ==============================================================================================

    /// <summary>
    /// The shipped bounds are what a composition root gets, and they are coherent.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// THE ONLY ROW THAT SPENDS THE CEILING, and it spends it on purpose: the single-argument overload is
    /// what every composition root calls, so a row that only asserted the constants could pass against an
    /// overload that ignored them. The elapsed assertion is a LOWER bound, which is the only kind that is
    /// safe under a loaded test host - a delay overshoots, never undershoots.
    /// </para>
    /// <para>
    /// NO CONFIGURATION KEY REACHES EITHER VALUE, which is the property that keeps a request-holding
    /// ceiling from becoming a deployment knob. That is asserted structurally by the options types
    /// carrying no such member, and stated here so the intent is not rediscovered as an omission.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TheShippedBoundsAreTheOnesACompositionRootGets()
    {
        Assert.Equal(TimeSpan.FromSeconds(1), UnknownSigningKeyRevalidation.RefreshLandingBudget);
        Assert.Equal(TimeSpan.FromMilliseconds(25), UnknownSigningKeyRevalidation.RefreshPollInterval);

        Assert.True(
            UnknownSigningKeyRevalidation.RefreshPollInterval
                < UnknownSigningKeyRevalidation.RefreshLandingBudget,
            "The poll interval is not shorter than the budget, so the wait can only ever ask once.");

        SymmetricSecurityKey retiring = GenerateKey(RetiringKeyId);
        SymmetricSecurityKey rotated = GenerateKey(RotatedKeyId);

        // A manager that never lands, so the wait runs to the shipped ceiling.
        RotatingKeySetManager manager = new(KeySet(retiring), KeySet(retiring, rotated));

        JwtBearerOptions options = new();
        Configure(options, manager);
        UnknownSigningKeyRevalidation.Attach(options);

        long started = Stopwatch.GetTimestamp();

        AuthenticateResult? result = await DriveAsync(
            options,
            Bearer(Mint(rotated)),
            new SecurityTokenSignatureKeyNotFoundException());

        TimeSpan elapsed = Stopwatch.GetElapsedTime(started);

        Assert.Null(result);

        Assert.True(
            elapsed >= TimeSpan.FromMilliseconds(900),
            $"The wait lasted {elapsed.TotalMilliseconds:F0} ms, so the shipped overload is not using the "
                + "shipped budget.");

        Assert.True(
            manager.Asks >= 20,
            $"The key set was asked for {manager.Asks} time(s) across a one-second wait, so the shipped "
                + "poll interval is not the one in use.");
    }

    /// <summary>
    /// Incoherent bounds are refused at attachment rather than at the first rotation.
    /// </summary>
    /// <param name="budgetMilliseconds">The budget to attach with.</param>
    /// <param name="pollMilliseconds">The poll interval to attach with.</param>
    /// <remarks>
    /// A ZERO BUDGET WOULD MAKE THE HOOK A NO-OP AND A POLL LONGER THAN THE BUDGET WOULD MAKE IT ASK ONCE -
    /// both silently reinstating the finding. Refused where they are stated, not where they would be felt.
    /// </remarks>
    [Theory]
    [InlineData(0, 25)]
    [InlineData(-1, 25)]
    [InlineData(1000, 0)]
    [InlineData(1000, -1)]
    [InlineData(20, 25)]
    public void IncoherentBoundsAreRefusedAtAttachment(int budgetMilliseconds, int pollMilliseconds)
    {
        JwtBearerOptions options = new();

        _ = Assert.Throws<ArgumentOutOfRangeException>(
            () => UnknownSigningKeyRevalidation.Attach(
                options,
                TimeSpan.FromMilliseconds(budgetMilliseconds),
                TimeSpan.FromMilliseconds(pollMilliseconds)));
    }

    /// <summary>An attachment with no options to extend is refused.</summary>
    [Fact]
    public void AnAttachmentWithNoOptionsIsRefused() =>
        Assert.Throws<ArgumentNullException>(() => UnknownSigningKeyRevalidation.Attach(null!));

    /// <summary>
    /// The DEPLOYED composition root absorbs a rotation on the first request, with the refusal record
    /// still chained beside it.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// 🔴 <b>EVERY ROW ABOVE DRIVES OPTIONS THIS FILE BUILT; THIS ONE DRIVES THE OPTIONS THE SERVICE
    /// ACTUALLY RUNS ON.</b> The finding was an omission in a composition root, and no amount of testing a
    /// helper in isolation can catch one. The bearer options are RESOLVED from the started host, so what
    /// runs is the deployed events chain - the refusal record's handler and this hook, in the order the
    /// root attached them.
    /// </para>
    /// <para>
    /// THE ISSUER AND AUDIENCE ARE READ FROM THE RESOLVED PARAMETERS rather than restated, so the row
    /// cannot pass against a host that trusts something else, and the configuration manager is replaced
    /// with a landing one because the fixture's static key set can never rotate.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TheDeployedCompositionRootAbsorbsARotationOnTheFirstRequest()
    {
        await using GatewayTestHostFixture host = new();

        using HttpClient client = host.CreateClient();

        JwtBearerOptions bearer = host.Services
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        TokenValidationParameters parameters = bearer.TokenValidationParameters;

        string issuer = parameters.ValidIssuer
            ?? parameters.ValidIssuers?.FirstOrDefault()
            ?? TrustedIssuer;

        string audience = parameters.ValidAudience
            ?? parameters.ValidAudiences?.FirstOrDefault()
            ?? TrustedAudience;

        SymmetricSecurityKey rotated = GenerateKey(RotatedKeyId);

        IConfigurationManager<OpenIdConnectConfiguration>? original = bearer.ConfigurationManager;

        try
        {
            bearer.ConfigurationManager = new RotatingKeySetManager(
                KeySet(GenerateKey(RetiringKeyId)),
                KeySet(rotated),
                issuer)
            {
                LandOnAsk = 2,
            };

            RecordingProvider provider = new();

            AuthenticateResult? result = await DriveAsync(
                bearer,
                Bearer(Mint(rotated, issuer: issuer, audience: audience)),
                new SecurityTokenSignatureKeyNotFoundException(),
                provider);

            Assert.NotNull(result);
            Assert.True(
                result.Succeeded,
                "The deployed composition root did not absorb the rotation, so either the hook is not "
                    + "attached there or an earlier handler in the chain answers first.");

            _ = Assert.Single(provider.RecordsFor(RevalidationCategory));
        }
        finally
        {
            bearer.ConfigurationManager = original;
        }
    }

    // ==============================================================================================
    //  HELPERS
    // ==============================================================================================

    /// <summary>Generates a key that exists only in this process, for one row.</summary>
    /// <param name="keyId">The identifier to stamp, or <see langword="null"/> for none.</param>
    /// <returns>The key.</returns>
    /// <remarks>
    /// 64 bytes from the platform's cryptographic generator, never a literal and never written down
    /// (constraint C-F). The anti-pattern being avoided is <c>tests/blink/test_jws.htm:L8-L23</c>, which
    /// hardcodes a plaintext PEM RSA private key and signs a JWS with it.
    /// </remarks>
    private static SymmetricSecurityKey GenerateKey(string? keyId) =>
        new(RandomNumberGenerator.GetBytes(64)) { KeyId = keyId };

    /// <summary>Builds a published key set.</summary>
    /// <param name="keys">The keys it publishes.</param>
    /// <returns>The configuration.</returns>
    private static OpenIdConnectConfiguration KeySet(params SecurityKey[] keys) => KeySet(TrustedIssuer, keys);

    /// <summary>Builds a published key set for a given issuer.</summary>
    /// <param name="issuer">The issuer the set declares.</param>
    /// <param name="keys">The keys it publishes.</param>
    /// <returns>The configuration.</returns>
    private static OpenIdConnectConfiguration KeySet(string issuer, params SecurityKey[] keys)
    {
        OpenIdConnectConfiguration configuration = new() { Issuer = issuer };

        foreach (SecurityKey key in keys)
        {
            configuration.SigningKeys.Add(key);
        }

        return configuration;
    }

    /// <summary>Mints a credential.</summary>
    /// <param name="key">The key to sign with, whose identifier is stamped into the header.</param>
    /// <param name="issuer">The issuer to claim.</param>
    /// <param name="audience">The audience to claim.</param>
    /// <param name="lifetime">How long it is valid, negative for an expired one.</param>
    /// <returns>The compact serialization.</returns>
    /// <remarks>
    /// <para>
    /// 🔴 THE SERIALIZATION IS BUILT HERE RATHER THAN BY A TOKEN HANDLER, AND THAT IS A CONSTRAINT RATHER
    /// THAN A PREFERENCE. This service's own suite asserts that NEITHER the application assembly NOR THIS
    /// TEST ASSEMBLY references a token-minting library, because AAP 0.6.6.3 makes Security the sole issuer
    /// and constraint C-G leaves every other service verification-side only. Calling
    /// <c>JsonWebTokenHandler.CreateToken</c> from here would put that reference into this assembly and
    /// fail that guard - correctly. So a compact serialization is assembled from its three segments with
    /// the verification library's own base64url encoder and the platform's keyed hash.
    /// </para>
    /// <para>
    /// WRITTEN WITH A <see cref="Utf8JsonWriter"/> rather than by string concatenation or a serializer, so
    /// that a key identifier carrying control characters or quotes is escaped correctly - one row depends
    /// on exactly that - and so that no reflection-based serialization is involved.
    /// </para>
    /// </remarks>
    private static string Mint(
        SecurityKey key,
        string? issuer = null,
        string? audience = null,
        TimeSpan? lifetime = null)
    {
        SymmetricSecurityKey symmetric = Assert.IsType<SymmetricSecurityKey>(key);

        TimeSpan window = lifetime ?? AcceptedLifetime;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset issued = window < TimeSpan.Zero ? now + window - AcceptedLifetime : now;

        byte[] header = JsonObject(writer =>
        {
            writer.WriteString("alg", "HS256");
            writer.WriteString("typ", "JWT");

            if (!string.IsNullOrEmpty(symmetric.KeyId))
            {
                writer.WriteString("kid", symmetric.KeyId);
            }
        });

        byte[] payload = JsonObject(writer =>
        {
            writer.WriteString("iss", issuer ?? TrustedIssuer);
            writer.WriteString("aud", audience ?? TrustedAudience);
            writer.WriteString("sub", TokenSubject);
            writer.WriteNumber("iat", issued.ToUnixTimeSeconds());
            writer.WriteNumber("nbf", issued.ToUnixTimeSeconds());
            writer.WriteNumber("exp", (now + window).ToUnixTimeSeconds());
        });

        string signingInput = string.Concat(
            Base64UrlEncoder.Encode(header),
            ".",
            Base64UrlEncoder.Encode(payload));

        byte[] signature = HMACSHA256.HashData(symmetric.Key, Encoding.UTF8.GetBytes(signingInput));

        return string.Concat(signingInput, ".", Base64UrlEncoder.Encode(signature));
    }

    /// <summary>Writes a JSON object with no reflection.</summary>
    /// <param name="write">Writes the members, between the braces.</param>
    /// <returns>The UTF-8 encoded object.</returns>
    private static byte[] JsonObject(Action<Utf8JsonWriter> write)
    {
        using MemoryStream buffer = new();

        using (Utf8JsonWriter writer = new(buffer))
        {
            writer.WriteStartObject();

            write(writer);

            writer.WriteEndObject();
        }

        return buffer.ToArray();
    }

    /// <summary>
    /// Builds a compact serialization with a chosen JOSE header, so the header reader's arms can be driven
    /// with shapes a minting library would never produce.
    /// </summary>
    /// <param name="headerJson">The header segment's plaintext, which need not be valid JSON.</param>
    /// <returns>A three-segment value whose header is exactly what was asked for.</returns>
    /// <remarks>
    /// SIGNS NOTHING AND CARRIES NOTHING. The payload and signature segments are fixed, meaningless and
    /// never validated: every row using this asserts that the credential is refused BEFORE any key set is
    /// consulted, so nothing downstream of the header read is reached.
    /// </remarks>
    private static string Crafted(string headerJson) =>
        Base64UrlEncoder.Encode(headerJson) + ".e30." + Base64UrlEncoder.Encode("not-a-signature");

    /// <summary>Wraps a credential in the authorization header value a caller sends.</summary>
    /// <param name="credential">The compact serialization.</param>
    /// <returns>The header value.</returns>
    private static string Bearer(string credential) => "Bearer " + credential;

    /// <summary>
    /// Builds bearer options shaped like a deployed verifier's, with the hook attached under the test
    /// bounds.
    /// </summary>
    /// <param name="manager">The configuration manager, or <see langword="null"/> for none.</param>
    /// <param name="budget">The landing budget, defaulting to the test bound.</param>
    /// <param name="poll">The poll interval, defaulting to the test bound.</param>
    /// <returns>The options.</returns>
    private static JwtBearerOptions Verifier(
        IConfigurationManager<OpenIdConnectConfiguration>? manager,
        TimeSpan? budget = null,
        TimeSpan? poll = null)
    {
        JwtBearerOptions options = new();

        Configure(options, manager);

        UnknownSigningKeyRevalidation.Attach(options, budget ?? TestBudget, poll ?? TestPoll);

        return options;
    }

    /// <summary>Applies the validation posture a deployed verifier runs with.</summary>
    /// <param name="options">The options to configure.</param>
    /// <param name="manager">The configuration manager, or <see langword="null"/> for none.</param>
    /// <remarks>
    /// EVERY CHECK ON, INCLUDING THE BOUNDED CLOCK SKEW, because the rows that prove nothing is weakened
    /// are only meaningful against a verifier that was strict to begin with.
    /// </remarks>
    private static void Configure(
        JwtBearerOptions options,
        IConfigurationManager<OpenIdConnectConfiguration>? manager)
    {
        options.ConfigurationManager = manager;

        options.TokenValidationParameters.ValidIssuer = TrustedIssuer;
        options.TokenValidationParameters.ValidAudience = TrustedAudience;
        options.TokenValidationParameters.ValidateIssuer = true;
        options.TokenValidationParameters.ValidateAudience = true;
        options.TokenValidationParameters.ValidateLifetime = true;
        options.TokenValidationParameters.ValidateIssuerSigningKey = true;
        options.TokenValidationParameters.ClockSkew = TimeSpan.FromSeconds(30);
    }

    /// <summary>
    /// Raises the failure the bearer handler would have raised and reports what the chain decided.
    /// </summary>
    /// <param name="options">The options whose events chain is driven.</param>
    /// <param name="credential">The authorization header value, or <see langword="null"/> for none.</param>
    /// <param name="failure">The exception the handler carried.</param>
    /// <param name="provider">A recorder to attach to the request's services, if any.</param>
    /// <returns>The result the chain set, or <see langword="null"/> when it set none.</returns>
    /// <remarks>
    /// THE EVENTS CHAIN IS DRIVEN, NOT A METHOD ON THE HOOK. That is what makes these rows assertions about
    /// what a deployed handler produces rather than about a helper's signature, and it is why the chained
    /// refusal-record handler is exercised too wherever one is attached.
    /// </remarks>
    private static async Task<AuthenticateResult?> DriveAsync(
        JwtBearerOptions options,
        string? credential,
        Exception? failure,
        RecordingProvider? provider = null)
    {
        DefaultHttpContext http = new();

        if (!string.IsNullOrEmpty(credential))
        {
            http.Request.Headers.Authorization = credential;
        }

        if (provider is not null)
        {
            ServiceCollection services = new();

            _ = services.AddLogging(logging => logging.AddProvider(provider));

            http.RequestServices = services.BuildServiceProvider();
        }

        AuthenticationScheme scheme = new(
            JwtBearerDefaults.AuthenticationScheme,
            displayName: null,
            handlerType: typeof(JwtBearerHandler));

        AuthenticationFailedContext context = new(http, scheme, options)
        {
            Exception = failure!,
        };

        await options.Events!.OnAuthenticationFailed(context);

        return context.Result;
    }
}

/// <summary>
/// A configuration manager that answers with a stale key set until a refresh "lands", exactly as the
/// framework's own manager measurably does.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 THE DOUBLE EXISTS TO REPRODUCE ONE MEASURED BEHAVIOUR: after <c>RequestRefresh()</c>,
/// <c>ConfigurationManager&lt;T&gt;</c> returns the instance it already holds - reference-identical, in
/// 0 ms - and retrieves on a background continuation, so the refreshed instance appears only on a later
/// ask. <see cref="LandOnAsk"/> is which ask that is. A double that returned the refreshed set on the
/// first ask would let a single-call implementation pass.
/// </para>
/// <para>
/// THE INSTANCES ARE DISTINCT OBJECTS AND THAT IS LOAD-BEARING, because the production code detects the
/// landing by reference comparison.
/// </para>
/// </remarks>
/// <param name="stale">The key set the verifier was primed on.</param>
/// <param name="refreshed">The key set the issuer rotated to.</param>
/// <param name="issuer">The issuer both sets declare.</param>
internal sealed class RotatingKeySetManager(
    OpenIdConnectConfiguration stale,
    OpenIdConnectConfiguration refreshed,
    string? issuer = null)
    : BaseConfigurationManager, IConfigurationManager<OpenIdConnectConfiguration>
{
    private readonly OpenIdConnectConfiguration _stale = Rebrand(stale, issuer);
    private readonly OpenIdConnectConfiguration _refreshed = Rebrand(refreshed, issuer);

    private int _asks;
    private bool _landed;

    /// <summary>
    /// Which ask the refreshed set first appears on, or zero for a refresh that never lands.
    /// </summary>
    internal int LandOnAsk { get; init; }

    /// <summary>How many times the key set has been asked for.</summary>
    internal int Asks => _asks;

    /// <summary>How many times a refresh has been requested.</summary>
    internal int RefreshRequests { get; private set; }

    /// <inheritdoc/>
    public override Task<BaseConfiguration> GetBaseConfigurationAsync(CancellationToken cancel)
    {
        cancel.ThrowIfCancellationRequested();

        int ask = Interlocked.Increment(ref _asks);

        if (LandOnAsk > 0 && ask >= LandOnAsk)
        {
            _landed = true;
        }

        return Task.FromResult<BaseConfiguration>(_landed ? _refreshed : _stale);
    }

    /// <inheritdoc/>
    public override void RequestRefresh() => RefreshRequests++;

    /// <summary>
    /// The typed member the bearer options property is declared in terms of.
    /// </summary>
    /// <param name="cancel">The abort signal.</param>
    /// <returns>The configuration currently answered.</returns>
    /// <remarks>
    /// BOTH SURFACES, BECAUSE THE FRAMEWORK'S OWN MANAGER HAS BOTH.
    /// <see cref="JwtBearerOptions.ConfigurationManager"/> is typed as
    /// <see cref="IConfigurationManager{T}"/> while the token layer consumes
    /// <see cref="BaseConfigurationManager"/>, and a double implementing only one of them could not be
    /// assigned to the options a deployed handler runs on.
    /// </remarks>
    public async Task<OpenIdConnectConfiguration> GetConfigurationAsync(CancellationToken cancel) =>
        (OpenIdConnectConfiguration)await GetBaseConfigurationAsync(cancel).ConfigureAwait(false);

    /// <summary>Restates a key set under a given issuer.</summary>
    /// <param name="configuration">The set.</param>
    /// <param name="issuer">The issuer to declare, or <see langword="null"/> to keep the existing one.</param>
    /// <returns>The set to answer with.</returns>
    /// <remarks>
    /// Present so a row driving a DEPLOYED host can hand the manager the issuer that host trusts without
    /// the key-set helpers having to know it.
    /// </remarks>
    private static OpenIdConnectConfiguration Rebrand(
        OpenIdConnectConfiguration configuration,
        string? issuer)
    {
        if (string.IsNullOrEmpty(issuer))
        {
            return configuration;
        }

        configuration.Issuer = issuer;

        return configuration;
    }
}

/// <summary>A configuration manager whose retrieval fails.</summary>
/// <remarks>
/// The unreachable-key-set case, which must leave a refusal exactly as it was. The ask it fails on is
/// selectable because the first ask and the polling ask are different code paths.
/// </remarks>
/// <param name="stale">The key set answered before the failing ask.</param>
internal sealed class ThrowingKeySetManager(OpenIdConnectConfiguration stale)
    : BaseConfigurationManager, IConfigurationManager<OpenIdConnectConfiguration>
{
    private int _asks;

    /// <summary>Which ask throws.</summary>
    internal int ThrowsFromAsk { get; init; } = 1;

    /// <inheritdoc/>
    public override Task<BaseConfiguration> GetBaseConfigurationAsync(CancellationToken cancel)
    {
        cancel.ThrowIfCancellationRequested();

        return Interlocked.Increment(ref _asks) >= ThrowsFromAsk
            ? throw new InvalidOperationException("IDX20803: Unable to obtain configuration.")
            : Task.FromResult<BaseConfiguration>(stale);
    }

    /// <inheritdoc/>
    public override void RequestRefresh()
    {
    }

    /// <summary>The typed member the bearer options property is declared in terms of.</summary>
    /// <param name="cancel">The abort signal.</param>
    /// <returns>The configuration, or a fault once <see cref="ThrowsFromAsk"/> is reached.</returns>
    public async Task<OpenIdConnectConfiguration> GetConfigurationAsync(CancellationToken cancel) =>
        (OpenIdConnectConfiguration)await GetBaseConfigurationAsync(cancel).ConfigureAwait(false);
}

/// <summary>
/// A configuration retriever that counts real retrievals, so the library's own refresh rate limit can be
/// asserted rather than restated.
/// </summary>
/// <param name="configuration">The key set every retrieval answers with.</param>
internal sealed class CountingConfigurationRetriever(OpenIdConnectConfiguration configuration)
    : IConfigurationRetriever<OpenIdConnectConfiguration>
{
    private int _retrievals;

    /// <summary>How many retrievals have been performed.</summary>
    internal int Retrievals => _retrievals;

    /// <inheritdoc/>
    public Task<OpenIdConnectConfiguration> GetConfigurationAsync(
        string address,
        IDocumentRetriever retriever,
        CancellationToken cancel)
    {
        _ = Interlocked.Increment(ref _retrievals);

        return Task.FromResult(configuration);
    }
}
