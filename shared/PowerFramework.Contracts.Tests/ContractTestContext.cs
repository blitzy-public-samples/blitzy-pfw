// ==================================================================================================
//  ContractTestContext - THE SHARED TEST INFRASTRUCTURE FOR shared/PowerFramework.Contracts.Tests
//  ------------------------------------------------------------------------------------------------
//  SUBJECT   Proto/common.v1.proto, Proto/dataservices.v1.proto, Proto/persistence.v1.proto
//            OpenApi/security.v1.yaml, OpenApi/gateway.v1.yaml
//            - i.e. the whole of shared/PowerFramework.Contracts, the published cross-service
//              boundary. Contracts C-01 through C-10 live in those five files and nowhere else.
//
//  WHAT THIS FILE IS, AND EQUALLY WHAT IT IS NOT
//  ------------------------------------------------------------------------------------------------
//  It carries exactly TWO surfaces and no third:
//
//      ContractDescriptors        reaches the three generated Protobuf FileDescriptors and looks
//                                things up inside them.
//      OpenApiContractDocuments   loads the two authored OpenAPI documents once per test assembly
//                                and hands them, and their parse diagnostics, to the test classes.
//
//  Every member of both is either a DESCRIPTOR LOOKUP or a DOCUMENT LOAD. That restriction is
//  deliberate and it is constraint C-A (AAP 0.7.3) applied to a test project: PowerFramework.Contracts
//  "carries no behaviour; it is the boundary definition, not a shared-code back door" (AAP 0.4.2.3),
//  and a test-side facade that wrapped, adapted or "improved" that boundary would reintroduce exactly
//  the shared-behaviour path the architecture forbids - only in a project nobody thinks to audit for
//  it. So there is no convenience assertion here, no builder, no fake, no adapter over a generated
//  type, and no place for a later agent to park logic. If a single test needs a helper, it belongs in
//  that test file; a helper earns a place here only when two or more sibling tests need it.
//
//  WHY DESCRIPTORS AND NOT CLR TYPES ARE THE REFLECTION SUBSTRATE  (constraint C-K)
//  ------------------------------------------------------------------------------------------------
//  Because CLR reflection physically cannot see the thing that has to be verified.
//
//  AAP 0.4.5.3 requires the legacy SCREAMING_SNAKE constant identifiers to be preserved VERBATIM -
//  `E_INVALID_ARGUMENT`, `E_NO_IMPLEMENTATION`, `SQL_MS_REPLACE`, `CANCELLED` - because those exact
//  spellings appear in serialized payloads, in log records and in characterization recordings, where a
//  rename silently invalidates every stored comparison. protoc PascalCases the C# projection of an
//  enum value, so `E_INVALID_ARGUMENT` in the .proto becomes the member `EInvalidArgument` in the
//  generated code. Reflecting over the generated CLR enum therefore reads the RENAMED spelling and can
//  never confirm the original one. `EnumValueDescriptor.Name` returns the proto spelling as authored,
//  and is the only thing in the build that can.
//
//  Descriptors are also the only substrate that can observe the SHAPE properties the legacy semantics
//  demand, none of which survives into the CLR surface in a checkable form:
//      repeated              FieldDescriptor.IsRepeated
//      oneof membership      FieldDescriptor.ContainingOneof / .RealContainingOneof, MessageDescriptor.Oneofs
//      explicit presence     FieldDescriptor.HasPresence            (proto3 `optional`)
//      streaming direction   MethodDescriptor.IsClientStreaming / .IsServerStreaming
//      wire numbering        FieldDescriptor.FieldNumber, EnumValueDescriptor.Number
//  A descriptor is additionally what BOTH ENDS of a call resolve against, so a property asserted
//  through one is a property of the CONTRACT rather than of one language's rendering of it.
//
//  MATCHING POLICY - EXACT AND CASE-SENSITIVE, WITH NO SILENT NORMALISATION
//  ------------------------------------------------------------------------------------------------
//  Every lookup below compares ordinally and case-sensitively. A test that passes because the helper
//  lower-cased away a spelling difference defeats the entire purpose of the file, so case-insensitive
//  comparison exists only in two members whose names say so out loud
//  (FindDescriptorsNamedIgnoringCase, FindDescriptorsMentioning) and which exist for the sweep tests
//  that reason over VOCABULARY rather than over identity.
//
//  Simple-name ambiguity is real in this contract set rather than theoretical - `UpdateRequest` and
//  `UpdateResponse` are each declared in BOTH persistence.v1 and dataservices.v1, `Flag` twice and
//  `Value` three times - so a lookup that quietly returned a first match would hand a test the wrong
//  descriptor and still pass. Ambiguity is consequently an ERROR that names every candidate, not a
//  coin toss.
//
//  FAILURE MESSAGES ARE THE PRODUCT HERE
//  ------------------------------------------------------------------------------------------------
//  Every sibling test class in this folder reaches through these lookups. A miss that surfaced as a
//  NullReferenceException would cost far more to diagnose than the few lines a descriptive failure
//  costs to write, so each Require* member throws an xunit-visible assertion failure that names what
//  was sought, how many candidates were searched, and the closest available names. A failure reading
//  "message 'UpdateReply' was not found; closest: persistence.v1.UpdateResponse, ..." is actionable in
//  one read.
//
//  ARGUMENT ERRORS AND LOOKUP FAILURES ARE DELIBERATELY DIFFERENT EXCEPTIONS. A null or blank name is
//  a defect in the CALLING TEST and throws ArgumentException; a well-formed name that does not resolve
//  is a finding ABOUT THE CONTRACT and throws Xunit.Sdk.FailException, the same exception Assert.Fail
//  raises, so it is reported as an assertion failure rather than as an unexpected error.
//
//  PURITY - THESE HELPERS HAVE NO AMBIENT DEPENDENCIES
//  ------------------------------------------------------------------------------------------------
//  No clock read, no randomness, no environment variable, no network, no sleep, and no mutable static
//  state beyond the immutable descriptor list. Repeatability is the one hard prerequisite of the
//  Golden-Master approach this repository adopts (AAP 0.6.7), and it starts here. The only I/O in the
//  file is the strictly read-only File.Exists / File.OpenRead pair in OpenApiContractDocuments.
//
//  READ-ONLY, AND NOWHERE NEAR THE LEGACY TREE  (constraint C-C)
//  ------------------------------------------------------------------------------------------------
//  The document locator resolves under shared/PowerFramework.Contracts/OpenApi/ and nowhere else. It
//  never touches ws_objects/**, tests/blink|sciter|webview/**, the root native binaries, the *.pbl /
//  *.pbt / *.pbw / *.pbr / *.pbd files or the five pre-existing Chinese documents under docs/. No test
//  in this folder reads a legacy file at runtime at all: the legacy locators cited in this project -
//  ws_objects/pfw.shared.pbl.src/retcode.sru for the return-code catalogue, and
//  ws_objects/pfw.pbl.src/project.srj for the build intent - appear only as citations in comments.
//  There is no file creation, no write, no move and no delete anywhere in this file, by construction.
//
//  RULES POSITION
//  ------------------------------------------------------------------------------------------------
//  review_rules returns exactly "No user rules provided.", so NO user-specified rule governs this
//  file. Its absence is not licence: the enterprise-standard baseline of AAP 0.7.2 applies in its
//  place - nullable enabled and warnings as errors inherited and never relaxed, no NoWarn and no
//  #pragma, no secret of any kind in source (nothing here needs one, per C-F's never-replicate
//  posture), and versioned contracts as the only cross-service coupling.
// ==================================================================================================

