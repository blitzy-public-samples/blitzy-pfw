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
//  ONE ENDPOINT CARRYING BOTH PROTOCOL VERSIONS, ON THE ASSIGNED PORT (constraint C-K)
//  appsettings.json declares ONE named Kestrel endpoint - `Rest https://+:5101` with `Http1AndHttp2` -
//  and it carries the two REST routes AND the four published gRPC contracts. AAP 0.3.2.2 assigns this
//  service 5101 as "gRPC, plus REST /health and /v1/ping", so one port is the topology the plan fixes;
//  a second listener for the gRPC half published C-05..C-08 at an address the plan does not assign.
//  Being TLS is what makes one address sufficient: ALPN negotiates `h2` or `http/1.1` per connection,
//  measured on this toolchain rather than assumed - an HTTP/1.1 /health probe and a gRPC unary call both
//  succeed against one such endpoint, with no "HTTP/2 is not enabled" warning.
//
//  ⚠ WHY THE SCHEME IS LOAD BEARING, VERIFIED BY RUNNING THE SERVICE RATHER THAN ASSUMED. Kestrel does
//  NOT enable prior-knowledge HTTP/2 on a PLAINTEXT `Http1AndHttp2` endpoint: it emits "HTTP/2 is not
//  enabled ... TLS is not enabled. HTTP/2 requires TLS application protocol negotiation. Connections to
//  this endpoint will use HTTP/1.1" and serves HTTP/1.1 only, which would silently take all four gRPC
//  contracts off the air while leaving the REST routes working - the worst possible failure shape,
//  because the readiness probe would still answer 200. That is why the endpoint declares `https`, and
//  why anyone tempted to "simplify" that URL to `http` must split the endpoint again and declare the
//  gRPC half HTTP/2-only - which is exactly the topology divergence the single endpoint corrects. The scheme is a deployment decision and it belongs to configuration;
//  the certificate arrives the same way, through the Kestrel certificate settings the orchestration
//  layer supplies, which is why no certificate is named in this file.
//
//  THE PORT IS NEVER RESTATED IN CODE (constraint C-F). There is no UseUrls call, no literal port and
//  no literal address below. The container definition EXPOSEs the port and the orchestration manifest
//  MAPs it, and configuration is the single seam between the three - so a port change is a
//  configuration change, never a recompile. Note also that the neighbouring port 5103 is a
//  DELIBERATELY RESERVED, COMMENTED placeholder for a deferred service and not a spare: binding it
//  here would break the documented port map and violate constraint C-D in spirit.
//
//  NO HTTPS IS CONFIGURED IN CODE EITHER, WHICH IS NOT THE SAME AS NO HTTPS. The configured endpoint
//  declares `https`, and the certificate reaches Kestrel through the certificate settings the
//  orchestration layer supplies - but there is no UseHttps call, no development certificate, no HSTS and
//  no HTTPS redirection in this file. Mutual TLS remains a documented per-pair fallback that this
//  service is not part of: it presents no client certificate, because it requests no token. The
//  listener's scheme and material are configuration's business.
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
//    * No destructive storage call. There is no EnsureDeleted, no EnsureCreated and no DROP anywhere
//      below, and the ONLY file this service ever deletes is the zero-byte writability probe the startup
//      gate itself just created, by a generated name that cannot name a database. Data/SchemaProvisioner
//      DOES apply this build's PENDING migrations before the pipeline is built when
//      Schema:ApplyMigrationsOnStartup is on - additive statements only, skipped entirely when the history
//      table already records them - which is what makes the one-command bring-up converge on a fresh
//      volume without ever mutating an existing one. The switch defaults to OFF so a characterization
//      capture's untouched volume never depends on remembering to disable anything; the orchestration
//      manifest opts in. See the storage section and SchemaProvisioner for why that distinction is load
//      bearing rather than merely tidy.
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
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml.Linq;
using Grpc.AspNetCore.Server;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using PowerFramework.Persistence.Buffers;
using PowerFramework.Persistence.Concurrency;
using PowerFramework.Persistence.Authorization;
using PowerFramework.Persistence.Configuration;
using PowerFramework.Persistence.Data;
using PowerFramework.Persistence.Endpoints;
using PowerFramework.Persistence.Errors;
using PowerFramework.Persistence.Grpc;
using PowerFramework.Persistence.Runtime;
using PowerFramework.Persistence.Sql.Paging;
using PowerFramework.Persistence.Tasks;
using PowerFramework.Persistence.Tasks.TaskProxies;
// ALIASED RATHER THAN IMPORTED, BECAUSE PowerFramework.Contracts.Persistence.V1 ALSO DECLARES
// QueryService, UpdateService, CommandService AND TransactionService - the four generated bases. Importing
// that namespace here would make every one of this file's four MapGrpcService calls ambiguous, so the one
// contract type this file needs is named on its own.
using CarrierState = PowerFramework.Contracts.Persistence.V1.CarrierState;
using PowerFramework.Persistence.Transactions;
using PowerFramework.Shared.Diagnostics;
using Predicates = PowerFramework.Shared.Kernel.Predicates;
using RetCode = PowerFramework.Shared.Kernel.RetCode;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// --------------------------------------------------------------------------------------------------
//  CORRELATION: THE W3C TRACE CONTEXT IS STAMPED ONTO EVERY LOG RECORD THIS SERVICE WRITES.
//
//  ASP.NET Core already starts an Activity per request and already continues an inbound `traceparent`,
//  so the identifier a caller upstream of this service holds is present here on every request - it was
//  simply never written anywhere an operator can read. An operator holding a Gateway `traceId` from a
//  502 therefore could not join it to the record on this side that explains the fault, which is the
//  whole point of having a correlation identifier at all.
//
//  ActivityTrackingOptions is shared-framework code and adds NO package: the deliberately-excluded list
//  in Directory.Packages.props rules out Serilog and the OpenTelemetry family, and this is the built-in
//  mechanism that remains. TraceId and SpanId identify the operation and the step within it; ParentId
//  is what makes a record attributable to the CALLER's span rather than only to the trace. Baggage and
//  Tags are deliberately NOT tracked: both are caller-controlled key-value sets, so tracking them would
//  copy attacker-influenced content into log records - the opposite of the redaction posture
//  docs/ARCHITECTURE.md 9.9 records.
//
//  This changes no response and no behaviour. It changes what a record CONTAINS, which is why it is
//  paired with the `traceId` member the problem-details customization below adds to every problem body:
//  one identifier, published on the wire and written in the log, so the two can be joined.
// --------------------------------------------------------------------------------------------------
builder.Logging.Configure(static options => options.ActivityTrackingOptions =
    ActivityTrackingOptions.TraceId | ActivityTrackingOptions.SpanId | ActivityTrackingOptions.ParentId);

// --------------------------------------------------------------------------------------------------
//  REGISTRATION. One call per concern, each implemented as an extension method at the bottom of this
//  file so the entry point reads as an inventory rather than as a wall of container calls. The order
//  of these six lines is presentational only - the container resolves lazily - but it is the order a
//  reader wants: what the service is configured with, what clock it reads, who it trusts, where it
//  stores, what it computes, and what it publishes.
// --------------------------------------------------------------------------------------------------
// THE INGRESS BOUNDS COME FIRST, AND THAT PLACEMENT IS NOT PRESENTATIONAL LIKE THE SIX BELOW IT. Its
// transport half configures the LISTENER, which must be bounded before the host is built rather than
// after the first unbounded request has already been accepted. Decomposition creates this system's
// first-ever listening socket [Agent Action Plan 0.1.4], so there is no legacy limit to port and the
// bound answers a failure mode the transition itself introduced - the same standing outbound resilience
// has (0.5.3), not a behaviour improvement layered on top (constraint C-B). No package is added: the
// limiter is shared-framework code. Configuration/IngressOptions.cs states every value and argues each.
builder.AddIngressHardening();

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
//  SCHEMA PROVISIONING - CONFIGURATION-GATED, ADDITIVE ONLY, AND OFF UNLESS A DEPLOYMENT ASKS
//
//  Immediately after the gate and strictly before the pipeline, which is the only position that makes
//  the documented one-command bring-up work: the gate has already proven the data directory present
//  and writable, and no request has been served yet, so a fresh `persistence-db` volume acquires its
//  schema before the readiness probe is ever asked about it. `Schema:ApplyMigrationsOnStartup` defaults
//  to FALSE, so this line is a single logged no-op for every deployment and every test that has not
//  opted in; the orchestration manifest opts in explicitly. It calls `Database.Migrate` and nothing
//  else - additive and idempotent, so it can neither destroy nor reseed the volume a paired
//  characterization capture depends on - and it serializes concurrent replicas through an exclusive
//  lock file on that same volume. A failure throws, which terminates the process with a non-zero exit
//  code exactly as a gate failure does: a service whose schema could not be applied can serve nothing,
//  and starting anyway would answer every retrieval and every update with a storage error.
//
//  Blocking here rather than deferring to a hosted service is the point of the position. A hosted
//  service registered in user code starts AFTER the web host's own, so the listener would already be
//  accepting requests while the schema was still being applied - which is a window in which the
//  readiness gate can open onto a service that is not yet able to answer.
// --------------------------------------------------------------------------------------------------
app.Services
    .GetRequiredService<SchemaProvisioner>()
    .ProvisionAsync(app.Lifetime.ApplicationStopping)
    .GetAwaiter()
    .GetResult();

// --------------------------------------------------------------------------------------------------
//  THE PIPELINE. Authentication STRICTLY BEFORE authorization, and both STRICTLY BEFORE the endpoint
//  mappings. This ordering is not stylistic: with the two reversed or placed after the mappings the
//  fallback policy never sees an authenticated principal and unauthenticated requests pass straight
//  through, silently, on every protected route at once. Phase 9's runtime checks exist to prove this
//  ordering holds rather than to assume it.
//
//  THE TWO DIAGNOSTICS MIDDLEWARES COME FIRST, and they are here for error-contract consistency rather
//  than as hardening. Each works by observing what the middlewares beneath it produced, and the response
//  they most need to observe is the bearer challenge the authentication middleware writes: without them the
//  framework answers a bare status with NO BODY on every path that never reaches this service's own code,
//  while the readiness endpoint answers a problem body with a return code - two different error shapes from
//  one service, chosen by which layer happened to fail. They write through the problem-details service
//  registered in the container, which is why the members its customization fills reach these responses too.
//
//  NEITHER TOUCHES THE gRPC EDGE. A gRPC response always carries its own content type, and status-code pages
//  write only where a response has neither a body nor a content type; a gRPC handler's own faults are mapped
//  to a status by PersistenceStatusInterceptor and converted to trailers by the hosting layer long before
//  they could reach an exception handler. Nothing else is added: no CORS, no rate limiting, no compression.
// --------------------------------------------------------------------------------------------------
// THE PROTECTIVE RESPONSE HEADERS, INSTALLED FIRST SO THEY REACH EVERY RESPONSE. It is registered ahead of
// the exception handler and of authentication deliberately: it works by registering a response-starting
// callback rather than by writing headers itself, so being outermost is what lets it cover a problem
// document the exception handler writes and a bodiless challenge the authentication middleware writes, as
// well as a handler's own response. It overrides nothing a route set for itself - see the file's own banner
// for the three directives and the reason for each.
SecurityResponseHeaders.Use(app);

app.UseExceptionHandler();
app.UseStatusCodePages();

app.UseAuthentication();

