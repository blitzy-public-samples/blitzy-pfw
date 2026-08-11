// ==================================================================================================
//  PersistenceWorkHandleLifecycleTests - THE C-05 AND C-06 HANDLE LIFECYCLE, END TO END
//  ------------------------------------------------------------------------------------------------
//  WHAT THESE ROWS EXIST TO CATCH, STATED AS THE DEFECT THEY WOULD HAVE FOUND
//
//  `persistence.v1` issues an opaque server-held handle for a transaction session and another for a task,
//  and field 1 of both `QueryRequest` and `UpdateRequest` is that task handle. `DataWindowService` was
//  building both messages with a DEFAULT handle - a message whose task_id is the empty string - because
//  nothing in this service had ever created one. Persistence refuses a blank handle outright with
//  `RetCode.E_INVALID_HANDLE` before it reaches a statement, so EVERY retrieval and EVERY update this
//  service issued was refused upstream, and the refusal looked like a caller error rather than a missing
//  call. Meanwhile all six lifecycle operations were published on `PersistenceClient` and none was called.
//
//  Not one assertion in the pre-existing suites could see that, because every one of them asserted on the
//  answer a SCRIPTED upstream gave rather than on what reached it. So this file asserts three things no
//  other row does:
//
//    1. THE HANDLES ARE ACQUIRED - a session is begun and a task created before any row is asked for, and
//       the run call NAMES the task the create call issued.
//    2. THE HANDLES ARE RELEASED ON EVERY PATH - success, upstream refusal, conflict and cancellation
//       alike, task before session. A leak is invisible to every assertion about a response, which is
//       exactly why it needs its own rows.
//    3. THE REFUSAL ARMS ARE HONEST - a retrieval that could not acquire refuses with a status that
//       classifies WHY, and an update refuses on the message where its outcome field already lives.
//
//  ============ WHY TWO VEHICLES ==================================================================
//  Class 1 drives the DEPLOYED host through the REST projection, so the refusal arms are asserted as the
//  HTTP status a caller actually receives - which is also the projection this service publishes for
//  Gateway. Class 2 drives the service IN-PROCESS over the generated-stub doubles, because cancellation
//  timing and a message-borne outcome code are only assertable where the test owns the cancellation source
//  and reads the response object rather than its serialization. Neither vehicle overrides the client's own
//  scope composition: both exercise `OpenQueryScopeAsync` and `OpenUpdateScopeAsync` as shipped.
// ==================================================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.Extensions.Options;
using PowerFramework.Contracts.DataServices.V1;
using PowerFramework.DataServices.Clients;
using PowerFramework.DataServices.Grpc;
using Xunit;
using RetCode = PowerFramework.Shared.Kernel.RetCode;
using WireRetCode = PowerFramework.Contracts.Common.V1.RetCode.Types.Value;
using WireUpdateRequest = PowerFramework.Contracts.DataServices.V1.UpdateRequest;
using WireUpdateResponse = PowerFramework.Contracts.DataServices.V1.UpdateResponse;

namespace PowerFramework.DataServices.Tests;

// =====================================================================================================
//  1. THROUGH THE DEPLOYED HOST - acquisition, release, and the refusal arms as HTTP
// =====================================================================================================

