// ==================================================================================================
//  AuthorizationTests.cs - WHO MAY CALL WHAT ON THE SECURITY SERVICE
//  ------------------------------------------------------------------------------------------------
//  WHY THIS SUITE EXISTS, AND WHY IT MATTERS MORE THAN ITS SIZE SUGGESTS
//
//  The legacy PowerFramework opens no listening socket, registers no route and receives no
//  unsolicited request: it is 39 libraries loaded into ONE process, and every call in it is an
//  in-process call that no credential could ever have been presented to. Decomposition therefore
//  creates the system's FIRST-EVER ingress, which is why "no new attack surface" cannot be read
//  literally - there was no surface at all to leave unchanged. It reads, and can only read, as
//  EVERY NEWLY CREATED SURFACE IS AUTHENTICATED FROM THE OUTSET. This file is the automated proof of
//  that reading for the Security service, and it is deliberately the narrowest kind of proof: it
//  asserts nothing about what an operation computes, only about who is allowed to reach it.
//
//  It is also where the OPERATIONAL readiness contract is pinned, because the anonymity of
//  GET /health is not a convenience. orchestration/docker-compose.yml expresses this service as a
//  health condition of the gateway, and the gateway is specified to report healthy only once
//  Persistence, DataServices AND Security do. A /health that demanded a credential would make that
//  condition unsatisfiable and deadlock the whole bring-up, so the anonymity assertion below protects
//  an orchestration property rather than a stylistic preference.
//
//  ==================================================================================================
//  THE POSTURE UNDER TEST, AS Program.cs ACTUALLY BUILDS IT
//  ==================================================================================================
//
//  A single authentication scheme - the stock JWT bearer handler - and a FALLBACK AUTHORIZATION
//  POLICY that requires an authenticated user. A fallback policy governs every endpoint that
//  declared no authorization metadata of its own, so the service is DEFAULT DENY: a route has to opt
//  out to be reachable without a credential, and exactly three do.
//
//  MEASURED, NOT ASSUMED. The endpoint inventory below was read out of the running host's own
//  EndpointDataSource rather than inferred from the source, and the guard at
//  TheAnonymousExemptionSetIsExactlyTheThreeContractRoutes asserts it every run:
//
//      3 endpoints carry IAllowAnonymous  -> GET /health
//                                            GET /.well-known/jwks.json
//                                            GET /.well-known/openid-configuration
//     20 endpoints carry IAuthorizeData   -> GET /v1/ping, POST /v1/tokens and the 17 C-02 operations
//      1 endpoint carries neither         -> the generated contract document, which is consequently
//                                            governed by the fallback policy and answers 401 to an
//                                            anonymous caller exactly as the twenty explicit ones do
//
//  Three is therefore an EXACT number rather than an approximate one, and treating it as exact is the
//  whole difference between a guard and a smoke test. A fourth anonymous route appearing for any
//  reason - a convenience exemption, a copied endpoint declaration, a policy refactor - fails this
//  suite, which is precisely what it is here to do.
//
//  POST /v1/tokens IS NOT AN ANONYMOUS BOOTSTRAP. Its route policy requires a TRANSPORT credential,
//  because a caller cannot present a bearer token in order to obtain its first bearer token; the
//  documented no-token path is the mutual-TLS scheme the authored contract declares on that operation
//  alone. An unauthenticated caller is therefore challenged rather than admitted, which is why
//  issuance appears in the PROTECTED table below alongside the cryptographic operations.
//
//  ==================================================================================================
//  THE ONE MEASURED FINDING THAT SHAPES A TEST'S STRUCTURE
//  ==================================================================================================
//
//  The inbound bearer handler validates a token's lifetime against the AMBIENT clock, not against the
//  TimeProvider this project substitutes. That was measured on the pinned toolchain, not assumed:
//  minting a token and then advancing the substituted clock past its expiry leaves the token
//  ACCEPTED, while the ping response's own timestamp moves - so the seam governs issuance instants and
//  the handler's output, and nothing else.
//
//  The consequence is structural rather than incidental. The expiry row cannot expire a token by
//  moving the clock forward from the instant it was minted at; it has to place ISSUANCE in the past,
//  and then advance the clock past the resulting expiry. MintAnExpiredToken does exactly that, in
//  three statements, and the middle one is the only one that had to be anywhere in particular. No
//  test below sleeps, waits, retries or polls: a sleeping test would be slow, would be flaky under a
//  loaded agent, and would still not have expired anything on a five-minute lifetime.
//
//  ==================================================================================================
//  CONSTRAINT COMPLIANCE - WHAT EACH GOVERNING CONSTRAINT REQUIRES OF THIS FILE SPECIFICALLY
//  ==================================================================================================
//
//  RULES POSITION, STATED EXPLICITLY BECAUSE ITS ABSENCE IS ITSELF A FINDING. The project's rules
//  document contains exactly one line: no user rules were provided. Nothing is invented in their
//  place, no convention is back-filled as though it had been a rule, and their absence is NOT treated
//  as licence to lower the bar. The enterprise-standard baseline applies instead, and for a test file
//  that means: warning-clean under warnings-as-errors, correct nullable annotations, every disposable
//  disposed, no secret in a fixture, no package added for convenience, no performance property
//  asserted - the repository publishes none - and no assertion that cannot fail.
//
//  C-G  NO NEW ATTACK SURFACE: EVERY NEW BOUNDARY AUTHENTICATED. This file IS the C-G proof for
//       Security, so it honours the constraint in its most literal form. NOTHING below disables
//       authentication, stubs the authentication handler into always succeeding, registers an
//       anonymous fallback, relaxes a route policy or sets a test-only skip-authentication flag; the
//       host is the service's own composition root, unmodified in every respect that bears on who may
//       call what. The ONLY way a test here reaches a protected route is with a token the service
//       itself minted through its own issuer and verifies against its own key. That restraint is what
//       makes the unauthorized assertions mean something: a suite that could reach a protected route
//       without a credential would go green while proving the opposite of what it claims, which is the
//       worst available outcome - worse than failing, because it would be believed.
//
//  C-L  THE ATTACHED ENVIRONMENT'S SETUP INSTRUCTIONS ARE BINDING OPERATIONAL CONSTRAINTS. The
//       documented contract is anonymous /health on every service and a JWT-required /v1/ping that
//       answers 401 without a token, with the gateway healthy only after its upstreams. This suite
//       asserts exactly that contract, at exactly those two paths, and substitutes neither: no
//       alternative health path, no alternative authenticated probe, and no relaxation of the 401.
//
//  C-F  NOTHING HARDCODED, AND THE NAMED SECRET SITES ARE A FLOOR RATHER THAN A CEILING. Every byte
//       of key material this file causes to exist is GENERATED AT RUN TIME - the companion host's
//       asymmetric key comes from the factory's own generator, which is RSA key generation on the
//       platform - and every token is MINTED AT RUN TIME by a real issuer. There is no key literal, no
//       armoured block, no base64 DER blob, no passphrase, no initialization vector and no token
//       literal anywhere below; the two rejected credential VALUES that are literals are readable
//       English non-credentials chosen precisely so that they cannot be mistaken for, and cannot
//       accidentally become, anything sensitive. None of the eight in-source sites inventoried in
//       docs/SECRETS.md is reproduced in any form: not the armoured private key at
//       tests/blink/test_jws.htm:L8-L22, not the two base64 private keys at
//       ws_objects/pfw.demos.pbl.src/u_cst_tabpage_utility_crypto.sru:L466 and :L718, and not the
//       shared symmetric configuration key at ws_objects/pfw.tests.pbl.src/n_cst_appconfig.sru:L22-L23.
//       Those are LOCATORS; their contents are not here and must never be pasted here. Nothing below
//       writes a token, a key or a configured value to test output, and where an assertion must
//       compare two pieces of key material it compares them behind a boolean and supplies its own
//       message, so a FAILURE cannot print either operand.
//
//  C-D  NOTHING FOR ANY DEFERRED SERVICE. The four reserved capability routes are Gateway's routing
//       metadata and belong to Gateway's own reserved-route and authorization suites. Not one of their
//       paths appears below, nor the not-implemented status they answer with, nor any prefix of one:
//       the Security service registers no route, handler, option or configuration key for any deferred
//       capability, so an assertion here expecting one would be asserting the existence of something
//       that must not exist.
//
//  C-C  THE LEGACY TREE IS READ ONLY AND IS THE BEHAVIOURAL ORACLE. Every legacy path in this file
//       appears in a comment and only in a comment. NOTHING below opens, reads, copies or
//       fixture-loads any path under ws_objects/ or under the pre-existing browser harness
//       directories in tests/, at construction time or at request time.
//
//  C-B  NO BEHAVIOUR IMPROVEMENTS. The contract is asserted as specified and not one requirement
//       beyond it. There is no assertion here demanding rate limiting, token revocation, audit
//       logging, scope-level or claim-level authorization policies, or enforcement of mutual TLS -
//       mutual TLS is documented as the per-pair FALLBACK rather than a phase-one requirement, so a
//       test that required it to be enforced would be inventing a requirement and calling it parity.
//
//  C-A  NO CROSS-SERVICE COUPLING. Only this service's host is exercised. No other service's project
//       is referenced, no client of another service is constructed, and the gateway is NOT booted in
//       order to observe Security's contribution to its readiness - that aggregation is asserted in
//       the gateway's own suite, against the gateway's own host.
//
//  C-H  80 PER CENT LINE COVERAGE PER SERVICE, MEASURED FROM COBERTURA. This file's named share is
//       Endpoints/PingEndpoints.cs and Endpoints/HealthEndpoints.cs. The ping handler is covered on
//       both of its arms - the resolved-clock arm through the host, and the ambient-clock fallback and
//       null guard through a direct call - and the single published error shape is covered through the
//       body assertions, which is the part of it that is easiest to leave uncovered because no
//       successful request ever produces it.
//
//  C-I  EACH SERVICE BUILDS AND TESTS INDEPENDENTLY. This file compiles and passes under the verbatim
//       per-service workflow from this service's directory - restore, release build, then test
//       collecting cross-platform coverage - with no sibling service, no shared solution and no
//       orchestration present. NO SOLUTION FILTER IS AUTHORED for it: the legacy filter format is
//       incompatible with the XML solution format this repository uses and fails restore, build and
//       test alike. NO PORT IS BOUND: every request below travels the in-memory transport, so this
//       service's real port is never contended for and a busy agent cannot fail a run for a reason
//       that has nothing to do with the code under test.
//
//  .editorconfig  NO PRESERVED-SPELLING IDENTIFIER IS DECLARED HERE. The repository's naming
//       suppressions are file-glob scoped to the production files that genuinely carry the legacy
//       screaming-snake constants, and the only one in this service is Crypto/LegacyDefaults.cs. A
//       test file is not in that scope, so with warnings as errors a preserved-spelling declaration
//       here would be a build error rather than a style nit. Where such a constant is meant it is
//       CONSUMED from PowerFramework.Shared.Kernel by name - RetCode.E_ACCESS_DENIED - and never
//       re-declared as a local with the same spelling.
//
//  ==================================================================================================
//  WHAT THIS SUITE DELIBERATELY DOES NOT ASSERT
//  ==================================================================================================
//
//  This suite is about who may call what. Four neighbouring concerns are owned elsewhere and are not
//  duplicated here, because a second copy of an assertion is a second thing to keep true:
//
//    * TOKEN CLAIM FIDELITY - what a minted token carries, and which requests the issuer refuses -
//      belongs to the issuance suite. Below, a token is only ever a credential to present.
//    * THE PUBLISHED KEY SET AND THE DISCOVERY METADATA - their content, their shape and the material
//      they do and do not expose - belong to the key-publication suite. Below, those two routes are
//      only ever addresses that must answer without a credential.
//    * CRYPTOGRAPHIC BEHAVIOUR - every algorithm, mode, padding and preserved weak default of the 17
//      C-02 operations - belongs to the cryptographic parity suites. Below, a C-02 route is only ever
//      an address that must NOT answer without a credential, and no request body is sent to one.
//    * THE RETURN-CODE ALGEBRA ITSELF - the tri-state hole, every value and every predicate - belongs
//      to the shared kernel's predicate suite, and the return-code-to-status projection belongs to the
//      suites of the two files that call it. Below, exactly one return code is asserted, the one the
//      unauthorized path produces, and it is read THROUGH Predicates rather than through a re-derived
//      comparison, because the algebra is tri-state and re-deriving it is how the hole gets closed by
//      accident.
// ==================================================================================================

