// ======================================================================================================
//  CapabilityEndpoints - GET /v1/capabilities, the legacy eight bit module gate projected over REST
//  ----------------------------------------------------------------------------------------------------
//  CONTRACT      shared/PowerFramework.Contracts/OpenApi/gateway.v1.yaml is AUTHORITATIVE FOR THE WIRE,
//                and every wire fact below was taken from it rather than from prose:
//
//                    path                /v1/capabilities                        [gateway.v1.yaml:L446]
//                    method              GET, and only GET                       [gateway.v1.yaml:L447]
//                    operationId         getCapabilities                         [gateway.v1.yaml:L448]
//                    contract            C-09                                    [gateway.v1.yaml:L449]
//                    tag                 Capabilities                            [gateway.v1.yaml:L450]
//                    200                 CapabilityReport                        [gateway.v1.yaml:L509-L513]
//                    401                 responses/Unauthorized, problem+json    [gateway.v1.yaml:L514-L515]
//                    authorization       the DOCUMENT level bearer requirement   [gateway.v1.yaml:L347-L348]
//
//  LEGACY ORACLE ws_objects/pfw.shared.pbl.src/enums.sru:L40-L49    the eight bits and the composite
//                ws_objects/pfw.pbl.src/pfw.sra:L91, :L108          the mask's only legacy call site,
//                                                                   paired with its finalize call
//                docs/README.md:L9, :L15, :L26, :L28, :L30          what a bit actually controls
//
//                Every one of those paths is READ ONLY. They are the behavioural oracle for parity
//                testing and never an edit target, so each assertion in this file carries the locator
//                that settles it and nothing here was inferred from an identifier's name.
//
//  WHY THIS ENDPOINT EXISTS AT ALL (C-K)
//  ----------------------------------------------------------------------------------------------------
//  The legacy framework gates its own modules with a bitmask handed to pfwInitialize, and that bitmask
//  is the legacy's OWN statement of decomposition intent: it was written years before this refactor,
//  for its own reasons, and mapping its bits onto the Phase 1 service roster corroborates
//  independently that the four service slice is drawn along seams the framework's authors had already
//  recognised. Publishing it is how that intent stays legible from outside the process.
//
//  THE GATE IS CONFIGURATION, NOT A COMPILE TIME SWITCH, and that is the decision this endpoint makes
//  observable. docs/README.md:L26 states that a module which is not explicitly initialized has its
//  related functionality UNUSABLE and does not need its DLL shipped at all, and :L30 warns that asking
//  for a module whose DLL is absent makes initialization FAIL outright rather than degrade. The legacy
//  therefore already treated its capability set as a DEPLOYMENT TIME decision determining which
//  artifacts even need to be present, and docs/README.md:L9 records the flags argument as optional and
//  combinable. A hard wired set would have reproduced neither property.
//
//  WHAT THE PROJECTION MEANS, EXACTLY AND NO MORE
//  ----------------------------------------------------------------------------------------------------
//  An unset bit means the capability is unusable in this deployment and its native payload need not be
//  present. That is the whole of the published meaning. This endpoint deliberately reports NOTHING about
//  the host filesystem: no DLL presence probe, no file path, no directory listing, no environment
//  variable value, no upstream address and no internal topology (C-G). It answers "which capabilities
//  did configuration ask for", which is a question about configuration, not about a machine.
//
//  IT IS A READ ONLY PROJECTION AND OWNS NO LIFECYCLE
//  ----------------------------------------------------------------------------------------------------
//  docs/README.md:L15 carries an explicit warning that pfwFinalize must be PAIRED with pfwInitialize,
//  and that pairing is owned by Composition/FrameworkInitializer.cs, which reproduces it as host
//  startup and shutdown with fail fast validation. Nothing here initializes, finalizes, re-initializes
//  or mutates the mask, and no lifecycle logic is duplicated into this file. GET is the only verb the
//  contract declares for this path, there is no POST, no PUT, no PATCH, no DELETE and no
//  /v1/capabilities/{name} sub resource, because the gate is configuration and configuration is not
//  edited through the surface that reports it.
//
//  WHY IOptions AND NOT IOptionsSnapshot, AND WHY NOT THE INITIALIZER
//  ----------------------------------------------------------------------------------------------------
//  Three candidate sources exist for the mask, and the choice between them is behavioural rather than
//  stylistic:
//
//    IOptions<GatewayOptions>          CHOSEN. A singleton computed once, so this endpoint reports the
//                                      same mask for the whole process lifetime - which is exactly what
//                                      pfw.sra:L91 does by calling pfwInitialize once at open and
//                                      pairing it with pfwFinalize at close. It is also the source
//                                      FrameworkInitializer reads, so there is precisely one value in
//                                      the process and the report cannot contradict the initialization.
//
//    IOptionsSnapshot<GatewayOptions>  REJECTED. It re-evaluates per request scope, so a configuration
//                                      reload would let this endpoint report a mask the framework was
//                                      never initialized with. The legacy has no re-initialize at all,
//                                      so reload semantics here would be new behaviour (C-B) whose
//                                      failure mode is a plausible looking lie.
//
//    FrameworkInitializer.Capabilities REJECTED. It is the truthful runtime value, but reading it would
//                                      couple this endpoint to lifecycle STATE: before StartingAsync has
//                                      run it is None, which is indistinguishable from a configured zero
//                                      mask. Injecting it would also drag a hosted service into an
//                                      endpoint the constraint set requires to stay pure (C-H).
//
//  PURITY, WHICH IS WHAT MAKES THE COVERAGE GATE REACHABLE (C-H)
//  ----------------------------------------------------------------------------------------------------
//  Inject options, project, return. This file performs no I/O, opens no connection, reads no clock,
//  consults no environment variable, calls no upstream service, holds no static mutable state and
//  caches nothing. CapabilityFlags is already a pure function of one 32 bit mask, and this endpoint is
//  kept equally pure so that the whole surface is exercisable in process against a test host with no
//  live upstream and no fixture beyond configuration.
//
//  CONSUME, NEVER RE-DERIVE
//  ----------------------------------------------------------------------------------------------------
//  Composition/CapabilityFlags.cs is purpose built for this projection and is consumed rather than
//  duplicated. In particular THIS FILE CONTAINS NO BIT ARITHMETIC WHATSOEVER: no mask literal, no
//  shift, no and, no or, no not, no bit test. The enabled state of a capability is decided by set
//  membership in CapabilityFlags.EnabledCapabilityNames, the vocabulary and ordering come from
//  CapabilityFlags.KnownCapabilityNames, the numeric values come from the CapabilityFlags bit
//  constants, and the aggregate comes from CapabilityFlags.AllCapabilitiesMask. Going around any of
//  them would put a second, uncharacterized implementation of the gate in the tree.
//
//  NO CONSTANT IS DECLARED HERE - THE MECHANICAL FORM OF C-B
//  ----------------------------------------------------------------------------------------------------
//  The refactor preserves the legacy SCREAMING_SNAKE constant spellings verbatim, because those exact
//  strings appear in serialized payloads, log records and characterization recordings where a rename
//  would silently invalidate every stored comparison. The analyzer suppressions that make those
//  spellings buildable are glob scoped in the repository root .editorconfig to the files that DECLARE
//  them, and no file under services/gateway-service is on that list. With warnings treated as errors,
//  a single SCREAMING_SNAKE declaration here would be a BUILD ERROR rather than a style nit.
//
//  So this file declares none. Where a preserved identifier is needed it is obtained with nameof over
//  the shared catalogue constant, which is an expression and not a declaration, and which additionally
//  means renaming a catalogue constant breaks this file's compilation instead of quietly changing what
//  a wire payload says.
//
//  WIRE WIDTH IS int64; IN PROCESS WIDTH IS uint. BOTH ARE DELIBERATE
//  ----------------------------------------------------------------------------------------------------
//  gateway.v1.yaml declares every mask member as `type: integer, format: int64` with the domain
//  0 .. 4294967295, i.e. the full UNSIGNED 32 bit range carried in a signed 64 bit field. In process
//  the mask is uint, because the legacy parameter is `readonly unsignedlong flags`
//  [ws_objects/pfw.base.pbl.src/pfwinitialize.srf:L8] and PowerBuilder's unsignedlong is 32 bits wide.
//  Every numeric member of the payload below is therefore a long produced by WIDENING the uint, which
//  is exact for every one of the 2^32 values and can never be negative - so the schema's declared
//  minimum of 0 holds by construction rather than by validation.
//
//  WHAT THIS FILE DELIBERATELY DOES NOT CARRY
//  ----------------------------------------------------------------------------------------------------
//  Recorded so each absence reads as a decision rather than an oversight.
//
//    * No storage of any kind: no connection string, no database, no provider, no query (C-E). Note the
//      irony this respects - INIT_FLAG_ENABLE_SQLITE is the ONE capability with an in scope consumer in
//      this phase, and this endpoint still only REPORTS that bit. Reporting a capability's state is not
//      exercising it, and the service that owns storage is not this one.
//    * No secret, key, credential, token, claim or signing authority, and nothing of the kind in any
//      response body (C-F). The payload carries preserved capability identifiers, their numeric values,
//      booleans and the two masks. Nothing else.
//    * No upstream client, no typed client, no service namespace import and no call across any network
//      edge (C-A). This endpoint reaches nothing. The only coupling Gateway is permitted towards another
//      service is the published contracts project, and this operation does not even need that.
//    * No class, handler, interface, configuration entry or dependency injection registration for any
//      deferred capability, and not one of the four reserved Phase 2 route families either (C-D). Those
//      four are declared exclusively by Endpoints/DeferredCapabilityEndpoints.cs, so exactly one route is
//      registered from this file and its path is the single route constant below.
//    * No listening port and no host address. Where this service listens is host and orchestration
//      configuration, so no port literal appears here at all - neither this service's own, nor the slot
//      held in reserve for the deferred presentation service.
//    * No name for, and no reservation of, bit positions 4 through 7 - values 16, 32, 64 and 128. An
//      exhaustive search of the estate returns exactly nine INIT_FLAG_ identifiers, the eight bits plus
//      the composite, so nothing claims that range. This file invents no meaning for it, publishes no
//      placeholder entry in it, and speculates about it nowhere. A mask that happens to carry such a bit
//      is reported as data through unrecognizedBits and is neither rejected nor silently corrected.
//    * No latency, throughput, availability or performance claim, expressed or implied. The repository
//      publishes no such budget anywhere, so none may be asserted here or anywhere else.
//    * No dependency on the host entry point. The composition root calls the single mapping method
//      below; the edge runs from the composition root to here and never back, so this file names no
//      entry-point type, imports nothing from one, and holds no reference to one.
//
//  ONE RECONCILIATION, STATED OPENLY BECAUSE IT LOOKS LIKE A CONFLICT
//  ----------------------------------------------------------------------------------------------------
//  gateway.v1.yaml makes `phaseOneDestination` a REQUIRED member of every capability entry
//  [gateway.v1.yaml:L4457] and closes its value set to four tokens [gateway.v1.yaml:L4508-L4510], one of
//  which names the service that owns storage. Emitting that token is therefore a wire obligation, and
//  omitting the member would break the schema's own required list.
//
//  It is a wire ENUM VALUE and nothing more. There is no client for that service in this file, no
//  import of its namespace, no dependency injection registration, no configuration entry and no call to
//  it - Gateway does not reach it at all, DataServices does. The constraint that forbids reaching into
//  another service's internals is about COUPLING, and naming a destination in a payload creates none;
//  the destination is exactly what tells a caller that enabling a bit whose destination is deferred has
//  no runtime effect in this phase, which is the field's stated purpose.
// ======================================================================================================

