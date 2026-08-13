// =====================================================================================================
//  CharacterizationWorkflowGuardTests - THE EXECUTABLE GUARD ON THE CHARACTERIZATION STORE
// =====================================================================================================
//
//  WHAT THIS FILE IS FOR
//    characterization/workflows/ carries fifteen golden-master workflow definitions and the JSON Schema
//    they are written against. The schema is the shape; nothing was CHECKING the definitions against it.
//    A schema that nothing validates is a comment: the editor extension named in each file's first line
//    validates for whoever happens to open the file in that editor, and for nobody else and in no build.
//
//    So the failures this guard exists to catch could all reach a reviewer looking correct: a member the
//    schema requires quietly dropped, a workflow identifier that no longer matches its own file name, a
//    determinism mask applied to only one half of a pair, a locator pointing at a legacy line that no
//    longer exists, a related-workflow reference to a definition that was renamed, an execution status
//    claiming nothing has been captured while recordings sit on disk. Every one of those turns a
//    characterization store into a store that describes captures nobody can take.
//
//  WHY THE RULES BELOW ARE THE RULES THE PLAN STATES, NOT AN INVENTED HOUSE STYLE
//    * AAP 0.6.7 requires that non-deterministic values be masked from BOTH the master and the candidate.
//      That is why every mask entry must apply to legacy AND dotnet, and why a one-sided mask is a
//      failure rather than a partial measure - masking one side changes the comparison rather than
//      neutralising it.
//    * AAP 0.6.7 also states that executionStatus "may only be specified-not-executed" in this phase, and
//      docs/PARITY.md 4.1 states that a recording whose identifier does not exist on the other side "is
//      not a comparison at all, and it must not be reported as a pass". Both are asserted, and asserted
//      as an EQUIVALENCE with what is on disk: recordings present with a not-executed status is a
//      contradiction, and so is a half-captured pair.
//    * AAP 0.3.2.3 renames the persistence volume from the environment's data-service-db to
//      persistence-db and requires the capture rule be restated verbatim against the new name, so the
//      store's own README is checked for it. A rename that lost the rule would leave the pair rule
//      documented against a volume that no longer exists.
//    * Every behavioural claim in this refactor must be traceable to a legacy locator (AAP 0.1.4), which
//      is why locators are RESOLVED here - file existence and line bounds - rather than pattern-matched.
//
//  WHAT IT DELIBERATELY IS NOT
//    Not a general JSON Schema validator. Writing one would mean either adding a package - forbidden by
//    AAP 0.5.1 and 0.5.3, and a hard restore error under central package management - or hand-rolling a
//    draft-2020-12 implementation, which would then need its own tests. Instead this reads the schema as
//    data and enforces the parts of it that carry meaning for this store: the closed member sets, the
//    constants, the enumerations, the patterns, the minimum sizes and the two conditional requirements.
//    The one thing that keeps that honest is TheGuardCoversEveryMemberTheSchemaDeclares: the schema's own
//    member list is compared against the list this file knows about, so a schema that grows a member
//    fails until this guard is extended to cover it.
//
//  THE YAML PARSER, AND WHY NO PACKAGE IS ADDED FOR IT
//    SharpYaml 2.1.4 is already in this project's compile closure: it is the single net assembly addition
//    that Microsoft.OpenApi.YamlReader 2.11.0 brings, that reference is version locked, and both are
//    recorded in the root NOTICE - Directory.Packages.props documents exactly that. Reading YAML with the
//    parser the project already resolves therefore adds nothing to the dependency graph, which is the
//    only way this guard can exist at all: a new PackageReference has no central version and fails
//    restore with NU1010.
//
//  READ-ONLY THROUGHOUT. The legacy tree, the definitions, the schema and the recordings are all read and
//  never written. A disagreement is a finding for a human, never something for a test to reconcile.
// =====================================================================================================

using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using SharpYaml.Serialization;
using Xunit;
using Xunit.Sdk;

namespace PowerFramework.Contracts.Tests;

/// <summary>
/// One workflow definition as read from disk.
/// </summary>
/// <param name="FileName">The file name including its extension.</param>
/// <param name="WorkflowId">The declared identifier, or the empty string when it is absent.</param>
/// <param name="Root">The parsed document's root mapping.</param>
internal sealed record WorkflowDefinition(
    string FileName,
    string WorkflowId,
    IDictionary<object, object> Root);

/// <summary>
/// The characterization store: the schema, the fifteen definitions, and the recording directories.
/// </summary>
/// <remarks>
/// Read once per run. Nothing here can change during a run, and re-parsing fifteen documents totalling
/// several thousand lines for every check would cost more than it proves.
/// </remarks>
internal static class CharacterizationStore
{
    /// <summary>The store root, relative to the repository root.</summary>
    internal const string StoreDirectoryName = "characterization";

    /// <summary>The schema every definition is written against.</summary>
    internal const string SchemaFileName = "workflow.schema.json";

    /// <summary>The volume the capture rule is stated against after the AAP 0.3.2.3 rename.</summary>
    internal const string PersistenceVolumeName = "persistence-db";

    /// <summary>A bound on every regex here, so a pathological input cannot hang a run.</summary>
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(5);

    /// <summary>Gets the workflows directory.</summary>
    internal static string WorkflowsDirectory { get; } = Path.Combine(
        EstateInventory.RepositoryRoot,
        StoreDirectoryName,
        "workflows");

    /// <summary>Gets the parsed schema.</summary>
    internal static JsonDocument Schema { get; } = ReadSchema();

    /// <summary>Gets every definition, ordered by file name.</summary>
    internal static ImmutableArray<WorkflowDefinition> Definitions { get; } = ReadDefinitions();

    /// <summary>Gets every declared workflow identifier.</summary>
    internal static ImmutableArray<string> WorkflowIds { get; } =
        [.. Definitions.Select(static definition => definition.WorkflowId)];

    /// <summary>Gets the definitions projected for <c>[MemberData]</c>, one row per file.</summary>
    /// <returns>The file names.</returns>
    /// <remarks>
    /// The file name alone, so the framework renders it into the test name and a CI log names the failing
    /// definition. The document itself is recovered inside the body through <see cref="Require"/>.
    /// </remarks>
    public static TheoryData<string> DefinitionFiles()
    {
        TheoryData<string> rows = [];

        foreach (WorkflowDefinition definition in Definitions)
        {
            rows.Add(definition.FileName);
        }

        return rows;
    }

