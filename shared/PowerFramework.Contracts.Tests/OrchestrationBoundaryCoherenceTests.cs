// ======================================================================================================
//  OrchestrationBoundaryCoherenceTests.cs - WHAT THE LOCAL STACK OFFERS, AND WHAT IT CARRIES IN THE CLEAR
//  ------------------------------------------------------------------------------------------------------
//  WHAT THESE ROWS PROTECT
//  Three properties of the orchestration manifest, each of which was wrong in a way that read as a
//  default rather than as a decision:
//
//    1. WHICH HOST INTERFACE EACH PUBLISHED PORT IS OFFERED ON. A two-field `ports:` mapping binds the
//       host half to 0.0.0.0, so all four listeners were offered on EVERY interface of the machine
//       running the stack. Any other host on the same LAN, cafe wifi or cloud subnet could reach
//       Persistence, DataServices and Security DIRECTLY, going around Gateway - which defeats the
//       sole-ingress topology at the network layer however correct the call graph above it is.
//       Persistence is the sharpest case: it is the only service holding a storage provider and sits two
//       hops behind the ingress by design.
//
//    2. WHETHER TWO SERVICES SHARE A CRYPTOGRAPHIC IDENTITY. That property is asserted in full by
//       OperationalTopologyCoherenceTests, which owns the TLS projection; this file does not restate it.
//
//    3. WHETHER A SECRET IS CARRIED IN AN ENVIRONMENT VARIABLE. `docker compose config` renders the
//       environment in cleartext, and it is the command an operator is told to run when a bring-up
//       misbehaves - so the natural debugging step printed the RSA private key that signs every token in
//       the estate. `docker inspect` returns the same block to anyone who can reach the daemon socket,
//       `/proc/<pid>/environ` exposes it to any process under the same account, and every child process
//       inherits it. A projected file has none of those properties.
//
//  WHY A MANIFEST-LEVEL ROW RATHER THAN A SERVICE TEST
//  None of these is visible from inside a service. A service cannot tell which host interface its port was
//  published on, and it cannot tell whether the value it read came from an environment variable or a file
//  - by design, since that indifference is what lets the environment form keep working. The manifest is
//  the only place the property exists, so it is the only place it can be checked.
// ======================================================================================================

using Xunit;

namespace PowerFramework.Contracts.Tests;

/// <summary>
/// Covers the orchestration manifest's exposure boundary: which interface each published port is offered
/// on, and that no secret is carried in an environment variable.
/// </summary>
public sealed class OrchestrationBoundaryCoherenceTests
{
    /// <summary>The manifest under test, repository-root-relative.</summary>
    private const string ManifestPath = "orchestration/docker-compose.yml";

    /// <summary>The environment template, repository-root-relative.</summary>
    private const string TemplatePath = "orchestration/.env.example";

    /// <summary>The four services, by the variable prefix each uses.</summary>
    private static readonly string[] ServicePrefixes =
        ["GATEWAY", "SECURITY", "PERSISTENCE", "DATASERVICES"];

    [Fact]
    public void EveryPublishedPortNamesTheHostInterfaceItBindsRatherThanDefaultingToAllOfThem()
    {
        string manifest = ReadRepositoryFile(ManifestPath);

        foreach (string prefix in ServicePrefixes)
        {
            // THE THREE-FIELD FORM IS THE ASSERTION. `"<bind>:<host>:<container>"` names the interface;
            // the two-field `"<host>:<container>"` silently means every interface. Pinning the shape is
            // what makes a regression to the two-field form fail here rather than pass as a tidy-up.
            string expected = $"\"${{{prefix}_HOST_BIND:-127.0.0.1}}:${{{prefix}_HOST_PORT:";

            Assert.Contains(
                expected,
                manifest,
                StringComparison.Ordinal);
        }

        // AND NO MAPPING ANYWHERE PUBLISHES ON EVERY INTERFACE BY DEFAULT. Scanned independently of the
        // four expectations above, so a FIFTH published port added later cannot slip in unbound.
        foreach (string line in manifest.Split('\n'))
        {
            string trimmed = line.TrimEnd('\r').Trim();

            if (!trimmed.StartsWith("- \"", StringComparison.Ordinal)
                || !trimmed.EndsWith('"')
                || trimmed.Contains("_HOST_BIND", StringComparison.Ordinal))
            {
                continue;
            }

            // A published-port entry is a quoted scalar in a `ports:` sequence. Two colon-separated
            // fields means the host interface was left unnamed.
            string value = trimmed[3..^1];

            Assert.False(
                value.Count(static character => character == ':') == 1,
                $"'{ManifestPath}' publishes '{value}' without naming a host interface. A two-field "
                    + "mapping binds the host half to 0.0.0.0, which offers the listener on every "
                    + "interface of the machine running the stack - so an internal service becomes "
                    + "reachable directly, going around Gateway and defeating the sole-ingress topology "
                    + "at the network layer.");
        }
    }

