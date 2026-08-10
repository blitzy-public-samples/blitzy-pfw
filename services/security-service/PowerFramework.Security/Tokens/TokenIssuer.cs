// ==================================================================================================
//  TokenIssuer - the ONE component in this system that mints a token
//  ------------------------------------------------------------------------------------------------
//  THIS FILE IS NET-NEW .NET BEHAVIOUR, NOT A PORT. The legacy PowerBuilder estate contains no token
//  issuance behaviour whatsoever. It is a library with no process of its own: it opens no listening
//  socket, registers no route, receives no unsolicited request, and therefore has nothing to
//  authenticate against and nothing to issue a credential for. A repository-wide search for the
//  token vocabulary finds it in three text files, and not one of them mints anything - one merely
//  CONSUMES a caller-supplied token in a capability area outside this phase, one is a vendored
//  third-party script, and one is the anti-pattern named below. The discovery-metadata path appears
//  in the legacy zero times.
//
//  Consequently every legacy locator cited in this file is EVIDENCE AND PROVENANCE ONLY. Each one
//  justifies a decision; none of them is translated, and none is read, opened, embedded, copied or
//  linked at build time or at run time.
//
//  LEGACY PROVENANCE, EVERY PATH READ ONLY - the behavioural oracle for parity testing, never an
//  edit target:
//
//      ws_objects/pfw.shared.pbl.src/enums.sru:L927        the hash-type block comment, which in the
//                                                          oracle's OWN words names that one set as
//                                                          the argument of the hashing, the SIGNING
//                                                          and the VERIFYING primitives alike
//      ws_objects/pfw.shared.pbl.src/enums.sru:L930        SHA-256, the digest this issuer's default
//                                                          algorithm identifier names
//      ws_objects/pfw.shared.pbl.src/enums.sru:L949-L951   the entire RSA padding set: block padding
//                                                          (the default) and OAEP. There is NO
//                                                          probabilistic member and NO no-padding
//                                                          member, so the legacy cannot express the
//                                                          scheme the anti-pattern's header names
//      ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L8       the whole cryptographic surface is a
//                                                          closed native binary with no PowerScript
//                                                          body, so there is no algorithm to read
//                                                          out of an implementation - only the
//                                                          declared signature
//      ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L70-L73  the signing and verifying primitives.
//                                                          Both take a HASH TYPE and NO padding
//                                                          argument, and both take the key as a
//                                                          caller-supplied parameter
//      ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L75      a global auto-instance shadowing its own
//                                                          type name - the collision this file
//                                                          answers with constructor injection
//      ws_objects/pfw.pbl.src/pfw.sra:L111-L144            the fail-fast posture, terminating the
//                                                          process at :L143 with no recovery arm
//      tests/blink/test_jws.htm:L8-L23                     THE ANTI-PATTERN THIS FILE REPLACES
//
//  ==================================================================================================
//  THE ANTI-PATTERN THIS FILE EXISTS TO MAKE STRUCTURALLY IMPOSSIBLE
//  ==================================================================================================
//  tests/blink/test_jws.htm:L8-L23 signs a token with a private key WRITTEN INTO THE SOURCE. Its
//  shape is described here because the shape is the lesson; its values are not reproduced anywhere.
//  A script variable is opened with an armour delimiter, a dozen or so lines of encoded private-key
//  body are concatenated onto it, a matching closing delimiter ends it, and the resulting in-source
//  key is handed straight to a third-party signer together with a hardcoded header and a hardcoded
//  payload. That payload carries four hardcoded members, and each one is a lesson of its own:
//
//      * A hardcoded subject. Here the subject is the caller identity carried on the request, and
//        the identity actually honoured is the one the transport establishes.
//      * A hardcoded expiry - an absolute instant that fell into the past years ago, so that page
//        cannot mint a usable token at all today. Here the expiry is a CONFIGURED LIFETIME plus an
//        INJECTED CLOCK, which is precisely the pairing that makes an expiry unable to go stale.
//      * A hardcoded digest value and a hardcoded credential value. Neither has any counterpart
//        here: this issuer mints one thing, a signed token, and nothing else.
//
//  NO VALUE, FRAGMENT, ENCODED RUN, SUBJECT, EXPIRY, DIGEST OR TOKEN FROM THAT FILE APPEARS IN THIS
//  ONE, in any form - not transformed, not reversed, not split, not re-encoded, not disguised - and
//  none may ever be added, here or in a test, a fixture, a settings file or a documentation example.
//  .dockerignore excludes that whole directory from every image layer as an explicit secrets
//  control, which is independent corroboration that the material must not re-enter the .NET tree.
//
//  Its header additionally names a PROBABILISTIC signature scheme. THAT IS THE THIRD-PARTY SCRIPT'S
//  CHOICE AND NOT A CONTRACT, and the evidence is first-hand rather than inferred: the oracle's
//  padding catalogue has exactly two members and neither is that scheme [enums.sru:L949-L951], and
//  its signing primitive takes no padding argument at all [n_crypto.sru:L70-L73], so the legacy
//  cannot express it. The sibling asymmetric provider reaches the same conclusion independently and
//  refuses it. This issuer therefore does not sign with it either.
//
//  ==================================================================================================
//  WHY THE SIGNATURE IS ASYMMETRIC, AND WHY THAT IS STRUCTURAL RATHER THAN A PREFERENCE
//  ==================================================================================================
//  Tokens are signed with the RSA family of block-padded signatures over the SHA-2 digests, whose
//  default identifier is the SHA-256 member of that family. A shared-secret signature is not a
//  weaker option here; it is an IMPOSSIBLE one, and the reason has nothing to do with algorithm
//  strength:
//
//      * The verification key set is published ANONYMOUSLY. It has to be - a consumer fetches it in
//        order to learn how to validate, so it cannot already hold a token to present when it asks.
//      * The other three services hold VERIFICATION MATERIAL ONLY and are explicitly NOT independent
//        signing authorities. Exactly one signing key exists in the whole system and this service
//        holds it.
//      * A shared-secret entry in a published key set would publish THE SIGNING KEY ITSELF, and
//        handing that key to three verifiers would make every one of them a CO-SIGNER. The
//        sole-issuer topology that is this service's entire reason for existing would be gone in one
//        anonymous request.
//
//  With an asymmetric key the split is structural instead of procedural: the private half never
//  leaves this process, and the public half is the only thing that ever does.
//
//  THE DIGEST AGREES WITH THE LEGACY SIGNING PRIMITIVE RATHER THAN BEING CHOSEN FRESHLY.
//  enums.sru:L927 records, in the oracle's own source comment, that ONE hash-type set parameterises
//  the hashing, the signing and the verifying primitives; SHA-256 is a member of that set at :L930.
//  The signature scheme is fixed inside the closed binary, because :L70-L73 declare a hash type and
//  no padding argument, and the sibling asymmetric provider adopts block padding for exactly that
//  reason. RSA plus SHA-256 plus block padding is therefore what the legacy primitive ALREADY
//  expresses - this issuer restates a legacy capability rather than introducing one.
//
//  THE PERMITTED SET IS THE THREE-MEMBER CLOSED SET THE OPTIONS CONTRACT PUBLISHES, not the default
//  alone, and the reason is agreement rather than permissiveness. The options validator admits three
//  identifiers and the signing-key provider accepts the same three. Narrowing the set here would let
//  a value pass validation, be accepted by the provider, and then be refused by the very type that
//  consumes the provider - three files in one service disagreeing about their own contract. Every
//  other value is refused: the shared-secret family for the structural reason above, the
//  probabilistic family for want of any legacy identifier, the elliptic-curve family because the
//  legacy cryptographic class declares no such primitive, and an unsigned token because an issuer
//  that does not sign is not an issuer.
//
//  ==================================================================================================
//  WHY THE MINTING LIBRARY IS REFERENCED BY THIS SERVICE AND BY NO OTHER
//  ==================================================================================================
//  Microsoft.IdentityModel.JsonWebTokens is the token-MINTING library, and its package reference
//  appears in exactly ONE project file in the whole repository: this service's. That is not
//  tidiness - it is the sole-issuer topology expressed in the build graph, where a reviewer can
//  check it by reading project files rather than by auditing code. The other three services
//  reference only the inbound bearer handler, so they are structurally incapable of minting: the
//  type that would do it is not on their compile path at all.
//
//  THIS FILE IS THE ONLY PLACE THAT CALLS IT. Its handler and its token descriptor appear here and
//  nowhere else in the service, so "who can mint" has exactly one answer at the project level and
//  exactly one answer at the file level.
//
//  ==================================================================================================
//  FAIL FAST, NEVER GRACEFUL DEGRADATION
//  ==================================================================================================
//  The legacy's answer to a structural fault is to end the process: its application object unpacks a
//  seven-field assert payload and then halts [ws_objects/pfw.pbl.src/pfw.sra:L111-L144, terminating
//  at :L143]. Read that event looking for a recovery arm and there is none - no retry, no default,
//  no continue, no downgrade to a warning.
//
//  The .NET equivalent at startup is a host that refuses to start, so every structural fault below
//  is raised from the CONSTRUCTOR. The options contract carries a validator with no fallback and the
//  service entry point validates on start, so this file's posture is the third link in a chain that
//  already fails closed rather than a new opinion.
//
//  FOUR SOFTENINGS ARE FORBIDDEN, each of which would be a severe defect dressed as robustness:
//
//      * Downgrading to a shared-secret signature when the supplied credential is not asymmetric.
//        It would collapse the sole-issuer topology (see above). A non-asymmetric credential is
//        refused loudly.
//      * Minting with an unverified algorithm, or falling back to a default algorithm when the
//        configured one is not recognised.
//      * Defaulting the lifetime when the configured one is unusable. A token whose expiry is
//        invented by the issuer is a token nobody configured.
//      * Minting for an audience that is not on the configured roster. There is no default audience
//        and no inference: an unlisted audience is refused, and the refusal is a reported outcome
//        rather than a silent substitution.
//
//  ==================================================================================================
//  WHAT THIS FILE DELIBERATELY DOES NOT DO
//  ==================================================================================================
//  The contract is ISSUANCE, and nothing else. None of the following exists here, behind a flag, in
//  a comment, or as a stub - each would be a new capability, which this refactor does not add:
//
//      * NO refresh token and no refresh grant of any kind. The contract issues a short-lived token
//        and a caller that needs another one asks again over the same authenticated channel.
//      * NO revocation, revocation list or identifier blacklist, and consequently NO token
//        identifier claim at all - which also keeps issuance perfectly deterministic under a fixed
//        clock, since a random identifier is exactly the kind of value a paired recording cannot
//        compare.
//      * NO introspection operation.
//      * NO rotation machinery, key ring, second key or key generation. A key identifier exists so
//        that rotation is EXPRESSIBLE, not so that rotation is IMPLEMENTED - and no key material
//        reaches this file in any case.
//      * NO caching, reuse, deduplication, sliding expiry or renewable expiry. Every call mints.
//      * NO audience discovery, scope inference, scope expansion or default-audience fallback.
//      * NO inbound validation. Validating a token is the stock bearer handler's work, configured at
//        the service entry point; a hand-rolled validator here would move the security-critical path
//        out of framework code, which is the opposite of the intent.
//      * NO encryption of the token, no nested token and no certificate header member.
//      * NO rate limit, quota or replay guard.
//      * NO clock skew, leeway or grace window. Skew tolerance is each verifier's concern, and
//        expressing it on both sides invites the two to be tuned against each other.
//      * NO OUTBOUND EDGE. There is no HTTP client, no remote-procedure client and no reference to
//        Gateway, DataServices or Persistence. Their identities appear here only as audience VALUES
//        read from configuration. This service is reached; it does not reach out.
//      * NO USER INTERFACE CONCERN OF ANY KIND. This phase is API and service level only: no
//        component library, no design tokens, no theming and no styling are in scope anywhere in the
//        refactor, and none appears here.
//
//  RULES POSITION. The project's rules document contains exactly one line, stating that no user
//  rules were provided, so NO user-specified rule governs this file. Nothing is invented or
//  back-filled from convention in their place; the bar applied instead is the enterprise-standard
//  baseline the migration plan states - nullable reference types with warnings as errors, no secret
//  in source or settings or any container definition, structured logging that carries no credential,
//  constructor injection throughout so the sibling test project can reach every branch, and the
//  published contract as the only cross-service coupling.
// ==================================================================================================

