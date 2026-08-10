// ==================================================================================================
//  HealthEndpoints - GET /health ON THE PERSISTENCE SERVICE, ANONYMOUS, AND THE SQLITE PROBE BEHIND IT
//  ------------------------------------------------------------------------------------------------
//  SUBJECT
//  The readiness half of contract C-10 as this LEAF service publishes it, together with the one
//  component check that gives the verdict meaning. Persistence is the ONLY service in the system that
//  holds a storage provider, and the Compose `depends_on: condition: service_healthy` chain gates
//  Gateway on this route - so this small file is the most operationally load bearing one in the
//  service. Every decision in it is recorded below, because the negatives matter more than the
//  positives: getting a positive slightly wrong yields a mildly imperfect probe, whereas getting the
//  central negative wrong deadlocks the whole local orchestration path.
//
//  RULES POSITION
//  No user rules were provided for this project. The rules document contains exactly one line saying
//  so, and re-reading it returns the same. Nothing is invented or back filled from convention in their
//  place: the binding constraints are the enterprise standard baseline - nullable enabled, warnings as
//  errors, no secret in source, structured logging, a genuinely testable design - plus the named non
//  rule constraints C-A through C-L, each cited below at the point it applies, as C-K requires.
//
//  ================================================================================================
//  THE DECISION RECORD
//  ================================================================================================
//
//   1. WHY IT IS ANONYMOUS, AND WHY THAT IS WRITTEN OUT RATHER THAN LEFT IMPLICIT (constraint C-G).
//      The thing that probes a readiness route - a container orchestrator, a load balancer, an
//      operator - holds no token, and it probes precisely during the window in which the service is
//      still starting. Requiring a token here would make readiness depend on Security's issuance being
//      already live, a circular dependency that cannot resolve during a cold start. `/health` is
//      therefore anonymous on all four services, and that uniformity is part of contract C-10 rather
//      than a per service choice.
//      AllowAnonymous is written EXPLICITLY because Program.cs installs a fallback authorization
//      policy requiring an authenticated user: omitting a policy here would CLOSE this route, not open
//      it, and the readiness gate could then never satisfy. This is the SINGLE anonymous exception on
//      this service; nothing here widens it, adds a second one, or touches an authentication scheme or
//      authorization policy - Program.cs owns the stock bearer registration and the fallback policy.
//
//   2. WHY THE BODY DISCLOSES NOTHING, AND WHY THE VOCABULARY IS CLOSED HERE RATHER THAN TRUSTED
//      (constraints C-F and C-G). Because the route is anonymous, its body is world readable by
//      anything that can reach port 5101, so everything it reports is public. It therefore carries an
//      overall status token, this service's own name, the time the report was produced, and a CLOSED
//      THREE MEMBER VOCABULARY of component identifiers - `self`, `sqlite` and `components` - each with
//      a coarse verdict and FIXED PROSE authored in this file.
//      EVERY VALUE IN THE BODY IS AUTHORED HERE. NOTHING A REGISTRATION SUPPLIED REACHES IT. A check's
//      Name and Description are chosen by whatever registered it rather than authored for disclosure,
//      so a name like `npgsql-primary-eu-west-1` or a description carrying a provider name, a host, a
//      file path, an exception summary or a configuration hint would otherwise be published to anything
//      able to reach the port. Requiring registrations to stay clean is a rule on their authors, which
//      this file cannot enforce; a closed vocabulary declared here is enforceable. The shared framework
//      exposes Exception, Data and Duration on every entry and all three are dropped: the first two can
//      carry internal detail, and the third is a timing figure this refactor may make no claim about,
//      because the repository publishes no latency, throughput or availability objective anywhere.
//      Checks other than `sqlite` are aggregated into ONE `components` entry rather than reported
//      individually, because the COUNT of registered checks is itself the shape of this service's
//      dependency graph - the same disclosure in a different unit.
//      Specifically absent, in the body, in a header and in every log record this file writes: any
//      field of transactiondata [ws_objects/pfw.thread.ext.pbl.src/transactiondata.srs] - dbms,
//      servername, database, logid, logpass, dbparm, lock, autocommit, userparm, of which logpass is a
//      credential and write only by contract; any field of dberrordata [.../dberrordata.srs] -
//      sqldbcode, sqlerrtext, sqlsyntax, buffer, row, of which sqlsyntax carries the complete generated
//      statement including interpolated literal values while the legacy logger redacts nothing; the
//      data directory, database file name, composed URI or whether a password is configured; Security's
//      authority, audience or key set path; and any assembly version, framework version, host name,
//      container id or topology detail.
//      The legacy models the right instinct at
//      [ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru:L118], whose comment above the
//      assignment that follows it reads "erase parameters unrelated to the connection target" - scrub
//      what does not belong before use.
//      THE DETAIL IS NOT LOST. Each check's name and verdict, and the numeric provider code behind a
//      failure, go to the OPERATOR channel, whose audience is authenticated by having access to this
//      service's logs rather than by being able to reach a port. Even there the free text members are
//      excluded, because a log is not exempt from C-F merely because its reader is trusted.
//
//   3. WHY IT AGGREGATES NO UPSTREAM. THIS IS NOT AN OVERSIGHT. DO NOT "FIX" IT.
//      Persistence is the BOTTOM of the layered, acyclic topology: Gateway calls DataServices and
//      Security, DataServices calls Persistence and Security, and Persistence calls nothing at all -
//      it reads Security's published verification material and no more. Contract C-10 assigns upstream
//      aggregation to GATEWAY ALONE, which reports healthy only after Persistence, DataServices and
//      Security each do, expressed in the Compose manifest as a health condition.
//      Adding an upstream check here would do two things, and the second is fatal: it would INVERT THE
//      TOPOLOGY, and it would DEADLOCK THE READINESS CHAIN, because Gateway is waiting on this very
//      endpoint while this endpoint would be waiting on Gateway's peers. So this file opens no
//      HttpClient, holds no typed client, creates no gRPC channel and injects no upstream reference of
//      any kind; the Clients concept does not appear in this project at all. It probes LOCAL RESOURCES
//      ONLY.
//      THE KEY SET NUANCE, stated so it is not mistaken for a licence. This service does fetch
//      Security's verification material for inbound bearer validation, and that fetch is LAZY on first
//      use, which is what keeps this host startable while Security is still coming up. A brief
//      unavailability of that material must not crash loop the service and must not make `/health`
//      report unhealthy - the service starts and reports its own local state. That is about metadata
//      retrieval TIMING only. It is emphatically not permission to skip, relax or conditionally disable
//      token validation anywhere, and no key set reachability check appears in this probe.
//
//   4. WHY THE PROBE TOUCHES STORAGE FOR REAL, AND WHY IT TOUCHES NOTHING ELSE.
//      A liveness check that never reached storage would report healthy while the only storage provider
//      in the entire system was unreachable, which is precisely the state the gate Gateway hangs on
//      exists to detect. So the probe goes through Data/SqliteConnectionFactory.cs - the single seam in
//      the system that opens a connection - and calls its reachability member, which executes one
//      constant, parameterless statement so that the ENGINE is proven to answer rather than merely a
//      file handle proven to open, and which reads no schema so it works against an empty database as
//      well as a populated one.
//      It composes no URI, constructs no connection, and reads no configuration value of its own. URI
//      composition is the factory's declared and matrix tested responsibility, and duplicating it would
//      create two grammars that could drift (C-F). It resolves the seam through DI and never through a
//      static or global instance: the legacy's `global n_sqlite n_sqlite`
//      [ws_objects/pfw.utility.sqlite.pbl.src/n_sqlite.sru:L90] is a global auto instance shadowing its
//      own type name, and the collision resolution rule turns the INSTANCE into an injected dependency.
//      SQLITE AND ONLY SQLITE (constraint C-E). No SQL Server and no Oracle is contacted, because
//      neither is provisioned and neither has a schema, a connection string or any DDL anywhere in the
//      repository. The legacy transaction layer declares exactly two database types, DBT_MSSQL = 0 and
//      DBT_ORACLE = 1 [ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:L60-L61], and SQLITE IS
//      ABSENT FROM THAT ENUMERATION ENTIRELY; those two survive only as pure string paging rewriters
//      under Sql/Paging/ that need no instance of either engine.
//
//   5. WHY THE PROBE IS STRICTLY READ ONLY. THIS IS A HARD CONSTRAINT, NOT A PREFERENCE.
//      The design is legacy anchored rather than invented: n_sqlite.sru itself separates read only
//      reachability - Copyright :L9, GetVersion :L10, GetTimeout :L12, IsAutoCommit :L14, IsOpened :L16,
//      SQLNRows :L26, SQLCode :L27, SQLDBCode :L28, SQLErrText :L29 and IsTableExists :L30-L31 - from
//      mutation, which is SetCancelEvent :L11, SetTimeout :L13, SetAutoCommit :L15, Open :L17-L18,
//      Close :L19, LoadExtension :L20-L21, AutoCommit :L22, Commit :L23-L24, Rollback :L25 and the
//      Exec, Query and Update arities at :L32-L87. This probe reaches only the reachability shape.
//      Nowhere in this file: EnsureCreated, EnsureDeleted, Migrate, a file deletion, any DDL or DML
//      statement, an auto commit toggle, a commit, a rollback, an extension load, or a seed or reseed
//      path of any kind.
//      WHY IT IS ABSOLUTE. For one workflow identifier the legacy side and target side characterization
//      recordings must be captured against the SAME `persistence-db` volume state, with the volume
//      neither recreated nor reseeded between them, or the paired recordings are not comparable at all.
//      A probe that "ensured" the schema would violate that on EVERY SINGLE POLL - and because the
//      Compose health condition polls continuously, that is a high frequency violation rather than an
//      edge case. The end to end suite corroborates the shared state fact independently by running one
//      worker and disabling parallelism, because the suite mutates shared COMPANY state in one volume.
//      THE ORACLE SHOWS THE EXACT ACT TO AVOID. [ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L448]
//      guards non destructively with `if sqlitedb.IsOpened() then return`, and then :L450 performs
//      `FileDelete("test.db")` before opening at :L456 and creating the table at :L463-L469. Line 450 is
//      test harness setup, it reads like part of the open sequence, and it is precisely what must never
//      appear on a probe path.
//      NO INTEGRITY PRAGMA EITHER. `check[=quick]` is documented at
//      [w_test_sqlite.srw:L452-L455] and is read only, so running it would not breach the volume rule -
//      but Data/SqliteConnectionFactory.cs already owns translating that URI extension into the
//      appropriate pragma AT CONNECTION OPEN TIME. Re-running it per poll would duplicate a declared
//      responsibility and impose a full database read on every probe.
//
//   6. WHY THE HANDLER AND THE CHECK BOTH REPORT RATHER THAN THROW, AND HOW THAT IS NOT A SOFTENING OF
//      FAIL FAST (constraint C-J). The framework's fail fast posture is real and is preserved: a
//      structural fault ends the process rather than degrading past it, which is what
//      [ws_objects/pfw.pbl.src/pfw.sra:L111-L144] does when it guards on an assert payload at :L114,
//      splits it on a carriage return line feed pair into up to seven fields at :L115-L126, composes a
//      diagnostic at :L129-L139 and then executes HALT CLOSE at :L143. That posture governs STARTUP
//      VALIDATION and STRUCTURAL FAULTS. It does NOT govern the health verdict.
//      A probe exists to REPORT a runtime condition. If it threw or terminated on unreachable storage,
//      the gate could never observe a structured unhealthy state and Gateway's aggregator would see a
//      transport error instead of a verdict - so returning Unhealthy is not a softening, it IS the C-10
//      contract. Every failure path here is therefore caught, logged without disclosure, and reported.
//      Not one line of pfw.sra is reproduced in this file.
//      TWO DELIBERATE EXCEPTIONS. OperationCanceledException raised by the CALLER disconnecting is
//      allowed to propagate, because there is no longer a response to write and swallowing it would only
//      produce a second failure while trying. And the null argument guards on the two extension methods
//      throw, because a null route builder or service collection is a programming error at composition
//      time rather than a request time condition.
//
//   7. WHY ONLY `Healthy` IS ANSWERED WITH 200, DEGRADED INCLUDED IN THE 503.
//      The three tokens stay distinct in the BODY - a service still completing startup validation is
//      not the same as one whose dependency has failed, and an operator needs to tell them apart - but
//      the readiness gate observes the status CODE. A not ready service answering 200 would let
//      `depends_on: condition: service_healthy` open on a service that had just said it was not ready,
//      which is the exact ordering property the gate exists to enforce. The test is therefore FOR the
//      single ready state rather than against the failed ones, which is what also makes a status value
//      this build does not recognise fail closed.
//
//   8. WHY THE CLOCK IS INJECTED AND THE PROBE IS BOUNDED.
//      Every clock read is a determinism seam that a characterization run has to be able to mask from
//      BOTH the master and the candidate recording, so the timestamp this endpoint emits comes from the
//      injected TimeProvider and never from an ambient clock, and the liveness window the probe passes
//      to the storage seam is measured on that same injected clock. There is no DateTime.UtcNow, no
//      DateTime.Now, no DateTimeOffset.UtcNow, no Stopwatch and no tick count anywhere in this file.
//      The probe is bounded because a probe that HANGS stalls the gate exactly as effectively as one
//      that fails: a storage call that never answers is reported unhealthy when the budget expires
//      rather than holding the request open until the orchestrator's own probe gives up, which would
//      make the gate read a timeout instead of a verdict.
//
//   9. WHAT IS NOT HERE, EACH OMISSION A CONSTRAINT DISCHARGED (C-K).
//       * NO SECOND ROUTE (C-B). One pattern, `/health`, spelled exactly that way. No `/healthz`, no
//         liveness and readiness split, no alias, no versioned variant, no metrics route, no info
//         route, no diagnostics dump and no admin surface. An operational contract is not a feature,
//         and surface the brief does not name does not go here.
//       * NO HOST FILTER AND NO LOOPBACK ASSUMPTION (C-J). The route must be reachable on the
//         container's bound interface on port 5101, because the Compose health condition polls it from
//         outside the container. The listener itself - the port, the plaintext endpoint and the protocol
//         set that lets one address carry HTTP/2 gRPC and HTTP/1.1 REST together - belongs to Program.cs
//         and the settings file and is not restated here.
//       * NO OPENAPI DOCUMENT GENERATION. This service's published contract is protocol buffer shaped
//         and the project references no OpenAPI package, so there is nothing to attach a document
//         generator to. The route metadata below is descriptive only.
//       * NO PERSISTENCE WIRE YAML IS REFERENCED, BECAUSE NONE EXISTS. Unlike every Gateway endpoint,
//         this route has no authoritative document behind it: the contracts project packages exactly two
//         OpenAPI documents, for Gateway and for Security. The authority for this endpoint is the
//         architecture's endpoint contract, contract C-10 and the attached environment's readiness gate,
//         and nothing else.
//       * NO DEFERRED SERVICE APPEARS IN ANY FORM (C-D). No reserved route, no named capability area,
//         no placeholder class and no not implemented exception. The four reserved routes are
//         declarations on GATEWAY's routing table alone.
//       * NO NEW PACKAGE REFERENCE. Health check registration ships in the Microsoft.AspNetCore.App
//         shared framework, so nothing is added to the project file and no community health check or
//         URI probe package is used.
//       * NO PRESERVED SPELLING IDENTIFIER IS DECLARED. The repository .editorconfig scopes its naming
//         analyzer relaxations to individually named files, of which the only two in this project are
//         under Sql/; Endpoints/ is outside every one of them and warnings are errors. The one legacy
//         return code this file needs is CONSUMED symbolically from the shared kernel.
// ==================================================================================================

