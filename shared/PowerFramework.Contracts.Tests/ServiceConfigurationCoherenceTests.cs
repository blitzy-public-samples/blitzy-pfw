// ==================================================================================================
//  ServiceConfigurationCoherenceTests - THE MECHANICAL GUARD OVER THE CROSS-SERVICE CONFIGURATION
//  CONTRACT
//  ------------------------------------------------------------------------------------------------
//  SUBJECT     The eight authored settings files of the four Phase-1 services:
//                  services/persistence-service/PowerFramework.Persistence/appsettings[.Development].json
//                  services/dataservices-service/PowerFramework.DataServices/appsettings[.Development].json
//                  services/security-service/PowerFramework.Security/appsettings[.Development].json
//                  services/gateway-service/PowerFramework.Gateway/appsettings[.Development].json
//  AUTHORITY   Agent Action Plan 0.3.2.2 (the fixed port map), 0.4.3 (the contract inventory),
//              0.6.6.3 (the token topology - exactly one issuer), 0.7.3 constraints C-F and C-G,
//              docs/ARCHITECTURE.md 4.1 and 4.2, and the published
//              shared/PowerFramework.Contracts/OpenApi/security.v1.yaml.
//
//  WHY A CONTRACTS TEST OWNS THIS, WHICH IS THE FIRST QUESTION A READER SHOULD HAVE
//  ------------------------------------------------------------------------------------------------
//  Every invariant below spans MORE THAN ONE SERVICE. "DataServices' Persistence address names a port
//  and scheme Persistence actually listens on" is not a fact about either service alone, so neither
//  service's own test project is the place it can be stated - and a per-service test that asserted its
//  own half would pass on both sides of a mismatch. This project already owns the published
//  cross-service boundary, so it is the one place in the tree where the two halves can be compared.
//
//  It also has to be here for a blunter reason: a service's own test project references that service,
//  and while a service's Program.cs is still a pending file its project does not compile at all. A
//  guard that lived there could not run.
//
//  WHAT THESE ASSERTIONS ARE FOR
//  ------------------------------------------------------------------------------------------------
//  Configuration mismatch is the failure mode that static review catches worst and runtime catches
//  latest. A caller pointed at a port nobody serves, an audience nobody can mint, an option key that
//  binds silently to its default - none of these is a compile error, none is a warning, and each of
//  them surfaces as an unexplained failure three layers away from its cause. Every test below turns
//  one of those into a build-time failure that names the offending key.
//
//  Six invariants are asserted:
//
//    1. ONE LISTENER PER SERVICE, ON THE PORT THE MAP ASSIGNS IT. No second endpoint, no undeclared
//       port. A service that needs two ports has two addresses for its callers to keep in step, and
//       that is exactly how a caller ends up pointed at the half that cannot serve it.
//    2. EVERY CALLER ADDRESS RESOLVES TO A DECLARED LISTENER, on scheme AND port. This is the
//       assertion that would have caught a gRPC client aimed at an HTTP/1-only endpoint.
//    3. TLS WHERE TLS IS LOAD-BEARING. Persistence and DataServices each carry gRPC (HTTP/2) and an
//       HTTP/1.1 readiness probe on ONE port, which requires ALPN and therefore TLS - measured, and
//       recorded in docs/ARCHITECTURE.md 4.1. Security's listener additionally requests a client
//       certificate, because token issuance is the system's one mutual-TLS edge.
//    4. THE SECURITY OPTION GRAPH IS EXACTLY THE AUTHORITATIVE ONE. A settings key that no option
//       property reads is dead configuration; an option property that no settings key supplies binds
//       silently to its default. Both directions are checked.
//    5. EVERY PROTECTED INBOUND AUDIENCE IS ISSUABLE. Security is the sole issuer, so an audience a
//       service validates but Security cannot mint makes that service's API unreachable by policy
//       rather than by accident - and it fails closed, with a 401 that looks like a caller error.
//    6. NO SIGNING OR CREDENTIAL MATERIAL IN ANY SETTINGS FILE. C-F, asserted rather than reviewed.
//
//  WHAT THESE ASSERTIONS DELIBERATELY DO NOT DO
//  ------------------------------------------------------------------------------------------------
//  They open no socket, start no host, and read no environment variable. They are assertions about
//  the authored text of eight files, which is what makes them runnable in a clean checkout with no
//  stack up. Runtime behaviour is the end-to-end suite's subject, not this file's.
//
//  They also assert nothing about the four deferred services, and name none of them, because C-D is
//  satisfied by absence rather than by a blocklist.
// ==================================================================================================

using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text.Json.Nodes;
using Xunit;
using Xunit.Sdk;

namespace PowerFramework.Contracts.Tests;

/// <summary>
/// Cross-service assertions over the authored <c>appsettings</c> files: the port map, the caller
/// addresses, the transport of each listener, the Security option graph, the issuable audience set,
/// and the absence of credential material.
/// </summary>
public sealed class ServiceConfigurationCoherenceTests
{
    /// <summary>Repository-root marker: the solution file, in the .NET 10 XML format.</summary>
    private const string SolutionMarkerFileName = "PowerFramework.slnx";

    /// <summary>Repository-root marker: the central package-version manifest.</summary>
    private const string PackageVersionMarkerFileName = "Directory.Packages.props";

    /// <summary>The directory holding the four service trees.</summary>
    private const string ServicesDirectoryName = "services";

    /// <summary>Base settings file name, loaded in every environment.</summary>
    private const string BaseSettingsFileName = "appsettings.json";

    /// <summary>Development overlay file name.</summary>
    private const string DevelopmentSettingsFileName = "appsettings.Development.json";

    /// <summary>
    /// The lowest and highest port of the band the attached environment fixes and the port map
    /// preserves. Anything outside it is an undeclared port by definition.
    /// </summary>
    private const int LowestDocumentedPort = 5101;

    /// <summary>The highest port of the documented band. See <see cref="LowestDocumentedPort"/>.</summary>
    private const int HighestDocumentedPort = 5105;

    /// <summary>
    /// The port the map reserves for the deferred DesignSystem service. No listener and no caller
    /// address may name it in this phase.
    /// </summary>
    private const int ReservedPhaseTwoPort = 5103;

    /// <summary>
    /// Reading a settings file the way the host reads it: comments skipped and trailing commas
    /// tolerated, because the authored files use both.
    /// </summary>
    /// <remarks>
    /// This mirrors <c>JsonConfigurationFileParser</c>. Parsing these files with stock strict JSON
    /// would fail on the DataServices pair, which is heavily commented - so a strict parse here would
    /// be a test that fails for a reason having nothing to do with the invariant under test.
    /// </remarks>
    private static readonly JsonDocumentOptions SettingsDocumentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// The four services, each with the port the map assigns it and the transport it must declare.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Transcribed from AAP 0.3.2.2 and docs/ARCHITECTURE.md 4.1. ALL FOUR services declare a Kestrel
    /// endpoint, Gateway included, and Gateway's inclusion is load-bearing rather than a detail: leaving
    /// its address to the orchestration layer is the tempting shape, and because the orchestration
    /// template's roster carries no <c>ASPNETCORE_URLS</c>, nothing in this repository would then bind
    /// 5105 - a plain run binds Kestrel's own default of 5000, leaving the composition root's documented
    /// access URL reachable by no means the repository provides. <see cref="ServiceProfile.DeclaresListener"/>
    /// is therefore <see langword="true"/> for every service.
    /// </para>
    /// <para>
    /// EVERY LISTENER IS TLS, AND EVERY SERVICE BINDS EXACTLY ONE OF THEM. The environment's readiness
    /// gates are spelled as plaintext URLs, but a gate fixes the probe SHAPE - an anonymous <c>GET</c> of
    /// <c>/health</c> on the documented port answering 200 - and not the transport beneath it, so
    /// honouring it with a trust anchor added takes no deviation under AAP 0.8.3. Every listener here is
    /// therefore <c>https</c>, which AAP 0.1.4 requires of a surface decomposition itself created.
    /// Persistence and DataServices serve gRPC contracts AND an HTTP/1.1 readiness probe from the SAME
    /// endpoint, which over TLS is exactly what ALPN is for: <c>Protocols: Http1AndHttp2</c> on the one
    /// port the map assigns them carries both, and a measured probe confirmed it answers an HTTP/1.1
    /// <c>GET /health</c> with 200 and a gRPC unary call with HTTP/2 on that single port. Security is
    /// REST-only and Gateway is the REST ingress, so both declare <c>Http1</c>.
    /// <see cref="ServiceProfile.ServesGrpc"/> is what selects between the two protocol values, and it is
    /// <see langword="true"/> for exactly the two services that publish a gRPC contract.
    /// </para>
    /// <para>
    /// A SECOND ENDPOINT PER PROTOCOL VERSION IS RULED OUT BY THE PORT MAP RATHER THAN BY THE TRANSPORT.
    /// Giving Persistence a second listener on 5111 and DataServices one on 5112, both <c>Http2</c>-only,
    /// so that a listener accepts only what it is for, is the tempting shape. AAP 0.3.2.2
    /// assigns contracts C-05..C-08 to port 5101 and C-03/C-04 to port 5102, so a gRPC contract answering
    /// on 5111 or 5112 answers on a port the AAP never names while the port it does name carries only
    /// the probe - the assignment, not the diagnostic convenience of one-version-per-port, is what governs.
    /// The band assertion below therefore admits ONE port per service and nothing else, and 5103 stays
    /// unallocated (constraint C-D) because nothing moves into it.
    /// </para>
    /// </remarks>
    private static readonly ServiceProfile[] Services =
    [
        new(
            Key: "persistence",
            DirectoryName: "persistence-service",
            ProjectName: "PowerFramework.Persistence",
            Port: 5101,
            ServesGrpc: true,
            DeclaresListener: true,
            ListenerScheme: Uri.UriSchemeHttps,
            RequiresClientCertificateMode: false),
        new(
            Key: "dataservices",
            DirectoryName: "dataservices-service",
            ProjectName: "PowerFramework.DataServices",
            Port: 5102,
            ServesGrpc: true,
            DeclaresListener: true,
            ListenerScheme: Uri.UriSchemeHttps,
            RequiresClientCertificateMode: false),
        new(
            Key: "security",
            DirectoryName: "security-service",
            ProjectName: "PowerFramework.Security",
            Port: 5104,
            ServesGrpc: false,
            DeclaresListener: true,
            ListenerScheme: Uri.UriSchemeHttps,
            RequiresClientCertificateMode: true),
        new(
            Key: "gateway",
            DirectoryName: "gateway-service",
            ProjectName: "PowerFramework.Gateway",
            Port: 5105,
            ServesGrpc: false,
            DeclaresListener: true,
            ListenerScheme: Uri.UriSchemeHttps,
            RequiresClientCertificateMode: false),
    ];

    /// <summary>
    /// Every address one service holds for another, as a configuration key path per settings file.
    /// </summary>
    /// <remarks>
    /// The paths are spelled exactly rather than searched for. A guard that hunted through candidate
    /// spellings would keep passing after the real key was renamed, which is the failure mode this
    /// file exists to prevent.
    /// </remarks>
    private static readonly CallerAddress[] CallerAddresses =
    [
        // The two gRPC call edges are marked as such, and every other address names a REST surface. Both
        // kinds resolve to the SAME port now that each service binds one endpoint carrying both protocol
        // versions, so the distinction no longer selects a port - it asserts that a gRPC edge names a
        // service that actually publishes a gRPC contract. That is still worth keeping separately: an
        // address moved onto Security or Gateway, neither of which serves gRPC, would otherwise look
        // well-formed here and fail only at the first call.
        new("gateway", BaseSettingsFileName, "Gateway:Upstreams:DataServices", "dataservices", TargetEndpoint.Grpc),
        new("gateway", BaseSettingsFileName, "Gateway:Upstreams:Security", "security", TargetEndpoint.Rest),
        new("gateway", BaseSettingsFileName, "Gateway:HealthProbes:Persistence", "persistence", TargetEndpoint.Rest),
        new("gateway", BaseSettingsFileName, "Gateway:HealthProbes:DataServices", "dataservices", TargetEndpoint.Rest),
        new("gateway", BaseSettingsFileName, "Gateway:HealthProbes:Security", "security", TargetEndpoint.Rest),
        new("gateway", DevelopmentSettingsFileName, "Gateway:Upstreams:DataServices", "dataservices", TargetEndpoint.Grpc),
        new("gateway", DevelopmentSettingsFileName, "Gateway:Upstreams:Security", "security", TargetEndpoint.Rest),
        new("gateway", DevelopmentSettingsFileName, "Gateway:HealthProbes:Persistence", "persistence", TargetEndpoint.Rest),
        new("gateway", DevelopmentSettingsFileName, "Gateway:HealthProbes:DataServices", "dataservices", TargetEndpoint.Rest),
        new("gateway", DevelopmentSettingsFileName, "Gateway:HealthProbes:Security", "security", TargetEndpoint.Rest),
        new("gateway", DevelopmentSettingsFileName, "Authentication:Schemes:Bearer:Authority", "security", TargetEndpoint.Rest),
        new("dataservices", BaseSettingsFileName, "DataServices:Persistence:Address", "persistence", TargetEndpoint.Grpc),
        new("dataservices", BaseSettingsFileName, "DataServices:Security:BaseAddress", "security", TargetEndpoint.Rest),
        new("dataservices", BaseSettingsFileName, "Authentication:Jwt:Authority", "security", TargetEndpoint.Rest),
        new("dataservices", DevelopmentSettingsFileName, "DataServices:Persistence:Address", "persistence", TargetEndpoint.Grpc),
        new("dataservices", DevelopmentSettingsFileName, "DataServices:Security:BaseAddress", "security", TargetEndpoint.Rest),
        new("dataservices", DevelopmentSettingsFileName, "Authentication:Jwt:Authority", "security", TargetEndpoint.Rest),
        new("persistence", BaseSettingsFileName, "Jwt:Authority", "security", TargetEndpoint.Rest),
        new("persistence", DevelopmentSettingsFileName, "Jwt:Authority", "security", TargetEndpoint.Rest),
        new("security", BaseSettingsFileName, "Security:Issuer", "security", TargetEndpoint.Rest),
        new("security", DevelopmentSettingsFileName, "Security:Issuer", "security", TargetEndpoint.Rest),
    ];

