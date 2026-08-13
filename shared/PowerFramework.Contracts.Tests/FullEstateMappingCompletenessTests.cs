// =====================================================================================================
//  FullEstateMappingCompletenessTests - THE EXECUTABLE GUARD ON THE FULL-ESTATE MAPPING
// =====================================================================================================
//
//  WHAT THIS FILE IS FOR
//    AAP G8 asks for one deliverable above all others in the discovery half of this refactor: "the
//    library-to-service mapping for all 544 objects including deferred assignments and their
//    justification", presented for review before code generation. docs/SERVICE_MAPPING.md IS that
//    deliverable, and AAP 0.2.2.4 states the acceptance condition in one line - "Zero objects are
//    unassigned" - together with the arithmetic that proves it: 103 in scope + 120 split remainders +
//    205 whole-library deferred + 116 permanently out = 544.
//
//    Until now that claim was prose. A document asserting its own completeness cannot detect an object
//    appearing in ws_objects/**, a ledger row losing its destination, or a subtotal drifting away from
//    the rows it is supposed to summarise - and every one of those turns the G8 deliverable into a
//    document that describes an estate the repository no longer has. This file re-derives the estate
//    from the filesystem on every run and adjudicates the document against it.
//
//  WHY IT LIVES IN THE CONTRACTS TEST PROJECT
//    The mapping is not a property of any one service, so it cannot sit in a service's test project
//    without making that service's suite the owner of an estate-wide fact. PowerFramework.Contracts is
//    the boundary-definition project every service already depends on and the only project whose
//    subject is the whole system, so its test project is where a whole-estate assertion belongs. It is
//    also the one place a check like this cannot break the per-service independence constraint C-A
//    demands, because no service references it.
//
//  THE THREE-WAY AGREEMENT IT ENFORCES
//    1. THE FILESYSTEM - ws_objects/** scanned live. This is the only authority on what the estate
//       actually contains, and AAP 0.2.2.1 makes it read-only, so the scan reads and never writes.
//    2. THE SECTION 13 LEDGER - 544 rows, each carrying exactly one category, one destination and one
//       role. Asserted to be a BIJECTION with the filesystem: every path once, nothing missing,
//       nothing invented.
//    3. THE SUMMARY TABLES - Sections 3.1, 3.2, 11.1, 11.2, 11.3, 11.4 and 11.5. The document states
//       that these are "this table filtered on a column and counted - none of them is an independent
//       tally that could drift out of agreement with the rows below". That sentence is a testable
//       claim, and here it is tested: every derived subtotal is recomputed from the ledger and
//       compared with the published figure.
//
//  THE TWO APPORTIONMENTS, AND WHY BOTH ARE CHECKED
//    Section 11 publishes two counts of the same 544 objects and is explicit that they are not equal.
//    The HEADLINE of 11.1 is the plan's own frozen reconciliation (103/120/205/116) and is
//    authoritative. The STRICT count of 11.3 (103/128/195/118) is taken one-object-one-category
//    directly over the ledger. Section 11.3 accounts for the whole of the difference as three named
//    attributions netting to zero. So the two are checked differently and deliberately:
//      * the strict count must equal the ledger's own category counts EXACTLY - it is derived, so any
//        difference is drift;
//      * the headline must total 544, must agree with the strict count on the in-scope figure of 103,
//        and its differences from the strict count must still net to zero.
//    Asserting the headline equals the ledger would be wrong: it would fail the document for the three
//    judgements it documents rather than for a defect.
//
//  READ-ONLY THROUGHOUT. Nothing here writes to ws_objects/** or to docs/SERVICE_MAPPING.md. A
//  disagreement is reported as a test failure naming the object or the figure, which is a finding for a
//  human to adjudicate - never something for a test to reconcile by adjusting the document.
// =====================================================================================================

using System.Collections.Immutable;
using System.Globalization;
using System.Text.RegularExpressions;
using Xunit;
using Xunit.Sdk;

namespace PowerFramework.Contracts.Tests;

/// <summary>
/// One row of the Section 13 object-level ledger.
/// </summary>
/// <param name="Index">The one-based row number as the document prints it.</param>
/// <param name="Path">The object path relative to <c>ws_objects/</c>, with forward slashes.</param>
/// <param name="Category">Exactly one of the four category values.</param>
/// <param name="Destination">
/// Exactly one destination, or <c>-</c> which the document reserves for permanently-out rows.
/// </param>
/// <param name="Role">Exactly one of the five role values.</param>
internal sealed record LedgerRow(
    int Index,
    string Path,
    string Category,
    string Destination,
    string Role)
{
    /// <summary>Gets the library name, which is the path segment with its export suffix removed.</summary>
    internal string Library =>
        Path[..Path.IndexOf('/', StringComparison.Ordinal)]
            .Replace(".pbl.src", string.Empty, StringComparison.Ordinal);
}

/// <summary>
/// The estate as the filesystem has it, and the mapping document as written, both read fresh.
/// </summary>
/// <remarks>
/// Resolved once per test run. Neither the legacy tree nor the document can change during a run, and
/// re-reading a 1,600-line document and re-walking thirty-nine directories for every one of the checks
/// below would cost more than it proves.
/// </remarks>
internal static class EstateInventory
{
    /// <summary>The repository markers the root walk requires, so it cannot latch onto a namesake.</summary>
    private const string SolutionMarkerFileName = "PowerFramework.slnx";

    /// <summary>The second repository marker, required together with the first.</summary>
    private const string PackageVersionMarkerFileName = "Directory.Packages.props";