using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using PowerFramework.Gateway.Authorization;
using PowerFramework.Gateway.Composition;
using PowerFramework.Gateway.Configuration;
using PowerFramework.Shared.Kernel;

namespace PowerFramework.Gateway.Endpoints;

/// <summary>
/// Registers and serves <c>GET /v1/capabilities</c>, the REST projection of the legacy framework's
/// eight-bit module-gating bitmask.
/// </summary>
/// <remarks>
/// <para>
/// The projected gate is declared at <c>ws_objects/pfw.shared.pbl.src/enums.sru:L41-L49</c> and its only
/// legacy consumer is <c>pfwInitialize(Enums.INIT_FLAG_ENABLE_ALL)</c> at
/// <c>ws_objects/pfw.pbl.src/pfw.sra:L91</c>. This type publishes that gate as a read-only resource: it
/// reports which capabilities configuration asked for and takes no action on any of them.
/// </para>
/// <para>
/// It is a pure projection. The mask arrives already bound, is handed to
/// <see cref="CapabilityFlags"/> unchanged, and is reported. No I/O is performed, no clock or
/// environment variable is read, no upstream service is called, no storage is touched and no state is
/// held, so the whole surface is exercisable in-process against a test host with no live upstream.
/// </para>
/// </remarks>
public static class CapabilityEndpoints
{
    /// <summary>
    /// The route the published contract declares for the capability projection.
    /// </summary>
    /// <remarks>
    /// Spelled exactly as <c>shared/PowerFramework.Contracts/OpenApi/gateway.v1.yaml:L446</c> spells it.
    /// The contract declares one verb for it, <c>GET</c>, and no sub-resource.
    /// </remarks>
    private const string CapabilitiesRoute = "/v1/capabilities";

