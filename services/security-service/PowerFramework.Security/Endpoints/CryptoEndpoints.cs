// ==================================================================================================
//  CryptoEndpoints - CONTRACT C-02 security.v1.CryptoService
//  The widest published surface in this service: 18 authenticated REST operations. SEVENTEEN project
//  the seven Crypto/* providers - and therefore 63 of the 65 declarations of the legacy cryptographic
//  class at ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L9-L73 - onto HTTP; the EIGHTEENTH releases a
//  private key this service retained and projects no legacy declaration at all, because the legacy
//  retains nothing to release.
//  ------------------------------------------------------------------------------------------------
//  TWO INSTRUCTIONS DOMINATE EVERYTHING ELSE IN THIS FILE
//
//    1. RAW KEY MATERIAL NEVER CROSSES THE WIRE FROM A CALLER. Every operation that needs a key,
//       an initialization vector or a file takes an OPAQUE REFERENCE that THIS file resolves against
//       this service's own configured store. There is no request member anywhere below into which a
//       key, a passphrase, a vector or an armoured key block could be placed, and no response member
//       through which one could come back - the single exception being the PUBLIC half of a freshly
//       generated pair, which is public by definition and which the contract requires to be returned.
//    2. A PRESERVED LEGACY WEAKNESS IS ANNOTATED, NEVER QUIETLY FIXED. Six of them travel with this
//       surface. Each is published in the operation description so a caller can see the risk, and
//       each behaves exactly as the legacy behaved.
//
//  THE AUTHORITATIVE WIRE DOCUMENT
//  ------------------------------------------------------------------------------------------------
//  shared/PowerFramework.Contracts/OpenApi/security.v1.yaml is authoritative for anything on the
//  wire; where it and docs/CONTRACTS.md disagree, the YAML wins. It is packaged as CONTENT and is not
//  compiled, and the Contracts project's protocol-buffer items cover the gRPC surfaces only, so there
//  is NO generated C# type for C-02: every request and response type below is hand-authored and
//  conforms to that document by review plus the assertions in the sibling test project.
//
//  Everything the document fixes is taken from it verbatim - the 18 paths, the 18 operation
//  identifiers, the member names, the required-member sets, the enumeration values, the security
//  requirement and the declared status codes. Four questions it settles that a reader might otherwise
//  expect to be open here, recorded because each had a plausible alternative:
//
//    * THE 32 SYMMETRIC DECLARATIONS ARE PUBLISHED AS TWO OPERATIONS, not as eight or sixteen. The
//      discriminators are payloadForm, ivRef presence and mode presence; the key and vector FORM
//      halves of the legacy cross-product are collapsed by the reference indirection. See THE
//      KEY-FORM COLLAPSE below for why that collapse loses no observable behaviour.
//    * THE INITIALIZATION VECTOR IS A REFERENCE, NOT A VALUE. ivRef stands where the legacy took
//      `readonly string iv` / `readonly blob iv`, and omitting it selects the no-vector overload
//      family - which is the only sensible choice under the default mode, since ECB consumes none.
//    * RSA KEY GENERATION RETURNS THE PUBLIC KEY AND A NEW REFERENCE, and RETAINS the private key.
//      The legacy returns both halves through two `ref` out-parameters [n_crypto.sru:L19-L20]; that
//      shape cannot cross a network boundary without shipping freshly generated private key material
//      across it, which is exactly what rule 1 above forbids. No private-key export path exists in
//      this contract and none may be added to this file.
//    * BOTH FILE-HASH OPERATIONS DO EXIST - unkeyed at /v1/crypto/hash-file and keyed at
//      /v1/crypto/hmac-file - and both take an opaque fileRef in the position the legacy took
//      `readonly string filename` [n_crypto.sru:L27-L29]. The document is explicit that an earlier
//      revision's omission of the family was the wrong call, because a declared legacy capability
//      that is simply absent leaves a reader unable to tell unsupported from forgotten. What is
//      narrowed is the REACH - the legacy hashes any file its process can open, this hashes any file
//      the deployment has configured - and NO CALLER-SUPPLIED PATH CROSSES THIS BOUNDARY, so there is
//      no traversal surface to defend and no path filter to guess at.
//
//  WHY REST AND NOT gRPC FOR THIS CONTRACT (constraint C-K)
//  ------------------------------------------------------------------------------------------------
//  Decided per service from the shape of the current interface, and for C-02 the shape is decisive:
//  the legacy class is roughly sixty STATELESS REQUEST/RESPONSE overloads with no ordering
//  requirement between calls, no cross-call state and nothing to stream - there is no event chain to
//  sequence and no progressive result to deliver. REST additionally keeps every consumer on the stock
//  Microsoft.AspNetCore.Authentication.JwtBearer handler with zero bespoke code, which is the same
//  reason token issuance and key publication are REST; choosing gRPC here would have forced
//  hand-written key-set retrieval into three services, a net increase in hand-written security code.
//
//  THE SIX ANNOTATED WEAK DEFAULTS, WITH THEIR ORACLE LOCATORS (constraints C-B and C-K)
//  ------------------------------------------------------------------------------------------------
//  Every one is PRESERVED as behaviour and PUBLISHED as an annotation. The annotation text is held in
//  one constant each, below, so the published prose cannot drift between operations and a test can
//  assert each fragment individually against the generated document. The catalogue of the defaults
//  themselves lives in Crypto/LegacyDefaults.cs, which is their single source; this file references
//  it and never restates a value.
//
//    1. ECB IS THE DEFAULT SYMMETRIC MODE. enums.sru:L946 declares CRYPTO_SYMCRYPT_MODE_DEFAULT as
//       CRYPTO_SYMCRYPT_MODE_ECB, so all sixteen mode-omitting legacy overloads run in electronic
//       codebook. Omitting `mode` here runs in ECB too. It is not silently strengthened to CBC.
//    2. PKCS#1 v1.5 IS THE DEFAULT RSA PADDING and NO-PADDING IS NOT SELECTABLE. enums.sru:L949-L951
//       declares exactly two padding constants and a default aliased to the first; there is no
//       no-padding constant at all, which is the MECHANICAL reason a request for it is refused rather
//       than a policy layered on top. OAEP remains selectable and is not promoted to the default.
//    3. BLOCK PADDING IS FIXED AND NOT SELECTABLE. Not one of the 32 symmetric declarations at
//       n_crypto.sru:L30-L61 has a padding parameter, and enums.sru:L936-L946 declares a cipher type
//       and a mode and nothing else, so this contract carries no symmetric padding member: offering
//       one would imply a choice the legacy never had.
//    4. NO KEY-DERIVATION FUNCTION IS REACHABLE AT ALL. No salt, iteration count or derivation
//       parameter appears in any of the 65 declarations at n_crypto.sru:L9-L73 and enums.sru declares
//       no derivation constant, so whatever the store holds IS the key and a passphrase is used as
//       RAW KEY BYTES with no stretching and no salting.
//    5. NO AUTHENTICATED ENCRYPTION, SO CIPHERTEXT CARRIES NO INTEGRITY TAG. The mode set is exactly
//       ECB, CBC and CFB [enums.sru:L943-L945] - no GCM, no CCM, no Poly1305 - and no tag or
//       associated-data parameter appears anywhere in the legacy surface, so there is no member here
//       in which a tag could travel and none is added.
//    6. 1024-BIT RSA REMAINS A LEGAL KEY SIZE. CRYPTO_RSA_BITS_1024 is a declared first-class value
//       [enums.sru:L965] and the legacy's own demonstration generates one. It is accepted, it is
//       annotated, and no minimum-size guard is imposed - imposing one would reject input the legacy
//       accepted, which is the silent correction this port forbids.
//
//  THE ERROR SHAPE, AND WHY NO STATUS IS DERIVED FROM A TRUTHINESS TEST
//  ------------------------------------------------------------------------------------------------
//  Every rejection is built by the ONE factory this folder has, `ProblemResults.Create` in
//  PingEndpoints.cs. No second problem schema is declared here and that factory is not copied. The
//  legacy delivered these conditions through MessageBox and MessageBoxEx dialogs; ONLY THE DELIVERY
//  CHANNEL CHANGES - the text, the severity and, where the legacy had one, the localization category
//  travel in the structured result.
//
//  The status comes from that factory's explicit per-code map, never from a predicate over the code,
//  because the algebra is tri-state and holed: IsSucceeded is `>= OK` and PREVENT is 1
//  [issucceeded.srf:L11-L13, retcode.sru:L42], so A PREVENTION READS AS A SUCCESS; IsFailed excludes
//  CANCELLED explicitly and CANCELLED also fails `>= OK` [isfailed.srf:L11-L13, retcode.sru:L44-L45],
//  so CANCELLED IS NEITHER; both answer false for null; the boolean overloads of IsFailed and
//  IsPrevented are indistinguishable negations [isfailed.srf:L15-L17, isprevented.srf:L15-L17];
//  iscancelled.srf has no null guard; and isallowed.srf alone treats null as permissive and allows
//  anything above 1000.
//
//  THE CONSEQUENCE FOR THIS FILE IS THAT IT TESTS NO RETURN CODE AT ALL. Every handler either succeeds
//  or names an explicit code when it rejects, so there is no comparison here to re-derive and nothing
//  for Shared.Kernel's Predicates to be consumed FOR - importing them would leave an unused symbol
//  while adding no safety. Where a predicate IS needed - by the shared factory, deciding the status -
//  the kernel's own explicit map is what runs, and Formatting.FormatRetCode supplies the symbolic
//  title with its two preserved defects intact: no arm for E_RETRY, and an alias collapse that makes
//  the strings SUCCESS, ALLOW and CANCELED unreachable.
//
//  THE RETURN CODES THIS FILE PRODUCES, AND THE STATUS EACH RESOLVES TO
//  ------------------------------------------------------------------------------------------------
//    E_INVALID_ARGUMENT  (-3)    400  a required member is absent, a number is outside the domain the
//                                     contract declares, a requested size exceeds the published cap,
//                                     or the platform will not generate the requested key size
//    E_NO_SUPPORT        (-2000) 400  a selector outside the legacy's own published set: an
//                                     unpublished hash type, cipher type, mode, padding or encoding,
//                                     THE NO-PADDING REQUEST, and the keyed or signature CRC32 arm
//    E_INVALID_DATA      (-9)    400  a payload that is not valid for its declared form, or
//                                     ciphertext that cannot be decrypted with the referenced key
//    E_ACCESS_DENIED     (-25)   403  the reference is not in the permitted set
//    E_OBJECT_NOT_FOUND  (-16)   404  the reference is permitted but resolves to nothing, or the file
//                                     the deployment configured is not there
//    E_NO_IMPLEMENTATION (-2001) 500  one of the two BLOCKED symmetric cells - see below
//    E_INTERNAL_ERROR    (-27)   500  the resolved material is not a usable key, an encrypt-direction
//                                     cryptographic refusal, or the retained-key store is full
//    E_IO_ERROR          (-31)   500  a configured file could not be read (the map's documented
//                                     default arm, which is a server fault)
//
//  THE DELIBERATE NON-501 MAPPING (constraint C-D)
//  ------------------------------------------------------------------------------------------------
//  HTTP's Not Implemented status IS NEVER PRODUCED BY THIS FILE, and that is a constraint rather than
//  a preference: those four routes belong to GATEWAY's routing metadata for the four deferred
//  capabilities, and answering that status from this service would advertise a deferred capability's
//  surface on a service that has none. Two return codes make the trap concrete and both have
//  deliberate arms in the shared map:
//
//    * E_NO_SUPPORT is 400. The caller selected something this surface does not offer - an
//      unpublished selector, or no-padding, which the legacy declares no constant for - so the fault
//      is in the request. The authored contract anchors this directly: its bad-request response
//      absorbs exactly a request for a value the legacy never declared.
//    * E_NO_IMPLEMENTATION is 500. Used for the two blocked symmetric cells, where the request is
//      well-formed and WOULD have succeeded and the limitation is this port's rather than the
//      caller's, so a client status would send a caller to fix something that is not broken.
//
//  A note for anyone diffing this file against the authored document: the prose inside the
//  CryptoSymCryptMode schema description says a blocked cell "appears" as Not Implemented. That
//  sentence is not the machine-readable contract - the two symmetric operations declare exactly 200,
//  400, 401, 403, 404 and 500 as their responses and declare no 501 - and constraint C-D forbids the
//  status outright on this service. The refusal is therefore 500 carrying E_NO_IMPLEMENTATION plus
//  the document's own machine-readable `reason` code, which preserves everything a caller branches on
//  while keeping the forbidden status out of the estate.
//
//  THE TWO BLOCKED SYMMETRIC CELLS, REFUSED AT THE BOUNDARY RATHER THAN GUESSED AT
//  ------------------------------------------------------------------------------------------------
//  n_crypto is declared `native "pfw.dll"` [n_crypto.sru:L8] and its declarations have no PowerScript
//  body, so two parameters the closed binary chose are unobservable from this repository: the CFB
//  FEEDBACK WIDTH, which the legacy publishes with no feedback-size argument even though full-block
//  and 8-bit CFB produce entirely different ciphertext, and the INITIALIZATION VECTOR substituted by
//  the eight overloads that take a mode but no vector. Either wrong choice is undetectable, because
//  encrypting and decrypting under the same wrong assumption round-trips perfectly and yields
//  ciphertext the legacy cannot decrypt - data loss presented as success. Both cells are therefore
//  BLOCKED, with the reason code the contract publishes, and the classification is
//  LegacyDefaults.ClassifySymmetricCell - the SAME policy the cipher provider screens with, so the
//  boundary and the provider cannot disagree. Nothing here is a second policy.
//
//  THE KEY-FORM COLLAPSE, STATED SO IT IS NOT MISTAKEN FOR A MISSING ARM
//  ------------------------------------------------------------------------------------------------
//  The legacy symmetric families are the cross-product of payload form, KEY FORM, vector presence and
//  mode presence: 16 declarations per direction. This contract exposes payload form, vector presence
//  and mode presence - 8 cells per direction - because a key HAS NO FORM ON THE WIRE: only a
//  reference to one travels, and the material behind it is resolved here.
//
//  THE COLLAPSE IS LOSS-FREE, AND THAT IS PROVABLE RATHER THAN ASSERTED.
//  LegacyDefaults.NormalizeKeyMaterial(string, int) is DEFINED as UTF-8 encoding followed by the span
//  overload, so a text key and its UTF-8 bytes normalise to the identical buffer and the two key-form
//  families cannot produce different ciphertext from the same material. The keyed-digest family is
//  the same case: its provider applies the construction's own key normalisation to whichever form it
//  is handed.
//
//  This file nonetheless exercises BOTH provider families from the boundary rather than picking one:
//  a STRING payload takes the text-key and text-vector overloads, a BLOB payload takes the
//  binary-key and binary-vector overloads. THE VECTOR FORM THEREFORE FOLLOWS THE KEY FORM BY
//  CONSTRUCTION - the legacy pairs a text key only with a text vector and a binary key only with a
//  binary vector, and here a mismatched pair is not merely rejected, IT CANNOT BE EXPRESSED. The
//  sibling test project's provider matrix covers all 32 declarations directly.
//
//  WHAT THIS FILE DELIBERATELY DOES NOT DO
//  ------------------------------------------------------------------------------------------------
//    * NO CRYPTOGRAPHIC IMPLEMENTATION. Not one algorithm, mode, padding scheme, digest, encoding or
//      random draw is implemented here. The seven providers in Crypto/ are the only implementation
//      path, and this file validates, resolves, dispatches and projects - nothing else. In particular
//      randomness is drawn ONLY through RandomProvider, so the injected entropy seam applies and a
//      parity test can substitute a deterministic double; no static randomness API is called.
//    * NO ALLOW-LIST OF ITS OWN. Every selector is screened through LegacyDefaults' predicates, so
//      the accepted sets here are the same sets the providers accept, by construction.
//    * NO CONSTANT OF THE PRESERVED CATALOGUE IS DECLARED. Every CRYPTO_* value is REFERENCED from
//      PowerFramework.Shared.Kernel's Enums or from LegacyDefaults. The repository .editorconfig
//      scopes its naming suppressions to a fixed list of seven files and THIS FILE IS NOT ONE OF
//      THEM, while warnings are errors, so a declaration here would break the build - which is the
//      correct outcome, because the catalogue has exactly one home.
//    * NO HTTP CLIENT, NO REMOTE-PROCEDURE CLIENT AND NO REFERENCE TO ANOTHER SERVICE (C-A).
//      DataServices is the CALLER of this contract; it is never a code reference here. Security is
//      called by its peers and calls none of them, so this file has no outbound edge.
//    * NO DATABASE CONTEXT, NO CONNECTION STRING AND NO STORAGE PROVIDER (C-E). The key store is
//      CONFIGURATION, not storage.
//    * NO ROUTE, HANDLER, MEMBER, TAG OR COMMENT FOR A DEFERRED CAPABILITY (C-D), and no
//      NotImplementedException anywhere.
//    * NO SECRET IN ANY FORM (C-F): no key, no armoured key block, no base64 or hexadecimal key run,
//      no password, no certificate, no token and NO INITIALIZATION VECTOR literal appears in code, in
//      a member default, in XML documentation or in a published description. There is no specimen
//      value anywhere below - not even an illustrative one - and nothing from the read-only tree's
//      eight in-source credential sites is reproduced in any form. That posture is
//      never-replicate-document-and-rotate, and it is why those sites are cited by locator only in
//      docs/SECRETS.md and never here.
//    * NO ANONYMOUS OPERATION (C-G). The group calls RequireAuthorization EXPLICITLY rather than
//      leaning on the host's default-deny fallback, so the requirement is legible at the declaration
//      and survives a change to that policy. AllowAnonymous appears nowhere in this file and must
//      never be added: this service's only anonymous surface is the readiness probe and the two
//      well-known metadata documents, and none of them is here.
//    * NO CAPABILITY OR VERSION ROUTE. Copyright() at n_crypto.sru:L9 and GetVersion() at :L10 are
//      the two deliberate NON-PORTS of the 65 declarations - a build's copyright banner and a native
//      library's version string are not cryptographic operations, and publishing either would put a
//      component-inventory surface on a security service.
//
//  TESTABILITY, WHICH IS A REQUIREMENT AND NOT A PREFERENCE (constraint C-H)
//  ------------------------------------------------------------------------------------------------
//  EVERY handler is a NAMED INTERNAL METHOD rather than a logic-bearing inline lambda, and so is
//  every helper, so the sibling test project can call each one DIRECTLY with provider doubles as well
//  as drive it through the booted host with WebApplicationFactory<Program>. The project file grants
//  InternalsVisibleTo to PowerFramework.Security.Tests for exactly this. This surface is the natural
//  table-driven theory matrix in the whole service - selector by payload form by reference presence -
//  and it is how the per-service line-coverage gate is reached.
// ==================================================================================================

using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using PowerFramework.Security.Authorization;
using PowerFramework.Security.Configuration;
using PowerFramework.Security.Crypto;
using PowerFramework.Shared.Kernel;

namespace PowerFramework.Security.Endpoints;

/// <summary>
/// Declares and serves contract C-02, <c>security.v1.CryptoService</c>: the 18 authenticated
/// operations that publish the legacy cryptographic surface - seventeen covering its overloads and one
/// authored release for the private keys this service retains on their behalf.
/// </summary>
/// <remarks>
/// <para>
/// This file authorises its routes; it does not authenticate them. Registering the authentication
/// scheme, the authorization services, the seven providers, the entropy seam and the options binding
/// is <c>Program.cs</c>'s responsibility and deliberately not this file's.
/// </para>
/// <para>
/// It performs no cryptography. Validation, reference resolution, dispatch and projection happen
/// here; every algorithm lives in <c>Crypto/</c>.
/// </para>
/// </remarks>
public static class CryptoEndpoints
{
    // ==============================================================================================
    //  ROUTES, NAMES AND TAG - taken verbatim from the authored contract document
    // ==============================================================================================

    /// <summary>The common prefix every operation of this contract sits under.</summary>
    /// <remarks>
    /// Declared once and applied through a single route group, so that the authorization requirement
    /// and the published tag are stated once and CANNOT be forgotten on a route added later.
    /// </remarks>
    private const string RouteGroupPrefix = "/v1/crypto";

    /// <summary>The scope a caller must hold to reach any operation in this contract.</summary>
    /// <remarks>
    /// <para>
    /// EVIDENCED BY THE ONLY CALLER IN THE REPOSITORY, NOT INVENTED HERE. DataServices requests exactly
    /// this scope when it obtains a token addressed to this service
    /// [services/dataservices-service/PowerFramework.DataServices/Clients/SecurityClient.cs:L2234, its
    /// <c>CryptoScope</c> constant],
    /// and that request is the whole of the evidence for what this surface's scope is called. A name
    /// chosen here instead would have refused the one caller the system has.
    /// </para>
    /// <para>
    /// ONE SCOPE FOR ALL 18 OPERATIONS rather than one per operation. The published contract groups them
    /// under a single tag and a single security requirement, the legacy surface they reproduce is one
    /// object [ws_objects/pfw.crypto.pbl.src/n_crypto.sru], and no caller in this system needs a subset -
    /// so a finer vocabulary would be scope names nothing requests, which is a permission model that
    /// cannot be verified against anything.
    /// </para>
    /// </remarks>
    internal const string RequiredScope = "security.crypto";

    /// <summary>The authorization-policy name this contract's operations are gated by.</summary>
    /// <remarks>
    /// DECLARED HERE AND CONSUMED BY THE COMPOSITION ROOT, which is the inverse of the usual direction
    /// and is deliberate. Program.cs builds the policy by reading this constant and
    /// <see cref="RequiredScope"/>, so the route and its requirement have ONE spelling. A policy name
    /// spelled independently in two files is the defect that matters most here: a route requiring a
    /// policy nobody registered fails closed and loudly, but a policy registered under a name no route
    /// requires silently enforces nothing at all.
    /// </remarks>
    internal static string ScopePolicyName => SecurityScopes.PolicyNameFor(RequiredScope);

    /// <summary>The tag the authored contract groups all 18 operations under.</summary>
    /// <remarks>
    /// Applied through the same single route group as the prefix and the authorization requirement, so an
    /// operation added later cannot be published untagged.
    /// </remarks>
    private const string TagName = "CryptoService";

    /// <summary>The security-scheme key the generated document declares the bearer requirement under.</summary>
    /// <remarks>
    /// The same spelling the sibling <c>PingEndpoints</c> uses, so the two land on ONE scheme in the
    /// generated document rather than on two that describe the same thing.
    /// </remarks>
    private const string BearerSchemeName = "bearerAuth";

    /// <summary>Route of the unkeyed in-memory digest operation, relative to the group prefix.</summary>
    private const string HashRoute = "/hash";

    /// <summary>Route of the keyed in-memory digest operation.</summary>
    private const string HmacRoute = "/hmac";

    /// <summary>Route of the unkeyed file digest operation.</summary>
    private const string HashFileRoute = "/hash-file";

    /// <summary>Route of the keyed file digest operation.</summary>
    private const string HmacFileRoute = "/hmac-file";

    /// <summary>Route of the symmetric encryption operation.</summary>
    private const string SymmetricEncryptRoute = "/symmetric/encrypt";

    /// <summary>Route of the symmetric decryption operation.</summary>
    private const string SymmetricDecryptRoute = "/symmetric/decrypt";

    /// <summary>Route of the RSA encryption operation.</summary>
    private const string RsaEncryptRoute = "/rsa/encrypt";

    /// <summary>Route of the RSA decryption operation.</summary>
    private const string RsaDecryptRoute = "/rsa/decrypt";

    /// <summary>Route of the RSA signature operation.</summary>
    private const string RsaSignRoute = "/rsa/sign";

    /// <summary>Route of the RSA signature verification operation.</summary>
    private const string RsaVerifyRoute = "/rsa/verify";

    /// <summary>Route of the RSA key-pair generation operation.</summary>
    private const string RsaKeysRoute = "/rsa/keys";

    /// <summary>
    /// The release route. The reference is a path segment because it NAMES the resource being deleted.
    /// </summary>
    /// <remarks>
    /// A reference is drawn from a constrained character set - the same set the configured references are
    /// validated against - so it is safe in a path segment, and no reference is ever logged, so it does
    /// not reach a diagnostic through the request line either. Naming it in a body instead would make the
    /// operation a POST that deletes, which is not what it is.
    /// </remarks>
    private const string RsaKeyReleaseRoute = "/rsa/keys/{keyRef}";

    /// <summary>Route of the random-bytes operation.</summary>
    private const string RandomBlobRoute = "/random/blob";

    /// <summary>Route of the random-string operation.</summary>
    private const string RandomStringRoute = "/random/string";

    /// <summary>Route of the identifier-generation operation.</summary>
    private const string RandomGuidRoute = "/random/guid";

    /// <summary>Route of the decode operation.</summary>
    private const string StringToBlobRoute = "/encoding/string-to-blob";

    /// <summary>Route of the encode operation.</summary>
    private const string BlobToStringRoute = "/encoding/blob-to-string";

    /// <summary>Route of the byte-reversal operation.</summary>
    private const string BlobReverseRoute = "/encoding/blob-reverse";

    /// <summary>Operation identifier of the unkeyed in-memory digest operation.</summary>
    /// <remarks>
    /// Every identifier below is the authored contract's own <c>operationId</c>, character for
    /// character. The document generator publishes the endpoint NAME as the operation identifier, so
    /// these values are what a generated client will be named after and they are not free to change.
    /// </remarks>
    private const string HashOperation = "hash";

    /// <summary>Operation identifier of the keyed in-memory digest operation.</summary>
    private const string HmacOperation = "hmac";

    /// <summary>Operation identifier of the unkeyed file digest operation.</summary>
    private const string HashFileOperation = "hashFile";

    /// <summary>Operation identifier of the keyed file digest operation.</summary>
    private const string HmacFileOperation = "hmacFile";

    /// <summary>Operation identifier of the symmetric encryption operation.</summary>
    private const string SymmetricEncryptOperation = "symmetricEncrypt";

    /// <summary>Operation identifier of the symmetric decryption operation.</summary>
    private const string SymmetricDecryptOperation = "symmetricDecrypt";

    /// <summary>Operation identifier of the RSA encryption operation.</summary>
    private const string RsaEncryptOperation = "rsaEncrypt";

    /// <summary>Operation identifier of the RSA decryption operation.</summary>
    private const string RsaDecryptOperation = "rsaDecrypt";

    /// <summary>Operation identifier of the RSA signature operation.</summary>
    private const string RsaSignOperation = "rsaSign";

    /// <summary>Operation identifier of the RSA verification operation.</summary>
    private const string RsaVerifyOperation = "rsaVerify";

    /// <summary>Operation identifier of the RSA key-pair generation operation.</summary>
    private const string GenerateRsaKeyOperation = "generateRsaKey";

    /// <summary>The operation identifier of the release operation.</summary>
    private const string ReleaseRsaKeyOperation = "releaseRsaKey";

    /// <summary>
    /// The subject claim name, which is the caller identity a retained key is charged to.
    /// </summary>
    /// <remarks>
    /// The PROTOCOL spelling rather than the framework's mapped alias, because this service configures
    /// <c>MapInboundClaims</c> false - so a claim arrives spelled exactly as the token spells it. It is
    /// the same name <c>Tokens/TokenIssuer.cs</c> stamps, and it is a protocol identifier rather than a
    /// value, which is why it is a literal at all.
    /// </remarks>
    private const string SubjectClaimName = "sub";

    /// <summary>Operation identifier of the random-bytes operation.</summary>
    private const string GenerateRandomBlobOperation = "generateRandomBlob";

    /// <summary>Operation identifier of the random-string operation.</summary>
    private const string GenerateRandomStringOperation = "generateRandomString";

    /// <summary>Operation identifier of the identifier-generation operation.</summary>
    private const string GenerateGuidOperation = "generateGuid";

    /// <summary>Operation identifier of the decode operation.</summary>
    private const string StringToBlobOperation = "stringToBlob";

    /// <summary>Operation identifier of the encode operation.</summary>
    private const string BlobToStringOperation = "blobToString";

    /// <summary>Operation identifier of the byte-reversal operation.</summary>
    private const string ReverseBlobOperation = "reverseBlob";

    // ==============================================================================================
    //  THE SIX WEAK-DEFAULT ANNOTATIONS
    //  ----------------------------------------------------------------------------------------------
    //  One constant each, so that the published prose is identical wherever it appears, is reviewable
    //  in one place against the authored contract, and can be asserted FRAGMENT BY FRAGMENT against
    //  the generated document. Each names the oracle line that fixes the default, and each says
    //  plainly that the behaviour is preserved rather than corrected.
    // ==============================================================================================

    /// <summary>Published annotation for the electronic-codebook default.</summary>
    private const string EcbDefaultAnnotation =
        "KNOWN LEGACY WEAKNESS - omitting mode runs in ECB. enums.sru:L946 declares "
        + "CRYPTO_SYMCRYPT_MODE_DEFAULT as CRYPTO_SYMCRYPT_MODE_ECB, so the sixteen mode-omitting "
        + "legacy overloads run in electronic codebook, which encrypts identical plaintext blocks to "
        + "identical ciphertext blocks and therefore leaks the structure of the plaintext. It is "
        + "preserved as the default and is NOT silently strengthened to CBC; a caller who needs "
        + "otherwise passes mode explicitly.";

    /// <summary>Published annotation for the RSA padding default and the refusal of no-padding.</summary>
    private const string Pkcs1DefaultAnnotation =
        "KNOWN LEGACY WEAKNESS - omitting padding selects PKCS#1 v1.5. enums.sru:L951 declares "
        + "CRYPTO_RSA_PADDING_DEFAULT as CRYPTO_RSA_PADDING_PKCS1, and PKCS#1 v1.5 encryption padding "
        + "is vulnerable to padding-oracle attack in the general case. It is preserved as the default "
        + "and is NOT quietly upgraded to OAEP, which remains selectable. NO-PADDING IS NOT "
        + "SELECTABLE: enums.sru:L949-L950 declares exactly two padding constants and no third, so a "
        + "request for no padding is refused with 400 - the legacy rejects it and so does this "
        + "contract.";

    /// <summary>Published annotation for the fixed, unselectable block padding.</summary>
    private const string FixedBlockPaddingAnnotation =
        "KNOWN LEGACY WEAKNESS - block padding is fixed and not selectable. Not one of the 32 "
        + "symmetric declarations at n_crypto.sru:L30-L61 has a padding parameter, and "
        + "enums.sru:L936-L946 declares a cipher type and a mode and nothing else, so the scheme is "
        + "PKCS#5-family and fixed. This operation therefore carries NO padding member: offering one "
        + "would imply a choice the legacy never had.";

    /// <summary>Published annotation for the total absence of key derivation.</summary>
    private const string NoKeyDerivationAnnotation =
        "KNOWN LEGACY WEAKNESS - no key-derivation function is reachable at all. No salt, iteration "
        + "count or derivation parameter appears in any of the 65 declarations at "
        + "n_crypto.sru:L9-L73, and enums.sru declares no derivation constant of any kind, so there "
        + "is no PBKDF2, scrypt, bcrypt or Argon2 and no salt concept anywhere on this surface. "
        + "Whatever the configured store holds IS the key, and a passphrase is therefore used as RAW "
        + "KEY BYTES with no stretching and no salting.";

    /// <summary>Published annotation for the absence of authenticated encryption.</summary>
    private const string NoAuthenticatedEncryptionAnnotation =
        "KNOWN LEGACY WEAKNESS - no authenticated encryption, so ciphertext carries no integrity tag. "
        + "The mode set is exactly ECB, CBC and CFB [enums.sru:L943-L945]: there is no GCM, no CCM "
        + "and no Poly1305, and no tag or associated-data parameter appears anywhere in the legacy "
        + "surface. This contract offers no member in which a tag could travel, so a caller needing "
        + "ciphertext integrity must obtain it separately - for instance from the keyed-digest "
        + "operation.";

    /// <summary>Published annotation for the legality of a 1024-bit modulus.</summary>
    private const string SmallRsaKeyAnnotation =
        "KNOWN LEGACY WEAKNESS - 1024-bit RSA remains a legal key size. CRYPTO_RSA_BITS_1024 is a "
        + "declared first-class value alongside 2048 and 4096 [enums.sru:L965-L967] and the legacy's "
        + "own demonstration generates a 1024-bit pair. A 1024-bit modulus is below every current "
        + "recommendation; 2048 bits or more is advisable. That advice is recorded and NOT enforced: "
        + "no minimum-size guard exists anywhere in the legacy, and imposing one would reject input "
        + "the legacy accepted.";

    // ==============================================================================================
    //  THE SHARED CONTRACT ANNOTATIONS
    //  Not weak defaults, but rules a caller must know and that must read identically everywhere.
    // ==============================================================================================

    /// <summary>Published annotation for the opaque key-reference rule.</summary>
    private const string KeyReferenceAnnotation =
        "RAW KEY MATERIAL NEVER CROSSES THE WIRE FROM A CALLER. keyRef is an opaque handle to material "
        + "held in this service's own configured store; it carries no key material, is meaningless "
        + "outside this service, and must not be parsed, constructed or inferred. It stands where the "
        + "legacy took an ordinary key in-parameter, which on a wire would place key material into "
        + "request bodies, into any log that recorded a request and into every characterization "
        + "recording of one. A reference outside the permitted set is refused with 403 and a permitted "
        + "reference that resolves to nothing with 404; neither response echoes any part of the stored "
        + "material, enumerates the store or suggests a nearby reference.";