    /// <summary>The read-only legacy tree: the only authority on what the estate contains.</summary>
    private const string LegacyTreeDirectoryName = "ws_objects";

    /// <summary>The G8 deliverable this file adjudicates.</summary>
    private const string MappingDocumentRelativePath = "docs/SERVICE_MAPPING.md";

    /// <summary>A bound on every regex here, so a pathological input cannot hang a run.</summary>
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// One ledger row: index, backticked path, category, destination, role.
    /// </summary>
    /// <remarks>
    /// The path is REQUIRED to be backticked, which is what separates ledger rows from the column-meaning
    /// table that precedes them in the same section: that table's rows have unbackticked prose in the
    /// second cell, so a laxer pattern would silently absorb them as objects.
    /// </remarks>
    private static readonly Regex LedgerRowPattern = new(
        @"^\|\s*(?<index>\d+)\s*\|\s*`(?<path>[^`]+)`\s*\|\s*(?<category>[^|]+?)\s*\|\s*(?<destination>[^|]+?)\s*\|\s*(?<role>[^|]+?)\s*\|\s*$",
        RegexOptions.ExplicitCapture,
        MatchTimeout);

    /// <summary>A two-cell row whose second cell is a count, with optional bold markers.</summary>
    private static readonly Regex LabelledCountPattern = new(
        @"^\|\s*\*{0,2}(?<label>[^|*]+?)\*{0,2}\s*\|\s*\*{0,2}(?<count>\d+)\*{0,2}\s*\|\s*$",
        RegexOptions.ExplicitCapture,
        MatchTimeout);

    /// <summary>Gets the absolute repository root.</summary>
    internal static string RepositoryRoot { get; } = ResolveRepositoryRoot();

    /// <summary>
    /// Gets every object under <c>ws_objects/</c>, as <c>&lt;library folder&gt;/&lt;object&gt;</c>, sorted.
    /// </summary>
    /// <remarks>
    /// The document states how to reproduce this set - <c>ls -1 ws_objects/*/*</c> - so the walk is
    /// deliberately exactly that shape: the immediate children of the immediate subdirectories, one level
    /// and no deeper. Recursing would count files the published set does not.
    /// </remarks>
    internal static ImmutableArray<string> FilesystemObjects { get; } = ScanLegacyTree();

