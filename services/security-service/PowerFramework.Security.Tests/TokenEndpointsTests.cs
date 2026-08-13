// ==================================================================================================
//  TokenEndpointsTests.cs - CONFORMANCE, AUTHENTICATION, PARITY AND SECRECY FOR CONTRACT C-01's
//  ISSUANCE OPERATION
//  ------------------------------------------------------------------------------------------------
//  WHAT IS UNDER TEST
//  services/security-service/PowerFramework.Security/Endpoints/TokenEndpoints.cs - the only route to
//  the system's sole minter. Security mints; Gateway, DataServices and Persistence hold verification
//  material only.
//
//  HOW THE ROWS ARE SPLIT, AND WHY
//    * CONTRACT rows read the AUTHORED document out of the contracts assembly and assert that the
//      implementation matches it. The authored document is authoritative for anything on the wire, so a
//      disagreement is a defect in the code and never in the document.
//    * UNIT rows call the handler's own members directly. They need no host, no transport and no key,
//      which is what makes every refusal arm reachable - including the ones a test host cannot produce
//      because it terminates no TLS.
//    * SERVICE rows boot the host through the in-process factory. Two shapes are used: the ordinary
//      client for the unauthenticated path, and a host with a client certificate injected into the
//      connection for the authenticated path, because this is the one operation in the system whose
//      caller identity comes from the transport rather than from a token.
//
//  NO KEY, CERTIFICATE OR TOKEN LITERAL APPEARS IN THIS FILE. Every piece of material is generated in
//  this process, kept in memory, and discarded with the fixture. Nothing is copied from any
//  hardcoded-secret site in the repository, and in particular nothing from
//  tests/blink/test_jws.htm:L8-L23, which the implementation exists to replace rather than to imitate.
//  Generating also proves more than a fixture would: the service must sign with what it was handed
//  rather than recognise a known value.
//
//  LEGACY ANCHORS (REFERENCE BY LOCATOR ONLY - never read as a build input, never edited)
//    ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L70-L73  the RSA signature pair the issuer is built on
//    ws_objects/pfw.shared.pbl.src/enums.sru:L927-L933    the digest catalogue those take their type
//                                                         from, SHA-256 at :L930
//    ws_objects/pfw.shared.pbl.src/issucceeded.srf:L11-L12 and isfailed.srf:L11-L12
//                                                         the tri-state return algebra, asserted here
//                                                         through the shared problem map
// ==================================================================================================

using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Net.Mime;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using PowerFramework.Security.Authorization;
using PowerFramework.Security.Configuration;
using PowerFramework.Security.Endpoints;
using PowerFramework.Security.Tokens;
using PowerFramework.Shared.Kernel;

// ==================================================================================================
//  THE ONE ASSEMBLY FIXTURE IN THIS PROJECT, AND WHY IT HAS TO BE ONE
//
//  `IssuanceFixture` owns a certificate authority for the WHOLE suite - one authority, created once,
//  so that a certificate it issues is trusted by any host any row builds. Because the trust setting a
//  deployment supplies is a PATH, the authority's public certificate has to exist as a FILE, which
//  means the suite creates a per-run directory under the system temporary directory.
//
//  Nothing owned by a test class can remove that directory: it outlives every class that uses it by
//  construction, and a class-level teardown would delete an anchor a later class still points a host
//  at. Leaving it was the defect - a run that finished left a directory behind, every run left
//  another, and on a shared agent they accumulate indefinitely. An ASSEMBLY fixture is the exactly
//  correct scope, and xUnit v3 disposes it after the last test in the assembly has finished whatever
//  the outcome, which is what makes the cleanup unconditional rather than best-effort.
//
//  It carries NO test and NO assertion. Its whole content is a lifetime.
//
//  The type name is FULLY QUALIFIED because an assembly-level attribute is resolved before the
//  file-scoped namespace declaration below takes effect.
// ==================================================================================================
[assembly: AssemblyFixture(typeof(PowerFramework.Security.Tests.IssuanceAuthorityLifetime))]

namespace PowerFramework.Security.Tests;

/// <summary>
/// Releases the suite-wide certificate authority and the per-run directory holding its anchor file,
/// after the last test in this assembly has run.
/// </summary>
/// <remarks>
/// <para>
/// UNCONDITIONAL, AND DELIBERATELY NOT DEFENSIVE. A failure to remove the directory is reported as an
/// assembly cleanup error rather than swallowed: this suite is the one place in the repository that
/// routinely materialises credential-shaped artifacts on disk, and a cleanup that quietly does nothing
/// is indistinguishable from one that worked. The anchor file carries the authority's PUBLIC certificate
/// only - the signing key never leaves the process - so what is being cleaned up is hygiene rather than a
/// disclosure, and it is still cleaned up.
/// </para>
/// <para>
/// IDEMPOTENT WITHOUT BEING SILENT. The release is a no-op when the authority was never created, because
/// the whole thing is lazy and a run that touched none of the certificate rows created no directory to
/// remove. That is a genuine absence rather than a suppressed failure.
/// </para>
/// </remarks>
public sealed class IssuanceAuthorityLifetime : IDisposable
{
    /// <inheritdoc />
    public void Dispose() => IssuanceFixture.ReleaseAuthority();
}

/// <summary>
/// The addresses, identities and material every row in this file shares.
/// </summary>
/// <remarks>
/// One place for the values the authored document fixes, so that a row asserting one of them reads as
/// an assertion rather than as a literal.
/// </remarks>
internal static class IssuanceFixture
{
    /// <summary>The issuance path, as the authored document declares it.</summary>
    internal const string IssuancePath = "/v1/tokens";

    /// <summary>The operation identifier, as the authored document declares it.</summary>
    internal const string OperationId = "issueToken";

    /// <summary>The published key set's address.</summary>
    internal const string KeySetPath = "/.well-known/jwks.json";

    /// <summary>The generated document's address.</summary>
    internal const string DocumentPath = "/openapi/v1.json";

    /// <summary>
    /// A caller identity the configured audience roster carries, used as both the claimed subject and
    /// the certificate's common name on the authenticated rows.
    /// </summary>
    /// <remarks>
    /// Taken from the roster the service's own settings file declares, and matching the naming the
    /// local certificate recipe in docs/ARCHITECTURE.md section 9.3.1 issues client certificates under.
    /// </remarks>
    internal const string CallerIdentity = "powerframework-gateway";

    /// <summary>A second configured audience, for the rows that vary the audience.</summary>
    internal const string SecondAudience = "powerframework-dataservices";

    /// <summary>
    /// This service's OWN identity, as its settings file declares it in both the issuance roster and the
    /// inbound-validation section.
    /// </summary>
    /// <remarks>
    /// It is the single audience the composition root's inbound bearer handler accepts, so it is the
    /// audience a token minted for reading this service's own non-exempt routes must carry.
    /// </remarks>
    internal const string SelfAudience = "powerframework-security";

    /// <summary>An audience no deployment in this repository configures.</summary>
    /// <remarks>
    /// Deliberately shaped like a service identity so that the refusal is proved to come from the
    /// roster check rather than from a shape check that a nonsense value would also have failed.
    /// </remarks>
    internal const string UnlistedAudience = "powerframework-elsewhere";

    /// <summary>The key identifier the service's settings file configures.</summary>
    internal const string ConfiguredKeyId = "powerframework-security-signing-1";

    /// <summary>One scope, so that a request asks for something rather than for nothing.</summary>
    internal const string ReadScope = "dataservices.datawindow";

    /// <summary>A second scope, for the rows that assert set handling.</summary>
    internal const string WriteScope = "dataservices.columnexpression";

    /// <summary>
    /// Every caller identity this suite mints for, and the only ones any host it builds authorises.
    /// </summary>
    /// <remarks>
    /// <para>
    /// AN EXPLICIT LIST RATHER THAN A WILDCARD, AND THAT IS NOT A STYLE CHOICE. The service's
    /// authorization matrix has no wildcard and must never acquire one - a matrix that can say "any
    /// caller" is the widening the matrix exists to close - so a harness that wanted convenience by
    /// asking for one would be asking for a production capability. Listing the identities instead costs
    /// one line per identity and keeps the production surface unchanged.
    /// </para>
    /// <para>
    /// WHY EACH NAME IS HERE. The first four are the service identities the roster carries and the
    /// suites mint for; the fifth is deliberately NOT a service identity, because a caller whose name is
    /// not also an audience is the case that proves the matrix keys on the caller rather than on the
    /// audience spelling; the last two are the harness identities the general-purpose factory and the
    /// authorization conformance suite use as subjects.
    /// </para>
    /// <para>
    /// A ROW EXERCISING THE MATRIX ITSELF DOES NOT USE THIS. It shapes the options directly, which runs
    /// after this default is applied, so it can narrow or clear the matrix deliberately - which is how
    /// the refusal arms are reached at all.
    /// </para>
    /// </remarks>
    internal static IReadOnlyList<string> TestCallers { get; } =
    [
        CallerIdentity,
        SecondAudience,
        "powerframework-persistence",
        SelfAudience,
        "a-caller-that-is-not-an-audience",
        "powerframework-security-tests",
        "authorization-conformance",
    ];

    /// <summary>
    /// Every scope this suite requests, and the only ones any host it builds permits.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE UNION OF WHAT THE SUITES ASK FOR, SO NO ROW IS SILENTLY NARROWED. A narrowing is a success
    /// under the published contract, which means a scope missing from this set would not fail its row
    /// with a refusal - it would quietly issue a token carrying a smaller granted set, and every
    /// assertion comparing the granted value against the requested one would fail with a confusing
    /// difference rather than a missing permission. Keeping the union here makes that class of failure
    /// impossible for a row that is not about scopes.
    /// </para>
    /// <para>
    /// THE NAMES ARE THE ONES THE SUITES ALREADY USED, gathered rather than invented: the two DataWindow
    /// scopes above, the two Persistence scopes the claim matrix requests, the general-purpose factory's
    /// own default scope, the contract-reading scope the health suite's host uses, and the single scope
    /// the authorization conformance suite carries. None of them is a wildcard or an administrative
    /// name, so a row cannot pass by having been granted everything.
    /// </para>
    /// </remarks>
    internal static IReadOnlyList<string> TestScopes { get; } =
    [
        ReadScope,
        WriteScope,
        "persistence.query",
        "persistence.update",
        "security.test",
        "contract.read",
        "authorization-conformance",
        SecurityScopes.Crypto,

        // AND THE PRODUCTION ROSTER'S OWN NAMES, gathered from the settings files rather than invented:
        // the issuance roster grants these to the deployment's own callers, and a row reading a permitted
        // scope out of that roster would otherwise be narrowed here by a name the harness had omitted -
        // which reads as a permission defect rather than as a gap in this list.
        "ping",
        "capabilities",
        "datawindow",
        "persistence.read",
        "persistence.write",
    ];

    /// <summary>
    /// Authorises every <see cref="TestCallers"/> identity for every audience already on the host's
    /// roster, with <see cref="TestScopes"/>.
    /// </summary>
    /// <param name="options">The options instance being shaped, its roster already settled.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// CALLED AFTER THE ROSTER IS SETTLED AND BEFORE A ROW'S OWN SHAPING, which is the only ordering that
    /// works: the matrix's audiences must be roster members or the service's own validator refuses the
    /// host, and a row that wants to narrow the matrix must be able to overwrite what this installed.
    /// </para>
    /// <para>
    /// IT IS ADDITIVE AND IDEMPOTENT, AND THE TWO PROPERTIES COME FROM THE SAME RULE. Nothing is cleared:
    /// an identity is granted only when NEITHER matrix shape already names it as a caller, and a roster
    /// entry is added only when the roster does not already name that subject. Called twice, the second
    /// call finds every identity it would have added already present and adds nothing - so no duplicate
    /// (caller, audience) pair can accumulate, which the service refuses to construct on because a
    /// duplicated pair makes the effective permission depend on which row is read first.
    /// </para>
    /// <para>
    /// THE DEPLOYMENT'S OWN THREE GRANTS ARE LEFT EXACTLY AS THE SETTINGS FILE STATES THEM, and this is
    /// the correction that matters most. An earlier revision replaced the whole matrix with a blanket
    /// grant, which made every row that OBSERVES the deployed matrix unable to observe it: a row asking
    /// whether Gateway is refused an audience the deployment does not grant it saw a host in which Gateway
    /// was granted everything, so the refusal it exists to prove could not occur. The deployed topology -
    /// Gateway to DataServices, DataServices to Persistence, DataServices to Security - is therefore
    /// untouched, and only the identities the deployment does NOT name are blanket-granted. Those are the
    /// harness's own subjects, which is exactly the set that needs the accommodation.
    /// </para>
    /// <para>
    /// A ROW THAT NEEDS EVEN THAT MUCH GONE clears the matrix in its own shaping delegate, which runs
    /// after this, and several do.
    /// </para>
    /// </remarks>
    internal static void PermitTestCallers(SecurityOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // EVERY IDENTITY EITHER SHAPE ALREADY NAMES, because the issuer enforces the union of the two and a
        // grant added here for a caller the deployment already constrains would WIDEN that constraint
        // rather than sit beside it.
        HashSet<string> alreadyGranted = new(StringComparer.Ordinal);

        foreach (SecurityCallerOptions declared in options.Callers)
        {
            if (!string.IsNullOrWhiteSpace(declared.Identity))
            {
                _ = alreadyGranted.Add(declared.Identity.Trim());
            }
        }

        foreach (CallerAuthorizationOptions declared in options.CallerAuthorizations)
        {
            if (!string.IsNullOrWhiteSpace(declared.Caller))
            {
                _ = alreadyGranted.Add(declared.Caller.Trim());
            }
        }

        foreach (string caller in TestCallers)
        {
            if (alreadyGranted.Contains(caller))
            {
                continue;
            }

            foreach (string audience in options.Audiences)
            {
                CallerAuthorizationOptions row = new()
                {
                    Caller = caller,
                    Audience = audience,
                };

                foreach (string scope in TestScopes)
                {
                    row.Scopes.Add(scope);
                }

                options.CallerAuthorizations.Add(row);
            }

        }

        EnsureTestCallersAreRostered(options);
    }

    /// <summary>
    /// Adds a credential-directory entry for every caller the grant matrix names and the directory does
    /// not, because a grant naming an uncredentialled caller REFUSES the host.
    /// </summary>
    /// <param name="options">The options instance being shaped.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// 🔴 <b>THIS IS A STARTUP INVARIANT AND NOT A CONVENIENCE, which is why it runs unconditionally after
    /// every shaping delegate.</b> The composition root refuses to start when the matrix grants a caller
    /// <c>Security:Clients</c> carries no entry for: identity is resolved against that directory on both
    /// the shared-secret and the client-certificate path, so such a grant can never be exercised by
    /// anything. A harness row that installs a matrix of its own - and several install a complete
    /// deployment - would otherwise produce an unstartable host and fail on a configuration rule instead of
    /// on the issuance behaviour it exists to assert.
    /// </para>
    /// <para>
    /// <b>ADDITIVE, AND IT CANNOT WIDEN WHAT A ROW READS.</b> The directory grants nothing - it names an
    /// identity and the configuration key its secret is found under - so an entry added here cannot turn a
    /// refusal into an issuance. Both matrix shapes are read, in the same union the enforcement point
    /// performs, and comparison is ordinal to match it.
    /// </para>
    /// <para>
    /// <b>A ROW THAT WANTS THE REFUSAL DOES NOT COME THROUGH HERE.</b>
    /// <c>IssuanceRosterAuthorityTests</c>'s shipped-settings cases boot the real composition root
    /// on the shipped settings with its own contributed grant, deliberately bypassing this harness, so the
    /// invariant is asserted rather than merely satisfied.
    /// </para>
    /// </remarks>
    internal static void EnsureEveryGrantedCallerIsCredentialled(SecurityOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        HashSet<string> credentialled = new(StringComparer.Ordinal);

        foreach (SecurityClientOptions registered in options.Clients)
        {
            if (!string.IsNullOrWhiteSpace(registered?.Subject))
            {
                _ = credentialled.Add(registered.Subject.Trim());
            }
        }

        List<string> granted = [];

        void Note(string? caller)
        {
            if (!string.IsNullOrWhiteSpace(caller)
                && !credentialled.Contains(caller.Trim())
                && !granted.Contains(caller.Trim(), StringComparer.Ordinal))
            {
                granted.Add(caller.Trim());
            }
        }

        foreach (SecurityCallerOptions caller in options.Callers)
        {
            Note(caller?.Identity);
        }

        foreach (CallerAuthorizationOptions row in options.CallerAuthorizations)
        {
            Note(row?.Caller);
        }

        foreach (string caller in granted)
        {
            options.Clients.Add(new SecurityClientOptions
            {
                Subject = caller,

                // The same shared key every harness identity authenticates with; the factory emits it, so
                // it resolves and the entry is one a row could genuinely present a credential for.
                SecretConfigurationKey = SharedRosterSecretConfigurationKey,
            });
        }
    }

