// ==============================================================================================
//  DeferredCapabilityEndpoints - the four reserved Phase 2 extension points, and nothing else
//  --------------------------------------------------------------------------------------------
//  WIRE AUTHORITY  shared/PowerFramework.Contracts/OpenApi/gateway.v1.yaml is authoritative for
//                  every observable detail below - route template, HTTP status, media type, body
//                  member set and authentication posture. Where that document and docs/CONTRACTS.md
//                  §13 read differently, the document wins; each such point is called out by
//                  locator in the decisions section further down.
//  LEGACY ANCHOR   ws_objects/pfw.shared.pbl.src/retcode.sru:L78 declares
//                  `Constant Long E_NO_IMPLEMENTATION = -2001`. That value is consumed here through
//                  the symbol RetCode.E_NO_IMPLEMENTATION and is never written as a literal. The
//                  .sru is READ ONLY - it is the behavioural oracle, never an edit target.
//
//  WHY THESE FOUR ROUTES EXIST, AND WHY A ROUTE-TABLE ENTRY IS NOT A STUB
//  --------------------------------------------------------------------------------------------
//  Phase 1 implements four of an eight-service target roster. The other four - DesignSystem,
//  Documents, Integration and ScriptBridge - are deferred, and roughly 81% of the legacy estate is
//  assigned to them. They are permitted exactly two representations in the whole refactor: a
//  destination assignment in the full-estate mapping (docs/SERVICE_MAPPING.md, docs/DEFERRED.md),
//  and the four named routes declared in this file.
//
//  The routes exist for one reason: so that the shape of the eventual system is legible from
//  Gateway's own contract while NOTHING is implemented behind it. A caller that reaches
//  /v1/design/anything learns that the capability area is known, named and deliberately not built,
//  rather than meeting an undifferentiated 404 that would make the eventual topology invisible.
//
//  The prohibition that governs this file is on IMPLEMENTING the deferred services - not
//  partially, and not even to "stub them out" - because a half-built deferred service is worse than
//  a documented gap. A declaration in a route table implements nothing: it has no handler beyond
//  the constant body below, it calls nothing, and it can reach nothing, because there is nothing
//  behind it to reach. That distinction is stated the same way from three other sides, so no single
//  document can be read as the only place it was claimed: docs/CONTRACTS.md §13.1,
//  docs/DEFERRED.md §5.2-5.3, and the header comment of gateway.v1.yaml itself.
//
//  WHAT THIS FILE DELIBERATELY DOES NOT CONTAIN
//  --------------------------------------------------------------------------------------------
//  The negatives are as much a part of the specification as the positives, and they are mechanically
//  checkable, which is the point:
//
//      * no service class, handler or typed client for any deferred service;
//      * no interface anticipating a future implementation of one;
//      * no exception-throwing placeholder class, and no not-implemented exception type named or
//        thrown anywhere - returning the 501 STATUS is required, while a type that throws to make
//        something compile is precisely the placeholder that is forbidden. They are not the same
//        thing, and the difference is the whole distinction this file rests on;
//      * no branch on the path remainder, no partial dispatch, no feature detection - every method
//        and every path under each prefix produces one identical answer;
//      * no options type, configuration key, appsettings section or environment variable for any
//        deferred service, and no signing, verification or mutual-TLS material for one either;
//      * no dependency-injection registration of any kind;
//      * no port number at all - not this service's own listener, and not the slot left reserved for
//        a Phase 2 DesignSystem listener. That reservation is recorded in the orchestration manifest
//        and in docs/ARCHITECTURE.md §4.1-4.3, which are its proper homes: a port belongs in
//        configuration and documentation, never in endpoint code;
//      * no upstream call and no upstream client, because there is no upstream. Gateway reaches the
//        three services that DO exist through the published contracts project; these four reach
//        nothing.
//
//  DECISIONS, RECORDED SO THEY ARE AUDITABLE RATHER THAN MERELY ASSERTED
//  --------------------------------------------------------------------------------------------
//  D1  MEDIA TYPE AND BODY SCHEMA: application/json carrying ReservedRouteBody, NOT
//      application/problem+json. gateway.v1.yaml:L2795-L2805 defines the ReservedForPhaseTwo
//      response with `content: application/json` and `schema: ReservedRouteBody`, while
//      problem+json is reserved in that document for the 4xx/5xx ProblemDetails family. The
//      reserved routes are not a fault to be diagnosed; they are a declaration with a fixed,
//      machine-readable shape, and the schema at :L4524-L4585 fixes four of its five members as
//      constants. The document is the wire authority, so the document is followed.
//
//  D2  CATCH-ALL TEMPLATE: the contract spells each path `/v1/<area>/{path}` and each template below
//      is `/v1/<area>/{**path}`. This is a route FAMILY rather than a single route, which is what makes
//      a Phase 2 consumer's eventual URL shape legible; it also means a nested path resolves here
//      instead of falling through to a generic 404, and the bare prefix resolves here too because a
//      catch-all segment matches the empty remainder.
//      OPENAPI CANNOT DESCRIBE THAT, AND THE GAP IS DECLARED RATHER THAN FAKED. An OpenAPI path
//      parameter matches a single segment, and there is no conformant field that widens it. An earlier
//      revision set `allowReserved` on the ReservedPath parameter to stand in for the difference; that
//      was INVALID - OpenAPI 3.1 defines `allowReserved` for `in: query` parameters only, so the
//      document failed validation while still not expressing catch-all semantics. Both this file and
//      gateway.v1.yaml therefore carry the behaviour in the `x-catch-all` vendor extension attached to
//      the parameter, which states the route template, the nested-segment capture, the empty-remainder
//      match and the every-method answer. A vendor extension is metadata about how the URL is
//      templated; it is not a request schema, and no operation here declares one.
//
//  D3  GET AND POST ARE MAPPED SEPARATELY, AND EVERY OTHER METHOD IS MAPPED TOO. The contract
//      declares two operations per path with DISTINCT operation identifiers - `reservedDesignSystem`
//      for the GET, which is the identifier v1 published first, and `reservedDesignSystemPost` for the
//      POST, which is the one added later and therefore the only one carrying a suffix. One MapMethods
//      call covering both verbs would produce one endpoint, one endpoint name and therefore one
//      duplicated operation identifier across the two generated operations, which is invalid
//      OpenAPI - so GET and POST are mapped as separate operations. The contract also requires that
//      *every other* HTTP method answer the same 501 "so no method appears implemented"
//      (:L2215, :L2263; docs/DEFERRED.md §5.1). Mapping only GET and POST would make an unlisted
//      verb answer 405, which is a different statement, so the remaining standard methods are mapped
//      to the same handler and excluded from the description - the described surface stays exactly
//      the two operations the contract declares, while the observable behaviour is uniform across
//      verbs. The method sets are disjoint, so the three mappings on one template cannot produce an
//      ambiguous match.
//
//  D4  THE HANDLER IS TYPED `IResult`, DELIBERATELY. A concrete JsonHttpResult<T> return type is an
//      endpoint metadata provider and would contribute an inferred 200 response to the generated
//      document. NO 2xx MAY APPEAR: one would say a deferred service had been built. Returning IResult
//      suppresses that inference, and every declared response is then supplied explicitly.
//      THE DECLARED SET IS {401, 501}, WHICH IS WHAT gateway.v1.yaml AUTHORS AND WHAT THE CONTRACT
//      TESTS ASSERT. 501 is supplied by Produces<ReservedRouteBody>(501) and 401 by
//      ProducesProblem(401, application/problem+json), matching the shared Unauthorized response the
//      authored document references. The two are not two outcomes of one handler: the 401 is the
//      pre-handler refusal (D5) and the 501 is the only result the handler computes - unconditionally,
//      for every method and every path remainder, with nothing evaluated first.
//      AN EARLIER REVISION DECLARED {501} ALONE, on the reasoning that a status produced by the
//      security scheme is not a response this ROUTE computes. That is right about the origin and wrong
//      about the obligation: a client generated from a set that omits 401 is told these operations
//      cannot return the status they demonstrably do return, and a conformance tool checking response
//      coverage reports a violation against a correct server. Declaring the pre-handler refusal costs
//      nothing about unconditionality, so it is declared.
//      WHAT WOULD STILL BE A VIOLATION, so the line stays auditable: any 2xx, and any 4xx OTHER than
//      that 401 - a 400, a 404 or a 409 would each say the route inspects the request before
//      answering. Neither appears.
//
//  D5  AUTHENTICATED, WITH THE GUARD ANSWERING FIRST. gateway.v1.yaml sets a document-level bearer
//      requirement that exactly one operation overrides - anonymous GET /health - and states that the
//      reserved families are explicitly NOT among the exceptions, "so an unauthenticated caller
//      cannot enumerate the deferred roster". Every mapping below is therefore authorized, which
//      means an anonymous request is answered 401 by the authentication middleware and never reaches
//      the 501. That ordering is asserted from the outside by
//      tests/e2e/specs/deferred-routes.spec.ts, which requires 401 and explicitly NOT 501 for an
//      anonymous probe, and from the inside by DeferredRouteTests.
//      THE ORIGIN OF THAT 401 IS PUBLISHED SEPARATELY FROM ITS ENUMERATION, because the two say
//      different things. gateway.v1.yaml carries an `x-cross-cutting-responses` declaration at document
//      level naming the status, the scheme that produces it, and the fact that it is evaluated before
//      any route handler; each guarded operation - these eight included - additionally enumerates the
//      response so that its own set is complete. A consumer needs both: the enumeration to generate a
//      client that can handle the status, and the origin declaration to know that receiving it says
//      nothing about whether the operation evaluated anything.
//
//  D6  WHAT THE `route` MEMBER CARRIES: the request path, and never the query string. Two contract
//      statements bear on it. The ReservedPath parameter says its value "is echoed in the `route`
//      field of the 501 body and is otherwise unused" (:L2657), and the member itself is described
//      as "The matched route pattern, echoed so a client can log what it asked for" (:L4568).
//      Echoing the request path satisfies both; echoing the literal template would make the first
//      statement false, because the parameter's value would then appear nowhere in the body. It is
//      also the only member of the schema declared without a `const` or an `enum` by an author who
//      used both everywhere else, which is how a varying member is spelled. The query string is
//      excluded: it is not part of a route, and a caller who mistakenly placed a credential in one
//      must not have it reflected back.
//
//  D7  ONE TABLE, ONE HELPER, FOUR IDENTICAL DECLARATIONS. Structural symmetry here is a compliance
//      property rather than a tidiness preference (:L2162-L2169): if one of the four ever acquired
//      more shape than the others - a request schema, a second status, an extra member - that
//      asymmetry would itself be the evidence that capability modelling had crept in. Driving all
//      four from a single table through a single mapping helper makes divergence impossible to
//      introduce accidentally, and makes diffing them against each other a meaningful audit.
//
//  D8  NO CONSTANT IS DECLARED THAT BELONGS TO THE LEGACY CATALOGUE. The repository-root
//      .editorconfig scopes its naming-analyzer suppressions to a fixed list of files that carry
//      preserved SCREAMING_SNAKE legacy identifiers, and no Gateway file is on that list, while
//      Directory.Build.props sets TreatWarningsAsErrors. RetCode.E_NO_IMPLEMENTATION is therefore
//      consumed from the shared kernel and never redeclared, which is also the correct design: one
//      catalogue, transcribed once from the oracle.
//
//  NO PERFORMANCE CLAIM IS MADE OR IMPLIED by anything in this file. The repository publishes no
//  service-level agreement, no latency budget, no throughput target and no availability commitment,
//  so none may be asserted.
// ==============================================================================================

