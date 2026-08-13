// ==================================================================================================
//  ContractsCarryNoBehaviourTests - THE MECHANICAL CONTROL ON "THE CONTRACTS PROJECT CARRIES NO
//  BEHAVIOUR"
//  ------------------------------------------------------------------------------------------------
//  SUBJECT   The compiled shared/PowerFramework.Contracts assembly, as a whole. Not one contract,
//            not one descriptor - the entire exported CLR surface and the entire set of assemblies
//            it references.
//
//  WHY THIS SUITE EXISTS, AND WHY DELETING IT WOULD BE EXPENSIVE  (constraint C-K)
//  ------------------------------------------------------------------------------------------------
//  ONE PROJECT, FOUR CONSUMERS. PowerFramework.Gateway, PowerFramework.DataServices,
//  PowerFramework.Persistence and PowerFramework.Security all reach this single project by
//  ProjectReference - Gateway and DataServices for the generated client stubs, DataServices and
//  Persistence for the generated server stubs, Security for the OpenAPI-described REST surface.
//  Anything that lands here is therefore, instantly and silently, shared code across all four
//  service boundaries. That is precisely the coupling the whole decomposition exists to prevent:
//  AAP 0.4.2.3 states that this project "carries no behaviour; it is the boundary definition, not a
//  shared-code back door", and the enterprise baseline of AAP 0.7.2 requires "explicitly versioned
//  contracts as the ONLY cross-service coupling - no shared behaviour crosses a service boundary".
//
//  This is not a style preference and this suite is not pedantry. A single static helper added here
//  - a RetCode mapper, a DwBuffer converter, a request validator, a "just one" factory - would be
//  compiled into four independently deployable services at once, and would be the first strand of
//  exactly the fused in-process library this refactor is decomposing. It is the highest-leverage
//  place in the system for a behavioural coupling to enter and never be noticed, because no
//  reviewer is watching a project whose whole point is that it contains nothing.
//
//  THE SUITE'S VALUE IS ENTIRELY IN WHAT IT FORBIDS TOMORROW, NOT IN WHAT IT CONFIRMS TODAY. Every
//  assertion below is written to fail on a FUTURE addition, and every failure message is addressed
//  to the person who made that addition: it names the offender and it names where the thing belongs
//  instead. If you are reading this because a test failed, the message told you the answer.
//
//  THE LEGACY ANCHOR - THIS PROPERTY IS INHERITED, NOT INVENTED
//  ------------------------------------------------------------------------------------------------
//  The legacy return-code catalogue ws_objects/pfw.shared.pbl.src/retcode.sru is 221 lines
//  containing 156 `Constant Long` declarations and NOT ONE function, subroutine or event. It is a
//  declaration-only object: pure vocabulary, zero behaviour. common.v1.proto is its successor and
//  keeps that shape, so "the boundary declares and does not act" is a property carried forward from
//  the oracle rather than a modern convention imposed on it.
//
//  The reason the boundary needs a project at all - and the reason nothing here is a port - is
//  visible in pfw.pbt: it declares a single flat ordered LibList of 32 libraries, and PowerBuilder
//  resolves one global namespace by that ORDER. The legacy has no namespaces, no import statements,
//  no serialization layer and no route table (AAP 0.4.5.1), so there was never a wire format to
//  translate. Every type this assembly exports is generated from a .proto authored for this
//  refactor, which is exactly why "everything exported must be traceable to a descriptor" is a
//  meaningful and complete test rather than an approximation.
//
//  HOW THE CONTROL IS BUILT - THREE INDEPENDENT MECHANISMS, DELIBERATELY NOT ONE
//  ------------------------------------------------------------------------------------------------
//  A single sweep would be one lock on one door. These are three different locks, each of which
//  would catch a back door the other two might let past, and none of which is a restatement of
//  another:
//
//    M1  WHAT THE TYPES ARE.        Every exported type is classified into exactly one category of
//                                   generated artifact, and every generated message and enum is
//                                   cross-checked against the three Protobuf FileDescriptors. A
//                                   hand-written class cannot pass, because it has no descriptor.
//    M2  WHAT THE TYPES EXPOSE.     Per category, the declared public methods, properties and
//                                   fields must be exactly what the generator emits for that
//                                   category - verified against the descriptor, not against a
//                                   wish-list. This is the lock that catches the realistic attack:
//                                   protoc emits `partial` classes, so a hand-written partial with
//                                   a Validate() on a generated message type is the most likely
//                                   form a back door would actually take, and M1 alone would miss
//                                   it because the type is genuinely generated.
//    M3  WHAT THE ASSEMBLY REACHES. The referenced-assembly set. An assembly cannot do what it
//                                   cannot reference: no cryptography, no sockets, no database, no
//                                   configuration, no web host, and above all no other project
//                                   assembly from this repository.
//
//  PURITY - REFLECTION ONLY  (AAP 0.6.7 repeatability)
//  ------------------------------------------------------------------------------------------------
//  No file, no network, no clock, no randomness, no environment variable, no mutable static state.
//  No generated type is INSTANTIATED and no member is INVOKED, with one narrow and deliberate
//  exception: the static `Descriptor` property of a descriptor holder, a generated message and a
//  gRPC service container is read, because reading it is the only way to obtain the descriptor a
//  type must be traceable to. Those getters return a cached immutable descriptor and are the same
//  ones ContractDescriptors reads. Nothing else is called.
//
//  READ-ONLY, AND NOWHERE NEAR THE LEGACY TREE  (constraint C-C)
//  ------------------------------------------------------------------------------------------------
//  This file performs no I/O at all, so it cannot touch ws_objects/**, the *.pbl / *.pbt / *.pbw /
//  *.pbr / *.pbd files, the root native binaries, oldversion/**, pack/**, tests/blink|sciter|webview
//  or the five pre-existing Chinese documents under docs/. The two legacy locators it cites -
//  pfw.pbt and ws_objects/pfw.shared.pbl.src/retcode.sru - appear only as citations in comments.
//
//  WHAT THIS SUITE DOES NOT OWN  (constraint C-D)
//  ------------------------------------------------------------------------------------------------
//  It does not police the deferred-capability VOCABULARY. Two siblings already do, and this file
//  deliberately weakens neither:
//      GatewayContractTests.cs          the four reserved /v1/** routes, their 501 responses, the
//                                       x-deferred-service extension and the machine-readable body.
//      OpenApiContractDocumentTests.cs  that no contract document exists for a deferred service.
//  The one place the two concerns meet is the helper-shaped TYPE-NAME sweep below, which bans
//  suffixes that denote a behaviour home. It bans no capability word, so it can never contradict
//  either sibling: a message named for a deferred capability is their finding to report, not this
//  file's, and a class named RetCodeMapper is this file's regardless of what it is named after.
//
// ==================================================================================================

using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Grpc.Core;
using PowerFramework.Contracts.Common.V1;
using Xunit;

namespace PowerFramework.Contracts.Tests;

/// <summary>
/// Asserts that <c>shared/PowerFramework.Contracts</c> carries no behaviour: that every type it
/// exports is a generated contract artifact traceable to one of the three published Protobuf
/// descriptors, that no type exposes an operation with logic, and that the assembly references
/// nothing it would need in order to do anything.
/// </summary>
/// <remarks>
/// <para>
/// All four services reference this one project, so a behavioural addition here is shared code
/// across four service boundaries the moment it compiles. Every assertion is therefore framed to
/// fail on a future addition - a helper, a mapper, a validator, a configuration reader, an HTTP
/// client, a reference to <c>PowerFramework.Shared.Kernel</c> - and every failure message names the
/// offending member and the place it belongs instead.
/// </para>
/// <para>
/// Reflection only. Nothing is instantiated and no member is invoked other than the static
/// <c>Descriptor</c> getters, which are the sole route to the descriptor a generated type must be
/// traceable to.
/// </para>
/// </remarks>
public sealed class ContractsCarryNoBehaviourTests
{
    // ==============================================================================================
    //  THE SUBJECT
    // ==============================================================================================

    /// <summary>The simple assembly name the subject must have.</summary>
    /// <remarks>
    /// Asserted rather than assumed. <c>PowerFramework.Contracts.csproj</c> sets both
    /// <c>AssemblyName</c> and <c>RootNamespace</c> to this value, and every consumer plus the root
    /// <c>PowerFramework.slnx</c> names the project by that exact path, so a rename would break four
    /// services at once and should be caught here first.
    /// </remarks>
    private const string ContractsAssemblyName = "PowerFramework.Contracts";

    /// <summary>
    /// The prefix every assembly produced by this repository shares.
    /// </summary>
    /// <remarks>
    /// Used to detect a repository project reference generically, so a shared library or service
    /// added after this file was written is caught without editing the list.
    /// </remarks>
    private const string RepositoryAssemblyPrefix = "PowerFramework.";

    /// <summary>
    /// The subject assembly, obtained from a known generated type rather than from a file path.
    /// </summary>
    /// <remarks>
    /// Deriving the handle from <see cref="CommonV1Reflection"/> means the suite reasons about the
    /// assembly the compiler actually bound this test project to, which is the one the four services
    /// bind to as well. Locating it by path would instead test whatever happened to be on disk, and
    /// would break the moment the output layout changed.
    /// </remarks>
    private static readonly Assembly Contracts = typeof(CommonV1Reflection).Assembly;

    /// <summary>
    /// Every exported type of <see cref="Contracts"/>, ordered by full name for determinism.
    /// </summary>
    /// <remarks>
    /// <see cref="Assembly.GetExportedTypes"/> is the right question to ask: a type that is not
    /// exported cannot be consumed by any of the four services and therefore cannot be a shared-code
    /// path, while everything that IS exported is part of the published boundary whether it was
    /// meant to be or not. Nested types are included by this call, which matters because the gRPC
    /// server bases and clients are nested inside their service containers.
    /// </remarks>
    private static readonly IReadOnlyList<Type> ExportedTypes =
        [.. Contracts.GetExportedTypes().OrderBy(static type => type.FullName, StringComparer.Ordinal)];

