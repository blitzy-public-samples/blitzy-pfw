// ==================================================================================================
//  PingEndpoints - GET /v1/ping ON THE SECURITY SERVICE, TOKEN REQUIRED
//  AND the single owner of this folder's application/problem+json shape and its legacy
//  retCode-to-HTTP-status map.
//  ------------------------------------------------------------------------------------------------
//  SUBJECT
//  The standing proof that this boundary is authenticated. The route exists on ALL FOUR services
//  rather than only at the ingress, because an internal edge is a created boundary too: the legacy
//  framework was an in-process library that opened no listening socket, registered no route and
//  received no unsolicited request, so every boundary in this system was created by the
//  decomposition itself. "No new attack surface" therefore means every newly created surface is
//  authenticated from the outset (constraint C-G), and this route is where that claim becomes
//  testable rather than asserted.
//
//  BOTH OUTCOMES ARE THE CONTRACT
//  200 with a valid token, and 401 WITHOUT ONE. The second half is the half that demonstrates the
//  boundary is actually closed, so it is a declared response rather than an implementation detail.
//
//  WHY THE SHARED ERROR HELPER LIVES IN THIS FILE - AN ARCHITECTURAL DECISION, ALREADY SETTLED
//  ------------------------------------------------------------------------------------------------
//  This folder is capped at exactly FIVE files - HealthEndpoints, PingEndpoints, JwksEndpoints,
//  TokenEndpoints and CryptoEndpoints - so a sixth file holding the problem factory is forbidden.
//  Four of the five need the problem+json + retCode shape, and HealthEndpoints must stay free of the
//  return-code algebra to remain cheap on the anonymous readiness path. This is the canonical
//  authenticated-endpoint file and it is authored before Jwks, Token and Crypto, so the shared
//  helper is declared HERE, once, as `internal static ProblemResults`, and those three consume it.
//
//  It is `internal` rather than `public` on purpose: it is a within-service composition detail, not
//  part of the published surface. The project file grants InternalsVisibleTo to
//  PowerFramework.Security.Tests, which is what makes the status map directly unit-testable as a
//  table-driven theory rather than only reachable through a booted host (constraint C-H).
//
//  Do not move it into a new file, and do not duplicate it into a sibling.
//
//  THE AUTHORITATIVE WIRE DOCUMENT, AND THE THREE PLACES THIS FILE OBEYS IT
//  ------------------------------------------------------------------------------------------------
//  shared/PowerFramework.Contracts/OpenApi/security.v1.yaml is authoritative for anything on the
//  wire; where it and docs/CONTRACTS.md disagree, the YAML wins. It is packaged as content and is
//  NOT compiled, so there is no generated C# type for this surface: the response shape below is
//  hand-authored and conforms to the document by review plus the assertions in the sibling test
//  project. Three consequences are visible in the code and are called out where they land:
//
//    1. The operation is tagged Health, not Diagnostics, because the document tags it Health and
//       groups it with /health under contract C-10.
//    2. PingResponse carries NO retCode member, because the document sets additionalProperties
//       FALSE on that schema and declares only service, authenticated and timestamp. The sibling
//       DataServices REST projection does carry one and justifies it by having no published schema;
//       this service HAS one, so the estate diverges here deliberately rather than by drift.
//    3. The 401 is declared with the application/problem+json media type, because the document
//       resolves it to the shared ProblemDetails response.
//
//  WHAT THE 200 BODY MAY NOT CARRY (constraints C-F and C-G)
//  ------------------------------------------------------------------------------------------------
//  No part of the presented token, no claim of it, no key, no key identifier, no issuer, no
//  audience, no algorithm name and no part of the verification material or the configuration behind
//  it. The published schema settles the question by construction: it closes the object, so there is
//  no member a claim could be echoed into. A liveness signal that grew a payload would also be
//  scope creep, which constraint C-B forbids.
//
//  THE 401 IS NOT WRITTEN BY THIS FILE, AND THAT WAS MEASURED RATHER THAN ASSUMED
//  ------------------------------------------------------------------------------------------------
//  An in-process probe of this host through WebApplicationFactory<Program> confirms that an
//  unauthenticated GET /v1/ping answers 401 with an EMPTY body, no Content-Type, and
//  `WWW-Authenticate: Bearer`. The challenge is written by the authentication middleware before the
//  route runs, so the handler below never sees the request. Two things follow, and both are
//  deliberate:
//
//    * The handler does NOT hand-craft a 401. It could not: it is never reached.
//    * The 401 is nonetheless DECLARED in the OpenAPI metadata, with the problem media type, so the
//      published description of this operation is complete. Declaring the response is not the same
//      as inventing a body for it, and nothing here asserts a shape the running service does not
//      produce.
//
//  THIS SERVICE PRESENTS TOKENS TO ITSELF LIKE ANY OTHER CALLER
//  ------------------------------------------------------------------------------------------------
//  Security mints the system's tokens and is nonetheless subject to the same rule on its own
//  surface: this route requires a valid token and grants no exemption to the issuer. There is no
//  development bypass, no dev-token mode and no relaxed validation branch anywhere in this file - a
//  bypass that exists only in Development is still a bypass, and on the service holding the system's
//  single signing secret it is the least acceptable place for one.
//
//  CONSTRAINT ROLL-CALL FOR THIS FILE
//  ------------------------------------------------------------------------------------------------
//  C-A  Nothing here reaches into another service. There is no HTTP client, no remote-procedure
//       client and no reference to any sibling service: Security is called by them and calls none of
//       them, so /v1/ping probes only this process. The project file enforces it structurally by
//       referencing shared libraries alone.
//  C-B  No rate limiter, response cache, cross-origin policy, telemetry exporter or validation
//       framework. None was asked for, and the deliberately-excluded-packages list forbids adding
//       one.
//  C-C  The read-only legacy tree appears ONLY as provenance locators in comments and XML
//       documentation - ws_objects/pfw.shared.pbl.src/retcode.sru and the six predicate functions
//       beside it. Nothing there is opened, read, copied or fixture-loaded at run time, and nothing
//       there is edited, moved or reformatted.
//  C-D  No deferred capability is implemented, stubbed or named, and no route belonging to one is
//       declared. The trap specific to a STATUS MAP is discharged at the map itself: the
//       not-implemented HTTP status is never produced anywhere in this file. See MapStatusCode.
//  C-E  No database context, no connection string and no storage probe. The probe answers from
//       process state alone.
//  C-F  No key, certificate, private-key block, password, token or initialization-vector literal
//       appears in code, in a default, in XML documentation or in an OpenAPI description. No
//       response body produced here echoes a bearer token, a signing key, a key identifier or any
//       resolved key material.
//  C-G  RequireAuthorization is called EXPLICITLY on the route rather than left to the host's
//       default-deny fallback, so the requirement is legible at the route and survives a change to
//       that fallback. AllowAnonymous appears nowhere in this file and must never be added.
//  C-H  Every piece of logic is a NAMED, directly-callable method rather than a logic-bearing inline
//       lambda, so the sibling test project can drive the status map as a theory and the route
//       through a booted host.
//  C-K  Every non-obvious status choice is documented AT the arm that makes it, and the map records
//       that it is deliberate rather than derived.
//  .editorconfig  This file is NOT in the naming-suppression scope, and warnings are errors, so it
//       DECLARES no preserved SCREAMING_SNAKE identifier. It only ever REFERENCES RetCode members,
//       which the naming analyzers do not report.
// ==================================================================================================

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using PowerFramework.Shared.Kernel;
using PowerFramework.Security.Authorization;

