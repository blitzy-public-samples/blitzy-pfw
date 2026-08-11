// ==================================================================================================
//  SecurityClient.cs
//  DataServices' only outbound edge to the Security service. It covers TWO published contracts:
//    C-01  security.v1.TokenService   POST /v1/tokens
//    C-02  security.v1.CryptoService  17 operations under /v1/crypto/**
//  ------------------------------------------------------------------------------------------------
//  WHAT THIS FILE IS
//    DataServices reaches Security for exactly two reasons, and this file is both of them.
//
//    (1) THE TOKEN IT PRESENTS ON ITS OWN OUTBOUND CALLS. DataServices calls Persistence, and that
//        edge is authenticated. The token-provider abstraction below is what the sibling
//        Clients/PersistenceClient.cs consumes to attach a bearer credential to its gRPC calls, so
//        the credential path does not couple that client to this one's transport.
//
//    (2) THE CRYPTOGRAPHIC SURFACE. Security holds the keyed cryptographic surface of the legacy
//        framework and DataServices reaches it over contract C-02. This service performs no
//        cryptography of its own; the packages that would allow it to are absent from the project
//        file, deliberately.
//
//  THERE IS NO LEGACY SOURCE FOR THIS FILE, AND THAT IS A FINDING RATHER THAN A GAP (C-C)
//    The legacy PowerFramework is a LIBRARY. It has no process of its own, no listening socket, no
//    route table and no serialization layer, and it has no token concept and no boundary to protect.
//    The cryptographic surface it does have -
//    ws_objects/pfw.crypto.pbl.src/n_crypto.sru, a `nonvisualobject ... native "pfw.dll"` declaring
//    65 members at :L9-L73 - was an in-process object in the same address space, reached through the
//    global auto-instance declared at :L75. There was no client, no address and no failure in
//    transit.
//
//    So nothing here is a port of a client. What IS faithful to the legacy in this file is its
//    VALUES and its SEMANTICS - the constant sets consumed from the shared kernel, the preserved
//    weak defaults annotated below, and the return-code algebra surfaced on the failure type - never
//    a schema. Every legacy path cited in this file was READ and never written to, moved,
//    reformatted or remediated.
//
//  THE WIRE IS DEFINED BY shared/PowerFramework.Contracts/OpenApi/security.v1.yaml, WHICH IS
//  AUTHORITATIVE
//    That document generates NO code. PowerFramework.Contracts compiles exactly one item - its
//    Proto/*.proto files, with GrpcServices="Both" - and packages its OpenAPI definitions as
//    content; its project directory contains zero .cs files and no OpenAPI client generator is
//    referenced anywhere in the repository. VERIFIED RATHER THAN ASSUMED: no
//    PowerFramework.Contracts.Security.V1 type exists to import. Every data type below is therefore
//    hand-authored against the schema, in the schema's exact member spellings, and adding a code
//    generator to avoid that would be an unrequested dependency.
//
//    Where docs/CONTRACTS.md and that document disagree about a request or a response shape, the
//    document governs and the prose is corrected to match it.
//
//  DECISION RECORD (C-K: document every technology-specific and boundary-specific decision)
//
//    (a) WHY THIS CONTRACT IS REST, AND WHY THAT IS NOT A STYLE PREFERENCE.
//        Token issuance and key publication have to speak ordinary HTTP so that a consumer's STOCK
//        Microsoft.AspNetCore.Authentication.JwtBearer handler fetches /.well-known/jwks.json and the
//        OpenID discovery document and self-configures from them with ZERO bespoke code. That keeps
//        the security-critical retrieval path inside framework code instead of hand-written code, on
//        three services rather than one. Choosing gRPC here would have forced a hand-written key-set
//        retrieval implementation into each of those three services - a net INCREASE in hand-written
//        security code, which is the opposite direction from the one an authenticated-boundary
//        requirement pushes in. It is the wrong choice, not merely a less convenient one.
//
//        The cryptographic surface reinforces the same decision from the other side: every one of
//        its operations is a plain request/response with no ordering requirement between calls and
//        nothing to stream. That interface shape is what decided REST for this service, exactly as
//        the ordered event chain of se_cst_dw.sru decided gRPC for this one.
//
//        "REST" names the interface shape, not the scheme. The contract's own server entry declares
//        the secure scheme, and the plaintext loopback address the local bring-up publishes is a
//        development convenience only. THIS CLIENT CHOOSES NEITHER: it issues RELATIVE requests
//        against the base address the composition root configured from
//        DataServices:Security:BaseAddress, so scheme, host and port are all configuration decisions
//        recorded there and in the port map of docs/ARCHITECTURE.md. NO HOSTNAME, PORT OR SCHEME
//        LITERAL APPEARS ANYWHERE IN THIS FILE, deliberately, so that a mechanical audit for one
//        returns nothing and a future hit is therefore worth reading.
//
//    (b) THE TWO /.well-known/* OPERATIONS ARE DELIBERATELY ABSENT FROM THIS CLIENT.
//        Contract C-01 declares three operations. This client implements one. The key-set and
//        discovery publications are NOT implemented here, and their absence is the whole point of
//        (a): they exist so the stock bearer handler consumes them directly, pointed at
//        Authentication:Jwt:Authority. Hand-rolling key-set retrieval, discovery, key parsing, key
//        caching, key rotation, signature verification or claims validation in this file would
//        replace framework code with hand-written security code, which is exactly what the transport
//        decision was made to avoid.
//
//        NOTHING IN THIS FILE FETCHES, PARSES, CACHES OR VALIDATES VERIFICATION MATERIAL, and
//        nothing in it parses, inspects or interprets a token. A token is an OPAQUE STRING that this
//        client obtains and hands on. The two published paths are named in documentation below so a
//        reader can find them; no code reaches them.
//
//    (c) CONTRACT C-10 IS ALSO ABSENT, FOR A DIFFERENT REASON.
//        /health and /v1/ping exist on all four services, including this one, but this service
//        reports its OWN readiness from its own endpoint layer and it is Gateway that aggregates the
//        three upstreams. DataServices probing Security's readiness would duplicate an aggregation
//        that belongs one layer up. No health or ping member appears here.
//
//    (d) RESILIENCE IS REQUIRED BY THE TRANSITION, NOT LAYERED ON TOP OF IT.
//        This client is wrapped by Microsoft.Extensions.Http.Resilience handlers registered on the
//        typed client in the composition root, from DataServices:Resilience:Security. That
//        dependency must not read as scope creep against the minimal-change clause, so the
//        justification is stated plainly: AN IN-PROCESS CALL CANNOT FAIL IN TRANSIT AND A NETWORK
//        CALL CAN. Every call this file makes was an in-process method call on the global
//        n_crypto auto-instance in the legacy library. Without retry and circuit breaking the first
//        transient network fault would surface as a defect the legacy COULD NOT HAVE HAD, which is a
//        regression introduced by the refactor rather than a preserved behaviour. Handling a failure
//        mode that decomposition itself creates is therefore required BY the transition.
//
//        Retry belongs to transient TRANSPORT faults only. A 4xx from any operation here is a
//        definitive answer - a malformed request, an unauthenticated caller, or a reference the
//        caller may not use or that does not exist - and retrying it would turn a clear refusal into
//        a loop. This file consequently raises a typed failure on any non-success status and retries
//        nothing itself.
//
//        NO LATENCY, THROUGHPUT, SCALABILITY OR AVAILABILITY PROPERTY IS CLAIMED OR IMPLIED BY ANY
//        OF THIS, here or anywhere else in this file. The repository publishes no service-level
//        agreement, no latency budget, no throughput target and no availability commitment, so none
//        may be asserted. The same applies to credential reuse in (h): it is the contract's own
//        stated remedy for a short-lived credential, not an optimization.
//
//    (e) RAW KEY MATERIAL NEVER CROSSES THE WIRE FROM A CALLER. THIS IS THE CENTRAL DECISION OF THE
//        C-02 HALF OF THIS FILE.
//        The legacy n_crypto declares 63 cryptographic overloads and EVERY keyed one takes raw key
//        bytes as an ordinary parameter - `readonly string key` and `readonly blob key` on the four
//        keyed Hash overloads at :L23-L26 and on all 32 symmetric overloads at :L30-L61,
//        `readonly string pubkey` / `readonly string prikey` as PEM text on the RSA operations at
//        :L62-L73, and `ref string prikey` / `ref string pubkey` as OUT-PARAMETERS on key generation
//        at :L19-L20. An agent who ported those signatures literally would put private keys on the
//        wire.
//
//        The boundary therefore takes an OPAQUE keyRef that Security resolves against its own
//        configured key store, an ivRef in the position the legacy took an initialization vector,
//        and a fileRef in the position the three HashFile overloads at :L27-L29 took a FILENAME. No
//        request type in this file has a member a key, an initialization vector, a passphrase, a
//        password or a certificate could be placed in, and no request schema permits one either -
//        every one of them sets additionalProperties: false. The rule is absolute and extends past
//        the body: no key material in a header, a query string, a URL segment, a log record or an
//        exception message.
//
//        Three consequences are real NARROWINGS of the legacy's reach and are recorded as such
//        rather than presented as equivalence. Key generation does not return the private key, only
//        the public key and a reference to the private one. File hashing reaches only files the
//        server has been configured to expose, where the legacy reached any file its process could
//        open. And a caller cannot supply its own key bytes at all.
//
//    (f) NO SECRET, NO CREDENTIAL AND NO KEY MATERIAL APPEARS IN THIS FILE IN ANY FORM (C-F).
//        Not a PEM block, not a private or public key literal, not a passphrase, not an
//        initialization vector literal, not a bearer-token literal, not a certificate, and not a
//        keyRef, ivRef or fileRef value. There is no example value of any of them either, because a
//        plausible-looking placeholder is indistinguishable from a real credential to a reader and
//        placeholder keys have a long history of reaching production unchanged. All such material
//        arrives at runtime - configuration for the transport, an opaque reference for the key
//        store.
//
//        THE NAMED ANTI-PATTERN THIS FILE REPLACES is the read-only legacy browser asset
//        tests/blink/test_jws.htm:L8-L23, which embeds a plaintext RSA private key in a script
//        variable and signs a token with it in the client. It was inspected structurally only. NO
//        VALUE FROM IT IS REPRODUCED HERE IN ANY FORM, and it was not edited, moved or reformatted.
//
//    (g) DATASERVICES VALIDATES TOKENS; IT NEVER MINTS THEM. THIS IS ABSOLUTE (C-G).
//        Security is the sole minter in the system and exactly one signing key exists anywhere in
//        it, held by Security alone and supplied to it by configuration injection. DataServices
//        holds VERIFICATION MATERIAL ONLY. When this client needs a service token it ASKS SECURITY
//        TO MINT ONE over the operation below; it never signs locally.
//
//        Accordingly this file contains no signing key, no private key, no signing credential, no
//        token handler construction and no token-creation call of any kind, and it names no signing
//        secret. That is enforced at the package level rather than by review alone: the minting
//        library Microsoft.IdentityModel.JsonWebTokens is deliberately absent from
//        PowerFramework.DataServices.csproj, so reaching for it does not compile. A compile error
//        there is the design working, not an obstacle to route around.
//
//        Inbound validation on this service's own boundary belongs to the stock bearer handler
//        registered in the composition root. It is not this file's concern and is not duplicated
//        here.
//
//    (h) THE TWO EDGES ARE AUTHENTICATED DIFFERENTLY, AND WHICH ONE APPLIES IS DECIDED BY THE
//        OPERATION - NEVER BY A DEFAULT.
//
//        ISSUANCE (C-01, POST /v1/tokens) is authenticated BY A CALLER CREDENTIAL and carries no
//        BEARER credential. That operation declares two security schemes - clientCredential and
//        mutualTls - and OVERRIDES the document-level bearer requirement with them. The reason is
//        structural rather than a preference: A CALLER CANNOT PRESENT A BEARER TOKEN IN ORDER TO OBTAIN
//        ITS FIRST BEARER TOKEN.
//
//        WHICH OF THE TWO THIS FILE PRESENTS IS DECIDED BY CONFIGURATION, NOT BY A DEFAULT. When an
//        issuance secret is configured it sends an HTTP Basic credential whose user-id is TokenSubject
//        below and whose password is that secret, because a client certificate exists only inside a TLS
//        handshake and a proxy or sidecar terminating TLS ahead of Security strips it - so on such a
//        documented topology Basic is the only scheme that can reach the service at all. When no secret
//        is configured it sends no Authorization header and relies on the certificate the composition
//        root attached to the primary handler, which is the mutualTls path for a deployment that
//        terminates TLS at Security. Configuring neither is refused at startup by
//        DataServicesOptions.cs's validator rather than discovered here, because a service that can
//        obtain no token can reach nothing downstream.
//
//        Two consequences this file honours exactly on that edge whichever scheme applies. The
//        credential travels in the Authorization HEADER or in the handshake, never in the body: the
//        schema carries no client secret, password, API key, assertion or key material, so the body
//        this file sends has exactly the three members the schema declares and a proxy log records a
//        header name rather than a body value. And the secret is never logged, never echoed into a
//        diagnostic and never included in any exception message.
//
//        THE SEVENTEEN C-02 CRYPTO OPERATIONS ARE THE OTHER EDGE, AND EVERY ONE OF THEM CARRIES A
//        BEARER CREDENTIAL. They do not override the document-level security, so the bearer
//        requirement applies to all of them; an unauthenticated crypto call is therefore not merely
//        unwise but unusable, because once Security enforces its own contract every one of them
//        answers 401 and the whole cryptographic surface becomes unreachable. This client obtains a
//        credential for Security's OWN audience, scoped to the cryptographic capability, through the
//        same cached GetTokenAsync path its external callers use - so the internal credential gets
//        the same expiry comparison and the same never-logged treatment as every other, rather than a
//        second credential path to keep in step.
//
//        THE SPLIT IS ENFORCED BY TWO DIFFERENTLY NAMED SENDERS RATHER THAN BY A FLAG. Routing
//        issuance through the authenticated sender would not merely be wrong on the wire: it would
//        recurse without bound, because acquiring the credential calls issuance. A name makes that
//        visible at the call site; a boolean parameter with a default would not. The credential is
//        carried on a PER-CALL request message and never on the shared client's default headers,
//        because a typed client instance serves concurrent requests and its default headers are
//        instance-wide mutable state.
//
//        The contract declares no grant type, no authorization endpoint, no token-endpoint client
//        authentication and no refresh token anywhere. Its stated remedy for needing another token
//        is to CALL THE OPERATION AGAIN over the same mutually authenticated channel. This client
//        therefore reuses a credential only while it remains valid and issues a fresh request once
//        it does not - which IS that remedy - and adds no refresh grant, no forced-invalidation
//        member and no retry-on-401 re-authentication loop.
//
//    (i) THE C-02 SURFACE HAS NO CALLER INSIDE THIS SERVICE TODAY, AND THAT IS NOT DEAD CODE.
//        Stated explicitly so a later reader does not mistake the absence of call sites for an
//        oversight in either direction. A repository-wide search establishes that the legacy
//        n_crypto is not used ANYWHERE in ws_objects/pfw.datawindow.services.pbl.src/,
//        ws_objects/pfw.thread.pbl.src/ or ws_objects/pfw.thread.ext.pbl.src/ - the three libraries
//        this service and its downstream are ported from. Its only legacy consumers in the whole
//        repository are a test object, a demo object, the constant declarations themselves, and the
//        DEFERRED outbound-payment objects.
//
//        Both possible over-corrections are refused. The client is NOT omitted, because the contract
//        inventory declares C-02 with Security serving DataServices and the folder layout assigns
//        this file to carry it. And no call site is FABRICATED for it, because inventing a consumer
//        would be a new feature. Its correctness bar is therefore CONFORMANCE TO THE PUBLISHED
//        SCHEMA plus its own unit tests, not call-site behavioural parity - there is no legacy call
//        site in this service to be in parity with.
//
//    (j) THE DUPLICATION WITH GATEWAY'S OWN SECURITY CLIENT IS REQUIRED, NOT A DEFECT.
//        Gateway has its own client for C-01 in its own project. The published contract is the ONLY
//        permitted cross-service coupling (C-A), so a shared client library between two services
//        would be exactly the behavioural back door that constraint forbids. Neither file references
//        the other, neither references any type from another service's project, and the resulting
//        overlap is the cost of the constraint rather than something to be tidied away.
//
//  PRESERVED LEGACY WEAKNESSES - ANNOTATED, NEVER CORRECTED (C-B)
//    Correcting a legacy default would violate the behaviour-preservation mandate, which forbids
//    improvements exactly as firmly as regressions. ANNOTATION IS THE WHOLE REMEDIATION. Each is
//    annotated again at its own point of use below, with its locator:
//      1. ECB is the default symmetric mode [enums.sru:L946], so a mode-omitting call runs in ECB.
//      2. PKCS#1 v1.5 is the default RSA padding [enums.sru:L951].
//      3. No-padding is NOT selectable: the legacy declares no such constant, the enumeration has
//         exactly two members, and a request for it is refused with a defined error.
//      4. PKCS#5-family symmetric padding only, and not selectable - not one of the 32 symmetric
//         overloads at n_crypto.sru:L30-L61 has a padding parameter.
//      5. No key-derivation function is reachable at all - no PBKDF2, scrypt, bcrypt or Argon2, and
//         no salt concept anywhere - so key material is used as raw key bytes.
//      6. No authenticated encryption - the mode set is exactly ECB, CBC and CFB
//         [enums.sru:L943-L945] - so ciphertext carries no integrity tag.
//      7. 1024-bit RSA remains a legal key size [enums.sru:L965] and is not removed from the
//         accepted set.
//      8. ONE hash set is DECLARED for the unkeyed hash, the keyed hash AND the RSA signature hash
//         - the legacy comment at enums.sru:L927 reads
//         "(n_crypto::Hash/RSASign/VerifyRSASign:[ntype])" - so MD5 is a legal signature-hash
//         selector and stays one. CRC32 is the single member the keyed and signing operations do
//         NOT accept, and that is an ABSENT CONSTRUCTION rather than a policy choice: a checksum
//         has no compression function for a keyed digest to key and no algorithm identifier for a
//         signature scheme to name, so HMAC-CRC32 and RSA-over-CRC32 were never defined. It stays
//         fully legal for the two UNKEYED digest operations, where computing it is well defined.
//         The published contract carries the same split: CryptoHashType for the unkeyed pair,
//         CryptoKeyedHashType - the same identifiers minus the checksum - for the other four.
//    Items 3, 4, 5 and 6 are established by ABSENCE: there is no constant to select and no signature
//    that accepts one. A capability the legacy cannot express is one this client must not offer,
//    because offering it would be a new feature.
//
//  WHAT THIS FILE DELIBERATELY DOES NOT DO
//    Recorded so each absence reads as a decision, and so nobody "completes" this file by adding one.
//      * It does not construct an HttpClient, a message handler or a resilience pipeline. It takes a
//        configured HttpClient by constructor injection, which is what lets a test replace the
//        transport and what keeps the composition root's registration authoritative.
//      * It does not set a base address or a timeout, and it contains no hostname, no port and no
//        scheme literal. The composition root sets the address from
//        DataServices:Security:BaseAddress; this file issues relative requests and fails fast with a
//        message naming that key when no address was configured.
//      * It reads no environment variable, and it reads no configuration to decide WHERE to send a
//        request. The one place it consults the bound options at all is that fail-fast diagnostic,
//        and the reason is recorded there.
//      * It performs NO network input or output during construction, in a static initializer, or in
//        any eager warm-up path. Startup must never require Security to be reachable.
//      * It holds no storage, no database context, no connection string, no SQL and no file-backed
//        persistence of tokens or keys (C-E). Exactly one service in this system holds a storage
//        provider and it is not this one; the packages that would allow otherwise are absent from the
//        project file.
//      * It performs no cryptography. There is no hashing, no encryption, no signing, no random
//        generation and no encoding of key material anywhere in it - every such operation is reached
//        over C-02. The only encoding it performs is the base64 JSON TRANSPORT of a payload, which is
//        a property of JSON rather than a cryptographic operation.
//      * It declares no client, partial client, placeholder, interface, constant or option for any
//        capability deferred out of this phase (C-D). Note that the deferred outbound-payment objects
//        are legacy crypto consumers; that thread is deliberately not followed into scope.
//      * It scaffolds no signing, verification or mutual-TLS setting for any service, and names no
//        secret (C-L).
//      * It declares no constant in the preserved SCREAMING_SNAKE spelling. Those spellings are legal
//        only in the files the repository-root .editorconfig scopes its CA1707 and IDE1006
//        suppressions to, and no file of this service's client layer is among them. The constant
//        catalogue and the return-code catalogue are CONSUMED from the shared kernel instead, which
//        is where they belong.
//      * It logs no request body and no response body for any C-02 operation, and never the token.
//        Plaintext, ciphertext, digests, signatures, generated keys and every opaque reference are
//        all treated as sensitive.
// ==================================================================================================

using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using PowerFramework.DataServices.Configuration;
using PowerFramework.Shared.Kernel;

namespace PowerFramework.DataServices.Clients;

