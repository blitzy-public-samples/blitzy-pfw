// ==================================================================================================
//  FILE-BACKED SECRET MATERIAL - THE `<KEY>_FILE` CONVENTION
//
//  WHY THIS EXISTS. Every secret this service consumes arrived as an ENVIRONMENT VARIABLE, and an
//  environment variable carrying key material is readable from more places than its owner expects:
//
//    * `docker compose config` renders it in cleartext, and that command is what an operator is told
//      to run when a bring-up misbehaves - so the natural debugging step prints the signing key.
//    * `docker inspect <container>` returns the full environment to anyone who can reach the daemon
//      socket, and so does the API behind it.
//    * `/proc/<pid>/environ` exposes it to any process in the container running as the same user, and
//      a child process inherits the whole block whether it needs it or not.
//    * It lands in shell history, in CI job logs that echo the environment, and in a crash dump.
//
//  A projected FILE has none of those properties: it is one path, readable by one account, and it does
//  not appear in a rendered manifest, an inspect payload or a process environment block.
//
//  WHAT THIS DOES NOT DO, WHICH IS THE POINT OF THE DESIGN. It reads no configuration schema, knows no
//  key names of its own, and cannot invent a secret. It takes the key names this service ALREADY
//  declares it reads secrets from - the same "a NAME, not a value" indirection the options types use -
//  and, for each, honours a sibling `<KEY>_FILE`. The environment-variable form keeps working
//  unchanged, so the documented bring-up is not broken by this being here.
//
//  THREE RULES, AND EACH REFUSES RATHER THAN GUESSES.
//    1. `<KEY>_FILE` set and readable  ->  its trimmed content becomes `<KEY>`.
//    2. `<KEY>` and `<KEY>_FILE` BOTH carry a value  ->  REFUSE. Two sources for one secret means the
//       operator believes one is in effect and cannot tell which; silently preferring either is how a
//       credential rotation appears to work while the old value is still being presented.
//    3. `<KEY>_FILE` set but missing, unreadable, or empty once trimmed  ->  REFUSE. Falling back to
//       the environment value would substitute a DIFFERENT credential for the one the operator
//       deployed, which is worse than not starting.
//
//  Refusal is `InvalidOperationException` from the composition root, which is this service's
//  established posture for a structural fault [ws_objects/pfw.pbl.src/pfw.sra:L111-L144, HALT CLOSE at
//  :L143] - the same treatment absent or unusable signing material already receives.
//
//  NO SECRET VALUE APPEARS IN ANY MESSAGE THIS FILE PRODUCES. Failures name the KEY, the FILE PATH and
//  the condition. A path is not material, and an operator cannot fix a misconfiguration they cannot
//  locate; the value itself is never quoted, never logged and never length-reported.
// ==================================================================================================

using Microsoft.Extensions.Configuration;

namespace PowerFramework.Security.Configuration;

/// <summary>
/// Resolves secret configuration values from projected files using the <c>&lt;KEY&gt;_FILE</c>
/// convention, so no secret has to be carried in an environment variable.
/// </summary>
internal static class FileBackedSecrets
{
    /// <summary>
    /// The suffix that names the file-backed companion of a secret-bearing configuration key.
    /// </summary>
    /// <remarks>
    /// The `_FILE` spelling is the de-facto convention across container images that accept secrets this
    /// way, so an operator who has met it elsewhere needs no new vocabulary here.
    /// </remarks>
    internal const string FileSuffix = "_FILE";

    /// <summary>
    /// Adds the file-backed secret values to the builder's configuration, overriding the
    /// environment-variable form for every key that names a readable file.
    /// </summary>
    /// <param name="builder">The host builder whose configuration is extended.</param>
    /// <returns>The same builder, for chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// A key declares both an inline value and a file, or names a file that cannot be read.
    /// </exception>
    internal static WebApplicationBuilder AddFileBackedSecrets(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        IReadOnlyDictionary<string, string> resolved = Resolve(
            builder.Configuration,
            DeclaredSecretKeys(builder.Configuration),
            File.ReadAllText,
            File.Exists);

        if (resolved.Count > 0)
        {
            // LAST SOURCE WINS in the configuration chain, which is what lets a projected file override
            // an environment variable of the same name. Rule 2 above means the two never actually
            // disagree - a conflict has already been refused by this point - so the precedence matters
            // only for the case where a deployment sets the file form alone.
            _ = builder.Configuration.AddInMemoryCollection(
                resolved.Select(static entry => new KeyValuePair<string, string?>(entry.Key, entry.Value)));
        }

        return builder;
    }