namespace PowerFramework.Security.Endpoints;

/// <summary>
/// Declares and serves this service's authenticated liveness probe.
/// </summary>
/// <remarks>
/// This file authorises the route; it does not authenticate it. Registering the authentication
/// scheme, the authorization services and the bearer options is <c>Program.cs</c>'s responsibility
/// and deliberately not this file's.
/// </remarks>
public static class PingEndpoints
{
    /// <summary>This service's canonical name, exactly as the published schema fixes it.</summary>
    private const string ServiceName = "security";

    /// <summary>
    /// The route, taken from the published document's <c>/v1/ping</c> path.
    /// </summary>
    /// <remarks>
    /// Held as a local constant rather than read from configuration. <c>SecurityOptions</c> declares
    /// a member for every path it owns - the token endpoint, the key set and the discovery document -
    /// and deliberately declares none for this one, so there is no options value to prefer over a
    /// literal here. Making the probe path configurable would also let a deployment move the one
    /// route that proves the boundary is closed, which is the opposite of what it exists for.
    /// </remarks>
    private const string RoutePattern = "/v1/ping";

    /// <summary>
    /// The endpoint name, which the document generator also publishes as the operation identifier.
    /// </summary>
    /// <remarks>Matches the <c>operationId</c> the authored contract declares for this path.</remarks>
    private const string EndpointName = "ping";

    /// <summary>The tag the operation is grouped under.</summary>
    /// <remarks>
    /// <c>Health</c>, because the authored contract tags this path <c>Health</c> and groups it with
    /// <c>/health</c> under one contract - readiness reporting and the standing proof that the
    /// boundary is authenticated are two halves of the same published concern. The sibling
    /// <c>HealthEndpoints</c> uses the same tag, so the two routes land together in the generated
    /// document rather than in two groups of one.
    /// </remarks>
    /// <summary>The scope a caller must hold to reach the authenticated probe.</summary>
    /// <remarks>
    /// <para>
    /// EVIDENCED BY THE ONE CALLER THAT PROBES THIS ROUTE. The end-to-end suite's declared minimum
    /// scope set names it first, alongside the two Gateway surfaces it exercises
    /// [tests/e2e/fixtures/auth.ts:L256-L260], and the name mirrors the path segment it authorises -
    /// the only naming convention the repository evidences for a route-shaped scope. A different name
    /// chosen here would have refused the only caller that probes this route.
    /// </para>
    /// <para>
    /// EVERY SERVICE'S PROBE USES THE SAME SCOPE NAME, deliberately: the probe means the same thing on
    /// all four, a caller that may prove one boundary is authenticated may prove any, and a per-service
    /// spelling would make the suite hold four scopes to make one assertion four times.
    /// </para>
    /// </remarks>
    internal const string RequiredScope = "ping";

    /// <summary>The authorization-policy name this route is gated by.</summary>
    /// <remarks>
    /// Declared here and consumed by the composition root, so the route and its requirement have ONE
    /// spelling. Composed from <see cref="RequiredScope"/> rather than written out, so the two cannot
    /// drift: a policy registered under a name no route requires enforces nothing and looks correct.
    /// </remarks>
    internal static string ScopePolicyName => SecurityScopes.PolicyNameFor(RequiredScope);

    private const string TagName = "Health";

    /// <summary>
    /// The security-scheme key the generated document declares the bearer requirement under.
    /// </summary>
    private const string BearerSchemeName = "bearerAuth";

    /// <summary>The summary published for the operation, matching the authored contract.</summary>
    private const string OperationSummary =
        "The standing proof that this boundary is authenticated. Token required.";

    /// <summary>The description published for the operation.</summary>
    /// <remarks>
    /// Authored as a constant rather than inline so the published prose is reviewable in one place
    /// against the contract document it must agree with.
    /// </remarks>
    private const string OperationDescription =
        "Returns a trivial success only when a valid token is presented, and 401 without one. Both "
        + "outcomes are part of the published health-and-readiness contract rather than "
        + "implementation detail: the operation exists so that the authenticated-boundary "
        + "requirement is testable rather than merely asserted, and it is present on all four "
        + "services rather than only at the ingress because an internal edge is a created boundary "
        + "too. Security grants itself no exemption on its own surface. The response body "
        + "deliberately does not echo the presented token, any claim of it, any key, key identifier, "
        + "issuer, audience or algorithm name, or any part of the verification material.";

    /// <summary>
    /// Maps the authenticated liveness probe.
    /// </summary>
    /// <param name="endpoints">The route builder to declare the route on.</param>
    /// <returns>The same <paramref name="endpoints"/> instance, so that mapping calls compose.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="endpoints"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// The single public entry point of this file, matching the one-registration-method-per-endpoint
    /// -file shape the whole folder uses and that <c>Program.cs</c> calls once per file.
    /// </remarks>
    public static IEndpointRouteBuilder MapPingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet(RoutePattern, Ping)
            // --------------------------------------------------------------------------------------
            // CONSTRAINT C-G, AND THE ONE LINE THIS WHOLE ROUTE EXISTS FOR.
            //
            // Unconditional. Not wrapped in an environment test, not paired with an anonymous
            // fallback, and not weakened by a configuration switch. An unauthenticated request
            // reaches the authorization middleware, is challenged by the bearer handler registered
            // in Program.cs, and receives 401 - in Development exactly as in production.
            //
            // Called EXPLICITLY rather than relying on the host's default-deny fallback policy. The
            // fallback would produce the same answer today, but a requirement satisfied by OMISSION
            // is invisible at the route and would evaporate silently if that policy were ever
            // relaxed. Stating it here makes the guard local to the thing it guards.
            //
            // A NAMED SCOPE POLICY RATHER THAN THE PARAMETERLESS FORM. The parameterless form
            // requires only an AUTHENTICATED principal, which every token this system mints
            // satisfies - so the route proved that the boundary is authenticated and nothing about
            // what the caller is entitled to. The policy below requires the scope named in this file
            // IN ADDITION, so a token whose caller was never granted it is refused with 403.
            //
            // THIS DOES NOT WEAKEN THE 401 THE ATTACHED ENVIRONMENT DOCUMENTS (C-L). A request with
            // no token is still challenged by the bearer handler and still receives 401, because
            // authentication precedes authorization; the scope check can only ever turn an
            // AUTHENTICATED request into a 403, which is a strictly narrower outcome than the
            // documented one and is a declared response of this operation.
            //
            // The scope name is declared IN THIS FILE and consumed by Program.cs when it builds the
            // policy. A route and its requirement are one decision, and a policy name spelled
            // independently in two files is the defect that enforces nothing while looking correct.
            // --------------------------------------------------------------------------------------
            .RequireAuthorization(ScopePolicyName)
            .WithName(EndpointName)
            .WithTags(TagName)
            .WithSummary(OperationSummary)
            .WithDescription(OperationDescription)
            .Produces<PingResponse>(StatusCodes.Status200OK)
            // The 401 is a DECLARED response, not an implementation detail: it is half of what this
            // operation promises, and the authored contract resolves it to the shared problem shape.
            // ProducesProblem is used rather than a bare Produces so the published media type is
            // application/problem+json, which is what that contract says a consumer will find there.
            //
            // NO RESPONSE TYPE OF THIS FILE'S OWN ACCOMPANIES IT. The challenge is produced by the
            // authentication middleware before the route runs - measured, not assumed; see the file
            // header - so this declaration describes the contract rather than a body this file
            // writes.
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            // THE SCOPE REFUSAL, DECLARED BECAUSE THE POLICY ABOVE MAKES IT REACHABLE. A token that is
            // valid, addressed to this service and unexpired is still refused here when its caller was
            // never granted this route's scope, so a document declaring only 401 would under-declare a
            // status the service produces. It does NOT replace the 401: an anonymous request is still
            // challenged and still answered 401, because authentication precedes authorization - the
            // scope check can only narrow an already-authenticated request (C-L).
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .AddOpenApiOperationTransformer(DeclareBearerRequirementAsync);