    /// <summary>Gets the export library folder names, with their <c>.pbl.src</c> suffix removed, sorted.</summary>
    internal static ImmutableArray<string> FilesystemLibraries { get; } =
        [.. FilesystemObjects
            .Select(static path => path[..path.IndexOf('/', StringComparison.Ordinal)]
                .Replace(".pbl.src", string.Empty, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];

    /// <summary>Gets the whole mapping document.</summary>
    internal static string DocumentText { get; } = ReadMappingDocument();

    /// <summary>Gets the Section 13 ledger, in document order.</summary>
    internal static ImmutableArray<LedgerRow> Ledger { get; } = ParseLedger();

    /// <summary>Gets the number of ledger rows in each category.</summary>
    internal static ImmutableDictionary<string, int> LedgerCategoryCounts { get; } =
        Ledger.GroupBy(static row => row.Category, StringComparer.Ordinal)
            .ToImmutableDictionary(
                static group => group.Key,
                static group => group.Count(),
                StringComparer.Ordinal);

    /// <summary>Gets the number of ledger rows against each destination.</summary>
    internal static ImmutableDictionary<string, int> LedgerDestinationCounts { get; } =
        Ledger.GroupBy(static row => row.Destination, StringComparer.Ordinal)
            .ToImmutableDictionary(
                static group => group.Key,
                static group => group.Count(),
                StringComparer.Ordinal);

    /// <summary>Gets the number of ledger rows per library.</summary>
    internal static ImmutableDictionary<string, int> LedgerLibraryCounts { get; } =
        Ledger.GroupBy(static row => row.Library, StringComparer.Ordinal)
            .ToImmutableDictionary(
                static group => group.Key,
                static group => group.Count(),
                StringComparer.Ordinal);

    /// <summary>
    /// Returns the text of one numbered section, from its heading to the next heading of any level.
    /// </summary>
    /// <param name="headingPrefix">The heading line's leading text, such as <c>### 11.1</c>.</param>
    /// <returns>The section body including its heading line.</returns>
    internal static string Section(string headingPrefix)
    {
        string[] lines = DocumentText.Split('\n');

        int start = Array.FindIndex(
            lines,
            line => line.StartsWith(headingPrefix, StringComparison.Ordinal));

        if (start < 0)
        {
            throw FailException.ForFailure(
                $"'{MappingDocumentRelativePath}' has no heading beginning '{headingPrefix}'. The "
                    + "reconciliation this suite adjudicates is published under numbered headings, so a "
                    + "renumbered or removed section is a finding rather than a reason to skip a check.");
        }

        int end = Array.FindIndex(
            lines,
            start + 1,
            static line => line.StartsWith('#'));

        return string.Join('\n', lines[start..(end < 0 ? lines.Length : end)]);
    }

    /// <summary>Reads every <c>| label | count |</c> row out of one section.</summary>
    /// <param name="headingPrefix">The section heading's leading text.</param>
    /// <returns>The labelled counts, in document order.</returns>
    internal static ImmutableArray<KeyValuePair<string, int>> LabelledCounts(string headingPrefix)
    {
        ImmutableArray<KeyValuePair<string, int>>.Builder counts =
            ImmutableArray.CreateBuilder<KeyValuePair<string, int>>();

        foreach (string line in Section(headingPrefix).Split('\n'))
        {
            Match match = LabelledCountPattern.Match(line.Trim());

            if (match.Success)
            {
                counts.Add(new KeyValuePair<string, int>(
                    match.Groups["label"].Value.Trim(),
                    int.Parse(match.Groups["count"].Value, CultureInfo.InvariantCulture)));
            }
        }

        return counts.ToImmutable();
    }

    /// <summary>Finds one labelled count in a section, failing with a diagnostic when it is absent.</summary>
    /// <param name="headingPrefix">The section heading's leading text.</param>
    /// <param name="label">The row label, matched exactly after bold markers are stripped.</param>
    /// <returns>The published count.</returns>
    internal static int LabelledCount(string headingPrefix, string label)
    {
        foreach (KeyValuePair<string, int> candidate in LabelledCounts(headingPrefix))
        {
            if (string.Equals(candidate.Key, label, StringComparison.Ordinal))
            {
                return candidate.Value;
            }
        }

        throw FailException.ForFailure(
            $"Section '{headingPrefix}' of '{MappingDocumentRelativePath}' has no row labelled "
                + $"'{label}'.");
    }

    /// <summary>Matches a pattern against the whole document, failing when it does not match.</summary>
    /// <param name="pattern">The pattern, which must carry the named groups the caller reads.</param>
    /// <param name="what">What the caller was looking for, for the diagnostic.</param>
    /// <returns>The match.</returns>
    internal static Match RequireMatch(string pattern, string what)
    {
        Match match = Regex.Match(DocumentText, pattern, RegexOptions.ExplicitCapture, MatchTimeout);

        return match.Success
            ? match
            : throw FailException.ForFailure(
                $"'{MappingDocumentRelativePath}' no longer states {what}. Searched for: {pattern}");
    }

    /// <summary>Walks up to the repository root.</summary>
    /// <returns>The absolute root path.</returns>
    private static string ResolveRepositoryRoot()
    {
        // Starts at the embedded root when the build supplied one, so this works under
        // `dotnet test --artifacts-path`. See TestRepositoryRoot.
        DirectoryInfo? candidate = new(TestRepositoryRoot.SearchStart);

        while (candidate is not null)
        {
            if (File.Exists(Path.Combine(candidate.FullName, SolutionMarkerFileName))
                && File.Exists(Path.Combine(candidate.FullName, PackageVersionMarkerFileName))
                && Directory.Exists(Path.Combine(candidate.FullName, LegacyTreeDirectoryName)))
            {
                return candidate.FullName;
            }

            candidate = candidate.Parent;
        }

        throw FailException.ForFailure(
            $"No directory from '{TestRepositoryRoot.SearchStart}' up to the filesystem root holds "
                + $"'{LegacyTreeDirectoryName}' together with both repository markers "
                + $"'{SolutionMarkerFileName}' and '{PackageVersionMarkerFileName}'.");
    }

    /// <summary>Scans <c>ws_objects/</c> one level deep.</summary>
    /// <returns>The object paths, sorted.</returns>
    private static ImmutableArray<string> ScanLegacyTree()
    {
        string tree = Path.Combine(RepositoryRoot, LegacyTreeDirectoryName);

        ImmutableArray<string>.Builder objects = ImmutableArray.CreateBuilder<string>();

        foreach (string library in Directory.EnumerateDirectories(tree))
        {
            string libraryName = Path.GetFileName(library);

            foreach (string entry in Directory.EnumerateFiles(library))
            {
                objects.Add(libraryName + "/" + Path.GetFileName(entry));
            }
        }

        objects.Sort(StringComparer.Ordinal);

        return objects.ToImmutable();
    }

    /// <summary>Reads the mapping document.</summary>
    /// <returns>Its whole text, with line endings normalised so line-based slicing is platform-neutral.</returns>
    private static string ReadMappingDocument()
    {
        string path = Path.Combine(
            RepositoryRoot,
            MappingDocumentRelativePath.Replace('/', Path.DirectorySeparatorChar));

        return File.Exists(path)
            ? File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal)
            : throw FailException.ForFailure(
                $"'{MappingDocumentRelativePath}' does not exist. It is the AAP G8 full-estate mapping "
                    + "deliverable and the artifact the environment's STEP 0 gate names, so its absence "
                    + "is a finding rather than a reason to skip a check.");
    }

    /// <summary>Parses the Section 13 ledger.</summary>
    /// <returns>The rows, in document order.</returns>
    private static ImmutableArray<LedgerRow> ParseLedger()
    {
        ImmutableArray<LedgerRow>.Builder rows = ImmutableArray.CreateBuilder<LedgerRow>();

        foreach (string line in Section("## 13.").Split('\n'))
        {
            Match match = LedgerRowPattern.Match(line.Trim());

            if (match.Success)
            {
                rows.Add(new LedgerRow(
                    int.Parse(match.Groups["index"].Value, CultureInfo.InvariantCulture),
                    match.Groups["path"].Value,
                    match.Groups["category"].Value,
                    match.Groups["destination"].Value,
                    match.Groups["role"].Value));
            }
        }

        return rows.ToImmutable();
    }
}

/// <summary>
/// The full-estate mapping is complete, is a bijection with the read-only legacy tree, and every
/// reconciliation figure it publishes is the ledger recomputed rather than an independent tally.
/// </summary>
public sealed class FullEstateMappingCompletenessTests
{
    /// <summary>The estate size AAP 0.1.1 fixes and Section 3.1 measures.</summary>
    private const int ExpectedObjects = 544;

