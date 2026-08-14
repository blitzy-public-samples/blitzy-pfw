// ==================================================================================================
//  ScopeAuthorization.cs - THE SCOPE CLAIM IS CHECKED, NOT MERELY MINTED
//  ------------------------------------------------------------------------------------------------
//  WHAT THIS FILE IS FOR
//  Every token reaching this service carries a `scope` claim, and its only caller requests two distinct
//  scopes - one per contract [Gateway/Clients/DataServicesClient.cs:1286,1289]. Gateway's own published
//  contract goes further and declares a `403` on all thirty-nine of its `/v1/datawindow/**` operations
//  whose description reads: "The projected gRPC method returned `PermissionDenied`. The token is valid
//  but does not carry the scope this operation requires." That sentence is a promise about THIS service:
//  Gateway's 403 there is a PROJECTION of a status this service was never producing. Until this file
//  existed nothing here read the claim, so C-03 and C-04 were both reachable by any token addressed to
//  this service, and the published 403 was unreachable through the whole chain.
//
//  WHY THE TWO CONTRACTS GET TWO SCOPES RATHER THAN ONE
//  C-04 is deliberately a SEPARATE service from C-03 so the expansion engine can version independently
//  of the event chain [AAP 0.4.3], and the caller requests a separate scope for each. One scope covering
//  both would undo that independence at the authorization layer: a deployment could not grant the event
//  chain without also granting the expression engine, and the two contracts' whole reason for being
//  separate is that they are separately consumable.
//
//  WHY THE FRAMEWORK'S OWN CLAIM REQUIREMENT IS NOT ENOUGH
//  `RequireClaim("scope", value)` compares the claim's WHOLE value. The scope claim is a SINGLE claim
//  carrying a SPACE-DELIMITED set, and this service's only caller sends exactly that - it requests both
//  scopes in one token and attaches that one credential to every call. `RequireClaim` would therefore
//  refuse every call Gateway makes. The failure would be in the safe direction and would still mean this
//  service served nothing at all.
//
//  WHY IT IS DUPLICATED IN EACH SERVICE RATHER THAN SHARED
//  The migration plan permits exactly one cross-service coupling - the published contracts project - and
//  states that it carries no behaviour and is not a shared-code back door [AAP 0.4.2.3, 0.7.2]. An
//  authorization handler is behaviour, so each service carries its own and names only its own scopes.
//
//  WHAT IS DELIBERATELY NOT SCOPE-GATED
//  `/health` is anonymous, so it reaches no scope check because it reaches no authentication. The
//  generated contract document is anonymous for the reason recorded at its own registration. `/v1/ping`
//  is authenticated and carries NO scope requirement: it performs no work and reaches no capability, so a
//  scope for it would be a permission over nothing.
//
//  RULES
//  review_rules reports that no user rules were provided, so no user-specified rule governs this file.
// ==================================================================================================

using System.Collections.Frozen;
using System.Security.Claims;

using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

using PowerFramework.DataServices.Configuration;

namespace PowerFramework.DataServices.Authorization;

/// <summary>
/// The scope names this service's two contracts require, and the policy names that carry them.
/// </summary>
/// <remarks>
/// Both values are read from the caller that sends them - Gateway's <c>DataServicesClient</c> declares
/// this exact pair as the scopes it requests for this edge, and Security's <c>appsettings.json</c>
/// authorises that caller for exactly them. Three files, two values, and these are the ones the
/// contracts check.
/// </remarks>
public static class DataServicesScopes
{
    /// <summary>The scope required by contract C-03: retrieval, validation, update and the event chain.</summary>
    public const string DataWindow = "dataservices.datawindow";

    /// <summary>The scope required by contract C-04: the column-expression expansion engine.</summary>
    /// <remarks>
    /// SEPARATE FROM <see cref="DataWindow"/> AND NOT IMPLIED BY IT. C-04 is its own gRPC service
    /// precisely so it can version independently of the event chain, and a caller may legitimately hold
    /// one contract without the other. An implication either way would undo that at the authorization
    /// layer while leaving the contracts looking independent.
    /// </remarks>
    public const string ColumnExpression = "dataservices.columnexpression";