using System.Collections.Frozen;
using System.Collections.Immutable;

using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

using PowerFramework.Security.Configuration;
using PowerFramework.Shared.Kernel;

namespace PowerFramework.Security.Tokens;

/// <summary>
/// What an issuance attempt produced: a token, or the one refusal this issuer can report.
/// </summary>
/// <remarks>
/// <para>
/// TWO MEMBERS, DELIBERATELY. The published contract gives issuance exactly two outcomes this type
/// is responsible for distinguishing - a token was minted, or the requested audience is not one this
/// issuer serves. A malformed request never reaches an outcome at all, because
/// <see cref="TokenIssuanceRequest"/> refuses to exist in a malformed state, and caller
/// authentication is settled by the transport before this type is reached.
/// </para>
/// <para>
/// This enumeration exists so the endpoint can select a status code without inspecting a message or
/// catching an exception. The refusal is an EXPECTED outcome of a well-formed request, not a fault,
/// and the published contract classifies it as such: the issuance operation answers "forbidden" when
/// the caller is not permitted the requested subject or audience
/// [<c>shared/PowerFramework.Contracts/OpenApi/security.v1.yaml</c>, operation
/// <c>issueToken</c>].
/// </para>
/// </remarks>
public enum TokenIssuanceOutcome
{
    /// <summary>
    /// A token was minted. <see cref="TokenIssuanceResult.Token"/> carries it.
    /// </summary>
    Issued,

    /// <summary>
    /// The requested audience is not on the configured roster, so nothing was minted.
    /// </summary>
    /// <remarks>
    /// No token was created, no signature was computed and
    /// <see cref="TokenIssuanceResult.Token"/> is <see langword="null"/>. There is no partial
    /// outcome: an unlisted audience is refused before any minting work begins.
    /// </remarks>
    AudienceNotPermitted,
}

/// <summary>
/// The three things, and the only three things, an issuance request carries: a caller identity, one
/// intended audience, and the requested scope set.
/// </summary>
/// <remarks>
/// <para>
/// DECLARED IN THIS FILE ON PURPOSE. Its folder admits exactly two files - the signing-key provider
/// and this one - so a request shape that lives anywhere else would be a third file. The precedent is
/// the entropy seam, which its own provider declares inside itself for the same reason.
/// </para>
/// <para>
/// IT CANNOT EXIST IN A MALFORMED STATE. Every constraint the published request schema states is
/// enforced by the constructor, so an instance is proof that the request was well formed and
/// <see cref="TokenIssuer"/> has no malformed case to handle. That split matches the contract's own
/// status codes exactly: a malformed request is a client error the endpoint reports as such, whereas
/// an unlisted audience is a permission decision that needs the configured roster and is therefore
/// made by the issuer.
/// </para>
/// <para>
/// IT CARRIES NO CREDENTIAL, AND THERE IS NOWHERE TO PUT ONE. The published request schema declares
/// no client secret, no password, no key reference, no assertion and no key material of any kind, and
/// forbids undeclared members outright; caller identity is established by the TRANSPORT. This type
/// mirrors that exactly - three members, none of which is a credential. The subject is a CLAIM the
/// caller makes, and reconciling it with the identity the transport established belongs to the
/// endpoint, which is the only layer that can see the connection.
/// </para>
/// <para>
/// IT IS A PLAIN SEALED CLASS RATHER THAN A RECORD. A record would generate a string rendering that
/// prints every member, and although none of these three is a credential, a value-printing rendering
/// on a request type is a habit worth not forming in a file that also handles a token. The default
/// rendering of a class names the type and nothing else.
/// </para>
/// </remarks>
public sealed class TokenIssuanceRequest
{
    /// <summary>The fixed message reported when the requested scope set is empty.</summary>
    private const string ScopeSetEmptyMessage =
        "A token request must ask for at least one scope. The published request schema requires a " +
        "non-empty scope array, and an issuer that granted an empty set would mint a token that " +
        "authorises nothing.";

    /// <summary>The fixed message reported when a requested scope is empty.</summary>
    private const string ScopeBlankMessage =
        "A requested scope is empty. The published request schema requires every scope to carry at " +
        "least one character.";

    /// <summary>The fixed message reported when a requested scope carries white space.</summary>
    private const string ScopeSpacedMessage =
        "A requested scope contains white space. The granted set is carried as a single " +
        "space-delimited value in both the response and the token claim, so a scope containing " +
        "white space could not be recovered by the reader and would silently become two scopes.";

    /// <summary>The fixed message reported when the requested scope set repeats a scope.</summary>
    private const string ScopeDuplicateMessage =
        "The requested scope set repeats a scope. The published request schema requires the scope " +
        "array to hold unique items.";

