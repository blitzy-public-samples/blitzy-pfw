// ==================================================================================================
//  JwksEndpoints.cs - THE TWO ANONYMOUS PUBLICATION ROUTES OF THE SECURITY SERVICE
//  ------------------------------------------------------------------------------------------------
//  ROLE
//  Publishes this service's PUBLIC verification material, and the discovery metadata that points at
//  it, on the two /.well-known/* addresses contract C-01 declares:
//
//      GET  <Security:JwksPath>                 default /.well-known/jwks.json
//      GET  <Security:OpenIdConfigurationPath>  default /.well-known/openid-configuration
//
//  This service is the SOLE token issuer in the whole decomposition. Exactly one signing secret
//  exists anywhere in it, this service holds it, and Gateway, DataServices and Persistence hold
//  verification material only. THIS FILE IS THE SINGLE PUBLICATION POINT OF THAT VERIFICATION
//  MATERIAL, on routes that require no credential at all. That combination is why it is written the
//  way it is: one wrong member on the key it serializes would publish the system's only signing
//  secret to every caller that can reach port 5104.
//
//  WHY REST AND NOT gRPC - THE RATIONALE LIVES HERE (constraint C-K)
//  Token issuance and key publication must be plain HTTP so that each consumer's stock
//  Microsoft.AspNetCore.Authentication.JwtBearer handler fetches the key set and the discovery
//  metadata WITH ZERO BESPOKE CODE. Pointed at this service, that handler discovers the issuer,
//  fetches the key set, and then performs signature checking, key selection by identifier, issuer and
//  audience validation and clock-skew tolerance entirely inside framework code.
//
//  Choosing gRPC here would have forced a hand-written key-set retrieval implementation into THREE
//  separate services - a net INCREASE in hand-written security-critical code, which is the opposite
//  direction from the one the authenticated-boundary requirement pushes in. It is the wrong choice
//  rather than merely a less convenient one, and the legacy surface argues nothing for it either: the
//  cryptographic class this service replaces is roughly sixty stateless request/response overloads
//  with no ordering requirement and nothing to stream
//  [ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L9-L73].
//
//  WHY BOTH ROUTES ARE ANONYMOUS, AND WHY THAT IS A DECISION RATHER THAN AN OVERSIGHT (C-G)
//  Program.cs installs a default-deny fallback authorization policy, so on this service every route
//  is authenticated unless it opts out. Exactly THREE routes opt out: the readiness probe in
//  HealthEndpoints.cs, and the two declared here. Both of these must stay anonymous, permanently:
//
//    * Verification material is PUBLIC BY DEFINITION. Publishing it is what allows a signature to be
//      checked, and that is precisely what distinguishes it from a signing key.
//    * A consumer's bearer handler fetches these two addresses IN ORDER TO LEARN HOW TO
//      AUTHENTICATE, so it cannot already hold a token when it asks. Requiring one would deadlock the
//      very self-configuration this file exists to provide.
//
//  Each route therefore calls AllowAnonymous() EXPLICITLY. Relying on omission would be wrong twice
//  over: under the fallback policy omission CLOSES a route rather than opening it, and a property
//  satisfied by absence is invisible at the thing it governs.
//
//  THE ALLOW-LIST RULE, AND WHY A GENERAL-PURPOSE KEY SERIALIZATION IS FORBIDDEN
//  The key set is built member by member into JsonWebKeyDocument, which DECLARES exactly six
//  members - the key family, the intended use, the algorithm, the identifier, the public modulus and
//  the public exponent - and declares NO private and NO symmetric member. There is no private
//  exponent, no prime factor, no exponent remainder, no coefficient and no symmetric key value, and
//  none may ever be added.
//
//  A general-purpose key object from the token library is NEVER handed to the serializer, and this is
//  defence in depth rather than distrust: Tokens/SigningKeyProvider.cs already exports its public
//  parameters with the private ones EXCLUDED, so the material reaching this file cannot carry a
//  private component in the first place. The second layer exists because a general-purpose key type
//  is designed to carry private members when it has them, so serializing one would make this route's
//  safety depend on a property of an upstream type that a future edit could change silently. A type
//  with nowhere to put a private member cannot publish one whatever any serializer setting, naming
//  policy or upstream change does, and a reviewer can confirm that by reading one declaration.
//
//  WHAT THIS FILE DELIBERATELY DOES NOT DO
//    * It never reads Tokens/SigningKeyProvider.SigningCredentials. That member is the only one
//      carrying private key material and its sole legitimate consumer is the token issuer. This file
//      consumes the public-only projection, and nothing else, from that type (C-F).
//    * No secret, key, certificate, credential, token or encoded key run appears anywhere below -
//      not in code, not in a default, not in a comment and not in any published description text. In
//      particular NO SPECIMEN KEY SET IS WRITTEN AS AN EXAMPLE. A plausible-looking encoded blob is
//      indistinguishable from real key material to a reader, and specimen keys have a long history of
//      being copied into a deployment unchanged; the authored contract takes the same position and
//      carries no example on any field of either schema (C-F).
//    * It generates no key, falls back to no keyed-hash scheme, and serves neither a partial nor a
//      placeholder key set to keep a readiness probe green. A structural fault in the signing
//      material is caught at STARTUP by the options validator and the signing-key provider; the
//      legacy posture is that a decoded assertion failure ends the process rather than degrading past
//      it [ws_objects/pfw.pbl.src/pfw.sra:L111-L144, ending in HALT CLOSE at :L143], and an
//      ephemeral key invented here would silently invalidate every token the other three services
//      hold.
//    * No outbound HTTP client, no channel and no client of any kind, and no reference to any sibling
//      service. This file SERVES their bearer handlers; it never calls them, and it fetches nothing
//      outbound in order to build either published shape (C-A).
//    * No storage of any kind: no context, no connection string, no store read (C-E).
//    * Nothing for a deferred capability area: no route, no member, no tag and no reserved-status
//      answer. Those four routing declarations belong to GATEWAY's routing table, and putting one
//      here would place a deferred capability's surface in the wrong service entirely (C-D).
//    * No key-rotation scheduler, no multi-key ring beyond what the signing-key provider publishes,
//      and no caching layer or cache directive (C-B). See the note on response headers below.
//    * No upper-case-with-underscores identifier is DECLARED anywhere below. The repository
//      .editorconfig scopes its naming suppressions to a fixed list of files carrying preserved legacy
//      identifiers and this file is NOT among them, while Directory.Build.props sets
//      warnings-as-errors, so a preserved-spelling constant declared here would be a build error
//      rather than a style nit. The one preserved identifier this file needs is CONSUMED from the
//      shared kernel - the internal-error return code - and never restated.
//    * No response header is emitted, and no cache directive in particular. The authored contract
//      declares no response header on either operation; a stock consumer caches the key set on its
//      OWN refresh interval rather than on an HTTP directive, so a directive would change nothing it
//      does; and adding one would be an unrequested behavioural change on a security-critical path.
//      Emitting nothing also makes the C-B property trivially checkable: with no directive at all,
//      no directive can outlive a key change.
//
//  A NOTE ON THE WIRE MEMBER NAMES
//  The published members are spelled in lower case and in snake case - the key family, modulus and
//  exponent abbreviations on the key, and the key-set address and algorithm-list members on the
//  discovery document. Those are JSON names fixed by the standards a stock handler reads, not C#
//  names, so every one is bound with a serialization attribute on a conventionally named property.
//  Naming the properties after the wire would have put the same underscore spellings the
//  .editorconfig scope forbids into this file for no benefit.
//
//  LEGACY REFERENCE (read only - never edited, never built, never shipped: constraint C-C)
//  PowerFramework is a library with no process of its own, no listener and no server tier, so there
//  is no legacy analogue for a key-set publication route and no wire format here translates an
//  existing one. What IS inherited is the cryptographic provenance:
//
//    * ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L70-L73 - the signature pair this issuer is built
//      on. RSASign takes the PRIVATE key and VerifyRSASign takes the PUBLIC one, and both are typed
//      as text in every overload, exactly as RSA key generation returns the two halves separately at
//      :L19-L20. That separation is the legacy's own, and publishing only the public half here is
//      its direct continuation rather than a new policy.
//    * ws_objects/pfw.shared.pbl.src/enums.sru:L927-L933 - the comment at :L927 records that this
//      digest set governs the hash primitive, RSASign AND VerifyRSASign, and the SHA-256 member sits
//      at :L930. That is the settled provenance for treating this issuer's default algorithm
//      identifier as RSA with SHA-256. The digest constants themselves are deliberately NOT restated
//      or re-mapped in this file: the closed algorithm set already has exactly two owners - the
//      options validator that publishes it and the signing-key provider that resolves against it -
//      and a third spelling here could drift from both. The provenance is cited; the set is consumed.
//    * tests/blink/test_jws.htm:L8-L23 - cited ONLY as THE ANTI-PATTERN THIS DESIGN REPLACES. That
//      page hardcodes a private key in plaintext and signs a token with it in the browser. Nothing
//      from it is copied here in any form, transformed or otherwise, and it is never edited: it is a
//      real in-repository secret site, inventoried in docs/SECRETS.md, and its remediation posture is
//      never-replicate rather than remove-from-source.
//
//  THE AUTHORED CONTRACT WINS
//  shared/PowerFramework.Contracts/OpenApi/security.v1.yaml is authoritative for everything on the
//  wire - both addresses, both operation identifiers, both response shapes and the absence of a
//  security requirement on each. Where it and docs/CONTRACTS.md ever disagree, the YAML wins and this
//  file changes. It is packaged as content rather than compiled, so there is no generated type for
//  this surface and the three response shapes below are hand-authored and conform to it by review
//  plus the assertions the sibling test project makes.
// ==================================================================================================

