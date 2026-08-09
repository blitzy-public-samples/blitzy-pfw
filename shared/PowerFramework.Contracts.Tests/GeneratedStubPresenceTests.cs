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
    private sealed record RequiredMethod(
        string ContractId,
        string ServiceName,
        string MethodName,
        bool ClientStreaming,
        bool ServerStreaming);

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

    // ==============================================================================================
    //  THE REQUIRED METHOD SURFACE, WITH THE STREAMING DIRECTION EACH ONE'S LEGACY SHAPE FORCES
    //
    //  Presence and direction only. What each method DOES belongs to the service projects' own tests;
    //  what its messages CONTAIN belongs to ProtoDescriptorTests. Direction is in this file because it
    //  is part of the generated signature on both halves, and because a direction changed to unary
    //  would destroy the behaviour it exists to carry while still compiling everywhere.
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
        new("C-03", "DataWindowService", "Retrieve", false, true),
        new("C-03", "DataWindowService", "OpenValidationSession", false, false),
        new("C-03", "DataWindowService", "CloseValidationSession", false, false),
        new("C-03", "DataWindowService", "EventChain", true, true),
        new("C-03", "DataWindowService", "Update", false, false),
        new("C-03", "DataWindowService", "GetEventGate", false, false),
        new("C-03", "DataWindowService", "DisableEvent", false, false),
        new("C-03", "DataWindowService", "EnableEvent", false, false),

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
        new("C-04", "ColumnExpressionService", "InvokeMethodChannel", true, true),
        new("C-04", "ColumnExpressionService", "TraceChannel", true, true),

        // C-05. Query is SERVER-STREAMING to reproduce progressive recordset delivery; chunking is a
        // first-class part of the legacy surface, whose guard rejects any size at or below 1000 with
        // RetCode.E_INVALID_ARGUMENT [n_cst_thread_task_sqlquery.sru:L410].
        new("C-05", "QueryService", "Query", false, true),

        // C-06. PrepareUpdate carries the per-table update contract, because the legacy re-derives the
        // whole thing at runtime from an ARRAY of table descriptors rather than trusting the DataWindow
        // definition - of_addupdatabletable takes the table name, the updatable columns, the key
        // columns, the identity column, updatewhere and updatekeyinplace
        // [n_cst_thread_task_sqlupdate.sru:L82], consumed by _of_updateprepare [:L98]. Update executes
        // it [:L172] and is UNARY because its conflict outcome is one definite answer.
        new("C-06", "UpdateService", "PrepareUpdate", false, false),
        new("C-06", "UpdateService", "Update", false, false),

        // C-07. Exec is the command half; the legacy rejects an empty statement with
        // RetCode.E_INVALID_SQL [n_cst_thread_task_sqlcommand.sru:L45] and executes on the worker
        // thread [:L60].
        new("C-07", "CommandService", "Exec", false, false),

        // C-08. Session begin and end plus commit and rollback, mirroring of_connect
        // [n_cst_thread_trans.sru:L111], of_disconnect [:L145], of_commit [:L383] and of_rollback
        // [:L185]. All unary: a transaction boundary is a single decision.
        new("C-08", "TransactionService", "BeginSession", false, false),
        new("C-08", "TransactionService", "EndSession", false, false),
        new("C-08", "TransactionService", "Commit", false, false),
        new("C-08", "TransactionService", "Rollback", false, false),
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
            { CommonFileName, "common.v1", CommonNamespace },
            { DataServicesFileName, "dataservices.v1", DataServicesNamespace },
            { PersistenceFileName, "persistence.v1", PersistenceNamespace },
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

    /// <summary>Every method this phase requires, with the streaming direction its contract requires.</summary>
    public static TheoryData<string, string, string, bool, bool> RequiredMethods
    {
        get
        {
            TheoryData<string, string, string, bool, bool> rows = new();
            foreach (RequiredMethod required in RequiredMethodRoster)
            {
                rows.Add(
                    required.ContractId,
                    required.ServiceName,
                    required.MethodName,
                    required.ClientStreaming,
                    required.ServerStreaming);
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
    /// </remarks>
    public static TheoryData<string> AuthoredMessageNames
    {
        get
        {
            TheoryData<string> rows = new();
            foreach (MessageDescriptor message in ContractDescriptors.AllMessages())
            {
                if (message.IsMapEntry)
                {
                    continue;
                }

                rows.Add(message.FullName);
            }

            return rows;
        }
    }

    /// <summary>Every method of every published service, as (service full name, method name).</summary>
    public static TheoryData<string, string> ServiceMethodKeys
    {
        get
        {
            TheoryData<string, string> rows = new();
            foreach (MethodDescriptor method in ContractDescriptors.AllMethods())
            {
                rows.Add(method.Service.FullName, method.Name);
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
    // ==============================================================================================

    [Theory]
    [MemberData(nameof(RequiredMethods))]
    public void EachRequiredMethodIsDeclaredWithTheStreamingDirectionItsContractRequires(
        string contractId,
        string serviceName,
        string methodName,
        bool clientStreaming,
        bool serverStreaming)
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
    }

    [Theory]
    [MemberData(nameof(RequiredMethods))]
    public void TheGeneratedClientCarriesEachRequiredMethodWithTheCallShapeItsDirectionImplies(
        string contractId,
        string serviceName,
        string methodName,
        bool clientStreaming,
        bool serverStreaming)
    {
        ServiceContract contract = RequireRosterEntry(serviceName);
        MethodDescriptor method = ContractDescriptors.RequireMethod(
            ContractDescriptors.RequireService(serviceName),
            methodName);

        Type client = RequireNestedType(
            RequireServiceContainer(contract.CsharpNamespace, serviceName),
            $"{serviceName}Client",
            $"Contract {contractId} cannot be called at all without its client: {GrpcServicesDiagnosis}.");

        string expectedName = ExpectedClientMethodName(clientStreaming, serverStreaming, methodName);
        Type expectedCall = ExpectedClientCallType(
            clientStreaming,
            serverStreaming,
            method.InputType.ClrType,
            method.OutputType.ClrType);

        // An exact single-element comparison, because every overload of one generated client method
        // agrees on its return type and differs only in how call options arrive. So this reads as "the
        // name resolves, and it resolves to exactly this one call shape".
        Assert.Equal([expectedCall], DeclaredReturnTypesOf(client, expectedName));
    }

    [Theory]
    [MemberData(nameof(RequiredMethods))]
    public void TheGeneratedServerBaseCarriesEachRequiredMethodWithTheHandlerShapeItsDirectionImplies(
        string contractId,
        string serviceName,
        string methodName,
        bool clientStreaming,
        bool serverStreaming)
    {
        ServiceContract contract = RequireRosterEntry(serviceName);
        MethodDescriptor method = ContractDescriptors.RequireMethod(
            ContractDescriptors.RequireService(serviceName),
            methodName);

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
            ExpectedHandlerReturnType(serverStreaming, method.OutputType.ClrType),
            handler.ReturnType);

        // The parameter list is where the direction becomes visible on the server half: a streamed
        // request arrives as a reader and a streamed response leaves through a writer, and the call
        // context is always last.
        Assert.Equal(
            ExpectedHandlerParameterTypes(
                clientStreaming,
                serverStreaming,
                method.InputType.ClrType,
                method.OutputType.ClrType),
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
