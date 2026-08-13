// =====================================================================================================
//  ColumnExpressionGrpcServiceTests - C-04 DRIVEN OVER THE WIRE
//  ---------------------------------------------------------------------------------------------------
//  SUBJECT   PowerFramework.DataServices.Grpc.ColumnExpressionService, reached THROUGH A REAL
//            GrpcChannel built on the TestServer handler published by DataServicesTestHostFactory. Every
//            assertion below therefore travels the deployed pipeline: the authorization policy applied at
//            MapGrpcService, protobuf serialization in both directions, and genuine HTTP/2 duplex
//            streaming for both inverted channels.
//
//  ORACLE    ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_columnexp.sru  (2,435 lines)
//            ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc.sru            (of_setenabled)
//            ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru              (:L14, the macro event)
//            docs/n_cst_dwsvc_columnexp.md                     (the authoritative expansion spec)
//            ws_objects/pfw.tests.pbl.src/w_test_dwsvc_columnexp.srw     (the behavioural scenarios)
//            ws_objects/pfw.utility.container.pbl.src/n_vector.sru       (the calculation stack)
//            ws_objects/pfw.shared.pbl.src/retcode.sru                   (the return-code algebra)
//            The legacy tree is READ-ONLY (constraint C-C): it is the behavioural oracle and never an
//            edit target, and every behavioural assertion here carries its `ws_objects/**` or `docs/**`
//            locator because nothing else in the repository can adjudicate it.
//
//  ============ WHY THIS SUITE EXISTS ALONGSIDE ColumnExpressionServiceContractTests ================
//  That suite invokes the service object DIRECTLY, handing it a hand-built ServerCallContext and a fake
//  host factory. It is the right shape for characterising the session facade and the wire PROJECTION,
//  and it is deliberately not duplicated here.
//
//  THREE PROPERTIES ARE ONLY OBSERVABLE THROUGH A REAL CHANNEL, AND THIS SUITE IS WHERE THEY LIVE:
//    1. AUTHORIZATION (constraint C-G). `Program.cs` maps this service with
//       `RequireAuthorization(CallerAuthorization.ColumnExpressionPolicyName)`. A direct invocation
//       never reaches the authorization middleware at all, so the refusal an unauthenticated caller
//       receives cannot be asserted without a pipeline. The legacy opened NO listening socket, so every
//       one of these surfaces was brought into existence by the decomposition itself - which is why
//       "no new attack surface" has to mean "every new surface authenticated from the outset".
//    2. REAL DUPLEX STREAMING. `InvokeMethodChannel` is INVERTED - the server asks and the client
//       answers - and a direct invocation supplies both halves from the same thread, so the property
//       that actually matters (a calculation in one call blocking on an answer that arrives on another)
//       is asserted here and only here.
//    3. SERIALIZATION FIDELITY. The `$` versus `$$` distinction is the one the requirements say "does
//       not survive naive serialization" (AAP 0.1.3 G4(b)). A payload that carried only the expanded
//       string would pass a direct-invocation value check and fail here, because here the payload is
//       genuinely encoded, transmitted and decoded before it is inspected.
//
//  ============================== WHAT IS ASSERTED, PHASE BY PHASE =================================
//    1. The service derives from the generated base, declares no protocol definition of its own, and
//       rejects an unauthenticated call on EVERY method while succeeding under the test scheme.
//       `#Enabled`'s veto maps to `RetCode.FAILED` (-1) and NEVER to `PREVENT` (+1)
//       [n_cst_dwsvc.sru:L89-L95], pinned through `Predicates.IsSucceeded`/`IsFailed`.
//    2. The full method surface - which is much larger than the nine documented methods
//       [docs/n_cst_dwsvc_columnexp.md:L166 closes by saying so], so the contract is taken from the
//       source's prototype block [:L124-L209].
//    3. The five expansion modes, PER REFERENCE and never per expression, with all three payload
//       components and the seven-arm typed union.
//    4. INVERTED STREAM 1 - macro invocation, one-based arguments, and no fabricated answer.
//    5. INVERTED STREAM 2 - the trace, its `>`-joined stack and its `"(null)"` sentinel.
//    6. THE ONE HARD LIMIT - a cross-session foreign reference is BLOCKED with a defined error and is
//       NEVER resolved to a value (AAP 0.6.2.3, risk R2).
//
//  NO USER RULE GOVERNS THIS FILE. `review_rules` answers "No user rules provided.", so AAP 0.7.2's
//  enterprise baseline applies in their place: nullable enabled with warnings as errors, plain xunit
//  assertions with hand-written doubles and no mocking package, no secret of any kind, and the published
//  contract as the only cross-service coupling (C-A).
//
//  NAMING (AAP 0.4.5.3). No test file is in the repository .editorconfig's CA1707 / IDE1006 suppression
//  list - only named APPLICATION files are - so this file REFERENCES preserved SCREAMING_SNAKE
//  identifiers (`RetCode.FAILED`, `RetCode.E_INVALID_HANDLE`) and DECLARES none.
//
//  DETERMINISM (AAP 0.6.7). No wall-time delay is awaited, no sleep is taken and no ambient clock is
//  read. Every wait is one of three things: a bounded read on a stream the server has already been made
//  to write to, an await on a task this test started, or a barrier that spins on IN-PROCESS state and so
//  ends the instant the server catches up - see SubscribeTraceAsync and WaitForFreeServicerSlotAsync,
//  each bounded by a COUNT rather than a duration so machine speed cannot decide a verdict. The one
//  timeout in the service that a test must reach - the macro invocation backstop - is driven by the
//  injected TimeProvider, so it is reached by ADVANCING the fixture's frozen clock rather than by waiting
//  for it, on a private host so the advance cannot age another case's session. NO NETWORK, NO DOCKER AND
//  NO DATABASE: the channel is an in-process handler, and Persistence is the only service in the system
//  that holds a storage provider (C-E).
//
//  DEPENDENCY PROVENANCE. Every symbol referenced here comes from a file this suite's schema lists, with
//  one exception recorded deliberately rather than left to be discovered: `DataServicesScopes`
//  [Authorization/ScopeAuthorization.cs] supplies the OTHER scope this service publishes, which the
//  authorization case needs in order to be refused for the right reason - a caller holding a genuine but
//  wrong scope, not an invented one. It is a public constant of the very project under test, the same
//  assembly as `Grpc/ColumnExpressionService.cs`, so nothing new is coupled and no project reference
//  changes; spelling the scope as a literal instead would duplicate a production constant and would keep
//  passing, meaninglessly, if that constant were ever renamed. `DataWindowCatalogue` needs no such note:
//  it shares the `PowerFramework.DataServices.Domain` namespace with the two Domain files the schema
//  already lists.
//
//  NO PACKAGE WAS ADDED (AAP 0.5.3). `Grpc.Net.Client` and `Grpc.Core` arrive transitively through the
//  application project's `Grpc.AspNetCore` reference and the test host through
//  `Microsoft.AspNetCore.Mvc.Testing`. This file contains no protocol definition and the test project
//  declares no `<Protobuf>` item, because `shared/PowerFramework.Contracts` compiles every `.proto`
//  exactly once.
// =====================================================================================================

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.DependencyInjection;
using PowerFramework.Contracts.DataServices.V1;
using PowerFramework.DataServices.Authorization;
using PowerFramework.DataServices.Configuration;
using PowerFramework.DataServices.Domain;
using PowerFramework.DataServices.Expressions;
using PowerFramework.DataServices.Grpc;
using PowerFramework.Shared.Kernel;
using Xunit;
using CommonExtensions = PowerFramework.Contracts.Common.V1.CommonV1Extensions;
using GeneratedBase =
    PowerFramework.Contracts.DataServices.V1.ColumnExpressionService.ColumnExpressionServiceBase;
using Svc = PowerFramework.DataServices.Grpc.ColumnExpressionService;
using WireAnyValue = PowerFramework.Contracts.Common.V1.AnyValue;
using WireClient =
    PowerFramework.Contracts.DataServices.V1.ColumnExpressionService.ColumnExpressionServiceClient;
using WireColumnValue = PowerFramework.Contracts.Common.V1.ColumnValue;
using WireDataWindowRow = PowerFramework.Contracts.Common.V1.DataWindowRow;
using WireDate = PowerFramework.Contracts.Common.V1.DateValue;
using WireDateTime = PowerFramework.Contracts.Common.V1.DateTimeValue;
using WireRetCode = PowerFramework.Contracts.Common.V1.RetCode.Types.Value;
using WireService = PowerFramework.Contracts.DataServices.V1.ColumnExpressionService;
using WireTime = PowerFramework.Contracts.Common.V1.TimeValue;

namespace PowerFramework.DataServices.Tests;

// -----------------------------------------------------------------------------------------------------
//  TEST DOUBLES - two of them, each reachable only from this assembly and shipped nowhere
// -----------------------------------------------------------------------------------------------------

/// <summary>
/// Binds every DataWindow name to ONE caller-supplied <see cref="FakeDataWindowHost"/>, so a case can
/// script host properties the deployed catalogue does not carry.
/// </summary>
/// <param name="host">The host every name resolves to.</param>
/// <remarks>
/// <para>
/// WHY THIS EXISTS AT ALL. The trace's <c>"(null)"</c> rendering has TWO triggering conditions
/// [n_cst_dwsvc_columnexp.sru:L758] and one of them is the column declaring <c>NilIsNull</c>. The
/// deployed catalogue's <c>Describe</c> does not recognise that attribute for any column, so the
/// condition is UNREACHABLE against it - and asserting only the other condition would leave half of a
/// two-armed sentinel untested. <c>DataServicesTestHostFactory</c> documents
/// <c>AdditionalServiceConfiguration</c> as the seam a suite needing per-host control registers through,
/// and this is that registration.
/// </para>
/// <para>
/// EVERY NAME ANSWERS THE SAME INSTANCE ON PURPOSE. The production factory is retentive per data-object
/// name, and a case using this double opens exactly one session over one name - so answering one instance
/// keeps the double honest about what it is: a scripted host, not a second implementation of the factory's
/// lifetime policy.
/// </para>
/// </remarks>
internal sealed class C04WireHostFactory(FakeDataWindowHost host) : IDataWindowHostFactory
{
    /// <inheritdoc />
    public DataWindowServiceHost? Create(string dataWindowName)
    {
        ArgumentNullException.ThrowIfNull(dataWindowName);

        return host;
    }
}


/// <summary>
/// A DataWindow service whose <c>OnEnable</c> hook VETOES every change - the only way to reach the veto
/// arm of the ported <c>of_setenabled</c>.
/// </summary>
/// <remarks>
/// <para>
/// WHY A DOUBLE IS REQUIRED HERE, AND WHY THAT IS NOT A GAP IN THE WIRE SUITE.
/// <c>ColumnExpressionEngine</c> overrides the hook as <c>protected override long OnEnable(bool) =&gt; 0</c>,
/// which allows every change - faithfully, because <c>n_cst_dwsvc_columnexp</c> attaches no script to
/// that event either. So the deployed engine CANNOT veto, and no request a client can send over the wire
/// will make it. The veto is nevertheless published contract - <c>SetEnabledResponse.ret_code</c> exists
/// precisely because a boolean could not distinguish "refused" from "applied" - so the arm is driven
/// through the very function the engine inherits: <c>DataWindowServiceBase.SetEnabled</c>, which is
/// public and NOT virtual, so this double changes only the hook and none of the sequencing around it.
/// </para>
/// <para>
/// THE HOOK RETURNS THE BARE NUMERAL <c>1</c> AND NOT <c>RetCode.PREVENT</c>, even though the two happen
/// to be the same number. <c>of_setenabled</c> tests for the numeral [<c>n_cst_dwsvc.sru:L90</c>] and
/// translates it into <c>-1</c>; conflating the signal with the constant is exactly the mistake the
/// assertion in this suite exists to catch.
/// </para>
/// </remarks>
internal sealed class C04WireVetoingService : DataWindowServiceBase
{
    /// <summary>How many times the hook was asked.</summary>
    /// <remarks>
    /// Counted because the idempotent early-out at <c>n_cst_dwsvc.sru:L89</c> returns success WITHOUT
    /// raising the hook at all, and that omission is observable behaviour rather than an optimisation.
    /// </remarks>
    internal int Asked { get; private set; }

    /// <inheritdoc />
    protected override long OnEnable(bool enabled)
    {
        Asked++;

        // The veto signal is the numeral 1 [n_cst_dwsvc.sru:L90]. NOT RetCode.PREVENT.
        return 1L;
    }
}

// -----------------------------------------------------------------------------------------------------
//  THE SUITE
// -----------------------------------------------------------------------------------------------------