/// <summary>
/// Supplies the short-lived service token DataServices presents on its own outbound calls. Contract
/// <b>C-01</b>, <c>security.v1.TokenService</c>.
/// </summary>
/// <remarks>
/// <para>
/// This abstraction exists for two reasons and is kept deliberately small so it serves only those
/// two. First, <c>Clients/PersistenceClient.cs</c> depends on it rather than on the concrete HTTP
/// client, so attaching a bearer credential to an outbound gRPC call does not couple that client to
/// this one's transport. Second, it is the substitution seam: a test replaces this interface with a
/// fake and exercises the whole outbound path with no Security instance running anywhere.
/// </para>
/// <para>
/// Every newly created boundary in this system is authenticated from the outset, INCLUDING the
/// internal ones. The legacy framework opened no listening socket and received no unsolicited
/// request, so decomposition creates every one of these edges from nothing; an internal edge that
/// trusted its caller because "it is internal" would be a new unauthenticated surface, which is
/// precisely what that requirement exists to prevent. This interface is how DataServices satisfies it
/// on the edge it originates towards Persistence.
/// </para>
/// <para>
/// It is NOT a general-purpose authentication framework, and it must not grow into one. There is
/// exactly one member. There is no token validation here, no key retrieval, no discovery, no token
/// parsing, no refresh grant and no signing capability of any kind - DataServices validates inbound
/// tokens with the stock bearer handler and mints nothing at all.
/// </para>
/// </remarks>
public interface IServiceTokenProvider
{
    /// <summary>
    /// Obtains a service token for the supplied subject, audience and requested scope set, reusing a
    /// credential this provider already holds while it remains valid.
    /// </summary>
    /// <param name="request">
    /// The identity being claimed, the single intended audience, and the requested scopes. The
    /// audience is deliberately singular: the contract carries one audience per request so that a
    /// token is never valid somewhere its holder did not intend, and a caller needing tokens for two
    /// audiences asks twice.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels the request. It is threaded through every awaited call on the path and is never
    /// swallowed - the target-side expression of the legacy's cross-thread proxy pattern, in which a
    /// caller-side proxy and a worker-side implementation existed as a pair so that no object was
    /// ever touched from two threads.
    /// </param>
    /// <returns>
    /// The issued credential together with its absolute expiry and the scope set that was actually
    /// GRANTED. The granted set may be narrower than the requested one; that is a normal successful
    /// outcome rather than a failure, so a caller must read it from the result instead of assuming
    /// its request was honoured in full.
    /// </returns>
    /// <remarks>
    /// The return type is a <see cref="ValueTask{TResult}"/> rather than a <see cref="Task{TResult}"/>
    /// because this member genuinely has a synchronous completion path: when a held credential is
    /// still valid it answers without any transport call at all. The cryptographic members of
    /// <see cref="ICryptoServiceClient"/> return <see cref="Task{TResult}"/> instead, because every
    /// one of them always reaches the network and so never completes synchronously. The distinction
    /// describes the shape of each member and asserts nothing about cost.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="SecurityClientException">
    /// The Security service refused the request, or answered in a shape the contract does not permit.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// No upstream address was configured for the Security service.
    /// </exception>
    /// <exception cref="HttpRequestException">The request could not be completed in transit.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    ValueTask<ServiceToken> GetTokenAsync(ServiceTokenRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// The cryptographic surface of the legacy framework as DataServices reaches it. Contract
/// <b>C-02</b>, <c>security.v1.CryptoService</c>: seventeen operations covering all sixty-three
/// cryptographic overloads of <c>ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L11-L73</c>.
/// </summary>
/// <remarks>
/// <para>
/// This interface exists as the substitution seam for the cryptographic half of this client, in the
/// same way <see cref="IServiceTokenProvider"/> is the seam for the issuance half. A test substitutes
/// it, or substitutes the message handler underneath the concrete client; either way no Security
/// instance is required.
/// </para>
/// <para>
/// HOW SIXTY-THREE OVERLOADS COLLAPSE ONTO SEVENTEEN MEMBERS WITHOUT LOSING A SEMANTIC DISTINCTION.
/// The legacy has parallel <c>string</c> and <c>blob</c> overload families throughout, and its return
/// form FOLLOWS its input form: the string-shaped symmetric overloads at <c>n_crypto.sru:L30-L37</c>
/// return <c>string</c> while the blob-shaped ones at <c>:L38-L45</c> return <c>blob</c>. That whole
/// distinction is carried by <see cref="CryptoPayload"/>, which records which family a value belongs
/// to, so choosing a family remains the caller's decision and is never made silently on its behalf.
/// The key and initialization-vector halves of the same cross-product disappear for a different
/// reason: they became opaque references, so there is no longer a string-versus-blob choice to make
/// about them. Sixteen symmetric overloads per direction therefore reach one member each with nothing
/// lost.
/// </para>
/// <para>
/// RAW KEY MATERIAL NEVER CROSSES THE WIRE FROM A CALLER. Every keyed member below takes an opaque
/// <c>keyRef</c> in the position the legacy took key bytes, an <c>ivRef</c> where it took an
/// initialization vector, and a <c>fileRef</c> where it took a filename. None of those references
/// carries material, none is meaningful outside the Security service, and none may be parsed,
/// constructed or inferred by a caller - they are configured on the Security side and handed to this
/// service as opaque values. There is no member on any type in this file that key bytes, a PEM block,
/// a passphrase, a password or a certificate could be placed in.
/// </para>
/// <para>
/// NO BODY IS EVER LOGGED BY THE IMPLEMENTATION OF ANY MEMBER BELOW. Plaintext, ciphertext, digests,
/// signatures, generated keys and every opaque reference are treated as sensitive without exception.
/// </para>
/// <para>
/// THE LEGACY'S WEAK DEFAULTS ARE PRESERVED AS DEFAULTS AND ANNOTATED, NEVER CORRECTED. The
/// annotations appear on the members they affect. No member offers a key-derivation function, an
/// authenticated-encryption mode, a selectable block padding, an additional RSA padding value or a
/// minimum key size, because the legacy surface expresses none of them and offering one would be a
/// new feature rather than a hardening.
/// </para>
/// </remarks>
public interface ICryptoServiceClient
{
    /// <summary>
    /// Computes an UNKEYED digest. <c>POST /v1/crypto/hash</c>, covering both legacy overloads at
    /// <c>n_crypto.sru:L21-L22</c>.
    /// </summary>
    /// <param name="data">The payload to digest, in either legacy overload family.</param>
    /// <param name="hashType">
    /// The hash selector, from <see cref="Enums"/>: <c>CRYPTO_HASH_MD5</c> (0) through
    /// <c>CRYPTO_HASH_CRC32</c> (5) [enums.sru:L928-L933].
    /// <para>
    /// PRESERVED LEGACY WEAKNESS 8. The legacy comment at <c>enums.sru:L927</c> reads
    /// "(n_crypto::Hash/RSASign/VerifyRSASign:[ntype])", declaring one set for the unkeyed hash, the
    /// keyed hash and the RSA signature hash alike. THIS operation accepts all six members:
    /// <c>CRYPTO_HASH_MD5</c> is weak and remains legal, and <c>CRYPTO_HASH_CRC32</c> is a checksum
    /// rather than a cryptographic hash yet computing it is perfectly well defined. Both are
    /// preserved quirks, not defects to fix.
    /// </para>
    /// <para>
    /// THE KEYED AND SIGNING OPERATIONS ACCEPT FIVE, NOT SIX. <c>CRYPTO_HASH_CRC32</c> has no keyed
    /// or signed form to implement - see <see cref="HmacAsync"/> and <see cref="RsaSignAsync"/>.
    /// </para>
    /// </param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>
    /// The digest as the legacy string-shaped overloads return it. The result form does NOT follow
    /// the payload form here, and that asymmetry is legacy behaviour: ALL SIX legacy hash overloads
    /// return <c>string</c>, so a blob payload still yields a string digest. It is preserved rather
    /// than harmonised.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="data"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="hashType"/> is not a declared member.</exception>
    /// <exception cref="SecurityClientException">The service refused the request or answered off-contract.</exception>
    /// <exception cref="InvalidOperationException">No upstream address was configured.</exception>
    Task<string> HashAsync(CryptoPayload data, long hashType, CancellationToken cancellationToken);

    /// <summary>
    /// Computes a KEYED digest. <c>POST /v1/crypto/hmac</c>, covering all four legacy overloads at
    /// <c>n_crypto.sru:L23-L26</c>.
    /// </summary>
    /// <param name="data">The payload to digest, in either legacy overload family.</param>
    /// <param name="keyRef">
    /// An opaque handle to key material held in the Security service's own configured key store.
    /// <para>
    /// The legacy declares these four overloads under THE SAME METHOD NAME as the unkeyed pair - they
    /// are <c>Hash</c> with a key argument, not a separately named <c>Hmac</c> - and the four are the
    /// string-versus-blob cross-product of the payload and THE KEY. The key half of that
    /// cross-product disappears here because a caller no longer supplies key bytes in any form.
    /// </para>
    /// <para>
    /// PRESERVED LEGACY WEAKNESS 5. No key-derivation function is reachable anywhere in the legacy
    /// surface - no PBKDF2, scrypt, bcrypt or Argon2 - and there is no salt concept, so key material
    /// is used as raw key bytes. This reference names material; it does not derive one.
    /// </para>
    /// </param>
    /// <param name="hashType">The hash selector. See <see cref="HashAsync"/> for weakness 8.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The digest, which is not a credential and carries no key material.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="data"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="keyRef"/> is empty or whitespace.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="hashType"/> is not a declared member.</exception>
    /// <exception cref="SecurityClientException">
    /// The service refused the request or answered off-contract. A reference that resolves but may not
    /// be used answers <c>403</c>; one that does not exist answers <c>404</c>. The two are
    /// deliberately distinguished so a caller can tell a configuration mistake from an authorization
    /// one, and neither response echoes any part of the stored material.
    /// </exception>
    Task<string> HmacAsync(CryptoPayload data, string keyRef, long hashType, CancellationToken cancellationToken);

    /// <summary>
    /// Computes an UNKEYED digest of a server-resolved file. <c>POST /v1/crypto/hash-file</c>,
    /// covering the legacy overload at <c>n_crypto.sru:L27</c>.
    /// </summary>
    /// <param name="fileRef">
    /// An opaque handle the Security service resolves against its own configured, allow-listed store.
    /// <para>
    /// A DELIBERATE NARROWING, RECORDED RATHER THAN PRESENTED AS EQUIVALENCE. The legacy overload
    /// takes a FILENAME and hashes any file its process could open. Accepting a caller-supplied
    /// server-side path over HTTP would be an arbitrary-file-read primitive the in-process legacy
    /// could not have had, so the boundary takes a reference exactly as it does for a key. THE CALLER
    /// NEVER SENDS A PATH, A FILENAME, A DIRECTORY OR A URL, so there is no path traversal to defend
    /// against - there is no path. What narrows is the REACH: this reaches only files the server has
    /// been configured to expose.
    /// </para>
    /// </param>
    /// <param name="hashType">The hash selector. See <see cref="HashAsync"/> for weakness 8.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The digest.</returns>
    /// <exception cref="ArgumentException"><paramref name="fileRef"/> is empty or whitespace.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="hashType"/> is not a declared member.</exception>
    /// <exception cref="SecurityClientException">The service refused the request or answered off-contract.</exception>
    Task<string> HashFileAsync(string fileRef, long hashType, CancellationToken cancellationToken);

    /// <summary>
    /// Computes a KEYED digest of a server-resolved file. <c>POST /v1/crypto/hmac-file</c>, covering
    /// both legacy overloads at <c>n_crypto.sru:L28-L29</c>.
    /// </summary>
    /// <param name="fileRef">An opaque file handle. See <see cref="HashFileAsync"/> for the narrowing.</param>
    /// <param name="keyRef">An opaque key handle. See <see cref="HmacAsync"/> for weakness 5.</param>
    /// <param name="hashType">The hash selector. See <see cref="HashAsync"/> for weakness 8.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The digest.</returns>
    /// <exception cref="ArgumentException">Either reference is empty or whitespace.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="hashType"/> is not a declared member.</exception>
    /// <exception cref="SecurityClientException">The service refused the request or answered off-contract.</exception>
    Task<string> HmacFileAsync(
        string fileRef,
        string keyRef,
        long hashType,
        CancellationToken cancellationToken);

    /// <summary>
    /// Encrypts a payload with a symmetric cipher. <c>POST /v1/crypto/symmetric/encrypt</c>, covering
    /// all SIXTEEN legacy overloads at <c>n_crypto.sru:L30-L45</c>.
    /// </summary>
    /// <param name="data">The plaintext, in either legacy overload family.</param>
    /// <param name="keyRef">An opaque key handle. See <see cref="HmacAsync"/> for weakness 5.</param>
    /// <param name="cipherType">
    /// The cipher selector, from <see cref="Enums"/>: <c>CRYPTO_SYMCRYPT_TYPE_DES</c> (0),
    /// <c>CRYPTO_SYMCRYPT_TYPE_3DES</c> (1), <c>CRYPTO_SYMCRYPT_TYPE_AES128</c> (2),
    /// <c>CRYPTO_SYMCRYPT_TYPE_AES192</c> (3) or <c>CRYPTO_SYMCRYPT_TYPE_AES256</c> (4)
    /// [enums.sru:L936-L940]. The legacy types this argument as a 16-bit <c>readonly uint</c>
    /// [n_crypto.sru:L30] while typing the mode beside it as <c>readonly long</c>; the published
    /// boundary normalizes both to one integer and preserves the VALUES and the identifier spellings
    /// rather than the widths.
    /// </param>
    /// <param name="ivRef">
    /// An opaque handle to an initialization vector, or <see langword="null"/> to reach the legacy
    /// overloads that take no vector at all. PASSING <see langword="null"/> OMITS THE MEMBER FROM THE
    /// REQUEST rather than sending an empty one, because "no vector supplied" and "a vector supplied"
    /// select different legacy overloads and collapsing them would lose a semantic distinction.
    /// </param>
    /// <param name="mode">
    /// The cipher mode, or <see langword="null"/> to reach the legacy overloads that take no mode.
    /// <para>
    /// PRESERVED LEGACY WEAKNESS 1. Omitting the mode runs in ECB:
    /// <c>CRYPTO_SYMCRYPT_MODE_DEFAULT</c> is defined as <c>CRYPTO_SYMCRYPT_MODE_ECB</c>
    /// [enums.sru:L946]. That default is preserved and annotated, never strengthened. As with
    /// <paramref name="ivRef"/>, <see langword="null"/> OMITS the member so that a defaulted mode
    /// stays distinguishable from an explicitly requested ECB.
    /// </para>
    /// <para>
    /// PRESERVED LEGACY WEAKNESS 6. The mode set is exactly ECB (0), CBC (1) and CFB (2)
    /// [enums.sru:L943-L945]. There is no authenticated mode - no GCM, CCM or Poly1305 - so
    /// ciphertext carries NO INTEGRITY TAG. PRESERVED LEGACY WEAKNESS 4: block padding is
    /// PKCS#5-family only and is not selectable, which is established by absence - not one of the
    /// thirty-two symmetric overloads has a padding parameter.
    /// </para>
    /// </param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>
    /// The ciphertext in the SAME overload family as the plaintext, because the legacy return type
    /// follows its input type. A string-shaped result already carries a printable, text-safe encoded
    /// form of the binary output, which is why it can be fed straight back to the string-shaped
    /// inverse.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="data"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A supplied reference is empty or whitespace.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="cipherType"/>, or a supplied <paramref name="mode"/>, is not a declared member.
    /// </exception>
    /// <exception cref="SecurityClientException">The service refused the request or answered off-contract.</exception>
    Task<CryptoPayload> SymmetricEncryptAsync(
        CryptoPayload data,
        string keyRef,
        long cipherType,
        string? ivRef,
        long? mode,
        CancellationToken cancellationToken);

    /// <summary>
    /// Decrypts a payload with a symmetric cipher. <c>POST /v1/crypto/symmetric/decrypt</c>, covering
    /// all SIXTEEN legacy overloads at <c>n_crypto.sru:L46-L61</c>.
    /// </summary>
    /// <param name="data">
    /// The ciphertext, in the same overload family it was produced in. A string-shaped ciphertext
    /// belongs to the string-shaped family and a blob-shaped one to the blob-shaped family; feeding a
    /// value to the other family is the mistake <see cref="CryptoPayload"/> exists to make visible.
    /// </param>
    /// <param name="keyRef">An opaque key handle. See <see cref="HmacAsync"/> for weakness 5.</param>
    /// <param name="cipherType">The cipher selector. See <see cref="SymmetricEncryptAsync"/>.</param>
    /// <param name="ivRef">An opaque vector handle, or <see langword="null"/> to omit it.</param>
    /// <param name="mode">
    /// The cipher mode, or <see langword="null"/> to omit it. PRESERVED LEGACY WEAKNESSES 1, 4 AND 6
    /// apply exactly as on <see cref="SymmetricEncryptAsync"/>: ECB is the default, padding is not
    /// selectable, and no mode is authenticated - so a decryption that succeeds proves nothing about
    /// the integrity of the ciphertext it consumed.
    /// </param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The plaintext in the same overload family as the ciphertext.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="data"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A supplied reference is empty or whitespace.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="cipherType"/>, or a supplied <paramref name="mode"/>, is not a declared member.
    /// </exception>
    /// <exception cref="SecurityClientException">The service refused the request or answered off-contract.</exception>
    Task<CryptoPayload> SymmetricDecryptAsync(
        CryptoPayload data,
        string keyRef,
        long cipherType,
        string? ivRef,
        long? mode,
        CancellationToken cancellationToken);

    /// <summary>
    /// Encrypts a payload with an RSA public key. <c>POST /v1/crypto/rsa/encrypt</c>, covering all
    /// four legacy overloads at <c>n_crypto.sru:L62-L65</c>.
    /// </summary>
    /// <param name="data">The plaintext, in either legacy overload family.</param>
    /// <param name="keyRef">
    /// An opaque handle standing in the position of the legacy <c>readonly string pubkey</c>, which
    /// took PEM text directly.
    /// </param>
    /// <param name="padding">
    /// The padding selector, or <see langword="null"/> to reach the legacy overloads that take none.
    /// <para>
    /// PRESERVED LEGACY WEAKNESS 2. Omitting it selects PKCS#1 v1.5:
    /// <c>CRYPTO_RSA_PADDING_DEFAULT</c> is defined as <c>CRYPTO_RSA_PADDING_PKCS1</c>
    /// [enums.sru:L951]. PRESERVED LEGACY WEAKNESS 3: the enumeration has EXACTLY TWO MEMBERS,
    /// <c>CRYPTO_RSA_PADDING_PKCS1</c> (0) and <c>CRYPTO_RSA_PADDING_OAEP</c> (1)
    /// [enums.sru:L949-L950]. There is no no-padding member because the legacy declares no such
    /// constant, so a request for it is REFUSED WITH A DEFINED ERROR rather than added - any value
    /// outside those two is rejected before the request is sent.
    /// </para>
    /// </param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The ciphertext in the same overload family as the plaintext.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="data"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="keyRef"/> is empty or whitespace.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A supplied <paramref name="padding"/> is not one of the two declared members.
    /// </exception>
    /// <exception cref="SecurityClientException">The service refused the request or answered off-contract.</exception>
    Task<CryptoPayload> RsaEncryptAsync(
        CryptoPayload data,
        string keyRef,
        long? padding,
        CancellationToken cancellationToken);

    /// <summary>
    /// Decrypts a payload with an RSA private key. <c>POST /v1/crypto/rsa/decrypt</c>, covering all
    /// four legacy overloads at <c>n_crypto.sru:L66-L69</c>.
    /// </summary>
    /// <param name="data">The ciphertext, in the overload family it was produced in.</param>
    /// <param name="keyRef">
    /// An opaque handle standing in the position of the legacy <c>readonly string prikey</c>. The
    /// private key stays on the Security side; it is named here and never carried.
    /// </param>
    /// <param name="padding">
    /// The padding selector, or <see langword="null"/> to omit it. PRESERVED LEGACY WEAKNESSES 2 AND
    /// 3 apply exactly as on <see cref="RsaEncryptAsync"/>.
    /// </param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The plaintext in the same overload family as the ciphertext.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="data"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="keyRef"/> is empty or whitespace.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A supplied <paramref name="padding"/> is not one of the two declared members.
    /// </exception>
    /// <exception cref="SecurityClientException">The service refused the request or answered off-contract.</exception>
    Task<CryptoPayload> RsaDecryptAsync(
        CryptoPayload data,
        string keyRef,
        long? padding,
        CancellationToken cancellationToken);

    /// <summary>
    /// Signs a payload with an RSA private key. <c>POST /v1/crypto/rsa/sign</c>, covering both legacy
    /// overloads at <c>n_crypto.sru:L70-L71</c>.
    /// </summary>
    /// <param name="data">The payload to sign, in either legacy overload family.</param>
    /// <param name="keyRef">An opaque handle to the signing key, which never leaves Security.</param>
    /// <param name="hashType">
    /// The signature hash selector. PRESERVED LEGACY WEAKNESS 8 in its sharpest form:
    /// <c>CRYPTO_HASH_MD5</c> is a legal signature-hash selector here and is NOT removed, even though
    /// signing under it yields no meaningful collision resistance.
    /// <para>
    /// <c>CRYPTO_HASH_CRC32</c> is the one declared member this operation refuses, and the reason is
    /// not that it is weak - it is that there is nothing to implement. A checksum has no
    /// digest-algorithm identifier for a signature scheme to name, so RSA-over-CRC32 was never a
    /// defined construction. Building one would mean inventing an encoding and calling it parity,
    /// which nothing in this repository could confirm. The published contract states the same
    /// narrowing through its <c>CryptoKeyedHashType</c> schema.
    /// </para>
    /// </param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>
    /// The signature in the same overload family as the payload, because the legacy return type
    /// follows its input type.
    /// </returns>
    /// <remarks>
    /// THIS DOES NOT MAKE DATASERVICES A TOKEN ISSUER, and it must never be used as one. These are
    /// the primitives Security's own token issuer is built on, reached here as an ordinary published
    /// operation. Security remains the sole minter, exactly one signing key exists in the system and
    /// it is held there, and this service holds verification material only. A caller that wants a
    /// token calls <see cref="IServiceTokenProvider.GetTokenAsync"/>; assembling one from this
    /// operation would create the second signing authority the sole-issuer topology exists to
    /// prevent.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="data"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="keyRef"/> is empty or whitespace.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="hashType"/> is not a declared member.</exception>
    /// <exception cref="SecurityClientException">The service refused the request or answered off-contract.</exception>
    Task<CryptoPayload> RsaSignAsync(
        CryptoPayload data,
        string keyRef,
        long hashType,
        CancellationToken cancellationToken);

    /// <summary>
    /// Verifies an RSA signature. <c>POST /v1/crypto/rsa/verify</c>, covering both legacy overloads at
    /// <c>n_crypto.sru:L72-L73</c>.
    /// </summary>
    /// <param name="data">The payload the signature was computed over.</param>
    /// <param name="signature">
    /// The signature to check, WHICH MUST BE IN THE SAME OVERLOAD FAMILY AS <paramref name="data"/>.
    /// The legacy correlates the two - <c>:L72</c> pairs a string payload with a string signature and
    /// <c>:L73</c> a blob payload with a blob signature - and declares no mixed overload, so the
    /// published request carries ONE form selector governing both. A mismatched pair is refused before
    /// the request is sent rather than silently coerced.
    /// </param>
    /// <param name="keyRef">An opaque handle standing in for the legacy <c>readonly string pubkey</c>.</param>
    /// <param name="hashType">
    /// The signature hash selector. See <see cref="RsaSignAsync"/> for weakness 8.
    /// </param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>
    /// The legacy <c>boolean</c> outcome. <see langword="false"/> IS A SUCCESSFUL RESPONSE: the
    /// operation ran and reported that the signature does not verify, which is not the same thing as
    /// the request being rejected. A refusal arrives as <see cref="SecurityClientException"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="data"/> or <paramref name="signature"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="keyRef"/> is empty or whitespace, or the two payloads are in different forms.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="hashType"/> is not a declared member.</exception>
    /// <exception cref="SecurityClientException">The service refused the request or answered off-contract.</exception>
    Task<bool> RsaVerifyAsync(
        CryptoPayload data,
        CryptoPayload signature,
        string keyRef,
        long hashType,
        CancellationToken cancellationToken);

