// ==================================================================================================
//  RecordingWriter - where a capture may be written, in what shape, and the one refusal
// ==================================================================================================

using System.Text;
using System.Text.Json.Nodes;

namespace PowerFramework.Characterization.Capture;

/// <summary>Writes a completed capture, or refuses to.</summary>
internal sealed class RecordingWriter
{
    /// <summary>The manifest every capture writes beside its artifacts.</summary>
    internal const string ManifestFileName = "manifest.json";

    /// <summary>Locates the repository root by walking up to the solution marker.</summary>
    /// <param name="start">Where to start, defaulting to the current directory.</param>
    /// <returns>The repository root.</returns>
    /// <exception cref="CaptureFailure">No ancestor carries the marker.</exception>
    /// <remarks>
    /// THE SAME MARKER-WALK THE TEST SUITES USE, so the tool and the guards agree on where the store is
    /// even when the tool is run from a service directory or from the store itself.
    /// </remarks>
    internal static string LocateRepositoryRoot(string? start = null)
    {
        DirectoryInfo? directory = new(start ?? Directory.GetCurrentDirectory());

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, StoreRules.RepositoryRootMarker)))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new CaptureFailure(
            $"No ancestor of '{start ?? Directory.GetCurrentDirectory()}' carries "
            + $"{StoreRules.RepositoryRootMarker}, so the characterization store cannot be located. Run "
            + "the tool from inside the repository, or pass an absolute --output.");
    }

    /// <summary>The store's own target-side directory for a workflow.</summary>
    /// <param name="repositoryRoot">The repository root.</param>
    /// <param name="workflowId">The pairing key.</param>
    /// <returns>The directory, which may not exist.</returns>
    internal static string StoreDirectoryFor(string repositoryRoot, string workflowId) => Path.Combine(
        repositoryRoot,
        StoreRules.StoreDirectoryName,
        "recordings",
        "dotnet",
        workflowId);

    /// <summary>The legacy half's directory for a workflow.</summary>
    /// <param name="repositoryRoot">The repository root.</param>
    /// <param name="workflowId">The pairing key.</param>
    /// <returns>The directory, which may not exist.</returns>
    internal static string LegacyDirectoryFor(string repositoryRoot, string workflowId) => Path.Combine(
        repositoryRoot,
        StoreRules.StoreDirectoryName,
        "recordings",
        "legacy",
        workflowId);

    /// <summary>
    /// Decides whether a destination may be written, and refuses an unpaired landing in the store.
    /// </summary>
    /// <param name="repositoryRoot">The repository root.</param>
    /// <param name="destination">The directory the run will write.</param>
    /// <param name="workflowId">The pairing key.</param>
    /// <exception cref="CaptureFailure">The destination may not be written.</exception>
    internal static void Authorize(string repositoryRoot, string destination, string workflowId)
    {
        if (!StoreRules.IsInsideRecordingStore(repositoryRoot, destination))
        {
            // Outside the store an operator owns the directory, and a target-side capture there is a
            // legitimate artifact: it proves the driver runs and it is what a comparison will use the day
            // the oracle exists. It simply is not a HALF OF A PAIR until it sits in the store.
            return;
        }

        string expected = StoreDirectoryFor(repositoryRoot, workflowId);

        if (!string.Equals(
                Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(expected).TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.Ordinal))
        {
            throw new CaptureFailure(
                $"'{destination}' is inside the recording store but is not this workflow's own directory. "
                + "characterization/README.md section 3.4 requires one workflow's output to stay in its own "
                + $"directory; the only in-store destination for '{workflowId}' is '{expected}'.");
        }

        if (!StoreRules.CarriesARecording(LegacyDirectoryFor(repositoryRoot, workflowId)))
        {
            throw new CaptureFailure(StoreRules.ExplainMissingLegacyHalf(workflowId));
        }
    }

    /// <summary>Writes the artifacts and the manifest.</summary>
    /// <param name="destination">The directory to write, created if absent.</param>
    /// <param name="artifacts">Artifact name to content.</param>
    /// <param name="manifest">The run manifest.</param>
    /// <param name="screen">The screen that proves no secret survives.</param>
    /// <returns>The files written, in write order.</returns>
    /// <exception cref="CaptureFailure">A name is refused, or a secret reached the output.</exception>
    /// <remarks>
    /// EVERY BYTE IS SCREENED AND PROVED BEFORE THE FIRST FILE IS CREATED. The whole set is serialized,
    /// screened and checked first, so a capture carrying a secret leaves nothing behind at all rather than
    /// leaving the artifacts written before the offending one.
    /// </remarks>
    internal static IReadOnlyList<string> Write(
        string destination,
        IReadOnlyDictionary<string, JsonObject> artifacts,
        JsonObject manifest,
        SecretScreen screen)
    {
        ArgumentNullException.ThrowIfNull(artifacts);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(screen);

        Dictionary<string, string> rendered = new(StringComparer.Ordinal);

        foreach ((string name, JsonObject content) in artifacts.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            if (!StoreRules.IsAdmittedRecordingName(name))
            {
                throw new CaptureFailure(StoreRules.ExplainRefusedName(name));
            }

            rendered[name] = Render(content, screen, name);
        }

        rendered[ManifestFileName] = Render(manifest, screen, ManifestFileName);

        Directory.CreateDirectory(destination);

        List<string> written = [];

        foreach ((string name, string content) in rendered.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            string path = Path.Combine(destination, name);

            // UTF-8 WITHOUT A BYTE-ORDER MARK AND WITH LINE FEEDS. A mark or a carriage return would
            // surface as a difference on the first line of every comparison, which is the platform
            // artifact characterization/README.md section 3.7 requires be normalized rather than recorded.
            File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            written.Add(path);
        }

        return written;
    }

    /// <summary>Serializes one artifact, screened and newline-normalized.</summary>
    /// <param name="content">The artifact.</param>
    /// <param name="screen">The secret screen.</param>
    /// <param name="artifact">The artifact name, for a failure message.</param>
    /// <returns>The bytes to write, as text.</returns>
    private static string Render(JsonObject content, SecretScreen screen, string artifact)
    {
        screen.Screen(content);

        string text = content.ToJsonString(CapturePlan.SerializerOptions)
            .ReplaceLineEndings("\n");

        screen.Prove(text, artifact);

        return text + "\n";
    }
}
