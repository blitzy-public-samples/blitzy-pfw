// --------------------------------------------------------------------------------------------------
//  OperationalTopologyCoherenceTests - the metadata surfaces have to agree with the running topology
// --------------------------------------------------------------------------------------------------
//
//  WHAT THIS FILE IS FOR, AND WHY IT IS NOT A DUPLICATE OF ServiceConfigurationCoherenceTests
//    That sibling pins the EXECUTABLE topology: it reads each service's appsettings.json and asserts the
//    endpoints, ports, schemes and protocol versions the host will actually bind. What it cannot see is
//    everything that only DESCRIBES that topology - the port tables in docs/BUILD.md and
//    docs/ARCHITECTURE.md, the EXPOSE lines in the four container definitions, and the header map in the
//    end-to-end suite's addressing fixture. Those are the surfaces an operator configures a deployment
//    against, and nothing in this repository noticed when one of them drifted.
//
//    Drift there is not cosmetic. Three concrete failures were live in this tree before this file existed:
//
//      * docs/ARCHITECTURE.md described Security's single listener as `Http1AndHttp2` in two places while
//        appsettings.json declares `Http1`. An operator collapsing a topology on the strength of that
//        sentence would expect a gRPC channel to negotiate on 5104, where nothing serves one.
//      * services/persistence-service/Dockerfile declared `EXPOSE 5101` while the service binds 5101 AND
//        5111. The gRPC endpoint the C-05..C-08 contracts live on was undocumented at the image boundary -
//        which is the half of the review finding that says "expose all required container ports".
//      * tests/e2e/fixtures/service-endpoints.ts printed `http, Http1` for three ports whose exported
//        defaults in the same file are `https`, and described Security as `Http1AndHttp2`.
//
//    Each assertion below is the one that would have failed on those, so the file discriminates rather
//    than merely passing.
//
//  A SECOND CLASS OF DRIFT: CURRENT-STATE CLAIMS THAT ARE NO LONGER TRUE
//    Active documentation in this tree stated that `services/persistence-service/Dockerfile` "does not
//    exist", "is absent" and "is still to be written" in five separate files, long after it was authored.
//    A false absence claim is worse than a stale one: it is read as a reason NOT to try something that
//    works, and it silently transfers into every document written from it. The absence assertions below
//    close that class by refusing any absence claim about a container definition that is present, while
//    deliberately leaving TRUE absence claims - `orchestration/docker-compose.yml`, `characterization/`
//    - untouched, because those are honest and load-bearing.
//
//  WHAT IT DOES NOT ASSERT
//    Nothing about runtime. No container is started, no socket is opened, no image is built. Every
//    assertion here is a comparison between two files in the tree, which is the only kind of claim this
//    project is in a position to make (AAP 0.6.7 records that Docker was unavailable where the migration
//    was planned, and BUILD.md 1.3 records the one image that has been built and run since).
//
//    Nothing about performance, latency or throughput, here or anywhere: the repository publishes no such
//    budget (AAP 0.8.5), so none is asserted.
//
//  THE READ-ONLY LEGACY TREE IS NOT TOUCHED (constraint C-C). Every path read below is a .NET-side
//  artifact authored by this refactor. ws_objects/** is neither read nor written by this file.
//
//  SOURCE OF TRUTH ORDER, STATED SO A FAILURE IS ACTIONABLE
//    appsettings.json is the authority in every assertion here, because it is the file the host binds.
//    When one of these tests fails, the fix is to correct the DESCRIBING surface named in the failure
//    message - not to edit the settings file to match a document.
// --------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

using Xunit;
using Xunit.Sdk;

namespace PowerFramework.Contracts.Tests;

/// <summary>
/// Asserts that every surface DESCRIBING the deployed topology - the documentation port tables, the
/// container definitions' exposed ports and the end-to-end suite's addressing fixture - agrees with the
/// Kestrel endpoints the services actually bind, and that no active surface claims a container definition
/// is absent while it is present in the tree.
/// </summary>
public sealed class OperationalTopologyCoherenceTests
{
    /// <summary>The repository-root marker files, both required so a partial match cannot pass.</summary>
    private const string SolutionMarkerFileName = "PowerFramework.slnx";

    /// <summary>The second repository-root marker.</summary>
    private const string PackageVersionMarkerFileName = "Directory.Packages.props";

