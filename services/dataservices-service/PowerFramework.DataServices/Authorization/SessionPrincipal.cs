// ==================================================================================================
//  SessionPrincipal.cs - A SESSION IDENTIFIER IS UNGUESSABLE, WHICH IS NOT THE SAME AS OWNER-BOUND
//  ------------------------------------------------------------------------------------------------
//  WHAT THIS FILE IS FOR
//  Both of this service's session registries mint a high-entropy correlation identifier and then hand
//  every subsequent call to that session to whoever presents the identifier. Entropy bounds DISCOVERY:
//  no caller finds another caller's session by searching for it. It does not bound USE. An identifier
//  that escapes through a log record, a proxy trace, a crash dump or the caller's own bug is a bearer
//  credential for the state behind it - and the state behind a validation session is another caller's
//  item-change stash and validation-error result, while the state behind an expression session is
//  another caller's live DataWindow hosts and every expression bound into them. Closing one is worse
//  again: it detaches a live event chain from the stash it is mid-way through reading.
//
//  This file supplies the missing half. Every session records the authenticated caller it was opened
//  for, and every caller-facing lookup and close compares the caller of the current call against it
//  (CWE-639, CWE-862, CWE-863).
//
//  WHY THE ANSWER TO A FOREIGN IDENTIFIER IS "NO SUCH SESSION"
//  The registries already collapse unknown, closed and expired into defined codes that tell a caller
//  nothing about sessions it does not hold. A distinct "not yours" answer would undo that: it would make
//  either registry an oracle for which identifiers exist, which is precisely what an unguessable
//  identifier is for. So a foreign identifier is answered by the SAME arm as an unknown one, and this
//  type deliberately does not decide that - it answers only "is this the same caller", and the collapse
//  belongs at each lookup where the other outcomes already live.
//
//  WHY THE MAINTENANCE PATHS ARE NOT OWNER-CHECKED
//  The idle sweep and the shutdown drain run on the SERVICE's behalf, inside no request, so there is no
//  caller to compare. Comparing anyway would test every stored subject against the unattributed sentinel
//  and collect nothing, which would convert the abandoned-session ceiling into a leak - the exact denial
//  of service the ceiling exists to prevent. Those paths therefore reach a separately named unchecked
//  member, and the naming is the audit trail.
//
//  WHY IT IS DUPLICATED RATHER THAN SHARED WITH PERSISTENCE
//  Persistence carries the same notion for its four handle registries. The migration plan permits
//  exactly one cross-service coupling - the published contracts project - and states that it carries no
//  behaviour and is not a shared-code back door [AAP 0.4.2.3, 0.7.2]. An authorization decision is
//  behaviour, so each service carries its own.
//
//  RULES
//  review_rules reports that no user rules were provided, so no user-specified rule governs this file.
// ==================================================================================================

using System.Security.Claims;

using Microsoft.AspNetCore.Http;

namespace PowerFramework.DataServices.Authorization;