using System.Globalization;
using System.Text;
using Google.Protobuf.Reflection;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;
using PowerFramework.Contracts.Common.V1;
using PowerFramework.Contracts.DataServices.V1;
using PowerFramework.Contracts.Persistence.V1;
using Xunit;
using Xunit.Sdk;

// ==================================================================================================
//  THE FIXTURE IS REGISTERED ONCE, FOR THE WHOLE ASSEMBLY, HERE.
//
//  xunit.v3's AssemblyFixtureAttribute creates the instance and awaits its InitializeAsync BEFORE any
//  test in the assembly runs, then disposes it after the last one. That is what makes "parse the two
//  documents once" true in the strong sense: a class fixture would reparse per test class and a
//  collection fixture per collection, and 4,585 + 3,460 lines of YAML parsed repeatedly is both wasted
//  work and noisier to read when something fails, because the same parse error is reported many times.
//
//  A sibling test class obtains it by declaring a constructor parameter of exactly this type:
//
//      public sealed class SomeContractTests(OpenApiContractDocuments documents)
//      {
//          [Fact]
//          public void GatewayParsesAsTheSpecificationVersionItDeclares() =>
//              Assert.Equal(OpenApiSpecVersion.OpenApi3_1, documents.GatewayDiagnostic.SpecificationVersion);
//      }
//
//  THAT IS THE ONE CONVENTION FOR THIS FOLDER - please do not add a competing [CollectionDefinition]
//  or a second registration of this fixture. The attribute allows multiple registrations, so a
//  duplicate would construct and parse twice while looking correct.
//
//  The type must be fully qualified here because an assembly-level attribute is evaluated before the
//  file-scoped namespace declaration below takes effect.
// ==================================================================================================
[assembly: AssemblyFixture(typeof(PowerFramework.Contracts.Tests.OpenApiContractDocuments))]

namespace PowerFramework.Contracts.Tests;

/// <summary>
/// Reaches the three generated Protobuf <see cref="FileDescriptor"/>s of
/// <c>shared/PowerFramework.Contracts</c> and looks up the messages, enums, enum values, fields,
/// services and methods declared inside them.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why descriptors rather than the generated CLR types.</b> protoc PascalCases the C# projection of
/// a proto identifier, so the enum value <c>E_INVALID_ARGUMENT</c> becomes the member
/// <c>EInvalidArgument</c>. CLR reflection therefore reads the renamed spelling and can never confirm
/// the original, while AAP 0.4.5.3 requires the legacy SCREAMING_SNAKE spellings to be preserved
/// verbatim because they appear in serialized payloads, log records and characterization recordings.
/// <see cref="EnumValueDescriptor.Name"/> returns the proto spelling as authored. Descriptors are
/// likewise the only way to observe <c>repeated</c> (<see cref="FieldDescriptor.IsRepeated"/>),
/// <c>oneof</c> membership (<see cref="FieldDescriptor.ContainingOneof"/>,
/// <see cref="MessageDescriptor.Oneofs"/>), explicit presence
/// (<see cref="FieldDescriptor.HasPresence"/>) and streaming direction
/// (<see cref="MethodDescriptor.IsClientStreaming"/>, <see cref="MethodDescriptor.IsServerStreaming"/>).
/// </para>
/// <para>
/// <b>Name matching.</b> Ordinal and case-sensitive throughout. A supplied name matches a descriptor
/// when it equals the descriptor's <c>FullName</c>, or equals its simple <c>Name</c>, or is a
/// dot-boundary suffix of its <c>FullName</c> - so <c>"UpdateResponse"</c>,
/// <c>"persistence.v1.UpdateResponse"</c> and <c>"ColumnExpEvent.ItemChanged"</c> are all valid ways
/// to address a descriptor. Zero matches yield <see langword="null"/> from a <c>Find</c> member; more
/// than one is an error that names every candidate, because simple-name collisions genuinely exist in
/// this contract set and returning a first match would hand a test the wrong descriptor silently.
/// </para>
/// <para>
/// <b>Purity.</b> No clock, no randomness, no environment variable, no network and no I/O. The
/// descriptor graph is immutable and process-wide, so every member here is safe to call from parallel
/// test collections.
/// </para>
/// </remarks>
public static class ContractDescriptors
{
    /// <summary>How many suggestions a lookup-failure message lists at most.</summary>
    /// <remarks>
    /// Capped because there are 247 messages and 300 enum values in the three files; a failure message
    /// that printed all of them would bury the one line the reader needs.
    /// </remarks>
    private const int MaxSuggestions = 12;

    /// <summary>
    /// Shortest shared leading run that still counts a candidate as "close" for a failure message.
    /// </summary>
    private const int MinimumSharedPrefix = 3;

    /// <summary>
    /// <c>common.v1.proto</c> - the shared vocabulary: <c>RetCode</c>, <c>DwBuffer</c>,
    /// <c>ItemStatus</c>, <c>DbError</c>, <c>ConflictDetail</c>. Declares no service.
    /// </summary>
    public static FileDescriptor Common => CommonV1Reflection.Descriptor;

    /// <summary>
    /// <c>dataservices.v1.proto</c> - C-03 <c>DataWindowService</c> and C-04
    /// <c>ColumnExpressionService</c>.
    /// </summary>
    public static FileDescriptor DataServices => DataservicesV1Reflection.Descriptor;

    /// <summary>
    /// <c>persistence.v1.proto</c> - C-05 Query, C-06 Update, C-07 Command and C-08 Transaction.
    /// </summary>
    public static FileDescriptor Persistence => PersistenceV1Reflection.Descriptor;

    /// <summary>
    /// The three published protocol definitions, in dependency order: <see cref="Common"/> first
    /// because both of the others import it, then <see cref="DataServices"/>, then
    /// <see cref="Persistence"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The order is fixed rather than incidental: every enumerator below walks this list, so the order
    /// here is what makes their output deterministic, and a test asserting on a first or single match
    /// depends on that.
    /// </para>
    /// <para>
    /// Written from the generated reflection holders rather than from the three properties above so
    /// that the mapping from proto file to holder type is visible in one place. Grpc.Tools derives each
    /// holder's name from the file name - <c>common.v1.proto</c> to <c>CommonV1Reflection</c>,
    /// <c>dataservices.v1.proto</c> to <c>DataservicesV1Reflection</c> (note the lower-case <c>s</c>,
    /// which follows the file name and not the <c>csharp_namespace</c>), <c>persistence.v1.proto</c> to
    /// <c>PersistenceV1Reflection</c>. Verified against the compiled assembly rather than assumed.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<FileDescriptor> All { get; } =
    [
        CommonV1Reflection.Descriptor,
        DataservicesV1Reflection.Descriptor,
        PersistenceV1Reflection.Descriptor,
    ];

    // ==============================================================================================
    //  ENUMERATORS - the substrate the sweep tests walk
    //
    //  All of them are deterministic: file order follows All, and within a file everything follows
    //  DECLARATION order, which is the order protoc recorded from the .proto text. A sweep test can
    //  therefore report "the third field of the fourth message" and mean something stable.
    // ==============================================================================================