using System.Collections.Immutable;
using System.Net.Mime;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using PowerFramework.Security.Configuration;
using PowerFramework.Security.Tokens;
using PowerFramework.Shared.Kernel;

namespace PowerFramework.Security.Endpoints;

/// <summary>
/// Declares and serves the two anonymous <c>/.well-known/</c> publications of contract C-01: this
/// service's JSON Web Key Set and the discovery metadata that points at it.
/// </summary>
/// <remarks>
/// <para>
/// ONE PUBLIC REGISTRATION METHOD, MATCHING EVERY OTHER FILE IN THIS FOLDER, and it registers BOTH
/// routes. The two belong together because they are one capability seen from two sides: the discovery
/// document exists only to address the key set, and a consumer that reads one always reads the other.
/// No third route may be added here.
/// </para>
/// <para>
/// BOTH HANDLERS ARE NAMED, INJECTABLE METHODS rather than logic-bearing lambdas, and so is the
/// allow-list projection between the published material and the wire. That is what lets the sibling
/// test project assert the projection directly - member set, member spellings, and the structural
/// absence of every private member - as well as through a booted host, which is what the per-service
/// coverage gate needs.
/// </para>
/// <para>
/// Nothing is logged on either success path. Both addresses are fetched by consumers' handlers on a
/// refresh schedule, so a record per request would be pure noise, and there is nothing about a
/// published key that could usefully be recorded in any case. The failure path records one structured
/// classifier through the shared problem factory, which has no parameter capable of carrying key
/// material.
/// </para>
/// </remarks>
public static class JwksEndpoints
{
    /// <summary>
    /// The operation identifier of the key-set publication, as the authored contract declares it
    /// [<c>OpenApi/security.v1.yaml:L633</c>].
    /// </summary>
    /// <remarks>
    /// Supplied as the endpoint name, which is what the document generator emits as the operation
    /// identifier. The authored contract is verified by counting operation identifiers rather than by
    /// reading prose, so a divergence here would be a divergence a conformance test can see.
    /// </remarks>
    private const string KeySetOperationName = "getJsonWebKeySet";

    /// <summary>
    /// The operation identifier of the discovery publication
    /// [<c>OpenApi/security.v1.yaml:L682</c>].
    /// </summary>
    private const string MetadataOperationName = "getOpenIdConfiguration";

    /// <summary>
    /// The tag both operations carry [<c>OpenApi/security.v1.yaml:L635,L684</c>].
    /// </summary>
    /// <remarks>
    /// Both publications are part of contract C-01 alongside token issuance, so they are grouped with
    /// it rather than under a tag of their own: a consumer configuring itself against this issuer
    /// needs all three described together.
    /// </remarks>
    private const string OperationTagName = "TokenService";

    /// <summary>
    /// The key-set operation summary [<c>OpenApi/security.v1.yaml:L636</c>].
    /// </summary>
    private const string KeySetOperationSummary =
        "Publish the token verification material as a JSON Web Key Set.";

    /// <summary>
    /// The discovery operation summary [<c>OpenApi/security.v1.yaml:L685</c>].
    /// </summary>
    private const string MetadataOperationSummary =
        "Publish the discovery metadata a stock bearer handler self-configures from.";

    /// <summary>
    /// The published description of the key-set operation.
    /// </summary>
    /// <remarks>
    /// States the two properties a consumer and a reviewer each need: that only public parameters are
    /// published and that the omission of private ones is structural, and that the route is anonymous
    /// by design. NO SPECIMEN VALUE APPEARS IN IT, for the reason recorded in the file header.
    /// </remarks>
    private const string KeySetOperationDescription =
        "Publishes the PUBLIC verification material for the tokens this service issues, and nothing "
        + "else. Each published key carries exactly six members: the key family, the intended use, "
        + "the algorithm it is intended for, the identifier a token header repeats so a consumer can "
        + "select it, and the public modulus and public exponent. No private and no symmetric member "
        + "is published, and none can be - the response shape declares none, so there is no member "
        + "for one to be written into. Anonymous by design: verification material is public by "
        + "definition, and a consumer's stock bearer handler fetches this address before it holds any "
        + "token it could present.";

    /// <summary>
    /// The published description of the discovery operation.
    /// </summary>
    /// <remarks>
    /// Carries the constraint C-K rationale onto the wire, so a consumer reading the published
    /// description learns why this surface is plain HTTP rather than having to find it in a comment.
    /// It also states the two deliberate absences, because an absence a consumer cannot see is an
    /// absence it will ask about.
    /// </remarks>
    private const string MetadataOperationDescription =
        "Publishes the minimum discovery metadata a stock JSON Web Token bearer handler needs in "
        + "order to configure itself: the issuer identifier, the absolute address of this service's "
        + "key set, the absolute address of the issuance operation, and the signature algorithm "
        + "identifiers this issuer uses. The issuer identifier is the configured identity and is the "
        + "same on every response. Both ADDRESSES are composed from a configured base address, chosen "
        + "from the deployment's declared set by matching the origin this request arrived on, so a "
        + "consumer reaching this service on an internal name and a consumer reaching it through a "
        + "published host port are each given a key-set address that resolves for them - while the set "
        + "of addresses that can be published is exactly the configured set, so no caller-supplied "
        + "host can be advertised and no host is fixed in code. THIS OPERATION IS WHY THIS SURFACE IS "
        + "PLAIN HTTP RATHER THAN A BINARY PROTOCOL: a "
        + "consumer points its stock handler here and writes no retrieval code at all, whereas a "
        + "binary protocol would have forced a hand-written key-set retrieval implementation into "
        + "three separate services. Anonymous by design, for the same reason as the key set - a "
        + "handler reads this in order to learn how to authenticate, so it cannot already hold a "
        + "token. Two absences are part of the contract rather than gaps: no interactive-flow address "
        + "and no end-user description member is published, because every caller here is a service "
        + "and there is no end user to redirect, to prompt or to describe.";

    /// <summary>
    /// Reported when the published projection carries no key at all.
    /// </summary>
    /// <remarks>
    /// AN EMPTY SET IS A FAILURE, NEVER A SUCCESS. Answering 200 with an empty collection would look
    /// like a healthy issuer with nothing to verify against, and every consumer would then reject every
    /// token this service minted while both sides reported themselves working. A structured failure
    /// names the condition instead.
    /// </remarks>
    private const string EmptyKeySetDetail =
        "The published verification material carries no key.";

    /// <summary>
    /// Reported when the published identifier and the configured identifier disagree.
    /// </summary>
    /// <remarks>
    /// The identifier published here and the identifier a minted token's header carries must be the
    /// same value, because that is how a consumer selects the right key. If they disagree, verification
    /// fails for every token, so the disagreement is reported rather than papered over by publishing
    /// one of the two.
    /// </remarks>
    private const string KeyIdentifierDisagreementDetail =
        "The published key identifier and the configured identifier disagree.";

    /// <summary>
    /// Reported when the published algorithm and the configured algorithm disagree.
    /// </summary>
    private const string AlgorithmDisagreementDetail =
        "The published key algorithm and the configured algorithm disagree.";

    /// <summary>
    /// Reported when the published algorithm is outside the closed set this issuer signs with.
    /// </summary>
    /// <remarks>
    /// The set is consumed from
    /// <see cref="SecurityOptionsValidator.PermittedSigningAlgorithms"/> rather than restated, so this
    /// check and the startup validation can never describe different sets.
    /// </remarks>
    private const string AlgorithmNotPermittedDetail =
        "The published key algorithm is outside the set this issuer signs with.";

    /// <summary>
    /// Reported when a published key is missing one of the six members the wire shape requires.
    /// </summary>
    /// <remarks>
    /// Deliberately ONE message for all six members rather than six. The response is anonymous, so it
    /// says that the material is incomplete and stops there; which member is absent is an operator
    /// concern and reaches the structured log, not the body.
    /// </remarks>
    private const string IncompleteKeyDetail =
        "The published verification material is incomplete.";

    /// <summary>
    /// Reported when no issuer identifier is configured.
    /// </summary>
    private const string IssuerAbsentDetail =
        "This service has no configured issuer identifier.";

    /// <summary>
    /// Reported when the configured issuer identifier is not an absolute address.
    /// </summary>
    /// <remarks>
    /// A consumer's handler validates the issuer claim of every inbound token against this value and
    /// the published schema types it as an absolute address, so a relative one would be rejected by a
    /// conforming consumer before it ever reached a token.
    /// </remarks>
    private const string IssuerNotAbsoluteDetail =
        "The configured issuer identifier is not an absolute address.";

    /// <summary>
    /// Reported when a published address cannot be composed because its configured path is not rooted.
    /// </summary>
    /// <remarks>
    /// Both published addresses are composed from the incoming request plus a configured path, and an
    /// unrooted path cannot be composed at all. Reported as a structured failure rather than allowed to
    /// surface as an unhandled argument fault, so an anonymous caller receives the contract's own error
    /// shape rather than a framework fault page.
    /// </remarks>
    private const string AddressNotRootedDetail =
        "A published address cannot be composed from its configured path.";