    /// <summary>
    /// The scope an authenticated caller must have been granted to reach the capability projection.
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
    internal const string RequiredScope = "capabilities";

    /// <summary>
    /// The authorization policy name carrying <see cref="RequiredScope"/>, so that the route and the
    /// registration cannot drift apart.
    /// </summary>
    /// <remarks>
    /// Composed from <see cref="RequiredScope"/> rather than written out, and prefixed with the service
    /// name so that a policy name is never ambiguous in a log line that carries policies from more than
    /// one service.
    /// </remarks>
    internal const string ScopePolicyName = "gateway:scope:" + RequiredScope;

    /// <summary>
    /// The published operation identifier, <c>getCapabilities</c>
    /// [<c>gateway.v1.yaml:L448</c>].
    /// </summary>
    /// <remarks>
    /// Applied through <c>WithName</c> so that the document this service generates carries the same
    /// operation identifier as the hand-authored contract, which is what makes the two diffable.
    /// </remarks>
    private const string GetCapabilitiesOperationId = "getCapabilities";

    /// <summary>
    /// The published tag, <c>Capabilities</c> [<c>gateway.v1.yaml:L450</c>], whose contract is C-09.
    /// </summary>
    private const string CapabilitiesTag = "Capabilities";

    /// <summary>
    /// The published operation summary, carried verbatim from <c>gateway.v1.yaml:L451</c>.
    /// </summary>
    private const string GetCapabilitiesSummary =
        "Project the framework's eight-bit capability gate. Token required.";

    /// <summary>
    /// The operation description surfaced in the generated OpenAPI document.
    /// </summary>
    /// <remarks>
    /// A condensed restatement of <c>gateway.v1.yaml:L452-L508</c>. The hand-authored contract carries
    /// the full narrative and remains authoritative; this text exists so the generated document is
    /// self-describing without duplicating several screens of prose into a string literal, where it
    /// could drift from the contract unnoticed.
    /// </remarks>
    private const string GetCapabilitiesDescription =
        "Reports which of the framework's eight capability bits are enabled in this deployment. " +
        "A capability that is not enabled is unusable and its native payload need not be present. " +
        "The bits are carried by their preserved legacy identifiers, in declaration order. " +
        "INIT_FLAG_ENABLE_ALL is 3847 and not 3855: the legacy declares it as a seven-term sum that " +
        "deliberately omits INIT_FLAG_ENABLE_BLINKFAST, because the standard and fast engine binaries " +
        "are alternative builds of one engine, so enabling both is meaningless. " +
        "Only INIT_FLAG_ENABLE_SQLITE has an in-scope consumer in this phase; every other bit names a " +
        "capability area outside it, so enabling one reports configuration and grants nothing. " +
        "The gate is read-only here: it is reported, never initialized, re-initialized or mutated.";