/// <summary>
/// Resolves the caller identity a session is attributed to, and compares a stored attribution against
/// the caller of the current call.
/// </summary>
/// <remarks>
/// <para>
/// THE SUBJECT CLAIM IS THE IDENTITY, AND IT IS NOT A CHOICE. Every contract on this service requires an
/// authenticated principal carrying the scope its contract names, and the scope roster is written in
/// terms of caller subjects, so the subject is both present and meaningful on every call that can reach
/// a registry. It is preferred over the peer address because a caller behind a proxy shares an address
/// with everything else behind that proxy, and over the client identifier because the subject is what
/// the issuer rosters.
/// </para>
/// <para>
/// AN ABSENT IDENTITY IS ATTRIBUTED, NOT EXEMPTED. A call reaching a registry with no principal - a unit
/// test constructing a registry directly, or a future surface not yet authorized - is attributed to
/// <see cref="Unattributed"/>. Such a session is then owned by unattributed callers and by nobody else,
/// which is a coherent owner rather than a wildcard: it neither opens another caller's session to an
/// anonymous call nor opens an anonymous session to an authenticated one.
/// </para>
/// <para>
/// THE ACCESSOR IS OPTIONAL SO A REGISTRY REMAINS CONSTRUCTIBLE WITHOUT A HOST, which is what keeps the
/// per-service coverage gate reachable for every branch of both registries (constraint C-H). Without one
/// every session is unattributed, and the comparison then admits every unattributed caller - which is
/// what makes the existing unit-test corpus continue to describe the behaviour it describes.
/// </para>
/// <para>
/// PUBLIC BECAUSE <c>ExpressionSessionRegistry</c> IS. That registry is a public type whose constructor
/// now names this one, and a public signature may not name a less accessible type (CS0051). Nothing is
/// published from this assembly as a library, so the visibility carries no packaging consequence.
/// </para>
/// </remarks>
public sealed class SessionPrincipalResolver
{
    /// <summary>The identity a session is attributed to when no principal can be read.</summary>
    /// <remarks>
    /// Parenthesised so it cannot collide with a real subject: a JWT subject is an identifier, and this
    /// value is not a legal one. It never reaches a response - only a diagnostic.
    /// </remarks>
    public const string Unattributed = "(unattributed)";

    private readonly IHttpContextAccessor? _accessor;

    /// <summary>Creates the resolver.</summary>
    /// <param name="accessor">
    /// The ambient request accessor, or <see langword="null"/> outside a host - in which case every
    /// session is <see cref="Unattributed"/>.
    /// </param>
    public SessionPrincipalResolver(IHttpContextAccessor? accessor = null) => _accessor = accessor;

    /// <summary>
    /// Reads the identity of the caller in whose request this call is running.
    /// </summary>
    /// <returns>The subject, or <see cref="Unattributed"/> when none can be read.</returns>
    /// <remarks>
    /// The three claim spellings are tried in the order of decreasing authority: the framework's mapped
    /// name identifier, the protocol's own <c>sub</c>, then the identity name. A host that maps inbound
    /// claims and one that does not therefore resolve the same caller to the same subject, which matters
    /// because a mismatch would attribute a session to one spelling and compare it against another.
    /// </remarks>
    public string Resolve()
    {
        ClaimsPrincipal? user = _accessor?.HttpContext?.User;

        if (user?.Identity?.IsAuthenticated != true)
        {
            return Unattributed;
        }

        string? subject = user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? user.FindFirst("sub")?.Value
            ?? user.Identity.Name;

        return string.IsNullOrWhiteSpace(subject) ? Unattributed : subject;
    }

    /// <summary>
    /// Determines whether the caller of the current call is the one a session was attributed to.
    /// </summary>
    /// <param name="attributedPrincipal">The identity stored on the session when it was opened.</param>
    /// <returns><see langword="true"/> when the two identities are the same caller.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="attributedPrincipal"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// THE COMPARISON IS AGAINST THE SAME SOURCE THE ATTRIBUTION USED - <see cref="Resolve"/>, and
    /// therefore the subject claim - so nothing in this service can disagree with anything else about who
    /// a caller is. One resolver, one notion of identity.
    /// </para>
    /// <para>
    /// ORDINAL, BECAUSE A SUBJECT IS MACHINE INPUT. A culture-sensitive or case-insensitive comparison
    /// would make two distinct issuer subjects the same caller under some cultures and not others, which
    /// is an authorization decision varying by locale.
    /// </para>
    /// <para>
    /// THIS MEMBER DELIBERATELY DOES NOT DECIDE THE OUTCOME. It reports sameness; the caller decides what
    /// a mismatch answers, and in both registries it answers exactly what an unknown identifier answers.
    /// </para>
    /// </remarks>
    public bool IsCaller(string attributedPrincipal)
    {
        ArgumentNullException.ThrowIfNull(attributedPrincipal);

        return string.Equals(attributedPrincipal, Resolve(), StringComparison.Ordinal);
    }
}