    /// <summary>
    /// The startup failure raised when a configured metadata path falls outside the well-known
    /// namespace.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A STARTUP MESSAGE RATHER THAN A RESPONSE BODY, so it may name the offending setting - and it
    /// names it WITHOUT ECHOING ITS VALUE, which is the same posture the options validator takes.
    /// </para>
    /// <para>
    /// Composed from <see cref="SecurityOptions.SectionName"/>, <c>nameof</c> and
    /// <see cref="SecurityOptionsValidator.WellKnownMetadataPathPrefix"/> rather than written out, so
    /// no setting name and no path prefix is spelled a second time in this file.
    /// </para>
    /// </remarks>
    private const string MetadataPathOutsideNamespaceMessage =
        " must be a rooted path inside the well-known namespace with something after the prefix. "
        + "That constraint is an authentication control rather than tidiness: the two routes this file "
        + "declares are the only anonymous exemptions it grants, so an unconstrained value here could "
        + "aim an anonymous exemption at an authenticated route and silently remove its "
        + "authentication.";

    /// <summary>
    /// The startup failure raised when both metadata paths are configured to the same value.
    /// </summary>
    /// <remarks>
    /// Two routes on one address is an ambiguous match, which fails at REQUEST time on whichever
    /// address a consumer happens to fetch first. Refusing to start instead turns a runtime ambiguity
    /// into a bring-up failure, which is where a configuration mistake belongs.
    /// </remarks>
    private const string CoincidentMetadataPathsMessage =
        "The key-set path and the discovery path are configured to the same value. Each publication "
        + "needs an address of its own, because two routes on one address is an ambiguous match at "
        + "request time.";

    /// <summary>
    /// The bracket pair an IPv6 literal is wrapped in, trimmed from both spellings before an origin's
    /// host is compared with a request's.
    /// </summary>
    /// <remarks>
    /// <see cref="Uri.Host"/> renders an IPv6 literal WITH brackets and <see cref="HostString.Host"/>
    /// renders it without, so the two would never compare equal for an IPv6 origin. A shared array rather
    /// than a literal at the call site, so both sides of that comparison are normalised by the same value
    /// by construction.
    /// </remarks>
    private static readonly char[] IpLiteralBrackets = ['[', ']'];

    /// <summary>
    /// Declares both publication routes on the supplied route builder.
    /// </summary>
    /// <param name="endpoints">The route builder to declare on.</param>
    /// <returns>
    /// <paramref name="endpoints"/>, so the call composes with the sibling registrations
    /// <c>Program.cs</c> makes.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="endpoints"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// A configured metadata path is not a rooted path inside the well-known namespace, or both paths
    /// are configured to the same value. Either condition prevents the host from starting - see the
    /// remarks.
    /// </exception>
    /// <remarks>
    /// <para>
    /// THE ONLY PUBLIC MEMBER OF THIS FILE, matching the one-registration-method-per-endpoint-file
    /// shape the whole folder uses and that <c>Program.cs</c> calls once per file. It registers BOTH
    /// routes and no others.
    /// </para>
    /// <para>
    /// THE ADDRESSES COME FROM CONFIGURATION AND ARE NOT SPELLED HERE.
    /// <see cref="SecurityOptions.JwksPath"/> and <see cref="SecurityOptions.OpenIdConfigurationPath"/>
    /// already define the well-known names and already carry the defaults, so restating either would
    /// create a second source of truth able to drift the route away from the address the discovery
    /// document advertises.
    /// </para>
    /// <para>
    /// THE PATH GUARD IS AT REGISTRATION AND IT IS LOAD BEARING, NOT CEREMONY. It fails the host
    /// rather than answering a request, because that is the only point at which it can actually
    /// prevent the harm: a request-time check cannot un-map a route that has already been declared
    /// anonymous. <see cref="SecurityOptions.JwksPath"/> records that the well-known constraint is an
    /// authentication control, and this is where that control is applied - an unconstrained value would
    /// otherwise let a deployment aim one of the two anonymous exemptions at an authenticated route.
    /// The options validator checks the same property at startup; both checks are wanted, because this
    /// one holds even for a host that composes options directly, and neither is a substitute for the
    /// other.
    /// </para>
    /// <para>
    /// FAILING TO START IS THE CORRECT ANSWER, and it is the ported posture rather than a preference.
    /// The legacy application object treats a decoded assertion failure as fatal and ends the process
    /// [<c>ws_objects/pfw.pbl.src/pfw.sra:L111-L144</c>, <c>HALT CLOSE</c> at <c>:L143</c>]. Softening
    /// either condition into a warning-and-continue would leave a service that answers a readiness
    /// probe while publishing an address it should not.
    /// </para>
    /// <para>
    /// Options are read from the builder's own service provider rather than captured in a closure, so
    /// the registration sees exactly the configuration the handlers will see. <c>IOptions</c> is used
    /// rather than a snapshot or a monitor deliberately: the published key is fixed for the lifetime of
    /// the process because this phase adds no rotation, so a per-request re-read would imply a
    /// mutability that does not exist.
    /// </para>
    /// </remarks>
    public static IEndpointRouteBuilder MapJwksEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        SecurityOptions security = endpoints.ServiceProvider
            .GetRequiredService<IOptions<SecurityOptions>>()
            .Value;

        string keySetPath = RequireWellKnownPath(security.JwksPath, nameof(SecurityOptions.JwksPath));
        string metadataPath = RequireWellKnownPath(
            security.OpenIdConfigurationPath,
            nameof(SecurityOptions.OpenIdConfigurationPath));

        if (string.Equals(keySetPath, metadataPath, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(CoincidentMetadataPathsMessage);
        }

        endpoints.MapGet(keySetPath, PublishKeySet)
            // ------------------------------------------------------------------------------------
            // CONSTRAINT C-G, AND ONE OF EXACTLY THREE PLACES IN THIS SERVICE THIS LINE APPEARS.
            //
            // Unconditional and explicit. Not wrapped in an environment test, not paired with an
            // authenticated variant, and not weakened by a configuration switch: this address must
            // answer an anonymous request in every environment, because a consumer's bearer handler
            // fetches it BEFORE it holds any token to present.
            //
            // Called explicitly rather than left to omission. Under the default-deny fallback policy
            // Program.cs installs, omission would CLOSE this route rather than open it, so the call
            // is what makes the route reachable at all - and stating it here keeps the exemption
            // visible at the thing it exempts.
            // ------------------------------------------------------------------------------------
            .AllowAnonymous()
            .WithName(KeySetOperationName)
            .WithTags(OperationTagName)
            .WithSummary(KeySetOperationSummary)
            .WithDescription(KeySetOperationDescription)
            // Exactly the two responses the authored contract declares for this operation and no
            // others. The handler returns IResult rather than a typed result union for that reason:
            // a union would let the framework infer response metadata of its own from the result
            // types, and the declaration below is meant to be the whole story.
            .Produces<JsonWebKeySetDocument>(
                StatusCodes.Status200OK,
                MediaTypeNames.Application.Json)
            .ProducesProblem(
                StatusCodes.Status500InternalServerError,
                MediaTypeNames.Application.ProblemJson)
            .AddOpenApiOperationTransformer(DeclareAnonymousAsync);

        endpoints.MapGet(metadataPath, PublishProviderMetadata)
            // The second of this file's two anonymous exemptions, explicit for the same reasons. A
            // handler reads THIS address in order to learn how to authenticate, so requiring a token
            // here would deadlock the self-configuration the address exists to provide.
            .AllowAnonymous()
            .WithName(MetadataOperationName)
            .WithTags(OperationTagName)
            .WithSummary(MetadataOperationSummary)
            .WithDescription(MetadataOperationDescription)
            .Produces<ProviderMetadataDocument>(
                StatusCodes.Status200OK,
                MediaTypeNames.Application.Json)
            .ProducesProblem(
                StatusCodes.Status500InternalServerError,
                MediaTypeNames.Application.ProblemJson)
            .AddOpenApiOperationTransformer(DeclareAnonymousAsync);

        return endpoints;
    }

    /// <summary>
    /// Serves the JSON Web Key Set: this service's public verification material, and nothing else.
    /// </summary>
    /// <param name="signingKeys">
    /// The signing-key layer. ONLY its public-only projection is read. Its credential member carries
    /// the private key and is never touched from this file.
    /// </param>
    /// <param name="options">The configured issuance settings, for the self-consistency check.</param>
    /// <param name="loggerFactory">
    /// Records one structured classifier on the failure path. Nothing is recorded on success.
    /// </param>
    /// <returns>
    /// 200 carrying the allow-listed key set, or the contract's problem document when the published
    /// material is not self-consistent with the configured settings.
    /// </returns>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// A NAMED METHOD RATHER THAN AN INLINE LAMBDA, and <c>internal</c> rather than <c>private</c>, so
    /// the sibling test project can call it directly as well as through a booted host - which is what
    /// constraint C-H asks of a handler. Every collaborator arrives as a parameter and none is pulled
    /// from a service locator, so a direct call needs no host at all.
    /// </para>
    /// <para>
    /// IT READS EXACTLY ONE MEMBER OF THE SIGNING-KEY LAYER, and that member is the public-only
    /// projection. The private credential is never referenced here, so no future edit to this method
    /// can reach private material by accident - there is no local, no field and no parameter through
    /// which it could arrive.
    /// </para>
    /// <para>
    /// THE CONSISTENCY CHECK IS ASSERTED, NOT ASSUMED. The identifier and the algorithm published here
    /// must be the same values a minted token's header carries, or verification fails for every token
    /// while both sides appear healthy. They are therefore compared against the configured settings on
    /// every request rather than trusted, and a disagreement is answered as a server fault.
    /// </para>
    /// <para>
    /// ON FAILURE IT PUBLISHES NOTHING AND INVENTS NOTHING. It does not synthesize a key, does not fall
    /// back to a keyed-hash scheme, and does not answer 200 with an empty collection to keep a probe
    /// green. The response carries no key material, no key reference and no configuration value: the
    /// detail strings are fixed sentences with no interpolation, and the shared problem factory has no
    /// parameter through which a secret could be passed.
    /// </para>
    /// <para>
    /// It is synchronous because it does no input or output of any kind: the projection is a copy of
    /// values already resident in memory. It opens no connection, reads no file and makes no outbound
    /// call (constraints C-A and C-E).
    /// </para>
    /// </remarks>
    internal static IResult PublishKeySet(
        SigningKeyProvider signingKeys,
        IOptions<SecurityOptions> options,
        ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(signingKeys);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        PublishedJsonWebKeySet published = signingKeys.PublishedKeySet;

        string? inconsistency = DescribeKeySetInconsistency(published, options.Value);

        if (inconsistency is not null)
        {
            return Fail(inconsistency, loggerFactory);
        }

        return TypedResults.Ok(ProjectKeySet(published));
    }

