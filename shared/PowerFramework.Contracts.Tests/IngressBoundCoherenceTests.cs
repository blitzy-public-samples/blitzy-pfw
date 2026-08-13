// =====================================================================================================
//  EVERY SERVICE BOUNDS ITS OWN INGRESS, AND NO FUTURE ONE CAN SHIP WITHOUT DOING SO
//
//  WHAT THIS GUARDS THAT A PER-SERVICE SUITE CANNOT
//  Each service's own suite proves that ITS host applies ITS bounds. None of them can prove that the
//  next service to be added does, and that is exactly how this class of control decays: the estate
//  acquires a fifth listener, nobody remembers the registration, and the gap is invisible because every
//  existing suite still passes. This file walks the service tree itself, so a new service directory is
//  discovered rather than enumerated - a service added tomorrow is held to the same three requirements
//  without anyone extending a list.
//
//  THE THREE REQUIREMENTS, AND WHY EACH IS SEPARATELY NECESSARY
//    1. THE OPTIONS TYPE SUPPRESSES THE SERVER HEADER AND APPLIES A CEILING TO EVERY LISTENER LIMIT IT
//       DECLARES. Kestrel announces its implementation on every response, including the ones no
//       application code composes, which tells an unauthenticated caller what to look advisories up for
//       (CWE-200). And its own defaults are either far too generous for an internal mesh or absent
//       altogether: 30,000,000 bytes of request body, and no connection ceiling whatsoever.
//    2. THE COMPOSITION ROOT REGISTERS IT, IN BOTH HALVES. The transport half configures the listener
//       before the host is built; the request half installs the limiter in the pipeline. A service that
//       declared the options and called neither would satisfy requirement 1 and bound nothing.
//    3. THE SETTINGS FILE DECLARES THE VALUES. A bound left to a code default is a bound an operator
//       cannot audit, and these are the settings a deployment most legitimately has to tune - one too
//       low refuses correct callers, one too high delays the discovery of an abuse.
//
//  BOUNDING A NEWLY CREATED INGRESS IS REQUIRED BY THE DECOMPOSITION (constraint C-B). The legacy was a
//  library with no listener [Agent Action Plan 0.1.4]: no legacy path could be flooded by a stranger, so
//  there is no legacy limit to port and nothing here preserves or alters a legacy behaviour. It answers a
//  failure mode the transition itself introduced, which is the standing outbound resilience has (0.5.3).
//
//  NOTHING HERE ASSERTS A PERFORMANCE PROPERTY. The repository publishes no latency budget, no throughput
//  target and no availability commitment (AAP 0.8.5), so no row claims a value is large or small enough -
//  only that a bound exists, is declared, and is the one the host applies.
//
//  RULES POSITION. No user rules were provided; nothing is invented in their place. The governing
//  constraints are C-B, C-F, C-I and C-K, cited where they apply.
// =====================================================================================================

using System.Text.Json;
using System.Text.Json.Nodes;

using Xunit;

namespace PowerFramework.Contracts.Tests;

/// <summary>
/// Every in-scope service bounds its own ingress, declares those bounds, and wires them into its host.
/// </summary>
public sealed class IngressBoundCoherenceTests
{
    /// <summary>The file that identifies the repository root.</summary>
    private const string RepositoryRootMarker = "PowerFramework.slnx";

    /// <summary>The directory every service project lives under.</summary>
    private const string ServicesDirectory = "services";

    /// <summary>The settings section the bounds are declared in, in every service.</summary>
    /// <remarks>
    /// TOP LEVEL RATHER THAN UNDER EACH SERVICE'S OWN SECTION, and uniformly so. Two of the four services
    /// pin the member set of their own section closed - one against an authoritative list here, one
    /// against a total census in its own suite that resolves every declared leaf to a bound property - so
    /// a separate bindable root nested under those prefixes would be reported as an orphan. One spelling
    /// everywhere is also what lets this file walk the tree instead of carrying a per-service map.
    /// </remarks>
    private const string IngressSection = "Ingress";

    /// <summary>The bounds every service declares, whatever its transport.</summary>
    private static readonly string[] UniversalBounds =
    [
        "MaxRequestBodyBytes",
        "MaxRequestHeadersTotalBytes",
        "MaxConcurrentConnections",
        "MaxHttp2StreamsPerConnection",
        "RequestHeadersTimeoutSeconds",
        "MaxConcurrentRequests",
        "RateLimitPermitsPerWindow",
        "RateLimitWindowSeconds",
        "RateLimitQueueLimit",
    ];

