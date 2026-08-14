// ================================================================================================
// PingEndpoints.cs - the AUTHENTICATED LIVENESS BOUNDARY of the DataServices service.
// ================================================================================================
//
// WHAT THIS FILE IS
// One route: GET /v1/ping on this service's REST listener (port 5102, per the Kestrel section of
// appsettings.json and the port map in docs/ARCHITECTURE.md 4.1). It answers 200 with a small
// body when a valid token is presented and 401 when one is not. Both halves are the contract, and
// the 401 is the more important of the two.
//
// WHY A ROUTE THIS SMALL IS WORTH THIS MUCH COMMENTARY
// The legacy PowerFramework opens no listening socket, registers no route and receives no
// unsolicited request: it is an in-process library loaded into one PowerBuilder application. So
// decomposition creates every boundary in this system from nothing, and "no new attack surface"
// cannot mean "no new surface" literally - it has to mean that every newly created surface is
// authenticated from the outset (constraint C-G). That requirement needs a surface a conformance
// test can point at, and this is it. The endpoint exists to make the property TESTABLE rather than
// merely asserted, on all four services rather than only at the ingress, because an internal edge
// that trusted its caller "because it is internal" would itself be a new unauthenticated surface.
//
// THIS SERVICE HOLDS VERIFICATION MATERIAL ONLY. SECURITY IS THE SOLE ISSUER.
// Read this before adding anything to this file.
//
// Exactly one signing credential exists in the whole system and Security holds it. Security mints
// short-lived service tokens and publishes verification material at the standard key-set path,
// with discovery metadata beside it. Both publication paths are anonymous, because verification
// material is public rather than sensitive. Gateway, DataServices and Persistence validate
// against it and none of them can mint a token.
//
// That is enforced structurally rather than by review: PowerFramework.DataServices.csproj
// references Microsoft.AspNetCore.Authentication.JwtBearer (validation) and deliberately does NOT
// reference Microsoft.IdentityModel.JsonWebTokens (minting), so this assembly physically lacks the
// minting capability. Do not add that package, and do not add key material of any kind here - no
// signing key, no PEM block, no base64 DER blob, no shared symmetric material, no token literal,
// not in code and not in a comment (constraint C-F). The repository-wide sweep recorded in
// docs/SECRETS.md found eight in-source credential sites plus three inside vendored binaries; the
// posture toward every one of them is "never replicate, document, and rotate". One of those sites
// embeds a plaintext RSA signing credential in a legacy browser asset and signs a JWS with it. It
// is named in the plan as the ANTI-PATTERN TO REPLACE, and nothing resembling it may appear
// anywhere in the .NET tree.
//
// DO NOT HAND-ROLL THE VALIDATION PATH EITHER.
// Program.cs registers the STOCK Microsoft.AspNetCore.Authentication.JwtBearer handler and binds it
// from the Authentication:Jwt section of appsettings.json (Authority / MetadataAddress pointing at
// Security, Audience, RequireHttpsMetadata and the four Validate* switches). That handler performs
// signature checking, key-set fetching and rollover, issuer and audience validation and clock-skew
// tolerance by itself. Keeping the security-critical path inside framework code rather than
// hand-written code is the whole reason Security speaks REST rather than gRPC, so a bespoke
// key-set or discovery fetcher here would be a net INCREASE in hand-written security code - the
// opposite of the requirement. This file constructs no key-set address, fetches no metadata, reads
// no value out of the Authentication:Jwt section, and registers no authentication scheme, no
// authorization policy and no options type. It maps a route and requires authorization on it.
// Security is reached only through published contract material, namely the C-01 endpoints described
// in shared/PowerFramework.Contracts/OpenApi/security.v1.yaml (constraint C-A).
//
// NO DEVELOPMENT BYPASS, IN ANY ENVIRONMENT.
// RequireAuthorization is applied unconditionally. There is no environment-conditional branch
// around it, no anonymous-access override anywhere in this file, no "test token" shortcut, no
// header-based escape and no configuration switch that turns authentication off. A bypass that
// exists only in Development is still a code path that ships. The ONLY anonymous endpoint on
// this service is /health, which has to be anonymous because the orchestrator probing it holds no
// token - requiring one would make readiness depend on the very service being probed. That
// single documented exception lives in HealthEndpoints.cs and does not leak into this file.
//
// WHAT THE BODY MAY AND MAY NOT CARRY
// The 200 body is the successful half of the authentication proof and nothing more: the responding
// service name, an explicit authenticated flag, the legacy return code for success, and the instant
// the response was produced. It deliberately does NOT echo the presented token, any claim of it,
// any part of the verification material, the configured Authority or MetadataAddress, any other
// configuration value, any upstream address, any exception message or any stack trace. A ping is a
// liveness signal, not a token-introspection or diagnostics endpoint (constraint C-B). Health data
// belongs to HealthEndpoints.cs and the capability gate belongs to Gateway alone.
//
// THE SHAPE MIRRORS THE AUTHORED CONTRACTS ON PURPOSE
// There is no shared/PowerFramework.Contracts/OpenApi/dataservices.v1.yaml - the OpenApi folder
// holds gateway.v1.yaml and security.v1.yaml only, and that omission is deliberate, so THIS
// service's OpenAPI document is generated at run time from the metadata declared below. Both
// authored contracts describe their own /v1/ping identically: operationId "ping", the Diagnostics
// tag, a 200 carrying a PingResponse of service plus authenticated plus an optional timestamp, and
// a declared 401. The member names, the operation id, the tag and the security scheme name below
// are chosen to match them, so the three services present one surface rather than three dialects.
// The `retCode` member is this body's one addition: it reuses the legacy return-code vocabulary
// that ProblemDetails.retCode already carries across the estate, so a client that branches on
// retCode needs no second mechanism here. It contradicts no published schema, because no published
// schema describes this service's ping body.
//
// NO PERFORMANCE OBJECTIVE IS ASSERTED ANYWHERE IN THIS FILE.
// The repository publishes no service-level agreement, no latency budget, no throughput target and
// no availability commitment, so none may be claimed, implied or used to justify a choice here.
// This endpoint reports whether the boundary is reachable AND authenticated. It measures nothing.
//
// LEGACY PROVENANCE: REFERENCE ONLY, NEVER PORTED
// ws_objects/pfw.pbl.src/pfw.sra is the application object whose open / close lifecycle
// (:L88-L109) these operational endpoints attach to in the target. It contains no authentication of
// any kind, because it has nothing to authenticate against - so there is no legacy behaviour to
// preserve on this route, only a new boundary to secure. Not one line of it is ported here, and the
// legacy tree is never edited, moved or reformatted (constraint C-C).
//
// ================================================================================================

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using PowerFramework.Shared.Kernel;