// Deliberately NO serialization-helper import: every protected row sends a request with NO BODY at all,
// because authorization runs before model binding, so this suite serializes nothing and consequently
// takes no dependency on the seventeen cryptographic request schemas that belong to the parity suites.
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using PowerFramework.Security.Configuration;
using PowerFramework.Security.Endpoints;
using PowerFramework.Shared.Kernel;

namespace PowerFramework.Security.Tests;

/// <summary>
/// Names one way of presenting a credential that this service must refuse.
/// </summary>
/// <remarks>
/// <para>
/// DECLARED AS AN ENUMERATION, AND DECLARED PUBLICLY, FOR TWO REASONS THAT LEAVE NO ALTERNATIVE.
/// A theory's data member has to be public and static for the test framework to discover it, and a
/// public member cannot expose a less accessible type, so a private or internal enumeration could not
/// back the table it exists to describe. Making the rows an enumeration rather than strings is what
/// makes a regression legible: a failing row names the credential shape that regressed, in the test
/// output, without a reader having to decode a magic string.
/// </para>
/// <para>
/// SIX MEMBERS, ONE PER INDEPENDENT REJECTION MECHANISM. The set is not an arbitrary sample: it walks
/// the inbound handler's validation surface one axis at a time - the value is not a token; the value
/// is not even a bearer credential; the token's lifetime has elapsed; the token names another issuer;
/// the token is addressed to somebody else; the token's signature was produced by another key - so a
/// validation step that silently stopped running is caught by exactly one row and named by it. Nothing
/// here varies two axes at once, because a row that did could go green while one of its two mechanisms
/// was inert.
/// </para>
/// </remarks>
public enum RejectedCredential
{
    /// <summary>
    /// A bearer value that is not a token at all: readable text with none of the structure a token
    /// has.
    /// </summary>
    /// <remarks>
    /// The cheapest and most common real failure - a configuration value pasted into the wrong place.
    /// It must be refused, and refused with an unauthorized status rather than a server fault, because
    /// a parser that threw on unparseable input would turn a rejected caller into an incident.
    /// </remarks>
    MalformedBearerValue = 0,

    /// <summary>
    /// A well-formed authorization header presenting a DIFFERENT authentication scheme.
    /// </summary>
    /// <remarks>
    /// This service declares one scheme. A header naming another is not a credential this service can
    /// evaluate, so it is exactly as unauthenticated as no header at all - and must be answered the
    /// same way, rather than with a fault from a handler that assumed the scheme it found was its own.
    /// </remarks>
    WrongAuthenticationScheme = 1,

    /// <summary>A genuine token from this host's own issuer whose lifetime has already elapsed.</summary>
    /// <remarks>
    /// The row that proves lifetime validation runs. It matters more here than it would elsewhere
    /// because this service's inbound handler is configured with NO clock-skew allowance, so an
    /// expired token has no grace period at all to hide in.
    /// </remarks>
    ExpiredToken = 2,

    /// <summary>
    /// A genuine, correctly signed token that names a DIFFERENT issuer.
    /// </summary>
    /// <remarks>
    /// Minted by a companion host configured with this host's signing material and another identity,
    /// so the signature verifies and the ONLY thing wrong with the token is who claims to have issued
    /// it. That isolation is the point: a row that changed the key as well would pass even if issuer
    /// validation had been switched off.
    /// </remarks>
    ForeignIssuer = 3,

    /// <summary>
    /// A genuine token this issuer is permitted to mint, addressed to a DIFFERENT service.
    /// </summary>
    /// <remarks>
    /// The replay case, and the reason the accepted inbound audience is deliberately narrower than the
    /// issuance roster: Security mints for every service in the system, so a token it minted for
    /// another service is a valid token that must nevertheless not be spendable here.
    /// </remarks>
    UnacceptedAudience = 4,

    /// <summary>
    /// A token correct in every claim, signed with an asymmetric key generated at test time.
    /// </summary>
    /// <remarks>
    /// The row that proves the signature is VERIFIED rather than the token merely parsed and believed.
    /// Every other row could in principle be refused by a claim comparison; only this one cannot.
    /// </remarks>
    DifferentSigningKey = 5,
}

/// <summary>
/// Conformance rows for the Security service's authorization posture: the authenticated ping route,
/// the anonymous readiness probe, the exact three-route anonymous exemption set, the refusal of every
/// protected route to an unauthenticated caller, the refusal of six shapes of bad credential, and the
/// single published error body that carries the legacy return code.
/// </summary>
public sealed class AuthorizationTests
{
    /// <summary>
    /// The anonymous readiness probe, spelled as the authored contract spells it.
    /// </summary>
    /// <remarks>
    /// THE ONE ROUTE WHOSE ANONYMITY IS AN ORCHESTRATION REQUIREMENT. The service container definition
    /// probes this address and never the authenticated one, and the compose manifest makes this
    /// service's health a condition of the gateway's - so a credential requirement here would make the
    /// gateway permanently unready and the local bring-up would never complete. That is why the
    /// production declaration opts out EXPLICITLY against the default-deny fallback rather than relying
    /// on omission, and why removing that opt-out has to fail this suite.
    /// </remarks>
    private const string HealthRoute = "/health";

    /// <summary>The authenticated route whose two outcomes are both part of the published contract.</summary>
    private const string PingRoute = "/v1/ping";

    /// <summary>The published verification material, anonymous so a stock bearer handler can fetch it.</summary>
    private const string JsonWebKeySetRoute = "/.well-known/jwks.json";

    /// <summary>The discovery metadata, anonymous for the same reason as the key set.</summary>
    private const string OpenIdConfigurationRoute = "/.well-known/openid-configuration";

    /// <summary>
    /// Token issuance, which is PROTECTED rather than an anonymous bootstrap.
    /// </summary>
    /// <remarks>
    /// Its route policy requires a transport credential, because a caller cannot present a bearer token
    /// in order to obtain its first one. An unauthenticated caller is therefore challenged, which is
    /// what places this address in the protected table rather than the anonymous one.
    /// </remarks>
    private const string TokenIssuanceRoute = "/v1/tokens";

