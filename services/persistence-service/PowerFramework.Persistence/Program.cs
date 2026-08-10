// ==================================================================================================
//  Program.cs - THE COMPOSITION ROOT OF THE POWERFRAMEWORK PERSISTENCE SERVICE
//  ------------------------------------------------------------------------------------------------
//  ROLE
//  The ASP.NET Core host for the ONLY service that generates or executes SQL and the ONLY one holding
//  a storage provider. It is the BOTTOM of the layered, acyclic topology: DataServices calls it, it
//  calls no service at all, and it reads Security's published verification material only. Nothing
//  here aggregates an upstream, because there is no upstream to aggregate.
//
//  This file WIRES and does nothing else (constraint C-H). Every behaviour lives in an injectable
//  type under one of the ten subfolders, and every route declaration lives in Endpoints/ or in the
//  generated gRPC base its implementation derives from. A fat entry point is the classic way a
//  service misses its coverage gate, so the rule here is: register, order the pipeline, map, run.
//
//  A NOTE ON THE using LIST BELOW, VERIFIED RATHER THAN ASSUMED. Warnings are errors in this
//  repository, but an UNNECESSARY using is NOT among them: the unused-import diagnostic is a code-style
//  rule, code-style enforcement is deliberately not switched on in the build, and adding a redundant
//  import here was tested and produced zero warnings. So the import list is kept honest by review, not
//  by the compiler - which is exactly why it is worth saying so rather than trusting a build that would
//  not object.
//
//  TRANSPORT, AND WHY IT IS gRPC (constraint C-K)
//  The legacy surface this service ports is action-oriented rather than resource-oriented - Query,
//  Update, Exec, Commit, Rollback, SQLErrText - so it is RPC-shaped rather than resource-shaped. The
//  SQL task classes carry mandatory thread affinity in their own source comments, the database-error
//  structure dberrordata.srs requires a structured error payload, the mandated concurrency response
//  requires richer status detail than a bare code, and server streaming reproduces the progressive
//  result delivery of the legacy recordset. Protocol buffers over gRPC carry all four; JSON over
//  REST carries none of them well.
//
//  ONE PORT CARRIES BOTH PROTOCOLS, AND THAT IS DELIBERATE (constraint C-K)
//  appsettings.json declares exactly ONE Kestrel endpoint, on the service's assigned port, with
//  `Protocols = Http1AndHttp2`. gRPC requires HTTP/2 and the two REST routes are ordinary HTTP, and
//  both are served from that single listener: on a TLS endpoint - which is the shape every internal
//  address in the orchestration environment file declares - ALPN negotiates `h2` for a gRPC client and
//  `http/1.1` for an ordinary one, per connection, so one port serves both with no protocol switch and
//  no second endpoint anywhere in this file.
//
//  ⚠ THE TLS PART OF THAT IS LOAD BEARING, AND IT WAS VERIFIED BY RUNNING THE SERVICE RATHER THAN
//  ASSUMED. Kestrel does NOT enable prior-knowledge HTTP/2 on a PLAINTEXT `Http1AndHttp2` endpoint: it
//  emits "HTTP/2 is not enabled ... TLS is not enabled. HTTP/2 requires TLS application protocol
//  negotiation. Connections to this endpoint will use HTTP/1.1" and serves HTTP/1.1 only, which would
//  silently take all four gRPC contracts off the air while leaving the REST routes working - the worst
//  possible failure shape, because the readiness probe would still answer 200. So anyone tempted to
//  "simplify" the configured URL from `https` to `http` must instead declare the endpoint as HTTP/2
//  only, and would then lose the REST routes on that port. The scheme is a deployment decision and it
//  belongs to configuration; the certificate arrives the same way, through the Kestrel certificate
//  settings the orchestration layer supplies, which is why no certificate is named in this file.
//
//  THE PORT IS NEVER RESTATED IN CODE (constraint C-F). There is no UseUrls call, no literal port and
//  no literal address below. The container definition EXPOSEs the port and the orchestration manifest
//  MAPs it, and configuration is the single seam between the three - so a port change is a
//  configuration change, never a recompile. Note also that the neighbouring port 5103 is a
//  DELIBERATELY RESERVED, COMMENTED placeholder for a deferred service and not a spare: binding it
//  here would break the documented port map and violate constraint C-D in spirit.
//
//  NO HTTPS IS CONFIGURED HERE EITHER. There is no UseHttps, no development certificate, no HSTS and
//  no HTTPS redirection. Mutual TLS is a documented per-pair fallback only, and no certificate
//  material is provisioned for this service, so inventing a TLS story in code would be scaffolding
//  for a capability nothing has asked for. The listener's scheme is configuration's business.
//
//  LEGACY REFERENCE (read only - never edited, never built, never shipped: constraint C-C)
//  There is no legacy analogue for a host at all: PowerFramework is a LIBRARY with no process of its
//  own, no listener and no server tier, so every boundary below is net new. What IS ported is the
//  framework application object's LIFECYCLE POSTURE at ws_objects/pfw.pbl.src/pfw.sra - a structural
//  fault ends the process rather than degrading past it [:L111-L144] - together with the transaction
//  pool's and transaction object's clock semantics [n_cst_thread_trans_pool.sru:L97, :L215;
//  n_cst_thread_trans.sru:L107, :L198]. No path under ws_objects/ is read or written at runtime, no
//  shipped native binary is loaded, and nothing here depends on the PowerBuilder toolchain or
//  runtime.
//
//  WHAT THIS FILE DELIBERATELY DOES NOT DO
//    * No HttpClient, no gRPC channel and no client of any kind. Persistence is called BY
//      DataServices and calls nothing, so it has no outbound edge, no transient network fault of its
//      own to absorb, and therefore no resilience pipeline - which is why
//      Microsoft.Extensions.Http.Resilience is assigned to Gateway and DataServices and is absent
//      from this project. Adding a retry here would be inventing a behaviour the legacy never had
//      (constraints C-A, C-B).
//    * No token minting and no signing key. Security is the SOLE issuer in the entire system; this
//      service holds verification material only. There is no SigningCredentials, no token handler, no
//      CreateToken and no WriteToken below, and the minting package
//      Microsoft.IdentityModel.JsonWebTokens is deliberately absent from this project's references
//      for exactly that reason (constraints C-F, C-G).
//    * No fabricated database. Only SQLite has evidence in the repository - one DDL statement and one
//      connection URI grammar - so SQLite is the only provider registered. There is no UseSqlServer,
//      no UseOracle, no client package for either and no SQLCipher. The legacy's two enumerated
//      database types survive as PURE STRING TRANSFORMS under Sql/Paging/ that need no instance of
//      either engine (constraint C-E).
//    * No destructive storage call. There is no EnsureDeleted, no EnsureCreated, no DROP and no
//      migration application anywhere below, and the ONLY file this service ever deletes is the
//      zero-byte writability probe the startup gate itself just created, by a generated name that
//      cannot name a database. See the storage section for why that distinction is load bearing rather
//      than merely tidy.
//    * No registration, route, client, options section or placeholder type for DesignSystem,
//      Documents, Integration or ScriptBridge. The four reserved 501 routes belong to GATEWAY alone
//      and this service declares none of them (constraint C-D).
//    * No OpenAPI document. This project references neither OpenAPI package: its published contract
//      is protocol-buffer shaped, and describing two diagnostic routes with a document generator
//      would be scope creep.
//    * No gRPC server reflection. It is a command-line-tooling convenience and is on the
//      deliberately-excluded list.
//    * No SCREAMING_SNAKE constant is DECLARED here. The preserved legacy identifiers are CONSUMED
//      from PowerFramework.Shared.Kernel, whose files are the ones the repository .editorconfig
//      scopes its naming suppressions to.
// ==================================================================================================

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using PowerFramework.Persistence.Buffers;
using PowerFramework.Persistence.Concurrency;
using PowerFramework.Persistence.Configuration;
using PowerFramework.Persistence.Data;
using PowerFramework.Persistence.Endpoints;
using PowerFramework.Persistence.Errors;
using PowerFramework.Persistence.Grpc;
using PowerFramework.Persistence.Sql.Paging;
using PowerFramework.Persistence.Tasks;
using PowerFramework.Persistence.Transactions;
using PowerFramework.Shared.Diagnostics;
using RetCode = PowerFramework.Shared.Kernel.RetCode;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// --------------------------------------------------------------------------------------------------
//  REGISTRATION. One call per concern, each implemented as an extension method at the bottom of this
//  file so the entry point reads as an inventory rather than as a wall of container calls. The order
//  of these six lines is presentational only - the container resolves lazily - but it is the order a
//  reader wants: what the service is configured with, what clock it reads, who it trusts, where it
//  stores, what it computes, and what it publishes.
// --------------------------------------------------------------------------------------------------
builder.Services.AddPersistenceDeterminismSeam();
builder.Services.AddPersistenceOptions();
builder.Services.AddPersistenceAuthentication(builder.Configuration);
builder.Services.AddPersistenceStorage();
builder.Services.AddPersistenceSqlLayer();
builder.Services.AddPersistencePublishedSurface();

WebApplication app = builder.Build();

// --------------------------------------------------------------------------------------------------
//  THE STARTUP GATE - FAIL FAST, PRESERVED AS FAIL FAST (constraint C-B)
//
//  The legacy is emphatically fail-fast and that posture must survive AS fail-fast, never as graceful
//  degradation. Two pieces of evidence anchor it: worker-session creation failure is fatal, and the
//  application object's system-error handler DECODES a seven-field assertion payload and then
//  executes `HALT CLOSE` [ws_objects/pfw.pbl.src/pfw.sra:L111-L144]. It does not warn and carry on.
//
//  This runs AFTER Build and BEFORE the pipeline, which is the only position that can honour that:
//  early enough that no request is ever served by a structurally broken process, late enough that the
//  container is fully composed and can be interrogated. A precondition failure throws, and an
//  exception escaping a composition root terminates the process with a non-zero exit code - which is
//  the managed equivalent of HALT CLOSE. Softening any of these into warn-and-continue would be a
//  behavioural change dressed up as robustness.
// --------------------------------------------------------------------------------------------------
app.Services.ValidatePersistenceStructuralPreconditions();

// --------------------------------------------------------------------------------------------------
//  THE PIPELINE. Authentication STRICTLY BEFORE authorization, and both STRICTLY BEFORE the endpoint
//  mappings. This ordering is not stylistic: with the two reversed or placed after the mappings the
//  fallback policy never sees an authenticated principal and unauthenticated requests pass straight
//  through, silently, on every protected route at once. Phase 9's runtime checks exist to prove this
//  ordering holds rather than to assume it.
// --------------------------------------------------------------------------------------------------
app.UseAuthentication();
app.UseAuthorization();

app.MapPersistenceEndpoints();

