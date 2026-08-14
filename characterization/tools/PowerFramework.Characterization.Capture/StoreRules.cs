// ==================================================================================================
//  StoreRules - the characterization store's own rules, enforced in code rather than trusted to a
//  reader
//  ------------------------------------------------------------------------------------------------
//  Every rule below is owned by a document and is cited to it. None is invented here, and none may be
//  relaxed here to make a capture land: the point of putting them in the driver is that a capture
//  which would break one cannot be taken by accident.
// ==================================================================================================

namespace PowerFramework.Characterization.Capture;

/// <summary>
/// The store rules a capture must satisfy before it may be written.
/// </summary>
internal static class StoreRules
{
    /// <summary>The store's directory name, relative to the repository root.</summary>
    internal const string StoreDirectoryName = "characterization";

    /// <summary>The marker that identifies the repository root.</summary>
    internal const string RepositoryRootMarker = "PowerFramework.slnx";

    /// <summary>The name both recording roots use for their scaffolding file.</summary>
    internal const string PlaceholderFileName = "README.md";

    /// <summary>
    /// The file extensions a recording may carry.
    /// </summary>
    /// <remarks>
    /// characterization/README.md §3.4: recordings must be TEXT-BASED AND DIFFABLE so that a parity
    /// failure is legible in review rather than merely detectable. The list is the readme's, in its
    /// order.
    /// </remarks>
    internal static readonly string[] AdmittedExtensions =
        [".json", ".jsonl", ".txt", ".csv", ".sql", ".md", ".yaml"];

    /// <summary>
    /// Names and extensions the repository's root <c>.gitignore</c> swallows at every depth.
    /// </summary>
    /// <remarks>
    /// characterization/README.md §3.5, and the reason it is a rule rather than advice: those patterns
    /// are UNANCHORED, so they match inside this store too, and the only negations in that file are
    /// anchored elsewhere. A capture named this way exists on the machine that produced it, is absent
    /// from review and from CI, and the comparison looks taken while being unfindable. The root
    /// <c>.gitignore</c> is not modified to accommodate a name; the name is chosen correctly.
    /// </remarks>
    internal static readonly string[] SwallowedExtensions = [".log", ".zip", ".rar", ".bak", ".dmp", ".bat"];

    /// <summary>A file name the root ignore file swallows outright.</summary>
    internal static readonly string[] SwallowedNames = ["thumbs.db"];

    /// <summary>Whether a recording file name is one the store admits.</summary>
    /// <param name="fileName">The bare file name, with no directory part.</param>
    /// <returns><see langword="true"/> when the name may be written.</returns>
    internal static bool IsAdmittedRecordingName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        if (fileName.IndexOfAny(['/', '\\']) >= 0)
        {
            return false;
        }

        if (SwallowedNames.Contains(fileName, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        string extension = Path.GetExtension(fileName);

        return !SwallowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase)
            && AdmittedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Explains, for an operator, why a name was refused.</summary>
    /// <param name="fileName">The refused name.</param>
    /// <returns>The refusal text.</returns>
    internal static string ExplainRefusedName(string fileName)
    {
        string extension = Path.GetExtension(fileName);

        if (SwallowedNames.Contains(fileName, StringComparer.OrdinalIgnoreCase)
            || SwallowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            return $"'{fileName}' matches a pattern the repository's root .gitignore carries UNANCHORED, "
                + "so the file would be silently untracked - present here and absent from review and from "
                + "CI. characterization/README.md section 3.5 owns this rule. Rename the artifact; do not "
                + "weaken the ignore file.";
        }

        return $"'{fileName}' does not carry one of the extensions the store admits "
            + $"({string.Join(", ", AdmittedExtensions)}). characterization/README.md section 3.4 requires "
            + "every recording to be text-based and diffable.";
    }

    /// <summary>Whether a directory is a per-workflow recording directory inside the store.</summary>
    /// <param name="repositoryRoot">The located repository root.</param>
    /// <param name="directory">The output directory a run was asked to write.</param>
    /// <returns><see langword="true"/> when the path lies under a recording root.</returns>
    internal static bool IsInsideRecordingStore(string repositoryRoot, string directory)
    {
        string recordings = Path.GetFullPath(
            Path.Combine(repositoryRoot, StoreDirectoryName, "recordings"));

        string candidate = Path.GetFullPath(directory);

        return candidate.StartsWith(
            recordings + Path.DirectorySeparatorChar,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether a recording directory holds a file an actual capture would have written.
    /// </summary>
    /// <param name="directory">The directory to inspect.</param>
    /// <returns><see langword="true"/> when a real capture is present.</returns>
    /// <remarks>
    /// THE SAME PREDICATE THE STORE'S GUARDS USE, deliberately identical to
    /// <c>CharacterizationWorkflowGuardTests.CharacterizationStore.CarriesARecording</c> and to the
    /// pinyin hook's activation predicate: a dot-file and a readme are scaffolding, anything else is a
    /// capture (characterization/README.md §3.6). Three copies of one predicate would drift; this one is
    /// the driver's, and the plan-agreement guard asserts the three agree.
    /// </remarks>
    internal static bool CarriesARecording(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return false;
        }

        foreach (string file in Directory.EnumerateFiles(directory))
        {
            string name = Path.GetFileName(file);

            if (!name.StartsWith('.')
                && !string.Equals(name, PlaceholderFileName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The refusal a target-side capture receives when its legacy half does not exist.
    /// </summary>
    /// <param name="workflowId">The pairing key.</param>
    /// <returns>The refusal text, naming the external prerequisite.</returns>
    /// <remarks>
    /// <para>
    /// 🔴 THIS IS THE ONE RULE THE DRIVER EXISTS TO KEEP RATHER THAN TO SATISFY.
    /// characterization/README.md §3.2 and docs/PARITY.md §4.1 both state it: a recording whose
    /// workflow identifier does not exist on the other side is not a partial comparison, it is not a
    /// comparison at all, and it must not be reported as a pass. So the driver will capture happily to
    /// any directory an operator names, and refuses to write into the store's own
    /// <c>recordings/dotnet/&lt;workflowId&gt;/</c> until the legacy half is there to pair with.
    /// </para>
    /// <para>
    /// AND IT IS NOT A SEQUENCING PREFERENCE. Landing a target-only half would make the store's own
    /// guard fail - it asserts the two halves exist together and that both agree with the declared
    /// execution status - and the honest way past that guard is the missing oracle, not an edit to the
    /// guard.
    /// </para>
    /// </remarks>
    internal static string ExplainMissingLegacyHalf(string workflowId) =>
        $"Refusing to write into characterization/recordings/dotnet/{workflowId}/ because "
        + $"characterization/recordings/legacy/{workflowId}/ carries no recording. A recording whose "
        + "workflow identifier does not exist on the other side is not a comparison at all "
        + "(characterization/README.md section 3.2, docs/PARITY.md section 4.1), and the store's guard "
        + "asserts the two halves exist together. THE MISSING HALF IS AN EXTERNAL PREREQUISITE, NOT A "
        + "MISSING FEATURE OF THIS TOOL: the oracle needs the Appeon PowerBuilder virtual machine, "
        + "because pfw.dll is a 32-bit PE exporting the PBNI entry points and consuming an IPB_Session "
        + "the VM supplies, and no pbvm library exists in this repository. Capture to a directory of "
        + "your own with --output while that remains true.";
}