namespace PowerFramework.DataServices.Endpoints;

/// <summary>
/// Maps the DataServices service's authenticated liveness route, <c>GET /v1/ping</c>.
/// </summary>
/// <remarks>
/// <para>
/// The route requires a valid JSON Web Token and answers <c>401 Unauthorized</c> without one, in
/// every environment including Development. There is no development bypass and no anonymous
/// fallback. The only anonymous endpoint on this service is <c>/health</c>.
/// </para>
/// <para>
/// Token validation is delegated entirely to the stock JWT bearer handler registered in
/// <c>Program.cs</c> from the <c>Authentication:Jwt</c> configuration section. This service holds
/// verification material only; the Security service is the sole token issuer in the system. No
/// signing key, key-set fetcher or discovery-document reader belongs in this type.
/// </para>
/// </remarks>
public static class PingEndpoints
{
    /// <summary>
    /// The responding service's canonical name, as it appears in the <c>service</c> member of the
    /// response body.
    /// </summary>
    /// <remarks>
    /// The value is fixed by the closed service enumeration published in
    /// <c>shared/PowerFramework.Contracts/OpenApi/gateway.v1.yaml</c>, whose upstream-health member
    /// admits exactly <c>persistence</c>, <c>dataservices</c> and <c>security</c>. It is a SERVICE
    /// NAME and must not be confused with this service's token AUDIENCE, which is a different
    /// string and lives in configuration rather than in source.
    /// </remarks>
    private const string ServiceName = "dataservices";

    /// <summary>
    /// The route pattern. Fixed by the attached environment's operational constraints, which name
    /// this exact path as the authenticated endpoint on every service in the 5101-5105 band.
    /// </summary>
    private const string RoutePattern = "/v1/ping";

    /// <summary>
    /// The endpoint name, which also becomes the operation identifier in the generated OpenAPI
    /// document. Spelled to match the <c>operationId</c> of the same operation in both authored
    /// contracts.
    /// </summary>
    private const string EndpointName = "ping";

    /// <summary>
    /// The OpenAPI tag. The authored contracts group the readiness half of the health-and-readiness
    /// contract under <c>Health</c> and the authentication-proof half under <c>Diagnostics</c>,
    /// deliberately, because the two halves have opposite authentication postures and a reader
    /// scanning the groupings should see that rather than infer it from one mixed group.
    /// </summary>
    private const string TagName = "Diagnostics";

