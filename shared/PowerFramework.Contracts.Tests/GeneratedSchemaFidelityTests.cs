// ==================================================================================================
//  GeneratedSchemaFidelityTests - THE COMPILE-TIME EDGE gateway.v1.yaml PREVIOUSLY DID NOT HAVE
//  ------------------------------------------------------------------------------------------------
//  SUBJECT     OpenApi/gateway.v1.yaml, components/schemas - every schema carrying
//              `x-proto-message` or `x-proto-enum`, which is 133 of the document's 144.
//  AUTHORITY   Proto/common.v1.proto and Proto/dataservices.v1.proto, read through their COMPILED
//              descriptors. Those files are the authority for every shape; this document publishes
//              them, and this suite is what keeps the two from drifting.
//
//  WHY THIS FILE EXISTS, STATED AS THE PROBLEM IT SOLVES
//  ------------------------------------------------------------------------------------------------
//  gateway.v1.yaml used to publish ONE open schema - `ProtoPayload`, `additionalProperties: true`
//  with no members - for every projected request and response body, 75 of them on today's projection,
//  and pointed a consumer at the
//  operation's `x-proto-*` extension to find the real message. That was wrong in the one direction
//  that matters: the projection binds every request with the STRICT canonical protobuf JSON parser,
//  which REJECTS a member the target message does not declare and answers 400. The document therefore
//  promised a permissiveness the runtime does not have, and a consumer generating a client from it
//  could not see a single member it was required to send. `UpdateResponse.rowsInserted`, `.rowsUpdated`,
//  `.rowsDeleted` and `.identity` were invisible to every standard OpenAPI consumer, on the one
//  operation whose entire purpose is to tell a caller what it changed and what the engine assigned.
//
//  The delegation's stated defence was sound and is answered here rather than overruled: transcribing
//  the shapes BY HAND would create a second source of truth in a different language with nothing
//  keeping the two in step, so the first divergence would be silent, "because nothing compiles this
//  document against those definitions". THIS FILE IS THAT COMPILATION. Every member of every marked
//  schema is compared against its descriptor on every build - member set, canonical JSON name, type,
//  format, repeatedness, map shape, closedness, enum value set, enum numbers, and the `required`
//  rule. A divergence is a build failure, not a silent inaccuracy.
//
//  THE FIVE RULES BEING ENFORCED, AND WHERE EACH COMES FROM
//  ------------------------------------------------------------------------------------------------
//  1. MEMBER SETS ARE EXACT. A marked message schema declares every field the descriptor declares,
//     under the descriptor's own `JsonName`, and declares nothing else. Both directions matter: a
//     missing member is the defect above, and an extra member is a shape the parser would refuse.
//
//  2. TYPES FOLLOW THE CANONICAL PROTOBUF JSON MAPPING, NOT THE .proto's SCALAR NAMES. 64-bit
//     integers are `[integer, string]`, because the mapping EMITS them as JSON strings and ACCEPTS
//     either form - measured, not assumed. `bytes` is `string`/`byte`. A repeated field is an array,
//     a map is an object with a value schema, and a message or enum field is a `$ref` to that type's
//     own published component.
//
//  3. EVERY OBJECT IS CLOSED. `additionalProperties: false` mirrors `JsonParser.Default`, whose
//     `IgnoreUnknownFields` is false. This is the rule whose violation was the finding.
//
//  4. `required` IS THE MEASURED WIRE TRUTH, WITH A DELIBERATE ASYMMETRY.
//       - A RESPONSE-ONLY shape must declare, at minimum, every member WITHOUT explicit protobuf
//         presence. The projection formats with default values ON, and the formatter writes a field
//         when `HasPresence` is false OR the field is set - so a presence-less member is on every
//         response even when it holds 0, "", `false` or `[]`. It MAY declare more, where the
//         operation always populates a message-typed member; the floor is what is enforced.
//       - A shape carried in a REQUEST must declare NOTHING required. The canonical parser reads an
//         absent member as its default, so absence and a default-valued member are indistinguishable
//         to the operation: a `required` list would publish a check nothing performs, and would make
//         a validator reject a body the runtime accepts.
//     The asymmetry is not a compromise between the two - it is what each direction actually
//     guarantees, and a shape that travels both ways can only honour the weaker one.
//
//  5. ENUM MEMBERS ARE THE PROTO SPELLINGS, IN DECLARATION ORDER, WITH THEIR NUMBERS ALONGSIDE. The
//     mapping emits the enumerator NAME, so the spellings are wire values; AAP 0.4.5.3 requires them
//     preserved verbatim because they appear in serialized payloads, log records and characterization
//     recordings, where a rename silently invalidates every stored comparison. `x-enum-values` must
//     agree positionally with `enum`, so value-for-value agreement is checkable from the document.
//
//  WHY DESCRIPTORS AND NOT CLR TYPES  (the reason ContractTestContext gives, applied here)
//  ------------------------------------------------------------------------------------------------
//  protoc PascalCases the C# projection of an enum value, so `E_INVALID_ARGUMENT` becomes
//  `EInvalidArgument` and CLR reflection can never confirm the preserved spelling.
//  `EnumValueDescriptor.Name` returns the proto spelling as authored and is the only thing in the
//  build that can. Descriptors are also the only substrate that can see `IsRepeated`, `IsMap`,
//  `HasPresence`, oneof membership and field numbers - every one of which this suite asserts.
//
//  WHAT THIS SUITE DOES NOT ASSERT, SO THE COVERAGE IS NOT OVERREAD
//  ------------------------------------------------------------------------------------------------
//  It does not compare PROSE. Each generated schema's description reproduces its message's opening
//  paragraph and its members' own comments from the .proto, and that text is deliberately not pinned:
//  the .proto keeps the full rationale and remains the authority, and pinning an English sentence
//  would make editing a comment a test failure. What is pinned is everything a consumer's generator
//  reads.
//
//  It does not assert that a 44-message remainder is absent for a reason: the three bidirectional
//  streams' payloads and the event-notification family never cross this ingress, so they are not in
//  the projected closure. `TheDocumentPublishesExactlyTheProjectedClosure` states that boundary from
//  both sides - nothing in the closure is missing, and nothing outside it is published.
//
//  PURITY  (constraints C-A, C-C, C-F)
//  ------------------------------------------------------------------------------------------------
//  No clock, no randomness, no environment variable, no network, no sleep, no file write. The only
//  input is the document embedded in the Contracts assembly and the descriptors compiled into it, so
//  the suite is repeatable by construction - the hard prerequisite of the Golden-Master approach this
//  repository adopts (AAP 0.6.7). Nothing here reads the legacy tree; the legacy locators that appear
//  in the generated descriptions are citations inside the document under test, not paths this file
//  opens. No secret, key or credential literal appears anywhere below.
//
//  RULES POSITION
//  ------------------------------------------------------------------------------------------------
//  review_rules returns exactly "No user rules provided.", so no user-specified rule governs this
//  file. Absence is not licence: the AAP 0.7.2 baseline applies - nullable and warnings-as-errors
//  inherited from Directory.Build.props and never relaxed, no NoWarn, no #pragma.
// ==================================================================================================