    /// <summary>The settings file the host binds in every environment.</summary>
    private const string BaseSettingsFileName = "appsettings.json";

    /// <summary>
    /// The four services, each with the directory and project that hold its container definition and its
    /// settings file. Ports are deliberately NOT recorded here: they are read from the settings file, so
    /// this table cannot become a second, divergent statement of the map.
    /// </summary>
    private static readonly (string Key, string DirectoryName, string ProjectName)[] Services =
    [
        ("persistence", "persistence-service", "PowerFramework.Persistence"),
        ("dataservices", "dataservices-service", "PowerFramework.DataServices"),
        ("security", "security-service", "PowerFramework.Security"),
        ("gateway", "gateway-service", "PowerFramework.Gateway"),
    ];

    /// <summary>
    /// The surfaces that DESCRIBE the topology and are read by a human configuring a deployment. Every
    /// one is repository-root-relative and every one is authored by this refactor.
    /// </summary>
    private static readonly string[] DescribingSurfaces =
    [
        "docs/BUILD.md",
        "docs/ARCHITECTURE.md",
        "docs/CONTRACTS.md",
        "docs/PARITY.md",
        "docs/SECRETS.md",
        "orchestration/.env.example",
        "tests/e2e/README.md",
        "tests/e2e/fixtures/service-endpoints.ts",
        "tests/e2e/specs/05-datawindow-workflow.spec.ts",
        "services/persistence-service/Dockerfile",
        "services/dataservices-service/Dockerfile",
        "services/security-service/Dockerfile",
        "services/gateway-service/Dockerfile",
    ];

    /// <summary>
    /// The listener form both port tables use: a backticked bind address followed by a backticked
    /// protocol token. Matching the punctuation is what keeps this from catching prose that merely
    /// mentions a port and a protocol in the same sentence.
    /// </summary>
    private static readonly Regex DocumentedListenerPattern = new(
        @"`(?<scheme>https?)://\+:(?<port>\d{4})`,\s*`(?<protocol>Http[A-Za-z0-9]*)`",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        TimeSpan.FromSeconds(5));

    /// <summary>
    /// The end-to-end fixture's header map: a port, a service, a project, then the scheme and protocol.
    /// </summary>
    private static readonly Regex FixturePortMapPattern = new(
        @"^\s*\*\s+(?<port>\d{4})\s+(?<service>\w+)\s+(?<project>\S+)\s+"
            + @"(?<scheme>https?),\s*(?<protocol>Http[A-Za-z0-9]*)\b",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture | RegexOptions.Multiline,
        TimeSpan.FromSeconds(5));

    /// <summary>A Dockerfile <c>EXPOSE</c> instruction and every port on it.</summary>
    private static readonly Regex ExposePattern = new(
        @"^EXPOSE\s+(?<ports>[\d\s]+)$",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture | RegexOptions.Multiline,
        TimeSpan.FromSeconds(5));

    /// <summary>
    /// Phrases that assert absence. Each is matched inside a short window around a named container
    /// definition, so the claim and its subject have to be in the same breath.
    /// </summary>
    /// <remarks>
    /// Deliberately narrow. A broad marker such as a bare negation would catch "is not built here",
    /// which is TRUE of three of the four images and must stay sayable - what is forbidden is a claim
    /// that the DEFINITION is missing.
    /// </remarks>
    private static readonly string[] AbsenceMarkers =
    [
        "does not exist",
        "does NOT exist",
        "do not exist",
        "is absent",
        "are absent",
        "absent too",
        "still to be written",
        "has not been authored",
        "have not been authored",
        "is not authored",
        "Dockerfile` is not.",
        "is missing",
    ];

    /// <summary>
    /// Generic claims about the container definitions as a set that are no longer true of this tree.
    /// Checked over whole files, because none of them names a path a window could be built around.
    /// </summary>
    private static readonly string[] ForbiddenSetClaims =
    [
        "no service `Dockerfile`",
        "three of the four container definitions exist",
        "Three of the four `Dockerfile`s",
        "three of the four Dockerfiles",
    ];

    /// <summary>How many characters either side of a path an absence claim has to fall within.</summary>
    private const int AbsenceWindowRadius = 260;

