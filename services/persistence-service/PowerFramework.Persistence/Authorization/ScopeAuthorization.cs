// ==================================================================================================
//  ScopeAuthorization.cs - THE SCOPE CLAIM IS CHECKED, NOT MERELY MINTED
//  ------------------------------------------------------------------------------------------------
//  WHAT THIS FILE IS FOR
//  Every token reaching this service carries a `scope` claim, and its only caller requests two distinct
//  scopes with a documented split - `persistence.read` for "C-05 and the C-08 reads" and
//  `persistence.write` for "C-06, C-07 and the C-08 state changes"
//  [DataServices/Clients/PersistenceClient.cs:438,441]. Until this file existed nothing read that
//  claim: all four contracts were reachable by any token addressed to this service, so a credential
//  obtained to RETRIEVE rows could also update them, execute arbitrary SQL and commit or roll back a
//  transaction. The split the caller declares was a comment rather than a boundary.
//
//  AND THE CALLER'S IDENTITY IS CHECKED IN THE SAME PLACE
//  A scope answers what a credential is FOR, not WHO holds it. This system's issuer serves four
//  services, so a scope-only boundary admits any of them that asks for the right scope - and the
//  evidenced call graph has exactly one caller reaching this service. Every policy registered below
//  therefore composes THREE requirements: authenticated, the operation's own scope, and a caller
//  identity on this deployment's `Jwt:PermittedCallers` roster. They are composed in ONE registrar
//  deliberately: a named policy REPLACES any policy already registered under the same name, so two
//  registrars naming their policies differently would leave the route table pointing at one of them
//  while the other enforced nothing anyone could reach.
//
//  WHY THE FRAMEWORK'S OWN CLAIM REQUIREMENT IS NOT ENOUGH
//  `RequireClaim("scope", value)` compares the claim's WHOLE value. The scope claim is a SINGLE claim
//  carrying a SPACE-DELIMITED set - which is exactly what this service's only caller sends, because it
//  requests both scopes in one token and attaches that one credential to every call it makes - so
//  `RequireClaim("scope", "persistence.read")` would refuse a token granted "persistence.read
//  persistence.write" and this service would answer nothing at all. The set must be split before it is
//  compared.
//
//  WHY IT IS DUPLICATED IN EACH SERVICE RATHER THAN SHARED
//  The migration plan permits exactly one cross-service coupling - the published contracts project -
//  and states that it carries no behaviour and is not a shared-code back door [AAP 0.4.2.3, 0.7.2]. An
//  authorization handler is behaviour, so each service carries its own and names only its own scopes.
//
//  THE ASSIGNMENT, AND WHERE EACH HALF COMES FROM
//    C-05 QueryService       -> read.  Retrieval and its configuration. Nothing here changes stored
//                                     state: the clause and paging setters mutate the TASK.
//    C-06 UpdateService      -> write. The optimistic-concurrency update path.
//    C-07 CommandService     -> write. `Exec` runs caller-supplied SQL, which is the broadest write in
//                                     the system whatever statement it happens to carry.
//    C-08 TransactionService -> BOTH, per method. Its thirteen methods divide into observers and
//                                     mutators, and the caller's own comments name that division. This
//                                     is the one contract where a service-wide policy would be wrong in
//                                     one direction or the other: `write` everywhere would deny a
//                                     read-only caller the ability to ask whether it is connected, and
//                                     `read` everywhere would let it commit.
//
//  RULES
//  review_rules reports that no user rules were provided, so no user-specified rule governs this file.
// ==================================================================================================

using System.Collections.Frozen;
using System.Security.Claims;

using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

using PowerFramework.Persistence.Configuration;

namespace PowerFramework.Persistence.Authorization;

/// <summary>
/// The scope names this service's contracts require, and the policy names that carry them.
/// </summary>
/// <remarks>
/// Both values are read from the caller that sends them - DataServices' <c>PersistenceClient</c>
/// declares this exact pair as the scopes it requests for this edge, and Security's
/// <c>appsettings.json</c> authorises that caller for exactly them. Three files, two values, and these
/// are the ones the contracts check.
/// </remarks>
public static class PersistenceScopes
{
    /// <summary>The scope required to retrieve: contract C-05 and the observing half of C-08.</summary>
    public const string Read = "persistence.read";

    /// <summary>
    /// The scope required to change stored state: contracts C-06 and C-07, and the mutating half of
    /// C-08.
    /// </summary>
    /// <remarks>
    /// IT DOES NOT IMPLY <see cref="Read"/>, DELIBERATELY. A hierarchy would be a policy of this file's
    /// own invention: the caller requests both scopes explicitly and the issuer grants both explicitly,
    /// so nothing in the system needs an implication - and adding one would silently widen every token
    /// that carries the write scope, which is the opposite of what a scope split is for.
    /// </remarks>
    public const string Write = "persistence.write";

