// ==================================================================================================
//  E2eManifestGuardTests - THE GUARD OVER THE END-TO-END COMMAND SURFACE
//  ------------------------------------------------------------------------------------------------
//  SUBJECT     tests/e2e/package.json - the manifest whose `scripts` object IS the documented command
//              surface of the end-to-end suite - together with the two artifacts that quote those
//              commands by name: tests/e2e/README.md and the suite's own TypeScript diagnostics.
//  AUTHORITY   Constraint C-L, which makes the attached environment's documented install-and-run path
//              binding, and C-K - document every technology and boundary decision ACCURATELY. Agent
//              Action Plan 0.3.1 places the manifest under tests/e2e/ as a created target.
//
//  THE DEFECT THIS EXISTS TO CATCH, AND IT HAD ALREADY HAPPENED
//  ------------------------------------------------------------------------------------------------
//  The manifest declared `test:partial` TWICE, two lines apart with another entry between them. The
//  first set E2E_ALLOW_ABSENT_STACK=1; the second set that AND
//  E2E_ALLOW_MISSING_ISSUANCE_IDENTITY=1. A JSON object with a repeated key is not an error in any
//  parser in this toolchain - the last occurrence simply wins, silently - so:
//
//    * the topology-only run the README documented in three places was UNREACHABLE. Asking for it by
//      name ran the two-variable form instead;
//    * an operator who meant to acknowledge only an absent stack ALSO acknowledged a missing client
//      identity, which is the one conflation the suite's own design rules out in prose, because it
//      lets one opt-in suppress two independent findings;
//    * and the README's measured evidence table described the behaviour of a command nobody could
//      run, so the record looked corroborated and was not.
//
//  Nothing in the toolchain could have reported it. `npm run` prints no diagnostic, the TypeScript
//  compiler never reads the manifest, and a C# build has no reason to. That is exactly the shape of
//  defect a guard has to hold, rather than a reviewer.
//
//  WHY THESE FOUR ASSERTIONS AND NOT A SCHEMA
//  ------------------------------------------------------------------------------------------------
//  A JSON Schema would describe the manifest's shape and would be blind to every one of the four
//  properties that actually matter here: that no key repeats (a schema validates the PARSED object,
//  by which point the repeat is gone), that each of the two partial scripts sets the variables its
//  name claims and no others, that every command the suite's own failure messages tell an operator to
//  run exists, and that every command that exists is documented. Each is asserted against the raw
//  text or the parsed manifest directly, and each names the offending entry when it fails.
//
//  WHY A CONTRACTS TEST OWNS IT
//  ------------------------------------------------------------------------------------------------
//  The subject spans three languages and no service - a JSON manifest, a Markdown document and
//  TypeScript modules - so it belongs to no service's test project. This project already reads
//  tests/e2e/README.md, tests/e2e/playwright.config.ts and three of its fixtures for the operational
//  topology guards, so the mechanism, the repository-root probe and the conventions are established.
//
//  READ-ONLY THROUGHOUT. Nothing here writes, and nothing runs npm. A disagreement is a finding for a
//  human, never something for a test to reconcile.
// ==================================================================================================

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;
using Xunit.Sdk;

namespace PowerFramework.Contracts.Tests;

/// <summary>
/// The guards over the end-to-end suite's command surface.
/// </summary>
public sealed class E2eManifestGuardTests
{
    /// <summary>The end-to-end manifest, whose <c>scripts</c> object is the command surface.</summary>
    private const string ManifestRelativePath = "tests/e2e/package.json";

    /// <summary>The document that publishes and explains that surface.</summary>
    private const string ReadmeRelativePath = "tests/e2e/README.md";

    /// <summary>The repository solution, used as one of the two root markers.</summary>
    private const string SolutionMarkerFileName = "PowerFramework.slnx";

    /// <summary>The central package manifest, used as the other root marker.</summary>
    private const string PackagesMarkerFileName = "Directory.Packages.props";

    /// <summary>The acknowledgement that an absent four-service stack is deliberate.</summary>
    private const string AbsentStackVariable = "E2E_ALLOW_ABSENT_STACK";

    /// <summary>The acknowledgement that a missing client identity is deliberate.</summary>
    private const string MissingIdentityVariable = "E2E_ALLOW_MISSING_ISSUANCE_IDENTITY";

    /// <summary>The script that acknowledges the topology axis and only that axis.</summary>
    private const string PartialScriptName = "test:partial";

    /// <summary>The script that acknowledges both axes.</summary>
    private const string PartialNoIdentityScriptName = "test:partial:no-identity";