    /// <summary>
    /// Generates an RSA key pair. <c>POST /v1/crypto/rsa/keys</c>, covering both legacy overloads at
    /// <c>n_crypto.sru:L19-L20</c>.
    /// </summary>
    /// <param name="bits">
    /// The modulus size. <see cref="Enums"/> publishes the three predefined values
    /// <c>CRYPTO_RSA_BITS_1024</c>, <c>CRYPTO_RSA_BITS_2048</c> and <c>CRYPTO_RSA_BITS_4096</c>
    /// [enums.sru:L965-L967], but they are CONVENIENCES RATHER THAN A CLOSED SET - the legacy argument
    /// is a 16-bit <c>readonly uint</c> and accepts any value in that domain, which is exactly the
    /// domain of this parameter. No value is filtered here.
    /// <para>
    /// PRESERVED LEGACY WEAKNESS 7. 1024 REMAINS A LEGAL KEY SIZE and is not removed from the accepted
    /// set. No minimum is enforced, because the legacy enforces none and enforcing one would be a
    /// hardening the behaviour-preservation mandate forbids.
    /// </para>
    /// </param>
    /// <param name="pemFormat">
    /// Mirrors the <c>readonly boolean pemformat</c> argument of the four-argument overload at
    /// <c>:L20</c>, which selects the textual format of the returned public key. Passing
    /// <see langword="null"/> OMITS the member and thereby reaches the three-argument overload at
    /// <c>:L19</c>, which does not take the switch at all - a distinction that would be lost if a
    /// value were substituted locally.
    /// </param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>
    /// The public key, an opaque reference to the private key, and the size that was generated.
    /// <para>
    /// A DELIBERATE NARROWING. The legacy returns BOTH keys through two <c>ref string</c>
    /// out-parameters; reproducing that would ship freshly generated private key material across a
    /// network boundary. The private key therefore stays on the Security side and is named by a
    /// reference the caller can use in subsequent operations. There is no parameter and no result
    /// member through which it could be transported, and there is no passphrase, salt or derivation
    /// parameter either, because the legacy surface has none anywhere (weakness 5).
    /// </para>
    /// </returns>
    /// <exception cref="SecurityClientException">The service refused the request or answered off-contract.</exception>
    /// <exception cref="InvalidOperationException">No upstream address was configured.</exception>
    Task<GeneratedRsaKey> GenerateRsaKeyAsync(
        ushort bits,
        bool? pemFormat,
        CancellationToken cancellationToken);

    /// <summary>
    /// Generates cryptographically random bytes. <c>POST /v1/crypto/random/blob</c>, covering the
    /// single legacy overload at <c>n_crypto.sru:L14</c>.
    /// </summary>
    /// <param name="size">
    /// The number of bytes to generate. The legacy argument is a 32-bit <c>readonly ulong</c>, which
    /// is the domain of this parameter; the published contract additionally caps an accepted request
    /// at <see cref="SecurityClient.MaximumRandomSize"/>, and that cap is the CONTRACT'S OWN and is
    /// enforced here so an over-large request is named at its origin rather than after a round trip.
    /// </param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The generated bytes.</returns>
    /// <remarks>
    /// A DETERMINISM SEAM ON THE SERVICE SIDE, NOT THIS ONE. This generator, the random-string
    /// generator and the identifier generator are the three primary sources of non-determinism in the
    /// cryptographic surface, and a parity comparison must mask them on both the master and the
    /// candidate. The seam that substitutes them lives inside Security; a test of THIS client must
    /// therefore assert the request it sends and the response it parses, never a stable value.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="size"/> exceeds the maximum the contract accepts.
    /// </exception>
    /// <exception cref="SecurityClientException">The service refused the request or answered off-contract.</exception>
    Task<byte[]> GenerateRandomBlobAsync(uint size, CancellationToken cancellationToken);

    /// <summary>
    /// Generates a random string. <c>POST /v1/crypto/random/string</c>, covering both legacy overloads
    /// at <c>n_crypto.sru:L15-L16</c>.
    /// </summary>
    /// <param name="size">
    /// The length to generate, bounded exactly as on <see cref="GenerateRandomBlobAsync"/>.
    /// </param>
    /// <param name="flags">
    /// The character-class bitmask, or <see langword="null"/> to reach the single-argument overload at
    /// <c>:L15</c>. The classes are <c>CRYPTO_RNDSTRING_NUMBER</c> (1),
    /// <c>CRYPTO_RNDSTRING_ALPHABET</c> (2) and <c>CRYPTO_RNDSTRING_SYMBOL</c> (4)
    /// [enums.sru:L954-L956], and <c>CRYPTO_RNDSTRING_DEFAULT</c> is their first two summed, so 3
    /// [enums.sru:L957]. The legacy declares this argument <c>Ulong</c>, a 32-bit value, which is the
    /// domain of this parameter. Passing <see langword="null"/> OMITS the member rather than sending
    /// the default value, so a defaulted request stays distinguishable from an explicit one.
    /// </param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The generated string, drawn from the selected character classes.</returns>
    /// <remarks>A non-determinism source. See <see cref="GenerateRandomBlobAsync"/>.</remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="size"/> exceeds the maximum the contract accepts.
    /// </exception>
    /// <exception cref="SecurityClientException">The service refused the request or answered off-contract.</exception>
    Task<string> GenerateRandomStringAsync(uint size, uint? flags, CancellationToken cancellationToken);

    /// <summary>
    /// Generates a globally unique identifier. <c>POST /v1/crypto/random/guid</c>, covering both
    /// legacy overloads at <c>n_crypto.sru:L17-L18</c>.
    /// </summary>
    /// <param name="flags">
    /// The formatting bitmask, or <see langword="null"/> to reach the no-argument overload at
    /// <c>:L17</c>. The flags are <c>CRYPTO_GUID_INCLUDE_BRACKET</c> (1) and
    /// <c>CRYPTO_GUID_INCLUDE_SEPARATOR</c> (2) [enums.sru:L960-L961], and
    /// <c>CRYPTO_GUID_DEFAULT</c> is both summed, so 3 [enums.sru:L962]. The legacy declares this
    /// argument <c>Ulong</c>, a 32-bit value. Passing <see langword="null"/> sends an EMPTY OBJECT,
    /// which the contract states reaches the no-argument overload and yields the default form.
    /// </param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The generated identifier in the requested textual form.</returns>
    /// <remarks>A non-determinism source. See <see cref="GenerateRandomBlobAsync"/>.</remarks>
    /// <exception cref="SecurityClientException">The service refused the request or answered off-contract.</exception>
    Task<string> GenerateGuidAsync(uint? flags, CancellationToken cancellationToken);

    /// <summary>
    /// Decodes an encoded string into bytes. <c>POST /v1/crypto/encoding/string-to-blob</c>, covering
    /// the legacy overload at <c>n_crypto.sru:L11</c>.
    /// </summary>
    /// <param name="data">The encoded text, carried verbatim.</param>
    /// <param name="encoding">
    /// The encoding the text is in: <c>CRYPTO_ENCODING_BASE64</c> (0) or <c>CRYPTO_ENCODING_HEX</c>
    /// (1) [enums.sru:L924-L925].
    /// <para>
    /// THIS IS A GENUINE LEGACY ARGUMENT AND IS NOT THE JSON TRANSPORT ENCODING. The published
    /// contract keeps three encodings apart and this is the third of them: the form selector of
    /// <see cref="CryptoPayload"/> is the first, the base64 in which bytes travel in JSON is the
    /// second, and this is a per-call argument that says how a string is to be interpreted. A request
    /// to decode a HEX string still returns its bytes over a base64 JSON member, and conflating the
    /// two would be a behavioural change.
    /// </para>
    /// </param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The decoded bytes. This operation returns bytes unconditionally, because the legacy
    /// overload has no parallel family to choose between.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="data"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="encoding"/> is not a declared member.</exception>
    /// <exception cref="SecurityClientException">The service refused the request or answered off-contract.</exception>
    Task<byte[]> StringToBlobAsync(string data, long encoding, CancellationToken cancellationToken);

    /// <summary>
    /// Encodes bytes into a string. <c>POST /v1/crypto/encoding/blob-to-string</c>, covering the
    /// legacy overload at <c>n_crypto.sru:L12</c>.
    /// </summary>
    /// <param name="data">The bytes to encode.</param>
    /// <param name="encoding">
    /// The encoding to produce. See <see cref="StringToBlobAsync"/> for why this is not the JSON
    /// transport encoding.
    /// </param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The encoded string, in Base64 or hex according to the requested encoding.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="encoding"/> is not a declared member.</exception>
    /// <exception cref="SecurityClientException">The service refused the request or answered off-contract.</exception>
    Task<string> BlobToStringAsync(
        ReadOnlyMemory<byte> data,
        long encoding,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reverses a sequence of bytes. <c>POST /v1/crypto/encoding/blob-reverse</c>, covering the legacy
    /// overload at <c>n_crypto.sru:L13</c>.
    /// </summary>
    /// <param name="data">The bytes to reverse.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>
    /// Both halves of the legacy outcome.
    /// <para>
    /// THE LEGACY SIGNATURE IS <c>boolean BlobReverse(ref blob data)</c>: it MUTATES ITS ARGUMENT IN
    /// PLACE and returns only a boolean. A request body is not a variable a server can write back
    /// into, so in-place mutation has no wire representation at all. The result therefore carries the
    /// reversed bytes - which the legacy left in the caller's own variable, so returning them is the
    /// ADDITION the boundary forces - and the boolean UNCHANGED, because that is the operation's
    /// actual return value and discarding it in favour of a status code would drop a signal a caller
    /// may be branching on.
    /// </para>
    /// </returns>
    /// <exception cref="SecurityClientException">The service refused the request or answered off-contract.</exception>
    Task<ReversedBlob> ReverseBlobAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken);
}

/// <summary>
/// The three things - and the only three things - a token request carries. Contract <b>C-01</b>.
/// </summary>
/// <remarks>
/// <para>
/// This mirrors the published token request schema exactly: a claimed subject, one intended audience,
/// and a requested scope set. WHAT IS ABSENT IS THE IMPORTANT PART. There is no client secret, no
/// password, no API key, no client assertion, no passphrase and no key material of any kind, and
/// there is no member one could be smuggled into either - the schema sets
/// <c>additionalProperties: false</c> and this type has no extension bag. Caller identity is
/// established by the TRANSPORT, through the client certificate the issuance operation requires, and
/// never by a credential in a request body.
/// </para>
/// <para>
/// The <see cref="Subject"/> below is a CLAIM rather than a credential. The identity actually
/// honoured is the one the presented client certificate establishes, and a mismatch between the two
/// is refused by the service with <c>403</c>.
/// </para>
/// <para>
/// The three members are constructor arguments rather than bound configuration BECAUSE NO
/// CONFIGURATION CARRIES THEM. The Security options group of this service declares one setting, the
/// upstream address, and deliberately nothing else; a token is requested for a particular audience by
/// the call site that needs it, and inventing settings to hold a subject and an audience would put a
/// second, silently authoritative copy of the token topology in a configuration file.
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
    /// value: a service may legitimately need tokens for more than one audience, and each is a
    /// separate request with a separately held credential.
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
/// carries. The conversion is done once, at the moment the response is read, because doing it at each
/// point of use would re-anchor a relative value to a later clock reading every time and steadily
/// overstate how long the credential remains valid.
/// </para>
/// <para>
/// The token is an OPAQUE STRING here and nowhere in this file is it parsed, decoded, inspected or
/// validated. This service holds verification material only and validates INBOUND tokens with the
/// stock bearer handler; the library that would allow it to inspect or mint one is deliberately
/// absent from the project file.
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
    /// <paramref name="accessToken"/>, <paramref name="tokenType"/> or
    /// <paramref name="grantedScopes"/> is <see langword="null"/>.
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
/// Selects WHICH OF THE TWO PARALLEL LEGACY OVERLOAD FAMILIES a cryptographic call belongs to, and
/// therefore what form its result takes.
/// </summary>
/// <remarks>
/// <para>
/// The member names are the wire values, spelled exactly as the published contract declares them, and
/// they travel as those strings. Identifier spellings are preserved throughout this port because they
/// appear in serialized payloads, in log records and in characterization recordings, where a rename
/// does not restyle a symbol - it silently invalidates every stored comparison that mentions it.
/// </para>
/// <para>
/// THIS IS A WIRE-SHAPE SELECTOR AND NOT AN ENCODING. Three distinct encodings exist across this
/// boundary and conflating any two of them would be a behavioural change: (1) this selector, which
/// says which legacy overload family applies; (2) the base64 in which bytes travel inside a JSON
/// string, which is a property of JSON, was invisible to the legacy, and is not a legacy argument at
/// all; and (3) the <c>CRYPTO_ENCODING_BASE64</c> / <c>CRYPTO_ENCODING_HEX</c> argument of the two
/// conversion operations [n_crypto.sru:L11-L12], which IS a genuine legacy argument chosen per call.
/// The third is the one most easily mistaken for the second.
/// </para>
/// </remarks>
[JsonConverter(typeof(PayloadFormJsonConverter))]
public enum PayloadForm
{
    /// <summary>
    /// The string-shaped legacy overload family. The payload is the legacy string verbatim.
    /// </summary>
    /// <remarks>
    /// For a RESULT, the legacy string-shaped overloads already return a printable, text-safe encoded
    /// form of their binary output, which is why a string-shaped ciphertext can be fed straight back
    /// to its string-shaped inverse.
    /// </remarks>
    STRING,

    /// <summary>
    /// The blob-shaped legacy overload family. The payload is the raw bytes the legacy would have held
    /// in a <c>blob</c>; the base64 they travel in is transport only and the blob-shaped overloads
    /// perform no encoding of their own.
    /// </summary>
    BLOB,
}

/// <summary>
/// The JSON converter for <see cref="PayloadForm"/>: the two published names and NOTHING ELSE.
/// </summary>
/// <remarks>
/// <para>
/// WHY A DERIVED CONVERTER EXISTS RATHER THAN THE STOCK ONE. The stock string-enum converter accepts
/// INTEGERS as well as names by default, and the contract publishes this member as a string with a
/// closed set of two values. Accepting an integer would therefore admit a value the published schema
/// cannot express - and, worse, admit values that name no member at all, because an enum in .NET does
/// not restrict a numeric value to its declared members. A response carrying <c>3</c> would deserialize
/// to an undefined <see cref="PayloadForm"/> that no comparison against
/// <see cref="PayloadForm.STRING"/> matches, so every "is it a string, otherwise treat it as a blob"
/// test would silently classify it as a blob and BASE64-DECODE A PAYLOAD THE SERVICE NEVER DESCRIBED
/// THAT WAY (CWE-20).
/// </para>
/// <para>
/// WHY IT IS HAND-WRITTEN RATHER THAN DERIVED FROM THE STOCK ONE, WHICH IS A SECOND AND INDEPENDENT
/// REASON. The stock converter matches token names CASE-INSENSITIVELY by construction and that setting
/// is not exposed: measured on this toolchain, a converter derived from it with a null naming policy and
/// integer values refused still accepts <c>"string"</c>, <c>"Blob"</c> and <c>"sTRING"</c>. The
/// published contract declares <c>enum: [STRING, BLOB]</c>, so accepting any other casing accepts a
/// response the document forbids - and this is the READING end of that document, which is exactly where
/// a lenient reader turns a non-conforming upstream into a silently accepted one. An ordinal comparison
/// against the two declared tokens is the only shape that closes both halves at once.
/// </para>
/// <para>
/// A CUSTOM CONVERTER PUTS THE DECISION ON THE TYPE rather than in one serializer-options instance, so
/// it holds for every reader and writer of this enum, including any added later that forgets to reuse
/// those options. It is deliberately identical in behaviour to the converter the issuing service
/// declares for the same enumeration; the two ends of this contract agreeing about the accepted tokens
/// is the property that keeps them interoperable.
/// </para>
/// <para>
/// This is the FIRST of two independent controls. The second is that every consumer of the value
/// switches exhaustively and rejects an undefined member rather than falling through to a family; see
/// <see cref="CryptoPayload.FromWire"/>. Two controls rather than one, deliberately: this one depends on
/// the value arriving through JSON, and the other holds however it arrives.
/// </para>
/// </remarks>
internal sealed class PayloadFormJsonConverter : JsonConverter<PayloadForm>
{
    /// <summary>The token that names the string-shaped legacy overload family.</summary>
    internal const string StringToken = nameof(PayloadForm.STRING);

    /// <summary>The token that names the blob-shaped legacy overload family.</summary>
    internal const string BlobToken = nameof(PayloadForm.BLOB);

    /// <summary>The refusal text, naming the accepted set and echoing nothing that was read.</summary>
    /// <remarks>
    /// The rejected token is deliberately NOT quoted back. This converter reads UPSTREAM bytes, and the
    /// failure it raises is surfaced through this client's own exception type, whose message reaches a
    /// log - so echoing upstream content into it would put unvalidated bytes into this service's records.
    /// </remarks>
    private const string RefusalMessage =
        "The payload form must be the JSON string \"" + StringToken + "\" or \"" + BlobToken
            + "\", spelled exactly as the contract declares it.";

    /// <summary>Reads one of the two declared tokens.</summary>
    /// <param name="reader">The reader positioned on the value.</param>
    /// <param name="typeToConvert">The requested type, always <see cref="PayloadForm"/>.</param>
    /// <param name="options">The serializer options, which this converter deliberately ignores.</param>
    /// <returns>The overload family the upstream named.</returns>
    /// <exception cref="JsonException">
    /// The value is not a JSON string, or is a string that is not one of the two declared tokens - which
    /// includes every integer form and every other casing of either token.
    /// </exception>
    public override PayloadForm Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException(RefusalMessage);
        }

        string? token = reader.GetString();

        if (string.Equals(token, StringToken, StringComparison.Ordinal))
        {
            return PayloadForm.STRING;
        }

        if (string.Equals(token, BlobToken, StringComparison.Ordinal))
        {
            return PayloadForm.BLOB;
        }

        throw new JsonException(RefusalMessage);
    }

    /// <summary>Writes the declared token for the value.</summary>
    /// <param name="writer">The writer to write to.</param>
    /// <param name="value">The overload family to write.</param>
    /// <param name="options">The serializer options, which this converter deliberately ignores.</param>
    /// <exception cref="ArgumentNullException"><paramref name="writer"/> is <see langword="null"/>.</exception>
    /// <exception cref="JsonException">
    /// The value names no declared member. An enumeration in .NET does not restrict a value to its
    /// declared members, and emitting a numeric form would send this contract a value its own reader
    /// refuses.
    /// </exception>
    public override void Write(Utf8JsonWriter writer, PayloadForm value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WriteStringValue(value switch
        {
            PayloadForm.STRING => StringToken,
            PayloadForm.BLOB => BlobToken,
            _ => throw new JsonException(RefusalMessage),
        });
    }
}

/// <summary>
/// A cryptographic payload together with the legacy overload family it belongs to.
/// </summary>
/// <remarks>
/// <para>
/// This type exists to keep ONE semantic distinction alive across a boundary that would otherwise
/// erase it. The legacy cryptographic surface has parallel <c>string</c> and <c>blob</c> overload
/// families throughout, and ITS RETURN FORM FOLLOWS ITS INPUT FORM:
/// <c>SymEncrypt(readonly string plain, ...)</c> returns <c>string</c> [n_crypto.sru:L30-L37] while
/// <c>SymEncrypt(readonly blob plain, ...)</c> returns <c>blob</c> [<c>:L38-L45</c>]. JSON has no
/// binary type, so a boundary that offered only one form would silently pick an overload family for
/// the caller and change the observable result shape. Carrying the selector explicitly is what lets
/// sixteen overloads per direction reach one operation with nothing lost.
/// </para>
/// <para>
/// The two factory methods are the only way to create one, so a payload can never exist without a
/// form, and the two accessors refuse to reinterpret one form as the other. That refusal is
/// deliberate: silently base64-decoding a string-shaped payload, or treating raw bytes as text, is
/// exactly the coercion that would make a result diverge from the legacy without any error being
/// raised.
/// </para>
/// <para>
/// <see cref="ToString"/> is overridden to describe a payload without disclosing it. Plaintext,
/// ciphertext and signatures all travel through this type and every one of them is sensitive.
/// </para>
/// </remarks>
public sealed class CryptoPayload
{
    private readonly string _text;
    private readonly ReadOnlyMemory<byte> _bytes;

    private CryptoPayload(PayloadForm form, string text, ReadOnlyMemory<byte> bytes)
    {
        Form = form;
        _text = text;
        _bytes = bytes;
    }

    /// <summary>
    /// Which legacy overload family this payload belongs to.
    /// </summary>
    public PayloadForm Form { get; }