/// <summary>
/// The retrieval half of the lifecycle, driven through the deployed host's REST projection.
/// </summary>
/// <remarks>
/// THE PROJECTION IS THE OBSERVATION POINT ON PURPOSE. It runs the real <c>DataWindowService</c> in
/// process and translates the gRPC status it raises into the HTTP status Gateway consumes, so one row
/// covers the service's classification AND the projection's mapping of it. A gRPC client over the test
/// server would have asserted the first without the second.
/// </remarks>
public sealed class PersistenceWorkHandleAcquisitionTests(DataServicesTestHostFactory host)
    : IClassFixture<DataServicesTestHostFactory>
{
    /// <summary>The projected retrieval route.</summary>
    private const string RetrieveRoute = "/v1/datawindow/retrieve";

    /// <summary>The DataWindow the rows retrieve from. Any non-blank handle reaches the upstream.</summary>
    private const string Handle = "dw-1";

    /// <summary>
    /// A retrieval acquires a session and a task, names the task on the run call, and releases both.
    /// </summary>
    /// <remarks>
    /// THE FOUR ASSERTIONS ARE FOUR SEPARATE FAILURES. A service that never created a task fails the
    /// second; one that created it and sent a default handle anyway fails the third; one that created it
    /// and never gave it back fails the fourth. Collapsing them into "the retrieval succeeded" is precisely
    /// what let the original defect through.
    /// </remarks>
    [Fact]
    public async Task ARetrievalAcquiresItsHandlesNamesTheTaskAndGivesBothBack()
    {
        host.PersistenceEdge.Reset();
        host.PersistenceEdge.ScriptQuery(rowCount: 2L);

        using HttpClient client = host.CreateAuthenticatedClient();

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            new Uri(RetrieveRoute, UriKind.Relative),
            new { datawindowHandle = Handle },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // 1 - the session and the task were both created, exactly once each.
        Assert.Equal(1, host.PersistenceEdge.BeginSessionCalls);
        Assert.Equal(1, host.PersistenceEdge.CreateQueryTaskCalls);

        // 2 - the run call named the task the create call issued, rather than the default blank handle
        //     Persistence refuses with E_INVALID_HANDLE.
        string? issued = host.PersistenceEdge.LastQueryRequest?.Task?.TaskId;
        Assert.False(string.IsNullOrWhiteSpace(issued));
        Assert.StartsWith("scripted-query-task-", issued, StringComparison.Ordinal);

        // 3 - the specification travelled on the CREATE call, which is where C-05 puts a task's initial
        //     configuration, and was NOT repeated on the run call: QueryRequest.spec merges over the state
        //     the task already has, so a clause setter carrying SQL_MS_APPEND would append twice.
        Assert.Equal(Handle, host.PersistenceEdge.LastCreateQueryTaskRequest?.Spec?.DataObject);
        Assert.Null(host.PersistenceEdge.LastQueryRequest?.Spec);

        // 4 - and both were given back, THE TASK BEFORE THE SESSION: the task borrowed the pooled
        //     transaction the session owns, so the other order would leave it holding a reference to
        //     something already collected. A pair of counts cannot tell the two orders apart.
        Assert.Equal(1, host.PersistenceEdge.ReleaseQueryTaskCalls);
        Assert.Equal(1, host.PersistenceEdge.EndSessionCalls);
        Assert.Equal(["query-task", "session"], host.PersistenceEdge.ReleaseOrder);
        Assert.True(
            host.PersistenceEdge.NothingIsStillHeld,
            "A retrieval left server-held handles behind: "
            + Describe(host.PersistenceEdge));
    }

    /// <summary>
    /// A refused session refuses the retrieval as 503 and asks Persistence for no rows at all.
    /// </summary>
    /// <remarks>
    /// THE "NO ROWS ASKED FOR" HALF IS THE POINT. An implementation that acquired lazily, or that ignored
    /// the acquisition's outcome and sent the run call anyway, would still fail the retrieval - but it would
    /// fail it with Persistence's <c>E_INVALID_HANDLE</c>, naming the wrong problem and doing upstream work
    /// for a request that could never have succeeded.
    /// </remarks>
    [Fact]
    public async Task ARefusedSessionRefusesTheRetrievalAndAsksForNoRows()
    {
        host.PersistenceEdge.Reset();
        host.PersistenceEdge.ScriptQuery(rowCount: 2L);
        host.PersistenceEdge.BeginSessionCode = RetCode.E_DB_ERROR;

        using HttpClient client = host.CreateAuthenticatedClient();

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            new Uri(RetrieveRoute, UriKind.Relative),
            new { datawindowHandle = Handle },
            TestContext.Current.CancellationToken);

        // Unavailable is the acquisition failure's default arm, and the projection answers it 503.
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

        Assert.Equal(0, host.PersistenceEdge.QueryCalls);
        Assert.Equal(0, host.PersistenceEdge.CreateQueryTaskCalls);

        // Nothing was acquired, so nothing is released - and NOT ending a session that was never begun is
        // as much a part of the contract as ending one that was.
        Assert.Equal(0, host.PersistenceEdge.EndSessionCalls);
        Assert.True(host.PersistenceEdge.NothingIsStillHeld);
    }

    /// <summary>
    /// A refused task still ends the session that was already begun, and refuses as 400 when the refusal
    /// names a caller-supplied value.
    /// </summary>
    /// <remarks>
    /// THE SECOND REFUSAL IS THE ONE THAT LEAKS. Acquisition is two calls, and this is the case where the
    /// first succeeded: a scope that returned null on a failed task creation would have no session to
    /// release and the pooled-transaction reference would be held for the life of the process. C-05
    /// adjudicates each initial setting on the create call, which is why a rejected setting is
    /// <c>InvalidArgument</c> and 400 rather than the default arm's 503.
    /// </remarks>
    [Fact]
    public async Task ARefusedTaskStillEndsTheSessionAndRefusesAsABadRequest()
    {
        host.PersistenceEdge.Reset();
        host.PersistenceEdge.ScriptQuery(rowCount: 2L);
        host.PersistenceEdge.CreateQueryTaskCode = RetCode.E_INVALID_ARGUMENT;

        using HttpClient client = host.CreateAuthenticatedClient();

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            new Uri(RetrieveRoute, UriKind.Relative),
            new { datawindowHandle = Handle },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        Assert.Equal(1, host.PersistenceEdge.BeginSessionCalls);
        Assert.Equal(1, host.PersistenceEdge.CreateQueryTaskCalls);
        Assert.Equal(0, host.PersistenceEdge.QueryCalls);

        // THE SESSION WAS STILL ENDED. This is the assertion that a null-returning acquisition could not
        // have satisfied.
        Assert.Equal(1, host.PersistenceEdge.EndSessionCalls);
        Assert.Equal(0, host.PersistenceEdge.ReleaseQueryTaskCalls);
        Assert.True(
            host.PersistenceEdge.NothingIsStillHeld,
            "A refused task creation left its session behind: " + Describe(host.PersistenceEdge));
    }

    /// <summary>
    /// A create that succeeds without a handle is refused here rather than sent on as a blank one.
    /// </summary>
    /// <remarks>
    /// A REAL PRODUCER FAULT AND A SEPARATE ARM. A status of <c>OK</c> with no handle beside it is
    /// malformed, and a consumer that read the handle without testing it would send a blank task_id and be
    /// answered <c>E_INVALID_HANDLE</c> - a diagnostic that blames the caller for the producer's bug. The
    /// session was still acquired, so it must still be ended.
    /// </remarks>
    [Fact]
    public async Task ASucceedingCreateWithoutAHandleIsRefusedRatherThanSentAsBlank()
    {
        host.PersistenceEdge.Reset();
        host.PersistenceEdge.ScriptQuery(rowCount: 2L);
        host.PersistenceEdge.IssueBlankTaskHandle = true;

        using HttpClient client = host.CreateAuthenticatedClient();

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            new Uri(RetrieveRoute, UriKind.Relative),
            new { datawindowHandle = Handle },
            TestContext.Current.CancellationToken);

        // ⚠ 500, NOT 503, AND THE DIFFERENCE IS WHAT THE ANSWER MEANS ⚠
        //
        // This row first expected 503, which is what the acquisition mapping's DEFAULT arm produces - it was
        // the answer this case fell through to rather than an answer chosen for it. A handle-less success is
        // not "the service cannot serve right now": the upstream reported SUCCESS and then carried nothing
        // to address, which is a breach of its own contract. 503 invites a retry and a retry cannot clear a
        // producer bug, so the mapping now names this case explicitly and answers 500 - an unretryable
        // server-side fault - while E_BUSY keeps its own 429 and a refused transaction its own 412. The
        // sibling row RetrieveRefusesAHandlelessSessionSuccessRatherThanSendingATasklessRequest pins the
        // same decision at the gRPC layer as Internal.
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(0, host.PersistenceEdge.QueryCalls);
        Assert.Equal(1, host.PersistenceEdge.EndSessionCalls);
        Assert.True(host.PersistenceEdge.NothingIsStillHeld);
    }

    /// <summary>
    /// A busy upstream is 429 rather than 503, so a caller can tell "try later" from "not right now".
    /// </summary>
    /// <remarks>
    /// THIS ROW IS ALSO THE FORWARD CONTRACT FOR THE HANDLE REGISTRIES. A registry at capacity answers
    /// <c>E_BUSY</c>, and the only status that projects to 429 is <c>ResourceExhausted</c>; folding it into
    /// the default arm would tell a caller its request was malformed or the service was down, and a
    /// resilience policy would draw the wrong retry conclusion from both.
    /// </remarks>
    [Fact]
    public async Task ABusyUpstreamIsReportedAsTooManyRequests()
    {
        host.PersistenceEdge.Reset();
        host.PersistenceEdge.ScriptQuery(rowCount: 2L);
        host.PersistenceEdge.BeginSessionCode = RetCode.E_BUSY;

        using HttpClient client = host.CreateAuthenticatedClient();

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            new Uri(RetrieveRoute, UriKind.Relative),
            new { datawindowHandle = Handle },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(0, host.PersistenceEdge.QueryCalls);
        Assert.True(host.PersistenceEdge.NothingIsStillHeld);
    }

    /// <summary>
    /// A refusal by policy is 403, which no resilience policy may retry.
    /// </summary>
    [Fact]
    public async Task ARefusalByPolicyIsForbiddenRatherThanUnavailable()
    {
        host.PersistenceEdge.Reset();
        host.PersistenceEdge.ScriptQuery(rowCount: 2L);
        host.PersistenceEdge.BeginSessionCode = RetCode.E_ACCESS_DENIED;

        using HttpClient client = host.CreateAuthenticatedClient();

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            new Uri(RetrieveRoute, UriKind.Relative),
            new { datawindowHandle = Handle },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, host.PersistenceEdge.QueryCalls);
        Assert.True(host.PersistenceEdge.NothingIsStillHeld);
    }

    /// <summary>
    /// Two retrievals in sequence acquire and release independently rather than sharing or accumulating.
    /// </summary>
    /// <remarks>
    /// A HANDLE HELD ACROSS REQUESTS WOULD PASS EVERY ROW ABOVE. Each of them drives one request, so an
    /// implementation that acquired once and cached would satisfy them all and still leak one handle per
    /// process. Two requests with two acquisitions, two releases and nothing held at the end is what
    /// distinguishes per-operation ownership from a cache.
    /// </remarks>
    [Fact]
    public async Task TwoRetrievalsAcquireAndReleaseIndependently()
    {
        host.PersistenceEdge.Reset();
        host.PersistenceEdge.ScriptQuery(rowCount: 1L);

        using HttpClient client = host.CreateAuthenticatedClient();

        for (int attempt = 0; attempt < 2; attempt++)
        {
            using HttpResponseMessage response = await client.PostAsJsonAsync(
                new Uri(RetrieveRoute, UriKind.Relative),
                new { datawindowHandle = Handle },
                TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        Assert.Equal(2, host.PersistenceEdge.BeginSessionCalls);
        Assert.Equal(2, host.PersistenceEdge.CreateQueryTaskCalls);
        Assert.Equal(2, host.PersistenceEdge.ReleaseQueryTaskCalls);
        Assert.Equal(2, host.PersistenceEdge.EndSessionCalls);
        Assert.True(host.PersistenceEdge.NothingIsStillHeld, Describe(host.PersistenceEdge));
    }

    /// <summary>Renders what is still held, for a failure message that names the leak.</summary>
    /// <param name="edge">The edge to describe.</param>
    /// <returns>The description.</returns>
    private static string Describe(ScriptedPersistenceEdge edge) => string.Format(
        CultureInfo.InvariantCulture,
        "sessions=[{0}] queryTasks=[{1}] updateTasks=[{2}]",
        string.Join(", ", edge.HeldSessionIds),
        string.Join(", ", edge.HeldQueryTaskIds),
        string.Join(", ", edge.HeldUpdateTaskIds));
}

// =====================================================================================================
//  2. IN PROCESS - the update half, the conflict path, and cancellation
// =====================================================================================================

/// <summary>
/// The update half of the lifecycle and the two paths whose release is easiest to lose: a conflict and a
/// cancellation.
/// </summary>
/// <remarks>
/// IN PROCESS RATHER THAN THROUGH THE HOST, for two reasons that the host cannot give. A conflict and an
/// update refusal are read off the RESPONSE OBJECT here rather than off its serialization, so the row
/// asserts the outcome code the contract declares rather than a JSON rendering of it. And cancellation is
/// driven by the test's own source at a chosen instant - after the first chunk has been written and while
/// the scope is still live - which is the only way to assert that the release runs on the cancellation path
/// rather than merely that it runs eventually.
/// </remarks>
public sealed class PersistenceWorkHandleReleaseTests
{
    /// <summary>
    /// An update names its task on the call and releases the task and the session afterwards.
    /// </summary>
    [Fact]
    public async Task AnUpdateNamesItsTaskAndReleasesBothHandles()
    {
        C03Fixture fixture = new();

        WireUpdateRequest request = new() { DatawindowHandle = "dw-1" };

        WireUpdateResponse response = await fixture.Service.Update(request, fixture.Context);

        Assert.Equal(WireRetCode.Ok, response.RetCode);

        // The run call named the task the create call issued.
        Assert.Equal(PersistenceStubTaskIds.Update, fixture.Persistence.LastUpdate?.Task?.TaskId);
        Assert.Single(fixture.Persistence.UpdateStub.CreateTaskRequests);

        // And it was given back, together with the session.
        Assert.Equal(
            PersistenceStubTaskIds.Update,
            Assert.Single(fixture.Persistence.UpdateStub.ReleaseTaskRequests).Task.TaskId);
        Assert.Single(fixture.Persistence.TransactionStub.BeginRequests);
        Assert.Single(fixture.Persistence.TransactionStub.EndRequests);
    }

    /// <summary>
    /// A conflict is still raised as <c>Aborted</c>, and the handles are still released.
    /// </summary>
    /// <remarks>
    /// THE MOST LIKELY NON-SUCCESS OUTCOME OF AN UPDATE IS THE ONE MOST LIKELY TO LEAK. An
    /// <c>updatewhereclause</c> mismatch leaves this frame by <c>throw</c>, so an acquisition released in a
    /// success-path statement rather than by scope disposal would hold an update task - and with it a
    /// worker task and a pooled-transaction reference - for the life of the process, on the exact path a
    /// contended table takes most often. The conflict itself must still surface unchanged: it is never
    /// retried, because retrying one automatically is the silent-overwrite failure mode
    /// (AAP 0.6.3.8).
    /// </remarks>
    [Fact]
    public async Task AConflictStillReleasesBothHandles()
    {
        C03Fixture fixture = new();
        fixture.Persistence.Conflict = new PersistenceConflictException(
            new PowerFramework.Contracts.Common.V1.ConflictDetail
            {
                UpdateTable = "COMPANY",
                RowsExpected = 1L,
                RowsMatched = 0L,
            },
            RetCode.E_DB_ERROR,
            new RpcException(new Status(StatusCode.Aborted, "scripted conflict")));

        RpcException failure = await Assert.ThrowsAsync<RpcException>(async () =>
            await fixture.Service.Update(
                new WireUpdateRequest { DatawindowHandle = "dw-1" },
                fixture.Context));

        Assert.Equal(StatusCode.Aborted, failure.StatusCode);
        Assert.Equal(1, fixture.Persistence.UpdateCalls);

        // The release ran on the throwing path.
        Assert.Single(fixture.Persistence.UpdateStub.ReleaseTaskRequests);
        Assert.Single(fixture.Persistence.TransactionStub.EndRequests);
    }

    /// <summary>
    /// A refused update task is reported on the response's own outcome field rather than as a status.
    /// </summary>
    /// <remarks>
    /// THE OPPOSITE CHOICE FROM RETRIEVAL, AND FOR THE OPPOSITE REASON. <c>UpdateResponse</c> declares an
    /// outcome-code field and every other non-success outcome of this operation already travels there, so a
    /// refusal has somewhere honest to go; <c>RetrieveChunk</c> declares no such field, and its
    /// <c>error</c> member is reserved for a failure the legacy would have surfaced through its error
    /// event, which a handle acquisition is not. The upstream code travels UNCHANGED, so a caller sees why.
    /// </remarks>
    [Fact]
    public async Task ARefusedUpdateTaskTravelsOnTheResponseAndStillEndsTheSession()
    {
        C03Fixture fixture = new();
        fixture.Persistence.UpdateStub.CreateTaskCode = RetCode.E_DB_ERROR;

        WireUpdateResponse response = await fixture.Service.Update(
            new WireUpdateRequest { DatawindowHandle = "dw-1" },
            fixture.Context);

        Assert.Equal((WireRetCode)(int)RetCode.E_DB_ERROR, response.RetCode);

        // The upstream was never asked to update, and the session was still ended.
        Assert.Equal(0, fixture.Persistence.UpdateCalls);
        Assert.Single(fixture.Persistence.TransactionStub.EndRequests);
        Assert.Empty(fixture.Persistence.UpdateStub.ReleaseTaskRequests);
    }

    /// <summary>
    /// A retrieval cancelled mid-stream still releases its handles.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE RELEASE IS ISSUED WITH A FRESH TOKEN, WHICH IS WHY THIS ROW EXISTS. Cleanup on the cancellation
    /// path is reached BECAUSE the caller's token was cancelled, so a release that forwarded that token
    /// would cancel the very call that undoes the acquisition and the handle would be held for the life of
    /// the process. An implementation that passed the token through would pass every other row in this file
    /// and fail this one.
    /// </para>
    /// <para>
    /// THE CANCELLATION HAPPENS AT A CHOSEN INSTANT rather than before the call: the writer cancels when it
    /// receives the first chunk, so the scope is provably live and the upstream stream provably in flight
    /// when the token trips.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ACancelledRetrievalStillReleasesItsHandles()
    {
        C03Fixture fixture = new();
        fixture.Persistence.QueryScript.Add(RetrievalChunk(1L, 3L));
        fixture.Persistence.QueryScript.Add(RetrievalChunk(2L, 3L));
        fixture.Persistence.QueryScript.Add(RetrievalChunk(3L, 3L));

        CancellingStreamWriter writer = new(fixture.Lifetime);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await fixture.Service.Retrieve(
                new RetrieveRequest { DatawindowHandle = "dw-1" },
                writer,
                fixture.Context));

        // It really did get far enough to be mid-stream rather than refused up front.
        Assert.Equal(1, writer.Writes);

        // And the handles came back anyway.
        Assert.Equal(
            PersistenceStubTaskIds.Query,
            Assert.Single(fixture.Persistence.QueryStub.ReleaseTaskRequests).Task.TaskId);
        Assert.Single(fixture.Persistence.TransactionStub.EndRequests);
    }

    /// <summary>Builds one upstream chunk carrying no rows.</summary>
    /// <param name="index">The producer's ONE-BASED chunk ordinal.</param>
    /// <param name="count">How many chunks the producer declares in total.</param>
    /// <returns>The upstream message.</returns>
    /// <remarks>
    /// ROWLESS ON PURPOSE. This row is about the release path, and a chunk with rows would drag the whole
    /// buffer projection into a test whose subject is a handle. The chunk count is greater than the index
    /// so the stream does not mark itself final before the cancellation lands.
    /// </remarks>
    private static PowerFramework.Contracts.Persistence.V1.QueryResponse RetrievalChunk(
        long index,
        long count) =>
        new()
        {
            DataChunk = new PowerFramework.Contracts.Persistence.V1.QueryDataChunk
            {
                ChunkIndex = index,
                ChunkCount = count,
                State = new PowerFramework.Contracts.Persistence.V1.CarrierState
                {
                    Segments =
                    {
                        new PowerFramework.Contracts.Persistence.V1.CarrierBufferSegment
                        {
                            Buffer = PowerFramework.Contracts.Common.V1.DwBuffer.Primary,
                        },
                    },
                },
            },
        };
}

/// <summary>
/// A stream writer that cancels a token source the moment it receives its first message.
/// </summary>
/// <param name="lifetime">The source to cancel.</param>
/// <remarks>
/// IT IMPLEMENTS ONLY THE SINGLE-ARGUMENT <c>WriteAsync</c>, exactly as the production ASP.NET Core
/// writer does, so a write that wrongly forwarded a cancellation token would fail here as it fails in
/// production rather than being quietly tolerated by a more permissive double.
/// </remarks>
internal sealed class CancellingStreamWriter(CancellationTokenSource lifetime)
    : IServerStreamWriter<RetrieveChunk>
{
    private readonly CancellationTokenSource _lifetime = lifetime;

    /// <inheritdoc/>
    public WriteOptions? WriteOptions { get; set; }

    /// <summary>How many messages were written before the stream stopped.</summary>
    public int Writes { get; private set; }

    /// <inheritdoc/>
    public Task WriteAsync(RetrieveChunk message)
    {
        Writes++;

        // Cancel AFTER accepting the message, so the operation is provably past its acquisition and
        // provably mid-stream when the token trips.
        _lifetime.Cancel();

        return Task.CompletedTask;
    }
}
