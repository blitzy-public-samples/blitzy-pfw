// ==================================================================================================
//  ScopeAuthorization.cs - THE SCOPE CLAIM IS CHECKED, NOT MERELY MINTED
//  ------------------------------------------------------------------------------------------------
//  WHAT THIS FILE IS FOR
//  Security mints a `scope` claim on every token it issues and publishes a `403` on thirteen of its
//  operations whose description says, in so many words, that the token is valid but does not carry the
//  scope the operation requires. Until this file existed nothing in the service read that claim: every
//  authenticated route was reachable by any token addressed to this service, whatever it was scoped
//  to. The claim was decorative, and a caller granted `security.crypto` for one purpose held the whole
//  compatibility surface.
//
//  WHY THE FRAMEWORK'S OWN CLAIM REQUIREMENT IS NOT ENOUGH
//  `RequireClaim("scope", value)` compares the claim's WHOLE value against each candidate. The scope
//  claim is a SINGLE claim carrying a SPACE-DELIMITED SET - `TokenIssuer` joins the granted set with
//  one space and stamps one claim, which is the form every stock bearer consumer expects - so a token
//  granted `security.crypto security.audit` carries the single value "security.crypto security.audit"
//  and `RequireClaim("scope", "security.crypto")` REFUSES it. The failure direction happens to be the
//  safe one, but it makes a correctly scoped multi-scope caller unable to call anything, so the set
//  must be split before it is compared. That is this file's entire reason for existing.
//
//  WHY IT IS DUPLICATED IN EACH SERVICE RATHER THAN SHARED
//  The migration plan permits exactly one cross-service coupling - the published contracts project -
//  and states that it carries no behaviour and is "not a shared-code back door" [AAP 0.4.2.3], with the
//  enterprise baseline restating that no shared behaviour crosses a service boundary [AAP 0.7.2]. An
//  authorization handler is behaviour. Four small independent copies is the shape the plan mandates,
//  and it is also the shape that lets one service tighten its own policy without a coordinated release
//  of the other three. Each copy names only ITS OWN service's scopes.
//
//  WHY THE SCOPE NAMES ARE NOT INVENTED HERE
//  `security.crypto` is read from the only caller that requests it - DataServices' own SecurityClient
//  declares it as the scope it asks for when it obtains a token for this service. Every other service's
//  vocabulary is likewise read from the client that consumes it. No name in this file was chosen for
//  its shape; each is the name a shipped caller already sends.
//
//  WHAT IS DELIBERATELY *NOT* SCOPE-GATED, AND WHY THAT IS A DECISION RATHER THAN AN OMISSION
//  `/health`, `/.well-known/jwks.json` and `/.well-known/openid-configuration` are the service's three
//  anonymous routes; they reach no scope check because they reach no authentication. `/v1/ping` is
//  authenticated and deliberately carries NO scope requirement: it performs no work, reaches no
//  capability and returns no data, so a scope for it would be a permission over nothing - and the
//  published document declares no `403` on it, so requiring one would produce a status the contract
//  does not describe. `POST /v1/tokens` is authenticated by mutual TLS and carries no bearer token at
//  all, so it has no scope claim to check; its own authorization is the caller-and-audience matrix in
//  Tokens/TokenIssuer.cs.
//
//  RULES
//  review_rules reports that no user rules were provided, so no user-specified rule governs this file.
// ==================================================================================================

using System.Collections.Frozen;
using System.Security.Claims;

using Microsoft.AspNetCore.Authorization;

namespace PowerFramework.Security.Authorization;

/// <summary>
/// The scope names this service's own operations require, and the policy names that carry them.
/// </summary>
/// <remarks>
/// <para>
/// ONE SCOPE, BECAUSE ONE CAPABILITY IS REACHED. This service publishes exactly one bearer-authenticated
/// capability area - the C-02 compatibility surface under <c>/v1/crypto</c> - so it declares exactly one
/// scope. Splitting it further (a read scope, a key scope) would invent a vocabulary no caller sends and
/// no document publishes, and every one of those names would then have to be added to the issuer's
/// authorization matrix before any caller could use the surface at all.
/// </para>
/// <para>
/// THE POLICY NAME AND THE SCOPE NAME ARE DELIBERATELY THE SAME STRING. A policy name is an internal
/// registry key and a scope is a wire value, so they could differ - but keeping them equal means a
/// registration and its requirement cannot drift apart, and a diagnostic naming the policy names the
/// scope an operator has to grant.
/// </para>
/// </remarks>
public static class SecurityScopes
{
    /// <summary>
    /// The scope required by every operation on the C-02 cryptographic compatibility surface.
    /// </summary>
    /// <remarks>
    /// Read from the caller that sends it: DataServices' <c>SecurityClient</c> declares this exact string
    /// as the scope it requests when obtaining a token for this service, and Security's own
    /// <c>appsettings.json</c> authorises it for that caller. Three files, one value, and this is the one
    /// the operation checks.
    /// </remarks>
    public const string Crypto = "security.crypto";

