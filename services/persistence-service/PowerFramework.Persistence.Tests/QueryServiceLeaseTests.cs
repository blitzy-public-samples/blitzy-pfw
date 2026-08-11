// ==================================================================================================
//  QueryServiceLeaseTests.cs - the operation lease on contract C-05, persistence.v1.QueryService
// ==================================================================================================
//
//  SCOPE, STATED HONESTLY SO THE FILE NAME IS NOT READ AS A CLAIM. This is NOT a full parity suite for
//  Grpc/QueryService.cs. It covers one property of it: that every mutation, every run, every count read
//  and the release itself pass through the entry's operation lease, so no two of them can overlap on one
//  task and no teardown can run under one. The rest of that adapter - the four numeric guard boundaries,
//  the clause modification styles, the paging validation, the five stream arms - is not covered here and
//  is not covered anywhere else either; that gap is real and is recorded rather than papered over.
//
//  ONE SECOND, DISTINCT PROPERTY LIVES HERE TOO, and it is here only because this file already owns the
//  fixture that can reach it: section 5 pins the REDACTION of the terminal status diagnostic on the way
//  out. It is a security property rather than a lease property, so it carries its own banner.
//
//  THE FINDING THIS PINS. The oracle's caller-side guard is `if of_IsBusy() then return RetCode.E_BUSY`,
//  written out nine times, once per mutator
//  [ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlbase.sru:L57, :L73, :L95, :L110, :L124,
//  :L144, :L162, :L179, :L188]. In process that READ cannot be raced: the caller and the task are one
//  thread of control. Across a request boundary two calls can both observe "not busy" and both proceed,
//  so a setter lands on the task a retrieval has already started merging its specification from, or a
//  reset clears state the run is using. The guard therefore becomes a LEASE - the same codes, the same
//  order, now indivisible (constraint C-B).
//
//  THE RETRIEVAL RUNNER IS THE SEAM, WHICH IS WHY NO DATABASE IS NEEDED (constraints C-E, C-H).
//  IQueryRetrievalRunner is a one-method seam over SqlQueryTask.ExecuteAsync, so a runner that blocks on
//  a gate holds the lease open for as long as a test wants without a connection, a statement or a row.
//  The task itself is the PRODUCTION one, resolved from the real composition root through
//  IQueryTaskFactory, so nothing about the object under lease is simulated.
//
//  WHAT THIS SUITE CANNOT ESTABLISH, SAID HERE RATHER THAN LEFT TO BE DISCOVERED. There is no seam
//  inside a MUTATION to pause at: SqlQueryTask is sealed, every setter on it is a pure field or clause
//  write with no injected collaborator, and the adapter logs nothing while holding the lease. So no test
//  can catch a real mutator mid-flight and ask whether it is holding the lease. Three tests below hold
//  THE SAME LEASE the mutators take, directly on the entry, which pins the mutual-exclusion contract and
//  the teardown handoff but does not by itself distinguish a mutator that TAKES the lease from one that
//  merely READ a flag - the distinction the fix is about. That distinction is covered two other ways:
//  TaskOperationLatchTests exercises every interleaving of the shared state machine directly, including
//  under contention, and the last test below pins the property that a mutator which takes a lease must
//  also give it back, which is the failure mode the fix itself could introduce.
// ==================================================================================================

using Grpc.Core;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using PowerFramework.Persistence.Configuration;
using PowerFramework.Persistence.Errors;
using PowerFramework.Persistence.Grpc;
using PowerFramework.Persistence.Tasks;
using PowerFramework.Persistence.Transactions;
using GrpcQueryService = PowerFramework.Persistence.Grpc.QueryService;

namespace PowerFramework.Persistence.Tests;

/// <summary>
/// Tests for the operation lease that <c>Grpc/QueryService.cs</c> takes on every call.
/// </summary>
public sealed class QueryServiceLeaseTests
{
    /// <summary>The issuer the host under test is configured to trust. A reserved name; nothing resolves.</summary>
    private const string TrustedIssuer = "https://security.invalid";

