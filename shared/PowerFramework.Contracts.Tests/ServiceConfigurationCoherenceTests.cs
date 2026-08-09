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
    /// Transcribed from AAP 0.3.2.2 and docs/ARCHITECTURE.md 4.1. Gateway declares no Kestrel section:
    /// it is the published ingress and its plaintext address is supplied by the orchestration layer,
    /// which is why <see cref="ServiceProfile.DeclaresListener"/> is <see langword="false"/> for it.
    /// </remarks>
    private static readonly ServiceProfile[] Services =
    [
        new(
            Key: "persistence",
            DirectoryName: "persistence-service",
            ProjectName: "PowerFramework.Persistence",
            Port: 5101,
            DeclaresListener: true,
            ListenerScheme: Uri.UriSchemeHttps,
            RequiresClientCertificateMode: false),
        new(
            Key: "dataservices",
            DirectoryName: "dataservices-service",
            ProjectName: "PowerFramework.DataServices",
            Port: 5102,
            DeclaresListener: true,
            ListenerScheme: Uri.UriSchemeHttps,
            RequiresClientCertificateMode: false),
        new(
            Key: "security",
            DirectoryName: "security-service",
            ProjectName: "PowerFramework.Security",
            Port: 5104,
            DeclaresListener: true,
            ListenerScheme: Uri.UriSchemeHttps,
            RequiresClientCertificateMode: true),
        new(
            Key: "gateway",
            DirectoryName: "gateway-service",
            ProjectName: "PowerFramework.Gateway",
            Port: 5105,
            DeclaresListener: false,
            ListenerScheme: Uri.UriSchemeHttp,
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
        new("gateway", BaseSettingsFileName, "Gateway:Upstreams:DataServices", "dataservices"),
        new("gateway", BaseSettingsFileName, "Gateway:Upstreams:Security", "security"),
        new("gateway", DevelopmentSettingsFileName, "Gateway:Upstreams:DataServices", "dataservices"),
        new("gateway", DevelopmentSettingsFileName, "Gateway:Upstreams:Security", "security"),
        new("gateway", DevelopmentSettingsFileName, "Authentication:Schemes:Bearer:Authority", "security"),
        new("dataservices", BaseSettingsFileName, "DataServices:Persistence:Address", "persistence"),
        new("dataservices", BaseSettingsFileName, "DataServices:Security:BaseAddress", "security"),
        new("dataservices", BaseSettingsFileName, "Authentication:Jwt:Authority", "security"),
        new("dataservices", DevelopmentSettingsFileName, "DataServices:Persistence:Address", "persistence"),
        new("dataservices", DevelopmentSettingsFileName, "DataServices:Security:BaseAddress", "security"),
        new("dataservices", DevelopmentSettingsFileName, "Authentication:Jwt:Authority", "security"),
        new("persistence", BaseSettingsFileName, "Jwt:Authority", "security"),
        new("persistence", DevelopmentSettingsFileName, "Jwt:Authority", "security"),
        new("security", BaseSettingsFileName, "Security:Issuer", "security"),
        new("security", DevelopmentSettingsFileName, "Security:Issuer", "security"),
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
    /// The complete authoritative leaf set of the <c>Security</c> section - twelve entries, no
    /// thirteenth.
    /// </summary>
    /// <remarks>
    /// An array-valued key counts as ONE leaf, because that is how it is authored and how the options
    /// type exposes it. <c>SigningKey</c> is absent on purpose and is asserted absent separately: it
    /// is the system's single signing secret and reaches the process through the flat environment key
    /// <c>SECURITY_JWT_SIGNING_KEY</c>, never through a settings file.
    /// </remarks>
    private static readonly string[] AuthoritativeSecurityLeafPaths =
    [
        "Security:Issuer",
        "Security:Audiences",
        "Security:TokenLifetime",
        "Security:SigningAlgorithm",

        // The two leaves that make the signing-key contract ENFORCED rather than documented. The key
        // arrives as a single line in an environment file, so a startup check is the only thing between
        // a wrong-shaped value and a host that starts and can never mint. The minimum size is a floor on
        // THIS key alone - the system's own net-new signing identity and the trust root every verifier
        // validates against - and is not a correction of the legacy's first-class 1024-bit allowance,
        // which is preserved where it belongs, on the caller-supplied C-02 surface.
        "Security:SigningKeyFormat",
        "Security:SigningKeyMinimumSizeBits",

        "Security:SigningKeyId",
        "Security:TokenEndpointPath",
        "Security:JwksPath",
        "Security:OpenIdConfigurationPath",
        "Security:KeyStore:ConfigurationKeyPrefix",
        "Security:KeyStore:PermittedKeyRefs",
    ];

    /// <summary>
    /// The complete authoritative option-leaf set of the Persistence service - nineteen entries, no
    /// twentieth.
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
    /// is precisely the exposure <c>Errors/SqlRedactor.cs</c> exists to close. Redaction is now
    /// UNCONDITIONAL and has no configuration surface at all, so a leaf here would name a key nothing
    /// binds and invite an operator to turn off something that cannot be turned off.
    /// </para>
    /// </remarks>
    private static readonly string[] AuthoritativePersistenceLeafPaths =
    [
        "Jwt:Authority",
        "Jwt:Audience",
        "Jwt:JwksPath",
        "Jwt:RequireHttpsMetadata",
        "Sqlite:DataDirectory",
        "Sqlite:DatabaseFileName",
        "Sqlite:Mode",
        "Sqlite:Check",
        "Sqlite:Journal",
        "TransactionPool:KeepAlive",
        "TransactionPool:KeepAliveExpireSeconds",
        "TransactionPool:TransactionClassName",
        "Query:ChunkSize",
        "Query:MaxRows",
        "Query:PageCounting",
        "Query:Cache",
        "Query:PageIndex",
        "Query:PageSize",
        "Query:Paged",
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
    public static TheoryData<string, string, string, string> AllCallerAddresses { get; } = BuildCallerAddressData();

    /// <summary>Every settings file, as a service key and file name pair.</summary>
    public static TheoryData<string, string> AllSettingsFiles { get; } = BuildSettingsFileData();

    // ----------------------------------------------------------------------------------------------
    // 1. One listener per service, on the port the map assigns it
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Asserts that a listening service declares exactly one Kestrel endpoint, on its assigned port,
    /// with the scheme the port map records, and with both protocol versions enabled.
    /// </summary>
    /// <param name="serviceKey">The service under test.</param>
    [Theory]
    [MemberData(nameof(ListeningServiceKeys))]
    public void EachListeningServiceDeclaresExactlyOneEndpointOnItsAssignedPort(string serviceKey)
    {
        ServiceProfile service = RequireService(serviceKey);
        JsonObject settings = LoadSettings(service, BaseSettingsFileName);
        JsonObject endpoints = RequireEndpoints(service, settings);

        Assert.Single(endpoints);

        (string _, JsonNode? endpointNode) = endpoints.First();
        JsonObject endpoint = RequireObject(endpointNode, service, BaseSettingsFileName, "Kestrel:Endpoints:<name>");

        string url = RequireString(endpoint, "Url", service, "Kestrel:Endpoints:<name>:Url");
        Assert.EndsWith(
            string.Create(CultureInfo.InvariantCulture, $":{service.Port}"),
            url,
            StringComparison.Ordinal);
        Assert.StartsWith(
            service.ListenerScheme + Uri.SchemeDelimiter,
            url,
            StringComparison.Ordinal);

        // Http1AndHttp2 is what makes one port carry gRPC and an HTTP/1.1 probe at the same time. On a
        // TLS listener ALPN selects between them per connection; the measurement behind this is in
        // docs/ARCHITECTURE.md 4.1.
        Assert.Equal(
            "Http1AndHttp2",
            RequireString(endpoint, "Protocols", service, "Kestrel:Endpoints:<name>:Protocols"));
    }

    /// <summary>
    /// Asserts that no declared listener and no caller address names a port outside the documented
    /// band, and that none names the port reserved for the deferred Phase-2 service.
    /// </summary>
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

                if (port is < LowestDocumentedPort or > HighestDocumentedPort)
                {
                    failures.Add(
                        $"{service.ProjectName} declares 'Kestrel:Endpoints:{name}:Url' on port {port}, "
                            + $"outside the documented {LowestDocumentedPort}-{HighestDocumentedPort} band.");
                }

                if (port == ReservedPhaseTwoPort)
                {
                    failures.Add(
                        $"{service.ProjectName} declares 'Kestrel:Endpoints:{name}:Url' on the reserved "
                            + $"port {ReservedPhaseTwoPort}, which must stay unallocated in this phase.");
                }
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
    [Theory]
    [MemberData(nameof(AllCallerAddresses))]
    public void EachCallerAddressResolvesToTheTargetServiceListener(
        string callerKey,
        string fileName,
        string keyPath,
        string targetKey)
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

        Assert.Equal(target.ListenerScheme, parsed.Scheme);
        Assert.Equal(target.Port, parsed.Port);
        Assert.Equal(string.Empty, parsed.UserInfo);
    }

    // ----------------------------------------------------------------------------------------------
    // 3. TLS where TLS is load-bearing
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Asserts that Security's listener requests a client certificate without demanding one, which is
    /// what lets the mutual-TLS issuance edge coexist with an anonymous readiness probe.
    /// </summary>
    /// <remarks>
    /// <c>RequireCertificate</c> was measured and rejected: it aborts the TLS handshake for a client
    /// presenting none, so the anonymous <c>/health</c> probe fails and the readiness chain that gates
    /// Gateway can never open. <c>AllowCertificate</c> requests the certificate and hands it to the
    /// application, which enforces its presence on the issuance operation alone.
    /// </remarks>
    [Fact]
    public void SecurityListenerRequestsAClientCertificateWithoutRequiringOne()
    {
        ServiceProfile security = RequireService("security");
        JsonObject endpoints = RequireEndpoints(security, LoadSettings(security, BaseSettingsFileName));

        (string name, JsonNode? endpointNode) = endpoints.First();
        JsonObject endpoint = RequireObject(
            endpointNode,
            security,
            BaseSettingsFileName,
            $"Kestrel:Endpoints:{name}");

        Assert.Equal(
            "AllowCertificate",
            RequireString(endpoint, "ClientCertificateMode", security, $"Kestrel:Endpoints:{name}:ClientCertificateMode"));
    }

    /// <summary>
    /// Asserts that only the services whose profile says so declare a <c>ClientCertificateMode</c>,
    /// so client-certificate handling stays confined to the one pair that adopted mutual TLS.
    /// </summary>
    /// <param name="serviceKey">The service under test.</param>
    [Theory]
    [MemberData(nameof(ListeningServiceKeys))]
    public void OnlyTheMutualTlsEdgeDeclaresClientCertificateHandling(string serviceKey)
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

            bool declared = endpoint.ContainsKey("ClientCertificateMode");
            Assert.Equal(service.RequiresClientCertificateMode, declared);
        }
    }

    /// <summary>
    /// Asserts that no settings file anywhere relaxes secure metadata retrieval.
    /// </summary>
    /// <remarks>
    /// Every authority in the estate is https, so a <c>false</c> here would relax nothing that needs
    /// relaxing - the setting only PERMITS a plaintext metadata address, it does not create one - while
    /// reading to a later maintainer as a requirement. A deployment that genuinely runs Security on
    /// plaintext has to state the relaxation itself.
    /// </remarks>
    /// <param name="serviceKey">The service whose file is under test.</param>
    /// <param name="fileName">The settings file under test.</param>
    [Theory]
    [MemberData(nameof(AllSettingsFiles))]
    public void NoSettingsFileRelaxesSecureMetadataRetrieval(string serviceKey, string fileName)
    {
        ServiceProfile service = RequireService(serviceKey);

        foreach ((string path, JsonNode? value) in Flatten(LoadSettings(service, fileName)))
        {
            if (!path.EndsWith("RequireHttpsMetadata", StringComparison.Ordinal))
            {
                continue;
            }

            Assert.True(
                value is JsonValue metadata && metadata.GetValue<bool>(),
                $"{service.ProjectName}/{fileName} sets '{path}' to a value other than true.");
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

        string[] declared = [.. Flatten(section, "Security").Select(static leaf => leaf.Path).Order(StringComparer.Ordinal)];
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

        HashSet<string> declaredInBase =
            [.. Flatten(baseSection, "Security").Select(static leaf => leaf.Path)];

        foreach ((string path, JsonNode? _) in Flatten(overlaySection, "Security"))
        {
            Assert.Contains(path, declaredInBase);
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

        string[] declared =
        [
            .. Flatten(settings)
                .Select(static leaf => leaf.Path)
                .Where(static path => !IsHostOwned(path))
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

    // ----------------------------------------------------------------------------------------------
    // Helpers
    // ----------------------------------------------------------------------------------------------

    /// <summary>Builds the caller-address theory data from <see cref="CallerAddresses"/>.</summary>
    /// <returns>One row per caller address.</returns>
    private static TheoryData<string, string, string, string> BuildCallerAddressData()
    {
        TheoryData<string, string, string, string> data = [];

        foreach (CallerAddress address in CallerAddresses)
        {
            data.Add(address.CallerKey, address.FileName, address.KeyPath, address.TargetKey);
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

    /// <summary>Locates the repository's <c>services</c> directory by walking up from the test binary.</summary>
    /// <returns>The absolute path of the services directory.</returns>
    /// <remarks>
    /// Both repository markers are required together with the directory itself, so the walk cannot latch
    /// onto a same-named directory elsewhere on the machine. The probe is read-only.
    /// </remarks>
    private static string RequireServicesDirectory()
    {
        DirectoryInfo? candidate = new(AppContext.BaseDirectory);

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
    /// <param name="owner">The object to flatten.</param>
    /// <param name="prefix">Path prefix, empty for the settings root.</param>
    /// <returns>One entry per leaf, in declaration order.</returns>
    /// <remarks>
    /// An array counts as ONE leaf because that is how it is authored and how an options type exposes
    /// it; expanding it to indexed paths would make the leaf inventory depend on how many entries a
    /// list happens to hold today.
    /// </remarks>
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
    /// <param name="Port">The port the map assigns.</param>
    /// <param name="DeclaresListener">Whether the service configures its own Kestrel endpoint.</param>
    /// <param name="ListenerScheme">The scheme callers must use to reach it.</param>
    /// <param name="RequiresClientCertificateMode">Whether its listener handles client certificates.</param>
    private sealed record ServiceProfile(
        string Key,
        string DirectoryName,
        string ProjectName,
        int Port,
        bool DeclaresListener,
        string ListenerScheme,
        bool RequiresClientCertificateMode);

    /// <summary>One address one service holds for another.</summary>
    /// <param name="CallerKey">Service holding the address.</param>
    /// <param name="FileName">Settings file the address is declared in.</param>
    /// <param name="KeyPath">Configuration key path of the address.</param>
    /// <param name="TargetKey">Service the address must reach.</param>
    private sealed record CallerAddress(string CallerKey, string FileName, string KeyPath, string TargetKey);

    /// <summary>Where one service declares the audience it validates on inbound requests.</summary>
    /// <param name="ServiceKey">The validating service.</param>
    /// <param name="KeyPath">Configuration key path of the audience declaration.</param>
    private sealed record InboundAudience(string ServiceKey, string KeyPath);
}
