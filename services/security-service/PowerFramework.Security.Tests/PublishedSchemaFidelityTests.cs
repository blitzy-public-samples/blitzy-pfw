// --------------------------------------------------------------------------------------------------
// THE PUBLISHED DOCUMENT AGAINST THE AUTHORED CONTRACT, AND THE REFUSAL THAT ENFORCES IT
//
// WHAT WENT WRONG, STATED AS THE FACT ABOUT THE CODE IT WAS. This service serves a generated OpenAPI
// document from /openapi/v1.json, and a client generator consumes THAT rather than the authored
// shared/PowerFramework.Contracts/OpenApi/security.v1.yaml. The two had drifted in four ways at once,
// every one of them a structural constraint rather than a cosmetic difference:
//
//   * SIXTEEN request schemas carried no `required` list at all, where the contract lists one for each.
//   * EVERY schema was open, where the contract closes all thirty of its named object schemas - including
//     JsonWebKey, whose own description says closure is what makes the prohibition on publishing private
//     key material structural rather than advisory.
//   * ProblemDetails was closed, where the contract opens it explicitly, so the published schema
//     contradicted every error body this service writes.
//   * Every required member was typed as admitting null, and the payload-form enumeration listed a third,
//     null member - none of which any handler accepts.
//
// And the receiving side did not enforce closure at all: an undeclared member was silently discarded, so
// a caller that misspelled a member received the absent-member refusal for a member it believed it had
// sent. A malformed body answered 500, telling a caller this service was broken when its own request was.
//
// WHAT THESE TESTS ARE FOR. The first class compares the two documents member by member, so a future
// change to either one that breaks the agreement fails here rather than reaching a consumer. That is the
// only durable guard: the fidelity pass could be made to do nothing and every other test in this
// assembly would still pass, because nothing else in the suite reads the generated document's structure.
// --------------------------------------------------------------------------------------------------

using System.Net;
using System.Net.Mime;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using PowerFramework.Security.Configuration;
using PowerFramework.Security.Endpoints;
using PowerFramework.Shared.Kernel;

namespace PowerFramework.Security.Tests;

/// <summary>
/// The generated document's structural constraints, compared against the authored contract's.
/// </summary>
/// <remarks>
/// <para>
/// THE COMPARISON IS DRIVEN FROM THE AUTHORED DOCUMENT, NOT FROM A LIST IN THIS FILE. Every expectation
/// is read out of <c>security.v1.yaml</c> at run time, so a member added to a contract schema is compared
/// without this file changing - and a schema whose requiredness is edited in one document only fails here.
/// </para>
/// <para>
/// The authored document is read as text rather than through a parser, for the reason the sibling contract
/// reader in this assembly states: a YAML parser is not a permitted dependency of this project. Every
/// pattern below is anchored on the document's own indentation, which was read off the file.
/// </para>
/// </remarks>
public sealed class PublishedSchemaFidelityTests
{
    /// <summary>The route the generated document is served from.</summary>
    private const string DocumentRoute = "/openapi/v1.json";

    /// <summary>
    /// The five shapes this service publishes under a name the authored contract spells differently.
    /// </summary>
    /// <remarks>
    /// A RECORDED DIVERGENCE RATHER THAN A DEFECT, AND IT IS DATA HERE SO THAT IT IS VISIBLE. A schema name
    /// is not on the wire: it names a generated client's type, and both documents are internally consistent
    /// about the constraints that ARE on the wire, which is what the rest of this class checks. The C# names
    /// were chosen deliberately - the key-set shape's own declaration says it is named for the shape rather
    /// than for the class that returns it - so renaming the published identifiers would be a change to two
    /// artifacts to settle a difference no caller can observe.
    /// </remarks>
    private static readonly Dictionary<string, string> ContractSchemaNames = new(StringComparer.Ordinal)
    {
        ["TokenIssuanceRequestBody"] = "TokenRequest",
        ["TokenIssuanceResponse"] = "TokenResponse",
        ["JsonWebKeyDocument"] = "JsonWebKey",
        ["JsonWebKeySetDocument"] = "JsonWebKeySet",
        ["ProviderMetadataDocument"] = "ProviderMetadata",
        ["ServiceHealthCheck"] = "HealthCheckResult",
        ["ServiceHealthReport"] = "HealthReport",
    };

