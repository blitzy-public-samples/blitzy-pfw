// ==================================================================================================
//  SessionOwnershipTests - A SESSION IDENTIFIER IS UNGUESSABLE, WHICH IS NOT THE SAME AS OWNER-BOUND
//  ------------------------------------------------------------------------------------------------
//  WHAT THIS SUITE PINS
//
//    1. TWO PRINCIPALS, NOT ONE. Every refusal row here has a positive twin taken through the same
//       fixture, because a service that refused EVERY caller would satisfy a suite that only ever
//       asserted refusals - and would be broken in the more obvious direction. The owner resolves,
//       uses and closes its own session in the same rows that show a second caller cannot.
//
//    2. THE REFUSAL IS INDISTINGUISHABLE FROM AN IDENTIFIER THIS SERVICE NEVER ISSUED. Both
//       registries mint 128-bit-opaque identifiers precisely so no caller can learn which sessions
//       exist. A distinct "not yours" answer would give that back: the refusal is therefore asserted
//       to be the SAME answer as an unknown identifier, value for value, not merely "also a failure".
//
//    3. A FOREIGN ATTEMPT DOES NOT TOUCH THE SESSION. Resolution records activity, so an ownership
//       check placed after acquisition would still refuse the call while letting a leaked identifier
//       hold another caller's session open indefinitely. The clock rows below prove the owner's idle
//       window is unaffected by a foreign attempt - which no status assertion could establish.
//
//    4. THE MAINTENANCE PATHS ARE DELIBERATELY NOT OWNER-CHECKED. The idle sweep and the shutdown
//       drain run inside no request, so a comparison there would test every stored subject against
//       the unattributed sentinel and collect nothing - converting the concurrent-session ceiling
//       into a leak. Two rows require the sweep and the drain to collect sessions they do not own.
//
//    5. THE OWNER IS COMPARED, NOT PUBLISHED. A reflection row requires that nothing named like an
//       owner appears on the close result or the state snapshot, so the attribution cannot reach a
//       response by a later edit.
//
//  WHY THE PRINCIPAL IS DRIVEN THROUGH THE STOCK ACCESSOR
//  ------------------------------------------------------------------------------------------------
//  `SessionPrincipalResolver` reads `IHttpContextAccessor`, which is exactly what the composition root
//  registers, so these rows exercise the same code path a real request does rather than a test seam
//  substituted for it. Setting the accessor's context is how one test thread impersonates two callers
//  in sequence: the ambient value is what a registry reads, so assigning it is the whole of what
//  arriving as a different caller means.
//
//  ORACLE
//  ------------------------------------------------------------------------------------------------
//  None. Session ownership is NET-NEW: the legacy's sessions were objects in the caller's own address
//  space, reachable only by a pointer the process already held, so the oracle had no notion of one
//  caller reaching another's. The boundary created the exposure and therefore owns the control
//  (AAP 0.1.4, constraint C-G).
// ==================================================================================================

using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

using PowerFramework.DataServices.Authorization;
using PowerFramework.DataServices.Configuration;
using PowerFramework.DataServices.Domain;
using PowerFramework.DataServices.Expressions;
using PowerFramework.Shared.Kernel;

using Xunit;

namespace PowerFramework.DataServices.Tests;

/// <summary>
/// Ownership of a server-held session: who may resolve it, who may close it, and what a caller who may
/// not is allowed to learn.
/// </summary>
public sealed class SessionOwnershipTests
{
    /// <summary>The idle window every fixture here is built with.</summary>
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(10);

    /// <summary>The caller that opens a session in every row.</summary>
    private const string Owner = "powerframework-gateway";

    /// <summary>A second authenticated caller. Rostered, valid, and not the owner.</summary>
    private const string Stranger = "powerframework-another-caller";

    // ----------------------------------------------------------------------------------------------
    //  VALIDATION SESSIONS - C-03's OpenValidationSession / EventChain / CloseValidationSession.
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// The opening caller resolves its own session, and a second caller does not - with the refusal
    /// carrying the code an unknown identifier carries.
    /// </summary>
    /// <remarks>
    /// THE POSITIVE ARM IS HALF THE POINT. Without it a registry that refused everyone would pass.
    /// </remarks>
    [Fact]
    public void OnlyTheOpeningCallerResolvesItsValidationSession()
    {
        (ValidationSessionRegistry registry, HttpContextAccessor callers, ValidationSessionTestClock _) =
            NewValidationRegistry();

        ArriveAs(callers, Owner);
        ValidationSessionOpenResult opened = registry.Open("dw_employee");

        Assert.True(opened.IsOpened);
        Assert.NotNull(opened.Session);

        string sessionId = opened.Session.SessionId;

        // The owner, in a later call of its own.
        ArriveAs(callers, Owner);
        ValidationSessionResolution mine = registry.Resolve(sessionId);

        Assert.True(mine.IsResolved);
        Assert.Equal(RetCode.OK, mine.ReturnCode);
        Assert.Same(opened.Session, mine.Session);

        // A different authenticated caller holding the same identifier.
        ArriveAs(callers, Stranger);
        ValidationSessionResolution theirs = registry.Resolve(sessionId);

        Assert.False(theirs.IsResolved);
        Assert.Null(theirs.Session);
        Assert.Equal(RetCode.E_INVALID_HANDLE, theirs.ReturnCode);

        // And the refusal changed nothing: the owner still holds it.
        ArriveAs(callers, Owner);
        Assert.True(registry.Resolve(sessionId).IsResolved);
    }