    /// <summary>
    /// The binding flags every member sweep below uses: public members DECLARED BY THE TYPE ITSELF,
    /// instance and static alike.
    /// </summary>
    /// <remarks>
    /// <see cref="BindingFlags.DeclaredOnly"/> is load-bearing rather than incidental. Without it a
    /// sweep would see every inherited member too - <see cref="object.ToString"/>,
    /// <see cref="object.GetHashCode"/>, and for a gRPC client the whole of
    /// <see cref="ClientBase{T}"/> - which would force a large exemption list of members this
    /// assembly never wrote. Declared-only asks the question that matters: what did the type in THIS
    /// assembly add?
    /// </remarks>
    private const BindingFlags Declared =
        BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    /// <summary>
    /// Collapses a finding list to its distinct entries in a stable order, so that a failure message's
    /// stated count and the lines beneath it always agree.
    /// </summary>
    /// <remarks>
    /// Ordinal throughout. One capability typically surfaces on several members - a stream named by
    /// both a property and its getter, say - and reporting the raw hit count above a de-duplicated list
    /// would read like a truncated list rather than a complete one.
    /// </remarks>
    private static string[] Deduplicate(IEnumerable<string> findings) =>
        [.. findings.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

    /// <summary>The line separator used when a failure message lists several findings.</summary>
    /// <remarks>
    /// A literal rather than <c>Environment.NewLine</c>, for two reasons. It keeps a failure message
    /// byte-identical on every host, which matters for a control whose output gets pasted into a review
    /// or a CI log. And it would be incongruous for the file that forbids <c>System.Environment</c> on
    /// the boundary - see <see cref="AmbientCapabilityTypes"/> - to reach for ambient host state itself
    /// in order to say so.
    /// </remarks>
    private const string FindingSeparator = "\n";

    // ==============================================================================================
    //  THE CATEGORY MODEL
    //
    //  These are the only kinds of type a Grpc.Tools build of Proto/**/*.proto with
    //  GrpcServices="Both" produces. The list was derived by ENUMERATING THE COMPILED ASSEMBLY, not
    //  by reading protoc's documentation, and the census it produced is asserted below so that a
    //  future generator change is a visible, diagnosable failure rather than a silent reclassification.
    // ==============================================================================================

    /// <summary>The kinds of generated artifact this assembly is permitted to export.</summary>
    private enum SurfaceCategory
    {
        /// <summary>
        /// Not a recognised generated artifact. Always a failure: it means something hand-written
        /// with a body has been added to the boundary definition.
        /// </summary>
        Unclassified = 0,

        /// <summary>
        /// A generated Protobuf message: implements <see cref="IMessage"/> and its static
        /// <c>Descriptor</c> is one of the messages declared in the three <c>.proto</c> files.
        /// </summary>
        GeneratedMessage,

        /// <summary>
        /// A generated Protobuf enum: the CLR type of an <see cref="EnumDescriptor"/> in one of the
        /// three <c>.proto</c> files.
        /// </summary>
        GeneratedEnum,

        /// <summary>
        /// The discriminator enum protoc synthesises for a <c>oneof</c>. It has no descriptor of its
        /// own - a <c>oneof</c> is not an enum in the protocol - so it is traced instead to the real
        /// <see cref="OneofDescriptor"/> on the message that declares it.
        /// </summary>
        OneofCaseEnum,

        /// <summary>
        /// A <c>*Reflection</c> holder carrying the <see cref="FileDescriptor"/> for one
        /// <c>.proto</c> file. Exactly three exist, one per file.
        /// </summary>
        DescriptorHolder,

        /// <summary>
        /// The holder protoc emits for a Protobuf <c>extend</c> block: a static class whose only
        /// members are static <see cref="Extension{TTarget,TValue}"/> fields.
        /// </summary>
        /// <remarks>
        /// A Protobuf extension is a custom OPTION on a descriptor - declarative metadata - and has
        /// nothing to do with a C# extension method. The distinction matters because the type is
        /// named <c>CommonV1Extensions</c>, which is exactly what a hand-written helper class would
        /// also be called, so this category is recognised by its member SHAPE and never by its name.
        /// </remarks>
        ProtoExtensionHolder,

        /// <summary>
        /// The nested static <c>Types</c> class protoc emits to scope a message's nested enums and
        /// messages. It declares nothing but nested types.
        /// </summary>
        NestedTypesHolder,

        /// <summary>
        /// A generated gRPC service container: the static class carrying the
        /// <see cref="ServiceDescriptor"/> and the <c>BindService</c> overloads, with the server base
        /// and the client nested inside it.
        /// </summary>
        GrpcServiceContainer,

        /// <summary>The generated abstract server base of a service - the <c>GrpcServices="Both"</c> server half.</summary>
        GrpcServerBase,

        /// <summary>The generated client of a service - the <c>GrpcServices="Both"</c> client half.</summary>
        GrpcClient,
    }

    // ==============================================================================================
    //  DESCRIPTOR INDEXES
    //
    //  Built once from ContractDescriptors, which is the folder's single source of descriptor truth
    //  (ContractTestContext.cs). Going through it rather than re-walking the FileDescriptors here
    //  keeps one definition of "the three published files" in the project, and means a file added to
    //  Proto/ is picked up by this suite the moment ContractDescriptors.All names it.
    // ==============================================================================================

    /// <summary>
    /// Every message declared in the three <c>.proto</c> files, indexed by the CLR type protoc
    /// generated for it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Synthetic map-entry messages are excluded deliberately, and this is the one place in the
    /// project where excluding them is not a normalisation. A <c>map&lt;K,V&gt;</c> field makes protoc
    /// synthesise a nested <c>...Entry</c> descriptor, but it generates NO CLR class for it - the
    /// field surfaces as <see cref="Google.Protobuf.Collections.MapField{TKey,TValue}"/> instead - so
    /// a map entry has no CLR type to index and <see cref="MessageDescriptor.ClrType"/> would have
    /// nothing to return. Keeping them would make the message census disagree with the CLR census by
    /// exactly the number of maps, which is precisely the arithmetic
    /// <see cref="TheGeneratedMessageCensusReconcilesExactlyWithTheDescriptors"/> asserts.
    /// </para>
    /// </remarks>
    private static readonly IReadOnlyDictionary<Type, MessageDescriptor> MessagesByClrType =
        ContractDescriptors.AllMessages()
            .Where(static message => !message.IsMapEntry)
            .ToDictionary(static message => message.ClrType, static message => message);

    /// <summary>Every enum declared in the three <c>.proto</c> files, by generated CLR type.</summary>
    private static readonly IReadOnlyDictionary<Type, EnumDescriptor> EnumsByClrType =
        ContractDescriptors.AllEnums().ToDictionary(static enumeration => enumeration.ClrType, static enumeration => enumeration);

    /// <summary>The three published <see cref="FileDescriptor"/>s, as a set for identity tests.</summary>
    private static readonly IReadOnlySet<FileDescriptor> PublishedFiles = ContractDescriptors.All.ToHashSet();

    /// <summary>The six published <see cref="ServiceDescriptor"/>s, as a set for identity tests.</summary>
    private static readonly IReadOnlySet<ServiceDescriptor> PublishedServices = ContractDescriptors.AllServices().ToHashSet();

    /// <summary>
    /// Every extension AUTHORED in the three <c>.proto</c> files - a Protobuf <c>extend</c> block's
    /// declarations - indexed by field number.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE FIELD NUMBER IS THE IDENTITY, and that is what makes the extension holder traceable rather
    /// than merely plausible. A generated <see cref="Extension{TTarget,TValue}"/> instance carries the
    /// number it was generated for, so a holder field can be matched to the declaration it came from
    /// by reading that number off the live object - not by trusting its name and not by trusting its
    /// generic arguments, either of which a hand-written static field could imitate.
    /// </para>
    /// <para>
    /// <see cref="FieldDescriptor.PropertyName"/> is EMPTY for an extension - measured on this
    /// toolchain, for both declarations - so it cannot be used here the way it is used for a message
    /// field. The generated field name is the PascalCase of the extension's proto name, which is why
    /// <see cref="ToPascalCase"/> appears in the extension trace and nowhere else in the field rules.
    /// </para>
    /// </remarks>
    private static readonly IReadOnlyDictionary<int, FieldDescriptor> PublishedExtensionsByFieldNumber =
        ContractDescriptors.All
            .SelectMany(static file => file.Extensions.UnorderedExtensions)
            .ToDictionary(static extension => extension.FieldNumber, static extension => extension);

    /// <summary>Every exported type, pre-classified once so each theory row is a dictionary hit.</summary>
    /// <remarks>
    /// Computed eagerly rather than per row because <see cref="SurfaceCategory.GrpcServerBase"/> and
    /// <see cref="SurfaceCategory.GrpcClient"/> are recognised partly by their declaring type's
    /// category, and classifying on demand would re-walk that parent for every nested type.
    /// </remarks>
    private static readonly IReadOnlyDictionary<Type, SurfaceCategory> CategoryByType = ClassifyEveryExportedType();

    // ==============================================================================================
    //  THEORY ROWS - ONE PER EXPORTED TYPE
    //
    //  A row per type rather than one aggregate assertion, because the aggregate form reports "some
    //  type failed" and this form reports WHICH. With 324 types that difference is the whole
    //  diagnostic value of the suite.
    //
    //  Rows carry the full name as a string rather than the Type itself: a string round-trips through
    //  xunit's serializer unambiguously, appears verbatim in the test name, and is exactly what a
    //  reader needs in order to find the offender. Assembly.GetType accepts the '+' separator that
    //  Type.FullName uses for nesting, so nested types address correctly.
    // ==============================================================================================

    /// <summary>The full name of every exported type, one theory row each.</summary>
    public static TheoryData<string> EveryExportedType
    {
        get
        {
            TheoryData<string> rows = [];

            foreach (Type type in ExportedTypes)
            {
                // Type.FullName is nullable in the framework's own signature. It returns null only
                // for open generic parameters and a few constructed forms, none of which an assembly
                // can export as a top-level or nested type, so a null here is a genuine anomaly
                // rather than a case to skip silently: name it and let the row fail.
                rows.Add(type.FullName ?? $"<no full name: {type.Name}>");
            }

            return rows;
        }
    }

    // ==============================================================================================
    //  THE CLASSIFIER
    //
    //  Every test below rests on this, so it is worth stating what it will and will not lean on.
    //
    //  CATEGORY TESTS ARE STRUCTURAL, NOT NAME-BASED, WHEREVER THAT IS POSSIBLE - a hand-written type
    //  can be given any name at all, so a name-based classifier would be trivially defeated by the
    //  very thing it is meant to catch. The two places a name is unavoidable are marked and narrow:
    //  the nested `Types` holder and the `*OneofCase` discriminator, both of which protoc names by a
    //  fixed rule, and in BOTH cases the name is only accepted alongside a structural check that a
    //  hand-written type could not satisfy.
    //
    //  IT DELIBERATELY DOES NOT USE GeneratedCodeAttribute. Measured on this toolchain: of the 324
    //  exported types, only the 18 nested `Types` holders carry it. Not one generated message, not one
    //  generated enum, not one service container, not one client and not one descriptor holder does.
    //  A classifier keyed on that attribute would therefore reject 305 legitimate types - and, far
    //  worse, would ACCEPT any hand-written class that declared the attribute itself, since nothing
    //  stops a human from writing [GeneratedCode]. Descriptor traceability cannot be forged that way.
    // ==============================================================================================

    private static Dictionary<Type, SurfaceCategory> ClassifyEveryExportedType()
    {
        Dictionary<Type, SurfaceCategory> categories = [];

        // Pass one classifies everything that can be decided on its own. Service containers must be
        // settled before their nested server base and client, which is why the nested gRPC halves
        // are left to pass two.
        foreach (Type type in ExportedTypes)
        {
            categories[type] = ClassifyIndependently(type);
        }

        // Pass two settles the nested gRPC halves, whose recognition depends on their declaring type
        // already being known to be a service container.
        foreach (Type type in ExportedTypes)
        {
            if (categories[type] is not SurfaceCategory.Unclassified || type.DeclaringType is not { } declaring)
            {
                continue;
            }

            if (!categories.TryGetValue(declaring, out SurfaceCategory parent) || parent is not SurfaceCategory.GrpcServiceContainer)
            {
                continue;
            }

            // THE SERVER BASE is recognised by Grpc.Core.BindServiceMethodAttribute, which the gRPC
            // runtime itself uses to locate the base when binding a service implementation. Nothing
            // hand-written would carry it, and if something did, gRPC would try to bind it.
            if (type.IsAbstract && type.IsDefined(typeof(BindServiceMethodAttribute), inherit: false))
            {
                categories[type] = SurfaceCategory.GrpcServerBase;
                continue;
            }

            // THE CLIENT is recognised by the self-referential curiously-recurring base
            // ClientBase<TSelf>, which protoc emits so that ClientBase.NewInstance can return the
            // derived client type. Requiring the generic argument to be the type itself is what makes
            // this structural: a hand-written class could derive from ClientBase<SomethingElse>, but
            // then it is not this service's generated client and should not pass.
            if (type.BaseType is { IsGenericType: true } baseType
                && baseType.GetGenericTypeDefinition() == typeof(ClientBase<>)
                && baseType.GetGenericArguments()[0] == type)
            {
                categories[type] = SurfaceCategory.GrpcClient;
            }
        }

        return categories;
    }

    private static SurfaceCategory ClassifyIndependently(Type type)
    {
        // A GENERATED MESSAGE. IMessage alone is not enough - a hand-written class may implement it -
        // so the type must additionally BE the CLR type of a message declared in one of the three
        // published files. That is the traceability the whole suite turns on.
        if (typeof(IMessage).IsAssignableFrom(type))
        {
            return MessagesByClrType.ContainsKey(type) ? SurfaceCategory.GeneratedMessage : SurfaceCategory.Unclassified;
        }

        if (type.IsEnum)
        {
            if (EnumsByClrType.ContainsKey(type))
            {
                return SurfaceCategory.GeneratedEnum;
            }

            return IsOneofDiscriminator(type) ? SurfaceCategory.OneofCaseEnum : SurfaceCategory.Unclassified;
        }

        // Everything remaining must be one of the four generated class shapes. Each is recognised by
        // its exact declared-member shape, so a type that adds anything at all to the shape falls
        // through to Unclassified and fails - which is the intended outcome, because "the generated
        // shape plus one extra member" is precisely what a hand-written partial class looks like.
        if (!type.IsClass)
        {
            return SurfaceCategory.Unclassified;
        }

        MethodInfo[] methods = DeclaredMethods(type);
        PropertyInfo[] properties = type.GetProperties(Declared);
        FieldInfo[] fields = type.GetFields(Declared);

        if (IsStaticClass(type))
        {
            if (IsDescriptorHolderShape(type, methods, properties, fields))
            {
                return SurfaceCategory.DescriptorHolder;
            }

            if (IsProtoExtensionHolderShape(type, methods, properties, fields))
            {
                return SurfaceCategory.ProtoExtensionHolder;
            }

            if (IsNestedTypesHolderShape(type, methods, properties, fields))
            {
                return SurfaceCategory.NestedTypesHolder;
            }

            if (IsServiceContainerShape(type, methods, properties, fields))
            {
                return SurfaceCategory.GrpcServiceContainer;
            }
        }

        return SurfaceCategory.Unclassified;
    }

    // ==============================================================================================
    //  SHAPE RECOGNISERS
    //
    //  Each answers one question: is this type EXACTLY the shape protoc emits for that category? Each
    //  is written as a conjunction that includes the negative clauses - "declares no method", "declares
    //  no field" - because those are what make the recogniser refuse a type that has had something
    //  added to it. Dropping a negative clause to "make a build pass" would silently open the back
    //  door this file exists to keep shut.
    // ==============================================================================================

    /// <summary>A C# <c>static class</c>, which the CLR represents as abstract and sealed together.</summary>
    private static bool IsStaticClass(Type type) => type is { IsClass: true, IsAbstract: true, IsSealed: true };

    /// <summary>
    /// The three <c>*Reflection</c> holders: a top-level static class whose single declared member is
    /// a static <c>Descriptor</c> property returning one of the three published
    /// <see cref="FileDescriptor"/>s.
    /// </summary>
    /// <remarks>
    /// Identity against <see cref="PublishedFiles"/> - not merely the property TYPE - is what makes
    /// this structural. A hand-written static class could expose a <see cref="FileDescriptor"/>
    /// property, but it could not expose one of the three descriptors this build produced without
    /// being the holder for one of them.
    /// </remarks>
    private static bool IsDescriptorHolderShape(Type type, MethodInfo[] methods, PropertyInfo[] properties, FieldInfo[] fields) =>
        !type.IsNested
        && methods.Length == 0
        && fields.Length == 0
        && properties.Length == 1
        && properties[0] is { Name: "Descriptor" } descriptor
        && descriptor.PropertyType == typeof(FileDescriptor)
        && descriptor.GetValue(obj: null) is FileDescriptor file
        && PublishedFiles.Contains(file);

    /// <summary>
    /// The holder for a Protobuf <c>extend</c> block: a top-level static class that declares no
    /// method and no property, and whose every declared field is a static
    /// <see cref="Extension{TTarget,TValue}"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE "DECLARES NO METHOD" CLAUSE IS THE ENTIRE CONTROL HERE, and it is the reason this file can
    /// accept a type called <c>CommonV1Extensions</c> without accepting a hand-written
    /// <c>RetCodeExtensions</c>. A Protobuf extension holder is pure declaration - two static fields
    /// describing custom options on <see cref="MethodOptions"/> in this build - whereas a C#
    /// extension-method class exists precisely in order to hold methods. Add one method to this shape
    /// and the type is no longer recognised, falls to <see cref="SurfaceCategory.Unclassified"/>, and
    /// fails.
    /// </para>
    /// <para>
    /// <c>fields.Length &gt; 0</c> is required so that an empty static class cannot slip through this
    /// recogniser on a vacuous "all fields are extensions".
    /// </para>
    /// <para>
    /// EVERY FIELD MUST TRACE TO AN EXTENSION THE PUBLISHED FILES ACTUALLY DECLARE. "Is a static
    /// <see cref="Extension{TTarget,TValue}"/>" is a TYPE test, and a type test is imitable: a
    /// hand-written static class can declare
    /// <c>public static readonly Extension&lt;MethodOptions, string&gt; Whatever = new(60001, ...);</c>
    /// and satisfy it, which would park shared static state on the boundary under a shape this
    /// recogniser had blessed. So the field's live value is read and its
    /// <see cref="Extension.FieldNumber"/> matched against
    /// <see cref="PublishedExtensionsByFieldNumber"/>, which is identity rather than resemblance -
    /// exactly the standard the message, enum and service recognisers already hold themselves to.
    /// </para>
    /// </remarks>
    private static bool IsProtoExtensionHolderShape(Type type, MethodInfo[] methods, PropertyInfo[] properties, FieldInfo[] fields) =>
        !type.IsNested
        && methods.Length == 0
        && properties.Length == 0
        && fields.Length > 0
        && Array.TrueForAll(fields, static field =>
            field.IsStatic
            && field.FieldType.IsGenericType
            && field.FieldType.GetGenericTypeDefinition() == typeof(Extension<,>)
            && field.GetValue(obj: null) is Extension declared
            && PublishedExtensionsByFieldNumber.ContainsKey(declared.FieldNumber));

    /// <summary>
    /// The nested static <c>Types</c> scope protoc emits for a message's nested declarations: it
    /// declares nested types and nothing else.
    /// </summary>
    /// <remarks>
    /// One of the two places a name is part of the test. It is safe here because the name is paired
    /// with three checks a hand-written helper could not satisfy together: the declaring type must
    /// itself be a message traceable to a descriptor, the holder must declare at least one nested
    /// type, and it must declare no method, property or field whatsoever.
    /// </remarks>
    private static bool IsNestedTypesHolderShape(Type type, MethodInfo[] methods, PropertyInfo[] properties, FieldInfo[] fields) =>
        type.IsNested
        && string.Equals(type.Name, "Types", StringComparison.Ordinal)
        && type.DeclaringType is { } declaring
        && MessagesByClrType.ContainsKey(declaring)
        && methods.Length == 0
        && properties.Length == 0
        && fields.Length == 0
        && type.GetNestedTypes(BindingFlags.Public).Length > 0;

    /// <summary>
    /// A generated gRPC service container: a top-level static class exposing exactly one property, a
    /// static <c>Descriptor</c> returning one of the six published <see cref="ServiceDescriptor"/>s,
    /// whose only declared methods are the <c>BindService</c> overloads, and which declares no field.
    /// </summary>
    /// <remarks>
    /// The two <c>BindService</c> overloads are how a service reaches the gRPC runtime - one returns a
    /// <see cref="ServerServiceDefinition"/>, the other registers with a <see cref="ServiceBinderBase"/>
    /// - so they are generator plumbing rather than behaviour. Restricting the method set to that one
    /// name is what stops a helper being parked on the container, which is the most inviting place for
    /// one because the container is already static.
    /// </remarks>
    private static bool IsServiceContainerShape(Type type, MethodInfo[] methods, PropertyInfo[] properties, FieldInfo[] fields) =>
        !type.IsNested
        && fields.Length == 0
        && properties.Length == 1
        && properties[0] is { Name: "Descriptor" } descriptor
        && descriptor.PropertyType == typeof(ServiceDescriptor)
        && descriptor.GetValue(obj: null) is ServiceDescriptor service
        && PublishedServices.Contains(service)
        && Array.TrueForAll(methods, static method => string.Equals(method.Name, "BindService", StringComparison.Ordinal));

    /// <summary>
    /// Whether <paramref name="type"/> is the discriminator enum protoc synthesises for a
    /// <c>oneof</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A <c>oneof</c> is not an enum in the protocol, so this type has no <see cref="EnumDescriptor"/>
    /// and cannot be traced the way a generated enum is. It is traced to the real
    /// <see cref="OneofDescriptor"/> instead: the enum must be nested inside a message that IS
    /// traceable to a descriptor, and its name must be the PascalCase of one of that message's oneof
    /// names with <c>OneofCase</c> appended - protoc's fixed rule, and the second and last place a
    /// name participates in classification.
    /// </para>
    /// <para>
    /// Synthetic oneofs are permitted as anchors: proto3 <c>optional</c> makes protoc synthesise a
    /// single-field oneof, and although it emits no discriminator enum for one today, excluding them
    /// would make this recogniser depend on that generator detail rather than on the protocol.
    /// </para>
    /// </remarks>
    private static bool IsOneofDiscriminator(Type type)
    {
        const string suffix = "OneofCase";

        if (!type.IsNested || !type.Name.EndsWith(suffix, StringComparison.Ordinal))
        {
            return false;
        }

        if (type.DeclaringType is not { } declaring || !MessagesByClrType.TryGetValue(declaring, out MessageDescriptor? message))
        {
            return false;
        }

        string oneofName = type.Name[..^suffix.Length];

        return message.Oneofs.Any(oneof => string.Equals(ToPascalCase(oneof.Name), oneofName, StringComparison.Ordinal));
    }

    /// <summary>
    /// Whether protoc emits the <c>Has&lt;Field&gt;</c> property and the <c>Clear&lt;Field&gt;()</c>
    /// method for <paramref name="field"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE FOUR CLAUSES ARE NOT INTERCHANGEABLE AND EACH WAS MEASURED, because this predicate is what
    /// makes the exact-member tests exact rather than approximately right. Explicit presence is the
    /// necessary condition, and the three exclusions are the generator's own carve-outs: a MESSAGE
    /// field has presence in the protocol yet gets no <c>Has</c> form, because a null reference already
    /// expresses absence; a REPEATED field and a MAP field have no presence to express at all, their
    /// emptiness being indistinguishable from their absence on the wire.
    /// </para>
    /// <para>
    /// It holds uniformly across BOTH kinds of oneof, which is the part worth stating because it is
    /// counter-intuitive. A member of a DECLARED oneof does get its own <c>Has</c> and <c>Clear</c>
    /// forms when it is scalar - <c>dataservices.v1.VarValue.string_value</c> yields
    /// <c>HasStringValue</c> and <c>ClearStringValue</c> alongside <c>KindCase</c> and
    /// <c>ClearKind()</c> - and does not when it is message-typed, which is why
    /// <c>dataservices.v1.EventNotification</c>, whose twenty-two members are all messages, exposes
    /// neither form for any of them. The same predicate covers the synthetic oneof proto3
    /// <c>optional</c> produces, so no separate rule is needed for it.
    /// </para>
    /// </remarks>
    private static bool GeneratesTheHasAndClearForms(FieldDescriptor field) =>
        field.HasPresence
        && field.FieldType != FieldType.Message
        && !field.IsRepeated
        && !field.IsMap;

    /// <summary>
    /// Converts a <c>snake_case</c> Protobuf identifier to the PascalCase member name protoc derives
    /// from it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Reproduces protoc's underscores-to-PascalCase rule: capitalise the first character, capitalise
    /// the character after each underscore, and drop the underscores. <c>column_name</c> becomes
    /// <c>ColumnName</c>, <c>int64_value</c> becomes <c>Int64Value</c>, <c>col_id</c> becomes
    /// <c>ColId</c>.
    /// </para>
    /// <para>
    /// This projection is the reason the sibling suites reason over descriptors rather than over CLR
    /// members: it is lossy in the direction that matters, because it erases the SCREAMING_SNAKE
    /// spellings AAP 0.4.5.3 requires to be preserved verbatim. Reproducing the rule here is
    /// legitimate for the opposite direction - mapping a CLR member back to the descriptor member it
    /// came from - and is used for nothing else in this file.
    /// </para>
    /// </remarks>
    private static string ToPascalCase(string protoName)
    {
        StringBuilder pascal = new(protoName.Length);
        bool capitaliseNext = true;

        foreach (char character in protoName)
        {
            if (character == '_')
            {
                capitaliseNext = true;
                continue;
            }

            pascal.Append(capitaliseNext ? char.ToUpperInvariant(character) : character);
            capitaliseNext = false;
        }

        return pascal.ToString();
    }

    /// <summary>
    /// The public methods a type declares itself, excluding compiler-generated accessors.
    /// </summary>
    /// <remarks>
    /// <see cref="MethodBase.IsSpecialName"/> is what separates a real method from a property getter
    /// or setter. Filtering by the name shape - a <c>get_</c> or <c>set_</c> prefix - would be a
    /// convention check that a hand-written method called <c>get_Something</c> could defeat, whereas
    /// the flag is set by the compiler and cannot be spelled by hand in C#.
    /// </remarks>
    private static MethodInfo[] DeclaredMethods(Type type) =>
        [.. type.GetMethods(Declared).Where(static method => !method.IsSpecialName)];

    /// <summary>Resolves a theory row's type name back to the exported type it names.</summary>
    private static Type ResolveExportedType(string typeName)
    {
        Type? type = Contracts.GetType(typeName, throwOnError: false, ignoreCase: false);

        Assert.True(
            type is not null,
            $"'{typeName}' is a theory row produced from {ContractsAssemblyName}'s own exported-type list, "
                + "yet the assembly no longer resolves it. That indicates the row source and the assembly "
                + "have drifted apart, not a contract problem - fix the row source in this file.");

        return type!;
    }

    /// <summary>The category <paramref name="type"/> was classified into.</summary>
    private static SurfaceCategory CategoryOf(Type type) =>
        CategoryByType.TryGetValue(type, out SurfaceCategory category) ? category : SurfaceCategory.Unclassified;

    /// <summary>Every type of a given category, in the deterministic order of <see cref="ExportedTypes"/>.</summary>
    private static IEnumerable<Type> TypesOf(SurfaceCategory category) =>
        ExportedTypes.Where(type => CategoryOf(type) == category);

    // ==============================================================================================
    //  VACUITY GUARDS
    //
    //  A reflection sweep over an empty set passes every assertion in this file. That is the single
    //  most dangerous failure mode here, because it looks exactly like success: a broken
    //  Grpc.Tools configuration, a renamed assembly or a stale build would produce a green suite that
    //  proves nothing at all. These tests exist so that the sweeps cannot be vacuous, and they run
    //  first for that reason.
    // ==============================================================================================

    [Fact]
    public void TheAssemblyUnderTestIsTheContractsBoundaryItself()
    {
        Assert.Equal(ContractsAssemblyName, Contracts.GetName().Name);

        // The three published namespaces and nothing else. This is a cheap check with a
        // disproportionate reach: a hand-written helper is very likely to arrive in a namespace of its
        // own - PowerFramework.Contracts.Mapping, .Validation, .Extensions - and a fourth namespace
        // appearing here is a finding even before anything is known about the types inside it.
        string[] namespaces =
        [
            .. ExportedTypes
                .Select(static type => type.Namespace ?? "<global namespace>")
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];

        Assert.Equal<string[]>(
            [
                "PowerFramework.Contracts.Common.V1",
                "PowerFramework.Contracts.DataServices.V1",
                "PowerFramework.Contracts.Persistence.V1",
            ],
            namespaces);
    }

    [Fact]
    public void TheExportedSurfaceIsPopulatedSoNoSweepBelowCanPassVacuously()
    {
        Assert.NotEmpty(ExportedTypes);

        // The sweeps must see the whole surface, so the row source and the type list must agree
        // exactly. A row source that silently lost types would weaken every theory below without
        // failing anything.
        Assert.Equal(ExportedTypes.Count, EveryExportedType.Count);

        // ONE KNOWN INSTANCE OF EVERY CATEGORY THAT CARRIES A METHOD SURFACE, named explicitly.
        //
        // A count alone would be satisfied by 324 enums. These four anchors prove the sweeps are
        // actually looking at the kinds of type that could hide behaviour: a message (which protoc
        // emits as `partial`, so it is the likeliest host for a hand-written method), a service
        // container and its client (the only exported types with a genuine operation surface), and a
        // descriptor holder. ConflictDetail and UpdateService are load-bearing contract types rather
        // than arbitrary picks - the C-06 concurrency contract is built from them - so neither can be
        // deleted to make this test pass.
        //
        // NAMING THEM WITH `typeof` RATHER THAN BY STRING IS DELIBERATE, and it buys more than
        // readability: these are COMPILE-TIME bindings, so the regression they guard against cannot
        // even build. Measured by changing the contracts project's GrpcServices from "Both" to
        // "Server" - the plausible "simplification", since it compiles cleanly and fails only later at
        // wire-up - which produced CS0426 on the UpdateServiceClient reference below rather than a test
        // failure. The opposite change, "Client", removes the server base instead and is caught at run
        // time by TheSixServiceContainersAreTheSixPublishedServicesAndEachHasBothGeneratedHalves.
        Assert.Equal(SurfaceCategory.GeneratedMessage, CategoryOf(typeof(ConflictDetail)));
        Assert.Equal(SurfaceCategory.DescriptorHolder, CategoryOf(typeof(CommonV1Reflection)));
        Assert.Equal(SurfaceCategory.GrpcServiceContainer, CategoryOf(typeof(Persistence.V1.UpdateService)));
        Assert.Equal(
            SurfaceCategory.GrpcClient,
            CategoryOf(typeof(Persistence.V1.UpdateService.UpdateServiceClient)));

        // And that the generated enum surface is real, since the alphabets are half of what the
        // boundary publishes.
        Assert.Equal(SurfaceCategory.GeneratedEnum, CategoryOf(typeof(DwBuffer)));
    }

    // ==============================================================================================
    //  M1 - WHAT THE TYPES ARE
    // ==============================================================================================

    [Theory]
    [MemberData(nameof(EveryExportedType))]
    public void EveryExportedTypeIsAGeneratedContractArtifactAndNothingElse(string typeName)
    {
        Type type = ResolveExportedType(typeName);
        SurfaceCategory category = CategoryOf(type);

        Assert.True(
            category is not SurfaceCategory.Unclassified,
            $"""
            {typeName} is exported by {ContractsAssemblyName} but is not a generated contract artifact.

            This project carries NO BEHAVIOUR. It is the boundary definition, not a shared-code back
            door (AAP 0.4.2.3), and all four services - Gateway, DataServices, Persistence and
            Security - reference it. Anything added here is shared code across four service
            boundaries the moment it compiles, which is the coupling this decomposition exists to
            prevent (AAP 0.7.2: versioned contracts are the ONLY permitted cross-service coupling).

            WHERE IT BELONGS INSTEAD:
              * If it is genuinely shared, pure behaviour with no I/O, it belongs in a shared library
                under shared/ - Shared.Kernel for the return-code algebra and primitives,
                Shared.Diagnostics, Shared.Eventful, Shared.Localization or Shared.Containers - and
                the consuming services reference that library directly.
              * If it belongs to one capability, it belongs in the owning service: Gateway,
                DataServices, Persistence or Security.
              * If it is a SHAPE that crosses the wire, it belongs in Proto/*.proto or in
                OpenApi/*.yaml, where the generator will produce it and this test will recognise it.

            Whatever it is, it does not belong in the compiled surface of the boundary project.
            """);
    }

    [Fact]
    public void TheGeneratedMessageCensusReconcilesExactlyWithTheDescriptors()
    {
        // BOTH DIRECTIONS, AS SETS, NOT AS COUNTS.
        //
        // Equal counts would tolerate one message disappearing while one hand-written IMessage
        // appeared. Set equality on the CLR types cannot: every generated message type must be a
        // descriptor's ClrType, and every descriptor must have produced an exported type.
        HashSet<Type> fromClr = [.. TypesOf(SurfaceCategory.GeneratedMessage)];
        HashSet<Type> fromDescriptors = [.. MessagesByClrType.Keys];

        Assert.Equal(fromDescriptors, fromClr);

        // THE MAP-ENTRY ARITHMETIC, STATED SO THE NUMBERS CANNOT DRIFT UNNOTICED.
        //
        // protoc synthesises a nested `...Entry` message descriptor for every map<K,V> field but
        // generates no CLR class for it, projecting the field as MapField<K,V> instead. So the total
        // message-descriptor count exceeds the exported message count by exactly the number of maps,
        // and asserting that identity is what proves the difference is entirely explained by maps
        // rather than by a message that failed to generate.
        int allMessageDescriptors = ContractDescriptors.AllMessages().Count();
        int mapEntries = ContractDescriptors.AllMessages().Count(static message => message.IsMapEntry);

        Assert.Equal(allMessageDescriptors - mapEntries, fromClr.Count);
        Assert.NotEqual(0, mapEntries);
    }

    [Fact]
    public void TheGeneratedEnumCensusReconcilesExactlyWithTheDescriptors()
    {
        HashSet<Type> fromClr = [.. TypesOf(SurfaceCategory.GeneratedEnum)];

        Assert.Equal([.. EnumsByClrType.Keys], fromClr);

        // EVERY REMAINING EXPORTED ENUM MUST BE A ONEOF DISCRIMINATOR, AND MUST NAME A REAL ONEOF.
        //
        // This is the case the classification sweep would otherwise have to take on trust: an enum
        // with no descriptor. protoc emits one per oneof, so each is traced to an actual
        // OneofDescriptor on its declaring message - which is what distinguishes generator output
        // from a hand-written enum that happened to be named *OneofCase.
        foreach (Type discriminator in TypesOf(SurfaceCategory.OneofCaseEnum))
        {
            // The null-forgiving operator is safe here by construction rather than by hope:
            // IsOneofDiscriminator is the only route to this category, and it already required both a
            // non-null DeclaringType and a successful MessagesByClrType lookup on it. Writing the
            // check again would be dead code that read as though the guarantee were in doubt.
            MessageDescriptor message = MessagesByClrType[discriminator.DeclaringType!];
            string oneofName = discriminator.Name[..^"OneofCase".Length];

            Assert.Contains(oneofName, message.Oneofs.Select(oneof => ToPascalCase(oneof.Name)), StringComparer.Ordinal);

            // The `None` member protoc adds for "no case selected" means the discriminator always has
            // one more member than the oneof has fields. Asserting that identity keeps the mapping
            // honest in both directions rather than only checking the name.
            int oneofFields = message.Oneofs
                .Single(oneof => string.Equals(ToPascalCase(oneof.Name), oneofName, StringComparison.Ordinal))
                .Fields.Count;

            Assert.Equal(oneofFields + 1, Enum.GetValues(discriminator).Length);
        }

        Assert.NotEmpty(TypesOf(SurfaceCategory.OneofCaseEnum));
    }

    [Fact]
    public void TheSixServiceContainersAreTheSixPublishedServicesAndEachHasBothGeneratedHalves()
    {
        Type[] containers = [.. TypesOf(SurfaceCategory.GrpcServiceContainer)];

        Assert.Equal(PublishedServices.Count, containers.Length);

        // GrpcServices="Both" is asserted structurally rather than read out of the project file.
        // Either half missing compiles perfectly and then fails at wire-up: a server-only build
        // leaves Gateway with no client to call DataServices with, and a client-only build leaves
        // DataServices and Persistence with no base to implement. One server base and one client per
        // container, nested inside it, is what "Both" means in the output.
        foreach (Type container in containers)
        {
            Type[] nested = container.GetNestedTypes(BindingFlags.Public);

            Assert.Single(nested, type => CategoryOf(type) is SurfaceCategory.GrpcServerBase);
            Assert.Single(nested, type => CategoryOf(type) is SurfaceCategory.GrpcClient);
        }

        Assert.Equal(containers.Length, TypesOf(SurfaceCategory.GrpcServerBase).Count());
        Assert.Equal(containers.Length, TypesOf(SurfaceCategory.GrpcClient).Count());
    }

    // ==============================================================================================
    //  M2 - WHAT THE TYPES EXPOSE
    //
    //  M1 proves each type IS generated. This proves each type exposes only what the generator emits
    //  for its kind, which is a strictly stronger and differently-shaped question. It matters because
    //  protoc emits `partial` classes: the realistic back door is not a brand-new class - that fails
    //  M1 immediately - but a hand-written partial adding Validate(), ToDomain() or IsEmpty to a type
    //  that is genuinely generated and therefore genuinely traceable to a descriptor.
    //
    //  EXEMPTIONS ARE BY CATEGORY AND VERIFIED AGAINST THE DESCRIPTOR, NOT BY A NAME WISH-LIST. The
    //  serialization members are permitted because the type implements IMessage; the Clear members are
    //  permitted only when the descriptor actually declares the oneof or the presence-bearing field
    //  they name; the RPC members are permitted only when the service descriptor actually declares
    //  that method. A flat name list would let a hand-written method through under a generated-looking
    //  name, which is exactly the mistake these rules are shaped to avoid.
    // ==============================================================================================

    /// <summary>
    /// The members every generated Protobuf message declares regardless of its fields, because
    /// <see cref="IMessage"/>, <see cref="IBufferMessage"/>, <see cref="IDeepCloneable{T}"/> and
    /// <see cref="IEquatable{T}"/> require them.
    /// </summary>
    /// <remarks>
    /// This is the one name-based exemption in the file, and it is deliberately narrow: seven names,
    /// each mandated by an interface the type demonstrably implements, and it is only ever applied to
    /// a type already proven to be a generated message traceable to a descriptor. A hand-written
    /// method could only hide here by being called <c>CalculateSize</c>, <c>Clone</c>, <c>Equals</c>,
    /// <c>GetHashCode</c>, <c>MergeFrom</c>, <c>ToString</c> or <c>WriteTo</c> - all of which are
    /// already occupied by the generator on every one of those types.
    /// </remarks>
    private static readonly IReadOnlySet<string> ProtobufMessageProtocol =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "CalculateSize",
            "Clone",
            "Equals",
            "GetHashCode",
            "MergeFrom",
            "ToString",
            "WriteTo",
        };

