// ==================================================================================================
//  PairState - the comparison pass, and the reason it reports rather than compares
//  ------------------------------------------------------------------------------------------------
//  A characterization comparison is only meaningful between two halves of one pair. This pass walks
//  every workflow the store declares, establishes which halves exist, and reports the state of each -
//  and for the state the store is actually in it reports BLOCKED with the external prerequisite named,
//  rather than reporting a pass over the one half that exists.
//
//  🔴 WHY THIS IS NOT A DIFF ENGINE YET, STATED PLAINLY. There is nothing to diff. Zero legacy
//  recordings exist and none can be produced here: pfw.dll is a 32-bit PE that exports the PBNI entry
//  points and consumes an IPB_Session the Appeon PowerBuilder virtual machine supplies, and no pbvm
//  library exists anywhere in this repository. Writing a comparator against an oracle that cannot run
//  would produce a component whose only exercised path is the empty one, which is worse than a pass
//  that names its own blocker: it would LOOK like parity coverage. So this pass does the one thing that
//  is both possible and true - it establishes and reports the pair state per workflow, and it exits
//  non-zero when a workflow is in a state a reader could mistake for a comparison.
//
//  characterization/README.md §3.2 owns the rule this enforces: "A recording whose workflow identifier
//  does not exist on the other side is not a partial comparison - it is not a comparison at all, and it
//  must not be reported as a pass."
// ==================================================================================================

namespace PowerFramework.Characterization.Capture;

/// <summary>Which halves of a workflow's pair exist, and what may be concluded from that.</summary>
internal enum PairVerdict
{
    /// <summary>Neither half exists. The workflow is specified and not executed. Nothing is claimed.</summary>
    NeitherHalf,

    /// <summary>Only the target half exists. A comparison MUST NOT be reported. Blocked.</summary>
    TargetHalfOnly,

    /// <summary>Only the legacy half exists. The target capture has not been taken. Blocked.</summary>
    LegacyHalfOnly,

    /// <summary>Both halves exist. A comparison is possible.</summary>
    BothHalves,
}

/// <summary>One workflow's pair state.</summary>
/// <param name="WorkflowId">The pairing key.</param>
/// <param name="LegacyCaptured">Whether the legacy half carries a recording.</param>
/// <param name="DotnetCaptured">Whether the target half carries a recording.</param>
internal readonly record struct PairStateEntry(string WorkflowId, bool LegacyCaptured, bool DotnetCaptured)
{
    /// <summary>What may be concluded.</summary>
    internal PairVerdict Verdict => (LegacyCaptured, DotnetCaptured) switch
    {
        (true, true) => PairVerdict.BothHalves,
        (true, false) => PairVerdict.LegacyHalfOnly,
        (false, true) => PairVerdict.TargetHalfOnly,
        _ => PairVerdict.NeitherHalf,
    };

    /// <summary>The one-word state for the report.</summary>
    internal string State => Verdict switch
    {
        PairVerdict.BothHalves => "COMPARABLE",
        PairVerdict.LegacyHalfOnly => "BLOCKED",
        PairVerdict.TargetHalfOnly => "BLOCKED",
        _ => "NOT EXECUTED",
    };

    /// <summary>Why, in one sentence a reader can act on.</summary>
    internal string Reason => Verdict switch
    {
        PairVerdict.BothHalves =>
            "both halves are present; run the comparison and treat any difference as a parity finding",
        PairVerdict.LegacyHalfOnly =>
            "the legacy half is present and the target half is not; take the target capture with this tool "
            + "against the same unrecreated persistence-db volume state",
        PairVerdict.TargetHalfOnly =>
            "the target half is present and the legacy half is not, so there is nothing to compare it "
            + "against and no pass may be reported from it",
        _ =>
            "neither half is present; the workflow is specified and not executed, which is what its "
            + "declared executionStatus says",
    };
}

