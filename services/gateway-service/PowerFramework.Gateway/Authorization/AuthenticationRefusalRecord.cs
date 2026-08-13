// ==================================================================================================
//  AuthenticationRefusalRecord - the operator record this service writes when it REFUSES a caller
//  ------------------------------------------------------------------------------------------------
//  WHY THIS FILE EXISTS
//
//  Every 401 and every 403 this service answered left NO record an operator could read. The refusal
//  itself was correct - the boundary worked - but it was invisible: a measured probe issued a
//  no-credential request, a forged-signature request and an insufficient-entitlement request, and the
//  shipped logging profile produced not one line for any of them. A credential-stuffing run against
//  this ingress would therefore be indistinguishable, in the log, from no traffic at all.
//
//  THE FRAMEWORK DOES RECORD THESE, AND THAT IS EXACTLY WHY IT IS NOT ENOUGH. The bearer handler and
//  the authorization middleware write at Information under the `Microsoft.AspNetCore.*` categories, and
//  every shipped profile in this estate caps `Microsoft.AspNetCore` at Warning - so those records are
//  filtered out in Production AND in Development. Raising that cap is not the fix: it would admit the
//  whole framework's Information traffic to buy back three records, and it would leave the decision in
//  a settings file where a deployment can silently switch security monitoring off. An own-code record
//  at Warning survives every profile this repository ships and cannot be turned off by a log-level
//  edit.
//
//  WHAT A RECORD MAY CARRY - THE HARD RULE
//  Classifiers only. The AAP's redaction posture (0.6.6, C-F) forbids credential material in a log,
//  and a refusal record is the single most dangerous place to forget that, because the thing being
//  refused IS a credential. So:
//    - the token, in whole or in part, is NEVER recorded, nor is any header value
//    - the exception MESSAGE from token validation is never recorded, only its TYPE NAME: those
//      messages quote what they rejected (IDX10214 prints the audiences it compared), so an attacker
//      choosing an audience could write chosen text into this service's log
//    - the subject and audience ARE recorded, bounded and control-escaped: they are the two fields that
//      make a record actionable, and after validation has FAILED they are unverified caller input,
//      which is precisely why they go through LogSafeText rather than into the template raw
//      - on a 403 they are verified, because a 403 means authentication SUCCEEDED
//    - the scope claim is reported as a PRESENCE and a COUNT, never as its values on a 401
//    - the route PATTERN is recorded and never the path: a pattern names this system's own route table,
//      whereas a path carries whatever the caller put in it
//
//  EXACTLY ONE RECORD PER REFUSAL. The three events below fire in combination - a forged token raises
//  OnAuthenticationFailed AND then OnChallenge - so the failure event deliberately writes NOTHING and
//  only stashes its reason class for the challenge to fold in. Two records for one refusal would make
//  every rate a reader computes from this log wrong by an unpredictable factor.
// ==================================================================================================

using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using PowerFramework.Shared.Diagnostics;

namespace PowerFramework.Gateway.Authorization;

/// <summary>
/// Writes one operator record per authentication or authorization refusal at this service's ingress.
/// </summary>
/// <remarks>
/// Attached to the bearer handler's event hooks by the composition root. It changes no status, no body
/// and no header - only what this service records about a refusal it was already making.
/// </remarks>
internal static class AuthenticationRefusalRecord
{
    /// <summary>The logger category, so an operator can select these records alone.</summary>
    /// <remarks>
    /// A DEDICATED CATEGORY RATHER THAN THE PROGRAM ONE. Authentication refusals are the records a
    /// security reader wants without the rest of this service's startup and request traffic, and a
    /// category is the only selector available to every provider the shared framework ships.
    /// </remarks>
    private const string LoggerCategory = "PowerFramework.Gateway.Authorization.AuthenticationRefusal";

    /// <summary>
    /// The key under which the validation failure's reason class is carried to the challenge.
    /// </summary>
    /// <remarks>
    /// PER REQUEST, because <see cref="Microsoft.AspNetCore.Http.HttpContext.Items"/> is per request -
    /// so two concurrent refusals cannot read each other's reason. A static field here would do exactly
    /// that.
    /// </remarks>
    private const string ReasonClassItemKey = "PowerFramework.Gateway.AuthenticationFailureReasonClass";

    /// <summary>The reported reason class when nothing raised a typed validation failure.</summary>
    /// <remarks>
    /// The overwhelmingly common case, and it is a real distinction rather than a missing value: no
    /// credential was presented at all, so nothing was validated and no validator could fail.
    /// </remarks>
    private const string NoCredentialPresentedReason = "NoCredentialPresented";

