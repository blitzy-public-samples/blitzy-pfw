// ==================================================================================================
//  CapturePlanAgreementTests - the plan and the workflow definition must say the same thing
//  ------------------------------------------------------------------------------------------------
//  🔴 THIS IS THE GUARD THE WHOLE DATA-RATHER-THAN-CODE DESIGN EXISTS TO MAKE POSSIBLE.
//
//  A workflow definition under characterization/workflows/ declares WHAT must be observed: its
//  observedOutputs are the store's reviewed contract. A plan under characterization/tools/plans/
//  supplies the missing half - the endpoints, the bodies, the chaining - and one step per declared
//  output. Because both are DATA, a test can read both and assert their agreement in both directions:
//
//    - a declared output with no step would be silently uncaptured, and the recording would look
//      complete while missing an observation the definition promised;
//    - a step naming an output the definition does not declare would put an UNDECLARED fact into a
//      recording, which is a comparison against something nobody reviewed.
//
//  Written as C# the same agreement would need this test to reference the driver's assembly and reflect
//  over it, and a step nobody declared would read as code rather than as a divergence.
//
//  The other three rows are the invariants a plan cannot be trusted without: every artifact name must be
//  one the store admits (by the DRIVER'S OWN predicate, not a restatement of it), no plan may carry a
//  literal that looks like key material, and every plan must belong to a workflow the roster declares.
// ==================================================================================================

using System.Reflection;
using System.Text.RegularExpressions;

using PowerFramework.Characterization.Capture;

using Xunit;

namespace PowerFramework.Characterization.Capture.Tests;

/// <summary>Every capture plan agrees with the workflow definition it claims to implement.</summary>
public sealed class CapturePlanAgreementTests
{
    /// <summary>The repository root, located once.</summary>
    private static string Root => RepositoryRoot.Locate();

    /// <summary>The plan files present, as absolute paths.</summary>
    /// <returns>One row per plan file.</returns>
    /// <remarks>
    /// THE ROWS COME FROM THE DIRECTORY, NOT FROM A LIST. A hardcoded list would stop covering a plan
    /// added later, and the value of this suite is that it cannot quietly omit one. A plan directory with
    /// no plans in it is itself asserted against below, so an empty enumeration cannot make the suite
    /// vacuously pass.
    /// </remarks>
    public static TheoryData<string> PlanFiles()
    {
        TheoryData<string> rows = [];

        foreach (string path in Directory
            .EnumerateFiles(Path.Combine(Root, "characterization", "tools", "plans"), "*.plan.json")
            .Order(StringComparer.Ordinal))
        {
            rows.Add(Path.GetFileName(path));
        }

        return rows;
    }

    /// <summary>At least one plan exists, so the theory above cannot pass by having no rows.</summary>
    /// <remarks>
    /// A VACUOUS THEORY IS THE FAILURE MODE OF EVERY DIRECTORY-DRIVEN SUITE. Without this row, deleting
    /// the plans directory would turn every agreement assertion below into zero assertions and the build
    /// would go green on a store that captures nothing.
    /// </remarks>
    [Fact]
    public void AtLeastOneCapturePlanExists()
    {
        string directory = Path.Combine(Root, "characterization", "tools", "plans");

        Assert.True(Directory.Exists(directory), $"The plan directory is missing at '{directory}'.");

        string[] plans = [.. Directory.EnumerateFiles(directory, "*.plan.json")];

        Assert.NotEmpty(plans);
    }

    /// <summary>
    /// A plan's step names are EXACTLY its workflow definition's observedOutputs, in both directions.
    /// </summary>
    /// <param name="planFileName">The plan file.</param>
    [Theory]
    [MemberData(nameof(PlanFiles))]
    public void APlansStepsAreExactlyItsDefinitionsObservedOutputs(string planFileName)
    {
        CapturePlan plan = CapturePlan.Load(PlanPath(planFileName));

        IReadOnlySet<string> declared = ObservedOutputs(plan.WorkflowId);
        HashSet<string> planned = [.. plan.Steps.Select(static step => step.Output)];

        string[] uncaptured = [.. declared.Except(planned).Order(StringComparer.Ordinal)];
        string[] undeclared = [.. planned.Except(declared).Order(StringComparer.Ordinal)];

        Assert.True(
            uncaptured.Length == 0,
            $"'{plan.WorkflowId}' declares outputs no plan step captures, so a recording taken from this "
            + "plan would look complete while missing an observation the reviewed definition promised: "
            + string.Join(", ", uncaptured));

        Assert.True(
            undeclared.Length == 0,
            $"The plan for '{plan.WorkflowId}' captures outputs the definition does not declare, which "
            + "would put an UNDECLARED fact into a recording - a comparison against something nobody "
            + "reviewed: " + string.Join(", ", undeclared));

        // Stated as a count as well, because the definition's own summary is quoted in the store's
        // documentation and a reader checks the number rather than the set.
        Assert.Equal(declared.Count, plan.Steps.Count);
    }

