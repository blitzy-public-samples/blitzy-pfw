// ==================================================================================================
//  SecurityClient.cs
//  Gateway's outbound client for contract C-01, security.v1.TokenService.
//  ------------------------------------------------------------------------------------------------
//  WHAT THIS FILE IS
//    Gateway reaches the Security service for exactly one reason: to obtain the short-lived service
//    token it presents on its own outbound calls. This file is that client, and the token-provider
//    abstraction the sibling DataServicesClient consumes to attach a bearer credential to every gRPC
//    call it makes.
//
//    It implements ONE operation - POST /v1/tokens - and deliberately implements nothing else. The
//    reasons for each omission are recorded below rather than left to be inferred, because every one
//    of them is a decision that a later reader could otherwise mistake for an unfinished edge.
//
//  THERE IS NO LEGACY SOURCE FOR THIS FILE, AND THAT IS A FINDING RATHER THAN A GAP (C-C)
//    The legacy PowerFramework is a LIBRARY. It has no process of its own, no listening socket, no
//    route table and no serialization layer, and it has no authentication concept anywhere. The
//    composition-root reference ws_objects/pfw.pbl.src/pfw.sra was read in full: its open event
//    declares a local locale variable, calls the framework initialize function with the
//    all-capabilities mask, selects one of three localization providers and opens a window; its close
//    event calls finalize. There is no credential, no token, no HTTP call, no client type and no auth
//    concept in it, or anywhere else in the 39 exported libraries.
//
//    So nothing here is a port. There is no "legacy-faithful client shape" to imitate and inventing
//    one would be fabrication. What IS faithful to the legacy in this file is its VALUES and its
//    SEMANTICS - specifically the return-code algebra surfaced on the failure type below - never a
//    schema. The legacy tree was read and never written to, moved, reformatted or remediated.
//
//  THE WIRE IS DEFINED BY shared/PowerFramework.Contracts/OpenApi/security.v1.yaml, WHICH IS
//  AUTHORITATIVE
//    That document generates NO code: PowerFramework.Contracts packages its OpenAPI definitions as
//    content, declares no protocol-definition item for them, and no OpenAPI client generator is
//    referenced anywhere in the repository. Its project directory contains zero .cs files, and no
//    PowerFramework.Contracts.Security.V1 type exists to import - verified rather than assumed. Every
//    data type below is therefore hand-authored against the schema, with the schema's exact member
//    spellings, and adding a code generator to avoid that would be an unrequested dependency.
//
//  DECISION RECORD (C-K: document every technology-specific and boundary-specific decision)
//
//    (a) WHY THIS CONTRACT IS REST, AND WHY THAT IS NOT A STYLE PREFERENCE.
//        Token issuance and key publication have to speak ordinary HTTP so that a consumer's STOCK
//        Microsoft.AspNetCore.Authentication.JwtBearer handler fetches /.well-known/jwks.json and the
//        OpenID discovery document and self-configures from them with ZERO bespoke code. That keeps
//        the security-critical retrieval path inside framework code instead of hand-written code, on
//        three services rather than one. Choosing gRPC here would have forced a hand-written key-set
//        retrieval implementation into each of the three consuming services - a net INCREASE in
//        hand-written security code, which is the opposite direction from the one an
//        authenticated-boundary requirement pushes in. It is the wrong choice, not merely a less
//        convenient one.
//
//        "REST" names the interface shape, not the scheme. The contract's own server entry is
//        https://localhost:5104, matching the listener Security binds (docs/ARCHITECTURE.md 4.1): the
//        channel is TLS because this edge carries a caller credential in one direction and a bearer
//        token in the other. This client does not choose the scheme either way - it issues a relative
//        request against the base address the composition root configured from the Gateway upstream
//        setting - which is exactly why that setting's DEFAULT is the https form rather than the
//        cleartext one a forgetful deployment would otherwise inherit.
//
//    (b) THE TWO /.well-known/* OPERATIONS ARE DELIBERATELY ABSENT FROM THIS CLIENT.
//        Contract C-01 declares three operations. This client implements one. The key-set and
//        discovery publications are NOT implemented here, and their absence is the point of (a): they
//        exist so the stock bearer handler consumes them directly. Hand-rolling key-set retrieval,
//        discovery, key parsing, key caching, key rotation, signature verification or claims
//        validation in this file would replace framework code with hand-written security code, which
//        is exactly what the transport decision was made to avoid. The composition root owns that
//        path. Nothing in this file fetches, parses or caches verification material.
//
//    (c) RESILIENCE IS REQUIRED BY THE TRANSITION, NOT LAYERED ON TOP OF IT.
//        This client is wrapped by Microsoft.Extensions.Http.Resilience handlers registered on the
//        typed client in the composition root. That dependency must not read as scope creep against
//        the minimal-change clause, so the justification is stated plainly: an in-process call cannot
//        fail in transit, and a network call can. Gateway's call to Security is an edge the legacy
//        framework simply did not have, because PowerFramework was an in-process library with no
//        process of its own, no listener and no server tier. Without retry and circuit breaking the
//        first transient network fault would surface as a defect the legacy COULD NOT HAVE HAD, which
//        is a regression introduced by the refactor rather than a preserved behaviour. Handling a
//        failure mode that decomposition itself creates is therefore required BY the transition.
//
//        Retry belongs to transient TRANSPORT faults only. A 4xx from this operation is a definitive
//        answer - a malformed request, an untrusted client certificate, or a caller not permitted the
//        subject or audience it asked for - and retrying it would turn a clear refusal into a loop.
//        This file consequently raises a typed failure on any non-success status and retries nothing
//        itself. No latency, throughput, scalability or availability property is claimed or implied
//        by any of this: the repository publishes no such budget anywhere, so none may be asserted.
//
//    (d) RAW KEY MATERIAL NEVER CROSSES THE WIRE FROM A CALLER, AND THAT NARROWS THE LEGACY
//        DELIBERATELY.
//        Contract C-02, the cryptographic surface, is NOT wrapped by this client - see (e) - but the
//        rule that governs it is recorded here because it is the single most consequential difference
//        between the legacy parameter lists and the published boundary. The legacy
//        ws_objects/pfw.crypto.pbl.src/n_crypto.sru declares 63 cryptographic overloads and EVERY
//        keyed one takes raw key bytes as an ordinary parameter - `readonly string key`,
//        `readonly blob key`, and `ref string prikey` / `ref string pubkey` on key generation. An
//        agent who ported those signatures literally would put private keys on the wire.
//
//        The boundary therefore takes an OPAQUE keyRef that the Security service resolves against its
//        own configured key store, and no request schema in that document has a field a key, an
//        initialization vector, a passphrase or a password could be placed in. That is a real
//        narrowing of the legacy's reach and it is recorded as such rather than presented as
//        equivalence. Should any C-02 member ever be authored here, the rule is absolute: no key
//        material in a body, a header, a query string, a URL segment, a log record or an exception
//        message.
//
//    (e) C-02 IS NOT WRAPPED HERE, ON PURPOSE.
//        The contract inventory assigns C-02's consumer explicitly - Security serves DataServices -
//        and Gateway's endpoint roster is health, ping, capabilities, the DataServices projections and
//        the reserved extension points. Not one of them consumes a cryptographic operation. Adding an
//        unused 17-operation crypto surface here would be precisely the convenience aggregation the
//        no-new-features constraint forbids. DataServices carries that surface in its own client at
//        its own layer; this file does not reference it, and the resulting duplication between the two
//        services' Security clients is REQUIRED by the one-coupling constraint rather than being a
//        DRY defect to be tidied away.
//
//    (f) ISSUANCE ACCEPTS TWO SCHEMES AND THIS CLIENT PRESENTS WHICHEVER IS CONFIGURED.
//        POST /v1/tokens OVERRIDES the document-level bearer requirement and declares clientCredential
//        (HTTP Basic) and mutualTLS as ALTERNATIVES. The override is structural rather than a
//        preference: A CALLER CANNOT PRESENT A BEARER TOKEN IN ORDER TO OBTAIN ITS FIRST BEARER TOKEN.
//
//        Three consequences this file honours exactly. It attaches an Authorization: Basic header when
//        a client secret is configured, and none when it is not - in which case the client certificate
//        attached to the primary handler in the composition root is the credential, and a header would
//        be redundant. It attaches NO BEARER header to this operation ever, because the operation does
//        not accept one and doing so would be meaningless. And it places NO credential in the request
//        BODY: the schema sets additionalProperties false and carries no client secret, password, API
//        key, assertion or key material, so the body this file sends has exactly three members. A
//        deployment configured with NEITHER scheme is refused at startup by Configuration/GatewayOptions
//        rather than discovered here, which is also why the issuance path must not be terminated by an
//        intermediary proxy - a terminating proxy would strip the client certificate.
//
//    (g) GATEWAY VALIDATES TOKENS; IT NEVER MINTS THEM. THIS IS ABSOLUTE.
//        Security is the sole minter in the system and exactly one signing key exists anywhere in it,
//        held by Security alone and supplied to it by configuration injection. Gateway holds
//        verification material only. When this client needs a service token it ASKS SECURITY TO MINT
//        ONE over the operation below; it never signs locally.
//
//        Accordingly this file contains no signing key, no private key, no signing credential, no
//        token handler construction and no token-creation call of any kind, and it names no signing
//        secret. That is enforced at the package level rather than by review alone: the minting
//        library is deliberately absent from PowerFramework.Gateway.csproj, so reaching for it does
//        not compile. A compile error there is the design working, not an obstacle to route around.
//
//        The named anti-pattern this file must never resemble is the legacy browser asset
//        tests/blink/test_jws.htm, which embeds a plaintext RSA private key and signs a token with it
//        locally - a client holding a signing key. It was inspected structurally only, no value from
//        it is reproduced here in any form, and it was not edited.
//
//    (h) NO REFRESH SEMANTICS EXIST IN THIS CONTRACT, SO NONE ARE INVENTED HERE.
//        The contract declares no grant type, no authorization endpoint, no token-endpoint client
//        authentication and no refresh token anywhere. Its stated remedy for needing another token is
//        to CALL THE OPERATION AGAIN over the same mutually authenticated channel. This client
//        therefore reuses a credential only while it remains valid and issues a fresh request once it
//        does not - which IS that remedy - and adds no refresh grant, no forced-invalidation member
//        and no retry-on-401 re-authentication loop. Adding any of them would be a new feature, and
//        refresh specifically would create the long-lived credential a sole-issuer topology
//        deliberately does without.
//
//  WHAT THIS FILE DELIBERATELY DOES NOT DO
//    Recorded so each absence reads as a decision, and so nobody "completes" this file by adding one.
//      * It does not construct an HttpClient, a message handler or a resilience pipeline. It takes a
//        configured HttpClient by constructor injection, which is what lets a test replace the
//        transport and what keeps the composition root's registration authoritative.
//      * It does not set the base address. The composition root does, from the Gateway upstream
//        setting for Security. This file issues a relative request and fails fast with a message
//        naming that configuration key if no base address was configured.
//      * It performs NO network input or output during construction, in a static initializer, or in
//        any eager warm-up path. Startup must never require Security to be reachable.
//      * It holds no storage, no database context, no connection string, no SQL and no file-backed
//        persistence of tokens or keys. Exactly one service in this system holds a storage provider
//        and it is not this one; the packages that would allow otherwise are absent from the project.
//      * It declares no client, partial client, placeholder, interface or option for any capability
//        deferred out of this phase. Those capabilities have no project and no implementation in this
//        build graph, and their reserved routes are route-table metadata in the endpoint layer.
//      * It declares no constant in the preserved SCREAMING_SNAKE spelling. Those spellings are legal
//        only in the files the repository-root .editorconfig scopes its naming-analyzer suppressions
//        to, and no Gateway file is among them. The return-code catalogue is CONSUMED from the shared
//        kernel instead, which is where it belongs.
//      * It asserts no timeout, retry count, backoff interval, circuit-breaker threshold or cache
//        duration justified by performance. The one lifetime decision it makes is bounded by the
//        contract's own expiry value and by nothing this file chose.
// ==================================================================================================

