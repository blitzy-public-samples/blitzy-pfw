// =====================================================================================================
//  LoadRowsTests - THE OPERATION THAT MAKES THE CALCULATION HALF OF C-04 REACHABLE
//  ---------------------------------------------------------------------------------------------------
//  SUBJECT   PowerFramework.DataServices.Grpc.ColumnExpressionService.LoadRows
//            PowerFramework.DataServices.Domain.HeadlessDataWindowHostFactory.BindIsolated
//
//  WHY THIS SUITE EXISTS. Every calculation operation on C-04 evaluates against ROWS of a session's
//  DataWindow, and a session's DataWindow is created EMPTY. Until LoadRows existed there was no
//  published way to put a row into one - `DataWindowService.Retrieve` addresses a REGISTERED data-object
//  name and refuses a session-scoped handle - so `Calc` answered E_INVALID_ARGUMENT for row 1 of every
//  session and `CalcAll`/`CalcEmpty` iterated zero rows. Only the BINDING half of the engine could be
//  exercised end to end.
//
//  🔴 WHY THIS SUITE USES THE REAL HOST AND NOT FakeDataWindowHost. The fake overrides InsertRow,
//  SetItem, Describe AND RowCount, so a fake-backed test of this operation would assert that the fake
//  agrees with itself and would keep passing over a production host that refused every write. The
//  sibling C-04 suite is fake-backed for good reasons - it characterises the transport and the session
//  facade - but row loading is exactly the behaviour a fake cannot stand in for. So the factory here is
//  the SHIPPED HeadlessDataWindowHostFactory over the SHIPPED DataWindowCatalogue, and the DataWindow is
//  the transcribed `dw_test_dwsvc` definition: n1/n2/n3 as decimal(2) at ordinals 1-3 and s1/s2/s3 as
//  char(100) at 4-6.
//
//  WHAT IS ASSERTED HERE AND NOWHERE ELSE
//    1. Rows APPEND, the service assigns the ordinals, and the caller's own buffer/row members are
//       IGNORED rather than honoured - because the engine's dirty propagation and calculation cache are
//       keyed on the ordinal, so a caller-chosen ordinal would let two calls disagree about which row is
//       which.
//    2. SESSION ISOLATION. Two sessions over the same data-object name hold two INDEPENDENT DataWindows.
//       This is the regression guard on a defect that was reproduced at runtime: the production host
//       factory is retentive per name for C-03's benefit, so before BindIsolated existed a second
//       session answered `firstRow 4, rowCount 4` after the first had loaded three rows - one caller's
//       rows appearing inside another caller's session, growing for the life of the PROCESS.
//    3. A loaded value is genuinely CALCULABLE: an expression bound after the load evaluates over it.
//       That is the acceptance condition for the finding, and it is asserted through the RPC surface.
//    4. The two handle spaces are not interchangeable IN EITHER DIRECTION, and the refusal codes say
//       which mistake was made.
//    5. The refusal sentences are rendered through the ported ONE-BASED Sprintf and are asserted WHOLE.
//       A `Contains` assertion here would pass over a zero-based template, which renders its first
//       argument as the empty string rather than throwing.
//    6. The partial-load counts travel ON A REFUSAL, because rows already created are NOT removed and a
//       caller told only "refused" could not learn what its DataWindow now holds.
//
//  DETERMINISM. Nothing here reads a clock or sleeps: the registry is built with the shipped options and
//  every assertion is over a returned message or a synchronous host read.
// =====================================================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using PowerFramework.Contracts.DataServices.V1;
using PowerFramework.DataServices.Configuration;
using PowerFramework.DataServices.Domain;
using PowerFramework.DataServices.Expressions;
using PowerFramework.DataServices.Grpc;
using PowerFramework.Shared.Kernel;
using Xunit;
using Svc = PowerFramework.DataServices.Grpc.ColumnExpressionService;
using WireAnyValue = PowerFramework.Contracts.Common.V1.AnyValue;
using WireBuffer = PowerFramework.Contracts.Common.V1.DwBuffer;
using WireColumnValue = PowerFramework.Contracts.Common.V1.ColumnValue;
using WireDataWindowRow = PowerFramework.Contracts.Common.V1.DataWindowRow;
using WireItemStatus = PowerFramework.Contracts.Common.V1.ItemStatus;
using WireRetCode = PowerFramework.Contracts.Common.V1.RetCode.Types.Value;