    /// <summary>
    /// Every published schema requires exactly the members the authored contract requires of it.
    /// </summary>
    /// <remarks>
    /// THE ROW THAT CLOSES SIXTEEN OF THE FOUR DRIFTS AT ONCE. It compares SETS in both directions, so a
    /// document that required too little - the original defect - and one that required too much both fail.
    /// The count assertion at the end is what stops the comparison going vacuous: a fidelity pass that
    /// stopped emitting schemas would otherwise satisfy an every-schema-agrees loop over nothing.
    /// </remarks>
    [Fact]
    public async Task EveryPublishedSchemaRequiresWhatTheAuthoredContractRequires()
    {
        using JsonDocument published = await ReadPublishedDocumentAsync();
        JsonElement schemas = Schemas(published);

        int compared = 0;

        foreach (JsonProperty schema in schemas.EnumerateObject())
        {
            string authoredName = AuthoredNameOf(schema.Name);
            IReadOnlyList<string> authored = AuthoredContract.RequiredMembers(authoredName);
            IReadOnlyList<string> live = RequiredMembers(schema.Value);

            Assert.Equal(
                authored.Order(StringComparer.Ordinal),
                live.Order(StringComparer.Ordinal));

            compared++;
        }

        Assert.Equal(35, compared);
    }

    /// <summary>
    /// Every object schema this service owns is closed, exactly as the authored contract closes it.
    /// </summary>
    /// <remarks>
    /// Closure is checked against the authored document rather than asserted outright, so the one shape the
    /// contract deliberately leaves open is not special-cased here - it is simply read as open, and the
    /// sibling row states why that matters.
    /// </remarks>
    [Fact]
    public async Task EveryObjectSchemaIsAsClosedAsTheAuthoredContractDeclaresIt()
    {
        using JsonDocument published = await ReadPublishedDocumentAsync();
        JsonElement schemas = Schemas(published);

        int closed = 0;
        int open = 0;

        foreach (JsonProperty schema in schemas.EnumerateObject())
        {
            bool authoredClosed = AuthoredContract.IsClosed(AuthoredNameOf(schema.Name));
            bool liveClosed =
                schema.Value.TryGetProperty("additionalProperties", out JsonElement additional) &&
                additional.ValueKind == JsonValueKind.False;

            Assert.Equal(authoredClosed, liveClosed);

            if (liveClosed)
            {
                closed++;
            }
            else
            {
                open++;
            }
        }

        Assert.Equal(33, closed);
        Assert.Equal(2, open);
    }