using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using PowerFramework.Gateway.Configuration;

namespace PowerFramework.Gateway.Clients;

/// <summary>
/// Supplies the short-lived service token Gateway presents on its own outbound calls.
/// </summary>
/// <remarks>
/// <para>
/// This abstraction exists for two reasons and is kept deliberately small so it serves only those
/// two. First, the sibling DataServices client depends on it rather than on the concrete HTTP client,
/// so attaching a bearer credential to an outbound call does not couple that client to this one's
/// transport. Second, it is the substitution seam: a test replaces this interface with a fake and
/// exercises the whole outbound path with no Security instance running anywhere.
/// </para>
/// <para>
/// Every newly created boundary in this system is authenticated from the outset, INCLUDING the
/// internal ones. The legacy framework opened no listening socket and received no unsolicited
/// request, so decomposition creates every one of these edges from nothing; an internal edge that
/// trusted its caller because "it is internal" would be a new unauthenticated surface, which is
/// precisely what that requirement exists to prevent. This interface is how Gateway satisfies it on
/// the edges it originates.
/// </para>
/// <para>
/// It is NOT a general-purpose authentication framework, and it must not grow into one. There is
/// exactly one member. There is no token validation here, no key retrieval, no discovery, no refresh
/// grant and no signing capability of any kind - Gateway validates inbound tokens with the stock
/// bearer handler and mints nothing at all.
/// </para>
/// </remarks>
public interface IServiceTokenProvider
{
    /// <summary>
    /// Obtains a service token for the supplied subject, audience and requested scope set.
    /// </summary>
    /// <param name="request">
    /// The identity being claimed, the single intended audience, and the requested scopes. The
    /// audience is deliberately singular: the contract carries one audience per request so that a
    /// token is never valid somewhere its holder did not intend, and a caller needing tokens for two
    /// audiences asks twice.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels the request. This is threaded through every awaited call on the path and is never
    /// swallowed - it is the target-side expression of the legacy's cross-thread proxy pattern, in
    /// which a caller-side proxy and a worker-side implementation existed as a pair so that no object
    /// was ever touched from two threads.
    /// </param>
    /// <returns>
    /// The issued credential together with its absolute expiry and the scope set that was actually
    /// GRANTED. The granted set may be narrower than the requested one; that is a normal successful
    /// outcome rather than a failure, so a caller must read it here instead of assuming its request
    /// was honoured in full.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="SecurityClientException">
    /// The Security service refused the request, or answered in a shape the contract does not permit.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// No upstream address was configured for the Security service.
    /// </exception>
    /// <exception cref="HttpRequestException">The request could not be completed in transit.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<ServiceToken> GetTokenAsync(ServiceTokenRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// The three things - and the only three things - a token request carries.
/// </summary>
/// <remarks>
/// <para>
/// This mirrors the token request schema of contract C-01 exactly: a claimed subject, one intended
/// audience, and a requested scope set. WHAT IS ABSENT IS THE IMPORTANT PART. There is no client
/// secret, no password, no API key, no client assertion, no passphrase and no key material of any
/// kind, and there is no member one could be smuggled into either - the schema sets
/// <c>additionalProperties: false</c> and this type has no extension bag. Caller identity is
/// established by the TRANSPORT, through the client certificate the issuance operation requires, and
/// never by a credential in a request body.
/// </para>
/// <para>
/// The <see cref="Subject"/> below is a CLAIM rather than a credential. The identity actually
/// honoured is the one the presented client certificate establishes, and a mismatch between the two
/// is refused by the service.
/// </para>
/// <para>
/// This is a sealed class rather than a record, deliberately. A record's compiler-generated equality
/// would compare <see cref="Scopes"/> by REFERENCE, because the declared member is an interface
/// rather than a value-equal collection - so two requests that are semantically identical would
/// usually compare unequal, and a reader would reasonably expect otherwise. Rather than publish
/// equality that is quietly wrong, this type publishes none, and the cache key the client derives is
/// computed explicitly from the members instead.
/// </para>
/// </remarks>
public sealed class ServiceTokenRequest
{
    /// <summary>
    /// Creates a token request, validating it against the published schema before it can exist.
    /// </summary>
    /// <param name="subject">The identity the caller claims. Must be non-empty.</param>
    /// <param name="audience">The single intended audience. Must be non-empty.</param>
    /// <param name="scopes">
    /// The requested scopes: at least one, each non-empty, all distinct, and none containing
    /// whitespace. Copied defensively, so a later mutation of the supplied sequence cannot alter a
    /// request that has already been made.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="subject"/>, <paramref name="audience"/> or <paramref name="scopes"/> is
    /// <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">Any argument violates the schema described above.</exception>
    public ServiceTokenRequest(string subject, string audience, IEnumerable<string> scopes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(audience);
        ArgumentNullException.ThrowIfNull(scopes);

        // The schema declares minItems 1, so an empty scope set is not a request this contract can
        // express. Refusing it here names the fault at its origin rather than spending a round trip to
        // be told the same thing.
        string[] requested = [.. scopes];
        if (requested.Length == 0)
        {
            throw new ArgumentException(
                "At least one scope must be requested; the contract declares a minimum of one.",
                nameof(scopes));
        }

        // Each scope carries minLength 1. The additional refusal of INTERNAL WHITESPACE is a narrowing
        // with a defined error rather than a stylistic preference, and it is forced by the response
        // half of this same contract: the GRANTED scope set comes back as a single space-delimited
        // string, so a scope token containing a space could not be represented there and therefore
        // could never be read back by the caller that asked for it. A request that cannot have a
        // readable answer is refused before it is sent.
        foreach (string scope in requested)
        {
            if (string.IsNullOrWhiteSpace(scope))
            {
                throw new ArgumentException(
                    "A requested scope must be a non-empty value.",
                    nameof(scopes));
            }

            if (scope.Any(char.IsWhiteSpace))
            {
                throw new ArgumentException(
                    "A requested scope must not contain whitespace, because the granted scope set is "
                    + "returned as a single space-delimited value and such a scope could not be read "
                    + "back from it.",
                    nameof(scopes));
            }
        }

        // uniqueItems is declared on the schema, so duplicates are not merely redundant - they make the
        // request non-conforming. Ordinal comparison is used because a scope is an opaque protocol
        // token, not culture-sensitive text.
        if (new HashSet<string>(requested, StringComparer.Ordinal).Count != requested.Length)
        {
            throw new ArgumentException(
                "Requested scopes must be distinct; the contract declares them as a unique set.",
                nameof(scopes));
        }

        Subject = subject;
        Audience = audience;
        Scopes = requested;
    }

