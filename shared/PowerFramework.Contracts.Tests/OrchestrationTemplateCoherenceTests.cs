// ==================================================================================================
//  OrchestrationTemplateCoherenceTests - THE MECHANICAL GUARD OVER orchestration/.env.example
//  ------------------------------------------------------------------------------------------------
//  SUBJECT     One file: orchestration/.env.example, the committed template for the uncommitted
//              environment file the Compose manifest reads.
//  AUTHORITY   Agent Action Plan 0.3.2.2 (the fixed port map), 0.6.6.3 (the token topology - exactly
//              one signing secret, held by Security), 0.7.3 constraints C-B, C-F and C-G,
//              docs/SECRETS.md, and the authoritative option surfaces of the four services as
//              declared by their own appsettings files.
//
//  WHY THIS FILE NEEDS A TEST AT ALL, WHICH IS THE FIRST QUESTION A READER SHOULD HAVE
//  ------------------------------------------------------------------------------------------------
//  It is prose and assignments, so nothing about it compiles and nothing about it runs. Every defect
//  it can carry is therefore invisible to the compiler, invisible to the linter, and invisible until
//  an operator follows it. Three classes of defect matter, and all three have already occurred here:
//
//    * A DOCUMENTED KEY PATH THAT RESOLVES TO NOTHING. The template's own parity rule requires every
//      variable to name the configuration key it maps onto. A name that no option binds is worse than
//      a missing one: it reads as configuration, an operator edits it expecting an effect, and nothing
//      happens. An absolute JWKS URL was declared here and no verifier had a reader for it.
//    * AN INSTRUCTION THAT CANNOT PRODUCE USABLE MATERIAL. The template once documented symmetric
//      random bytes for a signing key the service imports as an RSA private key. Following it produced
//      a stack that refused to start, and the natural "fix" - switching the algorithm to an HMAC family
//      - would have published the signing secret in the JWK set.
//    * A DEPLOYMENT-TIME OVERRIDE OF A PRESERVED BEHAVIOURAL DEFAULT. The template once narrowed the
//      Gateway capability mask, so the running system disagreed with both the code default and the
//      legacy call site it reproduces. That is the silent behavioural change C-B forbids.
//
//  Each assertion below turns one of those into a build-time failure that names the offending line.
//
//  WHY A CONTRACTS TEST OWNS IT
//  ------------------------------------------------------------------------------------------------
//  The invariants compare the template against the option surfaces of all four services at once, so
//  no single service's test project is where they can be stated. This project already owns the
//  published cross-service boundary. It is also the only test project that compiles while the four
//  service Program.cs files are still pending, so a guard placed anywhere else could not run.
//
//  WHAT THESE ASSERTIONS DELIBERATELY DO NOT DO
//  ------------------------------------------------------------------------------------------------
//  They read no environment variable, open no socket and start no host: they are assertions about the
//  authored text of one file plus the eight authored settings files, which is what makes them runnable
//  in a clean checkout with nothing brought up. They also assert nothing about, and name none of, the
//  four deferred services - C-D is satisfied by absence rather than by a blocklist.
// ==================================================================================================

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;
using Xunit.Sdk;

namespace PowerFramework.Contracts.Tests;

/// <summary>
/// Assertions over <c>orchestration/.env.example</c>: its declared format, its variable roster, the
/// resolvability of every configuration key path it documents, the absence of secret material and of
/// instructions that cannot produce usable material, and its agreement with the port map and with the
/// preserved behavioural defaults the services declare.
/// </summary>
public sealed class OrchestrationTemplateCoherenceTests
{
    /// <summary>Repository-root marker: the solution file, in the .NET 10 XML format.</summary>
    private const string SolutionMarkerFileName = "PowerFramework.slnx";

    /// <summary>Repository-root marker: the central package-version manifest.</summary>
    private const string PackageVersionMarkerFileName = "Directory.Packages.props";

    /// <summary>The orchestration directory, relative to the repository root.</summary>
    private const string OrchestrationDirectoryName = "orchestration";

    /// <summary>The template file name.</summary>
    private const string TemplateFileName = ".env.example";

    /// <summary>The services directory, relative to the repository root.</summary>
    private const string ServicesDirectoryName = "services";

    /// <summary>Base settings file name, authored for every service.</summary>
    private const string BaseSettingsFileName = "appsettings.json";

    /// <summary>Environment overlay file name, authored for every service.</summary>
    private const string DevelopmentSettingsFileName = "appsettings.Development.json";