using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using PowerFramework.Persistence.Data;
using PowerFramework.Shared.Kernel;

namespace PowerFramework.Persistence.Endpoints;

/// <summary>
/// Declares this service's anonymous readiness probe and the single storage component check behind it.
/// </summary>
/// <remarks>
/// <para>
/// The route pattern, the anonymity, the declared responses, the storage probe and the projection onto
/// the wire shape all live here, so that a reader comparing the running service against contract C-10
/// has one file to read. <c>Program.cs</c> owns the host - the authentication scheme, the fallback
/// authorization policy, the listener and the middleware order - and deliberately owns nothing about
/// this route beyond the two calls it makes into this class.
/// </para>
/// <para>
/// Read the decision record at the head of this file before changing anything in it. In particular,
/// items 3 and 5 record two changes that look like improvements and are not: adding an upstream check
/// inverts the topology and deadlocks the orchestration readiness chain, and adding a schema-ensuring
/// call silently corrupts the characterization evidence model on every poll.
/// </para>
/// </remarks>
public static class HealthEndpoints
{
    // ----------------------------------------------------------------------------------------------
    //  THE FIXED VOCABULARY. Every string a caller can observe is declared here, so the whole public
    //  surface of an anonymous route is readable in one place (decision record item 2). None is a
    //  configuration value, none is interpolated from one, and none is a preserved legacy spelling -
    //  Endpoints/ is outside every scoped naming relaxation in the repository .editorconfig.
    // ----------------------------------------------------------------------------------------------