    /// <summary>The unkeyed digest operation - the hash family's representative.</summary>
    private const string CryptoHashRoute = "/v1/crypto/hash";

    /// <summary>The keyed digest operation - the keyed-hash family's representative.</summary>
    private const string CryptoHmacRoute = "/v1/crypto/hmac";

    /// <summary>The symmetric encryption operation - the symmetric family's representative.</summary>
    private const string CryptoSymmetricEncryptRoute = "/v1/crypto/symmetric/encrypt";

    /// <summary>The asymmetric signing operation - the RSA family's representative.</summary>
    private const string CryptoRsaSignRoute = "/v1/crypto/rsa/sign";

    /// <summary>The identifier generator - the random family's representative.</summary>
    private const string CryptoRandomGuidRoute = "/v1/crypto/random/guid";

    /// <summary>The text-to-binary conversion - the encoding family's representative.</summary>
    private const string CryptoEncodingStringToBlobRoute = "/v1/crypto/encoding/string-to-blob";

    /// <summary>The verb every read-shaped operation in this suite is reached with.</summary>
    private const string GetMethod = "GET";

    /// <summary>
    /// The verb every operation in the protected table other than ping is reached with.
    /// </summary>
    /// <remarks>
    /// All seventeen cryptographic operations and issuance are declared with it, including the ones
    /// that look like reads, so that payloads, digests and references never reach a request line and
    /// therefore never reach an access log, a proxy cache or a browser history.
    /// </remarks>
    private const string PostMethod = "POST";

    /// <summary>The single authentication scheme this service declares.</summary>
    private const string BearerScheme = "Bearer";

    /// <summary>
    /// A scheme this service does not declare, used to prove that a foreign scheme is treated as no
    /// credential at all rather than as a credential to be parsed.
    /// </summary>
    private const string ForeignScheme = "Basic";

    /// <summary>
    /// The service identity the successful ping body reports, per the authored contract's constant.
    /// </summary>
    private const string RespondingServiceIdentity = "security";

    /// <summary>The status token the readiness probe reports when it is ready.</summary>
    private const string HealthyStatus = "Healthy";

    /// <summary>The single error media type this service produces for every non-success response.</summary>
    private const string ProblemMediaType = "application/problem+json";

    /// <summary>
    /// A bearer value that is not a token, chosen so that it cannot be mistaken for a credential.
    /// </summary>
    /// <remarks>
    /// READABLE ENGLISH ON PURPOSE (C-F). A realistic-looking token literal would be a credential-shaped
    /// string in version control, which is the exact class of artifact the secrets sweep exists to
    /// eliminate; it would also survive a copy-and-paste into somewhere that mattered. This value has
    /// none of a token's structure - no segment separators, no base64 alphabet, no armour - so a reviewer
    /// and a secret scanner reach the same conclusion about it without having to decode anything.
    /// </remarks>
    private const string NotAJsonWebToken = "not-a-json-web-token";

    /// <summary>
    /// The parameter presented with the foreign scheme, chosen for the same reason as the value above.
    /// </summary>
    /// <remarks>
    /// It is deliberately NOT valid for the scheme it accompanies, and that costs nothing: this
    /// service declares no handler for that scheme, so the parameter is never decoded by anything. A
    /// correctly encoded parameter would only add a base64 run to this file for no assertion's benefit.
    /// </remarks>
    private const string NoCredentialPresented = "no-credential-presented";

    /// <summary>
    /// The issuer identity the companion host claims for the foreign-issuer row.
    /// </summary>
    /// <remarks>
    /// Built on the reserved top-level domain that is guaranteed never to resolve, so nothing about this
    /// row can ever reach a network, and obviously synthetic so it cannot be mistaken for a deployment
    /// value that leaked into a test.
    /// </remarks>
    private const string ForeignIssuerIdentity = "https://not-this-security-service.invalid";

    /// <summary>The subject every token minted by this suite is issued for.</summary>
    /// <remarks>
    /// A caller identity and not a credential: it is stamped into the subject claim verbatim and grants
    /// nothing on its own, because authority comes from the signature rather than from the name.
    /// </remarks>
    private const string TokenSubject = "authorization-conformance";

    /// <summary>The single scope every token minted by this suite requests.</summary>
    /// <remarks>
    /// One scope, because the issuance contract requires at least one and this suite asserts nothing
    /// about scopes: no route on this service carries a scope-level policy, and inventing one to assert
    /// would be adding a requirement the contract does not declare.
    /// </remarks>
    private const string TokenScope = "authorization-conformance";

    /// <summary>
    /// The member names the successful ping body is permitted to carry, in ordinal order.
    /// </summary>
    /// <remarks>
    /// The authored schema closes this object with additional properties disallowed, so the assertion is
    /// on the EXACT set rather than on the presence of the three: a body that grew a fourth member would
    /// satisfy a presence check while violating the contract, and on this service in particular a new
    /// member on a body reachable with a token is exactly the shape a disclosure defect takes.
    /// </remarks>
    private static readonly string[] PermittedPingMembers = ["authenticated", "service", "timestamp"];

    /// <summary>
    /// The three route patterns permitted to carry an anonymous exemption, in ordinal order.
    /// </summary>
    /// <remarks>
    /// ORDINAL ORDER IS WHAT MAKES THE SET COMPARISON STABLE, and the order that falls out of it happens
    /// to put the two published metadata addresses before the readiness probe. The array is the single
    /// source of the anonymous table's rows as well as of the endpoint-inventory guard's expectation, so
    /// the two cannot drift apart and then both pass.
    /// </remarks>
    private static readonly string[] ExpectedAnonymousRoutePatterns =
    [
        JsonWebKeySetRoute,
        OpenIdConfigurationRoute,
        HealthRoute,
    ];

    /// <summary>
    /// How far beyond a token's lifetime the expiry row places issuance.
    /// </summary>
    /// <remarks>
    /// Generous rather than tight, and deliberately so: the substituted clock and the clock the inbound
    /// handler actually reads are two different clocks, and a margin measured in minutes removes any
    /// possibility of the row turning on the difference between them. A margin of milliseconds would
    /// pass on a quiet machine and fail on a loaded agent, which is the definition of a flaky test.
    /// </remarks>
    private static readonly TimeSpan ExpiryMargin = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Fragments that must never appear in an unauthorized body, independent of configuration.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Split from the configuration-derived fragments because these are absolute: an armour marker or a
    /// stack frame in an error body is a defect whatever the deployment is configured with. The two
    /// stack-shaped fragments catch the specific failure this group exists to rule out - a server fault
    /// dressed up as a rejection - which a status assertion alone would miss if the status happened to be
    /// rewritten on the way out.
    /// </para>
    /// <para>
    /// EVERY ENTRY IS A DETECTION MARKER RATHER THAN MATERIAL (C-F). The first four are the words that
    /// begin an armoured cryptographic block, and they are here so that an armoured block appearing in a
    /// response FAILS this suite; none of them is, or is part of, a credential, and the list carries no
    /// key, no token and no encoded blob. The last entry is the NAME of the signing secret's
    /// configuration key - consumed from the options contract rather than spelled out - so a diagnostic
    /// that named the key on its way to a caller would be caught as well.
    /// </para>
    /// </remarks>
    private static readonly string[] ForbiddenBodyFragments =
    [
        "BEGIN",
        "PRIVATE KEY",
        "PUBLIC KEY",
        "CERTIFICATE",
        "Exception",
        "StackTrace",
        "   at ",
        "PowerFramework",
        SecurityOptions.SigningKeyEnvironmentVariableName,
    ];

    /// <summary>
    /// Every protected address, paired with the verb it is declared for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// EIGHT ENTRIES, CHOSEN SO THAT NO FAMILY CAN HIDE. Ping and issuance are both here because both
    /// are named in the contract as protected. The remaining six are one representative of each SHAPE
    /// FAMILY of the cryptographic surface - unkeyed digest, keyed digest, symmetric, asymmetric, random
    /// and encoding - which matters because those seventeen operations are declared in family groups: an
    /// operation that lost its authorization requirement would take its whole family with it, and a
    /// table that sampled only one family would not notice.
    /// </para>
    /// <para>
    /// HELD AS A TUPLE ARRAY RATHER THAN ONLY AS THEORY DATA so that the structural guard can walk the
    /// same entries the theory runs. Projecting both from one declaration is what makes it impossible for
    /// the request-level table and the routing-level guard to describe different sets and both pass.
    /// </para>
    /// </remarks>
    private static readonly (string Method, string Route)[] ProtectedRouteDeclarations =
    [
        (GetMethod, PingRoute),
        (PostMethod, TokenIssuanceRoute),
        (PostMethod, CryptoHashRoute),
        (PostMethod, CryptoHmacRoute),
        (PostMethod, CryptoSymmetricEncryptRoute),
        (PostMethod, CryptoRsaSignRoute),
        (PostMethod, CryptoRandomGuidRoute),
        (PostMethod, CryptoEncodingStringToBlobRoute),
    ];