    /// <summary>
    /// Type-name suffixes that denote a place where behaviour lives, none of which protoc emits.
    /// </summary>
    /// <remarks>
    /// <para>
    /// SUFFIXES, NOT WORDS, AND THE DIFFERENCE IS MEASURED RATHER THAN ASSUMED. Six exported types
    /// already contain the word "Validation" - <c>ValidationSessionState</c>,
    /// <c>OpenValidationSessionRequest</c>, <c>CloseValidationSessionRequest</c> and their siblings -
    /// because the retrieval/validation/update triple is a genuine contract concern (C-03) and the
    /// validation session is a genuine wire shape. Banning the word would ban the contract. What
    /// cannot be a wire shape is an ACTOR: a type whose name ends in <c>Validator</c> is something
    /// that validates, and a boundary definition has nothing to validate with.
    /// </para>
    /// <para>
    /// FOUR PROTOC SUFFIXES ARE DELIBERATELY ABSENT FROM THIS LIST and must stay absent, because
    /// banning them would ban the generator's own output: <c>Service</c> (the six containers),
    /// <c>Client</c> and <c>Base</c> (their two halves), <c>Types</c> (the eighteen nested scopes),
    /// <c>Reflection</c> (the three descriptor holders) and <c>Extensions</c> (the Protobuf
    /// custom-option holder). Those five shapes are policed by their category recognisers instead,
    /// each of which requires the exact generated member shape - so a hand-written
    /// <c>RetCodeExtensions</c> with a method on it fails M1 even though this list lets its name past.
    /// </para>
    /// <para>
    /// The member half of this control is not a name list at all, because it does not need to be: the
    /// per-category rules above already require every declared method to be traceable to a descriptor
    /// or to the <see cref="IMessage"/> protocol, which rejects a hand-written <c>Validate</c>,
    /// <c>Map</c> or <c>Convert</c> without consulting any vocabulary.
    /// </para>
    /// </remarks>
    private static readonly IReadOnlyList<string> BehaviourHomeSuffixes =
    [
        "Validator", "Mapper", "Converter", "Factory", "Helper", "Helpers",
        "Util", "Utils", "Utility", "Utilities", "Builder", "Manager",
        "Adapter", "Handler", "Processor", "Runner", "Executor", "Serializer",
        "Deserializer", "Formatter", "Visitor", "Facade", "Wrapper", "Proxy",
        "Resolver", "Repository", "Logger", "Interceptor", "Middleware",
    ];