    /// <summary>The probe's route, fixed by the attached environment's readiness gate (constraint C-L).</summary>
    private const string HealthRoutePattern = "/health";

    /// <summary>
    /// This service's canonical name, as it appears in this report and in Gateway's aggregate.
    /// </summary>
    /// <remarks>
    /// Gateway's aggregate closes its reporting-service name over <c>persistence</c>,
    /// <c>dataservices</c> and <c>security</c>, so this spelling is a contract rather than a label.
    /// </remarks>
    private const string ServiceIdentifier = "persistence";

    /// <summary>The ready verdict, and the only one answered with 200 (decision record item 7).</summary>
    private const string StatusHealthy = "Healthy";

    /// <summary>The not-ready-but-not-failed verdict. Answered with 503, distinguished in the body.</summary>
    private const string StatusDegraded = "Degraded";

    /// <summary>The failed verdict, answered with 503.</summary>
    private const string StatusUnhealthy = "Unhealthy";

    /// <summary>
    /// The first member of the closed public component vocabulary: this endpoint's own statement that
    /// the process is running and answering requests.
    /// </summary>
    private const string SelfCheckName = "self";

    /// <summary>
    /// The second member: the storage engine's reachability. It is a fixed identifier authored in this
    /// file, naming a capability the architecture already publishes, and it carries no location,
    /// provider version, file name or credential of any kind.
    /// </summary>
    /// <remarks>
    /// Doubles as the REGISTRATION name of <see cref="SqliteReachabilityHealthCheck"/>, which is what
    /// lets <see cref="ProjectChecks"/> recognise that check by name and report it as its own entry
    /// while every other registration is folded into <see cref="ComponentsCheckName"/>. A test that
    /// substitutes the storage verdict does so by re-registering this same name.
    /// </remarks>
    internal const string SqliteCheckName = "sqlite";

    /// <summary>
    /// The third member: one aggregated entry standing for every OTHER registered check, so that
    /// neither a registration's chosen name nor the number of registrations reaches an unauthenticated
    /// caller (decision record item 2).
    /// </summary>
    private const string ComponentsCheckName = "components";

    /// <summary>
    /// The tag the storage check is registered under, so that a future filtered evaluation can select
    /// the readiness set without this file having to change.
    /// </summary>
    private const string ReadinessTag = "ready";

    /// <summary>The operation identifier.</summary>
    private const string OperationName = "getHealth";

    /// <summary>The tag the operation is grouped under.</summary>
    private const string OperationTag = "Health";

    /// <summary>The single extension member the problem document is permitted to carry.</summary>
    private const string RetCodeExtensionMember = "retCode";

    /// <summary>The problem title for the not-ready verdict.</summary>
    private const string UnavailableProblemTitle = "Service Unavailable";

    /// <summary>The logger category every record written from this file is filed under.</summary>
    /// <remarks>
    /// Stated as a literal rather than derived from a type name so that the category an operator
    /// filters on is stable across a rename, and so that the handler - which is static and has no
    /// generic logger of its own - files its records in the same place as the check does.
    /// </remarks>
    private const string LoggerCategoryName = "PowerFramework.Persistence.Endpoints.HealthEndpoints";

    /// <summary>The fixed prose for the <c>self</c> entry when the process is answering.</summary>
    private const string SelfHealthyDescription =
        "The service process is running and answering requests.";

    /// <summary>
    /// The fixed prose for the <c>self</c> entry when the readiness evaluation itself could not be
    /// completed. It states THAT the evaluation failed and never WHY: the reason is an exception, the
    /// response is anonymous, and the two must not meet. The reason goes to the operator log.
    /// </summary>
    private const string SelfUnhealthyDescription =
        "The readiness evaluation could not be completed.";

    /// <summary>The fixed prose for a reachable storage engine.</summary>
    private const string SqliteHealthyDescription =
        "The storage engine answered a read-only reachability probe.";

    /// <summary>
    /// The fixed prose for an unreachable storage engine. It names no path, no provider, no error text
    /// and no configuration value; the numeric provider code is on the operator channel instead.
    /// </summary>
    private const string SqliteNotReadyDescription =
        "The storage engine did not answer a read-only reachability probe.";

    /// <summary>The fixed prose for the aggregated <c>components</c> entry when every other check is ready.</summary>
    private const string ComponentsHealthyDescription =
        "Every other registered readiness check reports ready.";

    /// <summary>The fixed prose for the aggregated <c>components</c> entry when any other check is not ready.</summary>
    private const string ComponentsNotReadyDescription =
        "At least one other registered readiness check does not report ready.";

    /// <summary>
    /// The operator-channel message template for a completed readiness evaluation.
    /// </summary>
    /// <remarks>
    /// A single template used at two levels, so that the structured field set of a healthy evaluation
    /// and of a failed one are identical and one query serves both.
    /// </remarks>
    private const string OperatorReadinessRecord =
        "Readiness for the {Service} service is {Status}. Checks: {Checks}.";

    /// <summary>The operation summary published on the route.</summary>
    private const string OperationSummary = "Report this service's own readiness. Anonymous.";

    /// <summary>
    /// The description published on the route. It restates the five properties a consumer has to know -
    /// anonymous deliberately, never an aggregate, storage really is probed, the single 200 verdict, and
    /// the disclosure boundary - and asserts nothing about how quickly or how often the endpoint
    /// answers, because no such objective is published anywhere in the repository.
    /// </summary>
    private const string OperationDescription = """
        Reports the readiness of **this service only** (contract C-10, readiness half).

        **Anonymous deliberately, for a mechanical reason.** A readiness probe must be reachable before
        any token exists. The caller that probes it - a container orchestrator, a load balancer, an
        operator - holds no token, and it probes precisely during the window in which this service is
        still starting. Requiring a token here would make readiness depend on Security's token issuance
        being already live, a circular dependency that cannot resolve during a cold start. `/health` is
        anonymous on all four services, and that uniformity is part of contract C-10 rather than a per
        service choice. Every other operation on this service requires a bearer token.

        **Never an aggregate.** Persistence is the bottom of the layered, acyclic topology: it is called
        by DataServices, it calls no service at all, and it reads only Security's published verification
        material. Gateway is the single aggregator, reporting healthy only after Persistence,
        DataServices and Security each report healthy, with the ordering expressed in the orchestration
        manifest as a dependency condition on upstream health. This endpoint therefore opens no channel
        to any service and probes local resources only.

        **The storage engine really is probed, read only.** The verdict covers reachability of the one
        storage engine this service holds, established by executing a single constant, parameterless
        statement so that the engine is proven to answer rather than merely a file handle proven to
        open. The probe reads no schema, so it behaves identically against an empty database and a
        populated one, and it creates, deletes, migrates, seeds and modifies nothing - a probe that
        altered stored state would invalidate the paired characterization recordings on every poll.

        **Only `Healthy` is answered with 200.** `Degraded` means *not ready* and is answered with 503
        alongside `Unhealthy`, because the readiness gate observes the status code and a not-ready
        service answering 200 would open the dependency gate early. The `status` member keeps all three
        tokens, so an operator can still tell a service completing its startup validation from one whose
        storage engine has failed. A 503 carries a problem document naming the not-ready component and
        the legacy PowerFramework return code.

        **The body is public, and is scoped accordingly.** Every value in it is authored by this
        service: the component identifiers are a closed vocabulary - `self`, `sqlite` and `components` -
        rather than the names whatever registered a check chose for it, and all other checks are
        aggregated into one entry so the number of them is not disclosed either. The body carries no
        data directory, database file name, connection URI, credential, certificate, configuration
        value, host name, other service's port, provider error text, statement text or stack trace. The
        per-component detail is available to an operator through this service's own telemetry.
        """;