    /// <summary>
    /// Every protected address, projected onto theory rows.
    /// </summary>
    /// <returns>One row per protected address.</returns>
    /// <remarks>
    /// NO ROW ASSERTS SUCCESS, AND NO ROW SENDS A REQUEST BODY. Success on these addresses belongs to the
    /// issuance and cryptographic parity suites. Sending no body is not laziness: authorization runs
    /// BEFORE model binding - measured, not assumed - so a body would change nothing about the outcome
    /// while coupling this suite to seventeen request schemas it has no business knowing. Every row
    /// consequently also proves the ordering, because a service that bound the body first would answer a
    /// bad-request status instead of an unauthorized one.
    /// </remarks>
    public static TheoryData<string, string> ProtectedRoutes()
    {
        TheoryData<string, string> rows = [];

        foreach ((string method, string route) in ProtectedRouteDeclarations)
        {
            rows.Add(method, route);
        }

        return rows;
    }

    /// <summary>
    /// Every address that must answer without a credential. Exactly three, and exactly these three.
    /// </summary>
    /// <returns>One row per anonymous address.</returns>
    /// <remarks>
    /// Projected from the same array the endpoint-inventory guard compares against, so the table and the
    /// guard cannot disagree. If a fourth exemption is ever added, the guard fails first and names the
    /// pattern; this table then needs a considered decision rather than an extra line.
    /// </remarks>
    public static TheoryData<string> AnonymousRoutes() => [.. ExpectedAnonymousRoutePatterns];

    /// <summary>
    /// Every shape of credential this service must refuse.
    /// </summary>
    /// <returns>One row per rejection mechanism.</returns>
    /// <remarks>
    /// Enumerated from the enumeration itself rather than listed, so a member added there cannot be
    /// forgotten here: a new mechanism arrives in the table automatically and fails until the builder
    /// below is taught how to produce it, which is the failure mode worth having.
    /// </remarks>
    public static TheoryData<RejectedCredential> RejectedCredentials() =>
        [.. Enum.GetValues<RejectedCredential>()];

    // ==============================================================================================
    //  GROUP 1 - THE TWO NAMED ASSERTIONS
    //  The 401 without a credential and the 200 with one. Both halves of contract C-10, and the pair
    //  the whole file exists to establish.
    // ==============================================================================================