    /// <summary>
    /// Registers the capability projection on the supplied endpoint route builder.
    /// </summary>
    /// <param name="endpoints">The route builder the composition root supplies.</param>
    /// <returns>
    /// <paramref name="endpoints"/>, so that registration can be chained. Never <see langword="null"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="endpoints"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// This is the only public entry point on the type, and the route definition lives here rather than
    /// in the composition root so that the contract for this path is stated in exactly one file.
    /// </para>
    /// <para>
    /// <b>Authorization is taken from the contract and not guessed.</b>
    /// <c>gateway.v1.yaml:L347-L348</c> declares a document-level bearer requirement, and
    /// <c>GET /health</c> is the only operation in the whole document that overrides it. This operation
    /// does not override it, so a token is required and the response without one is <c>401</c>. There is
    /// deliberately no environment-conditional variant of that posture: an anonymous variant "for
    /// development" would make the authenticated boundary a property of configuration rather than of the
    /// contract.
    /// </para>
    /// </remarks>
    public static IEndpointRouteBuilder MapCapabilityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet(CapabilitiesRoute, GetCapabilities)
                 .WithName(GetCapabilitiesOperationId)
                 .WithTags(CapabilitiesTag)
                 .WithSummary(GetCapabilitiesSummary)
                 .WithDescription(GetCapabilitiesDescription)
                 .ProducesProblem(StatusCodes.Status401Unauthorized)

                 // The 403 this route can now answer, declared where a reviewer diffing against the
                 // published contract will look for it. The scope policy below is what produces it: a
                 // valid token that does not carry `capabilities` is refused as INSUFFICIENT rather than
                 // as absent, which is the distinction the contract's own two statuses exist to make.
                 .ProducesProblem(StatusCodes.Status403Forbidden)

                 // A NAMED POLICY RATHER THAN THE PARAMETERLESS FORM. The parameterless call used to
                 // stand here, requiring only an authenticated principal - so any token addressed to this
                 // service could read the capability gate's projection, which tells a caller which of the
                 // framework's eight capability bits this deployment enabled. The named policy requires
                 // the authenticated principal AND the `capabilities` scope.
                 .RequireAuthorization(GatewayScopes.Capabilities);