    /// <summary>Every policy this service registers, so a test can assert the set is complete.</summary>
    public static IReadOnlyList<string> All { get; } = [DataWindow, ColumnExpression];
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
        // Fail() would poison every other requirement in the same evaluation, including ones a future
        // policy might legitimately satisfy another way.
        return Task.CompletedTask;
    }

    /// <summary>Splits a space-delimited claim value into its entries, allocating nothing.</summary>
    /// <param name="value">The claim value.</param>
    /// <returns>The ranges of the non-empty entries.</returns>
    /// <remarks>
    /// Ranges rather than substrings because this runs on every authenticated call: the comparison needs
    /// no string, so none is created. Empty entries are skipped so a doubled or trailing delimiter behaves
    /// like a well-formed value.
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
/// A requirement that the caller's subject is one of the configured permitted identities.
/// </summary>
/// <remarks>
/// <para>
/// A SECOND, INDEPENDENT QUESTION FROM THE SCOPE, AND BOTH HAVE TO BE ASKED. A scope says what a
/// credential is FOR; the roster says who this service serves. Checking only the scope admits a token
/// minted for some other caller that happens to carry the right scope, and checking only the roster
/// admits a rostered caller to a contract it was never granted - CWE-863 in one direction and CWE-862 in
/// the other. They are separate requirement types so a policy's requirement set says which of the two
/// refused a call, and so neither can be removed by an edit that looks like it is only touching the
/// other.
/// </para>
/// <para>
/// AN EMPTY ROSTER REFUSES EVERY CALLER rather than permitting every caller. The options contract
/// requires at least one entry, so an empty list means the configuration was not written - and a
/// permissive reading of it is exactly the fail-open default this requirement exists to remove.
/// </para>
/// </remarks>
internal sealed class PermittedCallerRequirement : IAuthorizationRequirement;

/// <summary>
/// Decides a <see cref="PermittedCallerRequirement"/> against the configured caller roster.
/// </summary>
/// <param name="options">The monitor the roster is read from.</param>
/// <remarks>
/// <para>
/// READ THROUGH THE MONITOR ON EVERY EVALUATION RATHER THAN CAPTURED ONCE, so a deployment that reloads
/// its configuration is honoured without a restart and this handler holds no closed-over state a later
/// edit could make stale.
/// </para>
/// <para>
/// AND READ FROM THE CONTAINER RATHER THAN FROM <c>context.Resource</c>. Resolving the roster by
/// casting the authorization resource to an <c>HttpContext</c> and reaching into its request
/// services refuses whenever the resource is of any other shape. That is right for a refusal
/// default but wrong as a dependency: the two gRPC contracts authorize through endpoint routing as well,
/// and a policy whose correctness depends on the resource's runtime type is one refactor away from
/// silently refusing everything. An injected monitor cannot be absent.
/// </para>
/// <para>
/// THE SUBJECT IS READ UNDER BOTH SPELLINGS. Inbound claim mapping is off, so the issuer's <c>sub</c>
/// arrives under its own name; the framework's own name-identifier spelling is accepted too so that a
/// principal built by any other authentication path is judged by the same rule rather than refused for a
/// reason that has nothing to do with permission.
/// </para>
/// </remarks>
internal sealed class PermittedCallerHandler(IOptionsMonitor<JwtAuthenticationOptions> options)
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

        IList<string> permitted = options.CurrentValue.PermittedCallers;

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

                break;
            }
        }

        // Neither succeed nor fail when unmet, for the same reason ScopeHandler does not fail: Fail()
        // poisons every other requirement in the same evaluation.
        return Task.CompletedTask;
    }
}

/// <summary>
/// Registers this service's scope policies and the handler that decides them.
/// </summary>
public static class ScopeAuthorizationExtensions
{
    /// <summary>
    /// Adds the scope handler and one named policy per scope in <see cref="DataServicesScopes.All"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// EACH POLICY REQUIRES AN AUTHENTICATED USER AS WELL AS THE SCOPE, even though the fallback policy
    /// already requires one. A named policy REPLACES the fallback for the endpoint that names it, so
    /// omitting the authentication requirement would make a scope-gated endpoint the only one in the
    /// service not demanding authentication - and it would still appear to work, because an
    /// unauthenticated principal carries no scope claim and would be refused anyway. It would be refused
    /// with the wrong status: <c>PermissionDenied</c> rather than <c>Unauthenticated</c>, which Gateway
    /// projects as 403 rather than the published 401.
    /// </remarks>
    public static IServiceCollection AddScopeAuthorization(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IAuthorizationHandler, ScopeHandler>();
        services.AddSingleton<IAuthorizationHandler, PermittedCallerHandler>();

        AuthorizationBuilder builder = services.AddAuthorizationBuilder();

        foreach (string scope in DataServicesScopes.All)
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
        DataServicesScopes.All.ToFrozenSet(StringComparer.Ordinal);
}
