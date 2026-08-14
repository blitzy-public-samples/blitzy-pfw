// ==================================================================================================
//  DocumentationCoherenceTests - THE MECHANICAL GUARD OVER CLAIMS ONE DOCUMENT MAKES ABOUT ANOTHER
//  ------------------------------------------------------------------------------------------------
//  SUBJECT     The authored documents that publish a figure, a version or a count taken from
//              somewhere else in the tree, and that CLAIM correspondence with that source. Today
//              that is the root NOTICE against Directory.Packages.props, docs/CONTRACTS.md against
//              the compiled protobuf descriptors, and docs/BUILD.md's GitHub Actions inventory
//              against the pins .github/workflows/ci.yml actually carries.
//  AUTHORITY   Agent Action Plan 0.5.1 (the exact package inventory), 0.5.4.1 (central package
//              management is the single version authority), 0.7.2 (a current legal and dependency
//              inventory, and no floating version anywhere), and constraint C-K - document every
//              technology and boundary decision ACCURATELY.
//
//  WHY THESE GUARDS EXIST, STATED AS THE DEFECTS THEY CAUGHT RATHER THAN AS A PRINCIPLE
//  ------------------------------------------------------------------------------------------------
//  A prose count is a fact with no owner. Two of them had already gone stale in exactly the way an
//  unowned fact does, and neither was visible to the compiler, the linter or any existing test:
//
//    * NOTICE's dependency inventory claimed EXACT bidirectional correspondence with
//      Directory.Packages.props - "every PackageVersion entry in that manifest appears in the direct
//      list below at the same version, and every entry in the direct list below is pinned there" -
//      while listing six Microsoft packages at 10.0.10 after the pins had moved to 10.0.11. A legal
//      and provenance inventory that is wrong about a version is worse than one that declines to
//      state versions, because a licence, provenance or vulnerability review trusts it.
//    * docs/CONTRACTS.md published a ColumnExpressionService method count of twenty-seven in two
//      places while its own enumerated table, its own measurement section and the compiled
//      descriptor all said twenty-six. The document had already predicted this failure in its own
//      prose - it records having drifted on the same four totals TWICE, in opposite directions - and
//      answered it with a shell recipe for regenerating them, which only runs when somebody remembers
//      it. The register is now compared against the descriptor on every build instead.
//    * docs/CONTRACTS.md's versioning subsection stated "the three narrowings of 14.4" while 14.4
//      enumerated six - one behavioural plus five narrowing a field or a domain. Three is the size of
//      a DIFFERENT register in the same document, C-02's capability narrowings N1 to N3, which is
//      exactly how two registers of similar things drift into one another when neither count is
//      derived from anything.
//    * .github/workflows/ci.yml referenced four `actions/*` steps by MUTABLE major-version tag while
//      arguing, in its own header, that a tag can be repointed at different code after review. A tag
//      pin is not a version at all: `@v4` is repointed by design on every release in that line, so
//      the workflow had no reproducible answer to "which code ran". It now pins every step to a
//      commit, and three artifacts must agree about those pins - the `uses:` steps themselves, the
//      provenance table in the workflow's own header, and the inventory table in docs/BUILD.md 11.3,
//      which exists because Directory.Packages.props has no expression for a GitHub Action and an
//      action would otherwise be the one dependency class with no published provenance.
//
//  Each assertion below turns one of those into a test failure that names the offending entry, so the
//  claim is enforced rather than merely made.
//
//  WHY A CONTRACTS TEST OWNS THEM
//  ------------------------------------------------------------------------------------------------
//  The subjects are repository-wide: the package manifest governs all twenty projects and NOTICE
//  covers all of them, so no single service's test project is where the invariant can be stated. This
//  project already owns the published cross-service boundary and already carries the sibling
//  coherence guards over the orchestration template, the service settings and the operational
//  topology, so the mechanism, the locator and the conventions are the established ones.
//
//  WHAT THEY DELIBERATELY DO NOT DO
//  ------------------------------------------------------------------------------------------------
//  They resolve no package, contact no registry and read no lock file: the transitive half of
//  NOTICE's inventory is regenerated from `dotnet list package --include-transitive` by a human when a
//  pin moves, which is a network operation and cannot be a unit test. What IS asserted is the half
//  NOTICE claims to be exact in both directions, which is the direct list against the manifest. They
//  are also READ-ONLY: a disagreement is a finding for a human, never something a test reconciles.
//
//  The action guards below are the same shape and carry the same limit. They assert that every `uses:`
//  names a 40-character commit rather than a tag, that its trailing comment records a release, and
//  that the three artifacts agree with one another - all three properties readable from the tree. They
//  do NOT contact github.com to confirm a SHA still belongs to the tag its comment names; that is a
//  network act, it is how a pin is RESOLVED rather than how it is kept coherent, and a test that
//  reached the network would fail on an offline runner for a reason unrelated to the repository.
// ==================================================================================================

using System.Globalization;
using System.Text.RegularExpressions;
using PowerFramework.Contracts.DataServices.V1;
using PowerFramework.Contracts.Persistence.V1;
using Xunit;
using Xunit.Sdk;

namespace PowerFramework.Contracts.Tests;

/// <summary>
/// The guards over a document's claim to correspond with a source elsewhere in the tree.
/// </summary>
public sealed class DocumentationCoherenceTests
{
    /// <summary>The central package manifest, which is the single version authority.</summary>
    private const string PackageManifestFileName = "Directory.Packages.props";

    /// <summary>The repository solution, used only as the second root marker.</summary>
    private const string SolutionMarkerFileName = "PowerFramework.slnx";

    /// <summary>The root legal and third-party notice.</summary>
    private const string NoticeFileName = "NOTICE";

    /// <summary>The line that opens NOTICE's direct package list.</summary>
    private const string DirectListHeading = "Registry: nuget.org";

    /// <summary>The footnote that closes NOTICE's direct package list.</summary>
    private const string DirectListTerminator = "(1) Referenced by";

    /// <summary>The only CI workflow, and the authority on which action commit actually runs.</summary>
    private const string WorkflowRelativePath = ".github/workflows/ci.yml";

    /// <summary>The build document, which publishes the GitHub Actions provenance inventory.</summary>
    private const string BuildDocumentRelativePath = "docs/BUILD.md";

    /// <summary>The contract inventory, which publishes a derived method count per gRPC service.</summary>
    private const string ContractDocumentRelativePath = "docs/CONTRACTS.md";