    /// <summary>Published annotation for the opaque file-reference rule.</summary>
    private const string FileReferenceAnnotation =
        "NO CALLER-SUPPLIED FILESYSTEM PATH EVER CROSSES THIS BOUNDARY. fileRef is an opaque handle to "
        + "a file this service has been configured to expose, resolved exactly as keyRef is. It is not "
        + "a path, not a filename, not a directory and not a URL. The legacy hashed any file its "
        + "process could open [n_crypto.sru:L27-L29], which in process was unremarkable and on a wire "
        + "would be an arbitrary-file-read primitive; an opaque reference removes the problem rather "
        + "than defending against it, because THERE IS NO PATH TO TRAVERSE. What is narrowed is the "
        + "reach, and no response echoes the resolved path, the file's name, its size, its location or "
        + "any part of its contents.";

    /// <summary>Published annotation for the two determinism seams a parity author must mask.</summary>
    private const string DeterminismSeamAnnotation =
        "DETERMINISM SEAM. The random-bytes, random-string and identifier generators are the primary "
        + "non-determinism sources in the in-scope estate [n_crypto.sru:L14-L18], so the generator "
        + "behind them is injected server-side: a parity test substitutes a deterministic double while "
        + "production uses the platform generator. A consumer writing a characterization comparison "
        + "must MASK this operation's returned value on both the master and the candidate side.";

    /// <summary>Published annotation for the preserved hash-type set.</summary>
    private const string HashTypeSetAnnotation =
        "KNOWN LEGACY WEAKNESS - the published hash set includes MD5 and SHA-1, and both remain "
        + "selectable [enums.sru:L928-L933]. Neither is removed and neither is renumbered: preserving "
        + "the set exactly is the requirement and annotating it is the remediation. The oracle's own "
        + "comment at enums.sru:L927 records that this one set parameterises the digest, the signature "
        + "and the verification members alike.";

    /// <summary>Published annotation for the exclusion of the keyed and signature checksum arm.</summary>
    private const string KeyedChecksumExclusionAnnotation =
        "CAPABILITY NARROWING - CRYPTO_HASH_CRC32 (5) is refused by this operation with 400 carrying "
        + "E_NO_SUPPORT, while remaining a published hash type that the unkeyed digest operations "
        + "accept. The reason is not a judgement about its strength: a keyed construction over a "
        + "linear checksum is forgeable from the algebra, and there is no RSA-over-CRC32 signature "
        + "structure to name, so no such construction exists to implement. Making this cell work would "
        + "mean inventing a scheme and claiming it as parity, which nothing in this repository could "
        + "confirm - the legacy class is native with no readable body. A contract is narrowed with a "
        + "defined error, never widened with a guess.";

    /// <summary>Published annotation for the result-form rule of the digest family.</summary>
    private const string DigestFormAnnotation =
        "The response form does NOT follow payloadForm here, and that asymmetry is legacy behaviour: "
        + "all six legacy hash overloads return a string [n_crypto.sru:L21-L26], so a blob payload "
        + "still yields a string digest. Everywhere else in this contract the result form follows the "
        + "payload form. The exception is preserved rather than harmonised.";

    /// <summary>Published annotation for the result-form rule of the value-returning families.</summary>
    private const string PayloadFormAnnotation =
        "The result form FOLLOWS the request's payloadForm, exactly as the legacy return type follows "
        + "the legacy payload type. STRING selects the string-shaped legacy overload family and "
        + "carries the legacy string verbatim; BLOB selects the blob-shaped family and carries base64 "
        + "of the raw bytes, because JSON has no binary type. That transport base64 is NOT the "
        + "encoding argument of the two conversion operations, which is a genuine legacy argument "
        + "chosen per call.";

    // ==============================================================================================
    //  PUBLISHED SUMMARIES AND DESCRIPTIONS
    //  Composed from the annotation constants above so that a weakness is worded identically on every
    //  operation that carries it, and so that the whole published prose of this contract is
    //  reviewable in one region of one file against the authored document.
    // ==============================================================================================

    /// <summary>Published summary of the unkeyed in-memory digest operation.</summary>
    private const string HashSummary = "Compute an unkeyed digest.";

    /// <summary>Published description of the unkeyed in-memory digest operation.</summary>
    private const string HashDescription =
        "Computes an unkeyed digest over the supplied payload, mirroring the two unkeyed Hash "
        + "overloads at n_crypto.sru:L21-L22; payloadForm selects between them. There is no key member "
        + "here, because those two overloads take none. "
        + DigestFormAnnotation
        + " "
        + HashTypeSetAnnotation;

    /// <summary>Published summary of the keyed in-memory digest operation.</summary>
    private const string HmacSummary = "Compute a keyed digest.";

    /// <summary>Published description of the keyed in-memory digest operation.</summary>
    private const string HmacDescription =
        "Computes a keyed digest over the supplied payload. The legacy declares this as the full "
        + "two-by-two cross-product of payload type and key type at n_crypto.sru:L23-L26, all four "
        + "returning a string; the key half of that cross-product is collapsed here because a key has "
        + "no form on the wire, while the payload half remains fully expressible through payloadForm. "
        + KeyReferenceAnnotation
        + " "
        + DigestFormAnnotation
        + " "
        + KeyedChecksumExclusionAnnotation
        + " "
        + NoKeyDerivationAnnotation;

    /// <summary>Published summary of the unkeyed file digest operation.</summary>
    private const string HashFileSummary = "Compute an unkeyed digest over a server-resolved file.";

    /// <summary>Published description of the unkeyed file digest operation.</summary>
    private const string HashFileDescription =
        "Computes an unkeyed digest over a file this service has been configured to expose, mirroring "
        + "the single unkeyed HashFile overload at n_crypto.sru:L27. There is no payloadForm, because "
        + "the payload is a file rather than an inline value and the legacy overload returns a string "
        + "regardless. The full published hash set applies, CRYPTO_HASH_CRC32 included: an unkeyed "
        + "checksum over a file is a legitimate thing to compute and the oracle's own demonstration "
        + "does exactly that. "
        + FileReferenceAnnotation
        + " "
        + HashTypeSetAnnotation;

    /// <summary>Published summary of the keyed file digest operation.</summary>
    private const string HmacFileSummary = "Compute a keyed digest over a server-resolved file.";

    /// <summary>Published description of the keyed file digest operation.</summary>
    private const string HmacFileDescription =
        "Computes a keyed digest over a file this service has been configured to expose. The legacy "
        + "declares the keyed file family twice at n_crypto.sru:L28-L29, differing only in whether the "
        + "key is a string or a blob - a distinction the reference indirection collapses entirely, so "
        + "both overloads are reachable here without loss. Both parameters are references and neither "
        + "is a value. "
        + FileReferenceAnnotation
        + " "
        + KeyReferenceAnnotation
        + " "
        + KeyedChecksumExclusionAnnotation
        + " "
        + NoKeyDerivationAnnotation;

    /// <summary>Published summary of the symmetric encryption operation.</summary>
    private const string SymmetricEncryptSummary =
        "Encrypt a payload with a symmetric cipher. Omitting the mode selects ECB.";

    /// <summary>Published description of the symmetric encryption operation.</summary>
    private const string SymmetricEncryptDescription =
        "Encrypts the supplied payload with one of the five legacy symmetric ciphers, covering all 16 "
        + "SymEncrypt overloads at n_crypto.sru:L30-L45 - the cross-product of payload string or blob, "
        + "key string or blob, initialization vector present or absent, and explicit mode present or "
        + "absent. Omitting ivRef selects the no-vector overload family, which is the correct and only "
        + "sensible choice under the default mode since ECB consumes no vector at all. In the legacy "
        + "the key and vector types are correlated - a string key pairs only with a string vector and "
        + "a blob key only with a blob vector, with no mixed overload - and because neither travels as "
        + "a value here, no mixed request is expressible and none has to be validated for. "
        + EcbDefaultAnnotation
        + " "
        + NoAuthenticatedEncryptionAnnotation
        + " "
        + FixedBlockPaddingAnnotation
        + " "
        + KeyReferenceAnnotation
        + " "
        + NoKeyDerivationAnnotation
        + " "
        + PayloadFormAnnotation;

    /// <summary>Published summary of the symmetric decryption operation.</summary>
    private const string SymmetricDecryptSummary =
        "Decrypt a payload with a symmetric cipher. Omitting the mode selects ECB.";

    /// <summary>Published description of the symmetric decryption operation.</summary>
    private const string SymmetricDecryptDescription =
        "Decrypts the supplied payload with one of the five legacy symmetric ciphers, covering all 16 "
        + "SymDecrypt overloads at n_crypto.sru:L46-L61 - the same cross-product as the encrypt "
        + "direction, over ciphertext rather than plaintext. One consequence of the missing integrity "
        + "tag is explicit, because it changes how a caller must treat a 200: a decryption that used "
        + "the wrong key, the wrong mode or the wrong vector can still return 200 with a well-formed "
        + "but meaningless payload, and this contract cannot tell the caller which happened. That is "
        + "the legacy's behaviour and it is preserved; what is added is that the contract says so. "
        + EcbDefaultAnnotation
        + " "
        + NoAuthenticatedEncryptionAnnotation
        + " "
        + FixedBlockPaddingAnnotation
        + " "
        + KeyReferenceAnnotation
        + " "
        + NoKeyDerivationAnnotation
        + " "
        + PayloadFormAnnotation;

    /// <summary>Published summary of the RSA encryption operation.</summary>
    private const string RsaEncryptSummary =
        "Encrypt a payload with an RSA public key. Omitting the padding selects PKCS#1 v1.5.";

    /// <summary>Published description of the RSA encryption operation.</summary>
    private const string RsaEncryptDescription =
        "Encrypts the supplied payload with an RSA public key, covering all four RSAEncrypt overloads "
        + "at n_crypto.sru:L62-L65 - the cross-product of a string or blob payload with an explicit "
        + "padding present or absent. The legacy typed the key parameter as a string in all four "
        + "overloads regardless of the payload form, so there was never a key-form cross-product to "
        + "collapse here - only a key value to remove from the wire. "
        + Pkcs1DefaultAnnotation
        + " "
        + KeyReferenceAnnotation
        + " "
        + NoKeyDerivationAnnotation
        + " "
        + PayloadFormAnnotation;

    /// <summary>Published summary of the RSA decryption operation.</summary>
    private const string RsaDecryptSummary =
        "Decrypt a payload with an RSA private key. Omitting the padding selects PKCS#1 v1.5.";

    /// <summary>Published description of the RSA decryption operation.</summary>
    private const string RsaDecryptDescription =
        "Decrypts the supplied payload with an RSA private key, covering all four RSADecrypt overloads "
        + "at n_crypto.sru:L66-L69. This is the clearest illustration of why the reference indirection "
        + "exists: the legacy signature takes an RSA PRIVATE key as an ordinary string in-parameter, "
        + "which in process is unremarkable and on a wire would place a private key into a request "
        + "body, into any log that recorded the request and into every characterization recording of "
        + "it. NO REQUEST ANYWHERE IN THIS CONTRACT CARRIES A PRIVATE KEY. "
        + Pkcs1DefaultAnnotation
        + " "
        + KeyReferenceAnnotation
        + " "
        + NoKeyDerivationAnnotation
        + " "
        + PayloadFormAnnotation;

    /// <summary>Published summary of the RSA signature operation.</summary>
    private const string RsaSignSummary =
        "Produce an RSA signature. One of the two primitives the token issuer is built on.";

    /// <summary>Published description of the RSA signature operation.</summary>
    private const string RsaSignDescription =
        "Produces an RSA signature over the supplied payload, covering both RSASign overloads at "
        + "n_crypto.sru:L70-L71. This operation is NOT how a service token is minted: the token "
        + "endpoint mints tokens and accepts no caller choice of key or hash, while this is the "
        + "general-purpose signature primitive from the legacy surface, exposed for callers that used "
        + "it directly. Signing under MD5 is legal here and is preserved: the oracle declared one hash "
        + "set for digests and signatures alike, and a signature produced under a broken hash has no "
        + "meaningful collision resistance without this contract either preventing it or pretending it "
        + "is safe. "
        + HashTypeSetAnnotation
        + " "
        + KeyedChecksumExclusionAnnotation
        + " "
        + KeyReferenceAnnotation
        + " "
        + PayloadFormAnnotation;

    /// <summary>Published summary of the RSA verification operation.</summary>
    private const string RsaVerifySummary =
        "Verify an RSA signature. The counterpart primitive of the token issuer.";

    /// <summary>Published description of the RSA verification operation.</summary>
    private const string RsaVerifyDescription =
        "Verifies an RSA signature over the supplied payload and returns a boolean verdict, covering "
        + "both VerifyRSASign overloads at n_crypto.sru:L72-L73. ONE payloadForm GOVERNS BOTH data AND "
        + "signature, because the legacy correlates them: a string payload is verified against a "
        + "string signature and a blob payload against a blob signature, and there is no mixed "
        + "overload, so no independent form selector for the signature is offered. A signature that "
        + "does not verify is a SUCCESSFUL EXECUTION WITH A NEGATIVE RESULT - 200 with valid false - "
        + "not an error status; conflating the two would make a legitimate negative verdict "
        + "indistinguishable from a transport or authorization failure. "
        + HashTypeSetAnnotation
        + " "
        + KeyedChecksumExclusionAnnotation
        + " "
        + KeyReferenceAnnotation;

    /// <summary>Published summary of the RSA key-pair generation operation.</summary>
    private const string GenerateRsaKeySummary =
        "Generate an RSA key pair. The private key is retained and never returned.";

    /// <summary>Published description of the RSA key-pair generation operation.</summary>
    private const string GenerateRsaKeyDescription =
        "Generates an RSA key pair of the requested bit length, covering both GenRSAKey overloads at "
        + "n_crypto.sru:L19-L20; omitting pemFormat reaches the three-argument overload, which does "
        + "not take the switch at all. A DELIBERATE NARROWING, NOT AN OMISSION: the legacy returns "
        + "BOTH keys through two ref out-parameters and reports only success or failure, and that "
        + "shape cannot be reproduced across a network boundary without shipping freshly generated "
        + "private key material across it. The response therefore carries the generated PUBLIC key - "
        + "public by definition, and required, because a caller that cannot obtain it cannot use the "
        + "pair it just asked for - together with an opaque keyRef under which this service has "
        + "retained the PRIVATE key. THE PRIVATE KEY IS NEVER RETURNED, never logged and never echoed "
        + "in an error body: a caller that needs a signature calls the signing operation with the "
        + "reference rather than obtaining the key with which to produce one, and that inversion is "
        + "the entire point. No private-key export path exists in this contract; were one ever "
        + "required it would have to be a separately named operation and never the default behaviour "
        + "of this one. "
        + SmallRsaKeyAnnotation
        + " "
        + NoKeyDerivationAnnotation;

    /// <summary>Published summary of the random-bytes operation.</summary>
    private const string GenerateRandomBlobSummary = "Generate random bytes.";

    /// <summary>Published description of the random-bytes operation.</summary>
    private const string GenerateRandomBlobDescription =
        "Generates the requested number of random bytes, mirroring the single overload at "
        + "n_crypto.sru:L14, whose return type is an unconditional blob - so there is no form to "
        + "select and none is offered. The size domain the legacy declares is that of a 32-bit "
        + "unsigned argument; this contract accepts up to 1048576 bytes, a published service-level cap "
        + "rather than a cryptographic one, because these operations are reachable over a network by "
        + "any authenticated caller and an uncapped allocation driven by a few dozen bytes of request "
        + "would let a handful of concurrent calls terminate the service that holds the system's only "
        + "signing key. A request above the cap is refused with 400 and is NEVER silently reduced to "
        + "the cap: returning less random material than was asked for is the one outcome a caller "
        + "cannot detect. "
        + DeterminismSeamAnnotation;

    /// <summary>Published summary of the random-string operation.</summary>
    private const string GenerateRandomStringSummary =
        "Generate a random string. Omitting the flags selects digits and letters.";

    /// <summary>Published description of the random-string operation.</summary>
    private const string GenerateRandomStringDescription =
        "Generates a random string of the requested length drawn from the selected character classes, "
        + "covering both legacy overloads: n_crypto.sru:L15 takes only the size and :L16 adds the "
        + "flags. Omitting flags selects CRYPTO_RNDSTRING_DEFAULT, which enums.sru:L957 declares as "
        + "CRYPTO_RNDSTRING_NUMBER plus CRYPTO_RNDSTRING_ALPHABET and which is therefore 3 - digits "
        + "and letters, with the symbol class DELIBERATELY EXCLUDED. That exclusion is the legacy's "
        + "own default and is preserved rather than widened. flags is a BITMASK and not an "
        + "enumeration: the three classes compose, so 7 selects all three, and bits above the three "
        + "named remain accepted inside the declared domain because the legacy defines no behaviour "
        + "for them and rejecting them would be an invented narrowing. The same published size cap as "
        + "the random-bytes operation applies. "
        + DeterminismSeamAnnotation;

    /// <summary>Published summary of the identifier-generation operation.</summary>
    private const string GenerateGuidSummary =
        "Generate a GUID. Omitting the flags selects both braces and separators.";

    /// <summary>Published description of the identifier-generation operation.</summary>
    private const string GenerateGuidDescription =
        "Generates a globally unique identifier in the requested textual form, covering both legacy "
        + "overloads: n_crypto.sru:L17 takes no argument and :L18 adds the flags. The request body is "
        + "OPTIONAL - omitting it entirely, or sending an empty object, is equivalent to omitting "
        + "flags and is how the no-argument overload is reached. Omitting flags selects "
        + "CRYPTO_GUID_DEFAULT, which enums.sru:L962 declares as CRYPTO_GUID_INCLUDE_BRACKET plus "
        + "CRYPTO_GUID_INCLUDE_SEPARATOR and which is therefore 3, so the default form carries both "
        + "the surrounding braces and the group separators; 0 yields a bare value with neither. The "
        + "flags govern FORMATTING ONLY and have no bearing on the value generated. "
        + DeterminismSeamAnnotation;

    /// <summary>Published summary of the decode operation.</summary>
    private const string StringToBlobSummary = "Decode an encoded string into bytes, under Base64 or hex.";

    /// <summary>Published description of the decode operation.</summary>
    private const string StringToBlobDescription =
        "Decodes an encoded string into bytes, mirroring the single overload at n_crypto.sru:L11. "
        + "THREE DISTINCT ENCODINGS EXIST IN THIS CONTRACT AND NONE OF THEM IS THE OTHERS, and this is "
        + "the one place the confusion is genuinely easy to make. First, payloadForm is a wire-shape "
        + "selector that says which legacy overload family applies - this operation has none, because "
        + "the legacy declares exactly one overload taking a string and returning a blob. Second, the "
        + "JSON transport encoding of a blob is always base64, because JSON cannot carry bytes; that "
        + "is a property of JSON, invisible to the legacy. Third, the encoding argument of this "
        + "operation and of its inverse is a GENUINE LEGACY ARGUMENT chosen by the caller per call, "
        + "CRYPTO_ENCODING_BASE64 (0) or CRYPTO_ENCODING_HEX (1) [enums.sru:L924-L925], that says how "
        + "the input string is to be interpreted. In particular a request to decode a HEX string still "
        + "returns its bytes as a BASE64 JSON member, because the two answer different questions. "
        + "There is deliberately no default: both legacy declarations take the argument, so a caller "
        + "always states it.";

    /// <summary>Published summary of the encode operation.</summary>
    private const string BlobToStringSummary = "Encode bytes into a string, under Base64 or hex.";

    /// <summary>Published description of the encode operation.</summary>
    private const string BlobToStringDescription =
        "Encodes bytes into a string, mirroring the single overload at n_crypto.sru:L12 and the exact "
        + "inverse of the decode operation. The request's data member always carries the input bytes "
        + "as base64 because JSON has no binary type, while the encoding argument - the same genuine "
        + "legacy argument described on the decode operation - selects whether the RETURNED STRING is "
        + "Base64 or hex. A caller asking for hex therefore submits base64 and receives hex, which is "
        + "correct rather than contradictory. There is deliberately no default.";

    /// <summary>Published summary of the byte-reversal operation.</summary>
    private const string ReverseBlobSummary =
        "Reverse the byte order of a payload. The legacy mutates in place; the wire cannot.";

    /// <summary>Published description of the byte-reversal operation.</summary>
    private const string ReverseBlobDescription =
        "Reverses the byte order of the supplied bytes, mirroring the single overload at "
        + "n_crypto.sru:L13 - the only member of the legacy surface that mutates its argument and the "
        + "only one declared ref. AN IMPEDANCE MISMATCH, DOCUMENTED RATHER THAN QUIETLY RESOLVED: the "
        + "legacy parameter is a ref blob, so the operation mutates the caller's own variable and its "
        + "boolean return value reports only whether the mutation succeeded, with the reversed bytes "
        + "never returned at all. In-place mutation has no wire representation, because a request body "
        + "is not a variable a server can write back into. The response therefore carries BOTH halves "
        + "of the legacy outcome: data, the reversed bytes the legacy left in the caller's variable, "
        + "which is the addition the boundary forces; and succeeded, the legacy boolean return value "
        + "carried unchanged, preserved precisely because it IS the operation's actual return value "
        + "and dropping it in favour of an HTTP status would discard a signal a caller may be "
        + "branching on. succeeded may therefore be false in a 200 response.";

    // ==============================================================================================
    //  FAILURE DETAIL TEXT
    //  ----------------------------------------------------------------------------------------------
    //  EVERY ONE IS A FIXED SENTENCE THAT INTERPOLATES NO CALLER VALUE. Each states the accepted set
    //  or the required member, which a caller can act on, and says nothing about what was supplied,
    //  which the caller already has. That is not merely tidy: it is what makes it STRUCTURALLY
    //  IMPOSSIBLE for a payload, a ciphertext, a resolved key, a vector or a configured path to reach
    //  a problem body or a log record through this file, rather than something the call sites have to
    //  be trusted to observe.
    // ==============================================================================================

    /// <summary>Detail for a request that omitted a member the contract requires.</summary>
    private const string MissingMemberDetail =
        "A required member of the request is absent or empty. Every member the published schema lists "
        + "as required must be present; the offending member name is named in this detail and no value "
        + "is reported.";

    /// <summary>Detail for a payload whose declared form was not supplied.</summary>
    private const string MissingPayloadFormDetail =
        "payloadForm is required and must be either STRING or BLOB. It selects which of the two "
        + "parallel legacy overload families applies and therefore what form the result takes, so it "
        + "cannot be defaulted without silently choosing an overload family for the caller.";

    /// <summary>Detail for a payload that is not well-formed for its declared form.</summary>
    private const string MalformedBinaryPayloadDetail =
        "The payload is not well-formed for the declared payloadForm. With BLOB, data must carry "
        + "base64 of the raw bytes, because JSON has no binary type. The supplied value is "
        + "deliberately not reported.";

    /// <summary>Detail for an encoded string that does not match its declared encoding.</summary>
    private const string MalformedEncodedTextDetail =
        "The supplied string is not well-formed for the declared encoding. Base64 tolerates "
        + "surrounding and embedded white space; hexadecimal requires two digits per byte, accepts "
        + "upper, lower or mixed case, and permits no white space. The supplied value is deliberately "
        + "not reported.";

    /// <summary>Detail for a hash-type selector outside the published set.</summary>
    private const string UnsupportedHashTypeDetail =
        "Not a published hash type. The set is exactly MD5 (0), SHA-1 (1), SHA-256 (2), SHA-384 (3), "
        + "SHA-512 (4) and CRC32 (5) [enums.sru:L928-L933]. MD5 and SHA-1 remain selectable as "
        + "preserved legacy weaknesses.";

    /// <summary>Detail for the keyed and signature checksum arm.</summary>
    private const string KeyedChecksumUnsupportedDetail =
        "CRC32 (5) is a published hash type but has no keyed form and no signature construction. HMAC "
        + "is defined over an iterated cryptographic hash and CRC32 is a linear checksum, so a keyed "
        + "construction over it is forgeable from the algebra; and a checksum has no "
        + "digest-algorithm identifier for a signature structure to name. This cell is refused rather "
        + "than invented. Use MD5 (0), SHA-1 (1), SHA-256 (2), SHA-384 (3) or SHA-512 (4) here, or the "
        + "unkeyed digest surface for a checksum.";

    /// <summary>Detail for a cipher-type selector outside the published set.</summary>
    private const string UnsupportedCipherTypeDetail =
        "Not a published symmetric cipher type. The set is exactly DES (0), 3DES (1), AES128 (2), "
        + "AES192 (3) and AES256 (4) [enums.sru:L936-L940]. DES and 3DES are retained because the "
        + "legacy declares them; single DES in particular has a 56-bit effective key and is unfit for "
        + "new use, and neither is removed nor renumbered.";

    /// <summary>Detail for a cipher-mode selector outside the published set.</summary>
    private const string UnsupportedCipherModeDetail =
        "Not a published symmetric cipher mode. The set is exactly ECB (0), CBC (1) and CFB (2) "
        + "[enums.sru:L943-L945]. There is no authenticated mode in the legacy set and none may be "
        + "added. Omitting the member selects the legacy default, which is ECB.";

    /// <summary>Detail for an RSA padding selector outside the published set.</summary>
    private const string UnsupportedRsaPaddingDetail =
        "Not a published RSA padding mode. The set is exactly PKCS#1 v1.5 (0) and OAEP (1) "
        + "[enums.sru:L949-L950]. NO-PADDING IS NOT A PUBLISHED VALUE AND IS REFUSED, which is "
        + "preserved legacy behaviour rather than a hardening choice: the legacy declares no constant "
        + "for it, so there is nothing to select. Omitting the member selects the legacy default, "
        + "which is PKCS#1 v1.5.";

    /// <summary>Detail for an encoding selector outside the published set.</summary>
    private const string UnsupportedEncodingDetail =
        "Not a published encoding. The set is exactly Base64 (0) and hexadecimal (1) "
        + "[enums.sru:L924-L925]. This argument is the genuine legacy encoding argument and not the "
        + "JSON transport encoding of a blob member, which is always base64.";

    /// <summary>Detail for a requested length outside the published domain.</summary>
    private const string SizeOutOfDomainDetail =
        "The requested size is outside the accepted domain. This contract accepts 0 to 1048576 "
        + "inclusive - a published service-level cap rather than a cryptographic one - and refuses "
        + "anything above it rather than truncating, because short random material is the one defect a "
        + "caller cannot detect.";

    /// <summary>Detail for a flag value outside the published domain.</summary>
    private const string FlagsOutOfDomainDetail =
        "The supplied flags value is outside the accepted domain. The legacy argument is a 32-bit "
        + "unsigned integer, so the domain is 0 to 4294967295 inclusive. Bits above those the "
        + "catalogue names remain accepted inside that domain, because the legacy defines no behaviour "
        + "for them and rejecting them would narrow a contract the legacy leaves open.";

    /// <summary>Detail for a key size outside the published domain.</summary>
    private const string KeySizeOutOfDomainDetail =
        "The requested modulus length is outside the accepted domain. The legacy argument is a 16-bit "
        + "unsigned integer, so the domain is 0 to 65535 inclusive. That is a faithful statement of "
        + "the declared parameter type and NOT a cryptographic minimum or maximum: 1024 remains a "
        + "legal value and no floor is imposed anywhere.";

    /// <summary>
    /// The largest modulus length this boundary will spend work generating.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A WORK BOUND CREATED BY THE DECOMPOSITION, NOT A CRYPTOGRAPHIC POLICY (constraint C-B). The legacy
    /// generator was called in-process by the application that owned it; nothing could ask it for a key on
    /// a stranger's behalf, so its only bound was the declared argument's 16-bit domain and that was
    /// sufficient. Across a network boundary that domain is not a bound at all: the platform will attempt
    /// any size it considers legal, and generating a 16384-bit modulus occupies a core for minutes. A
    /// handful of concurrent requests at the top of the legal range is a denial of service against the
    /// service that holds the system's only signing key, delivered entirely through legitimate calls.
    /// </para>
    /// <para>
    /// 🔴 <b>IT IS THE LARGEST PUBLISHED SIZE, WHICH IS WHY IT NARROWS NOTHING A CALLER CAN USE.</b> The
    /// legacy declares exactly three convenience sizes - 1024, 2048 and 4096 [enums.sru:L965-L967] - and
    /// this is the third of them, so every size the legacy names as first-class is still accepted,
    /// INCLUDING the weak one. What is refused is only the range above the published vocabulary, which no
    /// legacy caller could have named from a declared constant. The 1024-bit weakness stays annotated and
    /// unenforced, exactly as the plan requires [AAP 0.6.6.4]: this is a ceiling, and there is no floor.
    /// </para>
    /// <para>
    /// THE BOUND SITS AT THE BOUNDARY AND NOT IN THE PROVIDER. Crypto/RsaProvider.cs is a parity surface
    /// and still answers the platform's own legal-size question for the whole 16-bit domain, so the
    /// provider-level parity rows that assert what the legacy returns for an extreme size are untouched.
    /// </para>
    /// </remarks>
    internal const long MaximumGeneratedModulusBits = 4_096;

    /// <summary>
    /// The largest file this boundary will read in order to digest it.
    /// </summary>
    /// <remarks>
    /// The file is chosen by the DEPLOYMENT - a caller names an opaque reference and this service resolves
    /// it - so the bound is not there to stop a caller naming an arbitrary path. It is there because the
    /// caller chooses HOW OFTEN, and an unbounded read repeated under an authenticated credential is an I/O
    /// amplifier whose multiplier is the file's size. A deployment that needs to expose something larger
    /// through this surface should reconsider the surface rather than the bound.
    /// </remarks>
    internal const long MaximumDigestedFileBytes = 67_108_864;

    /// <summary>Detail for a modulus length above the published work bound.</summary>
    /// <remarks>
    /// It states the bound. A published ceiling is not a fact about this deployment's state or its other
    /// callers, so quoting it tells a caller how to correct the request and discloses nothing - unlike the
    /// store-full refusal below, which deliberately says nothing about how much of the store is held.
    /// </remarks>
    private static readonly string KeySizeOverBudgetDetail =
        $"The requested modulus length is above the {MaximumGeneratedModulusBits} bits this boundary will "
            + "spend work generating. That is a work bound rather than a cryptographic policy: generating a "
            + "modulus far above the published sizes occupies a core for minutes, and this service holds "
            + "the system's only signing key. Every size the legacy publishes as first-class - 1024, 2048 "
            + "and 4096 - is accepted, and no minimum is imposed. No key was generated.";

    /// <summary>Detail for a configured file larger than the published work bound.</summary>
    private static readonly string FileTooLargeDetail =
        $"The file this reference names is larger than the {MaximumDigestedFileBytes} bytes this boundary "
            + "will read in order to digest it. That is a work bound on a repeatable read rather than a "
            + "statement about the request. No path, filename, location or content is reported.";

    /// <summary>Detail for a key size the platform will not generate.</summary>
    private const string KeySizeUnavailableDetail =
        "The platform will not generate a key pair of the requested modulus length. The legal sizes "
        + "are a property of the platform's cryptographic provider rather than of this contract, which "
        + "is why the platform is asked rather than a table consulted, and why a refusal here reflects "
        + "a real inability rather than a rule invented by this service. No minimum size is enforced "
        + "and 1024 is not excluded.";

    /// <summary>Detail for a reference the caller may not use.</summary>
    /// <remarks>
    /// ONE FIXED SENTENCE FOR EVERY NON-PERMITTED REFERENCE, whatever the reference names and whether
    /// or not anything is configured behind it. It does not say whether a value exists, does not
    /// enumerate the store, and does not suggest a nearby reference, so the response distinguishes
    /// authorization from configuration - which the authored contract requires - without becoming an
    /// oracle for the CONTENTS of the store.
    /// </remarks>
    internal const string ReferenceForbiddenDetail =
        "The reference is not one this service permits a caller to name. The permitted set is "
        + "configured for this deployment; this response names no part of it, enumerates nothing and "
        + "suggests no alternative.";

    /// <summary>Detail for a request that omitted an opaque reference the contract requires.</summary>
    /// <remarks>
    /// It names no member, deliberately, because the same resolution path serves the key, the vector
    /// and the file reference and a shared sentence cannot claim to know which one was absent. The
    /// published schema marks each of them required where it applies, so a client validating against
    /// the document refuses the request before sending it.
    /// </remarks>
    internal const string MissingReferenceDetail =
        "A required opaque reference is absent or empty. A reference carries no material of any kind - "
        + "it is a short identifier this service resolves against its own configured store - so there "
        + "is nothing a caller can substitute for one.";

    /// <summary>Detail for a permitted reference that resolves to nothing.</summary>
    internal const string ReferenceNotFoundDetail =
        "No material is configured for this reference in this service's store. The reference is "
        + "permitted, so this is a configuration gap rather than an authorization refusal - the two "
        + "are deliberately distinguishable. This response echoes no part of any stored material and "
        + "enumerates nothing.";

    /// <summary>Detail for a reference whose length exceeds the configured maximum.</summary>
    internal const string ReferenceTooLongDetail =
        "The reference is longer than any this service accepts. A reference is a short opaque "
        + "identifier, and the maximum length is the same one the configured set is validated against "
        + "at startup.";

    /// <summary>Detail for resolved material that is not usable as an asymmetric key.</summary>
    /// <remarks>
    /// A SERVER FAULT AND WORDED AS ONE. The caller named a permitted reference and this service found
    /// material behind it that it cannot import, which is a deployment defect the caller can neither
    /// see nor fix. The sentence reports nothing about the material - not its value, not a substring,
    /// not its length - because a problem body and its log record are exactly the wrong place to
    /// describe key material.
    /// </remarks>
    private const string UnusableKeyMaterialDetail =
        "The material configured for this reference could not be used as an RSA key. This is a "
        + "configuration fault in this service rather than a defect in the request. Nothing about the "
        + "configured material is reported.";