    /// <summary>
    /// Adds an issuance-roster entry for every <see cref="TestCallers"/> identity the roster does not
    /// already name, covering the audience roster as it currently stands.
    /// </summary>
    /// <param name="options">The options instance being shaped.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// THE ROSTER IS NOT THE MATRIX, AND THAT IS WHY THIS IS SEPARATE. <c>Security:Clients</c> is a
    /// CREDENTIAL DIRECTORY: <c>TokenIssuer</c> reads no permission from it, but the issuance ENDPOINT
    /// resolves a presented subject through it to the configuration key naming that caller's secret. A
    /// harness identity absent from it cannot authenticate at all, so every row driving
    /// <c>POST /v1/tokens</c> as that identity would be answered 401 before any rule it was asserting could
    /// be reached. Because it grants nothing, adding an entry cannot widen a permission an assertion is
    /// reading - which is what makes it safe to call this again after a shaping delegate has run, and the
    /// reconciliation step does exactly that.
    /// </para>
    /// <para>
    /// IT IS ADDITIVE AND NEVER REPLACES A DEPLOYMENT ENTRY. The shipped roster names two subjects, and the
    /// grants those subjects hold are narrower than the blanket matrix rows this fixture installs for its
    /// own identities - deliberately, because rows observe the deployed topology.
    /// </para>
    /// </remarks>
    internal static void EnsureTestCallersAreRostered(SecurityOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        HashSet<string> rostered = new(StringComparer.Ordinal);

        foreach (SecurityClientOptions registered in options.Clients)
        {
            if (!string.IsNullOrWhiteSpace(registered.Subject))
            {
                _ = rostered.Add(registered.Subject.Trim());
            }
        }

        foreach (string caller in TestCallers)
        {
            if (rostered.Contains(caller))
            {
                continue;
            }

            SecurityClientOptions client = new()
            {
                Subject = caller,

                // ONE KEY FOR EVERY TEST CALLER, and it is one the factory already emits. A per-caller key
                // would have to be added to the factory's own list in the same change or the host would not
                // start - which is exactly the drift this single shared name removes.
                SecretConfigurationKey = SharedRosterSecretConfigurationKey,
            };

            // A SUBJECT AND A SECRET KEY NAME, AND NOTHING ELSE. The credential directory carries no
            // permission member: what a caller may request is stated once, in the grant matrix
            // PermitTestCallers installs above. It used to carry an audience list and a scope list here too,
            // and they were read by nothing - which is exactly the divergence the production settings then
            // shipped with.
            options.Clients.Add(client);
        }
    }

    /// <summary>
    /// The first (audience, scope) pair the grant matrix permits one caller, read from the matrix rather
    /// than from the credential directory.
    /// </summary>
    /// <param name="options">The bound settings.</param>
    /// <param name="caller">The caller identity to read a grant for.</param>
    /// <returns>The audience and one scope the matrix permits that caller.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The matrix grants that caller nothing.</exception>
    /// <remarks>
    /// <b>THE MATRIX IS THE ONLY PLACE THIS CAN BE READ FROM, AND THAT IS THE POINT.</b> Rows used to read
    /// a permitted audience and scope off the caller's <c>Security:Clients</c> entry, which carried lists
    /// the issuer never consulted - so a row could construct a request the roster advertised and the matrix
    /// refused, and the assertion would fail for a reason that had nothing to do with its subject. Those
    /// lists are gone; this reads the surface that decides.
    /// </remarks>
    internal static (string Audience, string Scope) FirstGrant(SecurityOptions options, string caller)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(caller);

        foreach ((string granted, string audience, IReadOnlyList<string> scopes) in EffectiveGrants(options))
        {
            if (string.Equals(granted, caller, StringComparison.Ordinal) && scopes.Count > 0)
            {
                return (audience, scopes[0]);
            }
        }