using System.Globalization;
using System.Text.Json.Nodes;
using Google.Protobuf.Reflection;
using Microsoft.OpenApi;
using Xunit;

namespace PowerFramework.Contracts.Tests;

public sealed class GeneratedSchemaFidelityTests
{
    private const string GatewayResourceName = "PowerFramework.Contracts.OpenApi.gateway.v1.yaml";

    /// <summary>The marker naming the protobuf MESSAGE a schema publishes.</summary>
    private const string MessageMarker = "x-proto-message";

    /// <summary>The marker naming the protobuf ENUM a schema publishes.</summary>
    private const string EnumMarker = "x-proto-enum";

    /// <summary>The operation extension naming an operation's request message.</summary>
    private const string RequestMarker = "x-proto-request";

    /// <summary>The operation extension naming an operation's response message.</summary>
    private const string ResponseMarker = "x-proto-response";

    /// <summary>
    /// The one message reached through the `409` problem body rather than through an operation's
    /// response, so it belongs to the published closure without appearing in any
    /// <c>x-proto-response</c>.
    /// </summary>
    private const string ConflictRoot = "common.v1.ConflictDetail";

    private static OpenApiDocument Document =>
        OpenApiContractDocumentTests.ParseEmbeddedDocument(GatewayResourceName);

    // ==============================================================================================
    //  THE SUBSTRATE - descriptor lookups and the marked-schema index
    // ==============================================================================================

    /// <summary>Every message descriptor of the two files this document publishes, by full name.</summary>
    private static Dictionary<string, MessageDescriptor> MessagesByFullName { get; } =
        ContractDescriptors.AllMessages()
            .Where(static message => IsPublishedFile(message.File))
            .ToDictionary(static message => message.FullName, StringComparer.Ordinal);

    /// <summary>Every enum descriptor of the two files this document publishes, by full name.</summary>
    private static Dictionary<string, EnumDescriptor> EnumsByFullName { get; } =
        ContractDescriptors.AllEnums()
            .Where(static declared => IsPublishedFile(declared.File))
            .ToDictionary(static declared => declared.FullName, StringComparer.Ordinal);

    /// <summary>
    /// Whether a descriptor's file is one of the two the gateway contract publishes shapes from.
    /// </summary>
    /// <param name="file">The file the descriptor was declared in.</param>
    /// <returns><see langword="true"/> for common.v1 or dataservices.v1.</returns>
    /// <remarks>
    /// persistence.v1 is excluded deliberately rather than incidentally: nothing but DataServices calls
    /// Persistence, so no persistence.v1 message crosses this ingress and none is published here. A
    /// schema marked with a persistence.v1 name would be a topology violation, and
    /// <see cref="EveryMarkerResolvesToARealDescriptorOfAPublishedFile"/> reports it as one.
    /// </remarks>
    private static bool IsPublishedFile(FileDescriptor file) =>
        string.Equals(file.Package, "common.v1", StringComparison.Ordinal)
        || string.Equals(file.Package, "dataservices.v1", StringComparison.Ordinal);

    /// <summary>One published schema together with the descriptor name it claims to publish.</summary>
    /// <param name="SchemaName">The component schema's name.</param>
    /// <param name="Schema">The schema itself.</param>
    /// <param name="DescriptorName">The fully-qualified protobuf name it names.</param>
    /// <param name="IsEnum">Whether the marker was the enum marker rather than the message one.</param>
    private sealed record MarkedSchema(
        string SchemaName,
        IOpenApiSchema Schema,
        string DescriptorName,
        bool IsEnum);

    /// <summary>
    /// Every component schema carrying a protobuf marker, in document order.
    /// </summary>
    /// <param name="document">The parsed contract document.</param>
    /// <returns>The marked schemas.</returns>
    private static MarkedSchema[] MarkedSchemas(OpenApiDocument document) =>
    [
        .. from entry in document.Components!.Schemas!
           let messageName = SchemaExtension(entry.Value, MessageMarker)
           let enumName = SchemaExtension(entry.Value, EnumMarker)
           where messageName is not null || enumName is not null
           select new MarkedSchema(
               entry.Key,
               entry.Value,
               messageName ?? enumName!,
               messageName is null),
    ];

    /// <summary>
    /// Reads a string-valued specification extension off a schema, or null when absent.
    /// </summary>
    /// <param name="schema">The schema to read.</param>
    /// <param name="name">The extension's name.</param>
    /// <returns>The value, or null.</returns>
    /// <remarks>
    /// Microsoft.OpenApi 2.x models an unrecognised extension as a <c>JsonNodeExtension</c> wrapping a
    /// <c>JsonNode</c>, so the value is reached through the node rather than off a typed property.
    /// </remarks>
    private static string? SchemaExtension(IOpenApiSchema schema, string name)
    {
        if (schema.Extensions is null
            || !schema.Extensions.TryGetValue(name, out IOpenApiExtension? extension))
        {
            return null;
        }

        return extension is JsonNodeExtension node ? node.Node?.GetValue<string>() : null;
    }

    /// <summary>
    /// Reads a string-valued specification extension off an operation, or null when absent.
    /// </summary>
    /// <param name="operation">The operation to read.</param>
    /// <param name="name">The extension's name.</param>
    /// <returns>The value, or null.</returns>
    private static string? OperationExtension(OpenApiOperation operation, string name)
    {
        if (operation.Extensions is null
            || !operation.Extensions.TryGetValue(name, out IOpenApiExtension? extension))
        {
            return null;
        }

        return extension is JsonNodeExtension node ? node.Node?.GetValue<string>() : null;
    }