// --------------------------------------------------------------------------------------------------
//  THE HOST BOUNDARY. AssertionFailure is caught HERE and nowhere else in this file, and it is
//  treated as a structural fault: the decoded payload is logged and the process ends. That is the
//  managed equivalent of the legacy's terminate-after-decoding behaviour, and it is why the catch is
//  narrow. A broad `catch (Exception)` would be wrong twice over - it would swallow genuine faults
//  the runtime should report, and it would intercept the sentinel the in-process test host throws to
//  capture this host, breaking every service-level test in the sibling project.
//
//  Note that the class whose assertions raise this is named `Assertions`, not `Assert`: a static
//  class cannot contain a member of its own name. Its methods keep the legacy `Assert` and
//  `AssertFailed` spellings.
// --------------------------------------------------------------------------------------------------
try
{
    app.Run();
}
catch (AssertionFailure failure)
{
    // Structured, and the payload is logged as data rather than interpolated into the message, so a
    // log pipeline can index the fields the legacy protocol carries. No SQL and no credential is
    // reachable from an assertion payload, so nothing here needs redacting.
    app.Logger.LogCritical(
        failure,
        "A framework assertion failed, which is a structural fault rather than a request fault. The "
        + "process is terminating rather than continuing in a state its own invariants say is "
        + "impossible, which is the posture of the legacy application object's system-error handler "
        + "[ws_objects/pfw.pbl.src/pfw.sra:L111-L144].");

    // Rethrown rather than swallowed or translated into an exit code. The runtime reports the
    // terminating exception and exits non-zero, which is what an orchestrator's restart policy reads.
    throw;
}

// ==================================================================================================
//  REGISTRATION - THE CONTAINER, ONE CONCERN AT A TIME
//
//  EVERY REGISTRATION BELOW USES TryAdd. That is a deliberate contract with the deployment and with
//  the test host, not a habit: a host that can supply a better implementation - a real DBMS engine, a
//  materialised DataWindow runtime, a deterministic clock - registers its own descriptor FIRST and
//  this file needs no edit at all. `Endpoints/HealthEndpoints.AddPersistenceHealthChecks` already
//  relies on that property for the clock and the storage seam, so using anything stronger here would
//  make the two registration paths fight over which one wins.
// ==================================================================================================

/// <summary>
/// The container registrations for the Persistence service, grouped one method per concern.
/// </summary>
internal static class PersistenceServiceCollectionExtensions
{
    /// <summary>
    /// Registers the clock every component in this service reads through.
    /// </summary>
    /// <param name="services">The container.</param>
    /// <returns>The same container, for chaining.</returns>
    /// <remarks>
    /// <para>
    /// THE DETERMINISM SEAM, AND IT IS NOT STYLISTIC (constraint C-K). The characterization model
    /// requires that non-deterministic values be masked from BOTH the master recording and the
    /// candidate, so every clock read in this service has to be substitutable from one place. A
    /// <c>DateTime.UtcNow</c>, <c>DateTimeOffset.Now</c>, <c>Environment.TickCount</c> or
    /// <c>Stopwatch.GetTimestamp</c> anywhere in this project outside a
    /// <see cref="TimeProvider"/> implementation is therefore a determinism defect, not a style
    /// preference, and Phase 9 greps for exactly those six spellings.
    /// </para>
    /// <para>
    /// FOUR LEGACY CLOCK READS MAKE THIS NECESSARY, and every one of them is OBSERVABLE rather than
    /// incidental, which is why none of them can be left ambient:
    /// </para>
    /// <list type="number">
    /// <item>
    /// The transaction pool's idle expiry. It stamps <c>idleStartTime = CPU()</c> when a reference is
    /// released [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans_pool.sru:L97</c>] and
    /// collects on <c>CPU() - idleStartTime &gt;= _nKeepAliveExpireTime</c> [<c>:L215</c>], with the
    /// window defaulting to the library's own <c>KEEPALIVE_EXPIRE</c> of thirty seconds. Whether a
    /// pooled transaction survives a given moment is directly a function of that comparison.
    /// </item>
    /// <item>
    /// The connection-liveness cache in the transaction object. It stamps <c>_nLastConnOK = CPU()</c>
    /// on a successful connect [<c>n_cst_thread_trans.sru:L107</c>] and then SHORT-CIRCUITS the
    /// liveness probe entirely for roughly ten seconds - <c>if CPU() - _nLastConnOK &lt; 10000 then
    /// return true</c> [<c>:L198</c>] - so whether a probe reaches the database at all depends on the
    /// clock.
    /// </item>
    /// <item>
    /// The throttled progress notification. The main-thread carrier suppresses a notification unless
    /// <c>CPU() - _nUpdateNotifyTick &gt; 100</c> or the row is the first or the last
    /// [<c>n_cst_thread_task_sqlbase_ds_mt.sru:L37-L38</c>], so the number of notifications a caller
    /// observes in a stream is a function of a hundred-millisecond tick delta.
    /// </item>
    /// <item>
    /// The substrate's timed wait, which converts a timeout in seconds into a tick comparison
    /// [<c>n_cst_threading.sru:L329, :L334</c>].
    /// </item>
    /// </list>
    /// </remarks>
    internal static IServiceCollection AddPersistenceDeterminismSeam(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);