    /// <summary>
    /// The configuration key each service's inbound bearer audience is declared at.
    /// </summary>
    /// <remarks>
    /// Three different spellings for the same concept, and the asymmetry is deliberate rather than
    /// drift: Gateway configures the framework's stock handler directly and therefore binds at the
    /// stock <c>Authentication:Schemes:Bearer</c> path with a LIST, while the other services expose
    /// their own typed options and carry a SINGLE value. The list form is why
    /// <see cref="ReadAudiences"/> accepts both node kinds.
    /// </remarks>
    private static readonly InboundAudience[] InboundAudiences =
    [
        new("gateway", "Authentication:Schemes:Bearer:ValidAudiences"),
        new("dataservices", "Authentication:Jwt:Audience"),
        new("persistence", "Jwt:Audience"),
        new("security", "Authentication:Jwt:Audience"),
    ];

    /// <summary>
    /// The complete authoritative leaf set of the <c>Security</c> section - twenty-two entries, no
    /// twenty-third.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE TWO ARRAY KINDS ARE COUNTED DIFFERENTLY, AND THE DIFFERENCE IS THE BINDER'S OWN. An array of
    /// SCALARS binds wholesale to one collection property, so it is ONE leaf - <c>Security:Audiences</c>
    /// and <c>Security:KeyStore:PermittedKeyRefs</c> are each a single entry here. An array of OBJECTS
    /// binds each element to a nested options type, so every MEMBER of that type is a setting in its own
    /// right and is listed separately with its element index normalised to <c>*</c>. That is why the
    /// grant matrix and the issuance roster contribute member-level entries rather than one opaque name
    /// each: without descending, a roster entry could carry a member the options type does not read, or
    /// lose one it does, and this assertion would see one leaf and notice neither. <see cref="Flatten"/>
    /// implements the rule and records the same reasoning at the call site.
    /// </para>
    /// <para>
    /// <c>SigningKey</c> is absent on purpose and is asserted absent separately: it is the system's
    /// single signing secret and reaches the process through the flat environment key
    /// <c>SECURITY_JWT_SIGNING_KEY</c>, never through a settings file.
    /// </para>
    /// <para>
    /// THE COUNT IS PART OF THE ASSERTION, so it moves only when the option graph genuinely moves. It
    /// grew from twelve when authorization stopped being implicit: the grant matrix makes issuance an
    /// allowlist decision rather than a trust decision, and the two client-certificate leaves make the
    /// mutual-TLS fallback's trust anchor and revocation strictness configurable instead of assumed. It
    /// grew once more, to twenty-one, when signing-key ROLLOVER became expressible: replacing the one
    /// signing key in the estate without publishing the outgoing one alongside it refuses every token
    /// already in flight, so the retiring key's identifier is a leaf. The twenty-second arrived with the
    /// same kind of finding in the opposite direction - the deployment is reached under two origins and the
    /// discovery document could only describe one of them, so the additional origins it answers on became
    /// declarable. All of them are read by the options type and documented in the orchestration template,
    /// which is what qualifies a leaf as authoritative.
    /// </para>
    /// <para>
    /// THE SHRINKING DIRECTION IS THE MORE INTERESTING ONE. Giving the roster entries
    /// <c>Audiences</c> and <c>Scopes</c> members of their own makes the same
    /// permission declarable in two places: the grant matrix, which the issuer consults on every
    /// request, and the roster entry, which NOTHING would read once the matrix exists. A settings file
    /// declaring both invites an operator to tighten the half that decides nothing. Neither member is
    /// therefore on the options type at all rather than cross-checked, so neither has a leaf in
    /// this list - which is exactly the ORPHANED direction of this assertion working as
    /// intended, since a key no options type binds is a key an operator cannot discover is inert.
    /// </para>
    /// </remarks>
    private static readonly string[] AuthoritativeSecurityLeafPaths =
    [
        "Security:Issuer",

        // THE PUBLISHED LOCATION, WHICH IS A DIFFERENT FACT FROM THE IDENTITY ABOVE AND IS WHY BOTH
        // LEAVES EXIST. `Issuer` is the `iss` claim all three verifiers compare byte for byte; this is
        // the set of OTHER addresses this one service is reachable on, and the discovery document
        // composes `jwks_uri` and `token_endpoint` from whichever of them a request arrived on. Both
        // addresses used to be composed from the issuer alone, which handed every consumer outside the
        // Compose network a key-set address naming a host only resolvable inside it. Declared here as a
        // leaf because the base settings file ships it EMPTY - the shipped default reproduces the
        // issuer-only behaviour exactly - and an empty collection still has to be a member the options
        // type reads rather than dead configuration.
        "Security:PublishedOrigins",
        "Security:Audiences",

        // WHICH caller may obtain a token for WHICH audience, carrying WHICH scopes. Separate from
        // Audiences because that leaf states only which audiences this issuer serves at all: a caller
        // being trusted to authenticate is not the same as being permitted the audience it asks for,
        // and collapsing the two would make every authenticated caller able to mint for every audience.
        // Three member-level entries because the rows are an array of OBJECTS - see the leaf-counting
        // note above - and because a row that lost its `Scopes` member would otherwise bind an empty
        // grant, which the issuer refuses at startup for a reason no settings-file reader could see.
        "Security:CallerAuthorizations:*:Caller",
        "Security:CallerAuthorizations:*:Audience",
        "Security:CallerAuthorizations:*:Scopes",

        "Security:TokenLifetime",
        "Security:SigningAlgorithm",

        // ONE SIGNING-KEY POLICY LEAF, AND IT IS AUTHORITATIVE BECAUSE IT IS ENFORCED - which is the
        // only test that qualifies a leaf. SigningKeyFormat is compared against the recognised set by
        // SecurityOptionsValidator's signing-material check, so a deployment naming a format this service
        // cannot import fails the BRING-UP rather than the first issuance.
        //
        // THERE IS DELIBERATELY NO SigningKeyMinimumSizeBits LEAF TO DECLARE, AND THAT IS NOT BECAUSE
        // THERE IS NO FLOOR. A modulus below SecurityOptions.MinimumSigningKeySizeBits fails startup, in
        // SecurityOptionsValidator and again in Tokens/SigningKeyProvider. The floor is a CONSTANT rather
        // than a setting, because a floor an operator can lower is not a floor - and the operator most
        // likely to lower it is one whose deployment already holds a short key. A constant is not a
        // configuration leaf, so nothing belongs in this list for it.
        "Security:SigningKeyFormat",

        "Security:SigningKeyId",

        // THE RETIRING KEY'S IDENTIFIER, AND IT IS AUTHORITATIVE BY THE SAME ENFORCEMENT TEST AS EVERY
        // OTHER LEAF HERE. It is EMPTY as shipped, which is the steady state - no rollover in progress -
        // and the validator refuses a half-configured pair: an identifier with no material publishes
        // nothing while reading as a rollover under way, material with no identifier cannot be published
        // at all because a key set entry has to carry a `kid`, and an identifier repeating the active one
        // is an ambiguous key set rather than a rollover. Declared here EMPTY rather than omitted because
        // this file is meant to be the complete picture of the section: the retiring MATERIAL is
        // environment-only for the usual reason, but its identifier is not secret - a `kid` is published
        // anonymously in the key set by design - and a member an operator cannot discover is a member
        // whose rollover procedure they cannot find either.
        "Security:RetiringSigningKeyId",

        "Security:TokenEndpointPath",
        "Security:JwksPath",
        "Security:OpenIdConfigurationPath",

        // The mutual-TLS fallback's ISSUANCE trust anchor and its revocation strictness. This is the
        // anchor a caller certificate must chain to before POST /v1/tokens honours the identity it
        // carries, and it is a DIFFERENT leaf from Security:MutualTls:ClientCaPath below, which is the
        // LISTENER's. An empty anchor here means the composition root adopts the listener's - the one the
        // orchestration layer actually publishes a variable for - and only when NEITHER is configured
        // does no client-certificate reconciliation happen at all, which is the estate's default because
        // JWT is the default mechanism and mutual TLS is the documented per-pair fallback. Both leaves are
        // declared even though both carry usable defaults, so the settings file stays the complete
        // picture of the section - a member that exists in configuration but in no settings file is one
        // an operator cannot discover.
        "Security:ClientCertificateAuthorityPath",
        "Security:ClientCertificateRevocationMode",

        // THE COMPENSATING CONTROL FOR THE REVOCATION MODE ABOVE. Declared beside it because the two are
        // read together - the ceiling applies only while revocation is not checked - and an operator who
        // strengthens one needs to see the other in the same place.
        "Security:MaxCallerCertificateLifetimeDays",

        "Security:KeyStore:ConfigurationKeyPrefix",
        "Security:KeyStore:PermittedKeyRefs",

        // WHOSE CLIENT CERTIFICATES THE ISSUANCE EDGE ACCEPTS. Token issuance derives the caller's
        // identity from the presented certificate's COMMON NAME, and a name proves nothing unless the
        // chain behind it is verified - so without this leaf a caller could present a self-signed
        // certificate naming any service in the system and be minted that service's token. It carries a
        // PATH and there is deliberately no key-path sibling: an anchor with its private key beside it
        // would let this service issue the very caller identities it authenticates.
        "Security:MutualTls:ClientCaPath",

        // WHO MAY OBTAIN A TOKEN AT ALL, AND UNDER WHICH SECRET - the issuance roster. This is a
        // CREDENTIAL DIRECTORY and not a second authorization gate: it resolves a presented subject to the
        // configuration key naming that caller's secret, and an entry may omit the key entirely to
        // authenticate by client certificate only. The permission decision stays with the grant matrix
        // above, so the two cannot disagree about who is authorised. Four member-level entries because the
        // roster is an array of objects; `SecretConfigurationKey` is a KEY NAME and never a secret, which
        // is what keeps the roster declarable in a settings file at all (constraint C-F).
        // THE CREDENTIAL DIRECTORY, AND EXACTLY TWO MEMBERS OF IT. `Security:Clients` answers who may
        // AUTHENTICATE at the issuance edge and under which secret key name; what an authenticated identity
        // may REQUEST is stated once, in the grant matrix above. Listing `:Audiences` and `:Scopes` here
        // as well binds them, freezes them onto the resolved roster entry, and leaves them consulted by
        // NOTHING - so they advertise permissions the matrix withholds, and an operator editing them
        // changes nothing at all. Their absence from this list is enforced twice over: a settings file
        // declaring either fails this row, and a host started with either present in configuration refuses
        // to start by name (IssuanceRosterAuthority).
        "Security:Clients:*:Subject",
        "Security:Clients:*:SecretConfigurationKey",

        // AND NOTHING FOR `Security:Callers`, WHICH IS AN ABSENCE WITH A REASON RATHER THAN AN OMISSION.
        // That key is the NESTED authoring shape for the same grant matrix - each caller's grants under
        // its identity - and the issuer reads the UNION of it and the flat rows above, so a deployment may
        // author either or both. This deployment authors the flat shape ONLY, and the settings file records
        // why: because the two shapes union rather than collide, stating the matrix twice would let an
        // operator tighten one and leave the other in force with nothing refused and nothing logged. A
        // shape that is supported, validated and enforced but not AUTHORED here is therefore correctly
        // absent from a list of the leaves this file declares.
    ];