    /// <summary>
    /// Wraps a legacy string-shaped payload.
    /// </summary>
    /// <param name="value">
    /// The legacy string, carried verbatim. An empty string is accepted, because the legacy accepts an
    /// empty <c>string</c> argument and refusing it here would narrow the surface without a contract
    /// saying so.
    /// </param>
    /// <returns>A payload in the <see cref="PayloadForm.STRING"/> family.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public static CryptoPayload FromString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new CryptoPayload(PayloadForm.STRING, value, ReadOnlyMemory<byte>.Empty);
    }

    /// <summary>
    /// Wraps a legacy blob-shaped payload.
    /// </summary>
    /// <param name="value">
    /// The raw bytes. An empty sequence is accepted, for the same reason an empty string is.
    /// </param>
    /// <returns>A payload in the <see cref="PayloadForm.BLOB"/> family.</returns>
    public static CryptoPayload FromBlob(ReadOnlyMemory<byte> value) =>
        new(PayloadForm.BLOB, string.Empty, value);

    /// <summary>
    /// Reads this payload as the legacy string it is.
    /// </summary>
    /// <returns>The legacy string, verbatim.</returns>
    /// <exception cref="InvalidOperationException">
    /// This payload belongs to the blob-shaped family. It is NOT converted: reinterpreting one family
    /// as the other would change the observable result without reporting anything.
    /// </exception>
    public string AsString() => Form == PayloadForm.STRING
        ? _text
        : throw new InvalidOperationException(string.Create(
            CultureInfo.InvariantCulture,
            $"This payload belongs to the {nameof(PayloadForm.BLOB)} overload family and cannot be "
            + $"read as a string. Read it with {nameof(AsBlob)} instead; the two families are not "
            + $"interchangeable, because the legacy result form follows its input form."));

    /// <summary>
    /// Reads this payload as the legacy blob it is.
    /// </summary>
    /// <returns>The raw bytes.</returns>
    /// <exception cref="InvalidOperationException">
    /// This payload belongs to the string-shaped family. It is NOT converted, for the reason given on
    /// <see cref="AsString"/>.
    /// </exception>
    public ReadOnlyMemory<byte> AsBlob() => Form == PayloadForm.BLOB
        ? _bytes
        : throw new InvalidOperationException(string.Create(
            CultureInfo.InvariantCulture,
            $"This payload belongs to the {nameof(PayloadForm.STRING)} overload family and cannot be "
            + $"read as bytes. Read it with {nameof(AsString)} instead; the two families are not "
            + $"interchangeable, because the legacy result form follows its input form."));

    /// <summary>
    /// Describes this payload WITHOUT disclosing it.
    /// </summary>
    /// <returns>A redacted description carrying only the form and the length.</returns>
    /// <remarks>
    /// The length is disclosed and the content is not. That is a deliberate line: a length is already
    /// observable from the size of the request or response this payload travels in, so reporting it
    /// discloses nothing new, whereas any prefix of the content would.
    /// </remarks>
    public override string ToString() => string.Create(
        CultureInfo.InvariantCulture,
        $"{nameof(CryptoPayload)} {{ {nameof(Form)} = {Form}, Length = "
        + $"{(Form == PayloadForm.STRING ? _text.Length : _bytes.Length)}, Value = [REDACTED] }}");

    /// <summary>
    /// Projects this payload onto the single JSON string member the contract declares for it.
    /// </summary>
    /// <returns>
    /// The legacy string verbatim for the string-shaped family, or base64 of the raw bytes for the
    /// blob-shaped family. The base64 here is the JSON TRANSPORT encoding and is not a legacy
    /// argument; see <see cref="PayloadForm"/> for the three encodings this boundary keeps apart.
    /// </returns>
    internal string ToWireData() => Form switch
    {
        PayloadForm.STRING => _text,
        PayloadForm.BLOB => Convert.ToBase64String(_bytes.Span),
        _ => throw UndefinedForm(Form, nameof(Form)),
    };

    /// <summary>
    /// Reconstructs a payload from the form selector and data member of a response.
    /// </summary>
    /// <param name="form">The form the response reported.</param>
    /// <param name="data">The data member exactly as the response carried it.</param>
    /// <returns>The reconstructed payload.</returns>
    /// <exception cref="FormatException">
    /// The response declared the blob-shaped family but its data member is not valid base64. This is
    /// surfaced by the caller as an off-contract response rather than being absorbed, because a
    /// payload that cannot be decoded is not a payload.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="form"/> names no declared member. REJECTED BEFORE ANY DECODING, which is the
    /// substantive part: the previous test asked only whether the form was the string family and treated
    /// everything else as the blob family, so an undefined value was base64-decoded as though the
    /// service had described it that way.
    /// </exception>
    /// <remarks>
    /// This is the SECOND of the two controls described on <see cref="PayloadFormJsonConverter"/>, and
    /// it is the one that does not depend on how the value arrived. The converter refuses an undefined
    /// value coming through JSON; this switch refuses one however it was produced, so a future
    /// serializer-options change, a differently configured reader, or a direct internal call cannot
    /// reopen the hole. An exhaustive switch also makes ADDING a member a compile-time decision here
    /// rather than a silent reclassification into the blob family.
    /// </remarks>
    internal static CryptoPayload FromWire(PayloadForm form, string data) => form switch
    {
        PayloadForm.STRING => FromString(data),
        PayloadForm.BLOB => FromBlob(Convert.FromBase64String(data)),
        _ => throw UndefinedForm(form, nameof(form)),
    };

    /// <summary>
    /// Builds the refusal for a form value that names no declared member.
    /// </summary>
    /// <param name="form">The offending value.</param>
    /// <param name="parameterName">The parameter or member that carried it.</param>
    /// <returns>The exception to throw.</returns>
    private static ArgumentOutOfRangeException UndefinedForm(PayloadForm form, string parameterName) =>
        new(
            parameterName,
            form,
            string.Create(
                CultureInfo.InvariantCulture,
                $"'{(int)form}' names no {nameof(PayloadForm)} member. The published contract declares "
                + $"exactly two values, '{nameof(PayloadForm.STRING)}' and "
                + $"'{nameof(PayloadForm.BLOB)}', and an undefined value is refused rather than being "
                + $"treated as either family - reinterpreting it would decode a payload in a form the "
                + $"service never described."));
}

/// <summary>
/// The outcome of an RSA key generation: the public key, a reference to the private key, and the size
/// that was generated. Contract <b>C-02</b>, <c>POST /v1/crypto/rsa/keys</c>.
/// </summary>
/// <remarks>
/// <para>
/// A DELIBERATE NARROWING OF THE LEGACY, RECORDED RATHER THAN PRESENTED AS EQUIVALENCE. The legacy
/// <c>GenRSAKey</c> returns BOTH keys through two <c>ref string</c> out-parameters
/// [n_crypto.sru:L19-L20]. Reproducing that would ship freshly generated private key material across
/// a network boundary, so the private key stays inside the Security service and is named here by an
/// opaque reference instead. THERE IS NO MEMBER ON THIS TYPE THROUGH WHICH A PRIVATE KEY COULD BE
/// CARRIED, and adding one would defeat the rule that raw key material never crosses this wire.
/// </para>
/// <para>
/// <see cref="ToString"/> is overridden to disclose neither the public key nor the reference. The
/// public key is public by definition, but a generated key pair is a sensitive event and the reference
/// names material a caller may act on, so the conservative choice is taken at the type rather than
/// left to every future call site that might format one.
/// </para>
/// </remarks>
public sealed class GeneratedRsaKey
{
    /// <summary>
    /// Creates the generation outcome.
    /// </summary>
    /// <param name="publicKey">The generated public key in textual form.</param>
    /// <param name="keyRef">The opaque reference under which the private key is held.</param>
    /// <param name="bits">The modulus size that was actually generated.</param>
    /// <param name="pemFormat">
    /// The textual format the public key is in, when the service reported it, or
    /// <see langword="null"/> when it did not - which is what the three-argument legacy overload at
    /// <c>n_crypto.sru:L19</c>, taking no format switch at all, corresponds to.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="publicKey"/> or <paramref name="keyRef"/> is <see langword="null"/>.
    /// </exception>
    public GeneratedRsaKey(string publicKey, string keyRef, ushort bits, bool? pemFormat)
    {
        ArgumentNullException.ThrowIfNull(publicKey);
        ArgumentNullException.ThrowIfNull(keyRef);

        PublicKey = publicKey;
        KeyRef = keyRef;
        Bits = bits;
        PemFormat = pemFormat;
    }

    /// <summary>
    /// The generated PUBLIC key, in the textual form the request selected.
    /// </summary>
    public string PublicKey { get; }

    /// <summary>
    /// The opaque reference under which the Security service holds the corresponding PRIVATE key, for
    /// use as the <c>keyRef</c> of a later operation.
    /// </summary>
    /// <remarks>
    /// This carries no key material and is meaningless outside the Security service. It must not be
    /// parsed, decomposed or constructed by a caller: it is a handle, and the only supported use of it
    /// is to pass it back unchanged.
    /// </remarks>
    public string KeyRef { get; }

    /// <summary>
    /// The modulus size that was generated.
    /// </summary>
    /// <remarks>
    /// PRESERVED LEGACY WEAKNESS 7: 1024 remains a legal value [enums.sru:L965] and no minimum is
    /// enforced anywhere on this path, because the legacy enforces none.
    /// </remarks>
    public ushort Bits { get; }

    /// <summary>
    /// Whether the public key is in PEM form, when the service reported it.
    /// </summary>
    public bool? PemFormat { get; }

    /// <summary>
    /// Describes this outcome WITHOUT disclosing the key or the reference.
    /// </summary>
    /// <returns>A redacted description carrying only the size and the reported format.</returns>
    public override string ToString() => string.Create(
        CultureInfo.InvariantCulture,
        $"{nameof(GeneratedRsaKey)} {{ {nameof(Bits)} = {Bits}, {nameof(PemFormat)} = {PemFormat}, "
        + $"{nameof(PublicKey)} = [REDACTED], {nameof(KeyRef)} = [REDACTED] }}");
}

/// <summary>
/// Both halves of the legacy outcome of <c>boolean BlobReverse(ref blob data)</c>
/// [n_crypto.sru:L13]. Contract <b>C-02</b>, <c>POST /v1/crypto/encoding/blob-reverse</c>.
/// </summary>
/// <remarks>
/// <para>
/// WHY THERE ARE TWO MEMBERS RATHER THAN ONE. The legacy MUTATES ITS ARGUMENT IN PLACE and returns
/// only a boolean. A request body is not a variable a server can write back into, so in-place
/// mutation has no wire representation at all. <see cref="Data"/> is therefore the ADDITION the
/// boundary forces - the bytes the legacy left in the caller's own variable - and
/// <see cref="Succeeded"/> is the legacy return value carried UNCHANGED, preserved because it is the
/// operation's actual result and discarding it in favour of a status code would drop a signal a
/// caller may be branching on.
/// </para>
/// <para>
/// A reader should not mistake <see cref="Data"/> for the legacy's return value. It is not.
/// </para>
/// <para>
/// This is a sealed class rather than a record for the same reason <see cref="ServiceTokenRequest"/>
/// is: a record's compiler-generated equality would compare <see cref="Data"/> through
/// <see cref="ReadOnlyMemory{T}"/>'s own equality, which compares the underlying reference, offset and
/// length rather than the bytes - so two results holding identical bytes would usually compare
/// unequal. Publishing equality that is quietly wrong is worse than publishing none.
/// </para>
/// </remarks>
public sealed class ReversedBlob
{
    /// <summary>
    /// Creates the outcome.
    /// </summary>
    /// <param name="data">The reversed bytes.</param>
    /// <param name="succeeded">The legacy boolean return value, carried unchanged.</param>
    public ReversedBlob(ReadOnlyMemory<byte> data, bool succeeded)
    {
        Data = data;
        Succeeded = succeeded;
    }

    /// <summary>
    /// The reversed bytes, which the legacy left in the caller's own variable rather than returning.
    /// </summary>
    public ReadOnlyMemory<byte> Data { get; }

    /// <summary>
    /// The legacy <c>boolean</c> return value, unchanged.
    /// </summary>
    /// <remarks>
    /// It MAY BE <see langword="false"/> IN A SUCCESSFUL RESPONSE: the operation was executed and
    /// reported failure, which is not the same thing as the request being rejected. A rejection arrives
    /// as <see cref="SecurityClientException"/> instead.
    /// </remarks>
    public bool Succeeded { get; }

    /// <summary>
    /// Describes this outcome WITHOUT disclosing the bytes it carries.
    /// </summary>
    /// <returns>A redacted description carrying only the length and the legacy return value.</returns>
    public override string ToString() => string.Create(
        CultureInfo.InvariantCulture,
        $"{nameof(ReversedBlob)} {{ {nameof(Succeeded)} = {Succeeded}, Length = {Data.Length}, "
        + $"{nameof(Data)} = [REDACTED] }}");
}

/// <summary>
/// Raised when the Security service refuses a request, or answers in a shape the published contract
/// does not permit.
/// </summary>
/// <remarks>
/// <para>
/// The properties below project the ONE error body this boundary defines - the RFC 9457
/// problem-details object that every <c>4xx</c> and <c>5xx</c> response of both C-01 and C-02 carries.
/// No second error shape is invented here, because the contract deliberately defines only one so that
/// a consumer writes a single error handler.
/// </para>
/// <para>
/// <see cref="RetCode"/> is the reason this type exists rather than a bare
/// <see cref="HttpRequestException"/>: it surfaces the legacy PowerFramework return code the boundary
/// transcribes, so a caller can branch on the same algebra the rest of the port speaks. TWO
/// PROPERTIES OF THAT ALGEBRA MUST BE EXPECTED RATHER THAN REPAIRED. The legacy success predicate is
/// <c>&gt;= 0</c>, so <see cref="Shared.Kernel.RetCode.PREVENT"/> (1) reads as a SUCCESS; and
/// <see cref="Shared.Kernel.RetCode.CANCELLED"/> (-2) is excluded from failure by an explicit guard
/// while also failing the success test, so it is NEITHER succeeded nor failed. That tri-state hole in
/// a nominally boolean algebra is preserved legacy behaviour. Branch on the specific value, or on the
/// three predicate properties below which apply the kernel's own predicates; never on a two-way
/// success test, and never "correct" it.
/// </para>
/// <para>
/// The message is kept deliberately short - the operation, the status and, where the service supplied
/// one, the problem title. Everything else is exposed as a typed property instead. That is a
/// redaction-minded choice rather than a stylistic one: an exception message is the part most likely
/// to be copied into a log line or an outbound error response, so it carries the least it can while
/// nothing is lost to a caller that wants the rest. NO CREDENTIAL, KEY REFERENCE, KEY MATERIAL,
/// PAYLOAD OR REQUEST BODY IS PLACED IN THE MESSAGE OR IN ANY PROPERTY OF THIS TYPE.
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
    /// The published operation identifier this failure came from, so a caller can attribute a failure
    /// without parsing the message.
    /// </summary>
    public string? OperationId { get; init; }

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
    /// Numerically identical to <see cref="Shared.Kernel.RetCode"/>. The values to expect across this
    /// boundary are <see cref="Shared.Kernel.RetCode.E_INVALID_ARGUMENT"/> for a malformed request or
    /// an argument outside its declared set - which includes a request for RSA no-padding, because the
    /// legacy declares no such value - <see cref="Shared.Kernel.RetCode.E_ACCESS_DENIED"/> for an
    /// unauthenticated caller or one not permitted a reference it named,
    /// <see cref="Shared.Kernel.RetCode.E_OBJECT_NOT_FOUND"/> for a reference that does not exist in
    /// the configured store, and <see cref="Shared.Kernel.RetCode.E_INTERNAL_ERROR"/> or
    /// <see cref="Shared.Kernel.RetCode.UNKNOWN"/> for a server-side failure.
    /// </para>
    /// <para>
    /// It is left <see langword="null"/> rather than defaulted when absent. Substituting a plausible
    /// code would fabricate an oracle value and silently corrupt any stored parity comparison that
    /// mentions it - which is also why <see cref="RetCodeIsSucceeded"/> and its two siblings pass the
    /// nullable value through to the kernel predicates unchanged instead of coercing a missing code to
    /// zero. COERCING NULL TO ZERO WOULD TURN "NEITHER" INTO "SUCCEEDED".
    /// </para>
    /// </remarks>
    public long? RetCode { get; init; }

    /// <summary>
    /// Whether <see cref="RetCode"/> satisfies the legacy SUCCESS predicate.
    /// </summary>
    /// <remarks>
    /// Delegates to <see cref="Predicates.IsSucceeded(long?)"/>, whose test is <c>&gt;= 0</c>. THIS IS
    /// TRUE FOR <see cref="Shared.Kernel.RetCode.PREVENT"/>, so a prevention reads as a success. That
    /// is preserved legacy behaviour and is exactly why this property exists rather than a hand-written
    /// comparison at each call site. It is <see langword="false"/> when the response carried no code.
    /// </remarks>
    public bool RetCodeIsSucceeded => Predicates.IsSucceeded(RetCode);

    /// <summary>
    /// Whether <see cref="RetCode"/> satisfies the legacy FAILURE predicate.
    /// </summary>
    /// <remarks>
    /// Delegates to <see cref="Predicates.IsFailed(long?)"/>, whose test is <c>&lt; 0</c> WITH AN
    /// EXPLICIT EXCLUSION of <see cref="Shared.Kernel.RetCode.CANCELLED"/>. A cancelled code therefore
    /// satisfies neither this predicate nor <see cref="RetCodeIsSucceeded"/>, and neither does a
    /// missing code. Both cases are the tri-state hole, preserved rather than closed.
    /// </remarks>
    public bool RetCodeIsFailed => Predicates.IsFailed(RetCode);

    /// <summary>
    /// Whether <see cref="RetCode"/> is the legacy CANCELLED value.
    /// </summary>
    /// <remarks>
    /// Delegates to <see cref="Predicates.IsCancelled(long?)"/>. This is the only way to identify the
    /// state that is neither succeeded nor failed, which is why it is published alongside the other
    /// two rather than left to a caller to reconstruct.
    /// </remarks>
    public bool RetCodeIsCancelled => Predicates.IsCancelled(RetCode);
}