    /// <summary>
    /// Artifact names that can be the SUBJECT of an absence claim in their own right. Used to decide
    /// which of several nearby paths a marker is actually about.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WITHOUT THIS, THE WINDOW ALONE PRODUCES FALSE POSITIVES, and two were measured. Both
    /// <c>docs/ARCHITECTURE.md</c> and <c>docs/SECRETS.md</c> carry a sentence of the shape
    /// "<c>services/security-service/Dockerfile</c> installs no trust anchor, ... and
    /// <c>orchestration/docker-compose.yml</c> does not exist" - one sentence, two subjects, and the
    /// absence claim belongs to the SECOND. A window test reads that as a false claim about the
    /// Dockerfile; a reader does not, because the nearer subject wins.
    /// </para>
    /// <para>
    /// So the rule is nearest-subject: a marker counts against a path only when no OTHER artifact name
    /// lies between the two. That keeps the true claims - about the Compose manifest and about
    /// <c>characterization/</c> - sayable in the same breath as a present Dockerfile, which is exactly
    /// how both documents need to say them.
    /// </para>
    /// </remarks>
    private static readonly string[] ClaimableArtifactNames =
    [
        "orchestration/docker-compose.yml",
        "orchestration/README.md",
        "characterization/",
        "tests/e2e/",
        ".github/workflows/ci.yml",
        "services/persistence-service/Dockerfile",
        "services/dataservices-service/Dockerfile",
        "services/security-service/Dockerfile",
        "services/gateway-service/Dockerfile",
    ];

    /// <summary>
    /// Every service key, for the theories that iterate all four.
    /// </summary>
    public static TheoryData<string> AllServiceKeys { get; } =
        new(Services.Select(static service => service.Key));

    /// <summary>
    /// Every describing surface, so a failure names one file rather than a list.
    /// </summary>
    public static TheoryData<string> AllDescribingSurfaces { get; } = new(DescribingSurfaces);

    // ----------------------------------------------------------------------------------------------
    // 1. The documented listener rows agree with the bound endpoints
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Asserts that every documented listener row - `scheme://+:port`, `Protocol` - names a port some
    /// service actually binds, on the scheme and protocol version that service binds it with.
    /// </summary>
    /// <param name="surfacePath">The describing surface under test.</param>
    /// <remarks>
    /// <para>
    /// THIS IS THE ASSERTION THAT WOULD HAVE CAUGHT THE `Http1AndHttp2` DRIFT. Security binds ONE
    /// endpoint with <c>Protocols: Http1</c>; two places in <c>docs/ARCHITECTURE.md</c> and one in the
    /// end-to-end addressing fixture described that same endpoint as <c>Http1AndHttp2</c>. Nothing
    /// failed, because no test compared a sentence with a settings file.
    /// </para>
    /// <para>
    /// A surface carrying no row at all passes vacuously and that is correct: not every document is
    /// obliged to restate the map. What no document may do is restate it WRONGLY.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(AllDescribingSurfaces))]
    public void EveryDocumentedListenerRowAgreesWithTheBoundKestrelEndpoint(string surfacePath)
    {
        IReadOnlyDictionary<int, BoundEndpoint> bound = LoadBoundEndpoints();
        string text = ReadRepositoryFile(surfacePath);

        foreach (Match match in DocumentedListenerPattern.Matches(text))
        {
            int port = int.Parse(match.Groups["port"].Value, CultureInfo.InvariantCulture);
            string scheme = match.Groups["scheme"].Value;
            string protocol = match.Groups["protocol"].Value;

            Assert.True(
                bound.TryGetValue(port, out BoundEndpoint? endpoint),
                $"'{surfacePath}' documents a listener on port {port}, but no service binds that port. "
                    + $"Bound ports are {DescribePorts(bound)}. The settings files are the authority; "
                    + "correct the document.");

            Assert.Equal(
                (endpoint!.Scheme, endpoint.Protocol),
                (scheme, protocol));
        }
    }