    /// <summary>
    /// The complete authoritative option-leaf set of the Persistence service - thirty-two entries, no
    /// thirty-third.
    /// </summary>
    /// <remarks>
    /// Every one of these binds from the configuration ROOT rather than from a service-named wrapper,
    /// because <c>PersistenceOptions</c> is bound from the root and each nested option class binds from
    /// its like-named TOP-LEVEL section. That is what makes the environment-variable contract plain:
    /// <c>Sqlite__Password</c>, not <c>Persistence__Sqlite__Password</c>.
    /// <para>
    /// <c>Sqlite:Password</c> is absent on purpose and is asserted absent by
    /// <see cref="NoSettingsFileDeclaresCredentialMaterial"/>: it carries a credential, so it is
    /// environment-only. Its absence is also what preserves the difference between "no password" and
    /// "an empty password", which an empty placeholder in a settings file would destroy.
    /// </para>
    /// <para>
    /// THERE IS NO REDACTION LEAF, AND ITS ABSENCE IS THE POINT. An earlier shape of this service
    /// carried a <c>Redaction:Enabled</c> switch, so the statement text in an outbound
    /// <c>DbError</c> was masked or not masked according to configuration. That made a security
    /// property deployment-dependent: a stack brought up with the switch off would put the complete
    /// generated statement, interpolated literals and all, onto the network and into the log - which
    /// is precisely the exposure <c>Errors/SqlRedactor.cs</c> exists to close. Redaction is
    /// UNCONDITIONAL and has no configuration surface at all, so a leaf here would name a key nothing
    /// binds and invite an operator to turn off something that cannot be turned off.
    /// </para>
    /// <para>
    /// THERE IS NO <c>Jwt:JwksPath</c> LEAF EITHER, AND THAT IS THE SAME KIND OF ABSENCE. This list
    /// carried one, and the settings file declared it, while <c>AddPersistenceAuthentication</c> read
    /// Authority, Audience, RequireHttpsMetadata, MetadataAddress and the four validation flags and
    /// NEVER read a key-set path. The key was therefore dead configuration an operator would reasonably
    /// believe had an effect - the exact fault the ORPHANED direction of this assertion exists to catch,
    /// which it could not catch while this list expected the orphan. The live mechanism is standard
    /// discovery beneath the bearer authority, with <c>Jwt:MetadataAddress</c> as the one override, so
    /// the option leaf, its settings key and its validator rule were all removed together. Contrast
    /// <c>Security:JwksPath</c>, which is real and stays: Security PUBLISHES the key set at that path,
    /// whereas Persistence only verifies against it.
    /// </para>
    /// </remarks>
    private static readonly string[] AuthoritativePersistenceLeafPaths =
    [
        "Jwt:Authority",

        // MetadataAddress IS THE SUPPORTED RETRIEVAL OVERRIDE, and it is declared and empty rather than
        // absent so the key an operator may legitimately set is visible in the settings file. Program.cs
        // applies it only when non-empty, so null means "resolve discovery from the authority".
        "Jwt:MetadataAddress",
        "Jwt:Audience",
        "Jwt:RequireHttpsMetadata",

        // WHO MAY CALL THIS SERVICE AT ALL. Audience validation proves a token was minted FOR this
        // service; this proves it was minted for a caller this service serves. The AAP fixes the call graph
        // as layered and acyclic - nothing but DataServices calls Persistence - and without this leaf the
        // four contracts admitted any holder of any token minted for this audience, so a credential
        // obtained for retrieval could update rows, delete them or run an arbitrary command. Declared here
        // rather than defaulted in code because the binder POPULATES a collection rather than replacing it,
        // so a non-empty default would accumulate with whatever a deployment declares.
        "Jwt:PermittedCallers",

        // The four token-validation switches. Each removes an entire class of forgery, so none is a
        // deployment choice: the bearer handler is configured with all four enabled regardless of these
        // values, and the options validator REFUSES a false rather than ignoring it - a setting silently
        // ignored is worse than one honoured, because an operator would believe it applied. They remain
        // declared so a deployment can be audited for them by reading its settings file.
        "Jwt:ValidateIssuer",
        "Jwt:ValidateAudience",
        "Jwt:ValidateLifetime",
        "Jwt:ValidateIssuerSigningKey",

        // THE TWO KEY-SET REFRESH INTERVALS, AND THEY ARE DECLARED RATHER THAN DEFAULTED IN CODE ALONE
        // BECAUSE THEY BOUND A SECURITY WINDOW AN OPERATOR MUST BE ABLE TO SEE. Together they fix how long
        // a signing-key rotation at Security leaves this boundary's verdicts inverted - a pre-rotation
        // token still accepted, a post-rotation one refused 401 - and the token library's own defaults for
        // them are five minutes and TWELVE HOURS, so saying nothing was a rotation decision taken by
        // omission. Both are read by the composition root off this section and validated against the
        // library's published minimums at startup.
        "Jwt:MetadataRefreshInterval",
        "Jwt:MetadataAutomaticRefreshInterval",

        "Sqlite:DataDirectory",
        "Sqlite:DatabaseFileName",
        "Sqlite:Mode",
        "Sqlite:Check",
        "Sqlite:Journal",
        "TransactionPool:KeepAlive",
        "TransactionPool:KeepAliveExpireSeconds",
        "TransactionPool:TransactionClassName",
        "TransactionPool:IdleSweepIntervalSeconds",
        "Query:ChunkSize",
        "Query:MaxRows",
        "Query:PageCounting",
        "Query:Cache",
        "Query:PageIndex",
        "Query:PageSize",
        "Query:Paged",

        // The FIVE bounds on server-held work handles. Declared rather than left to code defaults because
        // they are the one part of this service's behaviour an operator may legitimately have to tune per
        // deployment: a ceiling too low refuses correct callers and one too high delays the discovery of a
        // leak, and neither is visible from anywhere but the settings file. The section counts as five
        // leaves because every member is a scalar.
        //
        // 🔴 THE FIFTH IS THE SECOND IDLE WINDOW, AND IT IS PUBLISHED FOR THE SAME REASON THE FIRST IS.
        // A session holding an OPEN, UNCOMMITTED transaction is holding a lock every other writer on the
        // same file needs, so it is governed by a shorter window than a handle holding nothing - a runtime
        // probe measured an abandoned uncommitted write denying every other writer for fourteen and a half
        // minutes under the single generic window. It must not exceed IdleExpirySeconds, which the options
        // validator enforces rather than this list; setting the two equal is the legible way to opt out.
        "Handles:MaxTotalPerRegistry",
        "Handles:MaxPerPrincipal",
        "Handles:IdleExpirySeconds",
        "Handles:UncommittedWorkIdleExpirySeconds",
        "Handles:SweepIntervalSeconds",

        // The trust anchor this service's ONE outbound channel verifies Security against. The bearer
        // handler's key-set backchannel is the channel that decides which keys sign a valid token, and
        // Security's certificate comes from a local authority no container's OS trust store carries -
        // so without this leaf the handler cannot fetch the key set and every inbound token is refused
        // for want of a key rather than on its merits.
        "InternalTls:TrustedCaPath",

        // REVOCATION IS A SETTING, WHICH IS THE POINT - IT USED TO BE A HARDCODED `NoCheck` WITH A
        // COMMENT EXPLAINING IT. Declared here, with its shipped value, so an operator can see the
        // posture the service actually runs under rather than having to read Program.cs to discover
        // that revocation was never checked. The value is a measurement, not a preference: the local
        // certificate authority the operational docs tell an operator to build publishes neither a CRL
        // distribution point nor an OCSP responder, and both strict modes were observed to fail the
        // chain with RevocationStatusUnknown | OfflineRevocation - so a strict default would refuse
        // every internal peer on a clean bring-up. An unrecognised value refuses startup rather than
        // falling back, and the compensating control (a bounded certificate lifetime) is enforced on
        // the Security side rather than asserted in a comment.
        "InternalTls:RevocationMode",

        // THE ELEVEN INGRESS BOUNDS. Declared in the settings file for the reason the handle ceilings
        // above are: decomposition created this service's first-ever listening socket, so there is no
        // legacy value to inherit and every bound is a deployment decision - one too low refuses correct
        // callers and one too high delays the discovery of an abuse, and neither is auditable from
        // anywhere but that file. The two message ceilings are gRPC-specific because gRPC is this
        // service's primary transport; the rest bound the listener and the request layer.
        "Ingress:MaxRequestBodyBytes",
        "Ingress:MaxRequestHeadersTotalBytes",
        "Ingress:MaxConcurrentConnections",
        "Ingress:MaxHttp2StreamsPerConnection",
        "Ingress:RequestHeadersTimeoutSeconds",
        "Ingress:MaxConcurrentRequests",
        "Ingress:RateLimitPermitsPerWindow",
        "Ingress:RateLimitWindowSeconds",
        "Ingress:RateLimitQueueLimit",
        "Ingress:MaxReceiveMessageBytes",
        "Ingress:MaxSendMessageBytes",

        // THE SCHEMA-PROVISIONING SWITCH, DECLARED HERE AND FALSE, WHICH IS THE OPPOSITE OF WHAT THE
        // ORCHESTRATION TEMPLATE SETS. That divergence is the design and not a drift: the CODE default is
        // opted out, so every deployment and every service-level test that does not set it behaves exactly
        // as it did before the switch existed, and the manifest is the single visible place the stack opts
        // in - which is what makes the one documented bring-up command sufficient on a fresh volume. What
        // it buys there is the whole of the readiness chain: this service's probe reports the storage
        // engine's real state, so on a fresh persistence-db volume an unprovisioned database never becomes
        // ready and the health condition holds DataServices and Gateway behind it for ever.
        //
        // It is declared rather than left to the code default because it is the one setting that decides
        // WHO OWNS THE SCHEMA, and an operator cannot audit that from anywhere but this file. It is also
        // deliberately NOT a member of the Sqlite section: that group is the evidenced connection-URI
        // grammar and its member set is pinned by Persistence's own suite precisely so a runtime concern
        // with no place in the URI cannot be added to it.
        "Schema:ApplyMigrationsOnStartup",

        // THE DATA-OBJECT DEFINITIONS A CALLER MAY NAME BY NAME. In the legacy, assigning a name loads a
        // compiled DataWindow out of the target's library list; the `.srd` objects live in the read-only
        // legacy tree and no managed runtime can load one, so what a name resolves TO is a deployment fact.
        // Without this leaf the four contracts could serve only callers that supply their own statement,
        // and the golden-master fixture - the ONLY updatable DataWindow in the repository - would be
        // unreachable, which would make the whole retrieve/validate/update triple unverifiable end to end.
        //
        // SIXTEEN MEMBER-LEVEL LEAVES, NOT ONE OPAQUE NAME, and the difference is the BINDER'S. An array of
        // SCALARS binds wholesale to one collection property and is therefore one leaf - Jwt:PermittedCallers
        // is the case here. This is an array of OBJECTS, so every member of the nested options type is a
        // setting in its own right, and the flattener descends into it for a stated reason: without
        // descending, a definition could carry a member the options type does not read, or LOSE one it does,
        // and this assertion would see one leaf and notice neither. Element indices are normalised to `*`,
        // so this holds the file to the MEMBERS a definition may carry and not to how many definitions a
        // deployment happens to register.
        //
        // Per-entry VALUE rules stay with PersistenceOptionsValidator rather than moving here: a duplicated
        // name, a duplicated column, two identity columns, an update table with no columns or no key, and an
        // unmodelled concurrency mode are each a refusal to start.
        "DataObjects:*:Name",
        "DataObjects:*:SqlSelect",
        "DataObjects:*:Sort",
        "DataObjects:*:Filter",
        "DataObjects:*:Processing",
        "DataObjects:*:Arguments",
        "DataObjects:*:Units",
        "DataObjects:*:UpdateTable",
        "DataObjects:*:UpdateWhere",
        "DataObjects:*:UpdateKeyInPlace",
        "DataObjects:*:Columns:*:Name",
        "DataObjects:*:Columns:*:DbName",
        "DataObjects:*:Columns:*:Update",
        "DataObjects:*:Columns:*:Key",
        "DataObjects:*:Columns:*:Identity",
        "DataObjects:*:Columns:*:UpdateWhereClause",
    ];

    /// <summary>
    /// Top-level sections the ASP.NET Core host binds itself, which therefore belong to no service
    /// option type and are excluded from a leaf inventory.
    /// </summary>
    private static readonly string[] HostOwnedSectionNames = ["Logging", "AllowedHosts", "Kestrel", "Urls"];

    /// <summary>
    /// Log categories whose records can carry a generated SQL statement, and which therefore may never
    /// be configured below <c>Warning</c>.
    /// </summary>
    /// <remarks>
    /// The legacy error structure's statement field carries the COMPLETE generated statement including
    /// interpolated literal values, and the legacy logger performed no redaction at all - which is why
    /// this port has a redactor and why redaction defaults to on. A framework-emitted command log
    /// bypasses that redactor entirely: it is written by Entity Framework, not by application code, so
    /// no application-level control sees it. Raising one of these categories to <c>Information</c> or
    /// below therefore re-exposes exactly the literals the redactor exists to remove, and does it in a
    /// channel the redactor cannot reach (CWE-532).
    /// </remarks>
    private static readonly string[] StatementBearingLogCategories =
    [
        "Microsoft.EntityFrameworkCore.Database.Command",
        "Microsoft.Data.Sqlite",
    ];

    /// <summary>
    /// Log levels that are at or above <c>Warning</c>, and are therefore permitted for a
    /// statement-bearing category.
    /// </summary>
    private static readonly string[] PermittedStatementLogLevels = ["Warning", "Error", "Critical", "None"];

    /// <summary>
    /// Log categories whose records render the FULL request URL, including its query string, and which
    /// therefore may never resolve below <c>Warning</c> in any environment.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 <b>A BEARER TOKEN IN A QUERY PARAMETER WAS WRITTEN TO A CONTAINER LOG IN CLEARTEXT, AND THIS IS
    /// THE CATEGORY THAT WROTE IT.</b> The hosting layer's "Request starting" and "Request finished"
    /// records render the request URL whole. Every application-authored record in this estate uses
    /// <c>Request.Path</c> or the route PATTERN and therefore carries no query string at all, so this
    /// framework-emitted pair is the only channel by which a credential a caller put in a query parameter
    /// reaches an operator's log - and it bypasses every application-level control exactly as the
    /// statement-bearing categories above bypass the SQL redactor (CWE-532).
    /// </para>
    /// <para>
    /// <b>THE SERVICE DOES NOT AND MUST NOT ACCEPT A TOKEN THAT WAY</b> - a query-parameter credential is
    /// answered <c>401</c> on all four services, verified at runtime - so nothing here is about supporting
    /// it. The exposure is that the value is RECORDED on the way to being refused, and a refused
    /// credential is still a live credential until it expires.
    /// </para>
    /// <para>
    /// ONE ENTRY, AND DELIBERATELY NOT A SPECULATIVE LIST. The HTTP-logging middleware would render a URL
    /// too, but no composition root in this estate adds it and its category sits UNDER
    /// <c>Microsoft.AspNetCore</c> - so naming it here would demand a suppression key in three settings
    /// files for a sink that does not exist, which is the kind of inert declaration this suite refuses
    /// elsewhere. This list names what actually emits a URL today.
    /// </para>
    /// </remarks>
    private static readonly string[] UrlBearingLogCategories =
    [
        "Microsoft.AspNetCore.Hosting.Diagnostics",
    ];

    /// <summary>
    /// Log levels that are at or above <c>Warning</c>, and are therefore permitted for a URL-bearing
    /// category.
    /// </summary>
    /// <remarks>
    /// The same four values as the statement-bearing set, and stated separately rather than shared
    /// because the two rules are independent: one is about a generated SQL statement reaching a log and
    /// the other about a request URL doing so. A future decision to permit <c>Information</c> for one of
    /// them must not silently move the other.
    /// </remarks>
    private static readonly string[] PermittedUrlBearingLogLevels = ["Warning", "Error", "Critical", "None"];

    /// <summary>
    /// Property names that may never appear as a settings leaf anywhere, at any depth.
    /// </summary>
    /// <remarks>
    /// C-F expressed as an assertion. <c>SigningKeyId</c> is deliberately NOT on this list and is
    /// deliberately not caught by it: an identifier that names which key signed a token is published
    /// in the JWK set and in every token header, so it is the opposite of secret. The match is on the
    /// whole leaf name rather than a substring, which is what keeps that distinction workable.
    /// </remarks>
    private static readonly string[] ForbiddenCredentialLeafNames =
    [
        "SigningKey",
        "IssuerSigningKey",
        "Secret",
        "ClientSecret",
        "Password",
        "Passphrase",
        "PrivateKey",
        "ApiKey",
    ];

    /// <summary>Every service key, for the theories that iterate all four.</summary>
    public static TheoryData<string> AllServiceKeys { get; } =
        new(Services.Select(static service => service.Key));

    /// <summary>Every service that declares its own Kestrel listener.</summary>
    public static TheoryData<string> ListeningServiceKeys { get; } =
        new(Services.Where(static service => service.DeclaresListener).Select(static service => service.Key));

    /// <summary>Every caller address, identified by its owning service, file and key path.</summary>
    public static TheoryData<string, string, string, string, bool> AllCallerAddresses { get; } =
        BuildCallerAddressData();

    /// <summary>Every settings file, as a service key and file name pair.</summary>
    public static TheoryData<string, string> AllSettingsFiles { get; } = BuildSettingsFileData();