// THE REQUEST-LAYER INGRESS BOUND FOR THE REST HALF OF THIS PORT, positioned after authentication because
// its per-caller partition IS the authenticated principal - and inside a container network every request
// from one peer shares one source address, so an address partition would put a whole upstream service in
// one bucket. gRPC requests are exempt here and bounded at the server interceptor instead, where a refusal
// travels as RESOURCE_EXHAUSTED rather than as a 429 carrying no gRPC status trailer. /health is exempt in
// both places: it is the readiness gate DataServices and Gateway are held behind, so rate-limiting it would
// turn a busy service into a permanently unready one and take the stack down with it.
app.UseIngressHardening();

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
    /// ALL FOUR VALIDATIONS ARE HARDCODED ON, and none is readable from configuration. A safe-by-default
    /// read would still let a deployment set one to <c>false</c> and be accepted, which is the one
    /// outcome C-G forbids: the service would then accept a token from any issuer, for any audience,
    /// expired, or unsigned, while starting cleanly and reporting healthy. Clock skew is pinned to a
    /// bounded constant for the same reason - the library's five-minute default can exceed a
    /// short-lived token's whole lifetime. There is no environment-conditional bypass anywhere in this
    /// method and no key that could introduce one.
    /// </para>
    /// <para>
    /// <c>RequireHttpsMetadata</c> IS the one setting that stays configurable, and it is not in the same
    /// category. It does not decide whether a token is checked; it decides whether the handler will FETCH
    /// from a plaintext authority - so it describes the AUTHORITY rather than the verification, and it
    /// must agree with the authority beside it or the handler discovers the contradiction only on its
    /// first fetch. The shipped authority is <c>https</c> and the shipped value is therefore
    /// <c>true</c>; a deployment that moves the authority moves both together, which is why both are
    /// read from the same snapshot inside the configure callback below. Lowering the pair to cleartext
    /// would put the verification key set on a channel an on-path attacker can rewrite, which is
    /// CWE-319 on the one fetch in the estate that must not be rewritable.
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

        // Registered here rather than beside the SQL layer because its ONE consumer is the bearer
        // handler's backchannel configured below. A singleton so the anchor file is read once for the
        // life of the process, which is also what lets the eager resolve in the startup gate turn an
        // unreadable anchor into a refusal to start.
        services.TryAddSingleton(static serviceProvider => new InternalTlsTrust(
            serviceProvider.GetRequiredService<IOptions<PersistenceOptions>>().Value.InternalTls));

        // =================================================================================================
        //  DATA PROTECTION IS EPHEMERAL BY DELIBERATE CHOICE, AND THE CHOICE IS ABOUT KEY MATERIAL AT REST.
        //
        //  AddAuthentication REGISTERS THE DATA-PROTECTION STACK WHETHER OR NOT ANYTHING PROTECTS A PAYLOAD -
        //  Microsoft.AspNetCore.Authentication calls AddDataProtection for the ticket formats its remote
        //  handlers use, and this service registers no remote handler. DataProtection's own eager initialiser
        //  then materialises a key ring during host start. That was MEASURED on all four services rather than
        //  inferred: each wrote a key file into its user profile at startup, and one that afterwards failed to
        //  bind its port had ALREADY written it. Left at the default the ring is an unencrypted private key
        //  under the process's user profile - observed at '/root/.aspnet/DataProtection-Keys' - created per
        //  container and shared with nothing, which the framework itself warns about for a container.
        //
        //  NOTHING IN THIS SERVICE PROTECTS A PAYLOAD. Inbound authentication is bearer-token validation
        //  against Security's published verification material, which is stateless and uses no protector; there
        //  is no cookie, no session, no antiforgery token and no protected payload that outlives a request. The
        //  default therefore writes key material to disk for NO CONSUMER - a secret at rest with no purpose,
        //  and a secret at rest with no purpose is the one shape the secrets mandate has no tolerance for.
        //
        //  EPHEMERAL IS THE HONEST POSTURE, AND ITS FAILURE MODE IS WHY. Keys live in this process and die with
        //  it, nothing reaches the filesystem, and a future capability that DOES need a durable protector
        //  fails immediately and visibly on the first restart - instead of working on one replica and failing
        //  on the next, which is the strictly worse of the two failures the default offers. Persisting the ring
        //  instead would not remove the hazard: at-rest encryption of a persisted ring needs an X.509
        //  certificate this deployment does not provision, DPAPI is Windows-only, and the target is Linux
        //  containers - so persisting would relocate unencrypted key material rather than protect it.
        //  docs/SECRETS.md section 5 records the posture and what a later phase must put in its place.
        //
        //  THE PROVIDER SWAP ALONE WAS NOT ENOUGH, AND THAT WAS MEASURED. Replacing IDataProtectionProvider with
        //  the ephemeral one leaves the KEY-MANAGEMENT stack untouched, and data protection's eager initialiser
        //  warms THAT rather than whichever provider is registered - so a host wired that way still wrote a key
        //  file to the user profile on every start. The repository is therefore what is redirected: with an
        //  in-memory IXmlRepository there is no file-system repository to construct, so the ring is created in
        //  this process and NOTHING reaches the disk. One mechanism, at the layer that decides where bytes go.
        //
        //  THIS IS NOT A BEHAVIOUR CHANGE UNDER C-B. There is no legacy analogue to preserve or to break: the
        //  key ring is an artifact of the ASP.NET Core hosting choice this refactor introduced, and the legacy
        //  framework - a library with no process of its own - has nothing that corresponds to it.
        // =================================================================================================
        services
            .AddDataProtection();

        // The key ring lives in memory, so the eager initialiser's key is created HERE rather than in a file.
        services.Configure<KeyManagementOptions>(static options =>
            options.XmlRepository = new InMemoryDataProtectionKeyRepository());

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(bearer =>
            {
                // EVERY VALUE IS READ HERE, INSIDE THE CONFIGURE CALLBACK, AND THAT PLACEMENT IS LOAD
                // BEARING RATHER THAN STYLISTIC. An earlier form of this method read the authority and
                // the metadata address EAGERLY, at registration time, while reading
                // RequireHttpsMetadata lazily from inside this callback - so the two came from
                // DIFFERENT configuration snapshots. That is harmless only while both snapshots agree.
                // It stops being harmless the moment a later configuration source changes one of them:
                // an in-memory source added after registration - which is exactly what
                // WebApplicationFactory does, and what a deployment layering an environment provider
                // over the settings files does - would move RequireHttpsMetadata without moving the
                // authority it qualifies. The handler then post-configures an `http` authority against
                // a `true` requirement and throws "The MetadataAddress or Authority must use HTTPS",
                // on the first authenticated request rather than at startup, with a message that
                // implicates neither of the two settings that actually disagreed. Reading both from one
                // snapshot removes the failure mode rather than documenting it.
                IConfigurationSection jwt =
                    configuration.GetSection(PersistenceStartupGate.JwtSectionName);

                bearer.Authority = (jwt[nameof(JwtOptions.Authority)] ?? string.Empty).Trim();
                bearer.Audience = (jwt[nameof(JwtOptions.Audience)] ?? string.Empty).Trim();
                bearer.RequireHttpsMetadata = jwt.GetValue(nameof(JwtOptions.RequireHttpsMetadata), true);

                // An explicit metadata address overrides the authority-relative default, which is what
                // lets a deployment point the handler at a key set published somewhere other than the
                // conventional path beneath the authority.
                string metadataAddress =
                    (jwt[PersistenceStartupGate.MetadataAddressKey] ?? string.Empty).Trim();

                if (metadataAddress.Length > 0)
                {
                    bearer.MetadataAddress = metadataAddress;
                }

                // 🔴 BOTH KEY-SET REFRESH INTERVALS ARE READ, BECAUSE LEAVING EITHER UNREAD IS A ROTATION
                // DECISION TAKEN BY OMISSION - and the two library defaults fail in OPPOSITE directions at
                // once. RefreshInterval defaults to five minutes, so a token minted after Security rotates
                // its signing key is refused 401 (IDX10503, no key matched the identifier) for up to that
                // long even though the handler asks to refresh the instant it sees an unknown identifier.
                // AutomaticRefreshInterval defaults to TWELVE HOURS and is the only thing that ever drops a
                // RETIRED key, because a successful validation provokes no refresh - so the superseded
                // credential stayed acceptable here for half a day. Rotation therefore inverted this
                // boundary's verdicts: the old token worked and the new one did not.
                //
                // READ FROM THE SAME SNAPSHOT AS THE AUTHORITY THEY QUALIFY, for the reason recorded at the
                // top of this callback, and MODELLED on JwtOptions so PersistenceOptionsValidator can refuse
                // a value below the library's own floor at startup rather than letting the configuration
                // manager throw on the first authenticated request. The fallbacks below are the same
                // constants the options type defaults to, so an absent key and an unbound one agree.
                bearer.RefreshInterval = jwt.GetValue(
                    nameof(JwtOptions.MetadataRefreshInterval),
                    JwtOptions.DefaultMetadataRefreshInterval);

                bearer.AutomaticRefreshInterval = jwt.GetValue(
                    nameof(JwtOptions.MetadataAutomaticRefreshInterval),
                    JwtOptions.DefaultMetadataAutomaticRefreshInterval);

                // ALL FOUR ARE ASSIGNED LITERALLY, NOT READ. Each removes an entire class of forgery, so
                // none is a deployment choice: without issuer validation a credential from any issuer is
                // accepted; without audience validation a credential minted for another service is
                // replayable here; without lifetime validation Security's short lifetimes bound nothing;
                // without signing-key validation the signature is not verified at all. Reading them with
                // a safe default still left a configuration path that could turn one OFF while this host
                // reported healthy - an unauthenticated boundary wearing the shape of an authenticated
                // one, which constraint C-G forbids. The four are MODELLED on JwtOptions so a
                // deployment can still be audited by reading its settings file, and
                // PersistenceOptionsValidator refuses a configured false rather than ignoring it in
                // silence.
                bearer.TokenValidationParameters.ValidateIssuer = true;
                bearer.TokenValidationParameters.ValidateAudience = true;
                bearer.TokenValidationParameters.ValidateLifetime = true;
                bearer.TokenValidationParameters.ValidateIssuerSigningKey = true;

                // CLOCK SKEW IS BOUNDED AND NOT CONFIGURABLE, AND SAYING NOTHING WAS NOT THE SAME AS
                // ALLOWING NOTHING.
                //
                // Left unassigned this property is the library's default of FIVE MINUTES, so every token
                // reaching this service stayed usable for five minutes past its own `exp` and the
                // `ValidateLifetime = true` immediately above enforced a bound five times looser than it
                // appears to. Security issues with a five-minute lifetime, which made the tolerance as
                // long as the lifetime it qualifies.
                //
                // THE FOUR BOUNDARIES NOW AGREE, WHICH IS THE POINT. Before this assignment each of the
                // four validators of one issuer's tokens used a different tolerance - zero at Security,
                // thirty seconds at DataServices, and five minutes at Gateway and here, both by
                // omission - so whether an expired credential was accepted depended only on which
                // service it reached. This service is the innermost one, the only holder of a storage
                // provider, and it was among the two most permissive.
                //
                // THIRTY SECONDS, MATCHING DataServices AND Gateway VERBATIM: enough to absorb ordinary
                // clock drift between containers on one host, which is the topology the frozen
                // environment describes, and no more. Security mints with a truncated whole-second
                // timestamp, so no sub-second allowance is needed. It is a CONSTANT rather than a
                // JwtOptions member for exactly the reason the four checks above are literals - a
                // deployment able to widen the tolerance past the token lifetime has turned expiry
                // checking off without turning any switch off. Security's own `TimeSpan.Zero` is
                // deliberately NOT copied: it validates only tokens it minted itself, moments earlier,
                // from the same clock, so it has no second clock to accommodate. docs/ARCHITECTURE.md
                // records all four values in one place.
                bearer.TokenValidationParameters.ClockSkew = TimeSpan.FromSeconds(30);
            });

        // THE KEY-SET BACKCHANNEL'S TRUST DECISION, APPLIED IN A SECOND CONFIGURATION PASS BECAUSE THE
        // FIRST ONE HAS NO SERVICE PROVIDER TO RESOLVE FROM. This is the only outbound channel this
        // service has, and it is the one that decides which keys sign a valid token: Security terminates
        // TLS with a certificate issued by the local authority docs/ARCHITECTURE.md 9.3.1 generates,
        // which is in no container's OS trust store, so without the anchor the handler cannot fetch the
        // key set at all and every inbound token is refused for want of a key rather than on its merits.
        // Supplied only when an anchor is configured, so an unset anchor leaves the handler's own
        // default backchannel untouched. Trust is NARROWED, never relaxed - there is no validation
        // callback anywhere in this service (constraint C-G).
        services
            .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<InternalTlsTrust>(static (bearer, trust) =>
            {
                if (!trust.IsPinned)
                {
                    return;
                }

                SocketsHttpHandler backchannel = new();

                trust.Apply(backchannel);

                bearer.BackchannelHttpHandler = backchannel;
            });

        // 🔴 THE THIRD DURATION, WHICH THE TWO ABOVE DO NOT BOUND: how long a SUPERSEDED key set stays
        // acceptable as a last-known-good fallback. MEASURED at a sibling boundary: with both intervals
        // configured, a token signed by a RETIRED key was still accepted nine and a half minutes after the
        // rotation - well past the background refresh that had already replaced the current configuration -
        // because BaseConfigurationManager keeps a CACHE of recently-good configurations that the token
        // handler retries against, and its entries live for LastKnownGoodLifetime, which defaults to ONE
        // HOUR. Two separately retired identities were both still honoured, so it is a cache of several
        // rather than one previous configuration.
        //
        // BOUNDED RATHER THAN TURNED OFF. UseLastKnownGoodConfiguration stays at its default of true
        // because it is what keeps this boundary validating tokens through a transient inability to FETCH
        // the key set, which is an availability property worth keeping; what is not worth keeping is a
        // retired credential honoured for an hour, which is the window rotation exists to close. The
        // lifetime is DERIVED from the background interval rather than made a third knob, so the two cannot
        // drift into an incoherent pair, and the fallback constants match the options type's own.
        //
        // IN A POST-CONFIGURE, because the handler's own post-configure step is what constructs the
        // configuration manager from the authority - it does not exist yet while the delegate above runs.
        // Reaching the manager the framework built keeps metadata retrieval entirely framework code:
        // nothing in this repository fetches a key set by hand.
        _ = services
            .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .PostConfigure(bearer =>
            {
                if (bearer.ConfigurationManager is not BaseConfigurationManager manager)
                {
                    return;
                }

                IConfigurationSection jwt =
                    configuration.GetSection(PersistenceStartupGate.JwtSectionName);

                manager.LastKnownGoodLifetime = jwt.GetValue(
                    nameof(JwtOptions.MetadataAutomaticRefreshInterval),
                    JwtOptions.DefaultMetadataAutomaticRefreshInterval);
            });

        // DENY BY DEFAULT, WITH ONE NAMED EXCEPTION. A fallback policy means no route is ever
        // authenticated by omission: a new endpoint arriving without an explicit declaration is a
        // CLOSED door rather than an open one, and the single anonymous route - the readiness probe -
        // opts out in the one place a reader looks for it. The inverse posture, allowing by default
        // and remembering to protect each endpoint, is not auditable, because proving it correct means
        // proving a negative across every route that exists and every route anyone adds later.
        // AND AUTHENTICATION IS NOT AUTHORIZATION, WHICH IS WHAT THE TWO NAMED POLICIES BELOW ADD.
        // Requiring only an authenticated user meant any holder of any token this issuer minted for this
        // audience could call every one of the four contracts - so a credential obtained for reading could
        // update, delete or run an arbitrary command, and a credential minted for a caller that has no
        // business here at all could do the same (CWE-862, CWE-863). The contracts already publish the
        // distinction: DataServices requests `persistence.read` for the reading half and
        // `persistence.write` for the writing half, and the AAP fixes the call graph as layered and
        // acyclic - nothing but DataServices calls Persistence.
        //
        // BOTH HALVES ARE ENFORCED: the operation's scope, and the caller's identity. Either alone leaves
        // a hole. Scope without subject lets any caller the issuer serves in, as long as it holds the
        // scope; subject without scope lets the one permitted caller do anything once it is in.
        //
        // A REFUSAL HERE IS gRPC PermissionDenied, NOT Unauthenticated, and the projections downstream
        // already publish that distinction as HTTP 403 versus 401 - "the token is valid but does not carry
        // the scope this operation requires". Nothing about the wire contract changes; this is the code
        // that makes the published 403 reachable.
        services.AddAuthorization(static options => options.FallbackPolicy =
            new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build());

        // THE TWO NAMED POLICIES ARE REGISTERED IN ONE PLACE, AND THIS IS NOT IT. Every token reaching
        // this service carries a `scope` claim, and its only caller requests two distinct scopes with a
        // documented split - read for C-05 and the C-08 reads, write for C-06, C-07 and the C-08 state
        // changes. Until these policies existed nothing read that claim, so a credential obtained to
        // RETRIEVE rows could equally update them, execute arbitrary SQL through C-07 and commit or roll
        // back a transaction. The split its caller declares was a comment, not a boundary.
        //
        // THE POLICY NAME IS THE SCOPE NAME, and there is exactly one registrar for both policies. A
        // second registrar using a second naming convention - `persistence:read` beside
        // `persistence.read` - is the shape that fails silently: `AddPolicy` REPLACES a policy of the
        // same name but does nothing at all to a policy of a DIFFERENT name, so the route table names
        // one convention and the other family enforces nothing anyone reaches, while both read as live.
        // Composing the requirements instead means each contract names one string and gets all three:
        // authenticated, the operation's own scope, and a caller identity on this deployment's roster.
        //
        // The mechanism, the reason the framework's own claim requirement cannot express it (the claim
        // is ONE value carrying a SPACE-DELIMITED set, which is exactly what this service's caller
        // sends), the roster half, and the reason all of it is duplicated per service rather than shared
        // are recorded in Authorization/ScopeAuthorization.cs.
        services.AddScopeAuthorization();

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
    /// <c>SqliteConnectionFactory.ConnectionString</c> rather than a string built in this file,
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
    /// MIGRATIONS ARE APPLIED AT STARTUP ONLY WHEN A DEPLOYMENT ASKS FOR IT, AND THE SWITCH IS OFF BY
    /// DEFAULT (constraint C-K). NEVER APPLYING THEM AT STARTUP IS THE OTHER DEFENSIBLE POSITION, and
    /// three of the four arguments for it hold, which is exactly why the DEFAULT is off rather than on: the
    /// paired-capture rule above means a parity run must be able to rely on the volume being untouched;
    /// this service is required to be independently SCALABLE (constraint C-J), so replicas racing to
    /// apply one migration is a real hazard; and a missing schema SHOULD be visible through the
    /// readiness probe rather than silently repaired by whichever replica started first. The fourth
    /// argument does not hold - that applying migrations is purely an operator action with a first-class
    /// tool. It is, but the runtime image carries neither the SDK nor <c>dotnet-ef</c>, and the
    /// documented bring-up is a SINGLE command (constraints C-J and C-L), so on a fresh volume that
    /// command produced a stack in which three of four services never became healthy and nothing in the
    /// manifest could fix it. An out-of-band step nobody following the documentation would know to run
    /// is not a provisioning strategy.
    /// </para>
    /// <para>
    /// SO THE SEAM THIS PARAGRAPH PRESCRIBES EXISTS, ON EXACTLY THOSE TERMS:
    /// ADDITIVE ONLY, never a drop-and-recreate, and gated by configuration so a parity run can switch
    /// it off. <see cref="SchemaProvisioner"/> holds it, <c>Schema:ApplyMigrationsOnStartup</c> enables
    /// it and defaults to <see langword="false"/>, and the orchestration manifest turns it on in one
    /// visible place so the documented command reaches a healthy stack. It calls
    /// <c>Database.Migrate</c> and nothing else, serializes replicas through an exclusive lock file on
    /// the mounted volume, and terminates the process on failure rather than starting a service that
    /// could answer nothing. The three sentences above this one still apply verbatim: no
    /// <c>EnsureDeleted</c>, no <c>EnsureCreated</c> and no <c>DROP</c> anywhere in this service, and
    /// the only deletion it performs at all remains the startup gate's own writability probe file.
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

        // THE PROVISIONER IS REGISTERED UNCONDITIONALLY AND DECIDES FOR ITSELF, which is deliberate: a
        // registration conditional on configuration would make "the switch is off" and "the type was
        // never registered" the same observable state, and only one of those is something an operator
        // can act on. It reads the switch first and performs no file operation at all when it is off.
        services.TryAddSingleton<SchemaProvisioner>();

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
        // ------------------------------------------------------------------------------------------
        //  TWO SOURCES A DATA-OBJECT NAME RESOLVES THROUGH, AND BOTH ARE REAL
        //  The legacy assigns a name and the PowerBuilder runtime loads the compiled DataWindow out of
        //  the target's library list [n_cst_thread_task_sqlbase.sru:L558]. There is no managed
        //  equivalent - the .srd objects live in the read-only legacy tree - so resolution became an
        //  injected collaborator, and this service carries two of them because they answer different
        //  deployments:
        //    * ConfiguredDataObjectCatalog is the DEPLOYMENT's own catalogue, bound from the
        //      `DataObjects` configuration section and FROZEN at construction so a definition cannot
        //      change under a retrieval already in flight. Its section is validated by this service's
        //      own options validator, which refuses an incomplete entry and two names differing only by
        //      case - so without this registration that whole section would be bound, validated and then
        //      read by nothing, and a deployment's definitions would resolve to nothing at all.
        //    * DataObjectDefinitionCatalogue is the in-process registry: it is seeded with the one
        //      EVIDENCED fixture [ws_objects/pfw.tests.pbl.src/dw_sqlite.srd] and it is where a
        //      definition DERIVED at run time from a caller-supplied grid syntax is registered, under a
        //      `pfw_derived_` name a caller cannot have configured.
        //  The retrieval runtime consults the configured catalogue FIRST and the registry second, which
        //  is the only order that lets a deployment override the built-in default while leaving the two
        //  populations unable to collide.
        // ------------------------------------------------------------------------------------------
        services.TryAddSingleton<IDataObjectCatalog, ConfiguredDataObjectCatalog>();
        services.TryAddSingleton<DataObjectDefinitionCatalogue>();
        services.TryAddSingleton<DataWindowStoreBindings>();
        services.TryAddSingleton<IDataObjectRuntime, SqliteDataObjectRuntime>();
        services.TryAddSingleton<ISqlDataStoreFactory>(static provider => new SqlDataStoreFactory(
            provider.GetRequiredService<IDataObjectRuntime>(),
            provider.GetRequiredService<TimeProvider>()));
        services.TryAddSingleton<ISqlRetrievalHookActivator>(static _ => new SqlRetrievalHookActivator());

        // ------------------------------------------------------------------------------------------
        //  THE RUNTIME SEAMS. Each is bound to a real SQLite-backed implementation under Runtime/. See
        //  the extended commentary at the bottom of this file for why a refusing seam is the tempting
        //  reading here and which of its premises do not hold. Every one is TryAdd-registered, so
        //  every one is substitutable by a characterization harness or a test host.
        // ------------------------------------------------------------------------------------------
        services.TryAddSingleton<IQueryTransactionSurface, SqliteQueryTransactionSurface>();
        services.TryAddSingleton<IQueryDataWindowRuntime, SqliteQueryDataWindowRuntime>();

        // ⚠ TRANSIENT, AND ANY OTHER LIFETIME IS A USE-AFTER-DISPOSE WAITING TO HAPPEN. The activator
        // below resolves an engine PER POOLED TRANSACTION through a delegate, and the pool DISPOSES every
        // engine it creates when it collects that transaction. A singleton would therefore hand the same
        // connection to every pooled transaction and then close it when the first one was collected -
        // a fault that only appears under concurrent load, which is the worst kind to ship. The engine is
        // also not thread safe, by design and by the legacy's own affinity contract, and one instance per
        // checked-out transaction is exactly what that contract asks for.
        services.TryAddTransient<ITransactionEngine, SqliteTransactionEngine>();

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

        // ------------------------------------------------------------------------------------------
        //  THE POOL'S IDLE COLLECTION, WHICH NEEDS SOMETHING TO FIRE IT. The legacy subscribes the pool
        //  to the framework's own idle notification inside its keep-alive branch
        //  [n_cst_thread_trans_pool.sru:L80]. A service has no such notification, so without this
        //  registration the collection entry point is reachable only from a test and a RETAINED
        //  transaction is held for the life of the process - a resource leak rather than a slow service.
        //  See Transactions/TransactionPoolIdleSweeper for the non-overlap and shutdown properties.
        //
        //  Registered by its concrete type as well, so a test can resolve it and drive one sweep against
        //  a controlled clock instead of waiting for a tick.
        // ------------------------------------------------------------------------------------------
        services.TryAddSingleton(static provider => new TransactionPoolIdleSweeper(
            provider.GetRequiredService<TransactionPool>(),
            provider.GetRequiredService<IOptions<PersistenceOptions>>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<ILogger<TransactionPoolIdleSweeper>>()));
        services.AddHostedService(static provider =>
            provider.GetRequiredService<TransactionPoolIdleSweeper>());

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

        // WHO A HANDLE IS ATTRIBUTED TO, which is what makes the per-caller ceiling per-CALLER. The
        // accessor is the stock ASP.NET Core one - registration ships in the shared framework, so no
        // package reference is added - and it is the only way a component below the endpoint layer can
        // read the principal of the request it is serving. Without it every handle would be attributed to
        // one bucket and only the total ceiling would be operative.
        services.AddHttpContextAccessor();
        services.TryAddSingleton<HandlePrincipalResolver>();

        // The server-side handle tables. See the remarks above for why every one of them is a
        // singleton and what breaks if one is not. Each carries a CEILING and an IDLE WINDOW: a
        // handle is server-held state that an in-process caller never had, so an abandoned one would
        // otherwise pin a connection - and, for a command or update task, a worker task - for the life of
        // the process. See Runtime/HandleLifecycle.cs.
        services.TryAddSingleton<TransactionSessionRegistry>();
        services.TryAddSingleton<QueryTaskRegistry>();
        services.TryAddSingleton<UpdateTaskRegistry>();
        services.TryAddSingleton<CommandTaskRegistry>();

        // THE RECLAIM PASS AND THE SHUTDOWN DRAIN. A hosted service rather than a timer field on a
        // registry, so its schedule is the host's lifetime rather than an object's, and so the drain runs
        // in the ordered stop sequence the host already provides - tasks before sessions, which the
        // reclaimer owns because a task borrows the transaction its session owns.
        services.AddHostedService<HandleReclaimer>();

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

        // The caller-side query proxy's two collaborators. Registered so that the query proxy pair is
        // CONSTRUCTIBLE from the container - both of these were interfaces with no implementation
        // anywhere, which made the pair unbuildable however correct each half was. The child resolver
        // answers a dropdown child lookup from the headless definition catalogue; the adopter wraps a
        // transferred carrier as a datastore so the caller side can go on describing and filtering it.
        services.TryAddSingleton<IQueryChildResolver>(static provider =>
            new QueryChildResolver(provider.GetRequiredService<IQueryDataWindowRuntime>()));
        services.TryAddSingleton<IQueryCarrierAdopter>(static provider =>
            new QueryCarrierAdopter(provider.GetRequiredService<IDataObjectRuntime>()));

        services.TryAddSingleton<IQueryTaskFactory, QueryTaskFactory>();
        services.TryAddSingleton<ISqlUpdateCarrierAdapter, SqlUpdateCarrierAdapter>();
        services.TryAddSingleton<IUpdateTaskFactory, UpdateTaskFactory>();
        services.TryAddSingleton<ICommandTaskFactory, CommandTaskFactory>();

        // gRPC IS THIS SERVICE'S PRIMARY TRANSPORT. The interceptor is added HERE, once, at the server
        // level, which is exactly what makes the status mapping central rather than duplicated across
        // four implementations - see PersistenceStatusInterceptor for the mapping itself.
        // The process-wide gRPC ingress bound. A SINGLETON, because the gRPC hosting layer activates an
        // interceptor registered by type once per CALL - so an interceptor owning its own limiter would
        // build a fresh empty one per call and bound nothing. See Grpc/GrpcIngressLimit.cs.
        services.TryAddSingleton<GrpcIngressLimiter>();

        services.AddGrpc(static options =>
        {
            // OUTERMOST, AHEAD OF THE STATUS INTERCEPTOR, because a bound exists to shed work before it is
            // done: an inner registration would admit the call, let the handler run, and bound nothing that
            // mattered. Its refusal is already an RpcException carrying RESOURCE_EXHAUSTED, so it needs no
            // mapping from the status interceptor beneath it.
            options.Interceptors.Add<GrpcIngressLimitInterceptor>();

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

        // THE TWO MESSAGE CEILINGS, APPLIED THROUGH A DEPENDENT CONFIGURE RATHER THAN INSIDE THE CALL
        // ABOVE, because AddGrpc's configure delegate takes no service provider and these two values come
        // from a bound options group. A Configure registered after AddGrpc runs after AddGrpc's own
        // delegate, so these are the last writes to the two properties and cannot be overwritten by it.
        //
        // The RECEIVE ceiling restates the framework's own 4 MiB default so the value is visible beside the
        // other bounds instead of being inherited invisibly. The SEND ceiling is the one that was genuinely
        // unbounded: a result carrier assembled from a caller-influenced request is exactly the payload
        // that needs one, because without it this process serialises the whole of it into memory before the
        // transport can ever push back. Both come from the one Ingress section, so this transport and the
        // REST half of the same port cannot be bounded differently by accident.
        _ = services
            .AddOptions<GrpcServiceOptions>()
            .Configure<IOptions<IngressOptions>>(static (grpc, ingress) =>
            {
                grpc.MaxReceiveMessageSize = ingress.Value.MaxReceiveMessageBytes;
                grpc.MaxSendMessageSize = ingress.Value.MaxSendMessageBytes;
            });

        // REGISTERED THROUGH AN EXPLICIT FACTORY RATHER THAN BY TYPE, so that the process-termination
        // seam its constructor exposes keeps its host-backed default here and is supplied deliberately
        // only by a test. Registered by type, the seam would be filled by whatever the container happened
        // to be able to resolve for it, and a future registration of an Action<int> for some unrelated
        // purpose would silently take over the termination path.
        services.TryAddSingleton(static serviceProvider => new PersistenceStatusInterceptor(
            serviceProvider.GetRequiredService<ISqlRedactor>(),
            serviceProvider.GetRequiredService<IHostApplicationLifetime>(),
            serviceProvider.GetRequiredService<ILogger<PersistenceStatusInterceptor>>()));

        services.AddPersistenceHealthChecks();

        // THE CUSTOMIZATION IS WHAT MAKES A FRAMEWORK-GENERATED BODY A CONTRACT-SHAPED ONE. The problem
        // responses this service writes itself - the readiness endpoint's not-ready body - carry `retCode`
        // because the code that writes them sets it. The bodies the FRAMEWORK writes carry nothing at all:
        // the bearer challenge, the authorization refusal, an unmatched route and a rejected method are
        // produced beneath any of this service's own code. Registering the problem-details service without
        // asking for those bodies left this service answering two different error shapes depending on which
        // layer produced the failure, which is the inconsistency the estate's other three services close the
        // same way.
        //
        // The guard is not an optimization: a response this service composed itself already carries the
        // precise code for its own condition, so overwriting it would replace a specific value with one
        // derived from a status.
        services.AddProblemDetails(static options =>
            options.CustomizeProblemDetails = static context =>
            {
                // THE CORRELATION IDENTIFIER IS ADDED TO EVERY PROBLEM BODY, INCLUDING ONE AN ENDPOINT COMPOSED
                // ITSELF - which is why it sits ABOVE the retCode guard rather than below it. The guard returns
                // early for a body that already carries retCode, so anything written after it would be skipped
                // for exactly the bodies this service authored by hand.
                //
                // The value is resolved the same way in every service that publishes it: the current Activity's
                // id when there is one - which there is on every request, because the host starts an Activity and
                // continues an inbound W3C `traceparent` - and the host's own request identifier otherwise. The
                // presence guard keeps a hand-written path that already set the member authoritative, and keeps an
                // empty member out of the body: a `traceId` with no value advertises a bridge with no far side.
                //
                // The published problem schema sets `additionalProperties: true` and states that a consumer must
                // ignore members it does not recognise, so this adds a member without widening any contract.
                if (!context.ProblemDetails.Extensions.ContainsKey(ProblemContractMembers.TraceId))
                {
                    string correlationId = Activity.Current?.Id
                        ?? context.HttpContext.TraceIdentifier
                        ?? string.Empty;

                    if (!string.IsNullOrEmpty(correlationId))
                    {
                        context.ProblemDetails.Extensions[ProblemContractMembers.TraceId] = correlationId;
                    }
                }

                if (context.ProblemDetails.Extensions.ContainsKey(ProblemContractMembers.RetCode))
                {
                    return;
                }

                int status = context.ProblemDetails.Status ?? context.HttpContext.Response.StatusCode;

                context.ProblemDetails.Extensions[ProblemContractMembers.RetCode] = ClassifyFailure(status);
            });

        return services;
    }

    /// <summary>
    /// Classifies a framework-generated failure status as a legacy return code, so that a body this service
    /// did not compose still carries the member every other error body it produces carries.
    /// </summary>
    /// <param name="statusCode">The status the framework is answering with.</param>
    /// <returns>
    /// The legacy code for that status, and <see cref="RetCode.UNKNOWN"/> for anything unclassifiable.
    /// </returns>
    /// <remarks>
    /// <para>
    /// EVERY ARM IS WRITTEN OUT RATHER THAN DERIVED FROM A TRUTHINESS TEST, and each is taken from the
    /// vocabulary this service already uses: a malformed request is <c>E_INVALID_ARGUMENT</c>, a refused
    /// caller is <c>E_ACCESS_DENIED</c> whether the refusal was authentication or authorization, a not-ready
    /// probe is <c>E_BUSY</c> - the same code <c>Endpoints/HealthEndpoints.cs</c> writes by hand, so the two
    /// agree - and an unmatched route is <c>E_OBJECT_NOT_FOUND</c>. The legacy algebra declares exactly one
    /// access code and draws no distinction between "no credential" and "credential without permission"; the
    /// HTTP statuses stay distinct, so the code gains no member the oracle never had.
    /// </para>
    /// <para>
    /// THE FINAL CHECK IS THE POINT OF THE GUARD. The algebra is tri-state with a documented hole -
    /// <c>PREVENT</c> is 1 and reads as a SUCCESS through <c>IsSucceeded</c>
    /// [<c>ws_objects/pfw.shared.pbl.src/issucceeded.srf:L11-L13</c>], and <c>CANCELLED</c> is excluded from
    /// <c>IsFailed</c> and so is NEITHER [<c>isfailed.srf:L11-L13</c>] - so an error body must never carry a
    /// code from either class. The kernel predicate is consumed to enforce that rather than the comparison
    /// being re-derived, so a future arm that violated it degrades to <c>UNKNOWN</c> instead of publishing a
    /// failure a consumer's own predicate would read as a success.
    /// </para>
    /// </remarks>
    private static long ClassifyFailure(int statusCode)
    {
        long classified = statusCode switch
        {
            StatusCodes.Status400BadRequest => RetCode.E_INVALID_ARGUMENT,
            StatusCodes.Status401Unauthorized => RetCode.E_ACCESS_DENIED,
            StatusCodes.Status403Forbidden => RetCode.E_ACCESS_DENIED,
            StatusCodes.Status404NotFound => RetCode.E_OBJECT_NOT_FOUND,
            StatusCodes.Status405MethodNotAllowed => RetCode.E_NO_SUPPORT,
            StatusCodes.Status408RequestTimeout => RetCode.E_TIME_OUT,
            StatusCodes.Status415UnsupportedMediaType => RetCode.E_INVALID_TYPE,
            StatusCodes.Status429TooManyRequests => RetCode.E_BUSY,
            StatusCodes.Status503ServiceUnavailable => RetCode.E_BUSY,
            StatusCodes.Status504GatewayTimeout => RetCode.E_TIME_OUT,
            >= StatusCodes.Status500InternalServerError => RetCode.E_INTERNAL_ERROR,
            _ => RetCode.UNKNOWN,
        };

        return Predicates.IsFailed(classified) ? classified : RetCode.UNKNOWN;
    }
}

