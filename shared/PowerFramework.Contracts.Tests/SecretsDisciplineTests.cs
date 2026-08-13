// ==================================================================================================
//  SecretsDisciplineTests - SECRETS DISCIPLINE, ASSERTED IN THE SHAPE OF THE PUBLISHED CONTRACTS
//  ------------------------------------------------------------------------------------------------
//  SUBJECT   Proto/persistence.v1.proto   (C-05..C-08, and the transaction descriptor)
//            Proto/common.v1.proto        (the shared vocabulary, and DbError)
//            Proto/dataservices.v1.proto  (C-03, C-04 - swept because it relays both of the above)
//            OpenApi/security.v1.yaml     (C-01 TokenService, C-02 CryptoService)
//
//  ONE OBLIGATION IN THREE PARTS, WHICH IS WHY THEY SHARE A FILE RATHER THAN THREE
//  ------------------------------------------------------------------------------------------------
//  The requirement is a single control expressed across both contract media:
//
//      1. the transaction descriptor's password field is marked write-only in the contract metadata
//         and never appears in any response message;
//      2. the crypto contract takes an opaque key reference and has NO field carrying raw key
//         material inbound;
//      3. the database-error message's statement field is documented as redacted or
//         parameter-separated.
//
//  Splitting those across three files by medium would hide what they have in common: each is the
//  same question - CAN A SECRET LEAVE, OR ARRIVE, THROUGH A SHAPE THIS CONTRACT PUBLISHES? - asked
//  of a different part of the boundary. Answering it in one place is what makes the answer auditable.
//
//  ================= THE ONE RULE FOR ANYONE EDITING THIS FILE (C-F) ==========================
//  THERE ARE TWO WAYS TO FAIL THIS OBLIGATION: LET A SECRET THROUGH THE CONTRACT, OR WRITE ONE INTO
//  THE TEST. The second is the easier mistake and the more embarrassing one, because it commits the
//  very material the control exists to keep out of version control, in the file that claims to
//  prevent it.
//
//  So EVERY assertion here is STRUCTURAL - field presence, field absence, field ordering, reserved
//  slots, reference-typed schemas, documented annotations. Nothing in this file is a value. A reader
//  will find only field names, vocabulary lists and legacy locators: no PEM header or footer, no
//  base64-DER fragment, no broker host, no account name, no password, no configuration key, no
//  certificate, no token. IF YOU FIND YOURSELF WANTING A SAMPLE KEY TO TEST WITH, YOU ARE WRITING THE
//  WRONG KIND OF TEST - a shape assertion needs no specimen.
//
//  The repository-wide sweep found EIGHT in-source secret sites plus three inside vendored binaries,
//  more than double the three the requirements named as a floor. All eight live in the READ-ONLY
//  legacy tree (C-C), and six of them sit in its test and demo regions, so the remediation posture is
//  "never replicate, document, and rotate" rather than "edit the offending file". docs/SECRETS.md
//  carries every locator, its severity and its required action, and reproduces no value. THIS FILE
//  READS NO LEGACY FILE AT RUNTIME AT ALL: the legacy locators below appear only as citations in
//  comments, exactly as they do throughout this project.
//  ==========================================================================================
//
//  THE LEGACY EVIDENCE, VERIFIED LINE BY LINE (C-K - every assertion cites its locator)
//  ------------------------------------------------------------------------------------------------
//  ws_objects/pfw.thread.ext.pbl.src/transactiondata.srs declares EXACTLY NINE fields in this order
//  at [:L4-L12]: dbms, servername, database, logid, LOGPASS, dbparm, lock, autocommit, userparm.
//  `logpass` is the login password, and it sits FIFTH. C-08 and AAP 0.4.2.6 require it be write-only:
//  supplied on a request, never echoed on a response, never written to a log. The legacy genuinely
//  round-trips it - `of_gettransdata` copies the whole structure including the password - so a
//  field-for-field port of the getter would have echoed a password on every session read.
//
//  ws_objects/pfw.thread.ext.pbl.src/dberrordata.srs declares EXACTLY FIVE fields at [:L4-L8]:
//  sqldbcode, sqlerrtext, SQLSYNTAX, buffer, row. The legacy places the COMPLETE GENERATED STATEMENT,
//  INCLUDING INTERPOLATED LITERAL VALUES, into `sqlsyntax`, and the legacy logger performs no
//  redaction of any kind (AAP 0.6.3.8, 0.6.4). The MECHANICAL ROOT is at
//  ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru:L128-L129, which parses the
//  connection parameter string for the bind-disabling flag: DisableBind=1 MEANS THE RUNTIME DOES NOT
//  USE BIND VARIABLES, so values are interpolated into the statement text as literals. That is why
//  the field contains data at all, and why a field that merely echoed it would leak whatever the
//  failing row held.
//
//  ws_objects/pfw.crypto.pbl.src/n_crypto.sru passes RAW KEY MATERIAL DIRECTLY, as an ordinary
//  in-parameter, throughout: the four keyed `Hash` overloads take `key` as string or blob [:L23-L26];
//  the sixteen `SymEncrypt` and sixteen `SymDecrypt` overloads take `key` and `iv` as string or blob
//  [:L30-L61]; `RSAEncrypt` and `RSADecrypt` take `pubkey` and `prikey` as strings [:L62-L69];
//  `RSASign` and `VerifyRSASign` likewise [:L70-L73]; and `GenRSAKey` hands the PRIVATE key back
//  through a `ref string` [:L19-L20]. In process that is unremarkable. On a wire the same signatures
//  would place key material into request bodies, into any log that recorded a request, and into every
//  characterization recording of one. C-02 (AAP 0.4.3) therefore narrows it deliberately: "raw key
//  material never crosses the wire from a caller - callers pass an opaque `keyRef` that Security
//  resolves against its configured key store."
//
//  WHAT THIS FILE ASSERTS THAT PROSE CANNOT
//  ------------------------------------------------------------------------------------------------
//  A comment saying "servers must blank this field on the way out" is not a control: it asks every
//  implementation, forever, in every code path, to remember something whose omission is silent. The
//  contracts express these rules structurally instead - two message shapes rather than one flag,
//  permanently reserved slots, reference-typed schemas, closed property sets. This file is where
//  those structural choices stop being design intent and become a build failure if they regress.
//
//  THE LOAD-BEARING ASSERTION IS THE SWEEP, NOT THE SPOT CHECK. Asserting that one named response
//  omits the password proves nothing about the seventy-six other responses, and a credential field
//  four levels down a response tree is exactly the kind that arrives unnoticed. Every sweep here is
//  therefore TRANSITIVE with a cycle guard, walks EVERY method of EVERY service, and reports a
//  violation with the full path - Method -> Response -> ... -> field - so a failure is actionable in
//  one read rather than being a starting point for an investigation.
//
//  TWO MEASURED TOOLCHAIN FACTS THAT SHAPE HOW THE ASSERTIONS ARE WRITTEN
//  ------------------------------------------------------------------------------------------------
//  1. FieldDescriptor.Declaration IS NULL FOR EVERY FIELD IN THIS BUILD. Grpc.Tools does not pass
//     protoc's source-info option, so the compiled descriptors carry no SourceCodeInfo and LEADING
//     COMMENTS ARE NOT REACHABLE THROUGH DESCRIPTOR REFLECTION. Measured on the pinned SDK with
//     Google.Protobuf 3.31.1, not assumed. Documentation-level markers are consequently asserted
//     against the AUTHORED PROTOCOL DEFINITION TEXT, which is the artifact that actually carries
//     them.
//  2. MessageDescriptor.ToProto() DOES expose `reserved` declarations, through
//     DescriptorProto.ReservedRange and .ReservedName. That is a MACHINE-READABLE marker in the
//     contract metadata and it is strictly stronger than a comment: reserving both the number and the
//     name makes it a COMPILE ERROR for a future edit to reintroduce the field or to reuse its slot.
//     It is therefore the primary write-only marker asserted here, with the authored comment as the
//     corroborating half.
//
//  PURITY, AND WHY IT MATTERS HERE SPECIFICALLY
//  ------------------------------------------------------------------------------------------------
//  Descriptor reflection, the shared OpenAPI fixture, and strictly read-only reads of the authored
//  contract files. No network, no clock, no randomness, no environment variable, no process, and no
//  write, move or delete of anything. Repeatability is the one hard prerequisite of the Golden-Master
//  approach this repository adopts (AAP 0.6.7); for an auditable security control it is also the
//  difference between a guarantee and an anecdote.
//
//  NO CRYPTOGRAPHY IS PERFORMED (C-A). Nothing here hashes, encrypts, decrypts, signs, verifies,
//  generates a key or mints a token. This file reasons about the SHAPE of the crypto boundary and
//  never exercises it - that is PowerFramework.Security.Tests' subject, on the other side of the
//  contract.
//
// ==================================================================================================

using System.Text;
using Google.Protobuf.Reflection;
using Microsoft.OpenApi;
using Xunit;
using Xunit.Sdk;

namespace PowerFramework.Contracts.Tests;

/// <summary>
/// Asserts the secrets-discipline properties of the published cross-service contracts: that the
/// transaction descriptor's password is write-only and unreachable from any response, that the crypto
/// boundary accepts an opaque key reference and no raw key material inbound, and that the
/// database-error statement field is documented as redacted.
/// </summary>
/// <remarks>
/// <para>
/// Every assertion is structural. No secret value, specimen key, sample certificate or example token
/// appears anywhere in this file, which is constraint C-F applied to the test rather than only to the
/// implementation.
/// </para>
/// <para>
/// The OpenAPI half of the suite reaches its subject through the assembly-scoped
/// <see cref="OpenApiContractDocuments"/> fixture, obtained by the constructor parameter below, which
/// is the one convention for this folder. The Protobuf half reaches its subject through
/// <see cref="ContractDescriptors"/>. Documentation-level markers are read from the authored contract
/// files on disk, because the compiled descriptors carry no comments - see the header of this file.
/// </para>
/// </remarks>
/// <param name="documents">
/// The assembly-scoped fixture holding the two parsed OpenAPI contract documents.
/// </param>
public sealed class SecretsDisciplineTests(OpenApiContractDocuments documents)
{
    // ==============================================================================================
    //  THE VOCABULARIES
    //
    //  Each list below is a DELIBERATE, AUDITABLE enumeration rather than a regular expression, so a
    //  reviewer can see exactly what coverage is claimed and add to it without re-deriving anything.
    //  Matching is described beside each list; all of it is case-insensitive and ordinal, over names
    //  first COMPACTED by removing the separators that distinguish snake_case from camelCase from
    //  kebab-case (see Compact). Compaction is what lets one list cover `logpass`, `log_pass` and
    //  `logPass` at once.
    //
    //  NOTE THAT EVERY ENTRY IS A FIELD-NAME FRAGMENT. Not one of them is a value, and none of them
    //  is derived from any of the eight in-source secret sites (C-F).
    // ==============================================================================================

    /// <summary>
    /// Name fragments that indicate a field carries a credential. Matched as a compacted,
    /// case-insensitive SUBSTRING, so <c>logpass</c>, <c>logPassword</c> and <c>db_pwd</c> all match.
    /// </summary>
    /// <remarks>
    /// <c>pass</c> is included even though <c>password</c> subsumes it, because the legacy's own
    /// spelling is <c>logpass</c> [transactiondata.srs:L8] rather than <c>logpassword</c>, and a port
    /// that shortened a name further would still be caught. Coverage is deliberately broad here: a
    /// false positive costs one review and a rename, while a false negative costs a leaked credential.
    /// </remarks>
    private static readonly string[] CredentialNameFragments =
    [
        "password",
        "pass",
        "pwd",
        "secret",
        "credential",
        "logpass",
    ];

    /// <summary>
    /// Name fragments that indicate RAW KEY MATERIAL - the thing C-02 forbids a caller from sending.
    /// Matched as a compacted, case-insensitive SUBSTRING.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The list is drawn from the legacy parameter spellings so that a mechanical port of any
    /// <c>n_crypto</c> signature would be caught by it: <c>prikey</c> and <c>pubkey</c> are the
    /// legacy's own names [n_crypto.sru:L19-L20, :L62-L73], and the remainder covers the forms a .NET
    /// author would reach for instead.
    /// </para>
    /// <para>
    /// <c>pem</c> ALONE IS DELIBERATELY ABSENT and the compound spellings are used instead. The
    /// contract carries a legitimate boolean named <c>pemFormat</c>, an output-format selector rather
    /// than key material, and a bare <c>pem</c> fragment would flag it. That exception is not
    /// swallowed silently: it is asserted on its own terms by
    /// <see cref="TheOnlyPemNamedPropertyIsABooleanFormatSelectorAndNeverKeyText"/>.
    /// </para>
    /// </remarks>
    private static readonly string[] RawKeyMaterialNameFragments =
    [
        "privatekey",
        "prikey",
        "keymaterial",
        "keybytes",
        "keyblob",
        "rawkey",
        "secretkey",
        "symmetrickey",
        "sharedsecret",
        "clientsecret",
        "passphrase",
        "pkcs8",
        "pkcs12",
        "pemkey",
        "keypem",
        "certificate",
    ];

