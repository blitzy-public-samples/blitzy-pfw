// ==================================================================================================
//  TokenEndpoints.cs - CONTRACT C-01's ISSUANCE HALF, AND THE ONLY ROUTE TO THE SYSTEM'S SOLE MINTER
//  ------------------------------------------------------------------------------------------------
//  ROLE
//  Declares and serves `POST /v1/tokens`. Security mints; Gateway, DataServices and Persistence hold
//  verification material only and are not independent signing authorities. Exactly one signing secret
//  exists in the whole system, it is held here, and it arrives by configuration injection.
//
//  SOLE-ISSUER DISCIPLINE - WHAT THIS FILE DOES NOT DO, STATED FIRST BECAUSE IT MATTERS MOST
//  This file creates NOTHING. It maps the published request shape onto the shapes Tokens/TokenIssuer.cs
//  already declares, hands them over, and projects the result. It does not construct a claim, does not
//  choose an instant, does not read the signing secret, does not touch the signing-key layer's
//  credential, and holds no reference to any minting type from the identity-model package. Exactly one
//  component in this refactor mints, and it is the file next door.
//
//  WHY REST AND NOT gRPC, FOR THIS OPERATION SPECIFICALLY (constraint C-K)
//  The legacy surface this contract is built on is roughly sixty stateless request/response overloads
//  with no ordering requirement between calls and nothing to stream
//  [ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L11-L73], so nothing about its shape argues for a
//  streaming transport. The decisive reason is downstream rather than here: issuance and key
//  publication must speak ordinary HTTP so that each consumer's STOCK bearer handler fetches the key
//  set and the discovery document with ZERO BESPOKE CODE. gRPC would force a hand-written key-set
//  retrieval implementation into three services, which is a net INCREASE in hand-written security
//  code - the opposite of what the requirement asks for.
//
//  WHY THIS ROUTE IS AUTHENTICATED BY A CLIENT CERTIFICATE RATHER THAN BY A TOKEN (constraint C-G)
//  A caller CANNOT PRESENT A BEARER TOKEN IN ORDER TO OBTAIN ITS FIRST BEARER TOKEN. Caller identity
//  on this one operation therefore has to come from somewhere other than a token, and it comes from
//  the transport. The published document says so in a machine-readable way rather than in prose: it
//  declares a `mutualTLS` security scheme and applies it to this operation as an OVERRIDE of the
//  document-level bearer requirement
//  [shared/PowerFramework.Contracts/OpenApi/security.v1.yaml, operation `issueToken`]. Mutual TLS is
//  the documented per-pair fallback for a pair where a token issuer is inappropriate, and the
//  issuance edge is that pair - the SINGLE such edge in this system. Two consequences this file
//  implements rather than merely documents:
//
//    * NO CREDENTIAL CROSSES IN THE BODY. The request carries a claimed identity, an audience and a
//      scope set, and the published schema forbids any further member outright. There is no client
//      secret, no password, no key reference, no assertion and nowhere to put one.
//    * THE CLAIMED IDENTITY IS RECONCILED AGAINST THE TRANSPORT'S. The `subject` member is a CLAIM;
//      the identity honoured is the one the presented certificate establishes, and a disagreement is
//      refused. docs/ARCHITECTURE.md section 9.3.1 assigns that mapping to THIS FILE, and the local
//      certificate recipe in that same subsection fixes the caller identity as the certificate's
//      common name.
//
//  This route is NOT one of the service's three anonymous exemptions - the readiness probe, the
//  published key set and the discovery metadata - and `AllowAnonymous` appears nowhere below.
//
//  THIS FILE IS THE INVERSION OF A COMMITTED ANTI-PATTERN
//  tests/blink/test_jws.htm:L8-L23 is a legacy browser asset that hardcodes a private key in PEM form,
//  hand-writes a token header and signs a token with it, on a line that also carries a hardcoded
//  bearer value, a digest, a subject and an expiry long since past. It is REFERENCE as the
//  anti-pattern being replaced (constraint C-C: read only, never edited). Everything about the design
//  below is its opposite: the key is configured and never present in source, the header is stamped by
//  the minting library from the key itself rather than written by hand, and no specimen token appears
//  in this file, in a schema default or in a published example.
//
//  LEGACY PROVENANCE (REFERENCE ONLY - never edited, never built, never shipped: constraint C-C)
//  The legacy framework is a library with no process of its own, no listener and no server tier, so it
//  has no token issuer and this contract translates NO existing wire format. What it does supply is
//  the signature primitives the issuer is built on -
//  `RSASign(readonly string data, readonly string prikey, readonly long ntype)` and its binary
//  overload, with `VerifyRSASign` beside them
//  [ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L70-L73] - and the digest catalogue those take their
//  `ntype` from, whose own comment records that the set governs `Hash`, `RSASign` AND `VerifyRSASign`
//  [ws_objects/pfw.shared.pbl.src/enums.sru:L927-L933, with SHA-256 at :L930]. That is the settled
//  provenance for an RSA signature over a SHA-256 digest. In every legacy overload the key parameters
//  are TEXT rather than binary, which is why the configured signing secret is a value rather than a
//  blob. The framework application object supplies the posture rather than the algorithm: a structural
//  fault ends the process instead of degrading past it
//  [ws_objects/pfw.pbl.src/pfw.sra:L111-L144, ending in `HALT CLOSE` at :L143]. Applied here, that
//  means unusable signing material is a STARTUP failure owned by the signing-key layer and the
//  issuer's eager constructor. This handler NEVER generates an ephemeral key, NEVER downgrades to a
//  symmetric algorithm and NEVER issues an unsigned token in order to keep a request succeeding.
//
//  WHAT THIS FILE DELIBERATELY DOES NOT CONTAIN
//    * No outbound client of any kind, and no reference to Gateway, DataServices or Persistence. Those
//      three are the AUDIENCES of the tokens issued here; they are configured identity values read
//      from the options roster, never code references (constraint C-A).
//    * No refresh flow, no introspection route, no revocation list, no rate limiter and no cache. None
//      is in the published contract, and each would be an unrequested feature on a security-critical
//      path (constraint C-B).
//    * No route, member, scope value or audience value for any capability deferred out of this phase,
//      and no reserved-route status of any kind - those declarations belong to the ingress service's
//      routing table, not here (constraint C-D).
//    * No storage of any kind: no context, no connection string, no token store and no persisted grant
//      (constraint C-E).
//    * No key, certificate, credential or specimen token in any form, including in a comment, a
//      schema default or a published example (constraint C-F).
//    * No SCREAMING_SNAKE identifier is DECLARED here. The repository .editorconfig scopes its naming
//      suppressions to a fixed list of files carrying preserved legacy identifiers and this file is not
//      among them, while warnings are errors; the preserved return-code identifiers are CONSUMED from
//      PowerFramework.Shared.Kernel instead.
//    * No ambient clock read. Every instant in the response comes from the issuer's own result, which
//      derives all three time claims from ONE reading of the injected clock seam. Reading the clock
//      here would defeat that seam and make a characterization recording irreproducible.
// ==================================================================================================

using System.Diagnostics.CodeAnalysis;
using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;
using System.Net.Mime;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using PowerFramework.Security.Configuration;
using PowerFramework.Security.Tokens;
using PowerFramework.Shared.Kernel;

namespace PowerFramework.Security.Endpoints;

/// <summary>
/// Declares and serves contract C-01's token-issuance operation.
/// </summary>
/// <remarks>
/// <para>
/// This file AUTHORISES and RECONCILES; it does not authenticate and it does not mint. Registering the
/// authentication scheme, the authorization services and the transport is <c>Program.cs</c>'s
/// responsibility; minting is <see cref="TokenIssuer"/>'s and nothing else's.
/// </para>
/// <para>
/// ONE PUBLIC MEMBER, matching the one-registration-method-per-endpoint-file shape the whole folder
/// uses and that <c>Program.cs</c> calls once per file. Everything else is <c>internal</c> so the
/// sibling test project can drive each arm directly, or <c>private</c> where it is a composition
/// detail.
/// </para>
/// </remarks>
public static class TokenEndpoints
{
    /// <summary>
    /// The endpoint name, which the document generator also publishes as the operation identifier.
    /// </summary>
    /// <remarks>
    /// Spelled exactly as the authored contract's <c>operationId</c> for this path. A consumer
    /// generates its client method name from this value, so a divergence would rename a published
    /// method.
    /// </remarks>
    private const string OperationName = "issueToken";

    /// <summary>The tag the operation is grouped under.</summary>
    /// <remarks>
    /// The authored contract tags this path with the token-service tag, grouping it with the key set
    /// and the discovery metadata under one contract identifier. The sibling key-publication file uses
    /// the same tag, so the three routes land together in the generated document rather than in
    /// separate groups of one.
    /// </remarks>
    private const string TagName = "TokenService";

    /// <summary>
    /// The security-scheme key the generated document declares this operation's requirement under.
    /// </summary>
    /// <remarks>
    /// Spelled exactly as the authored contract names the scheme in its component section. The scheme
    /// KEY and the scheme TYPE are different things and both matter: the key is what an operation
    /// references, and the type is the transport mechanism the specification defines.
    /// </remarks>
    private const string MutualTlsSchemeName = "mutualTls";

    /// <summary>
    /// The security-scheme key the generated document declares the shared-secret credential under.
    /// </summary>
    /// <remarks>
    /// Spelled exactly as the authored contract names the scheme in its component section, where it is
    /// declared as HTTP <c>basic</c>. The operation declares BOTH this scheme and the mutual-TLS one as
    /// ALTERNATIVES, which is what a list of two single-scheme requirements means in the specification -
    /// either satisfies the operation - and which is exactly the behaviour the handler implements.
    /// </remarks>
    private const string ClientCredentialSchemeName = "clientCredential";

    /// <summary>
    /// The authentication-scheme token the credential is presented under, WITH ITS TRAILING SPACE.
    /// </summary>
    /// <remarks>
    /// RFC 7617's scheme name. Matched case-INSENSITIVELY, because RFC 9110 section 11.1 makes the
    /// scheme token case-insensitive - a caller presenting <c>basic</c> is conformant and refusing it
    /// would be this service inventing a stricter protocol than the one it publishes. The credential
    /// that follows is matched case-SENSITIVELY, because it is a credential.
    /// </remarks>
    private const string BasicSchemeToken = "Basic";

    /// <summary>
    /// The credential type the response reports, fixed by the published response schema.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A PROTOCOL CONSTANT, NOT A CREDENTIAL. It is the token type of RFC 6749 section 5.1 and the
    /// published schema fixes it to this one value rather than enumerating alternatives, because no
    /// other credential type is issued. It is spelled once here so the response cannot carry a second
    /// spelling.
    /// </para>
    /// <para>
    /// This is the only string literal in this file that reaches the response body, and it carries no
    /// information about any key, any configuration value or any caller.
    /// </para>
    /// </remarks>
    private const string BearerTokenType = "Bearer";

    /// <summary>The logger category every record from this file is written under.</summary>
    /// <remarks>
    /// A named category rather than a generic type parameter, for two reasons. A static class cannot be
    /// used as a type argument at all, so the generic logger interface is unavailable here; and a named
    /// category makes a record attributable to this operation rather than to whichever framework type
    /// happened to resolve the logger. The sibling readiness probe and the cryptographic surface name
    /// their categories the same way.
    /// </remarks>
    private const string LoggerCategoryName = "PowerFramework.Security.Endpoints.TokenEndpoints";

    /// <summary>The summary published for the operation, matching the authored contract.</summary>
    private const string OperationSummary = "Issue a short-lived service token.";