    // ==============================================================================================
    //  REGISTRATION - THE SINGLE CALL SITE Program.cs MAKES
    // ==============================================================================================

    /// <summary>
    /// Registers the readiness evaluation and the one storage component check behind it.
    /// </summary>
    /// <param name="services">The service collection the host is populating.</param>
    /// <returns>
    /// The same <paramref name="services"/> instance, so that registrations may be chained.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// ONE CALL SITE, DELIBERATELY. <c>Program.cs</c> calls this and nothing else about readiness, so
    /// the whole of what <c>/health</c> evaluates is declared in this file next to the route that
    /// publishes it.
    /// </para>
    /// <para>
    /// IT IS IDEMPOTENT, AND THAT IS LOAD BEARING RATHER THAN POLITE. The shared framework THROWS while
    /// building its evaluator when two registrations share a name, so a second call to a naively written
    /// version of this method would not merely add a redundant entry - it would make every subsequent
    /// evaluation fault, turning <c>/health</c> into a 503 the moment anything called the extension
    /// twice. The registration is therefore added through an explicit guarded configure action rather
    /// than through the builder's own unguarded add, and the two seams are try-added. The whole method
    /// can be called any number of times and the resulting registry is identical.
    /// </para>
    /// <para>
    /// WHY THE TWO SEAMS ARE TRY-ADDED RATHER THAN ADDED. The composition root owns composition, and a
    /// try-add yields to it: if <c>Program.cs</c> - or a later test host - registers the clock or the
    /// storage seam itself, that registration wins and this method changes nothing. Registering them
    /// here as well is what makes this extension safe to call on a bare service collection, which is
    /// how the readiness tests drive it, and it means the probe can never be registered without the
    /// seam it probes. Neither line reads a configuration value: the storage seam takes its own
    /// settings through the options pattern, so every configuration value still arrives through
    /// <c>Configuration/PersistenceOptions.cs</c> and none through this file (constraint C-F).
    /// </para>
    /// <para>
    /// NO PACKAGE IS REQUIRED FOR ANY OF THIS. <see cref="HealthCheckService"/> and its registration
    /// builder ship inside the <c>Microsoft.AspNetCore.App</c> shared framework, which is why the
    /// project file carries no health check package reference and why no community health check or URI
    /// probe package appears anywhere in this refactor.
    /// </para>
    /// <para>
    /// THE FAILURE STATUS IS STATED RATHER THAN DEFAULTED. It is already the framework's default, and
    /// writing it out makes the fail-closed intent of decision record item 7 explicit at the
    /// registration as well as at the status-code mapping: unreachable storage on the only service that
    /// holds any is a failure, never a degradation.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddPersistenceHealthChecks(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // The determinism seam. Every clock read in this service is substitutable, because a
        // characterization run has to be able to mask non-deterministic values from both the master and
        // the candidate recording (decision record item 8).
        services.TryAddSingleton(TimeProvider.System);

        // The ONE storage seam in the system, and the only thing this probe touches. A singleton for the
        // process lifetime, the way the legacy global auto-instance at n_sqlite.sru:L90 was - but
        // injected rather than global, which is the whole point of the substitution.
        services.TryAddSingleton<SqliteConnectionFactory>();

        // Registers the evaluator and its infrastructure. Called for that effect only; the registration
        // itself is added below so it can be guarded.
        services.AddHealthChecks();

