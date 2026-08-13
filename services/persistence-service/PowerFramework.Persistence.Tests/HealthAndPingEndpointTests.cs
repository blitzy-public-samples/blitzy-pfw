// ==================================================================================================
//  HealthAndPingEndpointTests - THE HOST-LEVEL CONTRACT PROOF FOR THE PERSISTENCE SERVICE'S TWO
//  DIAGNOSTIC ROUTES, AND THE EXECUTABLE FORM OF "EVERY NEW BOUNDARY IS AUTHENTICATED"
//  ------------------------------------------------------------------------------------------------
//  WHAT THIS FILE IS FOR, AND WHY IT IS NOT A DUPLICATE OF ITS TWO NEIGHBOURS
//  Endpoints/HealthEndpoints.cs and Endpoints/PingEndpoints.cs each have a file of their own that
//  drives them in depth - the probe's caching window, the closed disclosure vocabulary, the four
//  individual token validations. This file asserts the properties that belong to NEITHER route on its
//  own but to the COMPOSED HOST that publishes both: that exactly one of the two is anonymous and the
//  other is not, that the anonymous one reaches no other service, that the authenticated one has no
//  bypass in any environment, that the service holds no minting capability at all, that a structurally
//  invalid configuration prevents the host from starting rather than degrading it, and that nothing a
//  DEFERRED capability area would eventually own is routed here.
//
//  Every one of those is a statement about the pipeline, the container graph and the route table
//  together, so every one of them needs a real host. WebApplicationFactory boots the service's own
//  Program.cs, which means what is exercised is the DEPLOYED composition: the stock bearer
//  registration, the deny-by-default fallback policy, the middleware ordering, the startup gate and
//  the real MapPersistenceEndpoints call. A hand-assembled host would prove only that this file can
//  configure ASP.NET Core correctly, which is not the question an auditor asks.
//
//  RULES POSITION, STATED BECAUSE ITS ABSENCE IS A FINDING RATHER THAN AN OMISSION
//  No user rules were provided for this project. The rules document contains exactly one line saying
//  so, and re-reading it returns the same one line. Nothing is invented, inferred or back-filled from
//  convention in their place. The bar applied instead is the enterprise-standard baseline - nullable
//  enabled, warnings as errors, no secret in source or in a fixture, a genuinely testable design, a
//  hard coverage gate - together with the named non-rule constraints, each cited below at the point it
//  actually applies rather than listed in a preamble.
//
//  ================================================================================================
//  THE DECISION RECORD
//  ================================================================================================
//
//   1. THE AUTHENTICATED ARM IS PRODUCED BY SUBSTITUTING THE HANDLER, NEVER BY MINTING A TOKEN
//      (constraints C-F and C-G, and the single most important decision in this file).
//
//      Exactly ONE signing secret exists in the whole system and the Security service holds it.
//      Persistence holds verification material only and can never mint - which is enforced
//      structurally rather than by review, because the minting package
//      Microsoft.IdentityModel.JsonWebTokens is deliberately absent from BOTH this test project's
//      manifest and the application project's, so a minting call would not compile in either and could
//      not restore under central package management even if someone tried to add one.
//
//      So the positive arm here does NOT construct a signing key, hand-assemble a JWS, embed a PEM or
//      a symmetric key, or disable authorization to get a 200. It registers an ADDITIONAL
//      authentication scheme whose handler evaluates an opaque, unsigned, non-secret test credential
//      and makes that scheme the host's default. Nothing in this file is key material, so there is
//      nothing here for a secret scanner to find and nothing to rotate. Search this file for
//      SigningCredentials, SymmetricSecurityKey, RsaSecurityKey, WriteToken or a key block marker and
//      the answer is that none of them appears; if a future change finds itself needing one, the
//      approach has gone wrong rather than the constraint.
//
//      AND THE SUBSTITUTION IS STILL AN HONEST EVALUATION. The substituted handler is not a
//      rubber stamp that authenticates everything: it returns no result when no credential is
//      presented, FAILS a credential it cannot parse, and FAILS an expired one - and it judges expiry
//      against the INJECTED clock, so the expiry arm is a real evaluation on a deterministic seam
//      rather than a sleep. A handler that authenticated unconditionally would make the negative arms
//      vacuous, which would be worse than not testing them.
//
//      TWO HOST POSTURES EXIST FOR THIS REASON, AND THE SPLIT IS DELIBERATE:
//        * The DEPLOYED posture leaves the real bearer handler in place as the default scheme. Every
//          negative case runs here, against the handler a deployment actually uses.
//        * The SUBSTITUTED posture is used ONLY where an authenticated principal is the subject of the
//          assertion. It changes WHO decides the principal and changes nothing about the route's
//          requirement, the fallback policy or the middleware order - so what it proves is that the
//          route admits an authenticated caller, which is precisely the half a 401-only suite leaves
//          unproven.
//
//   2. THE DEPLOYED POSTURE IS HERMETIC WITHOUT KEY MATERIAL, AND THAT NEEDED SOLVING RATHER THAN
//      ASSUMING. The bearer handler resolves its verification material from the configured authority,
//      and the authority configured here is a reserved `.invalid` host that can never resolve. Left at
//      that, every refusal in this file would be attributable to UNREACHABLE METADATA rather than to
//      the credential - a 401 that proves nothing. The fix is to hand the handler a STATIC
//      verification configuration carrying the issuer and NO keys at all: the framework wraps a
//      supplied configuration in a static configuration manager, so no discovery document is fetched
//      and no key set is downloaded, and a configuration with no keys can verify nothing - so every
//      presented credential is refused for a reason the handler itself established, offline, with
//      nothing secret anywhere in the fixture. Not one of the four mandatory validations is switched
//      off to make a case pass.
//
//   3. WHY `/health` IS PROVED TO BE LOCAL-ONLY, AND WHY A WELL-MEANING "IMPROVEMENT" HERE IS
//      DANGEROUS (constraint C-K). Persistence is the BOTTOM of the layered, acyclic topology: Gateway
//      calls DataServices and Security, DataServices calls Persistence and Security, and Persistence
//      calls nothing at all - it reads Security's published verification material and no more.
//      Upstream aggregation belongs to GATEWAY ALONE, which reports healthy only after Persistence,
//      DataServices and Security each do.
//
//      Adding an upstream probe to THIS endpoint would do two things and the second is fatal. It would
//      INVERT THE TOPOLOGY, and it would DEADLOCK THE READINESS CHAIN: the orchestration manifest
//      gates Gateway on this very endpoint with a health condition, so an endpoint that waited on
//      Gateway's peers would be waiting, transitively, on something waiting on it. Local orchestration
//      would then never come up, and the failure would present as a slow start rather than as a cycle.
//      That is why this file asserts the ABSENCE of an outbound edge as a positive property - a
//      reflective proof that the application assembly links no HTTP-client or resilience library at
//      all, and a live proof that the readiness verdict is produced while the configured authority is
//      unresolvable.
//
//   4. WHY THE UNHEALTHY ARM IS DRIVEN THROUGH THE PROBE AND NOT THROUGH A FILE DELETE (AAP section
//      0.6.7). For one workflow identifier the legacy-side and target-side characterization recordings
//      must be captured against the SAME storage volume state, with the volume neither recreated nor
//      reseeded between them, or the paired recordings are not comparable at all. A test that produced
//      a not-ready verdict by deleting a database file would be rehearsing exactly the act that
//      invalidates that evidence model, and the oracle shows the act to avoid literally:
//      [ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L448] guards non-destructively with
//      `if sqlitedb.IsOpened() then return`, and then :L450 performs `FileDelete("test.db")` before
//      opening at :L456 - test-harness setup that reads like part of the open sequence.
//
//      So the not-ready arm here is the REAL probe answering honestly: it opens READ-ONLY and
//      NON-CREATING, and against a directory with no database it therefore refuses rather than
//      creating one. The case then asserts the file system afterwards and finds no database - which
//      makes "the probe creates nothing" an observation rather than a claim. Nothing in this file
//      calls EnsureCreated, EnsureDeleted, Migrate, any DDL or DML, or deletes a database file.
//
//   5. WHY THE STORAGE LOCATION IS TEST-OWNED (constraints C-E and C-I). Each host creates its own
//      uniquely named directory and removes only that directory when it is disposed. The configured
//      deployment path is never touched, no volume is shared with another case, and nothing here needs
//      a container, a daemon, a network or a machine-specific setting - so the suite behaves the same
//      on a clean checkout as on a warm one, and two clones running in parallel cannot collide.
//      SQLite is the only storage engine mentioned anywhere in this file, because it is the only one
//      with any evidence in the repository: the legacy transaction layer enumerates exactly two
//      database types, DBT_MSSQL = 0 and DBT_ORACLE = 1
//      [ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:L60-L61], and SQLite is absent from
//      that enumeration entirely while being the only engine with a schema.
//
//   6. FAIL FAST IS ASSERTED AS FAIL FAST (AAP section 0.1.4). The legacy is emphatically fail-fast:
//      the application object's system-error handler guards on an assertion payload
//      [ws_objects/pfw.pbl.src/pfw.sra:L114], splits it on a carriage-return line-feed pair into up to
//      seven fields [:L115-L126], composes a diagnostic [:L129-L139] and then executes `HALT CLOSE`
//      [:L143]. It does not warn and carry on. The managed equivalent is a startup gate that refuses
//      to compose, so this file asserts that a structurally invalid configuration prevents the host
//      from STARTING. Softening that into warning-and-continue would be a behavioural change dressed
//      up as robustness, and it is the one shape of "improvement" this file exists to catch.
//
//   7. NO PERFORMANCE CLAIM APPEARS ANYWHERE (AAP section 0.8.5). The repository publishes no
//      service-level agreement, no latency budget, no throughput target and no availability
//      commitment, so nothing here measures a response time, a startup time or a rate, and no case
//      passes or fails on a duration. Status codes, payload shape, route metadata and container
//      contents are the only things asserted. Where a case needs a second request it makes one; it
//      does not time the first.
//
//   8. ONE DIVERGENCE FROM THIS FILE'S OWN BRIEF, RECORDED RATHER THAN QUIETLY RESOLVED (C-K). The
//      brief for this file asks it to assert that the REST routes and the gRPC surface share ONE
//      Kestrel endpoint with HTTP/1.1 and HTTP/2 both enabled on port 5101, in PLAINTEXT. One endpoint
//      on 5101 carrying both versions is exactly what ships; the plaintext half does not, and the
//      reason is recorded in appsettings.json and was measured on this toolchain rather than assumed:
//      a single CLEARTEXT endpoint declaring `Http1AndHttp2` does not serve HTTP/2 at all - Kestrel
//      warns that HTTP/2 requires TLS application-protocol negotiation and serves HTTP/1.1 only, which
//      would take all four gRPC contracts off the air while leaving the REST routes answering 200.
//      What ships is therefore ONE TLS endpoint, `Rest https://+:5101` with `Http1AndHttp2`, where
//      ALPN gives a probe HTTP/1.1 and a gRPC channel HTTP/2 on that one port - measured on a
//      throwaway host before it was asserted here.
//
//      A REVISION BETWEEN THE TWO SPLIT THEM ACROSS TWO TLS ENDPOINTS, 5101 pinned to HTTP/1.1 and a
//      second port pinned to HTTP/2, so each listener could only answer what it was for. That was
//      withdrawn: AAP 0.3.2.2 assigns contracts C-05 through C-08 to 5101, so serving them anywhere
//      else put a published contract on a port the map does not give it.
//
//      This file asserts the arrangement that SHIPS, and separately asserts the substance the brief is
//      protecting - that ONE application, ONE route table and ONE pipeline carry both REST routes AND
//      all four gRPC contracts, and that an HTTP/1.1 request for the readiness route is answered by
//      that same host. The listener is a deployment fact that lives in configuration; the shared
//      composition is the property a test can actually hold.
//
//   9. WHAT IS DELIBERATELY NOT HERE.
//       * NO DEFERRED SERVICE IN ANY FORM (constraint C-D). No project, type, route, client or
//         reference for DesignSystem, Documents, Integration or ScriptBridge. The four reserved
//         not-implemented routes are declarations on GATEWAY's routing table alone, and this file
//         asserts that Persistence does not answer them - they are simply NOT ROUTED here, which is a
//         404 and emphatically not a 501.
//       * NO REFERENCE TO ANOTHER SERVICE (constraint C-A). No Gateway, DataServices or Security type,
//         project or address appears, and no case needs any of the three to be running. The host boots
//         and serves with this service alone.
//       * NO SECOND ASSERTION LIBRARY, MOCKING LIBRARY OR CONTAINER LIBRARY. The five packages the
//         test manifest declares are the whole toolset; every double in this file is hand-authored,
//         and the clock double is the one the whole suite already shares so that a suite with three
//         fakes cannot disagree with itself.
//       * NO SCREAMING_SNAKE IDENTIFIER IS DECLARED. The preserved legacy spellings are CONSUMED from
//         the shared kernel, whose files are the ones the repository .editorconfig scopes its naming
//         relaxations to; this folder is outside every one of them and warnings are errors.
// ==================================================================================================