    /// <summary>
    /// Detail for resolved material that is a usable RSA key of the WRONG KIND for the operation.
    /// </summary>
    /// <remarks>
    /// A DISTINCT SENTENCE FROM THE UNUSABLE ONE, DELIBERATELY, because the two name different
    /// deployment defects and an operator fixing either needs to know which it has: this one says the
    /// configured value IS a key and is the wrong half of one, which is fixed by configuring the private
    /// half rather than by re-encoding what is already there. Like its sibling it is a server fault, and
    /// like its sibling it reports nothing about the material - a key kind is not a measurement of it.
    /// </remarks>
    private const string PublicOnlyKeyMaterialDetail =
        "The material configured for this reference is an RSA public key, and this operation requires "
        + "the private half. This is a configuration fault in this service rather than a defect in the "
        + "request. Nothing about the configured material is reported.";

    /// <summary>Detail for a cryptographic refusal on an encrypting or signing path.</summary>
    private const string CryptographicRefusalDetail =
        "The platform refused the operation with the configured key material. This is a property of "
        + "the material this deployment configured rather than of the request: this platform refuses "
        + "the known weak and semi-weak DES keys and refuses 3DES keys whose adjacent sub-keys "
        + "coincide, which shorter key material reaches once it is zero-padded to the cipher's length, "
        + "where an OpenSSL-based implementation would have encrypted. No workaround is applied and no "
        + "key is substituted - either would change behaviour - so the failure is loud rather than "
        + "silent. Nothing about the configured material is reported.";

    /// <summary>Detail for a payload the referenced key cannot process.</summary>
    private const string UnprocessablePayloadDetail =
        "The payload could not be processed with the referenced key. For an encrypting operation the "
        + "usual cause is a payload longer than the key's modulus permits for the selected padding; "
        + "for a decrypting one it is ciphertext that was not produced with this key, this mode or "
        + "this vector. There is no authenticated mode on this surface, so no integrity check can "
        + "distinguish those cases and none is claimed. The supplied value is deliberately not "
        + "reported.";

    /// <summary>Detail for a configured file that is not present.</summary>
    /// <remarks>
    /// Reported as a reference that resolves to nothing rather than as a missing FILE, and phrased so
    /// that it cannot reveal whether the reference had a configured value at all. Echoing the resolved
    /// path, the file's name or its location would reintroduce exactly what the reference indirection
    /// removes.
    /// </remarks>
    internal const string FileNotAvailableDetail =
        "No file is available for this reference. This response echoes no path, no filename, no "
        + "location, no size and no part of any file's contents.";

    /// <summary>Detail for a configured file that could not be read.</summary>
    private const string FileUnreadableDetail =
        "The file this reference names could not be read. This is a condition of this service's own "
        + "environment rather than of the request. No path, filename, location or content is reported.";

    /// <summary>Detail for the retained-key store having no room for another pair.</summary>
    private const string GeneratedKeyStoreFullDetail =
        "This service cannot retain another generated private key, so no key was generated. The "
        + "retained-key store is bounded deliberately, both in total and PER CALLER: it grows only on "
        + "request from an authenticated caller, and an unbounded store on the service that holds the "
        + "system's only signing key would let that growth terminate authentication for the whole "
        + "system, while an unbounded per-caller share would let one caller starve its peers using "
        + "nothing but legitimate calls. Release a key reference you no longer need, or wait for one to "
        + "expire, and retry. Whether the total or your own share is the binding limit is deliberately "
        + "not reported, because that would describe how much of the store other callers hold.";

    /// <summary>Published summary of the retained-key release operation.</summary>
    private const string ReleaseRsaKeySummary =
        "Release a retained generated private key by its reference.";

    /// <summary>Published description of the retained-key release operation.</summary>
    private const string ReleaseRsaKeyDescription =
        "Releases the private key this service retained under the given reference, freeing the caller's "
        + "quota immediately. PART OF THE SAME NARROWING as the generation operation: because the "
        + "private key is retained rather than returned, the caller needs a way to say it is finished "
        + "with one - otherwise the only way a slot is freed is the expiry backstop, and a provisioning "
        + "sequence longer than the per-caller quota would stall for no reason. A CALLER MAY RELEASE "
        + "ONLY ITS OWN KEYS, identified by the subject claim of its token. A reference that names "
        + "nothing retained and one that names another caller's key answer IDENTICALLY with 404, "
        + "deliberately: distinguishing them would turn this operation into an oracle for which "
        + "references exist, and a reference is a credential-like handle. Releasing is idempotent from "
        + "the caller's point of view - a second release of the same reference answers 404. Retained "
        + "keys also expire on their own, so a caller that crashes without releasing does not hold a "
        + "slot for the life of the process.";

    /// <summary>Detail reported when a release names no key this caller holds.</summary>
    /// <remarks>
    /// ONE MESSAGE FOR BOTH NEGATIVE CASES, and the wording is careful not to imply which applies. See
    /// <see cref="CryptoReferenceResolver.TryReleaseGeneratedKey"/> for why they must be
    /// indistinguishable.
    /// </remarks>
    private const string GeneratedKeyNotHeldDetail =
        "No generated private key is retained under that reference for this caller. It may never have "
        + "existed, it may already have been released, it may have expired, or it may belong to a "
        + "different caller - these are deliberately indistinguishable, because telling them apart "
        + "would report the existence of references this caller does not hold. This message never "
        + "echoes the reference.";

    /// <summary>Detail prefix for a blocked symmetric cell.</summary>
    /// <remarks>
    /// The refusal's own reason text comes from <see cref="SymmetricParityUnavailableException"/>,
    /// which is authored beside the classification it belongs to, so this file states the CONTRACT
    /// half - that the cell is blocked rather than unsupported, and that the limitation is the port's
    /// - and does not restate the evidence.
    /// </remarks>
    private const string BlockedCellDetailPrefix =
        "This combination of mode and initialization-vector presence is BLOCKED rather than "
        + "unsupported: the request is well-formed and would have succeeded, and the limitation is "
        + "this port's rather than the caller's. Encrypting under a guessed parameter would produce "
        + "ciphertext that round-trips perfectly against itself and that the legacy cannot decrypt, "
        + "which is data loss presented as success. ";


    // ==============================================================================================
    //  EXTENSION MEMBER AND LOGGING NAMES
    // ==============================================================================================

    /// <summary>
    /// The name of the problem extension member carrying a blocked cell's machine-readable reason.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Spelled exactly as the authored contract's <c>x-blocked-cells</c> entries name it, so a client
    /// that refuses a blocked cell in advance and a client that discovers it from a response read the
    /// SAME vocabulary. The published problem schema is the only one in that document with
    /// <c>additionalProperties: true</c>, which is what permits this member.
    /// </para>
    /// <para>
    /// It is added to the body built by the shared factory rather than by declaring a second problem
    /// shape - the folder has exactly one error shape and this file must not fork it.
    /// </para>
    /// </remarks>
    internal const string ReasonExtensionMember = "reason";

    /// <summary>The reason code published for a refused CFB cell.</summary>
    /// <remarks>
    /// The value is the enumeration member's own name from
    /// <see cref="SymmetricCellParity.BlockedFeedbackWidthUnprovable"/>, rendered in the authored
    /// contract's spelling. It is a STRING LITERAL rather than a declared identifier, so the naming
    /// analysers this file is not exempt from have nothing to report.
    /// </remarks>
    internal const string FeedbackWidthUnprovableReason = "SYMMETRIC_FEEDBACK_WIDTH_UNPROVABLE";

    /// <summary>The reason code published for a refused vector-less CBC cell.</summary>
    internal const string SynthesizedVectorUnprovableReason = "SYMMETRIC_VECTOR_UNPROVABLE";

    /// <summary>The logger category the cryptographic surface records under.</summary>
    private const string LoggerCategoryName = "PowerFramework.Security.Endpoints.CryptoEndpoints";

    /// <summary>
    /// The structured template a completed operation is recorded with.
    /// </summary>
    /// <remarks>
    /// EVERY FIELD IS A CLASSIFIER, NEVER CONTENT. The operation identifier and the algorithm, mode
    /// and padding selectors are published identifiers whose values a caller chose from a documented
    /// set; none of them is secret and none of them is caller DATA. A plaintext, a ciphertext, a
    /// digest, a signature, a resolved key, an initialization vector, a generated key pair, a
    /// reference and an authorization header are not merely omitted - THIS METHOD HAS NO PARAMETER
    /// THROUGH WHICH ONE COULD ARRIVE. It is written at debug level because these operations are
    /// called in bulk and a record per call at a shipped level would be pure noise; a REJECTION is
    /// recorded by the shared problem factory at warning or error level instead.
    /// </remarks>
    private const string CompletedOperationLogTemplate =
        "Security completed a cryptographic operation. operation={Operation} algorithm={Algorithm} "
        + "mode={Mode} padding={Padding}. No payload, key, vector, reference, digest or signature is "
        + "recorded.";

    // ==============================================================================================
    //  THE ONE PUBLIC ENTRY POINT
    // ==============================================================================================

    /// <summary>
    /// Maps the 18 operations of contract C-02 onto a single authenticated route group.
    /// </summary>
    /// <param name="endpoints">The route builder to declare the routes on.</param>
    /// <returns>
    /// The same <paramref name="endpoints"/> instance, so that mapping calls compose.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="endpoints"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>EIGHTEEN PUBLISHED, SEVENTEEN PROJECTING - two numbers that are easy to conflate and are kept
    /// apart deliberately.</b> Seventeen POST operations between them project ALL 63 of the legacy
    /// surface's cryptographic overloads. The eighteenth,
    /// <c>DELETE /v1/crypto/rsa/keys/{keyRef}</c>, is AUTHORED and projects nothing: the legacy had no
    /// key store, so it had nothing to release. Prose that says "17" about the published surface is
    /// wrong by one, and prose that says "18" about the legacy projection is wrong by one the other way.
    /// </para>
    /// <para>
    /// The single public member of this file, matching the one-registration-method-per-endpoint-file
    /// shape the whole folder uses and that <c>Program.cs</c> calls once per file.
    /// </para>
    /// <para>
    /// A ROUTE GROUP RATHER THAN 18 INDEPENDENT DECLARATIONS, and that is a safety property rather
    /// than a tidiness one: <see cref="AuthorizationEndpointConventionBuilderExtensions.RequireAuthorization{TBuilder}(TBuilder)"/>
    /// and the published tag are applied ONCE to the group, so a route added to this file later cannot
    /// be published anonymously by forgetting a line. Every route below inherits both.
    /// </para>
    /// <para>
    /// Each route additionally declares the exact response set the authored contract declares for it,
    /// because those statuses are part of the published contract rather than an implementation detail
    /// - the 401 in particular is the standing proof that this boundary is authenticated, and it is
    /// written by the authentication middleware before any handler runs rather than by this file.
    /// </para>
    /// </remarks>
    public static IEndpointRouteBuilder MapCryptoEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder group = endpoints
            .MapGroup(RouteGroupPrefix)
            // ------------------------------------------------------------------------------------
            // CONSTRAINT C-G, APPLIED ONCE AND UNCONDITIONALLY TO EVERY OPERATION IN THE GROUP.
            //
            // Not wrapped in an environment test, not paired with an anonymous fallback, and not
            // weakened by a configuration switch. Called EXPLICITLY rather than relying on the host's
            // default-deny fallback policy: the fallback would answer the same today, but a
            // requirement satisfied by OMISSION is invisible at the declaration and would evaporate
            // silently if that policy were ever relaxed.
            //
            // AND THE POLICY IS NAMED RATHER THAN PARAMETERLESS, WHICH IS THE LOAD-BEARING HALF. The
            // parameterless form requires an authenticated principal AND NOTHING MORE, so any holder of any
            // token minted for this service's audience could drive all 18 cryptographic operations - keyed
            // HMAC, symmetric encryption and decryption, RSA signing and RSA key generation among them -
            // regardless of what that credential was obtained for and regardless of which caller it was
            // minted for (CWE-862, CWE-863). Contract C-02 says who this surface is for: Security serves
            // DataServices, and DataServices requests exactly `security.crypto` for it. The named policy
            // requires that scope AND a configured permitted caller identity, because either alone
            // leaves a hole. None of this contract's operations is one of the service's three anonymous
            // exemptions, so AllowAnonymous appears nowhere in this file.
            // ------------------------------------------------------------------------------------
            .RequireAuthorization(CryptoCallerAuthorization.PolicyName)
            .WithTags(TagName);

        // The bearer requirement is declared on every operation of the group at once. The document
        // generator does not synthesise one from authorization metadata, so requiring a token without
        // declaring it would leave the published description claiming an anonymous surface while the
        // running service answers 401.
        group.AddOpenApiOperationTransformer(DeclareBearerRequirementAsync);

        // THE FORBIDDEN STATUS IS DECLARED AT THE GROUP, BECAUSE THE SCOPE REQUIREMENT IS AT THE GROUP.
        // Every operation here can answer 403 for a reason that has nothing to do with its own
        // parameters - a valid token that is not scoped `security.crypto` - so the status belongs to the
        // group exactly as the requirement producing it does. The TEN reference-resolving operations also
        // declare it individually for their own second cause, an unpermitted reference; the generator
        // keys a response by its status, so the two declarations describe one response rather than
        // duplicating it. Declaring it here is what makes an operation added to this file inherit the
        // status it will actually answer instead of having to remember it.
        group.ProducesProblem(StatusCodes.Status403Forbidden);

        // ------------------------------------------------------------------------------------------
        // WHY EVERY OPERATION BELOW DECLARES 403, INCLUDING THE EIGHT THAT RESOLVE NO REFERENCE AT ALL.
        //
        // The group's scope policy applies to all eighteen, so a token that is valid, addressed to
        // this service and unexpired is still refused when its caller was never granted this
        // contract's scope. That makes 403 reachable on EVERY operation - not only on the ten that
        // resolve a caller-supplied reference and can refuse one.
        //
        // THE THREE COUNTS, MEASURED RATHER THAN ESTIMATED, BECAUSE THEY ARE EASY TO CONFLATE:
        //   * NINE take a keyRef. Eight carry one in the request body - hmac, hmac-file, both
        //     symmetric operations and all four RSA operations that CONSUME a key - and the release
        //     operation takes one as a path parameter. POST /v1/crypto/rsa/keys is NOT among them,
        //     because it RETURNS a reference instead of resolving one, which is why it alone among the
        //     RSA operations declares no 404.
        //   * TEN resolve a reference of SOME kind, the tenth being hash-file, which resolves a fileRef
        //     through the same allow-listed store mechanism rather than a keyRef. Reference resolution,
        //     not key resolution, is what makes a 404 reachable, so these ten are exactly the ten that
        //     declare one.
        //   * EIGHT therefore resolve nothing at all, and their 403 has only the scope cause.
        //
        // It is declared per operation rather than once on the group because the ten reference-resolving
        // operations must ALSO keep their own 403, and a published document that under-declares a
        // status the service produces is the specific defect this file's contract-conformance rows
        // exist to catch. In the authored document sixteen of the seventeen POST operations point their
        // 403 at the reference-refusal component, whose description covers BOTH causes and is therefore
        // correct for an operation that resolves nothing too; key generation points at the
        // scope-refusal component because scope is its only cause; and the release operation declares
        // its 403 inline, because it is the one operation whose refusal names an owner rather than a
        // store.
        // ------------------------------------------------------------------------------------------