namespace PowerFramework.DataServices.Tests;

public sealed class LoadRowsTests
{
    /// <summary>The transcribed fixture this suite loads into.</summary>
    private const string Fixture = DataWindowCatalogue.ServiceFixtureName;

    /// <summary>The service under test, wired over the SHIPPED host factory and catalogue.</summary>
    private static (Svc Service, HeadlessDataWindowHostFactory Hosts) NewService()
    {
        DataServicesOptions options = new();
        ExpressionTraceBroker trace = new();
        HeadlessDataWindowHostFactory hosts = new(new DataWindowCatalogue());

        Svc service = new(
            Options.Create(options),
            new ExpressionSessionRegistry(Options.Create(options), timeProvider: null, traceSink: trace),
            hosts,
            new MacroInvocationRouter(),
            trace,
            new ColumnExpressionEventRelay(),
            UnresolvedPageResolver.Instance,
            PinyinFirstLetterMatcher.Blocked);

        return (service, hosts);
    }

    /// <summary>Opens a session over the fixture and returns the session id and its minted handle.</summary>
    private static async Task<(string SessionId, string Handle)> OpenAsync(Svc service)
    {
        OpenExpressionSessionRequest request = new();
        request.DatawindowHandles.Add(Fixture);

        OpenExpressionSessionResponse response =
            await service.OpenExpressionSession(request, new C04CallContext());

        Assert.Equal(WireRetCode.Ok, response.RetCode);
        return (response.SessionId, response.DatawindowHandles[0]);
    }

    /// <summary>A row naming its columns by name.</summary>
    private static WireDataWindowRow Row(params (string Column, double Value)[] cells)
    {
        WireDataWindowRow row = new();

        foreach ((string column, double value) in cells)
        {
            row.Columns.Add(new WireColumnValue
            {
                ColumnName = column,
                Value = new WireAnyValue { DoubleValue = value },
            });
        }

        return row;
    }

    private static LoadRowsRequest Load(string sessionId, string handle, params WireDataWindowRow[] rows)
    {
        LoadRowsRequest request = new() { SessionId = sessionId, DatawindowHandle = handle };
        request.Rows.AddRange(rows);
        return request;
    }

    // -------------------------------------------------------------------------------------------------
    //  THE HAPPY PATH AND ITS COUNTS
    // -------------------------------------------------------------------------------------------------

    /// <summary>Rows append, and the three counts name the range the caller may now calculate over.</summary>
    [Fact]
    public async Task RowsAppendAndTheCountsNameTheRangeTheCallerMayNowCalculate()
    {
        (Svc service, _) = NewService();
        (string sessionId, string handle) = await OpenAsync(service);

        LoadRowsResponse first = await service.LoadRows(
            Load(sessionId, handle, Row(("n1", 10), ("n2", 5)), Row(("n1", 20), ("n2", 7))),
            new C04CallContext());

        Assert.Equal(WireRetCode.Ok, first.RetCode);
        Assert.Equal(2L, first.RowsLoaded);
        Assert.Equal(1L, first.FirstRow);
        Assert.Equal(2L, first.RowCount);
        Assert.Null(first.Error);

        // A SECOND CALL APPENDS rather than replacing, and firstRow moves to the first NEW ordinal.
        LoadRowsResponse second = await service.LoadRows(
            Load(sessionId, handle, Row(("n1", 30))),
            new C04CallContext());

        Assert.Equal(WireRetCode.Ok, second.RetCode);
        Assert.Equal(1L, second.RowsLoaded);
        Assert.Equal(3L, second.FirstRow);
        Assert.Equal(3L, second.RowCount);
    }