using System.Net.Mime;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.OpenApi;
using PowerFramework.Shared.Kernel;

namespace PowerFramework.Gateway.Endpoints;

/// <summary>
/// Declares the four reserved Phase 2 extension points on Gateway's route table.
/// </summary>
/// <remarks>
/// <para>
/// Each declaration answers <c>501 Not Implemented</c> with a machine-readable
/// <see cref="ReservedRouteBody"/> naming the deferred service the route will eventually reach and
/// carrying the marker <c>reserved for Phase 2</c>. Nothing is implemented behind any of them: there
/// is no service directory, no project file, no container definition, no test project, no partial
/// implementation and no exception-throwing placeholder class for DesignSystem, Documents,
/// Integration or ScriptBridge.
/// </para>
/// <para>
/// The routes are declared so that the shape of the eventual system is legible from this service's
/// published contract. The prohibition they are held to is on <em>implementing</em> the deferred
/// services, and a route-table entry implements nothing - it has no handler beyond the constant body,
/// it calls nothing, and it can reach nothing.
/// </para>
/// <para>
/// All four are generated from one table by one helper, so they cannot drift apart. An asymmetry
/// between them would itself be evidence that capability modelling had crept in, which is why
/// diffing them against one another is a meaningful audit.
/// </para>
/// </remarks>
public static class DeferredCapabilityEndpoints
{
    /// <summary>
    /// The marker the contract fixes, verbatim, as a constant on every reserved response body.
    /// </summary>
    private const string ReservedMarker = "reserved for Phase 2";