    // ----------------------------------------------------------------------------------------------
    // 1. One listener per service, on the port the map assigns it
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Asserts that a listening service declares exactly ONE endpoint, on the port the map assigns it,
    /// on the scheme the port map records, naming the protocol value its contract surface needs.
    /// </summary>
    /// <param name="serviceKey">The service under test.</param>
    /// <remarks>
    /// <para>
    /// ONE ENDPOINT PER SERVICE IS THE POINT OF THIS TEST, AND THE PROTOCOL VALUE IS HOW A SINGLE PORT
    /// CARRIES TWO SURFACES. Over TLS, <c>Http1AndHttp2</c> genuinely means both: ALPN selects the version
    /// during the handshake, so one listener answers an HTTP/1.1 <c>GET /health</c> AND a gRPC unary call.
    /// A measured probe confirmed exactly that on a single TLS port before this assertion was written to
    /// expect it. Requiring <c>Http1AndHttp2</c> of the two gRPC-serving services and <c>Http1</c> of the
    /// two REST-only ones is what keeps the assignment of AAP 0.3.2.2 - contracts C-05..C-08 on 5101 and
    /// C-03/C-04 on 5102 - true of the listener rather than only of the documentation.
    /// </para>
    /// <para>
    /// THE TLS REQUIREMENT IS LOAD-BEARING HERE, WHICH IS WHY THE SCHEME IS ASSERTED IN THE SAME PLACE.
    /// On CLEARTEXT, <c>Http1AndHttp2</c> does NOT mean both: Kestrel disables HTTP/2 and logs "HTTP/2 is
    /// not enabled ... TLS is not enabled", so a drift back to a plaintext url would silently take every
    /// gRPC call with it while <c>/health</c> kept answering 200 and this file kept reading as correct.
    /// The sibling row below independently forbids any port other than the assigned one, so a regression
    /// that re-added a second protocol-specific listener fails there too.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(ListeningServiceKeys))]
    public void EachListeningServiceDeclaresExactlyOneEndpointOnItsAssignedPort(string serviceKey)
    {
        ServiceProfile service = RequireService(serviceKey);
        JsonObject settings = LoadSettings(service, BaseSettingsFileName);
        JsonObject endpoints = RequireEndpoints(service, settings);

        Assert.Single(endpoints);

        AssertEndpoint(
            service,
            endpoints,
            service.Port,
            service.ServesGrpc ? "Http1AndHttp2" : "Http1");
    }

    /// <summary>
    /// Asserts that a development overlay declaring a listener OVERRIDES the inherited one rather than
    /// adding a second listener beside it.
    /// </summary>
    /// <param name="serviceKey">The service under test.</param>
    /// <remarks>
    /// <para>
    /// WHAT WENT WRONG, AND WHY THE SIBLING ROW ABOVE COULD NOT SEE IT. <c>Kestrel:Endpoints</c> is a
    /// DICTIONARY KEYED BY ENDPOINT NAME and configuration merges per key, so an overlay entry under a
    /// different name does not replace the base entry - it adds one. Security's overlay named its entry
    /// <c>Https</c> where the base file names it <c>Default</c>, so a development bring-up bound TWO
    /// endpoints on the single <c>https://+:5104</c> address, contradicting that file's own statement that
    /// this service binds exactly one endpoint in every environment. The row above reads the base file only
    /// and passed throughout.
    /// </para>
    /// <para>
    /// It asserts NAME CONTAINMENT rather than an exact set, because an overlay is permitted to restate
    /// fewer endpoints than the base declares - what it may not do is introduce a name the base does not
    /// have, since that is precisely the shape that adds a listener instead of replacing one. The merged
    /// count is then checked against the one-listener rule that the port map fixes.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(ListeningServiceKeys))]
    public void ADevelopmentOverlayOverridesTheListenerRatherThanAddingASecondOne(string serviceKey)
    {
        ServiceProfile service = RequireService(serviceKey);

        if (FindNode(LoadSettings(service, DevelopmentSettingsFileName), "Kestrel:Endpoints")
            is not JsonObject overlayEndpoints)
        {
            // Inheriting the base listener outright is the other correct answer, and it cannot produce the
            // defect at all.
            return;
        }

        JsonObject baseEndpoints = RequireEndpoints(service, LoadSettings(service, BaseSettingsFileName));
        HashSet<string> merged = [.. baseEndpoints.Select(static entry => entry.Key)];

        foreach ((string name, JsonNode? _) in overlayEndpoints)
        {
            Assert.True(
                baseEndpoints.ContainsKey(name),
                $"{service.ProjectName}/{DevelopmentSettingsFileName} declares "
                    + $"'Kestrel:Endpoints:{name}', which '{BaseSettingsFileName}' does not. Endpoints merge "
                    + "by name, so this adds a second listener rather than overriding the inherited one. "
                    + $"Reuse the base file's name to override it. Base names: {string.Join(", ", merged)}.");

            merged.Add(name);
        }

        Assert.Single(merged);
    }

    /// <summary>
    /// Asserts that EVERY declared listener - in the base file and in the development overlay alike -
    /// binds the wildcard host, and never a loopback one.
    /// </summary>
    /// <param name="serviceKey">The service under test.</param>
    /// <remarks>
    /// <para>
    /// 🔴 WHAT WENT WRONG, AND WHY EVERY EXISTING ROW MISSED IT. Gateway's development overlay bound
    /// <c>https://localhost:5105</c> while its base file bound <c>https://+:5105</c>. The row above reads
    /// only endpoint NAMES, and the rows before it read only the base file, so the estate's single
    /// deployment-breaking listener passed every assertion in this file.
    /// </para>
    /// <para>
    /// A CONTAINER'S LOOPBACK INTERFACE IS NOT THE HOST'S, which is what makes this a deployment failure
    /// rather than a preference. Bound to <c>localhost</c> the process is reachable from inside its own
    /// container and from nowhere else, so a published port forwards to an address that refuses the
    /// connection - and the estate's documented environment selection for a local stack is
    /// <c>Development</c>, so the overlay is the file that decides.
    /// </para>
    /// <para>
    /// THE FAILURE IS SILENT, WHICH IS WHY IT NEEDS A TEST RATHER THAN A COMMENT. Every image HEALTHCHECK
    /// connects to <c>127.0.0.1</c> - the one interface a loopback binding still serves - so the container
    /// reports healthy, the <c>depends_on: service_healthy</c> chain opens on it, <c>docker compose ps</c>
    /// shows a fully healthy stack, and only an external client discovers that nothing is listening. No
    /// existing gate could see it: not the port map, not the protocol rows, not the container probe.
    /// </para>
    /// <para>
    /// THE WILDCARD IS THE RULE FOR A SETTINGS FILE BECAUSE A SETTINGS FILE SHIPS INSIDE AN IMAGE. A
    /// developer wanting the loopback narrowing for a host-side run states it on that run -
    /// <c>ASPNETCORE_URLS</c> or <c>dotnet run --urls</c> - which is scoped to the run and cannot travel
    /// into a container. The scheme, port and protocol are asserted by the rows above; this row asserts
    /// only the host, so a failure here names exactly one fault.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(ListeningServiceKeys))]
    public void EveryDeclaredListenerBindsTheWildcardHostInEveryEnvironment(string serviceKey)
    {
        ServiceProfile service = RequireService(serviceKey);
        List<string> failures = [];

        foreach (string fileName in new[] { BaseSettingsFileName, DevelopmentSettingsFileName })
        {
            if (FindNode(LoadSettings(service, fileName), "Kestrel:Endpoints") is not JsonObject endpoints)
            {
                // No listener declared in this file: it inherits the one the base file declares, which is
                // the other correct answer and cannot carry the defect.
                continue;
            }

            foreach ((string name, JsonNode? endpoint) in endpoints)
            {
                if (endpoint is not JsonObject declared
                    || declared["Url"]?.GetValue<string>() is not string url)
                {
                    continue;
                }

                // The authority is read textually rather than through Uri, because '+' is not a legal host
                // in an absolute URI and Uri.TryCreate rejects the very value this row requires.
                int schemeEnd = url.IndexOf("://", StringComparison.Ordinal);
                string authority = schemeEnd < 0 ? url : url[(schemeEnd + 3)..];
                string host = authority.Split(':', 2)[0];

                if (string.Equals(host, "+", StringComparison.Ordinal))
                {
                    continue;
                }

                failures.Add(
                    $"{service.ProjectName}/{fileName} binds 'Kestrel:Endpoints:{name}:Url' to host "
                        + $"'{host}' rather than the wildcard '+'. Inside a container that address serves "
                        + "the container's own loopback interface only, so a published port forwards to an "
                        + "address that refuses the connection - and the image HEALTHCHECK connects to "
                        + "127.0.0.1, so the container still reports healthy and the readiness chain still "
                        + "opens. Bind '+' here and narrow a host-side run with ASPNETCORE_URLS instead.");
            }
        }

        Assert.Empty(failures);
    }

    /// <summary>
    /// Asserts that no declared listener and no caller address names a port outside the documented
    /// band, and that none names the port reserved for the deferred Phase-2 service.
    /// </summary>
    /// <param name="service">The service whose endpoints are under test.</param>
    /// <param name="endpoints">Every endpoint the service declares.</param>
    /// <param name="port">The port the endpoint must bind.</param>
    /// <param name="protocols">The exact <c>Protocols</c> value it must name.</param>
    private static void AssertEndpoint(
        ServiceProfile service,
        JsonObject endpoints,
        int port,
        string protocols)
    {
        string suffix = string.Create(CultureInfo.InvariantCulture, $":{port}");

        List<KeyValuePair<string, JsonNode?>> matches =
        [
            .. endpoints.Where(candidate =>
                candidate.Value is JsonObject declared
                && declared["Url"]?.GetValue<string>() is string url
                && url.EndsWith(suffix, StringComparison.Ordinal)),
        ];

        Assert.True(
            matches.Count == 1,
            $"{service.ProjectName}/{BaseSettingsFileName} declares {matches.Count} Kestrel endpoints "
                + $"ending '{suffix}', expected exactly one.");

        (string name, JsonNode? endpointNode) = matches[0];
        JsonObject endpoint = RequireObject(
            endpointNode,
            service,
            BaseSettingsFileName,
            $"Kestrel:Endpoints:{name}");

        Assert.StartsWith(
            service.ListenerScheme + Uri.SchemeDelimiter,
            RequireString(endpoint, "Url", service, $"Kestrel:Endpoints:{name}:Url"),
            StringComparison.Ordinal);

        Assert.Equal(
            protocols,
            RequireString(endpoint, "Protocols", service, $"Kestrel:Endpoints:{name}:Protocols"));
    }

    /// <summary>
    /// Asserts that every declared listener names exactly the one port the map documents for its service,
    /// that none names the port reserved for the deferred Phase-2 service, and that every documented port
    /// sits inside the band the attached environment fixes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE RULE IS "EXACTLY THE ONE PORT THIS SERVICE OWNS", WHICH IS STRICTER THAN A BAND CHECK. An
    /// earlier form of this test asserted only that every listener sat inside the documented
    /// 5101-5105 band; that admitted a service binding a SIBLING'S port, which is a collision the band
    /// alone cannot see.
    /// </para>
    /// <para>
    /// IT IS ALSO WHAT KEEPS A SECOND LISTENER OUT. Giving the two gRPC-serving services an extra
    /// <c>Http2</c>-only endpoint on 5111 and 5112, chosen to sit outside the band so that neither
    /// collides with a documented address nor consumes the reserved slot, is the tempting shape. It is not
    /// available because AAP 0.3.2.2 assigns C-05..C-08 to 5101 and C-03/C-04 to 5102, and a
    /// contract answering on a port the AAP does not name is not on the port the AAP assigns it. With both
    /// surfaces on one <c>Http1AndHttp2</c> endpoint each, "the service's documented port"
    /// is the ONLY admissible port, every documented address stays exactly where the environment put it
    /// (constraint C-L), and 5103 stays unallocated (constraint C-D) because nothing moves into it.
    /// </para>
    /// </remarks>
    [Fact]
    public void NoDeclaredPortFallsOutsideTheDocumentedBand()
    {
        List<string> failures = [];

        foreach (ServiceProfile service in Services.Where(static candidate => candidate.DeclaresListener))
        {
            JsonObject endpoints = RequireEndpoints(service, LoadSettings(service, BaseSettingsFileName));

            foreach ((string name, JsonNode? endpointNode) in endpoints)
            {
                JsonObject endpoint = RequireObject(
                    endpointNode,
                    service,
                    BaseSettingsFileName,
                    $"Kestrel:Endpoints:{name}");

                string url = RequireString(endpoint, "Url", service, $"Kestrel:Endpoints:{name}:Url");
                int port = ParseListenerPort(url, service, $"Kestrel:Endpoints:{name}:Url");

                if (port != service.Port)
                {
                    failures.Add(
                        $"{service.ProjectName} declares 'Kestrel:Endpoints:{name}:Url' on port {port}, "
                            + $"which is not the port {service.Port} the map assigns it. Every service "
                            + "binds exactly one endpoint, on its own documented port.");
                }

                if (port == ReservedPhaseTwoPort)
                {
                    failures.Add(
                        $"{service.ProjectName} declares 'Kestrel:Endpoints:{name}:Url' on the reserved "
                            + $"port {ReservedPhaseTwoPort}, which must stay unallocated in this phase.");
                }
            }

            if (service.Port is < LowestDocumentedPort or > HighestDocumentedPort)
            {
                failures.Add(
                    $"{service.ProjectName}'s documented port {service.Port} falls outside the "
                        + $"{LowestDocumentedPort}-{HighestDocumentedPort} band the attached environment "
                        + "fixes.");
            }

        }

        Assert.Empty(failures);
    }

    // ----------------------------------------------------------------------------------------------
    // 2. Every caller address resolves to a declared listener
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Asserts that one caller address names the scheme and port of the listener its target service
    /// actually declares.
    /// </summary>
    /// <param name="callerKey">Service holding the address.</param>
    /// <param name="fileName">Settings file the address is declared in.</param>
    /// <param name="keyPath">Configuration key path of the address.</param>
    /// <param name="targetKey">Service the address is expected to reach.</param>
    /// <param name="namesGrpcEndpoint">
    /// Whether the address is a gRPC call edge rather than a REST one. Both now resolve to the SAME port,
    /// because each service binds one <c>Http1AndHttp2</c> endpoint carrying both surfaces - so this flag
    /// no longer selects a port. It asserts instead that a gRPC edge names a service that actually
    /// publishes a gRPC contract: an address repointed at Security or Gateway, neither of which serves
    /// one, would otherwise look well-formed and fail only at the first call.
    /// </param>
    [Theory]
    [MemberData(nameof(AllCallerAddresses))]
    public void EachCallerAddressResolvesToTheTargetServiceListener(
        string callerKey,
        string fileName,
        string keyPath,
        string targetKey,
        bool namesGrpcEndpoint)
    {
        ServiceProfile caller = RequireService(callerKey);
        ServiceProfile target = RequireService(targetKey);

        JsonObject settings = LoadSettings(caller, fileName);
        string address = RequireStringAtPath(settings, keyPath, caller, fileName);

        Assert.True(
            Uri.TryCreate(address, UriKind.Absolute, out Uri? parsed),
            $"{caller.ProjectName}/{fileName} key '{keyPath}' is not an absolute address. The value is "
                + "not quoted here because an address key is exactly where a credential-bearing value "
                + "would hide.");

        if (namesGrpcEndpoint && !target.ServesGrpc)
        {
            throw FailException.ForFailure(
                $"{caller.ProjectName}/{fileName} key '{keyPath}' is declared as a gRPC edge, but "
                    + $"{target.ProjectName} publishes no gRPC contract.");
        }

        Assert.Equal(target.ListenerScheme, parsed.Scheme);
        Assert.Equal(target.Port, parsed.Port);
        Assert.Equal(string.Empty, parsed.UserInfo);
    }

