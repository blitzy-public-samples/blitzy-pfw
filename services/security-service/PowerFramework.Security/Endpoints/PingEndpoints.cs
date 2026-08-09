// ==================================================================================================
//  PingEndpoints - GET /v1/ping ON THE SECURITY SERVICE, TOKEN REQUIRED
//  ------------------------------------------------------------------------------------------------
//  SUBJECT
//  The standing proof that this boundary is authenticated. It exists on ALL FOUR services rather than
//  only at the ingress, because an internal edge is a created boundary too: the legacy framework was an
//  in-process library that opened no listening socket, registered no route and received no unsolicited
//  request, so every boundary in this system was created by the decomposition itself. "No new attack
//  surface" therefore means every newly created surface is authenticated from the outset (constraint
//  C-G), and this route is where that claim becomes testable rather than asserted.
//
//  BOTH OUTCOMES ARE THE CONTRACT
//  200 with a valid token, and 401 WITHOUT ONE. The second half is the half that demonstrates the
//  boundary is actually closed, so it is a declared response rather than an implementation detail.
//
//  THIS SERVICE PRESENTS TOKENS TO ITSELF LIKE ANY OTHER CALLER
//  Security mints the system's tokens, and it is nonetheless subject to the same rule on its own
//  surface: this route requires a valid token and grants no exemption to the issuer. There is no
//  development bypass, no dev-token mode and no relaxed validation branch anywhere in this file or in
//  Program.cs - a bypass that exists only in Development is still a bypass, and on the service holding
//  the system's single signing secret it is the least acceptable place for one.
//
//  WHAT THE BODY MAY NOT CARRY (constraints C-F and C-G)
//  No part of the presented token, no claim of it, no key, no key identifier, no issuer, no audience, no
//  algorithm name and no part of the verification material or the configuration behind it. A liveness
//  signal that grew a payload would also be scope creep, which constraint C-B forbids.
// ==================================================================================================

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using PowerFramework.Shared.Kernel;

namespace PowerFramework.Security.Endpoints;

/// <summary>
/// Declares and serves this service's authenticated liveness probe.
/// </summary>
/// <remarks>
/// This file authorises the route; it does not authenticate it. Registering the authentication scheme,
/// the authorization services and the bearer options is <c>Program.cs</c>'s responsibility and
/// deliberately not this file's.
/// </remarks>
public static class PingEndpoints
{
    /// <summary>This service's canonical name, as it appears in the response body.</summary>
    private const string ServiceName = "security";

    /// <summary>The route, fixed by the environment's documented authenticated probe.</summary>
    private const string RoutePattern = "/v1/ping";

    /// <summary>The endpoint and operation name.</summary>
    private const string EndpointName = "ping";

    /// <summary>The tag the operation is grouped under.</summary>
    private const string TagName = "Diagnostics";

    /// <summary>The security-scheme key the generated document declares the bearer requirement under.</summary>
    private const string BearerSchemeName = "bearerAuth";