    /// <summary>The description published for the operation.</summary>
    /// <remarks>
    /// Authored as a constant rather than inline so the published prose is reviewable in one place
    /// against the contract document it must agree with. It states the three properties a consumer
    /// cannot infer from the schema: that identity comes from the transport, that this is not a grant
    /// request despite the response following the grant response's member spellings, and that the
    /// granted scope set may be narrower than the requested one.
    /// </remarks>
    private const string OperationDescription =
        "Issues a short-lived service token from a caller identity, an intended audience and a "
        + "requested scope set. This service is the sole minter in the system; Gateway, DataServices "
        + "and Persistence hold verification material only. CALLER IDENTITY COMES FROM THE PRESENTED "
        + "CREDENTIAL, NOT FROM THE BODY: the operation accepts a shared secret as an HTTP Basic "
        + "credential or a client certificate, and no bearer token, because a caller cannot present a "
        + "bearer token in order to obtain its first bearer token. The subject member is a CLAIM, "
        + "reconciled ordinally against the identity the presented credential establishes, so a caller "
        + "can only obtain a token whose subject is its own. WHAT A CALLER MAY ASK FOR IS DECIDED PER "
        + "CALLER: this issuer holds a roster naming, for each registered identity, the audiences it may "
        + "address and the scopes it may request, and a request outside that grant is refused rather "
        + "than narrowed. This is not "
        + "an OAuth 2.0 grant request and does not pretend to be one - there is no grant-type "
        + "parameter, no authorization endpoint and no refresh credential anywhere in this contract - "
        + "while the RESPONSE does use the RFC 6749 section 5.1 member spellings so that a stock "
        + "client library parses it without bespoke code. THE GRANTED SCOPE SET MAY BE NARROWER THAN "
        + "THE REQUESTED ONE: a narrowing is a normal successful outcome reported as a success rather "
        + "than as an error, so a caller must read the granted set from the response instead of "
        + "assuming its request was honoured in full.";

    /// <summary>The description published for the mutual-TLS security scheme.</summary>
    /// <remarks>
    /// Registered only when the generated document does not already carry the scheme, so this file
    /// composes with whatever the host declares rather than fighting it. It names the mechanism and
    /// the reason, and deliberately names no trust anchor, no certificate path and no caller identity:
    /// a description of an authentication scheme is not a place to publish the configuration that
    /// implements it.
    /// </remarks>
    private const string ClientCredentialSchemeDescription =
        "A shared secret presented as an HTTP Basic credential, whose user-id is the caller's service "
        + "identity and whose password is the secret this deployment injected for that identity. This "
        + "is one of the two credentials this operation accepts, and it is the one available to a "
        + "deployment whose TLS is terminated ahead of this service - a reverse proxy or a mesh sidecar "
        + "strips the client certificate, so certificate identity cannot be relied on universally even "
        + "though this service's own listener requests one. The identity is "
        + "resolved against this service's configured issuance roster, which also decides which "
        + "audiences and scopes that identity may request; a request presenting no credential, an "
        + "unknown identity or an incorrect secret is refused identically and before any signing work "
        + "is done, so the refusal cannot be used to discover which callers exist. Raw key material "
        + "never crosses this boundary - the secret authenticates the caller and is not used as a "
        + "cryptographic key by anything.";

    /// <summary>The description published for the mutual-TLS security scheme.</summary>
    private const string MutualTlsSchemeDescription =
        "A client certificate presented during the TLS handshake, requested and validated by this "
        + "service, and accepted only if it chains to the configured trust anchor. This is the only "
        + "operation in this document protected this way, and necessarily so: it is the only operation "
        + "a bearer token cannot protect, because a caller cannot present a token in order to obtain "
        + "its first token. A request that presents no certificate, or one whose certificate "
        + "establishes no caller identity, is refused before any signing work is done. A certificate "
        + "that is accepted but is not permitted the requested subject or audience is a different "
        + "outcome and is documented separately on the operation.";

    // ==============================================================================================
    //  REJECTION DETAIL TEXT - ONE CONSTANT PER ARM, AND NONE OF THEM CAN CARRY A CALLER VALUE
    //
    //  Every string below is a FIXED sentence. None is composed with a caller-supplied value, none is
    //  interpolated, and none names a configured value, a roster entry, an expected identity, a key,
    //  a key identifier or an algorithm. That is constraint C-F met by the SHAPE of the text rather
    //  than by the discipline of the call sites: a value that is never a parameter cannot be echoed.
    //
    //  Each names the member or the condition at fault, because a caller has to be able to fix its
    //  request without a support conversation, and that is the whole of what it names.
    //
    //  THE LEGACY DELIVERED CONDITIONS LIKE THESE THROUGH DIALOGS, AND ONLY THE DELIVERY CHANNEL
    //  CHANGES IN THIS REFACTOR - but there is NO legacy dialog behind any sentence here, because the
    //  legacy has no token issuer at all. These are net-new operational diagnostics. Consequently NO
    //  LOCALIZATION CATEGORY IS PASSED with any of them: inventing one would fabricate provenance for
    //  a message the oracle never displayed. The preserved severity is still stated per call site
    //  rather than defaulted, which is what the shared factory requires of every caller.
    // ==============================================================================================

    /// <summary>Detail for a request that arrived without a body.</summary>
    private const string RequestAbsentDetail =
        "The request body is absent. The published request schema requires an object carrying a "
        + "subject, an audience and a scope set, and no member of it is defaulted: an issuer that "
        + "supplied one would mint a token the caller never asked for.";

    /// <summary>Detail for a request whose claimed identity is absent or blank.</summary>
    private const string SubjectBlankDetail =
        "subject is required and must carry at least one character. It names the identity the caller "
        + "is claiming, and it is reconciled against the identity the presented client certificate "
        + "establishes. The supplied value is deliberately not reported.";

    /// <summary>Detail for a request whose intended audience is absent or blank.</summary>
    private const string AudienceBlankDetail =
        "audience is required and must carry at least one character. One audience is carried per "
        + "request deliberately, so that a token is never valid somewhere its holder did not intend; a "
        + "caller needing tokens for two audiences requests two tokens. The supplied value is "
        + "deliberately not reported.";

    /// <summary>Detail for a request whose scope set is absent or empty.</summary>
    private const string ScopeSetEmptyDetail =
        "scopes is required and must carry at least one entry. A token granted an empty set would "
        + "authorise nothing, so an empty request is refused rather than honoured.";

    /// <summary>Detail for a scope set carrying an empty entry.</summary>
    private const string ScopeBlankDetail =
        "A requested scope is empty. The published request schema requires every scope to carry at "
        + "least one character.";

    /// <summary>Detail for a scope carrying white space.</summary>
    /// <remarks>
    /// This constraint is not written in the schema in those words - it is what the schema's own
    /// encoding requires. The granted set travels as ONE space-delimited value, in the response and in
    /// the token claim alike, so that encoding is lossless only while no scope carries white space.
    /// Refusing one enforces the published encoding rather than adding a rule to it: accepting it would
    /// produce a token whose scope claim says something the caller did not request.
    /// </remarks>
    private const string ScopeSpacedDetail =
        "A requested scope contains white space. The granted set is carried as a single "
        + "space-delimited value in both the response and the token claim, so a scope containing white "
        + "space could not be recovered by the reader and would silently become two scopes.";

    /// <summary>Detail for a scope set that repeats a scope.</summary>
    private const string ScopeDuplicateDetail =
        "The requested scope set repeats a scope. The published request schema requires the scope "
        + "array to hold unique items, and scopes are compared exactly rather than case-insensitively, "
        + "because a scope is an opaque protocol token.";

    /// <summary>
    /// Detail for a request that presented no usable transport credential.
    /// </summary>
    /// <remarks>
    /// The two conditions the published contract folds into one status are stated together here
    /// because the response must not distinguish them: telling a caller which of the two it hit would
    /// describe this service's trust configuration to an unauthenticated party.
    /// </remarks>
    private const string CredentialAbsentDetail =
        "No usable caller credential was presented. This operation accepts a shared secret as an HTTP "
        + "Basic credential, or a client certificate; a request presenting neither, one whose Basic "
        + "credential is malformed, one naming an identity this service does not know, one presenting "
        + "an incorrect secret, and one whose certificate establishes no usable identity are all "
        + "refused identically - the response does not distinguish them, because telling an "
        + "unauthenticated party which condition it hit would let it enumerate this deployment's "
        + "callers one request at a time. There is no bearer-token alternative on this operation, "
        + "because a caller cannot present a token in order to obtain its first token, and no member "
        + "of the request body carries a credential. No token was created and no signing material was "
        + "read.";

    /// <summary>
    /// Detail for a claimed identity that disagrees with the one the transport established.
    /// </summary>
    /// <remarks>
    /// It names NEITHER the expected identity NOR any part of the stored configuration, which is what
    /// the published contract requires of this response. A message that reported the expected value
    /// would turn a refusal into an identity oracle.
    /// </remarks>
    private const string SubjectMismatchDetail =
        "The claimed subject is not the identity the presented credential establishes. The subject "
        + "member is a claim; the identity honoured is the credential's. Neither the expected identity "
        + "nor any part of this service's configuration is reported.";

    /// <summary>Detail for an authenticated caller with no entry in the issuance roster.</summary>
    /// <remarks>
    /// Reachable only by calling the issuer directly, since the issuance edge authenticates against the
    /// same roster - but stated as its own sentence because the outcome is its own outcome, and a
    /// response that described it as one of the others would be wrong.
    /// </remarks>
    private const string SubjectNotRegisteredDetail =
        "The claimed subject has no entry in this issuer's roster, so it is permitted no audience and "
        + "no scope. No token was created and no signature was computed. The roster is deliberately "
        + "not enumerated.";

    /// <summary>
    /// Detail for an audience this deployment serves but this caller may not address.
    /// </summary>
    /// <remarks>
    /// Distinguished from <see cref="AudienceNotPermittedDetail"/> so that an operator is sent to the
    /// right configuration section - that one means the deployment serves no such audience, this one
    /// means the caller's own roster entry does not grant it. Neither echoes the requested value nor
    /// enumerates any roster.
    /// </remarks>
    private const string AudienceNotPermittedForSubjectDetail =
        "This caller is not permitted to obtain a token for the requested audience. The audience is "
        + "one this issuer serves, but it is not among those granted to the authenticated identity: "
        + "both gates apply, and least privilege is stated per caller rather than per deployment. No "
        + "token was created, no signature was computed and nothing was substituted - an audience a "
        + "caller may not address is refused rather than replaced with one it may. Neither the "
        + "requested value nor the granted set is reported.";

    /// <summary>Detail for a scope set naming something this caller is not granted.</summary>
    /// <remarks>
    /// It names NEITHER the offending scope NOR the granted set, and that is what stops the refusal
    /// being used to probe the roster one scope at a time. Which scope was refused is a fact the
    /// caller's own operator can read from the caller's roster entry.
    /// </remarks>
    private const string ScopeNotPermittedForSubjectDetail =
        "The requested scope set names at least one scope the authenticated identity is not granted. "
        + "The request is REFUSED rather than narrowed: the published contract permits a granted set to "
        + "be narrower than a requested one, but silently dropping a scope would hand back a usable "
        + "token that lacks a capability the caller asked for, and the loss would surface at whichever "
        + "service refuses the later call - far from the misconfiguration that caused it. No token was "
        + "created and no signature was computed. Neither the offending scope nor the granted set is "
        + "reported.";

    /// <summary>Detail for an audience this issuer does not serve.</summary>
    /// <remarks>
    /// The configured roster is deliberately not enumerated and the requested value is not echoed. A
    /// refusal that listed the roster would let an authenticated caller enumerate the deployment's
    /// audience configuration one request at a time.
    /// </remarks>
    private const string AudienceNotPermittedDetail =
        "The requested audience is not one this issuer serves. No token was created, no signature was "
        + "computed and nothing was substituted: an unlisted audience is refused rather than replaced "
        + "with a default, because a token minted for an audience the caller did not ask for would be "
        + "valid somewhere it was never intended to be. The configured roster is deliberately not "
        + "enumerated.";