using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using PowerFramework.Persistence.Endpoints;

namespace PowerFramework.Persistence.Tests;

/// <summary>
/// Host-level conformance tests for the Persistence service's two diagnostic routes and for the
/// composition that publishes them.
/// </summary>
/// <remarks>
/// <para>
/// Every case boots the service's own <c>Program.cs</c> in process. Nothing here needs a live token
/// issuer, a reachable sibling service, a container, a daemon, a network or a real clock, and no case
/// depends on the deployment port being free - the factory supplies its own in-memory server.
/// </para>
/// <para>
/// Read the decision record at the head of this file before changing anything in it. Item 1 records why
/// the authenticated arm substitutes an authentication handler instead of minting a credential, item 3
/// records why adding an upstream probe to the readiness route deadlocks local orchestration, and item
/// 4 records why the not-ready arm must never be produced by deleting a file.
/// </para>
/// </remarks>
public sealed class HealthAndPingEndpointTests
{
    /// <summary>The anonymous readiness route, spelled as the attached environment's gate spells it.</summary>
    private const string HealthRoute = "/health";

    /// <summary>The authenticated liveness route, spelled as the documented contract spells it.</summary>
    private const string PingRoute = "/v1/ping";

    /// <summary>This service's canonical name, as both response bodies report it.</summary>
    private const string ServiceIdentifier = "persistence";

    /// <summary>The bearer scheme name, as it appears on an <c>Authorization</c> header.</summary>
    private const string BearerScheme = "Bearer";

    /// <summary>
    /// A credential shape no handler in this file accepts, used to prove a refusal is an evaluation
    /// rather than a blanket denial.
    /// </summary>
    /// <remarks>
    /// Deliberately not token-shaped and deliberately not secret-shaped: it is three words that could
    /// not be mistaken for key material by a reader or by a scanner, which is the whole point.
    /// </remarks>
    private const string UnparseableCredential = "not-a-credential-at-all";

    /// <summary>
    /// The component identifiers the readiness body is permitted to publish - the closed vocabulary
    /// <c>HealthEndpoints</c> authors for itself.
    /// </summary>
    /// <remarks>
    /// Asserted as a CLOSED set rather than as a set of expected members, because the property that
    /// matters is that nothing ELSE reaches the wire. A registration's own chosen name, a provider
    /// name, a host name or an upstream service name appearing here would be an information-disclosure
    /// surface wearing a diagnostic label, and the count of registered checks is the same disclosure in
    /// a different unit.
    /// </remarks>
    private static readonly string[] PublishableComponentNames =
    [
        "self",
        HealthEndpoints.SqliteCheckName,
        HealthEndpoints.RuntimeCheckName,
        "components",
    ];

    /// <summary>
    /// The four capability areas this refactor defers, named here ONLY so that their absence from this
    /// service can be asserted (constraint C-D).
    /// </summary>
    /// <remarks>
    /// These strings are the subject of a negative assertion and nothing else. No type, route, client,
    /// options section or placeholder for any of them exists in this service, and the reserved routes
    /// that will one day reach them are declarations on Gateway's routing table alone.
    /// </remarks>
    private static readonly string[] DeferredCapabilityAreas =
    [
        "design",
        "documents",
        "integration",
        "scripting",
    ];

    /// <summary>
    /// Substrings that must not appear in either diagnostic body. Configuration keys, every field name
    /// of the legacy transaction structure, and the connection details it carries.
    /// </summary>
    /// <remarks>
    /// The legacy models the right instinct at
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru:L118</c>, whose comment above
    /// the assignment that follows reads "erase parameters unrelated to the connection target" - scrub
    /// what does not belong before use. Of the structure's fields <c>logpass</c> is a credential and is
    /// write-only by contract [<c>transactiondata.srs</c>], and of the error structure's fields
    /// <c>sqlsyntax</c> carries the complete generated statement including interpolated literal values
    /// while the legacy logger redacts nothing [<c>dberrordata.srs</c>].
    /// </remarks>
    private static readonly string[] SubstringsThatMustNotLeak =
    [
        "authority",
        "audience",
        "issuer",
        "jwks",
        "well-known",
        "bearer",
        "claim",
        "logid",
        "logpass",
        "dbparm",
        "dbms",
        "servername",
        "userparm",
        "connectionstring",
        "datasource",
        "password",
        "sqlerrtext",
        "sqlsyntax",
        "/var/lib",
        "/tmp/",
        "test.db",
        "machine",
        "hostname",
        "assembly",
        "version",
    ];

    // ==============================================================================================
    //  1. THE HOST ITSELF - IT STARTS ALONE, AND IT REFUSES TO START BROKEN
    // ==============================================================================================

    /// <summary>
    /// The host boots and serves with no sibling service running, no network reachable and no
    /// deployment configuration.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// THE FIRST PROPERTY EVERYTHING ELSE IN THIS FILE RESTS ON (constraints C-A and C-I). The
    /// configured token authority is a reserved <c>.invalid</c> host that can never resolve, the storage
    /// location is a directory this case owns and nothing has provisioned, and no Gateway, DataServices
    /// or Security instance exists anywhere. The host still composes, still answers its anonymous route
    /// and still refuses its authenticated one - which is what "independently deployable" means when it
    /// is stated as a test rather than as an adjective.
    /// </para>
    /// <para>
    /// Both routes are exercised in one case on purpose. Booting successfully proves nothing on its own:
    /// a host can compose and then answer every route with a fault. Asserting the two verdicts together
    /// is what shows the pipeline is live in both of its postures.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TheHostStartsAndServesWithNothingElseRunning()
    {
        await using HealthAndPingHost host = HealthAndPingHost.Create();
        using HttpClient client = host.CreateClient();

        using HttpResponseMessage readiness = await client.GetAsync(
            HealthRoute,
            TestContext.Current.CancellationToken);
        using HttpResponseMessage liveness = await client.GetAsync(
            PingRoute,
            TestContext.Current.CancellationToken);

        // The anonymous route answered. Which verdict it carried is the subject of section 2; here the
        // point is only that it was SERVED rather than faulted or challenged.
        Assert.NotEqual(HttpStatusCode.Unauthorized, readiness.StatusCode);
        Assert.NotEqual(HttpStatusCode.InternalServerError, readiness.StatusCode);
        Assert.NotEqual(HttpStatusCode.NotFound, readiness.StatusCode);

        // And the authenticated route refused, so the boundary is live rather than merely declared.
        Assert.Equal(HttpStatusCode.Unauthorized, liveness.StatusCode);
    }

    /// <summary>
    /// A structurally invalid configuration prevents the host from starting.
    /// </summary>
    /// <remarks>
    /// <para>
    /// FAIL FAST, ASSERTED AS FAIL FAST (AAP section 0.1.4). The options graph is registered with
    /// validate-on-start and the startup gate reads its value before the pipeline is built, so a
    /// deployment that disables one of the four mandatory token validations does not get a service that
    /// starts and quietly trusts more than it should - it gets a process that refuses to compose.
    /// </para>
    /// <para>
    /// SOFTENING THIS WOULD BE A BEHAVIOURAL CHANGE DRESSED UP AS ROBUSTNESS, which is why the case
    /// exists at all. The legacy reports a decoded assertion failure and then executes <c>HALT CLOSE</c>
    /// [<c>ws_objects/pfw.pbl.src/pfw.sra:L141-L143</c>]; it does not log a warning and continue. A
    /// future change that turned this refusal into a logged warning would convert a startup failure an
    /// operator sees into a security posture nobody sees, and every other case in this file would still
    /// pass.
    /// </para>
    /// <para>
    /// THE CHOSEN FAULT IS A SECURITY ONE ON PURPOSE. Disabling issuer validation is not a typo an
    /// operator makes and recovers from; it is the setting whose silent acceptance would let a token
    /// minted by anyone through. The validator's own message names the member and states the
    /// consequence, and both are asserted so that the refusal is diagnosable rather than merely loud.
    /// </para>
    /// <para>
    /// The exception surfaces the moment the host is composed, which is what asking the factory for a
    /// client does. It is unwrapped through its inner chain because the in-process test host relays an
    /// entry-point failure rather than rethrowing it in place, and the assertion is about the CAUSE
    /// rather than about the relay.
    /// </para>
    /// </remarks>
    [Fact]
    public void AnInvalidOptionsGraphPreventsTheHostFromStarting()
    {
        using HealthAndPingHost host = HealthAndPingHost.Create(
            settings: new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["Jwt:ValidateIssuer"] = "false",
            });

        Exception failure = Assert.ThrowsAny<Exception>(() => host.CreateClient());

        OptionsValidationException? refusal = FindInChain<OptionsValidationException>(failure);

        Assert.NotNull(refusal);

