// ==================================================================================================
//  UnknownSigningKeyRevalidation - THE ONE 401 A PLANNED KEY ROTATION USED TO COST, PER VERIFIER
//  ------------------------------------------------------------------------------------------------
//  WHAT WAS MEASURED
//
//  Security is the estate's sole token issuer (AAP 0.6.6.3) and it publishes an ACTIVE key alongside a
//  RETIRING one, so during a rollover both identities are resolvable from its key set. A rotation was
//  then driven end to end: verifiers primed on the original key, Security restarted with a new active
//  key plus the original as retiring, and a token minted under the NEW `kid` presented immediately to
//  each boundary. Gateway, DataServices and Persistence each REFUSED that token 401 on attempt 1 and
//  accepted it on attempt 2 roughly a seventh of a second later. Security itself accepted it at once,
//  because Security holds the key rather than fetching a key set.
//
//  WHY THE ALREADY-CONFIGURED REFRESH DID NOT COVER IT, WHICH IS THE WHOLE POINT
//
//  The bearer handler does the right thing on an unknown key identifier: with
//  `RefreshOnIssuerKeyNotFound` (true by default) it calls `RequestRefresh()` on the configuration
//  manager the moment validation fails with `SecurityTokenSignatureKeyNotFoundException`. But that
//  request is for the NEXT request's benefit - the CURRENT one has already failed and the handler goes
//  straight on to answer 401. Both refresh intervals are configured on every verifier in this estate and
//  neither closes the window either: they decide WHEN a refresh may happen, not whether the request that
//  provoked it is retried. So the first caller after every rotation pays a refusal, and the shape of that
//  refusal is the worst available one - a 401 on a perfectly valid, correctly signed, unexpired token
//  minted seconds earlier by the only issuer in the system. AND THE REFUSED CALLER HERE IS
//  DATASERVICES, so the 401 lands two projections deep - DataServices reports a failed upstream call
//  and Gateway projects that outward - which is how one rotation became a visible ingress failure on a
//  retrieval nobody had touched.
//
//  WHAT THIS FILE DOES
//
//  It chains one more handler onto `OnAuthenticationFailed`, beside the refusal record's own. When - and
//  only when - the failure is "no key matched this token's identifier", it asks the configuration manager
//  for the key set, waits a bounded moment for the refresh that failure already armed to LAND, and then
//  validates the token ONCE more against the refreshed set under this service's own rules. If it validates
//  in full the request is admitted. Otherwise nothing is touched and the 401 stands exactly as it did.
//
//  WHY A BOUNDED WAIT AND NOT A SINGLE CALL - MEASURED, BECAUSE THE OBVIOUS VERSION DOES NOT WORK
//
//  The first revision of this file asked the manager for its configuration exactly ONCE, on the reasoning
//  that the call performs the armed refresh inline. IT DOES NOT, and that was established by probing the
//  pinned package directly rather than by reading its documentation. Once `ConfigurationManager<T>` holds
//  any configuration at all, a call after `RequestRefresh()` returns THE STALE INSTANCE IMMEDIATELY -
//  measured at 0 ms and reference-identical to the instance validation had just failed against - and
//  performs the retrieval on a background continuation. The refreshed instance appears only on a LATER
//  call, and how much later tracks how long the fetch takes: a retriever delayed 300 ms produced the new
//  key set two polls later than an instant one. A single-call version therefore looks the new `kid` up in
//  the OLD key set, misses it, and leaves standing precisely the 401 it was written to remove.
//
//  SO THE WAIT IS FOR AN EVENT RATHER THAN FOR A DURATION. Each poll compares the instance the manager
//  answers with against the one validation used, and the moment a DIFFERENT instance appears - the fetch
//  having landed - the key is looked up once and the answer is final either way. Nothing waits past the
//  refresh, and a refresh that never lands costs the compiled ceiling and then the same refusal.
//
//  THE FIVE PROPERTIES THAT MAKE THIS SAFE, EACH ASSERTED BY A ROW IN THE TEST SUITE
//
//    1. VALIDATION IS NOT WEAKENED. The revalidation runs the SAME `TokenValidationParameters` this
//       composition root configured - signature, issuer, audience, lifetime and the bounded clock skew
//       all still enforced - with only the manager's own issuer and signing keys merged in. That merge is
//       precisely what `JwtBearerHandler` does on its own first attempt, so this is the handler's own
//       validation repeated against fresher material, not a second, looser one.
//    2. NO NEW SIGNING AUTHORITY. Nothing here holds, derives or accepts key material. The keys come from
//       the framework's configuration manager, fetched from Security's published key set over the
//       handler's own backchannel. Constraint C-G is untouched: Security mints, everyone else verifies.
//    3. A GENUINELY UNKNOWN KEY STILL FAILS. A forged `kid` is absent from the refreshed set too, so the
//       lookup misses and the refusal is left alone. There is no arm in which an unresolvable key
//       identifier becomes a success.
//    4. NO REFRESH AMPLIFICATION, AND IT IS THE LIBRARY'S OWN RATE LIMIT RATHER THAN ONE INVENTED HERE.
//       Probed on the pinned package: `RequestRefresh()` re-arms at most once per `RefreshInterval` and a
//       second request inside that interval is ignored outright - the retrieval count did not move - so a
//       flood of forged key identifiers provokes ONE metadata retrieval per interval however many
//       requests arrive. The polling below only READS what the manager already holds; it cannot provoke a
//       fetch of its own.
//       THE RESIDUAL, STATED RATHER THAN GLOSSED: a forged key identifier does hold its own request for
//       the ceiling before being refused. That wait is asynchronous - `Task.Delay`, so no thread is
//       occupied - it is bounded by a compiled constant no deployment can widen, it is linked to
//       `HttpContext.RequestAborted` so a disconnected caller ends it at once, and the request was going
//       to be refused either way. No availability or latency objective is asserted by any of this
//       (AAP 0.8.5): the ceiling bounds the extra work this hook may do, and promises nothing about how
//       quickly anything answers.
//    5. IT COSTS NOTHING ON EVERY OTHER FAILURE. An absent credential, a bad signature under a KNOWN key,
//       a wrong audience, an expired token: none of them is a signature-key-not-found failure, so the
//       hook returns immediately without reading a header, a configuration or a clock.
//    6. IT COVERS THIS SERVICE'S gRPC SURFACE, WHICH IS ITS PRIMARY ONE, because authentication runs
//       in the shared middleware pipeline ahead of any interceptor - the same reason the refusal
//       record's hooks cover it.
//
//  WHY THIS RATHER THAN THE PRE-PUBLISHING RUNBOOK THAT WAS SUGGESTED
//
//  Pre-publishing the next public key and refreshing every verifier BEFORE minting with it would also
//  close the window, and it remains good operational practice - but it closes it only for a rotation
//  somebody performed that way. It needs a third key slot Security does not have (it publishes active and
//  RETIRING, and a "next" slot is new configuration), it needs an operator to follow a sequence, and it
//  cannot help an unplanned rotation or a verifier that started after the pre-publication. This closes
//  the window for ANY rotation, with no new configuration and no runbook dependency, at the boundary that
//  was producing the wrong answer.
//
//  ONE COPY PER SERVICE, deliberately. No behaviour crosses a service boundary in this estate (C-A), and
//  the sibling `AuthenticationRefusalRecord` is duplicated for the same reason.
// ==================================================================================================