    /// <summary>Detail for a caller that may not address the audience it asked for.</summary>
    /// <remarks>
    /// <para>
    /// DELIBERATELY THE SAME SENTENCE AS <see cref="AudienceNotPermittedDetail"/>, AND THAT IS THE WHOLE
    /// POINT OF DECLARING IT SEPARATELY RATHER THAN REUSING THAT CONSTANT. Two different decisions reach
    /// this response - an audience on no roster at all, and an audience that exists but is not this
    /// caller's - and a caller able to tell them apart could enumerate the deployment's audience
    /// configuration by asking for candidates and reading which refusal came back. It gets one sentence
    /// for both. The distinction an operator needs is preserved where it is safe to keep: two separate
    /// log records at the issuer.
    /// </para>
    /// <para>
    /// The constant is its own declaration so that a later edit to either sentence is a visible choice
    /// about one of them rather than a silent change to both, with a comment at each site saying why they
    /// currently agree.
    /// </para>
    /// </remarks>
    private const string CallerNotPermittedDetail = AudienceNotPermittedDetail;

    /// <summary>Detail for a request none of whose scopes this caller may carry to that audience.</summary>
    /// <remarks>
    /// <para>
    /// A DISTINCT SENTENCE, UNLIKE THE PAIR ABOVE, AND FOR A REASON THAT IS ABOUT DISCLOSURE RATHER THAN
    /// STYLE. A caller reaching this refusal IS authorized for the audience it named - it learns nothing
    /// about a permission it does not hold - and the fault is in a set it chose, so telling it which part
    /// of its request failed reveals nothing and is the difference between a request it can fix and one
    /// it cannot. Answering the audience sentence here would send it to change the audience, which is the
    /// one part of the request that was right.
    /// </para>
    /// <para>
    /// IT STILL ENUMERATES NOTHING. The permitted set is not listed, because a caller that could read its
    /// own permitted set from a refusal could read it from a refusal for every audience on the roster.
    /// The sentence also states the contract's narrowing rule explicitly, because that rule is what makes
    /// this refusal narrow: a request whose scopes are PARTLY permitted succeeds and returns the
    /// permitted part, so reaching this response means nothing at all was permitted.
    /// </para>
    /// </remarks>
    private const string ScopesNotPermittedDetail =
        "No requested scope is permitted for this caller and audience, so there was nothing to grant. "
        + "Note that a partly permitted request is NOT refused: it succeeds and the response reports the "
        + "narrower granted set, which is why this response means that none of the requested scopes was "
        + "permitted rather than that some were not. No token was created and no signature was computed. "
        + "The permitted set is deliberately not enumerated.";

    /// <summary>Detail for an issuance outcome this build does not recognise.</summary>
    /// <remarks>
    /// Reachable only if the issuance result gains a case this file has not been taught, or reports a
    /// success with no token attached. Answered as a fault of this service rather than of the caller,
    /// and fail-closed: nothing is returned that could be mistaken for a credential.
    /// </remarks>
    private const string OutcomeUnrecognisedDetail =
        "Issuance produced an outcome this build does not recognise, so nothing was returned. The "
        + "request is not at fault and no token was issued.";

    /// <summary>The structured template a successful issuance is recorded with.</summary>
    /// <remarks>
    /// <para>
    /// THREE FIELDS, AND WHAT IS ABSENT IS THE POINT. The operation identifier, the subject and the
    /// audience. The token is not among them, nor the signing key, the key identifier, the granted
    /// scope set, the expiry or any header of the request - and there is no parameter through which one
    /// could arrive.
    /// </para>
    /// <para>
    /// THE SUBJECT IS RECORDED HERE AND DELIBERATELY NOT BY THE ISSUER, and the difference is not an
    /// inconsistency. At the issuer's layer the subject is unvalidated caller text, so that file
    /// refuses to log it. By the time this record is written the subject has been reconciled ordinally
    /// against the identity the presented certificate established, so it is a transport-established
    /// identity rather than caller text - which is exactly what makes it safe to record and useful to
    /// have recorded. The audience is likewise a value that matched the configured roster ordinally by
    /// then, so it is a configured identity rather than caller text.
    /// </para>
    /// </remarks>
    private const string IssuedLogTemplate =
        "Security issued a service token. operation={Operation} subject={Subject} audience={Audience}. "
        + "The token itself is deliberately not recorded.";

    /// <summary>
    /// The fixed message reported when the configured issuance path is blank.
    /// </summary>
    /// <remarks>
    /// Names the configuration key and never its value, matching how every other refusal in this
    /// service reports a configuration fault.
    /// </remarks>
    private const string IssuancePathBlankMessage =
        "Security:TokenEndpointPath is blank, so the token-issuance operation has no address to be "
        + "published at. The host refuses to start rather than leaving the sole issuer unreachable, "
        + "which is a fault that a readiness probe alone would not reveal.";

    /// <summary>
    /// The fixed message reported when the configured issuance path is not rooted.
    /// </summary>
    private const string IssuancePathNotRootedMessage =
        "Security:TokenEndpointPath must begin with a forward slash. A relative pattern would be "
        + "published at an address no consumer could construct from the document.";

    /// <summary>
    /// The fixed message reported when the configured issuance path sits inside the metadata namespace.
    /// </summary>
    /// <remarks>
    /// That namespace holds this service's two anonymous exemptions - the published key set and the
    /// discovery metadata - so an issuance address inside it risks colliding with a route that is
    /// anonymous BY DESIGN. The one route that mints tokens must never be able to land there, and the
    /// options type documents the same constraint from the configuration side.
    /// </remarks>
    private const string IssuancePathInMetadataNamespaceMessage =
        "Security:TokenEndpointPath must not sit inside the well-known metadata namespace. That "
        + "namespace carries this service's anonymous key-set and discovery routes, so an issuance "
        + "address inside it could collide with a route that is anonymous by design.";

    /// <summary>
    /// Maps contract C-01's token-issuance operation.
    /// </summary>
    /// <param name="endpoints">The route builder to declare the route on.</param>
    /// <returns>The same <paramref name="endpoints"/> instance, so that mapping calls compose.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="endpoints"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The configured issuance path is blank, is not rooted, or sits inside the well-known metadata
    /// namespace. Each case fails the host at startup rather than publishing an unreachable or
    /// colliding address for the one route that mints tokens - the fail-fast posture the framework
    /// application object sets [ws_objects/pfw.pbl.src/pfw.sra:L143].
    /// </exception>
    /// <remarks>
    /// <para>
    /// THE SINGLE PUBLIC ENTRY POINT OF THIS FILE, matching the shape the whole folder uses and that
    /// <c>Program.cs</c> calls once per file.
    /// </para>
    /// <para>
    /// THE ADDRESS COMES FROM CONFIGURATION RATHER THAN FROM A LITERAL HERE. The options type declares a
    /// member for every path it owns and this is one of them, so reading it is preferred over a second
    /// spelling that could drift from the value the rest of the service reads. It is resolved ONCE at
    /// registration - the route pattern is fixed for the lifetime of the host, so re-reading it per
    /// request would buy nothing and would let two requests be routed by two different values.
    /// </para>
    /// <para>
    /// THE PATH IS VALIDATED BEFORE IT IS MAPPED, for the same reason the sibling key-publication file
    /// validates its two addresses: a misconfigured address on this route is not a degraded service, it
    /// is a service on which no token can ever be obtained, and a readiness probe would report it
    /// healthy throughout.
    /// </para>
    /// </remarks>
    public static IEndpointRouteBuilder MapTokenEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        SecurityOptions security = endpoints.ServiceProvider
            .GetRequiredService<IOptions<SecurityOptions>>()
            .Value;

        string issuancePath = RequireIssuancePath(security.TokenEndpointPath);

        endpoints.MapPost(issuancePath, IssueToken)
            // --------------------------------------------------------------------------------------
            // CONSTRAINT C-G. THE ROUTE THAT MINTS IS NEVER ANONYMOUS AND NEVER AUTHORISED BY
            // OMISSION.
            //
            // Called EXPLICITLY rather than relying on the host's default-deny fallback policy. The
            // fallback would close this route too, but a requirement satisfied by OMISSION is
            // invisible at the thing it guards and would evaporate silently if that policy were ever
            // relaxed. Unconditional: not wrapped in an environment test, not paired with an
            // anonymous variant and not weakened by a configuration switch. `AllowAnonymous` appears
            // nowhere in this file, and this route is none of the service's three exemptions.
            //
            // A POLICY IS SUPPLIED HERE, WHERE THE TWO SIBLING FILES SUPPLY NONE, AND THAT IS THE
            // CONTRACT SPEAKING RATHER THAN A PREFERENCE. Every other authenticated operation on this
            // service is protected by a bearer token, so the parameterless form - an authenticated
            // principal under the application's default policy - says exactly the right thing there.
            // It would say the WRONG thing here: the published document applies the mutual-TLS scheme
            // to this operation as an OVERRIDE of the document-level bearer requirement, because a
            // caller cannot present a bearer token in order to obtain its first one. Requiring a
            // bearer principal on this route would make the sole issuer unreachable by the only
            // callers that need it, which is a functional defect dressed as hardening.
            //
            // The requirement is enforced PER OPERATION rather than at the listener on purpose. The
            // transport requests a client certificate and validates one when it arrives, without
            // demanding one, so that this operation can require it while the anonymous readiness
            // probe, the published key set and the bearer-authenticated cryptographic operations all
            // stay reachable on the same single listener. Demanding a certificate at the listener
            // would break three contracts to enforce one.
            // --------------------------------------------------------------------------------------
            .RequireAuthorization(ConfigureIssuancePolicy)
            .WithName(OperationName)
            .WithTags(TagName)
            .WithSummary(OperationSummary)
            .WithDescription(OperationDescription)
            // ------------------------------------------------------------------------------------------
            // EXACTLY THE FIVE RESPONSES THE AUTHORED CONTRACT DECLARES FOR THIS OPERATION, AND NO
            // OTHERS. The handler returns the untyped result interface rather than a typed result
            // union for that reason: a union lets the framework infer response metadata of its own
            // from the result types, and the declarations below are meant to be the whole story. The
            // sibling key-publication file returns the interface for the same reason.
            //
            // Every error is `application/problem+json`, stated rather than left to a default,
            // because that media type is what the contract tells a consumer it will find there - a
            // single error shape across the whole document, so a consumer writes one error handler.
            // ------------------------------------------------------------------------------------------
            .Produces<TokenIssuanceResponse>(
                StatusCodes.Status200OK,
                MediaTypeNames.Application.Json)
            .ProducesProblem(
                StatusCodes.Status400BadRequest,
                MediaTypeNames.Application.ProblemJson)
            // The refusal of a caller that presented no usable transport credential. A DECLARED
            // response rather than an implementation detail: it is half of what this operation
            // promises, and it is the standing proof that the boundary is authenticated. Two producers
            // can answer it - the authorization middleware, before the route runs, when the policy
            // above can see the connection and finds no certificate on it; and this file's own arm,
            // which is the one that carries the contract's problem body and its return code.
            .ProducesProblem(
                StatusCodes.Status401Unauthorized,
                MediaTypeNames.Application.ProblemJson)
            // The refusal of an accepted caller that is not permitted the requested subject or
            // audience. Distinguished from the previous status by design, so a caller can tell a
            // transport problem from a permission one.
            .ProducesProblem(
                StatusCodes.Status403Forbidden,
                MediaTypeNames.Application.ProblemJson)
            .ProducesProblem(
                StatusCodes.Status500InternalServerError,
                MediaTypeNames.Application.ProblemJson)
            .AddOpenApiOperationTransformer(DeclareIssuanceCredentialRequirementAsync);

