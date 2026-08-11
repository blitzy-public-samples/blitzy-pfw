// ==================================================================================================
//  DataDirectoryFaultTests - THE STORAGE-DIRECTORY DIAGNOSTIC
//
//  WHAT THIS FILE GUARDS. Two places in this service can fail to reach the configured storage directory
//  - the startup gate's writability probe and the connection factory's create-if-absent step - and both
//  used to describe the failure in their own words while QUOTING THE CONFIGURED PATH and NAMING NO
//  CONFIGURATION KEY. Measured on a running host, one such failure put the path into the startup output
//  FOUR times and the key ZERO times: the message quoted it once and the ATTACHED file-system exception
//  republished it, so an operator was handed the value they already knew and not the setting to change.
//  The terminal record also appended "Configured values are deliberately not quoted", which was false in
//  the one record that said it.
//
//  Both sites now route through `Data/DataDirectoryFault`, and this file is the evidence for the three
//  properties that made the change worth making:
//
//    1. THE KEY IS NAMED - always, on every class of failure, because it is the only part of the record
//       an operator can act on.
//
//    2. THE PATH IS WITHHELD - by construction rather than by care. Every row asserts the absence as a
//       BOOLEAN rather than with Assert.DoesNotContain, because that overload renders both operands and
//       would print the mount layout at exactly the moment the defect it guards against was present.
//
//    3. THE FAILURE CLASS IS ESTABLISHED RATHER THAN GUESSED. The old form emitted one sentence - "not
//       writable by this process" - for all four ways this can fail, which sends an operator to check
//       permissions on a path that is occupied by a file or whose volume was never mounted. The rows
//       below assert that the four classes are distinguished AND that their sentences differ, since four
//       names mapping onto one sentence would satisfy a classification test and help nobody.
//
//  CONSTRAINT COMPLIANCE
//  ------------------------------------------------------------------------------------------------
//  C-F   Nothing echoed. The rows create real temporary paths and assert they never appear in a record.
//  C-I   Every row is a pure function call or a temporary file; no host, no port, no database.
// ==================================================================================================

using PowerFramework.Persistence.Data;

using Xunit;

namespace PowerFramework.Persistence.Tests;

/// <summary>
/// The storage-directory description names the key, establishes the class, and withholds the path.
/// </summary>
public sealed class DataDirectoryFaultTests
{
    /// <summary>
    /// The key the description names is the one the options layer actually binds.
    /// </summary>
    /// <remarks>
    /// THE PAIRING IS ASSERTED RATHER THAN LEFT TO INSPECTION. <c>DataDirectoryFault</c> deliberately
    /// takes no dependency on the options types - it is called from the connection factory's construction
    /// path, where the options graph is not available - so the key is a literal there and could drift from
    /// the setting it names. <c>Configuration/PersistenceOptions.cs</c> documents the spelling
    /// <c>Sqlite:DataDirectory</c> as the path that binds <c>SqliteOptions.DataDirectory</c>, and this row
    /// is what keeps the two in step. A record naming a key nothing reads would be worse than one naming
    /// no key at all, because an operator would set it and see nothing change.
    /// </remarks>
    [Fact]
    public void TheNamedKeyIsTheOneTheOptionsLayerBinds()
    {
        Assert.Equal("Sqlite:DataDirectory", DataDirectoryFault.ConfigurationKey);

        // Composed from the same symbols the options layer uses, so a property rename breaks this row.
        Assert.Equal(
            $"Sqlite:{nameof(PowerFramework.Persistence.Configuration.SqliteOptions.DataDirectory)}",
            DataDirectoryFault.ConfigurationKey);
    }

    /// <summary>
    /// A file occupying the configured path is classified as such, not as a permission problem.
    /// </summary>
    [Fact]
    public void AFileOccupyingThePathIsClassifiedAsAFile()
    {
        string file = Path.Combine(Path.GetTempPath(), $"pfw-fault-file-{Guid.NewGuid():n}");
        File.WriteAllText(file, string.Empty);

        try
        {
            Assert.Equal(DataDirectoryFaultKind.PathIsAFile, DataDirectoryFault.Classify(file));

            string described = DataDirectoryFault.Describe(file, new IOException("ignored"));

            Assert.Contains("a FILE occupies the configured path", described, StringComparison.Ordinal);
            AssertNamesTheKeyAndWithholds(described, file);
        }
        finally
        {
            File.Delete(file);
        }
    }