    // ----------------------------------------------------------------------------------------------
    // 3. One transport model, stated once and asserted everywhere
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Asserts that a listener declares the client-certificate mode its service actually needs and no
    /// protocol-floor setting at all, so certificate MATERIAL stays in the deployment's secret layer.
    /// </summary>
    /// <param name="serviceKey">The service under test.</param>
    /// <remarks>
    /// <para>
    /// THIS IS A SECRETS CONTROL AND A REACHABILITY CONTROL AT THE SAME TIME, WHICH IS WHY IT IS ONE
    /// TEST. Every listener in this estate is <c>https</c>, so the question is not whether TLS is used
    /// but which parts of it belong in a committed file. A certificate path, key path, password or
    /// thumbprint never does (constraint C-F): those arrive from the deployment's own secret layer as
    /// environment configuration on the same Kestrel keys, which override these files without being
    /// committed to them.
    /// </para>
    /// <para>
    /// <c>ClientCertificateMode</c> IS DIFFERENT AND MUST BE COMMITTED WHERE IT IS NEEDED, because it is
    /// a listener BEHAVIOUR rather than material: it decides whether the handshake asks for a client
    /// certificate at all. Only Security needs it - <c>POST /v1/tokens</c> accepts a certificate identity
    /// as one of its two credentials - and its absence there would not fail anything loudly: the
    /// handshake would simply never ask, the validation callback the composition root installs would
    /// never run, and <c>CallerCertificateTrust</c> would be dead code in the shipped environment while
    /// every document describing it still read as live. So the assertion is EQUALITY with the profile
    /// flag, in both directions: Security must declare it and the other three must not.
    /// </para>
    /// <para>
    /// <c>SslProtocols</c> IS FORBIDDEN OUTRIGHT. A committed protocol floor is a hardening declaration
    /// with no observable behaviour on the platform this targets - the default already excludes
    /// everything below TLS 1.2 - and pinning one in the repository takes the choice away from the
    /// deployment that owns the risk while adding a second place for the transport story to disagree
    /// with itself.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(ListeningServiceKeys))]
    public void NoListenerDeclaresTransportSecuritySettings(string serviceKey)
    {
        ServiceProfile service = RequireService(serviceKey);
        JsonObject endpoints = RequireEndpoints(service, LoadSettings(service, BaseSettingsFileName));

        foreach ((string name, JsonNode? endpointNode) in endpoints)
        {
            JsonObject endpoint = RequireObject(
                endpointNode,
                service,
                BaseSettingsFileName,
                $"Kestrel:Endpoints:{name}");

            Assert.Equal(
                service.RequiresClientCertificateMode,
                endpoint.ContainsKey("ClientCertificateMode"));

            Assert.DoesNotContain("SslProtocols", (IEnumerable<string>)[.. endpoint.Select(member => member.Key)]);
        }
    }

    /// <summary>
    /// Asserts that no settings file requires secure metadata retrieval while naming an authority that
    /// cannot satisfy it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THIS IS A COHERENCE RULE, NOT A POSTURE PREFERENCE, and it is the one the option validators
    /// enforce at startup: <c>RequireHttpsMetadata</c> true against an <c>http</c> authority is a
    /// contradiction the bearer handler discovers only on its first metadata fetch - long after the host
    /// reported itself started - so the pair must agree inside the file that declares them. The reverse
    /// combination is legitimate and is deliberately allowed: an <c>https</c> authority with the switch
    /// off relaxes nothing that needs relaxing, because the setting only PERMITS a plaintext metadata
    /// address rather than creating one.
    /// </para>
    /// <para>
    /// A file that declares the switch and NO authority - Gateway's base file, which deliberately names
    /// no authority so that a deployment configuring nothing fails to start rather than inheriting one
    /// topology's address - passes trivially, which is correct: there is nothing yet for the switch to
    /// contradict.
    /// </para>
    /// </remarks>
    /// <param name="serviceKey">The service whose file is under test.</param>
    /// <param name="fileName">The settings file under test.</param>
    [Theory]
    [MemberData(nameof(AllSettingsFiles))]
    public void NoSettingsFileRequiresSecureMetadataAgainstAPlaintextAuthority(
        string serviceKey,
        string fileName)
    {
        ServiceProfile service = RequireService(serviceKey);
        List<(string Path, JsonNode? Value)> flattened = [.. Flatten(LoadSettings(service, fileName))];

        bool requiresSecureMetadata = flattened.Any(entry =>
            entry.Path.EndsWith("RequireHttpsMetadata", StringComparison.Ordinal)
            && entry.Value is JsonValue metadata
            && metadata.GetValue<bool>());

        if (!requiresSecureMetadata)
        {
            return;
        }

        foreach ((string path, JsonNode? value) in flattened)
        {
            if (!path.EndsWith("Authority", StringComparison.Ordinal)
                && !path.EndsWith("MetadataAddress", StringComparison.Ordinal))
            {
                continue;
            }

            if (value is not JsonValue declared || declared.GetValueKind() != JsonValueKind.String)
            {
                continue;
            }

            string address = declared.GetValue<string>();

            if (!Uri.TryCreate(address, UriKind.Absolute, out Uri? parsed))
            {
                continue;
            }

            Assert.False(
                string.Equals(parsed.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal),
                $"{service.ProjectName}/{fileName} requires secure metadata retrieval while '{path}' "
                    + "names a plaintext authority. The two contradict, and the option validator "
                    + "refuses the combination at startup. The address is not quoted here.");
        }
    }

    // ----------------------------------------------------------------------------------------------
    // 4. The Security option graph is exactly the authoritative one
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Asserts that the <c>Security</c> section carries precisely the authoritative leaf paths, with
    /// nothing missing and nothing orphaned.
    /// </summary>
    [Fact]
    public void SecuritySectionCarriesExactlyTheAuthoritativeLeafPaths()
    {
        ServiceProfile security = RequireService("security");
        JsonObject settings = LoadSettings(security, BaseSettingsFileName);

        JsonObject section = RequireObject(
            settings["Security"],
            security,
            BaseSettingsFileName,
            "Security");

        // Collection-element indices are normalised to `*` and the result de-duplicated, so the
        // assertion is about which members a roster entry may carry rather than about how many callers
        // this deployment happens to register. See the roster note on the expected list.
        string[] declared =
        [
            .. Flatten(section, "Security")
                .Select(static leaf => NormaliseCollectionIndices(leaf.Path))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];

        string[] expected = [.. AuthoritativeSecurityLeafPaths.Order(StringComparer.Ordinal)];

        Assert.Equal(expected, declared);
    }

    /// <summary>
    /// Asserts that the Security development overlay overrides only keys the base file already
    /// declares, so the overlay can never introduce a leaf the options type does not read.
    /// </summary>
    [Fact]
    public void SecurityDevelopmentOverlayIntroducesNoNewOptionLeaf()
    {
        ServiceProfile security = RequireService("security");

        JsonObject baseSection = RequireObject(
            LoadSettings(security, BaseSettingsFileName)["Security"],
            security,
            BaseSettingsFileName,
            "Security");

        JsonNode? overlayNode = LoadSettings(security, DevelopmentSettingsFileName)["Security"];
        JsonObject overlaySection = RequireObject(
            overlayNode,
            security,
            DevelopmentSettingsFileName,
            "Security");

        // INDICES NORMALISED, FOR THE REASON THE ROSTER NOTE GIVES. A configuration array merges by
        // INDEX, so an overlay may legitimately declare more elements of a collection than the base file
        // does - the Development overlay adds the end-to-end suite's identity to the issuance roster,
        // which is a local bring-up fact rather than a deployment one. What must never differ is the set
        // of MEMBER NAMES, because a member the options type does not read is dead configuration an
        // operator will believe has an effect. Comparing raw paths would conflate the two and would fail
        // on the legitimate case.
        HashSet<string> declaredInBase =
        [
            .. Flatten(baseSection, "Security")
                .Select(static leaf => NormaliseCollectionIndices(leaf.Path)),
        ];

        foreach ((string path, JsonNode? _) in Flatten(overlaySection, "Security"))
        {
            Assert.Contains(NormaliseCollectionIndices(path), declaredInBase);
        }
    }

    /// <summary>
    /// Asserts that the Persistence settings carry precisely the authoritative option leaves, bound
    /// from the configuration root, with nothing missing and nothing orphaned.
    /// </summary>
    /// <remarks>
    /// Both directions matter and they fail differently. A MISSING key binds its option property
    /// silently to a default - no exception, no log entry, just a value that looks deliberate. An
    /// ORPHANED key is dead configuration that an operator will reasonably believe has an effect. The
    /// original defect here was the whole graph shifted one level down under a service-named wrapper,
    /// which produced both failures at once for every key.
    /// </remarks>
    [Fact]
    public void PersistenceSettingsCarryExactlyTheAuthoritativeOptionLeafPaths()
    {
        ServiceProfile persistence = RequireService("persistence");
        JsonObject settings = LoadSettings(persistence, BaseSettingsFileName);

        // Element indices normalised and the result de-duplicated, for the reason recorded on the expected
        // list: a leaf-parity assertion holds a settings file to the MEMBERS the options type reads, which is
        // a contract, and not to the NUMBER of elements a deployment registers, which is a deployment
        // decision. Without normalisation the two would be one assertion, and registering a second
        // data-object definition would fail a test about member names.
        string[] declared =
        [
            .. Flatten(settings)
                .Select(static leaf => NormaliseCollectionIndices(leaf.Path))
                .Where(static path => !IsHostOwned(path))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
        ];

        string[] expected = [.. AuthoritativePersistenceLeafPaths.Order(StringComparer.Ordinal)];

        Assert.Equal(expected, declared);
    }

    /// <summary>
    /// Asserts that the Persistence development overlay overrides only keys the base file declares,
    /// so the overlay can introduce no option leaf of its own.
    /// </summary>
    [Fact]
    public void PersistenceDevelopmentOverlayIntroducesNoNewOptionLeaf()
    {
        ServiceProfile persistence = RequireService("persistence");

        HashSet<string> declaredInBase =
        [
            .. Flatten(LoadSettings(persistence, BaseSettingsFileName))
                .Select(static leaf => leaf.Path)
                .Where(static path => !IsHostOwned(path))
        ];

        foreach ((string path, JsonNode? _) in Flatten(LoadSettings(persistence, DevelopmentSettingsFileName)))
        {
            if (IsHostOwned(path))
            {
                continue;
            }

            Assert.Contains(path, declaredInBase);
        }
    }

    /// <summary>
    /// Asserts that no settings file configures a statement-bearing log category below
    /// <c>Warning</c>, in any environment.
    /// </summary>
    /// <param name="serviceKey">The service whose file is under test.</param>
    /// <param name="fileName">The settings file under test.</param>
    [Theory]
    [MemberData(nameof(AllSettingsFiles))]
    public void NoSettingsFileLowersAStatementBearingLogCategory(string serviceKey, string fileName)
    {
        ServiceProfile service = RequireService(serviceKey);
        List<string> failures = [];

        foreach ((string path, JsonNode? value) in Flatten(LoadSettings(service, fileName)))
        {
            int separator = path.LastIndexOf(':');
            string categoryName = separator < 0 ? path : path[(separator + 1)..];

            if (!StatementBearingLogCategories.Contains(categoryName, StringComparer.Ordinal))
            {
                continue;
            }

            string level = value is JsonValue declared ? declared.GetValue<string>() : string.Empty;

            if (!PermittedStatementLogLevels.Contains(level, StringComparer.Ordinal))
            {
                failures.Add(
                    $"{service.ProjectName}/{fileName} sets '{path}' to '{level}'. A framework-emitted "
                        + "command log is written by the data provider rather than by application code, so "
                        + "the application's SQL redactor never sees it, and the generated statement it "
                        + "carries includes interpolated literal values. Permitted levels are "
                        + $"{string.Join(", ", PermittedStatementLogLevels)}.");
            }
        }

        Assert.Empty(failures);
    }

    /// <summary>
    /// Asserts that no service resolves a URL-bearing log category below <c>Warning</c>, in either the
    /// deployed configuration or the developer one.
    /// </summary>
    /// <param name="serviceKey">The service whose configuration is under test.</param>
    /// <remarks>
    /// <para>
    /// <b>THE EFFECTIVE LEVEL IS RESOLVED, NOT THE DECLARED ONE, AND THAT IS THE WHOLE POINT.</b> The
    /// defect this row exists for was not a category set too low - it was a category set NOWHERE, whose
    /// level was therefore inherited from a PARENT the Development overlay raised to
    /// <c>Information</c>. A check that only inspected declared keys would have read every settings file
    /// in the estate as correct while three services logged bearer tokens. So this walks the same
    /// longest-prefix rule the logging factory itself applies, over the merged document set the host
    /// actually loads.
    /// </para>
    /// <para>
    /// <b>BOTH ENVIRONMENTS ARE RESOLVED, IN LOAD ORDER.</b> The deployed configuration is the base
    /// document alone; the developer configuration is the base document with the Development overlay
    /// applied over it, key by key, exactly as the configuration builder composes them. Asserting only
    /// the merged form would let a base document that leaks be masked by an overlay that does not, and
    /// asserting only the base would miss the defect that actually occurred.
    /// </para>
    /// <para>
    /// A service whose configuration names no matching category at all is NOT a pass by omission: the
    /// <c>Default</c> rule is what the factory falls back to, so it is resolved here too - which is what
    /// makes a bare <c>"Default": "Information"</c> with no <c>Microsoft.AspNetCore</c> entry fail rather
    /// than slip through.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(AllServiceKeys))]
    public void NoServiceResolvesAUrlBearingLogCategoryBelowWarning(string serviceKey)
    {
        ServiceProfile service = RequireService(serviceKey);

        Dictionary<string, string> deployed = ReadLogLevelRules(service, BaseSettingsFileName);

        Dictionary<string, string> developer = new(deployed, StringComparer.OrdinalIgnoreCase);

        foreach ((string category, string level) in ReadLogLevelRules(service, DevelopmentSettingsFileName))
        {
            developer[category] = level;
        }

        List<string> failures = [];

        foreach (string category in UrlBearingLogCategories)
        {
            Check("the deployed configuration", BaseSettingsFileName, deployed, category);
            Check(
                "a developer run",
                $"{BaseSettingsFileName} + {DevelopmentSettingsFileName}",
                developer,
                category);
        }

        Assert.Empty(failures);

        void Check(
            string environment,
            string documents,
            Dictionary<string, string> rules,
            string category)
        {
            (string? matched, string? level) = ResolveLogLevel(rules, category);

            if (level is not null
                && PermittedUrlBearingLogLevels.Contains(level, StringComparer.OrdinalIgnoreCase))
            {
                return;
            }

            failures.Add(
                $"{service.ProjectName}: in {environment} ({documents}), the log category '{category}' "
                    + $"resolves to '{level ?? "<no rule and no Default>"}' through the rule "
                    + $"'{matched ?? "<none>"}'. That category's records render the FULL request URL "
                    + "including its query string, so a credential a caller placed in a query parameter "
                    + "is written to the operator log in cleartext on its way to being refused. Permitted "
                    + $"levels are {string.Join(", ", PermittedUrlBearingLogLevels)} - declare the "
                    + "category itself rather than lowering its parent, so the rest of the parent's "
                    + "diagnostics stay legible.");
        }
    }