    /// <summary>Recovers one definition by file name.</summary>
    /// <param name="fileName">The file name.</param>
    /// <returns>The definition.</returns>
    internal static WorkflowDefinition Require(string fileName) =>
        Definitions.SingleOrDefault(definition =>
            string.Equals(definition.FileName, fileName, StringComparison.Ordinal))
        ?? throw FailException.ForFailure(
            $"No workflow definition named '{fileName}' was read from '{StoreDirectoryName}/workflows'.");

    /// <summary>Reads one top-level schema property node.</summary>
    /// <param name="propertyName">The member name.</param>
    /// <returns>The schema node describing it.</returns>
    internal static JsonElement SchemaProperty(string propertyName)
    {
        JsonElement properties = Schema.RootElement.GetProperty("properties");

        return properties.TryGetProperty(propertyName, out JsonElement property)
            ? property
            : throw FailException.ForFailure(
                $"'{SchemaFileName}' declares no member '{propertyName}'. This guard enforces that "
                    + "member, so its removal is a finding rather than something to skip.");
    }

    /// <summary>Reads a closed enumeration out of the schema, so the test cannot drift from it.</summary>
    /// <param name="node">The schema node carrying the <c>enum</c> keyword.</param>
    /// <returns>The permitted values.</returns>
    internal static ImmutableArray<string> SchemaEnum(JsonElement node)
    {
        ImmutableArray<string>.Builder values = ImmutableArray.CreateBuilder<string>();

        foreach (JsonElement value in node.GetProperty("enum").EnumerateArray())
        {
            values.Add(value.GetString() ?? string.Empty);
        }

        return values.ToImmutable();
    }

    /// <summary>Reads the member names a schema object node declares.</summary>
    /// <param name="node">The schema node.</param>
    /// <returns>The member names, sorted.</returns>
    internal static ImmutableArray<string> SchemaMembers(JsonElement node) =>
        [.. node.GetProperty("properties").EnumerateObject()
            .Select(static member => member.Name)
            .Order(StringComparer.Ordinal)];

    /// <summary>Reads the member names a schema object node requires.</summary>
    /// <param name="node">The schema node.</param>
    /// <returns>The required member names, sorted.</returns>
    internal static ImmutableArray<string> SchemaRequired(JsonElement node) =>
        [.. node.GetProperty("required").EnumerateArray()
            .Select(static member => member.GetString() ?? string.Empty)
            .Order(StringComparer.Ordinal)];

    /// <summary>Reads a recording directory path for one half of a pair.</summary>
    /// <param name="half">Either <c>legacy</c> or <c>dotnet</c>.</param>
    /// <param name="workflowId">The pairing key.</param>
    /// <returns>The absolute directory path, which may not exist.</returns>
    internal static string RecordingDirectory(string half, string workflowId) => Path.Combine(
        EstateInventory.RepositoryRoot,
        StoreDirectoryName,
        "recordings",
        half,
        workflowId);

    /// <summary>
    /// Whether a recording directory holds a file an actual capture would have written.
    /// </summary>
    /// <param name="directory">The directory.</param>
    /// <returns><see langword="true"/> when a real capture is present.</returns>
    /// <remarks>
    /// The AAP's own target tree creates these directories and the store commits a readme into each, so a
    /// predicate satisfied by mere existence would report every workflow as captured from the moment the
    /// scaffold landed. A dot-file and a readme are scaffolding; anything else is a capture.
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
                && !string.Equals(name, "README.md", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Reads the store's own readme.</summary>
    /// <returns>Its text.</returns>
    internal static string ReadStoreReadme()
    {
        string path = Path.Combine(EstateInventory.RepositoryRoot, StoreDirectoryName, "README.md");

        return File.Exists(path)
            ? File.ReadAllText(path)
            : throw FailException.ForFailure(
                $"'{StoreDirectoryName}/README.md' does not exist. It carries the capture rule's "
                    + "canonical text, which the schema explicitly declines to restate, so its absence "
                    + "is a finding rather than a reason to skip a check.");
    }

    /// <summary>Compiles a schema pattern into a bounded regex anchored at both ends.</summary>
    /// <param name="node">The schema node carrying the <c>pattern</c> keyword.</param>
    /// <returns>The compiled pattern.</returns>
    internal static Regex SchemaPattern(JsonElement node) => new(
        node.GetProperty("pattern").GetString() ?? "^$",
        RegexOptions.None,
        MatchTimeout);

    /// <summary>Reads and parses the schema.</summary>
    /// <returns>The parsed document.</returns>
    private static JsonDocument ReadSchema()
    {
        string path = Path.Combine(WorkflowsDirectory, SchemaFileName);

        return File.Exists(path)
            ? JsonDocument.Parse(File.ReadAllText(path))
            : throw FailException.ForFailure(
                $"'{StoreDirectoryName}/workflows/{SchemaFileName}' does not exist. It is the shape every "
                    + "definition is written against, so its absence is a finding rather than a reason to "
                    + "skip a check.");
    }

    /// <summary>Reads and parses every definition.</summary>
    /// <returns>The definitions, ordered by file name.</returns>
    private static ImmutableArray<WorkflowDefinition> ReadDefinitions()
    {
        if (!Directory.Exists(WorkflowsDirectory))
        {
            throw FailException.ForFailure(
                $"'{StoreDirectoryName}/workflows' does not exist. It carries the golden-master workflow "
                    + "definitions AAP 0.6.7 requires, so its absence is a finding rather than a reason "
                    + "to skip a check.");
        }

        Serializer serializer = new();

        ImmutableArray<WorkflowDefinition>.Builder definitions =
            ImmutableArray.CreateBuilder<WorkflowDefinition>();

        foreach (string path in Directory.EnumerateFiles(WorkflowsDirectory, "*.yaml")
            .Order(StringComparer.Ordinal))
        {
            object? parsed;

            try
            {
                parsed = serializer.Deserialize(File.ReadAllText(path));
            }
            catch (SharpYaml.YamlException failure)
            {
                throw FailException.ForFailure(
                    $"'{Path.GetFileName(path)}' is not valid YAML: {failure.Message}");
            }

            if (parsed is not IDictionary<object, object> root)
            {
                throw FailException.ForFailure(
                    $"'{Path.GetFileName(path)}' does not parse to a mapping, so it cannot be a workflow "
                        + "definition.");
            }

            definitions.Add(new WorkflowDefinition(
                Path.GetFileName(path),
                root.TryGetValue("workflowId", out object? id) ? id as string ?? string.Empty : string.Empty,
                root));
        }

        return definitions.ToImmutable();
    }
}

/// <summary>
/// Every committed characterization workflow definition satisfies its schema, resolves its locators
/// against the read-only legacy tree, masks both halves of its pair, and agrees with the recordings on
/// disk.
/// </summary>
public sealed class CharacterizationWorkflowGuardTests
{
    /// <summary>The committed roster size.</summary>
    /// <remarks>
    /// A literal rather than the collection's own length, because comparing a count to itself is a
    /// tautology. A definition added or removed deliberately moves this number in the same reviewed change,
    /// which is the point.
    /// </remarks>
    private const int ExpectedWorkflowCount = 15;