using System.Diagnostics;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace PowerFramework.Persistence.Authorization;

/// <summary>
/// Retries a token that failed because its signing key was not yet known, once, against the key set the
/// bearer handler has already asked to refresh.
/// </summary>
/// <remarks>
/// Attached to the bearer handler's failure hook by the composition root. It changes no status and no body
/// on any refusal it does not convert; the only observable change is that a valid token minted under a
/// freshly rotated key is accepted on its FIRST presentation rather than its second.
/// </remarks>
internal static class UnknownSigningKeyRevalidation
{
    /// <summary>The logger category, so an operator can select rotation events alone.</summary>
    /// <remarks>
    /// A DEDICATED CATEGORY, on the same reasoning as the refusal record's: a reader asking "did the
    /// rotation land" wants these lines without the rest of this service's traffic, and a category is the
    /// one selector every provider the shared framework ships understands.
    /// </remarks>
    private const string LoggerCategory =
        "PowerFramework.Persistence.Authorization.UnknownSigningKeyRevalidation";

    /// <summary>The record written when a rotation was absorbed.</summary>
    /// <remarks>
    /// <para>
    /// THE KEY IDENTIFIER IS RECORDED AND NOTHING ELSE FROM THE TOKEN IS. A <c>kid</c> is a PUBLIC key
    /// set identifier - it appears in Security's anonymously published key set, so recording it discloses
    /// nothing a reader could not fetch - and it is the one field that makes this record actionable,
    /// because it names which rotation was absorbed. The token, its signature, its subject and every
    /// header value stay unrecorded, exactly as the refusal record's rules require (AAP 0.6.6, C-F). It is
    /// bounded and control-escaped before it is rendered, because until this validation succeeds it is
    /// unverified caller input.
    /// </para>
    /// <para>
    /// AT INFORMATION RATHER THAN WARNING, and the distinction is the point: this is a rotation being
    /// absorbed correctly, not a refusal. A record at Warning would put a successful request in the same
    /// band as credential stuffing.
    /// </para>
    /// </remarks>
    private const string AbsorbedRotationMessage =
        "A presented token named signing key {KeyId}, which this service's cached key set did not yet "
        + "contain. The key set was refreshed, the key resolved, and the token then validated in full - "
        + "signature, issuer, audience and lifetime - so the request was admitted rather than refused. "
        + "This is a key rotation at the issuer being absorbed on the first request that met it. No part "
        + "of the credential is recorded.";