    /// <summary>Every policy this service registers, so a test can assert the set is complete.</summary>
    public static IReadOnlyList<string> All { get; } = [Read, Write];
}

/// <summary>
/// A requirement that the caller's <c>scope</c> claim contains one particular scope.
/// </summary>
/// <param name="scope">The scope the caller must carry.</param>
/// <remarks>
/// A requirement type rather than an inline assertion, so a test can read back which scope a registered
/// policy demands and a diagnostic can name it.
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
/// ORDINAL, matching every other identity comparison in this system - the audience, the issuer, the
/// signing key identifier and the token issuer's own authorization matrix. A case-insensitive comparison
/// here would admit a scope spelling the issuer never granted, and the two layers would then disagree
/// about what a token carries in the one direction that is unsafe.
/// </para>
/// <para>
/// AN ABSENT CLAIM DOES NOT SUCCEED. Every token this system issues carries the claim, so an absent one
/// means a token from another issuer or a claim dropped in transit - neither a reason to grant. There is
/// no arm that reads a missing claim as a wildcard.
/// </para>
/// </remarks>
internal sealed class ScopeHandler : AuthorizationHandler<ScopeRequirement>
{
    /// <summary>The delimiter the scope claim's set is joined with.</summary>
    private const char Delimiter = ' ';

    /// <summary>The claim name the published contract uses for the granted scope set.</summary>
    /// <remarks>
    /// The bare wire spelling rather than a framework constant, because the inbound handler is configured
    /// with claim mapping switched off so the claim arrives named exactly as the token spells it.
    /// </remarks>
    private const string ScopeClaimName = "scope";

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

        // Neither succeed nor fail: the requirement is unmet, which the framework reports as a refusal.
        // Fail() would poison every other requirement in the same evaluation, including ones a future
        // policy might legitimately satisfy another way.
        return Task.CompletedTask;
    }

    /// <summary>Splits a space-delimited claim value into its entries, allocating nothing.</summary>
    /// <param name="value">The claim value.</param>
    /// <returns>The ranges of the non-empty entries.</returns>
    /// <remarks>
    /// Ranges rather than substrings because this runs on every authenticated call: the comparison needs
    /// no string, so none is created. Empty entries are skipped so a doubled or trailing delimiter
    /// behaves like a well-formed value.
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
/// A requirement that the caller's identity appears on this deployment's permitted-caller roster.
/// </summary>
/// <remarks>
/// <para>
/// THE SECOND HALF OF THE BOUNDARY, AND NEITHER HALF IS SUFFICIENT ALONE. The scope requirement above
/// asks what the credential is FOR; this one asks WHO it was minted for. Scope without subject admits
/// any caller the issuer serves as long as it holds the scope - and this system's issuer serves four
/// services. Subject without scope lets the one permitted caller do anything once it is in. Both are
/// therefore composed into every policy this file registers, which is the only place either is applied
/// (CWE-862 missing authorization, CWE-863 incorrect authorization).
/// </para>
/// <para>
/// A REQUIREMENT TYPE RATHER THAN AN INLINE ASSERTION, for the same reason as the scope requirement: a
/// test can read back from the registered policy that the roster check is present, which an assertion
/// closure cannot express, and a diagnostic can name the requirement that refused.
/// </para>
/// </remarks>
internal sealed class PermittedCallerRequirement : IAuthorizationRequirement;

