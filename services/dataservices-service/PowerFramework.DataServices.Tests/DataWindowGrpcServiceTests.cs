// =====================================================================================================
//  DataWindowGrpcServiceTests - CONTRACT C-03 AS IT ACTUALLY CROSSES THE WIRE
//  ---------------------------------------------------------------------------------------------------
//  SUBJECT   PowerFramework.DataServices.Grpc.DataWindowService, driven THROUGH A REAL GrpcChannel over
//            the TestServer handler DataServicesTestHostFactory provides - so every assertion below
//            passes through HTTP/2 framing, real protobuf serialization, the real authorization
//            middleware and the real DI composition, exactly as a deployed caller would.
//
//  ORACLE    ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru          the 22-event surface,
//                                                                             the gate, the topics,
//                                                                             the four session fields
//            ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc.sru        the service base
//            ws_objects/pfw.tests.pbl.src/dw_sqlite.srd                        the six marked columns
//            ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru the update protocol
//            ws_objects/pfw.utility.invoker.pbl.src/n_cst_eventful.sru         the veto algebra
//            ws_objects/pfw.tests.pbl.src/w_test_eventful.srw                  the broker fixture
//            ws_objects/pfw.shared.pbl.src/retcode.sru                         the return algebra
//            ALL READ-ONLY (C-C). Every behavioural assertion carries its `ws_objects/**` locator,
//            because nothing else in the repository can adjudicate it.
//
//  ============ WHY THIS SUITE IS NOT DataWindowServiceContractTests A SECOND TIME =================
//  THE TWO SUITES DRIVE THE SERVICE THROUGH DIFFERENT DOORS, AND ONLY ONE OF THEM IS THE WIRE.
//
//  DataWindowServiceContractTests CONSTRUCTS the service and calls its methods with a hand-rolled
//  `C03CallContext : ServerCallContext`. It opens no channel and uses no host - which is right for what
//  it owns (the descriptor, the projections, the ordering discipline, the four alphabets as types), but
//  it means the ASP.NET Core pipeline is not in the picture at all. A method could be mapped anonymous,
//  a message could fail to round-trip through protobuf, an enum could be renumbered on the wire, and
//  every one of its 119 rows would still pass.
//
//  THIS suite closes exactly that gap, and asserts only things that require the pipeline to be real:
//    1. EVERY published operation is refused without a credential and admitted with one. The mapping
//       carries `.RequireAuthorization(CallerAuthorization.DataWindowPolicyName)` (Program.cs), and a
//       policy is middleware - it does not exist when the service is called as an object. Driven as a
//       theory whose ROWS COME FROM THE SERVICE DESCRIPTOR, so an rpc added tomorrow fails this file
//       until it is covered rather than shipping silently unauthenticated (C-G).
//    2. `Retrieve` really is a server stream, with chunk boundaries a caller can observe, and each row
//       really carries BOTH value sets after a protobuf round-trip (AAP 0.6.3.2).
//    3. The sequencing token is ON THE GENERATED WIRE MESSAGE, and an out-of-order arrival inside a
//       synchronous group fails the call rather than being buffered, reordered or replayed (G5).
//    4. The topic triple arrives as THREE FIELDS with the fused legacy form confined to `legacy_name`.
//    5. The conflict arrives as `Aborted` with a decodable `ConflictDetail` in the trailer the
//       DESCRIPTOR declares, after exactly ONE upstream attempt (AAP 0.8.1).
//    6. The gate coupling is observable ACROSS the boundary: with EID_ITEMCHANGE disabled the chain
//       produces no column-expression evaluation and no trace payload.
//  EventOrderingPatternTests owns the per-capability-area ASSIGNMENT of the two ordering patterns;
//  this file owns the wire-level ENFORCEMENT of whichever pattern is in force. EventGateTests owns the
//  in-process gate semantics; this file owns their observable consequence over the channel.
//
//  ======================== NO NETWORK, NO DOCKER, NO DATABASE (C-E, C-I, R4) ======================
//  The channel is built over the TestServer's in-memory handler, so nothing binds a port. Persistence
//  is reached only through the substituted `Clients/PersistenceClient.cs`, which is a public class with
//  virtual members precisely so a double can stand in for it. No storage provider is reachable at all,
//  and that is asserted structurally rather than assumed - see NoStorageProviderIsReachable. Docker was
//  unavailable when this refactor was planned (risk R4) and nothing here needs it.
//
//  ====================== THE FOUR ALPHABETS THAT SHARE THE NUMERALS 1 AND 2 =======================
//  Each is its own type on the wire and this suite keeps them apart, because the numerals collide:
//    Veto.Types.Result       Continue=0, PreventOnce=1, PreventDeep=2   [n_cst_eventful.sru:L111-L112]
//    ItemChangeResult        the {0,1,2,3} alphabet                     [se_cst_dw.sru:L182-L253]
//    RetCode                 PREVENT=1, CANCELLED=-2                    [retcode.sru:L42,L44-L45]
//    EventGate.Types.Bit     1, 2, 4 - a BITMASK, not an ordinal        [se_cst_dw.sru:L41-L43]
//  The item-change alphabet is never mapped onto the return algebra: `case 3` rewrites its result to 1
//  [se_cst_dw.sru:L223-L225] and the `case else` arm forcibly returns 2 [:L250], and neither digit
//  means there what it means in RetCode.
//
//  ================================ NAMING, AND WHY IT LOOKS ODD ===================================
//  SCREAMING_SNAKE identifiers are REFERENCED here and DECLARED nowhere. Where a wire spelling itself
//  is the subject, this file reads it off the protobuf descriptor's `EnumValueDescriptor.Name` rather
//  than retyping the literal - so the assertion cannot drift from the contract, and no analyzer
//  suppression is needed for a constant this file does not own.
//
//  BINDING CONSTRAINTS AT THIS SITE
//  C-A SELF-AUDIT: every outcome is asserted against the GENERATED `dataservices.v1` and `common.v1`
//      types. Where a domain constant is compared, it is compared TO its wire twin to prove the two
//      agree - which is the only assertion holding an agreement the compiler cannot see - never used
//      as the sole subject.
//  C-B SELF-AUDIT: no behaviour is asserted that the oracle does not exhibit. The legacy quirks
//      reproduced rather than corrected are called out where they are asserted: the forced 2 from the
//      default item-change arm, the gate's suppression of column-expression calculation, and the
//      disable/enable return-type asymmetry.
//  DETERMINISM: nothing here reads a wall clock, opens a socket or touches storage, so a recording
//      taken from this suite is reproducible - the Golden-Master technique's one hard prerequisite
//      (AAP 0.6.7).
// =====================================================================================================

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf.Reflection;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PowerFramework.Contracts.Common.V1;
using PowerFramework.Contracts.DataServices.V1;
using PowerFramework.DataServices.Domain;
using PowerFramework.DataServices.Grpc;
using Xunit;

// ---- Alias block. Every entry resolves a REAL collision between the two vocabularies or between the
// ---- contract and its Persistence namesake; none is decoration, and the bare form of an aliased name
// ---- is never used below. The spelling matches Grpc/DataWindowService.cs so a reader moving between
// ---- the implementation and its wire tests never has to work out which side of the boundary a name is
// ---- on.
using DataWindowContractClient =
    PowerFramework.Contracts.DataServices.V1.DataWindowService.DataWindowServiceClient;
using DomainEventGate = PowerFramework.DataServices.Domain.EventGate;
using DomainItemChangeResult = PowerFramework.DataServices.Domain.ItemChangeResult;
using DomainVetoResult = PowerFramework.Shared.Eventful.VetoResult;
using GeneratedDataWindowService = PowerFramework.Contracts.DataServices.V1.DataWindowService;
using GeneratedDataWindowServiceBase =
    PowerFramework.Contracts.DataServices.V1.DataWindowService.DataWindowServiceBase;
using PersistenceClient = PowerFramework.DataServices.Clients.PersistenceClient;

// The PORTED constant catalogue, which is what every outcome code in this file is spelled from. Its wire
// twin is `common.v1.RetCode`, a wrapper message whose nested enum is aliased below as WireRetCode. The
// two agree value for value by construction, but they are different types and the bare name must resolve
// to exactly one of them - so it is bound here to the same side Grpc/DataWindowService.cs binds it to.
using RetCode = PowerFramework.Shared.Kernel.RetCode;
using ServedDataWindowService = PowerFramework.DataServices.Grpc.DataWindowService;
using WireEventGate = PowerFramework.Contracts.DataServices.V1.EventGate;
using WireItemChangeResult = PowerFramework.Contracts.DataServices.V1.ItemChangeResult;
using WireRetCode = PowerFramework.Contracts.Common.V1.RetCode.Types.Value;
using WireUpdateRequest = PowerFramework.Contracts.DataServices.V1.UpdateRequest;

namespace PowerFramework.DataServices.Tests;

/// <summary>
/// The published C-03 operation set, and the one property that can only be observed through the real
/// pipeline: that every one of those operations is authorized.
/// </summary>
/// <param name="host">The in-process host, shared across this class.</param>
/// <remarks>
/// <para>
/// SIXTEEN OPERATIONS, AND THE COUNT IS THE CONTRACT'S TO STATE. The service declares sixteen rpcs -
/// the eight core operations of the DataWindow boundary plus the eight read/apply operations that make
/// AAP 0.3.5's headless halves reachable as an API rather than four compiling declarations. This suite
/// asserts BOTH: that the eight core ones are present and overridden by name, and that the descriptor's
/// full method list is covered with nothing left over. Asserting only eight would let a ninth core
/// operation appear unnoticed; asserting only sixteen would lose the evidence that the eight the
/// boundary is specified in terms of are the eight that exist.
/// </para>
/// </remarks>
public sealed class DataWindowGrpcSurfaceTests(DataServicesTestHostFactory host)
    : IClassFixture<DataServicesTestHostFactory>
{
    /// <summary>
    /// The eight CORE operations of contract C-03, in the order the boundary is specified in.
    /// </summary>
    /// <remarks>
    /// Spelled out rather than derived, deliberately: this list is the independent statement of what the
    /// DataWindow boundary is FOR, and deriving it from the descriptor would make it agree with the
    /// contract by construction and therefore assert nothing.
    /// </remarks>
    private static readonly ImmutableArray<string> CoreOperations =
    [
        nameof(GeneratedDataWindowServiceBase.Retrieve),
        nameof(GeneratedDataWindowServiceBase.OpenValidationSession),
        nameof(GeneratedDataWindowServiceBase.CloseValidationSession),
        nameof(GeneratedDataWindowServiceBase.EventChain),
        nameof(GeneratedDataWindowServiceBase.Update),
        nameof(GeneratedDataWindowServiceBase.GetEventGate),
        nameof(GeneratedDataWindowServiceBase.DisableEvent),
        nameof(GeneratedDataWindowServiceBase.EnableEvent),
    ];

    /// <summary>Every rpc name the service descriptor publishes, as theory rows.</summary>
    /// <remarks>
    /// THE ROWS COME FROM THE CONTRACT, NOT FROM A LIST IN THIS FILE. That is the whole point of the
    /// authorization theory: an rpc added to the proto tomorrow produces a new row here immediately, and
    /// that row fails until the operation is genuinely covered. A hand-maintained list would have let it
    /// ship unauthenticated in silence, which is CWE-862 behind a boundary that looks authenticated.
    /// </remarks>
    public static TheoryData<string> PublishedOperations()
    {
        TheoryData<string> rows = [];

        foreach (MethodDescriptor method in GeneratedDataWindowService.Descriptor.Methods)
        {
            rows.Add(method.Name);
        }

        return rows;
    }

    /// <summary>The eight core operations, as theory rows.</summary>
    public static TheoryData<string> CoreOperationRows()
    {
        TheoryData<string> rows = [];

        foreach (string name in CoreOperations)
        {
            rows.Add(name);
        }

        return rows;
    }

    [Fact]
    public void TheServedImplementationDerivesFromTheGeneratedContractBase() =>
        Assert.True(
            typeof(GeneratedDataWindowServiceBase).IsAssignableFrom(typeof(ServedDataWindowService)),
            "The C-03 server must derive from the generated contract base so the contract - not the "
                + "implementation - decides the wire shape (C-A). Deriving from anything else would let "
                + "the two drift with nothing to notice.");

    [Theory]
    [MemberData(nameof(CoreOperationRows))]
    public void EachCoreOperationIsOverriddenByTheServedImplementation(string operation)
    {
        System.Reflection.MethodInfo? declared = typeof(ServedDataWindowService).GetMethod(
            operation,
            System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.DeclaredOnly);

        // DECLARED HERE rather than inherited. An inherited method would answer Unimplemented from the
        // generated base, and would still be found by name WITHOUT the DeclaredOnly flag - so the flag is
        // the assertion that the operation is implemented rather than merely reachable.
        Assert.NotNull(declared);

        // AND IT OVERRIDES THE CONTRACT'S OWN VIRTUAL rather than shadowing it with a `new` member of the
        // same name. `GetBaseDefinition` walks to the root of the override chain, so it naming the
        // generated base is what proves the dispatch a real caller gets actually lands here. A `new`
        // member would compile, pass a name check, and never be called by the gRPC binder.
        Assert.True(declared.IsVirtual);
        Assert.Equal(
            typeof(GeneratedDataWindowServiceBase),
            declared.GetBaseDefinition().DeclaringType);
    }

    [Fact]
    public void TheDescriptorPublishesTheEightCoreOperationsAndTheEightHeadlessOnes()
    {
        ImmutableArray<string> published =
            [.. GeneratedDataWindowService.Descriptor.Methods.Select(static method => method.Name)];

        // Ordered comparison, never a set: the proto's declaration order is the order the boundary is
        // documented in, and a reordering is a review-visible change to the contract.
        Assert.Equal(CoreOperations, [.. published.Take(CoreOperations.Length)]);

        Assert.Equal(
            CoreOperations.Length + 8,
            published.Length);

        // Every published method is overridden - the count above says how many there are, this says none
        // of them is left answering Unimplemented from the generated base.
        foreach (string operation in published)
        {
            Assert.NotNull(typeof(ServedDataWindowService).GetMethod(
                operation,
                System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.DeclaredOnly));
        }
    }

    [Fact]
    public void TheContractIsCompiledOnceAndOwnedByTheContractsAssembly()
    {
        // The generated request/response types, the service descriptor and the common vocabulary must
        // all come from PowerFramework.Contracts. If either service project declared a protocol
        // definition item of its own, these types would be generated a SECOND time into a second
        // assembly and the published boundary would stop being the single coupling C-A permits.
        string contracts = typeof(RetrieveRequest).Assembly.GetName().Name!;

        Assert.Equal(contracts, typeof(EventChainRequest).Assembly.GetName().Name);
        Assert.Equal(contracts, typeof(WireUpdateRequest).Assembly.GetName().Name);
        Assert.Equal(contracts, typeof(DwBuffer).Assembly.GetName().Name);
        Assert.Equal(contracts, typeof(ConflictDetail).Assembly.GetName().Name);
        Assert.Equal(contracts, typeof(GeneratedDataWindowService).Assembly.GetName().Name);

        // ONE DESCRIPTOR FOR ONE .proto FILE. A second generation would produce a second file descriptor
        // with the same declared name, and the two would be distinct types carrying identical wire formats -
        // the failure mode that makes a "cannot convert X to X" diagnostic possible.
        Assert.Equal("dataservices.v1.proto", GeneratedDataWindowService.Descriptor.File.Name);
        Assert.Same(
            GeneratedDataWindowService.Descriptor.File,
            RetrieveRequest.Descriptor.File);

        // And the serving implementation is NOT in that assembly: the contract is the boundary
        // definition, not a shared-code back door.
        Assert.NotEqual(contracts, typeof(ServedDataWindowService).Assembly.GetName().Name);
    }

    [Fact]
    public void NoStorageProviderIsReachableFromTheServiceUnderTest()
    {
        // C-E, asserted structurally rather than by inspection. Persistence is the only service in the
        // system that holds a storage provider; DataServices reaches it over gRPC. If an EF Core
        // provider, the ADO-level SQLite provider or the native SQLite bundle ever appeared in this
        // assembly's reference closure, a test could quietly start touching a database and the
        // no-fabricated-database constraint would be broken without a single assertion changing.
        ImmutableArray<string> forbidden =
        [
            "Microsoft.Data.Sqlite",
            "Microsoft.EntityFrameworkCore",
            "SQLitePCLRaw",
        ];

        IEnumerable<string> referenced = typeof(ServedDataWindowService).Assembly
            .GetReferencedAssemblies()
            .Select(static reference => reference.Name ?? string.Empty);

        foreach (string reference in referenced)
        {
            foreach (string prefix in forbidden)
            {
                Assert.False(
                    reference.StartsWith(prefix, StringComparison.OrdinalIgnoreCase),
                    $"DataServices references '{reference}', which is a storage provider. Persistence is "
                        + "the only service permitted to hold one (C-E).");
            }
        }
    }

    [Theory]
    [MemberData(nameof(PublishedOperations))]
    public async Task EveryPublishedOperationRefusesAnUnauthenticatedCall(string operation)
    {
        DataWindowContractClient client = new(host.CreateAnonymousGrpcChannel());

        RpcException refused = await Assert.ThrowsAsync<RpcException>(
            () => DataWindowWireCalls.InvokeAsync(
                operation,
                client,
                TestContext.Current.CancellationToken));

        // UNAUTHENTICATED, NOT PERMISSION-DENIED. The distinction is published and load-bearing: 401
        // tells a caller to present a credential, 403 tells it the credential it has is insufficient.
        // Collapsing them would tell a caller to fix the wrong thing. ReceiverAuthorizationTests owns
        // the policy's scope and subject halves; this row owns the per-operation COVERAGE of it.
        Assert.Equal(StatusCode.Unauthenticated, refused.StatusCode);
    }

    [Theory]
    [MemberData(nameof(PublishedOperations))]
    public async Task EveryPublishedOperationAdmitsThePermittedPrincipal(string operation)
    {
        host.PersistenceEdge.Reset();
        host.PersistenceEdge.ScriptEmptyQuery();

        DataWindowContractClient client = new(host.CreateAuthenticatedGrpcChannel());

        // ASSERTED ALONGSIDE THE REFUSALS ON PURPOSE. A suite that only proved refusals would leave open
        // the possibility that everything is refused, which passes every row above and serves no caller.
        // The call is allowed to fail on its own merits - a bare request names no session and no handle,
        // so most operations answer a defined argument or precondition fault - but it must never fail
        // for want of a credential.
        StatusCode? refusal = null;

        try
        {
            await DataWindowWireCalls.InvokeAsync(
                operation,
                client,
                TestContext.Current.CancellationToken);
        }
        catch (RpcException admitted)
        {
            refusal = admitted.StatusCode;
        }

        Assert.NotEqual(StatusCode.Unauthenticated, refusal);
        Assert.NotEqual(StatusCode.PermissionDenied, refusal);
    }
}