        return services;
    }

    /// <summary>
    /// Binds and validates this service's own configuration.
    /// </summary>
    /// <param name="services">The container.</param>
    /// <returns>The same container, for chaining.</returns>
    /// <remarks>
    /// <para>
    /// BOUND FROM THE CONFIGURATION ROOT rather than from a service-named wrapper, because
    /// <see cref="PersistenceOptions"/> declares its four groups - <c>Sqlite</c>,
    /// <c>TransactionPool</c>, <c>Query</c> and <c>Jwt</c> - as TOP-LEVEL sections and deliberately
    /// carries no section-name constant. That is what makes the environment-variable contract read
    /// <c>Sqlite__DataDirectory</c> rather than <c>Persistence__Sqlite__DataDirectory</c>, and it is
    /// the shape the cross-service configuration coherence suite pins.
    /// </para>
    /// <para>
    /// VALIDATED ON START, WHICH IS WHERE FAIL FAST BELONGS. <c>ValidateOnStart</c> moves the verdict
    /// from first use to host start, so a misconfigured deployment cannot serve a single request
    /// before anyone finds out. The validator is registered EXPLICITLY rather than through
    /// <c>ValidateDataAnnotations</c> because annotation validation does not recurse into nested
    /// complex properties: without it every annotation on the four groups would be silently ignored
    /// and a broken service would start happily.
    /// </para>
    /// <para>
    /// NOTHING HERE IS HARDCODED (constraint C-F). No connection string, no address, no port and no
    /// credential appears in this file in any form. Every value the service needs arrives through
    /// this binding, which is also what keeps the container definition and the orchestration manifest
    /// the single owners of the deployment shape.
    /// </para>
    /// </remarks>
    internal static IServiceCollection AddPersistenceOptions(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services
            .AddOptions<PersistenceOptions>()
            .BindConfiguration(string.Empty)
            .ValidateOnStart();

        services.TryAddSingleton<IValidateOptions<PersistenceOptions>, PersistenceOptionsValidator>();

        return services;
    }

    /// <summary>
    /// Registers inbound token validation and the deny-by-default authorization posture.
    /// </summary>
    /// <param name="services">The container.</param>
    /// <param name="configuration">The configuration the bearer settings are read from.</param>
    /// <returns>The same container, for chaining.</returns>
    /// <remarks>
    /// <para>
    /// VERIFICATION ONLY, AND THAT IS THE WHOLE POINT (constraint C-G). Security is the SOLE issuer in
    /// the system and there is exactly ONE signing secret anywhere in it, which Security holds. This
    /// service holds verification material and nothing else: there is no signing key, no symmetric
    /// key, no <c>SigningCredentials</c> and no minting call below, and if this method ever appeared
    /// to need one the topology would have been misread.
    /// </para>
    /// <para>
    /// THE STOCK HANDLER DOES THE RETRIEVAL. Pointing it at Security's authority is enough for it to
    /// resolve the discovery document and the published key set with ZERO bespoke retrieval code -
    /// and that property is precisely why Security is REST rather than gRPC. Choosing gRPC there
    /// would have forced hand-written key-set fetching into three separate services, a net increase
    /// in hand-written security code, which is the opposite of what the requirement asks for.
    /// </para>
    /// <para>
    /// ALL FOUR VALIDATIONS DEFAULT TO ON and each is read from configuration rather than hardcoded,
    /// so a deployment can tighten but never silently loosen one by omission: a missing key leaves the
    /// safe value in place. Require-HTTPS-metadata is the same shape, defaulting to <c>true</c>. There
    /// is no environment-conditional bypass anywhere in this method.
    /// </para>
    /// <para>
    /// METADATA IS FETCHED LAZILY by the handler on first use rather than eagerly at startup, and that
    /// is what satisfies constraint C-I: this host starts and reports its own health while Security is
    /// still coming up, instead of crash-looping and making the orchestration health chain
    /// unresolvable. An anonymous request is refused before any metadata is needed, because the
    /// handler looks for a token first - so the mandated 401 holds even with Security unreachable.
    /// That is about retrieval TIMING and is emphatically not a licence to skip the startup
    /// validation in <see cref="PersistenceStartupGate"/>.
    /// </para>
    /// </remarks>
    internal static IServiceCollection AddPersistenceAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        IConfigurationSection jwt = configuration.GetSection(PersistenceStartupGate.JwtSectionName);

        string authority = (jwt[nameof(JwtOptions.Authority)] ?? string.Empty).Trim();
        string metadataAddress = (jwt[PersistenceStartupGate.MetadataAddressKey] ?? string.Empty).Trim();

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(bearer =>
            {
                bearer.Authority = authority;
                bearer.Audience = (jwt[nameof(JwtOptions.Audience)] ?? string.Empty).Trim();
                bearer.RequireHttpsMetadata = jwt.GetValue(nameof(JwtOptions.RequireHttpsMetadata), true);

                // An explicit metadata address overrides the authority-relative default, which is what
                // lets a deployment point the handler at a key set published somewhere other than the
                // conventional path beneath the authority.
                if (metadataAddress.Length > 0)
                {
                    bearer.MetadataAddress = metadataAddress;
                }

                bearer.TokenValidationParameters.ValidateIssuer = jwt.GetValue("ValidateIssuer", true);
                bearer.TokenValidationParameters.ValidateAudience = jwt.GetValue("ValidateAudience", true);
                bearer.TokenValidationParameters.ValidateLifetime = jwt.GetValue("ValidateLifetime", true);
                bearer.TokenValidationParameters.ValidateIssuerSigningKey =
                    jwt.GetValue("ValidateIssuerSigningKey", true);
            });

        // DENY BY DEFAULT, WITH ONE NAMED EXCEPTION. A fallback policy means no route is ever
        // authenticated by omission: a new endpoint arriving without an explicit declaration is a
        // CLOSED door rather than an open one, and the single anonymous route - the readiness probe -
        // opts out in the one place a reader looks for it. The inverse posture, allowing by default
        // and remembering to protect each endpoint, is not auditable, because proving it correct means
        // proving a negative across every route that exists and every route anyone adds later.
        services.AddAuthorization(static options =>
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build());

        return services;
    }

    /// <summary>
    /// Registers the one storage seam in the system and the entity model over it.
    /// </summary>
    /// <param name="services">The container.</param>
    /// <returns>The same container, for chaining.</returns>
    /// <remarks>
    /// <para>
    /// SQLITE AND ONLY SQLITE (constraint C-E). It is the only storage engine anywhere in the
    /// repository with a schema, a DDL statement or a connection string behind it, so it is the only
    /// one provisioned. The legacy transaction object enumerates exactly TWO database types, SQL
    /// Server and Oracle [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:L60-L61</c>],
    /// and SQLite is not in that enumeration at all - yet NEITHER of those two has a schema, a
    /// connection string or a line of DDL anywhere in the tree. Registering a provider for either
    /// would therefore be fabricating a database. Their statement generation survives instead as pure
    /// string transforms under <c>Sql/Paging/</c>, which need no instance of either engine to be held
    /// to byte-exact parity.
    /// </para>
    /// <para>
    /// THE CONNECTION IS COMPOSED BY THE FACTORY, NEVER BY A LITERAL HERE. <c>UseSqlite</c> is handed
    /// <see cref="SqliteConnectionFactory.ConnectionString"/> rather than a string built in this file,
    /// which keeps the legacy URI grammar - the open mode, the three-state integrity <c>check</c>
    /// option and the <c>journal</c> mode defaulting to <c>DELETE</c>
    /// [<c>ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L452-L456</c>] - together with the optional
    /// environment-supplied password, as that one type's single responsibility. The factory is a
    /// singleton for the process lifetime, the way the legacy global auto-instance was, but injected
    /// rather than global, which is the whole point of the substitution: it composes connections and
    /// does not hold one.
    /// </para>
    /// <para>
    /// ⚠ CRITICAL - NOTHING HERE DELETES, RECREATES OR RESEEDS THE DATABASE, AND NOTHING EVER MAY.
    /// The legacy test window does perform a file delete immediately before opening
    /// [<c>w_test_sqlite.srw:L450</c>], but that call sits inside a CONNECT button's clicked event: it
    /// is the TEST HARNESS'S setup, not framework behaviour, and reproducing it in the service would
    /// be catastrophic rather than faithful. The characterization model requires that for a given
    /// workflow the legacy-side and target-side captures run against the SAME persistence volume
    /// state, with the volume neither recreated nor reseeded between them, or the paired recordings
    /// are not comparable at all. A startup file delete would destroy that guarantee on every single
    /// restart. Hence no <c>EnsureDeleted</c>, no <c>EnsureCreated</c> and no <c>DROP</c> anywhere in
    /// this service, and the only deletion it performs at all is of the startup gate's own writability
    /// probe file, which it created a moment earlier under a generated name.
    /// </para>
    /// <para>
    /// MIGRATIONS ARE NOT APPLIED AT STARTUP, AND THAT IS A DELIBERATE, DOCUMENTED CHOICE
    /// (constraint C-K). Four reasons, each sufficient on its own. First, the paired-capture rule
    /// above: an automatic schema mutation on every restart is exactly the between-captures volume
    /// change the rule forbids, and a parity run must be able to rely on the volume being untouched.
    /// Second, this service is required to be independently SCALABLE (constraint C-J), and concurrent
    /// replicas racing to apply the same migration is a genuine hazard with no upside. Third, a
    /// missing schema SHOULD be visible: the readiness probe reports storage reachability, so an
    /// unmigrated database surfaces as unhealthy and gates the orchestration chain, which is
    /// strictly better than being silently repaired by whichever replica started first. Fourth,
    /// applying migrations is an operator action with a first-class tool - the design-time factory and
    /// the migrations under <c>Data/Migrations/</c> exist precisely so <c>dotnet ef database
    /// update</c> can do it out of band, additively, when someone has decided to. If a future phase
    /// does want startup application, it must be ADDITIVE ONLY, never a drop-and-recreate, and gated
    /// by configuration so a parity run can switch it off.
    /// </para>
    /// <para>
    /// LIFETIME. The context is SCOPED, per the Entity Framework norm, and a gRPC call is itself a
    /// scope - which raises a real hazard worth naming: a streaming method that captured a
    /// request-scoped context for the life of a long stream would hold it far past its intended
    /// lifetime. None of the four gRPC implementations takes a context dependency at all - retrieval
    /// runs through the SQL task layer and its own connection seam - so the hazard is absent by
    /// construction here rather than merely unlikely. Any future streaming method that does need one
    /// must resolve its own scope or take a context factory instead of capturing the ambient context.
    /// </para>
    /// </remarks>
    internal static IServiceCollection AddPersistenceStorage(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<SqliteConnectionFactory>();

        services.AddDbContext<PowerFrameworkDbContext>(static (provider, options) =>
            options.UseSqlite(provider.GetRequiredService<SqliteConnectionFactory>().ConnectionString));

        return services;
    }

    /// <summary>
    /// Registers the SQL, buffer, concurrency, error and task layers, and the transaction pool.
    /// </summary>
    /// <param name="services">The container.</param>
    /// <returns>The same container, for chaining.</returns>
    /// <remarks>
    /// <para>
    /// LIFETIMES ARE CHOSEN, NOT DEFAULTED, and the non-obvious ones are commented at their
    /// registration. The stateless helpers - the redactor, the conflict detector, the changeset codec
    /// and the two paging rewriters - are singletons because they hold no per-request state; the
    /// rewriters in particular are PURE STRING TRANSFORMS, which is what makes them testable with no
    /// database of either dialect running.
    /// </para>
    /// <para>
    /// THE PAGING STRATEGY RESOLUTION PRESERVES THE LEGACY DISPATCH SHAPE EXACTLY (constraint C-B).
    /// Two rewriters are registered and only two, because the legacy has exactly two arms and a
    /// <c>case else</c>: an unrecognised database type yields
    /// <see cref="RetCode.E_NO_IMPLEMENTATION"/> [<c>n_cst_thread_task_sqlquery.sru:L396-L398</c>]
    /// rather than an exception or a silent fallback, and the dispatcher under <c>Sql/Paging/</c> owns
    /// that arm. A THIRD ARM IS NOT ADDED HERE, and specifically not one for SQLite: the legacy's own
    /// type resolution classifies anything whose DBMS string does not contain <c>ORACLE</c> - SQLite
    /// included - as the SQL Server type, and reproducing that is the requirement. Registering the two
    /// as an enumerable, rather than a switch in this file, is what keeps the dispatch decision in the
    /// one place that is unit-tested against the eleven documented matrix rows.
    /// </para>
    /// </remarks>
    internal static IServiceCollection AddPersistenceSqlLayer(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // ------------------------------------------------------------------------------------------
        //  DIAGNOSTICS AND CONCURRENCY. The redactor is registered against its interface because it is
        //  a POLICY seam: a deployment with a stricter disclosure rule substitutes it, and every
        //  statement-bearing field in the service then changes behaviour at once rather than one call
        //  site at a time. The detector depends on it by interface for the same reason.
        // ------------------------------------------------------------------------------------------
        services.TryAddSingleton<ISqlRedactor>(static _ => SqlRedactor.Instance);

        // Constructed through an explicit factory rather than by type, because the detector's
        // constructor is INTERNAL: the container's activator only considers public constructors, so a
        // type registration would compile happily and then fail container validation at startup. Naming
        // the dependency here also keeps the redactor arriving by interface, which is the point of the
        // policy seam above.
        services.TryAddSingleton(static provider =>
            new ConflictDetector(provider.GetRequiredService<ISqlRedactor>()));

        // ------------------------------------------------------------------------------------------
        //  BUFFERS. The payload codec is the wire half of the changeset transfer and the codec is the
        //  buffer half; both are stateless and both take the injected clock, so both are singletons.
        // ------------------------------------------------------------------------------------------
        services.TryAddSingleton<IChangesetPayloadCodec, ChangesetPayloadCodec>();
        services.TryAddSingleton(static provider => new ChangesetCodec(
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<IChangesetPayloadCodec>()));

        // ------------------------------------------------------------------------------------------
        //  PAGING. Registered as an ENUMERABLE of the one interface, in dialect order, so the
        //  dispatcher selects an arm by asking each rewriter for its dialect rather than by consulting
        //  a switch that would have to be kept in step with this file. TryAddEnumerable keyed on the
        //  implementation type makes the pair idempotent: calling this method twice cannot produce four
        //  rewriters and therefore cannot make the selection ambiguous.
        // ------------------------------------------------------------------------------------------
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IPagingRewriter, SqlServerPagingRewriter>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IPagingRewriter, OraclePagingRewriter>());

        // ------------------------------------------------------------------------------------------
        //  THE TASK LAYER'S SHARED COLLABORATORS. The data-store factory and the retrieval-hook
        //  activator are shared by BOTH halves of every proxy pair, which is what makes the
        //  marshalling boundary between them a boundary over shared state rather than two unrelated
        //  object graphs.
        // ------------------------------------------------------------------------------------------
        services.TryAddSingleton<IDataObjectRuntime, UnboundDataObjectRuntime>();
        services.TryAddSingleton<ISqlDataStoreFactory>(static provider => new SqlDataStoreFactory(
            provider.GetRequiredService<IDataObjectRuntime>(),
            provider.GetRequiredService<TimeProvider>()));
        services.TryAddSingleton<ISqlRetrievalHookActivator>(static _ => new SqlRetrievalHookActivator());

        // ------------------------------------------------------------------------------------------
        //  THE RUNTIME SEAMS. See the extended commentary on each type at the bottom of this file for
        //  why each answers its contract's own defined negative in this phase instead of binding a
        //  handle, and why that is a documented gap rather than a stub.
        // ------------------------------------------------------------------------------------------
        services.TryAddSingleton<IQueryTransactionSurface, UnboundQueryTransactionSurface>();
        services.TryAddSingleton<IQueryDataWindowRuntime, UnboundQueryDataWindowRuntime>();
        services.TryAddSingleton<ITransactionEngine, UnprovisionedTransactionEngine>();

        // ------------------------------------------------------------------------------------------
        //  THE TRANSACTION POOL - SINGLETON, AND ANY OTHER LIFETIME DEFEATS ITS PURPOSE ENTIRELY. It
        //  is REFERENCE COUNTED with idle expiry [n_cst_thread_trans_pool.sru:L97, :L174, :L215]: a
        //  scoped pool would hand every call its own pool, so a reference count would never exceed one
        //  and nothing would ever be shared or reused, which is the only reason a pool exists.
        //
        //  The activator is the pool's factory seam. It takes a DELEGATE producing an engine rather
        //  than an engine instance, because the pool creates one engine per pooled transaction and
        //  disposes it on collection - so resolving a single engine here and handing the same object
        //  round would be a use-after-dispose waiting to happen. The delegate resolves from the
        //  container each time, which means substituting the engine registration above is enough to
        //  change what the pool creates.
        // ------------------------------------------------------------------------------------------
        services.TryAddSingleton<IPooledTransactionActivator>(static provider =>
            new PooledTransactionActivator(
                provider.GetRequiredService<ITransactionEngine>,
                provider.GetRequiredService<TimeProvider>()));

        services.TryAddSingleton(static provider => new TransactionPool(
            provider.GetRequiredService<IOptions<PersistenceOptions>>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<IPooledTransactionActivator>()));

        return services;
    }

    /// <summary>
    /// Registers everything this service publishes: the four gRPC contracts and the two REST routes.
    /// </summary>
    /// <param name="services">The container.</param>
    /// <returns>The same container, for chaining.</returns>
    /// <remarks>
    /// <para>
    /// THE FOUR HANDLE TABLES ARE SINGLETONS BECAUSE THE HANDLES OUTLIVE THE CALL THAT MINTED THEM.
    /// A caller creates a transaction session in one request and names it in the next; a query task
    /// created by one call is retrieved, paged and destroyed by later ones. That is the legacy's own
    /// shape - the caller-side object holds the task index across calls
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru</c>] - and a scoped registry
    /// would discard every handle at the end of the request that created it, so the very next call
    /// would answer "unknown session" for a session the caller had just been told it owned.
    /// </para>
    /// <para>
    /// THE gRPC IMPLEMENTATIONS THEMSELVES ARE NOT REGISTERED, WHICH IS DELIBERATE. The gRPC hosting
    /// layer activates a service implementation per call, resolving its constructor arguments from the
    /// container, so leaving them unregistered is the framework's own recommended shape rather than an
    /// omission. Their collaborators - the handle tables, the factories, the pool - are singletons, so
    /// the per-call object is a thin router over shared state and costs nothing to create. Registering
    /// them explicitly would only invite a lifetime mismatch between the registration and the
    /// activation.
    /// </para>
    /// <para>
    /// NO SERVER REFLECTION IS REGISTERED. It is a convenience for command-line gRPC tooling, is on
    /// the deliberately-excluded package list, and would publish this service's whole method inventory
    /// to any caller that could reach the port.
    /// </para>
    /// <para>
    /// HEALTH CHECKS COME FROM THE SHARED FRAMEWORK. Registration ships inside
    /// <c>Microsoft.AspNetCore.App</c>, so the readiness probe needs no package reference and none is
    /// declared - the URI-probe packages are on the excluded list too. The one call below delegates to
    /// <c>Endpoints/HealthEndpoints</c>, which registers the storage reachability check that gives the
    /// probe its meaning, so this host cannot end up publishing a readiness verdict that never reached
    /// the only storage engine in the estate.
    /// </para>
    /// <para>
    /// PROBLEM DETAILS is registered so the framework's own authentication challenge and the readiness
    /// probe's failure response both answer <c>application/problem+json</c>, which is the error shape
    /// the authored REST contracts publish.
    /// </para>
    /// </remarks>
    internal static IServiceCollection AddPersistencePublishedSurface(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // The server-side handle tables. See the remarks above for why every one of them is a
        // singleton and what breaks if one is not.
        services.TryAddSingleton<TransactionSessionRegistry>();
        services.TryAddSingleton<QueryTaskRegistry>();
        services.TryAddSingleton<UpdateTaskRegistry>();
        services.TryAddSingleton<CommandTaskRegistry>();

        // The retrieval driver. Stateless - it forwards a task and a sink to the task's own execution
        // and returns the row count - so a singleton, and the seam exists so a characterization run can
        // observe the drive without reaching inside the task.
        services.TryAddSingleton<IQueryRetrievalRunner, QueryRetrievalRunner>();

        // The three task factories. The pairing is the point: see PersistenceSqlTaskHost and the factory
        // types below for why the caller-side proxy and the worker-side task are kept as DISTINCT types
        // with a marshalling boundary between them rather than flattened into one async method.
        //
        // ⚠ THE WORKER-SIDE HOST IS DELIBERATELY NOT REGISTERED, AND THE REASON IS A CORRECTNESS ONE
        // rather than a preference. A host is meaningful only when BOUND, at construction, to the fault
        // sink and caller-side proxy of the one task it serves - and a container cannot express that,
        // because the sink is supplied per call by the service implementation, not by configuration. It
        // must also be ONE HOST PER TASK: the host owns that task's datastore cache, and a shared host
        // would hand concurrent calls the same non-concurrent cache, which is a data race that locking
        // the bag could not fix. A singleton registration would quietly invite exactly that, so the
        // factory constructs the host itself and the SUBSTITUTION POINT is the factory below, which is
        // TryAdd-registered like everything else here.
        services.TryAddSingleton<IQueryTaskFactory, QueryTaskFactory>();
        services.TryAddSingleton<IUpdateTaskFactory, UnboundUpdateTaskFactory>();
        services.TryAddSingleton<ICommandTaskFactory, UnboundCommandTaskFactory>();

        // gRPC IS THIS SERVICE'S PRIMARY TRANSPORT. The interceptor is added HERE, once, at the server
        // level, which is exactly what makes the status mapping central rather than duplicated across
        // four implementations - see PersistenceStatusInterceptor for the mapping itself.
        services.AddGrpc(static options =>
        {
            options.Interceptors.Add<PersistenceStatusInterceptor>();

            // ⚠ LOAD BEARING, AND IT IS ABOUT THE SHARED PORT (constraint C-K). Left at its default,
            // the gRPC hosting layer maps a CATCH-ALL route of the shape /{service}/{method} so that a
            // call to a service this server does not host answers the gRPC UNIMPLEMENTED status instead
            // of falling through. That route is two parameter segments wide, so it matches ANY
            // two-segment path - including this service's own REST route /v1/ping - and because it
            // accepts POST it becomes a live candidate for verbs the REST route does not map. The
            // observable damage is precise: a POST to the ping route stops answering 405 Method Not
            // Allowed, because routing no longer sees a candidate set rejected solely on method, and
            // answers 404 from the gRPC handler instead. Since the port carries BOTH protocols by
            // design, the REST surface's HTTP semantics must not be silently reshaped by a gRPC
            // wildcard, so the catch-all is switched off.
            //
            // WHAT THIS COSTS IS ALMOST NOTHING, which is why it is the right trade. An AUTHENTICATED
            // caller naming a service this server does not host now falls through to routing and
            // receives HTTP 404, and every conforming gRPC client already maps a 404 onto UNIMPLEMENTED,
            // so the status it surfaces is unchanged. An UNAUTHENTICATED caller receives 401 either way,
            // because the fallback policy above applies to a request that matched no endpoint just as it
            // does to one that did - which is the deny-by-default posture working as intended and is
            // also why an unknown path discloses nothing about whether it exists. Unknown METHODS on a
            // service that IS hosted are unaffected and keep answering a proper UNIMPLEMENTED status,
            // because their route begins with a literal service name that cannot collide with a REST
            // path.
            options.IgnoreUnknownServices = true;
        });

        services.TryAddSingleton<PersistenceStatusInterceptor>();

        services.AddPersistenceHealthChecks();
        services.AddProblemDetails();

        return services;
    }
}