    /// <summary>
    /// Asserts that the port map in the end-to-end suite's addressing fixture header agrees, row for row,
    /// with the bound endpoints - and that it carries a row for every bound port.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE FIXTURE'S HEADER MAP IS THE ONE A SPEC AUTHOR READS, so a wrong scheme there is a wrong
    /// `fetch` in the next spec written. Before this test it printed <c>http</c> for 5101, 5111 and 5102
    /// while the very next paragraph in the same file said every listener terminates TLS, and while the
    /// module's own exported defaults were <c>https</c> - three statements in one file, one of them
    /// wrong.
    /// </para>
    /// <para>
    /// Completeness is asserted as well as agreement, because a row quietly dropped from that table is
    /// how the gRPC ports would stop being visible to the next reader.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheEndToEndFixturePortMapAgreesWithEveryBoundEndpoint()
    {
        IReadOnlyDictionary<int, BoundEndpoint> bound = LoadBoundEndpoints();
        string text = ReadRepositoryFile("tests/e2e/fixtures/service-endpoints.ts");

        Dictionary<int, (string Scheme, string Protocol)> documented = [];

        foreach (Match match in FixturePortMapPattern.Matches(text))
        {
            documented[int.Parse(match.Groups["port"].Value, CultureInfo.InvariantCulture)] =
                (match.Groups["scheme"].Value, match.Groups["protocol"].Value);
        }

        Assert.Equal(
            bound.Keys.OrderBy(static port => port),
            documented.Keys.OrderBy(static port => port));

        foreach ((int port, (string scheme, string protocol)) in documented.OrderBy(static row => row.Key))
        {
            BoundEndpoint endpoint = bound[port];

            Assert.Equal((endpoint.Scheme, endpoint.Protocol), (scheme, protocol));
        }
    }

    // ----------------------------------------------------------------------------------------------
    // 2. Every bound port is exposed by its own container definition
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Asserts that a service's container definition <c>EXPOSE</c>s exactly the ports that service binds -
    /// no fewer, so nothing it serves is invisible at the image boundary, and no more, so the definition
    /// does not advertise a surface that does not exist.
    /// </summary>
    /// <param name="serviceKey">The service under test.</param>
    /// <remarks>
    /// <para>
    /// THE REVIEW FINDING NAMED THIS HALF EXPLICITLY - "expose all required container ports". Persistence
    /// binds 5101 and 5111 and its definition declared only 5101, so the gRPC endpoint carrying contracts
    /// C-05 through C-08 was undeclared. <c>EXPOSE</c> creates nothing at run time, which is exactly why
    /// the omission was invisible in every build: it is documentation the tooling reads, and a reader or
    /// an orchestrator consulting it would have concluded the service serves one port.
    /// </para>
    /// <para>
    /// EQUALITY RATHER THAN CONTAINMENT, in both directions on purpose. An under-declaration hides a
    /// surface; an over-declaration invites a probe or a published mapping onto a port nothing answers,
    /// which fails in a way that looks like the service being down.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(AllServiceKeys))]
    public void EveryContainerDefinitionExposesEveryPortItsServiceBinds(string serviceKey)
    {
        (string _, string directoryName, string projectName) = RequireService(serviceKey);

        IReadOnlyList<int> boundPorts = ReadBoundPorts(directoryName, projectName);
        string dockerfile = ReadRepositoryFile($"services/{directoryName}/Dockerfile");

        List<int> exposed = [];

        foreach (Match match in ExposePattern.Matches(dockerfile))
        {
            exposed.AddRange(
                match.Groups["ports"].Value
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(static port => int.Parse(port, CultureInfo.InvariantCulture)));
        }

        Assert.Equal(boundPorts.OrderBy(static port => port), exposed.OrderBy(static port => port));
    }