    /// <summary>
    /// The scope required by the authenticated probe.
    /// </summary>
    /// <remarks>
    /// <b>DECLARED HERE BECAUSE A ROUTE ACTUALLY REQUIRES IT, AND OMITTING IT WAS NOT A HARMLESS GAP.</b>
    /// The probe route names its scope policy on its own registration, and this list is what the policies
    /// are registered FROM - so while this member was missing, every request to that route reached an
    /// unregistered policy name and the framework's answer to that is an unhandled
    /// <c>InvalidOperationException</c>. The route did not fail closed with a refusal; it failed open with
    /// a 500, which no authorization row and no anonymity row could tell apart from a server fault.
    /// </remarks>
    public const string Ping = "ping";

    /// <summary>Every scope this service's own routes require, so a test can assert the set is complete.</summary>
    /// <remarks>
    /// Exposed as data rather than described in prose so that a row asserting "every declared scope is
    /// registered" reads the same list the registration does, and a scope added without a registration
    /// fails a test rather than a request.
    /// </remarks>
    public static IReadOnlyList<string> All { get; } = [Crypto, Ping];

    /// <summary>
    /// The authorization-policy name carrying one scope's requirement.
    /// </summary>
    /// <param name="scope">The scope the policy demands.</param>
    /// <returns>The registry key the policy is registered under and a route requires.</returns>
    /// <remarks>
    /// <para>
    /// <b>ONE COMPOSITION, SO A REGISTRATION AND A REQUIREMENT CANNOT DRIFT APART.</b> A policy registered
    /// under a name no route requires enforces nothing while looking correct in review, and a route
    /// requiring a name nobody registered answers 500. Both happened: the registration used the BARE scope
    /// string as its key while the routes composed a prefixed one, so the two never met. Deriving the name
    /// in exactly one place is what makes that class of mismatch unexpressible.
    /// </para>
    /// <para>
    /// The prefix is retained rather than dropped in favour of the bare scope, because a policy name is an
    /// internal registry key and a scope is a wire value: keeping them textually distinct means a reader
    /// cannot mistake one for the other, and a diagnostic naming the policy still ENDS WITH the scope an
    /// operator has to grant.
    /// </para>
    /// </remarks>
    public static string PolicyNameFor(string scope)
    {
        ArgumentNullException.ThrowIfNull(scope);

        return PolicyNamePrefix + scope;
    }

    /// <summary>The prefix every scope policy's registry key carries.</summary>
    internal const string PolicyNamePrefix = "security:scope:";
}

/// <summary>
/// A requirement that the caller's <c>scope</c> claim contains one particular scope.
/// </summary>
/// <param name="scope">The scope the caller must carry.</param>
/// <remarks>
/// A requirement type rather than an inline assertion, because an assertion closure cannot be inspected:
/// this way a test can read back which scope a registered policy demands, and a diagnostic can name it.
/// </remarks>
internal sealed class ScopeRequirement(string scope) : IAuthorizationRequirement
{
    /// <summary>The scope the caller must carry.</summary>
    public string Scope { get; } = scope;
}

/// <summary>
/// Decides a <see cref="ScopeRequirement"/> by splitting the space-delimited <c>scope</c> claim.
/// </summary>
/// <remarks>
/// <para>
/// ORDINAL, AND THAT MATCHES EVERY OTHER IDENTITY COMPARISON IN THIS SYSTEM - the audience roster, the
/// signing key identifier, the certificate common-name reconciliation and the issuer's own authorization
/// matrix. A case-insensitive comparison here would admit a scope spelling the issuer never granted,
/// because the issuer's matrix compares ordinally: the two layers would then disagree about what a token
/// carries, and the disagreement would only be visible as an operation succeeding that should not have.
/// </para>
/// <para>
/// IT DOES NOT SUCCEED WHEN THE CLAIM IS ABSENT. Every token this system issues carries the claim, so an
/// absent one means either a token from another issuer or a claim that was dropped in transit - neither
/// of which is a reason to grant. The handler simply returns without calling succeed, which the framework
/// reads as a refusal; there is no arm that treats a missing claim as a wildcard.
/// </para>
/// <para>
/// MULTIPLE CLAIMS ARE ALL CONSIDERED, not just the first. One claim carrying a space-delimited set is
/// what this system's issuer produces, but a token minted elsewhere may repeat the claim instead, and
/// both spellings are legal in the wild. Reading every claim costs nothing and removes a silent
/// dependency on the issuer's serialization choice.
/// </para>
/// </remarks>
internal sealed class ScopeHandler : AuthorizationHandler<ScopeRequirement>
{
    /// <summary>The delimiter the scope claim's set is joined with.</summary>
    /// <remarks>
    /// A space, matching the issuer that produces the claim. Declared here as well because this handler
    /// must be able to split a claim minted by any conforming issuer, not only by the sibling file.
    /// </remarks>
    private const char Delimiter = ' ';