    /// <summary>The rendering used where a claim the record wants is absent.</summary>
    private const string AbsentClaim = "(absent)";

    /// <summary>The claim carrying the granted scope set, spelled as the scope handler spells it.</summary>
    /// <remarks>
    /// ONE SPELLING IS ENOUGH FOR THIS ONE, unlike the subject: <c>scope</c> is absent from the
    /// framework's inbound claim-type map, so it arrives spelled this way whether or not a service
    /// remaps claims. The sibling scope handler reads it under exactly this name too.
    /// </remarks>
    private const string ScopeClaimName = "scope";

    /// <summary>The rendering used where no route pattern was matched.</summary>
    private const string UnroutedRouteDescription = "(unrouted)";

    /// <summary>The greatest rendered length of a caller-supplied claim in a record.</summary>
    /// <remarks>
    /// A BOUND ON THE RECORD, NOT ON THE REQUEST. Nothing here refuses a long claim; the refusal has
    /// already happened for its own reasons. The bound exists because an unauthenticated caller controls
    /// these two strings completely, and an unbounded one lets a single request write an arbitrarily
    /// large line into the operator's log. 128 is generous for a service subject - the longest this
    /// estate issues is 27 characters - and small enough that a flood cannot be built out of it.
    /// </remarks>
    private const int ClaimRenderLimit = 128;

    /// <summary>The record written when a credential was absent, malformed or refused.</summary>
    /// <remarks>
    /// THE SUBJECT AND AUDIENCE ON THIS RECORD ARE UNVERIFIED, and the template says so in as many
    /// words. They are read from a token whose signature did NOT check out, so they are claims a caller
    /// asserted rather than facts this service established - a reader who treats them as identity would
    /// be trusting exactly the material the boundary just rejected.
    /// </remarks>
    private const string ChallengeRecordMessage =
        "Authentication was refused at this service's ingress: {Decision} answering {HttpStatus} for "
        + "{HttpMethod} {RoutePattern}. Reason class {ReasonClass}; challenge error {ChallengeError}. "
        + "The credential's claimed subject was {ClaimedSubject} and its claimed audience "
        + "{ClaimedAudience} - both UNVERIFIED, because the credential did not validate, so they are "
        + "caller assertions rather than established identity. Scope claim: {ScopeClaimState}. "
        + "Correlation {CorrelationId}. No part of the credential, and no header value, is recorded.";

    /// <summary>The record written when an authenticated caller lacked the entitlement.</summary>
    /// <remarks>
    /// THE SUBJECT HERE IS VERIFIED, unlike the challenge record above, and the two templates are
    /// deliberately worded differently so a reader cannot mistake one for the other. A 403 means the
    /// signature, issuer, audience and lifetime all checked out and the caller is who they say they
    /// are - what failed is what they are permitted to do.
    /// </remarks>
    private const string ForbiddenRecordMessage =
        "Authorization was refused at this service's ingress: an AUTHENTICATED caller lacked the "
        + "entitlement this route demands, answering {HttpStatus} for {HttpMethod} {RoutePattern}. "
        + "Policy demanded: {DemandedPolicy}. Verified subject {Subject}, audience {Audience}, holding "
        + "{HeldScopeCount} scope value(s). Either the demanded scope is not among them or this caller "
        + "is not on the permitted-caller roster; both requirements gate this route and the framework "
        + "reports one refusal for either. Correlation {CorrelationId}. No part of the credential, and "
        + "no header value, is recorded.";

    /// <summary>
    /// Attaches the refusal records to a bearer handler's event hooks.
    /// </summary>
    /// <param name="bearer">The options being configured.</param>
    /// <exception cref="ArgumentNullException"><paramref name="bearer"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// THE EXISTING DELEGATES ARE CHAINED RATHER THAN REPLACED. Assigning a fresh
    /// <see cref="JwtBearerEvents"/> would silently discard any handler another registration installed,
    /// which is the kind of loss that reads as working. Nothing in this service installs one today, and
    /// this composes correctly if something ever does.
    /// </para>
    /// <para>
    /// THE HOOKS COVER gRPC AS WELL AS REST, WHERE A SERVICE SERVES BOTH, because authentication and
    /// authorization run in the shared middleware pipeline ahead of any interceptor - so a refused gRPC
    /// call never reaches the interceptor that would otherwise have to record it separately.
    /// </para>
    /// </remarks>
    internal static void Attach(JwtBearerOptions bearer)
    {
        ArgumentNullException.ThrowIfNull(bearer);

        JwtBearerEvents events = bearer.Events ??= new JwtBearerEvents();

        Func<AuthenticationFailedContext, Task> onFailed = events.OnAuthenticationFailed;
        Func<JwtBearerChallengeContext, Task> onChallenge = events.OnChallenge;
        Func<ForbiddenContext, Task> onForbidden = events.OnForbidden;

        events.OnAuthenticationFailed = async context =>
        {
            RecordReasonClass(context);

            await onFailed(context).ConfigureAwait(false);
        };

        events.OnChallenge = async context =>
        {
            WriteChallengeRecord(context);

            await onChallenge(context).ConfigureAwait(false);
        };

        events.OnForbidden = async context =>
        {
            WriteForbiddenRecord(context);

            await onForbidden(context).ConfigureAwait(false);
        };
    }