    /// <summary>
    /// Reading the settings files the way the host reads them: comments skipped, trailing commas
    /// tolerated. The authored files use both.
    /// </summary>
    private static readonly JsonDocumentOptions SettingsDocumentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Every settings file the documented key paths are resolved against.</summary>
    /// <remarks>
    /// Both files of all four services, because a variable may legitimately target a key that only the
    /// environment overlay declares.
    /// </remarks>
    private static readonly string[] SettingsFileRelativePaths =
    [
        $"persistence-service/PowerFramework.Persistence/{BaseSettingsFileName}",
        $"persistence-service/PowerFramework.Persistence/{DevelopmentSettingsFileName}",
        $"dataservices-service/PowerFramework.DataServices/{BaseSettingsFileName}",
        $"dataservices-service/PowerFramework.DataServices/{DevelopmentSettingsFileName}",
        $"security-service/PowerFramework.Security/{BaseSettingsFileName}",
        $"security-service/PowerFramework.Security/{DevelopmentSettingsFileName}",
        $"gateway-service/PowerFramework.Gateway/{BaseSettingsFileName}",
        $"gateway-service/PowerFramework.Gateway/{DevelopmentSettingsFileName}",
    ];

    /// <summary>The complete expected variable roster - twenty-two names, no twenty-third.</summary>
    /// <remarks>
    /// <para>
    /// Spelled out rather than derived, so that both an addition and a removal are findings. The
    /// template's parity rule runs in both directions: a variable the manifest references but the
    /// template omits makes bring-up fail or silently resolve empty, and a variable the template
    /// declares that nothing references is dead configuration.
    /// </para>
    /// <para>
    /// The roster is four families, and reading it as one flat list hides why the count is what it is.
    /// TEN are the service-shape variables an operator always sets: the environment name, the three
    /// in-network base addresses, the two token-topology names, the signing secret, and the three
    /// Persistence storage and pool settings. TWO are the shared server-certificate paths, which are
    /// deliberately ONE pair for the whole stack rather than one pair per listener - every service
    /// terminates TLS with the same default material at <c>Kestrel:Certificates:Default</c>. FIVE are
    /// CLIENT-side mutual-TLS material, which is a different thing from the server pair and cannot be
    /// folded onto it: the client certificate Gateway presents to Security, the one DataServices
    /// presents to Security, and the issuer whose client certificates Security is willing to accept.
    /// THREE are Gateway's health-probe addresses, which stay separate from its upstream call addresses
    /// even where the values coincide, because one is what a caller dials and the other is what an
    /// observer reads - and because C-10 bounds the aggregate at three upstreams while Gateway's call
    /// roster has only two.
    /// </para>
    /// <para>
    /// GATEWAY_LOCALE and GATEWAY_CAPABILITY_FLAGS are the twenty-first and twenty-second, and they are
    /// the two that restate a preserved behavioural default rather than configuring anything new; the
    /// pairing below holds them to the value the service itself declares.
    /// </para>
    /// </remarks>
    private static readonly string[] ExpectedVariableNames =
    [
        "ASPNETCORE_ENVIRONMENT",
        "DATASERVICES_BASE_URL",
        "DATASERVICES_MTLS_CERT_PATH",
        "DATASERVICES_MTLS_KEY_PATH",
        "GATEWAY_CAPABILITY_FLAGS",
        "GATEWAY_HEALTH_PROBE_DATASERVICES_URL",
        "GATEWAY_HEALTH_PROBE_PERSISTENCE_URL",
        "GATEWAY_HEALTH_PROBE_SECURITY_URL",
        "GATEWAY_LOCALE",
        "GATEWAY_MTLS_CERT_PATH",
        "GATEWAY_MTLS_KEY_PATH",
        "PERSISTENCE_BASE_URL",
        "PERSISTENCE_SQLITE_DATA_DIRECTORY",
        "PERSISTENCE_TRANSPOOL_KEEPALIVE",
        "PERSISTENCE_TRANSPOOL_KEEPALIVE_EXPIRE_SECONDS",
        "SECURITY_BASE_URL",
        "SECURITY_JWT_AUDIENCE",
        "SECURITY_JWT_ISSUER",
        "SECURITY_JWT_SIGNING_KEY",
        "SECURITY_MTLS_CLIENT_CA_PATH",
        "TLS_CERTIFICATE_KEY_PATH",
        "TLS_CERTIFICATE_PATH",
    ];

    /// <summary>
    /// The variables whose value must be EMPTY in the committed template, because a populated one
    /// would be secret material or a path to it.
    /// </summary>
    /// <remarks>
    /// An example key is indistinguishable from a real one to a reader, and placeholder keys have a
    /// long history of reaching production unchanged - so these are left empty rather than pre-filled
    /// (C-F, docs/SECRETS.md).
    /// </remarks>
    private static readonly string[] MustBeEmptyVariableNames =
    [
        "SECURITY_JWT_SIGNING_KEY",
        "TLS_CERTIFICATE_PATH",
        "TLS_CERTIFICATE_KEY_PATH",
    ];