    /// <summary>How long any regular expression in this suite may run.</summary>
    private static readonly TimeSpan RegexBudget = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The suite's own sources that quote a command for an operator to run.
    /// </summary>
    /// <remarks>
    /// These are the files whose DIAGNOSTICS name a command: a message that says "run this instead" is
    /// worthless if the command it names does not exist, and it is worse than worthless if the command
    /// exists but does something else. The Playwright configuration is included because it quotes the
    /// collection commands in its own header.
    /// </remarks>
    private static readonly string[] CommandQuotingSources =
    [
        "tests/e2e/fixtures/run-mode.ts",
        "tests/e2e/fixtures/live-stack.ts",
        "tests/e2e/fixtures/token-issuance.ts",
        "tests/e2e/fixtures/service-endpoints.ts",
        "tests/e2e/playwright.config.ts",
        "tests/e2e/global-setup.ts",
    ];

    /// <summary>
    /// No JSON object in the end-to-end manifest declares the same key twice.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>ASSERTED WITH A TOKEN READER, NOT AGAINST A PARSED OBJECT.</b> Every JSON parser in this
    /// toolchain resolves a repeated key silently - one occurrence wins and the other vanishes - so a
    /// check that read the parsed manifest would be examining evidence the parser had already destroyed.
    /// A reader walk sees each key as it appears in the text, which is the only place the repeat exists.
    /// </para>
    /// <para>
    /// EVERY OBJECT IS CHECKED, not merely <c>scripts</c>. A repeated key anywhere in a manifest is the
    /// same silent overwrite: two <c>devDependencies</c> entries for one package would pin one version
    /// and appear to pin another, which is the pinning failure mode with no diagnostic at all.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheManifestDeclaresNoObjectKeyTwice()
    {
        byte[] json = File.ReadAllBytes(RequireRepositoryFile(ManifestRelativePath));
        Utf8JsonReader reader = new(json, new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip });

        // One key set per open object, so a key legitimately reused in a SIBLING object - "name" inside
        // two different entries - is not mistaken for a repeat.
        Stack<HashSet<string>> scopes = new();
        List<string> duplicates = [];

        while (reader.Read())
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.StartObject:
                    scopes.Push(new HashSet<string>(StringComparer.Ordinal));
                    break;

                case JsonTokenType.EndObject:
                    scopes.Pop();
                    break;

                case JsonTokenType.PropertyName:
                    string name = reader.GetString() ?? string.Empty;

                    if (!scopes.Peek().Add(name))
                    {
                        duplicates.Add(name);
                    }

                    break;

