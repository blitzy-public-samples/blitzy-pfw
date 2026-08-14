// =====================================================================================================
//  WHERE AN ON DISK LOCATOR STARTS LOOKING
// =====================================================================================================
//
//  ⚠ THE DEFECT THIS EXISTS FOR. Every locator in this project that reads a real file from the source
//  tree - a published contract document, a proto file, the orchestration manifest, the localization
//  resource, a legacy oracle object under ws_objects - finds it by walking UP from the test assembly's
//  own directory until an ancestor carries a marker such as PowerFramework.slnx. That is sound while the
//  output sits inside the checkout, which is where the default layout puts it. It is not sound under the
//  SDK artifacts layout: `dotnet test --artifacts-path <dir>` with a directory outside the tree puts the
//  assembly somewhere no ancestor of which holds any marker, so every one of those locators threw - and
//  the suite failed for a reason with nothing to do with the code under test.
//
//  WHAT THIS CHANGES, WHICH IS ONLY THE STARTING POINT. The walks themselves are untouched. They still
//  climb, they still verify their marker, and they still report the same diagnostic when they find
//  nothing. All that changes is where the climb BEGINS: at the repository root when the build embedded
//  it, and at the output directory otherwise.
//
//  WHY THAT IS SAFE RATHER THAN A SHORTCUT. The embedded value is an ANCHOR, never an answer. A locator
//  handed a root that does not carry its marker rejects it and keeps climbing exactly as before, so a
//  stale path cannot make a test read the wrong tree - the worst it can do is start the walk one
//  directory away from where it would otherwise have started. And because the fallback is the original
//  behaviour, an environment that supplies no metadata at all is unaffected.
//
//  WHY IT IS DUPLICATED PER TEST PROJECT RATHER THAN SHARED. Constraint C-A requires each service to
//  build and test from a clean checkout with no reference to another service's projects, so a shared
//  test helper library would couple the four services through their test projects and defeat the
//  independence the per service solutions exist to demonstrate. The type is a few lines and reads one
//  attribute; a shared project to carry it would cost more than the repetition does.
// =====================================================================================================

using System.Reflection;
using Xunit;

namespace PowerFramework.Contracts.Tests;

/// <summary>
/// The directory an on disk locator in this project starts its search from.
/// </summary>
internal static class TestRepositoryRoot
{
    /// <summary>
    /// The metadata key the build publishes the repository root under.
    /// </summary>
    /// <remarks>
    /// It must match the <c>AssemblyMetadata</c> item in this project's own project file, which takes
    /// its value from the <c>PowerFrameworkRepositoryRoot</c> property in the repository root
    /// <c>Directory.Build.props</c>. Nothing fails loudly if the two ever disagree - the value simply
    /// reads as absent and every locator falls back to its walk - so the spelling is stated in exactly
    /// these two places and nowhere else.
    /// </remarks>
    internal const string MetadataKey = "PowerFrameworkRepositoryRoot";

    /// <summary>
    /// The repository root the build embedded, or <see langword="null"/> when none was embedded or the
    /// embedded path no longer exists.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE DIRECTORY IS TESTED FOR EXISTENCE, because an assembly can outlive the tree it was built
    /// from - copied to another machine, or built in a container whose source mount is gone. A path that
    /// is not there is worth nothing to a locator, so it reads as absent rather than being handed on for
    /// every caller to check.
    /// </para>
    /// <para>
    /// Resolved once and cached. It cannot change during a test run, and the attribute scan would
    /// otherwise repeat for every locator call in the suite.
    /// </para>
    /// </remarks>
    internal static string? Embedded { get; } = ResolveEmbedded();

