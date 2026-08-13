// ==================================================================================================
//  ScopeAuthorization.cs - THE INGRESS CHECKS THE SCOPE IT PUBLISHES A 403 FOR
//  ------------------------------------------------------------------------------------------------
//  WHAT THIS FILE IS FOR
//  gateway.v1.yaml declares a `403` on forty of this service's operations, and its shared
//  `Forbidden` component says what that status means: the token is valid but does not carry the scope the
//  operation requires, "deliberately distinguished from `401` so a caller can tell a missing credential
//  from an insufficient one". Until this file existed nothing here read the scope claim. Every
//  authenticated route was reachable by any token addressed to this service, whatever it was scoped to,
//  so the published 403 could only ever have been produced by PROJECTING one from DataServices - and
//  DataServices was not producing one either. A caller's scope set was decorative at the one boundary in
//  the whole system that external clients can reach.
//
//  THE VOCABULARY IS THE INGRESS ONE, AND IT IS UNPREFIXED FOR A REASON
//  The three scope names below are read from the only consumer that sends them
//  [tests/e2e/fixtures/auth.ts:250-252], where they are described as "one entry per Gateway surface the
//  specs actually call". They are deliberately NOT prefixed with a service name, unlike every internal
//  service's vocabulary (`dataservices.datawindow`, `persistence.read`, `security.crypto`): the ingress
//  vocabulary is what an EXTERNAL client sees and reasons about, so it names capabilities rather than
//  the service that happens to implement them, while the internal names are namespaced by the service
//  that owns them. Both conventions are evidenced in the shipped code, and this file follows the one
//  that belongs to its own edge.
//
//  WHY THE FRAMEWORK'S OWN CLAIM REQUIREMENT IS NOT ENOUGH
//  `RequireClaim("scope", value)` compares the claim's WHOLE value. The scope claim is a SINGLE claim
//  carrying a SPACE-DELIMITED set - which is what Security's issuer produces and what the end-to-end
//  suite sends, three scopes in one credential - so `RequireClaim` would refuse every real caller. The
//  failure would be in the safe direction and would still mean the ingress served nothing.
//
//  WHY IT IS DUPLICATED IN EACH SERVICE RATHER THAN SHARED
//  The migration plan permits exactly one cross-service coupling - the published contracts project - and
//  states that it carries no behaviour and is not a shared-code back door [AAP 0.4.2.3, 0.7.2].
//
//  WHAT IS DELIBERATELY NOT SCOPE-GATED, AND WHY EACH IS A DECISION
//  `/health` is anonymous (C-10 requires it, and three services gate their readiness on it), so it
//  reaches no scope check because it reaches no authentication. THE FOUR RESERVED DEFERRED ROUTES are
//  authenticated and carry NO scope requirement: they reach no capability by construction - each answers
//  `501` naming the service that will eventually serve it - so a scope there would be a permission over a
//  feature that does not exist, and requiring one would turn the published `501` into a `403` for a
//  caller whose only fault is not holding a permission to nothing.
//
//  RULES
//  review_rules reports that no user rules were provided, so no user-specified rule governs this file.
// ==================================================================================================

using System.Collections.Frozen;
using System.Security.Claims;

using Microsoft.AspNetCore.Authorization;

namespace PowerFramework.Gateway.Authorization;

/// <summary>
/// The ingress scope vocabulary, and the policy names that carry it.
/// </summary>
/// <remarks>
/// Each value is read from the only consumer that sends it rather than chosen here, and each names a
/// capability this service exposes rather than the service behind it.
/// </remarks>
public static class GatewayScopes
{
    /// <summary>The scope required by the authenticated probe.</summary>
    /// <remarks>
    /// <para>
    /// THE ONE PING PROBE IN THE SYSTEM THAT IS SCOPE-GATED, AND THE ASYMMETRY IS DELIBERATE. The three
    /// internal services' probes require authentication and no scope, because no consumer anywhere names
    /// a scope for them and their published documents declare no `403` on them - inventing three names to
    /// look symmetrical would be exactly the invention this refactor avoids. This one is different on both
    /// counts: the end-to-end suite requests it by name as one of the surfaces it calls, and this is the
    /// INGRESS, so the probe is reachable by an external client rather than only by a sibling service that
    /// already holds every scope. Gating it is real least privilege here and would be theatre there.
    /// </para>
    /// </remarks>
    public const string Ping = "ping";