/// <summary>
/// The route table: the two REST diagnostics and the four gRPC contracts.
/// </summary>
internal static class PersistenceEndpointRouteExtensions
{
    /// <summary>
    /// Maps every route this service publishes.
    /// </summary>
    /// <param name="app">The application to map onto.</param>
    /// <returns>The same application, for chaining.</returns>
    /// <remarks>
    /// <para>
    /// TWO REST ROUTES AND FOUR gRPC SERVICES, AND NOTHING ELSE. Each REST route's own authorization
    /// posture is declared inside its endpoint file, where it is exercised: the readiness probe is
    /// ANONYMOUS, because it is the probe the orchestration health condition gates the gateway on and a
    /// probe that needed a token could never satisfy a container health check; and the ping route
    /// REQUIRES a token and answers 401 without one, which is a published contract that is explicitly
    /// tested and must never be relaxed for convenience.
    /// </para>
    /// <para>
    /// THE READINESS PROBE AGGREGATES NO UPSTREAM, BY DESIGN. Persistence is the BOTTOM of the call
    /// topology: nothing but DataServices calls it, and the only thing it reaches outward for is
    /// Security's published key set. Aggregating an upstream verdict here would invert the topology
    /// and create a readiness cycle - the gateway waits on this service, and this service would be
    /// waiting on something that waits on the gateway. Upstream aggregation is the gateway's job and
    /// only the gateway's.
    /// </para>
    /// <para>
    /// ALL FOUR gRPC SERVICES REQUIRE AUTHORIZATION EXPLICITLY (constraint C-G). The fallback policy
    /// already closes the door, so these calls are belt-and-braces rather than load bearing - and that
    /// is precisely why they are here: an explicit requirement at the mapping site is auditable by
    /// reading one screen, whereas relying on a policy declared elsewhere means proving a negative.
    /// None of the four implementation classes carries an authorization attribute of its own, so this
    /// is the single place the requirement is stated.
    /// </para>
    /// </remarks>
    internal static WebApplication MapPersistenceEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapHealthEndpoints();
        app.MapPingEndpoints();

        // C-05 retrieval, C-06 update, C-07 command, C-08 transaction. The four contracts this service
        // exists to serve, each deriving from a generated base in the published contracts project.
        app.MapGrpcService<QueryService>().RequireAuthorization();
        app.MapGrpcService<UpdateService>().RequireAuthorization();
        app.MapGrpcService<CommandService>().RequireAuthorization();
        app.MapGrpcService<TransactionService>().RequireAuthorization();

        return app;
    }
}

/// <summary>
/// The startup gate: the structural preconditions this service refuses to start without.
/// </summary>
/// <remarks>
/// <para>
/// A STRUCTURAL FAULT IS NOT A TRANSIENT FAULT, and the difference decides the whole posture here. A
/// missing key set while Security is still booting is transient and is handled by fetching metadata
/// lazily. A data directory that cannot be written, a deployment that names no token authority at all,
/// or a chunk size the service's own published contract would reject are none of them going to improve
/// by themselves, and a process that starts anyway merely converts a startup failure an operator would
/// see into a request failure a user would see. So each is settled before the pipeline is built, and a
/// failure ends the process - the managed equivalent of the legacy application object's
/// terminate-on-structural-fault behaviour [<c>ws_objects/pfw.pbl.src/pfw.sra:L111-L144</c>].
/// </para>
/// <para>
/// THREE PRECONDITIONS, AND ONLY ONE OF THEM IS ENFORCED BY CODE IN THIS FILE. That split is
/// deliberate and was established by testing each fault rather than by reading:
/// </para>
/// <list type="bullet">
/// <item>
/// THE TOKEN AUTHORITY is enforced by <see cref="PersistenceOptions"/>'s own validator, because
/// <see cref="JwtOptions.Authority"/> carries a required-value annotation. Re-checking it here would be
/// duplicated logic that can never run, so it is not re-checked.
/// </item>
/// <item>
/// THE RETRIEVAL CHUNK SIZE is likewise enforced by that validator, whose message quotes the legacy
/// guard and its locator. Same reasoning: not re-checked here.
/// </item>
/// <item>
/// THE STORAGE DIRECTORY'S WRITABILITY is enforced HERE and nowhere else. The options type can require
/// the setting to be present, but no annotation can establish that a path is writable by this process -
/// that needs a probe against the real file system, which is what this type performs.
/// </item>
/// </list>
/// <para>
/// WHAT MAKES THE FIRST TWO LAND AT STARTUP RATHER THAN ON FIRST USE IS THIS TYPE ALL THE SAME. Reading
/// <c>Value</c> on the bound options is what forces the validator to run, and this gate does that first,
/// before its own probe. So all three faults terminate the process during composition, which is the
/// requirement; only the third needs code here to do it. Both halves of that claim were verified by
/// running the service with each fault injected in turn.
/// </para>
/// </remarks>
internal static class PersistenceStartupGate
{
    /// <summary>The configuration section the inbound-token settings bind from.</summary>
    internal const string JwtSectionName = "Jwt";

    /// <summary>
    /// The optional explicit metadata-address key.
    /// </summary>
    /// <remarks>
    /// Not a member of <see cref="JwtOptions"/>, deliberately. The cross-service configuration
    /// coherence suite pins the option leaves this service's settings files may carry, so an override
    /// that no shipped settings file declares is read straight from configuration rather than being
    /// added to the bound type and to every file that binds it. It OVERRIDES where the key set is
    /// fetched from; it does not replace the authority, which the options validator requires
    /// regardless.
    /// </remarks>
    internal const string MetadataAddressKey = "MetadataAddress";

    /// <summary>
    /// Verifies every structural precondition, terminating the process when one fails.
    /// </summary>
    /// <param name="services">The composed container.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a precondition fails. Escaping a composition root, this ends the process with a
    /// non-zero exit code, which is what an orchestrator's restart policy reads.
    /// </exception>
    internal static void ValidatePersistenceStructuralPreconditions(this IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        ILogger logger = services
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(PersistenceStartupGate).FullName!);