    /// <summary>
    /// Builds a well-formed issuance request, or refuses to exist.
    /// </summary>
    /// <param name="subject">
    /// The identity the caller is requesting a token for, which becomes the subject claim VERBATIM.
    /// A claim rather than a credential.
    /// </param>
    /// <param name="audience">
    /// The single intended audience, checked against the configured roster by
    /// <see cref="TokenIssuer"/> and never inferred.
    /// </param>
    /// <param name="scopes">
    /// The requested scope set. Enumerated exactly once and copied, so a caller cannot mutate the
    /// request after constructing it.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="subject"/>, <paramref name="audience"/> or <paramref name="scopes"/> is
    /// <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="subject"/> or <paramref name="audience"/> is empty or white space only; or the
    /// scope set is empty, carries an empty scope, carries a scope containing white space, or repeats
    /// a scope. Each case reports which constraint was broken and never reproduces the offending
    /// value, so a message cannot become an echo of caller-supplied text.
    /// </exception>
    /// <remarks>
    /// <para>
    /// EVERY CHECK RESTATES A PUBLISHED CONSTRAINT, and none of them invents one. The subject and the
    /// audience must carry at least one character; the scope set must be non-empty, must hold unique
    /// items, and every scope must carry at least one character.
    /// </para>
    /// <para>
    /// THE WHITE-SPACE CHECK IS THE ONE THAT NEEDS EXPLAINING, because it is not written in the
    /// schema in those words - it is what the schema's own encoding requires. The granted set travels
    /// as ONE space-delimited value, in the response and in the token claim alike, so that encoding
    /// is lossless only while no scope contains white space. Rejecting one is enforcing the published
    /// encoding rather than adding a rule to it: accepting it would produce a token whose scope claim
    /// says something the caller did not request, which is worse than a refusal by a wide margin.
    /// White space is rejected in every form, not merely the delimiter, because a tab or a line break
    /// inside a claim value is a hazard in its own right.
    /// </para>
    /// <para>
    /// NOTHING IS NORMALISED. No value is trimmed, case-folded, sorted, de-duplicated or reordered.
    /// The subject reaches the claim exactly as given, the audience is compared to the roster exactly
    /// as given, and the scope set keeps the caller's own order so that the granted value a caller
    /// reads back is recognisably its own request. Silently repairing a request would make the
    /// difference between what was asked for and what was granted invisible, and the contract exists
    /// to keep that difference visible.
    /// </para>
    /// </remarks>
    public TokenIssuanceRequest(string subject, string audience, IEnumerable<string> scopes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(audience);
        ArgumentNullException.ThrowIfNull(scopes);

        // Enumerated ONCE into an immutable copy before any element is inspected, so a sequence that
        // yields different elements on a second pass cannot be validated in one shape and stored in
        // another.
        ImmutableArray<string> requested = [.. scopes];

        if (requested.IsEmpty)
        {
            throw new ArgumentException(ScopeSetEmptyMessage, nameof(scopes));
        }

        HashSet<string> seen = new(StringComparer.Ordinal);

        foreach (string scope in requested)
        {
            if (string.IsNullOrEmpty(scope))
            {
                throw new ArgumentException(ScopeBlankMessage, nameof(scopes));
            }

            if (scope.Any(char.IsWhiteSpace))
            {
                throw new ArgumentException(ScopeSpacedMessage, nameof(scopes));
            }

            // Ordinal, because a scope is an opaque protocol token: two spellings that differ only by
            // case are two different scopes, and folding them together would grant one the caller
            // never asked for.
            if (!seen.Add(scope))
            {
                throw new ArgumentException(ScopeDuplicateMessage, nameof(scopes));
            }
        }

        Subject = subject;
        Audience = audience;
        Scopes = requested;
    }

    /// <summary>
    /// The identity the caller is requesting a token for. Becomes the subject claim verbatim.
    /// </summary>
    /// <value>A non-blank identity.</value>
    /// <remarks>
    /// A CLAIM, NOT A CREDENTIAL. It is never logged by this file - the permitted log fields are the
    /// key identifier, the issuer, the audience and the expiry, and the subject is not among them -
    /// so a caller-supplied identity cannot reach a log record through this path at all.
    /// </remarks>
    public string Subject { get; }

    /// <summary>
    /// The single intended audience for the token.
    /// </summary>
    /// <value>A non-blank audience identity.</value>
    /// <remarks>
    /// ONE AUDIENCE PER REQUEST, which is the published contract's own shape rather than a
    /// simplification: a caller needing tokens for two audiences asks twice, so that a token is never
    /// valid somewhere its holder did not intend it to be.
    /// </remarks>
    public string Audience { get; }

    /// <summary>
    /// The requested scope set, in the caller's own order.
    /// </summary>
    /// <value>A non-empty set of non-blank, white-space-free, unique scopes.</value>
    /// <remarks>
    /// Immutable, so the set validated by the constructor is the set the issuer reads. The set
    /// actually GRANTED is reported on the result, and a caller must read it from there rather than
    /// assuming this request was honoured in full.
    /// </remarks>
    public ImmutableArray<string> Scopes { get; }
}


/// <summary>
/// One minted token and the metadata the published response needs, so the endpoint can answer without
/// recomputing an instant or parsing the token it just received.
/// </summary>
/// <remarks>
/// <para>
/// THIS IS THE ONE TYPE IN THE SERVICE THAT CARRIES A CREDENTIAL, so it is shaped to make leaking one
/// difficult rather than merely discouraged:
/// </para>
/// <list type="bullet">
/// <item>
/// It is a plain sealed class and DELIBERATELY NOT A RECORD. A record generates a string rendering
/// that prints every member, which on this type would print the token itself into whatever consumed
/// the rendering - a log line, an exception message, a debugger transcript, an assertion failure.
/// </item>
/// <item>
/// <see cref="ToString"/> IS OVERRIDDEN so that even the default naming behaviour cannot be relied on
/// by accident. It renders the key identifier and the expiry, both of which are public metadata, and
/// it renders nothing else - no token, no subject, no scope.
/// </item>
/// <item>
/// It declares no serialization helper, no debugger display and no value equality, so no generated or
/// framework-supplied member can print or compare the token.
/// </item>
/// </list>
/// <para>
/// EVERY MEMBER MAPS ONTO THE PUBLISHED RESPONSE, and none is decorative. The token is the credential
/// itself; the lifetime in seconds is the response's expiry member, whose schema requires it to be at
/// least one, which is why the issuer refuses to construct when the configured lifetime rounds down
/// below a second; the issuance instant is the response's optional issued-at member and is the same
/// value as the token's own issuance claim; and the granted scope value is BYTE-IDENTICAL to the
/// token's scope claim, so a caller reading either sees the same thing. The key identifier is not a
/// response member at all - it is here so that a log record and a test can name the key that signed
/// this token without opening it.
/// </para>
/// <para>
/// The response's credential-type member is not represented here. It is a fixed protocol constant of
/// the response shape, so it belongs to the layer that builds the response; this type carries only
/// what had to be computed to mint.
/// </para>
/// </remarks>
public sealed class IssuedToken
{
    /// <summary>
    /// Captures a minted token and its metadata.
    /// </summary>
    /// <param name="accessToken">The compact serialization.</param>
    /// <param name="keyId">The identifier of the key that signed it.</param>
    /// <param name="issuedAt">The issuance instant, already truncated to a whole second.</param>
    /// <param name="expiresAt">The expiry instant, already truncated to a whole second.</param>
    /// <param name="expiresInSeconds">The lifetime in whole seconds.</param>
    /// <param name="grantedScope">The granted scope set as one space-delimited value.</param>
    /// <remarks>
    /// INTERNAL, so that only the issuer in this file can create one. A token-bearing type that
    /// anyone could construct would invite a fabricated instance to be passed where a minted one is
    /// expected. The sibling test project sees it through the project's declared test visibility, so
    /// the restriction costs no coverage.
    /// </remarks>
    internal IssuedToken(
        string accessToken,
        string keyId,
        DateTimeOffset issuedAt,
        DateTimeOffset expiresAt,
        long expiresInSeconds,
        string grantedScope)
    {
        AccessToken = accessToken;
        KeyId = keyId;
        IssuedAt = issuedAt;
        ExpiresAt = expiresAt;
        ExpiresInSeconds = expiresInSeconds;
        GrantedScope = grantedScope;
    }

    /// <summary>
    /// The compact serialization of the signed token. THE CREDENTIAL.
    /// </summary>
    /// <value>A signed token in three dot-separated segments.</value>
    /// <remarks>
    /// NEVER LOGGED, NEVER ECHOED IN AN EXCEPTION MESSAGE, NEVER RENDERED BY
    /// <see cref="ToString"/>, and never returned by any operation other than the issuance operation
    /// that produced it. Its only legitimate destination is the issuance response.
    /// </remarks>
    public string AccessToken { get; }

    /// <summary>
    /// The identifier of the key that signed this token, as carried in the token's own header.
    /// </summary>
    /// <value>The configured key identifier.</value>
    /// <remarks>
    /// Equal to the configured identifier and to the one published in the key set - the issuer refuses
    /// to construct unless those agree, so this value cannot drift from what a verifier selects by.
    /// Public metadata: it names a key without carrying any part of it.
    /// </remarks>
    public string KeyId { get; }

    /// <summary>
    /// The issuance instant, truncated to a whole second.
    /// </summary>
    /// <value>An instant in UTC with no fractional second.</value>
    /// <remarks>
    /// The same value as the token's issuance claim, and the value the response's optional issued-at
    /// member carries. Truncated because the token's own time representation counts whole seconds, so
    /// a fractional part could not survive the round trip and would make the claim and this member
    /// disagree.
    /// </remarks>
    public DateTimeOffset IssuedAt { get; }

    /// <summary>
    /// The expiry instant, truncated to a whole second.
    /// </summary>
    /// <value>An instant in UTC with no fractional second.</value>
    /// <remarks>
    /// The same value as the token's expiry claim. Derived from
    /// <see cref="IssuedAt"/> plus the configured lifetime, from ONE clock reading, so the two are
    /// consistent by construction rather than by luck.
    /// </remarks>
    public DateTimeOffset ExpiresAt { get; }

    /// <summary>
    /// The lifetime in whole seconds, for the response's expiry member.
    /// </summary>
    /// <value>At least one second.</value>
    /// <remarks>
    /// EXACTLY the difference between the token's expiry and issuance claims, because both are derived
    /// from one already-truncated instant and this same whole-second lifetime. A test can therefore
    /// assert the identity rather than a tolerance.
    /// </remarks>
    public long ExpiresInSeconds { get; }