    /// <summary>
    /// Serves the discovery metadata a stock bearer handler self-configures from.
    /// </summary>
    /// <param name="signingKeys">
    /// The signing-key layer. Its public-only projection supplies the advertised algorithm, which is
    /// how the advertised value and the published key can never disagree.
    /// </param>
    /// <param name="options">The configured issuance settings.</param>
    /// <param name="request">
    /// The incoming request, read for its origin ALONE and only in order to CHOOSE among base addresses
    /// the deployment has already declared. Nothing from it is ever published verbatim.
    /// </param>
    /// <param name="loggerFactory">
    /// Records one structured classifier on the failure path. Nothing is recorded on success.
    /// </param>
    /// <returns>
    /// 200 carrying the discovery metadata, or the contract's problem document when the published
    /// material or the configured addresses are not self-consistent.
    /// </returns>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// EVERY MEMBER IS COMPOSED FROM CONFIGURATION, NEVER FROM THE REQUEST, AND NEVER BY FETCHING
    /// ANYTHING. Both published addresses are a configured base address joined to a configured path.
    /// Nothing outbound is called in order to build the document (constraint C-A), and no host appears in
    /// code (constraint C-F).
    /// </para>
    /// <para>
    /// THE IDENTITY IS THE CONFIGURED ISSUER ON EVERY RESPONSE. The <c>issuer</c> member is
    /// <see cref="SecurityOptions.Issuer"/> verbatim and does not vary with the caller, because all three
    /// consuming services compare the <c>iss</c> claim of every token against that value byte for byte.
    /// </para>
    /// <para>
    /// THE LOCATION IS CHOSEN AMONG DECLARED ADDRESSES, WHICH IS A DIFFERENT THING FROM REFLECTING THE
    /// REQUEST, AND THE DISTINCTION IS THE WHOLE DESIGN. One service is reachable on two addresses on the
    /// documented topology: the three internal verifiers arrive as the Compose service name, which is
    /// also the issuer, while an operator, the end-to-end suite and any third-party consumer arrive
    /// through the published host port on a loopback name. A document composed from the issuer alone
    /// answered that second group with a <c>jwks_uri</c> naming a host that resolves only inside the
    /// Compose network - so it parsed, looked correct, and pointed at nothing they could reach, failing
    /// as a DNS error during key retrieval rather than as a rejected document.
    /// <see cref="SecurityOptions.PublishedOrigins"/> lets the deployment declare that second address,
    /// and this method selects between the declared candidates by MATCHING the request's own origin
    /// against them.
    /// </para>
    /// <para>
    /// WHY THAT DOES NOT REINSTATE THE FORGERY THE EARLIER REVISION REMOVED. Reflecting the request built
    /// the published address FROM caller-controlled input, so any <c>Host</c> header a caller chose became
    /// the address every consumer was told to fetch this issuer's keys from - and a stock bearer handler
    /// follows <c>jwks_uri</c> without question. Here the request cannot contribute a value: it can only
    /// select one the deployment already configured, and an origin that matches nothing configured falls
    /// back to the canonical issuer. The set of addresses this document can possibly publish is therefore
    /// exactly the configured set, whatever any caller sends, so the worst a forged <c>Host</c> header
    /// achieves is the canonical document it would have received anyway. Host filtering already
    /// constrains the accepted host NAMES independently; this check is what stops an accepted name being
    /// treated as a publishable one.
    /// </para>
    /// <para>
    /// THE MATCH IS ON ORIGIN - SCHEME, HOST AND PORT - AND ON NOTHING ELSE. A path base is deliberately
    /// not read: a path-prefixing proxy remains a deployment configuration matter, expressed by declaring
    /// the prefixed base address, which is the honest place for it and the position the earlier revision
    /// established. The comparison is ordinal and case-insensitive because a scheme and a host are
    /// case-insensitive by specification while a port is numeric.
    /// </para>
    /// <para>
    /// THE PUBLISHED ADDRESS AND THE ISSUER MAY THEREFORE DIFFER IN HOST, AND THAT IS THE POINT RATHER
    /// THAN A LOOSENED INVARIANT. A consumer arriving on either origin validates against the same
    /// identity and retrieves keys from an address it can actually resolve. What is no longer required is
    /// that one address serve two networks, which was never satisfiable.
    /// </para>
    /// <para>
    /// THE ADVERTISED ALGORITHM SET IS DERIVED FROM THE PUBLISHED KEYS THEMSELVES rather than from
    /// configuration, so the advertised value and the value on the key are the same value by
    /// construction rather than by agreement. It is de-duplicated because the published shape requires
    /// unique members, and it survives a future key set carrying more than one key without changing.
    /// </para>
    /// <para>
    /// MEMBER FOR MEMBER A SUBSET OF THE AUTHORED SCHEMA, AND THE OMISSIONS ARE REASONED. Both required
    /// members are present, together with the issuance address the configured setting's own
    /// documentation says this operation advertises and the algorithm list a stock consumer reads in
    /// order to constrain what it will accept. The three purely informational collections the schema
    /// also permits are NOT published: this issuer performs no interactive grant of any kind and the
    /// authored contract states outright that the issuance operation is not a grant endpoint, so naming
    /// a grant type, a subject type or a response type would advertise a capability that does not
    /// exist. That is the same reasoning the authored contract gives for omitting the interactive-flow
    /// and end-user members, applied consistently.
    /// </para>
    /// <para>
    /// Synchronous, for the same reason as the key set: composing two addresses and copying four
    /// members performs no input or output.
    /// </para>
    /// </remarks>
    internal static IResult PublishProviderMetadata(
        SigningKeyProvider signingKeys,
        IOptions<SecurityOptions> options,
        HttpRequest request,
        ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(signingKeys);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        SecurityOptions security = options.Value;
        PublishedJsonWebKeySet published = signingKeys.PublishedKeySet;

        // The key-set check runs first and unchanged, so the two publications can never disagree about
        // the material: whatever makes the key set unpublishable makes the document that addresses it
        // unpublishable too.
        string? inconsistency =
            DescribeKeySetInconsistency(published, security)
            ?? DescribeMetadataInconsistency(security);

        if (inconsistency is not null)
        {
            return Fail(inconsistency, loggerFactory);
        }

        // The location half. This SELECTS among addresses the deployment declared; it never adopts one
        // from the request. The identity half below stays the configured issuer regardless.
        string publishedBase = SelectPublishedBaseAddress(security, request);

        ProviderMetadataDocument metadata = new()
        {
            // The IDENTITY stays the canonical issuer whichever origin asked, because three verifiers
            // compare the 'iss' claim against it byte for byte.
            Issuer = security.Issuer,
            JsonWebKeySetUri = BuildAbsoluteAddress(publishedBase, security.JwksPath),
            TokenEndpoint = BuildAbsoluteAddress(publishedBase, security.TokenEndpointPath),
            SigningAlgorithms =
            [
                .. published.Keys
                    .Select(static key => key.Algorithm)
                    .Distinct(StringComparer.Ordinal),
            ],
        };

        return TypedResults.Ok(metadata);
    }