    // ----------------------------------------------------------------------------------------------
    // 3. No active surface claims a container definition is absent while it exists
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Asserts that no describing surface makes an absence claim about a container definition that is
    /// present in the tree.
    /// </summary>
    /// <param name="surfacePath">The describing surface under test.</param>
    /// <remarks>
    /// <para>
    /// WHY A FALSE ABSENCE CLAIM IS THE WORST KIND OF STALENESS. It is not read as out of date, it is
    /// read as a reason not to attempt something that already works - and because the project's published
    /// documentation is written from these files, it propagates outward unchallenged. Five files in this
    /// tree said <c>services/persistence-service/Dockerfile</c> "does not exist", "is absent" or "is
    /// still to be written" after it had been authored, and three of them used that claim to justify a
    /// further conclusion about the bring-up.
    /// </para>
    /// <para>
    /// TRUE ABSENCE CLAIMS ARE DELIBERATELY UNAFFECTED, and there are two that matter:
    /// <c>orchestration/docker-compose.yml</c> genuinely does not exist and <c>characterization/</c>
    /// genuinely does not either. Both are load-bearing statements that keep a reader from running a
    /// command whose subject is missing, so the assertion is scoped to the container definitions rather
    /// than to absence claims in general.
    /// </para>
    /// <para>
    /// The window is what makes this precise: the marker has to appear within
    /// <see cref="AbsenceWindowRadius"/> characters of the path itself, so "the image has not been built"
    /// - true of three of the four - is not caught, while "this Dockerfile does not exist" is.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(AllDescribingSurfaces))]
    public void NoActiveSurfaceClaimsAContainerDefinitionIsAbsentWhileItExists(string surfacePath)
    {
        string text = ReadRepositoryFile(surfacePath);
        string root = RequireRepositoryRoot();

        foreach ((string _, string directoryName, string _) in Services)
        {
            string relativePath = $"services/{directoryName}/Dockerfile";

            if (!File.Exists(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar))))
            {
                // A genuinely absent definition may be described as absent. Nothing to assert.
                continue;
            }

