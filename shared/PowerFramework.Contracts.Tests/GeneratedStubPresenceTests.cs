// ==================================================================================================
//  GeneratedStubPresenceTests - THE GENERATED SURFACE EXISTS, AND THIS IS THE ONLY PROOF OF IT
//  ------------------------------------------------------------------------------------------------
//  SUBJECT     The CLR types that Grpc.Tools generates from shared/PowerFramework.Contracts:
//                Proto/common.v1.proto        shared vocabulary, no service
//                Proto/dataservices.v1.proto  C-03 DataWindowService, C-04 ColumnExpressionService
//                Proto/persistence.v1.proto   C-05 Query, C-06 Update, C-07 Command, C-08 Transaction
//
//  WHY THIS SUITE EXISTS, AND WHY IT CANNOT BE FOLDED INTO ProtoDescriptorTests
//  ------------------------------------------------------------------------------------------------
//  PowerFramework.Contracts compiles its protocol definitions with a single piece of metadata:
//
//      <Protobuf Include="Proto/**/*.proto" ProtoRoot="Proto" GrpcServices="Both" />
//
//  "Both" is what makes the generator emit the SERVER base classes that DataServices and Persistence
//  derive from AND the CLIENT stubs that Gateway and DataServices call through. Either half alone
//  breaks half the consumers, and the breakage surfaces far away - inside a service project, at
//  endpoint mapping or at client construction - where the cause is much harder to see.
//
//  A DESCRIPTOR-LEVEL ASSERTION CANNOT DETECT A MISCONFIGURATION HERE. That is measured, not assumed.
//  A throwaway build of these same three definitions with GrpcServices="None" was executed on this
//  toolchain (SDK 10.0.302, Grpc.Tools 2.83.0): DataservicesV1Reflection.Descriptor.Services still
//  reported both services with all 16 and 26 methods, because the FileDescriptorProto embedded in the
//  generated MESSAGE file carries the service declarations regardless of the GrpcServices value - only
//  the *Grpc.cs file, and with it every container, server base and client type, disappeared. Repeating
//  the experiment with GrpcServices="Server" produced the container and the abstract server base with
//  NO client at all. So reaching for a ServiceDescriptor proves the .proto declares a service; only
//  reaching for the generated CLR types proves the generator was asked to emit both halves.
//
//  That is the whole value of this file, and it is why the client assertion below carries an explicit
//  failure message naming GrpcServices as the cause.
//
//  WHAT THIS SUITE ASSERTS, AND WHAT IT DELIBERATELY DOES NOT
//  ------------------------------------------------------------------------------------------------
//  ASSERTED  Presence and SHAPE of the generated surface: the three file descriptors resolve; every
//            authored message materialised into the csharp_namespace its definition declares, with a
//            parser and a round-tripping descriptor; each of the six in-scope services produced a
//            container, an abstract server base and a ClientBase-derived client; each required method
//            exists on both generated halves with the signature its streaming direction implies.
//  NOT       Behaviour. Nothing here invokes a service method, opens a channel, starts a server,
//            constructs a client, touches a socket, reads a clock or draws a random number. The suite
//            is reflection over immutable generated metadata and is therefore pure and repeatable -
//            the constraint that the contracts project "carries no behaviour" applies to its tests as
//            much as to itself. Contract SEMANTICS - numeric alphabets, veto states, field-level
//            fidelity - belong to ProtoDescriptorTests and ProtoSerializationTests; this suite is the
//            layer beneath them, proving the surface they reason about was generated at all.
//
//  SCOPE: EXACTLY SIX SERVICES, AND NO SEVENTH
//  ------------------------------------------------------------------------------------------------
//  The roster below is closed. DesignSystem, Documents, Integration and ScriptBridge receive no code,
//  no test and no container in this phase, so no expectation for any of them appears here - and two
//  of the sweeps are written as exact set comparisons rather than "contains" checks precisely so that
//  a service appearing for a deferred capability would FAIL rather than pass unnoticed.
//
//  RULES POSITION
//  ------------------------------------------------------------------------------------------------
//  review_rules returns exactly "No user rules provided.", so no user-specified rule governs this
//  file. The enterprise-standard baseline applies in its place and is honoured here: nullable
//  reference types and warnings-as-errors inherited from the repository root and never relaxed or
//  suppressed, no secret or credential literal of any kind, one assertion per fact so a single
//  mis-generated service cannot mask the rest, and every assertion annotated with the plan decision
//  or the legacy locator it pins.
// ==================================================================================================

using System.Reflection;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Grpc.Core;
using PowerFramework.Contracts.Common.V1;
using Xunit;
using Xunit.Sdk;

namespace PowerFramework.Contracts.Tests;

/// <summary>
/// Proves that the three published protocol definitions generated their message types and BOTH gRPC
/// halves - the abstract server base and the <see cref="ClientBase"/>-derived client - into the
/// namespaces they declare.
/// </summary>
/// <remarks>
/// <para>
/// Every member is a reflection query over the immutable, process-wide descriptor graph and over the
/// generated types in <c>PowerFramework.Contracts.dll</c>. There is no fixture to obtain, no
/// asynchronous call, no external resource and no shared mutable state, so the whole suite is safe to
/// run in parallel with every other collection.
/// </para>
/// <para>
/// The <c>Require*</c> helpers throw <see cref="FailException"/> with a diagnosable message rather
/// than returning a nullable, which keeps the tests themselves free of null-forgiving operators - the
/// same convention <c>ContractTestContext.cs</c> uses.
/// </para>
/// </remarks>
public sealed class GeneratedStubPresenceTests
{
    // ==============================================================================================
    //  THE PUBLISHED BOUNDARY, NAMED ONCE
    //
    //  Proto file names are BARE, with no Proto/ prefix, because the contracts project sets
    //  ProtoRoot="Proto" explicitly - which is what makes `import "common.v1.proto";` resolve and what
    //  FileDescriptor.Name reports back. The namespaces are the csharp_namespace values declared at
    //  common.v1.proto:L88, dataservices.v1.proto:L75 and persistence.v1.proto:L123.
    // ==============================================================================================

    private const string CommonFileName = "common.v1.proto";

    private const string DataServicesFileName = "dataservices.v1.proto";

    private const string PersistenceFileName = "persistence.v1.proto";

    private const string CommonPackage = "common.v1";

    private const string DataServicesPackage = "dataservices.v1";

    private const string PersistencePackage = "persistence.v1";

    private const string CommonNamespace = "PowerFramework.Contracts.Common.V1";

    private const string DataServicesNamespace = "PowerFramework.Contracts.DataServices.V1";

    private const string PersistenceNamespace = "PowerFramework.Contracts.Persistence.V1";

    /// <summary>
    /// The one well-known definition <c>common.v1.proto</c> imports, for its
    /// <c>extend google.protobuf.MethodOptions</c> declaration of the rich-error option.
    /// </summary>
    private const string DescriptorProtoFileName = "google/protobuf/descriptor.proto";

    /// <summary>
    /// The sentence that names the cause when a server base exists without a client. It is the reason
    /// this suite was written, so it is stated once and reused verbatim by every affected assertion.
    /// </summary>
    private const string GrpcServicesDiagnosis =
        "a service base without a client means the <Protobuf> item was compiled with "
        + "GrpcServices=\"Server\" rather than \"Both\"";

    /// <summary>The assembly Grpc.Tools generated the contract types into.</summary>
    /// <remarks>
    /// Reached through a generated reflection holder rather than through a hand-written type, because
    /// the subject of this suite is precisely what the generator produced. If this expression stops
    /// compiling, no protocol definition generated at all.
    /// </remarks>
    private static Assembly ContractsAssembly { get; } = typeof(CommonV1Reflection).Assembly;

    // ==============================================================================================
    //  THE SIX IN-SCOPE SERVICES - ONE SOURCE OF TRUTH, PROJECTED INTO EVERY THEORY
    //
    //  Contracts C-03 through C-08. The two REST contracts (C-01 TokenService and C-02 CryptoService,
    //  served by Security) and C-09/C-10 are OpenAPI documents rather than protocol definitions and
    //  are therefore not in this roster; OpenApiContractDocumentTests and GatewayContractTests own
    //  them. common.v1.proto contributes no service by design.
    // ==============================================================================================

    /// <summary>One in-scope gRPC service, and where its generated surface must appear.</summary>
    /// <param name="ContractId">The contract identifier from the published inventory, C-03 to C-08.</param>
    /// <param name="ProtoFileName">The protocol definition that must declare it.</param>
    /// <param name="CsharpNamespace">The <c>csharp_namespace</c> its generated types must land in.</param>
    /// <param name="ServiceName">The proto service name, which is also the generated container's name.</param>
    private sealed record ServiceContract(
        string ContractId,
        string ProtoFileName,
        string CsharpNamespace,
        string ServiceName);

    /// <summary>One method a contract is required to declare, with the streaming direction it requires.</summary>
    /// <param name="ContractId">The owning contract identifier.</param>
    /// <param name="ServiceName">The declaring service.</param>
    /// <param name="MethodName">The rpc name, in its proto spelling.</param>
    /// <param name="ClientStreaming">Whether the client streams its requests.</param>
    /// <param name="ServerStreaming">Whether the server streams its responses.</param>
    /// <param name="RequestMessage">
    /// The fully-qualified message the method accepts, stated INDEPENDENTLY of the descriptor graph.
    /// </param>
    /// <param name="ResponseMessage">
    /// The fully-qualified message the method returns, likewise stated independently.
    /// </param>
    /// <remarks>
    /// THE TWO MESSAGE NAMES ARE THE POINT OF THIS RECORD, NOT DECORATION. Reading them off the
    /// descriptor - as an earlier form of this suite did - makes every signature assertion
    /// self-fulfilling: whatever the generator produced becomes the expectation, so a request type
    /// swapped for another that happens to compile passes. Stating them here means the frozen contract
    /// is the expectation and the descriptor is the subject.
    /// </remarks>
    private sealed record RequiredMethod(
        string ContractId,
        string ServiceName,
        string MethodName,
        bool ClientStreaming,
        bool ServerStreaming,
        string RequestMessage,
        string ResponseMessage);

    private static readonly ServiceContract[] Roster =
    [
        new("C-03", DataServicesFileName, DataServicesNamespace, "DataWindowService"),
        new("C-04", DataServicesFileName, DataServicesNamespace, "ColumnExpressionService"),
        new("C-05", PersistenceFileName, PersistenceNamespace, "QueryService"),
        new("C-06", PersistenceFileName, PersistenceNamespace, "UpdateService"),
        new("C-07", PersistenceFileName, PersistenceNamespace, "CommandService"),
        new("C-08", PersistenceFileName, PersistenceNamespace, "TransactionService"),
    ];

    // ==============================================================================================
    //  LOOKUP HELPERS
    // ==============================================================================================

    /// <summary>Resolves one of the three published definitions by its bare proto file name.</summary>
    /// <exception cref="FailException">No published definition carries that name.</exception>
    private static FileDescriptor RequireFile(string protoFileName) =>
        ContractDescriptors.All.SingleOrDefault(
            candidate => string.Equals(candidate.Name, protoFileName, StringComparison.Ordinal))
        ?? throw FailException.ForFailure(
            $"No published protocol definition is named '{protoFileName}'. The boundary publishes "
            + $"[{string.Join(", ", ContractDescriptors.All.Select(static file => file.Name))}]. A bare "
            + "file name is expected here because the contracts project sets ProtoRoot=\"Proto\".");

    /// <summary>Finds the roster entry for a service, so a theory row need not restate its namespace.</summary>
    /// <exception cref="FailException">The service is not one of the six in-scope contracts.</exception>
    private static ServiceContract RequireRosterEntry(string serviceName) =>
        Roster.SingleOrDefault(
            candidate => string.Equals(candidate.ServiceName, serviceName, StringComparison.Ordinal))
        ?? throw FailException.ForFailure(
            $"'{serviceName}' is not one of the six in-scope services "
            + $"[{string.Join(", ", Roster.Select(static entry => entry.ServiceName))}]. Nothing may be "
            + "expected here for a deferred capability, because no deferred service is implemented in "
            + "this phase.");

    /// <summary>Resolves a generated top-level type by its full CLR name.</summary>
    /// <exception cref="FailException">The generator emitted no such type.</exception>
    private static Type RequireGeneratedType(string fullName, string because) =>
        ContractsAssembly.GetType(fullName, throwOnError: false, ignoreCase: false)
        ?? throw FailException.ForFailure(
            $"'{fullName}' was not generated into {ContractsAssembly.GetName().Name}. {because}");

    /// <summary>Resolves a PUBLIC nested type of a generated container.</summary>
    /// <remarks>
    /// Public only, deliberately. Grpc.Tools also nests a private message cache inside each container;
    /// a search that included non-public types would report it and obscure the two types that matter.
    /// </remarks>
    /// <exception cref="FailException">The container has no public nested type of that name.</exception>
    private static Type RequireNestedType(Type container, string nestedName, string because) =>
        container.GetNestedType(nestedName, BindingFlags.Public)
        ?? throw FailException.ForFailure(
            $"'{container.FullName}' declares no public nested type '{nestedName}'. Its public nested "
            + $"types are [{string.Join(", ", PublicNestedTypeNames(container))}]. {because}");