    /// <summary>The number of export library folders AAP 0.1.1 fixes and Section 3.1 measures.</summary>
    private const int ExpectedLibraries = 39;

    /// <summary>The in-scope figure, identical under both apportionments (Section 11.3).</summary>
    private const int ExpectedInScope = 103;

    /// <summary>The four category values the ledger's category column may take.</summary>
    private static readonly ImmutableArray<string> Categories =
        ["In scope", "Deferred — split", "Deferred — whole", "Permanently out"];

    /// <summary>The five role values the ledger's role column may take.</summary>
    private static readonly ImmutableArray<string> Roles =
        ["Ported", "Not built", "REFERENCE", "Fixture source", "Legacy tooling"];

    /// <summary>The four deferred destinations, which are also the four reserved Gateway routes.</summary>
    private static readonly ImmutableArray<string> DeferredDestinations =
        ["DesignSystem", "Documents", "Integration", "ScriptBridge"];

    /// <summary>The destination a permanently-out row carries, there being no destination to name.</summary>
    private const string NoDestination = "-";

    // ==============================================================================================
    //  1 - THE MEASURED ESTATE
    // ==============================================================================================

    /// <summary>
    /// Section 3.1's measured totals are what the filesystem actually holds: thirty-nine libraries and
    /// five hundred and forty-four objects.
    /// </summary>
    /// <remarks>
    /// THE FIGURES ARE ASSERTED THREE WAYS, and the third is the one that matters. Each is compared with
    /// the constant AAP 0.1.1 fixes, so a document that drifted from the plan fails; each is compared with
    /// the live filesystem, so a document that drifted from the ESTATE fails; and the two are compared
    /// with each other, so a repository whose legacy tree changed under a still-accurate-looking document
    /// fails as well.
    /// </remarks>
    [Fact]
    public void TheMeasuredTotalsAreWhatTheFilesystemHolds()
    {
        Match libraries = EstateInventory.RequireMatch(
            @"\|\s*Export library folders under `ws_objects/`\s*\|\s*\*\*(?<count>\d+)\*\*",
            "how many export library folders ws_objects/ holds");
        Match objects = EstateInventory.RequireMatch(
            @"\|\s*Total objects under `ws_objects/`\s*\|\s*\*\*(?<count>\d+)\*\*",
            "how many objects ws_objects/ holds");

        int publishedLibraries = int.Parse(
            libraries.Groups["count"].Value,
            CultureInfo.InvariantCulture);
        int publishedObjects = int.Parse(objects.Groups["count"].Value, CultureInfo.InvariantCulture);

        Assert.Equal(ExpectedLibraries, publishedLibraries);
        Assert.Equal(ExpectedObjects, publishedObjects);

        Assert.Equal(publishedLibraries, EstateInventory.FilesystemLibraries.Length);
        Assert.Equal(publishedObjects, EstateInventory.FilesystemObjects.Length);
    }

    /// <summary>
    /// Section 3.1's breakdown by file extension sums to the estate size and matches the filesystem
    /// extension by extension.
    /// </summary>
    /// <remarks>
    /// The breakdown is what ties the estate size to the PowerBuilder object-kind census AAP 0.1.1 states
    /// - three application objects, seventy-one windows, and so on - so an extension count that no longer
    /// matches the tree breaks that reconciliation even when the grand total still happens to agree.
    /// </remarks>
    [Fact]
    public void TheExtensionBreakdownMatchesTheFilesystemExtensionByExtension()
    {
        Match breakdown = EstateInventory.RequireMatch(
            @"\|\s*Object breakdown by extension\s*\|(?<body>[^|]+)\|",
            "the object breakdown by extension");

        MatchCollection terms = Regex.Matches(
            breakdown.Groups["body"].Value,
            @"(?<count>\d+)\s*`(?<extension>\.[A-Za-z]+)`",
            RegexOptions.ExplicitCapture,
            TimeSpan.FromSeconds(5));

        Assert.NotEmpty(terms);

        Dictionary<string, int> measured = EstateInventory.FilesystemObjects
            .GroupBy(static path => Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                static group => group.Count(),
                StringComparer.OrdinalIgnoreCase);

        int published = 0;

        foreach (Match term in terms)
        {
            string extension = term.Groups["extension"].Value;
            int count = int.Parse(term.Groups["count"].Value, CultureInfo.InvariantCulture);

            published += count;

            Assert.True(
                measured.TryGetValue(extension, out int actual),
                $"The breakdown names '{extension}', which no object under ws_objects/ carries.");
            Assert.Equal(count, actual);
        }

        Assert.Equal(ExpectedObjects, published);

        // And no extension the tree carries is missing from the breakdown, which is the direction that
        // catches a new object KIND rather than a new object.
        Assert.Equal(terms.Count, measured.Count);
    }