        return endpoints;
    }

    /// <summary>
    /// Issues one short-lived service token, or refuses the request with the contract's own error
    /// shape.
    /// </summary>
    /// <param name="request">
    /// The published request body, or <see langword="null"/> when none arrived. Declared nullable
    /// deliberately - see the remarks.
    /// </param>
    /// <param name="httpContext">
    /// The current request context, read for ONE thing: the client certificate the transport
    /// established the caller's identity with. No header, no claim, no cookie and no other feature of
    /// it is read.
    /// </param>
    /// <param name="issuer">
    /// The sole minter. This handler hands it a well-formed request and projects its result; it
    /// constructs no claim and computes no signature.
    /// </param>
    /// <param name="loggerFactory">
    /// The logger factory a refusal is recorded through, and the source of this file's own success
    /// record.
    /// </param>
    /// <returns>
    /// The issued token, or a problem response carrying the legacy return code for the condition.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="httpContext"/>, <paramref name="issuer"/> or <paramref name="loggerFactory"/>
    /// is <see langword="null"/>, which can only mean the composition root is miswired.
    /// </exception>
    /// <remarks>
    /// <para>
    /// A NAMED METHOD RATHER THAN AN INLINE LAMBDA, and <c>internal</c> rather than <c>private</c>, so
    /// that the sibling test project can call it DIRECTLY as well as through a booted host - which is
    /// what constraint C-H asks of the handler, and what makes every arm below reachable by a unit test
    /// rather than only by a transport that a test host cannot produce. <c>internal</c> keeps it off
    /// the published surface, and the project file grants the test project access.
    /// </para>
    /// <para>
    /// SYNCHRONOUS, BECAUSE NOTHING HERE SUSPENDS. Minting is computation over material already in
    /// memory, this service has no outbound edge and reads no store while issuing, and the body has
    /// already been bound by the time the handler is entered. An asynchronous signature would advertise
    /// a suspension point that does not exist.
    /// </para>
    /// <para>
    /// THE BODY PARAMETER IS NULLABLE ON PURPOSE, AND THAT IS A CONTRACT-FIDELITY DECISION RATHER THAN
    /// LAXITY. A non-nullable body parameter makes the framework refuse an absent body itself, with a
    /// status but with NO problem document and therefore no return code. The published contract states
    /// that its bad-request response carries the legacy return code, so the only way to honour it is to
    /// accept the absence and answer it HERE. The same reasoning makes every member of the request
    /// record nullable, and the cryptographic surface's request records take the same shape for the
    /// same reason. The single cost is that the GENERATED request schema is one degree more permissive
    /// than the authored one; the observable behaviour is identical, and the authored document remains
    /// authoritative for what a caller may send.
    /// </para>
    /// <para>
    /// THE ORDER OF THE ARMS IS DELIBERATE, AND IT PUTS SHAPE BEFORE IDENTITY. A malformed request is
    /// answered before the certificate is examined, so a caller with a broken request is told what is
    /// broken rather than being told about its credentials, and a caller with no credentials learns
    /// nothing from the shape of its own payload about how this service authenticates. Neither arm
    /// leaks the other's information, because neither reports a value.
    /// </para>
    /// <para>
    /// NO CLOCK IS READ HERE. Every instant in the response is taken from the issuer's result, which
    /// derives all three time claims from ONE reading of the injected clock seam and truncates it to a
    /// whole second first. Reading an ambient clock here would produce a response whose instants
    /// disagreed with the token's own claims, and would make a characterization recording
    /// irreproducible under a fixed clock.
    /// </para>
    /// </remarks>
    internal static IResult IssueToken(
        TokenIssuanceRequestBody? request,
        HttpContext httpContext,
        [FromServices] TokenIssuer issuer,
        [FromServices] ClientCertificateTrust trust,
        [FromServices] ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(issuer);
        ArgumentNullException.ThrowIfNull(trust);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        // ------------------------------------------------------------------------------------------
        // 1. THE PUBLISHED REQUEST SHAPE. Every check below restates a constraint the authored schema
        //    states, and none of them invents one. The scope constraints are the same ones the
        //    issuance request type enforces in its own constructor; they are checked HERE first
        //    because the contract classifies a malformed request as a client error the ENDPOINT
        //    reports, whereas an unlisted audience is a permission decision only the issuer can make
        //    against its configured roster. That split is the request type's own documented design.
        // ------------------------------------------------------------------------------------------
        ProblemHttpResult? malformed = ValidateRequestShape(request, loggerFactory);

        if (malformed is not null)
        {
            return malformed;
        }

        // Established by the validation above, which refuses every shape in which either could be
        // absent. Read into locals so the compiler carries the non-null state into the call below.
        string subject = request!.Subject!;
        string audience = request.Audience!;

        // ------------------------------------------------------------------------------------------
        // 2. THE PRESENTED CREDENTIAL'S IDENTITY, WHICH IS THE ONLY IDENTITY THIS OPERATION HONOURS.
        //
        //    TWO SCHEMES, AND THE ORDER BETWEEN THEM IS DELIBERATE - see ResolvePresentedIdentity. The
        //    route's authorization policy already refuses a request carrying neither credential
        //    wherever it can see the connection, so in the ordinary case this arm is a second gate
        //    rather than the first. It is unconditional all the same, and that is the point: the
        //    security property of this operation does not depend on the policy's ability to reach the
        //    connection, and this arm is the one that produces the problem body and the return code
        //    the contract declares for the refusal.
        //
        //    A certificate that establishes no usable identity is refused here rather than at the
        //    reconciliation below, because there is nothing to reconcile against - and it is refused
        //    with the SAME status and the SAME sentence as an absent certificate, so the response
        //    cannot be used to probe which of the two conditions was hit.
        //
        //    TRUST IS ESTABLISHED BEFORE THE IDENTITY IS EVEN READ, which is the ordering the published
        //    401 depends on: that response's declared meaning is "no certificate was presented, OR the
        //    certificate presented is not trusted", and the second half is only a behaviour if the
        //    trust decision precedes the identity. Reading a common name first and validating
        //    afterwards would mean honouring a name from a certificate whose issuer was never
        //    established - and since the name IS the caller identity, that is the whole authentication
        //    of this operation. All THREE non-trusted outcomes answer the same sentence for the same
        //    reason the two conditions above do: the response is not a probe.
        // ------------------------------------------------------------------------------------------
        X509Certificate2? presented = httpContext.Connection.ClientCertificate;

        if (trust.Evaluate(presented) != ClientCertificateTrustState.Trusted)
        {
            return Unauthenticated(loggerFactory);
        }

        string? callerIdentity = ResolveCallerIdentity(presented);

        if (callerIdentity is null)
        {
            return Unauthenticated(loggerFactory);
        }

        // ------------------------------------------------------------------------------------------
        // 3. THE CLAIM RECONCILED AGAINST THE CREDENTIAL. Ordinal, because an identity is compared
        //    exactly everywhere else in this service - the issuer compares the audience ordinally
        //    against its roster and trims nothing, and the signing layer compares its configured key
        //    identifier the same way. Folding case here would honour a subject the certificate does
        //    not establish, and trimming would honour one that merely resembles it.
        //
        //    docs/ARCHITECTURE.md section 9.3.1 assigns this mapping to this file, and the refusal
        //    names neither the expected identity nor any part of the stored configuration.
        //
        //    THIS IS WHAT MAKES THE SUBJECT CLAIM UNFORGEABLE. The roster's subject IS the credential
        //    identity, so a caller can only ever obtain a token whose subject is its own - there is no
        //    arrangement of a well-formed request in which one registered caller mints for another.
        // ------------------------------------------------------------------------------------------
        if (!string.Equals(subject, callerIdentity, StringComparison.Ordinal))
        {
            return SubjectRefused(loggerFactory);
        }

        // ------------------------------------------------------------------------------------------
        // 4. MINT - BY DELEGATION, AND BY NO OTHER PATH. The request type refuses to exist in a
        //    malformed state, so constructing it here is the point at which this handler's validation
        //    and the issuer's precondition become the same statement. The scope set is passed THROUGH
        //    exactly as the caller supplied it: nothing is added, nothing is removed, nothing is
        //    reordered and no scope name is invented.
        // ------------------------------------------------------------------------------------------
        TokenIssuanceRequest issuance = new(subject, audience, request.Scopes!);

        TokenIssuanceResult result = issuer.Issue(issuance);

        // ------------------------------------------------------------------------------------------
        // 5. THE OUTCOME, SWITCHED EXPLICITLY AND NEVER TESTED FOR TRUTHINESS.
        //
        //    The published outcome set is closed and every case is named. The null-token guard on the
        //    success case is not redundant defensiveness: the result type declares the token nullable
        //    precisely so a consumer that reaches for it without checking is warned, and with warnings
        //    treated as errors that warning is a build failure. Checking it here is how that guarantee
        //    is honoured rather than suppressed.
        //
        //    THREE REFUSALS, ONE STATUS, TWO SENTENCES. The audience and caller arms answer the SAME
        //    sentence on purpose, so that a caller cannot tell an audience this deployment does not
        //    serve from one it serves but this caller may not have - that difference is an audience
        //    oracle, and it is kept in the issuer's log records instead, where an operator can read it
        //    and a caller cannot. The scope arm answers its own sentence, because a caller reaching it
        //    IS authorized for the audience and needs to know which part of its request failed.
        //
        //    An outcome this build does not recognise is answered as a fault of this service. It is
        //    fail-closed - nothing is returned that could be mistaken for a credential - and it is
        //    mapped explicitly rather than left to fall through, so that a future added case is a
        //    visible 500 rather than a silent success.
        // ------------------------------------------------------------------------------------------
        switch (result.Outcome)
        {
            case TokenIssuanceOutcome.Issued when result.Token is IssuedToken token:
                LogIssued(loggerFactory, subject, audience);

                return TypedResults.Ok(Project(token));

            case TokenIssuanceOutcome.AudienceNotPermitted:
                return AudienceRefused(loggerFactory);

            case TokenIssuanceOutcome.CallerNotPermitted:
                return CallerRefused(loggerFactory);

            case TokenIssuanceOutcome.ScopesNotPermitted:
                return ScopesRefused(loggerFactory);

            default:
                return Faulted(loggerFactory);
        }
    }

    /// <summary>
    /// Checks one issuance request against every constraint the published request schema states.
    /// </summary>
    /// <param name="request">The bound request body, or <see langword="null"/> when none arrived.</param>
    /// <param name="loggerFactory">The logger factory a refusal is recorded through.</param>
    /// <returns>
    /// The refusal to answer with, or <see langword="null"/> when the request is well formed.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="loggerFactory"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// SEPARATED FROM THE HANDLER SO IT IS DIRECTLY TESTABLE AS A TABLE, which is what makes the
    /// per-service coverage gate reachable on every arm without booting a host or fabricating a
    /// transport. Returning the refusal rather than throwing keeps a well-formed rejection off the
    /// exception path.
    /// </para>
    /// <para>
    /// EVERY ARM CARRIES THE SAME RETURN CODE, AND THAT IS THE CONTRACT'S OWN CHOICE RATHER THAN
    /// convenience: the published bad-request response states that its return code typically carries
    /// the invalid-argument value, and every condition below is an argument the caller can correct.
    /// The status is taken from the shared explicit per-code map rather than derived from the code's
    /// sign or from a predicate, because the legacy algebra is tri-state and holed - a prevention reads
    /// as a success under the success predicate, and a cancellation is neither succeeded nor failed.
    /// </para>
    /// <para>
    /// NOTHING IS REPAIRED. No value is trimmed, case-folded, sorted, de-duplicated or reordered before
    /// or after these checks. The subject reaches the claim exactly as given, the audience is compared
    /// to the roster exactly as given, and the scope set keeps the caller's own order so the granted
    /// value a caller reads back is recognisably its own request. Silently repairing a request would
    /// make the difference between what was asked for and what was granted invisible.
    /// </para>
    /// </remarks>
    internal static ProblemHttpResult? ValidateRequestShape(
        TokenIssuanceRequestBody? request,
        ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);

        if (request is null)
        {
            return Malformed(RequestAbsentDetail, loggerFactory);
        }

        // Blank includes white space only, for both members. A whitespace subject would be stamped
        // into a token as though it were an identity, and a whitespace audience would be compared
        // against the roster as though it were one.
        if (string.IsNullOrWhiteSpace(request.Subject))
        {
            return Malformed(SubjectBlankDetail, loggerFactory);
        }

        if (string.IsNullOrWhiteSpace(request.Audience))
        {
            return Malformed(AudienceBlankDetail, loggerFactory);
        }

        IReadOnlyList<string>? scopes = request.Scopes;

        if (scopes is null || scopes.Count == 0)
        {
            return Malformed(ScopeSetEmptyDetail, loggerFactory);
        }

        // Ordinal, because a scope is an opaque protocol token: two spellings that differ only by case
        // are two different scopes, and folding them together would treat a distinct scope as a
        // duplicate and refuse a request the contract permits.
        HashSet<string> seen = new(StringComparer.Ordinal);

        foreach (string scope in scopes)
        {
            if (string.IsNullOrEmpty(scope))
            {
                return Malformed(ScopeBlankDetail, loggerFactory);
            }

            // White space is refused in EVERY form rather than only the delimiter, because a tab or a
            // line break inside a claim value is a hazard in its own right.
            if (scope.Any(char.IsWhiteSpace))
            {
                return Malformed(ScopeSpacedDetail, loggerFactory);
            }

            if (!seen.Add(scope))
            {
                return Malformed(ScopeDuplicateDetail, loggerFactory);
            }
        }

        return null;
    }

    /// <summary>
    /// Reports the caller identity the request's presented credential establishes, under either of the
    /// two schemes this operation accepts.
    /// </summary>
    /// <param name="httpContext">
    /// The current request. TWO features of it are read and no others: the <c>Authorization</c> header,
    /// and the client certificate the transport accepted.
    /// </param>
    /// <param name="clients">The issuance roster a presented shared secret is authenticated against.</param>
    /// <returns>
    /// The authenticated caller identity, or <see langword="null"/> when the request presents no usable
    /// credential under either scheme.
    /// </returns>
    /// <exception cref="ArgumentNullException">Either argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// THE SHARED-SECRET SCHEME IS TRIED FIRST, AND THE ORDER IS LOAD BEARING RATHER THAN ARBITRARY. A
    /// caller that took the trouble to send an <c>Authorization</c> header is asserting an identity
    /// explicitly, so that assertion is the one answered - if it fails, the request is refused rather
    /// than quietly re-authenticated as whatever identity a certificate on the connection happens to
    /// establish. Trying the certificate first would mean a caller presenting a WRONG secret could still
    /// be authenticated, and a deployment could not tell from the outside which credential had actually
    /// been honoured.
    /// </para>
    /// <para>
    /// BOTH PATHS ARE LIVE, AND NEITHER IS VESTIGIAL. This service's own listener is
    /// <c>https://+:5104</c> with <c>ClientCertificateMode</c> <c>AllowCertificate</c>, so a client
    /// certificate CAN be presented and the second path is reachable on the topology this repository
    /// runs. Which one a given caller uses is a deployment fact rather than a code one: a deployment
    /// that mounts caller certificates from the local authority the certificate recipe in
    /// docs/ARCHITECTURE.md section 9.3.1 describes reaches the certificate path, and a roster entry
    /// naming no secret configuration key is exactly such a caller - while a deployment that terminates
    /// TLS ahead of this service in a proxy or a mesh sidecar has no certificate for the application to
    /// read at all, and the secret scheme is then the only one that can reach the operation. Removing
    /// either path would delete a working credential scheme the published contract declares.
    /// </para>
    /// <para>
    /// A CERTIFICATE IDENTITY IS NOT MATCHED AGAINST THE ROSTER HERE, deliberately, and the asymmetry is
    /// worth stating because it looks like an omission. A shared secret has to be checked against the
    /// roster because the roster is the only thing that knows it; a certificate has ALREADY been
    /// validated by the transport against the configured trust anchor, which is a stronger check than
    /// this file could perform, and the roster lookup that decides what the identity may ASK FOR still
    /// happens - in the issuer, on every request, for both schemes. An unregistered certificate identity
    /// is therefore refused by the issuer with its own outcome rather than being silently accepted.
    /// </para>
    /// <para>
    /// NOTHING IS TRIMMED, CASE-FOLDED OR REPAIRED about a resolved identity, under either scheme. It is
    /// compared ordinally against the claimed subject immediately afterwards, and it becomes the token's
    /// subject claim if the two agree.
    /// </para>
    /// </remarks>
    internal static string? ResolvePresentedIdentity(
        HttpContext httpContext,
        IssuanceClientRegistry clients)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(clients);

        if (TryReadBasicCredential(httpContext, out string? clientId, out string? secret))
        {
            // An asserted identity is answered on its own terms: a failure here is a refusal, never a
            // fall-through to the certificate path.
            return clients.Authenticate(clientId, secret)?.Subject;
        }

        return ResolveCallerIdentity(httpContext.Connection.ClientCertificate);
    }

    /// <summary>
    /// Reads an HTTP Basic credential out of the request's <c>Authorization</c> header.
    /// </summary>
    /// <param name="httpContext">The current request.</param>
    /// <param name="clientId">The presented user-id when one was read; otherwise <see langword="null"/>.</param>
    /// <param name="secret">The presented password when one was read; otherwise <see langword="null"/>.</param>
    /// <returns>
    /// <see langword="true"/> when a syntactically valid Basic credential was read. A header that names
    /// the Basic scheme but is malformed answers <see langword="true"/> with an EMPTY identity, so that
    /// the caller refuses it rather than falling through to the other scheme - see the remarks.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="httpContext"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// A MALFORMED BASIC HEADER RETURNS TRUE, AND THAT IS THE ONE COUNTER-INTUITIVE LINE HERE. Returning
    /// false would send a caller that presented a broken credential down the certificate path, where it
    /// might be authenticated as a different identity than the one it asserted - so a garbled header
    /// would become a silent identity substitution. Returning true with an empty identity makes the
    /// roster lookup fail and the request be refused, which is the only correct outcome. The refusal is
    /// the same status and the same sentence as every other authentication failure, so nothing is
    /// disclosed by the distinction.
    /// </para>
    /// <para>
    /// A HEADER NAMING A DIFFERENT SCHEME - a bearer token, say - RETURNS FALSE, because such a caller
    /// has asserted nothing under this scheme. The certificate path is then tried, and if it too yields
    /// nothing the request is refused. That matters because the document-level bearer requirement means
    /// a caller may well have a token in hand; presenting it here must neither authenticate it nor
    /// prevent it from presenting the credential this operation does accept.
    /// </para>
    /// <para>
    /// THE PARSING IS RFC 7617's, DONE BY THE PLATFORM WHERE THE PLATFORM HAS IT. The header value is
    /// split by the framework's own header parser rather than by hand, the base64 payload is decoded by
    /// the platform decoder, and the user-id is taken as everything BEFORE THE FIRST COLON - the
    /// specification is explicit that a colon may not appear in the user-id and that everything after
    /// the first one is the password, so splitting on the last colon or on all of them would corrupt any
    /// secret containing one. A payload with no colon at all is malformed.
    /// </para>
    /// <para>
    /// THE PAYLOAD IS DECODED AS UTF-8. RFC 7617 leaves the charset to the server absent a parameter and
    /// UTF-8 is the modern reading; the roster encodes its configured secrets the same way, so the two
    /// sides agree byte for byte. A payload that is not valid base64 is malformed, and an
    /// invalid-UTF-8 sequence is refused rather than replaced with a substitution character - a
    /// substitution would let two distinct byte sequences compare equal.
    /// </para>
    /// </remarks>
    internal static bool TryReadBasicCredential(
        HttpContext httpContext,
        [NotNullWhen(true)] out string? clientId,
        [NotNullWhen(true)] out string? secret)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        clientId = null;
        secret = null;

        string? header = httpContext.Request.Headers.Authorization.ToString();

        if (string.IsNullOrWhiteSpace(header)
            || !AuthenticationHeaderValue.TryParse(header, out AuthenticationHeaderValue? parsed)
            || !string.Equals(parsed.Scheme, BasicSchemeToken, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // From here on the caller HAS asserted an identity under this scheme, so every exit reports
        // true and lets the roster lookup refuse it. See the remarks for why falling through would be
        // an identity substitution.
        clientId = string.Empty;
        secret = string.Empty;

        if (string.IsNullOrEmpty(parsed.Parameter))
        {
            return true;
        }

        byte[] payload;

        try
        {
            payload = Convert.FromBase64String(parsed.Parameter);
        }
        catch (FormatException)
        {
            return true;
        }

        string decoded;

        try
        {
            // Throwing UTF-8, so an invalid sequence is refused rather than silently substituted.
            decoded = new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false,
                throwOnInvalidBytes: true).GetString(payload);
        }
        catch (ArgumentException)
        {
            return true;
        }
        finally
        {
            Array.Clear(payload);
        }

        int separator = decoded.IndexOf(':', StringComparison.Ordinal);

        if (separator < 0)
        {
            return true;
        }

        clientId = decoded[..separator];
        secret = decoded[(separator + 1)..];

        return true;
    }

    /// <summary>
    /// Reports the caller identity a presented client certificate establishes.
    /// </summary>
    /// <param name="certificate">
    /// The certificate the transport accepted, or <see langword="null"/> when none was presented.
    /// </param>
    /// <returns>
    /// The identity the certificate establishes, or <see langword="null"/> when there is no
    /// certificate or it establishes none.
    /// </returns>
    /// <remarks>
    /// <para>
    /// THE IDENTITY IS THE CERTIFICATE'S COMMON NAME, and that is the local certificate recipe's own
    /// convention rather than a choice made here: docs/ARCHITECTURE.md section 9.3.1 issues one client
    /// certificate per calling service with the caller's service identity as its common name, and those
    /// identities are the same ones the audience roster carries.
    /// </para>
    /// <para>
    /// THE FRAMEWORK'S OWN ACCESSOR IS USED RATHER THAN PARSING THE SUBJECT NAME. A distinguished name
    /// is a structured value with quoting, escaping and ordering rules, and a hand-rolled split on a
    /// comma gets every one of them wrong on the certificates that matter - the ones whose common name
    /// contains a separator. Getting it wrong here would mean honouring an identity the certificate
    /// does not establish, which is the worst failure this file can have.
    /// </para>
    /// <para>
    /// A BLANK RESULT IS TREATED AS NO IDENTITY. The accessor answers an empty string for a certificate
    /// whose subject carries no common name, and an empty identity is not something a claim can be
    /// reconciled against; treating it as absent is what keeps the reconciliation below a comparison of
    /// two real identities rather than a comparison two blanks could satisfy.
    /// </para>
    /// <para>
    /// NO PART OF THE CERTIFICATE IS RETAINED, LOGGED OR RETURNED beyond that one name. The thumbprint,
    /// the issuer, the serial number, the validity window and the public key are never read here,
    /// because this method answers ONE question - what identity does this certificate carry - and
    /// nothing else.
    /// </para>
    /// <para>
    /// IT DOES NOT ESTABLISH TRUST, AND MUST NOT BE CALLED BEFORE SOMETHING ELSE HAS. Chain
    /// verification against the configured anchor, the validity window, revocation and key usage all
    /// belong to <see cref="ClientCertificateTrust"/>, which the operation consults BEFORE this method
    /// - because the name this method reads IS the caller identity, so reading it from a certificate
    /// whose issuer was never established would be authentication by assertion. The two are separate
    /// types precisely so that neither can be mistaken for the other: this one is a projection, that
    /// one is a decision.
    /// </para>
    /// </remarks>
    internal static string? ResolveCallerIdentity(X509Certificate2? certificate)
    {
        if (certificate is null)
        {
            return null;
        }

        string identity = certificate.GetNameInfo(X509NameType.SimpleName, forIssuer: false);

        return string.IsNullOrWhiteSpace(identity) ? null : identity;
    }

    /// <summary>
    /// Reports whether the request this authorization decision is being made for presents a credential
    /// under either scheme this operation accepts.
    /// </summary>
    /// <param name="context">The authorization context the middleware built for the request.</param>
    /// <returns>
    /// <see langword="true"/> when a Basic credential or a client certificate is present, or when this
    /// decision cannot see the request at all; otherwise <see langword="false"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="context"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// THE EARLIEST REFUSAL, NOT THE AUTHORITATIVE ONE. This is what makes the route's requirement
    /// visible at its declaration and stops a caller with no transport credential before the handler
    /// runs. The AUTHORITATIVE refusal is the handler's own arm, which is unconditional and which
    /// carries the problem body and the return code the contract declares - so the security property of
    /// this operation does not depend on this predicate at all.
    /// </para>
    /// <para>
    /// WHY AN ABSENT CONNECTION ANSWERS TRUE RATHER THAN FALSE, WHICH IS THE ONE LINE HERE THAT NEEDS
    /// DEFENDING. The middleware supplies the request context as the authorization resource by default,
    /// and that default is switchable by host configuration; where it has been switched, this predicate
    /// can no longer see the connection. Answering false there would refuse EVERY caller and leave the
    /// system unable to obtain a single token, while answering true defers to the handler's
    /// unconditional check, which refuses exactly the same requests with a better response. Deferring
    /// therefore loses no security and costs no correctness - the check that matters still runs.
    /// </para>
    /// <para>
    /// PRESENCE ONLY, DELIBERATELY. Whether the certificate is TRUSTED, whether it establishes a usable
    /// identity, and whether that identity matches the claimed subject are CONTRACT decisions with their
    /// own declared statuses and their own response bodies, so they belong to the operation rather than
    /// to a policy that can only answer yes or no. The trust decision in particular is
    /// <see cref="ClientCertificateTrust"/>'s, is made before the identity is read, and is deliberately
    /// NOT duplicated here: a policy that answered no for an untrusted certificate would produce a
    /// bodiless challenge where the contract declares a problem document.
    /// </para>
    /// <para>
    /// BOTH SCHEMES ARE ACCEPTED HERE BECAUSE THE OPERATION ACCEPTS BOTH, and getting this wrong is a
    /// FUNCTIONAL outage rather than a weakening. This service's own listener requests a client
    /// certificate, but a deployment that terminates TLS ahead of it - a reverse proxy, or a mesh
    /// sidecar - presents none to the application. A predicate that DEMANDED a certificate would refuse
    /// every caller under such a topology: the sole issuer would answer nothing at all, no service in
    /// the system could obtain a credential, and the readiness probe would report healthy throughout.
    /// </para>
    /// </remarks>
    internal static bool HasIssuanceCredential(AuthorizationHandlerContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Resource is not HttpContext httpContext)
        {
            return true;
        }

        return httpContext.Connection.ClientCertificate is not null
            || TryReadBasicCredential(httpContext, out _, out _);
    }

    /// <summary>
    /// Projects one minted token onto the published response shape.
    /// </summary>
    /// <param name="token">The minted token and the metadata issuance already computed.</param>
    /// <returns>The response body, member for member the published schema.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="token"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// A PROJECTION AND NOTHING MORE. Nothing is recomputed, nothing is re-derived and the token is not
    /// parsed in order to read a value out of it: the issuer returns every value this body needs
    /// precisely so that the response and the token's own claims cannot disagree. The lifetime in
    /// seconds is EXACTLY the difference between the token's expiry and issuance claims, and the granted
    /// scope value is BYTE-IDENTICAL to the token's scope claim, because both are the same values the
    /// issuer stamped.
    /// </para>
    /// <para>
    /// THE ISSUANCE INSTANT IS CONVERTED, NOT MEASURED. The published member counts whole seconds from
    /// the epoch, matching the token's own issuance claim, and the issuer has already truncated the
    /// instant to a whole second - so the conversion is exact and a fixed clock produces a
    /// byte-identical body across runs. No clock is read here.
    /// </para>
    /// <para>
    /// THE KEY IDENTIFIER IS DELIBERATELY NOT PROJECTED. The published response schema declares no such
    /// member and closes the object against undeclared ones, so adding it would break the schema. The
    /// identifier is public metadata and is available where it belongs: stamped in the token's own
    /// header by the minting library, and published in the key set a verifier selects by.
    /// </para>
    /// </remarks>
    private static TokenIssuanceResponse Project(IssuedToken token)
    {
        ArgumentNullException.ThrowIfNull(token);

        return new TokenIssuanceResponse
        {
            AccessToken = token.AccessToken,
            TokenType = BearerTokenType,
            ExpiresIn = token.ExpiresInSeconds,
            Scope = token.GrantedScope,
            IssuedAt = token.IssuedAt.ToUnixTimeSeconds(),
        };
    }

    /// <summary>
    /// Configures the authorization policy this route requires.
    /// </summary>
    /// <param name="policy">The policy being built for this route alone.</param>
    /// <exception cref="ArgumentNullException"><paramref name="policy"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// BUILT INLINE FOR THIS ROUTE RATHER THAN NAMED, on purpose. A named policy would have to be
    /// registered in the composition root, which would put half of this operation's authentication
    /// requirement in a file that cannot see the contract it comes from, and would let the requirement
    /// be weakened by editing a registration rather than by editing this route.
    /// </para>
    /// <para>
    /// ONE REQUIREMENT, AND IT IS NOT AN AUTHENTICATED PRINCIPAL. Requiring one would demand a bearer
    /// token on the one operation whose whole purpose is to issue the caller's first token. The
    /// requirement is instead the presence of one of the two credentials the published document declares
    /// for this operation - a shared secret presented as an HTTP Basic credential, or a client
    /// certificate - which are the only credentials a caller can have before it holds a token.
    /// </para>
    /// <para>
    /// WHAT THE MIDDLEWARE DOES WITH A FAILURE, STATED SO IT IS NOT DISCOVERED AT A FAILING PROBE. A
    /// caller that presents neither of the two accepted credentials is CHALLENGED and receives the
    /// unauthorized status, which is exactly what the contract declares for a request carrying no
    /// caller credential. A caller that presents a valid bearer token and neither accepted credential
    /// is instead FORBIDDEN and receives the forbidden status - the framework's own classification of
    /// an authenticated caller who fails a policy - and the contract's forbidden response covers it: a
    /// caller offering a credential this operation does not accept is not permitted to obtain a token.
    /// Neither path mints anything, and there is no address on any topology at which a token is issued
    /// to a caller presenting NEITHER accepted credential.
    /// </para>
    /// </remarks>
    private static void ConfigureIssuancePolicy(AuthorizationPolicyBuilder policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        policy.RequireAssertion(HasIssuanceCredential);
    }

    /// <summary>
    /// Validates the configured issuance address before it is published.
    /// </summary>
    /// <param name="configured">The configured path.</param>
    /// <returns>The path, VERBATIM.</returns>
    /// <exception cref="InvalidOperationException">
    /// The path is blank, is not rooted, or sits inside the well-known metadata namespace.
    /// </exception>
    /// <remarks>
    /// <para>
    /// FAIL-FAST AT STARTUP RATHER THAN DEGRADED AT RUN TIME, which is the posture the framework
    /// application object sets by ending a structural fault in process termination rather than in a
    /// warning [ws_objects/pfw.pbl.src/pfw.sra:L111-L144]. A misconfigured address here is not a
    /// degraded service: it is a service on which no token can ever be obtained, while its readiness
    /// probe reports healthy throughout.
    /// </para>
    /// <para>
    /// THE METADATA-NAMESPACE CHECK CONSUMES THE OPTIONS VALIDATOR'S OWN CONSTANT rather than re-
    /// spelling the prefix, so the two cannot drift apart. The namespace carries this service's two
    /// anonymous routes, and the one route that mints must never be able to land beside them.
    /// </para>
    /// <para>
    /// RETURNED UNTRIMMED AND UNALTERED. The address is compared byte for byte by the router and is
    /// published byte for byte in the document, so repairing it here would publish one address and
    /// serve another.
    /// </para>
    /// </remarks>
    private static string RequireIssuancePath(string configured)
    {
        if (string.IsNullOrWhiteSpace(configured))
        {
            throw new InvalidOperationException(IssuancePathBlankMessage);
        }

        if (!configured.StartsWith('/'))
        {
            throw new InvalidOperationException(IssuancePathNotRootedMessage);
        }

        if (configured.StartsWith(
                SecurityOptionsValidator.WellKnownMetadataPathPrefix,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(IssuancePathInMetadataNamespaceMessage);
        }

        return configured;
    }

    // ==============================================================================================
    //  THE FOUR REFUSALS. EVERY ONE OF THEM IS BUILT BY THE FOLDER'S ONE PROBLEM FACTORY.
    //
    //  No second error shape is declared here and this one is not copied: the published document
    //  carries exactly ONE error schema so that a consumer writes one error handler, and the shared
    //  factory in PingEndpoints.cs is where it is built. These four wrappers exist only to bind a
    //  return code, a fixed sentence, a severity and - for the one case that needs it - a deliberate
    //  status to each condition, so that a reader can see the whole mapping in one screen.
    //
    //  NO STATUS IS DERIVED FROM A TRUTHINESS TEST ON A RETURN CODE, HERE OR ANYWHERE. The legacy
    //  algebra is tri-state and holed, measured from the read-only oracle: the success predicate is
    //  `rtCode >= RetCode.OK` [ws_objects/pfw.shared.pbl.src/issucceeded.srf:L11-L12] while PREVENT is
    //  1 [retcode.sru:L42], SO A PREVENTION READS AS A SUCCESS; the failure predicate is
    //  `rtCode < RetCode.OK and rtCode <> RetCode.CANCELLED` [isfailed.srf:L11-L12] while CANCELED and
    //  CANCELLED are two spellings of -2 [retcode.sru:L44-L45], SO A CANCELLATION IS NEITHER SUCCEEDED
    //  NOR FAILED; and both predicates answer false for a null code, SO NULL IS LIKEWISE NEITHER. The
    //  status therefore comes from the shared factory's EXPLICIT per-code map, and where this
    //  operation's semantics differ from that map's default the status is passed explicitly - which is
    //  the extension mechanism the map documents for exactly this purpose.
    //
    //  NO REFUSAL CARRIES A CALLER-SUPPLIED VALUE, a configured value, a roster entry, an expected
    //  identity, a key, a key identifier, a reference of any kind or the token. The factory's signature
    //  has no parameter through which one could arrive, and every sentence passed to it is a compile-
    //  time constant.
    // ==============================================================================================

    /// <summary>
    /// Refuses a request whose shape the published schema does not permit.
    /// </summary>
    /// <param name="detail">The fixed sentence naming the offending member or condition.</param>
    /// <param name="loggerFactory">The logger factory the refusal is recorded through.</param>
    /// <returns>A bad request carrying the invalid-argument return code.</returns>
    /// <remarks>
    /// The status is the shared map's own arm for this code and is deliberately NOT overridden: the
    /// published bad-request response states that its return code typically carries this value, so the
    /// map and the contract already agree. The severity is the legacy's warning icon, because the
    /// condition is one the caller can correct and re-send.
    /// </remarks>
    private static ProblemHttpResult Malformed(string detail, ILoggerFactory loggerFactory) =>
        ProblemResults.Create(
            retCode: RetCode.E_INVALID_ARGUMENT,
            detail: detail,
            severity: ProblemSeverity.Exclamation,
            loggerFactory: loggerFactory);

    /// <summary>
    /// Refuses a request that presented no usable transport credential.
    /// </summary>
    /// <param name="loggerFactory">The logger factory the refusal is recorded through.</param>
    /// <returns>An unauthorized response carrying the access-denied return code.</returns>
    /// <remarks>
    /// <para>
    /// THE STATUS IS PASSED EXPLICITLY, AND THIS IS ONE OF THE TWO CONDITIONS THE SHARED MAP NAMES AS
    /// NEEDING THAT. The published document uses the access-denied code for BOTH its unauthorized and
    /// its forbidden response, so the code alone cannot choose between them; the map defaults to
    /// forbidden because that is the only one of the two a handler can normally reach, and names the
    /// explicit status argument as the way to publish the other. This operation genuinely reaches the
    /// unauthorized one, because it authenticates its caller ITSELF from the transport rather than
    /// through an authentication scheme that would have challenged first.
    /// </para>
    /// <para>
    /// The severity is the legacy's stop-sign icon: a refused authentication on the service holding the
    /// system's only signing secret is not advisory.
    /// </para>
    /// </remarks>
    private static ProblemHttpResult Unauthenticated(ILoggerFactory loggerFactory) =>
        ProblemResults.Create(
            retCode: RetCode.E_ACCESS_DENIED,
            detail: CredentialAbsentDetail,
            severity: ProblemSeverity.StopSign,
            statusCode: StatusCodes.Status401Unauthorized,
            loggerFactory: loggerFactory);

    /// <summary>
    /// Refuses an accepted caller that claimed an identity the transport does not establish.
    /// </summary>
    /// <param name="loggerFactory">The logger factory the refusal is recorded through.</param>
    /// <returns>A forbidden response carrying the access-denied return code.</returns>
    /// <remarks>
    /// The status is the shared map's own arm for this code and needs no override: the caller IS
    /// authenticated - it presented a certificate this service accepted - and is simply not permitted
    /// the subject it asked for, which is precisely the distinction between the two statuses. The
    /// response names neither the expected identity nor any part of the stored configuration.
    /// </remarks>
    private static ProblemHttpResult SubjectRefused(ILoggerFactory loggerFactory) =>
        ProblemResults.Create(
            retCode: RetCode.E_ACCESS_DENIED,
            detail: SubjectMismatchDetail,
            severity: ProblemSeverity.StopSign,
            loggerFactory: loggerFactory);

    /// <summary>
    /// Refuses a well-formed request for an audience this issuer does not serve.
    /// </summary>
    /// <param name="loggerFactory">The logger factory the refusal is recorded through.</param>
    /// <returns>A forbidden response carrying the access-denied return code.</returns>
    /// <remarks>
    /// <para>
    /// FORBIDDEN RATHER THAN BAD REQUEST, AND THE PUBLISHED DOCUMENT IS WHAT DECIDES IT. Its forbidden
    /// response says in so many words that the status is answered when the authenticated caller is not
    /// permitted the requested SUBJECT OR AUDIENCE, and the issuance type's own documentation classifies
    /// this outcome the same way. The distinction from the malformed arms above is exact and worth
    /// stating: an audience that is absent or blank violates the published SCHEMA and is a bad request,
    /// whereas an audience that is well formed but absent from the configured roster violates no schema
    /// at all - the schema does not enumerate audiences - and is a permission decision.
    /// </para>
    /// <para>
    /// The roster is not widened here and no default audience is substituted. The issuer made this
    /// decision against its own frozen copy of the configured roster before doing any cryptographic
    /// work, so the refusal costs no signature.
    /// </para>
    /// </remarks>
    private static ProblemHttpResult AudienceRefused(ILoggerFactory loggerFactory) =>
        ProblemResults.Create(
            retCode: RetCode.E_ACCESS_DENIED,
            detail: AudienceNotPermittedDetail,
            severity: ProblemSeverity.StopSign,
            loggerFactory: loggerFactory);

    /// <summary>
    /// Refuses an authenticated caller that is not authorized for the audience it asked for.
    /// </summary>
    /// <param name="loggerFactory">The logger factory the refusal is recorded through.</param>
    /// <returns>A forbidden response carrying the access-denied return code.</returns>
    /// <remarks>
    /// <para>
    /// THE SAME STATUS AND THE SAME SENTENCE AS <see cref="AudienceRefused"/>, DELIBERATELY - see
    /// <see cref="CallerNotPermittedDetail"/> for why the two responses are indistinguishable and where
    /// the distinction is kept instead. This method exists as a separate one so that the mapping from
    /// each outcome to its response is written once per outcome and a reader of the switch can see that
    /// every case is named.
    /// </para>
    /// <para>
    /// THE PUBLISHED DOCUMENT DECIDES THE STATUS, and its forbidden response covers an authenticated
    /// caller that is not permitted the requested subject or audience - which is exactly this. The
    /// decision costs no signature: the issuer consults its frozen matrix before it reads any signing
    /// material at all.
    /// </para>
    /// </remarks>
    private static ProblemHttpResult CallerRefused(ILoggerFactory loggerFactory) =>
        ProblemResults.Create(
            retCode: RetCode.E_ACCESS_DENIED,
            detail: CallerNotPermittedDetail,
            severity: ProblemSeverity.StopSign,
            loggerFactory: loggerFactory);

    /// <summary>
    /// Refuses a request none of whose requested scopes this caller may carry to that audience.
    /// </summary>
    /// <param name="loggerFactory">The logger factory the refusal is recorded through.</param>
    /// <returns>A forbidden response carrying the access-denied return code.</returns>
    /// <remarks>
    /// <para>
    /// FORBIDDEN RATHER THAN BAD REQUEST, on the same reading that decides the audience arm: a scope set
    /// that is well formed violates no published schema - the schema does not enumerate scopes - so its
    /// refusal is a permission decision rather than a malformed request. And forbidden rather than a
    /// narrowed success, because the published response's granted-scope member is REQUIRED: a 200 whose
    /// granted set was empty would either omit a required member or report an empty one, and neither is a
    /// response the document describes.
    /// </para>
    /// <para>
    /// THIS ARM IS REACHED ONLY WHEN NOTHING WAS PERMITTED. A partly permitted request is issued with the
    /// intersection, because the document states that the granted set may be narrower than the requested
    /// one and that a caller must read it rather than assume its request was honoured in full - so
    /// narrowing is the contract's own success path and refusing it here would contradict the document
    /// this service publishes.
    /// </para>
    /// </remarks>
    private static ProblemHttpResult ScopesRefused(ILoggerFactory loggerFactory) =>
        ProblemResults.Create(
            retCode: RetCode.E_ACCESS_DENIED,
            detail: ScopesNotPermittedDetail,
            severity: ProblemSeverity.StopSign,
            loggerFactory: loggerFactory);

    /// <summary>
    /// Reports an issuance outcome this build does not recognise.
    /// </summary>
    /// <param name="loggerFactory">The logger factory the fault is recorded through.</param>
    /// <returns>A server error carrying the internal-error return code.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="loggerFactory"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// The published server-error response names this code first. It is a fault of this service rather
    /// than of the caller, so the caller is not told to fix a request that was acceptable, and nothing
    /// is returned that could be mistaken for a credential.
    /// </para>
    /// <para>
    /// INTERNAL RATHER THAN PRIVATE, AND THAT IS A TESTABILITY DECISION WITH A REASON. The only
    /// production route to this refusal is an issuance outcome this build does not recognise, and the
    /// published outcome set is closed with two cases - so through the issuer alone the refusal is
    /// unreachable and would sit in the codebase permanently unexercised. Exposing it to the sibling test
    /// project is what makes it provable, and a provable refusal is the point of having one: a defence
    /// nobody has ever seen fire is a defence nobody knows works. Nothing about this widens the surface a
    /// caller can reach - it is not public, it holds no state, and it cannot produce a token. The
    /// cryptographic surface in this folder exposes its own rejection builders for the same reason.
    /// </para>
    /// </remarks>
    internal static ProblemHttpResult Faulted(ILoggerFactory loggerFactory) =>
        ProblemResults.Create(
            retCode: RetCode.E_INTERNAL_ERROR,
            detail: OutcomeUnrecognisedDetail,
            severity: ProblemSeverity.StopSign,
            loggerFactory: loggerFactory);

    /// <summary>
    /// Writes one structured record for a successful issuance.
    /// </summary>
    /// <param name="loggerFactory">The logger factory the record is written through.</param>
    /// <param name="subject">
    /// The subject, already reconciled ordinally against the identity the presented certificate
    /// established - so a transport-established identity rather than unvalidated caller text.
    /// </param>
    /// <param name="audience">
    /// The audience. By this point a value the issuer matched ordinally against its configured roster,
    /// so a configured identity rather than caller text.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="loggerFactory"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// STRUCTURED, AND REDACTED BY CONSTRUCTION. Each value is captured as a named field rather than
    /// interpolated into a sentence, so a value cannot rewrite the shape of the record that carries it.
    /// The token, the signing key, the key identifier, the granted scope set and the request headers are
    /// not merely omitted - this method has no parameter through which one could be passed.
    /// </para>
    /// <para>
    /// INFORMATION LEVEL, matching the issuer's own success record, because an issuance is a
    /// security-relevant event an operator legitimately wants in the ordinary log. The enablement check
    /// keeps the formatting cost off a disabled sink, which is the shape the sibling cryptographic
    /// surface already uses in this folder.
    /// </para>
    /// <para>
    /// COMPLEMENTARY TO THE ISSUER'S RECORD RATHER THAN A DUPLICATE OF IT. That one carries the key
    /// identifier, the issuer identity, the audience and the expiry; this one carries the reconciled
    /// subject, which that layer deliberately refuses to record because at ITS layer the subject is
    /// still unvalidated caller text. Between the two, an operator can attribute a token to a caller
    /// without either file having logged anything unvalidated.
    /// </para>
    /// </remarks>
    private static void LogIssued(ILoggerFactory loggerFactory, string subject, string audience)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);

        ILogger logger = loggerFactory.CreateLogger(LoggerCategoryName);

        if (!logger.IsEnabled(LogLevel.Information))
        {
            return;
        }

        logger.LogInformation(IssuedLogTemplate, OperationName, subject, audience);
    }

    /// <summary>
    /// Declares the mutual-TLS security requirement on this operation in the generated OpenAPI
    /// document, registering the scheme itself when the document does not already carry it.
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
    /// A per-endpoint operation transformer is the sanctioned mechanism for this on this toolchain. The
    /// older per-endpoint OpenAPI configuration extension is deprecated here and raises a deprecation
    /// diagnostic, which the repository-wide warnings-as-errors setting turns into a build failure, so
    /// it is not an option even as a fallback.
    /// </para>
    /// <para>
    /// THE REQUIREMENT IS REPLACED RATHER THAN APPENDED, AND THAT IS THE WHOLE POINT OF THIS METHOD.
    /// The authored contract applies the two credential schemes to this operation as an OVERRIDE of the
    /// document-level bearer requirement; a document that also listed the bearer scheme would tell a
    /// consumer it may present a token instead of a credential, which is the one thing this operation
    /// cannot accept.
    /// The two sibling anonymous routes clear their requirement for the mirror-image reason, and the
    /// bearer-authenticated routes add theirs only when the operation carries none.
    /// </para>
    /// <para>
    /// The generator does not synthesise a security requirement from authorization metadata by itself,
    /// so requiring a credential without declaring it would leave the published document claiming an
    /// anonymous mint - which is the single most misleading thing this document could say.
    /// </para>
    /// <para>
    /// THE SCHEME REGISTRATION IS IDEMPOTENT, so this composes with whatever document-wide security the
    /// host registers rather than fighting it. NO KEY MATERIAL, TRUST ANCHOR, CERTIFICATE PATH OR CALLER
    /// IDENTITY APPEARS IN THE DECLARATION: it states the transport mechanism, which is all a consumer
    /// needs in order to present the certificate its own deployment gave it.
    /// </para>
    /// </remarks>
    private static Task DeclareIssuanceCredentialRequirementAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(context);

        cancellationToken.ThrowIfCancellationRequested();

        OpenApiDocument? document = context.Document;

        if (document is not null &&
            document.Components?.SecuritySchemes?.ContainsKey(ClientCredentialSchemeName) != true)
        {
            IOpenApiSecurityScheme clientCredentialScheme = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = BasicSchemeToken.ToLowerInvariant(),
                Description = ClientCredentialSchemeDescription,
            };

            document.AddComponent(ClientCredentialSchemeName, clientCredentialScheme);
        }

        if (document is not null &&
            document.Components?.SecuritySchemes?.ContainsKey(MutualTlsSchemeName) != true)
        {
            IOpenApiSecurityScheme mutualTlsScheme = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.MutualTLS,
                Description = MutualTlsSchemeDescription,
            };

            document.AddComponent(MutualTlsSchemeName, mutualTlsScheme);
        }

        // TWO SINGLE-SCHEME REQUIREMENTS, NOT ONE REQUIREMENT NAMING TWO SCHEMES, and the difference is
        // the whole meaning. A list of requirements is a DISJUNCTION - any one satisfies the operation -
        // whereas two schemes inside one requirement is a CONJUNCTION demanding both at once, which
        // would publish an operation no caller in this system can reach. The handler accepts either
        // credential, so the document says either.
        operation.Security = new List<OpenApiSecurityRequirement>
        {
            new()
            {
                [new OpenApiSecuritySchemeReference(ClientCredentialSchemeName, document)] =
                    new List<string>(),
            },
            new()
            {
                [new OpenApiSecuritySchemeReference(MutualTlsSchemeName, document)] =
                    new List<string>(),
            },
        };

        return Task.CompletedTask;
    }
}