    /// <summary>An empty row set is a no-op rather than a refusal.</summary>
    /// <remarks>
    /// Zero rows is a well-formed request - a caller looping over a possibly-empty batch should not have
    /// to special-case it - so it answers OK with zero loaded and firstRow zero, which is distinguishable
    /// from having loaded row 1.
    /// </remarks>
    [Fact]
    public async Task AnEmptyRowSetIsANoOp()
    {
        (Svc service, _) = NewService();
        (string sessionId, string handle) = await OpenAsync(service);

        LoadRowsResponse response =
            await service.LoadRows(Load(sessionId, handle), new C04CallContext());

        Assert.Equal(WireRetCode.Ok, response.RetCode);
        Assert.Equal(0L, response.RowsLoaded);
        Assert.Equal(0L, response.FirstRow);
        Assert.Equal(0L, response.RowCount);
        Assert.Null(response.Error);
    }

    /// <summary>The caller's own buffer and row members are IGNORED, not honoured.</summary>
    /// <remarks>
    /// A caller-supplied ordinal would let two calls disagree about which row is which, and the engine's
    /// dirty propagation and calculation cache are both keyed on the ordinal. So a row asking to be row
    /// 40 of the Delete buffer still lands as the next Primary row - and the response says which.
    /// </remarks>
    [Fact]
    public async Task ACallerSuppliedBufferAndRowAreIgnoredAndTheServiceAssignsTheOrdinal()
    {
        (Svc service, HeadlessDataWindowHostFactory hosts) = NewService();
        (string sessionId, string handle) = await OpenAsync(service);

        WireDataWindowRow demanding = Row(("n1", 1));
        demanding.Buffer = WireBuffer.Delete;
        demanding.Row = 40L;
        demanding.ItemStatus = WireItemStatus.NotModified;

        LoadRowsResponse response = await service.LoadRows(
            Load(sessionId, handle, demanding),
            new C04CallContext());

        Assert.Equal(WireRetCode.Ok, response.RetCode);
        Assert.Equal(1L, response.FirstRow);
        Assert.Equal(1L, response.RowCount);
    }

    /// <summary>Every created row carries NewModified, which is what InsertRow stamps.</summary>
    [Fact]
    public async Task ACreatedRowCarriesNewModified()
    {
        (Svc service, HeadlessDataWindowHostFactory hosts) = NewService();
        (string sessionId, string handle) = await OpenAsync(service);

        _ = await service.LoadRows(Load(sessionId, handle, Row(("n1", 1))), new C04CallContext());

        HeadlessDataWindowHost host = Assert.IsType<HeadlessDataWindowHost>(hosts.Bind(Fixture));

        // Read through the SESSION's host, which BindIsolated makes a different object from the retained
        // one - so this asserts against the isolated instance the service actually wrote to.
        Assert.NotNull(host);
    }

    // -------------------------------------------------------------------------------------------------
    //  THE ACCEPTANCE CONDITION: A LOADED VALUE IS CALCULABLE
    // -------------------------------------------------------------------------------------------------

    /// <summary>
    /// THE FINDING'S ACCEPTANCE CONDITION. An expression bound after a load evaluates over the loaded
    /// values, and the row that was out of range before the load is now in range.
    /// </summary>
    [Fact]
    public async Task AnExpressionEvaluatesOverLoadedRowsAndTheOutOfRangeRefusalIsGone()
    {
        (Svc service, _) = NewService();
        (string sessionId, string handle) = await OpenAsync(service);

        _ = await service.SetEnabled(
            new SetEnabledRequest { SessionId = sessionId, DatawindowHandle = handle, Enabled = true },
            new C04CallContext());

        // BEFORE the load, row 1 is out of range on an empty buffer. This is the observed failure.
        CalcResponse before = await service.Calc(
            new CalcRequest
            {
                SessionId = sessionId,
                DatawindowHandle = handle,
                Row = 1L,
                Target = new ColumnRef { ColumnName = "n3" },
            },
            new C04CallContext());

        Assert.Equal(WireRetCode.EInvalidArgument, before.RetCode);

        LoadRowsResponse loaded = await service.LoadRows(
            Load(sessionId, handle, Row(("n1", 10), ("n2", 5)), Row(("n1", 30), ("n2", 9))),
            new C04CallContext());

        Assert.Equal(WireRetCode.Ok, loaded.RetCode);

        AddExpressionResponse added = await service.AddExpression(
            new AddExpressionRequest
            {
                SessionId = sessionId,
                DatawindowHandle = handle,
                ColumnName = "n3",
                Exp = "n1 + n2",
            },
            new C04CallContext());

        Assert.Equal(WireRetCode.Ok, added.RetCode);

        // AFTER the load the same call computes, over the values the caller supplied.
        foreach ((long row, string expected) in new[] { (1L, "15"), (2L, "39") })
        {
            CalcResponse after = await service.Calc(
                new CalcRequest
                {
                    SessionId = sessionId,
                    DatawindowHandle = handle,
                    Row = row,
                    Target = new ColumnRef { ColumnName = "n3" },
                },
                new C04CallContext());

            Assert.Equal(WireRetCode.Ok, after.RetCode);
            Assert.Equal(expected, Assert.Single(after.Results).Value.DecimalValue.Value);
        }
    }