    /// <summary>
    /// Section 3.2's per-library counts cover all thirty-nine libraries, match the filesystem library by
    /// library, and sum to the estate size.
    /// </summary>
    [Fact]
    public void ThePerLibraryCountsMatchTheFilesystemLibraryByLibrary()
    {
        Dictionary<string, int> published = [];

        foreach (Match row in Regex.Matches(
            EstateInventory.Section("### 3.2"),
            @"`(?<library>[A-Za-z0-9.]+)`\s*\|\s*(?<count>\d+)",
            RegexOptions.ExplicitCapture,
            TimeSpan.FromSeconds(5)))
        {
            published[row.Groups["library"].Value] =
                int.Parse(row.Groups["count"].Value, CultureInfo.InvariantCulture);
        }

        Dictionary<string, int> measured = EstateInventory.FilesystemObjects
            .GroupBy(
                static path => path[..path.IndexOf('/', StringComparison.Ordinal)]
                    .Replace(".pbl.src", string.Empty, StringComparison.Ordinal),
                StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group.Count(),
                StringComparer.Ordinal);

        Assert.Equal(ExpectedLibraries, published.Count);
        Assert.Equal(measured.Count, published.Count);
        Assert.Equal(ExpectedObjects, published.Values.Sum());

        foreach (KeyValuePair<string, int> library in measured.OrderBy(
            static entry => entry.Key,
            StringComparer.Ordinal))
        {
            Assert.True(
                published.TryGetValue(library.Key, out int count),
                $"Section 3.2 does not list the library '{library.Key}', which exists on disk with "
                    + $"{library.Value.ToString(CultureInfo.InvariantCulture)} objects.");
            Assert.Equal(library.Value, count);
        }

        // The ledger must agree with the same per-library counts, so a library cannot be right in the
        // summary and wrong in the rows.
        foreach (KeyValuePair<string, int> library in published)
        {
            Assert.True(
                EstateInventory.LedgerLibraryCounts.TryGetValue(library.Key, out int inLedger),
                $"The Section 13 ledger carries no row for the library '{library.Key}'.");
            Assert.Equal(library.Value, inLedger);
        }
    }

    // ==============================================================================================
    //  2 - THE LEDGER IS THE ESTATE, EXACTLY ONCE EACH
    // ==============================================================================================

    /// <summary>
    /// The Section 13 ledger is a bijection with <c>ws_objects/**</c>: every object appears exactly once,
    /// nothing is missing and nothing is invented.
    /// </summary>
    /// <remarks>
    /// <b>THIS IS THE "ZERO OBJECTS ARE UNASSIGNED" ASSERTION</b> (AAP 0.2.2.4), and it is the reason this
    /// file exists. A total can be right while the rows are wrong - one object dropped and another
    /// duplicated leaves 544 intact - so the assertion is set equality plus a duplicate check, and the
    /// diagnostic names the offending paths rather than reporting a count.
    /// </remarks>
    [Fact]
    public void TheLedgerIsABijectionWithTheLegacyTree()
    {
        ImmutableArray<LedgerRow> ledger = EstateInventory.Ledger;

        Assert.Equal(ExpectedObjects, ledger.Length);

        // The printed row numbers run 1..544 with nothing skipped, so a row lost to a copy-paste shows up
        // here even if the path set happens to stay complete.
        Assert.Equal(
            Enumerable.Range(1, ExpectedObjects),
            ledger.Select(static row => row.Index));

        ImmutableArray<string> duplicated =
            [.. ledger.GroupBy(static row => row.Path, StringComparer.Ordinal)
                .Where(static group => group.Count() > 1)
                .Select(static group => group.Key)
                .Order(StringComparer.Ordinal)];

        Assert.Empty(duplicated);

        HashSet<string> onDisk = [.. EstateInventory.FilesystemObjects];
        HashSet<string> inLedger = [.. ledger.Select(static row => row.Path)];

        ImmutableArray<string> unassigned = [.. onDisk.Except(inLedger).Order(StringComparer.Ordinal)];
        ImmutableArray<string> invented = [.. inLedger.Except(onDisk).Order(StringComparer.Ordinal)];

        Assert.True(
            unassigned.IsEmpty,
            "These objects exist under ws_objects/ and have NO ledger row, so the mapping's "
                + "'zero objects are unassigned' claim (AAP 0.2.2.4) no longer holds: "
                + string.Join(", ", unassigned));
        Assert.True(
            invented.IsEmpty,
            "These ledger rows name objects that do not exist under ws_objects/, so the mapping "
                + "describes an estate this repository does not have: " + string.Join(", ", invented));
    }

    /// <summary>
    /// Every ledger row carries exactly one category, exactly one destination and exactly one role, each
    /// drawn from its closed value set.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The document's own words: "each row carries exactly one category, exactly one destination and
    /// exactly one role". Two things are asserted from that. Each value is a MEMBER of the published set,
    /// so a typo or an invented destination fails rather than quietly forming a new group that every
    /// subtotal then misses. And no cell carries a separator - a comma, a slash, or the word "and" - which
    /// is how "exactly one" fails in a hand-maintained table.
    /// </para>
    /// <para>
    /// The destination sentinel is asserted as an EQUIVALENCE: a row has no destination if and only if it
    /// is permanently out. A deferred or in-scope row with <c>-</c> would be an unassigned object wearing
    /// an assigned category, which is the exact failure the G8 deliverable exists to rule out.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryLedgerRowCarriesExactlyOneCategoryDestinationAndRole()
    {
        ImmutableArray<string> destinations =
            [.. EstateInventory.LedgerDestinationCounts.Keys.Order(StringComparer.Ordinal)];

        Assert.All(
            destinations,
            destination => Assert.False(
                string.IsNullOrWhiteSpace(destination),
                "A ledger row carries an empty destination."));

        Assert.All(
            EstateInventory.Ledger,
            row =>
            {
                Assert.Contains(row.Category, Categories);
                Assert.Contains(row.Role, Roles);

                Assert.False(
                    string.IsNullOrWhiteSpace(row.Destination),
                    $"Row {row.Index.ToString(CultureInfo.InvariantCulture)} ({row.Path}) carries no "
                        + "destination.");

                foreach (string separator in (string[])[",", "/", " and ", "+"])
                {
                    Assert.DoesNotContain(separator, row.Destination, StringComparison.Ordinal);
                    Assert.DoesNotContain(separator, row.Category, StringComparison.Ordinal);
                    Assert.DoesNotContain(separator, row.Role, StringComparison.Ordinal);
                }

                bool permanentlyOut = string.Equals(row.Category, "Permanently out", StringComparison.Ordinal);
                bool noDestination = string.Equals(row.Destination, NoDestination, StringComparison.Ordinal);

                Assert.Equal(permanentlyOut, noDestination);
            });
    }

