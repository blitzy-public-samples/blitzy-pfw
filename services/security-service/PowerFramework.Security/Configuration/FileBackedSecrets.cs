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
//  🔴 AND THE C-02 KEY STORE IS ONE OF THOSE DECLARED NAMES, WHICH IT WAS NOT. The declared set was the
//  two signing keys plus the client-secret roster - so the ONE class of material a CALLER can ask this
//  service to use was the one class with no file-backed form. A sweep of a running deployment found a
//  configured key reference's exact material in the `docker compose config` render, in `docker inspect`
//  and in `/proc/1/environ` at once: the mechanism above existed and simply did not cover it. The
//  declared set is DERIVED, so covering it is a derivation change rather than a new mechanism -
//  `<ConfigurationKeyPrefix><keyRef>_FILE` now projects a reference's material exactly as
//  `SECURITY_JWT_SIGNING_KEY_FILE` projects the signing key, under the same three rules.
//
//  THREE RULES, AND EACH REFUSES RATHER THAN GUESSES.
//    1. `<KEY>_FILE` set and readable  ->  its trimmed content becomes `<KEY>`.
//    2. `<KEY>` and `<KEY>_FILE` BOTH carry a value  ->  REFUSE. Two sources for one secret means the
//       operator believes one is in effect and cannot tell which; silently preferring either is how a
//       credential rotation appears to work while the old value is still being presented.
//    3. `<KEY>_FILE` set but missing, unreadable, or empty once trimmed  ->  REFUSE. Falling back to
//       the environment value would substitute a DIFFERENT credential for the one the operator
//       deployed, which is worse than not starting.
//    4. IN PRODUCTION ONLY: a permitted C-02 key reference carrying an INLINE value and no file form
//       ->  REFUSE. The inline form is the exposed one, and a production deployment that has been given
//       the projected alternative and did not use it is a deployment leaking key material into three
//       surfaces at once. Development and every other environment keep the inline form, so the
//       documented bring-up is unchanged.
//
//  WHY RULE 4 DOES NOT TRY TO TELL A KEY FROM A FILE PATH. A permitted reference resolves EITHER to key
//  material (the keyed operations) OR to a filesystem path (the two file-hashing operations), and the
//  configuration declares no difference between the two - so a refusal that applied only to "real key
//  material" would have to GUESS which kind a value is, from its shape. A guess that guesses wrong in the
//  permissive direction leaks the exact material this rule exists to protect. So the rule is uniform, the
//  refusal message names the file-path case explicitly, and the remedy is identical for both: project the
//  value. A projected file holding a path is not absurd - it is one line, and it is the only reading that
//  cannot mis-classify.
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

        // RULE 4, EVALUATED BEFORE THE RESOLVED VALUES ARE MERGED, so it reads the deployment's own
        // configuration rather than this method's output. The environment is the host's, not a setting:
        // a deployment that could relax the rule by writing a configuration key would be a deployment
        // that could opt out of it.
        RefuseInlineKeyStoreMaterial(
            builder.Configuration,
            builder.Environment.IsProduction());

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
    /// 🔴 THE C-02 KEY STORE IS DERIVED HERE TOO, AND ITS ABSENCE WAS THE DEFECT. Each permitted
    /// reference in <c>Security:KeyStore:PermittedKeyRefs</c> is appended to
    /// <c>Security:KeyStore:ConfigurationKeyPrefix</c> - the SAME concatenation the endpoint performs when
    /// it resolves a caller's reference - so the flat key a deployment supplies material under acquires a
    /// <c>_FILE</c> companion like every other secret-bearing key. Deriving it from the permitted set is
    /// what makes a reference added to that set file-backable with no change here; a hand-kept list would
    /// leave the newest reference on the exposed form, which is precisely the failure this whole file
    /// exists to prevent.
    /// </para>
    /// <para>
    /// A BLANK PREFIX YIELDS NOTHING RATHER THAN THE BARE REFERENCE NAMES. A permitted reference with no
    /// prefix to resolve against is unresolvable configuration and the options validator already refuses
    /// that combination at startup; emitting the bare names here would additionally invent flat keys the
    /// endpoint never reads.
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

        foreach (string keyStoreKey in DeclaredKeyStoreKeys(configuration))
        {
            _ = keys.Add(keyStoreKey);
        }

        return keys;
    }

    /// <summary>
    /// Enumerates the flat configuration keys the C-02 key store resolves permitted references against.
    /// </summary>
    /// <param name="configuration">The configuration to read the key-store declaration from.</param>
    /// <returns>
    /// One key per permitted reference, spelled exactly as the endpoint composes it, or nothing when no
    /// reference is permitted or no prefix is configured.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// THE COMPOSITION IS THE ENDPOINT'S OWN, RESTATED IN ONE PLACE RATHER THAN TWO. Resolution at the
    /// endpoint is <c>configuration[ConfigurationKeyPrefix + keyRef]</c>; this method must produce the
    /// identical spelling or a projected file would satisfy a key nothing reads. That is also why the
    /// reference is read from the section's own children rather than through a bound options instance:
    /// this runs BEFORE the options graph is built, exactly as the client-secret derivation above does.
    /// </para>
    /// <para>
    /// NO VALUE IS READ HERE, ONLY NAMES. The method cannot see key material, which is what keeps it
    /// usable from a diagnostic path.
    /// </para>
    /// </remarks>
    internal static IEnumerable<string> DeclaredKeyStoreKeys(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        IConfigurationSection keyStore =
            configuration.GetSection($"{SecurityOptions.SectionName}:KeyStore");

        string? prefix = keyStore["ConfigurationKeyPrefix"];

        if (string.IsNullOrWhiteSpace(prefix))
        {
            yield break;
        }

        prefix = prefix.Trim();

        foreach (IConfigurationSection reference in keyStore.GetSection("PermittedKeyRefs").GetChildren())
        {
            string? declared = reference.Value;

            if (!string.IsNullOrWhiteSpace(declared))
            {
                yield return prefix + declared.Trim();
            }
        }
    }

    /// <summary>
    /// Refuses a production deployment that supplies C-02 key-store material inline instead of through a
    /// projected file.
    /// </summary>
    /// <param name="configuration">The deployment's configuration, read for names and presence only.</param>
    /// <param name="enforce">
    /// Whether the rule applies. The caller passes the host's production determination; the parameter
    /// exists so the rule is drivable from a test without a host.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// A permitted reference carries an inline value and names no file.
    /// </exception>
    /// <remarks>
    /// <para>
    /// 🔴 RULE 4. The inline form is the EXPOSED form: it is rendered in cleartext by
    /// <c>docker compose config</c>, returned in full by <c>docker inspect</c>, and readable from
    /// <c>/proc/&lt;pid&gt;/environ</c> by any process in the container. A sweep of a running deployment
    /// found a configured reference's exact material in all three at once. With the projected form now
    /// available for these keys, a production deployment still using the inline one is leaking material it
    /// has been given a way not to leak.
    /// </para>
    /// <para>
    /// PRODUCTION ONLY, AND THAT IS A DELIBERATE BOUND RATHER THAN A LOOPHOLE. The documented bring-up and
    /// every parity capture run under Development and supply their fixtures inline; refusing those would
    /// break the documented commands to protect a development container from itself. What the rule
    /// protects is the deployment where the exposure matters, and it is decided by the HOST's environment
    /// rather than by a configuration key, so a deployment cannot opt out of it by writing a setting.
    /// </para>
    /// <para>
    /// IT REFUSES BEFORE THE FIRST REQUEST RATHER THAN WARNING. A warning about key material in the
    /// environment is a record in the same log an operator is reading when they run out of time, and the
    /// service would answer its readiness probe and gate three dependents behind that answer regardless.
    /// This service's posture for a structural fault is to refuse to start
    /// [ws_objects/pfw.pbl.src/pfw.sra:L111-L144, HALT CLOSE at :L143].
    /// </para>
    /// <para>
    /// NO VALUE APPEARS IN THE MESSAGE. It names the KEY, the companion key and the remedy, and states the
    /// file-path case explicitly so an operator whose reference names a file rather than a key is not left
    /// guessing whether the rule applies to them. It does not report a length, a prefix or a sample.
    /// </para>
    /// </remarks>
    internal static void RefuseInlineKeyStoreMaterial(IConfiguration configuration, bool enforce)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (!enforce)
        {
            return;
        }

        foreach (string key in DeclaredKeyStoreKeys(configuration))
        {
            if (string.IsNullOrWhiteSpace(configuration[key]))
            {
                // Either nothing is configured for this reference - which is a permitted state, answered
                // 404 at the endpoint - or the value arrived through the projected file, which is the
                // shape this rule exists to require.
                continue;
            }

            string fileKey = key + FileSuffix;

            if (!string.IsNullOrWhiteSpace(configuration[fileKey]))
            {
                // Both forms carry a value. That is rule 2's refusal, raised by Resolve with the message
                // written for it, and duplicating it here would report the wrong problem first.
                continue;
            }

            throw new InvalidOperationException(
                $"'{key}' carries an inline value in a production deployment. Material supplied that way "
                    + "is rendered in cleartext by 'docker compose config', returned in full by "
                    + "'docker inspect', and readable from /proc/<pid>/environ by any process in the "
                    + $"container. Write the value into a file the container can read and name that file "
                    + $"in '{fileKey}' instead, leaving '{key}' unset. IF THIS REFERENCE NAMES A FILE PATH "
                    + "rather than key material - the two file-hashing operations resolve a reference to a "
                    + "path - the same projection applies: put the path in the projected file. This "
                    + "service cannot tell the two apart, and guessing from the value's shape would leak "
                    + "the material this rule protects whenever the guess went the permissive way.");
        }
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