        // THIS LINE IS THE ONE THAT MAKES THE OTHER TWO PRECONDITIONS FAIL FAST. Reading Value forces
        // the registered options validator to run, so a missing token authority or an out-of-range
        // chunk size terminates the process HERE, during composition, instead of surfacing on whichever
        // request first happens to read the setting.
        PersistenceOptions options = services.GetRequiredService<IOptions<PersistenceOptions>>().Value;

        ValidateDataDirectory(options, logger);

        logger.LogInformation(
            "Structural preconditions satisfied: the bound configuration validated, and the storage "
            + "directory exists and is writable by this process.");
    }

    /// <summary>
    /// Verifies that the storage directory exists and can actually be written.
    /// </summary>
    /// <param name="options">The bound options.</param>
    /// <param name="logger">The logger the fault is reported through.</param>
    /// <remarks>
    /// <para>
    /// PROBED RATHER THAN INSPECTED. A permission bit says what the file system intends; a write says
    /// what it does. The container image runs as a NON-ROOT user against a mounted volume, and the
    /// combination that actually bites in practice - a volume owned by another uid, so the directory is
    /// present and readable and every write fails - is invisible to an existence check and invisible to
    /// a mode check. So this creates a uniquely named probe file, writes it and deletes it.
    /// </para>
    /// <para>
    /// THE PROBE FILE IS THE ONLY FILE THIS SERVICE EVER DELETES, and it deletes only the file it just
    /// created, by a name that cannot collide with a database. No database file, journal or volume
    /// content is touched, which is what keeps the paired-capture rule intact.
    /// </para>
    /// <para>
    /// CREATING THE DIRECTORY IS ADDITIVE AND IS NOT A RESEED. A mounted volume routinely presents an
    /// empty mount point without the service's own subdirectory, so creating a missing directory is
    /// ordinary first-run behaviour and destroys nothing. What is NOT done is creating, deleting or
    /// seeding a database inside it.
    /// </para>
    /// </remarks>
    private static void ValidateDataDirectory(PersistenceOptions options, ILogger logger)
    {
        // Not guarded for emptiness, because it cannot be empty by the time this runs: the setting
        // carries a required-value annotation, that annotation trims before testing, and the validator
        // enforcing it has already run at the Value read above. A guard here would be a branch no test
        // could ever reach. Trimming is retained purely to normalise a stray space in an environment
        // variable, and a path that somehow still resolved to nothing would be caught by the probe's
        // own argument handling below rather than slipping through.
        string directory = (options.Sqlite.DataDirectory ?? string.Empty).Trim();

        string probe = Path.Combine(
            directory,
            $".powerframework-persistence-writability-probe-{Guid.NewGuid():n}");

        try
        {
            _ = Directory.CreateDirectory(directory);

            File.WriteAllText(probe, string.Empty);
        }
        catch (Exception error) when (error is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or ArgumentException)
        {
            // The path is named because an operator needs it to fix the mount, and a directory path is
            // not a credential. The underlying error is attached as the exception rather than
            // interpolated, so a log pipeline keeps its type and its errno.
            Terminate(
                logger,
                $"The configured storage directory '{directory}' is not writable by this process, so "
                + "no database could be opened, created or updated there. The container image runs as "
                + "a NON-ROOT user, so the usual cause is a mounted volume owned by another user; "
                + "correct the volume's ownership or permissions and restart. This is a structural "
                + "fault rather than a transient one, so the process is terminating instead of "
                + "starting and failing every request.",
                error);

            return;
        }
        finally
        {
            TryDeleteProbe(probe);
        }
    }

    /// <summary>
    /// Removes the writability probe file, ignoring a failure to do so.
    /// </summary>
    /// <param name="probe">The probe file's full path.</param>
    /// <remarks>
    /// A PROBE THAT CANNOT BE CLEANED UP IS NOT A STARTUP FAULT. The directory has already proven
    /// writable by this point, which is the only thing the check exists to establish, so a leftover
    /// zero-byte file with a name no other component reads is not worth refusing to start over. The
    /// suppression is narrow, and it deliberately does not extend to the write itself.
    /// </remarks>
    private static void TryDeleteProbe(string probe)
    {
        try
        {
            File.Delete(probe);
        }
        catch (Exception error) when (error is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or ArgumentException)
        {
            // Intentionally ignored - see the remarks. Nothing downstream reads this path.
        }
    }

    /// <summary>
    /// Reports a structural fault and ends the process.
    /// </summary>
    /// <param name="logger">The logger the fault is reported through.</param>
    /// <param name="reason">The operator-facing explanation.</param>
    /// <param name="cause">The underlying failure, when there is one.</param>
    /// <exception cref="InvalidOperationException">Always thrown.</exception>
    /// <remarks>
    /// LOGGED AND THEN THROWN, IN THAT ORDER, because the log record is the artifact an operator reads
    /// and it must exist even if the terminating exception is reported differently by whatever hosts
    /// the process. NO REJECTED VALUE IS EVER QUOTED except a directory path, which is not a
    /// credential: a message that echoed a token authority, a connection string or a password would
    /// put in the log exactly what constraint C-F exists to keep out of it.
    /// </remarks>
    [DoesNotReturn]
    private static void Terminate(ILogger logger, string reason, Exception? cause = null)
    {
        logger.LogCritical(
            cause,
            "Refusing to start: {Reason} Configured values are deliberately not quoted.",
            reason);

        throw new InvalidOperationException(reason, cause);
    }
}

// ==================================================================================================
//  THE CENTRAL STATUS MAPPING
// ==================================================================================================

/// <summary>
/// The one place a fault leaving any of the four gRPC contracts becomes a status.
/// </summary>
/// <remarks>
/// <para>
/// CENTRAL BY CONSTRUCTION, NOT BY CONVENTION. Registered once at the server level, this interceptor
/// wraps all four contracts, so the mapping cannot drift between them the way four copies of the same
/// try/catch inevitably would. Every implementation is free to return its own contract-shaped result
/// for a fault it understands; this exists for the ones nobody anticipated.
/// </para>
/// <para>
/// THE MAPPING THAT MATTERS MOST IS THE ONE THIS TYPE DOES NOT PERFORM. An optimistic-concurrency
/// mismatch is <see cref="StatusCode.Aborted"/> carrying a structured conflict detail in the trailers -
/// the canonical gRPC-to-HTTP mapping the gateway projects as 409 - and the update contract builds
/// that status itself, precisely because only it holds the current row state the detail must carry. So
/// a status a handler has already chosen passes through here UNTOUCHED: rewriting it would strip the
/// trailers and turn a 409 with evidence into a bare 500, and no silent overwrite is permitted
/// anywhere in this system.
/// </para>
/// <para>
/// ⚠ REVIEW INVARIANT, RECORDED HERE BECAUSE THIS IS THE ONLY PLACE IT COULD EVER SURFACE. The
/// published contracts project declares NO project reference of its own - deliberately none to the
/// shared kernel - so nothing anywhere in the repository enforces that the return-code enumeration
/// generated from <c>common.v1.proto</c> and the return-code constants in
/// <c>PowerFramework.Shared.Kernel</c> agree NUMERICALLY. This project is the first and only place both
/// are referenced together. A divergence between them is a silent wire-compatibility defect that no
/// compiler will catch and that no unit test on either side alone can catch either, because each side
/// is self-consistent. The two must therefore be reconciled BY INSPECTION whenever either changes.
/// </para>
/// <para>
/// REDACTION IS APPLIED TO EVERY FAULT THAT LEAVES BY THIS PATH (baseline requirement). The legacy
/// error structure's statement field carries the COMPLETE generated statement including interpolated
/// literal values, and the legacy logger performed no redaction at all. An exception message can carry
/// the same thing, so the message is passed through the redactor before it reaches a log record or a
/// status detail. Redacting a diagnostic field changes nothing a caller can observe about the
/// operation's outcome, which is what makes it the permitted kind of safety improvement rather than a
/// behaviour change.
/// </para>
/// </remarks>
internal sealed class PersistenceStatusInterceptor : Interceptor
{
    /// <summary>The detail returned for a fault with no contract-shaped status of its own.</summary>
    /// <remarks>
    /// FIXED AND UNINFORMATIVE ON PURPOSE. A caller cannot act on this service's internal fault, and a
    /// detail assembled from an exception message is the classic way a generated statement, a
    /// connection parameter or a credential leaves the process. The redacted text goes to the log,
    /// where an operator can correlate it; the wire gets a constant.
    /// </remarks>
    internal const string UnhandledFaultDetail =
        "The persistence service failed to complete the call. The fault has been recorded with its "
        + "statement text redacted; no diagnostic detail is returned over the wire.";

    /// <summary>The detail returned when a framework assertion fails.</summary>
    internal const string AssertionFaultDetail =
        "The persistence service failed an internal assertion and is shutting down. Retry against a "
        + "healthy instance.";

    /// <summary>The detail returned when the caller's own deadline or cancellation ended the call.</summary>
    internal const string CancelledDetail = "The call was cancelled by its caller or its deadline.";

    /// <summary>Redacts statement text before it reaches a log record or a status detail.</summary>
    private readonly ISqlRedactor _redactor;

    /// <summary>Ends the process when an assertion proves an invariant broken.</summary>
    private readonly IHostApplicationLifetime _lifetime;

    /// <summary>Records the fault.</summary>
    private readonly ILogger<PersistenceStatusInterceptor> _logger;

    /// <summary>Initializes the interceptor.</summary>
    /// <param name="redactor">The statement redactor.</param>
    /// <param name="lifetime">The host lifetime used to terminate on a structural fault.</param>
    /// <param name="logger">The logger faults are recorded through.</param>
    public PersistenceStatusInterceptor(
        ISqlRedactor redactor,
        IHostApplicationLifetime lifetime,
        ILogger<PersistenceStatusInterceptor> logger)
    {
        _redactor = redactor ?? throw new ArgumentNullException(nameof(redactor));
        _lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(continuation);

        try
        {
            return await continuation(request, context).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            throw Translate(error, context);
        }
    }