    /// <summary>
    /// The OpenAPI tag grouping the reserved operations, matching the tag declared by the contract.
    /// </summary>
    private const string ReservedTag = "Reserved";

    /// <summary>
    /// The identifier of the REST ingress contract these declarations belong to.
    /// </summary>
    private const string ReservedContractId = "C-09";

    /// <summary>
    /// The media type the contract declares for the reserved response body. See decision D1: the
    /// reserved body is a declaration with a fixed shape, not a problem document.
    /// </summary>
    private const string ReservedMediaType = "application/json";

    /// <summary>The media type every refusal on this service carries.</summary>
    /// <remarks>
    /// Spelled here rather than taken from <c>MediaTypeNames.Application.ProblemJson</c> so that this file
    /// keeps its existing four using directives and its media types read from one place; the value is the
    /// same, and <c>gateway.v1.yaml</c>'s shared <c>Unauthorized</c> response declares exactly it.
    /// </remarks>
    private const string ProblemMediaType = "application/problem+json";

    /// <summary>
    /// Operation-identifier prefix, so that a stem composed with the deferred service name reproduces
    /// the contract's own spelling - for example <c>reserved</c> + <c>DesignSystem</c> + <c>Post</c>,
    /// and <c>reserved</c> + <c>DesignSystem</c> with NO suffix for the GET.
    /// </summary>
    private const string OperationIdPrefix = "reserved";

    /// <summary>
    /// The OpenAPI specification extensions each reserved operation carries in the published contract.
    /// </summary>
    private const string ContractIdExtension = "x-contract-id";

    /// <inheritdoc cref="ContractIdExtension"/>
    private const string DeferredServiceExtension = "x-deferred-service";

    /// <inheritdoc cref="ContractIdExtension"/>
    private const string ReservedMarkerExtension = "x-reserved-marker";