    /// <summary>
    /// The identity the caller is requesting a token for - a claim, checked by the service against the
    /// identity the presented client certificate establishes.
    /// </summary>
    public string Subject { get; }

    /// <summary>
    /// The single intended audience for the token, which the recipient's stock bearer handler
    /// validates.
    /// </summary>
    /// <remarks>
    /// One audience per request, deliberately, so that a token is never valid somewhere its holder did
    /// not intend. This is also why the audience is a per-call argument rather than a configured
    /// value: Gateway may legitimately need tokens for more than one audience, and each is a separate
    /// request with a separately cached credential.
    /// </remarks>
    public string Audience { get; }

    /// <summary>
    /// The REQUESTED scope set. The granted set may be narrower - read
    /// <see cref="ServiceToken.GrantedScopes"/> rather than treating this as authoritative.
    /// </summary>
    public IReadOnlyList<string> Scopes { get; }
}

/// <summary>
/// An issued service token, its absolute expiry, and the scope set that was actually granted.
/// </summary>
/// <remarks>
/// <para>
/// THIS TYPE HOLDS A CREDENTIAL, WHICH DICTATES TWO OF ITS DESIGN DECISIONS.
/// </para>
/// <para>
/// It is a sealed class rather than a record because a record's compiler-generated
/// <see cref="object.ToString"/> prints every member it declares. That would place the raw token in
/// any log entry, exception message or diagnostic string that happened to format the object - the
/// most ordinary way a credential leaks. <see cref="ToString"/> is overridden below to describe the
/// token without disclosing it, and no member of this type is ever logged by this file.
/// </para>
/// <para>
/// <see cref="ExpiresAt"/> is an ABSOLUTE instant rather than the lifetime-in-seconds the wire
/// carries. The conversion is done once, here, at the moment the response is read, because doing it
/// at each point of use would re-anchor a relative value to a later clock reading every time and
/// steadily overstate how long the credential remains valid.
/// </para>
/// </remarks>
/// <summary>
/// The one credential store contract C-01's token provider and contract C-02's crypto operations share.
/// </summary>
/// <remarks>
/// <para>
/// A NAMED TYPE FOR ONE DICTIONARY, AND IT EXISTS TO MAKE A GUARANTEE ASSERTABLE. Before this type,
/// <see cref="SecurityClient"/> owned its cache as a field initialiser - and because a typed HTTP client
/// is registered TRANSIENT, every resolve produced a client with an empty one. The composition root's
/// comments promised that the token provider and the crypto client shared a credential; the wiring
/// delivered two clients with two caches, each minting its own token on its first call. Extracting the
/// cache lets the composition root register it as a SINGLETON and lets a composition test assert with
/// <c>Assert.Same</c> that the sharing is real rather than intended.
/// </para>
/// <para>
/// THE KEY SPACE IS BOUNDED BY CONSTRUCTION. Keys are composed from the subject, audience and scope set
/// that this service's OWN call sites supply, never from external input, so it cannot grow without limit
/// no matter what a caller of this service sends. There is deliberately no eviction policy: an entry is
/// overwritten when its credential lapses and is otherwise reused, and a policy would be an invented
/// duration on a value whose lifetime the issuer already fixes.
/// </para>
/// <para>
/// NOTHING IS PERSISTED. There is no file, no database and no distributed cache behind it, so no
/// credential outlives the process and constraint C-E stays true of this file. A held token is never
/// logged, never echoed into a response and never written to a characterization recording.
/// </para>
/// <para>
/// CONCURRENCY: the dictionary is the concurrent one, because a singleton is reached from every request
/// at once. Last write wins on a refresh, and no ordering guarantee is required - both the value replaced
/// and the value stored are valid credentials for the same key.
/// </para>
/// </remarks>
public sealed class ServiceTokenCache
{
    /// <summary>
    /// The held credentials, keyed by the subject, audience and scope set they were minted for.
    /// </summary>
    public ConcurrentDictionary<string, ServiceToken> Entries { get; } = new(StringComparer.Ordinal);
}

public sealed class ServiceToken
{
    /// <summary>
    /// Creates an issued token.
    /// </summary>
    /// <param name="accessToken">The signed token, presented as a bearer credential.</param>
    /// <param name="tokenType">The credential type. The contract fixes this at <c>Bearer</c>.</param>
    /// <param name="expiresAt">The absolute instant at which the token stops being valid.</param>
    /// <param name="grantedScopes">
    /// The scopes actually granted, which may be fewer than were requested and may legitimately be
    /// none at all.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="accessToken"/>, <paramref name="tokenType"/> or <paramref name="grantedScopes"/>
    /// is <see langword="null"/>.
    /// </exception>
    public ServiceToken(
        string accessToken,
        string tokenType,
        DateTimeOffset expiresAt,
        IEnumerable<string> grantedScopes)
    {
        ArgumentNullException.ThrowIfNull(accessToken);
        ArgumentNullException.ThrowIfNull(tokenType);
        ArgumentNullException.ThrowIfNull(grantedScopes);

        AccessToken = accessToken;
        TokenType = tokenType;
        ExpiresAt = expiresAt;
        GrantedScopes = [.. grantedScopes];
    }

    /// <summary>
    /// The signed token, for presentation in the <c>Authorization</c> header as a bearer credential.
    /// </summary>
    /// <remarks>
    /// THIS IS A CREDENTIAL. It is never logged, never placed in an exception message, and never
    /// written to any diagnostic sink by this file. A caller composes the header value from
    /// <see cref="TokenType"/> and this member and should treat the result with the same care.
    /// </remarks>
    public string AccessToken { get; }

