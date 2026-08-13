// ==================================================================================================
//  PersistenceClientTests - the properties of PowerFramework.DataServices.Clients.PersistenceClient
//  that a reader of the class cannot verify by reading it.
//  ------------------------------------------------------------------------------------------------
//  WHAT THIS SUITE PINS, and why each one needs a test rather than a careful reading:
//
//    1. THE NO-SILENT-OVERWRITE GUARANTEE. An aborted update surfaces its conflict detail INTACT, is
//       NOT retried, is NOT downgraded, and no path re-attempts the update. "Intact" and "not
//       retried" are both invisible from a signature, so both are asserted - including the call
//       COUNT, which is the only way to prove nothing re-attempted anything.
//
//    2. THE UNDECODABLE-ABORT PATH. When the trailer is absent, unparseable, or carries the other arm
//       of the payload, the original gRPC failure is rethrown UNCHANGED rather than reported as a
//       detail-less conflict. That is the difference between a caller that can act and one that
//       cannot.
//
//    3. PROGRESSIVE DELIVERY. The retrieval is consumed as a stream, chunk by chunk, with the count,
//       the ONE-BASED index and the full-state selector intact. Asserting the returned collection
//       would pass just as well against an implementation that buffered the whole result first, so
//       the reader below counts how many messages were pulled and the test asserts on that.
//
//    4. THE REVERSED FILTER IDENTITY ARRAY. It is transmitted in the order received. This looks like
//       a defect and is not one - see the test for the two locators - so the test exists mainly to
//       stop a future reader "fixing" it.
//
//    5. EVERY OUTBOUND CALL CARRIES A BEARER CREDENTIAL, and the client cannot be built without a
//       token provider.
//
//    6. THE REDACTION RULE. A logged database error contains no statement text and no driver text.
//
//    7. THE PRESERVED LEGACY GUARDS, each at its exact boundary and with its exact outcome code, and
//       each proven to send NOTHING when it rejects.
//
//    8. THE BUSY GUARD, including that it is RELEASED when a stream ends.
//
//  HOW IT TESTS WITHOUT A SERVER
//  ------------------------------------------------------------------------------------------------
//  Through the seam the client documents: every generated stub declares its methods `virtual` and
//  exposes a protected parameterless constructor expressly so a double can be derived. The doubles
//  below derive from the four generated clients and return hand-built gRPC call objects. Nothing here
//  opens a socket, builds a channel, or needs Persistence to exist.
// ==================================================================================================

using System.Globalization;
using Google.Protobuf;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using PowerFramework.Contracts.Common.V1;
using PowerFramework.Contracts.Persistence.V1;
using PowerFramework.DataServices.Clients;
using PowerFramework.Shared.Kernel;
using Xunit;

// THE SAME COLLISION THE CLIENT RESOLVES, RESOLVED THE SAME WAY. `RetCode` names the generated
// protobuf wrapper message on one side and the ported static constant class on the other, so a bare
// use is CS0104. Both aliases appear below on purpose: the tests assert that the two families agree
// numerically at the one place the client converts between them, which is a REVIEW-TIME INVARIANT with
// no compile-time edge.
using KernelRetCode = PowerFramework.Shared.Kernel.RetCode;
using WireRetCode = PowerFramework.Contracts.Common.V1.RetCode.Types.Value;

namespace PowerFramework.DataServices.Tests;

/// <summary>
/// A token provider that answers immediately with a non-secret placeholder credential and records how
/// many times it was asked.
/// </summary>
/// <remarks>
/// The value is deliberately NOT token-shaped. Nothing in these tests inspects the credential itself,
/// and a realistic-looking one in a test file invites being mistaken for real material.
/// </remarks>
internal sealed class PersistenceStubTokenProvider : IServiceTokenProvider
{
    /// <summary>The placeholder credential value.</summary>
    internal const string Credential = "not-a-real-credential";

    /// <summary>The token type the client prefixes onto the header value.</summary>
    internal const string TokenType = "Bearer";

    /// <summary>How many times a token was requested.</summary>
    internal int Requests { get; private set; }

    /// <summary>Every request the client made, in order, so the per-operation scope mapping can be asserted.</summary>
    internal List<ServiceTokenRequest> AllRequests { get; } = [];

    /// <summary>The last request the client made, so its subject, audience and scopes can be asserted.</summary>
    internal ServiceTokenRequest? LastRequest { get; private set; }

    /// <summary>The scopes to report as GRANTED, or null to grant everything requested.</summary>
    internal IReadOnlyList<string>? GrantedScopes { get; set; }

    /// <inheritdoc/>
    public ValueTask<ServiceToken> GetTokenAsync(
        ServiceTokenRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        Requests++;
        LastRequest = request;
        AllRequests.Add(request);

        return ValueTask.FromResult(new ServiceToken(
            Credential,
            TokenType,
            DateTimeOffset.UnixEpoch.AddYears(60),
            GrantedScopes ?? request.Scopes));
    }
}

/// <summary>
/// A logger that keeps every formatted record, so the redaction rule can be asserted rather than
/// assumed.
/// </summary>
internal sealed class PersistenceRecordingLogger : ILogger<PersistenceClient>
{
    /// <summary>Every record written, formatted exactly as a provider would see it.</summary>
    internal List<string> Records { get; } = [];

    /// <inheritdoc/>
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    /// <summary>
    /// The lowest level this logger reports as enabled. The default admits everything, which is the
    /// strictest setting for a redaction test because it forces every level-guarded diagnostic to run.
    /// Raising it lets a test drive the suppressed arm instead.
    /// </summary>
    internal LogLevel MinimumLevel { get; set; } = LogLevel.Trace;

    /// <inheritdoc/>
    public bool IsEnabled(LogLevel logLevel) => logLevel >= MinimumLevel;

    /// <inheritdoc/>
    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);

        Records.Add(formatter(state, exception));
    }

    /// <summary>Every record joined, for a single containment assertion.</summary>
    /// <returns>The joined text.</returns>
    internal string AllText() => string.Join("\n", Records);
}

/// <summary>
/// A server-stream reader over a fixed message list that records how many messages were PULLED.
/// </summary>
/// <typeparam name="T">The message type.</typeparam>
internal sealed class PersistenceListStreamReader<T> : IAsyncStreamReader<T>
    where T : class
{
    private readonly IReadOnlyList<T> _messages;
    private int _index = -1;

    /// <summary>Creates the reader.</summary>
    /// <param name="messages">The messages to yield, in order.</param>
    internal PersistenceListStreamReader(IReadOnlyList<T> messages) => _messages = messages;

    /// <summary>How many times the consumer asked for a message.</summary>
    /// <remarks>
    /// THIS IS THE STREAMING ASSERTION. A client that materialised the whole stream before returning
    /// would drive this to the end of the list before the caller saw its first message, so comparing
    /// this count against what the caller has consumed so far is what distinguishes streaming from
    /// buffering.
    /// </remarks>
    internal int Pulls { get; private set; }

    /// <inheritdoc/>
    public T Current => _messages[_index];

    /// <inheritdoc/>
    public Task<bool> MoveNext(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Pulls++;
        _index++;

        return Task.FromResult(_index < _messages.Count);
    }
}

/// <summary>
/// Hand-built gRPC call objects, so a double can answer without a channel.
/// </summary>
internal static class PersistenceCallFactory
{
    /// <summary>Wraps an answer as a unary call.</summary>
    /// <typeparam name="TResponse">The response type.</typeparam>
    /// <param name="answer">The answer, which may be a faulted task.</param>
    /// <param name="trailers">The trailers a caller may read, or null for none.</param>
    /// <returns>The call.</returns>
    internal static AsyncUnaryCall<TResponse> Unary<TResponse>(
        Task<TResponse> answer,
        Metadata? trailers = null)
        => new(
            answer,
            Task.FromResult(new Metadata()),
            static () => Status.DefaultSuccess,
            () => trailers ?? [],
            static () => { });

    /// <summary>Wraps a reader as a server-streaming call.</summary>
    /// <typeparam name="TResponse">The response type.</typeparam>
    /// <param name="reader">The reader.</param>
    /// <returns>The call.</returns>
    internal static AsyncServerStreamingCall<TResponse> ServerStreaming<TResponse>(
        IAsyncStreamReader<TResponse> reader)
        => new(
            reader,
            Task.FromResult(new Metadata()),
            static () => Status.DefaultSuccess,
            static () => [],
            static () => { });
}

/// <summary>
/// A double for the generated C-05 stub, recording what was sent and with which call options.
/// </summary>
/// <summary>
/// The task identifiers the wire-level stubs mint, one per task-owning contract.
/// </summary>
/// <remarks>
/// ONE PER CONTRACT RATHER THAN ONE SHARED VALUE, AND THE DISTINCTION IS LOAD-BEARING. A real
/// Persistence issues a query task, an update task and a command task independently, and each handle
/// addresses work only its own contract can run - C-05 answers E_INVALID_HANDLE for an update task's
/// identifier as readily as for a blank one. A single shared spelling makes a service that carried the
/// WRONG handle to the right contract indistinguishable from one that carried the right handle, which
/// is the exact defect a task-handle assertion exists to catch. Distinct spellings make the mix-up
/// visible in the failure message instead.
/// </remarks>
internal static class PersistenceStubTaskIds
{
    /// <summary>The handle C-05's stub issues for a query task.</summary>
    internal const string Query = "query-task-under-test";

    /// <summary>The handle C-06's stub issues for an update task.</summary>
    internal const string Update = "update-task-under-test";

    /// <summary>The handle C-07's stub issues for a command task.</summary>
    internal const string Command = "command-task-under-test";
}

internal sealed class FakeQueryServiceClient : QueryService.QueryServiceClient
{
    /// <summary>The messages the next retrieval should yield.</summary>
    internal List<QueryResponse> StreamMessages { get; } = [];

    /// <summary>The reader handed to the last retrieval, for the streaming assertions.</summary>
    internal PersistenceListStreamReader<QueryResponse>? LastReader { get; private set; }

    /// <summary>Every chunk-size request that actually reached the wire.</summary>
    internal List<SetChunkSizeRequest> ChunkSizeRequests { get; } = [];

    /// <summary>Every maximum-rows request that actually reached the wire.</summary>
    internal List<SetMaxRowsRequest> MaxRowsRequests { get; } = [];

    /// <summary>Every where-clause request that actually reached the wire.</summary>
    internal List<SetWhereClauseRequest> WhereClauseRequests { get; } = [];

    /// <summary>Every order-by-clause request that actually reached the wire.</summary>
    internal List<SetOrderByClauseRequest> OrderByClauseRequests { get; } = [];

    /// <summary>Every unique-index-column request that actually reached the wire.</summary>
    internal List<SetPagedUniqueIndexColumnsRequest> UniqueIndexRequests { get; } = [];

    /// <summary>Every paging request that actually reached the wire.</summary>
    internal List<SetPagingRequest> PagingRequests { get; } = [];

    /// <summary>Every query-task creation that actually reached the wire, in call order.</summary>
    /// <remarks>
    /// THE REQUEST ITSELF AND NOT MERELY THE METHOD NAME, because C-05 carries the task's INITIAL
    /// CONFIGURATION on this call - so "the spec travelled on the create" is only assertable against the
    /// recorded request. The name alone would keep passing if the spec had been dropped.
    /// </remarks>
    internal List<CreateQueryTaskRequest> CreateTaskRequests { get; } = [];

    /// <summary>Every query-task release that actually reached the wire, in call order.</summary>
    internal List<ReleaseQueryTaskRequest> ReleaseTaskRequests { get; } = [];

    /// <summary>How many retrievals were started.</summary>
    internal int QueryCalls { get; private set; }

    /// <summary>The call options of the most recent call of any kind.</summary>
    internal CallOptions? LastOptions { get; private set; }

    /// <summary>Every method invoked, by name, so the wiring of each member can be asserted.</summary>
    internal List<string> Calls { get; } = [];

    /// <inheritdoc/>
    public override AsyncUnaryCall<CreateQueryTaskResponse> CreateQueryTaskAsync(
        CreateQueryTaskRequest request,
        CallOptions options)
    {
        Calls.Add(nameof(CreateQueryTaskAsync));
        CreateTaskRequests.Add(request);
        LastOptions = options;

        return PersistenceCallFactory.Unary(Task.FromResult(new CreateQueryTaskResponse
        {
            Status = new OperationStatus { RetCode = WireRetCode.Ok },
            Task = new TaskHandle { TaskId = PersistenceStubTaskIds.Query },
        }));
    }

    /// <inheritdoc/>
    public override AsyncUnaryCall<ReleaseQueryTaskResponse> ReleaseQueryTaskAsync(
        ReleaseQueryTaskRequest request,
        CallOptions options)
    {
        Calls.Add(nameof(ReleaseQueryTaskAsync));
        ReleaseTaskRequests.Add(request);
        LastOptions = options;

        return PersistenceCallFactory.Unary(Task.FromResult(new ReleaseQueryTaskResponse
        {
            Status = new OperationStatus { RetCode = WireRetCode.Ok },
        }));
    }

    /// <inheritdoc/>
    public override AsyncUnaryCall<ResetQueryTaskResponse> ResetAsync(
        ResetQueryTaskRequest request,
        CallOptions options)
    {
        Calls.Add(nameof(ResetAsync));
        LastOptions = options;

        return PersistenceCallFactory.Unary(Task.FromResult(new ResetQueryTaskResponse
        {
            Status = new OperationStatus { RetCode = WireRetCode.Ok },
        }));
    }

    /// <inheritdoc/>
    public override AsyncUnaryCall<CountResponse> CountAsync(CountRequest request, CallOptions options)
    {
        Calls.Add(nameof(CountAsync));
        LastOptions = options;

        return PersistenceCallFactory.Unary(Task.FromResult(new CountResponse
        {
            Status = new OperationStatus { RetCode = WireRetCode.Ok },
            PageCount = 3L,
            RecordCount = 27L,
            Counted = true,
        }));
    }

    /// <inheritdoc/>
    public override AsyncServerStreamingCall<QueryResponse> Query(QueryRequest request, CallOptions options)
    {
        QueryCalls++;
        LastOptions = options;

        PersistenceListStreamReader<QueryResponse> reader = new(StreamMessages);
        LastReader = reader;

        return PersistenceCallFactory.ServerStreaming(reader);
    }

    /// <inheritdoc/>
    public override AsyncUnaryCall<SetChunkSizeResponse> SetChunkSizeAsync(
        SetChunkSizeRequest request,
        CallOptions options)
    {
        ChunkSizeRequests.Add(request);
        LastOptions = options;

        return PersistenceCallFactory.Unary(Task.FromResult(new SetChunkSizeResponse
        {
            Status = new OperationStatus { RetCode = WireRetCode.Ok },
        }));
    }

    /// <inheritdoc/>
    public override AsyncUnaryCall<SetMaxRowsResponse> SetMaxRowsAsync(
        SetMaxRowsRequest request,
        CallOptions options)
    {
        MaxRowsRequests.Add(request);
        LastOptions = options;

        return PersistenceCallFactory.Unary(Task.FromResult(new SetMaxRowsResponse
        {
            Status = new OperationStatus { RetCode = WireRetCode.Ok },
        }));
    }

    /// <inheritdoc/>
    public override AsyncUnaryCall<SetWhereClauseResponse> SetWhereClauseAsync(
        SetWhereClauseRequest request,
        CallOptions options)
    {
        WhereClauseRequests.Add(request);
        LastOptions = options;

        return PersistenceCallFactory.Unary(Task.FromResult(new SetWhereClauseResponse
        {
            Status = new OperationStatus { RetCode = WireRetCode.Ok },
        }));
    }

    /// <inheritdoc/>
    public override AsyncUnaryCall<SetOrderByClauseResponse> SetOrderByClauseAsync(
        SetOrderByClauseRequest request,
        CallOptions options)
    {
        OrderByClauseRequests.Add(request);
        LastOptions = options;

        return PersistenceCallFactory.Unary(Task.FromResult(new SetOrderByClauseResponse
        {
            Status = new OperationStatus { RetCode = WireRetCode.Ok },
        }));
    }

    /// <inheritdoc/>
    public override AsyncUnaryCall<SetPagingResponse> SetPagingAsync(
        SetPagingRequest request,
        CallOptions options)
    {
        PagingRequests.Add(request);
        LastOptions = options;

        return PersistenceCallFactory.Unary(Task.FromResult(new SetPagingResponse
        {
            Status = new OperationStatus { RetCode = WireRetCode.Ok },
        }));
    }