    /// <summary>Every artifact a plan writes carries a name the store admits.</summary>
    /// <param name="planFileName">The plan file.</summary>
    /// <remarks>
    /// ASSERTED WITH THE DRIVER'S OWN PREDICATE. A restated extension list here would be a second thing
    /// to drift, and the whole hazard being closed is that a plausible-looking name the repository's
    /// ignore rules swallow produces a recording that exists locally and nowhere else.
    /// </remarks>
    [Theory]
    [MemberData(nameof(PlanFiles))]
    public void EveryArtifactNameAPlanWritesIsOneTheStoreAdmits(string planFileName)
    {
        CapturePlan plan = CapturePlan.Load(PlanPath(planFileName));

        Assert.NotEmpty(plan.Artifacts);

        Assert.All(
            plan.Artifacts,
            artifact => Assert.True(
                StoreRules.IsAdmittedRecordingName(artifact.Name),
                StoreRules.ExplainRefusedName(artifact.Name)));

        // ...and every artifact carries a description, because the description is written into the
        // recording itself and an empty one leaves a reader guessing what the file groups.
        Assert.All(plan.Artifacts, artifact => Assert.False(string.IsNullOrWhiteSpace(artifact.Description)));
    }

    /// <summary>
    /// No plan carries key material, a credential or anything shaped like one.
    /// </summary>
    /// <param name="planFileName">The plan file.</param>
    /// <remarks>
    /// 🔴 A PLAN IS A COMMITTED FILE, so a key in one is a key in the repository - exactly the class of
    /// finding docs/SECRETS.md exists to record and AAP constraint C-F forbids adding to. The C-02
    /// contract requires a caller pass an OPAQUE reference the service resolves against its own
    /// configured store, so a plan never needs material and the reference placeholders are the only
    /// legitimate spelling. The patterns below are shape-based rather than a value list, because a value
    /// list can only catch material somebody already knew about.
    /// </remarks>
    [Theory]
    [MemberData(nameof(PlanFiles))]
    public void NoPlanCarriesKeyMaterialOrAnythingShapedLikeIt(string planFileName)
    {
        string text = File.ReadAllText(PlanPath(planFileName));

        foreach ((string pattern, string what) in ((string, string)[])[
            ("-----BEGIN", "a PEM block"),
            ("PRIVATE KEY", "a private-key header"),
            ("\"password\"", "a password member"),
            ("\"clientSecret\"", "a client-secret member"),
            ("\"secret\"", "a secret member"),
            ("\"authorization\"", "an authorization header"),
            ("Bearer ", "a bearer credential")])
        {
            Assert.False(
                text.Contains(pattern, StringComparison.OrdinalIgnoreCase),
                $"'{planFileName}' appears to carry {what}. A plan names key material only by opaque "
                + "reference placeholder, never by value.");
        }

        // Every keyRef, ivRef and fileRef member must be a PLACEHOLDER rather than a value. The
        // placeholder spellings are the closed set the driver resolves; anything else is a literal.
        foreach (Match match in Regex.Matches(
            text,
            "\"(keyRef|ivRef|fileRef)\"\\s*:\\s*\"([^\"]*)\"",
            RegexOptions.None,
            TimeSpan.FromSeconds(5)))
        {
            string value = match.Groups[2].Value;

            Assert.True(
                value.StartsWith("${", StringComparison.Ordinal) && value.EndsWith('}'),
                $"'{planFileName}' gives {match.Groups[1].Value} the literal value '{value}'. A reference "
                + "must be a placeholder the driver resolves from its command line, so that the plan "
                + "carries no environment-specific name and no material of any kind.");
        }
    }

    /// <summary>Every plan belongs to a workflow the store's roster declares.</summary>
    /// <param name="planFileName">The plan file.</param>
    /// <remarks>
    /// The file name and the declared workflowId must agree with each other AND with a definition on
    /// disk. A plan for a workflow nobody declared would capture recordings under a key no pair can ever
    /// use, and the driver's own identifier check would not catch it because it compares the plan against
    /// the command line rather than against the roster.
    /// </remarks>
    [Theory]
    [MemberData(nameof(PlanFiles))]
    public void EveryPlanBelongsToADeclaredWorkflow(string planFileName)
    {
        CapturePlan plan = CapturePlan.Load(PlanPath(planFileName));

        Assert.Equal(plan.WorkflowId + ".plan.json", planFileName);

        Assert.Contains(plan.WorkflowId, PairState.DeclaredWorkflows(Root));

        Assert.False(string.IsNullOrWhiteSpace(plan.TargetService));
        Assert.False(string.IsNullOrWhiteSpace(plan.Notes));
    }