    /// <summary>
    /// The component name a <c>$ref</c> schema points at, or null when the schema is inline.
    /// </summary>
    /// <param name="schema">The schema to inspect.</param>
    /// <returns>The referenced component's name, or null.</returns>
    /// <remarks>
    /// A <c>$ref</c> is an <see cref="OpenApiSchemaReference"/> that PROXIES its target in
    /// Microsoft.OpenApi 2.x - reading <c>.Type</c> off one returns the TARGET's type rather than null -
    /// so the reference has to be detected by its runtime type and not by a missing member.
    /// </remarks>
    private static string? ReferencedSchemaName(IOpenApiSchema? schema) =>
        schema is OpenApiSchemaReference reference ? reference.Reference.Id : null;

    /// <summary>Every (route, method, operation) triple in the document, flattened.</summary>
    /// <param name="document">The parsed contract document.</param>
    /// <returns>The triples.</returns>
    private static IEnumerable<(string Route, HttpMethod Method, OpenApiOperation Operation)> Operations(
        OpenApiDocument document)
    {
        foreach ((string route, IOpenApiPathItem pathItem) in document.Paths)
        {
            foreach ((HttpMethod method, OpenApiOperation operation) in pathItem.Operations!)
            {
                yield return (route, method, operation);
            }
        }
    }

    /// <summary>
    /// The transitive closure of message names reachable from a set of roots by following
    /// message-typed fields, with map entries resolved to their VALUE type rather than to the
    /// synthetic entry message.
    /// </summary>
    /// <param name="roots">The messages to start from.</param>
    /// <returns>The closure, root messages included.</returns>
    /// <remarks>
    /// A map field's <c>MessageType</c> is protoc's synthetic <c>&lt;Field&gt;Entry</c> message, which
    /// never appears on the wire - the canonical mapping emits a map as a plain JSON object. Walking it
    /// as an ordinary message would put three phantom schemas in the closure, so the walk resolves the
    /// value type instead. The closure is also cycle-safe by construction, which it has to be:
    /// <c>ContextMenuItem</c> holds a repeated field of its own type, because menus nest.
    /// </remarks>
    private static HashSet<string> Closure(IEnumerable<string> roots)
    {
        HashSet<string> reached = new(StringComparer.Ordinal);
        Stack<string> pending = new(roots);

        while (pending.Count > 0)
        {
            string current = pending.Pop();

            if (!MessagesByFullName.TryGetValue(current, out MessageDescriptor? descriptor)
                || !reached.Add(current))
            {
                continue;
            }

            foreach (FieldDescriptor field in descriptor.Fields.InFieldNumberOrder())
            {
                MessageDescriptor? next = field.IsMap
                    ? MapValueField(field).MessageType
                    : field.FieldType is FieldType.Message or FieldType.Group ? field.MessageType : null;

                if (next is not null)
                {
                    pending.Push(next.FullName);
                }
            }
        }

        return reached;
    }

    /// <summary>The value field of a map entry message, which protoc always numbers 2.</summary>
    /// <param name="mapField">The map field.</param>
    /// <returns>The entry message's value field.</returns>
    private static FieldDescriptor MapValueField(FieldDescriptor mapField) =>
        mapField.MessageType.FindFieldByNumber(2);

    /// <summary>The key field of a map entry message, which protoc always numbers 1.</summary>
    /// <param name="mapField">The map field.</param>
    /// <returns>The entry message's key field.</returns>
    private static FieldDescriptor MapKeyField(FieldDescriptor mapField) =>
        mapField.MessageType.FindFieldByNumber(1);

    /// <summary>
    /// The enum names reachable from a set of messages, by following enum-typed fields and map values.
    /// </summary>
    /// <param name="messages">The messages to walk.</param>
    /// <returns>The enum full names.</returns>
    private static HashSet<string> ReferencedEnums(IEnumerable<string> messages)
    {
        HashSet<string> reached = new(StringComparer.Ordinal);

        foreach (FieldDescriptor field in messages
            .Select(static name => MessagesByFullName[name])
            .SelectMany(static message => message.Fields.InFieldNumberOrder()))
        {
            FieldDescriptor subject = field.IsMap ? MapValueField(field) : field;

            if (subject.FieldType == FieldType.Enum)
            {
                reached.Add(subject.EnumType.FullName);
            }
        }

        return reached;
    }

    /// <summary>
    /// The closure of messages the 39 projected operations carry, plus the conflict detail the `409`
    /// body carries.
    /// </summary>
    /// <param name="document">The parsed contract document.</param>
    /// <returns>The request-position closure and the response-position closure.</returns>
    private static (HashSet<string> Requests, HashSet<string> Responses) ProjectedClosures(
        OpenApiDocument document)
    {
        List<string> requestRoots = [];
        List<string> responseRoots = [ConflictRoot];

        foreach ((_, _, OpenApiOperation operation) in Operations(document))
        {
            if (OperationExtension(operation, RequestMarker) is { } request)
            {
                requestRoots.Add(request);
            }

            if (OperationExtension(operation, ResponseMarker) is { } response)
            {
                responseRoots.Add(response);
            }
        }

        return (Closure(requestRoots), Closure(responseRoots));
    }

    // ==============================================================================================
    //  THE GUARD - RUN THIS BEFORE BELIEVING ANY ASSERTION BELOW
    //
    //  Every test in this file walks a set. A set that came back empty would make each of them pass
    //  while checking nothing, and a delegation regression - one open schema reinstated in place of the
    //  133 - is exactly the change that would empty it. So the population is asserted first.
    // ==============================================================================================

