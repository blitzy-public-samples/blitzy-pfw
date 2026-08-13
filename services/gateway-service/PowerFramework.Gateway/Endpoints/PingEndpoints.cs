// ======================================================================================================
// PingEndpoints.cs
// The Gateway service's authenticated liveness probe: GET /v1/ping, contract C-10.
// ======================================================================================================
//
// WHAT THIS FILE IS
//   One route, and the standing proof that this boundary is authenticated. `/v1/ping` returns a trivial
//   success when a valid bearer token is presented and 401 when one is not, and BOTH outcomes are the
//   published contract rather than implementation detail
//   [shared/PowerFramework.Contracts/OpenApi/gateway.v1.yaml:L548-L583].
//
//   The route exists for exactly one reason, stated by the contract itself: to make the
//   authenticated-boundary requirement TESTABLE rather than merely asserted. It is present on all four
//   in-scope services rather than only at the ingress, because an internal edge is a created boundary
//   too, and that uniformity is part of C-10 [docs/CONTRACTS.md 12.2].
//
//   OpenApi/gateway.v1.yaml is AUTHORITATIVE FOR THE WIRE and was read in full before a single route,
//   status code or payload member was written here. Where it and docs/CONTRACTS.md disagree the document
//   wins; on this operation they agree - CONTRACTS.md 12.1 lists the route as "token required" and 12.2
//   states the posture - so nothing below had to be adjudicated.
//
// LEGACY REFERENCE, AND WHY THERE IS NO LEGACY ANALOGUE TO PORT (constraint C-C)
//   ws_objects/pfw.pbl.src/pfw.sra is the composition-root reference for this service: its `open` event
//   [:L88-L106] pairs framework initialization with the `pfwFinalize()` of its `close` event [:L108],
//   and its `systemerror` event [:L111-L144] unpacks a seven-field assert payload and then terminates.
//   That lifecycle is reproduced under Composition/ and Diagnostics/, not here.
//
//   There is NO legacy `/v1/ping`, and there could not be. PowerFramework is a LIBRARY: 39 PowerBuilder
//   libraries loaded into one process, with no process of its own, no listener, no route table and no
//   server tier. It opens no listening socket and receives no unsolicited request, so this route
//   translates no existing wire format - it is net new, like every route in C-09 and C-10.
//
//   The legacy tree - ws_objects/**, the compiled libraries and targets, oldversion/125/**, pack/**,
//   res/**, samples/**, sciter_control/**, tests/blink|sciter|webview/** and the five Chinese documents
//   under docs/ - is READ ONLY and is the behavioural oracle for parity testing. This file reads it and
//   changes nothing in it.
//
// WHY THIS EDGE IS REST AND NOT gRPC (constraint C-K: document every boundary-specific decision)
//   The transport for each service was decided from the shape of its CURRENT interface rather than
//   chosen once and applied uniformly. Gateway's legacy shape is the coarse `open` / `close` /
//   `systemerror` application lifecycle plus a navigation surface: request/response and nothing more -
//   no ordered chain, no veto, nothing to stream.
//
//   Being the SOLE INGRESS then decides it outright, because an ingress needs three properties that
//   gRPC at the edge would forfeit:
//
//     1. Reach            a browser or third-party client must call it directly, with no intermediary
//                         of ours in the path;
//     2. Alignment with   standard caching semantics, standard proxying and standard status codes that
//        HTTP itself      an operator's existing tooling already understands;
//     3. Mature           so a consumer generates a client from the published document instead of
//        description      hand-writing one.
//        tooling
//
//   gRPC-Web requires a translating proxy in front of it and supports SERVER streaming only, so the
//   reach an ingress exists to provide would depend on an extra hop and the richer streaming the
//   internal contracts rely on would not survive the translation anyway. The internal east-west edges
//   ARE gRPC precisely because the reasoning inverts there: both ends are owned and the contracts are
//   strongly typed and stable. docs/ARCHITECTURE.md records the decision for all four services.
//
// AUTHENTICATION IS THE SINGLE MOST IMPORTANT BEHAVIOUR IN THIS FILE (constraint C-G)
//   C-G says "no new attack surface", and it has to be read honestly. The legacy opened no socket,
//   registered no route and received no unsolicited request, so decomposition creates the system's
//   FIRST-EVER ingress; "no new attack surface" therefore cannot mean "no new surface". It means every
//   newly created surface is authenticated from the outset. This file is that principle's clearest
//   expression, and four properties carry it:
//
//     * A JWT bearer token is REQUIRED, declaratively, through `.RequireAuthorization()`. The framework
//       produces the 401. Nothing here inspects the `Authorization` header, parses a token, or decides
//       for itself whether a caller is authenticated.
//     * IN EVERY ENVIRONMENT, INCLUDING DEVELOPMENT. No environment test, no development-only policy and
//       no configuration switch anywhere in this file can relax it. The development settings file adds
//       no authentication bypass either; it only relaxes metadata retrieval to plain-HTTP loopback for
//       the local authority. A bypass that exists only locally is the exact regression the sibling test
//       suite asserts against.
//     * VERIFICATION ONLY. Gateway mints nothing. Security is the sole issuer in the system and holds
//       the single signing secret; every other in-scope service holds verification material only. That
//       is a structural property of the sole-issuer topology, not a configuration choice. This file
//       names no key, constructs no signing credential, and reads no signing configuration.
//     * The response body does NOT echo the presented token, any claim of it, the configured authority,
//       the key set, or any environment value - the contract states this and constraint C-F requires it.
//
//   Registering the authentication SCHEME is deliberately not this file's job. The composition root owns
//   the stock Microsoft.AspNetCore.Authentication.JwtBearer registration, pointed at Security's own
//   /.well-known/openid-configuration and /.well-known/jwks.json [OpenApi/security.v1.yaml, C-01], so
//   signature checking, key fetching and rollover, issuer and audience validation and clock-skew
//   tolerance all stay inside framework code. This file only REQUIRES authorization.
//
// THE 401 BODY, AND WHY NO retCode IS SURFACED FROM HERE (DECISION 7 - read before "fixing" this)
//   The contract's 401 is the shared `application/problem+json` body
//   [gateway.v1.yaml components/responses/Unauthorized], whose RFC 9457 object carries `retCode` as an
//   extension member. This file DECLARES that response - status, media type and shape - and does not
//   CONSTRUCT it, for a structural reason rather than a stylistic one:
//
//     The 401 is issued by the authorization middleware, which runs BEFORE the endpoint delegate. No
//     endpoint-scoped hook exists that could shape it: an endpoint filter never runs, a route handler
//     never returns, and an OpenAPI transformer only edits the published document. The problem-details
//     body is produced once, at application scope, by the framework's problem-details service.
//
//   So the legacy-faithful code for a denied request is named here for the composition root rather than
//   emitted here: PowerFramework.Shared.Kernel.RetCode.E_ACCESS_DENIED, verified as -25 at
//   ws_objects/pfw.shared.pbl.src/retcode.sru:L68 and present in the contract's closed `retCode`
//   enumeration. It is referenced by SYMBOL, never as a numeric literal, wherever it is emitted.
//
//   Adding an OpenAPI `example` to the 401 so that this file could consume the symbol was considered and
//   REJECTED: gateway.v1.yaml declares no example on its `Unauthorized` response, and that document is
//   authoritative for the wire, so the extra member would make the generated document deviate from the
//   contract it is checked against. Fidelity to the contract outranks a decorative use of a dependency.
//
// WHAT THIS FILE DELIBERATELY DOES NOT DO, each with its reason
//   * It calls no upstream at all. Ping proves the boundary, not the topology; readiness aggregation is
//     `/health`, which is C-10's other half and a different file. There is consequently no typed client,
//     no resilience pipeline and no upstream service name anywhere in this file.
//   * It reaches no storage. No connection string, no ORM context, no database of any kind (C-E: no
//     fabricated database - the only evidenced storage engine belongs to one other service entirely).
//   * It declares no route for any deferred capability. The four services deferred out of this phase are
//     not implemented at all - not partially, and not as stubs (C-D). Their reserved routes are routing
//     metadata that belongs exclusively to Endpoints/DeferredCapabilityEndpoints.cs, and this file
//     declares no route beyond the one above and reaches nothing on their behalf.
//   * It hardcodes no port and names none. Gateway's listener is fixed by the port map in
//     docs/ARCHITECTURE.md and realised by the container and host configuration; a port literal in an
//     endpoint file would be a second source of truth, and the deliberately unallocated Phase-2 slot in
//     that same band is never referenced or reassigned from here either.
//   * It declares no SCREAMING_SNAKE constant. The repository-root .editorconfig scopes its CA1707 and
//     IDE1006 suppressions to the individually named files that genuinely carry preserved legacy
//     identifiers - its BAND 3 roster is the single source of truth for which those are - and
//     no Gateway file is among them; with TreatWarningsAsErrors=true from Directory.Build.props such a
//     declaration would be a build ERROR here. Legacy constants are CONSUMED from
//     PowerFramework.Shared.Kernel - which is safe, because CA1707 reports declarations only - and
//     never declared.
//   * It adds no package and no second file. The five packages PowerFramework.Gateway.csproj already
//     carries plus the Microsoft.AspNetCore.App shared framework are the whole budget, every reference
//     is versionless under central package management, and the response shape is declared INLINE below
//     rather than in a DTO file of its own.
//   * It contains no placeholder. No deferred-work marker, no not-implemented throw, no stub and no
//     mock value: every path below is complete and returns a real, computed result.
//
// DECISIONS
//   1. `MapPingEndpoints` returns the IEndpointRouteBuilder it was given, NOT the RouteHandlerBuilder,
//      and that is a C-G safeguard rather than a style choice. Handing the convention builder back would
//      let a caller append `.AllowAnonymous()`, which wins over `.RequireAuthorization()` in endpoint
//      metadata and would silently turn this route anonymous from a completely different file. Returning
//      the route builder keeps the requirement un-overridable from outside this file while still
//      allowing the composition root to chain its endpoint registrations.
//   2. The clock is resolved from the request's service provider with a system fallback rather than
//      taken as a handler parameter. A `TimeProvider` parameter on a GET handler is inferred as a BODY
//      parameter when nothing registers the type, which fails at STARTUP - a fragile coupling between an
//      endpoint file and a container registration it does not own. Resolving it keeps the determinism
//      seam intact (a test registers a fake clock and gets it) while the endpoint works whether or not
//      one is registered. This mirrors the same seam in Clients/SecurityClient.cs.
//   3. OpenAPI metadata is applied with `AddOpenApiOperationTransformer`, NOT with `WithOpenApi()`.
//      Verified against the package rather than assumed: Microsoft.AspNetCore.OpenApi documents
//      `WithOpenApi` as NOT integrating with the built-in document generation this service uses, and as
//      intended for use alongside a third-party document generator that this refactor deliberately does
//      not reference. Using it here would silently produce metadata nothing reads.
//   4. The bearer requirement is declared ON THE OPERATION. gateway.v1.yaml states it once at document
//      level and overrides it in exactly one place (`/health`, anonymous), and an operation-level
//      requirement carrying the identical single scheme with an empty scope list is semantically the
//      same posture - OpenAPI says an operation-level `security` replaces the document-level one, and
//      here it replaces it with itself. Declaring it here makes the endpoint self-describing, so the
//      authenticated posture cannot be lost by a change to a document-level default in another file.
//   5. The `bearerAuth` scheme component is added ONLY IF the document does not already carry one. The
//      composition root publishes the full scheme with its description; this is a guard so that the
//      requirement above can never dangle against a missing component, and it is a no-op whenever the
//      composition root has done its job. It describes the scheme; it registers no authentication.
//   6. `PingResponse` is public and sealed, mirrors the contract member for member, and is closed by
//      construction - three members, no more. Two honest deviations in the GENERATED schema are recorded
//      rather than hidden, and both are limits of the schema generator rather than of the behaviour:
//        * `additionalProperties: false` is not emitted, because sealed-record closedness is not
//          projected into that keyword; the type cannot carry a fourth member regardless.
//        * The contract's `const: gateway` and `const: true` are not emitted, because no attribute
//          projects a property initializer into a JSON Schema `const`; the values are nonetheless
//          invariant at run time, which the sibling test project asserts against a live response.
//      Both were verified against a real generated document rather than assumed, and the contract side
//      is asserted by PowerFramework.Contracts.Tests. Projecting either would need a document-scoped
//      schema transformer, which is the composition root's registration and not an endpoint's to make.
//   7. No retCode is surfaced from this file. See the section above.
//   8. Every wire-visible string - the route, the operation id, the tag, the scheme id, the contract id
//      and both response descriptions - is a named constant taken from gateway.v1.yaml, so the contract
//      and this file cannot drift apart by an editing accident and a reviewer can diff them by name.
//
// TESTABILITY (constraint C-H: 80% line coverage per in-scope service)
//   Nothing here needs a live upstream, a database, a container or a real clock. PowerFramework.Gateway
//   .Tests hosts the application in process and asserts the whole published contract: 401 without an
//   Authorization header, 200 with a valid token and the contract's payload shape, 401 again under
//   ASPNETCORE_ENVIRONMENT=Development, and the generated document's `/v1/ping` operation against
//   OpenApi/gateway.v1.yaml.
//
// NO PERFORMANCE CLAIM IS MADE OR IMPLIED
//   The repository publishes no service-level agreement, no latency budget, no throughput target and no
//   availability commitment anywhere, so none may be asserted. The only quantitative non-functional
//   requirement in this refactor is the coverage gate above.
// ======================================================================================================