    // ---------------------------------------------------------------------------------------------
    //  1. MUTATION AND RUN EXCLUDE EACH OTHER
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task EveryMutatorIsRefusedWhileARetrievalIsInFlight()
    {
        // Five mutators, one per shape the adapter has: the reset, the two numeric setters, a clause
        // setter and the paging setter. Each answers E_BUSY - retryable - and none of them reaches the
        // task, which is the difference between a lease and a re-read of a flag.
        using Fixture fixture = new();
        TaskHandle handle = await fixture.CreateTaskAsync();

        TaskCompletionSource gate = new();
        fixture.Runner.Gate = gate;

        CollectingStream stream = new();
        Task retrieval = fixture.Service.Query(
            new QueryRequest { Task = handle },
            stream,
            Fixture.Context);

        await fixture.Runner.Entered.Task;

        Assert.Equal(
            WireRetCode.EBusy,
            (await fixture.Service.Reset(new ResetQueryTaskRequest { Task = handle }, Fixture.Context))
                .Status.RetCode);
        Assert.Equal(
            WireRetCode.EBusy,
            (await fixture.Service.SetChunkSize(
                new SetChunkSizeRequest { Task = handle, ChunkSize = 5_000 },
                Fixture.Context)).Status.RetCode);
        Assert.Equal(
            WireRetCode.EBusy,
            (await fixture.Service.SetMaxRows(
                new SetMaxRowsRequest { Task = handle, MaxRows = 10 },
                Fixture.Context)).Status.RetCode);
        Assert.Equal(
            WireRetCode.EBusy,
            (await fixture.Service.SetWhereClause(
                new SetWhereClauseRequest
                {
                    Task = handle,
                    Clause = new SqlClauseSpec
                    {
                        // ONE-BASED, and a zero select index is refused by its own guard [:L271] - so an
                        // unset field here would fail the call for a reason that has nothing to do with
                        // the lease.
                        SelectIndex = 1,
                        ModifyStyle = SqlModifyStyle.SqlMsReplace,
                        Clause = "id > 0",
                    },
                },
                Fixture.Context)).Status.RetCode);
        Assert.Equal(
            WireRetCode.EBusy,
            (await fixture.Service.SetPaging(
                new SetPagingRequest { Task = handle, Paged = true, PageSize = 10, PageIndex = 1 },
                Fixture.Context)).Status.RetCode);

        // A SECOND RETRIEVAL IS REFUSED TOO, and the runner was entered exactly once - so the refusal is
        // the adapter's own and the worker's latch behind it was never even asked.
        CollectingStream second = new();
        await fixture.Service.Query(new QueryRequest { Task = handle }, second, Fixture.Context);

        Assert.Equal(WireRetCode.EBusy, Assert.Single(second.Written).Status.RetCode);
        Assert.Equal(1, fixture.Runner.Runs);

        gate.SetResult();
        await retrieval;

        // The lease came back, so the very same mutator now succeeds unchanged.
        Assert.Equal(
            WireRetCode.Ok,
            (await fixture.Service.Reset(new ResetQueryTaskRequest { Task = handle }, Fixture.Context))
                .Status.RetCode);
    }

    [Fact]
    public async Task ARetrievalIsRefusedWhileAMutationHoldsTheLease()
    {
        // THE DIRECTION THE ORACLE NEVER HAD TO STATE. Its nine guards protect the task from a mutator
        // arriving during a RUN; a run arriving during a MUTATION was impossible in process, because the
        // two were the same thread of control. Across a request boundary the exclusion has to hold both
        // ways, or a retrieval starts on a task whose specification is half rewritten.
        //
        // HOW THE MUTATION IS HELD, STATED PLAINLY: no seam exists inside a mutator to pause at - every
        // setter on the task is a pure field or clause write with no collaborator to intercept - so the
        // test takes THE SAME LEASE the mutators take, directly on the entry. That is not a simulation of
        // the condition; it IS the condition, because the lease is the single object all of them share.
        using Fixture fixture = new();
        TaskHandle handle = await fixture.CreateTaskAsync();

        Assert.True(fixture.Tasks.TryResolve(handle, out QueryTaskEntry? entry));
        Assert.Equal(TaskLatchOutcome.Acquired, entry!.TryBeginExclusive());

        CollectingStream stream = new();
        await fixture.Service.Query(new QueryRequest { Task = handle }, stream, Fixture.Context);

        Assert.Equal(WireRetCode.EBusy, Assert.Single(stream.Written).Status.RetCode);
        Assert.Equal(0, fixture.Runner.Runs);

        // Give the lease back the way a mutation does, and the retrieval now goes through.
        Assert.False(entry.EndExclusive());

        CollectingStream second = new();
        await fixture.Service.Query(new QueryRequest { Task = handle }, second, Fixture.Context);

        Assert.Equal(1, fixture.Runner.Runs);
    }

    [Fact]
    public async Task TwoMutatorsCannotOverlapEachOther()
    {
        // Two setters interleaving is the same defect as a setter interleaving a run: the specification the
        // next retrieval reads would be a blend of two requests. Held the same way, and for the same
        // reason, as the test above.
        using Fixture fixture = new();
        TaskHandle handle = await fixture.CreateTaskAsync();

        Assert.True(fixture.Tasks.TryResolve(handle, out QueryTaskEntry? entry));
        Assert.Equal(TaskLatchOutcome.Acquired, entry!.TryBeginExclusive());

        // Every mutator shape is refused, including the reset - which states its guard inline rather than
        // through the shared helper, and so is the one that could most easily be left behind.
        Assert.Equal(
            WireRetCode.EBusy,
            (await fixture.Service.SetMaxRows(
                new SetMaxRowsRequest { Task = handle, MaxRows = 9 },
                Fixture.Context)).Status.RetCode);
        Assert.Equal(
            WireRetCode.EBusy,
            (await fixture.Service.Reset(new ResetQueryTaskRequest { Task = handle }, Fixture.Context))
                .Status.RetCode);
        Assert.Equal(
            WireRetCode.EBusy,
            (await fixture.Service.SetOrderByClause(
                new SetOrderByClauseRequest
                {
                    Task = handle,
                    Clause = new SqlClauseSpec
                    {
                        // ONE-BASED, and a zero select index is refused by its own guard [:L271] - so an
                        // unset field here would fail the call for a reason that has nothing to do with
                        // the lease.
                        SelectIndex = 1,
                        ModifyStyle = SqlModifyStyle.SqlMsReplace,
                        Clause = "id",
                    },
                },
                Fixture.Context)).Status.RetCode);
        Assert.Equal(
            WireRetCode.EBusy,
            (await fixture.Service.SetPagedUniqueIndexColumns(
                new SetPagedUniqueIndexColumnsRequest { Task = handle, Columns = { "id" } },
                Fixture.Context)).Status.RetCode);

        Assert.False(entry.EndExclusive());

        Assert.Equal(
            WireRetCode.Ok,
            (await fixture.Service.SetMaxRows(
                new SetMaxRowsRequest { Task = handle, MaxRows = 9 },
                Fixture.Context)).Status.RetCode);
    }