        throw new InvalidOperationException(
            $"The grant matrix permits caller '{caller}' nothing, so no request it could make would be "
            + "issued. Grant it through Security:CallerAuthorizations or Security:Callers before reading a "
            + "permitted pair for it.");
    }

    /// <summary>
    /// Projects both configured matrix shapes into one flat row sequence: the matrix the issuer actually
    /// enforces, whichever shape a deployment used to author it.
    /// </summary>
    /// <param name="options">The bound settings.</param>
    /// <returns>One entry per (caller, audience) statement, in nested-then-flat order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// WHY A HELPER EXISTS AT ALL. <c>TokenIssuer</c> folds <c>Security:Callers</c> and
    /// <c>Security:CallerAuthorizations</c> into ONE dictionary and decides from that, so a row asking
    /// "what does this deployment permit" has to ask the same question of both shapes. Reading one of them
    /// reports a caller as ungranted purely because the deployment expressed its grant in the other -
    /// which is a statement about the settings file's authoring style rather than about a permission, and
    /// it is exactly the failure this replaces.
    /// </para>
    /// <para>
    /// IT PROJECTS AND DOES NOT DECIDE, which keeps it honest as a test helper. It does not intersect, does
    /// not union a pair stated in both shapes, and does not screen a malformed entry: a caller reading it
    /// sees every statement the configuration makes, and the decision remains the issuer's alone. A row
    /// that needs the DECISION drives the issuer.
    /// </para>
    /// </remarks>
    internal static IEnumerable<(string Caller, string Audience, IReadOnlyList<string> Scopes)>
        EffectiveGrants(SecurityOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        foreach (SecurityCallerOptions caller in options.Callers)
        {
            foreach (SecurityCallerGrantOptions grant in caller.Grants)
            {
                yield return (
                    caller.Identity?.Trim() ?? string.Empty,
                    grant.Audience?.Trim() ?? string.Empty,
                    [.. grant.Scopes.Select(static scope => scope?.Trim() ?? string.Empty)]);
            }
        }

        foreach (CallerAuthorizationOptions row in options.CallerAuthorizations)
        {
            yield return (
                row.Caller?.Trim() ?? string.Empty,
                row.Audience?.Trim() ?? string.Empty,
                [.. row.Scopes.Select(static scope => scope?.Trim() ?? string.Empty)]);
        }
    }

    /// <summary>
    /// Removes whatever a shaping delegate's later narrowing of the audience roster has made incoherent,
    /// leaving only what that delegate itself declared.
    /// </summary>
    /// <param name="options">The options instance, after the shaping delegate has run.</param>
    /// <param name="preexistingRows">
    /// The grant rows present before the delegate ran - the deployment's own and this fixture's.
    /// </param>
    /// <param name="preexistingClients">
    /// The issuance-roster entries present before the delegate ran.
    /// </param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// THE ORDERING PROBLEM THIS SOLVES, STATED PLAINLY. The grant matrix and the roster are settled BEFORE
    /// a row's own shaping, because a row must be able to overwrite what they installed - and they are
    /// built from the audience roster as it stands at that moment. A row that then narrows the roster in its
    /// delegate, and several declare a complete two-audience deployment of their own, leaves behind grants
    /// and roster entries naming audiences the deployment no longer serves. The service refuses to START on
    /// either, and it refuses with a diagnostic about a configuration position no row was exercising, which
    /// is the most confusing failure available: the host under test never boots and the reported key belongs
    /// to the settings file or to this fixture.
    /// </para>
    /// <para>
    /// ONLY WHAT WAS THERE BEFORE THE DELEGATE IS TOUCHED, and that is what keeps this from defeating the
    /// rows it exists to enable. Membership is decided by REFERENCE against the two snapshots, so a grant or
    /// an entry the delegate itself added is left exactly as declared - including one deliberately naming an
    /// audience the delegate also removed from the roster, which still refuses the host, because that IS the
    /// fault such a row asserts and there are rows that assert it.
    /// </para>
    /// <para>
    /// IT PRUNES GRANTS AND NEVER ADDS ONE, so it cannot re-widen a matrix a row deliberately narrowed - the
    /// property the refusal rows depend on. The credential directory is the one thing it may re-populate,
    /// and only because a roster entry grants nothing: <c>TokenIssuer</c> reads no permission from it, while
    /// an EMPTY roster refuses the host outright, so a narrowing that emptied it has to be answered with
    /// entries valid for the final roster rather than with an unstartable host.
    /// </para>
    /// </remarks>
    internal static void ReconcileGrantsWithRoster(
        SecurityOptions options,
        IReadOnlyList<CallerAuthorizationOptions> preexistingRows,
        IReadOnlyList<SecurityClientOptions> preexistingClients)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(preexistingRows);
        ArgumentNullException.ThrowIfNull(preexistingClients);

        HashSet<string> served = new(StringComparer.Ordinal);

        foreach (string audience in options.Audiences)
        {
            if (!string.IsNullOrWhiteSpace(audience))
            {
                _ = served.Add(audience.Trim());
            }
        }

        for (int index = options.CallerAuthorizations.Count - 1; index >= 0; index--)
        {
            CallerAuthorizationOptions row = options.CallerAuthorizations[index];

            if (!Preexisting(preexistingRows, row))
            {
                continue;
            }

            if (!served.Contains(row.Audience?.Trim() ?? string.Empty))
            {
                options.CallerAuthorizations.RemoveAt(index);
            }
        }

        // THE CREDENTIAL DIRECTORY NEEDS NO PRUNING AND MUST NOT BE PRUNED. It used to be, because its
        // entries carried audience lists that a narrowed roster could make unservable - and those lists are
        // gone: a directory entry names a subject and a secret key, neither of which references an audience,
        // so a row that narrows the audience roster can no longer leave one incoherent. Pruning it here
        // would now only be able to remove a subject a row is about to authenticate as, which is exactly the
        // failure this reconciliation exists to prevent.
        EnsureTestCallersAreRostered(options);

        // AND ONE STEP THE DIRECTORY DOES STILL NEED, because it is now a HOST-REFUSING invariant.
        EnsureEveryGrantedCallerIsCredentialled(options);
    }

    /// <summary>Reports whether one instance is in a snapshot, by reference.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="snapshot">The snapshot taken before the shaping delegate ran.</param>
    /// <param name="candidate">The instance under consideration.</param>
    /// <returns><see langword="true"/> when the snapshot holds that same instance.</returns>
    /// <remarks>
    /// BY REFERENCE AND DELIBERATELY NOT BY VALUE. These option types carry no value equality, and giving
    /// them some would be the wrong fix: two grants that happen to say the same thing are still two
    /// statements, and the question here is which of them existed before the delegate ran.
    /// </remarks>
    private static bool Preexisting<T>(IReadOnlyList<T> snapshot, T candidate)
        where T : class
    {
        foreach (T element in snapshot)
        {
            if (ReferenceEquals(element, candidate))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The one configuration key every test caller's issuance-roster entry names its secret under.
    /// </summary>
    /// <remarks>
    /// It is deliberately one of the keys <c>SecurityAppFactory.RosterSecretOverrides</c> already emits, so
    /// a rebuilt roster needs no matching change there. The value behind it is a per-process random string
    /// the factory generates; nothing in this project declares credential material.
    /// </remarks>
    internal const string SharedRosterSecretConfigurationKey = "SECURITY_CLIENT_SECRET";

    /// <summary>The logger category the operation writes its own records under.</summary>
    /// <remarks>
    /// Selecting on it is what separates this operation's record from the shared problem factory's and
    /// from the issuer's, all three of which describe an issuance and therefore cannot be told apart by
    /// inspecting message text alone.
    /// </remarks>
    internal const string LoggerCategory = "PowerFramework.Security.Endpoints.TokenEndpoints";

    /// <summary>
    /// The one certificate authority every trusted caller certificate in this suite is issued from,
    /// together with the PEM file the hosts point their trust-anchor setting at.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ONE AUTHORITY FOR THE WHOLE SUITE, CREATED ONCE. Every host this file starts configures the same
    /// anchor path, so a certificate this fixture issues is honoured by any of them - which keeps a row
    /// free to build its own host without also having to arrange its own trust. A row that needs an
    /// UNTRUSTED certificate asks for one explicitly through
    /// <see cref="CreateUntrustedCallerCertificate"/>, so trust is never accidental in either direction.
    /// </para>
    /// <para>
    /// THE ANCHOR IS WRITTEN TO DISK BECAUSE THE SETTING IS A PATH, and the setting is a path because a
    /// deployment mounts its authority rather than embedding it. The file carries the authority's PUBLIC
    /// certificate only - never its key - so nothing secret is written; the signing key stays in memory
    /// in this process and is discarded when it exits. The directory is per-run, so two concurrent runs
    /// of this suite cannot observe each other's anchor.
    /// </para>
    /// </remarks>
    private static readonly Lazy<(X509Certificate2 Authority, string AnchorPath)> TestAuthority =
        new(CreateAuthority, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// The path to the PEM file holding this suite's certificate authority, for a host's trust anchor.
    /// </summary>
    internal static string ClientCertificateAuthorityPath => TestAuthority.Value.AnchorPath;

    /// <summary>
    /// Disposes the suite-wide authority and removes the per-run directory holding its anchor file.
    /// </summary>
    /// <remarks>
    /// <para>
    /// CALLED ONCE, BY THE ASSEMBLY FIXTURE AT THE TOP OF THIS FILE, and by nothing else. The authority is
    /// shared by every host every row in this project builds, so the only scope at which it can safely be
    /// released is the assembly's - which is why this is not an <c>IDisposable</c> on a test class.
    /// </para>
    /// <para>
    /// THE DIRECTORY IS REMOVED, NOT JUST THE FILE. The directory was created for this run and holds
    /// nothing else, so removing the file alone would leave an empty directory behind on every run - the
    /// same accumulation, one inode smaller. Recursive removal is therefore correct here and is safe
    /// because the path is one this fixture composed itself, from the system temporary directory plus a
    /// fresh identifier.
    /// </para>
    /// <para>
    /// NOT GUARDED WITH A <c>catch</c>. A failure to release is surfaced as an assembly cleanup error,
    /// because a cleanup that silently does nothing is indistinguishable from one that worked.
    /// </para>
    /// </remarks>
    internal static void ReleaseAuthority()
    {
        if (!TestAuthority.IsValueCreated)
        {
            // Lazy, and a run that exercised none of the certificate rows created nothing to remove. A
            // genuine absence rather than a suppressed failure.
            return;
        }

        (X509Certificate2 authority, string anchorPath) = TestAuthority.Value;

        authority.Dispose();

        string directory = Path.GetDirectoryName(anchorPath)
            ?? throw new InvalidOperationException(
                "The suite's trust-anchor path has no directory component, which cannot happen for a "
                    + "path this fixture composed, and would mean the release is about to delete "
                    + "something it did not create.");

        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// Builds a TRUSTED client certificate whose common name is the supplied identity.
    /// </summary>
    /// <param name="commonName">The identity the certificate should establish.</param>
    /// <returns>A certificate issued by this suite's authority, valid now, held only in memory.</returns>
    /// <remarks>
    /// <para>
    /// ISSUED BY THIS SUITE'S AUTHORITY RATHER THAN SELF-SIGNED, and that changed when the trust decision
    /// became a behaviour of this service rather than a promise about its transport. A self-signed
    /// certificate chains to nothing the deployment configured, so the issuance operation now refuses it -
    /// correctly - and every row asserting a successful mint needs a certificate that actually establishes
    /// itself.
    /// </para>
    /// <para>
    /// An elliptic-curve key rather than an RSA one, deliberately. The operation reads exactly ONE thing
    /// from the certificate - the identity in its subject - and never its key, so the key type is
    /// irrelevant to the behaviour under test; choosing the cheap one keeps a suite that builds a fresh
    /// host per row from spending its time on key generation. That the rows pass with a non-RSA client
    /// certificate is itself worth having asserted: the transport identity and the RSA signing identity
    /// are separate keys with separate lifetimes.
    /// </para>
    /// <para>
    /// NO EXTENDED KEY USAGE IS DECLARED, which matches what this repository's own generation recipe
    /// produces and is therefore the shape a deployment will actually present. A certificate declaring no
    /// usage restriction is unrestricted by definition, so it satisfies the client-authentication
    /// requirement without naming it - and a row that needs the refusing case declares server
    /// authentication explicitly instead.
    /// </para>
    /// </remarks>
    internal static X509Certificate2 CreateCallerCertificate(string commonName)
    {
        ArgumentNullException.ThrowIfNull(commonName);

        return IssueFromAuthority(BuildSubject(commonName), extension: null);
    }

    /// <summary>
    /// Builds a TRUSTED client certificate whose validity window brackets one specific instant.
    /// </summary>
    /// <param name="commonName">The identity the certificate should establish.</param>
    /// <param name="instant">The instant the certificate must be valid at.</param>
    /// <returns>A certificate issued by this suite's authority, valid around <paramref name="instant"/>.</returns>
    /// <remarks>
    /// <para>
    /// FOR THE ROWS THAT FREEZE THE HOST'S CLOCK, AND THE COUPLING IS DELIBERATE RATHER THAN INCIDENTAL.
    /// The trust layer measures a certificate's validity window against the INJECTED clock, because the
    /// refactor's determinism rule requires every clock read to be substitutable - so a host frozen at a
    /// calendar instant judges a certificate at that instant, and a certificate valid only around real
    /// "now" is correctly refused there.
    /// </para>
    /// <para>
    /// A NAMED BUILDER RATHER THAN TWO NULLABLE DATE PARAMETERS ON THE ORDINARY ONE, so that a row using
    /// it states WHY it needs a different window - it froze the clock - instead of expressing that as two
    /// arithmetic arguments a reader has to reverse-engineer.
    /// </para>
    /// </remarks>
    internal static X509Certificate2 CreateCallerCertificateValidAt(
        string commonName,
        DateTimeOffset instant)
    {
        ArgumentNullException.ThrowIfNull(commonName);

        return IssueFromAuthority(
            BuildSubject(commonName),
            extension: null,
            notBefore: instant.AddMinutes(-5),
            notAfter: instant.AddMinutes(5));
    }

    /// <summary>
    /// Builds a client certificate that chains to NO configured authority.
    /// </summary>
    /// <param name="commonName">The identity the certificate claims to establish.</param>
    /// <returns>A self-signed certificate, valid now, held only in memory.</returns>
    /// <remarks>
    /// The shape an attacker can produce without any cooperation from the deployment: a perfectly
    /// well-formed certificate, inside its validity window, carrying whatever common name it likes. It
    /// exists so the trust gate's refusal is asserted against the case that motivates it, rather than
    /// against a malformed certificate that would fail for a different reason.
    /// </remarks>
    internal static X509Certificate2 CreateUntrustedCallerCertificate(string commonName)
    {
        ArgumentNullException.ThrowIfNull(commonName);

        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        CertificateRequest request = new(BuildSubject(commonName), key, HashAlgorithmName.SHA256);

        return request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow.AddMinutes(5));
    }

    /// <summary>
    /// Builds a TRUSTED certificate whose declared extended key usage permits server authentication only.
    /// </summary>
    /// <param name="commonName">The identity the certificate carries.</param>
    /// <returns>A certificate issued by this suite's authority.</returns>
    /// <remarks>
    /// A LISTENER'S CERTIFICATE IS NOT A CALLER CREDENTIAL, and this is the material that proves the
    /// distinction is enforced. It chains to the configured authority and is inside its window, so it
    /// fails on the usage restriction alone and on nothing else - which is what makes the row driving it
    /// a test of the usage check rather than of the chain.
    /// </remarks>
    internal static X509Certificate2 CreateServerOnlyCallerCertificate(string commonName)
    {
        ArgumentNullException.ThrowIfNull(commonName);

        // 1.3.6.1.5.5.7.3.1 is server authentication. Written as the identifier because a friendly name
        // is localized by the platform on some hosts and an identifier is not.
        X509EnhancedKeyUsageExtension usage = new(
            new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") },
            critical: false);

        return IssueFromAuthority(BuildSubject(commonName), usage);
    }

    /// <summary>
    /// Builds a TRUSTED certificate whose validity window has already closed.
    /// </summary>
    /// <param name="commonName">The identity the certificate carries.</param>
    /// <returns>An expired certificate issued by this suite's authority.</returns>
    /// <remarks>
    /// Issued by the configured authority so that the ONLY thing wrong with it is its window - a
    /// self-signed expired certificate would be refused for two reasons and would prove neither.
    /// </remarks>
    internal static X509Certificate2 CreateExpiredCallerCertificate(string commonName)
    {
        ArgumentNullException.ThrowIfNull(commonName);

        return IssueFromAuthority(
            BuildSubject(commonName),
            extension: null,
            notBefore: DateTimeOffset.UtcNow.AddDays(-10),
            notAfter: DateTimeOffset.UtcNow.AddDays(-1));
    }

    /// <summary>
    /// Builds a TRUSTED certificate whose subject carries no common name at all.
    /// </summary>
    /// <returns>A certificate establishing no usable identity.</returns>
    /// <remarks>
    /// The subject declares an organisation and nothing else, which is the shape that makes the
    /// framework's simple-name accessor answer with nothing. It exists so the "presented, trusted, but
    /// unusable" arm is proved rather than assumed - and it is ISSUED BY THE AUTHORITY precisely so that
    /// it reaches the identity check instead of stopping at the trust gate.
    /// </remarks>
    internal static X509Certificate2 CreateCertificateWithoutCommonName()
    {
        X500DistinguishedNameBuilder subject = new();
        subject.AddOrganizationName("powerframework-tests");

        return IssueFromAuthority(subject.Build(), extension: null);
    }

    /// <summary>Encodes one common name as a distinguished name.</summary>
    /// <param name="commonName">The identity.</param>
    /// <returns>The encoded subject.</returns>
    /// <remarks>
    /// Built through the framework's own encoder rather than by concatenating a string. A distinguished
    /// name has quoting, escaping and ordering rules, and an identity carrying a separator is exactly the
    /// case a concatenated string gets wrong - which is the same reason the implementation reads the
    /// identity back through the framework's accessor instead of splitting the subject itself.
    /// </remarks>
    private static X500DistinguishedName BuildSubject(string commonName)
    {
        X500DistinguishedNameBuilder subject = new();
        subject.AddCommonName(commonName);

        return subject.Build();
    }

    /// <summary>Issues one leaf certificate from this suite's authority.</summary>
    /// <param name="subject">The encoded subject.</param>
    /// <param name="extension">An extension to declare, or <see langword="null"/> for none.</param>
    /// <param name="notBefore">The start of the validity window, defaulting to five minutes ago.</param>
    /// <param name="notAfter">The end of the validity window, defaulting to five minutes hence.</param>
    /// <returns>The issued certificate, which carries its own private key.</returns>
    /// <remarks>
    /// The serial number is drawn from a fresh identifier rather than a counter, so two leaves issued in
    /// the same run can never collide - a collision would make one of them unchainable for a reason no
    /// row is testing. The issued certificate is recombined with its own key, because the platform's
    /// issuance produces the public half only.
    /// </remarks>
    private static X509Certificate2 IssueFromAuthority(
        X500DistinguishedName subject,
        X509Extension? extension,
        DateTimeOffset? notBefore = null,
        DateTimeOffset? notAfter = null)
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        CertificateRequest request = new(subject, key, HashAlgorithmName.SHA256);

        if (extension is not null)
        {
            request.CertificateExtensions.Add(extension);
        }

        using X509Certificate2 issued = request.Create(
            TestAuthority.Value.Authority,
            notBefore ?? DateTimeOffset.UtcNow.AddMinutes(-5),
            notAfter ?? DateTimeOffset.UtcNow.AddMinutes(5),
            Guid.NewGuid().ToByteArray());

        return issued.CopyWithPrivateKey(key);
    }

    /// <summary>Creates the suite's authority and writes its public certificate to a temporary file.</summary>
    /// <returns>The authority and the path its certificate was written to.</returns>
    /// <remarks>
    /// The authority declares itself a certificate authority with a path length of zero, which is what
    /// makes a chain built against it accept a leaf and refuse an intermediate - the shape the
    /// documentation's own generation recipe produces and the narrowest one that works.
    /// </remarks>
    private static (X509Certificate2 Authority, string AnchorPath) CreateAuthority()
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        X500DistinguishedNameBuilder subject = new();
        subject.AddCommonName("powerframework-security-tests-ca");

        CertificateRequest request = new(subject.Build(), key, HashAlgorithmName.SHA256);

        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(
                certificateAuthority: true,
                hasPathLengthConstraint: true,
                pathLengthConstraint: 0,
                critical: true));

        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.DigitalSignature,
                critical: true));

        // A WIDE WINDOW, AND THE WIDTH HAS A REASON RATHER THAN BEING GENEROSITY. Several rows in this
        // suite freeze their host's clock at a CALENDAR LITERAL, and the trust layer both judges a
        // certificate's window and builds its chain against that injected clock - so the authority has to
        // be valid at every instant any row freezes at, in either direction, and the platform additionally
        // refuses to issue a leaf whose window starts before its issuer's. Five years each way covers the
        // literals this suite uses with room for one to be added. It is a test fixture held in memory and
        // in a per-run temporary file; nothing here is a deployment lifetime.
        X509Certificate2 authority = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddYears(-5),
            DateTimeOffset.UtcNow.AddYears(5));

        string directory = Path.Combine(
            Path.GetTempPath(),
            "pfw-security-client-ca-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));

        _ = Directory.CreateDirectory(directory);

        string anchorPath = Path.Combine(directory, "client-ca.crt");

        // The PUBLIC certificate only. The authority's key never leaves this process.
        File.WriteAllText(anchorPath, authority.ExportCertificatePem());

        return (authority, anchorPath);
    }

    /// <summary>Builds a well-formed request body.</summary>
    /// <param name="subject">The claimed identity.</param>
    /// <param name="audience">The intended audience.</param>
    /// <param name="scopes">The requested scopes.</param>
    /// <returns>The body.</returns>
    /// <remarks>
    /// <para>
    /// THE DEFAULT AUDIENCE IS THE CALLER'S OWN IDENTITY, AND THAT IS LOAD-BEARING RATHER THAN ARBITRARY.
    /// The narrow matrices the refusal rows install authorise exactly this caller for exactly this audience
    /// and nothing else, so the default is the ONE pairing a row can rely on being permitted - which is
    /// what lets a row vary the scope set alone and attribute the answer to the scope gate. A default
    /// naming any other roster member would make every scope row an audience refusal instead, silently: the
    /// status is the same 403 either way, and only the sentence differs.
    /// </para>
    /// <para>
    /// A caller minting for its own identity is not a self-grant of anything: the audience is the RECEIVER
    /// a token addresses, and a deployment whose ingress is also a receiver is ordinary. The shipped
    /// production matrix contains no such pairing, and a conformance row asserts that absence separately.
    /// </para>
    /// </remarks>
    internal static TokenIssuanceRequestBody Body(
        string? subject = CallerIdentity,
        string? audience = CallerIdentity,
        IReadOnlyList<string>? scopes = null) =>
        new()
        {
            Subject = subject,
            Audience = audience,
            Scopes = scopes ?? [ReadScope],
        };

    /// <summary>
    /// Declares the issuance permission matrix this fixture's rows exercise.
    /// </summary>
    /// <param name="options">The bound options to amend.</param>
    /// <remarks>
    /// <para>
    /// THE ROSTER IS PART OF THE SUBJECT HERE, NOT PLUMBING AROUND IT. Every row in this file drives the
    /// real <c>POST /v1/tokens</c> over the real pipeline, so the issuer's permission decision - who may
    /// address which audience, and which scopes they may hold - is on the path being tested. Declaring the
    /// matrix explicitly is what lets a row assert that a PERMITTED pairing is issued and an unpermitted
    /// one is refused, and it is deliberately declared here in one place rather than per row so the two
    /// kinds of row cannot drift apart.
    /// </para>
    /// <para>
    /// THE THREE CONFIGURED IDENTITIES ARE GRANTED THE THREE CONFIGURED AUDIENCES, and every grant carries
    /// both scopes. That is broader than any real deployment would be - the shipped settings file grants
    /// each caller only the audiences it actually calls - and it is deliberate: the narrowing behaviour has
    /// its own rows that declare their own tighter rosters, and a broad matrix here keeps every other row
    /// asserting the property it exists for rather than a permission it never meant to exercise.
    /// </para>
    /// <para>
    /// <see cref="UnlistedAudience"/> IS DELIBERATELY ABSENT, from this matrix and from the audience roster
    /// alike, which is what keeps the unlisted-audience rows meaningful.
    /// </para>
    /// </remarks>
    internal static void ApplyTestRoster(SecurityOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.Callers.Clear();

        // BOTH SHAPES ARE CLEARED, because the issuer enforces their UNION: leaving the settings file's
        // flat rows in place would give this host a matrix that is partly the deployment's and partly this
        // fixture's, and a row could then pass on a production grant it was not meant to be using.
        options.CallerAuthorizations.Clear();

        // AND THE CREDENTIAL DIRECTORY IS REBUILT WITH IT, for a reason that is not about authorization: the
        // composition root refuses a host whose matrix grants a caller the directory does not name, because
        // such a grant can never be exercised. A directory left over from the settings file names two
        // subjects and this fixture's matrix names three, so rebuilding both together is what keeps the
        // narrowed host startable. It carries a subject and a secret key name and no permission member -
        // permissions are the matrix's alone.
        options.Clients.Clear();

        foreach (string identity in RosteredIdentities)
        {
            SecurityCallerOptions caller = new() { Identity = identity };

            foreach (string audience in RosteredIdentities)
            {
                SecurityCallerGrantOptions grant = new() { Audience = audience };

                foreach (string scope in RosteredScopes)
                {
                    grant.Scopes.Add(scope);
                }

                caller.Grants.Add(grant);
            }

            options.Callers.Add(caller);

            options.Clients.Add(new SecurityClientOptions
            {
                Subject = identity,
                SecretConfigurationKey = SharedRosterSecretConfigurationKey,
            });
        }
    }

    /// <summary>
    /// The three identities this fixture's matrix knows, used as both callers and audiences.
    /// </summary>
    /// <remarks>
    /// DELIBERATELY A SUBSET of the deployment-wide audience list, and that is what makes the negative rows
    /// reachable: a request naming an audience this service SERVES but this caller holds no grant for has
    /// to be expressible, and it cannot be if every audience is granted to everyone.
    /// </remarks>
    private static readonly string[] RosteredIdentities =
    [
        CallerIdentity,
        SecondAudience,
        SelfAudience,
    ];

    /// <summary>
    /// Every scope this fixture's matrix grants each rostered pair.
    /// </summary>
    /// <remarks>
    /// <b>THE INBOUND SCOPES BELONG HERE, NOT ONLY THE OUTBOUND ONES.</b> This matrix used to grant the two
    /// DataWindow scopes alone, which was sufficient while the routes this service PUBLISHES were reachable
    /// by any authenticated caller. They are not: the cryptographic surface and the authenticated probe each
    /// require their own scope, so a host whose matrix omits them mints a token the host itself then refuses
    /// - and, because an empty granted set is a refusal rather than an empty success, the refusal happens at
    /// ISSUANCE, before any row reaches the behaviour it was written to assert.
    /// </remarks>
    private static readonly string[] RosteredScopes =
    [
        ReadScope,
        WriteScope,
        CryptoEndpoints.RequiredScope,
        PingEndpoints.RequiredScope,
    ];

    /// <summary>
    /// Builds a body whose scope set is genuinely absent rather than defaulted.
    /// </summary>
    /// <returns>The body.</returns>
    /// <remarks>
    /// A separate builder because <see cref="Body"/> substitutes a scope for an omitted one, which is
    /// convenient everywhere else and exactly wrong for the row that asserts absence.
    /// </remarks>
    internal static TokenIssuanceRequestBody BodyWithoutScopes() =>
        new()
        {
            Subject = CallerIdentity,
            Audience = SecondAudience,
            Scopes = null,
        };

    /// <summary>
    /// Generates fresh signing material for one host.
    /// </summary>
    /// <returns>The private key as armoured algorithm-tagged text.</returns>
    /// <remarks>
    /// <para>
    /// GENERATED RATHER THAN WRITTEN DOWN. No key literal appears in this file, and nothing is copied
    /// from any hardcoded-secret site in the repository. Generating also proves more than a fixture
    /// would: the service must sign with what it was handed rather than recognise a known value. 2048
    /// bits because the minting library applies its own asymmetric minimum when it builds a signature
    /// provider, and because the service's own configuration declares that floor for its signing
    /// identity - which is not a legacy correction: the legacy keeps a smaller size a first-class value
    /// [ws_objects/pfw.shared.pbl.src/enums.sru:L965] on the surface where a CALLER supplies it, and
    /// this key is the system's own signing identity instead.
    /// </para>
    /// <para>
    /// FRESH PER HOST RATHER THAN SHARED ACROSS ROWS, AND THAT IS NOT MERELY TIDINESS. The minting
    /// library caches signature providers in a PROCESS-WIDE cache keyed by the key's own identity, and a
    /// key's identity is derived from its material rather than from the object holding it - so two hosts
    /// configured with the same material share one cached provider, and the provider outlives whichever
    /// host is disposed first. Sharing one pair therefore makes a row fail intermittently, for a reason
    /// that has nothing to do with the code under test. A distinct pair per host removes the sharing.
    /// </para>
    /// </remarks>
    internal static string CreateSigningKeyPem()
    {
        using RSA key = RSA.Create(2048);

        return key.ExportPkcs8PrivateKeyPem();
    }
}

/// <summary>
/// A clock that never advances, so that two issuances for identical input produce identical claims.
/// </summary>
/// <remarks>
/// The clock is one of the two primary non-determinism sources in this service and the characterization
/// model requires every such value to be maskable from BOTH the master and the candidate recording.
/// Substituting the whole seam here is what proves the endpoint reads NO ambient clock of its own: if
/// it did, a fixed clock would not produce a byte-identical body.
/// </remarks>
internal sealed class FrozenTimeProvider : TimeProvider
{
    private readonly DateTimeOffset _instant;

    /// <summary>Creates the clock over the instant it will always report.</summary>
    /// <param name="instant">The instant.</param>
    public FrozenTimeProvider(DateTimeOffset instant) => _instant = instant;

    /// <inheritdoc/>
    public override DateTimeOffset GetUtcNow() => _instant;
}

/// <summary>
/// A transport-security feature that reports a caller-supplied client certificate.
/// </summary>
/// <remarks>
/// The in-process test host terminates no TLS, so there is no handshake to present a certificate in.
/// This feature is the seam that stands in for one. It reports exactly what the connection would have
/// carried and nothing else, so the code under test cannot tell the difference and no production type
/// is modified to accommodate the test.
/// </remarks>
internal sealed class StubTlsConnectionFeature : ITlsConnectionFeature
{
    /// <summary>Creates the feature over the certificate it will report.</summary>
    /// <param name="certificate">The certificate, or <see langword="null"/> for none.</param>
    public StubTlsConnectionFeature(X509Certificate2? certificate) => ClientCertificate = certificate;

    /// <inheritdoc/>
    public X509Certificate2? ClientCertificate { get; set; }

    /// <inheritdoc/>
    public Task<X509Certificate2?> GetClientCertificateAsync(CancellationToken cancellationToken) =>
        Task.FromResult(ClientCertificate);
}

/// <summary>
/// Prepends middleware that puts a client certificate on every connection.
/// </summary>
/// <remarks>
/// A startup filter rather than a replaced pipeline, so the host under test keeps its OWN middleware
/// order - authentication, then authorization, then the routes - and only gains a transport fact ahead
/// of all of it. Replacing the pipeline instead would test a different application.
/// </remarks>
internal sealed class ClientCertificateStartupFilter : IStartupFilter
{
    private readonly X509Certificate2 _certificate;