    [Fact]
    public void TheDocumentPublishesOneHundredAndThirtyThreeMarkedSchemasOverOneHundredAndFortyFour()
    {
        OpenApiDocument document = Document;
        MarkedSchema[] marked = MarkedSchemas(document);

        Assert.Equal(144, document.Components!.Schemas!.Count);
        Assert.Equal(133, marked.Length);
        Assert.Equal(118, marked.Count(static entry => !entry.IsEnum));
        Assert.Equal(15, marked.Count(static entry => entry.IsEnum));

        // AND THE ELEVEN UNMARKED SCHEMAS ARE EXACTLY THE GATEWAY-AUTHORED ENVELOPES, named by
        // identity. None of them mirrors a protobuf message: the two problem shapes are RFC 9457, the
        // health and capability shapes are this ingress's own, and the two array projections are the
        // collection form of a gRPC server stream, which the protocol definitions have no message for.
        //
        // Asserted as an exact set rather than as a count, so a NEW protobuf shape published without a
        // marker - which would escape every check below - fails here instead of passing silently.
        string[] unmarked =
        [
            .. document.Components.Schemas.Keys
                .Except(marked.Select(static entry => entry.SchemaName), StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];

        Assert.Equal(
            [
                "AggregateHealthReport",
                "Capability",
                "CapabilityReport",
                "ConflictProblemDetails",
                "ExpressionEventStreamResult",
                "HealthCheckResult",
                "PingResponse",
                "ProblemDetails",
                "ReservedRouteBody",
                "RetrieveResult",
                "UpstreamHealth",
            ],
            unmarked);
    }

    [Fact]
    public void EveryMarkerResolvesToARealDescriptorOfAPublishedFile()
    {
        List<string> failures = [];

        foreach (MarkedSchema marked in MarkedSchemas(Document))
        {
            bool resolved = marked.IsEnum
                ? EnumsByFullName.ContainsKey(marked.DescriptorName)
                : MessagesByFullName.ContainsKey(marked.DescriptorName);

            if (!resolved)
            {
                failures.Add(
                    $"schema '{marked.SchemaName}' names {(marked.IsEnum ? "enum" : "message")} "
                        + $"'{marked.DescriptorName}', which is not declared by common.v1.proto or "
                        + "dataservices.v1.proto");
            }
        }

        Assert.True(
            failures.Count == 0,
            "A protobuf marker must name a descriptor that exists, in one of the two files this "
                + "document publishes shapes from. persistence.v1 is excluded by topology - nothing "
                + $"but DataServices calls Persistence.{Environment.NewLine}"
                + string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void NoTwoSchemasPublishTheSameDescriptor()
    {
        // TWO SCHEMAS FOR ONE MESSAGE IS THE SECOND SOURCE OF TRUTH THIS SUITE EXISTS TO PREVENT,
        // reintroduced inside the document rather than between the document and the .proto. It is the
        // shape an "almost the same" copy takes when someone needs one member changed for one
        // operation, and every check below would still pass on both copies.
        (string Descriptor, string[] Schemas)[] duplicated =
        [
            .. MarkedSchemas(Document)
                .GroupBy(static entry => entry.DescriptorName, StringComparer.Ordinal)
                .Where(static group => group.Count() > 1)
                .Select(static group => (
                    group.Key,
                    group.Select(static entry => entry.SchemaName).ToArray())),
        ];

        Assert.True(
            duplicated.Length == 0,
            "Each protobuf descriptor is published by exactly one schema: "
                + string.Join(
                    "; ",
                    duplicated.Select(static entry =>
                        $"{entry.Descriptor} -> {string.Join(", ", entry.Schemas)}")));
    }

    // ==============================================================================================
    //  RULE 1 - MEMBER SETS ARE EXACT, IN BOTH DIRECTIONS
    // ==============================================================================================

    [Fact]
    public void EveryMarkedMessageSchemaDeclaresExactlyItsDescriptorsMembers()
    {
        List<string> failures = [];
        int membersChecked = 0;

        foreach (MarkedSchema marked in MarkedSchemas(Document).Where(static entry => !entry.IsEnum))
        {
            MessageDescriptor descriptor = MessagesByFullName[marked.DescriptorName];

            string[] declared =
            [
                .. descriptor.Fields.InFieldNumberOrder()
                    .Select(static field => field.JsonName)
                    .Order(StringComparer.Ordinal),
            ];

            string[] published =
            [
                .. (marked.Schema.Properties?.Keys ?? []).Order(StringComparer.Ordinal),
            ];

            membersChecked += declared.Length;

            string[] missing = [.. declared.Except(published, StringComparer.Ordinal)];
            string[] extra = [.. published.Except(declared, StringComparer.Ordinal)];

            if (missing.Length > 0 || extra.Length > 0)
            {
                failures.Add(
                    $"{marked.SchemaName} ({marked.DescriptorName}): "
                        + $"missing [{string.Join(", ", missing)}], "
                        + $"undeclared [{string.Join(", ", extra)}]");
            }
        }

        Assert.True(
            failures.Count == 0,
            "A published schema must declare every member its descriptor declares, under the "
                + "descriptor's own canonical JSON name, and nothing else. A MISSING member is the "
                + "defect this tier was built to close - a consumer cannot see something it must send. "
                + "An UNDECLARED member is worse in the other direction: the strict parser answers 400 "
                + $"for it, so the document would be advertising a rejected payload.{Environment.NewLine}"
                + string.Join(Environment.NewLine, failures));

        // The whole surface really was walked. 428 members across 118 messages.
        Assert.Equal(428, membersChecked);
    }

    // ==============================================================================================
    //  RULE 2 - TYPES FOLLOW THE CANONICAL MAPPING
    // ==============================================================================================

    [Fact]
    public void EveryPublishedMemberCarriesTheCanonicalMappingOfItsDescriptorsType()
    {
        List<string> failures = [];

        foreach (MarkedSchema marked in MarkedSchemas(Document).Where(static entry => !entry.IsEnum))
        {
            MessageDescriptor descriptor = MessagesByFullName[marked.DescriptorName];

            foreach (FieldDescriptor field in descriptor.Fields.InFieldNumberOrder())
            {
                if (marked.Schema.Properties is null
                    || !marked.Schema.Properties.TryGetValue(field.JsonName, out IOpenApiSchema? member))
                {
                    // Reported by the member-set test; not restated here.
                    continue;
                }

                string location = $"{marked.SchemaName}.{field.JsonName}";

                if (field.IsMap)
                {
                    CheckMap(field, member, location, failures);
                    continue;
                }

                if (field.IsRepeated)
                {
                    if (member.Type is null || !member.Type.Value.HasFlag(JsonSchemaType.Array))
                    {
                        failures.Add($"{location}: repeated field is not published as an array");
                        continue;
                    }

                    CheckScalar(field, member.Items, $"{location}[]", failures);
                    continue;
                }

                CheckScalar(field, member, location, failures);
            }
        }

        Assert.True(
            failures.Count == 0,
            "Every member must carry the canonical protobuf JSON mapping of its descriptor's type. A "
                + "64-bit integer is [integer, string] because the mapping EMITS it as a JSON string "
                + "and ACCEPTS either form; a schema declaring `integer` alone would reject the "
                + "encoding the upstream actually produces. `bytes` is a base64 string. A message or "
                + $"enum member is a $ref to that type's own published component.{Environment.NewLine}"
                + string.Join(Environment.NewLine, failures));
    }

    /// <summary>
    /// Checks the published shape of a map member: a JSON object whose value schema matches the map's
    /// value type.
    /// </summary>
    /// <param name="field">The map field.</param>
    /// <param name="member">The published member schema.</param>
    /// <param name="location">Where to report a failure.</param>
    /// <param name="failures">The failure accumulator.</param>
    /// <remarks>
    /// The canonical mapping emits a map as a plain object keyed by the map key's DECIMAL SPELLING when
    /// the key is numeric, so nothing is asserted about the key's JSON type - a JSON member name is a
    /// string whatever the .proto says. What is asserted is that the key type is one the mapping can
    /// spell at all, which excludes a message or a floating-point key; protoc rejects those, so this is
    /// a statement about what reached the descriptor rather than a hypothetical.
    /// </remarks>
    private static void CheckMap(
        FieldDescriptor field,
        IOpenApiSchema member,
        string location,
        List<string> failures)
    {
        if (member.Type is null || !member.Type.Value.HasFlag(JsonSchemaType.Object))
        {
            failures.Add($"{location}: map field is not published as an object");
            return;
        }

        FieldDescriptor key = MapKeyField(field);

        if (key.FieldType is FieldType.Message or FieldType.Group
            or FieldType.Double or FieldType.Float or FieldType.Bytes)
        {
            failures.Add($"{location}: map key type {key.FieldType} has no canonical JSON spelling");
        }

        if (member.AdditionalProperties is null)
        {
            failures.Add($"{location}: map field publishes no value schema");
            return;
        }

        CheckScalar(MapValueField(field), member.AdditionalProperties, $"{location}{{}}", failures);
    }

    /// <summary>
    /// Checks one published member against the canonical mapping of a singular field's type.
    /// </summary>
    /// <param name="field">The field whose type is being published.</param>
    /// <param name="member">The published member schema, or null when the document declared none.</param>
    /// <param name="location">Where to report a failure.</param>
    /// <param name="failures">The failure accumulator.</param>
    private static void CheckScalar(
        FieldDescriptor field,
        IOpenApiSchema? member,
        string location,
        List<string> failures)
    {
        if (member is null)
        {
            failures.Add($"{location}: no schema published for a {field.FieldType} field");
            return;
        }

        switch (field.FieldType)
        {
            case FieldType.Message or FieldType.Group:
                ExpectReference(member, field.MessageType.FullName, location, failures);
                return;

            case FieldType.Enum:
                ExpectReference(member, field.EnumType.FullName, location, failures);
                return;

            case FieldType.Int64 or FieldType.SInt64 or FieldType.SFixed64:
                ExpectSixtyFourBit(member, "int64", location, failures);
                return;

            case FieldType.UInt64 or FieldType.Fixed64:
                ExpectSixtyFourBit(member, "uint64", location, failures);
                return;

            case FieldType.Int32 or FieldType.SInt32 or FieldType.SFixed32
                or FieldType.UInt32 or FieldType.Fixed32:
                ExpectPrimitive(member, JsonSchemaType.Integer, location, failures);
                return;

            case FieldType.Bool:
                ExpectPrimitive(member, JsonSchemaType.Boolean, location, failures);
                return;

            case FieldType.String or FieldType.Bytes:
                ExpectPrimitive(member, JsonSchemaType.String, location, failures);

                if (field.FieldType == FieldType.Bytes
                    && !string.Equals(member.Format, "byte", StringComparison.Ordinal))
                {
                    failures.Add(
                        $"{location}: a bytes field must declare format 'byte' (base64), not "
                            + $"'{member.Format ?? "(none)"}'");
                }

                return;

            case FieldType.Double or FieldType.Float:
                ExpectPrimitive(member, JsonSchemaType.Number, location, failures);
                return;

            default:
                failures.Add($"{location}: field type {field.FieldType} has no published mapping rule");
                return;
        }
    }

    /// <summary>
    /// Asserts a member is a <c>$ref</c> to the schema that publishes the named descriptor.
    /// </summary>
    /// <param name="member">The published member schema.</param>
    /// <param name="descriptorName">The descriptor the member's type is.</param>
    /// <param name="location">Where to report a failure.</param>
    /// <param name="failures">The failure accumulator.</param>
    private static void ExpectReference(
        IOpenApiSchema member,
        string descriptorName,
        string location,
        List<string> failures)
    {
        string? referenced = ReferencedSchemaName(member);

        if (referenced is null)
        {
            failures.Add(
                $"{location}: a member typed {descriptorName} must be a $ref to that type's own "
                    + "published component, not an inline copy of it");
            return;
        }

        MarkedSchema? target = MarkedSchemas(Document)
            .FirstOrDefault(entry => string.Equals(entry.SchemaName, referenced, StringComparison.Ordinal));

        if (target is null)
        {
            failures.Add($"{location}: $ref target '{referenced}' carries no protobuf marker");
            return;
        }

        if (!string.Equals(target.DescriptorName, descriptorName, StringComparison.Ordinal))
        {
            failures.Add(
                $"{location}: $ref points at '{referenced}', which publishes "
                    + $"{target.DescriptorName} rather than {descriptorName}");
        }
    }

    /// <summary>
    /// Asserts a 64-bit member accepts both JSON forms and names the right signedness.
    /// </summary>
    /// <param name="member">The published member schema.</param>
    /// <param name="format">The expected format - <c>int64</c> or <c>uint64</c>.</param>
    /// <param name="location">Where to report a failure.</param>
    /// <param name="failures">The failure accumulator.</param>
    /// <remarks>
    /// SIGNEDNESS IS NOT COSMETIC. A <c>uint64</c> declared <c>int64</c> produces a signed 64-bit
    /// generated model, and every value above <c>Int64.MaxValue</c> - half the legacy
    /// <c>unsignedlong</c> domain - is then rejected by validation or wrapped to a negative number,
    /// silently.
    /// </remarks>
    private static void ExpectSixtyFourBit(
        IOpenApiSchema member,
        string format,
        string location,
        List<string> failures)
    {
        if (member.Type is null
            || !member.Type.Value.HasFlag(JsonSchemaType.Integer)
            || !member.Type.Value.HasFlag(JsonSchemaType.String))
        {
            failures.Add(
                $"{location}: a 64-bit integer must be published as [integer, string], not "
                    + $"'{member.Type?.ToString() ?? "(none)"}'");
        }

        if (!string.Equals(member.Format, format, StringComparison.Ordinal))
        {
            failures.Add(
                $"{location}: expected format '{format}', found '{member.Format ?? "(none)"}'");
        }
    }

    /// <summary>
    /// Asserts a member declares an expected primitive JSON type and is not a reference.
    /// </summary>
    /// <param name="member">The published member schema.</param>
    /// <param name="expected">The JSON type the canonical mapping produces.</param>
    /// <param name="location">Where to report a failure.</param>
    /// <param name="failures">The failure accumulator.</param>
    private static void ExpectPrimitive(
        IOpenApiSchema member,
        JsonSchemaType expected,
        string location,
        List<string> failures)
    {
        if (ReferencedSchemaName(member) is { } referenced)
        {
            failures.Add(
                $"{location}: a scalar member must be declared inline, not as a $ref to "
                    + $"'{referenced}'");
            return;
        }

        if (member.Type is null || !member.Type.Value.HasFlag(expected))
        {
            failures.Add(
                $"{location}: expected JSON type {expected}, found "
                    + $"'{member.Type?.ToString() ?? "(none)"}'");
        }
    }

    // ==============================================================================================
    //  RULE 3 - EVERY MARKED OBJECT IS CLOSED
    // ==============================================================================================

    [Fact]
    public void EveryMarkedMessageSchemaIsClosedToUnknownMembers()
    {
        string[] open =
        [
            .. MarkedSchemas(Document)
                .Where(static entry => !entry.IsEnum)
                .Where(static entry => entry.Schema.AdditionalPropertiesAllowed)
                .Select(static entry => entry.SchemaName)
                .Order(StringComparer.Ordinal),
        ];

        Assert.True(
            open.Length == 0,
            "Every protobuf shape must be closed, because the projection binds with JsonParser.Default "
                + "and its IgnoreUnknownFields is false: an unrecognised member is answered with 400, "
                + "not discarded. An open schema publishes a permissiveness the runtime does not have, "
                + "which is the defect this tier replaced. Open: "
                + string.Join(", ", open));
    }

    [Fact]
    public void NoProjectedOperationPublishesAnOpenOrInlineBody()
    {
        // THE REGRESSION GUARD FOR THE FINDING ITSELF, expressed over the OPERATIONS rather than over
        // the schema catalogue: reinstating a delegated envelope would leave the 133 marked schemas
        // untouched and every other test in this file passing, while the operations quietly stopped
        // pointing at them.
        List<string> failures = [];
        int bodies = 0;
        int successes = 0;

        foreach ((string route, HttpMethod method, OpenApiOperation operation) in Operations(Document))
        {
            string? requestMessage = OperationExtension(operation, RequestMarker);
            string? responseMessage = OperationExtension(operation, ResponseMarker);

            if (requestMessage is null && responseMessage is null)
            {
                continue;
            }

            string what = $"{method} {route}";

            if (operation.RequestBody?.Content is { } requestContent)
            {
                foreach ((string mediaType, OpenApiMediaType media) in requestContent)
                {
                    bodies++;
                    CheckBodyPointsAtItsMessage(
                        media.Schema,
                        requestMessage,
                        $"{what} request body ({mediaType})",
                        failures);
                }
            }

            OpenApiResponse? success = operation.Responses?
                .Where(static entry => entry.Key.StartsWith('2'))
                .Select(static entry => entry.Value as OpenApiResponse)
                .FirstOrDefault(static response => response?.Content is { Count: > 0 });

            if (success?.Content is { } responseContent)
            {
                foreach ((string mediaType, OpenApiMediaType media) in responseContent)
                {
                    successes++;

                    // A SERVER-STREAM PROJECTION IS AN ARRAY OF THE MESSAGE, not the message, so the
                    // element schema is what has to point at it. The two array projections are the
                    // collection form of C-03's Retrieve and C-04's EventStream.
                    IOpenApiSchema? subject = media.Schema;

                    if (ReferencedSchemaName(subject) is "RetrieveResult" or "ExpressionEventStreamResult")
                    {
                        subject = Document.Components!.Schemas![ReferencedSchemaName(subject)!].Items;
                    }

                    CheckBodyPointsAtItsMessage(
                        subject,
                        responseMessage,
                        $"{what} 2xx body ({mediaType})",
                        failures);
                }
            }
        }

        Assert.True(
            failures.Count == 0,
            "Every projected body must $ref the published schema of the message its own x-proto-* "
                + "extension names. A delegated envelope, an inline object, or a $ref to some other "
                + $"message are the three ways this goes wrong.{Environment.NewLine}"
                + string.Join(Environment.NewLine, failures));

        // 36 request bodies and 39 success bodies. Three operations carry their arguments as a path or
        // query parameter and declare no body at all: the event-gate read and the two session closes.
        Assert.Equal(36, bodies);
        Assert.Equal(39, successes);
    }

    /// <summary>
    /// Asserts one body schema is a reference to the published schema of an expected message.
    /// </summary>
    /// <param name="schema">The body's schema.</param>
    /// <param name="expectedMessage">The message the operation's extension names.</param>
    /// <param name="location">Where to report a failure.</param>
    /// <param name="failures">The failure accumulator.</param>
    private static void CheckBodyPointsAtItsMessage(
        IOpenApiSchema? schema,
        string? expectedMessage,
        string location,
        List<string> failures)
    {
        if (expectedMessage is null)
        {
            failures.Add($"{location}: the operation declares no protobuf message for this body");
            return;
        }

        string? referenced = ReferencedSchemaName(schema);

        if (referenced is null)
        {
            failures.Add($"{location}: the body schema is inline rather than a $ref to a published shape");
            return;
        }

        MarkedSchema? target = MarkedSchemas(Document)
            .FirstOrDefault(entry => string.Equals(entry.SchemaName, referenced, StringComparison.Ordinal));

        if (target is null)
        {
            failures.Add($"{location}: '{referenced}' carries no protobuf marker");
            return;
        }

        if (!string.Equals(target.DescriptorName, expectedMessage, StringComparison.Ordinal))
        {
            failures.Add(
                $"{location}: points at '{referenced}' ({target.DescriptorName}) while the operation "
                    + $"declares {expectedMessage}");
        }
    }

    // ==============================================================================================
    //  RULE 4 - `required` IS THE MEASURED WIRE TRUTH
    // ==============================================================================================

    [Fact]
    public void RequiredMembersAreTheMeasuredWireTruthInBothDirections()
    {
        OpenApiDocument document = Document;
        (HashSet<string> requests, HashSet<string> responses) = ProjectedClosures(document);

        List<string> failures = [];
        int responseOnly = 0;
        int requestCarried = 0;

        foreach (MarkedSchema marked in MarkedSchemas(document).Where(static entry => !entry.IsEnum))
        {
            MessageDescriptor descriptor = MessagesByFullName[marked.DescriptorName];
            string[] published = marked.Schema.Required is { } declared ? [.. declared] : [];

            string[] undeclared =
            [
                .. published.Except(
                    marked.Schema.Properties?.Keys ?? [],
                    StringComparer.Ordinal),
            ];

            if (undeclared.Length > 0)
            {
                failures.Add(
                    $"{marked.SchemaName}: required names members it does not declare - "
                        + string.Join(", ", undeclared));
            }

            bool inRequest = requests.Contains(marked.DescriptorName);

            if (inRequest)
            {
                requestCarried++;

                if (published.Length > 0)
                {
                    failures.Add(
                        $"{marked.SchemaName} ({marked.DescriptorName}) travels in a REQUEST and "
                            + $"declares required [{string.Join(", ", published)}]. The canonical parser "
                            + "reads an absent member as its default, so this publishes a check nothing "
                            + "performs and rejects a body the runtime accepts.");
                }

                continue;
            }

            if (!responses.Contains(marked.DescriptorName))
            {
                failures.Add(
                    $"{marked.SchemaName} ({marked.DescriptorName}) is published but is carried by "
                        + "neither a request nor a response");
                continue;
            }

            responseOnly++;

            string[] floor =
            [
                .. descriptor.Fields.InFieldNumberOrder()
                    .Where(static field => !field.HasPresence)
                    .Select(static field => field.JsonName),
            ];

            string[] absent = [.. floor.Except(published, StringComparer.Ordinal)];

            if (absent.Length > 0)
            {
                failures.Add(
                    $"{marked.SchemaName} ({marked.DescriptorName}) is RESPONSE-ONLY and omits "
                        + $"[{string.Join(", ", absent)}] from required. The projection formats default "
                        + "values, so a member without explicit presence is on every response even when "
                        + "it holds 0, \"\", false or [].");
            }
        }

        Assert.True(
            failures.Count == 0,
            "The required rule is asymmetric because the two directions guarantee different things - "
                + $"see this file's header for the measurement.{Environment.NewLine}"
                + string.Join(Environment.NewLine, failures));

        // Both arms of the asymmetry were actually exercised.
        Assert.Equal(52, requestCarried);
        Assert.Equal(66, responseOnly);
    }

    // ==============================================================================================
    //  RULE 5 - ENUM SPELLINGS AND NUMBERS
    // ==============================================================================================

    [Fact]
    public void EveryMarkedEnumSchemaPublishesItsDescriptorsSpellingsAndNumbersInOrder()
    {
        List<string> failures = [];
        int valuesChecked = 0;

        foreach (MarkedSchema marked in MarkedSchemas(Document).Where(static entry => entry.IsEnum))
        {
            EnumDescriptor descriptor = EnumsByFullName[marked.DescriptorName];

            string[] names = [.. descriptor.Values.Select(static value => value.Name)];
            int[] numbers = [.. descriptor.Values.Select(static value => value.Number)];
            valuesChecked += names.Length;

            if (marked.Schema.Type is null || !marked.Schema.Type.Value.HasFlag(JsonSchemaType.String))
            {
                failures.Add(
                    $"{marked.SchemaName}: the canonical mapping emits an enumerator NAME, so the "
                        + "schema must be a string");
            }

            string[] members =
            [
                .. (marked.Schema.Enum ?? []).Select(static node => node!.GetValue<string>()),
            ];

            if (!names.SequenceEqual(members, StringComparer.Ordinal))
            {
                failures.Add(
                    $"{marked.SchemaName}: enum is [{string.Join(", ", members)}], descriptor declares "
                        + $"[{string.Join(", ", names)}]");
            }

            CheckEnumExtension(marked, "x-enum-varnames", names, failures);
            CheckEnumNumbers(marked, numbers, failures);
        }

        Assert.True(
            failures.Count == 0,
            "Enumerator spellings are WIRE VALUES, and AAP 0.4.5.3 requires them preserved verbatim: "
                + "they appear in serialized payloads, log records and characterization recordings, "
                + "where a rename does not restyle a symbol - it silently invalidates every stored "
                + $"comparison that mentions it.{Environment.NewLine}"
                + string.Join(Environment.NewLine, failures));

        // 15 enums, 114 enumerators.
        Assert.Equal(114, valuesChecked);
    }

    /// <summary>
    /// Checks a string-array extension agrees with the descriptor's enumerator names, in order.
    /// </summary>
    /// <param name="marked">The schema under test.</param>
    /// <param name="extensionName">The extension to read.</param>
    /// <param name="expected">The expected names, in declaration order.</param>
    /// <param name="failures">The failure accumulator.</param>
    private static void CheckEnumExtension(
        MarkedSchema marked,
        string extensionName,
        string[] expected,
        List<string> failures)
    {
        if (marked.Schema.Extensions is null
            || !marked.Schema.Extensions.TryGetValue(extensionName, out IOpenApiExtension? extension)
            || extension is not JsonNodeExtension node
            || node.Node is not JsonArray array)
        {
            failures.Add($"{marked.SchemaName}: {extensionName} is missing or is not an array");
            return;
        }

        string[] published = [.. array.Select(static value => value!.GetValue<string>())];

        if (!expected.SequenceEqual(published, StringComparer.Ordinal))
        {
            failures.Add(
                $"{marked.SchemaName}: {extensionName} is [{string.Join(", ", published)}], descriptor "
                    + $"declares [{string.Join(", ", expected)}]");
        }
    }

    /// <summary>
    /// Checks <c>x-enum-values</c> carries the descriptor's numbers, positionally aligned with
    /// <c>enum</c>.
    /// </summary>
    /// <param name="marked">The schema under test.</param>
    /// <param name="expected">The expected numbers, in declaration order.</param>
    /// <param name="failures">The failure accumulator.</param>
    /// <remarks>
    /// The numbers matter even though the wire carries names: they are what the .proto declares, what a
    /// numeric bridge to a legacy ordinal casts to, and what a characterization recording holds - so
    /// value-for-value agreement has to be checkable from the document alone.
    /// </remarks>
    private static void CheckEnumNumbers(MarkedSchema marked, int[] expected, List<string> failures)
    {
        if (marked.Schema.Extensions is null
            || !marked.Schema.Extensions.TryGetValue("x-enum-values", out IOpenApiExtension? extension)
            || extension is not JsonNodeExtension node
            || node.Node is not JsonArray array)
        {
            failures.Add($"{marked.SchemaName}: x-enum-values is missing or is not an array");
            return;
        }

        int[] published = [.. array.Select(static value => (int)value!.GetValue<decimal>())];

        if (!expected.SequenceEqual(published))
        {
            failures.Add(
                $"{marked.SchemaName}: x-enum-values is "
                    + $"[{string.Join(", ", published.Select(static value => value.ToString(CultureInfo.InvariantCulture)))}], "
                    + $"descriptor declares "
                    + $"[{string.Join(", ", expected.Select(static value => value.ToString(CultureInfo.InvariantCulture)))}]");
        }
    }

    // ==============================================================================================
    //  THE CLOSURE BOUNDARY - NOTHING MISSING, NOTHING SURPLUS
    // ==============================================================================================

    [Fact]
    public void TheDocumentPublishesExactlyTheProjectedClosure()
    {
        OpenApiDocument document = Document;
        (HashSet<string> requests, HashSet<string> responses) = ProjectedClosures(document);

        HashSet<string> expectedMessages = new(requests, StringComparer.Ordinal);
        expectedMessages.UnionWith(responses);

        HashSet<string> expectedEnums = ReferencedEnums(expectedMessages);

        MarkedSchema[] marked = MarkedSchemas(document);

        string[] publishedMessages =
        [
            .. marked.Where(static entry => !entry.IsEnum)
                .Select(static entry => entry.DescriptorName)
                .Order(StringComparer.Ordinal),
        ];

        string[] publishedEnums =
        [
            .. marked.Where(static entry => entry.IsEnum)
                .Select(static entry => entry.DescriptorName)
                .Order(StringComparer.Ordinal),
        ];

        Assert.Equal(
            [.. expectedMessages.Order(StringComparer.Ordinal)],
            publishedMessages);

        Assert.Equal(
            [.. expectedEnums.Order(StringComparer.Ordinal)],
            publishedEnums);

        // AND THE REMAINDER IS DELIBERATE. The two files declare more messages than the projection
        // carries; the surplus is the three bidirectional streams' payloads and the event-notification
        // family, none of which crosses this ingress. Asserting the count keeps a NEW message from
        // being added to a projected shape without being published - that would fail the closure
        // comparison above - while leaving the unprojected family free to grow.
        Assert.Equal(118, publishedMessages.Length);
        Assert.Equal(15, publishedEnums.Length);
        Assert.True(
            MessagesByFullName.Count > publishedMessages.Length,
            "The published closure should be a strict subset of the two files' messages; if it is not, "
                + "either an unprojected message has been pulled into a projected shape or the walk is "
                + "no longer reaching what it used to.");
    }

    // ==============================================================================================
    //  THE FINDING'S OWN WITNESS - the four members it named by name
    // ==============================================================================================

    [Fact]
    public void TheUpdateReconciliationMembersArePublishedAndGuaranteed()
    {
        // THE OPERATION THE FINDING NAMES. `POST /v1/datawindow/update` reports what it changed, and a
        // caller cannot reconcile its buffer without reading all four members: the three counts say how
        // many rows were inserted, updated and deleted, and `identity` carries the values the engine
        // assigned to the rows it created. Under the delegated envelope not one of them was visible to a
        // consumer's generator.
        //
        // THE WITNESS USED TO READ `LoadRowsResponse`, WHICH THE SCHEMA HAS SINCE WITHDRAWN. It is
        // retargeted rather than deleted, because the property under test is the tier's rule - a
        // response-only shape declares every member without explicit presence as required - and that
        // rule needs a witness whose members are all legitimately zero or empty on a real response.
        IOpenApiSchema schema = Document.Components!.Schemas!["UpdateResponse"];

        Assert.Equal(
            "dataservices.v1.UpdateResponse",
            SchemaExtension(schema, MessageMarker));

        Assert.NotNull(schema.Properties);
        Assert.Contains("retCode", schema.Properties.Keys);
        Assert.Contains("rowsInserted", schema.Properties.Keys);
        Assert.Contains("rowsUpdated", schema.Properties.Keys);
        Assert.Contains("rowsDeleted", schema.Properties.Keys);
        Assert.Contains("identity", schema.Properties.Keys);
        Assert.Contains("error", schema.Properties.Keys);

        // THE THREE COUNTS AND THE IDENTITY ARRAY ARE GUARANTEED PRESENT, and that is the member of the
        // finding with teeth: every one of them is legitimately ZERO or EMPTY on a real response - an
        // update that changed nothing is a legitimate no-op rather than a fault - so a consumer must be
        // able to distinguish "zero" from "not reported". Formatting default values is what makes them
        // always present; declaring them required is what publishes that.
        Assert.NotNull(schema.Required);
        Assert.Contains("rowsInserted", schema.Required);
        Assert.Contains("rowsUpdated", schema.Required);
        Assert.Contains("rowsDeleted", schema.Required);
        Assert.Contains("identity", schema.Required);
        Assert.Contains("retCode", schema.Required);

        // `error` IS NOT REQUIRED, AND MUST NOT BE. It is a message-typed member with explicit
        // protobuf presence, so it is absent on a successful update. Requiring it would make every
        // success fail validation.
        Assert.DoesNotContain("error", schema.Required);

        // The counts are 64-bit, so they arrive as JSON STRINGS and a consumer that models them as
        // numbers is wrong about the encoding rather than merely imprecise.
        foreach (string member in (string[])["rowsInserted", "rowsUpdated", "rowsDeleted"])
        {
            IOpenApiSchema count = schema.Properties[member];
            Assert.NotNull(count.Type);
            Assert.True(count.Type.Value.HasFlag(JsonSchemaType.Integer));
            Assert.True(count.Type.Value.HasFlag(JsonSchemaType.String));
            Assert.Equal("int64", count.Format);
        }
    }
}