/// <summary>
/// The typed HTTP client for DataServices' only outbound edge to the Security service, covering
/// contract <b>C-01</b>'s token issuance operation and all seventeen operations of contract
/// <b>C-02</b>.
/// </summary>
/// <remarks>
/// <para>
/// Registered as a typed <see cref="HttpClient"/> in the composition root, which is also where the
/// base address is taken from <c>DataServices:Security:BaseAddress</c>, where the client certificate
/// the issuance edge requires is configured on the message handler, and where the
/// <c>Microsoft.Extensions.Http.Resilience</c> handlers are attached from
/// <c>DataServices:Resilience:Security</c>. This type constructs no <see cref="HttpClient"/>, no
/// message handler and no resilience pipeline of its own: doing so would both bypass that
/// registration and break a test's ability to substitute the transport.
/// </para>
/// <para>
/// IT PERFORMS NO NETWORK INPUT OR OUTPUT DURING CONSTRUCTION, and none in a static initializer or
/// any eager warm-up path. Nothing about this type requires Security to be reachable for the host to
/// start or for the test suite to run.
/// </para>
/// <para>
/// A credential is reused while it remains valid and re-requested once it does not. That is the
/// contract's own stated remedy for needing another short-lived token - call the operation again -
/// rather than an added caching feature, and the lifetime is bounded entirely by the expiry the
/// service reports. No duration is chosen here and no skew margin is subtracted: a margin would be a
/// value with no basis anywhere in the contract, and the repository publishes no budget from which one
/// could be derived. Reuse is a CORRECTNESS property of a short-lived credential, and no performance
/// property is claimed for it.
/// </para>
/// <para>
/// EVERY ARGUMENT CHECK BELOW IS CONTRACT CONFORMANCE RATHER THAN DEFENSIVE PADDING. Each corresponds
/// to a constraint the published schema states - a selector value that must be a member of its
/// enumeration, a reference that must be non-empty, a size within its declared domain - and refusing a
/// non-conforming request here names the fault at its origin instead of spending a round trip to be
/// told the same thing. The one place this is more than convenience is RSA no-padding: the legacy
/// declares no such constant, so a request for it is refused with a DEFINED ERROR rather than being
/// added to the surface.
/// </para>
/// </remarks>
public sealed class SecurityClient : IServiceTokenProvider, ICryptoServiceClient
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
    /// The largest random size the published contract accepts, in bytes for the byte generator and in
    /// characters for the string generator.
    /// </summary>
    /// <remarks>
    /// This is the CONTRACT'S OWN maximum, not a policy invented here. The legacy argument is a 32-bit
    /// <c>readonly ulong</c> [n_crypto.sru:L14-L16] whose domain is far wider; the boundary narrows the
    /// accepted range and states the narrowing, so this value is enforced rather than chosen.
    /// </remarks>
    public const uint MaximumRandomSize = 1_048_576;

    /// <summary>The C-01 issuance path, used exactly as the contract publishes it.</summary>
    /// <remarks>
    /// Relative on purpose, as are all the paths below. The base address belongs to the composition
    /// root, and the contract's server entry is a bare origin carrying no path prefix, so each
    /// published path resolves against it unchanged. THIS FILE CONTAINS NO HOSTNAME, PORT OR SCHEME.
    /// </remarks>
    private static readonly Uri TokenPath = new("/v1/tokens", UriKind.Relative);

    /// <summary>The C-02 unkeyed digest path.</summary>
    private static readonly Uri HashPath = new("/v1/crypto/hash", UriKind.Relative);

    /// <summary>The C-02 keyed digest path.</summary>
    private static readonly Uri HmacPath = new("/v1/crypto/hmac", UriKind.Relative);

    /// <summary>The C-02 unkeyed file digest path.</summary>
    private static readonly Uri HashFilePath = new("/v1/crypto/hash-file", UriKind.Relative);

    /// <summary>The C-02 keyed file digest path.</summary>
    private static readonly Uri HmacFilePath = new("/v1/crypto/hmac-file", UriKind.Relative);

    /// <summary>The C-02 symmetric encryption path.</summary>
    private static readonly Uri SymmetricEncryptPath = new("/v1/crypto/symmetric/encrypt", UriKind.Relative);

    /// <summary>The C-02 symmetric decryption path.</summary>
    private static readonly Uri SymmetricDecryptPath = new("/v1/crypto/symmetric/decrypt", UriKind.Relative);

    /// <summary>The C-02 RSA encryption path.</summary>
    private static readonly Uri RsaEncryptPath = new("/v1/crypto/rsa/encrypt", UriKind.Relative);

    /// <summary>The C-02 RSA decryption path.</summary>
    private static readonly Uri RsaDecryptPath = new("/v1/crypto/rsa/decrypt", UriKind.Relative);

    /// <summary>The C-02 RSA signing path.</summary>
    private static readonly Uri RsaSignPath = new("/v1/crypto/rsa/sign", UriKind.Relative);

    /// <summary>The C-02 RSA verification path.</summary>
    private static readonly Uri RsaVerifyPath = new("/v1/crypto/rsa/verify", UriKind.Relative);

    /// <summary>The C-02 RSA key generation path.</summary>
    private static readonly Uri RsaKeysPath = new("/v1/crypto/rsa/keys", UriKind.Relative);

    /// <summary>The C-02 random bytes path.</summary>
    private static readonly Uri RandomBlobPath = new("/v1/crypto/random/blob", UriKind.Relative);

    /// <summary>The C-02 random string path.</summary>
    private static readonly Uri RandomStringPath = new("/v1/crypto/random/string", UriKind.Relative);

    /// <summary>The C-02 identifier generation path.</summary>
    private static readonly Uri RandomGuidPath = new("/v1/crypto/random/guid", UriKind.Relative);

    /// <summary>The C-02 decode path.</summary>
    private static readonly Uri StringToBlobPath = new("/v1/crypto/encoding/string-to-blob", UriKind.Relative);

    /// <summary>The C-02 encode path.</summary>
    private static readonly Uri BlobToStringPath = new("/v1/crypto/encoding/blob-to-string", UriKind.Relative);

    /// <summary>The C-02 byte-reversal path.</summary>
    private static readonly Uri BlobReversePath = new("/v1/crypto/encoding/blob-reverse", UriKind.Relative);

    /// <summary>
    /// The serializer settings for both directions of this boundary.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One instance, shared and never mutated after construction, so that what travels is byte-stable
    /// across calls. That determinism is a requirement rather than a nicety: a characterization
    /// comparison holds a recorded payload against a produced one, and a serializer whose output
    /// varied would fail the comparison for a reason that has nothing to do with behaviour.
    /// </para>
    /// <para>
    /// The web defaults supply case-insensitive member matching, which makes reading tolerant without
    /// making it lax. Every member this file declares carries an explicit wire name, and an explicit
    /// name takes precedence over any naming policy, so the exact spellings the schema publishes are
    /// what travel regardless of the policy in force. The shared-framework problem-details type has no
    /// such attributes and relies on the web defaults' camel casing, which is the second reason these
    /// settings are shared rather than split in two.
    /// </para>
    /// <para>
    /// Null members are omitted when writing. THAT IS A CONTRACT REQUIREMENT, NOT A TIDINESS CHOICE:
    /// an optional member being ABSENT reaches a different legacy overload from the same member being
    /// present, so writing an explicit null for an omitted initialization vector, cipher mode, RSA
    /// padding, flag set or format switch would collapse a distinction the legacy makes.
    /// </para>
    /// </remarks>
    private static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// The lowest and highest values the optional issuance timestamp may carry and still be
    /// representable, computed from the type's own domain rather than transcribed as literals.
    /// </summary>
    private static readonly long MinIssuedAtSeconds = DateTimeOffset.MinValue.ToUnixTimeSeconds();

    /// <inheritdoc cref="MinIssuedAtSeconds"/>
    private static readonly long MaxIssuedAtSeconds = DateTimeOffset.MaxValue.ToUnixTimeSeconds();

    /// <summary>
    /// The configuration key that supplies this client's base address, composed from the options types'
    /// own identifiers so the two cannot drift apart.
    /// </summary>
    /// <remarks>
    /// Built with <c>nameof</c> against the bound options types rather than written out as a string, so
    /// that renaming a property there is a compile error here instead of a diagnostic message that
    /// quietly starts naming a key which no longer exists. This composes a NAME for a message; it reads
    /// no configuration and constructs no address.
    /// </remarks>
    private static readonly string SecurityAddressConfigurationKey = string.Join(
        ':',
        DataServicesOptions.SectionName,
        nameof(DataServicesOptions.Security),
        nameof(SecurityClientOptions.BaseAddress));

    /// <summary>
    /// The configuration key naming the client certificate this service presents on the issuance edge.
    /// </summary>
    /// <remarks>
    /// Composed the same way, and for the same reason, as <see cref="SecurityAddressConfigurationKey"/>.
    /// This is the NAME of a setting and never its value: no path and no material appears here.
    /// </remarks>
    private static readonly string ClientCertificateConfigurationKey = string.Join(
        ':',
        DataServicesOptions.SectionName,
        nameof(DataServicesOptions.Security),
        nameof(SecurityClientOptions.MutualTls),
        nameof(MutualTlsClientOptions.CertificatePath));

    /// <summary>
    /// The configuration key naming the private key for <see cref="ClientCertificateConfigurationKey"/>.
    /// </summary>
    private static readonly string ClientCertificateKeyConfigurationKey = string.Join(
        ':',
        DataServicesOptions.SectionName,
        nameof(DataServicesOptions.Security),
        nameof(SecurityClientOptions.MutualTls),
        nameof(MutualTlsClientOptions.CertificateKeyPath));

    /// <summary>
    /// The delimiter RFC 6749 uses for the granted scope set, and therefore the one this boundary uses.
    /// </summary>
    private const char ScopeSeparator = ' ';

    /// <summary>The credential type contract C-01 pins with a schema constant.</summary>
    private const string BearerTokenType = "Bearer";

    /// <summary>
    /// The authentication scheme token of the caller credential C-01 accepts on <c>POST /v1/tokens</c>:
    /// <c>Basic</c>, as RFC 7617 spells it.
    /// </summary>
    /// <remarks>
    /// Written with the canonical capitalisation even though RFC 9110 makes the scheme token
    /// case-insensitive and Security compares it case-insensitively. Sending the canonical spelling
    /// keeps this client interoperable with any intermediary that is stricter than the specification
    /// requires, and costs nothing.
    /// </remarks>
    private const string BasicSchemeToken = "Basic";

    /// <summary>
    /// The identity DataServices claims when it asks Security for a token to call Security's own
    /// cryptographic surface.
    /// </summary>
    /// <remarks>
    /// A NON-SECRET PROTOCOL IDENTIFIER, NOT A CREDENTIAL. It is a name, it authenticates nothing on its
    /// own, and it is deliberately not configurable: the identity actually honoured is the one the
    /// presented credential establishes when Security issues the token - the contract states in as many
    /// words that the subject is a claim checked against that identity and a mismatch is refused 403 -
    /// so a configurable value here could only ever disagree with the credential and be rejected.
    /// Everything genuinely sensitive binds from environment configuration in the composition root.
    /// <para>
    /// IT IS ALSO THE USER-ID HALF OF THE BASIC ISSUANCE CREDENTIAL, which is why one constant serves
    /// both. Security resolves the presented user-id against its issuance roster and then compares the
    /// body's subject against the entry it resolved, so sending one value in the header and another in
    /// the body would be refused by construction. Using the same constant for both makes that agreement
    /// structural rather than remembered.
    /// </para>
    /// </remarks>
    private const string TokenSubject = "powerframework-dataservices";

    /// <summary>
    /// The single audience a credential for contract C-02 is requested for: Security's own.
    /// </summary>
    /// <remarks>
    /// The audience of a call to Security IS Security, and the value follows the audience convention the
    /// service's own inbound configuration fixes. The token contract carries ONE audience per request,
    /// deliberately, so a credential is never valid somewhere its holder did not intend it to be - which
    /// is why this cannot be shared with the credential DataServices presents to Persistence.
    /// </remarks>
    private const string SecurityAudience = "powerframework-security";

    /// <summary>
    /// The scope covering contract C-02's cryptographic surface.
    /// </summary>
    /// <remarks>
    /// Named on the service-dot-capability convention this system's other outbound credentials already
    /// use. One scope covers all seventeen C-02 operations because they are one capability: a caller
    /// that may hash may also sign, since the same key store answers both. Splitting them into finer
    /// scopes would publish a distinction the contract does not make.
    /// </remarks>
    private const string CryptoScope = "security.crypto";

    /// <summary>The name of the single extension member the contract's error body defines.</summary>
    private const string RetCodeExtensionMember = "retCode";

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
    private readonly IOptions<DataServicesOptions> _options;
    private readonly ILogger<SecurityClient> _logger;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// The issued credentials this instance currently holds, keyed by the request that produced each.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Concurrent by construction rather than by convention. A typed client is resolved per scope while
    /// a single instance may serve several concurrent requests, so this is genuinely shared mutable
    /// state and is guarded accordingly. No lock is held across the network call - a lock spanning
    /// input and output cannot be held across an <c>await</c> in any case, and one that serialised
    /// unrelated callers behind each other would buy no correctness. The consequence is that two
    /// callers racing on the same key may each obtain a token, which is harmless: issuance is
    /// independent and free of side effects, and either credential is equally valid.
    /// </para>
    /// <para>
    /// The key space is bounded by construction. Keys are composed from the subject, audience and scope
    /// set that this service's OWN call sites supply, never from external input, so this cannot grow
    /// without limit no matter what a caller of this service sends.
    /// </para>
    /// <para>
    /// REUSE SPANS THE LIFETIME OF THE INJECTED CACHE, NOT OF THIS INSTANCE, and that distinction was
    /// a real defect before it was made explicit. This client is registered as a TYPED HTTP CLIENT,
    /// which the framework registers TRANSIENT - so an instance-owned cache meant every resolve got an
    /// empty one, and the two interfaces this type implements each minted their own credential on their
    /// first call however carefully the composition root was written. The cache is therefore supplied
    /// by the composition root as a SINGLETON and named on the constructor, so the sharing is a
    /// property of the wiring rather than of a comment about the wiring.
    /// </para>
    /// <para>
    /// NOTHING IS PERSISTED: there is no file, no database and no distributed cache behind it, which is
    /// also what keeps constraint C-E true of this file.
    /// </para>
    /// </remarks>
    private readonly ServiceTokenCache _tokenCache;

    /// <summary>
    /// The one token request every contract C-02 call is authenticated with.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Static and shared because all three of its members are compile-time constants, which is also what
    /// makes the credential cache reusable across every crypto operation: seventeen operations resolve to
    /// ONE cache key, so a burst of crypto calls costs one issuance rather than seventeen.
    /// </para>
    /// <para>
    /// This request is fed to <see cref="GetTokenAsync"/>, the same public member the service's own
    /// callers use. Nothing separate is built for it: reusing that path is what gives the internal
    /// credential the same caching, the same expiry comparison and the same never-logged treatment as
    /// every other, instead of a second credential path that would have to be kept in step with it.
    /// </para>
    /// </remarks>
    private static readonly ServiceTokenRequest CryptoTokenRequest =
        new(TokenSubject, SecurityAudience, [CryptoScope]);

    /// <summary>
    /// Creates the client against the system clock.
    /// </summary>
    /// <param name="httpClient">
    /// The configured client supplied by the typed-client factory, already carrying its base address
    /// and resilience handlers.
    /// </param>
    /// <param name="options">The bound options, used only as described on the four-argument overload.</param>
    /// <param name="logger">The logger for this client.</param>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// This overload exists so the type resolves whether or not a <see cref="TimeProvider"/> has been
    /// registered in the container. The activator selects the widest constructor it can satisfy, so
    /// registering one engages the seam below and registering nothing engages this. Neither performs
    /// any network input or output.
    /// </remarks>
    public SecurityClient(
        HttpClient httpClient,
        IOptions<DataServicesOptions> options,
        ILogger<SecurityClient> logger)
        : this(httpClient, options, logger, TimeProvider.System)
    {
    }

    /// <summary>
    /// Creates the client against an explicit clock.
    /// </summary>
    /// <param name="httpClient">
    /// The configured client supplied by the typed-client factory, already carrying its base address
    /// and resilience handlers.
    /// </param>
    /// <param name="options">
    /// The bound options. THESE ARE READ IN EXACTLY ONE PLACE and never to decide where a request goes:
    /// when the typed client carries no base address, the fail-fast diagnostic consults the bound
    /// Security setting so that it can distinguish "the setting is not configured at all" from "the
    /// setting is configured but this client was registered without it". Those are different faults
    /// with different fixes, and naming the wrong one sends an operator to the wrong file. NO REQUEST
    /// ADDRESS IS EVER COMPOSED FROM THIS, which is what keeps the composition root's registration the
    /// single authority over the transport.
    /// </param>
    /// <param name="logger">The logger for this client.</param>
    /// <param name="timeProvider">
    /// The clock used to decide whether a held credential is still valid. This is the determinism
    /// seam: a test substitutes it so that expiry and reuse are exercised without waiting for real
    /// time to pass, which is what makes the behaviour reproducible rather than schedule-dependent.
    /// It is the ONLY clock this file reads.
    /// </param>
    /// <param name="tokenCache">
    /// The credential cache this client reads and writes. SUPPLIED BY THE COMPOSITION ROOT AS A
    /// SINGLETON, which is what makes one credential genuinely shared by contract C-01's token provider
    /// and contract C-02's seventeen crypto operations. Omitting it gives this instance a private cache,
    /// which is the right default for a test that wants an isolated one and the WRONG state for a host -
    /// so the composition root always names it.
    /// </param>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// WHY THE CACHE IS A PARAMETER RATHER THAN A FIELD INITIALISER, WHICH IS THE DEFECT THIS CLOSES.
    /// This type is registered as a typed HTTP client, and the framework registers those TRANSIENT. An
    /// instance-owned cache therefore meant a fresh empty cache on every resolve: the composition root's
    /// two interface registrations each produced their own client on first use, each minted its own
    /// credential, and the comment claiming they shared one was describing an intention rather than the
    /// wiring. Naming the cache here moves the guarantee into the type system, where a composition test
    /// can assert it.
    /// </remarks>
    public SecurityClient(
        HttpClient httpClient,
        IOptions<DataServicesOptions> options,
        ILogger<SecurityClient> logger,
        TimeProvider timeProvider,
        ServiceTokenCache? tokenCache = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _httpClient = httpClient;
        _options = options;
        _logger = logger;
        _timeProvider = timeProvider;
        _tokenCache = tokenCache ?? new ServiceTokenCache();
    }

    // ==============================================================================================
    //  C-01 - security.v1.TokenService, POST /v1/tokens
    // ==============================================================================================

    /// <inheritdoc/>
    public async ValueTask<ServiceToken> GetTokenAsync(
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
    /// Asks the Security service to mint a token - the ONLY way this client obtains one.
    /// </summary>
    /// <param name="request">The validated request.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The issued credential.</returns>
    /// <remarks>
    /// NO BEARER CREDENTIAL IS SENT, and that is why this is the ONE call in this client that goes
    /// through <see cref="SendWithIssuanceCredentialAsync{TRequest, TResponse}"/> rather than through
    /// the authenticated sender: a caller cannot present a bearer token in order to obtain its first
    /// bearer token. What it does send is the CALLER credential C-01 accepts - an HTTP <c>Basic</c>
    /// credential when an issuance secret is configured, and otherwise nothing at all, leaving the
    /// client certificate the composition root attached to the handler to authenticate the handshake.
    /// The body carries exactly the three members the schema declares and no credential of any kind.
    /// <para>
    /// Routing this through the authenticated sender would not merely be wrong on the wire; it would
    /// recurse without bound, because acquiring the credential would call this operation again.
    /// </para>
    /// </remarks>
    private async Task<ServiceToken> IssueTokenAsync(
        ServiceTokenRequest request,
        CancellationToken cancellationToken)
    {
        // ADDRESS BEFORE IDENTITY, deliberately. Both are configuration faults that make issuance
        // unreachable, and reporting the more fundamental one first is what makes the message actionable:
        // a host with no address configured has nothing to present a certificate TO, so naming the
        // missing certificate there would send an operator to the wrong setting. The sender checks the
        // address too; checking it here as well is what fixes the ORDER rather than duplicating a guard.
        EnsureBaseAddress("issueToken");
        EnsureClientIdentityConfigured();
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

        (TokenIssuanceResponseBody payload, System.Net.HttpStatusCode statusCode) =
            await SendWithIssuanceCredentialAsync<TokenIssuanceRequestBody, TokenIssuanceResponseBody>(
                    TokenPath,
                    body,
                    "issueToken",
                    cancellationToken)
                .ConfigureAwait(false);

        ServiceToken token = ToServiceToken(payload, statusCode);

        // BOTH COUNTS ARE RECORDED RATHER THAN ASSUMED EQUAL. Security refuses a scope its roster does
        // not grant rather than narrowing the set, so on a success the two counts agree - and recording
        // both is what would make a disagreement visible if an issuer ever did narrow one, instead of
        // that loss surfacing far downstream as an unexplained refusal by whichever service needed the
        // dropped scope. It is an observation either way, never a warning: this client does not decide
        // policy. The token value itself is not logged, here or anywhere else.
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
    /// Converts a conforming issuance body into an issued token, refusing one that does not conform.
    /// </summary>
    /// <param name="payload">The deserialized response body.</param>
    /// <param name="statusCode">The status the response carried, recorded on any failure raised here.</param>
    /// <returns>The issued credential with an absolute expiry.</returns>
    /// <remarks>
    /// Refusing a non-conforming body here is a NARROWING WITH A DEFINED ERROR rather than a guess: an
    /// empty token or a nonsensical lifetime accepted silently would surface later as an unexplained
    /// rejection by whichever service the credential was presented to, at a point far away from the
    /// cause. The token is checked for CONFORMANCE ONLY - it is never parsed, decoded or validated.
    /// </remarks>
    private ServiceToken ToServiceToken(
        TokenIssuanceResponseBody payload,
        System.Net.HttpStatusCode statusCode)
    {
        // The schema declares minLength 1 on the token.
        if (payload.AccessToken.Length == 0)
        {
            throw Malformed("issueToken", "carried an empty access token", statusCode);
        }

        // The schema pins the credential type with a constant rather than an enumeration. RFC 6749
        // treats this member case-insensitively, but this contract is stricter than the RFC and the
        // contract is what both ends of this boundary are built to, so the stricter document governs
        // and a divergence is reported instead of being absorbed.
        if (!string.Equals(payload.TokenType, BearerTokenType, StringComparison.Ordinal))
        {
            throw Malformed(
                "issueToken",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"carried a credential type other than the '{BearerTokenType}' constant the "
                    + $"contract pins"),
                statusCode);
        }

        // The schema declares minimum 1 on the lifetime.
        if (payload.ExpiresIn < 1)
        {
            throw Malformed(
                "issueToken",
                "carried a token lifetime below the declared minimum of one second",
                statusCode);
        }

        DateTimeOffset issuedAt;
        if (payload.IssuedAt is long issuedAtSeconds)
        {
            if (issuedAtSeconds < MinIssuedAtSeconds || issuedAtSeconds > MaxIssuedAtSeconds)
            {
                throw Malformed(
                    "issueToken",
                    "carried an issuance timestamp outside the representable range",
                    statusCode);
            }

            // Present so a caller can compute an expiry WITHOUT PARSING THE TOKEN, which is exactly
            // what happens here. Anchoring to the issuer's own clock is preferred over a local reading
            // because the lifetime the response reports is measured from issuance, not from receipt.
            issuedAt = DateTimeOffset.FromUnixTimeSeconds(issuedAtSeconds);
        }
        else
        {
            // The member is optional, so a reading of the injected clock is the only anchor available.
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
                OperationId = "issueToken",
                StatusCode = (int)statusCode,
            };
        }

        return new ServiceToken(
            payload.AccessToken,
            payload.TokenType,
            expiresAt,
            ParseGrantedScopes(payload.Scope));
    }

    // ==============================================================================================
    //  C-02 - security.v1.CryptoService, 17 operations under /v1/crypto/**
    //
    //  Every member below is a POST, including the ones that read like queries. That is the published
    //  contract and it is deliberate: a GET with the payload in the query string would place plaintext,
    //  ciphertext and digests into request lines, and therefore into access logs, proxy caches and
    //  browser history - none of which the in-process legacy had. The three generators are not
    //  idempotent in any useful sense either, since each returns a different result per call by
    //  definition.
    //
    //  NO REQUEST BODY AND NO RESPONSE BODY IS LOGGED BY ANY MEMBER BELOW. The only record any of them
    //  writes is the published operation identifier, which carries nothing.
    // ==============================================================================================

    /// <inheritdoc/>
    public async Task<string> HashAsync(
        CryptoPayload data,
        long hashType,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(data);
        EnsureDeclaredHashType(hashType);

        HashRequestBody body = new()
        {
            Data = data.ToWireData(),
            PayloadForm = data.Form,
            HashType = hashType,
        };

        (DigestResponseBody payload, _) = await InvokeAsync<HashRequestBody, DigestResponseBody>(
            HashPath,
            body,
            "hash",
            cancellationToken).ConfigureAwait(false);

        return payload.Digest;
    }

    /// <inheritdoc/>
    public async Task<string> HmacAsync(
        CryptoPayload data,
        string keyRef,
        long hashType,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentException.ThrowIfNullOrWhiteSpace(keyRef);
        EnsureKeyedHashType(hashType);

        HmacRequestBody body = new()
        {
            Data = data.ToWireData(),
            PayloadForm = data.Form,
            KeyRef = keyRef,
            HashType = hashType,
        };

        (DigestResponseBody payload, _) = await InvokeAsync<HmacRequestBody, DigestResponseBody>(
            HmacPath,
            body,
            "hmac",
            cancellationToken).ConfigureAwait(false);

        return payload.Digest;
    }

    /// <inheritdoc/>
    public async Task<string> HashFileAsync(
        string fileRef,
        long hashType,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileRef);
        EnsureDeclaredHashType(hashType);

        HashFileRequestBody body = new()
        {
            FileRef = fileRef,
            HashType = hashType,
        };

        (DigestResponseBody payload, _) = await InvokeAsync<HashFileRequestBody, DigestResponseBody>(
            HashFilePath,
            body,
            "hashFile",
            cancellationToken).ConfigureAwait(false);

        return payload.Digest;
    }

    /// <inheritdoc/>
    public async Task<string> HmacFileAsync(
        string fileRef,
        string keyRef,
        long hashType,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileRef);
        ArgumentException.ThrowIfNullOrWhiteSpace(keyRef);
        EnsureKeyedHashType(hashType);

        HmacFileRequestBody body = new()
        {
            FileRef = fileRef,
            KeyRef = keyRef,
            HashType = hashType,
        };

        (DigestResponseBody payload, _) = await InvokeAsync<HmacFileRequestBody, DigestResponseBody>(
            HmacFilePath,
            body,
            "hmacFile",
            cancellationToken).ConfigureAwait(false);

        return payload.Digest;
    }

    /// <inheritdoc/>
    public async Task<CryptoPayload> SymmetricEncryptAsync(
        CryptoPayload data,
        string keyRef,
        long cipherType,
        string? ivRef,
        long? mode,
        CancellationToken cancellationToken)
    {
        SymEncryptRequestBody body = BuildSymmetricRequest(data, keyRef, cipherType, ivRef, mode);

        (PayloadResponseBody payload, System.Net.HttpStatusCode statusCode) = await InvokeAsync<
            SymEncryptRequestBody,
            PayloadResponseBody>(
            SymmetricEncryptPath,
            body,
            "symmetricEncrypt",
            cancellationToken).ConfigureAwait(false);

        return ToPayload(payload, "symmetricEncrypt", statusCode);
    }

    /// <inheritdoc/>
    public async Task<CryptoPayload> SymmetricDecryptAsync(
        CryptoPayload data,
        string keyRef,
        long cipherType,
        string? ivRef,
        long? mode,
        CancellationToken cancellationToken)
    {
        // The two directions share a request shape, so they share the builder. They remain two
        // operations rather than one with a direction flag, because the contract publishes them as two
        // and this client's business is conformance to the contract.
        SymEncryptRequestBody body = BuildSymmetricRequest(data, keyRef, cipherType, ivRef, mode);

        (PayloadResponseBody payload, System.Net.HttpStatusCode statusCode) = await InvokeAsync<
            SymEncryptRequestBody,
            PayloadResponseBody>(
            SymmetricDecryptPath,
            body,
            "symmetricDecrypt",
            cancellationToken).ConfigureAwait(false);

        return ToPayload(payload, "symmetricDecrypt", statusCode);
    }

    /// <inheritdoc/>
    public async Task<CryptoPayload> RsaEncryptAsync(
        CryptoPayload data,
        string keyRef,
        long? padding,
        CancellationToken cancellationToken)
    {
        RsaCipherRequestBody body = BuildRsaCipherRequest(data, keyRef, padding);

        (PayloadResponseBody payload, System.Net.HttpStatusCode statusCode) = await InvokeAsync<
            RsaCipherRequestBody,
            PayloadResponseBody>(
            RsaEncryptPath,
            body,
            "rsaEncrypt",
            cancellationToken).ConfigureAwait(false);

        return ToPayload(payload, "rsaEncrypt", statusCode);
    }

    /// <inheritdoc/>
    public async Task<CryptoPayload> RsaDecryptAsync(
        CryptoPayload data,
        string keyRef,
        long? padding,
        CancellationToken cancellationToken)
    {
        RsaCipherRequestBody body = BuildRsaCipherRequest(data, keyRef, padding);

        (PayloadResponseBody payload, System.Net.HttpStatusCode statusCode) = await InvokeAsync<
            RsaCipherRequestBody,
            PayloadResponseBody>(
            RsaDecryptPath,
            body,
            "rsaDecrypt",
            cancellationToken).ConfigureAwait(false);

        return ToPayload(payload, "rsaDecrypt", statusCode);
    }

    /// <inheritdoc/>
    public async Task<CryptoPayload> RsaSignAsync(
        CryptoPayload data,
        string keyRef,
        long hashType,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentException.ThrowIfNullOrWhiteSpace(keyRef);
        EnsureKeyedHashType(hashType);

        RsaSignRequestBody body = new()
        {
            Data = data.ToWireData(),
            PayloadForm = data.Form,
            KeyRef = keyRef,
            HashType = hashType,
        };

        (PayloadResponseBody payload, System.Net.HttpStatusCode statusCode) = await InvokeAsync<
            RsaSignRequestBody,
            PayloadResponseBody>(
            RsaSignPath,
            body,
            "rsaSign",
            cancellationToken).ConfigureAwait(false);

        return ToPayload(payload, "rsaSign", statusCode);
    }

    /// <inheritdoc/>
    public async Task<bool> RsaVerifyAsync(
        CryptoPayload data,
        CryptoPayload signature,
        string keyRef,
        long hashType,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(signature);
        ArgumentException.ThrowIfNullOrWhiteSpace(keyRef);
        EnsureKeyedHashType(hashType);

        // ONE form selector governs both members, because the legacy correlates them: :L72 pairs a
        // string payload with a string signature and :L73 a blob payload with a blob signature, and
        // there is no mixed overload. A mismatched pair is therefore not expressible on this wire, and
        // refusing it here reports the fault at its origin rather than sending a request whose single
        // selector would silently misdescribe one of the two values.
        if (data.Form != signature.Form)
        {
            throw new ArgumentException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The payload and the signature must belong to the same overload family; the "
                    + $"payload is {data.Form} and the signature is {signature.Form}. The legacy "
                    + $"declares no mixed overload and the request carries one form selector for both."),
                nameof(signature));
        }

        RsaVerifyRequestBody body = new()
        {
            Data = data.ToWireData(),
            PayloadForm = data.Form,
            Signature = signature.ToWireData(),
            KeyRef = keyRef,
            HashType = hashType,
        };

        (RsaVerifyResponseBody payload, _) = await InvokeAsync<
            RsaVerifyRequestBody,
            RsaVerifyResponseBody>(
            RsaVerifyPath,
            body,
            "rsaVerify",
            cancellationToken).ConfigureAwait(false);

        // Returned as the service reported it. A false outcome is a SUCCESSFUL response and is not
        // promoted to a failure here: the operation ran and answered.
        return payload.Valid;
    }

    /// <inheritdoc/>
    public async Task<GeneratedRsaKey> GenerateRsaKeyAsync(
        ushort bits,
        bool? pemFormat,
        CancellationToken cancellationToken)
    {
        // NO RANGE CHECK, AND THAT IS DELIBERATE. The legacy argument is a 16-bit value and accepts any
        // value in that domain, which is exactly this parameter's domain, so there is nothing to
        // validate. In particular 1024 is NOT rejected (preserved legacy weakness 7) and the three
        // predefined sizes are not treated as a closed set.
        GenRsaKeyRequestBody body = new()
        {
            Bits = bits,
            PemFormat = pemFormat,
        };

        (GenRsaKeyResponseBody payload, System.Net.HttpStatusCode statusCode) = await InvokeAsync<
            GenRsaKeyRequestBody,
            GenRsaKeyResponseBody>(
            RsaKeysPath,
            body,
            "generateRsaKey",
            cancellationToken).ConfigureAwait(false);

        // The schema declares minLength 1 on the reference, so an empty one is off-contract. Accepting
        // it would hand the caller a handle that cannot resolve.
        if (payload.KeyRef.Length == 0)
        {
            throw Malformed(
                "generateRsaKey",
                "carried an empty private-key reference, which cannot be used in a later operation",
                statusCode);
        }

        // The response's own reported size is used rather than the requested one, so that a service
        // which generated a different size reports it rather than having the request echoed back.
        if (payload.Bits is < ushort.MinValue or > ushort.MaxValue)
        {
            throw Malformed(
                "generateRsaKey",
                "carried a modulus size outside the domain the contract declares for it",
                statusCode);
        }

        return new GeneratedRsaKey(
            payload.PublicKey,
            payload.KeyRef,
            (ushort)payload.Bits,
            payload.PemFormat);
    }

    /// <inheritdoc/>
    public async Task<byte[]> GenerateRandomBlobAsync(uint size, CancellationToken cancellationToken)
    {
        EnsureAcceptedRandomSize(size);

        RandomBlobRequestBody body = new() { Size = size };

        (BlobResponseBody payload, System.Net.HttpStatusCode statusCode) = await InvokeAsync<
            RandomBlobRequestBody,
            BlobResponseBody>(
            RandomBlobPath,
            body,
            "generateRandomBlob",
            cancellationToken).ConfigureAwait(false);

        return DecodeTransportBase64(payload.Data, "generateRandomBlob", statusCode);
    }

    /// <inheritdoc/>
    public async Task<string> GenerateRandomStringAsync(
        uint size,
        uint? flags,
        CancellationToken cancellationToken)
    {
        EnsureAcceptedRandomSize(size);

        // NO FLAG VALIDATION. The contract declares this member's domain as the whole 32-bit range
        // rather than an enumeration - the three classes are bitmask VALUES, not a closed set of legal
        // arguments - so rejecting an unrecognised bit here would narrow a surface the legacy does not
        // narrow. A null omits the member and reaches the single-argument legacy overload.
        RndStringRequestBody body = new()
        {
            Size = size,
            Flags = flags,
        };

        (RndStringResponseBody payload, _) = await InvokeAsync<
            RndStringRequestBody,
            RndStringResponseBody>(
            RandomStringPath,
            body,
            "generateRandomString",
            cancellationToken).ConfigureAwait(false);

        return payload.Value;
    }

    /// <inheritdoc/>
    public async Task<string> GenerateGuidAsync(uint? flags, CancellationToken cancellationToken)
    {
        // Every member of this request is optional, so a null flag set serialises to an EMPTY OBJECT,
        // which the contract states reaches the no-argument legacy overload and yields the default
        // form. An empty object is sent rather than no body at all: the contract permits either, and
        // sending one keeps this operation's transmission identical in shape to the other sixteen.
        GuidRequestBody body = new() { Flags = flags };

        (GuidResponseBody payload, _) = await InvokeAsync<GuidRequestBody, GuidResponseBody>(
            RandomGuidPath,
            body,
            "generateGuid",
            cancellationToken).ConfigureAwait(false);

        return payload.Value;
    }

    /// <inheritdoc/>
    public async Task<byte[]> StringToBlobAsync(
        string data,
        long encoding,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(data);
        EnsureDeclaredEncoding(encoding);

        StringToBlobRequestBody body = new()
        {
            Data = data,
            Encoding = encoding,
        };

        (BlobResponseBody payload, System.Net.HttpStatusCode statusCode) = await InvokeAsync<
            StringToBlobRequestBody,
            BlobResponseBody>(
            StringToBlobPath,
            body,
            "stringToBlob",
            cancellationToken).ConfigureAwait(false);

        // The result travels as base64 REGARDLESS of the encoding argument above. A request to decode a
        // hex string still returns its bytes over a base64 member, because the two are different
        // encodings answering different questions.
        return DecodeTransportBase64(payload.Data, "stringToBlob", statusCode);
    }

    /// <inheritdoc/>
    public async Task<string> BlobToStringAsync(
        ReadOnlyMemory<byte> data,
        long encoding,
        CancellationToken cancellationToken)
    {
        EnsureDeclaredEncoding(encoding);

        BlobToStringRequestBody body = new()
        {
            Data = Convert.ToBase64String(data.Span),
            Encoding = encoding,
        };

        (EncodedTextResponseBody payload, _) = await InvokeAsync<
            BlobToStringRequestBody,
            EncodedTextResponseBody>(
            BlobToStringPath,
            body,
            "blobToString",
            cancellationToken).ConfigureAwait(false);

        return payload.Value;
    }

    /// <inheritdoc/>
    public async Task<ReversedBlob> ReverseBlobAsync(
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken)
    {
        BlobReverseRequestBody body = new() { Data = Convert.ToBase64String(data.Span) };

        (BlobReverseResponseBody payload, System.Net.HttpStatusCode statusCode) = await InvokeAsync<
            BlobReverseRequestBody,
            BlobReverseResponseBody>(
            BlobReversePath,
            body,
            "reverseBlob",
            cancellationToken).ConfigureAwait(false);

        // BOTH halves are carried through. The boolean is the legacy return value and is not converted
        // into a status, and the bytes are the addition the boundary forces because in-place mutation
        // has no wire representation.
        return new ReversedBlob(
            DecodeTransportBase64(payload.Data, "reverseBlob", statusCode),
            payload.Succeeded);
    }

    // ==============================================================================================
    //  REQUEST CONSTRUCTION - shared by the operation pairs that publish the same request shape
    // ==============================================================================================

    /// <summary>
    /// Builds and validates the request body both symmetric directions share.
    /// </summary>
    /// <param name="data">The payload.</param>
    /// <param name="keyRef">The opaque key handle.</param>
    /// <param name="cipherType">The cipher selector.</param>
    /// <param name="ivRef">The opaque vector handle, or <see langword="null"/> to omit it.</param>
    /// <param name="mode">The cipher mode, or <see langword="null"/> to omit it.</param>
    /// <returns>The request body.</returns>
    /// <remarks>
    /// A NULL REFERENCE OR MODE IS LEFT NULL AND THEREFORE OMITTED FROM THE REQUEST. No default is
    /// substituted here, because "no vector supplied" and "no mode supplied" select different legacy
    /// overloads from an explicit vector or an explicit ECB, and substituting a value locally would
    /// erase that distinction while appearing to preserve behaviour. Preserved legacy weakness 1 - the
    /// service applies ECB when the member is absent [enums.sru:L946] - is honoured by NOT interfering.
    /// </remarks>
    private static SymEncryptRequestBody BuildSymmetricRequest(
        CryptoPayload data,
        string keyRef,
        long cipherType,
        string? ivRef,
        long? mode)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentException.ThrowIfNullOrWhiteSpace(keyRef);
        EnsureDeclaredCipherType(cipherType);

        // Validated only when supplied: the member is optional, and the schema's minLength 1 applies to
        // a value that is present rather than to its absence.
        if (ivRef is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(ivRef);
        }

        if (mode is long requestedMode)
        {
            EnsureDeclaredMode(requestedMode);
            EnsureReproducibleCipherCell(requestedMode, initializationVectorSupplied: ivRef is not null);
        }

        return new SymEncryptRequestBody
        {
            Data = data.ToWireData(),
            PayloadForm = data.Form,
            KeyRef = keyRef,
            IvRef = ivRef,
            CipherType = cipherType,
            Mode = mode,
        };
    }

    /// <summary>
    /// Builds and validates the request body both RSA cipher directions share.
    /// </summary>
    /// <param name="data">The payload.</param>
    /// <param name="keyRef">The opaque key handle.</param>
    /// <param name="padding">The padding selector, or <see langword="null"/> to omit it.</param>
    /// <returns>The request body.</returns>
    /// <remarks>
    /// The padding check is where preserved legacy weakness 3 becomes a DEFINED ERROR. The legacy
    /// declares exactly two padding constants [enums.sru:L949-L950] and no no-padding constant, so a
    /// request for no padding - by any value outside those two - is refused rather than forwarded. A
    /// null omits the member and thereby selects PKCS#1 v1.5 on the service side (weakness 2,
    /// [enums.sru:L951]).
    /// </remarks>
    private static RsaCipherRequestBody BuildRsaCipherRequest(
        CryptoPayload data,
        string keyRef,
        long? padding)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentException.ThrowIfNullOrWhiteSpace(keyRef);

        if (padding is long requestedPadding)
        {
            EnsureDeclaredPadding(requestedPadding);
        }

        return new RsaCipherRequestBody
        {
            Data = data.ToWireData(),
            PayloadForm = data.Form,
            KeyRef = keyRef,
            Padding = padding,
        };
    }

    // ==============================================================================================
    //  TRANSPORT - the one place a request is actually sent
    // ==============================================================================================

    /// <summary>
    /// Sends one published operation and reads its conforming response.
    /// </summary>
    /// <typeparam name="TRequest">The request body type for the operation.</typeparam>
    /// <typeparam name="TResponse">The success response body type for the operation.</typeparam>
    /// <param name="path">The published relative path.</param>
    /// <param name="body">The request body.</param>
    /// <param name="operationId">
    /// The published operation identifier, carried into every diagnostic and onto the failure type so a
    /// caller can attribute a refusal without parsing a message.
    /// </param>
    /// <param name="cancellationToken">Cancels the request and the reading of its response.</param>
    /// <returns>The response body and the status it arrived with.</returns>
    /// <remarks>
    /// <para>
    /// THIS OVERLOAD IS THE AUTHENTICATED ONE, AND IT IS THE ONE ALL SEVENTEEN C-02 OPERATIONS USE. It
    /// obtains a bearer credential for Security's own audience and attaches it to the request before
    /// sending. Every C-02 operation inherits the document-level bearer requirement in the published
    /// contract, so an unauthenticated crypto call is not merely unwise - once Security enforces its own
    /// contract, every one of them answers 401 and the entire cryptographic surface is unreachable.
    /// </para>
    /// <para>
    /// THE ONE OPERATION THAT MUST NOT USE IT IS TOKEN ISSUANCE, which is why the split is expressed as
    /// two differently NAMED members rather than as a flag with a default. A credential cannot be
    /// presented in order to obtain the first credential, so
    /// <see cref="SendWithoutCredentialAsync{TRequest, TResponse}"/> exists for that one call - and,
    /// mechanically, routing issuance through here instead would recurse without bound: acquiring a
    /// token would require a token. A name makes that mistake visible at the call site; a boolean
    /// parameter would not.
    /// </para>
    /// <para>
    /// EVERY NON-SUCCESS STATUS IS A DEFINITIVE ANSWER AND IS SURFACED AS A TYPED FAILURE. Nothing is
    /// retried here: transient transport faults are the composition root's resilience handler's
    /// concern, and a 4xx is a refusal that repeating cannot change.
    /// </para>
    /// <para>
    /// The single diagnostic this method writes carries the operation identifier and nothing else. NO
    /// REQUEST BODY OR RESPONSE BODY IS LOGGED, on any operation, because every one of them may carry
    /// plaintext, ciphertext, a digest, a signature, a generated key or an opaque reference. NEITHER IS
    /// THE CREDENTIAL, nor the header that carries it.
    /// </para>
    /// </remarks>
    private async Task<(TResponse Payload, System.Net.HttpStatusCode StatusCode)> InvokeAsync<TRequest, TResponse>(
        Uri path,
        TRequest body,
        string operationId,
        CancellationToken cancellationToken)
        where TRequest : class
        where TResponse : class
    {
        // Obtained through the cached public path, so seventeen operations share one credential and one
        // issuance. The token's OWN reported type is used as the scheme rather than a literal: the
        // contract pins it with a schema constant and this client already validates it on arrival, so
        // reusing it keeps a single source of truth instead of two that can disagree.
        ServiceToken credential = await GetTokenAsync(CryptoTokenRequest, cancellationToken)
            .ConfigureAwait(false);

        return await SendAsync<TRequest, TResponse>(
                path,
                body,
                operationId,
                new AuthenticationHeaderValue(credential.TokenType, credential.AccessToken),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Sends one request with the CALLER credential rather than a bearer credential - the
    /// token-issuance path, and only that path.
    /// </summary>
    /// <typeparam name="TRequest">The request body type.</typeparam>
    /// <typeparam name="TResponse">The response body type.</typeparam>
    /// <param name="path">The operation's path, relative to the configured base address.</param>
    /// <param name="body">The request body.</param>
    /// <param name="operationId">The published operation identifier.</param>
    /// <param name="cancellationToken">Cancels the request and the reading of its response.</param>
    /// <returns>The response body and the status it arrived with.</returns>
    /// <remarks>
    /// <para>
    /// The operation this exists for accepts a CALLER credential and not a bearer token, because a
    /// caller cannot present a bearer token in order to obtain its first bearer token. C-01 publishes
    /// two schemes for it and accepts either, so this method presents whichever one the deployment
    /// configured: an HTTP <c>Basic</c> credential when an issuance secret is present, and otherwise no
    /// header at all, leaving the client certificate on the primary handler to authenticate the
    /// handshake. Presenting neither is not a state a started process can be in -
    /// <c>Configuration/DataServicesOptions.cs</c>'s validator refuses such a deployment - so the null
    /// case here is the certificate case rather than an anonymous call.
    /// </para>
    /// <para>
    /// THE CREDENTIAL IS BUILT PER REQUEST AND NOT CACHED. It is two short strings and one base64
    /// encode, so caching it would trade nothing measurable for a field holding a credential for the
    /// lifetime of the client; and building it per request means a configuration reload is picked up on
    /// the next issuance rather than at the next process start. It is written to the request message and
    /// nowhere else - not to the client's default headers, which are instance-wide and would publish it
    /// to all seventeen C-02 calls as well.
    /// </para>
    /// </remarks>
    private Task<(TResponse Payload, System.Net.HttpStatusCode StatusCode)>
        SendWithIssuanceCredentialAsync<TRequest, TResponse>(
            Uri path,
            TRequest body,
            string operationId,
            CancellationToken cancellationToken)
        where TRequest : class
        where TResponse : class =>
        SendAsync<TRequest, TResponse>(
            path,
            body,
            operationId,
            BuildIssuanceCredential(),
            cancellationToken);

    /// <summary>
    /// Builds the HTTP <c>Basic</c> issuance credential, or <see langword="null"/> when this deployment
    /// authenticates the issuance edge with a client certificate instead.
    /// </summary>
    /// <returns>
    /// A <c>Basic</c> credential naming <see cref="TokenSubject"/> and the configured secret, or
    /// <see langword="null"/> when no secret is configured.
    /// </returns>
    /// <remarks>
    /// <para>
    /// RFC 7617's ENCODING, EXACTLY. The user-id and the password are joined by a single colon and the
    /// result is base64 of its UTF-8 bytes. The user-id is <see cref="TokenSubject"/>, which contains no
    /// colon - the specification forbids one there - so the receiver's split on the FIRST colon
    /// recovers both halves even if the secret itself contains colons. UTF-8 is the charset Security
    /// decodes with, so the two sides agree byte for byte and a non-ASCII secret survives the round
    /// trip.
    /// </para>
    /// <para>
    /// THE SECRET IS READ FROM OPTIONS AT THE MOMENT OF USE AND HELD IN NO FIELD. It arrives through the
    /// flat configuration key <see cref="SecurityClientOptions.ClientSecretConfigurationKey"/>, applied
    /// to the bound options by an explicit post-configure step in the composition root, so it appears in
    /// no settings file and in no container definition. It is not logged here or anywhere else, and it
    /// is not included in any exception message: a failure to authenticate is reported by Security as a
    /// status, and repeating the secret into a diagnostic would put it in an operator's log.
    /// </para>
    /// </remarks>
    private AuthenticationHeaderValue? BuildIssuanceCredential()
    {
        string secret = _options.Value.Security?.ClientSecret ?? string.Empty;

        if (string.IsNullOrWhiteSpace(secret))
        {
            // The certificate path. The handler carries the identity; nothing belongs in the header.
            return null;
        }

        string parameter = Convert.ToBase64String(
            Encoding.UTF8.GetBytes(string.Concat(TokenSubject, ":", secret)));

        return new AuthenticationHeaderValue(BasicSchemeToken, parameter);
    }

    /// <summary>
    /// The single transport step both senders share: build the request, send it, and project the
    /// response.
    /// </summary>
    /// <typeparam name="TRequest">The request body type.</typeparam>
    /// <typeparam name="TResponse">The response body type.</typeparam>
    /// <param name="path">The operation's path, relative to the configured base address.</param>
    /// <param name="body">The request body.</param>
    /// <param name="operationId">The published operation identifier.</param>
    /// <param name="credential">
    /// The credential to present, or <see langword="null"/> to present none. Whether this is null is the
    /// ONLY difference between the two senders, and the decision is made by the caller's choice of
    /// sender rather than anywhere in here.
    /// </param>
    /// <param name="cancellationToken">Cancels the request and the reading of its response.</param>
    /// <returns>The response body and the status it arrived with.</returns>
    /// <remarks>
    /// A PER-CALL REQUEST MESSAGE, NOT A HEADER ON THE SHARED CLIENT. A typed client instance can serve
    /// several concurrent requests, and its default headers are instance-wide mutable state: setting a
    /// credential there would publish one call's credential to every other call in flight and would race
    /// on refresh. Carrying it on the message keeps it scoped to the one request it was obtained for.
    /// </remarks>
    private async Task<(TResponse Payload, System.Net.HttpStatusCode StatusCode)> SendAsync<TRequest, TResponse>(
        Uri path,
        TRequest body,
        string operationId,
        AuthenticationHeaderValue? credential,
        CancellationToken cancellationToken)
        where TRequest : class
        where TResponse : class
    {
        EnsureBaseAddress(operationId);
        _logger.LogDebug("Invoking the Security service operation {OperationId}.", operationId);

        using HttpRequestMessage request = new(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body, mediaType: null, WireJson),
        };

        // Assigned rather than added, so there is exactly one Authorization header and no possibility of
        // a second one being appended by a later change.
        request.Headers.Authorization = credential;

        using HttpResponseMessage response = await _httpClient
            .SendAsync(request, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw await CreateFailureAsync(response, operationId, cancellationToken).ConfigureAwait(false);
        }

        TResponse? payload;
        try
        {
            payload = await response.Content
                .ReadFromJsonAsync<TResponse>(WireJson, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (JsonException exception)
        {
            // Reaches here when a required member is absent or a member carries the wrong JSON type.
            // The serializer enforces the schema's own required list, so this is contract conformance
            // rather than defensive padding.
            throw new SecurityClientException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The Security service returned a response to '{operationId}' that does not match "
                    + $"the shape the published contract declares."),
                exception)
            {
                OperationId = operationId,
                StatusCode = (int)response.StatusCode,
            };
        }

        if (payload is null)
        {
            throw new SecurityClientException(string.Create(
                CultureInfo.InvariantCulture,
                $"The Security service returned an empty body for a successful '{operationId}' "
                + $"response."))
            {
                OperationId = operationId,
                StatusCode = (int)response.StatusCode,
            };
        }

        return (payload, response.StatusCode);
    }

    /// <summary>
    /// Refuses to send anything at all when the typed client carries no base address, naming the
    /// setting that is missing and which of two configuration faults occurred.
    /// </summary>
    /// <param name="operationId">The operation that could not be reached.</param>
    /// <exception cref="InvalidOperationException">No base address is configured.</exception>
    /// <remarks>
    /// FAIL FAST, AND NAME THE CAUSE RATHER THAN THE SYMPTOM. The base address is the composition
    /// root's responsibility; discovering its absence as a relative-URI failure deep inside the
    /// transport would report that a request could not be built rather than that a service was never
    /// configured. This is also the ONE place the bound options are read, and it reads them only to
    /// tell the two faults apart - it composes no address from them, so the composition root's
    /// registration remains the single authority over where a request goes.
    /// </remarks>
    private void EnsureBaseAddress(string operationId)
    {
        if (_httpClient.BaseAddress is not null)
        {
            return;
        }

        SecurityClientOptions? security = _options.Value.Security;
        string diagnosis = string.IsNullOrWhiteSpace(security?.BaseAddress)
            ? "no value is bound to that key either, so the setting itself is missing"
            : "a value IS bound to that key, so the typed client was registered without applying it";

        throw new InvalidOperationException(string.Create(
            CultureInfo.InvariantCulture,
            $"No base address is configured on the Security typed client, so the '{operationId}' "
            + $"operation cannot be reached. Set '{SecurityAddressConfigurationKey}' and register this "
            + $"typed client with that address in the composition root; {diagnosis}."));
    }

    /// <summary>
    /// Refuses to ask for a token when this deployment configures no client identity to present,
    /// naming the two settings an operator must supply.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Neither <c>DataServices:Security:MutualTls:CertificatePath</c> nor its key sibling is set.
    /// </exception>
    /// <remarks>
    /// <para>
    /// NAME THE CAUSE, NOT THE SYMPTOM - THE SAME RULE <see cref="EnsureBaseAddress"/> FOLLOWS.
    /// Contract C-01 protects the issuance endpoint with mutual TLS and with nothing else, because a
    /// caller cannot present a bearer token in order to obtain its first bearer token. A request sent
    /// with no client certificate therefore cannot succeed - it is refused for want of a caller
    /// identity - and the refusal arrives as an ordinary authentication failure that reads like a
    /// credential problem at Security rather than a missing setting here. Checking first turns that into
    /// a message naming the two keys, which is the difference between an operator fixing configuration
    /// and an operator investigating the wrong service.
    /// </para>
    /// <para>
    /// This reads the bound options ONLY to decide whether an identity is configured. It does not open,
    /// load or touch the material, and it never quotes either path: a path names where a private key is
    /// mounted, and neither a log nor an exception message is a place to publish that. Loading and
    /// validating the pair belongs to the composition root, which does it once at startup.
    /// </para>
    /// </remarks>
    private void EnsureClientIdentityConfigured()
    {
        if (_options.Value.Security?.MutualTls.IsConfigured != false)
        {
            return;
        }

        throw new InvalidOperationException(string.Create(
            CultureInfo.InvariantCulture,
            $"This host presents no client certificate, so it cannot obtain a service token: the "
            + $"issuance endpoint is authenticated by mutual TLS and by nothing else, and a "
            + $"certificate-less request can only be refused. Set both "
            + $"'{ClientCertificateConfigurationKey}' and '{ClientCertificateKeyConfigurationKey}' to "
            + $"the material this deployment mounts. Neither path is reproduced in this message, "
            + $"because a diagnostic must not record where key material is mounted."));
    }

    // ==============================================================================================
    //  RESPONSE PROJECTION
    // ==============================================================================================

    /// <summary>
    /// Reconstructs a payload from a response that reports its own form.
    /// </summary>
    /// <param name="payload">The response body.</param>
    /// <param name="operationId">The operation the response answered.</param>
    /// <param name="statusCode">The status the response carried.</param>
    /// <returns>The reconstructed payload, in the family the service reported.</returns>
    /// <remarks>
    /// <para>
    /// The service's own reported form is honoured rather than the request's, so that a response which
    /// answered in the other family is reported as such instead of being silently reinterpreted.
    /// </para>
    /// <para>
    /// Honouring it is not the same as trusting it. Two off-contract shapes are translated into the
    /// typed failure here rather than escaping as raw framework exceptions: a blob-shaped payload whose
    /// data is not base64, and a form value that names no declared member. Both are the service
    /// answering outside its own published schema, so both carry the operation identifier and the status
    /// the response arrived with, which is what lets a caller attribute the fault without parsing a
    /// message.
    /// </para>
    /// </remarks>
    private static CryptoPayload ToPayload(
        PayloadResponseBody payload,
        string operationId,
        System.Net.HttpStatusCode statusCode)
    {
        try
        {
            return CryptoPayload.FromWire(payload.PayloadForm, payload.Data);
        }
        catch (FormatException exception)
        {
            throw new SecurityClientException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The Security service answered '{operationId}' with a blob-shaped payload whose "
                    + $"data member is not valid base64, which the published contract does not permit."),
                exception)
            {
                OperationId = operationId,
                StatusCode = (int)statusCode,
            };
        }
        catch (ArgumentOutOfRangeException exception)
        {
            // The reconstruction refused an undefined form value. Reported as an off-contract response,
            // which is what it is: the payload is NOT decoded in either family, because guessing one
            // would produce a result the service never described.
            throw new SecurityClientException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The Security service answered '{operationId}' with a payload form that names no "
                    + $"value the published contract declares, so the payload was not decoded."),
                exception)
            {
                OperationId = operationId,
                StatusCode = (int)statusCode,
            };
        }
    }

    /// <summary>
    /// Decodes the base64 JSON TRANSPORT encoding of a byte-valued response member.
    /// </summary>
    /// <param name="data">The member exactly as the response carried it.</param>
    /// <param name="operationId">The operation the response answered.</param>
    /// <param name="statusCode">The status the response carried.</param>
    /// <returns>The decoded bytes.</returns>
    /// <remarks>
    /// This is the base64 that exists BECAUSE JSON HAS NO BINARY TYPE, and it is not the
    /// <c>CRYPTO_ENCODING_*</c> argument of the two conversion operations. The distinction is stated
    /// here as well as on <see cref="PayloadForm"/> because this method is where a reader is most
    /// likely to conflate them: a request to decode a HEX string is answered over a BASE64 member, and
    /// both statements are true at once.
    /// </remarks>
    private static byte[] DecodeTransportBase64(
        string data,
        string operationId,
        System.Net.HttpStatusCode statusCode)
    {
        try
        {
            return Convert.FromBase64String(data);
        }
        catch (FormatException exception)
        {
            throw new SecurityClientException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The Security service answered '{operationId}' with a byte member that is not "
                    + $"valid base64, which the published contract does not permit."),
                exception)
            {
                OperationId = operationId,
                StatusCode = (int)statusCode,
            };
        }
    }

    /// <summary>
    /// Builds the failure for a non-success response, projecting the contract's single error body.
    /// </summary>
    /// <param name="response">The unsuccessful response.</param>
    /// <param name="operationId">The operation that was refused.</param>
    /// <param name="cancellationToken">Cancels reading the error body.</param>
    /// <returns>The typed failure to raise.</returns>
    /// <remarks>
    /// Returns the exception rather than raising it so the call site reads as an explicit
    /// <c>throw</c>, which keeps the control flow of the transport path visible at the point it
    /// changes.
    /// </remarks>
    private static async Task<SecurityClientException> CreateFailureAsync(
        HttpResponseMessage response,
        string operationId,
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
            $"The Security service refused the '{operationId}' request with HTTP status {statusCode}."
            + $"{summary}"))
        {
            OperationId = operationId,
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
    /// A missing or unreadable body degrades to <see langword="null"/> rather than masking the
    /// underlying refusal - the status code is the substantive answer, and losing it behind a
    /// deserialization failure would be strictly worse. THAT PROPERTY IS THE WHOLE POINT OF THE CATCH
    /// LIST BELOW, AND ITS THIRD ENTRY WAS ADDED BECAUSE A TEST PROVED THE LIST INCOMPLETE: a response
    /// declaring a JSON media type with a character set this runtime cannot resolve raises
    /// <see cref="InvalidOperationException"/> rather than either of the other two, and without it a
    /// clean typed refusal carrying the status was replaced by an unrelated exception carrying nothing.
    /// All three are the same condition - the body cannot be read - and all three are absorbed.
    /// </para>
    /// <para>
    /// The three are absorbed HERE ONLY, inside a single reading call whose result is optional. Nothing
    /// else in this file catches any of them, so the fail-fast configuration diagnostic - which is also
    /// an <see cref="InvalidOperationException"/> - is raised before a request is ever sent and cannot
    /// reach this method. A CANCELLATION IS DELIBERATELY NOT CAUGHT and propagates unchanged.
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
            // The declared character set cannot be resolved, so the bytes cannot be decoded.
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
    /// convey - and because null is the only honest way to say "the response did not tell us", which
    /// the predicates on the failure type then treat as neither succeeded nor failed.
    /// </remarks>
    /// <remarks>
    /// Internal rather than private so the sibling test project can drive the two integral arms
    /// directly. They are unreachable through the transport, because deserialization always parks an
    /// unmatched member as a <see cref="JsonElement"/>; they exist for a body constructed in process,
    /// and internal visibility is how that is exercised without widening the public surface.
    /// </remarks>
    internal static long? ReadRetCode(ProblemDetails? problem)
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
    /// <param name="operationId">The operation that answered.</param>
    /// <param name="problem">A short clause describing what the body carried.</param>
    /// <param name="statusCode">The status the response carried.</param>
    /// <returns>The typed failure to raise.</returns>
    private static SecurityClientException Malformed(
        string operationId,
        string problem,
        System.Net.HttpStatusCode statusCode) =>
        new(string.Create(
            CultureInfo.InvariantCulture,
            $"The Security service returned a '{operationId}' response that {problem}, which the "
            + $"published contract does not permit."))
        {
            OperationId = operationId,
            StatusCode = (int)statusCode,
        };

    // ==============================================================================================
    //  ARGUMENT CONFORMANCE - every selector checked against the LEGACY CONSTANT CATALOGUE
    //
    //  Each check below compares against the constants published by the shared kernel, which carry the
    //  legacy values and the legacy identifier spellings verbatim. No value is transcribed as a literal
    //  here, so a selector cannot drift from the oracle without the kernel drifting first, and no
    //  SCREAMING_SNAKE identifier is DECLARED in this file - the naming-analyzer suppressions that
    //  permit those spellings are scoped to the kernel's own files and to no file of this service.
    // ==============================================================================================

    /// <summary>
    /// Refuses a hash selector that is not one of the six the legacy declares. Used by the two
    /// UNKEYED digest operations, where every declared member is genuinely usable.
    /// </summary>
    /// <param name="hashType">The selector to check.</param>
    /// <exception cref="ArgumentOutOfRangeException">The selector is not a declared member.</exception>
    /// <remarks>
    /// <para>
    /// Preserved legacy weakness 8: <c>CRYPTO_HASH_MD5</c> IS ACCEPTED, and so is
    /// <c>CRYPTO_HASH_CRC32</c> - a checksum rather than a cryptographic hash. Neither is filtered
    /// out here, because computing either digest is a well-defined operation that the legacy offers
    /// and this port performs identically.
    /// </para>
    /// <para>
    /// THE KEYED AND SIGNING OPERATIONS USE <see cref="EnsureKeyedHashType"/> INSTEAD. The oracle
    /// declares one set for all of them [enums.sru:L927], but a checksum cannot be keyed or signed -
    /// see that method for why that is an absent construction rather than a policy choice.
    /// </para>
    /// </remarks>
    private static void EnsureDeclaredHashType(long hashType)
    {
        if (hashType is not (Enums.CRYPTO_HASH_MD5
            or Enums.CRYPTO_HASH_SHA1
            or Enums.CRYPTO_HASH_SHA256
            or Enums.CRYPTO_HASH_SHA384
            or Enums.CRYPTO_HASH_SHA512
            or Enums.CRYPTO_HASH_CRC32))
        {
            throw new ArgumentOutOfRangeException(
                nameof(hashType),
                hashType,
                "The hash selector must be one of the six values the legacy constant set declares.");
        }
    }

    /// <summary>
    /// Refuses a hash selector that the KEYED and SIGNING operations cannot honour: anything outside
    /// the declared set, and additionally <c>CRYPTO_HASH_CRC32</c>.
    /// </summary>
    /// <param name="hashType">The selector to check.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The selector is not a declared member, or is the checksum member, which has no keyed or
    /// signed form.
    /// </exception>
    /// <remarks>
    /// <para>
    /// WHY THE CHECKSUM MEMBER IS EXCLUDED, AND WHY THAT IS NOT A NARROWING OF THE LEGACY'S INTENT.
    /// The oracle's comment at <c>enums.sru:L927</c> names one set for
    /// <c>Hash/RSASign/VerifyRSASign</c>, so the legacy DECLARED the checksum as legal for all of
    /// them. But there is no construction to perform: CRC32 has no compression function for a keyed
    /// digest to key and no digest-algorithm identifier for a signature scheme to name. Standard
    /// HMAC-CRC32 and RSA-over-CRC32 were never defined - this is an absent construction, not an
    /// option this platform withholds.
    /// </para>
    /// <para>
    /// WHY THE CHECK BELONGS HERE, ON THE CALLER'S SIDE. The service refuses this selector too, but
    /// it can only do so after a request has crossed the network. Rejecting it at construction turns
    /// a remote failure into a local, synchronous argument error at the earliest point it can be
    /// detected, and it means this client cannot emit a request the published contract declares
    /// invalid: <c>hashType</c> on these four operations resolves to <c>CryptoKeyedHashType</c>,
    /// whose enumeration omits the checksum member.
    /// </para>
    /// <para>
    /// It delegates the declared-set half rather than restating it, so the two checks cannot drift on
    /// which identifiers exist while differing - as they must - on which are usable here.
    /// </para>
    /// </remarks>
    private static void EnsureKeyedHashType(long hashType)
    {
        EnsureDeclaredHashType(hashType);

        if (hashType == Enums.CRYPTO_HASH_CRC32)
        {
            throw new ArgumentOutOfRangeException(
                nameof(hashType),
                hashType,
                "The checksum hash selector has no keyed or signed form: no standard construction "
                + "keys a checksum or names one as a signature digest. The keyed and signing "
                + "operations accept the declared set without it, as the published contract's "
                + "CryptoKeyedHashType schema records.");
        }
    }

    /// <summary>
    /// Refuses a cipher selector that is not one of the five the legacy declares.
    /// </summary>
    /// <param name="cipherType">The selector to check.</param>
    /// <exception cref="ArgumentOutOfRangeException">The selector is not a declared member.</exception>
    private static void EnsureDeclaredCipherType(long cipherType)
    {
        if (cipherType is not (Enums.CRYPTO_SYMCRYPT_TYPE_DES
            or Enums.CRYPTO_SYMCRYPT_TYPE_3DES
            or Enums.CRYPTO_SYMCRYPT_TYPE_AES128
            or Enums.CRYPTO_SYMCRYPT_TYPE_AES192
            or Enums.CRYPTO_SYMCRYPT_TYPE_AES256))
        {
            throw new ArgumentOutOfRangeException(
                nameof(cipherType),
                cipherType,
                "The cipher selector must be one of the five values the legacy constant set declares.");
        }
    }

    /// <summary>
    /// Refuses a cipher mode that is not one of the three the legacy declares.
    /// </summary>
    /// <param name="mode">The mode to check. Called only when a mode was actually supplied.</param>
    /// <exception cref="ArgumentOutOfRangeException">The mode is not a declared member.</exception>
    /// <remarks>
    /// Preserved legacy weaknesses 1 and 6. The set is exactly ECB, CBC and CFB
    /// [enums.sru:L943-L945]: <c>CRYPTO_SYMCRYPT_MODE_DEFAULT</c> is ECB [enums.sru:L946] and applies
    /// when the member is ABSENT, and there is NO AUTHENTICATED MODE to select - no GCM, CCM or
    /// Poly1305 - so no such value is accepted here. That absence is the mechanical proof of the
    /// weakness rather than an omission from this check.
    /// </remarks>
    private static void EnsureDeclaredMode(long mode)
    {
        if (mode is not (Enums.CRYPTO_SYMCRYPT_MODE_ECB
            or Enums.CRYPTO_SYMCRYPT_MODE_CBC
            or Enums.CRYPTO_SYMCRYPT_MODE_CFB))
        {
            throw new ArgumentOutOfRangeException(
                nameof(mode),
                mode,
                "The cipher mode must be one of the three values the legacy constant set declares. "
                + "There is no authenticated mode to select, because the legacy declares none.");
        }
    }

    /// <summary>
    /// Refuses a symmetric cell whose behaviour is not reproducible: any use of the feedback mode, and
    /// the vector-consuming mode without a vector reference.
    /// </summary>
    /// <param name="mode">The cipher mode, already checked against the declared set.</param>
    /// <param name="initializationVectorSupplied">
    /// Whether a vector reference accompanies the request.
    /// </param>
    /// <exception cref="NotSupportedException">
    /// The cell is blocked. The message names the same reason code the published contract carries.
    /// </exception>
    /// <remarks>
    /// <para>
    /// THE IDENTIFIER SET IS UNTOUCHED - <see cref="EnsureDeclaredMode"/> above still accepts all
    /// three declared modes, because it answers "is this a legacy identifier?" and the answer is yes
    /// for every one of them. THIS is the separate capability question, and the two are kept apart
    /// deliberately so that neither refusal is mistaken for the other.
    /// </para>
    /// <para>
    /// WHY THESE TWO CELLS. Both need a parameter the legacy never published and that nothing in this
    /// repository records. The feedback mode carries no feedback-size argument, yet full-block and
    /// 8-bit feedback produce entirely different ciphertext. The eight overloads that take a mode but
    /// no vector must get a vector from somewhere, and which one the closed binary
    /// [n_crypto.sru:L8] chose is unobservable. Either wrong choice ROUND-TRIPS PERFECTLY against
    /// itself, so a caller would receive ciphertext that passes every available check and that the
    /// legacy cannot decrypt - data loss wearing the appearance of success. The governing rule is that
    /// a contract is narrowed with a defined error, never widened with a guess.
    /// </para>
    /// <para>
    /// WHY <see cref="NotSupportedException"/> RATHER THAN AN ARGUMENT EXCEPTION. Nothing is wrong with
    /// the arguments: the request is well-formed, every value is a declared identifier, and the
    /// operation would have succeeded. The limitation is this port's, and the exception type says so.
    /// </para>
    /// <para>
    /// THE REASON CODES ARE THE CONTRACT'S OWN, reproduced as text rather than shared as a type,
    /// because constraint C-A forbids this service from referencing the security service where the
    /// equivalent classification lives. One vocabulary, no shared code.
    /// </para>
    /// </remarks>
    private static void EnsureReproducibleCipherCell(long mode, bool initializationVectorSupplied)
    {
        if (mode == Enums.CRYPTO_SYMCRYPT_MODE_CFB)
        {
            throw new NotSupportedException(
                "SYMMETRIC_FEEDBACK_WIDTH_UNPROVABLE: the feedback width the legacy binary used is "
                + "not determined by anything in this repository, and the candidate widths produce "
                + "entirely different ciphertext. This cell is blocked rather than encrypted under a "
                + "guessed width. Select the codebook mode, or the chaining mode with a vector.");
        }

        if (!initializationVectorSupplied && mode == Enums.CRYPTO_SYMCRYPT_MODE_CBC)
        {
            throw new NotSupportedException(
                "SYMMETRIC_VECTOR_UNPROVABLE: the initialization vector the legacy binary substituted "
                + "when a mode was supplied without one is not determined by anything in this "
                + "repository. This cell is blocked rather than encrypted under a guessed vector. "
                + "Supply a vector reference, or select the codebook mode, which consumes none.");
        }
    }

    /// <summary>
    /// Refuses an RSA padding value that is not one of the two the legacy declares.
    /// </summary>
    /// <param name="padding">The padding to check. Called only when a padding was actually supplied.</param>
    /// <exception cref="ArgumentOutOfRangeException">The padding is not a declared member.</exception>
    /// <remarks>
    /// THIS IS THE DEFINED ERROR THAT REFUSES NO-PADDING (preserved legacy weakness 3). The legacy
    /// declares exactly two constants, <c>CRYPTO_RSA_PADDING_PKCS1</c> and
    /// <c>CRYPTO_RSA_PADDING_OAEP</c> [enums.sru:L949-L950], and no no-padding constant - so a request
    /// for it has no representable value and is refused here rather than being invented into the
    /// surface. Weakness 2: <c>CRYPTO_RSA_PADDING_DEFAULT</c> is PKCS#1 v1.5 [enums.sru:L951] and
    /// applies when the member is absent.
    /// </remarks>
    private static void EnsureDeclaredPadding(long padding)
    {
        if (padding is not (Enums.CRYPTO_RSA_PADDING_PKCS1 or Enums.CRYPTO_RSA_PADDING_OAEP))
        {
            throw new ArgumentOutOfRangeException(
                nameof(padding),
                padding,
                "The RSA padding must be one of the two values the legacy constant set declares. "
                + "No-padding is not selectable, because the legacy declares no such constant.");
        }
    }

    /// <summary>
    /// Refuses an encoding that is not one of the two the legacy declares.
    /// </summary>
    /// <param name="encoding">The encoding to check.</param>
    /// <exception cref="ArgumentOutOfRangeException">The encoding is not a declared member.</exception>
    /// <remarks>
    /// This is the GENUINE LEGACY ARGUMENT of the two conversion operations [n_crypto.sru:L11-L12] -
    /// <c>CRYPTO_ENCODING_BASE64</c> or <c>CRYPTO_ENCODING_HEX</c> [enums.sru:L924-L925] - and not the
    /// base64 in which bytes travel inside JSON.
    /// </remarks>
    private static void EnsureDeclaredEncoding(long encoding)
    {
        if (encoding is not (Enums.CRYPTO_ENCODING_BASE64 or Enums.CRYPTO_ENCODING_HEX))
        {
            throw new ArgumentOutOfRangeException(
                nameof(encoding),
                encoding,
                "The encoding must be one of the two values the legacy constant set declares.");
        }
    }

    /// <summary>
    /// Refuses a generation size the published contract would reject.
    /// </summary>
    /// <param name="size">The size to check.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The size exceeds <see cref="MaximumRandomSize"/>.
    /// </exception>
    /// <remarks>
    /// The lower bound needs no check: the legacy argument is a 32-bit unsigned value and so is this
    /// parameter, so a negative size is not representable. Only the upper bound - which is the
    /// CONTRACT'S OWN narrowing of the legacy domain, not a policy invented here - is enforced.
    /// </remarks>
    private static void EnsureAcceptedRandomSize(uint size) =>
        ArgumentOutOfRangeException.ThrowIfGreaterThan(size, MaximumRandomSize);
}