        return endpoints;
    }

    /// <summary>
    /// Produces the successful half of the authentication proof.
    /// </summary>
    /// <param name="httpContext">The current request context.</param>
    /// <returns>200 carrying the liveness body.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="httpContext"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// A NAMED METHOD RATHER THAN AN INLINE LAMBDA, and <c>internal</c> rather than <c>private</c>, so
    /// that the sibling test project can call it DIRECTLY as well as through a booted host - which is
    /// what constraint C-H asks of the handler. It takes one parameter and touches no other
    /// collaborator, so a direct call needs nothing but a bare context; the request-services lookup
    /// below tolerates a context that has none precisely so that call works. <c>internal</c> keeps it
    /// off the published surface, and the project file grants the test project access.
    /// </para>
    /// <para>
    /// The handler is only ever reached with an authenticated principal, because the route requires
    /// authorization. It therefore does not test the principal, does not read a claim from it and
    /// does not report anything about it: <c>Authenticated</c> is unconditionally
    /// <see langword="true"/> precisely because reaching this line already proves it. Inspecting the
    /// principal instead would turn a liveness signal into a token-introspection endpoint - which on
    /// the issuing service would be an especially unfortunate thing to publish.
    /// </para>
    /// <para>
    /// It performs no cryptographic work, resolves no key reference, opens no storage and makes no
    /// outbound call, so the answer is a statement about this process and nothing else (constraints
    /// C-A and C-E).
    /// </para>
    /// <para>
    /// Nothing is logged on this path. The probe is called on a schedule by a readiness gate, so a
    /// log record per request would be pure noise, and there is nothing about the principal that
    /// could be recorded safely in any case.
    /// </para>
    /// <para>
    /// The clock is resolved from request services when one is registered and falls back to the
    /// system clock otherwise, so this handler imposes no registration requirement on
    /// <c>Program.cs</c> while remaining substitutable in a test. That seam matters on this service
    /// in particular: the cryptographic surface's random and identifier generators and this
    /// timestamp are the primary non-determinism sources in the whole estate, and characterization
    /// requires each to be maskable from both the master and the candidate recording.
    /// </para>
    /// </remarks>
    internal static Ok<PingResponse> Ping(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        TimeProvider clock =
            httpContext.RequestServices?.GetService<TimeProvider>() ?? TimeProvider.System;

        PingResponse body = new(Service: ServiceName, Authenticated: true)
        {
            Timestamp = clock.GetUtcNow(),
        };

        return TypedResults.Ok(body);
    }

    /// <summary>
    /// Declares the bearer security requirement on this operation in the generated OpenAPI document,
    /// registering the scheme itself when the document does not already carry it.
    /// </summary>
    /// <param name="operation">The operation being described.</param>
    /// <param name="context">The transformer context, exposing the document being built.</param>
    /// <param name="cancellationToken">Cancels document generation.</param>
    /// <returns>A completed task; the transformation is synchronous.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="operation"/> or <paramref name="context"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// A per-endpoint operation transformer is the sanctioned mechanism for this on .NET 10. The
    /// older per-endpoint OpenAPI configuration extension is deprecated on this toolchain and raises
    /// a deprecation diagnostic, which the repository-wide warnings-as-errors setting turns into a
    /// build failure, so it is not an option here even as a fallback.
    /// </para>
    /// <para>
    /// The generator does not synthesise a security requirement from authorization metadata by
    /// itself, so requiring a token without declaring it would leave the published document claiming
    /// an anonymous operation while the running service answers 401. Declaring it here keeps the
    /// document honest about the one property this operation exists to prove.
    /// </para>
    /// <para>
    /// Both steps are idempotent, so this composes with whatever document-wide security the host
    /// registers rather than fighting it: the scheme is added only when absent, and the requirement
    /// only when the operation carries none.
    /// </para>
    /// <para>
    /// NO KEY MATERIAL, KEY-SET ADDRESS OR DISCOVERY ADDRESS APPEARS IN THE DECLARATION. It states
    /// the transport scheme and the token format, which is all a consumer needs in order to point
    /// its own stock bearer handler at the issuer named in that consumer's own configuration.
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
                    "A JSON Web Token issued by this service, presented in the Authorization "
                    + "header. Security is the sole issuer in the system, and it validates tokens "
                    + "on its own surface with the same stock bearer handler the other three "
                    + "services use.",
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
}

/// <summary>
/// The successful half of the authentication proof, returned only when a valid token was presented.
/// </summary>
/// <param name="Service">
/// The responding service's canonical name. Always this service's own name, which the published
/// schema fixes as a constant value rather than leaving free.
/// </param>
/// <param name="Authenticated">
/// Always <see langword="true"/> when this body is returned, because the operation cannot be reached
/// without a valid token. Present so a conformance test asserting the authenticated path has
/// something explicit to assert on rather than inferring success from a status code.
/// </param>
/// <remarks>
/// <para>
/// MEMBER FOR MEMBER THE PUBLISHED SCHEMA, AND NOTHING MORE. The authored contract closes this object
/// with <c>additionalProperties: false</c> and declares exactly <c>service</c>, <c>authenticated</c>
/// and <c>timestamp</c>. A fourth member - a legacy return code, a subject, an audience, a key
/// identifier, an issued-at claim - would be refused by a consumer validating against that schema,
/// so THERE IS DELIBERATELY NO PLACE HERE FOR ONE. That is also what discharges constraint C-F on
/// this response by construction rather than by review: a closed object cannot echo a claim, a token
/// or any part of the verification material, because it has no member to echo one into.
/// </para>
/// <para>
/// The sibling DataServices projection of this route DOES carry a legacy return code in its body and
/// justifies it on the grounds that no published schema constrains that projection. This service has
/// one, so the two shapes differ on purpose. The divergence is recorded here and in the file header
/// so that a future reader aligning the estate does not "restore" a member this contract forbids.
/// </para>
/// <para>
/// Declared at file scope rather than nested inside <see cref="PingEndpoints"/>, matching the sibling
/// <c>HealthEndpoints</c> in this folder, so the generated schema is named for the shape rather than
/// for the class that happens to return it.
/// </para>
/// <para>
/// ONE PROPERTY OF THE PUBLISHED SCHEMA THE GENERATOR CANNOT RESTATE, recorded so the difference is
/// known rather than discovered. The generator emits no <c>additionalProperties</c> keyword for any
/// type in this service, so the generated document does not repeat the authored contract's closure of
/// this object. The BEHAVIOUR is closed regardless, because the type has exactly these three members
/// and no extension-data member, so no fourth member can appear in a response. The authored document
/// remains the place the closure is stated, which is precisely why it is the authoritative one.
/// </para>
/// </remarks>
public sealed record PingResponse(
    string Service,
    bool Authenticated)
{
    /// <summary>
    /// The instant the response was produced, so a consumer can detect a stale cached response.
    /// </summary>
    /// <remarks>
    /// An init-only PROPERTY rather than a third positional parameter, deliberately, and for exactly
    /// the reason the sibling <c>HealthEndpoints</c> gives for the same choice: a constructor
    /// parameter without a default is emitted as REQUIRED in the generated document, while the
    /// authored contract lists only <c>service</c> and <c>authenticated</c> as required and leaves
    /// this member optional. Declaring it here keeps the generated <c>required</c> list identical to
    /// the authored one instead of silently tightening the published contract.
    /// </remarks>
    public DateTimeOffset Timestamp { get; init; }
}