    /// <summary>
    /// Configuration keys that legitimately have no settings-file declaration, with the reason.
    /// </summary>
    /// <remarks>
    /// Each is environment-only BY DESIGN: the two certificate paths and the two secrets must never
    /// appear in an authored settings file (C-F), and the signing key additionally binds as a FLAT key
    /// because <c>__</c> is the section separator and <c>SECURITY_JWT_</c> would otherwise be read as
    /// three nested sections.
    /// </remarks>
    private static readonly string[] EnvironmentOnlyConfigurationKeys =
    [
        "Kestrel:Certificates:Default:Path",
        "Kestrel:Certificates:Default:KeyPath",
        "Sqlite:Password",
        "SECURITY_JWT_SIGNING_KEY",
    ];

    /// <summary>
    /// Key spellings the template mentions only in order to FORBID them.
    /// </summary>
    /// <remarks>
    /// The template tells a reader not to rename the flat signing-key variable into a section path, and
    /// naming the wrong spelling is how that instruction is made actionable. Without this allowance the
    /// resolvability check below would report the prohibition itself as the defect it exists to prevent.
    /// </remarks>
    private static readonly string[] DocumentedOnlyToBeForbidden = ["Security__SigningKey"];

    /// <summary>Every port the map assigns, as it may appear inside a template VALUE.</summary>
    /// <remarks>
    /// 5103 is absent deliberately: the map reserves it as a commented Phase-2 slot, so a value
    /// naming it would be an address for a service that does not exist.
    /// </remarks>
    private static readonly int[] AssignedPorts = [5101, 5102, 5104, 5105];

    /// <summary>Each address variable, the port it must name, and the scheme it must use.</summary>
    private static readonly AddressExpectation[] AddressExpectations =
    [
        new("PERSISTENCE_BASE_URL", 5101, Uri.UriSchemeHttps),
        new("DATASERVICES_BASE_URL", 5102, Uri.UriSchemeHttps),
        new("SECURITY_BASE_URL", 5104, Uri.UriSchemeHttps),
        new("SECURITY_JWT_ISSUER", 5104, Uri.UriSchemeHttps),
    ];

    /// <summary>
    /// The variables that restate a preserved behavioural default, paired with the service settings key
    /// that default is authored at.
    /// </summary>
    /// <remarks>
    /// These variables exist so an operator can SEE the knob, not so orchestration can move it. Any
    /// divergence between the two sides means the deployed system disagrees with the code default, and
    /// for a legacy-preserving default that divergence is the silent behavioural change C-B forbids.
    /// </remarks>
    private static readonly PreservedDefault[] PreservedDefaults =
    [
        new(
            "GATEWAY_CAPABILITY_FLAGS",
            $"gateway-service/PowerFramework.Gateway/{BaseSettingsFileName}",
            "Gateway:CapabilityFlags"),
        new(
            "GATEWAY_LOCALE",
            $"gateway-service/PowerFramework.Gateway/{BaseSettingsFileName}",
            "Gateway:Locale"),
    ];

    /// <summary>Every variable that must be left empty.</summary>
    public static TheoryData<string> AllMustBeEmptyVariableNames { get; } =
        BuildStringData(MustBeEmptyVariableNames);

    /// <summary>Every address variable, with its expected port and scheme.</summary>
    public static TheoryData<string, int, string> AllAddressExpectations { get; } =
        BuildAddressData();

    /// <summary>Every preserved default, with the settings key it must agree with.</summary>
    public static TheoryData<string, string, string> AllPreservedDefaults { get; } =
        BuildPreservedDefaultData();

    /// <summary>
    /// Every assignment obeys the format the template declares for itself: a bare
    /// <c>KEY=value</c> line, with no <c>export</c>, no quoting and no line continuation.
    /// </summary>
    /// <remarks>
    /// This is not house style. The format is what makes the single-line base64 signing key the only
    /// expressible shape for that variable - a multi-line PEM block cannot be written here at all - and
    /// it is what lets reserved URI characters pass through unquoted. A quoted or continued line would
    /// silently change how Compose's dotenv parser reads the value.
    /// </remarks>
    [Fact]
    public void TemplateObeysTheBareKeyEqualsValueFormatItDeclares()
    {
        string[] lines = ReadTemplateLines();
        HashSet<string> seen = new(StringComparer.Ordinal);
        List<string> failures = [];

        for (int index = 0; index < lines.Length; index++)
        {
            string line = lines[index];
            int number = index + 1;

            if (line.Length == 0 || line.TrimStart().StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith("export ", StringComparison.Ordinal))
            {
                failures.Add($"L{number}: begins with 'export', which the declared format excludes.");
                continue;
            }

            int separator = line.IndexOf('=', StringComparison.Ordinal);

            if (separator <= 0 || !IsVariableName(line[..separator]))
            {
                failures.Add($"L{number}: is neither blank, a whole-line comment, nor 'KEY=value'.");
                continue;
            }

            string name = line[..separator];
            string value = line[(separator + 1)..];

            if (!seen.Add(name))
            {
                failures.Add(
                    $"L{number}: '{name}' is assigned twice. The later line wins, so the file would "
                        + "state one value while a reader deduced another.");
            }

            if (value.Contains('"', StringComparison.Ordinal)
                || value.Contains('\'', StringComparison.Ordinal))
            {
                failures.Add($"L{number}: '{name}' is quoted; values are taken literally here.");
            }

            if (value.EndsWith('\\'))
            {
                failures.Add(
                    $"L{number}: '{name}' ends with a continuation, which this format does not support.");
            }

            if (value.Length != value.Trim().Length)
            {
                failures.Add(
                    $"L{number}: '{name}' has leading or trailing whitespace, which becomes part of the "
                        + "value because nothing is trimmed.");
            }
        }

        Assert.True(failures.Count == 0, Describe("format violations", failures));
    }