    /// <inheritdoc/>
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ScopeRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(requirement);

        foreach (Claim claim in context.User.FindAll(ScopeClaimName))
        {
            foreach (Range range in Split(claim.Value))
            {
                if (claim.Value.AsSpan()[range].SequenceEqual(requirement.Scope))
                {
                    context.Succeed(requirement);

                    return Task.CompletedTask;
                }
            }
        }

        // No succeed call and no fail call: the requirement is simply unmet, which the framework reports
        // as a refusal. Fail() is deliberately not used - it would poison every OTHER requirement in the
        // same policy evaluation, including ones a future policy might legitimately satisfy another way.
        return Task.CompletedTask;
    }

    /// <summary>The claim name the published contract uses for the granted scope set.</summary>
    /// <remarks>
    /// The bare wire spelling rather than a framework constant, because the inbound handler is configured
    /// with claim mapping switched off so that the claim reaches this code named exactly as the token
    /// spells it. A mapped name would silently stop matching if that setting changed.
    /// </remarks>
    private const string ScopeClaimName = "scope";

    /// <summary>Splits a space-delimited claim value into its entries, allocating nothing.</summary>
    /// <param name="value">The claim value.</param>
    /// <returns>The ranges of the non-empty entries.</returns>
    /// <remarks>
    /// Ranges rather than substrings because this runs on every authenticated request: the comparison
    /// needs no string, so none is created. Empty entries are skipped, which is what makes a value with a
    /// doubled or trailing delimiter behave the same as a well-formed one rather than matching an empty
    /// required scope - a required scope is never empty, but relying on that from here would be a
    /// dependency on another file's validation.
    /// </remarks>
    private static IEnumerable<Range> Split(string value)
    {
        int start = 0;

        for (int index = 0; index <= value.Length; index++)
        {
            if (index != value.Length && value[index] != Delimiter)
            {
                continue;
            }

            if (index > start)
            {
                yield return new Range(start, index);
            }

            start = index + 1;
        }
    }
}

/// <summary>
/// Registers this service's scope policies and the handler that decides them.
/// </summary>
/// <remarks>
/// An extension rather than inline registration so that the composition root reads as a list of
/// decisions and the mechanism lives beside the requirement it serves.
/// </remarks>
public static class ScopeAuthorizationExtensions
{
    /// <summary>
    /// Adds the scope handler and one named policy per scope in <see cref="SecurityScopes.All"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// EACH POLICY REQUIRES AN AUTHENTICATED USER AS WELL AS THE SCOPE, even though the fallback policy
    /// already requires one. A named policy REPLACES the fallback for the route that names it, so
    /// omitting the authentication requirement here would make a scope-gated route the only route in the
    /// service that did not demand authentication - and it would still appear to work, because an
    /// unauthenticated principal carries no scope claim and would be refused anyway. It would be refused
    /// with the WRONG status: a 403 rather than the published 401, telling a caller with no credential
    /// that its credential was insufficient.
    /// </para>
    /// <para>
    /// The policies are built from the declared list rather than written out one by one, so a scope added
    /// to that list cannot be left unregistered.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddScopeAuthorization(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IAuthorizationHandler, ScopeHandler>();

        AuthorizationBuilder builder = services.AddAuthorizationBuilder();

        foreach (string scope in SecurityScopes.All)
        {
            // REGISTERED UNDER THE NAME THE ROUTES ACTUALLY REQUIRE. This used to register under the bare
            // scope string while every route composed the prefixed one, so no route ever reached one of
            // these policies and the probe route reached no policy at all.
            builder.AddPolicy(
                SecurityScopes.PolicyNameFor(scope),
                policy => policy
                    .RequireAuthenticatedUser()
                    .AddRequirements(new ScopeRequirement(scope)));
        }

        return services;
    }

    /// <summary>The scopes each registered policy demands, for assertion.</summary>
    /// <remarks>
    /// Frozen and ordinal, and derived from the same declared list the registration walks, so a test
    /// comparing the two is comparing the registration against the declaration rather than against a
    /// restatement of it.
    /// </remarks>
    internal static FrozenSet<string> RegisteredScopes { get; } =
        SecurityScopes.All.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>The policy names each registered scope is reachable under, for assertion.</summary>
    internal static FrozenSet<string> RegisteredPolicyNames { get; } =
        SecurityScopes.All.Select(SecurityScopes.PolicyNameFor).ToFrozenSet(StringComparer.Ordinal);
}