/// <summary>
/// One real gRPC call per published C-03 operation, keyed by the descriptor's own rpc name.
/// </summary>
/// <remarks>
/// <para>
/// WHY A TABLE RATHER THAN SIXTEEN NEAR-IDENTICAL TESTS. The authorization property is the same sentence
/// for every operation - "refused without a credential, admitted with one" - so the interesting content
/// is the COVERAGE, and coverage is what a table can prove and sixteen hand-written tests cannot. The
/// table is looked up BY THE DESCRIPTOR'S NAME, so an rpc with no entry throws a diagnostic naming
/// itself rather than being skipped.
/// </para>
/// <para>
/// THE REQUESTS ARE DELIBERATELY BARE. Authorization runs before the request body is examined, so a
/// request that names no session and no handle exercises the policy exactly as a fully populated one
/// would, and carries no arrangement that could make a refusal look like something else. Both streaming
/// operations are driven to their FIRST READ, because that is where a stream's status surfaces - a
/// server-streaming call that was refused does not throw until the stream is read, and asserting on the
/// call object alone would pass whatever the policy did.
/// </para>
/// </remarks>
internal static class DataWindowWireCalls
{
    /// <summary>
    /// Every published operation, mapped to a call that drives it far enough for its status to surface.
    /// </summary>
    private static readonly ImmutableDictionary<
        string,
        Func<DataWindowContractClient, CancellationToken, Task>> Calls =
            ImmutableDictionary.CreateRange(
                StringComparer.Ordinal,
                [
                    Entry(nameof(GeneratedDataWindowServiceBase.Retrieve), DrainRetrieveAsync),
                    Entry(
                        nameof(GeneratedDataWindowServiceBase.OpenValidationSession),
                        static (client, token) => client
                            .OpenValidationSessionAsync(
                                new OpenValidationSessionRequest(),
                                cancellationToken: token)
                            .ResponseAsync),
                    Entry(
                        nameof(GeneratedDataWindowServiceBase.CloseValidationSession),
                        static (client, token) => client
                            .CloseValidationSessionAsync(
                                new CloseValidationSessionRequest(),
                                cancellationToken: token)
                            .ResponseAsync),
                    Entry(nameof(GeneratedDataWindowServiceBase.EventChain), DrainEventChainAsync),
                    Entry(
                        nameof(GeneratedDataWindowServiceBase.Update),
                        static (client, token) => client
                            .UpdateAsync(new WireUpdateRequest(), cancellationToken: token)
                            .ResponseAsync),
                    Entry(
                        nameof(GeneratedDataWindowServiceBase.GetEventGate),
                        static (client, token) => client
                            .GetEventGateAsync(new GetEventGateRequest(), cancellationToken: token)
                            .ResponseAsync),
                    Entry(
                        nameof(GeneratedDataWindowServiceBase.DisableEvent),
                        static (client, token) => client
                            .DisableEventAsync(new DisableEventRequest(), cancellationToken: token)
                            .ResponseAsync),
                    Entry(
                        nameof(GeneratedDataWindowServiceBase.EnableEvent),
                        static (client, token) => client
                            .EnableEventAsync(new EnableEventRequest(), cancellationToken: token)
                            .ResponseAsync),
                    Entry(
                        nameof(GeneratedDataWindowServiceBase.GetDropDownSearchState),
                        static (client, token) => client
                            .GetDropDownSearchStateAsync(
                                new GetDropDownSearchStateRequest(),
                                cancellationToken: token)
                            .ResponseAsync),
                    Entry(
                        nameof(GeneratedDataWindowServiceBase.ApplyDropDownSearch),
                        static (client, token) => client
                            .ApplyDropDownSearchAsync(
                                new ApplyDropDownSearchRequest(),
                                cancellationToken: token)
                            .ResponseAsync),
                    Entry(
                        nameof(GeneratedDataWindowServiceBase.GetColumnSortState),
                        static (client, token) => client
                            .GetColumnSortStateAsync(
                                new GetColumnSortStateRequest(),
                                cancellationToken: token)
                            .ResponseAsync),
                    Entry(
                        nameof(GeneratedDataWindowServiceBase.ApplyColumnSort),
                        static (client, token) => client
                            .ApplyColumnSortAsync(new ApplyColumnSortRequest(), cancellationToken: token)
                            .ResponseAsync),
                    Entry(
                        nameof(GeneratedDataWindowServiceBase.GetContextMenuModel),
                        static (client, token) => client
                            .GetContextMenuModelAsync(
                                new GetContextMenuModelRequest(),
                                cancellationToken: token)
                            .ResponseAsync),
                    Entry(
                        nameof(GeneratedDataWindowServiceBase.ApplyContextMenuModel),
                        static (client, token) => client
                            .ApplyContextMenuModelAsync(
                                new ApplyContextMenuModelRequest(),
                                cancellationToken: token)
                            .ResponseAsync),
                    Entry(
                        nameof(GeneratedDataWindowServiceBase.GetRowSelectState),
                        static (client, token) => client
                            .GetRowSelectStateAsync(
                                new GetRowSelectStateRequest(),
                                cancellationToken: token)
                            .ResponseAsync),
                    Entry(
                        nameof(GeneratedDataWindowServiceBase.ApplyRowSelectStyle),
                        static (client, token) => client
                            .ApplyRowSelectStyleAsync(
                                new ApplyRowSelectStyleRequest(),
                                cancellationToken: token)
                            .ResponseAsync),
                ]);

    /// <summary>Drives the named operation over the supplied channel.</summary>
    /// <param name="operation">The descriptor's rpc name.</param>
    /// <param name="client">The generated client, authenticated or not.</param>
    /// <param name="cancellationToken">The test's cancellation token.</param>
    /// <returns>A task completing when the operation's status has surfaced.</returns>
    /// <exception cref="InvalidOperationException">
    /// The contract publishes an operation this table does not cover.
    /// </exception>
    internal static Task InvokeAsync(
        string operation,
        DataWindowContractClient client,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(client);

        return Calls.TryGetValue(operation, out Func<DataWindowContractClient, CancellationToken, Task>? call)
            ? call(client, cancellationToken)
            : throw new InvalidOperationException(
                $"Contract C-03 publishes rpc '{operation}', which this suite does not drive. A newly "
                    + "added operation must be covered here before it ships, because the authorization "
                    + "theory is what proves no operation is silently anonymous - so this throws rather "
                    + "than skipping the row.");
    }

    private static KeyValuePair<string, Func<DataWindowContractClient, CancellationToken, Task>> Entry(
        string operation,
        Func<DataWindowContractClient, CancellationToken, Task> call) => new(operation, call);

    private static async Task DrainRetrieveAsync(
        DataWindowContractClient client,
        CancellationToken cancellationToken)
    {
        using AsyncServerStreamingCall<RetrieveChunk> call =
            client.Retrieve(new RetrieveRequest(), cancellationToken: cancellationToken);

        while (await call.ResponseStream.MoveNext(cancellationToken).ConfigureAwait(false))
        {
            // Drained rather than inspected: this path exists to let the CALL's status surface, and the
            // status of a server stream is only known once the stream ends.
        }
    }

    private static async Task DrainEventChainAsync(
        DataWindowContractClient client,
        CancellationToken cancellationToken)
    {
        using AsyncDuplexStreamingCall<EventChainRequest, EventChainResponse> call =
            client.EventChain(cancellationToken: cancellationToken);

        await call.RequestStream.WriteAsync(new EventChainRequest(), cancellationToken)
            .ConfigureAwait(false);
        await call.RequestStream.CompleteAsync().ConfigureAwait(false);

        while (await call.ResponseStream.MoveNext(cancellationToken).ConfigureAwait(false))
        {
            // As above. A refused bidirectional call does not fault on the write, only on the read.
        }
    }
}

/// <summary>
/// The six columns of the golden-master fixture, and the rows built from them.
/// </summary>
/// <remarks>
/// <para>
/// THE FIXTURE IS NOT INVENTED - IT IS TRANSCRIBED. <c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd</c> is
/// the ONLY updatable DataWindow in the entire legacy repository, and it is therefore the golden-master
/// fixture for the whole retrieval / validation / update triple. Its table specification reads
/// <c>update="COMPANY" updatewhere=1 updatekeyinplace=no</c> [<c>:L14</c>] and ALL SIX of its columns
/// carry <c>update=yes updatewhereclause=yes</c> [<c>:L8-L14</c>], with <c>id</c> additionally
/// <c>key=yes identity=yes</c> [<c>:L8</c>].
/// </para>
/// <para>
/// WHY THAT MAKES BOTH VALUE SETS MANDATORY ON THE WIRE. <c>updatewhere=1</c> is the "key and updateable
/// columns" concurrency mode: the generated where-clause carries the key column PLUS THE ORIGINAL VALUES
/// OF EVERY UPDATEABLE COLUMN. With all six marked, the optimistic-concurrency check spans all six
/// original values, so a payload carrying only current values cannot express the check at all - which is
/// exactly why AAP 0.6.3.2 records that a naive rowset is insufficient. The rows below therefore always
/// carry a matched pair, and the assertions prove the pair survives the round trip DISTINGUISHABLY.
/// </para>
/// </remarks>
internal static class DwSqliteWireRows
{
    /// <summary>
    /// The six columns with a current and an original value apiece, in the fixture's declared order.
    /// </summary>
    /// <remarks>
    /// EVERY VALUE IS VALID FOR ITS COLUMN'S DECLARED TYPE, AND THAT IS A REQUIREMENT RATHER THAN A
    /// COURTESY. The update path validates each value against the column type transcribed from
    /// <c>dw_sqlite.srd</c> - <c>number</c>, <c>char(100)</c>, <c>number</c>, <c>char(200)</c>,
    /// <c>decimal(2)</c>, <c>date</c> [<c>:L8-L13</c>] - and answers <c>E_INVALID_DATA</c> for one that does
    /// not parse. A fixture carrying unparseable placeholders would fail every conflict case for a reason
    /// that has nothing to do with concurrency, and the temptation would then be to weaken the assertion
    /// rather than fix the fixture.
    ///
    /// AND EVERY PAIR DIFFERS. If current and original agreed, an implementation that dropped the original
    /// set and echoed the current one twice would pass - which is exactly the failure AAP 0.6.3.2 warns
    /// about. The <c>id</c> pair differs too, which is legitimate rather than perverse: the fixture declares
    /// <c>updatekeyinplace=no</c> [<c>:L14</c>], so a key change is a real, exercised path performed as
    /// delete-plus-insert.
    /// </remarks>
    private static readonly ImmutableArray<(string Name, string Current, string Original)> Columns =
    [
        ("id", "11", "10"),
        ("name", "current-name", "original-name"),
        ("age", "31", "30"),
        ("address", "current-address", "original-address"),
        ("salary", "20000.00", "10000.00"),
        ("birth", "1999-05-08", "1998-04-07"),
    ];

    /// <summary>The six column names, in the fixture's declared order.</summary>
    internal static readonly ImmutableArray<string> ColumnNames =
        [.. Columns.Select(static column => column.Name)];

    /// <summary>Builds one row carrying a distinguishable current and original value per column.</summary>
    /// <param name="row">The one-based row number, as the oracle numbers rows.</param>
    /// <param name="buffer">The buffer the row belongs to.</param>
    /// <param name="itemStatus">The row's item status.</param>
    /// <returns>The row.</returns>
    internal static DataWindowRow Build(
        long row,
        DwBuffer buffer = DwBuffer.Primary,
        ItemStatus itemStatus = ItemStatus.DataModified)
    {
        DataWindowRow built = new()
        {
            Buffer = buffer,
            Row = row,
            ItemStatus = itemStatus,
        };

        for (int index = 0; index < ColumnNames.Length; index++)
        {
            string name = ColumnNames[index];

            // ONE-BASED COLUMN IDS. PowerBuilder columns are numbered from one and `dw_sqlite.srd`
            // declares `id=1` through `id=6` [:L21-L26]. AAP 0.4.5.4 names one-based translation the
            // single most dangerous mechanical hazard in this refactor, so the fixture is built the
            // oracle's way rather than the language's.
            built.Columns.Add(new ColumnValue
            {
                ColumnName = name,
                ColumnId = index + 1,
                Value = new AnyValue { StringValue = CurrentValueOf(name) },
                ItemStatus = itemStatus,
            });

            built.OriginalValues.Add(new ColumnValue
            {
                ColumnName = name,
                ColumnId = index + 1,
                Value = new AnyValue { StringValue = OriginalValueOf(name) },
                ItemStatus = itemStatus,
            });
        }

        return built;
    }

    /// <summary>The current value this fixture gives a named column.</summary>
    /// <param name="columnName">The column.</param>
    /// <returns>The value.</returns>
    internal static string CurrentValueOf(string columnName) => Find(columnName).Current;

    /// <summary>The original value this fixture gives a named column.</summary>
    /// <param name="columnName">The column.</param>
    /// <returns>The value.</returns>
    internal static string OriginalValueOf(string columnName) => Find(columnName).Original;

    private static (string Name, string Current, string Original) Find(string columnName)
    {
        ArgumentNullException.ThrowIfNull(columnName);

        foreach ((string Name, string Current, string Original) column in Columns)
        {
            if (string.Equals(column.Name, columnName, StringComparison.Ordinal))
            {
                return column;
            }
        }

        throw new ArgumentOutOfRangeException(
            nameof(columnName),
            columnName,
            "dw_sqlite.srd declares exactly six columns [ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L8-L13] "
                + "and this fixture transcribes all six; a name outside that set is a fixture error rather "
                + "than a behaviour under test.");
    }
}