    /// <summary>
    /// The credential type, which the contract pins to the constant <c>Bearer</c>.
    /// </summary>
    /// <remarks>
    /// The value the service returned is preserved verbatim rather than substituted with a local
    /// constant, so that what a caller presents is what the issuer actually said.
    /// </remarks>
    public string TokenType { get; }

    /// <summary>
    /// The absolute instant at which this token stops being valid.
    /// </summary>
    /// <remarks>
    /// Derived from the lifetime the response carries, anchored to the issuance instant the response
    /// reports when it supplies one and to the reading of the injected clock when it does not.
    /// </remarks>
    public DateTimeOffset ExpiresAt { get; }

    /// <summary>
    /// The scopes actually granted.
    /// </summary>
    /// <remarks>
    /// This may be NARROWER than the requested set, and an empty set is a legitimate, successful
    /// outcome meaning no requested scope was granted - not an error and not a malformed response.
    /// Treating the request as authoritative is the mistake this member exists to prevent.
    /// </remarks>
    public IReadOnlyList<string> GrantedScopes { get; }

    /// <summary>
    /// Describes this token WITHOUT disclosing it.
    /// </summary>
    /// <returns>
    /// A redacted description carrying the credential type, the expiry and the granted scope count,
    /// and no part of the token itself.
    /// </returns>
    /// <remarks>
    /// Overridden precisely so that formatting this object - which is how a credential most often
    /// reaches a log by accident - cannot disclose one. The token value is replaced by a fixed
    /// redaction marker rather than by a truncated prefix, because a prefix is still key material.
    /// </remarks>
    public override string ToString() => string.Create(
        CultureInfo.InvariantCulture,
        $"{nameof(ServiceToken)} {{ {nameof(TokenType)} = {TokenType}, {nameof(AccessToken)} = "
        + $"[REDACTED], {nameof(ExpiresAt)} = {ExpiresAt:O}, {nameof(GrantedScopes)} = "
        + $"{GrantedScopes.Count} granted }}");
}

/// <summary>
/// Raised when the Security service refuses a request, or answers in a shape the published contract
/// does not permit.
/// </summary>
/// <remarks>
/// <para>
/// The properties below project the ONE error body contract C-01 defines - the RFC 9457
/// problem-details object every one of its <c>4xx</c> and <c>5xx</c> responses carries. No second
/// error shape is invented here, because the contract deliberately defines only one so that a consumer
/// writes a single error handler.
/// </para>
/// <para>
/// <see cref="RetCode"/> is the reason this type exists rather than a bare
/// <see cref="HttpRequestException"/>: it surfaces the legacy PowerFramework return code the boundary
/// transcribes, so a caller can branch on the same algebra the rest of the port speaks. Two properties
/// of that algebra must be EXPECTED rather than repaired. The legacy success predicate is
/// <c>&gt;= 0</c>, so <see cref="Shared.Kernel.RetCode.PREVENT"/> (1) reads as a SUCCESS; and
/// <see cref="Shared.Kernel.RetCode.CANCELLED"/> (-2) is excluded from failure by an explicit guard
/// while also failing the success test, so it is NEITHER succeeded nor failed. That tri-state hole in
/// a nominally boolean algebra is preserved legacy behaviour. Branch on the specific value; never on a
/// two-way success test, and never "correct" it.
/// </para>
/// <para>
/// The message is kept deliberately short - the status and, where the service supplied one, the
/// problem title. Everything else is exposed as a typed property instead. That is a redaction-minded
/// choice rather than a stylistic one: an exception message is the part most likely to be copied into
/// a log line or an outbound error response, so it carries the least it can while nothing is lost to a
/// caller that wants the rest. No credential, key reference, key material or request body is placed
/// in the message or in any property of this type.
/// </para>
/// </remarks>
public sealed class SecurityClientException : Exception
{
    /// <summary>Creates the exception with no further detail.</summary>
    public SecurityClientException()
    {
    }

    /// <summary>Creates the exception with a message.</summary>
    /// <param name="message">A description that carries nothing sensitive.</param>
    public SecurityClientException(string? message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a message and the failure that caused it.</summary>
    /// <param name="message">A description that carries nothing sensitive.</param>
    /// <param name="innerException">The underlying failure.</param>
    public SecurityClientException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// The HTTP status code the Security service returned, when the failure reached a response at all.
    /// </summary>
    public int? StatusCode { get; init; }

    /// <summary>
    /// The problem type as a URI reference, from the <c>type</c> member. RFC 9457's own
    /// <c>about:blank</c> default appears when no more specific type applied.
    /// </summary>
    public string? ProblemType { get; init; }

    /// <summary>A short, human-readable summary of the problem type, stable across occurrences.</summary>
    public string? Title { get; init; }

    /// <summary>
    /// The explanation specific to this occurrence, from the <c>detail</c> member.
    /// </summary>
    /// <remarks>
    /// The contract guarantees this never carries key material, a stack trace, a file path, a provider
    /// class name or any part of the configured key store. It is nevertheless treated as
    /// service-supplied text and is not interpolated into the exception message.
    /// </remarks>
    public string? Detail { get; init; }

    /// <summary>A URI reference identifying this specific occurrence, from the <c>instance</c> member.</summary>
    public string? Instance { get; init; }

    /// <summary>
    /// The legacy PowerFramework return code for this failure, from the contract's single extension
    /// member, or <see langword="null"/> when the response carried none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Numerically identical to <see cref="Shared.Kernel.RetCode"/>. The values a caller should expect
    /// from this operation are <see cref="Shared.Kernel.RetCode.E_INVALID_ARGUMENT"/> for a malformed
    /// request, <see cref="Shared.Kernel.RetCode.E_ACCESS_DENIED"/> for an untrusted caller or one not
    /// permitted the subject or audience it asked for, and
    /// <see cref="Shared.Kernel.RetCode.E_INTERNAL_ERROR"/> or
    /// <see cref="Shared.Kernel.RetCode.UNKNOWN"/> for a server-side failure.
    /// </para>
    /// <para>
    /// It is left <see langword="null"/> rather than defaulted when absent. Substituting a plausible
    /// code would fabricate an oracle value and silently corrupt any stored parity comparison that
    /// mentions it.
    /// </para>
    /// </remarks>
    public long? RetCode { get; init; }
}

/// <summary>
/// The typed HTTP client for contract C-01's token issuance operation.
/// </summary>
/// <remarks>
/// <para>
/// Registered as a typed <see cref="HttpClient"/> in the composition root, which is also where the base
/// address is taken from the Gateway upstream setting for Security and where the
/// <c>Microsoft.Extensions.Http.Resilience</c> handlers are attached. This type constructs no
/// <see cref="HttpClient"/>, no message handler and no resilience pipeline of its own: doing so would
/// both bypass that registration and break a test's ability to substitute the transport.
/// </para>
/// <para>
/// IT PERFORMS NO NETWORK INPUT OR OUTPUT DURING CONSTRUCTION, and none in a static initializer or any
/// eager warm-up path. Nothing about this type requires Security to be reachable for the host to start
/// or for the test suite to run.
/// </para>
/// <para>
/// A credential is reused while it remains valid and re-requested once it does not. That is the
/// contract's own stated remedy for needing another short-lived token - call the operation again -
/// rather than an added caching feature, and the lifetime is bounded entirely by the expiry the
/// service reports. No duration is chosen here, and no skew margin is subtracted: a margin would be a
/// value with no basis anywhere in the contract, and the repository publishes no budget from which one
/// could be derived.
/// </para>
/// </remarks>
public sealed class SecurityClient : IServiceTokenProvider
{
    /// <summary>
    /// The name this client's HTTP channel is registered and resolved under.
    /// </summary>
    /// <remarks>
    /// AN EXPLICIT NAME, NOT A DERIVED ONE. The channel is registered by name so its lifetime can be owned
    /// by a scoped registration rather than by the framework's transient typed-client descriptor, and the
    /// name lives here - on the type that owns the channel - so the registration and the resolution cannot
    /// drift apart and neither depends on the client factory's type-name convention. A resolution under an
    /// UNREGISTERED name silently yields a default-configured client: no address, no pinned trust, no
    /// client certificate and no resilience.
    /// </remarks>
    public const string HttpClientName = "security-rest";