    /// <summary>
    /// Stashes the reason class of a validation failure for the challenge that follows it.
    /// </summary>
    /// <param name="context">The failure.</param>
    /// <remarks>
    /// WRITES NO RECORD, DELIBERATELY. This event and the challenge both fire for one refused token, so
    /// recording here as well would double-count every forged credential - and a reader computing a rate
    /// from this log would be wrong by a factor that varies with the failure mode. The TYPE NAME is
    /// taken and the message discarded: validation messages quote the values they rejected, so an
    /// attacker choosing an audience or an issuer would be choosing text in this service's log.
    /// </remarks>
    private static void RecordReasonClass(AuthenticationFailedContext context)
    {
        if (context.Exception is null)
        {
            return;
        }

        context.HttpContext.Items[ReasonClassItemKey] = context.Exception.GetType().Name;
    }

    /// <summary>Writes the one record for a 401.</summary>
    /// <param name="context">The challenge.</param>
    private static void WriteChallengeRecord(JwtBearerChallengeContext context)
    {
        ILogger logger = ResolveLogger(context.HttpContext);

        if (!logger.IsEnabled(LogLevel.Warning))
        {
            return;
        }

        // READ FROM THE FAILED PRINCIPAL WHERE THERE IS ONE, which there is not on an absent credential.
        // A token whose signature failed still parses, so its claims are readable and worth reporting -
        // marked unverified, which the template does.
        ClaimsPrincipal? claimed = context.HttpContext.User;

        logger.LogWarning(
            ChallengeRecordMessage,
            DescribeChallengeDecision(context),
            StatusCodes.Status401Unauthorized,
            context.HttpContext.Request.Method,
            DescribeRoute(context.HttpContext),
            ResolveReasonClass(context),

            // THE HANDLER'S OWN ERROR CODE, WHICH IS THIS SERVICE'S TEXT AND NOT THE CALLER'S. It is a
            // fixed OAuth token-error keyword the handler selects, so it is safe in the template as-is.
            LogSafeText.Render(context.Error, ClaimRenderLimit),
            RenderClaim(claimed, JwtRegisteredClaimNames.Subject, ClaimTypes.NameIdentifier),
            RenderClaim(claimed, JwtRegisteredClaimNames.Audience, mappedClaimType: null),
            DescribeScopeClaim(claimed),
            ResolveCorrelationId(context.HttpContext));
    }

    /// <summary>Writes the one record for a 403.</summary>
    /// <param name="context">The refusal.</param>
    private static void WriteForbiddenRecord(ForbiddenContext context)
    {
        ILogger logger = ResolveLogger(context.HttpContext);

        if (!logger.IsEnabled(LogLevel.Warning))
        {
            return;
        }

        // 🔴 THE PRINCIPAL IS READ FROM THE REQUEST AND NOT FROM THE CONTEXT, AND THAT IS NOT
        // INTERCHANGEABLE. `ForbiddenContext.Principal` is NULL on this event - measured directly
        // against a real Kestrel host, where a caller authenticated as `probe-caller` produced
        // `Principal == null` while `HttpContext.User` carried the subject. Reading the context
        // property would have made every 403 record report an absent subject, which is the single field
        // that makes a 403 record worth writing.
        ClaimsPrincipal verified = context.HttpContext.User;

        logger.LogWarning(
            ForbiddenRecordMessage,
            StatusCodes.Status403Forbidden,
            context.HttpContext.Request.Method,
            DescribeRoute(context.HttpContext),
            DescribeDemandedPolicy(context.HttpContext),
            RenderClaim(verified, JwtRegisteredClaimNames.Subject, ClaimTypes.NameIdentifier),
            RenderClaim(verified, JwtRegisteredClaimNames.Audience, mappedClaimType: null),
            CountScopeValues(verified),
            ResolveCorrelationId(context.HttpContext));
    }