    // ==============================================================================================
    //  3 - EVERY RECONCILIATION TABLE IS THE LEDGER RECOMPUTED
    // ==============================================================================================

    /// <summary>
    /// Section 11.1's headline reconciliation totals the estate, and its published total row says so.
    /// </summary>
    /// <remarks>
    /// The headline is the plan's own frozen figure set, so it is NOT required to equal the ledger's
    /// category counts - Section 11.3 documents three judgements that separate them. What it is required
    /// to do is add up, agree with its own total row, and agree with the strict count on the in-scope
    /// figure, which Section 11.3 states is "identical under either".
    /// </remarks>
    [Fact]
    public void TheHeadlineReconciliationAddsUpToTheWholeEstate()
    {
        int inScope = EstateInventory.LabelledCount("### 11.1", "In scope");
        int splitRemainders = EstateInventory.LabelledCount("### 11.1", "Deferred — split remainders");
        int wholeLibraries = EstateInventory.LabelledCount("### 11.1", "Deferred — whole libraries");
        int permanentlyOut = EstateInventory.LabelledCount("### 11.1", "Permanently out of scope");
        int total = EstateInventory.LabelledCount("### 11.1", "Total");

        Assert.Equal(ExpectedInScope, inScope);
        Assert.Equal(ExpectedObjects, total);
        Assert.Equal(total, inScope + splitRemainders + wholeLibraries + permanentlyOut);

        // The deferred total the section states in prose, and DEFERRED.md restates, is the two deferred
        // figures added.
        Match deferred = EstateInventory.RequireMatch(
            @"The deferred total is 120 \+ 205 = \*\*(?<count>\d+) objects across the four",
            "the deferred total as the sum of its two categories");

        Assert.Equal(
            splitRemainders + wholeLibraries,
            int.Parse(deferred.Groups["count"].Value, CultureInfo.InvariantCulture));

        // The in-scope figure is the one both apportionments share, so it must equal the ledger's.
        Assert.Equal(inScope, EstateInventory.LedgerCategoryCounts["In scope"]);
    }