    [Fact]
    public void EveryHostBindVariableIsDeclaredInTheTemplateAndDefaultsToLoopback()
    {
        string[] template = ReadRepositoryFile(TemplatePath).Split('\n');

        foreach (string prefix in ServicePrefixes)
        {
            string variable = $"{prefix}_HOST_BIND";

            // ASSIGNABLE IN THE TEMPLATE, not merely mentioned in its prose - an operator who cannot see
            // the variable cannot widen it deliberately, and the whole point of the default is that
            // widening should be a deliberate act rather than the status quo.
            string? declared = template
                .Select(static line => line.TrimEnd('\r'))
                .FirstOrDefault(line => line.StartsWith(variable + "=", StringComparison.Ordinal));

            Assert.NotNull(declared);

            // GATEWAY IS NOT EXCEPTED, AND THAT IS THE JUDGEMENT THIS ROW RECORDS. "Sole ingress" says
            // which service traffic SHOULD enter through, not that it should be exposed to the network
            // before a deployment asks. Every access route the documentation publishes is a localhost one
            // - the readiness gates, the composition-root URL, and the end-to-end suite's base URLs - so
            // loopback satisfies all of them while offering nothing to the network.
            Assert.Equal(variable + "=127.0.0.1", declared);
        }
    }

    [Fact]
    public void NoSecretIsCarriedInAnEnvironmentValueInTheManifest()
    {
        string[] lines = ReadRepositoryFile(ManifestPath).Split('\n');
        List<string> offenders = [];

        foreach (string raw in lines)
        {
            string line = raw.TrimEnd('\r').Trim();

            if (line.StartsWith('#') || !line.Contains(':', StringComparison.Ordinal))
            {
                continue;
            }

            string key = line[..line.IndexOf(':', StringComparison.Ordinal)].Trim();

            // The keys that name secret MATERIAL rather than a path, an identifier or a file reference.
            bool namesMaterial =
                (key.Contains("SIGNING_KEY", StringComparison.Ordinal)
                    || key.Contains("CLIENT_SECRET", StringComparison.Ordinal))
                && !key.EndsWith("_FILE", StringComparison.Ordinal)
                && !key.EndsWith("_PATH", StringComparison.Ordinal)
                && !key.EndsWith("_ID", StringComparison.Ordinal)
                && !key.EndsWith("KeyId", StringComparison.Ordinal)
                && !key.Contains("ConfigurationKey", StringComparison.Ordinal);

            if (!namesMaterial)
            {
                continue;
            }

            // THE TWO OPTIONAL SLOTS ARE THE ONE PERMITTED EXCEPTION, AND THEY ARE PERMITTED FOR A
            // STRUCTURAL REASON RATHER THAN A PRACTICAL ONE. A Compose secret's `file:` must name a path
            // that already exists, and both of these are EMPTY in the steady state - the retiring signing
            // slot carries material only during a rollover, and the operator identity ships unused. So
            // declaring a projection for either would abort every ordinary bring-up to serve a case that
            // is not happening. Both instead carry a `_FILE` companion that is passed through and
            // documented as the preferred form whenever the slot is actually in use, which is why the row
            // below requires that companion to exist rather than taking the exception on trust.
            if (key is "SECURITY_JWT_RETIRING_SIGNING_KEY" or "SECURITY_CLIENT_SECRET")
            {
                Assert.Contains(
                    key + "_FILE:",
                    string.Join('\n', lines),
                    StringComparison.Ordinal);

                continue;
            }

            offenders.Add(key);
        }

        Assert.True(
            offenders.Count == 0,
            $"'{ManifestPath}' carries secret material in {string.Join(", ", offenders)} as an "
                + "environment VALUE. `docker compose config` renders the environment in cleartext and is "
                + "the command an operator runs when a bring-up misbehaves, `docker inspect` returns it to "
                + "anyone who can reach the daemon socket, `/proc/<pid>/environ` exposes it to any process "
                + "under the same account, and every child process inherits it. Project the material as a "
                + "file and reference it through the `<KEY>_FILE` convention instead.");
    }

    [Fact]
    public void TheFileReferencesResolveToProjectedSecretTargets()
    {
        string manifest = ReadRepositoryFile(ManifestPath);

        // EVERY `_FILE` REFERENCE THE MANIFEST STATES MUST POINT AT SOMETHING IT ACTUALLY PROJECTS.
        // A reference to a path nothing grants is the failure mode the resolver refuses at startup, and
        // catching it here is cheaper than catching it in a bring-up - especially since the refusal is
        // deliberately indistinguishable from a mis-granted projection.
        (string Reference, string Target)[] expectations =
        [
            ("SECURITY_JWT_SIGNING_KEY_FILE", "security/jwt-signing-key"),
            ("SECURITY_CLIENT_SECRET_GATEWAY_FILE", "security/client-secret-gateway"),
            ("SECURITY_CLIENT_SECRET_DATASERVICES_FILE", "security/client-secret-dataservices"),
        ];

        foreach ((string reference, string target) in expectations)
        {
            Assert.Contains(
                $"{reference}: /run/secrets/{target}",
                manifest,
                StringComparison.Ordinal);

            Assert.Contains(
                $"target: {target}",
                manifest,
                StringComparison.Ordinal);
        }
    }

    /// <summary>Reads a repository-root-relative file.</summary>
    /// <param name="relativePath">The path, relative to the repository root.</param>
    /// <returns>The file's text.</returns>
    private static string ReadRepositoryFile(string relativePath)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null
            && !File.Exists(Path.Combine(directory.FullName, "PowerFramework.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);

        string absolute = Path.Combine(directory.FullName, relativePath);

        Assert.True(File.Exists(absolute), $"'{relativePath}' was not found at '{absolute}'.");

        return File.ReadAllText(absolute);
    }
}