        return endpoints;
    }

    /// <summary>
    /// Serves the capability projection.
    /// </summary>
    /// <param name="options">
    /// The bound Gateway configuration supplying the mask from <c>Gateway:CapabilityFlags</c>.
    /// </param>
    /// <returns>
    /// <c>200</c> carrying the <see cref="CapabilityReport"/>. The declared return type is what gives the
    /// generated OpenAPI document its typed success response, so no separate response-type annotation is
    /// needed and none is added - one declaration of the success shape rather than two that could drift.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// Deliberately <see cref="IOptions{TOptions}"/> rather than <see cref="IOptionsSnapshot{TOptions}"/>:
    /// the legacy fixes its capability set once, by the single
    /// <c>pfwInitialize(Enums.INIT_FLAG_ENABLE_ALL)</c> call at <c>ws_objects/pfw.pbl.src/pfw.sra:L91</c>
    /// paired with <c>pfwFinalize</c> at <c>:L108</c>, and has no re-initialize at all. A per-request
    /// re-evaluation could therefore report a mask the framework was never initialized with, which would
    /// be new behaviour whose failure mode is a plausible-looking lie. This is also the source
    /// <c>Composition/FrameworkInitializer.cs</c> reads, so exactly one mask exists in the process and
    /// this report cannot contradict the initialization that consumed it.
    /// </para>
    /// <para>
    /// There is no <c>try</c> block and no fallback, and that is a decision. Resolving the gate cannot
    /// fail on the value of a mask: <see cref="CapabilityFlags.FromConfiguredValue(long)"/> performs an
    /// unchecked narrowing and accepts every one of the 2^32 results, including zero, an all-bits-set
    /// mask and a mask carrying the unassigned bits, because the legacy native entry point validates
    /// nothing about the mask it receives either. Rejecting or repairing a mask here would be new
    /// behaviour dressed up as robustness.
    /// </para>
    /// </remarks>
    private static Ok<CapabilityReport> GetCapabilities(IOptions<GatewayOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        CapabilityFlags gate = CapabilityFlags.FromOptions(options.Value);

        return TypedResults.Ok(Project(gate));
    }

    /// <summary>
    /// Projects a resolved capability gate onto the published report shape.
    /// </summary>
    /// <param name="gate">The resolved gate. Every possible value is projectable.</param>
    /// <returns>The report. Never <see langword="null"/>.</returns>
    /// <exception cref="InvalidOperationException">
    /// The shared capability vocabulary no longer matches the published contract - either its size is not
    /// <see cref="CapabilityFlags.DeclaredCapabilityCount"/>, or it names a capability this contract does
    /// not describe.
    /// </exception>
    /// <remarks>
    /// <para>
    /// The ordering, the vocabulary and the enabled state all come from
    /// <see cref="CapabilityFlags"/> and none of them is re-derived here.
    /// <see cref="CapabilityFlags.KnownCapabilityNames"/> supplies all eight identifiers in
    /// <c>ws_objects/pfw.shared.pbl.src/enums.sru:L41-L48</c> declaration order, which is what makes the
    /// sparse bit layout legible in a payload and makes a given mask's output byte-for-byte
    /// reproducible - a property the characterization recordings depend on.
    /// </para>
    /// <para>
    /// <b>The enabled state is decided by set membership, not by bit arithmetic.</b>
    /// <see cref="CapabilityFlags.EnabledCapabilityNames"/> is the projection built for exactly this
    /// purpose, so this method performs no mask test of its own. That is deliberate rather than
    /// incidental: the shared bit helpers are the managed substitutes the legacy's own bit behaviour was
    /// characterized into, and a second implementation of the same operation in this file would be
    /// uncharacterized by construction. It is compared with
    /// <see cref="StringComparer.Ordinal"/> because these are legacy symbols, not human-readable text,
    /// and no culture may participate in matching them.
    /// </para>
    /// </remarks>
    private static CapabilityReport Project(CapabilityFlags gate)
    {
        IReadOnlyList<string> declaredNames = CapabilityFlags.KnownCapabilityNames;

        // FAIL FAST ON A VOCABULARY CHANGE, because the published array is closed at exactly eight
        // [gateway.v1.yaml:L4444-L4445, minItems and maxItems both 8]. A ninth or a seventh capability in
        // the shared catalogue is a change to the PUBLISHED CONTRACT, so it must break loudly here rather
        // than quietly emit an array of the wrong length that a schema validator would reject downstream.
        // This mirrors the framework's own posture: a structural fault stops the process rather than
        // degrading [ws_objects/pfw.pbl.src/pfw.sra:L111-L144].
        if (declaredNames.Count != CapabilityFlags.DeclaredCapabilityCount)
        {
            throw new InvalidOperationException(
                $"The shared capability vocabulary declares {declaredNames.Count} capabilities, but the " +
                $"published capability contract is closed at {CapabilityFlags.DeclaredCapabilityCount}. " +
                "The eight bits are fixed by ws_objects/pfw.shared.pbl.src/enums.sru:L41-L48, so a " +
                "different count means the ported catalogue and the published contract have diverged. " +
                "Reconcile shared/PowerFramework.Shared.Kernel/Enums.cs, " +
                "Composition/CapabilityFlags.cs and " +
                "shared/PowerFramework.Contracts/OpenApi/gateway.v1.yaml together.");
        }

        HashSet<string> enabledNames = new(gate.EnabledCapabilityNames, StringComparer.Ordinal);

        List<Capability> capabilities = new(declaredNames.Count);

        foreach (string capabilityName in declaredNames)
        {
            (long value, CapabilityDestination destination) = Describe(capabilityName);

            capabilities.Add(new Capability
            {
                Name = capabilityName,
                Value = value,
                Enabled = enabledNames.Contains(capabilityName),
                PhaseOneDestination = destination,
            });
        }

        return new CapabilityReport
        {
            EffectiveMask = gate.EffectiveMask,

            // ==========================================================================================
            //  THE AGGREGATE IS 3847. IT IS NOT 3855. THAT IS CORRECT AND IT IS NOT A TYPO.
            //  ----------------------------------------------------------------------------------------
            //  This is the point of reproduction, so the annotation lives here.
            //
            //  ws_objects/pfw.shared.pbl.src/enums.sru:L49 declares the aggregate as a SEVEN term sum
            //  over the EIGHT bits declared immediately above it at :L41-L48:
            //
            //      Constant Long INIT_FLAG_ENABLE_ALL = INIT_FLAG_ENABLE_UI        (1)
            //                                         + INIT_FLAG_ENABLE_SCITER    (2)
            //                                         + INIT_FLAG_ENABLE_BLINK     (4)
            //                                         + INIT_FLAG_ENABLE_ORCA      (256)
            //                                         + INIT_FLAG_ENABLE_SQLITE    (512)
            //                                         + INIT_FLAG_ENABLE_DPIAWARE  (1024)
            //                                         + INIT_FLAG_ENABLE_WEBVIEW   (2048)
            //
            //      1 + 2 + 4 + 256 + 512 + 1024 + 2048  =  3847
            //
            //  INIT_FLAG_ENABLE_BLINKFAST (8) IS DELIBERATELY ABSENT, and the reason is concrete:
            //  blink.dll and blinkfast.dll are two alternative BUILDS of one engine rather than two
            //  independent capabilities, so an "everything on" constant naming both at once would be
            //  incoherent. It looks like an oversight and it is not one.
            //
            //  ARRIVING AT 3855 MEANS THE EIGHTH BIT WAS FOLDED IN AND A DELIBERATE LEGACY DECISION
            //  REVERSED. That is a behavioural change, not the repair of a typo, and it is exactly what
            //  the replicate-verbatim constraint forbids. A future reader who "completes" this value
            //  breaks parity with the framework by precisely the BLINKFAST bit.
            //
            //  The value is obtained by CONSUMING CapabilityFlags.AllCapabilitiesMask, which itself
            //  consumes Enums.INIT_FLAG_ENABLE_ALL. Neither the total nor the seven-term sum is restated
            //  anywhere in this file: either would give the omission a second place to be got wrong, and
            //  a literal would additionally hide reasoning a later reader could only recover by opening
            //  PowerScript. Note that CapabilityFlags.KnownCapabilityMask - the union of all eight bits,
            //  which genuinely is 3855 - exists for an entirely different purpose and must never be used
            //  here.
            //
            //  Widened from uint to long because the wire member is declared `format: int64` with the
            //  unsigned 32-bit domain [gateway.v1.yaml:L4416-L4418]. The contract additionally declares
            //  it `const: 3847`, which the C# type system cannot express; consuming the single ported
            //  constant is what keeps that promise, and the parity test asserts it.
            // ==========================================================================================
            AllMask = CapabilityFlags.AllCapabilitiesMask,

            // Reported as DATA, never as a fault and never by correcting the mask. Bit positions 4
            // through 7 - values 16, 32, 64 and 128 - are unassigned in the legacy declaration, which
            // runs 1, 2, 4, 8 and then jumps to 256, and anything above 2048 is unclaimed as well. The
            // legacy accepts such a mask silently, so a configuration carrying a future or mistaken bit
            // starts and says so here instead of failing [gateway.v1.yaml:L4437-L4441]. No entry is
            // published for any of those positions, because no INIT_FLAG_ENABLE_ constant names one.
            UnrecognizedBits = gate.UnrecognizedBits,

            Capabilities = capabilities,
        };
    }

    /// <summary>
    /// Resolves the published numeric value and Phase-1 destination of one preserved capability
    /// identifier.
    /// </summary>
    /// <param name="capabilityName">
    /// A preserved legacy identifier from <see cref="CapabilityFlags.KnownCapabilityNames"/>.
    /// </param>
    /// <returns>The capability's numeric value and its Phase-1 destination.</returns>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="capabilityName"/> names a capability the published contract does not describe.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Each arm matches against <see langword="nameof"/> over the shared catalogue constant, which is a
    /// compile-time constant expression and therefore a legal constant pattern, and which is an
    /// expression rather than a declaration - so no preserved <c>SCREAMING_SNAKE</c> identifier is
    /// declared in this file and no analyzer suppression is needed or taken. Renaming a catalogue
    /// constant breaks compilation here instead of quietly changing what a wire payload says.
    /// </para>
    /// <para>
    /// The numeric values are consumed from the <see cref="CapabilityFlags"/> bit constants and widened
    /// from <see cref="uint"/> to <see cref="long"/> for the wire, so this file restates none of them.
    /// The eight are sparse - 1, 2, 4, 8, 256, 512, 1024, 2048 - rather than the first eight powers of
    /// two, and the gap is the legacy's own [<c>enums.sru:L41-L48</c>].
    /// </para>
    /// <para>
    /// The destinations are the Phase-1 mapping of <c>docs/ARCHITECTURE.md</c> §6.1, restated in the
    /// contract at <c>gateway.v1.yaml:L465-L474</c>. Exactly one of the eight resolves to a service that
    /// exists in this phase; the other seven name capability areas outside it, which is what tells a
    /// caller that enabling one of those bits reports configuration and has no runtime effect here. Each
    /// of those seven is a NAME in a payload and nothing else: no client, no handler, no interface, no
    /// configuration and no registration exists behind any of them, in this file or anywhere in this
    /// build graph.
    /// </para>
    /// </remarks>
    private static (long Value, CapabilityDestination Destination) Describe(string capabilityName)
    {
        return capabilityName switch
        {
            nameof(Enums.INIT_FLAG_ENABLE_UI) =>
                (CapabilityFlags.UiBit, CapabilityDestination.DesignSystem),

            nameof(Enums.INIT_FLAG_ENABLE_SCITER) =>
                (CapabilityFlags.SciterBit, CapabilityDestination.ScriptBridge),

            nameof(Enums.INIT_FLAG_ENABLE_BLINK) =>
                (CapabilityFlags.BlinkBit, CapabilityDestination.ScriptBridge),

            // Reported like any other bit even though no aggregate enables it by default, because the
            // report describes the CLOSED set of eight declared capabilities rather than the seven the
            // aggregate happens to name. Its state is simply false whenever the effective mask is the
            // aggregate - which is the preserved legacy quirk observable, not a gap to close.
            nameof(Enums.INIT_FLAG_ENABLE_BLINKFAST) =>
                (CapabilityFlags.BlinkFastBit, CapabilityDestination.ScriptBridge),

            // PowerBuilder packaging tooling, which is not a service at any phase - hence a destination
            // of its own rather than being folded into one of the deferred services.
            nameof(Enums.INIT_FLAG_ENABLE_ORCA) =>
                (CapabilityFlags.OrcaBit, CapabilityDestination.PackagingTooling),

            // THE ONLY BIT WITH AN IN-SCOPE CONSUMER IN THIS PHASE, and the consumer is not Gateway.
            // This arm names a destination on the wire and does nothing else: it opens no connection,
            // names no database, references no storage package and reaches no provider. Reporting a
            // capability's state is not exercising it.
            nameof(Enums.INIT_FLAG_ENABLE_SQLITE) =>
                (CapabilityFlags.SqliteBit, CapabilityDestination.Persistence),

            nameof(Enums.INIT_FLAG_ENABLE_DPIAWARE) =>
                (CapabilityFlags.DpiAwareBit, CapabilityDestination.DesignSystem),

            nameof(Enums.INIT_FLAG_ENABLE_WEBVIEW) =>
                (CapabilityFlags.WebViewBit, CapabilityDestination.ScriptBridge),

            // Unreachable while the shared catalogue and this contract agree, and deliberately fatal
            // rather than skipped: silently dropping an unrecognised capability would publish an array
            // shorter than the eight the contract requires, and inventing a destination for it would
            // fabricate a mapping no evidence supports.
            _ => throw new InvalidOperationException(
                $"'{capabilityName}' is not one of the eight capability identifiers declared at " +
                "ws_objects/pfw.shared.pbl.src/enums.sru:L41-L48 and described by " +
                "shared/PowerFramework.Contracts/OpenApi/gateway.v1.yaml. The published set is closed " +
                "because the legacy declaration is closed, so a new capability is a change to the " +
                "contract and must be added there before it can be projected."),
        };
    }
}