        services.Configure<HealthCheckServiceOptions>(static options =>
        {
            foreach (HealthCheckRegistration existing in options.Registrations)
            {
                // Already registered - by an earlier call to this method, or by a host or test that
                // deliberately substituted the storage verdict under this same name. Either way the
                // caller's registration stands: adding a second one under the same name would make the
                // framework fault while building its evaluator, and a probe that faults is worse than a
                // probe that reports.
                if (string.Equals(existing.Name, SqliteCheckName, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            options.Registrations.Add(
                new HealthCheckRegistration(
                    SqliteCheckName,

                    // The same activation the framework's own typed registration uses: resolve the check
                    // from the container when it is registered there, otherwise construct it with its
                    // dependencies injected. Nothing is resolved statically or globally.
                    static provider =>
                        ActivatorUtilities.GetServiceOrCreateInstance<SqliteReachabilityHealthCheck>(
                            provider),
                    failureStatus: HealthStatus.Unhealthy,
                    tags: [ReadinessTag]));
        });

        return services;
    }

    // ==============================================================================================
    //  THE ROUTE
    // ==============================================================================================

    /// <summary>
    /// Maps <c>GET /health</c> onto the supplied route builder as an anonymous readiness probe.
    /// </summary>
    /// <param name="app">
    /// The route builder to map onto. Supplied by <c>Program.cs</c>, which owns the host and calls this
    /// after the authentication and authorization middleware is in place.
    /// </param>
    /// <returns>
    /// The same <paramref name="app"/> instance, so that endpoint registrations may be chained.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="app"/> is <see langword="null"/>. A null route builder is a programming error at
    /// composition time rather than a request-time condition, which is why it throws while the request
    /// handler itself never does (decision record item 6).
    /// </exception>
    /// <remarks>
    /// EXACTLY ONE ROUTE. There is no <c>/healthz</c>, no liveness and readiness split, no alias and no
    /// versioned variant, and there is no host restriction of any kind - the Compose health condition
    /// polls this route from OUTSIDE the container, so a host filter or a loopback binding would make
    /// the readiness gate unsatisfiable (constraints C-B and C-J).
    /// </remarks>
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet(HealthRoutePattern, ReportReadinessAsync)
           // Explicit, and load bearing. Program.cs installs a fallback policy requiring an
           // authenticated user, so merely omitting a policy here would CLOSE this route and the
           // readiness gate could never satisfy. This is the one documented anonymous exception on this
           // service (decision record item 1).
           .AllowAnonymous()
           .WithName(OperationName)
           .WithTags(OperationTag)
           .WithSummary(OperationSummary)
           .WithDescription(OperationDescription)
           // Exactly two declared responses: the report on 200 and a problem document on 503. The
           // handler returns IResult rather than a typed result union deliberately - a union would have
           // the framework infer an additional 500 from the problem result's own metadata and publish a
           // response this operation cannot produce, since every failure path is caught and reported.
           .Produces<ServiceHealthReport>(StatusCodes.Status200OK)
           .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return app;
    }

    /// <summary>
    /// Evaluates this process's own readiness and projects it onto the wire.
    /// </summary>
    /// <param name="httpContext">
    /// The request context, used only to reach the request-scoped service provider. Nothing about the
    /// request influences the verdict, and nothing about the request is echoed back.
    /// </param>
    /// <param name="loggerFactory">
    /// The host's logging abstraction. Used exclusively for the operator-channel records, which is
    /// where the per-component detail belongs because the anonymous body may not carry it.
    /// </param>
    /// <param name="cancellationToken">
    /// The request's abort token, so a disconnected caller stops the evaluation rather than leaving it
    /// running against a response nobody will read.
    /// </param>
    /// <returns>
    /// 200 carrying a <see cref="ServiceHealthReport"/> when the verdict is <c>Healthy</c>; 503 carrying
    /// a problem document for <c>Degraded</c>, <c>Unhealthy</c> or any status this build does not
    /// recognise (decision record item 7).
    /// </returns>
    private static async Task<IResult> ReportReadinessAsync(
        HttpContext httpContext,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        HealthStatus status;
        IReadOnlyList<ServiceHealthCheck> checks;

        try
        {
            // OPTIONAL resolution, deliberately. The framework's own MapHealthChecks resolves this
            // service as REQUIRED and throws while the endpoint is being built if no registration ever
            // happened, which would turn a probe into a startup gate. Resolving it optionally means the
            // route answers truthfully whether or not any component check is registered - and it
            // aggregates every check that IS registered, so a check added later is reported with no
            // change here.
            HealthCheckService? healthCheckService =
                httpContext.RequestServices.GetService<HealthCheckService>();

            if (healthCheckService is null)
            {
                // No component check is registered at all, so the whole of this service's readiness is
                // the statement that its process is running and answering requests - which the
                // execution of this handler has just demonstrated. A truthful Healthy, not an assumed
                // one. In the composed service this branch is unreachable, because
                // AddPersistenceHealthChecks always registers the storage check; it exists so that a
                // host assembled without it still answers rather than faulting.
                status = HealthStatus.Healthy;
                checks = [BuildSelfCheck(HealthStatus.Healthy)];
            }
            else
            {
                HealthReport report = await healthCheckService
                    .CheckHealthAsync(cancellationToken)
                    .ConfigureAwait(false);

                status = report.Status;
                checks = ProjectChecks(report);

                // THE OPERATOR CHANNEL, WHICH IS WHERE THE DETAIL BELONGS (decision record item 2). The
                // response above carries only this file's own closed vocabulary, so the
                // registration-supplied check names - the thing an operator actually needs in order to
                // find a failing component - would otherwise exist nowhere.
                WriteOperatorRecord(loggerFactory, report);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Decision record item 6: the handler REPORTS rather than throws, because a probe that
            // faulted would take the container with it and the readiness gate could never recover. The
            // exception is logged HERE, where an operator can see it, and is deliberately NOT projected
            // onto the anonymous response. OperationCanceledException is excluded on purpose: it means
            // the caller disconnected, so there is no longer a response to write.
            loggerFactory
                .CreateLogger(LoggerCategoryName)
                .LogError(
                    exception,
                    "Readiness evaluation failed for the {Service} service; reporting {Status}.",
                    ServiceIdentifier,
                    StatusUnhealthy);

            status = HealthStatus.Unhealthy;
            checks = [BuildSelfCheck(HealthStatus.Unhealthy)];
        }

        // Fails CLOSED: ONLY Healthy is answered with 200. Testing FOR the single ready state rather
        // than against the failed ones is what makes an unrecognised status fail closed as well
        // (decision record item 7).
        if (status is not HealthStatus.Healthy)
        {
            return BuildNotReadyProblem(status, checks);
        }

        // The clock is a substitutable seam rather than a direct read (decision record item 8).
        // Resolving it optionally honours a deterministic double when the host registers one and needs
        // no registration when it does not.
        TimeProvider timeProvider =
            httpContext.RequestServices.GetService<TimeProvider>() ?? TimeProvider.System;

        return TypedResults.Ok(
            new ServiceHealthReport(
                ToWireStatus(status),
                ServiceIdentifier,
                timeProvider.GetUtcNow(),
                checks));
    }

    // ==============================================================================================
    //  THE PROJECTION - THE ONLY PLACE A VALUE BECOMES PUBLIC
    // ==============================================================================================

    /// <summary>
    /// Projects a shared-framework health report onto the wire shape, using ONLY this file's closed
    /// public component vocabulary.
    /// </summary>
    /// <param name="report">The evaluated report.</param>
    /// <returns>
    /// Between one and three entries, named exclusively from <see cref="SelfCheckName"/>,
    /// <see cref="SqliteCheckName"/> and <see cref="ComponentsCheckName"/>: this endpoint's own
    /// statement that the process is answering, the storage engine's verdict when the storage check is
    /// registered, and one aggregated entry standing for every other registered check when there is at
    /// least one.
    /// </returns>
    /// <remarks>
    /// <para>
    /// NOTHING A REGISTRATION SUPPLIED CROSSES THIS BOUNDARY, AND THAT IS THE WHOLE POINT OF THE METHOD
    /// (decision record item 2). A check's <c>Name</c> and <c>Description</c> are chosen by whatever
    /// registered it rather than authored for disclosure, and this route is anonymous - so a name
    /// carrying a host or a region, or a description carrying a provider name, a file path, an exception
    /// summary or a configuration hint, would be published to anything able to reach the port. The
    /// requirement that check names stay free of internal detail is a rule on the AUTHOR of each
    /// registration, which this file cannot enforce; a closed vocabulary declared here is.
    /// </para>
    /// <para>
    /// The storage entry is recognised BY NAME rather than by position or by index, and its own
    /// description is still not echoed: only its status is read, and the prose is chosen from the two
    /// constants above. That keeps the enforcement structural even for the one check this file registers
    /// itself, and it means a test that substitutes the storage verdict under the same name is reported
    /// through exactly the same path as the real one.
    /// </para>
    /// <para>
    /// Every other check is folded into ONE entry rather than reported individually, because the COUNT
    /// of registered checks is itself the shape of this service's dependency graph. Its status is the
    /// worst across those checks, computed here rather than taken from the report's own aggregate: the
    /// aggregate can be shaped by a registration's result predicate, whereas this entry stands for the
    /// checks themselves and must say what they said.
    /// </para>
    /// <para>
    /// The entry's <c>Exception</c>, <c>Data</c> and <c>Duration</c> members are all dropped - the first
    /// two because they carry internal detail, and the third because a timing figure is a claim this
    /// refactor may not make.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<ServiceHealthCheck> ProjectChecks(HealthReport report)
    {
        List<ServiceHealthCheck> projected = [BuildSelfCheck(HealthStatus.Healthy)];

        HealthStatus? storage = null;
        HealthStatus? othersWorst = null;

        foreach (KeyValuePair<string, HealthReportEntry> entry in report.Entries)
        {
            // The shared framework keys its registry case-insensitively, so the recognition test is
            // case-insensitive too - otherwise a registration spelled `SQLite` would silently fall into
            // the aggregated bucket instead of being reported as the storage entry.
            if (string.Equals(entry.Key, SqliteCheckName, StringComparison.OrdinalIgnoreCase))
            {
                // Worst-wins if the same name were somehow registered twice: the framework forbids it,
                // but a probe must not depend on that guarantee to stay fail-closed.
                storage = storage is HealthStatus known && known < entry.Value.Status
                    ? known
                    : entry.Value.Status;

                continue;
            }

            othersWorst = othersWorst is HealthStatus worst && worst < entry.Value.Status
                ? worst
                : entry.Value.Status;
        }

        if (storage is HealthStatus storageStatus)
        {
            projected.Add(
                new ServiceHealthCheck(SqliteCheckName, ToWireStatus(storageStatus))
                {
                    Description = storageStatus == HealthStatus.Healthy
                        ? SqliteHealthyDescription
                        : SqliteNotReadyDescription,
                });
        }

        if (othersWorst is HealthStatus othersStatus)
        {
            projected.Add(
                new ServiceHealthCheck(ComponentsCheckName, ToWireStatus(othersStatus))
                {
                    Description = othersStatus == HealthStatus.Healthy
                        ? ComponentsHealthyDescription
                        : ComponentsNotReadyDescription,
                });
        }

        return projected;
    }

    /// <summary>
    /// Records the per-check detail on the operator channel, which is the audience it is authored for.
    /// </summary>
    /// <param name="loggerFactory">The host's logging abstraction.</param>
    /// <param name="report">The evaluated report.</param>
    /// <remarks>
    /// <para>
    /// WHY THIS EXISTS: the anonymous response carries a closed vocabulary, so without this record the
    /// one thing an operator needs from a failed probe - WHICH component is not ready - would exist
    /// nowhere. The split is the same one the system applies to an unhandled fault: the caller learns
    /// the class of failure, the operator learns the detail.
    /// </para>
    /// <para>
    /// WHAT IS RECORDED AND WHAT IS NOT. Each check's NAME and STATUS travel; its <c>Description</c>,
    /// <c>Exception</c> and <c>Data</c> do NOT. A name is an identifier a registration chose and is safe
    /// to hold in this service's own logs, whereas a description or an exception message is free text
    /// that routinely carries a connection string, an address or a file path - and a log record is not
    /// exempt from that concern merely because its reader is trusted (constraint C-F). The names are
    /// joined into one field rather than logged per check, so a route polled continuously by an
    /// orchestrator produces one record per evaluation rather than one per component.
    /// </para>
    /// <para>
    /// THE LEVEL IS CHOSEN FROM THE VERDICT, AND THE HEALTHY PATH IS DELIBERATELY QUIET. A not-ready
    /// evaluation is a warning an operator should see; a ready one is trace level, because the Compose
    /// health condition polls continuously and an information-level line per poll would drown the
    /// channel in records saying that nothing happened. The enabled check short-circuits the whole
    /// method so the joined string is not even built when nothing will read it.
    /// </para>
    /// </remarks>
    private static void WriteOperatorRecord(ILoggerFactory loggerFactory, HealthReport report)
    {
        ILogger logger = loggerFactory.CreateLogger(LoggerCategoryName);

        bool ready = report.Status == HealthStatus.Healthy;

        if (!logger.IsEnabled(ready ? LogLevel.Trace : LogLevel.Warning))
        {
            return;
        }

        List<string> named = new(report.Entries.Count);

        foreach (KeyValuePair<string, HealthReportEntry> entry in report.Entries)
        {
            named.Add(string.Concat(entry.Key, "=", ToWireStatus(entry.Value.Status)));
        }

        string detail = string.Join(", ", named);

        if (ready)
        {
            logger.LogTrace(
                OperatorReadinessRecord,
                ServiceIdentifier,
                ToWireStatus(report.Status),
                detail);
        }
        else
        {
            logger.LogWarning(
                OperatorReadinessRecord,
                ServiceIdentifier,
                ToWireStatus(report.Status),
                detail);
        }
    }

    /// <summary>
    /// Builds this endpoint's own contribution to the report.
    /// </summary>
    /// <param name="status">
    /// <see cref="HealthStatus.Healthy"/> when the process is answering requests, or
    /// <see cref="HealthStatus.Unhealthy"/> when the readiness evaluation itself could not be completed.
    /// </param>
    /// <returns>The <c>self</c> entry.</returns>
    private static ServiceHealthCheck BuildSelfCheck(HealthStatus status)
    {
        return new ServiceHealthCheck(SelfCheckName, ToWireStatus(status))
        {
            Description = status == HealthStatus.Healthy
                ? SelfHealthyDescription
                : SelfUnhealthyDescription,
        };
    }

    /// <summary>
    /// Builds the 503 problem document for any verdict that is not fully ready.
    /// </summary>
    /// <param name="status">
    /// The evaluated status: <see cref="HealthStatus.Degraded"/>, <see cref="HealthStatus.Unhealthy"/>,
    /// or a value this build does not recognise.
    /// </param>
    /// <param name="checks">The projected checks, from which the not-ready names are taken.</param>
    /// <returns>A problem document result carrying the legacy return code.</returns>
    /// <remarks>
    /// <para>
    /// The not-ready component is NAMED, because an operator reading a failed probe needs to know which
    /// one is not satisfied rather than only that something is not. Every name that can reach this
    /// method comes from the closed vocabulary this file declares, so it is an identifier this file
    /// chose rather than one a registration supplied, and no description, exception, statement or
    /// configuration value is carried here (decision record item 2).
    /// </para>
    /// <para>
    /// The legacy return code travels in the single permitted extension member rather than in a second
    /// error schema defined for this one response, and it is CONSUMED symbolically from the shared
    /// kernel rather than restated as a literal. <see cref="RetCode.E_RETRY"/> is the right member of
    /// the ported algebra for this response: the condition is transient and the caller's correct
    /// response is to ask again, which is exactly what a readiness gate does.
    /// </para>
    /// <para>
    /// DEGRADED AND UNHEALTHY BOTH ARRIVE HERE and are distinguished in the body rather than in the
    /// status, because both mean not ready and the gate reads the status code (decision record item 7).
    /// </para>
    /// </remarks>
    private static IResult BuildNotReadyProblem(
        HealthStatus status,
        IReadOnlyList<ServiceHealthCheck> checks)
    {
        string wireStatus = ToWireStatus(status);

        List<string> notReady = [];

        foreach (ServiceHealthCheck check in checks)
        {
            if (!string.Equals(check.Status, StatusHealthy, StringComparison.Ordinal))
            {
                notReady.Add(check.Name);
            }
        }

        // The empty case is reachable and is handled rather than assumed away: an aggregate can be
        // Degraded or Unhealthy while every individual entry reports Healthy, for instance under a
        // registration whose own result predicate degrades the whole on a condition no single entry
        // reports.
        string detail = notReady.Count == 0
            ? "The Persistence service is not ready (" + wireStatus + ")."
            : "The Persistence service is not ready (" + wireStatus + "). Readiness check(s) not "
              + "reporting " + StatusHealthy + ": " + string.Join(", ", notReady) + ".";

        return TypedResults.Problem(
            detail: detail,
            statusCode: StatusCodes.Status503ServiceUnavailable,
            title: UnavailableProblemTitle,
            extensions: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [RetCodeExtensionMember] = RetCode.E_RETRY,
            });
    }

    /// <summary>
    /// Maps a shared-framework status onto the closed three-token vocabulary the contract publishes.
    /// </summary>
    /// <param name="status">The status to translate.</param>
    /// <returns>One of <c>Healthy</c>, <c>Degraded</c> or <c>Unhealthy</c>.</returns>
    /// <remarks>
    /// Translated through an explicit switch rather than by the enumeration's own string conversion, so
    /// the published vocabulary is stated in this file and cannot drift silently if a future framework
    /// version renames or adds a member. Gateway's aggregator reads these three tokens literally and
    /// treats anything else as a failure, so a drift here would be observed as an unhealthy upstream
    /// rather than as a mismatch. An unrecognised status maps to <c>Unhealthy</c>, the same fail-closed
    /// choice the status-code mapping makes.
    /// </remarks>
    private static string ToWireStatus(HealthStatus status) => status switch
    {
        HealthStatus.Healthy => StatusHealthy,
        HealthStatus.Degraded => StatusDegraded,
        HealthStatus.Unhealthy => StatusUnhealthy,
        _ => StatusUnhealthy,
    };
}

// ==================================================================================================
//  THE ONE COMPONENT CHECK - READ-ONLY SQLITE REACHABILITY
// ==================================================================================================

/// <summary>
/// Reports whether this service can reach the one storage engine it holds, without altering it.
/// </summary>
/// <remarks>
/// <para>
/// WHY IT EXISTS. This service is the only one in the system that holds a storage provider, and the
/// Compose <c>depends_on</c> health condition gates Gateway on this service's <c>/health</c>. A
/// readiness verdict that never reached storage would therefore report ready while the only storage
/// engine in the estate was unreachable, which is exactly the state the gate exists to detect. This
/// check is what gives the verdict its meaning.
/// </para>
/// <para>
/// WHAT IT TOUCHES, AND WHAT IT MAY NOT. It calls the reachability member of the single connection seam
/// and nothing else. That member executes one constant, parameterless statement - so the ENGINE is
/// proven to answer rather than merely a file handle proven to open - and reads no schema, so it behaves
/// identically against an empty database and a populated one. It composes no URI and constructs no
/// connection: that grammar is the seam's declared, matrix-tested responsibility, and a second copy of
/// it here could drift from the first.
/// </para>
/// <para>
/// STRICTLY READ ONLY, AND THIS IS A HARD CONSTRAINT. There is no create, no delete, no migration, no
/// seed, no reseed, no DDL, no DML, no auto-commit toggle, no commit, no rollback, no extension load and
/// no integrity pragma anywhere in this class. For one workflow identifier the legacy-side and
/// target-side characterization recordings must be captured against the same <c>persistence-db</c>
/// volume state with the volume neither recreated nor reseeded between them, and a probe that mutated
/// stored state would break that on every single poll. The behavioural oracle shows the precise act to
/// avoid: <c>ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L450</c> performs a file delete immediately
/// before the open at <c>:L456</c> and the table creation at <c>:L463-L469</c>, and it reads like part
/// of the open sequence.
/// </para>
/// <para>
/// SQLITE AND ONLY SQLITE. No SQL Server and no Oracle is contacted, because neither is provisioned and
/// neither has a schema, a connection string or any DDL in the repository. The legacy transaction layer
/// declares exactly two database types at
/// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:L60-L61</c> and SQLite is absent from
/// that enumeration entirely; those two survive only as pure string transforms under <c>Sql/Paging/</c>.
/// </para>
/// <para>
/// IT NEVER THROWS OUT OF <see cref="CheckHealthAsync"/> EXCEPT FOR CALLER CANCELLATION. A component
/// check that faulted would surface to the route as an evaluation failure rather than as a storage
/// verdict, so every failure is caught and reported as the registration's failure status. That is not a
/// softening of the framework's fail-fast posture: fail fast governs startup validation and structural
/// faults, and a probe exists to REPORT a runtime condition.
/// </para>
/// <para>
/// INTERNAL, MATCHING THE CONVENTION OF EVERY DOMAIN TYPE IN THIS PROJECT. Nothing outside this assembly
/// resolves it - the route above is its only consumer and the registration is in the same file - and the
/// sibling test project reaches it through the <c>InternalsVisibleTo</c> the project file already grants,
/// so no interface and no widened surface is needed to make it directly testable.
/// </para>
/// </remarks>
internal sealed class SqliteReachabilityHealthCheck : IHealthCheck
{
    // ----------------------------------------------------------------------------------------------
    //  THE FIXED PROSE. Four constants, one per outcome, each authored here rather than derived from
    //  anything a provider, a setting or an exception supplied - which is what makes the disclosure
    //  boundary structural instead of a matter of review (constraint C-F).
    // ----------------------------------------------------------------------------------------------