/// <summary>
/// The three things, and the only three things, a token request carries: a claimed caller identity, one
/// intended audience, and the requested scope set.
/// </summary>
/// <remarks>
/// <para>
/// MEMBER FOR MEMBER THE PUBLISHED SCHEMA, AND NOTHING MORE. The authored contract closes this object
/// with <c>additionalProperties: false</c> and declares exactly these three members, so a fourth
/// declared here would publish a schema the contract does not have.
/// </para>
/// <para>
/// IT CARRIES NO CREDENTIAL, AND THERE IS NOWHERE TO PUT ONE. No client secret, no password, no API
/// key, no assertion, no passphrase and no key material of any kind - caller identity is established by
/// the TRANSPORT, from the mutual-TLS client certificate the operation requires. The
/// <see cref="Subject"/> member is a CLAIM, and the identity actually honoured is the one the presented
/// certificate establishes.
/// </para>
/// <para>
/// EVERY MEMBER IS NULLABLE, WHICH IS A CONTRACT-FIDELITY DECISION RATHER THAN LAXITY. The published
/// bad-request response states that it carries the legacy return code, and a member the framework
/// refuses during binding is answered with a status and NO problem document at all - so a required
/// member modelled as non-nullable would make that declared response unreachable. Modelling absence as
/// an observable state is what lets the handler answer it with the contract's own error shape. The
/// service's cryptographic surface models its request records the same way for the same reason. The
/// single cost is that the GENERATED request schema is one degree more permissive than the authored
/// one; the authored document remains authoritative for what a caller may send, and the observable
/// behaviour on a violation is identical.
/// </para>
/// <para>
/// NO MEMBER CARRIES A DEFAULT VALUE. Defaulting one would let an issuer supply something the caller
/// never asked for - an audience above all, where a default would mint a token valid somewhere its
/// holder did not intend.
/// </para>
/// <para>
/// DECLARED IN THIS FILE, next to the operation that binds it, because the folder admits exactly five
/// files and a separate model folder would be a sixth. That is the same rule that puts the issuance
/// request and result shapes inside the issuer's own file.
/// </para>
/// </remarks>
public sealed record TokenIssuanceRequestBody
{
    /// <summary>
    /// The identity the caller is requesting a token for. A claim, not a credential.
    /// </summary>
    /// <value>
    /// A non-blank identity, or <see langword="null"/> when the member was absent - which the operation
    /// answers as a bad request.
    /// </value>
    /// <remarks>
    /// Checked against the identity the presented client certificate establishes, and a disagreement is
    /// refused rather than honoured. It becomes the token's subject claim VERBATIM: nothing is trimmed,
    /// case-folded or otherwise repaired on the way through.
    /// </remarks>
    [Required]
    [JsonPropertyName("subject")]
    public string? Subject { get; init; }