    /// <summary>
    /// The issuance path, used exactly as contract C-01 publishes it.
    /// </summary>
    /// <remarks>
    /// Relative on purpose. The base address belongs to the composition root, and the contract's server
    /// entry is a bare origin carrying no path prefix, so the published path resolves against it
    /// unchanged.
    /// </remarks>
    private static readonly Uri TokenPath = new("/v1/tokens", UriKind.Relative);

    /// <summary>
    /// The serializer settings for both directions of this boundary.
    /// </summary>
    /// <remarks>
    /// The web defaults supply case-insensitive member matching, which makes reading tolerant without
    /// making it lax. Every member this file declares carries an explicit wire name, and an explicit
    /// name takes precedence over any naming policy, so the exact spellings the schema publishes are
    /// what travel regardless of the policy in force. The shared-framework problem-details type has no
    /// such attributes and relies on the web defaults' camel casing, which is the second reason these
    /// settings are shared rather than split in two.
    /// </remarks>
    private static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// The lowest and highest values the optional issuance timestamp may carry and still be
    /// representable, computed from the type's own domain rather than transcribed as literals.
    /// </summary>
    private static readonly long MinIssuedAtSeconds = DateTimeOffset.MinValue.ToUnixTimeSeconds();

    /// <inheritdoc cref="MinIssuedAtSeconds"/>
    private static readonly long MaxIssuedAtSeconds = DateTimeOffset.MaxValue.ToUnixTimeSeconds();

    /// <summary>
    /// The configuration key that supplies this client's base address, composed from the options type's
    /// own identifiers so the two cannot drift apart.
    /// </summary>
    /// <remarks>
    /// Built with <c>nameof</c> against <see cref="GatewayOptions"/> rather than written out as a
    /// string, so that renaming a property there is a compile error here instead of a diagnostic message
    /// that quietly starts naming a key which no longer exists.
    /// </remarks>
    private static readonly string SecurityAddressConfigurationKey = string.Join(
        ':',
        GatewayOptions.SectionName,
        nameof(GatewayOptions.Upstreams),
        nameof(GatewayOptions.UpstreamAddresses.Security));

    /// <summary>
    /// The delimiter RFC 6749 uses for the granted scope set, and therefore the one this boundary uses.
    /// </summary>
    private const char ScopeSeparator = ' ';

    /// <summary>
    /// The credential type contract C-01 pins with a schema constant.
    /// </summary>
    private const string BearerTokenType = "Bearer";

    /// <summary>
    /// The name of the single extension member the contract's error body defines.
    /// </summary>
    private const string RetCodeExtensionMember = "retCode";

    /// <summary>
    /// The scheme token of the credential this client presents on the issuance operation.
    /// </summary>
    /// <remarks>
    /// Written with the canonical capitalisation even though RFC 9110 makes the scheme token
    /// case-insensitive and Security compares it case-insensitively. Sending the canonical spelling keeps
    /// this client interoperable with any intermediary stricter than the specification requires, and
    /// costs nothing.
    /// </remarks>
    private const string BasicSchemeToken = "Basic";

    /// <summary>
    /// Separates a cache-key component's LENGTH from the component itself.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is not a delimiter between components and the distinction is the whole point. A delimiter
    /// is only collision-free while every component is guaranteed not to contain it, and that guarantee
    /// has to be ENFORCED somewhere - which it was not. A separator that merely "cannot appear in
    /// practice" is an unverified invariant, and an unverified invariant on a cache key is a
    /// credential-confusion bug waiting for the first component that breaks it.
    /// </para>
    /// <para>
    /// Length prefixing needs no such guarantee: a reader consumes the digits, then exactly that many
    /// characters, so the encoding is self-delimiting and the mapping from component sequence to key is
    /// injective for ARBITRARY component content, including content carrying this character or any
    /// other. See <see cref="AppendKeyComponent"/>.
    /// </para>
    /// </remarks>
    private const char CacheKeyLengthSeparator = ':';

    private readonly HttpClient _httpClient;
    private readonly ILogger<SecurityClient> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly IOptions<GatewayOptions>? _options;

    /// <summary>
    /// The issued credentials this instance currently holds, keyed by the request that produced each.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Concurrent by construction rather than by convention. A typed client is resolved per scope while
    /// a single instance may serve several concurrent requests, so this is genuinely shared mutable
    /// state and is guarded accordingly. No lock is held across the network call - a lock spanning
    /// input and output would serialise unrelated callers behind one another for no correctness benefit,
    /// and it cannot be held across an <c>await</c> in any case. The consequence is that two callers
    /// racing on the same key may each obtain a token, which is harmless: issuance is independent and
    /// free of side effects, and either credential is equally valid.
    /// </para>
    /// <para>
    /// The key space is bounded by construction. Keys are composed from the subject, audience and scope
    /// set that this service's OWN call sites supply, never from external input, so this cannot grow
    /// without limit no matter what a caller of Gateway sends.
    /// </para>
    /// <para>
    /// Reuse therefore spans the lifetime of this instance. That is stated rather than assumed because
    /// the registration lifetime in the composition root is what decides how broadly a credential is
    /// shared, and this type deliberately does not reach for static state to widen it.
    /// </para>
    /// </remarks>
    private readonly ServiceTokenCache _tokenCache;

    /// <summary>
    /// Creates the client against the system clock.
    /// </summary>
    /// <param name="httpClient">
    /// The configured client supplied by the typed-client factory, already carrying its base address and
    /// resilience handlers.
    /// </param>
    /// <param name="logger">The logger for this client.</param>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// This overload exists so the type resolves whether or not a <see cref="TimeProvider"/> has been
    /// registered in the container. The activator selects the widest constructor it can satisfy, so
    /// registering one engages the seam below and registering nothing engages this. Neither performs any
    /// network input or output.
    /// </remarks>
    public SecurityClient(HttpClient httpClient, ILogger<SecurityClient> logger)
        : this(httpClient, logger, TimeProvider.System)
    {
    }

    /// <summary>
    /// Creates the client against an explicit clock.
    /// </summary>
    /// <param name="httpClient">
    /// The configured client supplied by the typed-client factory, already carrying its base address and
    /// resilience handlers.
    /// </param>
    /// <param name="logger">The logger for this client.</param>
    /// <param name="timeProvider">
    /// The clock used to decide whether a held credential is still valid. This is the determinism seam:
    /// a test substitutes it so that expiry and reuse are exercised without waiting for real time to
    /// pass, which is what makes the behaviour reproducible rather than schedule-dependent.
    /// </param>
    /// <param name="tokenCache">
    /// The credential store this client reads and writes. SUPPLIED BY THE COMPOSITION ROOT AS A
    /// SINGLETON, so a credential minted once is reused for as long as it is valid rather than re-minted
    /// on every resolve of this transient typed client. Omitting it gives this instance a private store,
    /// which is the right default for a test that wants an isolated one and the wrong state for a host.
    /// </param>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// WHY THE STORE IS A PARAMETER RATHER THAN A FIELD INITIALISER. A typed HTTP client is registered
    /// TRANSIENT by the framework, so an instance-owned store meant a fresh empty one on every resolve
    /// and a fresh token request behind it - the caching this client documents was, in a host, never
    /// actually reached across calls. Naming the store here moves the guarantee into the type system,
    /// where a composition test can assert it.
    /// </remarks>
    public SecurityClient(
        HttpClient httpClient,
        ILogger<SecurityClient> logger,
        TimeProvider timeProvider,
        IOptions<GatewayOptions>? options = null,
        ServiceTokenCache? tokenCache = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _httpClient = httpClient;
        _logger = logger;
        _options = options;
        _timeProvider = timeProvider;
        _tokenCache = tokenCache ?? new ServiceTokenCache();
    }