    /// <summary>
    /// The name of the catch-all route parameter every reserved template carries, and therefore also
    /// the name of the path parameter the generated document declares for it.
    /// </summary>
    /// <remarks>
    /// Every template in <see cref="ReservedDeclarations"/> is composed with this constant rather than
    /// spelling the parameter name again, so the route template and the declared parameter cannot
    /// drift apart.
    /// </remarks>
    private const string ReservedPathParameter = "path";

    /// <summary>
    /// The description the published contract gives the reserved path parameter.
    /// </summary>
    private const string ReservedPathParameterDescription =
        "The remainder of the requested path. Present so that each reserved entry is a route FAMILY "
        + "rather than a single route, which is what makes the eventual URL shape of the deferred "
        + "service legible from this contract. Its value is echoed in the route field of the 501 body "
        + "and is otherwise unused, because nothing exists behind the route to use it. "
        + "The server matches it as an ASP.NET Core catch-all route parameter, so the captured value "
        + "may itself contain '/' and may be empty; OpenAPI 3.1 has no conformant way to declare that, "
        + "so it is stated in the x-catch-all extension on this parameter rather than implied. Treat "
        + "the value as an opaque remainder and do not encode its separators.";

    /// <summary>
    /// The specification-extension name carrying the catch-all matching behaviour OpenAPI 3.1 cannot
    /// express on a path parameter. See decision D2.
    /// </summary>
    private const string CatchAllExtension = "x-catch-all";

    /// <summary>
    /// The two verbs the contract declares as operations on every reserved path, paired with the
    /// suffix that completes each operation identifier.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Mapped one at a time rather than as a single two-verb endpoint, because the contract gives the
    /// two operations distinct identifiers and one endpoint can carry only one name. See decision D3.
    /// </para>
    /// <para>
    /// THE GET SUFFIX IS EMPTY, AND THAT ASYMMETRY IS A COMPATIBILITY OBLIGATION RATHER THAN AN
    /// OVERSIGHT. The GET operation was published first, in this contract's `v1`, as
    /// <c>reservedDesignSystem</c> and its three siblings. When the POST was added a later revision
    /// suffixed BOTH verbs, which renamed four already-published operations - and an operationId is not
    /// decoration: it is the method name a generated client exposes, so four method names disappeared
    /// from every regenerated client under a version number promising nothing had changed. The original
    /// identifiers are therefore restored and only the NEW operation carries a suffix. The result reads
    /// slightly irregular and is correct; a symmetric pair would be a break.
    /// </para>
    /// </remarks>
    private static readonly (string Method, string OperationIdSuffix)[] DeclaredMethods =
    [
        (HttpMethods.Get, ""),
        (HttpMethods.Post, "Post"),
    ];

    /// <summary>
    /// Every other standard HTTP method, mapped to the same answer so that no verb reports 405 and
    /// therefore no verb appears implemented. Disjoint from <see cref="DeclaredMethods"/>, so the
    /// mappings on one template cannot produce an ambiguous match.
    /// </summary>
    private static readonly string[] UndeclaredMethods =
    [
        HttpMethods.Put,
        HttpMethods.Patch,
        HttpMethods.Delete,
        HttpMethods.Head,
        HttpMethods.Options,
        HttpMethods.Trace,
        HttpMethods.Connect,
    ];

    /// <summary>
    /// The four reserved declarations, in the contract's own order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Exactly four rows. A fifth would mean a service had been added to the roster without being
    /// deferred, and a missing row would mean an extension point had been dropped and the eventual
    /// topology had become invisible from the contract. Both are contract changes.
    /// </para>
    /// <para>
    /// The capability sentence on each row is naming, which is permitted and is the whole reason the
    /// route is declared. It is taken from the published contract and from docs/DEFERRED.md §5.1, and
    /// it is the only per-row datum besides the path and the service name - deliberately, because
    /// anything further would begin to describe a capability that must not be modelled.
    /// </para>
    /// </remarks>
    private static readonly ReservedRouteDeclaration[] ReservedDeclarations =
    [
        new(
            "/v1/design/{**" + ReservedPathParameter + "}",
            "DesignSystem",
            "theming, geometry structures, colour functions, the DPI conversion family, canvas, "
                + "painter, font, image, image list, popup menu, tooltip, tray icon, timer, win32 "
                + "interop, the logo control, and the presentational halves of ColumnSort, "
                + "ContextMenu and DropDownSearch."),
        new(
            "/v1/documents/{**" + ReservedPathParameter + "}",
            "Documents",
            "JSON and its helpers, the XML object family, ZIP, barcode and QR, file scanning, "
                + "logging, and the date/number conversion set."),
        new(
            "/v1/integration/{**" + ReservedPathParameter + "}",
            "Integration",
            "the HTTP client and its extensions, FTP, WebSocket, MQTT, and the pfwx.* transports."),
        new(
            "/v1/scripting/{**" + ReservedPathParameter + "}",
            "ScriptBridge",
            "Sciter and its extensions, MiniBlink, WebView embedding, the PowerScript compiler and "
                + "evaluator, dynamic object and script invocation, and global-variable access."),
    ];