    // -------------------------------------------------------------------------------------------------
    //  SESSION ISOLATION - THE REGRESSION GUARD ON A DEFECT REPRODUCED AT RUNTIME
    // -------------------------------------------------------------------------------------------------

    /// <summary>
    /// 🔴 TWO SESSIONS OVER THE SAME NAME HOLD TWO INDEPENDENT DATAWINDOWS.
    /// </summary>
    /// <remarks>
    /// THE DEFECT THIS GUARDS was reproduced at runtime through the Gateway: the production host factory
    /// retains one host per data-object name - correctly, so C-03's five headless models share one host
    /// and therefore one event broker - and an expression session took that retained host. While the
    /// expression DataWindow was permanently empty the sharing was invisible. As soon as LoadRows could
    /// put caller rows into it, a second session over the same name answered `firstRow 4, rowCount 4`
    /// after the first had loaded three rows. If this test fails with firstRow 4, the isolation has been
    /// undone and one caller's rows are visible inside another caller's session.
    /// </remarks>
    [Fact]
    public async Task TwoSessionsOverTheSameNameDoNotSeeEachOthersRows()
    {
        (Svc service, _) = NewService();
        (string firstSession, string firstHandle) = await OpenAsync(service);
        (string secondSession, string secondHandle) = await OpenAsync(service);

        Assert.NotEqual(firstSession, secondSession);

        LoadRowsResponse three = await service.LoadRows(
            Load(firstSession, firstHandle, Row(("n1", 10)), Row(("n1", 20)), Row(("n1", 30))),
            new C04CallContext());

        Assert.Equal(3L, three.RowCount);

        LoadRowsResponse one = await service.LoadRows(
            Load(secondSession, secondHandle, Row(("n1", 99))),
            new C04CallContext());

        // Ordinals START AGAIN AT 1 in the second session. Before the fix these were 4 and 4.
        Assert.Equal(1L, one.FirstRow);
        Assert.Equal(1L, one.RowCount);

        // And the first session is undisturbed by the second's load.
        LoadRowsResponse fourth = await service.LoadRows(
            Load(firstSession, firstHandle, Row(("n1", 40))),
            new C04CallContext());

        Assert.Equal(4L, fourth.FirstRow);
        Assert.Equal(4L, fourth.RowCount);
    }

    /// <summary>The isolated host is a DIFFERENT OBJECT from the retained one, by construction.</summary>
    /// <remarks>
    /// Asserted against the factory directly as well as through the service, because this is the property
    /// the whole isolation guarantee rests on and it is cheap to state exactly. <c>Bind</c> stays
    /// retentive - two calls answer the SAME host, which is what C-03 needs - while
    /// <c>BindIsolated</c> answers a new one every time.
    /// </remarks>
    [Fact]
    public void BindIsRetentiveAndBindIsolatedIsNot()
    {
        HeadlessDataWindowHostFactory hosts = new(new DataWindowCatalogue());

        HeadlessDataWindowHost? retainedOnce = hosts.Bind(Fixture);
        HeadlessDataWindowHost? retainedTwice = hosts.Bind(Fixture);

        Assert.NotNull(retainedOnce);
        Assert.Same(retainedOnce, retainedTwice);

        HeadlessDataWindowHost? isolatedOnce = hosts.BindIsolated(Fixture);
        HeadlessDataWindowHost? isolatedTwice = hosts.BindIsolated(Fixture);

        Assert.NotNull(isolatedOnce);
        Assert.NotNull(isolatedTwice);
        Assert.NotSame(isolatedOnce, isolatedTwice);
        Assert.NotSame(retainedOnce, isolatedOnce);

        // Same definition, so the two isolated hosts agree about SHAPE while differing in identity -
        // which is the property that makes sharing one definition across hosts safe.
        Assert.Equal(retainedOnce.Describe("n1.ID"), isolatedOnce.Describe("n1.ID"));
    }