    /// <inheritdoc/>
    public async Task<ServiceToken> GetTokenAsync(
        ServiceTokenRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        string cacheKey = BuildCacheKey(request);
        DateTimeOffset now = _timeProvider.GetUtcNow();

        // Strictly in the future. The comparison is deliberately exact, with no margin subtracted: any
        // margin would be an invented duration, and the contract's remedy for a credential that has
        // lapsed is simply to ask for another - which is the branch below.
        if (_tokenCache.Entries.TryGetValue(cacheKey, out ServiceToken? held) && held.ExpiresAt > now)
        {
            _logger.LogTrace(
                "Reusing the held service token for subject {Subject} and audience {Audience}; it "
                + "remains valid until {ExpiresAt:O}.",
                request.Subject,
                request.Audience,
                held.ExpiresAt);

            return held;
        }

        ServiceToken issued = await IssueTokenAsync(request, cancellationToken).ConfigureAwait(false);

        // Last write wins. Both the value replaced and the value stored are valid credentials for the
        // same key, so no ordering guarantee is required here and none is claimed.
        _tokenCache.Entries[cacheKey] = issued;
        return issued;
    }

    /// <summary>
    /// Builds the credential for the issuance operation, or <see langword="null"/> when this deployment
    /// authenticates that edge with a client certificate instead.
    /// </summary>
    /// <param name="subject">
    /// The identity the outbound request claims, which is also the user-id half of the credential.
    /// </param>
    /// <returns>
    /// A <c>Basic</c> header value when a client secret is configured; otherwise <see langword="null"/>,
    /// which leaves the request unheadered so the transport credential is the one presented.
    /// </returns>
    /// <remarks>
    /// <para>
    /// THE USER-ID IS THE CLAIMED SUBJECT, TAKEN FROM THE REQUEST RATHER THAN CONFIGURED SEPARATELY. The
    /// issuance edge reconciles the subject in the body against the identity the credential establishes
    /// and refuses a mismatch with <c>403</c>, so the two must agree; taking both from one value makes
    /// them agree by construction rather than by an operator setting them consistently.
    /// </para>
    /// <para>
    /// ENCODED PER RFC 7617: user-id, a colon, password, base64 of the UTF-8 bytes. The user-id contains
    /// no colon - the specification forbids one there, and every subject in the roster is a plain
    /// identifier - so the receiver's split on the FIRST colon recovers both halves even when the secret
    /// itself contains colons. UTF-8 is the charset Security decodes with, so the two sides agree byte
    /// for byte and a non-ASCII secret survives the round trip.
    /// </para>
    /// <para>
    /// THE SECRET IS READ AT THE MOMENT OF USE AND HELD IN NO FIELD. It arrives through the flat key
    /// <see cref="GatewayOptions.SecurityClientSecretConfigurationKey"/>, applied to the bound options by
    /// an explicit post-configure step in the composition root, so it appears in no settings file and in
    /// no container definition (C-F). It is not logged here or anywhere else and appears in no exception
    /// message: a failure to authenticate is reported by Security as a status, and repeating the secret
    /// into a diagnostic would put it in an operator's log.
    /// </para>
    /// <para>
    /// A DEPLOYMENT WITH NEITHER SCHEME NEVER REACHES HERE. <see cref="GatewayOptions"/> validates the
    /// disjunction on start, so the null returned below always means "the certificate is the credential"
    /// and never "there is no credential at all".
    /// </para>
    /// </remarks>
    private AuthenticationHeaderValue? BuildIssuanceCredential(string subject)
    {
        string secret = _options?.Value.SecurityClientSecret ?? string.Empty;

        if (string.IsNullOrWhiteSpace(secret))
        {
            return null;
        }

        string parameter = Convert.ToBase64String(
            Encoding.UTF8.GetBytes(string.Concat(subject, ":", secret)));

        return new AuthenticationHeaderValue(BasicSchemeToken, parameter);
    }

    /// <summary>
    /// Asks the Security service to mint a token - the ONLY way this client obtains one.
    /// </summary>
    /// <param name="request">The validated request.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The issued credential.</returns>
    /// <remarks>
    /// <para>
    /// NO BEARER CREDENTIAL IS SENT, BECAUSE THE OPERATION DOES NOT ACCEPT ONE - a caller cannot present
    /// a bearer token in order to obtain its first bearer token. The operation declares two ALTERNATIVE
    /// schemes instead, and this method presents whichever the deployment configured: an
    /// <c>Authorization: Basic</c> header built from the claimed subject and the configured client
    /// secret, or - when no secret is configured - the client certificate the composition root attached
    /// to this client's primary handler, which is a transport credential and needs no header.
    /// </para>
    /// <para>
    /// THE BODY CARRIES EXACTLY THE THREE MEMBERS THE SCHEMA DECLARES and never the credential. The
    /// schema sets <c>additionalProperties: false</c>, so a secret placed there would be rejected; more
    /// to the point, a credential in a body is logged by every intermediary that logs bodies.
    /// </para>
    /// </remarks>
    private async Task<ServiceToken> IssueTokenAsync(
        ServiceTokenRequest request,
        CancellationToken cancellationToken)
    {
        // Fail fast, and name the setting that is missing. The base address is the composition root's
        // responsibility; discovering its absence as a relative-URI failure deep inside the transport
        // would report the symptom rather than the cause.
        if (_httpClient.BaseAddress is null)
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"No base address is configured for the Security upstream, so contract C-01's issuance "
                + $"operation cannot be reached. Set '{SecurityAddressConfigurationKey}' and register "
                + $"this typed client with that address in the composition root."));
        }

        _logger.LogDebug(
            "Requesting a service token from Security for subject {Subject} and audience {Audience} "
            + "with {RequestedScopeCount} requested scope(s).",
            request.Subject,
            request.Audience,
            request.Scopes.Count);

        TokenIssuanceRequestBody body = new()
        {
            Subject = request.Subject,
            Audience = request.Audience,
            Scopes = request.Scopes,
        };

        // BUILT AS AN EXPLICIT REQUEST RATHER THAN THROUGH PostAsJsonAsync, for one reason: the
        // credential is a per-request header. Setting it on HttpClient.DefaultRequestHeaders instead
        // would attach it to every request this client ever makes, including ones that must not carry
        // it, and would leave a credential resident on a container-held singleton between calls.
        using HttpRequestMessage httpRequest = new(HttpMethod.Post, TokenPath)
        {
            Content = JsonContent.Create(body, options: WireJson),
            Headers = { Authorization = BuildIssuanceCredential(request.Subject) },
        };

        using HttpResponseMessage response = await _httpClient
            .SendAsync(httpRequest, cancellationToken)
            .ConfigureAwait(false);

        // Every non-success status is a definitive answer on this operation and is surfaced as a typed
        // failure. Nothing is retried here: transient transport faults are the resilience handler's
        // concern, and a 4xx is a refusal that repeating cannot change.
        if (!response.IsSuccessStatusCode)
        {
            throw await CreateFailureAsync(response, cancellationToken).ConfigureAwait(false);
        }