    /// <summary>Names what kind of 401 this is, in the reader's terms rather than the handler's.</summary>
    /// <param name="context">The challenge.</param>
    /// <returns>A fixed classifier.</returns>
    /// <remarks>
    /// THE DISTINCTION AN OPERATOR ACTUALLY NEEDS. "No credential presented" is ordinary traffic - a
    /// probe, a misconfigured client, a browser following a link. "A credential was presented and
    /// refused" is the one worth alerting on, because it means somebody had something they believed
    /// would work. The two arrive on the same event and the same status, so a record that did not
    /// separate them would leave the interesting case buried in the uninteresting one.
    /// </remarks>
    private static string DescribeChallengeDecision(JwtBearerChallengeContext context) =>
        context.HttpContext.Items.ContainsKey(ReasonClassItemKey) || !string.IsNullOrEmpty(context.Error)
            ? "a presented credential was refused"
            : "no credential was presented";

    /// <summary>Reads the stashed reason class, or reports that nothing was validated.</summary>
    /// <param name="context">The challenge.</param>
    /// <returns>The class name, bounded and escaped.</returns>
    private static string ResolveReasonClass(JwtBearerChallengeContext context) =>
        context.HttpContext.Items.TryGetValue(ReasonClassItemKey, out object? stashed)
            && stashed is string reason
                ? LogSafeText.Render(reason, ClaimRenderLimit)
                : NoCredentialPresentedReason;

    /// <summary>Names the policy the refused route demanded.</summary>
    /// <param name="httpContext">The request.</param>
    /// <returns>The policy name, or the absent marker.</returns>
    /// <remarks>
    /// THE POLICY NAME IS THE SCOPE NAME in this estate - one spelling requested by the caller, minted
    /// into the claim, named by the route and compared by the handler - so naming the policy tells a
    /// reader exactly which entitlement was missing without this file having to reach into the
    /// authorization result. It is this service's own route metadata, never caller input.
    /// </remarks>
    private static string DescribeDemandedPolicy(HttpContext httpContext)
    {
        string? policy = httpContext.GetEndpoint()?.Metadata
            .GetOrderedMetadata<IAuthorizeData>()
            .Select(static data => data.Policy)
            .LastOrDefault(static named => !string.IsNullOrEmpty(named));

        return string.IsNullOrEmpty(policy) ? AbsentClaim : policy;
    }

    /// <summary>Names the route pattern the request matched.</summary>
    /// <param name="httpContext">The request.</param>
    /// <returns>The pattern, or the unrouted marker.</returns>
    /// <remarks>
    /// THE PATTERN AND NEVER THE PATH, matching the posture the projection's records already take: a
    /// pattern names this system's own route table, whereas a path carries whatever the caller put in
    /// it - including a session identifier that correlates to their data.
    /// </remarks>
    private static string DescribeRoute(HttpContext httpContext)
    {
        string? pattern = (httpContext.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText;

        return string.IsNullOrEmpty(pattern) ? UnroutedRouteDescription : pattern;
    }

    /// <summary>Renders one claim for a record, bounded and control-escaped.</summary>
    /// <param name="principal">The principal, which may carry no claims at all.</param>
    /// <param name="claimType">The claim to read, spelled as the token carries it.</param>
    /// <param name="mappedClaimType">
    /// The framework's remapped spelling of the same claim, read when the raw one is absent, or
    /// <see langword="null"/> where the claim has no remapped form.
    /// </param>
    /// <returns>The rendered value, or the absent marker.</returns>
    /// <remarks>
    /// <para>
    /// 🔴 BOTH SPELLINGS ARE READ, BECAUSE THE THREE SERVICES THAT USE THIS FILE DO NOT AGREE ON WHICH
    /// ONE ARRIVES. Gateway, DataServices and Security all set <c>MapInboundClaims</c> to false, so a
    /// subject arrives as <c>sub</c>; Persistence assigns it nowhere and therefore takes the framework
    /// default, which remaps <c>sub</c> onto a long SOAP-era URI. Reading only the raw spelling would
    /// have made every Persistence record report an absent subject - the one field that makes the record
    /// worth writing - and it would have done so silently, since an absent claim is a legitimate outcome
    /// on an unauthenticated request and so reads as correct.
    /// </para>
    /// <para>
    /// EVERY VALUE GOES THROUGH <see cref="LogSafeText"/>, including on the 403 path where the claim IS
    /// verified. Verified means the issuer signed it, not that it contains no newline - and a record is
    /// read out of a stream where an embedded line break forges a record boundary.
    /// </para>
    /// </remarks>
    private static string RenderClaim(
        ClaimsPrincipal? principal,
        string claimType,
        string? mappedClaimType)
    {
        string? value = principal?.FindFirst(claimType)?.Value;

        if (string.IsNullOrEmpty(value) && mappedClaimType is not null)
        {
            value = principal?.FindFirst(mappedClaimType)?.Value;
        }

        return string.IsNullOrEmpty(value) ? AbsentClaim : LogSafeText.Render(value, ClaimRenderLimit);
    }

    /// <summary>Reports whether a scope claim was present, and how many values it carried.</summary>
    /// <param name="principal">The principal.</param>
    /// <returns>A fixed classifier with a count.</returns>
    /// <remarks>
    /// A PRESENCE AND A COUNT, NOT THE VALUES, on the 401 path. The scope set a caller ASKS for is
    /// caller-controlled text on an unvalidated credential, and recording it would let a caller write
    /// arbitrary strings into the log by requesting them. The count still answers the question an
    /// operator has - was this a credential shaped like one of ours, or noise.
    /// </remarks>
    private static string DescribeScopeClaim(ClaimsPrincipal? principal)
    {
        int count = CountScopeValues(principal);

        return count == 0
            ? "absent or empty"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"present, carrying {count} value(s) (values deliberately not recorded)");
    }