using System.Net.Mime;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

using PowerFramework.Gateway.Authorization;

namespace PowerFramework.Gateway.Endpoints;

/// <summary>
/// Maps <c>GET /v1/ping</c>, the authenticated liveness probe of contract C-10.
/// </summary>
/// <remarks>
/// <para>
/// The operation returns a trivial success only when a valid JWT bearer token is presented and
/// <c>401</c> when one is not. Both outcomes are the published contract, defined by
/// <c>shared/PowerFramework.Contracts/OpenApi/gateway.v1.yaml</c>, and both exist so that the
/// authenticated-boundary requirement is testable rather than merely asserted.
/// </para>
/// <para>
/// Gateway holds verification material only. It mints no token, holds no signing key and declares no
/// signing authority; the Security service is the sole issuer in the system. The authentication scheme
/// itself is registered by the composition root, not here - this type only requires authorization.
/// </para>
/// </remarks>
public static class PingEndpoints
{
    /// <summary>
    /// The route template, spelled exactly as the contract spells it.
    /// </summary>
    /// <remarks>
    /// One route and one method. No <c>/ping</c> alias, no trailing-slash variant, no <c>HEAD</c>
    /// companion and no sibling route: a consumer generating a client from the contract would find any
    /// of them undocumented, and an ingress with an undocumented surface is exactly what C-G forbids.
    /// </remarks>
    private const string RoutePattern = "/v1/ping";