    /// <summary>
    /// The pre-refactor baseline: the last upstream PowerBuilder commit, before any .NET file existed.
    /// </summary>
    /// <remarks>
    /// The one figure in this suite that IS a constant, and it has to be: it identifies the revision the
    /// inventory is measured against, so deriving it from anything would be circular. It is the same
    /// commit docs/BUILD.md section 16.1 names in its own derivation commands.
    /// </remarks>
    private const string BaselineCommit = "a80ac35";

    /// <summary>Tracked files at that baseline, which the build document also publishes.</summary>
    private const int BaselineTrackedFiles = 934;

    /// <summary>How long any regular expression in this suite may run.</summary>
    private static readonly TimeSpan RegexBudget = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The compiled service descriptors, which are the authority on how many methods each gRPC contract
    /// declares.
    /// </summary>
    /// <remarks>
    /// Read from the GENERATED descriptors rather than by parsing the <c>.proto</c> text, because the
    /// descriptor is what a caller's stub is built from. A hand-rolled parse of the definition file would
    /// agree with the document and still be wrong about the wire if code generation had not been run.
    /// </remarks>
    private static IReadOnlyDictionary<string, int> DeclaredMethodCounts =>
        new[] { DataservicesV1Reflection.Descriptor, PersistenceV1Reflection.Descriptor }
            .SelectMany(static file => file.Services)
            .ToDictionary(
                static service => service.Name,
                static service => service.Methods.Count,
                StringComparer.Ordinal);

    /// <summary>
    /// Every pin in the central manifest appears in NOTICE's direct list at the same version, and
    /// every entry in that list is pinned in the manifest.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>BOTH DIRECTIONS ARE ASSERTED BECAUSE NOTICE CLAIMS BOTH.</b> A one-directional check would
    /// pass a NOTICE that had quietly dropped a package - the inventory would be incomplete while
    /// every line in it was correct, which is the failure mode a licence review is least able to
    /// detect for itself.
    /// </para>
    /// <para>
    /// THE VERSION IS COMPARED AS TEXT, deliberately. The manifest and the notice both publish the
    /// exact string a restore resolves, and a semantic comparison would let <c>10.0.11</c> and
    /// <c>10.0.011</c> agree when a reader diffing the two files would see a difference.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheNoticeDirectPackageListCorrespondsExactlyWithTheCentralPins()
    {
        IReadOnlyDictionary<string, string> pins = ReadCentralPins();
        IReadOnlyDictionary<string, string> listed = ReadNoticeDirectList();

        Assert.NotEmpty(pins);
        Assert.NotEmpty(listed);

        foreach ((string id, string version) in pins)
        {
            Assert.True(
                listed.ContainsKey(id),
                $"{PackageManifestFileName} pins '{id}' at {version} and {NoticeFileName} does not list "
                    + "it. That file claims every PackageVersion entry appears in its direct list, so the "
                    + "omission is a defect in the inventory rather than in the manifest.");

            Assert.True(
                string.Equals(listed[id], version, StringComparison.Ordinal),
                $"{NoticeFileName} lists '{id}' at {listed[id]} and {PackageManifestFileName} pins it at "
                    + $"{version}. The notice claims exact correspondence in both directions, and a "
                    + "provenance or vulnerability review reads it as authoritative.");
        }

        foreach ((string id, string version) in listed)
        {
            Assert.True(
                pins.ContainsKey(id),
                $"{NoticeFileName} lists '{id}' at {version} as a direct reference and "
                    + $"{PackageManifestFileName} pins no such package. Either the pin was removed and the "
                    + "notice was not regenerated, or the entry names a transitive package that belongs in "
                    + "the second list.");
        }

        // The counts close independently of the two loops above, so a duplicate key on either side -
        // which a dictionary silently collapses - cannot pass this test.
        Assert.Equal(pins.Count, listed.Count);
    }

    /// <summary>
    /// NOTICE lists no package twice, in either of its two inventories.
    /// </summary>
    /// <remarks>
    /// A repeated identity is invisible to a reader scanning an alphabetical list of a hundred lines and
    /// changes what the inventory means: the same package would be credited under two sets of terms if
    /// the licence column ever diverged between the two lines. Asserted over the raw text rather than
    /// over a parsed dictionary, because a dictionary is exactly what hides it.
    /// </remarks>
    [Fact]
    public void TheNoticeListsNoPackageIdentityTwice()
    {
        string[] lines = File.ReadAllLines(Path.Combine(RequireRepositoryRoot(), NoticeFileName));

        Dictionary<string, int> firstSeen = new(StringComparer.Ordinal);
        List<string> duplicates = [];

        foreach ((string line, int number) in lines.Select(static (l, i) => (l, i + 1)))
        {
            Match entry = Regex.Match(
                line,
                @"^  (?<id>[A-Za-z][A-Za-z0-9_.-]*)\s{2,}(?<version>\d[^\s,]*)",
                RegexOptions.None,
                RegexBudget);

            if (!entry.Success)
            {
                continue;
            }

            string id = entry.Groups["id"].Value;

            if (firstSeen.TryGetValue(id, out int previous))
            {
                duplicates.Add(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"'{id}' appears on line {previous} and again on line {number}"));

                continue;
            }

            firstSeen[id] = number;
        }