    /// <summary>The names of a container's public nested types, ordered so a comparison is stable.</summary>
    private static string[] PublicNestedTypeNames(Type container) =>
        [.. container.GetNestedTypes(BindingFlags.Public)
            .Select(static nested => nested.Name)
            .OrderBy(static name => name, StringComparer.Ordinal)];

    /// <summary>The generated container type for one in-scope service.</summary>
    private static Type RequireServiceContainer(string csharpNamespace, string serviceName) =>
        RequireGeneratedType(
            $"{csharpNamespace}.{serviceName}",
            "Grpc.Tools emits one container class per service, so its absence means the whole *Grpc.cs "
            + "file was never generated: the <Protobuf> item was compiled with GrpcServices=\"None\", "
            + "which still yields the message types and the service DESCRIPTORS and therefore still "
            + "compiles.");

    /// <summary>The proto package one published definition declares.</summary>
    /// <exception cref="FailException">The name is not one of the three published definitions.</exception>
    private static string ProtoPackageOf(string protoFileName) => protoFileName switch
    {
        CommonFileName => CommonPackage,
        DataServicesFileName => DataServicesPackage,
        PersistenceFileName => PersistencePackage,
        _ => throw FailException.ForFailure(
            $"'{protoFileName}' is not one of the three published definitions, so no proto package is "
            + "known for it. A fourth definition entering the boundary must be added to this map and to "
            + "the file roster together."),
    };

    /// <summary>The fully-qualified proto name of one in-scope service, built from its own contract row.</summary>
    private static string ProtoServiceFullName(ServiceContract contract) =>
        $"{ProtoPackageOf(contract.ProtoFileName)}.{contract.ServiceName}";

    /// <summary>The <c>csharp_namespace</c> the definition declaring this package sets.</summary>
    /// <exception cref="FailException">The package is not one of the three published packages.</exception>
    private static string CsharpNamespaceOfPackage(string protoPackage) => protoPackage switch
    {
        CommonPackage => CommonNamespace,
        DataServicesPackage => DataServicesNamespace,
        PersistencePackage => PersistenceNamespace,
        _ => throw FailException.ForFailure(
            $"'{protoPackage}' is not one of the three published packages "
            + $"[{CommonPackage}, {DataServicesPackage}, {PersistencePackage}]."),
    };

    /// <summary>
    /// The generated CLR type a fully-qualified proto message name must project to, resolved WITHOUT
    /// consulting the descriptor graph.
    /// </summary>
    /// <remarks>
    /// This is the helper that lets a signature assertion be a real assertion. Taking the expected type
    /// from <c>MethodDescriptor.InputType.ClrType</c> makes whatever the generator produced into the
    /// expectation, so a request type swapped for another that happens to compile passes; building it
    /// from the FROZEN proto name instead means the swap fails.
    /// <para>
    /// The projection rule is protoc's own: the package maps to the declared <c>csharp_namespace</c>, and
    /// a nested message lands under a <c>Types</c> holder class - <c>a.b.Outer.Inner</c> becomes
    /// <c>Namespace.Outer+Types+Inner</c>. No rpc request or response is nested today, but the rule is
    /// implemented rather than assumed away so that adding one cannot silently mis-resolve.
    /// </para>
    /// </remarks>
    /// <exception cref="FailException">The name carries no known package, or nothing was generated.</exception>
    private static Type ExpectedGeneratedMessageType(string messageFullName, string because)
    {
        string protoPackage = new[] { CommonPackage, DataServicesPackage, PersistencePackage }
            .SingleOrDefault(candidate => messageFullName.StartsWith(
                candidate + ".",
                StringComparison.Ordinal))
            ?? throw FailException.ForFailure(
                $"'{messageFullName}' does not begin with any of the three published packages "
                + $"[{CommonPackage}, {DataServicesPackage}, {PersistencePackage}]. Every name in the "
                + "frozen inventories is fully qualified precisely so this can never be ambiguous.");

        string relativeName = messageFullName[(protoPackage.Length + 1)..];
        string nestedPath = string.Join("+Types+", relativeName.Split('.'));

        return RequireGeneratedType(
            $"{CsharpNamespaceOfPackage(protoPackage)}.{nestedPath}",
            because);
    }

    // ==============================================================================================
    //  THE REQUIRED METHOD SURFACE - COMPLETE, EXPLICIT AND FROZEN
    //
    //  All 77 rpcs of contracts C-03 through C-08, each with its streaming direction and BOTH of its
    //  message names, written out rather than derived. Completeness is the property that matters here,
    //  and it is why this list is long: a roster naming a selected subset lets a DELETED rpc delete its
    //  own test row, so the suite reports green on a boundary that has silently lost a method. Every row
    //  is therefore compared BOTH ways below - nothing named here may be missing from the descriptor
    //  graph, and nothing in the descriptor graph may be absent from here.
    //
    //  Presence, direction and message identity only. What each method DOES belongs to the service
    //  projects' own tests; what its messages CONTAIN belongs to ProtoDescriptorTests. Direction is in
    //  this file because it is part of the generated signature on both halves, and because a direction
    //  changed to unary would destroy the behaviour it exists to carry while still compiling everywhere.
    //  The two message names are here because reading them off the descriptor makes every signature
    //  assertion self-fulfilling - see the remarks on RequiredMethod.
    // ==============================================================================================