    /// <summary>
    /// Declares the four reserved Phase 2 route families on the supplied route builder.
    /// </summary>
    /// <param name="endpoints">The route builder to declare the reserved families on.</param>
    /// <returns>
    /// The same <paramref name="endpoints"/> instance, so the call composes with the other endpoint
    /// declarations at the composition root.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="endpoints"/> is <see langword="null"/>. A composition root that
    /// reached this method without a route builder is structurally broken, and the legacy framework's
    /// posture for a structural fault is to fail immediately rather than degrade.
    /// </exception>
    /// <remarks>
    /// <para>
    /// This is the only public entry point in the file. Route definitions live here rather than at the
    /// composition root so that the reserved surface is described in exactly one place and can be
    /// reviewed as a unit.
    /// </para>
    /// <para>
    /// Every declaration it produces requires a bearer token, so an anonymous caller is answered by
    /// the authentication middleware and cannot enumerate the deferred roster.
    /// </para>
    /// </remarks>
    public static IEndpointRouteBuilder MapDeferredCapabilityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        foreach (ReservedRouteDeclaration declaration in ReservedDeclarations)
        {
            // Two described operations per family, exactly as the contract declares them, and one
            // undescribed mapping carrying every other verb to the identical answer.
            foreach ((string method, string operationIdSuffix) in DeclaredMethods)
            {
                MapDeclaredMethod(endpoints, declaration, method, operationIdSuffix);
            }

            MapUndeclaredMethods(endpoints, declaration);
        }