    /// <summary>
    /// Where a walk up should begin: the embedded repository root when usable, and the test assembly's
    /// own directory otherwise.
    /// </summary>
    /// <remarks>
    /// <b>ALWAYS A USABLE STARTING DIRECTORY</b>, so a caller substitutes this for
    /// <see cref="AppContext.BaseDirectory"/> and needs no branch of its own. When the embedded root IS
    /// the repository root the walk terminates on its first iteration, and when it is absent the
    /// behaviour is identical to what it was before this type existed.
    /// </remarks>
    internal static string SearchStart => Embedded ?? AppContext.BaseDirectory;

    /// <summary>Reads the embedded root from this assembly's metadata.</summary>
    /// <returns>The directory, or <see langword="null"/> when absent, blank or not present on disk.</returns>
    private static string? ResolveEmbedded()
    {
        string? value = Assembly.GetExecutingAssembly()
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute =>
                string.Equals(attribute.Key, MetadataKey, StringComparison.Ordinal))
            ?.Value;

        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        // MSBuildThisFileDirectory carries a trailing separator, which DirectoryInfo.Parent treats as a
        // level of its own - so it is trimmed here, once, rather than at each of the call sites.
        string trimmed = value.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return Directory.Exists(trimmed) ? trimmed : null;
    }
}

/// <summary>
/// Conformance for the anchor itself: that the build actually publishes it and that it names this
/// repository.
/// </summary>
/// <remarks>
/// <b>⚠ WITHOUT THIS TEST THE ANCHOR CAN BREAK SILENTLY, AND THAT IS THE WHOLE REASON IT EXISTS.</b>
/// Every locator keeps its walk as a fallback, which is what makes the anchor safe - and also what makes
/// its absence invisible: if the <c>AssemblyMetadata</c> item were removed from this project file, or its
/// key spelled differently from <see cref="TestRepositoryRoot.MetadataKey"/>, every locator would quietly
/// revert to the fragile walk and the whole suite would still pass IN TREE. The regression would surface
/// only in the one configuration the fix was made for. Asserting the anchor directly is what turns that
/// silent reversion into a failure.
/// </remarks>
public sealed class TestRepositoryRootTests
{
    /// <summary>
    /// The build publishes the repository root, and the path it publishes is this repository.
    /// </summary>
    [Fact]
    public void TheBuildPublishesAnAnchorThatNamesThisRepository()
    {
        Assert.NotNull(TestRepositoryRoot.Embedded);

        // NAMED BY ITS OWN MARKER. Asserting only that the directory exists would pass for any directory;
        // the solution file is what identifies it as the repository root rather than some other path.
        Assert.True(
            File.Exists(Path.Combine(TestRepositoryRoot.Embedded, "PowerFramework.slnx")),
            $"The embedded anchor '{TestRepositoryRoot.Embedded}' does not carry PowerFramework.slnx, so "
                + "it is not this repository's root and every locator would fall back to its walk.");

        // NO TRAILING SEPARATOR, because DirectoryInfo.Parent treats one as a level of its own - a walk
        // started on an untrimmed path would visit the same directory twice before climbing.
        Assert.Equal(
            TestRepositoryRoot.Embedded,
            TestRepositoryRoot.Embedded.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar));
    }

    /// <summary>
    /// The search start is always usable, and prefers the anchor when there is one.
    /// </summary>
    /// <remarks>
    /// THE SECOND ASSERTION IS WHAT MAKES THE LOCATORS' CALL SITES CORRECT. They substitute
    /// <see cref="TestRepositoryRoot.SearchStart"/> for the output directory with no branch of their own,
    /// so it must never be blank whatever the environment supplies.
    /// </remarks>
    [Fact]
    public void TheSearchStartIsAlwaysUsableAndPrefersTheAnchor()
    {
        Assert.False(string.IsNullOrWhiteSpace(TestRepositoryRoot.SearchStart));
        Assert.True(Directory.Exists(TestRepositoryRoot.SearchStart));

        Assert.Equal(
            TestRepositoryRoot.Embedded ?? AppContext.BaseDirectory,
            TestRepositoryRoot.SearchStart);
    }
}