    private static readonly RequiredMethod[] RequiredMethodRoster =
    [
        // C-03. The retrieval/validation/update triple, the session that holds the four pieces of
        // cross-event state [se_cst_dw.sru:L89-L96], and the event gate whose three composable bits are
        // EID_ROWFOCUSCHANGE=1, EID_ITEMFOCUSCHANGE=2 and EID_ITEMCHANGE=4 [se_cst_dw.sru:L41-L43].
        //
        // Retrieve is SERVER-STREAMING because retrieval delivers progressively: the legacy raises
        // ondatareceived(ref data, rowcount) once per chunk [n_cst_thread_task_sqlquery.sru:L21, L73].
        //
        // EventChain is BIDIRECTIONAL because it carries the ordered, vetoable chain of all 22 events
        // se_cst_dw declares - 9 semantic and 13 raw pbm_dwn* [se_cst_dw.sru:L11-L32] - in which a
        // handler's result feeds the next event, so neither half can be a one-shot request.
        //
        // The eight headless-half accessors that follow the gate carry the three presentational
        // DataWindow services' data model across the boundary - the filter and sort expressions, the
        // menu item model and the row-selection state machine - while their rendering halves stay
        // deferred behind Gateway's /v1/design/** extension point.
        new("C-03", "DataWindowService", "Retrieve", false, true, "dataservices.v1.RetrieveRequest", "dataservices.v1.RetrieveChunk"),
        new("C-03", "DataWindowService", "OpenValidationSession", false, false, "dataservices.v1.OpenValidationSessionRequest", "dataservices.v1.OpenValidationSessionResponse"),
        new("C-03", "DataWindowService", "CloseValidationSession", false, false, "dataservices.v1.CloseValidationSessionRequest", "dataservices.v1.CloseValidationSessionResponse"),
        new("C-03", "DataWindowService", "EventChain", true, true, "dataservices.v1.EventChainRequest", "dataservices.v1.EventChainResponse"),
        new("C-03", "DataWindowService", "Update", false, false, "dataservices.v1.UpdateRequest", "dataservices.v1.UpdateResponse"),
        new("C-03", "DataWindowService", "GetEventGate", false, false, "dataservices.v1.GetEventGateRequest", "dataservices.v1.GetEventGateResponse"),
        new("C-03", "DataWindowService", "DisableEvent", false, false, "dataservices.v1.DisableEventRequest", "dataservices.v1.DisableEventResponse"),
        new("C-03", "DataWindowService", "EnableEvent", false, false, "dataservices.v1.EnableEventRequest", "dataservices.v1.EnableEventResponse"),
        new("C-03", "DataWindowService", "GetDropDownSearchState", false, false, "dataservices.v1.GetDropDownSearchStateRequest", "dataservices.v1.GetDropDownSearchStateResponse"),
        new("C-03", "DataWindowService", "ApplyDropDownSearch", false, false, "dataservices.v1.ApplyDropDownSearchRequest", "dataservices.v1.ApplyDropDownSearchResponse"),
        new("C-03", "DataWindowService", "GetColumnSortState", false, false, "dataservices.v1.GetColumnSortStateRequest", "dataservices.v1.GetColumnSortStateResponse"),
        new("C-03", "DataWindowService", "ApplyColumnSort", false, false, "dataservices.v1.ApplyColumnSortRequest", "dataservices.v1.ApplyColumnSortResponse"),
        new("C-03", "DataWindowService", "GetContextMenuModel", false, false, "dataservices.v1.GetContextMenuModelRequest", "dataservices.v1.GetContextMenuModelResponse"),
        new("C-03", "DataWindowService", "ApplyContextMenuModel", false, false, "dataservices.v1.ApplyContextMenuModelRequest", "dataservices.v1.ApplyContextMenuModelResponse"),
        new("C-03", "DataWindowService", "GetRowSelectState", false, false, "dataservices.v1.GetRowSelectStateRequest", "dataservices.v1.GetRowSelectStateResponse"),
        new("C-03", "DataWindowService", "ApplyRowSelectStyle", false, false, "dataservices.v1.ApplyRowSelectStyleRequest", "dataservices.v1.ApplyRowSelectStyleResponse"),

        // C-04. The two INVERTED channels, both bidirectional by structural necessity.
        //
        // InvokeMethodChannel exists because the legacy expects the APPLICATION to implement the macro
        // switch: oncolumnexpinvokemethod is an event the host handles, returning `any` over a
        // `string[]` [se_cst_dw.sru:L14]. Across a service boundary the expansion engine must therefore
        // call BACK INTO its client mid-calculation, and gRPC has no server-initiated call, so the
        // stream itself is inverted.
        //
        // TraceChannel carries oncolumnexptrace(row, dwo, stack, expr, value) [se_cst_dw.sru:L32], the
        // diagnostic stream the client subscribes to and the engine pushes into.
        //
        // THE INVERSION IS VISIBLE IN THIS TABLE AND NOWHERE ELSE, WHICH IS WHY THE MESSAGE NAMES HAD TO
        // BE WRITTEN OUT. InvokeMethodChannel's request side carries InvokeMethodResponse and its
        // response side carries InvokeMethodRequest - the names read backwards because the engine is the
        // party ISSUING the macro call. Derived from the descriptor that fact could never fail; stated
        // here, swapping the two sides fails immediately.
        //
        // The remaining 24 rows are the real API surface of n_cst_dwsvc_columnexp: the legacy
        // documentation describes nine methods, the 2,435-line source declares far more, and the wire
        // contract must carry what the source has rather than what the document lists.
        new("C-04", "ColumnExpressionService", "OpenExpressionSession", false, false, "dataservices.v1.OpenExpressionSessionRequest", "dataservices.v1.OpenExpressionSessionResponse"),
        new("C-04", "ColumnExpressionService", "CloseExpressionSession", false, false, "dataservices.v1.CloseExpressionSessionRequest", "dataservices.v1.CloseExpressionSessionResponse"),
        new("C-04", "ColumnExpressionService", "AddExpression", false, false, "dataservices.v1.AddExpressionRequest", "dataservices.v1.AddExpressionResponse"),
        new("C-04", "ColumnExpressionService", "SetExpression", false, false, "dataservices.v1.SetExpressionRequest", "dataservices.v1.SetExpressionResponse"),
        new("C-04", "ColumnExpressionService", "GetExpression", false, false, "dataservices.v1.GetExpressionRequest", "dataservices.v1.GetExpressionResponse"),
        new("C-04", "ColumnExpressionService", "RemoveExpression", false, false, "dataservices.v1.RemoveExpressionRequest", "dataservices.v1.RemoveExpressionResponse"),
        new("C-04", "ColumnExpressionService", "RemoveAllExpressions", false, false, "dataservices.v1.RemoveAllExpressionsRequest", "dataservices.v1.RemoveAllExpressionsResponse"),
        new("C-04", "ColumnExpressionService", "AddVariable", false, false, "dataservices.v1.AddVariableRequest", "dataservices.v1.AddVariableResponse"),
        new("C-04", "ColumnExpressionService", "SetVariable", false, false, "dataservices.v1.SetVariableRequest", "dataservices.v1.SetVariableResponse"),
        new("C-04", "ColumnExpressionService", "AddVariableExpression", false, false, "dataservices.v1.AddVariableExpressionRequest", "dataservices.v1.AddVariableExpressionResponse"),
        new("C-04", "ColumnExpressionService", "SetVariableExpression", false, false, "dataservices.v1.SetVariableExpressionRequest", "dataservices.v1.SetVariableExpressionResponse"),
        new("C-04", "ColumnExpressionService", "GetVariableExpression", false, false, "dataservices.v1.GetVariableExpressionRequest", "dataservices.v1.GetVariableExpressionResponse"),
        new("C-04", "ColumnExpressionService", "AddForeignVariable", false, false, "dataservices.v1.AddForeignVariableRequest", "dataservices.v1.AddForeignVariableResponse"),
        new("C-04", "ColumnExpressionService", "SetRelativeColumns", false, false, "dataservices.v1.SetRelativeColumnsRequest", "dataservices.v1.SetRelativeColumnsResponse"),
        new("C-04", "ColumnExpressionService", "SetExpressionFlag", false, false, "dataservices.v1.SetExpressionFlagRequest", "dataservices.v1.SetExpressionFlagResponse"),
        new("C-04", "ColumnExpressionService", "Calc", false, false, "dataservices.v1.CalcRequest", "dataservices.v1.CalcResponse"),
        new("C-04", "ColumnExpressionService", "CalcAll", false, false, "dataservices.v1.CalcAllRequest", "dataservices.v1.CalcAllResponse"),
        new("C-04", "ColumnExpressionService", "CalcEmpty", false, false, "dataservices.v1.CalcEmptyRequest", "dataservices.v1.CalcEmptyResponse"),
        new("C-04", "ColumnExpressionService", "CalcItem", false, false, "dataservices.v1.CalcItemRequest", "dataservices.v1.CalcItemResponse"),
        new("C-04", "ColumnExpressionService", "SetEnabled", false, false, "dataservices.v1.SetEnabledRequest", "dataservices.v1.SetEnabledResponse"),
        new("C-04", "ColumnExpressionService", "SetTrace", false, false, "dataservices.v1.SetTraceRequest", "dataservices.v1.SetTraceResponse"),
        new("C-04", "ColumnExpressionService", "GetServiceState", false, false, "dataservices.v1.GetServiceStateRequest", "dataservices.v1.GetServiceStateResponse"),
        new("C-04", "ColumnExpressionService", "GetExpressionState", false, false, "dataservices.v1.GetExpressionStateRequest", "dataservices.v1.GetExpressionStateResponse"),
        new("C-04", "ColumnExpressionService", "EventStream", false, true, "dataservices.v1.EventStreamRequest", "dataservices.v1.EventStreamResponse"),
        new("C-04", "ColumnExpressionService", "InvokeMethodChannel", true, true, "dataservices.v1.InvokeMethodResponse", "dataservices.v1.InvokeMethodRequest"),
        new("C-04", "ColumnExpressionService", "TraceChannel", true, true, "dataservices.v1.TraceChannelRequest", "dataservices.v1.TraceRecord"),

        // C-05. Query is SERVER-STREAMING to reproduce progressive recordset delivery; chunking is a
        // first-class part of the legacy surface, whose guard rejects any size at or below 1000 with
        // RetCode.E_INVALID_ARGUMENT [n_cst_thread_task_sqlquery.sru:L410].
        //
        // The task lifecycle pair and the seven setters are the legacy shape as found: the query task is
        // a stateful object that is created, configured through individual setters and then executed
        // [n_cst_thread_task_sqlquery.sru], so the contract carries that statefulness explicitly rather
        // than collapsing it into one fat request that would lose the per-setter validation.
        new("C-05", "QueryService", "CreateQueryTask", false, false, "persistence.v1.CreateQueryTaskRequest", "persistence.v1.CreateQueryTaskResponse"),
        new("C-05", "QueryService", "ReleaseQueryTask", false, false, "persistence.v1.ReleaseQueryTaskRequest", "persistence.v1.ReleaseQueryTaskResponse"),
        new("C-05", "QueryService", "Reset", false, false, "persistence.v1.ResetQueryTaskRequest", "persistence.v1.ResetQueryTaskResponse"),
        new("C-05", "QueryService", "SetChunkSize", false, false, "persistence.v1.SetChunkSizeRequest", "persistence.v1.SetChunkSizeResponse"),
        new("C-05", "QueryService", "SetMaxRows", false, false, "persistence.v1.SetMaxRowsRequest", "persistence.v1.SetMaxRowsResponse"),
        new("C-05", "QueryService", "SetWhereClause", false, false, "persistence.v1.SetWhereClauseRequest", "persistence.v1.SetWhereClauseResponse"),
        new("C-05", "QueryService", "SetOrderByClause", false, false, "persistence.v1.SetOrderByClauseRequest", "persistence.v1.SetOrderByClauseResponse"),
        new("C-05", "QueryService", "SetPaging", false, false, "persistence.v1.SetPagingRequest", "persistence.v1.SetPagingResponse"),
        new("C-05", "QueryService", "SetPagedUniqueIndexColumns", false, false, "persistence.v1.SetPagedUniqueIndexColumnsRequest", "persistence.v1.SetPagedUniqueIndexColumnsResponse"),
        new("C-05", "QueryService", "Query", false, true, "persistence.v1.QueryRequest", "persistence.v1.QueryResponse"),
        new("C-05", "QueryService", "Count", false, false, "persistence.v1.CountRequest", "persistence.v1.CountResponse"),

        // C-06. PrepareUpdate carries the per-table update contract, because the legacy re-derives the
        // whole thing at runtime from an ARRAY of table descriptors rather than trusting the DataWindow
        // definition - of_addupdatabletable takes the table name, the updatable columns, the key
        // columns, the identity column, updatewhere and updatekeyinplace
        // [n_cst_thread_task_sqlupdate.sru:L82], consumed by _of_updateprepare [:L98]. Update executes
        // it [:L172] and is UNARY because its conflict outcome is one definite answer - StatusCode.Aborted
        // with the current row state, projected by Gateway as HTTP 409, never a silent overwrite.
        new("C-06", "UpdateService", "CreateUpdateTask", false, false, "persistence.v1.CreateUpdateTaskRequest", "persistence.v1.CreateUpdateTaskResponse"),
        new("C-06", "UpdateService", "ReleaseUpdateTask", false, false, "persistence.v1.ReleaseUpdateTaskRequest", "persistence.v1.ReleaseUpdateTaskResponse"),
        new("C-06", "UpdateService", "Reset", false, false, "persistence.v1.ResetUpdateTaskRequest", "persistence.v1.ResetUpdateTaskResponse"),
        new("C-06", "UpdateService", "PrepareUpdate", false, false, "persistence.v1.PrepareUpdateRequest", "persistence.v1.PrepareUpdateResponse"),
        new("C-06", "UpdateService", "Update", false, false, "persistence.v1.UpdateRequest", "persistence.v1.UpdateResponse"),

        // C-07. Exec is the command half; the legacy rejects an empty statement with
        // RetCode.E_INVALID_SQL [n_cst_thread_task_sqlcommand.sru:L45] and executes on the worker
        // thread [:L60]. SetAutoCommit and SetSql carry their own request messages rather than sharing
        // the transaction service's, which is why the two Set* rows name SetCommand* messages: the
        // simple names collide across the two services and only the qualified form disambiguates them.
        new("C-07", "CommandService", "CreateCommandTask", false, false, "persistence.v1.CreateCommandTaskRequest", "persistence.v1.CreateCommandTaskResponse"),
        new("C-07", "CommandService", "ReleaseCommandTask", false, false, "persistence.v1.ReleaseCommandTaskRequest", "persistence.v1.ReleaseCommandTaskResponse"),
        new("C-07", "CommandService", "Reset", false, false, "persistence.v1.ResetCommandTaskRequest", "persistence.v1.ResetCommandTaskResponse"),
        new("C-07", "CommandService", "SetAutoCommit", false, false, "persistence.v1.SetCommandAutoCommitRequest", "persistence.v1.SetCommandAutoCommitResponse"),
        new("C-07", "CommandService", "SetSql", false, false, "persistence.v1.SetCommandSqlRequest", "persistence.v1.SetCommandSqlResponse"),
        new("C-07", "CommandService", "Exec", false, false, "persistence.v1.ExecRequest", "persistence.v1.ExecResponse"),

        // C-08. Session begin and end plus commit and rollback, mirroring of_connect
        // [n_cst_thread_trans.sru:L111], of_disconnect [:L145], of_commit [:L383] and of_rollback
        // [:L185]. All unary: a transaction boundary is a single decision. GetDatabaseType projects
        // of_getdbtype [:L357-L359], whose enumeration declares exactly DBT_MSSQL=0 and DBT_ORACLE=1 -
        // SQLite is absent from it, which is the two-storage-path tension recorded in the plan.
        new("C-08", "TransactionService", "BeginSession", false, false, "persistence.v1.BeginSessionRequest", "persistence.v1.BeginSessionResponse"),
        new("C-08", "TransactionService", "EndSession", false, false, "persistence.v1.EndSessionRequest", "persistence.v1.EndSessionResponse"),
        new("C-08", "TransactionService", "GetTransactionData", false, false, "persistence.v1.GetTransactionDataRequest", "persistence.v1.GetTransactionDataResponse"),
        new("C-08", "TransactionService", "SetAutoCommit", false, false, "persistence.v1.SetTransactionAutoCommitRequest", "persistence.v1.SetTransactionAutoCommitResponse"),
        new("C-08", "TransactionService", "AutoCommit", false, false, "persistence.v1.AutoCommitRequest", "persistence.v1.AutoCommitResponse"),
        new("C-08", "TransactionService", "Commit", false, false, "persistence.v1.CommitRequest", "persistence.v1.CommitResponse"),
        new("C-08", "TransactionService", "Rollback", false, false, "persistence.v1.RollbackRequest", "persistence.v1.RollbackResponse"),
        new("C-08", "TransactionService", "IsConnected", false, false, "persistence.v1.IsConnectedRequest", "persistence.v1.IsConnectedResponse"),
        new("C-08", "TransactionService", "GetDatabaseType", false, false, "persistence.v1.GetDatabaseTypeRequest", "persistence.v1.GetDatabaseTypeResponse"),
        new("C-08", "TransactionService", "GetSessionState", false, false, "persistence.v1.GetSessionStateRequest", "persistence.v1.GetSessionStateResponse"),
        new("C-08", "TransactionService", "ClearState", false, false, "persistence.v1.ClearStateRequest", "persistence.v1.ClearStateResponse"),
        new("C-08", "TransactionService", "SetBroken", false, false, "persistence.v1.SetBrokenRequest", "persistence.v1.SetBrokenResponse"),
        new("C-08", "TransactionService", "GridSyntaxFromSql", false, false, "persistence.v1.GridSyntaxFromSqlRequest", "persistence.v1.GridSyntaxFromSqlResponse"),
    ];

    // ==============================================================================================
    //  THE AUTHORED MESSAGE SURFACE - COMPLETE, EXPLICIT AND FROZEN
    //
    //  All 244 messages the three definitions AUTHOR, nested declarations included and the three
    //  synthetic map-entry messages excluded, written out for exactly the reason the method roster is:
    //  an inventory derived from ContractDescriptors.AllMessages() cannot detect a DELETED message,
    //  because the deletion removes the row that would have failed. Both directions are compared below.
    //
    //  Fully qualified throughout, because simple names are genuinely ambiguous in this contract set -
    //  UpdateRequest and UpdateResponse are each declared in BOTH boundary definitions, and Reset* /
    //  SetAutoCommit* exist once per persistence task service.
    //
    //  The four nested names in this list are the layout as found rather than an accident:
    //  ColumnSortState.ColumnSort is the per-column sort model, and the three ColumnExpEvent.* messages
    //  are the payload variants of the expression event feed. protoc nests their generated CLR types
    //  under a `Types` holder class, which is why the message-name-to-CLR-type helper below is written
    //  against the proto shape rather than assuming a flat projection.
    //
    //  The map-entry exclusion is asserted in its own right further down rather than left silent: a
    //  `map` field makes protoc synthesise a nested ...Entry message that is present in the descriptor
    //  graph and absent from the generated code, so including one here would fail the materialisation
    //  theory for a reason that is correct behaviour.
    // ==============================================================================================