    /// <summary>
    /// The scope an authenticated caller must have been granted to reach the authenticated liveness probe.
    /// </summary>
    /// <remarks>
    /// <para>
    /// DECLARED HERE, NEXT TO THE ROUTE THAT REQUIRES IT, and read by <c>Program.cs</c> when it builds
    /// the policy. A route and its entitlement are one decision; a scope name spelled independently in
    /// an authorization file and in a route file is the defect that enforces nothing while looking
    /// correct in both places.
    /// </para>
    /// <para>
    /// The spelling matches the grant the issuance roster hands the calling identity
    /// (<c>orchestration/.env.example</c>, <c>Security:Clients</c>), because Security refuses a scope it
    /// never granted rather than narrowing the request - so a mismatch here is not a smaller token, it
    /// is no token at all.
    /// </para>
    /// </remarks>
    internal const string RequiredScope = "ping";

    /// <summary>
    /// The contract's <c>operationId</c>. Also the endpoint name, which is what the OpenAPI document
    /// generator projects into <c>operationId</c>.
    /// </summary>
    private const string OperationId = "ping";

    /// <summary>
    /// The contract's tag for this operation.
    /// </summary>
    /// <remarks>
    /// Deliberately NOT the health tag, even though both operations belong to C-10: the two halves of
    /// that contract have opposite authentication postures - <c>/health</c> is anonymous and this route
    /// requires a token - and the contract separates them so a reader scanning the groupings sees that
    /// rather than having to infer it from one mixed group.
    /// </remarks>
    private const string DiagnosticsTag = "Diagnostics";