// ==================================================================================================
//  THE WIRE TYPES
//
//  HAND-AUTHORED, BECAUSE THE OPENAPI DOCUMENT GENERATES NO CODE AND NO CONTRACT TYPE EXISTS TO
//  IMPORT. PowerFramework.Contracts compiles only its protocol definitions and packages its OpenAPI
//  documents as content; its project directory holds zero .cs files. Adding a generator to avoid
//  hand-authoring these would be an unrequested dependency, so each type below mirrors one published
//  schema in that schema's EXACT member spellings, carried on [JsonPropertyName] so no naming policy
//  can restyle them.
//
//  EACH TYPE HAS EXACTLY THE MEMBERS ITS SCHEMA DECLARES AND NO EXTENSION BAG. That is how
//  `additionalProperties: false` is honoured STRUCTURALLY rather than by review: there is nowhere for
//  an extra member - least of all a key, an initialization vector, a passphrase, a password or a
//  certificate - to be added without editing a declaration and being seen doing it.
//
//  `required` IS THE SCHEMA'S OWN REQUIRED LIST, ENFORCED BY THE SERIALIZER. An absent member is
//  refused while reading rather than surfacing later as an empty credential or an empty digest.
//
//  THEY ARE CLASSES RATHER THAN RECORDS, DELIBERATELY, AND THIS IS A SECRET-HANDLING DECISION. A
//  record's compiler-generated ToString prints EVERY MEMBER IT DECLARES, and almost every member below
//  is sensitive - a token, a plaintext, a ciphertext, a digest, a signature, a generated key or an
//  opaque reference. Formatting an object is the most ordinary way such a value reaches a log by
//  accident. Declaring these as classes means the inherited ToString reports the type name and nothing
//  else, which removes the hazard AT THE TYPE and keeps it removed when a future member is added,
//  instead of relying on twenty separate overrides staying correct. Nothing here needs value equality,
//  so nothing is lost by the choice.
// ==================================================================================================