    /// <summary>
    /// Enumerates the configuration keys this service reads secret material from.
    /// </summary>
    /// <param name="configuration">The configuration to read client declarations from.</param>
    /// <returns>The declared secret-bearing key names, without duplicates.</returns>
    /// <remarks>
    /// <para>
    /// THE LIST IS DERIVED, NOT RESTATED. The two signing-key names are the constants the options type
    /// already publishes, and the client-secret names are read from
    /// <c>Security:Clients[n]:SecretConfigurationKey</c> - which is where an operator declares them, so
    /// a client added to the roster becomes file-backable with no change here. A hand-maintained list
    /// would drift away from the roster silently, and the failure would be a secret that quietly kept
    /// using its environment-variable form after an operator had moved it to a file.
    /// </para>
    /// <para>
    /// The enumeration deliberately does NOT scan for arbitrary <c>*_FILE</c> variables. Several
    /// unrelated ones exist in a normal container environment - <c>SSL_CERT_FILE</c> being the common
    /// example - and treating any of them as a secret declaration would both mis-read the environment
    /// and give an attacker a way to inject a configuration value by naming a file.
    /// </para>
    /// </remarks>
    internal static IEnumerable<string> DeclaredSecretKeys(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        HashSet<string> keys = new(StringComparer.Ordinal)
        {
            SecurityOptions.SigningKeyEnvironmentVariableName,
            SecurityOptions.RetiringSigningKeyEnvironmentVariableName,
        };

        foreach (IConfigurationSection client in
            configuration.GetSection($"{SecurityOptions.SectionName}:Clients").GetChildren())
        {
            string? declared = client["SecretConfigurationKey"];

            if (!string.IsNullOrWhiteSpace(declared))
            {
                _ = keys.Add(declared);
            }
        }

        return keys;
    }

    /// <summary>
    /// Resolves the file-backed value of every declared secret key that names one.
    /// </summary>
    /// <param name="configuration">The configuration carrying the inline and file-path values.</param>
    /// <param name="declaredKeys">The secret-bearing key names to consider.</param>
    /// <param name="readFile">Reads a file's full text. Injected so the rules are testable.</param>
    /// <param name="fileExists">Reports whether a path exists. Injected so the rules are testable.</param>
    /// <returns>The resolved values, keyed by the configuration key they satisfy.</returns>
    /// <exception cref="InvalidOperationException">
    /// A key declares both an inline value and a file, or names a file that cannot be read.
    /// </exception>
    internal static IReadOnlyDictionary<string, string> Resolve(
        IConfiguration configuration,
        IEnumerable<string> declaredKeys,
        Func<string, string> readFile,
        Func<string, bool> fileExists)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(declaredKeys);
        ArgumentNullException.ThrowIfNull(readFile);
        ArgumentNullException.ThrowIfNull(fileExists);

        Dictionary<string, string> resolved = new(StringComparer.Ordinal);

        foreach (string key in declaredKeys)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            string fileKey = key + FileSuffix;
            string? path = configuration[fileKey];

            if (string.IsNullOrWhiteSpace(path))
            {
                // No file form declared. The environment-variable form, if any, stands untouched.
                continue;
            }

            path = path.Trim();

            // RULE 2 - AMBIGUITY IS REFUSED BEFORE THE FILE IS EVEN OPENED, so the diagnostic names the
            // real problem rather than whatever the file turns out to contain.
            if (!string.IsNullOrWhiteSpace(configuration[key]))
            {
                throw new InvalidOperationException(
                    $"Both '{key}' and '{fileKey}' carry a value. A secret must have exactly one "
                        + "source: with two, an operator cannot tell which credential the service is "
                        + "actually presenting, and a rotation applied to one of them appears to have "
                        + $"taken effect while the other is still in use. Remove '{key}' to use the "
                        + $"projected file at '{path}', or remove '{fileKey}' to keep the inline value.");
            }

            if (!fileExists(path))
            {
                throw new InvalidOperationException(
                    $"'{fileKey}' names '{path}', which does not exist. The service will not fall back "
                        + $"to '{key}', because substituting a different credential for the one the "
                        + "deployment declared is a worse outcome than refusing to start. Check that "
                        + "the secret is granted to this service and that its projected target matches.");
            }

            string material;

            try
            {
                material = readFile(path).Trim();
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                // THE INNER EXCEPTION IS CARRIED, NOT ITS MESSAGE INLINE, and no content is read into
                // the diagnostic. A permission fault here is the single most likely cause: Compose
                // ignores `mode:`, `uid:` and `gid:` outside Swarm, so a host file at 0600 projects as
                // root-owned and this service runs unprivileged.
                throw new InvalidOperationException(
                    $"'{fileKey}' names '{path}', which could not be read. If the file exists, this is "
                        + "almost always host permissions: a projected secret keeps its host mode, and "
                        + "this service runs as an unprivileged account. See orchestration/README.md.",
                    failure);
            }

            if (material.Length == 0)
            {
                throw new InvalidOperationException(
                    $"'{fileKey}' names '{path}', which is empty. An empty secret is not a usable "
                        + $"credential, and falling back to '{key}' would present a different one than "
                        + "the deployment declared.");
            }

            resolved[key] = material;
        }

        return resolved;
    }
}