    /// <summary>Fixed prose for a reachable engine. Names nothing about where or what it is.</summary>
    private const string StorageReachableDescription =
        "The storage engine answered a read-only reachability probe.";

    /// <summary>Fixed prose for an engine that answered negatively.</summary>
    private const string StorageUnreachableDescription =
        "The storage engine did not answer a read-only reachability probe.";

    /// <summary>Fixed prose for a probe that exceeded its own budget.</summary>
    private const string StorageProbeTimedOutDescription =
        "The storage reachability probe did not complete within its budget.";

    /// <summary>
    /// Fixed prose for a probe that could not be completed at all. It states THAT the probe failed and
    /// never WHY: the reason is an exception and the publishing route is anonymous.
    /// </summary>
    private const string StorageProbeFailedDescription =
        "The storage reachability probe could not be completed.";

    /// <summary>
    /// The budget for one reachability probe.
    /// </summary>
    /// <remarks>
    /// <para>
    /// BOUNDED SO THAT A HUNG ENGINE CANNOT HANG THE PROBE. A storage call that accepts the work and
    /// then never answers is reported not-ready when the budget expires, instead of holding the request
    /// open until the orchestrator's own probe gives up - which would make the readiness gate read a
    /// timeout rather than a verdict. A probe that hangs stalls the gate exactly as effectively as one
    /// that fails.
    /// </para>
    /// <para>
    /// It is a fixed value in this file rather than a configuration key, deliberately, and it matches
    /// the budget Gateway's own upstream probe uses so the two ends of the readiness chain agree. No
    /// such key is declared by this service's options type, by either settings file or by the
    /// orchestration environment file, and a key invented here would bind to nothing and fail silently.
    /// It is also NOT a latency claim: the repository publishes no service-level agreement, latency
    /// budget, throughput target or availability commitment anywhere, and none is asserted here. It is a
    /// liveness bound on one call, which is a different thing entirely.
    /// </para>
    /// </remarks>
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(2);