    [Fact]
    public async Task AReleaseArrivingDuringAMutationDefersTheTeardownToTheMutationsExit()
    {
        // The teardown handoff on the mutation side. The release retires the handle and is refused the
        // duty; the mutation's exit takes it. Hazard 1 of docs/PB多线程绕坑提示.md applies to a mutation
        // exactly as it does to a run - the object is in use either way.
        using Fixture fixture = new();
        TaskHandle handle = await fixture.CreateTaskAsync();

        Assert.True(fixture.Tasks.TryResolve(handle, out QueryTaskEntry? entry));
        Assert.Equal(TaskLatchOutcome.Acquired, entry!.TryBeginExclusive());

        Assert.Equal(
            WireRetCode.Ok,
            (await fixture.Service.ReleaseQueryTask(
                new ReleaseQueryTaskRequest { Task = handle },
                Fixture.Context)).Status.RetCode);

        Assert.False(fixture.Tasks.TryResolve(handle, out _));
        Assert.True(entry.IsReleased);

        // TRUE, and that is the whole point: the duty passed to this exit rather than to the releaser.
        Assert.True(entry.EndExclusive());
    }

    [Fact]
    public async Task EveryMutatorGivesItsLeaseBackSoTheNextCallIsNotBusy()
    {
        // THE FAILURE MODE THE FIX ITSELF COULD INTRODUCE, and the one property here that a lease-free
        // implementation would pass for a different reason: a mutator that takes the lease and returns
        // without releasing it pins the task for the life of the process, and every later call on that
        // handle then answers E_BUSY for ever. The symptom is indistinguishable from a hung retrieval, so
        // it is pinned rather than trusted to the `finally` being present.
        using Fixture fixture = new();
        TaskHandle handle = await fixture.CreateTaskAsync();

        // One of every mutator shape, in sequence. Each must leave the lease free for the next.
        Assert.Equal(
            WireRetCode.Ok,
            (await fixture.Service.SetChunkSize(
                new SetChunkSizeRequest { Task = handle, ChunkSize = 5_000 },
                Fixture.Context)).Status.RetCode);
        Assert.Equal(
            WireRetCode.Ok,
            (await fixture.Service.SetMaxRows(
                new SetMaxRowsRequest { Task = handle, MaxRows = 10 },
                Fixture.Context)).Status.RetCode);
        Assert.Equal(
            WireRetCode.Ok,
            (await fixture.Service.SetWhereClause(
                new SetWhereClauseRequest
                {
                    Task = handle,
                    Clause = new SqlClauseSpec
                    {
                        // ONE-BASED, and a zero select index is refused by its own guard [:L271] - so an
                        // unset field here would fail the call for a reason that has nothing to do with
                        // the lease.
                        SelectIndex = 1,
                        ModifyStyle = SqlModifyStyle.SqlMsReplace,
                        Clause = "id > 0",
                    },
                },
                Fixture.Context)).Status.RetCode);
        Assert.Equal(
            WireRetCode.Ok,
            (await fixture.Service.SetPaging(
                new SetPagingRequest { Task = handle, Paged = true, PageSize = 10, PageIndex = 1 },
                Fixture.Context)).Status.RetCode);
        Assert.Equal(
            WireRetCode.Ok,
            (await fixture.Service.SetPagedUniqueIndexColumns(
                new SetPagedUniqueIndexColumnsRequest { Task = handle, Columns = { "id" } },
                Fixture.Context)).Status.RetCode);
        Assert.Equal(
            WireRetCode.Ok,
            (await fixture.Service.Reset(new ResetQueryTaskRequest { Task = handle }, Fixture.Context))
                .Status.RetCode);

        // AND A REFUSED MUTATOR MUST NOT LEAK A LEASE EITHER. The chunk-size guard is `<= 1000`, so one
        // thousand itself is refused [n_cst_thread_task_sqlquery.sru:L410] - and that refusal happens
        // INSIDE the lease, which is the arm a missing `finally` would strand.
        Assert.Equal(
            WireRetCode.EInvalidArgument,
            (await fixture.Service.SetChunkSize(
                new SetChunkSizeRequest { Task = handle, ChunkSize = 1_000 },
                Fixture.Context)).Status.RetCode);

        // A retrieval still runs, so nothing above stranded the lease.
        CollectingStream stream = new();
        await fixture.Service.Query(new QueryRequest { Task = handle }, stream, Fixture.Context);

        Assert.Equal(1, fixture.Runner.Runs);
        Assert.Empty(stream.Written);
    }

