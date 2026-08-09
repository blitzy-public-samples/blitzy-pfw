// ==================================================================================================
//  Program.cs - THE COMPOSITION ROOT OF THE POWERFRAMEWORK PERSISTENCE SERVICE
//  ------------------------------------------------------------------------------------------------
//  ROLE
//  The ASP.NET Core host for the ONLY service that generates or executes SQL and the ONLY one holding
//  a storage provider. Port 5101. It is the bottom of the layered, acyclic topology: DataServices
//  calls it, it calls no service at all, and it reads Security's published verification material only.
//
//  This file WIRES: the listening endpoints from configuration, inbound token validation, the readiness
//  and liveness routes and the determinism seam. Route declarations live in Endpoints/, next to the
//  contract they implement.
//
//  TRANSPORT, AND WHY IT IS gRPC (constraint C-K)
//  The legacy surface this service ports is action-oriented rather than resource-oriented - Query,
//  Update, Exec, Commit, Rollback, SQLErrText - so it is RPC-shaped. The thirteen SQL task classes
//  carry mandatory thread affinity in their own source comments, the database-error structure
//  dberrordata.srs requires a structured error payload, the mandated concurrency response requires rich
//  status detail, and server streaming reproduces the progressive result delivery of the legacy
//  recordset. Protocol buffers over gRPC carry all four; JSON over REST carries none of them well.
//
//  THE REST SURFACE HERE IS EXACTLY TWO ROUTES. /health for the orchestration readiness gate and
//  /v1/ping for the authentication proof. Everything else this service does travels over gRPC on the
//  second Kestrel endpoint. Both listeners - HTTP/1.1 for REST and HTTP/2 for gRPC - come from the
//  Kestrel section of appsettings.json and are never restated in code, which is what lets the container
//  and the orchestration manifest own the port map (constraint C-J).
//
//  LEGACY REFERENCE (read only - never edited, never built, never shipped: constraint C-C)
//  There is no legacy analogue for a host: PowerFramework is a library with no process of its own, no
//  listener and no server tier. What IS ported is the lifecycle posture of the framework application
//  object at ws_objects/pfw.pbl.src/pfw.sra - a structural fault ends the process rather than degrading
//  past it [:L111-L144] - and the fatality of worker-session creation failure recorded in
//  docs/PB多线程绕坑提示.md.
//
//  FAIL FAST, PRESERVED AS FAIL FAST (constraint C-B)
//  The inbound-token settings are validated at STARTUP: a deployment that names neither an authority nor
//  a metadata address cannot resolve verification material for any token, so the host refuses to start
//  rather than accepting every request as unauthenticated-looking and answering 401 forever. Softening
//  that into warn-and-continue would be a behavioural change dressed up as robustness.
//
//  WHAT THIS FILE DELIBERATELY DOES NOT DO
//    * No HttpClient, no gRPC channel and no client of any kind. Persistence is called BY DataServices
//      and calls nothing, so it has no outbound edge and therefore no transient network fault to handle
//      - which is why Microsoft.Extensions.Http.Resilience is absent from this project (constraint C-A).
//    * No token minting and no signing key. Security is the sole issuer; this service holds verification
//      material only, and SECURITY_JWT_SIGNING_KEY never appears here in any form (constraints C-F, C-G).
//    * No fabricated database. Only SQLite has evidence in the repository - one DDL statement and one
//      connection URI grammar - so no SQL Server or Oracle instance is provisioned and no connection
//      string for either is read. Their behaviours are preserved as pure string transforms under Sql/
//      that need no database of either kind (constraint C-E).
//    * No registration, route, handler or options type for DesignSystem, Documents, Integration or
//      ScriptBridge (constraint C-D).
//    * No OpenAPI document. The project references neither OpenAPI package: this service's published
//      contract is protocol-buffer shaped, and describing two diagnostic routes with a document
//      generator would be scope creep.
//    * No SCREAMING_SNAKE constant is DECLARED here - the preserved legacy identifiers are consumed from
//      PowerFramework.Shared.Kernel, whose files are the ones the repository .editorconfig scopes its
//      naming suppressions to.
// ==================================================================================================

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using PowerFramework.Persistence.Endpoints;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// --------------------------------------------------------------------------------------------------
// 1. INBOUND TOKEN VALIDATION - THE STOCK HANDLER, AND A STARTUP CHECK THAT IT CAN WORK AT ALL
//
// An internal edge is a created boundary too, so this surface is authenticated from the outset. The
// stock bearer handler resolves Security's discovery document and published key set beneath the
// configured authority with zero bespoke retrieval code, which is the property that made Security REST
// rather than gRPC - choosing otherwise would have forced hand-written key-set retrieval into three
// services, a net increase in hand-written security code.
//
// THE SECTION IS THE TOP-LEVEL `Jwt`, NOT `Authentication:Jwt`, AND THAT DIFFERS FROM ITS SIBLINGS ON
// PURPOSE. Every option group this service owns binds from the configuration ROOT - `Sqlite`, `Query`,
// `TransactionPool` and `Jwt` are each a top-level section - so the environment-variable contract reads
// `Jwt__Authority` and `Sqlite__Password` rather than a service-named wrapper around either. DataServices
// and Security nest theirs beneath `Authentication:Jwt` and Gateway binds the framework's stock
// `Authentication:Schemes:Bearer` path; the three spellings are the shape each service's own settings
// file declares, and `shared/PowerFramework.Contracts.Tests/ServiceConfigurationCoherenceTests.cs`
// asserts all three so a rename on either side is a build failure rather than a service that starts and
// then refuses every token. All four validations default to on and each is read from configuration
// rather than hardcoded, so a deployment can tighten but never silently loosen them by omission: a
// missing key leaves the safe value in place. There is no environment-conditional bypass anywhere in
// this file.
//
// METADATA IS FETCHED LAZILY by the handler on first use rather than at startup, which is what keeps
// this host startable while Security is still coming up (constraint C-I) and what makes the
// orchestration health-condition chain resolvable at all. An anonymous request is refused before any
// metadata is needed, because the handler looks for a token first - so the mandated 401 on /v1/ping
// holds even with Security unreachable.
// --------------------------------------------------------------------------------------------------
IConfigurationSection authentication = builder.Configuration.GetSection("Jwt");