        return endpoints;
    }

    /// <summary>
    /// Declares one of the two operations the contract publishes for a reserved family.
    /// </summary>
    /// <param name="endpoints">The route builder to declare the operation on.</param>
    /// <param name="declaration">The reserved family being declared.</param>
    /// <param name="httpMethod">The single HTTP method this operation is declared for.</param>
    /// <param name="operationIdSuffix">
    /// The suffix completing the operation identifier, so that the prefix, the deferred service name
    /// and this suffix compose to the contract's own spelling. EMPTY for the GET, which was published
    /// first and keeps its original unsuffixed identifier - see <see cref="DeclaredMethods"/>.
    /// </param>
    /// <remarks>
    /// One method per call, because the contract gives the two operations distinct identifiers and an
    /// endpoint carries only one name; a two-verb endpoint would emit the same identifier twice.
    /// </remarks>
    private static void MapDeclaredMethod(
        IEndpointRouteBuilder endpoints,
        ReservedRouteDeclaration declaration,
        string httpMethod,
        string operationIdSuffix)
    {
        string deferredService = declaration.DeferredService;

        endpoints
            .MapMethods(
                declaration.RoutePattern,
                [httpMethod],
                (HttpContext httpContext) => ReservedResponse(httpContext, deferredService))
            .WithName(OperationIdPrefix + deferredService + operationIdSuffix)
            .WithTags(ReservedTag)
            .WithSummary($"Reserved for the deferred {deferredService} service. Always 501.")
            .WithDescription(BuildOperationDescription(declaration))

            // The two declared responses, both explicit rather than inferred. See decision D4: 501 is
            // the only outcome the handler computes and 401 is the pre-handler refusal the bearer
            // requirement below guarantees, so a generated client and a conformance tool both see a
            // complete set. No 2xx and no other 4xx may appear. No Accepts call appears anywhere in
            // this file, so the operation declares no request body - a request schema would model a
            // deferred capability, which is the one thing a routing declaration must not do.
            .Produces<ReservedRouteBody>(StatusCodes.Status501NotImplemented, ReservedMediaType)
            .ProducesProblem(StatusCodes.Status401Unauthorized, MediaTypeNames.Application.ProblemJson)

            // AND THE REFUSAL THAT PRECEDES IT, which the authored contract has always declared and the
            // runtime metadata omitted. gateway.v1.yaml gives each of these eight operations exactly two
            // responses - `Unauthorized` and `ReservedForPhaseTwo` - so a document generated without this
            // line disagreed with the specification it is meant to project, and a caller reading the
            // generated document saw a route that could only ever answer 501 while an untokened request
            // actually got 401.
            //
            // THIS IS NOT THE SECOND STATUS D4 WARNS ABOUT. D4's concern is a status that would imply the
            // route EVALUATES something before answering; a 401 implies the opposite, because it is
            // written by the authentication middleware and the handler is never reached - which is
            // precisely the property decision D5 below exists to create, so declaring it makes the
            // reserved roster's non-enumerability legible instead of leaving it to be inferred.
            //
            // A 403 IS DELIBERATELY NOT DECLARED, and its absence is asserted rather than assumed. These
            // routes require authentication and NO SCOPE: they reach no capability by construction, so a
            // scope would be a permission over a feature that does not exist, and requiring one would
            // turn the published 501 into a 403 and hide the shape of the eventual system these routes
            // exist to publish.
            .ProducesProblem(StatusCodes.Status401Unauthorized, ProblemMediaType)

            // Specification extensions, so the generated document names the deferred service in the
            // same place and with the same spelling as the authored contract does.
            .AddOpenApiOperationTransformer((operation, _, _) =>
                ApplyReservedOperationMetadata(operation, deferredService))

            // Decision D5. An anonymous request is answered by the authentication middleware and
            // never reaches the handler, so the reserved roster is not anonymously enumerable.
            .RequireAuthorization();
    }

    /// <summary>
    /// Maps every remaining standard HTTP method for a reserved family onto the identical answer,
    /// without adding either to the described surface.
    /// </summary>
    /// <param name="endpoints">The route builder to declare the mapping on.</param>
    /// <param name="declaration">The reserved family being declared.</param>
    /// <remarks>
    /// <para>
    /// The contract requires that every other method answer the same 501 so that no verb appears
    /// implemented. Without this mapping an unlisted verb would answer 405, which is a different
    /// statement about the route.
    /// </para>
    /// <para>
    /// It is excluded from the description because the contract publishes exactly two operations per
    /// path; the described surface must match that, while the observable behaviour stays uniform
    /// across verbs. The handler is the same one the declared methods use - there is one answer per
    /// family, not one per verb.
    /// </para>
    /// </remarks>
    private static void MapUndeclaredMethods(
        IEndpointRouteBuilder endpoints,
        ReservedRouteDeclaration declaration)
    {
        string deferredService = declaration.DeferredService;

        endpoints
            .MapMethods(
                declaration.RoutePattern,
                UndeclaredMethods,
                (HttpContext httpContext) => ReservedResponse(httpContext, deferredService))
            .ExcludeFromDescription()
            .RequireAuthorization();
    }

    /// <summary>
    /// Produces the single, constant answer every reserved route gives.
    /// </summary>
    /// <param name="httpContext">The current request, read only for the path echoed back.</param>
    /// <param name="deferredService">The deferred service this family names.</param>
    /// <returns>A <c>501 Not Implemented</c> result carrying a <see cref="ReservedRouteBody"/>.</returns>
    /// <remarks>
    /// <para>
    /// This is the whole of what exists behind a reserved route. It inspects nothing about the request
    /// except the path it echoes, it branches on nothing, it calls nothing, and it reaches nothing. A
    /// request body, if one was sent, is neither bound nor read - modelling one would model a deferred
    /// capability.
    /// </para>
    /// <para>
    /// The declared return type is <see cref="IResult"/> rather than a concrete result type on purpose;
    /// see decision D4.
    /// </para>
    /// </remarks>
    private static IResult ReservedResponse(HttpContext httpContext, string deferredService) =>
        Results.Json(
            new ReservedRouteBody
            {
                Status = StatusCodes.Status501NotImplemented,

                // BOTH SERVICE MEMBERS CARRY THE SAME VALUE, ON PURPOSE. `service` is the member this
                // contract published for v1 first; `deferredService` is the unambiguous spelling added
                // later. Emitting both keeps a consumer written against the original contract working
                // without asking new code to use an ambiguous name. See ReservedRouteBody.
                Service = deferredService,
                DeferredService = deferredService,
                Marker = ReservedMarker,

                // Decision D6: the requested path, never the query string.
                Route = httpContext.Request.Path.ToUriComponent(),

                // The legacy framework's own not-implemented code, consumed as a symbol from the
                // single transcription of retcode.sru and never written here as a literal.
                RetCode = RetCode.E_NO_IMPLEMENTATION,
            },
            statusCode: StatusCodes.Status501NotImplemented,
            contentType: ReservedMediaType);

    /// <summary>
    /// Composes the operation description from the shared statement plus the family's capability
    /// sentence, so that the four descriptions differ only where the contract's four differ.
    /// </summary>
    /// <param name="declaration">The reserved family being described.</param>
    /// <returns>The description published for both of the family's declared operations.</returns>
    private static string BuildOperationDescription(ReservedRouteDeclaration declaration) =>
        $"""
        **Reserved for Phase 2. Not implemented.**

        This route family will eventually reach the deferred {declaration.DeferredService} service:
        {declaration.Capabilities}

        Naming the capability area is permitted metadata - it is what makes the shape of the eventual
        system legible from this contract, which is the only reason the route is declared.
        Implementing any part of it is not, and nothing here does.

        Nothing exists behind this route: no service directory, no project file, no container
        definition, no test project, no partial implementation and no exception-throwing placeholder
        class. It has no handler beyond the constant body, it calls nothing, and it can reach nothing.

        Every HTTP method answers this same 501 and none accepts a request body, so no verb appears
        implemented. A token is nonetheless required - this operation does not override the
        document-level bearer requirement - because an unauthenticated caller must not be able to
        enumerate the deferred roster.
        """;

    /// <summary>
    /// Attaches the contract's three specification extensions to a generated reserved operation.
    /// </summary>
    /// <param name="operation">The operation being generated for a reserved route.</param>
    /// <param name="deferredService">The deferred service this family names.</param>
    /// <returns>A completed task; the transformer contract is asynchronous, this work is not.</returns>
    /// <remarks>
    /// Metadata only. It names the destination and marks the route reserved, which is exactly what
    /// makes the eventual system legible, and it changes nothing about what the route does. The
    /// deferred-service extension deliberately carries the same value as the body's
    /// <see cref="ReservedRouteBody.DeferredService"/> member, so the routing metadata and the wire
    /// member read the same.
    /// </remarks>
    private static Task ApplyReservedOperationMetadata(
        OpenApiOperation operation,
        string deferredService)
    {
        operation.Extensions ??= new Dictionary<string, IOpenApiExtension>(StringComparer.Ordinal);
        operation.Extensions[ContractIdExtension] = StringExtension(ReservedContractId);
        operation.Extensions[DeferredServiceExtension] = StringExtension(deferredService);
        operation.Extensions[ReservedMarkerExtension] = StringExtension(ReservedMarker);

        DeclareReservedPathParameter(operation);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Declares the reserved path parameter on a generated operation, matching the published contract.
    /// </summary>
    /// <param name="operation">The operation being generated for a reserved route.</param>
    /// <remarks>
    /// <para>
    /// OpenAPI requires every templated path segment to have a corresponding parameter declaration, and
    /// the generated description does not surface a catch-all route parameter that no handler argument
    /// binds. No handler argument binds this one, deliberately - nothing behind the route uses the
    /// remainder - so the declaration is supplied here to keep the generated document valid and to make
    /// it read the same as the authored contract.
    /// </para>
    /// <para>
    /// Metadata only, exactly like the extensions above: declaring a PATH parameter says how the URL is
    /// templated. It is not a request schema, which is the thing that would model a deferred capability,
    /// and no operation in this file declares one.
    /// </para>
    /// <para>
    /// THE CATCH-ALL BEHAVIOUR IS CARRIED BY A VENDOR EXTENSION, NOT BY <c>allowReserved</c>. An earlier
    /// revision set <c>AllowReserved</c> here to stand in for the fact that the captured value may
    /// contain <c>/</c>. That emitted <c>allowReserved</c> on an <c>in: path</c> parameter, which OpenAPI
    /// 3.1 defines for <c>in: query</c> parameters only - so the generated document failed validation
    /// while still not expressing catch-all semantics, because a path parameter matches a single segment
    /// whatever that field says. The extension below states the behaviour instead, and matches the
    /// <c>x-catch-all</c> block the authored contract carries on the same parameter. See decision D2.
    /// </para>
    /// </remarks>
    private static void DeclareReservedPathParameter(OpenApiOperation operation)
    {
        operation.Parameters ??= [];

        bool alreadyDeclared = operation.Parameters.Any(
            parameter => string.Equals(parameter.Name, ReservedPathParameter, StringComparison.Ordinal));

        if (alreadyDeclared)
        {
            return;
        }

        OpenApiParameter parameter = new()
        {
            Name = ReservedPathParameter,
            In = ParameterLocation.Path,
            Required = true,
            Description = ReservedPathParameterDescription,
            Schema = new OpenApiSchema { Type = JsonSchemaType.String },
            Extensions = new Dictionary<string, IOpenApiExtension>(StringComparer.Ordinal)
            {
                [CatchAllExtension] = CatchAllExtensionValue(),
            },
        };

        operation.Parameters.Add(parameter);
    }

    /// <summary>
    /// Builds the <c>x-catch-all</c> extension value describing how the reserved templates actually
    /// match, in the same shape the authored contract publishes.
    /// </summary>
    /// <returns>The extension value, ready to attach to the generated path parameter.</returns>
    /// <remarks>
    /// Four statements, each of which OpenAPI 3.1 leaves unsayable on a path parameter: the ASP.NET Core
    /// route template, that the captured value spans nested segments, that it matches an empty
    /// remainder so the bare prefix resolves here, and that every HTTP method answers identically even
    /// though the described surface enumerates two. All four are facts about URL templating; none of
    /// them describes a capability.
    /// </remarks>
    private static JsonNodeExtension CatchAllExtensionValue() =>
        new(new JsonObject
        {
            ["routeTemplate"] = JsonValue.Create("/v1/{area}/{**" + ReservedPathParameter + "}"),
            ["capturesNestedSegments"] = JsonValue.Create(true),
            ["matchesEmptyRemainder"] = JsonValue.Create(true),
            ["matchesEveryHttpMethod"] = JsonValue.Create(true),
        });

    /// <summary>
    /// Wraps a non-null string as an OpenAPI specification extension value.
    /// </summary>
    /// <param name="value">The extension value; always a constant or a table member here.</param>
    /// <returns>The wrapped value, ready to be attached to an operation.</returns>
    private static JsonNodeExtension StringExtension(string value) =>
        // JsonValue.Create is annotated to return null only for a null input. Every argument reaching
        // this method is a non-null constant or a non-null table member, so the result cannot be null.
        new(JsonValue.Create(value)!);

    /// <summary>
    /// One row of the reserved route table: a path template, the deferred service it names, and the
    /// capability sentence published for it.
    /// </summary>
    /// <param name="RoutePattern">
    /// The catch-all template for the family, so the prefix and everything beneath it resolve here
    /// rather than falling through to a generic 404. See decision D2.
    /// </param>
    /// <param name="DeferredService">
    /// The deferred service the family will eventually reach. Also the stem of both operation
    /// identifiers and the value of the body's machine-readable service member.
    /// </param>
    /// <param name="Capabilities">
    /// The capability area, named and nothing more. Naming the destination is what makes the eventual
    /// system legible; anything beyond the name would begin to describe a capability that must not be
    /// modelled.
    /// </param>
    private sealed record ReservedRouteDeclaration(
        string RoutePattern,
        string DeferredService,
        string Capabilities);
}