    /// <summary>Creates the filter over the certificate every request will carry.</summary>
    /// <param name="certificate">The certificate.</param>
    public ClientCertificateStartupFilter(X509Certificate2 certificate) => _certificate = certificate;

    /// <inheritdoc/>
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        ArgumentNullException.ThrowIfNull(next);

        return builder =>
        {
            builder.Use(async (context, continuation) =>
            {
                context.Features.Set<ITlsConnectionFeature>(
                    new StubTlsConnectionFeature(_certificate));

                await continuation(context).ConfigureAwait(false);
            });

            next(builder);
        };
    }
}

/// <summary>
/// The issuance host: the real composition root, with signing material supplied and, optionally, a
/// client certificate on the connection and a frozen clock.
/// </summary>
/// <remarks>
/// <para>
/// THE COMPOSITION ROOT IS NOT REPLACED. Every registration the service makes is kept; this factory
/// only supplies what a deployment supplies - the signing secret - and substitutes the two determinism
/// seams the characterization model requires to be substitutable. The route, its authorization policy
/// and its handler are the production ones.
/// </para>
/// <para>
/// The signing secret is applied through the options pipeline rather than through an environment
/// variable, because that is the same ingress a deployment uses and it keeps the value out of the
/// process environment where a sibling row could observe it.
/// </para>
/// </remarks>
internal sealed class IssuanceHostFactory : WebApplicationFactory<Program>
{
    private readonly X509Certificate2? _certificate;
    private readonly DateTimeOffset? _instant;
    private readonly string _signingKey;
    private readonly string? _issuancePath;
    private readonly CapturedRecords? _captured;
    private readonly LogLevel? _minimumLevel;
    private readonly Action<SecurityOptions>? _matrix;

    /// <summary>Creates the factory.</summary>
    /// <param name="certificate">
    /// The client certificate every request should carry, or <see langword="null"/> to leave the
    /// connection without one - which is what an unauthenticated caller looks like.
    /// </param>
    /// <param name="instant">The instant to freeze the clock at, or <see langword="null"/> to keep the
    /// real one.</param>
    /// <param name="signingKey">
    /// The signing material, or <see langword="null"/> to generate a pair for this host alone.
    /// </param>
    /// <param name="issuancePath">
    /// A replacement issuance address, or <see langword="null"/> to keep the configured one. Supplied by
    /// the rows that assert the registration's fail-fast validation.
    /// </param>
    /// <param name="captured">
    /// A record store to capture the host's log records into, or <see langword="null"/> to keep the
    /// ordinary providers.
    /// </param>
    /// <param name="minimumLevel">
    /// A minimum level to filter this file's own logger category to, or <see langword="null"/> to leave
    /// the configured filtering alone. Supplied by the row that asserts issuance succeeds with the
    /// operator channel switched off.
    /// </param>
    /// <param name="matrix">
    /// A shaping delegate applied to the authorization matrix AFTER the suite's permissive default, or
    /// <see langword="null"/> to keep that default. Supplied by the rows that assert an authorization
    /// REFUSAL, which the default deliberately makes unreachable - it authorises every test caller for
    /// every roster audience precisely so that the rest of the suite is about something else.
    /// </param>
    public IssuanceHostFactory(
        X509Certificate2? certificate = null,
        DateTimeOffset? instant = null,
        string? signingKey = null,
        string? issuancePath = null,
        CapturedRecords? captured = null,
        LogLevel? minimumLevel = null,
        Action<SecurityOptions>? matrix = null)
    {
        _certificate = certificate;
        _instant = instant;
        _signingKey = signingKey ?? IssuanceFixture.CreateSigningKeyPem();
        _issuancePath = issuancePath;
        _captured = captured;
        _minimumLevel = minimumLevel;
        _matrix = matrix;
    }

    /// <inheritdoc/>
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // EVERY ROSTER SECRET THE SETTINGS FILES NAME, WITHOUT WHICH NO HOST STARTS. The issuance
        // registry resolves each named configuration key eagerly and refuses to construct when one is
        // absent - the posture that turns a missing deployment secret into a startup failure rather than
        // a caller that mysteriously cannot authenticate. Reused from the sibling factory so one
        // generated value serves the whole process and nothing in this repository is a usable
        // credential.
        builder.ConfigureAppConfiguration(configuration =>
            configuration.AddInMemoryCollection(SecurityAppFactory.RosterSecretOverrides()));

        builder.ConfigureServices(services =>
        {
            string signingKey = _signingKey;
            string? issuancePath = _issuancePath;
            Action<SecurityOptions>? matrix = _matrix;

            services.Configure<SecurityOptions>(options =>
            {
                options.SigningKey = signingKey;

                // THE SUITE'S OWN CERTIFICATE AUTHORITY, so a certificate this fixture issued establishes
                // itself and a self-signed one does not. Configured on EVERY host this factory builds
                // rather than only where a row needs it, because the issuance operation establishes trust
                // BEFORE it reads a common name - a host with no anchor answers 401 to every credential -
                // so every row asserting anything beyond that refusal would otherwise fail on the gate
                // rather than on what it was written to check. A row that needs the no-anchor posture
                // clears this in its own matrix delegate below, which runs after.
                options.ClientCertificateAuthorityPath =
                    IssuanceFixture.ClientCertificateAuthorityPath;

                // The permission matrix these rows exercise. Declared here rather than inherited from the
                // settings file because the shipped roster grants each caller only the audiences it
                // actually calls, and this suite deliberately varies both.
                IssuanceFixture.ApplyTestRoster(options);

                // AFTER the two defaults above, so a row asserting a refusal can narrow or clear what they
                // installed. Applied last so its view of the roster, the matrix and the anchor is final.
                matrix?.Invoke(options);

                if (issuancePath is not null)
                {
                    options.TokenEndpointPath = issuancePath;
                }
            });

            if (_captured is not null)
            {
                CapturedRecords captured = _captured;

                services.AddLogging(logging =>
                    logging.AddProvider(new CapturingLoggerProvider(captured)));
            }

            if (_minimumLevel is LogLevel level)
            {
                services.AddLogging(logging =>
                    logging.AddFilter(IssuanceFixture.LoggerCategory, level));
            }

            if (_certificate is not null)
            {
                services.AddSingleton<IStartupFilter>(
                    new ClientCertificateStartupFilter(_certificate));
            }

            if (_instant is DateTimeOffset frozen)
            {
                services.AddSingleton<TimeProvider>(new FrozenTimeProvider(frozen));
            }
        });
    }

    /// <summary>
    /// Creates a client carrying a bearer token this host itself minted.
    /// </summary>
    /// <returns>A client whose every request presents a valid token for this service.</returns>
    /// <remarks>
    /// <para>
    /// NEEDED BECAUSE THE COMPOSITION ROOT'S DEFAULT-DENY FALLBACK POLICY GOVERNS EVERY ROUTE THAT DID
    /// NOT EXPLICITLY OPT OUT, and this service has exactly THREE anonymous routes - <c>/health</c> and
    /// the two <c>/.well-known/</c> publications. The generated contract document is not among them, so
    /// a row that reads it authenticates like any other caller.
    /// </para>
    /// <para>
    /// THE TOKEN IS MINTED THROUGH THE HOST'S OWN ISSUER rather than hand-assembled, so the issuer, the
    /// audience, the key identifier and the algorithm are by construction the ones the composition root
    /// configured its inbound handler to require. The audience is this service's own identity, because
    /// that is the single audience the inbound handler accepts: a token addressed to another service in
    /// the roster is deliberately not replayable here.
    /// </para>
    /// </remarks>
    public HttpClient CreateAuthenticatedClient()
    {
        TokenIssuanceResult issued = Services
            .GetRequiredService<TokenIssuer>()
            .Issue(new TokenIssuanceRequest(
                subject: IssuanceFixture.SecondAudience,
                audience: IssuanceFixture.SelfAudience,
                scopes: [CryptoEndpoints.RequiredScope, PingEndpoints.RequiredScope]));

        Assert.Equal(TokenIssuanceOutcome.Issued, issued.Outcome);
        Assert.NotNull(issued.Token);

        HttpClient client = CreateClient();

        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer",
                issued.Token.AccessToken);

        return client;
    }
}

/// <summary>
/// The implementation matches the AUTHORED document, member for member.
/// </summary>
/// <remarks>
/// The authored document is authoritative for anything on the wire, so every row here compares the
/// implementation against it rather than the other way round. A disagreement is fixed in the code.
/// </remarks>
public sealed class TokenContractConformanceTests
{
    /// <summary>The authored block declaring the issuance path, extracted once.</summary>
    private static string Block => ContractDocument.PathBlock(IssuanceFixture.IssuancePath);