        // ------------------------------------------------------------------------------------------
        // THE DIGEST FAMILY - n_crypto.sru:L21-L29
        // ------------------------------------------------------------------------------------------
        group.MapPost(HashRoute, Hash)
            .WithName(HashOperation)
            .WithSummary(HashSummary)
            .WithDescription(HashDescription)
            .Produces<DigestResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        group.MapPost(HmacRoute, Hmac)
            .WithName(HmacOperation)
            .WithSummary(HmacSummary)
            .WithDescription(HmacDescription)
            .Produces<DigestResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        group.MapPost(HashFileRoute, HashFile)
            .WithName(HashFileOperation)
            .WithSummary(HashFileSummary)
            .WithDescription(HashFileDescription)
            .Produces<DigestResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        group.MapPost(HmacFileRoute, HmacFile)
            .WithName(HmacFileOperation)
            .WithSummary(HmacFileSummary)
            .WithDescription(HmacFileDescription)
            .Produces<DigestResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        // ------------------------------------------------------------------------------------------
        // THE SYMMETRIC FAMILY - n_crypto.sru:L30-L61, all 32 declarations
        // ------------------------------------------------------------------------------------------
        group.MapPost(SymmetricEncryptRoute, SymmetricEncrypt)
            .WithName(SymmetricEncryptOperation)
            .WithSummary(SymmetricEncryptSummary)
            .WithDescription(SymmetricEncryptDescription)
            .Produces<PayloadResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        group.MapPost(SymmetricDecryptRoute, SymmetricDecrypt)
            .WithName(SymmetricDecryptOperation)
            .WithSummary(SymmetricDecryptSummary)
            .WithDescription(SymmetricDecryptDescription)
            .Produces<PayloadResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        // ------------------------------------------------------------------------------------------
        // THE RSA FAMILY - n_crypto.sru:L19-L20 and :L62-L73
        // ------------------------------------------------------------------------------------------
        group.MapPost(RsaEncryptRoute, RsaEncrypt)
            .WithName(RsaEncryptOperation)
            .WithSummary(RsaEncryptSummary)
            .WithDescription(RsaEncryptDescription)
            .Produces<PayloadResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        group.MapPost(RsaDecryptRoute, RsaDecrypt)
            .WithName(RsaDecryptOperation)
            .WithSummary(RsaDecryptSummary)
            .WithDescription(RsaDecryptDescription)
            .Produces<PayloadResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        group.MapPost(RsaSignRoute, RsaSign)
            .WithName(RsaSignOperation)
            .WithSummary(RsaSignSummary)
            .WithDescription(RsaSignDescription)
            .Produces<PayloadResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        group.MapPost(RsaVerifyRoute, RsaVerify)
            .WithName(RsaVerifyOperation)
            .WithSummary(RsaVerifySummary)
            .WithDescription(RsaVerifyDescription)
            .Produces<RsaVerifyResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        // The generation operation declares 403 but NOT 404: it names no reference on the way in, so
        // there is nothing for this service to fail to resolve. The 403 it does declare is the
        // authorization layer's own answer to an authenticated caller a deployment's policy does not
        // permit to generate a pair; no handler logic below invents one, because no such policy exists
        // in this service's configuration and inventing one would be a fabricated requirement.
        group.MapPost(RsaKeysRoute, GenerateRsaKey)
            .WithName(GenerateRsaKeyOperation)
            .WithSummary(GenerateRsaKeySummary)
            .WithDescription(GenerateRsaKeyDescription)
            .Produces<GenRsaKeyResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        // The release operation is the generation operation's counterpart, and it is DELETE rather than
        // POST because it removes the resource the generation operation named. It declares 204 for a
        // release and 404 for a reference this caller does not hold; it declares no 400, because the
        // only input is a path segment and any value of it is a well-formed request that simply names
        // nothing - and no 500, because there is no configured material to fail to read.
        group.MapDelete(RsaKeyReleaseRoute, ReleaseRsaKey)
            .WithName(ReleaseRsaKeyOperation)
            .WithSummary(ReleaseRsaKeySummary)
            .WithDescription(ReleaseRsaKeyDescription)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        // ------------------------------------------------------------------------------------------
        // THE RANDOM FAMILY - n_crypto.sru:L14-L18, the three determinism seams
        // ------------------------------------------------------------------------------------------
        group.MapPost(RandomBlobRoute, GenerateRandomBlob)
            .WithName(GenerateRandomBlobOperation)
            .WithSummary(GenerateRandomBlobSummary)
            .WithDescription(GenerateRandomBlobDescription)
            .Produces<BlobResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        group.MapPost(RandomStringRoute, GenerateRandomString)
            .WithName(GenerateRandomStringOperation)
            .WithSummary(GenerateRandomStringSummary)
            .WithDescription(GenerateRandomStringDescription)
            .Produces<RndStringResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        group.MapPost(RandomGuidRoute, GenerateGuid)
            .WithName(GenerateGuidOperation)
            .WithSummary(GenerateGuidSummary)
            .WithDescription(GenerateGuidDescription)
            .Produces<GuidResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        // ------------------------------------------------------------------------------------------
        // THE ENCODING FAMILY - n_crypto.sru:L11-L13
        // ------------------------------------------------------------------------------------------
        group.MapPost(StringToBlobRoute, StringToBlob)
            .WithName(StringToBlobOperation)
            .WithSummary(StringToBlobSummary)
            .WithDescription(StringToBlobDescription)
            .Produces<BlobResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        group.MapPost(BlobToStringRoute, BlobToString)
            .WithName(BlobToStringOperation)
            .WithSummary(BlobToStringSummary)
            .WithDescription(BlobToStringDescription)
            .Produces<EncodedTextResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        group.MapPost(BlobReverseRoute, ReverseBlob)
            .WithName(ReverseBlobOperation)
            .WithSummary(ReverseBlobSummary)
            .WithDescription(ReverseBlobDescription)
            .Produces<BlobReverseResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        return endpoints;
    }


    // ==============================================================================================
    //  HANDLERS - THE DIGEST FAMILY
    //  ----------------------------------------------------------------------------------------------
    //  Every handler below is a NAMED INTERNAL METHOD, never an inline lambda, so the sibling test
    //  project can call it directly with provider doubles as well as drive it through a booted host.
    //  Each takes its collaborators by injection and holds no state of its own.
    // ==============================================================================================

    /// <summary>
    /// Computes an unkeyed digest over an in-memory payload.
    /// </summary>
    /// <param name="request">The digest request.</param>
    /// <param name="hashes">The unkeyed digest provider.</param>
    /// <param name="encodings">The encoding provider, used only for the JSON transport encoding.</param>
    /// <param name="loggerFactory">The logger factory a rejection is recorded through.</param>
    /// <returns>200 carrying the digest, or a problem response.</returns>
    /// <exception cref="ArgumentNullException">A collaborator or the request is <see langword="null"/>.</exception>
    /// <remarks>
    /// Projects <c>string Hash(readonly string data, readonly long ntype)</c> and
    /// <c>string Hash(readonly blob data, readonly long ntype)</c>
    /// [ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L21-L22]. The FULL published hash set applies here,
    /// CRC32 included, because an unkeyed checksum is a legitimate thing to compute; the keyed and
    /// signature members narrow it and say why at their own screen.
    /// </remarks>
    internal static Results<Ok<DigestResponse>, ProblemHttpResult> Hash(
        HashRequest request,
        [FromServices] HashProvider hashes,
        [FromServices] EncodingProvider encodings,
        [FromServices] ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(hashes);
        ArgumentNullException.ThrowIfNull(encodings);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        if (request.PayloadForm is not PayloadForm form)
        {
            return MissingPayloadForm(loggerFactory);
        }

        if (request.Data is null)
        {
            return MissingMember(nameof(HashRequest.Data), loggerFactory);
        }

        ProblemHttpResult? rejection =
            ValidateDeclaredHashType(request.HashType, loggerFactory, out long hashType);

        if (rejection is not null)
        {
            return rejection;
        }

        string digest;

        if (form == PayloadForm.STRING)
        {
            digest = hashes.Hash(request.Data, hashType);
        }
        else
        {
            rejection = TryDecodeTransportBytes(request.Data, encodings, loggerFactory, out byte[] payload);

            if (rejection is not null)
            {
                return rejection;
            }

            digest = hashes.Hash(payload, hashType);
        }

        LogCompleted(loggerFactory, HashOperation, hashType, mode: null, padding: null);

        // The digest is a STRING for both legacy overloads, so the response carries no payload form.
        return TypedResults.Ok(new DigestResponse(digest));
    }

    /// <summary>
    /// Computes a keyed digest over an in-memory payload.
    /// </summary>
    /// <param name="request">The keyed digest request.</param>
    /// <param name="authenticators">The keyed digest provider.</param>
    /// <param name="encodings">The encoding provider, used only for the JSON transport encoding.</param>
    /// <param name="references">The reference resolver.</param>
    /// <param name="loggerFactory">The logger factory a rejection is recorded through.</param>
    /// <returns>200 carrying the digest, or a problem response.</returns>
    /// <exception cref="ArgumentNullException">A collaborator or the request is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// Projects the four keyed <c>Hash</c> overloads at
    /// [ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L23-L26], whose key half is collapsed by the
    /// reference indirection. The KEY FORM FOLLOWS THE PAYLOAD FORM here, so both provider families
    /// are exercised from the boundary; the construction normalises a text key and its UTF-8 bytes
    /// identically, so the pairing loses nothing.
    /// </para>
    /// <para>
    /// The resolved key is used and dropped. The binary form is zeroed in a finally block, which the
    /// text form cannot be - a string is immutable and there is no supported way to wipe one - and
    /// that limitation is recorded rather than papered over.
    /// </para>
    /// </remarks>
    internal static Results<Ok<DigestResponse>, ProblemHttpResult> Hmac(
        HmacRequest request,
        ClaimsPrincipal user,
        [FromServices] HmacProvider authenticators,
        [FromServices] EncodingProvider encodings,
        [FromServices] CryptoReferenceResolver references,
        [FromServices] ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(authenticators);
        ArgumentNullException.ThrowIfNull(encodings);
        ArgumentNullException.ThrowIfNull(references);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        if (request.PayloadForm is not PayloadForm form)
        {
            return MissingPayloadForm(loggerFactory);
        }

        if (request.Data is null)
        {
            return MissingMember(nameof(HmacRequest.Data), loggerFactory);
        }

        ProblemHttpResult? rejection =
            ValidateKeyedHashType(request.HashType, loggerFactory, out long hashType);

        if (rejection is not null)
        {
            return rejection;
        }

        rejection = references.TryResolveReference(
            request.KeyRef,
            OwnerOf(user),
            loggerFactory,
            out string keyMaterial);

        if (rejection is not null)
        {
            return rejection;
        }

        string digest;

        if (form == PayloadForm.STRING)
        {
            digest = authenticators.Hash(request.Data, keyMaterial, hashType);
        }
        else
        {
            rejection = TryDecodeTransportBytes(request.Data, encodings, loggerFactory, out byte[] payload);

            if (rejection is not null)
            {
                return rejection;
            }

            byte[] keyBytes = LegacyDefaults.KeyMaterialEncoding.GetBytes(keyMaterial);

            try
            {
                digest = authenticators.Hash(payload, keyBytes, hashType);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(keyBytes);
            }
        }

        LogCompleted(loggerFactory, HmacOperation, hashType, mode: null, padding: null);

        return TypedResults.Ok(new DigestResponse(digest));
    }

    /// <summary>
    /// Computes an unkeyed digest over a file this service has been configured to expose.
    /// </summary>
    /// <param name="request">The file digest request.</param>
    /// <param name="hashes">The unkeyed digest provider.</param>
    /// <param name="references">The reference resolver.</param>
    /// <param name="loggerFactory">The logger factory a rejection is recorded through.</param>
    /// <returns>200 carrying the digest, or a problem response.</returns>
    /// <exception cref="ArgumentNullException">A collaborator or the request is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// Projects <c>string HashFile(readonly string filename, readonly long ntype)</c>
    /// [ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L27]. The legacy filename parameter becomes an
    /// OPAQUE REFERENCE resolved against this service's own configured store, so no caller-supplied
    /// path crosses the boundary and there is no traversal to defend against. The provider streams the
    /// file in bounded buffers, so a configured file's size is not a memory lever.
    /// </para>
    /// <para>
    /// The full published hash set applies, CRC32 included: the oracle's own demonstration digests a
    /// multi-megabyte native binary with exactly that checksum.
    /// </para>
    /// </remarks>
    internal static Results<Ok<DigestResponse>, ProblemHttpResult> HashFile(
        HashFileRequest request,
        [FromServices] HashProvider hashes,
        [FromServices] CryptoReferenceResolver references,
        [FromServices] ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(hashes);
        ArgumentNullException.ThrowIfNull(references);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        ProblemHttpResult? rejection =
            ValidateDeclaredHashType(request.HashType, loggerFactory, out long hashType);

        if (rejection is not null)
        {
            return rejection;
        }

        rejection = references.TryResolveFile(request.FileRef, loggerFactory, out string path);

        if (rejection is not null)
        {
            return rejection;
        }

        rejection = ScreenFileSize(path, loggerFactory);

        if (rejection is not null)
        {
            return rejection;
        }

        string digest;

        try
        {
            digest = hashes.HashFile(path, hashType);
        }
        catch (IOException)
        {
            // The path is NEVER interpolated into the detail or the log record: it is this
            // deployment's own filesystem layout, and the indirection exists precisely so that it
            // does not travel.
            return Reject(RetCode.E_IO_ERROR, FileUnreadableDetail, loggerFactory);
        }
        catch (UnauthorizedAccessException)
        {
            return Reject(RetCode.E_IO_ERROR, FileUnreadableDetail, loggerFactory);
        }

        LogCompleted(loggerFactory, HashFileOperation, hashType, mode: null, padding: null);

        return TypedResults.Ok(new DigestResponse(digest));
    }

    /// <summary>
    /// Screens a resolved file against the published work bound.
    /// </summary>
    /// <param name="path">The resolved path, which is never reported.</param>
    /// <param name="loggerFactory">The logger factory a refusal is recorded through.</param>
    /// <returns>A refusal, or <see langword="null"/> when the file is within the bound.</returns>
    /// <remarks>
    /// <para>
    /// ONE HELPER FOR BOTH FILE OPERATIONS, because two copies of a bound is how one of them is forgotten
    /// when the other moves. Both operations reach it after the reference has resolved, so the screen reads
    /// a path this deployment configured rather than anything a caller supplied.
    /// </para>
    /// <para>
    /// AN UNREADABLE FILE IS NOT THIS SCREEN'S BUSINESS. Reading the size can fail for exactly the reasons
    /// reading the content can, and those are already answered - with their own return code and their own
    /// sentence - by the operations themselves. Reporting them here as well would give one condition two
    /// different answers depending on which read happened to fail first, so an unreadable file passes the
    /// screen and is refused by the operation.
    /// </para>
    /// </remarks>
    private static ProblemHttpResult? ScreenFileSize(string path, ILoggerFactory loggerFactory)
    {
        long length;

        try
        {
            length = new FileInfo(path).Length;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        return length > MaximumDigestedFileBytes
            ? Reject(RetCode.E_OUT_OF_RANGE, FileTooLargeDetail, loggerFactory)
            : null;
    }

    /// <summary>
    /// Computes a keyed digest over a file this service has been configured to expose.
    /// </summary>
    /// <param name="request">The keyed file digest request.</param>
    /// <param name="authenticators">The keyed digest provider.</param>
    /// <param name="references">The reference resolver.</param>
    /// <param name="loggerFactory">The logger factory a rejection is recorded through.</param>
    /// <returns>200 carrying the digest, or a problem response.</returns>
    /// <exception cref="ArgumentNullException">A collaborator or the request is <see langword="null"/>.</exception>
    /// <remarks>
    /// Projects the two keyed <c>HashFile</c> overloads at
    /// [ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L28-L29], which differ only in the key's form - a
    /// distinction the reference indirection collapses entirely. Both the filename and the key stand
    /// behind references, so neither a path nor key material crosses this boundary in either
    /// direction.
    /// </remarks>
    internal static Results<Ok<DigestResponse>, ProblemHttpResult> HmacFile(
        HmacFileRequest request,
        ClaimsPrincipal user,
        [FromServices] HmacProvider authenticators,
        [FromServices] CryptoReferenceResolver references,
        [FromServices] ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(authenticators);
        ArgumentNullException.ThrowIfNull(references);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        ProblemHttpResult? rejection =
            ValidateKeyedHashType(request.HashType, loggerFactory, out long hashType);

        if (rejection is not null)
        {
            return rejection;
        }

        rejection = references.TryResolveReference(
            request.KeyRef,
            OwnerOf(user),
            loggerFactory,
            out string keyMaterial);

        if (rejection is not null)
        {
            return rejection;
        }

        rejection = references.TryResolveFile(request.FileRef, loggerFactory, out string path);

        if (rejection is not null)
        {
            return rejection;
        }

        rejection = ScreenFileSize(path, loggerFactory);

        if (rejection is not null)
        {
            return rejection;
        }

        string digest;

        try
        {
            digest = authenticators.HashFile(path, keyMaterial, hashType);
        }
        catch (IOException)
        {
            return Reject(RetCode.E_IO_ERROR, FileUnreadableDetail, loggerFactory);
        }
        catch (UnauthorizedAccessException)
        {
            return Reject(RetCode.E_IO_ERROR, FileUnreadableDetail, loggerFactory);
        }

        LogCompleted(loggerFactory, HmacFileOperation, hashType, mode: null, padding: null);

        return TypedResults.Ok(new DigestResponse(digest));
    }

    // ==============================================================================================
    //  HANDLERS - THE SYMMETRIC FAMILY
    // ==============================================================================================

    /// <summary>
    /// Encrypts a payload with one of the five published symmetric ciphers.
    /// </summary>
    /// <param name="request">The encryption request.</param>
    /// <param name="ciphers">The symmetric cipher provider.</param>
    /// <param name="encodings">The encoding provider, used only for the JSON transport encoding.</param>
    /// <param name="references">The reference resolver.</param>
    /// <param name="loggerFactory">The logger factory a rejection is recorded through.</param>
    /// <returns>200 carrying the ciphertext in the submitted form, or a problem response.</returns>
    /// <remarks>
    /// Projects the 16 <c>SymEncrypt</c> overloads at
    /// [ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L30-L45]. The two directions share their whole
    /// validation, resolution and dispatch path because the legacy declares them symmetrically, and
    /// they remain two operations because the contract publishes two.
    /// </remarks>
    internal static Results<Ok<PayloadResponse>, ProblemHttpResult> SymmetricEncrypt(
        SymEncryptRequest request,
        ClaimsPrincipal user,
        [FromServices] SymmetricCipherProvider ciphers,
        [FromServices] EncodingProvider encodings,
        [FromServices] CryptoReferenceResolver references,
        [FromServices] ILoggerFactory loggerFactory) =>
        SymmetricOperation(
            request,
            user,
            ciphers,
            encodings,
            references,
            loggerFactory,
            encrypting: true,
            SymmetricEncryptOperation);

    /// <summary>
    /// Decrypts a payload with one of the five published symmetric ciphers.
    /// </summary>
    /// <param name="request">The decryption request.</param>
    /// <param name="ciphers">The symmetric cipher provider.</param>
    /// <param name="encodings">The encoding provider, used only for the JSON transport encoding.</param>
    /// <param name="references">The reference resolver.</param>
    /// <param name="loggerFactory">The logger factory a rejection is recorded through.</param>
    /// <returns>200 carrying the plaintext in the submitted form, or a problem response.</returns>
    /// <remarks>
    /// Projects the 16 <c>SymDecrypt</c> overloads at
    /// [ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L46-L61]. A 200 DOES NOT ASSERT THAT THE CORRECT
    /// KEY WAS USED: there is no authenticated mode on this surface, so a decryption under the wrong
    /// key, mode or vector can return a well-formed but meaningless payload, and no integrity check
    /// this contract can offer distinguishes the two.
    /// </remarks>
    internal static Results<Ok<PayloadResponse>, ProblemHttpResult> SymmetricDecrypt(
        SymDecryptRequest request,
        ClaimsPrincipal user,
        [FromServices] SymmetricCipherProvider ciphers,
        [FromServices] EncodingProvider encodings,
        [FromServices] CryptoReferenceResolver references,
        [FromServices] ILoggerFactory loggerFactory) =>
        SymmetricOperation(
            request,
            user,
            ciphers,
            encodings,
            references,
            loggerFactory,
            encrypting: false,
            SymmetricDecryptOperation);

    /// <summary>
    /// The one body both symmetric directions run through.
    /// </summary>
    /// <param name="request">The request, whose shape is identical in both directions.</param>
    /// <param name="ciphers">The symmetric cipher provider.</param>
    /// <param name="encodings">The encoding provider.</param>
    /// <param name="references">The reference resolver.</param>
    /// <param name="loggerFactory">The logger factory.</param>
    /// <param name="encrypting">
    /// <see langword="true"/> for the encrypt direction, <see langword="false"/> for the decrypt
    /// direction. It selects the provider family and nothing else - no validation, resolution or
    /// classification rule differs between the two.
    /// </param>
    /// <param name="operation">The operation identifier, recorded in the completion log record.</param>
    /// <returns>200 carrying the result in the submitted form, or a problem response.</returns>
    /// <exception cref="ArgumentNullException">A collaborator or the request is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// THE ORDER OF THE STEPS IS DELIBERATE. Selector screening comes first, so a malformed request is
    /// refused before any reference is resolved; cell classification comes next, so a BLOCKED cell is
    /// refused before a key is read at all; and resolution comes last, so the store is touched only
    /// for a request that could actually be carried out.
    /// </para>
    /// <para>
    /// A CRYPTOGRAPHIC REFUSAL IS CLASSIFIED BY DIRECTION, and the split is structural rather than a
    /// guess about an exception message. On the ENCRYPT direction a plaintext cannot be invalid, so the
    /// only cause is the configured key material - this platform refuses the known weak and semi-weak
    /// DES keys and 3DES keys whose adjacent sub-keys coincide, which shorter material reaches once it
    /// is zero-padded - and that is a server-side condition answered 500, exactly as the authored
    /// contract states for it. On the DECRYPT direction the ciphertext is the caller's, so a padding
    /// failure is the caller's data and is answered 400. Neither arm inspects an exception message.
    /// </para>
    /// </remarks>
    private static Results<Ok<PayloadResponse>, ProblemHttpResult> SymmetricOperation(
        ISymmetricCipherRequest request,
        ClaimsPrincipal user,
        SymmetricCipherProvider ciphers,
        EncodingProvider encodings,
        CryptoReferenceResolver references,
        ILoggerFactory loggerFactory,
        bool encrypting,
        string operation)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(ciphers);
        ArgumentNullException.ThrowIfNull(encodings);
        ArgumentNullException.ThrowIfNull(references);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        if (request.PayloadForm is not PayloadForm form)
        {
            return MissingPayloadForm(loggerFactory);
        }

        if (request.Data is null)
        {
            return MissingMember(nameof(ISymmetricCipherRequest.Data), loggerFactory);
        }

        if (request.CipherType is not long declaredCipherType)
        {
            return MissingMember(nameof(ISymmetricCipherRequest.CipherType), loggerFactory);
        }

        // The legacy types this argument as a 16-bit unsigned value on all 32 overloads while
        // declaring the constants themselves as 32-bit signed [enums.sru:L936-L940] - an inconsistency
        // this port carries rather than tidies. The domain check is what lets the published predicate,
        // which takes the narrower type, be the only allow-list consulted.
        if (declaredCipherType is < 0 or > ushort.MaxValue ||
            !LegacyDefaults.IsSupportedSymmetricType((ushort)declaredCipherType))
        {
            return Reject(RetCode.E_NO_SUPPORT, UnsupportedCipherTypeDetail, loggerFactory);
        }

        if (request.Mode is long declaredMode && !LegacyDefaults.IsSupportedSymmetricMode(declaredMode))
        {
            return Reject(RetCode.E_NO_SUPPORT, UnsupportedCipherModeDetail, loggerFactory);
        }

        // Omitting the mode selects the LEGACY DEFAULT, which is ECB [enums.sru:L946]. The effective
        // value is needed to classify the cell; the REQUEST's value - present or absent - is what
        // selects the overload family below, so a mode-omitting request genuinely reaches a
        // mode-omitting legacy arm rather than being rewritten into an explicit ECB call.
        long effectiveMode = request.Mode ?? LegacyDefaults.SYMMETRIC_MODE_DEFAULT;
        bool vectorSupplied = request.IvRef is not null;

        SymmetricCellParity parity =
            LegacyDefaults.ClassifySymmetricCell(effectiveMode, vectorSupplied);

        if (parity != SymmetricCellParity.Supported)
        {
            return RefuseBlockedCell(parity, effectiveMode, loggerFactory);
        }

        ProblemHttpResult? rejection = references.TryResolveReference(
            request.KeyRef,
            OwnerOf(user),
            loggerFactory,
            out string keyMaterial);

        if (rejection is not null)
        {
            return rejection;
        }

        string? vectorMaterial = null;

        if (vectorSupplied)
        {
            rejection = references.TryResolveReference(
                request.IvRef,
                OwnerOf(user),
                loggerFactory,
                out string resolvedVector);

            if (rejection is not null)
            {
                return rejection;
            }

            vectorMaterial = resolvedVector;
        }

        ushort cipherType = (ushort)declaredCipherType;

        try
        {
            if (form == PayloadForm.STRING)
            {
                string textResult = InvokeTextSymmetric(
                    ciphers,
                    encrypting,
                    request.Data,
                    keyMaterial,
                    vectorMaterial,
                    cipherType,
                    request.Mode);

                LogCompleted(loggerFactory, operation, declaredCipherType, effectiveMode, padding: null);

                return TypedResults.Ok(new PayloadResponse(form, textResult));
            }

            rejection = TryDecodeTransportBytes(request.Data, encodings, loggerFactory, out byte[] payload);

            if (rejection is not null)
            {
                return rejection;
            }

            byte[] keyBytes = LegacyDefaults.KeyMaterialEncoding.GetBytes(keyMaterial);
            byte[]? vectorBytes = vectorMaterial is null
                ? null
                : LegacyDefaults.KeyMaterialEncoding.GetBytes(vectorMaterial);

            try
            {
                byte[] binaryResult = InvokeBinarySymmetric(
                    ciphers,
                    encrypting,
                    payload,
                    keyBytes,
                    vectorBytes,
                    cipherType,
                    request.Mode);

                LogCompleted(loggerFactory, operation, declaredCipherType, effectiveMode, padding: null);

                return TypedResults.Ok(new PayloadResponse(form, encodings.Base64Encode(binaryResult)));
            }
            finally
            {
                // The buffers this handler derived are spent the moment the call returns, whether it
                // returned a result or threw.
                CryptographicOperations.ZeroMemory(keyBytes);

                if (vectorBytes is not null)
                {
                    CryptographicOperations.ZeroMemory(vectorBytes);
                }
            }
        }
        catch (CryptographicException)
        {
            // THE SYMMETRIC FAMILY HAS NOTHING TO SCREEN AHEAD OF THIS, AND THAT IS A PROPERTY OF THE
            // PORTED RULE RATHER THAN AN OMISSION. Key and vector material of ANY length is accepted:
            // DECISION D1 truncates or zero-pads both to exactly the cipher's length
            // [LegacyDefaults.NormalizeKeyMaterial], so there is no size, structure or kind a screen
            // could reject - unlike the asymmetric family, where a public key where a private one is
            // needed is decidable and IS screened before the payload.
            //
            // The direction is therefore the only signal available, and it is a sound one on the
            // encrypting side: encryption consumes the payload without interpreting it, so a refusal
            // there is a property of the configured material and is answered as a server fault. On the
            // decrypting side a wrong key, a wrong mode and a corrupt payload all raise the identical
            // failure, because this surface has no authenticated encryption and so carries no integrity
            // tag to separate them - a preserved legacy weakness the published contract states at the
            // operation. That case is attributed to the payload.
            return encrypting
                ? Reject(RetCode.E_INTERNAL_ERROR, CryptographicRefusalDetail, loggerFactory)
                : Reject(RetCode.E_INVALID_DATA, UnprocessablePayloadDetail, loggerFactory);
        }
        catch (FormatException)
        {
            // Reachable on the decrypt direction with a STRING payload: the string-shaped legacy
            // overloads carry a printable encoded form, so text that is not in that form cannot be
            // decrypted. It is the caller's payload and is answered as such, and the value is not
            // reported.
            return Reject(RetCode.E_INVALID_DATA, UnprocessablePayloadDetail, loggerFactory);
        }
    }

    /// <summary>
    /// Dispatches the four text-payload, text-key symmetric arms of one direction.
    /// </summary>
    /// <param name="ciphers">The cipher provider.</param>
    /// <param name="encrypting">Which direction to invoke.</param>
    /// <param name="payload">The payload, as the legacy string-shaped overloads take it.</param>
    /// <param name="key">The resolved key material.</param>
    /// <param name="vector">The resolved vector material, or <see langword="null"/> for the no-vector family.</param>
    /// <param name="cipherType">The published cipher type.</param>
    /// <param name="mode">
    /// The mode AS THE REQUEST CARRIED IT. <see langword="null"/> selects a mode-omitting legacy
    /// overload, which is how the eight ECB-by-default arms are genuinely reached.
    /// </param>
    /// <returns>The result, in the legacy string form.</returns>
    /// <remarks>
    /// Eight legacy declarations reach this method - four per direction - and each arm below is one of
    /// them. The vector's form follows the key's form here by construction, which is exactly the
    /// legacy pairing: a text key pairs only with a text vector.
    /// </remarks>
    private static string InvokeTextSymmetric(
        SymmetricCipherProvider ciphers,
        bool encrypting,
        string payload,
        string key,
        string? vector,
        ushort cipherType,
        long? mode)
    {
        if (encrypting)
        {
            if (vector is null)
            {
                return mode is null
                    ? ciphers.SymEncrypt(payload, key, cipherType)
                    : ciphers.SymEncrypt(payload, key, cipherType, mode.Value);
            }

            return mode is null
                ? ciphers.SymEncrypt(payload, key, vector, cipherType)
                : ciphers.SymEncrypt(payload, key, vector, cipherType, mode.Value);
        }

        if (vector is null)
        {
            return mode is null
                ? ciphers.SymDecrypt(payload, key, cipherType)
                : ciphers.SymDecrypt(payload, key, cipherType, mode.Value);
        }

        return mode is null
            ? ciphers.SymDecrypt(payload, key, vector, cipherType)
            : ciphers.SymDecrypt(payload, key, vector, cipherType, mode.Value);
    }

    /// <summary>
    /// Dispatches the four binary-payload, binary-key symmetric arms of one direction.
    /// </summary>
    /// <param name="ciphers">The cipher provider.</param>
    /// <param name="encrypting">Which direction to invoke.</param>
    /// <param name="payload">The payload bytes, as the legacy blob-shaped overloads take them.</param>
    /// <param name="key">The resolved key material as bytes.</param>
    /// <param name="vector">The resolved vector bytes, or <see langword="null"/> for the no-vector family.</param>
    /// <param name="cipherType">The published cipher type.</param>
    /// <param name="mode">The mode as the request carried it; <see langword="null"/> selects a mode-omitting overload.</param>
    /// <returns>The result bytes, which the caller encodes for transport.</returns>
    /// <remarks>
    /// The other eight declarations of the 32, and the reason the key-form pairing exists: with both
    /// this method and its text sibling reachable from the boundary, NEITHER provider family is dead
    /// code, and the sibling test project's matrix covers the remaining mixed-form declarations
    /// directly against the provider.
    /// </remarks>
    private static byte[] InvokeBinarySymmetric(
        SymmetricCipherProvider ciphers,
        bool encrypting,
        byte[] payload,
        byte[] key,
        byte[]? vector,
        ushort cipherType,
        long? mode)
    {
        if (encrypting)
        {
            if (vector is null)
            {
                return mode is null
                    ? ciphers.SymEncrypt(payload, key, cipherType)
                    : ciphers.SymEncrypt(payload, key, cipherType, mode.Value);
            }

            return mode is null
                ? ciphers.SymEncrypt(payload, key, vector, cipherType)
                : ciphers.SymEncrypt(payload, key, vector, cipherType, mode.Value);
        }

        if (vector is null)
        {
            return mode is null
                ? ciphers.SymDecrypt(payload, key, cipherType)
                : ciphers.SymDecrypt(payload, key, cipherType, mode.Value);
        }

        return mode is null
            ? ciphers.SymDecrypt(payload, key, vector, cipherType)
            : ciphers.SymDecrypt(payload, key, vector, cipherType, mode.Value);
    }


    // ==============================================================================================
    //  HANDLERS - THE RSA FAMILY
    // ==============================================================================================

    /// <summary>
    /// Encrypts a payload with an RSA public key held behind a reference.
    /// </summary>
    /// <param name="request">The RSA cipher request.</param>
    /// <param name="rsa">The RSA provider.</param>
    /// <param name="encodings">The encoding provider, used only for the JSON transport encoding.</param>
    /// <param name="references">The reference resolver.</param>
    /// <param name="loggerFactory">The logger factory a rejection is recorded through.</param>
    /// <returns>200 carrying the ciphertext in the submitted form, or a problem response.</returns>
    /// <remarks>
    /// Projects the four <c>RSAEncrypt</c> overloads at
    /// [ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L62-L65]. Omitting the padding reaches a
    /// padding-omitting legacy arm, which runs under PKCS#1 v1.5.
    /// </remarks>
    internal static Results<Ok<PayloadResponse>, ProblemHttpResult> RsaEncrypt(
        RsaCipherRequest request,
        ClaimsPrincipal user,
        [FromServices] RsaProvider rsa,
        [FromServices] EncodingProvider encodings,
        [FromServices] CryptoReferenceResolver references,
        [FromServices] ILoggerFactory loggerFactory) =>
        RsaCipherOperation(
            request,
            user,
            rsa,
            encodings,
            references,
            loggerFactory,
            encrypting: true,
            RsaEncryptOperation);

    /// <summary>
    /// Decrypts a payload with an RSA private key held behind a reference.
    /// </summary>
    /// <param name="request">The RSA cipher request.</param>
    /// <param name="rsa">The RSA provider.</param>
    /// <param name="encodings">The encoding provider, used only for the JSON transport encoding.</param>
    /// <param name="references">The reference resolver.</param>
    /// <param name="loggerFactory">The logger factory a rejection is recorded through.</param>
    /// <returns>200 carrying the plaintext in the submitted form, or a problem response.</returns>
    /// <remarks>
    /// Projects the four <c>RSADecrypt</c> overloads at
    /// [ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L66-L69], whose legacy signature takes an RSA
    /// PRIVATE KEY as an ordinary string in-parameter. That is the position the reference stands in,
    /// and it is the clearest single reason the indirection exists.
    /// </remarks>
    internal static Results<Ok<PayloadResponse>, ProblemHttpResult> RsaDecrypt(
        RsaCipherRequest request,
        ClaimsPrincipal user,
        [FromServices] RsaProvider rsa,
        [FromServices] EncodingProvider encodings,
        [FromServices] CryptoReferenceResolver references,
        [FromServices] ILoggerFactory loggerFactory) =>
        RsaCipherOperation(
            request,
            user,
            rsa,
            encodings,
            references,
            loggerFactory,
            encrypting: false,
            RsaDecryptOperation);

    /// <summary>
    /// The one body both RSA cipher directions run through.
    /// </summary>
    /// <param name="request">The request, whose shape is identical in both directions.</param>
    /// <param name="rsa">The RSA provider.</param>
    /// <param name="encodings">The encoding provider.</param>
    /// <param name="references">The reference resolver.</param>
    /// <param name="loggerFactory">The logger factory.</param>
    /// <param name="encrypting">Which direction to invoke; it selects the provider family and nothing else.</param>
    /// <param name="operation">The operation identifier, recorded in the completion log record.</param>
    /// <returns>200 carrying the result in the submitted form, or a problem response.</returns>
    /// <exception cref="ArgumentNullException">A collaborator or the request is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// One schema serves both directions because the legacy declarations are symmetrical: encrypt
    /// differs from decrypt only in taking a public key where the other takes a private one, and both
    /// of those become the same reference here.
    /// </para>
    /// <para>
    /// THE CONFIGURED MATERIAL IS CLASSIFIED BEFORE THE PAYLOAD IS TOUCHED, so that a deployment fault
    /// and a caller fault are separated by a DECISION rather than by which exception the platform
    /// happened to raise. <see cref="RequireConfiguredRsaKey"/> answers 500 for material that is not an
    /// RSA key and for a public key where the decrypt direction needs the private half; only what
    /// survives that screen is attributed to the caller. What the screen cannot decide - a key of the
    /// right kind that is simply the wrong key - is recorded at the arm that answers it.
    /// </para>
    /// </remarks>
    private static Results<Ok<PayloadResponse>, ProblemHttpResult> RsaCipherOperation(
        RsaCipherRequest request,
        ClaimsPrincipal user,
        RsaProvider rsa,
        EncodingProvider encodings,
        CryptoReferenceResolver references,
        ILoggerFactory loggerFactory,
        bool encrypting,
        string operation)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(rsa);
        ArgumentNullException.ThrowIfNull(encodings);
        ArgumentNullException.ThrowIfNull(references);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        if (request.PayloadForm is not PayloadForm form)
        {
            return MissingPayloadForm(loggerFactory);
        }

        if (request.Data is null)
        {
            return MissingMember(nameof(RsaCipherRequest.Data), loggerFactory);
        }

        // A padding value outside the published pair is refused here, and that is where a request for
        // NO PADDING lands: enums.sru:L949-L950 declares exactly two constants and no third, so there
        // is no value a caller could send for it and nothing to accept. E_NO_SUPPORT is the code and
        // 400 is the status the shared map gives it - deliberately NOT the not-implemented status,
        // which belongs to the ingress service's deferred-capability routes and is never produced here.
        if (request.Padding is long declaredPadding && !LegacyDefaults.IsSupportedRsaPadding(declaredPadding))
        {
            return Reject(RetCode.E_NO_SUPPORT, UnsupportedRsaPaddingDetail, loggerFactory);
        }

        long effectivePadding = request.Padding ?? LegacyDefaults.RSA_PADDING_DEFAULT;

        ProblemHttpResult? rejection = references.TryResolveReference(
            request.KeyRef,
            OwnerOf(user),
            loggerFactory,
            out string keyMaterial);

        if (rejection is not null)
        {
            return rejection;
        }

        // The decrypt direction needs the private half; the encrypt direction is satisfied by either.
        // Screened HERE, before the payload is decoded, so that a configured public key on a decrypt is
        // the server fault it is rather than the unprocessable-payload verdict the platform's own
        // failure would otherwise have produced.
        rejection = RequireConfiguredRsaKey(
            rsa,
            keyMaterial,
            privateHalfRequired: !encrypting,
            loggerFactory);

        if (rejection is not null)
        {
            return rejection;
        }

        try
        {
            if (form == PayloadForm.STRING)
            {
                string textResult = encrypting
                    ? request.Padding is null
                        ? rsa.RSAEncrypt(request.Data, keyMaterial)
                        : rsa.RSAEncrypt(request.Data, keyMaterial, request.Padding.Value)
                    : request.Padding is null
                        ? rsa.RSADecrypt(request.Data, keyMaterial)
                        : rsa.RSADecrypt(request.Data, keyMaterial, request.Padding.Value);

                LogCompleted(loggerFactory, operation, algorithm: null, mode: null, effectivePadding);

                return TypedResults.Ok(new PayloadResponse(form, textResult));
            }

            rejection = TryDecodeTransportBytes(request.Data, encodings, loggerFactory, out byte[] payload);

            if (rejection is not null)
            {
                return rejection;
            }

            byte[] binaryResult = encrypting
                ? request.Padding is null
                    ? rsa.RSAEncrypt(payload, keyMaterial)
                    : rsa.RSAEncrypt(payload, keyMaterial, request.Padding.Value)
                : request.Padding is null
                    ? rsa.RSADecrypt(payload, keyMaterial)
                    : rsa.RSADecrypt(payload, keyMaterial, request.Padding.Value);

            LogCompleted(loggerFactory, operation, algorithm: null, mode: null, effectivePadding);

            return TypedResults.Ok(new PayloadResponse(form, encodings.Base64Encode(binaryResult)));
        }
        catch (ArgumentException)
        {
            // The configured material is not an importable RSA key. A deployment fault the caller can
            // neither see nor fix, so it is a server fault - and nothing about the material is
            // reported, not its value, not a substring and not its length.
            //
            // RETAINED THOUGH THE SCREEN ABOVE NOW REACHES THIS FIRST, deliberately. It is the arm that
            // holds if the screen is ever removed or the provider's accepted set ever diverges from the
            // screen's, and an unreachable-looking arm that answers 500 is preferable to a reachable one
            // that would answer 400 by falling through to the payload verdict below.
            return Reject(RetCode.E_INTERNAL_ERROR, UnusableKeyMaterialDetail, loggerFactory);
        }
        catch (CryptographicException)
        {
            // THE RESIDUAL AMBIGUITY, AND IT IS IRREDUCIBLE HERE. The screen above has already ruled out
            // material that is not a key and material of the wrong half, so what remains is either the
            // caller's payload or a configured key that is simply the WRONG key - and those two produce
            // the identical platform failure, because this surface has no authenticated encryption and
            // therefore no integrity tag to tell them apart. That absence is a preserved legacy weakness
            // [n_crypto.sru:L62-L69 declare no tag, no salt and no derivation], so the ambiguity cannot
            // be resolved without changing behaviour. It is attributed to the payload, which is the
            // arm the published contract declares for this operation.
            return Reject(RetCode.E_INVALID_DATA, UnprocessablePayloadDetail, loggerFactory);
        }
        catch (FormatException)
        {
            return Reject(RetCode.E_INVALID_DATA, UnprocessablePayloadDetail, loggerFactory);
        }
    }

    /// <summary>
    /// Produces an RSA signature over a payload, with a private key held behind a reference.
    /// </summary>
    /// <param name="request">The signature request.</param>
    /// <param name="rsa">The RSA provider.</param>
    /// <param name="encodings">The encoding provider, used only for the JSON transport encoding.</param>
    /// <param name="references">The reference resolver.</param>
    /// <param name="loggerFactory">The logger factory a rejection is recorded through.</param>
    /// <returns>200 carrying the signature in the submitted form, or a problem response.</returns>
    /// <exception cref="ArgumentNullException">A collaborator or the request is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// Projects both <c>RSASign</c> overloads at
    /// [ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L70-L71], one of the two primitives contract C-01
    /// is built on. THIS IS NOT HOW A SERVICE TOKEN IS MINTED - the token endpoint mints tokens and
    /// accepts no caller choice of key or hash; this is the general-purpose signature primitive the
    /// legacy surface published, exposed for callers that used it directly.
    /// </para>
    /// <para>
    /// The signature hash set is the keyed one: MD5 remains selectable, which is a preserved weakness
    /// the oracle's own comment at enums.sru:L927 makes unavoidable, while the checksum arm is refused
    /// because no RSA-over-CRC32 structure exists to name.
    /// </para>
    /// </remarks>
    internal static Results<Ok<PayloadResponse>, ProblemHttpResult> RsaSign(
        RsaSignRequest request,
        ClaimsPrincipal user,
        [FromServices] RsaProvider rsa,
        [FromServices] EncodingProvider encodings,
        [FromServices] CryptoReferenceResolver references,
        [FromServices] ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(rsa);
        ArgumentNullException.ThrowIfNull(encodings);
        ArgumentNullException.ThrowIfNull(references);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        if (request.PayloadForm is not PayloadForm form)
        {
            return MissingPayloadForm(loggerFactory);
        }

        if (request.Data is null)
        {
            return MissingMember(nameof(RsaSignRequest.Data), loggerFactory);
        }

        ProblemHttpResult? rejection =
            ValidateKeyedHashType(request.HashType, loggerFactory, out long hashType);

        if (rejection is not null)
        {
            return rejection;
        }

        rejection = references.TryResolveReference(
            request.KeyRef,
            OwnerOf(user),
            loggerFactory,
            out string keyMaterial);

        if (rejection is not null)
        {
            return rejection;
        }

        // Signing needs the private half without exception, so a configured public key here is a
        // deployment fault on every request this operation will ever receive - which makes screening it
        // before the payload the difference between one clear server fault and a permanent stream of
        // reports blaming callers' payloads.
        rejection = RequireConfiguredRsaKey(
            rsa,
            keyMaterial,
            privateHalfRequired: true,
            loggerFactory);

        if (rejection is not null)
        {
            return rejection;
        }

        try
        {
            if (form == PayloadForm.STRING)
            {
                string textSignature = rsa.RSASign(request.Data, keyMaterial, hashType);

                LogCompleted(loggerFactory, RsaSignOperation, hashType, mode: null, padding: null);

                return TypedResults.Ok(new PayloadResponse(form, textSignature));
            }

            rejection = TryDecodeTransportBytes(request.Data, encodings, loggerFactory, out byte[] payload);

            if (rejection is not null)
            {
                return rejection;
            }

            byte[] binarySignature = rsa.RSASign(payload, keyMaterial, hashType);

            LogCompleted(loggerFactory, RsaSignOperation, hashType, mode: null, padding: null);

            return TypedResults.Ok(new PayloadResponse(form, encodings.Base64Encode(binarySignature)));
        }
        catch (ArgumentException)
        {
            return Reject(RetCode.E_INTERNAL_ERROR, UnusableKeyMaterialDetail, loggerFactory);
        }
        catch (CryptographicException)
        {
            return Reject(RetCode.E_INVALID_DATA, UnprocessablePayloadDetail, loggerFactory);
        }
    }

    /// <summary>
    /// Verifies an RSA signature over a payload, with a public key held behind a reference.
    /// </summary>
    /// <param name="request">The verification request.</param>
    /// <param name="rsa">The RSA provider.</param>
    /// <param name="encodings">The encoding provider, used only for the JSON transport encoding.</param>
    /// <param name="references">The reference resolver.</param>
    /// <param name="loggerFactory">The logger factory a rejection is recorded through.</param>
    /// <returns>200 carrying the verdict, or a problem response.</returns>
    /// <exception cref="ArgumentNullException">A collaborator or the request is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// Projects both <c>VerifyRSASign</c> overloads at
    /// [ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L72-L73]. ONE payload form governs BOTH the payload
    /// and the signature, because the legacy correlates them and declares no mixed overload.
    /// </para>
    /// <para>
    /// A FALSE VERDICT IS A 200. A signature that does not verify is a successful execution with a
    /// negative result, and the provider deliberately collapses a malformed signature into the same
    /// false rather than raising, which is what keeps "wrong" and "malformed" indistinguishable to a
    /// caller - exactly as the legacy boolean does. Answering 4xx for either would make a legitimate
    /// negative verdict indistinguishable from a transport or authorization failure.
    /// </para>
    /// </remarks>
    internal static Results<Ok<RsaVerifyResponse>, ProblemHttpResult> RsaVerify(
        RsaVerifyRequest request,
        ClaimsPrincipal user,
        [FromServices] RsaProvider rsa,
        [FromServices] EncodingProvider encodings,
        [FromServices] CryptoReferenceResolver references,
        [FromServices] ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(rsa);
        ArgumentNullException.ThrowIfNull(encodings);
        ArgumentNullException.ThrowIfNull(references);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        if (request.PayloadForm is not PayloadForm form)
        {
            return MissingPayloadForm(loggerFactory);
        }

        if (request.Data is null)
        {
            return MissingMember(nameof(RsaVerifyRequest.Data), loggerFactory);
        }

        if (request.Signature is null)
        {
            return MissingMember(nameof(RsaVerifyRequest.Signature), loggerFactory);
        }

        ProblemHttpResult? rejection =
            ValidateKeyedHashType(request.HashType, loggerFactory, out long hashType);

        if (rejection is not null)
        {
            return rejection;
        }

        rejection = references.TryResolveReference(
            request.KeyRef,
            OwnerOf(user),
            loggerFactory,
            out string keyMaterial);

        if (rejection is not null)
        {
            return rejection;
        }

        // Verification is satisfied by either half, so only unusable material is a fault here. Screened
        // before the payload for a reason specific to THIS operation: it collapses a malformed signature
        // into a FALSE VERDICT rather than raising, so material that is not a key at all would otherwise
        // have to be distinguished from a legitimate negative answer after the fact.
        rejection = RequireConfiguredRsaKey(
            rsa,
            keyMaterial,
            privateHalfRequired: false,
            loggerFactory);

        if (rejection is not null)
        {
            return rejection;
        }

        bool valid;

        try
        {
            if (form == PayloadForm.STRING)
            {
                valid = rsa.VerifyRSASign(request.Data, request.Signature, keyMaterial, hashType);
            }
            else
            {
                rejection = TryDecodeTransportBytes(request.Data, encodings, loggerFactory, out byte[] payload);

                if (rejection is not null)
                {
                    return rejection;
                }

                rejection = TryDecodeTransportBytes(
                    request.Signature,
                    encodings,
                    loggerFactory,
                    out byte[] signature);

                if (rejection is not null)
                {
                    return rejection;
                }

                valid = rsa.VerifyRSASign(payload, signature, keyMaterial, hashType);
            }
        }
        catch (ArgumentException)
        {
            return Reject(RetCode.E_INTERNAL_ERROR, UnusableKeyMaterialDetail, loggerFactory);
        }

        LogCompleted(loggerFactory, RsaVerifyOperation, hashType, mode: null, padding: null);

        // The verdict itself is deliberately NOT logged. It is a statement about the caller's payload,
        // and the completion record carries classifiers only.
        return TypedResults.Ok(new RsaVerifyResponse(valid));
    }

    /// <summary>
    /// Generates an RSA key pair, returning the public half and retaining the private half.
    /// </summary>
    /// <param name="request">The generation request.</param>
    /// <param name="user">The caller identity presented with the credential.</param>
    /// <param name="rsa">The RSA provider.</param>
    /// <param name="random">The random provider, used to mint the opaque reference.</param>
    /// <param name="references">The reference resolver, which owns the retained-key store.</param>
    /// <param name="loggerFactory">The logger factory a rejection is recorded through.</param>
    /// <returns>200 carrying the public key and the new reference, or a problem response.</returns>
    /// <exception cref="ArgumentNullException">A collaborator or the request is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// Projects both <c>GenRSAKey</c> overloads at
    /// [ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L19-L20]: OMITTING <c>pemFormat</c> CALLS THE
    /// THREE-ARGUMENT OVERLOAD, which does not take the switch at all, and supplying it calls the
    /// four-argument one. Both are therefore genuinely reached rather than one being synthesised from
    /// the other.
    /// </para>
    /// <para>
    /// THE HIGHEST-CONSEQUENCE OUTBOUND DECISION IN THIS FILE, and it is the authored contract's:
    /// the response carries the PUBLIC key and an opaque reference to the RETAINED private key. The
    /// private key is never returned, never logged, never echoed in an error body and never placed in
    /// a published example. A caller that needs a signature passes the reference to the signing
    /// operation; it does not obtain the key with which to produce one.
    /// </para>
    /// <para>
    /// A 1024-BIT REQUEST IS ACCEPTED. No floor is imposed here or anywhere else, because the legacy
    /// imposes none and rejecting a size it accepted would be the silent correction this port forbids.
    /// The only size screening is what the platform itself will generate, asked of the platform.
    /// </para>
    /// <para>
    /// <b>CAPACITY IS RESERVED BEFORE THE KEY IS GENERATED, AND THAT ORDER IS THE CONTRACT.</b> RSA
    /// generation is the expensive half of this operation, so checking capacity afterwards let an
    /// authenticated caller drive unbounded generation work and have every pair discarded - a full store
    /// became an amplifier rather than a limit. The slot is taken first, atomically, and abandoned if
    /// generation refuses the size, so no work is ever done for a request that could not be retained.
    /// </para>
    /// <para>
    /// ONE LIMITATION RECORDED RATHER THAN HIDDEN: the provider returns key text as STRINGS, and a
    /// string cannot be wiped. The generated private key therefore lives until the retained-key store
    /// releases it and the garbage collector reclaims it; nothing here can zero it, and pretending
    /// otherwise would be worse than saying so. What CAN be bounded is how long it is held and how much
    /// of it one caller may hold, and both are - see <see cref="CryptoReferenceResolver"/>.
    /// </para>
    /// </remarks>
    internal static Results<Ok<GenRsaKeyResponse>, ProblemHttpResult> GenerateRsaKey(
        GenRsaKeyRequest request,
        ClaimsPrincipal user,
        [FromServices] RsaProvider rsa,
        [FromServices] RandomProvider random,
        [FromServices] CryptoReferenceResolver references,
        [FromServices] ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(rsa);
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(references);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        if (request.Bits is not long declaredBits)
        {
            return MissingMember(nameof(GenRsaKeyRequest.Bits), loggerFactory);
        }

        // The legacy argument is a 16-bit unsigned value [n_crypto.sru:L19-L20] and the published
        // convenience constants are declared at the same width [enums.sru:L965-L967], so the domain is
        // exactly that of the declared parameter. This is NOT a cryptographic bound.
        if (declaredBits is < 0 or > ushort.MaxValue)
        {
            return Reject(RetCode.E_INVALID_ARGUMENT, KeySizeOutOfDomainDetail, loggerFactory);
        }

        // ORDERED AFTER THE DOMAIN CHECK, DELIBERATELY. The domain check is a parity statement about the
        // legacy argument's declared width and keeps its own meaning and its own return code; this is a
        // work bound created by the boundary. Putting it first would re-attribute every out-of-domain
        // request to a bound the legacy never had. Ordered BEFORE the store reservation for the reason
        // that reservation is ordered before generation: a refusal must cost nothing.
        if (declaredBits > MaximumGeneratedModulusBits)
        {
            return Reject(RetCode.E_OUT_OF_RANGE, KeySizeOverBudgetDetail, loggerFactory);
        }

        // RESERVED BEFORE GENERATION. A refusal here has cost nothing: no key exists yet.
        if (!references.TryReserveGeneratedKeySlot(
            OwnerOf(user),
            random,
            out GeneratedKeyReservation reservation))
        {
            // Either the global cap or this caller's own quota is met. Both answer identically, because
            // distinguishing them would report how much of the store other callers hold.
            return Reject(RetCode.E_INTERNAL_ERROR, GeneratedKeyStoreFullDetail, loggerFactory);
        }

        string privateKey = string.Empty;
        string publicKey = string.Empty;
        string keyRef;

        try
        {
            bool generated = request.PemFormat is bool pemFormat
                ? rsa.GenRSAKey((ushort)declaredBits, ref privateKey, ref publicKey, pemFormat)
                : rsa.GenRSAKey((ushort)declaredBits, ref privateKey, ref publicKey);

            if (!generated)
            {
                // The legacy reports an unusable size through its boolean return rather than by raising,
                // and leaves both out-parameters untouched, so there is no partial key to discard here -
                // but the RESERVED SLOT must be given back, or a caller could fill the store with
                // refused sizes.
                references.AbandonGeneratedKeySlot(reservation);

                return Reject(RetCode.E_INVALID_ARGUMENT, KeySizeUnavailableDetail, loggerFactory);
            }

            keyRef = references.CommitGeneratedKey(reservation, privateKey);
        }
        catch
        {
            // Anything unexpected between reserving and committing releases the slot before it
            // propagates. Without this the slot would leak on a fault, and the store would fill with
            // reservations for keys that never existed.
            references.AbandonGeneratedKeySlot(reservation);

            throw;
        }

        // The record carries the modulus length only. Neither key half, nor the minted reference, is
        // recorded: the reference is a credential-like handle and the completion record is a
        // classifier record.
        LogCompleted(loggerFactory, GenerateRsaKeyOperation, declaredBits, mode: null, padding: null);

        return TypedResults.Ok(
            new GenRsaKeyResponse(publicKey, keyRef, declaredBits) { PemFormat = request.PemFormat });
    }

    /// <summary>
    /// Releases a retained generated private key at its owner's request.
    /// </summary>
    /// <param name="keyRef">The reference the caller was given by the generation operation.</param>
    /// <param name="user">The authenticated caller, whose subject claim owns the key.</param>
    /// <param name="references">The reference resolver, which owns the retained-key store.</param>
    /// <param name="loggerFactory">The logger factory a rejection is recorded through.</param>
    /// <returns>204 when the key was released, or 404 when this caller holds no such key.</returns>
    /// <exception cref="ArgumentNullException">A collaborator is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// PART OF THE AUTHORED NARROWING RATHER THAN A LEGACY OPERATION. The legacy has no retained-key
    /// store and therefore nothing to release: it hands the private half straight back through a
    /// <c>ref</c> parameter [<c>ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L19-L20</c>]. Retaining the
    /// key instead is what makes the generation response safe across a network boundary, and a retained
    /// thing needs a way to be given back - otherwise the per-caller quota is only escapable by waiting
    /// out the expiry.
    /// </para>
    /// <para>
    /// OWNERSHIP IS ENFORCED, AND ITS FAILURE IS INDISTINGUISHABLE FROM ABSENCE. A caller may release
    /// only keys charged to its own subject; a reference belonging to another caller answers exactly as
    /// an unknown one does. See <see cref="CryptoReferenceResolver.TryReleaseGeneratedKey"/>.
    /// </para>
    /// <para>
    /// NEITHER THE REFERENCE NOR ANY KEY MATERIAL IS LOGGED. The completion record carries the operation
    /// name and nothing else, which is the same discipline every sibling handler follows.
    /// </para>
    /// </remarks>
    internal static Results<NoContent, ProblemHttpResult> ReleaseRsaKey(
        string keyRef,
        ClaimsPrincipal user,
        [FromServices] CryptoReferenceResolver references,
        [FromServices] ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(references);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        if (!references.TryReleaseGeneratedKey(keyRef, OwnerOf(user)))
        {
            return Reject(RetCode.E_OBJECT_NOT_FOUND, GeneratedKeyNotHeldDetail, loggerFactory);
        }

        // No classifier at all: a release names no algorithm, mode or padding, and the reference is
        // never recorded.
        LogCompleted(loggerFactory, ReleaseRsaKeyOperation, algorithm: null, mode: null, padding: null);

        return TypedResults.NoContent();
    }

    /// <summary>
    /// Reads the caller identity a retained key is charged to.
    /// </summary>
    /// <param name="user">The authenticated caller.</param>
    /// <returns>The subject claim, or the empty string when the token carries none.</returns>
    /// <remarks>
    /// <para>
    /// THE SUBJECT CLAIM IS THE ONLY CALLER IDENTITY THIS SURFACE HAS, and it is the same claim
    /// <c>Tokens/TokenIssuer.cs</c> stamps. It is read by its PROTOCOL NAME rather than through the
    /// framework's mapped alias, because this service configures <c>MapInboundClaims</c> false - so the
    /// claim arrives spelled exactly as the token spells it and the mapped alias does not exist.
    /// </para>
    /// <para>
    /// AN EMPTY ANSWER IS NOT AN EXEMPTION. The resolver charges it to one fixed shared bucket, so a
    /// token carrying no subject cannot escape the quota by omitting a claim.
    /// </para>
    /// </remarks>
    private static string OwnerOf(ClaimsPrincipal user) =>
        user.FindFirst(SubjectClaimName)?.Value ?? string.Empty;

    // ==============================================================================================
    //  HANDLERS - THE RANDOM FAMILY, WHICH IS ALSO THE DETERMINISM SEAM
    //  ----------------------------------------------------------------------------------------------
    //  All three draw ONLY through RandomProvider, which holds the injected entropy abstraction. No
    //  static randomness API is called anywhere in this file, which is what lets a characterization
    //  test substitute a deterministic double for the whole host and get byte-reproducible answers.
    // ==============================================================================================

    /// <summary>
    /// Generates the requested number of random bytes.
    /// </summary>
    /// <param name="request">The random-bytes request.</param>
    /// <param name="random">The random provider, which holds the entropy seam.</param>
    /// <param name="encodings">The encoding provider, used only for the JSON transport encoding.</param>
    /// <param name="loggerFactory">The logger factory a rejection is recorded through.</param>
    /// <returns>200 carrying the bytes as base64, or a problem response.</returns>
    /// <exception cref="ArgumentNullException">A collaborator or the request is <see langword="null"/>.</exception>
    /// <remarks>
    /// Projects <c>blob GenRandomBlob(readonly ulong size)</c>
    /// [ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L14], the single overload of this operation, whose
    /// return type is an unconditional blob - so there is no form to select. The result is the RAW
    /// bytes as drawn: the legacy applies no encoding of its own here, because the caller owns that
    /// choice, and the base64 in the response is JSON transport only.
    /// </remarks>
    internal static Results<Ok<BlobResponse>, ProblemHttpResult> GenerateRandomBlob(
        RandomBlobRequest request,
        [FromServices] RandomProvider random,
        [FromServices] EncodingProvider encodings,
        [FromServices] ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(encodings);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        ProblemHttpResult? rejection = ValidateRequestedSize(
            request.Size,
            nameof(RandomBlobRequest.Size),
            loggerFactory,
            out uint size);

        if (rejection is not null)
        {
            return rejection;
        }

        byte[] blob = random.GenRandomBlob(size);

        LogCompleted(loggerFactory, GenerateRandomBlobOperation, algorithm: null, mode: null, padding: null);

        return TypedResults.Ok(new BlobResponse(encodings.Base64Encode(blob)));
    }

    /// <summary>
    /// Generates a random string of the requested length from the selected character classes.
    /// </summary>
    /// <param name="request">The random-string request.</param>
    /// <param name="random">The random provider, which holds the entropy seam.</param>
    /// <param name="loggerFactory">The logger factory a rejection is recorded through.</param>
    /// <returns>200 carrying the generated string, or a problem response.</returns>
    /// <exception cref="ArgumentNullException">A collaborator or the request is <see langword="null"/>.</exception>
    /// <remarks>
    /// Projects both overloads at [ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L15-L16]: OMITTING the
    /// flags calls the one-argument member, which applies the preserved default of digits and letters
    /// with the symbol class excluded, and supplying them calls the two-argument one. The default is
    /// not widened to include symbols, and unrecognised bits inside the declared domain are accepted
    /// rather than refused, because the legacy defines no behaviour for them.
    /// </remarks>
    internal static Results<Ok<RndStringResponse>, ProblemHttpResult> GenerateRandomString(
        RndStringRequest request,
        [FromServices] RandomProvider random,
        [FromServices] ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        ProblemHttpResult? rejection = ValidateRequestedSize(
            request.Size,
            nameof(RndStringRequest.Size),
            loggerFactory,
            out uint size);

        if (rejection is not null)
        {
            return rejection;
        }

        rejection = ValidateFlags(request.Flags, loggerFactory, out uint flags);

        if (rejection is not null)
        {
            return rejection;
        }

        string value = request.Flags is null
            ? random.GenRandomString(size)
            : random.GenRandomString(size, flags);

        LogCompleted(loggerFactory, GenerateRandomStringOperation, algorithm: null, mode: null, padding: null);

        return TypedResults.Ok(new RndStringResponse(value));
    }

    /// <summary>
    /// Generates a globally unique identifier in the requested textual form.
    /// </summary>
    /// <param name="request">
    /// The generation request, or <see langword="null"/> when the caller sent no body at all - which
    /// is equivalent to omitting the flags and is how the no-argument legacy overload is reached.
    /// </param>
    /// <param name="random">The random provider, which holds the entropy seam.</param>
    /// <param name="loggerFactory">The logger factory a rejection is recorded through.</param>
    /// <returns>200 carrying the identifier, or a problem response.</returns>
    /// <exception cref="ArgumentNullException">A collaborator is <see langword="null"/>.</exception>
    /// <remarks>
    /// Projects both overloads at [ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L17-L18]. The request
    /// body is OPTIONAL because the authored contract declares it so, which is what makes the
    /// no-argument overload reachable without inventing a member. The flags govern FORMATTING only and
    /// the preserved default carries both the braces and the separators.
    /// </remarks>
    internal static Results<Ok<GuidResponse>, ProblemHttpResult> GenerateGuid(
        GuidRequest? request,
        [FromServices] RandomProvider random,
        [FromServices] ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        long? declaredFlags = request?.Flags;

        ProblemHttpResult? rejection = ValidateFlags(declaredFlags, loggerFactory, out uint flags);

        if (rejection is not null)
        {
            return rejection;
        }

        string value = declaredFlags is null
            ? random.GenGuid()
            : random.GenGuid(flags);

        LogCompleted(loggerFactory, GenerateGuidOperation, algorithm: null, mode: null, padding: null);

        return TypedResults.Ok(new GuidResponse(value));
    }

    // ==============================================================================================
    //  HANDLERS - THE ENCODING FAMILY
    // ==============================================================================================

    /// <summary>
    /// Decodes an encoded string into bytes, under the caller's choice of Base64 or hexadecimal.
    /// </summary>
    /// <param name="request">The decode request.</param>
    /// <param name="encodings">The encoding provider.</param>
    /// <param name="loggerFactory">The logger factory a rejection is recorded through.</param>
    /// <returns>200 carrying the decoded bytes as base64, or a problem response.</returns>
    /// <exception cref="ArgumentNullException">A collaborator or the request is <see langword="null"/>.</exception>
    /// <remarks>
    /// Projects <c>blob StringToBlob(readonly string data, readonly long encoding)</c>
    /// [ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L11]. A REQUEST TO DECODE A HEX STRING STILL
    /// RETURNS ITS BYTES AS A BASE64 MEMBER, because the legacy encoding argument and the JSON
    /// transport encoding answer different questions and collapsing them would be a behavioural
    /// change.
    /// </remarks>
    internal static Results<Ok<BlobResponse>, ProblemHttpResult> StringToBlob(
        StringToBlobRequest request,
        [FromServices] EncodingProvider encodings,
        [FromServices] ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(encodings);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        if (request.Data is null)
        {
            return MissingMember(nameof(StringToBlobRequest.Data), loggerFactory);
        }

        ProblemHttpResult? rejection = ValidateEncoding(request.Encoding, loggerFactory, out long encoding);

        if (rejection is not null)
        {
            return rejection;
        }

        byte[] decoded;

        try
        {
            decoded = encodings.StringToBlob(request.Data, encoding);
        }
        catch (FormatException)
        {
            return Reject(RetCode.E_INVALID_DATA, MalformedEncodedTextDetail, loggerFactory);
        }

        LogCompleted(loggerFactory, StringToBlobOperation, encoding, mode: null, padding: null);

        return TypedResults.Ok(new BlobResponse(encodings.Base64Encode(decoded)));
    }

    /// <summary>
    /// Encodes bytes into a string, under the caller's choice of Base64 or hexadecimal.
    /// </summary>
    /// <param name="request">The encode request.</param>
    /// <param name="encodings">The encoding provider.</param>
    /// <param name="loggerFactory">The logger factory a rejection is recorded through.</param>
    /// <returns>200 carrying the encoded string, or a problem response.</returns>
    /// <exception cref="ArgumentNullException">A collaborator or the request is <see langword="null"/>.</exception>
    /// <remarks>
    /// Projects <c>string BlobToString(readonly blob data, readonly long encoding)</c>
    /// [ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L12], the exact inverse of the decode operation. A
    /// caller asking for hexadecimal SUBMITS BASE64 AND RECEIVES HEXADECIMAL, which is correct rather
    /// than contradictory: the inbound base64 is JSON transport and the argument selects the outbound
    /// form.
    /// </remarks>
    internal static Results<Ok<EncodedTextResponse>, ProblemHttpResult> BlobToString(
        BlobToStringRequest request,
        [FromServices] EncodingProvider encodings,
        [FromServices] ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(encodings);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        if (request.Data is null)
        {
            return MissingMember(nameof(BlobToStringRequest.Data), loggerFactory);
        }

        ProblemHttpResult? rejection = ValidateEncoding(request.Encoding, loggerFactory, out long encoding);

        if (rejection is not null)
        {
            return rejection;
        }

        rejection = TryDecodeTransportBytes(request.Data, encodings, loggerFactory, out byte[] payload);

        if (rejection is not null)
        {
            return rejection;
        }

        string value = encodings.BlobToString(payload, encoding);

        LogCompleted(loggerFactory, BlobToStringOperation, encoding, mode: null, padding: null);

        return TypedResults.Ok(new EncodedTextResponse(value));
    }

    /// <summary>
    /// Reverses the byte order of a payload and reports the legacy boolean outcome alongside it.
    /// </summary>
    /// <param name="request">The reversal request.</param>
    /// <param name="encodings">The encoding provider, which owns the reversal.</param>
    /// <param name="loggerFactory">The logger factory a rejection is recorded through.</param>
    /// <returns>200 carrying the reversed bytes and the legacy outcome, or a problem response.</returns>
    /// <exception cref="ArgumentNullException">A collaborator or the request is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// Projects <c>boolean BlobReverse(ref blob data)</c>
    /// [ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L13], the only member of the legacy surface that
    /// mutates its argument. In-place mutation has no wire representation, so the response carries BOTH
    /// halves of the outcome: the reversed bytes, which the legacy left in the caller's own variable,
    /// and the boolean, carried unchanged because it is the operation's ACTUAL return value.
    /// </para>
    /// <para>
    /// THE BUFFER THIS HANDLER REVERSES IS FRESHLY DECODED FROM THE REQUEST, so the mutation cannot
    /// touch anything shared: it is not a cached buffer, it does not alias resolved key material, and
    /// no other request can observe it.
    /// </para>
    /// </remarks>
    internal static Results<Ok<BlobReverseResponse>, ProblemHttpResult> ReverseBlob(
        BlobReverseRequest request,
        [FromServices] EncodingProvider encodings,
        [FromServices] ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(encodings);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        if (request.Data is null)
        {
            return MissingMember(nameof(BlobReverseRequest.Data), loggerFactory);
        }

        ProblemHttpResult? rejection =
            TryDecodeTransportBytes(request.Data, encodings, loggerFactory, out byte[] payload);

        if (rejection is not null)
        {
            return rejection;
        }

        bool succeeded = encodings.BlobReverse(ref payload);

        LogCompleted(loggerFactory, ReverseBlobOperation, algorithm: null, mode: null, padding: null);

        return TypedResults.Ok(new BlobReverseResponse(encodings.Base64Encode(payload), succeeded));
    }

    // ==============================================================================================
    //  VALIDATION HELPERS
    //  ----------------------------------------------------------------------------------------------
    //  EVERY ONE SCREENS THROUGH LegacyDefaults RATHER THAN THROUGH A LOCAL ALLOW-LIST, so the sets
    //  accepted at this boundary are the same sets the providers accept, by construction rather than by
    //  review. Each is internal and named, so a test can drive it as a theory without booting a host.
    // ==============================================================================================

    /// <summary>
    /// Screens a hash-type selector against the FULL published set, which the unkeyed digest
    /// operations use.
    /// </summary>
    /// <param name="declared">The selector as the request carried it, or <see langword="null"/>.</param>
    /// <param name="loggerFactory">The logger factory a rejection is recorded through.</param>
    /// <param name="hashType">The accepted selector, or zero when this method returns a rejection.</param>
    /// <returns><see langword="null"/> when the selector is acceptable; otherwise the rejection.</returns>
    /// <remarks>
    /// The full set is MD5 (0) through CRC32 (5) [enums.sru:L928-L933], and CRC32 belongs here: an
    /// unkeyed checksum is a legitimate thing to compute and the oracle's own demonstration computes
    /// one over a file.
    /// </remarks>
    internal static ProblemHttpResult? ValidateDeclaredHashType(
        long? declared,
        ILoggerFactory loggerFactory,
        out long hashType)
    {
        hashType = 0;

        if (declared is not long candidate)
        {
            return MissingMember(nameof(HashRequest.HashType), loggerFactory);
        }

        if (!LegacyDefaults.IsSupportedHashType(candidate))
        {
            return Reject(RetCode.E_NO_SUPPORT, UnsupportedHashTypeDetail, loggerFactory);
        }

        hashType = candidate;

        return null;
    }

    /// <summary>
    /// Screens a hash-type selector against the KEYED set, which the keyed digest and the signature
    /// operations use.
    /// </summary>
    /// <param name="declared">The selector as the request carried it, or <see langword="null"/>.</param>
    /// <param name="loggerFactory">The logger factory a rejection is recorded through.</param>
    /// <param name="hashType">The accepted selector, or zero when this method returns a rejection.</param>
    /// <returns><see langword="null"/> when the selector is acceptable; otherwise the rejection.</returns>
    /// <remarks>
    /// <para>
    /// TWO SCREENS IN A DELIBERATE ORDER, because they mean different things. The first refuses a value
    /// OUTSIDE the oracle's published set - an argument-domain error. The second refuses CRC32, which
    /// IS published and which the oracle's own comment at enums.sru:L927 names as an argument of the
    /// signature members too; what does not exist is a construction. Both answer E_NO_SUPPORT, and the
    /// detail text distinguishes them, so a caller learns which of the two it hit.
    /// </para>
    /// <para>
    /// The refusal happens HERE, at the earliest point it can be detected, rather than being left to
    /// the provider: the published contract narrows the selector set on these four operations, so a
    /// request naming the checksum arm is refused before any key is resolved.
    /// </para>
    /// </remarks>
    internal static ProblemHttpResult? ValidateKeyedHashType(
        long? declared,
        ILoggerFactory loggerFactory,
        out long hashType)
    {
        hashType = 0;

        if (declared is not long candidate)
        {
            return MissingMember(nameof(HmacRequest.HashType), loggerFactory);
        }

        if (!LegacyDefaults.IsSupportedHashType(candidate))
        {
            return Reject(RetCode.E_NO_SUPPORT, UnsupportedHashTypeDetail, loggerFactory);
        }

        if (candidate == Enums.CRYPTO_HASH_CRC32)
        {
            return Reject(RetCode.E_NO_SUPPORT, KeyedChecksumUnsupportedDetail, loggerFactory);
        }

        hashType = candidate;

        return null;
    }

    /// <summary>
    /// Screens an encoding selector against the published pair.
    /// </summary>
    /// <param name="declared">The selector as the request carried it, or <see langword="null"/>.</param>
    /// <param name="loggerFactory">The logger factory a rejection is recorded through.</param>
    /// <param name="encoding">The accepted selector, or zero when this method returns a rejection.</param>
    /// <returns><see langword="null"/> when the selector is acceptable; otherwise the rejection.</returns>
    /// <remarks>
    /// There is deliberately NO DEFAULT: both legacy declarations take the argument
    /// [n_crypto.sru:L11-L12], so a caller always states it and an absent member is a rejection rather
    /// than a silent Base64.
    /// </remarks>
    internal static ProblemHttpResult? ValidateEncoding(
        long? declared,
        ILoggerFactory loggerFactory,
        out long encoding)
    {
        encoding = 0;

        if (declared is not long candidate)
        {
            return MissingMember(nameof(StringToBlobRequest.Encoding), loggerFactory);
        }

        if (!LegacyDefaults.IsSupportedEncoding(candidate))
        {
            return Reject(RetCode.E_NO_SUPPORT, UnsupportedEncodingDetail, loggerFactory);
        }

        encoding = candidate;

        return null;
    }

    /// <summary>
    /// Screens a requested length against the published service-level cap.
    /// </summary>
    /// <param name="declared">The length as the request carried it, or <see langword="null"/>.</param>
    /// <param name="memberName">
    /// The member's own name, always a compile-time <c>nameof</c> from this file, used only to name the
    /// absent member in the rejection. NO CALLER VALUE IS EVER PASSED HERE.
    /// </param>
    /// <param name="loggerFactory">The logger factory a rejection is recorded through.</param>
    /// <param name="size">The accepted length, or zero when this method returns a rejection.</param>
    /// <returns><see langword="null"/> when the length is acceptable; otherwise the rejection.</returns>
    /// <remarks>
    /// <para>
    /// The cap is <see cref="RandomProvider.MaximumRequestedLength"/> itself rather than a second copy
    /// of the number, so the boundary and the provider CANNOT disagree about it and the published
    /// domain in the contract document has exactly one implementation to match.
    /// </para>
    /// <para>
    /// A request above the cap is REFUSED, never truncated: returning less random material than was
    /// asked for is the one outcome a caller cannot detect, and short random material is precisely the
    /// defect that survives every test and fails in production.
    /// </para>
    /// </remarks>
    internal static ProblemHttpResult? ValidateRequestedSize(
        long? declared,
        string memberName,
        ILoggerFactory loggerFactory,
        out uint size)
    {
        size = 0;

        if (declared is not long candidate)
        {
            return MissingMember(memberName, loggerFactory);
        }

        if (candidate < 0 || candidate > RandomProvider.MaximumRequestedLength)
        {
            return Reject(RetCode.E_INVALID_ARGUMENT, SizeOutOfDomainDetail, loggerFactory);
        }

        size = (uint)candidate;

        return null;
    }

    /// <summary>
    /// Screens a flag bitmask against the domain of the legacy argument's own type.
    /// </summary>
    /// <param name="declared">The bitmask as the request carried it, or <see langword="null"/>.</param>
    /// <param name="loggerFactory">The logger factory a rejection is recorded through.</param>
    /// <param name="flags">
    /// The accepted bitmask, or zero when the member was absent or this method returns a rejection.
    /// A caller that omitted the member must use the provider's own no-flags overload rather than this
    /// value, so that the preserved default is applied by the provider and not re-spelled here.
    /// </param>
    /// <returns><see langword="null"/> when the bitmask is acceptable; otherwise the rejection.</returns>
    /// <remarks>
    /// <para>
    /// AN ABSENT MEMBER IS NOT A REJECTION: both flag arguments are optional in the legacy, and
    /// omitting one selects a documented default. Only a value outside the 32-bit unsigned domain the
    /// legacy declares [n_crypto.sru:L16, :L18] is refused.
    /// </para>
    /// <para>
    /// BITS ABOVE THOSE THE CATALOGUE NAMES ARE ACCEPTED inside that domain, deliberately. The legacy
    /// defines no behaviour for them and its provider masks them away, so refusing them here would
    /// narrow a contract the legacy leaves open.
    /// </para>
    /// </remarks>
    internal static ProblemHttpResult? ValidateFlags(
        long? declared,
        ILoggerFactory loggerFactory,
        out uint flags)
    {
        flags = 0;

        if (declared is not long candidate)
        {
            return null;
        }

        if (candidate < 0 || candidate > uint.MaxValue)
        {
            return Reject(RetCode.E_INVALID_ARGUMENT, FlagsOutOfDomainDetail, loggerFactory);
        }

        flags = (uint)candidate;

        return null;
    }

    /// <summary>
    /// Screens the CONFIGURED key material behind a resolved reference before the caller's payload is
    /// touched, so that a deployment fault is answered as one.
    /// </summary>
    /// <param name="rsa">The RSA provider, which owns the single import policy this screen consults.</param>
    /// <param name="keyMaterial">The material the reference resolved to.</param>
    /// <param name="privateHalfRequired">
    /// Whether the operation needs the private half. True for decryption and for signing; false for
    /// encryption and for signature verification, both of which the public half satisfies.
    /// </param>
    /// <param name="loggerFactory">The logger factory a rejection is recorded through.</param>
    /// <returns><see langword="null"/> when the material suits the operation; otherwise the rejection.</returns>
    /// <exception cref="ArgumentNullException">A collaborator is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// WHY IT RUNS BEFORE THE PAYLOAD AND NOT AFTER THE FAILURE. Without it, a configured public key on
    /// a decrypt or a sign reaches the platform, which raises the SAME cryptographic failure an
    /// unprocessable payload raises - so the boundary attributed a deployment defect to the caller and
    /// answered 400. The caller supplied only an opaque reference and could neither see the material nor
    /// fix it, so that status sent every such report to the wrong party. Screening first converts the
    /// one part of that ambiguity that IS decidable into the server fault it always was.
    /// </para>
    /// <para>
    /// WHAT REMAINS AMBIGUOUS IS STATED RATHER THAN PAPERED OVER. A configured key that imports, carries
    /// the right half, and is simply the WRONG KEY still fails identically to a corrupt payload, because
    /// this surface has no authenticated encryption at all - no integrity tag exists to tell the two
    /// apart, which is a preserved legacy weakness rather than an omission here. That residual case is
    /// answered as the caller's payload, and the contract says so at the operation.
    /// </para>
    /// <para>
    /// THE MATERIAL IS IMPORTED TWICE ON A SUCCESSFUL REQUEST - once here and once inside the operation
    /// - and that is accepted deliberately. The alternative is to thread a live key object through the
    /// provider's overloads, whose signatures take key TEXT because the legacy declarations they
    /// substitute do [n_crypto.sru:L62-L73]; changing them to carry an imported key would break the
    /// parity those signatures exist to preserve. Nothing about this refactor asks for a performance
    /// objective, and no repository artifact publishes one, so the trade is a fidelity choice rather
    /// than a cost decision.
    /// </para>
    /// </remarks>
    internal static ProblemHttpResult? RequireConfiguredRsaKey(
        RsaProvider rsa,
        string keyMaterial,
        bool privateHalfRequired,
        ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(rsa);
        ArgumentNullException.ThrowIfNull(keyMaterial);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        return rsa.ClassifyKey(keyMaterial) switch
        {
            RsaKeyKind.KeyPair => null,
            RsaKeyKind.PublicOnly when !privateHalfRequired => null,
            RsaKeyKind.PublicOnly =>
                Reject(RetCode.E_INTERNAL_ERROR, PublicOnlyKeyMaterialDetail, loggerFactory),
            _ => Reject(RetCode.E_INTERNAL_ERROR, UnusableKeyMaterialDetail, loggerFactory),
        };
    }

    /// <summary>
    /// Decodes a blob-shaped member from its JSON transport encoding.
    /// </summary>
    /// <param name="data">The member's text, which carries base64 of the raw bytes.</param>
    /// <param name="encodings">The encoding provider, which owns the one Base64 implementation.</param>
    /// <param name="loggerFactory">The logger factory a rejection is recorded through.</param>
    /// <param name="bytes">The decoded bytes, or an empty array when this method returns a rejection.</param>
    /// <returns><see langword="null"/> when the member decoded; otherwise the rejection.</returns>
    /// <exception cref="ArgumentNullException">A collaborator is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// The decode goes through <see cref="EncodingProvider"/> rather than through a conversion
    /// primitive of this file's own, so the repository has exactly one Base64 implementation and the
    /// transport decode cannot drift from the legacy's own encoding behaviour. AN EMPTY MEMBER IS
    /// VALID and yields an empty array - the published schema declares no minimum length and a digest
    /// or a cipher over no bytes is well defined.
    /// </para>
    /// <para>
    /// The value is NOT reported in the rejection. A malformed payload is the caller's own input; it
    /// may be ciphertext, and it has no business in a problem body or a log record.
    /// </para>
    /// </remarks>
    internal static ProblemHttpResult? TryDecodeTransportBytes(
        string data,
        EncodingProvider encodings,
        ILoggerFactory loggerFactory,
        out byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(encodings);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        bytes = [];

        if (data is null)
        {
            return MissingMember(nameof(HashRequest.Data), loggerFactory);
        }

        try
        {
            bytes = encodings.Base64Decode(data);

            return null;
        }
        catch (FormatException)
        {
            return Reject(RetCode.E_INVALID_DATA, MalformedBinaryPayloadDetail, loggerFactory);
        }
    }

    // ==============================================================================================
    //  REJECTION HELPERS
    // ==============================================================================================

    /// <summary>
    /// Builds this file's <c>application/problem+json</c> rejection.
    /// </summary>
    /// <param name="retCode">The legacy return code for the condition.</param>
    /// <param name="detail">
    /// The fixed, value-free sentence describing the condition. Every caller passes a constant from
    /// this file; no caller value is ever interpolated into one.
    /// </param>
    /// <param name="loggerFactory">The logger factory the shared factory records through.</param>
    /// <returns>The problem response, whose status the shared per-code map chooses.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="loggerFactory"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// THE ONE FUNNEL, so that this whole surface answers ONE error shape and the status of every
    /// condition is chosen by the shared explicit per-code map rather than route by route. No second
    /// problem schema is declared here and the shared factory is not copied.
    /// </para>
    /// <para>
    /// THE SEVERITY IS DELIBERATELY <see cref="ProblemSeverity.None"/>, and that is evidence rather
    /// than indifference. The severity member exists to preserve the icon of a legacy
    /// <c>MessageBox</c> call, and the legacy cryptographic class is declared native
    /// [ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L8] - it displays NO dialog at any of its 65
    /// declarations, so there is no severity to preserve. Asserting one would fabricate information
    /// about the oracle, and the zero-valued member is precisely the one that claims nothing.
    /// </para>
    /// <para>
    /// NO LOCALIZATION CATEGORY IS PASSED either, for the same reason: a category is the argument of a
    /// legacy translation call, and this surface makes none.
    /// </para>
    /// </remarks>
    internal static ProblemHttpResult Reject(long retCode, string detail, ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);

        return ProblemResults.Create(
            retCode,
            detail,
            ProblemSeverity.None,
            loggerFactory: loggerFactory);
    }

    /// <summary>
    /// Builds the rejection for a required member the request did not carry.
    /// </summary>
    /// <param name="memberName">
    /// The member's own name. ALWAYS a compile-time <c>nameof</c> from this file - never a caller
    /// value - which is what makes it safe to place in a problem body.
    /// </param>
    /// <param name="loggerFactory">The logger factory the rejection is recorded through.</param>
    /// <returns>400 carrying <c>E_INVALID_ARGUMENT</c>.</returns>
    /// <remarks>
    /// <para>
    /// The published schema marks these members required, so a client validating against it refuses
    /// the request before sending. This is the server-side half of the same rule, because a contract
    /// enforced only by clients is not enforced.
    /// </para>
    /// <para>
    /// The name is rendered in the WIRE spelling rather than the C# one, so that the sentence names
    /// the member the caller actually sent - or did not. Every call site passes a compile-time
    /// <c>nameof</c>, whose result is Pascal-cased, while the document declares the same member
    /// camel-cased; lowering the first character is the whole of the difference between the two
    /// conventions across this entire surface, and doing it here means no call site has to spell a wire
    /// name as a literal.
    /// </para>
    /// </remarks>
    internal static ProblemHttpResult MissingMember(string memberName, ILoggerFactory loggerFactory)
    {
        ArgumentException.ThrowIfNullOrEmpty(memberName);

        string wireName = string.Concat(
            char.ToLowerInvariant(memberName[0]).ToString(),
            memberName.AsSpan(1));

        return Reject(
            RetCode.E_INVALID_ARGUMENT,
            string.Concat(MissingMemberDetail, " Member: ", wireName, "."),
            loggerFactory);
    }

    /// <summary>
    /// Builds the rejection for a request that did not declare its payload form.
    /// </summary>
    /// <param name="loggerFactory">The logger factory the rejection is recorded through.</param>
    /// <returns>400 carrying <c>E_INVALID_ARGUMENT</c>.</returns>
    /// <remarks>
    /// A SEPARATE ARM RATHER THAN A GENERIC MISSING MEMBER, because the consequence is specific: the
    /// selector chooses which of the two parallel legacy overload families runs and therefore what
    /// form the result takes, so defaulting it would silently pick an overload family on the caller's
    /// behalf. A value outside the two published members never reaches a handler at all - the
    /// deserializer refuses it, because the enumeration admits no integer form and no third name.
    /// </remarks>
    internal static ProblemHttpResult MissingPayloadForm(ILoggerFactory loggerFactory) =>
        Reject(RetCode.E_INVALID_ARGUMENT, MissingPayloadFormDetail, loggerFactory);

    /// <summary>
    /// Builds the rejection for a symmetric cell this port refuses to reproduce.
    /// </summary>
    /// <param name="parity">The classification, which is never the supported value here.</param>
    /// <param name="mode">The effective mode the request selected, recorded in the log line only.</param>
    /// <param name="loggerFactory">The logger factory the rejection is recorded through.</param>
    /// <returns>500 carrying <c>E_NO_IMPLEMENTATION</c> and the published reason code.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="loggerFactory"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// THE STATUS IS 500 AND NOT THE NOT-IMPLEMENTED STATUS, and that is constraint C-D rather than a
    /// preference: that status belongs to the ingress service's four routing declarations for the
    /// deferred capabilities, and producing it here would advertise a deferred capability's surface on
    /// a service that has none. The two symmetric operations declare exactly 200, 400, 401, 403, 404
    /// and 500 as their responses, so 500 is also the only server-side status the published contract
    /// admits for them. It is deliberately not 400: the request is well-formed and would have
    /// succeeded, and the limitation is this port's rather than the caller's.
    /// </para>
    /// <para>
    /// THE MACHINE-READABLE REASON IS PRESERVED. The published reason code is added as an extension
    /// member of the body the shared factory built - the published problem schema is the one schema in
    /// that document with <c>additionalProperties: true</c>, which is what permits it - so a caller
    /// keeps everything it would have branched on. A second problem shape is not declared and the
    /// shared factory is not copied.
    /// </para>
    /// <para>
    /// The refusal text comes from the exception type that owns the classification, so the evidence for
    /// each blocking reason is stated once, beside the classification, and cannot drift from it.
    /// </para>
    /// </remarks>
    internal static ProblemHttpResult RefuseBlockedCell(
        SymmetricCellParity parity,
        long mode,
        ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);

        SymmetricParityUnavailableException refusal = new(parity, mode);

        ProblemHttpResult problem = Reject(
            RetCode.E_NO_IMPLEMENTATION,
            string.Concat(BlockedCellDetailPrefix, refusal.Message),
            loggerFactory);

        problem.ProblemDetails.Extensions[ReasonExtensionMember] = DescribeBlockedCellReason(parity);

        return problem;
    }

    /// <summary>
    /// Renders a blocking classification as the machine-readable reason code the contract publishes.
    /// </summary>
    /// <param name="parity">The classification to render.</param>
    /// <returns>The published reason code.</returns>
    /// <remarks>
    /// Rendered through an explicit switch rather than by the enumeration's own string conversion,
    /// matching the convention the sibling endpoint files use: the published vocabulary is then stated
    /// in this file and cannot drift silently if a member is ever renamed. The supported value cannot
    /// reach a refusal, so it renders as the empty string rather than as a reason that would claim a
    /// block that did not happen.
    /// </remarks>
    internal static string DescribeBlockedCellReason(SymmetricCellParity parity) => parity switch
    {
        SymmetricCellParity.BlockedFeedbackWidthUnprovable => FeedbackWidthUnprovableReason,
        SymmetricCellParity.BlockedSynthesizedVectorUnprovable => SynthesizedVectorUnprovableReason,
        _ => string.Empty,
    };

    // ==============================================================================================
    //  LOGGING AND DOCUMENT METADATA
    // ==============================================================================================

    /// <summary>
    /// Records one structured line describing a completed operation.
    /// </summary>
    /// <param name="loggerFactory">The logger factory the record is written through.</param>
    /// <param name="operation">The operation identifier, which is a published constant.</param>
    /// <param name="algorithm">The algorithm, encoding or key-size selector, or <see langword="null"/>.</param>
    /// <param name="mode">The effective cipher mode, or <see langword="null"/> when none applies.</param>
    /// <param name="padding">The effective RSA padding, or <see langword="null"/> when none applies.</param>
    /// <remarks>
    /// <para>
    /// EVERY FIELD IS A CLASSIFIER AND NEVER CONTENT, and the signature is what enforces that rather
    /// than the discipline of the call sites: there is no parameter through which a payload, a
    /// ciphertext, a digest, a signature, a resolved key, a vector, a generated key pair, a reference
    /// or an authorization header could arrive.
    /// </para>
    /// <para>
    /// Written at DEBUG level. These operations are called in bulk by a service peer, so a record per
    /// call at a shipped level would be pure noise; a REJECTION is recorded instead by the shared
    /// problem factory, at warning for a caller fault and error for a server one.
    /// </para>
    /// </remarks>
    internal static void LogCompleted(
        ILoggerFactory loggerFactory,
        string operation,
        long? algorithm,
        long? mode,
        long? padding)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);

        ILogger logger = loggerFactory.CreateLogger(LoggerCategoryName);

        if (!logger.IsEnabled(LogLevel.Debug))
        {
            return;
        }

        logger.LogDebug(CompletedOperationLogTemplate, operation, algorithm, mode, padding);
    }

    /// <summary>
    /// Declares the bearer security requirement on every operation of this contract in the generated
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
    /// Applied to the ROUTE GROUP, so all 18 operations acquire the requirement from one registration
    /// and a route added later cannot be published as anonymous by omission.
    /// </para>
    /// <para>
    /// A per-endpoint operation transformer is the sanctioned mechanism for this on this toolchain: the
    /// older per-endpoint OpenAPI configuration extension is deprecated and raises a diagnostic that
    /// the repository-wide warnings-as-errors setting turns into a build failure. The generator does
    /// not synthesise a security requirement from authorization metadata by itself, so requiring a
    /// token without declaring it would leave the published document claiming an anonymous surface
    /// while the running service answers 401.
    /// </para>
    /// <para>
    /// Both steps are idempotent and compose with whatever document-wide security the host registers:
    /// the scheme is added only when absent and the requirement only when the operation carries none,
    /// which is also what keeps this file and the sibling <c>PingEndpoints</c> from declaring two
    /// schemes for one thing.
    /// </para>
    /// <para>
    /// NO KEY MATERIAL, KEY-SET ADDRESS OR DISCOVERY ADDRESS APPEARS IN THE DECLARATION. It states the
    /// transport scheme and the token format, which is all a consumer needs in order to point its own
    /// stock bearer handler at the issuer named in that consumer's own configuration.
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
                    + "Every operation of the cryptographic contract requires one and answers 401 "
                    + "without one.",
            };

            document.AddComponent(BearerSchemeName, bearerScheme);
        }

        IList<OpenApiSecurityRequirement> security =
            operation.Security ??= new List<OpenApiSecurityRequirement>();

        if (security.Count == 0)
        {
            security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(BearerSchemeName, document)] = new List<string>(),
            });
        }

        return Task.CompletedTask;
    }
}