            foreach (int index in IndexesOf(text, relativePath))
            {
                int start = Math.Max(0, index - AbsenceWindowRadius);
                int end = Math.Min(text.Length, index + relativePath.Length + AbsenceWindowRadius);

                foreach (string marker in AbsenceMarkers)
                {
                    foreach (int markerIndex in IndexesOf(text[start..end], marker))
                    {
                        Assert.False(
                            ClaimsAbsenceOf(text, index, relativePath, start + markerIndex, marker),
                            $"'{surfacePath}' claims '{relativePath}' {marker}, but that file is present "
                                + "in the tree. Reconcile the statement, or scope it to what is genuinely "
                                + $"absent. The claim was found here:{Environment.NewLine}"
                                + text[start..end]);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Asserts that no describing surface carries a claim about the container definitions AS A SET that
    /// is no longer true - the "three of the four" family.
    /// </summary>
    /// <param name="surfacePath">The describing surface under test.</param>
    /// <remarks>
    /// A closed list rather than a pattern, because each of these phrases was a real sentence in this
    /// tree and each carried a different conclusion off the back of it: that the images could not be
    /// fully assembled, that a reader could build three containers by hand, and that the definitions were
    /// partially authored. All four definitions exist, so all four phrasings are simply false now.
    /// </remarks>
    [Theory]
    [MemberData(nameof(AllDescribingSurfaces))]
    public void NoActiveSurfaceDescribesTheContainerDefinitionsAsPartiallyAuthored(string surfacePath)
    {
        string text = ReadRepositoryFile(surfacePath);

        foreach (string claim in ForbiddenSetClaims)
        {
            Assert.False(
                text.Contains(claim, StringComparison.OrdinalIgnoreCase),
                $"'{surfacePath}' contains '{claim}'. All four container definitions are present in the "
                    + "tree, so that statement is false; state what is actually missing instead.");
        }
    }

    // ----------------------------------------------------------------------------------------------
    // Helpers
    // ----------------------------------------------------------------------------------------------

    /// <summary>One bound Kestrel endpoint, reduced to the three things a document can get wrong.</summary>
    /// <param name="ServiceKey">The service that binds it.</param>
    /// <param name="Scheme">The url scheme, <c>http</c> or <c>https</c>.</param>
    /// <param name="Protocol">The protocol token, verbatim from the settings file.</param>
    private sealed record BoundEndpoint(string ServiceKey, string Scheme, string Protocol);

    /// <summary>
    /// Reads every service's settings file and returns the bound endpoints keyed by port.
    /// </summary>
    /// <exception cref="XunitException">
    /// When two services claim the same port, which would make the map ambiguous rather than merely
    /// wrong.
    /// </exception>
    private static IReadOnlyDictionary<int, BoundEndpoint> LoadBoundEndpoints()
    {
        Dictionary<int, BoundEndpoint> bound = [];

        foreach ((string key, string directoryName, string projectName) in Services)
        {
            foreach ((int port, string scheme, string protocol) in ReadEndpoints(directoryName, projectName))
            {
                Assert.False(
                    bound.ContainsKey(port),
                    $"Port {port} is bound by both '{bound.GetValueOrDefault(port)?.ServiceKey}' and "
                        + $"'{key}'. The port map allocates one port to one service.");

                bound[port] = new BoundEndpoint(key, scheme, protocol);
            }
        }

        Assert.NotEmpty(bound);

        return bound;
    }

    /// <summary>The ports one service binds, ascending.</summary>
    private static IReadOnlyList<int> ReadBoundPorts(string directoryName, string projectName) =>
        [.. ReadEndpoints(directoryName, projectName).Select(static endpoint => endpoint.Port).Order()];

    /// <summary>
    /// Reads <c>Kestrel:Endpoints</c> out of one service's base settings file.
    /// </summary>
    /// <remarks>
    /// The settings files carry <c>//</c> comments, which <see cref="JsonNode"/> rejects by default, so
    /// comments are allowed and trailing commas skipped - the same reader posture the sibling coherence
    /// suite uses.
    /// </remarks>
    private static IReadOnlyList<(int Port, string Scheme, string Protocol)> ReadEndpoints(
        string directoryName,
        string projectName)
    {
        string text = ReadRepositoryFile($"services/{directoryName}/{projectName}/{BaseSettingsFileName}");

        JsonNode settings = JsonNode.Parse(
            text,
            documentOptions: new System.Text.Json.JsonDocumentOptions
            {
                CommentHandling = System.Text.Json.JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            })
            ?? throw FailException.ForFailure(
                $"'services/{directoryName}/{projectName}/{BaseSettingsFileName}' parsed to null.");

        JsonObject endpoints = settings["Kestrel"]?["Endpoints"] as JsonObject
            ?? throw FailException.ForFailure(
                $"'services/{directoryName}/{projectName}/{BaseSettingsFileName}' declares no "
                    + "'Kestrel:Endpoints' object, so this service's listeners cannot be read.");

        List<(int, string, string)> bound = [];

        foreach ((string name, JsonNode? node) in endpoints)
        {
            string url = node?["Url"]?.GetValue<string>()
                ?? throw FailException.ForFailure(
                    $"Endpoint '{name}' of '{projectName}' declares no 'Url'.");

            string protocol = node?["Protocols"]?.GetValue<string>()
                ?? throw FailException.ForFailure(
                    $"Endpoint '{name}' of '{projectName}' declares no 'Protocols'. The protocol version "
                        + "is stated explicitly on every endpoint in this estate, deliberately.");

            // The bind form is `scheme://+:port`, which Uri cannot parse because `+` is not a host. The
            // scheme and the port are both unambiguous by position, so they are taken directly.
            int schemeEnd = url.IndexOf("://", StringComparison.Ordinal);

            Assert.True(
                schemeEnd > 0,
                $"Endpoint '{name}' of '{projectName}' has url '{url}', which carries no scheme.");

            int portStart = url.LastIndexOf(':');

            Assert.True(
                portStart > schemeEnd,
                $"Endpoint '{name}' of '{projectName}' has url '{url}', which carries no port.");

            bound.Add((
                int.Parse(url[(portStart + 1)..], CultureInfo.InvariantCulture),
                url[..schemeEnd],
                protocol));
        }

        return bound;
    }

    /// <summary>Looks a service up by key, failing with the roster rather than a null reference.</summary>
    private static (string Key, string DirectoryName, string ProjectName) RequireService(string serviceKey)
    {
        foreach ((string Key, string DirectoryName, string ProjectName) service in Services)
        {
            if (string.Equals(service.Key, serviceKey, StringComparison.Ordinal))
            {
                return service;
            }
        }

        throw FailException.ForFailure(
            $"No service is registered under key '{serviceKey}'. Known keys: "
                + string.Join(", ", Services.Select(static service => service.Key)) + ".");
    }

    /// <summary>
    /// Decides whether an absence marker is a claim about <paramref name="path"/>, by the nearest-subject
    /// rule: the marker belongs to whichever claimable artifact name sits closest to it, in either
    /// direction.
    /// </summary>
    /// <param name="text">The whole surface text.</param>
    /// <param name="pathIndex">Where the path occurs; the anchor this call was made for.</param>
    /// <param name="path">The path whose presence is in question.</param>
    /// <param name="markerIndex">Where the absence marker occurs.</param>
    /// <param name="marker">The absence marker.</param>
    /// <remarks>
    /// <para>
    /// BOTH DIRECTIONS ARE NECESSARY, AND EACH WAS MEASURED ON A REAL SENTENCE IN THIS TREE. The subject
    /// can precede its marker — "<c>...docker-compose.yml</c> — which would mount the file — does not
    /// exist" — or follow it — "What is missing is the mount: <c>orchestration/docker-compose.yml</c>". A
    /// rule looking only at the text BETWEEN the path and the marker catches the first and misses the
    /// second, and a naive window catches both as false positives against a Dockerfile named earlier in
    /// the same sentence.
    /// </para>
    /// <para>
    /// Distance is measured to the nearest EDGE of each candidate rather than to its start, so a name
    /// immediately abutting the marker scores zero regardless of how long the name is.
    /// </para>
    /// </remarks>
    private static bool ClaimsAbsenceOf(
        string text,
        int pathIndex,
        string path,
        int markerIndex,
        string marker)
    {
        string? nearest = null;
        int nearestDistance = int.MaxValue;

        foreach (string candidate in ClaimableArtifactNames)
        {
            foreach (int candidateIndex in IndexesOf(text, candidate))
            {
                int distance = candidateIndex >= markerIndex + marker.Length
                    ? candidateIndex - (markerIndex + marker.Length)
                    : markerIndex - Math.Min(markerIndex, candidateIndex + candidate.Length);

                // A longer name containing a shorter one - "tests/e2e/" inside a longer path - would tie
                // at the same position, so the LONGER match wins a tie and the specific name is chosen.
                if (distance < nearestDistance
                    || (distance == nearestDistance && candidate.Length > (nearest?.Length ?? 0)))
                {
                    nearest = candidate;
                    nearestDistance = distance;
                }
            }
        }

        // The path under test occurs at pathIndex and is itself on the roster, so a null nearest would be
        // a roster mistake rather than a document defect - which is worth failing loudly for.
        Assert.NotNull(nearest);

        return string.Equals(nearest, path, StringComparison.Ordinal);
    }

    /// <summary>Every index at which <paramref name="value"/> occurs in <paramref name="text"/>.</summary>
    private static IEnumerable<int> IndexesOf(string text, string value)
    {
        int index = text.IndexOf(value, StringComparison.Ordinal);

        while (index >= 0)
        {
            yield return index;

            index = text.IndexOf(value, index + 1, StringComparison.Ordinal);
        }
    }

    /// <summary>Renders the bound port map for a failure message.</summary>
    private static string DescribePorts(IReadOnlyDictionary<int, BoundEndpoint> bound)
    {
        StringBuilder text = new();

        foreach ((int port, BoundEndpoint endpoint) in bound.OrderBy(static row => row.Key))
        {
            if (text.Length > 0)
            {
                text.Append(", ");
            }

            text.Append(CultureInfo.InvariantCulture, $"{port} ({endpoint.ServiceKey}, {endpoint.Scheme}, {endpoint.Protocol})");
        }

        return text.ToString();
    }

    /// <summary>Reads a repository-root-relative file, failing with the resolved path when it is absent.</summary>
    private static string ReadRepositoryFile(string relativePath)
    {
        string absolutePath = Path.Combine(
            RequireRepositoryRoot(),
            relativePath.Replace('/', Path.DirectorySeparatorChar));

        Assert.True(
            File.Exists(absolutePath),
            $"'{relativePath}' was expected at '{absolutePath}' and is not there. Every path this suite "
                + "reads is authored by this refactor, so an absence here is a moved or deleted file "
                + "rather than a missing dependency.");

        return File.ReadAllText(absolutePath);
    }

    /// <summary>
    /// Walks up from the test output directory to the repository root, identified by both markers.
    /// </summary>
    private static string RequireRepositoryRoot()
    {
        DirectoryInfo? candidate = new(AppContext.BaseDirectory);

        while (candidate is not null)
        {
            if (File.Exists(Path.Combine(candidate.FullName, SolutionMarkerFileName))
                && File.Exists(Path.Combine(candidate.FullName, PackageVersionMarkerFileName)))
            {
                return candidate.FullName;
            }

            candidate = candidate.Parent;
        }

        throw FailException.ForFailure(
            $"No ancestor of '{AppContext.BaseDirectory}' carries both repository-root markers "
                + $"'{SolutionMarkerFileName}' and '{PackageVersionMarkerFileName}'.");
    }
}