    /// <summary>The scope required to read the capability gate's projection.</summary>
    public const string Capabilities = "capabilities";

    /// <summary>
    /// The scope required by every projected DataWindow operation, the expression sub-surface included.
    /// </summary>
    /// <remarks>
    /// ONE SCOPE FOR BOTH PROJECTED CONTRACTS, WHICH IS NOT WHAT DATASERVICES DOES, AND THE DIFFERENCE IS
    /// THE POINT. DataServices splits C-03 and C-04 into two scopes because the two gRPC services version
    /// independently and a caller may hold one contract without the other. At the ingress they are one
    /// capability area under one path prefix, and the only consumer requests one name for all of it. So the
    /// ingress does not re-express an internal versioning boundary as an external permission - it gates
    /// what it publishes, and the projection layer presents its own credential downstream.
    /// </remarks>
    public const string DataWindow = "datawindow";

    /// <summary>Every policy this service registers, so a test can assert the set is complete.</summary>
    public static IReadOnlyList<string> All { get; } = [Ping, Capabilities, DataWindow];
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
/// here would admit a scope spelling the issuer never granted.
/// </para>
/// <para>
/// AN ABSENT CLAIM DOES NOT SUCCEED. Every token this system issues carries the claim, so an absent one
/// means a token from another issuer or a claim dropped in transit - neither a reason to grant.
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
        // Fail() would poison every other requirement in the same evaluation.
        return Task.CompletedTask;
    }

    /// <summary>Splits a space-delimited claim value into its entries, allocating nothing.</summary>
    /// <param name="value">The claim value.</param>
    /// <returns>The ranges of the non-empty entries.</returns>
    /// <remarks>
    /// Ranges rather than substrings because this runs on every authenticated request at the system's only
    /// ingress: the comparison needs no string, so none is created. Empty entries are skipped so a doubled
    /// or trailing delimiter behaves like a well-formed value.
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
public static class ScopeAuthorizationExtensions
{
    /// <summary>
    /// Adds the scope handler and one named policy per scope in <see cref="GatewayScopes.All"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// EACH POLICY REQUIRES AN AUTHENTICATED USER AS WELL AS THE SCOPE, even though the fallback policy
    /// already requires one. A named policy REPLACES the fallback for the endpoint that names it, so
    /// omitting the authentication requirement would make a scope-gated route the only one in the service
    /// not demanding authentication - and it would still appear to work, because an unauthenticated
    /// principal carries no scope claim and would be refused anyway. It would be refused with the wrong
    /// status: the 403 this service publishes for an INSUFFICIENT credential, given to a caller that
    /// presented none, which is the precise confusion the published contract says the two statuses exist
    /// to prevent.
    /// </remarks>
    public static IServiceCollection AddScopeAuthorization(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IAuthorizationHandler, ScopeHandler>();

        AuthorizationBuilder builder = services.AddAuthorizationBuilder();

        foreach (string scope in GatewayScopes.All)
        {
            builder.AddPolicy(
                scope,
                policy => policy
                    .RequireAuthenticatedUser()
                    .AddRequirements(new ScopeRequirement(scope)));
        }

        return services;
    }

    /// <summary>The scopes each registered policy demands, for assertion.</summary>
    internal static FrozenSet<string> RegisteredScopes { get; } =
        GatewayScopes.All.ToFrozenSet(StringComparer.Ordinal);
}