    /// <summary>
    /// Every top-level member the schema declares, so a schema that grows one fails this guard until the
    /// guard covers it.
    /// </summary>
    private static readonly ImmutableArray<string> CoveredMembers =
    [
        "blockedBehaviours",
        "capabilityArea",
        "capturePhase",
        "deferredCapabilities",
        "description",
        "determinismMask",
        "executionStatus",
        "oracleFixtures",
        "provesDefects",
        "relatedWorkflows",
        "schemaVersion",
        "seedPhase",
        "sharedVolume",
        "targetService",
        "title",
        "workflowId",
    ];

    /// <summary>
    /// One <c>path:Lnnn</c> or <c>path:Lnnn-Lmmm</c> reference, or a bare <c>, Lnnn</c> continuation that
    /// belongs to the path most recently named in the same string.
    /// </summary>
    /// <remarks>
    /// THE CORPUS'S ACTUAL CONVENTION, MEASURED RATHER THAN ASSUMED. A locator is free text carrying one or
    /// more citations: a single line, an inclusive range, several files in one string, and continuations
    /// written as <c>:L201-L207</c> or <c>, L301</c> that inherit the previous path. The path token is a
    /// NEGATED character class rather than a list of allowed characters, because one cited document's name
    /// is Chinese - <c>docs/PB多线程绕坑提示.md</c> - and an ASCII-only class silently fails to see it.
    /// </remarks>
    private static readonly Regex LocatorReference = new(
        @"(?:(?<path>[^\s,:;()""']+\.[A-Za-z0-9]+))?:L(?<from>\d+)(?:-L(?<to>\d+))?|,\s*L(?<also>\d+)",
        RegexOptions.ExplicitCapture,
        TimeSpan.FromSeconds(5));

    /// <summary>Line counts of cited files, so a file is read at most once per run.</summary>
    private static readonly Dictionary<string, int> LineCounts = [];

    // ==============================================================================================
    //  1 - THE ROSTER, AND THE GUARD'S OWN COVERAGE OF THE SCHEMA
    // ==============================================================================================

    /// <summary>
    /// The store holds the fifteen committed definitions, each named for the workflow it declares, with no
    /// duplicate identifier.
    /// </summary>
    /// <remarks>
    /// THE FILE NAME AND THE IDENTIFIER ARE ASSERTED EQUAL, and that is not cosmetic. The identifier is the
    /// pairing key - it names <c>recordings/legacy/&lt;id&gt;/</c> and <c>recordings/dotnet/&lt;id&gt;/</c> -
    /// so a definition whose file name says one thing and whose identifier says another sends a reader to a
    /// directory that does not exist while looking perfectly consistent in isolation.
    /// </remarks>
    [Fact]
    public void TheRosterIsTheFifteenCommittedDefinitionsEachNamedForItsWorkflow()
    {
        ImmutableArray<WorkflowDefinition> definitions = CharacterizationStore.Definitions;

        Assert.Equal(ExpectedWorkflowCount, definitions.Length);

        Assert.All(
            definitions,
            static definition => Assert.Equal(
                Path.GetFileNameWithoutExtension(definition.FileName),
                definition.WorkflowId,
                StringComparer.Ordinal));

        Assert.Equal(
            definitions.Length,
            CharacterizationStore.WorkflowIds.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// The guard covers every member the schema declares, and the schema still requires the members this
    /// guard enforces.
    /// </summary>
    /// <remarks>
    /// <b>THE ANTI-DRIFT DEVICE, AND THE REASON A HAND-WRITTEN VALIDATOR IS HONEST HERE.</b> This file
    /// enforces the schema's meaning member by member rather than by running a general schema validator, so
    /// the failure mode to guard against is the schema growing a member that nothing checks - a new
    /// requirement that every definition satisfies vacuously. Comparing the schema's own member list with
    /// the list above turns that into a build failure naming the uncovered member.
    /// </remarks>
    [Fact]
    public void TheGuardCoversEveryMemberTheSchemaDeclares()
    {
        Assert.Equal(
            CoveredMembers,
            CharacterizationStore.SchemaMembers(CharacterizationStore.Schema.RootElement));

        // The schema is a closed shape, which is what makes an unknown member in a definition detectable.
        Assert.False(
            CharacterizationStore.Schema.RootElement.GetProperty("additionalProperties").GetBoolean(),
            "The schema no longer closes its top-level member set, so a definition could carry a member "
                + "nothing validates.");

        // Every member this guard treats as mandatory is still mandatory in the schema.
        ImmutableArray<string> required =
            CharacterizationStore.SchemaRequired(CharacterizationStore.Schema.RootElement);

        Assert.Equal(
            (string[])
            [
                "capabilityArea",
                "capturePhase",
                "determinismMask",
                "executionStatus",
                "oracleFixtures",
                "provesDefects",
                "schemaVersion",
                "seedPhase",
                "sharedVolume",
                "targetService",
                "title",
                "workflowId",
            ],
            required);
    }

    // ==============================================================================================
    //  2 - EVERY DEFINITION, AGAINST THE SCHEMA IT IS WRITTEN AGAINST
    // ==============================================================================================

    /// <summary>
    /// One definition satisfies its schema: closed member set, required members present, constants and
    /// enumerations honoured, patterns matched, and both conditional requirements discharged.
    /// </summary>
    /// <param name="fileName">The definition file.</param>
    /// <remarks>
    /// <para>
    /// One row per definition so a CI log names the failing file. Every enumeration, constant, pattern and
    /// minimum size is READ FROM THE SCHEMA rather than restated here, so this row cannot pass a definition
    /// the schema would reject or fail one it would accept.
    /// </para>
    /// <para>
    /// The two conditional requirements are the ones a hand-written check most easily misses, and both
    /// exist to stop an omission reading as an exemption: an empty seed phase must say WHY it is empty, and
    /// a workflow claiming exemption from the shared-volume rule must say why it is exempt.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(
        nameof(CharacterizationStore.DefinitionFiles),
        MemberType = typeof(CharacterizationStore))]
    public void EveryDefinitionSatisfiesItsSchema(string fileName)
    {
        WorkflowDefinition definition = CharacterizationStore.Require(fileName);
        IDictionary<object, object> root = definition.Root;

        // CLOSED MEMBER SET. additionalProperties is false, so a member the schema does not declare is a
        // validation error rather than a harmless extra.
        foreach (object key in root.Keys)
        {
            Assert.Contains(Convert.ToString(key, CultureInfo.InvariantCulture) ?? string.Empty, CoveredMembers);
        }

        foreach (string required in CharacterizationStore.SchemaRequired(
            CharacterizationStore.Schema.RootElement))
        {
            Assert.True(
                root.ContainsKey(required),
                $"'{fileName}' omits the required member '{required}'.");
        }

        // schemaVersion is a const, so it is compared with the schema's own value.
        Assert.Equal(
            CharacterizationStore.SchemaProperty("schemaVersion").GetProperty("const").GetString(),
            Text(root, "schemaVersion"),
            StringComparer.Ordinal);

        Assert.Matches(
            CharacterizationStore.SchemaPattern(CharacterizationStore.SchemaProperty("workflowId")),
            definition.WorkflowId);

        Assert.NotEmpty(Text(root, "title"));
        Assert.NotEmpty(Text(root, "capabilityArea"));

        Assert.Contains(
            Text(root, "targetService"),
            CharacterizationStore.SchemaEnum(CharacterizationStore.SchemaProperty("targetService")));

        // executionStatus is a single-member enum in this phase: AAP 0.6.7 permits no other value while no
        // capture has been taken.
        Assert.Contains(
            Text(root, "executionStatus"),
            CharacterizationStore.SchemaEnum(CharacterizationStore.SchemaProperty("executionStatus")));

        AssertOracleFixtures(fileName, root);
        AssertSeedPhase(fileName, root);
        AssertCapturePhase(fileName, root);
        AssertDeterminismMask(fileName, root);
        AssertProvesDefects(fileName, root);
        AssertOptionalRosters(fileName, root);
        AssertSharedVolume(fileName, root);
    }