    /// <summary>
    /// Chooses which configured base address the discovery document should publish its two locations
    /// from, by matching the request's own origin against the declared candidates.
    /// </summary>
    /// <param name="security">The configured issuance settings.</param>
    /// <param name="request">The incoming request, read for scheme, host and port only.</param>
    /// <returns>
    /// The matching entry of <see cref="SecurityOptions.PublishedOrigins"/>, or
    /// <see cref="SecurityOptions.Issuer"/> when the request's origin matches no declared candidate -
    /// including when it matches the issuer's own origin, which needs no entry.
    /// </returns>
    /// <remarks>
    /// <para>
    /// THE RETURN VALUE IS ALWAYS A CONFIGURED STRING, NEVER A COMPOSED ONE. Nothing from the request is
    /// concatenated into the result: the request decides WHICH configured value is returned and cannot
    /// contribute any part of it. That is the property that separates this from the request-reflecting
    /// revision that was removed as forgeable, and it is enforced structurally - the only expressions
    /// that can be returned are <c>security.PublishedOrigins[i]</c> and <c>security.Issuer</c>.
    /// </para>
    /// <para>
    /// THE EMPTY-COLLECTION PATH RETURNS IMMEDIATELY, so a deployment that configures no additional origin
    /// behaves exactly as it did before this member existed, and pays no per-request cost for a feature it
    /// does not use. The shipped default is empty.
    /// </para>
    /// <para>
    /// A MALFORMED ENTRY CANNOT REACH HERE - <c>SecurityOptionsValidator</c> refuses startup on one - so
    /// the parse below is a total function in practice. It is still written defensively rather than with
    /// an assertion, because a discovery document is the wrong place to throw: an unparseable entry simply
    /// does not match, and the canonical issuer is published, which is the same safe outcome as an
    /// unrecognised origin.
    /// </para>
    /// <para>
    /// <see cref="HttpRequest.Host"/> IS USED RATHER THAN THE RAW HEADER because it is the parsed,
    /// host-filtered value: <c>Host.Value</c> carries the port when one was sent and omits it otherwise,
    /// which is exactly the shape an origin comparison needs. A request with no host at all - which an
    /// in-process test host produces - matches nothing and takes the issuer, deliberately.
    /// </para>
    /// </remarks>
    internal static string SelectPublishedBaseAddress(SecurityOptions security, HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(security);
        ArgumentNullException.ThrowIfNull(request);

        if (security.PublishedOrigins.Count == 0)
        {
            return security.Issuer;
        }

        if (!request.Host.HasValue || string.IsNullOrEmpty(request.Scheme))
        {
            return security.Issuer;
        }

        // BOTH SIDES ARE NORMALISED THROUGH THE SAME PARSER, WHICH MATTERS FOR THE DEFAULT PORT. Uri
        // renders an authority with its scheme's default port omitted, so a candidate spelled
        // "https://proxy:443" and a request arriving as "Host: proxy" reduce to one string instead of
        // missing each other - the case a hand-built comparison gets wrong. Scheme and host are
        // case-insensitive by specification and the port is numeric, so one case-insensitive ordinal
        // comparison then covers the whole origin.
        if (!Uri.TryCreate(
                string.Concat(request.Scheme, Uri.SchemeDelimiter, request.Host.Value),
                UriKind.Absolute,
                out Uri? arrivedOn))
        {
            return security.Issuer;
        }

        string requestOrigin = arrivedOn.GetLeftPart(UriPartial.Authority);

        // THE IN-NETWORK VIEW IS DECIDED BY THE ISSUER RATHER THAN BY THE ROSTER, and the order matters
        // for one real configuration: a deployment that ALSO lists its primary origin for completeness
        // must publish exactly what it published before listing it - the issuer's own spelling, not the
        // roster entry's, which may differ from it by a trailing separator or by case in the host while
        // naming the same origin.
        if (Uri.TryCreate(security.Issuer, UriKind.Absolute, out Uri? configuredIssuer)
            && string.Equals(
                configuredIssuer.GetLeftPart(UriPartial.Authority),
                requestOrigin,
                StringComparison.OrdinalIgnoreCase))
        {
            return security.Issuer;
        }

        foreach (string candidate in security.PublishedOrigins)
        {
            if (!Uri.TryCreate(candidate, UriKind.Absolute, out Uri? parsed))
            {
                continue;
            }

            if (string.Equals(
                    parsed.GetLeftPart(UriPartial.Authority),
                    requestOrigin,
                    StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return security.Issuer;
    }

    /// <summary>
    /// Projects the published material onto the wire shape, member by member, through an explicit
    /// allow-list.
    /// </summary>
    /// <param name="publishedKeySet">The public-only projection from the signing-key layer.</param>
    /// <returns>The key set as it will be serialized.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="publishedKeySet"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// THIS METHOD IS THE ALLOW-LIST, AND IT IS WRITTEN AS SIX NAMED ASSIGNMENTS ON PURPOSE. Each
    /// published member is copied by name into a shape that declares only those six. There is no
    /// reflection, no mapping convention, no serializer configuration and no library key object
    /// anywhere on the path from the signing-key layer to the response, so the set of members that can
    /// possibly appear on the wire is exactly the set enumerated below and a reviewer can confirm it by
    /// counting assignments.
    /// </para>
    /// <para>
    /// THE OMISSION OF PRIVATE MATERIAL IS STRUCTURAL, NOT AN OMISSION IN THIS METHOD.
    /// <see cref="JsonWebKeyDocument"/> declares no private and no symmetric member, so even a future
    /// edit that tried to copy one would not compile. That is why the allow-list is expressed as a type
    /// rather than as a filter: a filter can be bypassed, and a type with nowhere to put a value
    /// cannot.
    /// </para>
    /// <para>
    /// The optional permitted-operations member the authored schema also allows is deliberately not
    /// published. A key set that carries no private component cannot be used to sign in the first
    /// place, so stating verification-only would restate what the absence of a private member already
    /// guarantees, and every member not published is one fewer member to keep correct.
    /// </para>
    /// <para>
    /// The key family and the intended use are COPIED THROUGH rather than checked against literals
    /// here. The signing-key layer holds those two as compiled constants with a single call site and is
    /// the single source of truth for them; a literal in this file would be a second spelling able to
    /// drift from it. Their published values are asserted by the sibling test project, which is where a
    /// value assertion belongs.
    /// </para>
    /// <para>
    /// <c>internal</c> so the sibling test project can exercise the projection directly - asserting the
    /// exact set of serialized member names rather than only the absence of the forbidden ones, which
    /// is the assertion that would fail if anyone ever replaced this shape with a library key object.
    /// </para>
    /// <para>
    /// A default or empty collection is projected as an empty result rather than faulting. That state
    /// is refused by <see cref="DescribeKeySetInconsistency"/> before this method is reached on either
    /// route, so it never reaches the wire; this method stays total so that it is safe to call directly
    /// from a test with any input.
    /// </para>
    /// </remarks>
    internal static JsonWebKeySetDocument ProjectKeySet(PublishedJsonWebKeySet publishedKeySet)
    {
        ArgumentNullException.ThrowIfNull(publishedKeySet);

        ImmutableArray<PublishedJsonWebKey> keys = publishedKeySet.Keys;

        if (keys.IsDefaultOrEmpty)
        {
            return new JsonWebKeySetDocument { Keys = [] };
        }

        List<JsonWebKeyDocument> projected = new(keys.Length);

        foreach (PublishedJsonWebKey key in keys)
        {
            projected.Add(new JsonWebKeyDocument
            {
                KeyType = key.KeyType,
                Use = key.Use,
                Algorithm = key.Algorithm,
                KeyId = key.KeyId,
                Modulus = key.Modulus,
                Exponent = key.Exponent,
            });
        }

        return new JsonWebKeySetDocument { Keys = projected };
    }

    /// <summary>
    /// Reports whether the published material agrees with the configured issuance settings.
    /// </summary>
    /// <param name="publishedKeySet">The public-only projection from the signing-key layer.</param>
    /// <param name="options">The configured issuance settings.</param>
    /// <returns>
    /// <see langword="null"/> when the material is publishable; otherwise the fixed sentence describing
    /// the disagreement, suitable for an anonymous response body.
    /// </returns>
    /// <exception cref="ArgumentNullException">Either argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// FOUR PROPERTIES ARE CHECKED, AND EACH ONE BREAKS EVERY TOKEN IF IT DOES NOT HOLD. There is a key
    /// to publish at all; the identifier on it is the one a minted token's header will carry; the
    /// algorithm on it is both the configured one and a member of the closed set this issuer signs
    /// with; and all six members carry a value.
    /// </para>
    /// <para>
    /// COMPARISONS ARE ORDINAL AND CASE-SENSITIVE, matching how the signing-key layer resolves the same
    /// two settings - it stores the identifier verbatim and untrimmed and switches over the algorithm
    /// ordinally. Comparing any other way here would let this check pass a value that layer refuses, or
    /// refuse one it accepts, and put two files in one service at odds about their own contract.
    /// </para>
    /// <para>
    /// THE CLOSED ALGORITHM SET IS CONSUMED, NOT RESTATED.
    /// <see cref="SecurityOptionsValidator.PermittedSigningAlgorithms"/> already publishes it as the
    /// options contract's own closed set, and its three members correspond to the three digests the
    /// oracle's own comment names as the argument set of its signature primitive
    /// [<c>ws_objects/pfw.shared.pbl.src/enums.sru:L927-L933</c>]. Restating the set here - or
    /// re-deriving the digest mapping from the ported constants - would make a third spelling of one
    /// decision, and three spellings drift.
    /// </para>
    /// <para>
    /// EVERY RETURNED SENTENCE IS A FIXED CONSTANT WITH NO INTERPOLATION. These strings reach an
    /// ANONYMOUS response body, so none of them may carry a configured value, a key reference or any
    /// part of the material - and none can, because none has a substitution point. Which of the six
    /// members was absent is deliberately not distinguished for the same reason.
    /// </para>
    /// <para>
    /// The well-known namespace constraint on the two metadata paths is NOT re-checked here. It is
    /// enforced once, at registration, where it can prevent the route from existing at all; repeating it
    /// per request would suggest it were recoverable at request time, which it is not.
    /// </para>
    /// <para>
    /// <c>internal</c> so the sibling test project can drive each arm directly instead of having to
    /// construct a host whose configuration disagrees with its own key.
    /// </para>
    /// </remarks>
    internal static string? DescribeKeySetInconsistency(
        PublishedJsonWebKeySet publishedKeySet,
        SecurityOptions options)
    {
        ArgumentNullException.ThrowIfNull(publishedKeySet);
        ArgumentNullException.ThrowIfNull(options);

        ImmutableArray<PublishedJsonWebKey> keys = publishedKeySet.Keys;

        if (keys.IsDefaultOrEmpty)
        {
            return EmptyKeySetDetail;
        }

        // ------------------------------------------------------------------------------------------
        // 🔴 THE IDENTIFIER RULE IS PER POSITION IN THE RING, NOT ONE VALUE FOR EVERY KEY. An earlier
        // revision required EVERY published key to carry Security:SigningKeyId, which was right while one
        // key existed and would have made a rollover unpublishable: the retiring entry carries
        // Security:RetiringSigningKeyId by construction, so the check would have reported the key set as
        // inconsistent and answered a problem document on the one path every verifier depends on.
        //
        // The active entry is first by construction, so `expected` is the active identifier for it and the
        // retiring identifier for anything after it. A set carrying more than two entries cannot arise -
        // the provider builds one or two - and if one ever did, its third entry would find an EMPTY
        // expected identifier and be reported, which is the fail-closed direction.
        // ------------------------------------------------------------------------------------------
        for (int index = 0; index < keys.Length; index++)
        {
            PublishedJsonWebKey key = keys[index];

            string expectedKeyId = index == 0
                ? options.SigningKeyId
                : options.RetiringSigningKeyId;

            if (!string.Equals(key.KeyId, expectedKeyId, StringComparison.Ordinal))
            {
                return KeyIdentifierDisagreementDetail;
            }

            if (!string.Equals(key.Algorithm, options.SigningAlgorithm, StringComparison.Ordinal))
            {
                return AlgorithmDisagreementDetail;
            }

            if (!SecurityOptionsValidator.PermittedSigningAlgorithms.Contains(
                    key.Algorithm,
                    StringComparer.Ordinal))
            {
                return AlgorithmNotPermittedDetail;
            }

            // All six members, checked together and reported as one condition. A blank modulus or
            // exponent would publish a key no consumer can verify with, and a blank identifier would
            // publish something that reads as an identity while selecting nothing.
            if (string.IsNullOrWhiteSpace(key.KeyType)
                || string.IsNullOrWhiteSpace(key.Use)
                || string.IsNullOrWhiteSpace(key.Algorithm)
                || string.IsNullOrWhiteSpace(key.KeyId)
                || string.IsNullOrWhiteSpace(key.Modulus)
                || string.IsNullOrWhiteSpace(key.Exponent))
            {
                return IncompleteKeyDetail;
            }
        }

        return null;
    }

    /// <summary>
    /// Reports whether the discovery metadata can be published from the configured settings.
    /// </summary>
    /// <param name="options">The configured issuance settings.</param>
    /// <returns>
    /// <see langword="null"/> when the metadata is publishable; otherwise the fixed sentence describing
    /// the obstacle.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="options"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// THE ISSUER IS REQUIRED AND MUST BE ABSOLUTE, because a consumer validates the issuer claim of
    /// every inbound token against it and the published shape types it as an absolute address. An
    /// absent or relative value would be rejected by a conforming consumer before it ever reached a
    /// token, so publishing one would produce a document that looks valid and configures nothing.
    /// </para>
    /// <para>
    /// BOTH PATHS ARE REQUIRED TO BE ROOTED BECAUSE THAT IS WHAT MAKES THEM COMPOSABLE. An unrooted
    /// path cannot be joined to a request's scheme and host at all, and the framework's address helper
    /// raises an argument fault for one. Checking here converts that fault into the contract's own error
    /// shape, which is what an anonymous caller must receive instead of a framework fault page.
    /// </para>
    /// <para>
    /// The issuance path is validated here rather than at registration because this file declares no
    /// route for it: it is another file's route, and this operation is the only place its value is
    /// published. Failing the whole host over a setting this file does not own would be the wrong
    /// blast radius.
    /// </para>
    /// <para>
    /// <c>internal</c> for the same testability reason as its sibling check.
    /// </para>
    /// </remarks>
    internal static string? DescribeMetadataInconsistency(SecurityOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.Issuer))
        {
            return IssuerAbsentDetail;
        }

        if (!Uri.TryCreate(options.Issuer, UriKind.Absolute, out _))
        {
            return IssuerNotAbsoluteDetail;
        }

        if (!IsRootedPath(options.JwksPath) || !IsRootedPath(options.TokenEndpointPath))
        {
            return AddressNotRootedDetail;
        }

        return null;
    }

    /// <summary>
    /// Builds the contract's problem document for a publication that must not proceed.
    /// </summary>
    /// <param name="detail">One of this file's fixed diagnostic sentences.</param>
    /// <param name="loggerFactory">The factory the structured classifier is written through.</param>
    /// <returns>A 500 problem result carrying the legacy return code.</returns>
    /// <remarks>
    /// <para>
    /// THE SHARED FACTORY IS CONSUMED RATHER THAN A SECOND ERROR SHAPE INVENTED. The sibling
    /// authenticated route in this folder owns the one <c>application/problem+json</c> builder for this
    /// service, and the authored contract defines exactly one error body for every response it declares,
    /// so a consumer writes one error handler.
    /// </para>
    /// <para>
    /// NO STATUS OVERRIDE IS PASSED, AND THAT IS DELIBERATE RATHER THAN AN OMISSION. Every condition
    /// this file reports is a fault internal to this service, which is exactly what the shared map
    /// already answers for <see cref="RetCode.E_INTERNAL_ERROR"/>, and it is the code the authored
    /// contract names first on the server-error response both operations declare. Passing a status
    /// explicitly would add a second opinion where the shared one is already correct.
    /// </para>
    /// <para>
    /// The severity is the legacy dialog's most serious icon, because none of these conditions is
    /// recoverable by the caller: the service cannot publish its own verification material. It is stated
    /// at the call site rather than defaulted, which is what the shared factory requires of every
    /// caller.
    /// </para>
    /// <para>
    /// NO LOCALIZATION CATEGORY IS PASSED. These sentences are net-new operational diagnostics with no
    /// legacy dialog behind them - the legacy has no token issuer and therefore no message for this
    /// condition - so there is no category to preserve, and inventing one would fabricate provenance.
    /// </para>
    /// <para>
    /// THE FACTORY CANNOT LEAK. Its signature has no parameter for a token, a key, a key reference, an
    /// initialization vector or a request payload, so neither the body it builds nor the record it
    /// writes can carry one. The record itself carries only classifiers - the code, its symbolic name,
    /// the status and the severity - and never the modulus, the exponent, the two of them together, any
    /// private component, or the configured value the signing material arrived in.
    /// </para>
    /// </remarks>
    private static ProblemHttpResult Fail(string detail, ILoggerFactory loggerFactory) =>
        ProblemResults.Create(
            retCode: RetCode.E_INTERNAL_ERROR,
            detail: detail,
            severity: ProblemSeverity.StopSign,
            loggerFactory: loggerFactory);

    /// <summary>
    /// Joins a configured path to a configured base address.
    /// </summary>
    /// <param name="baseAddress">
    /// A validated configured base address - either the canonical issuer or the entry of
    /// <see cref="SecurityOptions.PublishedOrigins"/> the request's origin selected. The caller has
    /// already established that it is present and absolute, and the options validator holds both sources
    /// to the same shape rule.
    /// </param>
    /// <param name="path">A rooted configured path.</param>
    /// <returns>The absolute address a consumer should fetch.</returns>
    /// <remarks>
    /// <para>
    /// COMPOSED FROM CONFIGURATION, NEVER FROM THE REQUEST, AND THAT IS AN INTEGRITY PROPERTY RATHER
    /// THAN A STYLE CHOICE. Joining each path to the incoming request's scheme, host and path base is the
    /// idiomatic way to build an absolute address, and it is wrong here: those three are all
    /// CALLER-CONTROLLED - a Host header, an <c>X-Forwarded-*</c> header honoured by a proxy - so a request
    /// carrying a host of the caller's choosing would be answered with a discovery document telling every
    /// consumer to fetch this issuer's verification keys from that host. A consumer's stock bearer handler follows <c>jwks_uri</c>
    /// without question, which is the entire reason this document exists, so the document is exactly the
    /// wrong place to reflect caller input back.
    /// </para>
    /// <para>
    /// THE CALLER MAY SELECT AMONG CONFIGURED BASE ADDRESSES, WHICH IS NOT THE SAME THING AND IS WHY THIS
    /// PARAMETER IS NO LONGER NAMED FOR THE ISSUER. <c>SelectPublishedBaseAddress</c> matches the
    /// request's origin against the declared set and returns one of those declared values or the issuer;
    /// the request contributes no character to what arrives here. So the invariant this method depends on
    /// is unchanged - every value it composes came from configuration - while the document can name the
    /// address the consumer actually reached this service on.
    /// </para>
    /// <para>
    /// THE ISSUER REMAINS THE ANCHOR OF THE CONTRACT AND THE DEFAULT SOURCE HERE. It is the <c>iss</c>
    /// claim of every minted token, the <c>issuer</c> member of this very document, and the value all
    /// three consuming services validate every token against. It is what this method composes whenever
    /// the deployment declares no additional origin or the request arrived on an origin that matches none
    /// - which is every case the previous revision handled, composed identically.
    /// </para>
    /// <para>
    /// A PATH-PREFIXING PROXY IS THEREFORE A DEPLOYMENT CONFIGURATION MATTER, WHICH IS THE HONEST PLACE
    /// FOR IT. Reflecting the request's path base made a prefixed deployment work without configuration
    /// and made every deployment forgeable. A configured base address that includes the prefix - the
    /// issuer, or a declared published origin - produces the same published address with none of the
    /// exposure, and the issuer has to be correct for a consumer's validation to pass regardless.
    /// </para>
    /// <para>
    /// One trailing separator on the base address is dropped before joining. Both spellings of an
    /// authority are legitimate in configuration, and the joined address must not carry a doubled
    /// separator - the configured value itself is never rewritten, which matters most for the issuer,
    /// because that one is also published verbatim as the identity and has to match the token claim byte
    /// for byte.
    /// </para>
    /// </remarks>
    private static string BuildAbsoluteAddress(string baseAddress, string path) =>
        string.Concat(baseAddress.TrimEnd('/'), path);

    /// <summary>
    /// Requires a configured metadata path to be rooted and inside the well-known namespace.
    /// </summary>
    /// <param name="configured">The configured path.</param>
    /// <param name="settingName">
    /// The setting's own member name, supplied through <c>nameof</c> so the diagnostic names it without
    /// this file re-spelling it.
    /// </param>
    /// <returns>The path, VERBATIM, for use as the route pattern.</returns>
    /// <exception cref="InvalidOperationException">
    /// The path is not rooted, is not inside the well-known namespace, or names nothing after the
    /// prefix.
    /// </exception>
    /// <remarks>
    /// <para>
    /// RETURNED UNCHANGED, deliberately. The route pattern and the address the discovery document
    /// advertises must be the same string, and a value this method quietly reshaped would be a second
    /// spelling of the one thing those two have to agree on.
    /// </para>
    /// <para>
    /// The prefix is consumed from <see cref="SecurityOptionsValidator.WellKnownMetadataPathPrefix"/>,
    /// so the guard and the options validator cannot describe different namespaces. Requiring something
    /// AFTER the prefix rejects the bare prefix itself, which would otherwise map an anonymous route
    /// onto the namespace root.
    /// </para>
    /// <para>
    /// The message names the offending setting and NEVER echoes its value, matching the posture the
    /// options validator takes. It is a startup diagnostic rather than a response body, so naming the
    /// setting is the helpful thing to do; echoing a value would not be.
    /// </para>
    /// </remarks>
    private static string RequireWellKnownPath(string configured, string settingName)
    {
        string prefix = SecurityOptionsValidator.WellKnownMetadataPathPrefix;

        if (!IsRootedPath(configured)
            || !configured.StartsWith(prefix, StringComparison.Ordinal)
            || configured.Length <= prefix.Length)
        {
            throw new InvalidOperationException(
                string.Concat(
                    SecurityOptions.SectionName,
                    ":",
                    settingName,
                    MetadataPathOutsideNamespaceMessage));
        }

        return configured;
    }

    /// <summary>
    /// Reports whether a configured path can be used as a route pattern and composed into an address.
    /// </summary>
    /// <param name="path">The configured path.</param>
    /// <returns>
    /// <see langword="true"/> when the value is present and rooted; otherwise <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// Whitespace counts as absent, because a whitespace path would be accepted by a presence test and
    /// then compose into an address that addresses nothing.
    /// </remarks>
    private static bool IsRootedPath(string? path) =>
        !string.IsNullOrWhiteSpace(path) && path.StartsWith('/');

    /// <summary>
    /// Declares in the generated document that the operation carries NO security requirement.
    /// </summary>
    /// <param name="operation">The operation being described.</param>
    /// <param name="context">The transformer context. Deliberately not used - see the remarks.</param>
    /// <param name="cancellationToken">Cancels document generation.</param>
    /// <returns>A completed task; the transformation is synchronous.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="operation"/> or <paramref name="context"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// AN EXPLICIT EMPTY REQUIREMENT LIST IS ASSIGNED, AND THE EMPTINESS IS THE STATEMENT. The authored
    /// contract overrides its document-level bearer requirement on both these operations with an empty
    /// requirement LIST, and records that the empty list and a one-item list holding an empty
    /// requirement are not interchangeable: the empty list says no requirement applies, whereas the
    /// other says one applies and is satisfied by nothing. This transformer reproduces the form the
    /// contract states.
    /// </para>
    /// <para>
    /// IT IS THE DOCUMENT-SIDE COUNTERPART OF <c>AllowAnonymous</c>, AND IT IS DEFENCE IN DEPTH RATHER
    /// THAN DECORATION. The generator synthesises no security requirement from authorization metadata by
    /// itself, so today these operations would carry none even without this transformer. The value is in
    /// what happens later: if a document-wide requirement is ever registered, an operation carrying no
    /// requirement of its own INHERITS it, and the published description would then claim these two
    /// anonymous addresses need a token they do not need - and cannot need. An explicit empty list is
    /// immune to that, exactly as the explicit <c>AllowAnonymous</c> is immune to a change in the
    /// fallback policy.
    /// </para>
    /// <para>
    /// SHARED BY BOTH ROUTES, because the property is identical on both and one method makes that
    /// visible. It is assigned unconditionally rather than only when the list is empty: the whole point
    /// is that whatever else may have been added, these operations require nothing.
    /// </para>
    /// <para>
    /// NO SECURITY SCHEME IS REGISTERED AND THE DOCUMENT IS NOT OTHERWISE TOUCHED. An operation that
    /// requires nothing references no scheme, so adding one would describe a mechanism these addresses
    /// do not use. The context is guarded for consistency with the sibling transformer in this folder
    /// and then left alone, which is itself the statement that nothing document-wide is being changed
    /// from here.
    /// </para>
    /// <para>
    /// A per-endpoint operation transformer is the sanctioned mechanism for this on this toolchain. The
    /// older per-endpoint OpenAPI configuration extension is deprecated here and raises a diagnostic
    /// that the repository-wide warnings-as-errors setting turns into a build failure, so it is not an
    /// option even as a fallback.
    /// </para>
    /// </remarks>
    private static Task DeclareAnonymousAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(context);

        cancellationToken.ThrowIfCancellationRequested();

        operation.Security = [];

        return Task.CompletedTask;
    }
}