    /// <summary>
    /// Name fragments that indicate a property carries a KEY, of any kind, in any direction. Matched
    /// as a compacted, case-insensitive SUBSTRING.
    /// </summary>
    /// <remarks>
    /// This list drives the DIRECTION rule rather than a prohibition: a key in a RESPONSE can be
    /// legitimate - RSA key generation exists to produce one, and the legacy hands the private key back
    /// through a <c>ref string</c> [n_crypto.sru:L19-L20] - while the same name in a REQUEST is exactly
    /// what C-02 forbids. A name matching here is treated as key material only when it is NOT a
    /// reference form; see <see cref="IsOpaqueReferenceName"/>.
    /// </remarks>
    private static readonly string[] KeyMaterialNameFragments =
    [
        "publickey",
        "privatekey",
        "pubkey",
        "prikey",
        "keypair",
        "keymaterial",
    ];

    /// <summary>
    /// Property names that are forbidden on a request outright, matched by EXACT compacted name rather
    /// than as a substring.
    /// </summary>
    /// <remarks>
    /// Exact matching is required for these because each is a fragment of something legitimate: a
    /// substring rule for <c>key</c> would flag the approved <c>keyRef</c> handle and the JWK's
    /// <c>key_ops</c> metadata, and one for <c>iv</c> would flag <c>ivRef</c>. A property named
    /// precisely <c>key</c>, <c>iv</c> or <c>secret</c>, by contrast, can only be the material itself.
    /// </remarks>
    private static readonly string[] ForbiddenExactRequestPropertyNames =
    [
        "key",
        "iv",
        "secret",
        "password",
        "credential",
        "certificate",
        "cert",
        "pem",
        "privatekey",
        "passphrase",
    ];

    /// <summary>
    /// The JSON Web Key members that carry PRIVATE key material, per RFC 7518 sections 6.3.2 and
    /// 6.4.1. Matched by EXACT, CASE-SENSITIVE name, because JWK member names are single letters.
    /// </summary>
    /// <remarks>
    /// Case-sensitive and exact by necessity: a substring rule for <c>d</c> would match almost every
    /// name in the document, and JWK member names are defined ordinally by the specification. The set
    /// is the RSA private set - <c>d</c>, <c>p</c>, <c>q</c>, <c>dp</c>, <c>dq</c>, <c>qi</c>,
    /// <c>oth</c> - plus <c>k</c>, the symmetric key member. The public members <c>n</c> and <c>e</c>
    /// are verification material and are deliberately NOT here: publishing them is the entire purpose
    /// of a JWKS endpoint.
    /// </remarks>
    private static readonly string[] JsonWebKeyPrivateMemberNames =
    [
        "d",
        "p",
        "q",
        "dp",
        "dq",
        "qi",
        "oth",
        "k",
    ];

    /// <summary>
    /// Literal markers whose presence in a contract artifact would mean a key, certificate or token
    /// had been committed into the published boundary itself.
    /// </summary>
    /// <remarks>
    /// These are STRUCTURAL MARKERS, not secrets: <c>-----BEGIN</c> is the PEM armour prefix and the
    /// rest are provider key prefixes published by the providers themselves. None is a credential and
    /// none is derived from any site the repository sweep found (C-F). They are the smallest set that
    /// catches an accidentally pasted specimen in a <c>description</c>, an <c>example</c> or a
    /// <c>default</c>.
    /// </remarks>
    private static readonly string[] CommittedSecretMarkers =
    [
        "-----BEGIN",
        "-----END",
        "PRIVATE KEY",
        "sk_live_",
        "sk_test_",
        "pk_live_",
        "github_pat_",
        "ghp_",
        "AKIA",
        "ASIA",
        "xoxb-",
        "xoxp-",
        "AIza",
    ];

    // ==============================================================================================
    //  NAMES AND ORDERINGS TAKEN FROM THE ORACLE
    //
    //  Every constant below is a NAME or a POSITION, never a value. The two field-order arrays are
    //  transcriptions of the two legacy structures and are the reason the mirror assertions can be
    //  checked against the oracle rather than against themselves.
    // ==============================================================================================

    /// <summary>The nine fields of <c>transactiondata.srs</c>, in the oracle's own order [:L4-L12].</summary>
    /// <remarks>
    /// The boolean is EIGHTH and <c>userparm</c> NINTH - not the other way round, which is the one
    /// ordering a reader is likely to get wrong, since a trailing boolean would be the more natural
    /// layout. The order is transcribed rather than derived so that a reordering of the contract fails
    /// here instead of quietly changing what every consumer's generated code expects.
    /// </remarks>
    private static readonly string[] LegacyTransactionDataFieldOrder =
    [
        "dbms",
        "servername",
        "database",
        "logid",
        "logpass",
        "dbparm",
        "lock",
        "autocommit",
        "userparm",
    ];

    /// <summary>The five fields of <c>dberrordata.srs</c>, in the oracle's own order [:L4-L8].</summary>
    private static readonly string[] LegacyDbErrorFieldOrder =
    [
        "sqldbcode",
        "sqlerrtext",
        "sqlsyntax",
        "buffer",
        "row",
    ];

    /// <summary>
    /// Fragments that count as a documented WRITE-ONLY marker on the password field. Matched
    /// case-insensitively against the field's authored leading comment.
    /// </summary>
    private static readonly string[] WriteOnlyMarkerFragments =
    [
        "write-only",
        "write only",
        "never echoed",
        "never logged",
    ];

    /// <summary>
    /// Fragments that count as a documented REDACTION marker on the statement field. Matched
    /// case-insensitively against the field's authored leading comment.
    /// </summary>
    /// <remarks>
    /// Either of the two permitted shapes satisfies the requirement, so both vocabularies are here:
    /// <c>redact</c> for the redacted single field, and the parameter-separation wording for the split
    /// shape. <c>placeholder</c> is included because it is the operative instruction to a producer -
    /// placeholders only, never interpolated literals - and is therefore the strongest phrasing of the
    /// same rule.
    /// </remarks>
    private static readonly string[] RedactionMarkerFragments =
    [
        "redact",
        "placeholder",
        "parameter-separated",
        "parameter separated",
    ];

    /// <summary>
    /// Route-segment fragments identifying the cryptographic families the legacy keys, and which
    /// therefore must consume an opaque key reference.
    /// </summary>
    /// <remarks>
    /// Derived from the oracle, not from this document's route list: the keyed families are keyed hash
    /// [n_crypto.sru:L23-L26], symmetric [:L30-L61] and RSA [:L62-L73]. The unkeyed
    /// <c>Hash(data, ntype)</c> [:L21-L22] and <c>HashFile(filename, ntype)</c> [:L27] families are
    /// deliberately absent, which is what makes "this operation must NOT carry a key" a real assertion
    /// for them rather than an omission.
    /// </remarks>
    private static readonly string[] KeyedCryptoFamilyRouteFragments =
    [
        "hmac",
        "symmetric",
        "rsa",
    ];

    /// <summary>
    /// The only three operations permitted to be anonymous, addressed by route.
    /// </summary>
    /// <remarks>
    /// <c>/health</c> must answer before any token can be obtained, and the two publication endpoints
    /// carry PUBLIC verification material by design - a consumer's stock bearer handler fetches them
    /// before it holds any credential. Every other operation is authenticated, which is C-G.
    /// </remarks>
    private static readonly string[] PermittedAnonymousRoutes =
    [
        HealthRoute,
        JsonWebKeySetRoute,
        OpenIdDiscoveryRoute,
    ];

    private const string HealthRoute = "/health";
    private const string JsonWebKeySetRoute = "/.well-known/jwks.json";
    private const string OpenIdDiscoveryRoute = "/.well-known/openid-configuration";

    /// <summary>
    /// The exact names of the initialisation vector, matched EXACTLY rather than as a substring.
    /// </summary>
    /// <remarks>
    /// Substring matching on <c>iv</c> is unusable and the reason is worth recording so nobody
    /// "simplifies" it back: <c>privatekey</c> CONTAINS <c>iv</c>, so a substring rule would classify
    /// the single most dangerous inbound name in this contract as a harmless initialisation vector.
    /// </remarks>
    private static readonly string[] InitialisationVectorExactNames = ["iv"];

    /// <summary>
    /// Compound spellings that unambiguously name an initialisation vector, matched as a compacted,
    /// case-insensitive substring.
    /// </summary>
    private static readonly string[] InitialisationVectorNameFragments =
    [
        "ivref",
        "initialisationvector",
        "initializationvector",
        "initvector",
    ];

    /// <summary>
    /// Name fragments identifying a collection of statement parameters, used to detect whether the
    /// database-error contract chose the parameter-separated shape rather than the redacted one.
    /// </summary>
    private static readonly string[] ParameterCollectionNameFragments =
    [
        "parameter",
        "param",
        "binding",
        "argument",
    ];

    private const string CommonProtoFileName = "common.v1.proto";
    private const string PersistenceProtoFileName = "persistence.v1.proto";
    private const string DataServicesProtoFileName = "dataservices.v1.proto";
    private const string SecurityDocumentFileName = "security.v1.yaml";
    private const string GatewayDocumentFileName = "gateway.v1.yaml";
    private const string ProtoDirectoryName = "Proto";
    private const string OpenApiDirectoryName = "OpenApi";

    private const string TransactionDescriptorMessageName = "persistence.v1.TransactionDescriptor";
    private const string TransactionDescriptorViewMessageName = "persistence.v1.TransactionDescriptorView";
    private const string DbErrorMessageName = "common.v1.DbError";
    private const string ExecRequestMessageName = "persistence.v1.ExecRequest";

    /// <summary>The legacy password field name, preserved verbatim [transactiondata.srs:L8].</summary>
    private const string PasswordFieldName = "logpass";

    /// <summary>The password field's wire slot, which the response-side view reserves permanently.</summary>
    private const int PasswordFieldNumber = 5;

    /// <summary>The legacy statement field name, preserved verbatim [dberrordata.srs:L6].</summary>
    private const string StatementFieldName = "sqlsyntax";

    private const string KeyReferenceSchemaName = "KeyReference";
    private const string IvReferenceSchemaName = "IvReference";
    private const string JsonWebKeySchemaName = "JsonWebKey";
    private const string BearerSchemeName = "bearerAuth";
    private const string CryptoServiceTagName = "CryptoService";

    /// <summary>The approved inbound key handle - an opaque reference, never key material.</summary>
    private const string KeyReferencePropertyName = "keyRef";

    /// <summary>The approved inbound initialisation vector handle.</summary>
    private const string IvReferencePropertyName = "ivRef";

    /// <summary>Compacted substring identifying any key-bearing property, of any spelling.</summary>
    private const string KeyNameFragment = "key";

    /// <summary>Compacted substring identifying a property that mentions PEM.</summary>
    private const string PemNameFragment = "pem";

    /// <summary>Compacted substring identifying a PUBLIC key, which a response may legitimately carry.</summary>
    private const string PublicKeyNameFragment = "publickey";

    /// <summary>The only permitted compacted name mentioning PEM - a boolean output-format selector.</summary>
    private const string PemFormatPropertyName = "pemformat";

    /// <summary>Compacted suffix marking a property as an OPAQUE REFERENCE rather than material.</summary>
    private const string OpaqueReferenceNameSuffix = "ref";

    /// <summary>The YAML key introducing an operation identifier, for the static inventory scan.</summary>
    private const string OperationIdYamlKey = "operationId:";

    private const string SharedDirectoryName = "shared";
    private const string ContractsProjectDirectoryName = "PowerFramework.Contracts";
    private const string SolutionMarkerFileName = "PowerFramework.slnx";
    private const string PackageVersionMarkerFileName = "Directory.Packages.props";

    /// <summary>
    /// The mechanical root of the statement field's exposure, cited so a reader of a failure knows why
    /// the field is dangerous rather than merely that a marker was missing.
    /// </summary>
    private const string DisableBindLocator = "n_cst_thread_task_sqlbase.sru:L128-L129";

    /// <summary>
    /// How many <c>$ref</c> hops <see cref="ResolveSchema"/> will follow before giving up.
    /// </summary>
    /// <remarks>
    /// A bound rather than an unbounded loop, so a pathological self-referencing chain terminates as a
    /// failed assertion somewhere rather than as a hung test run. Thirty-two is far beyond anything
    /// this document contains, where the deepest chain is a single hop.
    /// </remarks>
    private const int MaxReferenceHops = 32;

    /// <summary>
    /// The minimum nesting depth the response sweep must actually reach for its transitivity to be
    /// demonstrated rather than assumed.
    /// </summary>
    /// <remarks>
    /// Two means "a message nested two levels below a response root", which is the bar a
    /// single-level walk cannot clear. The measured value on this contract set is higher; the
    /// assertion reports it either way so a future narrowing of the graph is visible.
    /// </remarks>
    private const int MinimumTransitiveDepth = 2;

    // ==============================================================================================
    //  THE SWEEP TABLES - one row per target, so a violation names itself
    //
    //  Both providers are built from the CONTRACTS THEMSELVES rather than from a written-out list, and
    //  that is a requirement rather than a convenience: a hardcoded roster silently stops covering an
    //  operation or a method added later, which is precisely when a new credential field would arrive.
    //  Rows carry only strings so xunit can serialise them; each test resolves its subject back from
    //  the row, and a row naming something that no longer exists fails loudly at that lookup.
    // ==============================================================================================