    /// <summary>The two additional bounds a service that SERVES gRPC declares.</summary>
    /// <remarks>
    /// Held apart from the universal set because they are transport-specific and their absence is
    /// meaningful: a service publishing no gRPC contract that declared them would be describing a
    /// transport it does not serve, which reads to an operator as a surface that exists.
    /// </remarks>
    private static readonly string[] GrpcBounds =
    [
        "MaxReceiveMessageBytes",
        "MaxSendMessageBytes",
    ];

    /// <summary>Every service directory under <c>services/</c>, discovered rather than enumerated.</summary>
    /// <returns>One row per service directory.</returns>
    public static TheoryData<string> EveryServiceDirectory()
    {
        TheoryData<string> rows = [];

        foreach (string directory in Directory
            .EnumerateDirectories(Path.Combine(RepositoryRoot(), ServicesDirectory))
            .Order(StringComparer.Ordinal))
        {
            rows.Add(Path.GetFileName(directory));
        }

        return rows;
    }

    /// <summary>
    /// The estate has exactly the four in-scope services, and the walk below therefore covers all of them.
    /// </summary>
    /// <remarks>
    /// A GUARD ON THE GUARD, and it carries a second meaning worth stating. An empty or partial walk would
    /// make every row below vacuously true, which is the quiet way a discovered-rather-than-enumerated
    /// assertion stops asserting. It also pins the Phase-1 roster: the four deferred services receive no
    /// project, no container and no test (constraint C-D), so a fifth directory here is either a scope
    /// breach or a deliberate later phase, and both deserve to stop a build rather than pass silently.
    /// </remarks>
    [Fact]
    public void TheWalkCoversExactlyTheFourInScopeServices()
    {
        string[] discovered =
        [
            .. Directory
                .EnumerateDirectories(Path.Combine(RepositoryRoot(), ServicesDirectory))
                .Select(Path.GetFileName)
                .OfType<string>()
                .Order(StringComparer.Ordinal),
        ];

        Assert.Equal(
            [
                "dataservices-service",
                "gateway-service",
                "persistence-service",
                "security-service",
            ],
            discovered);
    }