    /// <summary>
    /// The granted scope set as one space-delimited value.
    /// </summary>
    /// <value>The granted scopes, delimited by single spaces.</value>
    /// <remarks>
    /// BYTE-IDENTICAL TO THE TOKEN'S SCOPE CLAIM, because the claim and this member are the same
    /// string. A caller must read the granted set rather than assume its request was honoured in full;
    /// this is the value it reads.
    /// </remarks>
    public string GrantedScope { get; }

    /// <summary>
    /// Renders the token's public metadata and NEVER the token itself.
    /// </summary>
    /// <returns>The key identifier and the expiry instant.</returns>
    /// <remarks>
    /// Overridden precisely so that the inherited behaviour cannot be replaced later by a generated
    /// one, and so that a diagnostic that renders this object is useful without being dangerous. The
    /// two rendered values are public metadata; the credential, the subject and the scope are absent.
    /// Rendered with the invariant culture so a diagnostic reads the same wherever it is produced.
    /// </remarks>
    public override string ToString() =>
        FormattableString.Invariant($"IssuedToken {{ KeyId = {KeyId}, ExpiresAt = {ExpiresAt:O} }}");
}

/// <summary>
/// The outcome of one issuance attempt: either a token, or the refusal of an audience this issuer does
/// not serve.
/// </summary>
/// <remarks>
/// <para>
/// A CLOSED TWO-CASE OUTCOME, EXPRESSED SO THE COMPILER ENFORCES THE CHECK. <see cref="Token"/> is
/// declared nullable and is non-null exactly when <see cref="Outcome"/> is
/// <see cref="TokenIssuanceOutcome.Issued"/>, so a consumer that reaches for the token without
/// checking is warned by nullable analysis rather than discovering the problem at run time. With
/// warnings treated as errors across every project, that warning is a build failure.
/// </para>
/// <para>
/// A RESULT RATHER THAN AN EXCEPTION, and the reason is that an unlisted audience is not a fault. It
/// is an expected answer to a well-formed request from an authenticated caller, and the published
/// contract gives it a status code of its own. Signalling it by throwing would put an expected branch
/// on the exception path, force the endpoint to catch in order to route it, and make the two distinct
/// meanings of that status code - a subject the transport does not support, and an audience the
/// configuration does not list - arrive by two different mechanisms.
/// </para>
/// <para>
/// THE FACTORIES ARE INTERNAL, so only the issuer in this file can produce an outcome. A result is
/// therefore evidence that the issuer produced it. The sibling test project sees them through the
/// project's declared test visibility.
/// </para>
/// </remarks>
public sealed class TokenIssuanceResult
{
    /// <summary>
    /// Binds an outcome to its payload.
    /// </summary>
    /// <param name="outcome">The outcome.</param>
    /// <param name="token">The token when one was minted; otherwise <see langword="null"/>.</param>
    private TokenIssuanceResult(TokenIssuanceOutcome outcome, IssuedToken? token)
    {
        Outcome = outcome;
        Token = token;
    }

    /// <summary>
    /// What the attempt produced.
    /// </summary>
    /// <value>One of the two published outcomes.</value>
    public TokenIssuanceOutcome Outcome { get; }

    /// <summary>
    /// The minted token, or <see langword="null"/> when nothing was minted.
    /// </summary>
    /// <value>
    /// Non-null exactly when <see cref="Outcome"/> is <see cref="TokenIssuanceOutcome.Issued"/>.
    /// </value>
    /// <remarks>
    /// On a refusal this is null because NO TOKEN WAS CREATED - not because one was created and
    /// withheld. No signature is computed on the refusal path at all.
    /// </remarks>
    public IssuedToken? Token { get; }

    /// <summary>
    /// Reports a minted token.
    /// </summary>
    /// <param name="token">The minted token. Never <see langword="null"/>.</param>
    /// <returns>A result whose outcome is <see cref="TokenIssuanceOutcome.Issued"/>.</returns>
    internal static TokenIssuanceResult ForIssuedToken(IssuedToken token) =>
        new(TokenIssuanceOutcome.Issued, token);

    /// <summary>
    /// Reports that the requested audience is not one this issuer serves.
    /// </summary>
    /// <returns>
    /// A result whose outcome is <see cref="TokenIssuanceOutcome.AudienceNotPermitted"/> and whose
    /// token is <see langword="null"/>.
    /// </returns>
    /// <remarks>
    /// A FRESH INSTANCE RATHER THAN A SHARED ONE. A cached instance would be shared mutable-by-reach
    /// state for no measurable gain on a path that is, by design, rare - and this file holds no static
    /// state at all, which is a property worth keeping absolute rather than nearly true.
    /// </remarks>
    internal static TokenIssuanceResult ForUnpermittedAudience() =>
        new(TokenIssuanceOutcome.AudienceNotPermitted, token: null);
}


/// <summary>
/// Mints short-lived service tokens. THE ONLY COMPONENT IN THIS SYSTEM THAT MINTS ANYTHING.
/// </summary>
/// <remarks>
/// <para>
/// Security is the sole issuer; Gateway, DataServices and Persistence hold verification material only
/// and are not independent signing authorities. This type is reachable in exactly one way - the
/// issuance operation the published contract declares, which the service entry point protects with a
/// default-deny authorization policy whose only anonymous exemptions are the readiness probe, the
/// published key set and the discovery document. There is no anonymous path to this type, no internal
/// transport surface reaching it, and no second route.
/// </para>
/// <para>
/// THE ANTI-PATTERN THIS TYPE REPLACES is <c>tests/blink/test_jws.htm:L8-L23</c>, cited BY LOCATOR AND
/// NEVER BY VALUE: there, a private key written into the source is handed straight to a third-party
/// signer with a hardcoded header and a hardcoded payload whose subject, expiry, digest and credential
/// members are all literals - and whose expiry is an absolute instant that has long since passed, so
/// that page can no longer mint a usable token at all. Every one of those four literals has a derived
/// counterpart here or no counterpart at all: the subject comes from the request, the expiry comes from
/// the configured lifetime plus the injected clock, and no digest or credential value is minted. This
/// type holds NO key material of its own by construction - its credential arrives from
/// <see cref="SigningKeyProvider"/>, it never reads configuration for a key, and it constructs no
/// cryptographic key object. No value from that file appears here in any form.
/// </para>
/// <para>
/// THE SIGNATURE IS ASYMMETRIC AND ITS DEFAULT IS THE SHA-256 MEMBER OF THE RSA BLOCK-PADDED FAMILY,
/// and that is structural rather than a preference. The key set is published ANONYMOUSLY - it must be,
/// since a consumer fetches it in order to learn how to validate and so cannot already hold a token to
/// present - and the other three services hold verification material only. A shared-secret entry in
/// that published set - an <c>oct</c> key, in the key-set format's own vocabulary - would publish THE
/// SIGNING KEY ITSELF, and handing that key to three verifiers would make every one of them a
/// CO-SIGNER, collapsing the sole-issuer topology in a single anonymous request. That is why the
/// published key schema admits the RSA family alone. The digest agrees with the legacy signing primitive rather than being chosen freshly: the
/// oracle's own source comment records that ONE hash-type set parameterises its hashing, signing and
/// verifying primitives [<c>ws_objects/pfw.shared.pbl.src/enums.sru:L927</c>] and SHA-256 is a member
/// of it [<c>:L930</c>], while the scheme is fixed inside the closed binary because the signing
/// primitive declares a hash type and no padding argument
/// [<c>ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L70-L73</c>]. The probabilistic scheme named in the
/// anti-pattern's header has NO legacy identifier whatsoever - the oracle's padding catalogue holds
/// exactly two members, block padding and OAEP [<c>enums.sru:L949-L951</c>] - so it is that script's
/// choice and not a contract, and this issuer does not produce it.
/// </para>
/// <para>
/// WHY THE MINTING LIBRARY IS REFERENCED BY THIS SERVICE AND BY NO OTHER.
/// <c>Microsoft.IdentityModel.JsonWebTokens</c> is the token-MINTING library, and its package
/// reference appears in exactly one project file in the whole repository: this service's. Security is
/// the only minter, so this is the only project that needs a minting library, and THIS FILE IS THE
/// ONLY PLACE THAT CALLS IT - its handler and its token descriptor appear here and nowhere else. The
/// other three services reference only the inbound bearer handler and are therefore structurally
/// incapable of minting: the type that would do it is not on their compile path. "Who can mint" thus
/// has one answer a reviewer can check by reading project files, and one answer they can check by
/// reading this file.
/// </para>
/// <para>
/// TIME COMES ONLY FROM AN INJECTED CLOCK. The instant is read from <see cref="TimeProvider"/>, ONCE
/// per issuance, and the three time claims are derived from that single reading, so they cannot
/// disagree with one another. Nothing here calls a static clock, a tick counter or a stopwatch. A
/// fixed clock therefore yields byte-identical time claims across runs, which is what lets the
/// characterization model compare a recording at all - and the issuance path holds no other source of
/// non-determinism, because no token identifier is minted and the signature scheme is deterministic.
/// </para>
/// <para>
/// EVERY CLAIM VALUE IS DERIVED AND NONE IS A LITERAL. The issuer identity and the lifetime come from
/// <see cref="SecurityOptions"/>; the subject, the audience and the scope set come from the request;
/// the key identifier and the algorithm come from the credential. The only literals in this file are
/// the protocol's own claim NAMES and the delimiter its scope encoding requires. No key, no armour
/// delimiter, no encoded run, no password and no token literal appears anywhere in it, in code, in a
/// default value or in a comment.
/// </para>
/// <para>
/// FAIL FAST. Every structural fault is raised from the constructor rather than deferred to a request,
/// so a misconfigured issuer never reaches the point of answering a readiness probe and then failing
/// everything behind it. The legacy precedent is process termination on a structural fault
/// [<c>ws_objects/pfw.pbl.src/pfw.sra:L111-L144</c>, terminating at <c>:L143</c>], and the options
/// contract's validator plus validate-on-start already fail closed, so this is the third link in a
/// chain rather than a new opinion.
/// </para>
/// <para>
/// THREAD-SAFE AND INTENDED AS A SINGLETON. Every field is read-only, the audience roster is frozen at
/// construction, the clock and the minting handler are both safe for concurrent use, and the
/// signing-key provider hands out the same immutable credential on every access. Nothing is cached
/// between calls and no state is mutated by an issuance, so concurrent issuances cannot interfere.
/// </para>
/// <para>
/// NO INTERFACE IS DECLARED FOR IT, matching the way this service registers its other providers -
/// concretely - and costing nothing in testability, because the two seams that matter are already
/// injected: the clock, and the key provider.
/// </para>
/// </remarks>
/// <example>
/// Registration alongside the seams the entry point already provides, then one issuance:
/// <code>
/// builder.Services.AddSingleton&lt;SigningKeyProvider&gt;();
/// builder.Services.AddSingleton&lt;TokenIssuer&gt;();
///
/// TokenIssuanceResult result = issuer.Issue(new TokenIssuanceRequest(subject, audience, scopes));
///
/// if (result.Token is null)
/// {
///     return Results.Problem(statusCode: StatusCodes.Status403Forbidden);
/// }
/// </code>
/// </example>
public sealed class TokenIssuer
{
    /// <summary>The subject claim name.</summary>
    /// <remarks>
    /// A protocol identifier rather than a value, which is why it is a literal at all. The claim it
    /// names carries the caller identity from the request and never a configured or invented one.
    /// </remarks>
    private const string SubjectClaimName = "sub";