/// <summary>
/// The request body of contract <b>C-01</b>'s issuance operation.
/// </summary>
/// <remarks>
/// EXACTLY THREE MEMBERS, AND WHAT IS ABSENT IS THE POINT. There is no client secret, no password, no
/// API key, no client assertion and no key material, because the operation is authenticated by the
/// client certificate the transport presents and a credential in a body would be meaningless on it.
/// </remarks>
internal sealed class TokenIssuanceRequestBody
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
/// The response body of contract <b>C-01</b>'s issuance operation.
/// </summary>
/// <remarks>
/// <para>
/// The four required members carry the RFC 6749 section 5.1 spellings, deliberately, so that a stock
/// client library parses this object without bespoke code - the same property that made this contract
/// REST rather than gRPC. The request half above does NOT follow that RFC, because it is not a grant
/// request; the asymmetry is intentional and belongs to the contract rather than to this file.
/// </para>
/// <para>
/// There is no refresh token here, because the contract defines none anywhere. A caller that needs
/// another token asks for another token.
/// </para>
/// </remarks>
internal sealed class TokenIssuanceResponseBody
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
    /// an expiry WITHOUT PARSING THE TOKEN.
    /// </summary>
    [JsonPropertyName("issued_at")]
    public long? IssuedAt { get; init; }
}