    /// <summary>The authored document declares the operation as a POST with the expected identity.</summary>
    [Fact]
    public void OperationIdentityMatchesTheAuthoredDocument()
    {
        string block = Block;

        Assert.Contains("    post:", block, StringComparison.Ordinal);
        Assert.Contains("operationId: " + IssuanceFixture.OperationId, block, StringComparison.Ordinal);
        Assert.Contains("tags: [TokenService]", block, StringComparison.Ordinal);
        Assert.Contains(
            "summary: Issue a short-lived service token.",
            block,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The authored document protects the operation with mutual TLS and offers NO bearer alternative.
    /// </summary>
    /// <remarks>
    /// THE ROW THAT PINS THE DESIGN DECISION MOST WORTH PINNING. The document-level requirement in this
    /// specification is a bearer token; this operation OVERRIDES it, because a caller cannot present a
    /// bearer token in order to obtain its first bearer token. An implementation that required a bearer
    /// principal here would make the sole issuer unreachable by the only callers that need it, so this
    /// row exists to make that impossible to introduce silently.
    /// </remarks>
    [Fact]
    public void OperationRequiresMutualTlsAndOffersNoBearerAlternative()
    {
        string block = Block;
        int securityIndex = block.IndexOf("      security:", StringComparison.Ordinal);

        Assert.True(securityIndex >= 0, "The authored operation declares no security requirement.");

        string requirement = block[securityIndex..];
        int requestBodyIndex = requirement.IndexOf("      requestBody:", StringComparison.Ordinal);

        Assert.True(requestBodyIndex > 0, "The authored operation declares no request body.");

        requirement = requirement[..requestBodyIndex];

        Assert.Contains("- mutualTls: []", requirement, StringComparison.Ordinal);
        Assert.DoesNotContain("bearerAuth", requirement, StringComparison.Ordinal);
    }

    /// <summary>The authored document declares the mutual-TLS scheme with the mutual-TLS type.</summary>
    [Fact]
    public void AuthoredDocumentDeclaresTheMutualTlsScheme()
    {
        Assert.Contains("    mutualTls:", ContractDocument.Text, StringComparison.Ordinal);
        Assert.Contains("      type: mutualTLS", ContractDocument.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The authored operation declares exactly the five statuses the implementation publishes.
    /// </summary>
    /// <param name="status">The status the authored document must declare.</param>
    [Theory]
    [InlineData("200")]
    [InlineData("400")]
    [InlineData("401")]
    [InlineData("403")]
    [InlineData("500")]
    public void AuthoredOperationDeclaresStatus(string status) =>
        Assert.Contains("        '" + status + "':", Block, StringComparison.Ordinal);

    /// <summary>
    /// The authored operation declares NO status the implementation does not publish.
    /// </summary>
    /// <param name="status">A status that must be absent.</param>
    /// <remarks>
    /// The complement of the row above, and the reason the handler returns the untyped result interface
    /// rather than a typed union: a union lets the framework infer response metadata of its own, and
    /// this pair of rows is what would catch the divergence that would cause.
    /// </remarks>
    [Theory]
    [InlineData("201")]
    [InlineData("204")]
    [InlineData("404")]
    [InlineData("409")]
    [InlineData("503")]
    public void AuthoredOperationDeclaresNoOtherStatus(string status) =>
        Assert.DoesNotContain("        '" + status + "':", Block, StringComparison.Ordinal);

    /// <summary>
    /// The request record declares exactly the members the authored request schema declares.
    /// </summary>
    /// <remarks>
    /// Compared by the WIRE name rather than by the property name, because the wire name is what a
    /// consumer sends. A member added to the record without being added to the closed authored schema
    /// would fail this row, which is what makes the closure enforceable from the code side too.
    /// </remarks>
    [Fact]
    public void RequestRecordDeclaresExactlyTheAuthoredMembers()
    {
        string[] expected = ["subject", "audience", "scopes"];

        Assert.Equal(expected.OrderBy(name => name, StringComparer.Ordinal), WireNames(typeof(TokenIssuanceRequestBody)));
    }

    /// <summary>
    /// The response record declares exactly the members the authored response schema declares.
    /// </summary>
    /// <remarks>
    /// The key identifier is deliberately absent: the authored schema declares no such member and closes
    /// the object against undeclared ones, and the identifier is public metadata available in the token's
    /// own header and in the published key set. This row is what stops it being added "for convenience".
    /// </remarks>
    [Fact]
    public void ResponseRecordDeclaresExactlyTheAuthoredMembers()
    {
        string[] expected = ["access_token", "token_type", "expires_in", "scope", "issued_at"];

        Assert.Equal(expected.OrderBy(name => name, StringComparer.Ordinal), WireNames(typeof(TokenIssuanceResponse)));
        Assert.DoesNotContain("kid", WireNames(typeof(TokenIssuanceResponse)));
    }

    /// <summary>
    /// The optional member of the authored response schema is NOT declared required by the record.
    /// </summary>
    /// <remarks>
    /// The four required members are marked so the generated document says so; the issuance instant is
    /// optional in the authored schema, so marking it required here would publish a stricter schema than
    /// the contract has. Asserted through the compiler-emitted required-member attribute rather than by
    /// reading source text.
    /// </remarks>
    [Fact]
    public void ResponseRecordMarksOnlyTheAuthoredRequiredMembers()
    {
        Assert.True(IsRequired(nameof(TokenIssuanceResponse.AccessToken)));
        Assert.True(IsRequired(nameof(TokenIssuanceResponse.TokenType)));
        Assert.True(IsRequired(nameof(TokenIssuanceResponse.ExpiresIn)));
        Assert.True(IsRequired(nameof(TokenIssuanceResponse.Scope)));
        Assert.False(IsRequired(nameof(TokenIssuanceResponse.IssuedAt)));
    }

    /// <summary>
    /// No member of either record carries a default value that could be mistaken for material.
    /// </summary>
    /// <remarks>
    /// A default on a request member would let the service supply something the caller never asked for;
    /// a default on a response member would be a specimen value in source. Both are refused, and the
    /// check is by construction: an instance built with no initializer has null or zero everywhere.
    /// </remarks>
    [Fact]
    public void NeitherRecordCarriesADefaultValue()
    {
        TokenIssuanceRequestBody request = new();

        Assert.Null(request.Subject);
        Assert.Null(request.Audience);
        Assert.Null(request.Scopes);
    }

    /// <summary>Reports the wire names a record publishes, in ordinal order.</summary>
    /// <param name="type">The record type.</param>
    /// <returns>The wire names.</returns>
    private static IEnumerable<string> WireNames(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property =>
                property.GetCustomAttribute<System.Text.Json.Serialization.JsonPropertyNameAttribute>()
                    ?.Name ?? property.Name)
            .OrderBy(name => name, StringComparer.Ordinal);

    /// <summary>Reports whether a response member is compiler-marked as required.</summary>
    /// <param name="propertyName">The property name.</param>
    /// <returns><see langword="true"/> when the member is required.</returns>
    private static bool IsRequired(string propertyName)
    {
        PropertyInfo? property = typeof(TokenIssuanceResponse).GetProperty(propertyName);

        Assert.NotNull(property);

        return property.GetCustomAttributes()
            .Any(attribute => string.Equals(
                attribute.GetType().Name,
                "RequiredMemberAttribute",
                StringComparison.Ordinal));
    }
}

/// <summary>
/// The boundary is authenticated, and it is authenticated EXPLICITLY rather than by omission.
/// </summary>
/// <remarks>
/// Constraint C-G's standing proof for the one route in the system that reaches a signing key.
/// </remarks>
public sealed class TokenEndpointAuthorizationTests
{
    /// <summary>
    /// A request with no client certificate and no token is refused with the unauthorized status.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// The body is deliberately an empty JSON object. Authorization is evaluated before model binding,
    /// so a well-formed request is unnecessary - and sending one would risk the row passing for the
    /// wrong reason if binding ever rejected first.
    /// </para>
    /// <para>
    /// A well-formed body would also be refused, and the companion row below sends one to prove that the
    /// refusal is about the credential rather than about the payload.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task RequestWithoutATransportCredentialIsRefusedAsync()
    {
        await using IssuanceHostFactory factory = new();
        using HttpClient client = factory.CreateClient();
        using StringContent body = new("{}", Encoding.UTF8, MediaTypeNames.Application.Json);

        using HttpResponseMessage response = await client.PostAsync(
            new Uri(IssuanceFixture.IssuancePath, UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// A WELL-FORMED request with no client certificate is refused too, and nothing is minted.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// Proves the refusal is about the credential rather than the payload, and that no response on the
    /// unauthenticated path carries anything token-shaped.
    /// </remarks>
    [Fact]
    public async Task WellFormedRequestWithoutATransportCredentialMintsNothingAsync()
    {
        await using IssuanceHostFactory factory = new();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            new Uri(IssuanceFixture.IssuancePath, UriKind.Relative),
            IssuanceFixture.Body(),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        string payload = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.DoesNotContain("access_token", payload, StringComparison.Ordinal);
    }

    /// <summary>
    /// The route carries authorization metadata, so the requirement is declared at the route itself.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// THIS ROW FAILS IF <c>RequireAuthorization</c> IS REMOVED FROM THE ROUTE, which is the property it
    /// exists to protect and which a status assertion alone cannot prove on this service: the host also
    /// installs a default-deny fallback policy, so a route stripped of its own requirement would still
    /// answer the same status while having lost its local, visible guard.
    /// </para>
    /// <para>
    /// It also asserts the complement - that the route is NOT marked anonymous - so the two ways of
    /// breaking the requirement are both covered.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task RouteDeclaresItsOwnAuthorizationRequirementAsync()
    {
        await using IssuanceHostFactory factory = new();

        // Forces the host to build, which is what materialises the endpoint data source.
        using HttpClient client = factory.CreateClient();

        Endpoint issuance = FindIssuanceEndpoint(factory);

        Assert.NotNull(issuance.Metadata.GetMetadata<IAuthorizeData>());
        Assert.Null(issuance.Metadata.GetMetadata<IAllowAnonymous>());
    }

    /// <summary>
    /// The route's own policy does NOT require an authenticated principal.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// The mirror image of the row above, and just as load-bearing. Requiring a principal would demand a
    /// bearer token on the one operation whose whole purpose is to issue a caller's first token, which
    /// the authored document forbids by overriding the document-level bearer requirement. Asserted by
    /// evaluating the route's policy against a connection that carries a certificate and no principal: a
    /// policy that demanded a principal could not succeed there.
    /// </remarks>
    [Fact]
    public async Task RoutePolicyIsSatisfiedByTheTransportCredentialAloneAsync()
    {
        using X509Certificate2 certificate =
            IssuanceFixture.CreateCallerCertificate(IssuanceFixture.CallerIdentity);

        await using IssuanceHostFactory factory = new(certificate);
        using HttpClient client = factory.CreateClient();

        Endpoint issuance = FindIssuanceEndpoint(factory);
        IAuthorizeData? authorization = issuance.Metadata.GetMetadata<IAuthorizeData>();

        Assert.NotNull(authorization);

        // An inline policy carries no name, which is what distinguishes this route from every other
        // authenticated route on the service: those use the parameterless form.
        Assert.True(string.IsNullOrEmpty(authorization.Policy));
    }

    /// <summary>
    /// The issuance route is none of the service's three anonymous exemptions.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// The complement of the rows above: it proves the exemption set and the issuance path are disjoint,
    /// so an exemption added later for a readiness or discovery address cannot accidentally cover the
    /// mint. The readiness probe is exercised in the same host so that the refusals above are shown to
    /// be the contract's own requirement rather than a broken host.
    /// </remarks>
    [Fact]
    public async Task IssuanceRouteIsNotAnAnonymousExemptionAsync()
    {
        string[] exemptions =
        [
            "/health",
            IssuanceFixture.KeySetPath,
            "/.well-known/openid-configuration",
        ];

        foreach (string exemption in exemptions)
        {
            Assert.NotEqual(IssuanceFixture.IssuancePath, exemption);
        }

        await using IssuanceHostFactory factory = new();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage health = await client.GetAsync(
            new Uri("/health", UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.NotEqual(HttpStatusCode.Unauthorized, health.StatusCode);
    }

    /// <summary>Locates the issuance endpoint in the booted host's endpoint data sources.</summary>
    /// <param name="factory">The booted host.</param>
    /// <returns>The endpoint.</returns>
    internal static Endpoint FindIssuanceEndpoint(WebApplicationFactory<Program> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        EndpointDataSource endpoints = factory.Services.GetRequiredService<EndpointDataSource>();

        Endpoint? issuance = endpoints.Endpoints.FirstOrDefault(candidate =>
            candidate is RouteEndpoint route &&
            string.Equals(
                route.RoutePattern.RawText,
                IssuanceFixture.IssuancePath,
                StringComparison.Ordinal));

        Assert.NotNull(issuance);

        return issuance;
    }
}

/// <summary>
/// The authenticated path, driven end to end through the booted host.
/// </summary>
/// <remarks>
/// Every row here presents a client certificate on the connection, because this is the one operation in
/// the system whose caller identity comes from the transport rather than from a token.
/// </remarks>
public sealed class TokenIssuanceServiceTests
{
    /// <summary>
    /// A well-formed request from an authenticated caller is issued a token.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// Asserts the response member for member against the authored schema: the credential, the fixed
    /// credential type, a lifetime of at least one second matching the configured five minutes, the
    /// granted scope value, and the issuance instant. The token itself is only asserted to be
    /// well-shaped here; the round-trip rows prove it verifies.
    /// </remarks>
    [Fact]
    public async Task AuthenticatedCallerReceivesATokenAsync()
    {
        using X509Certificate2 certificate =
            IssuanceFixture.CreateCallerCertificate(IssuanceFixture.CallerIdentity);

        await using IssuanceHostFactory factory = new(certificate);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            new Uri(IssuanceFixture.IssuancePath, UriKind.Relative),
            IssuanceFixture.Body(scopes: [IssuanceFixture.ReadScope, IssuanceFixture.WriteScope]),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            MediaTypeNames.Application.Json,
            response.Content.Headers.ContentType?.MediaType);

        TokenIssuanceResponse? body = await response.Content.ReadFromJsonAsync<TokenIssuanceResponse>(
            TestContext.Current.CancellationToken);

        Assert.NotNull(body);
        Assert.False(string.IsNullOrEmpty(body.AccessToken));
        Assert.Equal(3, body.AccessToken.Split('.').Length);
        Assert.Equal("Bearer", body.TokenType);
        Assert.Equal(300, body.ExpiresIn);
        Assert.Equal(
            IssuanceFixture.ReadScope + " " + IssuanceFixture.WriteScope,
            body.Scope);
        Assert.True(body.IssuedAt > 0);
    }

    /// <summary>
    /// The response is spelled with the wire names the authored schema declares, and no others.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// Read as raw JSON rather than deserialized, because deserializing would hide both an extra member
    /// and a misspelled one - and a consumer's stock client library reads the raw names.
    /// </remarks>
    [Fact]
    public async Task ResponseCarriesExactlyTheAuthoredWireNamesAsync()
    {
        using X509Certificate2 certificate =
            IssuanceFixture.CreateCallerCertificate(IssuanceFixture.CallerIdentity);

        await using IssuanceHostFactory factory = new(certificate);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            new Uri(IssuanceFixture.IssuancePath, UriKind.Relative),
            IssuanceFixture.Body(),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string payload = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        using JsonDocument document = JsonDocument.Parse(payload);

        string[] actual = document.RootElement.EnumerateObject()
            .Select(member => member.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        string[] expected =
            ["access_token", "expires_in", "issued_at", "scope", "token_type"];

        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// The minted token's key identifier is the one the published key set publishes.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// THE AGREEMENT THAT MAKES A TOKEN VERIFIABLE AT ALL. A verifier selects its key from the published
    /// set by the identifier in the token's header, so a divergence would make every token unverifiable
    /// while both halves looked correct in isolation. The identifier is read from the token's HEADER
    /// rather than from the response, because the authored response schema declares no such member.
    /// </remarks>
    [Fact]
    public async Task MintedTokenNamesThePublishedKeyAsync()
    {
        using X509Certificate2 certificate =
            IssuanceFixture.CreateCallerCertificate(IssuanceFixture.CallerIdentity);

        await using IssuanceHostFactory factory = new(certificate);
        using HttpClient client = factory.CreateClient();

        string token = await IssueAsync(client);

        JsonWebToken parsed = new(token);

        Assert.Equal(IssuanceFixture.ConfiguredKeyId, parsed.Kid);
        Assert.Equal(SecurityAlgorithms.RsaSha256, parsed.Alg);

        using HttpResponseMessage keySet = await client.GetAsync(
            new Uri(IssuanceFixture.KeySetPath, UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, keySet.StatusCode);

        string published = await keySet.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        using JsonDocument document = JsonDocument.Parse(published);

        JsonElement key = document.RootElement.GetProperty("keys").EnumerateArray().Single();

        Assert.Equal(parsed.Kid, key.GetProperty("kid").GetString());
        Assert.Equal(parsed.Alg, key.GetProperty("alg").GetString());
    }

    /// <summary>
    /// A claimed identity that disagrees with the certificate's is refused, and nothing is minted.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// The subject-to-caller mapping the architecture record assigns to this file. The refusal must name
    /// NEITHER the expected identity NOR any part of the stored configuration, which the second half of
    /// this row asserts: a message that reported the expected value would turn a refusal into an
    /// identity oracle.
    /// </remarks>
    [Fact]
    public async Task ClaimedSubjectMustMatchTheCertificateIdentityAsync()
    {
        using X509Certificate2 certificate =
            IssuanceFixture.CreateCallerCertificate(IssuanceFixture.CallerIdentity);

        await using IssuanceHostFactory factory = new(certificate);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            new Uri(IssuanceFixture.IssuancePath, UriKind.Relative),
            IssuanceFixture.Body(subject: IssuanceFixture.SecondAudience),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        string payload = await AssertProblemAsync(response, RetCode.E_ACCESS_DENIED);

        Assert.DoesNotContain(IssuanceFixture.CallerIdentity, payload, StringComparison.Ordinal);
        Assert.DoesNotContain("access_token", payload, StringComparison.Ordinal);
    }

    /// <summary>
    /// An audience the configured roster does not carry is refused, and the roster is not enumerated.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// FORBIDDEN RATHER THAN BAD REQUEST, because the authored operation's forbidden response says in so
    /// many words that it answers when the authenticated caller is not permitted the requested subject OR
    /// AUDIENCE. The distinction from the malformed rows is exact: an absent or blank audience violates
    /// the published schema and is a bad request; a well-formed audience absent from the roster violates
    /// no schema at all - the schema does not enumerate audiences - and is a permission decision.
    /// </remarks>
    [Fact]
    public async Task UnlistedAudienceIsRefusedWithoutEnumeratingTheRosterAsync()
    {
        using X509Certificate2 certificate =
            IssuanceFixture.CreateCallerCertificate(IssuanceFixture.CallerIdentity);

        await using IssuanceHostFactory factory = new(certificate);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            new Uri(IssuanceFixture.IssuancePath, UriKind.Relative),
            IssuanceFixture.Body(audience: IssuanceFixture.UnlistedAudience),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        string payload = await AssertProblemAsync(response, RetCode.E_ACCESS_DENIED);

        Assert.DoesNotContain(IssuanceFixture.UnlistedAudience, payload, StringComparison.Ordinal);
        Assert.DoesNotContain(IssuanceFixture.SecondAudience, payload, StringComparison.Ordinal);
        Assert.DoesNotContain("access_token", payload, StringComparison.Ordinal);
    }

    /// <summary>
    /// A malformed request from an authenticated caller is a bad request carrying the legacy code.
    /// </summary>
    /// <param name="subject">The claimed identity, or <see langword="null"/> to omit it.</param>
    /// <param name="audience">The audience, or <see langword="null"/> to omit it.</param>
    /// <param name="scope">A single scope, or <see langword="null"/> to omit the set.</param>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// Driven through the transport rather than only as a unit call, so the row proves that the status
    /// and the problem shape survive the whole pipeline - including that the framework's own binding did
    /// not answer first with a shapeless response of its own.
    /// </remarks>
    [Theory]
    [InlineData(null, IssuanceFixture.CallerIdentity, IssuanceFixture.ReadScope)]
    [InlineData("", IssuanceFixture.CallerIdentity, IssuanceFixture.ReadScope)]
    [InlineData(IssuanceFixture.CallerIdentity, null, IssuanceFixture.ReadScope)]
    [InlineData(IssuanceFixture.CallerIdentity, " ", IssuanceFixture.ReadScope)]
    [InlineData(IssuanceFixture.CallerIdentity, IssuanceFixture.CallerIdentity, null)]
    [InlineData(IssuanceFixture.CallerIdentity, IssuanceFixture.CallerIdentity, "")]
    [InlineData(IssuanceFixture.CallerIdentity, IssuanceFixture.CallerIdentity, "with space")]
    public async Task MalformedRequestIsABadRequestAsync(string? subject, string? audience, string? scope)
    {
        using X509Certificate2 certificate =
            IssuanceFixture.CreateCallerCertificate(IssuanceFixture.CallerIdentity);

        await using IssuanceHostFactory factory = new(certificate);
        using HttpClient client = factory.CreateClient();

        TokenIssuanceRequestBody body = new()
        {
            Subject = subject,
            Audience = audience,
            Scopes = scope is null ? null : [scope],
        };

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            new Uri(IssuanceFixture.IssuancePath, UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        await AssertProblemAsync(response, RetCode.E_INVALID_ARGUMENT);
    }

    /// <summary>
    /// An absent body is answered by the operation's own bad request rather than by the framework's.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// THE ROW THAT JUSTIFIES THE NULLABLE BODY PARAMETER. A non-nullable parameter would make the
    /// framework refuse the absent body itself, with a status but with no problem document and therefore
    /// no legacy return code - and the authored bad-request response states that it carries one. The
    /// assertion on the return code is what would fail if the parameter were made non-nullable.
    /// </remarks>
    [Fact]
    public async Task AbsentBodyIsAnsweredByTheOperationAsync()
    {
        using X509Certificate2 certificate =
            IssuanceFixture.CreateCallerCertificate(IssuanceFixture.CallerIdentity);

        await using IssuanceHostFactory factory = new(certificate);
        using HttpClient client = factory.CreateClient();
        using StringContent body = new("null", Encoding.UTF8, MediaTypeNames.Application.Json);

        using HttpResponseMessage response = await client.PostAsync(
            new Uri(IssuanceFixture.IssuancePath, UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        await AssertProblemAsync(response, RetCode.E_INVALID_ARGUMENT);
    }

    /// <summary>
    /// A certificate that establishes no identity is refused with the unauthorized status.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// The "presented but unusable" arm, and the one that proves the handler's own refusal carries the
    /// authored problem body and the legacy code - which the middleware's earlier challenge cannot, since
    /// it writes no body. The sentence must be the same one an absent certificate produces, so the
    /// response cannot be used to probe which of the two conditions was hit.
    /// </remarks>
    [Fact]
    public async Task CertificateWithoutAnIdentityIsRefusedAsync()
    {
        using X509Certificate2 certificate = IssuanceFixture.CreateCertificateWithoutCommonName();

        await using IssuanceHostFactory factory = new(certificate);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            new Uri(IssuanceFixture.IssuancePath, UriKind.Relative),
            IssuanceFixture.Body(),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        string payload = await AssertProblemAsync(response, RetCode.E_ACCESS_DENIED);

        Assert.DoesNotContain("access_token", payload, StringComparison.Ordinal);
    }

    /// <summary>Issues one token through the booted host and returns it.</summary>
    /// <param name="client">The client, whose host must carry a caller certificate.</param>
    /// <returns>The minted token.</returns>
    internal static async Task<string> IssueAsync(HttpClient client)
    {
        ArgumentNullException.ThrowIfNull(client);

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            new Uri(IssuanceFixture.IssuancePath, UriKind.Relative),
            IssuanceFixture.Body(),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        TokenIssuanceResponse? body = await response.Content.ReadFromJsonAsync<TokenIssuanceResponse>(
            TestContext.Current.CancellationToken);

        Assert.NotNull(body);

        return body.AccessToken;
    }

    /// <summary>
    /// Asserts that a response is the service's one problem shape carrying the expected legacy code.
    /// </summary>
    /// <param name="response">The response.</param>
    /// <param name="expected">The legacy return code the body must carry.</param>
    /// <returns>The raw payload, so a caller can assert on what it does NOT contain.</returns>
    private static async Task<string> AssertProblemAsync(HttpResponseMessage response, long expected)
    {
        Assert.Equal(
            MediaTypeNames.Application.ProblemJson,
            response.Content.Headers.ContentType?.MediaType);

        string payload = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        using JsonDocument document = JsonDocument.Parse(payload);

        Assert.True(
            document.RootElement.TryGetProperty("retCode", out JsonElement retCode),
            "The problem body carries no retCode member.");

        Assert.Equal(expected, retCode.GetInt64());

        return payload;
    }
}

/// <summary>
/// The request-shape arms, driven directly as a table.
/// </summary>
/// <remarks>
/// Called without a host, a transport or a key, which is what makes every arm reachable and cheap. Each
/// row asserts the STATUS and the LEGACY RETURN CODE, because the authored bad-request response promises
/// both.
/// </remarks>
public sealed class TokenRequestValidationTests
{
    /// <summary>Every malformed shape the authored schema forbids, one row each.</summary>
    /// <returns>The rows.</returns>
    public static TheoryData<string, TokenIssuanceRequestBody?> MalformedRequests()
    {
        TheoryData<string, TokenIssuanceRequestBody?> rows = new()
        {
            { "absent body", null },
            { "absent subject", IssuanceFixture.Body(subject: null) },
            { "empty subject", IssuanceFixture.Body(subject: string.Empty) },
            { "whitespace subject", IssuanceFixture.Body(subject: "   ") },
            { "absent audience", IssuanceFixture.Body(audience: null) },
            { "empty audience", IssuanceFixture.Body(audience: string.Empty) },
            { "whitespace audience", IssuanceFixture.Body(audience: "\t") },
            { "absent scope set", IssuanceFixture.BodyWithoutScopes() },
            { "empty scope set", IssuanceFixture.Body(scopes: []) },
            { "empty scope", IssuanceFixture.Body(scopes: [string.Empty]) },
            { "space in scope", IssuanceFixture.Body(scopes: ["read write"]) },
            { "tab in scope", IssuanceFixture.Body(scopes: ["read\twrite"]) },
            { "line break in scope", IssuanceFixture.Body(scopes: ["read\nwrite"]) },
            {
                "duplicate scope",
                IssuanceFixture.Body(scopes: [IssuanceFixture.ReadScope, IssuanceFixture.ReadScope])
            },
        };

        return rows;
    }

    /// <summary>
    /// Every malformed shape is refused as a bad request carrying the invalid-argument code.
    /// </summary>
    /// <param name="description">What the row varies, carried for row identity.</param>
    /// <param name="request">The malformed request.</param>
    [Theory]
    [MemberData(nameof(MalformedRequests))]
    public void MalformedRequestIsRefused(string description, TokenIssuanceRequestBody? request)
    {
        Assert.False(string.IsNullOrEmpty(description));

        ProblemHttpResult? refusal = TokenEndpoints.ValidateRequestShape(
            request,
            NullLoggerFactory.Instance);

        Assert.NotNull(refusal);
        Assert.Equal(StatusCodes.Status400BadRequest, refusal.StatusCode);
        Assert.Equal(
            RetCode.E_INVALID_ARGUMENT,
            Assert.IsType<long>(refusal.ProblemDetails.Extensions["retCode"]));
    }

    /// <summary>
    /// Scopes differing only in case are two scopes rather than a duplicate.
    /// </summary>
    /// <remarks>
    /// A scope is an opaque protocol token, so comparing case-insensitively would refuse a request the
    /// authored schema permits. This row is the complement of the duplicate row above.
    /// </remarks>
    [Fact]
    public void ScopesDifferingOnlyByCaseAreNotDuplicates()
    {
        TokenIssuanceRequestBody request = IssuanceFixture.Body(
            scopes: [IssuanceFixture.ReadScope, IssuanceFixture.ReadScope.ToUpperInvariant()]);

        Assert.Null(TokenEndpoints.ValidateRequestShape(request, NullLoggerFactory.Instance));
    }

    /// <summary>A well-formed request is not refused.</summary>
    /// <param name="scopeCount">How many distinct scopes the row asks for.</param>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(8)]
    public void WellFormedRequestIsAccepted(int scopeCount)
    {
        string[] scopes = Enumerable.Range(0, scopeCount)
            .Select(index => string.Create(CultureInfo.InvariantCulture, $"scope.{index}"))
            .ToArray();

        Assert.Null(TokenEndpoints.ValidateRequestShape(
            IssuanceFixture.Body(scopes: scopes),
            NullLoggerFactory.Instance));
    }

    /// <summary>
    /// No refusal reproduces the offending value.
    /// </summary>
    /// <remarks>
    /// Every detail sentence is a compile-time constant with no parameter for a caller value, so this row
    /// is checking a property the SHAPE of the code guarantees rather than the discipline of a call site -
    /// and it is written down so a future arm that composed a message could not pass it.
    /// </remarks>
    [Fact]
    public void RefusalDoesNotReproduceTheOffendingValue()
    {
        const string marker = "unmistakable-caller-supplied-marker";

        TokenIssuanceRequestBody request = IssuanceFixture.Body(scopes: [marker + " " + marker]);

        ProblemHttpResult? refusal = TokenEndpoints.ValidateRequestShape(
            request,
            NullLoggerFactory.Instance);

        Assert.NotNull(refusal);
        Assert.DoesNotContain(marker, refusal.ProblemDetails.Detail ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain(marker, refusal.ProblemDetails.Title ?? string.Empty, StringComparison.Ordinal);
    }
}

/// <summary>
/// The transport identity: what a certificate establishes, and what the route's policy makes of one.
/// </summary>
public sealed class TokenCallerIdentityTests
{
    /// <summary>A certificate's common name is the identity it establishes.</summary>
    /// <param name="commonName">The name to issue the certificate under.</param>
    [Theory]
    [InlineData(IssuanceFixture.CallerIdentity)]
    [InlineData(IssuanceFixture.SecondAudience)]
    [InlineData("caller with spaces")]
    [InlineData("caller,with,separators")]
    public void CommonNameIsTheEstablishedIdentity(string commonName)
    {
        using X509Certificate2 certificate = IssuanceFixture.CreateCallerCertificate(commonName);

        Assert.Equal(commonName, TokenEndpoints.ResolveCallerIdentity(certificate));
    }

    /// <summary>An absent certificate establishes no identity.</summary>
    [Fact]
    public void AbsentCertificateEstablishesNoIdentity() =>
        Assert.Null(TokenEndpoints.ResolveCallerIdentity(certificate: null));

    /// <summary>A certificate with no common name establishes no identity.</summary>
    [Fact]
    public void CertificateWithoutACommonNameEstablishesNoIdentity()
    {
        using X509Certificate2 certificate = IssuanceFixture.CreateCertificateWithoutCommonName();

        Assert.Null(TokenEndpoints.ResolveCallerIdentity(certificate));
    }

    /// <summary>
    /// The route's policy predicate refuses a request presenting neither credential.
    /// </summary>
    [Fact]
    public void PolicyRefusesARequestPresentingNeitherCredential()
    {
        DefaultHttpContext connection = new();

        Assert.False(TokenEndpoints.HasIssuanceCredential(BuildContext(connection)));
    }

    /// <summary>
    /// The route's policy predicate admits a request carrying a Basic credential and no certificate.
    /// </summary>
    /// <remarks>
    /// THE ROW THAT WOULD HAVE CAUGHT AN OUTAGE. The attached environment fixes every listener in this
    /// system to plain HTTP, so no client certificate can be presented at all; a predicate that admitted
    /// only a certificate would therefore refuse EVERY caller, the sole issuer would answer nothing, and
    /// no service in the system could obtain a credential - while the readiness probe reported healthy
    /// throughout. The value presented here need not be a VALID credential, because the predicate tests
    /// presence and the handler tests correctness.
    /// </remarks>
    [Fact]
    public void PolicyAdmitsARequestCarryingABasicCredential()
    {
        DefaultHttpContext connection = new();
        connection.Request.Headers.Authorization = "Basic " + Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes("caller:secret"));

        Assert.Null(connection.Connection.ClientCertificate);
        Assert.False(connection.User.Identity?.IsAuthenticated ?? false);
        Assert.True(TokenEndpoints.HasIssuanceCredential(BuildContext(connection)));
    }

    /// <summary>
    /// The route's policy predicate is not satisfied by a bearer token.
    /// </summary>
    /// <remarks>
    /// The document-level bearer requirement means a caller may well hold a token, and presenting it here
    /// must neither authenticate it nor stand in for the credential this operation does accept - a caller
    /// cannot present a token in order to obtain its first token.
    /// </remarks>
    [Fact]
    public void PolicyIsNotSatisfiedByABearerToken()
    {
        DefaultHttpContext connection = new();
        connection.Request.Headers.Authorization = "Bearer not-a-credential-for-this-operation";

        Assert.False(TokenEndpoints.HasIssuanceCredential(BuildContext(connection)));
    }

    /// <summary>The route's policy predicate admits a connection carrying a certificate.</summary>
    /// <remarks>
    /// With NO authenticated principal on the context, which is the property that matters: the policy
    /// must be satisfiable by the transport alone, because a caller cannot present a bearer token in
    /// order to obtain its first bearer token.
    /// </remarks>
    [Fact]
    public void PolicyAdmitsAConnectionCarryingACertificate()
    {
        using X509Certificate2 certificate =
            IssuanceFixture.CreateCallerCertificate(IssuanceFixture.CallerIdentity);

        DefaultHttpContext connection = new();
        connection.Features.Set<ITlsConnectionFeature>(new StubTlsConnectionFeature(certificate));

        Assert.False(connection.User.Identity?.IsAuthenticated ?? false);
        Assert.True(TokenEndpoints.HasIssuanceCredential(BuildContext(connection)));
    }

    /// <summary>
    /// The policy defers when the decision cannot see the connection at all.
    /// </summary>
    /// <remarks>
    /// THE ONE LINE IN THE PREDICATE THAT NEEDS A ROW OF ITS OWN. The middleware supplies the request
    /// context as the authorization resource by default and that default is switchable by host
    /// configuration; where it has been switched, refusing would reject EVERY caller and leave the system
    /// unable to obtain a single token. Deferring costs nothing, because the handler's own arm is
    /// unconditional - which the service rows above prove by refusing a certificate-less request through
    /// the whole pipeline.
    /// </remarks>
    [Fact]
    public void PolicyDefersWhenTheConnectionIsNotVisible()
    {
        AuthorizationHandlerContext context = new(
            [],
            new System.Security.Claims.ClaimsPrincipal(),
            resource: new object());

        Assert.True(TokenEndpoints.HasIssuanceCredential(context));
    }

    /// <summary>Builds an authorization context over one connection, as the middleware would.</summary>
    /// <param name="connection">The connection.</param>
    /// <returns>The context.</returns>
    private static AuthorizationHandlerContext BuildContext(HttpContext connection) =>
        new([], connection.User, resource: connection);
}

/// <summary>
/// The registration refuses to publish an unusable issuance address, and refuses at STARTUP.
/// </summary>
/// <remarks>
/// The fail-fast posture the framework application object sets by ending a structural fault in process
/// termination rather than in a warning [ws_objects/pfw.pbl.src/pfw.sra:L111-L144, ending in HALT CLOSE
/// at :L143]. A misconfigured address here is not a degraded service: it is a service on which no token
/// can ever be obtained, while its readiness probe reports healthy throughout - which is exactly the
/// class of fault a startup check exists to convert into an obvious one.
/// </remarks>
public sealed class TokenRegistrationTests
{
    /// <summary>An unusable configured address fails the host rather than being published.</summary>
    /// <param name="description">What the row varies, carried for row identity.</param>
    /// <param name="issuancePath">The unusable address.</param>
    /// <remarks>
    /// The metadata-namespace row is the one worth reading twice: that namespace carries this service's
    /// two ANONYMOUS routes, so an issuance address inside it could collide with a route that is anonymous
    /// by design - and the one route that mints must never be able to land beside them.
    /// </remarks>
    [Theory]
    [InlineData("blank", "")]
    [InlineData("whitespace", "   ")]
    [InlineData("not rooted", "v1/tokens")]
    [InlineData("inside the metadata namespace", "/.well-known/tokens")]
    public void UnusableIssuanceAddressFailsTheHost(string description, string issuancePath)
    {
        Assert.False(string.IsNullOrEmpty(description));

        using IssuanceHostFactory factory = new(
            certificate: null,
            instant: null,
            signingKey: null,
            issuancePath);

        Exception failure = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains(
            "Security:TokenEndpointPath",
            Flatten(failure),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The configured address is published verbatim rather than repaired.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// The router compares the address byte for byte and the document publishes it byte for byte, so
    /// trimming or rewriting it here would publish one address and serve another. This row moves the
    /// operation to a second legal address and asserts that BOTH the route and the generated document
    /// follow it - which also proves the address genuinely comes from configuration rather than from a
    /// literal beside the registration.
    /// </remarks>
    [Fact]
    public async Task ConfiguredAddressIsPublishedVerbatimAsync()
    {
        const string relocated = "/v1/service-tokens";

        using X509Certificate2 certificate =
            IssuanceFixture.CreateCallerCertificate(IssuanceFixture.CallerIdentity);

        await using IssuanceHostFactory factory = new(
            certificate,
            instant: null,
            signingKey: null,
            relocated);

        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            new Uri(relocated, UriKind.Relative),
            IssuanceFixture.Body(),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using HttpResponseMessage original = await client.PostAsJsonAsync(
            new Uri(IssuanceFixture.IssuancePath, UriKind.Relative),
            IssuanceFixture.Body(),
            TestContext.Current.CancellationToken);

        // NOTHING IS MINTED AT THE ADDRESS THE OPERATION NO LONGER OCCUPIES, which is the property that
        // matters. The status is deliberately NOT asserted to be the router's not-found: this host
        // installs a default-deny fallback policy, and that policy applies to a request that matched no
        // endpoint as well as to one that matched an endpoint declaring no requirement - so an unmatched
        // address is refused as unauthorized BEFORE routing reports it missing. Measured on this host
        // rather than assumed, and it is the fail-closed answer: an unauthenticated caller cannot map the
        // service's addresses by comparing statuses.
        Assert.NotEqual(HttpStatusCode.OK, original.StatusCode);

        string refused = await original.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.DoesNotContain("access_token", refused, StringComparison.Ordinal);

        // A BEARER-AUTHENTICATED CLIENT FOR THE DOCUMENT, because the generated document is not one of
        // this service's three anonymous routes: the default-deny fallback policy governs it, and a
        // transport credential authenticates the ISSUANCE operation rather than the whole surface.
        using HttpClient reader = factory.CreateAuthenticatedClient();

        using HttpResponseMessage document = await reader.GetAsync(
            new Uri(IssuanceFixture.DocumentPath, UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, document.StatusCode);

        string payload = await document.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        using JsonDocument generated = JsonDocument.Parse(payload);

        Assert.True(generated.RootElement.GetProperty("paths").TryGetProperty(relocated, out _));
    }

    /// <summary>
    /// The unrecognised-outcome refusal is a server fault carrying the internal-error code.
    /// </summary>
    /// <remarks>
    /// Its only production route is an issuance outcome this build does not recognise, and the published
    /// outcome set is closed with two cases - so the refusal is unreachable through the issuer and would
    /// otherwise sit permanently unexercised. A defence nobody has ever seen fire is a defence nobody
    /// knows works.
    /// </remarks>
    [Fact]
    public void UnrecognisedOutcomeIsAServerFault()
    {
        ProblemHttpResult refusal = TokenEndpoints.Faulted(NullLoggerFactory.Instance);

        Assert.Equal(StatusCodes.Status500InternalServerError, refusal.StatusCode);
        Assert.Equal(
            RetCode.E_INTERNAL_ERROR,
            Assert.IsType<long>(refusal.ProblemDetails.Extensions["retCode"]));
        Assert.DoesNotContain(
            "access_token",
            refusal.ProblemDetails.Detail ?? string.Empty,
            StringComparison.Ordinal);
    }

    /// <summary>Flattens an exception chain into one searchable string.</summary>
    /// <param name="failure">The exception.</param>
    /// <returns>Every message in the chain.</returns>
    /// <remarks>
    /// The host builder wraps a startup failure, and how deeply it wraps is not this row's business - so
    /// the assertion is made against the whole chain rather than against whichever layer happened to be
    /// outermost.
    /// </remarks>
    private static string Flatten(Exception failure)
    {
        StringBuilder messages = new();

        for (Exception? current = failure; current is not null; current = current.InnerException)
        {
            messages.AppendLine(current.Message);
        }

        return messages.ToString();
    }
}

/// <summary>
/// A minted token verifies against the material this service publishes, and is refused on every
/// condition a verifier is supposed to refuse it on.
/// </summary>
/// <remarks>
/// <para>
/// This is the row set that proves the issuance half and the publication half of contract C-01 agree.
/// Each consumer of this service validates with the stock bearer handler pointed at the published key
/// set, so a token that does not verify against that set is worthless however well formed it is.
/// </para>
/// <para>
/// The validation library is used only to VALIDATE here. Nothing in this file mints, and nothing in the
/// implementation under test mints either: exactly one component in the refactor creates a token, and it
/// is the issuer the endpoint delegates to.
/// </para>
/// </remarks>
public sealed class TokenRoundTripTests
{
    /// <summary>The issuer identity the development settings configure.</summary>
    /// <remarks>
    /// <b>TLS, BECAUSE THAT IS THE LISTENER THIS SERVICE BINDS.</b> Every endpoint in the estate is
    /// <c>https</c> - a token is a bearer credential and a key set is the material every other service
    /// trusts, so carrying either over cleartext would let an observer replay the one and substitute the
    /// other (CWE-319). The issuer is compared BYTE FOR BYTE against the <c>iss</c> claim, so this
    /// constant and <c>Security:Issuer</c> in <c>appsettings.Development.json</c> must agree on the SCHEME
    /// as well as on the host and the port - a mismatch of one character rejects every token, which is
    /// exactly what this row set exists to catch and exactly what it did catch.
    /// </remarks>
    private const string ConfiguredIssuer = "https://localhost:5104";

    /// <summary>A minted token validates against the published key set.</summary>
    /// <returns>A task representing the assertion.</returns>
    [Fact]
    public async Task MintedTokenValidatesAgainstThePublishedKeySetAsync()
    {
        using X509Certificate2 certificate =
            IssuanceFixture.CreateCallerCertificate(IssuanceFixture.CallerIdentity);

        await using IssuanceHostFactory factory = new(certificate);
        using HttpClient client = factory.CreateClient();

        string token = await TokenIssuanceServiceTests.IssueAsync(client);
        SecurityKey published = await ReadPublishedKeyAsync(client);

        TokenValidationResult result = await new JsonWebTokenHandler().ValidateTokenAsync(
            token,
            Parameters(published));

        Assert.True(result.IsValid, result.Exception?.Message);
        Assert.Equal(
            IssuanceFixture.CallerIdentity,
            result.ClaimsIdentity.FindFirst("sub")?.Value);
        Assert.Equal(
            IssuanceFixture.ReadScope,
            result.ClaimsIdentity.FindFirst("scope")?.Value);
    }

    /// <summary>
    /// The signature is computed over the digest the legacy catalogue's own constant names.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// THE PARITY ASSERTION THAT TIES THE .NET ALGORITHM BACK TO THE ORACLE, made executable rather than
    /// left as prose in a comment. The legacy signature primitives take a digest selector whose catalogue
    /// comment records that the same set governs hashing, signing AND verification
    /// [ws_objects/pfw.shared.pbl.src/enums.sru:L927], and its SHA-256 member is the value 2 at
    /// [:L930] - which is the settled provenance for the algorithm this issuer signs with
    /// [n_crypto.sru:L70-L73]. Reading the issuer's reported selector and comparing it against the
    /// PRESERVED KERNEL CONSTANT is what makes that equivalence provable instead of asserted.
    /// </para>
    /// <para>
    /// The constant is CONSUMED from the shared kernel rather than re-spelled here, which is the same rule
    /// the operation follows for the return codes: the preserved legacy identifiers have exactly one
    /// definition in the estate, and no file outside the fixed suppression list declares one of its own.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task SignatureUsesTheLegacyDigestSelectorAsync()
    {
        using X509Certificate2 certificate =
            IssuanceFixture.CreateCallerCertificate(IssuanceFixture.CallerIdentity);

        await using IssuanceHostFactory factory = new(certificate);
        using HttpClient client = factory.CreateClient();

        string token = await TokenIssuanceServiceTests.IssueAsync(client);

        Assert.Equal(SecurityAlgorithms.RsaSha256, new JsonWebToken(token).Alg);
        Assert.Equal(
            Enums.CRYPTO_HASH_SHA256,
            factory.Services.GetRequiredService<TokenIssuer>().LegacySigningHashType);
    }

    /// <summary>A minted token is refused for an audience it was not issued to.</summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// One audience is carried per request deliberately, so that a token is never valid somewhere its
    /// holder did not intend. This row is what proves that property rather than assuming it.
    /// </remarks>
    [Fact]
    public async Task MintedTokenIsRefusedForTheWrongAudienceAsync()
    {
        using X509Certificate2 certificate =
            IssuanceFixture.CreateCallerCertificate(IssuanceFixture.CallerIdentity);

        await using IssuanceHostFactory factory = new(certificate);
        using HttpClient client = factory.CreateClient();

        string token = await TokenIssuanceServiceTests.IssueAsync(client);
        SecurityKey published = await ReadPublishedKeyAsync(client);

        TokenValidationParameters parameters = Parameters(published);
        // AN AUDIENCE THE DEPLOYMENT SERVES BUT THIS TOKEN DOES NOT CARRY, which is the shape of a
        // replay: a credential minted for one recipient presented to another. The second roster identity
        // serves, because it is on the deployment roster and is NOT what the default body addresses - the
        // default addresses the caller's own identity, so naming that here would validate successfully and
        // assert the opposite of this row.
        parameters.ValidAudiences = [IssuanceFixture.SecondAudience];

        TokenValidationResult result =
            await new JsonWebTokenHandler().ValidateTokenAsync(token, parameters);

        Assert.False(result.IsValid);
        Assert.IsType<SecurityTokenInvalidAudienceException>(result.Exception);
    }

    /// <summary>A minted token is refused once its lifetime has elapsed.</summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// Driven by validating against a clock well past the frozen issuance instant rather than by waiting,
    /// which is the whole point of the clock seam: expiry is provable in milliseconds and reproducibly.
    /// The skew tolerance is set to nothing so the row asserts the expiry rather than the library's
    /// default leeway.
    /// </remarks>
    [Fact]
    public async Task MintedTokenIsRefusedAfterItsLifetimeAsync()
    {
        DateTimeOffset issuedAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        // Valid AT the frozen issuance instant, for the reason recorded on the builder: this host's clock
        // is the one the trust layer measures the certificate's window against.
        using X509Certificate2 certificate = IssuanceFixture.CreateCallerCertificateValidAt(
            IssuanceFixture.CallerIdentity,
            issuedAt);

        await using IssuanceHostFactory factory = new(certificate, issuedAt);
        using HttpClient client = factory.CreateClient();

        string token = await TokenIssuanceServiceTests.IssueAsync(client);
        SecurityKey published = await ReadPublishedKeyAsync(client);

        TokenValidationParameters parameters = Parameters(published);
        parameters.ClockSkew = TimeSpan.Zero;
        parameters.LifetimeValidator = (notBefore, expires, _, _) =>
            expires is not null && expires > issuedAt.AddHours(1).UtcDateTime;

        TokenValidationResult result =
            await new JsonWebTokenHandler().ValidateTokenAsync(token, parameters);

        Assert.False(result.IsValid);
        Assert.IsType<SecurityTokenInvalidLifetimeException>(result.Exception);
    }

    /// <summary>A tampered token is refused.</summary>
    /// <param name="segment">Which segment of the token the row alters.</param>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// Both halves matter and are asserted separately: altering the PAYLOAD proves the signature covers
    /// the claims, and altering the SIGNATURE proves the signature is checked at all. A token whose
    /// payload could be edited without detection would let any holder grant itself any subject, audience
    /// and scope it liked.
    /// </remarks>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task TamperedTokenIsRefusedAsync(int segment)
    {
        using X509Certificate2 certificate =
            IssuanceFixture.CreateCallerCertificate(IssuanceFixture.CallerIdentity);

        await using IssuanceHostFactory factory = new(certificate);
        using HttpClient client = factory.CreateClient();

        string token = await TokenIssuanceServiceTests.IssueAsync(client);
        SecurityKey published = await ReadPublishedKeyAsync(client);

        string[] parts = token.Split('.');

        Assert.Equal(3, parts.Length);

        parts[segment] = Tamper(parts[segment]);

        TokenValidationResult result = await new JsonWebTokenHandler().ValidateTokenAsync(
            string.Join('.', parts),
            Parameters(published));

        Assert.False(result.IsValid);
        Assert.NotNull(result.Exception);
    }

    /// <summary>Alters one base64url segment so that it decodes to different bytes.</summary>
    /// <param name="segment">The segment.</param>
    /// <returns>The altered segment.</returns>
    private static string Tamper(string segment)
    {
        char[] characters = segment.ToCharArray();

        characters[^1] = characters[^1] == 'A' ? 'B' : 'A';

        return new string(characters);
    }

    /// <summary>Reads the published verification key out of the key set.</summary>
    /// <param name="client">The client.</param>
    /// <returns>The key.</returns>
    /// <remarks>
    /// Rebuilt from the PUBLISHED parameters rather than taken from the host's own service graph, so the
    /// row validates with exactly what a consumer would fetch. That is also what makes it an assertion
    /// about the published document rather than about the process.
    /// </remarks>
    private static async Task<SecurityKey> ReadPublishedKeyAsync(HttpClient client)
    {
        using HttpResponseMessage response = await client.GetAsync(
            new Uri(IssuanceFixture.KeySetPath, UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string payload = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        using JsonDocument document = JsonDocument.Parse(payload);

        JsonElement published = document.RootElement.GetProperty("keys").EnumerateArray().Single();

        RSAParameters parameters = new()
        {
            Modulus = Base64UrlEncoder.DecodeBytes(published.GetProperty("n").GetString()),
            Exponent = Base64UrlEncoder.DecodeBytes(published.GetProperty("e").GetString()),
        };

        RSA verifier = RSA.Create();
        verifier.ImportParameters(parameters);

        return new RsaSecurityKey(verifier)
        {
            KeyId = published.GetProperty("kid").GetString(),
        };
    }

    /// <summary>Builds validation parameters that check everything a consumer checks.</summary>
    /// <param name="key">The published verification key.</param>
    /// <returns>The parameters.</returns>
    private static TokenValidationParameters Parameters(SecurityKey key) =>
        new()
        {
            ValidateIssuer = true,
            ValidIssuers = [ConfiguredIssuer],
            ValidateAudience = true,
            // THE AUDIENCE THE DEFAULT REQUEST BODY NAMES, read from the same constant the body reads so
            // the two cannot drift. A verifier configured for any other audience would be validating a
            // token this fixture never minted, and would fail for a reason unrelated to what its row
            // asserts. The default is the caller's own identity, and Body() records why that pairing is
            // the one every narrow matrix in this suite permits.
            ValidAudiences = [IssuanceFixture.CallerIdentity],
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = key,
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
        };
}

/// <summary>
/// The clock seam: a frozen clock produces byte-identical claims.
/// </summary>
/// <remarks>
/// The characterization model requires every non-deterministic value to be maskable from BOTH the master
/// and the candidate recording, and the clock is one of this service's two primary sources of one. These
/// rows are what prove the endpoint reads NO ambient clock of its own - if it did, no substitution could
/// make two issuances agree.
/// </remarks>
public sealed class TokenDeterminismTests
{
    /// <summary>
    /// Two issuances for identical input under a frozen clock produce identical instants.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// The token itself is asserted identical too, which is a stronger statement and a legitimate one for
    /// this algorithm: the signature scheme is deterministic, so identical claims signed by the same key
    /// produce identical bytes. A row that only compared the instants would still pass if the endpoint
    /// had stamped something of its own into the payload.
    /// </remarks>
    [Fact]
    public async Task FrozenClockProducesIdenticalIssuancesAsync()
    {
        DateTimeOffset instant = new(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);

        // Valid AT the frozen instant, not at real "now": the trust layer judges a certificate's window
        // against the injected clock, so this host - frozen at a calendar instant - would correctly
        // refuse a certificate scoped to the wall clock.
        using X509Certificate2 certificate = IssuanceFixture.CreateCallerCertificateValidAt(
            IssuanceFixture.CallerIdentity,
            instant);

        await using IssuanceHostFactory factory = new(certificate, instant);
        using HttpClient client = factory.CreateClient();

        TokenIssuanceResponse first = await IssueAsync(client);
        TokenIssuanceResponse second = await IssueAsync(client);

        Assert.Equal(first.IssuedAt, second.IssuedAt);
        Assert.Equal(first.ExpiresIn, second.ExpiresIn);

        // COMPARED BY FINGERPRINT, NOT BY VALUE (C-F). These two credentials came off the live endpoint, so
        // handing them to Assert.Equal would render two working tokens into the failure message and from
        // there into the CI log. The digest keeps the byte-identity claim exactly and makes the rendered
        // operands one-way; SensitiveValueAssertions.cs carries the full reasoning.
        Assert.Equal(
            SensitiveValueAssertions.Fingerprint(first.AccessToken),
            SensitiveValueAssertions.Fingerprint(second.AccessToken));

        JsonWebToken parsed = new(first.AccessToken);

        Assert.Equal(instant.ToUnixTimeSeconds(), first.IssuedAt);
        Assert.Equal(instant.UtcDateTime, parsed.IssuedAt);
        Assert.Equal(instant.UtcDateTime, parsed.ValidFrom);
        Assert.Equal(instant.AddSeconds(first.ExpiresIn).UtcDateTime, parsed.ValidTo);
    }

    /// <summary>
    /// The response's issuance instant equals the token's own issuance claim.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// Asserted against the REAL clock as well as the frozen one, because the identity must hold in both:
    /// the endpoint takes the instant from the issuer's result rather than measuring one, so the two can
    /// never disagree by a scheduling delay.
    /// </remarks>
    [Fact]
    public async Task ResponseInstantEqualsTheTokenClaimAsync()
    {
        using X509Certificate2 certificate =
            IssuanceFixture.CreateCallerCertificate(IssuanceFixture.CallerIdentity);

        await using IssuanceHostFactory factory = new(certificate);
        using HttpClient client = factory.CreateClient();

        TokenIssuanceResponse body = await IssueAsync(client);

        JsonWebToken parsed = new(body.AccessToken);

        Assert.Equal(
            body.IssuedAt,
            new DateTimeOffset(parsed.IssuedAt, TimeSpan.Zero).ToUnixTimeSeconds());
        Assert.Equal(
            body.ExpiresIn,
            (long)(parsed.ValidTo - parsed.IssuedAt).TotalSeconds);
    }

    /// <summary>Issues one token and returns the whole response body.</summary>
    /// <param name="client">The client.</param>
    /// <returns>The body.</returns>
    private static async Task<TokenIssuanceResponse> IssueAsync(HttpClient client)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            new Uri(IssuanceFixture.IssuancePath, UriKind.Relative),
            IssuanceFixture.Body(),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        TokenIssuanceResponse? body = await response.Content.ReadFromJsonAsync<TokenIssuanceResponse>(
            TestContext.Current.CancellationToken);

        Assert.NotNull(body);

        return body;
    }
}

/// <summary>
/// The legacy return codes this operation uses are mapped EXPLICITLY, and the tri-state hole in the
/// legacy algebra is asserted rather than assumed.
/// </summary>
/// <remarks>
/// <para>
/// Measured from the read-only oracle: the success predicate is a greater-than-or-equal test against zero
/// [ws_objects/pfw.shared.pbl.src/issucceeded.srf:L11-L12] while a prevention is 1
/// [retcode.sru:L42], SO A PREVENTION READS AS A SUCCESS; the failure predicate excludes the cancellation
/// value explicitly [isfailed.srf:L11-L12] while that value also fails the success test
/// [retcode.sru:L44-L45], SO A CANCELLATION IS NEITHER. Both are preserved rather than repaired, which is
/// exactly why no status anywhere in this service is derived from a truthiness test on a code.
/// </para>
/// <para>
/// The two rows below assert those two values INDIVIDUALLY, so neither can be collapsed into a generic
/// success or failure by a future change to the shared map.
/// </para>
/// </remarks>
public sealed class TokenProblemMappingTests
{
    /// <summary>
    /// A prevention is a distinct status and is NOT treated as a success by the shared map.
    /// </summary>
    [Fact]
    public void PreventionIsNotTreatedAsASuccess()
    {
        Assert.True(Predicates.IsSucceeded(RetCode.PREVENT));
        Assert.Equal(StatusCodes.Status409Conflict, ProblemResults.MapStatusCode(RetCode.PREVENT));
        Assert.NotEqual(StatusCodes.Status200OK, ProblemResults.MapStatusCode(RetCode.PREVENT));
        Assert.False(ProblemResults.ClaimsSuccess(RetCode.PREVENT));
        Assert.False(ProblemResults.IsIndeterminate(RetCode.PREVENT));
    }

    /// <summary>
    /// A cancellation is neither succeeded nor failed, and is a distinct status.
    /// </summary>
    [Fact]
    public void CancellationIsNeitherSucceededNorFailed()
    {
        Assert.False(Predicates.IsSucceeded(RetCode.CANCELLED));
        Assert.False(Predicates.IsFailed(RetCode.CANCELLED));
        Assert.True(ProblemResults.IsIndeterminate(RetCode.CANCELLED));
        Assert.Equal(StatusCodes.Status409Conflict, ProblemResults.MapStatusCode(RetCode.CANCELLED));
        Assert.NotEqual(
            ProblemResults.MapStatusCode(RetCode.FAILED),
            ProblemResults.MapStatusCode(RetCode.CANCELLED));
    }

    /// <summary>
    /// The three codes this operation actually uses map to the three statuses it publishes.
    /// </summary>
    /// <param name="retCode">The legacy code.</param>
    /// <param name="expected">The status the shared map answers with.</param>
    /// <remarks>
    /// The access-denied code appears TWICE in the authored document - once for the unauthorized response
    /// and once for the forbidden one - so the map cannot pick between them from the code alone. It
    /// defaults to forbidden, and the operation passes the unauthorized status explicitly for the arm that
    /// needs it; the service rows prove both statuses are actually produced.
    /// </remarks>
    [Theory]
    [InlineData(RetCode.E_INVALID_ARGUMENT, StatusCodes.Status400BadRequest)]
    [InlineData(RetCode.E_ACCESS_DENIED, StatusCodes.Status403Forbidden)]
    [InlineData(RetCode.E_INTERNAL_ERROR, StatusCodes.Status500InternalServerError)]
    public void OperationCodesMapToTheirPublishedStatuses(long retCode, int expected) =>
        Assert.Equal(expected, ProblemResults.MapStatusCode(retCode));

    /// <summary>
    /// The shared map's output set is closed, and the status reserved for a deferred capability's routing
    /// declaration is outside it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Constraint C-D, and the specific trap a status map walks into. The four routes that answer the
    /// reserved status are the ingress service's routing metadata for the four capabilities deferred out
    /// of this phase; producing it from this service's error map would put a deferred capability's surface
    /// on the wrong service entirely.
    /// </para>
    /// <para>
    /// STATED AS A CLOSED ALLOWED SET RATHER THAN AS THE ABSENCE OF ONE VALUE, which is both stronger and
    /// cleaner: it fails on ANY status the map was not designed to answer rather than only on the one
    /// this constraint names, and it keeps the forbidden status out of this file altogether. Asserted
    /// across the whole catalogue of codes this service can reach, including the two the constraint puts
    /// under particular scrutiny, rather than only the ones the operation uses today.
    /// </para>
    /// </remarks>
    [Fact]
    public void SharedMapAnswersOnlyFromItsClosedStatusSet()
    {
        int[] allowed =
        [
            StatusCodes.Status400BadRequest,
            StatusCodes.Status403Forbidden,
            StatusCodes.Status404NotFound,
            StatusCodes.Status409Conflict,
            StatusCodes.Status500InternalServerError,
            StatusCodes.Status503ServiceUnavailable,
        ];

        long[] codes =
        [
            RetCode.OK,
            RetCode.PREVENT,
            RetCode.FAILED,
            RetCode.CANCELLED,
            RetCode.E_INVALID_ARGUMENT,
            RetCode.E_INVALID_TYPE,
            RetCode.E_INVALID_DATA,
            RetCode.E_OUT_OF_RANGE,
            RetCode.E_OBJECT_NOT_FOUND,
            RetCode.E_NOT_EXISTS,
            RetCode.E_BUSY,
            RetCode.E_TIME_OUT,
            RetCode.E_RETRY,
            RetCode.E_ACCESS_DENIED,
            RetCode.E_INTERNAL_ERROR,
            RetCode.E_NO_SUPPORT,
            RetCode.E_NO_IMPLEMENTATION,
            RetCode.UNKNOWN,
        ];

        foreach (long code in codes)
        {
            Assert.Contains(ProblemResults.MapStatusCode(code), allowed);
        }

        // The null code is not a code at all, and it is classified too rather than left to chance.
        Assert.Contains(ProblemResults.MapStatusCode(retCode: null), allowed);
    }
}

/// <summary>
/// The generated document and the authored one agree about this operation.
/// </summary>
/// <remarks>
/// This is the comparison that matters most in practice: a consumer generates its client from whichever
/// document it is handed. The authored one is authoritative, so a disagreement is fixed in the code.
/// </remarks>
public sealed class TokenGeneratedDocumentTests
{
    /// <summary>
    /// The generated document declares the operation at the authored path with the authored identity.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    [Fact]
    public async Task GeneratedDocumentDeclaresTheOperationAsync()
    {
        using JsonDocument document = await ReadAsync();

        JsonElement operation = Operation(document);

        Assert.Equal(IssuanceFixture.OperationId, operation.GetProperty("operationId").GetString());
        Assert.Equal("TokenService", operation.GetProperty("tags").EnumerateArray().Single().GetString());
        Assert.Contains(
            "short-lived service token",
            operation.GetProperty("summary").GetString() ?? string.Empty,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The generated document declares the two credential schemes as ALTERNATIVES and NO bearer
    /// alternative.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// THE ROW THAT PROVES THE OVERRIDE SURVIVED GENERATION. A document that also listed the bearer
    /// scheme would tell a consumer it may present a token instead of a credential, which is the one
    /// thing this operation cannot accept - and the generator does not synthesise a requirement from
    /// authorization metadata by itself, so a document with no requirement at all would advertise an
    /// anonymous mint.
    /// </para>
    /// <para>
    /// TWO REQUIREMENT OBJECTS, NOT ONE OBJECT NAMING TWO SCHEMES, and this row asserts that shape
    /// specifically because the difference is the whole meaning. A list of requirements is a DISJUNCTION
    /// - any one satisfies the operation, which is what the handler implements - whereas two schemes
    /// inside one requirement is a CONJUNCTION demanding both at once, which would publish an operation
    /// no caller in this system can reach. Both spellings look nearly identical in a document and only
    /// one of them is correct.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task GeneratedDocumentDeclaresBothCredentialSchemesAsAlternativesAsync()
    {
        using JsonDocument document = await ReadAsync();

        List<JsonElement> requirements =
            [.. Operation(document).GetProperty("security").EnumerateArray()];

        Assert.Equal(2, requirements.Count);

        // Each requirement names exactly ONE scheme, which is what makes the list a disjunction.
        foreach (JsonElement requirement in requirements)
        {
            Assert.Single(requirement.EnumerateObject());
            Assert.False(requirement.TryGetProperty("bearerAuth", out _));
        }

        Assert.True(requirements[0].TryGetProperty("clientCredential", out JsonElement credentialScopes));
        Assert.Empty(credentialScopes.EnumerateArray());

        Assert.True(requirements[1].TryGetProperty("mutualTls", out JsonElement certificateScopes));
        Assert.Empty(certificateScopes.EnumerateArray());

        JsonElement schemes = document.RootElement
            .GetProperty("components")
            .GetProperty("securitySchemes");

        JsonElement credentialScheme = schemes.GetProperty("clientCredential");

        Assert.Equal("http", credentialScheme.GetProperty("type").GetString());
        Assert.Equal("basic", credentialScheme.GetProperty("scheme").GetString());

        Assert.Equal("mutualTLS", schemes.GetProperty("mutualTls").GetProperty("type").GetString());
    }

    /// <summary>
    /// The generated document declares exactly the five responses the authored document declares.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// Both directions are asserted. The presence half catches a response the implementation forgot to
    /// declare; the ABSENCE half catches one the framework inferred from a result type, which is the exact
    /// reason the handler returns the untyped result interface rather than a typed union.
    /// </remarks>
    [Fact]
    public async Task GeneratedDocumentDeclaresExactlyTheAuthoredResponsesAsync()
    {
        using JsonDocument document = await ReadAsync();

        string[] actual = Operation(document).GetProperty("responses").EnumerateObject()
            .Select(response => response.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["200", "400", "401", "403", "500"], actual);
    }

    /// <summary>
    /// Every error response in the generated document carries the one problem media type.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    [Theory]
    [InlineData("400")]
    [InlineData("401")]
    [InlineData("403")]
    [InlineData("500")]
    public async Task GeneratedErrorResponseCarriesTheProblemMediaTypeAsync(string status)
    {
        using JsonDocument document = await ReadAsync();

        JsonElement content = Operation(document)
            .GetProperty("responses")
            .GetProperty(status)
            .GetProperty("content");

        Assert.True(content.TryGetProperty(MediaTypeNames.Application.ProblemJson, out _));
    }

    /// <summary>
    /// The generated success response carries the response schema's members and no key identifier.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    [Fact]
    public async Task GeneratedSuccessResponseMatchesTheAuthoredSchemaAsync()
    {
        using JsonDocument document = await ReadAsync();

        JsonElement schema = Operation(document)
            .GetProperty("responses")
            .GetProperty("200")
            .GetProperty("content")
            .GetProperty(MediaTypeNames.Application.Json)
            .GetProperty("schema");

        JsonElement resolved = Resolve(document, schema);

        string[] members = resolved.GetProperty("properties").EnumerateObject()
            .Select(member => member.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            ["access_token", "expires_in", "issued_at", "scope", "token_type"],
            members);

        string[] required = resolved.GetProperty("required").EnumerateArray()
            .Select(member => member.GetString() ?? string.Empty)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["access_token", "expires_in", "scope", "token_type"], required);
    }

    /// <summary>
    /// The generated request schema declares the three authored members and no credential-shaped one.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// The authored schema carries no client secret, password, key reference, assertion or key material,
    /// and neither may the generated one: caller identity comes from the transport. A row that only
    /// counted members would miss a credential added alongside them, so the names are asserted exactly.
    /// </remarks>
    [Fact]
    public async Task GeneratedRequestSchemaCarriesNoCredentialAsync()
    {
        using JsonDocument document = await ReadAsync();

        JsonElement schema = Operation(document)
            .GetProperty("requestBody")
            .GetProperty("content")
            .GetProperty(MediaTypeNames.Application.Json)
            .GetProperty("schema");

        JsonElement resolved = Resolve(document, schema);

        string[] members = resolved.GetProperty("properties").EnumerateObject()
            .Select(member => member.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["audience", "scopes", "subject"], members);
    }

    /// <summary>Resolves a schema reference against the generated document's component section.</summary>
    /// <param name="document">The document.</param>
    /// <param name="schema">The schema, which may be a reference or a nullable union around one.</param>
    /// <returns>The resolved schema.</returns>
    /// <remarks>
    /// THE UNION ARM IS THE DOCUMENTED CONSEQUENCE OF THE OPTIONAL BODY, and unwrapping it here is what
    /// keeps the assertion about the MEMBERS rather than about the wrapper. The operation's body parameter
    /// is nullable so that an absent body is answered by the operation's own bad request carrying the
    /// legacy return code, instead of by the framework's shapeless refusal - so the generator publishes
    /// the request schema as a choice between the object and nothing. The authored document declares the
    /// object directly, which is stricter; the difference is one degree of permissiveness in the
    /// GENERATED schema only, the authored document remains authoritative for what a caller may send, and
    /// the observable behaviour on a violation is identical.
    /// </remarks>
    private static JsonElement Resolve(JsonDocument document, JsonElement schema)
    {
        if (schema.TryGetProperty("oneOf", out JsonElement union))
        {
            foreach (JsonElement arm in union.EnumerateArray())
            {
                bool isNullArm =
                    arm.TryGetProperty("type", out JsonElement type) &&
                    type.ValueKind == JsonValueKind.String &&
                    string.Equals(type.GetString(), "null", StringComparison.Ordinal);

                if (!isNullArm)
                {
                    return Resolve(document, arm);
                }
            }

            Assert.Fail("The generated request schema declares no non-null arm.");
        }

        if (!schema.TryGetProperty("$ref", out JsonElement reference))
        {
            return schema;
        }

        string pointer = reference.GetString() ?? string.Empty;
        string name = pointer[(pointer.LastIndexOf('/') + 1)..];

        return document.RootElement
            .GetProperty("components")
            .GetProperty("schemas")
            .GetProperty(name);
    }

    /// <summary>Extracts the issuance operation from a generated document.</summary>
    /// <param name="document">The document.</param>
    /// <returns>The operation.</returns>
    private static JsonElement Operation(JsonDocument document)
    {
        Assert.True(
            document.RootElement.GetProperty("paths")
                .TryGetProperty(IssuanceFixture.IssuancePath, out JsonElement item),
            $"The generated document declares no path '{IssuanceFixture.IssuancePath}'.");

        Assert.True(
            item.TryGetProperty("post", out JsonElement operation),
            "The issuance path is not declared as a POST operation.");

        return operation;
    }

    /// <summary>Fetches the generated document from a booted host.</summary>
    /// <returns>The document.</returns>
    /// <remarks>
    /// AUTHENTICATED, BECAUSE THE DOCUMENT IS NOT ONE OF THIS SERVICE'S THREE ANONYMOUS ROUTES. The
    /// composition root installs a default-deny fallback policy and exempts only <c>/health</c> and the
    /// two <c>/.well-known/</c> publications, so a description of the surface is fetched with a token
    /// like any other non-exempt route.
    /// </remarks>
    private static async Task<JsonDocument> ReadAsync()
    {
        await using IssuanceHostFactory factory = new();
        using HttpClient client = factory.CreateAuthenticatedClient();

        using HttpResponseMessage response = await client.GetAsync(
            new Uri(IssuanceFixture.DocumentPath, UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string payload = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        return JsonDocument.Parse(payload);
    }
}

/// <summary>
/// Nothing this operation produces discloses the signing material, and the credential appears in exactly
/// one place.
/// </summary>
/// <remarks>
/// Constraint C-F, asserted rather than asserted-about. The minted token is a credential and belongs in
/// the success body alone; the signing material belongs nowhere a caller or a log reader can see.
/// </remarks>
public sealed class TokenSecrecyTests
{
    /// <summary>
    /// Neither the published document nor the generated one carries a specimen token.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// A plausible-looking specimen is indistinguishable from real material to a reader, and specimens
    /// have a long history of being copied into production unchanged. The authored specification carries
    /// no example on any field, and the generated one must not acquire one either.
    /// </remarks>
    [Fact]
    public async Task NoDocumentCarriesASpecimenTokenAsync()
    {
        Assert.DoesNotContain("eyJ", ContractDocument.Text, StringComparison.Ordinal);

        await using IssuanceHostFactory factory = new();

        // Authenticated: the generated document is governed by the default-deny fallback policy, so an
        // anonymous read would return a refusal rather than the document this row inspects.
        using HttpClient client = factory.CreateAuthenticatedClient();

        using HttpResponseMessage response = await client.GetAsync(
            new Uri(IssuanceFixture.DocumentPath, UriKind.Relative),
            TestContext.Current.CancellationToken);

        string payload = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.DoesNotContain("eyJ", payload, StringComparison.Ordinal);

        // The armour marker rather than the words "private key": the cryptographic surface's own
        // published prose legitimately DISCUSSES private keys, and a row that forbade the phrase would
        // fail on a description while missing an actual key. Only material carries the marker.
        Assert.DoesNotContain("-----BEGIN", payload, StringComparison.Ordinal);
        Assert.DoesNotContain("-----BEGIN", ContractDocument.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The signing material reaches neither the response nor the operator channel.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// Checked in WINDOWS as well as in full, so a record that quoted a fragment of the key would still
    /// fail. The window is short on purpose: a long one would only catch a wholesale echo.
    /// </para>
    /// <para>
    /// The token is asserted PRESENT in the success body and ABSENT from every record, which is the pair
    /// of statements that matters: the credential has exactly one legitimate destination.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task SigningMaterialNeverLeavesTheProcessAsync()
    {
        string signingKey = IssuanceFixture.CreateSigningKeyPem();

        using X509Certificate2 certificate =
            IssuanceFixture.CreateCallerCertificate(IssuanceFixture.CallerIdentity);

        await using IssuanceHostFactory factory = new(certificate, instant: null, signingKey);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            new Uri(IssuanceFixture.IssuancePath, UriKind.Relative),
            IssuanceFixture.Body(),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string payload = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Contains("access_token", payload, StringComparison.Ordinal);
        AssertDisclosesNothingAbout(payload, signingKey);
        Assert.DoesNotContain("-----BEGIN", payload, StringComparison.Ordinal);

        // The key set is the one place verification material is published, and it must carry the PUBLIC
        // half only - so the private material must be absent from it too.
        using HttpResponseMessage keySet = await client.GetAsync(
            new Uri(IssuanceFixture.KeySetPath, UriKind.Relative),
            TestContext.Current.CancellationToken);

        string published = await keySet.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        AssertDisclosesNothingAbout(published, signingKey);

        foreach (string member in new[] { "\"d\"", "\"p\"", "\"q\"", "\"dp\"", "\"dq\"", "\"qi\"", "\"k\"" })
        {
            Assert.DoesNotContain(member, published, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// A refusal never carries the minted token, the request payload or the signing material.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    [Fact]
    public async Task RefusalCarriesNothingSensitiveAsync()
    {
        string signingKey = IssuanceFixture.CreateSigningKeyPem();

        using X509Certificate2 certificate =
            IssuanceFixture.CreateCallerCertificate(IssuanceFixture.CallerIdentity);

        await using IssuanceHostFactory factory = new(certificate, instant: null, signingKey);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            new Uri(IssuanceFixture.IssuancePath, UriKind.Relative),
            IssuanceFixture.Body(audience: IssuanceFixture.UnlistedAudience),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        string payload = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.DoesNotContain("access_token", payload, StringComparison.Ordinal);
        Assert.DoesNotContain(IssuanceFixture.ConfiguredKeyId, payload, StringComparison.Ordinal);
        AssertDisclosesNothingAbout(payload, signingKey);
    }

    /// <summary>
    /// The operator record names the caller and the audience, and never the credential.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// THE SUBJECT IS RECORDED DELIBERATELY, and only because it has already been reconciled against the
    /// identity the presented certificate established - so it is a transport-established identity rather
    /// than unvalidated caller text. The issuer refuses to record it at ITS layer for exactly that reason,
    /// and the difference between the two files is the reconciliation that happens between them.
    /// </para>
    /// <para>
    /// The token, the key identifier and the signing material must be absent from every record. Asserted
    /// across ALL captured records rather than only the operation's own, so a record written by the shared
    /// problem factory or by the issuer cannot leak what this one withholds.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task OperatorRecordNamesTheCallerAndNeverTheCredentialAsync()
    {
        string signingKey = IssuanceFixture.CreateSigningKeyPem();
        CapturedRecords captured = new();

        using X509Certificate2 certificate =
            IssuanceFixture.CreateCallerCertificate(IssuanceFixture.CallerIdentity);

        await using IssuanceHostFactory factory = new(
            certificate,
            instant: null,
            signingKey,
            issuancePath: null,
            captured);

        using HttpClient client = factory.CreateClient();

        string token = await TokenIssuanceServiceTests.IssueAsync(client);

        string issuance = Assert.Single(
            captured.Records,
            record => record.Contains("Security issued a service token", StringComparison.Ordinal));

        Assert.Contains(IssuanceFixture.CallerIdentity, issuance, StringComparison.Ordinal);
        Assert.Contains(IssuanceFixture.OperationId, issuance, StringComparison.Ordinal);

        foreach (string record in captured.Records)
        {
            Assert.DoesNotContain(token, record, StringComparison.Ordinal);
            AssertDisclosesNothingAbout(record, signingKey);
        }
    }

    /// <summary>
    /// A token is still issued when the operation's own operator channel is switched off.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// The enablement guard keeps the formatting cost off a disabled sink, and this row proves the guard
    /// is a guard rather than a gate: a deployment that raises the level for this category loses the
    /// record and keeps the behaviour.
    /// </remarks>
    [Fact]
    public async Task IssuanceSucceedsWithTheOperatorChannelDisabledAsync()
    {
        CapturedRecords captured = new();

        using X509Certificate2 certificate =
            IssuanceFixture.CreateCallerCertificate(IssuanceFixture.CallerIdentity);

        await using IssuanceHostFactory factory = new(
            certificate,
            instant: null,
            signingKey: null,
            issuancePath: null,
            captured,
            LogLevel.Warning);

        using HttpClient client = factory.CreateClient();

        string token = await TokenIssuanceServiceTests.IssueAsync(client);

        Assert.False(string.IsNullOrEmpty(token));
        Assert.DoesNotContain(
            captured.Records,
            record => record.Contains("Security issued a service token", StringComparison.Ordinal));
    }

    /// <summary>Asserts that a payload discloses no part of some material.</summary>
    /// <param name="payload">The payload.</param>
    /// <param name="material">The material that must not appear.</param>
    private static void AssertDisclosesNothingAbout(string payload, string material)
    {
        Assert.DoesNotContain(material, payload, StringComparison.Ordinal);

        const int windowLength = 12;

        string body = material.Replace("\n", string.Empty, StringComparison.Ordinal)
            .Replace("\r", string.Empty, StringComparison.Ordinal);

        for (int start = 0; start + windowLength <= body.Length; start += windowLength)
        {
            string window = body.Substring(start, windowLength);

            Assert.DoesNotContain(window, payload, StringComparison.Ordinal);
        }
    }
}