    /// <summary>The single intended audience for the token.</summary>
    /// <value>
    /// A non-blank audience identity, or <see langword="null"/> when the member was absent.
    /// </value>
    /// <remarks>
    /// ONE AUDIENCE PER REQUEST, which is the published contract's own shape rather than a
    /// simplification: a caller needing tokens for two audiences asks twice, so that a token is never
    /// valid somewhere its holder did not intend it to be. It is compared against the configured roster
    /// exactly as given, and an audience that is not on it is refused rather than substituted.
    /// </remarks>
    [Required]
    [JsonPropertyName("audience")]
    public string? Audience { get; init; }

    /// <summary>The requested scope set, in the caller's own order.</summary>
    /// <value>
    /// A non-empty set of non-blank, white-space-free, unique scopes, or <see langword="null"/> when the
    /// member was absent.
    /// </value>
    /// <remarks>
    /// <para>
    /// THE GRANTED SET RETURNED IN THE RESPONSE MAY BE NARROWER, and a narrowing is a SUCCESSFUL outcome
    /// rather than an error, so a caller must read the granted value from the response instead of
    /// assuming this request was honoured in full.
    /// </para>
    /// <para>
    /// A read-only list rather than an array, so the bound value cannot be mutated through this
    /// property after the operation has validated it. The order is preserved, so the granted value a
    /// caller reads back is recognisably its own request.
    /// </para>
    /// </remarks>
    [Required]
    [JsonPropertyName("scopes")]
    public IReadOnlyList<string>? Scopes { get; init; }
}