    /// <summary>
    /// A foreign identifier and an identifier this service never issued are the SAME answer.
    /// </summary>
    /// <remarks>
    /// ASSERTED AS EQUALITY OF THE WHOLE RESOLUTION rather than as "both failed". Two different failure
    /// codes would still be two different observations, and the identifier is unguessable precisely so
    /// that no caller can determine which sessions exist (CWE-639).
    /// </remarks>
    [Fact]
    public void AForeignValidationSessionIsAnsweredExactlyAsAnUnknownOneIs()
    {
        (ValidationSessionRegistry registry, HttpContextAccessor callers, ValidationSessionTestClock _) =
            NewValidationRegistry();

        ArriveAs(callers, Owner);
        ValidationSessionOpenResult opened = registry.Open();

        Assert.NotNull(opened.Session);

        ArriveAs(callers, Stranger);

        ValidationSessionResolution foreign = registry.Resolve(opened.Session.SessionId);
        ValidationSessionResolution unknown = registry.Resolve(new string('a', 32));

        Assert.Equal(unknown.ReturnCode, foreign.ReturnCode);
        Assert.Equal(unknown.IsResolved, foreign.IsResolved);
        Assert.Null(foreign.Session);
        Assert.Null(unknown.Session);
    }

    /// <summary>
    /// A foreign resolution does not renew the owner's idle window.
    /// </summary>
    /// <remarks>
    /// THE ONE PROPERTY A STATUS ASSERTION CANNOT REACH, and the reason the ownership comparison sits
    /// ABOVE acquisition rather than below it. Acquisition RECORDS ACTIVITY, so a check placed after it
    /// refuses the call and still renews the session - which would let a leaked identifier pin another
    /// caller's session, and its five attached services, for as long as the holder kept trying. The row
    /// is built so it would FAIL against that ordering: the foreign attempt lands three-quarters of the
    /// way through the window, so a renewal there would leave the session alive at the point where this
    /// row requires it to have expired.
    /// </remarks>
    [Fact]
    public void AForeignAttemptDoesNotRenewTheOwnersValidationSession()
    {
        (ValidationSessionRegistry registry, HttpContextAccessor callers, ValidationSessionTestClock clock) =
            NewValidationRegistry();

        ArriveAs(callers, Owner);
        ValidationSessionOpenResult opened = registry.Open();

        Assert.NotNull(opened.Session);

        string sessionId = opened.Session.SessionId;

        clock.Advance(IdleTimeout * 0.75);

        ArriveAs(callers, Stranger);
        Assert.Equal(RetCode.E_INVALID_HANDLE, registry.Resolve(sessionId).ReturnCode);

        // Past the window measured from the OPEN, which it would not be if the foreign attempt had
        // touched the session.
        clock.Advance(IdleTimeout * 0.75);

        ArriveAs(callers, Owner);
        ValidationSessionResolution afterExpiry = registry.Resolve(sessionId);

        Assert.False(afterExpiry.IsResolved);
        Assert.Equal(RetCode.E_NOT_EXISTS, afterExpiry.ReturnCode);
    }