    /// <summary>
    /// Every RPC in the three protocol definitions, as (service full name, method name). One row per
    /// method, so a credential field on a response path names the method that exposes it.
    /// </summary>
    public static TheoryData<string, string> ResponseSweepTargets { get; } = BuildResponseSweepTargets();

    /// <summary>
    /// Every <c>operationId</c> declared in the authored <c>security.v1.yaml</c>, scanned from its text.
    /// </summary>
    /// <remarks>
    /// Scanned rather than parsed because a theory data provider must be STATIC while the parsed
    /// document is fixture-scoped, and held to agreement with the parsed document by
    /// <see cref="TheScannedOperationInventoryMatchesTheParsedDocumentExactly"/>. Declared immediately
    /// above the table that consumes it because static initialisers run in textual order.
    /// </remarks>
    private static readonly string[] SecurityOperationIdInventory = ScanSecurityOperationIds();

    /// <summary>
    /// Every operation declared in <c>security.v1.yaml</c>, by <c>operationId</c>. One row per
    /// operation, so an inbound key-material violation names the operation that accepts it.
    /// </summary>
    public static TheoryData<string> SecurityOperationIds { get; } = new(SecurityOperationIdInventory);

    /// <summary>
    /// EVERY published contract artifact, as repository-relative paths inside the contracts project, for
    /// the artifact-level literal sweep.
    /// </summary>
    /// <remarks>
    /// <para>
    /// All five: the three protocol definitions plus BOTH OpenAPI documents. The gateway document belongs
    /// here as much as the security one and is the likelier of the two to acquire a specimen credential,
    /// because it is the ingress contract - the surface whose examples a reader reaches for first. An
    /// artifact omitted from this list is not swept at all, and the omission is invisible: the theory
    /// simply runs one row fewer and still reports success. <see
    /// cref="TheArtifactSweepCoversEveryFileInBothPublishedContractFolders"/> exists so it cannot happen
    /// again - it enumerates both folders on disk and fails if anything in either is missing from here.
    /// </para>
    /// <para>
    /// Paths use forward slashes and are split before being combined, so the rows read the same on every
    /// operating system while resolving correctly on the Linux target.
    /// </para>
    /// </remarks>
    public static TheoryData<string> ContractArtifactPaths { get; } = new(ContractArtifactInventory);

    /// <summary>
    /// The same five artifact paths as a plain array, for the coverage assertion that compares them
    /// against what the two published folders actually hold.
    /// </summary>
    private static string[] ContractArtifactInventory =>
    [
        $"{ProtoDirectoryName}/{CommonProtoFileName}",
        $"{ProtoDirectoryName}/{DataServicesProtoFileName}",
        $"{ProtoDirectoryName}/{PersistenceProtoFileName}",
        $"{OpenApiDirectoryName}/{GatewayDocumentFileName}",
        $"{OpenApiDirectoryName}/{SecurityDocumentFileName}",
    ];

    // ==============================================================================================
    //  PART 1 - THE TRANSACTION DESCRIPTOR'S PASSWORD IS WRITE-ONLY, AND ABSENT FROM EVERY RESPONSE
    // ==============================================================================================

    /// <summary>
    /// The request-side transaction descriptor mirrors <c>transactiondata.srs</c> field for field, in
    /// the oracle's own order, with the password in the fifth slot.
    /// </summary>
    /// <remarks>
    /// Asserted in FIELD NUMBER order rather than declaration order, because the field number is what
    /// crosses the wire: two peers agree on numbers and are indifferent to the order the text happens
    /// to list them in. The position matters for the same reason a rename would - AAP 0.4.5.3 keeps the
    /// legacy spellings verbatim because they appear in serialized payloads, log records and
    /// characterization recordings, and the slot a value lands in is part of that same agreement.
    /// </remarks>
    [Fact]
    public void TheTransactionDescriptorMirrorsTheLegacyStructureInFieldNumberOrderWithThePasswordFifth()
    {
        MessageDescriptor descriptor = ContractDescriptors.RequireMessage(TransactionDescriptorMessageName);

        string[] actual = descriptor.Fields.InFieldNumberOrder()
            .Select(static field => field.Name)
            .ToArray();

        Assert.Equal(LegacyTransactionDataFieldOrder, actual);

        // CONTIGUOUS 1..9, so the mirror is positional and not merely a set with the right members.
        int[] numbers = descriptor.Fields.InFieldNumberOrder()
            .Select(static field => field.FieldNumber)
            .ToArray();

        Assert.Equal(Enumerable.Range(1, LegacyTransactionDataFieldOrder.Length).ToArray(), numbers);

        // THE PASSWORD IS FIFTH, in the oracle [transactiondata.srs:L8] and here.
        FieldDescriptor password = ContractDescriptors.RequireField(descriptor, PasswordFieldName);

        Assert.Equal(PasswordFieldNumber, password.FieldNumber);
        Assert.Equal(PasswordFieldName, actual[PasswordFieldNumber - 1]);
    }