    /// <summary>
    /// A missing containing directory is classified as such rather than as unwritable.
    /// </summary>
    /// <remarks>
    /// THIS IS THE CLASS THE OLD SINGLE SENTENCE GOT MOST WRONG. In deployment it is the unmounted volume,
    /// and "correct the volume's ownership or permissions" is advice about a directory that is not there.
    /// </remarks>
    [Fact]
    public void AMissingContainingDirectoryIsClassifiedAsAMissingParent()
    {
        string absentParent = Path.Combine(Path.GetTempPath(), $"pfw-fault-gone-{Guid.NewGuid():n}");

        // A UNIQUE LEAF RATHER THAN "data". The helper below also asserts the final SEGMENT is withheld,
        // and a generic leaf cannot discriminate: the word "data" occurs in the description's own prose,
        // so the row would fail on a false positive rather than on a leak.
        string configured = Path.Combine(absentParent, $"leaf-{Guid.NewGuid():n}");

        Assert.False(Directory.Exists(absentParent));
        Assert.Equal(DataDirectoryFaultKind.ParentMissing, DataDirectoryFault.Classify(configured));

        string described = DataDirectoryFault.Describe(configured, new IOException("ignored"));

        Assert.Contains("the CONTAINING DIRECTORY does not exist", described, StringComparison.Ordinal);
        AssertNamesTheKeyAndWithholds(described, configured);
    }

    /// <summary>
    /// A file standing where the parent should be is reported as the parent's problem.
    /// </summary>
    /// <remarks>
    /// REPORTED AS ParentMissing RATHER THAN PathIsAFile, deliberately: the configured leaf is not itself a
    /// file, so telling an operator "a file occupies the configured path" would point them at the wrong
    /// component of the path. This is the exact shape both realigned suites provoke, because it fails for
    /// every user and therefore does not depend on the identity a test run happens to have.
    /// </remarks>
    [Fact]
    public void AFileStandingWhereTheParentShouldBeIsReportedAsTheParentsProblem()
    {
        string blocker = Path.Combine(Path.GetTempPath(), $"pfw-fault-block-{Guid.NewGuid():n}");
        File.WriteAllText(blocker, string.Empty);

        string configured = Path.Combine(blocker, "nested");

        try
        {
            Assert.Equal(DataDirectoryFaultKind.ParentMissing, DataDirectoryFault.Classify(configured));

            string described = DataDirectoryFault.Describe(configured, new IOException("ignored"));

            Assert.Contains("CONTAINING DIRECTORY", described, StringComparison.Ordinal);
            AssertNamesTheKeyAndWithholds(described, configured);
            AssertWithholds(described, blocker);
        }
        finally
        {
            File.Delete(blocker);
        }
    }

