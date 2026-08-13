// ==================================================================================================
//  QuerySendCeilingTests.cs - what C-05 answers when a response will not fit the send ceiling
// ==================================================================================================
//
//  WHAT THIS SUITE EXISTS FOR, AND WHY IT IS ITS OWN FILE. The subject is one boundary condition on
//  `Grpc/QueryService.cs`: a chunk size a caller is free to choose decides how many rows go into ONE
//  gRPC response message, and a message over `Ingress:MaxSendMessageBytes` cannot be written at all. The
//  retrieval therefore has to end - the question this suite settles is HOW it ends.
//
//  THE DEFECT IT PINS AGAINST, MEASURED RATHER THAN IMAGINED. It used to end as gRPC `OK` carrying ZERO
//  chunks. Against a table of 100,201 rows at ~352 bytes each, a chunk size of 100000 produced a message
//  of ~35 MB against a 32 MiB ceiling; the writer threw, the sink's catch treated EVERY exception as a
//  vanished peer, marking the stream broken; the broken flag then made WriteTerminalStatusAsync
//  short-circuit; and the call completed successfully with nothing on it. Through the REST projection
//  that arrived as HTTP 200 with `{"rows":[],"rowCount":"0","final":true}` - byte for byte the answer a
//  retrieval against an EMPTY table gives, with `final: true` asserting completeness. A caller had no way
//  to tell a hundred thousand rows from none, and the only server-side trace was
//  "a retrieval ended with return code -27" with no record of why.
//
//  THE PROPERTY BEING PINNED. An over-sized response never reaches the wire, so the transport and the
//  peer are both healthy - which is exactly why this is a REPORTABLE condition rather than a broken
//  stream. The sink measures each response first, refuses the over-sized one as a named condition,
//  leaves the stream writable, and the boundary reports E_BUSY with a sentence naming the setting and the
//  remedy. E_BUSY is not an arbitrary choice: DataServices maps it to ResourceExhausted and then to
//  HTTP 429, which is what the sibling ceiling one hop up (its own 4 MiB RECEIVE limit) already answers,
//  so both boundaries give one condition one answer.
//
//  WHAT IS DELIBERATELY NOT PINNED HERE. The codec's own reaction is unchanged and is asserted elsewhere:
//  it sees only `< 0`, reports its own `TransData Failed` and abandons the sequence
//  [ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L183-L185]. The ceiling is a
//  PORT-INTRODUCED bound with no legacy counterpart - the legacy hands a blob to an in-process event and
//  has no message size at all - so its reporting belongs at the service boundary and the ported arm stays
//  verbatim (constraint C-B).
// ==================================================================================================

using Grpc.Core;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using PowerFramework.Persistence.Configuration;
using PowerFramework.Persistence.Grpc;
using PowerFramework.Persistence.Tasks;
using PowerFramework.Persistence.Transactions;
using GrpcQueryService = PowerFramework.Persistence.Grpc.QueryService;

namespace PowerFramework.Persistence.Tests;

/// <summary>
/// Tests for the gRPC send ceiling C-05's result stream enforces.
/// </summary>
public sealed class QuerySendCeilingTests
{
    /// <summary>A reserved issuer name, so the host never fetches discovery metadata.</summary>
    private const string TrustedIssuer = "https://persistence.invalid/send-ceiling";

    /// <summary>The ceiling every fixture in this suite runs with, small enough for one chunk to pass it.</summary>
    private const int Ceiling = 4_096;