    /// <inheritdoc/>
    public override async Task<TResponse> ClientStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream,
        ServerCallContext context,
        ClientStreamingServerMethod<TRequest, TResponse> continuation)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(continuation);

        try
        {
            return await continuation(requestStream, context).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            throw Translate(error, context);
        }
    }

    /// <inheritdoc/>
    public override async Task ServerStreamingServerHandler<TRequest, TResponse>(
        TRequest request,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        ServerStreamingServerMethod<TRequest, TResponse> continuation)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(continuation);

        try
        {
            await continuation(request, responseStream, context).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            throw Translate(error, context);
        }
    }

    /// <inheritdoc/>
    public override async Task DuplexStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        DuplexStreamingServerMethod<TRequest, TResponse> continuation)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(continuation);

        try
        {
            await continuation(requestStream, responseStream, context).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            throw Translate(error, context);
        }
    }

    /// <summary>
    /// Maps a fault onto the status the caller receives.
    /// </summary>
    /// <param name="error">The fault that escaped the handler.</param>
    /// <param name="context">The call the fault escaped from.</param>
    /// <returns>The exception to throw in its place.</returns>
    private Exception Translate(Exception error, ServerCallContext context)
    {
        // ALREADY CONTRACT SHAPED. Passed through with its status and its trailers intact - this is
        // the Aborted-plus-conflict-detail path, and touching it here is how a 409 with evidence
        // becomes an opaque 500.
        if (error is RpcException)
        {
            return error;
        }

        // A BROKEN INVARIANT, NOT A BROKEN REQUEST. The legacy terminates the application after
        // decoding an assertion payload [ws_objects/pfw.pbl.src/pfw.sra:L111-L144], and that posture is
        // preserved: the host is asked to stop, so the process ends and the orchestrator replaces it.
        // StopApplication is used rather than an abrupt exit precisely because it lets in-flight calls
        // and the readiness probe observe the shutdown instead of being severed mid-write. The caller
        // is told immediately rather than left waiting for a socket to close.
        if (error is AssertionFailure assertion)
        {
            _logger.LogCritical(
                assertion,
                "A framework assertion failed while serving {Method}. The invariant it guards is broken, "
                + "so this instance is shutting down rather than continuing in a state it believes "
                + "impossible.",
                context.Method);

            _lifetime.StopApplication();

            return new RpcException(new Status(StatusCode.Internal, AssertionFaultDetail));
        }

        // THE CALLER'S OWN DOING. A cancellation raised while the call's token is cancelled is the
        // caller hanging up or its deadline expiring, not a server fault, so it is reported as
        // cancelled and logged at debug. A cancellation raised WITHOUT the token being cancelled is a
        // genuine internal fault and deliberately falls through to the general arm below.
        if (error is OperationCanceledException && context.CancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug(
                "{Method} was cancelled by its caller before it completed.",
                context.Method);

            return new RpcException(new Status(StatusCode.Cancelled, CancelledDetail));
        }

        // EVERYTHING ELSE. The message is redacted BEFORE it reaches the log record, because an
        // exception raised anywhere near statement generation can carry the complete generated
        // statement with its literal values interpolated - which is exactly the field the legacy logged
        // verbatim. The exception object itself is attached so its type and stack survive for
        // diagnosis, and the wire receives a constant that discloses nothing.
        _logger.LogError(
            error,
            "{Method} failed with an unhandled {FaultType}. Redacted message: {RedactedMessage}",
            context.Method,
            error.GetType().FullName,
            _redactor.Redact(error.Message));

        return new RpcException(new Status(StatusCode.Internal, UnhandledFaultDetail));
    }
}

// ==================================================================================================
//  THE TASK LAYER'S TWO HALVES, AND WHY THEY STAY TWO
//
//  The legacy encodes execution context as a CONTRACT rather than as commentary, and it does so by
//  shipping every concurrency class TWICE. Measured across the SQL task library: SIX objects are
//  annotated as worker-thread affine, EXACTLY ONE as main-thread affine, and FOUR as calling-thread
//  proxies [the `$PBExportComments$` headers of ws_objects/pfw.thread.ext.pbl.src/*.sru]. The pair
//  exists so that no object is ever touched from two threads.
//
//  THE PAIR IS THEREFORE NOT FLATTENED INTO ONE ASYNC METHOD. It would be easy to collapse a
//  worker-side task and its caller-side proxy into a single `await`-ing method and call the result
//  equivalent. It is not equivalent: the annotation is the contract, and flattening it discards the one
//  piece of information that says which side of a marshalling boundary a given member may be touched
//  from. So the two halves below stay two distinct types, and the ONLY thing that crosses between them
//  is a fault sink - which is precisely what a marshalling boundary should look like.
//
//  ONE PAIR PER TASK, NOT ONE PAIR PER PROCESS. Both halves are created per task by the factory rather
//  than shared, because the worker-side host owns the task's datastore cache
//  [Tasks/SqlTaskBase.cs, the cache is held in the host's own data bag] and sharing one cache across
//  concurrent calls would be a data race that no amount of locking at the bag level would fix, since
//  the cached object itself is not concurrent. Isolation per task is both the safe answer and the
//  faithful one: in the legacy, one thread owns one host owns one cache.
// ==================================================================================================

/// <summary>
/// The worker-side execution context a SQL task runs against.
/// </summary>
/// <remarks>
/// <para>
/// This is the port of the legacy worker-thread object as a SQL task sees it: a data bag, a
/// notification sink, and the identity of the caller-side proxy to report database errors to. It is
/// REAL behaviour rather than a placeholder - the data bag genuinely carries the task's datastore cache
/// between calls on the same task, which is the whole reason the legacy put it on the thread.
/// </para>
/// <para>
/// <see cref="IsMainThread"/> IS FALSE, AND THAT IS THE AFFINITY CONTRACT BEING HONOURED RATHER THAN
/// IGNORED. Every SQL task in the legacy is annotated worker-thread affine; only the datastore CARRIER
/// is main-thread affine. A host that claimed to be the main thread would invite a task to take the
/// main-thread branch of code that was written for a UI message pump this service does not have.
/// </para>
/// <para>
/// <see cref="IsCancelled"/> IS FALSE BECAUSE CANCELLATION DOES NOT LIVE HERE. In the legacy the
/// cancellation flag belongs to the thread, so the thread object is where a task asks about it. In this
/// service a cancellation is per CALL, arrives as the call's own token, and is passed explicitly into
/// every asynchronous member the task exposes - so the token is the authority and this flag would be a
/// second, staler copy of it. Reporting false here means "this host is not the cancellation authority",
/// which is true.
/// </para>
/// </remarks>
internal sealed class PersistenceSqlTaskHost : ISqlTaskHost
{
    /// <summary>
    /// The one-based task position this host reports.
    /// </summary>
    /// <remarks>
    /// ONE, BECAUSE THE LEGACY INDEX IS ONE-BASED and this host owns exactly one task. One-based
    /// indexing is the single most dangerous mechanical hazard in this port, so the value is named
    /// rather than written as a bare literal at the property.
    /// </remarks>
    internal const int SoleTaskIndex = 1;

    /// <summary>The task's data bag, keyed exactly as the legacy keys it.</summary>
    /// <remarks>
    /// Concurrent because a task's asynchronous members may resume on different thread-pool threads
    /// even though only one logical task ever uses this instance. Ordinal comparison because the legacy
    /// key space is case sensitive and a culture-sensitive comparison could silently merge two keys.
    /// </remarks>
    private readonly ConcurrentDictionary<string, object?> _data = new(StringComparer.Ordinal);

    /// <summary>The sink faults are reported to, or <see langword="null"/> when none was supplied.</summary>
    private readonly IQueryFaultSink? _faults;

    /// <summary>Records notifications and errors that no sink consumes.</summary>
    private readonly ILogger<PersistenceSqlTaskHost> _logger;

    /// <summary>Initializes a host with no fault sink and no caller-side proxy.</summary>
    /// <param name="logger">The logger notifications and errors are recorded through.</param>
    /// <remarks>
    /// The container-resolvable shape. A host obtained straight from the container serves a task that
    /// reports its faults through its own return codes rather than through a sink.
    /// </remarks>
    public PersistenceSqlTaskHost(ILogger<PersistenceSqlTaskHost> logger)
        : this(logger, faults: null, parentTasking: null)
    {
    }

    /// <summary>Initializes a host bound to a fault sink and a caller-side proxy.</summary>
    /// <param name="logger">The logger notifications and errors are recorded through.</param>
    /// <param name="faults">The sink framework errors are forwarded to, or <see langword="null"/>.</param>
    /// <param name="parentTasking">The caller-side proxy, or <see langword="null"/> when unattached.</param>
    internal PersistenceSqlTaskHost(
        ILogger<PersistenceSqlTaskHost> logger,
        IQueryFaultSink? faults,
        ISqlTaskProxy? parentTasking)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _faults = faults;
        ParentTasking = parentTasking;
    }

    /// <inheritdoc/>
    public bool IsMainThread => false;

    /// <inheritdoc/>
    public bool IsCancelled => false;

    /// <inheritdoc/>
    public int TaskIndex => SoleTaskIndex;

    /// <inheritdoc/>
    public ISqlTaskProxy? ParentTasking { get; }

    /// <inheritdoc/>
    /// <remarks>
    /// A SIBLING LOOKUP HAS NO ANSWER HERE, AND THAT IS THE ACCURATE ANSWER. The legacy resolves a
    /// sibling task through the thread's own task table; in this service the task tables are the gRPC
    /// handle registries, reached by handle rather than by position, and this host owns exactly one
    /// task. <see cref="RetCode.E_OUT_OF_BOUND"/> is the contract's own code for a position that names
    /// nothing, so a caller learns the truth - there is no task at that index - rather than receiving
    /// some other task.
    /// </remarks>
    public long GetTask(int index, out SqlTaskBase? task)
    {
        task = null;

        return RetCode.E_OUT_OF_BOUND;
    }

    /// <inheritdoc/>
    public bool HasData(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return _data.ContainsKey(name);
    }

    /// <inheritdoc/>
    public object? GetData(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return _data.TryGetValue(name, out object? value) ? value : null;
    }

    /// <inheritdoc/>
    public long SetData(string name, object? data)
    {
        ArgumentNullException.ThrowIfNull(name);

        _data[name] = data;

        return RetCode.OK;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Returns the event-continue value rather than a return code, because this member sits on the
    /// legacy EVENT channel and its alphabet is continue-or-stop, not the return-code algebra. Nothing
    /// needs preparing on a host that holds a dictionary, so continuing is the whole behaviour.
    /// </remarks>
    public long OnPrepare() => DataWindowBufferStore.EventContinue;

    /// <inheritdoc/>
    /// <remarks>
    /// Clears the data bag, which releases the datastore cache the task built. Deterministic release
    /// matters here: the cached carriers are disposable, and waiting for a finaliser would hold storage
    /// resources for an unbounded time after the task that owned them finished.
    /// </remarks>
    public void OnUninit() => _data.Clear();

    /// <inheritdoc/>
    /// <remarks>
    /// FORWARDED TO THE SINK WHEN THERE IS ONE, WHICH IS THE MARSHALLING BOUNDARY IN ACTION: the
    /// worker-side task raises, this worker-side host forwards, and the caller-side collector reads. It
    /// is also logged, because a fault nobody collected must still be visible to an operator. The text
    /// is passed through unchanged rather than redacted, because this channel carries a FRAMEWORK
    /// diagnostic and never a generated statement - the statement-bearing channel is the database-error
    /// one, and it is redacted where it is logged.
    /// </remarks>
    public long OnError(long errCode, string errInfo)
    {
        string text = errInfo ?? string.Empty;

        _faults?.OnError(errCode, text);

        _logger.LogError(
            "A SQL task reported framework error {ErrorCode}: {ErrorText}",
            errCode,
            text);

        return DataWindowBufferStore.EventContinue;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Notifications are progress and lifecycle signals, so they are recorded at trace level: the
    /// legacy throttles them to roughly one per hundred milliseconds per retrieval
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds_mt.sru:L37</c>], which is
    /// still far too many for any higher level.
    /// </remarks>
    public long OnNotify(long notifyCode, long payload, string text)
    {
        _logger.LogTrace(
            "A SQL task raised notification {NotifyCode} with payload {Payload}: {Text}",
            notifyCode,
            payload,
            text ?? string.Empty);

        return DataWindowBufferStore.EventContinue;
    }
}

/// <summary>
/// The caller-side half of the pair: it collects database errors on behalf of one query task.
/// </summary>
/// <remarks>
/// The whole of the legacy caller-side proxy's database-error contract is one member, so this type is
/// one member. It exists as its OWN type rather than as a second interface on the host precisely
/// because the two are on opposite sides of the marshalling boundary: the host is worker-side, this is
/// caller-side, and collapsing them would erase the distinction the affinity annotations exist to
/// state.
/// </remarks>
internal sealed class QueryFaultProxy : ISqlTaskProxy
{
    /// <summary>The collector the error is handed to.</summary>
    private readonly IQueryFaultSink _faults;