/// <summary>
/// How severe the legacy framework considered a condition, preserved from the dialog it used to be
/// delivered through.
/// </summary>
/// <remarks>
/// <para>
/// The legacy framework reported these conditions with <c>MessageBox</c> and <c>MessageBoxEx</c>
/// dialogs, whose third argument is an icon that carries the severity. ONLY THE DELIVERY CHANNEL
/// CHANGES in this refactor: a structured result replaces the dialog, and the text, the localization
/// category and this severity all survive the move unchanged. The member names are therefore the
/// legacy icon vocabulary rather than a new one invented for the wire, so a reader comparing a
/// recorded .NET result against the legacy call site sees the same word in both.
/// </para>
/// <para>
/// The names are ordinary PascalCase and carry no underscore, so no naming diagnostic can fire on
/// them and this file needs no suppression entry - which matters, because it has none and warnings
/// are errors here.
/// </para>
/// <para>
/// This is NOT a severity scale to branch on. It is preserved information about how the legacy
/// presented a condition, and the HTTP status is chosen from the return code instead - see
/// <see cref="ProblemResults.MapStatusCode(long?)"/>.
/// </para>
/// </remarks>
internal enum ProblemSeverity
{
    /// <summary>
    /// The legacy call site displayed no icon, so there is no severity to preserve.
    /// </summary>
    /// <remarks>
    /// Zero-valued so that the default value of the enumeration is the one that claims nothing,
    /// rather than one that asserts a severity the legacy never stated.
    /// </remarks>
    None = 0,

    /// <summary>Informational: the legacy displayed an information icon.</summary>
    Information = 1,

    /// <summary>Interrogative: the legacy displayed a question icon.</summary>
    Question = 2,

    /// <summary>Warning: the legacy displayed an exclamation icon.</summary>
    Exclamation = 3,

    /// <summary>Error: the legacy displayed a stop-sign icon.</summary>
    StopSign = 4,
}


/// <summary>
/// This folder's ONE <c>application/problem+json</c> factory and its explicit legacy return code to
/// HTTP status map.
/// </summary>
/// <remarks>
/// <para>
/// DECLARED IN <c>PingEndpoints.cs</c> BY DESIGN, NOT BY ACCIDENT. The folder is capped at five
/// files, so a separate file for this type is forbidden; four of the five need this shape, and the
/// anonymous readiness probe in <c>HealthEndpoints</c> is deliberately kept free of the return-code
/// algebra. <c>JwksEndpoints</c>, <c>TokenEndpoints</c> and <c>CryptoEndpoints</c> all consume this
/// type. None of them may declare a second error shape, and none may copy this one.
/// </para>
/// <para>
/// HOW A SIBLING ADDS ITS OWN DELIBERATE PER-CODE ARM. The three consumers each have conditions this
/// shared map cannot see. Where a sibling's semantics differ from the shared default, it passes
/// <c>statusCode</c> to <see cref="Create"/> explicitly, and the caller's deliberate choice wins over
/// the map. Two arms already exist specifically to be overridden that way, and both are documented at
/// <see cref="MapStatusCode(long?)"/>: the authored contract uses <c>E_ACCESS_DENIED</c> for BOTH its
/// 401 and its 403 response, so the code alone cannot pick between them; and a key-reference miss and
/// a genuinely absent object share <c>E_OBJECT_NOT_FOUND</c>. What a sibling must NOT do is derive a
/// status from a truthiness test on the code - see the tri-state note below - or introduce a second
/// problem schema of its own.
/// </para>
/// <para>
/// ONLY THE DELIVERY CHANNEL CHANGES. The legacy framework reported these conditions through
/// <c>MessageBox</c> and <c>MessageBoxEx</c> dialogs. Every dialog argument survives the move to a
/// structured result: the message text becomes the problem <c>detail</c>, the localization category
/// becomes an extension member, and the icon becomes <see cref="ProblemSeverity"/>. Nothing is
/// reworded, reclassified or dropped, and no new information is added.
/// </para>
/// <para>
/// THE ALGEBRA IS TRI-STATE AND HOLED, SO NO STATUS IS EVER DERIVED FROM A TRUTHINESS TEST. Measured
/// from the read-only oracle: <c>IsSucceeded</c> is <c>rtCode &gt;= RetCode.OK</c>
/// [<c>ws_objects/pfw.shared.pbl.src/issucceeded.srf:L11-L13</c>] and <c>PREVENT</c> is 1
/// [<c>retcode.sru:L42</c>], SO A PREVENTION READS AS A SUCCESS; <c>IsFailed</c> is
/// <c>rtCode &lt; RetCode.OK and rtCode &lt;&gt; RetCode.CANCELLED</c>
/// [<c>isfailed.srf:L11-L13</c>] and <c>CANCELED</c>/<c>CANCELLED</c> are two spellings of -2
/// [<c>retcode.sru:L44-L45</c>], SO CANCELLED IS NEITHER SUCCEEDED NOR FAILED; both predicates answer
/// false for <see langword="null"/>, SO NULL IS LIKEWISE NEITHER; and the boolean overloads of
/// <c>isfailed</c> and <c>isprevented</c> are textually identical negations
/// [<c>isfailed.srf:L15-L17</c>, <c>isprevented.srf:L15-L17</c>], so prevent and failed are
/// indistinguishable in boolean form while remaining cleanly distinct numerically. Two further
/// asymmetries matter to anyone extending this type: <c>iscancelled.srf:L9</c> has no null guard at
/// all, and <c>isallowed.srf:L11</c> is the only predicate that treats null as PERMISSIVE and that
/// additionally allows anything above 1000.
/// </para>
/// <para>
/// Consequently the predicates in <see cref="PowerFramework.Shared.Kernel.Predicates"/> are consumed
/// here - rather than having their comparisons re-derived, which would fork the algebra - but ONLY
/// through <see cref="ClaimsSuccess(long?)"/> and <see cref="IsIndeterminate(long?)"/>, and neither
/// of those chooses a status. The status comes from <see cref="MapStatusCode(long?)"/>, which is an
/// explicit per-code map, authored arm by arm.
/// </para>
/// <para>
/// The type is <c>internal</c> because it is a within-service composition detail rather than part of
/// the published surface, and the project file grants <c>InternalsVisibleTo</c> to
/// <c>PowerFramework.Security.Tests</c>, so every member below is directly unit-testable as a
/// table-driven theory (constraint C-H).
/// </para>
/// </remarks>
internal static class ProblemResults
{
    /// <summary>
    /// The name of the one extension member the published problem schema DECLARES.
    /// </summary>
    /// <remarks>
    /// Spelled exactly as <c>shared/PowerFramework.Contracts/OpenApi/security.v1.yaml</c> names it.
    /// The schema types it as a 64-bit integer over a closed enumeration of the 38 distinct values
    /// the oracle's 41 constants cover, and it is the reason that schema alone in the document sets
    /// <c>additionalProperties: true</c>. The sibling <c>HealthEndpoints</c> spells the same member
    /// for its own readiness problem, so the whole service answers one shape.
    /// </remarks>
    internal const string RetCodeExtensionMember = "retCode";