    /// <summary>The template declares exactly the expected roster, with nothing added or missing.</summary>
    [Fact]
    public void TemplateDeclaresExactlyTheExpectedVariableRoster()
    {
        string[] declared = [.. ReadTemplateVariables().Keys.Order(StringComparer.Ordinal)];
        string[] expected = [.. ExpectedVariableNames.Order(StringComparer.Ordinal)];

        string[] missing = [.. expected.Except(declared, StringComparer.Ordinal)];
        string[] unexpected = [.. declared.Except(expected, StringComparer.Ordinal)];

        Assert.True(
            missing.Length == 0 && unexpected.Length == 0,
            $"The orchestration roster has drifted. Missing: {Join(missing)}. Unexpected: "
                + $"{Join(unexpected)}. A missing variable makes bring-up fail or silently resolve to an "
                + "empty value; an unexpected one is configuration nothing consumes.");
    }

    /// <summary>Every secret-valued variable is present and empty.</summary>
    [Theory]
    [MemberData(nameof(AllMustBeEmptyVariableNames))]
    public void TemplateLeavesEverySecretValuedVariableEmpty(string variableName)
    {
        IReadOnlyDictionary<string, string> variables = ReadTemplateVariables();

        Assert.True(
            variables.TryGetValue(variableName, out string? value),
            $"'{variableName}' is not declared. It is part of the roster, and its absence would make the "
                + "manifest resolve it to an empty value with no statement that it had to be supplied.");

        Assert.True(
            value!.Length == 0,
            $"'{variableName}' carries a value. It must be empty in the committed template: an example is "
                + "indistinguishable from real material to a reader, and placeholder credentials have a "
                + "long history of reaching production unchanged.");
    }

    /// <summary>
    /// No line of the template - assignment or comment - carries secret-shaped text.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Comments are searched as well as values, because a key pasted into an example is exactly as
    /// disclosed as one pasted into an assignment. The base64 test is applied to values only: prose
    /// legitimately contains long words, whereas a value that is a long encoded run is key material.
    /// </para>
    /// <para>
    /// The armour marker is the discriminator rather than a phrase like "private key". This file's whole
    /// subject IS a private key, so it discusses one in English throughout and a phrase match would
    /// report the documentation as the defect. The five-hyphen BEGIN delimiter is never natural prose,
    /// and it is what every secret scanner keys on, so it is both the precise signal and the one that
    /// matters operationally. It covers a key, a certificate and an ASCII-armoured block alike.
    /// </para>
    /// </remarks>
    [Fact]
    public void TemplateCarriesNoSecretShapedText()
    {
        const string ArmourMarker = "-----BEGIN";

        string text = ReadTemplateText();
        List<string> failures = [];

        if (text.Contains(ArmourMarker, StringComparison.OrdinalIgnoreCase))
        {
            failures.Add($"the text contains the armour delimiter '{ArmourMarker}'.");
        }

        foreach ((string name, string value) in ReadTemplateVariables())
        {
            if (LooksLikeEncodedMaterial(value))
            {
                failures.Add($"'{name}' holds a long encoded run, which is the shape of key material.");
            }
        }

        Assert.True(failures.Count == 0, Describe("secret-shaped text", failures));
    }