    /// <summary>
    /// Every message declared in the three files, including messages nested inside other messages, at
    /// any depth.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Nesting is a source-organisation choice and not a wire-visible one, so a sweep that reasons
    /// about "every message in the contract" must see nested declarations too - the column-expression
    /// model in particular nests its event bodies inside <c>ColumnExpEvent</c>.
    /// </para>
    /// <para>
    /// <b>Synthetic map-entry messages are included, and that is stated rather than filtered.</b> A
    /// <c>map&lt;K,V&gt;</c> field makes protoc synthesise a nested <c>...Entry</c> message that appears
    /// in <see cref="MessageDescriptor.NestedTypes"/> but was never written in the <c>.proto</c> text;
    /// there are three in this contract set. Silently dropping them would be exactly the kind of quiet
    /// normalisation this file refuses to do, so a test that means "messages as AUTHORED" filters on
    /// <see cref="MessageDescriptor.IsMapEntry"/> itself and can see that it is doing so.
    /// </para>
    /// <para>
    /// Ordering: for each file in <see cref="All"/>, each top-level message in declaration order,
    /// immediately followed by its nested messages depth-first.
    /// </para>
    /// </remarks>
    public static IEnumerable<MessageDescriptor> AllMessages() => All.SelectMany(static file => Flatten(file.MessageTypes));

    /// <summary>
    /// Every enum declared in the three files: the file-level ones first, then the ones nested inside
    /// messages.
    /// </summary>
    /// <remarks>
    /// Both scopes are walked because where an enum is declared is a readability decision. The
    /// numeric alphabets that matter most are split across the two: <c>common.v1.DwBuffer</c> and
    /// <c>common.v1.ItemStatus</c> are file-level, while <c>common.v1.RetCode.Value</c> and the two
    /// other result-code alphabets are nested inside the message that carries them. A test asserting an
    /// alphabet should not have to know which.
    /// </remarks>
    public static IEnumerable<EnumDescriptor> AllEnums()
    {
        foreach (FileDescriptor file in All)
        {
            foreach (EnumDescriptor declaredAtFileScope in file.EnumTypes)
            {
                yield return declaredAtFileScope;
            }

            foreach (MessageDescriptor message in Flatten(file.MessageTypes))
            {
                foreach (EnumDescriptor nested in message.EnumTypes)
                {
                    yield return nested;
                }
            }
        }
    }

    /// <summary>Every field of every message returned by <see cref="AllMessages()"/>.</summary>
    /// <remarks>
    /// In declaration order rather than field-number order, because declaration order is what the
    /// <c>.proto</c> reads like and therefore what a failure message should quote back. Use
    /// <c>message.Fields.InFieldNumberOrder()</c> directly where wire order is the subject. The
    /// synthetic <c>key</c> and <c>value</c> fields of map-entry messages are included for the same
    /// reason they are in <see cref="AllMessages()"/>.
    /// </remarks>
    public static IEnumerable<FieldDescriptor> AllFields() =>
        AllMessages().SelectMany(static message => message.Fields.InDeclarationOrder());

    /// <summary>
    /// Every gRPC service declared in the three files - the six of contracts C-03 through C-08.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A <see cref="ServiceDescriptor"/> here proves the <c>.proto</c> DECLARES the service, and that
    /// is all it proves. It is worth being precise about, because the opposite reading is easy to reach
    /// for and is wrong: a descriptor is deserialized from the <c>FileDescriptorProto</c> protoc embeds
    /// in the generated reflection holder, and that proto carries every <c>service</c> block the file
    /// declares REGARDLESS of the <c>GrpcServices</c> setting. So a build configured
    /// <c>GrpcServices="None"</c> would still return all six services from this method while emitting
    /// no client and no server base at all - the misconfiguration that compiles perfectly and then
    /// fails at wire-up.
    /// </para>
    /// <para>
    /// THE EVIDENCE FOR <c>GrpcServices="Both"</c> IS THE GENERATED CLR TYPES, not the descriptors: the
    /// abstract <c>&lt;Service&gt;Base</c> server half and the <c>&lt;Service&gt;Client</c> client half
    /// nested inside each container. That pairing is asserted directly by
    /// <c>ContractsCarryNoBehaviourTests.TheSixServiceContainersAreTheSixPublishedServicesAndEachHasBothGeneratedHalves</c>,
    /// which is where the claim belongs and where a regression in the build configuration is actually
    /// caught. This method's own contribution is the descriptor side of that comparison.
    /// </para>
    /// <para>
    /// <c>common.v1.proto</c> contributes none by design, since it is shared vocabulary rather than a
    /// service.
    /// </para>
    /// </remarks>
    public static IEnumerable<ServiceDescriptor> AllServices() => All.SelectMany(static file => file.Services);

    /// <summary>Every method of every service returned by <see cref="AllServices()"/>.</summary>
    public static IEnumerable<MethodDescriptor> AllMethods() =>
        AllServices().SelectMany(static service => service.Methods);

    /// <summary>
    /// Every named descriptor in the three files - messages, their fields and oneofs, enums, their
    /// values, services and their methods - as the common <see cref="IDescriptor"/> view.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the substrate for the vocabulary sweeps: <see cref="FindDescriptorsNamedIgnoringCase"/>
    /// and <see cref="FindDescriptorsMentioning"/> both walk it. The C-D compliance sweep is the
    /// motivating case - proving that no identifier anywhere in the published boundary mentions a
    /// deferred service - and it has to cover every kind of name, not just message names, because a
    /// field or a oneof named after a deferred capability would be just as much a partial
    /// implementation of it.
    /// </para>
    /// <para>
    /// Synthetic oneofs are included. proto3 <c>optional</c> makes protoc synthesise a single-field
    /// oneof whose name is the field name with a leading underscore; filter on
    /// <see cref="OneofDescriptor.IsSynthetic"/> where authored oneofs are the subject.
    /// </para>
    /// </remarks>
    public static IEnumerable<IDescriptor> AllDescriptors()
    {
        foreach (MessageDescriptor message in AllMessages())
        {
            yield return message;

            foreach (FieldDescriptor field in message.Fields.InDeclarationOrder())
            {
                yield return field;
            }

            foreach (OneofDescriptor oneof in message.Oneofs)
            {
                yield return oneof;
            }
        }

        foreach (EnumDescriptor enumeration in AllEnums())
        {
            yield return enumeration;

            foreach (EnumValueDescriptor value in enumeration.Values)
            {
                yield return value;
            }
        }

        foreach (ServiceDescriptor service in AllServices())
        {
            yield return service;

            foreach (MethodDescriptor method in service.Methods)
            {
                yield return method;
            }
        }
    }

    // ==============================================================================================
    //  LOOKUPS
    //
    //  Each kind comes as a pair. Find* answers "is it there?" and returns null when it is not, which
    //  is a legitimate thing for a test to assert on - the C-D sweep asserts the ABSENCE of things.
    //  Require* answers "get it, it must be there" and throws a diagnosable assertion failure when it
    //  is not, so a test body can read straight through without a null check.
    // ==============================================================================================

    /// <summary>
    /// Finds the single message matching <paramref name="name"/>, searching every file's top-level
    /// messages and their nested messages recursively.
    /// </summary>
    /// <param name="name">
    /// A fully-qualified proto name (<c>persistence.v1.UpdateResponse</c>), a simple name
    /// (<c>UpdateResponse</c>) or a dot-boundary suffix (<c>ColumnExpEvent.ItemChanged</c>). Compared
    /// ordinally and case-sensitively.
    /// </param>
    /// <returns>The matching descriptor, or <see langword="null"/> when nothing matches.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> is null, empty or whitespace.</exception>
    /// <exception cref="FailException">
    /// More than one message matches. Qualify the name to disambiguate - the message text lists every
    /// candidate's full name. <c>UpdateRequest</c> and <c>UpdateResponse</c> are each declared in both
    /// <c>persistence.v1</c> and <c>dataservices.v1</c>, so this is a live case rather than a
    /// hypothetical one, and returning a first match would silently hand back the wrong contract.
    /// </exception>
    public static MessageDescriptor? FindMessage(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return FindSingleByName("message", AllMessages(), name);
    }