    private static readonly string[] AuthoredMessageRoster =
    [
        // ---- common.v1.proto  (20 authored messages) ----
        "common.v1.RetCode",
        "common.v1.XmlParseStatus",
        "common.v1.SqliteResultCode",
        "common.v1.DecimalValue",
        "common.v1.DateValue",
        "common.v1.TimeValue",
        "common.v1.DateTimeValue",
        "common.v1.AnyValue",
        "common.v1.ColumnValue",
        // PROMOTED FROM dataservices.v1 INTO THE SHARED VOCABULARY. Both boundary definitions now
        // declare a row: C-03's RetrieveChunk and update rows, and persistence.v1's CarrierState
        // inside every buffer segment of C-05's chunks and C-06's update input. The C-A admission
        // test therefore puts ONE definition here, exactly as it did for IdentityColumnData.
        "common.v1.DataWindowRow",
        // The per-element presence wrapper the identity round trip needs: GetItemNumber answers null
        // for a null item and a repeated scalar has no way to say so.
        "common.v1.NullableInt64",
        "common.v1.DbError",
        "common.v1.ConflictRow",
        "common.v1.ConflictDetail",
        "common.v1.IdentityColumnData",
        "common.v1.RichErrorTrailer",
        "common.v1.RichErrorBinding",
        "common.v1.RichError",
        // ---- dataservices.v1.proto  (142 authored messages) ----
        "dataservices.v1.DwObjectRef",
        "dataservices.v1.SequencingToken",
        "dataservices.v1.Veto",
        "dataservices.v1.BrokerTopic",
        "dataservices.v1.StructuredError",
        "dataservices.v1.ValidationSessionState",
        "dataservices.v1.OpenValidationSessionRequest",
        "dataservices.v1.OpenValidationSessionResponse",
        "dataservices.v1.CloseValidationSessionRequest",
        "dataservices.v1.CloseValidationSessionResponse",
        // DataWindowRow is DELIBERATELY ABSENT here - it moved to common.v1 above.
        "dataservices.v1.RetrieveRequest",
        "dataservices.v1.RetrieveChunk",
        "dataservices.v1.InitContextMenuEvent",
        "dataservices.v1.ContextMenuEvent",
        "dataservices.v1.DdsGetFilterEvent",
        "dataservices.v1.ColumnExpInvokeMethodEvent",
        "dataservices.v1.DoItemChangeEvent",
        "dataservices.v1.ItemChangedEvent",
        "dataservices.v1.DoItemChangedEvent",
        "dataservices.v1.DdsFilteredEvent",
        "dataservices.v1.ColumnExpTraceEvent",
        "dataservices.v1.DwnRButtonDownEvent",
        "dataservices.v1.DwnRButtonUpEvent",
        "dataservices.v1.DwnRowChangeEvent",
        "dataservices.v1.DwnRowChangingEvent",
        "dataservices.v1.DwnLButtonDblClkEvent",
        "dataservices.v1.DwnLButtonClkEvent",
        "dataservices.v1.DwnChangingEvent",
        "dataservices.v1.DwnItemChangeFocusEvent",
        "dataservices.v1.DwnItemChangeEvent",
        "dataservices.v1.DwnItemValidationErrorEvent",
        "dataservices.v1.DwnKillFocusEvent",
        "dataservices.v1.DwnLButtonUpEvent",
        "dataservices.v1.DwnSetFocusEvent",
        "dataservices.v1.EventNotification",
        "dataservices.v1.OrderedDispatchReport",
        "dataservices.v1.EventResult",
        "dataservices.v1.EventChainRequest",
        "dataservices.v1.EventChainResponse",
        "dataservices.v1.EventGate",
        "dataservices.v1.GetEventGateRequest",
        "dataservices.v1.GetEventGateResponse",
        "dataservices.v1.DisableEventRequest",
        "dataservices.v1.DisableEventResponse",
        "dataservices.v1.EnableEventRequest",
        "dataservices.v1.EnableEventResponse",
        "dataservices.v1.UpdateRequest",
        "dataservices.v1.UpdateResponse",
        // The per-column refusal C-03's Update answers when a payload fails validation BEFORE any
        // statement is generated. Its own message rather than a bare string list, because a caller
        // correcting a payload needs the address it used - buffer, row, name, ordinal - beside the
        // declared type and the oracle's own structured dialog.
        "dataservices.v1.RowValidationError",
        "dataservices.v1.DropDownSearchState",
        "dataservices.v1.PinyinLike",
        "dataservices.v1.ColumnSortState",
        "dataservices.v1.ColumnSortState.ColumnSort",
        "dataservices.v1.ContextMenuItem",
        "dataservices.v1.ContextMenuModel",
        "dataservices.v1.RowSelectState",
        "dataservices.v1.GetDropDownSearchStateRequest",
        "dataservices.v1.GetDropDownSearchStateResponse",
        "dataservices.v1.ApplyDropDownSearchRequest",
        "dataservices.v1.ApplyDropDownSearchResponse",
        "dataservices.v1.GetColumnSortStateRequest",
        "dataservices.v1.GetColumnSortStateResponse",
        "dataservices.v1.ApplyColumnSortRequest",
        "dataservices.v1.ApplyColumnSortResponse",
        "dataservices.v1.GetContextMenuModelRequest",
        "dataservices.v1.GetContextMenuModelResponse",
        "dataservices.v1.ApplyContextMenuModelRequest",
        "dataservices.v1.ApplyContextMenuModelResponse",
        "dataservices.v1.GetRowSelectStateRequest",
        "dataservices.v1.GetRowSelectStateResponse",
        "dataservices.v1.ApplyRowSelectStyleRequest",
        "dataservices.v1.ApplyRowSelectStyleResponse",
        "dataservices.v1.ColumnExpData",
        "dataservices.v1.ColumnData",
        "dataservices.v1.VarData",
        "dataservices.v1.FuncData",
        "dataservices.v1.GlobalVarData",
        "dataservices.v1.LocalVarData",
        "dataservices.v1.ForeignVarRef",
        "dataservices.v1.VarValue",
        "dataservices.v1.ExpressionSentinels",
        "dataservices.v1.MacroResult",
        "dataservices.v1.ExpressionBinding",
        "dataservices.v1.ColumnRef",
        "dataservices.v1.OpenExpressionSessionRequest",
        "dataservices.v1.OpenExpressionSessionResponse",
        "dataservices.v1.CloseExpressionSessionRequest",
        "dataservices.v1.CloseExpressionSessionResponse",
        "dataservices.v1.AddExpressionRequest",
        "dataservices.v1.AddExpressionResponse",
        "dataservices.v1.SetExpressionRequest",
        "dataservices.v1.SetExpressionResponse",
        "dataservices.v1.GetExpressionRequest",
        "dataservices.v1.GetExpressionResponse",
        "dataservices.v1.RemoveExpressionRequest",
        "dataservices.v1.RemoveExpressionResponse",
        "dataservices.v1.RemoveAllExpressionsRequest",
        "dataservices.v1.RemoveAllExpressionsResponse",
        "dataservices.v1.AddVariableRequest",
        "dataservices.v1.AddVariableResponse",
        "dataservices.v1.SetVariableRequest",
        "dataservices.v1.SetVariableResponse",
        "dataservices.v1.AddVariableExpressionRequest",
        "dataservices.v1.AddVariableExpressionResponse",
        "dataservices.v1.SetVariableExpressionRequest",
        "dataservices.v1.SetVariableExpressionResponse",
        "dataservices.v1.GetVariableExpressionRequest",
        "dataservices.v1.GetVariableExpressionResponse",
        "dataservices.v1.AddForeignVariableRequest",
        "dataservices.v1.AddForeignVariableResponse",
        "dataservices.v1.SetRelativeColumnsRequest",
        "dataservices.v1.SetRelativeColumnsResponse",
        "dataservices.v1.SetExpressionFlagRequest",
        "dataservices.v1.SetExpressionFlagResponse",
        "dataservices.v1.CalcRequest",
        "dataservices.v1.CalcResponse",
        "dataservices.v1.CalcResult",
        "dataservices.v1.CalcAllRequest",
        "dataservices.v1.CalcAllResponse",
        "dataservices.v1.CalcEmptyRequest",
        "dataservices.v1.CalcEmptyResponse",
        "dataservices.v1.CalcItemRequest",
        "dataservices.v1.CalcItemResponse",
        "dataservices.v1.SetEnabledRequest",
        "dataservices.v1.SetEnabledResponse",
        "dataservices.v1.SetTraceRequest",
        "dataservices.v1.SetTraceResponse",
        "dataservices.v1.GetServiceStateRequest",
        "dataservices.v1.GetServiceStateResponse",
        "dataservices.v1.GetExpressionStateRequest",
        "dataservices.v1.GetExpressionStateResponse",
        "dataservices.v1.EventStreamRequest",
        "dataservices.v1.EventStreamResponse",
        "dataservices.v1.ColumnExpEvent",
        "dataservices.v1.ColumnExpEvent.ItemChanged",
        "dataservices.v1.ColumnExpEvent.DoItemChanged",
        "dataservices.v1.ColumnExpEvent.VarChanged",
        "dataservices.v1.ExpressionError",
        "dataservices.v1.InvokeMethodRequest",
        "dataservices.v1.InvokeMethodResponse",
        "dataservices.v1.TraceChannelRequest",
        "dataservices.v1.TraceRecord",
        // ---- persistence.v1.proto  (88 authored messages) ----
        "persistence.v1.TaskHandle",
        "persistence.v1.SessionHandle",
        "persistence.v1.OperationStatus",
        "persistence.v1.PositionalParameter",
        "persistence.v1.SqlClauseSpec",
        // The typed carrier that replaced the opaque `bytes` payload on C-05's chunks and C-06's
        // update input. Declared HERE and not in common.v1 by the same admission test that promoted
        // DataWindowRow: only ONE sibling declares these two, so the shared vocabulary stays the set
        // of types both files genuinely need.
        "persistence.v1.CarrierBufferSegment",
        "persistence.v1.CarrierState",
        "persistence.v1.QuerySpec",
        "persistence.v1.CreateQueryTaskRequest",
        "persistence.v1.CreateQueryTaskResponse",
        "persistence.v1.ReleaseQueryTaskRequest",
        "persistence.v1.ReleaseQueryTaskResponse",
        "persistence.v1.ResetQueryTaskRequest",
        "persistence.v1.ResetQueryTaskResponse",
        "persistence.v1.SetChunkSizeRequest",
        "persistence.v1.SetChunkSizeResponse",
        "persistence.v1.SetMaxRowsRequest",
        "persistence.v1.SetMaxRowsResponse",
        "persistence.v1.SetWhereClauseRequest",
        "persistence.v1.SetWhereClauseResponse",
        "persistence.v1.SetOrderByClauseRequest",
        "persistence.v1.SetOrderByClauseResponse",
        "persistence.v1.SetPagingRequest",
        "persistence.v1.SetPagingResponse",
        "persistence.v1.SetPagedUniqueIndexColumnsRequest",
        "persistence.v1.SetPagedUniqueIndexColumnsResponse",
        "persistence.v1.QueryRequest",
        "persistence.v1.QueryResponse",
        "persistence.v1.QueryRowCount",
        "persistence.v1.QueryDataChunk",
        "persistence.v1.QueryChildDataChunk",
        "persistence.v1.QueryPageCounts",
        "persistence.v1.CountRequest",
        "persistence.v1.CountResponse",
        "persistence.v1.TableUpdateContract",
        "persistence.v1.PrepareUpdateRequest",
        "persistence.v1.PrepareUpdateResponse",
        "persistence.v1.UpdateRequest",
        "persistence.v1.UpdateCounts",
        "persistence.v1.UpdateResponse",
        "persistence.v1.CreateUpdateTaskRequest",
        "persistence.v1.CreateUpdateTaskResponse",
        "persistence.v1.ReleaseUpdateTaskRequest",
        "persistence.v1.ReleaseUpdateTaskResponse",
        "persistence.v1.ResetUpdateTaskRequest",
        "persistence.v1.ResetUpdateTaskResponse",
        "persistence.v1.CreateCommandTaskRequest",
        "persistence.v1.CreateCommandTaskResponse",
        "persistence.v1.ReleaseCommandTaskRequest",
        "persistence.v1.ReleaseCommandTaskResponse",
        "persistence.v1.ResetCommandTaskRequest",
        "persistence.v1.ResetCommandTaskResponse",
        "persistence.v1.SetCommandAutoCommitRequest",
        "persistence.v1.SetCommandAutoCommitResponse",
        "persistence.v1.SetCommandSqlRequest",
        "persistence.v1.SetCommandSqlResponse",
        "persistence.v1.ExecRequest",
        "persistence.v1.ExecResponse",
        "persistence.v1.ConnectionParameterFlags",
        "persistence.v1.PoolKeepAliveSettings",
        "persistence.v1.TransactionDescriptor",
        "persistence.v1.TransactionDescriptorView",
        "persistence.v1.BeginSessionRequest",
        "persistence.v1.BeginSessionResponse",
        "persistence.v1.EndSessionRequest",
        "persistence.v1.EndSessionResponse",
        "persistence.v1.GetTransactionDataRequest",
        "persistence.v1.GetTransactionDataResponse",
        "persistence.v1.SetTransactionAutoCommitRequest",
        "persistence.v1.SetTransactionAutoCommitResponse",
        "persistence.v1.AutoCommitRequest",
        "persistence.v1.AutoCommitResponse",
        "persistence.v1.CommitRequest",
        "persistence.v1.CommitResponse",
        "persistence.v1.RollbackRequest",
        "persistence.v1.RollbackResponse",
        "persistence.v1.IsConnectedRequest",
        "persistence.v1.IsConnectedResponse",
        "persistence.v1.GetDatabaseTypeRequest",
        "persistence.v1.GetDatabaseTypeResponse",
        "persistence.v1.GetSessionStateRequest",
        "persistence.v1.GetSessionStateResponse",
        "persistence.v1.ClearStateRequest",
        "persistence.v1.ClearStateResponse",
        "persistence.v1.SetBrokenRequest",
        "persistence.v1.SetBrokenResponse",
        "persistence.v1.GridSyntaxFromSqlRequest",
        "persistence.v1.GridSyntaxFromSqlResponse",
    ];