    /// <summary>
    /// The problem shape stays open, because every error body this service writes extends it.
    /// </summary>
    /// <remarks>
    /// STATED AS ITS OWN ROW BECAUSE IT IS THE ONE CASE WHERE FIDELITY MEANS THE OPPOSITE OF CLOSING. With
    /// any schema transformer registered the generator materializes every schema and publishes this one
    /// CLOSED, so leaving it alone is not sufficient - it has to be opened. A closed problem schema would
    /// forbid the retCode member that appears on every single failure this service produces, and the second
    /// half of this row proves that member is really there.
    /// </remarks>
    [Fact]
    public async Task TheProblemShapeIsPublishedOpenBecauseEveryErrorBodyExtendsIt()
    {
        using JsonDocument published = await ReadPublishedDocumentAsync();
        JsonElement problem = Schemas(published).GetProperty("ProblemDetails");

        bool closed =
            problem.TryGetProperty("additionalProperties", out JsonElement additional) &&
            additional.ValueKind == JsonValueKind.False;

        Assert.False(closed);
        Assert.False(problem.GetProperty("properties").TryGetProperty(
            ProblemResults.RetCodeExtensionMember,
            out _));

        using SecurityAppFactory factory = new();
        using HttpClient caller = factory.CreateClient();

        using HttpResponseMessage refused = await caller.GetAsync(
            new Uri("/v1/ping", UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);

        using JsonDocument body = JsonDocument.Parse(
            await refused.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        Assert.True(body.RootElement.TryGetProperty(ProblemResults.RetCodeExtensionMember, out _));
    }

    /// <summary>No required member is published as admitting null.</summary>
    /// <remarks>
    /// <para>
    /// THE TWO SHAPES ARE BOTH CHECKED BECAUSE THE GENERATOR PRODUCES BOTH AND THEY ARE FIXED IN DIFFERENT
    /// PASSES. A nullable scalar is published as a type union and is corrected while the schemas are built;
    /// a nullable reference to a named schema is published as a choice against a null branch and does not
    /// exist until the document is complete, so it is corrected afterwards. A test that checked only the
    /// union would pass against eight payload-form members still admitting null.
    /// </para>
    /// <para>
    /// It is true of every required member on the surface, not only the ones the request shapes declare,
    /// which is why it walks the whole document rather than a list.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task NoRequiredMemberIsPublishedAsAdmittingNull()
    {
        using JsonDocument published = await ReadPublishedDocumentAsync();

        int inspected = 0;

        foreach (JsonProperty schema in Schemas(published).EnumerateObject())
        {
            foreach (string memberName in RequiredMembers(schema.Value))
            {
                Assert.True(
                    schema.Value.GetProperty("properties").TryGetProperty(
                        memberName,
                        out JsonElement member),
                    $"'{schema.Name}' requires '{memberName}' but declares no such member.");

                Assert.DoesNotContain("null", DeclaredTypes(member), StringComparer.Ordinal);

                foreach (JsonElement branch in Choices(member))
                {
                    Assert.DoesNotContain("null", DeclaredTypes(branch), StringComparer.Ordinal);
                }

                inspected++;
            }
        }

        // Thirty-two of the thirty-five published schemas declare a required list, and the members across them
        // come to seventy-nine. Asserted so that a document publishing no requiredness at all - the original
        // defect, which left sixteen request shapes with no list - cannot pass the loop by inspecting nothing.
        Assert.Equal(79, inspected);
    }

    /// <summary>
    /// The payload-form enumeration publishes exactly the two members the contract lists.
    /// </summary>
    /// <remarks>
    /// The generator adds a third, null member because every member referencing the enumeration is declared
    /// nullable. Every handler refuses a null selector with its own rejection arm, so publishing it stated
    /// something the service does not do - and it is the one enumeration on this surface published as a
    /// named schema, so it is checked by name.
    /// </remarks>
    [Fact]
    public async Task ThePayloadFormEnumerationPublishesExactlyTheContractsTwoMembers()
    {
        using JsonDocument published = await ReadPublishedDocumentAsync();

        string[] members =
        [
            .. Schemas(published)
                .GetProperty("PayloadForm")
                .GetProperty("enum")
                .EnumerateArray()
                .Select(static member => member.ValueKind == JsonValueKind.Null
                    ? "null"
                    : member.GetString() ?? string.Empty),
        ];

        Assert.Equal(["STRING", "BLOB"], members);
    }

    /// <summary>Reads the document this service publishes.</summary>
    /// <returns>The parsed document.</returns>
    /// <remarks>
    /// THE DOCUMENT IS BEHIND THE AUTHENTICATION BOUNDARY, which is why this presents a credential. This
    /// service exempts only its readiness route and the two anonymous discovery routes, so an unauthenticated
    /// read of the document answers 401 - a fact worth stating here because a probe that missed it would look
    /// like an empty document rather than a refusal.
    /// </remarks>
    private static async Task<JsonDocument> ReadPublishedDocumentAsync()
    {
        await using IssuanceHostFactory factory = new();
        using HttpClient caller = factory.CreateAuthenticatedClient();

        using HttpResponseMessage response = await caller.GetAsync(
            new Uri(DocumentRoute, UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>The document's schema map.</summary>
    /// <param name="document">The published document.</param>
    /// <returns>The <c>components/schemas</c> element.</returns>
    private static JsonElement Schemas(JsonDocument document) =>
        document.RootElement.GetProperty("components").GetProperty("schemas");

    /// <summary>The authored contract's name for a published schema.</summary>
    /// <param name="publishedName">The name the generated document uses.</param>
    /// <returns>The authored name, which is the same one unless it is a recorded divergence.</returns>
    private static string AuthoredNameOf(string publishedName) =>
        ContractSchemaNames.TryGetValue(publishedName, out string? authored) ? authored : publishedName;

    /// <summary>One schema's required member list, or an empty list when it declares none.</summary>
    /// <param name="schema">The schema element.</param>
    /// <returns>The member names.</returns>
    private static IReadOnlyList<string> RequiredMembers(JsonElement schema) =>
        schema.TryGetProperty("required", out JsonElement required)
            ? [.. required.EnumerateArray().Select(static member => member.GetString() ?? string.Empty)]
            : [];

    /// <summary>One member's declared types, whether declared singly or as a union.</summary>
    /// <param name="member">The member's schema element.</param>
    /// <returns>The type names, empty when the member declares none.</returns>
    private static IReadOnlyList<string> DeclaredTypes(JsonElement member)
    {
        if (!member.TryGetProperty("type", out JsonElement type))
        {
            return [];
        }

        return type.ValueKind == JsonValueKind.Array
            ? [.. type.EnumerateArray().Select(static entry => entry.GetString() ?? string.Empty)]
            : [type.GetString() ?? string.Empty];
    }

    /// <summary>One member's choice branches, across both spellings of a choice.</summary>
    /// <param name="member">The member's schema element.</param>
    /// <returns>The branch elements.</returns>
    private static IEnumerable<JsonElement> Choices(JsonElement member)
    {
        foreach (string keyword in (string[])["oneOf", "anyOf"])
        {
            if (member.TryGetProperty(keyword, out JsonElement branches))
            {
                foreach (JsonElement branch in branches.EnumerateArray())
                {
                    yield return branch;
                }
            }
        }
    }
}

/// <summary>
/// The authored contract's structural declarations, read as text.
/// </summary>
/// <remarks>
/// Scanning rules read off the document itself: a schema key sits at four spaces under <c>schemas:</c>, and
/// its own <c>required</c> and <c>additionalProperties</c> declarations sit at six. Every required list in
/// the document is written in flow style on one line and no <c>required</c> key appears deeper than six
/// spaces, both verified against the file, so the scan is exact without a parser.
/// </remarks>
internal static class AuthoredContract
{
    /// <summary>The declarations, read once.</summary>
    private static readonly Dictionary<string, (List<string> Required, bool Closed)> Declarations = Read();

    /// <summary>The members the contract requires of one schema.</summary>
    /// <param name="schemaName">The authored schema name.</param>
    /// <returns>The required member names, empty when the schema requires none.</returns>
    internal static IReadOnlyList<string> RequiredMembers(string schemaName)
    {
        Assert.True(
            Declarations.ContainsKey(schemaName),
            $"The authored contract declares no schema '{schemaName}', so the generated document publishes "
            + "a shape the contract does not describe.");

        return Declarations[schemaName].Required;
    }

    /// <summary>Whether the contract closes one schema.</summary>
    /// <param name="schemaName">The authored schema name.</param>
    /// <returns><see langword="true"/> when the contract sets additionalProperties to false.</returns>
    internal static bool IsClosed(string schemaName)
    {
        Assert.True(Declarations.ContainsKey(schemaName), $"No authored schema '{schemaName}'.");

        return Declarations[schemaName].Closed;
    }

    /// <summary>Scans the authored document.</summary>
    /// <returns>One entry per authored schema.</returns>
    private static Dictionary<string, (List<string> Required, bool Closed)> Read()
    {
        Dictionary<string, (List<string>, bool)> declarations = new(StringComparer.Ordinal);
        string[] lines = ContractDocument.Text.Split('\n');

        int start = Array.FindIndex(lines, static line => line.TrimEnd('\r') == "  schemas:");
        Assert.True(start >= 0, "The authored document declares no schemas section.");

        string? current = null;

        for (int index = start + 1; index < lines.Length; index++)
        {
            string line = lines[index].TrimEnd('\r');

            if (line.Length > 0 && !line.StartsWith("    ", StringComparison.Ordinal))
            {
                break;
            }

            if (IsSchemaKey(line, out string? name) && name is not null)
            {
                current = name;
                declarations[current] = ([], false);
                continue;
            }

            if (current is null)
            {
                continue;
            }

            const string RequiredPrefix = "      required: [";

            if (line.StartsWith(RequiredPrefix, StringComparison.Ordinal) &&
                line.EndsWith(']'))
            {
                string body = line[RequiredPrefix.Length..^1];

                declarations[current] = (
                    [.. body.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)],
                    declarations[current].Item2);
            }

            if (line == "      additionalProperties: false")
            {
                declarations[current] = (declarations[current].Item1, true);
            }
        }

        Assert.Equal(49, declarations.Count);

        return declarations.ToDictionary(
            static entry => entry.Key,
            static entry => (entry.Value.Item1, entry.Value.Item2),
            StringComparer.Ordinal);
    }

    /// <summary>Recognises a schema key, which sits at exactly four spaces.</summary>
    /// <param name="line">The line to classify.</param>
    /// <param name="name">The schema name when the line is a key.</param>
    /// <returns><see langword="true"/> when the line declares a schema.</returns>
    private static bool IsSchemaKey(string line, out string? name)
    {
        name = null;

        if (!line.StartsWith("    ", StringComparison.Ordinal) ||
            line.StartsWith("     ", StringComparison.Ordinal) ||
            !line.EndsWith(':'))
        {
            return false;
        }

        string candidate = line[4..^1];

        if (candidate.Length == 0 || !candidate.All(static character => char.IsLetterOrDigit(character) || character == '_'))
        {
            return false;
        }

        name = candidate;
        return true;
    }
}

/// <summary>
/// The fidelity pass's selection rules, exercised directly.
/// </summary>
/// <remarks>
/// The document-level rows above prove the OUTCOME on the real surface. These prove the RULES, including
/// the arms no shape on this surface reaches today - a schema with no members, a member typed as null and
/// nothing else - which would otherwise be unexercised branches whose behaviour nobody had checked.
/// </remarks>
public sealed class SchemaFidelitySelectionTests
{
    /// <summary>A shape this service owns is closed.</summary>
    [Fact]
    public void AShapeThisServiceOwnsIsClosed()
    {
        OpenApiSchema schema = ObjectSchema();

        Assert.Equal(SchemaFidelityOutcome.Closed, PublishedSchemaFidelity.Restore(schema, typeof(HashRequest)));
        Assert.False(schema.AdditionalPropertiesAllowed);
    }

    /// <summary>A shape this service does not own is opened rather than left alone.</summary>
    /// <remarks>
    /// THE ASSERTION IS THAT IT IS ACTIVELY OPENED, not merely that it is not closed. A materialized schema
    /// arrives closed, so a rule that skipped a foreign shape would publish the contradiction this row
    /// exists to prevent - and both a skip and an open leave the same value visible unless the outcome is
    /// checked too.
    /// </remarks>
    [Fact]
    public void AShapeThisServiceDoesNotOwnIsOpened()
    {
        OpenApiSchema schema = ObjectSchema();
        schema.AdditionalPropertiesAllowed = false;

        Assert.Equal(
            SchemaFidelityOutcome.Opened,
            PublishedSchemaFidelity.Restore(schema, typeof(Microsoft.AspNetCore.Mvc.ProblemDetails)));

        Assert.True(schema.AdditionalPropertiesAllowed);
    }

    /// <summary>An enumeration this service owns loses its null member.</summary>
    /// <param name="nullable">Whether to present the type as the nullable wrapper a member declares.</param>
    /// <remarks>
    /// THE NULLABLE ROW IS THE ONE THAT MATTERED. A member declared <c>PayloadForm?</c> reaches the pass as
    /// the nullable wrapper, whose IsEnum is false and whose assembly is the runtime's, so the rule silently
    /// did nothing until the wrapper was unwrapped. Both spellings are driven here so that regressing the
    /// unwrap fails rather than passing on the unwrapped row alone.
    /// </remarks>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnEnumerationThisServiceOwnsLosesItsNullMember(bool nullable)
    {
        // The generator's null member is a null ENTRY in the member list rather than a node of kind null,
        // so it is placed the same way here - an assertion against a shape the generator never produces
        // would prove nothing about the surface.
        List<JsonNode> members = ["STRING", "BLOB"];
        members.Add(null!);

        OpenApiSchema schema = new() { Enum = members };

        Type declared = nullable ? typeof(PayloadForm?) : typeof(PayloadForm);

        Assert.Equal(SchemaFidelityOutcome.EnumNarrowed, PublishedSchemaFidelity.Restore(schema, declared));
        Assert.Equal(2, schema.Enum!.Count);
        Assert.DoesNotContain(schema.Enum, static member => member is null);
    }

    /// <summary>An enumeration with no null member is left exactly as generated.</summary>
    [Fact]
    public void AnEnumerationWithoutANullMemberIsUntouched()
    {
        OpenApiSchema schema = new()
        {
            Enum = ["STRING", "BLOB"],
        };

        Assert.Equal(
            SchemaFidelityOutcome.Untouched,
            PublishedSchemaFidelity.Restore(schema, typeof(PayloadForm)));

        Assert.Equal(2, schema.Enum!.Count);
    }

    /// <summary>
    /// A shape generated with no members is left open, because closing it would forbid every member.
    /// </summary>
    [Fact]
    public void AnObjectShapeWithNoMembersIsLeftOpen()
    {
        OpenApiSchema schema = new() { Type = JsonSchemaType.Object };

        Assert.Equal(
            SchemaFidelityOutcome.Untouched,
            PublishedSchemaFidelity.Restore(schema, typeof(HashRequest)));

        Assert.True(schema.AdditionalPropertiesAllowed);
    }

    /// <summary>A required member's null type is cleared; an unrequired member's is not.</summary>
    [Fact]
    public void OnlyARequiredMemberLosesItsNullType()
    {
        OpenApiSchema schema = new()
        {
            Type = JsonSchemaType.Object,
            Required = new HashSet<string>(StringComparer.Ordinal) { "required" },
            Properties = new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal)
            {
                ["required"] = new OpenApiSchema { Type = JsonSchemaType.Null | JsonSchemaType.String },
                ["optional"] = new OpenApiSchema { Type = JsonSchemaType.Null | JsonSchemaType.String },
            },
        };

        PublishedSchemaFidelity.Restore(schema, typeof(HashRequest));

        Assert.Equal(JsonSchemaType.String, schema.Properties["required"].Type);
        Assert.Equal(JsonSchemaType.Null | JsonSchemaType.String, schema.Properties["optional"].Type);
    }

    /// <summary>A member typed as null and nothing else keeps its type.</summary>
    /// <remarks>
    /// Clearing the flag would leave an empty type, which admits EVERYTHING - the opposite of the rule's
    /// purpose. No member on this surface is declared that way; the guard is checked because a silent
    /// widening is the worst outcome a narrowing rule can produce.
    /// </remarks>
    [Fact]
    public void AMemberTypedOnlyAsNullKeepsItsType()
    {
        OpenApiSchema schema = new()
        {
            Type = JsonSchemaType.Object,
            Required = new HashSet<string>(StringComparer.Ordinal) { "member" },
            Properties = new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal)
            {
                ["member"] = new OpenApiSchema { Type = JsonSchemaType.Null },
            },
        };

        PublishedSchemaFidelity.Restore(schema, typeof(HashRequest));

        Assert.Equal(JsonSchemaType.Null, schema.Properties["member"].Type);
    }

    /// <summary>The document pass collapses a required member's null choice onto the reference.</summary>
    [Fact]
    public void TheDocumentPassCollapsesARequiredNullChoiceOntoTheReference()
    {
        OpenApiSchemaReference reference = new("PayloadForm");

        OpenApiDocument document = DocumentWith(
            required: "member",
            member: new OpenApiSchema
            {
                OneOf = [new OpenApiSchema { Type = JsonSchemaType.Null }, reference],
            });

        Assert.Equal(1, PublishedSchemaFidelity.RemoveNullChoices(document));

        IOpenApiSchema published = ((OpenApiSchema)document.Components!.Schemas!["Shape"]).Properties!["member"];

        Assert.Same(reference, published);
    }

    /// <summary>A wrapper carrying detail of its own is trimmed rather than replaced.</summary>
    /// <remarks>
    /// A description on the wrapper is authored information, and replacing the member with its reference
    /// would discard it. No member on this surface carries one today, so this row is what makes the arm
    /// something other than untested code.
    /// </remarks>
    [Fact]
    public void AWrapperCarryingItsOwnDetailIsTrimmedRatherThanReplaced()
    {
        OpenApiSchemaReference reference = new("PayloadForm");

        OpenApiDocument document = DocumentWith(
            required: "member",
            member: new OpenApiSchema
            {
                Description = "Authored detail.",
                OneOf = [new OpenApiSchema { Type = JsonSchemaType.Null }, reference],
            });

        Assert.Equal(1, PublishedSchemaFidelity.RemoveNullChoices(document));

        OpenApiSchema published =
            (OpenApiSchema)((OpenApiSchema)document.Components!.Schemas!["Shape"]).Properties!["member"];

        Assert.Equal("Authored detail.", published.Description);
        Assert.Same(reference, Assert.Single(published.OneOf!));
    }

    /// <summary>A member the schema does not require keeps its null choice.</summary>
    [Fact]
    public void AnUnrequiredMemberKeepsItsNullChoice()
    {
        OpenApiDocument document = DocumentWith(
            required: "other",
            member: new OpenApiSchema
            {
                OneOf = [new OpenApiSchema { Type = JsonSchemaType.Null }, new OpenApiSchemaReference("PayloadForm")],
            });

        Assert.Equal(0, PublishedSchemaFidelity.RemoveNullChoices(document));
    }

    /// <summary>A document with no schemas is handled rather than faulting.</summary>
    [Fact]
    public void ADocumentWithNoSchemasChangesNothing() =>
        Assert.Equal(0, PublishedSchemaFidelity.RemoveNullChoices(new OpenApiDocument()));

    /// <summary>An object schema carrying one member, which the closure rules apply to.</summary>
    /// <returns>The schema.</returns>
    private static OpenApiSchema ObjectSchema() => new()
    {
        Type = JsonSchemaType.Object,
        Properties = new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal)
        {
            ["member"] = new OpenApiSchema { Type = JsonSchemaType.String },
        },
    };

    /// <summary>A one-schema document, for the document pass's rows.</summary>
    /// <param name="required">The member name the schema requires.</param>
    /// <param name="member">The member's sub-schema, always registered under the name "member".</param>
    /// <returns>The document.</returns>
    private static OpenApiDocument DocumentWith(string required, OpenApiSchema member) => new()
    {
        Components = new OpenApiComponents
        {
            Schemas = new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal)
            {
                ["Shape"] = new OpenApiSchema
                {
                    Type = JsonSchemaType.Object,
                    Required = new HashSet<string>(StringComparer.Ordinal) { required },
                    Properties = new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal)
                    {
                        ["member"] = member,
                    },
                },
            },
        },
    };
}