    /// <summary>The scope claim name.</summary>
    /// <remarks>
    /// ONE CLAIM CARRYING A SPACE-DELIMITED VALUE, not a repeated claim, and the published contract
    /// decides that rather than this file: the only scope spelling anywhere in
    /// <c>shared/PowerFramework.Contracts/OpenApi/security.v1.yaml</c> is the space-delimited
    /// response member, and no repeated-entry alternative appears in it even once. Using the same
    /// spelling and the same encoding for the claim keeps the token and the response reporting the
    /// granted set identically, which is a property a test can assert byte for byte.
    /// </remarks>
    private const string ScopeClaimName = "scope";

    /// <summary>The delimiter joining the granted scope set into one value.</summary>
    /// <remarks>
    /// A single space, as the published response member's encoding requires. This is the reason a
    /// requested scope containing white space is refused: the encoding is lossless only while no
    /// scope carries the delimiter.
    /// </remarks>
    private const char ScopeDelimiter = ' ';

    /// <summary>The configuration key carrying the issuer identity.</summary>
    /// <remarks>
    /// Composed from <see cref="SecurityOptions.SectionName"/> rather than written out, so one
    /// spelling of the section name serves every message and a rename cannot leave a message pointing
    /// at a key that no longer exists.
    /// </remarks>
    private const string IssuerKey = SecurityOptions.SectionName + ":Issuer";

    /// <summary>The configuration key carrying the audience roster.</summary>
    private const string AudiencesKey = SecurityOptions.SectionName + ":Audiences";

    /// <summary>The configuration key carrying the token lifetime.</summary>
    private const string LifetimeKey = SecurityOptions.SectionName + ":TokenLifetime";

    /// <summary>The configuration key carrying the key identifier.</summary>
    private const string KeyIdKey = SecurityOptions.SectionName + ":SigningKeyId";

    /// <summary>The configuration key carrying the signature algorithm.</summary>
    private const string AlgorithmKey = SecurityOptions.SectionName + ":SigningAlgorithm";

    /// <summary>The fixed message reported when the issuer identity is blank.</summary>
    /// <remarks>
    /// Fixed at compile time and naming the configuration key, never the configured value - the
    /// discipline the whole service applies to failure messages, so that no code path can interpolate
    /// configuration into a diagnostic by accident.
    /// </remarks>
    private const string IssuerBlankMessage =
        "Configuration key '" + IssuerKey + "' is not set, is empty or contains only whitespace. It " +
        "is stamped into every minted token as the issuer claim and is what each verifier validates " +
        "that claim against, so there is no usable default and none is invented. This message never " +
        "echoes the configured value.";

    /// <summary>The fixed message reported when the audience roster is empty.</summary>
    private const string RosterEmptyMessage =
        "Configuration key '" + AudiencesKey + "' lists no audience. An issuer that may address no " +
        "audience cannot mint a usable token, and no audience is inferred, discovered or defaulted.";

    /// <summary>The fixed message reported when an audience roster entry is blank.</summary>
    private const string RosterBlankMessage =
        "Configuration key '" + AudiencesKey + "' contains an entry that is empty or whitespace " +
        "only. Every entry must be a service identity, because an entry that is not one could be " +
        "matched by a request and would then be stamped into a token as though it were an identity.";

    /// <summary>The fixed message reported when the configured lifetime rounds below one second.</summary>
    private const string LifetimeMessage =
        "Configuration key '" + LifetimeKey + "' does not carry at least one whole second. The " +
        "published response reports the lifetime in whole seconds and requires it to be at least " +
        "one, and a token whose expiry equals its issuance is expired the instant it is minted. The " +
        "lifetime is neither defaulted nor rounded up to compensate.";

    /// <summary>The fixed message reported when the signing credential is not asymmetric.</summary>
    /// <remarks>
    /// THE ONE REFUSAL THAT IS ABOUT TOPOLOGY RATHER THAN CONFIGURATION. It names no configuration
    /// key because no configuration setting can produce it: the signing-key provider resolves an
    /// asymmetric key or refuses to construct, so a non-asymmetric credential can only mean the
    /// composition root supplied something other than that provider's credential.
    /// </remarks>
    private const string SymmetricKeyMessage =
        "The supplied signing credential does not carry an asymmetric key. This issuer signs with an " +
        "asymmetric key only, and the reason is structural rather than a strength preference: the " +
        "verification key set is published anonymously, so a shared-secret entry there would publish " +
        "the signing key itself and make every verifier a co-signer. There is no fallback to a keyed " +
        "hash and no degraded mode; the signing credential must be the one the signing-key provider " +
        "resolves. This message never echoes any part of the credential.";

    /// <summary>The fixed message reported when the credential's key identifier disagrees with the setting.</summary>
    private const string KeyIdMismatchMessage =
        "The signing credential's key identifier does not match configuration key '" + KeyIdKey +
        "'. The identifier is stamped into every token header and published in the key set, so a " +
        "verifier selects a key by it; a disagreement here would mint tokens naming a key the " +
        "published set does not describe. This message never echoes either value.";

    /// <summary>
    /// The signing-key layer. THE ONLY SOURCE OF A SIGNING CREDENTIAL, and the only holder of key
    /// material anywhere in this service.
    /// </summary>
    private readonly SigningKeyProvider _signingKeys;

    /// <summary>The clock seam. THE ONLY SOURCE OF THE CURRENT INSTANT.</summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>The log sink. Receives public metadata only, never a credential.</summary>
    private readonly ILogger<TokenIssuer> _logger;

    /// <summary>
    /// The minting handler, reused across issuances because it is safe for concurrent use and holds no
    /// per-token state.
    /// </summary>
    private readonly JsonWebTokenHandler _handler = new();

    /// <summary>
    /// The audience roster, frozen at construction and matched ORDINALLY.
    /// </summary>
    /// <remarks>
    /// Ordinal because an audience identity is compared byte for byte by the recipient's own bearer
    /// handler: admitting a differently cased spelling here would mint a token that every verifier
    /// then rejects, which is a harder failure to diagnose than an outright refusal.
    /// </remarks>
    private readonly FrozenSet<string> _audiences;

    /// <summary>The issuer identity stamped into every token.</summary>
    private readonly string _issuer;

    /// <summary>The key identifier the credential's key carries, verified against configuration.</summary>
    private readonly string _keyId;

    /// <summary>The token lifetime in whole seconds, at least one.</summary>
    private readonly long _lifetimeSeconds;

    /// <summary>The token lifetime as a whole-second interval, for the expiry arithmetic.</summary>
    private readonly TimeSpan _lifetime;