    // ---------------------------------------------------------------------------------------------
    //  2. THE COUNT READER IS DELIBERATELY OUTSIDE THE LEASE
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task TheCountReaderIsNotRefusedWhileARetrievalIsInFlight()
    {
        // NO BUSY GUARD, DELIBERATELY (constraint C-B). The oracle's nine guards are on MUTATORS; its
        // three count accessors [n_cst_threading_task_sqlquery.sru:L70, :L71, :L78] have none, and a field
        // read cannot fail. Adding one here would refuse a call the legacy answers, so the reader stays
        // outside the lease and reads a consistent snapshot under the entry's own leaf gate instead.
        using Fixture fixture = new();
        TaskHandle handle = await fixture.CreateTaskAsync();

        TaskCompletionSource gate = new();
        fixture.Runner.Gate = gate;

        CollectingStream stream = new();
        Task retrieval = fixture.Service.Query(
            new QueryRequest { Task = handle },
            stream,
            Fixture.Context);

        await fixture.Runner.Entered.Task;

        CountResponse counts = await fixture.Service.Count(
            new CountRequest { Task = handle },
            Fixture.Context);

        Assert.Equal(WireRetCode.Ok, counts.Status.RetCode);

        gate.SetResult();
        await retrieval;
    }

    // ---------------------------------------------------------------------------------------------
    //  3. THE TEARDOWN HANDOFF
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task AReleaseArrivingDuringARetrievalDefersTheTeardownToTheRetrievalsExit()
    {
        // Hazard 1 of docs/PB多线程绕坑提示.md at a request boundary: a teardown with work still pending
        // faults. The release retires the handle and hands disposal to whichever path finishes last.
        using Fixture fixture = new();
        TaskHandle handle = await fixture.CreateTaskAsync();

        Assert.True(fixture.Tasks.TryResolve(handle, out QueryTaskEntry? entry));

        TaskCompletionSource gate = new();
        fixture.Runner.Gate = gate;

        CollectingStream stream = new();
        Task retrieval = fixture.Service.Query(
            new QueryRequest { Task = handle },
            stream,
            Fixture.Context);

        await fixture.Runner.Entered.Task;

        Assert.Equal(
            WireRetCode.Ok,
            (await fixture.Service.ReleaseQueryTask(
                new ReleaseQueryTaskRequest { Task = handle },
                Fixture.Context)).Status.RetCode);

        // Retired, released - and still running, so nothing was disposed.
        Assert.False(fixture.Tasks.TryResolve(handle, out _));
        Assert.True(entry!.IsReleased);
        Assert.True(entry.IsRunning);

        gate.SetResult();
        await retrieval;

        Assert.False(entry.IsRunning);

        // A second release finds nothing and says so.
        Assert.Equal(
            WireRetCode.EInvalidHandle,
            (await fixture.Service.ReleaseQueryTask(
                new ReleaseQueryTaskRequest { Task = handle },
                Fixture.Context)).Status.RetCode);
    }

    [Fact]
    public async Task ARequestArrivingAfterAReleaseIsAnUnknownHandleRatherThanBusy()
    {
        // GONE is NOT retryable advice and BUSY is, so conflating them would send a caller into a loop
        // that can never succeed.
        using Fixture fixture = new();
        TaskHandle handle = await fixture.CreateTaskAsync();

        Assert.Equal(
            WireRetCode.Ok,
            (await fixture.Service.ReleaseQueryTask(
                new ReleaseQueryTaskRequest { Task = handle },
                Fixture.Context)).Status.RetCode);

        Assert.Equal(
            WireRetCode.EInvalidHandle,
            (await fixture.Service.Reset(new ResetQueryTaskRequest { Task = handle }, Fixture.Context))
                .Status.RetCode);
        Assert.Equal(
            WireRetCode.EInvalidHandle,
            (await fixture.Service.SetMaxRows(
                new SetMaxRowsRequest { Task = handle, MaxRows = 1 },
                Fixture.Context)).Status.RetCode);

        CollectingStream stream = new();
        await fixture.Service.Query(new QueryRequest { Task = handle }, stream, Fixture.Context);

        Assert.Equal(WireRetCode.EInvalidHandle, Assert.Single(stream.Written).Status.RetCode);
    }