/// <summary>
/// <c>Retrieve</c> as a server stream: chunk boundaries, the buffer and status vocabulary, both value
/// sets per row, the upstream it is sourced from, and cancellation.
/// </summary>
/// <param name="host">The in-process host, shared across this class.</param>
public sealed class DataWindowGrpcRetrieveTests(DataServicesTestHostFactory host)
    : IClassFixture<DataServicesTestHostFactory>
{
    /// <summary>The three legacy buffers, as theory rows.</summary>
    public static TheoryData<DwBuffer> Buffers() =>
        [DwBuffer.Primary, DwBuffer.Delete, DwBuffer.Filter];

    /// <summary>The four legacy item statuses, as theory rows.</summary>
    public static TheoryData<ItemStatus> ItemStatuses() =>
    [
        ItemStatus.NotModified,
        ItemStatus.DataModified,
        ItemStatus.New,
        ItemStatus.NewModified,
    ];

    [Fact]
    public async Task RetrieveIsAServerStreamWhoseChunkBoundariesTheCallerCanObserve()
    {
        host.PersistenceEdge.Reset();
        host.PersistenceEdge.ScriptQuery(rowCount: 3L, chunkCount: 3);

        ImmutableArray<RetrieveChunk> chunks = await DrainAsync(
            new RetrieveRequest { DatawindowHandle = DataWindowCatalogue.SqliteFixtureName });

        // A SERVER STREAM, NOT A SINGLE RESPONSE. Three upstream chunks arrive as three messages, which
        // is the property that matters: the legacy delivered rows progressively through
        // `ondatareceived(ref n_cst_thread_task_sqlbase_ds data, long rowcount)`
        // [n_cst_thread_task_sqlquery.sru], and collapsing the stream into one response would lose the
        // progressive delivery the contract exists to reproduce.
        Assert.Equal(3, chunks.Length);

        // THE BOUNDARIES ARE OBSERVABLE, and ordered - an index the caller can count on, and exactly one
        // final marker, at the end. Compared as ordered lists, never as sets.
        Assert.Equal([1L, 2L, 3L], [.. chunks.Select(static chunk => chunk.ChunkIndex)]);
        Assert.Equal([false, false, true], [.. chunks.Select(static chunk => chunk.Final)]);
    }

    [Fact]
    public async Task RetrieveSourcesItsChunksThroughThePersistenceClientAndTouchesNoDatabase()
    {
        host.PersistenceEdge.Reset();
        host.PersistenceEdge.ScriptQuery(rowCount: 2L, chunkCount: 1);

        _ = await DrainAsync(new RetrieveRequest
        {
            DatawindowHandle = DataWindowCatalogue.SqliteFixtureName,
        });

        // THE SUBSTITUTED EDGE RECORDED THE CALL. This is what proves the rows came from Persistence
        // rather than from anywhere else - and, with the edge substituted, that no storage was reached
        // (C-E). The handle the caller named is the data object the upstream task was created for, so the
        // request was relayed rather than reinvented.
        Assert.Equal(1, host.PersistenceEdge.QueryCalls);
        Assert.Equal(
            DataWindowCatalogue.SqliteFixtureName,
            host.PersistenceEdge.LastCreateQueryTaskRequest?.Spec?.DataObject);

        // AND EVERY UPSTREAM HANDLE WAS GIVEN BACK. A retrieval borrows a Persistence session and a query
        // task; leaking either would hold a pooled transaction open for a caller that has gone.
        Assert.True(
            host.PersistenceEdge.NothingIsStillHeld,
            "The retrieval left server-held upstream handles behind.");
    }

    [Fact]
    public async Task EveryRowCarriesBothTheCurrentAndTheOriginalValueOfAllSixMarkedColumns()
    {
        host.PersistenceEdge.Reset();
        host.PersistenceEdge.ScriptQueryMessages(
        [
            ScriptedPersistenceResponses.RowCountMessage(1L),
            PersistenceWireChunks.CarryingRows(1L, 1L, DwSqliteWireRows.Build(1L)),
            ScriptedPersistenceResponses.StatusMessage(WireRetCode.Ok),
        ]);

        ImmutableArray<RetrieveChunk> chunks = await DrainAsync(new RetrieveRequest
        {
            DatawindowHandle = DataWindowCatalogue.SqliteFixtureName,
        });

        DataWindowRow row = Assert.Single(chunks.SelectMany(static chunk => chunk.Rows));

        // SIX AND SIX, AFTER A REAL PROTOBUF ROUND TRIP. `dw_sqlite.srd` marks all six columns
        // `updatewhereclause=yes` under `updatewhere=1`, so the concurrency check spans all six original
        // values and both sets must survive [dw_sqlite.srd:L8-L14].
        Assert.Equal(DwSqliteWireRows.ColumnNames.Length, row.Columns.Count);
        Assert.Equal(DwSqliteWireRows.ColumnNames.Length, row.OriginalValues.Count);

        // Ordered comparison of the column names in both sets: the pair is positional as well as present.
        Assert.Equal(
            DwSqliteWireRows.ColumnNames,
            [.. row.Columns.Select(static column => column.ColumnName)]);
        Assert.Equal(
            DwSqliteWireRows.ColumnNames,
            [.. row.OriginalValues.Select(static column => column.ColumnName)]);

        // AND THE TWO ARE DISTINGUISHABLE, COLUMN BY COLUMN. Presence alone would be satisfied by an
        // implementation that echoed the current values into both fields.
        foreach (string columnName in DwSqliteWireRows.ColumnNames)
        {
            ColumnValue current = row.Columns.Single(
                column => string.Equals(column.ColumnName, columnName, StringComparison.Ordinal));
            ColumnValue original = row.OriginalValues.Single(
                column => string.Equals(column.ColumnName, columnName, StringComparison.Ordinal));

            Assert.Equal(DwSqliteWireRows.CurrentValueOf(columnName), current.Value.StringValue);
            Assert.Equal(DwSqliteWireRows.OriginalValueOf(columnName), original.Value.StringValue);
            Assert.NotEqual(original.Value.StringValue, current.Value.StringValue);

            // The one-based column id travels with each value, on both sides of the pair.
            Assert.Equal(current.ColumnId, original.ColumnId);
            Assert.InRange(current.ColumnId, 1L, DwSqliteWireRows.ColumnNames.Length);
        }
    }

    [Theory]
    [MemberData(nameof(Buffers))]
    public async Task TheThreeBufferModelCrossesTheWire(DwBuffer buffer)
    {
        host.PersistenceEdge.Reset();
        host.PersistenceEdge.ScriptQueryMessages(
        [
            ScriptedPersistenceResponses.RowCountMessage(1L),
            PersistenceWireChunks.CarryingRows(
                1L,
                1L,
                DwSqliteWireRows.Build(1L, buffer)),
            ScriptedPersistenceResponses.StatusMessage(WireRetCode.Ok),
        ]);

        ImmutableArray<RetrieveChunk> chunks = await DrainAsync(new RetrieveRequest
        {
            DatawindowHandle = DataWindowCatalogue.SqliteFixtureName,
            Buffers = { buffer },
        });

        DataWindowRow row = Assert.Single(chunks.SelectMany(static chunk => chunk.Rows));

        // Primary!, Delete! and Filter! all reach the caller under their own identity. The Filter buffer
        // matters most: its ROW ORDER IS INVERTED relative to the source, which
        // `n_cst_thread_task_sqlupdate.sru:L235` documents and `:L237` relies on by iterating it
        // BACKWARDS. A boundary that could not name the buffer could not preserve that.
        Assert.Equal(buffer, row.Buffer);
    }

    [Theory]
    [MemberData(nameof(ItemStatuses))]
    public async Task TheItemStatusDomainCrossesTheWire(ItemStatus itemStatus)
    {
        host.PersistenceEdge.Reset();
        host.PersistenceEdge.ScriptQueryMessages(
        [
            ScriptedPersistenceResponses.RowCountMessage(1L),
            PersistenceWireChunks.CarryingRows(
                1L,
                1L,
                DwSqliteWireRows.Build(1L, DwBuffer.Primary, itemStatus)),
            ScriptedPersistenceResponses.StatusMessage(WireRetCode.Ok),
        ]);

        ImmutableArray<RetrieveChunk> chunks = await DrainAsync(new RetrieveRequest
        {
            DatawindowHandle = DataWindowCatalogue.SqliteFixtureName,
        });

        DataWindowRow row = Assert.Single(chunks.SelectMany(static chunk => chunk.Rows));

        // The PowerBuilder `dwItemStatus` domain, whole. DataModified! and NewModified! are the two the
        // update protocol discriminates on, and losing the distinction would make an inserted-and-edited
        // row indistinguishable from an edited existing one - which decides whether the generated
        // statement is an INSERT or an UPDATE.
        Assert.Equal(itemStatus, row.ItemStatus);
    }

    [Fact]
    public async Task CancellationMidStreamIsHonouredAndLeavesNothingHeldUpstream()
    {
        host.PersistenceEdge.Reset();
        host.PersistenceEdge.ScriptQuery(rowCount: 6L, chunkCount: 6);

        DataWindowContractClient client = new(host.CreateAuthenticatedGrpcChannel());

        using CancellationTokenSource caller = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);

        using AsyncServerStreamingCall<RetrieveChunk> call = client.Retrieve(
            new RetrieveRequest { DatawindowHandle = DataWindowCatalogue.SqliteFixtureName },
            cancellationToken: caller.Token);

        // Read one chunk, then go away - the shape of a caller that abandoned a large retrieval.
        Assert.True(await call.ResponseStream.MoveNext(caller.Token));

        await caller.CancelAsync();

        RpcException cancelled = await Assert.ThrowsAsync<RpcException>(
            async () =>
            {
                while (await call.ResponseStream.MoveNext(caller.Token))
                {
                    // Drained until the cancellation surfaces.
                }
            });

        // THE CALLER'S TOKEN IS THE AUTHORITY ON WHEN TO STOP. Honouring it is what keeps a cancelled
        // retrieval from continuing to pull rows from Persistence for a caller that has gone.
        Assert.Equal(StatusCode.Cancelled, cancelled.StatusCode);

        // AND THE ABANDONED CALL STILL GAVE ITS UPSTREAM HANDLES BACK. A cancellation is the path most
        // likely to leak them, because it is the one path that does not run to the end of the method
        // body; the release must therefore be unconditional. Awaited rather than polled because the
        // server's own unwind races the client's observation of the status - the assertion is that the
        // release happens, not that it has already happened at the instant the client noticed.
        await WaitForUpstreamReleaseAsync();

        Assert.True(
            host.PersistenceEdge.NothingIsStillHeld,
            "A cancelled retrieval leaked upstream handles: sessions "
                + $"[{string.Join(", ", host.PersistenceEdge.HeldSessionIds)}], query tasks "
                + $"[{string.Join(", ", host.PersistenceEdge.HeldQueryTaskIds)}].");
    }

    /// <summary>Yields until the upstream edge holds nothing.</summary>
    /// <returns>A task that completes once every upstream handle has been given back.</returns>
    /// <remarks>
    /// <para>
    /// TOKEN-DRIVEN AND UNBOUNDED, BECAUSE A BOUNDED RETRY IS WORSE THAN MERELY SLOW. Retrying up to fifty
    /// times at twenty milliseconds and then RETURNING NORMALLY means an expired bound does not report itself
    /// at all - it hands a still-leaking edge to the caller's assertion, which then fails as though the
    /// release had never been attempted. The truth in that case is "a one-second budget elapsed on a loaded
    /// agent", and the two are indistinguishable in the failure message.
    /// </para>
    /// <para>
    /// There is therefore no attempt count and no delay: the loop yields until the edge is clear, and only the
    /// test's own cancellation token can end it early. A release that genuinely never happens - the leak
    /// this case exists to catch - is ended by the runner's timeout, the separate liveness bound that
    /// belongs outside the assertion, and the caller's assertion still names exactly which handles were
    /// held. Nothing here asserts a duration (AAP 0.8.5).
    /// </para>
    /// </remarks>
    private async Task WaitForUpstreamReleaseAsync()
    {
        while (!host.PersistenceEdge.NothingIsStillHeld)
        {
            TestContext.Current.CancellationToken.ThrowIfCancellationRequested();

            // Hands the scheduler the continuation carrying the server's unwind. No duration, so nothing
            // here can expire; a release that never happens is ended by the runner, not by this loop.
            await Task.Yield();
        }
    }

    private async Task<ImmutableArray<RetrieveChunk>> DrainAsync(RetrieveRequest request)
    {
        DataWindowContractClient client = new(host.CreateAuthenticatedGrpcChannel());

        using AsyncServerStreamingCall<RetrieveChunk> call = client.Retrieve(
            request,
            cancellationToken: TestContext.Current.CancellationToken);

        List<RetrieveChunk> chunks = [];

        while (await call.ResponseStream.MoveNext(TestContext.Current.CancellationToken))
        {
            chunks.Add(call.ResponseStream.Current);
        }

        return [.. chunks];
    }
}

/// <summary>
/// Upstream query messages carrying real rows, for the cases whose subject is the row payload.
/// </summary>
/// <remarks>
/// <c>ScriptedPersistenceResponses.DataChunkMessage</c> builds buffer segments with NO rows in them,
/// which is right for the cases whose subject is the chunk framing and wrong for the cases whose subject
/// is what a row carries. This adds the rows without duplicating the framing.
/// </remarks>
internal static class PersistenceWireChunks
{
    /// <summary>Builds one upstream data chunk carrying the supplied rows.</summary>
    /// <param name="chunkIndex">The one-based chunk index.</param>
    /// <param name="chunkCount">How many chunks the upstream will send in total.</param>
    /// <param name="rows">The rows, which may span buffers.</param>
    /// <returns>The upstream message.</returns>
    internal static PowerFramework.Contracts.Persistence.V1.QueryResponse CarryingRows(
        long chunkIndex,
        long chunkCount,
        params DataWindowRow[] rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        PowerFramework.Contracts.Persistence.V1.CarrierState state = new();

        // ONE SEGMENT PER BUFFER PRESENT, in the buffers' own enum order, because the carrier's segments
        // are positional: the codec validates segment n against buffer n
        // [Buffers/ChangesetCodec.cs - SerializedBuffers, TryValidateSegments].
        foreach (DwBuffer buffer in rows.Select(static row => row.Buffer).Distinct().Order())
        {
            PowerFramework.Contracts.Persistence.V1.CarrierBufferSegment segment = new()
            {
                Buffer = buffer,
            };

            segment.Rows.AddRange(rows.Where(row => row.Buffer == buffer));

            state.Segments.Add(segment);
        }

        return new PowerFramework.Contracts.Persistence.V1.QueryResponse
        {
            DataChunk = new PowerFramework.Contracts.Persistence.V1.QueryDataChunk
            {
                State = state,
                ChunkIndex = chunkIndex,
                ChunkCount = chunkCount,
                FullState = chunkIndex >= chunkCount,
            },
        };
    }
}

/// <summary>
/// One <c>EventChain</c> conversation, driven the way a real client must drive it.
/// </summary>
/// <remarks>
/// <para>
/// THE SEQUENCE COUNTER IS SHARED BETWEEN THE TWO DIRECTIONS, and that is a property of the contract
/// rather than of this helper: the server assigns its OWN outbound tokens from the same counter the client
/// draws from, so the next admissible client token is one past the highest the stream has carried IN
/// EITHER DIRECTION. A driver that counted only its own writes would send a token the server had already
/// issued and be refused for a reason that has nothing to do with the case under test - so the highest
/// seen is tracked across both directions here, once, rather than being rediscovered per test.
/// </para>
/// <para>
/// IT ANSWERS THE INVERTED QUESTIONS. Nine of the 22 events are SEMANTIC - the framework asking the
/// application, which now lives across the boundary [<c>se_cst_dw.sru:L11-L14</c>, <c>:L24-L26</c>,
/// <c>:L28</c>, <c>:L32</c>] - so the server sends an <c>invoke</c> and BLOCKS until the client answers.
/// A driver that read responses without answering would deadlock on every semantic event and time out,
/// which is why the read loop answers first and collects second.
/// </para>
/// </remarks>
internal sealed class EventChainDriver : IAsyncDisposable
{
    private readonly AsyncDuplexStreamingCall<EventChainRequest, EventChainResponse> _call;
    private readonly List<EventChainResponse> _received = [];
    private readonly List<EventNotification> _answered = [];
    private long _highestSequenceSeen;
    private bool _completed;

    private EventChainDriver(
        AsyncDuplexStreamingCall<EventChainRequest, EventChainResponse> call,
        string sessionId)
    {
        _call = call;
        SessionId = sessionId;
    }

    /// <summary>The validation session this conversation is correlated to.</summary>
    internal string SessionId { get; }

    /// <summary>Every response the server sent, in arrival order.</summary>
    internal ImmutableArray<EventChainResponse> Received => [.. _received];

    /// <summary>Every inverted question the server asked, in the order it asked them.</summary>
    internal ImmutableArray<EventNotification> Answered => [.. _answered];

    /// <summary>Opens a conversation over an authenticated channel.</summary>
    /// <param name="client">The generated client.</param>
    /// <param name="sessionId">The validation session id, or any value when the case is about refusal.</param>
    /// <param name="cancellationToken">The test's cancellation token.</param>
    /// <returns>The driver.</returns>
    internal static EventChainDriver Open(
        DataWindowContractClient client,
        string sessionId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(sessionId);

        return new EventChainDriver(
            client.EventChain(cancellationToken: cancellationToken),
            sessionId);
    }

    /// <summary>The next token this client may legitimately send.</summary>
    /// <returns>One past the highest sequence the stream has carried in either direction.</returns>
    internal long NextSequence() => _highestSequenceSeen + 1L;

    /// <summary>Sends a notification with an explicit token.</summary>
    /// <param name="notify">The event.</param>
    /// <param name="discipline">The ordering discipline claimed for it.</param>
    /// <param name="sequence">The token to send, which a refusal case may deliberately make stale.</param>
    /// <param name="cancellationToken">The test's cancellation token.</param>
    /// <returns>A task completing when the message has been written.</returns>
    internal async Task NotifyAsync(
        EventNotification notify,
        OrderingDiscipline discipline,
        long sequence,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notify);

        _highestSequenceSeen = Math.Max(_highestSequenceSeen, sequence);