    /// <summary>
    /// Resolves everything issuance depends on, or refuses to construct.
    /// </summary>
    /// <param name="signingKeys">
    /// The signing-key layer. THE ONLY SOURCE OF A SIGNING CREDENTIAL: no key is imported here, no
    /// cryptographic key object is constructed here, and no configuration setting carrying key
    /// material is read here.
    /// </param>
    /// <param name="options">The bound security configuration.</param>
    /// <param name="timeProvider">
    /// The clock seam, which the service entry point registers. THE ONLY SOURCE OF THE CURRENT
    /// INSTANT.
    /// </param>
    /// <param name="logger">The log sink, which receives public metadata only.</param>
    /// <exception cref="ArgumentNullException">
    /// Any argument is <see langword="null"/>, which can only mean the composition root is miswired.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The configuration or the supplied credential cannot support issuance: the issuer identity is
    /// blank; the audience roster is empty or carries a blank entry; the lifetime does not carry a
    /// whole second; the credential's key is not asymmetric; its algorithm is not one this issuer
    /// produces; or its key identifier disagrees with the configured one. Each case reports a fixed
    /// message that names the configuration key and never its value.
    /// </exception>
    /// <remarks>
    /// <para>
    /// EAGER BY DESIGN. Everything is resolved and verified here rather than on first use, because the
    /// point of validating at all is that a misconfigured issuer must not reach the point of answering
    /// a readiness probe - a service that reports healthy and then refuses every request looks correct
    /// from the outside for exactly as long as it takes to page someone.
    /// </para>
    /// <para>
    /// ORDER MATTERS AND IS DELIBERATE. The configuration is checked first, because a deployment that
    /// got a plain setting wrong should be told about that setting rather than about the credential;
    /// the credential's structure is checked afterwards by <see cref="VerifyCredential"/>, which
    /// applies its own most-significant-property-first ordering.
    /// </para>
    /// <para>
    /// THE CREDENTIAL IS VERIFIED HERE BUT READ AT EACH ISSUANCE, and both halves of that are
    /// intentional. Verifying here is what makes the failure a startup failure. Reading it fresh each
    /// time means the signing-key layer's own lifetime is respected - a disposed provider reports that
    /// itself rather than having a stale handle used behind its back - and it costs nothing in
    /// consistency, because that layer hands out the same immutable credential on every access, which
    /// is the property that lets the verification performed here still hold at issuance time.
    /// </para>
    /// <para>
    /// THE AUDIENCE ROSTER IS COPIED INTO A FROZEN SET rather than read through the options instance on
    /// each request. That gives a constant-time ordinal membership test, and it means the roster this
    /// issuer enforces cannot be altered after the checks above passed - a mutable roster read
    /// per-request would let a later mutation introduce the blank entry this constructor just refused.
    /// </para>
    /// <para>
    /// NOTHING IS TRIMMED, CASE-FOLDED OR OTHERWISE REPAIRED. The issuer identity, the audience
    /// entries and the key identifier are all used exactly as configured, which keeps this file in
    /// agreement with the options validator and the signing-key layer - both of which also compare
    /// configured values as given. A type that quietly accepted a value its own validator refuses
    /// would put two files in one service at odds about their own contract.
    /// </para>
    /// </remarks>
    public TokenIssuer(
        SigningKeyProvider signingKeys,
        IOptions<SecurityOptions> options,
        TimeProvider timeProvider,
        ILogger<TokenIssuer> logger)
    {
        ArgumentNullException.ThrowIfNull(signingKeys);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        SecurityOptions security = options.Value;

        _signingKeys = signingKeys;
        _timeProvider = timeProvider;
        _logger = logger;

        _issuer = RequireIssuer(security.Issuer);
        _audiences = RequireAudienceRoster(security.Audiences);
        _lifetimeSeconds = RequireLifetime(security.TokenLifetime);
        _lifetime = TimeSpan.FromSeconds(_lifetimeSeconds);

        (string keyId, long legacyHashType) =
            VerifyCredential(signingKeys.SigningCredentials, security.SigningKeyId);

        _keyId = keyId;
        LegacySigningHashType = legacyHashType;
    }

    /// <summary>
    /// Verifies a signing credential against every structural invariant this issuer requires, and
    /// reports the two facts issuance derives from it.
    /// </summary>
    /// <param name="credentials">The credential to verify.</param>
    /// <param name="configuredKeyId">The configured key identifier it must agree with.</param>
    /// <returns>
    /// The verified key identifier, and the legacy hash-type constant naming the digest the
    /// credential's algorithm signs over.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="credentials"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The key is not asymmetric; the algorithm is not one this issuer produces; or the key identifier
    /// is blank or disagrees with the configured one.
    /// </exception>
    /// <remarks>
    /// <para>
    /// ONE ENTRY POINT FOR THE WHOLE CREDENTIAL CONTRACT, called from the constructor so that every
    /// fault below is a startup failure. The order is deliberate and runs from the most structural
    /// property to the most incidental: is the key asymmetric at all, then is its algorithm one this
    /// issuer produces, then does its identifier agree with what the key set publishes. A credential
    /// that fails the first question makes the other two meaningless, so reporting them in that order
    /// keeps a diagnostic pointed at the real fault.
    /// </para>
    /// <para>
    /// INTERNAL RATHER THAN PRIVATE, AND THAT IS A TESTABILITY DECISION WITH A REASON. The only
    /// production supplier of a credential is <see cref="SigningKeyProvider"/>, which refuses to
    /// construct unless it has resolved an asymmetric key with a permitted algorithm - so through that
    /// supplier alone, two of the three refusals here are unreachable and would sit in the codebase
    /// permanently unexercised. Exposing the verification to the sibling test project is what makes
    /// them provable, and provable refusals are the point of having them: a defence nobody has ever
    /// seen fire is a defence nobody knows works. Nothing about this widens the surface a caller can
    /// reach - it is not public, it holds no state, it performs no minting, and it cannot produce a
    /// token.
    /// </para>
    /// </remarks>
    internal static (string KeyId, long LegacyHashType) VerifyCredential(
        SigningCredentials credentials,
        string configuredKeyId)
    {
        ArgumentNullException.ThrowIfNull(credentials);

        RequireAsymmetricKey(credentials);

        long legacyHashType = RequireSigningAlgorithm(credentials.Algorithm);
        string keyId = RequireKeyIdAgreement(credentials, configuredKeyId);

        return (keyId, legacyHashType);
    }

    /// <summary>
    /// The legacy hash-type constant corresponding to the signature this issuer produces.
    /// </summary>
    /// <value>
    /// <see cref="Enums.CRYPTO_HASH_SHA256"/>, <see cref="Enums.CRYPTO_HASH_SHA384"/> or
    /// <see cref="Enums.CRYPTO_HASH_SHA512"/>.
    /// </value>
    /// <remarks>
    /// <para>
    /// THE ALGORITHM-AGREEMENT CLAIM, MADE CHECKABLE RATHER THAN ONLY ASSERTED IN PROSE. The oracle's
    /// own source comment records that one hash-type set parameterises its hashing, signing and
    /// verifying primitives [<c>ws_objects/pfw.shared.pbl.src/enums.sru:L927</c>], so the digest this
    /// issuer signs with is expressible as a member of that set - and this property is that member.
    /// It is produced by the same total switch that admits the algorithm, so an algorithm this issuer
    /// accepts and a legacy hash type it maps to cannot fall out of step: there is no arm of the switch
    /// that yields one without the other.
    /// </para>
    /// <para>
    /// INTERNAL, because it is an audit projection rather than a capability. It influences no claim, no
    /// header and no signature - the signature scheme is carried by the credential's own algorithm
    /// identifier - and it exists so the sibling test project can assert the correspondence directly.
    /// The constant is REFERENCED from the shared kernel and never redeclared here, which also keeps
    /// this file free of the preserved screaming-case spellings that the shared catalogue carries and
    /// that only the catalogue's own file is permitted to declare.
    /// </para>
    /// </remarks>
    internal long LegacySigningHashType { get; }