// ==================================================================================================
//  THE REFERENCE RESOLUTION BOUNDARY
//  --------------------------------------------------------------------------------------------------
//  This type is the OTHER HALF of the contract-level secrets rule. The seven providers under Crypto/
//  were authored to take ALREADY-RESOLVED material and to read no configuration of their own; that is
//  the structural half. This is where the resolution actually happens, and it happens in exactly one
//  place so that there is one implementation of the rule to review rather than eighteen.
//
//  RAW KEY MATERIAL NEVER CROSSES THE WIRE INBOUND. Not as a key, not as a passphrase, not as a PEM
//  block, not as an initialization vector, and not as an "advanced" alternative to a reference. The
//  proof is structural rather than procedural: no request record in this file declares a member that
//  could carry one, so there is nothing for this type to guard against - it only ever receives short
//  opaque identifiers and only ever hands material forward as a call argument.
// ==================================================================================================

/// <summary>
/// Resolves the opaque references of contract C-02 against this service's configured store, and
/// retains the private half of a generated RSA key pair behind a freshly minted reference.
/// </summary>
/// <remarks>
/// <para>
/// <b>THE RULE THIS TYPE EXISTS TO ENFORCE.</b> Every keyed legacy declaration takes its key as an
/// ordinary in-parameter - <c>readonly string key</c> and <c>readonly blob key</c> on the keyed digest
/// family [ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L23-L26], the same pair on both symmetric
/// families [<c>:L30-L61</c>], and <c>readonly string prikey</c> / <c>readonly string pubkey</c> on the
/// RSA families [<c>:L62-L73</c>]. In one process that is unremarkable, because the key came from the
/// same trust domain as the code that used it. Republished on a wire, the identical signature would put
/// key material into request bodies, into any log that records a request, and into every
/// characterization recording of one. The reference indirection is therefore a deliberate, documented
/// NARROWING of the legacy signature, and it is the reason the published contract has no field into
/// which a key could be placed.
/// </para>
/// <para>
/// <b>RESOLUTION IS EXACTLY WHAT THE OPTIONS TYPE DEFINES AND NOTHING MORE:</b>
/// <c>configuration[ConfigurationKeyPrefix + reference]</c>. There is deliberately no provider
/// discriminator, no reference-to-value map, no secondary store and no fallback - those are choices
/// <see cref="SecurityKeyStoreOptions"/> makes and states, and re-deciding any of them here would put
/// two answers in the repository for one question. The permitted set is consulted BEFORE the
/// configuration is read, so a reference the deployment has not published never becomes a
/// configuration lookup at all.
/// </para>
/// <para>
/// <b>THE TWO FAILURES ARE DISTINGUISHABLE, BECAUSE THE AUTHORED CONTRACT REQUIRES IT.</b> A
/// reference outside the permitted set answers 403; a permitted reference with nothing configured
/// behind it answers 404. The published schema states that distinction explicitly and gives the reason
/// - so that a configuration mistake is tellable from an authorization one - and the contract document
/// is authoritative over any general preference for a single indistinguishable answer. Neither
/// response echoes any part of any stored material, enumerates the store, reports its size or suggests
/// a nearby reference, so the pair distinguishes the two CONDITIONS without becoming an oracle for the
/// store's CONTENTS.
/// </para>
/// <para>
/// <b>ONE NAMESPACE, BECAUSE THE OPTIONS TYPE DECLARES ONE.</b> A file reference is resolved through
/// the same permitted set and the same prefix as a key reference; what makes a reference name a file is
/// solely the value the operator configured behind it. Two consequences are worth stating rather than
/// leaving to be discovered. A key reference passed where a file reference belongs resolves to material
/// that is not an existing rooted path and therefore answers 404, revealing nothing. A file reference
/// passed where a key reference belongs uses the configured path TEXT as key material, which produces a
/// well-defined digest over a value the caller cannot see and discloses nothing about it - and is
/// precisely what the legacy would have done with the same string. Both are bounded by the operator's
/// choice of permitted set and by the fact that every caller of this contract is authenticated.
/// </para>
/// <para>
/// <b>NOTHING IS RETAINED THAT DOES NOT HAVE TO BE.</b> Resolved material is returned to the caller
/// and is never stored in a field, a static or a cache owned by this type. The one exception is
/// deliberate, bounded and documented: the private half of a key pair generated through
/// <see cref="CryptoEndpoints.GenerateRsaKey"/> is retained, because the published contract returns a
/// reference to it rather than the key itself. That store is capped at
/// <see cref="MaximumRetainedGeneratedKeys"/> entries for a reason that is specific to THIS service -
/// it holds the only signing key in the system, so an unbounded store that grows on request would let
/// authenticated callers terminate authentication for every service.
/// </para>
/// <para>
/// <b>NO KEY MATERIAL IS LOGGED, EVER.</b> This type writes no log record of its own at all. A
/// rejection is recorded by the shared problem factory, which receives only a return code and a fixed
/// value-free sentence; there is no code path here through which a reference, a configured value, a
/// path or a generated key could reach a log sink.
/// </para>
/// <para>
/// Registered as a singleton, and safe for concurrent use: the options and configuration it holds are
/// read-only after startup, and the one piece of mutable state is a
/// <see cref="ConcurrentDictionary{TKey, TValue}"/> guarded by an interlocked counter.
/// </para>
/// </remarks>
internal sealed class CryptoReferenceResolver
{
    /// <summary>
    /// The greatest number of generated private keys this service retains at one time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A BOUND RATHER THAN A POLICY, and small on purpose. Key generation is an authenticated
    /// operation, so every entry was asked for by a caller this service admitted; the cap exists
    /// because this is the service that holds the system's only signing key, and memory exhaustion here
    /// would take authentication down for Gateway, DataServices and Persistence at once. Sixty-four
    /// pairs is far more than any legitimate provisioning sequence needs and small enough that the
    /// store cannot become a liability.
    /// </para>
    /// <para>
    /// THE GLOBAL CAP IS RESERVED ATOMICALLY AND BEFORE GENERATION, which is the half that matters
    /// under load. A read of the entry count followed by an insertion is not atomic, so concurrent
    /// callers could each observe room and collectively exceed the bound; worse, RSA generation is the
    /// expensive part, so checking capacity AFTER it would let a caller drive unbounded key-generation work
    /// and have every pair discarded. Both are closed by reserving a slot FIRST - see
    /// <see cref="TryReserveGeneratedKeySlot"/>.
    /// </para>
    /// </remarks>
    internal const int MaximumRetainedGeneratedKeys = 64;