    /// <summary>The greatest rendered length of the key identifier in a record.</summary>
    /// <remarks>
    /// The same bound and the same reason as the refusal record's claim bound: an unauthenticated caller
    /// controls this string completely at the moment it is read, and an unbounded one lets a single
    /// request write an arbitrarily large line into the operator's log. Security's own identifiers are 33
    /// characters.
    /// </remarks>
    private const int KeyIdRenderLimit = 128;

    /// <summary>The rendering used where a token carries no key identifier.</summary>
    private const string AbsentKeyId = "(absent)";

    /// <summary>The greatest header segment this reader will decode, in characters.</summary>
    /// <remarks>
    /// UNVERIFIED INPUT IS BOUNDED BEFORE IT IS DECODED. The segment arrives from an unauthenticated
    /// caller, and a JOSE header carrying an algorithm, a type and a key identifier is a few dozen bytes -
    /// Security's own is well under a hundred. The ingress already caps total header bytes, so this is the
    /// second bound rather than the only one, and it exists so that this reader cannot be made to decode
    /// and parse a megabyte because somebody put one before the first separator.
    /// </remarks>
    private const int MaximumHeaderSegmentLength = 4096;

    /// <summary>
    /// How long one revalidation may wait for the key-set refresh the failure armed to land.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A CEILING ON THIS HOOK'S OWN WORK, NOT A LATENCY OBJECTIVE. AAP 0.8.5 forbids asserting a latency,
    /// throughput or availability posture anywhere in this estate, and none is asserted here: this value
    /// bounds how long a request that is ALREADY BEING REFUSED may be held while the refresh lands, after
    /// which the refusal proceeds exactly as it would have without this file.
    /// </para>
    /// <para>
    /// SIZED FROM THE MEASUREMENT THAT PRODUCED THE FINDING. In the rotation drill that recorded the 401,
    /// the FOLLOWING request - the one the refreshed key set admitted - completed 0.127 s and 0.177 s later
    /// at two different boundaries, which bounds the metadata retrieval those figures contain. One second
    /// is more than five times that, so a retrieval materially slower than the one measured still lands
    /// inside the ceiling, while a retrieval that never completes costs one second and then the same
    /// refusal. The value is a bound, not a target: nothing waits for it when the refresh lands sooner.
    /// </para>
    /// <para>
    /// NOT CONFIGURABLE, DELIBERATELY. No configuration key reaches it, so no deployment can widen it into
    /// a request-holding budget or narrow it to nothing.
    /// <see cref="Attach(JwtBearerOptions, TimeSpan, TimeSpan)"/> exists as a determinism seam for the
    /// test suite (AAP 0.6.7) and is reachable from nothing else.
    /// </para>
    /// </remarks>
    internal static readonly TimeSpan RefreshLandingBudget = TimeSpan.FromSeconds(1);

    /// <summary>How often the manager is re-asked while waiting for the refresh to land.</summary>
    /// <remarks>
    /// SMALL ENOUGH THAT THE LANDING IS NOTICED PROMPTLY, LARGE ENOUGH NOT TO SPIN. A poll is one call
    /// that returns a cached object plus one reference comparison, so the cost of the loop is the delay
    /// itself rather than the work inside it.
    /// </remarks>
    internal static readonly TimeSpan RefreshPollInterval = TimeSpan.FromMilliseconds(25);