/// <summary>
/// The <c>501</c> body every reserved Phase 2 route returns.
/// </summary>
/// <remarks>
/// <para>
/// Machine-readable rather than a text message, so a client branches on structured members instead of
/// parsing prose - which is the whole point of declaring the routes: the shape of the eventual system
/// is legible from the contract while nothing is implemented behind it.
/// </para>
/// <para>
/// Five of the six members are constants fixed by the contract; only <see cref="Route"/> varies. The
/// member names are pinned explicitly rather than left to a serializer naming policy, so the wire
/// shape cannot drift if the composition root's JSON options change.
/// </para>
/// <para>
/// TWO MEMBERS NAME THE DEFERRED SERVICE AND THEY CARRY THE IDENTICAL VALUE. <see cref="Service"/> is
/// the member this contract published for <c>v1</c> first. <see cref="DeferredService"/> was introduced
/// later for a real reason - <c>service</c> already names the responding service on the ping body and an
/// upstream service on the health body, and a third meaning on the same word makes this body ambiguous
/// in exactly the place a client branches on it - and the revision that introduced it also REMOVED
/// <c>service</c>. That was a silent break of an unversioned wire member: every <c>v1</c> consumer
/// reading <c>service</c> stopped seeing the deferred-service name, under a version number promising it
/// had not changed. Both are therefore emitted. New code should read <see cref="DeferredService"/>;
/// retiring <see cref="Service"/> is a <c>v2</c> decision rather than a tidying one.
/// </para>
/// <para>
/// The body carries the deferred service's name, the reserved marker and the legacy return code, and
/// nothing else. It discloses no port, no host, no internal address and no configuration value: an
/// eventual destination is named, never located.
/// </para>
/// </remarks>
public sealed record ReservedRouteBody
{
    /// <summary>
    /// Always <c>501</c>. Repeated in the body so that a logged body is self-describing.
    /// </summary>
    [JsonPropertyName("status")]
    public required int Status { get; init; }