    /// <summary>
    /// The name of the extension member carrying the preserved localization category.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Qualified as <c>messageCategory</c> rather than a bare <c>category</c> so that it cannot be
    /// mistaken for a classification of the problem itself: it is the localization category of the
    /// MESSAGE the legacy dialog displayed, which is a different thing.
    /// </para>
    /// <para>
    /// The value is the numeric category identifier the legacy passed to its translation call - an
    /// offset from <c>Enums.I18N_CAT_CUSTOM</c>. It is carried as a number rather than as a symbolic
    /// name on purpose: the named catalogue lives in the shared localization library, which this
    /// service does not reference and must not acquire a reference to just to spell a constant.
    /// </para>
    /// <para>
    /// Permitted by the published schema's <c>additionalProperties: true</c>, which states that
    /// further members may appear and that a consumer must ignore any it does not recognise. Emitted
    /// only when a caller supplies one, so a condition that had no category does not acquire a null
    /// member describing nothing.
    /// </para>
    /// </remarks>
    internal const string MessageCategoryExtensionMember = "messageCategory";

    /// <summary>
    /// The name of the extension member carrying the preserved dialog severity.
    /// </summary>
    /// <remarks>
    /// Qualified for the same reason as <see cref="MessageCategoryExtensionMember"/>, and carried as
    /// the legacy icon's own name rather than as a number so a recorded result can be compared with
    /// the legacy call site by eye. Permitted by the schema's <c>additionalProperties: true</c>.
    /// </remarks>
    internal const string MessageSeverityExtensionMember = "messageSeverity";

    /// <summary>
    /// The title used when the symbolic renderer produces nothing and the caller supplied no title.
    /// </summary>
    /// <remarks>
    /// Reachable in exactly one situation: the return code is <see langword="null"/>, which is the
    /// only input for which <c>FormatRetCode</c> answers <see langword="null"/>. It deliberately does
    /// not read <c>UNKNOWN</c>, which is the symbolic name of the real code -4000 and would imply the
    /// caller had returned a code when it had not.
    /// </remarks>
    internal const string UnclassifiedProblemTitle = "Unclassified problem";

    /// <summary>The logger category a problem response is recorded under.</summary>
    private const string LoggerCategoryName =
        "PowerFramework.Security.Endpoints.ProblemResults";

    /// <summary>
    /// The structured template a problem response is recorded with.
    /// </summary>
    /// <remarks>
    /// EVERY FIELD IS A CLASSIFIER, NEVER CONTENT. The numeric code, its symbolic rendering, the
    /// chosen status, the preserved category and severity, and the two tri-state classifications. The
    /// problem <c>detail</c> is deliberately absent: it is server-authored, but it may carry
    /// substitution arguments that originated with the caller, so it stays out of the log by default.
    /// A bearer token, a request payload, a key reference and any resolved key material are not
    /// merely omitted here - there is no parameter through which one could arrive.
    /// </remarks>
    private const string ProblemLogTemplate =
        "Security produced a problem response. retCode={RetCode} name={RetCodeName} "
        + "status={StatusCode} messageCategory={MessageCategory} messageSeverity={MessageSeverity} "
        + "claimsSuccess={ClaimsSuccess} indeterminate={Indeterminate}. The problem detail text is "
        + "deliberately not recorded.";