    /// <summary>
    /// Mints one short-lived service token, or refuses an audience this issuer does not serve.
    /// </summary>
    /// <param name="request">The well-formed request.</param>
    /// <returns>
    /// A result carrying the minted token, or one reporting that the requested audience is not on the
    /// configured roster.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">
    /// The signing-key layer has been disposed, which at run time means the host is shutting down.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The configured lifetime added to the current instant is not a representable point in time. The
    /// platform reports this precisely, and no cap is invented here to pre-empt it: capping the
    /// lifetime would be a policy the published contract does not state.
    /// </exception>
    /// <remarks>
    /// <para>
    /// SYNCHRONOUS, BECAUSE THERE IS NO INPUT OR OUTPUT. Signing is computation over material already
    /// in memory; this service has no outbound edge and reads no store while issuing. An asynchronous
    /// signature would advertise a suspension point that does not exist.
    /// </para>
    /// <para>
    /// THE CLOCK IS READ EXACTLY ONCE, and all three time claims are derived from that one reading, so
    /// the issuance instant, the not-before instant and the expiry cannot disagree with one another.
    /// The reading is truncated to a whole second before anything is derived from it, because the
    /// token's own time representation counts whole seconds: truncating first means the expiry is
    /// exactly the lifetime after the issuance instant IN THE TOKEN, whereas truncating each claim
    /// independently would let a fractional reading move the difference by a second. That identity is
    /// what makes a fixed clock produce byte-identical claims, which the characterization model
    /// requires.
    /// </para>
    /// <para>
    /// THE NOT-BEFORE INSTANT EQUALS THE ISSUANCE INSTANT. No skew, leeway or grace window is added -
    /// tolerance for clock drift is each verifier's concern, and expressing it on both sides invites
    /// the two to be tuned against each other until neither is knowable.
    /// </para>
    /// <para>
    /// THE HEADER IS NOT WRITTEN BY HAND. The key identifier is set ON THE KEY by the signing-key
    /// layer, so the minting library stamps it into the header itself; nothing here restates it, and
    /// the header therefore cannot disagree with the published key set. The algorithm identifier
    /// likewise comes from the credential. This is the direct inversion of the anti-pattern's
    /// hand-written header at <c>tests/blink/test_jws.htm:L8-L23</c>.
    /// </para>
    /// <para>
    /// THE CLAIMS ARE SUPPLIED AS A DICTIONARY RATHER THAN AS AN IDENTITY, deliberately. An identity
    /// would be subjected to the minting library's outbound claim-name mapping, which rewrites some
    /// well-known names; a dictionary is written through verbatim, so the payload carries exactly the
    /// two claim names this file declares. That matters because the service's own inbound handler is
    /// configured NOT to map claim names either, so mapping on the way out and not on the way back
    /// would break the round trip.
    /// </para>
    /// <para>
    /// THE AUDIENCE IS CHECKED BEFORE ANY WORK IS DONE. A refused request costs no signature, no clock
    /// reading and no allocation of a token, and the refusal carries no partial result. There is no
    /// default audience and no inference of one: an unlisted audience is refused, never substituted.
    /// </para>
    /// <para>
    /// THE GRANTED SET IS THE REQUESTED SET, and that is a reported fact rather than an assumption. The
    /// published contract permits the granted set to be narrower and requires a caller to read it from
    /// the response; no narrowing policy is configured anywhere in this service, so nothing is
    /// narrowed, and inventing one would be a new capability. The result reports the granted set
    /// either way, so a caller that reads it is correct now and stays correct if a policy is ever
    /// introduced.
    /// </para>
    /// <para>
    /// WHAT IS LOGGED, AND WHAT DELIBERATELY IS NOT. A successful issuance records the key identifier,
    /// the issuer, the audience and the expiry - all four public metadata, and the audience is by then
    /// a value read from the roster rather than from the request. A refusal records NO CALLER-SUPPLIED
    /// VALUE AT ALL: the audience that was refused is, by definition, not a configured identity, so
    /// writing it to a log would put unvalidated caller text into a log record. The subject is never
    /// logged on either path, and neither is the token, the credential or the scope set.
    /// </para>
    /// </remarks>
    public TokenIssuanceResult Issue(TokenIssuanceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Ordinal membership, and the check comes first so that a refusal performs no cryptographic
        // work whatsoever. Because the comparison is ordinal, the value that passes it IS the roster
        // entry byte for byte, so the audience claim below carries the configured identity exactly.
        if (!_audiences.Contains(request.Audience))
        {
            TokenIssuerLog.AudienceRefused(_logger, _issuer);

            return TokenIssuanceResult.ForUnpermittedAudience();
        }

        // ONE clock reading, truncated once, and every instant below derived from it.
        DateTimeOffset issuedAt = TruncateToWholeSecond(_timeProvider.GetUtcNow());
        DateTimeOffset expiresAt = issuedAt + _lifetime;

        // One string serves both the claim and the reported granted set, so the two cannot differ.
        string grantedScope = string.Join(ScopeDelimiter, request.Scopes.AsSpan());

        SecurityTokenDescriptor descriptor = new()
        {
            Issuer = _issuer,
            Audience = request.Audience,
            IssuedAt = issuedAt.UtcDateTime,
            NotBefore = issuedAt.UtcDateTime,
            Expires = expiresAt.UtcDateTime,

            // Read fresh from the layer that owns it. Its key carries the identifier, so the header is
            // stamped rather than written, and its algorithm carries the scheme.
            SigningCredentials = _signingKeys.SigningCredentials,

            // Ordinal, because claim names are case-sensitive.
            Claims = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                [SubjectClaimName] = request.Subject,
                [ScopeClaimName] = grantedScope,
            },
        };

        IssuedToken token = new(
            accessToken: _handler.CreateToken(descriptor),
            keyId: _keyId,
            issuedAt: issuedAt,
            expiresAt: expiresAt,
            expiresInSeconds: _lifetimeSeconds,
            grantedScope: grantedScope);

        TokenIssuerLog.TokenIssued(_logger, _keyId, _issuer, request.Audience, expiresAt);