                default:
                    // Values are irrelevant here: this assertion is about keys alone.
                    break;
            }
        }

        Assert.True(
            duplicates.Count == 0,
            $"{ManifestRelativePath} declares the key(s) {string.Join(", ", duplicates)} more than once "
                + "in one object. A repeated key is resolved silently in favour of the LAST occurrence, so "
                + "the earlier declaration is unreachable while still reading as though it were in effect - "
                + "which is how a documented command came to run something other than what it said.");
    }

    /// <summary>
    /// The two partial-run scripts are distinct and each sets exactly the acknowledgements its name
    /// claims.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>THE NEGATIVE HALF IS THE LOAD-BEARING HALF.</b> That <c>test:partial</c> sets the stack
    /// acknowledgement is the easy assertion; that it does NOT set the identity acknowledgement is the
    /// one that keeps the two axes independent. The suite's design turns on an operator being able to
    /// acknowledge an absent stack while still being told about a missing client identity, and a single
    /// script setting both is precisely how that guarantee was lost.
    /// </para>
    /// <para>
    /// The bodies are read as text rather than executed. Nothing here runs npm, spawns a process or
    /// opens a socket.
    /// </para>
    /// </remarks>
    [Fact]
    public void EachPartialRunScriptSetsExactlyTheAcknowledgementsItsNameClaims()
    {
        IReadOnlyDictionary<string, string> scripts = ReadManifestScripts();

        Assert.True(
            scripts.ContainsKey(PartialScriptName),
            $"{ManifestRelativePath} declares no '{PartialScriptName}' script. It is the topology-only "
                + $"partial run, and {ReadmeRelativePath} publishes it as the alternative every "
                + "absent-stack refusal quotes by name.");

        Assert.True(
            scripts.ContainsKey(PartialNoIdentityScriptName),
            $"{ManifestRelativePath} declares no '{PartialNoIdentityScriptName}' script. It is the only "
                + "stack-free invocation that exits zero with no client identity provisioned, and it is a "
                + "SEPARATE key on purpose: folding it back into "
                + $"'{PartialScriptName}' is the defect this suite exists to prevent.");

        string partial = scripts[PartialScriptName];
        string both = scripts[PartialNoIdentityScriptName];

        Assert.True(
            Mentions(partial, AbsentStackVariable),
            $"'{PartialScriptName}' does not set {AbsentStackVariable}, so the command documented as the "
                + $"partial run would be refused exactly as a strict run is. Body: {partial}");

        Assert.False(
            Mentions(partial, MissingIdentityVariable),
            $"'{PartialScriptName}' sets {MissingIdentityVariable}, which conflates the two axes of "
                + "'deliberately partial'. An operator acknowledging an absent STACK must still be told "
                + $"about a missing identity; '{PartialNoIdentityScriptName}' is the script that "
                + $"acknowledges both. Body: {partial}");

        Assert.True(
            Mentions(both, AbsentStackVariable) && Mentions(both, MissingIdentityVariable),
            $"'{PartialNoIdentityScriptName}' must set both {AbsentStackVariable} and "
                + $"{MissingIdentityVariable} - that pairing is what its name claims and what makes it the "
                + $"one stack-free invocation that exits zero. Body: {both}");

        Assert.False(
            string.Equals(partial, both, StringComparison.Ordinal),
            "The two partial-run scripts carry identical bodies, so one of them is a duplicate under a "
                + "second name rather than a second scenario.");
    }

    /// <summary>
    /// Every command the suite's own diagnostics quote names a script the manifest declares.
    /// </summary>
    /// <remarks>
    /// A failure message that says "ask for it by name" and then names a script that does not exist
    /// sends an operator looking for a different mechanism, which is worse than saying nothing. The
    /// quoted names live in TypeScript constants that no compiler can check against a JSON manifest, so
    /// this is the only place the two can be held together.
    /// </remarks>
    [Fact]
    public void EveryCommandTheSuiteQuotesNamesAScriptTheManifestDeclares()
    {
        IReadOnlyDictionary<string, string> scripts = ReadManifestScripts();
        List<string> unresolved = [];

        foreach (string source in CommandQuotingSources)
        {
            foreach (string quoted in ReadQuotedScriptNames(source))
            {
                if (!scripts.ContainsKey(quoted))
                {
                    unresolved.Add(
                        string.Create(CultureInfo.InvariantCulture, $"{source} quotes 'npm run {quoted}'"));
                }
            }
        }

        Assert.True(
            unresolved.Count == 0,
            "The end-to-end suite tells an operator to run a script the manifest does not declare: "
                + string.Join("; ", unresolved)
                + $". Either the script was renamed and the diagnostic was not, or the diagnostic names a "
                + $"command that never existed. Declared: {string.Join(", ", scripts.Keys.Order(StringComparer.Ordinal))}.");
    }

    /// <summary>
    /// Every script the manifest declares is documented in the end-to-end readme, and every command the
    /// readme names exists.
    /// </summary>
    /// <remarks>
    /// <para>
    /// BOTH DIRECTIONS, because each catches a different half of the same drift. An undocumented script
    /// is a command surface nobody knows about; a documented command that does not exist is an
    /// instruction that fails when followed. The duplicate-key defect produced the second of those in
    /// its worst form - the command existed, was documented, and did something else.
    /// </para>
    /// <para>
    /// <c>npm test</c> is accepted as documentation of the <c>test</c> script alongside
    /// <c>npm run test</c>, because the readme deliberately uses the shorter spelling npm defines for the
    /// lifecycle script and rewriting it would be wrong.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheReadmeAndTheManifestAgreeOnTheCommandSurface()
    {
        IReadOnlyDictionary<string, string> scripts = ReadManifestScripts();
        string readme = File.ReadAllText(RequireRepositoryFile(ReadmeRelativePath));

        foreach (string quoted in ReadQuotedScriptNames(ReadmeRelativePath))
        {
            Assert.True(
                scripts.ContainsKey(quoted),
                $"{ReadmeRelativePath} tells a reader to run 'npm run {quoted}' and "
                    + $"{ManifestRelativePath} declares no such script.");
        }

        foreach (string name in scripts.Keys)
        {
            bool documented = readme.Contains($"npm run {name}", StringComparison.Ordinal)
                || readme.Contains($"npm {name}", StringComparison.Ordinal);

            Assert.True(
                documented,
                $"{ManifestRelativePath} declares the script '{name}' and {ReadmeRelativePath} never names "
                    + "it. An undocumented command surface is one an operator can only find by reading the "
                    + "manifest, which is where this suite's one silent behavioural change hid.");
        }
    }

    /// <summary>Whether a script body sets the named environment variable.</summary>
    /// <param name="body">The script body as the manifest declares it.</param>
    /// <param name="variable">The variable name.</param>
    /// <returns><see langword="true"/> when the body assigns that variable.</returns>
    /// <remarks>
    /// Matched as an ASSIGNMENT rather than as a substring, so a variable merely mentioned in a comment
    /// or in a longer name cannot satisfy it. The two variable names share a prefix with nothing else in
    /// the manifest, but the assignment form is what the assertion is actually about.
    /// </remarks>
    private static bool Mentions(string body, string variable)
        => Regex.IsMatch(body, @"(^|\s)" + Regex.Escape(variable) + "=", RegexOptions.None, RegexBudget);

    /// <summary>Reads the manifest's <c>scripts</c> object.</summary>
    /// <returns>The script names and their bodies.</returns>
    private static IReadOnlyDictionary<string, string> ReadManifestScripts()
    {
        using JsonDocument manifest = JsonDocument.Parse(
            File.ReadAllBytes(RequireRepositoryFile(ManifestRelativePath)));

        if (!manifest.RootElement.TryGetProperty("scripts", out JsonElement scripts))
        {
            throw FailException.ForFailure(
                $"{ManifestRelativePath} declares no 'scripts' object. That object IS the end-to-end "
                    + "suite's documented command surface, so its absence is a finding rather than a "
                    + "reason to skip a check.");
        }

        Dictionary<string, string> bodies = new(StringComparer.Ordinal);

        foreach (JsonProperty script in scripts.EnumerateObject())
        {
            // A repeated key is reported by TheManifestDeclaresNoObjectKeyTwice, which reads the raw
            // bytes; here the parser has already collapsed it, so the last one is simply taken.
            bodies[script.Name] = script.Value.GetString() ?? string.Empty;
        }

        return bodies;
    }

    /// <summary>Reads every <c>npm run &lt;name&gt;</c> a file quotes.</summary>
    /// <param name="relativePath">The repository-relative file to read.</param>
    /// <returns>The distinct script names quoted there.</returns>
    /// <remarks>
    /// A token beginning with a dash is skipped: <c>npm run --silent</c> names a flag rather than a
    /// script, and the readme uses that form.
    /// </remarks>
    private static IReadOnlyList<string> ReadQuotedScriptNames(string relativePath)
    {
        string text = File.ReadAllText(RequireRepositoryFile(relativePath));

        return
        [
            .. Regex.Matches(text, @"npm run ([A-Za-z0-9:_-]+)", RegexOptions.None, RegexBudget)
                .Select(static match => match.Groups[1].Value)
                .Where(static name => !name.StartsWith('-'))
                .Distinct(StringComparer.Ordinal),
        ];
    }

    /// <summary>Resolves a repository-relative path, failing when the file is absent.</summary>
    /// <param name="relativePath">The forward-slash repository-relative path.</param>
    /// <returns>The absolute path.</returns>
    private static string RequireRepositoryFile(string relativePath)
    {
        string path = Path.Combine(
            [RequireRepositoryRoot(), .. relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries)]);

        return File.Exists(path)
            ? path
            : throw FailException.ForFailure(
                $"'{relativePath}' does not exist. It is part of the end-to-end command surface this suite "
                    + "holds together, so its absence is a finding rather than a reason to skip a check.");
    }

    /// <summary>Locates the repository root by walking up from the test binary.</summary>
    /// <returns>The absolute repository-root path.</returns>
    /// <remarks>
    /// Both markers are required together, so the walk cannot latch onto a same-named directory
    /// elsewhere on the machine. Read-only, and it starts at the embedded root when the build supplied
    /// one - see <see cref="TestRepositoryRoot"/>.
    /// </remarks>
    private static string RequireRepositoryRoot()
    {
        DirectoryInfo? candidate = new(TestRepositoryRoot.SearchStart);

        while (candidate is not null)
        {
            if (File.Exists(Path.Combine(candidate.FullName, SolutionMarkerFileName))
                && File.Exists(Path.Combine(candidate.FullName, PackagesMarkerFileName)))
            {
                return candidate.FullName;
            }

            // Parent is null at the filesystem root, which is what terminates the walk.
            candidate = candidate.Parent;
        }

        throw FailException.ForFailure(
            $"No directory from '{TestRepositoryRoot.SearchStart}' up to the filesystem root holds both "
                + $"repository markers '{SolutionMarkerFileName}' and '{PackagesMarkerFileName}'.");
    }
}