/// <summary>
/// The published key set: the keys with which tokens issued by this service can be verified.
/// </summary>
/// <remarks>
/// <para>
/// SERIALIZES TO EXACTLY ONE MEMBER, matching the authored contract's set schema, which requires that
/// member and permits no other. Where this shape and that document ever disagree, THE DOCUMENT WINS AND
/// THIS SHAPE CHANGES, because the document is what a consumer's stock handler reads.
/// </para>
/// <para>
/// Declared at file scope rather than nested inside <see cref="JwksEndpoints"/>, matching the sibling
/// endpoint files in this folder, so the generated schema is named for the shape rather than for the
/// class that happens to return it.
/// </para>
/// <para>
/// The collection may legitimately carry exactly one key, and does in this phase. The shape admits more
/// so that a key rollover is EXPRESSIBLE and a consumer can select by identifier; rotation machinery is
/// not added here, so one configured key yields one entry.
/// </para>
/// </remarks>
public sealed record JsonWebKeySetDocument
{
    /// <summary>
    /// The published keys. Public parameters only.
    /// </summary>
    /// <remarks>
    /// <c>required</c> rather than defaulted, so this shape can never be constructed without a decision
    /// about its only member. An empty collection is refused before it reaches the wire by the
    /// publication route's consistency check, because a healthy-looking response carrying nothing to
    /// verify against would make every consumer reject every token while both sides reported success.
    /// </remarks>
    [JsonPropertyName("keys")]
    public required IReadOnlyList<JsonWebKeyDocument> Keys { get; init; }
}