    /// <summary>
    /// No recording may persist a literal value: the treatment set is closed to placeholder-redaction and
    /// omission, and every declared treatment is a member of it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 THE REDACTION ARRAY WAS THE ONE MEMBER OF THIS SCHEMA WITH NO GUARD AT ALL, and it is the most
    /// security-sensitive member in it. <see cref="AssertCapturePhase"/> checks the capture phase's
    /// behaviour text and its observed outputs and stops there, so the treatments went entirely unread -
    /// and because <see cref="EveryDefinitionSatisfiesItsSchema"/> is a hand-written member walk rather
    /// than a JSON Schema validator, tightening the schema's own enumeration would not have been enforced
    /// by any existing row either. Both halves are closed here.
    /// </para>
    /// <para>
    /// WHAT THE WITHDRAWN TREATMENT DID. <c>split-statement-and-parameters</c> recorded a statement's text
    /// separately from the values it carried, which means it WROTE those values - into a tracked, committed
    /// file, for a field the legacy fills with the complete generated statement including interpolated
    /// literals wherever the connection's bind-disabling flag is set. Four persistence workflows declared
    /// it. <c>docs/SECRETS.md</c> §6.3 had already recorded the decision against it - a parameter
    /// collection is itself the sensitive data, so separating a literal from its statement moves the value
    /// rather than protecting it - and <c>characterization/README.md</c> §6.2 already stated the recording
    /// rule as one redacted field with nothing beside it. The enum member was residue of wording withdrawn
    /// elsewhere, and it described a shape the published contract cannot even produce:
    /// <c>common.v1.DbError</c> declares <c>sqlsyntax</c> and has no <c>parameters</c> member.
    /// </para>
    /// <para>
    /// NOTHING IS LOST BY REDACTING INSTEAD. Byte-exact parity is measured on a statement's SHAPE, and the
    /// placeholder preserves the shape exactly; the values were never part of what the comparison reads.
    /// </para>
    /// </remarks>
    [Fact]
    public void NoRecordingTreatmentMayPersistALiteralValue()
    {
        // THE SCHEMA'S OWN ENUMERATION, PINNED. Read from the schema rather than restated, so this row
        // fails if the enumeration changes rather than silently describing a stale one.
        JsonElement treatment = CharacterizationStore.Schema.RootElement
            .GetProperty("properties")
            .GetProperty("capturePhase")
            .GetProperty("properties")
            .GetProperty("redaction")
            .GetProperty("items")
            .GetProperty("properties")
            .GetProperty("treatment");

        ImmutableArray<string> permitted = CharacterizationStore.SchemaEnum(treatment);

        // Both sides materialised into arrays before comparison: a collection expression here binds
        // ambiguously between Assert.Equal<T>(T?, T?) and the ReadOnlySpan<T> overload (CS0121).
        string[] expected = ["redacted", "omitted"];
        string[] declared = [.. permitted];

        Assert.Equal(expected, declared);

        // NAMED EXPLICITLY, so a reader of a failure knows which value must never return and why.
        Assert.DoesNotContain("split-statement-and-parameters", permitted);

        int declarations = 0;

        foreach (WorkflowDefinition definition in CharacterizationStore.Definitions)
        {
            IDictionary<object, object> capture = Mapping(definition.Root, "capturePhase");

            foreach (IDictionary<object, object> entry in Items(capture, "redaction"))
            {
                declarations++;

                Assert.NotEmpty(Text(entry, "field"));

                Assert.Contains(Text(entry, "treatment"), permitted);
            }
        }

        // THE STORE REALLY DOES DECLARE REDACTIONS, so a guard that read nothing cannot pass as a guard
        // that found nothing wrong. Every treatment above was checked against the closed set.
        Assert.True(
            declarations >= 15,
            $"Only {declarations} redaction declarations were read across the store, which is fewer than "
                + "the persistence and security workflows alone declare - the walk is not reaching them.");
    }