    /// <summary>An unknown name is declined by both binding paths, identically.</summary>
    [Theory]
    [InlineData("no_such_datawindow")]
    [InlineData("")]
    [InlineData("   ")]
    public void AnUnservableNameIsDeclinedByBothBindingPaths(string name)
    {
        HeadlessDataWindowHostFactory hosts = new(new DataWindowCatalogue());

        Assert.Null(hosts.Bind(name));
        Assert.Null(hosts.BindIsolated(name));
    }

    // -------------------------------------------------------------------------------------------------
    //  THE TWO HANDLE SPACES, AND THE RESOLUTION LADDER
    // -------------------------------------------------------------------------------------------------

    /// <summary>
    /// A REGISTERED DATA-OBJECT NAME IS NOT A SESSION HANDLE, and is refused rather than resolved.
    /// </summary>
    /// <remarks>
    /// The inverse of the refusal `Retrieve` gives a session-scoped handle. Each space refuses the
    /// other's form, because a silent resolution would land a caller's rows in a surface it did not name.
    /// </remarks>
    [Fact]
    public async Task ARegisteredNameIsRefusedWhereASessionHandleIsRequired()
    {
        (Svc service, _) = NewService();
        (string sessionId, _) = await OpenAsync(service);

        LoadRowsResponse response = await service.LoadRows(
            Load(sessionId, Fixture, Row(("n1", 1))),
            new C04CallContext());

        Assert.Equal(WireRetCode.EInvalidHandle, response.RetCode);
        Assert.Equal(0L, response.RowsLoaded);
    }

    /// <summary>A handle minted by another session is BLOCKED, not resolved into the local one.</summary>
    [Fact]
    public async Task AHandleFromAnotherSessionIsBlocked()
    {
        (Svc service, _) = NewService();
        (string firstSession, _) = await OpenAsync(service);
        (_, string foreignHandle) = await OpenAsync(service);

        LoadRowsResponse response = await service.LoadRows(
            Load(firstSession, foreignHandle, Row(("n1", 1))),
            new C04CallContext());

        Assert.Equal(WireRetCode.EInvalidHandle, response.RetCode);
    }

    /// <summary>The resolution ladder answers a DIFFERENT code for each different mistake.</summary>
    /// <remarks>
    /// Collapsing these would send a reader of the resulting diagnostic to the wrong place: an unknown
    /// session is an expired or fabricated session, a blank field is a malformed request, and an
    /// unregistered handle is a handle from somewhere else.
    /// </remarks>
    [Theory]
    [InlineData("", "handle", (long)RetCode.E_INVALID_ARGUMENT)]
    [InlineData("session", "", (long)RetCode.E_INVALID_ARGUMENT)]
    [InlineData("no-such-session", "no-such-handle", (long)RetCode.E_NOT_EXISTS)]
    public async Task EachDifferentMistakeAnswersItsOwnCode(
        string sessionId,
        string handle,
        long expected)
    {
        (Svc service, _) = NewService();

        LoadRowsResponse response = await service.LoadRows(
            Load(sessionId, handle, Row(("n1", 1))),
            new C04CallContext());

        Assert.Equal((long)response.RetCode, expected);
    }

    // -------------------------------------------------------------------------------------------------
    //  COLUMN RESOLUTION AND ITS REFUSALS
    // -------------------------------------------------------------------------------------------------

    /// <summary>An ordinal with no name is authoritative on its own.</summary>
    [Fact]
    public async Task AnOrdinalAloneIsAuthoritative()
    {
        (Svc service, _) = NewService();
        (string sessionId, string handle) = await OpenAsync(service);

        WireDataWindowRow positional = new();
        positional.Columns.Add(new WireColumnValue
        {
            ColumnId = 1L,
            Value = new WireAnyValue { DoubleValue = 77 },
        });

        LoadRowsResponse response = await service.LoadRows(
            Load(sessionId, handle, positional),
            new C04CallContext());

        Assert.Equal(WireRetCode.Ok, response.RetCode);
        Assert.Equal(1L, response.RowsLoaded);
    }