    /// <summary>
    /// How stale a previous SUCCESSFUL reachability answer may be before it is re-measured.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THIS IS AN AMPLIFICATION BOUND ON AN ANONYMOUS ROUTE, NOT A PERFORMANCE OPTIMISATION, and the
    /// distinction matters because no performance objective may be asserted anywhere in this refactor.
    /// <c>/health</c> is the one surface an unauthenticated caller can always reach, so without a window
    /// every anonymous request would make this service do storage work on demand. A short window
    /// collapses a burst into a single round trip while leaving an operator or an orchestrator polling on
    /// any realistic interval with an answer measured after its previous one.
    /// </para>
    /// <para>
    /// WHAT MAKES THE VALUE SAFE RATHER THAN MERELY CHOSEN: the seam caches ONLY a success and
    /// deliberately never remembers a failure, so this window can never delay the reporting of an
    /// unreachable engine. A service that has just failed is reported not-ready at the very next ask, and
    /// one that has just recovered is reported ready at the very next ask. The window therefore trades
    /// nothing away on either edge of a transition - it only bounds how often a repeated *positive*
    /// answer is re-measured.
    /// </para>
    /// <para>
    /// The age is measured on the injected clock inside the seam, never on an ambient one, because every
    /// clock read is a determinism seam a characterization run must be able to mask from both the master
    /// and the candidate recording. Passing <see cref="TimeSpan.Zero"/> instead would disable the window
    /// entirely, which is what a test asserting a probe per call does.
    /// </para>
    /// </remarks>
    private static readonly TimeSpan LivenessCacheWindow = TimeSpan.FromSeconds(1);

    /// <summary>The single storage seam, injected rather than resolved statically or globally.</summary>
    private readonly SqliteConnectionFactory _storage;

    /// <summary>The operator channel. Never receives a credential, a path, a statement or provider text.</summary>
    private readonly ILogger<SqliteReachabilityHealthCheck> _logger;

    /// <summary>
    /// Initializes the check.
    /// </summary>
    /// <param name="storage">
    /// The one connection seam in the system. Injected through DI, which is the deliberate replacement
    /// for the legacy global auto-instance <c>global n_sqlite n_sqlite</c>
    /// [<c>ws_objects/pfw.utility.sqlite.pbl.src/n_sqlite.sru:L90</c>] - the collision-resolution rule
    /// keeps the descriptive type name and turns the INSTANCE into a dependency.
    /// </param>
    /// <param name="logger">
    /// The operator channel, which is where the detail an anonymous body may not carry belongs.
    /// </param>
    /// <exception cref="ArgumentNullException">Either argument is <see langword="null"/>.</exception>
    public SqliteReachabilityHealthCheck(
        SqliteConnectionFactory storage,
        ILogger<SqliteReachabilityHealthCheck> logger)
    {
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(logger);

        _storage = storage;
        _logger = logger;
    }