/// <summary>
/// The extension-member names this service's problem bodies declare.
/// </summary>
/// <remarks>
/// SPELLED ONCE HERE BECAUSE THE COMPOSITION ROOT AND THE READINESS ENDPOINT BOTH WRITE THEM, and the
/// customization above exists precisely to fill the member an endpoint did not. Two independent spellings
/// would let a rename go half-applied, at which point a body would carry both the old member and the new one
/// and a consumer would read whichever it happened to look for.
/// </remarks>
internal static class ProblemContractMembers
{
    /// <summary>The legacy return code carried by every problem body.</summary>
    internal const string RetCode = "retCode";

    /// <summary>The correlation identifier carried by every problem body this service writes.</summary>
    /// <remarks>
    /// <para>
    /// Spelled <c>traceId</c>, which is the spelling every other service in the estate publishes and the
    /// one the authored OpenAPI documents describe. A second spelling anywhere would leave an operator
    /// joining two halves of one request by two different member names.
    /// </para>
    /// <para>
    /// The value is the current <c>Activity</c> identifier - a W3C trace context id, continued from an
    /// inbound <c>traceparent</c> when the caller sent one - falling back to the host's request identifier.
    /// The composition root's logging configuration stamps the same trace and span identifiers onto every
    /// log record, which is what makes a body and a record joinable rather than merely both timestamped.
    /// </para>
    /// </remarks>
    internal const string TraceId = "traceId";
}

/// <summary>
/// The two named authorization policies this service enforces, and the scope parsing behind them.
/// </summary>
/// <remarks>
/// <para>
/// <b>WHY NAMED POLICIES EXIST AT ALL (constraint C-G).</b> Every new boundary the decomposition created
/// must be authenticated, and the four gRPC contracts are all new. But authentication answers "who is
/// this" and says nothing about "may it do this": a fallback policy requiring only an authenticated
/// principal accepts a credential minted for retrieval on the update and command contracts too, because
/// the token's purpose is never read. These two policies read it.
/// </para>
/// <para>
/// <b>READ AND WRITE, AND NOTHING FINER.</b> Two scopes are what the token contract's caller actually
/// requests and what the audience naming convention already fixed, and the split falls where the legacy
/// itself splits: the retrieval task issues only SELECT statements
/// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru</c>], while the update and
/// command tasks generate and execute DML
/// [<c>n_cst_thread_task_sqlupdate.sru</c>, <c>n_cst_thread_task_sqlcommand.sru</c>]. The transaction
/// contract straddles the line and is therefore annotated PER RPC rather than per class - its session,
/// descriptor and state readers need read, and its commit, rollback, auto-commit, clear-state and
/// set-broken operations need write.
/// </para>
/// <para>
/// <b>NO INFERENCE BETWEEN THEM.</b> Write does not imply read and read does not imply write. A caller
/// that needs both asks Security for both, which is a decision visible at the request site rather than a
/// rule hidden in a policy.
/// </para>
/// </remarks>
internal static class PersistenceAuthorizationPolicies
{
    /// <summary>
    /// The policy - and the scope name - a caller must hold to reach the retrieval contract.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE POLICY NAME IS THE SCOPE NAME, DELIBERATELY. A separate policy identifier would be one more
    /// mapping to keep in step across two services: DataServices requests this exact string from Security
    /// [<c>services/dataservices-service/PowerFramework.DataServices/Clients/PersistenceClient.cs</c>],
    /// Security mints it into the token's space-delimited <c>scope</c> claim
    /// [<c>services/security-service/PowerFramework.Security/Tokens/TokenIssuer.cs</c>], and this file
    /// enforces it. One spelling, three places, no translation table.
    /// </para>
    /// <para>
    /// ALIASED TO <see cref="PersistenceScopes.Read"/> RATHER THAN RESTATED, so the literal exists once in
    /// this service. The registrar in <c>Authorization/ScopeAuthorization.cs</c> builds the policy under
    /// that name and every contract on this service names it through this constant; two independent
    /// spellings of the same value is how a route comes to name a policy nothing registered, which the
    /// framework answers as an unexplained internal error rather than as a refusal.
    /// </para>
    /// </remarks>
    internal const string Read = PersistenceScopes.Read;