        await _call.RequestStream.WriteAsync(
                new EventChainRequest
                {
                    SessionId = SessionId,
                    Token = new SequencingToken { Sequence = sequence, Discipline = discipline },
                    Notify = notify,
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Sends a notification with the next admissible token.</summary>
    /// <param name="notify">The event.</param>
    /// <param name="discipline">The ordering discipline claimed for it.</param>
    /// <param name="cancellationToken">The test's cancellation token.</param>
    /// <returns>A task completing when the message has been written.</returns>
    internal Task NotifyAsync(
        EventNotification notify,
        OrderingDiscipline discipline,
        CancellationToken cancellationToken) =>
        NotifyAsync(notify, discipline, NextSequence(), cancellationToken);

    /// <summary>
    /// Reads until the server reports an outcome for <paramref name="eventId"/>, answering every inverted
    /// question raised on the way.
    /// </summary>
    /// <param name="eventId">The event whose outcome is awaited.</param>
    /// <param name="cancellationToken">The test's cancellation token.</param>
    /// <param name="answer">
    /// The numeric each answered question is answered with, defaulting to continue.
    /// </param>
    /// <param name="answerVeto">The veto each answered question carries, defaulting to continue.</param>
    /// <returns>The outcome.</returns>
    /// <exception cref="InvalidOperationException">The stream ended without reporting the outcome.</exception>
    internal async Task<EventResult> ReadOutcomeAsync(
        EventId eventId,
        CancellationToken cancellationToken,
        long answer = 0L,
        Veto.Types.Result answerVeto = Veto.Types.Result.Continue)
    {
        while (await _call.ResponseStream.MoveNext(cancellationToken).ConfigureAwait(false))
        {
            EventChainResponse response = _call.ResponseStream.Current;

            _received.Add(response);

            if (response.Token is not null)
            {
                _highestSequenceSeen = Math.Max(_highestSequenceSeen, response.Token.Sequence);
            }

            switch (response.PayloadCase)
            {
                case EventChainResponse.PayloadOneofCase.Invoke:
                    _answered.Add(response.Invoke);

                    await AnswerAsync(response, answer, answerVeto, cancellationToken)
                        .ConfigureAwait(false);

                    continue;

                case EventChainResponse.PayloadOneofCase.Result when response.Result.EventId == eventId:
                    return response.Result;

                default:
                    continue;
            }
        }

        throw new InvalidOperationException(
            string.Format(
                CultureInfo.InvariantCulture,
                "The event chain ended without reporting an outcome for {0}. Responses seen: {1}.",
                eventId,
                _received.Count));
    }

    /// <summary>Reads any remaining responses, so a terminal status surfaces.</summary>
    /// <param name="cancellationToken">The test's cancellation token.</param>
    /// <returns>A task completing when the response stream ends.</returns>
    internal async Task DrainAsync(CancellationToken cancellationToken)
    {
        while (await _call.ResponseStream.MoveNext(cancellationToken).ConfigureAwait(false))
        {
            _received.Add(_call.ResponseStream.Current);
        }
    }

    /// <summary>Half-closes the request stream.</summary>
    /// <returns>A task completing when the half-close has been sent.</returns>
    internal async Task CompleteAsync()
    {
        if (_completed)
        {
            return;
        }

        _completed = true;

        await _call.RequestStream.CompleteAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        // Best effort, because a case that asserted a terminal status has already seen the stream fault
        // and half-closing it again would throw for a reason the test has no interest in. The call itself
        // is always disposed, which is what releases the server-side conversation.
        try
        {
            await CompleteAsync().ConfigureAwait(false);
        }
        catch (RpcException)
        {
            // The stream was already terminated by the status the test asserted.
        }
        catch (InvalidOperationException)
        {
            // The stream was already completed.
        }

        _call.Dispose();
    }

    private async Task AnswerAsync(
        EventChainResponse question,
        long answer,
        Veto.Types.Result veto,
        CancellationToken cancellationToken)
    {
        long sequence = NextSequence();

        _highestSequenceSeen = Math.Max(_highestSequenceSeen, sequence);

        await _call.RequestStream.WriteAsync(
                new EventChainRequest
                {
                    SessionId = SessionId,
                    Token = new SequencingToken
                    {
                        Sequence = sequence,
                        Discipline = question.Token?.Discipline ?? OrderingDiscipline.Synchronous,
                    },
                    Result = new EventResult
                    {
                        CorrelationId = question.Invoke.CorrelationId,
                        EventId = question.Invoke.EventId,
                        ReturnValue = answer,
                        SemanticVeto = veto,
                    },
                },
                cancellationToken)
            .ConfigureAwait(false);
    }
}

/// <summary>
/// One populated notification per event id, so the 22-event surface can be driven as a theory.
/// </summary>
/// <remarks>
/// EVERY ARM IS SPELLED OUT, AND THAT IS THE POINT. The oracle declares 22 events in one type
/// [<c>se_cst_dw.sru:L11-L32</c>] - nine semantic and thirteen raw <c>pbm_dwn*</c> - and each carries its
/// own argument list. A helper that sent a bare notification for all of them would prove only that the
/// enum has 22 members; populating each body proves the ARGUMENTS have somewhere to live on the wire,
/// which is the half that a naive serialization loses.
/// </remarks>
internal static class DataWindowEventNotifications
{
    /// <summary>The column this fixture raises every column-scoped event against.</summary>
    internal const string Column = "age";

    /// <summary>The one-based id of <see cref="Column"/> in the golden-master fixture.</summary>
    internal const long ColumnId = 3L;

    /// <summary>Builds a populated notification for the named event.</summary>
    /// <param name="eventId">The event.</param>
    /// <param name="correlationId">The correlation the answer must echo.</param>
    /// <returns>The notification.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The contract publishes an event id this helper does not build.
    /// </exception>
    internal static EventNotification Build(EventId eventId, string correlationId)
    {
        ArgumentNullException.ThrowIfNull(correlationId);

        EventNotification notify = new()
        {
            CorrelationId = correlationId,
            EventId = eventId,
            DatawindowHandle = DataWindowCatalogue.SqliteFixtureName,
        };

        DwObjectRef Dwo() => new()
        {
            Name = Column,
            Id = ColumnId,
            ColType = "number",
            ColTypePrefix = DwObjectRef.Types.ColTypePrefix.Numbe,
        };

        switch (eventId)
        {
            // ---- The nine SEMANTIC events [se_cst_dw.sru:L11-L14, L24-L26, L28, L32] ----
            case EventId.Oninitcontextmenu:
                notify.InitContextMenu = new InitContextMenuEvent { Row = 1L, Dwo = Dwo() };
                break;

            case EventId.Oncontextmenu:
                notify.ContextMenu = new ContextMenuEvent { Row = 1L, Dwo = Dwo(), Mid = 1L };
                break;

            case EventId.Onddsgetfilter:
                // `ref string filter` - no return type at source [:L13]. The produced filter travels back
                // on EventResult.produced_filter, which is why that field is `optional`.
                notify.DdsGetFilter = new DdsGetFilterEvent { Row = 1L, Dwo = Dwo(), Data = "4" };
                break;

            case EventId.Oncolumnexpinvokemethod:
                // `any` return over a `string[]` argument [:L14].
                notify.ColumnExpInvokeMethod = new ColumnExpInvokeMethodEvent
                {
                    Row = 1L,
                    Dwo = Dwo(),
                    Name = "Invoke",
                    Args = { "1", "2" },
                };
                break;

            case EventId.Ondoitemchange:
                notify.DoItemChange = new DoItemChangeEvent { Row = 1L, Dwo = Dwo(), Data = "42" };
                break;

            case EventId.Onitemchanged:
                notify.ItemChanged = new ItemChangedEvent { Row = 1L, Dwo = Dwo() };
                break;

            case EventId.Ondoitemchanged:
                notify.DoItemChanged = new DoItemChangedEvent { Row = 1L, Dwo = Dwo() };
                break;

            case EventId.Onddsfiltered:
                notify.DdsFiltered = new DdsFilteredEvent
                {
                    Row = 1L,
                    Dwo = Dwo(),
                    RowCount = 3L,
                    FilteredCount = 1L,
                };
                break;

            case EventId.Oncolumnexptrace:
                notify.ColumnExpTrace = new ColumnExpTraceEvent
                {
                    Row = 1L,
                    Dwo = Dwo(),
                    Stack = "age",
                    Expr = "1 + 1",
                    Value = "2",
                };
                break;

            // ---- The thirteen RAW pbm_dwn* events [se_cst_dw.sru:L15-L23, L27, L29-L31] ----
            case EventId.Ondwnrbuttondown:
                notify.DwnRbuttonDown = new DwnRButtonDownEvent
                {
                    Xpos = 10,
                    Ypos = 20,
                    Row = 1L,
                    Dwo = Dwo(),
                };
                break;

            case EventId.Ondwnrbuttonup:
                notify.DwnRbuttonUp = new DwnRButtonUpEvent
                {
                    Xpos = 10,
                    Ypos = 20,
                    Row = 1L,
                    Dwo = Dwo(),
                };
                break;

            case EventId.Ondwnrowchange:
                notify.DwnRowChange = new DwnRowChangeEvent { CurrentRow = 1L };
                break;

            case EventId.Ondwnrowchanging:
                notify.DwnRowChanging = new DwnRowChangingEvent { CurrentRow = 1L, NewRow = 2L };
                break;

            case EventId.Ondwnlbuttondblclk:
                notify.DwnLbuttonDblClk = new DwnLButtonDblClkEvent
                {
                    Xpos = 10,
                    Ypos = 20,
                    Row = 1L,
                    Dwo = Dwo(),
                };
                break;

            case EventId.Ondwnlbuttonclk:
                notify.DwnLbuttonClk = new DwnLButtonClkEvent
                {
                    Xpos = 10,
                    Ypos = 20,
                    Row = 1L,
                    Dwo = Dwo(),
                };
                break;

            case EventId.Ondwnchanging:
                notify.DwnChanging = new DwnChangingEvent { Row = 1L, Dwo = Dwo(), Data = "42" };
                break;

            case EventId.Ondwnitemchangefocus:
                notify.DwnItemChangeFocus = new DwnItemChangeFocusEvent { Row = 1L, Dwo = Dwo() };
                break;

            case EventId.Ondwnitemchange:
                notify.DwnItemChange = new DwnItemChangeEvent
                {
                    Row = 1L,
                    Dwo = Dwo(),
                    Data = "42",
                    OriginalValue = new AnyValue { StringValue = "41" },
                    OriginalStatus = ItemStatus.NotModified,
                    CurrentValue = new AnyValue { StringValue = "41" },
                };
                break;

            case EventId.Ondwnitemvalidationerror:
                notify.DwnItemValidationError = new DwnItemValidationErrorEvent
                {
                    Row = 1L,
                    Dwo = Dwo(),
                    Data = "42",
                    ValidationMessage = string.Empty,
                    OriginalValue = new AnyValue { StringValue = "41" },
                    OriginalStatus = ItemStatus.NotModified,
                };
                break;

            case EventId.Ondwnkillfocus:
                notify.DwnKillFocus = new DwnKillFocusEvent { DeferredAcceptQueued = false };
                break;

            case EventId.Ondwnlbuttonup:
                notify.DwnLbuttonUp = new DwnLButtonUpEvent
                {
                    Xpos = 10,
                    Ypos = 20,
                    Row = 1L,
                    Dwo = Dwo(),
                };
                break;

            case EventId.Ondwnsetfocus:
                notify.DwnSetFocus = new DwnSetFocusEvent();
                break;

            case EventId.Unspecified:
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(eventId),
                    eventId,
                    "Contract C-03 publishes an event id this suite does not build a body for. The oracle "
                        + "declares exactly 22 events at se_cst_dw.sru:L11-L32; a 23rd must be given a "
                        + "populated body here before it ships, because an unpopulated notification would "
                        + "prove only that the enum grew.");
        }

        return notify;
    }
}

/// <summary>
/// The validation session as an explicit, correlated thing on the wire.
/// </summary>
/// <param name="host">The in-process host, shared across this class.</param>
/// <remarks>
/// <para>
/// WHY THE SESSION EXISTS AT ALL. <c>se_cst_dw.sru:L89-L96</c> declares FOUR PRIVATE FIELDS that the event
/// chain reads and writes BETWEEN events: the disabled-event bitmask <c>_nDisabledEvent</c> [<c>:L89</c>],
/// a flag recording that execution is inside the item-change handler <c>_bDoItemChange</c> [<c>:L92</c>],
/// a flag recording that it is inside the validation-error handler <c>_bDwnItemValidationError</c>
/// [<c>:L94</c>], and - most consequentially - the item-changed return value stashed FOR THE
/// VALIDATION-ERROR EVENT TO CONSUME, <c>_nItemChangeRetCode</c> [<c>:L96</c>]. A stateless request
/// boundary has nowhere to put any of them, so they become fields of a server-held session that dedicated
/// calls open and close.
/// </para>
/// <para>
/// AND WHY IT IS NEVER CREATED IMPLICITLY. If an unknown correlation id quietly opened a session, an
/// item-change event whose predecessor's stashed code the validation-error handler needs would run against
/// a session that never saw the predecessor - and would produce a plausible, wrong answer rather than an
/// error. Fail fast, never graceful degradation (AAP 0.6.7).
/// </para>
/// </remarks>
public sealed class DataWindowGrpcValidationSessionTests(DataServicesTestHostFactory host)
    : IClassFixture<DataServicesTestHostFactory>
{
    [Fact]
    public async Task OpeningASessionReturnsACorrelationIdAndMaterializesTheFourCrossEventFields()
    {
        DataWindowContractClient client = new(host.CreateAuthenticatedGrpcChannel());

        OpenValidationSessionResponse opened = await client.OpenValidationSessionAsync(
            new OpenValidationSessionRequest
            {
                DatawindowHandle = DataWindowCatalogue.SqliteFixtureName,
            },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(string.IsNullOrWhiteSpace(opened.SessionId));
        Assert.Equal(WireRetCode.Success, opened.RetCode);

        // THE FOUR FIELDS ARE MATERIALIZED, NOT IMPLIED. Each of the oracle's four private fields has a
        // named field of its own on `ValidationSessionState`, so a caller can read the state rather than
        // infer it from the chain's behaviour. `deferred_accept_pending` is the fifth: the posted
        // accept-text at [se_cst_dw.sru:L387-L393] relied on the Win32 message pump, which a headless
        // container does not have, so the queued continuation that replaces it is state too and is
        // declared rather than hidden.
        ValidationSessionState state = Assert.IsType<ValidationSessionState>(opened.State);

        Assert.Equal(0L, state.DisabledEventMask);
        Assert.False(state.InItemChange);
        Assert.False(state.InItemValidationError);
        Assert.Equal(WireItemChangeResult.Default, state.ItemChangeRetCode);
        Assert.False(state.DeferredAcceptPending);
    }

    [Fact]
    public async Task AnInitialDisabledMaskIsAcceptedAtOpenAndIsVisibleInTheReturnedState()
    {
        DataWindowContractClient client = new(host.CreateAuthenticatedGrpcChannel());

        // The composable bitmask, set at open. Composability is the oracle's own word for it - the
        // constants are declared "支持组合" (supports combination) at [se_cst_dw.sru:L40].
        long composed = DomainEventGate.EID_ROWFOCUSCHANGE | DomainEventGate.EID_ITEMCHANGE;

        OpenValidationSessionResponse opened = await client.OpenValidationSessionAsync(
            new OpenValidationSessionRequest
            {
                DatawindowHandle = DataWindowCatalogue.SqliteFixtureName,
                InitialDisabledEventMask = composed,
            },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(composed, opened.State.DisabledEventMask);
        Assert.Equal(5L, opened.State.DisabledEventMask);
    }

    [Fact]
    public async Task TwoSessionsAreIsolatedOverTheWire()
    {
        DataWindowContractClient client = new(host.CreateAuthenticatedGrpcChannel());

        string first = await OpenAsync(client);
        string second = await OpenAsync(client);

        Assert.NotEqual(first, second);

        // Disable a bit in the first only.
        _ = await client.DisableEventAsync(
            new DisableEventRequest
            {
                SessionId = first,
                EventMask = DomainEventGate.EID_ITEMCHANGE,
            },
            cancellationToken: TestContext.Current.CancellationToken);

        GetEventGateResponse firstGate = await client.GetEventGateAsync(
            new GetEventGateRequest { SessionId = first },
            cancellationToken: TestContext.Current.CancellationToken);
        GetEventGateResponse secondGate = await client.GetEventGateAsync(
            new GetEventGateRequest { SessionId = second },
            cancellationToken: TestContext.Current.CancellationToken);

        // THE STATE IS PER CORRELATION ID. Two callers editing two DataWindows share a service instance;
        // if the gate were process-wide, one caller disabling item-change would silently suppress the
        // other's column-expression calculation [se_cst_dw.sru:L43].
        Assert.Equal(DomainEventGate.EID_ITEMCHANGE, firstGate.Gate.Mask);
        Assert.Equal(0L, secondGate.Gate.Mask);
    }

    [Fact]
    public async Task AnUnknownCorrelationIdIsADefinedStatusRatherThanASilentlyCreatedSession()
    {
        DataWindowContractClient client = new(host.CreateAuthenticatedGrpcChannel());

        await using EventChainDriver driver = EventChainDriver.Open(
            client,
            "no-such-validation-session",
            TestContext.Current.CancellationToken);

        await driver.NotifyAsync(
            DataWindowEventNotifications.Build(EventId.Ondwnsetfocus, "unknown-session"),
            OrderingDiscipline.Sequenced,
            TestContext.Current.CancellationToken);
        await driver.CompleteAsync();

        RpcException refused = await Assert.ThrowsAsync<RpcException>(
            () => driver.DrainAsync(TestContext.Current.CancellationToken));

        // FAILED PRECONDITION, AND THE CHOICE IS DELIBERATE. The system is not in a state where the
        // operation can run, and re-sending the same message would not help - which is exactly the
        // distinction FailedPrecondition draws against Aborted. Aborted is already bound on this contract
        // to the optimistic-concurrency conflict and its canonical projection to HTTP 409, so reusing it
        // here would make a client that maps 409 to "retry or surface the row state" do so for a protocol
        // fault carrying no row state at all.
        Assert.Equal(StatusCode.FailedPrecondition, refused.StatusCode);

        // The detail names the registry's own answer, so a caller learns WHY rather than only THAT.
        Assert.Contains(
            RetCode.E_INVALID_HANDLE.ToString(CultureInfo.InvariantCulture),
            refused.Status.Detail,
            StringComparison.Ordinal);

        // NOTHING WAS CREATED. The refusal is not a partial success with a session left behind.
        Assert.Empty(driver.Received);
    }

    [Fact]
    public async Task ClosingASessionReleasesItAndReportsWhetherItWasOpen()
    {
        DataWindowContractClient client = new(host.CreateAuthenticatedGrpcChannel());

        string session = await OpenAsync(client);

        CloseValidationSessionResponse closed = await client.CloseValidationSessionAsync(
            new CloseValidationSessionRequest { SessionId = session },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(WireRetCode.Success, closed.RetCode);
        Assert.True(closed.WasOpen);

        // THE FINAL STATE IS HANDED BACK. Closing discards the four fields, so the response is the last
        // opportunity a caller has to read them - which matters for a caller that closes on a fault and
        // needs to know whether the chain was inside the item-change or validation-error handler when it
        // did.
        Assert.NotNull(closed.FinalState);

        // AND CLOSING IS IDEMPOTENT, reporting that there was nothing to close rather than failing. The
        // contract declares that; a caller unwinding twice on an error path must not turn its own cleanup
        // into a second fault.
        CloseValidationSessionResponse again = await client.CloseValidationSessionAsync(
            new CloseValidationSessionRequest { SessionId = session },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(again.WasOpen);
    }

    [Fact]
    public async Task AnEventChainOnAClosedSessionFails()
    {
        DataWindowContractClient client = new(host.CreateAuthenticatedGrpcChannel());

        string session = await OpenAsync(client);

        _ = await client.CloseValidationSessionAsync(
            new CloseValidationSessionRequest { SessionId = session },
            cancellationToken: TestContext.Current.CancellationToken);

        await using EventChainDriver driver = EventChainDriver.Open(
            client,
            session,
            TestContext.Current.CancellationToken);

        await driver.NotifyAsync(
            DataWindowEventNotifications.Build(EventId.Ondwnsetfocus, "after-close"),
            OrderingDiscipline.Sequenced,
            TestContext.Current.CancellationToken);
        await driver.CompleteAsync();

        RpcException refused = await Assert.ThrowsAsync<RpcException>(
            () => driver.DrainAsync(TestContext.Current.CancellationToken));

        // A CLOSED ID IS AS UNKNOWN AS ONE THAT NEVER EXISTED. If a closed session's id kept working, the
        // close would not be a release and the four fields would outlive the conversation that owns them.
        Assert.Equal(StatusCode.FailedPrecondition, refused.StatusCode);
    }

    private static async Task<string> OpenAsync(DataWindowContractClient client) =>
        (await client.OpenValidationSessionAsync(
            new OpenValidationSessionRequest
            {
                DatawindowHandle = DataWindowCatalogue.SqliteFixtureName,
            },
            cancellationToken: TestContext.Current.CancellationToken)).SessionId;
}

/// <summary>
/// A host whose event chains are the fixture's own, so every one of the 22 event arms is reachable and the
/// column-expression participant is observable.
/// </summary>
/// <remarks>
/// <para>
/// WHY THE DEPLOYED CHAIN CANNOT ANSWER THESE CASES. The composition root binds
/// <c>HeadlessAttachedServiceFactory</c>, whose <c>CreateColumnExp</c> returns a DISABLED
/// column-expression service - correct for the deployed service, since C-04 owns the engine and C-03 must
/// not reach into it, but it makes the EID_ITEMCHANGE coupling permanently invisible through the deployed
/// wiring. <c>DataServicesTestHostFactory</c> documents the remedy and deliberately leaves it to a caller:
/// <c>IDataWindowEventChainFactory</c> is not substituted by the fixture, and "a chain double lives in
/// another test file and is that file's to register through <c>AdditionalServiceConfiguration</c>". This is
/// that file, and this is that registration.
/// </para>
/// <para>
/// THE CHAIN DOUBLE IS REUSED, NOT REBUILT. <c>FakeEventChain</c> and <c>FakeAttachedServiceFactory</c>
/// already exist in this assembly and already carry the ~50 host members
/// <c>DataWindowServiceHost</c> declares abstract. Writing a second chain here would duplicate every one
/// of them for no gain and would let the two drift.
/// </para>
/// <para>
/// ITS OBJECT MODEL IS DECLARED, BECAUSE AN UNDECLARED NAME IS A DEFINED FAULT. <c>FakeEventChain</c>
/// starts with an empty object model, and the service answers <c>InvalidArgument</c> for a
/// <c>dwo</c> name the model does not carry - the oracle's own warning at <c>n_cst_dwsvc.sru:L121</c>. The
/// six columns of <c>dw_sqlite.srd</c> are therefore declared on it, with the marks the fixture actually
/// carries: <c>update=yes updatewhereclause=yes</c> on all six [<c>:L8-L14</c>].
/// </para>
/// </remarks>
public sealed class WireEventChainHostFixture : IAsyncLifetime
{
    /// <summary>The chain factory the host serves every conversation from.</summary>
    /// <remarks>
    /// INTERNAL because the chain contract it implements is internal to the service, and a public member
    /// cannot expose a less accessible type. The fixture itself must be public for xunit to construct it.
    /// </remarks>
    internal WireEventChainFactory Chains { get; } = new();

    /// <summary>The in-process host.</summary>
    public DataServicesTestHostFactory Host { get; } = new();

    /// <inheritdoc />
    public ValueTask InitializeAsync()
    {
        Host.AdditionalServiceConfiguration.Add(services =>
        {
            services.RemoveAll<IDataWindowEventChainFactory>();
            services.AddSingleton<IDataWindowEventChainFactory>(Chains);
        });

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync() => await Host.DisposeAsync();
}

/// <summary>
/// Serves every conversation a <see cref="FakeEventChain"/> over the six columns of the golden-master
/// fixture, and remembers the last one so a test can read what the chain's participants were asked.
/// </summary>
internal sealed class WireEventChainFactory : IDataWindowEventChainFactory
{
    /// <summary>The most recently created chain, or <see langword="null"/> before the first.</summary>
    internal FakeEventChain? Last { get; private set; }

    /// <inheritdoc />
    public DataWindowEventChain? Create(
        ValidationSession session,
        string dataWindowHandle,
        IDataWindowEventObserver observer,
        IDataWindowSemanticResponder responder)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(dataWindowHandle);
        ArgumentNullException.ThrowIfNull(observer);
        ArgumentNullException.ThrowIfNull(responder);

        // THE UNBOUND NAME STILL ANSWERS ITS PUBLISHED NEGATIVE. A factory that bound every name would
        // make the refusal path untestable, and that refusal is published contract.
        if (string.Equals(
                dataWindowHandle,
                DataServicesTestHostFactory.UnboundDataWindowName,
                StringComparison.Ordinal))
        {
            return null;
        }

        FakeEventChain chain = new(session, new FakeAttachedServiceFactory(), observer);

        // The six columns of dw_sqlite.srd, in declaration order, with the marks that file carries
        // [ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L8-L14].
        Declare(chain, "id", FakeColumnType.Number, key: true, identity: true);
        Declare(chain, "name", FakeColumnType.CharOf(100));
        Declare(chain, "age", FakeColumnType.Number);
        Declare(chain, "address", FakeColumnType.CharOf(200));
        Declare(chain, "salary", FakeColumnType.DecimalOf(2));
        Declare(chain, "birth", FakeColumnType.Date);

        // One row, so a row-scoped event has a row to act on. `InsertRow(0)` appends, which is the
        // oracle's own convention for "at the end".
        _ = chain.Host.InsertRow(0L);

        Last = chain;

        return chain;
    }

    private static void Declare(
        FakeEventChain chain,
        string columnName,
        string columnType,
        bool key = false,
        bool identity = false)
    {
        FakeDataWindowObjectDefinition column = chain.Host.AddColumn(columnName, columnType);

        column.Update = true;
        column.UpdateWhereClause = true;
        column.Key = key;
        column.Identity = identity;
    }
}

/// <summary>
/// <c>EventChain</c> as a bidirectional stream: all 22 events, the sequencing token, the ordering
/// enforcement, the decomposed topic, the tri-valued veto and the item-change alphabet.
/// </summary>
/// <param name="fixture">The host whose chains are the fixture's own.</param>
public sealed class DataWindowGrpcEventChainTests(WireEventChainHostFixture fixture)
    : IClassFixture<WireEventChainHostFixture>
{
    /// <summary>All 22 events the oracle declares, as theory rows.</summary>
    /// <remarks>
    /// DERIVED FROM THE CONTRACT ENUM, MINUS ITS UNSPECIFIED MEMBER. Proto3 requires a zero member and it
    /// names no event, so it is excluded rather than given a body - and the count is asserted separately so
    /// the exclusion cannot hide a missing event.
    /// </remarks>
    public static TheoryData<EventId> AllEvents()
    {
        TheoryData<EventId> rows = [];

        foreach (EventId eventId in Enum.GetValues<EventId>().Where(static id => id != EventId.Unspecified))
        {
            rows.Add(eventId);
        }

        return rows;
    }

    /// <summary>The tri-valued veto, as theory rows.</summary>
    public static TheoryData<Veto.Types.Result> Vetoes() =>
    [
        Veto.Types.Result.Continue,
        Veto.Types.Result.PreventOnce,
        Veto.Types.Result.PreventDeep,
    ];

    [Fact]
    public void TheContractPublishesExactlyTheTwentyTwoEventsTheOracleDeclares()
    {
        // NINE SEMANTIC PLUS THIRTEEN RAW, and the divergence the AAP names as the highest risk is exactly
        // 13 against 9. Asserting the total alone would pass if a raw event were mistakenly modelled as a
        // semantic one, so both halves are counted.
        ImmutableArray<EventId> published =
            [.. Enum.GetValues<EventId>().Where(static id => id != EventId.Unspecified)];

        Assert.Equal(22, published.Length);

        ImmutableArray<EventId> semantic =
        [
            EventId.Oninitcontextmenu,        // se_cst_dw.sru:L11
            EventId.Oncontextmenu,            // :L12
            EventId.Onddsgetfilter,           // :L13  `ref string filter`, no return type
            EventId.Oncolumnexpinvokemethod,  // :L14  `any` return over string[]
            EventId.Ondoitemchange,           // :L24
            EventId.Onitemchanged,            // :L25
            EventId.Ondoitemchanged,          // :L26
            EventId.Onddsfiltered,            // :L28
            EventId.Oncolumnexptrace,         // :L32
        ];

        ImmutableArray<EventId> raw =
        [
            EventId.Ondwnrbuttondown,         // :L15
            EventId.Ondwnrbuttonup,           // :L16
            EventId.Ondwnrowchange,           // :L17
            EventId.Ondwnrowchanging,         // :L18
            EventId.Ondwnlbuttondblclk,       // :L19
            EventId.Ondwnlbuttonclk,          // :L20
            EventId.Ondwnchanging,            // :L21
            EventId.Ondwnitemchangefocus,     // :L22
            EventId.Ondwnitemchange,          // :L23
            EventId.Ondwnitemvalidationerror, // :L27
            EventId.Ondwnkillfocus,           // :L29
            EventId.Ondwnlbuttonup,           // :L30
            EventId.Ondwnsetfocus,            // :L31
        ];

        Assert.Equal(9, semantic.Length);
        Assert.Equal(13, raw.Length);

        // ORDERED COMPARISON, NEVER A SET. The enum's numbering follows the oracle's DECLARATION order
        // [se_cst_dw.sru:L11-L32], and that order is carried on the wire as the field number of each body
        // in the `oneof` - so renumbering is a breaking change and is caught here.
        Assert.Equal(
            published,
            [.. semantic.Concat(raw).OrderBy(static id => (int)id)]);
    }

    [Theory]
    [MemberData(nameof(AllEvents))]
    public async Task EveryEventIsRepresentableOnTheWireWithItsArgumentsAndItsReturnValue(EventId eventId)
    {
        DataWindowContractClient client = new(fixture.Host.CreateAuthenticatedGrpcChannel());

        string session = await OpenAsync(client);
        string correlation = string.Concat("evt-", eventId.ToString());

        await using EventChainDriver driver = EventChainDriver.Open(
            client,
            session,
            TestContext.Current.CancellationToken);

        EventNotification notify = DataWindowEventNotifications.Build(eventId, correlation);

        // THE BODY IS POPULATED AND THE ONEOF SELECTED IT. A notification whose body did not round-trip
        // would arrive with `BodyCase` unset, and the server would have nothing to dispatch.
        Assert.NotEqual(EventNotification.BodyOneofCase.None, notify.BodyCase);

        await driver.NotifyAsync(
            notify,
            OrderingDiscipline.Synchronous,
            TestContext.Current.CancellationToken);

        EventResult outcome = await driver.ReadOutcomeAsync(
            eventId,
            TestContext.Current.CancellationToken);

        // THE CORRELATION AND THE IDENTITY BOTH COME BACK. Without them a caller multiplexing a chain
        // could not tell which of its own notifications an outcome belongs to.
        Assert.Equal(correlation, outcome.CorrelationId);
        Assert.Equal(eventId, outcome.EventId);

        // AND A RETURN VALUE HAS SOMEWHERE TO LIVE, for every event - including the four the oracle
        // declares with no return type at all [se_cst_dw.sru:L13, L25, L26, L28, L32], whose numeric is the
        // prevent convention rather than a declared result.
        Assert.NotNull(outcome.Dispatch);

        await driver.CompleteAsync();
    }

    [Fact]
    public async Task EveryMessageCarriesItsSequencingTokenOnTheGeneratedWireMessage()
    {
        DataWindowContractClient client = new(fixture.Host.CreateAuthenticatedGrpcChannel());

        string session = await OpenAsync(client);

        await using EventChainDriver driver = EventChainDriver.Open(
            client,
            session,
            TestContext.Current.CancellationToken);

        List<long> outboundTokens = [];
        List<long> inboundTokens = [];

        foreach (EventId eventId in new[]
        {
            EventId.Ondwnsetfocus,
            EventId.Ondwnrowchange,
            EventId.Ondwnlbuttonclk,
        })
        {
            long sequence = driver.NextSequence();

            outboundTokens.Add(sequence);

            await driver.NotifyAsync(
                DataWindowEventNotifications.Build(eventId, string.Concat("tok-", eventId.ToString())),
                OrderingDiscipline.Sequenced,
                sequence,
                TestContext.Current.CancellationToken);

            EventResult outcome = await driver.ReadOutcomeAsync(
                eventId,
                TestContext.Current.CancellationToken);

            Assert.Equal(eventId, outcome.EventId);
        }

        foreach (EventChainResponse response in driver.Received)
        {
            // THE TOKEN IS ON THE GENERATED WIRE MESSAGE, not merely in the server's own bookkeeping. That
            // is the whole difference between a boundary a client can police and one it must trust: with
            // `SequencingToken` on `EventChainResponse`, a caller can detect a reordering itself.
            Assert.NotNull(response.Token);

            inboundTokens.Add(response.Token.Sequence);
        }

        // MONOTONIC IN BOTH DIRECTIONS. Ordered comparison against the sorted-distinct projection of the
        // same list, which is what "strictly increasing" means and what a set comparison could not say.
        Assert.Equal([.. outboundTokens.Distinct().Order()], outboundTokens);
        Assert.Equal([.. inboundTokens.Distinct().Order()], inboundTokens);

        // AND THE TWO DIRECTIONS DRAW FROM ONE COUNTER, so a token never repeats across the conversation.
        Assert.Empty(outboundTokens.Intersect(inboundTokens));

        await driver.CompleteAsync();
    }

    [Fact]
    public async Task TheDisciplineIsCarriedOnTheTokenRatherThanBeingAssumed()
    {
        DataWindowContractClient client = new(fixture.Host.CreateAuthenticatedGrpcChannel());

        string session = await OpenAsync(client);

        await using EventChainDriver driver = EventChainDriver.Open(
            client,
            session,
            TestContext.Current.CancellationToken);

        await driver.NotifyAsync(
            DataWindowEventNotifications.Build(EventId.Ondwnsetfocus, "discipline"),
            OrderingDiscipline.Sequenced,
            TestContext.Current.CancellationToken);

        EventResult outcome = await driver.ReadOutcomeAsync(
            EventId.Ondwnsetfocus,
            TestContext.Current.CancellationToken);

        Assert.Equal(EventId.Ondwnsetfocus, outcome.EventId);

        EventChainResponse answered = driver.Received.Last(
            static response => response.PayloadCase == EventChainResponse.PayloadOneofCase.Result);

        // AAP 0.6.1.4 assigns the two patterns PER CAPABILITY AREA, not globally, so the discipline in
        // force has to travel with each message - a boundary that assumed one pattern could not carry
        // both. Focus and mouse notification is area (a), the sequencing-token pattern.
        // EventOrderingPatternTests owns the assignment; this asserts the wire carries it.
        Assert.NotEqual(OrderingDiscipline.Unspecified, answered.Token.Discipline);

        await driver.CompleteAsync();
    }

    [Fact]
    public async Task AnOutOfOrderSequenceIsAHardErrorAndIsNeitherBufferedReorderedNorReplayed()
    {
        DataWindowContractClient client = new(fixture.Host.CreateAuthenticatedGrpcChannel());

        string session = await OpenAsync(client);

        await using EventChainDriver driver = EventChainDriver.Open(
            client,
            session,
            TestContext.Current.CancellationToken);

        // A first, well-ordered message, so the stream has a mark to regress from.
        await driver.NotifyAsync(
            DataWindowEventNotifications.Build(EventId.Ondwnsetfocus, "in-order"),
            OrderingDiscipline.Synchronous,
            1L,
            TestContext.Current.CancellationToken);

        EventResult accepted = await driver.ReadOutcomeAsync(
            EventId.Ondwnsetfocus,
            TestContext.Current.CancellationToken);

        Assert.Equal(EventId.Ondwnsetfocus, accepted.EventId);

        int beforeRegression = driver.Received.Length;

        // NOW REGRESS THE TOKEN. Under the synchronous discipline the expected token is the ONLY one
        // admitted, because one event's behaviour there is a function of its predecessor's return value -
        // the validation-error handler reads and clears the code the item-change event stashed
        // [se_cst_dw.sru:L96], so a reordering there is not merely undesirable, it is semantically
        // impossible.
        await driver.NotifyAsync(
            DataWindowEventNotifications.Build(EventId.Ondwnkillfocus, "out-of-order"),
            OrderingDiscipline.Synchronous,
            1L,
            TestContext.Current.CancellationToken);
        await driver.CompleteAsync();

        RpcException violation = await Assert.ThrowsAsync<RpcException>(
            () => driver.DrainAsync(TestContext.Current.CancellationToken));

        // A HARD ERROR, NOT A REORDER OPPORTUNITY. The token is for DETECTION ONLY inside a synchronous
        // group, and the failure is terminal for the conversation.
        Assert.Equal(StatusCode.FailedPrecondition, violation.StatusCode);

        // THE STATUS SAYS THE SERVER DOES NOT BUFFER, in the words a client will read. Asserted because
        // the alternative - a server that quietly held the message and re-sorted - would produce the same
        // status code while behaving completely differently.
        Assert.Contains("does NOT buffer", violation.Status.Detail, StringComparison.Ordinal);

        // AND NOTHING WAS REPLAYED. The out-of-order event produced no outcome of its own, and the
        // conversation's earlier outcome was not re-sent. A server that buffered and replayed would show
        // an extra response here.
        Assert.Equal(beforeRegression, driver.Received.Length);
        Assert.DoesNotContain(
            EventId.Ondwnkillfocus,
            driver.Received
                .Where(static response =>
                    response.PayloadCase == EventChainResponse.PayloadOneofCase.Result)
                .Select(static response => response.Result.EventId));
    }

    [Fact]
    public async Task TheTopicTripleCrossesAsThreeFieldsAndTheFusedLegacyFormOnlyAtTheCompatibilityEdge()
    {
        DataWindowContractClient client = new(fixture.Host.CreateAuthenticatedGrpcChannel());

        string session = await OpenAsync(client);

        await using EventChainDriver driver = EventChainDriver.Open(
            client,
            session,
            TestContext.Current.CancellationToken);

        // `ondoitemchanged` triggers EVT_ITEMCHANGED, whose legacy spelling carries a SEQUENCE PREFIX:
        // constant string EVT_ITEMCHANGED = "0-itemchanged" [se_cst_dw.sru:L54], beside
        // constant string EVT_EDITCHANGED = "1-editchanged" [:L57]. The broker's dispatch order derives
        // from the LEXICAL SORT of the subscription name, so those prefixes are ordering data fused into
        // an identity string - and `.^persistent` fuses a LIFETIME into the same string
        // [n_cst_threading.sru:L544,L596]. Three independent encodings, one opaque value.
        await driver.NotifyAsync(
            DataWindowEventNotifications.Build(EventId.Ondoitemchanged, "topic"),
            OrderingDiscipline.Synchronous,
            TestContext.Current.CancellationToken);

        EventResult outcome = await driver.ReadOutcomeAsync(
            EventId.Ondoitemchanged,
            TestContext.Current.CancellationToken);

        BrokerTopic topic = Assert.IsType<BrokerTopic>(outcome.Topic);

        // THE THREE FIELDS, DECOMPOSED. Transmit the fused string alone and the ordering becomes invisible
        // to a consumer; parse it at the far end and the contract acquires an undocumented grammar.
        Assert.Equal(0, topic.Sequence);
        Assert.True(topic.HasSequencePrefix);
        Assert.Equal("itemchanged", topic.Name);
        Assert.Equal(BrokerTopic.Types.Lifetime.Transient, topic.Lifetime);

        // AND THE FUSED FORM SURVIVES IN EXACTLY ONE PLACE - the compatibility edge. `legacy_name` remains
        // the AUTHORITATIVE name and the dispatch key, because it is the lexical sort of THAT string which
        // keeps "0-itemchanged" ahead of "1-editchanged"; sorting on the decomposed `name` would reverse
        // them.
        Assert.Equal("0-itemchanged", topic.LegacyName);
        Assert.Equal(BrokerTopic.Types.WellKnownName.EvtItemchanged, topic.WellKnown);

        // The decomposition is a PROJECTION of the fused form, never a replacement: reconstituting it from
        // the three fields must give the legacy spelling back exactly.
        Assert.Equal(
            topic.LegacyName,
            string.Format(
                CultureInfo.InvariantCulture,
                "{0}-{1}",
                topic.Sequence,
                topic.Name));

        // The DECOMPOSED name is NOT the fused one - which is the assertion that the two fields are
        // genuinely different things rather than the same string copied twice.
        Assert.NotEqual(topic.LegacyName, topic.Name);

        await driver.CompleteAsync();
    }

    [Theory]
    [MemberData(nameof(Vetoes))]
    public void TheVetoCrossesTheWireTriValuedAndAgreesWithTheDomainValueForValue(Veto.Types.Result veto)
    {
        // THREE STATES, NEVER A BOOLEAN. `n_cst_eventful.sru:L111-L112` distinguishes prevent-once, which
        // is cleared as the dispatch unwinds, from prevent-deep, which SURVIVES the unwind. Flattening the
        // pair to a boolean silently turns a deep prevention into a shallow one - the dispatch continues at
        // the next level up, and nothing reports that it did.
        Assert.Equal(3, Enum.GetValues<Veto.Types.Result>().Length);

        DomainVetoResult domain = veto switch
        {
            Veto.Types.Result.Continue => DomainVetoResult.Continue,
            Veto.Types.Result.PreventOnce => DomainVetoResult.PreventOnce,
            Veto.Types.Result.PreventDeep => DomainVetoResult.PreventDeep,
            _ => throw new ArgumentOutOfRangeException(nameof(veto), veto, "Unmapped veto state."),
        };

        // VALUE FOR VALUE with the ported enum. Nothing in the compiler holds this agreement - the two are
        // unrelated types in different assemblies - so this assertion is the only thing that does.
        Assert.Equal((int)domain, (int)veto);

        // AND THE WIRE SPELLING IS READ OFF THE DESCRIPTOR rather than retyped here, so the SCREAMING_SNAKE
        // name this file must not declare is still asserted.
        EnumValueDescriptor value = Veto.Descriptor
            .EnumTypes
            .Single(static enumType => string.Equals(enumType.Name, "Result", StringComparison.Ordinal))
            .FindValueByNumber((int)veto);

        Assert.Equal(
            veto switch
            {
                Veto.Types.Result.Continue => "CONTINUE",
                Veto.Types.Result.PreventOnce => "PREVENT_ONCE",
                Veto.Types.Result.PreventDeep => "PREVENT_DEEP",
                _ => throw new ArgumentOutOfRangeException(nameof(veto), veto, "Unmapped veto state."),
            },
            value.Name);
    }

    [Fact]
    public async Task BothVetoChannelsTravelSeparatelyOnTheResult()
    {
        DataWindowContractClient client = new(fixture.Host.CreateAuthenticatedGrpcChannel());

        string session = await OpenAsync(client);

        await using EventChainDriver driver = EventChainDriver.Open(
            client,
            session,
            TestContext.Current.CancellationToken);

        await driver.NotifyAsync(
            DataWindowEventNotifications.Build(EventId.Ondwnrbuttondown, "veto-channels"),
            OrderingDiscipline.Sequenced,
            TestContext.Current.CancellationToken);

        EventResult outcome = await driver.ReadOutcomeAsync(
            EventId.Ondwnrbuttondown,
            TestContext.Current.CancellationToken);

        // TWO INDEPENDENT VETO CHANNELS, because the oracle has two: a raw event has up to two edges - a
        // PARTNER call and a BROKER trigger - and either can stop the dispatch. On this event both exist -
        // `if Event RButtonDown(...) = 1 then return 1` followed by
        // `if Eventful.of_Trigger(EVT_RBUTTONDOWN,...) = 1 then return 1` [se_cst_dw.sru:L115-L117].
        // Collapsing them into one field would lose which of the two prevented, and they unwind
        // differently.
        Assert.Equal(Veto.Types.Result.Continue, outcome.SemanticVeto);
        Assert.Equal(Veto.Types.Result.Continue, outcome.BrokerVeto);

        await driver.CompleteAsync();
    }

    [Fact]
    public void TheItemChangeAlphabetIsItsOwnFourValueEnumAgreeingWithTheDomainValueForValue()
    {
        // FOUR VALUES, ITS OWN TYPE. `se_cst_dw.sru:L182-L253` dispatches on {0,1,2,3} and NONE of those
        // digits means what the same digit means in the return-code algebra: `case 3` keeps the value, does
        // not move focus, and REWRITES the result to 1 [:L223-L225], while the default arm coerces by
        // column-type prefix and then FORCIBLY RETURNS 2 [:L250] so the runtime will not re-apply the edit
        // text over the buffer. Mapping this onto RetCode - where 1 is PREVENT and 2 is nothing at all -
        // would corrupt every one of those meanings.
        ImmutableArray<WireItemChangeResult> wire = [.. Enum.GetValues<WireItemChangeResult>()];

        Assert.Equal(4, wire.Length);

        // VALUE FOR VALUE against Domain/ItemChangeProtocol.cs. This agreement has NO compile-time
        // enforcement - two enums in two assemblies with no relationship the compiler can see - so this is
        // the only thing holding it, exactly as the file's brief states.
        Assert.Equal((int)DomainItemChangeResult.Default, (int)WireItemChangeResult.Default);
        Assert.Equal(
            (int)DomainItemChangeResult.TriggerValidationError,
            (int)WireItemChangeResult.TriggerValidationError);
        Assert.Equal(
            (int)DomainItemChangeResult.RestoreAndRejectText,
            (int)WireItemChangeResult.RestoreAndRejectText);
        Assert.Equal(
            (int)DomainItemChangeResult.KeepValueNoFocusMove,
            (int)WireItemChangeResult.KeepValueNoFocusMove);

        // The numerals themselves, ordered, because the oracle dispatches on the NUMERAL and a renumbering
        // would change which arm of `choose case` runs.
        Assert.Equal([0, 1, 2, 3], [.. wire.Select(static value => (int)value).Order()]);

        // NAME FOR NAME as well, so a rename on either side is caught rather than silently tolerated by an
        // ordinal comparison that only looks at the numbers.
        Assert.Equal(
            [.. Enum.GetNames<DomainItemChangeResult>()],
            [.. Enum.GetNames<WireItemChangeResult>()]);

        // AND IT IS NOT THE VETO ALPHABET, though they share the numerals 1 and 2. Asserted because the
        // collision is exactly the sort a reader resolves by assuming they are the same thing.
        Assert.NotEqual(
            typeof(WireItemChangeResult),
            typeof(Veto.Types.Result));
    }

    [Fact]
    public async Task TheItemChangeAlphabetReachesTheCallerOnItsOwnFieldRatherThanAsAReturnCode()
    {
        DataWindowContractClient client = new(fixture.Host.CreateAuthenticatedGrpcChannel());

        string session = await OpenAsync(client);

        await using EventChainDriver driver = EventChainDriver.Open(
            client,
            session,
            TestContext.Current.CancellationToken);

        await driver.NotifyAsync(
            DataWindowEventNotifications.Build(EventId.Ondwnitemchange, "alphabet"),
            OrderingDiscipline.Synchronous,
            TestContext.Current.CancellationToken);

        EventResult outcome = await driver.ReadOutcomeAsync(
            EventId.Ondwnitemchange,
            TestContext.Current.CancellationToken);

        // THE DEFAULT ARM'S FORCED 2, PRESERVED VERBATIM (C-B). The notification answers the semantic
        // handler with 0, which falls to `case else`: the arm coerces by the first five characters of the
        // column type, fires the changed event, and then FORCIBLY RETURNS 2 [se_cst_dw.sru:L228-L250]. That
        // is a legacy behaviour reproduced, not an implementation choice - a "tidier" 0 here would make the
        // runtime re-apply the edit text over the buffer the handler just wrote.
        Assert.Equal(WireItemChangeResult.RestoreAndRejectText, outcome.ItemChangeResult);

        // THE NUMERIC AND THE ALPHABET AGREE, on their own separate fields. `return_value` carries the
        // numeric the chain returned; `item_change_result` carries the alphabet. Both are populated, and
        // neither is derived from the return-code algebra.
        Assert.Equal((long)WireItemChangeResult.RestoreAndRejectText, outcome.ReturnValue);
        Assert.Equal(2L, outcome.ReturnValue);

        // AND THAT 2 IS NOT RetCode's 2. RetCode has no 2 at all - PREVENT is 1 and CANCELLED is -2
        // [retcode.sru:L42,L44-L45] - which is the clearest possible demonstration that the two alphabets
        // must not be mapped onto one another.
        Assert.NotEqual(RetCode.PREVENT, outcome.ReturnValue);
        Assert.NotEqual(RetCode.OK, outcome.ReturnValue);

        await driver.CompleteAsync();
    }

    private static async Task<string> OpenAsync(DataWindowContractClient client) =>
        (await client.OpenValidationSessionAsync(
            new OpenValidationSessionRequest
            {
                DatawindowHandle = DataWindowCatalogue.SqliteFixtureName,
            },
            cancellationToken: TestContext.Current.CancellationToken)).SessionId;
}

/// <summary>
/// A Persistence double reaching far enough to drive <c>Update</c> end to end.
/// </summary>
/// <remarks>
/// <para>
/// WHY THE FIXTURE'S OWN EDGE IS NOT ENOUGH HERE, AND WHY THAT IS WORTH SAYING. <c>Update</c> prepares the
/// update contract before submitting rows - <c>PrepareUpdate</c> carries the repeated table descriptor that
/// reproduces the legacy <c>Tables[]</c> array - and <c>ScriptedPersistenceClient</c> does not override
/// <c>PrepareUpdateAsync</c>. Left alone it reaches the real client, which reaches the substituted
/// transport, which has no route scripted for it and THROWS RATHER THAN ANSWERING - by design, because a
/// plausible answer would let a mis-wired test pass through the client's error handling and assert
/// something true about a request that never arrived. The observable consequence is
/// <c>StatusCode.Unavailable</c> on a case whose subject is <c>Aborted</c>, which is exactly the sort of
/// false negative that gets an assertion weakened. This double closes the gap by SCRIPTING THE PREPARE and
/// delegating everything else to the same recording edge, so the conflict assertions are about the conflict.
/// </para>
/// <para>
/// IT ADDS NO PACKAGE AND NO MOCKING FRAMEWORK. <c>PersistenceClient</c> is a public class whose operations
/// are <c>virtual</c> precisely so a derived double can stand in for it; the whole dependency inventory
/// deliberately carries no mocking library.
/// </para>
/// </remarks>
internal sealed class UpdatePathPersistenceClient : PersistenceClient
{
    private readonly ScriptedPersistenceEdge _edge;

    internal UpdatePathPersistenceClient(ScriptedPersistenceEdge edge, IServiceProvider services)
        : base(
            services.GetRequiredService<
                PowerFramework.Contracts.Persistence.V1.QueryService.QueryServiceClient>(),
            services.GetRequiredService<
                PowerFramework.Contracts.Persistence.V1.UpdateService.UpdateServiceClient>(),
            services.GetRequiredService<
                PowerFramework.Contracts.Persistence.V1.CommandService.CommandServiceClient>(),
            services.GetRequiredService<
                PowerFramework.Contracts.Persistence.V1.TransactionService.TransactionServiceClient>(),
            services.GetRequiredService<PowerFramework.DataServices.Clients.IServiceTokenProvider>(),
            services.GetRequiredService<
                Microsoft.Extensions.Logging.ILogger<PersistenceClient>>())
    {
        ArgumentNullException.ThrowIfNull(edge);

        _edge = edge;
    }

    /// <summary>Every prepare request this double was asked to perform, in order.</summary>
    internal List<PowerFramework.Contracts.Persistence.V1.PrepareUpdateRequest> PrepareRequests { get; } =
        [];

    /// <inheritdoc />
    public override Task<PowerFramework.Contracts.Persistence.V1.BeginSessionResponse> BeginSessionAsync(
        PowerFramework.Contracts.Persistence.V1.BeginSessionRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_edge.RecordBeginSession());

    /// <inheritdoc />
    public override Task<PowerFramework.Contracts.Persistence.V1.EndSessionResponse> EndSessionAsync(
        PowerFramework.Contracts.Persistence.V1.EndSessionRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_edge.RecordEndSession(request));

    /// <inheritdoc />
    public override Task<PowerFramework.Contracts.Persistence.V1.CreateUpdateTaskResponse>
        CreateUpdateTaskAsync(
            PowerFramework.Contracts.Persistence.V1.CreateUpdateTaskRequest request,
            CancellationToken cancellationToken = default) =>
        Task.FromResult(_edge.RecordCreateUpdateTask(request));

    /// <inheritdoc />
    public override Task<PowerFramework.Contracts.Persistence.V1.ReleaseUpdateTaskResponse>
        ReleaseUpdateTaskAsync(
            PowerFramework.Contracts.Persistence.V1.ReleaseUpdateTaskRequest request,
            CancellationToken cancellationToken = default) =>
        Task.FromResult(_edge.RecordReleaseUpdateTask(request));

    /// <inheritdoc />
    public override Task<PowerFramework.Contracts.Persistence.V1.PrepareUpdateResponse> PrepareUpdateAsync(
        PowerFramework.Contracts.Persistence.V1.PrepareUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        PrepareRequests.Add(request);

        return Task.FromResult(new PowerFramework.Contracts.Persistence.V1.PrepareUpdateResponse
        {
            Status = new PowerFramework.Contracts.Persistence.V1.OperationStatus
            {
                RetCode = WireRetCode.Ok,
            },
        });
    }

    /// <inheritdoc />
    public override Task<PowerFramework.Contracts.Persistence.V1.UpdateResponse> UpdateAsync(
        PowerFramework.Contracts.Persistence.V1.UpdateRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_edge.RecordUpdate(request));
}

/// <summary>
/// <c>Update</c>, and the concurrency conflict it must surface rather than resolve.
/// </summary>
/// <remarks>
/// <para>
/// THE ORACLE'S CONCURRENCY CONTRACT. <c>dw_sqlite.srd</c> declares <c>updatewhere=1</c> [<c>:L14</c>] -
/// the "key and updateable columns" mode - and marks ALL SIX columns <c>updatewhereclause=yes</c>
/// [<c>:L8-L14</c>], so the generated where-clause carries the key plus the ORIGINAL VALUES of every
/// updateable column and the optimistic-concurrency check spans all six. When the row moved underneath the
/// caller, the update matches nothing.
/// </para>
/// <para>
/// AND WHAT THE BOUNDARY MUST DO ABOUT IT (AAP 0.8.1). Return a VERSIONED conflict response carrying the
/// CURRENT ROW STATE, with a defined retry-or-surface policy and NO SILENT OVERWRITE. `Aborted` is the
/// canonical gRPC-to-HTTP-409 mapping, which is what lets Gateway's REST projection surface the conflict
/// unchanged instead of inventing a translation of its own. That projection is
/// <c>RestProjectionInBandStatusTests</c>'s to assert; the gRPC side of the same sentence is asserted here.
/// </para>
/// <para>
/// TWO OF THE LEGACY UPDATE PATH'S OWN INVERSIONS ARE WHY THE "NEVER SYNTHESISED" ROW EXISTS.
/// <c>n_cst_thread_task_sqlupdate.sru</c> treats 1 - not the return algebra's 0 - as success, and then
/// DEFENSIVELY REWRITES a claimed success into a failure when the transaction's SQL code says otherwise
/// [<c>:L208-L210</c>]. An implementation that trusted the update's own return value would report success
/// on a failed update, so the direction that matters is that a failure upstream can never read as a success
/// here.
/// </para>
/// </remarks>
public sealed class DataWindowGrpcUpdateConflictTests
{
    [Fact]
    public async Task AConcurrencyMismatchIsAbortedCarryingTheCurrentRowState()
    {
        await using UpdatePathHost host = await UpdatePathHost.StartAsync();

        // The row moved underneath the caller: one row expected, none matched.
        ConflictDetail scripted = host.Edge.ScriptUpdateConflict(
            rowsExpected: 1L,
            rowsMatched: 0L,
            ScriptedPersistenceResponses.ModifiedConflictRow(
                row: 1L,
                columnName: "salary",
                columnId: 5L,
                currentValue: 60000d,
                originalValue: 50000d));

        RpcException aborted = await Assert.ThrowsAsync<RpcException>(
            () => host.UpdateAsync(DwSqliteWireRows.Build(1L)));

        // ABORTED, THE CANONICAL 409. Not Internal, not Unknown, not a successful response carrying a
        // failure code in its body - a caller that maps gRPC status to HTTP gets 409 without being told to.
        Assert.Equal(StatusCode.Aborted, aborted.StatusCode);

        // THE STATUS CODE IS THE ONE THE DESCRIPTOR DECLARES, read off the contract rather than retyped.
        // The declaration is the machine-readable half of the sentence above: a client discovers the code,
        // the trailer key and the payload type from the method's options instead of from prose.
        RichErrorBinding binding = ScriptedPersistenceResponses.UpdateConflictBinding();

        Assert.Equal((int)StatusCode.Aborted, binding.GrpcStatusCode);
        Assert.Equal(RichErrorTrailer.Descriptor.FullName, binding.PayloadType);

        // THE DETAIL TRAVELS AS A BINARY TRAILER, because a gRPC status carries only a code and a message.
        Metadata.Entry entry = Assert.Single(
            aborted.Trailers,
            candidate => string.Equals(candidate.Key, binding.TrailerKey, StringComparison.Ordinal));

        RichErrorTrailer trailer = RichErrorTrailer.Parser.ParseFrom(entry.ValueBytes);

        Assert.Equal(RichErrorTrailer.DetailOneofCase.Conflict, trailer.DetailCase);

        ConflictDetail detail = trailer.Conflict;

        // THE CURRENT ROW STATE, RELAYED INTACT. A conflict a caller cannot inspect is a conflict it cannot
        // resolve, so the counts, the table and the row are all carried - and the table is the one the
        // fixture actually names.
        Assert.Equal(scripted.RowsExpected, detail.RowsExpected);
        Assert.Equal(scripted.RowsMatched, detail.RowsMatched);
        Assert.Equal(1L, detail.RowsExpected);
        Assert.Equal(0L, detail.RowsMatched);
        Assert.Equal(ScriptedPersistenceResponses.EvidencedUpdateTable, detail.UpdateTable);

        ConflictRow conflicted = Assert.Single(detail.Rows);

        // BOTH VALUE SETS, AGAIN - and this is where they earn their keep. The current values say what the
        // row holds NOW; the original values are what the where-clause compared against and failed to match.
        // A conflict carrying only one of the two would leave the caller unable to see what changed.
        Assert.Equal(DwBuffer.Primary, conflicted.Buffer);
        Assert.Equal(1L, conflicted.Row);
        Assert.Equal(ItemStatus.DataModified, conflicted.ItemStatus);
        Assert.NotEmpty(conflicted.CurrentValues);
        Assert.NotEmpty(conflicted.OriginalValues);
        Assert.Equal(60000d, Assert.Single(conflicted.CurrentValues).Value.DoubleValue);
        Assert.Equal(50000d, Assert.Single(conflicted.OriginalValues).Value.DoubleValue);
    }

    [Fact]
    public async Task TheConflictIsNeitherRetriedSwallowedNorRemapped()
    {
        await using UpdatePathHost host = await UpdatePathHost.StartAsync();

        _ = host.Edge.ScriptUpdateConflict(
            rowsExpected: 1L,
            rowsMatched: 0L,
            ScriptedPersistenceResponses.ModifiedConflictRow(
                row: 1L,
                columnName: "salary",
                columnId: 5L,
                currentValue: 60000d,
                originalValue: 50000d));

        RpcException aborted = await Assert.ThrowsAsync<RpcException>(
            () => host.UpdateAsync(DwSqliteWireRows.Build(1L)));

        // EXACTLY ONE UPSTREAM ATTEMPT. NOT RETRIED - the caller implements an explicit retry-or-surface
        // policy, and a retry here would silently defeat it: the second attempt would compare against
        // original values the caller has not seen since, and could succeed against a row the caller never
        // agreed to overwrite. `Microsoft.Extensions.Http.Resilience` is in the dependency inventory for
        // the transport faults decomposition itself creates, and a concurrency conflict is not one of them.
        Assert.Equal(1, host.Edge.UpdateCalls);

        // NOT SWALLOWED - it surfaced as a status rather than as a response with a quiet code in its body.
        Assert.Equal(StatusCode.Aborted, aborted.StatusCode);

        // NOT REMAPPED - the detail is still a ConflictDetail under the key the contract declares, not
        // flattened into the status message or re-encoded under a key of this service's own invention. The
        // SAME key and payload type C-06 uses, deliberately: Gateway relays what DataServices relays from
        // Persistence, and one decoding path must serve the whole chain.
        RichErrorBinding binding = ScriptedPersistenceResponses.UpdateConflictBinding();

        Assert.Contains(
            binding.TrailerKey,
            aborted.Trailers.Select(static candidate => candidate.Key),
            StringComparer.Ordinal);
    }

    [Fact]
    public async Task NothingIsOverwrittenSilentlyAndTheUpstreamRowsAreUnchanged()
    {
        await using UpdatePathHost host = await UpdatePathHost.StartAsync();

        _ = host.Edge.ScriptUpdateConflict(
            rowsExpected: 1L,
            rowsMatched: 0L,
            ScriptedPersistenceResponses.ModifiedConflictRow(
                row: 1L,
                columnName: "salary",
                columnId: 5L,
                currentValue: 60000d,
                originalValue: 50000d));

        DataWindowRow submitted = DwSqliteWireRows.Build(1L);

        _ = await Assert.ThrowsAsync<RpcException>(() => host.UpdateAsync(submitted));

        // THE ROWS THE UPSTREAM WAS ASKED TO WRITE ARE THE ROWS THE CALLER SENT, and nothing followed the
        // refusal. THERE IS NO SILENT OVERWRITE ANYWHERE IN THE SYSTEM: no second submission with the
        // original values rewritten to match, no force flag, no fallback.
        Assert.Equal(1, host.Edge.UpdateCalls);

        PowerFramework.Contracts.Persistence.V1.UpdateRequest? relayed = host.Edge.LastUpdateRequest;

        Assert.NotNull(relayed);

        // THE CARRIER IS POSITIONAL, AND ALL THREE CANONICAL SEGMENTS ARE PRESENT IN BUFFER ORDER. The
        // codec validates segment n against buffer n [Buffers/ChangesetCodec.cs - SerializedBuffers,
        // TryValidateSegments], so an update carrying only the buffers it happens to have rows for would be
        // rejected downstream. Compared as an ordered list because the ORDER is the addressing.
        Assert.Equal(
            [DwBuffer.Primary, DwBuffer.Delete, DwBuffer.Filter],
            [.. relayed.UpdateData.Segments.Select(static segment => segment.Buffer)]);

        // The original values reached the upstream INTACT - they are the concurrency check, so a boundary
        // that dropped or rewrote them would turn every conflict into a silent overwrite by construction.
        PowerFramework.Contracts.Persistence.V1.CarrierBufferSegment segment = Assert.Single(
            relayed.UpdateData.Segments,
            candidate => candidate.Buffer == DwBuffer.Primary);
        DataWindowRow arrived = Assert.Single(segment.Rows);

        Assert.Equal(DwSqliteWireRows.ColumnNames.Length, arrived.OriginalValues.Count);
        Assert.Equal(
            [.. submitted.OriginalValues.Select(static column => column.Value.StringValue)],
            [.. arrived.OriginalValues.Select(static column => column.Value.StringValue)]);
    }

    [Fact]
    public async Task TheSuccessfulPathReturnsTheCountsAndIdentityInformationTheContractDeclares()
    {
        await using UpdatePathHost host = await UpdatePathHost.StartAsync();

        _ = host.Edge.ScriptUpdateSuccess(rowsInserted: 2L, rowsUpdated: 3L, rowsDeleted: 1L);

        PowerFramework.Contracts.DataServices.V1.UpdateResponse response =
            await host.UpdateAsync(DwSqliteWireRows.Build(1L));

        // ASSERTED ALONGSIDE THE CONFLICT ROWS ON PURPOSE. A suite that only proved the refusal would leave
        // open that every update is refused.
        Assert.Equal(WireRetCode.Success, response.RetCode);
        Assert.Equal(2L, response.RowsInserted);
        Assert.Equal(3L, response.RowsUpdated);
        Assert.Equal(1L, response.RowsDeleted);

        // THE IDENTITY ROUND TRIP HAS SOMEWHERE TO LIVE. `_of_updateexecute` discovers the identity column
        // and collects the values newly-modified rows were assigned, from the primary buffer FORWARD and the
        // filter buffer BACKWARD - because the filter buffer's row order is inverted relative to the source
        // [n_cst_thread_task_sqlupdate.sru:L235,L237]. The field is repeated so a multi-table update from one
        // DataWindow, which the legacy `Tables[]` array genuinely supports, can report one per table.
        Assert.NotNull(response.Identity);

        // AND THE PREPARE CARRIED THE TABLE CONTRACT. `_of_updateprepare` does not trust the DataWindow's
        // static definition: it resets update, key and identity to off on every column and then selectively
        // re-enables from the descriptor array [:L104-L108]. The descriptor is therefore part of the request,
        // not an assumption the server makes.
        Assert.NotEmpty(host.Client.PrepareRequests);
    }

    [Fact]
    public async Task ASuccessIsNeverSynthesisedFromAFailedUpstream()
    {
        await using UpdatePathHost host = await UpdatePathHost.StartAsync();

        // The upstream reports a database error rather than a concurrency conflict - the other way an update
        // fails, and the one a defensive implementation is most likely to paper over.
        host.Edge.UpdateResponse = new PowerFramework.Contracts.Persistence.V1.UpdateResponse
        {
            Status = new PowerFramework.Contracts.Persistence.V1.OperationStatus
            {
                RetCode = WireRetCode.EDbError,
            },
        };

        StatusCode? status = null;
        PowerFramework.Contracts.DataServices.V1.UpdateResponse? response = null;

        try
        {
            response = await host.UpdateAsync(DwSqliteWireRows.Build(1L));
        }
        catch (RpcException failed)
        {
            status = failed.StatusCode;
        }

        // EITHER SHAPE IS ACCEPTABLE - a status, or a response carrying a failing code - BUT NOT SUCCESS.
        // This mirrors the oracle's own defensive override, which rewrites a claimed success into a failure
        // when the transaction's SQL code indicates an error [n_cst_thread_task_sqlupdate.sru:L208-L210]. An
        // implementation that trusted the update's own return value would report success on a failed update,
        // and every downstream count would then be a fiction.
        if (response is not null)
        {
            Assert.NotEqual(WireRetCode.Success, response.RetCode);
            Assert.NotEqual(WireRetCode.Ok, response.RetCode);
        }
        else
        {
            Assert.NotEqual(StatusCode.OK, status);
        }

        // AND THE COUNTS ARE NOT INVENTED. A failed update inserted, updated and deleted nothing, so
        // reporting a non-zero count would be a fabricated result.
        Assert.Equal(0L, response?.RowsInserted ?? 0L);
        Assert.Equal(0L, response?.RowsUpdated ?? 0L);
        Assert.Equal(0L, response?.RowsDeleted ?? 0L);
    }

    /// <summary>A host wired so <c>Update</c> reaches the recording edge for its whole path.</summary>
    private sealed class UpdatePathHost : IAsyncDisposable
    {
        private readonly DataServicesTestHostFactory _host;

        private UpdatePathHost(DataServicesTestHostFactory host, ScriptedPersistenceEdge edge)
        {
            _host = host;
            Edge = edge;
        }

        internal ScriptedPersistenceEdge Edge { get; }

        internal UpdatePathPersistenceClient Client { get; private set; } = null!;

        internal static Task<UpdatePathHost> StartAsync()
        {
            DataServicesTestHostFactory host = new();

            host.PersistenceEdge.Reset();

            UpdatePathHost wired = new(host, host.PersistenceEdge);

            host.AdditionalServiceConfiguration.Add(services =>
            {
                services.RemoveAll<PersistenceClient>();

                // SCOPED, matching the registration it replaces, so the client's lifetime still tracks the
                // call using it. The instance is captured so a test can read what the prepare carried.
                services.AddScoped<PersistenceClient>(provider =>
                {
                    wired.Client = new UpdatePathPersistenceClient(host.PersistenceEdge, provider);

                    return wired.Client;
                });
            });

            return Task.FromResult(wired);
        }

        internal async Task<PowerFramework.Contracts.DataServices.V1.UpdateResponse> UpdateAsync(
            params DataWindowRow[] rows)
        {
            ArgumentNullException.ThrowIfNull(rows);

            DataWindowContractClient client = new(_host.CreateAuthenticatedGrpcChannel());

            WireUpdateRequest request = new()
            {
                DatawindowHandle = DataWindowCatalogue.SqliteFixtureName,
            };

            request.Rows.AddRange(rows);

            return await client.UpdateAsync(
                request,
                cancellationToken: TestContext.Current.CancellationToken);
        }

        public async ValueTask DisposeAsync() => await _host.DisposeAsync();
    }
}


/// <summary>
/// The event-gate surface: the three composable bits, their per-session scope, and the coupling whereby
/// disabling item-change also suppresses column-expression calculation.
/// </summary>
/// <param name="fixture">The host whose chains are the fixture's own.</param>
/// <remarks>
/// <para>
/// THE GATE IS A BITMASK, NOT AN ORDINAL. <c>se_cst_dw.sru:L40</c> annotates the three constants
/// "支持组合" - supports combination - and declares <c>EID_ROWFOCUSCHANGE = 1</c> [<c>:L41</c>],
/// <c>EID_ITEMFOCUSCHANGE = 2</c> [<c>:L42</c>] and <c>EID_ITEMCHANGE = 4</c> [<c>:L43</c>]. The chain tests
/// membership with <c>BitTest</c> [<c>:L124</c>, <c>:L130</c>, <c>:L176</c>, <c>:L187</c>], so the values are
/// powers of two on purpose and renumbering them to 1, 2, 3 would make two of the three bits alias.
/// </para>
/// <para>
/// AND THE COUPLING IS DOCUMENTED AT SOURCE, IN THE COMMENT ON THE CONSTANT ITSELF: EID_ITEMCHANGE carries
/// the note "禁用后将不会触发列表达式计算！" - once disabled, column-expression calculation will NOT be
/// triggered [<c>:L43</c>]. The mechanism is not a special case anywhere; it falls out of the chain's shape.
/// <c>ondwnitemchange</c> returns 0 immediately when the bit is set [<c>:L187</c>], so <c>ondoitemchanged</c>
/// is never reached, and it is <c>ondoitemchanged</c> that calls the column-expression service's changed
/// handler. That is why the coupling is observable as a CONSEQUENCE rather than as a flag.
/// </para>
/// </remarks>
public sealed class DataWindowGrpcEventGateTests(WireEventChainHostFixture fixture)
    : IClassFixture<WireEventChainHostFixture>
{
    /// <summary>
    /// The three gate bits paired with the domain constant each must equal, as theory rows.
    /// </summary>
    public static TheoryData<WireEventGate.Types.Bit, long, string> GateBits() =>
        new()
        {
            {
                WireEventGate.Types.Bit.EidRowfocuschange,
                DomainEventGate.EID_ROWFOCUSCHANGE,
                "EID_ROWFOCUSCHANGE"
            },
            {
                WireEventGate.Types.Bit.EidItemfocuschange,
                DomainEventGate.EID_ITEMFOCUSCHANGE,
                "EID_ITEMFOCUSCHANGE"
            },
            {
                WireEventGate.Types.Bit.EidItemchange,
                DomainEventGate.EID_ITEMCHANGE,
                "EID_ITEMCHANGE"
            },
        };

    [Theory]
    [MemberData(nameof(GateBits))]
    public void EachGateBitEqualsItsDomainConstantValueForValueAndKeepsItsWireSpelling(
        WireEventGate.Types.Bit bit,
        long domain,
        string wireSpelling)
    {
        // VALUE FOR VALUE. The wire bit and the ported constant are unrelated types in different
        // assemblies, so nothing in the compiler holds this agreement and this assertion is the only thing
        // that does. The literal is stated too, because "they agree with each other" would still pass if
        // both drifted together away from the oracle.
        Assert.Equal(domain, (long)bit);

        // THE WIRE SPELLING IS READ OFF THE DESCRIPTOR rather than declared here, which is how a
        // SCREAMING_SNAKE identifier this file must not own is nonetheless asserted. `EnumValueDescriptor`
        // carries the proto name, so a rename in the contract fails this row.
        EnumValueDescriptor value = WireEventGate.Descriptor
            .EnumTypes
            .Single(static enumType => string.Equals(enumType.Name, "Bit", StringComparison.Ordinal))
            .FindValueByNumber((int)bit);

        Assert.Equal(wireSpelling, value.Name);

        // AND EVERY BIT IS A SINGLE BIT. `BitTest` composition [se_cst_dw.sru:L124] requires it; a value
        // like 3 would test true for two logically distinct events at once.
        Assert.Equal(0L, domain & (domain - 1L));
    }

    [Fact]
    public void TheGateEnumCarriesExactlyTheThreeOracleBitsPlusTheProtoZeroMember()
    {
        ImmutableArray<WireEventGate.Types.Bit> bits = [.. Enum.GetValues<WireEventGate.Types.Bit>()];

        // Three real bits, and the unspecified zero proto3 requires. A fourth would mean the oracle grew an
        // event class, which it has not.
        Assert.Equal(4, bits.Length);
        Assert.Equal(
            [0L, 1L, 2L, 4L],
            [.. bits.Select(static bit => (long)bit).Order()]);
    }

    [Fact]
    public async Task GetEventGateReturnsTheCurrentMaskAndDisableAndEnableMutateIt()
    {
        DataWindowContractClient client = new(fixture.Host.CreateAuthenticatedGrpcChannel());

        string session = await OpenAsync(client);

        GetEventGateResponse initial = await client.GetEventGateAsync(
            new GetEventGateRequest { SessionId = session },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0L, initial.Gate.Mask);
        Assert.Empty(initial.DisabledBits);
        Assert.Equal(WireRetCode.Success, initial.RetCode);

        // Disable two bits, one call at a time, so the composition is observed accumulating rather than
        // being set wholesale.
        DisableEventResponse disabledFirst = await client.DisableEventAsync(
            new DisableEventRequest
            {
                SessionId = session,
                EventMask = DomainEventGate.EID_ROWFOCUSCHANGE,
            },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(WireRetCode.Success, disabledFirst.RetCode);
        Assert.Equal(DomainEventGate.EID_ROWFOCUSCHANGE, disabledFirst.Gate.Mask);

        DisableEventResponse disabledBoth = await client.DisableEventAsync(
            new DisableEventRequest
            {
                SessionId = session,
                EventMask = DomainEventGate.EID_ITEMCHANGE,
            },
            cancellationToken: TestContext.Current.CancellationToken);

        // COMPOSED, NOT REPLACED. The oracle ORs the bit in [se_cst_dw.sru:L41-L43 with the BitTest sites],
        // so disabling a second event must not re-enable the first.
        Assert.Equal(
            DomainEventGate.EID_ROWFOCUSCHANGE | DomainEventGate.EID_ITEMCHANGE,
            disabledBoth.Gate.Mask);

        GetEventGateResponse afterDisable = await client.GetEventGateAsync(
            new GetEventGateRequest { SessionId = session },
            cancellationToken: TestContext.Current.CancellationToken);

        // THE PER-BIT PREDICATE IS PROJECTED TOO - the port of `of_iseventdisabled` [se_cst_dw.sru:L109].
        // A caller reading a mask would otherwise have to reimplement BitTest to interpret it.
        Assert.Equal(
            [WireEventGate.Types.Bit.EidRowfocuschange, WireEventGate.Types.Bit.EidItemchange],
            [.. afterDisable.DisabledBits.OrderBy(static bit => (int)bit)]);

        EnableEventResponse enabled = await client.EnableEventAsync(
            new EnableEventRequest
            {
                SessionId = session,
                EventMask = DomainEventGate.EID_ROWFOCUSCHANGE,
            },
            cancellationToken: TestContext.Current.CancellationToken);

        // Enabling clears exactly the named bit and leaves the rest standing.
        Assert.Equal(WireRetCode.Success, enabled.RetCode);
        Assert.Equal(DomainEventGate.EID_ITEMCHANGE, enabled.Gate.Mask);
    }

    [Fact]
    public async Task AZeroMaskIsRefusedOnBothMutatorsAsTheOracleRefusesIt()
    {
        DataWindowContractClient client = new(fixture.Host.CreateAuthenticatedGrpcChannel());

        string session = await OpenAsync(client);

        DisableEventResponse disabled = await client.DisableEventAsync(
            new DisableEventRequest { SessionId = session, EventMask = 0L },
            cancellationToken: TestContext.Current.CancellationToken);

        EnableEventResponse enabled = await client.EnableEventAsync(
            new EnableEventRequest { SessionId = session, EventMask = 0L },
            cancellationToken: TestContext.Current.CancellationToken);

        // A zero mask names no event, so it is an invalid argument rather than a no-op success
        // [se_cst_dw.sru:L506 for disable, :L530 for enable]. Reported IN BAND on the response's own code
        // rather than as a call status, because the contract declares a code field for exactly this.
        Assert.Equal(
            DataWindowWireProjection.ToWireRetCode(RetCode.E_INVALID_ARGUMENT),
            disabled.RetCode);
        Assert.Equal(
            DataWindowWireProjection.ToWireRetCode(RetCode.E_INVALID_ARGUMENT),
            enabled.RetCode);

        // AND THE MASK IS UNTOUCHED by the refusal.
        Assert.Equal(0L, disabled.Gate.Mask);
        Assert.Equal(0L, enabled.Gate.Mask);
    }

    [Fact]
    public async Task TheMaskIsPerSessionSoDisablingInOneCorrelationIdDoesNotAffectAnother()
    {
        DataWindowContractClient client = new(fixture.Host.CreateAuthenticatedGrpcChannel());

        string first = await OpenAsync(client);
        string second = await OpenAsync(client);

        _ = await client.DisableEventAsync(
            new DisableEventRequest
            {
                SessionId = first,
                EventMask = DomainEventGate.EID_ITEMCHANGE,
            },
            cancellationToken: TestContext.Current.CancellationToken);

        GetEventGateResponse firstGate = await client.GetEventGateAsync(
            new GetEventGateRequest { SessionId = first },
            cancellationToken: TestContext.Current.CancellationToken);
        GetEventGateResponse secondGate = await client.GetEventGateAsync(
            new GetEventGateRequest { SessionId = second },
            cancellationToken: TestContext.Current.CancellationToken);

        // THE MASK IS ONE OF THE FOUR CROSS-EVENT FIELDS [se_cst_dw.sru:L89], and in the legacy those fields
        // are PER DATAWINDOW INSTANCE - each control carries its own. A service instance now serves many
        // callers, so a process-wide mask would let one caller disable another's item-change and, through the
        // coupling at :L43, silently suppress a column-expression calculation the other caller depends on.
        Assert.Equal(DomainEventGate.EID_ITEMCHANGE, firstGate.Gate.Mask);
        Assert.Equal(0L, secondGate.Gate.Mask);

        // The bit-level projection is per session too, not just the raw mask.
        Assert.Equal(
            [WireEventGate.Types.Bit.EidItemchange],
            [.. firstGate.DisabledBits]);
        Assert.Empty(secondGate.DisabledBits);
    }

    [Fact]
    public async Task WithItemChangeDisabledTheChainRunsNoColumnExpressionEvaluationAndEmitsNoTracePayload()
    {
        DataWindowContractClient client = new(fixture.Host.CreateAuthenticatedGrpcChannel());

        string session = await OpenAsync(client);

        _ = await client.DisableEventAsync(
            new DisableEventRequest
            {
                SessionId = session,
                EventMask = DomainEventGate.EID_ITEMCHANGE,
            },
            cancellationToken: TestContext.Current.CancellationToken);

        await using EventChainDriver driver = EventChainDriver.Open(
            client,
            session,
            TestContext.Current.CancellationToken);

        await driver.NotifyAsync(
            DataWindowEventNotifications.Build(EventId.Ondwnitemchange, "gated-off"),
            OrderingDiscipline.Synchronous,
            TestContext.Current.CancellationToken);

        EventResult outcome = await driver.ReadOutcomeAsync(
            EventId.Ondwnitemchange,
            TestContext.Current.CancellationToken);

        // THE GATE SHORT-CIRCUITED, AND IT SAYS SO ON THE WIRE. `if BitTest(_nDisabledEvent,EID_ITEMCHANGE)
        // then return 0` [se_cst_dw.sru:L187] - so the semantic handler never ran and the numeric is the
        // oracle's bare 0 rather than an outcome invented for it.
        Assert.True(outcome.Dispatch.GatedOut);
        Assert.False(outcome.Dispatch.SemanticHandlerRan);
        Assert.False(outcome.Dispatch.ColumnExpressionHandlerRan);
        Assert.Equal(0L, outcome.ReturnValue);
        Assert.Equal(WireItemChangeResult.Default, outcome.ItemChangeResult);

        // NO COLUMN-EXPRESSION EVALUATION. This is the coupling itself, measured on the participant the
        // chain would have called: `ondoitemchanged` is what invokes the column-expression service's changed
        // handler, and the gate stopped the chain before it got there.
        FakeEventChain chain = Assert.IsType<FakeEventChain>(fixture.Chains.Last);

        Assert.Empty(chain.Services.ColumnExp.ItemChangedCalls);

        // AND NO TRACE PAYLOAD. `oncolumnexptrace` [se_cst_dw.sru:L32] is the diagnostic the engine raises
        // while calculating, so an evaluation that did not happen has nothing to trace - and the stream
        // carries no such message either as an outcome or as an inverted question.
        Assert.DoesNotContain(
            EventId.Oncolumnexptrace,
            driver.Received
                .Where(static response =>
                    response.PayloadCase == EventChainResponse.PayloadOneofCase.Result)
                .Select(static response => response.Result.EventId));
        Assert.DoesNotContain(
            EventId.Oncolumnexptrace,
            driver.Answered.Select(static question => question.EventId));

        await driver.CompleteAsync();
    }

    [Fact]
    public async Task WithItemChangeEnabledTheChainRunsTheColumnExpressionEvaluationTheGateWasSuppressing()
    {
        DataWindowContractClient client = new(fixture.Host.CreateAuthenticatedGrpcChannel());

        string session = await OpenAsync(client);

        // Disable, then enable - the same session, so the observation is of the BIT changing rather than of
        // two differently constructed fixtures.
        _ = await client.DisableEventAsync(
            new DisableEventRequest
            {
                SessionId = session,
                EventMask = DomainEventGate.EID_ITEMCHANGE,
            },
            cancellationToken: TestContext.Current.CancellationToken);
        EnableEventResponse reopened = await client.EnableEventAsync(
            new EnableEventRequest
            {
                SessionId = session,
                EventMask = DomainEventGate.EID_ITEMCHANGE,
            },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0L, reopened.Gate.Mask);

        await using EventChainDriver driver = EventChainDriver.Open(
            client,
            session,
            TestContext.Current.CancellationToken);

        await driver.NotifyAsync(
            DataWindowEventNotifications.Build(EventId.Ondwnitemchange, "gated-on"),
            OrderingDiscipline.Synchronous,
            TestContext.Current.CancellationToken);

        EventResult outcome = await driver.ReadOutcomeAsync(
            EventId.Ondwnitemchange,
            TestContext.Current.CancellationToken);

        // THE GATE IS OPEN, so the chain ran to depth.
        Assert.False(outcome.Dispatch.GatedOut);
        Assert.True(outcome.Dispatch.SemanticHandlerRan);

        // AND THE COLUMN-EXPRESSION EVALUATION HAPPENED. Exactly one call, for the column the notification
        // named - which is the other half of the coupling and the reason the disabled case above is evidence
        // of anything at all. Without this row, "no calls when disabled" would also be satisfied by a chain
        // that never calls the participant under any circumstances.
        FakeEventChain chain = Assert.IsType<FakeEventChain>(fixture.Chains.Last);

        (long Row, IDataWindowObject Dwo) evaluated =
            Assert.Single(chain.Services.ColumnExp.ItemChangedCalls);

        Assert.Equal(1L, evaluated.Row);
        Assert.Equal(DataWindowEventNotifications.Column, evaluated.Dwo.Name);

        // The forced 2 of the default arm, again - the same legacy behaviour, now reached because the gate
        // let the chain through [se_cst_dw.sru:L250].
        Assert.Equal(WireItemChangeResult.RestoreAndRejectText, outcome.ItemChangeResult);

        await driver.CompleteAsync();
    }

    [Fact]
    public void TheDisableEnableReturnTypeAsymmetryIsPreservedInTheDomainAndNormalisedOnTheWire()
    {
        // THE ORACLE'S TWO MUTATORS GENUINELY DIFFER IN RETURN TYPE:
        //     public function long    of_disableevent (readonly long evt)   [se_cst_dw.sru:L110]
        //     public function integer of_enableevent  (readonly long evt)   [se_cst_dw.sru:L111]
        // There is no reason for it in the source and no behaviour rests on it - it is an inconsistency the
        // original author left behind. C-B forbids CORRECTING legacy behaviour, so the port keeps it: the
        // ported gate's DisableEvent returns long and its EnableEvent returns int.
        System.Reflection.MethodInfo disable = Assert.Single(
            typeof(DomainEventGate).GetMethods(
                System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Static
                    | System.Reflection.BindingFlags.DeclaredOnly),
            method => string.Equals(method.Name, "DisableEvent", StringComparison.Ordinal));
        System.Reflection.MethodInfo enable = Assert.Single(
            typeof(DomainEventGate).GetMethods(
                System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Static
                    | System.Reflection.BindingFlags.DeclaredOnly),
            method => string.Equals(method.Name, "EnableEvent", StringComparison.Ordinal));

        Assert.Equal(typeof(long), disable.ReturnType);
        Assert.Equal(typeof(int), enable.ReturnType);
        Assert.NotEqual(disable.ReturnType, enable.ReturnType);

        // THE PROTO NORMALISES THE PAIR, AND THAT IS THE RIGHT CALL RATHER THAN A LOSS. Both responses carry
        // the same `common.v1.RetCode.Value`, because the asymmetry is a PowerScript declaration detail with
        // no observable consequence - the two widths carry the same value range in practice - and
        // reproducing it on the wire would export an accident as a contract that could never be tidied. The
        // domain keeps the asymmetry, asserted above; the wire keeps the meaning. Both response types are
        // otherwise field-for-field identical, which is what makes the normalisation legible.
        Assert.Equal(
            [.. DisableEventResponse.Descriptor.Fields
                .InFieldNumberOrder()
                .Select(static field => field.Name)],
            [.. EnableEventResponse.Descriptor.Fields
                .InFieldNumberOrder()
                .Select(static field => field.Name)]);

        Assert.Equal(
            DisableEventResponse.Descriptor.FindFieldByName("ret_code").FieldType,
            EnableEventResponse.Descriptor.FindFieldByName("ret_code").FieldType);
    }

    private static async Task<string> OpenAsync(DataWindowContractClient client) =>
        (await client.OpenValidationSessionAsync(
            new OpenValidationSessionRequest
            {
                DatawindowHandle = DataWindowCatalogue.SqliteFixtureName,
            },
            cancellationToken: TestContext.Current.CancellationToken)).SessionId;
}