    /// <summary>
    /// <see cref="FindMessage"/>, failing the test with a diagnosable message when nothing matches.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="name"/> is null, empty or whitespace.</exception>
    /// <exception cref="FailException">Nothing matches, or more than one does.</exception>
    public static MessageDescriptor RequireMessage(string name) =>
        FindMessage(name)
        ?? throw FailException.ForFailure(
            BuildNotFoundMessage(
                "message",
                AllFilesDescription,
                name,
                AllMessages().Select(static message => message.FullName)));

    /// <summary>
    /// Finds the single enum matching <paramref name="name"/>, searching file-level enums and the enums
    /// nested inside every message at any depth.
    /// </summary>
    /// <param name="name">
    /// A fully-qualified proto name (<c>common.v1.RetCode.Value</c>), a simple name (<c>DwBuffer</c>)
    /// or a dot-boundary suffix (<c>RetCode.Value</c>). Compared ordinally and case-sensitively.
    /// </param>
    /// <returns>The matching descriptor, or <see langword="null"/> when nothing matches.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> is null, empty or whitespace.</exception>
    /// <exception cref="FailException">
    /// More than one enum matches. The bare name <c>Value</c> matches three enums in
    /// <c>common.v1</c> alone - <c>RetCode.Value</c>, <c>XmlParseStatus.Value</c> and
    /// <c>SqliteResultCode.Value</c> - so addressing a nested enum by its simple name is genuinely
    /// ambiguous here and has to be reported rather than guessed.
    /// </exception>
    public static EnumDescriptor? FindEnum(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return FindSingleByName("enum", AllEnums(), name);
    }

    /// <summary>
    /// <see cref="FindEnum"/>, failing the test with a diagnosable message when nothing matches.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="name"/> is null, empty or whitespace.</exception>
    /// <exception cref="FailException">Nothing matches, or more than one does.</exception>
    public static EnumDescriptor RequireEnum(string name) =>
        FindEnum(name)
        ?? throw FailException.ForFailure(
            BuildNotFoundMessage(
                "enum",
                AllFilesDescription,
                name,
                AllEnums().Select(static enumeration => enumeration.FullName)));

    /// <summary>
    /// Every declaration site of the enum value name <paramref name="valueName"/>, across every enum in
    /// all three files.
    /// </summary>
    /// <param name="valueName">
    /// The value's proto spelling, compared ordinally and case-sensitively against
    /// <see cref="EnumValueDescriptor.Name"/>. The SCREAMING_SNAKE form as authored -
    /// <c>E_INVALID_ARGUMENT</c>, not the generated <c>EInvalidArgument</c>.
    /// </param>
    /// <returns>
    /// Every matching value, in the deterministic order of <see cref="AllEnums()"/> and then of each
    /// enum's declaration order. Empty when the name is declared nowhere.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>This member returns a list rather than a single value on purpose, and it is load-bearing.</b>
    /// The fidelity assertion the legacy constant catalogue needs is not "this name exists" but "this
    /// name is declared EXACTLY ONCE in the whole boundary". A legacy code that appeared in two enums
    /// with two different numbers would be a silent divergence of precisely the kind AAP 0.6.1 warns
    /// about: each side internally consistent, the two disagreeing with each other. Only the full set
    /// of declaration sites can express that, so zero sites and two sites are both answers rather than
    /// failures, and the caller asserts the count it means.
    /// </para>
    /// <para>
    /// Oracle for the catalogue: <c>ws_objects/pfw.shared.pbl.src/retcode.sru</c> - read as
    /// specification only, never at runtime (constraint C-C).
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// <paramref name="valueName"/> is null, empty or whitespace.
    /// </exception>
    public static IReadOnlyList<EnumValueDescriptor> FindEnumValuesNamed(string valueName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(valueName);

        List<EnumValueDescriptor> sites = [];

        foreach (EnumDescriptor enumeration in AllEnums())
        {
            foreach (EnumValueDescriptor value in enumeration.Values)
            {
                if (string.Equals(value.Name, valueName, StringComparison.Ordinal))
                {
                    sites.Add(value);
                }
            }
        }

        return sites;
    }

