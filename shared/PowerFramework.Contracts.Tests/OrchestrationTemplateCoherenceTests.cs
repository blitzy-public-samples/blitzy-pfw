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
using System.Text.RegularExpressions;
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

    /// <summary>The complete expected variable roster - twenty-eight names, no twenty-ninth.</summary>
    /// <remarks>
    /// <para>
    /// Spelled out rather than derived, so that both an addition and a removal are findings. The
    /// template's parity rule runs in both directions: a variable the manifest references but the
    /// template omits makes bring-up fail or silently resolve empty, and a variable the template
    /// declares that nothing references is dead configuration.
    /// </para>
    /// <para>
    /// The roster is four families, and reading it as one flat list hides why the count is what it is.
    /// TWELVE are the service-shape variables an operator always sets: the environment name, the three
    /// in-network REST addresses, the TWO gRPC addresses, the two token-topology names, the signing
    /// secret, and the three Persistence storage and pool settings.
    /// </para>
    /// <para>
    /// THE TWO <c>_GRPC_URL</c> VARIABLES ARE WHY THE COUNT MOVED FROM TWENTY-TWO, and they are not a
    /// duplication of the <c>_BASE_URL</c> pair. Persistence and DataServices each declare TWO endpoints,
    /// one per PROTOCOL VERSION: one <c>Http1</c> endpoint on the documented port for <c>/health</c>,
    /// <c>/v1/ping</c> and REST, and one <c>Http2</c> endpoint for the gRPC contracts. Both are
    /// <c>https</c>, so ALPN could have carried both on one address - the split is kept because a caller
    /// that dials the wrong one then fails at first use rather than reaching the wrong surface and being
    /// answered: a gRPC channel against the <c>Http1</c> endpoint fails negotiation, and an HTTP/1.1 probe
    /// against the <c>Http2</c> endpoint answers 400. Each is therefore named for what it carries rather
    /// than sharing one variable.
    /// </para>
    /// <para>
    /// TWO are the shared server-certificate paths, which are deliberately ONE pair for the whole stack
    /// rather than one pair per listener. FIVE are CLIENT-side mutual-TLS material, which is a different
    /// thing from the server pair and cannot be folded onto it: the client certificate Gateway presents
    /// to Security, the one DataServices presents to Security, and the issuer whose client certificates
    /// Security is willing to accept. All seven are empty in the TEMPLATE rather than optional in effect:
    /// every listener in this estate is <c>https</c>, so the server pair has to be supplied for the
    /// documented bring-up to start at all, and an example path is indistinguishable from a real one to a
    /// reader. The five client-side paths are genuinely optional, because JWT is the standing mechanism
    /// and mutual TLS is the documented per-pair fallback.
    /// </para>
    /// <para>
    /// THREE are Gateway's health-probe addresses, which stay separate from its upstream call addresses
    /// because one is what a caller dials and the other is what an observer reads - and because C-10
    /// bounds the aggregate at three upstreams while Gateway's call roster has only two. Their values
    /// additionally differ from the call addresses, since a probe is an HTTP/1.1 <c>GET</c> and must
    /// therefore reach the <c>Http1</c> endpoint rather than the <c>Http2</c> one.
    /// </para>
    /// <para>
    /// GATEWAY_LOCALE and GATEWAY_CAPABILITY_FLAGS are the last two, and they are the ones that restate a
    /// preserved behavioural default rather than configuring anything new; the pairing below holds them to
    /// the value the service itself declares.
    /// </para>
    /// <para>
    /// THE LAST SIX ARE THE AUTHORIZATION FAMILY, and they are the only entries in the roster that are
    /// not flat screaming-snake names. SECURITY_MTLS_CLIENT_REVOCATION_MODE belongs with the client-side
    /// material above - it says how thoroughly a presented client certificate's revocation is checked, and
    /// it is separate from the anchor because a deployment can trust an issuer without being able to reach
    /// its revocation data. The other five are the end-to-end suite's row in Security's issuance
    /// allowlist, spelled as SECTION-PATH variables because they address one element of an array of
    /// objects and no flat name can express that shape.
    /// </para>
    /// <para>
    /// THE INDEX 3 IS LOAD-BEARING AND MUST NOT BE CHANGED TO 0, 1 OR 2. Those three indices are authored
    /// in the settings file, and the environment provider MERGES by index rather than replacing or
    /// appending: naming an occupied index does not add a row and does not override the row either, it
    /// produces a silently HYBRID row carrying the environment's value for the members it names and the
    /// settings file's values for the rest, leaving the row count unchanged. That hybrid is not a
    /// duplicate, so the validator's duplicate-pair guard does not fire on it either. 3 is the first free
    /// index, which is why it is the one that appends a genuine fourth row.
    /// </para>
    /// </remarks>
    private static readonly string[] ExpectedVariableNames =
    [
        "ASPNETCORE_ENVIRONMENT",
        "DATASERVICES_BASE_URL",
        "DATASERVICES_GRPC_URL",
        "DATASERVICES_MTLS_CERT_PATH",
        "DATASERVICES_MTLS_KEY_PATH",
        "GATEWAY_CAPABILITY_FLAGS",
        "GATEWAY_HEALTH_PROBE_DATASERVICES_URL",
        "GATEWAY_HEALTH_PROBE_PERSISTENCE_URL",
        "GATEWAY_HEALTH_PROBE_SECURITY_URL",
        "GATEWAY_LOCALE",
        "GATEWAY_MTLS_CERT_PATH",
        "GATEWAY_MTLS_KEY_PATH",

        // The trust anchor every internal client verifies its peer against. Three consumers, one
        // variable: Gateway__InternalTls__TrustedCaPath, DataServices__InternalTls__TrustedCaPath and
        // Persistence's unprefixed InternalTls__TrustedCaPath. Its absence was the defect behind an
        // entire class of unreachable-upstream failure - the documented topology issues every internal
        // certificate from a LOCAL authority that no container's OS trust store carries, so without an
        // anchor named to each service every internal channel refuses the certificate it is presented.
        "INTERNAL_TLS_TRUSTED_CA_PATH",

        "PERSISTENCE_BASE_URL",
        "PERSISTENCE_GRPC_URL",
        "PERSISTENCE_SQLITE_DATA_DIRECTORY",
        "PERSISTENCE_TRANSPOOL_KEEPALIVE",
        "PERSISTENCE_TRANSPOOL_KEEPALIVE_EXPIRE_SECONDS",
        "SECURITY_BASE_URL",
        "SECURITY_JWT_AUDIENCE",
        "SECURITY_JWT_ISSUER",
        "SECURITY_JWT_SIGNING_KEY",

        // THE ISSUANCE-ROSTER SECRETS - one per caller that may obtain a token. Required, and a missing
        // one REFUSES THE HOST: Security resolves every secret its roster names at startup and reports
        // the roster position of any that resolves to nothing. Three rather than five, because
        // Persistence requests no token at all (it reads the published key set anonymously) and the third
        // is the operator/end-to-end identity the Development overlay registers.
        "SECURITY_CLIENT_SECRET_GATEWAY",
        "SECURITY_CLIENT_SECRET_DATASERVICES",
        "SECURITY_CLIENT_SECRET",

        "SECURITY_MTLS_CLIENT_CA_PATH",
        "SECURITY_MTLS_CLIENT_REVOCATION_MODE",

        // The end-to-end suite's row in the issuance allowlist. Index 3 appends a fourth row to the three
        // the settings file authors; see the remarks above for why an occupied index would silently
        // produce a hybrid row instead.
        "Security__CallerAuthorizations__3__Audience",
        "Security__CallerAuthorizations__3__Caller",
        "Security__CallerAuthorizations__3__Scopes__0",
        "Security__CallerAuthorizations__3__Scopes__1",
        "Security__CallerAuthorizations__3__Scopes__2",

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

        // THE FIVE CLIENT-SIDE MUTUAL-TLS PATHS BELONG HERE FOR A SECOND REASON BESIDES SECRECY, and it
        // is the sharper of the two: Gateway and DataServices load their pair EAGERLY at startup with
        // X509Certificate2.CreateFromPemFile and turn an IOException into a fail-fast host failure. A
        // pre-filled mount point that no manifest provides therefore stops the documented bring-up dead,
        // while reading as configured to anyone auditing the template. Both-empty is the supported state
        // meaning "this deployment presents no client certificate", which is the estate's default because
        // JWT is the standing mechanism and mutual TLS is the documented per-pair fallback - a deployment
        // that wants it mounts the pair and fills these in.
        "SECURITY_MTLS_CLIENT_CA_PATH",
        "GATEWAY_MTLS_CERT_PATH",
        "GATEWAY_MTLS_KEY_PATH",
        "DATASERVICES_MTLS_CERT_PATH",
        "DATASERVICES_MTLS_KEY_PATH",
    ];

    /// <summary>
    /// Configuration keys that legitimately have no settings-file declaration, with the reason.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each is environment-only BY DESIGN: the two certificate paths and the two secrets must never
    /// appear in an authored settings file (C-F), and the signing key additionally binds as a FLAT key
    /// because <c>__</c> is the section separator and <c>SECURITY_JWT_</c> would otherwise be read as
    /// three nested sections.
    /// </para>
    /// <para>
    /// <c>Jwt:MetadataAddress</c> is environment-only for a different reason, and it is a LIVE key
    /// rather than an indulgence: Persistence reads it straight off its bearer section and, when it is
    /// non-blank, assigns it to the handler
    /// (<c>services/persistence-service/PowerFramework.Persistence/Program.cs:376</c> and <c>:391</c>).
    /// Declaring it in the settings file would mean giving it a VALUE, and any value at all overrides
    /// the discovery address - so the only way to express "left unset so discovery resolves from the
    /// authority", which is the documented default, is to omit it. It is the same shape as
    /// <c>Sqlite:Password</c>: real, read, and deliberately absent from every authored file.
    /// </para>
    /// </remarks>
    private static readonly string[] EnvironmentOnlyConfigurationKeys =
    [
        "Kestrel:Certificates:Default:Path",
        "Kestrel:Certificates:Default:KeyPath",
        "Sqlite:Password",
        "Jwt:MetadataAddress",
        "SECURITY_JWT_SIGNING_KEY",
    ];

    /// <summary>
    /// Key spellings the template mentions only in order to FORBID them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The template tells a reader not to rename the flat signing-key variable into a section path, and
    /// naming the wrong spelling is how that instruction is made actionable. Without this allowance the
    /// resolvability check below would report the prohibition itself as the defect it exists to prevent.
    /// </para>
    /// <para>
    /// <c>Jwt__JwksPath</c> is here for the neighbouring reason: it is named in order to record that it
    /// USED to exist on Persistence, was read by nothing, and has been REMOVED. Documenting a withdrawn
    /// key is worth more than deleting the sentence - an operator carrying it forward from an older
    /// template needs to be told it is gone rather than left wondering why it has no effect - and the
    /// resolvability check would otherwise report that explanation as an unresolvable key path, which is
    /// the same category error as reporting a prohibition.
    /// </para>
    /// </remarks>
    private static readonly string[] DocumentedOnlyToBeForbidden =
        ["Security__SigningKey", "Jwt__JwksPath"];

    /// <summary>Every port the map assigns, as it may appear inside a template VALUE.</summary>
    /// <remarks>
    /// <para>
    /// 5103 is absent deliberately: the map reserves it as a commented Phase-2 slot, so a value naming it
    /// would be an address for a service that does not exist.
    /// </para>
    /// <para>
    /// 5111 and 5112 are the gRPC endpoints of Persistence and DataServices. They sit OUTSIDE the
    /// documented 5101-5105 band on purpose: the attached environment fixes <c>/health</c> addresses on
    /// that band and documents no gRPC address at all, so an address it never named can be added without
    /// moving one it did. Every documented port keeps exactly the meaning it was given.
    /// </para>
    /// </remarks>
    private static readonly int[] AssignedPorts = [5101, 5102, 5104, 5105, 5111, 5112];

    /// <summary>Each address variable, the port it must name, and the scheme it must use.</summary>
    /// <remarks>
    /// EVERY SCHEME IS <c>https</c>, AND THAT IS ASSERTED RATHER THAN TOLERATED. The environment gates
    /// each service's readiness on <c>curl -sf http://localhost:&lt;port&gt;/health</c>, which fixes the
    /// probe SHAPE - an anonymous <c>GET</c> of <c>/health</c> on that port answering 200 - and not the
    /// transport beneath it, so honouring it with a trust anchor added is not one of the deviations AAP
    /// 0.8.3 governs. What a template naming an <c>http</c> listener WOULD describe is a stack carrying
    /// bearer credentials and a verification key set in cleartext, which is CWE-319 on surfaces the
    /// decomposition itself created (AAP 0.1.4). The issuer is held to the same scheme as the base address
    /// because the two are compared byte for byte against the <c>iss</c> claim: a one-character divergence
    /// rejects every token in the system, silently, until the first authenticated request.
    /// </remarks>
    private static readonly AddressExpectation[] AddressExpectations =
    [
        new("PERSISTENCE_BASE_URL", 5101, Uri.UriSchemeHttps),
        new("PERSISTENCE_GRPC_URL", 5111, Uri.UriSchemeHttps),
        new("DATASERVICES_BASE_URL", 5102, Uri.UriSchemeHttps),
        new("DATASERVICES_GRPC_URL", 5112, Uri.UriSchemeHttps),
        new("SECURITY_BASE_URL", 5104, Uri.UriSchemeHttps),
        new("SECURITY_JWT_ISSUER", 5104, Uri.UriSchemeHttps),
        new("GATEWAY_HEALTH_PROBE_PERSISTENCE_URL", 5101, Uri.UriSchemeHttps),
        new("GATEWAY_HEALTH_PROBE_DATASERVICES_URL", 5102, Uri.UriSchemeHttps),
        new("GATEWAY_HEALTH_PROBE_SECURITY_URL", 5104, Uri.UriSchemeHttps),
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

        // ------------------------------------------------------------------------------------------
        // `openssl rand` MUST NOT BE OFFERED FOR THE SIGNING KEY, AND MUST BE OFFERED FOR THE ROSTER
        // SECRETS. The assertion is therefore per line rather than over the whole file.
        //
        // The distinction is real and getting it wrong costs a bring-up either way. The signing key is
        // an ASYMMETRIC PRIVATE KEY that Security imports: random bytes cannot be imported at all, so
        // an operator who reached for `openssl rand` gets a host that refuses to start. The issuance
        // roster's secrets are SHARED SECRETS compared byte for byte and imported by nothing, so
        // random bytes are exactly right - and an operator told nothing about how to produce one
        // invents something weaker.
        //
        // An earlier form of this row banned the string outright, which was correct while the signing
        // key was the only secret in the template and became wrong the moment the roster arrived. Per
        // line keeps the original intent - no random-bytes recipe anywhere near the signing key - while
        // admitting the recipe the roster needs.
        // ------------------------------------------------------------------------------------------
        string[] offendingLines =
        [
            .. text.Split('\n')
                .Where(static line =>
                    line.Contains("openssl rand", StringComparison.Ordinal)
                    && line.Contains("SIGNING", StringComparison.Ordinal)),
        ];

        Assert.True(
            offendingLines.Length == 0,
            "The template offers a random-bytes recipe on a line naming the signing key. Random bytes "
                + "cannot be imported as an asymmetric private key, so an operator who followed it "
                + "would get a host that refuses to start.");

        Assert.True(
            text.Contains("openssl rand -base64 32", StringComparison.Ordinal),
            "The template documents no way to generate an issuance-roster secret. Every roster entry "
                + "that names a secret key must have one supplied or the host refuses to start, so an "
                + "operator told nothing here either cannot bring the stack up or invents something "
                + "weaker.");

        // AND THE WARNING AGAINST CONFUSING THE TWO IS STILL PRESENT, which is the part a reader acts on
        // when a startup refusal names the signing key.
        Assert.True(
            text.Contains("fails the startup usability check", StringComparison.Ordinal),
            "The template no longer states that random material is rejected for the signing key at "
                + "startup. That sentence is what turns a refusal an operator has already hit into an "
                + "action.");
    }

    /// <summary>
    /// No variable exists for an absolute JWK-set address, because no verifier can read one.
    /// </summary>
    /// <remarks>
    /// All three verifiers reach the key set through the discovery document beneath their bearer
    /// authority and expose no key-set setting of their own. Persistence once declared a relative
    /// <c>Jwt:JwksPath</c>, but nothing read it and it has been removed, so an absolute URL has nowhere
    /// to go in any of the three shapes.
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
        HashSet<string> shapes = ReadDeclaredShapesThroughArrays();
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

            // An indexed reference addresses an ELEMENT, while the settings file authors a SHAPE, so the
            // two are compared with indices normalized away at every depth. This resolves both array
            // kinds - a scalar array such as Security:Audiences:0, and an array of objects such as
            // Security:CallerAuthorizations:3:Caller - while still holding the member name after the
            // index to the shape the element actually declares.
            if (shapes.Contains(NormalizeAwayIndexes(path)))
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

    /// <summary>
    /// The end-to-end suite's compiled-in fallback for each address variable names the same listener the
    /// template does — same scheme, same port.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHY THIS CHECK IS HERE, IN A .NET TEST, READING TYPESCRIPT. There are two places a default address
    /// for these services is written: the orchestration template, which an operator fills in, and
    /// <c>tests/e2e/fixtures/service-endpoints.ts</c>, whose <c>resolveBaseUrl</c> fallbacks are what the
    /// suite uses when no variable is set — which is the documented local run. Nothing else compares them.
    /// The suite's own type check cannot: a string literal is a valid string whatever it names, and the
    /// TypeScript has no access to the settings files that decide which listeners exist.
    /// </para>
    /// <para>
    /// AND THE TWO HAD DIVERGED, WHICH IS WHY IT IS WORTH A TEST RATHER THAN A CONVENTION. The fixture
    /// defaulted Security to an <c>http</c> address while Security declares exactly one listener and it
    /// terminates TLS — so the documented default run could not bootstrap at all: a client certificate is
    /// the only caller authentication <c>POST /v1/tokens</c> accepts, and a certificate cannot be
    /// presented where no handshake happens. Seventy lines of commentary directly above the literal argued
    /// correctly for <c>https</c>; the literal said otherwise, and no gate read either.
    /// </para>
    /// <para>
    /// SCHEME AND PORT ONLY — the HOST is deliberately not compared. The template names container DNS
    /// (<c>security-service</c>) because Compose resolves it; the fixture names <c>localhost</c> because a
    /// developer runs it from the host. Those are the same listener reached from two places, so requiring
    /// them to match would encode a topology assumption rather than a coherence rule. What must agree is
    /// what an operator cannot override away: whether TLS is spoken, and which port answers.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(AllAddressExpectations))]
    public void EndToEndFallbackAddressNamesTheSameListenerAsTheTemplate(
        string variableName,
        int expectedPort,
        string expectedScheme)
    {
        string? fallback = ReadEndToEndFallback(variableName);

        if (fallback is null)
        {
            // Not every template variable is an address the suite resolves - SECURITY_JWT_ISSUER is
            // Security's own identity rather than an address a spec calls - so absence is not a failure.
            // What would be a failure is a fallback that exists and names a different listener.
            return;
        }

        Assert.True(
            Uri.TryCreate(fallback, UriKind.Absolute, out Uri? address),
            $"The end-to-end fallback for '{variableName}' is not an absolute address.");

        Assert.Equal(expectedScheme, address!.Scheme);

        Assert.True(
            address.Port == expectedPort,
            $"The end-to-end fallback for '{variableName}' names port {address.Port}, but the map assigns "
                + $"{expectedPort}. The documented run uses this fallback, so a wrong port here fails the "
                + "suite before any assertion in it is reached.");
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

    /// <summary>
    /// Removes every all-digit segment from a configuration path, yielding the SHAPE the path
    /// addresses rather than the particular element it addresses.
    /// </summary>
    /// <param name="path">The colon-separated path.</param>
    /// <returns>The path with all index segments removed.</returns>
    /// <remarks>
    /// <para>
    /// The earlier form of this helper removed only a TRAILING index, which resolved an array of scalars
    /// - <c>Security:Audiences:0</c> reduces to the one authored leaf <c>Security:Audiences</c> - but
    /// could not resolve an array of OBJECTS, where the index sits in the middle. Security's issuance
    /// allowlist is exactly that shape, so every one of its documented key paths read as unresolvable
    /// while being perfectly correct.
    /// </para>
    /// <para>
    /// Removing indices at ANY depth is strictly STRONGER than truncating at the array, which is the
    /// other way this could have been fixed. Truncating would have accepted anything at all after the
    /// index - <c>CallerAuthorizations:3:Typo</c> included - because the array's own existence would have
    /// been the whole test. Normalizing both sides instead compares the member name against the element
    /// shape the settings file actually authors, so a misspelled member inside an element is still a
    /// finding. Indices are DROPPED rather than preserved because one element's shape describes every
    /// element's: an environment file may legitimately address an index the settings file never authors,
    /// which is precisely how a row gets appended.
    /// </para>
    /// </remarks>
    private static string NormalizeAwayIndexes(string path) =>
        string.Join(
            ':',
            path.Split(':')
                .Where(static segment =>
                    segment.Length == 0 || !segment.All(char.IsAsciiDigit)));

    /// <summary>
    /// Reads every declared path, descending THROUGH arrays so that the members of an array element
    /// are declared shapes too, with index segments normalized away.
    /// </summary>
    /// <returns>The normalized declared shapes.</returns>
    /// <remarks>
    /// <see cref="Flatten"/> stops at an array because an array is one authored leaf, and the
    /// exact-roster assertions depend on it continuing to do so. This is the additional, wider view used
    /// only for resolving a documented key path, so the two coexist rather than one replacing the other.
    /// </remarks>
    private static HashSet<string> ReadDeclaredShapesThroughArrays()
    {
        HashSet<string> shapes = new(StringComparer.Ordinal);

        foreach (string relativePath in SettingsFileRelativePaths)
        {
            foreach (string path in FlattenThroughArrays(LoadSettings(relativePath), prefix: string.Empty))
            {
                shapes.Add(NormalizeAwayIndexes(path));
            }
        }

        return shapes;
    }

    /// <summary>Enumerates every path beneath a node, descending into arrays as well as objects.</summary>
    /// <param name="node">The node to walk.</param>
    /// <param name="prefix">The path accumulated so far.</param>
    /// <returns>Every path the node contributes.</returns>
    private static IEnumerable<string> FlattenThroughArrays(JsonNode? node, string prefix)
    {
        if (node is JsonObject owner)
        {
            foreach ((string name, JsonNode? value) in owner)
            {
                string path = prefix.Length == 0 ? name : $"{prefix}:{name}";

                // An array is reported as a leaf in its own right - matching Flatten, so that a
                // reference to the array itself still resolves - AND then descended into.
                if (value is not JsonObject)
                {
                    yield return path;
                }

                foreach (string nested in FlattenThroughArrays(value, path))
                {
                    yield return nested;
                }
            }

            yield break;
        }

        if (node is JsonArray elements)
        {
            foreach (JsonNode? element in elements)
            {
                // The index contributes nothing: the prefix is carried through unchanged so that every
                // element describes the same shape.
                foreach (string nested in FlattenThroughArrays(element, prefix))
                {
                    yield return nested;
                }
            }
        }
    }

    /// <summary>Reads the template as lines, with line endings normalized away.</summary>
    /// <returns>The template's lines.</returns>
    private static string[] ReadTemplateLines() =>
        ReadTemplateText().Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

    /// <summary>
    /// Reads the compiled-in fallback the end-to-end suite uses for one address variable.
    /// </summary>
    /// <param name="variableName">The variable name, as both artifacts spell it.</param>
    /// <returns>
    /// The fallback literal, or <see langword="null"/> when the fixture resolves no such variable.
    /// </returns>
    /// <remarks>
    /// <para>
    /// A TARGETED TEXT MATCH RATHER THAN A PARSE, and the shape it matches is the one the fixture actually
    /// uses: <c>resolveBaseUrl('NAME', 'value')</c>, whether written on one line or across three. Parsing
    /// TypeScript from a .NET test would need a parser this repository does not have and must not acquire
    /// for a two-argument call.
    /// </para>
    /// <para>
    /// A MISSING FIXTURE IS A FAILURE, NOT A SKIP. The suite is committed, so a check that quietly passed
    /// when the file moved would be worth nothing — the one state it exists to catch is the fixture and the
    /// template disagreeing, and a renamed fixture is indistinguishable from that until somebody looks.
    /// </para>
    /// </remarks>
    private static string? ReadEndToEndFallback(string variableName)
    {
        string path = Path.Combine(
            RequireRepositoryRoot(),
            "tests",
            "e2e",
            "fixtures",
            "service-endpoints.ts");

        string text = File.Exists(path)
            ? File.ReadAllText(path)
            : throw FailException.ForFailure(
                "'tests/e2e/fixtures/service-endpoints.ts' does not exist. It carries the addresses the "
                    + "documented end-to-end run uses when no variable is set, so its absence is a finding "
                    + "rather than a reason to skip a check.");

        Match match = Regex.Match(
            text,
            @"resolveBaseUrl\(\s*'" + Regex.Escape(variableName) + @"'\s*,\s*'(?<fallback>[^']+)'",
            RegexOptions.None,
            TimeSpan.FromSeconds(5));

        return match.Success ? match.Groups["fallback"].Value : null;
    }

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
