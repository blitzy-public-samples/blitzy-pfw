// ==================================================================================================
//  PingEndpoints - GET /v1/ping ON THE PERSISTENCE SERVICE, TOKEN REQUIRED
//  ------------------------------------------------------------------------------------------------
//  SUBJECT
//  The standing proof that this boundary is authenticated. It exists on ALL FOUR services rather than
//  only at the ingress, because an internal edge is a created boundary too: the legacy framework was
//  an in-process library that opened no listening socket, registered no route and received no
//  unsolicited request, so every boundary in this system was created by the decomposition itself.
//  "No new attack surface" therefore means every newly created surface is authenticated from the
//  outset (constraint C-G), and this route is where that claim becomes testable rather than asserted.
//
//  BOTH OUTCOMES ARE THE CONTRACT
//  200 with a valid token, and 401 WITHOUT ONE. The second half is the half that demonstrates the
//  boundary is actually closed, so it is a declared response rather than an implementation detail.
//
//  WHAT THE BODY MAY NOT CARRY (constraints C-F and C-G)
//  No part of the presented token, no claim of it, no part of the verification material or the
//  configuration behind it, no connection string, no data-source path and no upstream address. A
//  liveness signal that grew a payload would also be scope creep, which constraint C-B forbids.
//
//  NO OPENAPI TRANSFORMER HERE, AND THAT IS A PROJECT-LEVEL FACT RATHER THAN AN OMISSION
//  Persistence is gRPC-primary: its published surface is contracts C-05 to C-08 in
//  shared/PowerFramework.Contracts/Proto/persistence.v1.proto, and this REST pair exists only for the
//  readiness gate and this authentication proof. The project therefore references neither
//  Microsoft.AspNetCore.OpenApi nor Microsoft.OpenApi and publishes no OpenAPI document, so the bearer
//  requirement is declared through endpoint metadata alone. Adding either package to describe two
//  diagnostic routes would be scope creep on a service whose contract is protocol-buffer shaped.
//
//  THERE IS ALSO NO HAND-AUTHORED WIRE DOCUMENT BEHIND THIS ROUTE, AND NONE MAY BE INVENTED.
//  PowerFramework.Contracts carries exactly two OpenAPI wire contracts - one for the ingress and one
//  for Security - and neither covers this service. The authority for the path, the token requirement
//  and the 401 is the refactor plan's port-and-endpoint map together with the attached environment's
//  operational access contract, and nothing else. The generated
//  PowerFramework.Contracts.Persistence.V1 namespace is the gRPC surface for contracts C-05 to C-08
//  and has no bearing here, so no protocol-buffer message is pulled into the body below.
//
//  WHAT THIS FILE DELIBERATELY DOES NOT DO, each with the reason it does not
//    * IT REGISTERS NO AUTHENTICATION. The stock bearer handler, its verification material and the
//      authorization policy are all registered by Program.cs. This file only REQUIRES authorization
//      on one route. Keeping the security-critical path inside framework code rather than
//      hand-written code is the very reason Security publishes its key set over plain HTTP, so
//      re-implementing any part of it here would forfeit the property that decision bought.
//    * IT MINTS NOTHING, AND CANNOT. Security is the sole issuer in the system and holds the only
//      signing secret; this service holds verification material only. There is no signing credential,
//      no key construction and no token writer below - and the minting package is deliberately absent
//      from this project's references, so a minting call would not even compile. That omission is a
//      structural guarantee and must not be "fixed" by adding the package.
//    * IT WEAKENS NO VALIDATION. Issuer, audience, lifetime and signing-key validation all stay on.
//      Nothing here reads the Authorization header, parses a token, or decides for itself whether a
//      caller is authenticated.
//    * IT MAKES NO OUTBOUND CALL. It fetches no key set of its own; the stock handler retrieves and
//      caches that metadata lazily, which is what lets this host start while Security is still coming
//      up. Lazy retrieval is a matter of TIMING and is emphatically not licence to skip validation.
//    * IT TOUCHES NO STORAGE. No connection, no context, no factory, no query. Storage reachability
//      belongs to the sibling readiness route; adding it here would duplicate that responsibility and
//      hand an authenticated caller a storage-probing primitive nobody asked for. This service is
//      also the only holder of a storage provider, and the only evidenced engine in the repository is
//      the file-backed one, so no other database is reached for or fabricated.
//    * IT DECLARES NO ROUTE FOR ANY CAPABILITY AREA HELD BACK FROM THIS PHASE. Those areas are not
//      implemented at all - not partially, and not as stubs. Their reserved routes are routing
//      metadata belonging exclusively to one file on the ingress service, and a routing declaration
//      there is not a stub here. This file declares exactly the one route above, throws no
//      not-implemented exception, and carries no placeholder anticipating a future service.
//    * IT DECLARES NO SCREAMING_SNAKE CONSTANT. The repository .editorconfig scopes its CA1707 and
//      IDE1006 suppressions to the individually named files that genuinely carry preserved legacy
//      identifiers, and no file in this folder is among them; with TreatWarningsAsErrors inherited as
//      true such a declaration would be a build ERROR here. Legacy constants are CONSUMED from
//      PowerFramework.Shared.Kernel - which is safe, because CA1707 reports declarations only.
//    * IT CONTAINS NO PLACEHOLDER. No deferred-work marker, no stub, no mock value: every path below
//      is complete and returns a real, computed result.
//
//  FAIL FAST, CORRECTLY SCOPED - READ THIS BEFORE ADDING A GUARD HERE
//  The framework's posture for a STRUCTURAL fault is to stop rather than continue degraded: the
//  application object's systemerror event unpacks a seven-field assert payload split on a line
//  separator and then executes HALT CLOSE [ws_objects/pfw.pbl.src/pfw.sra:L111-L144]. That posture is
//  reproduced by Program.cs's startup validation, NOT here. A missing, malformed, expired or
//  wrong-audience token is a NORMAL, EXPECTED outcome on an authenticated boundary, and its correct
//  expression is 401. Terminating the process over a rejected credential would turn the proof this
//  file exists to provide into a denial-of-service lever for any anonymous caller.
//
//  TESTABILITY (constraint C-H: 80 percent line coverage per in-scope service)
//  Nothing below needs a live issuer, a database, a container or a real clock. The sibling test
//  project hosts this application in process and asserts BOTH arms - 401 without a token and 200 with
//  a valid one - plus the malformed, wrong-audience, expired and unknown-key rejections that prove
//  each validation is live, the same 401 under the Development environment, the absence of every
//  disclosed value from the success body, and that a poisoned storage seam changes nothing here.
// ==================================================================================================