    /// <summary>
    /// Maps a legacy PowerFramework return code onto the HTTP status this service answers with.
    /// </summary>
    /// <param name="retCode">
    /// The return code to classify, or <see langword="null"/> when the failing operation produced
    /// none.
    /// </param>
    /// <returns>The HTTP status code for that return code.</returns>
    /// <remarks>
    /// <para>
    /// THIS MAP IS DELIBERATE, NOT DERIVED. Every arm was chosen individually and carries its reason
    /// at the arm. No arm is computed from a predicate, from the sign of the value, or from any other
    /// truthiness test, because the algebra it classifies is tri-state and holed - see the type-level
    /// remarks. Reading a status off <c>IsSucceeded</c> would answer 2xx for a prevention, and
    /// reading it off <c>IsFailed</c> would answer 2xx for a cancellation.
    /// </para>
    /// <para>
    /// THE ONE STATUS THIS MAP NEVER PRODUCES IS HTTP'S <em>Not Implemented</em>. That is constraint
    /// C-D and it is the specific trap a status-map file walks into: the four routes that answer that
    /// status are the ingress service's routing metadata for the four deferred capabilities, and
    /// introducing it into this service's error map would put a deferred-capability surface on the
    /// wrong service. <c>E_NO_IMPLEMENTATION</c> and <c>E_NO_SUPPORT</c> therefore have deliberately
    /// chosen arms elsewhere, each documented at the arm.
    /// </para>
    /// <para>
    /// ALIASES CANNOT APPEAR TWICE, AND THAT IS THE LANGUAGE AGREEING WITH THE ORACLE. <c>OK</c>,
    /// <c>SUCCESS</c> and <c>ALLOW</c> are three spellings of 0 and <c>CANCELED</c> and
    /// <c>CANCELLED</c> are two spellings of -2, so only one spelling of each value can be written as
    /// a pattern - a second would be unreachable. The legacy's own renderer collapses the same
    /// aliases the same way, which is why the strings <c>SUCCESS</c>, <c>ALLOW</c> and
    /// <c>CANCELED</c> are unreachable there too. Neither collapse is a defect to fix.
    /// </para>
    /// <para>
    /// Codes with no arm reach the default and are answered as a server fault. Nothing throws: a
    /// classification failure while building an error response must not become a second, harder
    /// failure.
    /// </para>
    /// </remarks>
    internal static int MapStatusCode(long? retCode)
    {
        // ------------------------------------------------------------------------------------------
        // THE NULL ARM, HANDLED FIRST AND EXPLICITLY.
        //
        // Null is not a code; it is the absence of one. The four kernel predicates DISAGREE about it
        // by design - IsSucceeded, IsFailed and IsPrevented all answer false [issucceeded.srf:L11,
        // isfailed.srf:L11, isprevented.srf:L11] while IsAllowed answers TRUE because IsNull is an
        // arm of its result rather than a guard against it [isallowed.srf:L11] - so no verdict can be
        // read off them. A response is nonetheless being built, so the honest status is a server
        // fault: the failure could not be classified, and that is this service's problem rather than
        // the caller's.
        //
        // Note what is NOT done here. Null is not coerced to zero, which would make IsSucceeded
        // answer true and convert "neither succeeded nor failed" into "succeeded"; and it is not
        // substituted with UNKNOWN, which would fabricate a code the caller never returned. Create
        // completes the treatment by OMITTING the retCode member from the body, because the published
        // enumeration is closed and has no null member.
        // ------------------------------------------------------------------------------------------
        if (retCode is null)
        {
            return StatusCodes.Status500InternalServerError;
        }

        return retCode.Value switch
        {
            // A SUCCESS CODE REACHING AN ERROR FACTORY IS A DEFECT IN THE CALLER, and it is answered
            // as a server fault rather than as a success. This factory builds error bodies only, so
            // 200 is not among its outputs: answering 2xx with a problem body would publish a
            // self-contradicting response. The arm is reachable and is therefore mapped and pinned,
            // rather than being left to the default where it would be indistinguishable from an
            // unrecognised code. Create additionally records the misuse when a logger is supplied.
            RetCode.OK => StatusCodes.Status500InternalServerError,

            // PREVENT (1) GETS ITS OWN ARM, AND IS NOT ALLOWED NEAR A SUCCESS BRANCH.
            // IsSucceeded answers TRUE for it, because the predicate is `>= OK` and this value is 1
            // [issucceeded.srf:L12, retcode.sru:L42]. That is the first defect the refactor names,
            // and it is preserved in the predicate rather than repaired - which is exactly why the
            // status is taken from this map instead.
            //
            // 409 is chosen: a prevention is a VETO returned by a hook, so the request was understood
            // and well-formed and was refused by the current state or policy, which is what that
            // status is registered to mean. 403 is deliberately not used - it is an AUTHORIZATION
            // refusal, and conflating a policy veto with one would make an access denial
            // indistinguishable from a rule declining to proceed. A body-semantics status is
            // deliberately not used either, because a veto says nothing about the payload, which may
            // be perfectly valid and still refused.
            RetCode.PREVENT => StatusCodes.Status409Conflict,

            // An unspecified failure with no further classification. The caller reported that the
            // operation failed and nothing more, so the answer is a plain server fault.
            RetCode.FAILED => StatusCodes.Status500InternalServerError,

            // CANCELLED (-2) GETS ITS OWN ARM, AND FALLS INTO NEITHER A SUCCESS NOR A FAILURE BRANCH.
            // IsFailed excludes it EXPLICITLY [isfailed.srf:L12] while it also fails `>= OK`, so it is
            // neither succeeded nor failed - the tri-state hole itself. CANCELED is the same value
            // and therefore cannot be written as a second pattern.
            //
            // 409 again, and the coincidence with PREVENT is deliberate rather than careless. HTTP
            // registers no "cancelled" semantics; the widely seen numeric spelling for it is a
            // proprietary extension rather than a registered code, and emitting an unregistered
            // status would be inventing wire vocabulary. Both conditions are honestly "the operation
            // could not be completed given the current state", which is that status's registered
            // meaning. The two remain distinguishable on the wire through retCode, which is precisely
            // the instruction the published contract gives its consumers: branch on the specific
            // value, never on a two-way success test.
            RetCode.CANCELLED => StatusCodes.Status409Conflict,

            // THE CALLER-INPUT FAMILY, ANSWERED 400.
            // The published contract anchors this directly: its bad-request response states that
            // retCode typically carries E_INVALID_ARGUMENT (-3), and that a request for an
            // unsupported padding lands there because the legacy declares no such value. The three
            // siblings mapped alongside it are the other input-shaped codes this service can produce
            // - a selector outside its enumeration, a payload that does not match its declared form,
            // and a size outside its declared domain.
            RetCode.E_INVALID_ARGUMENT => StatusCodes.Status400BadRequest,
            RetCode.E_INVALID_TYPE => StatusCodes.Status400BadRequest,
            RetCode.E_INVALID_DATA => StatusCodes.Status400BadRequest,
            RetCode.E_OUT_OF_RANGE => StatusCodes.Status400BadRequest,

            // THE ABSENT-REFERENT FAMILY, ANSWERED 404.
            // The contract's reference-not-found response carries E_OBJECT_NOT_FOUND (-16), and
            // E_NOT_EXISTS is the same condition stated the other way round. Neither response
            // enumerates the configured store, suggests a nearby reference, or echoes any part of the
            // stored material; this map only chooses the status, and Create carries only what its
            // caller passed.
            //
            // A caller that must distinguish "the reference exists but you may not use it" passes
            // 403 explicitly - see the type-level remarks on per-code arms - because that condition
            // shares E_ACCESS_DENIED with authentication failure rather than having a code of its own.
            RetCode.E_OBJECT_NOT_FOUND => StatusCodes.Status404NotFound,
            RetCode.E_NOT_EXISTS => StatusCodes.Status404NotFound,

            // THE TRY-AGAIN FAMILY, ANSWERED 503.
            // All three say the same thing to a client - the condition is transient, retry later -
            // and they are distinguished from one another by retCode rather than by status.
            //
            // E_RETRY's pairing with 503 is not invented here: the sibling readiness probe in this
            // same folder already answers a not-ready service with that status carrying that code, so
            // the two routes agree.
            //
            // HTTP's upstream-timeout status is deliberately NOT used for E_TIME_OUT. Its registered
            // meaning is that an intermediary did not receive a timely response from a server behind
            // it, and this service has nothing behind it at all: it is called by its peers and calls
            // none of them (constraint C-A). Claiming it would misdescribe the failure and point an
            // operator at a dependency that does not exist. The request-timeout status is not used
            // either: that one blames the client for not producing a request in time, which is not
            // what happened.
            RetCode.E_BUSY => StatusCodes.Status503ServiceUnavailable,
            RetCode.E_TIME_OUT => StatusCodes.Status503ServiceUnavailable,
            RetCode.E_RETRY => StatusCodes.Status503ServiceUnavailable,

            // ACCESS DENIED IS 403, NOT 401, AND THE DISTINCTION IS STRUCTURAL RATHER THAN STYLISTIC.
            // 401 is emitted by the AUTHENTICATION middleware before any handler runs - measured on
            // this host, which answers an unauthenticated probe with 401, an empty body and a bearer
            // challenge header - so no handler-produced code can ever be the cause of one. A denial
            // that reaches this factory therefore came from a handler that did run, which makes it an
            // AUTHORIZATION refusal, and 403 is that status.
            //
            // The published contract uses this same code for BOTH its 401 and its 403 response, so
            // the code alone genuinely cannot pick between them. 403 is the default because it is the
            // only one of the two a handler can reach; 401 remains available to a caller that must
            // publish it, through the explicit statusCode argument.
            RetCode.E_ACCESS_DENIED => StatusCodes.Status403Forbidden,

            // The contract's server-error response names this code first, and it means exactly what
            // the status means: the operation failed for a reason internal to this service.
            RetCode.E_INTERNAL_ERROR => StatusCodes.Status500InternalServerError,

            // E_NO_SUPPORT (-2000) IS 400, AND THE CONTRACT ITSELF ANCHORS IT. Its bad-request
            // response absorbs a request for a value the legacy never declared, which is precisely
            // what "not supported" means when a caller asks for it: the caller selected something
            // this surface does not offer, so the fault is in the request. This is also one of the two
            // codes constraint C-D puts under scrutiny, and the answer here is a client status rather
            // than the forbidden one.
            RetCode.E_NO_SUPPORT => StatusCodes.Status400BadRequest,

            // E_NO_IMPLEMENTATION (-2001) IS 500, AND THE CHOICE IS THE POINT OF THIS ARM.
            // The obvious HTTP spelling for it is the "Not Implemented" status, and that status
            // IS DELIBERATELY NOT PRODUCED ANYWHERE IN THIS FILE. Constraint C-D reserves it for the
            // ingress service's four routing declarations, which are metadata describing where the
            // deferred capabilities will eventually live; answering it from this service's error map
            // would advertise a deferred-capability surface on a service that has none, which is the
            // wrong-service surface that constraint exists to prevent.
            //
            // 500 is chosen rather than a client status because this code describes a SERVER-side gap:
            // unlike E_NO_SUPPORT above, the request was acceptable and it is this build that cannot
            // carry it out. Telling a caller its request was bad would send it to fix something that
            // is not broken.
            RetCode.E_NO_IMPLEMENTATION => StatusCodes.Status500InternalServerError,

            // The catalogue's own unclassifiable value, which the contract's server-error response
            // names for a failure that cannot be classified. Mapped explicitly rather than left to the
            // default, so that a deliberately unclassifiable failure and an unrecognised code are two
            // distinguishable, separately pinned paths even though they answer the same status.
            RetCode.UNKNOWN => StatusCodes.Status500InternalServerError,

            // THE DOCUMENTED DEFAULT. Every code without an arm above lands here, including the codes
            // this service cannot produce - the transaction, statement, data-object, image, handle,
            // memory, file, member, variable, event, function, platform-error, database, transport and
            // argument-binding codes that belong to the other services' capability areas. They are
            // deliberately NOT enumerated: mapping a code this service cannot raise would be
            // speculation dressed as completeness, and a status chosen for a condition nobody has seen
            // is a status nobody can verify.
            //
            // A server fault is the fail-closed answer. It never blames the caller for a condition
            // this service could not classify, and it never throws: a classification failure while
            // building an error response must not become a second, harder failure.
            _ => StatusCodes.Status500InternalServerError,
        };
    }