    /// <summary>
    /// The name of the bearer security scheme, matching the scheme name declared by both authored
    /// contracts so that the three services describe one scheme rather than three.
    /// </summary>
    private const string BearerSchemeName = "bearerAuth";

    /// <summary>
    /// Maps <c>GET /v1/ping</c>, the authenticated liveness route, onto the supplied endpoint route
    /// builder.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder to map the route onto.</param>
    /// <returns>
    /// The same <paramref name="endpoints"/> instance, so that mapping calls compose.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="endpoints"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// This method authorises the route; it does not authenticate it. Registering the
    /// authentication scheme, the authorization services and the bearer options is
    /// <c>Program.cs</c>'s responsibility, and deliberately not this file's.
    /// </remarks>
    public static IEndpointRouteBuilder MapPingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet(RoutePattern, Ping)
            // ------------------------------------------------------------------------------------
            // CONSTRAINT C-G, AND THE ONE LINE THIS WHOLE FILE EXISTS FOR.
            //
            // Unconditional. Not wrapped in an environment test, not paired with an
            // anonymous-access fallback, not weakened by a configuration switch. An
            // unauthenticated request reaches the authorization middleware, is challenged by the
            // bearer handler registered in Program.cs, and receives 401 - in Development exactly
            // as in production.
            //
            // No policy name is supplied on purpose. The parameterless form requires an
            // authenticated principal under the application's default policy, which is precisely
            // the property being proved. Naming a policy here would couple this route to a policy
            // definition it does not own and could silently weaken the requirement if that
            // definition were ever relaxed.
            // ------------------------------------------------------------------------------------
            .RequireAuthorization()
            .WithName(EndpointName)
            .WithTags(TagName)
            .WithSummary("The standing proof that this boundary is authenticated. Token required.")
            .WithDescription(
                "Returns a trivial success only when a valid token is presented, and 401 without " +
                "one. Both outcomes are part of the health-and-readiness contract. " +
                "The operation exists so that the authenticated-boundary requirement is testable " +
                "rather than merely asserted, and it is present on all four services rather than " +
                "only at the ingress because an internal edge is a created boundary too. " +
                "The body does not echo the presented token, any claim of it, or any part of the " +
                "verification material.")
            .Produces<PingResponse>(StatusCodes.Status200OK)
            // The 401 is a DECLARED response, not an implementation detail: it is half of what this
            // operation promises. No response type is declared with it, because the challenge is
            // produced by the authentication middleware rather than by this handler, so the body
            // that accompanies it is the host's problem-details shape when the host is configured
            // to produce one and empty otherwise. Declaring a schema here would advertise a body
            // this file cannot guarantee.
            .Produces(StatusCodes.Status401Unauthorized)

            // 🔴 THE TWO STATUSES THIS ROUTE PRODUCES WITHOUT ANY CODE IN THIS FILE, AND BOTH WERE
            // UNDECLARED. Neither comes from the handler, which is why neither was noticed: a reviewer
            // reading this file sees a handler that cannot fail and a response set that matches it.
            //
            // 429 comes from the request-layer limiter, whose ONLY exemption is /health
            // [Configuration/IngressHardening.cs - HealthPath]. 500 comes from the exception handler
            // [Program.cs - UseExceptionHandler] and is reachable upstream of the handler: this route is
            // authenticated, so the bearer handler must obtain Security's key set before the handler is
            // reached, and a retrieval that fails with no last-known-good configuration cached - a cold
            // start while Security is unreachable - faults inside the authentication middleware.
            //
            // /health declares neither and correctly does not: it is the limiter's one exemption, and it is
            // anonymous, so no key set is needed to reach it.
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .AddOpenApiOperationTransformer(DeclareBearerRequirementAsync);