/// <summary>
/// The framework's eight-bit capability gate, projected as configuration. The <c>200</c> body of
/// <c>GET /v1/capabilities</c>.
/// </summary>
/// <remarks>
/// <para>
/// Declared in this file rather than in a type of its own because this folder's scope is a fixed set of
/// endpoint files: the constraint is one FILE per endpoint group, not one TYPE, so the shape a route
/// returns belongs beside the route that returns it.
/// </para>
/// <para>
/// Every member carries an explicit <see cref="JsonPropertyNameAttribute"/>. That is deliberate rather
/// than redundant: the published contract is authoritative for the wire, and pinning each name here
/// means the payload matches it whatever JSON naming policy the host happens to be configured with. A
/// name that agreed with the contract only because a default policy happened to align would be a
/// contract violation waiting for a configuration change.
/// </para>
/// <para>
/// The numeric members are <see cref="long"/> because the contract declares them
/// <c>type: integer, format: int64</c> over the unsigned 32-bit domain <c>0 .. 4294967295</c>. In
/// process the mask is <see cref="uint"/>, matching the <c>readonly unsignedlong</c> parameter at
/// <c>ws_objects/pfw.base.pbl.src/pfwinitialize.srf:L8</c>, so each value reaches this shape by a
/// widening conversion that is exact for every possible mask and can never be negative - which makes the
/// contract's declared minimum of zero true by construction rather than by validation.
/// </para>
/// <para>
/// Required-ness follows the contract exactly. <c>effectiveMask</c>, <c>allMask</c> and
/// <c>capabilities</c> are required [<c>gateway.v1.yaml:L4405</c>]; <c>unrecognizedBits</c> is not, and
/// is therefore declared without <see langword="required"/> so the generated document agrees with the
/// hand-authored one. The handler populates it on every response regardless, because an optional member
/// that is always present is a superset of the contract and a required member that is sometimes absent
/// would be a violation of it.
/// </para>
/// </remarks>
public sealed record CapabilityReport
{
    /// <summary>
    /// The bitmask actually in effect in this deployment, resolved from <c>Gateway:CapabilityFlags</c>.
    /// </summary>
    /// <remarks>
    /// The full unsigned 32-bit range is representable and none of it is rejected: the configured value
    /// is projected with an unchecked conversion, so a negative configured value wraps rather than
    /// failing. That reproduces the legacy signed-to-unsigned handover, which performs no check either
    /// [<c>gateway.v1.yaml:L4406-L4415</c>].
    /// </remarks>
    [JsonPropertyName("effectiveMask")]
    public required long EffectiveMask { get; init; }