    /// <summary>
    /// The password is marked write-only in the contract METADATA: the response-side view permanently
    /// reserves both its slot and its name, so no response shape can carry it and no future edit can
    /// bring it back.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is the marker, and it is stronger than a comment.</b> A <c>reserved</c> declaration is
    /// machine-readable through <see cref="MessageDescriptor.ToProto"/>, and reserving the NUMBER as
    /// well as the NAME closes both failure modes: without the number a future field could be GIVEN
    /// slot 5, at which point an old client would deserialize it straight into its password slot and a
    /// proxy replaying traffic would move data into a password field; without the name the field could
    /// be reintroduced at a different number and read as legitimate.
    /// </para>
    /// <para>
    /// <b>Why the marker is asserted here rather than from the field's own leading comment.</b> Measured
    /// on this toolchain, <c>FieldDescriptor.Declaration</c> is <see langword="null"/> for every
    /// field in the build: Grpc.Tools does not request protoc's source-info option, so the compiled
    /// descriptors carry no comments at all and a comment-level marker is simply not reachable through
    /// descriptor reflection. The reserved metadata is what the descriptors DO carry, and
    /// <see cref="ThePasswordFieldsWriteOnlyRuleIsAlsoDocumentedOnTheFieldItself"/> adds the authored
    /// comment as the corroborating half by reading the protocol definition text.
    /// </para>
    /// </remarks>
    [Fact]
    public void ThePasswordFieldIsMarkedWriteOnlyInTheContractMetadata()
    {
        MessageDescriptor view = ContractDescriptors.RequireMessage(TransactionDescriptorViewMessageName);
        DescriptorProto proto = view.ToProto();

        // THE NAME IS RESERVED - so reintroducing `logpass` at any number is a compile error.
        Assert.Contains(PasswordFieldName, proto.ReservedName);

        // THE NUMBER IS RESERVED - so nothing else may ever occupy the password's wire slot.
        // Protobuf reserved ranges are half-open: Start is inclusive and End exclusive.
        Assert.Contains(
            proto.ReservedRange,
            range => range.Start <= PasswordFieldNumber && PasswordFieldNumber < range.End);

        // AND NOTHING OCCUPIES THE SLOT TODAY - the runtime half of the same guarantee.
        Assert.DoesNotContain(
            PasswordFieldNumber,
            view.Fields.InFieldNumberOrder().Select(static field => field.FieldNumber));

        // NO CREDENTIAL-SHAPED FIELD OF ANY SPELLING SURVIVES ON THE VIEW.
        string[] offenders = view.Fields.InFieldNumberOrder()
            .Where(static field => MentionsAny(field.Name, CredentialNameFragments))
            .Select(static field => field.Name)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"{TransactionDescriptorViewMessageName} is the RESPONSE-side descriptor and must carry no "
                + $"credential-shaped field, yet it declares: {string.Join(", ", offenders)}. The "
                + "write-only rule for transactiondata.srs:L8 is enforced by the field's ABSENCE from "
                + "this shape, so a credential-shaped field here defeats it whatever the field is "
                + "called.");
    }

    /// <summary>
    /// The password field's write-only rule is also documented on the field itself, in the authored
    /// protocol definition, with the oracle locator cited.
    /// </summary>
    /// <remarks>
    /// The corroborating half of the marker requirement. The structural half - the reserved slot and
    /// name asserted by <see cref="ThePasswordFieldIsMarkedWriteOnlyInTheContractMetadata"/> - is what
    /// a build enforces; this is what a human reads before writing a server that populates the field.
    /// Both are required: prose without the reservation is unenforceable, and a reservation without
    /// prose leaves the next maintainer guessing why the slot is empty.
    /// </remarks>
    [Fact]
    public void ThePasswordFieldsWriteOnlyRuleIsAlsoDocumentedOnTheFieldItself()
    {
        string comment = RequireFieldLeadingComment(
            PersistenceProtoFileName,
            "TransactionDescriptor",
            $"string {PasswordFieldName} = {PasswordFieldNumber};");

        Assert.True(
            MentionsAnyLiteral(comment, WriteOnlyMarkerFragments),
            $"The leading comment on {TransactionDescriptorMessageName}.{PasswordFieldName} records no "
                + $"write-only marker. Expected one of: {string.Join(", ", WriteOnlyMarkerFragments)}. "
                + $"Comment read:{Environment.NewLine}{comment}");

        // C-K: the comment must cite the oracle, so a reader can check the claim rather than trust it.
        Assert.Contains("transactiondata.srs:L8", comment, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// No response path of any RPC in the contract set carries a credential field, at any nesting
    /// depth. This is the load-bearing assertion of Part 1.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The walk is TRANSITIVE and cycle-guarded: it starts at the method's output message and descends
    /// through every message-typed field, including map entry values and messages nested inside other
    /// messages, until the reachable graph is exhausted. A credential field four levels below a response
    /// root is exactly the kind that arrives unnoticed, and a spot check on the top level would not see
    /// it.
    /// </para>
    /// <para>
    /// A violation is reported with the whole path - method, response message, then every field
    /// traversed - because "a credential field exists somewhere" is the least useful form the finding
    /// could take.
    /// </para>
    /// </remarks>
    /// <param name="serviceFullName">Fully qualified service name, from <see cref="ResponseSweepTargets"/>.</param>
    /// <param name="methodName">Method name within that service.</param>
    [Theory]
    [MemberData(nameof(ResponseSweepTargets))]
    public void NoResponsePathOfAnyRpcCarriesACredentialField(string serviceFullName, string methodName)
    {
        ServiceDescriptor service = ContractDescriptors.RequireService(serviceFullName);
        MethodDescriptor method = ContractDescriptors.RequireMethod(service, methodName);

        MessageGraphSweep sweep = SweepMessageGraph(
            method.OutputType,
            $"{serviceFullName}.{methodName} -> {method.OutputType.FullName}",
            static field => MentionsAny(field.Name, CredentialNameFragments));

        Assert.True(
            sweep.Matches.Count == 0,
            $"A credential-shaped field is reachable from the response of {serviceFullName}.{methodName}."
                + $"{Environment.NewLine}{FormatPaths(sweep.Matches)}{Environment.NewLine}"
                + "transactiondata.srs:L8 is a password and C-08 makes it write-only: supplied on a "
                + "request, never echoed on a response, never logged. A credential reachable from a "
                + "response defeats that rule however deeply it is nested, so the fix is to remove the "
                + "field from the response shape rather than to blank it in an implementation.");
    }

    /// <summary>
    /// No message in the shared vocabulary carries a credential field.
    /// </summary>
    /// <remarks>
    /// Swept as a whole rather than only where it is currently reachable. <c>common.v1</c> declares no
    /// service, so every message in it exists to be embedded by <c>persistence.v1</c> or
    /// <c>dataservices.v1</c> - which means any message here MAY become part of a response later, and a
    /// credential field added to one would be a leak waiting for its first consumer. Holding the whole
    /// file to the rule is the only form of the assertion that does not depend on today's reachability.
    /// </remarks>
    [Fact]
    public void TheSharedVocabularyCarriesNoCredentialFieldOnAnyMessage()
    {
        string[] offenders = ContractDescriptors.Common.MessageTypes
            .SelectMany(FlattenNestedMessages)
            .SelectMany(static message => message.Fields.InFieldNumberOrder())
            .Where(static field => MentionsAny(field.Name, CredentialNameFragments))
            .Select(static field => $"{field.ContainingType.FullName}.{field.Name}")
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            "The shared vocabulary common.v1 declares a credential-shaped field, and every message in "
                + "it is embeddable by both sibling definitions - so this would reach a response as soon "
                + $"as anything embedded it:{Environment.NewLine}{FormatPaths(offenders)}");
    }

    /// <summary>
    /// Exactly one credential-shaped field exists anywhere in the three protocol definitions, and it is
    /// the request-side password.
    /// </summary>
    /// <remarks>
    /// The complement of the response sweep, and the reason the pair is worth having: the sweep proves
    /// no credential is REACHABLE from a response, while this proves no credential-shaped field EXISTS
    /// beyond the one the legacy structure requires. Together they establish that the contract set has
    /// one credential field, that it is on a request, and that nothing else was introduced under a name
    /// that happened not to be reachable yet.
    /// </remarks>
    [Fact]
    public void TheOnlyCredentialShapedFieldInTheWholeContractSetIsTheRequestSidePassword()
    {
        string[] credentialFields = ContractDescriptors.AllFields()
            .Where(static field => MentionsAny(field.Name, CredentialNameFragments))
            .Select(static field => $"{field.ContainingType.FullName}.{field.Name}")
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

        string only = Assert.Single(credentialFields);

        Assert.Equal($"{TransactionDescriptorMessageName}.{PasswordFieldName}", only);
    }

    /// <summary>
    /// The password IS reachable from a request path, so its rule is genuinely write-only rather than
    /// the field simply being absent from the contract.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The DIRECTION is the whole content of "write-only", and asserting only the response half would
    /// leave a contract that had dropped the field entirely looking compliant. It is not: a connection
    /// cannot be opened without the password, so the request side MUST carry it. C-B applies here as
    /// much as anywhere - the legacy behaviour survives, and only the disclosure is controlled.
    /// </para>
    /// <para>
    /// The complementary half is asserted too: the request-side descriptor MESSAGE is unreachable from
    /// every response path, so the narrowing is achieved by two shapes rather than by a rule that a
    /// server implementation has to remember to apply.
    /// </para>
    /// </remarks>
    [Fact]
    public void ThePasswordIsReachableFromARequestPathSoItsRuleIsWriteOnlyRatherThanAbsent()
    {
        List<string> requestReaches = [];
        List<string> responseReaches = [];

        foreach (MethodDescriptor method in ContractDescriptors.AllMethods())
        {
            string label = $"{method.Service.FullName}.{method.Name}";

            requestReaches.AddRange(SweepMessageGraph(
                method.InputType,
                $"{label} request -> {method.InputType.FullName}",
                static field => field.Name == PasswordFieldName).Matches);

            responseReaches.AddRange(SweepMessageGraph(
                method.OutputType,
                $"{label} response -> {method.OutputType.FullName}",
                static field => field.Name == PasswordFieldName
                    || (field.FieldType is FieldType.Message
                        && field.MessageType.FullName == TransactionDescriptorMessageName)).Matches);
        }

        Assert.True(
            requestReaches.Count > 0,
            $"No request path reaches {PasswordFieldName}. The field is not merely permitted on a "
                + "request, it is REQUIRED there: transactiondata.srs:L8 is the login password and a "
                + "connection cannot be opened without it. Removing it from the request side would be a "
                + "behavioural change dressed as a security improvement (C-B).");

        Assert.True(
            responseReaches.Count == 0,
            "A response path reaches the password field or the request-side descriptor message that "
                + $"carries it:{Environment.NewLine}{FormatPaths(responseReaches)}{Environment.NewLine}"
                + $"The response side must use {TransactionDescriptorViewMessageName}, which has no such "
                + "field and permanently reserves its slot.");
    }

    /// <summary>
    /// The response sweep genuinely descends: it reaches messages nested well below the response roots
    /// rather than inspecting only the top level of each response.
    /// </summary>
    /// <remarks>
    /// A sweep that silently stopped at the first level would pass every assertion in Part 1 while
    /// checking almost nothing, and that failure mode is invisible in a green run. This test makes the
    /// walk's reach an asserted property, and reports the measured depth in its failure message so a
    /// future narrowing of the message graph is legible rather than mysterious.
    /// </remarks>
    [Fact]
    public void TheResponseSweepIsGenuinelyTransitiveAcrossTheWholeContractSet()
    {
        int deepest = 0;
        int rootCount = 0;
        int visited = 0;

        foreach (MethodDescriptor method in ContractDescriptors.AllMethods())
        {
            MessageGraphSweep sweep = SweepMessageGraph(
                method.OutputType,
                method.OutputType.FullName,
                static _ => false);

            rootCount++;
            visited += sweep.MessagesVisited;
            deepest = Math.Max(deepest, sweep.DeepestLevel);
        }

        Assert.True(rootCount > 0, "No RPC was found to sweep, so the sweep asserts nothing.");

        Assert.True(
            deepest >= MinimumTransitiveDepth,
            $"The response sweep reached a maximum nesting depth of {deepest}, below the required "
                + $"{MinimumTransitiveDepth}. Either the walk stopped descending - in which case every "
                + "credential assertion in Part 1 is checking only the top level of each response - or "
                + "the message graph genuinely flattened, which needs recording rather than accepting.");

        Assert.True(
            visited > rootCount,
            $"The sweep visited {visited} messages across {rootCount} response roots, so it never "
                + "descended past a root at all.");
    }

    // ==============================================================================================
    //  PART 2 - THE CRYPTO CONTRACT TAKES AN OPAQUE KEY REFERENCE, AND NO RAW KEY MATERIAL INBOUND
    // ==============================================================================================

    /// <summary>
    /// The operation inventory scanned from the authored document text is exactly the inventory the
    /// parsed document declares, and it is not empty.
    /// </summary>
    /// <remarks>
    /// This is the guard that keeps the two sweeps below honest. Their rows come from a static text scan
    /// - a theory data provider cannot reach a fixture-scoped document - so an inventory that silently
    /// came back empty, or that drifted from the parsed document, would leave each sweep reporting
    /// success while asserting nothing at all. That is the worst available outcome for an auditable
    /// control, so it is asserted rather than assumed.
    /// </remarks>
    [Fact]
    public void TheScannedOperationInventoryMatchesTheParsedDocumentExactly()
    {
        Assert.NotEmpty(SecurityOperationIdInventory);

        string[] scanned = SecurityOperationIdInventory
            .OrderBy(static id => id, StringComparer.Ordinal)
            .ToArray();

        string[] parsed = EnumerateOperations()
            .Select(static located => OperationIdOf(located.Operation))
            .OrderBy(static id => id, StringComparer.Ordinal)
            .ToArray();

        // EVERY OPERATION DECLARES AN IDENTIFIER, which is what makes a row addressable at all.
        Assert.DoesNotContain(string.Empty, parsed);

        Assert.Equal(parsed, scanned);
    }

    /// <summary>
    /// Every crypto operation belonging to a keyed legacy family consumes an OPAQUE KEY REFERENCE, and
    /// every other operation in the document carries no key-bearing property at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The rule is total, which is what makes it a control rather than a sample.</b> Each row falls
    /// into exactly one of two arms - must consume a reference, or must carry no key - so no operation is
    /// unclassified and the key surface is proved to be confined to the operations that need it. The
    /// second arm is the interesting one: it is what stops a key from being accepted by an encoding
    /// helper, a random generator, the unkeyed hash family or the token endpoint.
    /// </para>
    /// <para>
    /// <b>Classification comes from the document and the oracle, never from a written-out path list.</b>
    /// An operation is a crypto operation when it carries the document's own crypto tag. It belongs to a
    /// keyed family when one of its route segments names a family the legacy keys - keyed hash
    /// [n_crypto.sru:L23-L26], symmetric [:L30-L61] or RSA [:L62-L73]. And it is excluded when it
    /// PRODUCES key material rather than consuming it, which is detected from its own response shape
    /// rather than declared: RSA key generation exists to hand a key back [:L19-L20], so requiring it to
    /// accept one would be nonsense.
    /// </para>
    /// <para>
    /// <b>What "opaque" is asserted to mean.</b> Not merely a scalar named suggestively: the property
    /// must resolve to the document's own <c>KeyReference</c> component, which is a non-empty string
    /// carrying no key material and meaningless outside the service. That is C-02's central decision -
    /// "raw key material never crosses the wire from a caller" - and it discharges the mandate at the
    /// contract level rather than by review.
    /// </para>
    /// </remarks>
    /// <param name="operationId">The operation to check, from <see cref="SecurityOperationIds"/>.</param>
    [Theory]
    [MemberData(nameof(SecurityOperationIds))]
    public void EveryKeyConsumingCryptoOperationTakesAnOpaqueKeyReferenceAndNothingElseTakesAKey(
        string operationId)
    {
        LocatedOperation located = RequireOperation(operationId);

        SchemaProperty[] keyBearing = RequestProperties(located.Operation)
            .Where(static property => Mentions(property.Name, KeyNameFragment))
            .ToArray();

        bool consumesKey = IsCryptoOperation(located.Operation)
            && IsKeyedFamilyRoute(located.Route)
            && !ProducesKeyMaterial(located.Operation);

        if (!consumesKey)
        {
            Assert.True(
                keyBearing.Length == 0,
                $"Operation '{operationId}' ({located.Route}) belongs to no keyed cryptographic family, "
                    + "yet its request carries a key-bearing property: "
                    + $"{FormatProperties(keyBearing)}. The key surface must stay confined to the keyed "
                    + "families the oracle actually keys - keyed hash [n_crypto.sru:L23-L26], symmetric "
                    + "[:L30-L61] and RSA [:L62-L73]. Widening it here would create an inbound key path "
                    + "on a boundary that has no use for one.");
            return;
        }

        SchemaProperty reference = Assert.Single(keyBearing);

        Assert.Equal(KeyReferencePropertyName, reference.Name);

        Assert.Equal(KeyReferenceSchemaName, ReferenceIdOf(reference.Schema));

        IOpenApiSchema? declared = reference.Schema;
        Assert.NotNull(declared);

        IOpenApiSchema resolved = ResolveSchema(declared);

        Assert.Equal(JsonSchemaType.String, resolved.Type);

        Assert.True(
            resolved.MinLength >= 1,
            $"Operation '{operationId}' takes {KeyReferencePropertyName} but the "
                + $"{KeyReferenceSchemaName} schema permits an empty value. An opaque handle that may be "
                + "empty is not a handle: it lets a caller omit the key while appearing to supply one.");
    }

    /// <summary>
    /// No operation in the document accepts raw key material, a credential, or any of the forms the
    /// legacy passed by value, on a request.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The whole request surface is swept, transitively through <c>$ref</c>-resolved component schemas
    /// and through arrays, additional properties and composition keywords, with a cycle guard. Both
    /// request bodies and parameters are covered, because a key smuggled into a query or header
    /// parameter would be worse than one in a body - it would reach every access log on the path.
    /// </para>
    /// <para>
    /// The DIRECTION is what is asserted, not merely the name. A key-material name is forbidden here
    /// because this is a REQUEST; the same name on a response can be entirely legitimate, and
    /// <see cref="KeyMaterialNamesAppearOnlyInResponsesAndNeverOnARequest"/> asserts that half.
    /// </para>
    /// </remarks>
    /// <param name="operationId">The operation to check, from <see cref="SecurityOperationIds"/>.</param>
    [Theory]
    [MemberData(nameof(SecurityOperationIds))]
    public void NoOperationAcceptsRawKeyMaterialInbound(string operationId)
    {
        LocatedOperation located = RequireOperation(operationId);

        SchemaProperty[] violations = RequestProperties(located.Operation)
            .Where(static property =>
                MentionsAny(property.Name, RawKeyMaterialNameFragments)
                || MentionsAny(property.Name, CredentialNameFragments)
                || EqualsAny(property.Name, ForbiddenExactRequestPropertyNames)
                || (MentionsAny(property.Name, KeyMaterialNameFragments)
                    && !IsOpaqueReferenceName(property.Name)))
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"Operation '{operationId}' ({located.Route}) accepts raw key or credential material "
                + $"inbound:{Environment.NewLine}{FormatProperties(violations)}{Environment.NewLine}"
                + "C-02 narrows the legacy surface deliberately: every keyed n_crypto operation took the "
                + "key as an ordinary in-parameter [n_crypto.sru:L23-L26, :L30-L61, :L62-L73], and "
                + "republishing that signature on a wire would place key material into request bodies, "
                + "into any log that records a request, and into every characterization recording of "
                + "one. Callers pass an opaque keyRef instead.");
    }

    /// <summary>
    /// The only property in the document whose name mentions PEM is a boolean output-format selector,
    /// so it cannot carry key text in either direction.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A DELIBERATE EXCEPTION, ASSERTED RATHER THAN EXCLUDED SILENTLY. The raw-key-material vocabulary
    /// omits a bare <c>pem</c> fragment because the contract legitimately carries <c>pemFormat</c>,
    /// which stands in for the legacy's <c>readonly boolean pemformat</c> parameter
    /// [n_crypto.sru:L20]. Dropping the fragment without asserting anything in its place would leave a
    /// real gap; this is the assertion that closes it.
    /// </para>
    /// <para>
    /// The type is the whole point. A BOOLEAN can only select a format. A STRING named <c>pemFormat</c>
    /// could carry armoured key text while looking like a flag, and would pass every other assertion in
    /// this file.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheOnlyPemNamedPropertyIsABooleanFormatSelectorAndNeverKeyText()
    {
        List<SchemaProperty> pemNamed = [];

        foreach (LocatedOperation located in EnumerateOperations())
        {
            pemNamed.AddRange(RequestProperties(located.Operation)
                .Concat(ResponseProperties(located.Operation))
                .Where(static property => Mentions(property.Name, PemNameFragment)));
        }

        Assert.NotEmpty(pemNamed);

        foreach (SchemaProperty property in pemNamed)
        {
            // Compaction removes separators but deliberately does not fold case, so the comparison
            // supplies the case-insensitivity rather than the helper. See Compact.
            Assert.Equal(PemFormatPropertyName, Compact(property.Name), ignoreCase: true);

            Assert.NotNull(property.Schema);

            Assert.Equal(JsonSchemaType.Boolean, ResolveSchema(property.Schema).Type);
        }
    }

    /// <summary>
    /// Key-material names appear only on responses and never on a request, which is the direction rule
    /// stated as a property of the whole document.
    /// </summary>
    /// <remarks>
    /// The legacy <c>GenRSAKey</c> hands its keys back through <c>ref string</c> out-parameters
    /// [n_crypto.sru:L19-L20], so a key in a RESPONSE is in scope for this contract while a key in a
    /// REQUEST is not. Both halves are asserted: none inbound, and at least one outbound - because a
    /// contract that had quietly stopped publishing anything from key generation would satisfy a
    /// one-sided assertion while no longer implementing the capability at all.
    /// </remarks>
    [Fact]
    public void KeyMaterialNamesAppearOnlyInResponsesAndNeverOnARequest()
    {
        List<SchemaProperty> inbound = [];
        List<SchemaProperty> outbound = [];

        foreach (LocatedOperation located in EnumerateOperations())
        {
            inbound.AddRange(RequestProperties(located.Operation).Where(IsKeyMaterialProperty));
            outbound.AddRange(ResponseProperties(located.Operation).Where(IsKeyMaterialProperty));
        }

        Assert.True(
            inbound.Count == 0,
            "Key material is named on a request, which is the direction C-02 forbids: "
                + FormatProperties(inbound));

        Assert.True(
            outbound.Count > 0,
            "No response in the document names key material, so the direction rule has nothing to "
                + "distinguish. RSA key generation exists to produce a key pair [n_crypto.sru:L19-L20], "
                + "so at least one response is expected to publish something.");
    }

    /// <summary>
    /// The RSA key-generation response publishes the PUBLIC key and an opaque handle, and never the
    /// private key.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A further narrowing than the requirement anticipated, recorded here because it is the shape
    /// actually found.</b> The legacy returns the PRIVATE key by value through a <c>ref string</c>
    /// [n_crypto.sru:L19-L20]. A response carrying a private key would have been permitted by the
    /// direction rule alone - it is outbound, and the caller asked for it - yet this contract does not
    /// carry one: it publishes the public half plus an opaque reference to the private half, which the
    /// service retains in its own configured store. The private key therefore never crosses the boundary
    /// in either direction.
    /// </para>
    /// <para>
    /// This is asserted rather than merely noted, because it is the property a future edit is most
    /// likely to relax "for convenience" - and relaxing it would put a private key into every response
    /// log and every characterization recording of a key-generation call.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheRsaKeyGenerationResponsePublishesThePublicKeyAndAnOpaqueHandleButNeverThePrivateKey()
    {
        LocatedOperation[] producers = EnumerateOperations()
            .Where(static located => ProducesKeyMaterial(located.Operation))
            .ToArray();

        LocatedOperation producer = Assert.Single(producers);

        SchemaProperty[] response = ResponseProperties(producer.Operation).ToArray();

        Assert.Contains(response, property => Mentions(property.Name, PublicKeyNameFragment));

        // AN OPAQUE HANDLE STANDS IN FOR THE PRIVATE HALF, so the caller can use the key it just asked
        // for without the material ever crossing the boundary.
        Assert.Contains(response, static property => IsOpaqueReferenceName(property.Name));

        SchemaProperty[] privateMaterial = response
            .Where(static property => MentionsAny(property.Name, RawKeyMaterialNameFragments)
                || MentionsAny(property.Name, CredentialNameFragments))
            .ToArray();

        Assert.True(
            privateMaterial.Length == 0,
            $"The key-generation response of '{producer.Operation.OperationId}' publishes private key or "
                + $"credential material:{Environment.NewLine}{FormatProperties(privateMaterial)}"
                + $"{Environment.NewLine}The legacy returned the private key by value through a ref "
                + "string [n_crypto.sru:L19-L20]; this contract deliberately returns an opaque handle to "
                + "it instead, so the material stays inside the service.");

        // AND THE REQUEST SIDE TAKES NO KEY, which is what makes it a producer rather than a transformer.
        Assert.DoesNotContain(RequestProperties(producer.Operation), IsKeyMaterialProperty);
    }

    /// <summary>
    /// The initialisation vector is carried as an opaque reference, and every key-bearing sibling on the
    /// same request is a reference too - so an IV is never paired with an inbound raw key.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>An initialisation vector is not key material</b>, so a contract carrying one is acceptable and
    /// the requirement says so explicitly. The hazard is not the IV itself but the company it keeps: an
    /// IV parameter appears in the legacy exclusively alongside a raw <c>key</c> parameter
    /// [n_crypto.sru:L32-L33, :L36-L37, :L40-L41, :L44-L45 and their decrypt counterparts], so a
    /// mechanical port of any of those signatures would bring both across together. That pairing is what
    /// is asserted against.
    /// </para>
    /// <para>
    /// The vector is nonetheless carried as a reference here rather than by value, and the reason is
    /// recorded in the contract itself: it is resolved exactly as a key reference is, from the service's
    /// own configured store. Omitting it selects the no-vector legacy overload family, which is the
    /// correct choice under the default mode - ECB uses no vector at all, so the eight no-vector
    /// overloads and the default mode go together.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheInitialisationVectorIsAnOpaqueReferenceAndIsNeverPairedWithAnInboundRawKey()
    {
        int vectorsFound = 0;

        foreach (LocatedOperation located in EnumerateOperations())
        {
            SchemaProperty[] request = RequestProperties(located.Operation).ToArray();

            foreach (SchemaProperty vector in request.Where(static property =>
                EqualsAny(property.Name, InitialisationVectorExactNames)
                || MentionsAny(property.Name, InitialisationVectorNameFragments)))
            {
                vectorsFound++;

                Assert.Equal(IvReferencePropertyName, vector.Name);
                Assert.Equal(IvReferenceSchemaName, ReferenceIdOf(vector.Schema));

                IOpenApiSchema? declared = vector.Schema;
                Assert.NotNull(declared);

                Assert.Equal(JsonSchemaType.String, ResolveSchema(declared).Type);

                SchemaProperty[] rawKeySiblings = request
                    .Where(static property => Mentions(property.Name, KeyNameFragment)
                        && !IsOpaqueReferenceName(property.Name))
                    .ToArray();

                Assert.True(
                    rawKeySiblings.Length == 0,
                    $"Operation '{located.Operation.OperationId}' pairs an initialisation vector with a "
                        + $"non-reference key property: {FormatProperties(rawKeySiblings)}. Every legacy "
                        + "IV parameter sits beside a raw key parameter [n_crypto.sru:L32-L33, :L36-L37, "
                        + ":L40-L41, :L44-L45], and that pairing must not survive the port.");
            }
        }

        Assert.True(
            vectorsFound > 0,
            "No initialisation vector was found anywhere in the document, so this exception is vacuous. "
                + "The symmetric families take an IV in the legacy [n_crypto.sru:L32-L45], so the "
                + "contract is expected to carry one.");
    }

    /// <summary>
    /// The document declares the bearer scheme and requires it at the document level, so an operation is
    /// authenticated unless it explicitly declares otherwise.
    /// </summary>
    /// <remarks>
    /// Stating the requirement once and forcing every anonymous operation to OVERRIDE it means a new
    /// operation is authenticated by default. The opposite arrangement - no document-level requirement,
    /// each operation adding its own - makes an unauthenticated surface the consequence of FORGETTING
    /// something, which is the most common way a boundary ends up open. C-G requires every new boundary
    /// to be authenticated; this is the arrangement that makes that hard to get wrong rather than merely
    /// required.
    /// </remarks>
    [Fact]
    public void TheDocumentDeclaresTheBearerSchemeAndRequiresItByDefault()
    {
        OpenApiDocument document = documents.Security;

        Assert.NotNull(document.Components);
        Assert.NotNull(document.Components.SecuritySchemes);
        Assert.True(document.Components.SecuritySchemes.ContainsKey(BearerSchemeName));

        Assert.NotNull(document.Security);
        OpenApiSecurityRequirement requirement = Assert.Single(document.Security);
        OpenApiSecuritySchemeReference scheme = Assert.Single(requirement.Keys);

        Assert.Equal(BearerSchemeName, scheme.Reference?.Id);
    }

    /// <summary>
    /// Every crypto operation requires a bearer token.
    /// </summary>
    /// <remarks>
    /// A crypto endpoint reachable without a token would be an oracle: anyone able to call it could sign
    /// with, encrypt under, or verify against the service's configured key material without holding any
    /// of it. That is exactly the exposure C-G forbids, and it is independent of the key-reference rule -
    /// an opaque handle stops key material from crossing the wire, but it does not stop an unauthenticated
    /// caller from USING the key behind it.
    /// </remarks>
    [Fact]
    public void EveryCryptoOperationRequiresABearerToken()
    {
        string[] anonymous = EnumerateOperations()
            .Where(static located => IsCryptoOperation(located.Operation))
            .Where(static located => located.Operation.Security is { Count: 0 })
            .Select(static located => $"{located.Route} ({located.Operation.OperationId})")
            .ToArray();

        Assert.True(
            anonymous.Length == 0,
            "A crypto operation declares itself anonymous, which turns the service's configured key "
                + $"material into an open oracle:{Environment.NewLine}{FormatPaths(anonymous)}");
    }

    /// <summary>
    /// The only anonymous operations are the health probe and the two public verification-material
    /// publications.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The health probe must answer before any token can be obtained - a readiness gate that required a
    /// token could never open during a cold start - and the two publication endpoints carry PUBLIC
    /// material that a consumer's stock bearer handler fetches before it holds any credential.
    /// </para>
    /// <para>
    /// The set is asserted to be EXACTLY those three rather than "at most". A new anonymous operation is
    /// the single most consequential change anyone can make to this document, so it fails here and gets
    /// reviewed on its own merits.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheOnlyAnonymousOperationsAreHealthAndThePublicVerificationEndpoints()
    {
        string[] anonymous = EnumerateOperations()
            .Where(static located => located.Operation.Security is { Count: 0 })
            .Select(static located => located.Route)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static route => route, StringComparer.Ordinal)
            .ToArray();

        string[] permitted = PermittedAnonymousRoutes
            .OrderBy(static route => route, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(permitted, anonymous);
    }

    /// <summary>
    /// The two anonymous publication endpoints expose verification material only: no private JSON Web
    /// Key member, no private key, no credential, and no room to smuggle one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Exactly ONE signing secret exists in the whole system and Security holds it (AAP 0.6.6.3);
    /// Gateway, DataServices and Persistence hold verification material only and validate with the stock
    /// bearer handler. These two endpoints are how that material is published, and they are anonymous -
    /// so anything private reachable through them is world-readable by construction.
    /// </para>
    /// <para>
    /// The private JSON Web Key members are checked by EXACT, CASE-SENSITIVE name because RFC 7518 names
    /// them with single letters: the RSA private set <c>d</c>, <c>p</c>, <c>q</c>, <c>dp</c>, <c>dq</c>,
    /// <c>qi</c>, <c>oth</c>, plus <c>k</c> for a symmetric key. The public members <c>n</c> and <c>e</c>
    /// are asserted PRESENT, because publishing them is the entire purpose of the endpoint and an empty
    /// key would make this test vacuous.
    /// </para>
    /// <para>
    /// The closed property set matters as much as the absences. With <c>additionalProperties</c> false on
    /// the key schema, a private member cannot be added by an implementation that merely serialises more
    /// of its key object than it meant to - the document rejects it rather than relaying it.
    /// </para>
    /// </remarks>
    [Fact]
    public void ThePublicationEndpointsExposeVerificationMaterialOnlyAndNeverAPrivateKey()
    {
        SchemaProperty[] published = EnumerateOperations()
            .Where(static located => located.Route is JsonWebKeySetRoute or OpenIdDiscoveryRoute)
            .SelectMany(static located => ResponseProperties(located.Operation))
            .ToArray();

        Assert.NotEmpty(published);

        SchemaProperty[] privateMembers = published
            .Where(static property =>
                JsonWebKeyPrivateMemberNames.Contains(property.Name, StringComparer.Ordinal)
                || MentionsAny(property.Name, RawKeyMaterialNameFragments)
                || MentionsAny(property.Name, CredentialNameFragments)
                || (MentionsAny(property.Name, KeyMaterialNameFragments)
                    && !IsOpaqueReferenceName(property.Name)
                    && !Mentions(property.Name, PublicKeyNameFragment)))
            .ToArray();

        Assert.True(
            privateMembers.Length == 0,
            "An anonymous publication endpoint exposes private key or credential material:"
                + $"{Environment.NewLine}{FormatProperties(privateMembers)}{Environment.NewLine}"
                + "These endpoints are anonymous by design because verification material is public; "
                + "anything private reachable through them is world-readable. Exactly one signing secret "
                + "exists in this system and it never leaves the service holding it.");

        // VERIFICATION MATERIAL IS GENUINELY PUBLISHED, so the absences above are meaningful.
        IOpenApiSchema key = RequireComponentSchema(JsonWebKeySchemaName);

        Assert.NotNull(key.Properties);
        Assert.True(key.Properties.ContainsKey("n"), "The key schema publishes no RSA modulus.");
        Assert.True(key.Properties.ContainsKey("e"), "The key schema publishes no RSA exponent.");

        Assert.False(
            key.AdditionalPropertiesAllowed,
            $"{JsonWebKeySchemaName} permits additional properties, so an implementation that "
                + "serialised its whole key object would publish the private members through a document "
                + "that never mentions them.");
    }

    // ==============================================================================================
    //  PART 3 - THE DATABASE-ERROR STATEMENT FIELD IS DOCUMENTED AS REDACTED
    // ==============================================================================================

    /// <summary>
    /// The database-error message mirrors <c>dberrordata.srs</c> field for field, in the oracle's own
    /// order, including the statement field and the buffer and row pair.
    /// </summary>
    /// <remarks>
    /// <b>The statement field is PRESENT, and that is the requirement.</b> C-B forbids correcting legacy
    /// behaviour, and deleting the field would be a correction: the legacy raises this structure with
    /// five positional arguments in structure order, so a consumer expecting the third of them would find
    /// nothing. What is controlled is the DISCLOSURE, not the existence - see
    /// <see cref="TheStatementFieldIsDocumentedAsRedactedAndCitesWhyItIsDangerous"/>.
    /// </remarks>
    [Fact]
    public void TheDatabaseErrorMirrorsTheLegacyStructureIncludingTheStatementAndTheBufferRowPair()
    {
        MessageDescriptor error = ContractDescriptors.RequireMessage(DbErrorMessageName);

        string[] actual = error.Fields.InFieldNumberOrder()
            .Select(static field => field.Name)
            .ToArray();

        Assert.Equal(LegacyDbErrorFieldOrder, actual);

        int[] numbers = error.Fields.InFieldNumberOrder()
            .Select(static field => field.FieldNumber)
            .ToArray();

        Assert.Equal(Enumerable.Range(1, LegacyDbErrorFieldOrder.Length).ToArray(), numbers);

        // THE STATEMENT FIELD IS PRESERVED, NOT REMOVED (C-B).
        FieldDescriptor statement = ContractDescriptors.RequireField(error, StatementFieldName);
        Assert.Equal(3, statement.FieldNumber);

        // AND THE BUFFER AND ROW PAIR SURVIVES, which is what attributes an error to a row at all
        // [dberrordata.srs:L7-L8].
        Assert.Equal(4, ContractDescriptors.RequireField(error, "buffer").FieldNumber);
        Assert.Equal(5, ContractDescriptors.RequireField(error, "row").FieldNumber);
    }

    /// <summary>
    /// The statement field is documented as redacted, and the documentation cites the mechanism that
    /// makes the field dangerous rather than merely asserting that it is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Two shapes were permitted and the test accepts whichever the contract chose</b>, then applies
    /// the assertion that shape requires. The redacted single field carries placeholder-only statement
    /// text; the parameter-separated shape carries a statement plus a repeated parameter collection.
    /// <b>The shape found in this contract is the REDACTED SINGLE FIELD</b> - the message carries five
    /// fields and no parameter collection - and the deciding reason is recorded in the definition itself:
    /// splitting would have introduced a sixth field and broken the exact field-for-field correspondence
    /// to <c>dberrordata.srs</c> that the rest of the message depends on.
    /// </para>
    /// <para>
    /// <b>Why the documentation is asserted against the authored text.</b> Measured on this toolchain the
    /// compiled descriptors carry no comments at all, so a documentation-level marker is reachable only
    /// from the protocol definition itself. That is the artifact a producer's author reads, which makes it
    /// the right subject regardless.
    /// </para>
    /// <para>
    /// <b>Why this field is dangerous, cited so a failure explains itself (C-K).</b> The legacy places the
    /// COMPLETE GENERATED STATEMENT, INCLUDING INTERPOLATED LITERAL VALUES, into it, and the legacy logger
    /// performs no redaction. The mechanical root is the connection parameter string: the legacy parses
    /// <c>DBParm</c> for the bind-disabling flag [n_cst_thread_task_sqlbase.sru:L128-L129], and
    /// <c>DisableBind=1</c> means the runtime does not use bind variables - values are interpolated into
    /// the statement text as literals instead. That is why the field contains data in the first place.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheStatementFieldIsDocumentedAsRedactedAndCitesWhyItIsDangerous()
    {
        MessageDescriptor error = ContractDescriptors.RequireMessage(DbErrorMessageName);

        string comment = RequireFieldLeadingComment(
            CommonProtoFileName,
            "DbError",
            $"string {StatementFieldName} = 3;");

        // THE MARKER ITSELF - satisfied by either permitted wording.
        Assert.True(
            MentionsAnyLiteral(comment, RedactionMarkerFragments),
            $"The leading comment on {DbErrorMessageName}.{StatementFieldName} records no redaction or "
                + $"parameter-separation marker. Expected one of: "
                + $"{string.Join(", ", RedactionMarkerFragments)}. Comment read:"
                + $"{Environment.NewLine}{comment}");

        // WHICH FORM THE CONTRACT CHOSE, DETECTED RATHER THAN ASSUMED.
        FieldDescriptor[] parameterCollections = error.Fields.InFieldNumberOrder()
            .Where(static field => field.IsRepeated
                && MentionsAny(field.Name, ParameterCollectionNameFragments))
            .ToArray();

        if (parameterCollections.Length > 0)
        {
            // THE PARAMETER-SEPARATED FORM. The collection must be repeated - a single parameter field
            // could not express a statement with more than one placeholder - and the statement must be
            // documented as carrying placeholders rather than values, or the split achieves nothing.
            Assert.All(parameterCollections, static collection => Assert.True(collection.IsRepeated));

            Assert.True(
                MentionsAnyLiteral(comment, RedactionMarkerFragments),
                "The parameter-separated form requires the statement field to be documented as carrying "
                    + "placeholders rather than interpolated values.");
        }
        else
        {
            // THE REDACTED SINGLE FIELD FORM, which is what this contract uses. The absence of a sixth
            // field is the other half of the choice, and is asserted so that a later split cannot happen
            // silently while this branch still reports success.
            Assert.Equal(LegacyDbErrorFieldOrder.Length, error.Fields.InFieldNumberOrder().Count);
        }

        // THE MECHANISM IS CITED, so a reader of the definition learns why redaction is required rather
        // than being told that it is.
        string messageComment = RequireMessageLeadingComment(CommonProtoFileName, "DbError");

        Assert.Contains(DisableBindLocator, messageComment, StringComparison.Ordinal);
    }

    /// <summary>
    /// Where the contract DOES express parameter separation structurally, the parameters travel as a
    /// repeated collection alongside the statement rather than being interpolated into it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The database-error message uses the redacted single field, so the parameter-separated form is
    /// discharged where the contract genuinely uses it: the command execution request, which carries the
    /// statement and its positional parameters as separate fields. That is the same disclosure control
    /// applied at the other end of the same path - a statement that never has values spliced into it
    /// cannot leak them when it fails.
    /// </para>
    /// <para>
    /// The legacy is explicit that a statement and its parameters are separable: the command surface binds
    /// positional parameters before execution, and the eleven-argument ceiling it stops at is a limit of
    /// PowerScript's inability to forward an arbitrary-length argument list rather than anything the
    /// database requires. A repeated field has no such ceiling.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheContractsParameterSeparatedFormCarriesItsParametersAsARepeatedCollection()
    {
        MessageDescriptor request = ContractDescriptors.RequireMessage(ExecRequestMessageName);

        FieldDescriptor[] parameterCollections = request.Fields.InFieldNumberOrder()
            .Where(static field => MentionsAny(field.Name, ParameterCollectionNameFragments))
            .ToArray();

        Assert.NotEmpty(parameterCollections);

        foreach (FieldDescriptor collection in parameterCollections)
        {
            Assert.True(
                collection.IsRepeated,
                $"{ExecRequestMessageName}.{collection.Name} carries statement parameters but is not "
                    + "repeated, so a statement with more than one placeholder could not be expressed "
                    + "without interpolating values into the statement text.");
        }

        // THE STATEMENT TRAVELS SEPARATELY, as a scalar of its own, which is what "separated" means.
        FieldDescriptor statement = ContractDescriptors.RequireField(request, "sql");

        Assert.Equal(FieldType.String, statement.FieldType);
        Assert.False(
            statement.IsRepeated,
            $"{ExecRequestMessageName}.sql is repeated, which would make the statement and its "
                + "parameters indistinguishable.");
    }

    // ==============================================================================================
    //  PART 4 - NO SECRET WAS COMMITTED INTO THE PUBLISHED BOUNDARY ITSELF
    // ==============================================================================================

    /// <summary>
    /// No contract artifact carries a committed key, certificate or provider token.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The other three parts assert that no secret can TRAVEL through a contract. This one asserts that
    /// none was written INTO one. The two are different failures with the same consequence, and the second
    /// is the easier to commit: a specimen key pasted into a <c>description</c>, an <c>example</c> or a
    /// <c>default</c> to illustrate a field is in version control forever, and a document that documents
    /// its own discipline is exactly where a reader would least expect to find one.
    /// </para>
    /// <para>
    /// The markers matched are STRUCTURAL - PEM armour and published provider key prefixes - so this test
    /// contains no credential of its own (C-F). The repository sweep found eight in-source secret sites,
    /// all of them inside the read-only legacy tree; docs/SECRETS.md records every locator and its
    /// required action, and no value from any of them appears in this file or in any artifact swept here.
    /// </para>
    /// </remarks>
    /// <param name="artifactRelativePath">Contract artifact path, from <see cref="ContractArtifactPaths"/>.</param>
    [Theory]
    [MemberData(nameof(ContractArtifactPaths))]
    public void NoContractArtifactCarriesACommittedSecretLiteral(string artifactRelativePath)
    {
        string text = RequireArtifactText(artifactRelativePath);

        foreach (string marker in CommittedSecretMarkers)
        {
            Assert.False(
                text.Contains(marker, StringComparison.Ordinal),
                $"'{artifactRelativePath}' contains the marker '{marker}', which means key, certificate "
                    + "or token material has been written into the published boundary. The remediation "
                    + "posture is never-replicate: remove the value, rotate it at its owner, and record "
                    + "the site in docs/SECRETS.md without reproducing it.");
        }
    }

    /// <summary>
    /// The artifact sweep covers EVERY file in both published contract folders, with nothing extra.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE SWEEP ABOVE CAN ONLY EXAMINE WHAT IT IS HANDED, AND A MISSING ROW LOOKS EXACTLY LIKE A CLEAN
    /// RESULT. This test removes that failure mode by enumerating <c>Proto</c> and <c>OpenApi</c> on disk
    /// and comparing them, both ways, against the sweep's own inventory. A new artifact added to either
    /// folder without being added to the inventory fails here, before it has spent a single build being
    /// silently unswept; an inventory row naming a file that no longer exists fails here too, rather than
    /// deep inside the reader with a path that means nothing to the person who moved it.
    /// </para>
    /// <para>
    /// Read-only and creates nothing: both folders are enumerated with a top-level search, and the walk
    /// that locates them anchors on the two repository-root markers, so it cannot latch onto a same-named
    /// folder elsewhere on the machine.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheArtifactSweepCoversEveryFileInBothPublishedContractFolders()
    {
        string contractsDirectory = RequireContractsDirectory();

        string[] onDisk =
            [.. new[] { ProtoDirectoryName, OpenApiDirectoryName }
                .SelectMany(folder => EnumerateContractFolder(contractsDirectory, folder))
                .OrderBy(static path => path, StringComparer.Ordinal)];

        string[] swept =
            [.. ContractArtifactInventory.OrderBy(static path => path, StringComparer.Ordinal)];

        string[] unswept = [.. onDisk.Except(swept, StringComparer.Ordinal)];
        string[] absent = [.. swept.Except(onDisk, StringComparer.Ordinal)];

        Assert.True(
            unswept.Length == 0,
            $"{unswept.Length} published contract artifact(s) are not swept for committed secret "
                + $"literals: [{string.Join(", ", unswept)}]. Add each one to ContractArtifactPaths in "
                + "the same change that adds it to the folder - an unswept artifact is the one place a "
                + "specimen key can be committed without any test noticing.");

        Assert.True(
            absent.Length == 0,
            $"The sweep names {absent.Length} artifact(s) that are not present in the published folders: "
                + $"[{string.Join(", ", absent)}]. Either the artifact moved, in which case the inventory "
                + "row needs updating, or it is missing, which is itself the finding.");

        // Stated as a count as well, so the failure message carries the size of the surface being swept
        // rather than leaving a reader to count the rows: three protocol definitions and two OpenAPI
        // documents are the whole published boundary of this phase.
        Assert.Equal(5, onDisk.Length);
    }

    // ==============================================================================================
    //  HELPERS - the sweeps, the lookups and the text readers
    //
    //  All private, and all here rather than in ContractTestContext: that file states the folder's rule
    //  explicitly - a helper earns a place there only when two or more sibling test classes need it -
    //  and every helper below is specific to this suite's vocabulary-driven sweeps.
    // ==============================================================================================

    /// <summary>The result of walking one message graph: what matched, how deep, and how much was seen.</summary>
    /// <param name="Matches">Full paths of every field the predicate matched.</param>
    /// <param name="DeepestLevel">Nesting level of the deepest message reached, the root being level 0.</param>
    /// <param name="MessagesVisited">How many distinct messages the walk visited.</param>
    private sealed record MessageGraphSweep(
        IReadOnlyList<string> Matches,
        int DeepestLevel,
        int MessagesVisited);

    /// <summary>One property discovered by an OpenAPI schema sweep.</summary>
    /// <param name="Path">Where it was found, from the sweep root down.</param>
    /// <param name="Name">The property name exactly as the document spells it.</param>
    /// <param name="Schema">Its schema, which may be a <c>$ref</c> holder rather than an inline schema.</param>
    private sealed record SchemaProperty(string Path, string Name, IOpenApiSchema? Schema);

    /// <summary>One operation together with the route and verb it was found under.</summary>
    private sealed record LocatedOperation(string Route, HttpMethod Verb, OpenApiOperation Operation);

    /// <summary>
    /// Walks a message graph from <paramref name="root"/>, reporting every field the predicate matches
    /// with the full path that reaches it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Breadth-first with a SHARED VISITED SET, which is what makes the walk terminate on the recursive
    /// shapes this contract set genuinely contains. A message reached by two paths is inspected once and
    /// reported on the first path that reached it: a matching field is a property of the MESSAGE, so one
    /// valid path is sufficient to act on and enumerating every path would bury the finding.
    /// </para>
    /// <para>
    /// <see cref="FieldDescriptor.MessageType"/> THROWS for a field that is not a message or a group, so
    /// the field type is checked before it is read. Map fields are message-typed - their type is the
    /// synthetic entry message - so the walk descends into map values without needing a special case.
    /// </para>
    /// </remarks>
    private static MessageGraphSweep SweepMessageGraph(
        MessageDescriptor root,
        string rootPath,
        Func<FieldDescriptor, bool> isViolation)
    {
        List<string> matches = [];
        HashSet<MessageDescriptor> visited = [];
        Queue<(MessageDescriptor Message, string Path, int Level)> pending = new();

        visited.Add(root);
        pending.Enqueue((root, rootPath, 0));

        int deepest = 0;
        int messagesVisited = 0;

        while (pending.Count > 0)
        {
            (MessageDescriptor message, string path, int level) = pending.Dequeue();

            messagesVisited++;
            deepest = Math.Max(deepest, level);

            foreach (FieldDescriptor field in message.Fields.InFieldNumberOrder())
            {
                if (isViolation(field))
                {
                    matches.Add($"{path} -> {field.Name}");
                }

                if (field.FieldType is not (FieldType.Message or FieldType.Group))
                {
                    continue;
                }

                MessageDescriptor nested = field.MessageType;

                if (!visited.Add(nested))
                {
                    continue;
                }

                pending.Enqueue((nested, $"{path} -> {field.Name}:{nested.FullName}", level + 1));
            }
        }

        return new MessageGraphSweep(matches, deepest, messagesVisited);
    }

    /// <summary>A message and every message nested inside it, at any depth.</summary>
    private static IEnumerable<MessageDescriptor> FlattenNestedMessages(MessageDescriptor message)
    {
        yield return message;

        foreach (MessageDescriptor nested in message.NestedTypes.SelectMany(FlattenNestedMessages))
        {
            yield return nested;
        }
    }

    /// <summary>Every RPC in the three protocol definitions, as serialisable theory rows.</summary>
    private static TheoryData<string, string> BuildResponseSweepTargets()
    {
        TheoryData<string, string> targets = [];

        foreach (MethodDescriptor method in ContractDescriptors.AllMethods())
        {
            targets.Add(method.Service.FullName, method.Name);
        }

        return targets;
    }

    /// <summary>
    /// Scans the authored Security contract document for every <c>operationId</c> it declares.
    /// </summary>
    /// <remarks>
    /// A line scan rather than a parse, because this runs during test DISCOVERY where no fixture exists
    /// and where pulling in the YAML reader would duplicate the fixture's job. The scan is deliberately
    /// literal - it reads the key and takes the remainder of the line - and its result is held to
    /// agreement with the parsed document by
    /// <see cref="TheScannedOperationInventoryMatchesTheParsedDocumentExactly"/>, so a scan that drifted
    /// from reality fails there rather than quietly shrinking the sweep.
    /// </remarks>
    private static string[] ScanSecurityOperationIds()
    {
        string text = RequireArtifactText($"{OpenApiDirectoryName}/{SecurityDocumentFileName}");

        List<string> identifiers = [];

        foreach (string line in text.Split('\n'))
        {
            string trimmed = line.Trim();

            if (!trimmed.StartsWith(OperationIdYamlKey, StringComparison.Ordinal))
            {
                continue;
            }

            string identifier = trimmed[OperationIdYamlKey.Length..].Trim();

            if (identifier.Length > 0)
            {
                identifiers.Add(identifier);
            }
        }

        return [.. identifiers];
    }

    /// <summary>Every operation in the parsed Security document, with its route and verb.</summary>
    private IEnumerable<LocatedOperation> EnumerateOperations()
    {
        foreach ((string route, IOpenApiPathItem item) in documents.Security.Paths)
        {
            if (item.Operations is null)
            {
                continue;
            }

            foreach ((HttpMethod verb, OpenApiOperation operation) in item.Operations)
            {
                yield return new LocatedOperation(route, verb, operation);
            }
        }
    }

    /// <summary>
    /// Resolves one operation by identifier, failing with the available identifiers listed when it is
    /// absent.
    /// </summary>
    /// <remarks>
    /// A row naming an operation the document no longer declares is a real finding - either the document
    /// changed or the static scan drifted - so it fails loudly here rather than being skipped.
    /// </remarks>
    private LocatedOperation RequireOperation(string operationId)
    {
        foreach (LocatedOperation located in EnumerateOperations())
        {
            if (string.Equals(OperationIdOf(located.Operation), operationId, StringComparison.Ordinal))
            {
                return located;
            }
        }

        string available = string.Join(
            ", ",
            EnumerateOperations()
                .Select(static located => OperationIdOf(located.Operation))
                .OrderBy(static id => id, StringComparer.Ordinal));

        throw FailException.ForFailure(
            $"Operation '{operationId}' is not declared by {SecurityDocumentFileName}. Declared "
                + $"operations: {available}.");
    }

    /// <summary>The operation's identifier, or the empty string when it declares none.</summary>
    private static string OperationIdOf(OpenApiOperation operation) =>
        string.IsNullOrEmpty(operation.OperationId) ? string.Empty : operation.OperationId;

    /// <summary>
    /// Every property reachable from an operation's INBOUND surface - its request body content and its
    /// parameters - transitively and with a cycle guard.
    /// </summary>
    /// <remarks>
    /// Parameters are swept as well as bodies, and that is not defensive padding: key material in a query
    /// or header parameter would reach every access log and every proxy on the path, which is strictly
    /// worse than the same value in a body.
    /// </remarks>
    private static IReadOnlyList<SchemaProperty> RequestProperties(OpenApiOperation operation)
    {
        List<SchemaProperty> found = [];

        if (operation.RequestBody?.Content is { } content)
        {
            foreach ((string mediaType, OpenApiMediaType media) in content)
            {
                found.AddRange(SweepSchemaGraph(media.Schema, $"requestBody[{mediaType}]"));
            }
        }

        foreach (IOpenApiParameter parameter in operation.Parameters ?? [])
        {
            string name = string.IsNullOrEmpty(parameter.Name) ? string.Empty : parameter.Name;
            string path = $"parameter[{parameter.In}].{name}";

            found.Add(new SchemaProperty(path, name, parameter.Schema));
            found.AddRange(SweepSchemaGraph(parameter.Schema, path));
        }

        return found;
    }

    /// <summary>
    /// Every property reachable from an operation's OUTBOUND surface - the content of every declared
    /// response, whatever its status code - transitively and with a cycle guard.
    /// </summary>
    /// <remarks>
    /// Every status code is swept, not only the success one. An error body is exactly where a leaked value
    /// is least expected and most likely: a diagnostic payload assembled to help someone debug is written
    /// by an author thinking about clarity rather than about disclosure.
    /// </remarks>
    private static IReadOnlyList<SchemaProperty> ResponseProperties(OpenApiOperation operation)
    {
        List<SchemaProperty> found = [];

        if (operation.Responses is null)
        {
            return found;
        }

        foreach ((string statusCode, IOpenApiResponse response) in operation.Responses)
        {
            if (response.Content is not { } content)
            {
                continue;
            }

            foreach ((string mediaType, OpenApiMediaType media) in content)
            {
                found.AddRange(SweepSchemaGraph(media.Schema, $"response[{statusCode}][{mediaType}]"));
            }
        }

        return found;
    }

    /// <summary>
    /// Walks a schema graph from <paramref name="root"/>, returning every property it reaches with the
    /// path that reached it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Breadth-first over RESOLVED schemas, so a <c>$ref</c> chain is followed to its target before the
    /// cycle guard is consulted and a recursive component therefore terminates. Identity is reference
    /// identity on the resolved schema, which is sound here because the reader stores one instance per
    /// component and hands the same instance back on every access - measured, not assumed.
    /// </para>
    /// <para>
    /// Arrays, additional properties and all three composition keywords are followed, because a property
    /// declared inside an <c>allOf</c> member or an array item schema is as much part of the surface as one
    /// declared directly. A walk that read only <c>properties</c> would miss exactly the shapes an author
    /// reaches for when a schema grows.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<SchemaProperty> SweepSchemaGraph(IOpenApiSchema? root, string rootPath)
    {
        List<SchemaProperty> found = [];
        HashSet<IOpenApiSchema> visited = new(ReferenceEqualityComparer.Instance);
        Queue<(IOpenApiSchema Schema, string Path)> pending = new();

        void Enqueue(IOpenApiSchema? candidate, string path)
        {
            if (candidate is null)
            {
                return;
            }

            IOpenApiSchema resolved = ResolveSchema(candidate);

            if (visited.Add(resolved))
            {
                pending.Enqueue((resolved, path));
            }
        }

        Enqueue(root, rootPath);

        while (pending.Count > 0)
        {
            (IOpenApiSchema schema, string path) = pending.Dequeue();

            if (schema.Properties is { } properties)
            {
                foreach ((string name, IOpenApiSchema child) in properties)
                {
                    found.Add(new SchemaProperty($"{path}.{name}", name, child));
                    Enqueue(child, $"{path}.{name}");
                }
            }

            Enqueue(schema.Items, $"{path}[]");
            Enqueue(schema.AdditionalProperties, $"{path}{{}}");

            foreach (IOpenApiSchema part in schema.AllOf ?? [])
            {
                Enqueue(part, $"{path}/allOf");
            }

            foreach (IOpenApiSchema part in schema.OneOf ?? [])
            {
                Enqueue(part, $"{path}/oneOf");
            }

            foreach (IOpenApiSchema part in schema.AnyOf ?? [])
            {
                Enqueue(part, $"{path}/anyOf");
            }
        }

        return found;
    }

    /// <summary>Follows a <c>$ref</c> chain to the schema it ultimately names.</summary>
    /// <remarks>
    /// Bounded by <see cref="MaxReferenceHops"/> so a pathological self-referencing chain terminates as a
    /// failing assertion somewhere rather than as a hung run. An unresolvable reference yields the holder
    /// itself, whose members read as absent; the document tests separately assert that the reader produced
    /// no warnings, and an unresolved <c>$ref</c> is one of the conditions that produces one.
    /// </remarks>
    private static IOpenApiSchema ResolveSchema(IOpenApiSchema schema)
    {
        IOpenApiSchema current = schema;

        for (int hop = 0; hop < MaxReferenceHops; hop++)
        {
            if (current is not OpenApiSchemaReference reference || reference.Target is not { } target)
            {
                break;
            }

            current = target;
        }

        return current;
    }

    /// <summary>The component name a schema references, or <see langword="null"/> when it is inline.</summary>
    private static string? ReferenceIdOf(IOpenApiSchema? schema) =>
        schema is OpenApiSchemaReference reference ? reference.Reference?.Id : null;

    /// <summary>
    /// Resolves a component schema by name, failing with the available names listed when it is absent.
    /// </summary>
    private IOpenApiSchema RequireComponentSchema(string name)
    {
        IDictionary<string, IOpenApiSchema>? schemas = documents.Security.Components?.Schemas;

        if (schemas is not null
            && schemas.TryGetValue(name, out IOpenApiSchema? schema)
            && schema is not null)
        {
            return schema;
        }

        string available = schemas is null
            ? "the document declares no component schemas at all"
            : string.Join(", ", schemas.Keys.OrderBy(static key => key, StringComparer.Ordinal));

        throw FailException.ForFailure(
            $"Component schema '{name}' was not found in {SecurityDocumentFileName}. Available: "
                + $"{available}.");
    }

    /// <summary>Whether an operation belongs to the crypto surface, per the document's own tag.</summary>
    /// <remarks>
    /// Read from the document rather than from a written-out route list, so an operation added to the
    /// crypto surface later is swept without anybody remembering to extend a roster - which is precisely
    /// when an inbound key path would arrive unnoticed.
    /// </remarks>
    private static bool IsCryptoOperation(OpenApiOperation operation) =>
        operation.Tags is { } tags
        && tags.Any(static tag =>
            string.Equals(tag.Reference?.Id, CryptoServiceTagName, StringComparison.Ordinal));

    /// <summary>
    /// Whether a route names one of the cryptographic families the legacy keys, and which therefore must
    /// consume an opaque key reference.
    /// </summary>
    /// <remarks>
    /// Matched per ROUTE SEGMENT so that a family name cannot be found accidentally inside an unrelated
    /// word elsewhere in the path, and as a substring within the segment so that a compound segment - the
    /// file-taking variant of the keyed hash family, for instance - is recognised as the same family.
    /// </remarks>
    private static bool IsKeyedFamilyRoute(string route) =>
        route.Split('/').Any(static segment =>
            KeyedCryptoFamilyRouteFragments.Any(fragment =>
                segment.Contains(fragment, StringComparison.OrdinalIgnoreCase)));

    /// <summary>
    /// Whether an operation PRODUCES key material, detected from its own response shape.
    /// </summary>
    /// <remarks>
    /// Detected rather than declared, and detected from the RESPONSE specifically, so that the test which
    /// asserts what a request may carry does not depend on what that same request carries - which would be
    /// circular and would pass no matter what the document said.
    /// </remarks>
    private static bool ProducesKeyMaterial(OpenApiOperation operation) =>
        ResponseProperties(operation).Any(IsKeyMaterialProperty);

    /// <summary>Whether a property names key material rather than an opaque reference to it.</summary>
    private static bool IsKeyMaterialProperty(SchemaProperty property) =>
        MentionsAny(property.Name, KeyMaterialNameFragments) && !IsOpaqueReferenceName(property.Name);

    /// <summary>Whether a property name marks itself as an opaque reference rather than material.</summary>
    private static bool IsOpaqueReferenceName(string name) =>
        Compact(name).EndsWith(OpaqueReferenceNameSuffix, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Removes the separators that distinguish <c>snake_case</c>, <c>camelCase</c> and <c>kebab-case</c>,
    /// so one vocabulary entry covers every spelling of the same name.
    /// </summary>
    /// <remarks>
    /// Case is deliberately NOT folded here. Every comparison against a compacted name supplies
    /// <see cref="StringComparison.OrdinalIgnoreCase"/> instead, which keeps this method culture-free and
    /// avoids the case-mapping surprises that make a lower-casing helper a poor place to normalise.
    /// </remarks>
    private static string Compact(string name)
    {
        StringBuilder compacted = new(name.Length);

        foreach (char character in name)
        {
            if (character is '_' or '-' or '.' or ' ')
            {
                continue;
            }

            compacted.Append(character);
        }

        return compacted.ToString();
    }

    /// <summary>Whether a name, once compacted, contains the given fragment.</summary>
    private static bool Mentions(string name, string fragment) =>
        Compact(name).Contains(fragment, StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether a name, once compacted, contains any of the given fragments.</summary>
    private static bool MentionsAny(string name, IReadOnlyList<string> fragments)
    {
        string compacted = Compact(name);

        foreach (string fragment in fragments)
        {
            if (compacted.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether a block of authored text contains any of the given fragments, WITHOUT compaction.
    /// </summary>
    /// <remarks>
    /// Comments are prose rather than identifiers, so compaction would be wrong here: it would fuse
    /// separate words and make a fragment such as <c>write only</c> impossible to express.
    /// </remarks>
    private static bool MentionsAnyLiteral(string text, IReadOnlyList<string> fragments)
    {
        foreach (string fragment in fragments)
        {
            if (text.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether a name, once compacted, equals any of the given names exactly.</summary>
    private static bool EqualsAny(string name, IReadOnlyList<string> names)
    {
        string compacted = Compact(name);

        foreach (string candidate in names)
        {
            if (string.Equals(compacted, candidate, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Renders paths one per line, indented, for a failure message.</summary>
    private static string FormatPaths(IReadOnlyCollection<string> paths) =>
        paths.Count == 0
            ? "  (none)"
            : string.Join(Environment.NewLine, paths.Select(static path => $"  {path}"));

    /// <summary>
    /// Renders discovered properties one per line, naming the component each resolves to so a reader can
    /// tell an inline schema from a shared one.
    /// </summary>
    private static string FormatProperties(IReadOnlyCollection<SchemaProperty> properties) =>
        properties.Count == 0
            ? "  (none)"
            : string.Join(
                Environment.NewLine,
                properties.Select(static property =>
                    $"  {property.Path} (schema: {ReferenceIdOf(property.Schema) ?? "inline"})"));

    /// <summary>Reads one protocol definition's authored text.</summary>
    private static string RequireProtoText(string protoFileName) =>
        RequireArtifactText($"{ProtoDirectoryName}/{protoFileName}");

    /// <summary>
    /// Reads a contract artifact's authored text from the contracts project directory.
    /// </summary>
    /// <remarks>
    /// <para>
    /// STRICTLY READ-ONLY, and nowhere near the legacy tree (C-C). The path resolved is always inside
    /// <c>shared/PowerFramework.Contracts</c>; nothing here opens <c>ws_objects/**</c>, a native binary,
    /// a <c>*.pbl</c>, <c>*.pbt</c>, <c>*.pbw</c>, <c>*.pbr</c> or <c>*.pbd</c> file, or any of the
    /// pre-existing documents under <c>docs/</c>. Nothing is created, written, moved or deleted.
    /// </para>
    /// <para>
    /// AN UNRESOLVABLE ARTIFACT FAILS RATHER THAN SKIPS, deliberately. Quietly standing down would turn
    /// every documentation assertion in this file into a test that reports success without reading
    /// anything, which for an auditable security control is the worst available outcome. The failure names
    /// the path attempted so the cause is obvious.
    /// </para>
    /// </remarks>
    private static string RequireArtifactText(string artifactRelativePath)
    {
        string contractsDirectory = RequireContractsDirectory();

        string[] segments = artifactRelativePath.Split('/');
        string path = Path.Combine([contractsDirectory, .. segments]);

        if (!File.Exists(path))
        {
            throw FailException.ForFailure(
                $"The contract artifact '{artifactRelativePath}' was not found at '{path}'. It is the "
                    + "subject of a documentation-level assertion, so it cannot be stood down from: "
                    + "either the artifact moved, in which case this row's path needs updating, or it is "
                    + "missing, which is itself the finding.");
        }

        string text = File.ReadAllText(path);

        if (string.IsNullOrWhiteSpace(text))
        {
            throw FailException.ForFailure(
                $"The contract artifact '{artifactRelativePath}' at '{path}' is present but empty.");
        }

        return text;
    }

    /// <summary>
    /// The files one published contract folder holds, as forward-slashed paths relative to the project.
    /// </summary>
    /// <remarks>
    /// A TOP-LEVEL enumeration, matching the flat layout both folders actually have, and the absent or
    /// empty folder is a failure rather than an empty result: a coverage assertion that stood down when
    /// its subject disappeared would report success on a boundary that had lost its definitions.
    /// </remarks>
    /// <exception cref="FailException">The folder is missing, or holds no file at all.</exception>
    private static IEnumerable<string> EnumerateContractFolder(
        string contractsDirectory,
        string folderName)
    {
        string folder = Path.Combine(contractsDirectory, folderName);

        if (!Directory.Exists(folder))
        {
            throw FailException.ForFailure(
                $"The published contract folder '{folderName}' was not found at '{folder}'. Both "
                    + $"'{ProtoDirectoryName}' and '{OpenApiDirectoryName}' are part of the published "
                    + "boundary, so neither can be stood down from.");
        }

        string[] names =
            [.. Directory
                .EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly)
                .Select(static path => Path.GetFileName(path))];

        if (names.Length == 0)
        {
            throw FailException.ForFailure(
                $"The published contract folder '{folderName}' at '{folder}' holds no file, which is "
                    + "itself the finding rather than a reason to report nothing to sweep.");
        }

        return names.Select(name => $"{folderName}/{name}");
    }

    /// <summary>
    /// Walks upward from the test assembly for the contracts project directory.
    /// </summary>
    /// <remarks>
    /// An upward SEARCH rather than a fixed number of parent hops, because the distance between a test
    /// assembly and the repository root is a function of the configuration and target framework in the
    /// output path, so a fixed count breaks silently when either changes. The search anchors on the SAME
    /// TWO repository-root markers the shared document fixture uses, together with the project directory
    /// itself, so it cannot latch onto a same-named directory elsewhere on the machine.
    /// </remarks>
    private static string RequireContractsDirectory()
    {
        // STARTS AT THE EMBEDDED REPOSITORY ROOT WHEN THE BUILD SUPPLIED ONE, so this locator works when
        // the test output sits outside the checkout - `dotnet test --artifacts-path` - where no ancestor
        // of the output directory carries the marker below. The walk itself is unchanged and still
        // verifies that marker, so an absent or stale value simply falls back to the previous start.
        // See TestRepositoryRoot.
        DirectoryInfo? candidate = new(TestRepositoryRoot.SearchStart);

        while (candidate is not null)
        {
            string contracts = Path.Combine(
                candidate.FullName,
                SharedDirectoryName,
                ContractsProjectDirectoryName);

            if (Directory.Exists(contracts)
                && File.Exists(Path.Combine(candidate.FullName, SolutionMarkerFileName))
                && File.Exists(Path.Combine(candidate.FullName, PackageVersionMarkerFileName)))
            {
                return contracts;
            }

            // Parent is null at the filesystem root, which is what terminates the walk.
            candidate = candidate.Parent;
        }

        throw FailException.ForFailure(
            $"No directory from '{AppContext.BaseDirectory}' up to the filesystem root holds "
                + $"'{SharedDirectoryName}/{ContractsProjectDirectoryName}' together with both repository "
                + $"markers '{SolutionMarkerFileName}' and '{PackageVersionMarkerFileName}', so the "
                + "authored contract artifacts could not be located. Both probes are read-only; nothing "
                + "was created.");
    }

    /// <summary>
    /// Reads the contiguous leading comment block immediately above a field declaration inside a message.
    /// </summary>
    /// <param name="protoFileName">Protocol definition file name, within the <c>Proto</c> directory.</param>
    /// <param name="messageName">Simple message name, as spelled in the definition.</param>
    /// <param name="fieldDeclaration">The field's declaration line, trimmed - for example <c>string x = 1;</c>.</param>
    /// <remarks>
    /// Reading the authored text is the only way to reach a comment: measured on this toolchain,
    /// <c>FieldDescriptor.Declaration</c> is <see langword="null"/> for every field because
    /// Grpc.Tools does not request protoc's source-info option, so the compiled descriptors carry no
    /// comments at all.
    /// </remarks>
    private static string RequireFieldLeadingComment(
        string protoFileName,
        string messageName,
        string fieldDeclaration)
    {
        string[] lines = RequireMessageBody(protoFileName, messageName);

        int declarationIndex = Array.FindIndex(
            lines,
            line => string.Equals(line.Trim(), fieldDeclaration, StringComparison.Ordinal));

        if (declarationIndex < 0)
        {
            throw FailException.ForFailure(
                $"The declaration '{fieldDeclaration}' was not found inside 'message {messageName}' in "
                    + $"{protoFileName}. Either the field was renamed or renumbered - both of which change "
                    + "the contract - or this assertion's expectation is stale.");
        }

        return LeadingCommentBlock(lines, declarationIndex);
    }

    /// <summary>
    /// Reads the contiguous leading comment block immediately above a message declaration.
    /// </summary>
    private static string RequireMessageLeadingComment(string protoFileName, string messageName)
    {
        string[] lines = RequireProtoText(protoFileName).Split('\n');

        int declarationIndex = Array.FindIndex(
            lines,
            line => string.Equals(
                line.Trim(),
                $"message {messageName} {{",
                StringComparison.Ordinal));

        if (declarationIndex < 0)
        {
            throw FailException.ForFailure(
                $"'message {messageName}' was not found in {protoFileName}.");
        }

        return LeadingCommentBlock(lines, declarationIndex);
    }

    /// <summary>
    /// The lines of one message's body, from its declaration line to the closing brace in column one.
    /// </summary>
    private static string[] RequireMessageBody(string protoFileName, string messageName)
    {
        string[] lines = RequireProtoText(protoFileName).Split('\n');

        int start = Array.FindIndex(
            lines,
            line => string.Equals(
                line.Trim(),
                $"message {messageName} {{",
                StringComparison.Ordinal));

        if (start < 0)
        {
            throw FailException.ForFailure(
                $"'message {messageName}' was not found in {protoFileName}.");
        }

        int end = Array.FindIndex(
            lines,
            start + 1,
            line => line.TrimEnd('\r').Equals("}", StringComparison.Ordinal));

        if (end < 0)
        {
            throw FailException.ForFailure(
                $"The body of 'message {messageName}' in {protoFileName} is not closed by a brace in "
                    + "column one, so it could not be delimited.");
        }

        return lines[start..end];
    }

    /// <summary>
    /// The contiguous run of comment lines immediately above <paramref name="declarationIndex"/>, joined
    /// in source order.
    /// </summary>
    /// <remarks>
    /// The run stops at the first line that is not a comment, which is what makes it the declaration's OWN
    /// documentation rather than everything that happens to precede it in the file.
    /// </remarks>
    private static string LeadingCommentBlock(string[] lines, int declarationIndex)
    {
        int first = declarationIndex;

        while (first > 0 && lines[first - 1].TrimStart().StartsWith("//", StringComparison.Ordinal))
        {
            first--;
        }

        return string.Join(
            Environment.NewLine,
            lines[first..declarationIndex].Select(static line => line.Trim()));
    }
}