using Microsoft.AspNetCore.Http.HttpResults;
using PowerFramework.Shared.Kernel;

namespace PowerFramework.Persistence.Endpoints;

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
    private const string ServiceName = "persistence";

    /// <summary>The route, fixed by the environment's documented authenticated probe.</summary>
    private const string RoutePattern = "/v1/ping";

    /// <summary>The endpoint and operation name.</summary>
    private const string EndpointName = "ping";

    /// <summary>The tag the operation is grouped under.</summary>
    private const string TagName = "Diagnostics";

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
            // authenticated principal under the application's default policy, which is precisely
            // the property being proved. Naming a policy would couple this route to a definition it
            // does not own and could silently weaken the requirement if that definition were
            // relaxed.
            // ----------------------------------------------------------------------------------
            .RequireAuthorization()
            .WithName(EndpointName)
            .WithTags(TagName)
            .WithSummary("The standing proof that this boundary is authenticated. Token required.")
            .WithDescription(
                "Returns a trivial success only when a valid token is presented, and 401 without "
                + "one. Both outcomes are part of the health-and-readiness contract. The operation "
                + "exists so that the authenticated-boundary requirement is testable rather than "
                + "merely asserted, and it is present on all four services rather than only at the "
                + "ingress because an internal edge is a created boundary too. The body does not "
                + "echo the presented token, any claim of it, or any part of the verification "
                + "material, and it carries no connection string or data-source detail.")
            .Produces<PingResponse>(StatusCodes.Status200OK)
            // The 401 is a DECLARED response, not an implementation detail: it is half of what this
            // operation promises. No response type accompanies it, because the challenge is produced
            // by the authentication middleware rather than by this handler, so declaring a schema
            // would advertise a body this file cannot guarantee.
            .Produces(StatusCodes.Status401Unauthorized);

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
    /// turn a liveness signal into a token-introspection endpoint and leak claim content into a body.
    /// </para>
    /// <para>
    /// THE CLOCK IS THE ONE SEAM THIS HANDLER HAS, and it is resolved rather than injected.
    /// <c>Program.cs</c> registers <see cref="TimeProvider.System"/>, so the deployed host always
    /// supplies one and a test substitutes a deterministic double for the whole host - which is what
    /// the characterization model requires, because every clock read has to be maskable from the
    /// master recording and the candidate alike. The fallback covers a host that maps this route
    /// without registering the seam, so the route is never the reason such a host fails.
    /// </para>
    /// <para>
    /// Taking <see cref="TimeProvider"/> as a handler parameter instead would be inferred as a request
    /// BODY on a GET and fail at startup wherever the type is not registered, and reading the ambient
    /// system clock directly would defeat the seam outright. Neither is used.
    /// </para>
    /// </remarks>
    private static Ok<PingResponse> Ping(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        // RequestServices is non-nullable on HttpContext, so this deliberately does NOT guard it: a
        // null-conditional here would be an untestable branch that silently swallowed the seam.
        TimeProvider clock = httpContext.RequestServices.GetService<TimeProvider>() ?? TimeProvider.System;

        PingResponse body = new(
            Service: ServiceName,
            Authenticated: true,
            RetCode: RetCode.OK,
            Timestamp: clock.GetUtcNow());

        return TypedResults.Ok(body);
    }

    /// <summary>
    /// The successful half of the authentication proof, returned only when a valid token was presented.
    /// </summary>
    /// <param name="Service">The responding service's canonical name. Always this service's own name.</param>
    /// <param name="Authenticated">
    /// Always <see langword="true"/> when this body is returned, because the operation cannot be reached
    /// without a valid token. Present so a conformance test asserting the authenticated path has
    /// something explicit to assert on rather than inferring success from a status code.
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