    /// <summary>
    /// An existing directory whose write failed is classified as unwritable.
    /// </summary>
    /// <remarks>
    /// CLASSIFIED FROM EXISTENCE RATHER THAN FROM A PERMISSION BIT, and that is the contract rather than a
    /// shortcut: the classifier is only ever consulted AFTER a write has already failed, so "the directory
    /// is there and the write failed" is exactly what makes the write the fault. Provoking a real
    /// permission failure would additionally depend on the identity the suite runs as - as root it cannot
    /// be provoked at all - which is why the surrounding code probes with a real write and this classifier
    /// does not.
    /// </remarks>
    [Fact]
    public void AnExistingDirectoryIsClassifiedAsUnwritable()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"pfw-fault-exists-{Guid.NewGuid():n}");
        Directory.CreateDirectory(directory);

        try
        {
            Assert.Equal(DataDirectoryFaultKind.NotWritable, DataDirectoryFault.Classify(directory));

            string described = DataDirectoryFault.Describe(
                directory,
                new UnauthorizedAccessException("ignored"));

            Assert.Contains("cannot write inside it", described, StringComparison.Ordinal);
            Assert.Contains("NON-ROOT user", described, StringComparison.Ordinal);
            AssertNamesTheKeyAndWithholds(described, directory);
        }
        finally
        {
            Directory.Delete(directory);
        }
    }

    /// <summary>
    /// An absent directory whose parent exists is classified as a refused creation.
    /// </summary>
    [Fact]
    public void AnAbsentDirectoryUnderAnExistingParentIsClassifiedAsARefusedCreation()
    {
        string parent = Path.Combine(Path.GetTempPath(), $"pfw-fault-parent-{Guid.NewGuid():n}");
        Directory.CreateDirectory(parent);

        string configured = Path.Combine(parent, $"leaf-{Guid.NewGuid():n}");

        try
        {
            Assert.False(Directory.Exists(configured));
            Assert.Equal(DataDirectoryFaultKind.CannotCreate, DataDirectoryFault.Classify(configured));

            string described = DataDirectoryFault.Describe(
                configured,
                new UnauthorizedAccessException("ignored"));

            Assert.Contains("its parent refused to create it", described, StringComparison.Ordinal);
            AssertNamesTheKeyAndWithholds(described, configured);
        }
        finally
        {
            Directory.Delete(parent);
        }
    }

    /// <summary>
    /// A blank path is classified as unknown, and still produces a record naming the key.
    /// </summary>
    /// <remarks>
    /// UNREACHABLE THROUGH CONFIGURATION AND STILL WORTH A ROW. The setting carries a required-value
    /// annotation, so a blank value never reaches either call site; the classifier is nonetheless total,
    /// because a classifier that threw while describing a failure would replace a poor diagnostic with
    /// none at all.
    /// </remarks>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankPathIsClassifiedAsUnknownAndStillNamesTheKey(string blank)
    {
        Assert.Equal(DataDirectoryFaultKind.Unknown, DataDirectoryFault.Classify(blank));

        string described = DataDirectoryFault.Describe(blank, cause: null);

        Assert.Contains(DataDirectoryFault.ConfigurationKey, described, StringComparison.Ordinal);
        Assert.Contains("could not be established", described, StringComparison.Ordinal);

        // With no cause, the record says so rather than rendering an empty type name.
        Assert.Contains("no exception", described, StringComparison.Ordinal);
    }

    /// <summary>
    /// The four classes produce four different sentences.
    /// </summary>
    /// <remarks>
    /// THE ROW THAT MAKES THE CLASSIFICATION MEAN SOMETHING. Distinguishing four enum values is worthless
    /// if they all render the same advice - which is precisely the state the old single "not writable"
    /// sentence was in. Comparing the rendered descriptions is what proves the distinction reaches the
    /// operator rather than stopping at the type system.
    /// </remarks>
    [Fact]
    public void EachClassRendersADistinctSentence()
    {
        string file = Path.Combine(Path.GetTempPath(), $"pfw-fault-d1-{Guid.NewGuid():n}");
        string parent = Path.Combine(Path.GetTempPath(), $"pfw-fault-d2-{Guid.NewGuid():n}");

        File.WriteAllText(file, string.Empty);
        Directory.CreateDirectory(parent);

        try
        {
            IOException cause = new("ignored");

            string[] rendered =
            [
                DataDirectoryFault.Describe(file, cause),
                DataDirectoryFault.Describe(
                    Path.Combine(Path.GetTempPath(), $"pfw-fault-d3-{Guid.NewGuid():n}", "data"),
                    cause),
                DataDirectoryFault.Describe(parent, cause),
                DataDirectoryFault.Describe(Path.Combine(parent, "data"), cause),
                DataDirectoryFault.Describe(string.Empty, cause),
            ];

            Assert.Equal(rendered.Length, rendered.Distinct(StringComparer.Ordinal).Count());

            // And every one of them names the key, so no class is an escape hatch from rule 1.
            Assert.All(
                rendered,
                description => Assert.Contains(
                    DataDirectoryFault.ConfigurationKey,
                    description,
                    StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(file);
            Directory.Delete(parent);
        }
    }

    /// <summary>
    /// The cause's own message is never rendered, only its type name.
    /// </summary>
    /// <remarks>
    /// THIS IS THE DISCLOSURE CHANNEL THAT MADE REWORDING INSUFFICIENT. A file-system exception quotes the
    /// path it failed on in its OWN Message, so a description that withheld the path and then attached or
    /// interpolated the cause would publish it anyway - which is how one omission became four occurrences.
    /// The row uses a cause whose message is a distinctive marker, so its appearance would be unambiguous.
    /// </remarks>
    [Fact]
    public void TheCausesOwnMessageIsNeverRendered()
    {
        const string marker = "a-message-that-must-not-be-rendered";

        string directory = Path.Combine(Path.GetTempPath(), $"pfw-fault-msg-{Guid.NewGuid():n}");
        Directory.CreateDirectory(directory);

        try
        {
            string described = DataDirectoryFault.Describe(
                directory,
                new UnauthorizedAccessException(marker));

            Assert.False(
                described.Contains(marker, StringComparison.Ordinal),
                "The description renders the cause's own message, which is where the path leaks from.");

            // The TYPE is named, so nothing diagnostic was lost by withholding the message.
            Assert.Contains(
                nameof(UnauthorizedAccessException),
                described,
                StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory);
        }
    }

    /// <summary>Asserts a description names the key and does not reproduce the path.</summary>
    /// <param name="described">The rendered description.</param>
    /// <param name="path">The path that must not appear.</param>
    private static void AssertNamesTheKeyAndWithholds(string described, string path)
    {
        Assert.Contains(DataDirectoryFault.ConfigurationKey, described, StringComparison.Ordinal);
        Assert.Contains("DELIBERATELY NOT REPRODUCED", described, StringComparison.Ordinal);

        AssertWithholds(described, path);
    }

    /// <summary>Asserts a description does not reproduce a path, without rendering it.</summary>
    /// <param name="described">The rendered description.</param>
    /// <param name="path">The path that must not appear.</param>
    private static void AssertWithholds(string described, string path)
    {
        Assert.False(
            described.Contains(path, StringComparison.Ordinal),
            "The description reproduces a configured filesystem path.");

        // The leaf alone is checked too: a description that dropped the directory portion and kept the
        // final segment would still publish the mount's name.
        //
        // ONLY WHEN THE LEAF IS DISTINCTIVE ENOUGH TO DISCRIMINATE. A short, generic segment - "data" is
        // the obvious one - occurs in ordinary English prose, so asserting its absence would fail on the
        // description's own wording rather than on a leak. Every row above therefore uses a unique leaf,
        // and this guard states the requirement rather than leaving it as an unwritten convention.
        string leaf = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar));

        if (leaf.Length >= 12)
        {
            Assert.False(
                described.Contains(leaf, StringComparison.Ordinal),
                "The description reproduces the final segment of a configured filesystem path.");
        }
    }
}