    /// <summary>
    /// Reports whether a return code CLAIMS SUCCESS under the kernel predicate while nonetheless
    /// having been handed to this error factory.
    /// </summary>
    /// <param name="retCode">The return code to classify.</param>
    /// <returns>
    /// <see langword="true"/> when the code satisfies the success predicate and is not a prevention;
    /// otherwise <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// One of the two places this type consumes
    /// <see cref="PowerFramework.Shared.Kernel.Predicates"/>, and it consumes them rather than
    /// re-deriving their comparisons so that the algebra has exactly one definition in the estate.
    /// </para>
    /// <para>
    /// THE SECOND CLAUSE IS THE TRI-STATE HOLE MADE EXECUTABLE. <c>IsSucceeded</c> answers true for
    /// <c>PREVENT</c>, because the predicate is <c>&gt;= OK</c> and a prevention is 1
    /// [<c>issucceeded.srf:L12</c>, <c>retcode.sru:L42</c>]. A prevention is a perfectly legitimate
    /// input to this factory, so it is excluded EXPLICITLY here; without that exclusion every veto
    /// would be reported as a caller defect. The exclusion is written against <c>IsPrevented</c>
    /// rather than against the literal 1, because the numeric overload of that predicate is exactly
    /// the <c>= PREVENT</c> test [<c>isprevented.srf:L12</c>] and naming it keeps the reason legible.
    /// </para>
    /// <para>
    /// Null answers <see langword="false"/>, because both predicates guard null to false
    /// [<c>issucceeded.srf:L11</c>, <c>isprevented.srf:L11</c>]. That is correct here: an absent code
    /// claims nothing.
    /// </para>
    /// <para>
    /// This is a DIAGNOSTIC, never a status decision. It feeds the structured log record and the
    /// severity that record is written at, and nothing else. <see cref="MapStatusCode(long?)"/> does
    /// not consult it.
    /// </para>
    /// </remarks>
    internal static bool ClaimsSuccess(long? retCode) =>
        Predicates.IsSucceeded(retCode) && !Predicates.IsPrevented(retCode);

    /// <summary>
    /// Reports whether a return code falls through BOTH halves of the nominally boolean algebra -
    /// neither succeeded nor failed.
    /// </summary>
    /// <param name="retCode">The return code to classify.</param>
    /// <returns>
    /// <see langword="true"/> for a cancellation and for <see langword="null"/>; otherwise
    /// <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The other place this type consumes <see cref="PowerFramework.Shared.Kernel.Predicates"/>, and
    /// the executable statement of the hole itself. Exactly two classes of input satisfy it:
    /// <c>CANCELED</c>/<c>CANCELLED</c>, which <c>IsFailed</c> excludes explicitly
    /// [<c>isfailed.srf:L12</c>] and which also fails <c>&gt;= OK</c>; and
    /// <see langword="null"/>, which both predicates guard to false. A prevention does NOT satisfy it,
    /// because the success predicate claims it.
    /// </para>
    /// <para>
    /// Written as the conjunction of two negated predicates rather than as
    /// <c>IsCancelled(retCode) || retCode is null</c> deliberately. The predicate spelling states the
    /// PROPERTY - that neither half of the algebra accepts this value - instead of enumerating the two
    /// inputs that happen to have it, so it stays correct if the catalogue ever grows another hole. It
    /// also avoids depending on <c>IsCancelled</c>, which is the one predicate with no null guard at
    /// all [<c>iscancelled.srf:L9</c>] and therefore the one whose null behaviour falls out of
    /// language rules rather than from a decision.
    /// </para>
    /// <para>
    /// A DIAGNOSTIC, never a status decision, for the same reason as
    /// <see cref="ClaimsSuccess(long?)"/>.
    /// </para>
    /// </remarks>
    internal static bool IsIndeterminate(long? retCode) =>
        !Predicates.IsSucceeded(retCode) && !Predicates.IsFailed(retCode);

    /// <summary>
    /// Renders a preserved dialog severity as the legacy icon's own name.
    /// </summary>
    /// <param name="severity">The severity to render.</param>
    /// <returns>The legacy icon name for that severity.</returns>
    /// <remarks>
    /// Rendered through an explicit switch rather than by the enumeration's own string conversion,
    /// matching the convention the sibling readiness probe already uses in this folder: the published
    /// vocabulary is then stated in this file and cannot drift silently if a member is ever renamed.
    /// An unrecognised value - reachable only by casting an arbitrary number to the enumeration -
    /// renders as the value that claims nothing, which is the same fail-closed choice the status map's
    /// default makes.
    /// </remarks>
    internal static string DescribeSeverity(ProblemSeverity severity) => severity switch
    {
        ProblemSeverity.None => "None",
        ProblemSeverity.Information => "Information",
        ProblemSeverity.Question => "Question",
        ProblemSeverity.Exclamation => "Exclamation",
        ProblemSeverity.StopSign => "StopSign",
        _ => "None",
    };