    /// <inheritdoc/>
    public override AsyncUnaryCall<SetPagedUniqueIndexColumnsResponse> SetPagedUniqueIndexColumnsAsync(
        SetPagedUniqueIndexColumnsRequest request,
        CallOptions options)
    {
        UniqueIndexRequests.Add(request);
        LastOptions = options;

        return PersistenceCallFactory.Unary(Task.FromResult(new SetPagedUniqueIndexColumnsResponse
        {
            Status = new OperationStatus { RetCode = WireRetCode.Ok },
        }));
    }
}

/// <summary>
/// A double for the generated C-06 stub. It can answer, or fail with a status the test chooses.
/// </summary>
internal sealed class FakeUpdateServiceClient : UpdateService.UpdateServiceClient
{
    /// <summary>The failure the next update should raise, or null to answer normally.</summary>
    internal RpcException? UpdateFailure { get; set; }

    /// <summary>The answer the next update should give when it is not failing.</summary>
    internal UpdateResponse UpdateAnswer { get; set; } = new()
    {
        Status = new OperationStatus { RetCode = WireRetCode.Ok },
        Counts = new UpdateCounts(),
    };

    /// <summary>How many updates were attempted. The re-attempt assertion reads this.</summary>
    internal int UpdateCalls { get; private set; }

    /// <summary>Every prepare request that actually reached the wire.</summary>
    internal List<PrepareUpdateRequest> PrepareRequests { get; } = [];

    /// <summary>Every update-task creation that actually reached the wire, in call order.</summary>
    internal List<CreateUpdateTaskRequest> CreateTaskRequests { get; } = [];

    /// <summary>
    /// The outcome code the next update-task creation answers with. <c>0</c> is <c>RetCode.OK</c>.
    /// </summary>
    /// <remarks>
    /// A FAILING CODE ANSWERS WITHOUT A HANDLE, as a producer must. It is how the acquisition's SECOND
    /// refusal is reached - the one where a session HAS been obtained and must still be ended - which no
    /// assertion about an update's own answer can distinguish from the first.
    /// </remarks>
    internal long CreateTaskCode { get; set; }

    /// <summary>Every update-task release that actually reached the wire, in call order.</summary>
    internal List<ReleaseUpdateTaskRequest> ReleaseTaskRequests { get; } = [];

    /// <summary>The call options of the most recent call.</summary>
    internal CallOptions? LastOptions { get; private set; }

    /// <summary>Every method invoked, by name.</summary>
    internal List<string> Calls { get; } = [];

    /// <inheritdoc/>
    public override AsyncUnaryCall<CreateUpdateTaskResponse> CreateUpdateTaskAsync(
        CreateUpdateTaskRequest request,
        CallOptions options)
    {
        Calls.Add(nameof(CreateUpdateTaskAsync));
        CreateTaskRequests.Add(request);
        LastOptions = options;

        if (CreateTaskCode != 0L)
        {
            return PersistenceCallFactory.Unary(Task.FromResult(new CreateUpdateTaskResponse
            {
                Status = new OperationStatus
                {
                    RetCode = (WireRetCode)(int)CreateTaskCode,
                    ErrorText = "Scripted: the update task was refused.",
                },
            }));
        }

        return PersistenceCallFactory.Unary(Task.FromResult(new CreateUpdateTaskResponse
        {
            Status = new OperationStatus { RetCode = WireRetCode.Ok },
            Task = new TaskHandle { TaskId = PersistenceStubTaskIds.Update },
        }));
    }

    /// <inheritdoc/>
    public override AsyncUnaryCall<ReleaseUpdateTaskResponse> ReleaseUpdateTaskAsync(
        ReleaseUpdateTaskRequest request,
        CallOptions options)
    {
        Calls.Add(nameof(ReleaseUpdateTaskAsync));
        ReleaseTaskRequests.Add(request);
        LastOptions = options;

        return PersistenceCallFactory.Unary(Task.FromResult(new ReleaseUpdateTaskResponse
        {
            Status = new OperationStatus { RetCode = WireRetCode.Ok },
        }));
    }

    /// <inheritdoc/>
    public override AsyncUnaryCall<ResetUpdateTaskResponse> ResetAsync(
        ResetUpdateTaskRequest request,
        CallOptions options)
    {
        Calls.Add(nameof(ResetAsync));
        LastOptions = options;

        return PersistenceCallFactory.Unary(Task.FromResult(new ResetUpdateTaskResponse
        {
            Status = new OperationStatus { RetCode = WireRetCode.Ok },
        }));
    }

    /// <inheritdoc/>
    public override AsyncUnaryCall<UpdateResponse> UpdateAsync(UpdateRequest request, CallOptions options)
    {
        UpdateCalls++;
        LastOptions = options;

        return UpdateFailure is null
            ? PersistenceCallFactory.Unary(Task.FromResult(UpdateAnswer))
            : PersistenceCallFactory.Unary(
                Task.FromException<UpdateResponse>(UpdateFailure),
                UpdateFailure.Trailers);
    }

    /// <inheritdoc/>
    public override AsyncUnaryCall<PrepareUpdateResponse> PrepareUpdateAsync(
        PrepareUpdateRequest request,
        CallOptions options)
    {
        PrepareRequests.Add(request);
        LastOptions = options;

        return PersistenceCallFactory.Unary(Task.FromResult(new PrepareUpdateResponse
        {
            Status = new OperationStatus { RetCode = WireRetCode.Ok },
        }));
    }
}

/// <summary>
/// A double for the generated C-07 stub.
/// </summary>
internal sealed class FakeCommandServiceClient : CommandService.CommandServiceClient
{
    /// <summary>Every statement request that actually reached the wire.</summary>
    internal List<SetCommandSqlRequest> SqlRequests { get; } = [];

    /// <summary>Every autocommit request that actually reached the wire.</summary>
    internal List<SetCommandAutoCommitRequest> AutoCommitRequests { get; } = [];

    /// <summary>Every execution request that actually reached the wire.</summary>
    internal List<ExecRequest> ExecRequests { get; } = [];

    /// <summary>The answer the next execution should give.</summary>
    internal ExecResponse ExecAnswer { get; set; } = new()
    {
        Status = new OperationStatus { RetCode = WireRetCode.Ok },
    };

    /// <summary>The call options of the most recent call.</summary>
    internal CallOptions? LastOptions { get; private set; }

    /// <summary>Every method invoked, by name.</summary>
    internal List<string> Calls { get; } = [];

    /// <inheritdoc/>
    public override AsyncUnaryCall<CreateCommandTaskResponse> CreateCommandTaskAsync(
        CreateCommandTaskRequest request,
        CallOptions options)
    {
        Calls.Add(nameof(CreateCommandTaskAsync));
        LastOptions = options;

        return PersistenceCallFactory.Unary(Task.FromResult(new CreateCommandTaskResponse
        {
            Status = new OperationStatus { RetCode = WireRetCode.Ok },
            Task = new TaskHandle { TaskId = PersistenceStubTaskIds.Command },
        }));
    }

    /// <inheritdoc/>
    public override AsyncUnaryCall<ReleaseCommandTaskResponse> ReleaseCommandTaskAsync(
        ReleaseCommandTaskRequest request,
        CallOptions options)
    {
        Calls.Add(nameof(ReleaseCommandTaskAsync));
        LastOptions = options;

        return PersistenceCallFactory.Unary(Task.FromResult(new ReleaseCommandTaskResponse
        {
            Status = new OperationStatus { RetCode = WireRetCode.Ok },
        }));
    }

    /// <inheritdoc/>
    public override AsyncUnaryCall<ResetCommandTaskResponse> ResetAsync(
        ResetCommandTaskRequest request,
        CallOptions options)
    {
        Calls.Add(nameof(ResetAsync));
        LastOptions = options;

        return PersistenceCallFactory.Unary(Task.FromResult(new ResetCommandTaskResponse
        {
            Status = new OperationStatus { RetCode = WireRetCode.Ok },
        }));
    }

    /// <inheritdoc/>
    public override AsyncUnaryCall<SetCommandSqlResponse> SetSqlAsync(
        SetCommandSqlRequest request,
        CallOptions options)
    {
        SqlRequests.Add(request);
        LastOptions = options;

        return PersistenceCallFactory.Unary(Task.FromResult(new SetCommandSqlResponse
        {
            Status = new OperationStatus { RetCode = WireRetCode.Ok },
        }));
    }

    /// <inheritdoc/>
    public override AsyncUnaryCall<SetCommandAutoCommitResponse> SetAutoCommitAsync(
        SetCommandAutoCommitRequest request,
        CallOptions options)
    {
        AutoCommitRequests.Add(request);
        LastOptions = options;

        return PersistenceCallFactory.Unary(Task.FromResult(new SetCommandAutoCommitResponse
        {
            Status = new OperationStatus { RetCode = WireRetCode.Ok },
        }));
    }

    /// <inheritdoc/>
    public override AsyncUnaryCall<ExecResponse> ExecAsync(ExecRequest request, CallOptions options)
    {
        ExecRequests.Add(request);
        LastOptions = options;

        return PersistenceCallFactory.Unary(Task.FromResult(ExecAnswer));
    }
}

/// <summary>
/// A double for the generated C-08 stub.
/// </summary>
internal sealed class FakeTransactionServiceClient : TransactionService.TransactionServiceClient
{
    /// <summary>Every session-begin request that reached the wire.</summary>
    internal List<BeginSessionRequest> BeginRequests { get; } = [];

    /// <summary>Every commit request that reached the wire.</summary>
    internal List<CommitRequest> CommitRequests { get; } = [];

    /// <summary>Every session-end request that reached the wire, in call order.</summary>
    /// <remarks>
    /// PAIRED WITH <see cref="BeginRequests"/> ON PURPOSE: a count that does not match is a session leak,
    /// and a leak is invisible to every assertion about the operation's own answer.
    /// </remarks>
    internal List<EndSessionRequest> EndRequests { get; } = [];

    /// <summary>The call options of the most recent call.</summary>
    internal CallOptions? LastOptions { get; private set; }

    /// <summary>Every method invoked, by name.</summary>
    internal List<string> Calls { get; } = [];

    /// <inheritdoc/>
    public override AsyncUnaryCall<EndSessionResponse> EndSessionAsync(
        EndSessionRequest request,
        CallOptions options)
    {
        Calls.Add(nameof(EndSessionAsync));
        EndRequests.Add(request);
        LastOptions = options;

        return PersistenceCallFactory.Unary(Task.FromResult(new EndSessionResponse
        {
            Status = new OperationStatus { RetCode = WireRetCode.Ok },
        }));
    }

    /// <inheritdoc/>
    public override AsyncUnaryCall<GetTransactionDataResponse> GetTransactionDataAsync(
        GetTransactionDataRequest request,
        CallOptions options)
    {
        Calls.Add(nameof(GetTransactionDataAsync));
        LastOptions = options;

        return PersistenceCallFactory.Unary(Task.FromResult(new GetTransactionDataResponse
        {
            Status = new OperationStatus { RetCode = WireRetCode.Ok },
            Descriptor_ = new TransactionDescriptorView
            {
                Dbms = "SNC",
                Logid = "pfw-log-identity",
                Flags = new ConnectionParameterFlags { DisableBind = true, NcharBind = true },
            },
        }));
    }

    /// <inheritdoc/>
    public override AsyncUnaryCall<SetTransactionAutoCommitResponse> SetAutoCommitAsync(
        SetTransactionAutoCommitRequest request,
        CallOptions options)
    {
        Calls.Add(nameof(SetAutoCommitAsync));
        LastOptions = options;

        return PersistenceCallFactory.Unary(Task.FromResult(new SetTransactionAutoCommitResponse
        {
            Status = new OperationStatus { RetCode = WireRetCode.Ok },
        }));
    }

    /// <inheritdoc/>
    public override AsyncUnaryCall<AutoCommitResponse> AutoCommitAsync(
        AutoCommitRequest request,
        CallOptions options)
    {
        Calls.Add(nameof(AutoCommitAsync));
        LastOptions = options;

        return PersistenceCallFactory.Unary(Task.FromResult(new AutoCommitResponse
        {
            Status = new OperationStatus { RetCode = WireRetCode.Ok },
        }));
    }

    /// <inheritdoc/>
    public override AsyncUnaryCall<RollbackResponse> RollbackAsync(
        RollbackRequest request,
        CallOptions options)
    {
        Calls.Add(nameof(RollbackAsync));
        LastOptions = options;

        return PersistenceCallFactory.Unary(Task.FromResult(new RollbackResponse
        {
            Status = new OperationStatus { RetCode = WireRetCode.Failed },
        }));
    }

    /// <inheritdoc/>
    public override AsyncUnaryCall<IsConnectedResponse> IsConnectedAsync(
        IsConnectedRequest request,
        CallOptions options)
    {
        Calls.Add(nameof(IsConnectedAsync));
        LastOptions = options;

        return PersistenceCallFactory.Unary(Task.FromResult(new IsConnectedResponse
        {
            Status = new OperationStatus { RetCode = WireRetCode.Ok },
            Connected = true,
            Probed = true,
        }));
    }

    /// <inheritdoc/>
    public override AsyncUnaryCall<GetDatabaseTypeResponse> GetDatabaseTypeAsync(
        GetDatabaseTypeRequest request,
        CallOptions options)
    {
        Calls.Add(nameof(GetDatabaseTypeAsync));
        LastOptions = options;

        return PersistenceCallFactory.Unary(Task.FromResult(new GetDatabaseTypeResponse
        {
            Status = new OperationStatus { RetCode = WireRetCode.Ok },
            DatabaseType = DatabaseType.DbtOracle,
        }));
    }

    /// <inheritdoc/>
    public override AsyncUnaryCall<GetSessionStateResponse> GetSessionStateAsync(
        GetSessionStateRequest request,
        CallOptions options)
    {
        Calls.Add(nameof(GetSessionStateAsync));
        LastOptions = options;

        return PersistenceCallFactory.Unary(Task.FromResult(new GetSessionStateResponse
        {
            Status = new OperationStatus { RetCode = WireRetCode.Ok },
            SqlCode = 100L,
            Succeeded = true,
        }));
    }

    /// <inheritdoc/>
    public override AsyncUnaryCall<ClearStateResponse> ClearStateAsync(
        ClearStateRequest request,
        CallOptions options)
    {
        Calls.Add(nameof(ClearStateAsync));
        LastOptions = options;

        return PersistenceCallFactory.Unary(Task.FromResult(new ClearStateResponse
        {
            Status = new OperationStatus { RetCode = WireRetCode.Ok },
        }));
    }

    /// <inheritdoc/>
    public override AsyncUnaryCall<SetBrokenResponse> SetBrokenAsync(
        SetBrokenRequest request,
        CallOptions options)
    {
        Calls.Add(nameof(SetBrokenAsync));
        LastOptions = options;

        return PersistenceCallFactory.Unary(Task.FromResult(new SetBrokenResponse
        {
            Status = new OperationStatus { RetCode = WireRetCode.Ok },
        }));
    }

    /// <inheritdoc/>
    public override AsyncUnaryCall<GridSyntaxFromSqlResponse> GridSyntaxFromSqlAsync(
        GridSyntaxFromSqlRequest request,
        CallOptions options)
    {
        Calls.Add(nameof(GridSyntaxFromSqlAsync));
        LastOptions = options;

        return PersistenceCallFactory.Unary(Task.FromResult(new GridSyntaxFromSqlResponse
        {
            Status = new OperationStatus { RetCode = WireRetCode.Ok },
            Syntax = "table(column=(...))",
        }));
    }

    /// <inheritdoc/>
    public override AsyncUnaryCall<BeginSessionResponse> BeginSessionAsync(
        BeginSessionRequest request,
        CallOptions options)
    {
        BeginRequests.Add(request);
        LastOptions = options;

        return PersistenceCallFactory.Unary(Task.FromResult(new BeginSessionResponse
        {
            Status = new OperationStatus { RetCode = WireRetCode.Ok },
            Session = new SessionHandle { SessionId = "session-under-test" },
        }));
    }

    /// <inheritdoc/>
    public override AsyncUnaryCall<CommitResponse> CommitAsync(CommitRequest request, CallOptions options)
    {
        CommitRequests.Add(request);
        LastOptions = options;

        return PersistenceCallFactory.Unary(Task.FromResult(new CommitResponse
        {
            Status = new OperationStatus { RetCode = WireRetCode.Ok },
        }));
    }
}