    /// <summary>Finds a field of <paramref name="message"/> by its proto name.</summary>
    /// <param name="message">The message to search. Only its own fields are considered.</param>
    /// <param name="fieldName">
    /// The field's proto spelling - the <c>snake_case</c> form as authored, compared ordinally and
    /// case-sensitively. The returned descriptor exposes <see cref="FieldDescriptor.JsonName"/> and
    /// <see cref="FieldDescriptor.PropertyName"/> for tests whose subject is the JSON or the CLR
    /// projection; this lookup deliberately does not accept either spelling, because accepting all
    /// three would make a test that passed say nothing about which one the contract actually declares.
    /// </param>
    /// <returns>The matching field, or <see langword="null"/> when the message has no such field.</returns>
    /// <remarks>
    /// No ambiguity is possible: protobuf requires field names to be unique within a message.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="message"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="fieldName"/> is null, empty or whitespace.
    /// </exception>
    public static FieldDescriptor? FindField(MessageDescriptor message, string fieldName)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldName);

        foreach (FieldDescriptor field in message.Fields.InDeclarationOrder())
        {
            if (string.Equals(field.Name, fieldName, StringComparison.Ordinal))
            {
                return field;
            }
        }

        return null;
    }

    /// <summary>
    /// <see cref="FindField"/>, failing the test with a diagnosable message when the field is absent.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="message"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="fieldName"/> is null, empty or whitespace.
    /// </exception>
    /// <exception cref="FailException">The message declares no such field.</exception>
    public static FieldDescriptor RequireField(MessageDescriptor message, string fieldName)
    {
        ArgumentNullException.ThrowIfNull(message);

        return FindField(message, fieldName)
            ?? throw FailException.ForFailure(
                BuildNotFoundMessage(
                    "field",
                    $"message '{message.FullName}'",
                    fieldName,
                    message.Fields.InDeclarationOrder().Select(static field => field.Name)));
    }

    /// <summary>Finds the single gRPC service matching <paramref name="name"/>.</summary>
    /// <param name="name">
    /// A fully-qualified proto name (<c>dataservices.v1.DataWindowService</c>) or a simple name
    /// (<c>DataWindowService</c>). Compared ordinally and case-sensitively.
    /// </param>
    /// <returns>The matching service, or <see langword="null"/> when nothing matches.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> is null, empty or whitespace.</exception>
    /// <exception cref="FailException">More than one service matches.</exception>
    public static ServiceDescriptor? FindService(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return FindSingleByName("service", AllServices(), name);
    }

    /// <summary>
    /// <see cref="FindService"/>, failing the test with a diagnosable message when nothing matches.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="name"/> is null, empty or whitespace.</exception>
    /// <exception cref="FailException">Nothing matches, or more than one does.</exception>
    public static ServiceDescriptor RequireService(string name) =>
        FindService(name)
        ?? throw FailException.ForFailure(
            BuildNotFoundMessage(
                "service",
                AllFilesDescription,
                name,
                AllServices().Select(static service => service.FullName)));

    /// <summary>Finds a method of <paramref name="service"/> by its proto name.</summary>
    /// <param name="service">The service to search.</param>
    /// <param name="methodName">
    /// The method's proto spelling, compared ordinally and case-sensitively.
    /// </param>
    /// <returns>The matching method, or <see langword="null"/> when the service has no such method.</returns>
    /// <remarks>
    /// No ambiguity is possible: protobuf requires method names to be unique within a service. Note
    /// that the same method name recurs ACROSS services on purpose - <c>Reset</c> and
    /// <c>SetAutoCommit</c> each appear on several of the persistence services - which is exactly why
    /// this lookup is scoped to one service rather than searching all of them.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="service"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="methodName"/> is null, empty or whitespace.
    /// </exception>
    public static MethodDescriptor? FindMethod(ServiceDescriptor service, string methodName)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentException.ThrowIfNullOrWhiteSpace(methodName);

        foreach (MethodDescriptor method in service.Methods)
        {
            if (string.Equals(method.Name, methodName, StringComparison.Ordinal))
            {
                return method;
            }
        }

        return null;
    }

    /// <summary>
    /// <see cref="FindMethod"/>, failing the test with a diagnosable message when the method is absent.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="service"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="methodName"/> is null, empty or whitespace.
    /// </exception>
    /// <exception cref="FailException">The service declares no such method.</exception>
    public static MethodDescriptor RequireMethod(ServiceDescriptor service, string methodName)
    {
        ArgumentNullException.ThrowIfNull(service);

        return FindMethod(service, methodName)
            ?? throw FailException.ForFailure(
                BuildNotFoundMessage(
                    "method",
                    $"service '{service.FullName}'",
                    methodName,
                    service.Methods.Select(static method => method.Name)));
    }

    // ==============================================================================================
    //  VOCABULARY HELPERS - the ONLY case-insensitive members, and their names say so
    //
    //  Everything above compares case-sensitively because identity is the subject. These two exist for
    //  the sweep tests whose subject is VOCABULARY: proving that a word appears nowhere in the
    //  published boundary, or finding a descriptor whose spelling a test only half remembers. They are
    //  named for what they do so that neither can be mistaken for an identity lookup at a call site.
    // ==============================================================================================

    /// <summary>
    /// Every descriptor of any kind whose simple name or full name equals <paramref name="name"/>,
    /// ignoring case.
    /// </summary>
    /// <param name="name">The name to match, compared with <see cref="StringComparison.OrdinalIgnoreCase"/>.</param>
    /// <returns>
    /// Every match in the deterministic order of <see cref="AllDescriptors()"/>. Empty when nothing
    /// matches.
    /// </returns>
    /// <remarks>
    /// Useful for proving a NEGATIVE across spellings - that no descriptor is named after a deferred
    /// service under any casing - and for diagnosing a case mismatch in a failing identity lookup.
    /// Never use it as a substitute for <see cref="FindMessage"/> or <see cref="FindEnum"/>: a test
    /// that resolves a descriptor case-insensitively cannot detect a spelling regression, which is the
    /// whole thing AAP 0.4.5.3 asks to be guarded.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="name"/> is null, empty or whitespace.</exception>
    public static IReadOnlyList<IDescriptor> FindDescriptorsNamedIgnoringCase(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        List<IDescriptor> matches = [];

        foreach (IDescriptor descriptor in AllDescriptors())
        {
            if (string.Equals(descriptor.Name, name, StringComparison.OrdinalIgnoreCase)
                || string.Equals(descriptor.FullName, name, StringComparison.OrdinalIgnoreCase))
            {
                matches.Add(descriptor);
            }
        }

        return matches;
    }

    /// <summary>
    /// Every descriptor of any kind whose full name CONTAINS <paramref name="token"/>, ignoring case.
    /// </summary>
    /// <param name="token">
    /// The substring to look for, compared with <see cref="StringComparison.OrdinalIgnoreCase"/>.
    /// </param>
    /// <returns>
    /// Every match in the deterministic order of <see cref="AllDescriptors()"/>. Empty when the token
    /// appears nowhere.
    /// </returns>
    /// <remarks>
    /// This is the C-D compliance instrument. The four deferred services - DesignSystem, Documents,
    /// Integration and ScriptBridge (AAP 0.2.2.2) - receive no code, no test, no container and no
    /// contract, and their only permitted representation anywhere in the system is Gateway's four
    /// reserved routes returning 501. A message, field, oneof, enum, value, service or method in the
    /// published protocol definitions whose name mentioned one of them would be a partial definition of
    /// a forbidden target, so a sweep asserts this returns EMPTY for each of their names. The full name
    /// is searched rather than the simple name so that a package or an enclosing message carrying the
    /// token is caught too.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="token"/> is null, empty or whitespace.</exception>
    public static IReadOnlyList<IDescriptor> FindDescriptorsMentioning(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        List<IDescriptor> matches = [];

        foreach (IDescriptor descriptor in AllDescriptors())
        {
            if (descriptor.FullName.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                matches.Add(descriptor);
            }
        }

        return matches;
    }

    // ==============================================================================================
    //  PRIVATE - matching, and the failure messages that are the point of this class
    // ==============================================================================================

    /// <summary>
    /// Returns the one descriptor matching <paramref name="name"/>, null when none does, and throws a
    /// diagnosable assertion failure when several do.
    /// </summary>
    /// <remarks>
    /// The three accepted spellings - exact full name, exact simple name, and dot-boundary suffix of
    /// the full name - are checked ordinally, so nothing is normalised away. The suffix form is what
    /// makes a nested descriptor addressable as <c>ColumnExpEvent.ItemChanged</c> without spelling out
    /// its package, and the dot is part of the compared text so <c>ItemChanged</c> can never match
    /// <c>DoItemChanged</c>.
    /// </remarks>
    private static T? FindSingleByName<T>(string what, IEnumerable<T> candidates, string name)
        where T : class, IDescriptor
    {
        string dotBoundarySuffix = "." + name;
        List<T> matches = [];

        foreach (T candidate in candidates)
        {
            if (string.Equals(candidate.FullName, name, StringComparison.Ordinal)
                || string.Equals(candidate.Name, name, StringComparison.Ordinal)
                || candidate.FullName.EndsWith(dotBoundarySuffix, StringComparison.Ordinal))
            {
                matches.Add(candidate);
            }
        }

        return matches.Count switch
        {
            0 => null,
            1 => matches[0],
            _ => throw FailException.ForFailure(BuildAmbiguityMessage(what, name, matches)),
        };
    }

    /// <summary>
    /// Names the whole search space of the file-scoped lookups, for use in a failure message.
    /// </summary>
    private static string AllFilesDescription =>
        $"the published protocol definitions {string.Join(", ", All.Select(static file => file.Name))}";

    /// <summary>Builds the failure text for a lookup that found nothing.</summary>
    /// <param name="what">The kind of descriptor sought, lower case and singular: <c>message</c>.</param>
    /// <param name="searchedIn">
    /// The space that was actually searched, phrased to follow "in" - the three files for a file-scoped
    /// lookup, or the one containing message or service for a scoped one. Stated rather than assumed,
    /// because telling a reader that nine field names were "searched across three proto files" is
    /// actively misleading about where to look next.
    /// </param>
    /// <param name="sought">The name that did not resolve.</param>
    /// <param name="availableNames">Every name that WAS available in that space.</param>
    private static string BuildNotFoundMessage(
        string what,
        string searchedIn,
        string sought,
        IEnumerable<string> availableNames)
    {
        List<string> available = [.. availableNames];
        IReadOnlyList<string> closest = RankByProximity(sought, available);

        StringBuilder text = new();
        text.Append(CultureInfo.InvariantCulture, $"No {what} named '{sought}' was found in {searchedIn}. ");
        text.Append(CultureInfo.InvariantCulture, $"{available.Count.ToString(CultureInfo.InvariantCulture)} candidate(s) were searched.");

        if (closest.Count > 0)
        {
            text.Append(CultureInfo.InvariantCulture, $" Closest available: {string.Join(", ", closest)}.");
        }
        else
        {
            text.Append(" No available name resembles it, so this is a missing declaration rather than a misspelling.");
        }

        return text.ToString();
    }

    /// <summary>Builds the failure text for a lookup that found more than one match.</summary>
    private static string BuildAmbiguityMessage(string what, string sought, IEnumerable<IDescriptor> matches)
    {
        string candidates = string.Join(", ", matches.Select(static match => match.FullName));

        return $"The {what} name '{sought}' is ambiguous - it matches {candidates}. "
            + "Pass a fully-qualified proto name, or a longer dot-boundary suffix, to select one. "
            + "This is reported rather than resolved to a first match on purpose: a silently chosen "
            + "candidate would hand the test a descriptor from the wrong contract and still pass.";
    }

    /// <summary>
    /// Orders <paramref name="available"/> by how plausibly each entry is what
    /// <paramref name="sought"/> meant, best first, capped at <see cref="MaxSuggestions"/>.
    /// </summary>
    /// <remarks>
    /// Deterministic by construction: rank first, then ordinal name, so the same miss always produces
    /// the same suggestion list. Suggestions are advisory text only - nothing about the result of a
    /// lookup depends on this ordering.
    /// </remarks>
    private static IReadOnlyList<string> RankByProximity(string sought, IEnumerable<string> available)
    {
        List<(int Rank, string Name)> ranked = [];

        foreach (string candidate in available)
        {
            int rank = ProximityRank(sought, candidate);

            if (rank >= 0)
            {
                ranked.Add((rank, candidate));
            }
        }

        return
        [
            .. ranked
                .OrderBy(static entry => entry.Rank)
                .ThenBy(static entry => entry.Name, StringComparer.Ordinal)
                .Select(static entry => entry.Name)
                .Take(MaxSuggestions),
        ];
    }

    /// <summary>
    /// Ranks one candidate against the sought name - lower is closer, negative means "not close".
    /// </summary>
    /// <remarks>
    /// Rank 0 is a difference of CASE ONLY, promoted above everything else because it is by far the
    /// most common lookup mistake against a contract that mixes PascalCase type names with
    /// SCREAMING_SNAKE value names and snake_case field names. Rank 1 is a containment relation in
    /// either direction, which catches a forgotten or an invented suffix such as <c>UpdateResponse</c>
    /// against <c>Update</c>. Rank 2 is a shared leading run, which catches a mistyped tail.
    /// </remarks>
    private static int ProximityRank(string sought, string candidate)
    {
        string simple = SimpleName(candidate);

        if (string.Equals(simple, sought, StringComparison.OrdinalIgnoreCase)
            || string.Equals(candidate, sought, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (simple.Contains(sought, StringComparison.OrdinalIgnoreCase)
            || sought.Contains(simple, StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        return SharedPrefixLength(simple, sought) >= MinimumSharedPrefix ? 2 : -1;
    }

    /// <summary>The text after the last dot, or the whole string when it has none.</summary>
    private static string SimpleName(string qualifiedName)
    {
        int lastDot = qualifiedName.LastIndexOf('.');

        return lastDot < 0 ? qualifiedName : qualifiedName[(lastDot + 1)..];
    }

    /// <summary>Length of the shared leading run of two names, compared without regard to case.</summary>
    private static int SharedPrefixLength(string left, string right)
    {
        int shared = 0;
        int limit = Math.Min(left.Length, right.Length);

        while (shared < limit
            && char.ToUpperInvariant(left[shared]) == char.ToUpperInvariant(right[shared]))
        {
            shared++;
        }

        return shared;
    }

    /// <summary>Depth-first walk of a message list and everything nested inside it.</summary>
    private static IEnumerable<MessageDescriptor> Flatten(IEnumerable<MessageDescriptor> messages)
    {
        foreach (MessageDescriptor message in messages)
        {
            yield return message;

            foreach (MessageDescriptor nested in Flatten(message.NestedTypes))
            {
                yield return nested;
            }
        }
    }
}

/// <summary>
/// Loads <c>OpenApi/security.v1.yaml</c> and <c>OpenApi/gateway.v1.yaml</c> once for the whole test
/// assembly and exposes each parsed document together with the diagnostic the reader produced for it.
/// </summary>
/// <remarks>
/// <para>
/// <b>How a test class gets hold of it.</b> The fixture is registered once, at the top of this file,
/// with <c>[assembly: AssemblyFixture(typeof(OpenApiContractDocuments))]</c>. xunit.v3 constructs it
/// and awaits <see cref="InitializeAsync"/> before any test in the assembly runs, and disposes it after
/// the last one. A sibling test class receives it by declaring a constructor parameter of exactly this
/// type:
/// </para>
/// <code>
/// public sealed class SomeContractTests(OpenApiContractDocuments documents)
/// {
///     [Fact]
///     public void TheSecurityContractParsesWithoutError() =>
///         Assert.Empty(documents.SecurityDiagnostic.Errors);
/// }
/// </code>
/// <para>
/// <b>That is the one convention for this folder.</b> Please do not add a competing
/// <c>[CollectionDefinition]</c>, a class fixture, or a second registration of this type.
/// <c>AssemblyFixtureAttribute</c> permits multiple registrations, so a duplicate would construct and
/// reparse both documents while looking entirely correct.
/// </para>
/// <para>
/// <b>Assembly scope rather than class or collection scope.</b> gateway.v1.yaml is 4,585 lines and
/// security.v1.yaml 3,460; reparsing them per test class is wasted work, and it also makes a failure
/// noisier, because one malformed document is then reported once per class instead of once.
/// </para>
/// <para>
/// <b>This fixture asserts nothing about the documents, deliberately.</b> It exposes
/// <see cref="SecurityDiagnostic"/> and <see cref="GatewayDiagnostic"/> untouched so that the document
/// validation tests can assert over them - including that <c>Errors</c> and <c>Warnings</c> are both
/// empty, which is an assertion those tests own rather than a precondition this fixture imposes. If the
/// fixture failed on a warning, a test written to characterise that warning could never run. The only
/// conditions it treats as fatal are the ones that leave it with nothing to hand over: an unlocatable
/// file, or a reader that returns no document.
/// </para>
/// <para>
/// <b>Purity and repeatability.</b> No clock, no randomness, no environment variable, no network and no
/// sleep. Loading is entirely offline - see <see cref="CreateOfflineReaderSettings"/> - so a run in CI
/// with no egress produces byte-identical results to a run on a workstation. Access to the filesystem
/// is strictly read-only: <see cref="File.Exists(string)"/> to probe and
/// <see cref="File.OpenRead(string)"/> to read, and nothing in this type creates, writes, moves or
/// deletes anything (constraint C-C).
/// </para>
/// </remarks>
public sealed class OpenApiContractDocuments : IAsyncLifetime
{
    /// <summary>File name of the Security contract document - C-01 TokenService, C-02 CryptoService.</summary>
    public const string SecurityDocumentFileName = "security.v1.yaml";

    /// <summary>
    /// File name of the Gateway contract document - C-09 REST ingress, carrying the C-10 health
    /// contract and the four reserved 501 routes.
    /// </summary>
    public const string GatewayDocumentFileName = "gateway.v1.yaml";

    /// <summary>Folder holding both documents, in the contracts project and in a copy-to-output layout alike.</summary>
    private const string OpenApiDirectoryName = "OpenApi";

    /// <summary>Repository-relative location of the contracts project: <c>shared/PowerFramework.Contracts</c>.</summary>
    private const string SharedDirectoryName = "shared";

    /// <summary>Project folder name; frozen, because every consumer and the root solution name it exactly.</summary>
    private const string ContractsProjectDirectoryName = "PowerFramework.Contracts";

    /// <summary>First half of the repository-root marker pair.</summary>
    private const string SolutionMarkerFileName = "PowerFramework.slnx";

    /// <summary>Second half of the repository-root marker pair.</summary>
    private const string PackageVersionMarkerFileName = "Directory.Packages.props";

    private OpenApiDocument? _security;
    private OpenApiDocument? _gateway;
    private OpenApiDiagnostic? _securityDiagnostic;
    private OpenApiDiagnostic? _gatewayDiagnostic;
    private string? _securityDocumentPath;
    private string? _gatewayDocumentPath;

    /// <summary>The parsed Security contract document.</summary>
    public OpenApiDocument Security => _security ?? throw NotInitialised(nameof(Security));

    /// <summary>The parsed Gateway contract document.</summary>
    public OpenApiDocument Gateway => _gateway ?? throw NotInitialised(nameof(Gateway));

    /// <summary>
    /// The diagnostic the reader produced for the Security document - its errors, warnings, detected
    /// specification version and detected format.
    /// </summary>
    /// <remarks>
    /// Exposed unfiltered. Warnings are not cosmetic in an OpenAPI diagnostic: an unresolved
    /// <c>$ref</c>, a <c>$ref</c> pointing at a component that does not exist, and a construct the
    /// declared specification version does not support all surface as warnings rather than errors, and a
    /// document carrying any of them still parses while making a generated client either skip an
    /// operation or emit something wrong.
    /// </remarks>
    public OpenApiDiagnostic SecurityDiagnostic =>
        _securityDiagnostic ?? throw NotInitialised(nameof(SecurityDiagnostic));

    /// <summary>The diagnostic the reader produced for the Gateway document.</summary>
    public OpenApiDiagnostic GatewayDiagnostic =>
        _gatewayDiagnostic ?? throw NotInitialised(nameof(GatewayDiagnostic));

    /// <summary>
    /// Absolute path the Security document was actually read from, so a failing test can report it.
    /// </summary>
    public string SecurityDocumentPath =>
        _securityDocumentPath ?? throw NotInitialised(nameof(SecurityDocumentPath));

    /// <summary>
    /// Absolute path the Gateway document was actually read from, so a failing test can report it.
    /// </summary>
    public string GatewayDocumentPath =>
        _gatewayDocumentPath ?? throw NotInitialised(nameof(GatewayDocumentPath));

    /// <summary>
    /// Locates and parses both documents. Called once by xunit.v3 before any test in the assembly runs.
    /// </summary>
    /// <exception cref="FailException">
    /// A document could not be located, or the reader returned no document or no diagnostic for it. The
    /// message names every path that was attempted.
    /// </exception>
    public async ValueTask InitializeAsync()
    {
        // TestContext.Current.CancellationToken is threaded into every awaited call that accepts one.
        // That is not a style preference here: xUnit1051 flags a missing token, warnings are errors
        // repository-wide, and honouring it means a cancelled or timed-out run tears this fixture down
        // instead of holding a read handle open.
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        OpenApiReaderSettings settings = CreateOfflineReaderSettings();

        _securityDocumentPath = LocateDocument(SecurityDocumentFileName);
        (_security, _securityDiagnostic) = await ReadDocumentAsync(
            _securityDocumentPath,
            settings,
            cancellationToken);

        _gatewayDocumentPath = LocateDocument(GatewayDocumentFileName);
        (_gateway, _gatewayDiagnostic) = await ReadDocumentAsync(
            _gatewayDocumentPath,
            settings,
            cancellationToken);
    }

    /// <summary>Releases the fixture. Nothing is held open, so there is nothing to release.</summary>
    /// <remarks>
    /// <see cref="IAsyncLifetime"/> derives from <see cref="IAsyncDisposable"/>, so this member is
    /// required rather than optional. Both file handles are closed inside
    /// <see cref="InitializeAsync"/> by the <c>await using</c> that owns each one, and the parsed
    /// documents are plain managed objects, so a completed task is the honest implementation - an empty
    /// body that pretended to do work would be worse than one that says it has none.
    /// </remarks>
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    /// <summary>
    /// Builds reader settings that can parse YAML and cannot reach the network.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why <c>AddYamlReader()</c> has to be called at all.</b> The pinned
    /// <c>Microsoft.OpenApi</c> 2.11.0 assembly ships NO YAML reader - it has
    /// <c>OpenApiJsonReader</c>, <c>OpenApiJsonWriter</c> and <c>OpenApiYamlWriter</c>, and a writer
    /// cannot parse. Measured rather than assumed: a fresh
    /// <see cref="OpenApiReaderSettings"/> registers exactly one reader, <c>json</c>, and after this
    /// call registers three, <c>json</c>, <c>yaml</c> and <c>yml</c>. Both contract documents are YAML,
    /// so without the call every load fails at the format lookup. The reader arrives from the
    /// separately published, exactly version-locked <c>Microsoft.OpenApi.YamlReader</c> 2.11.0, which
    /// this test project references directly and versionlessly; being locked to 2.11.0 it cannot drag
    /// the mandatory <c>Microsoft.OpenApi</c> pin off that version. The pin itself is mandatory in both
    /// directions (AAP 0.5.2): 2.0.0 carries advisory NU1903, and the 3.x line breaks the build with
    /// CS0200 because the 10.0.x OpenAPI source generator is compiled against the 2.x object model.
    /// </para>
    /// <para>
    /// <b>Why loading is pinned offline.</b> <c>LoadExternalRefs</c> is set to false explicitly even
    /// though that is already the default, and no <c>HttpClient</c> is supplied. A document that
    /// silently fetched a remote reference would make these tests depend on network reachability and on
    /// somebody else's server contents, which destroys repeatability - the one hard prerequisite of the
    /// Golden-Master approach this repository adopts (AAP 0.6.7) - and CI carries no network guarantee.
    /// Stating it in code rather than relying on the default also means a future change to that default
    /// cannot quietly open the door.
    /// </para>
    /// <para>
    /// The settings object is built once per fixture and shared by both loads, because it is read-only
    /// configuration once constructed.
    /// </para>
    /// </remarks>
    private static OpenApiReaderSettings CreateOfflineReaderSettings()
    {
        OpenApiReaderSettings settings = new()
        {
            LoadExternalRefs = false,
        };

        settings.AddYamlReader();

        return settings;
    }

    /// <summary>Reads and parses one document from an absolute path.</summary>
    private static async ValueTask<(OpenApiDocument Document, OpenApiDiagnostic Diagnostic)> ReadDocumentAsync(
        string path,
        OpenApiReaderSettings settings,
        CancellationToken cancellationToken)
    {
        // `await using` keeps ownership of the handle here rather than with the reader, so the file is
        // released even when parsing throws. The reader may also close the stream itself; FileStream
        // disposal is idempotent, so the two cannot conflict.
        await using FileStream stream = File.OpenRead(path);

        ReadResult result = await OpenApiDocument.LoadAsync(
            stream,
            OpenApiConstants.Yaml,
            settings,
            cancellationToken);

        // Both members of ReadResult are nullable, so both are checked. A null diagnostic means the
        // reader contract changed under the pinned version, which is worth saying plainly rather than
        // dereferencing through.
        if (result.Diagnostic is null)
        {
            throw FailException.ForFailure(
                $"The OpenAPI reader returned no diagnostic for '{path}'. "
                + "Microsoft.OpenApi 2.11.0 always produces one, so this indicates the reader API "
                + "changed under the central pin.");
        }

        if (result.Document is null)
        {
            string errors = result.Diagnostic.Errors.Count == 0
                ? "the reader reported no error, which usually means the file is empty or is not a YAML mapping"
                : string.Join("; ", result.Diagnostic.Errors.Select(static error => error.ToString()));

            throw FailException.ForFailure(
                $"The OpenAPI document at '{path}' could not be parsed into a document: {errors}.");
        }

        return (result.Document, result.Diagnostic);
    }

    /// <summary>
    /// Resolves the absolute path of one contract document, probing the output directory first and the
    /// contracts project in the source tree second.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why the two documents are located at runtime instead of read from the assembly.</b>
    /// <c>PowerFramework.Contracts.csproj</c> owns the build action of its own items, and this project
    /// must not reach across and change another project's item group to suit a test. So the fixture
    /// resolves the documents where they actually are, in a fixed order, and reports precisely what it
    /// tried when it cannot.
    /// </para>
    /// <para>
    /// <b>Step 1 - output-relative.</b> <c>AppContext.BaseDirectory/OpenApi/&lt;file&gt;.yaml</c>. This
    /// is the resolving probe whenever the contracts project carries the documents as content with
    /// copy-to-output, because content with that setting flows into a referencing project's output
    /// directory. It is anchored on <see cref="AppContext.BaseDirectory"/> and never on the current
    /// working directory, so the result does not depend on where the test command was invoked from.
    /// </para>
    /// <para>
    /// <b>Step 2 - repository-root walk-up.</b> Walk parents of
    /// <see cref="AppContext.BaseDirectory"/> until a directory holds BOTH <c>PowerFramework.slnx</c>
    /// and <c>Directory.Packages.props</c>, then probe
    /// <c>&lt;root&gt;/shared/PowerFramework.Contracts/OpenApi/&lt;file&gt;.yaml</c>. That pair is a
    /// deterministic root marker: the solution file alone is not, because a per-service
    /// <c>.slnx</c> sits in each service directory, and the central package manifest alone is not
    /// either. The walk stops at the filesystem root.
    /// </para>
    /// <para>
    /// <b>Step 3 - fail loudly.</b> Neither resolving throws, naming both attempted absolute paths and
    /// the detected root, or saying that no root was detected. Skipping instead would turn a compliance
    /// test into a no-op that reports success, which for an auditable control is the worst available
    /// outcome.
    /// </para>
    /// <para>
    /// <b>Measured note on the current configuration.</b> The contracts project presently declares
    /// <c>&lt;EmbeddedResource Include="OpenApi/**/*.yaml" /&gt;</c> and explicitly records having
    /// rejected copy-to-output, so today the output directory holds no <c>OpenApi</c> folder and step 1
    /// does not resolve while step 2 does. Step 1 is kept first regardless, because it costs one
    /// <see cref="File.Exists(string)"/> call, it is the probe that keeps working if the documents are
    /// ever published beside the assembly, and preferring the output copy over the source tree is the
    /// right precedence when both exist. The embedded copy is a different subject and is covered
    /// elsewhere: the document tests read the assembly manifest to prove the documents SHIP, while this
    /// fixture reads the authored files to let tests reason about what they SAY.
    /// </para>
    /// </remarks>
    private static string LocateDocument(string fileName)
    {
        string outputRelativePath = Path.Combine(AppContext.BaseDirectory, OpenApiDirectoryName, fileName);

        if (File.Exists(outputRelativePath))
        {
            return outputRelativePath;
        }

        string? repositoryRoot = FindRepositoryRoot(AppContext.BaseDirectory);

        string? sourceRelativePath = repositoryRoot is null
            ? null
            : Path.Combine(
                repositoryRoot,
                SharedDirectoryName,
                ContractsProjectDirectoryName,
                OpenApiDirectoryName,
                fileName);

        if (sourceRelativePath is not null && File.Exists(sourceRelativePath))
        {
            return sourceRelativePath;
        }

        throw FailException.ForFailure(
            BuildLocationFailureMessage(fileName, outputRelativePath, repositoryRoot, sourceRelativePath));
    }

    /// <summary>
    /// Walks <paramref name="startDirectory"/> and its parents for the directory holding both
    /// repository-root markers.
    /// </summary>
    /// <returns>The repository root, or <see langword="null"/> when the walk reaches the filesystem root.</returns>
    private static string? FindRepositoryRoot(string startDirectory)
    {
        DirectoryInfo? candidate = new(startDirectory);

        while (candidate is not null)
        {
            if (File.Exists(Path.Combine(candidate.FullName, SolutionMarkerFileName))
                && File.Exists(Path.Combine(candidate.FullName, PackageVersionMarkerFileName)))
            {
                return candidate.FullName;
            }

            // Parent is null at the filesystem root, which is what terminates the walk.
            candidate = candidate.Parent;
        }

        return null;
    }

    /// <summary>
    /// Builds the failure text for a document that could not be located, naming every path attempted.
    /// </summary>
    private static string BuildLocationFailureMessage(
        string fileName,
        string outputRelativePath,
        string? repositoryRoot,
        string? sourceRelativePath)
    {
        StringBuilder text = new();

        text.Append(CultureInfo.InvariantCulture, $"The OpenAPI contract document '{fileName}' could not be located.");
        text.Append(CultureInfo.InvariantCulture, $" Step 1, output-relative, tried '{outputRelativePath}'.");

        if (repositoryRoot is null)
        {
            text.Append(CultureInfo.InvariantCulture, $" Step 2, repository-root walk-up, found no repository root: no directory from '{AppContext.BaseDirectory}' up to the filesystem root holds both '{SolutionMarkerFileName}' and '{PackageVersionMarkerFileName}', so no source-tree path could be formed.");
        }
        else
        {
            text.Append(CultureInfo.InvariantCulture, $" Step 2, repository-root walk-up, detected the repository root '{repositoryRoot}' and tried '{sourceRelativePath}'.");
        }

        text.Append(CultureInfo.InvariantCulture, $" Expected the document under '{SharedDirectoryName}/{ContractsProjectDirectoryName}/{OpenApiDirectoryName}/'. Both probe paths are absolute and read-only; nothing was created.");

        return text.ToString();
    }

    /// <summary>
    /// The failure raised when a property is read before xunit.v3 has initialised the fixture.
    /// </summary>
    /// <remarks>
    /// A guarded getter is used rather than a null-forgiving backing field, so the diagnosis arrives as
    /// a sentence instead of as a <see cref="NullReferenceException"/> from inside a property. It also
    /// means the file needs no <c>!</c> operator and no suppression to satisfy nullable analysis.
    /// </remarks>
    private static FailException NotInitialised(string memberName) =>
        FailException.ForFailure(
            $"{nameof(OpenApiContractDocuments)}.{memberName} was read before the fixture was initialised. "
            + "Obtain the fixture through a test-class constructor parameter of type "
            + $"{nameof(OpenApiContractDocuments)} so xunit.v3 supplies the initialised instance "
            + "registered by [assembly: AssemblyFixture(typeof(" + nameof(OpenApiContractDocuments) + "))]; "
            + "constructing it directly skips InitializeAsync and leaves it empty.");
}