/// <summary>
/// The receiving half of <c>additionalProperties: false</c>, and the answer a caller gets.
/// </summary>
/// <remarks>
/// A schema that forbids a member is a statement about what the SERVICE accepts, and a document-validating
/// client is only one of the two parties bound by it. These rows drive the other party.
/// </remarks>
public sealed class MalformedRequestBodyTests
{
    /// <summary>A crypto route whose request shape declares members, used to drive the body rules.</summary>
    private const string HashRoute = "/v1/crypto/hash";

    /// <summary>The scope the cryptographic family requires of a caller.</summary>
    private const string CryptographicScope = "security.crypto";

    /// <summary>A body every member of which the shape declares.</summary>
    private const string WellFormedBody = """{"data":"abc","payloadForm":"STRING","hashType":2}""";

    /// <summary>Only a fault about the request itself is claimed.</summary>
    /// <param name="statusCode">The status the framework's bad-request fault carries.</param>
    /// <param name="claimed">Whether the refusal claims it.</param>
    /// <remarks>
    /// A bad-request fault also carries 413 for a body over the configured limit and 408 for a client that
    /// stopped sending. Answering either with an argument fault would misdescribe it, so the status is
    /// checked rather than the exception type alone.
    /// </remarks>
    [Theory]
    [InlineData(StatusCodes.Status400BadRequest, true)]
    [InlineData(StatusCodes.Status413PayloadTooLarge, false)]
    [InlineData(StatusCodes.Status408RequestTimeout, false)]
    public void OnlyTheBadRequestArmOfABadRequestFaultIsClaimed(int statusCode, bool claimed) =>
        Assert.Equal(
            claimed,
            MalformedRequestBody.Describes(new BadHttpRequestException("refused", statusCode)));