/// <summary>
/// Decides a <see cref="PermittedCallerRequirement"/> against <c>Jwt:PermittedCallers</c>.
/// </summary>
/// <param name="options">The live options monitor for this service's bound configuration.</param>
/// <remarks>
/// <para>
/// THE ROSTER IS READ FROM THE CONTAINER, NOT FROM <c>context.Resource</c>. An earlier form of this
/// check cast the authorization resource to <see cref="HttpContext"/> and refused whenever the cast
/// failed - which is correct for a route but silently refuses every OTHER shape the framework evaluates
/// a policy with, including <see langword="null"/>. That matters here: authorization is also evaluated
/// directly through <c>IAuthorizationService</c> with no resource at all, and a check that could not see
/// its own configuration in that case would answer "not permitted" for a reason no caller could act on.
/// Injecting the monitor removes the dependency on the resource entirely.
/// </para>
/// <para>
/// A MONITOR RATHER THAN A SNAPSHOT, so a deployment that reloads its configuration is honoured without
/// a restart and so the handler holds no closed-over state a later edit could make stale.
/// </para>
/// <para>
/// AN EMPTY ROSTER REFUSES EVERY CALLER rather than admitting every caller. The options contract
/// requires at least one entry and the startup gate enforces it, so an empty roster cannot occur in a
/// deployment that started - but a permissive reading of an empty list is exactly the fail-open default
/// this requirement exists to remove, so the refusing arm is written rather than left implied.
/// </para>
/// <para>
/// THE SUBJECT IS READ FROM EITHER SPELLING, deliberately. A bearer handler with inbound claim mapping
/// ON renames <c>sub</c> to the framework's name-identifier claim type, and with it OFF leaves <c>sub</c>
/// alone. Both are legitimate configurations and this service must not silently stop enforcing identity
/// because of one. Whichever is present is compared ORDINALLY, matching how every other identity in this
/// system - the audience, the issuer, the signing key identifier and the scope - is compared.
/// </para>
/// </remarks>
internal sealed class PermittedCallerHandler(IOptionsMonitor<PersistenceOptions> options)
    : AuthorizationHandler<PermittedCallerRequirement>
{
    /// <summary>The claim the issuer stamps the caller identity into.</summary>
    private const string SubjectClaimName = "sub";

    /// <inheritdoc/>
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermittedCallerRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(requirement);

        IList<string> permitted = options.CurrentValue.Jwt.PermittedCallers;

        if (permitted.Count == 0)
        {
            return Task.CompletedTask;
        }

        string? subject = context.User.FindFirst(SubjectClaimName)?.Value
            ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrEmpty(subject))
        {
            return Task.CompletedTask;
        }

        foreach (string identity in permitted)
        {
            if (string.Equals(identity, subject, StringComparison.Ordinal))
            {
                context.Succeed(requirement);

                return Task.CompletedTask;
            }
        }

        // Neither succeed nor fail, for the same reason the scope handler does not fail: Fail() poisons
        // every other requirement in the same evaluation, and an unmet requirement is already a refusal.
        return Task.CompletedTask;
    }
}

/// <summary>
/// Registers this service's scope policies and the handler that decides them.
/// </summary>
public static class ScopeAuthorizationExtensions
{
    /// <summary>
    /// Adds the scope handler and one named policy per scope in <see cref="PersistenceScopes.All"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// EACH POLICY REQUIRES AN AUTHENTICATED USER AS WELL AS THE SCOPE, even though the fallback policy
    /// already requires one. A named policy REPLACES the fallback for the endpoint that names it, so
    /// omitting the authentication requirement would make a scope-gated method the only one in the
    /// service not demanding authentication - and it would still appear to work, because an
    /// unauthenticated principal carries no scope claim and would be refused anyway. It would be refused
    /// with the wrong status: <c>PermissionDenied</c> rather than <c>Unauthenticated</c>, which Gateway
    /// projects as 403 rather than the published 401.
    /// </para>
    /// <para>
    /// <b>AND EACH POLICY REQUIRES A PERMITTED CALLER.</b> The two halves are composed HERE, in ONE
    /// registrar, rather than registered as two policy families. A policy family per half is the shape
    /// that fails silently: <see cref="AuthorizationOptions.AddPolicy(string, Action{AuthorizationPolicyBuilder})"/>
    /// REPLACES any policy already registered under the same name, and two families registered under two
    /// different naming conventions leave the route table naming one of them - so whichever family the
    /// endpoints happen to name becomes the whole boundary, the other enforces nothing anyone reaches,
    /// and both read as live. Composing them means a route names one string and gets both requirements.
    /// </para>
    /// <para>
    /// THE POLICY NAME IS THE SCOPE NAME, which removes a translation table rather than adding one. The
    /// same string is requested by the caller
    /// [<c>services/dataservices-service/PowerFramework.DataServices/Clients/PersistenceClient.cs</c>],
    /// minted into the <c>scope</c> claim by the issuer
    /// [<c>services/security-service/PowerFramework.Security/Tokens/TokenIssuer.cs</c>], named by each
    /// contract's <c>[Authorize]</c> attribute, and compared here. One spelling, four places.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddScopeAuthorization(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IAuthorizationHandler, ScopeHandler>();
        services.AddSingleton<IAuthorizationHandler, PermittedCallerHandler>();

        AuthorizationBuilder builder = services.AddAuthorizationBuilder();

        foreach (string scope in PersistenceScopes.All)
        {
            builder.AddPolicy(
                scope,
                policy => policy
                    .RequireAuthenticatedUser()
                    .AddRequirements(new ScopeRequirement(scope))
                    .AddRequirements(new PermittedCallerRequirement()));
        }

        return services;
    }

    /// <summary>The scopes each registered policy demands, for assertion.</summary>
    internal static FrozenSet<string> RegisteredScopes { get; } =
        PersistenceScopes.All.ToFrozenSet(StringComparer.Ordinal);
}