    /// <summary>
    /// The deferred service this route will eventually reach, under the member name this contract
    /// published for <c>v1</c> first: <c>DesignSystem</c>, <c>Documents</c>, <c>Integration</c> or
    /// <c>ScriptBridge</c>.
    /// </summary>
    /// <remarks>
    /// RETAINED FOR WIRE COMPATIBILITY, NOT DUPLICATED BY ACCIDENT. It carries the identical value as
    /// <see cref="DeferredService"/>. A consumer written against the original <c>v1</c> body reads this
    /// member, and removing it - which a later revision did - broke those consumers without a version
    /// change to signal it. New code should prefer <see cref="DeferredService"/>, whose name cannot be
    /// confused with the responding service on the ping body or the upstream service on the health body.
    /// </remarks>
    [JsonPropertyName("service")]
    public required string Service { get; init; }

    /// <summary>
    /// The deferred service this route will eventually reach: <c>DesignSystem</c>, <c>Documents</c>,
    /// <c>Integration</c> or <c>ScriptBridge</c>.
    /// </summary>
    /// <remarks>
    /// The name is the whole of what this member carries, and that is the point. docs/DEFERRED.md is
    /// the authoritative roster and records which legacy objects each deferred service is assigned.
    /// The unambiguous spelling, and the one new code should read; <see cref="Service"/> beside it is
    /// the same value under the originally published name.
    /// </remarks>
    [JsonPropertyName("deferredService")]
    public required string DeferredService { get; init; }

    /// <summary>
    /// The literal marker <c>reserved for Phase 2</c>, a constant rather than free text so that a
    /// conformance test and a client can both match on it.
    /// </summary>
    [JsonPropertyName("marker")]
    public required string Marker { get; init; }

    /// <summary>
    /// The requested path, echoed so a client can log what it asked for.
    /// </summary>
    /// <remarks>
    /// The path only. The query string is excluded, both because it is not part of a route and because
    /// a caller who mistakenly placed a credential in one must not have it reflected back.
    /// </remarks>
    [JsonPropertyName("route")]
    public required string Route { get; init; }

    /// <summary>
    /// <c>E_NO_IMPLEMENTATION</c>, which the legacy framework declares as <c>-2001</c>
    /// [<c>ws_objects/pfw.shared.pbl.src/retcode.sru:L78</c>].
    /// </summary>
    /// <remarks>
    /// The legacy framework's own not-implemented code rather than a parallel vocabulary invented for
    /// this boundary, so a client that already branches on a return code handles a reserved route with
    /// the code it knows. Its neighbour <c>E_NO_SUPPORT</c> (-2000) is a different statement - "this
    /// framework does not support that" rather than "this is not implemented here" - and is
    /// deliberately not used. Typed as a 64-bit integer because the legacy declarations are
    /// <c>Constant Long</c> and the contract declares the member as <c>int64</c>.
    /// </remarks>
    [JsonPropertyName("retCode")]
    public required long RetCode { get; init; }
}