    /// <summary>
    /// A foreign caller cannot close another caller's validation session, and its refusal is the
    /// idempotent already-closed answer.
    /// </summary>
    /// <remarks>
    /// CLOSING IS THE MORE DAMAGING HALF. A foreign close detaches a live event chain from the stash it
    /// is mid-way through reading, so it is a denial of service against the owner rather than a
    /// disclosure (CWE-862). The row proves the session survives the attempt AND that the owner's own
    /// close still reports it as having been open - so the refusal did not quietly half-close it.
    /// </remarks>
    [Fact]
    public void AForeignCallerCannotCloseAValidationSessionAndCannotTellThatFromAnUnknownOne()
    {
        (ValidationSessionRegistry registry, HttpContextAccessor callers, ValidationSessionTestClock _) =
            NewValidationRegistry();

        ArriveAs(callers, Owner);
        ValidationSessionOpenResult opened = registry.Open();

        Assert.NotNull(opened.Session);

        string sessionId = opened.Session.SessionId;

        ArriveAs(callers, Stranger);

        ValidationSessionCloseResult foreign = registry.Close(sessionId);
        ValidationSessionCloseResult unknown = registry.Close(new string('b', 32));

        Assert.Equal(RetCode.OK, foreign.ReturnCode);
        Assert.False(foreign.WasOpen);
        Assert.Equal(unknown.ReturnCode, foreign.ReturnCode);
        Assert.Equal(unknown.WasOpen, foreign.WasOpen);

        // Untouched, and still the owner's.
        ArriveAs(callers, Owner);
        Assert.True(registry.Resolve(sessionId).IsResolved);
        Assert.Equal(1, registry.Count);

        ValidationSessionCloseResult mine = registry.Close(sessionId);

        Assert.Equal(RetCode.OK, mine.ReturnCode);
        Assert.True(mine.WasOpen);
        Assert.Equal(0, registry.Count);
    }

    /// <summary>
    /// A blank identifier is still an argument fault for a foreign caller, not an ownership refusal.
    /// </summary>
    /// <remarks>
    /// The ownership comparison must not swallow the malformed-request arm: a caller that sends no
    /// identifier at all has made a different mistake from one that sends someone else's, and the
    /// contract distinguishes them. The distinction is safe because a blank identifier names no session,
    /// so answering it precisely discloses nothing.
    /// </remarks>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankIdentifierRemainsAnArgumentFaultWhoeverSendsIt(string? sessionId)
    {
        (ValidationSessionRegistry registry, HttpContextAccessor callers, ValidationSessionTestClock _) =
            NewValidationRegistry();

        ArriveAs(callers, Stranger);

        Assert.Equal(RetCode.E_INVALID_ARGUMENT, registry.Resolve(sessionId).ReturnCode);
        Assert.Equal(RetCode.E_INVALID_ARGUMENT, registry.Close(sessionId).ReturnCode);
    }

    /// <summary>
    /// The idle sweep collects sessions it does not own, because it runs on nobody's behalf.
    /// </summary>
    /// <remarks>
    /// THE ROW THAT WOULD FAIL IF THE MAINTENANCE PATH WERE OWNER-CHECKED, and it is the reason the
    /// unchecked close exists as a separately named member. Outside a request the resolver reports the
    /// unattributed sentinel, so a comparison here would refuse every session a real caller opened: the
    /// sweep would collect nothing, abandoned sessions would stay pinned for the life of the process,
    /// and the concurrent-session ceiling would stay permanently reached - the denial of service the
    /// ceiling exists to prevent, arriving by the one route that looks like extra safety.
    /// </remarks>
    [Fact]
    public void TheIdleSweepCollectsValidationSessionsItDoesNotOwn()
    {
        (ValidationSessionRegistry registry, HttpContextAccessor callers, ValidationSessionTestClock clock) =
            NewValidationRegistry();

        ArriveAs(callers, Owner);
        Assert.True(registry.Open().IsOpened);

        ArriveAs(callers, Stranger);
        Assert.True(registry.Open().IsOpened);

        Assert.Equal(2, registry.Count);

        // No request in flight: exactly the state a background pass runs in.
        callers.HttpContext = null;
        clock.Advance(IdleTimeout + TimeSpan.FromSeconds(1));

        Assert.Equal(2, registry.SweepExpired());
        Assert.Equal(0, registry.Count);
    }

    /// <summary>
    /// Shutdown closes every validation session whoever opened it.
    /// </summary>
    [Fact]
    public void ShutdownClosesEveryValidationSessionRegardlessOfOwner()
    {
        (ValidationSessionRegistry registry, HttpContextAccessor callers, ValidationSessionTestClock _) =
            NewValidationRegistry();

        ArriveAs(callers, Owner);
        Assert.True(registry.Open().IsOpened);

        ArriveAs(callers, Stranger);
        Assert.True(registry.Open().IsOpened);

        callers.HttpContext = null;

        Assert.Equal(2, registry.CloseAll());
        Assert.Equal(0, registry.Count);
    }