    [Theory]
    [MemberData(nameof(EveryExportedType))]
    public void NoExportedTypeDeclaresAMethodItsCategoryDoesNotExplain(string typeName)
    {
        Type type = ResolveExportedType(typeName);
        SurfaceCategory category = CategoryOf(type);

        // An unclassified type is already reported by
        // EveryExportedTypeIsAGeneratedContractArtifactAndNothingElse. Repeating that failure here
        // would double-count one defect and bury the member-level findings, so this row defers.
        if (category is SurfaceCategory.Unclassified)
        {
            return;
        }

        IReadOnlySet<string> permitted = PermittedMethodNames(type, category);

        foreach (MethodInfo method in DeclaredMethods(type))
        {
            Assert.True(
                permitted.Contains(method.Name),
                $"""
                {typeName} declares the public method '{method.Name}', which its category
                ({category}) does not account for.

                {ContractsAssemblyName} carries NO BEHAVIOUR - it is the boundary definition, not a
                shared-code back door (AAP 0.4.2.3) - and all four services reference it, so this
                method is shared code across four service boundaries.

                protoc emits generated messages as `partial` classes, so this is most likely a
                hand-written partial declaration. Move it out:
                  * pure, genuinely shared behaviour -> a library under shared/ (Shared.Kernel,
                    Shared.Diagnostics, Shared.Eventful, Shared.Localization, Shared.Containers);
                  * anything capability-specific -> the owning service (Gateway, DataServices,
                    Persistence, Security);
                  * an operation that should cross the wire -> an rpc in Proto/*.proto or a path in
                    OpenApi/*.yaml, so the generator produces it and this test recognises it.

                Members this category permits: {(permitted.Count == 0 ? "(none)" : string.Join(", ", permitted.Order(StringComparer.Ordinal)))}
                """);
        }
    }

    /// <summary>
    /// The exact set of public method names the generator emits for <paramref name="type"/>, derived
    /// from its descriptor rather than from a fixed list.
    /// </summary>
    private static IReadOnlySet<string> PermittedMethodNames(Type type, SurfaceCategory category)
    {
        switch (category)
        {
            case SurfaceCategory.GeneratedMessage:
            {
                MessageDescriptor message = MessagesByClrType[type];
                HashSet<string> permitted = new(ProtobufMessageProtocol, StringComparer.Ordinal);

                // protoc emits ClearX() for a DECLARED oneof - which resets whichever member is set -
                // and for a field that carries the Has form. Deriving the set from the descriptor means
                // adding a field to the .proto widens it automatically, while a hand-written
                // ClearSomething() with no corresponding field in the protocol is rejected.
                //
                // A SYNTHETIC oneof gets no ClearX() of its own. proto3 `optional` makes protoc
                // synthesise a single-field oneof named `_field`, and the generated member is
                // ClearField() from the FIELD rule below - not ClearField from a oneof whose proto name
                // starts with an underscore. Including synthetic oneofs here would permit a name protoc
                // never emits, which the exactness test would then report as missing.
                foreach (OneofDescriptor oneof in message.Oneofs)
                {
                    if (!oneof.IsSynthetic)
                    {
                        permitted.Add("Clear" + ToPascalCase(oneof.Name));
                    }
                }

                foreach (FieldDescriptor field in message.Fields.InDeclarationOrder())
                {
                    if (GeneratesTheHasAndClearForms(field))
                    {
                        permitted.Add("Clear" + field.PropertyName);
                    }
                }

                return permitted;
            }

            case SurfaceCategory.GrpcServerBase:
            {
                // THE SERVER BASE GETS EXACTLY ONE VIRTUAL METHOD PER RPC, NAMED FOR THE RPC, AND NO
                // Async SPELLING AT ALL. A service implementation overrides these, so an Async name
                // here would be a hand-written member on a type four services derive from.
                HashSet<string> permitted = new(StringComparer.Ordinal);

                foreach (MethodDescriptor rpc in ServiceOfNestedGrpcHalf(type).Methods)
                {
                    permitted.Add(rpc.Name);
                }

                return permitted;
            }

            case SurfaceCategory.GrpcClient:
            {
                // THE Async SPELLING EXISTS ONLY FOR A UNARY RPC, and permitting it for the streaming
                // ones was a real gap: a hand-written RetrieveAsync convenience wrapper on the client -
                // the most tempting place for one, since the client is what services actually hold -
                // would have passed under a blanket "name or name+Async" rule. Measured on this
                // toolchain: a unary rpc yields the blocking form plus an Async form, while a
                // server-streaming, client-streaming or bidirectional rpc yields ONLY the one form,
                // because a stream is inherently asynchronous and its call object is already awaited
                // through its own reader and writer.
                HashSet<string> permitted = new(StringComparer.Ordinal);

                foreach (MethodDescriptor rpc in ServiceOfNestedGrpcHalf(type).Methods)
                {
                    permitted.Add(rpc.Name);

                    if (!rpc.IsClientStreaming && !rpc.IsServerStreaming)
                    {
                        permitted.Add(rpc.Name + "Async");
                    }
                }

                return permitted;
            }

            // The container's only job is to carry the descriptor and to hand the service to the gRPC
            // runtime. Both overloads share the one name.
            case SurfaceCategory.GrpcServiceContainer:
                return new HashSet<string>(StringComparer.Ordinal) { "BindService" };

            // A descriptor holder, a Protobuf extension holder, a nested Types scope and every
            // generated enum declare no method at all. The empty set is the assertion.
            default:
                return new HashSet<string>(StringComparer.Ordinal);
        }
    }

    /// <summary>
    /// The <see cref="ServiceDescriptor"/> of the service container that declares
    /// <paramref name="half"/>.
    /// </summary>
    private static ServiceDescriptor ServiceOfNestedGrpcHalf(Type half)
    {
        // Both halves are nested inside a container already classified as GrpcServiceContainer, and
        // that recogniser required the container to expose a static Descriptor returning one of the
        // six published services. So this read cannot fail for a correctly classified type, and the
        // assertions state that rather than letting a null slip into a comparison and become a
        // confusing NullReferenceException instead of a finding.
        Type container = half.DeclaringType!;
        PropertyInfo? descriptor = container.GetProperty("Descriptor", BindingFlags.Public | BindingFlags.Static);

        Assert.True(
            descriptor is not null,
            $"{container.FullName} was classified as a gRPC service container but exposes no static "
                + "Descriptor property. The classifier and this lookup have diverged - fix this file.");

        ServiceDescriptor? service = descriptor!.GetValue(obj: null) as ServiceDescriptor;

        Assert.True(
            service is not null,
            $"{container.FullName}.Descriptor did not return a ServiceDescriptor. The classifier and "
                + "this lookup have diverged - fix this file.");

        return service!;
    }

    [Theory]
    [MemberData(nameof(EveryExportedType))]
    public void NoExportedTypeDeclaresAPropertyOrFieldItsCategoryDoesNotExplain(string typeName)
    {
        Type type = ResolveExportedType(typeName);
        SurfaceCategory category = CategoryOf(type);

        if (category is SurfaceCategory.Unclassified)
        {
            return;
        }

        // PROPERTIES ARE SWEPT TOO, NOT ONLY METHODS, BECAUSE A GETTER IS A METHOD BODY.
        //
        // `public bool IsValid => Rows.Count > 0 && !string.IsNullOrEmpty(UpdateTable);` declares no
        // method and would sail past a method-only sweep, yet it is a validation rule compiled into
        // four services. A computed property is the quietest possible form of the thing this file
        // exists to prevent, so the property surface is held to the same descriptor-derivation
        // standard as the method surface.
        IReadOnlySet<string> permittedProperties = PermittedPropertyNames(type, category);

        foreach (PropertyInfo property in type.GetProperties(Declared))
        {
            Assert.True(
                permittedProperties.Contains(property.Name),
                $"""
                {typeName} declares the public property '{property.Name}', which its category
                ({category}) does not account for.

                A property getter is a method body. A computed or derived property here is behaviour
                compiled into all four services, and it is harder to spot than a method because it
                reads like data. {ContractsAssemblyName} declares shapes only (AAP 0.4.2.3).

                If it is genuinely part of the wire shape, declare it as a field in Proto/*.proto or
                as a schema property in OpenApi/*.yaml and let the generator emit it. If it is derived
                from other fields, the derivation belongs in the service that needs it.

                Members this category permits: {(permittedProperties.Count == 0 ? "(none)" : string.Join(", ", permittedProperties.Order(StringComparer.Ordinal)))}
                """);
        }

        // FIELDS. Only two shapes are legitimate: protoc's `const int <Field>FieldNumber` on a message,
        // and the static Protobuf Extension<,> descriptors on the custom-option holder. A mutable
        // static field of any other kind would be shared MUTABLE state across four services, which is
        // worse than a helper method - so every other category permits no field whatsoever.
        foreach (FieldInfo field in type.GetFields(Declared))
        {
            // An enum's members are public static literal fields of the enum type. They are the enum,
            // not a field surface, and the alphabets themselves are asserted by ProtoDescriptorTests
            // and RetCodeAgreementTests.
            if (type.IsEnum)
            {
                continue;
            }

            switch (category)
            {
                case SurfaceCategory.GeneratedMessage:
                    AssertIsGeneratedFieldNumberConstant(type, field, typeName);
                    break;

                case SurfaceCategory.ProtoExtensionHolder:
                    Assert.True(
                        field.IsStatic
                        && field.FieldType.IsGenericType
                        && field.FieldType.GetGenericTypeDefinition() == typeof(Extension<,>),
                        $"{typeName} declares the public field '{field.Name}' of type "
                            + $"{field.FieldType.Name}. A Protobuf custom-option holder may declare "
                            + "static Extension<,> descriptors and nothing else; anything else here is "
                            + "shared state across all four services.");
                    break;

                default:
                    Assert.Fail(
                        $"{typeName} declares the public field '{field.Name}', and its category "
                            + $"({category}) permits no field at all. A field on the boundary is shared "
                            + "state across all four services - move it to the owning service, or "
                            + "declare it in Proto/*.proto if it is part of the wire shape.");
                    break;
            }
        }
    }

    /// <summary>
    /// Asserts that <paramref name="field"/> is one of protoc's <c>const int &lt;Field&gt;FieldNumber</c>
    /// declarations, naming a field the message's descriptor actually declares.
    /// </summary>
    private static void AssertIsGeneratedFieldNumberConstant(Type type, FieldInfo field, string typeName)
    {
        const string suffix = "FieldNumber";

        MessageDescriptor message = MessagesByClrType[type];

        // `IsLiteral` means const, so the value is baked into every caller and the field cannot hold
        // state. That is what makes this shape harmless, and it is asserted rather than assumed: a
        // `public static int RowFieldNumber` - mutable, not const - would be shared mutable state
        // across four services under a generated-looking name.
        Assert.True(
            field is { IsLiteral: true, IsStatic: true } && field.FieldType == typeof(int),
            $"{typeName} declares the public field '{field.Name}', which is not a "
                + "`const int`. The only field a generated message declares is protoc's "
                + "`const int <Field>FieldNumber`; anything else is state on the boundary.");

        Assert.True(
            field.Name.EndsWith(suffix, StringComparison.Ordinal),
            $"{typeName} declares the public field '{field.Name}'. A generated message declares only "
                + $"`const int <Field>{suffix}` constants.");

        string stem = field.Name[..^suffix.Length];

        // THE STEM MUST BE EXACTLY ONE FIELD'S PropertyName - not "one of two accepted spellings of
        // it". protoc appends '_' when the generated member would collide with an existing one, and
        // FieldDescriptor.PropertyName already reports the mangled result, so the collision case needs
        // no special handling: `descriptor` on persistence.v1.BeginSessionRequest reports
        // "Descriptor_" and its constant is Descriptor_FieldNumber. Accepting both spellings for every
        // field, as this once did, meant a hand-written `RowCount_FieldNumber` const would pass.
        bool namesADeclaredField = message.Fields
            .InDeclarationOrder()
            .Any(candidate => string.Equals(stem, candidate.PropertyName, StringComparison.Ordinal));

        Assert.True(
            namesADeclaredField,
            $"{typeName} declares the field-number constant '{field.Name}', but "
                + $"{message.FullName} declares no field whose generated member name is '{stem}'. A "
                + "field-number constant with no field behind it is hand-written, not generated.");
    }