    /// <summary>
    /// Wherever a workflow withholds the transaction descriptor's password it also withholds its user
    /// name, because both recordings READMEs rule the two equally excluded.
    /// </summary>
    /// <remarks>
    /// <b>AN EXCLUSION THAT RESTED ON PROSE ALONE.</b> Both recordings READMEs already ruled the
    /// descriptor's user-name field "equally sensitive and equally excluded from every recording", yet no
    /// workflow named it while three named the password beside it - so the store asserted an exclusion its
    /// own declarations did not carry, in the one array whose entire purpose is that a withheld field is
    /// declared rather than assumed. Both fields take <c>omitted</c> rather than <c>redacted</c>, and for a
    /// different reason than a statement does: a credential half is sensitive in its entirety rather than a
    /// shape with values inside it, so a placeholder would preserve nothing while still asserting that an
    /// account was configured and how long its name was.
    /// </remarks>
    [Fact]
    public void WithholdingTheTransactionPasswordAlsoWithholdsItsUserName()
    {
        int pairs = 0;

        foreach (WorkflowDefinition definition in CharacterizationStore.Definitions)
        {
            IDictionary<object, object> capture = Mapping(definition.Root, "capturePhase");

            ImmutableArray<IDictionary<object, object>> redactions = Items(capture, "redaction");

            string[] fields = [.. redactions.Select(entry => Text(entry, "field"))];

            if (!fields.Contains("transactionDescriptor.logpass", StringComparer.Ordinal))
            {
                continue;
            }

            pairs++;

            Assert.Contains("transactionDescriptor.logid", fields, StringComparer.Ordinal);

            // AND BOTH ARE OMITTED, not merely present. A masked credential is still a disclosure of the
            // fact and the length.
            Assert.All(
                redactions.Where(entry =>
                    Text(entry, "field") is "transactionDescriptor.logpass"
                        or "transactionDescriptor.logid"),
                entry => Assert.Equal("omitted", Text(entry, "treatment"), StringComparer.Ordinal));
        }

        Assert.True(
            pairs >= 3,
            $"Only {pairs} workflows were found declaring the transaction password, which is fewer than "
                + "the three persistence workflows that carry a transaction descriptor.");
    }

    /// <summary>
    /// Every determinism mask in the store applies to BOTH halves of its pair.
    /// </summary>
    /// <remarks>
    /// <b>THE RULE AAP 0.6.7 STATES, AND THE ONE A ONE-SIDED MASK BREAKS SILENTLY.</b> A mask applied to
    /// the candidate alone does not neutralise variation - it CHANGES one side of the comparison, so the
    /// pair then differs for a reason that has nothing to do with behaviour and the difference looks like a
    /// parity failure. Asserted store-wide as well as per definition, because this is the property the
    /// whole store's comparability rests on and a single exception voids one pair completely.
    /// </remarks>
    [Fact]
    public void EveryDeterminismMaskAppliesToBothHalvesOfThePair()
    {
        int masks = 0;

        foreach (WorkflowDefinition definition in CharacterizationStore.Definitions)
        {
            foreach (IDictionary<object, object> mask in Items(definition.Root, "determinismMask"))
            {
                masks++;

                ImmutableArray<string> appliesTo =
                    [.. Strings(mask, "appliesTo").Order(StringComparer.Ordinal)];

                Assert.Equal((string[])["dotnet", "legacy"], appliesTo);
            }
        }

        // Every definition contributes at least one, so the total cannot be satisfied by one generous file.
        Assert.True(
            masks >= CharacterizationStore.Definitions.Length,
            $"The store declares {masks.ToString(CultureInfo.InvariantCulture)} determinism masks across "
                + $"{CharacterizationStore.Definitions.Length.ToString(CultureInfo.InvariantCulture)} "
                + "definitions, so at least one definition masks nothing.");
    }

    /// <summary>
    /// Every locator in the store resolves: the file it names exists, and every line it cites is inside
    /// that file.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>LOCATORS ARE RESOLVED, NOT PATTERN-MATCHED.</b> AAP 0.1.4 makes the legacy tree the only thing
    /// that can adjudicate a behavioural claim, so a locator is the load-bearing part of a definition: a
    /// citation pointing at a file that no longer exists, or at a line past the end of one, is a claim
    /// nobody can check. A pattern match would accept both.
    /// </para>
    /// <para>
    /// A file is read at most once and only its line count is taken. The tree is read-only (AAP 0.2.2.1) and
    /// reading is what an oracle is for.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryLocatorInTheStoreResolvesToARealLineOfARealFile()
    {
        int resolved = 0;

        foreach (WorkflowDefinition definition in CharacterizationStore.Definitions)
        {
            foreach (IDictionary<object, object> fixture in Items(definition.Root, "oracleFixtures"))
            {
                string path = Text(fixture, "path");

                int lines = RequireLineCount(definition.FileName, path);

                foreach (string reference in Strings(fixture, "locators"))
                {
                    foreach (Match match in LocatorReference.Matches(":" + reference))
                    {
                        resolved += AssertLinesInRange(definition.FileName, path, lines, match);
                    }
                }
            }

            foreach (string locator in ProseLocators(definition.Root))
            {
                string? current = null;
                int lines = 0;

                foreach (Match match in LocatorReference.Matches(locator))
                {
                    if (match.Groups["path"].Success)
                    {
                        current = match.Groups["path"].Value;
                        lines = RequireLineCount(definition.FileName, current);
                    }

                    Assert.NotNull(current);

                    resolved += AssertLinesInRange(definition.FileName, current, lines, match);
                }
            }
        }

        // A guard that resolved nothing would pass vacuously, so the count itself is asserted.
        Assert.True(
            resolved > 1000,
            $"Only {resolved.ToString(CultureInfo.InvariantCulture)} line references were resolved across "
                + "the store, which is far fewer than the committed definitions carry - the locator "
                + "extraction has stopped seeing them.");
    }

    // ==============================================================================================
    //  3 - THE STORE AGREES WITH WHAT IS ON DISK
    // ==============================================================================================