/// <summary>
/// One published verification key. PUBLIC PARAMETERS ONLY.
/// </summary>
/// <remarks>
/// <para>
/// THE PROHIBITION ON PRIVATE MATERIAL IS STRUCTURAL RATHER THAN ADVISORY, AND THIS DECLARATION IS
/// WHERE IT IS ENFORCED. This shape declares no private and no symmetric member: there is no private
/// exponent, no prime factor, no exponent remainder, no coefficient and no symmetric key value, and
/// NONE MAY EVER BE ADDED. Publishing any one of them on an anonymous address would hand every caller
/// the ability to mint tokens indistinguishable from legitimate ones, and would defeat the sole-issuer
/// topology of the whole system in a single request.
/// </para>
/// <para>
/// Because the members are ABSENT FROM THE SHAPE rather than merely left unset, no serializer setting,
/// no naming policy, no future change to a shared serialization option and no upstream change to the
/// signing-key layer can surface one. This is the reason the publication route projects into this shape
/// member by member instead of serializing a general-purpose key object from the token library: such an
/// object is designed to carry private members when it has them, so handing one to a serializer would
/// make this route's safety a property of another type rather than of this one.
/// </para>
/// <para>
/// EXACTLY SIX MEMBERS, AND THEIR SPELLINGS ARE THE CONTRACT'S. Four of them the authored key schema
/// requires - the key family, the identifier, the modulus and the exponent - and two more it permits and
/// this issuer always sends, so a consumer never has to infer the use or the algorithm. The one further
/// member that schema permits, the permitted-operations list, is deliberately not published: a key with
/// no private component cannot sign, so declaring verification-only would restate what the absence of a
/// private member already guarantees.
/// </para>
/// <para>
/// NO SPECIMEN VALUE APPEARS ON ANY MEMBER, and none may be added. An encoded blob in an example is
/// indistinguishable from real key material to a reader, and specimen keys have a long history of being
/// copied into a deployment unchanged. The members are DESCRIBED instead - the same position the authored
/// contract takes on the same schema, where no field carries an example either.
/// </para>
/// <para>
/// A RECORD HERE, WHERE THE PRODUCING TYPE IN THE SIGNING-KEY LAYER IS NOT ONE, AND THE DIFFERENCE IS
/// REASONED. That type avoids a generated rendering and value comparison because it is the type that
/// TOUCHES the key, and it declines to establish the habit there. This one exists only to shape a
/// response that is already public by definition, and it is a record because a value-shaped response
/// shape is what the rest of this folder uses; a generated rendering of it can disclose nothing that the
/// route it feeds does not already publish anonymously.
/// </para>
/// <para>
/// ONE PROPERTY OF THE AUTHORED SCHEMA THE GENERATOR CANNOT RESTATE, recorded so the difference is known
/// rather than discovered. The generator emits no closure keyword for any shape in this service, so the
/// generated description does not repeat the authored schema's refusal of unknown members; and because
/// all six members here are required while the authored schema requires four, the generated description
/// is NARROWER than the authored one rather than wider. Both differences are safe in the same direction:
/// the behaviour is closed regardless, since this shape has exactly these six members and no
/// extension-data member, and it always populates all six. The authored document remains the place those
/// properties are stated, which is precisely why it is the authoritative one.
/// </para>
/// </remarks>
public sealed record JsonWebKeyDocument
{
    /// <summary>
    /// The key family. This issuer publishes asymmetric keys of one family only.
    /// </summary>
    /// <remarks>
    /// Copied from the signing-key layer, which holds the value as a compiled constant with a single
    /// call site and is its single source of truth. Not re-spelled here, so the two cannot drift.
    /// </remarks>
    [JsonPropertyName("kty")]
    [JsonPropertyOrder(1)]
    public required string KeyType { get; init; }