    /// <summary>
    /// Maps the authenticated liveness probe.
    /// </summary>
    /// <param name="endpoints">The route builder to declare the route on.</param>
    /// <returns>The same <paramref name="endpoints"/> instance, so that mapping calls compose.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="endpoints"/> is <see langword="null"/>.
    /// </exception>
    public static IEndpointRouteBuilder MapPingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet(RoutePattern, Ping)
            // ----------------------------------------------------------------------------------
            // CONSTRAINT C-G, AND THE ONE LINE THIS WHOLE FILE EXISTS FOR.
            //
            // Unconditional. Not wrapped in an environment test, not paired with an anonymous
            // fallback, not weakened by a configuration switch. An unauthenticated request reaches
            // the authorization middleware, is challenged by the bearer handler registered in
            // Program.cs, and receives 401 - in Development exactly as in production.
            //
            // No policy name is supplied on purpose: the parameterless form requires an
            // authenticated principal under the application's default policy, which is precisely the
            // property being proved. Naming a policy would couple this route to a definition it does
            // not own and could silently weaken the requirement if that definition were relaxed.
            // ----------------------------------------------------------------------------------
            .RequireAuthorization()
            .WithName(EndpointName)
            .WithTags(TagName)
            .WithSummary("The standing proof that this boundary is authenticated. Token required.")
            .WithDescription(
                "Returns a trivial success only when a valid token is presented, and 401 without one. "
                + "Both outcomes are part of the health-and-readiness contract. The operation exists "
                + "so that the authenticated-boundary requirement is testable rather than merely "
                + "asserted, and it is present on all four services rather than only at the ingress "
                + "because an internal edge is a created boundary too. Security grants itself no "
                + "exemption on its own surface. The body does not echo the presented token, any claim "
                + "of it, any key, key identifier, issuer, audience or algorithm name, or any part of "
                + "the verification material.")
            .Produces<PingResponse>(StatusCodes.Status200OK)
            // The 401 is a DECLARED response, not an implementation detail: it is half of what this
            // operation promises. No response type accompanies it, because the challenge is produced
            // by the authentication middleware rather than by this handler, so declaring a schema
            // would advertise a body this file cannot guarantee.
            .Produces(StatusCodes.Status401Unauthorized)
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
    /// The handler is only ever reached with an authenticated principal, because the route requires
    /// authorization. It therefore does not test the principal, does not read a claim from it and does
    /// not report anything about it: <c>Authenticated</c> is unconditionally <see langword="true"/>
    /// precisely because reaching this line already proves it. Inspecting the principal instead would
    /// turn a liveness signal into a token-introspection endpoint - which on the issuing service would
    /// be an especially unfortunate thing to publish.
    /// </para>
    /// <para>
    /// The clock is resolved from request services when one is registered and falls back to the system
    /// clock otherwise, so this handler imposes no registration requirement on <c>Program.cs</c> while
    /// remaining substitutable in a test. That seam matters on this service in particular: the
    /// cryptographic surface's random and GUID generators and this timestamp are the primary
    /// non-determinism sources in the whole estate, and characterization requires each to be maskable.
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
    /// A per-endpoint operation transformer is the sanctioned mechanism for this on .NET 10. The older
    /// per-endpoint OpenAPI configuration extension is deprecated on this toolchain and raises a
    /// deprecation diagnostic, which the repository-wide warnings-as-errors setting turns into a build
    /// failure, so it is not an option here even as a fallback.
    /// </para>
    /// <para>
    /// The generator does not synthesise a security requirement from authorization metadata by itself, so
    /// requiring a token without declaring it would leave the published document claiming an anonymous
    /// operation while the running service answers 401. Declaring it here keeps the document honest about
    /// the one property this operation exists to prove.
    /// </para>
    /// <para>
    /// Both steps are idempotent, so this composes with whatever document-wide security the host
    /// registers rather than fighting it: the scheme is added only when absent, and the requirement only
    /// when the operation carries none.
    /// </para>
    /// <para>
    /// NO KEY MATERIAL, KEY-SET ADDRESS OR DISCOVERY ADDRESS APPEARS IN THE DECLARATION. It states the
    /// transport scheme and the token format, which is all a consumer needs in order to point its own
    /// stock bearer handler at the issuer named in that consumer's configuration.
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
                    "A JSON Web Token issued by this service, presented in the Authorization header. "
                    + "Security is the sole issuer in the system, and it validates tokens on its own "
                    + "surface with the same stock bearer handler the other three services use.",
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
    /// The successful half of the authentication proof, returned only when a valid token was presented.
    /// </summary>
    /// <param name="Service">The responding service's canonical name. Always this service's own name.</param>
    /// <param name="Authenticated">
    /// Always <see langword="true"/> when this body is returned, because the operation cannot be reached
    /// without a valid token. Present so a conformance test asserting the authenticated path has something
    /// explicit to assert on rather than inferring success from a status code.
    /// </param>
    /// <param name="RetCode">
    /// The legacy PowerFramework return code for success, surfaced symbolically from the shared kernel
    /// rather than written as a literal, so a client already branching on the legacy return-code
    /// vocabulary needs no second mechanism on this route.
    /// </param>
    /// <param name="Timestamp">
    /// The instant the response was produced, so a consumer can detect a stale cached response.
    /// </param>
    /// <remarks>
    /// Nested inside its endpoint class because it is the shape of exactly one response and belongs with
    /// the route that produces it. Its four members are the whole body, and the member names match the
    /// equivalent body on the sibling services so the estate presents one shape.
    /// </remarks>
    public sealed record PingResponse(
        string Service,
        bool Authenticated,
        long RetCode,
        DateTimeOffset Timestamp);
}