    /// <summary>
    /// Every workflow's execution status agrees with its recording directories, and no pair is half
    /// captured.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>AN EQUIVALENCE, WHICH IS WHAT MAKES IT A GATE RATHER THAN A DESCRIPTION.</b> Today no capture has
    /// been taken and every definition says <c>specified-not-executed</c>, so this asserts that the
    /// recording directories hold nothing but scaffolding. The day a capture lands, the same assertion
    /// fails until the definition's status is updated - and the schema's status enumeration has to grow a
    /// member for it, which is the reviewed change the store is meant to force.
    /// </para>
    /// <para>
    /// <b>AND THE PAIR RULE, FROM EITHER SIDE.</b> docs/PARITY.md 4.1: a recording whose identifier does not
    /// exist on the other side is not a comparison at all. Checking only the legacy half would leave a
    /// target-only capture unexamined, which is the half that a target-side test run produces by accident.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryWorkflowsRecordingsAgreeWithItsExecutionStatus()
    {
        foreach (WorkflowDefinition definition in CharacterizationStore.Definitions)
        {
            string legacy = CharacterizationStore.RecordingDirectory("legacy", definition.WorkflowId);
            string dotnet = CharacterizationStore.RecordingDirectory("dotnet", definition.WorkflowId);

            bool legacyCaptured = CharacterizationStore.CarriesARecording(legacy);
            bool dotnetCaptured = CharacterizationStore.CarriesARecording(dotnet);

            // THE PAIR RULE. Either both halves exist or neither does.
            Assert.Equal(legacyCaptured, dotnetCaptured);

            bool notExecuted = string.Equals(
                Text(definition.Root, "executionStatus"),
                "specified-not-executed",
                StringComparison.Ordinal);

            Assert.Equal(!notExecuted, legacyCaptured);
        }
    }

    /// <summary>
    /// The store's readme still states the capture rule against the renamed persistence volume, and every
    /// definition that touches storage relies on it.
    /// </summary>
    /// <remarks>
    /// AAP 0.3.2.3 renames the environment's <c>data-service-db</c> volume to <c>persistence-db</c> and
    /// requires the paired-capture rule be restated verbatim against the new name "so its intent survives
    /// the rename". The schema deliberately does not restate the rule, pointing at this readme instead - so
    /// if the readme lost the volume name, the rule would be documented against a volume that does not
    /// exist and nothing else in the repository would say otherwise.
    /// </remarks>
    [Fact]
    public void TheCaptureRuleIsStatedAgainstTheRenamedPersistenceVolume()
    {
        string readme = CharacterizationStore.ReadStoreReadme();

        Assert.Contains(
            CharacterizationStore.PersistenceVolumeName,
            readme,
            StringComparison.Ordinal);

        // THE PRE-RENAME NAME MAY APPEAR, BUT NEVER ALONE. The readme has to be able to say what the
        // environment's original name was in order to record the rename at all - forbidding the old name
        // outright would forbid documenting the change. What must not happen is the old name standing on its
        // own as though it were still current, so every line that mentions it must name the new one too.
        foreach (string line in readme.Split('\n'))
        {
            if (line.Contains("data-service-db", StringComparison.Ordinal))
            {
                Assert.Contains(
                    CharacterizationStore.PersistenceVolumeName,
                    line,
                    StringComparison.Ordinal);
            }
        }

        // Most definitions touch storage; the exemption is narrow, so a store where nothing applied would
        // mean the rule had quietly stopped governing anything.
        int applies = CharacterizationStore.Definitions.Count(static definition =>
            Flag(Mapping(definition.Root, "sharedVolume"), "applies"));

        Assert.True(
            applies > 0,
            "No definition declares that the shared-volume capture rule applies to it, so the rule now "
                + "governs nothing.");
    }

    /// <summary>
    /// Every related-workflow reference names a definition that is actually in the roster, and none names
    /// itself.
    /// </summary>
    /// <remarks>
    /// A dangling reference is how a rename goes unnoticed: the identifier is stable by contract precisely
    /// because renaming one orphans every pair captured under the old name, and a sibling still pointing at
    /// the old name is the only evidence that happened.
    /// </remarks>
    [Fact]
    public void EveryRelatedWorkflowReferenceNamesADefinitionInTheRoster()
    {
        HashSet<string> roster = [.. CharacterizationStore.WorkflowIds];

        foreach (WorkflowDefinition definition in CharacterizationStore.Definitions)
        {
            if (!definition.Root.ContainsKey("relatedWorkflows"))
            {
                continue;
            }

            ImmutableArray<string> related = [.. Strings(definition.Root, "relatedWorkflows")];

            Assert.NotEmpty(related);
            Assert.Equal(related.Length, related.Distinct(StringComparer.Ordinal).Count());

            Assert.DoesNotContain(definition.WorkflowId, related);

            foreach (string sibling in related)
            {
                Assert.True(
                    roster.Contains(sibling),
                    $"'{definition.FileName}' names the related workflow '{sibling}', which no definition "
                        + "in the roster declares.");
            }
        }
    }

    // ==============================================================================================
    //  PER-MEMBER ASSERTIONS
    // ==============================================================================================

    /// <summary>Asserts the oracle-fixture roster.</summary>
    /// <param name="fileName">The definition file, for diagnostics.</param>
    /// <param name="root">The definition root.</param>
    private static void AssertOracleFixtures(string fileName, IDictionary<object, object> root)
    {
        JsonElement schema = CharacterizationStore.SchemaProperty("oracleFixtures");
        ImmutableArray<string> roles =
            CharacterizationStore.SchemaEnum(schema.GetProperty("items").GetProperty("properties")
                .GetProperty("role"));
        Regex pathPattern = CharacterizationStore.SchemaPattern(
            schema.GetProperty("items").GetProperty("properties").GetProperty("path"));
        Regex locatorPattern = CharacterizationStore.SchemaPattern(
            schema.GetProperty("items").GetProperty("properties").GetProperty("locators")
                .GetProperty("items"));

        ImmutableArray<IDictionary<object, object>> fixtures = Items(root, "oracleFixtures");

        Assert.True(
            fixtures.Length >= schema.GetProperty("minItems").GetInt32(),
            $"'{fileName}' cites fewer oracle fixtures than the schema requires.");

        foreach (IDictionary<object, object> fixture in fixtures)
        {
            string path = Text(fixture, "path");

            Assert.Matches(pathPattern, path);
            Assert.Contains(Text(fixture, "role"), roles);

            if (!fixture.ContainsKey("locators"))
            {
                continue;
            }

            ImmutableArray<string> locators = [.. Strings(fixture, "locators")];

            Assert.NotEmpty(locators);
            Assert.Equal(locators.Length, locators.Distinct(StringComparer.Ordinal).Count());
            Assert.All(locators, locator => Assert.Matches(locatorPattern, locator));
        }
    }