    /// <summary>
    /// The value of <c>INIT_FLAG_ENABLE_ALL</c>, which is <c>3847</c> and never <c>3855</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Fixed by the legacy declaration at <c>ws_objects/pfw.shared.pbl.src/enums.sru:L49</c> rather than
    /// by this deployment, which is why the contract declares it <c>const</c>
    /// [<c>gateway.v1.yaml:L4419-L4420</c>]. It is a <b>seven-term</b> sum that deliberately omits
    /// <c>INIT_FLAG_ENABLE_BLINKFAST</c> (8), so it is <b>not</b> the bitwise union of the eight declared
    /// bits - that union would be <c>3855</c>, and a consumer computing it that way disagrees with the
    /// framework by exactly the <c>BLINKFAST</c> bit.
    /// </para>
    /// <para>
    /// The omission is not an oversight and must not be corrected: the standard and fast engine binaries
    /// are alternative builds of one engine, so enabling both is meaningless. The full annotation sits at
    /// the point of reproduction, where this member is populated.
    /// </para>
    /// </remarks>
    [JsonPropertyName("allMask")]
    public required long AllMask { get; init; }

    /// <summary>
    /// Any bits set in <see cref="EffectiveMask"/> that none of the eight declared capabilities claims.
    /// </summary>
    /// <remarks>
    /// Reported rather than rejected, so a configuration carrying a future or mistaken bit starts and
    /// says so [<c>gateway.v1.yaml:L4437-L4441</c>]. Zero when every set bit is a declared capability.
    /// The unassigned positions are values 16, 32, 64 and 128, which sit in the legacy's own gap between
    /// <c>INIT_FLAG_ENABLE_BLINKFAST</c> (8) and <c>INIT_FLAG_ENABLE_ORCA</c> (256), plus everything
    /// above <c>INIT_FLAG_ENABLE_WEBVIEW</c> (2048). No capability entry is published for any of them,
    /// because no <c>INIT_FLAG_ENABLE_</c> constant names one.
    /// </remarks>
    [JsonPropertyName("unrecognizedBits")]
    public long UnrecognizedBits { get; init; }

    /// <summary>
    /// All eight declared capabilities with their values and enabled state, in
    /// <c>ws_objects/pfw.shared.pbl.src/enums.sru:L41-L48</c> declaration order.
    /// </summary>
    /// <remarks>
    /// Exactly eight, always [<c>gateway.v1.yaml:L4443-L4445</c>]: the set is closed because the legacy
    /// declaration is closed, so a ninth capability would be a change to the published contract rather
    /// than a value this member could carry. Declaration order is preserved so that the sparse bit layout
    /// is legible in any payload and so that a given mask's output is byte-for-byte reproducible, which
    /// is what the characterization recordings compare against.
    /// </remarks>
    [JsonPropertyName("capabilities")]
    public required IReadOnlyList<Capability> Capabilities { get; init; }
}