    // ---------------------------------------------------------------------------------------------
    //  4. CANCELLATION REACHES THE RUNNER
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task TheRequestsOwnTokenIsWhatTheRetrievalRunnerReceives()
    {
        // F-11 for this contract. The retrieval is the ONE provider call on this service whose whole call
        // chain is already asynchronous, so its token genuinely stops a multi-row walk between rows; what
        // is asserted here is only that the token which arrives is the REQUEST's, not None and not a fresh
        // one, because everything below depends on that.
        using Fixture fixture = new();
        TaskHandle handle = await fixture.CreateTaskAsync();

        using CancellationTokenSource source = new();
        CollectingStream stream = new();

        await fixture.Service.Query(
            new QueryRequest { Task = handle },
            stream,
            new Fixture.CallContext(source.Token));

        Assert.Equal(source.Token, fixture.Runner.TokenSeen);
    }

    // ---------------------------------------------------------------------------------------------
    //  5. THE TERMINAL DIAGNOSTIC IS MASKED ON THE WAY OUT  (constraint C-F, AAP 0.6.3.8)
    // ---------------------------------------------------------------------------------------------
    //
    //  THE ONE TEXT ON THIS CONTRACT THAT ARRIVES FROM OUTSIDE THE ADAPTER. Every other diagnostic the
    //  query service emits is a fixed sentence it wrote itself; this one is whatever the worker task
    //  raised. Two of the task layer's arms raise statement-bearing text rather than a sentence - the
    //  carrier's select-property modification failure forwards the runtime's own message, which quotes
    //  the whole rejected `DataWindow.Table.Select='...'` assignment, and the driver's message echoes
    //  offending values. Relaying either verbatim would hand a caller row data and statement text
    //  through a field the contract documents as opaque display text.
    //
    //  WHAT SURVIVES IS THE POINT. The redactor masks literals and comment bodies only, so the wording,
    //  the shape and the return code are unchanged and any diagnostic quoting no value passes through
    //  byte for byte - which is every fixed sentence the oracle raises (constraint C-B).

    [Fact]
    public async Task ATerminalDiagnosticCarryingLiteralsIsMaskedBeforeItReachesTheStream()
    {
        using Fixture fixture = new();
        TaskHandle handle = await fixture.CreateTaskAsync();

        Assert.True(fixture.Tasks.TryResolve(handle, out QueryTaskEntry? entry));

        // Shaped exactly like the real modify diagnostic: a fixed prefix, then the rejected property
        // assignment with a whole statement inside it.
        const string Raw =
            "设置 DataWindow.Table.Select 失败: DataWindow.Table.Select='SELECT ID FROM COMPANY "
            + "WHERE NAME = ''Zhang Wei'' AND SALARY > 82500'";

        fixture.Runner.OnEntered = () => entry!.Faults.OnError(RetCode.E_INTERNAL_ERROR, Raw);
        fixture.Runner.Result = RetCode.E_INTERNAL_ERROR;

        CollectingStream stream = new();
        await fixture.Service.Query(new QueryRequest { Task = handle }, stream, Fixture.Context);

        OperationStatus status = Assert.Single(stream.Written).Status;

        // The outcome is unchanged - masking is not a status change.
        Assert.Equal(WireRetCode.EInternalError, status.RetCode);

        // No literal from the quoted statement leaves.
        Assert.DoesNotContain("Zhang Wei", status.ErrorText, StringComparison.Ordinal);
        Assert.DoesNotContain("82500", status.ErrorText, StringComparison.Ordinal);
        Assert.Contains(SqlRedactor.DefaultPlaceholder, status.ErrorText, StringComparison.Ordinal);

        // ...and the message still reads as itself, which is what keeps it useful to a caller.
        Assert.Contains("DataWindow.Table.Select", status.ErrorText, StringComparison.Ordinal);
        Assert.Contains("失败", status.ErrorText, StringComparison.Ordinal);

        // The RECORDER is untouched: it reproduces the oracle's own last-error field, so the mask belongs
        // at the egress and not there.
        Assert.Equal(Raw, entry!.Faults.Snapshot().ErrorText);
    }