    /// <summary>
    /// The greatest number of generated private keys ONE CALLER may hold retained at a time.
    /// </summary>
    /// <remarks>
    /// A GLOBAL CAP ALONE IS NOT A QUOTA. Without a per-caller bound, one authenticated caller can
    /// occupy the whole store and every other caller's generation request then fails - a denial of
    /// service against peers, delivered entirely through legitimate calls. Eight is far more than a
    /// provisioning sequence needs and small enough that no single caller can crowd the others out.
    /// The owner is the token's subject claim, which is the only caller identity this surface has.
    /// </remarks>
    internal const int MaximumRetainedGeneratedKeysPerCaller = 8;

    /// <summary>How long a retained generated private key remains resolvable.</summary>
    /// <remarks>
    /// <para>
    /// AN EXPIRY IS REQUIRED BECAUSE THE ALTERNATIVE IS UNBOUNDED RETENTION OF PRIVATE KEYS. An
    /// earlier reading declined to expire entries on the grounds that a reference which silently
    /// stopped resolving would be a 404 a caller could not explain. That trade was the wrong way round:
    /// the store holds PRIVATE KEY MATERIAL, and material kept for the process lifetime because nobody
    /// released it is a standing liability in the one service that also holds the system's only signing
    /// key. A 404 after ten minutes is explainable - it is published on the operation, and the caller
    /// can generate again - whereas an unbounded store is not recoverable at all without a restart.
    /// </para>
    /// <para>
    /// TEN MINUTES IS SIZED AGAINST THE USE, not chosen for roundness: a reference exists so that a
    /// caller can immediately use the generated key on one of the sibling operations, which is a
    /// sequence of seconds. It is deliberately longer than the five-minute token lifetime this service
    /// mints, so a caller does not lose its key reference and its credential in the same window.
    /// </para>
    /// <para>
    /// EXPIRY IS SWEPT LAZILY, on reservation and on resolution, rather than by a timer. A timer would
    /// be a background service whose only job is to delete map entries, and the sweep is O(entries)
    /// over a map bounded at sixty-four.
    /// </para>
    /// </remarks>
    internal static readonly TimeSpan RetainedGeneratedKeyLifetime = TimeSpan.FromMinutes(10);

    /// <summary>
    /// The same retention window under the name the ownerless retention path and its suite use.
    /// </summary>
    /// <remarks>
    /// AN ALIAS AND NOT A SECOND POLICY, deliberately. Two window constants with two values in one type
    /// would let a key be expired by one code path and live by the other, and every expiry assertion
    /// would then be true of whichever constant it happened to read. There is one window;
    /// <see cref="RetainedGeneratedKeyLifetime"/> is its definition and this is its other name.
    /// </remarks>
    internal static readonly TimeSpan GeneratedKeyRetention = RetainedGeneratedKeyLifetime;

    /// <summary>
    /// The prefix every minted reference carries.
    /// </summary>
    /// <remarks>
    /// Present so that a minted reference is recognisable as one in a deployment's own diagnostics, and
    /// chosen from the character set the configured set is validated against so that a minted reference
    /// and a configured one are the same shape of thing. It is NOT a namespace separator and confers no
    /// authority: a minted reference resolves because this service minted it and remembers it, not
    /// because of how it is spelled.
    /// </remarks>
    private const string GeneratedReferencePrefix = "gen-";

    /// <summary>The separator between the ordinal and the random half of a minted reference.</summary>
    private const string GeneratedReferenceSeparator = "-";

    /// <summary>
    /// The single bucket every request carrying no subject claim is charged to.
    /// </summary>
    /// <remarks>
    /// ONE SHARED BUCKET RATHER THAN AN EXEMPTION. A request with no attributable caller must not be the
    /// one with no quota, or the quota is bypassable by omitting a claim. The value is not a legal
    /// subject spelling, so it cannot collide with a real caller identity.
    /// </remarks>
    private const string UnattributedOwner = "<unattributed>";

    /// <summary>
    /// Reported when a reservation is committed twice or was never a reservation.
    /// </summary>
    /// <remarks>
    /// Fixed at compile time and mentioning no reference and no key material, like every other message on
    /// this surface. It describes a defect in THIS file rather than a request state, so it is raised
    /// rather than answered as a rejection - an internal fault reported as a caller error is a bug that
    /// hides itself.
    /// </remarks>
    private const string UncommittableReservationMessage =
        "A generated-key slot was committed without a reservation, or its reservation was already " +
        "filled. Reserve a slot before generating a key, and commit or abandon each reservation exactly " +
        "once.";

    /// <summary>
    /// The flag combination used when minting the random half of a reference: neither braces nor
    /// separators.
    /// </summary>
    /// <remarks>
    /// Zero selects the bare 32-character hexadecimal form from
    /// <see cref="RandomProvider.GenGuid(uint)"/>, whose characters are all inside the set a reference
    /// is validated against - braces and hyphens from the default form are not. This is a FORMATTING
    /// choice about an identifier and has nothing to do with the preserved default of the published
    /// <c>generateGuid</c> operation, which is unaffected and still carries both.
    /// </remarks>
    private const uint BareIdentifierFlags = 0U;

    /// <summary>The bound options describing the key store.</summary>
    private readonly IOptions<SecurityOptions> _options;

    /// <summary>The configuration the store is read from.</summary>
    private readonly IConfiguration _configuration;

    /// <summary>The private halves of key pairs generated through this service, by reference.</summary>
    private readonly ConcurrentDictionary<string, RetainedGeneratedKey> _retained =
        new(StringComparer.Ordinal);

    /// <summary>
    /// The number of slots currently RESERVED, which is entries plus reservations in flight.
    /// </summary>
    /// <remarks>
    /// Tracked separately from the map's own count because a slot is reserved BEFORE the key that will
    /// occupy it exists. Counting the map would let a second caller reserve the same slot while the
    /// first is still generating, which is precisely the race the reservation exists to remove.
    /// </remarks>
    private int _reservedSlots;

    /// <summary>Reserved slots per owner, for the per-caller quota.</summary>
    private readonly ConcurrentDictionary<string, int> _reservedPerOwner = new(StringComparer.Ordinal);

    /// <summary>The clock every expiry decision is taken against.</summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>The ordinal of the most recently minted reference.</summary>
    private int _mintOrdinal;

    /// <summary>
    /// Initializes a new instance of the <see cref="CryptoReferenceResolver"/> class.
    /// </summary>
    /// <param name="options">The bound security options, whose key-store section is the policy.</param>
    /// <param name="configuration">The configuration the store is read from.</param>
    /// <exception cref="ArgumentNullException">Either argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// <see cref="IOptions{TOptions}"/> rather than a resolved snapshot, matching the convention the
    /// rest of this service uses, and <see cref="IConfiguration"/> because the store IS the
    /// configuration - the options type says so, and introducing an abstraction over it here would add
    /// a seam with nothing on the other side of it.
    /// </remarks>
    /// <param name="timeProvider">
    /// The clock retained-key expiry is measured against. Injected rather than read from
    /// <see cref="DateTimeOffset.UtcNow"/> so that a characterization run is reproducible: every
    /// non-deterministic value must be maskable from BOTH the master and the candidate recording (AAP
    /// 0.6.7), and an expiry clock is one. Optional so that a test constructing this type directly gets
    /// the system clock without ceremony.
    /// </param>
    public CryptoReferenceResolver(
        IOptions<SecurityOptions> options,
        IConfiguration configuration,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(configuration);

        _options = options;
        _configuration = configuration;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Resolves a key or vector reference to the material behind it.
    /// </summary>
    /// <param name="reference">The opaque reference as the request carried it.</param>
    /// <param name="owner">
    /// The calling caller's identity - the token's subject, as <c>CryptoEndpoints.OwnerOf</c> reads it. A
    /// MINTED reference resolves only for the caller it was minted for; a CONFIGURED reference is
    /// deployment material and is not owned by any caller, so this value does not gate it.
    /// </param>
    /// <param name="loggerFactory">The logger factory a rejection is recorded through.</param>
    /// <param name="material">
    /// The resolved material, or the empty string when this method returns a rejection. The caller uses
    /// it as a call argument and lets it fall out of scope; it is not stored here.
    /// </param>
    /// <returns><see langword="null"/> when the reference resolved; otherwise the rejection.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="owner"/> or <paramref name="loggerFactory"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// THE ORDER OF THE FOUR SCREENS IS PART OF THE DESIGN.
    /// </para>
    /// <para>
    /// First, the retained store, because a reference this service minted seconds ago cannot be in a
    /// set that was configured before the process started. A minted reference is 32 hexadecimal
    /// characters of entropy drawn through the injected source, so it is not guessable.
    /// </para>
    /// <para>
    /// <b>BUT POSSESSION IS NOT THE AUTHORITY TO USE IT, WHICH IS THE CORRECTION IN THIS SCREEN.</b> An
    /// earlier revision resolved a retained reference on a bare table hit, so a minted reference that
    /// leaked - through a caller's log, a proxy trace or a crash dump - let ANOTHER authenticated caller
    /// sign, decrypt and authenticate with a private key it never held (CWE-639, CWE-863). Release already
    /// compared the owner [<see cref="TryReleaseGeneratedKey"/>]; use did not, so the weaker of the two
    /// operations was the guarded one. The owner is now compared on both, from the same subject claim.
    /// </para>
    /// <para>
    /// <b>A FOREIGN MINTED REFERENCE FALLS THROUGH RATHER THAN BEING REFUSED IN PLACE, AND THAT IS WHAT
    /// MAKES IT INDISTINGUISHABLE.</b> It then meets the same length, membership and configuration screens
    /// any unknown value meets, and answers exactly what an unpublished reference answers - so no caller
    /// can use this operation to discover that a reference exists, or that it belongs to somebody else.
    /// Refusing with a distinct code or detail here would have turned the surface into precisely that
    /// oracle.
    /// </para>
    /// <para>
    /// Second, the length bound, refused as a non-permitted reference rather than as a distinct
    /// condition. No permitted entry can exceed it, because
    /// <see cref="SecurityOptionsValidator.MaximumKeyRefLength"/> is the same bound the configured set
    /// is validated against at startup, so this screen changes no outcome - it only stops an
    /// arbitrarily long caller value from being concatenated onto a configuration key.
    /// </para>
    /// <para>
    /// Third, membership of the permitted set, compared ORDINALLY. A configuration key name is machine
    /// input, so a culture-sensitive or case-insensitive comparison would admit references the operator
    /// did not publish - and a case-insensitive match would then read a configuration key the operator
    /// did not write.
    /// </para>
    /// <para>
    /// Fourth, and only then, the configuration read. A configured value that is present but empty or
    /// whitespace is treated as ABSENT, which is the honest reading: an empty key is not a key, and
    /// silently using one would produce a digest or a ciphertext under no key at all while reporting
    /// success.
    /// </para>
    /// </remarks>
    internal ProblemHttpResult? TryResolveReference(
        string? reference,
        string owner,
        ILoggerFactory loggerFactory,
        out string material)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        material = string.Empty;

        if (string.IsNullOrEmpty(reference))
        {
            return CryptoEndpoints.Reject(
                RetCode.E_INVALID_ARGUMENT,
                CryptoEndpoints.MissingReferenceDetail,
                loggerFactory);
        }

        // Expired entries are swept before the lookup, so an expired reference is answered as
        // not-permitted rather than resolved from a map that had not been tidied yet.
        SweepExpiredGeneratedKeys();

        // SCREEN ONE - a reference this service minted FOR THIS CALLER. Checked first because it cannot
        // appear in the configured set, and answered without consulting that set at all.
        //
        // THE OWNER IS NORMALISED EXACTLY AS THE RESERVATION AND THE RELEASE NORMALISE IT, so a token
        // carrying no subject charges to, and reads from, the one shared unattributed bucket rather than
        // matching every entry or none.
        if (_retained.TryGetValue(reference, out RetainedGeneratedKey? retained)
            && string.Equals(
                retained.Owner,
                owner.Length == 0 ? UnattributedOwner : owner,
                StringComparison.Ordinal))
        {
            material = retained.PrivateKey;

            return null;
        }

        SecurityKeyStoreOptions keyStore = _options.Value.KeyStore;

        // SCREEN TWO - the shape bound. Answered as a refusal, at the same status as any other
        // non-permitted reference, so no new distinguishable outcome is introduced.
        if (reference.Length > SecurityOptionsValidator.MaximumKeyRefLength)
        {
            return CryptoEndpoints.Reject(
                RetCode.E_ACCESS_DENIED,
                CryptoEndpoints.ReferenceTooLongDetail,
                loggerFactory);
        }

        // SCREEN THREE - membership, ordinal. A miss ends the call here, so a reference the deployment
        // has not published never reaches the configuration read below.
        if (!IsPermitted(keyStore, reference))
        {
            return CryptoEndpoints.Reject(
                RetCode.E_ACCESS_DENIED,
                CryptoEndpoints.ReferenceForbiddenDetail,
                loggerFactory);
        }

        // SCREEN FOUR - the configuration read, which is exactly the expression the options type
        // documents and nothing more elaborate.
        string? configured = _configuration[keyStore.ConfigurationKeyPrefix + reference];

        if (string.IsNullOrWhiteSpace(configured))
        {
            return CryptoEndpoints.Reject(
                RetCode.E_OBJECT_NOT_FOUND,
                CryptoEndpoints.ReferenceNotFoundDetail,
                loggerFactory);
        }

        material = configured;

        return null;
    }