    /// <summary>A serializer fault raised outside the binder's wrapping is claimed too.</summary>
    [Fact]
    public void ASerializerFaultIsClaimed() =>
        Assert.True(MalformedRequestBody.Describes(new JsonException("unreadable")));

    /// <summary>Nothing else is claimed, so the unhandled-fault path is unchanged.</summary>
    [Fact]
    public void NoOtherFaultIsClaimed() =>
        Assert.False(MalformedRequestBody.Describes(new InvalidOperationException("a server fault")));

    /// <summary>
    /// A body carrying a member the shape does not declare is refused as a client error.
    /// </summary>
    /// <remarks>
    /// THE STATUS IS ASSERTED AS 400 AND NOT MERELY AS A FAILURE, because the defect this row closes is a
    /// 500: the serializer's refusal escaping as an unhandled fault, telling a caller its own request was
    /// fine and this service is broken.
    /// </remarks>
    [Fact]
    public async Task AnUndeclaredMemberIsRefusedAsAClientError()
    {
        using HttpResponseMessage refused = await PostAsync(
            """{"data":"abc","payloadForm":"STRING","hashType":2,"undeclared":1}""");

        await AssertBodyRefusalAsync(refused);
    }

    /// <summary>A body that is not well-formed JSON is refused the same way.</summary>
    [Fact]
    public async Task ABodyThatIsNotWellFormedJsonIsRefusedTheSameWay()
    {
        using HttpResponseMessage refused = await PostAsync("""{"data":"abc",""");

        await AssertBodyRefusalAsync(refused);
    }