    /// <summary>
    /// The policy - and the scope name - a caller must hold to reach the update, command or
    /// state-changing transaction operations.
    /// </summary>
    /// <remarks>Aliased to <see cref="PersistenceScopes.Write"/>, for the reason above.</remarks>
    internal const string Write = PersistenceScopes.Write;

    /// <summary>
    /// The claim the granted scope set arrives in, as this system's issuer mints it.
    /// </summary>
    /// <remarks>
    /// ONE claim carrying a SPACE-DELIMITED value, which is the encoding the published token contract
    /// fixes and the issuer implements - not a repeated claim. Reading it therefore means splitting, and
    /// splitting is the whole reason this parsing lives in one tested place rather than being written
    /// inline at each policy.
    /// </remarks>
    private const string ScopeClaimName = "scope";

    /// <summary>
    /// The alternative claim name some issuers use for the same set.
    /// </summary>
    /// <remarks>
    /// NOT MINTED BY THIS SYSTEM'S ISSUER, and accepted anyway. The deployment contract points this
    /// service at an authority through configuration, so the token could in principle come from an
    /// issuer that spells the set this way; refusing it would turn a working deployment into a silent
    /// 403 whose cause is invisible. Accepting both spellings widens nothing, because a scope must still
    /// be present by exact name to satisfy anything.
    /// </remarks>
    private const string AlternativeScopeClaimName = "scp";

    /// <summary>
    /// The delimiters a scope value may be split on.
    /// </summary>
    /// <remarks>
    /// A space is the encoding the contract fixes; the other three are accepted because a value that
    /// arrived with a tab or a line break would otherwise be read as ONE scope whose name happens to
    /// contain white space, which can never match and would fail closed for a reason nobody could see.
    /// </remarks>
    private static readonly char[] ScopeDelimiters = [' ', '\t', '\r', '\n'];

    /// <summary>
    /// Whether a principal holds a scope by exact name.
    /// </summary>
    /// <param name="user">The authenticated principal.</param>
    /// <param name="scope">The required scope name.</param>
    /// <returns>
    /// <see langword="true"/> when a scope claim carries <paramref name="scope"/> as one of its
    /// space-delimited entries; otherwise <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>EXACT, ORDINAL AND CASE-SENSITIVE.</b> Scope names are case-sensitive strings in the OAuth
    /// framework, so a case-insensitive comparison here would silently admit a credential naming
    /// something the issuer never granted. There is no prefix matching, no wildcard, no hierarchy and no
    /// "read implies write" or "write implies read" inference: a caller that needs both holds both.
    /// </para>
    /// <para>
    /// <b>A NULL OR UNAUTHENTICATED PRINCIPAL ANSWERS FALSE</b> rather than throwing. The policies pair
    /// this with <c>RequireAuthenticatedUser</c>, so an anonymous request is already refused by the time
    /// this could be reached; answering false keeps the helper total and makes it safe to call from
    /// anywhere without a null dance at each site.
    /// </para>
    /// <para>
    /// Nothing here is logged. A claim value is caller-supplied material and the granted scope set is
    /// part of a credential, so neither reaches a log record from this file (constraint C-F).
    /// </para>
    /// </remarks>
    internal static bool HasScope(ClaimsPrincipal? user, string scope)
    {
        ArgumentException.ThrowIfNullOrEmpty(scope);

        if (user is null)
        {
            return false;
        }

        foreach (Claim claim in user.Claims)
        {
            if (!string.Equals(claim.Type, ScopeClaimName, StringComparison.Ordinal)
                && !string.Equals(claim.Type, AlternativeScopeClaimName, StringComparison.Ordinal))
            {
                continue;
            }

            // AsSpan + a manual walk rather than Split, because this runs on every authorized request and
            // a split would allocate an array and a string per entry to answer a question about one of
            // them. The behaviour is identical to Split with RemoveEmptyEntries.
            ReadOnlySpan<char> remaining = claim.Value.AsSpan();

            while (!remaining.IsEmpty)
            {
                int delimiter = remaining.IndexOfAny(ScopeDelimiters);

                ReadOnlySpan<char> candidate = delimiter < 0 ? remaining : remaining[..delimiter];

                if (candidate.SequenceEqual(scope))
                {
                    return true;
                }

                remaining = delimiter < 0 ? [] : remaining[(delimiter + 1)..];
            }
        }

        return false;
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
    /// ALL FOUR gRPC SERVICES REQUIRE AUTHORIZATION EXPLICITLY, AND EACH NAMES THE POLICY ITS CONTRACT
    /// CALLS FOR (constraint C-G) - BUT THE DECLARATION IS AN ATTRIBUTE ON THE IMPLEMENTATION, NOT A
    /// SECOND ONE HERE. The fallback policy closes the door on a route that declares nothing; the
    /// attributes decide WHO may open it and for WHAT. The parameterless form at this mapping
    /// site would let any holder of any token minted for this audience call all four contracts -
    /// a credential obtained for reading could update, delete or run an arbitrary command. Retrieval takes
    /// the reading scope; update and command take the writing scope, which is exactly the split
    /// DataServices already requests its credential under; and each policy additionally requires the
    /// caller to be a configured permitted identity, because the AAP fixes the call graph as layered and
    /// acyclic - nothing but DataServices calls Persistence.
    /// </para>
    /// <para>
    /// <b>AND NO POLICY IS NAMED AT THE MAPPING SITE, WHICH IS A REQUIREMENT AND NOT A STYLE CHOICE.</b>
    /// C-08 straddles the read/write line: its session, descriptor and state readers need the reading
    /// scope and its commit, rollback, auto-commit, clear-state and set-broken operations need the writing
    /// one, so it is annotated PER RPC and deliberately carries no class-level attribute. Authorization
    /// metadata COMBINES rather than overriding, so a service-wide policy declared here would AND itself
    /// with every method policy - and a service-wide WRITE policy would then refuse a read-only caller the
    /// ability to ask whether the transaction is connected, while a service-wide READ policy would let
    /// that caller commit. Both are wrong in one direction, which is exactly why the split is per method.
    /// The three single-scope contracts carry their policy as a class-level attribute for the same reason
    /// of single statement: one place per contract declares its requirement, and a route therefore carries
    /// exactly ONE scope policy.
    /// </para>
    /// </remarks>
    internal static WebApplication MapPersistenceEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapHealthEndpoints();
        app.MapPingEndpoints();

        // C-05 retrieval, C-06 update, C-07 command, C-08 transaction. The four contracts this service
        // exists to serve, each deriving from a generated base in the published contracts project. Their
        // authorization requirement travels as an [Authorize(Policy = ...)] attribute on the
        // implementation - class-level on the three single-scope contracts, per method on C-08 - so
        // nothing is declared here and no route ends up carrying two scope policies ANDed together.
        app.MapGrpcService<QueryService>();
        app.MapGrpcService<UpdateService>();
        app.MapGrpcService<CommandService>();
        app.MapGrpcService<TransactionService>();

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
    /// How many times schema provisioning is attempted before a locked database is treated as fatal.
    /// </summary>
    /// <remarks>
    /// Bounded rather than open-ended, because an unbounded retry turns a genuinely stuck volume into a
    /// container that never starts and never says why. Four attempts across the delay below cover the
    /// only contention this service can create for itself - two instances starting against one volume at
    /// once - and the migration itself is a handful of statements against an empty database.
    /// </remarks>
    private const int ProvisioningAttempts = 4;

    /// <summary>How long to wait between provisioning attempts when another writer holds the database.</summary>
    private static readonly TimeSpan ProvisioningRetryDelay = TimeSpan.FromMilliseconds(750);

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
        ValidateRuntimeGraph(services, logger);

        // THE SCHEMA IS PROVISIONED AFTER THIS GATE AND BEFORE THE PIPELINE, BY Data/SchemaProvisioner,
        // WHICH IS THE ONLY POSITION THAT MAKES THE ONE-COMMAND BRING-UP CONVERGE. It has to run after
        // ValidateDataDirectory, which creates and proves the directory, and after ValidateRuntimeGraph,
        // which proves the connection seam constructible - and before the pipeline is built, because
        // everything after that point can answer /health. It is a SEPARATE type rather than a step of this
        // gate because the replica lock, the bounded wait and the additive-only property are each
        // assertable there and none of them is a composition concern.

        // THE TRUST ANCHOR IS LOADED HERE SO A BAD ONE IS A REFUSAL TO START. A configured-but-unreadable
        // anchor means the bearer handler cannot fetch the key set it validates every inbound token
        // against, so this instance would start healthy and then refuse every authenticated call for a
        // reason no caller could diagnose. An UNSET anchor resolves to platform default trust and is not
        // a fault.
        InternalTlsTrust trust = services.GetRequiredService<InternalTlsTrust>();

        logger.LogInformation(
            "Structural preconditions satisfied: the bound configuration validated, the storage "
            + "directory exists and is writable by this process, every runtime seam this service "
            + "executes SQL through resolved, the schema is {SchemaPosture}, and internal TLS trust is "
            + "{TrustPosture}.",
            options.Schema.ApplyMigrationsOnStartup
                ? "this build's to provision, because startup provisioning is switched on"
                : "the operator's to provision, because startup provisioning is switched off",
            trust.IsPinned ? "pinned to the configured anchor" : "the platform default");
    }
    /// <summary>
    /// Reports whether a SQLite fault is write contention rather than a structural fault.
    /// </summary>
    /// <param name="error">The provider's exception.</param>
    /// <returns><see langword="true"/> for a busy or locked database.</returns>
    /// <remarks>
    /// The two SQLite result codes that mean "another writer holds this database": 5 is
    /// <c>SQLITE_BUSY</c> and 6 is <c>SQLITE_LOCKED</c>. Compared numerically rather than by message,
    /// because a message is localised and is not a contract. Every other code is a structural fault and
    /// is deliberately NOT retried - retrying a malformed schema or an unwritable file would only delay
    /// the same failure while making it look intermittent.
    /// </remarks>
    private static bool IsContention(SqliteException error) =>
        error.SqliteErrorCode is 5 or 6;