    /// <summary>
    /// The component name of the contract's bearer security scheme.
    /// </summary>
    private const string BearerSecuritySchemeId = "bearerAuth";

    /// <summary>
    /// The HTTP authentication scheme name the contract's security scheme declares.
    /// </summary>
    private const string BearerSchemeName = "bearer";

    /// <summary>
    /// The credential format the contract's security scheme declares.
    /// </summary>
    private const string BearerCredentialFormat = "JWT";

    /// <summary>
    /// The specification extension the contract carries on every operation to name its contract.
    /// </summary>
    private const string ContractIdExtensionName = "x-contract-id";

    /// <summary>
    /// The contract this operation belongs to: the health and readiness contract.
    /// </summary>
    private const string ContractId = "C-10";

    /// <summary>
    /// The status key of the success response in an OpenAPI responses map.
    /// </summary>
    private const string SuccessStatusKey = "200";

    /// <summary>
    /// The status key of the unauthorized response in an OpenAPI responses map.
    /// </summary>
    private const string UnauthorizedStatusKey = "401";

    /// <summary>
    /// The operation summary, verbatim from the contract.
    /// </summary>
    private const string OperationSummary =
        "The standing proof that this boundary is authenticated. Token required.";

    /// <summary>
    /// The success response description, verbatim from the contract.
    /// </summary>
    private const string SuccessResponseDescription = "A valid token was presented.";