    /// <summary>
    /// The store holds no unpaired recording, which is the one state a reader could misread as a pass.
    /// </summary>
    /// <remarks>
    /// 🔴 THIS ROW IS THE PAIRING RULE, AS A TEST. characterization/README.md §3.2: a recording whose
    /// workflow identifier does not exist on the other side is not a partial comparison, it is not a
    /// comparison at all, and it must not be reported as a pass. The report exits non-zero on exactly
    /// that state, and this asserts the shipped store is not in it - so a target-half-only capture landed
    /// into the store fails the build rather than quietly inviting a false conclusion.
    /// </remarks>
    [Fact]
    public void TheStoreHoldsNoUnpairedRecording()
    {
        using StringWriter report = new();

        int exitCode = PairState.Report(Root, report);

        Assert.Equal(0, exitCode);

        Assert.DoesNotContain("BLOCKED", report.ToString(), StringComparison.Ordinal);

        // ...and the roster the report walked is the whole roster, not a subset it happened to find.
        Assert.Equal(
            PairState.DeclaredWorkflows(Root).Count,
            PairState.Establish(Root).Count);
    }

    /// <summary>Resolves a plan file name to its path.</summary>
    /// <param name="planFileName">The file name.</param>
    /// <returns>The absolute path.</returns>
    private static string PlanPath(string planFileName) =>
        Path.Combine(Root, "characterization", "tools", "plans", planFileName);

    /// <summary>
    /// Reads a workflow definition's observedOutputs names out of its YAML.
    /// </summary>
    /// <param name="workflowId">The workflow.</param>
    /// <returns>The declared output names.</returns>
    /// <remarks>
    /// READ AS TEXT RATHER THAN THROUGH A YAML PARSER, deliberately. This project declares no package
    /// reference at all - matching the driver it tests - and adding a YAML reader here to extract a list
    /// of names would put a package into the graph the CI dependency gates walk for the sake of one
    /// regular expression (AAP 0.5.3). The block is delimited by its own key and the next top-level-child
    /// key, so a name appearing elsewhere in the document cannot be mistaken for an output. The schema
    /// pins the indentation this relies on by pinning the document's structure.
    /// </remarks>
    private static IReadOnlySet<string> ObservedOutputs(string workflowId)
    {
        string path = Path.Combine(Root, "characterization", "workflows", workflowId + ".yaml");

        Assert.True(File.Exists(path), $"'{workflowId}' names no workflow definition at '{path}'.");

        string[] lines = File.ReadAllLines(path);

        int start = Array.FindIndex(lines, static line => line == "  observedOutputs:");

        Assert.True(start >= 0, $"'{path}' declares no observedOutputs block.");

        HashSet<string> names = new(StringComparer.Ordinal);

        for (int index = start + 1; index < lines.Length; index++)
        {
            string line = lines[index];

            // The block ends at the next key of the same parent - two spaces of indent and not a list
            // item - which is what keeps a name elsewhere in the document out of this set.
            if (line.Length > 2
                && line[0] == ' '
                && line[1] == ' '
                && line[2] != ' '
                && line[2] != '-')
            {
                break;
            }

            Match match = Regex.Match(
                line,
                "^    - name: (?<name>\\S+)$",
                RegexOptions.None,
                TimeSpan.FromSeconds(5));

            if (match.Success)
            {
                Assert.True(
                    names.Add(match.Groups["name"].Value),
                    $"'{path}' declares observed output '{match.Groups["name"].Value}' twice.");
            }
        }

        Assert.NotEmpty(names);

        return names;
    }
}

/// <summary>Locates the repository root for the rows that read real files.</summary>
/// <remarks>
/// The build embeds the root as assembly metadata, exactly as the other test projects do it, so a run
/// whose artifacts path lies outside the checkout still finds the tree. The walk-up remains as a
/// fallback, so an absent or stale value degrades to the previous behaviour rather than misreading a
/// different tree - and the marker is verified either way.
/// </remarks>
internal static class RepositoryRoot
{
    /// <summary>The file whose presence identifies the repository root.</summary>
    private const string Marker = "PowerFramework.slnx";

    /// <summary>Finds the repository root.</summary>
    /// <returns>The absolute path.</returns>
    internal static string Locate()
    {
        string? embedded = CustomAttributeExtensions
            .GetCustomAttributes<AssemblyMetadataAttribute>(typeof(RepositoryRoot).Assembly)
            .FirstOrDefault(static attribute =>
                string.Equals(attribute.Key, "PowerFrameworkRepositoryRoot", StringComparison.Ordinal))
            ?.Value;

        if (!string.IsNullOrEmpty(embedded) && File.Exists(Path.Combine(embedded, Marker)))
        {
            return embedded;
        }

        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, Marker)))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);

        return directory.FullName;
    }
}