    /// <summary>
    /// The intended use, which is signature verification. An encryption key is never published here.
    /// </summary>
    /// <remarks>
    /// This set exists so that a recipient can VERIFY a token, which is why the use is stated rather
    /// than left for a consumer to assume. Copied from the signing-key layer for the same reason as the
    /// key family.
    /// </remarks>
    [JsonPropertyName("use")]
    [JsonPropertyOrder(2)]
    public required string Use { get; init; }

    /// <summary>
    /// The algorithm identifier this key is intended for.
    /// </summary>
    /// <remarks>
    /// Published so a consumer need not infer it from the key family, and so a consumer can constrain
    /// which algorithms it will accept - a constraint worth setting, because accepting whatever
    /// algorithm a token declares is a well-understood token-validation weakness. Its provenance is the
    /// digest set the oracle's own comment names as the argument set of its signature primitive
    /// [<c>ws_objects/pfw.shared.pbl.src/enums.sru:L927-L933</c>].
    /// </remarks>
    [JsonPropertyName("alg")]
    [JsonPropertyOrder(3)]
    public required string Algorithm { get; init; }

    /// <summary>
    /// The key identifier, which the header of every minted token repeats so a consumer can select the
    /// right key.
    /// </summary>
    /// <remarks>
    /// OPAQUE, AND SAFE TO PUBLISH: it is not parsed, it is not derived from the key, and it carries no
    /// key material. It is the one thing about the key besides its public parameters that is published
    /// at all, and it is published precisely because a consumer cannot select a key without it.
    /// </remarks>
    [JsonPropertyName("kid")]
    [JsonPropertyOrder(4)]
    public required string KeyId { get; init; }

    /// <summary>
    /// The PUBLIC modulus, encoded as the key-set format requires.
    /// </summary>
    /// <remarks>
    /// Public by definition - it is one half of what makes signature verification possible at all, and
    /// its counterpart in the legacy signature pair is the public key argument of the verification
    /// primitive [<c>ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L72-L73</c>]. It is never logged, in
    /// isolation or together with the exponent.
    /// </remarks>
    [JsonPropertyName("n")]
    [JsonPropertyOrder(5)]
    public required string Modulus { get; init; }

    /// <summary>
    /// The PUBLIC exponent, encoded as the key-set format requires.
    /// </summary>
    /// <remarks>
    /// Public by definition, and the other half of the verification parameters. Never logged, and never
    /// logged together with the modulus.
    /// </remarks>
    [JsonPropertyName("e")]
    [JsonPropertyOrder(6)]
    public required string Exponent { get; init; }
}

/// <summary>
/// The discovery metadata a stock bearer handler self-configures from.
/// </summary>
/// <remarks>
/// <para>
/// FOUR MEMBERS, ALL FOUR DECLARED BY THE AUTHORED SCHEMA. Both of that schema's required members are
/// present; the issuance address is present because the setting that owns that path states outright that
/// this operation advertises it, and because a consumer that trusted discovery could not otherwise obtain
/// a token at all; and the algorithm list is present because a stock consumer reads it when constraining
/// which algorithms it will accept.
/// </para>
/// <para>
/// THE THREE PURELY INFORMATIONAL COLLECTIONS THE SCHEMA ALSO PERMITS ARE OMITTED, AND THE OMISSION IS A
/// DECISION. This issuer performs no interactive grant of any kind, and the authored contract states
/// outright that the issuance operation is not a grant endpoint, so naming a grant type would advertise a
/// mechanism that does not exist and invite a consumer to attempt it; a subject type and a response type
/// describe an end-user flow, and there is no end user anywhere in this system. Publishing an empty
/// collection instead would be no better - it would still assert that the concept applies here. This is
/// the same reasoning the authored contract gives for omitting the interactive-flow and end-user members,
/// applied consistently to the members it left optional.
/// </para>
/// <para>
/// THE ISSUER IS AN IDENTITY AND THE OTHER TWO ARE LOCATIONS, which is why they are sourced differently.
/// The issuer comes from configuration and never varies, because a consumer validates the issuer claim of
/// every inbound token against it byte for byte and it must therefore be identical whichever address the
/// metadata was fetched through. The two addresses also come from configuration, but from whichever
/// declared base address matches the origin the request arrived on - because an address that does not
/// resolve for the caller that read it configures nothing, and one service on this topology is genuinely
/// reachable on two addresses. The request SELECTS among declared values and contributes none, so the
/// locations can vary without becoming forgeable.
/// </para>
/// <para>
/// Declared at file scope for the same reason as its siblings, and its members carry serialization
/// attributes because the published names are fixed by the standards a stock handler reads and are not
/// the conventional rendering of any C# name.
/// </para>
/// </remarks>
public sealed record ProviderMetadataDocument
{
    /// <summary>
    /// The issuer identifier every consumer validates the issuer claim of an inbound token against.
    /// </summary>
    /// <remarks>
    /// Published from configuration VERBATIM. It must match, byte for byte, the value this service
    /// stamps into a token, so nothing here trims, normalizes or re-cases it.
    /// </remarks>
    [JsonPropertyName("issuer")]
    [JsonPropertyOrder(1)]
    public required string Issuer { get; init; }

    /// <summary>
    /// The absolute address of this service's key set.
    /// </summary>
    /// <remarks>
    /// THE MEMBER THAT MAKES ZERO-BESPOKE-CODE VERIFICATION POSSIBLE: a consumer points its stock handler
    /// here and never fetches a key itself. Composed from a CONFIGURED base address plus the configured
    /// key-set path - the canonical issuer, or the declared published origin matching the one this request
    /// arrived on - so it resolves for whoever read it while remaining a value the deployment chose. No
    /// host is fixed in code and no caller-supplied host is ever published.
    /// </remarks>
    [JsonPropertyName("jwks_uri")]
    [JsonPropertyOrder(2)]
    public required string JsonWebKeySetUri { get; init; }

    /// <summary>
    /// The absolute address of the issuance operation on this same service.
    /// </summary>
    /// <remarks>
    /// Published for completeness, and composed the same way from the configured issuance path, which is
    /// the single declared source of truth for it. Note that the operation is protected by a transport
    /// credential and is NOT a standard authorization grant endpoint, which is why no grant metadata
    /// accompanies this member.
    /// </remarks>
    [JsonPropertyName("token_endpoint")]
    [JsonPropertyOrder(3)]
    public required string TokenEndpoint { get; init; }

    /// <summary>
    /// The signature algorithm identifiers this issuer uses.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE C# NAME AND THE PUBLISHED NAME DIVERGE DELIBERATELY. The published spelling is the one the
    /// standards fix and a stock consumer looks for, and it names an identity token; this issuer mints no
    /// identity token and has no end-user flow at all, so the member is named here for what it actually
    /// carries. Renaming the published member instead would make a stock consumer fail to find it, which
    /// is the one thing this whole document exists to prevent.
    /// </para>
    /// <para>
    /// Derived from the published keys themselves and de-duplicated, so the advertised set and the
    /// algorithm on each key are the same values by construction rather than by agreement, and the
    /// published shape's uniqueness requirement holds without a separate step.
    /// </para>
    /// </remarks>
    [JsonPropertyName("id_token_signing_alg_values_supported")]
    [JsonPropertyOrder(4)]
    public required IReadOnlyList<string> SigningAlgorithms { get; init; }
}