    /// <summary>
    /// The exact set of public property names the generator emits for <paramref name="type"/>.
    /// </summary>
    private static IReadOnlySet<string> PermittedPropertyNames(Type type, SurfaceCategory category)
    {
        switch (category)
        {
            case SurfaceCategory.GeneratedMessage:
            {
                MessageDescriptor message = MessagesByClrType[type];

                // Descriptor and Parser are the two members every generated message exposes
                // independently of its fields: the descriptor it was generated from, and the parser
                // Google.Protobuf uses to read it off the wire.
                HashSet<string> permitted = new(StringComparer.Ordinal) { "Descriptor", "Parser" };

                foreach (FieldDescriptor field in message.Fields.InDeclarationOrder())
                {
                    // THE ONE SPELLING THE GENERATOR ACTUALLY EMITS, TAKEN FROM THE DESCRIPTOR.
                    // FieldDescriptor.PropertyName ALREADY CARRIES protoc's collision mangling, so
                    // there is nothing to guess and nothing to permit twice: the two fields in these
                    // contracts that collide - `descriptor` on persistence.v1.BeginSessionRequest and
                    // on persistence.v1.GetTransactionDataResponse, which would clash with the static
                    // Descriptor property - report PropertyName "Descriptor_", and every other field
                    // reports the plain PascalCase form.
                    //
                    // PERMITTING `Foo`, `Foo_`, `HasFoo` and `HasFoo_` for EVERY field would be four
                    // spellings where the generator emits at most two. It would accept `Descriptor` on the
                    // two mangled messages, where protoc emits only `Descriptor_`, and would accept a
                    // trailing-underscore spelling on every authored message, where protoc emits it on two
                    // - so a hand-written `RowCount_` property would pass. Deriving the name from the
                    // descriptor removes the guess entirely.
                    permitted.Add(field.PropertyName);

                    if (GeneratesTheHasAndClearForms(field))
                    {
                        permitted.Add("Has" + field.PropertyName);
                    }
                }

                // A DECLARED oneof gets a discriminator property; a synthetic one does not, for the
                // reason recorded in PermittedMethodNames.
                foreach (OneofDescriptor oneof in message.Oneofs)
                {
                    if (!oneof.IsSynthetic)
                    {
                        permitted.Add(ToPascalCase(oneof.Name) + "Case");
                    }
                }

                return permitted;
            }

            // A descriptor holder and a service container each expose exactly one property, and the
            // category recognisers already required it to return one of the published descriptors.
            case SurfaceCategory.DescriptorHolder:
            case SurfaceCategory.GrpcServiceContainer:
                return new HashSet<string>(StringComparer.Ordinal) { "Descriptor" };

            // The gRPC halves, the custom-option holder, the nested Types scopes and the enums expose
            // no property. A property on a client would be ambient state on a per-call object.
            default:
                return new HashSet<string>(StringComparer.Ordinal);
        }
    }

    // ==============================================================================================
    //  M2 (CONTINUED) - EXACTNESS: THE CONVERSE DIRECTION, THE COUNTS AND THE SIGNATURES
    //
    //  WHAT THE THREE TESTS ABOVE PROVE, AND WHAT THEY DO NOT. Each asserts that every member a type
    //  DECLARES appears in the descriptor-derived permitted set. That is one direction of a set
    //  comparison, and on its own it is a NAME test with two gaps a hand-written partial can walk
    //  through:
    //
    //    * OVERLOADS. `permitted.Contains(method.Name)` is satisfied by ten methods called Clone as
    //      readily as by one. A hand-written `Clone(bool deep)` or `MergeFrom(string json)` declares
    //      a name the generator already emits and would pass unremarked.
    //    * SIGNATURES. A member could keep a generated name and take or return something the
    //      generator never produces - `Equals(string)`, or a `WriteTo` that returns a payload.
    //
    //  The tests in this section close both, and add the three member kinds the sweeps above do not
    //  look at at all: CONSTRUCTORS, EVENTS and OPERATORS. Together with the sweeps above, the member
    //  surface of every exported type is pinned to EXACT SET EQUALITY plus exact arity and exact
    //  signature, rather than to membership.
    //
    //  THE EXPECTATIONS ARE STILL DERIVED FROM DESCRIPTORS, never from a transcript of today's output.
    //  Adding a field to a .proto widens them automatically; adding a hand-written member does not.
    // ==============================================================================================

    /// <summary>
    /// The nine method signatures every generated Protobuf message declares, as
    /// (name, return type, parameter types) where <see langword="null"/> in a type position means "the
    /// message type itself".
    /// </summary>
    /// <remarks>
    /// <para>
    /// SELF-REFERENTIAL POSITIONS ARE MODELLED RATHER THAN LOOSENED. <c>Clone</c> returns the message
    /// type, and <c>Equals</c> and <c>MergeFrom</c> each have a strongly typed overload taking it, so
    /// those positions cannot be written as a fixed <see cref="Type"/>. Encoding them as
    /// <see langword="null"/> and substituting the type under test keeps the assertion exact for every
    /// generated message from one table, instead of degrading to "some overload with the right name".
    /// </para>
    /// <para>
    /// Measured against every message in the assembly, all nine present and none extra. The two
    /// <see cref="IBufferMessage"/> members are absent on purpose and their absence is correct: protoc
    /// emits them as EXPLICIT interface implementations, so they are not public declared members and no
    /// public sweep should expect them.
    /// </para>
    /// </remarks>
    private static readonly IReadOnlyList<(string Name, Type? Returns, Type?[] Parameters)> MessageProtocolSignatures =
    [
        ("Clone", null, []),
        ("Equals", typeof(bool), [typeof(object)]),
        ("Equals", typeof(bool), [null]),
        ("GetHashCode", typeof(int), []),
        ("ToString", typeof(string), []),
        ("WriteTo", typeof(void), [typeof(CodedOutputStream)]),
        ("CalculateSize", typeof(int), []),
        ("MergeFrom", typeof(void), [null]),
        ("MergeFrom", typeof(void), [typeof(CodedInputStream)]),
    ];

    [Theory]
    [MemberData(nameof(EveryExportedType))]
    public void EveryMemberTheDescriptorImpliesIsActuallyDeclared(string typeName)
    {
        Type type = ResolveExportedType(typeName);
        SurfaceCategory category = CategoryOf(type);

        if (category is SurfaceCategory.Unclassified)
        {
            return;
        }

        // THIS IS THE OTHER HALF OF THE SET COMPARISON, and it is not merely symmetry for its own
        // sake. A generated member that has GONE MISSING means the CLR surface and the published
        // descriptors have diverged: either the .proto declares something the shipped stubs do not
        // carry - a stale build, or a Grpc.Tools item that stopped matching the file - or a
        // hand-written partial has shadowed a generated member. Both are contract defects that the
        // "nothing unexplained" direction is structurally unable to see, because a smaller surface
        // trivially satisfies it.
        AssertSetsAgree(
            typeName,
            category,
            "property",
            PermittedPropertyNames(type, category),
            type.GetProperties(Declared).Select(static property => property.Name));

        AssertSetsAgree(
            typeName,
            category,
            "method",
            PermittedMethodNames(type, category),
            DeclaredMethods(type).Select(static method => method.Name));

        // An enum's declared fields ARE its members, which the alphabet suites own; and the extension
        // holder's fields are pinned by name AND by traced identity in
        // EveryExtensionFieldTracesToAnAuthoredExtensionAndEveryAuthoredExtensionHasOneField, which is
        // a stronger statement than a name-set comparison could make. Every other category is compared
        // here.
        if (!type.IsEnum && category is not SurfaceCategory.ProtoExtensionHolder)
        {
            AssertSetsAgree(
                typeName,
                category,
                "field",
                PermittedFieldNames(type, category),
                type.GetFields(Declared).Select(static field => field.Name));
        }
    }

    /// <summary>
    /// Asserts that the members a type declares are EXACTLY the members its descriptor implies -
    /// reporting the missing ones, the extra ones, or both.
    /// </summary>
    /// <param name="typeName">The type under test, for the failure message.</param>
    /// <param name="category">Its category, for the failure message.</param>
    /// <param name="kind">The member kind being compared, singular, for the failure message.</param>
    /// <param name="expected">The descriptor-derived member names.</param>
    /// <param name="declared">The member names the type actually declares.</param>
    private static void AssertSetsAgree(
        string typeName,
        SurfaceCategory category,
        string kind,
        IReadOnlySet<string> expected,
        IEnumerable<string> declared)
    {
        HashSet<string> actual = new(declared, StringComparer.Ordinal);

        string[] missing = Deduplicate(expected.Except(actual, StringComparer.Ordinal));
        string[] extra = Deduplicate(actual.Except(expected, StringComparer.Ordinal));

        Assert.True(
            missing.Length == 0 && extra.Length == 0,
            $"""
            {typeName}'s declared {kind} surface is not the surface its category ({category}) and its
            descriptor imply.

            Missing ({missing.Length}) - implied by the descriptor, not declared by the type:
            {(missing.Length == 0 ? "  (none)" : "  " + string.Join(FindingSeparator + "  ", missing))}

            Extra ({extra.Length}) - declared by the type, not implied by the descriptor:
            {(extra.Length == 0 ? "  (none)" : "  " + string.Join(FindingSeparator + "  ", extra))}

            A MISSING member means the shipped stubs and the published .proto have diverged - a stale
            build, a Grpc.Tools item that no longer matches the file, or a hand-written partial
            shadowing a generated member.

            An EXTRA member means behaviour has been added to the boundary definition, which all four
            services reference ({ContractsAssemblyName} carries none - AAP 0.4.2.3).
            """);
    }

    /// <summary>
    /// The exact set of public field names the generator emits for <paramref name="type"/>.
    /// </summary>
    /// <remarks>
    /// Only a generated message declares a field at all, and only protoc's
    /// <c>const int &lt;Member&gt;FieldNumber</c> constants. The name comes from
    /// <see cref="FieldDescriptor.PropertyName"/> for the reason recorded on
    /// <see cref="AssertIsGeneratedFieldNumberConstant"/>: it already carries the collision mangling,
    /// so the constant for <c>persistence.v1.BeginSessionRequest.descriptor</c> is
    /// <c>Descriptor_FieldNumber</c> and nothing has to be guessed.
    /// </remarks>
    private static IReadOnlySet<string> PermittedFieldNames(Type type, SurfaceCategory category)
    {
        if (category is not SurfaceCategory.GeneratedMessage)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        return MessagesByClrType[type]
            .Fields
            .InDeclarationOrder()
            .Select(static field => field.PropertyName + "FieldNumber")
            .ToHashSet(StringComparer.Ordinal);
    }

    [Theory]
    [MemberData(nameof(EveryExportedType))]
    public void EveryGeneratedMethodHasExactlyTheOverloadCountItsCategoryImplies(string typeName)
    {
        Type type = ResolveExportedType(typeName);
        SurfaceCategory category = CategoryOf(type);

        if (category is SurfaceCategory.Unclassified)
        {
            return;
        }

        foreach (IGrouping<string, MethodInfo> overloads in
            DeclaredMethods(type).GroupBy(static method => method.Name, StringComparer.Ordinal))
        {
            int expected = ExpectedOverloadCount(category, overloads.Key);

            Assert.True(
                overloads.Count() == expected,
                $"""
                {typeName} declares {overloads.Count()} public overload(s) of '{overloads.Key}', and its
                category ({category}) implies exactly {expected}.

                A NAME CHECK CANNOT SEE THIS. The extra overload carries a name the generator already
                emits, so the member sweeps accept it - and it is a hand-written method body shared
                across all four services (AAP 0.4.2.3).

                Declared overloads:
                  {string.Join(FindingSeparator + "  ", Deduplicate(overloads.Select(static method => method.ToString() ?? method.Name)))}
                """);
        }
    }

    /// <summary>
    /// How many public overloads of <paramref name="methodName"/> the generator emits for a type of
    /// <paramref name="category"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Three rules, each measured across the whole assembly rather than assumed. A generated message
    /// declares ONE of everything except <c>Equals</c> and <c>MergeFrom</c>, which have two apiece -
    /// the <see cref="object"/> and strongly typed forms of the first, and the message and
    /// <see cref="CodedInputStream"/> forms of the second. A gRPC client declares TWO of every method,
    /// which are the <c>(request, Metadata, DateTime?, CancellationToken)</c> and
    /// <c>(request, CallOptions)</c> spellings of the same call. Everything else - the server base's
    /// rpc virtuals, the container's two <c>BindService</c> entries under one name - declares ONE.
    /// </para>
    /// <para>
    /// The container is the one place where "one name, two overloads" is the generated shape:
    /// <c>ServerServiceDefinition BindService(TBase)</c> and
    /// <c>void BindService(ServiceBinderBase, TBase)</c>. It is spelled out rather than folded into the
    /// client rule so that neither rule can drift into covering the other.
    /// </para>
    /// </remarks>
    private static int ExpectedOverloadCount(SurfaceCategory category, string methodName) => category switch
    {
        SurfaceCategory.GeneratedMessage when methodName is "Equals" or "MergeFrom" => 2,
        SurfaceCategory.GrpcClient => 2,
        SurfaceCategory.GrpcServiceContainer => 2,
        _ => 1,
    };

    [Theory]
    [MemberData(nameof(EveryExportedType))]
    public void EveryGeneratedMessageDeclaresTheProtobufProtocolWithExactlyTheGeneratedSignatures(string typeName)
    {
        Type type = ResolveExportedType(typeName);

        if (CategoryOf(type) is not SurfaceCategory.GeneratedMessage)
        {
            return;
        }

        MethodInfo[] declared = DeclaredMethods(type);

        foreach ((string name, Type? returns, Type?[] parameters) in MessageProtocolSignatures)
        {
            Type expectedReturn = returns ?? type;
            Type[] expectedParameters = [.. parameters.Select(parameter => parameter ?? type)];

            bool present = declared.Any(method =>
                string.Equals(method.Name, name, StringComparison.Ordinal)
                && method.ReturnType == expectedReturn
                && method.GetParameters().Select(static p => p.ParameterType).SequenceEqual(expectedParameters));

            Assert.True(
                present,
                $"""
                {typeName} does not declare the generated signature
                  {expectedReturn.Name} {name}({string.Join(", ", expectedParameters.Select(static p => p.Name))})

                Every generated Protobuf message declares all nine, because IMessage, IDeepCloneable<T>
                and IEquatable<T> require them. A message that declares the NAME but not the SIGNATURE
                has had that member hand-written or shadowed, which the name-based sweeps cannot detect.

                Declared:
                  {string.Join(FindingSeparator + "  ", Deduplicate(declared.Select(static method => method.ToString() ?? method.Name)))}
                """);
        }
    }