    /// <summary>
    /// The unauthorized response description, verbatim from the contract's shared
    /// <c>Unauthorized</c> response component.
    /// </summary>
    private const string UnauthorizedResponseDescription =
        "No token was presented, or the token presented is expired, malformed, or not valid for this " +
        "service. This response is part of the published contract rather than an implementation " +
        "detail: it is the standing proof that the boundary is authenticated (C-G).";

    /// <summary>
    /// The operation description, carried across from the contract so that the generated document and
    /// the hand-authored one say the same thing to a consumer.
    /// </summary>
    private const string OperationDescription = """
        Returns a trivial success **only** when a valid token is presented, and `401` without one.

        This operation exists for one reason: to make the authenticated-boundary requirement
        **testable rather than merely asserted**. The legacy opens no listening socket and receives no
        unsolicited request, so decomposition creates every boundary in this system from nothing - and
        the requirement that each be authenticated needs a surface a conformance test can point at.

        It is present on **all four** services rather than only at the ingress, because an internal
        edge is a created boundary too. That uniformity is part of contract C-10.

        The body does not echo the presented token, any claim of it, or any part of the verification
        material.
        """;

    /// <summary>
    /// Registers <c>GET /v1/ping</c> together with its authorization requirement and the OpenAPI
    /// metadata that makes the generated document agree with
    /// <c>OpenApi/gateway.v1.yaml</c>.
    /// </summary>
    /// <param name="endpoints">The route builder the composition root is populating.</param>
    /// <returns>
    /// The same <paramref name="endpoints"/> instance, so the composition root can chain its endpoint
    /// registrations.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="endpoints"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// The route definition lives here rather than in the composition root, so that the whole of this
    /// operation's contract - its path, its method, its authorization requirement, its response surface
    /// and its published metadata - is reviewable in one place.
    /// </para>
    /// <para>
    /// <b>The return type is deliberately the route builder and not the route handler builder.</b>
    /// Returning the handler builder would let a caller append <c>AllowAnonymous</c>, which takes
    /// precedence over <c>RequireAuthorization</c> in endpoint metadata and would silently turn the
    /// system's authentication proof into an anonymous route from a different file. Withholding it makes
    /// that impossible.
    /// </para>
    /// </remarks>
    public static IEndpointRouteBuilder MapPingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet(RoutePattern, Ping)