    /// <summary>
    /// A session opened with no principal is owned by unattributed callers, and not by an authenticated
    /// one.
    /// </summary>
    /// <remarks>
    /// AN ABSENT IDENTITY IS ATTRIBUTED, NOT EXEMPTED. Treating "no principal" as a wildcard would make
    /// the whole control avoidable by the one route that is hardest to audit - and would also open every
    /// unattributed session to any authenticated caller. Both directions are asserted here.
    /// </remarks>
    [Fact]
    public void AnUnattributedValidationSessionBelongsToUnattributedCallersOnly()
    {
        (ValidationSessionRegistry registry, HttpContextAccessor callers, ValidationSessionTestClock _) =
            NewValidationRegistry();

        callers.HttpContext = null;
        ValidationSessionOpenResult opened = registry.Open();

        Assert.NotNull(opened.Session);
        Assert.Equal(SessionPrincipalResolver.Unattributed, opened.Session.Owner);

        string sessionId = opened.Session.SessionId;

        // An authenticated caller cannot take it over...
        ArriveAs(callers, Owner);
        Assert.Equal(RetCode.E_INVALID_HANDLE, registry.Resolve(sessionId).ReturnCode);

        // ...and the unattributed caller still holds it.
        callers.HttpContext = null;
        Assert.True(registry.Resolve(sessionId).IsResolved);
    }