        return endpoints;
    }

    /// <summary>
    /// Produces the successful half of the authentication proof.
    /// </summary>
    /// <param name="httpContext">The current request context.</param>
    /// <returns>A <c>200 OK</c> result carrying the liveness body.</returns>
    /// <remarks>
    /// <para>
    /// The handler is only ever reached with an authenticated principal, because the route requires
    /// authorization. It therefore does not test the principal, does not read a claim from it and
    /// does not report anything about it: the <c>authenticated</c> member is unconditionally
    /// <see langword="true"/> precisely because reaching this line already proves it. That is a
    /// deliberate choice over inspecting the principal, which would turn a liveness signal into a
    /// token-introspection endpoint and leak claim content into a response body.
    /// </para>
    /// <para>
    /// The clock is resolved from request services when one is registered and falls back to the
    /// system clock otherwise, so this handler imposes no registration requirement on
    /// <c>Program.cs</c> while remaining substitutable in a test. The fallback is genuinely
    /// necessary rather than defensive decoration: the host does not register a time provider by
    /// default, so a hard dependency on one would fail at request time in exactly the deployment
    /// that is otherwise correct.
    /// </para>
    /// </remarks>
    private static Ok<PingResponse> Ping(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        IServiceProvider? services = httpContext.RequestServices;
        TimeProvider clock = services?.GetService<TimeProvider>() ?? TimeProvider.System;

        PingResponse body = new(
            Service: ServiceName,
            Authenticated: true,
            RetCode: RetCode.OK,
            Timestamp: clock.GetUtcNow());

        return TypedResults.Ok(body);
    }

    /// <summary>
    /// Declares the bearer security requirement on this operation in the generated OpenAPI
    /// document, registering the scheme itself when the document does not already carry it.
    /// </summary>
    /// <param name="operation">The operation being described.</param>
    /// <param name="context">The transformer context, exposing the document being built.</param>
    /// <param name="cancellationToken">Cancels document generation.</param>
    /// <returns>A completed task; the transformation is synchronous.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="operation"/> or <paramref name="context"/> is
    /// <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// A per-endpoint operation transformer is the sanctioned mechanism for this on .NET 10. The
    /// older per-endpoint OpenAPI configuration extension is deprecated on this toolchain and
    /// raises a deprecation diagnostic, which the repository-wide warnings-as-errors setting turns
    /// into a build failure, so it is not an option here even as a fallback.
    /// </para>
    /// <para>
    /// The generator does not synthesise a security requirement from authorization metadata by
    /// itself, so requiring a token without declaring it would leave the published document
    /// claiming an anonymous operation while the running service answers 401. Declaring it here
    /// keeps the document honest about the one property this operation exists to prove.
    /// </para>
    /// <para>
    /// Both steps are idempotent, so this composes with whatever document-wide security a host
    /// registers instead of fighting it: the scheme is added only when absent, and the requirement
    /// is added only when the operation carries none. An operation-level requirement overrides a
    /// document-level one in OpenAPI, and the two are equivalent here, so either outcome describes
    /// the same wire behaviour.
    /// </para>
    /// <para>
    /// No key material, key-set address or discovery address appears in the declaration. It states
    /// the transport scheme and the token format, which is all a consumer needs in order to point
    /// its own stock bearer handler at the issuer named in that consumer's configuration.
    /// </para>
    /// </remarks>
    private static Task DeclareBearerRequirementAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(context);

        cancellationToken.ThrowIfCancellationRequested();

        OpenApiDocument? document = context.Document;

        if (document is not null &&
            document.Components?.SecuritySchemes?.ContainsKey(BearerSchemeName) != true)
        {
            IOpenApiSecurityScheme bearerScheme = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description =
                    "A JSON Web Token issued by the Security service, presented in the " +
                    "Authorization header. This service validates it with the stock bearer " +
                    "handler against the issuer's published key set and holds no signing " +
                    "authority of its own.",
            };

            document.AddComponent(BearerSchemeName, bearerScheme);
        }

        IList<OpenApiSecurityRequirement> security =
            operation.Security ??= new List<OpenApiSecurityRequirement>();

        if (security.Count == 0)
        {
            security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(BearerSchemeName, document)] =
                    new List<string>(),
            });
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// The successful half of the authentication proof, returned only when a valid token was
    /// presented.
    /// </summary>
    /// <param name="Service">
    /// The responding service's canonical name. Always the DataServices service's own name.
    /// </param>
    /// <param name="Authenticated">
    /// Always <see langword="true"/> when this body is returned, because the operation cannot be
    /// reached without a valid token. It is present so that a conformance test asserting the
    /// authenticated path has something explicit to assert on rather than inferring success from a
    /// status code.
    /// </param>
    /// <param name="RetCode">
    /// The legacy PowerFramework return code for success, surfaced symbolically from the shared
    /// kernel rather than written as a literal. Carried so that a client already branching on the
    /// legacy return-code vocabulary needs no second mechanism on this route.
    /// </param>
    /// <param name="Timestamp">
    /// The instant the response was produced, so a consumer can detect a stale cached response.
    /// </param>
    /// <remarks>
    /// Declared inline in this file rather than in a file of its own, and nested inside its
    /// endpoint class, because it is the shape of exactly one response and belongs with the route
    /// that produces it. Its four members are the whole body: no token, no claim, no verification
    /// material, no configuration value, no upstream address, no exception message and no stack
    /// trace. The first three member names and the response schema name match the equivalent body
    /// in both authored contracts so the estate presents one shape.
    /// </remarks>
    public sealed record PingResponse(
        string Service,
        bool Authenticated,
        long RetCode,
        DateTimeOffset Timestamp);
}