    /// <summary>
    /// Builds this service's <c>application/problem+json</c> response, carrying the legacy return
    /// code and the preserved text, category and severity of the condition.
    /// </summary>
    /// <param name="retCode">
    /// The legacy PowerFramework return code for the condition, or <see langword="null"/> when the
    /// failing operation produced none. Determines the status through
    /// <see cref="MapStatusCode(long?)"/> unless <paramref name="statusCode"/> overrides it, and is
    /// carried on the wire as the one extension member the published schema declares.
    /// </param>
    /// <param name="detail">
    /// The preserved message text - the second argument of the legacy dialog, verbatim, including any
    /// substitutions already applied to it. Whitespace-only text is treated as absent so the member is
    /// omitted rather than serialized as noise. It must never be given key material, a stack trace, a
    /// filesystem path, a provider type name or any part of the configured key store: the published
    /// contract forbids all of those in this member, and this factory copies what it is handed.
    /// </param>
    /// <param name="severity">
    /// The preserved severity - the legacy dialog's icon. Required rather than defaulted, so that each
    /// call site states the severity the legacy stated instead of inheriting one by omission.
    /// </param>
    /// <param name="category">
    /// The preserved localization category of the message, as the numeric identifier the legacy passed
    /// to its translation call, or <see langword="null"/> when the condition had none. Omitted from the
    /// body when absent.
    /// </param>
    /// <param name="statusCode">
    /// A deliberate per-condition status that overrides the shared map. THIS IS THE MECHANISM BY WHICH
    /// THE THREE SIBLING ENDPOINT FILES ADD THEIR OWN ARMS: where a sibling's semantics differ from
    /// the shared default it passes the status explicitly, and the shared map is left untouched. Two
    /// conditions are known to need it, both documented on <see cref="MapStatusCode(long?)"/>.
    /// </param>
    /// <param name="title">
    /// A stable summary of the problem TYPE. Defaults to the symbolic name of the return code, which is
    /// rendered by the kernel's own formatter rather than re-spelled here.
    /// </param>
    /// <param name="loggerFactory">
    /// An optional logger factory. When supplied, one structured record is written under this
    /// factory's own category; when omitted, nothing is logged and this method is pure. A factory is
    /// taken rather than a logger so the record is attributable to THIS factory rather than to
    /// whichever endpoint happened to call it - the same shape the sibling readiness probe uses.
    /// </param>
    /// <returns>
    /// A problem result carrying the resolved status, the preserved text, and the extension members.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The concrete result type is returned rather than the interface, so a test can read the status
    /// and the built payload directly instead of executing the result against a fabricated context. It
    /// implements the result interface, so a handler can still return it unchanged.
    /// </para>
    /// <para>
    /// THE TITLE IS THE SYMBOLIC NAME, AND THE FORMATTER'S TWO PRESERVED DEFECTS TRAVEL WITH IT. The
    /// kernel's formatter is used rather than a second spelling of the code names, and it reproduces
    /// the oracle's own arm table [<c>formatretcode.srf:L10-L83</c>] exactly - including the two
    /// defects that table has and that this refactor is required NOT to repair. It declares no arm for
    /// <c>E_RETRY</c>, nor for <c>PREVENT</c> or <c>UNKNOWN</c>, so those three render through the
    /// fallback as the word UNKNOWN followed by the number in parentheses; and its zero and -2 arms
    /// shadow their aliases, so the strings <c>SUCCESS</c>, <c>ALLOW</c> and <c>CANCELED</c> are
    /// unreachable. A title of <c>UNKNOWN (-33)</c> for a retry condition is therefore correct
    /// behaviour, not a bug in this file. A caller that needs a human title passes one.
    /// </para>
    /// <para>
    /// THE RETURN CODE MEMBER IS OMITTED WHEN THERE IS NO CODE. The published schema types it as a
    /// 64-bit integer over a CLOSED enumeration with no null member, so emitting null would break the
    /// schema, and substituting the catalogue's unclassifiable value would fabricate a code the caller
    /// never returned. The schema declares no required members, so an absent extension member is
    /// valid. The status is still chosen deliberately for that case - see the null arm of the map.
    /// </para>
    /// <para>
    /// THE PROBLEM TYPE MEMBER IS LEFT TO THE FRAMEWORK. The published contract states that the
    /// specification's own default is used when no more specific type applies, and the framework fills
    /// the member with the specification section for the resolved status, which IS a more specific
    /// type. Adopting the framework's own error shape rather than hand-writing one is the same
    /// reasoning that leaves token validation to the stock bearer handler: it keeps hand-written code
    /// out of a path where a mistake is expensive.
    /// </para>
    /// <para>
    /// NOTHING SENSITIVE CAN ARRIVE HERE. There is no parameter for a token, a key, a key reference, an
    /// initialization vector or a request payload, so no such value can be copied into a body or into
    /// a log record by this factory. Constraint C-F is met by the SHAPE of this signature and not only
    /// by the discipline of its callers.
    /// </para>
    /// </remarks>
    internal static ProblemHttpResult Create(
        long? retCode,
        string? detail,
        ProblemSeverity severity,
        long? category = null,
        int? statusCode = null,
        string? title = null,
        ILoggerFactory? loggerFactory = null)
    {
        // The caller's deliberate choice wins; otherwise the shared, explicitly authored map decides.
        int resolvedStatus = statusCode ?? MapStatusCode(retCode);

        // Null exactly when retCode is null - that is the formatter's own documented behaviour, and
        // it is the only input for which it answers null.
        string? symbolicName = Formatting.FormatRetCode(retCode);

        string resolvedTitle = title ?? symbolicName ?? UnclassifiedProblemTitle;

        // Whitespace-only text becomes absent rather than being serialized as noise, matching how the
        // sibling readiness probe treats an empty check description.
        string? resolvedDetail = string.IsNullOrWhiteSpace(detail) ? null : detail;

        Dictionary<string, object?> extensions = new(StringComparer.Ordinal)
        {
            [MessageSeverityExtensionMember] = DescribeSeverity(severity),
        };

        if (retCode is not null)
        {
            extensions[RetCodeExtensionMember] = retCode.Value;
        }

        if (category is not null)
        {
            extensions[MessageCategoryExtensionMember] = category.Value;
        }

        if (loggerFactory is not null)
        {
            LogProblem(
                loggerFactory.CreateLogger(LoggerCategoryName),
                retCode,
                symbolicName,
                resolvedStatus,
                category,
                severity);
        }

        return TypedResults.Problem(
            detail: resolvedDetail,
            statusCode: resolvedStatus,
            title: resolvedTitle,
            extensions: extensions);
    }

    /// <summary>
    /// Writes one structured record describing a problem response.
    /// </summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="retCode">The legacy return code, or <see langword="null"/> when there is none.</param>
    /// <param name="symbolicName">
    /// The code's symbolic rendering, already produced by the kernel formatter so that the log and the
    /// response body cannot disagree about it.
    /// </param>
    /// <param name="statusCode">The resolved HTTP status.</param>
    /// <param name="category">The preserved localization category, or <see langword="null"/>.</param>
    /// <param name="severity">The preserved dialog severity.</param>
    /// <remarks>
    /// <para>
    /// STRUCTURED, AND REDACTED BY CONSTRUCTION. Every field is a classifier - the code, its symbolic
    /// name, the status, the preserved category and severity, and the two tri-state classifications.
    /// The problem detail text is deliberately not among them, because although it is server-authored
    /// it may carry substitutions that originated with the caller. A bearer token, a request payload, a
    /// key reference and any resolved key material are not merely omitted: this method has no
    /// parameter through which one could be passed.
    /// </para>
    /// <para>
    /// A server fault is recorded at error level and everything else at warning level, so that a
    /// caller's malformed request does not page an operator while a genuine internal failure does. The
    /// threshold is expressed against the lowest server-fault status rather than by arithmetic on the
    /// number, so it reads as the classification it is.
    /// </para>
    /// <para>
    /// The logger is handed in by <see cref="Create"/> rather than being pulled from a service locator
    /// here, which keeps this whole type usable without a host and therefore directly unit-testable. A
    /// named category is used rather than a generic type parameter so that the record is attributable
    /// to this factory rather than to whichever endpoint happened to call it.
    /// </para>
    /// </remarks>
    private static void LogProblem(
        ILogger logger,
        long? retCode,
        string? symbolicName,
        int statusCode,
        long? category,
        ProblemSeverity severity)
    {
        LogLevel level = statusCode >= StatusCodes.Status500InternalServerError
            ? LogLevel.Error
            : LogLevel.Warning;

        logger.Log(
            level,
            ProblemLogTemplate,
            retCode,
            symbolicName,
            statusCode,
            category,
            DescribeSeverity(severity),
            ClaimsSuccess(retCode),
            IsIndeterminate(retCode));
    }
}