    [Fact]
    public async Task AFixedSentenceDiagnosticPassesThroughByteForByte()
    {
        // THE OTHER HALF OF THE CLAIM, and without it the mask could be a blanket replacement and this
        // suite would not notice. Every diagnostic the oracle raises for a guard is a fixed sentence
        // quoting no value, and every one of them must arrive unaltered (constraint C-B).
        using Fixture fixture = new();
        TaskHandle handle = await fixture.CreateTaskAsync();

        Assert.True(fixture.Tasks.TryResolve(handle, out QueryTaskEntry? entry));

        const string Sentence = "检索失败";

        fixture.Runner.OnEntered = () => entry!.Faults.OnError(RetCode.E_DB_ERROR, Sentence);
        fixture.Runner.Result = RetCode.E_DB_ERROR;

        CollectingStream stream = new();
        await fixture.Service.Query(new QueryRequest { Task = handle }, stream, Fixture.Context);

        OperationStatus status = Assert.Single(stream.Written).Status;

        Assert.Equal(WireRetCode.EDbError, status.RetCode);
        Assert.Equal(Sentence, status.ErrorText);
        Assert.DoesNotContain(SqlRedactor.DefaultPlaceholder, status.ErrorText, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------------------------------------
    //  6. THE SESSION LIFECYCLE: NOTHING IS PUBLISHED AGAINST A SESSION THAT HAS BEGUN RETIRING
    // ---------------------------------------------------------------------------------------------
    //
    //  A DIFFERENT EXCLUSION FROM THE ONE ABOVE, AND IT IS WHY BOTH LIVE IN THIS FILE. Sections 1 to 4
    //  are about two operations overlapping on one TASK; this one is about a task being published into a
    //  registry that a session teardown has already walked. EndSession marks its session closing inside
    //  the session's lifecycle gate and only then purges the task registries, so a create that reaches
    //  that gate after the mark must be refused - otherwise the task it registers survives the purge and
    //  holds a pooled transaction that has been handed back, and the pool may already have re-issued that
    //  transaction to a different session.
    //
    //  The mark is written here exactly as EndSession writes it - under the gate - so the case exercises
    //  the real interleaving rather than a simulated flag.

    [Fact]
    public async Task ACreateThatReachesTheGateAfterTheClosingMarkIsRefusedAndPublishesNothing()
    {
        using Fixture fixture = new();

        Assert.True(fixture.Sessions.TryResolve(fixture.Session, out TransactionSession? session));
        Assert.NotNull(session);

        using (session!.Gate.Enter())
        {
            session.MarkClosing();
        }

        CreateQueryTaskResponse refused = await fixture.Service.CreateQueryTask(
            new CreateQueryTaskRequest { Session = new SessionHandle { SessionId = fixture.Session } },
            Fixture.Context);

        // The code the oracle answers for a transaction it cannot use
        // [ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:L113].
        Assert.Equal(WireRetCode.EInvalidTransaction, refused.Status.RetCode);
        Assert.Null(refused.Task);

        // A DISTINCT DIAGNOSTIC FROM THE UNKNOWN-SESSION ONE: the handle WAS live when the caller sent it,
        // so the message says the session began retiring rather than that it was never held.
        Assert.Contains("began retiring", refused.Status.ErrorText, StringComparison.Ordinal);
        Assert.DoesNotContain("does not hold", refused.Status.ErrorText, StringComparison.Ordinal);

        // AND NOTHING WAS PUBLISHED, which is the property that matters: a handle issued here would name a
        // task the purge has already walked past.
        Assert.Equal(0, fixture.Tasks.Count);
    }

    [Fact]
    public async Task ALiveSessionStillPublishesSoTheRefusalIsNotUnconditional()
    {
        // THE OTHER HALF OF THE CLAIM. Without it the liveness test could be a constant refusal and the
        // case above would still pass.
        using Fixture fixture = new();

        TaskHandle handle = await fixture.CreateTaskAsync();

        Assert.NotEqual(string.Empty, handle.TaskId);
        Assert.Equal(1, fixture.Tasks.Count);
    }

    // ---------------------------------------------------------------------------------------------
    //  7. THE READ SCOPE IS ENFORCED, NOT ASSUMED
    // ---------------------------------------------------------------------------------------------
    //
    //  THE BOUNDARY HALF OF A CLAIM WHOSE GRAMMAR HALF LIVES IN ReadOnlyStatementGuardTests. This
    //  contract is published under `persistence.read`, and the justification on record for that used to
    //  be that the legacy retrieval task generates SELECT statements and nothing else - true of the TASK
    //  and silent about the SURFACE. Two of this contract's inputs are caller-authored SQL that reaches
    //  the database: QuerySpec.sql, and QuerySpec.sql_syntax through its `retrieve="..."` clause. Left
    //  ungated, a read-scoped credential reached INSERT, DELETE, DDL, PRAGMA and - because the provider
    //  executes every statement in a batch it is handed - whole batches spliced behind a semicolon.
    //
    //  These cases live here rather than with the grammar because this file already owns a fixture with a
    //  real session over a real pooled transaction and the PRODUCTION task factory, which is what makes
    //  "no handle was issued" and "the statement was stored byte for byte" assertable rather than
    //  simulated.

    [Fact]
    public async Task CreateQueryTaskRefusesAnInadmissibleStatementAndIssuesNoHandle()
    {
        using Fixture fixture = new();

        CreateQueryTaskResponse refused = await fixture.Service.CreateQueryTask(
            new CreateQueryTaskRequest
            {
                Session = new SessionHandle { SessionId = fixture.Session },
                Spec = new QuerySpec { Sql = "SELECT 1; DELETE FROM COMPANY" },
            },
            Fixture.Context);

        // The code this contract already answers for a statement it will not run [:L612, :L616, :L620],
        // so no new value enters a consumer's branch set.
        Assert.Equal(WireRetCode.EInvalidSql, refused.Status.RetCode);
        Assert.Null(refused.Task);

        // BEFORE TASK ACCEPTANCE, which is the property that matters: a handle issued here would name a
        // task configured with a statement the caller's scope does not permit.
        Assert.Equal(0, fixture.Tasks.Count);

        // THE DIAGNOSTIC NAMES THE RULE AND NEVER THE REJECTED TEXT (constraint C-F). Echoing it would put
        // a caller-authored value - and on an already-bound statement an interpolated literal - into a
        // field documented as opaque display text.
        Assert.DoesNotContain("DELETE FROM", refused.Status.ErrorText, StringComparison.Ordinal);
        Assert.Contains("read scope", refused.Status.ErrorText, StringComparison.Ordinal);
        Assert.Contains("C-07", refused.Status.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateQueryTaskRefusesAnInadmissibleSyntaxWithItsOwnDiagnostic()
    {
        using Fixture fixture = new();

        CreateQueryTaskResponse refused = await fixture.Service.CreateQueryTask(
            new CreateQueryTaskRequest
            {
                Session = new SessionHandle { SessionId = fixture.Session },
                Spec = new QuerySpec { SqlSyntax = GridSyntax.From("DELETE FROM COMPANY", ["ID"]) },
            },
            Fixture.Context);

        Assert.Equal(WireRetCode.EInvalidSql, refused.Status.RetCode);
        Assert.Null(refused.Task);
        Assert.Equal(0, fixture.Tasks.Count);

        // A DISTINCT MESSAGE FROM THE STATEMENT ONE, because the caller sent a different field and the
        // offending text is nested inside it: telling it "the statement" was refused would send it looking
        // at the wrong request field.
        Assert.Contains("DataWindow syntax", refused.Status.ErrorText, StringComparison.Ordinal);
        Assert.DoesNotContain("DELETE FROM", refused.Status.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnOrdinaryReadStatementIsStillAcceptedAndStoredByteForByte()
    {
        // WITHOUT THIS CASE THE GUARD COULD BE A CONSTANT REFUSAL and both cases above would still pass.
        // It also pins that an ACCEPTED statement is stored unaltered: the guard REFUSES rather than
        // rewrites, and byte-exact statement parity is the acceptance criterion for this whole layer -
        // note the accepted statement contains a refused word as DATA inside a string literal.
        using Fixture fixture = new();

        const string Sql = "SELECT * FROM COMPANY WHERE NOTE = 'delete me'";

        CreateQueryTaskResponse created = await fixture.Service.CreateQueryTask(
            new CreateQueryTaskRequest
            {
                Session = new SessionHandle { SessionId = fixture.Session },
                Spec = new QuerySpec { Sql = Sql },
            },
            Fixture.Context);

        Assert.Equal(WireRetCode.Ok, created.Status.RetCode);
        Assert.NotNull(created.Task);
        Assert.Equal(1, fixture.Tasks.Count);

        Assert.True(fixture.Tasks.TryResolve(created.Task, out QueryTaskEntry? entry));
        Assert.Equal(Sql, entry!.Task.Sql);
    }

    [Fact]
    public async Task AnEmptyStatementIsStillAcceptedSoTheRunTimeCheckKeepsOwningIt()
    {
        // CONSTRAINT C-B, ASSERTED AT THE BOUNDARY. The oracle's setter is a plain assignment and the
        // emptiness check happens later, inside the task body, where an empty statement answers
        // E_INVALID_SQL with SQL为空! [:L615-L617]. The new scope guard must not move that rejection
        // forward to configuration time, and the published contract says so - so a caller sending an
        // explicitly empty statement must still get a handle.
        using Fixture fixture = new();

        CreateQueryTaskResponse created = await fixture.Service.CreateQueryTask(
            new CreateQueryTaskRequest
            {
                Session = new SessionHandle { SessionId = fixture.Session },
                Spec = new QuerySpec { Sql = string.Empty },
            },
            Fixture.Context);

        Assert.Equal(WireRetCode.Ok, created.Status.RetCode);
        Assert.NotNull(created.Task);
    }

    [Fact]
    public async Task TheGuardAlsoRunsOnTheRunItselfAndNotOnlyOnCreate()
    {
        // ApplySpec IS SHARED BY BOTH ENTRY POINTS, so a spec sent on Query must be gated exactly as one
        // sent on CreateQueryTask. Without this case the guard could be wired into create alone and a
        // caller would simply send its batch on the run instead.
        using Fixture fixture = new();
        TaskHandle handle = await fixture.CreateTaskAsync();

        CollectingStream stream = new();

        await fixture.Service.Query(
            new QueryRequest
            {
                Task = handle,
                Spec = new QuerySpec { Sql = "SELECT 1; DROP TABLE COMPANY" },
            },
            stream,
            Fixture.Context);

        OperationStatus status = Assert.Single(stream.Written).Status;

        Assert.Equal(WireRetCode.EInvalidSql, status.RetCode);
        Assert.Contains("read scope", status.ErrorText, StringComparison.Ordinal);
        Assert.DoesNotContain("DROP TABLE", status.ErrorText, StringComparison.Ordinal);

        // NO RETRIEVAL RAN. The refusal happens while the spec is being merged, before the runner is
        // reached at all, so the statement was never issued.
        Assert.Equal(0, fixture.Runner.Runs);
    }

    // ---------------------------------------------------------------------------------------------
    //  THE FIXTURE - THE PRODUCTION TASK FACTORY, A GATED RUNNER, AND NO DATABASE
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// A runner that answers success without retrieving, and that can be paused inside the run.
    /// </summary>
    /// <remarks>
    /// The one-method seam over <c>SqlQueryTask.ExecuteAsync</c>. Pausing here is what holds the entry's
    /// operation lease open across a second request, which is the only way the exclusion can be observed
    /// at all.
    /// </remarks>
    private sealed class GatedRunner : IQueryRetrievalRunner
    {
        /// <summary>Completes the first time a run is entered.</summary>
        internal TaskCompletionSource Entered { get; } = new();

        /// <summary>Set by a test to hold the run - and so the lease - open.</summary>
        internal TaskCompletionSource? Gate { get; set; }

        /// <summary>How many runs actually reached this runner.</summary>
        internal int Runs { get; private set; }

        /// <summary>The token the adapter handed down.</summary>
        internal CancellationToken TokenSeen { get; private set; }

        /// <summary>What the run answers. Success unless a test wants a failure path.</summary>
        internal long Result { get; set; } = RetCode.OK;

        /// <summary>
        /// Run once on entry, BEFORE the gate. This is where a test raises into the entry's fault
        /// recorder: the adapter clears that recorder immediately before calling the runner, so seeding
        /// it from outside would be wiped and seeding it from here is the only faithful moment.
        /// </summary>
        internal Action? OnEntered { get; set; }

        public async Task<long> RunAsync(
            SqlQueryTask task,
            IQueryResultSink sink,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(task);
            ArgumentNullException.ThrowIfNull(sink);

            Runs++;
            TokenSeen = cancellationToken;

            OnEntered?.Invoke();

            _ = Entered.TrySetResult();

            if (Gate is not null)
            {
                await Gate.Task.ConfigureAwait(false);
            }

            return Result;
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

    /// <summary>
    /// The subject, its handle table and its runner, over the production query-task factory.
    /// </summary>
    private sealed class Fixture : IDisposable
    {
        private readonly Host _host = new();
        private readonly TransactionPool _pool;
        private readonly PoolLease _lease;

        internal Fixture()
        {
            // The two tables now carry a handle ceiling and an idle window, so both take the bound
            // settings and the real clock. The shipped defaults are used deliberately: this suite is
            // about the LEASE a query holds, not about the quota, so the ceiling must not be the thing
            // a case trips over.
            IOptions<PersistenceOptions> handleOptions = Options.Create(new PersistenceOptions());

            // A session, taken from the real pool but NEVER CONNECTED: the pool hands out a transaction
            // without opening one, and nothing this suite drives issues a statement, so no database is
            // provisioned and none is needed (constraint C-E). RESOLVED FIRST because the session table
            // now takes the pool as a construction dependency - it hands a lease back on retirement, so
            // it cannot be built without the pool that issued the lease.
            _pool = _host.Services.GetRequiredService<TransactionPool>();

            Sessions = new TransactionSessionRegistry(handleOptions, TimeProvider.System, _pool);
            Tasks = new QueryTaskRegistry(handleOptions, TimeProvider.System);
            Runner = new GatedRunner();

            IQueryTaskFactory factory = _host.Services.GetRequiredService<IQueryTaskFactory>();

            TransactionData descriptor = new()
            {
                Dbms = "SQLite",
                ServerName = "server-1",
                Database = "lease-fixture.db",
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

            Service = new GrpcQueryService(Sessions, Tasks, factory, Runner);
        }

        /// <summary>The context every call is made with unless it is testing cancellation.</summary>
        internal static ServerCallContext Context { get; } = new CallContext(CancellationToken.None);

        internal TransactionSessionRegistry Sessions { get; }

        internal QueryTaskRegistry Tasks { get; }

        internal GatedRunner Runner { get; }

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

            protected override ContextPropagationToken CreatePropagationTokenCore(
                ContextPropagationOptions? options) => throw new NotSupportedException();

            protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders) =>
                Task.CompletedTask;
        }

        /// <summary>
        /// The host, booted only for its container. No request is ever sent over HTTP.
        /// </summary>
        private sealed class Host : WebApplicationFactory<Program>
        {
            protected override void ConfigureWebHost(IWebHostBuilder builder)
            {
                ArgumentNullException.ThrowIfNull(builder);

                builder.UseEnvironment(Environments.Production);

                // Supplied as host configuration so the service's own startup gate runs against them,
                // exactly as CompositionRootTests does. The issuer is a reserved name, so no metadata is
                // ever fetched.
                builder.UseSetting("Jwt:Authority", TrustedIssuer);
                builder.UseSetting("Jwt:Audience", "powerframework-persistence-query-lease");
                builder.UseSetting("Jwt:RequireHttpsMetadata", "true");

                // A directory of its own, so two hosts in one test run cannot contend. Nothing writes to
                // it: no connection is opened anywhere in this suite.
                builder.UseSetting(
                    "Sqlite:DataDirectory",
                    Path.Combine(Path.GetTempPath(), "pfw-query-lease-" + Guid.NewGuid().ToString("N")));
            }
        }
    }

}