    /// <summary>Asserts the seed phase, including its conditional requirement.</summary>
    /// <param name="fileName">The definition file, for diagnostics.</param>
    /// <param name="root">The definition root.</param>
    private static void AssertSeedPhase(string fileName, IDictionary<object, object> root)
    {
        IDictionary<object, object> seed = Mapping(root, "seedPhase");

        // Both constants are true, and they are the mechanism rather than a formality: a seed that ran per
        // capture would be a reseed, which voids the pair.
        Assert.True(Flag(seed, "runsOncePerPair"));
        Assert.True(Flag(seed, "neverBetweenCaptures"));

        ImmutableArray<IDictionary<object, object>> steps = Items(seed, "steps");

        if (steps.IsEmpty)
        {
            Assert.True(
                seed.ContainsKey("emptyReason") && Text(seed, "emptyReason").Length > 0,
                $"'{fileName}' declares no seeding steps and gives no emptyReason, so an omission is "
                    + "indistinguishable from a workflow that genuinely needs no starting state.");

            return;
        }

        Assert.All(steps, step => Assert.NotEmpty(Text(step, "action")));
    }

    /// <summary>Asserts the capture phase.</summary>
    /// <param name="fileName">The definition file, for diagnostics.</param>
    /// <param name="root">The definition root.</param>
    private static void AssertCapturePhase(string fileName, IDictionary<object, object> root)
    {
        IDictionary<object, object> capture = Mapping(root, "capturePhase");

        Assert.NotEmpty(Text(capture, "behaviourUnderTest"));

        ImmutableArray<IDictionary<object, object>> outputs = Items(capture, "observedOutputs");

        Assert.True(
            !outputs.IsEmpty,
            $"'{fileName}' records no observed output, and a capture that records nothing cannot be "
                + "compared - an empty comparison must never be reported as a pass.");

        Assert.All(
            outputs,
            output =>
            {
                Assert.NotEmpty(Text(output, "name"));
                Assert.NotEmpty(Text(output, "description"));
            });
    }

    /// <summary>Asserts the determinism mask's per-entry shape.</summary>
    /// <param name="fileName">The definition file, for diagnostics.</param>
    /// <param name="root">The definition root.</param>
    private static void AssertDeterminismMask(string fileName, IDictionary<object, object> root)
    {
        ImmutableArray<IDictionary<object, object>> masks = Items(root, "determinismMask");

        Assert.True(
            !masks.IsEmpty,
            $"'{fileName}' masks no seam at all. Repeatability is the Golden-Master technique's one hard "
                + "prerequisite, so a definition that masks nothing is asserting that its workflow has no "
                + "source of variation - which is a claim, not a default.");

        Assert.All(
            masks,
            mask =>
            {
                Assert.NotEmpty(Text(mask, "seam"));
                Assert.NotEmpty(Text(mask, "method"));
                Assert.NotEmpty(Text(mask, "reason"));
            });
    }

    /// <summary>Asserts the preserved-defect roster.</summary>
    /// <param name="fileName">The definition file, for diagnostics.</param>
    /// <param name="root">The definition root.</param>
    private static void AssertProvesDefects(string fileName, IDictionary<object, object> root)
    {
        ImmutableArray<IDictionary<object, object>> defects = Items(root, "provesDefects");

        Assert.True(
            !defects.IsEmpty,
            $"'{fileName}' proves no defect. G2 requires legacy quirks be replicated and documented, so a "
                + "pair that demonstrates none is not characterizing the thing the mandate is about.");

        Assert.All(
            defects,
            defect =>
            {
                Assert.NotEmpty(Text(defect, "defect"));
                Assert.NotEmpty(Text(defect, "locator"));
            });
    }

    /// <summary>Asserts the two optional rosters, whose enumerations are closed.</summary>
    /// <param name="fileName">The definition file, for diagnostics.</param>
    /// <param name="root">The definition root.</param>
    /// <remarks>
    /// The deferred roster's two enumerations are the deferral boundary in data form: the four capability
    /// areas and the four reserved routes AAP 0.4.4 declares. A definition that named a fifth would be
    /// claiming a destination the plan does not have.
    /// </remarks>
    private static void AssertOptionalRosters(string fileName, IDictionary<object, object> root)
    {
        if (root.ContainsKey("blockedBehaviours"))
        {
            JsonElement schema = CharacterizationStore.SchemaProperty("blockedBehaviours");
            ImmutableArray<string> dispositions = CharacterizationStore.SchemaEnum(
                schema.GetProperty("items").GetProperty("properties").GetProperty("disposition"));

            ImmutableArray<IDictionary<object, object>> blocked = Items(root, "blockedBehaviours");

            Assert.NotEmpty(blocked);

            Assert.All(
                blocked,
                entry =>
                {
                    Assert.NotEmpty(Text(entry, "behaviour"));
                    Assert.NotEmpty(Text(entry, "reason"));
                    Assert.Contains(Text(entry, "disposition"), dispositions);
                });
        }

        if (!root.ContainsKey("deferredCapabilities"))
        {
            return;
        }

        JsonElement deferredSchema = CharacterizationStore.SchemaProperty("deferredCapabilities");
        ImmutableArray<string> areas = CharacterizationStore.SchemaEnum(
            deferredSchema.GetProperty("items").GetProperty("properties").GetProperty("deferredTo"));
        ImmutableArray<string> routes = CharacterizationStore.SchemaEnum(
            deferredSchema.GetProperty("items").GetProperty("properties").GetProperty("reservedRoute"));

        ImmutableArray<IDictionary<object, object>> deferred = Items(root, "deferredCapabilities");

        Assert.True(
            !deferred.IsEmpty,
            $"'{fileName}' declares an empty deferredCapabilities roster, which claims a gap list rather "
                + "than stating one.");

        Assert.All(
            deferred,
            entry =>
            {
                Assert.NotEmpty(Text(entry, "capability"));
                Assert.Contains(Text(entry, "deferredTo"), areas);
                Assert.Contains(Text(entry, "reservedRoute"), routes);
            });
    }

    /// <summary>Asserts the shared-volume declaration and its conditional requirement.</summary>
    /// <param name="fileName">The definition file, for diagnostics.</param>
    /// <param name="root">The definition root.</param>
    private static void AssertSharedVolume(string fileName, IDictionary<object, object> root)
    {
        IDictionary<object, object> volume = Mapping(root, "sharedVolume");

        if (Flag(volume, "applies"))
        {
            return;
        }

        Assert.True(
            volume.ContainsKey("exemptionReason") && Text(volume, "exemptionReason").Length > 0,
            $"'{fileName}' claims exemption from the shared-volume capture rule and gives no reason. The "
                + "exemption is narrow - it holds only while the workflow reads and writes nothing - so an "
                + "unstated one is indistinguishable from a loophole.");
    }