    /// <summary>
    /// Section 11.3's strict apportionment is exactly the ledger's own category counts, and its
    /// differences from the headline net to zero.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE STRICT COUNT IS DERIVED, SO EQUALITY IS THE RIGHT TEST. The document describes it as "taken
    /// directly over the Section 13 ledger", which means any difference between the four figures and the
    /// four category counts is drift and nothing else.
    /// </para>
    /// <para>
    /// The netting check is the audit Section 11.3 offers about itself - "those three differences net to
    /// zero - as they must, since both apportionments count the same 544 objects". Asserted here so that
    /// changing one figure without the other three fails.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheStrictApportionmentIsTheLedgerRecomputed()
    {
        Match strict = EstateInventory.RequireMatch(
            @"\*\*(?<inScope>\d+) in scope \+ (?<split>\d+) split remainders \+ (?<whole>\d+) "
                + @"whole-library deferred \+ (?<permanent>\d+) permanently out =\s+(?<total>\d+)\*\*",
            "the strict one-object-one-category apportionment");

        int inScope = int.Parse(strict.Groups["inScope"].Value, CultureInfo.InvariantCulture);
        int split = int.Parse(strict.Groups["split"].Value, CultureInfo.InvariantCulture);
        int whole = int.Parse(strict.Groups["whole"].Value, CultureInfo.InvariantCulture);
        int permanent = int.Parse(strict.Groups["permanent"].Value, CultureInfo.InvariantCulture);
        int total = int.Parse(strict.Groups["total"].Value, CultureInfo.InvariantCulture);

        Assert.Equal(ExpectedObjects, total);
        Assert.Equal(total, inScope + split + whole + permanent);

        Assert.Equal(EstateInventory.LedgerCategoryCounts["In scope"], inScope);
        Assert.Equal(EstateInventory.LedgerCategoryCounts["Deferred — split"], split);
        Assert.Equal(EstateInventory.LedgerCategoryCounts["Deferred — whole"], whole);
        Assert.Equal(EstateInventory.LedgerCategoryCounts["Permanently out"], permanent);

        // Every category is populated, so none of the four is a heading with no rows behind it.
        Assert.Equal(Categories.Length, EstateInventory.LedgerCategoryCounts.Count);

        // The three movements against the headline net to zero.
        int headlineSplit = EstateInventory.LabelledCount("### 11.1", "Deferred — split remainders");
        int headlineWhole = EstateInventory.LabelledCount("### 11.1", "Deferred — whole libraries");
        int headlinePermanent = EstateInventory.LabelledCount("### 11.1", "Permanently out of scope");

        Assert.Equal(
            0,
            (split - headlineSplit) + (whole - headlineWhole) + (permanent - headlinePermanent));

        Match deferredTotal = EstateInventory.RequireMatch(
            @"with a deferred total of (?<count>\d+) and, again, \*\*zero objects unassigned\*\*",
            "the strict deferred total");

        Assert.Equal(
            split + whole,
            int.Parse(deferredTotal.Groups["count"].Value, CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Section 11.2's deferred-by-destination table is the ledger cross-tabulated: each destination's
    /// total, its split-remainder half and its whole-library half.
    /// </summary>
    /// <remarks>
    /// The four deferred destinations are also the four reserved Gateway routes, so this table is what
    /// ties the estate to the routing metadata AAP 0.4.4 declares. A destination that lost its rows would
    /// leave a route reserved for a capability area the mapping no longer assigns anything to.
    /// </remarks>
    [Fact]
    public void TheDeferredEstateByDestinationIsTheLedgerCrossTabulated()
    {
        string section = EstateInventory.Section("### 11.2");

        int publishedTotal = 0;
        int publishedSplit = 0;
        int publishedWhole = 0;

        foreach (string destination in DeferredDestinations)
        {
            Match row = Regex.Match(
                section,
                @"\|\s*" + Regex.Escape(destination)
                    + @"\s*\|\s*(?<total>\d+)\s*\|\s*(?<split>\d+|—)\s*\|\s*(?<whole>\d+|—)\s*\|",
                RegexOptions.ExplicitCapture,
                TimeSpan.FromSeconds(5));

            Assert.True(
                row.Success,
                $"Section 11.2 has no row for the deferred destination '{destination}', which AAP 0.4.4 "
                    + "reserves a Gateway route for.");

            int total = int.Parse(row.Groups["total"].Value, CultureInfo.InvariantCulture);
            int split = ParseCountOrDash(row.Groups["split"].Value);
            int whole = ParseCountOrDash(row.Groups["whole"].Value);

            Assert.Equal(total, split + whole);

            Assert.Equal(total, EstateInventory.LedgerDestinationCounts[destination]);
            Assert.Equal(split, CountDeferred(destination, "Deferred — split"));
            Assert.Equal(whole, CountDeferred(destination, "Deferred — whole"));

            publishedTotal += total;
            publishedSplit += split;
            publishedWhole += whole;
        }

        Match totals = Regex.Match(
            section,
            @"\|\s*\*\*Total\*\*\s*\|\s*\*\*(?<total>\d+)\*\*\s*\|\s*\*\*(?<split>\d+)\*\*\s*\|\s*\*\*(?<whole>\d+)\*\*\s*\|",
            RegexOptions.ExplicitCapture,
            TimeSpan.FromSeconds(5));

        Assert.True(totals.Success, "Section 11.2 has no bold total row.");

        Assert.Equal(publishedTotal, int.Parse(totals.Groups["total"].Value, CultureInfo.InvariantCulture));
        Assert.Equal(publishedSplit, int.Parse(totals.Groups["split"].Value, CultureInfo.InvariantCulture));
        Assert.Equal(publishedWhole, int.Parse(totals.Groups["whole"].Value, CultureInfo.InvariantCulture));

        // Nothing deferred is assigned anywhere other than these four destinations.
        Assert.Equal(
            publishedTotal,
            EstateInventory.Ledger.Count(static row => row.Category.StartsWith(
                "Deferred",
                StringComparison.Ordinal)));

        // And the five REFERENCE rows Section 11.2 names are exactly the five the ledger carries.
        Assert.Equal(5, EstateInventory.Ledger.Count(static row =>
            string.Equals(row.Role, "REFERENCE", StringComparison.Ordinal)));
    }

    /// <summary>
    /// Section 11.4's split arithmetic holds for every split library, and the libraries it lists are
    /// exactly the libraries the ledger splits.
    /// </summary>
    /// <remarks>
    /// The second half is what makes this more than seven sums. A library that acquired an in-scope object
    /// becomes a split library, and if Section 11.4 does not list it the reconciliation has a gap that no
    /// total would reveal - the object is counted, just not accounted for.
    /// </remarks>
    [Fact]
    public void TheSplitArithmeticHoldsForExactlyTheLibrariesTheLedgerSplits()
    {
        Dictionary<string, (int Total, int InScope, int Deferred)> published = [];

        foreach (Match row in Regex.Matches(
            EstateInventory.Section("### 11.4"),
            @"\|\s*`(?<library>[A-Za-z0-9.]+)`\s*\|\s*(?<total>\d+)\s*\|\s*=\s*\|\s*(?<inScope>\d+)"
                + @"\s*\|\s*\+\s*\|\s*(?<deferred>\d+)\s*\|",
            RegexOptions.ExplicitCapture,
            TimeSpan.FromSeconds(5)))
        {
            published[row.Groups["library"].Value] = (
                int.Parse(row.Groups["total"].Value, CultureInfo.InvariantCulture),
                int.Parse(row.Groups["inScope"].Value, CultureInfo.InvariantCulture),
                int.Parse(row.Groups["deferred"].Value, CultureInfo.InvariantCulture));
        }

        Assert.NotEmpty(published);

        ImmutableArray<string> ledgerSplitLibraries =
            [.. EstateInventory.Ledger
                .GroupBy(static row => row.Library, StringComparer.Ordinal)
                .Where(static group =>
                    group.Any(static row => string.Equals(
                        row.Category,
                        "In scope",
                        StringComparison.Ordinal))
                    && group.Any(static row => !string.Equals(
                        row.Category,
                        "In scope",
                        StringComparison.Ordinal)))
                .Select(static group => group.Key)
                .Order(StringComparer.Ordinal)];

        Assert.Equal(ledgerSplitLibraries, [.. published.Keys.Order(StringComparer.Ordinal)]);

        foreach (KeyValuePair<string, (int Total, int InScope, int Deferred)> library in published)
        {
            (int total, int inScope, int deferred) = library.Value;

            // The published row's own arithmetic. The deferred figure here is the STRICT one, which for
            // pfw.ui.controls.ext folds its REFERENCE row back into the destination - the compact form
            // 4 + 1 + 38 in the prose is the same 39.
            Assert.Equal(total, inScope + deferred);

            Assert.Equal(total, EstateInventory.LedgerLibraryCounts[library.Key]);

            Assert.Equal(
                inScope,
                EstateInventory.Ledger.Count(row =>
                    string.Equals(row.Library, library.Key, StringComparison.Ordinal)
                    && string.Equals(row.Category, "In scope", StringComparison.Ordinal)));
        }

        // The seven in-scope contributions of the split libraries plus the wholly-in-scope libraries'
        // objects are the whole in-scope set, so no in-scope object sits in a library neither list covers.
        Assert.Equal(ExpectedInScope, EstateInventory.LedgerCategoryCounts["In scope"]);
    }

    /// <summary>
    /// Section 11.5's library-level partition is the ledger's own: no library counted twice, none
    /// unaccounted for.
    /// </summary>
    [Fact]
    public void TheLibraryPartitionIsTheLedgersOwn()
    {
        int whollyInScope = EstateInventory.LabelledCount("### 11.5", "Wholly in scope");
        int split = EstateInventory.LabelledCount("### 11.5", "Split");
        int whollyDeferred = EstateInventory.LabelledCount("### 11.5", "Wholly deferred");
        int permanentlyOut = EstateInventory.LabelledCount("### 11.5", "Permanently out of scope");
        int total = EstateInventory.LabelledCount("### 11.5", "Total");

        Assert.Equal(ExpectedLibraries, total);
        Assert.Equal(total, whollyInScope + split + whollyDeferred + permanentlyOut);
        Assert.Equal(total, EstateInventory.LedgerLibraryCounts.Count);

        Dictionary<string, HashSet<string>> categoriesByLibrary = EstateInventory.Ledger
            .GroupBy(static row => row.Library, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => new HashSet<string>(
                    group.Select(static row => row.Category),
                    StringComparer.Ordinal),
                StringComparer.Ordinal);

        Assert.Equal(
            whollyInScope,
            categoriesByLibrary.Count(static entry =>
                entry.Value.Count == 1 && entry.Value.Contains("In scope")));
        Assert.Equal(
            split,
            categoriesByLibrary.Count(static entry =>
                entry.Value.Count > 1 && entry.Value.Contains("In scope")));
        Assert.Equal(
            whollyDeferred,
            categoriesByLibrary.Count(static entry =>
                !entry.Value.Contains("In scope")
                && !entry.Value.Contains("Permanently out")));
        Assert.Equal(
            permanentlyOut,
            categoriesByLibrary.Count(static entry =>
                entry.Value.Count == 1 && entry.Value.Contains("Permanently out")));
    }

    /// <summary>
    /// The in-scope destinations are exactly the four Phase-1 services and the five shared libraries, and
    /// no deferred destination appears on an in-scope row.
    /// </summary>
    /// <remarks>
    /// <b>THE DEFERRAL BOUNDARY, ASSERTED ON THE MAPPING ITSELF.</b> AAP 0.2.2.2 forbids implementing any
    /// part of the four deferred services in this phase, and the mapping is where that boundary is
    /// declared. An in-scope row pointing at DesignSystem, Documents, Integration or ScriptBridge would be
    /// the document authorising exactly the work the plan forbids - and it would do so somewhere no
    /// service's own test suite could see.
    /// </remarks>
    [Fact]
    public void NoInScopeRowIsAssignedToADeferredDestination()
    {
        ImmutableArray<string> inScopeDestinations =
            [.. EstateInventory.Ledger
                .Where(static row => string.Equals(row.Category, "In scope", StringComparison.Ordinal))
                .Select(static row => row.Destination)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)];

        Assert.All(
            inScopeDestinations,
            destination => Assert.DoesNotContain(destination, DeferredDestinations));

        // Conversely, every deferred row names one of the four deferred destinations.
        Assert.All(
            EstateInventory.Ledger.Where(static row =>
                row.Category.StartsWith("Deferred", StringComparison.Ordinal)),
            row => Assert.Contains(row.Destination, DeferredDestinations));
    }

    /// <summary>Reads a count cell that may be an em dash standing for zero.</summary>
    /// <param name="cell">The cell text.</param>
    /// <returns>The count.</returns>
    /// <remarks>
    /// Section 11.2 writes Integration's split-remainder column as an em dash rather than <c>0</c>, which
    /// is the document's convention for "this category is empty for this destination". Parsed rather than
    /// special-cased at the call site so the convention lives in one place.
    /// </remarks>
    private static int ParseCountOrDash(string cell) =>
        int.TryParse(cell, CultureInfo.InvariantCulture, out int count) ? count : 0;

    /// <summary>Counts ledger rows against one destination in one deferred category.</summary>
    /// <param name="destination">The destination.</param>
    /// <param name="category">The deferred category.</param>
    /// <returns>The number of rows.</returns>
    private static int CountDeferred(string destination, string category) =>
        EstateInventory.Ledger.Count(row =>
            string.Equals(row.Destination, destination, StringComparison.Ordinal)
            && string.Equals(row.Category, category, StringComparison.Ordinal));
}