/// <summary>Establishes and reports the pair state of every declared workflow.</summary>
internal static class PairState
{
    /// <summary>Reads every workflow identifier the store declares.</summary>
    /// <param name="repositoryRoot">The repository root.</param>
    /// <returns>The identifiers, in sorted order.</returns>
    /// <exception cref="CaptureFailure">The workflow directory is missing.</exception>
    /// <remarks>
    /// THE ROSTER COMES FROM THE FILE NAMES, NOT FROM A LIST IN THIS FILE. A hardcoded roster would
    /// silently stop covering a workflow added later, and the whole value of this pass is that it cannot
    /// quietly omit one.
    /// </remarks>
    internal static IReadOnlyList<string> DeclaredWorkflows(string repositoryRoot)
    {
        string directory = Path.Combine(repositoryRoot, StoreRules.StoreDirectoryName, "workflows");

        if (!Directory.Exists(directory))
        {
            throw new CaptureFailure(
                $"No workflow definitions exist at '{directory}'. The store's roster is the set of "
                + "definition files, so without them there is nothing to report a pair state for.");
        }

        return [.. Directory.EnumerateFiles(directory, "*.yaml")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(static name => !string.IsNullOrEmpty(name))
            .Select(static name => name!)
            .Order(StringComparer.Ordinal)];
    }

    /// <summary>Establishes the pair state of every declared workflow.</summary>
    /// <param name="repositoryRoot">The repository root.</param>
    /// <returns>One entry per workflow, in roster order.</returns>
    internal static IReadOnlyList<PairStateEntry> Establish(string repositoryRoot) =>
        [.. DeclaredWorkflows(repositoryRoot).Select(workflowId => new PairStateEntry(
            workflowId,
            StoreRules.CarriesARecording(RecordingWriter.LegacyDirectoryFor(repositoryRoot, workflowId)),
            StoreRules.CarriesARecording(RecordingWriter.StoreDirectoryFor(repositoryRoot, workflowId))))];

    /// <summary>Writes the report and returns the exit code.</summary>
    /// <param name="repositoryRoot">The repository root.</param>
    /// <param name="output">Where to write the report.</param>
    /// <returns>0 when no workflow is in a state that could be mistaken for a comparison, else 1.</returns>
    /// <remarks>
    /// 🔴 EXIT ZERO FOR "NOT EXECUTED" AND NON-ZERO FOR "BLOCKED", which is the distinction that makes
    /// this pass worth running in CI. A workflow with neither half is exactly what its declared
    /// executionStatus says it is, so it is not a failure. A workflow with ONE half is a store that
    /// invites a false conclusion, and it fails.
    /// </remarks>
    internal static int Report(string repositoryRoot, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);

        IReadOnlyList<PairStateEntry> entries = Establish(repositoryRoot);

        int width = entries.Max(static entry => entry.WorkflowId.Length);

        output.WriteLine("characterization pair state");
        output.WriteLine(new string('-', width + 32));

        foreach (PairStateEntry entry in entries)
        {
            output.WriteLine(
                $"{entry.WorkflowId.PadRight(width)}  legacy={(entry.LegacyCaptured ? "yes" : " no")}  "
                + $"dotnet={(entry.DotnetCaptured ? "yes" : " no")}  {entry.State}");
        }

        int comparable = entries.Count(static entry => entry.Verdict == PairVerdict.BothHalves);
        int blocked = entries.Count(static entry =>
            entry.Verdict is PairVerdict.LegacyHalfOnly or PairVerdict.TargetHalfOnly);

        output.WriteLine();
        output.WriteLine($"comparable pairs : {comparable}/{entries.Count}");
        output.WriteLine($"blocked          : {blocked}");
        output.WriteLine(
            $"not executed     : {entries.Count(static entry => entry.Verdict == PairVerdict.NeitherHalf)}");

        if (comparable == 0 && blocked == 0)
        {
            output.WriteLine();
            output.WriteLine(
                "NO PAIR IS COMPARABLE, AND THAT IS REPORTED RATHER THAN WORKED AROUND. The legacy half of "
                + "every pair needs the Appeon PowerBuilder virtual machine: pfw.dll is a 32-bit PE "
                + "exporting the PBNI entry points and consuming an IPB_Session the VM supplies, and no "
                + "pbvm library exists in this repository. Until that prerequisite is met, the target half "
                + "can be captured with this tool to a directory outside the store - which proves the "
                + "driver runs - but no parity conclusion may be drawn from it.");
        }

        foreach (PairStateEntry entry in entries.Where(static entry => entry.State == "BLOCKED"))
        {
            output.WriteLine();
            output.WriteLine($"BLOCKED  {entry.WorkflowId}: {entry.Reason}");
        }

        return blocked == 0 ? 0 : 1;
    }
}