    /// <summary>
    /// Resolves every seam the SQL path executes through, so an incomplete graph terminates the process
    /// at startup rather than failing whichever request first reaches the missing registration.
    /// </summary>
    /// <param name="services">The composed provider.</param>
    /// <param name="logger">The logger a fatal fault is recorded through.</param>
    /// <remarks>
    /// <para>
    /// WHY THIS GATE EXISTS AT ALL. This service is the only one that generates or executes SQL and the
    /// only one holding a storage provider, so a seam that failed to register is not a degraded feature -
    /// it is a service that can answer nothing. Six of these seams are the ones a deliberate-refusal
    /// reading of the deferral would leave unbound, and what makes that unacceptable is precisely that the
    /// shortfall is invisible until a caller's first request: the process reports healthy, accepts traffic,
    /// and then answers every retrieval and every update with a not-implemented code. Resolving them here
    /// means the same shortfall is a startup failure with a named cause.
    /// </para>
    /// <para>
    /// THE ENGINE IS RESOLVED AND IMMEDIATELY DISPOSED, AND THAT IS DELIBERATE. It is transient because
    /// the transaction pool owns and disposes one engine per pooled transaction, so this gate must not
    /// leak the instance it proves is constructible. Nothing is connected: proving a CONNECTION here
    /// would create a database file as a side effect of a health gate, and the storage probe above has
    /// already proven the directory writable without leaving anything behind.
    /// </para>
    /// <para>
    /// A FAILURE TERMINATES RATHER THAN WARNS, matching the oracle's own posture - a decoded assertion
    /// failure runs <c>HALT CLOSE</c> [<c>ws_objects/pfw.pbl.src/pfw.sra:L111-L144</c>] - and matching
    /// the AAP's instruction that the .NET equivalent is fail-fast startup validation and process
    /// termination on structural faults, never graceful degradation.
    /// </para>
    /// </remarks>
    private static void ValidateRuntimeGraph(IServiceProvider services, ILogger logger)
    {
        try
        {
            // The storage seam and the SQL-execution seams. Each is resolved rather than merely checked
            // for registration, because a registration whose constructor throws is the same outage as no
            // registration at all.
            _ = services.GetRequiredService<SqliteConnectionFactory>();

            using (services.GetRequiredService<ITransactionEngine>())
            {
                // Constructed and released. See the remarks: proving construction is the whole point,
                // and connecting would create a file this gate has no business creating.
            }

            _ = services.GetRequiredService<TransactionPool>();
            _ = services.GetRequiredService<IDataObjectRuntime>();
            _ = services.GetRequiredService<IQueryDataWindowRuntime>();
            _ = services.GetRequiredService<IQueryTransactionSurface>();
            _ = services.GetRequiredService<ISqlUpdateCarrierAdapter>();

            // The three task factories - the caller-side/worker-side pair builders. A missing one here
            // would take out exactly one of the four published gRPC services, which is the shortfall
            // most likely to reach production unnoticed.
            _ = services.GetRequiredService<IQueryTaskFactory>();
            _ = services.GetRequiredService<IUpdateTaskFactory>();
            _ = services.GetRequiredService<ICommandTaskFactory>();
        }
        catch (Exception error) when (error is InvalidOperationException
            or ArgumentException
            or ObjectDisposedException)
        {
            // The exception's own message names the service type that could not be produced, so no type
            // name is interpolated here: doing so would duplicate it and risk the two disagreeing.
            Terminate(
                logger,
                "A runtime seam this service executes every statement through could not be produced "
                + "from the composed container, so this process could serve no retrieval, no update and "
                + "no command. This is a composition fault rather than a transient one, so the process "
                + "is terminating instead of starting and refusing every request. The attached exception "
                + "names the seam.",
                error);
        }
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

        // ------------------------------------------------------------------------------------------
        //  🔴 THE READ-ONLY LEGACY TREE IS REFUSED BEFORE ANY FILESYSTEM MUTATION, AND THE ORDER IS
        //  THE WHOLE POINT OF THIS BLOCK.
        //
        //  SqliteConnectionFactory refuses this path in its constructor too, so relying on that alone
        //  is the tempting position. It is not sufficient: that constructor runs in ValidateRuntimeGraph
        //  BELOW - after the writability probe here has already called Directory.CreateDirectory. Without
        //  the refusal AT THIS POINT the service would CREATE a directory inside ws_objects/** and only
        //  then refuse to start, writing into the behavioural oracle that constraint C-C states is never
        //  an edit target: the very next characterization capture would read whatever landed there as
        //  legacy source. Refusing startup is not enough on its own; the mutation that would precede it
        //  is the defect this ordering prevents.
        //
        //  The path is resolved to an absolute one first, exactly as the factory resolves it, because
        //  the segment test is defined on an absolute path - a relative setting would otherwise slip
        //  past a check the factory later applies to its resolved form. The predicate and the refusal
        //  text are BOTH consumed from the factory rather than restated, so the rule has one home.
        // ------------------------------------------------------------------------------------------
        if (directory.Length != 0
            && SqliteConnectionFactory.ResolvesInsideReadOnlyLegacyTree(ResolveOrEmpty(directory)))
        {
            Terminate(logger, SqliteConnectionFactory.ReadOnlyLegacyTreeRefusalText);

            return;
        }

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
            // THE DESCRIPTION IS BUILT BY DataDirectoryFault AND THE CAUSE IS NOT ATTACHED, both
            // deliberately, and both are corrections.
            //
            // An earlier form interpolated the configured path into the message on the reasoning that an
            // operator needs it and that a directory path is not a credential, and attached the file
            // system's own exception so a log pipeline kept its type and errno. Measured on a running
            // host, the two together put the PATH into the startup output FOUR times and the
            // CONFIGURATION KEY zero times: the operator was handed the value they already knew and not
            // the setting they had to change, and the terminal record simultaneously asserted that
            // "Configured values are deliberately not quoted" - which was false in the one record that
            // said it.
            //
            // The cause cannot simply be attached with a redacted message either: IOException and
            // UnauthorizedAccessException from the file APIs quote the path in their OWN Message, so
            // attaching one republishes what the sentence withholds. It is therefore described BY TYPE
            // ALONE inside the description - the same rule Gateway's system-error handler applies to a
            // fault it cannot safely render - and no exception is passed to Terminate.
            //
            // The single "not writable" sentence is gone too: DataDirectoryFault establishes WHICH of the
            // four ways this can fail actually happened, because "check the permissions" is the wrong
            // instruction for a path occupied by a file or a volume that was never mounted.
            Terminate(logger, DataDirectoryFault.Describe(directory, error));

            return;
        }
        finally
        {
            TryDeleteProbe(probe);
        }
    }

    /// <summary>
    /// Resolves a configured directory to an absolute path, answering the empty string when the value
    /// is not a path this host can resolve at all.
    /// </summary>
    /// <param name="directory">The configured value, already trimmed.</param>
    /// <returns>The absolute form, or the empty string when it cannot be resolved.</returns>
    /// <remarks>
    /// A VALUE THIS HOST CANNOT RESOLVE IS NOT REPORTED HERE. The legacy-tree test above needs an
    /// absolute path and nothing else, and the empty answer simply declines that test - the writability
    /// probe immediately below is the arm that reports a malformed path, with the operator-facing
    /// message and the underlying error attached. Reporting it twice, in two shapes, would give one
    /// misconfiguration two different diagnostics depending on which check happened to run first.
    /// </remarks>
    private static string ResolveOrEmpty(string directory)
    {
        try
        {
            return Path.GetFullPath(directory);
        }
        catch (Exception error) when (error is ArgumentException
            or NotSupportedException
            or PathTooLongException
            or IOException
            or System.Security.SecurityException)
        {
            return string.Empty;
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
    /// <para>
    /// LOGGED AND THEN THROWN, IN THAT ORDER, because the log record is the artifact an operator reads
    /// and it must exist even if the terminating exception is reported differently by whatever hosts
    /// the process.
    /// </para>
    /// <para>
    /// <b>NO REJECTED VALUE IS EVER QUOTED - WITHOUT EXCEPTION, AND THAT IS A CONTRACT ON THE CALLER.</b>
    /// The emitted record appends the sentence "Configured values are deliberately not quoted", and this
    /// method cannot inspect <paramref name="reason"/> to make that true - so every caller must supply a
    /// value-free reason, or the record asserts something false about itself. An earlier form of these
    /// remarks carved out "except a directory path, which is not a credential", and the one caller that
    /// relied on the carve-out produced exactly that contradiction: a record quoting a configured path
    /// and then declaring that configured values are not quoted. The carve-out is gone; the storage
    /// directory's description is built by <c>Data.DataDirectoryFault</c>, which names the
    /// configuration key instead. A message echoing a token authority, a connection string, a mounted
    /// path or a password would put in the log exactly what constraint C-F exists to keep out of it.
    /// </para>
    /// <para>
    /// <paramref name="cause"/> IS FOR A CAUSE WHOSE OWN MESSAGE IS SAFE TO PUBLISH. The runtime-graph
    /// gate passes one because the exception names the service TYPE that could not be produced, which is
    /// exactly what its reader needs. A file-system exception is the opposite case - its message quotes
    /// the path it failed on - so that caller passes none and names the type inside its reason instead.
    /// </para>
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

    /// <summary>The separator between links of a described fault chain, outermost towards innermost.</summary>
    /// <remarks>
    /// FORWARDED RATHER THAN DECLARED, so that this service and the shared primitive cannot disagree about
    /// the shape of a record. The walk itself moved to
    /// <see cref="PowerFramework.Shared.Diagnostics.ExceptionChain"/> when the same defect was found at
    /// fifteen more sites in this service; the name is kept here because it is what the composition tests
    /// assert against and because the interceptor is the record an operator reads first.
    /// </remarks>
    internal const string FaultChainSeparator = ExceptionChain.Separator;

    /// <summary>The marker appended when a fault chain is deeper than the bound below.</summary>
    internal const string FaultChainTruncationMarker = ExceptionChain.TruncationMarker;

    /// <summary>
    /// How many links of a fault chain are described before truncation.
    /// </summary>
    /// <remarks>
    /// BOUNDED BECAUSE A CHAIN CAN BE CYCLIC. Nothing prevents an exception from being its own ancestor
    /// through aggregation, and an unbounded walk over one would build a string until the process ran out of
    /// memory - while handling a fault, which is the worst possible moment for a second one.
    /// </remarks>
    internal const int MaximumDescribedFaultDepth = ExceptionChain.MaximumDepth;

    /// <summary>
    /// The process exit code reported when a structural fault terminates this service.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A STOP WITHOUT AN EXIT CODE IS A CLEAN STOP, AND THAT IS THE DEFECT THIS CONSTANT CLOSES.
    /// Requesting host shutdown alone leaves <see cref="Environment.ExitCode"/> at zero, so a process
    /// that ended because one of its own invariants was proven broken is indistinguishable, to every
    /// mechanism that reads an exit status, from one that was asked to stop: an orchestrator's restart
    /// policy scoped to failures does not fire, a supervisor records a successful run, and the fault is
    /// visible only to whoever reads the log. The legacy has no equivalent ambiguity - its halt follows a
    /// reported failure and is unconditional [<c>ws_objects/pfw.pbl.src/pfw.sra:L143</c>] - so a clean
    /// exit on this path is a behaviour the refactor introduced rather than one it preserved.
    /// </para>
    /// <para>
    /// Any non-zero value satisfies the requirement. This particular value is the conventional "internal
    /// software error" code from the historical exit-code convention, so a structural fault is
    /// distinguishable from a generic non-zero exit rather than merely non-zero. It deliberately matches
    /// the value the gateway's own structural-fault path reports, so one number means one thing across
    /// the estate; the constant is nonetheless declared per service, because no behaviour crosses a
    /// service boundary in this system and a shared constant would be exactly that.
    /// </para>
    /// </remarks>
    internal const int StructuralFaultExitCode = 70;

    /// <summary>Redacts statement text before it reaches a log record or a status detail.</summary>
    private readonly ISqlRedactor _redactor;

    /// <summary>Ends the process when an assertion proves an invariant broken.</summary>
    private readonly IHostApplicationLifetime _lifetime;

    /// <summary>Records the fault.</summary>
    private readonly ILogger<PersistenceStatusInterceptor> _logger;

    /// <summary>The injectable termination effect, taking the process exit code.</summary>
    private readonly Action<int> _requestProcessTermination;

    /// <summary>Initializes the interceptor.</summary>
    /// <param name="redactor">The statement redactor.</param>
    /// <param name="lifetime">The host lifetime used to terminate on a structural fault.</param>
    /// <param name="logger">The logger faults are recorded through.</param>
    /// <param name="requestProcessTermination">
    /// The termination effect, taking the process exit code. Optional, and defaulting to the host-backed
    /// behaviour of <see cref="RequestHostShutdown"/> - set the exit code, then request shutdown so the
    /// registered shutdown path actually runs. A test supplies its own callback here to assert that
    /// termination was requested with the documented code, without stopping the test host.
    /// </param>
    public PersistenceStatusInterceptor(
        ISqlRedactor redactor,
        IHostApplicationLifetime lifetime,
        ILogger<PersistenceStatusInterceptor> logger,
        Action<int>? requestProcessTermination = null)
    {
        _redactor = redactor ?? throw new ArgumentNullException(nameof(redactor));
        _lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _requestProcessTermination = requestProcessTermination ?? RequestHostShutdown;
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
        // Shutdown is REQUESTED rather than the process being aborted precisely because it lets in-flight
        // calls and the readiness probe observe the shutdown instead of being severed mid-write, and
        // because the registered shutdown path is where this service's finalize step runs - the legacy
        // halt likewise runs the application close event before terminating [pfw.sra:L108], and
        // docs/README.md section 初始化 warns that call must stay paired with the initialize call. The
        // caller is told immediately rather than left waiting for a socket to close.
        //
        // THE REPORT IS BEST EFFORT AND THE TERMINATION IS NOT, WHICH IS WHY THEY ARE SEPARATED BY A
        // finally. Until they were, termination ran only if the log write returned: a logging provider
        // that throws - a full disk, a saturated sink, a misconfigured formatter - propagated out of this
        // method and took the termination request with it, leaving the process serving requests in a
        // state its own invariants say is impossible. That is strictly worse than a lost log record, and
        // the legacy has no equivalent opportunity to skip its halt, so the ordering is fixed:
        // report, then terminate, and terminate whatever the report did.
        if (error is AssertionFailure assertion)
        {
            try
            {
                _logger.LogCritical(
                    assertion,
                    "A framework assertion failed while serving {Method}. The invariant it guards is "
                    + "broken, so this instance is shutting down rather than continuing in a state it "
                    + "believes impossible.",
                    context.Method);
            }
            finally
            {
                _requestProcessTermination(StructuralFaultExitCode);
            }

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

        // EVERYTHING ELSE, AND THE EXCEPTION OBJECT IS DELIBERATELY NOT PASSED TO THE LOGGER.
        //
        // THE DEFECT THIS CLOSES IS SUBTLE AND COMPLETE. Redacting the message is correct, and it is what
        // makes such a record LOOK safe - but attach the exception itself as the logging
        // abstraction's exception argument and every provider renders it by calling ToString().
        // That renders the UNREDACTED message, every inner exception's unredacted message and the stack, so
        // the redaction applied to one placeholder is undone by the argument beside it. An exception raised
        // anywhere near statement generation carries the complete generated statement with its literal values
        // interpolated - exactly the field the legacy logged verbatim and the one this service exists to stop
        // logging.
        //
        // THE WHOLE CHAIN IS REDACTED, NOT ONLY THE OUTERMOST MESSAGE. A provider fault is habitually wrapped,
        // so the statement text is usually on an INNER exception; redacting only the outer message would
        // leave the common case fully exposed.
        //
        // WHAT IS LOST AND WHY THAT IS THE RIGHT TRADE. The managed stack trace no longer reaches this record.
        // It is upstream content in the sense that matters here - its frames carry parameter values in no
        // sanitized form - and the type chain plus the method name locates the fault precisely enough to
        // find it in code. The structural arm above still passes its exception, because that record is
        // written at most once per process, terminates the host, and reproduces the legacy dialog that C-B
        // requires be preserved in full.
        _logger.LogError(
            "{Method} failed with an unhandled fault. FaultTypes={FaultTypes} RedactedMessage={RedactedMessage}",
            context.Method,
            DescribeExceptionTypes(error),
            DescribeRedactedMessages(error));

        return new RpcException(new Status(StatusCode.Internal, UnhandledFaultDetail));
    }

    /// <summary>
    /// Names the types in an exception chain, outermost first, without reading any message.
    /// </summary>
    /// <param name="error">The fault to describe.</param>
    /// <returns>The namespace-qualified type names joined outermost-first.</returns>
    /// <remarks>
    /// A TYPE NAME IS ALLOWLISTED CONTENT AND A MESSAGE IS NOT. Every name this produces comes from this
    /// codebase, the framework or a package - none of them can carry a value a caller sent or a statement this
    /// service generated - so the chain identifies the fault without disclosing anything. The depth is bounded
    /// because a chain can be cyclic through aggregation, and a truncation marker is appended so a shortened
    /// chain is never mistaken for a complete one.
    /// </remarks>
    /// <remarks>
    /// DELEGATED RATHER THAN IMPLEMENTED, and the delegation is the fix rather than tidying. This walk was
    /// written twice - here and in DataServices - with the same three constants, while FIFTEEN other sites in
    /// THIS service still attached the exception object itself and undid the redaction beside it. Three
    /// independent copies of one security control drift; one primitive with one test suite does not.
    /// </remarks>
    private static string DescribeExceptionTypes(Exception error) =>
        ExceptionChain.DescribeTypes(error);

    /// <summary>
    /// Redacts the message of every exception in a chain and joins them outermost first.
    /// </summary>
    /// <param name="error">The fault whose chain is described.</param>
    /// <returns>The redacted messages, joined outermost-first.</returns>
    /// <remarks>
    /// THE CHAIN IS WALKED BECAUSE THE STATEMENT IS USUALLY NOT ON THE OUTERMOST EXCEPTION. A provider fault
    /// arrives wrapped - a task fault wrapping a command fault wrapping the provider's own - and the
    /// interpolated statement sits at the bottom. Every message goes through the same redactor the wire path
    /// uses, so one rule governs both and a change to it cannot apply to one and not the other.
    /// </remarks>
    /// <remarks>
    /// THE INJECTED REDACTOR IS PASSED THROUGH, NOT THE STATIC ONE. The shared primitive refuses to read a
    /// message without a policy, and the policy handed to it here is this interceptor's own injected
    /// <c>ISqlRedactor</c> - so the wire path and the log path stay literally the same object and a change to
    /// one cannot miss the other.
    /// </remarks>
    private string DescribeRedactedMessages(Exception error) =>
        ExceptionChain.DescribeMessages(error, _redactor.Redact);

    /// <summary>
    /// The default termination effect: report the structural-fault exit code, then request shutdown.
    /// </summary>
    /// <param name="exitCode">The process exit code to report.</param>
    /// <remarks>
    /// <para>
    /// THE ORDER IS LOAD BEARING. The exit code is set FIRST so that it is already in place while the
    /// shutdown path runs and whatever inspects the process afterwards reads a failure; setting it after
    /// requesting shutdown races the host's own return from <c>app.Run()</c> and can lose the value
    /// entirely.
    /// </para>
    /// <para>
    /// A fail-fast abort is deliberately NOT used. It would bypass the registered shutdown path, which is
    /// where the pooled transactions are drained and the handle registries release what they hold, and it
    /// would break the documented initialize/finalize pairing the legacy halt preserves
    /// [<c>ws_objects/pfw.pbl.src/pfw.sra:L108</c>, <c>docs/README.md</c> section 初始化]. The dependency
    /// is on the host-lifetime abstraction rather than on any component owning a finalize step, because
    /// the abstraction is the seam those steps are registered against.
    /// </para>
    /// </remarks>
    private void RequestHostShutdown(int exitCode)
    {
        Environment.ExitCode = exitCode;
        _lifetime.StopApplication();
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

    /// <summary>Masks literal values out of any text before it reaches a log record.</summary>
    /// <remarks>
    /// THE SAME SEAM THE OUTWARD DATABASE-ERROR PROJECTION USES, DELIBERATELY. A framework-error text is
    /// not a generated statement, but it is not free of caller data either: the query task composes two
    /// of them by concatenating a fixed prefix with the externally supplied sort or filter EXPRESSION
    /// [<c>Tasks/SqlQueryTask.cs</c>, the two <c>SetSort</c>/<c>SetFilter</c> rejection arms], and a
    /// filter expression carries literal comparison values. Routing the text through the one redactor
    /// means a single policy governs every log-bound value in this service rather than one rule for
    /// statements and a different, weaker one here.
    /// </remarks>
    private readonly ISqlRedactor _redactor;

    /// <summary>Initializes a host with no fault sink and no caller-side proxy.</summary>
    /// <param name="logger">The logger notifications and errors are recorded through.</param>
    /// <param name="redactor">The masking policy applied to text bound for a log record.</param>
    /// <remarks>
    /// The container-resolvable shape. A host obtained straight from the container serves a task that
    /// reports its faults through its own return codes rather than through a sink.
    /// </remarks>
    public PersistenceSqlTaskHost(ILogger<PersistenceSqlTaskHost> logger, ISqlRedactor redactor)
        : this(logger, redactor, faults: null, parentTasking: null)
    {
    }

    /// <summary>Initializes a host bound to a fault sink and a caller-side proxy.</summary>
    /// <param name="logger">The logger notifications and errors are recorded through.</param>
    /// <param name="redactor">The masking policy applied to text bound for a log record.</param>
    /// <param name="faults">The sink framework errors are forwarded to, or <see langword="null"/>.</param>
    /// <param name="parentTasking">The caller-side proxy, or <see langword="null"/> when unattached.</param>
    internal PersistenceSqlTaskHost(
        ILogger<PersistenceSqlTaskHost> logger,
        ISqlRedactor redactor,
        IQueryFaultSink? faults,
        ISqlTaskProxy? parentTasking)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _redactor = redactor ?? throw new ArgumentNullException(nameof(redactor));
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
    /// <remarks>
    /// <para>
    /// SETTABLE, AND FOR THE SAME REASON <c>Worker</c> IS. The legacy substrate's
    /// <c>#ParentTasking</c> is the caller-side tasking object, and the worker forwards its
    /// <c>ondberror</c> event to it [<c>n_cst_thread_task_sqlbase.sru:L94-L95</c>]. Composing the pair in
    /// C# closes a cycle the constructors cannot: the worker takes the host, and the proxy is built before
    /// the worker so that the worker can publish into it - so the host exists before the proxy does and
    /// the reference has to be installed afterwards.
    /// </para>
    /// <para>
    /// WITHOUT THE INSTALLATION THE DATABASE-ERROR CHANNEL IS SILENTLY DEAD, which is what it was: the
    /// factories passed <see langword="null"/>, the base's validity test then skipped the forward, and the
    /// caller-side latch every wire projection reads stayed cleared. A caller saw <c>E_DB_ERROR</c> with no
    /// payload at all - the code, the driver text, the offending buffer and the offending row all lost -
    /// even though the worker had them in hand. The proxy's own row-translation override
    /// [<c>n_cst_threading_task_sqlupdate.sru:L310-L342</c>] was unreachable for the same reason.
    /// </para>
    /// <para>
    /// REBINDING IS REFUSED rather than silently accepted, exactly as it is for the worker: a host serving
    /// two proxies would latch one task's driver error onto another task's caller.
    /// </para>
    /// </remarks>
    public ISqlTaskProxy? ParentTasking { get; private set; }

    /// <summary>Binds the caller-side proxy this host's worker reports its database errors to.</summary>
    /// <param name="parentTasking">The caller-side proxy composed against this host's worker.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="parentTasking"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// A proxy is already bound. See the remarks on <see cref="ParentTasking"/>.
    /// </exception>
    internal void BindParentTasking(ISqlTaskProxy parentTasking)
    {
        ArgumentNullException.ThrowIfNull(parentTasking);

        if (ParentTasking is not null)
        {
            throw new InvalidOperationException(
                "This task host already reports to a caller-side proxy. A host serves exactly one proxy "
                + "pair, and rebinding it would latch one task's database error onto another task's "
                + "caller - a misattribution no caller could detect, because the payload's shape is "
                + "identical either way.");
        }

        ParentTasking = parentTasking;
    }

    /// <summary>The single worker task this host serves, or <see langword="null"/> before binding.</summary>
    /// <remarks>
    /// NOT A CONSTRUCTOR ARGUMENT, BECAUSE THE DEPENDENCY IS CIRCULAR. The worker takes the host as its
    /// own constructor argument, so the cycle is closed by <see cref="BindTask"/> immediately after the
    /// worker exists. Until then the field is null and every member that needs a worker says so rather
    /// than inventing one.
    /// </remarks>
    private SqlTaskBase? _task;

    /// <summary>Binds the single worker task this host serves.</summary>
    /// <param name="task">The worker task composed against this host.</param>
    /// <remarks>
    /// <para>
    /// Called exactly once, by the factory that composed the proxy pair, immediately after constructing
    /// the worker and before anything can reach it. It is what makes <see cref="GetTask"/> answerable:
    /// the worker cannot be a constructor argument, because the worker takes the host as ITS constructor
    /// argument, so the cycle is closed here instead.
    /// </para>
    /// <para>
    /// Rebinding is refused rather than silently accepted. A host serving two tasks would hand
    /// <see cref="GetTask"/> the wrong one, and the caller that reads it is the commit-signal walk -
    /// where the wrong answer is a commit reported against a task that never committed.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="task"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">A task is already bound.</exception>
    internal void BindTask(SqlTaskBase task)
    {
        ArgumentNullException.ThrowIfNull(task);

        if (_task is not null)
        {
            throw new InvalidOperationException(
                "This SQL task host already owns a task. A host serves exactly one worker task, and "
                + "rebinding would make the commit-signal walk report a commit against the wrong task.");
        }

        _task = task;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// THIS HOST OWNS EXACTLY ONE TASK, AT <see cref="SoleTaskIndex"/>, AND THAT POSITION RESOLVES. The
    /// legacy resolves a sibling task through the thread's own task table; in this service each proxy
    /// pair gets its own host, so the table has one entry. Any other position names nothing and answers
    /// <see cref="RetCode.E_OUT_OF_BOUND"/> - the contract's own code for a position that names nothing
    /// - so a caller learns the truth rather than receiving some other task.
    /// </para>
    /// <para>
    /// <b>Why resolving matters.</b> The one caller is the commit-signal walk in
    /// <c>SqlTaskBase.OnCommitted</c>, which counts DOWN from <see cref="TaskIndex"/> to one and sets the
    /// commit signal of every task it resolves. While this member refused every index, that walk found
    /// nothing, no signal was ever set, and the caller-side <c>IsCommitted()</c> could not become true
    /// however the transaction actually ended - so a committed command reported as uncommitted.
    /// </para>
    /// <para>
    /// A position asked for before <see cref="BindTask"/> has run also answers out-of-bound: an unbound
    /// host genuinely holds no task, and inventing one would be worse than saying so.
    /// </para>
    /// </remarks>
    public long GetTask(int index, out SqlTaskBase? task)
    {
        if (index == SoleTaskIndex && _task is { } owned)
        {
            task = owned;

            return RetCode.OK;
        }

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
    /// <para>
    /// FORWARDED TO THE SINK WHEN THERE IS ONE, WHICH IS THE MARSHALLING BOUNDARY IN ACTION: the
    /// worker-side task raises, this worker-side host forwards, and the caller-side collector reads.
    /// <b>The forwarded text is the raw text, unchanged and unmasked</b> - that is the contract channel,
    /// it is what the legacy's own general-error event carries, and narrowing it would change what a
    /// caller is told about its own request.
    /// </para>
    /// <para>
    /// <b>THE LOG RECORD IS NOT THAT TEXT, AND THE TWO ARE DELIBERATELY DIFFERENT.</b> An earlier
    /// revision logged <c>errInfo</c> verbatim on the grounds that this channel carries only framework
    /// diagnostics. It does not: the retrieval task composes two of these texts by concatenating a fixed
    /// prefix with the externally supplied SORT or FILTER expression when the carrier rejects it
    /// [<c>Tasks/SqlQueryTask.cs</c>, the <c>SetSort</c> and <c>SetFilter</c> rejection arms, which
    /// report the value UNTRIMMED and in full], and a filter expression is caller data that routinely
    /// carries literal comparison values. Logging it verbatim wrote business values into the default log
    /// of the one service whose entire disclosure posture is that no literal reaches a log or a response.
    /// So the record carries the CODE, the text's length as safe metadata, and the text only after the
    /// service's single redaction policy has masked every literal out of it. The length is included
    /// because it is what tells an operator that a value was present at all without disclosing it.
    /// </para>
    /// </remarks>
    public long OnError(long errCode, string errInfo)
    {
        string text = errInfo ?? string.Empty;

        // The contract channel first, and with the text exactly as the task raised it.
        _faults?.OnError(errCode, text);

        _logger.LogError(
            "A SQL task reported framework error {ErrorCode}. Redacted detail ({DetailLength} chars): "
                + "{RedactedErrorText}",
            errCode,
            text.Length,
            _redactor.Redact(text));

        return DataWindowBufferStore.EventContinue;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Notifications are progress and lifecycle signals, so they are recorded at trace level: the
    /// legacy throttles them to roughly one per hundred milliseconds per retrieval
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds_mt.sru:L37</c>], which is
    /// still far too many for any higher level. The text is masked on the same grounds as the error
    /// channel above: a notification payload is free text from the task layer, trace records land in
    /// the same sink as every other record, and a level is not an access control.
    /// </remarks>
    public long OnNotify(long notifyCode, long payload, string text)
    {
        _logger.LogTrace(
            "A SQL task raised notification {NotifyCode} with payload {Payload}: {Text}",
            notifyCode,
            payload,
            SqlRedactor.Instance.Redact(text));

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
            _redactor,
            faults,
            new QueryFaultProxy(faults));

        SqlQueryTask task = new(
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

        // THE HOST MUST OWN THE TASK, not merely serve it. The inherited commit-signal walk counts down
        // from the host's task index and sets the commit signal of every task the host resolves, so a
        // host that answers "no task at that position" makes a committed retrieval indistinguishable
        // from an uncommitted one. Bound here because the cycle cannot be closed in either constructor:
        // the task takes the host, so the host cannot take the task.
        host.BindTask(task);

        return task;
    }
}

// ==================================================================================================
//  THE RUNTIME SEAMS ARE BOUND - AND WHY A REFUSING SEAM IS NOT AN OPTION HERE
//
//  READ THIS ONCE HERE RATHER THAN CHASING SIX REGISTRATIONS.
//
//  Six refusing types - UnboundDataObjectRuntime, UnboundQueryDataWindowRuntime,
//  UnboundQueryTransactionSurface, UnprovisionedTransactionEngine, UnboundUpdateTaskFactory and
//  UnboundCommandTaskFactory - each answering its contract's own defined negative, are what a
//  documented-gap reading of the deferral produces here. That argument is internally consistent and its
//  conclusion is wrong: with all six bound that way, contracts C-05 through C-08 cannot perform a
//  single retrieval, update, command or commit, so the whole of this service's published surface answers a
//  refusal no matter what a caller sends. A gap that spans every capability a service exists to provide is
//  not a gap in it; it is the absence of it.
//
//  The three premises such a refusal rests on, and what each is actually true of:
//
//    1. "A DataWindow runtime would implement the deferred DesignSystem." It would not. AAP 0.2.2.2 scopes
//       DesignSystem to the `pfw.ui*` libraries - visual controls, geometry, DPI, canvas, painter, font and
//       menus. The DataWindow RESULT CARRIER is scoped to Persistence by 0.3.4, 0.4.2.6 and 0.6.4, which
//       state in as many words that the legacy result carrier IS a DataWindow because
//       `n_cst_thread_task_sqlbase_ds` derives from `datastore`, and that a naive rowset would discard the
//       state the update contract depends on. `Buffers/` is that carrier, it is already in this project,
//       and binding a runtime over it implements nothing presentational: there is no window, no font, no
//       colour and no pixel anywhere in `Runtime/`.
//    2. "An update task's carrier needs original values, which only a carrier holds." True, and the carrier
//       holds them - `CarrierRow` captures the original on first write and `GetItemOriginalValue` answers
//       it. That premise was an argument FOR binding the seam, not against it.
//    3. "A command task's proxy needs the calling-thread substrate, which a headless service does not
//       have." This conflated the substrate with the thread. `n_cst_threading_task` is a STATE HOLDER -
//       a running flag, a cancellation handle, a synchronization event, an identity triple and a worker
//       insertion - and none of those needs a second thread. `Runtime/SqliteCommandTaskFactory.cs` holds
//       that state and says which of its members are deliberately not ported and why.
//
//  What is bound, and where the behaviour lives:
//
//      IDataObjectRuntime        ->  Runtime/SqliteDataObjectRuntime.cs
//      IQueryDataWindowRuntime   ->  Runtime/SqliteQueryRuntime.cs
//      IQueryTransactionSurface  ->  Runtime/SqliteQueryRuntime.cs
//      ITransactionEngine        ->  Runtime/SqliteTransactionEngine.cs
//      ISqlUpdateCarrierAdapter  ->  Runtime/SqlUpdateCarrier.cs
//      IUpdateTaskFactory        ->  Runtime/SqliteUpdateTaskFactory.cs
//      ICommandTaskFactory       ->  Runtime/SqliteCommandTaskFactory.cs
//
//  Every one is TryAdd-registered, so every one remains substitutable - which is the property a refusing
//  seam also has and the only one worth keeping from it. And the definitions those runtimes resolve are
//  configuration, because
//  the `.srd` objects live in the read-only legacy tree and no managed runtime can load one; the
//  `DataObjects` section is that material, and `ValidatePersistenceStructuralPreconditions` refuses to
//  start a host whose seams are not all resolvable.
// ==================================================================================================

/// <summary>
/// The trust anchor this service's one outbound channel verifies Security against, loaded once from the
/// path <c>InternalTls:TrustedCaPath</c> names.
/// </summary>
/// <remarks>
/// <para>
/// WHAT THIS FIXES. This service calls nobody, but the stock bearer handler does: it fetches Security's
/// discovery document and published key set over its own backchannel, and that is the channel which
/// decides WHICH KEYS SIGN A VALID TOKEN. Security terminates TLS with a certificate issued by the
/// LOCAL authority the generation recipe in <c>docs/ARCHITECTURE.md</c> §9.3.1 creates, and that
/// authority is in no container's operating-system trust store. Left on platform default trust the
/// handler cannot fetch the key set at all, so every inbound token is refused for want of a key rather
/// than on its merits - which reads to an operator as a token problem when it is a trust problem.
/// </para>
/// <para>
/// IT NARROWS TRUST; IT DOES NOT RELAX IT. The policy built here sets
/// <see cref="X509ChainTrustMode.CustomRootTrust"/>, so the mounted anchor becomes the ONLY acceptable
/// root and the machine's public roots stop being acceptable for internal traffic. Chain building, name
/// validation and validity dates remain the platform's. There is no
/// <c>RemoteCertificateValidationCallback</c>, no <c>ServerCertificateCustomValidationCallback</c> and
/// no environment-conditional bypass anywhere in this service (constraint C-G).
/// </para>
/// <para>
/// REVOCATION IS A SETTING, AND ITS DEFAULT IS A CONSEQUENCE OF THE TOPOLOGY. <c>InternalTls:RevocationMode</c>
/// selects the posture, so a deployment whose authority DOES publish revocation information can ask for a
/// real check - which a hardcoded value denied it, leaving a stolen peer certificate acceptable until it
/// expired. The shipped default is the only value the DOCUMENTED topology can answer, and that was
/// measured rather than assumed: a local authority generated by two <c>openssl</c> invocations publishes
/// no distribution point and runs no responder, so chain building for a leaf it issued succeeds with no
/// check and fails under both stricter modes with an indeterminate revocation status - which those modes
/// treat as a refusal, never as a pass. While the default stands, the substituting control is the recipe's
/// own 30-day lifetime, and the operational surfaces carry the production recommendation and the
/// emergency procedure.
/// </para>
/// </remarks>
internal sealed class InternalTlsTrust
{
    /// <summary>The configuration path of the group this type is built from.</summary>
    /// <remarks>
    /// Composed once so the resolver's failure message and the loader's failure message name the group
    /// the same way, and so neither can drift from the property names it quotes.
    /// </remarks>
    private static readonly string ConfigurationKeyPrefix = "InternalTls";

    private readonly X509Certificate2Collection _anchors;

    /// <summary>
    /// The revocation posture the configured group selected, resolved once at construction.
    /// </summary>
    /// <remarks>
    /// RESOLVED HERE RATHER THAN PER HANDLER, so an unrecognised value fails the host's start instead of
    /// failing the first outbound handshake - the fail-fast posture the rest of this file keeps.
    /// </remarks>
    private readonly X509RevocationMode _revocationMode;

    /// <summary>
    /// Loads the anchor bundle, or records that this deployment uses platform default trust.
    /// </summary>
    /// <param name="options">The bound <c>InternalTls</c> section. A path, never material.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// A path is configured but the bundle cannot be read or does not parse. Structural, and therefore
    /// fatal - the fail-fast posture of <c>ws_objects/pfw.pbl.src/pfw.sra:L111-L144</c>.
    /// </exception>
    /// <remarks>
    /// The path is NOT echoed into the failure message. A trust anchor is public material, but a
    /// container's secret mount layout is not something a startup record should publish, so the message
    /// names the configuration key instead - the same rule
    /// <c>Configuration/PersistenceOptions.cs</c> applies to its own validation messages.
    /// </remarks>
    public InternalTlsTrust(InternalTlsTrustOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // RESOLVED BEFORE THE EARLY RETURN, DELIBERATELY. An unrecognised mode is a misconfiguration
        // whether or not this deployment pins an anchor, and a deployment that later sets a path would
        // otherwise discover the typo only once it had.
        _revocationMode = options.ResolveRevocationMode(ConfigurationKeyPrefix);

        if (!options.IsConfigured)
        {
            _anchors = [];

            return;
        }

        try
        {
            X509Certificate2Collection loaded = [];

            loaded.ImportFromPemFile(options.TrustedCaPath.Trim());

            if (loaded.Count == 0)
            {
                throw new CryptographicException("The file carried no PEM-encoded certificate.");
            }

            _anchors = loaded;
        }
        catch (Exception failure) when (failure
            is CryptographicException
            or IOException
            or UnauthorizedAccessException
            or ArgumentException)
        {
            throw new InvalidOperationException(
                "The internal trust anchor named by "
                    + $"'InternalTls:{nameof(InternalTlsTrustOptions.TrustedCaPath)}' could not be "
                    + "loaded, so this deployment cannot verify the key set it validates every inbound "
                    + "token against and the host will not start. Check that the file exists, that the "
                    + "process can read it, and that it is a PEM-encoded certificate or chain of them. "
                    + "The path is not reproduced here, because a startup record must not publish a "
                    + "container's secret mount layout.",
                failure);
        }
    }

    /// <summary>
    /// Whether internal trust is pinned to a mounted anchor rather than left to the platform.
    /// </summary>
    public bool IsPinned => _anchors.Count > 0;

    /// <summary>
    /// Applies the pinned anchor to one outbound handler, or leaves platform trust in place.
    /// </summary>
    /// <param name="handler">The handler about to be used for internal traffic.</param>
    /// <exception cref="ArgumentNullException"><paramref name="handler"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// A FRESH POLICY PER HANDLER, DELIBERATELY. <see cref="X509ChainPolicy"/> is not documented as
    /// thread-safe and a handler may be used concurrently, so each handler receives its own instance
    /// built over the SAME shared anchor collection - one file read, one policy per consumer.
    /// </remarks>
    public void Apply(SocketsHttpHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        if (!IsPinned)
        {
            return;
        }

        X509ChainPolicy policy = new()
        {
            TrustMode = X509ChainTrustMode.CustomRootTrust,

            // THE CONFIGURED POSTURE, not a constant. See the option's own remarks for why the shipped
            // default cannot be the strict value on the documented topology, and why an indeterminate
            // status under the stricter two is a refusal rather than a pass.
            RevocationMode = _revocationMode,
        };

        // ONLY MEANINGFUL WHEN A CHECK IS ACTUALLY PERFORMED, and it excludes the root because a locally
        // generated authority does not revoke itself - asking about it would turn every check into an
        // indeterminate answer and therefore into a refusal, which is the failure the mode's own remarks
        // describe. It matches the flag Security's issuance-credential check already uses.
        policy.RevocationFlag = X509RevocationFlag.ExcludeRoot;

        policy.CustomTrustStore.AddRange(_anchors);

        handler.SslOptions.CertificateChainPolicy = policy;
    }
}

// ==================================================================================================
//  THE PROVISIONED TASK FACTORIES - THE PROXY PAIR, BUILT AND JOINED
//
//  READ THIS ONCE HERE RATHER THAN TWICE BELOW.
//
//  WHY THESE FACTORIES BIND REAL IMPLEMENTATIONS RATHER THAN REFUSALS
//  Binding six refusing types here is the tempting reading of the deferral rules: a data-object runtime
//  that resolves nothing, a carrier runtime that materialises nothing, a transaction surface that runs
//  nothing, a transaction engine that connects to nothing, and two task factories that answer
//  RetCode.E_NO_IMPLEMENTATION. Such a set is defensible as a documented gap rather than a stub, and the
//  distinction is real - none throws, each answers its own contract's defined negative. The defence
//  still does not hold, for a reason that has nothing to do with how the refusals are spelled:
//  the four capabilities behind them are the ones this service EXISTS to own. Persistence is the only
//  service that generates or executes SQL and the only one holding a storage provider, and its four
//  published contracts are Query, Update, Command and Transaction. A composition root that binds
//  refusals to all four does not ship a documented gap; it ships a service that cannot do its job, and
//  the mandatory acceptance checks for this file - a concurrency mismatch surfacing as Aborted with a
//  populated ConflictDetail, and a database error whose statement text is proven redacted - are not
//  reachable at all without a real graph.
//
//  WHERE EACH CAPABILITY LIVES, so a reader looking for one of them finds it:
//    ITransactionEngine        -> Data/SqliteTransactionEngine.cs        (transient, one per pooled
//                                                                        transaction, and the lifetime
//                                                                        is load bearing)
//    IDataObjectRuntime        -> Data/DataWindowRuntime.cs
//    IQueryDataWindowRuntime   -> Data/DataWindowRuntime.cs
//    IQueryTransactionSurface  -> Data/DataWindowRuntime.cs
//    ISqlUpdateCarrierAdapter  -> Tasks/SqlUpdateCarrier.cs
//    ISqlTaskProxyHost         -> Tasks/TaskProxies/PersistenceSqlTaskProxyHost.cs
//  Each of those files carries, at its head, the constraint it could be misread as breaking and the
//  reason it does not: C-E for the engine, because SQLite is the one storage engine this repository
//  evidences and no SQL Server or Oracle connection is provisioned anywhere; C-D for the carrier
//  runtime, because what DesignSystem owns is the RENDERING half and the DATA half already lives in
//  this project's Buffers/ folder.
//
//  WHAT DID *NOT* CHANGE, AND MUST NOT
//    * The dialect discriminator is still the caller's, and the classification over it still has two
//      arms plus the legacy's not-implemented arm - anything not containing ORACLE is the SQL Server
//      type, and there is no SQLite arm. Both paging rewriters remain pure string transforms held to
//      byte-exact parity with no instance of either engine running.
//    * The proxy pair is still a PAIR. Both factories below build a worker-side task AND a caller-side
//      proxy and join them through a fault sink, because the legacy encodes thread affinity as a
//      contract rather than as commentary. Flattening either into one async method is what the
//      affinity contract forbids, and neither does.
//    * Nothing here deletes, recreates or reseeds the database, and nothing issues a DROP. The
//      paired-capture rule depends on that and is restated at each file that touches storage.
// ==================================================================================================

/// <summary>
/// The provisioned <see cref="IUpdateTaskFactory"/>: it builds an update proxy pair per session.
/// </summary>
/// <remarks>
/// <para>
/// THE SESSION IS RESOLVED FIRST AND A MISSING ONE IS THE ONLY REFUSAL. The contract's own documented
/// negative for an unknown or ended session is <see cref="RetCode.E_INVALID_TRANSACTION"/>, and that arm
/// stays reachable - it is a caller naming a session it does not have, which is a real state rather than
/// an unimplemented capability.
/// </para>
/// <para>
/// THE PAIR IS BUILT IN THE ORDER THE ORACLE BUILDS IT, and the order is load bearing. The worker-side
/// host is constructed around the caller-side fault sink; the worker is constructed around that host;
/// the caller-side substrate is constructed and joined to the worker; the proxy is constructed around the
/// substrate; and only then is the worker told about its proxy. Building it in any other order leaves one
/// half holding a null reference to the other, which the legacy avoids by the same sequencing.
/// </para>
/// </remarks>
internal sealed class UpdateTaskFactory : IUpdateTaskFactory
{
    /// <summary>The worker class name the caller-side substrate registers.</summary>
    /// <remarks>
    /// The exact string the oracle's <c>event ongettaskclsname</c> answers, lower-cased when stored, and
    /// contract rather than description: the substrate resolves a worker BY this name.
    /// </remarks>
    internal const string WorkerClassName = "n_cst_thread_task_sqlupdate";

    /// <summary>Resolves per-task collaborators.</summary>
    private readonly IServiceProvider _services;

    /// <summary>The sessions an update task is created against.</summary>
    private readonly TransactionSessionRegistry _sessions;

    /// <summary>The shared transaction pool.</summary>
    private readonly TransactionPool _transactionPool;

    /// <summary>The shared datastore factory.</summary>
    private readonly ISqlDataStoreFactory _dataStoreFactory;

    /// <summary>The shared retrieval-hook activator.</summary>
    private readonly ISqlRetrievalHookActivator _hookActivator;

    /// <summary>The adapter that wraps a store as an update-capable carrier.</summary>
    private readonly ISqlUpdateCarrierAdapter _carrierAdapter;

    /// <summary>The conflict classifier.</summary>
    private readonly ConflictDetector _conflictDetector;

    /// <summary>The statement redactor the worker-side host logs through.</summary>
    private readonly ISqlRedactor _redactor;

    /// <summary>The injected clock.</summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>Creates task and proxy loggers.</summary>
    private readonly ILoggerFactory _loggerFactory;

    /// <summary>Initializes the factory.</summary>
    /// <param name="services">The container, used to resolve a fresh host per task.</param>
    /// <param name="sessions">The transaction-session registry.</param>
    /// <param name="transactionPool">The shared transaction pool.</param>
    /// <param name="dataStoreFactory">The shared datastore factory.</param>
    /// <param name="hookActivator">The shared retrieval-hook activator.</param>
    /// <param name="carrierAdapter">The update-carrier adapter.</param>
    /// <param name="conflictDetector">The conflict classifier.</param>
    /// <param name="redactor">The statement redactor.</param>
    /// <param name="timeProvider">The injected clock.</param>
    /// <param name="loggerFactory">The factory task and proxy loggers are created from.</param>
    public UpdateTaskFactory(
        IServiceProvider services,
        TransactionSessionRegistry sessions,
        TransactionPool transactionPool,
        ISqlDataStoreFactory dataStoreFactory,
        ISqlRetrievalHookActivator hookActivator,
        ISqlUpdateCarrierAdapter carrierAdapter,
        ConflictDetector conflictDetector,
        ISqlRedactor redactor,
        TimeProvider timeProvider,
        ILoggerFactory loggerFactory)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _transactionPool = transactionPool ?? throw new ArgumentNullException(nameof(transactionPool));
        _dataStoreFactory = dataStoreFactory ?? throw new ArgumentNullException(nameof(dataStoreFactory));
        _hookActivator = hookActivator ?? throw new ArgumentNullException(nameof(hookActivator));
        _carrierAdapter = carrierAdapter ?? throw new ArgumentNullException(nameof(carrierAdapter));
        _conflictDetector = conflictDetector ?? throw new ArgumentNullException(nameof(conflictDetector));
        _redactor = redactor ?? throw new ArgumentNullException(nameof(redactor));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
    }

    /// <inheritdoc/>
    public long TryCreate(string sessionId, out IUpdateTaskSurface? task)
    {
        ArgumentNullException.ThrowIfNull(sessionId);

        // ASSIGNED BEFORE ANY EARLY RETURN, so every refusal arm answers with no surface rather
        // than with an unassigned one. A caller reads the code first, but a null out-parameter is
        // what makes a refusal safe to ignore the code on.
        task = null;

        if (!_sessions.TryResolve(sessionId, out TransactionSession? session) || session is null)
        {
            // The contract's own code for an unknown or ended session. A caller learns the truth rather
            // than receiving a task bound to nothing.
            return RetCode.E_INVALID_TRANSACTION;
        }

        UpdateFaultCollector faults = new();

        PersistenceSqlTaskHost workerHost = new(
            _services.GetRequiredService<ILogger<PersistenceSqlTaskHost>>(),
            _redactor,
            faults,
            parentTasking: null);

        // THE PROXY IS BUILT FIRST, AND THAT ORDERING IS FORCED RATHER THAN CHOSEN. The worker publishes
        // its identity blocks and its row counts INTO the caller-side proxy [:L243, :L247], and it takes
        // that proxy at construction - so the proxy has to exist before the worker does. The substrate's
        // worker reference is therefore assigned afterwards, which is exactly the sequence the oracle uses:
        // its own init inserts the worker into the task list and only then assigns `_Task` [:L188].
        PersistenceSqlTaskProxyHost proxyHost = new(WorkerClassName);

        SqlUpdateTaskProxy proxy = new(
            proxyHost,
            _loggerFactory.CreateLogger<SqlUpdateTaskProxy>(),
            _timeProvider);

        SqlUpdateTask worker = new(
            workerHost,
            _transactionPool,
            _dataStoreFactory,
            _hookActivator,
            _carrierAdapter,
            _conflictDetector,
            _timeProvider,
            _loggerFactory.CreateLogger<SqlUpdateTask>(),
            proxy);

        proxyHost.Worker = worker;

        // THE HOST MUST OWN THE TASK, NOT MERELY SERVE IT - the same obligation the retrieval factory
        // discharges at QueryTaskFactory.Create, and the omission of it here was observable. The
        // inherited commit-signal walk counts DOWN from the host's task index and sets the commit signal
        // of every task the host RESOLVES [n_cst_thread_task_sqlbase.sru:L100-L111]; an unbound host
        // answers E_OUT_OF_BOUND for every position [PersistenceSqlTaskHost.GetTask], so the walk found
        // nothing, no signal was ever set, and the caller-side `IsCommitted()` could not become true
        // however the transaction actually ended. Bound here because the cycle cannot be closed in either
        // constructor: the worker takes the host, so the host cannot take the worker.
        workerHost.BindTask(worker);

        // `#ParentTasking` ON THE WORKER'S OWN SUBSTRATE, AND WITHOUT THIS LINE THE DATABASE-ERROR
        // CHANNEL IS DEAD. The worker packs its five ondberror scalars into a payload and forwards it to
        // `#ParentTasking` [n_cst_thread_task_sqlbase.sru:L94-L95]; that reference is the caller-side
        // proxy, and it is the proxy's latch that every wire projection of a driver error reads. Left
        // unbound, the base's validity test skipped the forward and a caller was told E_DB_ERROR with an
        // EMPTY payload - no code, no text, no buffer, no row - for a failure the worker had fully in hand.
        // Installed here rather than at construction because the proxy is built before the worker (the
        // worker publishes into it) while the host is built before both, so the cycle can only close here -
        // the same reason the worker reference above is assigned rather than passed.
        //
        // THE SECOND CHANNEL IS NOT A SUBSTITUTE FOR THIS ONE. The fault collector below receives the
        // update task's own framework-error raises, which is a different event with a different payload;
        // it never carries the offending buffer or row, and the proxy's row-translation override
        // [n_cst_threading_task_sqlupdate.sru:L310-L342] is reachable only through this reference.
        workerHost.BindParentTasking(proxy);

        // THE ORACLE'S ONE LIVE `#Running` GUARD, AND WITHOUT THIS LINE IT READS FALSE FOR EVER. Every
        // other guard written against `#Running` in n_cst_thread_task_sqlbase is COMMENTED OUT in the
        // oracle and carried across inert, but the two in n_cst_thread_task_sqlupdate are live [:L60] -
        // they make of_Reset and the descriptor reset answer E_BUSY mid-run rather than clearing the
        // inputs of a running update. The predicate is a seam precisely so the flag can live on the
        // caller side, which is where the oracle keeps it [n_cst_threading_task.sru:L112]; leaving it
        // unassigned makes both guards unreachable and a mid-run reset silently permitted.
        worker.IsRunning = () => proxyHost.IsRunning;

        // THE SEAM'S ORDERING OBLIGATION. Initialization borrows the worker's commit signal
        // [n_cst_threading_task_sqlbase.sru:L218], and the committed notification signals only an ALREADY
        // CREATED handle [n_cst_thread_task_sqlbase.sru:L100-L111]. Skipping it leaves every commit
        // notification unobserved, so the code is checked rather than discarded.
        long initialized = proxy.Initialize();

        if (initialized != RetCode.OK)
        {
            proxyHost.Dispose();
            worker.Dispose();

            return initialized;
        }

        // ==========================================================================================
        //  THE SESSION'S OWN DESCRIPTOR, AND WITHOUT IT THE TASK WRITES ON A DIFFERENT CONNECTION
        //  ------------------------------------------------------------------------------------------
        //  The pool keys its entries on WHOLE-DESCRIPTOR VALUE EQUALITY and appends a new one when
        //  nothing matches [TransactionPool.AddRefCore, the port of :L136-L146]. A task whose stored
        //  descriptor is still the default therefore leases a SECOND entry, with a second transaction
        //  object over a second connection - and every consequence of that is silent:
        //
        //    * the update runs inside the second entry's transaction, so the `Commit` a caller sends on
        //      C-08 against ITS session commits a transaction that never saw the write;
        //    * on a file-backed store the two connections contend, and the loser cannot even open a
        //      transaction - the write then fails with the provider's busy code for a reason that has
        //      nothing to do with the caller's data;
        //    * the identity round trip and the row counts are read back through the entry the task
        //      holds, so they describe the wrong unit of work.
        //
        //  Applied AFTER the proxy's initialization and BEFORE the surface is published, because
        //  SetTransData drops any pool reference already taken for a previous descriptor [:L121-L125]
        //  and nothing may hold the surface across that drop. Its own code is checked rather than
        //  discarded: a refusal here means the task is bound to no session's transaction, and handing
        //  back a surface in that state would defer the fault to the first statement.
        //
        //  THE AUTO-COMMIT MEMBER IS ERASED BY SetTransData ITSELF [:L118-L119], so the stored
        //  descriptor still compares equal to the session's and the two share one entry. A caller's
        //  later SetAutoCommit therefore changes the borrowed transaction's mode rather than re-keying
        //  the borrow, which is the oracle's own shape.
        // ==========================================================================================
        long applied = worker.SetTransData(session.Descriptor);

        if (!Predicates.IsSucceeded(applied))
        {
            proxyHost.Dispose();
            worker.Dispose();

            return applied;
        }

        task = new UpdateTaskSurface(session, worker, proxy, proxyHost, faults);

        return RetCode.OK;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// THE ONLY IMPLEMENTATION THAT ACTUALLY HOLDS A GATE, because it is the only <c>IUpdateTaskFactory</c>
    /// with a session registry behind it. The session is re-resolved rather than carried over from
    /// <see cref="TryCreate"/>, so a session that ended in between yields the unguarded-but-retiring answer
    /// and the adapter refuses instead of publishing a task against a transaction nobody holds any more.
    /// </para>
    /// <para>
    /// The liveness flag is read AFTER the gate is taken and returned as a snapshot, so it stays true for as
    /// long as the window lives. That is what makes the adapter's test-then-register one step: <c>EndSession</c>
    /// cannot mark the session closing while this window is open, because marking happens inside this same
    /// gate.
    /// </para>
    /// </remarks>
    public async ValueTask<UpdateTaskPublication> EnterPublicationAsync(
        string sessionId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sessionId);

        if (!_sessions.TryResolve(sessionId, out TransactionSession? session) || session is null)
        {
            // ALREADY GONE, WHICH IS REPORTED AS RETIRING RATHER THAN AS LIVE. A session that has left the
            // registry has had - or is having - its transaction handed back, so publishing against it is
            // exactly the outcome this window exists to prevent. No gate is held because there is no
            // session to hold one for.
            return new UpdateTaskPublication(default, isSessionRetiring: true);
        }

        TransactionGateScope gate = await session.Gate
            .EnterAsync(cancellationToken)
            .ConfigureAwait(false);

        return new UpdateTaskPublication(gate, session.IsClosing);
    }
}

/// <summary>
/// The provisioned <see cref="ICommandTaskFactory"/>: it builds a command proxy pair.
/// </summary>
/// <remarks>
/// A command task is a PAIR, and the caller-side half's substrate is what a headless service is easily
/// argued not to have. It does have one - <c>Tasks/TaskProxies/PersistenceSqlTaskProxyHost.cs</c>
/// is that substrate, ported as flags and a cancellation token rather than as Win32 handles, which is the
/// substitution the migration records for the deliberately non-ported handle surface. So the pair is built
/// in full and neither half is flattened into the other.
/// </remarks>
internal sealed class CommandTaskFactory : ICommandTaskFactory
{
    /// <summary>The worker class name the caller-side substrate registers.</summary>
    internal const string WorkerClassName = "n_cst_thread_task_sqlcommand";

    /// <summary>Resolves per-task collaborators.</summary>
    private readonly IServiceProvider _services;

    /// <summary>The shared transaction pool.</summary>
    private readonly TransactionPool _transactionPool;

    /// <summary>The shared datastore factory.</summary>
    private readonly ISqlDataStoreFactory _dataStoreFactory;

    /// <summary>The shared retrieval-hook activator.</summary>
    private readonly ISqlRetrievalHookActivator _hookActivator;

    /// <summary>The statement redactor the worker-side host logs through.</summary>
    private readonly ISqlRedactor _redactor;

    /// <summary>The injected clock.</summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>Creates task and proxy loggers.</summary>
    private readonly ILoggerFactory _loggerFactory;

    /// <summary>Initializes the factory.</summary>
    /// <param name="services">The container, used to resolve a fresh host per task.</param>
    /// <param name="transactionPool">The shared transaction pool.</param>
    /// <param name="dataStoreFactory">The shared datastore factory.</param>
    /// <param name="hookActivator">The shared retrieval-hook activator.</param>
    /// <param name="redactor">The statement redactor.</param>
    /// <param name="timeProvider">The injected clock.</param>
    /// <param name="loggerFactory">The factory task and proxy loggers are created from.</param>
    public CommandTaskFactory(
        IServiceProvider services,
        TransactionPool transactionPool,
        ISqlDataStoreFactory dataStoreFactory,
        ISqlRetrievalHookActivator hookActivator,
        ISqlRedactor redactor,
        TimeProvider timeProvider,
        ILoggerFactory loggerFactory)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _transactionPool = transactionPool ?? throw new ArgumentNullException(nameof(transactionPool));
        _dataStoreFactory = dataStoreFactory ?? throw new ArgumentNullException(nameof(dataStoreFactory));
        _hookActivator = hookActivator ?? throw new ArgumentNullException(nameof(hookActivator));
        _redactor = redactor ?? throw new ArgumentNullException(nameof(redactor));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
    }

    /// <inheritdoc/>
    public long Create(out CommandTaskComponents? components)
    {
        components = null;

        PersistenceSqlTaskHost workerHost = new(
            _services.GetRequiredService<ILogger<PersistenceSqlTaskHost>>(),
            _redactor,
            faults: null,
            parentTasking: null);

        SqlCommandTask worker = new(
            workerHost,
            _transactionPool,
            _dataStoreFactory,
            _hookActivator,
            _timeProvider,
            _loggerFactory.CreateLogger<SqlCommandTask>());

        // THE HOST MUST OWN THE TASK, NOT MERELY SERVE IT - see the identical note in
        // UpdateTaskFactory.TryCreate and the original at QueryTaskFactory.Create. Without this line the
        // commit-signal walk [n_cst_thread_task_sqlbase.sru:L100-L111] resolves nothing, so the AC_NATIVE
        // arm's direct OnCommitted() raise - the one place in the command path that signals a commit
        // WITHOUT performing one, because the provider already made the work durable - set no signal at
        // all and ExecResponse.committed reported false for a write that was durable on disk.
        workerHost.BindTask(worker);

        PersistenceSqlTaskProxyHost proxyHost = new(WorkerClassName) { Worker = worker };

        SqlCommandTaskProxy proxy = new(
            proxyHost,
            _loggerFactory.CreateLogger<SqlCommandTaskProxy>(),
            _timeProvider);

        // `#ParentTasking`, for the same reason and with the same consequence as the update pair - see the
        // long note at UpdateTaskFactory.TryCreate. This path reads the caller-side latch too
        // [Grpc/CommandService.cs, `task.Proxy.GetLastDbErrorData()`] and decides from its emptiness whether
        // to publish a driver payload at all, so leaving the reference unbound meant a failing command could
        // never carry one: the response's error text fell back to the transaction's own message and the
        // structured payload the contract declares was permanently absent.
        workerHost.BindParentTasking(proxy);

        // The same ordering obligation as the update pair - see UpdateTaskFactory.TryCreate.
        long initialized = proxy.Initialize();

        if (initialized != RetCode.OK)
        {
            proxyHost.Dispose();
            worker.Dispose();

            return initialized;
        }

        components = new CommandTaskComponents(proxy, worker);

        return RetCode.OK;
    }
}

/// <summary>
/// Collects the framework errors an update task raises, on the caller's side of the pair.
/// </summary>
/// <remarks>
/// The update path's worker-side host forwards its framework errors to a sink, exactly as the retrieval
/// path's does, and this is that sink for an update. It stores rather than logs: the host has already
/// recorded a redacted projection, and a second record here would be a second opportunity to disclose the
/// same thing.
/// </remarks>
internal sealed class UpdateFaultCollector : IQueryFaultSink
{
    /// <summary>The last framework error code raised, or <see cref="RetCode.OK"/> when none was.</summary>
    internal long ErrorCode { get; private set; } = RetCode.OK;

    /// <summary>The last framework error text raised.</summary>
    internal string ErrorText { get; private set; } = string.Empty;

    /// <summary>The last database error raised, or the cleared payload when none was.</summary>
    internal DbErrorData LastDbError { get; private set; } = DbErrorData.Empty;

    /// <inheritdoc/>
    public void OnError(long errCode, string errInfo)
    {
        ErrorCode = errCode;
        ErrorText = errInfo ?? string.Empty;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Taken by <see langword="in"/> and stored, never logged. The statement field carries the complete
    /// generated statement including any interpolated literal value, and the place that redacts it for a
    /// log record is the task that raised it.
    /// </remarks>
    public void OnDbError(in DbErrorData error) => LastDbError = error;
}

/// <summary>
/// The caller-side surface of one update task: the eleven contract operations, over the real pair.
/// </summary>
/// <remarks>
/// <para>
/// A BRIDGE AND NOTHING MORE. Every member forwards to the worker, whose own implementation carries the
/// ported protocol - the busy guards, the descriptor admission rules, the absence-must-survive-as-absence
/// rule for the two nullable settings, and the {reset, prepare} asymmetry. Re-implementing any of it here
/// would fork one rule into two places.
/// </para>
/// <para>
/// 🔴 THE SESSION IS HELD FOR TWO THINGS, AND THE SECOND IS LOAD-BEARING. It correlates the task with the
/// session it was created against, which is what the log records that pair the two read - and it carries
/// THE TRANSACTION GATE this surface takes around the run. Every operation on one pooled transaction
/// object holds that gate, so the update cannot overlap a query, a command or a commit on the same object
/// (AAP 0.4.5.4). The session's own LIFECYCLE still belongs to the transaction service; this surface only
/// reads its liveness flag inside the gate, which is the one place the reading is trustworthy.
/// </para>
/// </remarks>
internal sealed class UpdateTaskSurface : IUpdateTaskSurface
{
    /// <summary>The session this task was created against.</summary>
    private readonly TransactionSession _session;

    /// <summary>The worker-side task.</summary>
    private readonly SqlUpdateTask _worker;

    /// <summary>The caller-side proxy.</summary>
    private readonly SqlUpdateTaskProxy _proxy;

    /// <summary>The caller-side substrate.</summary>
    private readonly PersistenceSqlTaskProxyHost _proxyHost;

    /// <summary>The caller-side fault collector.</summary>
    private readonly UpdateFaultCollector _faults;

    /// <summary>Whether this surface has been disposed.</summary>
    private bool _disposed;

    /// <summary>Initializes the surface.</summary>
    /// <param name="session">The session this task was created against.</param>
    /// <param name="worker">The worker-side task.</param>
    /// <param name="proxy">The caller-side proxy.</param>
    /// <param name="proxyHost">The caller-side substrate.</param>
    /// <param name="faults">The caller-side fault collector.</param>
    internal UpdateTaskSurface(
        TransactionSession session,
        SqlUpdateTask worker,
        SqlUpdateTaskProxy proxy,
        PersistenceSqlTaskProxyHost proxyHost,
        UpdateFaultCollector faults)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _worker = worker ?? throw new ArgumentNullException(nameof(worker));
        _proxy = proxy ?? throw new ArgumentNullException(nameof(proxy));
        _proxyHost = proxyHost ?? throw new ArgumentNullException(nameof(proxyHost));
        _faults = faults ?? throw new ArgumentNullException(nameof(faults));
    }

    /// <summary>The session this task was created against.</summary>
    internal TransactionSession Session => _session;

    /// <inheritdoc/>
    public long Reset() => _proxy.Reset();

    /// <inheritdoc/>
    /// <remarks>
    /// REPLACEMENT, NOT TRIMMING, and separate from <see cref="Reset"/> precisely because a prepare must
    /// not also discard the payload, the autocommit flag or the source.
    /// </remarks>
    public long ResetUpdatableTables() => _worker.Tables.Reset();

    /// <inheritdoc/>
    public long SetMultiTableUpdate(bool multiTable) => _proxy.SetMultiTableUpdate(multiTable);

    /// <inheritdoc/>
    public long AddUpdatableTable(
        string name,
        IEnumerable<string> updatableColumns,
        IEnumerable<string> keyColumns,
        string identityColumn,
        long? updateWhere,
        bool? updateKeyInPlace) =>
        _proxy.AddUpdatableTable(
            name,
            [.. updatableColumns],
            [.. keyColumns],
            identityColumn,
            updateWhere,
            updateKeyInPlace);

    /// <inheritdoc/>
    public long AddUpdatableTable(
        string name,
        IEnumerable<string> updatableColumns,
        IEnumerable<string> keyColumns,
        string identityColumn) =>
        _proxy.AddUpdatableTable(name, [.. updatableColumns], [.. keyColumns], identityColumn);

    /// <inheritdoc/>
    public long SetDataObject(string dataObject) => _proxy.SetDataObject(dataObject);

    /// <inheritdoc/>
    public long SetSqlSyntax(string sqlSyntax) => _proxy.SetSqlSyntax(sqlSyntax);

    /// <inheritdoc/>
    /// <remarks>
    /// A LOCAL, because the proxy's installer takes its payload by <c>ref</c> - the oracle's
    /// <c>of_setupdatedata(ref blob, long)</c> - and an interface parameter is not addressable. The value
    /// the installer may write back is deliberately discarded here: the contract's own signature passes
    /// the payload IN, so a caller has nothing to read back.
    /// </remarks>
    public long SetUpdateData(CarrierState? updateData, long updateRows)
    {
        CarrierState? payload = updateData;

        return _proxy.SetUpdateData(ref payload, updateRows);
    }

    /// <inheritdoc/>
    public long SetAutoCommit(bool autoCommit) => _proxy.SetAutoCommit(autoCommit);

    /// <inheritdoc/>
    /// <remarks>
    /// Read from the WORKER, which is where the two source fields live [<c>:L38-L39</c>] and where the
    /// shaping branch reads them [<c>:L316-L331</c>]. The proxy holds no copy, deliberately: one field with
    /// two homes is a field that can disagree with itself.
    /// </remarks>
    public bool HasUpdateSource => _worker.DataObject.Length != 0 || _worker.SqlSyntax.Length != 0;

    /// <inheritdoc/>
    public string DataObject => _worker.DataObject;

    /// <inheritdoc/>
    /// <remarks>
    /// The run's outcome is assembled from the worker's own answer and the caller-side latches, which is
    /// where the oracle reads each of them from: the code and the classification from the task, the three
    /// counts and the identity blocks from the proxy's running totals, and the latched driver error from
    /// the caller-side collector. Nothing is recomputed here.
    /// </remarks>
    public UpdateRunResult Execute(CancellationToken cancellationToken)
    {
        // 🔴 THE TRANSACTION GATE, HELD ACROSS THE BODY AND THE RESULT CAPTURE BOTH. C-06 shares one
        // pooled transaction object with C-05, C-07 and C-08 - the pool keys its entries on
        // WHOLE-descriptor equality, so two sessions opened with equal descriptors are handed the SAME
        // object [ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans_pool.sru:L136-L172] - and the
        // legacy is free of any race on it only because each pool lives on ONE worker thread [:L194-L206].
        // A concurrent server has to reproduce that guarantee explicitly (AAP 0.4.5.4): the transaction's
        // SQL code, error text and statement status are shared mutable state, and its rollback path saves,
        // mutates and restores five of them [n_cst_thread_trans.sru:L163-L183], so a sibling landing
        // between the save and the restore would observe state no single-threaded caller could.
        //
        // THE CAPTURE IS INSIDE THE GATE FOR THE SAME REASON THE BODY IS. The result is assembled from the
        // proxy's latched counts, identity blocks and driver error - all written by the body from the
        // transaction it just used - so releasing the gate before assembling it would let another operation
        // overwrite what this run is about to report.
        using TransactionGateScope gate = _session.Gate.Enter();

        // THE LIVENESS TEST BELONGS INSIDE THE GATE. EndSession marks the session closing under this same
        // gate BEFORE it hands the transaction back, so either this update is inside the gate first and
        // completes, or the teardown is and this sees the flag. Executing anyway would write through a
        // transaction the pool has already taken back - and may already have handed to another session.
        // NOTHING IS PUBLISHED on this arm: no statement ran, so there is no outcome, no count and no
        // identity block to report, and a caller reads the code rather than inferring from an empty payload.
        if (_session.IsClosing)
        {
            return new UpdateRunResult { Code = RetCode.E_INVALID_TRANSACTION };
        }

        // RAISED BEFORE THE BODY AND LOWERED IN A GUARANTEED finally. The substrate raises its running
        // flag as part of dispatching a task [n_cst_thread_task.sru:L164], and the guards that read it
        // are only meaningful while it is up. The finally is not defensive tidying either: a body that
        // threw would leave the flag raised, and every subsequent mutator on this task would then answer
        // E_BUSY for the life of the handle.
        _proxyHost.IsRunning = true;

        long code;

        try
        {
            code = _worker.OnDoTask(cancellationToken);
        }
        finally
        {
            _proxyHost.IsRunning = false;
        }

        // AN EMPTY LATCH IS NO DRIVER ERROR, AND PUBLISHING IT AS ONE IS NOT HARMLESS. The caller-side
        // latch holds a default-valued payload until a driver error is actually raised into it, and the
        // wire projection of that default is a fully-formed DbError whose every member happens to be
        // empty or zero. A consumer cannot tell that apart from a genuine error carrying an unset code:
        // the presence of the payload IS the signal. So the emptiness test is the same one the command
        // path applies before it publishes, and a run with nothing latched publishes nothing.
        DbErrorData captured = _proxy.GetLastDbErrorData();

        return new UpdateRunResult
        {
            Code = code,
            Outcome = _worker.LastOutcome,
            Counts = new UpdateRowCounts(
                Inserted: _proxy.GetRowsInserted(),
                Updated: _proxy.GetRowsUpdated(),
                Deleted: _proxy.GetRowsDeleted()),
            Identity = _proxy.IdentityBlocks,
            LastDbError = captured != DbErrorData.Empty ? _proxy.GetLastDbErrorForWire() : null,
            ErrorText = _faults.ErrorText,
        };
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Releases the pair in the reverse of the order it was built: the proxy's substrate, then the worker.
    /// Idempotent, because the registry and the service may both release a task on a teardown path.
    /// </remarks>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _proxyHost.Dispose();
        _worker.Dispose();
    }
}

/// <summary>
/// The data-protection key repository, held in this process's memory and never written to storage.
/// </summary>
/// <remarks>
/// <para>
/// WHY THIS TYPE EXISTS AT ALL. Data protection is registered by the authentication stack whether or not
/// anything protects a payload, and its eager initialiser materialises a key ring during host start - which,
/// left at the default, writes an UNENCRYPTED private key into the process's user profile for no consumer.
/// Nothing in this service protects a payload: inbound authentication is bearer-token validation against
/// published verification material, and there is no cookie, no session, no antiforgery token and no
/// protected payload that outlives a request.
/// </para>
/// <para>
/// 🔴 <b>AND THE OBVIOUS FIX IS THE ONE THAT DOES NOT WORK.</b> Swapping
/// <c>IDataProtectionProvider</c> for the framework's ephemeral provider was tried and MEASURED: a key file
/// was still written on every start, because the eager initialiser warms the key-management stack rather
/// than the registered provider. Redirecting the REPOSITORY is what removes the write, because it removes
/// the file-system repository from the graph entirely.
/// </para>
/// <para>
/// The consequence is deliberate and is the reason this posture was chosen: a capability that later needs a
/// DURABLE protector fails immediately and visibly at the first restart, rather than working on one replica
/// and failing on the next. <c>docs/SECRETS.md</c> section 5.4 records the decision, the rejected
/// alternative and what a later phase must put in its place.
/// </para>
/// <para>
/// THREAD SAFETY IS REQUIRED, NOT OPTIONAL. The key ring is read on request threads and written by the
/// initialiser, so every access is taken under one lock. The returned collection is a snapshot, so a caller
/// enumerating it cannot observe a concurrent store.
/// </para>
/// </remarks>
internal sealed class InMemoryDataProtectionKeyRepository : IXmlRepository
{
    /// <summary>The stored elements, guarded by <see cref="_gate"/>.</summary>
    private readonly List<XElement> _elements = [];

    /// <summary>Serialises every read and write of <see cref="_elements"/>.</summary>
    private readonly object _gate = new();

    /// <inheritdoc />
    public IReadOnlyCollection<XElement> GetAllElements()
    {
        lock (_gate)
        {
            // A COPY, and each element cloned: the key manager is free to mutate what it is handed, and a
            // shared instance would let one caller's edit reach another's read.
            return [.. _elements.Select(static element => new XElement(element))];
        }
    }

    /// <inheritdoc />
    public void StoreElement(XElement element, string friendlyName)
    {
        ArgumentNullException.ThrowIfNull(element);

        lock (_gate)
        {
            _elements.Add(new XElement(element));
        }
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