    /// <summary>Counts the space-separated values across every scope claim.</summary>
    /// <param name="principal">The principal.</param>
    /// <returns>The count, which is zero when no claim is present.</returns>
    /// <remarks>
    /// SPACE-SEPARATED WITHIN A CLAIM AND SUMMED ACROSS CLAIMS, which is how the sibling scope handler
    /// reads the same claim - RFC 8693 carries a scope set as one space-delimited string, and a token
    /// may carry more than one claim of that name.
    /// </remarks>
    private static int CountScopeValues(ClaimsPrincipal? principal)
    {
        if (principal is null)
        {
            return 0;
        }

        int count = 0;

        foreach (Claim claim in principal.FindAll(ScopeClaimName))
        {
            count += CountNonEmptySegments(claim.Value);
        }

        return count;
    }

    /// <summary>Counts the non-empty space-separated segments of a claim value.</summary>
    /// <param name="value">The claim value.</param>
    /// <returns>The count.</returns>
    private static int CountNonEmptySegments(string value)
    {
        int count = 0;
        bool inSegment = false;

        foreach (char character in value)
        {
            if (character == ' ')
            {
                inSegment = false;

                continue;
            }

            if (!inSegment)
            {
                inSegment = true;
                count++;
            }
        }

        return count;
    }

    /// <summary>Resolves the correlation identifier a record is joined on.</summary>
    /// <param name="httpContext">The request.</param>
    /// <returns>The identifier.</returns>
    /// <remarks>
    /// THE SAME RESOLUTION THE PROBLEM DOCUMENTS PUBLISH, so a caller holding a <c>traceId</c> from a
    /// refusal body and an operator reading this record are joining on one identifier rather than two.
    /// </remarks>
    private static string ResolveCorrelationId(HttpContext httpContext)
    {
        string? activityId = System.Diagnostics.Activity.Current?.Id;

        return string.IsNullOrEmpty(activityId)
            ? httpContext.TraceIdentifier ?? string.Empty
            : activityId;
    }

    /// <summary>Resolves the logger for the dedicated refusal category.</summary>
    /// <param name="httpContext">The request.</param>
    /// <returns>The logger.</returns>
    private static ILogger ResolveLogger(HttpContext httpContext) =>
        httpContext.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(LoggerCategory);

    /// <summary>The registered claim names this record reads, spelled once.</summary>
    /// <remarks>
    /// THE SPELLING A TOKEN ACTUALLY CARRIES, which is what three of the four services in this estate
    /// present because they set <c>MapInboundClaims</c> to false. It is tried FIRST and the remapped
    /// spelling second - see <see cref="RenderClaim"/> for the service that needs the second.
    /// </remarks>
    private static class JwtRegisteredClaimNames
    {
        /// <summary>The subject claim.</summary>
        internal const string Subject = "sub";

        /// <summary>The audience claim.</summary>
        internal const string Audience = "aud";
    }
}