/// <summary>
/// The suite.
/// </summary>
public sealed class PersistenceClientTests
{
    /// <summary>The task identifier this suite SENDS on a request it builds itself.</summary>
    /// <remarks>
    /// A CALLER-SUPPLIED HANDLE, DELIBERATELY DISTINCT FROM EVERY MINTED ONE. The stubs mint their own
    /// handles per contract - see <see cref="PersistenceStubTaskIds"/> - and this value is the other
    /// direction: what a caller puts ON a request. Keeping the two vocabularies separate is what stops an
    /// assertion from passing because a value travelled in a circle rather than because the client
    /// forwarded what it was given.
    /// </remarks>
    private const string TaskId = "caller-supplied-task-under-test";

    /// <summary>
    /// Builds a client over fresh doubles.
    /// </summary>
    /// <returns>The client and its four doubles, the token provider and the logger.</returns>
    private static (
        PersistenceClient Client,
        FakeQueryServiceClient Query,
        FakeUpdateServiceClient Update,
        FakeCommandServiceClient Command,
        FakeTransactionServiceClient Transaction,
        PersistenceStubTokenProvider Tokens,
        PersistenceRecordingLogger Logger) CreateClient()
    {
        FakeQueryServiceClient query = new();
        FakeUpdateServiceClient update = new();
        FakeCommandServiceClient command = new();
        FakeTransactionServiceClient transaction = new();
        PersistenceStubTokenProvider tokens = new();
        PersistenceRecordingLogger logger = new();

        PersistenceClient client = new(query, update, command, transaction, tokens, logger);

        return (client, query, update, command, transaction, tokens, logger);
    }

    /// <summary>A task handle for the identifier every test uses.</summary>
    /// <returns>The handle.</returns>
    private static TaskHandle Handle() => new() { TaskId = TaskId };

    /// <summary>
    /// The trailer key the contract declares, read from the generated descriptor rather than written as
    /// a literal.
    /// </summary>
    /// <returns>The binding.</returns>
    private static RichErrorBinding Binding()
    {
        Google.Protobuf.Reflection.MethodDescriptor? method =
            UpdateService.Descriptor.FindMethodByName("Update");
        RichErrorBinding? binding = method?.GetOptions()?.GetExtension(CommonV1Extensions.RichError);

        Assert.NotNull(binding);

        return binding;
    }

    /// <summary>Builds an aborted failure carrying a rich-error trailer.</summary>
    /// <param name="payload">The trailer bytes.</param>
    /// <returns>The failure.</returns>
    private static RpcException AbortedWith(byte[] payload)
    {
        Metadata trailers = [];
        trailers.Add(Binding().TrailerKey, payload);

        return new RpcException(new Status(StatusCode.Aborted, "conflict"), trailers);
    }

    /// <summary>Builds a conflict detail with two rows on the only evidenced update table.</summary>
    /// <returns>The detail.</returns>
    private static ConflictDetail SampleConflict() => new()
    {
        UpdateTable = "COMPANY",
        RowsExpected = 2L,
        RowsMatched = 1L,
        Rows =
        {
            new ConflictRow
            {
                Buffer = DwBuffer.Primary,
                Row = 4L,
                ItemStatus = ItemStatus.DataModified,
                CurrentValues =
                {
                    new ColumnValue
                    {
                        ColumnName = "salary",
                        ColumnId = 5L,
                        Value = new AnyValue { DoubleValue = 21_000d },
                    },
                },
                OriginalValues =
                {
                    new ColumnValue
                    {
                        ColumnName = "salary",
                        ColumnId = 5L,
                        Value = new AnyValue { DoubleValue = 20_000d },
                    },
                },
            },
        },
    };

    // ==============================================================================================
    //  1 - THE NO-SILENT-OVERWRITE GUARANTEE
    // ==============================================================================================

    /// <summary>
    /// An aborted update surfaces the conflict detail INTACT, exactly once, and is never re-attempted.
    /// </summary>
    /// <returns>The test.</returns>
    [Fact]
    public async Task UpdateAsync_SurfacesTheConflictDetailIntactAndNeverRetries()
    {
        (PersistenceClient client, _, FakeUpdateServiceClient update, _, _, _, _) = CreateClient();

        ConflictDetail detail = SampleConflict();
        update.UpdateFailure = AbortedWith(new RichErrorTrailer
        {
            Conflict = detail,
            RetCode = KernelRetCode.E_DB_ERROR,
        }.ToByteArray());

        PersistenceConflictException failure =
            await Assert.ThrowsAsync<PersistenceConflictException>(() => client.UpdateAsync(
                new UpdateRequest { Task = Handle(), UpdateRows = 1L },
                TestContext.Current.CancellationToken));

        // INTACT means field for field, including both value sets per row - under updatewhere=1 the
        // failed statement compared the ORIGINAL value of every marked column, so a payload without
        // them could not tell a caller which column moved.
        Assert.NotNull(failure.Conflict);
        Assert.Equal("COMPANY", failure.Conflict.UpdateTable);
        Assert.Equal(2L, failure.Conflict.RowsExpected);
        Assert.Equal(1L, failure.Conflict.RowsMatched);
        ConflictRow row = Assert.Single(failure.Conflict.Rows);
        Assert.Equal(DwBuffer.Primary, row.Buffer);
        Assert.Equal(ItemStatus.DataModified, row.ItemStatus);
        Assert.Equal(21_000d, Assert.Single(row.CurrentValues).Value.DoubleValue);
        Assert.Equal(20_000d, Assert.Single(row.OriginalValues).Value.DoubleValue);
        Assert.Equal(detail, failure.Conflict);

        // NOT DOWNGRADED: the reconciled code and the originating status both survive.
        Assert.Equal(KernelRetCode.E_DB_ERROR, failure.RetCode);
        Assert.Equal(StatusCode.Aborted, failure.Status.StatusCode);
        Assert.NotNull(failure.Trailers);
        Assert.IsType<RpcException>(failure.InnerException);

        // NOT RETRIED, AND NOT RE-ATTEMPTED. The call count is the only proof of that.
        Assert.Equal(1, update.UpdateCalls);
    }