    /// <summary>
    /// An over-sized chunk is refused in band with <c>E_BUSY</c>, and the chunk itself never reaches the
    /// wire.
    /// </summary>
    /// <remarks>
    /// THE TWO HALVES ARE BOTH THE POINT. That the payload is absent is what makes the answer honest - a
    /// truncated chunk would be a wrong answer dressed as a right one. That a STATUS is present is what
    /// makes the failure visible at all, and it is the half the previous behaviour lost: with the stream
    /// marked broken there was nowhere to write it, so the call ended clean.
    /// </remarks>
    [Fact]
    public async Task AnOverSizedResponseIsRefusedInBandAndIsNotWritten()
    {
        using Fixture fixture = new();
        TaskHandle handle = await fixture.CreateTaskAsync();

        fixture.Runner.Payload = OverSizedState();

        CollectingStream stream = new();

        await fixture.Service.Query(
            new QueryRequest { Task = handle },
            stream,
            Fixture.Context);

        // NOTHING of the refused sequence was written.
        Assert.DoesNotContain(
            stream.Written,
            written => written.PayloadCase == QueryResponse.PayloadOneofCase.DataChunk);

        QueryResponse terminal = Assert.Single(stream.Written);

        Assert.Equal(QueryResponse.PayloadOneofCase.Status, terminal.PayloadCase);
        Assert.Equal(WireRetCode.EBusy, terminal.Status.RetCode);

        // NO DRIVER PAYLOAD, which is what makes DataServices raise this as the call status rather than
        // writing a terminal chunk that would read as a successful empty retrieval.
        Assert.Null(terminal.Status.DbError);

        // The sentence names the setting and the remedy, and quotes neither the ceiling nor the size.
        Assert.Contains(
            "Ingress:MaxSendMessageBytes",
            terminal.Status.ErrorText,
            StringComparison.Ordinal);
        Assert.Contains("SetChunkSize", terminal.Status.ErrorText, StringComparison.Ordinal);
        Assert.Contains("INCOMPLETE", terminal.Status.ErrorText, StringComparison.Ordinal);
        Assert.DoesNotContain(
            Ceiling.ToString(System.Globalization.CultureInfo.InvariantCulture),
            terminal.Status.ErrorText,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The sink answers the codec a NEGATIVE code, which is the sign the ported abandon arm tests.
    /// </summary>
    /// <remarks>
    /// ASSERTED ON THE SINK'S RETURN VALUE RATHER THAN ON THE WIRE, because this is the half that keeps the
    /// legacy behaviour intact: the codec reacts to <c>&lt; 0</c> by reporting its own
    /// <c>TransData Failed</c> and abandoning the sequence [<c>:L183-L185</c>], and a sink that answered
    /// zero for an undelivered chunk would let the loop carry on sending chunks nobody can receive.
    /// </remarks>
    [Fact]
    public async Task TheCodecIsToldTheHandOverFailed()
    {
        using Fixture fixture = new();
        TaskHandle handle = await fixture.CreateTaskAsync();

        fixture.Runner.Payload = OverSizedState();

        await fixture.Service.Query(
            new QueryRequest { Task = handle },
            new CollectingStream(),
            Fixture.Context);

        Assert.NotNull(fixture.Runner.SendOutcome);
        Assert.True(fixture.Runner.SendOutcome < 0L);
        Assert.Equal(RetCode.E_BUSY, fixture.Runner.SendOutcome);
    }

    /// <summary>
    /// A response inside the ceiling is written, and no status is manufactured behind it.
    /// </summary>
    /// <remarks>
    /// THE CONTROL CASE, AND IT GUARDS TWO SEPARATE REGRESSIONS. That an ordinary chunk still goes out
    /// unchanged says the pre-flight measurement is not refusing work it should accept; that NO terminal
    /// status follows a successful retrieval says the fix did not quietly adopt the rejected suggestion of
    /// emitting a status on every stream, which the contract declares as a terminal FAILURE arm only.
    /// </remarks>
    [Fact]
    public async Task AResponseInsideTheCeilingIsWrittenAndCarriesNoStatus()
    {
        using Fixture fixture = new();
        TaskHandle handle = await fixture.CreateTaskAsync();

        fixture.Runner.Payload = SmallState();

        CollectingStream stream = new();

        await fixture.Service.Query(
            new QueryRequest { Task = handle },
            stream,
            Fixture.Context);

        QueryResponse written = Assert.Single(stream.Written);

        Assert.Equal(QueryResponse.PayloadOneofCase.DataChunk, written.PayloadCase);
        Assert.Equal(RetCode.OK, fixture.Runner.SendOutcome);
        Assert.DoesNotContain(
            stream.Written,
            candidate => candidate.PayloadCase == QueryResponse.PayloadOneofCase.Status);
    }

    /// <summary>
    /// An adapter constructed without the ingress group enforces the group's own declared default.
    /// </summary>
    /// <remarks>
    /// THE FALLBACK IS A DEFAULT, NOT AN ABSENCE OF ONE. The parameter is optional so a unit test can build
    /// the adapter from behavioural collaborators alone, and an uninjected ceiling that meant "unbounded"
    /// would make exactly those tests exercise a code path no deployment has. Read off the group so the
    /// number has one authority.
    /// </remarks>
    [Fact]
    public async Task TheUninjectedCeilingIsTheGroupsOwnDefault()
    {
        using Fixture fixture = new(injectIngress: false);
        TaskHandle handle = await fixture.CreateTaskAsync();

        // 4 KiB is far under the group's 32 MiB default, so the same payload that trips the small ceiling
        // above must now go out untouched.
        fixture.Runner.Payload = OverSizedState();

        CollectingStream stream = new();

        await fixture.Service.Query(
            new QueryRequest { Task = handle },
            stream,
            Fixture.Context);

        QueryResponse written = Assert.Single(stream.Written);

        Assert.Equal(QueryResponse.PayloadOneofCase.DataChunk, written.PayloadCase);
        Assert.Equal(RetCode.OK, fixture.Runner.SendOutcome);
        Assert.True(new IngressOptions().MaxSendMessageBytes > Ceiling);
    }

    /// <summary>Builds a carrier state whose serialized size exceeds the suite's ceiling.</summary>
    /// <returns>The state.</returns>
    private static CarrierState OverSizedState()
    {
        CarrierState state = new();
        CarrierBufferSegment segment = new() { Buffer = DwBuffer.Primary };

        for (int row = 1; row <= 8; row++)
        {
            DataWindowRow carried = new()
            {
                Buffer = DwBuffer.Primary,
                Row = row,
                ItemStatus = ItemStatus.DataModified,
            };

            carried.Columns.Add(new ColumnValue
            {
                ColumnName = "address",
                ColumnId = 4,
                Value = new AnyValue { StringValue = new string('x', 1_024) },
                ItemStatus = ItemStatus.DataModified,
            });

            segment.Rows.Add(carried);
        }

        state.Segments.Add(segment);

        Assert.True(
            new QueryResponse { DataChunk = new QueryDataChunk { State = state } }.CalculateSize() > Ceiling,
            "The fixture payload must exceed the ceiling or the case proves nothing.");

        return state;
    }

    /// <summary>Builds a carrier state comfortably inside the suite's ceiling.</summary>
    /// <returns>The state.</returns>
    private static CarrierState SmallState()
    {
        CarrierState state = new();
        CarrierBufferSegment segment = new() { Buffer = DwBuffer.Primary };

        DataWindowRow carried = new()
        {
            Buffer = DwBuffer.Primary,
            Row = 1,
            ItemStatus = ItemStatus.DataModified,
        };

        carried.Columns.Add(new ColumnValue
        {
            ColumnName = "name",
            ColumnId = 2,
            Value = new AnyValue { StringValue = "NAME0000001" },
            ItemStatus = ItemStatus.DataModified,
        });

        segment.Rows.Add(carried);
        state.Segments.Add(segment);

        return state;
    }

    /// <summary>A runner that pushes one chunk through the sink and records what the sink answered.</summary>
    private sealed class CeilingRunner : IQueryRetrievalRunner
    {
        /// <summary>The state the single chunk carries. Nothing is sent when it is null.</summary>
        internal CarrierState? Payload { get; set; }

        /// <summary>What the sink answered the hand-over, or null when none was attempted.</summary>
        internal long? SendOutcome { get; private set; }

        public async Task<long> RunAsync(
            SqlQueryTask task,
            IQueryResultSink sink,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(task);
            ArgumentNullException.ThrowIfNull(sink);

            if (Payload is null)
            {
                return RetCode.OK;
            }

            // ONE-BASED, exactly as the codec sends it: the first chunk is 1 and the total is 1.
            long outcome = await sink
                .SendChunkAsync(new ChangesetChunk(Payload, 1L, 1L), cancellationToken)
                .ConfigureAwait(false);

            SendOutcome = outcome;

            // The ported abandon arm, reproduced faithfully so the boundary meets the same code it would
            // in production: the codec reports its own diagnostic and returns E_INTERNAL_ERROR
            // [n_cst_thread_task_sqlquery.sru:L183-L185].
            if (outcome < 0L)
            {
                sink.ReportError(RetCode.E_INTERNAL_ERROR, ChangesetCodec.TransDataFailedText);

                return RetCode.E_INTERNAL_ERROR;
            }

            return RetCode.OK;
        }
    }

    /// <summary>Records everything written to the server stream.</summary>
    private sealed class CollectingStream : IServerStreamWriter<QueryResponse>
    {
        internal List<QueryResponse> Written { get; } = [];

        public WriteOptions? WriteOptions { get; set; }

        public Task WriteAsync(QueryResponse message)
        {
            Written.Add(message);

            return Task.CompletedTask;
        }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly Host _host = new();
        private readonly TransactionPool _pool;
        private readonly PoolLease _lease;

        internal Fixture(bool injectIngress = true)
        {
            IOptions<PersistenceOptions> handleOptions = Options.Create(new PersistenceOptions());

            _pool = _host.Services.GetRequiredService<TransactionPool>();

            Sessions = new TransactionSessionRegistry(handleOptions, TimeProvider.System, _pool);
            Tasks = new QueryTaskRegistry(handleOptions, TimeProvider.System);
            Runner = new CeilingRunner();

            IQueryTaskFactory factory = _host.Services.GetRequiredService<IQueryTaskFactory>();

            // Never connected, because nothing in this suite issues a statement: the runner is a stub, so
            // no database is provisioned and none is needed (constraint C-E).
            TransactionData descriptor = new()
            {
                Dbms = "SQLite",
                ServerName = "server-1",
                Database = "send-ceiling-fixture.db",
                LogId = "account-1",
                LogPass = string.Empty,
                DbParm = string.Empty,
                Lock = string.Empty,
                AutoCommit = false,
                UserParm = string.Empty,
            };

            _lease = _pool.AddRefLease(in descriptor);
            _ = _pool.Get(_lease, out IPooledTransaction? transaction);

            TransactionSession? registered = Sessions.Register(
                _lease,
                in descriptor,
                transaction!,
                out string registrationDiagnostic);

            Assert.Equal(string.Empty, registrationDiagnostic);
            Assert.NotNull(registered);

            Session = registered!.SessionId;

            Service = injectIngress
                ? new GrpcQueryService(
                    Sessions,
                    Tasks,
                    factory,
                    Runner,
                    logger: null,
                    Options.Create(new IngressOptions { MaxSendMessageBytes = Ceiling }))
                : new GrpcQueryService(Sessions, Tasks, factory, Runner);
        }

        internal static ServerCallContext Context { get; } = new CallContext(CancellationToken.None);

        internal TransactionSessionRegistry Sessions { get; }

        internal QueryTaskRegistry Tasks { get; }

        internal CeilingRunner Runner { get; }

        internal GrpcQueryService Service { get; }

        internal string Session { get; }

        internal async Task<TaskHandle> CreateTaskAsync()
        {
            CreateQueryTaskResponse created = await Service.CreateQueryTask(
                new CreateQueryTaskRequest { Session = new SessionHandle { SessionId = Session } },
                Context);

            Assert.Equal(WireRetCode.Ok, created.Status.RetCode);
            Assert.NotNull(created.Task);

            return created.Task;
        }

        public void Dispose()
        {
            _ = _pool.RemoveRef(_lease);
            _host.Dispose();
        }

        /// <summary>A server call context whose only interesting member is its cancellation token.</summary>
        internal sealed class CallContext(CancellationToken cancellationToken) : ServerCallContext
        {
            private readonly CancellationToken _token = cancellationToken;

            protected override string MethodCore => "/persistence.v1.QueryService/Query";

            protected override string HostCore => "localhost:5101";

            protected override string PeerCore => "ipv4:127.0.0.1:0";

            protected override DateTime DeadlineCore => DateTime.MaxValue;

            protected override Metadata RequestHeadersCore { get; } = [];

            protected override CancellationToken CancellationTokenCore => _token;

            protected override Metadata ResponseTrailersCore { get; } = [];

            protected override Status StatusCore { get; set; }

            protected override WriteOptions? WriteOptionsCore { get; set; }

            protected override AuthContext AuthContextCore { get; } =
                new("fake", new Dictionary<string, List<AuthProperty>>(StringComparer.Ordinal));

            protected override IDictionary<object, object> UserStateCore { get; } =
                new Dictionary<object, object>();

            protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders) =>
                Task.CompletedTask;

            protected override ContextPropagationToken CreatePropagationTokenCore(
                ContextPropagationOptions? options) =>
                throw new NotSupportedException();
        }

        private sealed class Host : WebApplicationFactory<Program>
        {
            protected override void ConfigureWebHost(IWebHostBuilder builder)
            {
                ArgumentNullException.ThrowIfNull(builder);

                builder.UseEnvironment(Environments.Production);

                builder.UseSetting("Jwt:Authority", TrustedIssuer);
                builder.UseSetting("Jwt:Audience", "powerframework-persistence-send-ceiling");
                builder.UseSetting("Jwt:RequireHttpsMetadata", "true");

                // A directory of its own so two hosts in one run cannot contend. Nothing writes to it.
                builder.UseSetting(
                    "Sqlite:DataDirectory",
                    Path.Combine(Path.GetTempPath(), "pfw-send-ceiling-" + Guid.NewGuid().ToString("N")));
            }
        }
    }
}