    // ==============================================================================================
    //  READING HELPERS
    // ==============================================================================================

    /// <summary>Reads a required scalar as text.</summary>
    /// <param name="node">The mapping.</param>
    /// <param name="member">The member name.</param>
    /// <returns>The value, trimmed of nothing - the document's text verbatim.</returns>
    private static string Text(IDictionary<object, object> node, string member) =>
        node.TryGetValue(member, out object? value)
            ? Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
            : throw FailException.ForFailure($"The member '{member}' is absent.");

    /// <summary>Reads a required boolean.</summary>
    /// <param name="node">The mapping.</param>
    /// <param name="member">The member name.</param>
    /// <returns>The value.</returns>
    private static bool Flag(IDictionary<object, object> node, string member) =>
        node.TryGetValue(member, out object? value) && value is bool flag
            ? flag
            : throw FailException.ForFailure($"The member '{member}' is absent or is not a boolean.");

    /// <summary>Reads a required nested mapping.</summary>
    /// <param name="node">The mapping.</param>
    /// <param name="member">The member name.</param>
    /// <returns>The nested mapping.</returns>
    private static IDictionary<object, object> Mapping(IDictionary<object, object> node, string member) =>
        node.TryGetValue(member, out object? value) && value is IDictionary<object, object> mapping
            ? mapping
            : throw FailException.ForFailure($"The member '{member}' is absent or is not a mapping.");

    /// <summary>Reads a sequence of mappings, treating an absent member as empty.</summary>
    /// <param name="node">The mapping.</param>
    /// <param name="member">The member name.</param>
    /// <returns>The items.</returns>
    private static ImmutableArray<IDictionary<object, object>> Items(
        IDictionary<object, object> node,
        string member)
    {
        if (!node.TryGetValue(member, out object? value) || value is not IList<object> list)
        {
            return [];
        }

        ImmutableArray<IDictionary<object, object>>.Builder items =
            ImmutableArray.CreateBuilder<IDictionary<object, object>>();

        foreach (object item in list)
        {
            items.Add(
                item as IDictionary<object, object>
                ?? throw FailException.ForFailure(
                    $"An item of '{member}' is not a mapping, so its members cannot be validated."));
        }

        return items.ToImmutable();
    }

    /// <summary>Reads a sequence of scalars, treating an absent member as empty.</summary>
    /// <param name="node">The mapping.</param>
    /// <param name="member">The member name.</param>
    /// <returns>The values.</returns>
    private static ImmutableArray<string> Strings(IDictionary<object, object> node, string member)
    {
        if (!node.TryGetValue(member, out object? value) || value is not IList<object> list)
        {
            return [];
        }

        return [.. list.Select(static item =>
            Convert.ToString(item, CultureInfo.InvariantCulture) ?? string.Empty)];
    }

    /// <summary>
    /// Every prose locator in a definition: the seeding steps, the observed outputs and the proved defects.
    /// </summary>
    /// <param name="root">The definition root.</param>
    /// <returns>The locator strings.</returns>
    private static ImmutableArray<string> ProseLocators(IDictionary<object, object> root)
    {
        ImmutableArray<string>.Builder locators = ImmutableArray.CreateBuilder<string>();

        foreach (IDictionary<object, object> step in Items(Mapping(root, "seedPhase"), "steps"))
        {
            if (step.ContainsKey("locator"))
            {
                locators.Add(Text(step, "locator"));
            }
        }

        foreach (IDictionary<object, object> output in Items(
            Mapping(root, "capturePhase"),
            "observedOutputs"))
        {
            if (output.ContainsKey("locator"))
            {
                locators.Add(Text(output, "locator"));
            }
        }

        foreach (IDictionary<object, object> defect in Items(root, "provesDefects"))
        {
            locators.Add(Text(defect, "locator"));
        }

        return locators.ToImmutable();
    }

    /// <summary>Counts the lines of a cited file, failing when it does not exist.</summary>
    /// <param name="fileName">The citing definition, for diagnostics.</param>
    /// <param name="repositoryRelativePath">The cited path, with forward slashes.</param>
    /// <returns>The line count.</returns>
    private static int RequireLineCount(string fileName, string repositoryRelativePath)
    {
        if (LineCounts.TryGetValue(repositoryRelativePath, out int cached))
        {
            return cached;
        }

        string absolute = Path.Combine(
            EstateInventory.RepositoryRoot,
            repositoryRelativePath.Replace('/', Path.DirectorySeparatorChar));

        if (!File.Exists(absolute))
        {
            throw FailException.ForFailure(
                $"'{fileName}' cites '{repositoryRelativePath}', which does not exist. Every behavioural "
                    + "claim in this refactor is traceable to a locator, so a citation that resolves to "
                    + "nothing is a claim nobody can check.");
        }

        int lines = File.ReadAllLines(absolute).Length;

        LineCounts[repositoryRelativePath] = lines;

        return lines;
    }

    /// <summary>Asserts that every line a locator match names is inside the cited file.</summary>
    /// <param name="fileName">The citing definition, for diagnostics.</param>
    /// <param name="path">The cited path.</param>
    /// <param name="lines">The cited file's line count.</param>
    /// <param name="match">One locator match.</param>
    /// <returns>How many line references were checked.</returns>
    private static int AssertLinesInRange(string fileName, string path, int lines, Match match)
    {
        int checked_ = 0;
        int from = 0;

        foreach (string group in (string[])["from", "to", "also"])
        {
            if (!match.Groups[group].Success)
            {
                continue;
            }

            int line = int.Parse(match.Groups[group].Value, CultureInfo.InvariantCulture);

            checked_++;

            Assert.True(
                line >= 1 && line <= lines,
                $"'{fileName}' cites {path}:L{line.ToString(CultureInfo.InvariantCulture)}, but that file "
                    + $"has {lines.ToString(CultureInfo.InvariantCulture)} lines.");

            if (string.Equals(group, "from", StringComparison.Ordinal))
            {
                from = line;
            }
            else if (string.Equals(group, "to", StringComparison.Ordinal))
            {
                Assert.True(
                    line >= from,
                    $"'{fileName}' cites the inverted range {path}:L"
                        + from.ToString(CultureInfo.InvariantCulture) + "-L"
                        + line.ToString(CultureInfo.InvariantCulture) + ".");
            }
        }

        return checked_;
    }
}