    /// <summary>
    /// The conflict message carries counts only - never a column value and never a statement.
    /// </summary>
    /// <returns>The test.</returns>
    [Fact]
    public async Task UpdateAsync_ConflictMessageCarriesNoCallerData()
    {
        (PersistenceClient client, _, FakeUpdateServiceClient update, _, _, _, _) = CreateClient();

        update.UpdateFailure = AbortedWith(new RichErrorTrailer
        {
            Conflict = SampleConflict(),
            RetCode = KernelRetCode.E_DB_ERROR,
        }.ToByteArray());

        PersistenceConflictException failure =
            await Assert.ThrowsAsync<PersistenceConflictException>(() => client.UpdateAsync(
                new UpdateRequest { Task = Handle(), UpdateRows = 1L },
                TestContext.Current.CancellationToken));

        Assert.Contains("COMPANY", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("21000", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("20000", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("salary", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// An aborted status with NO trailer is rethrown unchanged rather than reported as a detail-less
    /// conflict.
    /// </summary>
    /// <returns>The test.</returns>
    [Fact]
    public async Task UpdateAsync_RethrowsAnAbortWithNoTrailerUnchanged()
    {
        (PersistenceClient client, _, FakeUpdateServiceClient update, _, _, _, _) = CreateClient();

        RpcException raised = new(new Status(StatusCode.Aborted, "conflict"));
        update.UpdateFailure = raised;

        RpcException observed = await Assert.ThrowsAsync<RpcException>(() => client.UpdateAsync(
            new UpdateRequest { Task = Handle(), UpdateRows = 1L },
            TestContext.Current.CancellationToken));

        Assert.Same(raised, observed);
        Assert.Equal(1, update.UpdateCalls);
    }

    /// <summary>
    /// An aborted status whose trailer cannot be parsed is rethrown unchanged, with its trailers intact.
    /// </summary>
    /// <returns>The test.</returns>
    [Fact]
    public async Task UpdateAsync_RethrowsAnAbortWhoseTrailerCannotBeParsed()
    {
        (PersistenceClient client, _, FakeUpdateServiceClient update, _, _, _, _) = CreateClient();

        // A byte sequence that is not a valid protobuf message for this type: field 1 declared as a
        // length-delimited value whose declared length runs past the end of the buffer.
        update.UpdateFailure = AbortedWith([0x0A, 0x7F, 0x01]);

        RpcException observed = await Assert.ThrowsAsync<RpcException>(() => client.UpdateAsync(
            new UpdateRequest { Task = Handle(), UpdateRows = 1L },
            TestContext.Current.CancellationToken));

        Assert.Equal(StatusCode.Aborted, observed.StatusCode);
        Assert.NotNull(observed.Trailers.GetValueBytes(Binding().TrailerKey));
        Assert.Equal(1, update.UpdateCalls);
    }

    /// <summary>
    /// An aborted status whose trailer carries the DATABASE-ERROR arm rather than a conflict is rethrown
    /// unchanged, and nothing from that arm is logged.
    /// </summary>
    /// <returns>The test.</returns>
    [Fact]
    public async Task UpdateAsync_RethrowsAnAbortCarryingTheDatabaseErrorArmAndLogsNoStatement()
    {
        (PersistenceClient client, _, FakeUpdateServiceClient update, _, _, _,
            PersistenceRecordingLogger logger) = CreateClient();

        update.UpdateFailure = AbortedWith(new RichErrorTrailer
        {
            DbError = new DbError
            {
                Sqldbcode = -1L,
                Sqlerrtext = "constraint COMPANY_PK violated for NAME = 'Paul'",
                Sqlsyntax = "UPDATE COMPANY SET SALARY = 21000 WHERE NAME = 'Paul'",
                Buffer = DwBuffer.Primary,
                Row = 4L,
            },
            RetCode = KernelRetCode.E_DB_ERROR,
        }.ToByteArray());

        _ = await Assert.ThrowsAsync<RpcException>(() => client.UpdateAsync(
            new UpdateRequest { Task = Handle(), UpdateRows = 1L },
            TestContext.Current.CancellationToken));

        string logged = logger.AllText();
        Assert.DoesNotContain("UPDATE COMPANY", logged, StringComparison.Ordinal);
        Assert.DoesNotContain("'Paul'", logged, StringComparison.Ordinal);
        Assert.DoesNotContain("constraint", logged, StringComparison.Ordinal);
    }

    /// <summary>
    /// The contract's own binding names the aborted status and the payload type this client decodes, so
    /// the decode path is keyed to the descriptor rather than to a literal.
    /// </summary>
    [Fact]
    public void RichErrorBinding_NamesTheAbortedStatusAndTheDecodablePayload()
    {
        RichErrorBinding binding = Binding();

        Assert.Equal((int)StatusCode.Aborted, binding.GrpcStatusCode);
        Assert.Equal(RichErrorTrailer.Descriptor.FullName, binding.PayloadType);
        Assert.EndsWith("-bin", binding.TrailerKey, StringComparison.Ordinal);
    }

    // ==============================================================================================
    //  2 - PROGRESSIVE DELIVERY
    // ==============================================================================================

    /// <summary>
    /// The retrieval is consumed as a STREAM - one message is pulled per message consumed - and every
    /// chunk field survives.
    /// </summary>
    /// <returns>The test.</returns>
    [Fact]
    public async Task QueryAsync_YieldsChunksProgressivelyWithEveryChunkFieldIntact()
    {
        (PersistenceClient client, FakeQueryServiceClient query, _, _, _, _, _) = CreateClient();

        query.StreamMessages.Add(new QueryResponse { RowCount = new QueryRowCount { RowCount = 7L } });
        query.StreamMessages.Add(new QueryResponse
        {
            DataChunk = new QueryDataChunk
            {
                State = new CarrierState
                {
                    Processing = 1L,
                    Segments =
                    {
                        new CarrierBufferSegment { Buffer = DwBuffer.Primary },
                    },
                },
                ChunkCount = 2L,
                ChunkIndex = 1L,
                FullState = false,
            },
        });
        query.StreamMessages.Add(new QueryResponse
        {
            DataChunk = new QueryDataChunk { ChunkCount = 2L, ChunkIndex = 2L, FullState = true },
        });
        query.StreamMessages.Add(new QueryResponse
        {
            Status = new OperationStatus { RetCode = WireRetCode.Ok },
        });

        List<QueryResponse> observed = [];
        int pullsWhenFirstSeen = -1;

        await foreach (QueryResponse response in client
            .QueryAsync(new QueryRequest { Task = Handle() }, TestContext.Current.CancellationToken))
        {
            if (observed.Count == 0)
            {
                // THE STREAMING ASSERTION. A client that buffered the whole stream first would have
                // driven the reader to the end before the caller saw anything, so this would read 5
                // rather than 1.
                pullsWhenFirstSeen = query.LastReader!.Pulls;
            }

            observed.Add(response);
        }

        Assert.Equal(1, pullsWhenFirstSeen);
        Assert.Equal(4, observed.Count);

        Assert.Equal(7L, observed[0].RowCount.RowCount);

        QueryDataChunk first = observed[1].DataChunk;
        Assert.Equal(2L, first.ChunkCount);

        // ONE-BASED: the first chunk is 1. Reading it as zero-based is wrong by one for the whole stream.
        Assert.Equal(1L, first.ChunkIndex);
        Assert.False(first.FullState);
        Assert.Equal(1L, first.State.Processing);
        Assert.Equal(DwBuffer.Primary, Assert.Single(first.State.Segments).Buffer);

        // The full-state selector says WHICH serialization the payload is - a full-state image or a
        // changeset - so it must survive per chunk rather than per stream.
        Assert.True(observed[2].DataChunk.FullState);

        // AN ABSENT CARRIER STATE IS THE LEGACY'S ZERO-LENGTH BLOB and is distinct from a state holding
        // empty segments; the client collapses neither.
        Assert.Null(observed[2].DataChunk.State);

        Assert.Equal(WireRetCode.Ok, observed[3].Status.RetCode);
        Assert.Equal(1, query.QueryCalls);
    }

    /// <summary>
    /// Cancelling the token stops the enumeration rather than draining the stream.
    /// </summary>
    /// <returns>The test.</returns>
    [Fact]
    public async Task QueryAsync_StopsEnumeratingWhenTheTokenIsCancelled()
    {
        (PersistenceClient client, FakeQueryServiceClient query, _, _, _, _, _) = CreateClient();

        for (int index = 1; index <= 5; index++)
        {
            query.StreamMessages.Add(new QueryResponse
            {
                DataChunk = new QueryDataChunk { ChunkCount = 5L, ChunkIndex = index },
            });
        }

        using CancellationTokenSource cancellation = new();
        int consumed = 0;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (QueryResponse response in client
                .QueryAsync(new QueryRequest { Task = Handle() }, cancellation.Token))
            {
                consumed++;
                await cancellation.CancelAsync();
            }
        });

        Assert.Equal(1, consumed);
        Assert.Equal(1, query.LastReader!.Pulls);
    }

    // ==============================================================================================
    //  3 - THE IDENTITY ROUND TRIP, AND THE REVERSED FILTER ARRAY
    // ==============================================================================================

    /// <summary>
    /// Both identity arrays are surfaced separately, per update table, in the order received - and the
    /// FILTER array's reversed order is preserved.
    /// </summary>
    /// <returns>The test.</returns>
    /// <remarks>
    /// THE FILTER ARRAY ARRIVES REVERSED RELATIVE TO THE SOURCE, AND THAT IS NOT A DEFECT. The legacy
    /// iterates <c>for nIndex = nCount to 1 step -1</c>
    /// [ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru:L237] precisely because its own
    /// comment at [:L235] states that the filter buffer's row order is INVERTED relative to the source,
    /// while the primary buffer is collected FORWARD [:L229]. "Correcting" the direction produces wrong
    /// identity values that a row-count assertion would still pass, which is why this test asserts the
    /// exact received order and names those two locators for whoever reads it next.
    /// </remarks>
    [Fact]
    public async Task UpdateAsync_PreservesBothIdentityArraysAndTheFilterOrder()
    {
        (PersistenceClient client, _, FakeUpdateServiceClient update, _, _, _, _) = CreateClient();

        update.UpdateAnswer = new UpdateResponse
        {
            Status = new OperationStatus { RetCode = WireRetCode.Ok },
            Counts = new UpdateCounts { Inserted = 3L, Updated = 1L, Deleted = 2L },
            Identity =
            {
                new IdentityColumnData
                {
                    IdentityColumnId = 1L,
                    PrimaryValues =
                    {
                        new NullableInt64 { Value = 11L },
                        new NullableInt64 { Value = 12L },
                    },
                    FilterValues =
                    {
                        new NullableInt64 { Value = 22L },
                        new NullableInt64 { Value = 21L },
                        new NullableInt64(),
                    },
                },
                new IdentityColumnData { IdentityColumnId = 4L },
            },
        };

        UpdateResponse response = await client.UpdateAsync(
            new UpdateRequest { Task = Handle(), UpdateRows = 6L },
            TestContext.Current.CancellationToken);

        Assert.Equal(3L, response.Counts.Inserted);
        Assert.Equal(1L, response.Counts.Updated);
        Assert.Equal(2L, response.Counts.Deleted);

        // ONE BLOCK PER UPDATE TABLE, in the declared table order - the legacy proxy appends a block per
        // firing rather than replacing one [n_cst_threading_task_sqlupdate.sru:L73-L76].
        Assert.Equal(2, response.Identity.Count);

        IdentityColumnData block = response.Identity[0];

        // The ordinal is ONE-BASED and is surfaced unchanged; the legacy only fires the callback when it
        // is positive [n_cst_thread_task_sqlupdate.sru:L226].
        Assert.Equal(1L, block.IdentityColumnId);

        Assert.Equal([11L, 12L], block.PrimaryValues.Select(static value => value.Value));
        Assert.Equal([22L, 21L], block.FilterValues.Take(2).Select(static value => value.Value));

        // PER-ELEMENT PRESENCE: a null identity is representable and is not coerced to zero, which would
        // be indistinguishable from a legitimate identity of zero.
        Assert.False(block.FilterValues[2].HasValue);

        Assert.Equal(4L, response.Identity[1].IdentityColumnId);
        Assert.Empty(response.Identity[1].PrimaryValues);
        Assert.Empty(response.Identity[1].FilterValues);
    }

    // ==============================================================================================
    //  4 - AUTHENTICATION ON EVERY OUTBOUND CALL
    // ==============================================================================================

    /// <summary>
    /// Every outbound call - unary and streamed, across all four contracts - carries the bearer
    /// credential the token provider issued.
    /// </summary>
    /// <returns>The test.</returns>
    [Fact]
    public async Task EveryOutboundCall_CarriesTheBearerCredential()
    {
        (PersistenceClient client,
            FakeQueryServiceClient query,
            FakeUpdateServiceClient update,
            FakeCommandServiceClient command,
            FakeTransactionServiceClient transaction,
            PersistenceStubTokenProvider tokens,
            _) = CreateClient();

        _ = await client.SetChunkSizeAsync(
            new SetChunkSizeRequest { Task = Handle(), ChunkSize = PersistenceClient.DefaultChunkSize },
            TestContext.Current.CancellationToken);

        await foreach (QueryResponse _ in client
            .QueryAsync(new QueryRequest { Task = Handle() }, TestContext.Current.CancellationToken))
        {
            // The stream is empty; opening it is what attaches the credential.
        }

        _ = await client.UpdateAsync(
            new UpdateRequest { Task = Handle(), UpdateRows = 1L },
            TestContext.Current.CancellationToken);

        _ = await client.ExecAsync(
            new ExecRequest { Task = Handle(), Sql = "DELETE FROM COMPANY" },
            TestContext.Current.CancellationToken);

        _ = await client.CommitAsync(
            new SessionHandle { SessionId = "session-under-test" },
            TestContext.Current.CancellationToken);

        string expected = string.Concat(
            PersistenceStubTokenProvider.TokenType, " ", PersistenceStubTokenProvider.Credential);

        foreach (CallOptions? options in new[]
        {
            query.LastOptions, update.LastOptions, command.LastOptions, transaction.LastOptions,
        })
        {
            Assert.NotNull(options);
            Metadata? headers = options.Value.Headers;
            Assert.NotNull(headers);
            Assert.Equal(expected, headers.GetValue("authorization"));
        }

        // One credential per call, and the token request names this service and the Persistence audience.
        Assert.Equal(5, tokens.Requests);
        Assert.NotNull(tokens.LastRequest);
        Assert.Equal("powerframework-dataservices", tokens.LastRequest.Subject);
        Assert.Equal("powerframework-persistence", tokens.LastRequest.Audience);

        // ONE SCOPE PER REQUEST, CHOSEN BY THE OPERATION (constraint C-G). A credential asking for read
        // AND write on every call meant a retrieval carried the authority to update; each call now
        // presents exactly what it is about to do, and the upstream enforces the same two names per RPC.
        // The five calls above, in order: set-chunk-size and query READ, update and exec WRITE, commit
        // WRITE.
        Assert.Equal(
            [
                new[] { "persistence.read" },
                new[] { "persistence.read" },
                new[] { "persistence.write" },
                new[] { "persistence.write" },
                new[] { "persistence.write" },
            ],
            tokens.AllRequests.Select(static request => request.Scopes.ToArray()));

        // AND EVERY REQUEST NAMES THIS SERVICE AND THAT ONE AUDIENCE - a per-operation scope must not
        // become a per-operation identity.
        Assert.All(tokens.AllRequests, static request =>
        {
            Assert.Equal("powerframework-dataservices", request.Subject);
            Assert.Equal("powerframework-persistence", request.Audience);
            Assert.Single(request.Scopes);
        });
    }

    /// <summary>
    /// A read-shaped call never asks for the write scope, and a write-shaped one never asks for the read
    /// scope.
    /// </summary>
    /// <returns>The test.</returns>
    /// <remarks>
    /// The mapping is asserted across the WHOLE surface rather than on a sample, because the failure mode
    /// is one member quietly acquiring the other half's authority - which no functional test would notice,
    /// since a broader credential works everywhere a narrower one does.
    /// </remarks>
    [Fact]
    public async Task TheScopeRequestedIsOperationSpecificAcrossTheWholeSurface()
    {
        (PersistenceClient client, _, _, _, _, PersistenceStubTokenProvider tokens, _) = CreateClient();

        CancellationToken token = TestContext.Current.CancellationToken;
        SessionHandle session = new() { SessionId = "session-under-test" };

        // ---- READ-SHAPED: C-05 in full, plus C-08's session, descriptor and state readers ----
        _ = await client.CreateQueryTaskAsync(new CreateQueryTaskRequest { Session = session }, token);
        _ = await client.ReleaseQueryTaskAsync(new ReleaseQueryTaskRequest { Task = Handle() }, token);
        _ = await client.ResetQueryTaskAsync(new ResetQueryTaskRequest { Task = Handle() }, token);
        _ = await client.SetChunkSizeAsync(
            new SetChunkSizeRequest { Task = Handle(), ChunkSize = PersistenceClient.DefaultChunkSize },
            token);
        _ = await client.SetMaxRowsAsync(new SetMaxRowsRequest { Task = Handle(), MaxRows = 1L }, token);
        _ = await client.CountAsync(new CountRequest { Task = Handle() }, token);
        _ = await client.BeginSessionAsync(
            new BeginSessionRequest { Descriptor_ = new TransactionDescriptor { Dbms = "SQLite" } },
            token);
        _ = await client.EndSessionAsync(new EndSessionRequest { Session = session }, token);
        _ = await client.GetTransactionDataAsync(
            new GetTransactionDataRequest { Session = session },
            token);
        _ = await client.IsConnectedAsync(new IsConnectedRequest { Session = session }, token);
        _ = await client.GetDatabaseTypeAsync(new GetDatabaseTypeRequest { Session = session }, token);
        _ = await client.GetSessionStateAsync(new GetSessionStateRequest { Session = session }, token);
        _ = await client.GridSyntaxFromSqlAsync(
            new GridSyntaxFromSqlRequest { Session = session, Sql = "SELECT 1" },
            token);

        Assert.All(tokens.AllRequests, static request =>
            Assert.Equal(["persistence.read"], request.Scopes));

        int readCalls = tokens.AllRequests.Count;
        Assert.Equal(13, readCalls);

        // ---- WRITE-SHAPED: C-06 and C-07 in full, plus C-08's state changers ----
        _ = await client.CreateUpdateTaskAsync(new CreateUpdateTaskRequest { Session = session }, token);
        _ = await client.ReleaseUpdateTaskAsync(new ReleaseUpdateTaskRequest { Task = Handle() }, token);
        _ = await client.ResetUpdateTaskAsync(new ResetUpdateTaskRequest { Task = Handle() }, token);
        _ = await client.PrepareUpdateAsync(new PrepareUpdateRequest { Task = Handle() }, token);
        _ = await client.UpdateAsync(new UpdateRequest { Task = Handle(), UpdateRows = 1L }, token);
        _ = await client.CreateCommandTaskAsync(new CreateCommandTaskRequest { Session = session }, token);
        _ = await client.ReleaseCommandTaskAsync(
            new ReleaseCommandTaskRequest { Task = Handle() },
            token);
        _ = await client.ResetCommandTaskAsync(new ResetCommandTaskRequest { Task = Handle() }, token);
        _ = await client.SetCommandAutoCommitAsync(
            new SetCommandAutoCommitRequest { Task = Handle(), Autocommit = AutoCommitMode.AcOn },
            token);
        _ = await client.SetCommandSqlAsync(
            new SetCommandSqlRequest { Task = Handle(), Sql = "DELETE FROM COMPANY" },
            token);
        _ = await client.ExecAsync(
            new ExecRequest { Task = Handle(), Sql = "DELETE FROM COMPANY" },
            token);
        _ = await client.SetTransactionAutoCommitAsync(
            new SetTransactionAutoCommitRequest { Session = session, Autocommit = true },
            token);
        _ = await client.AutoCommitAsync(new AutoCommitRequest { Session = session }, token);
        _ = await client.CommitAsync(session, token);
        _ = await client.RollbackAsync(new RollbackRequest { Session = session }, token);
        _ = await client.ClearStateAsync(new ClearStateRequest { Session = session }, token);
        _ = await client.SetBrokenAsync(new SetBrokenRequest { Session = session }, token);

        Assert.All(
            tokens.AllRequests.Skip(readCalls),
            static request => Assert.Equal(["persistence.write"], request.Scopes));

        // Every member exercised above asked for a credential, so none of them skipped authentication.
        Assert.Equal(tokens.AllRequests.Count, tokens.Requests);
    }

    /// <summary>
    /// The client cannot be constructed without a token provider, so an unauthenticated call is
    /// unreachable rather than merely discouraged.
    /// </summary>
    [Fact]
    public void Constructor_RefusesEveryAbsentDependency()
    {
        FakeQueryServiceClient query = new();
        FakeUpdateServiceClient update = new();
        FakeCommandServiceClient command = new();
        FakeTransactionServiceClient transaction = new();
        PersistenceStubTokenProvider tokens = new();
        PersistenceRecordingLogger logger = new();

        Assert.Throws<ArgumentNullException>(() =>
            new PersistenceClient(null!, update, command, transaction, tokens, logger));
        Assert.Throws<ArgumentNullException>(() =>
            new PersistenceClient(query, null!, command, transaction, tokens, logger));
        Assert.Throws<ArgumentNullException>(() =>
            new PersistenceClient(query, update, null!, transaction, tokens, logger));
        Assert.Throws<ArgumentNullException>(() =>
            new PersistenceClient(query, update, command, null!, tokens, logger));
        Assert.Throws<ArgumentNullException>(() =>
            new PersistenceClient(query, update, command, transaction, null!, logger));
        Assert.Throws<ArgumentNullException>(() =>
            new PersistenceClient(query, update, command, transaction, tokens, null!));
    }

    /// <summary>
    /// A grant that withholds the required scope is refused before the call is sent, by exact name, and
    /// nothing about the credential is logged.
    /// </summary>
    /// <returns>The test.</returns>
    /// <remarks>
    /// COMPARING SCOPE COUNTS AND PROCEEDING IS THE TEMPTING CHECK. It is wrong in two independent ways: a
    /// count cannot say WHICH scope was withheld, and an issuer granting a completely different scope of the
    /// same cardinality satisfies the comparison outright. Both are covered below - the
    /// missing-name case and the right-count-wrong-name case.
    /// </remarks>
    [Fact]
    public async Task AGrantMissingTheRequiredScope_IsRefusedBeforeTheCallAndLogsNoCredential()
    {
        (PersistenceClient client, _, _, _, FakeTransactionServiceClient transaction,
            PersistenceStubTokenProvider tokens, PersistenceRecordingLogger logger) = CreateClient();

        // Commit needs the WRITE scope; the issuer grants only read.
        tokens.GrantedScopes = ["persistence.read"];

        RpcException refused = await Assert.ThrowsAsync<RpcException>(() => client.CommitAsync(
            new SessionHandle { SessionId = "session-under-test" },
            TestContext.Current.CancellationToken));

        Assert.Equal(StatusCode.PermissionDenied, refused.StatusCode);

        // BEFORE THE CALL, not after it: the upstream was never reached.
        Assert.Null(transaction.LastOptions);

        string logged = logger.AllText();
        Assert.Contains("withheld a required scope", logged, StringComparison.Ordinal);
        Assert.DoesNotContain(PersistenceStubTokenProvider.Credential, logged, StringComparison.Ordinal);
        Assert.DoesNotContain("persistence.read", logged, StringComparison.Ordinal);
        Assert.DoesNotContain("persistence.write", logged, StringComparison.Ordinal);
    }

    /// <summary>
    /// A grant of the right SIZE but the wrong NAME is refused, which a count comparison could not do.
    /// </summary>
    /// <returns>The test.</returns>
    [Fact]
    public async Task AGrantOfTheRightSizeButTheWrongName_IsRefused()
    {
        (PersistenceClient client, _, _, _, _, PersistenceStubTokenProvider tokens, _) = CreateClient();

        // One scope requested, one scope granted - and not the one that was asked for.
        tokens.GrantedScopes = ["persistence.something.else"];

        RpcException refused = await Assert.ThrowsAsync<RpcException>(() => client.CommitAsync(
            new SessionHandle { SessionId = "session-under-test" },
            TestContext.Current.CancellationToken));

        Assert.Equal(StatusCode.PermissionDenied, refused.StatusCode);
    }

    /// <summary>
    /// A grant carrying the required scope alongside others is accepted: a superset is not a narrowing.
    /// </summary>
    /// <returns>The test.</returns>
    [Fact]
    public async Task AGrantCarryingTheRequiredScopeAmongOthers_IsAccepted()
    {
        (PersistenceClient client, _, _, _, FakeTransactionServiceClient transaction,
            PersistenceStubTokenProvider tokens, _) = CreateClient();

        tokens.GrantedScopes = ["persistence.read", "persistence.write", "something.unrelated"];

        _ = await client.CommitAsync(
            new SessionHandle { SessionId = "session-under-test" },
            TestContext.Current.CancellationToken);

        Assert.NotNull(transaction.LastOptions);
    }

    /// <summary>
    /// The scope comparison is case-sensitive, because scope names are case-sensitive strings.
    /// </summary>
    /// <returns>The test.</returns>
    [Fact]
    public async Task TheScopeComparisonIsCaseSensitive()
    {
        (PersistenceClient client, _, _, _, _, PersistenceStubTokenProvider tokens, _) = CreateClient();

        tokens.GrantedScopes = ["PERSISTENCE.WRITE"];

        RpcException refused = await Assert.ThrowsAsync<RpcException>(() => client.CommitAsync(
            new SessionHandle { SessionId = "session-under-test" },
            TestContext.Current.CancellationToken));

        Assert.Equal(StatusCode.PermissionDenied, refused.StatusCode);
    }

    // ==============================================================================================
    //  5 - THE REDACTION RULE
    // ==============================================================================================

    /// <summary>
    /// A logged database error carries the numeric code, the buffer and the row - and never the statement
    /// text or the driver text.
    /// </summary>
    /// <returns>The test.</returns>
    /// <remarks>
    /// The statement field carries the complete generated statement INCLUDING INTERPOLATED LITERAL VALUES
    /// [ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlcommand.sru:L110 passing the statement
    /// produced by n_cst_thread_task_sqlbase.sru:L464], and the legacy logger performed no redaction at
    /// all. Both this client's paths that write a database error are covered here.
    /// </remarks>
    [Fact]
    public async Task DatabaseFailure_IsLoggedWithoutTheStatementOrTheDriverText()
    {
        (PersistenceClient client, _, FakeUpdateServiceClient update, FakeCommandServiceClient command,
            _, _, PersistenceRecordingLogger logger) = CreateClient();

        DbError error = new()
        {
            Sqldbcode = -803L,
            Sqlerrtext = "unique constraint violated: NAME = 'Paul'",
            Sqlsyntax = "INSERT INTO COMPANY (NAME,SALARY) VALUES ('Paul',20000)",
            Buffer = DwBuffer.Filter,
            Row = 9L,
        };

        update.UpdateAnswer = new UpdateResponse
        {
            Status = new OperationStatus { RetCode = WireRetCode.EDbError, DbError = error },
        };
        command.ExecAnswer = new ExecResponse
        {
            Status = new OperationStatus { RetCode = WireRetCode.EDbError, DbError = error },
            SqlCode = -1L,
        };

        _ = await client.UpdateAsync(
            new UpdateRequest { Task = Handle(), UpdateRows = 1L },
            TestContext.Current.CancellationToken);
        _ = await client.ExecAsync(
            new ExecRequest { Task = Handle(), Sql = "INSERT INTO COMPANY DEFAULT VALUES" },
            TestContext.Current.CancellationToken);

        string logged = logger.AllText();

        Assert.Contains("-803", logged, StringComparison.Ordinal);
        Assert.Contains("Filter", logged, StringComparison.Ordinal);
        Assert.Contains("9", logged, StringComparison.Ordinal);

        Assert.DoesNotContain("INSERT INTO COMPANY (NAME,SALARY)", logged, StringComparison.Ordinal);
        Assert.DoesNotContain("'Paul'", logged, StringComparison.Ordinal);
        Assert.DoesNotContain("unique constraint", logged, StringComparison.Ordinal);
        Assert.DoesNotContain("20000", logged, StringComparison.Ordinal);
    }

    /// <summary>
    /// Beginning a session logs nothing from the transaction descriptor - not the password, not the log
    /// identity, not the connection parameters, and not the server or database either.
    /// </summary>
    /// <returns>The test.</returns>
    [Fact]
    public async Task BeginSessionAsync_LogsNothingFromTheDescriptor()
    {
        (PersistenceClient client, _, _, _, FakeTransactionServiceClient transaction, _,
            PersistenceRecordingLogger logger) = CreateClient();

        // Values shaped like a real descriptor but deliberately not credentials: the point of the test is
        // that NONE of them is written out, whatever they are.
        BeginSessionRequest request = new()
        {
            Descriptor_ = new TransactionDescriptor
            {
                Dbms = "SNC",
                Servername = "db.invalid",
                Database = "PFWDEMO",
                Logid = "pfw-log-identity",
                Logpass = "REDACTED_PLACEHOLDER_NOT_A_SECRET",
                Dbparm = "DisableBind=1,NCharBind=1",
                Lock = "RU",
                Autocommit = false,
                Userparm = "userparm-value",
            },
            Flags = new ConnectionParameterFlags { DisableBind = true, NcharBind = true },
        };

        _ = await client.BeginSessionAsync(request, TestContext.Current.CancellationToken);

        string logged = logger.AllText();

        Assert.DoesNotContain("REDACTED_PLACEHOLDER_NOT_A_SECRET", logged, StringComparison.Ordinal);
        Assert.DoesNotContain("pfw-log-identity", logged, StringComparison.Ordinal);
        Assert.DoesNotContain("DisableBind", logged, StringComparison.Ordinal);
        Assert.DoesNotContain("db.invalid", logged, StringComparison.Ordinal);
        Assert.DoesNotContain("PFWDEMO", logged, StringComparison.Ordinal);
        Assert.DoesNotContain("userparm-value", logged, StringComparison.Ordinal);

        // THE TWO CONNECTION FLAGS STAY DISTINCT ON THE WIRE, because the legacy honours the
        // national-character flag only when the bind-disabling flag is also set
        // [ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru:L127-L132].
        BeginSessionRequest sent = Assert.Single(transaction.BeginRequests);
        Assert.True(sent.Flags.DisableBind);
        Assert.True(sent.Flags.NcharBind);

        // The nine descriptor fields cross in the legacy's own order, the password included - it is
        // WRITE-ONLY rather than absent.
        Assert.Equal("REDACTED_PLACEHOLDER_NOT_A_SECRET", sent.Descriptor_.Logpass);
    }

    /// <summary>
    /// The read model has no field a password, connection parameters or user parameters could arrive in,
    /// so the write-only rule is structural rather than conventional.
    /// </summary>
    [Fact]
    public void TransactionDescriptorView_HasNoFieldForTheWriteOnlyValues()
    {
        IReadOnlyList<string> fields =
        [
            .. TransactionDescriptorView.Descriptor.Fields.InFieldNumberOrder()
                .Select(static field => field.Name),
        ];

        Assert.DoesNotContain("logpass", fields);
        Assert.DoesNotContain("dbparm", fields);
        Assert.DoesNotContain("userparm", fields);
        Assert.Contains("logid", fields);
        Assert.Contains("flags", fields);
    }

    // ==============================================================================================
    //  6 - THE PRESERVED LEGACY GUARDS, EACH AT ITS EXACT BOUNDARY
    // ==============================================================================================

    /// <summary>
    /// The chunk-size boundary is "at or below 1000 is invalid", so 1000 is refused and 1001 is accepted -
    /// and a refusal sends nothing.
    /// </summary>
    /// <param name="chunkSize">The requested size.</param>
    /// <param name="accepted">Whether it should reach the wire.</param>
    /// <returns>The test.</returns>
    /// <remarks>
    /// <c>if chunkSize &lt;= 1000 then return RetCode.E_INVALID_ARGUMENT</c>
    /// [ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L410]. The neighbouring legacy
    /// comment says "min:1000" and therefore disagrees with the code by one [:L34]; the CODE is what runs
    /// and what is reproduced, and this matrix pins the disagreement so it cannot be "tidied".
    /// </remarks>
    [Theory]
    [InlineData(0L, false)]
    [InlineData(-1L, false)]
    [InlineData(999L, false)]
    [InlineData(1000L, false)]
    [InlineData(1001L, true)]
    [InlineData(10000L, true)]
    public async Task SetChunkSizeAsync_ReproducesTheLegacyBoundaryExactly(long chunkSize, bool accepted)
    {
        (PersistenceClient client, FakeQueryServiceClient query, _, _, _, _, _) = CreateClient();

        SetChunkSizeResponse response = await client.SetChunkSizeAsync(
            new SetChunkSizeRequest { Task = Handle(), ChunkSize = chunkSize },
            TestContext.Current.CancellationToken);

        if (accepted)
        {
            Assert.Equal(WireRetCode.Ok, response.Status.RetCode);
            Assert.Equal(chunkSize, Assert.Single(query.ChunkSizeRequests).ChunkSize);
        }
        else
        {
            Assert.Equal(WireRetCode.EInvalidArgument, response.Status.RetCode);

            // A REJECTION SENDS NOTHING. The legacy setter never reached its own task state either.
            Assert.Empty(query.ChunkSizeRequests);
        }

        Assert.Equal(1001L, PersistenceClient.SmallestValidChunkSize);
        Assert.Equal(10_000L, PersistenceClient.DefaultChunkSize);
    }

    /// <summary>
    /// The maximum-rows boundary is "strictly negative is invalid", which is a DIFFERENT boundary from the
    /// chunk-size guard: zero is legal here and means no ceiling.
    /// </summary>
    /// <param name="maxRows">The requested ceiling.</param>
    /// <param name="accepted">Whether it should reach the wire.</param>
    /// <returns>The test.</returns>
    /// <remarks>
    /// <c>if rows &lt; 0 then return RetCode.E_INVALID_ARGUMENT</c>
    /// [ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L433]. Transposing this with the
    /// chunk-size boundary would either reject a legal setting or accept an illegal one.
    /// </remarks>
    [Theory]
    [InlineData(-1L, false)]
    [InlineData(0L, true)]
    [InlineData(1L, true)]
    public async Task SetMaxRowsAsync_ReproducesTheLegacyBoundaryExactly(long maxRows, bool accepted)
    {
        (PersistenceClient client, FakeQueryServiceClient query, _, _, _, _, _) = CreateClient();

        SetMaxRowsResponse response = await client.SetMaxRowsAsync(
            new SetMaxRowsRequest { Task = Handle(), MaxRows = maxRows },
            TestContext.Current.CancellationToken);

        if (accepted)
        {
            Assert.Equal(WireRetCode.Ok, response.Status.RetCode);
            Assert.Equal(maxRows, Assert.Single(query.MaxRowsRequests).MaxRows);
        }
        else
        {
            Assert.Equal(WireRetCode.EInvalidArgument, response.Status.RetCode);
            Assert.Empty(query.MaxRowsRequests);
        }
    }

    /// <summary>
    /// A clause setter refuses a select index of zero or less AND an empty clause, with ONE code - and the
    /// protocol's unspecified modification style is refused too, so it never reaches the wire.
    /// </summary>
    /// <param name="selectIndex">The one-based select index.</param>
    /// <param name="clause">The clause text.</param>
    /// <param name="style">The modification style.</param>
    /// <param name="accepted">Whether it should reach the wire.</param>
    /// <returns>The test.</returns>
    /// <remarks>
    /// <c>if selectIndex &lt;= 0 or clause = "" then return RetCode.E_INVALID_ARGUMENT</c>
    /// [ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L271 for the where clause and
    /// :L288 for the order-by clause]. The index is ONE-BASED, so zero is an error rather than a request
    /// for a default.
    /// </remarks>
    [Theory]
    [InlineData(0, "AGE > 30", SqlModifyStyle.SqlMsReplace, false)]
    [InlineData(-1, "AGE > 30", SqlModifyStyle.SqlMsReplace, false)]
    [InlineData(1, "", SqlModifyStyle.SqlMsReplace, false)]
    [InlineData(1, "AGE > 30", SqlModifyStyle.SqlMsUnspecified, false)]
    [InlineData(1, "AGE > 30", SqlModifyStyle.SqlMsReplace, true)]
    [InlineData(2, "AGE > 30", SqlModifyStyle.SqlMsAppend, true)]
    [InlineData(3, "AGE > 30", SqlModifyStyle.SqlMsPrepend, true)]
    public async Task ClauseSetters_ReproduceBothLegacyConditionsWithOneCode(
        int selectIndex,
        string clause,
        SqlModifyStyle style,
        bool accepted)
    {
        (PersistenceClient client, FakeQueryServiceClient query, _, _, _, _, _) = CreateClient();

        SqlClauseSpec Spec() => new()
        {
            SelectIndex = selectIndex,
            ModifyStyle = style,
            Clause = clause,
        };

        SetWhereClauseResponse where = await client.SetWhereClauseAsync(
            new SetWhereClauseRequest { Task = Handle(), Clause = Spec() },
            TestContext.Current.CancellationToken);
        SetOrderByClauseResponse orderBy = await client.SetOrderByClauseAsync(
            new SetOrderByClauseRequest { Task = Handle(), Clause = Spec() },
            TestContext.Current.CancellationToken);

        if (accepted)
        {
            Assert.Equal(WireRetCode.Ok, where.Status.RetCode);
            Assert.Equal(WireRetCode.Ok, orderBy.Status.RetCode);

            // ONE-BASED ON THE WIRE, AND NOT REBASED: what the caller supplied is what is sent.
            Assert.Equal(selectIndex, Assert.Single(query.WhereClauseRequests).Clause.SelectIndex);
            Assert.Equal(style, Assert.Single(query.OrderByClauseRequests).Clause.ModifyStyle);
        }
        else
        {
            Assert.Equal(WireRetCode.EInvalidArgument, where.Status.RetCode);
            Assert.Equal(WireRetCode.EInvalidArgument, orderBy.Status.RetCode);
            Assert.Empty(query.WhereClauseRequests);
            Assert.Empty(query.OrderByClauseRequests);
        }
    }

    /// <summary>
    /// An absent clause specification is refused with the same code rather than dereferenced.
    /// </summary>
    /// <returns>The test.</returns>
    [Fact]
    public async Task ClauseSetters_RefuseAnAbsentSpecification()
    {
        (PersistenceClient client, FakeQueryServiceClient query, _, _, _, _, _) = CreateClient();

        SetWhereClauseResponse response = await client.SetWhereClauseAsync(
            new SetWhereClauseRequest { Task = Handle() },
            TestContext.Current.CancellationToken);

        Assert.Equal(WireRetCode.EInvalidArgument, response.Status.RetCode);
        Assert.Empty(query.WhereClauseRequests);
    }

    /// <summary>
    /// The two-argument legacy clause form maps the kernel modification style onto the wire enumeration
    /// and supplies select index ONE - never zero, and never the protocol's unspecified member.
    /// </summary>
    /// <param name="kernelStyle">The kernel constant.</param>
    /// <param name="expected">The expected wire member.</param>
    /// <returns>The test.</returns>
    [Theory]
    [InlineData(Enums.SQL_MS_REPLACE, SqlModifyStyle.SqlMsReplace)]
    [InlineData(Enums.SQL_MS_APPEND, SqlModifyStyle.SqlMsAppend)]
    [InlineData(Enums.SQL_MS_PREPEND, SqlModifyStyle.SqlMsPrepend)]
    public async Task TwoArgumentClauseForm_MapsTheStyleAndImpliesTheFirstStatement(
        long kernelStyle,
        SqlModifyStyle expected)
    {
        (PersistenceClient client, FakeQueryServiceClient query, _, _, _, _, _) = CreateClient();

        _ = await client.SetWhereClauseAsync(
            Handle(), kernelStyle, "AGE > 30", TestContext.Current.CancellationToken);
        _ = await client.SetOrderByClauseAsync(
            Handle(), kernelStyle, "AGE A", TestContext.Current.CancellationToken);

        SqlClauseSpec where = Assert.Single(query.WhereClauseRequests).Clause;
        Assert.Equal(expected, where.ModifyStyle);
        Assert.Equal(PersistenceClient.DefaultSelectIndex, where.SelectIndex);
        Assert.Equal(1, PersistenceClient.DefaultSelectIndex);

        Assert.Equal(expected, Assert.Single(query.OrderByClauseRequests).Clause.ModifyStyle);
    }

    /// <summary>
    /// A modification style outside the legacy three is refused rather than defaulted, and nothing is sent.
    /// </summary>
    /// <param name="kernelStyle">The unmapped value.</param>
    /// <returns>The test.</returns>
    /// <remarks>
    /// The legacy declares exactly three styles and NO ZERO [ws_objects/pfw.shared.pbl.src/enums.sru:L718
    /// to :L720] and never validates the argument at all, so there is no legacy behaviour to copy - which
    /// is exactly why the handling has to be DEFINED. Defaulting to "replace" would widen the contract
    /// with a guess.
    /// </remarks>
    [Theory]
    [InlineData(0L)]
    [InlineData(4L)]
    [InlineData(-1L)]
    public async Task TwoArgumentClauseForm_RefusesAnUnmappedStyle(long kernelStyle)
    {
        (PersistenceClient client, FakeQueryServiceClient query, _, _, _, _, _) = CreateClient();

        SetWhereClauseResponse where = await client.SetWhereClauseAsync(
            Handle(), kernelStyle, "AGE > 30", TestContext.Current.CancellationToken);
        SetOrderByClauseResponse orderBy = await client.SetOrderByClauseAsync(
            Handle(), kernelStyle, "AGE A", TestContext.Current.CancellationToken);

        Assert.Equal(WireRetCode.EInvalidArgument, where.Status.RetCode);
        Assert.Equal(WireRetCode.EInvalidArgument, orderBy.Status.RetCode);
        Assert.Empty(query.WhereClauseRequests);
        Assert.Empty(query.OrderByClauseRequests);
    }

    /// <summary>
    /// The unique-index column list is forwarded WITHOUT client-side validation, including an empty list,
    /// because the legacy setter validates nothing and the identifier check is the SQL generator's.
    /// </summary>
    /// <returns>The test.</returns>
    /// <remarks>
    /// The legacy assigns the array and returns success
    /// [ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L406]. An empty list on this call
    /// is the documented CLEAR.
    /// </remarks>
    [Fact]
    public async Task SetPagedUniqueIndexColumnsAsync_AddsNoValidationOfItsOwn()
    {
        (PersistenceClient client, FakeQueryServiceClient query, _, _, _, _, _) = CreateClient();

        _ = await client.SetPagedUniqueIndexColumnsAsync(
            new SetPagedUniqueIndexColumnsRequest { Task = Handle() },
            TestContext.Current.CancellationToken);
        _ = await client.SetPagedUniqueIndexColumnsAsync(
            new SetPagedUniqueIndexColumnsRequest { Task = Handle(), Columns = { "ID", "" } },
            TestContext.Current.CancellationToken);

        Assert.Equal(2, query.UniqueIndexRequests.Count);
        Assert.Empty(query.UniqueIndexRequests[0].Columns);
        Assert.Equal(["ID", ""], query.UniqueIndexRequests[1].Columns);
    }

    /// <summary>
    /// Paging is forwarded without a client-side bound check, because the contract FUSES five independent
    /// legacy setters and the legacy per-setter bounds do not compose onto one message.
    /// </summary>
    /// <returns>The test.</returns>
    [Fact]
    public async Task SetPagingAsync_ForwardsWithoutFabricatingARejection()
    {
        (PersistenceClient client, FakeQueryServiceClient query, _, _, _, _, _) = CreateClient();

        _ = await client.SetPagingAsync(
            new SetPagingRequest { Task = Handle(), Paged = false },
            TestContext.Current.CancellationToken);

        SetPagingRequest sent = Assert.Single(query.PagingRequests);
        Assert.False(sent.Paged);
        Assert.Equal(0L, sent.PageSize);

        // The legacy default for page counting is TRUE, which a caller using this call must send
        // explicitly [ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlquery.sru:L40].
        Assert.True(PersistenceClient.DefaultPageCounting);
    }

    /// <summary>
    /// An empty statement is refused with the INVALID-STATEMENT code rather than the invalid-argument one.
    /// </summary>
    /// <returns>The test.</returns>
    /// <remarks>
    /// <c>if sql = "" then return RetCode.E_INVALID_SQL</c>
    /// [ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlcommand.sru:L45]. Substituting the argument
    /// code would change an outcome a characterization recording can compare.
    /// </remarks>
    [Fact]
    public async Task SetCommandSqlAsync_RefusesAnEmptyStatementWithTheInvalidSqlCode()
    {
        (PersistenceClient client, _, _, FakeCommandServiceClient command, _, _, _) = CreateClient();

        SetCommandSqlResponse refused = await client.SetCommandSqlAsync(
            new SetCommandSqlRequest { Task = Handle() },
            TestContext.Current.CancellationToken);

        Assert.Equal(WireRetCode.EInvalidSql, refused.Status.RetCode);
        Assert.NotEqual(WireRetCode.EInvalidArgument, refused.Status.RetCode);
        Assert.Empty(command.SqlRequests);

        SetCommandSqlResponse accepted = await client.SetCommandSqlAsync(
            new SetCommandSqlRequest { Task = Handle(), Sql = "DELETE FROM COMPANY" },
            TestContext.Current.CancellationToken);

        Assert.Equal(WireRetCode.Ok, accepted.Status.RetCode);
        Assert.Equal("DELETE FROM COMPANY", Assert.Single(command.SqlRequests).Sql);
    }

    /// <summary>
    /// The command autocommit setting is TRI-VALUED and every member reaches the wire unflattened.
    /// </summary>
    /// <param name="mode">The mode.</param>
    /// <returns>The test.</returns>
    /// <remarks>
    /// <c>AC_OFF = 0</c>, <c>AC_ON = 1</c> which begins a transaction, and <c>AC_NATIVE = 2</c> which does
    /// NOT [ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlcommand.sru:L16-L18]. Contract C-06's
    /// equivalent is a plain boolean [n_cst_thread_task_sqlupdate.sru:L254], so flattening this would
    /// silently merge "do not begin a transaction" into "off".
    /// </remarks>
    [Theory]
    [InlineData(AutoCommitMode.AcOff)]
    [InlineData(AutoCommitMode.AcOn)]
    [InlineData(AutoCommitMode.AcNative)]
    public async Task SetCommandAutoCommitAsync_CarriesTheTriStateUnflattened(AutoCommitMode mode)
    {
        (PersistenceClient client, _, _, FakeCommandServiceClient command, _, _, _) = CreateClient();

        _ = await client.SetCommandAutoCommitAsync(
            new SetCommandAutoCommitRequest { Task = Handle(), Autocommit = mode },
            TestContext.Current.CancellationToken);

        Assert.Equal(mode, Assert.Single(command.AutoCommitRequests).Autocommit);
        Assert.Equal(0, (int)AutoCommitMode.AcOff);
        Assert.Equal(1, (int)AutoCommitMode.AcOn);
        Assert.Equal(2, (int)AutoCommitMode.AcNative);
    }

    /// <summary>
    /// A table contract missing its name, its updatable columns or its key columns is refused - and an
    /// EMPTY IDENTITY COLUMN is accepted, because the legacy does not check that one.
    /// </summary>
    /// <returns>The test.</returns>
    /// <remarks>
    /// <c>if name = "" or UpperBound(updatableColumns) = 0 or UpperBound(keyColumns) = 0 then return
    /// RetCode.E_INVALID_ARGUMENT</c>
    /// [ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru:L84]. What it does NOT check is
    /// equally part of the contract.
    /// </remarks>
    [Fact]
    public async Task PrepareUpdateAsync_ReproducesTheLegacyTableValidationAndItsOmissions()
    {
        (PersistenceClient client, _, FakeUpdateServiceClient update, _, _, _, _) = CreateClient();

        TableUpdateContract Missing(string name, bool updatable, bool keys)
        {
            TableUpdateContract table = new() { Name = name };

            if (updatable)
            {
                table.Updatablecolumns.Add("salary");
            }

            if (keys)
            {
                table.Keycolumns.Add("id");
            }

            return table;
        }

        foreach (TableUpdateContract invalid in new[]
        {
            Missing(string.Empty, true, true),
            Missing("COMPANY", false, true),
            Missing("COMPANY", true, false),
        })
        {
            PrepareUpdateResponse refused = await client.PrepareUpdateAsync(
                new PrepareUpdateRequest { Task = Handle(), Tables = { invalid } },
                TestContext.Current.CancellationToken);

            Assert.Equal(WireRetCode.EInvalidArgument, refused.Status.RetCode);
        }

        Assert.Empty(update.PrepareRequests);

        // AN EMPTY IDENTITY COLUMN IS LEGITIMATE, and the two trailing settings are UNCHECKED.
        TableUpdateContract valid = Missing("COMPANY", true, true);

        PrepareUpdateResponse accepted = await client.PrepareUpdateAsync(
            new PrepareUpdateRequest { Task = Handle(), Tables = { valid } },
            TestContext.Current.CancellationToken);

        Assert.Equal(WireRetCode.Ok, accepted.Status.RetCode);

        TableUpdateContract sent = Assert.Single(Assert.Single(update.PrepareRequests).Tables);
        Assert.Equal(string.Empty, sent.Identitycolumn);

        // PRESENCE-TRACKED, AND UNSET IS NOT A DEFAULT: the legacy four-argument add leaves both unset
        // [ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlupdate.sru:L59-L60].
        Assert.False(sent.HasUpdatewhere);
        Assert.False(sent.HasUpdatekeyinplace);
    }

    /// <summary>
    /// Multi-table order is transmitted as declared, the presence of the two trailing settings survives,
    /// and the update-where mode is an INTEGER rather than a flag.
    /// </summary>
    /// <returns>The test.</returns>
    [Fact]
    public async Task PrepareUpdateAsync_PreservesTableOrderAndTheIntegerUpdateWhereMode()
    {
        (PersistenceClient client, _, FakeUpdateServiceClient update, _, _, _, _) = CreateClient();

        PrepareUpdateRequest request = new()
        {
            Task = Handle(),
            MultiTableUpdate = true,
            Tables =
            {
                new TableUpdateContract
                {
                    Name = "COMPANY",
                    Updatablecolumns = { "name", "salary" },
                    Keycolumns = { "id" },
                    Identitycolumn = "id",
                    Updatewhere = 1L,
                    Updatekeyinplace = false,
                },
                new TableUpdateContract
                {
                    Name = "DEPARTMENT",
                    Updatablecolumns = { "title" },
                    Keycolumns = { "id" },
                },
            },
        };

        PrepareUpdateResponse response = await client.PrepareUpdateAsync(
            request, TestContext.Current.CancellationToken);

        Assert.Equal(WireRetCode.Ok, response.Status.RetCode);

        PrepareUpdateRequest sent = Assert.Single(update.PrepareRequests);
        Assert.True(sent.MultiTableUpdate);
        Assert.Equal(2, sent.Tables.Count);

        // ORDER IS SIGNIFICANT: with the switch on, the worker loops the array IN ARRAY ORDER and stops at
        // the first failure [ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru:L364-L369].
        Assert.Equal("COMPANY", sent.Tables[0].Name);
        Assert.Equal("DEPARTMENT", sent.Tables[1].Name);

        // AN INTEGER MODE, NOT A BOOLEAN: 1 selects key-and-updateable-columns concurrency.
        Assert.Equal(1L, sent.Tables[0].Updatewhere);
        Assert.True(sent.Tables[0].HasUpdatekeyinplace);
        Assert.False(sent.Tables[0].Updatekeyinplace);
        Assert.False(sent.Tables[1].HasUpdatewhere);
    }

    /// <summary>
    /// With the multi-table switch ON an empty table array is refused; with it OFF the same array is
    /// ordinary, because the legacy never applies the descriptors in that mode.
    /// </summary>
    /// <returns>The test.</returns>
    /// <remarks>
    /// The multi-table arm rejects an empty array with <c>E_INVALID_ARGUMENT</c>
    /// [ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru:L356-L363], while the
    /// single-table branch calls the update directly and never prepares at all [:L371].
    /// </remarks>
    [Fact]
    public async Task PrepareUpdateAsync_RefusesAnEmptyArrayOnlyInMultiTableMode()
    {
        (PersistenceClient client, _, FakeUpdateServiceClient update, _, _, _, _) = CreateClient();

        PrepareUpdateResponse refused = await client.PrepareUpdateAsync(
            new PrepareUpdateRequest { Task = Handle(), MultiTableUpdate = true },
            TestContext.Current.CancellationToken);

        Assert.Equal(WireRetCode.EInvalidArgument, refused.Status.RetCode);
        Assert.Empty(update.PrepareRequests);

        PrepareUpdateResponse accepted = await client.PrepareUpdateAsync(
            new PrepareUpdateRequest { Task = Handle(), MultiTableUpdate = false },
            TestContext.Current.CancellationToken);

        Assert.Equal(WireRetCode.Ok, accepted.Status.RetCode);
        Assert.False(Assert.Single(update.PrepareRequests).MultiTableUpdate);
    }

    /// <summary>
    /// The no-argument legacy commit form sends automatic rollback TRUE explicitly rather than leaving the
    /// optional field absent.
    /// </summary>
    /// <returns>The test.</returns>
    /// <remarks>
    /// <c>public function long of_commit ();return of_Commit(true)</c>
    /// [ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:L383], mirrored on the caller-side proxy
    /// [n_cst_threading_task_sqlbase.sru:L200].
    /// </remarks>
    [Fact]
    public async Task CommitAsync_NoArgumentFormSendsAutomaticRollback()
    {
        (PersistenceClient client, _, _, _, FakeTransactionServiceClient transaction, _, _) =
            CreateClient();

        _ = await client.CommitAsync(
            new SessionHandle { SessionId = "session-under-test" },
            TestContext.Current.CancellationToken);

        CommitRequest sent = Assert.Single(transaction.CommitRequests);
        Assert.True(sent.HasAutoRollback);
        Assert.True(sent.AutoRollback);
    }

    /// <summary>
    /// The execution convenience form preserves parameter ORDER and leaves an absent autocommit absent.
    /// </summary>
    /// <returns>The test.</returns>
    /// <remarks>
    /// Order matters because an unnamed parameter is matched POSITIONALLY, stopping after one substitution
    /// [ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru:L460, :L472]. The statement text is
    /// carried verbatim, INCLUDING a leading statement-caching selector - and forwarding it unread is what
    /// makes the mode reachable, because Persistence is the layer that removes the selector and honours the
    /// mode (AAP §0.4.3 C-07). A client that stripped the character here would silently cancel a mode the
    /// caller selected. No performance claim attaches to it at either end.
    /// </remarks>
    [Fact]
    public async Task ExecAsync_ConvenienceFormPreservesOrderAndAbsence()
    {
        (PersistenceClient client, _, _, FakeCommandServiceClient command, _, _, _) = CreateClient();

        PositionalParameter[] parameters =
        [
            new() { Name = string.Empty, Value = new AnyValue { StringValue = "Paul" } },
            new() { Name = "age", Value = new AnyValue { Int64Value = 32L } },
        ];

        _ = await client.ExecAsync(
            Handle(),
            "@INSERT INTO COMPANY (NAME,AGE) VALUES (?, ?)",
            parameters,
            autoCommit: null,
            TestContext.Current.CancellationToken);

        ExecRequest sent = Assert.Single(command.ExecRequests);
        Assert.Equal("@INSERT INTO COMPANY (NAME,AGE) VALUES (?, ?)", sent.Sql);
        Assert.Equal([string.Empty, "age"], sent.Parameters.Select(static parameter => parameter.Name));
        Assert.False(sent.HasAutocommit);

        // And a supplied mode is carried as supplied.
        _ = await client.ExecAsync(
            Handle(),
            "DELETE FROM COMPANY",
            parameters: null,
            AutoCommitMode.AcNative,
            TestContext.Current.CancellationToken);

        Assert.Equal(AutoCommitMode.AcNative, command.ExecRequests[1].Autocommit);
        Assert.Equal(11, PersistenceClient.LegacyExecArgumentCeiling);
    }

    // ==============================================================================================
    //  7 - THE BUSY GUARD
    // ==============================================================================================

    /// <summary>
    /// A setter is refused with the legacy busy code while a retrieval is streaming on the same task, and
    /// is accepted again once the stream has ended.
    /// </summary>
    /// <returns>The test.</returns>
    /// <remarks>
    /// Every caller-side legacy proxy setter opens with
    /// <c>if of_IsBusy() then return RetCode.E_BUSY</c>
    /// [ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlbase.sru:L57], because mutating a task's
    /// settings while its operation runs corrupts in-flight state. The RELEASE half matters just as much:
    /// a guard that never released would make the task unusable after its first retrieval.
    /// </remarks>
    [Fact]
    public async Task BusyGuard_RefusesASetterMidStreamAndReleasesAfterwards()
    {
        (PersistenceClient client, FakeQueryServiceClient query, FakeUpdateServiceClient update, _, _, _, _)
            = CreateClient();

        query.StreamMessages.Add(new QueryResponse
        {
            DataChunk = new QueryDataChunk { ChunkCount = 2L, ChunkIndex = 1L },
        });
        query.StreamMessages.Add(new QueryResponse
        {
            DataChunk = new QueryDataChunk { ChunkCount = 2L, ChunkIndex = 2L },
        });

        SetChunkSizeResponse? midStream = null;
        UpdateResponse? midStreamUpdate = null;

        await foreach (QueryResponse _ in client
            .QueryAsync(new QueryRequest { Task = Handle() }, TestContext.Current.CancellationToken))
        {
            midStream ??= await client.SetChunkSizeAsync(
                new SetChunkSizeRequest { Task = Handle(), ChunkSize = 5_000L },
                TestContext.Current.CancellationToken);

            midStreamUpdate ??= await client.UpdateAsync(
                new UpdateRequest { Task = Handle(), UpdateRows = 1L },
                TestContext.Current.CancellationToken);
        }

        Assert.NotNull(midStream);
        Assert.Equal(WireRetCode.EBusy, midStream.Status.RetCode);
        Assert.Empty(query.ChunkSizeRequests);

        Assert.NotNull(midStreamUpdate);
        Assert.Equal(WireRetCode.EBusy, midStreamUpdate.Status.RetCode);
        Assert.Equal(0, update.UpdateCalls);

        // RELEASED. The same setter now reaches the wire.
        SetChunkSizeResponse afterStream = await client.SetChunkSizeAsync(
            new SetChunkSizeRequest { Task = Handle(), ChunkSize = 5_000L },
            TestContext.Current.CancellationToken);

        Assert.Equal(WireRetCode.Ok, afterStream.Status.RetCode);
        Assert.Equal(5_000L, Assert.Single(query.ChunkSizeRequests).ChunkSize);
    }

    /// <summary>
    /// A retrieval on a task already streaming answers a single terminal BUSY status and sends nothing.
    /// </summary>
    /// <returns>The test.</returns>
    [Fact]
    public async Task BusyGuard_RefusesASecondRetrievalWithATerminalStatusMessage()
    {
        (PersistenceClient client, FakeQueryServiceClient query, _, _, _, _, _) = CreateClient();

        query.StreamMessages.Add(new QueryResponse
        {
            DataChunk = new QueryDataChunk { ChunkCount = 1L, ChunkIndex = 1L },
        });

        List<QueryResponse> second = [];

        await foreach (QueryResponse _ in client
            .QueryAsync(new QueryRequest { Task = Handle() }, TestContext.Current.CancellationToken))
        {
            await foreach (QueryResponse nested in client
                .QueryAsync(new QueryRequest { Task = Handle() }, TestContext.Current.CancellationToken))
            {
                second.Add(nested);
            }
        }

        QueryResponse only = Assert.Single(second);
        Assert.Equal(QueryResponse.PayloadOneofCase.Status, only.PayloadCase);
        Assert.Equal(WireRetCode.EBusy, only.Status.RetCode);

        // The second retrieval never reached the wire.
        Assert.Equal(1, query.QueryCalls);
    }

    /// <summary>
    /// A request carrying no task handle is FORWARDED rather than rejected, because whether a handle is
    /// required is the contract's question and Persistence's to answer.
    /// </summary>
    /// <returns>The test.</returns>
    [Fact]
    public async Task UntrackedRequest_IsForwardedRatherThanRejectedLocally()
    {
        (PersistenceClient client, FakeQueryServiceClient query, _, _, _, _, _) = CreateClient();

        SetChunkSizeResponse response = await client.SetChunkSizeAsync(
            new SetChunkSizeRequest { ChunkSize = 5_000L },
            TestContext.Current.CancellationToken);

        Assert.Equal(WireRetCode.Ok, response.Status.RetCode);
        Assert.Single(query.ChunkSizeRequests);
    }

    // ==============================================================================================
    //  8 - THE TWO RetCode FAMILIES, AND THE TRI-STATE ALGEBRA THEY MUST NOT LOSE
    // ==============================================================================================

    /// <summary>
    /// The wire codes this client produces agree numerically with the ported kernel constants, and the
    /// ported predicates classify them the way the legacy algebra does.
    /// </summary>
    /// <remarks>
    /// The agreement is a REVIEW-TIME INVARIANT with no compile-time edge - the contracts project holds no
    /// reference to the shared kernel - so it is asserted here for the codes this client actually emits.
    /// The tri-state properties are the ones a naive port loses: a PREVENTION reads as SUCCEEDED because
    /// the predicate tests for zero or greater [ws_objects/pfw.shared.pbl.src/issucceeded.srf:L11-L13],
    /// and CANCELLED is excluded from failure while also failing the success test
    /// [isfailed.srf:L11-L13], so it is NEITHER.
    /// </remarks>
    [Fact]
    public void TheTwoRetCodeFamilies_AgreeOnEveryCodeThisClientEmits()
    {
        Assert.Equal(KernelRetCode.E_INVALID_ARGUMENT, (long)WireRetCode.EInvalidArgument);
        Assert.Equal(KernelRetCode.E_INVALID_SQL, (long)WireRetCode.EInvalidSql);
        Assert.Equal(KernelRetCode.E_BUSY, (long)WireRetCode.EBusy);
        Assert.Equal(KernelRetCode.E_DB_ERROR, (long)WireRetCode.EDbError);
        Assert.Equal(KernelRetCode.OK, (long)WireRetCode.Ok);

        Assert.True(Predicates.IsSucceeded((long)WireRetCode.Prevent));
        Assert.False(Predicates.IsFailed((long)WireRetCode.Cancelled));
        Assert.False(Predicates.IsSucceeded((long)WireRetCode.Cancelled));
        Assert.True(Predicates.IsCancelled((long)WireRetCode.Cancelled));

        // NULL IS NEITHER, and is never collapsed to zero - doing so would convert "neither succeeded nor
        // failed" into "succeeded".
        Assert.False(Predicates.IsSucceeded((long?)null));
        Assert.False(Predicates.IsFailed((long?)null));
    }

    /// <summary>
    /// A refused update is REPORTED rather than thrown, so the tri-state algebra survives the boundary.
    /// </summary>
    /// <returns>The test.</returns>
    [Fact]
    public async Task NonOkStatuses_AreReturnedRatherThanThrown()
    {
        (PersistenceClient client, _, FakeUpdateServiceClient update, _, _, _, _) = CreateClient();

        update.UpdateAnswer = new UpdateResponse
        {
            Status = new OperationStatus { RetCode = WireRetCode.Cancelled },
        };

        UpdateResponse response = await client.UpdateAsync(
            new UpdateRequest { Task = Handle(), UpdateRows = 1L },
            TestContext.Current.CancellationToken);

        Assert.Equal(WireRetCode.Cancelled, response.Status.RetCode);
        Assert.False(Predicates.IsFailed((long)response.Status.RetCode));
        Assert.False(Predicates.IsSucceeded((long)response.Status.RetCode));
    }

    // ==============================================================================================
    //  9 - CONTRACT WIRING: EVERY REMAINING MEMBER REACHES THE RPC IT CLAIMS TO, AND CARRIES A
    //      CREDENTIAL WHILE DOING SO
    // ==============================================================================================

    /// <summary>
    /// Every remaining member of all four contracts forwards to its own rpc, returns the contract's own
    /// response unchanged, and attaches a bearer credential.
    /// </summary>
    /// <returns>The test.</returns>
    /// <remarks>
    /// WHY THE WIRING NEEDS A TEST AT ALL. The compiler proves the request and response TYPES, but not
    /// that a member is bound to the right METHOD - three of the four contracts declare a <c>Reset</c>
    /// and two declare a <c>SetAutoCommit</c>, so a mis-binding would compile. The doubles record which
    /// method they answered, which is what turns that into an assertion.
    /// <para>
    /// THE CREDENTIAL CHECK IS STRUCTURAL AS WELL AS OBSERVED: every unary member routes through the one
    /// private helper that acquires the token and builds the call options, so the token request count
    /// equalling the call count proves no member has its own unauthenticated path.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task EveryRemainingMember_ForwardsToItsOwnRpcWithACredential()
    {
        (PersistenceClient client,
            FakeQueryServiceClient query,
            FakeUpdateServiceClient update,
            FakeCommandServiceClient command,
            FakeTransactionServiceClient transaction,
            PersistenceStubTokenProvider tokens,
            _) = CreateClient();

        CancellationToken token = TestContext.Current.CancellationToken;
        SessionHandle session = new() { SessionId = "session-under-test" };

        // ---- C-05 ------------------------------------------------------------------------------
        CreateQueryTaskResponse created = await client.CreateQueryTaskAsync(
            new CreateQueryTaskRequest { Session = session }, token);
        Assert.Equal(PersistenceStubTaskIds.Query, created.Task.TaskId);

        Assert.Equal(
            WireRetCode.Ok,
            (await client.ResetQueryTaskAsync(new ResetQueryTaskRequest { Task = Handle() }, token))
                .Status.RetCode);

        CountResponse counted = await client.CountAsync(new CountRequest { Task = Handle() }, token);

        // READ THE COUNTED FLAG RATHER THAN INFERRING FROM THE TOTALS: the legacy suppresses counting
        // entirely in some modes [ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L818].
        Assert.True(counted.Counted);
        Assert.Equal(3L, counted.PageCount);
        Assert.Equal(27L, counted.RecordCount);

        Assert.Equal(
            WireRetCode.Ok,
            (await client.ReleaseQueryTaskAsync(new ReleaseQueryTaskRequest { Task = Handle() }, token))
                .Status.RetCode);

        Assert.Equal(
            [
                nameof(FakeQueryServiceClient.CreateQueryTaskAsync),
                nameof(FakeQueryServiceClient.ResetAsync),
                nameof(FakeQueryServiceClient.CountAsync),
                nameof(FakeQueryServiceClient.ReleaseQueryTaskAsync),
            ],
            query.Calls);

        // ---- C-06 ------------------------------------------------------------------------------
        _ = await client.CreateUpdateTaskAsync(new CreateUpdateTaskRequest { Session = session }, token);
        _ = await client.ResetUpdateTaskAsync(new ResetUpdateTaskRequest { Task = Handle() }, token);
        _ = await client.ReleaseUpdateTaskAsync(new ReleaseUpdateTaskRequest { Task = Handle() }, token);

        Assert.Equal(
            [
                nameof(FakeUpdateServiceClient.CreateUpdateTaskAsync),
                nameof(FakeUpdateServiceClient.ResetAsync),
                nameof(FakeUpdateServiceClient.ReleaseUpdateTaskAsync),
            ],
            update.Calls);

        // ---- C-07 ------------------------------------------------------------------------------
        _ = await client.CreateCommandTaskAsync(new CreateCommandTaskRequest { Session = session }, token);
        _ = await client.ResetCommandTaskAsync(new ResetCommandTaskRequest { Task = Handle() }, token);
        _ = await client.ReleaseCommandTaskAsync(new ReleaseCommandTaskRequest { Task = Handle() }, token);

        Assert.Equal(
            [
                nameof(FakeCommandServiceClient.CreateCommandTaskAsync),
                nameof(FakeCommandServiceClient.ResetAsync),
                nameof(FakeCommandServiceClient.ReleaseCommandTaskAsync),
            ],
            command.Calls);

        // ---- C-08 ------------------------------------------------------------------------------
        GetTransactionDataResponse view = await client.GetTransactionDataAsync(
            new GetTransactionDataRequest { Session = session }, token);

        // The read model carries the two PARSED flags but no parameter string and no password.
        Assert.True(view.Descriptor_.Flags.DisableBind);
        Assert.True(view.Descriptor_.Flags.NcharBind);

        _ = await client.SetTransactionAutoCommitAsync(
            new SetTransactionAutoCommitRequest { Session = session, Autocommit = true }, token);
        _ = await client.AutoCommitAsync(new AutoCommitRequest { Session = session }, token);

        // A ROLLBACK ANSWERS "FAILED" WHILE AUTOCOMMIT IS ON, which is preserved behaviour rather than an
        // error condition [ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:L185] - and it is
        // REPORTED, not thrown.
        RollbackResponse rolledBack = await client.RollbackAsync(
            new RollbackRequest { Session = session }, token);
        Assert.Equal(WireRetCode.Failed, rolledBack.Status.RetCode);

        IsConnectedResponse connected = await client.IsConnectedAsync(
            new IsConnectedRequest { Session = session }, token);
        Assert.True(connected.Connected);
        Assert.True(connected.Probed);

        GetDatabaseTypeResponse databaseType = await client.GetDatabaseTypeAsync(
            new GetDatabaseTypeRequest { Session = session }, token);

        // TWO MEMBERS ONLY, AND SQLITE IS DELIBERATELY NOT ONE OF THEM
        // [ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:L60-L61].
        Assert.Equal(DatabaseType.DbtOracle, databaseType.DatabaseType);
        Assert.Equal(0, (int)DatabaseType.DbtMssql);
        Assert.Equal(1, (int)DatabaseType.DbtOracle);
        Assert.Equal(2, Enum.GetValues<DatabaseType>().Length);

        GetSessionStateResponse state = await client.GetSessionStateAsync(
            new GetSessionStateRequest { Session = session }, token);

        // DRIVER CODE 100 IS "NO ROWS" AND IS NOT AN ERROR [n_cst_thread_trans.sru:L233].
        Assert.Equal(100L, state.SqlCode);
        Assert.True(state.Succeeded);

        _ = await client.ClearStateAsync(new ClearStateRequest { Session = session }, token);
        _ = await client.SetBrokenAsync(new SetBrokenRequest { Session = session }, token);

        GridSyntaxFromSqlResponse syntax = await client.GridSyntaxFromSqlAsync(
            new GridSyntaxFromSqlRequest { Session = session, Sql = "SELECT * FROM COMPANY" }, token);
        Assert.Equal("table(column=(...))", syntax.Syntax);

        _ = await client.EndSessionAsync(new EndSessionRequest { Session = session }, token);

        Assert.Equal(
            [
                nameof(FakeTransactionServiceClient.GetTransactionDataAsync),
                nameof(FakeTransactionServiceClient.SetAutoCommitAsync),
                nameof(FakeTransactionServiceClient.AutoCommitAsync),
                nameof(FakeTransactionServiceClient.RollbackAsync),
                nameof(FakeTransactionServiceClient.IsConnectedAsync),
                nameof(FakeTransactionServiceClient.GetDatabaseTypeAsync),
                nameof(FakeTransactionServiceClient.GetSessionStateAsync),
                nameof(FakeTransactionServiceClient.ClearStateAsync),
                nameof(FakeTransactionServiceClient.SetBrokenAsync),
                nameof(FakeTransactionServiceClient.GridSyntaxFromSqlAsync),
                nameof(FakeTransactionServiceClient.EndSessionAsync),
            ],
            transaction.Calls);

        // ONE CREDENTIAL PER CALL, ACROSS ALL FOUR CONTRACTS: 4 + 3 + 3 + 11 = 21.
        Assert.Equal(21, tokens.Requests);

        string expected = string.Concat(
            PersistenceStubTokenProvider.TokenType, " ", PersistenceStubTokenProvider.Credential);

        foreach (CallOptions? options in new[]
        {
            query.LastOptions, update.LastOptions, command.LastOptions, transaction.LastOptions,
        })
        {
            Assert.NotNull(options);
            Assert.Equal(expected, options.Value.Headers?.GetValue("authorization"));

            // EVERY CALL CARRIES A DEADLINE, AND ASSERTING THE OPPOSITE IS THE TEMPTING READING. The
            // reasoning for it - that a duration invented in the client would have no derivation, and that
            // the policy belongs in the composition root - is right about where the policy belongs and
            // wrong as a justification for having none. Without one, Persistence keeps working, and keeps
            // the query, update, command or transaction handle behind that work alive, for a caller
            // that has already gone; those handles are bounded per principal and globally, so the
            // abandoned work consumes admission capacity a live caller then cannot get. The policy now
            // exists as DataServices:Resilience:Persistence, and this client applies it.
            //
            // NO PARTICULAR VALUE IS ASSERTED HERE, deliberately. Which duration applies is
            // OutboundDeadlines' contract and is asserted against that type directly; what this suite
            // pins is that no member reaches the wire without one, which is the property that has to
            // hold for all thirty-five of them rather than for the two a targeted test would cover.
            Assert.NotNull(options.Value.Deadline);
            Assert.Equal(DateTimeKind.Utc, options.Value.Deadline.Value.Kind);
        }
    }

    /// <summary>
    /// The conflict type's plain constructors are usable and carry no detail, which is what lets a caller
    /// construct one without pretending to have a payload.
    /// </summary>
    [Fact]
    public void PersistenceConflictException_PlainConstructorsCarryNoDetail()
    {
        PersistenceConflictException fromDefault = new();
        Assert.NotEmpty(fromDefault.Message);
        Assert.Null(fromDefault.Conflict);
        Assert.Equal(0L, fromDefault.RetCode);
        Assert.Null(fromDefault.Trailers);

        PersistenceConflictException fromMessage = new("a message");
        Assert.Equal("a message", fromMessage.Message);
        Assert.Null(fromMessage.Conflict);

        InvalidOperationException cause = new("cause");
        PersistenceConflictException fromBoth = new("a message", cause);
        Assert.Same(cause, fromBoth.InnerException);
        Assert.Null(fromBoth.Conflict);
    }

    /// <summary>
    /// The rich constructor refuses an absent detail or an absent cause, so a conflict cannot be reported
    /// without the payload a caller needs to act on it.
    /// </summary>
    [Fact]
    public void PersistenceConflictException_RichConstructorRefusesAbsentArguments()
    {
        RpcException cause = new(new Status(StatusCode.Aborted, "conflict"));

        Assert.Throws<ArgumentNullException>(() =>
            new PersistenceConflictException(null!, 0L, cause));
        Assert.Throws<ArgumentNullException>(() =>
            new PersistenceConflictException(SampleConflict(), 0L, null!));
    }

    // ==============================================================================================
    // 10 - THE BUSY GUARD IS UNIFORM, AND IT IS CHECKED FIRST
    // ==============================================================================================

    /// <summary>
    /// Every guarded member of all four contracts refuses while the same task is streaming, sends nothing
    /// while refusing, and refuses with the BUSY code even when its own argument is separately invalid.
    /// </summary>
    /// <returns>The test.</returns>
    /// <remarks>
    /// WHY UNIFORMITY IS THE PROPERTY UNDER TEST. The legacy applies this guard as the FIRST statement of
    /// essentially every caller-side member - roughly forty sites, e.g.
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlquery.sru:L273</c>, <c>:L301</c>,
    /// <c>:L310</c>, <c>:L337</c>, <c>:L344</c>, <c>:L404</c>, <c>:L409</c>, <c>:L424</c>,
    /// <c>n_cst_threading_task_sqlupdate.sru:L99</c>, <c>:L207</c>, <c>:L223</c>,
    /// <c>n_cst_threading_task_sqlcommand.sru:L34</c>, <c>:L43</c>. A guard applied to most members but
    /// missing from one is worse than no guard at all, because the gap is invisible until it corrupts
    /// in-flight state. So the assertion is over the whole surface at once.
    /// <para>
    /// WHY THE ORDER MATTERS TOO. The legacy tests busy BEFORE it validates arguments, so a call that is
    /// both ill-timed and ill-formed is refused as ill-timed. Each case below therefore passes a
    /// separately invalid argument - a chunk size of zero, a negative row ceiling, an absent clause
    /// specification, an empty statement, a multi-table request with no tables - and still expects BUSY.
    /// Were the checks reordered, these would answer the argument code instead and this test would fail.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task BusyGuard_IsAppliedUniformlyAndAheadOfArgumentValidation()
    {
        (PersistenceClient client,
            FakeQueryServiceClient query,
            FakeUpdateServiceClient update,
            FakeCommandServiceClient command,
            _,
            _,
            _) = CreateClient();

        CancellationToken token = TestContext.Current.CancellationToken;

        query.StreamMessages.Add(new QueryResponse
        {
            DataChunk = new QueryDataChunk { ChunkCount = 1L, ChunkIndex = 1L },
        });

        List<(string Member, WireRetCode Code)> refusals = [];

        await foreach (QueryResponse _ in client.QueryAsync(new QueryRequest { Task = Handle() }, token))
        {
            // ---- C-05 -------------------------------------------------------------------------
            refusals.Add((
                nameof(PersistenceClient.ResetQueryTaskAsync),
                (await client.ResetQueryTaskAsync(
                    new ResetQueryTaskRequest { Task = Handle() }, token)).Status.RetCode));

            // ChunkSize = 0 is ALSO invalid on its own [n_cst_thread_task_sqlquery.sru:L410]; busy wins.
            refusals.Add((
                nameof(PersistenceClient.SetChunkSizeAsync),
                (await client.SetChunkSizeAsync(
                    new SetChunkSizeRequest { Task = Handle(), ChunkSize = 0L }, token)).Status.RetCode));

            // MaxRows = -1 is ALSO invalid on its own [:L433]; busy wins.
            refusals.Add((
                nameof(PersistenceClient.SetMaxRowsAsync),
                (await client.SetMaxRowsAsync(
                    new SetMaxRowsRequest { Task = Handle(), MaxRows = -1L }, token)).Status.RetCode));

            // An ABSENT clause specification is ALSO invalid on its own [:L271]; busy wins.
            refusals.Add((
                nameof(PersistenceClient.SetWhereClauseAsync),
                (await client.SetWhereClauseAsync(
                    new SetWhereClauseRequest { Task = Handle() }, token)).Status.RetCode));

            refusals.Add((
                nameof(PersistenceClient.SetOrderByClauseAsync),
                (await client.SetOrderByClauseAsync(
                    new SetOrderByClauseRequest { Task = Handle() }, token)).Status.RetCode));

            refusals.Add((
                nameof(PersistenceClient.SetPagingAsync),
                (await client.SetPagingAsync(
                    new SetPagingRequest { Task = Handle() }, token)).Status.RetCode));

            refusals.Add((
                nameof(PersistenceClient.SetPagedUniqueIndexColumnsAsync),
                (await client.SetPagedUniqueIndexColumnsAsync(
                    new SetPagedUniqueIndexColumnsRequest { Task = Handle() }, token)).Status.RetCode));

            refusals.Add((
                nameof(PersistenceClient.CountAsync),
                (await client.CountAsync(new CountRequest { Task = Handle() }, token)).Status.RetCode));

            // ---- C-06 -------------------------------------------------------------------------
            refusals.Add((
                nameof(PersistenceClient.ResetUpdateTaskAsync),
                (await client.ResetUpdateTaskAsync(
                    new ResetUpdateTaskRequest { Task = Handle() }, token)).Status.RetCode));

            // Multi-table ON with no tables is ALSO invalid on its own
            // [n_cst_thread_task_sqlupdate.sru:L356-L363]; busy wins.
            refusals.Add((
                nameof(PersistenceClient.PrepareUpdateAsync),
                (await client.PrepareUpdateAsync(
                    new PrepareUpdateRequest { Task = Handle(), MultiTableUpdate = true }, token))
                    .Status.RetCode));

            refusals.Add((
                nameof(PersistenceClient.UpdateAsync),
                (await client.UpdateAsync(
                    new UpdateRequest { Task = Handle(), UpdateRows = 1L }, token)).Status.RetCode));

            // ---- C-07 -------------------------------------------------------------------------
            refusals.Add((
                nameof(PersistenceClient.ResetCommandTaskAsync),
                (await client.ResetCommandTaskAsync(
                    new ResetCommandTaskRequest { Task = Handle() }, token)).Status.RetCode));

            refusals.Add((
                nameof(PersistenceClient.SetCommandAutoCommitAsync),
                (await client.SetCommandAutoCommitAsync(
                    new SetCommandAutoCommitRequest { Task = Handle(), Autocommit = AutoCommitMode.AcOn },
                    token)).Status.RetCode));

            // An empty statement is ALSO invalid on its own, with its OWN code
            // [n_cst_thread_task_sqlcommand.sru:L45]; busy still wins.
            refusals.Add((
                nameof(PersistenceClient.SetCommandSqlAsync),
                (await client.SetCommandSqlAsync(
                    new SetCommandSqlRequest { Task = Handle(), Sql = string.Empty }, token))
                    .Status.RetCode));

            refusals.Add((
                nameof(PersistenceClient.ExecAsync),
                (await client.ExecAsync(new ExecRequest { Task = Handle() }, token)).Status.RetCode));
        }

        // EVERY guarded member refused, and refused with the same code.
        Assert.Equal(15, refusals.Count);
        Assert.All(refusals, entry => Assert.Equal(WireRetCode.EBusy, entry.Code));

        // AND NOTHING REACHED THE WIRE WHILE REFUSING. The stream itself is the only call recorded.
        Assert.Equal(1, query.QueryCalls);
        Assert.Empty(query.Calls);
        Assert.Empty(query.ChunkSizeRequests);
        Assert.Empty(query.MaxRowsRequests);
        Assert.Empty(query.WhereClauseRequests);
        Assert.Empty(query.OrderByClauseRequests);
        Assert.Empty(query.PagingRequests);
        Assert.Empty(query.UniqueIndexRequests);
        Assert.Empty(update.Calls);
        Assert.Empty(update.PrepareRequests);
        Assert.Equal(0, update.UpdateCalls);
        Assert.Empty(command.Calls);
        Assert.Empty(command.ExecRequests);

        // RELEASED WHEN THE SPAN ENDS: the same members now reach the wire, and the one that is invalid on
        // its own now answers with ITS OWN code rather than BUSY - which is what proves the guard was the
        // reason for every refusal above, not the argument.
        Assert.Equal(
            WireRetCode.Ok,
            (await client.ResetQueryTaskAsync(new ResetQueryTaskRequest { Task = Handle() }, token))
                .Status.RetCode);

        Assert.Equal(
            WireRetCode.EInvalidArgument,
            (await client.SetChunkSizeAsync(
                new SetChunkSizeRequest { Task = Handle(), ChunkSize = 0L }, token)).Status.RetCode);

        Assert.Equal(
            WireRetCode.EInvalidSql,
            (await client.SetCommandSqlAsync(
                new SetCommandSqlRequest { Task = Handle(), Sql = string.Empty }, token))
                .Status.RetCode);
    }

    // ==============================================================================================
    // 11 - AN ABSENT STATUS IS A REPRESENTABLE WIRE STATE, AND MUST NOT FAULT THE CLIENT
    // ==============================================================================================

    /// <summary>
    /// A response whose status message is absent is surfaced rather than faulting, and the diagnostic it
    /// produces still carries no statement text.
    /// </summary>
    /// <returns>The test.</returns>
    /// <remarks>
    /// WHY THIS IS NOT A CONTRIVED CASE. In proto3 a singular message field is genuinely absent-able, so
    /// a peer at a different contract revision - or a fault injected between the two - can deliver a
    /// response with no status at all. Reading it with a bare dereference would turn a degraded peer into
    /// a client crash, which is a failure mode the in-process legacy could not have had and which this
    /// refactor must therefore not introduce.
    /// <para>
    /// It also pins the tri-state reading: an absent status must NOT be read as the zero of the algebra,
    /// because zero means SUCCESS [ws_objects/pfw.shared.pbl.src/retcode.sru]. The client neither claims
    /// success nor invents a failure; it hands the response back for the caller to interpret.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AbsentStatus_IsSurfacedRatherThanFaulting()
    {
        (PersistenceClient client, _, FakeUpdateServiceClient update, _, _, _,
            PersistenceRecordingLogger logger) = CreateClient();

        // No Status, and no Counts either - the minimum a peer can legally send.
        update.UpdateAnswer = new UpdateResponse();

        UpdateResponse response = await client.UpdateAsync(
            new UpdateRequest { Task = Handle(), UpdateRows = 1L },
            TestContext.Current.CancellationToken);

        Assert.Null(response.Status);
        Assert.Equal(1, update.UpdateCalls);

        // The client did not manufacture a verdict in either direction.
        Assert.DoesNotContain("SELECT", logger.AllText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE COMPANY", logger.AllText(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// With the diagnostic level suppressed the outcome trace is not written at all, and the failure
    /// diagnostic - which is not level-guarded - still omits the statement.
    /// </summary>
    /// <returns>The test.</returns>
    /// <remarks>
    /// The trace is behind a level check so that a production deployment pays nothing for it. The failure
    /// path deliberately is NOT, because a database failure must be visible at the default level; what
    /// makes that safe is the field-level redaction rather than a level gate. See
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlcommand.sru:L110</c> for why the
    /// statement field can never be logged.
    /// </remarks>
    [Fact]
    public async Task SuppressedDiagnosticLevel_WritesNoTraceAndStillRedactsTheFailure()
    {
        (PersistenceClient client, _, FakeUpdateServiceClient update, _, _, _,
            PersistenceRecordingLogger logger) = CreateClient();

        logger.MinimumLevel = LogLevel.Warning;

        update.UpdateAnswer = new UpdateResponse
        {
            Status = new OperationStatus { RetCode = WireRetCode.Ok },
            Counts = new UpdateCounts { Inserted = 1L },
        };

        _ = await client.UpdateAsync(
            new UpdateRequest { Task = Handle(), UpdateRows = 1L },
            TestContext.Current.CancellationToken);

        // The success trace is a debug-level record; suppressed, nothing is written.
        Assert.Empty(logger.Records);

        // A failure is reported regardless of the level, and still without the statement.
        update.UpdateAnswer = new UpdateResponse
        {
            Status = new OperationStatus
            {
                RetCode = WireRetCode.EDbError,
                DbError = new DbError
                {
                    Sqldbcode = -1L,
                    Sqlerrtext = "constraint failed",
                    Sqlsyntax = "UPDATE COMPANY SET SALARY = 9999 WHERE ID = 7",
                    Buffer = DwBuffer.Primary,
                    Row = 7L,
                },
            },
        };

        _ = await client.UpdateAsync(
            new UpdateRequest { Task = Handle(), UpdateRows = 1L },
            TestContext.Current.CancellationToken);

        string written = logger.AllText();

        Assert.NotEmpty(written);
        Assert.DoesNotContain("UPDATE COMPANY", written, StringComparison.Ordinal);
        Assert.DoesNotContain("SALARY", written, StringComparison.Ordinal);
        Assert.DoesNotContain("constraint failed", written, StringComparison.Ordinal);
        Assert.Contains("-1", written, StringComparison.Ordinal);
    }

    // ==============================================================================================
    // 12 - CONTRACT DRIFT IS REFUSED, NOT GUESSED AT
    // ==============================================================================================

    /// <summary>
    /// A binding that has drifted from what this client can decode makes the decoder refuse, so the caller
    /// receives the untouched gRPC failure instead of a payload parsed as a type it may not be.
    /// </summary>
    /// <param name="trailerKey">The trailer key the drifted binding declares.</param>
    /// <param name="statusCode">The status code the drifted binding declares.</param>
    /// <param name="payloadType">The payload type the drifted binding declares.</param>
    /// <remarks>
    /// WHY REFUSING IS THE CORRECT ANSWER TO DRIFT. The payload arrives as opaque bytes. Parsing them as a
    /// type the contract no longer says they are would produce a conflict detail assembled from whatever
    /// those bytes happened to decode into - a wrong row state that a caller would then act on, which is
    /// precisely the silent-corruption class of failure the update contract exists to prevent. Refusing
    /// costs the caller only the typed shape: the exception, its status and its complete trailer set are
    /// rethrown intact, so nothing is lost and nothing is invented.
    /// <para>
    /// The three cases are the three ways the generated contract can move underneath this client: the key
    /// the payload travels under, the status it is attached to, and the type it is encoded as.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("", 10, "common.v1.RichErrorTrailer")]
    [InlineData("some-other-key-bin", 4, "common.v1.RichErrorTrailer")]
    [InlineData("some-other-key-bin", 10, "common.v2.SomeOtherTrailer")]
    public void DriftedRichErrorBinding_IsRefusedRatherThanParsed(
        string trailerKey,
        int statusCode,
        string payloadType)
    {
        (PersistenceClient client, _, _, _, _, _, PersistenceRecordingLogger logger) = CreateClient();

        // The trailer carries the WIDENED code, which is why the kernel constant is the right form here.
        RichErrorTrailer payload = new()
        {
            RetCode = KernelRetCode.E_DB_ERROR,
            Conflict = SampleConflict(),
        };

        RpcException aborted = AbortedWith(payload.ToByteArray());

        RichErrorBinding drifted = new()
        {
            TrailerKey = trailerKey,
            GrpcStatusCode = statusCode,
            PayloadType = payloadType,
        };

        ConflictDetail? decoded = client.TryReadConflictDetail(aborted, drifted, out long retCode);

        // NOTHING WAS DECODED, AND NOTHING WAS INVENTED.
        Assert.Null(decoded);
        Assert.Equal(0L, retCode);

        // THE FAILURE ITSELF IS UNTOUCHED: the caller still has the status and the payload bytes.
        Assert.Equal(StatusCode.Aborted, aborted.StatusCode);
        Assert.NotNull(aborted.Trailers.GetValueBytes(Binding().TrailerKey));

        // A drift is reported so it is diagnosable - except for the status-code case, which is an ordinary
        // "this failure is not the conflict one" answer rather than a misconfiguration.
        if (statusCode == 10)
        {
            Assert.NotEmpty(logger.Records);
            Assert.Contains("unchanged", logger.AllText(), StringComparison.OrdinalIgnoreCase);
        }

        // AND NO DIAGNOSTIC LEAKS THE PAYLOAD, on any of the three arms.
        Assert.DoesNotContain("COMPANY", logger.AllText(), StringComparison.Ordinal);
    }

    /// <summary>
    /// An absent binding is refused in the same way as a drifted one, so a contract that stops declaring
    /// the trailer cannot cause bytes to be read under a guessed key.
    /// </summary>
    [Fact]
    public void AbsentRichErrorBinding_IsRefusedRatherThanGuessed()
    {
        (PersistenceClient client, _, _, _, _, _, PersistenceRecordingLogger logger) = CreateClient();

        RpcException aborted = AbortedWith(new RichErrorTrailer
        {
            RetCode = KernelRetCode.E_DB_ERROR,
            Conflict = SampleConflict(),
        }.ToByteArray());

        ConflictDetail? decoded = client.TryReadConflictDetail(aborted, binding: null, out long retCode);

        Assert.Null(decoded);
        Assert.Equal(0L, retCode);
        Assert.Contains("unchanged", logger.AllText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("COMPANY", logger.AllText(), StringComparison.Ordinal);
    }

    /// <summary>
    /// The decoder refuses an absent failure rather than dereferencing it.
    /// </summary>
    [Fact]
    public void ConflictDecoder_RefusesAnAbsentFailure()
    {
        (PersistenceClient client, _, _, _, _, _, _) = CreateClient();

        Assert.Throws<ArgumentNullException>(() =>
            client.TryReadConflictDetail(null!, Binding(), out _));
    }
}