    /// <summary>Initializes the proxy.</summary>
    /// <param name="faults">The collector database errors are handed to.</param>
    internal QueryFaultProxy(IQueryFaultSink faults) =>
        _faults = faults ?? throw new ArgumentNullException(nameof(faults));

    /// <inheritdoc/>
    /// <remarks>
    /// Handed over BY REFERENCE and stored, never logged here. The error's statement field carries the
    /// complete generated statement including interpolated literal values, and the place that redacts it
    /// for a log record is the task that raised it; a second log record here would be a second
    /// opportunity to leak the same thing (constraint C-F).
    /// </remarks>
    public void OnDbError(in DbErrorData error) => _faults.OnDbError(in error);
}

/// <summary>
/// Composes a retrieval task and its caller-side fault proxy, one pair per request.
/// </summary>
/// <remarks>
/// THE ONE FACTORY THAT BUILDS A REAL TASK, and it is worth saying why it can when the update and
/// command factories below cannot. Retrieval's contract lets the task be constructed first and fail
/// later at the exact operation that needs a capability this phase does not provision, so everything
/// that does NOT need one keeps working: clause modification, the chunk-size guard, both paging
/// rewriters and the count wrapper are pure string transforms over a statement model and are fully
/// reachable through this task. Refusing to construct it at all would take those away for no gain.
/// </remarks>
internal sealed class QueryTaskFactory : IQueryTaskFactory
{
    /// <summary>Resolves a fresh worker-side host for each task.</summary>
    private readonly IServiceProvider _services;

    /// <summary>The shared transaction pool.</summary>
    private readonly TransactionPool _transactionPool;

    /// <summary>The shared datastore factory.</summary>
    private readonly ISqlDataStoreFactory _dataStoreFactory;

    /// <summary>The shared retrieval-hook activator.</summary>
    private readonly ISqlRetrievalHookActivator _hookActivator;

    /// <summary>The injected clock.</summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>The transaction surface retrieval reaches the database through.</summary>
    private readonly IQueryTransactionSurface _transactionSurface;

    /// <summary>The runtime that materialises a result carrier.</summary>
    private readonly IQueryDataWindowRuntime _dataWindowRuntime;

    /// <summary>Both paging rewriters, in dialect order.</summary>
    private readonly IEnumerable<IPagingRewriter> _pagingRewriters;

    /// <summary>The changeset codec.</summary>
    private readonly ChangesetCodec _changesetCodec;

    /// <summary>The statement redactor.</summary>
    private readonly ISqlRedactor _redactor;

    /// <summary>This service's bound configuration.</summary>
    private readonly IOptions<PersistenceOptions> _options;

    /// <summary>Creates task loggers.</summary>
    private readonly ILoggerFactory _loggerFactory;

    /// <summary>Initializes the factory.</summary>
    /// <param name="services">The container, used to resolve a fresh host per task.</param>
    /// <param name="transactionPool">The shared transaction pool.</param>
    /// <param name="dataStoreFactory">The shared datastore factory.</param>
    /// <param name="hookActivator">The shared retrieval-hook activator.</param>
    /// <param name="timeProvider">The injected clock.</param>
    /// <param name="transactionSurface">The transaction surface retrieval uses.</param>
    /// <param name="dataWindowRuntime">The result-carrier runtime.</param>
    /// <param name="pagingRewriters">Both paging rewriters.</param>
    /// <param name="changesetCodec">The changeset codec.</param>
    /// <param name="redactor">The statement redactor.</param>
    /// <param name="options">This service's bound configuration.</param>
    /// <param name="loggerFactory">The factory task loggers are created from.</param>
    public QueryTaskFactory(
        IServiceProvider services,
        TransactionPool transactionPool,
        ISqlDataStoreFactory dataStoreFactory,
        ISqlRetrievalHookActivator hookActivator,
        TimeProvider timeProvider,
        IQueryTransactionSurface transactionSurface,
        IQueryDataWindowRuntime dataWindowRuntime,
        IEnumerable<IPagingRewriter> pagingRewriters,
        ChangesetCodec changesetCodec,
        ISqlRedactor redactor,
        IOptions<PersistenceOptions> options,
        ILoggerFactory loggerFactory)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _transactionPool = transactionPool ?? throw new ArgumentNullException(nameof(transactionPool));
        _dataStoreFactory = dataStoreFactory ?? throw new ArgumentNullException(nameof(dataStoreFactory));
        _hookActivator = hookActivator ?? throw new ArgumentNullException(nameof(hookActivator));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _transactionSurface = transactionSurface
            ?? throw new ArgumentNullException(nameof(transactionSurface));
        _dataWindowRuntime = dataWindowRuntime ?? throw new ArgumentNullException(nameof(dataWindowRuntime));
        _pagingRewriters = pagingRewriters ?? throw new ArgumentNullException(nameof(pagingRewriters));
        _changesetCodec = changesetCodec ?? throw new ArgumentNullException(nameof(changesetCodec));
        _redactor = redactor ?? throw new ArgumentNullException(nameof(redactor));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// BOTH HALVES ARE BUILT HERE AND ONLY THE SINK JOINS THEM. The caller-side proxy is constructed
    /// around the supplied collector, the worker-side host is constructed around the same collector and
    /// the proxy, and the task is constructed around the host. Nothing else passes between the two
    /// halves, which is exactly the property the affinity contract asks for.
    /// </remarks>
    public SqlQueryTask Create(IQueryFaultSink faults)
    {
        ArgumentNullException.ThrowIfNull(faults);

        PersistenceSqlTaskHost host = new(
            _services.GetRequiredService<ILogger<PersistenceSqlTaskHost>>(),
            faults,
            new QueryFaultProxy(faults));

        return new SqlQueryTask(
            host,
            _transactionPool,
            _dataStoreFactory,
            _hookActivator,
            _timeProvider,
            _loggerFactory.CreateLogger<SqlQueryTask>(),
            _transactionSurface,
            _dataWindowRuntime,
            _pagingRewriters,
            _changesetCodec,
            _redactor,
            _options);
    }
}

// ==================================================================================================
//  THE SHIPPED RUNTIME SEAMS - A DOCUMENTED GAP, WHICH IS NOT THE SAME THING AS A STUB
//
//  READ THIS ONCE HERE RATHER THAN SIX TIMES BELOW.
//
//  Two capabilities that the types in this section stand in front of are genuinely not provisioned in
//  this phase, and neither omission is an oversight - each is forced by a constraint that would be
//  VIOLATED by supplying the obvious implementation.
//
//  1. THERE IS NO DBMS TO CONNECT TO, AND PROVIDING ONE WOULD FABRICATE A DATABASE (constraint C-E).
//     The legacy transaction object enumerates exactly TWO database types, SQL Server and Oracle
//     [ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:L60-L61], and SQLite is not in that
//     enumeration at all - the SQLite binding is an entirely separate, independent storage path. Yet
//     NEITHER SQL Server NOR Oracle has a schema, a connection string or one line of DDL anywhere in
//     the repository; only the two constants and the two statement generators exist. So the transaction
//     object's connect verb has no evidenced target, and inventing one - a host name, a catalogue, a
//     credential - is precisely the fabrication the constraint forbids. What those two dialects DO have
//     is their statement generation, and that is preserved in full under Sql/Paging/ as pure string
//     transforms held to byte-exact parity with no instance of either engine running. The evidenced
//     storage path, SQLite, is fully provisioned: see AddPersistenceStorage above, and the readiness
//     probe genuinely reaches it.
//
//  2. THERE IS NO DATAWINDOW RUNTIME TO MATERIALISE, AND BUILDING ONE WOULD IMPLEMENT A DEFERRED
//     SERVICE (constraint C-D). The legacy result carrier IS a DataWindow - the SQL task's carrier
//     derives from `datastore` - so materialising one, generating grid syntax from a statement, or
//     resolving a child carrier all require a live DataWindow engine. That engine belongs to the
//     deferred DesignSystem capability, which must not be implemented in this phase EVEN PARTIALLY and
//     even to stub it out. Between a partial implementation and a documented gap, the documented gap
//     wins.
//
//  WHAT THESE TYPES DO INSTEAD IS ANSWER EACH CONTRACT'S OWN DEFINED NEGATIVE. That is a different
//  thing from a stub, and the difference is observable:
//    * Not one of them throws NotImplementedException, and not one of them is reachable only from a
//      test. Each is the SHIPPED implementation, on the real path, returning a value its own interface
//      already documents as meaning "this provider cannot serve that".
//    * Each negative is a value the CONSUMER already handles: a definition that does not resolve, a
//      carrier that reports a create failure with an error text, a query that answers
//      RetCode.E_NO_IMPLEMENTATION - the same code the legacy's own unsupported-dialect arm returns
//      [n_cst_thread_task_sqlquery.sru:L396-L398] - and a connect that answers a failed SQL state
//      carrying an explanation. So a caller learns a TRUE statement about what happened rather than
//      receiving a fabricated success or an opaque crash.
//    * Nothing silently succeeds. A commit never reports data committed that was never written, which
//      is the one failure mode that would be worse than refusing.
//
//  SUBSTITUTION NEEDS NO EDIT TO THIS FILE. Every one of them is registered with TryAdd, so a
//  deployment that has a real DBMS engine, or a phase that brings a DataWindow runtime, registers its
//  own implementation first and the whole published surface starts serving unchanged.
// ==================================================================================================

/// <summary>
/// The shipped <see cref="IDataObjectRuntime"/>: it resolves no data-object definition.
/// </summary>
/// <remarks>
/// A definition is the six describe-able properties of a DataWindow object - its statement, sort,
/// filter, processing mode, arguments and units - so resolving one requires the deferred DataWindow
/// engine. The interface's own signature documents the negative as a false result with no definition,
/// and the datastore layer turns that into its documented "no such data object" path rather than
/// creating an empty carrier nobody asked for.
/// </remarks>
internal sealed class UnboundDataObjectRuntime : IDataObjectRuntime
{
    /// <inheritdoc/>
    public bool TryResolveDefinition(
        string dataObject,
        [NotNullWhen(true)] out DataObjectDefinition? definition)
    {
        // Validated even though the answer does not depend on it: a caller that supplied nothing has
        // made a different mistake from one that supplied an unbound name, and collapsing the two sends
        // a reader of the resulting diagnostic to the wrong place.
        ArgumentNullException.ThrowIfNull(dataObject);

        definition = null;

        return false;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Answers the datastore layer's own failure value rather than a return code, because this member
    /// sits on the DATASTORE channel whose alphabet is a row count with a negative one for failure. A
    /// zero here would be a lie of the worst kind available - it reads as "retrieved successfully, no
    /// rows matched".
    /// </remarks>
    public long Retrieve(ISqlDataStore data, IReadOnlyList<object?> parameters)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(parameters);

        return DataWindowBufferStore.DataStoreFailure;
    }
}

/// <summary>
/// The shipped <see cref="IQueryDataWindowRuntime"/>: it materialises no result carrier.
/// </summary>
/// <remarks>
/// All three members need the deferred DataWindow engine - one creates a carrier from generated grid
/// syntax, one resolves a child carrier behind a column, and one attaches a transaction to a carrier -
/// and each answers the negative its own signature documents. The create outcome carries an error TEXT
/// as well as a code, so the reason travels with the refusal instead of having to be inferred.
/// </remarks>
internal sealed class UnboundQueryDataWindowRuntime : IQueryDataWindowRuntime
{
    /// <summary>The reason a carrier cannot be created, carried on the outcome itself.</summary>
    internal const string UnboundCarrierText =
        "No DataWindow runtime is bound in this phase, so a result carrier cannot be created from "
        + "generated grid syntax. Retrieval that needs a materialised carrier is unavailable until a "
        + "runtime is registered; statement construction, clause modification and paging rewriting are "
        + "unaffected.";