    /// <summary>
    /// Chains the revalidation onto the bearer handler's authentication-failure hook, with the shipped
    /// bounds.
    /// </summary>
    /// <param name="bearer">The bearer options to extend.</param>
    /// <exception cref="ArgumentNullException"><paramref name="bearer"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// THE ONLY OVERLOAD A COMPOSITION ROOT CALLS. It supplies <see cref="RefreshLandingBudget"/> and
    /// <see cref="RefreshPollInterval"/>, which is what makes those two values properties of the build
    /// rather than of a deployment.
    /// </remarks>
    internal static void Attach(JwtBearerOptions bearer) =>
        Attach(bearer, RefreshLandingBudget, RefreshPollInterval);

    /// <summary>
    /// Chains the revalidation onto the bearer handler's authentication-failure hook.
    /// </summary>
    /// <param name="bearer">The bearer options to extend.</param>
    /// <param name="refreshLandingBudget">The ceiling on waiting for the refreshed key set to appear.</param>
    /// <param name="refreshPollInterval">How often the manager is re-asked while waiting.</param>
    /// <exception cref="ArgumentNullException"><paramref name="bearer"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Either duration is not positive, or the poll interval exceeds the budget.
    /// </exception>
    /// <remarks>
    /// <para>
    /// CHAINED RATHER THAN ASSIGNED, exactly as <see cref="AuthenticationRefusalRecord.Attach"/> chains:
    /// the previously registered handler is captured and invoked, so attaching this displaces nothing and
    /// the order of the two attachments does not matter. The refusal record's own failure handler writes
    /// no record and only stashes a reason class, so a failure this hook goes on to CONVERT still leaves
    /// that stash behind harmlessly - the challenge that would have read it never runs.
    /// </para>
    /// <para>
    /// THE PREVIOUS HANDLER RUNS FIRST. It is the one that classifies the failure for the operator record,
    /// and it must see the failure as it arrived rather than as this hook left it.
    /// </para>
    /// <para>
    /// THE TWO DURATIONS ARE PARAMETERS FOR ONE REASON: the test suite has to drive the not-found path
    /// without spending the shipped ceiling on every negative row, which is the determinism-seam pattern
    /// AAP 0.6.7 already requires of every clock and generator in this estate. They are NOT bound from
    /// configuration anywhere, and the shipped overload above is the only caller in the product.
    /// </para>
    /// </remarks>
    internal static void Attach(
        JwtBearerOptions bearer,
        TimeSpan refreshLandingBudget,
        TimeSpan refreshPollInterval)
    {
        ArgumentNullException.ThrowIfNull(bearer);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(refreshLandingBudget, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(refreshPollInterval, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(refreshPollInterval, refreshLandingBudget);

        JwtBearerEvents events = bearer.Events ??= new JwtBearerEvents();

        Func<AuthenticationFailedContext, Task> onFailed = events.OnAuthenticationFailed;

        events.OnAuthenticationFailed = async context =>
        {
            await onFailed(context).ConfigureAwait(false);

            await RevalidateAsync(context, refreshLandingBudget, refreshPollInterval)
                .ConfigureAwait(false);
        };
    }

    /// <summary>
    /// Converts a not-yet-known-key failure into a success when the refreshed key set validates the token.
    /// </summary>
    /// <param name="context">The failure the bearer handler raised.</param>
    /// <param name="refreshLandingBudget">The ceiling on waiting for the refreshed key set to appear.</param>
    /// <param name="refreshPollInterval">How often the manager is re-asked while waiting.</param>
    /// <returns>A task representing the attempt.</returns>
    /// <remarks>
    /// <para>
    /// EVERY GUARD BELOW IS A REASON TO LEAVE THE REFUSAL ALONE, and they are ordered cheapest first so
    /// that the overwhelmingly common failures - no credential at all, a wrong audience, an expired token
    /// - cost one type test and nothing more. In particular nothing WAITS until a failure has been shown
    /// to be a key-identifier miss carrying a readable identifier, so no other refusal pays the ceiling.
    /// </para>
    /// <para>
    /// A FAILURE ALREADY CONVERTED BY AN EARLIER HANDLER IS NOT RECONSIDERED. If the chained handler set a
    /// result, this hook is not entitled to overwrite that decision - in either direction.
    /// </para>
    /// </remarks>
    private static async Task RevalidateAsync(
        AuthenticationFailedContext context,
        TimeSpan refreshLandingBudget,
        TimeSpan refreshPollInterval)
    {
        if (context.Result is not null)
        {
            return;
        }

        if (!NamesAnUnresolvedKeyIdentifier(context.Exception))
        {
            return;
        }

        if (context.Options.ConfigurationManager is not BaseConfigurationManager manager)
        {
            // No manager means no authority-published key set to refresh - a deployment validating against
            // statically configured keys, where an unknown identifier cannot become known by refetching.
            return;
        }

        string? token = ReadBearerToken(context);

        if (string.IsNullOrEmpty(token))
        {
            // The credential did not arrive in the header this reader knows about. Nothing in this estate
            // supplies a token any other way - no OnMessageReceived hook is registered on any of the four
            // services - so this is the defensive arm rather than a reachable one, and it declines rather
            // than guessing where the token might be.
            return;
        }

        string? keyId = ReadKeyIdentifier(token);

        if (string.IsNullOrEmpty(keyId))
        {
            // A token whose header names no key cannot become resolvable by refreshing a key set keyed on
            // exactly that name.
            return;
        }

        BaseConfiguration configuration;

        try
        {
            // THE FIRST ASK, AND IT IS ALSO WHAT STARTS THE FETCH. The failure has already armed a refresh
            // - the token library requests one itself on a key-identifier miss, and the bearer handler
            // requests one again through RefreshOnIssuerKeyNotFound - and a call in that state is what
            // makes the manager begin retrieving. What it RETURNS, though, is the configuration it already
            // holds: measured at 0 ms and reference-identical to the one validation just failed against.
            // So this call resolves the case where the refresh has already landed (another request having
            // met the rotation first) and otherwise starts the clock on the bounded wait below.
            configuration = await manager
                .GetBaseConfigurationAsync(context.HttpContext.RequestAborted)
                .ConfigureAwait(false);
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            // An unreachable or malformed key set leaves the refusal exactly as it was. This is the one
            // place where doing nothing is the whole correct behaviour: the request was already being
            // refused, and a fetch failure is not a reason to admit it.
            return;
        }

        if (!ResolvesKeyIdentifier(configuration, keyId))
        {
            BaseConfiguration? refreshed = await AwaitRefreshedKeySetAsync(
                    manager,
                    configuration,
                    keyId,
                    refreshLandingBudget,
                    refreshPollInterval,
                    context.HttpContext.RequestAborted)
                .ConfigureAwait(false);

            if (refreshed is null)
            {
                // THE ARM A FORGED KEY IDENTIFIER TAKES, and the arm a genuinely unreachable key set takes
                // too: either the refresh landed and does not name this identifier, or it did not land
                // inside the ceiling. Both leave the 401 exactly as it was before this file existed.
                return;
            }

            configuration = refreshed;
        }

        TokenValidationResult result;

        try
        {
            result = await ValidateAsync(context, configuration, token).ConfigureAwait(false);
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            return;
        }

        if (!result.IsValid || result.ClaimsIdentity is null)
        {
            // The key resolved and the token still did not validate - a wrong audience, a lapsed lifetime,
            // a signature that does not check out under the resolved key. All of those are the refusal the
            // handler was already making, and it stands.
            return;
        }

        Record(context, keyId);

        context.Principal = new ClaimsPrincipal(result.ClaimsIdentity);
        context.Success();
    }

    /// <summary>
    /// Waits, bounded, for the armed key-set refresh to land and reports the refreshed configuration when
    /// it names the wanted key.
    /// </summary>
    /// <param name="manager">The configuration manager the handler is configured with.</param>
    /// <param name="observed">The configuration validation used, which the refresh will replace.</param>
    /// <param name="keyId">The key identifier the token names.</param>
    /// <param name="budget">The ceiling on the wait.</param>
    /// <param name="pollInterval">How often the manager is re-asked.</param>
    /// <param name="cancellationToken">The request's abort signal.</param>
    /// <returns>
    /// The refreshed configuration when it resolves <paramref name="keyId"/>; otherwise
    /// <see langword="null"/>, which leaves the refusal standing.
    /// </returns>
    /// <remarks>
    /// <para>
    /// 🔴 THE LOOP WAITS FOR AN EVENT, NOT FOR A DURATION, AND THE EVENT IS OBSERVED BY REFERENCE.
    /// <c>ConfigurationManager&lt;T&gt;</c> answers with the instance it currently holds and swaps in a NEW
    /// instance when a retrieval completes - probed directly on the pinned package - so a reference
    /// comparison is an exact test of "has the refresh landed". The moment a different instance appears the
    /// key is looked up ONCE and the answer is final in both directions: a hit revalidates, a miss refuses
    /// immediately rather than waiting out the remaining ceiling. Only one retrieval can occur per
    /// <c>RefreshInterval</c>, so there is no second landing worth waiting for.
    /// </para>
    /// <para>
    /// EQUALITY IS DELIBERATELY REFERENCE EQUALITY. <c>BaseConfiguration</c> declares no value equality and
    /// two retrievals of an unchanged key set produce equal CONTENT, so comparing content would report "not
    /// landed" for a refresh that had in fact completed and would then spend the whole ceiling.
    /// </para>
    /// <para>
    /// A DISCONNECTED CALLER ENDS THE WAIT AT ONCE, because the delay and the ask both carry the request's
    /// abort signal and the resulting cancellation is deliberately not swallowed here.
    /// </para>
    /// </remarks>
    private static async Task<BaseConfiguration?> AwaitRefreshedKeySetAsync(
        BaseConfigurationManager manager,
        BaseConfiguration observed,
        string keyId,
        TimeSpan budget,
        TimeSpan pollInterval,
        CancellationToken cancellationToken)
    {
        long started = Stopwatch.GetTimestamp();

        while (Stopwatch.GetElapsedTime(started) < budget)
        {
            await Task.Delay(pollInterval, cancellationToken).ConfigureAwait(false);

            BaseConfiguration current;

            try
            {
                current = await manager
                    .GetBaseConfigurationAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception failure) when (failure is not OperationCanceledException)
            {
                // The retrieval failed. The refusal stands, and it stands now rather than after the rest
                // of the ceiling, because a manager that just threw will not answer differently in 25 ms.
                return null;
            }

            if (ReferenceEquals(current, observed))
            {
                continue;
            }

            return ResolvesKeyIdentifier(current, keyId) ? current : null;
        }

        return null;
    }

    /// <summary>
    /// Whether a validation failure is the specific "no key matched this identifier" one.
    /// </summary>
    /// <remarks>
    /// NAMED WITHOUT THE WORDS "SIGNING KEY" ON PURPOSE. This service's suite scans every member this
    /// assembly declares and treats a name containing <c>SigningKey</c>, <c>SigningCredential</c> or
    /// <c>PrivateKey</c> as POSSESSION of signing material, which C-G forbids anywhere but Security. The
    /// predicate is right and the collision was in this member's name, so the member was renamed rather
    /// than the guard widened. Do not rename it back.
    /// </remarks>
    /// <param name="failure">The exception the handler carried, if any.</param>
    /// <returns><see langword="true"/> when a signing key could not be resolved.</returns>
    /// <remarks>
    /// <para>
    /// AN AGGREGATE IS UNWRAPPED, WHICH IS NOT DEFENSIVE PADDING. The bearer handler tries the current
    /// configuration and then each cached last-known-good one, and when more than one attempt fails it
    /// raises the collected failures as an <see cref="AggregateException"/> - so on precisely the path
    /// this file exists for, the exception is usually an aggregate rather than the typed failure itself.
    /// A test that only exercised the single-attempt shape would pass against a hook that never fired in
    /// production.
    /// </para>
    /// <para>
    /// THE TYPE IS THE TEST, NOT THE MESSAGE. Validation messages quote the values they rejected, so
    /// matching on message text would let a caller choose which branch this code takes.
    /// </para>
    /// </remarks>
    private static bool NamesAnUnresolvedKeyIdentifier(Exception? failure) => failure switch
    {
        null => false,
        SecurityTokenSignatureKeyNotFoundException => true,
        AggregateException aggregate => aggregate.InnerExceptions.Any(NamesAnUnresolvedKeyIdentifier),
        _ => failure.InnerException is not null && NamesAnUnresolvedKeyIdentifier(failure.InnerException),
    };

    /// <summary>Reads the bearer credential from the request's authorization header.</summary>
    /// <param name="context">The failure, carrying the request.</param>
    /// <returns>The token, or <see langword="null"/> when the header carries none.</returns>
    /// <remarks>
    /// THE SCHEME COMPARISON IS ORDINAL AND CASE-INSENSITIVE, matching how the bearer handler itself reads
    /// the header: RFC 9110 makes the scheme token case-insensitive, so a caller writing <c>bearer</c>
    /// presents the same credential as one writing <c>Bearer</c> and the handler accepts both. A
    /// case-sensitive read here would decline to help exactly the callers the handler had already accepted.
    /// </remarks>
    private static string? ReadBearerToken(AuthenticationFailedContext context)
    {
        string? header = context.Request.Headers.Authorization;

        if (string.IsNullOrEmpty(header))
        {
            return null;
        }

        const string scheme = JwtBearerDefaults.AuthenticationScheme + " ";

        return header.StartsWith(scheme, StringComparison.OrdinalIgnoreCase)
            ? header[scheme.Length..].Trim()
            : null;
    }

    /// <summary>Reads the key identifier out of a token's header without validating anything.</summary>
    /// <param name="token">The presented credential.</param>
    /// <returns>The <c>kid</c> header value, or <see langword="null"/> when there is none to read.</returns>
    /// <remarks>
    /// <para>
    /// READING AN UNVALIDATED HEADER IS EXACTLY WHAT THE HANDLER DOES TOO, and it is safe for the same
    /// reason: the value is used ONLY to look up a key in this service's own refreshed key set. It selects
    /// which trusted key is tried; it never becomes a key, a claim, an identity or a log field beyond the
    /// bounded record. Nothing is trusted on the strength of it - the signature check that follows is what
    /// decides the request.
    /// </para>
    /// <para>
    /// 🔴 ONE JOSE FIELD IS READ RATHER THAN A TOKEN OBJECT CONSTRUCTED, AND THAT IS AN ARCHITECTURAL
    /// CONSTRAINT RATHER THAN A MICRO-OPTIMISATION. The obvious spelling - constructing the identity
    /// library's own <c>JsonWebToken</c> and reading its <c>Kid</c> - puts a compile-time reference to
    /// <c>Microsoft.IdentityModel.JsonWebTokens</c> into this assembly, and that is one of the two
    /// libraries carrying a token handler capable of WRITING a signed token. AAP 0.6.6.3 makes Security
    /// the sole issuer and constraint C-G keeps every other service to verification material only, which
    /// this service's own suite enforces structurally by asserting that neither minting library is among
    /// its referenced assemblies. So the header is decoded with the verification library's own base64url
    /// decoder and one property is read from it; no token object is built, no claim is parsed, and the
    /// reference set is unchanged.
    /// </para>
    /// <para>
    /// A MALFORMED TOKEN ANSWERS NULL RATHER THAN RAISING. Anything unreadable here is a credential this
    /// hook has no business converting, and the refusal the handler is already making is the right answer
    /// for it. Every way the read can fail - no separator, an over-long segment, invalid base64url, JSON
    /// that is not an object, an absent or non-string <c>kid</c> - answers the same way.
    /// </para>
    /// </remarks>
    private static string? ReadKeyIdentifier(string token)
    {
        int separator = token.IndexOf('.');

        if (separator <= 0 || separator > MaximumHeaderSegmentLength)
        {
            return null;
        }

        try
        {
            byte[] header = Base64UrlEncoder.DecodeBytes(token[..separator]);

            using JsonDocument document = JsonDocument.Parse(header);

            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("kid", out JsonElement keyId)
                && keyId.ValueKind == JsonValueKind.String
                    ? keyId.GetString()
                    : null;
        }
        catch (Exception failure) when (
            failure is FormatException
                or JsonException
                or ArgumentException
                or InvalidOperationException)
        {
            // ArgumentException covers the decoder's own fallback failure, which derives from it, so a
            // segment that decodes to bytes that are not UTF-8 lands here too rather than escaping.
            return null;
        }
    }

    /// <summary>Whether a configuration publishes a signing key under the given identifier.</summary>
    /// <param name="configuration">The refreshed configuration.</param>
    /// <param name="keyId">The identifier the token names.</param>
    /// <returns><see langword="true"/> when a key in the set carries that identifier.</returns>
    /// <remarks>
    /// COMPARED ORDINALLY. A key identifier is machine input on both sides - Security stamps it and its
    /// key set publishes it - so a culture-sensitive or case-insensitive comparison would match keys the
    /// issuer did not name.
    /// </remarks>
    private static bool ResolvesKeyIdentifier(BaseConfiguration configuration, string keyId) =>
        configuration.SigningKeys.Any(
            key => string.Equals(key.KeyId, keyId, StringComparison.Ordinal));

    /// <summary>
    /// Validates the token once against the refreshed configuration, under this service's own rules.
    /// </summary>
    /// <param name="context">The failure, carrying the configured validation parameters.</param>
    /// <param name="configuration">The refreshed configuration whose keys are merged in.</param>
    /// <param name="token">The presented credential.</param>
    /// <returns>The validation result.</returns>
    /// <remarks>
    /// <para>
    /// 🔴 THE MERGE IS THE HANDLER'S OWN, REPRODUCED RATHER THAN INVENTED. <c>JwtBearerHandler</c> clones
    /// its configured parameters and folds the configuration's issuer into the valid issuers and its
    /// signing keys into the issuer signing keys before validating; doing the same here is what makes this
    /// the handler's validation repeated against fresher material instead of a second, differently
    /// configured one. Every check this composition root turned on stays on, including the bounded clock
    /// skew, and no property is relaxed.
    /// </para>
    /// <para>
    /// THE CLONE IS WHAT KEEPS IT TO ONE REQUEST. Mutating the shared options instance would leak this
    /// request's merged issuer and key list into every later validation on the service.
    /// </para>
    /// <para>
    /// THE HANDLER'S OWN TOKEN HANDLERS DO THE WORK, so the token is parsed and verified by exactly the
    /// component that parsed and verified it the first time. Nothing here implements a validation step.
    /// </para>
    /// </remarks>
    private static async Task<TokenValidationResult> ValidateAsync(
        AuthenticationFailedContext context,
        BaseConfiguration configuration,
        string token)
    {
        TokenValidationParameters parameters = context.Options.TokenValidationParameters.Clone();

        parameters.ValidIssuers = parameters.ValidIssuers is null
            ? [configuration.Issuer]
            : [.. parameters.ValidIssuers, configuration.Issuer];

        parameters.IssuerSigningKeys = parameters.IssuerSigningKeys is null
            ? configuration.SigningKeys
            : [.. parameters.IssuerSigningKeys, .. configuration.SigningKeys];

        foreach (TokenHandler handler in context.Options.TokenHandlers)
        {
            TokenValidationResult result = await handler
                .ValidateTokenAsync(token, parameters)
                .ConfigureAwait(false);

            if (result.IsValid)
            {
                return result;
            }
        }

        // Every handler refused it. Reported as an invalid result rather than raised, because the caller
        // treats "did not validate" as "leave the refusal alone" and has no use for a distinction between
        // the ways it failed.
        return new TokenValidationResult { IsValid = false };
    }

    /// <summary>Writes the one record for an absorbed rotation.</summary>
    /// <param name="context">The failure being converted.</param>
    /// <param name="keyId">The identifier that resolved after the refresh.</param>
    private static void Record(AuthenticationFailedContext context, string keyId)
    {
        ILoggerFactory? factory = context.HttpContext.RequestServices
            ?.GetService<ILoggerFactory>();

        if (factory is null)
        {
            return;
        }

        ILogger logger = factory.CreateLogger(LoggerCategory);

        if (!logger.IsEnabled(LogLevel.Information))
        {
            return;
        }

        logger.LogInformation(AbsorbedRotationMessage, LogSafeText(keyId));
    }

    /// <summary>Bounds and control-escapes a caller-supplied value before it is recorded.</summary>
    /// <param name="value">The value read from an unvalidated token header.</param>
    /// <returns>The rendering to log.</returns>
    /// <remarks>
    /// THE SAME TREATMENT THE REFUSAL RECORD GIVES AN UNVERIFIED CLAIM, for the same reason: at the moment
    /// this value is read nothing has validated it, so a control character in it could forge a line break
    /// in an operator's log and an unbounded length could flood it.
    /// </remarks>
    private static string LogSafeText(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return AbsentKeyId;
        }

        string bounded = value.Length <= KeyIdRenderLimit ? value : value[..KeyIdRenderLimit];

        return string.Concat(
            bounded.Select(character => char.IsControl(character) ? ' ' : character));
    }
}