    [Theory]
    [MemberData(nameof(EveryExportedType))]
    public void NoExportedTypeDeclaresAConstructorItsCategoryDoesNotExplain(string typeName)
    {
        Type type = ResolveExportedType(typeName);
        SurfaceCategory category = CategoryOf(type);

        if (category is SurfaceCategory.Unclassified)
        {
            return;
        }

        // CONSTRUCTORS ARE SWEPT AT EVERY ACCESSIBILITY, NOT ONLY PUBLIC. A hand-written internal or
        // private constructor still holds a body, still runs, and on a type four services construct
        // per call it is the quietest place to put initialisation logic - it has no name to notice.
        // The three sweeps above cannot see any of it, because a constructor is neither a method, a
        // property nor a field.
        ConstructorInfo[] constructors = type.GetConstructors(
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        string Rendered() => constructors.Length == 0
            ? "  (none)"
            : "  " + string.Join(
                FindingSeparator + "  ",
                Deduplicate(constructors.Select(c =>
                    $"{(c.IsPublic ? "public" : c.IsFamily ? "protected" : c.IsAssembly ? "internal" : "private")} "
                    + $"({string.Join(", ", c.GetParameters().Select(static p => p.ParameterType.Name))})")));

        switch (category)
        {
            case SurfaceCategory.GeneratedMessage:
                // Exactly two, both public: the parameterless one and the copy constructor protoc emits
                // so that Clone() and `new T(other)` share one implementation.
                Assert.True(
                    constructors.Length == 2
                    && Array.TrueForAll(constructors, static c => c.IsPublic)
                    && constructors.Any(static c => c.GetParameters().Length == 0)
                    && constructors.Any(c => c.GetParameters() is [{ } only] && only.ParameterType == type),
                    $"""
                    {typeName} is a generated message, which declares EXACTLY TWO public constructors -
                    a parameterless one and a copy constructor taking itself - and nothing else.

                    Declared:
                    {Rendered()}
                    """);
                break;

            case SurfaceCategory.GrpcServerBase:
                // One protected parameterless constructor. A parameterised one would mean the base a
                // service implementation derives from now demands a dependency.
                Assert.True(
                    constructors is [{ IsFamily: true } only] && only.GetParameters().Length == 0,
                    $"""
                    {typeName} is a generated gRPC server base, which declares EXACTLY ONE protected
                    parameterless constructor.

                    Declared:
                    {Rendered()}
                    """);
                break;

            case SurfaceCategory.GrpcClient:
                AssertGeneratedClientConstructors(typeName, type, constructors, Rendered);
                break;

            default:
                // A static class - the three descriptor holders, the extension holder, the eighteen
                // nested Types scopes, the six service containers - has no constructor to declare, and
                // an enum has none either. Anything here is hand-written by definition.
                Assert.True(
                    constructors.Length == 0,
                    $"""
                    {typeName}'s category ({category}) is a generated static class or enum, which
                    declares NO constructor at all. A constructor here is a body on the boundary.

                    Declared:
                    {Rendered()}
                    """);
                break;
        }
    }

    /// <summary>
    /// Asserts that <paramref name="constructors"/> are exactly the four a generated gRPC client
    /// declares.
    /// </summary>
    /// <param name="typeName">The client under test, for the failure message.</param>
    /// <param name="client">The client type, used to confirm its base is the gRPC client base.</param>
    /// <param name="constructors">Its declared constructors at every accessibility.</param>
    /// <param name="rendered">Renders the declared set for the failure message.</param>
    /// <remarks>
    /// <para>
    /// THE FOURTH PARAMETER TYPE IS MATCHED BY NAME, AND THAT IS FORCED RATHER THAN CHOSEN.
    /// <c>ClientBase.ClientBaseConfiguration</c> is a PROTECTED nested type in <c>Grpc.Core</c>, so it
    /// cannot be named from outside a derived class at all - there is no <c>typeof</c> to write. The
    /// name comparison is bracketed by three things a hand-written constructor could not fake
    /// together: the type is already classified as a generated client, its base type is
    /// <c>ClientBase&lt;TSelf&gt;</c>, and the constructor is protected, which no caller outside the
    /// generated inheritance chain can invoke.
    /// </para>
    /// <para>
    /// The two public overloads are the wire-up surface a service actually uses - a channel or a call
    /// invoker - and the protected parameterless one exists for test doubles, which is the shape
    /// <c>Grpc.Core</c> documents.
    /// </para>
    /// </remarks>
    private static void AssertGeneratedClientConstructors(
        string typeName,
        Type client,
        ConstructorInfo[] constructors,
        Func<string> rendered)
    {
        bool baseIsGrpcClientBase =
            client.BaseType is { IsGenericType: true } baseType
            && baseType.GetGenericTypeDefinition() == typeof(ClientBase<>);

        bool exact =
            baseIsGrpcClientBase
            && constructors.Length == 4
            && constructors.Any(static c =>
                c.IsPublic && c.GetParameters() is [{ } only] && only.ParameterType == typeof(ChannelBase))
            && constructors.Any(static c =>
                c.IsPublic && c.GetParameters() is [{ } only] && only.ParameterType == typeof(CallInvoker))
            && constructors.Any(static c => c.IsFamily && c.GetParameters().Length == 0)
            && constructors.Any(static c =>
                c.IsFamily
                && c.GetParameters() is [{ } only]
                && string.Equals(only.ParameterType.Name, "ClientBaseConfiguration", StringComparison.Ordinal));

        Assert.True(
            exact,
            $"""
            {typeName} is a generated gRPC client, which declares EXACTLY FOUR constructors: public
            (ChannelBase), public (CallInvoker), protected (), and protected (ClientBaseConfiguration) -
            and derives from ClientBase<TSelf>.

            Base type: {client.BaseType?.Name ?? "(none)"}

            Declared:
            {rendered()}
            """);
    }

    [Fact]
    public void NoExportedTypeDeclaresAnEventAtAnyAccessibility()
    {
        // AN EVENT IS A SUBSCRIPTION SURFACE, WHICH IS BEHAVIOUR AND SHARED MUTABLE STATE AT ONCE: the
        // backing delegate field lives on the type, so on a static type it is shared across all four
        // services, and every handler added to it is a body that runs when the boundary is touched.
        // protoc emits none, at any accessibility, and there is nothing in a wire shape an event could
        // express - so the assertion is simply that the set is empty.
        string[] events = Deduplicate(ExportedTypes.SelectMany(static type => type
            .GetEvents(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Select(declared => $"{CanonicalName(type)}.{declared.Name}")));

        Assert.True(
            events.Length == 0,
            $"""
            {ContractsAssemblyName} declares {events.Length} event(s). A boundary definition declares
            none: an event is a subscription surface - behaviour - and its backing delegate field is
            shared mutable state on a type all four services reference (AAP 0.4.2.3).

            {string.Join(FindingSeparator, events)}
            """);
    }

    [Fact]
    public void NoExportedTypeDeclaresAnOperatorAtAnyAccessibility()
    {
        // AN OPERATOR IS A METHOD THAT DOES NOT LOOK LIKE ONE, which is exactly why it needs its own
        // control: `IsSpecialName` is set on an op_* method, so DeclaredMethods FILTERS IT OUT and the
        // method sweeps above are structurally blind to it. A hand-written
        // `public static bool operator true(DbError e) => e.SqlDbCode != 0;` would be a rule compiled
        // into four services that no other test in this file can see.
        //
        // Equality operators are included in the prohibition rather than exempted. protoc emits none -
        // measured, zero across the whole assembly - and generated messages express equality through
        // Equals, which the signature test pins.
        string[] operators = Deduplicate(ExportedTypes.SelectMany(static type => type
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(static method => method.IsSpecialName
                && method.Name.StartsWith("op_", StringComparison.Ordinal))
            .Select(declared => $"{CanonicalName(type)}.{declared.Name}")));

        Assert.True(
            operators.Length == 0,
            $"""
            {ContractsAssemblyName} declares {operators.Length} operator(s). An operator is a method
            body that the name-based sweeps in this file cannot see, because the compiler marks it
            IsSpecialName. protoc emits none.

            {string.Join(FindingSeparator, operators)}
            """);
    }

    [Fact]
    public void EveryExtensionFieldTracesToAnAuthoredExtensionAndEveryAuthoredExtensionHasOneField()
    {
        // THE BIJECTION IS THE CONTROL. A Protobuf extension holder is the one category whose members
        // are FIELDS, which makes it the one place a static mutable member could sit on the boundary
        // wearing a generated shape - so "every field is an Extension<,>" is not enough. Each field's
        // live value is read and its field number matched to an extension the published files actually
        // declare, and the match is required in BOTH directions: no field without a declaration behind
        // it, and no declaration without exactly one field carrying it.
        List<string> findings = [];

        Dictionary<int, string> claimed = [];

        foreach (Type holder in TypesOf(SurfaceCategory.ProtoExtensionHolder))
        {
            foreach (FieldInfo field in holder.GetFields(Declared))
            {
                string where = $"{CanonicalName(holder)}.{field.Name}";

                if (field.GetValue(obj: null) is not Extension declared)
                {
                    findings.Add($"{where} is not an Extension instance, so it cannot be traced to a declaration.");
                    continue;
                }

                if (!PublishedExtensionsByFieldNumber.TryGetValue(declared.FieldNumber, out FieldDescriptor? authored))
                {
                    findings.Add(
                        $"{where} carries field number {declared.FieldNumber}, which no `extend` block "
                        + "in the published .proto files declares - so it is hand-written state, not a "
                        + "generated custom-option descriptor.");
                    continue;
                }

                if (claimed.TryGetValue(declared.FieldNumber, out string? already))
                {
                    findings.Add(
                        $"{where} and {already} both carry field number {declared.FieldNumber}. An "
                        + "authored extension is generated into exactly one field.");
                    continue;
                }

                claimed[declared.FieldNumber] = where;

                // THE NAME IS DERIVED, NOT ASSUMED. FieldDescriptor.PropertyName is empty for an
                // extension - measured, for both of these - so the generated field name is the
                // PascalCase of the proto name: rich_error -> RichError, legacy_name -> LegacyName.
                string expectedName = ToPascalCase(authored.Name);

                if (!string.Equals(field.Name, expectedName, StringComparison.Ordinal))
                {
                    findings.Add(
                        $"{where} carries the declaration '{authored.FullName}', whose generated field "
                        + $"name is '{expectedName}'. A generated holder field and its declaration "
                        + "cannot disagree about the name.");
                }

                // AND THE EXTENDED TYPE MUST AGREE. Extension<TTarget, TValue>'s first argument is the
                // descriptor the option hangs off, which the declaration states as its extendee - so a
                // field claiming a declaration while extending something else is caught here.
                Type? expectedTarget = authored.ExtendeeType?.ClrType;
                Type actualTarget = field.FieldType.GetGenericArguments()[0];

                if (expectedTarget is not null && actualTarget != expectedTarget)
                {
                    findings.Add(
                        $"{where} is an Extension over {actualTarget.Name}, but '{authored.FullName}' "
                        + $"extends {expectedTarget.Name}.");
                }
            }
        }

        string[] orphans = Deduplicate(PublishedExtensionsByFieldNumber
            .Where(entry => !claimed.ContainsKey(entry.Key))
            .Select(static entry => $"{entry.Value.FullName} (#{entry.Value.FieldNumber})"));

        foreach (string orphan in orphans)
        {
            findings.Add(
                $"The published files declare the extension {orphan}, but no generated holder field "
                + "carries it. The shipped stubs and the published .proto have diverged.");
        }

        // THE VACUITY GUARD. Everything above passes over an empty holder set, so the count is asserted
        // too: these contracts declare two custom options on google.protobuf.MethodOptions, and a build
        // that produced none would otherwise report a green trace over nothing.
        Assert.True(
            PublishedExtensionsByFieldNumber.Count > 0 && claimed.Count == PublishedExtensionsByFieldNumber.Count,
            $"""
            The published extension declarations and the generated holder fields do not correspond
            one-to-one: {PublishedExtensionsByFieldNumber.Count} declared, {claimed.Count} traced.

            {(findings.Count == 0 ? "(no individual finding - the counts alone disagree)" : string.Join(FindingSeparator, Deduplicate(findings)))}
            """);

        Assert.True(
            findings.Count == 0,
            $"""
            {findings.Count} extension-holder finding(s):

            {string.Join(FindingSeparator, Deduplicate(findings))}
            """);
    }

    [Fact]
    public void NoExportedStaticClassIsAHelperClass()
    {
        // A STATIC CLASS IS WHERE A HELPER WOULD ACTUALLY GO, so the static classes are swept as a
        // population in their own right rather than only per type. Twenty-eight exist and every one is
        // generator plumbing: three descriptor holders, eighteen nested Types scopes, one Protobuf
        // custom-option holder and six service containers. Only the containers declare a method at
        // all, and only BindService.
        List<string> findings = [];

        foreach (Type type in ExportedTypes.Where(static type => IsStaticClass(type)))
        {
            SurfaceCategory category = CategoryOf(type);
            MethodInfo[] methods = DeclaredMethods(type);

            if (methods.Length == 0)
            {
                continue;
            }

            if (category is SurfaceCategory.GrpcServiceContainer
                && Array.TrueForAll(methods, static method => string.Equals(method.Name, "BindService", StringComparison.Ordinal)))
            {
                continue;
            }

            findings.Add($"{type.FullName} ({category}): {string.Join(", ", methods.Select(static method => method.Name + "()").Order(StringComparer.Ordinal))}");
        }

        Assert.True(
            findings.Count == 0,
            $"""
            {ContractsAssemblyName} exports {findings.Count} static class(es) carrying a public method
            beyond the generator's own BindService:

            {string.Join(FindingSeparator, findings)}

            A static class with methods on the published boundary is a helper, and a helper here is
            shared behaviour in Gateway, DataServices, Persistence and Security simultaneously. Move it
            to a library under shared/ if it is pure and genuinely shared, or to the owning service
            otherwise.
            """);

        // THE POPULATION MUST BE NON-EMPTY, OR THE SWEEP PROVES NOTHING - and every static-class
        // category must be represented, which is a stronger guard than a bare count and stays robust
        // when a .proto edit changes how many nested Types scopes exist.
        foreach (SurfaceCategory staticCategory in (SurfaceCategory[])
        [
            SurfaceCategory.DescriptorHolder,
            SurfaceCategory.ProtoExtensionHolder,
            SurfaceCategory.NestedTypesHolder,
            SurfaceCategory.GrpcServiceContainer,
        ])
        {
            Assert.Contains(ExportedTypes, type => IsStaticClass(type) && CategoryOf(type) == staticCategory);
        }
    }

    [Fact]
    public void NoExtensionMethodIsPublishedOnTheBoundary()
    {
        // AN EXTENSION METHOD IS THE MOST LIKELY FORM A HELPER WOULD TAKE, because it reads at the call
        // site as though it were part of the contract type: `request.Validate()` or
        // `retCode.ToKernelCode()` looks like the generated surface and would spread through four
        // services before anyone questioned it.
        //
        // Both halves are checked. The compiler marks the containing class AND the method with
        // ExtensionAttribute, and the attribute is compiler-emitted rather than hand-writable in C#, so
        // this is a structural test and not a convention one.
        List<string> findings = [];

        foreach (Type type in ExportedTypes)
        {
            if (type.IsDefined(typeof(ExtensionAttribute), inherit: false))
            {
                findings.Add($"{type.FullName} is marked as containing extension methods");
            }

            foreach (MethodInfo method in DeclaredMethods(type))
            {
                if (method.IsDefined(typeof(ExtensionAttribute), inherit: false))
                {
                    findings.Add($"{type.FullName}.{method.Name}() is an extension method");
                }
            }
        }

        Assert.True(
            findings.Count == 0,
            $"""
            {ContractsAssemblyName} publishes {findings.Count} extension-method declaration(s):

            {string.Join(FindingSeparator, findings)}

            An extension method on a contract type is shared behaviour that reads like part of the
            contract. It belongs in the service that needs it, or - if genuinely shared and pure - in a
            library under shared/ that the consuming services reference directly.

            NOTE FOR THE READER: `CommonV1Extensions` is NOT an extension-method class despite its
            name. It is the holder protoc emits for the Protobuf `extend` block in common.v1.proto, and
            its two members are static Extension<MethodOptions, ...> descriptors - declarative custom
            options, carrying no code. That is why this test asks the compiler-emitted attribute rather
            than reading names.
            """);
    }

    [Fact]
    public void NoTypeNameOnTheBoundaryAnnouncesItselfAsAHomeForBehaviour()
    {
        List<string> findings = [];

        foreach (Type type in ExportedTypes)
        {
            foreach (string suffix in BehaviourHomeSuffixes)
            {
                if (type.Name.EndsWith(suffix, StringComparison.Ordinal))
                {
                    findings.Add($"{type.FullName} (suffix '{suffix}')");
                }
            }
        }

        Assert.True(
            findings.Count == 0,
            $"""
            {ContractsAssemblyName} exports {findings.Count} type(s) whose name denotes an actor that
            performs an operation rather than a shape that crosses the wire:

            {string.Join(FindingSeparator, findings)}

            A boundary definition declares shapes. It has nothing to validate with, nothing to map
            between and nothing to construct, so a type named for doing one of those things does not
            belong here even if it is currently empty - it is an invitation.

            THE CONTRACT VOCABULARY ITSELF IS NOT BANNED, and the distinction is deliberate. Six
            exported types already carry the word "Validation" - ValidationSessionState,
            OpenValidationSessionRequest and their siblings - because the retrieval/validation/update
            triple is a real contract (C-03) and a validation session is a real wire shape. What is
            banned is the ACTOR suffix, not the subject matter.
            """);
    }

    // ==============================================================================================
    //  M2 (CONTINUED) - THE SIGNATURE CLOSURE
    //
    //  A contract definition cannot DO anything, and the sharpest expression of that is the set of
    //  types its public surface mentions. A member cannot write a file without naming a stream, cannot
    //  call a service without naming a client, cannot read a setting without naming a configuration
    //  abstraction and cannot encrypt without naming a cryptographic primitive. Sweeping the closure
    //  therefore catches the whole family at once, and catches it at the declaration rather than
    //  waiting for someone to notice the call.
    //
    //  SCOPE, STATED HONESTLY: this walks the types named by the assembly's OWN declared members, base
    //  types and interfaces - one level, not the transitive API of Google.Protobuf. That is the right
    //  boundary. Google.Protobuf's MessageParser can read a Stream, but that is Google.Protobuf's
    //  surface and every protobuf consumer in existence has it; what matters is that nothing in THIS
    //  assembly puts such a thing in its own signature.
    // ==============================================================================================

    /// <summary>Namespaces the public signature closure is permitted to mention.</summary>
    /// <remarks>
    /// <para>
    /// Measured, not guessed: the closure spans exactly these, plus this assembly's own three contract
    /// namespaces. Note what is absent and therefore caught - <c>System.IO</c> (streams, paths, files),
    /// <c>System.Net</c> and <c>System.Net.Http</c> (sockets, HTTP clients), <c>System.Data</c> and
    /// <c>System.Data.Common</c> (connections, commands), <c>System.Security.Cryptography</c>,
    /// <c>System.Diagnostics</c>, <c>System.Reflection</c>, <c>System.Text.Json</c> and every
    /// <c>Microsoft.*</c> namespace including the configuration, options, logging and dependency
    /// injection abstractions.
    /// </para>
    /// <para>
    /// <c>System</c> itself is permitted as a whole rather than enumerated type by type, because that
    /// is where every Protobuf scalar projection lives - <see cref="bool"/>, <see cref="int"/>,
    /// <see cref="long"/>, <see cref="ulong"/>, <see cref="double"/>, <see cref="string"/> - so
    /// enumerating it would turn an ordinary <c>.proto</c> edit into a false failure in this file. The
    /// ambient primitives that also live in <c>System</c> are handled by
    /// <see cref="AmbientCapabilityTypes"/> instead, which is a deny-list precisely because the
    /// namespace around it has to stay open.
    /// </para>
    /// </remarks>
    private static readonly IReadOnlySet<string> PermittedSignatureNamespaces =
        new HashSet<string>(StringComparer.Ordinal)
        {
            // Protobuf scalar projections, the object protocol, and the enum base and its interfaces.
            "System",

            // CancellationToken on every generated client call, and Task / Task<T> on every server-side
            // rpc. Both are how an asynchronous CONTRACT is expressed; neither starts work by itself.
            "System.Threading",
            "System.Threading.Tasks",

            // The Protobuf runtime: ByteString, the coded streams, the parser, the message interfaces,
            // the repeated and map collections, the descriptor model, and Extension<,>.
            "Google.Protobuf",
            "Google.Protobuf.Collections",
            "Google.Protobuf.Reflection",

            // The gRPC call model: the call-option and metadata types, the stream reader and writer
            // interfaces, ServerCallContext, ClientBase, and the service binding types.
            "Grpc.Core",
        };

    /// <summary>
    /// Types that would give the boundary an ambient capability, and that live inside a namespace which
    /// must stay permitted for other reasons.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the companion deny-list to <see cref="PermittedSignatureNamespaces"/>. Everything here
    /// sits in <c>System</c> or <c>System.Threading</c>, which are open because the Protobuf scalars
    /// and the asynchronous call model live there, so a namespace rule alone would wave these through.
    /// </para>
    /// <para>
    /// <see cref="DateTime"/> is deliberately NOT on this list, and that is a measurement rather than
    /// an oversight: every generated client declares a <c>Nullable&lt;DateTime&gt; deadline</c>
    /// parameter, which is a call option a caller supplies and not a clock the boundary reads. Banning
    /// the type would ban the gRPC call model. A clock READ would arrive as
    /// <see cref="TimeProvider"/> or as one of the framework's clock abstractions, which are banned
    /// here and by the namespace rule respectively - and, more fundamentally, an assembly proven by M1
    /// to consist entirely of generated types has no hand-written body in which to read a clock at all.
    /// </para>
    /// </remarks>
    private static readonly IReadOnlySet<string> AmbientCapabilityTypes =
        new HashSet<string>(StringComparer.Ordinal)
        {
            // Non-determinism. AAP 0.6.7 requires every such source to be seamed for substitution in
            // the services that need one; the boundary needs none.
            "System.Random",
            "System.TimeProvider",

            // Ambient process and host state. Configuration and secrets reach a service through its own
            // options type (AAP 0.3.4), never through the shared boundary - a contract that read a
            // setting would smuggle deployment coupling into all four services at once.
            "System.Environment",
            "System.AppContext",
            "System.AppDomain",
            "System.Console",
            "System.GC",

            // Late-bound construction and invocation. The legacy used dynamic invocation as a
            // variadic-call escape hatch (AAP 0.2.1.4); C# needs no such workaround, and a boundary
            // definition needs no construction at all.
            "System.Activator",

            // Locators. A contract carries identifiers, never a resource to go and fetch.
            "System.Uri",

            // Threads, timers, locks and waits. The legacy encoded thread affinity as a contract
            // (AAP 0.4.5.4) and the services reproduce it as an explicit marshalling boundary; none of
            // that belongs in the shape definitions the four services share.
            "System.Threading.Thread",
            "System.Threading.ThreadPool",
            "System.Threading.Timer",
            "System.Threading.Monitor",
            "System.Threading.Lock",
            "System.Threading.Mutex",
            "System.Threading.Semaphore",
            "System.Threading.SemaphoreSlim",
            "System.Threading.WaitHandle",
            "System.Threading.EventWaitHandle",
            "System.Threading.ManualResetEvent",
            "System.Threading.ManualResetEventSlim",
            "System.Threading.AutoResetEvent",
            "System.Threading.Interlocked",
            "System.Threading.SynchronizationContext",
        };

    [Fact]
    public void NoExportedTypeMentionsIoNetworkDatabaseConfigurationOrAmbientStateInItsSignature()
    {
        List<string> findings = [];

        foreach ((Type mentioned, string where) in WalkPublicSignatureClosure())
        {
            string canonical = CanonicalName(mentioned);
            string? mentionedNamespace = mentioned.Namespace;

            // Our own contract types are permitted by construction - they are what the boundary is made
            // of - but they must be OUR namespace. A PowerFramework type from any other namespace,
            // Shared.Kernel above all, is a project reference in disguise.
            if (mentionedNamespace is not null && mentionedNamespace.StartsWith(RepositoryAssemblyPrefix, StringComparison.Ordinal))
            {
                if (!mentionedNamespace.StartsWith(ContractsAssemblyName + ".", StringComparison.Ordinal))
                {
                    findings.Add($"{where} mentions {canonical}, which belongs to another project in this repository");
                }

                continue;
            }

            if (AmbientCapabilityTypes.Contains(canonical))
            {
                findings.Add($"{where} mentions {canonical}, an ambient capability the boundary must not hold");
                continue;
            }

            if (!PermittedSignatureNamespaces.Contains(mentionedNamespace ?? "<global namespace>"))
            {
                findings.Add($"{where} mentions {canonical}, whose namespace '{mentionedNamespace ?? "<global namespace>"}' is outside the permitted set");
            }
        }

        // De-duplicated once, so the count in the message agrees with the list beneath it. The same
        // capability typically shows up on several members, and reporting "mentions 12" above three
        // lines would read like a truncated list.
        string[] distinct = Deduplicate(findings);

        Assert.True(
            distinct.Length == 0,
            $"""
            The public surface of {ContractsAssemblyName} mentions {distinct.Length} type(s) a boundary
            definition has no business naming:

            {string.Join(FindingSeparator, distinct)}

            A contract definition cannot DO anything, and that is enforced here at the declaration: a
            member cannot write a file without naming a stream, call a service without naming a client,
            read a setting without naming a configuration abstraction, or encrypt without naming a
            cryptographic primitive. All four services reference this project, so any of those would be
            a capability handed to all four at once.

            Permitted namespaces: {string.Join(", ", PermittedSignatureNamespaces.Order(StringComparer.Ordinal))},
            plus this assembly's own {ContractsAssemblyName}.*.V1.
            """);

        // The walk must actually have visited something.
        Assert.NotEmpty(WalkPublicSignatureClosure());
    }

    [Fact]
    public void NoExportedTypeExposesACryptographicOperationEvenThoughTheVocabularyIsLegitimate()
    {
        // THE VOCABULARY IS LEGITIMATE HERE; THE OPERATIONS ARE NOT. This is exactly the distinction a
        // later reader is most likely to get backwards, so it is asserted rather than only described.
        //
        // OpenApi/security.v1.yaml is part of this same boundary and carries the C-02 crypto surface,
        // including sixty-three CRYPTO_* identifiers - CRYPTO_HASH_MD5, CRYPTO_SYMCRYPT_TYPE_AES256,
        // CRYPTO_RSA_BITS_1024 and the rest - whose SCREAMING_SNAKE spellings AAP 0.4.5.3 requires to
        // be preserved verbatim because they appear in stored characterization recordings. A
        // name-based crypto ban would therefore attack the contract itself. The three .proto files
        // happen to contain no crypto identifier today, but a future CryptoRequest message for C-02
        // would be a perfectly legitimate addition to this project.
        //
        // What must never appear is a cryptographic OPERATION: an algorithm object, a key, a hash
        // transform, a random generator. Security owns those, behind SecurityOptions and its configured
        // key store, and C-02's contract-level rule is that raw key material never crosses the wire at
        // all - callers pass an opaque keyRef. So the test is over types, never over names.
        const string cryptographyNamespacePrefix = "System.Security";

        List<string> findings = [];

        foreach ((Type mentioned, string where) in WalkPublicSignatureClosure())
        {
            if (mentioned.Namespace is { } mentionedNamespace
                && mentionedNamespace.StartsWith(cryptographyNamespacePrefix, StringComparison.Ordinal))
            {
                findings.Add($"{where} mentions {CanonicalName(mentioned)}");
            }
        }

        string[] distinct = Deduplicate(findings);

        Assert.True(
            distinct.Length == 0,
            $"""
            The public surface of {ContractsAssemblyName} mentions {distinct.Length} cryptographic type(s):

            {string.Join(FindingSeparator, distinct)}

            Cryptography belongs to PowerFramework.Security, which is the sole token issuer and the only
            holder of key material (AAP 0.6.6.3). A cryptographic operation on the shared boundary would
            hand the capability to Gateway, DataServices and Persistence as well.

            The crypto VOCABULARY is not the target of this test and must not become one: the CRYPTO_*
            identifiers in OpenApi/security.v1.yaml are the published C-02 contract and their exact
            spellings are required by AAP 0.4.5.3.
            """);
    }

    /// <summary>
    /// Every type mentioned by the declared public members, base types and interfaces of every exported
    /// type, paired with a description of where it was mentioned.
    /// </summary>
    /// <remarks>
    /// Generic types are decomposed into their definition and their arguments, and arrays, by-ref
    /// parameters and pointers into their element types, so that a capability cannot hide inside
    /// <c>Task&lt;HttpResponseMessage&gt;</c> or <c>Stream[]</c>. Nothing is instantiated and nothing is
    /// invoked: this reads metadata only.
    /// </remarks>
    private static IEnumerable<(Type Mentioned, string Where)> WalkPublicSignatureClosure()
    {
        foreach (Type type in ExportedTypes)
        {
            string owner = type.FullName ?? type.Name;

            foreach (Type mentioned in Decompose(type.BaseType))
            {
                yield return (mentioned, $"{owner} (base type)");
            }

            // Interfaces are walked because they are part of what the type publishes. Enums bring the
            // language-mandated System.Enum, IComparable, IConvertible, IFormattable and
            // ISpanFormattable with them, all of which sit in the permitted System namespace.
            foreach (Type contract in type.GetInterfaces())
            {
                foreach (Type mentioned in Decompose(contract))
                {
                    yield return (mentioned, $"{owner} (interface)");
                }
            }

            // Accessors are NOT filtered out here, unlike in the member sweeps: a property's type is
            // part of the signature whether it is reached through the property or through its getter,
            // and including both costs nothing while guaranteeing nothing is missed.
            foreach (MethodInfo method in type.GetMethods(Declared))
            {
                foreach (Type mentioned in Decompose(method.ReturnType))
                {
                    yield return (mentioned, $"{owner}.{method.Name}() return");
                }

                foreach (ParameterInfo parameter in method.GetParameters())
                {
                    foreach (Type mentioned in Decompose(parameter.ParameterType))
                    {
                        yield return (mentioned, $"{owner}.{method.Name}() parameter '{parameter.Name}'");
                    }
                }
            }

            foreach (PropertyInfo property in type.GetProperties(Declared))
            {
                foreach (Type mentioned in Decompose(property.PropertyType))
                {
                    yield return (mentioned, $"{owner}.{property.Name} property");
                }
            }

            foreach (FieldInfo field in type.GetFields(Declared))
            {
                foreach (Type mentioned in Decompose(field.FieldType))
                {
                    yield return (mentioned, $"{owner}.{field.Name} field");
                }
            }

            foreach (ConstructorInfo constructor in type.GetConstructors(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                foreach (ParameterInfo parameter in constructor.GetParameters())
                {
                    foreach (Type mentioned in Decompose(parameter.ParameterType))
                    {
                        yield return (mentioned, $"{owner} constructor parameter '{parameter.Name}'");
                    }
                }
            }

            // An event would be a callback surface, which is behaviour by definition. None exists;
            // walking them means one appearing later is reported rather than ignored.
            foreach (EventInfo declaredEvent in type.GetEvents(Declared))
            {
                foreach (Type mentioned in Decompose(declaredEvent.EventHandlerType))
                {
                    yield return (mentioned, $"{owner}.{declaredEvent.Name} event");
                }
            }
        }
    }

    /// <summary>
    /// Reduces <paramref name="type"/> to the named types it is built from: element types for arrays,
    /// by-ref parameters and pointers, and the generic definition plus every argument for a constructed
    /// generic.
    /// </summary>
    private static IEnumerable<Type> Decompose(Type? type)
    {
        if (type is null)
        {
            yield break;
        }

        // A generic parameter names nothing; there are none in this assembly, and skipping them keeps
        // the walk total rather than throwing on a shape it does not expect.
        if (type.IsGenericParameter)
        {
            yield break;
        }

        if (type.HasElementType)
        {
            foreach (Type element in Decompose(type.GetElementType()))
            {
                yield return element;
            }

            yield break;
        }

        yield return type;

        if (!type.IsGenericType)
        {
            yield break;
        }

        if (!type.IsGenericTypeDefinition)
        {
            yield return type.GetGenericTypeDefinition();
        }

        foreach (Type argument in type.GetGenericArguments())
        {
            foreach (Type mentioned in Decompose(argument))
            {
                yield return mentioned;
            }
        }
    }

    /// <summary>
    /// The namespace-qualified name of <paramref name="type"/>, keeping the arity suffix of a generic
    /// so that <c>System.Nullable`1</c> and a hypothetical <c>System.Nullable</c> cannot be confused.
    /// </summary>
    /// <remarks>
    /// Used in place of <see cref="Type.FullName"/> because <c>FullName</c> of a CONSTRUCTED generic
    /// embeds its arguments and their assembly identities, producing a value no allow-list or deny-list
    /// could ever match.
    /// </remarks>
    private static string CanonicalName(Type type) =>
        type.Namespace is { } declaringNamespace ? declaringNamespace + "." + type.Name : type.Name;

    // ==============================================================================================
    //  M3 - WHAT THE ASSEMBLY REACHES
    //
    //  The third lock, and mechanically the simplest: an assembly cannot do what it cannot reference.
    //  M1 and M2 both reason about the surface the assembly PUBLISHES, which leaves one gap between
    //  them - a private member, or a body inside an otherwise well-shaped type, publishes nothing and
    //  is invisible to both. The reference set closes it, because the reference is required whether the
    //  use is public or private.
    //
    //  ONE LIMITATION, STATED RATHER THAN GLOSSED OVER. A CONST-ONLY dependency is invisible here, and
    //  necessarily so. Writing `PowerFramework.Shared.Kernel.RetCode.OK` compiles the VALUE into this
    //  assembly and emits no metadata reference at all - measured by adding exactly that and observing
    //  the reference set come back unchanged at five entries - so no reflection-based control can see a
    //  ProjectReference whose only use is a constant.
    //
    //  That gap is narrow, and it is at the harmless end of the range: sharing a VALUE is not sharing
    //  BEHAVIOUR, and the value agreement in question is precisely the one common.v1.proto already
    //  documents as a review-time invariant. The moment a type, an instance or a member from another
    //  project is touched, the reference IS emitted and this test fires - measured by replacing that
    //  constant with a PowerFramework.Shared.Kernel.PfwException-typed property, which was then caught
    //  four times over: by this test, by the framework allow-list below, by the signature-closure sweep
    //  in M2 and by the property sweep. The const-only edge is left where it belongs, with
    //  PowerFramework.Contracts.csproj, which declares zero ProjectReference and records why.
    // ==============================================================================================

    /// <summary>
    /// The runtime assemblies the generated code legitimately needs, beyond the framework itself.
    /// </summary>
    /// <remarks>
    /// Both are required rather than merely permitted, and their presence is asserted: Google.Protobuf
    /// is what a generated message is built on, and Grpc.Core.Api is what the service containers,
    /// server bases and clients are built on. Grpc.Core.Api missing would mean
    /// <c>GrpcServices="Both"</c> had stopped producing service stubs - a failure that compiles cleanly
    /// and only surfaces at wire-up.
    /// </remarks>
    private static readonly IReadOnlySet<string> RequiredRuntimeAssemblies =
        new HashSet<string>(StringComparer.Ordinal) { "Google.Protobuf", "Grpc.Core.Api" };

    /// <summary>
    /// The non-framework assemblies that arrive from the three package references
    /// <c>PowerFramework.Contracts.csproj</c> declares, beyond the two required runtime assemblies.
    /// </summary>
    /// <remarks>
    /// Permitted rather than required, because whether the OpenAPI object model emits a metadata
    /// reference at all depends on what the generated and embedded surface touches, and this project
    /// compiles no source of its own. What matters is that nothing OUTSIDE this set arrives: these four
    /// names are exactly what <c>Microsoft.AspNetCore.OpenApi</c> and <c>Microsoft.OpenApi</c> bring,
    /// and any fifth name means a fourth package was added to a project whose contract fixes the count
    /// at three.
    /// </remarks>
    private static readonly IReadOnlySet<string> DeclaredPackageAssemblies =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "Microsoft.AspNetCore.OpenApi",
            "Microsoft.OpenApi",
            "Microsoft.AspNetCore.Mvc.Abstractions",
            "Microsoft.AspNetCore.Mvc.Core",
        };

    /// <summary>
    /// Assembly-name prefixes that would give the boundary a capability, each paired with the
    /// capability it grants.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THIS LIST IS NOT REDUNDANT WITH THE FRAMEWORK ALLOW-LIST, and the reason is worth stating
    /// because it looks redundant at a glance. That allow-list admits any <c>System.*</c> assembly,
    /// because the framework reference assemblies a generated build legitimately needs are
    /// <c>System.Runtime</c>, <c>System.Collections</c> and <c>System.Memory</c> and enumerating them
    /// exactly would turn a routine toolchain update into a false failure here. But
    /// <c>System.Net.Http</c>, <c>System.Data.Common</c>, <c>System.Security.Cryptography</c> and
    /// <c>System.Text.Json</c> are also <c>System.*</c>, so the open prefix would wave through exactly
    /// the capabilities that matter most. This deny-list is checked FIRST for that reason, and it is
    /// what makes the pair of rules both robust and sharp.
    /// </para>
    /// <para>
    /// WHAT IS DELIBERATELY ABSENT FROM THIS LIST, AND WHY. <c>Microsoft.AspNetCore.OpenApi</c> and
    /// <c>Microsoft.OpenApi</c> are two of the three package references
    /// <c>PowerFramework.Contracts.csproj</c> is required to declare, because this project carries the
    /// REST half of the published boundary - <c>OpenApi/gateway.v1.yaml</c> and
    /// <c>OpenApi/security.v1.yaml</c>, the source of truth for C-01, C-02, C-09 and C-10 - and the
    /// second of the two is the direct reference by which central package management substitutes the
    /// mandatory <c>Microsoft.OpenApi</c> 2.11.0 pin for the vulnerable 2.0.0 the first pulls
    /// transitively (AAP 0.5.2). Naming either here would make this test contradict the project file
    /// it guards. <c>Microsoft.AspNetCore.Mvc.Core</c>, <c>Microsoft.AspNetCore.Mvc.Abstractions</c>
    /// and <c>System.Text.Json</c> arrive with that pair and are likewise not capabilities this project
    /// took for itself. So the ASP.NET Core entries below are narrowed from the whole
    /// <c>Microsoft.AspNetCore</c> prefix to the specific hosting, server, request-handling, routing and
    /// security assemblies a boundary definition would have no business referencing - which is the
    /// capability this rule was always aimed at, expressed so that the one legitimate ASP.NET Core
    /// dependency does not have to be argued about again.
    /// </para>
    /// <para>
    /// Every remaining entry is on AAP 0.5.3's deliberately-excluded list or is assigned by AAP 0.5.1
    /// to a specific service rather than to the boundary.
    /// </para>
    /// </remarks>
    private static readonly IReadOnlyList<(string Prefix, string Capability)> ForbiddenCapabilityAssemblies =
    [
        ("System.Net", "network and HTTP"),
        ("System.Security", "cryptography and security primitives"),
        ("System.Data", "database access"),
        ("System.Configuration", "configuration reading"),
        ("System.Diagnostics.Process", "process control"),
        ("Microsoft.Extensions", "configuration, options, dependency injection and logging"),
        ("Microsoft.AspNetCore.Hosting", "web hosting"),
        ("Microsoft.AspNetCore.Server", "a web server"),
        ("Microsoft.AspNetCore.Http", "request and response handling"),
        ("Microsoft.AspNetCore.Routing", "a route table"),
        ("Microsoft.AspNetCore.Authentication", "authentication"),
        ("Microsoft.AspNetCore.Authorization", "authorization"),
        ("Microsoft.Data", "a database driver"),
        ("Microsoft.EntityFrameworkCore", "object-relational mapping"),
        ("Microsoft.IdentityModel", "token minting and validation"),
        ("Grpc.AspNetCore", "gRPC hosting"),
        ("Grpc.Net", "gRPC channels and hosting"),
        ("SQLitePCLRaw", "the native SQLite engine"),
        ("Polly", "resilience policies"),
        ("Serilog", "third-party logging"),
        ("OpenTelemetry", "telemetry"),
        ("Swashbuckle", "OpenAPI user interface"),
        ("Scalar", "OpenAPI user interface"),
        ("FluentValidation", "a validation framework"),
        ("xunit", "a test framework"),
        ("coverlet", "coverage instrumentation"),
    ];

    [Fact]
    public void TheAssemblyReferencesNoOtherProjectAssemblyFromThisRepository()
    {
        string[] repositoryReferences =
        [
            .. Contracts.GetReferencedAssemblies()
                .Select(static reference => reference.Name ?? "<unnamed reference>")
                .Where(static name => name.StartsWith(RepositoryAssemblyPrefix, StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];

        Assert.True(
            repositoryReferences.Length == 0,
            $"""
            {ContractsAssemblyName} references {repositoryReferences.Length} other project assembly
            from this repository: {string.Join(", ", repositoryReferences)}.

            PowerFramework.Contracts.csproj declares ZERO ProjectReference by design (constraint C-A).
            The boundary project must sit at the bottom of the graph: all four services reference it, so
            anything it references becomes a transitive dependency of Gateway, DataServices, Persistence
            and Security at once, and a shared LIBRARY reached that way is a shared-code path dressed up
            as a contract.
            """);

        // Named explicitly as well as by prefix, because these are the specific edges that would be
        // most tempting to add and the prefix test alone would not say which one was meant.
        string[] mustNeverBeReferenced =
        [
            "PowerFramework.Shared.Kernel",
            "PowerFramework.Shared.Diagnostics",
            "PowerFramework.Shared.Eventful",
            "PowerFramework.Shared.Localization",
            "PowerFramework.Shared.Containers",
            "PowerFramework.Gateway",
            "PowerFramework.DataServices",
            "PowerFramework.Persistence",
            "PowerFramework.Security",
        ];

        foreach (string forbidden in mustNeverBeReferenced)
        {
            Assert.DoesNotContain(
                forbidden,
                Contracts.GetReferencedAssemblies().Select(static reference => reference.Name),
                StringComparer.Ordinal);
        }

        // ==========================================================================================
        //  THE CONSEQUENCE OF THIS PASSING, AND WHY IT IS RECORDED HERE  (constraint C-K)
        //
        //  Because there is no reference to PowerFramework.Shared.Kernel - and there must not be - the
        //  numeric agreement between the RetCode enum in Proto/common.v1.proto and the ported constants
        //  in shared/PowerFramework.Shared.Kernel/RetCode.cs HAS NO COMPILE-TIME ENFORCEMENT ANYWHERE
        //  IN THE TREE. Nothing breaks if the two drift apart. common.v1.proto says so itself, and
        //  RetCode.cs records the direction of alignment: the contract author aligns to RetCode.cs,
        //  never the reverse.
        //
        //  RetCodeAgreementTests.cs, in THIS folder, is the only place that agreement is checked
        //  mechanically. It can do so only because PowerFramework.Contracts.Tests.csproj references
        //  BOTH projects - which is exactly why that second ProjectReference is not a stray to be
        //  tidied away, and why its own comment says so.
        //
        //  DELETING OR WEAKENING RetCodeAgreementTests WOULD SILENTLY REMOVE THE CONTROL. The cost of
        //  drift is not a compile error; it is that every stored characterization recording mentioning
        //  a changed member becomes invalid, because AAP 0.4.5.3's preserved SCREAMING_SNAKE spellings
        //  appear in serialized payloads and log records. The read-only oracle
        //  ws_objects/pfw.shared.pbl.src/retcode.sru is the arbiter, and its 156 constants are the
        //  audit both sides are measured against.
        // ==========================================================================================
    }

    [Fact]
    public void TheAssemblyReferencesNoCapabilityItWouldNeedInOrderToDoAnything()
    {
        List<string> findings = [];

        foreach (AssemblyName reference in Contracts.GetReferencedAssemblies())
        {
            string name = reference.Name ?? "<unnamed reference>";

            foreach ((string prefix, string capability) in ForbiddenCapabilityAssemblies)
            {
                if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    findings.Add($"{name} grants {capability}");
                }
            }
        }

        string[] distinct = Deduplicate(findings);

        Assert.True(
            distinct.Length == 0,
            $"""
            {ContractsAssemblyName} references {distinct.Length} assembly that grants it a capability:

            {string.Join(FindingSeparator, distinct)}

            The boundary project declares shapes. It has nothing to send, store, encrypt, configure,
            host or log, and all four services reference it - so a capability added here is added to
            Gateway, DataServices, Persistence and Security simultaneously, widening the dependency and
            NuGet-audit surface of every one of them including the services that have no use for it.

            Add the package to the project that actually needs it. AAP 0.5.1 assigns every approved
            package to its consumers by name, and AAP 0.5.3 records the ones excluded from this refactor
            outright together with the reason for each.
            """);
    }

    [Fact]
    public void TheReferencedAssemblySetIsLimitedToTheFrameworkAndTheProtobufGrpcRuntime()
    {
        string[] references =
        [
            .. Contracts.GetReferencedAssemblies()
                .Select(static reference => reference.Name ?? "<unnamed reference>")
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];

        List<string> unexpected = [];

        foreach (string name in references)
        {
            // The framework reference assemblies. Admitted by prefix rather than enumerated, so that a
            // toolchain update that splits or renames a facade does not produce a false failure here.
            // The capability deny-list checked by
            // TheAssemblyReferencesNoCapabilityItWouldNeedInOrderToDoAnything is what stops that open
            // prefix from admitting System.Net.Http, System.Data.Common or
            // System.Security.Cryptography under the same umbrella.
            if (name.StartsWith("System.", StringComparison.Ordinal)
                || string.Equals(name, "System", StringComparison.Ordinal)
                || string.Equals(name, "netstandard", StringComparison.Ordinal))
            {
                continue;
            }

            if (RequiredRuntimeAssemblies.Contains(name))
            {
                continue;
            }

            // The two OpenAPI package assemblies the project file declares, plus the two MVC
            // abstractions Microsoft.AspNetCore.OpenApi brings with it. They are approved by the
            // project's own three-reference contract rather than tolerated: the boundary carries the
            // OpenAPI documents as well as the protocol definitions, and the Microsoft.OpenApi
            // reference is the mechanism that overrides the vulnerable transitive 2.0.0 (AAP 0.5.2).
            if (DeclaredPackageAssemblies.Contains(name))
            {
                continue;
            }

            unexpected.Add(name);
        }

        Assert.True(
            unexpected.Count == 0,
            $"""
            {ContractsAssemblyName} references {unexpected.Count} unexpected assembly:
            {string.Join(", ", unexpected)}.

            PowerFramework.Contracts.csproj declares exactly THREE package references - Grpc.AspNetCore,
            from which Grpc.Tools and Google.Protobuf arrive transitively (AAP 0.5.1), plus
            Microsoft.AspNetCore.OpenApi and the mandatory Microsoft.OpenApi pin (AAP 0.5.2). Nothing
            else is approved for this project. A new name here means a fourth package was added - report
            which, and add it to the project that needs it instead.

            Full reference set observed: {string.Join(", ", references)}.
            """);

        // BOTH RUNTIME ASSEMBLIES MUST BE PRESENT, which is the vacuity guard for this test and a
        // genuine assertion in its own right. Google.Protobuf absent would mean no message type
        // generated; Grpc.Core.Api absent would mean GrpcServices="Both" had stopped emitting service
        // stubs - a change that compiles cleanly and fails only when a service tries to wire up.
        foreach (string required in RequiredRuntimeAssemblies.Order(StringComparer.Ordinal))
        {
            Assert.Contains(required, references, StringComparer.Ordinal);
        }
    }
}