/// <summary>
/// The issued token and its metadata.
/// </summary>
/// <remarks>
/// <para>
/// THE MEMBER SPELLINGS ARE RFC 6749 SECTION 5.1's, DELIBERATELY, and the asymmetry with the request
/// half is recorded rather than accidental (constraint C-K). A stock client library parses THIS object
/// shape without bespoke code, which is the same property that made this contract REST rather than a
/// binary protocol; the REQUEST half does not follow that RFC, because it is not a grant request -
/// there is no grant-type parameter, no authorization endpoint and no token-endpoint client
/// authentication anywhere in this contract.
/// </para>
/// <para>
/// NO REFRESH CREDENTIAL EXISTS, HERE OR ANYWHERE IN THIS CONTRACT. The contract issues a short-lived
/// service token, and a caller that needs another one calls the operation again over the same mutually
/// authenticated channel. Adding refresh semantics would be a new feature, and it would create exactly
/// the long-lived credential the sole-issuer topology deliberately does without (constraint C-B).
/// </para>
/// <para>
/// MEMBER FOR MEMBER THE PUBLISHED SCHEMA, AND NOTHING MORE. That schema closes the object against
/// undeclared members and declares no key identifier, so none is projected here even though the issuer
/// reports one: the identifier is public metadata available where a verifier actually needs it, stamped
/// in the token's own header and published in the key set. Four members are required by the schema and
/// are declared <c>required</c> so the generated document says so too; the issuance instant is optional
/// there and is therefore NOT declared required here, while still always being emitted, because the
/// issuer always has an exact value for it.
/// </para>
/// <para>
/// NO MEMBER CARRIES A DEFAULT, AND NO SPECIMEN VALUE APPEARS ANYWHERE - not on a property, not in an
/// example and not in this documentation (constraint C-F). The published specification carries no
/// example on any field for the same reason: a plausible-looking value is indistinguishable from real
/// material to a reader, and specimens have a long history of being copied into production unchanged.
/// </para>
/// </remarks>
public sealed record TokenIssuanceResponse
{
    /// <summary>
    /// The signed token, for presentation as a bearer credential. THE CREDENTIAL.
    /// </summary>
    /// <value>A signed token in three dot-separated segments.</value>
    /// <remarks>
    /// Recipients verify it against this service's published key set. NEVER LOGGED, never echoed by any
    /// other operation in this document, never carried in an error body and never rendered by a
    /// diagnostic: this response is its only legitimate destination.
    /// </remarks>
    [JsonPropertyName("access_token")]
    public required string AccessToken { get; init; }