    /// <summary>
    /// Every service declares an ingress options type that suppresses the server header.
    /// </summary>
    /// <param name="serviceDirectory">The service directory under <c>services/</c>.</param>
    /// <remarks>
    /// Read from source rather than by reflection, for the reason the estate's other architectural
    /// assertions read source: this project references no service assembly - it must not, because each
    /// service builds independently from a clean checkout (constraint C-I) - and naming the FILE a
    /// reviewer would open is what makes a failure actionable.
    /// </remarks>
    [Theory]
    [MemberData(nameof(EveryServiceDirectory))]
    public void EveryServiceSuppressesTheServerHeader(string serviceDirectory)
    {
        string options = ReadIngressOptions(serviceDirectory);

        Assert.Contains("AddServerHeader = false", options, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every service applies a ceiling to every listener limit the framework leaves open.
    /// </summary>
    /// <param name="serviceDirectory">The service directory under <c>services/</c>.</param>
    /// <remarks>
    /// The upgraded-connection ceiling is required alongside the ordinary one because a connection that
    /// upgrades escapes the first and would otherwise be unbounded - which is the specific hole a reader
    /// checking only <c>MaxConcurrentConnections</c> would not notice.
    /// </remarks>
    [Theory]
    [MemberData(nameof(EveryServiceDirectory))]
    public void EveryServiceBoundsEveryListenerLimit(string serviceDirectory)
    {
        string options = ReadIngressOptions(serviceDirectory);

        foreach (string limit in (string[])
            [
                "Limits.MaxRequestBodySize",
                "Limits.MaxRequestHeadersTotalSize",
                "Limits.MaxConcurrentConnections",
                "Limits.MaxConcurrentUpgradedConnections",
                "Limits.RequestHeadersTimeout",
                "Limits.Http2.MaxStreamsPerConnection",
            ])
        {
            Assert.Contains(limit, options, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Every service's composition root registers both halves of the bound.
    /// </summary>
    /// <param name="serviceDirectory">The service directory under <c>services/</c>.</param>
    /// <remarks>
    /// BOTH HALVES, because either alone bounds nothing that matters. Without the registration the
    /// listener keeps the framework's own limits and announces itself; without the pipeline call the
    /// request-layer limiter is configured and never consulted. A service with one of the two is the
    /// hardest case to spot by reading, which is why it is asserted rather than assumed.
    /// </remarks>
    [Theory]
    [MemberData(nameof(EveryServiceDirectory))]
    public void EveryCompositionRootRegistersBothHalves(string serviceDirectory)
    {
        string program = File.ReadAllText(
            Path.Combine(ApplicationProjectDirectory(serviceDirectory), "Program.cs"));

        Assert.Contains("AddIngressHardening()", program, StringComparison.Ordinal);
        Assert.Contains("UseIngressHardening()", program, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every service declares its bounds in the settings file an operator reads.
    /// </summary>
    /// <param name="serviceDirectory">The service directory under <c>services/</c>.</param>
    /// <remarks>
    /// A CLOSED SET RATHER THAN A SUBSET, in both directions. A missing bound is a value only source
    /// discloses; an extra one is a key the options type does not read, which the configuration binder
    /// ignores in silence - the document says one thing and the service runs on another, and nothing
    /// throws. The gRPC pair is required of exactly the services that serve gRPC and forbidden of the
    /// others, which is what stops a REST-only service describing a transport it does not have.
    /// </remarks>
    [Theory]
    [MemberData(nameof(EveryServiceDirectory))]
    public void EveryServiceDeclaresItsBoundsInItsSettingsFile(string serviceDirectory)
    {
        string projectDirectory = ApplicationProjectDirectory(serviceDirectory);
        JsonObject settings = LoadSettings(Path.Combine(projectDirectory, "appsettings.json"));

        JsonNode? section = settings[IngressSection];

        Assert.NotNull(section);

        JsonObject bounds = Assert.IsType<JsonObject>(section);

        bool servesGrpc = ServesGrpc(projectDirectory);

        string[] expected =
        [
            .. servesGrpc ? [.. UniversalBounds, .. GrpcBounds] : UniversalBounds,
        ];

        Assert.Equal(
            [.. expected.Order(StringComparer.Ordinal)],
            [.. bounds.Select(static leaf => leaf.Key).Order(StringComparer.Ordinal)]);

        // AND EVERY ONE OF THEM CARRIES A NUMBER. A bound declared as a string binds to nothing on an
        // integer property and leaves the compiled default in place, which is the silent half of the
        // failure this row exists to catch.
        foreach ((string key, JsonNode? value) in bounds)
        {
            Assert.True(
                value is JsonValue declared && declared.GetValueKind() == JsonValueKind.Number,
                $"{serviceDirectory} declares '{IngressSection}:{key}' as something other than a number, "
                    + "so the binder cannot land it on the option and the compiled default silently "
                    + "stands.");
        }
    }

    /// <summary>
    /// A service that serves gRPC bounds both message directions.
    /// </summary>
    /// <param name="serviceDirectory">The service directory under <c>services/</c>.</param>
    /// <remarks>
    /// The SEND direction is the one that was genuinely unbounded - the framework's receive default is
    /// 4 MiB and its send default is unlimited - so a service that set only the receive ceiling would look
    /// bounded and serialise an arbitrarily large result into memory before the transport could push back.
    /// Skipped rather than failed for a service that serves no gRPC, because the requirement does not
    /// apply to it and a skip says so more honestly than a vacuous pass.
    /// </remarks>
    [Theory]
    [MemberData(nameof(EveryServiceDirectory))]
    public void EveryGrpcServerBoundsBothMessageDirections(string serviceDirectory)
    {
        string projectDirectory = ApplicationProjectDirectory(serviceDirectory);

        if (!ServesGrpc(projectDirectory))
        {
            return;
        }

        string program = File.ReadAllText(Path.Combine(projectDirectory, "Program.cs"));

        Assert.Contains("MaxReceiveMessageSize", program, StringComparison.Ordinal);
        Assert.Contains("MaxSendMessageSize", program, StringComparison.Ordinal);

        // AND THE REFUSAL TRAVELS AS A gRPC STATUS. A 429 with no status trailer is not something a
        // conforming client can classify, so a gRPC surface bounded only by the HTTP middleware would
        // answer an opaque transport fault. RESOURCE_EXHAUSTED is the canonical status for a bound met.
        string limiter = File.ReadAllText(
            Path.Combine(projectDirectory, "Grpc", "GrpcIngressLimit.cs"));

        Assert.Contains("StatusCode.ResourceExhausted", limiter, StringComparison.Ordinal);
    }

    /// <summary>Reads a service's ingress options source.</summary>
    /// <param name="serviceDirectory">The service directory under <c>services/</c>.</param>
    /// <returns>The file's text.</returns>
    private static string ReadIngressOptions(string serviceDirectory) => File.ReadAllText(
        Path.Combine(
            ApplicationProjectDirectory(serviceDirectory),
            "Configuration",
            "IngressOptions.cs"));

    /// <summary>
    /// Reports whether a service hosts gRPC services of its own.
    /// </summary>
    /// <param name="projectDirectory">The application project directory.</param>
    /// <returns><see langword="true"/> when the service maps at least one gRPC service.</returns>
    /// <remarks>
    /// <para>
    /// Decided from the composition root's own registration rather than from a list here, so the answer
    /// cannot disagree with what the service actually serves. The REST-only ingress consumes two gRPC
    /// contracts as a CLIENT and hosts none, and a client is bounded by the server that answers it.
    /// </para>
    /// <para>
    /// COMMENT LINES ARE SKIPPED, AND THE FIRST DRAFT OF THIS METHOD PROVED WHY. The REST-only ingress's
    /// composition root explains in a comment that it maps no gRPC service at all - so a plain substring
    /// search over the whole file found the registration's NAME inside the sentence denying it, classified
    /// that service as a gRPC server, and demanded two message ceilings for a transport it does not serve.
    /// Reading only code lines is what makes the answer the code's rather than the prose's.
    /// </para>
    /// </remarks>
    private static bool ServesGrpc(string projectDirectory)
    {
        foreach (string line in File.ReadLines(Path.Combine(projectDirectory, "Program.cs")))
        {
            string code = line.TrimStart();

            if (code.StartsWith("//", StringComparison.Ordinal)
                || code.StartsWith('*')
                || code.StartsWith("/*", StringComparison.Ordinal))
            {
                continue;
            }

            if (code.Contains("MapGrpcService", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Resolves a service's single application project directory.
    /// </summary>
    /// <param name="serviceDirectory">The service directory under <c>services/</c>.</param>
    /// <returns>The absolute directory path.</returns>
    /// <remarks>
    /// Discovered by looking for the one project directory that carries a composition root, rather than by
    /// deriving a name: the test project sits beside the application project under the same service
    /// directory and a name-shaped guess would have to encode the estate's naming convention here as well
    /// as in the project files.
    /// </remarks>
    private static string ApplicationProjectDirectory(string serviceDirectory)
    {
        string root = Path.Combine(RepositoryRoot(), ServicesDirectory, serviceDirectory);

        string[] candidates =
        [
            .. Directory
                .EnumerateDirectories(root)
                .Where(static directory => File.Exists(Path.Combine(directory, "Program.cs")))
                .Order(StringComparer.Ordinal),
        ];

        Assert.Single(candidates);

        return candidates[0];
    }

    /// <summary>
    /// Loads a commented settings document exactly as the configuration provider parses it.
    /// </summary>
    /// <param name="path">The document's path.</param>
    /// <returns>The parsed root object.</returns>
    /// <remarks>
    /// Comments are skipped and trailing commas allowed, which is the JSON configuration provider's own
    /// reader configuration - so what this row asserts about is the document the deployed host reads
    /// rather than an approximation of it.
    /// </remarks>
    private static JsonObject LoadSettings(string path)
    {
        JsonNode? parsed = JsonNode.Parse(
            File.ReadAllText(path),
            documentOptions: new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });

        return Assert.IsType<JsonObject>(parsed);
    }

    /// <summary>
    /// Walks up from the test output to the repository root.
    /// </summary>
    /// <returns>The absolute repository root path.</returns>
    /// <remarks>
    /// Anchored on <see cref="TestRepositoryRoot.SearchStart"/> so it also works when the test output sits
    /// outside the tree under an artifacts path. The root is used only as an anchor: every path resolved
    /// from it descends into the service tree this project already reads.
    /// </remarks>
    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(TestRepositoryRoot.SearchStart);

        while (directory is not null
            && !File.Exists(Path.Combine(directory.FullName, RepositoryRootMarker)))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);

        return directory.FullName;
    }
}