    // ==============================================================================================
    //  GENERATED-SIGNATURE SHAPE MAPPING
    //
    //  These four helpers encode what Grpc.Tools emits for each of the four possible streaming
    //  directions. They are total functions of the direction rather than of today's roster, which is
    //  why the client-streaming-only arm is present even though no contract currently uses it - the
    //  census test below asserts that fact explicitly rather than leaving it implied.
    // ==============================================================================================

    /// <summary>
    /// The closed <c>Async*Call</c> type the generated client returns for a method of this direction.
    /// </summary>
    /// <remarks>
    /// The direction is supplied by the CALLER rather than read off the descriptor, so that the theory
    /// row remains the specification and the descriptor contributes only the message types. Reading the
    /// direction off the descriptor here would make the assertion self-fulfilling.
    /// </remarks>
    private static Type ExpectedClientCallType(
        bool clientStreaming,
        bool serverStreaming,
        Type request,
        Type response)
    {
        if (clientStreaming)
        {
            return serverStreaming
                ? typeof(AsyncDuplexStreamingCall<,>).MakeGenericType(request, response)
                : typeof(AsyncClientStreamingCall<,>).MakeGenericType(request, response);
        }

        return serverStreaming
            ? typeof(AsyncServerStreamingCall<>).MakeGenericType(response)
            : typeof(AsyncUnaryCall<>).MakeGenericType(response);
    }

    /// <summary>
    /// The name the generated client gives a method. Streaming calls keep the rpc name; a unary call's
    /// asynchronous form takes the <c>Async</c> suffix, because its blocking form already owns the bare
    /// name.
    /// </summary>
    private static string ExpectedClientMethodName(
        bool clientStreaming,
        bool serverStreaming,
        string methodName) =>
        clientStreaming || serverStreaming ? methodName : $"{methodName}Async";

    /// <summary>The return type of the generated server base's handler for a method.</summary>
    /// <remarks>
    /// A handler that writes a response STREAM returns a bare task, because its results leave through
    /// the writer rather than through the return value; every other shape returns the single response.
    /// </remarks>
    private static Type ExpectedHandlerReturnType(bool serverStreaming, Type response) =>
        serverStreaming ? typeof(Task) : typeof(Task<>).MakeGenericType(response);

    /// <summary>The parameter types of the generated server base's handler, in order.</summary>
    private static Type[] ExpectedHandlerParameterTypes(
        bool clientStreaming,
        bool serverStreaming,
        Type request,
        Type response)
    {
        List<Type> parameters =
        [
            clientStreaming ? typeof(IAsyncStreamReader<>).MakeGenericType(request) : request,
        ];

        if (serverStreaming)
        {
            parameters.Add(typeof(IServerStreamWriter<>).MakeGenericType(response));
        }

        parameters.Add(typeof(ServerCallContext));
        return [.. parameters];
    }

    /// <summary>Names a streaming direction the way a failure message should read it back.</summary>
    private static string DescribeDirection(bool clientStreaming, bool serverStreaming) =>
        (clientStreaming, serverStreaming) switch
        {
            (true, true) => "bidirectional streaming",
            (true, false) => "client streaming",
            (false, true) => "server streaming",
            _ => "unary",
        };

    /// <summary>
    /// The value of a generated type's public static <c>Descriptor</c> property, or
    /// <see langword="null"/> when it declares none.
    /// </summary>
    /// <remarks>
    /// Every generated message class and every generated service container exposes this property. Its
    /// value round-tripping BY REFERENCE to the descriptor the test started from is the strongest
    /// available statement that the generated CLR surface and the wire contract are one artifact rather
    /// than two that happen to agree.
    /// </remarks>
    private static object? StaticDescriptorOf(Type generated) =>
        generated.GetProperty("Descriptor", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);

    /// <summary>
    /// Asserts that a message descriptor genuinely materialised: a CLR type implementing
    /// <see cref="IMessage"/>, a parser closed over that type, and a descriptor that round-trips.
    /// </summary>
    /// <param name="message">The descriptor under test.</param>
    /// <param name="role">How the failure message should describe it.</param>
    private static void AssertMaterialisedAsGeneratedMessage(MessageDescriptor message, string role)
    {
        Assert.False(
            message.IsMapEntry,
            $"{role} is a synthetic map-entry message, which protoc never projects to a CLR type. A "
            + "test whose subject is authored messages must exclude map entries explicitly.");

        Assert.NotNull(message.ClrType);
        Assert.True(
            typeof(IMessage).IsAssignableFrom(message.ClrType),
            $"{role} generated '{message.ClrType.FullName}', which does not implement "
            + "Google.Protobuf.IMessage and therefore cannot be sent or received.");

        // A DECLARED message and a GENERATED one are different things, and the parser is what separates
        // them: it exists only where protoc emitted real serialization code.
        Assert.NotNull(message.Parser);
        Assert.Equal(typeof(MessageParser<>).MakeGenericType(message.ClrType), message.Parser.GetType());

        Assert.Same(message, StaticDescriptorOf(message.ClrType));
    }

    /// <summary>The public methods a generated type declares under one name, with their return types.</summary>
    /// <remarks>
    /// Distinct return types, because every generated overload group agrees on its return type - a
    /// unary client emits two <c>Async</c> overloads differing only in how call options are supplied.
    /// Collapsing them means the assertion reads as "this name resolves to exactly this one shape".
    /// </remarks>
    private static Type[] DeclaredReturnTypesOf(Type generated, string methodName) =>
        [.. generated
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(candidate => string.Equals(candidate.Name, methodName, StringComparison.Ordinal))
            .Select(static candidate => candidate.ReturnType)
            .Distinct()];

    /// <summary>The distinct FIRST parameter types the overloads declared under one name accept.</summary>
    /// <remarks>
    /// The client half of a non-client-streaming call is the one place where the request type appears
    /// ONLY in the parameter list: a unary call returns <c>AsyncUnaryCall&lt;TResponse&gt;</c> and a
    /// server-streaming call returns <c>AsyncServerStreamingCall&lt;TResponse&gt;</c>, so neither return
    /// type mentions the request at all. Checking the return type alone therefore cannot see a request
    /// message swapped for another - which is exactly the fault a frozen inventory exists to catch, so
    /// the parameter is checked too.
    /// <para>
    /// Both generated overloads take the request first and differ only in how call options arrive, so a
    /// correct client yields exactly one distinct first parameter type.
    /// </para>
    /// </remarks>
    private static Type[] DeclaredFirstParameterTypesOf(Type generated, string methodName) =>
        [.. generated
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(candidate => string.Equals(candidate.Name, methodName, StringComparison.Ordinal))
            .Select(static candidate => candidate.GetParameters())
            .Where(static parameters => parameters.Length > 0)
            .Select(static parameters => parameters[0].ParameterType)
            .Distinct()];

    // ==============================================================================================
    //  THEORY DATA - EVERY ROW IS ONE FACT
    //
    //  Rows carry only strings and booleans so they stay serializable, and every row is addressed by
    //  NAME with the descriptor resolved inside the test through ContractDescriptors. That keeps the
    //  single lookup mechanism this folder already owns as the only one, and it keeps the row itself
    //  readable in the test explorer.
    // ==============================================================================================

    /// <summary>Each published definition with its proto package and its declared C# namespace.</summary>
    public static TheoryData<string, string, string> ContractFiles =>
        new()
        {
            { CommonFileName, CommonPackage, CommonNamespace },
            { DataServicesFileName, DataServicesPackage, DataServicesNamespace },
            { PersistenceFileName, PersistencePackage, PersistenceNamespace },
        };

    /// <summary>The two definitions that declare services and therefore import the shared vocabulary.</summary>
    public static TheoryData<string> BoundaryFileNames =>
        new() { DataServicesFileName, PersistenceFileName };

    /// <summary>Contract identifier, declaring definition and service name, for the descriptor side.</summary>
    public static TheoryData<string, string, string> InScopeServiceDeclarations
    {
        get
        {
            TheoryData<string, string, string> rows = new();
            foreach (ServiceContract contract in Roster)
            {
                rows.Add(contract.ContractId, contract.ProtoFileName, contract.ServiceName);
            }

            return rows;
        }
    }

    /// <summary>Contract identifier, target namespace and service name, for the generated-type side.</summary>
    public static TheoryData<string, string, string> InScopeServiceGeneratedSurfaces
    {
        get
        {
            TheoryData<string, string, string> rows = new();
            foreach (ServiceContract contract in Roster)
            {
                rows.Add(contract.ContractId, contract.CsharpNamespace, contract.ServiceName);
            }

            return rows;
        }
    }

    /// <summary>
    /// All 77 methods this phase requires, each with its streaming direction and both of its message
    /// names, projected straight from the frozen roster.
    /// </summary>
    public static TheoryData<string, string, string, bool, bool, string, string> RequiredMethods
    {
        get
        {
            TheoryData<string, string, string, bool, bool, string, string> rows = new();
            foreach (RequiredMethod required in RequiredMethodRoster)
            {
                rows.Add(
                    required.ContractId,
                    required.ServiceName,
                    required.MethodName,
                    required.ClientStreaming,
                    required.ServerStreaming,
                    required.RequestMessage,
                    required.ResponseMessage);
            }

            return rows;
        }
    }

    /// <summary>Each in-scope service with the complete, ordered set of rpc names its contract freezes.</summary>
    /// <remarks>
    /// The method names arrive as one tab-free, comma-joined string because a theory row must stay
    /// serializable and xunit will not serialize a string array. The test splits it again; what matters
    /// is that the EXPECTED set is stated here rather than read from the service being examined.
    /// </remarks>
    public static TheoryData<string, string, string> ServiceMethodInventories
    {
        get
        {
            TheoryData<string, string, string> rows = new();
            foreach (ServiceContract contract in Roster)
            {
                rows.Add(
                    contract.ContractId,
                    contract.ServiceName,
                    string.Join(
                        ",",
                        RequiredMethodRoster
                            .Where(required => string.Equals(
                                required.ServiceName,
                                contract.ServiceName,
                                StringComparison.Ordinal))
                            .Select(static required => required.MethodName)));
            }

            return rows;
        }
    }

    /// <summary>
    /// Every message AUTHORED in the three definitions, nested declarations included and the synthetic
    /// map-entry messages excluded.
    /// </summary>
    /// <remarks>
    /// The exclusion is deliberate and is asserted in its own right below rather than left silent. A
    /// <c>map</c> field makes protoc synthesise a nested <c>...Entry</c> message that appears in the
    /// descriptor graph but is never projected to a CLR type, so including one here would fail the
    /// materialisation theory for a reason that is correct behaviour.
    /// <para>
    /// Projected from the FROZEN roster, not from <c>ContractDescriptors.AllMessages()</c>. Derived from
    /// the descriptor graph this theory could never report a deleted message, because the deletion takes
    /// the row that would have failed away with it.
    /// </para>
    /// </remarks>
    public static TheoryData<string> AuthoredMessageNames
    {
        get
        {
            TheoryData<string> rows = new();
            foreach (string messageFullName in AuthoredMessageRoster)
            {
                rows.Add(messageFullName);
            }

            return rows;
        }
    }

    /// <summary>Every method of every published service, as (service full name, method name).</summary>
    /// <remarks>
    /// Projected from the frozen roster for the same reason as the message inventory above, with the
    /// service's fully-qualified proto name reconstructed from the package its declaring definition
    /// carries rather than read off the descriptor.
    /// </remarks>
    public static TheoryData<string, string> ServiceMethodKeys
    {
        get
        {
            TheoryData<string, string> rows = new();
            foreach (RequiredMethod required in RequiredMethodRoster)
            {
                rows.Add(
                    ProtoServiceFullName(RequireRosterEntry(required.ServiceName)),
                    required.MethodName);
            }

            return rows;
        }
    }

    /// <summary>
    /// The shared-vocabulary MESSAGES both boundary definitions depend on, by fully-qualified name.
    /// </summary>
    /// <remarks>
    /// Fully qualified throughout, because simple names are genuinely ambiguous in this contract set -
    /// <c>UpdateRequest</c> and <c>UpdateResponse</c> are each declared in both boundary definitions.
    /// </remarks>
    public static TheoryData<string> SharedVocabularyMessages =>
        new()
        {
            "common.v1.RetCode",
            "common.v1.XmlParseStatus",
            "common.v1.SqliteResultCode",
            "common.v1.DbError",
            "common.v1.ConflictRow",
            "common.v1.ConflictDetail",
            "common.v1.IdentityColumnData",
            "common.v1.RichError",
        };