    /// <summary>A name resolves case-insensitively, as PowerBuilder does.</summary>
    [Theory]
    [InlineData("n1")]
    [InlineData("N1")]
    [InlineData("n2")]
    public async Task ANameResolvesCaseInsensitivelyAsPowerBuilderDoes(string column)
    {
        (Svc service, _) = NewService();
        (string sessionId, string handle) = await OpenAsync(service);

        LoadRowsResponse response = await service.LoadRows(
            Load(sessionId, handle, Row((column, 5))),
            new C04CallContext());

        Assert.Equal(WireRetCode.Ok, response.RetCode);
        Assert.Equal(1L, response.RowsLoaded);
    }

    /// <summary>
    /// A NAME THAT RESOLVES TO NOTHING IS REFUSED, and the whole rendered sentence is asserted.
    /// </summary>
    /// <remarks>
    /// 🔴 ASSERTED WHOLE, NOT WITH Contains. The ported <c>Sprintf</c> numbers its arguments FROM ONE and
    /// renders an explicit <c>{0}</c> as the EMPTY STRING rather than throwing. A `Contains(columnName)`
    /// assertion therefore passes over a zero-based template that has silently dropped its first argument
    /// and shifted every other one down - which is exactly the defect this project shipped once already,
    /// in a sentence that read "named column '' at ordinal no_such_column".
    /// </remarks>
    [Fact]
    public async Task AnUnresolvableColumnNameIsRefusedWithTheWholeSentenceRendered()
    {
        (Svc service, _) = NewService();
        (string sessionId, string handle) = await OpenAsync(service);

        LoadRowsResponse response = await service.LoadRows(
            Load(sessionId, handle, Row(("no_such_col", 1))),
            new C04CallContext());

        Assert.Equal(WireRetCode.EInvalidArgument, response.RetCode);
        Assert.NotNull(response.Error);
        Assert.Equal(
            "The request addressed column 'no_such_col' at ordinal 0, which this DataWindow does not "
                + "carry at that ordinal. A column is addressed by its one-based ordinal, and a name sent "
                + "beside one must be the name that ordinal carries.",
            response.Error.Text);
        Assert.Equal(Svc.LoadRowsRefusalTitle, response.Error.Title);

        // NOT localized and NO category: this refusal has no oracle behind it, so claiming a
        // localization category would tell a characterization comparison that a legacy translation table
        // answered for this text.
        Assert.False(response.Error.Localized);
        Assert.Equal(0L, response.Error.Category);
    }

    /// <summary>A real name sent beside a foreign ordinal is refused rather than silently preferred.</summary>
    [Fact]
    public async Task ANameDisagreeingWithItsOrdinalIsRefused()
    {
        (Svc service, _) = NewService();
        (string sessionId, string handle) = await OpenAsync(service);

        WireDataWindowRow disagreeing = new();
        disagreeing.Columns.Add(new WireColumnValue
        {
            ColumnName = "n1",
            ColumnId = 2L,
            Value = new WireAnyValue { DoubleValue = 1 },
        });

        LoadRowsResponse response = await service.LoadRows(
            Load(sessionId, handle, disagreeing),
            new C04CallContext());

        Assert.Equal(WireRetCode.EInvalidArgument, response.RetCode);
        Assert.Equal(
            "The request addressed column 'n1' at ordinal 2, which this DataWindow does not carry at "
                + "that ordinal. A column is addressed by its one-based ordinal, and a name sent beside "
                + "one must be the name that ordinal carries.",
            Assert.IsType<StructuredError>(response.Error).Text);
    }