    /// <summary>
    /// An authenticated request carrying no subject claim is unattributed rather than privileged.
    /// </summary>
    /// <remarks>
    /// The composition root admits only rostered subjects, so this shape is unreachable through the
    /// published surface. It is pinned anyway because the failure direction matters: reading a missing
    /// subject as "any owner" would turn a malformed token into a master key.
    /// </remarks>
    [Fact]
    public void AnAuthenticatedCallerWithNoSubjectIsUnattributed()
    {
        (ValidationSessionRegistry registry, HttpContextAccessor callers, ValidationSessionTestClock _) =
            NewValidationRegistry();

        ArriveAs(callers, Owner);
        ValidationSessionOpenResult owned = registry.Open();

        Assert.NotNull(owned.Session);

        // Authenticated, but naming nobody.
        callers.HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(authenticationType: "Test")),
        };

        Assert.Equal(RetCode.E_INVALID_HANDLE, registry.Resolve(owned.Session.SessionId).ReturnCode);

        ValidationSessionOpenResult anonymous = registry.Open();

        Assert.NotNull(anonymous.Session);
        Assert.Equal(SessionPrincipalResolver.Unattributed, anonymous.Session.Owner);
    }

    /// <summary>
    /// Two callers each resolve their own session and neither resolves the other's.
    /// </summary>
    [Fact]
    public void TwoCallersEachHoldTheirOwnValidationSession()
    {
        (ValidationSessionRegistry registry, HttpContextAccessor callers, ValidationSessionTestClock _) =
            NewValidationRegistry();

        ArriveAs(callers, Owner);
        ValidationSessionOpenResult first = registry.Open("dw_first");

        ArriveAs(callers, Stranger);
        ValidationSessionOpenResult second = registry.Open("dw_second");

        Assert.NotNull(first.Session);
        Assert.NotNull(second.Session);

        ArriveAs(callers, Owner);
        Assert.True(registry.Resolve(first.Session.SessionId).IsResolved);
        Assert.False(registry.Resolve(second.Session.SessionId).IsResolved);

        ArriveAs(callers, Stranger);
        Assert.False(registry.Resolve(first.Session.SessionId).IsResolved);
        Assert.True(registry.Resolve(second.Session.SessionId).IsResolved);
    }

    // ----------------------------------------------------------------------------------------------
    //  EXPRESSION SESSIONS - C-04's OpenExpressionSession / the two inverted channels / close.
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// The opening caller resolves its own expression session, and a second caller does not.
    /// </summary>
    /// <remarks>
    /// SHARPER HERE THAN ON THE VALIDATION SESSION. An expression session scopes the DataWindow hosts
    /// every handle it minted resolves through, so a foreign lookup would hand another caller's live
    /// hosts - and every expression bound into them - to whoever held the identifier. The refusal is a
    /// plain <see langword="false"/>, which is exactly what an unknown identifier answers.
    /// </remarks>
    [Fact]
    public void OnlyTheOpeningCallerResolvesItsExpressionSession()
    {
        (ExpressionSessionRegistry registry, HttpContextAccessor callers, ValidationSessionTestClock _) =
            NewExpressionRegistry();

        ArriveAs(callers, Owner);
        ExpressionSessionOpenResult opened = registry.Open();

        Assert.True(opened.IsOpened);
        Assert.NotNull(opened.Session);

        string sessionId = opened.Session.SessionId;

        ArriveAs(callers, Owner);
        Assert.True(registry.TryGet(sessionId, out ExpressionSession? mine));
        Assert.Same(opened.Session, mine);

        ArriveAs(callers, Stranger);
        Assert.False(registry.TryGet(sessionId, out ExpressionSession? theirs));
        Assert.Null(theirs);

        // Indistinguishable from an identifier this service never issued.
        Assert.False(registry.TryGet(new string('c', 32), out ExpressionSession? unknown));
        Assert.Null(unknown);

        ArriveAs(callers, Owner);
        Assert.True(registry.TryGet(sessionId, out _));
    }

    /// <summary>
    /// A foreign lookup does not renew the owner's expression session.
    /// </summary>
    [Fact]
    public void AForeignAttemptDoesNotRenewTheOwnersExpressionSession()
    {
        (ExpressionSessionRegistry registry, HttpContextAccessor callers, ValidationSessionTestClock clock) =
            NewExpressionRegistry();

        ArriveAs(callers, Owner);
        ExpressionSessionOpenResult opened = registry.Open();

        Assert.NotNull(opened.Session);

        string sessionId = opened.Session.SessionId;

        clock.Advance(IdleTimeout * 0.75);

        ArriveAs(callers, Stranger);
        Assert.False(registry.TryGet(sessionId, out _));

        clock.Advance(IdleTimeout * 0.75);

        ArriveAs(callers, Owner);
        Assert.False(registry.TryGet(sessionId, out _));
        Assert.Equal(0, registry.Count);
    }

    /// <summary>
    /// A foreign caller cannot close another caller's expression session.
    /// </summary>
    /// <remarks>
    /// A close here releases every host registration, so every handle the session minted stops
    /// resolving and the owner's open calculation is BLOCKED mid-flight. The refusal is the same
    /// <see langword="false"/> an unknown identifier answers, and the owner's own close still reports
    /// the session as having been open.
    /// </remarks>
    [Fact]
    public void AForeignCallerCannotCloseAnExpressionSession()
    {
        (ExpressionSessionRegistry registry, HttpContextAccessor callers, ValidationSessionTestClock _) =
            NewExpressionRegistry();

        ArriveAs(callers, Owner);
        ExpressionSessionOpenResult opened = registry.Open();

        Assert.NotNull(opened.Session);

        string sessionId = opened.Session.SessionId;

        ArriveAs(callers, Stranger);
        Assert.False(registry.Close(sessionId));
        Assert.False(registry.Close(new string('d', 32)));
        Assert.Equal(1, registry.Count);

        ArriveAs(callers, Owner);
        Assert.True(registry.TryGet(sessionId, out _));
        Assert.True(registry.Close(sessionId));
        Assert.Equal(0, registry.Count);
    }

    /// <summary>
    /// The idle sweep and the shutdown drain both collect expression sessions they do not own.
    /// </summary>
    [Fact]
    public void MaintenanceCollectsExpressionSessionsItDoesNotOwn()
    {
        (ExpressionSessionRegistry sweeping, HttpContextAccessor sweepCallers, ValidationSessionTestClock clock) =
            NewExpressionRegistry();

        ArriveAs(sweepCallers, Owner);
        Assert.True(sweeping.Open().IsOpened);

        ArriveAs(sweepCallers, Stranger);
        Assert.True(sweeping.Open().IsOpened);

        sweepCallers.HttpContext = null;
        clock.Advance(IdleTimeout + TimeSpan.FromSeconds(1));

        Assert.Equal(2, sweeping.SweepExpired());
        Assert.Equal(0, sweeping.Count);

        (ExpressionSessionRegistry draining, HttpContextAccessor drainCallers, ValidationSessionTestClock _) =
            NewExpressionRegistry();

        ArriveAs(drainCallers, Owner);
        Assert.True(draining.Open().IsOpened);

        ArriveAs(drainCallers, Stranger);
        Assert.True(draining.Open().IsOpened);

        drainCallers.HttpContext = null;

        Assert.Equal(2, draining.CloseAll());
        Assert.Equal(0, draining.Count);
    }

    /// <summary>
    /// An expression session opened with no principal belongs to unattributed callers only.
    /// </summary>
    [Fact]
    public void AnUnattributedExpressionSessionBelongsToUnattributedCallersOnly()
    {
        (ExpressionSessionRegistry registry, HttpContextAccessor callers, ValidationSessionTestClock _) =
            NewExpressionRegistry();

        callers.HttpContext = null;
        ExpressionSessionOpenResult opened = registry.Open();

        Assert.NotNull(opened.Session);
        Assert.Equal(SessionPrincipalResolver.Unattributed, opened.Session.Owner);

        ArriveAs(callers, Owner);
        Assert.False(registry.TryGet(opened.Session.SessionId, out _));

        callers.HttpContext = null;
        Assert.True(registry.TryGet(opened.Session.SessionId, out _));
    }

    // ----------------------------------------------------------------------------------------------
    //  THE ATTRIBUTION IS COMPARED, NOT PUBLISHED.
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Neither the close result nor the state snapshot carries the owner.
    /// </summary>
    /// <remarks>
    /// A GUARD AGAINST A LATER EDIT rather than a statement about today's code. The owner is a caller
    /// identity, and a response that carried it would tell whoever held a session identifier which
    /// principal owns it - re-creating by disclosure exactly what the ownership check exists to prevent.
    /// Asserted by member name over both response-shaped types, so a field added under any casing is
    /// caught.
    /// </remarks>
    [Fact]
    public void NoResponseShapedTypeCarriesTheOwner()
    {
        foreach (Type shape in new[] { typeof(ValidationSessionCloseResult), typeof(ValidationSessionSnapshot) })
        {
            foreach (MemberInfo member in shape.GetMembers(BindingFlags.Public | BindingFlags.Instance))
            {
                Assert.DoesNotContain("owner", member.Name, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("principal", member.Name, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("subject", member.Name, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    /// <summary>
    /// The resolver compares ordinally, and reports the unattributed sentinel outside a request.
    /// </summary>
    /// <param name="stored">The identity a session was attributed to.</param>
    /// <param name="arriving">The subject the current caller presents.</param>
    /// <param name="same">Whether the two are the same caller.</param>
    /// <remarks>
    /// CASE IS SIGNIFICANT ON PURPOSE. A subject is machine input, and a case-insensitive comparison
    /// would make two distinct issuer subjects the same caller - an authorization decision varying by
    /// culture and casing rules rather than by identity.
    /// </remarks>
    [Theory]
    [InlineData(Owner, Owner, true)]
    [InlineData(Owner, Stranger, false)]
    [InlineData(Owner, "POWERFRAMEWORK-GATEWAY", false)]
    [InlineData(Owner, " powerframework-gateway", false)]
    [InlineData(SessionPrincipalResolver.Unattributed, null, true)]
    [InlineData(Owner, null, false)]
    public void TheResolverComparesTheSubjectOrdinally(string stored, string? arriving, bool same)
    {
        HttpContextAccessor callers = new();
        SessionPrincipalResolver resolver = new(callers);

        if (arriving is null)
        {
            callers.HttpContext = null;

            Assert.Equal(SessionPrincipalResolver.Unattributed, resolver.Resolve());
        }
        else
        {
            ArriveAs(callers, arriving);

            Assert.Equal(arriving, resolver.Resolve());
        }

        Assert.Equal(same, resolver.IsCaller(stored));
    }

    /// <summary>A resolver with no accessor attributes everything to the sentinel.</summary>
    /// <remarks>
    /// The state a registry constructed without a host runs in, which is what keeps every branch of both
    /// registries reachable from a plain unit test (constraint C-H).
    /// </remarks>
    [Fact]
    public void AResolverWithNoAccessorIsAlwaysUnattributed()
    {
        SessionPrincipalResolver resolver = new();

        Assert.Equal(SessionPrincipalResolver.Unattributed, resolver.Resolve());
        Assert.True(resolver.IsCaller(SessionPrincipalResolver.Unattributed));
        Assert.False(resolver.IsCaller(Owner));
        Assert.Throws<ArgumentNullException>(() => resolver.IsCaller(null!));
    }

    // ----------------------------------------------------------------------------------------------
    //  FIXTURE CONSTRUCTION.
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Makes the ambient request belong to a named caller, exactly as authentication would.
    /// </summary>
    /// <param name="callers">The accessor both the resolver and this test read.</param>
    /// <param name="subject">The subject claim to present.</param>
    /// <remarks>
    /// The claim is spelled with its PROTOCOL name, because this service configures inbound claim
    /// mapping off and a token's claim therefore arrives spelled as the token spells it. A row using the
    /// framework's mapped alias would pass against a differently configured host and say nothing about
    /// this one.
    /// </remarks>
    private static void ArriveAs(HttpContextAccessor callers, string subject) =>
        callers.HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("sub", subject)],
                authenticationType: "PowerFramework.DataServices.Tests")),
        };

    private static (ValidationSessionRegistry Registry, HttpContextAccessor Callers, ValidationSessionTestClock Clock)
        NewValidationRegistry()
    {
        ValidationSessionTestClock clock = new();
        HttpContextAccessor callers = new();

        DataServicesOptions options = new();
        options.Sessions.ValidationSession.IdleTimeout = IdleTimeout;
        options.Sessions.ValidationSession.MaxConcurrentSessions = 32;

        return (
            new ValidationSessionRegistry(
                options,
                i18n: null,
                timeProvider: clock,
                principals: new SessionPrincipalResolver(callers)),
            callers,
            clock);
    }

    private static (ExpressionSessionRegistry Registry, HttpContextAccessor Callers, ValidationSessionTestClock Clock)
        NewExpressionRegistry()
    {
        ValidationSessionTestClock clock = new();
        HttpContextAccessor callers = new();

        DataServicesOptions options = new();
        options.Sessions.ExpressionSession.IdleTimeout = IdleTimeout;
        options.Sessions.ExpressionSession.MaxConcurrentSessions = 32;

        return (
            new ExpressionSessionRegistry(
                Options.Create(options),
                clock,
                traceSink: null,
                logger: null,
                principals: new SessionPrincipalResolver(callers)),
            callers,
            clock);
    }
}

/// <summary>
/// Ownership through the REAL composition root: the registries are owner-bound only if the host supplies
/// them with an identity, and only the host can prove that it does.
/// </summary>
/// <remarks>
/// <para>
/// <b>WHY THE UNIT ROWS ABOVE ARE NOT ENOUGH.</b> Those rows hand the registry a resolver built over an
/// accessor they control. The deployed service builds one over
/// <c>IHttpContextAccessor</c> resolved from the container, and the registry's resolver parameter is
/// OPTIONAL - so a composition root that forgot either the accessor registration or the constructor
/// argument would compile, start, pass every unit row above, and attribute every session in the
/// deployment to one shared unattributed owner. That is precisely the shape of defect where a control
/// exists in code and is unreachable in the only supported deployment, so it is asserted here against the
/// host rather than inferred.
/// </para>
/// <para>
/// <b>THE SECOND CALLER IS ROSTERED.</b> Both contracts require the caller to be one of
/// <c>Authentication:Jwt:PermittedCallers</c>, which the shipped settings declare as Gateway alone. A row
/// that simply claimed another subject would be refused 403 by the receiver policy BEFORE reaching a
/// session, and would then prove the roster works rather than that ownership does. The second identity is
/// therefore added to the roster for this fixture only, so both callers are equally entitled to the
/// surface and the ONLY thing separating them is whose session it is.
/// </para>
/// </remarks>
public sealed class SessionOwnershipServiceLevelTests
{
    /// <summary>The rostered caller that opens the session.</summary>
    private const string OwnerSubject = DataServicesTestHostFactory.TestPrincipalSubject;

    /// <summary>A second rostered caller, equally entitled to the surface and not the owner.</summary>
    private const string StrangerSubject = "powerframework-second-gateway";

    /// <summary>Where a validation session is opened.</summary>
    private const string SessionsRoute = "/v1/datawindow/sessions";

    /// <summary>A session-scoped read, used to prove a session is still usable by its owner.</summary>
    private const string EventGateRoute = "/v1/datawindow/event-gate";

    /// <summary>An identifier of the right shape that this service never issued.</summary>
    private const string UnknownSessionId = "0123456789abcdef0123456789abcdef";

    /// <summary>
    /// Through the real host: a second rostered caller can neither read nor close another caller's
    /// validation session, and cannot tell either refusal from one naming a session that never existed.
    /// </summary>
    [Fact]
    public async Task ASecondRosteredCallerCannotUseOrCloseAnotherCallersSessionThroughTheHost()
    {
        await using DataServicesTestHostFactory host = new();

        // The roster's second entry. Index 1 leaves the shipped index 0 - Gateway - in place.
        host.AdditionalSettings["Authentication:Jwt:PermittedCallers:1"] = StrangerSubject;

        using HttpClient owner = host.CreateAuthenticatedClient();
        using HttpClient stranger = host.CreateAuthenticatedClient();

        stranger.DefaultRequestHeaders.Add(
            DataServicesTestHostFactory.TestPrincipalSubjectHeaderName,
            StrangerSubject);

        // --- The owner opens a session. -----------------------------------------------------------
        string sessionId;

        using (HttpResponseMessage opened = await RestProjection.PostAsync(
            owner,
            SessionsRoute,
            new { datawindowHandle = DataWindowCatalogue.SqliteFixtureName }))
        {
            Assert.Equal(HttpStatusCode.OK, opened.StatusCode);

            using JsonDocument body = await RestProjection.DocumentAsync(opened);

            sessionId = body.RootElement.GetProperty("sessionId").GetString() ?? string.Empty;
        }

        Assert.False(string.IsNullOrWhiteSpace(sessionId));

        // --- The stranger reads it, and reads an identifier that never existed. -------------------
        using (HttpResponseMessage foreignRead = await stranger.GetAsync(
            RestProjection.Relative($"{EventGateRoute}?sessionId={sessionId}"),
            TestContext.Current.CancellationToken))
        using (HttpResponseMessage unknownRead = await stranger.GetAsync(
            RestProjection.Relative($"{EventGateRoute}?sessionId={UnknownSessionId}"),
            TestContext.Current.CancellationToken))
        {
            // Refused, and refused AS AN UNKNOWN SESSION - not as a permission fault, which would tell
            // the caller the session exists and belongs to somebody else.
            Assert.Equal(HttpStatusCode.NotFound, foreignRead.StatusCode);
            Assert.Equal(unknownRead.StatusCode, foreignRead.StatusCode);
            Assert.Equal(
                await RetCodeOfAsync(unknownRead),
                await RetCodeOfAsync(foreignRead));
        }

        // --- The stranger closes it, and closes an identifier that never existed. -----------------
        using (HttpResponseMessage foreignClose = await stranger.DeleteAsync(
            RestProjection.Relative($"{SessionsRoute}/{sessionId}"),
            TestContext.Current.CancellationToken))
        using (HttpResponseMessage unknownClose = await stranger.DeleteAsync(
            RestProjection.Relative($"{SessionsRoute}/{UnknownSessionId}"),
            TestContext.Current.CancellationToken))
        {
            Assert.Equal(unknownClose.StatusCode, foreignClose.StatusCode);
            Assert.Equal(
                await RetCodeOfAsync(unknownClose),
                await RetCodeOfAsync(foreignClose));
        }

        // --- The owner still holds it, which is what makes the refusals above meaningful. ---------
        using (HttpResponseMessage ownerRead = await owner.GetAsync(
            RestProjection.Relative($"{EventGateRoute}?sessionId={sessionId}"),
            TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, ownerRead.StatusCode);
        }

        // --- And the owner's own close reports it as having been open. ----------------------------
        using (HttpResponseMessage ownerClose = await owner.DeleteAsync(
            RestProjection.Relative($"{SessionsRoute}/{sessionId}"),
            TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, ownerClose.StatusCode);

            using JsonDocument body = await RestProjection.DocumentAsync(ownerClose);

            Assert.True(body.RootElement.GetProperty("wasOpen").GetBoolean());
        }
    }

    /// <summary>
    /// Through the real host: the opening caller's own subsequent calls succeed.
    /// </summary>
    /// <remarks>
    /// THE POSITIVE ARM AS A ROW OF ITS OWN, so a host that refused every session-scoped call - the most
    /// obvious way to break this - fails here rather than passing the refusal rows above.
    /// </remarks>
    [Fact]
    public async Task TheOpeningCallerKeepsUsingItsOwnSessionThroughTheHost()
    {
        await using DataServicesTestHostFactory host = new();

        using HttpClient owner = host.CreateAuthenticatedClient();

        string sessionId;

        using (HttpResponseMessage opened = await RestProjection.PostAsync(
            owner,
            SessionsRoute,
            new { datawindowHandle = DataWindowCatalogue.SqliteFixtureName }))
        {
            Assert.Equal(HttpStatusCode.OK, opened.StatusCode);

            using JsonDocument body = await RestProjection.DocumentAsync(opened);

            sessionId = body.RootElement.GetProperty("sessionId").GetString() ?? string.Empty;
        }

        // Three calls of its own, across two operations, all after the opening request has ended - so a
        // session attributed to something request-scoped rather than to the CALLER would fail here.
        for (int attempt = 0; attempt < 2; attempt++)
        {
            using HttpResponseMessage read = await owner.GetAsync(
                RestProjection.Relative($"{EventGateRoute}?sessionId={sessionId}"),
                TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        }

        using HttpResponseMessage closed = await owner.DeleteAsync(
            RestProjection.Relative($"{SessionsRoute}/{sessionId}"),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, closed.StatusCode);
    }

    /// <summary>
    /// Reads the in-band return code a projected response carries, or null when it carries none.
    /// </summary>
    /// <param name="response">The response.</param>
    /// <returns>The code, or <see langword="null"/>.</returns>
    /// <remarks>
    /// COMPARED RATHER THAN ASSERTED AGAINST A LITERAL. The property under test is that a foreign refusal
    /// and an unknown-session refusal are the SAME answer, which holds however the projection maps them -
    /// so the row compares the two responses instead of encoding today's mapping a second time.
    /// </remarks>
    private static async Task<string?> RetCodeOfAsync(HttpResponseMessage response)
    {
        using JsonDocument body = await RestProjection.DocumentAsync(response);

        // READ AS RAW TEXT rather than as a number. The canonical protobuf JSON mapping renders a 64-bit
        // integer as a STRING, so a numeric read throws on exactly the bodies this row exists to compare -
        // and the comparison does not care about the representation, only that the two agree.
        return body.RootElement.TryGetProperty("retCode", out JsonElement code)
            ? code.GetRawText()
            : null;
    }
}