    /// <summary>
    /// THE C-G PROOF: the authenticated route answers exactly 401 to a caller presenting nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE STATUS IS ASSERTED FOR EQUALITY, NOT FOR FAILURE. 403 would mean the caller was
    /// authenticated and then denied, which is a different and weaker statement; 404 would mean the
    /// route is not mapped, so the absence of a credential was never the reason; 500 would mean the
    /// authentication path faulted, which is a defect wearing a rejection's clothes. Only 401 says the
    /// boundary asked for a credential and got none, so only 401 is accepted here.
    /// </para>
    /// <para>
    /// THE CHALLENGE HEADER IS PART OF THE PROOF RATHER THAN DECORATION. An unauthorized response is
    /// required to say which scheme it wants, and it is that header - not the status - that
    /// distinguishes "authenticate and try again" from "you are authenticated and still not allowed".
    /// Asserting it also pins the scheme to the one this service declares, so a second scheme appearing
    /// on this boundary would be caught here.
    /// </para>
    /// <para>
    /// The client is asserted to be carrying no authorization header before the request is made. That
    /// looks redundant and is not: the factory hands out authenticated clients from the same base class
    /// method, so a future edit that reached for the wrong helper would otherwise turn this row green
    /// for the exact reason it exists to rule out.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ThePingRouteRefusesACallerPresentingNoCredentialWithExactlyUnauthorized()
    {
        using SecurityAppFactory factory = new();
        using HttpClient anonymous = factory.CreateClient();

        Assert.Null(anonymous.DefaultRequestHeaders.Authorization);

        using HttpResponseMessage refused = await anonymous.GetAsync(
            new Uri(PingRoute, UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);

        AuthenticationHeaderValue challenge = Assert.Single(refused.Headers.WwwAuthenticate);

        Assert.Equal(BearerScheme, challenge.Scheme, StringComparer.Ordinal);

        await AssertUnauthorizedProblemBodyAsync(refused);
    }

    /// <summary>
    /// The authenticated route admits a caller presenting a token THIS host minted, and answers the
    /// body the authored contract declares.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE TOKEN COMES FROM THE SERVICE'S OWN ISSUER, WHICH IS THE ONLY LEGITIMATE WAY IN (C-G). The
    /// factory resolves the real minter out of the booted host, so the issuer identity, the audience,
    /// the key identifier, the algorithm and every instant in the token are by construction the ones
    /// this host's configuration produced - and the key that verifies it is the key that signed it,
    /// held by one process for the lifetime of one test. A hand-assembled token would assert this
    /// test's beliefs about the configuration rather than the configuration itself, and would keep
    /// passing after the configuration changed underneath it.
    /// </para>
    /// <para>
    /// THE BODY IS ASSERTED ON ITS EXACT MEMBER SET, not on the presence of the members it must have.
    /// The authored schema closes the object, so a fourth member is a contract violation; and on the
    /// one service that holds the system's signing secret, a new member on a body reachable with a
    /// token is the precise shape a disclosure defect takes. The two constant-valued members are
    /// checked against their declared constants rather than merely for presence, because the whole
    /// point of the second one is to give an authenticated-path assertion something explicit to assert
    /// instead of inferring success from a status code.
    /// </para>
    /// <para>
    /// THE TIMESTAMP IS ASSERTED AGAINST THE SUBSTITUTED CLOCK, which covers the ping handler's clock
    /// resolution rather than merely its happy path: the handler prefers the clock the container
    /// resolves, so an equal instant proves it read the seam, and an unequal one would mean it had
    /// silently fallen back to the ambient clock and the characterization recordings that mask this
    /// field would be masking the wrong thing.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ThePingRouteAdmitsACallerPresentingATokenThisHostMinted()
    {
        using SecurityAppFactory factory = new();
        using HttpClient caller = factory.CreateAuthenticatedClient();

        Assert.NotNull(caller.DefaultRequestHeaders.Authorization);
        Assert.Equal(
            BearerScheme,
            caller.DefaultRequestHeaders.Authorization.Scheme,
            StringComparer.Ordinal);

        using HttpResponseMessage admitted = await caller.GetAsync(
            new Uri(PingRoute, UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, admitted.StatusCode);

        using JsonDocument body = JsonDocument.Parse(
            await admitted.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        string[] members =
        [
            .. body.RootElement.EnumerateObject()
                .Select(static member => member.Name)
                .Order(StringComparer.Ordinal),
        ];

        Assert.Equal(PermittedPingMembers, members);

        Assert.Equal(
            RespondingServiceIdentity,
            body.RootElement.GetProperty("service").GetString(),
            StringComparer.Ordinal);
        Assert.True(body.RootElement.GetProperty("authenticated").GetBoolean());
        Assert.Equal(
            factory.Clock.GetUtcNow(),
            body.RootElement.GetProperty("timestamp").GetDateTimeOffset());
    }

    /// <summary>
    /// THE ORCHESTRATION ASSERTION: the readiness probe answers without a credential, and the identical
    /// client is refused on the authenticated route.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHY THIS ROW PROTECTS THE BRING-UP RATHER THAN A PREFERENCE. The service container definition's
    /// health check probes the readiness address and NEVER the authenticated one, and the compose
    /// manifest expresses this service's health as a condition of the gateway's - the gateway reports
    /// healthy only once Persistence, DataServices and Security all do. A readiness probe that demanded
    /// a credential would therefore never succeed, this service would never become healthy, the
    /// gateway's condition would never be satisfied, and the documented local bring-up would hang
    /// rather than fail with a diagnosis. That dependency chain is also the specific property that
    /// ruled out the generated-orchestration alternative, so it is the one orchestration guarantee this
    /// plan treats as least negotiable.
    /// </para>
    /// <para>
    /// THE REFUTATION IS WHAT GIVES THE ROW ITS FORCE. On its own, a 200 from an anonymous probe proves
    /// nothing about the posture: it would look identical if the default-deny fallback policy had
    /// silently stopped applying, in which case the whole service would be open and this row would be
    /// the thing reporting success. The second half sends the SAME client, carrying the same nothing, to
    /// the authenticated route and requires a refusal. Together they establish the only conclusion worth
    /// drawing - the probe is open BECAUSE its declaration opts out explicitly, and not because nothing
    /// is closed.
    /// </para>
    /// <para>
    /// The body is asserted only on the two members the readiness contract fixes. Nothing richer is
    /// expected, because the probe is deliberately cheap: it resolves no signing material, no
    /// cryptographic provider and no reference store, so that the gate it feeds cannot be made to fail
    /// by the very subsystem whose readiness it is reporting.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TheReadinessProbeIsAnonymousWhileTheAuthenticatedRouteRefusesTheSameClient()
    {
        using SecurityAppFactory factory = new();
        using HttpClient anonymous = factory.CreateClient();

        Assert.Null(anonymous.DefaultRequestHeaders.Authorization);

        using HttpResponseMessage probe = await anonymous.GetAsync(
            new Uri(HealthRoute, UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, probe.StatusCode);

        using JsonDocument report = JsonDocument.Parse(
            await probe.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        Assert.Equal(
            HealthyStatus,
            report.RootElement.GetProperty("status").GetString(),
            StringComparer.Ordinal);
        Assert.Equal(
            RespondingServiceIdentity,
            report.RootElement.GetProperty("service").GetString(),
            StringComparer.Ordinal);

        using HttpResponseMessage refused = await anonymous.GetAsync(
            new Uri(PingRoute, UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
    }

    /// <summary>
    /// The readiness probe also answers an AUTHENTICATED caller: an anonymous route must not refuse a
    /// credential it did not ask for.
    /// </summary>
    /// <remarks>
    /// The failure this rules out is a real one and not a hypothetical: an exemption expressed as a
    /// policy that requires the ABSENCE of a principal - rather than as an opt-out from requiring one -
    /// passes every anonymous assertion and then rejects the operator, the gateway and any diagnostic
    /// caller that happens to be holding a token. Nothing in the readiness contract says the probe is
    /// for unauthenticated callers only; it says the probe does not REQUIRE a credential, which is a
    /// strictly weaker statement and is what this row pins.
    /// </remarks>
    [Fact]
    public async Task TheReadinessProbeAlsoAdmitsAnAuthenticatedCaller()
    {
        using SecurityAppFactory factory = new();
        using HttpClient caller = factory.CreateAuthenticatedClient();

        using HttpResponseMessage probe = await caller.GetAsync(
            new Uri(HealthRoute, UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, probe.StatusCode);

        using JsonDocument report = JsonDocument.Parse(
            await probe.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        Assert.Equal(
            HealthyStatus,
            report.RootElement.GetProperty("status").GetString(),
            StringComparer.Ordinal);
    }

    // ==============================================================================================
    //  GROUP 2 - THE DEFAULT-DENY POSTURE AS A WHOLE
    //  Two tables that between them state the complete partition of this service's surface, and one
    //  structural guard that proves the partition is the one the host actually composed.
    // ==============================================================================================

    /// <summary>
    /// Every protected address refuses a caller presenting no credential.
    /// </summary>
    /// <param name="method">The verb the address is declared for.</param>
    /// <param name="route">The address.</param>
    /// <remarks>
    /// <para>
    /// ONE HOST PER ROW, WHICH IS THE COST OF A ROW NAMING ITS OWN FAILURE. A shared host would be
    /// faster, and it would also mean that one row's failure left the remaining rows reporting a
    /// consequence rather than a cause. Eight in-memory hosts is a cost worth paying for a table where
    /// a red row names exactly which address regressed.
    /// </para>
    /// <para>
    /// NO REQUEST BODY IS SENT, and that is a second assertion hiding inside the first. Authorization
    /// runs before model binding, so a protected address answers 401 to a request carrying nothing at
    /// all; if the pipeline were ever reordered so that binding came first, these rows would start
    /// reporting a bad-request status and the table would catch it. It also keeps this suite free of any
    /// knowledge of the seventeen cryptographic request schemas, which belong to the parity suites.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(ProtectedRoutes))]
    public async Task AProtectedRouteRefusesACallerPresentingNoCredential(string method, string route)
    {
        using SecurityAppFactory factory = new();
        using HttpClient anonymous = factory.CreateClient();

        Assert.Null(anonymous.DefaultRequestHeaders.Authorization);

        using HttpRequestMessage request = new(HttpMethod.Parse(method), route);
        using HttpResponseMessage refused = await anonymous.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);

        await AssertUnauthorizedProblemBodyAsync(refused);
    }

    /// <summary>
    /// Every anonymous address answers a caller presenting no credential.
    /// </summary>
    /// <param name="route">The address.</param>
    /// <remarks>
    /// <para>
    /// THREE ROWS, AND EXACTLY THREE. The count is not a sample of a larger set - it IS the set, which
    /// is what makes this table a guard. The companion structural assertion proves that claim against
    /// the host's own routing rather than leaving it to review, so if a fourth exemption ever appears
    /// this pair fails and names the pattern instead of quietly widening.
    /// </para>
    /// <para>
    /// Success is asserted rather than a specific status, because two of the three are metadata
    /// publications whose successful status is theirs to choose; what this row fixes is that a credential
    /// was not required, not which flavour of success was returned.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(AnonymousRoutes))]
    public async Task AnAnonymousRouteAnswersACallerPresentingNoCredential(string route)
    {
        using SecurityAppFactory factory = new();
        using HttpClient anonymous = factory.CreateClient();

        Assert.Null(anonymous.DefaultRequestHeaders.Authorization);

        using HttpResponseMessage answered = await anonymous.GetAsync(
            new Uri(route, UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.True(
            answered.IsSuccessStatusCode,
            $"The anonymous route '{route}' must answer a caller presenting no credential, because it "
            + "opts out of the default-deny fallback policy explicitly. A refusal here means the "
            + "exemption was lost - which, for the readiness probe, deadlocks the documented local "
            + "bring-up rather than merely failing a test.");

        Assert.NotEqual(HttpStatusCode.Unauthorized, answered.StatusCode);
    }

    /// <summary>
    /// THE STRUCTURAL GUARD: exactly three of this host's endpoints carry an anonymous exemption, and
    /// they are exactly the three the authored contract exempts.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHY THIS IS AN ASSERTION RATHER THAN A COMMENT. A request-level table can only prove that the
    /// addresses it happens to name behave correctly; it cannot prove that no OTHER address is open,
    /// because it does not know what else exists. This row reads the exemption set out of the running
    /// host's own routing and compares it to the contract, so a fourth exemption fails the build the
    /// moment it is introduced, whatever route it was attached to and whoever attached it. That is the
    /// difference between documenting an invariant and enforcing one.
    /// </para>
    /// <para>
    /// WHAT THE COMPARISON DELIBERATELY DOES NOT REQUIRE. It does not require the other endpoints to
    /// carry explicit authorization metadata, because they need not: the default-deny fallback policy
    /// governs anything that declared none, and the generated contract document is exactly such an
    /// endpoint - it carries neither an exemption nor a policy of its own and is protected anyway. An
    /// assertion that demanded explicit metadata everywhere would fail on a correctly protected route
    /// and would push a future author towards adding metadata for a test's benefit.
    /// </para>
    /// <para>
    /// A client is created purely to force the host to start. Routing is composed during startup, so
    /// resolving the endpoint source before the host has started would observe an empty set and this
    /// row would pass by knowing nothing.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheAnonymousExemptionSetIsExactlyTheThreeContractRoutes()
    {
        using SecurityAppFactory factory = new();
        using HttpClient starter = factory.CreateClient();

        Assert.NotNull(starter.BaseAddress);

        string[] exempt =
        [
            .. factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
                .Where(static endpoint => endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null)
                .Select(static endpoint => endpoint is RouteEndpoint route
                    ? route.RoutePattern.RawText ?? string.Empty
                    : string.Empty)
                .Order(StringComparer.Ordinal),
        ];

        Assert.Equal(ExpectedAnonymousRoutePatterns, exempt);
        Assert.Equal(ExpectedAnonymousRoutePatterns.Length, exempt.Length);
    }

    /// <summary>
    /// Both tables address routes this host ACTUALLY publishes, at the verbs they are declared for, and
    /// none of the protected ones is exempt.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE FAILURE THIS RULES OUT IS THE QUIETEST ONE AVAILABLE. A table row naming an address the host
    /// does not map would receive a not-found status, and a not-found status is not a success - so the
    /// protected table would keep passing while asserting nothing whatsoever about authorization. This
    /// row removes that possibility by requiring every table entry to be present in the host's own
    /// endpoint inventory, verb included.
    /// </para>
    /// <para>
    /// THREE OF THE ADDRESSES ARE CONFIGURABLE, WHICH IS WHY THEY ARE CHECKED AGAINST CONFIGURATION AS
    /// WELL. The two published metadata addresses and the issuance address are read from settings by the
    /// declarations that map them, so a deployment could move them; the tables above spell the authored
    /// contract's values. Comparing the two makes a drift between contract and configuration a build
    /// failure here rather than a mystery in a consumer, and it is cheap because the settings the host
    /// loaded are already available.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheTablesAddressTheRoutesThisHostActuallyPublishes()
    {
        using SecurityAppFactory factory = new();
        using HttpClient starter = factory.CreateClient();

        Assert.NotNull(starter.BaseAddress);

        SecurityOptions configured = factory.ResolveSecurityOptions();

        Assert.Equal(JsonWebKeySetRoute, configured.JwksPath, StringComparer.Ordinal);
        Assert.Equal(
            OpenIdConfigurationRoute,
            configured.OpenIdConfigurationPath,
            StringComparer.Ordinal);
        Assert.Equal(TokenIssuanceRoute, configured.TokenEndpointPath, StringComparer.Ordinal);

        IReadOnlyList<Endpoint> endpoints =
            factory.Services.GetRequiredService<EndpointDataSource>().Endpoints;

        HashSet<string> published = new(
            endpoints
                .OfType<RouteEndpoint>()
                .SelectMany(static route =>
                    (route.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [])
                        .Select(method => $"{method} {route.RoutePattern.RawText}")),
            StringComparer.Ordinal);

        foreach ((string method, string route) in ProtectedRouteDeclarations)
        {
            Assert.Contains($"{method} {route}", published);

            // Exactly one declaration per protected pattern, asserted rather than assumed: a second
            // declaration of the same address would make "this route is protected" depend on which of
            // the two the router matched, and the anonymity check below would then be inspecting an
            // arbitrary one of them.
            Endpoint declaration = Assert.Single(
                endpoints,
                candidate =>
                    candidate is RouteEndpoint mapped
                    && string.Equals(mapped.RoutePattern.RawText, route, StringComparison.Ordinal));

            Assert.Null(declaration.Metadata.GetMetadata<IAllowAnonymous>());
        }

        foreach (string route in ExpectedAnonymousRoutePatterns)
        {
            Assert.Contains($"{GetMethod} {route}", published);
        }
    }

    // ==============================================================================================
    //  GROUP 3 - THE SIX SHAPES OF BAD CREDENTIAL
    //  Every one must be refused with an unauthorized status and never with a server fault, because a
    //  fault in the authentication path is a defect that looks exactly like a successful rejection.
    // ==============================================================================================

    /// <summary>
    /// Every shape of bad credential is refused with an unauthorized status, and never with a server
    /// fault.
    /// </summary>
    /// <param name="credential">The credential shape under test.</param>
    /// <remarks>
    /// <para>
    /// THE COMPANION HOST'S LIFETIME IS DELIBERATE. Two rows need a token minted by a DIFFERENT
    /// configuration, so a second in-memory host is created for them - and it is created here, in the
    /// test's own scope, so that it is disposed AFTER the assertion rather than before. Minting from a
    /// host and then tearing it down before presenting its token would introduce an ordering dependency
    /// on the signing library's internal caching for no benefit at all.
    /// </para>
    /// <para>
    /// THE WIRING IS ASSERTED IN BOTH DIRECTIONS. A companion must exist for exactly the two rows that
    /// need one, and must NOT exist for the other four: a row that quietly booted a host it never used
    /// would be paying for an unused host, and a row that failed to boot one it did need would fail with
    /// a fixture error rather than a verdict. One equality covers both mistakes.
    /// </para>
    /// <para>
    /// THE REDUNDANT NON-FAULT ASSERTION IS KEPT ON PURPOSE. Equality with the unauthorized status
    /// already excludes a server fault, so the second assertion adds nothing today. It stays because the
    /// tempting future edit here is to loosen the status check to "not a success" once a row is added
    /// whose refusal has a different status - and that loosening would silently start tolerating a
    /// faulting authentication path, which is the one outcome this group exists to rule out. The
    /// assertion makes that loosening fail rather than pass quietly.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(RejectedCredentials))]
    public async Task ABadCredentialIsRefusedAsUnauthorizedAndNeverAsAServerFault(
        RejectedCredential credential)
    {
        using SecurityAppFactory primary = new();
        using HttpClient caller = primary.CreateClient();
        using SecurityAppFactory? companion = CreateCompanionHost(credential, primary);

        Assert.Equal(RequiresCompanionHost(credential), companion is not null);

        if (companion is not null && credential is RejectedCredential.DifferentSigningKey)
        {
            // Compared behind a BOOLEAN LOCAL so that a failure cannot print either operand (C-F): the
            // conventional equality assertion reports both of its arguments, and both of these are
            // asymmetric private keys. Both are freshly generated, so this can only fail if the
            // companion were somehow handed the primary host's material - in which case the row would
            // prove nothing at all, because a correctly signed token would be accepted and the refusal
            // would have had to come from somewhere else entirely.
            bool sharesSigningMaterial = string.Equals(
                primary.SigningKeyMaterial,
                companion.SigningKeyMaterial,
                StringComparison.Ordinal);

            Assert.False(
                sharesSigningMaterial,
                "The companion host must run on signing material generated separately from the primary "
                + "host's, or this row cannot prove that the signature is verified rather than merely "
                + "parsed. Neither value is echoed by this message.");
        }

        caller.DefaultRequestHeaders.Authorization =
            BuildBadCredential(credential, primary, companion);

        using HttpResponseMessage refused = await caller.GetAsync(
            new Uri(PingRoute, UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        Assert.NotEqual(HttpStatusCode.InternalServerError, refused.StatusCode);

        await AssertUnauthorizedProblemBodyAsync(refused);
    }

    // ==============================================================================================
    //  GROUP 4 - THE ONE PUBLISHED ERROR BODY
    //  The unauthorized response is not a bare status: it is the single problem shape this service
    //  emits for every failure, carrying the legacy return code. These two rows pin its content and
    //  its silence.
    // ==============================================================================================

    /// <summary>
    /// The unauthorized body is the single published problem shape and carries the legacy access-denied
    /// return code.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE CODE IS NAMED, NEVER NUMBERED. It is read from PowerFramework.Shared.Kernel by name, because
    /// the value crosses the wire, reaches log records and is stored in characterization recordings - so
    /// a numeric literal here would be a second, silently divergent copy of a value transcribed from
    /// ws_objects/pfw.shared.pbl.src/retcode.sru:L39-L79.
    /// </para>
    /// <para>
    /// THE CLASSIFICATION IS READ THROUGH THE PREDICATES, NOT RE-DERIVED. The legacy algebra is
    /// tri-state and has a documented hole - a prevention satisfies the success predicate, and a
    /// cancellation is excluded from failure by an explicit guard while also failing the success test, so
    /// it is neither - and re-deriving a comparison here is precisely how that hole gets closed by
    /// accident. Three predicate calls therefore establish that the unauthorized code is a genuine
    /// failure and is NOT in the hole: it fails, it does not succeed, and it is not the cancellation
    /// value. The algebra itself is asserted in the shared kernel's own suite and is not restated here.
    /// </para>
    /// <para>
    /// THE ONE-WAY ASYMMETRY IS ASSERTED BECAUSE IT LOOKS LIKE A BUG AND IS NOT. The same code the
    /// challenge path stamps onto a 401 is projected by the service's own return-code-to-status map onto
    /// a FORBIDDEN status, because that map answers the opposite question: given a legacy code, what
    /// status best describes it. The classification here runs the other way - given a status, which
    /// legacy code describes it - and the two directions are not required to compose. Pinning the
    /// asymmetry stops a future reader from "fixing" one side into agreement with the other and changing
    /// the status of every unauthorized response in the process.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TheUnauthorizedBodyIsTheSingleProblemShapeCarryingTheLegacyReturnCode()
    {
        using SecurityAppFactory factory = new();
        using HttpClient anonymous = factory.CreateClient();

        using HttpResponseMessage refused = await anonymous.GetAsync(
            new Uri(PingRoute, UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        Assert.Equal(ProblemMediaType, refused.Content.Headers.ContentType?.MediaType);

        using JsonDocument problem = JsonDocument.Parse(
            await refused.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        Assert.Equal(
            (int)HttpStatusCode.Unauthorized,
            problem.RootElement.GetProperty("status").GetInt32());

        // Both RFC members are asserted through a boolean local rather than by comparing their text,
        // because their exact wording is the framework's to choose: what the contract fixes is that a
        // consumer receives a stable problem TYPE and a human-readable TITLE, not which sentence.
        bool carriesTitle =
            !string.IsNullOrWhiteSpace(problem.RootElement.GetProperty("title").GetString());
        bool carriesType =
            !string.IsNullOrWhiteSpace(problem.RootElement.GetProperty("type").GetString());

        Assert.True(carriesTitle, "The published problem shape requires a human-readable title.");
        Assert.True(carriesType, "The published problem shape requires a problem-type reference.");

        bool carriesReturnCode = problem.RootElement.TryGetProperty(
            ProblemResults.RetCodeExtensionMember,
            out JsonElement retCode);

        Assert.True(
            carriesReturnCode,
            "The unauthorized body must carry the legacy return code as an extension member of the "
            + "single published problem shape. Its absence means the customization that stamps it was "
            + "bypassed, and every consumer written against this boundary loses the one member that "
            + "ties an HTTP failure back to the behavioural oracle.");

        long classified = retCode.GetInt64();

        Assert.Equal(RetCode.E_ACCESS_DENIED, classified);

        Assert.True(Predicates.IsFailed(classified));
        Assert.False(Predicates.IsSucceeded(classified));
        Assert.False(Predicates.IsCancelled(classified));

        Assert.Equal(
            (int)HttpStatusCode.Forbidden,
            ProblemResults.MapStatusCode(RetCode.E_ACCESS_DENIED));
    }

    /// <summary>
    /// The unauthorized body discloses nothing: no configured value, no key material, no internal type
    /// name and no fault detail.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THIS SERVICE IS THE ONE THAT HOLDS THE SYSTEM'S SIGNING SECRET, so its refusal is the response
    /// most worth reading for an attacker and the one with the most to give away. Everything a caller
    /// gets from it must be information the caller already had: that it was refused, and which scheme to
    /// use next.
    /// </para>
    /// <para>
    /// THE FORBIDDEN VALUES ARE DERIVED FROM THE HOST RATHER THAN LISTED. The issuer identity, the whole
    /// audience roster, the key identifier, the algorithm name and the reference-store prefix are read
    /// out of the settings this host actually loaded, so the assertion follows a deployment instead of
    /// going stale against one. The generated signing material is included the same way - it is the one
    /// value in this process that genuinely must never appear - and the comparison is written so that a
    /// failure names the CATEGORY that leaked and never prints the value.
    /// </para>
    /// <para>
    /// THE BASE64 GUARD IS BOUND AT FORTY CHARACTERS, AND THE BOUND IS EVIDENCE-BASED. A shorter bound
    /// would be tripped legitimately: an unauthorized body carries a distributed-tracing identifier whose
    /// middle segment is thirty-two hexadecimal characters, and that identifier is a correlation value
    /// rather than a disclosure. Forty is comfortably above it and vastly below the length of any
    /// encoded key, so the guard catches encoded material while accepting the one long run the shape is
    /// supposed to contain.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TheUnauthorizedBodyDisclosesNothingSensitive()
    {
        using SecurityAppFactory factory = new();
        using HttpClient anonymous = factory.CreateClient();

        SecurityOptions configured = factory.ResolveSecurityOptions();

        using HttpResponseMessage refused = await anonymous.GetAsync(
            new Uri(PingRoute, UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);

        string body = await refused.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        foreach (string fragment in ForbiddenBodyFragments)
        {
            Assert.DoesNotContain(fragment, body, StringComparison.OrdinalIgnoreCase);
        }

        AssertBodyOmits(body, "the configured issuer identity", configured.Issuer);
        AssertBodyOmits(body, "the configured key identifier", configured.SigningKeyId);
        AssertBodyOmits(body, "the configured signature algorithm", configured.SigningAlgorithm);
        AssertBodyOmits(
            body,
            "the configured reference-store prefix",
            configured.KeyStore.ConfigurationKeyPrefix);
        AssertBodyOmits(body, "the generated signing material", factory.SigningKeyMaterial);

        foreach (string audience in configured.Audiences)
        {
            AssertBodyOmits(body, "a configured audience identity", audience);
        }

        Assert.DoesNotMatch("[A-Za-z0-9+/]{40,}={0,2}", body);
    }

    // ==============================================================================================
    //  GROUP 5 - THE PING HANDLER'S OWN TWO ARMS
    //  Covered by a direct call because neither arm is reachable through the host: the host always
    //  resolves a clock, so the fallback is unreachable there, and a request can never present a null
    //  context at all.
    // ==============================================================================================

    /// <summary>
    /// The ping handler falls back to the ambient clock when no clock can be resolved, and refuses a
    /// null context outright.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHY A DIRECT CALL RATHER THAN A REQUEST. Through the host both of these are unreachable by
    /// construction - the composition root always registers a clock, and the framework never invokes a
    /// handler with no context - so the two arms would stay uncovered no matter how many requests this
    /// suite made. They are nonetheless real code on the response path of the route this file owns, and
    /// the fallback in particular is the arm that keeps the handler total rather than dependent on a
    /// registration it does not make itself.
    /// </para>
    /// <para>
    /// THE TIMESTAMP IS ASSERTED ONLY TO BE POPULATED. On this arm it comes from the ambient clock, so
    /// pinning it to an instant would be pinning wall-clock time and would be the one flaky assertion in
    /// this file. Its equality with the substituted clock is asserted on the arm where that is
    /// meaningful, which is the request-level row above.
    /// </para>
    /// <para>
    /// The guard is asserted with a statement-bodied lambda so that the void-returning overload is
    /// selected unambiguously, which keeps the row warning-clean under warnings-as-errors.
    /// </para>
    /// </remarks>
    [Fact]
    public void ThePingHandlerFallsBackToTheAmbientClockAndRefusesANullContext()
    {
        // A bare context resolves no clock by either route available to the handler - it carries no
        // request services, and a service provider that carried none would answer the lookup with
        // nothing - so the ambient arm is taken whichever of the two the framework happens to give it.
        // The assertion below is therefore on the OUTCOME rather than on which of the two occurred,
        // which keeps the row independent of a framework internal it has no business depending on.
        DefaultHttpContext contextWithoutServices = new();

        Ok<PingResponse> answered = PingEndpoints.Ping(contextWithoutServices);

        Assert.NotNull(answered.Value);
        Assert.Equal(RespondingServiceIdentity, answered.Value.Service, StringComparer.Ordinal);
        Assert.True(answered.Value.Authenticated);
        Assert.NotEqual(default, answered.Value.Timestamp);

        Assert.Throws<ArgumentNullException>(() =>
        {
            _ = PingEndpoints.Ping(null!);
        });
    }

    // ==============================================================================================
    //  HELPERS
    //  Not one of them contains an assertion about the service's behaviour, and not one of them can
    //  weaken an assertion: they build credentials and read bodies. The two that DO assert -
    //  AssertUnauthorizedProblemBodyAsync and AssertBodyOmits - are shared assertions rather than
    //  fixtures, and are named so that a reader cannot mistake them for either.
    // ==============================================================================================

    /// <summary>
    /// Answers whether a credential shape needs a token minted by a DIFFERENT configuration.
    /// </summary>
    /// <param name="credential">The credential shape.</param>
    /// <returns><see langword="true"/> for the two rows that need a companion host.</returns>
    /// <remarks>
    /// Exactly two shapes do, and for the same structural reason: a host cannot mint a token that its
    /// own inbound validation would reject on the issuer or on the signature, because it stamps its own
    /// identity and signs with its own key. Every other shape is constructible against the host under
    /// test.
    /// </remarks>
    private static bool RequiresCompanionHost(RejectedCredential credential) =>
        credential is RejectedCredential.ForeignIssuer or RejectedCredential.DifferentSigningKey;

    /// <summary>
    /// Creates the second in-memory host the foreign-issuer and different-key rows mint from, or
    /// <see langword="null"/> when the row needs none.
    /// </summary>
    /// <param name="credential">The credential shape.</param>
    /// <param name="primary">The host under test.</param>
    /// <returns>The companion host, or <see langword="null"/>.</returns>
    /// <remarks>
    /// <para>
    /// EACH COMPANION VARIES EXACTLY ONE THING, WHICH IS WHAT MAKES ITS ROW DIAGNOSTIC. The
    /// foreign-issuer companion runs on the PRIMARY host's signing material and changes only the issuer
    /// identity, so its token's signature verifies and the issuer claim is the single reason it can be
    /// refused. The different-key companion changes only the key, so every claim matches and the
    /// signature is the single reason. A companion that varied both would be refused either way and would
    /// prove neither.
    /// </para>
    /// <para>
    /// THE SECOND COMPANION'S KEY IS GENERATED AT TEST TIME, EXPLICITLY (C-F). The factory's constructor
    /// would have generated one anyway, but requesting it here states the property the row depends on
    /// instead of inheriting it silently, and it keeps the requirement visible at the point a future edit
    /// would be tempted to pass a literal.
    /// </para>
    /// </remarks>
    private static SecurityAppFactory? CreateCompanionHost(
        RejectedCredential credential,
        SecurityAppFactory primary)
    {
        switch (credential)
        {
            case RejectedCredential.ForeignIssuer:
                return new SecurityAppFactory
                {
                    SigningKeyMaterial = primary.SigningKeyMaterial,
                    Issuer = ForeignIssuerIdentity,
                };

            case RejectedCredential.DifferentSigningKey:
                // GENERATED BEFORE THE HOST IS CONSTRUCTED, AND THAT ORDER IS THE REASON THIS IS A
                // STATEMENT BODY RATHER THAN AN EXPRESSION ONE. Generating inside the object initializer
                // would leave a constructed-but-never-returned host to be collected rather than disposed
                // on the one path where generation can fail - the platform refusing the requested modulus
                // size. Hoisting it out means either both the material and the host exist, or neither
                // does, and the caller's using declaration owns whatever came back.
                string freshMaterial = SecurityAppFactory.CreateSigningKeyMaterial(
                    SecurityAppFactory.DefaultSigningKeySizeInBits);

                return new SecurityAppFactory { SigningKeyMaterial = freshMaterial };

            default:
                return null;
        }
    }

    /// <summary>
    /// Builds the authorization header for one credential shape.
    /// </summary>
    /// <param name="credential">The credential shape.</param>
    /// <param name="primary">The host under test.</param>
    /// <param name="companion">The companion host, where the shape needs one.</param>
    /// <returns>The header to present.</returns>
    /// <exception cref="InvalidOperationException">
    /// The shape needs a companion host and none was supplied, or the shape is not one this builder
    /// knows how to produce.
    /// </exception>
    /// <remarks>
    /// THE FINAL ARM THROWS RATHER THAN FALLING BACK TO SOMETHING PLAUSIBLE. A member added to the
    /// enumeration reaches the table automatically, and if this builder answered such a row with, say, a
    /// malformed value, the new row would pass while testing the wrong mechanism entirely. Throwing makes
    /// the omission a loud failure that names the member.
    /// </remarks>
    private static AuthenticationHeaderValue BuildBadCredential(
        RejectedCredential credential,
        SecurityAppFactory primary,
        SecurityAppFactory? companion) =>
        credential switch
        {
            RejectedCredential.MalformedBearerValue =>
                new AuthenticationHeaderValue(BearerScheme, NotAJsonWebToken),

            RejectedCredential.WrongAuthenticationScheme =>
                new AuthenticationHeaderValue(ForeignScheme, NoCredentialPresented),

            RejectedCredential.ExpiredToken =>
                new AuthenticationHeaderValue(BearerScheme, MintAnExpiredToken(primary)),

            RejectedCredential.UnacceptedAudience =>
                new AuthenticationHeaderValue(
                    BearerScheme,
                    primary
                        .IssueToken(TokenSubject, ResolveUnacceptedAudience(primary), [TokenScope])
                        .AccessToken),

            RejectedCredential.ForeignIssuer or RejectedCredential.DifferentSigningKey =>
                new AuthenticationHeaderValue(
                    BearerScheme,
                    RequireCompanionHost(companion, credential)
                        .IssueToken(TokenSubject, primary.ResolveInboundAudience(), [TokenScope])
                        .AccessToken),

            _ => throw new InvalidOperationException(
                $"'{credential}' is a credential shape this builder has not been taught to produce. A "
                + $"member added to {nameof(RejectedCredential)} reaches the theory table automatically, "
                + "so it must be given a construction here rather than allowed to fall through to a "
                + "plausible-looking substitute that would test a different mechanism."),
        };

    /// <summary>
    /// Returns the companion host, or explains that the wiring predicates disagree.
    /// </summary>
    /// <param name="companion">The companion host, possibly absent.</param>
    /// <param name="credential">The credential shape that requested one.</param>
    /// <returns>The companion host.</returns>
    /// <exception cref="InvalidOperationException">No companion host was supplied.</exception>
    private static SecurityAppFactory RequireCompanionHost(
        SecurityAppFactory? companion,
        RejectedCredential credential) =>
        companion ?? throw new InvalidOperationException(
            $"The '{credential}' row must mint its token from a companion host, and none was supplied. "
            + $"{nameof(RequiresCompanionHost)} and {nameof(CreateCompanionHost)} disagree about which "
            + "rows need one, which is a fixture defect rather than a verdict about the service.");

    /// <summary>
    /// Mints a genuine token from this host's own issuer whose lifetime has ALREADY elapsed, and leaves
    /// the substituted clock advanced past that expiry.
    /// </summary>
    /// <param name="primary">The host under test.</param>
    /// <returns>The expired token.</returns>
    /// <remarks>
    /// <para>
    /// THREE STATEMENTS, AND THE ORDER IS THE WHOLE POINT. The clock is REWOUND by one full token
    /// lifetime plus a margin, the token is minted - so its validity window closes a margin ago - and the
    /// clock is then ADVANCED back past that expiry, which is the state the request is made in.
    /// </para>
    /// <para>
    /// WHY IT IS NOT SIMPLY MINT-THEN-ADVANCE, WHICH IS THE OBVIOUS SHAPE AND DOES NOT WORK. Measured on
    /// the pinned toolchain: the inbound bearer handler validates a token's lifetime against the AMBIENT
    /// clock, not against the substituted one, so a token minted at the present instant and then left
    /// behind by an advancing substituted clock is still ACCEPTED - while the ping response's own
    /// timestamp moves, which is how the difference was observed. Rewinding first makes the token expired
    /// on BOTH clocks, so the row asserts lifetime validation rather than asserting which clock the
    /// handler happens to read.
    /// </para>
    /// <para>
    /// NOTHING SLEEPS, WAITS OR POLLS. A five-minute lifetime cannot be waited out, a shortened lifetime
    /// would be a different configuration from the one deployments run, and a sleeping test is slow on a
    /// quiet machine and flaky on a loaded one. The seam is the only mechanism that produces an expired
    /// token deterministically and instantly.
    /// </para>
    /// </remarks>
    private static string MintAnExpiredToken(SecurityAppFactory primary)
    {
        TimeSpan lifetime = primary.ResolveSecurityOptions().TokenLifetime;
        DateTimeOffset present = primary.Clock.GetUtcNow();

        primary.Clock.SetUtcNow(present - lifetime - ExpiryMargin);

        string expired = primary
            .IssueToken(TokenSubject, primary.ResolveInboundAudience(), [TokenScope])
            .AccessToken;

        primary.Clock.SetUtcNow(present);

        return expired;
    }

    /// <summary>
    /// Resolves an audience this issuer is permitted to mint for but this host does NOT accept inbound.
    /// </summary>
    /// <param name="primary">The host under test.</param>
    /// <returns>The audience identity.</returns>
    /// <exception cref="InvalidOperationException">
    /// The issuance roster carries no audience other than the accepted one.
    /// </exception>
    /// <remarks>
    /// DERIVED FROM THE HOST, NEVER NAMED. The roster and the accepted inbound identity are both read
    /// from the configuration this host actually loaded, so this suite hardcodes no deployment fact and
    /// keeps working when either half is overridden. The first non-matching entry is chosen because it is
    /// the deterministic choice, and the asymmetry it exploits is intended rather than accidental:
    /// Security mints for every service in the system while accepting only tokens addressed to itself, so
    /// a token minted for another service is a perfectly valid token that must not be spendable here.
    /// </remarks>
    private static string ResolveUnacceptedAudience(SecurityAppFactory primary)
    {
        string accepted = primary.ResolveInboundAudience();

        string? unaccepted = primary.ResolveSecurityOptions().Audiences
            .FirstOrDefault(candidate =>
                !string.Equals(candidate, accepted, StringComparison.Ordinal));

        return unaccepted ?? throw new InvalidOperationException(
            "This host's issuance roster carries no audience other than the one it accepts inbound, so "
            + "the replay case cannot be constructed against it. Widen the roster through the factory's "
            + "own audience override before the host starts. This message echoes no configured value.");
    }

    /// <summary>
    /// Asserts that a refusal carries the single published problem shape, the unauthorized status inside
    /// the body, the legacy access-denied return code, and no fault detail.
    /// </summary>
    /// <param name="refused">The refusal.</param>
    /// <returns>A task that completes when the body has been read and asserted.</returns>
    /// <remarks>
    /// <para>
    /// SHARED SO THAT EVERY REFUSAL IN THIS FILE IS HELD TO THE SAME STANDARD, and in particular so that
    /// the table-driven rows assert the BODY as well as the status. A row that only checked the status
    /// would accept a refusal that had lost its return code, and the return code is the one member that
    /// ties an HTTP failure on this boundary back to the behavioural oracle.
    /// </para>
    /// <para>
    /// THE STATUS IS ASSERTED INSIDE THE BODY AS WELL AS ON THE RESPONSE. The published shape repeats it
    /// deliberately, so that a logged body is self-describing; asserting it here is what stops the two
    /// from disagreeing, which is a real failure mode when a status is rewritten after the body was
    /// composed - and a body claiming success beside a refusing status is worse than either alone.
    /// </para>
    /// <para>
    /// THE FAULT-SHAPED FRAGMENTS ARE CHECKED HERE RATHER THAN ONLY IN THE DISCLOSURE ROW, because this
    /// is the assertion every refusal passes through: it is what makes "unauthorized, and never a server
    /// fault dressed up as a rejection" true of all fourteen refusals in this file rather than of one.
    /// </para>
    /// </remarks>
    private static async Task AssertUnauthorizedProblemBodyAsync(HttpResponseMessage refused)
    {
        ArgumentNullException.ThrowIfNull(refused);

        Assert.Equal(ProblemMediaType, refused.Content.Headers.ContentType?.MediaType);

        string body = await refused.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        using JsonDocument problem = JsonDocument.Parse(body);

        Assert.Equal(
            (int)HttpStatusCode.Unauthorized,
            problem.RootElement.GetProperty("status").GetInt32());

        bool carriesReturnCode = problem.RootElement.TryGetProperty(
            ProblemResults.RetCodeExtensionMember,
            out JsonElement retCode);

        Assert.True(
            carriesReturnCode,
            "Every refusal must carry the legacy return code as an extension member of the single "
            + "published problem shape. Its absence means the customization that stamps it was bypassed, "
            + "and a consumer loses the only member tying this failure back to the behavioural oracle.");

        Assert.Equal(RetCode.E_ACCESS_DENIED, retCode.GetInt64());

        foreach (string fragment in ForbiddenBodyFragments)
        {
            Assert.DoesNotContain(fragment, body, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Asserts that a body does not contain one value, WITHOUT the value appearing in the failure
    /// message.
    /// </summary>
    /// <param name="body">The body to inspect.</param>
    /// <param name="description">What the value is, for the failure message.</param>
    /// <param name="value">The value that must be absent; ignored when empty.</param>
    /// <remarks>
    /// <para>
    /// WHY THE CONTAINMENT TEST IS COMPUTED INTO A LOCAL FIRST, WHICH LOOKS LIKE AN INDIRECTION AND IS
    /// NOT. The conventional spelling of this assertion takes the value as its EXPECTED argument, and an
    /// assertion library reports its expected argument when it fails - which for the generated signing
    /// material would write key material into the test log, in CI, on the one run where it mattered
    /// most. That is precisely the outcome C-F exists to prevent, so the boolean is computed first and
    /// the message is supplied by hand: a failure then names the CATEGORY that leaked and nothing else,
    /// which is all a reader needs in order to go and look.
    /// </para>
    /// <para>
    /// AN EMPTY VALUE IS SKIPPED RATHER THAN ASSERTED, because every body contains the empty string:
    /// asserting its absence would fail every time, and asserting its presence would be meaningless. An
    /// unset optional setting therefore contributes no row, which is correct - a value that does not
    /// exist cannot leak.
    /// </para>
    /// <para>
    /// The comparison is case-insensitive deliberately: a value re-cased on its way into a message is
    /// still that value disclosed.
    /// </para>
    /// </remarks>
    private static void AssertBodyOmits(string body, string description, string? value)
    {
        ArgumentNullException.ThrowIfNull(body);

        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        bool disclosed = body.Contains(value, StringComparison.OrdinalIgnoreCase);

        Assert.False(
            disclosed,
            $"The unauthorized body must not disclose {description}. This service holds the system's "
            + "single signing secret, so its refusal is the response with the most to give away. This "
            + "message names the category that leaked and deliberately never echoes the value.");
    }
}