    /// <summary>
    /// Reads one settings file's <c>Logging:LogLevel</c> rules as a category-to-level map.
    /// </summary>
    /// <param name="service">The service whose file is being read.</param>
    /// <param name="fileName">The settings file.</param>
    /// <returns>The rules, keyed case-insensitively as the logging factory keys them.</returns>
    private static Dictionary<string, string> ReadLogLevelRules(ServiceProfile service, string fileName)
    {
        Dictionary<string, string> rules = new(StringComparer.OrdinalIgnoreCase);

        if (LoadSettings(service, fileName)["Logging"] is not JsonObject logging
            || logging["LogLevel"] is not JsonObject levels)
        {
            return rules;
        }

        foreach ((string category, JsonNode? level) in levels)
        {
            if (level is JsonValue declared)
            {
                rules[category] = declared.GetValue<string>();
            }
        }

        return rules;
    }

    /// <summary>
    /// Resolves one category's effective level under a rule set, the way the logging factory does.
    /// </summary>
    /// <param name="rules">The category-to-level rules.</param>
    /// <param name="category">The category whose level is wanted.</param>
    /// <returns>The rule that matched and the level it carries, or two nulls when nothing matched.</returns>
    /// <remarks>
    /// LONGEST PREFIX WINS, AND <c>Default</c> IS THE FALLBACK RATHER THAN A PREFIX. That is the
    /// framework's own selection rule: a rule for <c>Microsoft.AspNetCore.Hosting.Diagnostics</c> beats
    /// one for <c>Microsoft.AspNetCore</c>, which in turn beats <c>Default</c>. Comparison is
    /// case-insensitive because the factory's own lookup is.
    /// </remarks>
    private static (string? Rule, string? Level) ResolveLogLevel(
        Dictionary<string, string> rules,
        string category)
    {
        string? matched = null;

        foreach (string rule in rules.Keys)
        {
            if (string.Equals(rule, "Default", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!category.StartsWith(rule, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (matched is null || rule.Length > matched.Length)
            {
                matched = rule;
            }
        }

        if (matched is not null)
        {
            return (matched, rules[matched]);
        }

        return rules.TryGetValue("Default", out string? fallback)
            ? ("Default", fallback)
            : (null, null);
    }

    // ----------------------------------------------------------------------------------------------
    // 5. Every protected inbound audience is issuable
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Asserts that every audience a service validates on its inbound boundary is an audience Security
    /// is configured to issue.
    /// </summary>
    /// <remarks>
    /// This is the assertion that would have caught an API unauthorised by construction: Security's own
    /// cryptographic surface validated an audience that Security's issuance list omitted, so no caller
    /// could obtain a token that surface would accept. It fails closed, as a 401 that reads like a
    /// caller error, which is why a static check is worth more here than a runtime one.
    /// </remarks>
    [Fact]
    public void EveryProtectedInboundAudienceIsIssuableBySecurity()
    {
        ServiceProfile security = RequireService("security");
        JsonObject securitySettings = LoadSettings(security, BaseSettingsFileName);

        JsonObject section = RequireObject(
            securitySettings["Security"],
            security,
            BaseSettingsFileName,
            "Security");

        HashSet<string> issuable = [.. ReadAudiences(section["Audiences"], security, "Security:Audiences")];
        Assert.NotEmpty(issuable);

        List<string> failures = [];

        foreach (InboundAudience inbound in InboundAudiences)
        {
            ServiceProfile service = RequireService(inbound.ServiceKey);
            JsonObject settings = LoadSettings(service, BaseSettingsFileName);
            JsonNode? node = FindNode(settings, inbound.KeyPath);

            IReadOnlyList<string> audiences = ReadAudiences(node, service, inbound.KeyPath);

            if (audiences.Count == 0)
            {
                failures.Add(
                    $"{service.ProjectName} declares no inbound audience at '{inbound.KeyPath}'. A bearer "
                        + "configuration with no audience either rejects every token or forces audience "
                        + "validation off, and neither is acceptable on a boundary C-G requires to be "
                        + "authenticated.");
                continue;
            }

            failures.AddRange(
                audiences
                    .Where(audience => !issuable.Contains(audience))
                    .Select(audience =>
                        $"{service.ProjectName} validates inbound audience '{audience}' at "
                            + $"'{inbound.KeyPath}', but 'Security:Audiences' does not list it, so the sole "
                            + "issuer cannot mint a token that service will accept."));
        }

        Assert.Empty(failures);
    }

    /// <summary>
    /// Asserts that Security lists no audience that no service validates, so the issuance list carries
    /// no audience nothing consumes.
    /// </summary>
    [Fact]
    public void SecurityIssuesNoAudienceThatNoServiceValidates()
    {
        ServiceProfile security = RequireService("security");

        JsonObject section = RequireObject(
            LoadSettings(security, BaseSettingsFileName)["Security"],
            security,
            BaseSettingsFileName,
            "Security");

        HashSet<string> validated = [];

        foreach (InboundAudience inbound in InboundAudiences)
        {
            ServiceProfile service = RequireService(inbound.ServiceKey);
            JsonNode? node = FindNode(LoadSettings(service, BaseSettingsFileName), inbound.KeyPath);
            validated.UnionWith(ReadAudiences(node, service, inbound.KeyPath));
        }

        string[] unconsumed =
        [
            .. ReadAudiences(section["Audiences"], security, "Security:Audiences")
                .Where(audience => !validated.Contains(audience))
        ];

        Assert.Empty(unconsumed);
    }

    // ----------------------------------------------------------------------------------------------
    // 6. No signing or credential material in any settings file
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Asserts that no settings file declares a leaf whose name is a credential, at any depth.
    /// </summary>
    /// <param name="serviceKey">The service whose file is under test.</param>
    /// <param name="fileName">The settings file under test.</param>
    [Theory]
    [MemberData(nameof(AllSettingsFiles))]
    public void NoSettingsFileDeclaresCredentialMaterial(string serviceKey, string fileName)
    {
        ServiceProfile service = RequireService(serviceKey);
        List<string> failures = [];

        foreach ((string path, JsonNode? _) in Flatten(LoadSettings(service, fileName)))
        {
            int separator = path.LastIndexOf(':');
            string leafName = separator < 0 ? path : path[(separator + 1)..];

            if (ForbiddenCredentialLeafNames.Contains(leafName, StringComparer.OrdinalIgnoreCase))
            {
                failures.Add(
                    $"{service.ProjectName}/{fileName} declares '{path}'. Credential material reaches a "
                        + "process only through environment configuration from the orchestration secret "
                        + "layer, never through a settings file (C-F). The value is deliberately not "
                        + "reproduced in this message.");
            }
        }

        Assert.Empty(failures);
    }

    /// <summary>
    /// No listener in any settings file binds a loopback-only host, in any environment.
    /// </summary>
    /// <param name="serviceKey">The service under test.</param>
    /// <param name="fileName">The settings file under test.</param>
    /// <remarks>
    /// <para>
    /// 🔴 THIS GUARD EXISTS BECAUSE THE DEFECT HAPPENED, AND NOTHING REPORTED IT. Gateway's
    /// <c>appsettings.Development.json</c> declared <c>https://localhost:5105</c> - narrowed from the
    /// base file's <c>https://+:5105</c> on the reasoning that <c>localhost</c> "restricts a developer's
    /// run to the loopback interface". True for a host <c>dotnet run</c>, and wrong here, because
    /// <c>orchestration/.env.example</c> sets <c>ASPNETCORE_ENVIRONMENT=Development</c> and a Compose
    /// environment value wins over the image <c>ENV</c> - so THE DOCUMENTED CONTAINER BRING-UP SELECTS
    /// THIS OVERLAY.
    /// </para>
    /// <para>
    /// Inside a container <c>localhost</c> binds the container's own loopback only, while Docker's
    /// published-port forwarding arrives on its <c>eth0</c> address. Measured: <c>curl</c> and
    /// <c>openssl</c> from the host both failed the TLS handshake on 5105 while 5101, 5102 and 5104
    /// answered 200. AND THE SERVICE STILL REPORTED HEALTHY, because its own HEALTHCHECK connects to
    /// <c>127.0.0.1</c> from inside the container - so Compose showed four healthy services,
    /// <c>depends_on</c> opened, and the unreachable surface was the INGRESS every end-to-end workflow
    /// enters through.
    /// </para>
    /// <para>
    /// A runtime probe would have caught it and no static test did, which is exactly why this one is
    /// static: it runs in a clean checkout with nothing up, and it fails on the authored text rather
    /// than on a bring-up somebody has to remember to perform.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(AllSettingsFiles))]
    public void NoListenerBindsALoopbackOnlyHost(string serviceKey, string fileName)
    {
        ServiceProfile service = RequireService(serviceKey);

        if (FindNode(LoadSettings(service, fileName), "Kestrel:Endpoints") is not JsonObject endpoints)
        {
            // No override in this file is a legitimate state - the base file's listener stands.
            return;
        }

        foreach ((string name, JsonNode? endpoint) in endpoints)
        {
            string? url = (endpoint as JsonObject)?["Url"]?.GetValue<string>();

            if (url is null)
            {
                continue;
            }

            // ORDINAL AND CASE-INSENSITIVE ON THE HOST ONLY. The three spellings below are the ones
            // Kestrel treats as loopback-only; `+` and `*` bind every interface and are what a
            // container needs. A named host that resolves to a loopback address is not detectable from
            // text and is out of this assertion's reach - stated so the bound is not overclaimed.
            foreach (string loopback in (string[])["//localhost:", "//127.0.0.1:", "//[::1]:"])
            {
                Assert.False(
                    url.Contains(loopback, StringComparison.OrdinalIgnoreCase),
                    $"{service.ProjectName}/{fileName} binds endpoint '{name}' to a loopback-only host "
                        + $"('{url}'). Inside a container that binds the container's own loopback, while "
                        + "Docker's published-port forwarding arrives on its eth0 address - so the "
                        + "service is unreachable from the host WHILE ITS OWN HEALTHCHECK, which "
                        + "connects to 127.0.0.1 inside the container, still reports healthy. Use '+' "
                        + "so every interface is bound; a host run on '+' still serves loopback.");
            }
        }
    }

    // ==============================================================================================
    //  7. THE EXTERNAL INGRESS GRANT - THE ONE GRANT AN OPERATOR PROVISIONS BY HAND
    //
    //  🔴 THE DEFECT THESE THREE ASSERTIONS CLOSE. Gateway is the ingress: nothing inside the system
    //  calls it, so Security's shipped matrix pre-grants NO caller the gateway audience and out of the
    //  box every authenticated gateway route answers 401 to every credential the issuer will mint. That
    //  is fail-closed on purpose. Opening it is therefore a deployment act, and the guidance for it used
    //  to say the grant's scope list "may be anything the deployment finds useful for its own auditing,
    //  INCLUDING a single placeholder, because the gateway itself requires no scope".
    //
    //  IT DOES REQUIRE SCOPES - three of them - so following that guidance produced a token that MINTED
    //  SUCCESSFULLY and was then refused 403 on every route it could reach. Issuance intersects the
    //  requested scopes with the grant and reports the overlap, and a NARROWING IS A SUCCESS, so nothing
    //  refuses at provisioning time: the whole cost lands at the boundary, three layers from the cause.
    //
    //  THE GRANT IS STATED IN EXACTLY ONE ARTIFACT, AND THAT IS DELIBERATE RATHER THAN A GAP. Stating it in
    //  three - Security's Development overlay, the orchestration template and the Compose manifest - is
    //  the tempting belt-and-braces, and the manifest's injection is excluded for three reasons its own
    //  comment records: in Development it duplicates the overlay, which already ships the grant AND the
    //  credential entry the caller needs to authenticate for it; in Production it grants a permission no
    //  credential can exercise; and it makes the literal index 3 load-bearing across two files, so a
    //  fourth row legitimately added to the settings file would be merged INTO the injected one rather
    //  than appended, because the configuration provider merges an array BY INDEX. What follows asserts
    //  the single statement is correct, that neither of the other two artifacts restates it, and that both
    //  say where it lives - so the guidance cannot go stale silently.
    // ==============================================================================================

    /// <summary>The one external caller the repository provisions a grant for.</summary>
    private const string IngressGrantCaller = "pfw-e2e-suite";

    /// <summary>The audience that grant addresses: Gateway, the sole ingress.</summary>
    private const string IngressGrantAudience = "powerframework-gateway";

    /// <summary>
    /// The published external-ingress grant is stated in Security's Development overlay and in NO other
    /// roster artifact, and both of the others say where it is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ONE ARTIFACT STATES THE GRANT, AND AN OPERATOR IS TOLD WHICH. The overlay is what a local
    /// <c>dotnet run</c> reads and what the Compose bring-up selects, and it is also where the caller's
    /// CREDENTIAL is registered - so the grant and the thing that authenticates for it live in one file
    /// and cannot drift apart. A second statement of it in the template or the manifest would be a
    /// grant with two owners, which is the hardest kind of configuration defect to attribute.
    /// </para>
    /// <para>
    /// THE INDEX IS ASSERTED TOO, NOT ONLY THE VALUES, and it is also why the second statement had to
    /// go. The configuration provider merges an array BY INDEX rather than by appending: Security's base
    /// file occupies <c>CallerAuthorizations</c> 0, 1 and 2, so this grant is element 3. An environment
    /// injection at that same index made the literal 3 load-bearing across two files - a fourth row
    /// legitimately added to the settings file would have been merged INTO the injected one, producing a
    /// HYBRID row with the row count unchanged and nothing refused at startup.
    /// </para>
    /// <para>
    /// THE NEGATIVE HALF READS DECLARATIONS AND NOT MENTIONS. Both the template and the manifest discuss
    /// the excluded keys at length, which is the documentation this test wants to keep; what neither may
    /// carry is a LIVE declaration of them. So the template is read line by line for an uncommented
    /// assignment and the manifest for an uncommented mapping key.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheExternalIngressGrantIsStatedOnceAndTheOtherRosterArtifactsSayWhereItIs()
    {
        const int GrantIndex = 3;

        // 1. SECURITY'S DEVELOPMENT OVERLAY - the settings shape.
        JsonArray overlay = LoadSettings(RequireService("security"), DevelopmentSettingsFileName)
            ["Security"]?["CallerAuthorizations"] as JsonArray
            ?? throw FailException.ForFailure(
                "Security's Development overlay declares no 'Security:CallerAuthorizations' array, so the "
                    + "external ingress grant this repository documents is not published anywhere a local "
                    + "run would read it.");

        Assert.True(
            overlay.Count > GrantIndex,
            $"Security's Development overlay declares {overlay.Count} caller authorizations, so index "
                + $"{GrantIndex} - the index every artifact agrees the ingress grant occupies - is absent.");

        JsonNode grant = overlay[GrantIndex]
            ?? throw FailException.ForFailure(
                $"Security's Development overlay has a null element at CallerAuthorizations[{GrantIndex}].");

        Assert.Equal(IngressGrantCaller, grant["Caller"]?.GetValue<string>());
        Assert.Equal(IngressGrantAudience, grant["Audience"]?.GetValue<string>());

        string[] overlayScopes =
        [
            .. (grant["Scopes"] as JsonArray ?? []).Select(static scope => scope!.GetValue<string>()),
        ];

        Assert.Equal(GatewayContractTests.PublishedRequiredScopes.Length - 1, overlayScopes.Length);

        Assert.Equal(
            [.. GatewayContractTests.PublishedRequiredScopes
                .Where(static scope => !string.Equals(
                    scope,
                    GatewayContractTests.NoScopeRequiredSentinel,
                    StringComparison.Ordinal))],
            overlayScopes);

        // 2. THE ORCHESTRATION TEMPLATE AND 3. THE COMPOSE MANIFEST - neither DECLARES the keys, and
        //    both explain the grant instead. A commented mention is the documentation; an uncommented
        //    declaration is the second owner this test exists to forbid.
        string templatePath = Path.Combine(RequireOrchestrationDirectory(), ".env.example");
        string manifestPath = Path.Combine(RequireOrchestrationDirectory(), "docker-compose.yml");
        string template = File.ReadAllText(templatePath);
        string manifest = File.ReadAllText(manifestPath);

        string prefix = $"Security__CallerAuthorizations__{GrantIndex}__";

        foreach ((string path, string text) in ((string, string)[])
            [
                (".env.example", template),
                ("docker-compose.yml", manifest),
            ])
        {
            string[] declarations =
            [
                .. text.Split('\n')
                    .Select(static line => line.TrimStart())
                    .Where(line => !line.StartsWith('#') && line.Contains(prefix, StringComparison.Ordinal)),
            ];

            Assert.True(
                declarations.Length == 0,
                $"orchestration/{path} DECLARES the external ingress grant:{Environment.NewLine}"
                    + string.Join(Environment.NewLine, declarations)
                    + $"{Environment.NewLine}The grant is stated once, in Security's "
                    + "appsettings.Development.json, beside the credential entry the caller "
                    + "authenticates with. A second declaration at the same array index makes the literal "
                    + $"{GrantIndex} load-bearing across two files: the configuration provider merges an "
                    + "array by index, so a fourth row added to the settings file would be merged into "
                    + "this one instead of appended.");

            // AND THE ARTIFACT SAYS WHERE THE GRANT IS, so its absence reads as a decision rather than
            // as an oversight to be corrected by re-adding it.
            Assert.Contains(prefix, text, StringComparison.Ordinal);
            Assert.Contains("Development", text, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Every scope the ingress grant carries is a scope Gateway's routes actually enforce, and the grant
    /// carries all of them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE ENFORCED SET IS READ OUT OF THE ROUTES, NOT RESTATED HERE. Each Gateway endpoint file declares
    /// its own <c>RequiredScope</c> constant and the composition root registers a policy under that exact
    /// name, so those three literals are the authority. Restating them in this test would make a rename
    /// invisible - the grant would keep matching the copy while the routes moved on.
    /// </para>
    /// <para>
    /// AND THE CONTRACT IS HELD TO THE SAME SET. <c>gateway.v1.yaml</c> now publishes the requirement per
    /// operation as <c>x-required-scope</c>; asserting the document, the grant and the code together is
    /// what makes the published contract usable for provisioning rather than merely accurate.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryScopeTheIngressGrantCarriesIsAScopeGatewayEnforces()
    {
        string[] enforced =
        [
            .. new[]
            {
                ("PingEndpoints.cs", "ping"),
                ("CapabilityEndpoints.cs", "capabilities"),
                ("DataServicesProxyEndpoints.cs", "datawindow"),
            }.Select(entry => ReadRequiredScopeConstant(entry.Item1)),
        ];

        JsonArray overlay = (JsonArray)LoadSettings(
            RequireService("security"),
            DevelopmentSettingsFileName)["Security"]!["CallerAuthorizations"]!;

        string[] granted =
        [
            .. (overlay[3]!["Scopes"] as JsonArray ?? []).Select(static scope => scope!.GetValue<string>()),
        ];

        Assert.Equal(
            enforced.Order(StringComparer.Ordinal),
            granted.Order(StringComparer.Ordinal));

        // AND THE PUBLISHED DOCUMENT'S CLOSED SET IS THE SAME THREE, plus its no-requirement sentinel.
        Assert.Equal(
            GatewayContractTests.PublishedRequiredScopes
                .Where(static scope =>
                    !string.Equals(
                        scope,
                        GatewayContractTests.NoScopeRequiredSentinel,
                        StringComparison.Ordinal))
                .Order(StringComparer.Ordinal),
            enforced.Order(StringComparer.Ordinal));

        // The sentinel must not collide with a real scope, or "no requirement" would read as a grant.
        Assert.DoesNotContain(
            GatewayContractTests.NoScopeRequiredSentinel,
            enforced,
            StringComparer.Ordinal);
    }

    /// <summary>
    /// No settings file tells an operator that an arbitrary or placeholder scope will do for the ingress
    /// grant, and the orchestration template names the three real scopes instead.
    /// </summary>
    /// <remarks>
    /// 🔴 A PROSE ASSERTION OVER A COMMENT, WHICH NEEDS DEFENDING AND EARNS IT. The retired sentence was
    /// not decoration: it was the instruction an operator followed, and following it produced a token
    /// that minted and was then refused everywhere. A comment that misinstructs is a defect with the same
    /// cost as a wrong value and none of the visibility, so the retired wording is pinned as absent and
    /// the corrective wording as present. The check is deliberately narrow - it looks for the phrase that
    /// did the damage, not for the word "placeholder", which appears legitimately elsewhere.
    /// </remarks>
    [Fact]
    public void NoSettingsFileOffersAPlaceholderScopeAsSufficientForTheIngress()
    {
        string[] retiredClaims =
        [
            "INCLUDING a single placeholder",
            "the gateway itself requires no scope",
        ];

        List<string> failures = [];

        foreach (ServiceProfile service in Services)
        {
            foreach (string fileName in (string[])[BaseSettingsFileName, DevelopmentSettingsFileName])
            {
                string path = Path.Combine(
                    RequireServicesDirectory(),
                    service.DirectoryName,
                    service.ProjectName,
                    fileName);

                if (!File.Exists(path))
                {
                    continue;
                }

                string text = File.ReadAllText(path);

                failures.AddRange(
                    retiredClaims
                        .Where(claim => text.Contains(claim, StringComparison.Ordinal))
                        .Select(claim =>
                            $"{service.ProjectName}/{fileName} still says \"{claim}\". Gateway enforces "
                                + "ping, capabilities and datawindow per route, so a grant carrying "
                                + "anything else authenticates and is then refused 403 on every route it "
                                + "reaches."));
            }
        }

        Assert.Empty(failures);

        // AND THE REPLACEMENT IS PRESENT, so the guidance was corrected rather than merely deleted.
        //
        // IT LIVES IN THE OPERATOR-FACING TEMPLATE RATHER THAN IN A SETTINGS FILE, and that is a
        // constraint rather than a preference: all four of Security's and Persistence's settings files
        // are STRICT JSON with no comment lines, so prose cannot live in them at all, and
        // orchestration/.env.example is the artifact an operator actually reads while provisioning a
        // caller. The retired wording is pinned as absent from every settings file above; the corrective
        // wording is pinned as present here.
        string guidance = File.ReadAllText(
            Path.Combine(RequireOrchestrationDirectory(), ".env.example"));

        Assert.Contains(
            "A PLACEHOLDER WILL NOT DO",
            guidance,
            StringComparison.Ordinal);

        foreach (string scope in GatewayContractTests.PublishedRequiredScopes)
        {
            if (string.Equals(
                scope,
                GatewayContractTests.NoScopeRequiredSentinel,
                StringComparison.Ordinal))
            {
                continue;
            }

            Assert.Contains(scope, guidance, StringComparison.Ordinal);
        }

        Assert.Contains(IngressGrantCaller, guidance, StringComparison.Ordinal);
        Assert.Contains(IngressGrantAudience, guidance, StringComparison.Ordinal);
    }

    // ----------------------------------------------------------------------------------------------
    // Helpers
    // ----------------------------------------------------------------------------------------------

    /// <summary>Builds the caller-address theory data from <see cref="CallerAddresses"/>.</summary>
    /// <returns>One row per caller address.</returns>
    private static TheoryData<string, string, string, string, bool> BuildCallerAddressData()
    {
        TheoryData<string, string, string, string, bool> data = [];

        foreach (CallerAddress address in CallerAddresses)
        {
            data.Add(
                address.CallerKey,
                address.FileName,
                address.KeyPath,
                address.TargetKey,
                address.Endpoint == TargetEndpoint.Grpc);
        }

        return data;
    }

    /// <summary>Builds the settings-file theory data: both files for all four services.</summary>
    /// <returns>One row per settings file.</returns>
    private static TheoryData<string, string> BuildSettingsFileData()
    {
        TheoryData<string, string> data = [];

        foreach (ServiceProfile service in Services)
        {
            data.Add(service.Key, BaseSettingsFileName);
            data.Add(service.Key, DevelopmentSettingsFileName);
        }

        return data;
    }

    /// <summary>Reports whether a leaf path belongs to a section the ASP.NET Core host binds itself.</summary>
    /// <param name="path">A colon-separated configuration leaf path.</param>
    /// <returns><see langword="true"/> when the path's root section is host-owned.</returns>
    private static bool IsHostOwned(string path)
    {
        int separator = path.IndexOf(':');
        string root = separator < 0 ? path : path[..separator];

        return HostOwnedSectionNames.Contains(root, StringComparer.Ordinal);
    }

    /// <summary>Resolves a service profile by key.</summary>
    /// <param name="serviceKey">The key to resolve.</param>
    /// <returns>The matching profile.</returns>
    private static ServiceProfile RequireService(string serviceKey) =>
        Array.Find(Services, service => string.Equals(service.Key, serviceKey, StringComparison.Ordinal))
            ?? throw FailException.ForFailure($"No service profile is declared for key '{serviceKey}'.");

    /// <summary>Reads and parses one settings file of one service.</summary>
    /// <param name="service">The owning service.</param>
    /// <param name="fileName">The settings file name.</param>
    /// <returns>The parsed root object.</returns>
    private static JsonObject LoadSettings(ServiceProfile service, string fileName)
    {
        string path = Path.Combine(
            RequireServicesDirectory(),
            service.DirectoryName,
            service.ProjectName,
            fileName);

        if (!File.Exists(path))
        {
            throw FailException.ForFailure(
                $"'{service.DirectoryName}/{service.ProjectName}/{fileName}' does not exist. Both settings "
                    + "files are authored for every service, so its absence is a finding rather than a "
                    + "reason to skip a check.");
        }

        JsonNode? parsed;

        try
        {
            parsed = JsonNode.Parse(File.ReadAllText(path), documentOptions: SettingsDocumentOptions);
        }
        catch (JsonException failure)
        {
            throw FailException.ForFailure(
                $"'{service.DirectoryName}/{service.ProjectName}/{fileName}' is not parseable as the host "
                    + $"reads it (comments skipped, trailing commas allowed): {failure.Message}");
        }

        return parsed as JsonObject
            ?? throw FailException.ForFailure(
                $"'{service.DirectoryName}/{service.ProjectName}/{fileName}' does not have a JSON object at "
                    + "its root, so no configuration key could be bound from it.");
    }

    /// <summary>Locates the repository's <c>orchestration</c> directory.</summary>
    /// <returns>The absolute path of the orchestration directory.</returns>
    /// <remarks>
    /// Derived from the services directory's parent rather than walked for separately, so both locators
    /// can never disagree about which checkout they are reading.
    /// </remarks>
    private static string RequireOrchestrationDirectory()
    {
        string root = Directory.GetParent(RequireServicesDirectory())?.FullName
            ?? throw FailException.ForFailure(
                "The services directory has no parent, so the repository root cannot be derived from it.");

        string orchestration = Path.Combine(root, "orchestration");

        if (!Directory.Exists(orchestration))
        {
            throw FailException.ForFailure(
                $"'{orchestration}' does not exist. The orchestration directory carries the template and "
                    + "the manifest this assertion compares, so its absence is a finding rather than a "
                    + "reason to skip a check.");
        }

        return orchestration;
    }

    /// <summary>
    /// Reads the single <c>RequiredScope</c> constant declared by one Gateway endpoint source file.
    /// </summary>
    /// <param name="endpointFileName">The endpoint file name, for example <c>PingEndpoints.cs</c>.</param>
    /// <returns>The scope literal the route enforces.</returns>
    /// <remarks>
    /// SOURCE TEXT RATHER THAN A PROJECT REFERENCE, for the reason the file header gives: this project
    /// deliberately references no service, so that its guards keep running while a service's own project
    /// is unbuildable. Exactly one declaration must be found - a second would mean the file declares two
    /// requirements and this test would be silently reading whichever came first.
    /// </remarks>
    private static string ReadRequiredScopeConstant(string endpointFileName)
    {
        string path = Path.Combine(
            RequireServicesDirectory(),
            "gateway-service",
            "PowerFramework.Gateway",
            "Endpoints",
            endpointFileName);

        if (!File.Exists(path))
        {
            throw FailException.ForFailure(
                $"'{path}' does not exist, so the scope Gateway enforces on that surface cannot be read "
                    + "from the route that declares it.");
        }

        MatchCollection matches = RequiredScopeDeclarationPattern.Matches(File.ReadAllText(path));

        if (matches.Count != 1)
        {
            throw FailException.ForFailure(
                $"'{endpointFileName}' declares {matches.Count} 'RequiredScope' constants; exactly one is "
                    + "expected, because a route has one scope requirement.");
        }

        return matches[0].Groups["scope"].Value;
    }

    /// <summary>Matches a <c>RequiredScope</c> constant declaration in an endpoint source file.</summary>
    private static readonly Regex RequiredScopeDeclarationPattern = new(
        """(?:internal|public|private)\s+const\s+string\s+RequiredScope\s*=\s*"(?<scope>[^"]+)"\s*;""",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        TimeSpan.FromSeconds(5));

    /// <summary>Locates the repository's <c>services</c> directory by walking up from the test binary.</summary>
    /// <returns>The absolute path of the services directory.</returns>
    /// <remarks>
    /// Both repository markers are required together with the directory itself, so the walk cannot latch
    /// onto a same-named directory elsewhere on the machine. The probe is read-only.
    /// </remarks>
    private static string RequireServicesDirectory()
    {
        // STARTS AT THE EMBEDDED REPOSITORY ROOT WHEN THE BUILD SUPPLIED ONE, so this locator works when
        // the test output sits outside the checkout - `dotnet test --artifacts-path` - where no ancestor
        // of the output directory carries the marker below. The walk itself is unchanged and still
        // verifies that marker, so an absent or stale value simply falls back to the previous start.
        // See TestRepositoryRoot.
        DirectoryInfo? candidate = new(TestRepositoryRoot.SearchStart);

        while (candidate is not null)
        {
            string services = Path.Combine(candidate.FullName, ServicesDirectoryName);

            if (Directory.Exists(services)
                && File.Exists(Path.Combine(candidate.FullName, SolutionMarkerFileName))
                && File.Exists(Path.Combine(candidate.FullName, PackageVersionMarkerFileName)))
            {
                return services;
            }

            // Parent is null at the filesystem root, which is what terminates the walk.
            candidate = candidate.Parent;
        }

        throw FailException.ForFailure(
            $"No directory from '{AppContext.BaseDirectory}' up to the filesystem root holds "
                + $"'{ServicesDirectoryName}' together with both repository markers "
                + $"'{SolutionMarkerFileName}' and '{PackageVersionMarkerFileName}'.");
    }

    /// <summary>Reads the <c>Kestrel:Endpoints</c> object of a listening service.</summary>
    /// <param name="service">The owning service.</param>
    /// <param name="settings">The parsed settings root.</param>
    /// <returns>The endpoints object.</returns>
    private static JsonObject RequireEndpoints(ServiceProfile service, JsonObject settings) =>
        FindNode(settings, "Kestrel:Endpoints") as JsonObject
            ?? throw FailException.ForFailure(
                $"{service.ProjectName} declares no 'Kestrel:Endpoints' object. Its listener address and "
                    + "protocol set are what the port map fixes, so they are configured rather than left "
                    + "to a host default.");

    /// <summary>Narrows a node to an object, failing with the key path when it is not one.</summary>
    /// <param name="node">The node to narrow.</param>
    /// <param name="service">The owning service, for the diagnostic.</param>
    /// <param name="fileName">The owning file, for the diagnostic.</param>
    /// <param name="keyPath">The key path, for the diagnostic.</param>
    /// <returns>The node as an object.</returns>
    private static JsonObject RequireObject(
        JsonNode? node,
        ServiceProfile service,
        string fileName,
        string keyPath) =>
        node as JsonObject
            ?? throw FailException.ForFailure(
                $"{service.ProjectName}/{fileName} has no object at '{keyPath}'.");

    /// <summary>Reads a required non-blank string property of an object.</summary>
    /// <param name="owner">The object to read from.</param>
    /// <param name="propertyName">The property name.</param>
    /// <param name="service">The owning service, for the diagnostic.</param>
    /// <param name="keyPath">The key path, for the diagnostic.</param>
    /// <returns>The property value.</returns>
    private static string RequireString(
        JsonObject owner,
        string propertyName,
        ServiceProfile service,
        string keyPath)
    {
        string? value = owner[propertyName] is JsonValue candidate ? candidate.GetValue<string>() : null;

        return string.IsNullOrWhiteSpace(value)
            ? throw FailException.ForFailure(
                $"{service.ProjectName} declares no non-blank '{keyPath}'.")
            : value;
    }

    /// <summary>Reads a required non-blank string at a colon-separated configuration key path.</summary>
    /// <param name="settings">The parsed settings root.</param>
    /// <param name="keyPath">The colon-separated key path.</param>
    /// <param name="service">The owning service, for the diagnostic.</param>
    /// <param name="fileName">The owning file, for the diagnostic.</param>
    /// <returns>The value at that path.</returns>
    private static string RequireStringAtPath(
        JsonObject settings,
        string keyPath,
        ServiceProfile service,
        string fileName)
    {
        string? value = FindNode(settings, keyPath) is JsonValue candidate
            ? candidate.GetValue<string>()
            : null;

        return string.IsNullOrWhiteSpace(value)
            ? throw FailException.ForFailure(
                $"{service.ProjectName}/{fileName} declares no non-blank value at '{keyPath}'. An address "
                    + "that binds to its default points the caller nowhere, silently.")
            : value;
    }

    /// <summary>Walks a colon-separated configuration key path.</summary>
    /// <param name="settings">The parsed settings root.</param>
    /// <param name="keyPath">The colon-separated key path.</param>
    /// <returns>The node at that path, or <see langword="null"/> when any segment is absent.</returns>
    private static JsonNode? FindNode(JsonObject settings, string keyPath)
    {
        JsonNode? current = settings;

        foreach (string segment in keyPath.Split(':', StringSplitOptions.RemoveEmptyEntries))
        {
            if (current is not JsonObject owner || !owner.TryGetPropertyValue(segment, out JsonNode? next))
            {
                return null;
            }

            current = next;
        }

        return current;
    }

    /// <summary>
    /// Enumerates a settings object as configuration leaf paths, treating an array as a single leaf.
    /// </summary>
    /// <returns>One entry per leaf, in declaration order.</returns>
    /// <remarks>
    /// An array counts as ONE leaf because that is how it is authored and how an options type exposes
    /// it; expanding it to indexed paths would make the leaf inventory depend on how many entries a
    /// list happens to hold today.
    /// </remarks>
    /// <summary>
    /// Replaces every numeric collection-element segment of a configuration key path with <c>*</c>.
    /// </summary>
    /// <param name="path">The key path, for example <c>Security:Clients:1:Subject</c>.</param>
    /// <returns>The path with element indices normalised, for example <c>Security:Clients:*:Subject</c>.</returns>
    /// <remarks>
    /// WHAT THIS SEPARATES. A leaf-parity assertion should hold a settings file to the MEMBERS the
    /// options type reads, which is a contract, and not to the NUMBER of elements a deployment
    /// registers, which is a deployment decision. Without normalisation the two are the same assertion,
    /// and registering an additional caller would fail a test about member names.
    /// </remarks>
    private static string NormaliseCollectionIndices(string path) =>
        string.Join(
            ':',
            path.Split(':').Select(static segment =>
                segment.Length > 0 && segment.All(char.IsAsciiDigit) ? "*" : segment));

    private static IEnumerable<(string Path, JsonNode? Value)> Flatten(JsonObject owner, string prefix = "")
    {
        foreach ((string name, JsonNode? value) in owner)
        {
            string path = prefix.Length == 0 ? name : $"{prefix}:{name}";

            if (value is JsonObject nested)
            {
                foreach ((string nestedPath, JsonNode? nestedValue) in Flatten(nested, path))
                {
                    yield return (nestedPath, nestedValue);
                }

                continue;
            }

            // AN ARRAY OF OBJECTS IS DESCENDED INTO; AN ARRAY OF SCALARS IS A LEAF. The distinction is
            // the binder's own: a scalar array binds wholesale to one collection property, so its
            // elements are values rather than settings, whereas an array of objects binds each element
            // to a nested options type and each MEMBER of that type is a setting in its own right. The
            // issuance roster is the case that makes this matter - without descending, a roster entry
            // could carry a member name the options type does not read, or lose one it does, and the
            // leaf-parity assertions would see one opaque leaf and notice neither.
            if (value is JsonArray array && array.Any(static element => element is JsonObject))
            {
                for (int index = 0; index < array.Count; index++)
                {
                    if (array[index] is not JsonObject element)
                    {
                        yield return ($"{path}:{index.ToString(CultureInfo.InvariantCulture)}", array[index]);

                        continue;
                    }

                    string elementPath = $"{path}:{index.ToString(CultureInfo.InvariantCulture)}";

                    foreach ((string nestedPath, JsonNode? nestedValue) in Flatten(element, elementPath))
                    {
                        yield return (nestedPath, nestedValue);
                    }
                }

                continue;
            }

            yield return (path, value);
        }
    }

    /// <summary>Reads an audience declaration, which may be a single value or a list of them.</summary>
    /// <param name="node">The declared node.</param>
    /// <param name="service">The owning service, for the diagnostic.</param>
    /// <param name="keyPath">The key path, for the diagnostic.</param>
    /// <returns>The declared audiences, blank entries rejected.</returns>
    private static IReadOnlyList<string> ReadAudiences(JsonNode? node, ServiceProfile service, string keyPath)
    {
        switch (node)
        {
            case null:
                return [];

            case JsonValue single:
            {
                string value = single.GetValue<string>();

                return string.IsNullOrWhiteSpace(value)
                    ? throw FailException.ForFailure(
                        $"{service.ProjectName} declares a blank audience at '{keyPath}'.")
                    : (IReadOnlyList<string>)[value];
            }

            case JsonArray many:
            {
                List<string> values = [];

                foreach (JsonNode? entry in many)
                {
                    string? value = entry is JsonValue candidate ? candidate.GetValue<string>() : null;

                    if (string.IsNullOrWhiteSpace(value))
                    {
                        throw FailException.ForFailure(
                            $"{service.ProjectName} declares a blank audience entry at '{keyPath}'.");
                    }

                    values.Add(value);
                }

                return values;
            }

            default:
                throw FailException.ForFailure(
                    $"{service.ProjectName} declares '{keyPath}' as neither a string nor a list of strings.");
        }
    }

    /// <summary>Extracts the port from a Kestrel listener URL, which may use a wildcard host.</summary>
    /// <param name="url">The declared URL.</param>
    /// <param name="service">The owning service, for the diagnostic.</param>
    /// <param name="keyPath">The key path, for the diagnostic.</param>
    /// <returns>The declared port.</returns>
    /// <remarks>
    /// <c>https://+:5101</c> binds every interface and is the form a container must use, but <c>+</c>
    /// is not a legal URI host, so the port is read from the text after the final colon rather than
    /// through <see cref="Uri"/>.
    /// </remarks>
    private static int ParseListenerPort(string url, ServiceProfile service, string keyPath)
    {
        int separator = url.LastIndexOf(':');

        if (separator >= 0
            && int.TryParse(
                url.AsSpan(separator + 1).TrimEnd('/'),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int port))
        {
            return port;
        }

        throw FailException.ForFailure(
            $"{service.ProjectName} declares '{keyPath}' without a readable port.");
    }

    /// <summary>One service's identity, assigned port and declared transport.</summary>
    /// <param name="Key">Stable lower-case key used by the theory data.</param>
    /// <param name="DirectoryName">Directory under <c>services/</c>.</param>
    /// <param name="ProjectName">The .NET project name, used in diagnostics.</param>
    /// <param name="Port">
    /// The one port the map assigns, carrying every surface the service publishes - the HTTP/1.1 REST one
    /// and, where <paramref name="ServesGrpc"/> is set, the HTTP/2 gRPC one alongside it.
    /// </param>
    /// <param name="ServesGrpc">
    /// Whether the service publishes a gRPC contract. It selects the <c>Protocols</c> value the single
    /// endpoint must declare - <c>Http1AndHttp2</c> when set so that ALPN can carry both surfaces on the
    /// assigned port, <c>Http1</c> when not - and it is what a gRPC caller edge is checked against.
    /// </param>
    /// <param name="DeclaresListener">Whether the service configures its own Kestrel endpoint.</param>
    /// <param name="ListenerScheme">The scheme callers must use to reach it.</param>
    /// <param name="RequiresClientCertificateMode">
    /// Whether its listener must declare <c>ClientCertificateMode</c>. True for Security alone, whose
    /// issuance operation accepts a client-certificate identity as one of its two credentials; asserted as
    /// equality in both directions, so its absence there and its presence elsewhere both fail.
    /// </param>
    private sealed record ServiceProfile(
        string Key,
        string DirectoryName,
        string ProjectName,
        int Port,
        bool ServesGrpc,
        bool DeclaresListener,
        string ListenerScheme,
        bool RequiresClientCertificateMode);

    /// <summary>Which surface of a target service an address is held for.</summary>
    /// <remarks>
    /// Both land on the same port now that a service binds one endpoint carrying both protocol versions.
    /// The distinction records which SURFACE the address is used against, which is what makes an edge
    /// pointed at a service publishing no gRPC contract visible here rather than at the first call.
    /// </remarks>
    private enum TargetEndpoint
    {
        /// <summary>The HTTP/1.1 REST surface on the port the map documents.</summary>
        Rest,

        /// <summary>The HTTP/2 gRPC surface, on the same port and the same endpoint.</summary>
        Grpc,
    }

    /// <summary>One address one service holds for another.</summary>
    /// <param name="CallerKey">Service holding the address.</param>
    /// <param name="FileName">Settings file the address is declared in.</param>
    /// <param name="KeyPath">Configuration key path of the address.</param>
    /// <param name="TargetKey">Service the address must reach.</param>
    /// <param name="Endpoint">Which of the target's endpoints the address must name.</param>
    private sealed record CallerAddress(
        string CallerKey,
        string FileName,
        string KeyPath,
        string TargetKey,
        TargetEndpoint Endpoint);

    /// <summary>Where one service declares the audience it validates on inbound requests.</summary>
    /// <param name="ServiceKey">The validating service.</param>
    /// <param name="KeyPath">Configuration key path of the audience declaration.</param>
    private sealed record InboundAudience(string ServiceKey, string KeyPath);
}
