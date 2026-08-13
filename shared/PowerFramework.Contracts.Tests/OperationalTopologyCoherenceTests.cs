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
//      * services/persistence-service/Dockerfile declared `EXPOSE 5101` while the service bound 5101 AND
//        a second gRPC port. The endpoint the C-05..C-08 contracts lived on was undocumented at the image
//        boundary - which is the half of the review finding that says "expose all required container
//        ports". (There is no such second port: each gRPC-serving service binds ONE `Http1AndHttp2`
//        endpoint on the port AAP 0.3.2.2 assigns it, so the equality assertion below reads one port per
//        definition. It still discriminates - it is the assertion that fails if a definition and a
//        listener ever disagree in either direction.)
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
//    close that class by refusing any absence claim about an artifact that is present, while deliberately
//    leaving TRUE absence claims - above all "no characterization recording exists on either side" -
//    untouched, because those are honest and load-bearing. Two theories do it: one defends the four
//    container definitions, the other the rest of the operational set (the Compose manifest, its readme,
//    the environment template, the workflow definitions and the CI workflow), all of which now exist.
//
//  WHAT IT DOES NOT ASSERT
//    Nothing about runtime. No container is started, no socket is opened, no image is built. Every
//    assertion here is a comparison between two files in the tree. What HAS been run against a live stack
//    is reported in exactly one place - orchestration/README.md section 10 - and this file deliberately
//    neither reproduces nor depends on it: a coherence test that needed a daemon would stop being a
//    coherence test.
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
        "README.md",
        "docs/BUILD.md",
        "docs/ARCHITECTURE.md",
        "docs/CONTRACTS.md",
        "docs/PARITY.md",
        "docs/SECRETS.md",
        "docs/SERVICE_MAPPING.md",
        "docs/DEFERRED.md",
        "orchestration/.env.example",
        "orchestration/README.md",
        "characterization/README.md",
        "characterization/workflows/README.md",
        "characterization/recordings/legacy/README.md",
        "characterization/recordings/dotnet/README.md",
        "shared/PowerFramework.Contracts/OpenApi/gateway.v1.yaml",
        "shared/PowerFramework.Contracts/OpenApi/security.v1.yaml",
        "tests/e2e/README.md",
        "tests/e2e/playwright.config.ts",
        "tests/e2e/fixtures/service-endpoints.ts",
        "tests/e2e/fixtures/auth.ts",
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
        "orchestration/.env.example",
        "orchestration/README.md",
        "characterization/",
        "characterization/workflows/",
        "characterization/recordings/",
        "tests/e2e/",
        ".github/workflows/ci.yml",
        "PowerFramework.slnx",
        "services/persistence-service/Dockerfile",
        "services/dataservices-service/Dockerfile",
        "services/security-service/Dockerfile",
        "services/gateway-service/Dockerfile",
    ];

    /// <summary>The Compose scale flag, which is the token every replica claim is built around.</summary>
    private const string ScaleFlag = "--scale";

    /// <summary>How many characters either side of a <c>--scale</c> mention the caveat has to fall within.</summary>
    /// <remarks>
    /// Wider than <see cref="AbsenceWindowRadius"/> on purpose. An absence claim is one clause; the
    /// constraint on scaling takes a sentence or a fenced override block to state, and both the manifest
    /// comment and the operations readme separate the mention from the explanation by a paragraph.
    /// </remarks>
    private const int ScalingCaveatWindowRadius = 1200;

    /// <summary>
    /// Any one of these, near a <c>--scale</c> mention, discharges the obligation to say what bounds it.
    /// </summary>
    /// <remarks>
    /// Three shapes, because three different surfaces legitimately say it three ways: the measured daemon
    /// error, the override that removes the host publish, and the plain statement of the topology the
    /// manifest actually runs.
    /// </remarks>
    private static readonly string[] ScalingCaveatMarkers =
    [
        "already allocated",
        "!reset",
        "one container per service",
        "one-container-per-service",
        "fixed host port",
    ];

    /// <summary>
    /// The handle-affinity keys the scaling documentation names, each as the contract message and field it
    /// actually is.
    /// </summary>
    /// <remarks>
    /// SIX HANDLE FAMILIES, FOUR DISTINCT FIELDS. Persistence's three task kinds - query, update and
    /// command - all travel in <c>TaskHandle.task_id</c>, which is why the list is shorter than the table
    /// in <c>orchestration/README.md</c> §6.3.2 and not in disagreement with it.
    /// </remarks>
    private static readonly (string ProtoPath, string Message, string Field)[] HandleAffinityKeys =
    [
        ("shared/PowerFramework.Contracts/Proto/persistence.v1.proto", "SessionHandle", "session_id"),
        ("shared/PowerFramework.Contracts/Proto/persistence.v1.proto", "TaskHandle", "task_id"),
        ("shared/PowerFramework.Contracts/Proto/dataservices.v1.proto", "OpenValidationSessionResponse", "session_id"),
        ("shared/PowerFramework.Contracts/Proto/dataservices.v1.proto", "RetrieveRequest", "datawindow_handle"),
    ];

    /// <summary>
    /// The surfaces that may legitimately discuss replicas: the manifest itself, plus every describing
    /// surface.
    /// </summary>
    private static readonly string[] ScalingSurfaces =
        [.. new[] { "orchestration/docker-compose.yml" }.Concat(DescribingSurfaces)];

    /// <summary>
    /// Operational artifacts that exist in this tree and may therefore not be described as absent.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THIS ROSTER IS THE GENERALISATION OF A DEFECT THAT WAS ONLY EVER FIXED FOR THE CONTAINER
    /// DEFINITIONS. Nine surfaces in this tree said <c>orchestration/docker-compose.yml</c> "does not
    /// exist" after it had been authored, and several used that claim to justify a further conclusion -
    /// that the bring-up could not be run, that a health gate could not be probed, that a secret had
    /// nowhere to be projected. Each of those conclusions then read as a limitation of the system rather
    /// than as stale text, and the project's published documentation is written from these files.
    /// </para>
    /// <para>
    /// EVERY ENTRY IS ASSERTED TO EXIST BEFORE ITS CLAIMS ARE CHECKED, so this roster cannot silently
    /// become a list of things that were once present. A directory entry ends in <c>/</c> and is checked
    /// as a directory; anything else is checked as a file.
    /// </para>
    /// <para>
    /// WHAT IS DELIBERATELY NOT ON IT: <c>characterization/recordings/legacy/</c> and
    /// <c>characterization/recordings/dotnet/</c>. Both roots exist, but they hold no recording and no
    /// per-workflow directory - so statements about an absent capture are TRUE and load-bearing, and
    /// putting the recording roots here would forbid the one absence claim this repository most needs to
    /// keep saying.
    /// </para>
    /// </remarks>
    private static readonly string[] ExistingOperationalArtifacts =
    [
        "orchestration/docker-compose.yml",
        "orchestration/README.md",
        "orchestration/.env.example",
        "characterization/workflows/",
        ".github/workflows/ci.yml",
    ];

    /// <summary>
    /// The operational artifacts whose presence a describing surface may not deny. Each is a repository
    /// path that EXISTS, paired with the concrete on-disk probe that proves it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THIS ROSTER IS THE GENERALISATION THAT THE DOCKERFILE-ONLY VERSION OF THIS GUARD LACKED. The
    /// original checked four container definitions and nothing else, so an entire documentation set could
    /// - and did - go on asserting that the Compose manifest, its runbook, the characterization store and
    /// the end-to-end tree were unwritten long after all four had landed. An operator following that prose
    /// would configure TLS, bootstrap, readiness or characterization against a tree that no longer
    /// matched it.
    /// </para>
    /// <para>
    /// A directory entry is deliberately matched with its trailing slash, which is how every one of these
    /// claims was actually spelled. The nearest-subject rule of <see cref="ClaimsAbsenceOf"/> keeps the
    /// still-true statements sayable: "the store exists and no RECORDING exists" survives, because the
    /// absence is attached to the recording rather than to the path.
    /// </para>
    /// </remarks>
    /// <summary>
    /// Subjects that legitimately carry an absence marker in this tree but are NOT artifacts this suite
    /// asserts the presence of. They exist so <see cref="ClaimsAbsenceOf"/> can attribute a marker to the
    /// right subject and stop mis-reading a true statement as a false one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// EVERY ENTRY HERE IS A MEASURED FALSE POSITIVE, not a defensive guess. When the absence guard was
    /// generalised from the four container definitions to every operational artifact, it reported eight
    /// failures. Two were real and were fixed. The other six were all the same mistake: a window
    /// contained an artifact path AND an absence marker that belonged to some other subject sitting
    /// between them, and because that subject was not on any roster the nearest-subject rule could not
    /// see it and defaulted to the path.
    /// </para>
    /// <para>
    /// The six, so the entries below are traceable to the sentence each one rescues:
    /// "naming a path that does not exist fails the COPY" in three container definitions, where the
    /// subject is the absent <c>NuGet.config</c> two lines above and the nearby path is the excluded
    /// <c>tests/e2e/</c>; "paired legacy recordings that do not exist yet" in the build document, near a
    /// mention of the CI workflow; and "a volume named after a service that does not exist" in the
    /// characterization readme, near a link to the Compose manifest that declares the volume. A seventh
    /// surfaced on the next run: "a recording whose workflow identifier does not exist on the other side",
    /// sitting directly under a table of <c>characterization/</c> paths.
    /// </para>
    /// <para>
    /// Adding to this roster WEAKENS the guard, so each addition has to be a subject that genuinely owns
    /// the marker - never a way to silence a claim that is actually false.
    /// </para>
    /// </remarks>
    private static readonly string[] NonArtifactAbsenceSubjects =
    [
        "NuGet.config",
        "legacy recordings",
        "legacy recording",
        "named after a service",
        "a service that",
        "workflow identifier",
    ];

    /// <summary>
    /// Phrasings that assert, in the present tense, that a listener THIS repository binds is cleartext.
    /// </summary>
    /// <remarks>
    /// A closed list rather than a pattern, because "cleartext" appears legitimately well over a hundred
    /// times in this tree - always explaining what a cleartext listener would cost, which is why none is
    /// declared. Only these phrasings state it as a present fact, and every one of them was a real
    /// sentence here that had outlived the listener it described.
    /// </remarks>
    private static readonly string[] PresentTenseCleartextClaims =
    [
        "listeners this repository binds are cleartext",
        "listeners here binds are cleartext",
        "listener is cleartext",
        "both listeners are cleartext",
        "documented cleartext bring-up",
        "cleartext, like the other three",
        "bind a second cleartext endpoint",
    ];

    /// <summary>
    /// Phrasings that tell a reader the documented Compose bring-up cannot be performed at all.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="AbsenceMarkers"/>: these are the CONCLUSIONS drawn from the manifest's
    /// former absence rather than statements of that absence, so the absence guard cannot see them. Each
    /// was a real sentence in this tree. "Has not been run" is deliberately NOT here - unexercised is a
    /// legitimate thing to say, and several places still need to say it.
    /// </remarks>
    private static readonly string[] PresentTenseUnrunnableClaims =
    [
        "NOTHING IN IT CAN BE RUN TODAY",
        "cannot be run today",
        "no way to bring a stack up",
        "every `docker compose` command below will fail",
        "no stack can be brought up today",
        "This command cannot pass in this repository today",
        "PLANNED, NOT AVAILABLE",
        "it does not exist yet",
        "remain intended rather than runnable",
    ];

    /// <summary>
    /// The gRPC contracts whose method count <c>docs/CONTRACTS.md</c> publishes, paired with the protobuf
    /// service that count is about.
    /// </summary>
    /// <remarks>
    /// C-09 and C-10 are deliberately absent: C-09's register cell defers to its own section rather than
    /// carrying a number, and C-10 is stated per service rather than as a service method count. C-01 and
    /// C-02 are REST and are counted in operations, which the OpenAPI documents' own tests own.
    /// </remarks>
    private static readonly (string ContractId, string ServiceName)[] DocumentedGrpcContracts =
    [
        ("C-03", "DataWindowService"),
        ("C-04", "ColumnExpressionService"),
        ("C-05", "QueryService"),
        ("C-06", "UpdateService"),
        ("C-07", "CommandService"),
        ("C-08", "TransactionService"),
    ];

    private static readonly (string RelativePath, bool IsDirectory)[] ClaimableOperationalArtifacts =
    [
        ("orchestration/docker-compose.yml", false),
        ("orchestration/.env.example", false),
        ("orchestration/README.md", false),
        ("characterization/", true),
        ("characterization/workflows/", true),
        ("characterization/recordings/", true),
        ("tests/e2e/", true),
        (".github/workflows/ci.yml", false),
        ("PowerFramework.slnx", false),
        ("services/persistence-service/Dockerfile", false),
        ("services/dataservices-service/Dockerfile", false),
        ("services/security-service/Dockerfile", false),
        ("services/gateway-service/Dockerfile", false),
    ];

    /// <summary>
    /// Every service key, for the theories that iterate all four.
    /// </summary>
    public static TheoryData<string> AllServiceKeys { get; } =
        new(Services.Select(static service => service.Key));

    /// <summary>Every surface a replica claim could appear in, so a failure names one file.</summary>
    public static TheoryData<string> AllScalingSurfaces { get; } = new(ScalingSurfaces);

    /// <summary>
    /// Every operational artifact whose presence a surface may not deny.
    /// </summary>
    public static TheoryData<string> AllExistingOperationalArtifacts { get; } =
        new(ExistingOperationalArtifacts);

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
    /// `fetch` in the next spec written. Before this test it printed <c>http</c> for three of its rows
    /// while the very next paragraph in the same file said every listener terminates TLS, and while the
    /// module's own exported defaults were <c>https</c> - three statements in one file, one of them
    /// wrong.
    /// </para>
    /// <para>
    /// Completeness is asserted as well as agreement, because a row quietly dropped from that table is how
    /// a bound port stops being visible to the next reader. The table is four rows now that the two
    /// separate gRPC endpoints have been collapsed onto the assigned ports, and it must stay exactly the
    /// bound set: a fifth row would describe a listener nobody binds, and a missing row would hide one.
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
    /// bound two ports at the time and its definition declared only the first, so the endpoint carrying
    /// contracts C-05 through C-08 was undeclared. <c>EXPOSE</c> creates nothing at run time, which is
    /// exactly why the omission was invisible in every build: it is documentation the tooling reads, and a
    /// reader or an orchestrator consulting it would have concluded the service serves one port.
    /// </para>
    /// <para>
    /// ONE ENDPOINT PER SERVICE DOES NOT MAKE THIS ASSERTION IDLE, IT DECIDES WHICH HALF BITES. Every
    /// service binds one port, so under-declaration is unlikely and OVER-declaration is
    /// the live risk: a definition carrying a second <c>EXPOSE</c> line advertises
    /// a listener the host does not bind, and a published mapping or probe aimed at it fails in a
    /// way that reads as the service being down.
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
    public void NoActiveSurfaceClaimsAnOperationalArtifactIsAbsentWhileItExists(string surfacePath)
    {
        string text = ReadRepositoryFile(surfacePath);
        string root = RequireRepositoryRoot();

        foreach ((string relativePath, bool isDirectory) in ClaimableOperationalArtifacts)
        {
            string absolute = Path.Combine(
                root,
                relativePath.TrimEnd('/').Replace('/', Path.DirectorySeparatorChar));

            bool present = isDirectory ? Directory.Exists(absolute) : File.Exists(absolute);

            if (!present)
            {
                // A genuinely absent artifact may be described as absent. Nothing to assert.
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
    /// Asserts that no describing surface claims an operational artifact on
    /// <see cref="ExistingOperationalArtifacts"/> is absent, while that artifact is present in the tree.
    /// </summary>
    /// <param name="artifactPath">The artifact whose presence is being defended.</param>
    /// <remarks>
    /// <para>
    /// THE SIBLING THEORY ABOVE DEFENDS THE FOUR CONTAINER DEFINITIONS; THIS ONE DEFENDS THE REST OF THE
    /// OPERATIONAL SET, for the same reason and against the same measured failure. A false absence claim
    /// is not read as out of date - it is read as a reason not to attempt something that already works,
    /// and it propagates into published documentation unchallenged.
    /// </para>
    /// <para>
    /// The mechanism is identical, deliberately: the marker has to fall within
    /// <see cref="AbsenceWindowRadius"/> characters of the artifact's own name, and the nearest-subject
    /// rule of <see cref="ClaimsAbsenceOf"/> decides which of several nearby names a marker is about. That
    /// keeps a true absence claim - "no recording exists" - sayable in the same paragraph as the manifest
    /// that is present, which is exactly how these documents need to say it.
    /// </para>
    /// <para>
    /// EVERY SURFACE IS SCANNED, INCLUDING <c>orchestration/README.md</c> ITSELF. The single
    /// execution-status statement is the one file whose accuracy every other file now depends on, so it is
    /// held to the same rule rather than exempted as the authority.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(AllExistingOperationalArtifacts))]
    public void NoActiveSurfaceClaimsAnExistingOperationalArtifactIsAbsent(string artifactPath)
    {
        string root = RequireRepositoryRoot();
        string absolutePath = Path.Combine(
            root,
            artifactPath.TrimEnd('/').Replace('/', Path.DirectorySeparatorChar));

        Assert.True(
            artifactPath.EndsWith('/')
                ? Directory.Exists(absolutePath)
                : File.Exists(absolutePath),
            $"'{artifactPath}' is on the roster of artifacts that must exist, but it is not present at "
                + $"'{absolutePath}'. Either restore it or remove it from the roster - a roster of things "
                + "that used to exist defends nothing.");

        foreach (string surfacePath in DescribingSurfaces)
        {
            string text = ReadRepositoryFile(surfacePath);

            foreach (int index in IndexesOf(text, artifactPath))
            {
                int start = Math.Max(0, index - AbsenceWindowRadius);
                int end = Math.Min(text.Length, index + artifactPath.Length + AbsenceWindowRadius);

                foreach (string marker in AbsenceMarkers)
                {
                    foreach (int markerIndex in IndexesOf(text[start..end], marker))
                    {
                        Assert.False(
                            ClaimsAbsenceOf(text, index, artifactPath, start + markerIndex, marker),
                            $"'{surfacePath}' claims '{artifactPath}' {marker}, but it is present in the "
                                + "tree. Reconcile the statement, or scope it to what is genuinely absent "
                                + "- for the capture store that is the recordings, not the workflows. The "
                                + $"claim was found here:{Environment.NewLine}{text[start..end]}");
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
    // 5. Scaling is never claimed without the constraint that bounds it
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Asserts that no surface mentions <c>--scale</c> without carrying, nearby, the reason it does not
    /// work against the published manifest or the override that makes it work.
    /// </summary>
    /// <param name="surfacePath">The surface under test.</param>
    /// <remarks>
    /// <para>
    /// THIS CLOSES A MEASURED FALSE CLAIM. The manifest asserted that omitting <c>container_name:</c> is
    /// "what make[s] more than one replica expressible". Omitting it is NECESSARY and is not SUFFICIENT:
    /// every service publishes a fixed host port, a host port can be bound once, and the second replica
    /// therefore fails at start with <c>Bind for 0.0.0.0:&lt;port&gt; failed: port is already allocated</c>.
    /// That was measured on this manifest, and the claim had been standing beside the very
    /// <c>ports:</c> blocks that falsified it.
    /// </para>
    /// <para>
    /// The rule is deliberately a PROXIMITY rule rather than a forbidden-phrase list. A phrase list closes
    /// one sentence; this closes the shape of the mistake, which is discussing scaling while leaving out
    /// what bounds it. Any of the caveat markers satisfies it, so a surface remains free to explain the
    /// constraint, to give the override, or to do both, in whatever wording suits it.
    /// </para>
    /// <para>
    /// A surface that never mentions <c>--scale</c> passes vacuously, and that is correct: no document is
    /// obliged to discuss replicas. What none of them may do is discuss them incompletely.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(AllScalingSurfaces))]
    public void NoSurfaceDiscussesScalingWithoutTheConstraintThatBoundsIt(string surfacePath)
    {
        string text = ReadRepositoryFile(surfacePath);

        foreach (int index in IndexesOf(text, ScaleFlag))
        {
            int start = Math.Max(0, index - ScalingCaveatWindowRadius);
            int end = Math.Min(text.Length, index + ScaleFlag.Length + ScalingCaveatWindowRadius);
            string window = text[start..end];

            Assert.True(
                ScalingCaveatMarkers.Any(marker =>
                    window.Contains(marker, StringComparison.OrdinalIgnoreCase)),
                $"'{surfacePath}' mentions '{ScaleFlag}' without any of "
                    + $"[{string.Join(", ", ScalingCaveatMarkers)}] within {ScalingCaveatWindowRadius} characters. Every "
                    + "service in the manifest publishes a fixed host port, so N>1 fails at the second "
                    + "replica's start with 'port is already allocated'. Say what bounds it, or give the "
                    + $"override that removes the publish. The mention was found here:{Environment.NewLine}"
                    + window);
        }
    }

    /// <summary>
    /// Asserts that every handle-affinity key the scaling documentation names is a real field of a real
    /// contract message.
    /// </summary>
    /// <remarks>
    /// <para>
    /// STICKY ROUTING IS THE ONE OBLIGATION A SECOND REPLICA PLACES ON A CALLER, and it is only actionable
    /// if the key it is keyed on is named exactly. Four operations return an opaque handle whose state
    /// lives in the process that issued it, so a follow-up call has to reach that process; the
    /// documentation therefore tabulates the field each handle travels in. A field renamed in a
    /// <c>.proto</c> would leave that table naming something a caller cannot find, which is worse than
    /// naming nothing: it reads as precise and is not.
    /// </para>
    /// <para>
    /// The assertion is deliberately two-directional in effect. It fails if a documented key stops
    /// existing in the contract, and it fails if the documentation drops one of the four messages the
    /// handles travel in, because each expectation names both the message and the field.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryDocumentedHandleAffinityKeyIsARealContractField()
    {
        string operationalText = ReadRepositoryFile("orchestration/README.md");
        string manifestText = ReadRepositoryFile("orchestration/docker-compose.yml");

        foreach ((string protoPath, string message, string field) in HandleAffinityKeys)
        {
            string protoText = ReadRepositoryFile(protoPath);
            int messageIndex = protoText.IndexOf(
                $"message {message} {{",
                StringComparison.Ordinal);

            Assert.True(
                messageIndex >= 0,
                $"'{protoPath}' declares no 'message {message}', but the scaling documentation names it as "
                    + "the carrier of a handle-affinity key. Reconcile the two: a caller cannot route on a "
                    + "message that does not exist.");

            int messageEnd = protoText.IndexOf(
                $"{Environment.NewLine}}}",
                messageIndex,
                StringComparison.Ordinal);
            string body = messageEnd < 0 ? protoText[messageIndex..] : protoText[messageIndex..messageEnd];

            Assert.Contains($"string {field} = ", body, StringComparison.Ordinal);

            Assert.True(
                operationalText.Contains(field, StringComparison.Ordinal)
                    && manifestText.Contains(field, StringComparison.Ordinal),
                $"'{field}' is a field of '{message}' in '{protoPath}' and is one of the handle-affinity "
                    + "keys, so both 'orchestration/README.md' and 'orchestration/docker-compose.yml' must "
                    + "name it. A replica cannot be routed to on a key the operator is never told.");
        }
    }

    // ----------------------------------------------------------------------------------------------
    // 5. Every container path the manifest configures is behind a projection that actually places a file
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Asserts that every service the Compose manifest defines has the TLS material projected into it,
    /// read-only, and that every TLS path it configures resolves INSIDE that projection.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>⚠ THIS ASSERTION CATCHES A DEFECT THAT MAKES THE DOCUMENTED BRING-UP IMPOSSIBLE.</b> The
    /// defect: a manifest that passes three CONTAINER paths to all four services - the Kestrel
    /// certificate pair and the internal trust anchor - with all four Dockerfile <c>HEALTHCHECK</c>s
    /// reading the anchor path, while the manifest mounts NOTHING at any of them. Every path is
    /// individually correct and every other guard passes. But every listener in the stack
    /// is <c>https</c>, and Kestrel REFUSES TO START an HTTPS endpoint whose certificate it cannot
    /// resolve rather than downgrading to plaintext, so all four services crash-loop, no
    /// <c>/health</c> ever answers, and the <c>depends_on: service_healthy</c> chain never opens.
    /// </para>
    /// <para>
    /// THE DEFECT LIVES BETWEEN TWO CORRECT HALVES, which is why no test of either half can see it: a
    /// configuration key naming a path, and a manifest declaring mounts. Nothing else compares them. This row
    /// is that comparison, and it is stated as a rule rather than as a list - EVERY service, and EVERY
    /// configured path - so that a fifth service, or a fourth path, is covered the moment it is added.
    /// </para>
    /// <para>
    /// PARSED FROM THE MANIFEST'S OWN TEXT rather than from a resolved <c>docker compose config</c>,
    /// because this suite must run with no Docker daemon and no populated environment file. The scan is
    /// deliberately narrow: it reads only the service keys, the projection targets and the values of the
    /// three TLS configuration keys, all of which sit at fixed indentation in this authored file, and it
    /// REFUSES rather than passing vacuously when it finds no service at all.
    /// </para>
    /// <para>
    /// THE MECHANISM IS A COMPOSE SECRET, AND THIS ROW ASSERTS THE PROPERTY RATHER THAN THE MECHANISM.
    /// Each service takes a <c>secrets:</c> grant whose <c>target:</c> is relative to <c>/run/secrets</c>,
    /// so a grant of <c>internal-tls/server.crt</c> places the file at
    /// <c>/run/secrets/internal-tls/server.crt</c> - which is what the configured paths name. A secret
    /// projection is read-only by definition, so the read-only half needs no separate flag to assert;
    /// that is the one respect in which it is stronger than a bind mount, where omitting <c>read_only</c>
    /// silently yields a writable private key. The three host-side sources are
    /// declared once at the foot of the manifest and are asserted by
    /// <see cref="TheTlsMaterialSourceVariablesAreDeclaredAndDocumented"/>.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryConfiguredTlsPathIsBehindAReadOnlyMountInEveryComposeService()
    {
        const string manifestPath = "orchestration/docker-compose.yml";

        // The container-side directory every configured TLS path must resolve inside. A Compose secret's
        // own `target:` is stated RELATIVE to /run/secrets, so the grant spells the tail alone.
        const string mountTarget = "/run/secrets/internal-tls";
        const string grantTargetPrefix = "target: internal-tls/";

        string[] lines = ReadRepositoryFile(manifestPath).Split('\n');

        // The three keys whose VALUE is a path the running container must be able to open. Kestrel
        // resolves the first two or refuses to start; the third is read by the application AND by the
        // image's own readiness probe.
        string[] pathKeys =
        [
            "Kestrel__Certificates__Default__Path:",
            "Kestrel__Certificates__Default__KeyPath:",
            "INTERNAL_TLS_TRUSTED_CA_PATH:",
        ];

        Dictionary<string, bool> mountsMaterial = new(StringComparer.Ordinal);
        Dictionary<string, List<string>> configuredPaths = new(StringComparer.Ordinal);
        string? current = null;
        bool insideServices = false;

        foreach (string rawLine in lines)
        {
            string line = rawLine.TrimEnd('\r');

            if (line.Length == 0)
            {
                continue;
            }

            // A top-level key. `services:` opens the block and any other one closes it.
            if (!char.IsWhiteSpace(line[0]) && !line.StartsWith('#'))
            {
                insideServices = line.StartsWith("services:", StringComparison.Ordinal);
                current = null;

                continue;
            }

            if (!insideServices)
            {
                continue;
            }

            // A service key: exactly two spaces of indentation, not a comment, and a trailing colon.
            string trimmed = line.Trim();
            if (line.StartsWith("  ", StringComparison.Ordinal)
                && !line.StartsWith("   ", StringComparison.Ordinal)
                && !trimmed.StartsWith('#')
                && trimmed.EndsWith(':')
                && !trimmed.Contains(' ', StringComparison.Ordinal))
            {
                current = trimmed[..^1];
                mountsMaterial[current] = false;
                configuredPaths[current] = [];

                continue;
            }

            if (current is null || trimmed.StartsWith('#'))
            {
                continue;
            }

            // ONE GRANT INTO THIS DIRECTORY IS ENOUGH TO RECORD THE PROJECTION; the per-file completeness
            // of the grant is asserted below, against every service at once.
            if (trimmed.StartsWith(grantTargetPrefix, StringComparison.Ordinal))
            {
                mountsMaterial[current] = true;

                continue;
            }

            foreach (string key in pathKeys)
            {
                if (trimmed.StartsWith(key, StringComparison.Ordinal))
                {
                    configuredPaths[current].Add(trimmed[key.Length..].Trim());
                }
            }
        }

        // REFUSE RATHER THAN PASS VACUOUSLY. A structural change that defeated the scan would otherwise
        // leave this row asserting nothing at all.
        Assert.True(
            mountsMaterial.Count >= Services.Length,
            $"'{manifestPath}' yielded {mountsMaterial.Count} service keys and at least "
                + $"{Services.Length} were expected, so this scan is no longer reading the manifest's "
                + "structure and would pass without checking anything.");

        foreach ((string service, bool mounted) in mountsMaterial)
        {
            Assert.True(
                mounted,
                $"'{manifestPath}' service '{service}' has nothing projected into '{mountTarget}'. It "
                    + "configures TLS paths under that directory, and Kestrel refuses to start an https "
                    + "endpoint whose certificate it cannot resolve - so without the projection this "
                    + "service crash-loops and its /health never answers.");

            foreach (string configured in configuredPaths[service])
            {
                Assert.Contains(mountTarget + "/", configured, StringComparison.Ordinal);
            }
        }

        // EVERY SERVICE TAKES ALL THREE FILES, not merely one of them. Kestrel needs the certificate and
        // the key; the application and the image's own probe need the anchor. Counted over the whole file
        // because one count per file is enough to prove none was omitted anywhere.
        foreach (string leaf in (string[])["server.crt", "server.key", "ca.crt"])
        {
            int grants = lines.Count(line => string.Equals(
                line.TrimEnd('\r').Trim(),
                grantTargetPrefix + leaf,
                StringComparison.Ordinal));

            Assert.True(
                grants == Services.Length,
                $"'{manifestPath}' grants '{grantTargetPrefix}{leaf}' to {grants} services and "
                    + $"{Services.Length} were expected. A service missing the certificate or the key "
                    + "cannot start its https listener; one missing the anchor cannot verify a peer, and "
                    + "its own HEALTHCHECK reads that same path.");
        }

        // AND EVERY GRANT RESOLVES TO A DECLARED SOURCE. A `secrets:` grant naming a secret the manifest
        // never declares is refused by Compose at parse time, but naming the WRONG declared secret is not
        // - so the three names are pinned here against the block that declares them.
        string manifestText = string.Join('\n', lines);
        int declarations = manifestText.IndexOf("\nsecrets:", StringComparison.Ordinal);

        Assert.True(
            declarations >= 0,
            $"'{manifestPath}' declares no top-level 'secrets:' block, so the projections asserted above "
                + "have no source and bring-up cannot supply the material.");

        // THE ANCHOR IS SHARED BY ALL FOUR, AND SHARING IT IS CORRECT. It is the PUBLIC certificate of the
        // authority that signed every leaf: it carries no private material, and every service must verify
        // against the same authority or the mesh cannot form.
        Assert.Contains(
            "  internal-tls-ca-certificate:",
            manifestText[declarations..],
            StringComparison.Ordinal);

        Assert.Equal(
            Services.Length,
            lines.Count(line => string.Equals(
                line.TrimEnd('\r').Trim(),
                "- source: internal-tls-ca-certificate",
                StringComparison.Ordinal)));

        // ⚠ BUT THE SERVER PAIR IS PER-SERVICE, AND THIS IS THE ROW THAT KEEPS IT THAT WAY.
        //
        // An earlier revision declared ONE `tls-server-certificate` and ONE `tls-server-private-key` and
        // granted each to all four services, and this assertion REQUIRED that - it pinned both names at a
        // grant count of four. That made the four services cryptographically indistinguishable: one key
        // read out of any container was the key every other service presented, the shared certificate had
        // to carry subject alternative names for every origin so it validated as any peer, and mutual TLS
        // between two internal services proved only that the peer held THE key rather than that it was
        // the peer it claimed to be. Compromise had no blast radius short of the whole estate.
        //
        // So each service now declares its own pair and is granted only its own. The assertion is
        // inverted accordingly: each name must be granted EXACTLY ONCE, and the four sources must be
        // four DISTINCT names. Counting is what makes a regression detectable - re-pointing two services
        // at one source is a two-line edit that reads as a simplification.
        foreach ((string prefix, _, _) in Services)
        {
            foreach (string half in (string[])["certificate", "private-key"])
            {
                string secretName = $"{prefix}-tls-server-{half}";

                Assert.Contains(
                    $"  {secretName}:",
                    manifestText[declarations..],
                    StringComparison.Ordinal);

                int grants = lines.Count(line => string.Equals(
                    line.TrimEnd('\r').Trim(),
                    "- source: " + secretName,
                    StringComparison.Ordinal));

                Assert.True(
                    grants == 1,
                    $"'{manifestPath}' grants '{secretName}' to {grants} services and exactly 1 was "
                        + "expected. A server key granted to more than one service gives those services "
                        + "the same cryptographic identity, so either can present itself as the other and "
                        + "a compromise of one is a compromise of both.");
            }
        }

        // AND NO SHARED SERVER-PAIR SECRET SURVIVES ANYWHERE, so the old names cannot be reintroduced
        // beside the new ones and quietly re-shared.
        foreach (string retired in (string[])["tls-server-certificate", "tls-server-private-key"])
        {
            Assert.DoesNotContain(
                $"\n  {retired}:",
                manifestText[declarations..]);

            Assert.DoesNotContain(
                lines,
                line => string.Equals(
                    line.TrimEnd('\r').Trim(),
                    "- source: " + retired,
                    StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// Asserts that the three variables the TLS projection cannot work without are declared in the
    /// environment template, demanded by the manifest, and documented in the surfaces an operator reads.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A PROJECTION WHOSE SOURCE VARIABLE NOBODY DECLARES IS A PROJECTION NOBODY CAN SUPPLY. Each of the
    /// three secrets at the foot of the manifest takes its <c>file:</c> from a variable in Compose's
    /// <c>:?</c> form, so bring-up ABORTS when one is missing rather than starting a stack whose listeners
    /// cannot resolve a certificate - which is the right posture and also a dead end for an operator who
    /// cannot find out what to put in it. This row keeps the template and the generation recipe in step
    /// with the demand.
    /// </para>
    /// <para>
    /// THREE VARIABLES RATHER THAN ONE DIRECTORY, DELIBERATELY. The certificate, its key and the issuing
    /// authority are three distinct artifacts with three distinct lifetimes and, on a real deployment,
    /// three distinct sources - so naming a single parent directory would force them to be co-located and
    /// would make a rotation of one look like a rotation of all three. The container side is not
    /// templated at all: it is fixed by the grant targets, which
    /// <see cref="EveryConfiguredTlsPathIsBehindAReadOnlyMountInEveryComposeService"/> pins.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheTlsMaterialSourceVariablesAreDeclaredAndDocumented()
    {
        string manifest = ReadRepositoryFile("orchestration/docker-compose.yml");
        string[] template = ReadRepositoryFile("orchestration/.env.example").Split('\n');

        // THE PER-SERVICE PAIRS PLUS THE ONE SHARED ANCHOR. Nine variables, and the count is the point:
        // eight of them exist BECAUSE each service carries its own server identity rather than sharing
        // one key with the other three.
        //
        // ⚠ AND EVERY MATCH BELOW IS ANCHORED, WHICH FIXED A VACUOUS PASS. This row used to iterate the
        // shared names `TLS_CERTIFICATE_PATH` and `TLS_CERTIFICATE_KEY_PATH` with an unanchored
        // `Assert.Contains`. When the manifest moved to per-service pairs those two variables stopped
        // existing entirely - and the row still PASSED, because `SECURITY_TLS_CERTIFICATE_PATH:?`
        // contains `TLS_CERTIFICATE_PATH:?` as a substring. A guard that cannot tell a variable from the
        // tail of a longer one is not guarding the variable, so each match is now pinned to a position
        // where only the whole name can satisfy it.
        string[] expected =
        [
            .. Services.SelectMany(static service => (string[])
            [
                $"{service.Key.ToUpperInvariant()}_TLS_CERTIFICATE_PATH",
                $"{service.Key.ToUpperInvariant()}_TLS_CERTIFICATE_KEY_PATH",
            ]),
            "INTERNAL_TLS_CA_PATH",
        ];

        foreach (string variable in expected)
        {
            // The `:?` form, so an unset value aborts bring-up instead of resolving to empty. Anchored on
            // the opening brace so a longer variable ending in this name cannot satisfy it.
            Assert.Contains("${" + variable + ":?", manifest, StringComparison.Ordinal);

            // Declared as an assignable line in the template, not merely mentioned in its prose.
            Assert.Contains(
                template,
                line => line.TrimEnd('\r').StartsWith(variable + "=", StringComparison.Ordinal));

            foreach (string surface in (string[])
                ["orchestration/README.md", "docs/ARCHITECTURE.md", "docs/BUILD.md"])
            {
                Assert.Contains(variable, ReadRepositoryFile(surface), StringComparison.Ordinal);
            }
        }
    }

    /// <summary>
    /// Asserts that the two certificate-strictness variables are projected by the manifest, assignable in
    /// the template, and documented on all three operational surfaces.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>THESE TWO SETTINGS REPLACED A HARDCODED VALUE, WHICH IS WHY BEING DISCOVERABLE IS PART OF THE
    /// FIX RATHER THAN POLISH ON TOP OF IT.</b> Revocation checking used to be
    /// <c>X509RevocationMode.NoCheck</c> written into four call sites with a comment explaining it, so the
    /// weakest posture was not merely the default - it was the only reachable one, and no deployment could
    /// change it without editing code. Making it a setting is only half the remedy: a setting an operator
    /// cannot find is not meaningfully configurable either, and this failure mode is silent, because the
    /// shipped default IS the permissive value and a deployment that never discovers the key keeps it.
    /// </para>
    /// <para>
    /// <b>THE `:-` FORM RATHER THAN THE `:?` FORM, AND THAT DIFFERENCE IS ASSERTED DELIBERATELY.</b> The
    /// TLS material above uses <c>:?</c> because there is no sane default for a path only the operator
    /// knows - bring-up must abort. These two DO have a defensible default, and it is the same value the
    /// settings files ship, so a template copied unedited must behave identically to one that sets
    /// neither. A <c>:?</c> here would break the documented bring-up for every deployment that does not
    /// care about the setting.
    /// </para>
    /// <para>
    /// <b>WHY THE DOCUMENTATION HALF IS ENFORCED.</b> The shipped mode is <c>NoCheck</c> because the local
    /// authority the documentation tells an operator to build publishes neither a CRL distribution point
    /// nor an OCSP responder - measured, not assumed: both stricter modes fail the chain with
    /// <c>RevocationStatusUnknown | OfflineRevocation</c>. An operator who sets a stricter mode against
    /// that PKI refuses every internal peer and every caller certificate, which presents as a broken
    /// deployment rather than as a rejected setting. Requiring the reason on all three surfaces is what
    /// stops that being rediscovered, and requiring the LIFETIME variable beside it is what stops the
    /// compensating control being documented as a claim - which is the state it was in before, asserted
    /// in a code comment and enforced nowhere.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheCertificateStrictnessVariablesAreProjectedDeclaredAndDocumented()
    {
        string manifest = ReadRepositoryFile("orchestration/docker-compose.yml");
        string[] template = ReadRepositoryFile("orchestration/.env.example").Split('\n');

        foreach (string variable in (string[])
            ["INTERNAL_TLS_REVOCATION_MODE", "SECURITY_MTLS_CLIENT_MAX_LIFETIME_DAYS"])
        {
            // Projected WITH a default, so an unset value takes the shipped posture rather than aborting.
            Assert.Contains(variable + ":-", manifest, StringComparison.Ordinal);

            Assert.DoesNotContain(variable + ":?", manifest);

            // Assignable in the template, not merely described in its prose.
            Assert.Contains(
                template,
                line => line.TrimEnd('\r').StartsWith(variable + "=", StringComparison.Ordinal));

            foreach (string surface in (string[])
                ["orchestration/README.md", "docs/ARCHITECTURE.md", "docs/BUILD.md"])
            {
                Assert.Contains(variable, ReadRepositoryFile(surface), StringComparison.Ordinal);
            }
        }

        // AND THE MEASUREMENT ITSELF IS ON THE RECORD, on the surface an architect reads. Without it the
        // permissive default reads as carelessness, and the next reader's instinct is to "harden" it into
        // a posture that refuses every peer in the estate.
        string architecture = ReadRepositoryFile("docs/ARCHITECTURE.md");

        foreach (string evidence in (string[])
            ["RevocationStatusUnknown", "OfflineRevocation", "MaxCallerCertificateLifetimeDays"])
        {
            Assert.Contains(evidence, architecture, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Asserts that each service's container definition copies every project its application actually
    /// references, transitively.
    /// </summary>
    /// <param name="serviceKey">The service under test.</param>
    /// <remarks>
    /// <para>
    /// <b>⚠ THE DEFECT THIS ROW CATCHES IS INVISIBLE TO EVERY OTHER GUARD AND TO THE HOST BUILD - ONLY A
    /// REAL `docker build` SEES IT.</b> Let a service acquire a <c>ProjectReference</c> - say Persistence to
    /// <c>PowerFramework.Shared.Eventful</c> - without adding it to the <c>Dockerfile</c>'s two <c>COPY</c>
    /// lists, and the host build stays green, because the whole tree is present there and the reference
    /// resolves. The image build then fails with <c>CS0234</c>, "the type or namespace name 'Eventful'
    /// does not exist in the namespace 'PowerFramework.Shared'": every other guard passes, the whole
    /// solution builds, every test is green, and the service simply cannot be containerised.
    /// </para>
    /// <para>
    /// THE TWO HALVES ARE BOTH CORRECT IN ISOLATION, which is what makes it invisible: a project file
    /// declaring its references, and a Dockerfile declaring its build context. Nothing else compares them.
    /// This row is that comparison, and it derives the expected set from the PROJECT FILES rather than
    /// from a list written here, so a reference added tomorrow is covered without editing this suite.
    /// </para>
    /// <para>
    /// IT ASSERTS THE SOURCE COPY RATHER THAN THE MANIFEST COPY, because the manifest list exists only to
    /// cache the restore layer and its formatting varies - some entries wrap across two lines. The source
    /// list has one canonical form, <c>COPY &lt;dir&gt;/ &lt;dir&gt;/</c>, and it is the one whose absence
    /// makes the compile fail. A restore-only omission fails the build too, one layer earlier.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(AllServiceKeys))]
    public void EveryServiceContainerDefinitionCopiesItsWholeProjectReferenceClosure(string serviceKey)
    {
        (string _, string directoryName, string projectName) = Services.Single(service =>
            string.Equals(service.Key, serviceKey, StringComparison.Ordinal));

        string dockerfile = ReadRepositoryFile($"services/{directoryName}/Dockerfile");
        string applicationProject =
            $"services/{directoryName}/{projectName}/{projectName}.csproj";

        IReadOnlyCollection<string> closure = ProjectReferenceClosure(applicationProject);

        // REFUSE RATHER THAN PASS VACUOUSLY: every service references at least the shared kernel, so an
        // empty closure means the reader stopped working rather than that the graph is empty.
        Assert.NotEmpty(closure);

        foreach (string referenced in closure)
        {
            Assert.Contains($"COPY {referenced}/ {referenced}/", dockerfile, StringComparison.Ordinal);
        }
    }

    // ----------------------------------------------------------------------------------------------
    // 8. No surface claims this estate binds a cleartext listener, because none of them does
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Asserts that no describing surface asserts, as a fact about THIS repository, that a listener it
    /// binds is cleartext - measured against the settings files rather than taken on trust.
    /// </summary>
    /// <param name="surfacePath">The describing surface under test.</param>
    /// <remarks>
    /// <para>
    /// THE DRIFT THIS CATCHES WAS REAL AND IT WAS SELF-CONTRADICTORY WITHIN A SINGLE FILE.
    /// <c>services/security-service/Dockerfile</c> said in one place that its listener was "cleartext,
    /// like the other three" and that "both listeners are cleartext", and concluded from that a bash
    /// <c>/dev/tcp</c> probe would work - while three hundred lines away the same file correctly recorded
    /// that the listener is <c>https://+:5104</c>, that socket redirection therefore cannot probe it, and
    /// that the <c>HEALTHCHECK</c> uses <c>openssl s_client</c> for exactly that reason. An earlier
    /// revision genuinely did bind cleartext; the prose outlived it. A reader trusting the stale half
    /// would conclude the estate transmits bearer tokens and DataWindow rows in the clear.
    /// </para>
    /// <para>
    /// THE ASSERTION IS NARROW ON PURPOSE, because the overwhelming majority of the word "cleartext" in
    /// this tree is legitimate and must stay: every settings file, container definition and contract
    /// document explains what a cleartext listener WOULD have cost, which is precisely why none is
    /// declared. Only the small closed set of phrasings below asserts cleartext as a present fact, and
    /// each one was a real sentence here. Adding a phrasing to that set is how a future regression of the
    /// same kind gets caught.
    /// </para>
    /// <para>
    /// The guard is skipped entirely if the settings files ever DO bind a cleartext endpoint, so it can
    /// never force a document to lie: it constrains prose only for as long as the measurement supports it.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(AllDescribingSurfaces))]
    public void NoActiveSurfaceClaimsThisEstateBindsACleartextListener(string surfacePath)
    {
        IReadOnlyDictionary<int, BoundEndpoint> bound = LoadBoundEndpoints();

        // REFUSE TO PASS VACUOUSLY: an empty map would mean the settings reader broke, not that the
        // estate binds nothing.
        Assert.NotEmpty(bound);

        if (bound.Values.Any(static endpoint =>
                !string.Equals(endpoint.Scheme, "https", StringComparison.Ordinal)))
        {
            // Something really does bind cleartext. Prose describing that is accurate; assert nothing.
            return;
        }

        string text = ReadRepositoryFile(surfacePath);

        foreach (string claim in PresentTenseCleartextClaims)
        {
            Assert.DoesNotContain(claim, text, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ----------------------------------------------------------------------------------------------
    // 9. No surface claims the bring-up is unrunnable, now that it has been run
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Asserts that no describing surface tells a reader the documented Compose bring-up cannot be run,
    /// while the manifest that runs it is present in the tree.
    /// </summary>
    /// <param name="surfacePath">The describing surface under test.</param>
    /// <remarks>
    /// <para>
    /// This is the second half of the same defect as
    /// <see cref="NoActiveSurfaceClaimsAnOperationalArtifactIsAbsentWhileItExists"/> and it needs its own
    /// guard, because the two are spelled differently. The absence guard catches "the manifest does not
    /// exist"; it does not catch the CONCLUSION drawn from it - "nothing in this section can be run",
    /// "every docker compose command below will fail at the first line", "there is no way to bring a
    /// stack up today". Those sentences survived the manifest landing, and a reader who believes them
    /// never attempts the bring-up at all.
    /// </para>
    /// <para>
    /// Only present-tense impossibility is refused. A document may freely say that something has not been
    /// run, or that a run proves nothing about a deployed topology - both are still true here, and both
    /// are load-bearing honesty rather than staleness. The line this draws is between "unexercised" and
    /// "unrunnable".
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(AllDescribingSurfaces))]
    public void NoActiveSurfaceClaimsTheDocumentedBringUpCannotBeRun(string surfacePath)
    {
        string root = RequireRepositoryRoot();
        string manifest = Path.Combine(
            root,
            Path.Combine("orchestration", "docker-compose.yml"));

        if (!File.Exists(manifest))
        {
            // No manifest, so "it cannot be run" is accurate. Assert nothing.
            return;
        }

        string text = ReadRepositoryFile(surfacePath);

        foreach (string claim in PresentTenseUnrunnableClaims)
        {
            Assert.DoesNotContain(claim, text, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ----------------------------------------------------------------------------------------------
    // 10. The published count register agrees with the contracts it counts
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Asserts that every gRPC method count stated in <c>docs/CONTRACTS.md</c>'s contract register equals
    /// the method count the protocol definition actually declares.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE REGISTER IS PROSE THAT LOOKS LIKE DATA, WHICH IS THE DANGEROUS KIND. It publishes a per-contract
    /// count - "C-04 <c>ColumnExpressionService</c> | gRPC | 26 methods" - and nothing compared any of
    /// those numbers with a <c>.proto</c> file. The protocol side was already pinned
    /// (<c>ProtoDescriptorTests</c> asserts the same 26), so the two could disagree only by the DOCUMENT
    /// drifting, and a document that miscounts a published surface is exactly what sends an integrator
    /// looking for a method that is not there - or, worse, leaves them unaware of one that is.
    /// </para>
    /// <para>
    /// This closes the loop from the other end: the register is read, its numbers are extracted, and each
    /// is compared with the descriptor. The register's own introduction makes the point this enforces -
    /// that a hand-maintained list of a 26-method service is a list that silently falls behind the schema.
    /// </para>
    /// <para>
    /// The row pattern is anchored on the contract identifier and the service name together, so a table
    /// that renames a service, drops a row or reorders the columns fails to match and is caught by the
    /// completeness assertion at the end rather than passing vacuously.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheDocumentedContractCountRegisterAgreesWithEveryProtoService()
    {
        string register = ReadRepositoryFile("docs/CONTRACTS.md");
        int verified = 0;

        foreach ((string contractId, string serviceName) in DocumentedGrpcContracts)
        {
            Match row = Regex.Match(
                register,
                @"^\|\s*\*{0,2}" + Regex.Escape(contractId) + @"\*{0,2}\s*`"
                    + Regex.Escape(serviceName)
                    + @"`\s*\|\s*gRPC\s*\|\s*(?<count>\d+)\s+methods?\s*\|",
                RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture | RegexOptions.Multiline,
                TimeSpan.FromSeconds(5));

            Assert.True(
                row.Success,
                $"docs/CONTRACTS.md carries no register row for {contractId} '{serviceName}' in the "
                    + "expected `| C-nn `Service` | gRPC | N methods |` shape. The register is the "
                    + "published count for that contract; restore the row rather than deleting the "
                    + "assertion.");

            int documented = int.Parse(row.Groups["count"].Value, CultureInfo.InvariantCulture);
            int declared = ContractDescriptors.RequireService(serviceName).Methods.Count;

            Assert.Equal(documented, declared);
            verified++;
        }

        // REFUSE TO PASS VACUOUSLY: the roster is fixed at six gRPC contracts, so a reader that silently
        // matched nothing must fail rather than report success.
        Assert.Equal(DocumentedGrpcContracts.Length, verified);
    }

    // ----------------------------------------------------------------------------------------------
    // Helpers
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Reads the transitive <c>ProjectReference</c> closure of one project, as repository-relative
    /// directories with forward slashes.
    /// </summary>
    /// <param name="projectRelativePath">The starting project, repository-root-relative.</param>
    /// <returns>Every referenced project's directory, excluding the starting project's own.</returns>
    /// <remarks>
    /// READ WITH A REGULAR EXPRESSION RATHER THAN AN XML PARSER, deliberately and on the same terms as
    /// every other reader in this suite: the shape is one attribute on one element, the project files are
    /// authored by this refactor, and an XML load would have to reason about MSBuild conditions and
    /// imports to be more correct than this is. Paths are normalised through <see cref="Path"/> so a
    /// <c>..</c> segment resolves rather than being compared literally.
    /// </remarks>
    private static IReadOnlyCollection<string> ProjectReferenceClosure(string projectRelativePath)
    {
        string root = RequireRepositoryRoot();
        HashSet<string> discovered = new(StringComparer.Ordinal);
        Queue<string> pending = new();
        pending.Enqueue(projectRelativePath);

        while (pending.Count > 0)
        {
            string current = pending.Dequeue();
            string currentDirectory = Path.GetDirectoryName(current) ?? string.Empty;
            string text = ReadRepositoryFile(current);

            foreach (Match match in ProjectReferencePattern.Matches(text))
            {
                string referenced = match.Groups["path"].Value.Replace('\\', '/');

                // Resolved against the referencing project's directory, then made repository-relative
                // again, so a `../../../shared/...` reference compares as `shared/...`.
                string absolute = Path.GetFullPath(Path.Combine(root, currentDirectory, referenced));
                string relative = Path.GetRelativePath(root, absolute).Replace('\\', '/');
                string directory = Path.GetDirectoryName(relative)?.Replace('\\', '/') ?? string.Empty;

                if (directory.Length > 0 && discovered.Add(directory))
                {
                    pending.Enqueue(relative);
                }
            }
        }

        return discovered;
    }

    /// <summary>One <c>ProjectReference</c> element's <c>Include</c> attribute.</summary>
    private static readonly Regex ProjectReferencePattern = new(
        @"ProjectReference\s+Include=""(?<path>[^""]+)""",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        TimeSpan.FromSeconds(5));

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

        // Both rosters participate in the nearest-subject search, but only a name from the ARTIFACT
        // roster can make this return true - a non-artifact subject winning the search is precisely the
        // signal that the marker belongs to something this suite does not assert about.
        foreach (string candidate in ClaimableArtifactNames.Concat(NonArtifactAbsenceSubjects))
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
        // STARTS AT THE EMBEDDED REPOSITORY ROOT WHEN THE BUILD SUPPLIED ONE, so this locator works when
        // the test output sits outside the checkout - `dotnet test --artifacts-path` - where no ancestor
        // of the output directory carries the marker below. The walk itself is unchanged and still
        // verifies that marker, so an absent or stale value simply falls back to the previous start.
        // See TestRepositoryRoot.
        DirectoryInfo? candidate = new(TestRepositoryRoot.SearchStart);

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