        TokenIssuanceResponseBody? payload;
        try
        {
            payload = await response.Content
                .ReadFromJsonAsync<TokenIssuanceResponseBody>(WireJson, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (JsonException exception)
        {
            // Reaches here when a required member is absent or a member carries the wrong JSON type.
            // The serializer enforces the schema's own required list, so this is contract conformance
            // rather than defensive padding.
            throw new SecurityClientException(
                "The Security service returned a token issuance response that does not match the shape "
                + "contract C-01 publishes.",
                exception)
            {
                StatusCode = (int)response.StatusCode,
            };
        }

        if (payload is null)
        {
            throw new SecurityClientException(
                "The Security service returned an empty body for a successful token issuance response.")
            {
                StatusCode = (int)response.StatusCode,
            };
        }

        ServiceToken token = ToServiceToken(payload, response.StatusCode);

        // The granted scope set may be NARROWER than the requested one. That is a normal successful
        // outcome under this contract, so it is recorded as an observation and never raised as a
        // warning or an error. The token value itself is not logged, here or anywhere else.
        _logger.LogDebug(
            "Security issued a service token for subject {Subject} and audience {Audience}; it expires "
            + "at {ExpiresAt:O} with {GrantedScopeCount} of {RequestedScopeCount} requested scope(s) "
            + "granted.",
            request.Subject,
            request.Audience,
            token.ExpiresAt,
            token.GrantedScopes.Count,
            request.Scopes.Count);

        return token;
    }

    /// <summary>
    /// Converts a conforming response body into an issued token, refusing one that does not conform.
    /// </summary>
    /// <param name="payload">The deserialized response body.</param>
    /// <param name="statusCode">The status the response carried, recorded on any failure raised here.</param>
    /// <returns>The issued credential with an absolute expiry.</returns>
    /// <remarks>
    /// The checks below are contract conformance, not defensive padding, and each corresponds to a
    /// constraint the schema states. Refusing a non-conforming body here is a NARROWING WITH A DEFINED
    /// ERROR rather than a guess: an empty token or a nonsensical lifetime accepted silently would
    /// surface later as an unexplained rejection by whichever service the credential was presented to,
    /// at a point far away from the cause.
    /// </remarks>
    private ServiceToken ToServiceToken(TokenIssuanceResponseBody payload, System.Net.HttpStatusCode statusCode)
    {
        // The schema declares minLength 1 on the token.
        if (payload.AccessToken.Length == 0)
        {
            throw Malformed("carried an empty access token", statusCode);
        }

        // The schema pins the credential type with a constant rather than an enumeration. RFC 6749
        // treats this member case-insensitively, but this contract is stricter than the RFC and the
        // contract is what both ends of this boundary are built to, so the stricter document governs
        // and a divergence is reported instead of being absorbed.
        if (!string.Equals(payload.TokenType, BearerTokenType, StringComparison.Ordinal))
        {
            throw Malformed(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"carried a credential type other than the '{BearerTokenType}' constant the "
                    + $"contract pins"),
                statusCode);
        }

        // The schema declares minimum 1 on the lifetime.
        if (payload.ExpiresIn < 1)
        {
            throw Malformed("carried a token lifetime below the declared minimum of one second", statusCode);
        }

        DateTimeOffset issuedAt;
        if (payload.IssuedAt is long issuedAtSeconds)
        {
            if (issuedAtSeconds < MinIssuedAtSeconds || issuedAtSeconds > MaxIssuedAtSeconds)
            {
                throw Malformed("carried an issuance timestamp outside the representable range", statusCode);
            }

            // Present so a caller can compute an expiry without parsing the token, which is exactly what
            // happens here. Anchoring to the issuer's own clock is preferred over a local reading because
            // the lifetime the response reports is measured from issuance, not from receipt.
            issuedAt = DateTimeOffset.FromUnixTimeSeconds(issuedAtSeconds);
        }
        else
        {
            // The member is optional, so a local reading is the only anchor available.
            issuedAt = _timeProvider.GetUtcNow();
        }

        DateTimeOffset expiresAt;
        try
        {
            expiresAt = issuedAt.AddSeconds(payload.ExpiresIn);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new SecurityClientException(
                "The Security service returned a token issuance response whose lifetime and issuance "
                + "timestamp combine to an expiry outside the representable range.",
                exception)
            {
                StatusCode = (int)statusCode,
            };
        }

        return new ServiceToken(
            payload.AccessToken,
            payload.TokenType,
            expiresAt,
            ParseGrantedScopes(payload.Scope));
    }

    /// <summary>
    /// Builds the failure for a non-success response, projecting the contract's single error body.
    /// </summary>
    /// <param name="response">The unsuccessful response.</param>
    /// <param name="cancellationToken">Cancels reading the error body.</param>
    /// <returns>The typed failure to raise.</returns>
    /// <remarks>
    /// Returns the exception rather than raising it so the call site reads as an explicit
    /// <c>throw</c>, which keeps the control flow of the issuance path visible at the point it changes.
    /// </remarks>
    private static async Task<SecurityClientException> CreateFailureAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        int statusCode = (int)response.StatusCode;
        ProblemDetails? problem = await ReadProblemDetailsAsync(response, cancellationToken)
            .ConfigureAwait(false);

        string summary = problem?.Title is { Length: > 0 } title
            ? string.Create(CultureInfo.InvariantCulture, $" Reported problem: {title}.")
            : string.Empty;