string authority = (authentication["Authority"] ?? string.Empty).Trim();
string metadataAddress = (authentication["MetadataAddress"] ?? string.Empty).Trim();

// FAIL FAST. With neither an authority nor a metadata address the handler has nowhere to fetch a
// signing key from, so every token would be rejected and the service would look configured while being
// unable to authenticate anyone. That is a structural fault, and the framework's own posture for a
// structural fault is to stop rather than to continue degraded
// [ws_objects/pfw.pbl.src/pfw.sra:L111-L144]. The message names the configuration keys and quotes no
// value, because a rejected setting may not be echoed into a log.
if (authority.Length == 0 && metadataAddress.Length == 0)
{
    throw new InvalidOperationException(
        "Neither 'Jwt:Authority' nor 'Jwt:MetadataAddress' is configured, "
        + "so inbound tokens could not be verified against the Security service's published key set and "
        + "every authenticated request would be refused. Configure the authority - the Security "
        + "service's base address - and restart. The configured values are deliberately not quoted "
        + "here.");
}

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(bearer =>
    {
        bearer.Authority = authority;
        bearer.Audience = (authentication["Audience"] ?? string.Empty).Trim();
        bearer.RequireHttpsMetadata = authentication.GetValue("RequireHttpsMetadata", true);

        // An explicit metadata address overrides the authority-relative default, which is what lets a
        // deployment point the handler at a key set published somewhere other than the conventional
        // path beneath the authority.
        if (metadataAddress.Length > 0)
        {
            bearer.MetadataAddress = metadataAddress;
        }

        bearer.TokenValidationParameters.ValidateIssuer =
            authentication.GetValue("ValidateIssuer", true);
        bearer.TokenValidationParameters.ValidateAudience =
            authentication.GetValue("ValidateAudience", true);
        bearer.TokenValidationParameters.ValidateLifetime =
            authentication.GetValue("ValidateLifetime", true);
        bearer.TokenValidationParameters.ValidateIssuerSigningKey =
            authentication.GetValue("ValidateIssuerSigningKey", true);
    });

// A FALLBACK POLICY, so no route is ever authenticated by omission (constraint C-G). The endpoint files
// declare their requirements explicitly; this makes the absence of a declaration a closed door rather
// than an open one, and /health opts out in the one place a reader looks for it.
builder.Services.AddAuthorization(static options =>
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build());

// --------------------------------------------------------------------------------------------------
// 2. THE DETERMINISM SEAM
//
// Registered rather than read ambiently. The characterization model requires every clock read to be
// maskable from BOTH the master and the candidate recording, and this service holds two clock readers
// of its own: the transaction pool's idle expiry, keyed on the two keep-alive settings, and the ping
// timestamp. Endpoints resolve it optionally and fall back to the system clock, so registering it here
// is what lets a test substitute a deterministic double for the whole host.
// --------------------------------------------------------------------------------------------------
builder.Services.AddSingleton(TimeProvider.System);

// --------------------------------------------------------------------------------------------------
// 3. THE PUBLISHED REST SURFACE
//
// Health-check registration ships inside the Microsoft.AspNetCore.App shared framework, so /health
// needs no package reference and none is declared. Problem details are registered so the framework's
// own challenge and the readiness probe's 503 both answer application/problem+json, which is the error
// shape the authored contracts publish.
// --------------------------------------------------------------------------------------------------
builder.Services.AddHealthChecks();
builder.Services.AddProblemDetails();

WebApplication app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

// One call per endpoint file. /health is anonymous and /v1/ping requires a token and answers 401
// without one; both properties are declared inside those files, where they are exercised.
app.MapHealthEndpoints();
app.MapPingEndpoints();

app.Run();

/// <summary>
/// The reachable entry-point type for the in-process service tests.
/// </summary>
/// <remarks>
/// LOAD BEARING, NOT CEREMONIAL. Top-level statements compile to an internal <c>Program</c> class, so
/// without this declaration <c>WebApplicationFactory&lt;Program&gt;</c> in the sibling
/// <c>PowerFramework.Persistence.Tests</c> project cannot name the entry point, the service-level tests
/// cannot boot this host at all, and the per-service coverage gate becomes unreachable for every line in
/// this file.
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