    /// <summary>The credential type, always the fixed protocol constant.</summary>
    /// <value>The one credential type this service issues.</value>
    /// <remarks>
    /// The published schema fixes this to a single value rather than enumerating alternatives, because
    /// no other credential type is issued. It is written from a private constant in the operation's own
    /// file, so the response cannot carry a second spelling of it.
    /// </remarks>
    [JsonPropertyName("token_type")]
    public required string TokenType { get; init; }

    /// <summary>The token lifetime in seconds from issuance.</summary>
    /// <value>At least one second.</value>
    /// <remarks>
    /// EXACTLY the difference between the token's expiry and issuance claims, because both are derived
    /// by the issuer from one already-truncated instant and this same whole-second lifetime - so a test
    /// can assert the identity rather than a tolerance. Tokens are short-lived by contract; a caller
    /// must be prepared to obtain another rather than caching one indefinitely.
    /// </remarks>
    [JsonPropertyName("expires_in")]
    public required long ExpiresIn { get; init; }

    /// <summary>The GRANTED scope set as a space-delimited list.</summary>
    /// <value>The granted scopes, delimited by single spaces. An empty string is legal.</value>
    /// <remarks>
    /// BYTE-IDENTICAL TO THE TOKEN'S SCOPE CLAIM, because the claim and this member are the same string.
    /// It MAY BE NARROWER than the requested set, and a caller must read this value: treating the
    /// request as authoritative is the mistake this member exists to prevent. An empty string means no
    /// requested scope was granted.
    /// </remarks>
    [JsonPropertyName("scope")]
    public required string Scope { get; init; }

    /// <summary>
    /// The issuance time as seconds since the Unix epoch, matching the token's own issuance claim.
    /// </summary>
    /// <value>A whole number of seconds since the epoch.</value>
    /// <remarks>
    /// <para>
    /// Present so a caller can compute an expiry without parsing the token. Optional in the published
    /// schema, which is why it is NOT declared <c>required</c> - a required declaration here would
    /// publish a stricter schema than the contract has - but it is always emitted, because the issuer
    /// always computes an exact value for it.
    /// </para>
    /// <para>
    /// Converted from the issuer's instant rather than measured here. The issuer truncates its single
    /// clock reading to a whole second before deriving anything from it, so this value and the token's
    /// claim agree exactly and a fixed clock produces a byte-identical body across runs.
    /// </para>
    /// </remarks>
    [JsonPropertyName("issued_at")]
    public long IssuedAt { get; init; }
}