    /// <summary>The shared-vocabulary ENUMS, by fully-qualified name.</summary>
    /// <remarks>
    /// Three of the five are NESTED inside a wrapper message, which is the layout as found rather than
    /// the layout one might assume; the wrapper test below records why.
    /// </remarks>
    public static TheoryData<string> SharedVocabularyEnums =>
        new()
        {
            "common.v1.RetCode.Value",
            "common.v1.XmlParseStatus.Value",
            "common.v1.SqliteResultCode.Value",
            "common.v1.DwBuffer",
            "common.v1.ItemStatus",
        };

    // ==============================================================================================
    //  1. THE DEFINITIONS RESOLVE, AND THEY GENERATED INTO THE NAMESPACES THEY DECLARE
    // ==============================================================================================

    [Theory]
    [MemberData(nameof(ContractFiles))]
    public void EachPublishedDefinitionResolvesAsADescriptorDeclaringItsPackageAndCsharpNamespace(
        string protoFileName,
        string protoPackage,
        string csharpNamespace)
    {
        FileDescriptor file = RequireFile(protoFileName);

        // THE VERSION LIVES IN THE PACKAGE NAME, which is what lets a breaking change ship as a
        // parallel `.v2` package while `.v1` keeps serving its existing consumers.
        Assert.Equal(protoPackage, file.Package);

        // The DECLARED intention. What the generator actually did with it is the next test's subject;
        // the two together are what prove the declaration was honoured rather than merely written.
        Assert.Equal(csharpNamespace, file.GetOptions()?.CsharpNamespace);
    }

    [Theory]
    [MemberData(nameof(ContractFiles))]
    public void EachPublishedDefinitionGeneratedItsTypesIntoTheNamespaceItDeclares(
        string protoFileName,
        string protoPackage,
        string csharpNamespace)
    {
        MessageDescriptor[] authored =
            [.. ContractDescriptors.AllMessages()
                .Where(message => !message.IsMapEntry
                    && string.Equals(message.File.Name, protoFileName, StringComparison.Ordinal))];

        Assert.NotEmpty(authored);

        // MECHANISM, RECORDED DELIBERATELY: the generated namespace is read from
        // MessageDescriptor.ClrType.Namespace - the namespace of the type protoc actually emitted -
        // rather than from the csharp_namespace option asserted above. The option states an intention;
        // the CLR type is the outcome. Reading the outcome is what makes this an assertion about code
        // GENERATION rather than about the contents of a .proto file.
        string[] generatedNamespaces =
            [.. authored
                .Select(static message => message.ClrType.Namespace ?? "<global namespace>")
                .Distinct()];

        Assert.Equal([csharpNamespace], generatedNamespaces);

        // The wire identity is package-qualified across every message, nested declarations included.
        // That is what keeps the wire name stable independently of the C# projection, so the two can
        // follow their own conventions - lower-case dotted on the wire, PascalCase in the CLR.
        Assert.All(
            authored,
            message => Assert.StartsWith($"{protoPackage}.", message.FullName, StringComparison.Ordinal));
    }

    [Fact]
    public void TheBoundaryPublishesExactlyTheThreeProtocolDefinitionsOfThisPhase()
    {
        // AN EXACT COMPARISON, NOT A "CONTAINS" CHECK. Four deferred capabilities receive no code in
        // this phase, so a fourth protocol definition appearing here - a design, documents, integration
        // or scripting contract - must FAIL rather than slip past unnoticed.
        Assert.Equal(
            [CommonFileName, DataServicesFileName, PersistenceFileName],
            ContractDescriptors.All.Select(static file => file.Name));
    }

    [Theory]
    [MemberData(nameof(BoundaryFileNames))]
    public void EachBoundaryDefinitionImportsTheSharedVocabularyByItsBareFileName(string protoFileName)
    {
        FileDescriptor file = RequireFile(protoFileName);

        // THIS PINS ProtoRoot="Proto". With the root set, the import is written and reported as the
        // BARE file name; without it Grpc.Tools defaults the root to the project directory, every import
        // would need a Proto/ prefix, and the mismatch surfaces as a protoc "file not found" that names
        // no cause. Asserting the dependency set EXACTLY also keeps the shared vocabulary the only thing
        // a boundary definition may depend on.
        Assert.Equal(
            [CommonFileName],
            file.Dependencies.Select(static dependency => dependency.Name));
    }

    [Fact]
    public void TheSharedVocabularyDependsOnlyOnTheWellKnownDefinitionItsCustomOptionNeeds()
    {
        // common.v1.proto declares `extend google.protobuf.MethodOptions` for its rich-error option, so
        // descriptor.proto is a genuine and sufficient dependency. It must depend on NEITHER boundary
        // definition: the layering runs one way only, and an exact comparison is what keeps it acyclic.
        Assert.Equal(
            [DescriptorProtoFileName],
            ContractDescriptors.Common.Dependencies.Select(static dependency => dependency.Name));
    }

    // ==============================================================================================
    //  2. THE SIX SERVICES, WITH A SERVER BASE *AND* A CLIENT - THE GrpcServices="Both" PROOF
    // ==============================================================================================

    [Theory]
    [MemberData(nameof(InScopeServiceDeclarations))]
    public void EachInScopeContractDeclaresItsServiceInItsOwnProtocolDefinition(
        string contractId,
        string protoFileName,
        string serviceName)
    {
        ServiceDescriptor service = ContractDescriptors.RequireService(serviceName);

        Assert.True(
            string.Equals(service.File.Name, protoFileName, StringComparison.Ordinal),
            $"Contract {contractId} must be declared in '{protoFileName}', but '{service.FullName}' is "
            + $"declared in '{service.File.Name}'. A contract declared in the wrong definition would "
            + "version with the wrong file, which defeats the point of separating C-03 from C-04.");

        Assert.NotEmpty(service.Methods);
    }

    [Theory]
    [MemberData(nameof(InScopeServiceGeneratedSurfaces))]
    public void EachInScopeServiceGeneratedItsContainerTypeIntoItsDeclaredNamespace(
        string contractId,
        string csharpNamespace,
        string serviceName)
    {
        Type container = RequireServiceContainer(csharpNamespace, serviceName);

        Assert.Equal(csharpNamespace, container.Namespace);

        // A C# `static class` IS abstract AND sealed in IL. Stating that here is not pedantry: the next
        // test identifies the SERVER BASE as "abstract and not sealed", and without this distinction the
        // container itself would satisfy a naive "is abstract" check.
        Assert.True(
            container.IsAbstract && container.IsSealed,
            $"Contract {contractId}'s generated container '{container.FullName}' must be the static "
            + "class Grpc.Tools emits, which IL represents as abstract and sealed together.");

        // The container ties itself back to the descriptor the tests resolve, by reference.
        Assert.Same(ContractDescriptors.RequireService(serviceName), StaticDescriptorOf(container));
    }

    [Theory]
    [MemberData(nameof(InScopeServiceGeneratedSurfaces))]
    public void EachInScopeServiceGeneratedAnAbstractServerBaseForItsImplementationToDeriveFrom(
        string contractId,
        string csharpNamespace,
        string serviceName)
    {
        Type container = RequireServiceContainer(csharpNamespace, serviceName);
        Type serverBase = RequireNestedType(
            container,
            $"{serviceName}Base",
            $"Contract {contractId}'s SERVER half is missing. DataServices and Persistence derive their "
            + "service implementations from this type, so its absence means the <Protobuf> item was "
            + "compiled with GrpcServices=\"Client\" or \"None\" rather than \"Both\".");

        Assert.True(
            serverBase.IsAbstract && !serverBase.IsSealed,
            $"'{serverBase.FullName}' must be an abstract class for a service implementation to override "
            + "its handlers; abstract AND sealed would mean a static class, which cannot be derived from.");

        Assert.False(
            typeof(ClientBase).IsAssignableFrom(serverBase),
            $"'{serverBase.FullName}' must be the server half, not the client half.");
    }