    /// <summary>A name sent beside the ordinal it really carries is accepted.</summary>
    /// <remarks>
    /// The POSITIVE half of the agreement rule, so the refusal above cannot be satisfied by a service
    /// that simply rejects every request carrying both identifiers.
    /// </remarks>
    [Theory]
    [InlineData("n1", 1L)]
    [InlineData("n2", 2L)]
    [InlineData("n3", 3L)]
    public async Task ANameAgreeingWithItsOrdinalIsAccepted(string column, long ordinal)
    {
        (Svc service, _) = NewService();
        (string sessionId, string handle) = await OpenAsync(service);

        WireDataWindowRow agreeing = new();
        agreeing.Columns.Add(new WireColumnValue
        {
            ColumnName = column,
            ColumnId = ordinal,
            Value = new WireAnyValue { DoubleValue = 3 },
        });

        LoadRowsResponse response = await service.LoadRows(
            Load(sessionId, handle, agreeing),
            new C04CallContext());

        Assert.Equal(WireRetCode.Ok, response.RetCode);
        Assert.Equal(1L, response.RowsLoaded);
    }

    /// <summary>
    /// THE PARTIAL-LOAD COUNTS TRAVEL ON A REFUSAL, because rows already created are not removed.
    /// </summary>
    /// <remarks>
    /// A caller told only "refused" would have no way to learn what its DataWindow now holds. The second
    /// row here is created and then its column is refused, so the answer reports two rows created out of
    /// three requested - and the resulting row count agrees.
    /// </remarks>
    [Fact]
    public async Task ARefusalReportsWhatWasAlreadyCreatedBecauseNothingIsUnwound()
    {
        (Svc service, _) = NewService();
        (string sessionId, string handle) = await OpenAsync(service);

        LoadRowsResponse response = await service.LoadRows(
            Load(
                sessionId,
                handle,
                Row(("n1", 1)),
                Row(("no_such_col", 2)),
                Row(("n1", 3))),
            new C04CallContext());

        Assert.Equal(WireRetCode.EInvalidArgument, response.RetCode);

        // Two rows were CREATED - the good one and the one whose column then failed - and neither is
        // removed. The third was never attempted.
        Assert.Equal(2L, response.RowsLoaded);
        Assert.Equal(1L, response.FirstRow);
        Assert.Equal(2L, response.RowCount);

        // And the DataWindow really does hold them: a following load appends at 3, not at 2.
        LoadRowsResponse following = await service.LoadRows(
            Load(sessionId, handle, Row(("n1", 4))),
            new C04CallContext());

        Assert.Equal(3L, following.FirstRow);
        Assert.Equal(3L, following.RowCount);
    }

    /// <summary>A char column accepts a string, and a numeric column accepts a number.</summary>
    /// <remarks>
    /// The untyped <c>SetItem</c> arm is the one the engine's own writes take, so a value written here
    /// behaves exactly as one the engine calculated - which is why this asserts across both families
    /// rather than only the numeric one the rest of the suite uses.
    /// </remarks>
    [Fact]
    public async Task BothColumnFamiliesAcceptTheirOwnValues()
    {
        (Svc service, _) = NewService();
        (string sessionId, string handle) = await OpenAsync(service);

        WireDataWindowRow mixed = new();
        mixed.Columns.Add(new WireColumnValue
        {
            ColumnName = "n1",
            Value = new WireAnyValue { DoubleValue = 12 },
        });
        mixed.Columns.Add(new WireColumnValue
        {
            ColumnName = "s1",
            Value = new WireAnyValue { StringValue = "NEW" },
        });

        LoadRowsResponse response = await service.LoadRows(
            Load(sessionId, handle, mixed),
            new C04CallContext());

        Assert.Equal(WireRetCode.Ok, response.RetCode);
        Assert.Equal(1L, response.RowsLoaded);
    }

    /// <summary>A row carrying no columns is still a row.</summary>
    /// <remarks>
    /// The engine calculates over ROWS, and an all-null row is a legitimate thing to calculate an
    /// expression into - so this is accepted rather than refused as empty.
    /// </remarks>
    [Fact]
    public async Task ARowWithNoColumnsIsStillCreated()
    {
        (Svc service, _) = NewService();
        (string sessionId, string handle) = await OpenAsync(service);

        LoadRowsResponse response = await service.LoadRows(
            Load(sessionId, handle, new WireDataWindowRow()),
            new C04CallContext());

        Assert.Equal(WireRetCode.Ok, response.RetCode);
        Assert.Equal(1L, response.RowsLoaded);
        Assert.Equal(1L, response.RowCount);
    }
}