            // C-G. The framework evaluates the named authorization policy and issues the challenge, so
            // the 401 is produced by framework code rather than by hand-rolled header inspection. This
            // call is UNCONDITIONAL: there is no environment test and no configuration switch guarding
            // it, in this file or anywhere else, because a bypass that exists only in Development is
            // still a bypass and would make the local build disagree with the published contract.
            //
            // IT NAMES A SCOPE, AND THIS IS THE ONLY PING PROBE IN THE SYSTEM THAT DOES. The three
            // internal services' probes require authentication and no scope, because no consumer names a
            // scope for them and their documents declare no 403 - inventing three names to look
            // symmetrical would be an invention. This probe is different on both counts: the end-to-end
            // suite requests `ping` by name as one of the surfaces it calls, and this is the INGRESS, so
            // the probe is reachable by an external client rather than only by a sibling service that
            // already holds every scope. The 401 for a caller presenting NO token is unchanged - the
            // policy requires an authenticated principal as well as the scope, which is what keeps the
            // absent-credential case a 401 rather than a 403.
            .RequireAuthorization(GatewayScopes.Ping)

            // The 403 the scope requirement can answer.
            .ProducesProblem(StatusCodes.Status403Forbidden)

            // Published metadata. WithName supplies the contract's operationId as well as the endpoint
            // name; the typed result below already declares the success payload, and the explicit
            // Produces call states it where a reviewer diffing against the contract will look for it.
            .WithName(OperationId)
            .WithTags(DiagnosticsTag)
            .WithSummary(OperationSummary)
            .WithDescription(OperationDescription)
            .Produces<PingResponse>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)
            .ProducesProblem(StatusCodes.Status401Unauthorized, MediaTypeNames.Application.ProblemJson)

            // AND THE 403 THE SCOPE POLICY ABOVE MAKES REACHABLE. Declared because it is a genuine
            // outcome of this operation: an authenticated caller whose issuance roster entry never granted
            // this scope is refused here. An undeclared response on the ingress is an undocumented
            // surface, which is what C-G forbids.
            .ProducesProblem(StatusCodes.Status403Forbidden, MediaTypeNames.Application.ProblemJson)

            // The parts of the contract that endpoint metadata alone cannot express: the specification
            // extension, the two response descriptions and the bearer security requirement.
            .AddOpenApiOperationTransformer(ApplyContractMetadataAsync);

        return endpoints;
    }

    /// <summary>
    /// Produces the success half of the authentication proof.
    /// </summary>
    /// <param name="httpContext">The current request, used only to resolve the clock.</param>
    /// <returns>
    /// <c>200</c> carrying a <see cref="PingResponse"/>. The handler has no failure path: it is only
    /// reachable once the authorization requirement declared in
    /// <see cref="MapPingEndpoints(IEndpointRouteBuilder)"/> has already been satisfied, so the
    /// unauthenticated outcome never reaches this method.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="httpContext"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// The body carries what the contract says and nothing more. No uptime counter, no build or version
    /// banner, no upstream roll-up, no echo of a request header and no invented correlation identifier:
    /// a liveness probe that grows a payload is scope creep, which constraint C-B forbids. It likewise
    /// carries no part of the presented token, no claim of it, and no part of the verification material
    /// or the configuration behind it, which constraint C-F requires.
    /// </para>
    /// <para>
    /// The clock is resolved from the request's services with a fallback to
    /// <see cref="TimeProvider.System"/>, which is the determinism seam: a test registers a fake
    /// <see cref="TimeProvider"/> and this method reads it, while a deployment that registers none still
    /// works. Taking <see cref="TimeProvider"/> as a handler parameter instead would be inferred as a
    /// request body on a GET and fail at startup wherever the type is not registered.
    /// </para>
    /// </remarks>
    private static Ok<PingResponse> Ping(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        TimeProvider clock = httpContext.RequestServices.GetService<TimeProvider>() ?? TimeProvider.System;

        return TypedResults.Ok(new PingResponse { Timestamp = clock.GetUtcNow() });
    }

    /// <summary>
    /// Applies the parts of the contract that endpoint metadata cannot express to the generated
    /// operation.
    /// </summary>
    /// <param name="operation">The operation the document generator produced for this endpoint.</param>
    /// <param name="context">The transformation context, which carries the document being built.</param>
    /// <param name="cancellationToken">
    /// Cancellation for the document generation this transformer participates in. The transformer is
    /// pure in-memory editing with no asynchronous work of its own, so it has nothing to observe the
    /// token with and completes synchronously.
    /// </param>
    /// <returns>A completed task.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="operation"/> or <paramref name="context"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// Endpoint-scoped, so it can never affect another operation, and applied through
    /// <c>AddOpenApiOperationTransformer</c> rather than <c>WithOpenApi</c> because the latter is
    /// documented as not integrating with the built-in document generation this service uses.
    /// </remarks>
    private static Task ApplyContractMetadataAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(context);

        // Stated explicitly as well as through WithName. The endpoint name and the operation id are two
        // different concepts that happen to share a value here, and the contract pins the operation id.
        operation.OperationId = OperationId;

        ApplyContractIdExtension(operation);
        ApplyResponseDescriptions(operation);
        ApplyBearerSecurityRequirement(operation, context.Document);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Carries the contract's <c>x-contract-id</c> specification extension onto the operation.
    /// </summary>
    /// <param name="operation">The operation to annotate.</param>
    /// <remarks>
    /// RFC-permitted specification extensions are how the contract records which of its ten contracts an
    /// operation belongs to, and an operation that omits it is harder to audit against the inventory in
    /// <c>docs/CONTRACTS.md</c>.
    /// </remarks>
    private static void ApplyContractIdExtension(OpenApiOperation operation)
    {
        operation.Extensions ??= new Dictionary<string, IOpenApiExtension>(StringComparer.Ordinal);
        operation.Extensions[ContractIdExtensionName] = new JsonNodeExtension(ContractId);
    }

    /// <summary>
    /// Replaces the generator's default response descriptions with the contract's own wording.
    /// </summary>
    /// <param name="operation">The operation whose responses are being described.</param>
    /// <remarks>
    /// <para>
    /// The generator derives a description from the status code's reason phrase, which is accurate but
    /// says nothing. The contract's wording is what a consumer needs, and the <c>401</c> description in
    /// particular records that the response is part of the published contract rather than an
    /// implementation detail.
    /// </para>
    /// <para>
    /// Each assignment is guarded: a response the generator did not produce is left alone rather than
    /// created here, because inventing a response would publish a status this endpoint cannot return.
    /// The cast is required because the responses map is typed by the read-only response interface while
    /// the description is settable on the concrete type.
    /// </para>
    /// </remarks>
    private static void ApplyResponseDescriptions(OpenApiOperation operation)
    {
        if (operation.Responses is null)
        {
            return;
        }

        if (operation.Responses.TryGetValue(SuccessStatusKey, out IOpenApiResponse? success)
            && success is OpenApiResponse successResponse)
        {
            successResponse.Description = SuccessResponseDescription;
        }

        if (operation.Responses.TryGetValue(UnauthorizedStatusKey, out IOpenApiResponse? unauthorized)
            && unauthorized is OpenApiResponse unauthorizedResponse)
        {
            unauthorizedResponse.Description = UnauthorizedResponseDescription;
        }
    }

    /// <summary>
    /// Declares that this operation requires the contract's bearer security scheme.
    /// </summary>
    /// <param name="operation">The operation to constrain.</param>
    /// <param name="document">
    /// The document being built, used to resolve the scheme reference and, if necessary, to make it
    /// resolvable. May be <see langword="null"/> when a generator invokes the transformer without one.
    /// </param>
    /// <remarks>
    /// <para>
    /// The requirement names one scheme with an empty scope list, which is what the specification
    /// requires for an HTTP scheme: scopes belong to OAuth 2.0 and OpenID Connect schemes only.
    /// </para>
    /// <para>
    /// The requirement is added rather than assigned over any existing one, so a document-level default
    /// or another convention contributing the same scheme cannot be silently discarded here. The
    /// requirement's own equality contract compares reference identifiers, so re-adding the same scheme
    /// cannot produce a contradictory duplicate within one requirement object.
    /// </para>
    /// </remarks>
    private static void ApplyBearerSecurityRequirement(OpenApiOperation operation, OpenApiDocument? document)
    {
        if (document is not null)
        {
            EnsureBearerSecurityScheme(document);
        }

        OpenApiSecurityRequirement requirement = new()
        {
            [new OpenApiSecuritySchemeReference(BearerSecuritySchemeId, document)] = [],
        };

        operation.Security ??= [];
        operation.Security.Add(requirement);
    }

    /// <summary>
    /// Makes sure the document carries a <c>bearerAuth</c> security scheme, so that the requirement
    /// applied by <see cref="ApplyBearerSecurityRequirement"/> cannot reference a component that does
    /// not exist.
    /// </summary>
    /// <param name="document">The document being built.</param>
    /// <remarks>
    /// <para>
    /// A guard, not a registration. The composition root publishes the scheme together with the full
    /// description the contract carries, and this method is then a no-op; it only fills the gap in a
    /// document that has an operation-level requirement and no matching component, which is an invalid
    /// document that no generated client could satisfy.
    /// </para>
    /// <para>
    /// The scheme it would add is the contract's own: HTTP authentication, the <c>bearer</c> scheme,
    /// credentials formatted as a JWT. It describes how a caller presents a credential and configures no
    /// authentication of any kind - the authentication handler and its verification material remain the
    /// composition root's concern.
    /// </para>
    /// <para>
    /// <b>One note for the composition root, verified against a real generated document rather than
    /// assumed.</b> Document transformers run after the operations are built, so a
    /// composition root publishing the full scheme sees whatever this guard left behind and overwrites
    /// it - which is the intended outcome and is asserted by the sibling test project. Write that scheme
    /// with the indexer or by replacing the dictionary, <b>not</b> with an add that rejects a duplicate
    /// key, because the key may already be present for exactly this reason.
    /// </para>
    /// </remarks>
    private static void EnsureBearerSecurityScheme(OpenApiDocument document)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??=
            new Dictionary<string, IOpenApiSecurityScheme>(StringComparer.Ordinal);

        if (document.Components.SecuritySchemes.ContainsKey(BearerSecuritySchemeId))
        {
            return;
        }

        document.Components.SecuritySchemes[BearerSecuritySchemeId] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = BearerSchemeName,
            BearerFormat = BearerCredentialFormat,
        };
    }
}