    /// <summary>
    /// Resolves a file reference to a readable path on this service's own filesystem.
    /// </summary>
    /// <param name="reference">The opaque reference as the request carried it.</param>
    /// <param name="loggerFactory">The logger factory a rejection is recorded through.</param>
    /// <param name="path">
    /// The resolved absolute path, or the empty string when this method returns a rejection.
    /// </param>
    /// <returns><see langword="null"/> when the reference resolved; otherwise the rejection.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="loggerFactory"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// <b>NO CALLER-SUPPLIED PATH EVER CROSSES THIS BOUNDARY, WHICH IS THE WHOLE POINT.</b> The legacy
    /// <c>HashFile</c> family takes <c>readonly string filename</c>
    /// [ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L27-L29]; republished as a wire parameter that would
    /// be an arbitrary-file-read primitive, and every traversal defence would then be a
    /// string-inspection guess about a path the contract does not own. Here the caller names a
    /// reference, the OPERATOR names the path, and the permitted set IS the allow-list. Traversal,
    /// absolute-path escape and symlink escape are consequently not caller-reachable conditions at all
    /// - there is no caller-controlled component of the path to traverse with.
    /// </para>
    /// <para>
    /// A MINTED REFERENCE IS DELIBERATELY NOT CONSULTED. The retained store holds generated private
    /// keys, and letting a minted key reference resolve as a file would let one part of this surface
    /// reinterpret another part's material. Resolution therefore starts at the configured set, which
    /// also means the retained store can never be reached through a file-shaped operation.
    /// </para>
    /// <para>
    /// The configured value is normalised through <see cref="Path.GetFullPath(string)"/> and required
    /// to be rooted and to exist. A value that is not a usable path answers 404 - the same answer as a
    /// permitted reference with nothing behind it - because from the caller's side those are the same
    /// condition: no file is available for this reference. NO PATH, FILENAME, LOCATION, SIZE OR CONTENT
    /// APPEARS IN THE RESPONSE OR IN ANY RECORD.
    /// </para>
    /// <para>
    /// A malformed configured value raises rather than resolving, and the three raising cases are
    /// caught and folded into the same 404: an unusable configuration value is a deployment condition
    /// the caller can neither see nor act on, and reporting a distinct error for it would tell a caller
    /// that a value exists.
    /// </para>
    /// </remarks>
    internal ProblemHttpResult? TryResolveFile(
        string? reference,
        ILoggerFactory loggerFactory,
        out string path)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);

        path = string.Empty;

        if (string.IsNullOrEmpty(reference))
        {
            return CryptoEndpoints.Reject(
                RetCode.E_INVALID_ARGUMENT,
                CryptoEndpoints.MissingReferenceDetail,
                loggerFactory);
        }

        SecurityKeyStoreOptions keyStore = _options.Value.KeyStore;

        if (reference.Length > SecurityOptionsValidator.MaximumKeyRefLength)
        {
            return CryptoEndpoints.Reject(
                RetCode.E_ACCESS_DENIED,
                CryptoEndpoints.ReferenceTooLongDetail,
                loggerFactory);
        }

        if (!IsPermitted(keyStore, reference))
        {
            return CryptoEndpoints.Reject(
                RetCode.E_ACCESS_DENIED,
                CryptoEndpoints.ReferenceForbiddenDetail,
                loggerFactory);
        }

        string? configured = _configuration[keyStore.ConfigurationKeyPrefix + reference];

        if (string.IsNullOrWhiteSpace(configured))
        {
            return CryptoEndpoints.Reject(
                RetCode.E_OBJECT_NOT_FOUND,
                CryptoEndpoints.FileNotAvailableDetail,
                loggerFactory);
        }

        string resolved;

        try
        {
            resolved = Path.GetFullPath(configured);
        }
        catch (ArgumentException)
        {
            return CryptoEndpoints.Reject(
                RetCode.E_OBJECT_NOT_FOUND,
                CryptoEndpoints.FileNotAvailableDetail,
                loggerFactory);
        }
        catch (NotSupportedException)
        {
            return CryptoEndpoints.Reject(
                RetCode.E_OBJECT_NOT_FOUND,
                CryptoEndpoints.FileNotAvailableDetail,
                loggerFactory);
        }
        catch (PathTooLongException)
        {
            return CryptoEndpoints.Reject(
                RetCode.E_OBJECT_NOT_FOUND,
                CryptoEndpoints.FileNotAvailableDetail,
                loggerFactory);
        }

        if (!Path.IsPathRooted(resolved) || !File.Exists(resolved))
        {
            return CryptoEndpoints.Reject(
                RetCode.E_OBJECT_NOT_FOUND,
                CryptoEndpoints.FileNotAvailableDetail,
                loggerFactory);
        }

        path = resolved;

        return null;
    }

    /// <summary>
    /// Reserves one slot in the retained store and mints the reference it will be filled under, BEFORE
    /// any key is generated.
    /// </summary>
    /// <param name="owner">The caller identity the slot is charged to - the token's subject.</param>
    /// <param name="random">The random provider, which holds the injected entropy seam.</param>
    /// <param name="reservation">The reservation, when this method returns <see langword="true"/>.</param>
    /// <returns>
    /// <see langword="true"/> when a slot was reserved; <see langword="false"/> when the global cap or
    /// this owner's quota is already met, in which case NOTHING has been generated and nothing is
    /// retained.
    /// </returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// <b>THE RESERVATION IS THE POINT, AND SO IS ITS ORDER.</b> Checking capacity by reading the entry
    /// count and then inserting is two operations and therefore not atomic: concurrent callers would each
    /// see room and collectively exceed the bound. And checking AFTER generation would let a caller whose
    /// key is then discarded still consume the expensive work, which makes a full store an amplifier rather
    /// than a limit. Reserving first closes both: the counter is advanced with a single interlocked
    /// operation and rolled back on refusal, so the bound holds exactly under any amount of concurrency,
    /// and no RSA key is generated for a request that cannot be retained.
    /// </para>
    /// <para>
    /// <b>THE PER-OWNER QUOTA IS RESERVED IN THE SAME WAY AND ROLLED BACK TOGETHER.</b> A global cap
    /// alone lets one caller occupy the store and starve its peers using nothing but legitimate calls.
    /// The owner comes from the bearer token's subject claim, which is the only caller identity this
    /// surface has; a request carrying no subject is charged to a single fixed anonymous bucket rather
    /// than being exempted, because an unattributable caller must not be the one with no quota.
    /// </para>
    /// <para>
    /// <b>THE REFERENCE IS MINTED HERE, WITH THE SLOT</b>, so that the ordinal that guarantees
    /// uniqueness is drawn exactly once per reservation. The ordinal - not the entropy - is what makes
    /// every reference distinct: the random half is drawn through <see cref="RandomProvider"/> so the
    /// injected entropy seam applies, and a deterministic double returns the same value every time, so
    /// entropy alone would collide immediately under test.
    /// </para>
    /// <para>
    /// Expired entries are swept first, so a store that is full only of stale material admits the
    /// request rather than refusing it.
    /// </para>
    /// </remarks>
    internal bool TryReserveGeneratedKeySlot(
        string owner,
        RandomProvider random,
        out GeneratedKeyReservation reservation)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(random);

        reservation = GeneratedKeyReservation.None;

        SweepExpiredGeneratedKeys();

        if (Interlocked.Increment(ref _reservedSlots) > MaximumRetainedGeneratedKeys)
        {
            _ = Interlocked.Decrement(ref _reservedSlots);

            return false;
        }

        string chargedTo = owner.Length == 0 ? UnattributedOwner : owner;

        if (_reservedPerOwner.AddOrUpdate(chargedTo, 1, static (_, held) => held + 1)
            > MaximumRetainedGeneratedKeysPerCaller)
        {
            // Both counters are rolled back, in the reverse order they were taken, so a refusal leaves
            // the store exactly as it was found.
            ReleaseOwnerSlot(chargedTo);
            _ = Interlocked.Decrement(ref _reservedSlots);

            return false;
        }

        int ordinal = Interlocked.Increment(ref _mintOrdinal);

        reservation = new GeneratedKeyReservation(
            string.Concat(
                GeneratedReferencePrefix,
                ordinal.ToString(CultureInfo.InvariantCulture),
                GeneratedReferenceSeparator,
                random.GenGuid(BareIdentifierFlags)),
            chargedTo);

        return true;
    }

    /// <summary>
    /// Fills a reserved slot with the generated private key.
    /// </summary>
    /// <param name="reservation">A reservation obtained from <see cref="TryReserveGeneratedKeySlot"/>.</param>
    /// <param name="privateKey">The generated private key, in the form the provider emitted it.</param>
    /// <returns>The reference the key is retained under.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="privateKey"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="reservation"/> is not a reservation, or its slot was already filled. Both are
    /// caller defects in this file rather than reachable request states, and neither may be tolerated:
    /// silently overwriting a filled slot would strand the previous key beyond every release path.
    /// </exception>
    /// <remarks>
    /// <b>THE RETAINED PRIVATE KEY IS NEVER LOGGED, ECHOED, DOCUMENTED OR RETURNED.</b> The legacy hands
    /// the private half straight back through a <c>ref</c> parameter
    /// [<c>ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L19-L20</c>]; the authored contract returns the
    /// public half plus a reference to the private one instead, which is the single highest-severity
    /// outbound decision on this surface. It also can never be this service's signing key or a
    /// configured store entry: the value is whatever the provider just generated, so it is not read from
    /// configuration at all, and the reference is minted rather than chosen.
    /// </remarks>
    internal string CommitGeneratedKey(GeneratedKeyReservation reservation, string privateKey)
    {
        ArgumentNullException.ThrowIfNull(privateKey);

        if (!reservation.IsReserved)
        {
            throw new InvalidOperationException(UncommittableReservationMessage);
        }

        RetainedGeneratedKey entry = new(
            privateKey,
            reservation.Owner,
            _timeProvider.GetUtcNow() + RetainedGeneratedKeyLifetime);

        if (!_retained.TryAdd(reservation.Reference, entry))
        {
            throw new InvalidOperationException(UncommittableReservationMessage);
        }

        return reservation.Reference;
    }

    /// <summary>
    /// Gives a reserved slot back without filling it.
    /// </summary>
    /// <param name="reservation">The reservation to abandon. A non-reservation is ignored.</param>
    /// <remarks>
    /// Called when generation fails after a slot was taken. Without it a refused size would leak a slot
    /// per attempt and the store would fill with nothing in it, which is a denial of service assembled
    /// entirely out of rejected requests.
    /// </remarks>
    internal void AbandonGeneratedKeySlot(GeneratedKeyReservation reservation)
    {
        if (!reservation.IsReserved)
        {
            return;
        }

        ReleaseOwnerSlot(reservation.Owner);
        _ = Interlocked.Decrement(ref _reservedSlots);
    }

    /// <summary>
    /// Releases a retained generated key at its owner's request.
    /// </summary>
    /// <param name="reference">The reference the caller was given.</param>
    /// <param name="owner">The releasing caller's identity - the token's subject.</param>
    /// <returns>
    /// <see langword="true"/> when a key owned by that caller was released; <see langword="false"/> when
    /// the reference names nothing retained OR names an entry belonging to a different caller.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="owner"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// <b>THE TWO NEGATIVE CASES ARE DELIBERATELY INDISTINGUISHABLE.</b> "No such reference" and "not
    /// yours" answer identically, because telling them apart would turn this operation into an oracle
    /// for which references exist - and a reference is a credential-like handle. A caller learns only
    /// about its own keys.
    /// </para>
    /// <para>
    /// <b>EXPLICIT RELEASE IS WHAT MAKES THE QUOTA WORKABLE.</b> Without it a caller's only way to free
    /// a slot is to wait out the expiry, so a provisioning sequence longer than the quota would stall
    /// for no reason. With it, the expiry is a backstop for a caller that crashed rather than the only
    /// mechanism.
    /// </para>
    /// <para>
    /// THE VALUE ITSELF CANNOT BE WIPED. It is a <see cref="string"/>, and .NET strings are immutable,
    /// so removal drops the last reference this service holds and nothing more. That residual belongs to
    /// the preserved legacy signature, which returns key text rather than bytes
    /// [<c>n_crypto.sru:L19-L20</c>], and it is recorded rather than hidden.
    /// </para>
    /// </remarks>
    internal bool TryReleaseGeneratedKey(string? reference, string owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        SweepExpiredGeneratedKeys();

        if (string.IsNullOrEmpty(reference)
            || !_retained.TryGetValue(reference, out RetainedGeneratedKey? entry))
        {
            return false;
        }

        string chargedTo = owner.Length == 0 ? UnattributedOwner : owner;

        if (!string.Equals(entry.Owner, chargedTo, StringComparison.Ordinal))
        {
            // Not this caller's key. Answered exactly as an unknown reference is.
            return false;
        }

        if (!_retained.TryRemove(reference, out _))
        {
            // Another release won the race. That caller's release freed the slot, so this one must not
            // free it a second time.
            return false;
        }

        ReleaseOwnerSlot(entry.Owner);
        _ = Interlocked.Decrement(ref _reservedSlots);

        return true;
    }

    /// <summary>Removes every retained key whose lifetime has elapsed.</summary>
    /// <remarks>
    /// Swept lazily from the reservation and resolution paths rather than by a timer: a background
    /// service whose only job is deleting map entries would be more machinery than the problem needs,
    /// and the sweep is O(entries) over a map bounded at
    /// <see cref="MaximumRetainedGeneratedKeys"/>. Each removal releases the slot it held, so an expired
    /// key frees both the global slot and its owner's quota.
    /// </remarks>
    private void SweepExpiredGeneratedKeys()
    {
        if (_retained.IsEmpty)
        {
            return;
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();

        foreach (KeyValuePair<string, RetainedGeneratedKey> entry in _retained)
        {
            // THE BOUNDARY INSTANT IS INSIDE THE WINDOW, WHICH IS THE COMPARISON THE FIELD'S OWN CONTRACT
            // STATES: RetainedGeneratedKey.ExpiresAt is documented as "the instant AFTER which the
            // reference no longer resolves", so AT that instant it still does. A strictly-greater test
            // here made the window exclusive at its end and closed it one tick early - invisible in
            // production, where no caller lands on an exact tick, and a real off-by-one in a lifetime
            // contract that the retention suite pins from both sides.
            if (entry.Value.ExpiresAt >= now)
            {
                continue;
            }

            if (!_retained.TryRemove(entry.Key, out RetainedGeneratedKey? removed))
            {
                // Someone else removed it, and released its slot as part of doing so.
                continue;
            }

            ReleaseOwnerSlot(removed.Owner);
            _ = Interlocked.Decrement(ref _reservedSlots);
        }
    }

    /// <summary>Gives one slot back to an owner's quota, removing the owner when it reaches zero.</summary>
    /// <param name="owner">The owner whose count is decremented.</param>
    /// <remarks>
    /// The entry is REMOVED at zero rather than left holding a zero, so the per-owner map cannot grow
    /// without bound across many short-lived callers - which would reintroduce the unbounded growth the
    /// global cap exists to prevent, one map entry at a time.
    /// </remarks>
    private void ReleaseOwnerSlot(string owner)
    {
        while (_reservedPerOwner.TryGetValue(owner, out int held))
        {
            if (held <= 1)
            {
                if (_reservedPerOwner.TryRemove(new KeyValuePair<string, int>(owner, held)))
                {
                    return;
                }

                continue;
            }

            if (_reservedPerOwner.TryUpdate(owner, held - 1, held))
            {
                return;
            }
        }
    }

    /// <summary>
    /// Retains a generated private key in one step, for a caller this surface cannot attribute.
    /// </summary>
    /// <param name="privateKey">The provider's textual rendering of the private half.</param>
    /// <param name="random">The entropy seam the reference's random half is drawn through.</param>
    /// <param name="keyRef">The minted reference, or the empty string when the store refused.</param>
    /// <returns><see langword="true"/> when the key was retained.</returns>
    /// <remarks>
    /// <para>
    /// THE GLOBAL CAP BINDS HERE AND THE PER-CALLER QUOTA DOES NOT, WHICH IS THE WHOLE DIFFERENCE FROM
    /// <see cref="TryReserveGeneratedKeySlot"/>. A quota is a statement about one caller's share, and this
    /// entry point has no caller to charge - so charging every ownerless retention to one bucket would cap
    /// the whole surface at one caller's share, and exempting it from the global cap would leave the store
    /// unbounded by the one route that names nobody. The global cap therefore applies and the quota does
    /// not, and the entry is attributed to the same fixed anonymous owner the reservation path uses so a
    /// release can still find it.
    /// </para>
    /// <para>
    /// EXPIRED ENTRIES ARE RECLAIMED FIRST, so a store whose window has closed on its contents admits the
    /// next retention instead of refusing it for the life of the process.
    /// </para>
    /// <para>
    /// THE CAPACITY CLAIM AND THE CAP COMPARISON ARE ONE INTERLOCKED STEP, for the reason the reservation
    /// path records: reading the map's count and then inserting is a check-then-act pair that every
    /// concurrent caller passes, admitting as many entries beyond the cap as there are requests in flight.
    /// A claim that overshoots is released immediately, and so is one whose insertion then fails.
    /// </para>
    /// </remarks>
    internal bool TryRetainGeneratedPrivateKey(
        string privateKey,
        RandomProvider random,
        out string keyRef)
    {
        ArgumentNullException.ThrowIfNull(privateKey);
        ArgumentNullException.ThrowIfNull(random);

        keyRef = string.Empty;

        SweepExpiredGeneratedKeys();

        if (!TryReserveRetentionSlot())
        {
            return false;
        }

        int ordinal = Interlocked.Increment(ref _mintOrdinal);

        string minted = string.Concat(
            GeneratedReferencePrefix,
            ordinal.ToString(CultureInfo.InvariantCulture),
            GeneratedReferenceSeparator,
            random.GenGuid(BareIdentifierFlags));

        RetainedGeneratedKey entry = new(
            privateKey,
            UnattributedOwner,
            _timeProvider.GetUtcNow() + RetainedGeneratedKeyLifetime);

        if (!_retained.TryAdd(minted, entry))
        {
            // Unreachable while the interlocked ordinal is part of the reference, and released anyway:
            // leaking the claim would shrink the usable store by one slot per collision, which is the same
            // outage the cap exists to prevent arriving by another route.
            ReleaseRetentionSlot();

            return false;
        }

        keyRef = minted;

        return true;
    }

    /// <summary>
    /// Claims one slot in the retained store without minting a reference for it.
    /// </summary>
    /// <returns><see langword="true"/> when a slot was claimed.</returns>
    /// <remarks>
    /// THE BARE HALF OF THE RESERVATION, for a caller that will decide later whether it has anything to
    /// retain. Capacity is claimed by a single interlocked increment compared against the cap, so the
    /// bound holds exactly however many callers arrive at once - taking capacity IS observing it, and no
    /// window exists in which two callers both see the same room. A claim that overshoots is given back
    /// immediately, leaving the counter exactly where it was.
    /// </remarks>
    internal bool TryReserveRetentionSlot()
    {
        if (Interlocked.Increment(ref _reservedSlots) <= MaximumRetainedGeneratedKeys)
        {
            return true;
        }

        _ = Interlocked.Decrement(ref _reservedSlots);

        return false;
    }

    /// <summary>
    /// Releases a slot a caller reserved but did not fill.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Called on every path that reserved a slot and retained nothing - an unusable key size, a generation
    /// the provider refused, or a reference collision. Without it a refused request would consume capacity
    /// permanently, so a service that had answered enough failures would refuse every subsequent
    /// generation while holding no keys at all: a self-inflicted denial of service reachable by repeating
    /// a request the service itself rejects.
    /// </para>
    /// <para>
    /// It is deliberately NOT idempotent and NOT clamped at zero. Releasing a slot that was never reserved
    /// is a defect in the caller's pairing, and clamping would hide it while quietly raising the effective
    /// cap; the call sites pair reserve and release in a <c>try</c>/<c>finally</c> so the pairing is
    /// structural rather than remembered.
    /// </para>
    /// </remarks>
    internal void ReleaseRetentionSlot() => _ = Interlocked.Decrement(ref _reservedSlots);

    /// <summary>
    /// Determines whether a reference is one the deployment has published.
    /// </summary>
    /// <param name="keyStore">The bound key-store options.</param>
    /// <param name="reference">The reference to test.</param>
    /// <returns><see langword="true"/> when the reference is permitted.</returns>
    /// <remarks>
    /// <para>
    /// An ordinal walk of the configured list rather than a prepared set. The list is validated at
    /// startup to hold short bounded identifiers and a deployment publishes a handful of them, so a
    /// walk is cheaper than the set would be to build and cannot go stale if the options are ever
    /// reloaded. <see cref="StringComparer.Ordinal"/> is deliberate: a configuration key name is
    /// machine input, and a case-insensitive match would resolve against a configuration key the
    /// operator never wrote.
    /// </para>
    /// <para>
    /// AN EMPTY PERMITTED SET REFUSES EVERY REFERENCE, which is the correct closed default. A
    /// deployment that has published nothing has authorised nothing, and the shipped configuration
    /// leaves the set empty - so a service brought up without a reviewed key store answers 403 to every
    /// keyed operation rather than quietly reading whatever configuration key a caller names.
    /// </para>
    /// </remarks>
    private static bool IsPermitted(SecurityKeyStoreOptions keyStore, string reference)
    {
        IList<string> permitted = keyStore.PermittedKeyRefs;

        for (int index = 0; index < permitted.Count; index++)
        {
            if (StringComparer.Ordinal.Equals(permitted[index], reference))
            {
                return true;
            }
        }

        return false;
    }
}


// ==================================================================================================
//  THE PAYLOAD FORM SELECTOR
//  --------------------------------------------------------------------------------------------------
//  The legacy declares each payload-bearing operation TWICE - once over a string and once over a blob
//  - and the two halves are not interchangeable, because the string half applies a text encoding and
//  the blob half does not. JSON has no binary type, so a wire contract cannot recover the distinction
//  from the value itself: a base64 string and a legacy string are both strings. This selector is
//  therefore the member that chooses which legacy overload family runs, and it is required on every
//  operation that has two of them.
// ==================================================================================================

/// <summary>
/// Selects which of the two parallel legacy overload families an operation runs, and therefore how the
/// <c>data</c> member is interpreted and what form the result takes.
/// </summary>
/// <remarks>
/// <para>
/// THE MEMBER NAMES ARE THE WIRE VALUES AND ARE UPPER CASE DELIBERATELY. The published document
/// declares <c>enum: [STRING, BLOB]</c> with matching <c>x-enum-varnames</c>, the consuming service
/// already serializes exactly those two tokens, and its own tests assert them, so any other spelling
/// here would be a wire-breaking change dressed as a naming preference. The upper-case spelling is
/// consequently a CONTRACT value rather than a constant identifier, which is why it does not fall under
/// the preserved-identifier rule that governs the <c>CRYPTO_</c> catalogue.
/// </para>
/// <para>
/// THE TWO DECLARED TOKENS ARE THE ONLY ACCEPTED FORMS, CHARACTER FOR CHARACTER. The converter admits
/// <c>STRING</c> and <c>BLOB</c> and refuses everything else - a number, a third name, an empty string,
/// and any other casing of either token - and it refuses them in the deserializer, before any handler
/// runs. That matters because the selector governs which overload family executes, and a silently
/// coerced selector would silently pick one.
/// </para>
/// </remarks>
[JsonConverter(typeof(PayloadFormJsonConverter))]
public enum PayloadForm
{
    /// <summary>
    /// The string-shaped legacy overload family: the <c>data</c> member carries the legacy string
    /// verbatim, and the result - where the operation produces a payload - is a legacy string.
    /// </summary>
    /// <remarks>
    /// Text is converted to and from bytes through the single repository-wide encoding recorded as
    /// decision D4 on <see cref="LegacyDefaults"/>, which is the one place that choice is stated.
    /// </remarks>
    STRING,

    /// <summary>
    /// The blob-shaped legacy overload family: the <c>data</c> member carries base64 of the raw bytes,
    /// and the result - where the operation produces a payload - is base64 of raw bytes.
    /// </summary>
    /// <remarks>
    /// THE BASE64 IS JSON TRANSPORT ONLY and is never the legacy encoding argument. The two answer
    /// different questions, which is why a request to encode bytes as hexadecimal submits base64 and
    /// receives hexadecimal - correct rather than contradictory.
    /// </remarks>
    BLOB,
}

/// <summary>
/// Reads and writes <see cref="PayloadForm"/> as exactly one of the two tokens the published document
/// declares, and refuses every other form.
/// </summary>
/// <remarks>
/// <para>
/// HAND-WRITTEN RATHER THAN DERIVED FROM THE STOCK STRING-ENUM CONVERTER, AND THE REASON IS THE ONE
/// SETTING THAT CONVERTER DOES NOT EXPOSE: it matches token names CASE-INSENSITIVELY by construction.
/// Measured on this toolchain, a converter derived from it with a null naming policy and integer values
/// refused still accepts <c>"string"</c>, <c>"Blob"</c> and <c>"sTRING"</c>. The published document
/// declares <c>enum: [STRING, BLOB]</c>, so a document-validating client refuses all three, and
/// accepting a token the document forbids is a divergence between this service and its own contract
/// (CWE-20). An ordinal comparison against the two declared tokens is the only shape that closes it.
/// </para>
/// <para>
/// A NAMED CONVERTER TYPE RATHER THAN AN INLINE ATTRIBUTE ARGUMENT, so the decision is stated once and
/// cannot drift between the operations that use it, and so it holds for EVERY reader and writer of the
/// enumeration - including one added later that does not reuse this service's configured serializer
/// options. It mirrors the converter the consuming service declares for the same enumeration, which is
/// what keeps the two ends of this contract in agreement about the wire tokens.
/// </para>
/// <para>
/// THE GENERATED DOCUMENT'S SCHEMA FOR THIS TYPE IS RESTORED SEPARATELY, and that is a consequence of
/// this choice rather than an oversight. The schema exporter can describe the stock enum converter and
/// cannot describe a custom one, so it emits an UNCONSTRAINED schema for a type carrying this
/// attribute. <see cref="PayloadFormSchemaTransformer"/> puts the two declared tokens back, so the
/// generated document keeps saying what the authored one says.
/// </para>
/// <para>
/// NULL IS NOT THIS CONVERTER'S CONCERN. Every request record models the member as nullable for the
/// contract-fidelity reason recorded on those records, and the serializer's own nullable wrapper answers
/// an absent or null member without reaching this type - so an absent selector remains the observable
/// state the handlers answer with the contract's own bad-request body.
/// </para>
/// </remarks>
internal sealed class PayloadFormJsonConverter : JsonConverter<PayloadForm>
{
    /// <summary>The token that selects the string-shaped legacy overload family.</summary>
    internal const string StringToken = nameof(PayloadForm.STRING);

    /// <summary>The token that selects the blob-shaped legacy overload family.</summary>
    internal const string BlobToken = nameof(PayloadForm.BLOB);

    /// <summary>The refusal text, which names the accepted set and echoes nothing the caller sent.</summary>
    /// <remarks>
    /// The rejected token is deliberately NOT quoted back. A deserialization failure is answered as the
    /// contract's bad request, and echoing caller-supplied bytes into a response body is the one habit
    /// that turns a validation message into a reflection vector.
    /// </remarks>
    private const string RefusalMessage =
        "The payload form must be the JSON string \"" + StringToken + "\" or \"" + BlobToken
            + "\", spelled exactly as the contract declares it.";

    /// <summary>Reads one of the two declared tokens.</summary>
    /// <param name="reader">The reader positioned on the value.</param>
    /// <param name="typeToConvert">The requested type, always <see cref="PayloadForm"/>.</param>
    /// <param name="options">The serializer options, which this converter deliberately ignores.</param>
    /// <returns>The selected overload family.</returns>
    /// <exception cref="JsonException">
    /// The value is not a JSON string, or is a string that is not one of the two declared tokens.
    /// </exception>
    /// <remarks>
    /// The options are ignored ON PURPOSE. A naming policy, a case-insensitivity setting or a number
    /// handling mode configured on the host would otherwise be able to change what this contract accepts,
    /// and a contract whose accepted values depend on host configuration is not a contract.
    /// </remarks>
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
    /// The value names no declared member, which .NET permits for any enumeration and which this
    /// contract does not.
    /// </exception>
    /// <remarks>
    /// THE UNDEFINED ARM THROWS RATHER THAN EMITTING A NUMBER. An enumeration in .NET does not restrict a
    /// value to its declared members, and a numeric form on the wire is precisely what the read half
    /// refuses - so emitting one would publish a value this contract's own reader would reject.
    /// </remarks>
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
/// Restores the two declared payload-form tokens on the generated contract document.
/// </summary>
/// <remarks>
/// <para>
/// THIS EXISTS BECAUSE OF A TOOLCHAIN LIMIT RATHER THAN A DESIGN PREFERENCE, and the limit is worth
/// stating so a later reader does not delete the transformer as redundant. The document generator builds
/// each schema from the serializer's type information, and the exporter can only describe converters it
/// recognises: a type carrying a CUSTOM converter is emitted as an unconstrained schema, because the
/// exporter has no way to ask a converter what it accepts. Measured on this toolchain, replacing the
/// stock string-enum converter with <see cref="PayloadFormJsonConverter"/> turns this member's generated
/// schema from the two declared tokens into "anything".
/// </para>
/// <para>
/// IT RESTATES THE AUTHORED DOCUMENT AND NOTHING ELSE.
/// shared/PowerFramework.Contracts/OpenApi/security.v1.yaml declares this member as
/// <c>type: string</c> with <c>enum: [STRING, BLOB]</c>; that file is authoritative, and if it and this
/// transformer ever disagree, that file wins and this one is the defect. Nothing about the member's
/// name, its requiredness or any other schema is touched, and no other type is inspected.
/// </para>
/// </remarks>
internal sealed class PayloadFormSchemaTransformer : IOpenApiSchemaTransformer
{
    /// <summary>Applies the declared token set to the payload-form schema.</summary>
    /// <param name="schema">The schema the generator produced.</param>
    /// <param name="context">The generation context, which names the type the schema describes.</param>
    /// <param name="cancellationToken">A token that cancels generation.</param>
    /// <returns>A completed task; the work is synchronous and touches only an in-memory schema.</returns>
    /// <remarks>
    /// THE NULLABLE FORM IS MATCHED AS WELL AS THE BARE ONE, AND THAT IS NOT DEFENSIVE. Every request
    /// record declares the selector as nullable, for the contract-fidelity reason recorded on those
    /// records, so the type the generator describes is the NULLABLE one - measured on this toolchain, a
    /// test against the bare type alone matches nothing at all and the component schema is emitted empty.
    /// The bare arm is kept because the type is public and a later request record may declare it
    /// non-nullable.
    /// </remarks>
    public Task TransformAsync(
        OpenApiSchema schema,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(context);

        Type described = context.JsonTypeInfo.Type;

        if ((Nullable.GetUnderlyingType(described) ?? described) != typeof(PayloadForm))
        {
            return Task.CompletedTask;
        }

        schema.Type = JsonSchemaType.String;

        schema.Enum =
        [
            PayloadFormJsonConverter.StringToken,
            PayloadFormJsonConverter.BlobToken,
        ];

        return Task.CompletedTask;
    }
}

// ==================================================================================================
//  THE REQUEST AND RESPONSE RECORDS
//  --------------------------------------------------------------------------------------------------
//  ALL DECLARED IN THIS FILE, deliberately. The published contract is not compiled - the definition is
//  packaged as content and the protocol definitions of the Contracts project cover the other three
//  contracts only - so there is no generated type for this surface and every record here is authored by
//  hand and conforms to the document by review plus the assertions the test project makes against it.
//
//  EVERY WIRE NAME IS PINNED WITH AN EXPLICIT ATTRIBUTE rather than left to a serializer naming policy.
//  A policy is host configuration, and a contract whose member names depend on host configuration is
//  not a contract; pinning them means a change to the host's serializer options cannot rename a
//  published member.
//
//  EVERY MEMBER OF EVERY REQUEST IS NULLABLE AND NONE IS 'required'. That is not laxity, it is what
//  puts the absent-member rejection inside this file: a 'required' member makes the deserializer answer
//  with ITS OWN error shape, which carries no legacy return code, no severity and none of the fixed
//  value-free wording this surface publishes. Absence is therefore detected by a handler and answered
//  through the one shared problem factory, so every rejection on this surface has the same shape.
//
//  NO MEMBER HAS A DEFAULT VALUE OF ANY KIND, and specifically not a key, a vector, a passphrase, a
//  certificate, a token, a modulus, a ciphertext or a sample of any of them. Constraint C-F is
//  discharged structurally here: there is no property initializer anywhere in this section for such a
//  value to hide in, and no request member into which key material could be placed even by a caller
//  that wanted to.
// ==================================================================================================

/// <summary>
/// The members both symmetric directions share, so that the one validation, resolution and dispatch
/// body can serve either without the two published schemas collapsing into one.
/// </summary>
/// <remarks>
/// The published document declares <c>SymEncryptRequest</c> and <c>SymDecryptRequest</c> as two
/// schemas whose members are identical, and the generated document names a schema after the type that
/// binds it - so binding one type to both operations would publish one schema where the contract
/// declares two. Two records with a shared view keeps the document faithful AND keeps the body single,
/// which is the combination that matters: the two directions differ only in which provider family they
/// reach, and no validation, resolution or classification rule differs between them.
/// </remarks>
internal interface ISymmetricCipherRequest
{
    /// <summary>The payload, interpreted according to <see cref="PayloadForm"/>.</summary>
    string? Data { get; }

    /// <summary>The overload-family selector.</summary>
    PayloadForm? PayloadForm { get; }

    /// <summary>An opaque handle to the key, which never crosses the wire itself.</summary>
    string? KeyRef { get; }

    /// <summary>An opaque handle to the initialization vector, absent for the no-vector family.</summary>
    string? IvRef { get; }

    /// <summary>The cipher selector, from the five published members.</summary>
    long? CipherType { get; }

    /// <summary>The mode selector, absent for the mode-omitting family.</summary>
    long? Mode { get; }
}

/// <summary>
/// A request to compute an unkeyed digest over an in-memory payload.
/// </summary>
/// <remarks>
/// Binds the two <c>Hash</c> declarations at
/// [ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L21-L22], whose only difference is the payload's form.
/// The selector set is the FULL published one, MD5 (0) through CRC32 (5) [enums.sru:L928-L933].
/// </remarks>
public sealed record HashRequest
{
    /// <summary>The payload, interpreted according to <see cref="PayloadForm"/>.</summary>
    [Required]
    [JsonPropertyName("data")]
    public string? Data { get; init; }

    /// <summary>The overload-family selector.</summary>
    [Required]
    [JsonPropertyName("payloadForm")]
    public PayloadForm? PayloadForm { get; init; }

    /// <summary>The hash selector, from the six published members.</summary>
    [Required]
    [JsonPropertyName("hashType")]
    public long? HashType { get; init; }
}

/// <summary>
/// A request to compute a keyed digest over an in-memory payload.
/// </summary>
/// <remarks>
/// Binds the four keyed <c>Hash</c> declarations at
/// [ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L23-L26] - the full cross product of a text or binary
/// payload with a text or binary key - whose key half is collapsed by the reference indirection. The
/// checksum member of the selector set is refused on this operation, because a checksum has no keyed
/// construction.
/// </remarks>
public sealed record HmacRequest
{
    /// <summary>The payload, interpreted according to <see cref="PayloadForm"/>.</summary>
    [Required]
    [JsonPropertyName("data")]
    public string? Data { get; init; }

    /// <summary>The overload-family selector, which also selects the resolved key's form.</summary>
    [Required]
    [JsonPropertyName("payloadForm")]
    public PayloadForm? PayloadForm { get; init; }

    /// <summary>
    /// An opaque handle to key material held in this service's own configured store.
    /// </summary>
    /// <remarks>
    /// IT CARRIES NO KEY MATERIAL. There is deliberately no alternative member on this record through
    /// which a key, a passphrase or a PEM block could be supplied instead - that is the contract-level
    /// secrets rule made structural rather than enforced by review.
    /// </remarks>
    [Required]
    [JsonPropertyName("keyRef")]
    public string? KeyRef { get; init; }

    /// <summary>The hash selector, from the five published members a key applies to.</summary>
    [Required]
    [JsonPropertyName("hashType")]
    public long? HashType { get; init; }
}

/// <summary>
/// A request to compute an unkeyed digest over a file this service has been configured to expose.
/// </summary>
/// <remarks>
/// Binds <c>HashFile</c> at [ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L27]. THERE IS NO PATH MEMBER,
/// which is the whole point: the legacy takes a filename, and republishing that as a wire parameter
/// would be an arbitrary-file-read primitive.
/// </remarks>
public sealed record HashFileRequest
{
    /// <summary>
    /// An opaque handle to a file this service has been configured to expose.
    /// </summary>
    /// <remarks>
    /// NOT A PATH, NOT A FILENAME, NOT A DIRECTORY AND NOT A URL. The caller names a reference and the
    /// operator names the path, so traversal and absolute-path escape are not caller-reachable
    /// conditions - there is no caller-controlled component of the path to traverse with.
    /// </remarks>
    [Required]
    [JsonPropertyName("fileRef")]
    public string? FileRef { get; init; }

    /// <summary>The hash selector, from the six published members.</summary>
    [Required]
    [JsonPropertyName("hashType")]
    public long? HashType { get; init; }
}

/// <summary>
/// A request to compute a keyed digest over a file this service has been configured to expose.
/// </summary>
/// <remarks>
/// Binds the two keyed <c>HashFile</c> declarations at
/// [ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L28-L29], which differ only in the key's form. BOTH the
/// filename and the key stand behind references, so neither a path nor key material crosses this
/// boundary in either direction.
/// </remarks>
public sealed record HmacFileRequest
{
    /// <summary>An opaque handle to a file. See <see cref="HashFileRequest.FileRef"/>.</summary>
    [Required]
    [JsonPropertyName("fileRef")]
    public string? FileRef { get; init; }

    /// <summary>An opaque handle to the key. See <see cref="HmacRequest.KeyRef"/>.</summary>
    [Required]
    [JsonPropertyName("keyRef")]
    public string? KeyRef { get; init; }

    /// <summary>The hash selector, from the five published members a key applies to.</summary>
    [Required]
    [JsonPropertyName("hashType")]
    public long? HashType { get; init; }
}

/// <summary>
/// A request to encrypt a payload with one of the five published symmetric ciphers.
/// </summary>
/// <remarks>
/// <para>
/// Binds the 16 <c>SymEncrypt</c> declarations at
/// [ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L30-L45]. All 16 are reachable through the three
/// discriminators this record carries: the payload form, which also selects the resolved key's and
/// vector's form, the presence of the vector reference, and the presence of the mode.
/// </para>
/// <para>
/// THE VECTOR'S FORM FOLLOWS THE KEY'S BY CONSTRUCTION RATHER THAN BY VALIDATION. The legacy pairs a
/// text key only with a text vector [<c>:L32-L33</c>, <c>:L40-L41</c>] and a binary key only with a
/// binary vector [<c>:L36-L37</c>, <c>:L44-L45</c>]; here both forms are chosen by the single payload
/// selector, so a mismatched pair is not merely rejected - it is inexpressible.
/// </para>
/// <para>
/// KNOWN LEGACY WEAKNESS - OMITTING THE MODE RUNS IN ECB, which is the preserved default aliased at
/// [enums.sru:L946]. The mode is not made required and the default is not strengthened; both would be
/// the silent correction the requirements forbid.
/// </para>
/// <para>
/// KNOWN LEGACY WEAKNESS - THERE IS NO AUTHENTICATED MODE. The published set is ECB, CBC and CFB
/// [enums.sru:L943-L945]; no member names an authenticated mode, so the ciphertext this operation
/// returns carries no integrity tag and this record offers no member with which to ask for one.
/// </para>
/// </remarks>
public sealed record SymEncryptRequest : ISymmetricCipherRequest
{
    /// <summary>The plaintext, interpreted according to <see cref="PayloadForm"/>.</summary>
    [Required]
    [JsonPropertyName("data")]
    public string? Data { get; init; }