    /// <summary>
    /// The signing-key instructions generate ASYMMETRIC material, and the withdrawn symmetric
    /// instruction is nowhere in the file.
    /// </summary>
    /// <remarks>
    /// Security signs with RS256 over a closed RS-family allow-list and imports the configured material
    /// as an RSA private key, so random bytes fail the startup usability check and the host refuses to
    /// start. Measured: base64 of 32 random bytes and of 64 random bytes are both rejected by the import
    /// path, while a PKCS#8 PEM key, a PKCS#1 PEM key and a single-line base64 of the DER encoding are
    /// all accepted at 2048 bits.
    /// </remarks>
    [Fact]
    public void TemplateInstructsAsymmetricKeyGenerationForTheSigningKey()
    {
        string text = ReadTemplateText();

        Assert.True(
            text.Contains("openssl genpkey", StringComparison.Ordinal),
            "The template documents no asymmetric key generation. An operator has to be told how to "
                + "produce material Security can import, or the first bring-up fails at startup with "
                + "nothing to act on.");

        Assert.DoesNotContain("openssl rand -base64", text, StringComparison.Ordinal);
        Assert.DoesNotContain("openssl rand -hex", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// No variable exists for an absolute JWK-set address, because no verifier can read one.
    /// </summary>
    /// <remarks>
    /// Gateway and DataServices reach the key set through the discovery document beneath their bearer
    /// authority and expose no key-set setting; Persistence composes its own from
    /// <c>Jwt:Authority</c> plus the RELATIVE <c>Jwt:JwksPath</c>. An absolute URL has nowhere to go in
    /// any of the three shapes.
    /// </remarks>
    [Fact]
    public void TemplateDeclaresNoAbsoluteJwksAddressVariable()
    {
        string[] offending =
        [
            .. ReadTemplateVariables()
                .Keys
                .Where(static name => name.Contains("JWKS", StringComparison.OrdinalIgnoreCase))
                .Order(StringComparer.Ordinal),
        ];

        Assert.True(
            offending.Length == 0,
            $"The template declares {Join(offending)}. No service binds an absolute key-set address, so "
                + "such a variable reads as configuration, gets edited, and changes nothing.");
    }

    /// <summary>
    /// Every configuration key path the template documents resolves to a real option leaf.
    /// </summary>
    /// <remarks>
    /// This is the assertion the template's own parity rule asks for, made mechanical. It harvests every
    /// <c>Section__Key</c> token in the file and resolves it against the leaves of all eight authored
    /// settings files, allowing only the environment-only keys and the spellings the template mentions
    /// in order to forbid them. It is what catches a key path left behind when a section is re-rooted.
    /// </remarks>
    [Fact]
    public void EveryDocumentedConfigurationKeyPathResolvesToADeclaredOptionLeaf()
    {
        HashSet<string> declared = ReadAllDeclaredLeafPaths();
        List<string> failures = [];

        foreach (string token in HarvestDocumentedKeyPaths(ReadTemplateText()))
        {
            if (DocumentedOnlyToBeForbidden.Contains(token, StringComparer.Ordinal))
            {
                continue;
            }

            string path = token.Replace("__", ":", StringComparison.Ordinal);

            if (declared.Contains(path)
                || EnvironmentOnlyConfigurationKeys.Contains(path, StringComparer.Ordinal))
            {
                continue;
            }

            // An array is one authored leaf, so an indexed reference resolves through its parent.
            string? parent = StripIndexSuffix(path);

            if (parent is not null && declared.Contains(parent))
            {
                continue;
            }

            failures.Add($"'{token}' resolves to '{path}', which no settings file declares.");
        }

        Assert.True(failures.Count == 0, Describe("unresolvable key paths", failures));
    }

    /// <summary>Every address variable names the port the map assigns, on the scheme it serves.</summary>
    [Theory]
    [MemberData(nameof(AllAddressExpectations))]
    public void TemplateAddressResolvesToTheAssignedListener(
        string variableName,
        int expectedPort,
        string expectedScheme)
    {
        string value = RequireVariable(variableName);

        Assert.True(
            Uri.TryCreate(value, UriKind.Absolute, out Uri? address),
            $"'{variableName}' is not an absolute address.");

        Assert.Equal(expectedScheme, address!.Scheme);

        Assert.True(
            address.Port == expectedPort,
            $"'{variableName}' names port {address.Port}, but the map assigns {expectedPort}. A caller "
                + "pointed at a port nobody serves fails at first use, three layers from its cause.");
    }

    /// <summary>No value names a port outside the assigned set.</summary>
    /// <remarks>
    /// The check is on VALUES only. Comments discuss the reserved Phase-2 slot and record a rejected
    /// parallel-band alternative, and recording a rejected alternative is a documentation requirement
    /// (C-K) rather than a live address.
    /// </remarks>
    [Fact]
    public void NoTemplateValueNamesAnUnassignedPort()
    {
        List<string> failures = [];

        foreach ((string name, string value) in ReadTemplateVariables())
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? address) || address.IsDefaultPort)
            {
                continue;
            }

            if (!AssignedPorts.Contains(address.Port))
            {
                failures.Add(
                    $"'{name}' names port {address.Port}, which the map does not assign to any service.");
            }
        }