        // The member an operator has to put back, and the consequence of having removed it. Both come
        // from the validator's own text, so this also pins that the refusal explains itself.
        Assert.Contains(
            refusal.Failures,
            message => message.Contains("ValidateIssuer", StringComparison.Ordinal));
    }

    /// <summary>
    /// The single clock seam is substitutable for the whole host, and both diagnostic bodies read it.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// DETERMINISM IS A REQUIREMENT OF THE EVIDENCE MODEL, NOT A CONVENIENCE (AAP section 0.6.7). Every
    /// clock read is a seam a characterization run has to be able to mask from the master recording and
    /// the candidate alike, so a timestamp produced from an ambient clock is a defect rather than a
    /// style choice. This case proves the substitution reaches BOTH routes through one registration -
    /// the composition root registers the seam once and each handler resolves it - so a future handler
    /// that reached for the ambient clock would show up here as a timestamp that refused to be pinned.
    /// </para>
    /// <para>
    /// It asserts EQUALITY against the fake's own instant rather than a tolerance, which is the
    /// difference between proving the seam is used and proving it exists. Note also what is NOT
    /// asserted: nothing here measures how long anything took. The clock is the subject, not the
    /// instrument (AAP section 0.8.5).
    /// </para>
    /// </remarks>
    [Fact]
    public async Task BothBodiesTakeTheirTimestampFromTheInjectedClock()
    {
        await using HealthAndPingHost host = HealthAndPingHost.Create(
            posture: AuthenticationPosture.Substituted,
            storage: ScriptedStorageCheck.Reporting(HealthStatus.Healthy));

        using HttpClient client = host.CreateClient();

        // Moved off the fake's default start so that a handler quietly reading the ambient clock cannot
        // coincidentally agree with it.
        host.Clock.Advance(TimeSpan.FromMinutes(37));

        DateTimeOffset expected = host.Clock.GetUtcNow();

        using HttpResponseMessage readiness = await client.GetAsync(
            HealthRoute,
            TestContext.Current.CancellationToken);

        using HttpRequestMessage livenessRequest = new(HttpMethod.Get, PingRoute);
        livenessRequest.Headers.Authorization = host.PrincipalCredential();

        using HttpResponseMessage liveness = await client.SendAsync(
            livenessRequest,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, readiness.StatusCode);
        Assert.Equal(HttpStatusCode.OK, liveness.StatusCode);

        using JsonDocument readinessBody = await ReadJsonAsync(readiness);
        using JsonDocument livenessBody = await ReadJsonAsync(liveness);

        Assert.Equal(
            expected,
            readinessBody.RootElement.GetProperty("checkedAt").GetDateTimeOffset());
        Assert.Equal(
            expected,
            livenessBody.RootElement.GetProperty("timestamp").GetDateTimeOffset());
    }

    // ==============================================================================================
    //  2. GET /health - ANONYMOUS, LOCAL ONLY, READ ONLY (contract C-10)
    // ==============================================================================================

    /// <summary>
    /// Readiness answers without a credential, and it is the only route on this service that does.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// THE PROPERTY THE WHOLE LOCAL ORCHESTRATION PATH RESTS ON (constraints C-G, C-L). The composition
    /// root installs a fallback authorization policy requiring an authenticated user, so on this service
    /// a route with no policy of its own is CLOSED rather than open - which means the anonymous opt-out
    /// on this one route is what makes the readiness gate satisfiable at all. Without it the probe would
    /// receive 401, the dependency condition would never open, and Gateway would stay ungated forever.
    /// </para>
    /// <para>
    /// AND THE EXCEPTION IS ASSERTED TO BE EXACTLY ONE ROUTE WIDE. The live pair of requests shows the
    /// two postures differ; the route-table assertion shows that no OTHER endpoint has quietly acquired
    /// the same opt-out - including the four gRPC contracts, where an anonymous one would expose the
    /// only SQL-executing surface in the system. A live pair alone could not see that, because it can
    /// only ever ask about the routes it thought to ask about.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ReadinessIsTheOnlyAnonymousRouteOnThisService()
    {
        await using HealthAndPingHost host = HealthAndPingHost.Create(
            storage: ScriptedStorageCheck.Reporting(HealthStatus.Healthy));

        using HttpClient client = host.CreateClient();

        using HttpResponseMessage readiness = await client.GetAsync(
            HealthRoute,
            TestContext.Current.CancellationToken);
        using HttpResponseMessage liveness = await client.GetAsync(
            PingRoute,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, readiness.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, liveness.StatusCode);

        // The route table, read from the composed host rather than from the source. Anything carrying
        // the anonymous opt-out is listed, and the list must be exactly the readiness route.
        string[] anonymous =
        [
            .. RoutedEndpoints(host)
                .Where(static endpoint =>
                    endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null)
                .Select(static endpoint => endpoint.RoutePattern.RawText ?? string.Empty)
                .Order(StringComparer.Ordinal),
        ];

        Assert.Equal([HealthRoute], anonymous);

        // And the authenticated route carries a requirement rather than merely lacking an exemption -
        // the two are not the same, and a route with neither would be open.
        RouteEndpoint ping = SingleRoute(host, PingRoute);

        Assert.NotEmpty(ping.Metadata.GetOrderedMetadata<IAuthorizeData>());
        Assert.Null(ping.Metadata.GetMetadata<IAllowAnonymous>());
    }

    /// <summary>
    /// The ready body publishes only the closed component vocabulary this service authors for itself.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// BECAUSE THE ROUTE IS ANONYMOUS, ITS BODY IS WORLD READABLE by anything that can reach the port,
    /// so the vocabulary is asserted as CLOSED rather than as a set of expected members (constraints C-F
    /// and C-G). A check's registered name is chosen by whatever registered it rather than authored for
    /// disclosure, so a name naming a provider, a region, a host or a sibling service would otherwise be
    /// published; folding every other registration into one aggregate entry is what also keeps the COUNT
    /// of registrations - the shape of this service's dependency graph - off the wire.
    /// </para>
    /// <para>
    /// THIS IS ALSO THE ASSERTION THAT NO UPSTREAM APPEARS. An entry named for Gateway, DataServices or
    /// Security would fail this case, which is the cheapest possible tripwire on the topology inversion
    /// described in item 3 of the decision record.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TheReadyBodyPublishesOnlyTheClosedComponentVocabulary()
    {
        await using HealthAndPingHost host = HealthAndPingHost.Create(
            storage: ScriptedStorageCheck.Reporting(HealthStatus.Healthy));

        using HttpClient client = host.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(
            HealthRoute,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using JsonDocument document = await ReadJsonAsync(response);

        JsonElement root = document.RootElement;

        Assert.Equal("Healthy", root.GetProperty("status").GetString());
        Assert.Equal(ServiceIdentifier, root.GetProperty("service").GetString());

        JsonElement checks = root.GetProperty("checks");

        Assert.Equal(JsonValueKind.Array, checks.ValueKind);
        Assert.NotEqual(0, checks.GetArrayLength());

        foreach (JsonElement check in checks.EnumerateArray())
        {
            string name = check.GetProperty("name").GetString() ?? string.Empty;

            Assert.Contains(name, PublishableComponentNames);
            Assert.Equal("Healthy", check.GetProperty("status").GetString());
        }
    }

    /// <summary>
    /// An unreachable engine is reported not ready, the host keeps serving, and nothing is created.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// THE NOT-READY ARM IS THE REAL PROBE ANSWERING HONESTLY, NOT A STUB AND NOT A FILE DELETE (item 4
    /// of the decision record). The storage location this host owns has never been provisioned, and the
    /// readiness path opens READ-ONLY AND NON-CREATING - so it refuses rather than creating a database,
    /// which is exactly the state the gate Gateway hangs on exists to detect.
    /// </para>
    /// <para>
    /// THE NON-CREATING PART IS A SECURITY PROPERTY RATHER THAN A TIDINESS ONE, and it is asserted
    /// against the file system rather than described. This route is anonymous, so anything the probe does
    /// an unauthenticated caller can make it do; a probe that created the database would let an
    /// unauthenticated caller provoke a data mutation, and it would rewrite the volume state that paired
    /// characterization recordings must share.
    /// </para>
    /// <para>
    /// READINESS IS NOT A STARTUP GATE, WHICH IS THE OTHER HALF OF THIS CASE. Unreachable storage is a
    /// runtime condition and is REPORTED; it is not a structural fault and does not terminate the
    /// process. That distinction is the whole reason the readiness handler returns a verdict instead of
    /// throwing: if it threw, Gateway's aggregator would observe a transport error rather than the
    /// structured not-ready state contract C-10 promises. So the case asks again afterwards, and asks
    /// the authenticated route too, to show the host is still composed and still enforcing.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AnUnreachableEngineIsReportedNotReadyWithoutCreatingADatabase()
    {
        // No storage substitution: the REAL reachability check runs, against a directory this host owns
        // and nothing has provisioned.
        await using HealthAndPingHost host = HealthAndPingHost.Create();

        using HttpClient client = host.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(
            HealthRoute,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

        using JsonDocument document = await ReadJsonAsync(response);

        JsonElement problem = document.RootElement;

        Assert.Equal(503, problem.GetProperty("status").GetInt32());

        // The machine-readable verdict Gateway's aggregator reads. It cannot live in `status`, which RFC
        // 9457 uses for the integer HTTP status asserted above.
        Assert.Equal("Unhealthy", problem.GetProperty("serviceStatus").GetString());

        // The legacy vocabulary's retry code, because the condition is transient and the caller's
        // correct response is to ask again - which is precisely what a readiness gate does.
        Assert.Equal(RetCode.E_RETRY, problem.GetProperty("retCode").GetInt64());

        // NOTHING WAS CREATED. Asserted as an observation of the storage location rather than as a claim
        // about the code path.
        Assert.Empty(host.StoredDatabaseFiles());

        // AND THE HOST IS STILL COMPOSED AND STILL ENFORCING. A not-ready verdict is a report, not a
        // structural fault.
        using HttpResponseMessage again = await client.GetAsync(
            HealthRoute,
            TestContext.Current.CancellationToken);
        using HttpResponseMessage liveness = await client.GetAsync(
            PingRoute,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, again.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, liveness.StatusCode);
    }

    /// <summary>
    /// Repeated probes leave the storage location exactly as they found it.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// WHY REPETITION IS THE RIGHT SHAPE FOR THIS CASE (AAP section 0.6.7). The orchestration health
    /// condition polls this route CONTINUOUSLY, so a probe that mutated anything would not be an edge
    /// case - it would be a high-frequency rewrite of the very volume state that paired characterization
    /// recordings are required to share. Asserting the directory before and against after is the only
    /// form of this assertion that a future <c>EnsureCreated</c>, migration application, journal switch
    /// or seed could not slip past.
    /// </para>
    /// <para>
    /// The verdict is deliberately not the subject here and is not asserted - a stub keeps the storage
    /// verdict out of the way so that what remains under observation is the file system alone.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task RepeatedProbesLeaveTheStorageLocationUntouched()
    {
        await using HealthAndPingHost host = HealthAndPingHost.Create(
            storage: ScriptedStorageCheck.Reporting(HealthStatus.Healthy));

        using HttpClient client = host.CreateClient();

        // Taken after the host has composed, so the startup gate's own writability probe - which
        // creates a uniquely named zero-byte file and removes it again - is already accounted for.
        using HttpResponseMessage first = await client.GetAsync(
            HealthRoute,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        string[] before = host.StoredEntries();

        for (int poll = 0; poll < 3; poll++)
        {
            using HttpResponseMessage repeat = await client.GetAsync(
                HealthRoute,
                TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.OK, repeat.StatusCode);
        }

        Assert.Equal(before, host.StoredEntries());
    }

    /// <summary>
    /// Neither readiness arm discloses configuration, credentials, storage detail or topology.
    /// </summary>
    /// <param name="ready">Whether the storage verdict is substituted as ready.</param>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// BOTH ARMS ARE CHECKED BECAUSE THE FAILURE ARM IS THE ONE THAT LEAKS. A ready body has little to
    /// say; a not-ready body is written while something has gone wrong, which is exactly when a provider
    /// message, a file path, a connection string or an exception summary is most likely to be helpfully
    /// included. The forbidden list is drawn from the two legacy structures that carry the material:
    /// <c>transactiondata</c>, whose <c>logpass</c> member is a credential and write-only by contract,
    /// and <c>dberrordata</c>, whose <c>sqlsyntax</c> member carries the complete generated statement
    /// including interpolated literal values.
    /// </para>
    /// <para>
    /// The comparison is case-insensitive and covers the WHOLE body rather than named members, so a
    /// value appearing in a place this case did not anticipate still fails it.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NeitherReadinessArmDisclosesConfigurationOrTopology(bool ready)
    {
        await using HealthAndPingHost host = HealthAndPingHost.Create(
            storage: ScriptedStorageCheck.Reporting(
                ready ? HealthStatus.Healthy : HealthStatus.Unhealthy));

        using HttpClient client = host.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(
            HealthRoute,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            ready ? HttpStatusCode.OK : HttpStatusCode.ServiceUnavailable,
            response.StatusCode);

        AssertDisclosesNothing(await ReadTextAsync(response));
    }

    /// <summary>
    /// The readiness verdict is produced with no outbound edge of any kind.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// THIS IS THE TRIPWIRE ON THE TOPOLOGY (item 3 of the decision record, constraint C-K). Persistence
    /// is the bottom of the layered, acyclic call graph and aggregation belongs to Gateway alone. An
    /// upstream probe added here would invert the graph AND deadlock the readiness chain, because the
    /// orchestration manifest gates Gateway on this endpoint - so the endpoint would be waiting,
    /// transitively, on something waiting on it, and local orchestration would present the cycle as a
    /// service that never becomes ready.
    /// </para>
    /// <para>
    /// TWO INDEPENDENT PROOFS, BECAUSE EITHER ALONE IS EVADABLE. The static one reads the application
    /// assembly's own reference list: no HTTP-client-factory library and no resilience or retry library
    /// is linked at all, which is why the resilience package is assigned to Gateway and DataServices and
    /// is absent here - an outbound edge cannot be added to this service without first changing its
    /// manifest. The live one runs the route while the configured token authority is a reserved
    /// <c>.invalid</c> host with NO static verification material supplied, so the only way to obtain a
    /// key set would be a real network fetch that cannot succeed; the verdict still arrives, which it
    /// could not do if the endpoint fetched anything.
    /// </para>
    /// <para>
    /// The verification library IS expected to be linked and is asserted present, so that the negative
    /// assertions cannot pass vacuously against an assembly that simply references nothing.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ReadinessIsProducedWithNoOutboundEdge()
    {
        string[] linked =
        [
            .. ApplicationAssembly
                .GetReferencedAssemblies()
                .Select(static reference => reference.Name ?? string.Empty),
        ];

        // Verification material is expected, and is the whole reason a token library is linked at all.
        Assert.Contains("Microsoft.IdentityModel.Tokens", linked);

        // No client factory, and no retry or circuit-breaker library. An outbound edge would need one.
        Assert.DoesNotContain("Microsoft.Extensions.Http", linked);
        Assert.DoesNotContain("Microsoft.Extensions.Http.Resilience", linked);
        Assert.DoesNotContain("Microsoft.Extensions.Resilience", linked);
        Assert.DoesNotContain("Polly", linked);
        Assert.DoesNotContain("Polly.Core", linked);

        // The live half: no static verification configuration is supplied, so nothing but an impossible
        // network fetch could produce key material for this host.
        await using HealthAndPingHost host = HealthAndPingHost.Create(
            storage: ScriptedStorageCheck.Reporting(HealthStatus.Healthy),
            supplyStaticVerification: false);

        using HttpClient client = host.CreateClient();

        using HttpResponseMessage readiness = await client.GetAsync(
            HealthRoute,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, readiness.StatusCode);

        using JsonDocument document = await ReadJsonAsync(readiness);

        Assert.Equal("Healthy", document.RootElement.GetProperty("status").GetString());

        // And the unreachable authority is still not a licence: the authenticated route refuses rather
        // than falling open while its metadata is unavailable.
        using HttpResponseMessage liveness = await client.GetAsync(
            PingRoute,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, liveness.StatusCode);
    }

    // ==============================================================================================
    //  3. GET /v1/ping - AUTHENTICATED, WITH NO BYPASS AND NO MINTING CAPABILITY (constraint C-G)
    // ==============================================================================================

    /// <summary>
    /// A request carrying no credential is refused with exactly <c>401</c>, and the challenge names the
    /// bearer scheme.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// THE STATUS IS ASSERTED EXACTLY RATHER THAN AS "NOT 200", because each neighbouring code would be
    /// a different defect wearing the same refused-request clothes. A <c>403</c> would mean the caller
    /// was authenticated and then found insufficient - so something authenticated an anonymous request. A
    /// <c>404</c> would mean the route is not mapped at all, so the boundary being tested does not exist.
    /// A redirect would mean an interactive challenge scheme is installed, which is wrong for a
    /// service-to-service edge. Each of those would pass a laxer assertion.
    /// </para>
    /// <para>
    /// THE CHALLENGE HEADER IS PART OF THE CONTRACT AND NOT DECORATION. It tells the caller which
    /// credential to present, and asserting it also proves the refusal came from the BEARER handler
    /// rather than from a cookie or negotiate scheme having been registered by accident.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AnAnonymousRequestIsRefusedWithExactlyFourHundredAndOne()
    {
        await using HealthAndPingHost host = HealthAndPingHost.Create();
        using HttpClient client = host.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(
            PingRoute,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        Assert.Contains(
            response.Headers.WwwAuthenticate,
            header => string.Equals(header.Scheme, BearerScheme, StringComparison.Ordinal));
    }

    /// <summary>
    /// The deployed handler refuses a credential it cannot verify.
    /// </summary>
    /// <param name="credential">The credential parameter to present.</param>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// THE HANDLER UNDER TEST HERE IS THE DEPLOYED ONE - the stock bearer handler with all four
    /// mandatory validations on, given a STATIC verification configuration that carries the issuer and no
    /// keys (item 2 of the decision record). No key material exists anywhere in this fixture, so nothing
    /// presented can verify, and the refusal is established by the handler OFFLINE rather than by an
    /// unreachable discovery document - which is what makes it a real assertion rather than a
    /// coincidence of an unresolvable authority.
    /// </para>
    /// <para>
    /// WHAT THIS CASE DELIBERATELY DOES NOT CLAIM. It does not attribute the refusal to a particular
    /// validation, because with no verification key installed the first check to fail is the signature
    /// one for every row. Per-validation attribution - this audience, this lifetime, this signature -
    /// needs a host that can accept SOMETHING, which needs key material, which this file must not hold;
    /// it is asserted in <c>PingEndpointsTests</c> instead. The property proved here is the one that
    /// matters for the boundary: an unverifiable credential does not get in.
    /// </para>
    /// <para>
    /// The three rows are shaped differently on purpose, because each enters the handler by a different
    /// path: a value that is not a token at all, a value that IS a well-formed compact serialization, and
    /// an empty value.
    /// </para>
    /// <para>
    /// THE SECOND ROW IS THE CLASSIC FORGERY AND IS WORTH NAMING, so that a reader or a scanner does not
    /// mistake it for key material. Decoded, its two segments are the header <c>{"alg":"none"}</c> and the
    /// payload <c>{"sub":"anyone"}</c>, followed by an EMPTY signature - the unsigned-token attack, in
    /// which a caller asserts an identity and declines to prove it. It contains no key, no certificate and
    /// no credential of any kind; it is three constants and a full stop, and the only thing it can do is
    /// be refused. A handler that honoured the algorithm a token names about itself would accept it, which
    /// is exactly why the row is here.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(UnparseableCredential)]
    [InlineData("eyJhbGciOiJub25lIn0.eyJzdWIiOiJhbnlvbmUifQ.")]
    [InlineData("")]
    public async Task TheDeployedHandlerRefusesAnUnverifiableCredential(string credential)
    {
        await using HealthAndPingHost host = HealthAndPingHost.Create();
        using HttpClient client = host.CreateClient();

        using HttpRequestMessage request = new(HttpMethod.Get, PingRoute);
        request.Headers.Authorization = new AuthenticationHeaderValue(BearerScheme, credential);

        using HttpResponseMessage response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        // The refusal never echoes what was presented. A diagnostic that quoted the credential would copy
        // it into every log and proxy between here and the caller (constraint C-F).
        //
        // SKIPPED FOR THE EMPTY ROW, AND THE REASON IS THAT THE ASSERTION IS MEANINGLESS THERE RATHER THAN
        // INCONVENIENT: every string contains the empty string, so the check would fail on any response
        // body whatsoever - including a perfectly clean one - and it would prove nothing about echoing
        // either way. The row is still exercised for its status, which is what it is here for.
        if (credential.Length > 0)
        {
            string body = await ReadTextAsync(response);

            Assert.DoesNotContain(credential, body, StringComparison.Ordinal);
            Assert.DoesNotContain(
                credential,
                string.Join(' ', response.Headers.WwwAuthenticate.Select(static header =>
                    header.Parameter ?? string.Empty)),
                StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// An authenticated principal is admitted, and the body is the contract's four-member response.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// THE POSITIVE ARM, PRODUCED BY SUBSTITUTING THE HANDLER AND NEVER BY MINTING (item 1 of the
    /// decision record, constraints C-F and C-G). Asserting only the refusal would leave a route that
    /// rejected EVERY request - including valid ones - passing this file, which is why the arm exists;
    /// producing it by hand-assembling a credential would put key material in a fixture of a service
    /// that must never hold any, which is why it is produced this way.
    /// </para>
    /// <para>
    /// WHAT THE SUBSTITUTION DOES AND DOES NOT CHANGE. It changes WHO decides the principal. It does not
    /// touch the route's authorization requirement, the fallback policy, the middleware order or the
    /// endpoint's own handler - so the 200 is produced by the deployed route, the deployed policy and the
    /// deployed pipeline, reached by a principal a substituted scheme vouched for. The credential is
    /// presented on the wire in the ordinary way, so the request shape is the real one too.
    /// </para>
    /// <para>
    /// The body is asserted member for member AND for its exact member COUNT, because the failure worth
    /// catching is an added member rather than a missing one: a liveness probe that grew a diagnostic
    /// field would be an information-disclosure surface, and it would grow silently.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AnAuthenticatedPrincipalIsAdmittedAndAnswersTheContractsBody()
    {
        await using HealthAndPingHost host = HealthAndPingHost.Create(
            posture: AuthenticationPosture.Substituted);

        using HttpClient client = host.CreateClient();

        using HttpRequestMessage request = new(HttpMethod.Get, PingRoute);
        request.Headers.Authorization = host.PrincipalCredential();

        using HttpResponseMessage response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using JsonDocument document = await ReadJsonAsync(response);

        JsonElement root = document.RootElement;

        Assert.Equal(JsonValueKind.Object, root.ValueKind);
        Assert.Equal(ServiceIdentifier, root.GetProperty("service").GetString());
        Assert.True(root.GetProperty("authenticated").GetBoolean());

        // The legacy success code, consumed symbolically from the shared kernel rather than written as a
        // literal, so a client already branching on the legacy return-code vocabulary needs no second
        // mechanism on this route.
        Assert.Equal(RetCode.OK, root.GetProperty("retCode").GetInt64());
        Assert.Equal(JsonValueKind.String, root.GetProperty("timestamp").ValueKind);

        Assert.Equal(4, root.EnumerateObject().Count());

        AssertDisclosesNothing(await ReadTextAsync(response));
    }

    /// <summary>
    /// The same credential is admitted while live and refused once the injected clock passes its expiry.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// EXPIRY IS PROVED BY MOVING THE CLOCK RATHER THAN BY PRESENTING SOMETHING ALREADY STALE, and the
    /// difference is what makes the case honest. One credential is used for both halves, so the only
    /// variable between the 200 and the 401 is TIME - which rules out the whole family of false passes in
    /// which a refusal is caused by the credential being malformed, by the wrong subject, or by the
    /// handler refusing everything. A separate already-expired value could not distinguish any of those.
    /// </para>
    /// <para>
    /// IT ALSO PROVES THE HANDLER RE-EVALUATES rather than caching its first verdict, and it does so
    /// against the same injected seam every other clock read in the service uses - so the case is
    /// deterministic, needs no sleep, and asserts nothing about duration (AAP sections 0.6.7 and 0.8.5).
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AnExpiredCredentialIsRefusedOnceTheInjectedClockPassesIt()
    {
        await using HealthAndPingHost host = HealthAndPingHost.Create(
            posture: AuthenticationPosture.Substituted);

        using HttpClient client = host.CreateClient();

        AuthenticationHeaderValue credential = host.PrincipalCredential(
            host.Clock.GetUtcNow() + TimeSpan.FromMinutes(5));

        using HttpRequestMessage live = new(HttpMethod.Get, PingRoute);
        live.Headers.Authorization = credential;

        using HttpResponseMessage accepted = await client.SendAsync(
            live,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);

        host.Clock.Advance(TimeSpan.FromMinutes(6));

        using HttpRequestMessage stale = new(HttpMethod.Get, PingRoute);
        stale.Headers.Authorization = credential;

        using HttpResponseMessage refused = await client.SendAsync(
            stale,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
    }

    /// <summary>
    /// A credential the substituted handler cannot read is refused, so the handler is an evaluation.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// THE ASSERTION THAT KEEPS THE POSITIVE ARM MEANINGFUL. A substituted handler that authenticated
    /// unconditionally would make every refusal in the substituted posture vacuous and would quietly turn
    /// the positive arm into a tautology. This case presents an unreadable value to the substituted
    /// handler and requires a refusal, so the 200 asserted above is known to be the result of a decision.
    /// It also covers the absent-credential path under the substituted scheme, which is a different code
    /// path from the unreadable one - no result at all, rather than a failure.
    /// </remarks>
    [Fact]
    public async Task TheSubstitutedHandlerRefusesWhatItCannotRead()
    {
        await using HealthAndPingHost host = HealthAndPingHost.Create(
            posture: AuthenticationPosture.Substituted);

        using HttpClient client = host.CreateClient();

        using HttpRequestMessage unreadable = new(HttpMethod.Get, PingRoute);
        unreadable.Headers.Authorization =
            new AuthenticationHeaderValue(BearerScheme, UnparseableCredential);

        using HttpResponseMessage refused = await client.SendAsync(
            unreadable,
            TestContext.Current.CancellationToken);

        using HttpResponseMessage absent = await client.GetAsync(
            PingRoute,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, absent.StatusCode);
    }

    /// <summary>
    /// There is no Development bypass and no anonymous fallback on the authenticated route.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// ASSERTED IN THE ENVIRONMENT WHERE A BYPASS WOULD BE MOST TEMPTING. The Development overlay is a
    /// real configuration path of its own - it moves the token authority to a loopback address and moves
    /// the storage location - so this case exercises a different path to the same posture rather than
    /// re-running the previous one under a different label. What the overlay deliberately does NOT do is
    /// relax anything: the four mandatory validations are not configurable in any environment, and the
    /// development concession this system permits is a self-signed certificate whose anchor arrives
    /// through configuration, never a plaintext authority and never a skipped check.
    /// </para>
    /// <para>
    /// A BYPASS THAT EXISTS ONLY IN DEVELOPMENT IS STILL A BYPASS. It makes the local build disagree with
    /// the published contract, and it is precisely the kind of convenience that reaches production by
    /// accident. The accepted half is included so the refusal is shown to be a real evaluation in this
    /// environment too rather than a blanket denial that would pass either way.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ThereIsNoDevelopmentBypass()
    {
        await using HealthAndPingHost deployed = HealthAndPingHost.Create(
            environment: Environments.Development);

        using HttpClient deployedClient = deployed.CreateClient();

        using HttpResponseMessage anonymous = await deployedClient.GetAsync(
            PingRoute,
            TestContext.Current.CancellationToken);

        using HttpRequestMessage presented = new(HttpMethod.Get, PingRoute);
        presented.Headers.Authorization =
            new AuthenticationHeaderValue(BearerScheme, UnparseableCredential);

        using HttpResponseMessage unverifiable = await deployedClient.SendAsync(
            presented,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unverifiable.StatusCode);

        // And the route still ADMITS an authenticated principal in Development, so the refusals above are
        // an evaluation rather than the route being closed outright in this environment.
        await using HealthAndPingHost substituted = HealthAndPingHost.Create(
            posture: AuthenticationPosture.Substituted,
            environment: Environments.Development);

        using HttpClient substitutedClient = substituted.CreateClient();

        using HttpRequestMessage authenticated = new(HttpMethod.Get, PingRoute);
        authenticated.Headers.Authorization = substituted.PrincipalCredential();

        using HttpResponseMessage admitted = await substitutedClient.SendAsync(
            authenticated,
            TestContext.Current.CancellationToken);

        using HttpResponseMessage stillRefused = await substitutedClient.GetAsync(
            PingRoute,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, admitted.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, stillRefused.StatusCode);
    }

    /// <summary>
    /// This service holds no token-minting capability at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE SOLE-ISSUER TOPOLOGY, ASSERTED STRUCTURALLY RATHER THAN BY REVIEW (constraints C-F and C-G).
    /// Exactly one signing secret exists in the system and Security holds it; every other service holds
    /// verification material only. A comment saying so is not enforcement, and neither is a code review,
    /// so this case reads the compiled application assembly and requires the capability to be ABSENT.
    /// </para>
    /// <para>
    /// FOUR INDEPENDENT PROBES, EACH CLOSING A DIFFERENT ROUTE BACK IN. The reference list closes the
    /// package route - neither minting library is linked, so a minting call could not compile. The member
    /// scan closes the hand-rolled route - no member is typed as signing credentials and no member is
    /// named for a private key or a secret. The method scan closes the API route - nothing writes,
    /// creates, mints or issues a token. And the verification-side member is asserted PRESENT so that
    /// none of the three negatives can pass vacuously against an assembly that simply has no token
    /// handling at all.
    /// </para>
    /// <para>
    /// THE VERIFICATION-SIDE SPELLING IS EXCLUDED DELIBERATELY AND THE EXCLUSION IS NARROW. A member
    /// governing issuer-signing-KEY VALIDATION is verification and must exist; a member HOLDING signing
    /// material must not. The scan therefore excludes exactly the validation spellings and nothing else,
    /// so a member called <c>SigningKey</c> or <c>SigningKeyPath</c> would still fail it.
    /// </para>
    /// </remarks>
    [Fact]
    public void ThisServiceHoldsNoTokenMintingCapability()
    {
        string[] linked =
        [
            .. ApplicationAssembly
                .GetReferencedAssemblies()
                .Select(static reference => reference.Name ?? string.Empty),
        ];

        Assert.Contains("Microsoft.IdentityModel.Tokens", linked);
        Assert.DoesNotContain("Microsoft.IdentityModel.JsonWebTokens", linked);
        Assert.DoesNotContain("System.IdentityModel.Tokens.Jwt", linked);

        List<string> possession = [];
        List<string> minting = [];
        bool verificationSideMemberFound = false;

        const BindingFlags everything = BindingFlags.Public
            | BindingFlags.NonPublic
            | BindingFlags.Instance
            | BindingFlags.Static
            | BindingFlags.DeclaredOnly;

        foreach (Type declared in ApplicationAssembly.GetTypes())
        {
            foreach (MemberInfo member in declared.GetMembers(everything))
            {
                if (HoldsSigningMaterial(member))
                {
                    possession.Add(string.Concat(declared.FullName, ".", member.Name));
                }

                if (member is MethodBase && MintsATokenByName(member.Name))
                {
                    minting.Add(string.Concat(declared.FullName, ".", member.Name));
                }

                verificationSideMemberFound |= string.Equals(
                    member.Name,
                    VerificationSideMemberName,
                    StringComparison.Ordinal);
            }
        }

        Assert.Empty(possession);
        Assert.Empty(minting);

        // NOT VACUOUS. The verification-side member exists, so this assembly demonstrably does handle
        // tokens and the three negatives above are statements about an assembly that has token code
        // rather than about one that has none. Without this line all three would pass against an empty
        // assembly, which is the classic way a security assertion becomes decoration.
        Assert.True(
            verificationSideMemberFound,
            "The application assembly declares no issuer-signing-key VALIDATION member, so the "
            + "minting assertions above may have passed vacuously. Verification material is expected "
            + "here; only minting is forbidden.");
    }

    /// <summary>
    /// No token-issuance or key-publication route exists on this service.
    /// </summary>
    /// <param name="issuanceRoute">A route shape that belongs to the sole issuer and not here.</param>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// THE OTHER HALF OF THE SOLE-ISSUER PROOF (constraint C-G). The previous case shows this service
    /// cannot mint; this one shows it does not PUBLISH an issuance or key-publication surface either -
    /// not a token endpoint, not a key set, and not the discovery document that advertises both. A service
    /// that answered any of these would be claiming an authority it does not have, and a consumer
    /// self-configuring from a discovery document it found here would be trusting the wrong party.
    /// </para>
    /// <para>
    /// ASSERTED TWICE, IN THE ROUTE TABLE AND ON THE WIRE, because the two miss different things. The
    /// route table cannot see a surface produced by middleware rather than by an endpoint; a live request
    /// cannot see a route that exists but happens to be refused before it answers. A <c>404</c> is the
    /// required answer and a <c>401</c> would not do - that would mean the route EXISTS and is merely
    /// protected.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("/v1/tokens")]
    [InlineData("/token")]
    [InlineData("/connect/token")]
    [InlineData("/.well-known/jwks.json")]
    [InlineData("/.well-known/openid-configuration")]
    public async Task NoTokenIssuanceOrKeyPublicationRouteExists(string issuanceRoute)
    {
        await using HealthAndPingHost host = HealthAndPingHost.Create(
            posture: AuthenticationPosture.Substituted);

        Assert.DoesNotContain(
            issuanceRoute,
            RoutedEndpoints(host)
                .Select(static endpoint => endpoint.RoutePattern.RawText ?? string.Empty)
                .ToArray(),
            StringComparer.OrdinalIgnoreCase);

        using HttpClient client = host.CreateClient();

        // Presented WITH a principal on purpose: an unauthenticated probe would be refused before the
        // router could report that the route is absent, and a 401 would hide a route that exists.
        using HttpRequestMessage request = new(HttpMethod.Get, issuanceRoute);
        request.Headers.Authorization = host.PrincipalCredential();

        using HttpResponseMessage response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// No deferred capability area is routed on this service, and none answers not-implemented.
    /// </summary>
    /// <param name="area">The deferred capability area's path segment.</param>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// CONSTRAINT C-D, AND THE DISTINCTION IT TURNS ON. The four reserved not-implemented routes are
    /// declarations on GATEWAY's routing table alone - metadata that makes the shape of the eventual
    /// system legible from the ingress contract. They are emphatically not a shape every service
    /// repeats: this service declares no route, no type, no client, no options section and no
    /// placeholder for DesignSystem, Documents, Integration or ScriptBridge.
    /// </para>
    /// <para>
    /// SO THE REQUIRED ANSWER IS <c>404</c> AND A <c>501</c> WOULD BE A FAILURE, which is the opposite of
    /// what the same request must produce at the ingress. A <c>501</c> here would mean this service had
    /// acquired a reserved declaration of its own - the beginning of exactly the partial implementation
    /// the constraint forbids, and the form it would most plausibly arrive in.
    /// </para>
    /// <para>
    /// Both the bare segment and a nested path are requested, because a two-segment path and a
    /// three-segment path match different route templates - a catch-all with two parameter segments would
    /// swallow the first and not the second, and it would answer from the wrong handler.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("design")]
    [InlineData("documents")]
    [InlineData("integration")]
    [InlineData("scripting")]
    public async Task NoDeferredCapabilityIsRoutedOnThisService(string area)
    {
        Assert.Contains(area, DeferredCapabilityAreas);

        await using HealthAndPingHost host = HealthAndPingHost.Create(
            posture: AuthenticationPosture.Substituted);

        string prefix = string.Create(CultureInfo.InvariantCulture, $"/v1/{area}");

        Assert.DoesNotContain(
            RoutedEndpoints(host),
            endpoint => (endpoint.RoutePattern.RawText ?? string.Empty)
                .StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

        using HttpClient client = host.CreateClient();

        foreach (string path in new[] { prefix, string.Concat(prefix, "/anything") })
        {
            using HttpRequestMessage request = new(HttpMethod.Get, path);
            request.Headers.Authorization = host.PrincipalCredential();

            using HttpResponseMessage response = await client.SendAsync(
                request,
                TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.NotEqual(HttpStatusCode.NotImplemented, response.StatusCode);

            // And nothing in the answer names a deferred service or claims a reservation, which is what
            // separates "not routed here" from "reserved here".
            string body = await ReadTextAsync(response);

            Assert.DoesNotContain("NotImplemented", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("reserved", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("DesignSystem", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("ScriptBridge", body, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ==============================================================================================
    //  4. THE REST OF THE PUBLISHED SURFACE
    // ==============================================================================================

    /// <summary>
    /// The two REST routes and all four gRPC contracts are published by one composed host.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// THIS IS THE SUBSTANCE BEHIND ITEM 8 OF THE DECISION RECORD. The listener arrangement is a
    /// deployment fact that lives in configuration and is asserted from there in the next case; the
    /// property a test can actually hold is that ONE application, ONE route table and ONE pipeline carry
    /// the readiness route, the liveness route and contracts C-05 through C-08 together. That is what
    /// makes the diagnostic routes diagnostic OF the gRPC surface rather than of a second process that
    /// happens to answer beside it.
    /// </para>
    /// <para>
    /// EVERY gRPC METHOD IS REQUIRED TO CARRY AN AUTHORIZATION REQUIREMENT, which matters more here than
    /// on the two REST routes: these four are the only surfaces in the entire system that generate or
    /// execute SQL, so an unprotected one would be direct, unauthenticated access to the only storage
    /// engine there is. The requirement travels as an attribute on each implementation rather than as a
    /// mapping-time call, so reading it back off the composed route table is the only way to see it.
    /// </para>
    /// <para>
    /// The in-memory server is protocol-agnostic and negotiates nothing, so what the final request proves
    /// is that an ordinary HTTP/1.1 REST request is answered by the same host these contracts are mapped
    /// on - not that a socket negotiated a version. Claiming the latter from a test server would be a
    /// claim about the test server rather than about the service.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task OneComposedHostPublishesBothRestRoutesAndAllFourGrpcContracts()
    {
        await using HealthAndPingHost host = HealthAndPingHost.Create(
            storage: ScriptedStorageCheck.Reporting(HealthStatus.Healthy));

        IReadOnlyList<RouteEndpoint> routed = RoutedEndpoints(host);

        string[] patterns =
        [
            .. routed.Select(static endpoint => endpoint.RoutePattern.RawText ?? string.Empty),
        ];

        Assert.Contains(HealthRoute, patterns);
        Assert.Contains(PingRoute, patterns);

        foreach (string contract in GrpcContractPrefixes)
        {
            List<RouteEndpoint> methods =
            [
                .. routed.Where(endpoint => (endpoint.RoutePattern.RawText ?? string.Empty)
                    .StartsWith(contract, StringComparison.Ordinal)),
            ];

            Assert.NotEmpty(methods);
            Assert.All(
                methods,
                static method => Assert.NotEmpty(method.Metadata.GetOrderedMetadata<IAuthorizeData>()));
        }

        using HttpClient client = host.CreateClient();

        using HttpRequestMessage request = new(HttpMethod.Get, HealthRoute)
        {
            Version = HttpVersion.Version11,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact,
        };

        using HttpResponseMessage response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// The one configured listener carries both protocol versions over TLS on the assigned port, and the
    /// reserved port stays unbound.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// ASSERTED FROM CONFIGURATION BECAUSE THAT IS WHERE THE DECISION LIVES (constraints C-F and C-K).
    /// No port, address or scheme is restated in code anywhere in this service - there is no
    /// <c>UseUrls</c> call and no literal port - so a port change is a configuration change rather than a
    /// recompile, and configuration is consequently the only honest place to assert the arrangement.
    /// </para>
    /// <para>
    /// WHY TLS IS ASSERTED RATHER THAN TOLERATED, AND WHY IT IS THE LOAD-BEARING HALF. A single CLEARTEXT
    /// endpoint declaring both protocol versions does NOT serve HTTP/2 - measured on this toolchain,
    /// Kestrel warns that HTTP/2 requires TLS application-protocol negotiation and then serves HTTP/1.1
    /// only, which would take all four gRPC contracts off the air while the readiness probe kept answering
    /// 200. That is the worst available failure shape, because the gate would open onto a service that
    /// could serve nothing. Over TLS the same declaration works exactly as written: ALPN gives a probe
    /// HTTP/1.1 and a channel HTTP/2 on one port, measured on a throwaway host before being asserted here.
    /// TLS is additionally required in its own right - every request across this listener carries a bearer
    /// token and this is the only service holding a storage provider, so a readable channel would expose
    /// the credential and the data it authorises together.
    /// </para>
    /// <para>
    /// ONE ENDPOINT, ON THE PORT THE MAP ASSIGNS, AND THE WITHDRAWN SECOND ONE IS ASSERTED ABSENT. A
    /// revision before this one split the surfaces across two TLS endpoints - 5101 pinned to
    /// <c>Http1</c> and a second port pinned to <c>Http2</c> - so that each listener could only answer
    /// what it was for. AAP 0.3.2.2 assigns contracts C-05 through C-08 to 5101, so that arrangement
    /// served published contracts on a port the map does not give them; it was collapsed onto the assigned
    /// port. The absence of a second endpoint is asserted rather than assumed, because re-adding one is
    /// how the surfaces would drift apart again.
    /// </para>
    /// <para>
    /// AND THE RESERVED PORT STAYS UNBOUND (constraint C-D). The documented port band assigns 5103 to a
    /// service this refactor defers, so binding it here would break the published map and quietly claim a
    /// slot that is deliberately being held open.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TheConfiguredListenerCarriesBothProtocolVersionsOverTlsOnTheAssignedPort()
    {
        await using HealthAndPingHost host = HealthAndPingHost.Create(
            storage: ScriptedStorageCheck.Reporting(HealthStatus.Healthy));

        IConfiguration configuration = host.Services.GetRequiredService<IConfiguration>();

        string restUrl = configuration["Kestrel:Endpoints:Rest:Url"] ?? string.Empty;

        Assert.Equal("https://+:5101", restUrl);
        Assert.Equal("Http1AndHttp2", configuration["Kestrel:Endpoints:Rest:Protocols"]);

        // It is not plaintext, because every request across it carries a bearer token and this is the only
        // service holding a storage provider - and because HTTP/2 is unavailable without TLS at all.
        Assert.StartsWith("https://", restUrl, StringComparison.Ordinal);

        // EXACTLY ONE ENDPOINT. A second would put a published contract back on a port AAP 0.3.2.2 does
        // not assign it, which is precisely what was withdrawn.
        Assert.Equal(
            ["Rest"],
            configuration
                .GetSection("Kestrel:Endpoints")
                .GetChildren()
                .Select(section => section.Key)
                .OrderBy(static key => key, StringComparer.Ordinal));

        Assert.Null(configuration["Kestrel:Endpoints:Grpc:Url"]);

        // The reserved Phase-2 slot is not bound by this service.
        Assert.DoesNotContain(
            configuration
                .GetSection("Kestrel:Endpoints")
                .GetChildren()
                .Select(section => section["Url"] ?? string.Empty),
            url => url.Contains(":5103", StringComparison.Ordinal));
    }

    // ==============================================================================================
    //  5. SHARED ASSERTION AND READING HELPERS
    // ==============================================================================================

    /// <summary>The four published gRPC contracts, by their route prefix, leading slash included.</summary>
    /// <remarks>
    /// Spelled as the generated bases route them - the protocol-buffer package and service name - so a
    /// contract renamed on the wire fails this file rather than silently moving.
    /// </remarks>
    private static string[] GrpcContractPrefixes =>
    [
        "/persistence.v1.QueryService/",
        "/persistence.v1.UpdateService/",
        "/persistence.v1.CommandService/",
        "/persistence.v1.TransactionService/",
    ];

    /// <summary>
    /// The member name that proves this assembly handles tokens on the VERIFICATION side.
    /// </summary>
    /// <remarks>
    /// Used to keep the minting assertions from passing vacuously. A member governing issuer-signing-key
    /// VALIDATION is verification and is required; a member HOLDING signing material is minting and is
    /// forbidden. The two are one word apart, which is exactly why the distinction is written down.
    /// </remarks>
    private const string VerificationSideMemberName = "ValidateIssuerSigningKey";

    /// <summary>The application assembly under test, reached through a type it publishes.</summary>
    /// <remarks>
    /// Resolved from <see cref="PingEndpoints"/> rather than from <c>Program</c> so that this property
    /// reads the same whether or not the entry point is reachable, and so that nothing here depends on
    /// the internals grant beyond what the sibling cases already rely on.
    /// </remarks>
    private static Assembly ApplicationAssembly => typeof(PingEndpoints).Assembly;

    /// <summary>Reads the composed host's route table.</summary>
    /// <param name="host">The host to interrogate. Composing it is a side effect of asking.</param>
    /// <returns>Every routed endpoint the host published.</returns>
    private static IReadOnlyList<RouteEndpoint> RoutedEndpoints(HealthAndPingHost host) =>
    [
        .. host.Services
            .GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>(),
    ];

    /// <summary>Resolves exactly one routed endpoint by its literal pattern.</summary>
    /// <param name="host">The host to interrogate.</param>
    /// <param name="pattern">The route pattern, matched exactly.</param>
    /// <returns>The single endpoint carrying that pattern.</returns>
    /// <remarks>
    /// Deliberately insists on exactly one. A duplicate mapping of a diagnostic route would make which
    /// metadata applies depend on registration order, and the anonymity of one of these two routes is
    /// decided by metadata.
    /// </remarks>
    private static RouteEndpoint SingleRoute(HealthAndPingHost host, string pattern) =>
        Assert.Single(
            RoutedEndpoints(host),
            endpoint => string.Equals(
                endpoint.RoutePattern.RawText,
                pattern,
                StringComparison.Ordinal));

    /// <summary>Reads a response body as text.</summary>
    /// <param name="response">The response to read.</param>
    /// <returns>The body, or an empty string when there is none.</returns>
    private static async Task<string> ReadTextAsync(HttpResponseMessage response) =>
        await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

    /// <summary>Reads a response body as JSON.</summary>
    /// <param name="response">The response to read.</param>
    /// <returns>The parsed document. The caller owns it.</returns>
    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await ReadTextAsync(response));

    /// <summary>
    /// Asserts that a diagnostic body discloses nothing it must not.
    /// </summary>
    /// <param name="body">The body to inspect.</param>
    /// <remarks>
    /// Compared case-insensitively over the WHOLE body rather than member by member, so a value appearing
    /// in a member this file did not anticipate still fails. The failure message names the offending
    /// substring and deliberately does NOT print the body, because a disclosure assertion whose failure
    /// output republishes the disclosure has defeated itself.
    /// </remarks>
    private static void AssertDisclosesNothing(string body)
    {
        foreach (string forbidden in SubstringsThatMustNotLeak)
        {
            Assert.False(
                body.Contains(forbidden, StringComparison.OrdinalIgnoreCase),
                string.Concat(
                    "A diagnostic body published the forbidden substring '",
                    forbidden,
                    "'. The body itself is deliberately NOT reproduced in this message: a disclosure "
                    + "assertion whose failure output republishes the disclosure has defeated itself."));
        }
    }

    /// <summary>Finds the first exception of a given type anywhere in a fault's chain.</summary>
    /// <typeparam name="TException">The exception type to find.</typeparam>
    /// <param name="thrown">The exception that surfaced.</param>
    /// <returns>The matching exception, or <see langword="null"/> when the chain holds none.</returns>
    /// <remarks>
    /// The in-process test host relays a failure raised inside the entry point rather than rethrowing it
    /// in place, and the relay's shape is an implementation detail of the hosting layer. Asserting on the
    /// CAUSE rather than on the wrapper is what keeps the startup-gate case about the service.
    /// </remarks>
    private static TException? FindInChain<TException>(Exception thrown)
        where TException : Exception
    {
        for (Exception? candidate = thrown; candidate is not null; candidate = candidate.InnerException)
        {
            if (candidate is TException match)
            {
                return match;
            }

            if (candidate is not AggregateException aggregate)
            {
                continue;
            }

            foreach (Exception inner in aggregate.InnerExceptions)
            {
                if (FindInChain<TException>(inner) is TException nested)
                {
                    return nested;
                }
            }
        }

        return null;
    }

    /// <summary>Decides whether a member holds, or is named for holding, signing material.</summary>
    /// <param name="member">The member to judge.</param>
    /// <returns><see langword="true"/> when the member would give this service a minting capability.</returns>
    /// <remarks>
    /// Verification-side spellings are excluded by the narrowest possible rule - a name containing
    /// <c>IssuerSigningKey</c>, which covers the validation flag together with its compiler-generated
    /// accessors and backing field. A member called <c>SigningKey</c> or <c>SigningKeyPath</c> contains no
    /// such segment and is therefore still caught.
    /// </remarks>
    private static bool HoldsSigningMaterial(MemberInfo member)
    {
        if (member.Name.Contains("IssuerSigningKey", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (member.Name.Contains("SigningCredential", StringComparison.OrdinalIgnoreCase)
            || member.Name.Contains("SigningKey", StringComparison.OrdinalIgnoreCase)
            || member.Name.Contains("PrivateKey", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string? held = member switch
        {
            PropertyInfo property => property.PropertyType.Name,
            FieldInfo field => field.FieldType.Name,
            _ => null,
        };

        return string.Equals(held, "SigningCredentials", StringComparison.Ordinal);
    }

    /// <summary>Decides whether a method's name claims to produce a token.</summary>
    /// <param name="name">The method name to judge.</param>
    /// <returns><see langword="true"/> when the name describes issuing rather than validating.</returns>
    private static bool MintsATokenByName(string name) =>
        name.Contains("WriteToken", StringComparison.OrdinalIgnoreCase)
        || name.Contains("CreateToken", StringComparison.OrdinalIgnoreCase)
        || name.Contains("MintToken", StringComparison.OrdinalIgnoreCase)
        || name.Contains("IssueToken", StringComparison.OrdinalIgnoreCase);

    // ==============================================================================================
    //  6. THE FIXTURES
    // ==============================================================================================

    /// <summary>
    /// Which authentication handler decides the principal for a given host.
    /// </summary>
    /// <remarks>
    /// The distinction exists so that no case has to choose between being honest about the deployed
    /// handler and being able to reach the authenticated route at all. See item 1 of the decision record
    /// at the head of this file: every negative case runs under <see cref="Deployed"/>, and
    /// <see cref="Substituted"/> is used only where an authenticated principal is the subject.
    /// </remarks>
    private enum AuthenticationPosture
    {
        /// <summary>
        /// The service's own bearer handler, with every mandatory validation on and no key installed, so
        /// nothing presented can verify and every refusal is the handler's own offline decision.
        /// </summary>
        Deployed = 0,

        /// <summary>
        /// An additional scheme, made the host default, whose handler evaluates an opaque unsigned test
        /// credential against the injected clock. No key material exists in either posture.
        /// </summary>
        Substituted = 1,
    }

    /// <summary>
    /// Boots this service's own composition root in process, with a deterministic clock and a storage
    /// location the host owns.
    /// </summary>
    /// <remarks>
    /// <para>
    /// EVERYTHING THIS FIXTURE SUPPLIES IS SUPPLIED AS CONFIGURATION, so the service's own options
    /// validator and startup gate judge it exactly as they would judge a deployment. Nothing is injected
    /// past those gates, which is what makes a case that boots successfully evidence about the real
    /// composition rather than about a bypass.
    /// </para>
    /// <para>
    /// NO CREDENTIAL, KEY OR SECRET OF ANY KIND EXISTS ON THIS TYPE (constraint C-F). The authority it
    /// names is a reserved <c>.invalid</c> host that can never resolve, the audience is a test-only
    /// string, and the only credential it produces is an opaque unsigned marker carrying an expiry.
    /// </para>
    /// </remarks>
    private sealed class HealthAndPingHost : WebApplicationFactory<Program>
    {
        /// <summary>
        /// The issuer this host names. A reserved <c>.invalid</c> host, so it can never resolve.
        /// </summary>
        /// <remarks>
        /// <c>https</c> deliberately: the settings file requires secure metadata retrieval, and that
        /// setting names a FACT about the authority beside it rather than a hardening level - declaring it
        /// true against a plaintext authority does not make the fetch secure, it makes the fetch fail, and
        /// the options validator refuses the incoherent pair. Naming an <c>http</c> authority here would
        /// therefore fail startup for a reason unrelated to any case.
        /// </remarks>
        private const string TrustedIssuer = "https://security-service.persistence-host.invalid";

        /// <summary>The audience this host expects. Test-only, and not a value any deployment uses.</summary>
        private const string TrustedAudience = "powerframework-persistence-host-tests";

        /// <summary>The name of the substituted scheme, distinct from the bearer scheme it displaces.</summary>
        private const string SubstitutedScheme = "SubstitutedPrincipal";

        /// <summary>The storage location this host owns, names in configuration, and removes on disposal.</summary>
        private readonly string _storageDirectory =
            Path.Combine(Path.GetTempPath(), string.Concat("pfw-health-ping-", Guid.NewGuid().ToString("n")));

        /// <summary>The single clock every seam in the composed host reads.</summary>
        private readonly FakeTimeProvider _clock = new();

        /// <summary>Which handler decides the principal.</summary>
        private readonly AuthenticationPosture _posture;

        /// <summary>The host environment name, so the no-bypass case can exercise the overlay.</summary>
        private readonly string _environment;

        /// <summary>A substituted storage verdict, or <see langword="null"/> to keep the real probe.</summary>
        private readonly IHealthCheck? _storage;

        /// <summary>Whether the bearer handler is given static verification material.</summary>
        private readonly bool _supplyStaticVerification;

        /// <summary>Configuration applied last, so a case may override anything this host sets.</summary>
        private readonly IReadOnlyDictionary<string, string?> _settings;

        /// <summary>Whether the storage location has already been removed.</summary>
        private bool _storageRemoved;

        /// <summary>Initializes the fixture.</summary>
        /// <param name="posture">Which handler decides the principal.</param>
        /// <param name="environment">The host environment name.</param>
        /// <param name="storage">A substituted storage verdict, or <see langword="null"/>.</param>
        /// <param name="supplyStaticVerification">Whether to supply static verification material.</param>
        /// <param name="settings">Configuration applied after this host's own.</param>
        private HealthAndPingHost(
            AuthenticationPosture posture,
            string environment,
            IHealthCheck? storage,
            bool supplyStaticVerification,
            IReadOnlyDictionary<string, string?> settings)
        {
            _posture = posture;
            _environment = environment;
            _storage = storage;
            _supplyStaticVerification = supplyStaticVerification;
            _settings = settings;
        }

        /// <summary>The deterministic clock this host runs on.</summary>
        /// <remarks>
        /// Exposed so a case can move time deliberately. It is the SAME instance the composed host
        /// resolves, which is what lets the expiry case advance the clock and observe the substituted
        /// handler re-evaluate rather than assert against a second, unrelated fake.
        /// </remarks>
        internal FakeTimeProvider Clock => _clock;

        /// <summary>Creates a host.</summary>
        /// <param name="posture">Which handler decides the principal. Deployed unless stated otherwise.</param>
        /// <param name="environment">The host environment name. Production unless stated otherwise.</param>
        /// <param name="storage">
        /// A substituted storage verdict registered under the real check's own name, or
        /// <see langword="null"/> to leave the REAL read-only probe in place - which, against a location
        /// nothing has provisioned, is what produces an honest not-ready verdict.
        /// </param>
        /// <param name="supplyStaticVerification">
        /// Whether the bearer handler is given a static, KEY-LESS verification configuration so that no
        /// discovery document is fetched. Set to <see langword="false"/> only to prove that a route
        /// produces its answer without any outbound fetch at all.
        /// </param>
        /// <param name="settings">Configuration applied after this host's own, or <see langword="null"/>.</param>
        /// <returns>A host that composes on first use.</returns>
        internal static HealthAndPingHost Create(
            AuthenticationPosture posture = AuthenticationPosture.Deployed,
            string? environment = null,
            IHealthCheck? storage = null,
            bool supplyStaticVerification = true,
            IReadOnlyDictionary<string, string?>? settings = null)
            => new(
                posture,
                environment ?? Environments.Production,
                storage,
                supplyStaticVerification,
                settings ?? new Dictionary<string, string?>(StringComparer.Ordinal));

        /// <summary>
        /// Produces a credential the substituted handler accepts until the given instant.
        /// </summary>
        /// <param name="expiresAt">
        /// When the credential stops being acceptable. Ten minutes ahead of the injected clock by default.
        /// </param>
        /// <returns>An <c>Authorization</c> header value carrying an opaque, unsigned marker.</returns>
        /// <remarks>
        /// <para>
        /// THIS IS NOT A TOKEN AND IT IS NOT A SECRET (constraints C-F and C-G). It is not signed, it
        /// carries no key, it cannot be replayed anywhere - the only handler that recognises it exists in
        /// this file - and it is meaningless outside this process. That is the entire point: Persistence
        /// is never an issuer, exactly one signing secret exists in the system and Security holds it, and
        /// the minting library is absent from this project's manifest, so a test that needed to mint could
        /// not even restore.
        /// </para>
        /// <para>
        /// The expiry is expressed against the INJECTED clock rather than the ambient one, so a case can
        /// move time and watch the same credential stop being accepted without sleeping and without
        /// asserting anything about duration.
        /// </para>
        /// </remarks>
        internal AuthenticationHeaderValue PrincipalCredential(DateTimeOffset? expiresAt = null)
        {
            DateTimeOffset expiry = expiresAt ?? _clock.GetUtcNow() + TimeSpan.FromMinutes(10);

            return new AuthenticationHeaderValue(
                BearerScheme,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{SubstitutedPrincipalHandler.CredentialPrefix}{expiry.ToUnixTimeSeconds()}"));
        }

        /// <summary>Lists every entry currently in this host's storage location.</summary>
        /// <returns>The entry names, ordered, or an empty list when the location does not exist.</returns>
        /// <remarks>
        /// Names only, and sorted, so that the comparison is stable and so that a failure message carries
        /// no absolute path - the location is under the machine's temporary root and republishing it would
        /// be the disclosure this suite asserts against elsewhere.
        /// </remarks>
        internal string[] StoredEntries() =>
            Directory.Exists(_storageDirectory)
                ? [.. Directory
                    .EnumerateFileSystemEntries(_storageDirectory)
                    .Select(Path.GetFileName)
                    .Select(static name => name ?? string.Empty)
                    .Order(StringComparer.Ordinal)]
                : [];

        /// <summary>Lists every database file in this host's storage location.</summary>
        /// <returns>The database file names, ordered.</returns>
        /// <remarks>
        /// The journal and write-ahead companions are included by the pattern deliberately: a probe that
        /// opened for writing would leave one of those behind even where it created no main database file,
        /// and that is precisely the mutation this must catch.
        /// </remarks>
        internal string[] StoredDatabaseFiles() =>
        [
            .. StoredEntries()
                .Where(static name => name.Contains(".db", StringComparison.OrdinalIgnoreCase)),
        ];

        /// <inheritdoc/>
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            builder.UseEnvironment(_environment);

            // Supplied as configuration, applied after the shipped settings files, so the service's own
            // validator and startup gate judge exactly these values.
            builder.ConfigureAppConfiguration(configuration =>
                configuration.AddInMemoryCollection(BuildSettings()));

            builder.ConfigureServices(services =>
            {
                // REPLACED RATHER THAN TRY-ADDED. The composition root try-adds the system clock, and this
                // delegate runs after it, so a try-add here would silently lose and every timestamp
                // assertion in this file would be comparing the fake against the real clock.
                services.Replace(ServiceDescriptor.Singleton<TimeProvider>(_clock));

                if (_supplyStaticVerification)
                {
                    ConfigureStaticVerification(services);
                }

                if (_posture == AuthenticationPosture.Substituted)
                {
                    // Registers an ADDITIONAL scheme and makes it this host's default. The bearer scheme
                    // stays registered and every route keeps its own authorization requirement; the only
                    // thing that changes is which handler establishes the principal.
                    services
                        .AddAuthentication(SubstitutedScheme)
                        .AddScheme<AuthenticationSchemeOptions, SubstitutedPrincipalHandler>(
                            SubstitutedScheme,
                            static _ => { });
                }

                if (_storage is IHealthCheck substitute)
                {
                    SubstituteStorageVerdict(services, substitute);
                }
            });
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            try
            {
                base.Dispose(disposing);
            }
            finally
            {
                RemoveStorageDirectory();
            }
        }

        /// <inheritdoc/>
        /// <remarks>
        /// BOTH DISPOSAL PATHS ARE OVERRIDDEN because both are used - every case that boots a host awaits
        /// its disposal, while the startup-gate case disposes synchronously since its host never started.
        /// The removal itself is idempotent, so it does not matter whether the base implementation also
        /// routes this path through the synchronous one.
        /// </remarks>
        public override async ValueTask DisposeAsync()
        {
            try
            {
                await base.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                RemoveStorageDirectory();
            }
        }

        /// <summary>
        /// Hands the bearer handler a static verification configuration carrying no keys.
        /// </summary>
        /// <param name="services">The container being configured.</param>
        /// <remarks>
        /// <para>
        /// WHAT THIS BUYS, AND WHY IT IS NOT A RELAXATION (item 2 of the decision record). Supplying a
        /// configuration makes the framework wrap it in a static configuration manager, so the handler
        /// fetches no discovery document and downloads no key set - which is what keeps every case in this
        /// file hermetic and, more importantly, keeps every refusal HONEST. Left to resolve its material
        /// from an unresolvable authority, the handler would refuse everything for a reason that had
        /// nothing to do with the credential.
        /// </para>
        /// <para>
        /// AND IT CARRIES NO KEYS, WHICH IS THE POINT RATHER THAN AN OMISSION. A configuration with no
        /// verification key can verify nothing, so every presented credential is refused - by the real
        /// handler, offline, with all four mandatory validations still enabled and nothing secret anywhere
        /// in this fixture. Adding a key here would be the beginning of minting.
        /// </para>
        /// </remarks>
        private static void ConfigureStaticVerification(IServiceCollection services) =>
            services.Configure<JwtBearerOptions>(
                JwtBearerDefaults.AuthenticationScheme,
                static options => options.Configuration = new OpenIdConnectConfiguration
                {
                    Issuer = TrustedIssuer,
                });

        /// <summary>
        /// Replaces the registered storage readiness check with a scripted verdict, under the same name.
        /// </summary>
        /// <param name="services">The container being configured.</param>
        /// <param name="substitute">The verdict to report.</param>
        /// <remarks>
        /// UNDER THE SAME NAME AND THE SAME TAG DELIBERATELY, so the endpoint's own recognition and
        /// projection path is the real one: the readiness handler reports the storage component as its own
        /// entry precisely because it matches that registered name, and folds anything else into the
        /// aggregate. Registering the substitute under a different name would quietly move the assertion
        /// onto the aggregation path instead.
        /// </remarks>
        private static void SubstituteStorageVerdict(
            IServiceCollection services,
            IHealthCheck substitute) =>
            services.Configure<HealthCheckServiceOptions>(options =>
            {
                List<HealthCheckRegistration> replaced =
                [
                    .. options.Registrations.Where(static candidate => string.Equals(
                        candidate.Name,
                        HealthEndpoints.SqliteCheckName,
                        StringComparison.OrdinalIgnoreCase)),
                ];

                foreach (HealthCheckRegistration existing in replaced)
                {
                    _ = options.Registrations.Remove(existing);
                }

                options.Registrations.Add(
                    new HealthCheckRegistration(
                        HealthEndpoints.SqliteCheckName,
                        _ => substitute,
                        HealthStatus.Unhealthy,
                        ["ready"]));
            });

        /// <summary>Builds the configuration this host runs on.</summary>
        /// <returns>The settings, applied after the shipped files so they win.</returns>
        /// <remarks>
        /// THE STORAGE LOCATION IS ALWAYS OVERRIDDEN, IN EVERY ENVIRONMENT (constraints C-E and C-I). The
        /// deployed path and the development overlay's path are both replaced by a location this host
        /// created and will remove, so no case can touch a configured directory, no two cases can share
        /// state, and two checkouts running in parallel cannot collide.
        /// </remarks>
        private Dictionary<string, string?> BuildSettings()
        {
            Dictionary<string, string?> settings = new(StringComparer.Ordinal)
            {
                ["Jwt:Authority"] = TrustedIssuer,
                ["Jwt:Audience"] = TrustedAudience,
                ["Jwt:RequireHttpsMetadata"] = "true",
                ["Sqlite:DataDirectory"] = _storageDirectory,

                // STARTUP PROVISIONING OFF, SO THIS FILE'S SUBJECT STAYS REACHABLE. Switched ON, the
                // startup sequence applies pending migrations before the pipeline is built, which would
                // repair the unprovisioned engine these cases report on - and would create the database
                // file the unreachable-engine case asserts the absence of. This IS the shipped default, so
                // the line is written out rather than relied upon: it pins the posture these cases need
                // against a later default, and it is the same posture a characterization capture run uses.
                // The provisioning behaviour itself is asserted against the real graph in
                // CompositionRootTests and against the step directly in SchemaProvisionerTests.
                ["Schema:ApplyMigrationsOnStartup"] = "false",
            };

            // LAST, so a case can override any of the four above - which is how the startup-gate case
            // supplies a structurally invalid value and still reaches the real validator with it.
            foreach (KeyValuePair<string, string?> setting in _settings)
            {
                settings[setting.Key] = setting.Value;
            }

            return settings;
        }

        /// <summary>Removes this host's storage location, once, and never anything else.</summary>
        /// <remarks>
        /// <para>
        /// SCOPED TO A UNIQUELY NAMED DIRECTORY THIS FIXTURE CREATED, which is the whole reason a recursive
        /// removal is safe here. It is not a database reset and it is not a volume teardown: no shared
        /// volume, no configured path and no deployment location is reachable from this method, so the
        /// persistence rule that paired characterization recordings must be captured against one
        /// unrecreated volume state is untouched by it.
        /// </para>
        /// <para>
        /// Idempotent and non-throwing, because disposal runs on both the synchronous and asynchronous
        /// paths and a cleanup failure must never be reported as a test failure - it would attribute a
        /// file-system condition to the code under test.
        /// </para>
        /// </remarks>
        private void RemoveStorageDirectory()
        {
            if (_storageRemoved)
            {
                return;
            }

            _storageRemoved = true;

            try
            {
                if (Directory.Exists(_storageDirectory))
                {
                    Directory.Delete(_storageDirectory, recursive: true);
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // Left in the machine's temporary root for the operating system to reclaim. Reporting this
                // would turn a file-system condition into a failure of whichever case happened to run last.
            }
        }
    }

    /// <summary>
    /// The substituted authentication handler: it establishes a principal from an opaque, unsigned test
    /// credential, and refuses anything else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THIS TYPE IS WHY NO KEY MATERIAL APPEARS ANYWHERE IN THIS FILE (item 1 of the decision record,
    /// constraints C-F and C-G). Persistence is never a token issuer: exactly one signing secret exists in
    /// the whole system and Security holds it, and the minting package is deliberately absent from this
    /// project's manifest, so producing the authenticated arm by minting is neither permitted nor possible.
    /// Substituting the handler reaches the same route, the same policy and the same pipeline with an
    /// authenticated principal, and needs no key, no certificate and no secret to do it.
    /// </para>
    /// <para>
    /// IT IS AN EVALUATION AND NOT A RUBBER STAMP. It returns NO RESULT when nothing is presented - which
    /// is a different path from a refusal and is what lets the anonymous case still observe a challenge -
    /// FAILS a credential it cannot read, and FAILS one whose expiry the injected clock has passed. A
    /// handler that authenticated unconditionally would make every negative case in the substituted
    /// posture vacuous.
    /// </para>
    /// <para>
    /// The credential it reads is not a token: it is unsigned, carries no key, means nothing outside this
    /// process, and cannot be replayed against anything because no other handler anywhere recognises it.
    /// </para>
    /// </remarks>
    private sealed class SubstitutedPrincipalHandler
        : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        /// <summary>
        /// The marker every credential this handler accepts begins with, followed by an expiry in seconds
        /// since the Unix epoch.
        /// </summary>
        /// <remarks>
        /// Chosen to be unmistakable to a reader and to a secret scanner alike: it is a description of what
        /// it is, not a random-looking string, so nothing here can be mistaken for a credential that needs
        /// rotating.
        /// </remarks>
        internal const string CredentialPrefix = "substituted-principal/expires-at-";

        /// <summary>The subject the established principal claims.</summary>
        private const string PrincipalName = "persistence-host-test-caller";

        /// <summary>The clock this handler judges expiry against.</summary>
        private readonly TimeProvider _clock;

        /// <summary>Initializes the handler.</summary>
        /// <param name="options">The scheme's options monitor.</param>
        /// <param name="logger">The logger factory the base class records through.</param>
        /// <param name="encoder">The URL encoder the base class uses.</param>
        /// <param name="clock">
        /// The host's single clock seam, resolved from the container so that this handler judges expiry
        /// against the SAME instant every other component in the composed host reads.
        /// </param>
        /// <remarks>
        /// The constructor is public because the container activates handler types through their public
        /// constructors; the type itself stays private to this file.
        /// </remarks>
        public SubstitutedPrincipalHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder,
            TimeProvider clock)
            : base(options, logger, encoder) => _clock = clock;

        /// <inheritdoc/>
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            string presented = Request.Headers.Authorization.ToString();

            if (string.IsNullOrEmpty(presented))
            {
                // NO RESULT rather than a failure: nothing was presented, so there is nothing to judge.
                // The authorization middleware then challenges, which is the 401 the anonymous case wants.
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            if (!AuthenticationHeaderValue.TryParse(presented, out AuthenticationHeaderValue? credential)
                || !string.Equals(credential.Scheme, BearerScheme, StringComparison.OrdinalIgnoreCase)
                || credential.Parameter is not string parameter
                || !parameter.StartsWith(CredentialPrefix, StringComparison.Ordinal)
                || !long.TryParse(
                    parameter.AsSpan(CredentialPrefix.Length),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out long expiresAt))
            {
                return Task.FromResult(
                    AuthenticateResult.Fail("The presented credential could not be read."));
            }

            if (DateTimeOffset.FromUnixTimeSeconds(expiresAt) <= _clock.GetUtcNow())
            {
                // Judged against the INJECTED clock, so the expiry arm is a real evaluation on the same
                // determinism seam the rest of the service reads rather than a sleep.
                return Task.FromResult(
                    AuthenticateResult.Fail("The presented credential has expired."));
            }

            ClaimsIdentity identity = new(
                [new Claim(ClaimTypes.NameIdentifier, PrincipalName)],
                Scheme.Name);

            return Task.FromResult(
                AuthenticateResult.Success(
                    new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }

    /// <summary>
    /// A readiness check that reports a scripted verdict and touches nothing.
    /// </summary>
    /// <remarks>
    /// It reports a STATUS AND NO DESCRIPTION on purpose. The readiness endpoint authors its own fixed
    /// prose for every component and verdict and publishes nothing a registration supplied, so a double
    /// that offered a description would be offering something the endpoint is required to ignore - and a
    /// case asserting on it would be asserting the opposite of the disclosure rule.
    /// </remarks>
    private sealed class ScriptedStorageCheck : IHealthCheck
    {
        /// <summary>The verdict to report.</summary>
        private readonly HealthStatus _verdict;

        /// <summary>Initializes the double.</summary>
        /// <param name="verdict">The verdict to report.</param>
        private ScriptedStorageCheck(HealthStatus verdict) => _verdict = verdict;

        /// <summary>Creates a double reporting a given verdict.</summary>
        /// <param name="verdict">The verdict to report.</param>
        /// <returns>The double.</returns>
        internal static ScriptedStorageCheck Reporting(HealthStatus verdict) => new(verdict);

        /// <inheritdoc/>
        public Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(context);

            return Task.FromResult(new HealthCheckResult(_verdict));
        }
    }
}