    /// <summary>
    /// The overload-family selector, which governs the payload's interpretation, the result's form, and
    /// the form the resolved key and vector are presented in.
    /// </summary>
    [Required]
    [JsonPropertyName("payloadForm")]
    public PayloadForm? PayloadForm { get; init; }

    /// <summary>An opaque handle to the key. See <see cref="HmacRequest.KeyRef"/>.</summary>
    [Required]
    [JsonPropertyName("keyRef")]
    public string? KeyRef { get; init; }

    /// <summary>
    /// An opaque handle to the initialization vector, resolved exactly as a key reference is.
    /// </summary>
    /// <remarks>
    /// OMITTING IT SELECTS THE NO-VECTOR OVERLOAD FAMILY, which is the correct pairing under the
    /// preserved default mode: ECB uses no vector at all, so the eight no-vector declarations and the
    /// default mode belong together. The vector itself never crosses the wire, for the same reason a
    /// key does not.
    /// </remarks>
    [JsonPropertyName("ivRef")]
    public string? IvRef { get; init; }

    /// <summary>
    /// The cipher selector, from the five published members [enums.sru:L936-L940].
    /// </summary>
    /// <remarks>
    /// Declared here at the width of the published catalogue rather than at the narrower width the
    /// legacy signature uses for this argument on all 32 overloads. The inconsistency is the oracle's
    /// own; the boundary screens the value into the narrower domain and carries the discrepancy rather
    /// than tidying either side of it.
    /// </remarks>
    [Required]
    [JsonPropertyName("cipherType")]
    public long? CipherType { get; init; }

    /// <summary>
    /// The mode selector, from the three published members, or absent for the mode-omitting family.
    /// </summary>
    [JsonPropertyName("mode")]
    public long? Mode { get; init; }
}

/// <summary>
/// A request to decrypt a payload with one of the five published symmetric ciphers.
/// </summary>
/// <remarks>
/// <para>
/// Binds the 16 <c>SymDecrypt</c> declarations at
/// [ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L46-L61], which the legacy declares as the exact mirror
/// of the encrypt family - so this record's members mirror
/// <see cref="SymEncryptRequest"/> exactly. It is a SEPARATE TYPE because the published document
/// declares a separate schema, and the generated document names a schema after the type that binds it.
/// </para>
/// <para>
/// A 200 FROM THIS OPERATION DOES NOT ASSERT THAT THE CORRECT KEY WAS USED. There is no authenticated
/// mode on this surface, so a decryption under the wrong key, mode or vector may return a well-formed
/// but meaningless payload, and no integrity check this contract can offer distinguishes the two.
/// </para>
/// </remarks>
public sealed record SymDecryptRequest : ISymmetricCipherRequest
{
    /// <summary>The ciphertext, interpreted according to <see cref="PayloadForm"/>.</summary>
    [Required]
    [JsonPropertyName("data")]
    public string? Data { get; init; }

    /// <summary>The overload-family selector. See <see cref="SymEncryptRequest.PayloadForm"/>.</summary>
    [Required]
    [JsonPropertyName("payloadForm")]
    public PayloadForm? PayloadForm { get; init; }

    /// <summary>An opaque handle to the key. See <see cref="HmacRequest.KeyRef"/>.</summary>
    [Required]
    [JsonPropertyName("keyRef")]
    public string? KeyRef { get; init; }

    /// <summary>An opaque handle to the vector. See <see cref="SymEncryptRequest.IvRef"/>.</summary>
    [JsonPropertyName("ivRef")]
    public string? IvRef { get; init; }

    /// <summary>The cipher selector. See <see cref="SymEncryptRequest.CipherType"/>.</summary>
    [Required]
    [JsonPropertyName("cipherType")]
    public long? CipherType { get; init; }

    /// <summary>The mode selector, or absent for the mode-omitting family.</summary>
    [JsonPropertyName("mode")]
    public long? Mode { get; init; }
}

/// <summary>
/// A request to encrypt or decrypt a payload with an RSA key.
/// </summary>
/// <remarks>
/// <para>
/// Binds the four <c>RSAEncrypt</c> declarations at
/// [ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L62-L65] and the four mirrored <c>RSADecrypt</c>
/// declarations at [<c>:L66-L69</c>]. One schema serves both directions because the published document
/// declares one for them, which is itself faithful: the legacy declares the two families with
/// identical parameter shapes.
/// </para>
/// <para>
/// KNOWN LEGACY WEAKNESS - OMITTING THE PADDING USES PKCS#1, the preserved default aliased at
/// [enums.sru:L951]. The alternative member is selectable, and the third possibility a modern caller
/// might reach for - no padding at all - IS EXPLICITLY REFUSED, for a mechanical reason rather than a
/// policy one: the published set has exactly two members [enums.sru:L949-L950] and none of them names
/// it, so there is nothing to select and nothing to implement.
/// </para>
/// </remarks>
public sealed record RsaCipherRequest
{
    /// <summary>The payload, interpreted according to <see cref="PayloadForm"/>.</summary>
    [Required]
    [JsonPropertyName("data")]
    public string? Data { get; init; }

    /// <summary>
    /// The overload-family selector, which governs the payload's interpretation and the result's form.
    /// </summary>
    /// <remarks>
    /// It does NOT govern the key's form here: the legacy types both RSA key halves as text on every
    /// one of the 12 declarations at [n_crypto.sru:L62-L73], so the resolved material is used as text
    /// in both directions.
    /// </remarks>
    [Required]
    [JsonPropertyName("payloadForm")]
    public PayloadForm? PayloadForm { get; init; }

    /// <summary>An opaque handle to the key half this direction needs.</summary>
    /// <remarks>
    /// The encrypt direction needs the public half and the decrypt direction the private one. WHICH
    /// HALF A REFERENCE HOLDS IS THE OPERATOR'S CONFIGURATION RATHER THAN A MEMBER OF THIS SCHEMA, so
    /// there is no member here that could be used to ask this service for a private key.
    /// </remarks>
    [Required]
    [JsonPropertyName("keyRef")]
    public string? KeyRef { get; init; }

    /// <summary>
    /// The padding selector, from the two published members, or absent for the preserved default.
    /// </summary>
    [JsonPropertyName("padding")]
    public long? Padding { get; init; }
}

/// <summary>
/// A request to sign a payload with an RSA private key.
/// </summary>
/// <remarks>
/// Binds the two <c>RSASign</c> declarations at
/// [ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L70-L71], which differ in the form of the SIGNATURE they
/// return rather than of the payload they consume - so the payload selector governs the result's form
/// here. The checksum member of the selector set is refused: the oracle's own comment at
/// [enums.sru:L927] names this set as the argument of the signature members too, but no signature
/// construction exists over a checksum.
/// </remarks>
public sealed record RsaSignRequest
{
    /// <summary>The payload to sign, interpreted according to <see cref="PayloadForm"/>.</summary>
    [Required]
    [JsonPropertyName("data")]
    public string? Data { get; init; }

    /// <summary>The overload-family selector, which governs the signature's form.</summary>
    [Required]
    [JsonPropertyName("payloadForm")]
    public PayloadForm? PayloadForm { get; init; }

    /// <summary>
    /// An opaque handle to the signing key, whose private half never leaves this service.
    /// </summary>
    /// <remarks>
    /// THIS IS NOT THE SERVICE'S OWN TOKEN-SIGNING KEY AND CANNOT BE MADE TO NAME IT. The reference is
    /// resolved against the configured key store, which is a different setting from the issuer's
    /// signing material; no member of this schema addresses the latter.
    /// </remarks>
    [Required]
    [JsonPropertyName("keyRef")]
    public string? KeyRef { get; init; }

    /// <summary>The hash selector, from the five published members a signature applies to.</summary>
    [Required]
    [JsonPropertyName("hashType")]
    public long? HashType { get; init; }
}

/// <summary>
/// A request to verify an RSA signature over a payload.
/// </summary>
/// <remarks>
/// Binds the two <c>VerifyRSASign</c> declarations at
/// [ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L72-L73], in which the SIGNATURE parameter is text in one
/// and binary in the other. Both are reachable: the payload selector governs the signature's form,
/// mirroring the signing operation exactly, so a signature produced by one form verifies under the same
/// form.
/// </remarks>
public sealed record RsaVerifyRequest
{
    /// <summary>The payload the signature covers, interpreted according to <see cref="PayloadForm"/>.</summary>
    [Required]
    [JsonPropertyName("data")]
    public string? Data { get; init; }

    /// <summary>The overload-family selector, which governs BOTH the payload's and the signature's form.</summary>
    [Required]
    [JsonPropertyName("payloadForm")]
    public PayloadForm? PayloadForm { get; init; }

    /// <summary>
    /// The signature to verify, in the same form as the payload.
    /// </summary>
    /// <remarks>
    /// A SIGNATURE IS NOT KEY MATERIAL AND NOT A CREDENTIAL, so it is a legitimate inbound value and
    /// stands here as an ordinary member rather than behind a reference. It is nevertheless never echoed
    /// in a response or a log record, because it is caller data with no place in either.
    /// </remarks>
    [Required]
    [JsonPropertyName("signature")]
    public string? Signature { get; init; }

    /// <summary>An opaque handle to the verification key.</summary>
    [Required]
    [JsonPropertyName("keyRef")]
    public string? KeyRef { get; init; }

    /// <summary>The hash selector, from the five published members a signature applies to.</summary>
    [Required]
    [JsonPropertyName("hashType")]
    public long? HashType { get; init; }
}

/// <summary>
/// A request to generate an RSA key pair.
/// </summary>
/// <remarks>
/// <para>
/// Binds the two <c>GenRSAKey</c> declarations at
/// [ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L19-L20], which differ in whether the emitted text is
/// wrapped in the textual envelope. The legacy hands BOTH halves back through <c>ref</c> parameters;
/// the published contract returns the public half and a reference to the private one instead.
/// </para>
/// <para>
/// KNOWN LEGACY WEAKNESS - 1024 BITS REMAINS A LEGAL SIZE. It is one of the three convenience constants
/// the catalogue publishes [enums.sru:L965-L967] and the oracle's own demonstration uses it, so this
/// contract ACCEPTS it and annotates the weakness rather than refusing it. No minimum is enforced.
/// </para>
/// </remarks>
public sealed record GenRsaKeyRequest
{
    /// <summary>
    /// The modulus length in bits.
    /// </summary>
    /// <remarks>
    /// The domain is that of the legacy argument's own 16-bit unsigned type, and the three published
    /// convenience values sit inside it. IT IS NOT A CRYPTOGRAPHIC BOUND: a value the platform will not
    /// generate is refused by the platform, which is asked rather than second-guessed from a table.
    /// </remarks>
    [Required]
    [JsonPropertyName("bits")]
    public long? Bits { get; init; }

    /// <summary>
    /// Whether the emitted public key carries the textual envelope, or absent for the overload that
    /// takes no such argument.
    /// </summary>
    /// <remarks>
    /// ABSENCE IS MEANINGFUL AND IS NOT THE SAME AS FALSE. Omitting it reaches the three-argument legacy
    /// declaration [n_crypto.sru:L19] and supplying it reaches the four-argument one [<c>:L20</c>], so a
    /// default here would make one of the two unreachable.
    /// </remarks>
    [JsonPropertyName("pemFormat")]
    public bool? PemFormat { get; init; }
}

/// <summary>
/// A request for random bytes.
/// </summary>
/// <remarks>
/// Binds <c>GenRandomBlob</c> at [ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L14], the single
/// declaration of this operation, whose return is an unconditional blob - so there is no form selector.
/// </remarks>
public sealed record RandomBlobRequest
{
    /// <summary>
    /// The number of bytes to draw.
    /// </summary>
    /// <remarks>
    /// The published cap is a SERVICE-LEVEL bound well below the legacy argument's own domain, and a
    /// request above it is refused rather than truncated: short random material is the one outcome a
    /// caller cannot detect, and it is precisely the defect that survives every test and fails in
    /// production.
    /// </remarks>
    [Required]
    [JsonPropertyName("size")]
    public long? Size { get; init; }
}

/// <summary>
/// A request for a random string.
/// </summary>
/// <remarks>
/// Binds both declarations at [ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L15-L16]. OMITTING THE FLAGS
/// REACHES THE ONE-ARGUMENT DECLARATION, which applies the preserved default of digits and letters
/// [enums.sru:L957] - the symbol class is DELIBERATELY EXCLUDED from that default, and this contract
/// does not widen it.
/// </remarks>
public sealed record RndStringRequest
{
    /// <summary>The number of characters to draw. See <see cref="RandomBlobRequest.Size"/>.</summary>
    [Required]
    [JsonPropertyName("size")]
    public long? Size { get; init; }

    /// <summary>
    /// The character-class bitmask, or absent for the preserved default.
    /// </summary>
    /// <remarks>
    /// ABSENCE SELECTS THE ONE-ARGUMENT DECLARATION rather than being rewritten into an explicit
    /// default, so the default is applied by the provider that owns it and is not re-spelled at this
    /// boundary. Bits the catalogue does not name are accepted inside the declared domain, because the
    /// legacy defines no behaviour for them and refusing them would narrow a contract it leaves open.
    /// </remarks>
    [JsonPropertyName("flags")]
    public long? Flags { get; init; }
}

/// <summary>
/// A request for a globally unique identifier.
/// </summary>
/// <remarks>
/// Binds both declarations at [ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L17-L18], whose legacy
/// spelling of the operation differs in case from this contract's. THE REQUEST BODY IS OPTIONAL, which
/// is how the no-argument declaration is reached without inventing a member; the flags govern FORMATTING
/// only, and the preserved default carries both the braces and the separators [enums.sru:L962].
/// </remarks>
public sealed record GuidRequest
{
    /// <summary>
    /// The formatting bitmask, or absent for the preserved default.
    /// </summary>
    /// <remarks>
    /// Absence - whether by omitting the member or by sending no body at all - selects the no-argument
    /// declaration, so the preserved default is applied by the provider rather than re-spelled here.
    /// </remarks>
    [JsonPropertyName("flags")]
    public long? Flags { get; init; }
}

/// <summary>
/// A request to decode an encoded string into bytes.
/// </summary>
/// <remarks>
/// Binds <c>StringToBlob</c> at [ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L11]. THERE IS NO DEFAULT
/// ENCODING: the legacy declaration takes the argument, so a caller always states it and an absent
/// member is a rejection rather than a silent choice.
/// </remarks>
public sealed record StringToBlobRequest
{
    /// <summary>The encoded text to decode.</summary>
    [Required]
    [JsonPropertyName("data")]
    public string? Data { get; init; }

    /// <summary>
    /// The encoding selector, from the two published members [enums.sru:L924-L925].
    /// </summary>
    /// <remarks>
    /// This is the LEGACY encoding argument and is not the JSON transport encoding. The two answer
    /// different questions, and the response carries its bytes as base64 whichever value is chosen here.
    /// </remarks>
    [Required]
    [JsonPropertyName("encoding")]
    public long? Encoding { get; init; }
}

/// <summary>
/// A request to encode bytes into a string.
/// </summary>
/// <remarks>
/// Binds <c>BlobToString</c> at [ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L12], the exact inverse of
/// the decode operation. A caller asking for hexadecimal SUBMITS BASE64 AND RECEIVES HEXADECIMAL, which
/// is correct rather than contradictory: the inbound base64 is JSON transport and the selector governs
/// the outbound form.
/// </remarks>
public sealed record BlobToStringRequest
{
    /// <summary>The bytes to encode, carried as base64 for JSON transport.</summary>
    [Required]
    [JsonPropertyName("data")]
    public string? Data { get; init; }

    /// <summary>The encoding selector. See <see cref="StringToBlobRequest.Encoding"/>.</summary>
    [Required]
    [JsonPropertyName("encoding")]
    public long? Encoding { get; init; }
}

/// <summary>
/// A request to reverse the byte order of a payload.
/// </summary>
/// <remarks>
/// Binds <c>BlobReverse</c> at [ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L13], the ONLY member of the
/// legacy surface that mutates its argument. In-place mutation has no wire representation, so the
/// operation is projected as an ordinary request-in, response-out pair and the response carries both the
/// reversed bytes and the boolean the legacy actually returned.
/// </remarks>
public sealed record BlobReverseRequest
{
    /// <summary>The bytes to reverse, carried as base64 for JSON transport.</summary>
    [Required]
    [JsonPropertyName("data")]
    public string? Data { get; init; }
}

/// <summary>
/// A digest, rendered as printable text.
/// </summary>
/// <param name="Digest">The digest.</param>
/// <remarks>
/// THE DIGEST IS TEXT FOR EVERY LEGACY OVERLOAD, whatever the payload's form: both unkeyed declarations
/// [ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L21-L22], all four keyed ones [<c>:L23-L26</c>] and all
/// three file ones [<c>:L27-L29</c>] return a string. This response therefore carries NO payload-form
/// member, and that asymmetry against the payload-bearing responses is the oracle's own rather than an
/// oversight.
/// </remarks>
public sealed record DigestResponse(
    [property: JsonPropertyName("digest")] string Digest);

/// <summary>
/// A payload, in the same form the request declared.
/// </summary>
/// <param name="PayloadForm">The form of <paramref name="Data"/>, echoing the request's selector.</param>
/// <param name="Data">The payload, interpreted according to <paramref name="PayloadForm"/>.</param>
/// <remarks>
/// The form is ECHOED rather than inferred, so a caller reading the response alone knows how to
/// interpret the payload without re-deriving it from what it sent.
/// </remarks>
public sealed record PayloadResponse(
    [property: JsonPropertyName("payloadForm")] PayloadForm PayloadForm,
    [property: JsonPropertyName("data")] string Data);

/// <summary>
/// The outcome of a signature verification.
/// </summary>
/// <param name="Valid">Whether the signature verified.</param>
/// <remarks>
/// A FAILED VERIFICATION IS A 200 CARRYING FALSE, not an error status. The legacy returns a boolean
/// [ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L72-L73], so an invalid signature is an ANSWER rather
/// than a fault, and mapping it onto a failure status would change the observable behaviour of the
/// operation.
/// </remarks>
public sealed record RsaVerifyResponse(
    [property: JsonPropertyName("valid")] bool Valid);

/// <summary>
/// A generated RSA key pair, of which only the public half is returned.
/// </summary>
/// <param name="PublicKey">The generated public key, as text.</param>
/// <param name="KeyRef">An opaque handle to the retained private half.</param>
/// <param name="Bits">The modulus length that was generated.</param>
/// <remarks>
/// <para>
/// <b>THE PRIVATE HALF IS NOT HERE, AND THERE IS NO MEMBER FOR IT.</b> This is the highest-severity
/// outbound decision on the whole surface. The legacy returns the private key as text through a
/// <c>ref</c> parameter [ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L19-L20]; this contract retains it
/// and returns a reference, so the private half never enters a response body, a log record, an error
/// body or a document example.
/// </para>
/// <para>
/// The returned reference can never name this service's own signing key or an existing store entry: the
/// retained value is what the provider just generated, so it is not read from configuration at all.
/// </para>
/// </remarks>
public sealed record GenRsaKeyResponse(
    [property: JsonPropertyName("publicKey")] string PublicKey,
    [property: JsonPropertyName("keyRef")] string KeyRef,
    [property: JsonPropertyName("bits")] long Bits)
{
    /// <summary>
    /// Whether the public key carries the textual envelope, echoed only when the request stated it.
    /// </summary>
    /// <remarks>
    /// Echoed as ABSENT when the request omitted it, preserving the distinction between the two legacy
    /// declarations in the response as well as in the request.
    /// </remarks>
    [JsonPropertyName("pemFormat")]
    public bool? PemFormat { get; init; }
}

/// <summary>
/// Bytes, carried as base64 for JSON transport.
/// </summary>
/// <param name="Data">The bytes, as base64.</param>
/// <remarks>
/// THE BASE64 HERE IS TRANSPORT AND IS NEVER THE LEGACY ENCODING ARGUMENT, which is why the decode
/// operation returns its bytes through this shape even when the caller asked it to interpret
/// hexadecimal.
/// </remarks>
public sealed record BlobResponse(
    [property: JsonPropertyName("data")] string Data);

/// <summary>
/// A generated random string.
/// </summary>
/// <param name="Value">The generated string.</param>
public sealed record RndStringResponse(
    [property: JsonPropertyName("value")] string Value);

/// <summary>
/// A generated globally unique identifier, in the requested textual form.
/// </summary>
/// <param name="Value">The identifier.</param>
public sealed record GuidResponse(
    [property: JsonPropertyName("value")] string Value);

/// <summary>
/// Text produced by an encoding conversion.
/// </summary>
/// <param name="Value">The encoded text, in the form the request's selector chose.</param>
public sealed record EncodedTextResponse(
    [property: JsonPropertyName("value")] string Value);

/// <summary>
/// The result of a byte-order reversal.
/// </summary>
/// <param name="Data">The reversed bytes, as base64.</param>
/// <param name="Succeeded">The boolean the legacy operation itself returned.</param>
/// <remarks>
/// BOTH HALVES OF THE OUTCOME ARE CARRIED, because the legacy split them: the bytes were left in the
/// caller's own variable through a <c>ref</c> parameter, and the boolean was the actual return value
/// [ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L13]. Dropping either would lose information the legacy
/// gave its caller.
/// </remarks>
public sealed record BlobReverseResponse(
    [property: JsonPropertyName("data")] string Data,
    [property: JsonPropertyName("succeeded")] bool Succeeded);

/// <summary>
/// The authorization policy all 18 cryptographic operations of contract C-02 are served under: who may
/// call, and what they must hold.
/// </summary>
/// <remarks>
/// <para>
/// WHY THIS EXISTS. Requiring an authenticated principal AND NOTHING MORE would let any holder of any token
/// minted for this service's audience drive keyed HMAC, symmetric encryption and decryption, RSA signing and
/// RSA key generation - regardless of what the credential was obtained for and regardless of which caller it
/// was minted for. Paired with an issuer that granted every requested scope to any caller it authenticated,
/// a credential obtained for one purpose would reach the whole cryptographic surface (CWE-862, CWE-863).
/// This policy is what makes neither of those true.
/// </para>
/// <para>
/// THE SCOPE NAME IS NOT INVENTED HERE. DataServices requests exactly <c>security.crypto</c> for this edge,
/// and contract C-02 states who the surface is for: Security serves DataServices. Both halves are enforced
/// because either alone leaves a hole - scope without subject admits any caller the issuer serves as long as
/// it holds the scope, and subject without scope lets the permitted caller reach the surface with a
/// credential obtained for something else entirely.
/// </para>
/// <para>
/// A SCOPE CLAIM IS SPACE-DELIMITED AND MUST BE SPLIT, which is why this is an assertion rather than
/// <c>RequireClaim</c>: RFC 6749 carries the granted set as ONE claim holding a space-delimited list, so a
/// claim-equality requirement would demand a token whose entire scope claim is that single value.
/// </para>
/// <para>
/// THE PERMITTED-CALLER ROSTER IS THE ISSUANCE ROSTER READ FROM THE OTHER SIDE. It is derived from the
/// deployment's grant matrix - <c>Security:CallerAuthorizations</c> and <c>Security:Callers</c>, whose UNION
/// is what the issuer enforces - by asking whether that matrix would have minted THIS caller THIS scope for
/// the audience the caller is presenting. So the deployment states the topology ONCE and both the issuing
/// decision and the receiving decision follow from it. A second, independently maintained list would be a
/// second source of truth able to drift from the first, and a drift in this direction is silent: the issuer
/// would mint a credential the receiver refuses. Reading only ONE of the two shapes is that same drift in
/// miniature, which is why both are consulted.
/// </para>
/// <para>
/// NOTHING HERE IS EXEMPT AND NOTHING IS CONFIGURABLE AWAY. There is no environment test, no anonymous
/// fallback and no switch: a bypass that exists only in development is still a bypass, and it would make the
/// local build disagree with the published contract.
/// </para>
/// </remarks>
internal static class CryptoCallerAuthorization
{
    /// <summary>The scope contract C-02 is served under.</summary>
    internal const string Scope = "security.crypto";

    /// <summary>The policy name the cryptographic group is mapped under.</summary>
    internal const string PolicyName = "security:crypto";

    /// <summary>The claim RFC 6749 carries the granted scope set in.</summary>
    private const string ScopeClaimName = "scope";

    /// <summary>The claim the issuer stamps the caller identity into.</summary>
    private const string SubjectClaimName = "sub";

    /// <summary>The claim the issuer stamps the addressed audience into.</summary>
    private const string AudienceClaimName = "aud";

    /// <summary>
    /// Requires an authenticated caller that this service's own issuance roster admits for its own
    /// audience, and that holds the cryptographic scope.
    /// </summary>
    /// <param name="builder">The policy being built.</param>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    internal static void Configure(AuthorizationPolicyBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        _ = builder
            .RequireAuthenticatedUser()
            .RequireAssertion(static context =>
                HoldsScope(context.User) && IsPermittedCaller(context.User, context.Resource));
    }

    /// <summary>
    /// Reports whether the principal's scope claim carries the cryptographic scope.
    /// </summary>
    /// <param name="user">The established principal.</param>
    /// <returns><see langword="true"/> when the scope is present.</returns>
    /// <remarks>
    /// Every scope claim is split, not just the first, because a token may legitimately carry the set as
    /// several claims and a check reading only the first would refuse a credential the issuer considers
    /// sufficient. Comparison is ORDINAL, because RFC 6749 scope tokens are case-sensitive.
    /// </remarks>
    private static bool HoldsScope(ClaimsPrincipal user)
    {
        foreach (Claim claim in user.FindAll(ScopeClaimName))
        {
            foreach (string granted in claim.Value.Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (string.Equals(granted, Scope, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Reports whether this service's issuance roster grants the principal's subject the cryptographic
    /// scope for the audience the presented token was minted for.
    /// </summary>
    /// <param name="user">The established principal.</param>
    /// <param name="resource">The authorization resource, which is the HTTP context for an endpoint.</param>
    /// <returns><see langword="true"/> when the caller is permitted.</returns>
    /// <remarks>
    /// <para>
    /// AN UNRESOLVABLE ROSTER REFUSES. The resource is the HTTP context for every endpoint this service
    /// maps, so the options are always reachable in production; a resource of another shape can only mean
    /// the policy was evaluated outside a request, and refusing is the correct answer there rather than
    /// admitting a caller whose permissions could not be read.
    /// </para>
    /// <para>
    /// THE AUDIENCE COMES FROM THE TOKEN, NOT FROM A CONFIGURATION KEY, and that is a deliberate choice
    /// with two consequences. First, the bearer handler has ALREADY established that the presented
    /// audience is one this service accepts - re-reading the declared identity here would either restate
    /// that check or, when a deployment relies on the documented roster fallback and declares no inbound
    /// identity of its own, silently refuse the entire cryptographic surface while the authenticated ping
    /// route kept answering. Second, it makes the question exact: not "is this caller known?" but "would
    /// this service have minted THIS caller THIS scope for THE AUDIENCE it is presenting?" - which is the
    /// issuance decision read from the receiving side, per (caller, audience) pair rather than per caller.
    /// </para>
    /// <para>
    /// THE GRANT'S OWN SCOPE LIST IS CONSULTED AS WELL AS THE CLAIM. The claim check proves the credential
    /// carries the scope; this proves the deployment ever permitted that pairing. They are not redundant:
    /// a token minted before a grant was narrowed still carries what it was granted then, and the roster
    /// is what the deployment means now.
    /// </para>
    /// </remarks>
    private static bool IsPermittedCaller(ClaimsPrincipal user, object? resource)
    {
        if (resource is not HttpContext http)
        {
            return false;
        }

        string? subject = user.FindFirst(SubjectClaimName)?.Value
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrEmpty(subject))
        {
            return false;
        }

        SecurityOptions configured = http.RequestServices
            .GetRequiredService<IOptions<SecurityOptions>>()
            .Value;

        // ------------------------------------------------------------------------------------------
        // BOTH AUTHORING SHAPES ARE CONSULTED, BECAUSE THE ISSUER ENFORCES THEIR UNION.
        //
        // `Security:Callers` nests each caller's grants under its identity and `Security:CallerAuthorizations`
        // states one flat (caller, audience, scopes) row at a time; TokenIssuer folds the two into ONE matrix
        // and decides from that, so a deployment may author either or both. Reading only the nested shape -
        // the tempting simplification - refuses the ENTIRE cryptographic surface for every caller of any
        // deployment that states its matrix in flat rows, while that same deployment's issuer happily mints the
        // credential being refused. That is the worst shape this drift can take: the issuing side and the
        // receiving side disagree, the mint succeeds, the call is refused, and nothing names the cause.
        //
        // THE MATCH IS EXACT AND PER PAIR IN BOTH SHAPES. Subject compared ordinally against the row's
        // caller, audience against an audience claim the token actually carries, and the cryptographic
        // scope against the row's own permitted set - so the question stays "would this service have minted
        // THIS caller THIS scope for THE AUDIENCE it is presenting".
        // ------------------------------------------------------------------------------------------
        foreach (SecurityCallerOptions caller in configured.Callers)
        {
            if (caller is null || !string.Equals(caller.Identity?.Trim(), subject, StringComparison.Ordinal))
            {
                continue;
            }

            foreach (SecurityCallerGrantOptions grant in caller.Grants)
            {
                if (grant is not null && GrantAdmits(grant.Audience, grant.Scopes, user))
                {
                    return true;
                }
            }
        }

        foreach (CallerAuthorizationOptions row in configured.CallerAuthorizations)
        {
            if (row is null || !string.Equals(row.Caller?.Trim(), subject, StringComparison.Ordinal))
            {
                continue;
            }

            if (GrantAdmits(row.Audience, row.Scopes, user))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Reports whether one grant covers both an audience the principal presents and the cryptographic
    /// scope.
    /// </summary>
    /// <param name="grantedAudience">The audience the grant under consideration permits.</param>
    /// <param name="grantedScopes">The scopes that grant permits.</param>
    /// <param name="user">The established principal.</param>
    /// <returns><see langword="true"/> when the grant covers both.</returns>
    /// <remarks>
    /// Every audience claim is examined, because a token addressed to more than one audience carries one
    /// claim per member. Comparison is ORDINAL throughout: an audience is an opaque protocol identifier
    /// and a scope token is case-sensitive by RFC 6749, so folding either by case would admit a spelling
    /// the deployment never wrote.
    /// </remarks>
    private static bool GrantAdmits(
        string? grantedAudience,
        IList<string> grantedScopes,
        ClaimsPrincipal user)
    {
        string granted = grantedAudience?.Trim() ?? string.Empty;

        if (granted.Length == 0)
        {
            return false;
        }

        bool addressed = false;

        foreach (Claim claim in user.FindAll(AudienceClaimName))
        {
            if (string.Equals(claim.Value.Trim(), granted, StringComparison.Ordinal))
            {
                addressed = true;
                break;
            }
        }

        if (!addressed)
        {
            return false;
        }

        foreach (string scope in grantedScopes)
        {
            if (string.Equals(scope?.Trim(), Scope, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// One retained generated private key: the material, the caller it is charged to, and when it stops
/// resolving.
/// </summary>
/// <param name="PrivateKey">The generated private key, in the form the provider emitted it.</param>
/// <param name="Owner">The caller identity the slot is charged to - the token's subject.</param>
/// <param name="ExpiresAt">The instant after which the reference no longer resolves.</param>
/// <remarks>
/// <para>
/// A RECORD WITH NO CUSTOM RENDERING IS DELIBERATE HERE, AND SO IS THE FACT THAT IT IS INTERNAL. A
/// positional record synthesizes a <c>ToString</c> that would print every member - including the private
/// key - into any log or diagnostic that stringified it. That is acceptable ONLY because this type never
/// leaves the resolver: it is internal, it is never returned from a public member, it is never bound to a
/// response and it is never logged. Nothing in this file interpolates an instance of it.
/// </para>
/// <para>
/// The owner is carried on the ENTRY rather than only in the per-owner counter, because a release must
/// verify ownership before removing - and a counter cannot answer "whose is this?".
/// </para>
/// </remarks>
internal sealed record RetainedGeneratedKey(string PrivateKey, string Owner, DateTimeOffset ExpiresAt);

/// <summary>
/// One reserved slot in the retained generated-key store: the reference the slot will answer to and the
/// caller it is charged against.
/// </summary>
/// <param name="Reference">The opaque reference the committed key will resolve under.</param>
/// <param name="Owner">The caller identity whose per-caller ceiling the slot counts against.</param>
/// <remarks>
/// RESERVED BEFORE THE KEY EXISTS, which is the point of separating the reservation from the key. RSA
/// generation is the expensive step, so the capacity claim is made first and given back on every path that
/// does not commit - a refusal then costs nothing, and the cap cannot be overshot by concurrent callers
/// whose generations were already in flight.
/// </remarks>
internal readonly record struct GeneratedKeyReservation(string Reference, string Owner)
{
    /// <summary>The absence of a reservation.</summary>
    public static GeneratedKeyReservation None { get; } = new(string.Empty, string.Empty);

    /// <summary>Whether this value names a reserved slot.</summary>
    public bool IsReserved => Reference.Length > 0;
}