        Assert.True(failures.Count == 0, Describe("unassigned ports", failures));
    }

    /// <summary>
    /// A variable that restates a preserved default states the same value the service declares.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllPreservedDefaults))]
    public void TemplateRestatesPreservedDefaultsWithoutMovingThem(
        string variableName,
        string settingsRelativePath,
        string keyPath)
    {
        string declared = RequireVariable(variableName);
        JsonNode? node = FindNode(LoadSettings(settingsRelativePath), keyPath);

        Assert.True(
            node is JsonValue,
            $"'{settingsRelativePath}' declares no value at '{keyPath}', so '{variableName}' has nothing "
                + "to agree with.");

        string authored = node!.ToJsonString().Trim('"');

        Assert.True(
            string.Equals(declared, authored, StringComparison.Ordinal),
            $"'{variableName}' is '{declared}' while '{keyPath}' is '{authored}'. This variable exists so "
                + "an operator can see the knob, not so orchestration can move it: for a default that "
                + "reproduces legacy behaviour, a deployment-time divergence is a silent behavioural "
                + "change.");
    }

    /// <summary>
    /// The audience the template templates is one the ingress actually validates.
    /// </summary>
    [Fact]
    public void TemplateIngressAudienceIsTheOneGatewayValidates()
    {
        string templated = RequireVariable("SECURITY_JWT_AUDIENCE");
        JsonObject gateway = LoadSettings($"gateway-service/PowerFramework.Gateway/{BaseSettingsFileName}");
        JsonNode? node = FindNode(gateway, "Authentication:Schemes:Bearer:ValidAudiences");

        Assert.True(node is JsonArray, "Gateway declares no 'ValidAudiences' list to compare against.");

        string[] validated =
        [
            .. ((JsonArray)node!)
                .Where(static entry => entry is JsonValue)
                .Select(static entry => entry!.ToJsonString().Trim('"')),
        ];

        Assert.True(
            validated.Contains(templated, StringComparer.Ordinal),
            $"The template issues for audience '{templated}', which Gateway does not validate "
                + $"({Join(validated)}). A token minted for an audience the ingress rejects fails closed, "
                + "as a 401 that looks like a caller error.");
    }

    /// <summary>
    /// Exactly one signing secret is declared, and no per-service signing key under any spelling.
    /// </summary>
    /// <remarks>
    /// Security is the sole issuer; the other three hold verification material only. A per-service
    /// signing key would make each of them a second issuer, and the security properties of a
    /// sole-issuer topology depend on there being exactly one (AAP 0.6.6.3).
    /// </remarks>
    [Fact]
    public void TemplateDeclaresExactlyOneSigningSecret()
    {
        string[] signingVariables =
        [
            .. ReadTemplateVariables()
                .Keys
                .Where(static name => name.Contains("SIGNING_KEY", StringComparison.OrdinalIgnoreCase))
                .Order(StringComparer.Ordinal),
        ];

        Assert.True(
            signingVariables.Length == 1
                && string.Equals(signingVariables[0], "SECURITY_JWT_SIGNING_KEY", StringComparison.Ordinal),
            $"The template declares {Join(signingVariables)} as signing material. Exactly one signing "
                + "secret exists in this system and Security holds it.");
    }

    /// <summary>Builds theory data from a name list.</summary>
    /// <param name="names">The names to project.</param>
    /// <returns>One row per name.</returns>
    private static TheoryData<string> BuildStringData(string[] names)
    {
        TheoryData<string> data = [];

        foreach (string name in names)
        {
            data.Add(name);
        }

        return data;
    }

    /// <summary>Builds theory data for the address expectations.</summary>
    /// <returns>One row per address variable.</returns>
    private static TheoryData<string, int, string> BuildAddressData()
    {
        TheoryData<string, int, string> data = [];

        foreach (AddressExpectation expectation in AddressExpectations)
        {
            data.Add(expectation.VariableName, expectation.Port, expectation.Scheme);
        }

        return data;
    }

    /// <summary>Builds theory data for the preserved defaults.</summary>
    /// <returns>One row per preserved default.</returns>
    private static TheoryData<string, string, string> BuildPreservedDefaultData()
    {
        TheoryData<string, string, string> data = [];

        foreach (PreservedDefault preserved in PreservedDefaults)
        {
            data.Add(preserved.VariableName, preserved.SettingsRelativePath, preserved.KeyPath);
        }

        return data;
    }

    /// <summary>Reports whether a token is a legal environment-variable name.</summary>
    /// <param name="candidate">The text before the first <c>=</c>.</param>
    /// <returns><see langword="true"/> when it is a legal name.</returns>
    private static bool IsVariableName(string candidate)
    {
        if (candidate.Length == 0 || char.IsAsciiDigit(candidate[0]))
        {
            return false;
        }

        foreach (char character in candidate)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character != '_')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Reports whether a value has the shape of encoded key material.</summary>
    /// <param name="value">The declared value.</param>
    /// <returns><see langword="true"/> when it is a long base64 or hex run.</returns>
    private static bool LooksLikeEncodedMaterial(string value)
    {
        if (value.Length < 24)
        {
            return false;
        }

        foreach (char character in value)
        {
            if (!char.IsAsciiLetterOrDigit(character)
                && character != '+'
                && character != '/'
                && character != '=')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Harvests every <c>Section__Key</c> token from the template text.
    /// </summary>
    /// <param name="text">The whole template.</param>
    /// <returns>The distinct tokens, ordered.</returns>
    /// <remarks>
    /// A token is a maximal run of letters, digits and underscores that contains a DOUBLE underscore
    /// and neither starts nor ends with one. That excludes the flat variable names, which carry single
    /// underscores only, and captures a documented section path however deeply nested.
    /// </remarks>
    private static IReadOnlyCollection<string> HarvestDocumentedKeyPaths(string text)
    {
        SortedSet<string> tokens = new(StringComparer.Ordinal);

        for (int index = 0; index + 1 < text.Length; index++)
        {
            if (text[index] != '_' || text[index + 1] != '_')
            {
                continue;
            }

            int start = index;

            while (start > 0 && IsTokenCharacter(text[start - 1]))
            {
                start--;
            }

            int end = index + 2;

            while (end < text.Length && IsTokenCharacter(text[end]))
            {
                end++;
            }

            string token = text[start..end];

            if (token.StartsWith('_') || token.EndsWith('_'))
            {
                continue;
            }

            tokens.Add(token);
        }

        return tokens;
    }

    /// <summary>Reports whether a character may appear inside a harvested token.</summary>
    /// <param name="character">The character to test.</param>
    /// <returns><see langword="true"/> when it is a letter, a digit or an underscore.</returns>
    private static bool IsTokenCharacter(char character) =>
        char.IsAsciiLetterOrDigit(character) || character == '_';

    /// <summary>Removes a trailing all-digit segment from a configuration path.</summary>
    /// <param name="path">The colon-separated path.</param>
    /// <returns>The parent path, or <see langword="null"/> when the last segment is not an index.</returns>
    private static string? StripIndexSuffix(string path)
    {
        int separator = path.LastIndexOf(':');

        if (separator <= 0)
        {
            return null;
        }

        foreach (char character in path.AsSpan(separator + 1))
        {
            if (!char.IsAsciiDigit(character))
            {
                return null;
            }
        }

        return path[..separator];
    }

    /// <summary>Reads the template as lines, with line endings normalized away.</summary>
    /// <returns>The template's lines.</returns>
    private static string[] ReadTemplateLines() =>
        ReadTemplateText().Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

    /// <summary>Reads the whole template.</summary>
    /// <returns>The template text.</returns>
    private static string ReadTemplateText()
    {
        string path = Path.Combine(
            RequireRepositoryRoot(),
            OrchestrationDirectoryName,
            TemplateFileName);

        return File.Exists(path)
            ? File.ReadAllText(path)
            : throw FailException.ForFailure(
                $"'{OrchestrationDirectoryName}/{TemplateFileName}' does not exist. It is the committed "
                    + "template the Compose manifest reads, so its absence is a finding rather than a "
                    + "reason to skip a check.");
    }

    /// <summary>Reads every assignment in the template.</summary>
    /// <returns>The variables, in declaration order, later assignment winning as the parser does.</returns>
    private static IReadOnlyDictionary<string, string> ReadTemplateVariables()
    {
        Dictionary<string, string> variables = new(StringComparer.Ordinal);

        foreach (string line in ReadTemplateLines())
        {
            if (line.Length == 0 || line.TrimStart().StartsWith('#'))
            {
                continue;
            }

            int separator = line.IndexOf('=', StringComparison.Ordinal);

            if (separator <= 0 || !IsVariableName(line[..separator]))
            {
                continue;
            }

            variables[line[..separator]] = line[(separator + 1)..];
        }

        return variables;
    }

    /// <summary>Reads one variable, failing with a named diagnostic when it is absent.</summary>
    /// <param name="variableName">The variable to read.</param>
    /// <returns>Its declared value.</returns>
    private static string RequireVariable(string variableName) =>
        ReadTemplateVariables().TryGetValue(variableName, out string? value)
            ? value
            : throw FailException.ForFailure(
                $"The template declares no '{variableName}'.");

    /// <summary>Reads the union of the leaf paths of all eight authored settings files.</summary>
    /// <returns>Every declared configuration leaf path.</returns>
    private static HashSet<string> ReadAllDeclaredLeafPaths()
    {
        HashSet<string> paths = new(StringComparer.Ordinal);

        foreach (string relativePath in SettingsFileRelativePaths)
        {
            foreach (string path in Flatten(LoadSettings(relativePath)))
            {
                paths.Add(path);
            }
        }

        return paths;
    }

    /// <summary>Reads and parses one settings file.</summary>
    /// <param name="relativePath">Path relative to the services directory.</param>
    /// <returns>The parsed root object.</returns>
    private static JsonObject LoadSettings(string relativePath)
    {
        string path = Path.Combine(
            RequireRepositoryRoot(),
            ServicesDirectoryName,
            relativePath.Replace('/', Path.DirectorySeparatorChar));

        if (!File.Exists(path))
        {
            throw FailException.ForFailure($"'{ServicesDirectoryName}/{relativePath}' does not exist.");
        }

        JsonNode? parsed;

        try
        {
            parsed = JsonNode.Parse(File.ReadAllText(path), documentOptions: SettingsDocumentOptions);
        }
        catch (JsonException failure)
        {
            throw FailException.ForFailure(
                $"'{ServicesDirectoryName}/{relativePath}' is not parseable as the host reads it: "
                    + failure.Message);
        }

        return parsed as JsonObject
            ?? throw FailException.ForFailure(
                $"'{ServicesDirectoryName}/{relativePath}' has no JSON object at its root.");
    }

    /// <summary>Locates the repository root by walking up from the test binary.</summary>
    /// <returns>The absolute repository-root path.</returns>
    /// <remarks>
    /// Both repository markers are required together with the orchestration directory, so the walk
    /// cannot latch onto a same-named directory elsewhere on the machine. The probe is read-only.
    /// </remarks>
    private static string RequireRepositoryRoot()
    {
        DirectoryInfo? candidate = new(AppContext.BaseDirectory);

        while (candidate is not null)
        {
            if (Directory.Exists(Path.Combine(candidate.FullName, OrchestrationDirectoryName))
                && File.Exists(Path.Combine(candidate.FullName, SolutionMarkerFileName))
                && File.Exists(Path.Combine(candidate.FullName, PackageVersionMarkerFileName)))
            {
                return candidate.FullName;
            }

            // Parent is null at the filesystem root, which is what terminates the walk.
            candidate = candidate.Parent;
        }

        throw FailException.ForFailure(
            $"No directory from '{AppContext.BaseDirectory}' up to the filesystem root holds "
                + $"'{OrchestrationDirectoryName}' together with both repository markers "
                + $"'{SolutionMarkerFileName}' and '{PackageVersionMarkerFileName}'.");
    }

    /// <summary>Resolves a configuration key path against a parsed settings object.</summary>
    /// <param name="settings">The parsed root.</param>
    /// <param name="keyPath">The colon-separated path.</param>
    /// <returns>The node, or <see langword="null"/> when the path is not declared.</returns>
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

    /// <summary>Enumerates a settings object as leaf paths, treating an array as a single leaf.</summary>
    /// <param name="owner">The object to flatten.</param>
    /// <param name="prefix">Path prefix, empty for the root.</param>
    /// <returns>One entry per leaf.</returns>
    private static IEnumerable<string> Flatten(JsonObject owner, string prefix = "")
    {
        foreach ((string name, JsonNode? value) in owner)
        {
            string path = prefix.Length == 0 ? name : $"{prefix}:{name}";

            if (value is JsonObject nested)
            {
                foreach (string nestedPath in Flatten(nested, path))
                {
                    yield return nestedPath;
                }

                continue;
            }

            yield return path;
        }
    }

    /// <summary>Renders a failure list as one diagnostic.</summary>
    /// <param name="subject">What the failures are about.</param>
    /// <param name="failures">The collected failures.</param>
    /// <returns>A single message naming every failure.</returns>
    private static string Describe(string subject, IReadOnlyList<string> failures) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"orchestration/.env.example has {failures.Count} {subject}: {string.Join(" | ", failures)}");

    /// <summary>Renders a name list for a diagnostic.</summary>
    /// <param name="names">The names to render.</param>
    /// <returns>A readable list, or the word none.</returns>
    private static string Join(IReadOnlyCollection<string> names) =>
        names.Count == 0 ? "none" : string.Join(", ", names);

    /// <summary>One address variable and the listener it must resolve to.</summary>
    /// <param name="VariableName">The variable.</param>
    /// <param name="Port">The port the map assigns its target.</param>
    /// <param name="Scheme">The scheme its target serves.</param>
    private sealed record AddressExpectation(string VariableName, int Port, string Scheme);

    /// <summary>One variable that restates a service's authored default.</summary>
    /// <param name="VariableName">The variable.</param>
    /// <param name="SettingsRelativePath">The settings file holding the default.</param>
    /// <param name="KeyPath">The configuration key path of the default.</param>
    private sealed record PreservedDefault(
        string VariableName,
        string SettingsRelativePath,
        string KeyPath);
}