    /// <inheritdoc/>
    public CarrierCreateOutcome CreateFromSyntax(ISqlDataStore data, string syntax)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(syntax);

        return new CarrierCreateOutcome(DataWindowBufferStore.DataStoreFailure, UnboundCarrierText);
    }

    /// <inheritdoc/>
    public bool TryGetChild(ISqlDataStore data, string columnName, out DataWindowBufferStore? child)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(columnName);

        child = null;

        return false;
    }

    /// <inheritdoc/>
    public long AttachTransaction(ISqlDataStore data, IPooledTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(transaction);

        return DataWindowBufferStore.DataStoreFailure;
    }
}

/// <summary>
/// The shipped <see cref="IQueryTransactionSurface"/>: it generates no grid syntax and runs no query.
/// </summary>
/// <remarks>
/// The two productive members are the DataWindow engine's statement-to-syntax conversion and the
/// transaction object's own query verb, so both are unavailable for the two reasons given above. The
/// two RETRIEVAL HOOKS are different in kind and are treated differently: they are notifications with a
/// veto, so the correct behaviour for a surface with nothing to notify is to CONTINUE - vetoing a
/// retrieval that has not been asked to stop would invent a refusal, and the legacy's own convention is
/// that a hook nobody implemented does not prevent anything.
/// </remarks>
internal sealed class UnboundQueryTransactionSurface : IQueryTransactionSurface
{
    /// <summary>The reason grid syntax cannot be generated.</summary>
    internal const string UnboundSyntaxText =
        "No DataWindow runtime is bound in this phase, so grid syntax cannot be generated from a "
        + "statement.";

    /// <summary>The reason a query cannot be executed through the transaction object.</summary>
    internal const string UnprovisionedQueryText =
        "No database engine is provisioned for the transaction-object path in this phase, so this query "
        + "cannot be executed. The legacy path enumerates SQL Server and Oracle only, and neither has a "
        + "schema, a connection string or any DDL in the repository; the evidenced SQLite path is "
        + "reached through the storage seam instead.";

    /// <inheritdoc/>
    public GridSyntaxOutcome GridSyntaxFromSql(IPooledTransaction transaction, string sql)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(sql);

        // Empty syntax paired with a non-empty error text, which is how the outcome type spells "no
        // syntax, and here is why" - the consumer tests the text rather than guessing from the emptiness.
        return new GridSyntaxOutcome(string.Empty, UnboundSyntaxText);
    }

    /// <inheritdoc/>
    public ValueTask<CountQueryOutcome> Query(
        IPooledTransaction transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(sql);

        // The caller's cancellation still wins over a refusal, because a cancelled call must report
        // cancellation rather than a capability verdict it never waited for.
        cancellationToken.ThrowIfCancellationRequested();

        return ValueTask.FromResult(
            new CountQueryOutcome(RetCode.E_NO_IMPLEMENTATION, null, UnprovisionedQueryText));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// CONTINUE, NOT REFUSE. This is a vetoable notification and there is nothing here to notify, so the
    /// only faithful answer is the one that prevents nothing.
    /// </remarks>
    public long RaiseBeforeRetrieve(IPooledTransaction transaction, DataWindowCarrier data)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(data);

        return RetCode.OK;
    }

    /// <inheritdoc/>
    /// <remarks>A notification with no return contract and nothing to notify, so genuinely nothing.</remarks>
    public void RaiseAfterRetrieve(IPooledTransaction transaction, DataWindowCarrier data, long rowCount)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(data);
    }
}

/// <summary>
/// The shipped <see cref="ITransactionEngine"/>: it connects to nothing, because nothing is provisioned.
/// </summary>
/// <remarks>
/// <para>
/// See reason 1 in the section banner above: the transaction-object path enumerates SQL Server and
/// Oracle and neither has any evidence in the repository, so a connect verb here has no target that
/// would not have to be invented.
/// </para>
/// <para>
/// THE VERBS ARE SPLIT DELIBERATELY, AND THE SPLIT IS THE HONEST PART. Connect, commit and execute
/// FAIL, because each of them would otherwise report work that did not happen - and a commit that
/// claims success is the single most damaging lie this type could tell. Disconnect and rollback SUCCEED,
/// because they are idempotent unwinds and there is genuinely nothing left to unwind: failing them would
/// make every cleanup path report an error it cannot act on, and the legacy tolerates a disconnect of
/// something that never connected in exactly the same way.
/// </para>
/// <para>
/// THE DIALECT ANSWER PRESERVES THE LEGACY CLASSIFICATION RATHER THAN ADDING AN ARM. Whatever descriptor
/// the caller supplies is echoed back through <see cref="Dbms"/>, so the paging dispatcher sees the
/// caller's own dialect string and classifies it exactly as the legacy does - anything that does not
/// contain ORACLE is the SQL Server type, and there is no third arm and specifically none for SQLite.
/// </para>
/// </remarks>
internal sealed class UnprovisionedTransactionEngine : ITransactionEngine
{
    /// <summary>The explanation carried on every failing SQL state this engine produces.</summary>
    internal const string UnprovisionedText =
        "No database engine is provisioned for the transaction-object path in this phase. The legacy "
        + "path enumerates SQL Server and Oracle only, and neither has a schema, a connection string or "
        + "any DDL in the repository, so connecting would require inventing a target. Register an engine "
        + "implementation to enable this path; the evidenced SQLite storage path is unaffected and is "
        + "reached through the storage seam.";

    /// <summary>
    /// The database code carried on a failing state.
    /// </summary>
    /// <remarks>
    /// The legacy's own not-implemented code, reused here rather than inventing a number. It cannot
    /// collide with a genuine vendor code, and a caller that already recognises the unsupported-dialect
    /// arm recognises this too.
    /// </remarks>
    internal const long UnprovisionedDbCode = RetCode.E_NO_IMPLEMENTATION;

    /// <inheritdoc/>
    /// <remarks>Zero is the legacy's unconnected handle, and no connection is ever established here.</remarks>
    public int DbHandle => 0;

    /// <inheritdoc/>
    public string Dbms { get; private set; } = string.Empty;

    /// <inheritdoc/>
    /// <remarks>
    /// Stored and honoured as a SETTING rather than acted upon. The descriptor's auto-commit choice is
    /// part of the session state a caller can read back, and losing it would make the descriptor round
    /// trip lossily for no reason connected to the missing engine.
    /// </remarks>
    public bool AutoCommit { get; set; }

    /// <inheritdoc/>
    /// <remarks>
    /// Only the dialect is retained, and deliberately only the dialect. The descriptor also carries the
    /// server, the user, the connection parameters and the log password, and none of those has any use
    /// on an engine that will not connect - so none is copied anywhere, which keeps the credential out
    /// of this object's state entirely (constraint C-F).
    /// </remarks>
    public void ApplyConnectionFields(in TransactionData descriptor) => Dbms = descriptor.Dbms;

    /// <inheritdoc/>
    public SqlState Connect() => SqlState.Failed(UnprovisionedDbCode, UnprovisionedText);

    /// <inheritdoc/>
    /// <remarks>An idempotent unwind of a session that was never established - see the remarks above.</remarks>
    public SqlState Disconnect() => SqlState.Succeeded();

    /// <inheritdoc/>
    public SqlState Commit() => SqlState.Failed(UnprovisionedDbCode, UnprovisionedText);

    /// <inheritdoc/>
    /// <remarks>Nothing was written, so there is nothing to undo - see the remarks above.</remarks>
    public SqlState Rollback() => SqlState.Succeeded();

    /// <inheritdoc/>
    public SqlState Execute(string sqlCommand)
    {
        // The statement is deliberately neither stored nor logged. It may carry interpolated literal
        // values whenever the connection disabled bind variables, and an engine that cannot run it has
        // no reason whatsoever to retain it (constraint C-F).
        ArgumentNullException.ThrowIfNull(sqlCommand);

        return SqlState.Failed(UnprovisionedDbCode, UnprovisionedText);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Nothing unmanaged is held, because nothing was ever opened. Implemented rather than omitted
    /// because the interface requires it and because the pool disposes every engine it creates.
    /// </remarks>
    public void Dispose()
    {
    }
}

/// <summary>
/// The shipped <see cref="IUpdateTaskFactory"/>: it creates no update task in this phase.
/// </summary>
/// <remarks>
/// <para>
/// An update task needs BOTH of the missing capabilities at once, which is why this factory refuses
/// rather than constructing something that would fail later. Its carrier is a DataWindow with live
/// buffers and per-item statuses - the update contract depends on the ORIGINAL value of every marked
/// column, which only a carrier holds - and its statement execution needs the transaction-object path.
/// Handing back a task that could not carry original values would be worse than refusing, because the
/// concurrency check is the one thing this contract exists to get right and a task without original
/// values would silently overwrite.
/// </para>
/// <para>
/// THE REFUSAL IS THE CONTRACT'S OWN. The interface returns a code with a null surface, the consumer
/// already logs that and answers with the code in its response, and the code chosen is the legacy's own
/// not-implemented value - so a caller receives a definite, machine-readable answer instead of a
/// timeout, an exception or a task that misbehaves on first use.
/// </para>
/// </remarks>
internal sealed class UnboundUpdateTaskFactory : IUpdateTaskFactory
{
    /// <inheritdoc/>
    public long TryCreate(string sessionId, out IUpdateTaskSurface? task)
    {
        ArgumentNullException.ThrowIfNull(sessionId);

        task = null;

        return RetCode.E_NO_IMPLEMENTATION;
    }
}

/// <summary>
/// The shipped <see cref="ICommandTaskFactory"/>: it creates no command task in this phase.
/// </summary>
/// <remarks>
/// A command task is a PAIR - a worker-side task and its caller-side proxy - and the proxy's host is the
/// calling-thread substrate object, which is the very thing a headless service does not have: there is
/// no calling thread with a live worker attached to marshal against. Flattening the pair to avoid that
/// is exactly what the affinity contract forbids, so the factory refuses with the contract's own code
/// and null components rather than fabricating half a pair.
/// </remarks>
internal sealed class UnboundCommandTaskFactory : ICommandTaskFactory
{
    /// <inheritdoc/>
    public long Create(out CommandTaskComponents? components)
    {
        components = null;

        return RetCode.E_NO_IMPLEMENTATION;
    }
}

/// <summary>
/// The reachable entry-point type for the in-process service tests.
/// </summary>
/// <remarks>
/// LOAD BEARING, NOT CEREMONIAL. Top-level statements compile to an implicitly internal
/// <c>Program</c> class, so without this declaration <c>WebApplicationFactory&lt;Program&gt;</c> in the
/// sibling <c>PowerFramework.Persistence.Tests</c> project cannot name the entry point, the
/// service-level tests cannot boot this host at all, and every line of wiring in this file becomes an
/// uncovered island dragging the per-service coverage gate down with it (constraint C-H).
/// </remarks>
public partial class Program
{
    /// <summary>
    /// Prevents the partial class from being constructed directly.
    /// </summary>
    /// <remarks>
    /// The host is started by the top-level statements above, never by instantiating this type. A
    /// protected constructor states that without suppressing anything.
    /// </remarks>
    protected Program()
    {
    }
}