        return TokenIssuanceResult.ForIssuedToken(token);
    }

    /// <summary>
    /// Drops the fractional part of an instant, leaving a whole second.
    /// </summary>
    /// <param name="instant">The instant, as read from the clock seam.</param>
    /// <returns>The same instant with its fractional second removed, in UTC.</returns>
    /// <remarks>
    /// TRUNCATION, NEVER ROUNDING. Rounding up would move an issuance instant into the future, and
    /// rounding an expiry up would let a token outlive its configured lifetime by a fraction of a
    /// second; truncation can do neither. The arithmetic is on the UTC tick count, so the result is
    /// independent of whatever offset the supplied instant carried.
    /// </remarks>
    private static DateTimeOffset TruncateToWholeSecond(DateTimeOffset instant) =>
        new(instant.UtcTicks - (instant.UtcTicks % TimeSpan.TicksPerSecond), TimeSpan.Zero);

    /// <summary>
    /// Requires a non-blank issuer identity.
    /// </summary>
    /// <param name="issuer">The configured identity.</param>
    /// <returns>The identity, VERBATIM.</returns>
    /// <exception cref="InvalidOperationException">The identity is blank.</exception>
    /// <remarks>
    /// Blank includes whitespace only, because a whitespace identity would be stamped into every token
    /// as though it were one. Returned untrimmed: each verifier compares the issuer claim byte for
    /// byte against its own configured value, so trimming here would mint tokens that a correctly
    /// configured verifier rejects. No format rule is imposed - the identity's shape is a deployment
    /// decision, and a rule invented here could refuse one a deployment legitimately uses.
    /// </remarks>
    private static string RequireIssuer(string issuer)
    {
        if (string.IsNullOrWhiteSpace(issuer))
        {
            throw new InvalidOperationException(IssuerBlankMessage);
        }

        return issuer;
    }

    /// <summary>
    /// Copies the configured audience roster into a frozen ordinal set.
    /// </summary>
    /// <param name="audiences">The configured roster.</param>
    /// <returns>The roster as a frozen set.</returns>
    /// <exception cref="InvalidOperationException">
    /// The roster is empty or carries an entry that is blank.
    /// </exception>
    /// <remarks>
    /// <para>
    /// The roster property cannot be null - it is initialised at declaration and exposes no setter, so
    /// the configuration binder populates the existing instance rather than replacing it - which is why
    /// no null check appears here rather than a check being overlooked.
    /// </para>
    /// <para>
    /// A REPEATED ENTRY IS NOT AN ERROR HERE. The options validator reports one, and the set simply
    /// collapses it, because a duplicate changes nothing about which audiences are permitted. A BLANK
    /// entry is a different matter and is refused: it could be matched by a request and would then be
    /// stamped into a token as though it were an identity.
    /// </para>
    /// <para>
    /// The comparer is ordinal, matching the way a recipient's bearer handler compares the audience
    /// claim. A case-insensitive roster would admit a request whose audience differs from the
    /// configured spelling and mint a token every verifier then rejects - a failure that surfaces far
    /// from its cause.
    /// </para>
    /// </remarks>
    private static FrozenSet<string> RequireAudienceRoster(IList<string> audiences)
    {
        if (audiences.Count == 0)
        {
            throw new InvalidOperationException(RosterEmptyMessage);
        }

        for (int index = 0; index < audiences.Count; index++)
        {
            if (string.IsNullOrWhiteSpace(audiences[index]))
            {
                throw new InvalidOperationException(RosterBlankMessage);
            }
        }

        return audiences.ToFrozenSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// Requires a lifetime of at least one whole second, and reduces it to whole seconds.
    /// </summary>
    /// <param name="lifetime">The configured lifetime.</param>
    /// <returns>The lifetime in whole seconds.</returns>
    /// <exception cref="InvalidOperationException">
    /// The lifetime does not carry a whole second.
    /// </exception>
    /// <remarks>
    /// <para>
    /// TRUNCATED, NEVER ROUNDED UP, so a token cannot outlive the configured lifetime. The published
    /// response reports the lifetime as a whole number of seconds and requires at least one, so a
    /// sub-second remainder is unrepresentable on the wire in any case - and where this file and that
    /// document disagree, the document wins, because it is what a consumer reads.
    /// </para>
    /// <para>
    /// THE FLOOR IS THE DOCUMENT'S, NOT AN INVENTED POLICY. The options validator requires the
    /// lifetime to be positive, which a fraction of a second satisfies while still reducing to zero
    /// whole seconds - a token expiring at the instant it is minted, and a response member below its
    /// declared minimum. Refusing at construction is what turns that into a startup failure rather than
    /// a stream of useless tokens. There is no upper bound, because the contract states none and
    /// inventing a cap would be a policy of this file's own.
    /// </para>
    /// </remarks>
    private static long RequireLifetime(TimeSpan lifetime)
    {
        long seconds = (long)lifetime.TotalSeconds;

        if (seconds < 1)
        {
            throw new InvalidOperationException(LifetimeMessage);
        }

        return seconds;
    }

    /// <summary>
    /// Requires the signing credential to carry an asymmetric key.
    /// </summary>
    /// <param name="credentials">The credential supplied by the signing-key layer.</param>
    /// <exception cref="InvalidOperationException">The key is not asymmetric.</exception>
    /// <remarks>
    /// THE KEY FAMILY IS CHECKED, NOT MERELY THE ALGORITHM NAME, because the two can disagree: a
    /// credential could name an asymmetric algorithm while carrying a shared secret, and that
    /// combination must not reach the minting library. Refused for a structural reason rather than a
    /// strength preference - the verification key set is published anonymously, so a shared secret
    /// there would publish the signing key itself and make every verifier a co-signer. There is no
    /// downgrade path and no warn-and-continue arm.
    /// </remarks>
    private static void RequireAsymmetricKey(SigningCredentials credentials)
    {
        if (credentials.Key is not AsymmetricSecurityKey)
        {
            throw new InvalidOperationException(SymmetricKeyMessage);
        }
    }

    /// <summary>
    /// Resolves the credential's algorithm against the closed set this issuer produces, yielding the
    /// legacy hash type it corresponds to.
    /// </summary>
    /// <param name="algorithm">The credential's algorithm identifier.</param>
    /// <returns>The legacy hash-type constant naming the digest.</returns>
    /// <exception cref="InvalidOperationException">
    /// The identifier is not one of the permitted values.
    /// </exception>
    /// <remarks>
    /// <para>
    /// A TOTAL SWITCH OVER THREE CONSTANTS, matching the signing-key layer's own switch arm for arm so
    /// that the two cannot admit different sets. Matching is ordinal and case-sensitive, which is what
    /// a switch over string constants already is and is also correct: these identifiers are
    /// case-sensitive, and a differently cased spelling would be published to verifiers that do not
    /// recognise it.
    /// </para>
    /// <para>
    /// IT RETURNS THE LEGACY HASH TYPE RATHER THAN A BOOLEAN, which is what keeps the
    /// algorithm-agreement claim honest: the same arm that admits an algorithm names the legacy digest
    /// it corresponds to, so the two are written once and cannot drift. The three digests are the ones
    /// the oracle's own catalogue names as its signing primitive's argument set
    /// [<c>ws_objects/pfw.shared.pbl.src/enums.sru:L927</c>, values at <c>:L930-L932</c>].
    /// </para>
    /// <para>
    /// Everything else falls to the refusal arm: the keyed-hash family structurally, the probabilistic
    /// family for want of any legacy identifier [<c>enums.sru:L949-L951</c> declares block padding and
    /// OAEP and nothing else], the elliptic-curve family because the legacy cryptographic class
    /// declares no such primitive, and an unsigned token because an issuer that does not sign is not
    /// an issuer.
    /// </para>
    /// </remarks>
    private static long RequireSigningAlgorithm(string algorithm) =>
        algorithm switch
        {
            SecurityAlgorithms.RsaSha256 => Enums.CRYPTO_HASH_SHA256,
            SecurityAlgorithms.RsaSha384 => Enums.CRYPTO_HASH_SHA384,
            SecurityAlgorithms.RsaSha512 => Enums.CRYPTO_HASH_SHA512,
            _ => throw new InvalidOperationException(AlgorithmMessage()),
        };

    /// <summary>
    /// Requires the credential's key identifier to be present and to match the configured one.
    /// </summary>
    /// <param name="credentials">The credential supplied by the signing-key layer.</param>
    /// <param name="configured">The configured identifier.</param>
    /// <returns>The identifier, VERBATIM.</returns>
    /// <exception cref="InvalidOperationException">
    /// The configured identifier is blank, or the credential's key carries a different one.
    /// </exception>
    /// <remarks>
    /// <para>
    /// THIS IS THE CHECK THAT MAKES THE HEADER AND THE PUBLISHED KEY SET PROVABLY THE SAME KEY. The
    /// identifier is stamped into the header from the credential's key and published in the key set
    /// from configuration; a verifier selects by it. Were the two to disagree, this issuer would mint
    /// tokens naming a key the published set does not describe, and every verifier would fail to
    /// select a key - a failure that looks like a signature problem and is not one.
    /// </para>
    /// <para>
    /// A BLANK CONFIGURED IDENTIFIER IS REFUSED BY THE SAME GUARD, because two blanks compare equal
    /// and would otherwise pass. The signing-key layer refuses a blank as well, so this restates that
    /// refusal rather than relaxing it. Compared ordinally and used untrimmed, for the same reason
    /// every other configured value here is.
    /// </para>
    /// </remarks>
    private static string RequireKeyIdAgreement(SigningCredentials credentials, string configured)
    {
        if (string.IsNullOrWhiteSpace(configured) ||
            !string.Equals(credentials.Key.KeyId, configured, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(KeyIdMismatchMessage);
        }

        return configured;
    }

    /// <summary>
    /// Builds the fixed message reported when the credential's algorithm is not one this issuer
    /// produces.
    /// </summary>
    /// <returns>The message, naming the configuration key and the permitted set.</returns>
    /// <remarks>
    /// A METHOD RATHER THAN A STATIC FIELD, so that this file holds no static state of any kind. The
    /// permitted set is read from the options contract that publishes it rather than restated, so the
    /// message and the switch above describe the same three values by construction. It names no
    /// configured value, only the key that carries one.
    /// </remarks>
    private static string AlgorithmMessage() =>
        "Configuration key '" + AlgorithmKey + "' names a signature algorithm this issuer does not " +
        "produce. The permitted set is " +
        string.Join(", ", SecurityOptionsValidator.PermittedSigningAlgorithms) +
        " - the closed set the options contract publishes and the signing-key layer accepts, so all " +
        "three agree by construction. A keyed-hash algorithm is refused structurally, because the " +
        "verification key set is published anonymously and a shared secret there would publish the " +
        "signing key itself; the probabilistic and elliptic-curve families are refused because the " +
        "legacy cryptographic surface declares no identifier for either. This message never echoes " +
        "any part of the credential.";
}

/// <summary>
/// The log records issuance produces. PUBLIC METADATA ONLY.
/// </summary>
/// <remarks>
/// <para>
/// SOURCE-GENERATED, so each template is checked against its parameters at compile time and each
/// value is captured as a named field rather than interpolated into a sentence. That is what makes
/// the two records below structured rather than merely formatted, and it is also the mitigation that
/// matters here: a value captured as a field cannot rewrite the shape of the record that carries it.
/// </para>
/// <para>
/// WHAT IS ABSENT IS THE POINT. No record here carries the token, the signing credential, any key
/// material or the subject. The permitted fields are the key identifier, the issuer, the audience and
/// the expiry, and the refusal record carries FEWER than that on purpose: it names no
/// caller-supplied value at all, because the audience it refused is by definition not a configured
/// identity, and writing unvalidated caller text into a log record is a hazard whether or not the
/// text happens to be harmless. The caller already knows what it asked for; it received a refusal.
/// </para>
/// <para>
/// Declared in this file because its folder admits exactly two files, following the same rule that
/// puts the request and result shapes here.
/// </para>
/// </remarks>
internal static partial class TokenIssuerLog
{
    /// <summary>
    /// Records a successful issuance.
    /// </summary>
    /// <param name="logger">The log sink.</param>
    /// <param name="keyId">The identifier of the key that signed the token. Public metadata.</param>
    /// <param name="issuer">The issuer identity. Configured, not caller-supplied.</param>
    /// <param name="audience">
    /// The audience. By this point a value matched ordinally against the configured roster, so it is a
    /// configured identity rather than unvalidated caller text.
    /// </param>
    /// <param name="expiresAt">The expiry instant.</param>
    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Information,
        Message = "Issued a service token. kid={KeyId} iss={Issuer} aud={Audience} exp={ExpiresAt}")]
    internal static partial void TokenIssued(
        ILogger logger,
        string keyId,
        string issuer,
        string audience,
        DateTimeOffset expiresAt);

    /// <summary>
    /// Records a refusal, carrying no caller-supplied value.
    /// </summary>
    /// <param name="logger">The log sink.</param>
    /// <param name="issuer">The issuer identity. Configured, not caller-supplied.</param>
    /// <remarks>
    /// A warning rather than an error: a request for an audience this issuer does not serve is a
    /// caller mistake or a misconfiguration on the caller's side, and the issuer handled it correctly.
    /// </remarks>
    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Warning,
        Message = "Refused a token request: the requested audience is not on the configured roster. " +
                  "iss={Issuer}")]
    internal static partial void AudienceRefused(ILogger logger, string issuer);
}