/// <summary>The request body of <c>POST /v1/crypto/hash</c>. Contract <b>C-02</b>.</summary>
internal sealed class HashRequestBody
{
    /// <summary>The payload, in the form the selector below declares.</summary>
    [JsonPropertyName("data")]
    public required string Data { get; init; }

    /// <summary>Which legacy overload family this call belongs to.</summary>
    [JsonPropertyName("payloadForm")]
    public required PayloadForm PayloadForm { get; init; }

    /// <summary>The hash selector, from the six declared legacy members.</summary>
    [JsonPropertyName("hashType")]
    public required long HashType { get; init; }
}

/// <summary>The request body of <c>POST /v1/crypto/hmac</c>. Contract <b>C-02</b>.</summary>
/// <remarks>
/// <c>keyRef</c> occupies the position the four legacy keyed <c>Hash</c> overloads gave to raw key
/// bytes [n_crypto.sru:L23-L26]. THERE IS NO MEMBER HERE THAT KEY MATERIAL COULD BE PLACED IN.
/// </remarks>
internal sealed class HmacRequestBody
{
    /// <summary>The payload, in the form the selector below declares.</summary>
    [JsonPropertyName("data")]
    public required string Data { get; init; }

    /// <summary>Which legacy overload family this call belongs to.</summary>
    [JsonPropertyName("payloadForm")]
    public required PayloadForm PayloadForm { get; init; }

    /// <summary>An opaque handle the Security service resolves against its own key store.</summary>
    [JsonPropertyName("keyRef")]
    public required string KeyRef { get; init; }

    /// <summary>The hash selector, from the five declared members that have a keyed form.</summary>
    [JsonPropertyName("hashType")]
    public required long HashType { get; init; }
}

/// <summary>The request body of <c>POST /v1/crypto/hash-file</c>. Contract <b>C-02</b>.</summary>
/// <remarks>
/// <c>fileRef</c> occupies the position the legacy gave to a FILENAME [n_crypto.sru:L27]. No path,
/// filename, directory or URL crosses this boundary, which is why there is no path traversal to defend
/// against.
/// </remarks>
internal sealed class HashFileRequestBody
{
    /// <summary>An opaque handle the Security service resolves against its own allow-listed store.</summary>
    [JsonPropertyName("fileRef")]
    public required string FileRef { get; init; }

    /// <summary>The hash selector, from the six declared legacy members.</summary>
    [JsonPropertyName("hashType")]
    public required long HashType { get; init; }
}

/// <summary>The request body of <c>POST /v1/crypto/hmac-file</c>. Contract <b>C-02</b>.</summary>
internal sealed class HmacFileRequestBody
{
    /// <summary>An opaque file handle. See <see cref="HashFileRequestBody.FileRef"/>.</summary>
    [JsonPropertyName("fileRef")]
    public required string FileRef { get; init; }

    /// <summary>An opaque key handle. See <see cref="HmacRequestBody.KeyRef"/>.</summary>
    [JsonPropertyName("keyRef")]
    public required string KeyRef { get; init; }

    /// <summary>The hash selector, from the five declared members that have a keyed form.</summary>
    [JsonPropertyName("hashType")]
    public required long HashType { get; init; }
}

/// <summary>
/// The request body BOTH symmetric operations publish - <c>POST /v1/crypto/symmetric/encrypt</c> and
/// <c>POST /v1/crypto/symmetric/decrypt</c>. Contract <b>C-02</b>.
/// </summary>
/// <remarks>
/// <para>
/// One type serves both directions because the two schemas declare the same members; the operations
/// stay distinct because the contract publishes them as two paths.
/// </para>
/// <para>
/// THE TWO OPTIONAL MEMBERS ARE OMITTED WHEN NULL rather than written as an explicit null, and that is
/// load-bearing. An absent vector reaches the legacy overloads that take none, and an absent mode
/// reaches those that take none and therefore run in ECB [enums.sru:L946]. Writing a null, or
/// substituting the default locally, would collapse a distinction the legacy makes between different
/// overloads. There is no padding member on this type at all, because not one of the thirty-two
/// symmetric overloads has one - the mechanical proof that block padding is not selectable.
/// </para>
/// </remarks>
internal sealed class SymEncryptRequestBody
{
    /// <summary>The payload, in the form the selector below declares.</summary>
    [JsonPropertyName("data")]
    public required string Data { get; init; }

    /// <summary>Which legacy overload family this call belongs to.</summary>
    [JsonPropertyName("payloadForm")]
    public required PayloadForm PayloadForm { get; init; }

    /// <summary>An opaque key handle. See <see cref="HmacRequestBody.KeyRef"/>.</summary>
    [JsonPropertyName("keyRef")]
    public required string KeyRef { get; init; }

    /// <summary>
    /// An opaque handle to an initialization vector, omitted entirely when absent.
    /// </summary>
    [JsonPropertyName("ivRef")]
    public string? IvRef { get; init; }

    /// <summary>The cipher selector, from the five-member legacy set.</summary>
    [JsonPropertyName("cipherType")]
    public required long CipherType { get; init; }

    /// <summary>
    /// The cipher mode, omitted entirely when absent - in which case the service applies ECB.
    /// </summary>
    [JsonPropertyName("mode")]
    public long? Mode { get; init; }
}

/// <summary>
/// The request body BOTH RSA cipher operations publish - <c>POST /v1/crypto/rsa/encrypt</c> and
/// <c>POST /v1/crypto/rsa/decrypt</c>. Contract <b>C-02</b>.
/// </summary>
/// <remarks>
/// <c>keyRef</c> occupies the position the legacy gave to PEM text in <c>readonly string pubkey</c> and
/// <c>readonly string prikey</c> [n_crypto.sru:L62-L69]. The padding member is omitted when absent, in
/// which case the service applies PKCS#1 v1.5 [enums.sru:L951]; it has only two representable values,
/// so a no-padding request cannot be expressed here at all.
/// </remarks>
internal sealed class RsaCipherRequestBody
{
    /// <summary>The payload, in the form the selector below declares.</summary>
    [JsonPropertyName("data")]
    public required string Data { get; init; }

    /// <summary>Which legacy overload family this call belongs to.</summary>
    [JsonPropertyName("payloadForm")]
    public required PayloadForm PayloadForm { get; init; }

    /// <summary>An opaque key handle. See <see cref="HmacRequestBody.KeyRef"/>.</summary>
    [JsonPropertyName("keyRef")]
    public required string KeyRef { get; init; }

    /// <summary>The padding selector, omitted entirely when absent.</summary>
    [JsonPropertyName("padding")]
    public long? Padding { get; init; }
}

/// <summary>The request body of <c>POST /v1/crypto/rsa/sign</c>. Contract <b>C-02</b>.</summary>
internal sealed class RsaSignRequestBody
{
    /// <summary>The payload to sign, in the form the selector below declares.</summary>
    [JsonPropertyName("data")]
    public required string Data { get; init; }

    /// <summary>Which legacy overload family this call belongs to.</summary>
    [JsonPropertyName("payloadForm")]
    public required PayloadForm PayloadForm { get; init; }

    /// <summary>An opaque handle to the signing key, which never leaves the Security service.</summary>
    [JsonPropertyName("keyRef")]
    public required string KeyRef { get; init; }

    /// <summary>
    /// The signature hash selector, from the five declared members that have a signed form
    /// [enums.sru:L927]. MD5 is legal here and is preserved; the checksum member is not, because no
    /// RSA-over-checksum construction exists to implement.
    /// </summary>
    [JsonPropertyName("hashType")]
    public required long HashType { get; init; }
}

/// <summary>The request body of <c>POST /v1/crypto/rsa/verify</c>. Contract <b>C-02</b>.</summary>
/// <remarks>
/// ONE form selector governs both <see cref="Data"/> and <see cref="Signature"/>, because the legacy
/// correlates them and declares no mixed overload [n_crypto.sru:L72-L73].
/// </remarks>
internal sealed class RsaVerifyRequestBody
{
    /// <summary>The payload the signature was computed over.</summary>
    [JsonPropertyName("data")]
    public required string Data { get; init; }

    /// <summary>Which legacy overload family BOTH values below belong to.</summary>
    [JsonPropertyName("payloadForm")]
    public required PayloadForm PayloadForm { get; init; }

    /// <summary>
    /// The signature to verify, in the same form as the payload. Not a credential and not key material;
    /// it is the value being checked.
    /// </summary>
    [JsonPropertyName("signature")]
    public required string Signature { get; init; }

    /// <summary>An opaque handle standing in for the legacy public key.</summary>
    [JsonPropertyName("keyRef")]
    public required string KeyRef { get; init; }

    /// <summary>The signature hash selector.</summary>
    [JsonPropertyName("hashType")]
    public required long HashType { get; init; }
}

/// <summary>The request body of <c>POST /v1/crypto/rsa/keys</c>. Contract <b>C-02</b>.</summary>
/// <remarks>
/// There is no passphrase member, no salt member and no derivation parameter, because the legacy
/// surface has none anywhere - the mechanical proof that no key-derivation function is reachable.
/// </remarks>
internal sealed class GenRsaKeyRequestBody
{
    /// <summary>
    /// The modulus size. Any 16-bit value is legal, exactly as in the legacy; 1024 is not filtered out.
    /// </summary>
    [JsonPropertyName("bits")]
    public required ushort Bits { get; init; }

    /// <summary>
    /// The textual key format switch, omitted entirely when absent - which reaches the three-argument
    /// legacy overload that does not take it [n_crypto.sru:L19].
    /// </summary>
    [JsonPropertyName("pemFormat")]
    public bool? PemFormat { get; init; }
}

/// <summary>The request body of <c>POST /v1/crypto/random/blob</c>. Contract <b>C-02</b>.</summary>
internal sealed class RandomBlobRequestBody
{
    /// <summary>The number of bytes to generate.</summary>
    [JsonPropertyName("size")]
    public required uint Size { get; init; }
}

/// <summary>The request body of <c>POST /v1/crypto/random/string</c>. Contract <b>C-02</b>.</summary>
internal sealed class RndStringRequestBody
{
    /// <summary>The length to generate.</summary>
    [JsonPropertyName("size")]
    public required uint Size { get; init; }

    /// <summary>
    /// The character-class bitmask, omitted entirely when absent - which reaches the single-argument
    /// legacy overload [n_crypto.sru:L15].
    /// </summary>
    [JsonPropertyName("flags")]
    public uint? Flags { get; init; }
}

/// <summary>The request body of <c>POST /v1/crypto/random/guid</c>. Contract <b>C-02</b>.</summary>
/// <remarks>
/// Every member is optional, so this serialises to an EMPTY OBJECT when no flags are supplied - which
/// the contract states reaches the no-argument legacy overload [n_crypto.sru:L17].
/// </remarks>
internal sealed class GuidRequestBody
{
    /// <summary>The formatting bitmask, omitted entirely when absent.</summary>
    [JsonPropertyName("flags")]
    public uint? Flags { get; init; }
}

/// <summary>
/// The request body of <c>POST /v1/crypto/encoding/string-to-blob</c>. Contract <b>C-02</b>.
/// </summary>
internal sealed class StringToBlobRequestBody
{
    /// <summary>The encoded text, carried verbatim.</summary>
    [JsonPropertyName("data")]
    public required string Data { get; init; }

    /// <summary>
    /// The GENUINE LEGACY ENCODING ARGUMENT [n_crypto.sru:L11] - base64 or hex - and not the JSON
    /// transport encoding.
    /// </summary>
    [JsonPropertyName("encoding")]
    public required long Encoding { get; init; }
}

/// <summary>
/// The request body of <c>POST /v1/crypto/encoding/blob-to-string</c>. Contract <b>C-02</b>.
/// </summary>
internal sealed class BlobToStringRequestBody
{
    /// <summary>The bytes to encode, carried as base64 because JSON has no binary type.</summary>
    [JsonPropertyName("data")]
    public required string Data { get; init; }

    /// <summary>The genuine legacy encoding argument [n_crypto.sru:L12] - base64 or hex.</summary>
    [JsonPropertyName("encoding")]
    public required long Encoding { get; init; }
}

/// <summary>
/// The request body of <c>POST /v1/crypto/encoding/blob-reverse</c>. Contract <b>C-02</b>.
/// </summary>
internal sealed class BlobReverseRequestBody
{
    /// <summary>The bytes to reverse, carried as base64 because JSON has no binary type.</summary>
    [JsonPropertyName("data")]
    public required string Data { get; init; }
}

/// <summary>
/// The response body of the four digest operations. Contract <b>C-02</b>.
/// </summary>
/// <remarks>
/// IT CARRIES NO FORM SELECTOR, DELIBERATELY. All six legacy hash overloads return a <c>string</c>
/// [n_crypto.sru:L21-L26], so a blob payload still yields a string digest. Everywhere else on this
/// boundary the result form follows the payload form; the hash family is the exception and it is
/// PRESERVED RATHER THAN HARMONISED.
/// </remarks>
internal sealed class DigestResponseBody
{
    /// <summary>The digest, as the legacy string-shaped overloads return it.</summary>
    [JsonPropertyName("digest")]
    public required string Digest { get; init; }
}

/// <summary>
/// The response body of every operation whose legacy return type follows its input type. Contract
/// <b>C-02</b>.
/// </summary>
internal sealed class PayloadResponseBody
{
    /// <summary>The form the service answered in, which is honoured rather than assumed.</summary>
    [JsonPropertyName("payloadForm")]
    public required PayloadForm PayloadForm { get; init; }

    /// <summary>The payload, interpreted according to the form above.</summary>
    [JsonPropertyName("data")]
    public required string Data { get; init; }
}

/// <summary>
/// The response body of <c>POST /v1/crypto/rsa/verify</c>. Contract <b>C-02</b>.
/// </summary>
internal sealed class RsaVerifyResponseBody
{
    /// <summary>
    /// The legacy boolean outcome. FALSE IS A SUCCESSFUL RESPONSE: the signature does not verify, which
    /// is not the same thing as the request being rejected.
    /// </summary>
    [JsonPropertyName("valid")]
    public required bool Valid { get; init; }
}

/// <summary>
/// The response body of <c>POST /v1/crypto/rsa/keys</c>. Contract <b>C-02</b>.
/// </summary>
/// <remarks>
/// THERE IS NO PRIVATE-KEY MEMBER AND THERE NEVER WILL BE. The legacy returns both keys through two
/// <c>ref string</c> out-parameters [n_crypto.sru:L19-L20]; this boundary returns the public key and an
/// opaque reference to the private one instead, which is a deliberate narrowing rather than an
/// omission.
/// </remarks>
internal sealed class GenRsaKeyResponseBody
{
    /// <summary>The generated PUBLIC key in textual form.</summary>
    [JsonPropertyName("publicKey")]
    public required string PublicKey { get; init; }

    /// <summary>The opaque reference under which the private key is held.</summary>
    [JsonPropertyName("keyRef")]
    public required string KeyRef { get; init; }

    /// <summary>
    /// The modulus size that was generated. Declared as the contract's own integer width and narrowed
    /// to the legacy 16-bit domain only after it has been checked.
    /// </summary>
    [JsonPropertyName("bits")]
    public required long Bits { get; init; }

    /// <summary>The textual format of the public key, when the service reported it.</summary>
    [JsonPropertyName("pemFormat")]
    public bool? PemFormat { get; init; }
}

/// <summary>
/// The response body of the two operations whose legacy overload returns a <c>blob</c>
/// unconditionally - the decode operation at <c>n_crypto.sru:L11</c> and the random-bytes operation at
/// <c>:L14</c>. Contract <b>C-02</b>.
/// </summary>
/// <remarks>
/// There is no form selector here because there was no overload family to choose between. The bytes
/// arrive as base64 BECAUSE JSON HAS NO BINARY TYPE, which is the transport encoding and not the
/// <c>encoding</c> argument of the conversion operations.
/// </remarks>
internal sealed class BlobResponseBody
{
    /// <summary>The bytes, carried as base64.</summary>
    [JsonPropertyName("data")]
    public required string Data { get; init; }
}

/// <summary>
/// The response body of <c>POST /v1/crypto/random/string</c>. Contract <b>C-02</b>.
/// </summary>
internal sealed class RndStringResponseBody
{
    /// <summary>The generated string, drawn from the selected character classes.</summary>
    [JsonPropertyName("value")]
    public required string Value { get; init; }
}

/// <summary>
/// The response body of <c>POST /v1/crypto/random/guid</c>. Contract <b>C-02</b>.
/// </summary>
internal sealed class GuidResponseBody
{
    /// <summary>The generated identifier in the requested textual form.</summary>
    [JsonPropertyName("value")]
    public required string Value { get; init; }
}

/// <summary>
/// The response body of <c>POST /v1/crypto/encoding/blob-to-string</c>. Contract <b>C-02</b>.
/// </summary>
internal sealed class EncodedTextResponseBody
{
    /// <summary>The encoded string, in base64 or hex according to the requested encoding.</summary>
    [JsonPropertyName("value")]
    public required string Value { get; init; }
}

/// <summary>
/// The response body of <c>POST /v1/crypto/encoding/blob-reverse</c>. Contract <b>C-02</b>.
/// </summary>
/// <remarks>
/// TWO MEMBERS, BECAUSE THE LEGACY MUTATES ITS ARGUMENT IN PLACE AND RETURNS ONLY A BOOLEAN
/// [n_crypto.sru:L13]. In-place mutation has no wire representation, so the bytes are the addition the
/// boundary forces and the boolean is the legacy return value carried unchanged.
/// </remarks>
internal sealed class BlobReverseResponseBody
{
    /// <summary>The reversed bytes, carried as base64.</summary>
    [JsonPropertyName("data")]
    public required string Data { get; init; }

    /// <summary>
    /// The legacy boolean return value, unchanged. It may be false in a successful response.
    /// </summary>
    [JsonPropertyName("succeeded")]
    public required bool Succeeded { get; init; }
}