    /// <summary>The refusal echoes no part of the body it refused.</summary>
    /// <remarks>
    /// ON THIS SURFACE THE BODY IS PLAINTEXT, CIPHERTEXT OR A KEY REFERENCE. A serializer message quotes the
    /// offending fragment and its path names the member the caller sent, so a refusal built from either would
    /// publish request content - which is exactly the commitment the sibling absent-member refusal makes and
    /// states in the same terms. The distinctive values below appear nowhere in a conforming refusal.
    /// </remarks>
    [Fact]
    public async Task TheRefusalEchoesNoPartOfTheBody()
    {
        using HttpResponseMessage refused = await PostAsync(
            """{"data":"S3CR3T-PLAINTEXT","payloadForm":"STRING","hashType":2,"smuggled":"K3Y-M4TERIAL"}""");

        string body = await refused.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.DoesNotContain("S3CR3T-PLAINTEXT", body, StringComparison.Ordinal);
        Assert.DoesNotContain("K3Y-M4TERIAL", body, StringComparison.Ordinal);
        Assert.DoesNotContain("smuggled", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// An absent member still reaches its handler and is still named in the wire spelling.
    /// </summary>
    /// <remarks>
    /// THE NON-DISPLACEMENT GUARANTEE, AND IT IS THE ROW THAT PROTECTS THE DESIGN THE REQUEST SHAPES WERE
    /// BUILT FOR. Their members are declared nullable on purpose so that an ABSENT member reaches the handler
    /// and is refused with a detail naming it, rather than being refused by the binder with a message that
    /// names nothing useful. Absent and present-but-unknown are disjoint conditions; a refusal that had
    /// collapsed them would pass every row above while destroying that.
    /// </remarks>
    [Fact]
    public async Task AnAbsentMemberStillReachesItsHandlerAndIsNamed()
    {
        using HttpResponseMessage refused = await PostAsync("""{"payloadForm":"STRING","hashType":2}""");

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);

        using JsonDocument body = JsonDocument.Parse(
            await refused.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        string detail = body.RootElement.GetProperty("detail").GetString() ?? string.Empty;

        Assert.Contains("data", detail, StringComparison.Ordinal);
        Assert.DoesNotContain(MalformedRequestBody.RefusalDetail, detail, StringComparison.Ordinal);
    }

    /// <summary>
    /// The framework setting the refusal depends on is pinned rather than inherited from the environment.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE ROW THAT EXISTS BECAUSE EVERY OTHER ROW IN THIS CLASS IS BLIND TO THE DIFFERENCE.
    /// <c>RouteHandlerOptions.ThrowOnBadRequest</c> defaults to TRUE in Development and FALSE elsewhere, and
    /// a test host runs Development. Removing the pin was measured to leave every one of the sixteen hundred
    /// tests in this assembly passing, while a production deployment would answer a malformed body with the
    /// framework's own bodiless 400 and let the status-code-pages middleware fill in a generic body - a
    /// different answer to the same request from the one the rows above assert, and one carrying none of the
    /// published detail.
    /// </para>
    /// <para>
    /// WHAT THIS ROW DOES AND DOES NOT CATCH, STATED PLAINLY SO NOBODY READS MORE INTO IT. It catches the
    /// setting being pinned to FALSE, which is the change that would break the refusal outright. It does NOT
    /// catch the pin being DELETED, because the Development default is already true and no assertion made
    /// from inside a Development host can tell a pinned true from an inherited one.
    /// </para>
    /// <para>
    /// The stronger row would re-host this service for the other environment and require the identical
    /// answer. That was built and abandoned rather than left half-working: body binding runs after
    /// authorization, so the request needs a credential, and this fixture's Development-only settings
    /// establish the issuer, the audience roster, the inbound audience and the grant matrix that inbound
    /// validation reads - so a re-hosted Production server refuses the credential with a 401 before the rule
    /// under test is reached. Pinning every one of those on the factory to make the two hosts agree would
    /// turn the row into a measurement of the fixture's configuration plumbing rather than of the service.
    /// The gap is recorded instead of papered over.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheBadRequestSettingIsPinnedRatherThanInheritedFromTheEnvironment()
    {
        using SecurityAppFactory factory = new();

        RouteHandlerOptions options = factory.Services.GetRequiredService<IOptions<RouteHandlerOptions>>().Value;

        Assert.True(options.ThrowOnBadRequest);
    }

    /// <summary>A body every member of which is declared still succeeds.</summary>
    /// <remarks>
    /// The row that keeps the rules above from being satisfied by a service that refuses everything.
    /// </remarks>
    [Fact]
    public async Task AWellFormedBodyStillSucceeds()
    {
        using HttpResponseMessage answered = await PostAsync(WellFormedBody);

        Assert.Equal(HttpStatusCode.OK, answered.StatusCode);

        using JsonDocument body = JsonDocument.Parse(
            await answered.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        Assert.False(string.IsNullOrEmpty(body.RootElement.GetProperty("digest").GetString()));
    }

    /// <summary>Asserts the published refusal shape for a body this service could not read.</summary>
    /// <param name="refused">The response.</param>
    private static async Task AssertBodyRefusalAsync(HttpResponseMessage refused)
    {
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal("application/problem+json", refused.Content.Headers.ContentType?.MediaType);

        using JsonDocument body = JsonDocument.Parse(
            await refused.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        Assert.Equal(
            StatusCodes.Status400BadRequest,
            body.RootElement.GetProperty("status").GetInt32());

        Assert.Equal(
            RetCode.E_INVALID_ARGUMENT,
            body.RootElement.GetProperty(ProblemResults.RetCodeExtensionMember).GetInt64());

        Assert.Equal(
            MalformedRequestBody.RefusalDetail,
            body.RootElement.GetProperty("detail").GetString());
    }

    /// <summary>Posts a body to the digest route as a caller the deployed grant matrix authorises.</summary>
    /// <param name="json">The body, offered as JSON whether or not it is well formed.</param>
    /// <returns>The response.</returns>
    private static async Task<HttpResponseMessage> PostAsync(string json)
    {
        using SecurityAppFactory factory = new();

        string audience = factory.ResolveInboundAudience();

        using HttpClient caller = factory.CreateAuthenticatedClient(
            PermittedIdentity(factory.ResolveSecurityOptions(), audience),
            audience,
            [CryptographicScope]);

        using StringContent content = new(json, Encoding.UTF8, MediaTypeNames.Application.Json);

        return await caller.PostAsync(
            new Uri(HashRoute, UriKind.Relative),
            content,
            TestContext.Current.CancellationToken);
    }

    /// <summary>An identity the deployed grant matrix authorises for the cryptographic scope.</summary>
    /// <param name="configured">The deployed options.</param>
    /// <param name="audience">This service's own inbound audience.</param>
    /// <returns>The identity.</returns>
    private static string PermittedIdentity(SecurityOptions configured, string audience)
    {
        // BOTH CONFIGURED SHAPES, BECAUSE THE ISSUER ENFORCES THEIR UNION. Reading only the nested one
        // reported the deployment as granting nothing the moment its matrix moved to the flat shape - a
        // statement about the settings file's authoring style rather than about a permission.
        foreach ((string caller, string granted, IReadOnlyList<string> scopes)
            in IssuanceFixture.EffectiveGrants(configured))
        {
            if (!string.Equals(granted, audience, StringComparison.Ordinal))
            {
                continue;
            }

            if (scopes.Any(scope => string.Equals(scope, CryptographicScope, StringComparison.Ordinal)))
            {
                return caller;
            }
        }

        Assert.Fail(
            "The deployed grant matrix authorises no caller for the cryptographic scope on this service's "
            + "own inbound audience, so these rows cannot reach a handler. No configured value is echoed.");

        return string.Empty;
    }
}