        Assert.True(
            duplicates.Count == 0,
            $"{NoticeFileName} credits a package identity more than once: "
                + string.Join("; ", duplicates)
                + ". One identity, one line, one set of terms.");
    }

    /// <summary>
    /// The CI workflow references no marketplace action at all - there is no <c>uses:</c> step in it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>THIS IS STRICTER THAN PINNING, NOT LOOSER.</b> A commit pin fixes WHICH code runs; it does not
    /// make that code safe. Every action this workflow once referenced shipped a committed <c>dist/</c>
    /// bundle carrying advisory-affected npm dependencies, and two of them ran with
    /// <c>packages: write</c> while handling a build context that a pull request makes untrusted. The
    /// <c>git</c> CLI, the vendor <c>dotnet-install.sh</c> script and the <c>docker</c> CLI all ship with
    /// the runner image and do the same work with nothing vendored, so the reachable third-party surface
    /// is zero rather than merely reproducible.
    /// </para>
    /// <para>
    /// IT IS ASSERTED AGAINST THE STEPS RATHER THAN AGAINST THE HEADER, because the <c>uses:</c> lines are
    /// what actually runs. The companion guard holds the two published inventories to the same position,
    /// so neither an action added without a record nor a record left behind after an action was removed
    /// can pass.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheWorkflowReferencesNoMarketplaceActionAtAll()
    {
        IReadOnlyList<ActionPin> pins = ReadWorkflowActionPins();

        Assert.True(
            pins.Count == 0,
            $"{WorkflowRelativePath} references {pins.Count} marketplace action(s): "
                + string.Join(
                    "; ",
                    pins.Select(static pin => $"line {pin.Line} uses {pin.Action}@{pin.Commit}"))
                + ". This workflow references NONE, and that is a stricter position than pinning them. "
                + "Every action it once used shipped a committed `dist/` bundle carrying advisory-affected "
                + "npm dependencies, and two of them ran with `packages: write` while handling a build "
                + "context that a pull request makes untrusted; a commit pin makes such a bundle "
                + "reproducible without making it safe. The `git` CLI, the vendor `dotnet-install.sh` "
                + "script and the `docker` CLI all ship with the runner image and do the same work with "
                + "nothing vendored. If a step genuinely needs an action, that is a supply-chain decision "
                + "to take deliberately - record it in the workflow header and in "
                + $"{BuildDocumentRelativePath} section 11.3, and re-aim this guard at the same time.");
    }

    /// <summary>
    /// The provenance table in the workflow's own header and the inventory published by
    /// <c>docs/BUILD.md</c> section 11.3 both record that no marketplace action is referenced, and both
    /// state that position in words rather than merely omitting a table.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>AN EMPTY INVENTORY AND A MISSING INVENTORY READ THE SAME AND MEAN DIFFERENT THINGS.</b> A
    /// supply-chain review needs to know that the absence is deliberate, so each artifact is required to
    /// SAY the workflow references no marketplace action. An unstated invariant is one the next edit
    /// reintroduces without noticing.
    /// </para>
    /// <para>
    /// Both directions are covered: a row naming an action the workflow does not have fails here, and an
    /// action added to the workflow fails the companion guard. Together they close the gap a provenance
    /// table exists to close.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheActionInventoriesRecordThatNoMarketplaceActionIsReferenced()
    {
        foreach ((string claimant, IReadOnlyDictionary<string, ActionPin> claimed) in
            (IReadOnlyList<(string, IReadOnlyDictionary<string, ActionPin>)>)
            [
                ($"{WorkflowRelativePath} header provenance table", ReadWorkflowHeaderInventory()),
                ($"{BuildDocumentRelativePath} section 11.3 inventory", ReadBuildDocumentInventory()),
            ])
        {
            Assert.True(
                claimed.Count == 0,
                $"The {claimant} lists {claimed.Count} action(s) - "
                    + string.Join(", ", claimed.Keys)
                    + $" - while {WorkflowRelativePath} references none. An inventory that names a build "
                    + "dependency the workflow does not have is as misleading as one that omits a "
                    + "dependency it does have, and this is the pair a supply-chain review reads.");
        }

        foreach ((string relativePath, string claim) in
            (IReadOnlyList<(string, string)>)
            [
                (WorkflowRelativePath, "no marketplace action"),
                (BuildDocumentRelativePath, "references no marketplace action"),
            ])
        {
            string text = string.Join("\n", ReadRepositoryLines(relativePath));

            Assert.True(
                text.Contains(claim, StringComparison.OrdinalIgnoreCase),
                $"{relativePath} does not state the position it is holding: the phrase '{claim}' appears "
                    + "nowhere in it. The workflow references no action, and both artifacts must SAY so - "
                    + "an unstated invariant is one the next edit reintroduces without noticing.");
        }
    }

    /// <summary>
    /// The workflow mutates no part of the runner image, and the one Python closure it does install is
    /// hash-locked, binary-only, and placed in an ephemeral target directory.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>PINNING ONE DISTRIBUTION NAME IS NOT PINNING AN INSTALL.</b> Naming a single version let the
    /// resolver choose everything beneath it from whatever was current at install time, so the code the
    /// job executed was not the code anyone reviewed (CWE-1357). <c>--require-hashes</c> refuses the
    /// ENTIRE install unless every requirement, transitive included, is pinned with <c>==</c> and carries
    /// a matching hash, so the lock cannot be half-applied; <c>--only-binary :all:</c> refuses to build a
    /// source distribution, which would run arbitrary build code inside the job.
    /// </para>
    /// <para>
    /// AND IT MUST NOT LAND OVER THE SYSTEM INTERPRETER. Writing into the runner's own Python leaves a
    /// later step unable to tell what it is importing, so the closure goes to a <c>--target</c> directory
    /// reached through <c>PYTHONPATH</c> - legible, disposable, and gone with the job. Package managers
    /// that mutate the image itself are refused outright: every .NET dependency is pinned centrally, and a
    /// package installed from a workflow file is recorded nowhere.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryToolTheWorkflowInstallsIsHashLockedAndEphemeral()
    {
        string[] lines = ReadRepositoryLines(WorkflowRelativePath);
        List<string> offenders = [];

        foreach ((string line, int number) in lines.Select(static (text, index) => (text, index + 1)))
        {
            string statement = line.TrimStart();

            if (statement.StartsWith('#'))
            {
                continue;
            }

            bool forbidden = Regex.IsMatch(
                    statement,
                    @"\b(apt|apt-get|npm|yarn|pnpm|brew)\b.*\b(install|add)\b",
                    RegexOptions.None,
                    RegexBudget)
                || Regex.IsMatch(
                    statement,
                    @"\bdotnet\s+tool\s+install\b",
                    RegexOptions.None,
                    RegexBudget);

            if (forbidden)
            {
                offenders.Add(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"line {number} mutates the runner image: {statement}"));
                continue;
            }

            if (!Regex.IsMatch(
                    statement,
                    @"\bpip3?\b.*\binstall\b",
                    RegexOptions.None,
                    RegexBudget))
            {
                continue;
            }

            // The install is one shell statement continued over several lines, so the whole run block
            // from this line onward is what carries its arguments.
            string invocation = string.Join(" ", lines.Skip(number - 1).Take(12));

            foreach (string required in (IReadOnlyList<string>)
                ["--require-hashes", "--only-binary :all:", "--target", "--requirement"])
            {
                if (!invocation.Contains(required, StringComparison.Ordinal))
                {
                    offenders.Add(
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"line {number} installs a Python distribution without `{required}`"));
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            $"{WorkflowRelativePath} installs software unsafely: "
                + string.Join("; ", offenders)
                + $". Nothing may mutate the runner image, and the one Python closure this workflow does "
                + "install must be refused outright unless EVERY requirement - transitive included - is "
                + "pinned with `==` and carries a matching hash (`--require-hashes`), no source "
                + "distribution may run its own build code (`--only-binary :all:`), and the closure lands "
                + "in an ephemeral `--target` directory rather than over the system interpreter. The "
                + $"requirement file is checked in, so the closure is reviewable; {PackageManifestFileName} "
                + "pins everything on the .NET side.");
    }

    /// <summary>
    /// Every per-service method count the contract register publishes equals the count the compiled
    /// descriptor declares, and the register omits no gRPC service.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THIS IS THE GUARD THE DOCUMENT ASKED FOR IN PROSE AND DID NOT HAVE. Its own §2 explains that a
    /// hand-written inventory of a twenty-six-method service falls behind the schema silently, and §16.3
    /// carries a shell recipe for regenerating the totals - but a recipe only runs when somebody
    /// remembers it, and this document had already drifted on the same four totals TWICE in opposite
    /// directions. The register is now compared against the descriptor on every build.
    /// </para>
    /// <para>
    /// BOTH DIRECTIONS ARE ASSERTED, because a register that silently dropped a service would be
    /// consistent about every row it still carried. A missing row is the harder failure to notice: a
    /// reader of an inventory cannot see the entry that is not there.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheContractRegisterAgreesWithTheCompiledDescriptorsOnEveryMethodCount()
    {
        IReadOnlyDictionary<string, int> declared = DeclaredMethodCounts;
        IReadOnlyDictionary<string, int> published = ReadContractRegisterMethodCounts();

        List<string> disagreements = [];

        foreach ((string service, int count) in declared.OrderBy(static entry => entry.Key, StringComparer.Ordinal))
        {
            if (!published.TryGetValue(service, out int stated))
            {
                disagreements.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"{service} declares {count} methods and the register carries no row for it"));

                continue;
            }

            if (stated != count)
            {
                disagreements.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"{service}: register says {stated}, descriptor declares {count}"));
            }
        }

        foreach (string service in published.Keys.Where(name => !declared.ContainsKey(name)))
        {
            disagreements.Add(
                $"the register carries a row for {service}, which no compiled descriptor declares");
        }

        Assert.True(
            disagreements.Count == 0,
            $"{ContractDocumentRelativePath} §2 disagrees with the compiled protobuf descriptors: "
                + string.Join("; ", disagreements)
                + $". The descriptor is authoritative for the wire; regenerate the counts with the "
                + "recipe in §16.3 rather than adjusting one of them by hand.");
    }

    /// <summary>
    /// Every total the contract document states as a service's COMPLETE method surface is a total some
    /// service actually has.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The register of §2 is one place a total appears; the document states each surface again in prose at
    /// the head of the section that enumerates it, and those restatements are what went stale - it
    /// published twenty-seven methods for C-04 in two such sentences while its own table numbered
    /// twenty-six rows and the descriptor declared twenty-six.
    /// </para>
    /// <para>
    /// WHY THE ASSERTION IS MEMBERSHIP RATHER THAN EQUALITY. Resolving which service a sentence is about
    /// would mean inferring section ownership from surrounding headings, which is brittle in a document
    /// that discusses six services and cites all of them from everywhere. Membership in the set of real
    /// totals is weaker but sound, needs no inference, and catches the whole failure class: a stale total
    /// is stale precisely because no service has it. Only the two COMPLETENESS idioms this document uses
    /// are read - "All &lt;n&gt; RPCs the service declares" and an emphasised "&lt;n&gt; methods/RPCs"
    /// opening - so an ordinary count of some subset ("Two RPCs in the whole system declare that
    /// binding") is not a subject and is not treated as one.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryCompleteSurfaceTotalTheContractDocumentStatesIsARealServiceTotal()
    {
        IReadOnlyDictionary<string, int> declared = DeclaredMethodCounts;
        HashSet<int> realTotals = [.. declared.Values];

        // Line wrapping puts "All" and the count on different lines in at least one section, so the
        // idioms are matched against the document as one whitespace-normalised string.
        string prose = Regex.Replace(
            string.Join(' ', ReadRepositoryLines(ContractDocumentRelativePath)),
            @"\s+",
            " ",
            RegexOptions.None,
            RegexBudget);

        string[] idioms =
        [
            @"All (?<count>[A-Za-z][A-Za-z-]*) (?:RPCs|methods) the service declares",
            @"\*\*(?<count>[A-Za-z][A-Za-z-]*) (?:RPCs|methods)\b",
        ];

        List<string> impossible = [];
        int examined = 0;

        foreach (string idiom in idioms)
        {
            foreach (Match statement in Regex.Matches(prose, idiom, RegexOptions.None, RegexBudget))
            {
                string word = statement.Groups["count"].Value;

                if (!TryReadCountWord(word, out int stated))
                {
                    // "All" in the emphasised idiom, and any other word that is not a number, is not a
                    // total. Skipping it is correct: the completeness form it introduces is matched by
                    // the other idiom.
                    continue;
                }

                examined++;

                if (!realTotals.Contains(stated))
                {
                    impossible.Add(string.Create(
                        CultureInfo.InvariantCulture,
                        $"\"{statement.Value.Trim()}\" states {stated}"));
                }
            }
        }

        Assert.True(
            examined >= declared.Count,
            $"{ContractDocumentRelativePath} was expected to state a complete surface for each of the "
                + $"{declared.Count} gRPC services, but only {examined} such statements were found. Either "
                + "a section lost its total or the idiom it is written in changed, and this guard cannot "
                + "see a total it does not recognise.");

        Assert.True(
            impossible.Count == 0,
            $"{ContractDocumentRelativePath} states a complete method surface no service has: "
                + string.Join("; ", impossible)
                + ". The real totals are "
                + string.Join(
                    ", ",
                    declared
                        .OrderBy(static entry => entry.Key, StringComparer.Ordinal)
                        .Select(static entry => string.Create(
                            CultureInfo.InvariantCulture,
                            $"{entry.Key}={entry.Value}")))
                + ".");
    }

    /// <summary>
    /// Every narrowing count the contract document states agrees with the canonical register the document
    /// itself carries.
    /// </summary>
    /// <remarks>
    /// <para>
    /// §14.4 is the canonical register and its shape is one behavioural narrowing plus a numbered table of
    /// field-or-domain narrowings, so the canonical total is derived from the table rather than written
    /// down twice. The defect this catches is real: the versioning subsection stated "the three
    /// narrowings of §14.4" while the register enumerated six, and three is the count of a DIFFERENT
    /// register - C-02's capability narrowings N1 to N3, which the document is careful to distinguish
    /// everywhere except there.
    /// </para>
    /// <para>
    /// The guard reads only statements that cite §14.4 explicitly. A count of some other narrowing set is
    /// not this register's business, and conflating the two is the very error being guarded against.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheNarrowingCountsAgreeWithTheCanonicalRegister()
    {
        string[] lines = ReadRepositoryLines(ContractDocumentRelativePath);

        int registerHeading = Array.FindIndex(
            lines,
            static line => line.StartsWith("### 14.4 ", StringComparison.Ordinal));

        Assert.True(
            registerHeading >= 0,
            $"{ContractDocumentRelativePath} carries no §14.4 heading. That subsection is the canonical "
                + "narrowing register, and every narrowing count in the document is stated against it.");

        int fieldOrDomainNarrowings = 0;

        for (int index = registerHeading + 1; index < lines.Length; index++)
        {
            if (lines[index].StartsWith("### ", StringComparison.Ordinal))
            {
                break;
            }

            if (Regex.IsMatch(lines[index], @"^\| \d+ \|", RegexOptions.None, RegexBudget))
            {
                fieldOrDomainNarrowings++;
            }
        }

        // One behavioural narrowing - the cross-session foreign variable - plus the tabulated field and
        // domain narrowings. The behavioural one is deliberately NOT a table row: it is the subject of the
        // subsection's opening sentence, because it is the only one that removes a capability.
        int canonical = fieldOrDomainNarrowings + 1;

        Assert.True(
            fieldOrDomainNarrowings > 0,
            $"{ContractDocumentRelativePath} §14.4 enumerates no field or domain narrowing. The register "
                + "cannot be empty while the document states counts against it.");

        List<string> disagreements = [];

        string prose = Regex.Replace(
            string.Join(' ', lines),
            @"\s+",
            " ",
            RegexOptions.None,
            RegexBudget);

        foreach (Match statement in Regex.Matches(
            prose,
            @"(?<count>[A-Za-z][A-Za-z-]*) narrowings of \[§14\.4\]",
            RegexOptions.None,
            RegexBudget))
        {
            string word = statement.Groups["count"].Value;

            if (!TryReadCountWord(word, out int stated))
            {
                disagreements.Add($"\"{statement.Value.Trim()}\" states no readable number");

                continue;
            }

            if (stated != canonical)
            {
                disagreements.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"\"{statement.Value.Trim()}\" states {stated}"));
            }
        }

        Assert.True(
            disagreements.Count == 0,
            $"{ContractDocumentRelativePath} disagrees with its own §14.4 register, which enumerates "
                + string.Create(
                    CultureInfo.InvariantCulture,
                    $"{fieldOrDomainNarrowings} field or domain narrowings plus the one behavioural "
                        + $"narrowing, so {canonical} in total: ")
                + string.Join("; ", disagreements)
                + ". C-02's three capability narrowings are a different register and are counted "
                + "separately.");
    }

    /// <summary>Reads a spelled-out or numeric count word.</summary>
    /// <param name="word">The word as the document spells it, such as <c>twenty-six</c> or <c>26</c>.</param>
    /// <param name="value">The number it names, when it names one.</param>
    /// <returns><see langword="true"/> when the word is a number this method understands.</returns>
    /// <remarks>
    /// Deliberately narrow. It understands the forms this documentation set actually uses - the units, the
    /// teens, the tens to fifty and their hyphenated compounds, and bare digits - and refuses everything
    /// else rather than guessing. A word it refuses is treated by the callers as "not a count", which is
    /// why refusing has to be exact: silently mapping an unknown word to zero would turn a stale total
    /// into a passing test.
    /// </remarks>
    private static bool TryReadCountWord(string word, out int value)
    {
        value = 0;

        if (int.TryParse(word, NumberStyles.None, CultureInfo.InvariantCulture, out int digits))
        {
            value = digits;

            return true;
        }

        Dictionary<string, int> units = new(StringComparer.OrdinalIgnoreCase)
        {
            ["zero"] = 0,
            ["one"] = 1,
            ["two"] = 2,
            ["three"] = 3,
            ["four"] = 4,
            ["five"] = 5,
            ["six"] = 6,
            ["seven"] = 7,
            ["eight"] = 8,
            ["nine"] = 9,
            ["ten"] = 10,
            ["eleven"] = 11,
            ["twelve"] = 12,
            ["thirteen"] = 13,
            ["fourteen"] = 14,
            ["fifteen"] = 15,
            ["sixteen"] = 16,
            ["seventeen"] = 17,
            ["eighteen"] = 18,
            ["nineteen"] = 19,
        };

        Dictionary<string, int> tens = new(StringComparer.OrdinalIgnoreCase)
        {
            ["twenty"] = 20,
            ["thirty"] = 30,
            ["forty"] = 40,
            ["fifty"] = 50,
        };

        if (units.TryGetValue(word, out int unit))
        {
            value = unit;

            return true;
        }

        if (tens.TryGetValue(word, out int ten))
        {
            value = ten;

            return true;
        }

        string[] parts = word.Split('-', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 2
            && tens.TryGetValue(parts[0], out int compoundTen)
            && units.TryGetValue(parts[1], out int compoundUnit)
            && compoundUnit is > 0 and < 10)
        {
            value = compoundTen + compoundUnit;

            return true;
        }

        return false;
    }

    /// <summary>Reads the per-service method counts the contract register publishes.</summary>
    /// <returns>The service name and the count stated for it, one entry per gRPC row.</returns>
    /// <remarks>
    /// Only the gRPC rows are read. The REST rows of the same table count OPERATIONS, which are a property
    /// of an OpenAPI document rather than of a protobuf descriptor and are asserted against that document
    /// by <c>OpenApiContractDocumentTests</c>; reading them here would compare two different units.
    /// </remarks>
    private static IReadOnlyDictionary<string, int> ReadContractRegisterMethodCounts()
    {
        Dictionary<string, int> counts = new(StringComparer.Ordinal);

        foreach (string line in ReadRepositoryLines(ContractDocumentRelativePath))
        {
            Match row = Regex.Match(
                line,
                @"^\|\s*C-\d+\s+`(?<service>\w+)`\s*\|\s*gRPC\s*\|\s*(?<count>\d+)\s+methods\s*\|",
                RegexOptions.None,
                RegexBudget);

            if (!row.Success)
            {
                continue;
            }

            string service = row.Groups["service"].Value;

            Assert.False(
                counts.ContainsKey(service),
                $"{ContractDocumentRelativePath} §2 carries two rows for {service}. A register with a "
                    + "duplicated subject has no single stated count to check.");

            counts[service] = int.Parse(
                row.Groups["count"].Value,
                NumberStyles.None,
                CultureInfo.InvariantCulture);
        }

        return counts;
    }

    /// <summary>One pinned action reference as the workflow carries it.</summary>
    /// <param name="Action">The owner-and-name reference, such as <c>actions/checkout</c>.</param>
    /// <param name="Commit">The 40-character commit the step is pinned to.</param>
    /// <param name="Release">The release recorded for that commit, such as <c>v4.4.0</c>.</param>
    /// <param name="Line">The one-based line number, so a failure names where to look.</param>
    private sealed record ActionPin(string Action, string Commit, string Release, int Line);

    /// <summary>Reads every <c>uses:</c> step in the workflow.</summary>
    /// <returns>One entry per step, in file order.</returns>
    /// <remarks>
    /// Each step is asserted to be well formed HERE rather than in the callers, so a malformed reference
    /// fails whichever guard reads it first and no caller has to defend against a partial parse.
    /// </remarks>
    private static IReadOnlyList<ActionPin> ReadWorkflowActionPins()
    {
        List<ActionPin> pins = [];

        foreach ((string line, int number) in ReadRepositoryLines(WorkflowRelativePath)
            .Select(static (text, index) => (text, index + 1)))
        {
            string statement = line.TrimStart();

            if (!statement.StartsWith("uses:", StringComparison.Ordinal))
            {
                continue;
            }

            Match step = Regex.Match(
                statement,
                @"^uses:\s+(?<action>[A-Za-z0-9._-]+/[A-Za-z0-9._/-]+)@(?<reference>[^\s#]+)(?<trailer>.*)$",
                RegexOptions.None,
                RegexBudget);

            Assert.True(
                step.Success,
                $"{WorkflowRelativePath} line {number} is a `uses:` step this guard cannot parse: "
                    + $"'{statement}'. Every step must read `uses: owner/action@<40-hex commit> # vX.Y.Z`.");

            string reference = step.Groups["reference"].Value;

            Assert.True(
                Regex.IsMatch(reference, "^[0-9a-f]{40}$", RegexOptions.None, RegexBudget),
                $"{WorkflowRelativePath} line {number} references "
                    + $"'{step.Groups["action"].Value}@{reference}', which is not a 40-character commit. A "
                    + "tag or branch is a MOVEABLE reference: it can be repointed at different code after "
                    + "the version was reviewed, so the run is not reproducible and the review does not "
                    + "bind. Resolve it with `git ls-remote https://github.com/<owner>/<action> "
                    + "refs/tags/<tag>` and pin the commit.");

            Match release = Regex.Match(
                step.Groups["trailer"].Value,
                @"^\s+#\s+(?<release>v\d+(\.\d+)*)\s*$",
                RegexOptions.None,
                RegexBudget);

            Assert.True(
                release.Success,
                $"{WorkflowRelativePath} line {number} pins a commit but records no release for it. The "
                    + "trailing `# vX.Y.Z` comment has no effect on what runs and is the only thing that "
                    + "makes the SHA readable to a human, to a reviewer and to a dependency scanner.");

            pins.Add(new ActionPin(
                step.Groups["action"].Value,
                reference,
                release.Groups["release"].Value,
                number));
        }

        return pins;
    }

    /// <summary>Reads the provenance table in the workflow's own header comment.</summary>
    /// <returns>The claimed pins, keyed by action.</returns>
    private static IReadOnlyDictionary<string, ActionPin> ReadWorkflowHeaderInventory()
        => ReadInventory(
            WorkflowRelativePath,
            @"^#\s+(?<action>[A-Za-z0-9._-]+/[A-Za-z0-9._-]+)\s+(?<commit>[0-9a-f]{40})\s+(?<release>v\d+(\.\d+)*)\s*$");

    /// <summary>Reads the GitHub Actions inventory table published by the build document.</summary>
    /// <returns>The claimed pins, keyed by action.</returns>
    private static IReadOnlyDictionary<string, ActionPin> ReadBuildDocumentInventory()
        => ReadInventory(
            BuildDocumentRelativePath,
            @"^\|\s*`(?<action>[A-Za-z0-9._-]+/[A-Za-z0-9._-]+)`\s*\|\s*(?<release>v\d+(\.\d+)*)\s*\|\s*`(?<commit>[0-9a-f]{40})`\s*\|");

    /// <summary>Reads one claimant's inventory rows.</summary>
    /// <param name="relativePath">The repository-relative file to read.</param>
    /// <param name="pattern">The row pattern, which must capture action, commit and release.</param>
    /// <returns>The rows, keyed by action.</returns>
    /// <remarks>
    /// A repeated action is rejected rather than collapsed, because a dictionary is exactly what would
    /// hide two rows disagreeing about one action's commit.
    /// </remarks>
    private static IReadOnlyDictionary<string, ActionPin> ReadInventory(string relativePath, string pattern)
    {
        Dictionary<string, ActionPin> rows = new(StringComparer.Ordinal);

        foreach ((string line, int number) in ReadRepositoryLines(relativePath)
            .Select(static (text, index) => (text, index + 1)))
        {
            Match row = Regex.Match(line, pattern, RegexOptions.None, RegexBudget);

            if (!row.Success)
            {
                continue;
            }

            string action = row.Groups["action"].Value;

            // TryGetValue rather than ContainsKey plus an indexer inside the message: an interpolated
            // failure message is built EAGERLY, so indexing a key that is absent in the passing case
            // would throw on every row.
            if (rows.TryGetValue(action, out ActionPin? existing))
            {
                throw FailException.ForFailure(
                    $"{relativePath} lists '{action}' twice, at lines {existing.Line} and {number}. One "
                        + "action, one row: two rows can disagree, and a reader has no way to tell which "
                        + "is authoritative.");
            }

            rows[action] = new ActionPin(
                action,
                row.Groups["commit"].Value,
                row.Groups["release"].Value,
                number);
        }

        // AN EMPTY RESULT IS A LEGITIMATE ANSWER AND IS DELIBERATELY NOT REFUSED HERE. This workflow
        // references no marketplace action, so both inventories correctly carry no rows; asserting a
        // non-empty table here would make the absence unrepresentable. The caller is what decides
        // whether empty is right, and it additionally requires each artifact to STATE the position in
        // words - which is the check that keeps "no rows" from being indistinguishable from "no table".
        return rows;
    }

    /// <summary>Reads a repository-relative file as lines, failing when it is absent.</summary>
    /// <param name="relativePath">The forward-slash repository-relative path.</param>
    /// <returns>The file's lines.</returns>
    private static string[] ReadRepositoryLines(string relativePath)
    {
        string path = Path.Combine(
            [RequireRepositoryRoot(), .. relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries)]);

        return File.Exists(path)
            ? File.ReadAllLines(path)
            : throw FailException.ForFailure(
                $"'{relativePath}' does not exist. It is one of the artifacts this suite holds to its own "
                    + "published claims, so its absence is a finding rather than a reason to skip a check.");
    }

    /// <summary>Reads every central pin.</summary>
    /// <returns>The pinned identities and their exact version strings.</returns>
    private static IReadOnlyDictionary<string, string> ReadCentralPins()
    {
        string path = Path.Combine(RequireRepositoryRoot(), PackageManifestFileName);

        string text = File.Exists(path)
            ? File.ReadAllText(path)
            : throw FailException.ForFailure(
                $"'{PackageManifestFileName}' does not exist at the repository root. It is the single "
                    + "version authority for all twenty projects, so its absence is a finding rather than "
                    + "a reason to skip a check.");

        Dictionary<string, string> pins = new(StringComparer.Ordinal);

        foreach (Match pin in Regex.Matches(
            text,
            @"<PackageVersion\s+Include=""(?<id>[^""]+)""\s+Version=""(?<version>[^""]+)""",
            RegexOptions.None,
            RegexBudget))
        {
            string id = pin.Groups["id"].Value;

            Assert.False(
                pins.ContainsKey(id),
                $"{PackageManifestFileName} declares '{id}' more than once. Central package management "
                    + "would resolve one of the two and the other would be silently dead.");

            pins[id] = pin.Groups["version"].Value;
        }

        return pins;
    }

    /// <summary>Reads NOTICE's direct package list.</summary>
    /// <returns>The listed identities and their published version strings.</returns>
    /// <remarks>
    /// The list is bounded by its own heading and its own footnote rather than by line numbers, so
    /// editing prose above or below it cannot silently move the window this reads.
    /// </remarks>
    private static IReadOnlyDictionary<string, string> ReadNoticeDirectList()
    {
        string path = Path.Combine(RequireRepositoryRoot(), NoticeFileName);

        string[] lines = File.Exists(path)
            ? File.ReadAllLines(path)
            : throw FailException.ForFailure(
                $"'{NoticeFileName}' does not exist at the repository root. It carries the licence, the "
                    + "attributions and the dependency inventory, so its absence is a finding rather than "
                    + "a reason to skip a check.");

        int start = Array.FindIndex(lines, line => line.Trim() == DirectListHeading);

        Assert.True(
            start >= 0,
            $"{NoticeFileName} carries no '{DirectListHeading}' heading, so its direct package list "
                + "cannot be located. The heading is what bounds the list.");

        int end = Array.FindIndex(
            lines,
            start,
            line => line.TrimStart().StartsWith(DirectListTerminator, StringComparison.Ordinal));

        Assert.True(
            end > start,
            $"{NoticeFileName} carries no '{DirectListTerminator}' footnote after its direct package "
                + "list, so the end of the list cannot be located.");

        Dictionary<string, string> listed = new(StringComparer.Ordinal);

        for (int index = start + 1; index < end; index++)
        {
            Match entry = Regex.Match(
                lines[index],
                @"^  (?<id>[A-Za-z][A-Za-z0-9_.-]*)\s{2,}(?<version>\d[^\s]*)",
                RegexOptions.None,
                RegexBudget);

            if (!entry.Success)
            {
                continue;
            }

            listed[entry.Groups["id"].Value] = entry.Groups["version"].Value;
        }

        return listed;
    }

    /// <summary>
    /// The build document's target-file inventory is DERIVED from git rather than compared to a
    /// transcribed figure, and the arithmetic it publishes actually closes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 THIS GUARD IS THE ANSWER TO A FINDING, AND THE FINDING WAS THAT PROSE COUNTS GO STALE. The
    /// document published a CREATE count, an UPDATE count, a tracked-file total and a per-group table,
    /// and every one of the four had drifted from the tree by tens of files - while the document's own
    /// section 16.6 was titled "what keeps this section honest". Nothing kept those particular figures
    /// honest, because a count written in prose is a fact with no owner.
    /// </para>
    /// <para>
    /// WHY IT DERIVES RATHER THAN COMPARES TO AN EXPECTED CONSTANT. An expected constant here would be a
    /// SECOND transcription of the same fact, drifting on the same day the first one does and needing the
    /// same manual bump. What is asserted instead is that the numbers the document prints equal the
    /// numbers git reports, so the only way to satisfy this test is to make the document true.
    /// </para>
    /// <para>
    /// THE COMPARISON RUNS BASELINE -&gt; WORKING TREE, NOT BASELINE -&gt; HEAD, and that is deliberate.
    /// A file authored and staged but not yet committed is part of the delivered tree, and a
    /// <c>HEAD</c>-anchored count would jump the moment <c>git commit</c> ran - so a document correct
    /// before the commit would be wrong after it, for no change in content. The document's section 16.1
    /// publishes the same command shape for the same reason.
    /// </para>
    /// <para>
    /// IT SKIPS RATHER THAN FAILS WHEN GIT CANNOT ANSWER. A shallow CI clone does not carry the
    /// pre-refactor baseline commit, and neither does an exported archive with no history. Failing there
    /// would make this suite report a defect in the documentation when the real condition is a checkout
    /// with no history to measure - so the skip is reported with its reason, and the environments that
    /// CAN answer are the ones that hold the document to the tree.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheBuildDocumentsTargetFileInventoryAgreesWithGit()
    {
        string root = RequireRepositoryRoot();

        if (RunGit(root, "cat-file", "-e", BaselineCommit + "^{commit}") is null)
        {
            Assert.Skip(
                $"The pre-refactor baseline commit '{BaselineCommit}' is not present in this checkout, so "
                + "the inventory cannot be derived here. A shallow clone or an export with no history "
                + "reaches this path; a full clone does not.");

            return;
        }

        string? status = RunGit(root, "-c", "core.quotePath=false", "diff", "--name-status", BaselineCommit);

        Assert.NotNull(status);

        Dictionary<char, int> operations = new();
        List<string> updated = [];
        int changed = 0;

        foreach (string line in status.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] fields = line.Split('\t', StringSplitOptions.RemoveEmptyEntries);

            if (fields.Length < 2)
            {
                continue;
            }

            char operation = fields[0][0];

            operations[operation] = operations.GetValueOrDefault(operation) + 1;
            changed++;

            if (operation == 'M')
            {
                updated.Add(fields[^1]);
            }
        }

        string? listing = RunGit(root, "-c", "core.quotePath=false", "ls-files");

        Assert.NotNull(listing);

        int tracked = listing.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
        int created = operations.GetValueOrDefault('A');
        int deleted = operations.GetValueOrDefault('D');

        // The two identities the document publishes, asserted here rather than trusted there.
        Assert.Equal(changed, created + operations.GetValueOrDefault('M') + deleted);
        Assert.Equal(tracked, BaselineTrackedFiles + created);

        // C-C, and the measurement that matters most: the .NET tree is purely additive.
        Assert.Equal(0, deleted);

        // Exactly two pre-existing files are amended, and they are named rather than counted.
        Assert.Equal<string[]>([".gitignore", "README.md"], [.. updated.Order(StringComparer.Ordinal)]);

        string document = File.ReadAllText(Path.Combine(root, BuildDocumentRelativePath));

        AssertDocumentPublishes(document, "total target files", changed);
        AssertDocumentPublishes(document, "CREATE count", created);
        AssertDocumentPublishes(document, "tracked-file total", tracked);

        // The per-group table must sum to the same total, so a group added without its files being
        // counted - or a file counted twice - fails here rather than in a reader's arithmetic.
        Assert.Equal(changed, SumOfPerGroupFileCounts(document));
    }

    /// <summary>
    /// Asserts a derived figure appears in the document, and that no stale neighbour of it survives.
    /// </summary>
    /// <param name="document">The document text.</param>
    /// <param name="what">What the figure is, for the failure message.</param>
    /// <param name="value">The derived value.</param>
    /// <remarks>
    /// Presence alone is a weak assertion, so this also refuses the specific stale values the finding
    /// reported. That pairing is what makes the test catch the realistic failure: a document updated in
    /// one of its four places and not the others would otherwise pass on the strength of the one.
    /// </remarks>
    private static void AssertDocumentPublishes(string document, string what, int value)
    {
        string rendered = value.ToString(CultureInfo.InvariantCulture);

        Assert.True(
            Regex.IsMatch(document, $@"\b{rendered}\b", RegexOptions.None, RegexBudget),
            $"docs/BUILD.md does not publish the derived {what} of {rendered}. Section 16 is the single "
            + "measurement of target scope, and its figures are derived from git by this test rather than "
            + "transcribed - so the fix is to update the document, never to relax this assertion.");
    }

    /// <summary>Sums the file column of the build document's per-group inventory table.</summary>
    /// <param name="document">The document text.</param>
    /// <returns>The sum of the per-group file counts.</returns>
    /// <remarks>
    /// The table is located by its own header row rather than by a line number, and reading stops at the
    /// total row. A table located by position would silently start summing a different table the first
    /// time anything above it grew a line.
    /// </remarks>
    private static int SumOfPerGroupFileCounts(string document)
    {
        string[] lines = document.Split('\n');

        int header = Array.FindIndex(
            lines,
            static line => line.StartsWith("| # | Group | Files | CREATE | UPDATE |", StringComparison.Ordinal));

        Assert.True(header >= 0, "docs/BUILD.md carries no per-group inventory table with the expected header.");

        int sum = 0;

        for (int index = header + 2; index < lines.Length; index++)
        {
            string line = lines[index].Trim();

            if (!line.StartsWith('|'))
            {
                break;
            }

            if (line.Contains("**Total**", StringComparison.Ordinal))
            {
                break;
            }

            string[] cells = line.Split('|', StringSplitOptions.RemoveEmptyEntries);

            Assert.True(cells.Length >= 3, $"Unexpected inventory row: {line}");

            sum += int.Parse(cells[2].Trim(), CultureInfo.InvariantCulture);
        }

        return sum;
    }

    /// <summary>Runs one git command, answering null when git cannot answer at all.</summary>
    /// <param name="root">The working directory.</param>
    /// <param name="arguments">The git arguments.</param>
    /// <returns>Standard output, or null on a non-zero exit or a missing git.</returns>
    /// <remarks>
    /// A NON-ZERO EXIT IS A "CANNOT ANSWER", NOT A FAILURE, which is what lets the caller skip rather than
    /// fail on a checkout with no history. The process is read-only: nothing here writes to the index,
    /// the working tree or the object store.
    /// </remarks>
    private static string? RunGit(string root, params string[] arguments)
    {
        System.Diagnostics.ProcessStartInfo start = new("git")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using System.Diagnostics.Process? process = System.Diagnostics.Process.Start(start);

        if (process is null)
        {
            return null;
        }

        string output = process.StandardOutput.ReadToEnd();

        _ = process.StandardError.ReadToEnd();

        process.WaitForExit();

        return process.ExitCode == 0 ? output : null;
    }

    /// <summary>Locates the repository root by walking up from the test binary.</summary>
    /// <returns>The absolute repository-root path.</returns>
    /// <remarks>
    /// Both repository markers are required together, so the walk cannot latch onto a same-named
    /// directory elsewhere on the machine. The probe is read-only, and it starts at the embedded root
    /// when the build supplied one - see <see cref="TestRepositoryRoot"/>.
    /// </remarks>
    private static string RequireRepositoryRoot()
    {
        DirectoryInfo? candidate = new(TestRepositoryRoot.SearchStart);

        while (candidate is not null)
        {
            if (File.Exists(Path.Combine(candidate.FullName, SolutionMarkerFileName))
                && File.Exists(Path.Combine(candidate.FullName, PackageManifestFileName)))
            {
                return candidate.FullName;
            }

            // Parent is null at the filesystem root, which is what terminates the walk.
            candidate = candidate.Parent;
        }

        throw FailException.ForFailure(
            $"No directory from '{TestRepositoryRoot.SearchStart}' up to the filesystem root holds both "
                + $"repository markers '{SolutionMarkerFileName}' and '{PackageManifestFileName}'.");
    }
}