    [Theory]
    [MemberData(nameof(InScopeServiceGeneratedSurfaces))]
    public void EachInScopeServiceGeneratedAClientDerivingFromClientBase(
        string contractId,
        string csharpNamespace,
        string serviceName)
    {
        Type container = RequireServiceContainer(csharpNamespace, serviceName);
        Type client = RequireNestedType(
            container,
            $"{serviceName}Client",
            $"Contract {contractId}'s CLIENT half is missing, and that is exactly the failure this suite "
            + $"exists to catch: {GrpcServicesDiagnosis}. Gateway and DataServices call through this "
            + "type, so nothing downstream can reach the service without it.");

        Assert.True(
            typeof(ClientBase).IsAssignableFrom(client),
            $"'{client.FullName}' must derive from Grpc.Core.ClientBase to carry a call invoker.");

        Assert.False(
            client.IsAbstract,
            $"'{client.FullName}' must be concrete: a consumer constructs it directly from a channel.");

        // The generated client is self-referentially typed, ClientBase<TSelf>, which is what lets
        // WithHost and similar members return the derived type rather than the base.
        Assert.Equal(typeof(ClientBase<>).MakeGenericType(client).FullName, client.BaseType?.FullName);

        // CONSTRUCTIBLE IN SHAPE ONLY - NOTHING IS CONSTRUCTED HERE. Asserting the two public
        // single-argument constructors proves a consumer can bind the client to a channel or to a call
        // invoker; actually creating one would drag a transport into a suite that must stay pure.
        Assert.Equal(
            [typeof(CallInvoker), typeof(ChannelBase)],
            client.GetConstructors()
                .Where(static constructor => constructor.GetParameters().Length == 1)
                .Select(static constructor => constructor.GetParameters()[0].ParameterType)
                .OrderBy(static parameterType => parameterType.Name, StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(InScopeServiceGeneratedSurfaces))]
    public void EachGeneratedContainerExposesExactlyItsServerBaseAndItsClient(
        string contractId,
        string csharpNamespace,
        string serviceName)
    {
        Type container = RequireServiceContainer(csharpNamespace, serviceName);
        string[] expected = [$"{serviceName}Base", $"{serviceName}Client"];
        string[] actual = PublicNestedTypeNames(container);

        Assert.True(
            expected.SequenceEqual(actual, StringComparer.Ordinal),
            $"Contract {contractId} must publish exactly its server base and its client. Expected "
            + $"[{string.Join(", ", expected)}] but '{container.FullName}' publishes "
            + $"[{string.Join(", ", actual)}]. Both halves are required, and one without the other is "
            + $"the diagnosis this suite carries: {GrpcServicesDiagnosis}.");
    }

    [Fact]
    public void TheSharedVocabularyGeneratedNoServiceSurfaceBecauseItDeclaresNoService()
    {
        Assert.Empty(ContractDescriptors.Common.Services);

        // A SERVICE IN THE SHARED LAYER WOULD BE A CROSS-SERVICE COUPLING OF ITS OWN. The shared
        // vocabulary exists so DataServices and Persistence describe the same concepts identically; the
        // moment it gains a boundary it becomes the shared-code back door the published contracts are
        // meant to replace.
        Assert.DoesNotContain(
            ContractsAssembly.GetTypes(),
            candidate => string.Equals(candidate.Namespace, CommonNamespace, StringComparison.Ordinal)
                && typeof(ClientBase).IsAssignableFrom(candidate));
    }

    [Fact]
    public void ThePublishedBoundaryDeclaresExactlyTheSixServicesOfThisPhase()
    {
        string[] declared =
            [.. ContractDescriptors.AllServices()
                .Select(static service => service.FullName)
                .OrderBy(static name => name, StringComparer.Ordinal)];

        string[] frozen =
            [.. Roster
                .Select(ProtoServiceFullName)
                .OrderBy(static name => name, StringComparer.Ordinal)];

        // THE SERVICE SET, BOTH WAYS, AND NOT COVERED BY THE RPC COMPARISON BELOW. That one compares
        // service-qualified METHOD keys, so a seventh service carrying no rpc at all would pass it
        // unnoticed - and an empty service is precisely the shape a capability arrives in on its first
        // commit. A seventh service here is also a C-D violation if it belongs to a deferred capability:
        // AAP 0.2.2.2 permits no definition, project, container or placeholder for DesignSystem,
        // Documents, Integration or ScriptBridge, which surface only as Gateway's reserved 501 routes.
        Assert.Equal(frozen, declared);

        Assert.Equal(6, declared.Length);
    }

    [Fact]
    public void TheAssemblyPublishesOneServerBaseAndOneClientPerInScopeContractAndNoOthers()
    {
        string[] expectedServerBases = ExpectedNestedFullNames("Base");
        string[] expectedClients = ExpectedNestedFullNames("Client");

        // "abstract and not sealed" is the server-base signature; the generated containers are static
        // classes and so are excluded by the sealed test, and the generated message classes are neither
        // abstract nor nested.
        string[] actualServerBases =
            [.. ContractsAssembly.GetTypes()
                .Where(static candidate =>
                    candidate.IsNestedPublic && candidate.IsAbstract && !candidate.IsSealed)
                .Select(static candidate => candidate.FullName ?? candidate.Name)
                .OrderBy(static name => name, StringComparer.Ordinal)];

        string[] actualClients =
            [.. ContractsAssembly.GetTypes()
                .Where(static candidate =>
                    candidate.IsNestedPublic && typeof(ClientBase).IsAssignableFrom(candidate))
                .Select(static candidate => candidate.FullName ?? candidate.Name)
                .OrderBy(static name => name, StringComparer.Ordinal)];

        // SIX AND SIX, EXACTLY. Fewer server bases or fewer clients is a GrpcServices misconfiguration;
        // more of either is a service that should not exist in this phase at all.
        Assert.Equal(expectedServerBases, actualServerBases);
        Assert.Equal(expectedClients, actualClients);
    }

    /// <summary>The full CLR names the roster requires for a given nested-type suffix.</summary>
    private static string[] ExpectedNestedFullNames(string suffix) =>
        [.. Roster
            .Select(contract =>
                $"{contract.CsharpNamespace}.{contract.ServiceName}+{contract.ServiceName}{suffix}")
            .OrderBy(static name => name, StringComparer.Ordinal)];

    // ==============================================================================================
    //  3. THE REQUIRED METHODS, ON THE DESCRIPTOR AND ON BOTH GENERATED HALVES
    //
    //  Streaming direction is asserted three times on purpose - once on the descriptor, once on the
    //  generated client and once on the generated server base - because those are three independent
    //  artifacts. A direction that survived into the descriptor but not into a generated signature would
    //  be a generator or toolchain fault, and it would compile.
    //
    //  The two completeness facts come FIRST, because every per-row theory below is conditional on them:
    //  a theory can only report on the rows it is given, so without an exact both-ways comparison a
    //  deleted rpc simply stops being tested. These two close that gap for the rpc surface, and the
    //  message-inventory fact in section 4 closes it for the messages.
    // ==============================================================================================

    [Fact]
    public void ThePublishedBoundaryDeclaresExactlyTheRpcsTheFrozenInventoryNames()
    {
        string[] declared =
            [.. ContractDescriptors.AllMethods()
                .Select(static method => $"{method.Service.FullName}.{method.Name}")
                .OrderBy(static key => key, StringComparer.Ordinal)];

        string[] frozen =
            [.. RequiredMethodRoster
                .Select(static required =>
                    $"{ProtoServiceFullName(RequireRosterEntry(required.ServiceName))}.{required.MethodName}")
                .OrderBy(static key => key, StringComparer.Ordinal)];

        // BOTH DIRECTIONS, AND THE TWO ASYMMETRIES ARE DIFFERENT FAULTS WORTH DIFFERENT MESSAGES.
        // Missing means the boundary lost an rpc the contract promises - a caller's generated client stops
        // compiling. Undeclared means an rpc reached the published boundary without passing through the
        // reviewed inventory, which is how an unreviewed surface arrives.
        string[] missing = [.. frozen.Except(declared, StringComparer.Ordinal)];
        string[] undeclared = [.. declared.Except(frozen, StringComparer.Ordinal)];

        Assert.True(
            missing.Length == 0,
            $"The frozen inventory names {missing.Length} rpc(s) the published boundary does not declare: "
            + $"[{string.Join(", ", missing)}]. Either the definition lost them or the inventory is ahead "
            + "of the contract; the contract is frozen, so the definition is the side to look at.");

        Assert.True(
            undeclared.Length == 0,
            $"The published boundary declares {undeclared.Length} rpc(s) the frozen inventory does not "
            + $"name: [{string.Join(", ", undeclared)}]. A new rpc must be added to the inventory in the "
            + "same change that adds it to the definition, so that its direction and both of its message "
            + "names are reviewed rather than inferred from whatever was generated.");

        // Stated as a count as well, so the failure message carries the size of the surface a reader is
        // being asked to trust. 77 is the whole of C-03 through C-08 - 78 while C-04 also carried the
        // withdrawn row-loading rpc, which AAP 0.4.3's frozen inventory never named.
        Assert.Equal(77, declared.Length);
    }

    [Theory]
    [MemberData(nameof(ServiceMethodInventories))]
    public void EachInScopeServiceDeclaresExactlyTheRpcsItsContractFreezes(
        string contractId,
        string serviceName,
        string frozenMethodNames)
    {
        ServiceDescriptor service = ContractDescriptors.RequireService(serviceName);

        string[] declared =
            [.. service.Methods
                .Select(static method => method.Name)
                .OrderBy(static name => name, StringComparer.Ordinal)];

        string[] frozen =
            [.. frozenMethodNames
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .OrderBy(static name => name, StringComparer.Ordinal)];

        // PER SERVICE AS WELL AS IN AGGREGATE, because the aggregate comparison above would still pass if
        // an rpc MOVED between two services - the key set would be unchanged in size but wrong in owner,
        // and a method on the wrong service is a different client, a different authorization boundary and
        // a different deployable.
        Assert.Equal(frozen, declared);

        Assert.Equal(
            RequiredMethodRoster.Count(required => string.Equals(
                required.ServiceName,
                serviceName,
                StringComparison.Ordinal)),
            service.Methods.Count);

        Assert.Equal(
            ProtoServiceFullName(RequireRosterEntry(serviceName)),
            service.FullName);

        Assert.Equal(
            contractId,
            RequireRosterEntry(serviceName).ContractId);
    }

    [Theory]
    [MemberData(nameof(RequiredMethods))]
    public void EachRequiredMethodIsDeclaredWithTheStreamingDirectionItsContractRequires(
        string contractId,
        string serviceName,
        string methodName,
        bool clientStreaming,
        bool serverStreaming,
        string requestMessage,
        string responseMessage)
    {
        MethodDescriptor method = ContractDescriptors.RequireMethod(
            ContractDescriptors.RequireService(serviceName),
            methodName);

        Assert.True(
            method.IsClientStreaming == clientStreaming && method.IsServerStreaming == serverStreaming,
            $"Contract {contractId}'s '{serviceName}.{methodName}' must be "
            + $"{DescribeDirection(clientStreaming, serverStreaming)} but is "
            + $"{DescribeDirection(method.IsClientStreaming, method.IsServerStreaming)}. Direction is part "
            + "of the contract rather than an implementation detail: the ordered event chain and the two "
            + "inverted channels cannot be expressed at all without it.");

        // THE MESSAGE IDENTITY IS ASSERTED HERE AND NOWHERE ELSE ON THE DESCRIPTOR SIDE. A request type
        // exchanged for a different message that happens to carry compatible fields compiles, serializes
        // and passes every shape check below, because those check the SHAPE the direction implies rather
        // than WHICH message fills it. Only a frozen name catches it.
        Assert.Equal(requestMessage, method.InputType.FullName);
        Assert.Equal(responseMessage, method.OutputType.FullName);
    }

    [Theory]
    [MemberData(nameof(RequiredMethods))]
    public void TheGeneratedClientCarriesEachRequiredMethodWithTheCallShapeItsDirectionImplies(
        string contractId,
        string serviceName,
        string methodName,
        bool clientStreaming,
        bool serverStreaming,
        string requestMessage,
        string responseMessage)
    {
        ServiceContract contract = RequireRosterEntry(serviceName);

        Type client = RequireNestedType(
            RequireServiceContainer(contract.CsharpNamespace, serviceName),
            $"{serviceName}Client",
            $"Contract {contractId} cannot be called at all without its client: {GrpcServicesDiagnosis}.");

        // BOTH MESSAGE TYPES COME FROM THE FROZEN NAMES, NOT FROM THE DESCRIPTOR. That is the whole
        // difference between this assertion and a tautology: the descriptor would supply whatever the
        // generator produced and the comparison would then always hold.
        Type requestType = ExpectedGeneratedMessageType(
            requestMessage,
            $"Contract {contractId} declares it as the request of '{serviceName}.{methodName}'.");

        Type responseType = ExpectedGeneratedMessageType(
            responseMessage,
            $"Contract {contractId} declares it as the response of '{serviceName}.{methodName}'.");

        string expectedName = ExpectedClientMethodName(clientStreaming, serverStreaming, methodName);
        Type expectedCall = ExpectedClientCallType(
            clientStreaming,
            serverStreaming,
            requestType,
            responseType);

        // An exact single-element comparison, because every overload of one generated client method
        // agrees on its return type and differs only in how call options arrive. So this reads as "the
        // name resolves, and it resolves to exactly this one call shape".
        Assert.Equal([expectedCall], DeclaredReturnTypesOf(client, expectedName));

        if (!clientStreaming)
        {
            // THE RETURN TYPE OF A UNARY OR SERVER-STREAMING CALL DOES NOT MENTION ITS REQUEST, so the
            // assertion above cannot see a request message swapped for another - it would still return
            // AsyncUnaryCall<TResponse> and still pass. The first parameter is where the request type is
            // observable on this half, and the frozen name is what it is compared against.
            Assert.Equal([requestType], DeclaredFirstParameterTypesOf(client, expectedName));
        }
    }

    [Theory]
    [MemberData(nameof(RequiredMethods))]
    public void TheGeneratedServerBaseCarriesEachRequiredMethodWithTheHandlerShapeItsDirectionImplies(
        string contractId,
        string serviceName,
        string methodName,
        bool clientStreaming,
        bool serverStreaming,
        string requestMessage,
        string responseMessage)
    {
        ServiceContract contract = RequireRosterEntry(serviceName);

        Type requestType = ExpectedGeneratedMessageType(
            requestMessage,
            $"Contract {contractId} declares it as the request of '{serviceName}.{methodName}'.");

        Type responseType = ExpectedGeneratedMessageType(
            responseMessage,
            $"Contract {contractId} declares it as the response of '{serviceName}.{methodName}'.");

        Type serverBase = RequireNestedType(
            RequireServiceContainer(contract.CsharpNamespace, serviceName),
            $"{serviceName}Base",
            $"Contract {contractId} cannot be implemented at all without its server base.");

        MethodInfo[] handlers =
            [.. serverBase
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(candidate => string.Equals(candidate.Name, methodName, StringComparison.Ordinal))];

        MethodInfo handler = Assert.Single(handlers);

        Assert.Equal(
            ExpectedHandlerReturnType(serverStreaming, responseType),
            handler.ReturnType);

        // The parameter list is where the direction becomes visible on the server half: a streamed
        // request arrives as a reader and a streamed response leaves through a writer, and the call
        // context is always last. Both message types are the FROZEN ones, so a handler generated over a
        // different message fails here rather than agreeing with itself.
        Assert.Equal(
            ExpectedHandlerParameterTypes(
                clientStreaming,
                serverStreaming,
                requestType,
                responseType),
            handler.GetParameters().Select(static parameter => parameter.ParameterType));
    }

    [Fact]
    public void TheInvertedMacroChannelKeepsItsResponseTypeOnTheRequestSideInBothGeneratedHalves()
    {
        // THE INVERSION IS STRUCTURAL, NOT STYLISTIC, AND IT READS BACKWARDS ON PURPOSE.
        //
        // The legacy expects the APPLICATION to implement the macro switch: oncolumnexpinvokemethod is an
        // event the host handles, returning `any` over a `string[]` [se_cst_dw.sru:L14], and the
        // calculation cannot proceed until it answers. Across a service boundary that means the
        // expansion engine must call BACK INTO its client mid-calculation - and gRPC has no
        // server-initiated call, so the stream itself is inverted: the CLIENT sends RESPONSES and the
        // SERVER sends REQUESTS.
        //
        // Asserting it on the GENERATED SIGNATURES as well as on the descriptor is the point of doing it
        // here. A well-meaning correction of the direction would break macro invocation entirely while
        // continuing to compile on both sides.
        ServiceDescriptor service = ContractDescriptors.RequireService(
            "dataservices.v1.ColumnExpressionService");
        MethodDescriptor channel = ContractDescriptors.RequireMethod(service, "InvokeMethodChannel");

        Assert.Equal("dataservices.v1.InvokeMethodResponse", channel.InputType.FullName);
        Assert.Equal("dataservices.v1.InvokeMethodRequest", channel.OutputType.FullName);

        Type container = RequireServiceContainer(DataServicesNamespace, "ColumnExpressionService");
        Type client = RequireNestedType(
            container,
            "ColumnExpressionServiceClient",
            $"The inverted channel is unreachable without a client: {GrpcServicesDiagnosis}.");

        Assert.Equal(
            [typeof(AsyncDuplexStreamingCall<,>).MakeGenericType(
                channel.InputType.ClrType,
                channel.OutputType.ClrType)],
            DeclaredReturnTypesOf(client, "InvokeMethodChannel"));

        Type serverBase = RequireNestedType(
            container,
            "ColumnExpressionServiceBase",
            "The inverted channel cannot be implemented without a server base.");

        MethodInfo handler = Assert.Single(
            [.. serverBase
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(static candidate => string.Equals(
                    candidate.Name,
                    "InvokeMethodChannel",
                    StringComparison.Ordinal))]);

        Assert.Equal(
            [
                typeof(IAsyncStreamReader<>).MakeGenericType(channel.InputType.ClrType),
                typeof(IServerStreamWriter<>).MakeGenericType(channel.OutputType.ClrType),
                typeof(ServerCallContext),
            ],
            handler.GetParameters().Select(static parameter => parameter.ParameterType));
    }

    [Fact]
    public void TheStreamingCensusOfThePublishedBoundaryIsExactlyWhatTheContractsRequire()
    {
        string[] serverStreamingOnly = MethodKeysWhereDirectionIs(clientStreaming: false, serverStreaming: true);
        string[] bidirectional = MethodKeysWhereDirectionIs(clientStreaming: true, serverStreaming: true);
        string[] clientStreamingOnly = MethodKeysWhereDirectionIs(clientStreaming: true, serverStreaming: false);

        // Progressive delivery, three times over: DataWindow retrieval, the column-expression event feed
        // and the SQL query whose legacy analogue raises ondatareceived once per chunk
        // [n_cst_thread_task_sqlquery.sru:L21, L73].
        Assert.Equal(
            [
                "dataservices.v1.ColumnExpressionService.EventStream",
                "dataservices.v1.DataWindowService.Retrieve",
                "persistence.v1.QueryService.Query",
            ],
            serverStreamingOnly);

        // The ordered vetoable event chain plus the two inverted channels. Exactly three, and no more:
        // a duplex stream is the most expensive shape to implement correctly, so one appearing where a
        // unary call would do is worth failing over.
        Assert.Equal(
            [
                "dataservices.v1.ColumnExpressionService.InvokeMethodChannel",
                "dataservices.v1.ColumnExpressionService.TraceChannel",
                "dataservices.v1.DataWindowService.EventChain",
            ],
            bidirectional);

        // NO CONTRACT ASKS THE CLIENT TO STREAM WITHOUT THE SERVER STREAMING BACK. Recorded as a fact
        // rather than left implied, because the shape helpers above cover that direction for totality
        // and a reader is entitled to know whether anything exercises it.
        Assert.Empty(clientStreamingOnly);
    }

    /// <summary>Every published method of one streaming direction, as ordered service-qualified names.</summary>
    private static string[] MethodKeysWhereDirectionIs(bool clientStreaming, bool serverStreaming) =>
        [.. ContractDescriptors.AllMethods()
            .Where(method => method.IsClientStreaming == clientStreaming
                && method.IsServerStreaming == serverStreaming)
            .Select(static method => $"{method.Service.FullName}.{method.Name}")
            .OrderBy(static key => key, StringComparer.Ordinal)];

    [Fact]
    public void EveryUnaryMethodAlsoExposesABlockingClientOverloadReturningItsResponseDirectly()
    {
        List<string> mismatches = [];

        foreach (ServiceContract contract in Roster)
        {
            Type client = RequireNestedType(
                RequireServiceContainer(contract.CsharpNamespace, contract.ServiceName),
                $"{contract.ServiceName}Client",
                $"Contract {contract.ContractId} has no client: {GrpcServicesDiagnosis}.");

            ServiceDescriptor service = ContractDescriptors.RequireService(contract.ServiceName);

            foreach (MethodDescriptor method in service.Methods)
            {
                if (method.IsClientStreaming || method.IsServerStreaming)
                {
                    continue;
                }

                // A unary call generates BOTH forms: the bare name blocks and returns the response, and
                // the Async-suffixed name returns an AsyncUnaryCall. Only the blocking form is checked
                // here; the asynchronous form is the subject of the required-method theory above.
                Type[] returnTypes = DeclaredReturnTypesOf(client, method.Name);
                if (!returnTypes.SequenceEqual([method.OutputType.ClrType]))
                {
                    mismatches.Add(
                        $"{contract.ContractId} {service.FullName}.{method.Name} expected a blocking "
                        + $"overload returning '{method.OutputType.ClrType.FullName}' but found "
                        + $"[{string.Join(", ", returnTypes.Select(static type => type.FullName))}]");
                }
            }
        }

        Assert.Empty(mismatches);
    }

    // ==============================================================================================
    //  4. MESSAGE MATERIALISATION - DECLARED IS NOT THE SAME AS GENERATED
    // ==============================================================================================

    [Theory]
    [MemberData(nameof(AuthoredMessageNames))]
    public void EveryAuthoredMessageMaterialisedAsAGeneratedTypeWithItsOwnParser(string messageFullName)
    {
        MessageDescriptor message = ContractDescriptors.RequireMessage(messageFullName);

        AssertMaterialisedAsGeneratedMessage(message, $"Message '{messageFullName}'");

        // AND IT MATERIALISED WHERE THE FROZEN NAME SAYS IT SHOULD. The check above proves a CLR type
        // exists and round-trips to this descriptor; this one proves the type landed in the namespace the
        // declaring definition's csharp_namespace commits to, which is the part a consumer's `using`
        // depends on and the part a package rename would silently move.
        Assert.Same(
            ExpectedGeneratedMessageType(
                messageFullName,
                $"The frozen inventory names '{messageFullName}', so its generated type must appear at the "
                + "name its package's csharp_namespace implies."),
            message.ClrType);
    }

    [Fact]
    public void ThePublishedBoundaryAuthorsExactlyTheMessagesTheFrozenInventoryNames()
    {
        string[] authored =
            [.. ContractDescriptors.AllMessages()
                .Where(static message => !message.IsMapEntry)
                .Select(static message => message.FullName)
                .OrderBy(static name => name, StringComparer.Ordinal)];

        string[] frozen = [.. AuthoredMessageRoster.OrderBy(static name => name, StringComparer.Ordinal)];

        string[] missing = [.. frozen.Except(authored, StringComparer.Ordinal)];
        string[] unlisted = [.. authored.Except(frozen, StringComparer.Ordinal)];

        // THE SAME BOTH-WAYS COMPARISON THE RPC SURFACE GETS, AND FOR THE SAME REASON. The per-message
        // materialisation theory above runs one row per name in the frozen list, so a message deleted from
        // a definition would take its own row away and the suite would still be green. Only this
        // comparison notices.
        Assert.True(
            missing.Length == 0,
            $"The frozen inventory names {missing.Length} message(s) the boundary no longer authors: "
            + $"[{string.Join(", ", missing)}]. A message removed from a definition takes every field it "
            + "carried with it, so this is a wire-breaking change however small the diff looks.");

        Assert.True(
            unlisted.Length == 0,
            $"The boundary authors {unlisted.Length} message(s) the frozen inventory does not name: "
            + $"[{string.Join(", ", unlisted)}]. Adding a message to the inventory in the same change that "
            + "adds it to the definition is what keeps the published surface a reviewed one.");

        // 248 = 20 in common.v1 + 142 in dataservices.v1 + 88 in persistence.v1, less no map entries.
        // The count moved from 244 with the carrier-state typing: common.v1 gained DataWindowRow
        // (PROMOTED out of dataservices.v1, which therefore lost it) and NullableInt64, and
        // persistence.v1 gained CarrierBufferSegment and CarrierState - a net of three. It moved from
        // 247 to 248 with dataservices.v1.RowValidationError, the per-column refusal C-03's Update
        // answers for a payload rejected before any statement is generated. It reached 250 with
        // dataservices.v1.LoadRowsRequest and LoadRowsResponse and RETURNED to 248 when they were
        // withdrawn: that pair was an unreviewed row-loading mutator absent from AAP 0.4.3's frozen C-04
        // inventory, so it is no longer part of the published surface.
        Assert.Equal(248, authored.Length);
        Assert.Equal(authored.Length, AuthoredMessageRoster.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void TheOnlyMessagesWithoutAGeneratedTypeAreTheThreeSyntheticMapEntries()
    {
        string[] withoutGeneratedType =
            [.. ContractDescriptors.AllMessages()
                .Where(static message => message.ClrType is null)
                .Select(static message => message.FullName)
                .OrderBy(static name => name, StringComparer.Ordinal)];

        // THE LAYOUT AS FOUND, RECORDED SO THE EXCLUSION ABOVE CANNOT LOOK LIKE AN OVERSIGHT. A
        // `map<K,V>` field makes protoc synthesise a nested ...Entry message that is present in the
        // descriptor graph and absent from the generated code; there are exactly three, and every other
        // message in the boundary materialises. Naming them means adding or removing a map field fails
        // here first, where the cause is obvious.
        Assert.Equal(
            [
                "dataservices.v1.ExpressionBinding.BindTimeSnapshotEntry",
                "dataservices.v1.ExpressionBinding.LiveEnvironmentEntry",
                "dataservices.v1.GetExpressionStateResponse.ColumnsEntry",
            ],
            withoutGeneratedType);

        Assert.All(
            withoutGeneratedType.Select(ContractDescriptors.RequireMessage),
            static message => Assert.True(
                message.IsMapEntry,
                $"'{message.FullName}' has no generated type and is NOT a synthetic map entry, so it is a "
                + "genuine generation failure rather than an expected absence."));
    }

    [Theory]
    [MemberData(nameof(ServiceMethodKeys))]
    public void EveryMethodResolvesBothItsRequestAndItsResponseToAGeneratedMessageType(
        string serviceFullName,
        string methodName)
    {
        MethodDescriptor method = ContractDescriptors.RequireMethod(
            ContractDescriptors.RequireService(serviceFullName),
            methodName);

        // A METHOD WHOSE REQUEST OR RESPONSE TYPE FAILED TO GENERATE IS THE OTHER SYMPTOM OF A
        // MIS-SCOPED <Protobuf> ITEM: the service stub compiles against a type that is not there, or the
        // import that supplies it was never compiled at all. Both sides are checked because a request
        // and a response can come from different definitions - every method below carrying a shared
        // vocabulary type reaches across the common.v1 import to get it.
        AssertMaterialisedAsGeneratedMessage(
            method.InputType,
            $"The request of '{serviceFullName}.{methodName}'");

        AssertMaterialisedAsGeneratedMessage(
            method.OutputType,
            $"The response of '{serviceFullName}.{methodName}'");
    }

    [Theory]
    [MemberData(nameof(SharedVocabularyMessages))]
    public void EachSharedVocabularyMessageIsDeclaredInTheSharedDefinitionAndMaterialised(
        string messageFullName)
    {
        MessageDescriptor message = ContractDescriptors.RequireMessage(messageFullName);

        Assert.Equal(CommonFileName, message.File.Name);
        Assert.Equal(CommonNamespace, message.ClrType.Namespace);

        AssertMaterialisedAsGeneratedMessage(message, $"Shared vocabulary message '{messageFullName}'");
    }

    [Theory]
    [MemberData(nameof(SharedVocabularyEnums))]
    public void EachSharedVocabularyEnumIsDeclaredInTheSharedDefinitionAndMaterialised(string enumFullName)
    {
        EnumDescriptor enumeration = ContractDescriptors.RequireEnum(enumFullName);

        Assert.Equal(CommonFileName, enumeration.File.Name);
        Assert.NotEmpty(enumeration.Values);

        Assert.NotNull(enumeration.ClrType);
        Assert.True(
            enumeration.ClrType.IsEnum,
            $"'{enumFullName}' generated '{enumeration.ClrType.FullName}', which is not a CLR enum.");

        // A nested enum projects to a type nested inside its wrapper class, so its NAMESPACE is still the
        // definition's csharp_namespace - which is what keeps the three result-code alphabets reachable
        // from one using directive.
        Assert.Equal(CommonNamespace, enumeration.ClrType.Namespace);
    }

    [Fact]
    public void TheResultCodeFamiliesAreOneWrapperMessagePerAlphabetWhichIsTheLayoutAsFound()
    {
        // RECORDED BECAUSE IT IS NOT THE LAYOUT ONE WOULD GUESS. Only the buffer and item-status
        // alphabets are declared at file scope. The three result-code families are each WRAPPED in a
        // message that carries a single nested enum named Value, and the wrapping is load-bearing:
        // protobuf places enum VALUE names in the scope enclosing the enum, so three file-level enums
        // would have to keep their legacy SCREAMING_SNAKE spellings distinct across the whole package.
        // Nesting each alphabet inside its own message scopes its value names to that message, which is
        // what lets RetCode.Value keep OK/SUCCESS/ALLOW aliased to zero (allow_alias, common.v1.proto
        // :L330) alongside the XML_* and SQLITE_* families untouched.
        Assert.Equal(
            ["DwBuffer", "ItemStatus"],
            ContractDescriptors.Common.EnumTypes.Select(static enumeration => enumeration.Name));

        string[] wrappedAlphabets =
            [.. ContractDescriptors.AllEnums()
                .Where(static enumeration =>
                    enumeration.ContainingType is not null
                    && string.Equals(enumeration.File.Name, CommonFileName, StringComparison.Ordinal))
                .Select(static enumeration => enumeration.FullName)
                .OrderBy(static name => name, StringComparer.Ordinal)];

        Assert.Equal(
            [
                "common.v1.RetCode.Value",
                "common.v1.RichError.Key",
                "common.v1.SqliteResultCode.Value",
                "common.v1.XmlParseStatus.Value",
            ],
            wrappedAlphabets);
    }
}