/// <summary>
/// One declared capability bit of the framework's module gate.
/// </summary>
/// <remarks>
/// All four members are required by the published contract
/// [<c>shared/PowerFramework.Contracts/OpenApi/gateway.v1.yaml:L4457</c>] and are declared
/// <see langword="required"/> here so the generated document says the same.
/// </remarks>
public sealed record Capability
{
    /// <summary>
    /// The preserved legacy identifier, spelled exactly as <c>enums.sru:L41-L48</c> declares it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Carried as a <see cref="string"/> rather than as a C# enumeration, and that is a constraint rather
    /// than a preference. These identifiers keep their legacy <c>SCREAMING_SNAKE</c> spelling because they
    /// appear in serialized payloads, log records and characterization recordings, where a rename would
    /// silently invalidate every stored comparison - and the analyzer suppressions that make such
    /// spellings buildable are scoped to the files that declare them, none of which is in this service.
    /// Declaring an enumeration with those member names here would therefore be a build error under
    /// warnings-as-errors, so the value is consumed from
    /// <c>Composition/CapabilityFlags.KnownCapabilityNames</c>, where each name is derived with
    /// <see langword="nameof"/> from the shared catalogue constant it names.
    /// </para>
    /// <para>
    /// The consequence for the generated document is stated plainly rather than papered over: the
    /// hand-authored contract constrains this member to a closed <c>enum</c> of the eight identifiers,
    /// and a <see cref="string"/> cannot carry that constraint. The closure is enforced instead at the
    /// only place it can be - the projection, which iterates the closed vocabulary and fails fast on
    /// anything outside it - and is asserted by the parity tests.
    /// </para>
    /// </remarks>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>
    /// The bit's numeric value, exactly as <c>enums.sru:L41-L48</c> declares it.
    /// </summary>
    /// <remarks>
    /// One of <c>1</c>, <c>2</c>, <c>4</c>, <c>8</c>, <c>256</c>, <c>512</c>, <c>1024</c> or <c>2048</c>
    /// [<c>gateway.v1.yaml:L4497</c>]. The eight are sparse rather than the first eight powers of two,
    /// and the gap between <c>8</c> and <c>256</c> is the legacy's own: bits 16, 32, 64 and 128 are
    /// simply not declared, and nothing here invents a name or an entry for them. Widened from the
    /// in-process <see cref="uint"/> for the contract's <c>format: int64</c>.
    /// </remarks>
    [JsonPropertyName("value")]
    public required long Value { get; init; }

    /// <summary>
    /// Whether this bit is set in <see cref="CapabilityReport.EffectiveMask"/>.
    /// </summary>
    /// <remarks>
    /// An unset bit means the capability is unusable in this deployment and its native payload need not
    /// be present [<c>docs/README.md:L26</c>]. A set bit means configuration asked for the capability -
    /// nothing more. In particular it grants no access, and for every capability whose destination is
    /// outside this phase it has no runtime effect at all.
    /// </remarks>
    [JsonPropertyName("enabled")]
    public required bool Enabled { get; init; }

    /// <summary>
    /// Where this capability's implementation lives in the Phase-1 slice.
    /// </summary>
    /// <remarks>
    /// Exactly one of the eight capabilities resolves to a service that exists in this phase; the
    /// remaining seven name capability areas outside it, so a client can see from this member that
    /// enabling such a bit has no runtime effect here [<c>gateway.v1.yaml:L4511-L4520</c>]. The value is a
    /// destination name and nothing more - no client, handler, interface, configuration entry or
    /// dependency-injection registration exists behind any of these names, and this service calls none of
    /// them.
    /// </remarks>
    [JsonPropertyName("phaseOneDestination")]
    public required CapabilityDestination PhaseOneDestination { get; init; }
}

/// <summary>
/// Where a capability's implementation lives in the Phase-1 slice.
/// </summary>
/// <remarks>
/// <para>
/// The closed value set of the contract's <c>phaseOneDestination</c> member, in the order the contract
/// declares it [<c>shared/PowerFramework.Contracts/OpenApi/gateway.v1.yaml:L4508-L4510</c>]. It is
/// serialized as its NAME rather than as an ordinal, enforced by the converter attribute on the type
/// itself rather than by a serializer option, so the wire form cannot be changed by host configuration
/// and the generated document carries a string enumeration matching the hand-authored one.
/// </para>
/// <para>
/// These are destination NAMES on a wire and create no coupling whatsoever. Nothing in this service
/// imports, registers, injects, constructs or calls anything behind any of them; the member exists so
/// that the shape of the eventual system is legible from the contract, which is precisely what
/// distinguishes published metadata from an implementation.
/// </para>
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<CapabilityDestination>))]
public enum CapabilityDestination
{
    /// <summary>
    /// The service that owns storage - the destination of <c>INIT_FLAG_ENABLE_SQLITE</c>, and the only
    /// one of the four that names a service existing in this phase.
    /// </summary>
    /// <remarks>
    /// A wire value only. This service does not reach that one at all, and no client, import,
    /// configuration entry or registration for it exists anywhere in this project.
    /// </remarks>
    Persistence,

    /// <summary>
    /// The deferred presentation capability area - the destination of <c>INIT_FLAG_ENABLE_UI</c> and
    /// <c>INIT_FLAG_ENABLE_DPIAWARE</c>.
    /// </summary>
    /// <remarks>
    /// Outside this phase. It has no project, no container, no test project and no implementation
    /// anywhere in this build graph.
    /// </remarks>
    DesignSystem,

    /// <summary>
    /// The deferred script and embedded-engine capability area - the destination of
    /// <c>INIT_FLAG_ENABLE_SCITER</c>, <c>INIT_FLAG_ENABLE_BLINK</c>,
    /// <c>INIT_FLAG_ENABLE_BLINKFAST</c> and <c>INIT_FLAG_ENABLE_WEBVIEW</c>.
    /// </summary>
    /// <remarks>
    /// Outside this phase. It has no project, no container, no test project and no implementation
    /// anywhere in this build graph. That four of the eight bits point here is itself evidence the legacy
    /// treated embedded engines as one cohesive area.
    /// </remarks>
    ScriptBridge,

    /// <summary>
    /// PowerBuilder packaging tooling - the destination of <c>INIT_FLAG_ENABLE_ORCA</c>.
    /// </summary>
    /// <remarks>
    /// Build tooling rather than a service at any phase, which is why it is a destination of its own
    /// instead of being folded into one of the deferred capability areas.
    /// </remarks>
    PackagingTooling,
}