/// <summary>
/// The successful half of the authentication proof: the body of <c>200 GET /v1/ping</c>.
/// </summary>
/// <remarks>
/// <para>
/// Mirrors the contract's <c>PingResponse</c> schema member for member. It is returned only when a valid
/// token was presented; the absence of a token yields <c>401</c>, and both outcomes are contract C-10.
/// </para>
/// <para>
/// The body deliberately does not echo the presented token, any claim of it, or any part of the
/// verification material, and it carries no configuration value, no upstream address and no environment
/// value of any kind. The type is sealed and has exactly three members, so it cannot acquire one by
/// accident.
/// </para>
/// <para>
/// Declared in this file rather than in a file of its own: this folder permits exactly five files, and
/// the shape is small enough that inlining it keeps the whole operation reviewable in one place.
/// </para>
/// </remarks>
public sealed record PingResponse
{
    /// <summary>
    /// The value the contract pins for the responding service.
    /// </summary>
    private const string RespondingService = "gateway";

    /// <summary>
    /// The responding service, which the contract pins to a constant so a consumer can tell which
    /// listener answered.
    /// </summary>
    /// <remarks>
    /// Each of the four in-scope services publishes this same operation with its own constant, which is
    /// what makes C-10's uniformity checkable from a response rather than only from a status code.
    /// </remarks>
    [JsonPropertyName("service")]
    [JsonRequired]
    public string Service { get; init; } = RespondingService;

    /// <summary>
    /// Always <see langword="true"/> when this body is returned, because the operation cannot be
    /// reached without a valid token.
    /// </summary>
    /// <remarks>
    /// Present so that a conformance test asserting the authenticated path has something explicit to
    /// assert on rather than inferring success from a status code.
    /// </remarks>
    [JsonPropertyName("authenticated")]
    [JsonRequired]
    public bool Authenticated { get; init; } = true;

    /// <summary>
    /// When the response was produced.
    /// </summary>
    /// <remarks>
    /// Read from the injected <see cref="TimeProvider"/> rather than from the ambient system clock, so a
    /// test can substitute a deterministic clock and assert an exact value. Optional in the contract and
    /// therefore not marked required here, even though this implementation always populates it.
    /// </remarks>
    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; init; }
}