        return new SecurityClientException(string.Create(
            CultureInfo.InvariantCulture,
            $"The Security service refused the token issuance request with HTTP status {statusCode}."
            + $"{summary}"))
        {
            StatusCode = statusCode,
            ProblemType = problem?.Type,
            Title = problem?.Title,
            Detail = problem?.Detail,
            Instance = problem?.Instance,
            RetCode = ReadRetCode(problem),
        };
    }

    /// <summary>
    /// Reads the RFC 9457 problem-details body, tolerating a response that does not carry one.
    /// </summary>
    /// <param name="response">The unsuccessful response.</param>
    /// <param name="cancellationToken">Cancels reading the body.</param>
    /// <returns>The parsed problem details, or <see langword="null"/> when none could be read.</returns>
    /// <remarks>
    /// <para>
    /// The shared framework's own problem-details type is used rather than a hand-authored equivalent.
    /// That is the contract's own reasoning applied on the consuming side: it is the shape ASP.NET Core
    /// emits without hand-written code, so adopting it keeps the error path out of hand-written code
    /// exactly as leaving token validation to the stock bearer handler keeps the security-critical path
    /// out of it. Its extension bag is what carries the contract's single extension member.
    /// </para>
    /// <para>
    /// A missing or unparseable body degrades to <see langword="null"/> rather than masking the
    /// underlying refusal - THE STATUS CODE IS THE SUBSTANTIVE ANSWER, and losing it behind a
    /// deserialization failure would be strictly worse. This body is entirely OPTIONAL enrichment: the
    /// caller already has the refusal, and everything read here only adds detail to it.
    /// </para>
    /// <para>
    /// THREE FAILURE SHAPES ARE ABSORBED, AND THE THIRD IS THE ONE THAT WAS MISSING. A malformed body
    /// raises a JSON fault; a body whose declared media type has no reader raises an unsupported-type
    /// fault; and A BODY WHOSE CHARSET PARAMETER CANNOT BE RESOLVED RAISES AN INVALID-OPERATION FAULT
    /// FROM THE CONTENT READER ITSELF, before any JSON is looked at. That third shape is not exotic -
    /// a proxy or a misconfigured upstream emitting <c>charset=utf8x</c> is enough to produce it - and
    /// leaving it unabsorbed meant a 401 or a 503 was REPLACED by an unrelated encoding complaint, so
    /// the caller was told the wrong thing about its own request. All three now degrade identically,
    /// for the same reason: none of them changes what the status code already said.
    /// </para>
    /// <para>
    /// A CANCELLATION IS DELIBERATELY NOT CAUGHT and propagates unchanged. That distinction is why the
    /// absorbed set is enumerated by type rather than written as a blanket catch: a cancellation is not
    /// a malformed body, it is the caller's own instruction, and swallowing it here would report a
    /// refusal for a request that was abandoned.
    /// </para>
    /// </remarks>
    private static async Task<ProblemDetails?> ReadProblemDetailsAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        string? mediaType = response.Content.Headers.ContentType?.MediaType;
        if (mediaType is not ("application/problem+json" or "application/json"))
        {
            return null;
        }

        try
        {
            return await response.Content
                .ReadFromJsonAsync<ProblemDetails>(WireJson, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (JsonException)
        {
            // The body is not valid JSON, or does not match the shape.
            return null;
        }
        catch (NotSupportedException)
        {
            // The content cannot be read as JSON at all.
            return null;
        }
        catch (InvalidOperationException)
        {
            // The content reader raises this when the response declares a charset it cannot resolve to
            // an encoding. It is a property of the RESPONSE HEADER rather than of the body, so it fires
            // before deserialization and is not covered by either catch above. Absorbed for the same
            // reason they are: the refusal this method is enriching is already in hand.
            return null;
        }
    }

    /// <summary>
    /// Extracts the legacy return code from the error body's single extension member.
    /// </summary>
    /// <param name="problem">The parsed problem details, if any.</param>
    /// <returns>The return code, or <see langword="null"/> when the response carried none.</returns>
    /// <remarks>
    /// Both representations are handled on purpose. Deserialization parks an unmatched member as a
    /// <see cref="JsonElement"/>, while a body constructed directly in a test carries a plain integral
    /// value; accepting either keeps the test seam honest without loosening what the wire may say. A
    /// member that is present but not an integer yields <see langword="null"/> rather than a coerced
    /// value, because inventing a return code would corrupt the very algebra this member exists to
    /// convey.
    /// </remarks>
    private static long? ReadRetCode(ProblemDetails? problem)
    {
        if (problem is null
            || !problem.Extensions.TryGetValue(RetCodeExtensionMember, out object? raw)
            || raw is null)
        {
            return null;
        }

        return raw switch
        {
            JsonElement { ValueKind: JsonValueKind.Number } element when element.TryGetInt64(out long parsed)
                => parsed,
            long value => value,
            int value => value,
            _ => null,
        };
    }

    /// <summary>
    /// Splits the granted scope set from its single space-delimited value.
    /// </summary>
    /// <param name="scope">The <c>scope</c> member exactly as the response carried it.</param>
    /// <returns>The granted scopes, which may legitimately be none.</returns>
    /// <remarks>
    /// An empty value means no requested scope was granted, which the contract states is a successful
    /// outcome; it yields an empty set rather than a failure. Empty entries are removed so that
    /// incidental repeated separators cannot manufacture blank scopes.
    /// </remarks>
    private static string[] ParseGrantedScopes(string scope) => scope.Split(
        ScopeSeparator,
        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>
    /// Composes the key under which an issued credential is held.
    /// </summary>
    /// <param name="request">The request the credential was issued for.</param>
    /// <returns>The cache key.</returns>
    /// <remarks>
    /// <para>
    /// All three members participate, because a credential is valid only for the subject, the single
    /// audience and the scope set it was issued against - keying on the audience alone would hand a
    /// caller a token minted for somewhere else, which is the precise outcome the contract's
    /// one-audience-per-request rule exists to prevent. Scopes are ordered so that the same set
    /// requested in a different order resolves to the same key; the order the caller supplied is
    /// preserved in the request that is actually sent.
    /// </para>
    /// <para>
    /// EVERY COMPONENT IS LENGTH-PREFIXED, INCLUDING EACH SCOPE INDIVIDUALLY, so the key depends on NO
    /// invariant about what a component may contain. The previous encoding joined the components with a
    /// control character on the stated ground that no subject, audience or scope could contain it -
    /// true of this service's own fixed call sites, but never CHECKED anywhere, so the claim was a
    /// convention rather than a control. Two distinct requests colliding on one key is not a cache
    /// inefficiency; it is one caller receiving a credential minted for another audience or another
    /// scope set, which is the exact confusion the contract's one-audience rule exists to prevent.
    /// </para>
    /// <para>
    /// The scope SET is also encoded element by element rather than pre-joined with its RFC 6749
    /// separator, which removes the last place an invariant was relied upon: a scope containing a space
    /// is refused by the request type today, and this key does not care whether it stays refused.
    /// </para>
    /// </remarks>
    private static string BuildCacheKey(ServiceTokenRequest request)
    {
        string[] orderedScopes = [.. request.Scopes];
        Array.Sort(orderedScopes, StringComparer.Ordinal);

        StringBuilder key = new();

        AppendKeyComponent(key, request.Subject);
        AppendKeyComponent(key, request.Audience);

        foreach (string scope in orderedScopes)
        {
            AppendKeyComponent(key, scope);
        }

        return key.ToString();
    }

    /// <summary>
    /// Appends one length-prefixed component to a cache key under construction.
    /// </summary>
    /// <param name="key">The key being built.</param>
    /// <param name="component">The component, whose content is unconstrained.</param>
    /// <remarks>
    /// The encoding is the component's character count, then
    /// <see cref="CacheKeyLengthSeparator"/>, then the component verbatim. A reader consumes the digits
    /// and then exactly that many characters, so a concatenation of these is self-delimiting and the
    /// sequence-to-key mapping is injective for arbitrary content. No component is escaped, rejected or
    /// normalised, because none needs to be: nothing about the content can change where the next
    /// component begins.
    /// </remarks>
    private static void AppendKeyComponent(StringBuilder key, string component)
    {
        key.Append(component.Length.ToString(CultureInfo.InvariantCulture))
            .Append(CacheKeyLengthSeparator)
            .Append(component);
    }

    /// <summary>
    /// Builds the failure raised when a successful response does not conform to the published shape.
    /// </summary>
    /// <param name="problem">A short clause describing what the body carried.</param>
    /// <param name="statusCode">The status the response carried.</param>
    /// <returns>The typed failure to raise.</returns>
    private static SecurityClientException Malformed(string problem, System.Net.HttpStatusCode statusCode) =>
        new(string.Create(
            CultureInfo.InvariantCulture,
            $"The Security service returned a token issuance response that {problem}, which contract "
            + $"C-01 does not permit."))
        {
            StatusCode = (int)statusCode,
        };
}

/// <summary>
/// The request body of contract C-01's issuance operation, in the member spellings the schema declares.
/// </summary>
/// <remarks>
/// Hand-authored, because the OpenAPI document generates no code and no contract type exists to import.
/// It has EXACTLY the three members the schema declares and no extension bag, which is how
/// <c>additionalProperties: false</c> is honoured structurally: there is nowhere for a fourth member -
/// least of all a credential-shaped one - to be added without editing this declaration.
/// </remarks>
internal sealed record TokenIssuanceRequestBody
{
    /// <summary>The identity being claimed. A claim, never a credential.</summary>
    [JsonPropertyName("subject")]
    public required string Subject { get; init; }

    /// <summary>The single intended audience.</summary>
    [JsonPropertyName("audience")]
    public required string Audience { get; init; }

    /// <summary>The requested scope set.</summary>
    [JsonPropertyName("scopes")]
    public required IReadOnlyList<string> Scopes { get; init; }
}

/// <summary>
/// The response body of contract C-01's issuance operation, in the member spellings the schema declares.
/// </summary>
/// <remarks>
/// <para>
/// The four required members carry the RFC 6749 section 5.1 spellings, deliberately, so that a stock
/// client library parses this object without bespoke code - the same property that made this contract
/// REST rather than gRPC. The request half above does NOT follow that RFC, because it is not a grant
/// request; the asymmetry is intentional and belongs to the contract rather than to this file.
/// </para>
/// <para>
/// Declaring the four as <c>required</c> makes the serializer enforce the schema's own required list, so
/// an absent member is refused while reading instead of surfacing later as an empty credential.
/// </para>
/// <para>
/// There is no refresh token here, because the contract defines none anywhere. A caller that needs
/// another token asks for another token.
/// </para>
/// </remarks>
internal sealed record TokenIssuanceResponseBody
{
    /// <summary>The signed token. A CREDENTIAL - never logged and never echoed.</summary>
    [JsonPropertyName("access_token")]
    public required string AccessToken { get; init; }

    /// <summary>The credential type, pinned by the schema to a constant.</summary>
    [JsonPropertyName("token_type")]
    public required string TokenType { get; init; }

    /// <summary>The token lifetime in seconds from issuance.</summary>
    [JsonPropertyName("expires_in")]
    public required long ExpiresIn { get; init; }

    /// <summary>
    /// The GRANTED scope set as a single space-delimited value. An empty value means none was granted.
    /// </summary>
    [JsonPropertyName("scope")]
    public required string Scope { get; init; }

    /// <summary>
    /// The issuance time as seconds since the Unix epoch. Optional, and present so a caller can compute
    /// an expiry without parsing the token.
    /// </summary>
    [JsonPropertyName("issued_at")]
    public long? IssuedAt { get; init; }

    /// <summary>
    /// Describes this body WITHOUT disclosing the credential it carries.
    /// </summary>
    /// <returns>A redacted description.</returns>
    /// <remarks>
    /// A record's generated implementation would print every member, including the token. This override
    /// removes that hazard at the type rather than relying on every future call site to avoid formatting
    /// the object.
    /// </remarks>
    public override string ToString() => string.Create(
        CultureInfo.InvariantCulture,
        $"{nameof(TokenIssuanceResponseBody)} {{ {nameof(AccessToken)} = [REDACTED], "
        + $"{nameof(TokenType)} = {TokenType}, {nameof(ExpiresIn)} = {ExpiresIn}, "
        + $"{nameof(Scope)} = {Scope}, {nameof(IssuedAt)} = {IssuedAt} }}");
}