/// <summary>
/// Contract C-04 exercised end to end over a real <see cref="GrpcChannel"/> on the deployed DataServices
/// pipeline.
/// </summary>
/// <param name="host">
/// The shared in-process host. It is a CLASS fixture rather than one per test because booting the
/// composition root is the expensive part and nothing in this suite mutates host-wide state: every case
/// works inside its own expression session, and the two cases that need bespoke wiring create and
/// dispose their own factory instead.
/// </param>
public sealed class ColumnExpressionGrpcServiceTests(DataServicesTestHostFactory host)
    : IClassFixture<DataServicesTestHostFactory>
{
    /// <summary>
    /// The oracle's own variable names, transcribed rather than anglicised.
    /// </summary>
    /// <remarks>
    /// THE CHINESE SPELLINGS ARE LOAD-BEARING. `docs/n_cst_dwsvc_columnexp.md` is the authoritative
    /// specification and its worked examples are Chinese, so an anglicised rendering would not be
    /// diffable against the oracle - and these strings additionally prove that a non-ASCII identifier
    /// survives protobuf encoding, which is a property of the wire and not of the engine.
    /// 上月读数 is "last month's reading" - the variable that gets mutated from 0 to 1 - and 本月读数 is
    /// "this month's reading", the expression variable valued "5" [docs:L44-L47].
    /// </remarks>
    private const string LastMonth = "上月读数";

    /// <summary>The expression variable valued "5" [docs:L44-L45].</summary>
    private const string ThisMonth = "本月读数";

    /// <summary>The precision variable the macro examples pass as an argument [docs:L115].</summary>
    private const string Precision = "精度";

    /// <summary>The variable HOLDING the function name for the dynamic form [docs:L139].</summary>
    private const string FormatterName = "单价格式化";

    /// <summary>The macro the documented handler implements [docs:L125-L126].</summary>
    private const string FormatPrice = "FormatPrice";

    /// <summary>
    /// The transcribed DataWindow this suite calculates over - <c>n1</c>, <c>n2</c> and <c>n3</c> as
    /// <c>decimal(2)</c> and <c>s1</c> to <c>s3</c> as <c>char(100)</c>
    /// [ws_objects/pfw.tests.pbl.src/dw_test_dwsvc.srd].
    /// </summary>
    private const string Fixture = DataWindowCatalogue.ServiceFixtureName;

    /// <summary>The second catalogue definition, used where a session needs two co-resident DataWindows.</summary>
    private const string Peer = DataWindowCatalogue.SqliteFixtureName;

    /// <summary>
    /// The variable the oracle defines in its SECOND DataWindow and references from its first
    /// [ws_objects/pfw.tests.pbl.src/w_test_dwsvc_columnexp.srw:L141-L144].
    /// </summary>
    private const string Summary = "数据汇总";

    /// <summary>
    /// The column name carried by the synthetic record that proves a trace subscription is live.
    /// </summary>
    /// <remarks>
    /// DELIBERATELY UNSPELLABLE AS A COLUMN. No definition in the catalogue carries this name, so the
    /// pump's enrichment cannot bind it to a real column and no calculation can ever produce it.
    /// </remarks>
    private const string TraceBarrierColumn = "__pfw_wire_trace_barrier__";

    /// <summary>How many yields a synchronisation barrier will spend before declaring the pipeline broken.</summary>
    /// <remarks>
    /// A COUNT AND NOT A DURATION, so the bound cannot make a passing run depend on machine speed. Both
    /// barriers in this suite - the trace subscription and the macro servicer slot - normally clear within
    /// a handful of yields; this ceiling exists only so a genuinely broken pipeline fails with a diagnosis
    /// rather than hanging the run.
    /// </remarks>
    private const int BarrierAttemptCeiling = 200_000;

    /// <summary>
    /// The test's own cancellation token, threaded into every awaited call that accepts one.
    /// </summary>
    /// <remarks>
    /// NOT OPTIONAL HYGIENE HERE: <c>xUnit1051</c> is an ERROR in this repository, because
    /// <c>Directory.Build.props</c> sets <c>TreatWarningsAsErrors</c>. It is also what stops an
    /// abandoned duplex stream from outliving its test - a leaked host makes the run hang, and a hung run
    /// never writes the Cobertura report the per-service 80% gate is measured from (constraint C-H).
    /// </remarks>
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>One open expression session and the handles it minted.</summary>
    /// <param name="SessionId">The session identifier.</param>
    /// <param name="Handles">
    /// The session-scoped handles, in the order requested. Spelled <c>"&lt;sessionId&gt;/&lt;ordinal&gt;"</c>
    /// and MINTED BY THE SERVICE - never adopted from the request, which is what makes the co-residency
    /// rule enforceable.
    /// </param>
    private sealed record Session(string SessionId, IReadOnlyList<string> Handles)
    {
        /// <summary>The first handle, for the common single-DataWindow case.</summary>
        internal string Handle => Handles[0];
    }

    /// <summary>
    /// Every rpc the generated service declares. The authorization theory is driven from this list and a
    /// census assertion proves the list is the whole surface.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A <see cref="TheoryData{T}"/> OF STRINGS RATHER THAN OF DELEGATES, so every case is serializable
    /// and appears as its own named test result. The dispatcher below turns a name into a call; a
    /// delegate-valued theory would collapse the whole surface into one opaque row.
    /// </para>
    /// <para>
    /// THE LIST IS NOT TRUSTED. <see cref="TheAuthorizationTheoryCoversEveryPublishedMethod"/> compares it
    /// against the generated service descriptor, so a method added to the contract and not to this list
    /// fails that test rather than silently going unauthenticated-untested.
    /// </para>
    /// </remarks>
    public static TheoryData<string> EveryMethod => [.. PublishedMethods];

    /// <summary>The rpc names, single-sourced so the theory and the census read the same list.</summary>
    private static readonly string[] PublishedMethods =
    [
        "OpenExpressionSession",
        "CloseExpressionSession",
        "AddExpression",
        "SetExpression",
        "GetExpression",
        "RemoveExpression",
        "RemoveAllExpressions",
        "AddVariable",
        "SetVariable",
        "AddVariableExpression",
        "SetVariableExpression",
        "GetVariableExpression",
        "AddForeignVariable",
        "SetRelativeColumns",
        "SetExpressionFlag",
        "Calc",
        "CalcAll",
        "CalcEmpty",
        "CalcItem",
        "SetEnabled",
        "SetTrace",
        "GetServiceState",
        "GetExpressionState",
        "EventStream",
        "InvokeMethodChannel",
        "TraceChannel",
    ];

    // -------------------------------------------------------------------------------------------------
    //  HELPERS - every one of them goes over the wire
    // -------------------------------------------------------------------------------------------------

    /// <summary>A C-04 client over an authenticated channel on the shared host.</summary>
    /// <returns>The generated client.</returns>
    private WireClient Client() => host.CreateColumnExpressionClient();

    /// <summary>Opens an expression session over the named DataWindows.</summary>
    /// <param name="client">The client.</param>
    /// <param name="names">
    /// The data-object names to make CO-RESIDENT. Defaults to the single service fixture; a case that
    /// asserts a foreign reference passes two, because co-residency in ONE session is the whole test that
    /// a cross-DataWindow reference has to pass.
    /// </param>
    /// <returns>The session and its minted handles.</returns>
    private static async Task<Session> OpenAsync(WireClient client, params string[] names)
    {
        OpenExpressionSessionRequest request = new();
        request.DatawindowHandles.AddRange(names.Length == 0 ? [Fixture] : names);

        OpenExpressionSessionResponse response =
            await client.OpenExpressionSessionAsync(request, cancellationToken: Ct);

        Assert.Equal(WireRetCode.Ok, response.RetCode);
        Assert.Equal(names.Length == 0 ? 1 : names.Length, response.DatawindowHandles.Count);

        // The handle is session-qualified BY CONSTRUCTION, which is what makes a handle carried in from
        // another session miss this session's registry and be BLOCKED rather than silently resolve.
        foreach (string handle in response.DatawindowHandles)
        {
            Assert.StartsWith(response.SessionId + "/", handle, StringComparison.Ordinal);
        }

        return new Session(response.SessionId, [.. response.DatawindowHandles]);
    }

    /// <summary>
    /// Enables the engine - step 2 of the documented call order, and the step that is easiest to miss.
    /// </summary>
    /// <param name="client">The client.</param>
    /// <param name="session">The session.</param>
    /// <param name="handle">The DataWindow handle.</param>
    /// <returns>A task representing the call.</returns>
    /// <remarks>
    /// THE ENGINE IS CREATED INERT AND DOES NOT AUTO-ENABLE, because <c>#Enabled</c> is
    /// <c>protectedwrite boolean</c> with no initializer [n_cst_dwsvc.sru:L38] - a PowerBuilder service is
    /// off until <c>of_setenabled</c> turns it on. Auto-enabling on open would be a behaviour change
    /// (C-B), which is why this is an explicit step rather than something the open does.
    /// </remarks>
    private static async Task EnableAsync(WireClient client, string session, string handle)
    {
        SetEnabledResponse response = await client.SetEnabledAsync(
            new SetEnabledRequest { SessionId = session, DatawindowHandle = handle, Enabled = true },
            cancellationToken: Ct);

        Assert.Equal(WireRetCode.Ok, response.RetCode);
        Assert.True(response.Changed);
        Assert.True(response.Enabled);
    }

    /// <summary>
    /// Puts rows into the DataWindow this service's sessions bind, SERVER-SIDE, because no published
    /// operation does.
    /// </summary>
    /// <param name="rows">One entry per row: the columns to populate, by name.</param>
    /// <remarks>
    /// <para>
    /// ⚠ THE ABSENCE OF A PUBLISHED ROW LOADER IS THE FROZEN CONTRACT'S SHAPE, NOT A GAP IN THIS SUITE.
    /// AAP 0.4.3's C-04 inventory carries no row-loading member, C-03 publishes none either, and C-03's
    /// <c>Retrieve</c> streams its rows TO THE CALLER rather than into a service-side model - in the oracle
    /// the DataWindow is a control owned by the APPLICATION and filled by the application's own retrieval,
    /// so the population half sits on the presentation side of the split. Every calculation case below
    /// still has to evaluate over rows, so the rows are placed where the application would have placed
    /// them: directly on the host, through the container's own retentive factory, which is the same
    /// technique <c>EventChainDeadlockTests</c> uses.
    /// </para>
    /// <para>
    /// THE ROWS ARE CLEARED FIRST, and that is load bearing rather than tidy. The production factory
    /// retains ONE host per data-object name and this suite shares one web host across its whole class, so
    /// without the reset each case would calculate over its predecessors' rows and the one-based ordinals
    /// every assertion is written in terms of would drift.
    /// </para>
    /// </remarks>
    private void SeedRows(DataServicesTestHostFactory? target, params (string Column, double Value)[][] rows)
    {
        DataWindowServiceHost seeded =
            (target ?? host).Services.GetRequiredService<IDataWindowHostFactory>().Create(Fixture)
            ?? throw new InvalidOperationException(
                "The container's DataWindow host factory declined the service fixture name, so no row "
                    + "could be seeded. The catalogue is the authority for that name.");

        // Backwards, because deleting row N renumbers everything above it.
        for (long existing = seeded.RowCount(); existing >= 1L; existing--)
        {
            _ = seeded.DeleteRow(existing);
        }

        foreach ((string Column, double Value)[] cells in rows)
        {
            // InsertRow(0) is the legacy's own "at the end" spelling and answers the new ordinal.
            long ordinal = seeded.InsertRow(0L);

            Assert.True(ordinal > 0L, "the host declined to create a row to calculate over.");

            foreach ((string column, double value) in cells)
            {
                Assert.Equal(1, seeded.SetItem(ordinal, column, (decimal)value));
            }
        }
    }

    /// <summary>Opens and enables a session over a DataWindow carrying one row.</summary>
    /// <param name="client">The client.</param>
    /// <returns>The ready session.</returns>
    private async Task<Session> ReadySessionAsync(
        WireClient client,
        DataServicesTestHostFactory? target = null)
    {
        SeedRows(target, [("n1", 10d), ("n2", 5d), ("n3", 1d)]);

        Session session = await OpenAsync(client);
        await EnableAsync(client, session.SessionId, session.Handle);

        return session;
    }

    /// <summary>
    /// The call metadata that identifies which session and DataWindow an <c>InvokeMethodChannel</c>
    /// serves.
    /// </summary>
    /// <param name="session">The session identifier.</param>
    /// <param name="handle">The session-scoped handle.</param>
    /// <returns>The headers.</returns>
    /// <remarks>
    /// METADATA AND NOT THE PAYLOAD, because the client-to-server message on that channel is
    /// <c>InvokeMethodResponse</c> - an invocation identifier, a result, an unhandled flag and an error,
    /// and NOTHING that names a session or a handle. The header names are published by the application on
    /// <see cref="ColumnExpressionChannelHeaders"/>, so this helper does not restate them.
    /// </remarks>
    private static Metadata MacroHeaders(string session, string handle) =>
    [
        new Metadata.Entry(ColumnExpressionChannelHeaders.SessionId, session),
        new Metadata.Entry(ColumnExpressionChannelHeaders.DataWindowHandle, handle),
    ];

    /// <summary>Adds one expression to a column and asserts the add succeeded.</summary>
    /// <param name="client">The client.</param>
    /// <param name="session">The session.</param>
    /// <param name="handle">The handle.</param>
    /// <param name="column">The column to bind.</param>
    /// <param name="exp">The expression AS AUTHORED, sigils intact - never pre-expanded.</param>
    /// <returns>The ONE-BASED index the add returned.</returns>
    /// <remarks>
    /// <c>of_addexp</c> returns THE NEW EXPRESSION'S INDEX rather than a return code, and its return type
    /// is <c>integer</c> and not <c>long</c> [:L172] - which is why the response carries an index field
    /// distinct from <c>ret_code</c> and why this helper answers the index.
    /// </remarks>
    private static async Task<int> AddExpressionAsync(
        WireClient client,
        string session,
        string handle,
        string column,
        string exp)
    {
        AddExpressionResponse response = await client.AddExpressionAsync(
            new AddExpressionRequest
            {
                SessionId = session,
                DatawindowHandle = handle,
                ColumnName = column,
                Exp = exp,
            },
            cancellationToken: Ct);

        Assert.Equal(WireRetCode.Ok, response.RetCode);
        Assert.True(response.Index > 0, "of_addexp answers a ONE-BASED index [:L172, :L1583].");

        return response.Index;
    }

    /// <summary>Calculates one column of one row and answers the response.</summary>
    /// <param name="client">The client.</param>
    /// <param name="session">The session.</param>
    /// <param name="handle">The handle.</param>
    /// <param name="column">The column whose expression is calculated.</param>
    /// <param name="row">The ONE-BASED row ordinal.</param>
    /// <returns>The response.</returns>
    private static Task<CalcResponse> CalcColumnAsync(
        WireClient client,
        string session,
        string handle,
        string column,
        long row = 1L) =>
        client.CalcAsync(
            new CalcRequest
            {
                SessionId = session,
                DatawindowHandle = handle,
                Row = row,
                Target = new ColumnRef { ColumnName = column },
            },
            cancellationToken: Ct).ResponseAsync;

    /// <summary>The single computed value of a calculation, rendered as the DataWindow renders it.</summary>
    /// <param name="response">The calculation response.</param>
    /// <returns>The decimal text.</returns>
    /// <remarks>
    /// A DECIMAL COLUMN IS REPORTED AS <c>DecimalValue</c> AND NOT AS A DOUBLE, deliberately: common.v1's
    /// rule is that a legacy decimal never travels as a double, so its canonical TEXT form is what
    /// crosses and what a characterization recording compares.
    /// </remarks>
    private static string DecimalTextOf(CalcResponse response)
    {
        CalcResult result = Assert.Single(response.Results);

        Assert.Equal(WireAnyValue.KindOneofCase.DecimalValue, result.Value.KindCase);

        return result.Value.DecimalValue.Value;
    }


    /// <summary>
    /// Issues one named rpc with a DELIBERATELY UNUSABLE session, so the call reaches - or is refused
    /// before it reaches - the handler without depending on any prior state.
    /// </summary>
    /// <param name="client">The client to issue through.</param>
    /// <param name="method">The rpc name, from <see cref="EveryMethod"/>.</param>
    /// <returns>A task that completes when the call has been answered or has faulted.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="method"/> is not a published rpc. Reported rather than skipped, because a silently
    /// ignored name would make the theory pass while testing nothing.
    /// </exception>
    /// <remarks>
    /// <para>
    /// EVERY REQUEST IS MINIMAL AND NAMES NOTHING REAL, on purpose. Authorization runs BEFORE the handler,
    /// so an anonymous caller is refused whatever the body says; an authenticated caller reaches the
    /// handler and is answered by the resolution ladder - <c>E_INVALID_ARGUMENT</c> for a blank identifier
    /// - which is exactly the discrimination the two assertions need. Using a real session would prove
    /// less, not more: a call that succeeded could not distinguish "authorized" from "the state happened
    /// to be right".
    /// </para>
    /// <para>
    /// THE THREE STREAMS ARE DRIVEN TO THEIR FIRST READ. A duplex call is dispatched lazily, so a channel
    /// that is merely opened has not yet been authorized; the read is what forces the round trip. The
    /// macro channel is opened WITHOUT its identity headers here, because this theory is about
    /// authorization and its own header requirement is asserted separately.
    /// </para>
    /// </remarks>
    private static async Task IssueAsync(WireClient client, string method)
    {
        switch (method)
        {
            case "OpenExpressionSession":
                _ = await client.OpenExpressionSessionAsync(
                    new OpenExpressionSessionRequest(),
                    cancellationToken: Ct);
                break;
            case "CloseExpressionSession":
                _ = await client.CloseExpressionSessionAsync(
                    new CloseExpressionSessionRequest(),
                    cancellationToken: Ct);
                break;
            case "AddExpression":
                _ = await client.AddExpressionAsync(new AddExpressionRequest(), cancellationToken: Ct);
                break;
            case "SetExpression":
                _ = await client.SetExpressionAsync(new SetExpressionRequest(), cancellationToken: Ct);
                break;
            case "GetExpression":
                _ = await client.GetExpressionAsync(new GetExpressionRequest(), cancellationToken: Ct);
                break;
            case "RemoveExpression":
                _ = await client.RemoveExpressionAsync(
                    new RemoveExpressionRequest(),
                    cancellationToken: Ct);
                break;
            case "RemoveAllExpressions":
                _ = await client.RemoveAllExpressionsAsync(
                    new RemoveAllExpressionsRequest(),
                    cancellationToken: Ct);
                break;
            case "AddVariable":
                _ = await client.AddVariableAsync(new AddVariableRequest(), cancellationToken: Ct);
                break;
            case "SetVariable":
                _ = await client.SetVariableAsync(new SetVariableRequest(), cancellationToken: Ct);
                break;
            case "AddVariableExpression":
                _ = await client.AddVariableExpressionAsync(
                    new AddVariableExpressionRequest(),
                    cancellationToken: Ct);
                break;
            case "SetVariableExpression":
                _ = await client.SetVariableExpressionAsync(
                    new SetVariableExpressionRequest(),
                    cancellationToken: Ct);
                break;
            case "GetVariableExpression":
                _ = await client.GetVariableExpressionAsync(
                    new GetVariableExpressionRequest(),
                    cancellationToken: Ct);
                break;
            case "AddForeignVariable":
                _ = await client.AddForeignVariableAsync(
                    new AddForeignVariableRequest(),
                    cancellationToken: Ct);
                break;
            case "SetRelativeColumns":
                _ = await client.SetRelativeColumnsAsync(
                    new SetRelativeColumnsRequest(),
                    cancellationToken: Ct);
                break;
            case "SetExpressionFlag":
                _ = await client.SetExpressionFlagAsync(
                    new SetExpressionFlagRequest(),
                    cancellationToken: Ct);
                break;
            case "Calc":
                _ = await client.CalcAsync(new CalcRequest(), cancellationToken: Ct);
                break;
            case "CalcAll":
                _ = await client.CalcAllAsync(new CalcAllRequest(), cancellationToken: Ct);
                break;
            case "CalcEmpty":
                _ = await client.CalcEmptyAsync(new CalcEmptyRequest(), cancellationToken: Ct);
                break;
            case "CalcItem":
                _ = await client.CalcItemAsync(new CalcItemRequest(), cancellationToken: Ct);
                break;
            case "SetEnabled":
                _ = await client.SetEnabledAsync(new SetEnabledRequest(), cancellationToken: Ct);
                break;
            case "SetTrace":
                _ = await client.SetTraceAsync(new SetTraceRequest(), cancellationToken: Ct);
                break;
            case "GetServiceState":
                _ = await client.GetServiceStateAsync(
                    new GetServiceStateRequest(),
                    cancellationToken: Ct);
                break;
            case "GetExpressionState":
                _ = await client.GetExpressionStateAsync(
                    new GetExpressionStateRequest(),
                    cancellationToken: Ct);
                break;
            case "EventStream":
                using (AsyncServerStreamingCall<EventStreamResponse> events =
                    client.EventStream(new EventStreamRequest(), cancellationToken: Ct))
                {
                    _ = await events.ResponseStream.MoveNext(Ct);
                }

                break;
            case "InvokeMethodChannel":
                using (AsyncDuplexStreamingCall<InvokeMethodResponse, InvokeMethodRequest> macro =
                    client.InvokeMethodChannel(cancellationToken: Ct))
                {
                    _ = await macro.ResponseStream.MoveNext(Ct);
                }

                break;
            case "TraceChannel":
                using (AsyncDuplexStreamingCall<TraceChannelRequest, TraceRecord> trace =
                    client.TraceChannel(cancellationToken: Ct))
                {
                    await trace.RequestStream.WriteAsync(new TraceChannelRequest { Subscribe = true }, Ct);
                    _ = await trace.ResponseStream.MoveNext(Ct);
                }

                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(method),
                    method,
                    "The name is not a published rpc on dataservices.v1.ColumnExpressionService, so the "
                        + "authorization theory has nothing to issue. Add the dispatch arm rather than "
                        + "removing the name.");
        }
    }

    // =================================================================================================
    //  PHASE 1 - THE SERVICE, ITS AUTHORIZATION, AND THE #Enabled VETO
    // =================================================================================================

    /// <summary>
    /// The service IS the generated C-04 server base, and it defines no wire shape of its own.
    /// </summary>
    /// <remarks>
    /// <para>
    /// CONSTRAINT C-A MADE MECHANICAL. Every generated type must come from
    /// <c>shared/PowerFramework.Contracts</c>, which compiles the three protocol definitions exactly once
    /// under one <c>&lt;Protobuf GrpcServices="Both"&gt;</c> item. A second <c>&lt;Protobuf&gt;</c> item in
    /// the application project would generate the same types again and break the build outright, so this
    /// asserts BOTH halves: that the generated base really is the application's base class, and that the
    /// base's assembly is the CONTRACTS assembly rather than the application's own.
    /// </para>
    /// <para>
    /// THE FILESYSTEM HALF IS ASSERTED TOO, because the type test alone would still pass if a protocol
    /// definition were added and merely not referenced. The application project directory must carry no
    /// <c>.proto</c> and its project file no <c>&lt;Protobuf</c> item.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheServiceIsTheGeneratedBaseAndDeclaresNoContractOfItsOwn()
    {
        Assert.Equal(typeof(GeneratedBase), typeof(Svc).BaseType);
        Assert.True(typeof(Svc).IsSealed);

        // The generated base, and every message this suite sends, belongs to the CONTRACTS assembly.
        Assert.NotEqual(typeof(Svc).Assembly, typeof(GeneratedBase).Assembly);
        Assert.Equal(typeof(GeneratedBase).Assembly, typeof(CalcRequest).Assembly);
        Assert.Equal(typeof(GeneratedBase).Assembly, typeof(WireRetCode).Assembly);

        string projectDirectory = DataServicesProjectDirectory();

        Assert.Empty(Directory.GetFiles(projectDirectory, "*.proto", SearchOption.AllDirectories));

        string projectFile = Path.Combine(projectDirectory, "PowerFramework.DataServices.csproj");

        Assert.True(File.Exists(projectFile));
        Assert.DoesNotContain("<Protobuf", File.ReadAllText(projectFile), StringComparison.Ordinal);
    }

    /// <summary>
    /// The authorization theory really does cover the WHOLE published surface.
    /// </summary>
    /// <remarks>
    /// A CENSUS, so the claim "every method" is verified rather than asserted. The generated service
    /// descriptor is the authority: a method added to the contract without a row here fails this test,
    /// and a row naming a method the contract does not have fails it too. Both directions matter -
    /// the first would leave a new surface untested for authentication, which is precisely the failure
    /// constraint C-G exists to prevent.
    /// </remarks>
    [Fact]
    public void TheAuthorizationTheoryCoversEveryPublishedMethod()
    {
        IReadOnlyList<string> declared =
        [
            .. WireService.Descriptor.Methods
                .Select(static method => method.Name)
                .Order(StringComparer.Ordinal),
        ];

        IReadOnlyList<string> covered = [.. PublishedMethods.Order(StringComparer.Ordinal)];

        Assert.Equal(declared, covered);

        // The count is stated so a reader sees the size of the surface without counting the list, and so
        // that a coincidental one-for-one swap cannot pass unnoticed.
        Assert.Equal(26, declared.Count);
    }

    /// <summary>
    /// Every published method REFUSES an unauthenticated caller and is REACHED by an authenticated one.
    /// </summary>
    /// <param name="method">The rpc under test.</param>
    /// <returns>A task representing the assertions.</returns>
    /// <remarks>
    /// <para>
    /// CONSTRAINT C-G, ASSERTED AT THE ONLY PLACE IT IS OBSERVABLE. <c>Program.cs</c> maps this service
    /// with <c>RequireAuthorization(CallerAuthorization.ColumnExpressionPolicyName)</c> and the
    /// application's fallback policy would close it even if that line were absent. The HTTP 401 the
    /// challenge produces surfaces to a gRPC caller as <see cref="StatusCode.Unauthenticated"/>.
    /// </para>
    /// <para>
    /// AN INTERNAL EDGE IS COVERED EXACTLY AS A PUBLIC ONE IS. The legacy opened no listening socket,
    /// registered no route and received no unsolicited request, so "no new attack surface" cannot mean
    /// "no new surface" - it means every newly created surface is authenticated from the outset. C-04 is
    /// reached only by Gateway, and it is authenticated anyway.
    /// </para>
    /// <para>
    /// THE POSITIVE HALF IS THE OTHER HALF OF THE PROOF. An authenticated call must be ANSWERED - by its
    /// own resolution code in band for a unary method, or by its own defined status for a stream - and
    /// never by <see cref="StatusCode.Unauthenticated"/>. Without it, a service that refused everybody
    /// would pass the negative half.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(EveryMethod))]
    public async Task EveryMethodRefusesAnAnonymousCallerAndIsReachedByAnAuthenticatedOne(string method)
    {
        WireClient anonymous = new(host.CreateAnonymousGrpcChannel());

        RpcException refused =
            await Assert.ThrowsAsync<RpcException>(() => IssueAsync(anonymous, method));

        Assert.Equal(StatusCode.Unauthenticated, refused.StatusCode);

        // The same call under the test authentication scheme reaches the handler. A unary method answers
        // its own code in band and does not throw; a stream refuses the blank session it was given with
        // its own defined status. Either way it is NOT an authentication failure.
        try
        {
            await IssueAsync(Client(), method);
        }
        catch (RpcException reached)
        {
            Assert.NotEqual(StatusCode.Unauthenticated, reached.StatusCode);
            Assert.Equal(StatusCode.InvalidArgument, reached.StatusCode);
        }
    }

    /// <summary>
    /// A scope-less credential is refused, so authentication alone is not authorization.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// THE POLICY NAMES THIS CONTRACT'S OWN SCOPE, <c>dataservices.columnexpression</c>, and C-03 names a
    /// different one - so a caller holding only the DataWindow scope must not reach the expression engine.
    /// A token that authenticates but carries no scope is the closest thing to a confused-deputy request
    /// this boundary can receive, and it answers <see cref="StatusCode.PermissionDenied"/> rather than
    /// succeeding.
    /// </remarks>
    [Fact]
    public async Task ACredentialWithoutTheColumnExpressionScopeIsRefused()
    {
        // The fixture publishes a scoped HTTP client but no scoped CHANNEL, so the channel is built here
        // from that client. DisposeHttpClient stays false because the fixture already owns and disposes
        // every client it created, and two owners for one object is a defect waiting for a teardown race.
        HttpClient scopedClient = host.CreateScopedClient([DataServicesScopes.DataWindow]);

        using GrpcChannel channel = GrpcChannel.ForAddress(
            scopedClient.BaseAddress!,
            new GrpcChannelOptions { HttpClient = scopedClient, DisposeHttpClient = false });

        WireClient scoped = new(channel);

        RpcException refused = await Assert.ThrowsAsync<RpcException>(() =>
            scoped.GetServiceStateAsync(
                new GetServiceStateRequest(),
                cancellationToken: Ct).ResponseAsync);

        Assert.Equal(StatusCode.PermissionDenied, refused.StatusCode);
    }


    /// <summary>
    /// <c>#Enabled</c> has THREE outcomes and only two of them are OK, so <c>changed</c> carries what the
    /// return code cannot.
    /// </summary>
    /// <returns>A task representing the assertions.</returns>
    /// <remarks>
    /// <para>
    /// THE IDEMPOTENT EARLY-OUT IS OBSERVABLE AND MUST NOT BE SIMPLIFIED AWAY.
    /// <c>if #Enabled = bEnabled then return RetCode.OK</c> [n_cst_dwsvc.sru:L89] returns success WITHOUT
    /// RAISING THE HOOK AT ALL, so "already in that state" and "changed" are both OK and only
    /// <c>changed</c> separates them.
    /// </para>
    /// <para>
    /// AND THE ENGINE IS INERT UNTIL ASKED. A caller that skips this step sees <c>ret_code = FAILED</c>
    /// from every calculation - the oracle's own answer at four separate call sites
    /// [:L1158, :L1201, :L1976, :L2087] - which Gateway projects as 502. That reads like an upstream
    /// outage and is not one, so the flag is published by <c>GetServiceState</c> precisely so a caller can
    /// tell "disabled" from "nothing to do".
    /// </para>
    /// </remarks>
    [Fact]
    public async Task SetEnabledIsIdempotentAndTheInertDefaultIsObservableOverTheWire()
    {
        WireClient client = Client();
        Session session = await OpenAsync(client);

        GetServiceStateResponse inert = await client.GetServiceStateAsync(
            new GetServiceStateRequest
            {
                SessionId = session.SessionId,
                DatawindowHandle = session.Handle,
            },
            cancellationToken: Ct);

        Assert.Equal(WireRetCode.Ok, inert.RetCode);
        Assert.False(inert.Enabled);

        // The preserved default of DataServices:ColumnExpression:Trace is FALSE, reproducing
        // `privatewrite boolean #Trace` having no initializer at source [:L98].
        Assert.False(inert.Trace);

        SetEnabledResponse applied = await client.SetEnabledAsync(
            new SetEnabledRequest
            {
                SessionId = session.SessionId,
                DatawindowHandle = session.Handle,
                Enabled = true,
            },
            cancellationToken: Ct);

        Assert.Equal(WireRetCode.Ok, applied.RetCode);
        Assert.True(applied.Changed);
        Assert.True(applied.Enabled);

        SetEnabledResponse unchanged = await client.SetEnabledAsync(
            new SetEnabledRequest
            {
                SessionId = session.SessionId,
                DatawindowHandle = session.Handle,
                Enabled = true,
            },
            cancellationToken: Ct);

        // [:L89] - success, nothing moved, and the hook was never raised.
        Assert.Equal(WireRetCode.Ok, unchanged.RetCode);
        Assert.False(unchanged.Changed);
        Assert.True(unchanged.Enabled);

        // #Enabled is observable afterwards over the wire, which is the whole reason GetServiceState
        // publishes it.
        GetServiceStateResponse after = await client.GetServiceStateAsync(
            new GetServiceStateRequest
            {
                SessionId = session.SessionId,
                DatawindowHandle = session.Handle,
            },
            cancellationToken: Ct);

        Assert.True(after.Enabled);

        // And with the engine switched back off, a calculation answers the oracle's own FAILED.
        _ = await client.SetEnabledAsync(
            new SetEnabledRequest
            {
                SessionId = session.SessionId,
                DatawindowHandle = session.Handle,
                Enabled = false,
            },
            cancellationToken: Ct);

        CalcAllResponse whileDisabled = await client.CalcAllAsync(
            new CalcAllRequest
            {
                SessionId = session.SessionId,
                DatawindowHandle = session.Handle,
            },
            cancellationToken: Ct);

        Assert.Equal(WireRetCode.Failed, whileDisabled.RetCode);
    }

    /// <summary>
    /// *** A VETO MAPS TO <c>RetCode.FAILED</c> (-1) AND NOT TO <c>PREVENT</c> (+1). ***
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE ORACLE IS <c>if Event OnEnable(bEnabled) = 1 then return RetCode.FAILED</c>
    /// [n_cst_dwsvc.sru:L90]: THE HANDLER SIGNALS WITH THE BARE NUMERAL 1 AND THE FUNCTION TRANSLATES THAT
    /// SIGNAL INTO -1. A reasonable author would map a veto onto <c>PREVENT</c> - which is also 1 - and
    /// that would be wrong in a way that FLIPS EVERY CALLER'S BRANCH, because
    /// <c>Predicates.IsSucceeded</c> is <c>&gt;= 0</c>: it reads <c>PREVENT</c> as a SUCCESS while
    /// <c>FAILED</c> is a failure. Both halves are pinned below, in both directions, so the two can never
    /// be silently interchanged.
    /// </para>
    /// <para>
    /// WHY THE ARM IS DRIVEN THROUGH THE PORTED FUNCTION AND NOT THROUGH A REQUEST. The deployed engine
    /// overrides the hook to allow every change - faithfully, since <c>n_cst_dwsvc_columnexp</c> attaches
    /// no script to that event - so NO request a client can send will make it veto. The veto is
    /// nevertheless published contract, and it is reached here through <c>DataWindowServiceBase.SetEnabled</c>
    /// itself: the very function the engine inherits and the gRPC method calls, with only the overridable
    /// hook substituted. See <see cref="C04WireVetoingService"/>.
    /// </para>
    /// <para>
    /// AND THE WIRE KEEPS THEM DISTINCT TOO. <c>WireRetCode.Failed</c> is -1 and <c>WireRetCode.Prevent</c>
    /// is 1 on the enumeration the response carries, so a veto cannot arrive as a prevention even by
    /// accident of projection.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheEnabledVetoIsFailedAndNeverPrevent()
    {
        C04WireVetoingService vetoing = new();

        // [:L89] - the early-out returns OK and does NOT raise the hook, so the veto is unreachable for a
        // no-op change. Asserted first, because it is what makes the count below meaningful.
        Assert.Equal(RetCode.OK, vetoing.SetEnabled(false));
        Assert.Equal(0, vetoing.Asked);
        Assert.False(vetoing.Enabled);

        // [:L90] - the hook signals 1 and the function answers -1.
        long vetoed = vetoing.SetEnabled(true);

        Assert.Equal(1, vetoing.Asked);
        Assert.Equal(RetCode.FAILED, vetoed);
        Assert.Equal(-1L, vetoed);
        Assert.NotEqual(RetCode.PREVENT, vetoed);
        Assert.False(vetoing.Enabled);

        // THE ALGEBRA, PINNED IN BOTH DIRECTIONS. IsSucceeded is `>= 0`, so a prevention reads as a
        // SUCCESS [issucceeded.srf:L11-L13]; IsFailed is `< 0` with CANCELLED explicitly excluded
        // [isfailed.srf:L11-L13]. For a VETOED change, therefore: IsFailed is TRUE, IsPrevented is FALSE,
        // and a caller testing for prevention does not see the veto at all.
        Assert.True(Predicates.IsFailed(vetoed));
        Assert.False(Predicates.IsSucceeded(vetoed));
        Assert.False(Predicates.IsPrevented(vetoed));

        // The mirror image, which is the reason the mapping matters.
        Assert.True(Predicates.IsSucceeded(RetCode.PREVENT));
        Assert.False(Predicates.IsFailed(RetCode.PREVENT));
        Assert.True(Predicates.IsPrevented(RetCode.PREVENT));

        // The wire enumeration keeps the two apart, so the projection cannot conflate them either.
        Assert.Equal(-1, (int)WireRetCode.Failed);
        Assert.Equal(1, (int)WireRetCode.Prevent);
        Assert.NotEqual(WireRetCode.Prevent, WireRetCode.Failed);
    }

    /// <summary>
    /// The DataServices project directory, located by walking up for the repository markers.
    /// </summary>
    /// <returns>The absolute path.</returns>
    /// <remarks>
    /// AN UPWARD SEARCH RATHER THAN A FIXED NUMBER OF HOPS, because the distance between a test assembly
    /// and the repository root is a function of the configuration and the target framework in the output
    /// path. The walk starts at the embedded root when the build supplied one - which is what makes this
    /// work when the test output sits outside the checkout - and still verifies the markers, so a stale
    /// value degrades to the walk rather than misreading another tree. Read-only: nothing is created.
    /// </remarks>
    private static string DataServicesProjectDirectory()
    {
        DirectoryInfo? candidate = new(TestRepositoryRoot.SearchStart);

        while (candidate is not null)
        {
            string project = Path.Combine(
                candidate.FullName,
                "services",
                "dataservices-service",
                "PowerFramework.DataServices");

            if (Directory.Exists(project)
                && File.Exists(Path.Combine(candidate.FullName, "PowerFramework.slnx"))
                && File.Exists(Path.Combine(candidate.FullName, "Directory.Packages.props")))
            {
                return project;
            }

            candidate = candidate.Parent;
        }

        Assert.Fail(
            "No directory from '"
                + TestRepositoryRoot.SearchStart
                + "' up to the filesystem root holds services/dataservices-service/"
                + "PowerFramework.DataServices together with both repository markers, so the assertion "
                + "that the application declares no protocol definition of its own could not be made. "
                + "Both probes are read-only; nothing was created.");

        return string.Empty;
    }


    // =================================================================================================
    //  PHASE 2 - THE FULL METHOD SURFACE, WHICH IS LARGER THAN THE DOCUMENTED NINE
    //  -----------------------------------------------------------------------------------------------
    //  The legacy documentation describes nine methods and CLOSES BY SAYING IT IS INCOMPLETE
    //  [docs/n_cst_dwsvc_columnexp.md:L166]. The prototype block at [:L124-L209] declares far more, and
    //  the wire surface is taken from there - including two groups an earlier reading omitted: the
    //  CACHEABLE flag [:L196-L197] and the CALCULATE-EMPTY arities [:L199-L200].
    // =================================================================================================

    /// <summary>
    /// Expression add, set in FOUR arities, get in TWO, remove in TWO, and remove-all.
    /// </summary>
    /// <returns>A task representing the assertions.</returns>
    /// <remarks>
    /// <para>
    /// ARITY IS COLLAPSED ONLY WHERE AN OPTIONAL FIELD EXPRESSES IT EXACTLY. The four <c>of_setexp</c>
    /// overloads [:L174-L177] are by index or by name, each with and without an explicit recalculate flag,
    /// so on the wire they are one message with an explicit-presence boolean: ABSENT MEANS THE CALLER USED
    /// THE SHORTER OVERLOAD, which is the same information rather than less.
    /// </para>
    /// <para>
    /// ADDRESSING MODE IS NOT COLLAPSED, because an index and a name are not the same request: an index
    /// binds to a position and a name goes through a lookup that CAN FAIL [:L141
    /// <c>_of_findexpindex</c>]. Both forms are therefore exercised for every operation that publishes
    /// both.
    /// </para>
    /// <para>
    /// AND THE GETTER RETURNS THE UNEXPANDED TEXT, so set-then-get is lossless. A getter that answered the
    /// rewritten text would make the round trip lossy in exactly the way section 10 of the contract warns
    /// about.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ExpressionAddSetGetRemoveAndRemoveAllCoverEveryLegacyArity()
    {
        WireClient client = Client();
        Session session = await ReadySessionAsync(client, host);

        int index = await AddExpressionAsync(client, session.SessionId, session.Handle, "n3", "n1 + n2");

        Assert.Equal(1, index);

        // THE FOUR of_setexp ARITIES [:L174-L177].
        foreach ((ColumnRef target, bool? recalc, string exp) in new (ColumnRef, bool?, string)[]
        {
            (new ColumnRef { Index = index }, null, "n1 + 1"),
            (new ColumnRef { Index = index }, true, "n1 + 2"),
            (new ColumnRef { ColumnName = "n3" }, null, "n1 + 3"),
            (new ColumnRef { ColumnName = "n3" }, false, "n1 + 4"),
        })
        {
            SetExpressionRequest request = new()
            {
                SessionId = session.SessionId,
                DatawindowHandle = session.Handle,
                Target = target,
                Exp = exp,
            };

            if (recalc is { } flag)
            {
                request.Recalc = flag;
            }

            SetExpressionResponse set = await client.SetExpressionAsync(request, cancellationToken: Ct);

            Assert.Equal(WireRetCode.Ok, set.RetCode);
            Assert.Equal(recalc is not null, request.HasRecalc);

            // THE TWO of_getexp ARITIES [:L127, :L130] - both answer the text that was SET, unexpanded.
            foreach (ColumnRef addressing in new[]
            {
                new ColumnRef { Index = index },
                new ColumnRef { ColumnName = "n3" },
            })
            {
                GetExpressionResponse got = await client.GetExpressionAsync(
                    new GetExpressionRequest
                    {
                        SessionId = session.SessionId,
                        DatawindowHandle = session.Handle,
                        Target = addressing,
                    },
                    cancellationToken: Ct);

                Assert.Equal(WireRetCode.Ok, got.RetCode);
                Assert.Equal(exp, got.Exp);
                Assert.Equal(exp, got.Binding.UnexpandedExp);
            }
        }

        // THE TWO of_remove ARITIES [:L131-L132], by name then by index.
        RemoveExpressionResponse byName = await client.RemoveExpressionAsync(
            new RemoveExpressionRequest
            {
                SessionId = session.SessionId,
                DatawindowHandle = session.Handle,
                Target = new ColumnRef { ColumnName = "n3" },
            },
            cancellationToken: Ct);

        Assert.Equal(WireRetCode.Ok, byName.RetCode);

        _ = await AddExpressionAsync(client, session.SessionId, session.Handle, "n3", "n1 + n2");

        RemoveExpressionResponse byIndex = await client.RemoveExpressionAsync(
            new RemoveExpressionRequest
            {
                SessionId = session.SessionId,
                DatawindowHandle = session.Handle,
                Target = new ColumnRef { Index = 1 },
            },
            cancellationToken: Ct);

        Assert.Equal(WireRetCode.Ok, byIndex.RetCode);

        // of_removeall [:L125] - and the state really is empty afterwards, which is what makes the
        // remove assertions above more than a return-code check.
        _ = await AddExpressionAsync(client, session.SessionId, session.Handle, "n2", "n1 * 2");
        _ = await AddExpressionAsync(client, session.SessionId, session.Handle, "n3", "n1 + n2");

        RemoveAllExpressionsResponse cleared = await client.RemoveAllExpressionsAsync(
            new RemoveAllExpressionsRequest
            {
                SessionId = session.SessionId,
                DatawindowHandle = session.Handle,
            },
            cancellationToken: Ct);

        Assert.Equal(WireRetCode.Ok, cleared.RetCode);
        Assert.Empty((await StateAsync(client, session)).Expressions);
    }

    /// <summary>
    /// The SEVEN typed <c>of_addvar</c> families, in the source's own declaration order.
    /// </summary>
    /// <param name="arm">The union arm under test.</param>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// EXACTLY SEVEN - NOT SIX, NOT EIGHT. They are the seven typed overloads at [:L153-L159]:
    /// <c>time</c>, <c>string</c>, <c>long</c>, <c>double</c>, <c>datetime</c>, <c>date</c>,
    /// <c>boolean</c>. The wire union is narrowed to those seven rather than reusing the eleven-armed
    /// <c>any</c> carrier, because a caller CANNOT declare a variable of any other type - an eighth arm
    /// would invent capability the coercion path has nothing to dispatch on.
    /// </remarks>
    [Theory]
    [InlineData(VarValue.KindOneofCase.TimeValue)]
    [InlineData(VarValue.KindOneofCase.StringValue)]
    [InlineData(VarValue.KindOneofCase.LongValue)]
    [InlineData(VarValue.KindOneofCase.DoubleValue)]
    [InlineData(VarValue.KindOneofCase.DatetimeValue)]
    [InlineData(VarValue.KindOneofCase.DateValue)]
    [InlineData(VarValue.KindOneofCase.BoolValue)]
    public async Task AddVariableAcceptsEachOfTheSevenTypedFamilies(VarValue.KindOneofCase arm)
    {
        WireClient client = Client();
        Session session = await ReadySessionAsync(client, host);

        AddVariableResponse added = await client.AddVariableAsync(
            new AddVariableRequest
            {
                SessionId = session.SessionId,
                DatawindowHandle = session.Handle,
                Name = "v_" + arm.ToString(),
                Value = ValueFor(arm),
            },
            cancellationToken: Ct);

        Assert.Equal(WireRetCode.Ok, added.RetCode);
        Assert.Null(added.Error);

        // A DUPLICATE DEFINITION IS AN ERROR IN THE LEGACY [:L1607], reported rather than overwriting.
        AddVariableResponse duplicate = await client.AddVariableAsync(
            new AddVariableRequest
            {
                SessionId = session.SessionId,
                DatawindowHandle = session.Handle,
                Name = "v_" + arm.ToString(),
                Value = ValueFor(arm),
            },
            cancellationToken: Ct);

        Assert.NotEqual(WireRetCode.Ok, duplicate.RetCode);
        Assert.NotNull(duplicate.Error);
        Assert.Equal(
            ExpressionError.Types.Category.DuplicateDefinition,
            duplicate.Error.Category);

        // AND THE MESSAGES FROM THIS ENGINE ARE HARDCODED CHINESE THAT DOES NOT ROUTE THROUGH
        // LOCALIZATION [:L1607 against se_cst_dw.sru:L355, :L357 which DO localize]. That inconsistency
        // is legacy behaviour and is reproduced rather than harmonized (C-B).
        Assert.False(duplicate.Error.Error.Localized);
    }

    /// <summary>
    /// The SEVEN types times THREE arities - value, value plus recalculate, and value plus recalculate
    /// plus force.
    /// </summary>
    /// <param name="arm">The union arm under test.</param>
    /// <returns>A task representing the assertions.</returns>
    /// <remarks>
    /// FORCE AND RECALCULATE ARE NOT THE SAME REQUEST, which is what makes the third arity worth having:
    /// recalculate ASKS for a recalculation, force asks for it EVEN WHERE THE ENGINE WOULD HAVE DECIDED IT
    /// WAS UNNECESSARY [:L188-L194]. Absence of each field is carried explicitly, so a two-argument call
    /// stays distinguishable from a three-argument one with the flag set false.
    /// </remarks>
    [Theory]
    [InlineData(VarValue.KindOneofCase.TimeValue)]
    [InlineData(VarValue.KindOneofCase.StringValue)]
    [InlineData(VarValue.KindOneofCase.LongValue)]
    [InlineData(VarValue.KindOneofCase.DoubleValue)]
    [InlineData(VarValue.KindOneofCase.DatetimeValue)]
    [InlineData(VarValue.KindOneofCase.DateValue)]
    [InlineData(VarValue.KindOneofCase.BoolValue)]
    public async Task SetVariableAcceptsAllThreeAritiesForEveryTypedFamily(VarValue.KindOneofCase arm)
    {
        WireClient client = Client();
        Session session = await ReadySessionAsync(client, host);

        string name = "v_" + arm.ToString();

        Assert.Equal(
            WireRetCode.Ok,
            (await client.AddVariableAsync(
                new AddVariableRequest
                {
                    SessionId = session.SessionId,
                    DatawindowHandle = session.Handle,
                    Name = name,
                    Value = ValueFor(arm),
                },
                cancellationToken: Ct)).RetCode);

        foreach ((bool? recalc, bool? force) in new (bool?, bool?)[]
        {
            (null, null),
            (true, null),
            (true, true),
        })
        {
            SetVariableRequest request = new()
            {
                SessionId = session.SessionId,
                DatawindowHandle = session.Handle,
                Name = name,
                Value = ValueFor(arm),
            };

            if (recalc is { } wantsRecalc)
            {
                request.Recalc = wantsRecalc;
            }

            if (force is { } wantsForce)
            {
                request.Force = wantsForce;
            }

            SetVariableResponse set = await client.SetVariableAsync(request, cancellationToken: Ct);

            Assert.Equal(WireRetCode.Ok, set.RetCode);
            Assert.Equal(recalc is not null, request.HasRecalc);
            Assert.Equal(force is not null, request.HasForce);
        }
    }

    /// <summary>
    /// Expression variables: add, set in three arities, and get.
    /// </summary>
    /// <returns>A task representing the assertions.</returns>
    /// <remarks>
    /// AN EXPRESSION VARIABLE IS RECURSIVE BY CONSTRUCTION - its value MAY ITSELF BE AN EXPRESSION with
    /// its own variable and function references [docs:L26] - which is why <c>LocalVarData</c> nests and why
    /// the getter answers the local definition alongside the text.
    /// </remarks>
    [Fact]
    public async Task ExpressionVariableAddSetAndGetCoverTheirArities()
    {
        WireClient client = Client();
        Session session = await ReadySessionAsync(client, host);

        AddVariableExpressionResponse added = await client.AddVariableExpressionAsync(
            new AddVariableExpressionRequest
            {
                SessionId = session.SessionId,
                DatawindowHandle = session.Handle,
                Name = ThisMonth,
                Exp = "5",
            },
            cancellationToken: Ct);

        Assert.Equal(WireRetCode.Ok, added.RetCode);

        // THE THREE of_setvarexp ARITIES [:L178, :L186, :L187].
        foreach ((bool? recalc, bool? force, string exp) in new (bool?, bool?, string)[]
        {
            (null, null, "6"),
            (true, null, "7"),
            (true, true, "8"),
        })
        {
            SetVariableExpressionRequest request = new()
            {
                SessionId = session.SessionId,
                DatawindowHandle = session.Handle,
                Name = ThisMonth,
                Exp = exp,
            };

            if (recalc is { } wantsRecalc)
            {
                request.Recalc = wantsRecalc;
            }

            if (force is { } wantsForce)
            {
                request.Force = wantsForce;
            }

            SetVariableExpressionResponse set =
                await client.SetVariableExpressionAsync(request, cancellationToken: Ct);

            Assert.Equal(WireRetCode.Ok, set.RetCode);

            GetVariableExpressionResponse got = await client.GetVariableExpressionAsync(
                new GetVariableExpressionRequest
                {
                    SessionId = session.SessionId,
                    DatawindowHandle = session.Handle,
                    Name = ThisMonth,
                },
                cancellationToken: Ct);

            Assert.Equal(WireRetCode.Ok, got.RetCode);
            Assert.Equal(exp, got.Exp);
            Assert.Equal(exp, got.Local.Exp);
        }

        // An unknown name is a DEFINED refusal and not an empty success.
        GetVariableExpressionResponse missing = await client.GetVariableExpressionAsync(
            new GetVariableExpressionRequest
            {
                SessionId = session.SessionId,
                DatawindowHandle = session.Handle,
                Name = "nothingDefinedUnderThisName",
            },
            cancellationToken: Ct);

        Assert.Equal(WireRetCode.EVarNotFound, missing.RetCode);
    }

    /// <summary>Reads the engine-state snapshot, bindings included.</summary>
    /// <param name="client">The client.</param>
    /// <param name="session">The session.</param>
    /// <returns>The snapshot.</returns>
    /// <remarks>
    /// THE OPERATION THAT MAKES THE SEVEN STRUCTURES REACHABLE. It is a READ - the engine's state is
    /// mutated through the typed operations and never by posting a snapshot back - so there is no Apply,
    /// and that asymmetry is deliberate: a settable snapshot would let a caller construct a dependency
    /// graph the parser never produced.
    /// </remarks>
    private static async Task<GetExpressionStateResponse> StateAsync(WireClient client, Session session)
    {
        GetExpressionStateResponse state = await client.GetExpressionStateAsync(
            new GetExpressionStateRequest
            {
                SessionId = session.SessionId,
                DatawindowHandle = session.Handle,
                IncludeBindings = true,
            },
            cancellationToken: Ct);

        Assert.Equal(WireRetCode.Ok, state.RetCode);

        return state;
    }

    /// <summary>One value per union arm, so the seven families are driven from one place.</summary>
    /// <param name="arm">The arm to build.</param>
    /// <returns>The value.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The arm is not one of the seven the legacy declares. Reported rather than defaulted, because a
    /// silent default would let an eighth arm appear untested.
    /// </exception>
    /// <remarks>
    /// THE TEMPORAL FORMS ARE THE CONTRACT'S CANONICAL TEXT and are deliberately not built from
    /// <see cref="DateTime.Now"/>: a recording taken from this suite has to be reproducible, so no clock
    /// is read anywhere in it.
    /// </remarks>
    private static VarValue ValueFor(VarValue.KindOneofCase arm) => arm switch
    {
        // [:L153] time
        VarValue.KindOneofCase.TimeValue => new VarValue
        {
            TimeValue = new WireTime { Value = "13:45:59" },
        },

        // [:L154] string - and note the legacy string overload has no body of its own: it delegates to
        // of_AddVarExp(name, "'" + value + "'"), so a string variable IS an expression variable.
        VarValue.KindOneofCase.StringValue => new VarValue { StringValue = "text" },

        // [:L155] long
        VarValue.KindOneofCase.LongValue => new VarValue { LongValue = 42L },

        // [:L156] double - a GENUINE double, so genuinely a protobuf double. There is no decimal
        // overload among the seven, which is why common.v1's "decimals are not doubles" rule is not in
        // tension with this arm.
        VarValue.KindOneofCase.DoubleValue => new VarValue { DoubleValue = 6.25d },

        // [:L157] datetime
        VarValue.KindOneofCase.DatetimeValue => new VarValue
        {
            DatetimeValue = new WireDateTime { Value = "2022-04-14T09:30:00" },
        },

        // [:L158] date
        VarValue.KindOneofCase.DateValue => new VarValue
        {
            DateValue = new WireDate { Value = "2022-04-14" },
        },

        // [:L159] boolean - rendered into an expression as `1=1` or `1=0` rather than as a keyword.
        VarValue.KindOneofCase.BoolValue => new VarValue { BoolValue = true },

        _ => throw new ArgumentOutOfRangeException(
            nameof(arm),
            arm,
            "of_addvar declares exactly seven typed overloads [:L153-L159], so an eighth arm has no "
                + "legacy counterpart and no coercion behaviour to reproduce."),
    };


    /// <summary>
    /// Relative columns in ALL FOUR SHAPES for BOTH kinds - singular and plural, by index and by name.
    /// </summary>
    /// <param name="inputOnly">
    /// <see langword="false"/> for the change-triggered set <c>relativecolids[]</c>,
    /// <see langword="true"/> for the input-triggered set <c>relativeinputcolids[]</c>.
    /// </param>
    /// <returns>A task representing the assertions.</returns>
    /// <remarks>
    /// <para>
    /// EIGHT LEGACY ENTRY POINTS COLLAPSE ONTO ONE MESSAGE WITH TWO DISCRIMINATORS:
    /// <c>of_setrelativecolumn</c> [:L128 by name, :L137 by index], <c>of_setrelativecolumns</c> [:L135 by
    /// index, :L136 by name], and the input variants <c>of_setrelativeinputcolumn</c> [:L129, :L140] and
    /// <c>of_setrelativeinputcolumns</c> [:L138-L139]. All four shapes per kind must reach THE SAME STATE,
    /// which is asserted by reading the snapshot back after each one.
    /// </para>
    /// <para>
    /// SINGULAR IS NOT PLURAL-WITH-ONE-ELEMENT, so <c>plural</c> is carried rather than inferred from the
    /// list length: a singular request carrying zero or several columns is MALFORMED rather than quietly
    /// reinterpreted, because collapsing them would silently turn an add into a replace.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RelativeColumnsCoverAllFourShapesForBothKinds(bool inputOnly)
    {
        WireClient client = Client();
        Session session = await ReadySessionAsync(client, host);

        int index = await AddExpressionAsync(client, session.SessionId, session.Handle, "n3", "n1 + n2");

        foreach ((ColumnRef target, bool plural) in new (ColumnRef, bool)[]
        {
            (new ColumnRef { Index = index }, false),
            (new ColumnRef { ColumnName = "n3" }, false),
            (new ColumnRef { Index = index }, true),
            (new ColumnRef { ColumnName = "n3" }, true),
        })
        {
            SetRelativeColumnsRequest request = new()
            {
                SessionId = session.SessionId,
                DatawindowHandle = session.Handle,
                Target = target,
                Plural = plural,
                InputOnly = inputOnly,
            };

            request.RelativeColumns.Add("n1");

            SetRelativeColumnsResponse set =
                await client.SetRelativeColumnsAsync(request, cancellationToken: Ct);

            Assert.Equal(WireRetCode.Ok, set.RetCode);

            ColumnExpData bound = Assert.Single((await StateAsync(client, session)).Expressions);

            // n1 is column ordinal 1 in the transcribed definition, and the ordinals on the wire are
            // ONE-BASED [dw_test_dwsvc.srd:L8].
            if (inputOnly)
            {
                Assert.Equal([1L], bound.RelativeInputColIds);
                Assert.Empty(bound.RelativeColIds);
            }
            else
            {
                Assert.Equal([1L], bound.RelativeColIds);
                Assert.Empty(bound.RelativeInputColIds);
            }
        }

        // A SINGULAR REQUEST CARRYING SEVERAL COLUMNS IS MALFORMED, not a plural one in disguise.
        SetRelativeColumnsRequest batch = new()
        {
            SessionId = session.SessionId,
            DatawindowHandle = session.Handle,
            Target = new ColumnRef { Index = index },
            Plural = false,
            InputOnly = inputOnly,
        };

        batch.RelativeColumns.Add("n1");
        batch.RelativeColumns.Add("n2");

        Assert.Equal(
            WireRetCode.EInvalidArgument,
            (await client.SetRelativeColumnsAsync(batch, cancellationToken: Ct)).RetCode);

        // And so is a request that addresses nothing at all.
        Assert.Equal(
            WireRetCode.EInvalidArgument,
            (await client.SetRelativeColumnsAsync(
                new SetRelativeColumnsRequest
                {
                    SessionId = session.SessionId,
                    DatawindowHandle = session.Handle,
                    Plural = true,
                    InputOnly = inputOnly,
                },
                cancellationToken: Ct)).RetCode);
    }

    /// <summary>
    /// THE INPUT SET IS A DIFFERENT TRIGGER, NOT A STRICTER ONE: a programmatic change does not fire an
    /// input-relative expression.
    /// </summary>
    /// <param name="inputOnly">Which set the dependency is registered in.</param>
    /// <param name="expectDependentToFire">Whether the dependent expression should recalculate.</param>
    /// <returns>A task representing the assertions.</returns>
    /// <remarks>
    /// <para>
    /// THE SPECIFICATION'S OWN DISTINCTION [docs/n_cst_dwsvc_columnexp.md:L162-L163]: a relative column
    /// recalculates on ANY value change, while a relative INPUT column recalculates ONLY WHEN THE USER
    /// TYPED IT. Mechanically that is the <c>frominput</c> flag of <c>ondoitemchanged</c> [:L87] reaching
    /// the trigger decision, and a CALCULATION passes it FALSE [:L860-L862] - "a calculation is not user
    /// input", which is exactly what stops a calculated change from firing an input-triggered expression.
    /// </para>
    /// <para>
    /// OBSERVED THROUGH THE TRACE RATHER THAN THROUGH A TIMEOUT. A sentinel expression is calculated after
    /// the one under test, so the absence of a record is proved by the NEXT record being the sentinel's -
    /// no wall-clock wait, and therefore reproducible (AAP 0.6.7).
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task TheInputRelativeSetIsADifferentTriggerAndNotAStricterOne(
        bool inputOnly,
        bool expectDependentToFire)
    {
        WireClient client = Client();
        Session session = await ReadySessionAsync(client, host);

        // n1 gets a constant expression, so calculating it CHANGES its value from the loaded 10 and
        // therefore cascades [:L860-L862]. n2 depends on n1. s1 is the sentinel.
        _ = await AddExpressionAsync(client, session.SessionId, session.Handle, "n1", "3");
        int dependent = await AddExpressionAsync(
            client,
            session.SessionId,
            session.Handle,
            "n2",
            "n1 + 1");
        _ = await AddExpressionAsync(client, session.SessionId, session.Handle, "s1", "'sentinel'");

        SetRelativeColumnsRequest relative = new()
        {
            SessionId = session.SessionId,
            DatawindowHandle = session.Handle,
            Target = new ColumnRef { Index = dependent },
            Plural = true,
            InputOnly = inputOnly,
        };

        relative.RelativeColumns.Add("n1");

        Assert.Equal(
            WireRetCode.Ok,
            (await client.SetRelativeColumnsAsync(relative, cancellationToken: Ct)).RetCode);

        await SetTraceAsync(client, session, true);

        using AsyncDuplexStreamingCall<TraceChannelRequest, TraceRecord> trace =
            await SubscribeTraceAsync(host, client, session);

        Assert.Equal(WireRetCode.Ok, (await CalcColumnAsync(
            client,
            session.SessionId,
            session.Handle,
            "n1")).RetCode);

        Assert.Equal(WireRetCode.Ok, (await CalcColumnAsync(
            client,
            session.SessionId,
            session.Handle,
            "s1")).RetCode);

        IReadOnlyList<TraceRecord> records =
            await ReadTraceAsync(trace, expectDependentToFire ? 3 : 2);

        await trace.RequestStream.CompleteAsync();

        IReadOnlyList<string> stacks = [.. records.Select(static record => record.Stack)];

        if (expectDependentToFire)
        {
            // The cascade fired: n2 was calculated INSIDE n1's frame, so its stack carries n1 ahead of it.
            Assert.Equal(["n1", "n1>n2", "s1"], stacks);
        }
        else
        {
            // The cascade did NOT fire, and the sentinel proves it: the record after n1's is s1's.
            Assert.Equal(["n1", "s1"], stacks);
        }
    }

    /// <summary>
    /// FOUR per-expression flags times TWO addressing modes, and an unspecified flag is refused.
    /// </summary>
    /// <param name="flag">The flag under test.</param>
    /// <returns>A task representing the assertions.</returns>
    /// <remarks>
    /// <c>of_setalwayscalc</c> [:L133-L134], <c>of_setrecursive</c> [:L146-L147],
    /// <c>of_settriggerevent</c> [:L148-L149] AND <c>of_setcacheable</c> [:L196-L197]. The fourth is
    /// DECLARED IN THE SOURCE and absent from the documented nine, which is exactly why it is here: the
    /// contract covers the surface the source has, not the surface the documentation describes.
    /// </remarks>
    [Theory]
    [InlineData(SetExpressionFlagRequest.Types.Flag.AlwaysCalc)]
    [InlineData(SetExpressionFlagRequest.Types.Flag.Recursive)]
    [InlineData(SetExpressionFlagRequest.Types.Flag.TriggerEvent)]
    [InlineData(SetExpressionFlagRequest.Types.Flag.Cacheable)]
    public async Task EveryExpressionFlagIsSettableByIndexAndByName(
        SetExpressionFlagRequest.Types.Flag flag)
    {
        WireClient client = Client();
        Session session = await ReadySessionAsync(client, host);

        int index = await AddExpressionAsync(client, session.SessionId, session.Handle, "n3", "n1 + n2");

        foreach (ColumnRef target in new[]
        {
            new ColumnRef { Index = index },
            new ColumnRef { ColumnName = "n3" },
        })
        {
            foreach (bool enabled in new[] { true, false })
            {
                SetExpressionFlagResponse set = await client.SetExpressionFlagAsync(
                    new SetExpressionFlagRequest
                    {
                        SessionId = session.SessionId,
                        DatawindowHandle = session.Handle,
                        Target = target,
                        Flag = flag,
                        Enabled = enabled,
                    },
                    cancellationToken: Ct);

                Assert.Equal(WireRetCode.Ok, set.RetCode);

                ColumnExpData bound = Assert.Single((await StateAsync(client, session)).Expressions);

                Assert.Equal(enabled, flag switch
                {
                    SetExpressionFlagRequest.Types.Flag.AlwaysCalc => bound.AlwaysCalc,
                    SetExpressionFlagRequest.Types.Flag.Recursive => bound.Recursive,
                    SetExpressionFlagRequest.Types.Flag.TriggerEvent => bound.TriggerEvent,
                    _ => bound.Cacheable,
                });
            }
        }

        // FLAG_UNSPECIFIED NAMES NO LEGACY SETTER, so it is refused rather than defaulted onto one of the
        // four. Defaulting would silently set a flag the caller never named.
        Assert.Equal(
            WireRetCode.EInvalidArgument,
            (await client.SetExpressionFlagAsync(
                new SetExpressionFlagRequest
                {
                    SessionId = session.SessionId,
                    DatawindowHandle = session.Handle,
                    Target = new ColumnRef { Index = index },
                    Flag = SetExpressionFlagRequest.Types.Flag.Unspecified,
                    Enabled = true,
                },
                cancellationToken: Ct)).RetCode);
    }

    /// <summary>
    /// Calc in FOUR arities, calc-all in TWO and calc-empty in TWO.
    /// </summary>
    /// <returns>A task representing the assertions.</returns>
    /// <remarks>
    /// <para>
    /// THE FOUR <c>of_calc</c> OVERLOADS ARE [:L143] by index, [:L144] by name, [:L168] with a force flag
    /// and [:L169] the whole row. THE LEGACY HAS AN OVERLOAD COLLISION the wire resolves: at source
    /// <c>of_calc(row, integer index)</c> and <c>of_calc(row, boolean force)</c> are distinguished ONLY BY
    /// THE SECOND ARGUMENT'S TYPE, so on the wire they are distinguished by WHICH FIELD IS SET - and a
    /// request setting BOTH is malformed rather than merely redundant.
    /// </para>
    /// <para>
    /// <c>of_calcall(true)</c> is the FORCE variant the oracle's own window binds to a button
    /// [w_test_dwsvc_columnexp.srw:L223], and <c>of_calcempty</c>'s two arities [:L199-L200] are the second
    /// group the documented nine omit.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task CalcCoversFourAritiesAndCalcAllAndCalcEmptyCoverBoth()
    {
        WireClient client = Client();
        Session session = await ReadySessionAsync(client, host);

        int index = await AddExpressionAsync(client, session.SessionId, session.Handle, "n3", "n1 + n2");

        // [:L143] by index, and [:L144] by name - each answers the one result it named.
        foreach (ColumnRef target in new[]
        {
            new ColumnRef { Index = index },
            new ColumnRef { ColumnName = "n3" },
        })
        {
            CalcResponse calc = await client.CalcAsync(
                new CalcRequest
                {
                    SessionId = session.SessionId,
                    DatawindowHandle = session.Handle,
                    Row = 1L,
                    Target = target,
                },
                cancellationToken: Ct);

            Assert.Equal(WireRetCode.Ok, calc.RetCode);
            Assert.Equal("15", DecimalTextOf(calc));
        }

        // [:L169] the whole row, and [:L168] the whole row with force. Neither names a single expression,
        // so neither answers a single result - the response is a code, not a value.
        foreach (bool? force in new bool?[] { null, true, false })
        {
            CalcRequest request = new()
            {
                SessionId = session.SessionId,
                DatawindowHandle = session.Handle,
                Row = 1L,
            };

            if (force is { } wantsForce)
            {
                request.Force = wantsForce;
            }

            CalcResponse calc = await client.CalcAsync(request, cancellationToken: Ct);

            Assert.Equal(WireRetCode.Ok, calc.RetCode);
            Assert.Empty(calc.Results);
        }

        // A REQUEST THAT SETS BOTH A TARGET AND A FORCE FLAG IS MALFORMED - it names two different legacy
        // overloads at once.
        CalcRequest ambiguous = new()
        {
            SessionId = session.SessionId,
            DatawindowHandle = session.Handle,
            Row = 1L,
            Target = new ColumnRef { Index = index },
            Force = true,
        };

        Assert.Equal(
            WireRetCode.EInvalidArgument,
            (await client.CalcAsync(ambiguous, cancellationToken: Ct)).RetCode);

        // of_calcall x2 [:L145, :L170] - including the force variant the oracle binds to a button.
        foreach (bool? force in new bool?[] { null, true })
        {
            CalcAllRequest request = new()
            {
                SessionId = session.SessionId,
                DatawindowHandle = session.Handle,
            };

            if (force is { } wantsForce)
            {
                request.Force = wantsForce;
            }

            CalcAllResponse all = await client.CalcAllAsync(request, cancellationToken: Ct);

            Assert.Equal(WireRetCode.Ok, all.RetCode);
            Assert.Equal(force is not null, request.HasForce);
        }

        // of_calcempty x2 [:L199 one row, :L200 every row].
        foreach (long? row in new long?[] { null, 1L })
        {
            CalcEmptyRequest request = new()
            {
                SessionId = session.SessionId,
                DatawindowHandle = session.Handle,
            };

            if (row is { } wantsRow)
            {
                request.Row = wantsRow;
            }

            CalcEmptyResponse empty = await client.CalcEmptyAsync(request, cancellationToken: Ct);

            Assert.Equal(WireRetCode.Ok, empty.RetCode);
            Assert.Equal(row is not null, request.HasRow);
        }
    }

    /// <summary>
    /// <c>_of_calcitem</c> keeps BOTH of its anomalies: it is PUBLIC despite the private-convention
    /// underscore prefix, and it answers a bare <c>bool</c>.
    /// </summary>
    /// <returns>A task representing the assertions.</returns>
    /// <remarks>
    /// <para>
    /// ANOMALY 1 - PUBLIC WITH AN UNDERSCORE. Every other <c>_of_*</c> member of this object is private or
    /// protected; this one is <c>public function boolean _of_calcitem</c> [:L142]. Callers may depend on
    /// it and THE NAME APPEARS IN RECORDINGS, so it is carried across rather than tidied into
    /// <c>CalcItem</c> on the managed side. A protobuf identifier cannot begin with an underscore, so the
    /// rpc is named <c>CalcItem</c> and the legacy spelling travels as DESCRIPTOR METADATA - asserted below
    /// off the descriptor, because a comment claiming the name survived would not be true and a silent
    /// drop would not be honest either.
    /// </para>
    /// <para>
    /// ANOMALY 2 - IT RETURNS <c>boolean</c>, NOT A RETURN CODE, unlike every other calculation entry
    /// point. So IT CANNOT REPORT WHY IT FAILED, only that it did. That is a real expressive limit of the
    /// legacy API and it is reproduced: the response's primary result is a bool, and <c>ret_code</c> beside
    /// it is for TRANSPORT-level outcomes only - a missing session, a non-positive index - which the
    /// in-process legacy could not have had. It must not be used to smuggle a richer verdict, which is why
    /// the out-of-range case below answers <c>succeeded = false</c> with <c>ret_code = OK</c>.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task CalcItemKeepsItsBooleanResultAndCarriesItsLegacyNameOnTheDescriptor()
    {
        // ANOMALY 1, on the managed member: public, and spelled with the leading underscore.
        MethodInfo? calcItem = typeof(ColumnExpressionEngine).GetMethod(
            "_of_calcitem",
            BindingFlags.Public | BindingFlags.Instance);

        Assert.NotNull(calcItem);
        Assert.True(calcItem.IsPublic);
        Assert.StartsWith("_of_", calcItem.Name, StringComparison.Ordinal);

        // ANOMALY 2, on the managed member: the awaited result is a bool and not a code.
        Assert.Equal(typeof(ValueTask<bool>), calcItem.ReturnType);

        // ANOMALY 1, on the wire: the rpc is CalcItem and the legacy spelling is descriptor metadata, so
        // an adapter or a recording comparator keys on the option rather than on the rpc name.
        MethodDescriptor descriptor = WireService.Descriptor.FindMethodByName("CalcItem");

        Assert.NotNull(descriptor.GetOptions());
        Assert.Equal(
            "_of_calcitem",
            descriptor.GetOptions()!.GetExtension(CommonExtensions.LegacyName));

        WireClient client = Client();
        Session session = await ReadySessionAsync(client, host);

        int index = await AddExpressionAsync(client, session.SessionId, session.Handle, "n3", "n1 + n2");

        CalcItemResponse calculated = await client.CalcItemAsync(
            new CalcItemRequest
            {
                SessionId = session.SessionId,
                DatawindowHandle = session.Handle,
                Row = 1L,
                Index = index,
            },
            cancellationToken: Ct);

        Assert.True(calculated.Succeeded);
        Assert.Equal(WireRetCode.Ok, calculated.RetCode);
        Assert.Equal("15", calculated.Result.Value.DecimalValue.Value);

        // The SECOND calculation of an unchanged value answers FALSE, because the legacy refuses a write
        // that would not change anything [:L773-L774] - and a bare boolean is all it can say about that.
        CalcItemResponse unchanged = await client.CalcItemAsync(
            new CalcItemRequest
            {
                SessionId = session.SessionId,
                DatawindowHandle = session.Handle,
                Row = 1L,
                Index = index,
            },
            cancellationToken: Ct);

        Assert.False(unchanged.Succeeded);
        Assert.Equal(WireRetCode.Ok, unchanged.RetCode);

        // THIS ENTRY POINT TAKES AN INDEX ONLY - there is no by-name overload [:L142] - so a non-positive
        // index is a TRANSPORT fault and answers on ret_code, while an out-of-range one is a calculation
        // that did not happen and answers on the boolean.
        CalcItemResponse nonPositive = await client.CalcItemAsync(
            new CalcItemRequest
            {
                SessionId = session.SessionId,
                DatawindowHandle = session.Handle,
                Row = 1L,
                Index = 0,
            },
            cancellationToken: Ct);

        Assert.Equal(WireRetCode.EInvalidArgument, nonPositive.RetCode);
        Assert.False(nonPositive.Succeeded);

        CalcItemResponse outOfRange = await client.CalcItemAsync(
            new CalcItemRequest
            {
                SessionId = session.SessionId,
                DatawindowHandle = session.Handle,
                Row = 1L,
                Index = 99,
            },
            cancellationToken: Ct);

        Assert.Equal(WireRetCode.Ok, outOfRange.RetCode);
        Assert.False(outOfRange.Succeeded);
    }

    /// <summary>
    /// An out-of-range index and an unknown column name answer DEFINED statuses rather than throwing.
    /// </summary>
    /// <returns>A task representing the assertions.</returns>
    /// <remarks>
    /// <para>
    /// A UNARY METHOD CARRIES ITS OUTCOME IN ITS OWN <c>ret_code</c>, which preserves the tri-state algebra
    /// a transport status cannot express - so none of these is an exception, and asserting that they are
    /// not is the point. The codes are the sibling session layer's vocabulary, so a caller sees one
    /// vocabulary across C-03 and C-04.
    /// </para>
    /// <para>
    /// THE TWO ADDRESSING MODES ANSWER DIFFERENTLY AND THAT IS CORRECT, not an inconsistency: an INDEX
    /// addresses a position, so reading past the end is simply nothing to read, while a NAME goes through
    /// a lookup that reports its own failure [:L141].
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AnOutOfRangeIndexAndAnUnknownColumnNameAnswerDefinedStatuses()
    {
        WireClient client = Client();
        Session session = await ReadySessionAsync(client, host);

        _ = await AddExpressionAsync(client, session.SessionId, session.Handle, "n3", "n1 + n2");

        // BY INDEX, past the end: no expression to read, reported as an empty answer rather than a fault.
        GetExpressionResponse pastTheEnd = await client.GetExpressionAsync(
            new GetExpressionRequest
            {
                SessionId = session.SessionId,
                DatawindowHandle = session.Handle,
                Target = new ColumnRef { Index = 99 },
            },
            cancellationToken: Ct);

        Assert.Equal(WireRetCode.Ok, pastTheEnd.RetCode);
        Assert.Equal(string.Empty, pastTheEnd.Exp);

        // BY NAME, unknown: the lookup reports its own failure.
        GetExpressionResponse unknownName = await client.GetExpressionAsync(
            new GetExpressionRequest
            {
                SessionId = session.SessionId,
                DatawindowHandle = session.Handle,
                Target = new ColumnRef { ColumnName = "no_such_column" },
            },
            cancellationToken: Ct);

        Assert.Equal(WireRetCode.ENotExists, unknownName.RetCode);

        // REMOVE by an unknown name answers the bound-check code, which is a different statement from
        // "no expression is bound to that column" and is preserved as such.
        Assert.Equal(
            WireRetCode.EOutOfBound,
            (await client.RemoveExpressionAsync(
                new RemoveExpressionRequest
                {
                    SessionId = session.SessionId,
                    DatawindowHandle = session.Handle,
                    Target = new ColumnRef { ColumnName = "no_such_column" },
                },
                cancellationToken: Ct)).RetCode);

        // ADDING to a column the DataWindow does not have fails, and the failure is in band.
        AddExpressionResponse unknownColumn = await client.AddExpressionAsync(
            new AddExpressionRequest
            {
                SessionId = session.SessionId,
                DatawindowHandle = session.Handle,
                ColumnName = "no_such_column",
                Exp = "1",
            },
            cancellationToken: Ct);

        Assert.Equal(WireRetCode.Failed, unknownColumn.RetCode);

        // Calculating a row outside the buffer is an argument fault, not an empty success.
        Assert.Equal(
            WireRetCode.EInvalidArgument,
            (await CalcColumnAsync(client, session.SessionId, session.Handle, "n3", 99L)).RetCode);

        // And every one of the four resolution codes is reachable and distinct: a blank identifier, an
        // unknown session, and a handle that is not co-resident.
        Assert.Equal(
            WireRetCode.EInvalidArgument,
            (await client.GetServiceStateAsync(
                new GetServiceStateRequest(),
                cancellationToken: Ct)).RetCode);

        Assert.Equal(
            WireRetCode.ENotExists,
            (await client.GetServiceStateAsync(
                new GetServiceStateRequest
                {
                    SessionId = "no-such-session",
                    DatawindowHandle = "no-such-session/1",
                },
                cancellationToken: Ct)).RetCode);

        Assert.Equal(
            WireRetCode.EInvalidHandle,
            (await client.GetServiceStateAsync(
                new GetServiceStateRequest
                {
                    SessionId = session.SessionId,
                    DatawindowHandle = session.SessionId + "/99",
                },
                cancellationToken: Ct)).RetCode);
    }

    /// <summary>
    /// <c>of_settrace</c> is NOT vetoable, unlike <c>#Enabled</c>, and the asymmetry is the legacy's.
    /// </summary>
    /// <returns>A task representing the assertions.</returns>
    /// <remarks>
    /// It assigns and returns OK unconditionally [:L208, implemented :L2409-L2411], where
    /// <c>of_setenabled</c> raises a vetoable hook. Setting it does NOT subscribe anybody to
    /// <c>TraceChannel</c> and subscribing does not set it - two different acts, which is why
    /// <c>GetServiceState</c> publishes both flags together.
    /// </remarks>
    [Fact]
    public async Task SetTraceIsNotVetoableAndItsStateIsPublished()
    {
        WireClient client = Client();
        Session session = await ReadySessionAsync(client, host);

        foreach (bool wanted in new[] { true, false, true })
        {
            SetTraceResponse set = await client.SetTraceAsync(
                new SetTraceRequest
                {
                    SessionId = session.SessionId,
                    DatawindowHandle = session.Handle,
                    Trace = wanted,
                },
                cancellationToken: Ct);

            Assert.Equal(WireRetCode.Ok, set.RetCode);
            Assert.Equal(wanted, set.Trace);

            GetServiceStateResponse state = await client.GetServiceStateAsync(
                new GetServiceStateRequest
                {
                    SessionId = session.SessionId,
                    DatawindowHandle = session.Handle,
                },
                cancellationToken: Ct);

            Assert.Equal(wanted, state.Trace);
        }
    }

    /// <summary>Sets <c>#Trace</c> and asserts the setter answered.</summary>
    /// <param name="client">The client.</param>
    /// <param name="session">The session.</param>
    /// <param name="trace">The state to move to.</param>
    /// <returns>A task representing the call.</returns>
    private static async Task SetTraceAsync(WireClient client, Session session, bool trace)
    {
        SetTraceResponse response = await client.SetTraceAsync(
            new SetTraceRequest
            {
                SessionId = session.SessionId,
                DatawindowHandle = session.Handle,
                Trace = trace,
            },
            cancellationToken: Ct);

        Assert.Equal(WireRetCode.Ok, response.RetCode);
        Assert.Equal(trace, response.Trace);
    }

    /// <summary>Opens a trace channel, subscribes it to one DataWindow and waits for registration.</summary>
    /// <param name="factory">
    /// The host that produced <paramref name="client"/>. REQUIRED AND NOT DEFAULTED: the barrier below
    /// reaches into that host's broker, and a case using its own host would otherwise silently address the
    /// shared fixture's broker instead and never see its barrier come back.
    /// </param>
    /// <param name="client">The client.</param>
    /// <param name="session">The session.</param>
    /// <returns>The channel, which the caller owns and must dispose.</returns>
    /// <remarks>
    /// <para>
    /// SUBSCRIBING IS NOT ENABLING. The subscription only asks to receive; emission is gated on the
    /// engine's own <c>#Trace</c> [:L98], so a subscriber on a service whose flag is clear correctly
    /// receives nothing.
    /// </para>
    /// <para>
    /// WHY THE BARRIER IS REQUIRED, AND WHY IT IS NOT A SLEEP. The subscribe message travels one HTTP/2
    /// stream while the calculations that follow are issued on others, and NOTHING orders the server's
    /// <em>reading</em> of that message against them: a completed <c>WriteAsync</c> means the frame was
    /// handed to the transport, not that the handler consumed it. A calculation that emits before the
    /// handler registers loses its record - <see cref="ExpressionTraceBroker.Emit"/> drops a record with
    /// no subscriber, correctly, because the trace is fire-and-forget diagnostics - and the test then
    /// waits forever for a record that no longer exists. That is a flake in the harness, not in the
    /// service, so it is closed here rather than papered over with a timeout: a synthetic barrier record
    /// is offered to the broker until one completes the full trip back over the wire, which can only
    /// happen once the server-side subscription exists. The loop finishes the instant the handler catches
    /// up, awaits no wall-clock delay, and so leaves the suite reproducible; the attempt ceiling exists
    /// only so a genuinely broken pipeline fails with a diagnosis instead of hanging.
    /// </para>
    /// </remarks>
    private static async Task<AsyncDuplexStreamingCall<TraceChannelRequest, TraceRecord>>
        SubscribeTraceAsync(
            DataServicesTestHostFactory factory,
            WireClient client,
            Session session)
    {
        AsyncDuplexStreamingCall<TraceChannelRequest, TraceRecord> channel =
            client.TraceChannel(cancellationToken: Ct);

        await channel.RequestStream.WriteAsync(
            new TraceChannelRequest
            {
                SessionId = session.SessionId,
                DatawindowHandle = session.Handle,
                Subscribe = true,
            },
            Ct);

        ExpressionTraceBroker broker = factory.Services.GetRequiredService<ExpressionTraceBroker>();
        Task<bool> arrival = channel.ResponseStream.MoveNext(Ct);

        for (int attempt = 0; !arrival.IsCompleted; attempt++)
        {
            Assert.True(
                attempt < BarrierAttemptCeiling,
                "The trace subscription was never registered server-side.");

            if (attempt % 512 == 0)
            {
                broker.Emit(BarrierRecord(session));
            }

            await Task.Yield();
        }

        Assert.True(await arrival);
        Assert.Equal(TraceBarrierColumn, channel.ResponseStream.Current.Dwo.Name);

        return channel;
    }

    /// <summary>Builds the synthetic record that proves a trace subscription is registered.</summary>
    /// <param name="session">The session that owns the subscription.</param>
    /// <returns>A record addressed to this session's DataWindow and to no calculation.</returns>
    /// <remarks>
    /// TAGGED SO IT CANNOT BE MISTAKEN FOR A CALCULATION. Its column name matches no column in the
    /// fixture, so the pump's own enrichment reports it verbatim and
    /// <see cref="ReadTraceAsync(AsyncDuplexStreamingCall{TraceChannelRequest, TraceRecord}, int)"/>
    /// discards it without counting it. Its sequence number is zero, which the engine never issues
    /// because its counter is pre-incremented, so no assertion on sequencing can be confused by residue.
    /// </remarks>
    private static ExpressionTraceRecord BarrierRecord(Session session) =>
        new()
        {
            SequenceNumber = 0L,
            SessionId = session.SessionId,
            Handle = new DataWindowHandle(session.Handle),
            Row = 0L,
            ColumnName = TraceBarrierColumn,
            Stack = TraceBarrierColumn,
            StackFrames = ImmutableArray<string>.Empty,
            Expression = string.Empty,
            Value = string.Empty,
            Depth = 0,
            Timestamp = DateTimeOffset.UnixEpoch,
        };

    /// <summary>Reads exactly <paramref name="count"/> trace records.</summary>
    /// <param name="channel">The subscribed channel.</param>
    /// <param name="count">How many records to read.</param>
    /// <returns>The records, in arrival order.</returns>
    /// <remarks>
    /// AN EXACT COUNT AND NO TIMEOUT. Every record this suite waits for has already been produced by a
    /// calculation the test itself awaited, so the read completes without a wall-clock bound - which is
    /// what keeps a recording taken from this suite reproducible. Asserting the ABSENCE of a record is
    /// done with a sentinel record rather than with a timeout, for the same reason.
    /// </remarks>
    private static async Task<IReadOnlyList<TraceRecord>> ReadTraceAsync(
        AsyncDuplexStreamingCall<TraceChannelRequest, TraceRecord> channel,
        int count)
    {
        List<TraceRecord> records = new(count);

        while (records.Count < count && await channel.ResponseStream.MoveNext(Ct))
        {
            TraceRecord record = channel.ResponseStream.Current;

            // Barrier residue, never a calculation - see BarrierRecord.
            if (string.Equals(record.Dwo?.Name, TraceBarrierColumn, StringComparison.Ordinal))
            {
                continue;
            }

            records.Add(record);
        }

        Assert.Equal(count, records.Count);

        return records;
    }


    // =================================================================================================
    //  PHASE 3 - THE FIVE EXPANSION MODES, PER REFERENCE, WITH THE FULL THREE-PART PAYLOAD
    //  -----------------------------------------------------------------------------------------------
    //  This is requirement G4(b): the `$` versus `$$` distinction "does not survive naive
    //  serialization" (AAP 0.1.3). The mechanism is documented at
    //  docs/n_cst_dwsvc_columnexp.md:sections 静态展开 and 动态展开 and implemented at
    //  n_cst_dwsvc_columnexp.sru:L1415-L1435: the parse rewrites the expression IN PLACE, so a static
    //  reference is resolved and substituted into the stored text at bind time - its name is GONE - while
    //  a dynamic reference is retained as an entry pointing into the live variable table.
    //
    //  That is why the contract carries THREE things and not one, and why every assertion below reads
    //  all three. A payload carrying only the expanded string would pass a naive value check and fail
    //  every test in this region.
    // =================================================================================================

    /// <summary>
    /// Pins C-04's expansion-mode alphabet and asserts the mode is tagged PER REFERENCE.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE ABSENCE ASSERTED HERE IS THE POINT. <c>ColumnExpData</c> carries no expansion-mode field, and
    /// it must not: the documentation's own worked example mixes a dynamic and a static reference inside
    /// ONE expression [docs/n_cst_dwsvc_columnexp.md:L68], so an expression-level mode could not describe
    /// it. The mode therefore lives on <c>VarData</c>, one per reference.
    /// </para>
    /// <para>
    /// The numeric values are pinned rather than merely the names, because a characterization recording
    /// stores the encoded field and a renumbering would silently invalidate every stored comparison
    /// (AAP 0.4.5.3).
    /// </para>
    /// </remarks>
    [Fact]
    public void TheModeAlphabetIsCompleteAndTheModeIsTaggedPerReferenceNeverPerExpression()
    {
        Assert.Equal(
            new[]
            {
                ExpansionMode.Unspecified,
                ExpansionMode.Static,
                ExpansionMode.Dynamic,
                ExpansionMode.DynamicIndirect,
                ExpansionMode.MacroDirect,
                ExpansionMode.MacroDynamic,
            },
            Enum.GetValues<ExpansionMode>());

        Assert.Equal(0, (int)ExpansionMode.Unspecified);
        Assert.Equal(1, (int)ExpansionMode.Static);
        Assert.Equal(2, (int)ExpansionMode.Dynamic);
        Assert.Equal(3, (int)ExpansionMode.DynamicIndirect);
        Assert.Equal(4, (int)ExpansionMode.MacroDirect);
        Assert.Equal(5, (int)ExpansionMode.MacroDynamic);

        FieldDescriptor mode = VarData.Descriptor.FindFieldByName("expansion_mode");

        Assert.Equal(FieldType.Enum, mode.FieldType);
        Assert.Equal("dataservices.v1.ExpansionMode", mode.EnumType.FullName);

        // PER REFERENCE, NEVER PER EXPRESSION.
        Assert.DoesNotContain(
            ColumnExpData.Descriptor.Fields.InDeclarationOrder(),
            field => field.Name.Contains("expansion", StringComparison.Ordinal));
    }

    /// <summary>
    /// Sends the documentation's own mixed expression and asserts its two references cross the wire
    /// differently - one retained and tagged dynamic, one baked in and recoverable only from the
    /// bind-time half of the payload.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    /// <remarks>
    /// NO RETAINED REFERENCE IS EVER TAGGED STATIC, AND THAT IS THE MECHANISM RATHER THAN AN OMISSION.
    /// The parse substitutes a static reference away before publishing its reference lists
    /// [:L1435, :L1509-L1511], so a <c>VarData</c> tagged <c>Static</c> would mean a static reference had
    /// survived binding - which would BE the bug this region exists to catch. <c>Static</c> is in the
    /// alphabet because a consumer must be able to name what happened to the reference, and the payload
    /// lets it: the unexpanded text still spells <c>$本月读数</c> and the bind-time snapshot still carries
    /// the value that was baked in.
    /// </remarks>
    [Fact]
    public async Task AMixedExpressionCarriesItsTwoReferencesWithTwoDifferentModes()
    {
        WireClient client = Client();
        Session session = await ReadySessionAsync(client, host);

        await DefineVariableAsync(client, session, LastMonth, "5");
        await DefineVariableAsync(client, session, ThisMonth, "1");

        // docs/n_cst_dwsvc_columnexp.md:L68 - one dynamic reference and one static reference, in one
        // expression, which is what makes an expression-level mode impossible.
        const string Mixed = "$$" + LastMonth + " + $" + ThisMonth;

        _ = await AddExpressionAsync(client, session.SessionId, session.Handle, "n3", Mixed);

        GetExpressionStateResponse state = await StateAsync(client, session);
        ColumnExpData data = Assert.Single(state.Expressions);

        // THE IN-PLACE REWRITE [:L1435]. The static reference is gone from the stored text, replaced by
        // its bind-time value inside the mandatory parenthesis wrap; the dynamic one is untouched.
        Assert.Equal("$$" + LastMonth + " + (1)", data.Exp);
        Assert.DoesNotContain("$" + ThisMonth, data.Exp, StringComparison.Ordinal);

        // ONE retained reference, tagged dynamic, naming the variable it will resolve at calculation time.
        VarData retained = Assert.Single(data.Vars);

        Assert.Equal(LastMonth, retained.Name);
        Assert.Equal("$$" + LastMonth, retained.FullName);
        Assert.Equal(ExpansionMode.Dynamic, retained.ExpansionMode);
        Assert.True(retained.IsMacro);
        Assert.False(retained.IsCtx);

        // The static reference is absent from the retained list - see the remarks.
        Assert.DoesNotContain(data.Vars, reference => reference.Name == ThisMonth);
        Assert.DoesNotContain(data.Vars, reference => reference.ExpansionMode == ExpansionMode.Static);

        // ... and recoverable from the bind-time half of the payload, which is the whole reason that half
        // exists. BOTH names appear here: the static one because its text was baked in, the dynamic one
        // because recording what its value WAS is what makes the pair comparable on the wire.
        ExpressionBinding binding = Assert.Single(state.Bindings);

        Assert.Equal(Mixed, binding.UnexpandedExp);
        Assert.Contains("$" + ThisMonth, binding.UnexpandedExp, StringComparison.Ordinal);
        Assert.Equal("5", binding.BindTimeSnapshot[LastMonth].StringValue);
        Assert.Equal("1", binding.BindTimeSnapshot[ThisMonth].StringValue);
        Assert.Equal(
            ExpansionMode.Dynamic,
            Assert.Single(binding.Vars, reference => reference.Name == LastMonth).ExpansionMode);
    }

    /// <summary>
    /// Round-trips the three-part payload: recalculates from what the wire delivered, mutates one
    /// variable of each kind, and asserts the dynamic result moves while the static one is frozen.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    /// <remarks>
    /// THE DOCUMENTATION'S OWN WORKED EXAMPLE, TAKEN LITERALLY. It fixes a static result at 5 permanently
    /// and shows the identical mutation yielding 6 through a dynamic reference
    /// [docs/n_cst_dwsvc_columnexp.md:sections 静态展开 / 动态展开]. The arithmetic here is that example
    /// with both halves in one expression so the two behaviours are observed against one payload: the sum
    /// moves when the dynamic operand is reassigned and does NOT move when the static one is.
    /// </remarks>
    [Fact]
    public async Task TheThreePartPayloadCrossesTheBoundaryAndTheGoldenPairHolds()
    {
        WireClient client = Client();
        Session session = await ReadySessionAsync(client, host);

        await DefineVariableAsync(client, session, LastMonth, "5");
        await DefineVariableAsync(client, session, ThisMonth, "1");

        _ = await AddExpressionAsync(
            client,
            session.SessionId,
            session.Handle,
            "n3",
            "$$" + LastMonth + " + $" + ThisMonth);

        // ALL THREE PARTS, BEFORE ANY MUTATION.
        ExpressionBinding bound = Assert.Single((await StateAsync(client, session)).Bindings);

        Assert.NotEmpty(bound.UnexpandedExp);
        Assert.Equal(2, bound.BindTimeSnapshot.Count);
        Assert.Equal("5", bound.LiveEnvironment[LastMonth].StringValue);
        Assert.Equal("1", bound.LiveEnvironment[ThisMonth].StringValue);

        Assert.Equal("6", DecimalTextOf(await CalcColumnAsync(client, session.SessionId, session.Handle, "n3")));

        // The DYNAMIC operand is resolved at calculation time [:L2189-L2200], so reassigning it moves the
        // result.
        await DefineVariableAsync(client, session, LastMonth, "6", replace: true);

        Assert.Equal("7", DecimalTextOf(await CalcColumnAsync(client, session.SessionId, session.Handle, "n3")));

        // The STATIC operand was substituted at bind time [:L1435], so reassigning it CANNOT reach the
        // stored expression. 7 and not 106.
        await DefineVariableAsync(client, session, ThisMonth, "100", replace: true);

        Assert.Equal("7", DecimalTextOf(await CalcColumnAsync(client, session.SessionId, session.Handle, "n3")));

        // AND THE TWO MAPS HAVE NOW GENUINELY DIVERGED, which is the proof that they are two things and
        // not one field projected twice: the snapshot still reports what was baked in, the environment
        // reports what is live. A payload carrying only the expanded string could report neither.
        ExpressionBinding after = Assert.Single((await StateAsync(client, session)).Bindings);

        Assert.Equal("5", after.BindTimeSnapshot[LastMonth].StringValue);
        Assert.Equal("1", after.BindTimeSnapshot[ThisMonth].StringValue);
        Assert.Equal("6", after.LiveEnvironment[LastMonth].StringValue);
        Assert.Equal("100", after.LiveEnvironment[ThisMonth].StringValue);
        Assert.Equal(
            "$$" + LastMonth + " + $" + ThisMonth,
            after.UnexpandedExp);
    }

    /// <summary>
    /// Asserts the three macro modes are distinguishable on the wire from the function reference's own
    /// shape, and that the argument lists carry the legacy's substitution asymmetry.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    /// <remarks>
    /// <para>
    /// <c>FuncData</c> CARRIES NO MODE FIELD, DELIBERATELY, because the legacy stores none: it derives
    /// <c>funcdata.builtin</c> from the doubled sigil [:L1311] and then dispatches on the resulting NAME
    /// [:L1386-L1401], so the pair (name, builtin) already determines the arm. The three arms are
    /// <c>FUNC_VAR</c> - the empty name - for a variable lookup [:L1388], <c>FUNC_INVOKE</c> for dynamic
    /// dispatch [:L1393], and any other name for an application macro [:L2286-L2287]. Each is asserted
    /// through that pair here; the mode ITSELF is tagged on the wire where the invocation crosses it, as
    /// <c>InvokeMethodRequest.form</c> - see the Phase 4 region.
    /// </para>
    /// <para>
    /// THE ARGUMENT ASYMMETRY IS LEGACY BEHAVIOUR AND IS ASSERTED AS SUCH. A static variable inside a
    /// DIRECT macro's argument list is substituted like any other static reference, so
    /// <c>$FormatPrice(n2, $精度)</c> stores <c>(2)</c>. Inside <c>$$Invoke</c>'s argument list the text is
    /// retained verbatim - <c>$精度</c> survives unsubstituted - because those arguments are re-based and
    /// forwarded at invocation time rather than expanded at bind time.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TheThreeMacroModesAreDistinguishableByTheirFunctionReferenceShape()
    {
        WireClient client = Client();
        Session session = await ReadySessionAsync(client, host);

        await DefineVariableAsync(client, session, Precision, "2");
        await DefineVariableAsync(client, session, FormatterName, "'" + FormatPrice + "'");

        _ = await AddExpressionAsync(
            client,
            session.SessionId,
            session.Handle,
            "n1",
            "$" + FormatPrice + "(n2, $" + Precision + ")");

        _ = await AddExpressionAsync(
            client,
            session.SessionId,
            session.Handle,
            "n2",
            "$$Invoke($" + FormatterName + ", n2, $" + Precision + ")");

        _ = await AddExpressionAsync(
            client,
            session.SessionId,
            session.Handle,
            "n3",
            "$$('" + Precision + "')");

        GetExpressionStateResponse state = await StateAsync(client, session);

        Assert.Equal(3, state.Expressions.Count);
        Assert.All(state.Expressions, data => Assert.True(data.HasMacro));

        // MACRO_DIRECT - a named function the APPLICATION owns [:L2286-L2287]. `builtin` is false because
        // the sigil was single.
        FuncData direct = Assert.Single(SingleExpressionFor(state, "n1").Fns);

        Assert.Equal(FormatPrice, direct.Name);
        Assert.False(direct.Builtin);
        Assert.Equal(["n2", "(2)"], direct.Args);
        Assert.Equal("$" + FormatPrice + "(n2, (2))", SingleExpressionFor(state, "n1").Exp);

        // MACRO_DYNAMIC - FUNC_INVOKE, the callee named by an argument [:L1393, :L2258].
        FuncData dynamic = Assert.Single(SingleExpressionFor(state, "n2").Fns);

        Assert.Equal("Invoke", dynamic.Name);
        Assert.True(dynamic.Builtin);
        Assert.Equal(["('" + FormatPrice + "')", "n2", "$" + Precision], dynamic.Args);

        // DYNAMIC_INDIRECT - FUNC_VAR, the EMPTY name, the variable named at run time [:L1388, :L2239].
        FuncData indirect = Assert.Single(SingleExpressionFor(state, "n3").Fns);

        Assert.Equal(string.Empty, indirect.Name);
        Assert.True(indirect.Builtin);
        Assert.Equal(["'" + Precision + "'"], indirect.Args);

        // The three shapes are pairwise distinct, which is what makes the pair (name, builtin) a
        // sufficient discriminator without a mode field.
        Assert.Equal(
            3,
            new HashSet<(string Name, bool Builtin)>
            {
                (direct.Name, direct.Builtin),
                (dynamic.Name, dynamic.Builtin),
                (indirect.Name, indirect.Builtin),
            }.Count);

        // The sentinels the discrimination rests on are published rather than assumed by the consumer.
        Assert.Equal(string.Empty, state.Sentinels.FuncVar);
        Assert.Equal("Invoke", state.Sentinels.FuncInvoke);
        Assert.Equal("$", state.Sentinels.MacroFlag);
        Assert.Equal("@", state.Sentinels.MacroContext);
    }

    /// <summary>
    /// Asserts the typed variable environment crosses the wire as a discriminated union, so a string
    /// holding <c>"5"</c> is distinguishable from a number holding 5.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    /// <remarks>
    /// <para>
    /// AAP 0.6.2.4 requires this shape rather than a stringly-typed map, because the seven typed
    /// <c>of_addvar</c> overloads [:L153-L159] are what the coercion behaviour depends on. The proof is
    /// taken twice: once at the encoding, where a genuine protobuf round trip preserves the arm, and once
    /// through the service, where the string arm is stored QUOTED and the numeric arm bare - the legacy
    /// string overload has no body of its own and delegates to <c>of_AddVarExp(name, "'" + value + "'")</c>.
    /// </para>
    /// <para>
    /// TWO ARMS RENDER IDENTICALLY AND THAT IS NOT A DEFECT IN THE UNION. A long 5 and a double 5 both
    /// store as <c>5</c>, because the legacy renders a variable's value as an expression and the two
    /// expressions coincide. The arms remain distinct ON THE WIRE, which is exactly the property a
    /// stringly-typed map would have destroyed.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TheTypedVariableUnionCrossesTheWireAsADiscriminatedUnion()
    {
        Assert.Equal(
            new[]
            {
                "time_value",
                "string_value",
                "long_value",
                "double_value",
                "datetime_value",
                "date_value",
                "bool_value",
            },
            Assert.Single(VarValue.Descriptor.Oneofs).Fields.Select(field => field.Name));

        VarValue text = new() { StringValue = "5" };
        VarValue number = new() { LongValue = 5L };
        VarValue real = new() { DoubleValue = 5d };

        // THE ENCODING KEEPS THEM APART. Same denoted digit, three different arms, all three surviving a
        // genuine serialize-parse cycle.
        Assert.Equal(
            VarValue.KindOneofCase.StringValue,
            VarValue.Parser.ParseFrom(text.ToByteArray()).KindCase);
        Assert.Equal(
            VarValue.KindOneofCase.LongValue,
            VarValue.Parser.ParseFrom(number.ToByteArray()).KindCase);
        Assert.Equal(
            VarValue.KindOneofCase.DoubleValue,
            VarValue.Parser.ParseFrom(real.ToByteArray()).KindCase);
        Assert.NotEqual(text.ToByteArray(), number.ToByteArray());

        // AND THE SERVICE KEEPS THEM APART TOO, in the way the legacy does: quoted against bare.
        WireClient client = Client();
        Session session = await ReadySessionAsync(client, host);

        await AddTypedVariableAsync(client, session, "asText", text);
        await AddTypedVariableAsync(client, session, "asLong", number);
        await AddTypedVariableAsync(client, session, "asDouble", real);

        Assert.Equal("'5'", await VariableExpressionAsync(client, session, "asText"));
        Assert.Equal("5", await VariableExpressionAsync(client, session, "asLong"));
        Assert.Equal("5", await VariableExpressionAsync(client, session, "asDouble"));
    }

    /// <summary>
    /// Asserts the seven legacy structures are each represented as a message, and that the foreign one
    /// carries an index plus a session-scoped handle rather than a serialized object pointer.
    /// </summary>
    /// <remarks>
    /// THE FIELD COUNT ON <c>ColumnExpData</c> IS PINNED AT NINETEEN because the oracle's structure has
    /// exactly nineteen fields [:L22-L42], and the unexpanded expression text - which the oracle never
    /// needed, having no boundary to cross - is deliberately carried on <c>ExpressionBinding</c> instead
    /// of smuggled in as a twentieth. The count is the audit that it stayed out.
    /// </remarks>
    [Fact]
    public void TheSevenLegacyStructuresAreMessagesAndTheForeignOneCarriesAHandleNotAPointer()
    {
        // columnexpdata [:L22-L42], columndata [:L44-L48], vardata [:L50-L56], funcdata [:L58-L63],
        // globalvardata [:L65-L71], localvardata [:L73-L78], foreignvardata [:L80-L83].
        Assert.Equal(
            new[]
            {
                "dataservices.v1.ColumnExpData",
                "dataservices.v1.ColumnData",
                "dataservices.v1.VarData",
                "dataservices.v1.FuncData",
                "dataservices.v1.GlobalVarData",
                "dataservices.v1.LocalVarData",
                "dataservices.v1.ForeignVarRef",
            },
            new[]
            {
                ColumnExpData.Descriptor,
                ColumnData.Descriptor,
                VarData.Descriptor,
                FuncData.Descriptor,
                GlobalVarData.Descriptor,
                LocalVarData.Descriptor,
                ForeignVarRef.Descriptor,
            }.Select(descriptor => descriptor.FullName));

        Assert.Equal(19, ColumnExpData.Descriptor.Fields.InDeclarationOrder().Count);

        // localvardata IS RECURSIVE [:L73-L78]: a variable's value may itself be an expression carrying
        // its own variable and function references.
        Assert.Equal(
            "dataservices.v1.VarData",
            LocalVarData.Descriptor.FindFieldByName("vars").MessageType.FullName);
        Assert.Equal(
            "dataservices.v1.FuncData",
            LocalVarData.Descriptor.FindFieldByName("fns").MessageType.FullName);

        // THE HARD LIMIT, VISIBLE IN THE SCHEMA. `foreignvardata.expsvc` is a live object pointer at
        // source [:L80-L83]; a pointer cannot be serialized, so the message carries the foreign table's
        // one-based index and a SESSION-SCOPED HANDLE, plus the resolvability the session decided. Three
        // scalar fields and nothing that could carry an address - see the Phase 6 region for the
        // behavioural half of this narrowing (AAP 0.6.2.3, risk R2).
        Assert.Equal(
            new[] { "index", "foreign_datawindow_handle", "resolvable" },
            ForeignVarRef.Descriptor.Fields.InDeclarationOrder().Select(field => field.Name));
        Assert.Equal(
            new[] { FieldType.Int32, FieldType.String, FieldType.Bool },
            ForeignVarRef.Descriptor.Fields.InDeclarationOrder().Select(field => field.FieldType));
        Assert.DoesNotContain(
            ForeignVarRef.Descriptor.Fields.InDeclarationOrder(),
            field => field.FieldType == FieldType.Bytes || field.FieldType == FieldType.UInt64);

        // The two closed alphabets the structures carry, at their oracle values.
        Assert.Equal(0, (int)ColumnData.Types.CalcFlag.ClcUnknown);
        Assert.Equal(1, (int)ColumnData.Types.CalcFlag.ClcYes);
        Assert.Equal(2, (int)ColumnData.Types.CalcFlag.ClcNo);
        Assert.Equal(0, (int)GlobalVarData.Types.VarType.VarLocal);
        Assert.Equal(1, (int)GlobalVarData.Types.VarType.VarForeign);
    }

    /// <summary>Defines or reassigns a variable through its expression form.</summary>
    /// <param name="client">The client.</param>
    /// <param name="session">The session.</param>
    /// <param name="name">The variable name.</param>
    /// <param name="exp">The value expression.</param>
    /// <param name="replace">
    /// <see langword="true"/> to reassign an existing variable with <c>of_setvarexp</c>, otherwise
    /// <see langword="false"/> to define it with <c>of_addvarexp</c>.
    /// </param>
    /// <returns>A task representing the call.</returns>
    private static async Task DefineVariableAsync(
        WireClient client,
        Session session,
        string name,
        string exp,
        bool replace = false)
    {
        if (replace)
        {
            SetVariableExpressionResponse reassigned = await client.SetVariableExpressionAsync(
                new SetVariableExpressionRequest
                {
                    SessionId = session.SessionId,
                    DatawindowHandle = session.Handle,
                    Name = name,
                    Exp = exp,
                    Recalc = true,
                },
                cancellationToken: Ct);

            Assert.Equal(WireRetCode.Ok, reassigned.RetCode);

            return;
        }

        AddVariableExpressionResponse defined = await client.AddVariableExpressionAsync(
            new AddVariableExpressionRequest
            {
                SessionId = session.SessionId,
                DatawindowHandle = session.Handle,
                Name = name,
                Exp = exp,
            },
            cancellationToken: Ct);

        Assert.Equal(WireRetCode.Ok, defined.RetCode);
    }

    /// <summary>Defines a variable from one arm of the typed union.</summary>
    /// <param name="client">The client.</param>
    /// <param name="session">The session.</param>
    /// <param name="name">The variable name.</param>
    /// <param name="value">The typed value.</param>
    /// <returns>A task representing the call.</returns>
    private static async Task AddTypedVariableAsync(
        WireClient client,
        Session session,
        string name,
        VarValue value)
    {
        AddVariableResponse added = await client.AddVariableAsync(
            new AddVariableRequest
            {
                SessionId = session.SessionId,
                DatawindowHandle = session.Handle,
                Name = name,
                Value = value,
            },
            cancellationToken: Ct);

        Assert.Equal(WireRetCode.Ok, added.RetCode);
    }

    /// <summary>Reads one variable's stored value expression.</summary>
    /// <param name="client">The client.</param>
    /// <param name="session">The session.</param>
    /// <param name="name">The variable name.</param>
    /// <returns>The stored expression.</returns>
    private static async Task<string> VariableExpressionAsync(
        WireClient client,
        Session session,
        string name)
    {
        GetVariableExpressionResponse got = await client.GetVariableExpressionAsync(
            new GetVariableExpressionRequest
            {
                SessionId = session.SessionId,
                DatawindowHandle = session.Handle,
                Name = name,
            },
            cancellationToken: Ct);

        Assert.Equal(WireRetCode.Ok, got.RetCode);
        Assert.Equal(got.Exp, got.Local.Exp);

        return got.Exp;
    }

    /// <summary>Selects the one expression bound to a column from a state payload.</summary>
    /// <param name="state">The state.</param>
    /// <param name="column">The column name.</param>
    /// <returns>That column's single expression.</returns>
    private static ColumnExpData SingleExpressionFor(GetExpressionStateResponse state, string column) =>
        Assert.Single(
            state.Expressions,
            data => string.Equals(data.Name, column, StringComparison.Ordinal));


    // =================================================================================================
    //  PHASE 4 - InvokeMethodChannel: THE FIRST INVERTED STREAM
    //  -----------------------------------------------------------------------------------------------
    //  THE INVERSION IS STRUCTURALLY REQUIRED, NOT A PREFERENCE. The legacy expects the APPLICATION to
    //  implement the macro switch: `se_cst_dw.sru:L14` declares
    //  `oncolumnexpinvokemethod(row, dwo, name, string args[]) returns any` and
    //  docs/n_cst_dwsvc_columnexp.md:section 宏函数 shows the handler as a `choose case` on `name` inside
    //  the DataWindow's own event. In process that is a call the engine makes into its owner. Across a
    //  service boundary the owner is on the other side, so DataServices must call BACK into its client -
    //  which is why this rpc's request message is the RESPONSE type and its response message is the
    //  REQUEST type. That inversion is asserted from the descriptor as well as from behaviour.
    // =================================================================================================

    /// <summary>
    /// Asserts the channel is inverted in the schema itself and that the calculation genuinely blocks on
    /// the client's answer.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    /// <remarks>
    /// THE WAITING ASSERTION IS THE ONE A DIRECT INVOCATION CANNOT MAKE. Invoked directly, both halves of
    /// the exchange run on the test's own thread, so "the calculation waits for the answer" is vacuous.
    /// Here the unary <c>Calc</c> is a separate HTTP/2 stream, and observing that its task has NOT
    /// completed while the question is in the client's hands is a real observation about the service.
    /// </remarks>
    [Fact]
    public async Task TheInvokeMethodChannelIsInvertedAndTheCalculationWaitsForTheClientsAnswer()
    {
        MethodDescriptor channel = WireService.Descriptor.FindMethodByName("InvokeMethodChannel");

        // THE INVERSION, VISIBLE IN THE CONTRACT: what the client sends is the *Response* and what it
        // receives is the *Request*, because the server is the party doing the asking.
        Assert.Equal("dataservices.v1.InvokeMethodResponse", channel.InputType.FullName);
        Assert.Equal("dataservices.v1.InvokeMethodRequest", channel.OutputType.FullName);
        Assert.True(channel.IsClientStreaming);
        Assert.True(channel.IsServerStreaming);

        WireClient client = Client();
        Session session = await MacroSessionAsync(client, host);

        _ = await AddExpressionAsync(
            client,
            session.SessionId,
            session.Handle,
            "n3",
            "$" + FormatPrice + "(n2, $" + Precision + ")");

        using AsyncDuplexStreamingCall<InvokeMethodResponse, InvokeMethodRequest> macros =
            client.InvokeMethodChannel(
                MacroHeaders(session.SessionId, session.Handle),
                cancellationToken: Ct);

        // The attachment is a SERVER-side event, so it is awaited rather than assumed - see
        // WaitForAttachedServicerAsync.
        await WaitForAttachedServicerAsync(host);

        Task<CalcResponse> calculating =
            CalcColumnAsync(client, session.SessionId, session.Handle, "n3");

        Assert.True(await macros.ResponseStream.MoveNext(Ct));

        InvokeMethodRequest ask = macros.ResponseStream.Current;

        // BLOCKED. The question is out and the answer has not been given, so the calculation cannot have
        // finished - and it must not have guessed.
        Assert.False(calculating.IsCompleted);

        await AnswerAsync(macros, ask, 42d);

        // The answer flows back INTO the calculation: 42 is the macro's return, not the column's loaded 1.
        Assert.Equal("42", DecimalTextOf(await calculating));

        await macros.RequestStream.CompleteAsync();
    }

    /// <summary>
    /// Asserts the documented direct form reaches the client with the identity and the arguments the
    /// legacy handler expects.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    /// <remarks>
    /// ONE-BASED AT SOURCE, ZERO-BASED ON THE WIRE, AND THE MAPPING IS THE POINT. The documented handler
    /// reads <c>Round(Double(args[1]), Long(args[2]))</c> [docs/n_cst_dwsvc_columnexp.md:section 宏函数],
    /// so the first written argument is <c>args[1]</c> and the second is <c>args[2]</c>. A protobuf
    /// repeated field is zero-based, so those are positions 0 and 1 here IN THE SAME ORDER - the
    /// renumbering is a representation change and never a reordering. The arguments are also already
    /// EVALUATED [:L2227-L2236]: <c>n2</c> arrives as the row's value and <c>$精度</c> as the number it
    /// was bound to, so a client's handler coerces text and never evaluates an expression.
    /// </remarks>
    [Fact]
    public async Task TheDirectFormReachesTheClientWithItsArgumentsEvaluatedAndInOrder()
    {
        WireClient client = Client();
        Session session = await MacroSessionAsync(client, host);

        _ = await AddExpressionAsync(
            client,
            session.SessionId,
            session.Handle,
            "n3",
            "$" + FormatPrice + "(n2, $" + Precision + ")");

        // The static argument was substituted at bind time, so the STORED text already carries the value
        // inside the mandatory parenthesis wrap.
        Assert.Equal(
            "$" + FormatPrice + "(n2, (2))",
            SingleExpressionFor(await StateAsync(client, session), "n3").Exp);

        using AsyncDuplexStreamingCall<InvokeMethodResponse, InvokeMethodRequest> macros =
            client.InvokeMethodChannel(
                MacroHeaders(session.SessionId, session.Handle),
                cancellationToken: Ct);

        // The attachment is a SERVER-side event, so it is awaited rather than assumed - see
        // WaitForAttachedServicerAsync.
        await WaitForAttachedServicerAsync(host);

        Task<CalcResponse> calculating =
            CalcColumnAsync(client, session.SessionId, session.Handle, "n3");

        Assert.True(await macros.ResponseStream.MoveNext(Ct));

        InvokeMethodRequest ask = macros.ResponseStream.Current;

        Assert.Equal(FormatPrice, ask.Name);
        Assert.Equal(ExpansionMode.MacroDirect, ask.Form);

        // `builtin` means "was written with $$" and NOT "the framework implements it" - the misleading
        // name is the legacy's own [:L1311], carried so a consumer can correlate the invocation with the
        // reference that produced it.
        Assert.False(ask.Builtin);
        Assert.Equal(1L, ask.Row);
        Assert.Equal("n3", ask.Dwo.Name);
        Assert.NotEmpty(ask.InvocationId);
        Assert.Equal(session.SessionId, ask.SessionId);
        Assert.Equal(session.Handle, ask.DatawindowHandle);

        // args[1] then args[2], in that order, both already values.
        Assert.Equal(["5", "2"], ask.Args);

        await AnswerAsync(macros, ask, 7d);

        Assert.Equal("7", DecimalTextOf(await calculating));
        await macros.RequestStream.CompleteAsync();
    }

    /// <summary>
    /// Asserts the dynamic form resolves its callee from the first argument and re-bases the remaining
    /// arguments to one before they cross the wire.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    /// <remarks>
    /// THE CLIENT SEES NO POSITIONAL DIFFERENCE BETWEEN THE TWO FORMS, AND THAT UNIFORMITY IS LEGACY
    /// BEHAVIOUR. The oracle takes the function name from <c>args[1]</c>, drops it, and renumbers the rest
    /// [:L2259-L2263], so the handler that services <c>$FormatPrice(a, b)</c> services
    /// <c>$$Invoke('FormatPrice', a, b)</c> unchanged. Three arguments are written here and two are
    /// delivered; the one that vanished is the callee's name, which arrives as <c>name</c> instead.
    /// </remarks>
    [Fact]
    public async Task TheDynamicFormResolvesItsCalleeFromTheFirstArgumentAndRebasesTheRest()
    {
        WireClient client = Client();
        Session session = await MacroSessionAsync(client, host);

        _ = await AddExpressionAsync(
            client,
            session.SessionId,
            session.Handle,
            "n3",
            "$$Invoke($$" + FormatterName + ", n2, $$" + Precision + ")");

        // Both references are retained and both are dynamic, so the variable pass rewrites them into the
        // function's argument list before the arguments are evaluated [:L2215-L2223].
        ColumnExpData stored = SingleExpressionFor(await StateAsync(client, session), "n3");

        Assert.Equal(
            new[] { FormatterName, Precision },
            stored.Vars.Select(reference => reference.Name));
        Assert.All(
            stored.Vars,
            reference => Assert.Equal(ExpansionMode.Dynamic, reference.ExpansionMode));
        Assert.Equal(3, Assert.Single(stored.Fns).Args.Count);

        using AsyncDuplexStreamingCall<InvokeMethodResponse, InvokeMethodRequest> macros =
            client.InvokeMethodChannel(
                MacroHeaders(session.SessionId, session.Handle),
                cancellationToken: Ct);

        // The attachment is a SERVER-side event, so it is awaited rather than assumed - see
        // WaitForAttachedServicerAsync.
        await WaitForAttachedServicerAsync(host);

        Task<CalcResponse> calculating =
            CalcColumnAsync(client, session.SessionId, session.Handle, "n3");

        Assert.True(await macros.ResponseStream.MoveNext(Ct));

        InvokeMethodRequest ask = macros.ResponseStream.Current;

        // RESOLVED, not the sentinel and not the variable that held it.
        Assert.Equal(FormatPrice, ask.Name);
        Assert.NotEqual("Invoke", ask.Name);
        Assert.Equal(ExpansionMode.MacroDynamic, ask.Form);
        Assert.True(ask.Builtin);

        // RE-BASED: three written, two delivered, and the callee's name is not among them.
        Assert.Equal(["5", "2"], ask.Args);
        Assert.DoesNotContain(FormatPrice, ask.Args);

        await AnswerAsync(macros, ask, 11d);

        Assert.Equal("11", DecimalTextOf(await calculating));
        await macros.RequestStream.CompleteAsync();
    }

    /// <summary>
    /// Characterises the documented mixed dynamic form, whose later argument keeps its unexpanded macro
    /// and therefore aborts the calculation before any invocation is issued.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    /// <remarks>
    /// <para>
    /// PRESERVED, NOT CORRECTED (constraint C-B, goal G2). The documentation writes this exact expression
    /// [docs/n_cst_dwsvc_columnexp.md:section 动态调用函数], and what the engine stores for it is
    /// <c>$$Invoke(('FormatPrice'), n2, $精度)</c>: the FIRST argument's static reference was expanded and
    /// the THIRD one's was not, and neither is retained in <c>vars</c>. The static rewrite re-anchors the
    /// scanner as it substitutes [:L1435-L1437], so the reference after the substitution point is left in
    /// the captured argument text. At calculation time every argument is evaluated before dispatch and a
    /// failure aborts the whole calculation [:L2227-L2236], so <c>$精度</c> - which is not a DataWindow
    /// expression - takes the abort path.
    /// </para>
    /// <para>
    /// The consequences asserted here are the ones a caller can see: a defined preprocessing error naming
    /// the offending sub-expression, NO invocation issued at all, and the column's value untouched. The
    /// all-dynamic spelling is the form that reaches the client, and the test above uses it.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TheDocumentedMixedDynamicFormAbortsOnItsUnexpandedArgumentAndIsPreserved()
    {
        WireClient client = Client();
        Session session = await MacroSessionAsync(client, host);

        const string Documented = "$$Invoke($" + FormatterName + ", n2, $" + Precision + ")";

        _ = await AddExpressionAsync(client, session.SessionId, session.Handle, "n3", Documented);

        ColumnExpData stored = SingleExpressionFor(await StateAsync(client, session), "n3");

        Assert.Equal("$$Invoke(('" + FormatPrice + "'), n2, $" + Precision + ")", stored.Exp);
        Assert.Equal(
            ["('" + FormatPrice + "')", "n2", "$" + Precision],
            Assert.Single(stored.Fns).Args);
        Assert.Empty(stored.Vars);

        using AsyncDuplexStreamingCall<InvokeMethodResponse, InvokeMethodRequest> macros =
            client.InvokeMethodChannel(
                MacroHeaders(session.SessionId, session.Handle),
                cancellationToken: Ct);

        // The attachment is a SERVER-side event, so it is awaited rather than assumed - see
        // WaitForAttachedServicerAsync.
        await WaitForAttachedServicerAsync(host);

        CalcResponse calculated =
            await CalcColumnAsync(client, session.SessionId, session.Handle, "n3");

        // A defined refusal, in band, naming the sub-expression that could not be evaluated.
        Assert.Equal(ExpressionError.Types.Category.Preprocess, calculated.Error.Category);
        Assert.Contains("$" + Precision, calculated.Error.Error.Text, StringComparison.Ordinal);

        // The column keeps what it was loaded with: nothing was written and nothing was fabricated.
        Assert.Equal("1", DecimalTextOf(calculated));

        // AND NO INVOCATION WAS EVER ISSUED. The channel is drained by completing it and reading to the
        // end, which cannot block because the server has nothing to send.
        await macros.RequestStream.CompleteAsync();
        Assert.Empty(await DrainAsync(macros));
    }

    /// <summary>
    /// Asserts that a rejected answer and an unhandled answer both reach the legacy's own "invalid return
    /// value" refusal, and that neither writes a value.
    /// </summary>
    /// <param name="unhandled">
    /// <see langword="true"/> to answer <c>unhandled</c>, otherwise <see langword="false"/> to answer with
    /// a rejected result.
    /// </param>
    /// <returns>A task representing the test.</returns>
    /// <remarks>
    /// THE TWO FIELDS ARE DISTINCT AND THEIR DESTINATION IS THE SAME, DELIBERATELY. <c>rejected</c> means
    /// the handler ran and returned an unusable type [:L2282, :L2306]; <c>unhandled</c> means the client
    /// has no handler for the name at all, which the legacy cannot express because in process a
    /// fallen-through <c>choose case</c> returns null - and a null return matches no arm, so it reaches
    /// the identical refusal. Keeping the fields apart lets a client say "not mine" instead of fabricating
    /// a null; making them converge is what preserves the legacy's single observable outcome.
    /// </remarks>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnInvalidAnswerIsRefusedWithTheLegacyMessageAndNeverBecomesAValue(bool unhandled)
    {
        WireClient client = Client();
        Session session = await MacroSessionAsync(client, host);

        _ = await AddExpressionAsync(
            client,
            session.SessionId,
            session.Handle,
            "n3",
            "$" + FormatPrice + "(n2, $" + Precision + ")");

        using AsyncDuplexStreamingCall<InvokeMethodResponse, InvokeMethodRequest> macros =
            client.InvokeMethodChannel(
                MacroHeaders(session.SessionId, session.Handle),
                cancellationToken: Ct);

        // The attachment is a SERVER-side event, so it is awaited rather than assumed - see
        // WaitForAttachedServicerAsync.
        await WaitForAttachedServicerAsync(host);

        Task<CalcResponse> calculating =
            CalcColumnAsync(client, session.SessionId, session.Handle, "n3");

        Assert.True(await macros.ResponseStream.MoveNext(Ct));

        InvokeMethodRequest ask = macros.ResponseStream.Current;

        await macros.RequestStream.WriteAsync(
            unhandled
                ? new InvokeMethodResponse { InvocationId = ask.InvocationId, Unhandled = true }
                : new InvokeMethodResponse
                {
                    InvocationId = ask.InvocationId,
                    Result = new MacroResult { Rejected = true },
                },
            Ct);

        CalcResponse calculated = await calculating;

        // The refusal is IN BAND: a rejected macro is an expression error, not a transport failure, so the
        // rpc itself succeeds and the diagnosis travels in the payload.
        Assert.Equal(WireRetCode.Ok, calculated.RetCode);
        Assert.Equal(ExpressionError.Types.Category.InvalidValue, calculated.Error.Category);

        // The legacy's own wording, hardcoded Chinese and NOT localized - defect (f), reproduced.
        Assert.Contains(
            "函数[" + FormatPrice + "],返回值无效",
            calculated.Error.Error.Text,
            StringComparison.Ordinal);
        Assert.False(calculated.Error.Error.Localized);

        // Nothing was written and nothing was invented.
        Assert.Equal("1", DecimalTextOf(calculated));
        Assert.Equal(string.Empty, Assert.Single(calculated.Results).EvaluatedExp);

        await macros.RequestStream.CompleteAsync();
    }

    /// <summary>
    /// Asserts the invocation backstop abandons a silent client without fabricating a value, driven by the
    /// substituted clock rather than by waiting.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    /// <remarks>
    /// <para>
    /// A PRIVATE HOST, BECAUSE THIS TEST MOVES THE CLOCK. <c>MacroInvoker</c> arms its deadline with
    /// <c>new CancellationTokenSource(timeout, timeProvider)</c> and the fixture's clock overrides
    /// <c>CreateTimer</c>, so advancing it fires the backstop deterministically with no wall-clock wait -
    /// which is what keeps this suite reproducible. The advance would also age every other session in a
    /// shared host past its idle limit, so the host is this test's own.
    /// </para>
    /// <para>
    /// THE ABORT IS SILENT ON THE WIRE, AND THAT IS THE LEGACY'S SHAPE. A timed-out invocation carries
    /// <c>E_TIME_OUT</c> in process but no structured error, so preprocessing takes the same abort path
    /// the oracle takes on an empty rendering - <c>if sVal = "" then return ""</c> [:L2201] - which raises
    /// nothing. What a caller observes is therefore a calculation that produced NOTHING: no value written,
    /// no evaluated expression, and emphatically not a default standing in for the answer that never came.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ASilentClientIsAbandonedByTheBackstopWithoutFabricatingAValue()
    {
        using DataServicesTestHostFactory own = new();

        WireClient client = own.CreateColumnExpressionClient();
        Session session = await MacroSessionAsync(client, own);

        _ = await AddExpressionAsync(
            client,
            session.SessionId,
            session.Handle,
            "n3",
            "$" + FormatPrice + "(n2, $" + Precision + ")");

        using AsyncDuplexStreamingCall<InvokeMethodResponse, InvokeMethodRequest> macros =
            client.InvokeMethodChannel(
                MacroHeaders(session.SessionId, session.Handle),
                cancellationToken: Ct);

        // The attachment is a SERVER-side event, so it is awaited rather than assumed - see
        // WaitForAttachedServicerAsync.
        await WaitForAttachedServicerAsync(own);

        Task<CalcResponse> calculating =
            CalcColumnAsync(client, session.SessionId, session.Handle, "n3");

        Assert.True(await macros.ResponseStream.MoveNext(Ct));
        Assert.Equal(FormatPrice, macros.ResponseStream.Current.Name);
        Assert.False(calculating.IsCompleted);

        // The client never answers. Move the clock past the deadline instead of waiting for it.
        own.Clock.Advance(SessionLifetimeOptions.DefaultIdleTimeout + TimeSpan.FromMinutes(1));

        CalcResponse calculated = await calculating;

        Assert.Equal(WireRetCode.Ok, calculated.RetCode);
        Assert.Equal("1", DecimalTextOf(calculated));
        Assert.Equal(string.Empty, Assert.Single(calculated.Results).EvaluatedExp);
        Assert.NotEqual("42", DecimalTextOf(calculated));

        await macros.RequestStream.CompleteAsync();
    }

    /// <summary>
    /// Asserts a client that closes the stream while an invocation is outstanding is refused with a
    /// status, and that the session and the router are left clean.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    /// <remarks>
    /// <para>
    /// THE PROPERTIES ARE PINNED AND THE PARTICULAR CODE IS NOT, DELIBERATELY. Disposing the registration
    /// fails the outstanding invocation with a protocol violation; the channel handler maps that violation
    /// to <c>FailedPrecondition</c>, but the violation raised during disposal surfaces on the AWAITING
    /// UNARY CALL, which sits outside that mapping and reports the generic server-fault status. Pinning
    /// that particular code would freeze an unmapped escape as though it were the contract. What IS the
    /// contract is asserted instead: the caller receives a status rather than a hang or a fabricated
    /// value, the session survives, and the router releases the slot so another servicer can attach and
    /// complete a calculation.
    /// </para>
    /// <para>
    /// A PRIVATE HOST, so an abandoned registration cannot reach another test.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AClientThatClosesMidInvocationIsRefusedAndLeavesNoSessionOrSlotBehind()
    {
        using DataServicesTestHostFactory own = new();

        WireClient client = own.CreateColumnExpressionClient();
        Session session = await MacroSessionAsync(client, own);
        Metadata headers = MacroHeaders(session.SessionId, session.Handle);

        _ = await AddExpressionAsync(
            client,
            session.SessionId,
            session.Handle,
            "n3",
            "$" + FormatPrice + "(n2, $" + Precision + ")");

        using (AsyncDuplexStreamingCall<InvokeMethodResponse, InvokeMethodRequest> abandoning =
            client.InvokeMethodChannel(headers, cancellationToken: Ct))
        {
            // The attach happens on the server's schedule, so wait for it before asking a question that
            // needs a servicer - see WaitForAttachedServicerAsync for what happens without this.
            await WaitForAttachedServicerAsync(own);

            Task<CalcResponse> calculating =
                CalcColumnAsync(client, session.SessionId, session.Handle, "n3");

            Assert.True(await abandoning.ResponseStream.MoveNext(Ct));

            // Walk away with the question unanswered.
            await abandoning.RequestStream.CompleteAsync();

            RpcException refusal = await Assert.ThrowsAsync<RpcException>(async () => await calculating);

            Assert.NotEqual(StatusCode.OK, refusal.StatusCode);
            Assert.True(Enum.IsDefined(refusal.StatusCode));
        }

        // THE SESSION SURVIVED. An abandoned macro servicer is not a reason to tear down the expression
        // service that was waiting on it.
        GetServiceStateResponse state = await client.GetServiceStateAsync(
            new GetServiceStateRequest
            {
                SessionId = session.SessionId,
                DatawindowHandle = session.Handle,
            },
            cancellationToken: Ct);

        Assert.Equal(WireRetCode.Ok, state.RetCode);
        Assert.True(state.Enabled);

        // AND THE SLOT WAS RELEASED: a second servicer attaches and the calculation completes normally,
        // which it could not do if the abandoned registration had leaked. The release happens in the
        // channel handler's own unwinding, on the server's schedule and not the client's, so the slot is
        // observed empty before the replacement is attached - see WaitForFreeServicerSlotAsync.
        await WaitForFreeServicerSlotAsync(own);

        using AsyncDuplexStreamingCall<InvokeMethodResponse, InvokeMethodRequest> replacement =
            client.InvokeMethodChannel(headers, cancellationToken: Ct);

        await WaitForAttachedServicerAsync(own);

        Task<CalcResponse> retried =
            CalcColumnAsync(client, session.SessionId, session.Handle, "n3");

        Assert.True(await replacement.ResponseStream.MoveNext(Ct));
        await AnswerAsync(replacement, replacement.ResponseStream.Current, 3d);

        Assert.Equal("3", DecimalTextOf(await retried));
        await replacement.RequestStream.CompleteAsync();
    }

    /// <summary>
    /// Asserts the channel identifies itself through call metadata and admits exactly one servicer at a
    /// time.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    /// <remarks>
    /// THE HEADERS ARE NOT A STYLE CHOICE. The client-to-server message on this rpc is an ANSWER, and an
    /// answer carries no session or handle - it cannot, because the invocation it answers was addressed by
    /// the server. So the channel has to be identified before the first message arrives, which is what the
    /// metadata is for. One servicer at a time follows from the same synchronous discipline: a second
    /// attachment would leave the first client's outstanding invocation permanently unanswered, so it is
    /// refused rather than queued.
    /// </remarks>
    [Fact]
    public async Task TheChannelRequiresItsCorrelationHeadersAndAdmitsOneServicerAtATime()
    {
        using DataServicesTestHostFactory own = new();

        WireClient client = own.CreateColumnExpressionClient();
        Session session = await MacroSessionAsync(client, own);
        Metadata headers = MacroHeaders(session.SessionId, session.Handle);

        _ = await AddExpressionAsync(
            client,
            session.SessionId,
            session.Handle,
            "n3",
            "$" + FormatPrice + "(n2, $" + Precision + ")");

        using (AsyncDuplexStreamingCall<InvokeMethodResponse, InvokeMethodRequest> unidentified =
            client.InvokeMethodChannel(cancellationToken: Ct))
        {
            RpcException missing = await Assert.ThrowsAsync<RpcException>(
                async () => await unidentified.ResponseStream.MoveNext(Ct));

            Assert.Equal(StatusCode.InvalidArgument, missing.StatusCode);
            Assert.Contains(
                ColumnExpressionChannelHeaders.SessionId,
                missing.Status.Detail,
                StringComparison.Ordinal);
            Assert.Contains(
                ColumnExpressionChannelHeaders.DataWindowHandle,
                missing.Status.Detail,
                StringComparison.Ordinal);
        }

        using AsyncDuplexStreamingCall<InvokeMethodResponse, InvokeMethodRequest> servicer =
            client.InvokeMethodChannel(headers, cancellationToken: Ct);

        // The attachment is a SERVER-side event, so it is awaited rather than assumed - see
        // WaitForAttachedServicerAsync.
        await WaitForAttachedServicerAsync(own);

        Task<CalcResponse> calculating =
            CalcColumnAsync(client, session.SessionId, session.Handle, "n3");

        // Waiting for the question proves the first attachment is registered before the second races it.
        Assert.True(await servicer.ResponseStream.MoveNext(Ct));

        using (AsyncDuplexStreamingCall<InvokeMethodResponse, InvokeMethodRequest> intruder =
            client.InvokeMethodChannel(headers, cancellationToken: Ct))
        {
            RpcException taken = await Assert.ThrowsAsync<RpcException>(
                async () => await intruder.ResponseStream.MoveNext(Ct));

            Assert.Equal(StatusCode.AlreadyExists, taken.StatusCode);
            Assert.Contains(session.Handle, taken.Status.Detail, StringComparison.Ordinal);
        }

        // The refusal did not disturb the servicer that was already there.
        await AnswerAsync(servicer, servicer.ResponseStream.Current, 9d);

        Assert.Equal("9", DecimalTextOf(await calculating));
        await servicer.RequestStream.CompleteAsync();
    }

    /// <summary>
    /// Opens a ready session and defines the two variables the documented macro examples use.
    /// </summary>
    /// <param name="client">The client.</param>
    /// <returns>The session.</returns>
    /// <remarks>
    /// THE VARIABLES ARE DEFINED THE WAY THE DOCUMENTATION DEFINES THEM - <c>of_SetVar("精度", 1)</c> and
    /// <c>of_SetVar("单价格式化", "FormatPrice")</c> [docs/n_cst_dwsvc_columnexp.md:section 宏函数], i.e.
    /// through the TYPED overloads rather than as value expressions, because the string overload's own
    /// quoting is what makes the callee name resolve to a bare name later.
    /// </remarks>
    private async Task<Session> MacroSessionAsync(
        WireClient client,
        DataServicesTestHostFactory? target = null)
    {
        Session session = await ReadySessionAsync(client, target);

        await AddTypedVariableAsync(client, session, Precision, new VarValue { LongValue = 2L });
        await AddTypedVariableAsync(
            client,
            session,
            FormatterName,
            new VarValue { StringValue = FormatPrice });

        return session;
    }

    /// <summary>Answers one macro invocation with a numeric value.</summary>
    /// <param name="macros">The attached channel.</param>
    /// <param name="ask">The invocation being answered.</param>
    /// <param name="value">The value the handler returns.</param>
    /// <returns>A task representing the write.</returns>
    /// <remarks>
    /// THE CORRELATION IDENTIFIER IS ECHOED AND NOT INVENTED. A single calculation may issue several
    /// invocations and a nested argument may issue another [:L2216-L2220], so the answer names the question
    /// it belongs to.
    /// </remarks>
    private static Task AnswerAsync(
        AsyncDuplexStreamingCall<InvokeMethodResponse, InvokeMethodRequest> macros,
        InvokeMethodRequest ask,
        double value) =>
        macros.RequestStream.WriteAsync(
            new InvokeMethodResponse
            {
                InvocationId = ask.InvocationId,
                Result = new MacroResult { Value = new WireAnyValue { DoubleValue = value } },
            },
            Ct);


    private static async Task WaitForFreeServicerSlotAsync(DataServicesTestHostFactory factory)
    {
        MacroInvocationRouter router = factory.Services.GetRequiredService<MacroInvocationRouter>();

        for (int attempt = 0; router.AttachedCount != 0; attempt++)
        {
            Assert.True(
                attempt < BarrierAttemptCeiling,
                "An abandoned macro invocation channel never released its servicer slot.");

            await Task.Yield();
        }
    }

    /// <summary>
    /// Waits until the server has REGISTERED a macro servicer, so a calculation started afterwards is
    /// guaranteed to find one attached.
    /// </summary>
    /// <param name="factory">The private host whose router is inspected.</param>
    /// <returns>A task that completes once one servicer is attached.</returns>
    /// <remarks>
    /// <para>
    /// THE MIRROR OF <see cref="WaitForFreeServicerSlotAsync"/>, AND IT EXISTS BECAUSE OF A MEASURED
    /// FAILURE RATHER THAN A THEORY. Opening the channel client-side does not mean the server handler has
    /// run: <c>InvokeMethodChannel</c> attaches from CALL METADATA before it reads the request stream, so
    /// registration happens on the SERVER'S schedule. A calculation started before that attach lands finds
    /// no servicer, and the router answers <c>Unhandled</c> rather than queueing the question - the
    /// deliberate reproduction of an unimplemented <c>OnColumnExpInvokeMethod</c> in-process. The
    /// calculation then completes with nothing asked, no invocation is ever pushed to the channel, and a
    /// <c>MoveNext</c> waiting for one blocks until the test's own cancellation deadline fires. That is
    /// exactly what was observed once under heavy host load: a hundred-second run ending in
    /// <c>Unavailable / the client aborted the request</c>.
    /// </para>
    /// <para>
    /// SO THE BARRIER IS A CORRECTNESS FIX TO THE TEST, NOT A TOLERANCE. It removes an assumption the
    /// transport never made; it does not wait "a bit" and hope, and it cannot mask a product fault -
    /// registration is read from the router's own state, and failing to observe it inside
    /// <see cref="BarrierAttemptCeiling"/> yields a named assertion rather than a hang.
    /// </para>
    /// <para>
    /// IT MEASURES NO WALL-CLOCK TIME AND ASSERTS NOTHING ABOUT DURATION. The loop yields, it does not
    /// sleep, and the ceiling is an attempt count rather than a deadline - so no performance objective is
    /// asserted anywhere in it (AAP 0.8.5).
    /// </para>
    /// </remarks>
    private static async Task WaitForAttachedServicerAsync(DataServicesTestHostFactory factory)
    {
        MacroInvocationRouter router = factory.Services.GetRequiredService<MacroInvocationRouter>();

        for (int attempt = 0; router.AttachedCount == 0; attempt++)
        {
            Assert.True(
                attempt < BarrierAttemptCeiling,
                "A macro invocation channel never attached its servicer, so a calculation started after it "
                    + "would wait out the macro backstop instead of being asked.");

            await Task.Yield();
        }
    }

    /// <summary>Reads a completed inverted stream to its end.</summary>
    /// <param name="macros">The channel, whose request stream the caller has already completed.</param>
    /// <returns>Every invocation the server issued, which is normally none.</returns>
    /// <remarks>
    /// SAFE BECAUSE THE REQUEST STREAM IS ALREADY COMPLETE. The server ends the call once it has read to
    /// the end of the answers, so this drain terminates without a timeout and asserting emptiness proves
    /// no invocation was issued rather than merely that none had arrived yet.
    /// </remarks>
    private static async Task<IReadOnlyList<InvokeMethodRequest>> DrainAsync(
        AsyncDuplexStreamingCall<InvokeMethodResponse, InvokeMethodRequest> macros)
    {
        List<InvokeMethodRequest> issued = [];

        while (await macros.ResponseStream.MoveNext(Ct))
        {
            issued.Add(macros.ResponseStream.Current);
        }

        return issued;
    }


    // =================================================================================================
    //  PHASE 5 - TraceChannel: THE SECOND INVERTED STREAM
    //  -----------------------------------------------------------------------------------------------
    //  The trace is the ONLY column-expression event assigned pattern (a) - a sequencing token rather
    //  than a synchronous chain (AAP 0.6.1.4) - and the reason is that it is pure diagnostics. Nothing in
    //  a calculation depends on a trace record being delivered, in order, or at all. Every assertion in
    //  this region is written so that it would fail if that ever stopped being true: the shape of the
    //  record is pinned exactly, and the calculation's own result is pinned independently of it.
    //
    //  ABSENCE IS ASSERTED WITH A SENTINEL AND NEVER WITH A TIMEOUT. A test that "waits a bit and checks
    //  nothing arrived" is a test whose result depends on machine speed. Offering one barrier record and
    //  asserting it is the NEXT thing read proves nothing was queued ahead of it, deterministically,
    //  because the stream is FIFO - see AssertNoTraceRecordAsync.
    // =================================================================================================

    /// <summary>
    /// Asserts <c>#Trace</c> is clear on a new service, that no record flows while it is clear, and that
    /// enabling it starts the flow.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    /// <remarks>
    /// THE PRESERVED DEFAULT IS FALSE, AND IT IS A PRESERVED DEFAULT RATHER THAN A CHOICE.
    /// <c>n_cst_dwsvc_columnexp</c> declares its trace flag with no initializer, so the oracle starts
    /// silent, and <c>DataServices:ColumnExpression:Trace</c> carries that same default forward. A service
    /// that traced by default would emit a diagnostic stream nobody asked for and would change the
    /// observable behaviour of every calculation in a recording.
    /// </remarks>
    [Fact]
    public async Task TheTraceIsSilentByDefaultAndFlowsOnlyOnceTraceIsEnabled()
    {
        WireClient client = Client();
        Session session = await ReadySessionAsync(client, host);

        // The shipped default, read back over the wire before anything touches it.
        GetServiceStateResponse fresh = await client.GetServiceStateAsync(
            new GetServiceStateRequest
            {
                SessionId = session.SessionId,
                DatawindowHandle = session.Handle,
            },
            cancellationToken: Ct);

        Assert.Equal(WireRetCode.Ok, fresh.RetCode);
        Assert.False(fresh.Trace);

        _ = await AddExpressionAsync(client, session.SessionId, session.Handle, "n3", "n1 + n2");

        using AsyncDuplexStreamingCall<TraceChannelRequest, TraceRecord> trace =
            await SubscribeTraceAsync(host, client, session);

        // SUBSCRIBING IS NOT ENABLING. The calculation runs and writes its value; the subscriber gets
        // nothing, because emission is gated on the engine's flag and not on the subscription.
        Assert.Equal(
            "15",
            DecimalTextOf(await CalcColumnAsync(client, session.SessionId, session.Handle, "n3")));

        await AssertNoTraceRecordAsync(host, trace, session);

        // Now enable it and the identical calculation traces.
        await SetTraceAsync(client, session, true);

        _ = await CalcColumnAsync(client, session.SessionId, session.Handle, "n3");

        TraceRecord traced = Assert.Single(await ReadTraceAsync(trace, 1));

        Assert.Equal("n3", traced.Dwo.Name);
        Assert.Equal("n1 + n2", traced.Expr);
        Assert.Equal("15", traced.Value);
    }

    /// <summary>
    /// Asserts the flattened call stack is the frames joined with <c>&gt;</c> and the calculated column
    /// appended last, with no trailing delimiter, at two and at three levels of nesting.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    /// <remarks>
    /// <para>
    /// THE ORACLE BUILDS THE STRING BY SUFFIXING EVERY FRAME AND THEN APPENDING THE COLUMN
    /// [n_cst_dwsvc_columnexp.sru:L752-L758], walking the calculation stack the vector container holds
    /// [ws_objects/pfw.utility.container.pbl.src/n_vector.sru]. So the terminal segment carries NO
    /// delimiter and a top-level calculation traces its own bare column name. The nesting here is produced
    /// the way a real one is - by relative-column dependencies - so the second and third records are
    /// genuinely nested calculations and not a fabricated stack.
    /// </para>
    /// <para>
    /// <c>stack_frames</c> IS ASSERTED ALONGSIDE, because the flattened form is ambiguous: a column named
    /// with a <c>&gt;</c> would split into two frames on parsing. The legacy cannot distinguish them
    /// either, so the flattened field is preserved verbatim for parity while the unflattened one is what a
    /// consumer should read - and the two are asserted to agree.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TheCallStackIsTheGreaterThanJoinedFormWithNoTrailingDelimiter()
    {
        WireClient client = Client();
        Session session = await ReadySessionAsync(client, host);

        await SetTraceAsync(client, session, true);

        // A three-deep chain: n1 is a constant, n2 follows n1, n3 follows n2.
        _ = await AddExpressionAsync(client, session.SessionId, session.Handle, "n1", "3");
        _ = await AddExpressionAsync(client, session.SessionId, session.Handle, "n2", "n1 + 1");
        _ = await AddExpressionAsync(client, session.SessionId, session.Handle, "n3", "n2 + 1");

        await BindRelativeColumnAsync(client, session, "n2", "n1");
        await BindRelativeColumnAsync(client, session, "n3", "n2");

        using AsyncDuplexStreamingCall<TraceChannelRequest, TraceRecord> trace =
            await SubscribeTraceAsync(host, client, session);

        _ = await CalcColumnAsync(client, session.SessionId, session.Handle, "n1");

        IReadOnlyList<TraceRecord> records = await ReadTraceAsync(trace, 3);

        // ONE LEVEL: the bare column name, no delimiter anywhere.
        Assert.Equal("n1", records[0].Stack);
        Assert.Empty(records[0].StackFrames);
        Assert.Equal(0, records[0].Depth);
        Assert.Equal("3", records[0].Value);

        // TWO LEVELS.
        Assert.Equal("n1>n2", records[1].Stack);
        Assert.Equal(["n1"], records[1].StackFrames);
        Assert.Equal(1, records[1].Depth);
        Assert.Equal("4", records[1].Value);

        // THREE LEVELS.
        Assert.Equal("n1>n2>n3", records[2].Stack);
        Assert.Equal(["n1", "n2"], records[2].StackFrames);
        Assert.Equal(2, records[2].Depth);
        Assert.Equal("5", records[2].Value);

        // The structural relation, asserted for every record rather than only for the three above: the
        // flattened form is exactly the frames plus the calculated column, in order.
        Assert.All(
            records,
            record =>
            {
                Assert.Equal(
                    string.Join(">", record.StackFrames.Append(record.Dwo.Name)),
                    record.Stack);
                Assert.DoesNotContain(">", record.Stack[^1..], StringComparison.Ordinal);
                Assert.Equal(record.StackFrames.Count, record.Depth);
            });
    }

    /// <summary>
    /// Asserts the trace carries the row, the expression and the sequencing token alongside the stack and
    /// the value.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    /// <remarks>
    /// THE ROW IS EXERCISED AT TWO, NOT AT ONE. A one-based ordinal that is always 1 is indistinguishable
    /// from a zero-based one that is always 0, and the one-based convention is contract throughout C-04, so
    /// the calculation here is driven on the SECOND row. The token's discipline is asserted because it is
    /// what declares this stream pattern (a): a monotonic sequence a consumer may use to detect and reorder
    /// delivery, which is admissible precisely because nothing depends on the order.
    /// </remarks>
    [Fact]
    public async Task TheTraceCarriesTheRowTheExpressionAndItsSequencingToken()
    {
        WireClient client = Client();

        SeedTwoRows();

        Session session = await OpenAsync(client);

        await EnableAsync(client, session.SessionId, session.Handle);
        await SetTraceAsync(client, session, true);

        _ = await AddExpressionAsync(client, session.SessionId, session.Handle, "n3", "n1 + n2");

        using AsyncDuplexStreamingCall<TraceChannelRequest, TraceRecord> trace =
            await SubscribeTraceAsync(host, client, session);

        CalcResponse calculated =
            await CalcColumnAsync(client, session.SessionId, session.Handle, "n3", row: 2L);

        // The second row's own arithmetic: 20 + 5, not the first row's 10 + 5.
        Assert.Equal("25", DecimalTextOf(calculated));
        Assert.Equal(2L, Assert.Single(calculated.Results).Row);

        TraceRecord traced = Assert.Single(await ReadTraceAsync(trace, 1));

        Assert.Equal(2L, traced.Row);
        Assert.Equal("n1 + n2", traced.Expr);
        Assert.Equal("25", traced.Value);
        Assert.Equal("n3", traced.Stack);
        Assert.Equal("n3", traced.Dwo.Name);

        // Pattern (a): a sequencing token, monotonic from one.
        Assert.Equal(1L, traced.Token.Sequence);
        Assert.Equal(OrderingDiscipline.Sequenced, traced.Token.Discipline);

        // A second calculation advances the sequence rather than repeating it.
        _ = await CalcColumnAsync(client, session.SessionId, session.Handle, "n3", row: 1L);

        TraceRecord next = Assert.Single(await ReadTraceAsync(trace, 1));

        Assert.Equal(2L, next.Token.Sequence);
        Assert.Equal(1L, next.Row);
        Assert.Equal("15", next.Value);
    }

    /// <summary>
    /// Asserts the <c>"(null)"</c> sentinel's two triggering conditions and one case that does not trigger
    /// it.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    /// <remarks>
    /// <para>
    /// TWO CONDITIONS, JOINED BY OR, AND BOTH ARE ASSERTED [:L758]. An empty value is reported as
    /// <c>"(null)"</c> when the column declares <c>NilIsNull</c> - so the empty string IS null for it - or
    /// when the column is not a string column at all, since a number has no empty value to report. A plain
    /// string column without <c>NilIsNull</c> therefore traces the EMPTY STRING, and that third case is
    /// what makes the assertion an assertion rather than a tautology.
    /// </para>
    /// <para>
    /// THE FIRST CONDITION NEEDS THE SCRIPTED HOST, and the reason is recorded on
    /// <see cref="C04WireHostFactory"/>: the deployed catalogue's <c>Describe</c> answers no
    /// <c>NilIsNull</c> for any column, so against it the condition is unreachable and half of a two-armed
    /// sentinel would go untested. The host is otherwise the transcription of the same fixture DataWindow
    /// this suite calculates over.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TheNullSentinelHasTwoTriggeringConditionsAndOneCaseThatDoesNotTriggerIt()
    {
        FakeDataWindowHost scripted = FakeDataWindowFixtures.CreateColumnExpressionFixture();

        // s2 declares NilIsNull; s1 does not. Both are char(100), so the ONLY difference between them is
        // the condition under test.
        scripted.SetDescribe("s2.edit.NilIsNull", "yes");

        using DataServicesTestHostFactory own = new();

        own.AdditionalServiceConfiguration.Add(
            services => services.AddSingleton<IDataWindowHostFactory>(
                new C04WireHostFactory(scripted)));

        WireClient client = own.CreateColumnExpressionClient();
        Session session = await OpenAsync(client);

        await EnableAsync(client, session.SessionId, session.Handle);
        await SetTraceAsync(client, session, true);

        // An empty string literal, so every one of the three expressions evaluates to the empty value the
        // sentinel is selected on. The columns differ; the expression does not.
        foreach (string column in new[] { "s1", "s2", "n1" })
        {
            _ = await AddExpressionAsync(client, session.SessionId, session.Handle, column, "''");
        }

        GetExpressionStateResponse state = await StateAsync(client, session);

        Assert.False(SingleExpressionFor(state, "s1").EmptyStringIsNull);
        Assert.Equal(ColumnExpData.Types.ColType.String, SingleExpressionFor(state, "s1").ColType);
        Assert.True(SingleExpressionFor(state, "s2").EmptyStringIsNull);
        Assert.Equal(ColumnExpData.Types.ColType.String, SingleExpressionFor(state, "s2").ColType);
        Assert.False(SingleExpressionFor(state, "n1").EmptyStringIsNull);
        Assert.Equal(ColumnExpData.Types.ColType.Decimal, SingleExpressionFor(state, "n1").ColType);

        using AsyncDuplexStreamingCall<TraceChannelRequest, TraceRecord> trace =
            await SubscribeTraceAsync(own, client, session);

        foreach (string column in new[] { "s1", "s2", "n1" })
        {
            _ = await CalcColumnAsync(client, session.SessionId, session.Handle, column);
        }

        IReadOnlyList<TraceRecord> records = await ReadTraceAsync(trace, 3);

        // NOT TRIGGERED: a string column that does not treat empty as null reports the empty string.
        Assert.Equal("s1", records[0].Dwo.Name);
        Assert.Equal(string.Empty, records[0].Value);

        // TRIGGERED, condition one: the column declares NilIsNull.
        Assert.Equal("s2", records[1].Dwo.Name);
        Assert.Equal("(null)", records[1].Value);

        // TRIGGERED, condition two: the column is not a string column.
        Assert.Equal("n1", records[2].Dwo.Name);
        Assert.Equal("(null)", records[2].Value);

        // The expression text is identical in all three, so the value alone carries the distinction.
        Assert.All(records, record => Assert.Equal("''", record.Expr));
    }

    /// <summary>
    /// Asserts the trace is fire-and-forget: no subscriber, an inattentive subscriber and a departed
    /// subscriber all leave the calculation's result identical.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    /// <remarks>
    /// THIS IS WHAT MAKES PATTERN (a) LEGITIMATE FOR THIS STREAM. Emission happens inside a calculation, so
    /// it must never block: the broker's per-subscriber queue is bounded and drops the OLDEST record rather
    /// than waiting for a slow reader, and a pump that meets a dead client ends quietly. An undelivered
    /// trace record is a diagnostic gap and never a fault - the three postures below are the three ways a
    /// record can go undelivered, and the calculated value is the same in all three.
    /// </remarks>
    [Fact]
    public async Task TheTraceIsFireAndForgetAndNeverChangesTheCalculationResult()
    {
        using DataServicesTestHostFactory own = new();

        WireClient client = own.CreateColumnExpressionClient();
        Session session = await ReadySessionAsync(client, own);

        await SetTraceAsync(client, session, true);

        _ = await AddExpressionAsync(client, session.SessionId, session.Handle, "n3", "n1 + n2");

        // (a) NO SUBSCRIBER. Every record is dropped at the broker with no subscriber to offer it to.
        Assert.Equal(
            "15",
            DecimalTextOf(await CalcColumnAsync(client, session.SessionId, session.Handle, "n3")));

        // (b) A SUBSCRIBER THAT NEVER READS. The record is queued and abandoned; the calculation neither
        // waits for it to be read nor fails because it was not.
        AsyncDuplexStreamingCall<TraceChannelRequest, TraceRecord> inattentive =
            await SubscribeTraceAsync(own, client, session);

        Assert.Equal(
            "15",
            DecimalTextOf(await CalcColumnAsync(client, session.SessionId, session.Handle, "n3")));

        // (c) THE SUBSCRIBER WALKS AWAY MID-STREAM.
        await inattentive.RequestStream.CompleteAsync();
        inattentive.Dispose();

        Assert.Equal(
            "15",
            DecimalTextOf(await CalcColumnAsync(client, session.SessionId, session.Handle, "n3")));

        // And the service is still healthy: a new subscriber attaches and receives the next record, so a
        // departed consumer did not poison the topic.
        using AsyncDuplexStreamingCall<TraceChannelRequest, TraceRecord> replacement =
            await SubscribeTraceAsync(own, client, session);

        _ = await CalcColumnAsync(client, session.SessionId, session.Handle, "n3");

        Assert.Equal("15", Assert.Single(await ReadTraceAsync(replacement, 1)).Value);
    }

    /// <summary>Asserts no trace record is waiting, using a sentinel rather than a wall-clock wait.</summary>
    /// <param name="factory">The host whose broker the sentinel is offered to.</param>
    /// <param name="channel">The subscribed channel.</param>
    /// <param name="session">The session the subscription belongs to.</param>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// DETERMINISTIC BECAUSE THE STREAM IS FIFO. The subscription is already registered - the barrier in
    /// <see cref="SubscribeTraceAsync"/> proved it - so this record is guaranteed to be delivered, and it
    /// can only be the FIRST thing read if nothing was queued ahead of it. A timeout-based check would
    /// assert the same thing while making the result depend on how busy the machine is.
    /// </remarks>
    private static async Task AssertNoTraceRecordAsync(
        DataServicesTestHostFactory factory,
        AsyncDuplexStreamingCall<TraceChannelRequest, TraceRecord> channel,
        Session session)
    {
        factory.Services.GetRequiredService<ExpressionTraceBroker>().Emit(BarrierRecord(session));

        Assert.True(await channel.ResponseStream.MoveNext(Ct));
        Assert.Equal(TraceBarrierColumn, channel.ResponseStream.Current.Dwo.Name);
    }

    /// <summary>Binds one driving column to an expression so a change to it cascades.</summary>
    /// <param name="client">The client.</param>
    /// <param name="session">The session.</param>
    /// <param name="target">The column carrying the expression.</param>
    /// <param name="driver">The column whose change triggers it.</param>
    /// <returns>A task representing the call.</returns>
    private static async Task BindRelativeColumnAsync(
        WireClient client,
        Session session,
        string target,
        string driver)
    {
        SetRelativeColumnsRequest request = new()
        {
            SessionId = session.SessionId,
            DatawindowHandle = session.Handle,
            Target = new ColumnRef { ColumnName = target },
            InputOnly = false,
        };

        request.RelativeColumns.Add(driver);

        SetRelativeColumnsResponse set =
            await client.SetRelativeColumnsAsync(request, cancellationToken: Ct);

        Assert.Equal(WireRetCode.Ok, set.RetCode);
    }

    /// <summary>Seeds two rows so a one-based row ordinal can be exercised at two.</summary>
    /// <remarks>See <see cref="SeedRows"/> for why the rows are placed server-side.</remarks>
    private void SeedTwoRows() =>
        SeedRows(
            null,
            [("n1", 10d), ("n2", 5d), ("n3", 0d)],
            [("n1", 20d), ("n2", 5d), ("n3", 0d)]);


    // =================================================================================================
    //  PHASE 6 - THE ONE HARD LIMIT: A CROSS-SESSION FOREIGN REFERENCE IS BLOCKED
    //  -----------------------------------------------------------------------------------------------
    //  `foreignvardata` is four lines long and one of its two fields is an OBJECT POINTER:
    //  `n_cst_dwsvc_columnexp expsvc` [n_cst_dwsvc_columnexp.sru:L80-L83]. In process, resolving a foreign
    //  variable is a dereference of that pointer into another live DataWindow's expression service. A
    //  pointer cannot be serialized, and nothing on the wire can stand in for one: an index into the peer's
    //  table is meaningless without the peer, and a copied value is stale the moment the peer changes.
    //
    //  So the contract is NARROWED, deliberately and in exactly one place. A foreign reference resolves
    //  while both DataWindows are co-resident in ONE expression session inside ONE DataServices instance -
    //  the session owns both engines, so the dereference is still in process - and a reference spanning
    //  sessions or instances is REFUSED with a defined, machine-readable error. That is AAP 0.6.2.3's hard
    //  limit and risk R2: a narrowing surfaced in the plan BEFORE implementation rather than discovered
    //  during it, precisely so that it is a documented gap instead of a silent one.
    //
    //  WHY REFUSAL AND NOT APPROXIMATION. The alternative - resolving the pointer across a network by
    //  fetching or caching the peer's value - would answer with a number. It would be the RIGHT SHAPE and
    //  the WRONG VALUE whenever the peer had moved on, and no assertion on the result's type, range or
    //  plausibility would catch it. A refusal is loud; a stale number is not. Every assertion below is
    //  therefore written to prove that the blocked path yields NO VALUE AT ALL, and that the refusal is
    //  distinguishable from the three neighbouring refusals it could be confused with.
    // =================================================================================================

    /// <summary>
    /// Runs the oracle's own cross-DataWindow scenario over the wire and asserts it resolves while both
    /// DataWindows are co-resident.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    /// <remarks>
    /// THE SCENARIO IS TRANSCRIBED, NOT INVENTED
    /// [ws_objects/pfw.tests.pbl.src/w_test_dwsvc_columnexp.srw:L136-L157]: the second DataWindow enables
    /// its own service and defines <c>数据汇总</c> as <c>SUM(n1)</c>, the first adds that variable as a
    /// foreign one naming the second DataWindow, and then references it as <c>$$数据汇总</c>. The oracle's
    /// expression also carries a macro term, which is asserted in the Phase 4 region; here the foreign
    /// reference stands alone so that the value proves the dereference and nothing else.
    /// </remarks>
    [Fact]
    public async Task AForeignVariableResolvesWhileBothDataWindowsAreCoResidentInOneSession()
    {
        WireClient client = Client();
        Session session = await CoResidentSessionAsync(client);

        string first = session.Handles[0];
        string peer = session.Handles[1];

        // The peer defines the aggregate. of_AddVarExp, exactly as the oracle writes it.
        AddVariableExpressionResponse defined = await client.AddVariableExpressionAsync(
            new AddVariableExpressionRequest
            {
                SessionId = session.SessionId,
                DatawindowHandle = peer,
                Name = Summary,
                Exp = "SUM(n1)",
            },
            cancellationToken: Ct);

        Assert.Equal(WireRetCode.Ok, defined.RetCode);

        // of_AddForeignVar("数据汇总", dw_2) - the second argument is a DataWindow at source and a
        // session-scoped HANDLE here, which is the whole of the substitution.
        AddForeignVariableResponse adopted = await client.AddForeignVariableAsync(
            new AddForeignVariableRequest
            {
                SessionId = session.SessionId,
                DatawindowHandle = first,
                Name = Summary,
                ForeignDatawindowHandle = peer,
            },
            cancellationToken: Ct);

        Assert.Equal(WireRetCode.Ok, adopted.RetCode);

        // THE WIRE PROJECTION OF THE ADOPTED ENTRY. A foreign entry carries no local value - its value
        // lives in the peer's table and is never copied into a snapshot, because a copy is the stale
        // reading the narrowing exists to prevent.
        GetExpressionStateResponse state = await StateAsync(client, session);
        GlobalVarData global = Assert.Single(state.GlobalVars, entry => entry.Name == Summary);

        Assert.Equal(GlobalVarData.Types.VarType.VarForeign, global.VarType);
        Assert.Equal(string.Empty, global.Local.Exp);
        Assert.Equal(peer, global.Foreign.ForeignDatawindowHandle);
        Assert.True(global.Foreign.Index > 0, "The foreign index is ONE-BASED in the PEER's table [:L81].");
        Assert.True(global.Foreign.Resolvable);

        // The handle is SESSION-QUALIFIED by construction, which is what makes a handle from another
        // session miss this session's registry rather than collide with one of its own.
        Assert.StartsWith(session.SessionId + "/", global.Foreign.ForeignDatawindowHandle, StringComparison.Ordinal);

        // THE DEREFERENCE, over the wire: SUM(n1) over the peer's rows, read from the first DataWindow.
        _ = await AddExpressionAsync(client, session.SessionId, first, "n3", "$$" + Summary);

        Assert.Equal("7", DecimalTextOf(await CalcColumnAsync(client, session.SessionId, first, "n3")));
    }

    /// <summary>
    /// Asserts a reference to a handle outside the session is blocked with a defined, machine-readable
    /// error, that it yields no value, and that it is distinguishable from every neighbouring refusal.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    /// <remarks>
    /// FOUR REFUSALS THAT MUST NOT BE CONFLATED. An UNDEFINED name is the peer's own lookup failing
    /// [:L2131-L2135] and says nothing about co-residency; a DUPLICATE definition is the legacy's own
    /// hardcoded 错误 [:L2121-L2124]; an EMPTY handle is a malformed request; and a CROSS-SESSION handle is
    /// the narrowing. A caller retries or reports each differently, so each carries its own code and its
    /// own category - and the blocked one is the only one that names the handle and the session in its
    /// detail, because that is what a caller needs in order to understand that co-residency, not
    /// existence, is what failed.
    /// </remarks>
    [Fact]
    public async Task ACrossSessionForeignReferenceIsBlockedWithADefinedErrorAndNeverAValue()
    {
        WireClient client = Client();
        Session session = await CoResidentSessionAsync(client);

        string first = session.Handles[0];
        string peer = session.Handles[1];

        // (1) UNDEFINED IN THE PEER - not a blocked reference. The peer resolved the name and did not
        // find it, which is a lookup failure and nothing to do with co-residency.
        AddForeignVariableResponse undefined = await AdoptForeignAsync(
            client,
            session,
            first,
            "notDefinedInThePeer",
            peer);

        Assert.Equal(WireRetCode.EVarNotFound, undefined.RetCode);
        Assert.Equal(ExpressionError.Types.Category.UndefinedName, undefined.Error.Category);
        Assert.Contains("notDefinedInThePeer", undefined.Error.Error.Text, StringComparison.Ordinal);

        // Define it so the remaining refusals are not all failing for the same trivial reason.
        Assert.Equal(
            WireRetCode.Ok,
            (await client.AddVariableExpressionAsync(
                new AddVariableExpressionRequest
                {
                    SessionId = session.SessionId,
                    DatawindowHandle = peer,
                    Name = Summary,
                    Exp = "SUM(n1)",
                },
                cancellationToken: Ct)).RetCode);

        Assert.Equal(WireRetCode.Ok, (await AdoptForeignAsync(client, session, first, Summary, peer)).RetCode);

        // (2) DUPLICATE - the legacy's hardcoded, UN-LOCALIZED 重复定义 [:L2121-L2124]; defect (f).
        AddForeignVariableResponse duplicate =
            await AdoptForeignAsync(client, session, first, Summary, peer);

        Assert.Equal(WireRetCode.Failed, duplicate.RetCode);
        Assert.Equal(ExpressionError.Types.Category.DuplicateDefinition, duplicate.Error.Category);
        Assert.False(duplicate.Error.Error.Localized);

        // (3) AN EMPTY HANDLE - a malformed request, refused before any co-residency question arises, and
        // deliberately carrying NO expression error because nothing was parsed or resolved.
        AddForeignVariableResponse empty =
            await AdoptForeignAsync(client, session, first, "blank", string.Empty);

        Assert.Equal(WireRetCode.EInvalidArgument, empty.RetCode);
        Assert.Null(empty.Error);

        // (4) THE NARROWING. A handle minted by a DIFFERENT session, in the same service instance, over the
        // same client - the closest a caller can get to a cross-session reference - is BLOCKED. The other
        // session binds the OTHER catalogue definition as well, so the refused handle names a DataWindow
        // that is foreign in both senses and nothing about it could plausibly have resolved.
        Session other = await OpenAsync(client, Peer);

        AddForeignVariableResponse blocked = await AdoptForeignAsync(
            client,
            session,
            first,
            "crossSession",
            other.Handle);

        Assert.Equal(WireRetCode.EInvalidHandle, blocked.RetCode);
        Assert.Equal(
            ExpressionError.Types.Category.ForeignReferenceBlocked,
            blocked.Error.Category);

        // MACHINE-READABLE AND SPECIFIC: the detail names the offending handle and the session it is not
        // co-resident in, so the caller can tell this apart from "no such variable" without parsing prose.
        Assert.Contains(other.Handle, blocked.Error.Error.Text, StringComparison.Ordinal);
        Assert.Contains(session.SessionId, blocked.Error.Error.Text, StringComparison.Ordinal);
        Assert.False(blocked.Error.Error.Localized);

        // NEVER A VALUE, AND NEVER A HALF-ADOPTED ENTRY: the refusal left the variable table untouched, so
        // no later reference can find a stub that resolves to something plausible.
        Assert.DoesNotContain(
            (await StateAsync(client, session)).GlobalVars,
            entry => entry.Name == "crossSession");

        // A handle that never existed at all takes the same path, because "not co-resident" is exactly what
        // is wrong with it.
        AddForeignVariableResponse nonexistent = await AdoptForeignAsync(
            client,
            session,
            first,
            "neverExisted",
            session.SessionId + "/99");

        Assert.Equal(WireRetCode.EInvalidHandle, nonexistent.RetCode);
        Assert.Equal(
            ExpressionError.Types.Category.ForeignReferenceBlocked,
            nonexistent.Error.Category);

        // The four refusals are four distinct codes, which is the property a caller's policy depends on.
        Assert.Equal(
            4,
            new HashSet<WireRetCode>
            {
                undefined.RetCode,
                duplicate.RetCode,
                empty.RetCode,
                blocked.RetCode,
            }.Count);
    }

    /// <summary>
    /// Asserts a static reference to a foreign variable is refused with the legacy's own parse error while
    /// the dynamic reference to the very same variable succeeds.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    /// <remarks>
    /// THE SPECIFICATION SAYS THE FOREIGN FORM REQUIRES DYNAMIC EXPANSION
    /// [docs/n_cst_dwsvc_columnexp.md:L34] and the parser enforces it at [:L1426]: a static reference is
    /// resolved and BAKED IN at bind time, and a foreign variable's value is not this service's to bake -
    /// it belongs to the peer and may change. Refusing at parse time is therefore the same decision as
    /// refusing a cross-session handle, taken one layer earlier.
    /// </remarks>
    [Fact]
    public async Task AStaticReferenceToAForeignVariableIsRefusedWhileTheDynamicFormSucceeds()
    {
        WireClient client = Client();
        Session session = await CoResidentSessionAsync(client);

        string first = session.Handles[0];
        string peer = session.Handles[1];

        Assert.Equal(
            WireRetCode.Ok,
            (await client.AddVariableExpressionAsync(
                new AddVariableExpressionRequest
                {
                    SessionId = session.SessionId,
                    DatawindowHandle = peer,
                    Name = Summary,
                    Exp = "SUM(n1)",
                },
                cancellationToken: Ct)).RetCode);

        Assert.Equal(WireRetCode.Ok, (await AdoptForeignAsync(client, session, first, Summary, peer)).RetCode);

        // THE STATIC FORM IS REFUSED.
        AddExpressionResponse statically = await client.AddExpressionAsync(
            new AddExpressionRequest
            {
                SessionId = session.SessionId,
                DatawindowHandle = first,
                ColumnName = "n2",
                Exp = "$" + Summary,
            },
            cancellationToken: Ct);

        Assert.Equal(WireRetCode.Failed, statically.RetCode);

        // `of_addexp` returns an INDEX and not a code [:L172], so a failure travels in the index slot as the
        // legacy's own negative return - the flattened E_INVALID_ARGUMENT of [:L1554] - while `ret_code`
        // carries the coarse outcome. Both are asserted because the two fields exist precisely so a caller
        // need not guess which one it is reading.
        Assert.Equal(-3, statically.Index);
        Assert.Equal(-3, (int)WireRetCode.EInvalidArgument);

        Assert.Equal(ExpressionError.Types.Category.Parse, statically.Error.Category);
        Assert.Equal(3L, statically.Error.CaretPosition);
        Assert.Contains(
            "不支持静态展开",
            statically.Error.Error.Text,
            StringComparison.Ordinal);
        Assert.Contains(Summary, statically.Error.Error.Text, StringComparison.Ordinal);
        Assert.False(statically.Error.Error.Localized);

        // The caret is rendered as well as reported, so a client can show the position without recomputing
        // it - and the marker points into the expression that was refused.
        Assert.Contains("$" + Summary, statically.Error.RenderedMarker, StringComparison.Ordinal);
        Assert.Contains("^", statically.Error.RenderedMarker, StringComparison.Ordinal);

        // Nothing was bound: the refusal is not a partial bind.
        Assert.DoesNotContain(
            (await StateAsync(client, session)).Expressions,
            data => data.Name == "n2");

        // THE DYNAMIC FORM, SAME VARIABLE, SAME SESSION, SUCCEEDS. The difference is the expansion mode and
        // nothing else, which is what makes this pair the assertion rather than either half alone.
        _ = await AddExpressionAsync(client, session.SessionId, first, "n3", "$$" + Summary);

        Assert.Equal("7", DecimalTextOf(await CalcColumnAsync(client, session.SessionId, first, "n3")));
    }

    /// <summary>
    /// Opens one session holding TWO co-resident DataWindows over a DataWindow carrying one row.
    /// </summary>
    /// <param name="client">The client.</param>
    /// <returns>The session, whose handles are the two registrations in order.</returns>
    /// <remarks>
    /// <para>
    /// BOTH HANDLES NAME THE SAME DEFINITION, and that is deliberate rather than a shortcut: the oracle's
    /// two DataWindows are two instances of the same transcribed definition
    /// [ws_objects/pfw.tests.pbl.src/dw_test_dwsvc_columnexp.srd], the peer's variable is <c>SUM(n1)</c>
    /// and only that definition has an <c>n1</c>. The session mints a DISTINCT HANDLE per registration, so
    /// the two are separate ENGINES - separate expression tables, separate variable environments, separate
    /// calculation caches - which is what the foreign reference below has to cross.
    /// </para>
    /// <para>
    /// THEY SHARE ONE DATAWINDOW, BECAUSE THE HOST FACTORY IS RETENTIVE PER NAME, and for this scenario
    /// that is immaterial: what a foreign reference resolves is the PEER ENGINE'S variable, and the two
    /// engines are distinct. See <see cref="SeedRows"/> for why the row is placed server-side.
    /// </para>
    /// </remarks>
    private async Task<Session> CoResidentSessionAsync(WireClient client)
    {
        SeedRows(null, [("n1", 7d), ("n2", 2d), ("n3", 3d)]);

        Session session = await OpenAsync(client, Fixture, Fixture);

        Assert.Equal(2, session.Handles.Count);
        Assert.NotEqual(session.Handles[0], session.Handles[1]);

        foreach (string handle in session.Handles)
        {
            await EnableAsync(client, session.SessionId, handle);
        }

        return session;
    }

    /// <summary>Asks one DataWindow to adopt a variable defined in another.</summary>
    /// <param name="client">The client.</param>
    /// <param name="session">The session.</param>
    /// <param name="handle">The DataWindow doing the adopting.</param>
    /// <param name="name">The variable's name in the peer's table.</param>
    /// <param name="foreignHandle">The peer's handle, or a handle deliberately outside the session.</param>
    /// <returns>The unexamined response, so the caller can assert the refusal it is testing for.</returns>
    private static Task<AddForeignVariableResponse> AdoptForeignAsync(
        WireClient client,
        Session session,
        string handle,
        string name,
        string foreignHandle) =>
        client.AddForeignVariableAsync(
            new AddForeignVariableRequest
            {
                SessionId = session.SessionId,
                DatawindowHandle = handle,
                Name = name,
                ForeignDatawindowHandle = foreignHandle,
            },
            cancellationToken: Ct).ResponseAsync;

}