    /// <summary>
    /// Establishes whether the storage engine answers, read-only and within a bounded budget.
    /// </summary>
    /// <param name="context">
    /// The registration being evaluated. Its failure status is honoured rather than assumed, so a host
    /// that registered this check as degrading rather than failing gets what it asked for.
    /// </param>
    /// <param name="cancellationToken">
    /// The evaluation's cancellation token, supplied by the framework and ultimately by the request.
    /// </param>
    /// <returns>
    /// <see cref="HealthCheckResult.Healthy(string, IReadOnlyDictionary{string, object})"/> when the
    /// engine answered; otherwise a result carrying the registration's failure status.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException">
    /// The CALLER cancelled - the request was aborted. Propagated deliberately: there is no longer a
    /// response to write, so producing a verdict would be work nobody reads. An expiry of this check's
    /// own budget is NOT propagated; it is reported as not-ready.
    /// </exception>
    /// <remarks>
    /// <para>
    /// The description strings are fixed prose authored in this file, and the returned result carries no
    /// data dictionary at all. Nothing from the provider - no error text, no statement, no path, no
    /// connection URI - is placed on the result, because the route that publishes it is anonymous. The
    /// numeric provider code goes to the operator channel instead, where it is genuinely useful and
    /// where it still discloses no location, credential or statement.
    /// </para>
    /// </remarks>
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        HealthStatus failureStatus = context.Registration.FailureStatus;

        // The budget is layered ON TOP of the caller's token rather than replacing it, so a
        // disconnecting caller still cancels immediately and the budget only ever shortens the wait.
        using CancellationTokenSource budget =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        budget.CancelAfter(ProbeTimeout);

        try
        {
            bool reachable = await _storage
                .IsReachableAsync(LivenessCacheWindow, budget.Token)
                .ConfigureAwait(false);

            if (reachable)
            {
                // Trace level on the ready path, because the Compose health condition polls
                // continuously and an information-level line per poll would drown the channel in
                // records saying nothing happened.
                _logger.LogTrace(
                    "The storage engine answered a read-only reachability probe for the {Check} "
                        + "readiness check.",
                    HealthEndpoints.SqliteCheckName);

                return HealthCheckResult.Healthy(StorageReachableDescription);
            }

            // NUMERIC CODES ONLY. The seam also exposes the provider's error TEXT and a structured error
            // payload, and neither is read here: provider text routinely names a file path, and the
            // legacy error structure's statement field carries a complete generated statement including
            // interpolated literal values [ws_objects/pfw.thread.ext.pbl.src/dberrordata.srs]. A ported
            // return code and a provider result code are integers that locate the fault without
            // disclosing anything (constraint C-F).
            _logger.LogWarning(
                "The storage engine did not answer a read-only reachability probe for the {Check} "
                    + "readiness check. Ported return code {ReturnCode}, provider result code "
                    + "{ProviderCode}.",
                HealthEndpoints.SqliteCheckName,
                _storage.SqlCode,
                _storage.SqlDbCode);

            return new HealthCheckResult(failureStatus, StorageUnreachableDescription);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // THIS CHECK'S OWN BUDGET EXPIRED, not the caller disconnecting - the filter is what
            // separates the two, and only this one is converted into a verdict. An engine that never
            // answers must be reported not-ready rather than allowed to hold the readiness gate open
            // until the orchestrator's own probe times out.
            _logger.LogWarning(
                "The storage reachability probe for the {Check} readiness check did not complete "
                    + "within its budget; reporting not ready.",
                HealthEndpoints.SqliteCheckName);

            return new HealthCheckResult(failureStatus, StorageProbeTimedOutDescription);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The seam fails FAST on a structural fault - a configured password it will not silently
            // ignore, a data directory it cannot create, a token the provider cannot honour - and that
            // posture is correct where it lives, at startup. Reaching it from a probe must not take the
            // container with it, or the readiness gate could never observe a structured not-ready state
            // and Gateway's aggregator would see a transport error instead of a verdict. So it is caught
            // here and reported. The exception object goes to the operator channel ONLY; it is never
            // projected onto the anonymous response, because its message can name a path or a setting.
            _logger.LogError(
                exception,
                "The storage reachability probe for the {Check} readiness check failed; reporting not "
                    + "ready.",
                HealthEndpoints.SqliteCheckName);

            return new HealthCheckResult(failureStatus, StorageProbeFailedDescription);
        }
    }
}

// ==================================================================================================
//  THE WIRE SHAPE
// ==================================================================================================

/// <summary>
/// The readiness of THIS service only, as published on <c>GET /health</c>.
/// </summary>
/// <param name="Status">
/// The overall verdict, carrying the distinction contract C-10 requires between "not ready" and
/// "unhealthy": <c>Healthy</c> is fully ready and is the ONLY verdict answered with 200;
/// <c>Degraded</c> is not ready but not failed, for instance a service still completing its startup
/// validation; <c>Unhealthy</c> has failed. The latter two are both answered with 503, because both mean
/// not ready and the orchestration readiness gate observes the status code - this member is what keeps
/// them distinguishable.
/// </param>
/// <param name="Service">
/// The reporting service, so Gateway's aggregate can name each upstream individually rather than
/// returning one opaque verdict. Always <c>persistence</c> on this service, which is one of the three
/// names that aggregate closes over.
/// </param>
/// <param name="CheckedAt">
/// When this report was produced, read from the injected clock so a characterization run can mask it,
/// and present so a consumer can detect a stale cached report.
/// </param>
/// <param name="Checks">
/// The components behind the verdict, named from a CLOSED public vocabulary authored by this service -
/// never from a registration's own check name, and never one entry per registered check beyond the
/// storage entry. See <c>HealthEndpoints.ProjectChecks</c>: an anonymous body must disclose neither what
/// a registration called itself nor how many registrations exist, and the per-check detail an operator
/// needs is carried on this service's operator telemetry instead.
/// </param>
/// <remarks>
/// <para>
/// Deliberately NOT an aggregate. Persistence is the bottom of the topology and calls no service, so it
/// has no upstream to report on; Gateway alone combines this report with those of DataServices and
/// Security and reports healthy only after all three do. This shape mirrors the leaf-service health
/// shape the sibling services publish member for member, so the estate presents one shape rather than
/// four - and Gateway's probe reads the <c>Status</c> member of exactly this shape.
/// </para>
/// <para>
/// Because <c>GET /health</c> is anonymous, everything in this record is public. It therefore carries no
/// data directory, database file name, connection URI, credential, certificate, environment-variable
/// value, configuration value, host name, other service's port, provider error text, statement text or
/// stack trace, and no member of it can be used to enumerate internal topology.
/// </para>
/// </remarks>
public sealed record ServiceHealthReport(
    string Status,
    string Service,
    DateTimeOffset CheckedAt,
    IReadOnlyList<ServiceHealthCheck> Checks);

/// <summary>
/// One component check contributing to a <see cref="ServiceHealthReport"/>.
/// </summary>
/// <param name="Name">
/// The component's stable identifier, drawn from this service's own closed public vocabulary -
/// <c>self</c>, <c>sqlite</c> or <c>components</c> - and never a registration-supplied check name, a
/// file path, a provider type name or any other internal detail.
/// </param>
/// <param name="Status">This check's own verdict, using the same three tokens as the overall report.</param>
public sealed record ServiceHealthCheck(
    string Name,
    string Status)
{
    /// <summary>
    /// An optional human-readable note. FIXED PROSE authored in <c>HealthEndpoints</c>, identical for
    /// every occurrence of a given component and verdict: it carries no configuration value, no key
    /// material, no path and no count, and never an exception message, a statement or a stack trace.
    /// </summary>
    /// <remarks>
    /// DECLARED AS AN INIT-ONLY PROPERTY RATHER THAN A THIRD POSITIONAL PARAMETER, and both halves of
    /// that are deliberate. A constructor parameter without a default is required by definition, so as a
    /// parameter this member would be published as required while the leaf-service health shape the
    /// estate follows lists only the name and the status as required. The serialization rule then makes
    /// the member ABSENT rather than null when there is no note, which is what "optional" means on the
    /// wire, and it keeps the body small - Gateway's probe reads a bounded prefix of it.
    /// </remarks>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; init; }
}
