// ==================================================================================================
//  SqlUpdateTaskErrorLatchTests.cs - the per-execution error latch of the update worker
// ==================================================================================================
//
//  ONE SUBJECT, DELIBERATELY NARROW. This suite pins the substrate's error latch as it behaves inside
//  Tasks/SqlUpdateTask.cs - the field `_lastErrCode`
//  [ws_objects/pfw.thread.pbl.src/n_cst_thread_task.sru:L89, :L189], the reader `of_GetLastErrorCode()`
//  [:L597], the clear `of_ClearError()` [:L607-L611], and the de-duplication guard that reads it twice
//  in the update epilogue [n_cst_thread_task_sqlupdate.sru:L389, :L397].
//
//  WHY IT IS ITS OWN FILE. The latch is a property of the WORKER, and the only other suite that builds
//  a real SqlUpdateTask - SqlUpdateTaskProxyTests - states in its own header that no test there runs the
//  worker. Running it needs a transaction that can be acquired and a carrier that can be torn down, so
//  the harness below is genuinely different in kind and belongs beside the behaviour it exists for.
//
//  THE ORACLE'S ORDERING IS THE WHOLE POINT, AND IT IS THREE STATEMENTS LONG. `of_execute` runs its
//  running guard, then its skip guard, then `of_ClearError()`, and only then `OnStart`
//  [n_cst_thread_task.sru:L240-L246]. So the latch a de-duplication guard compares against can only
//  ever hold a code THIS execution raised. A port that latches and never clears silently redefines that
//  guard from "already reported on this run" to "reported at some point in this object's life", and the
//  longer a task is reused the more codes accumulate that can never be reported again. Every assertion
//  below is a different face of that one distinction.
//
//  NO DATABASE AND NO THREAD (constraints C-E, C-H). The transaction engine is a recording fake that
//  opens nothing, the carrier adapter answers a carrier whose update surface throws if anything reaches
//  it, and the clock is frozen. The one path every test drives - "neither a data object nor a SQL syntax
//  was set" [n_cst_thread_task_sqlupdate.sru:L327-L328] - is chosen precisely because it reaches the
//  epilogue WITHOUT touching a data store, a changeset or an update statement, so the latch can be
//  observed with nothing else in the way.
// ==================================================================================================

using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PowerFramework.Persistence.Concurrency;
using PowerFramework.Persistence.Configuration;
using PowerFramework.Persistence.Tasks;
using PowerFramework.Persistence.Transactions;

namespace PowerFramework.Persistence.Tests;

/// <summary>
/// Parity tests for the update worker's error latch and its per-execution clear.
/// </summary>
[SuppressMessage(
    "Reliability",
    "CA2000:Dispose objects before losing scope",
    Justification = "Every disposable the harness creates is owned by the harness and released by its own Dispose.")]
public sealed class SqlUpdateTaskErrorLatchTests
{
    // ---------------------------------------------------------------------------------------------
    //  1. THE CLEAR ITSELF
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void ClearErrorZeroesTheLatchAndAnswersOk()
    {
        // [n_cst_thread_task.sru:L607-L611] `_lastErrCode = 0 ; _lastErrInfo = "" ; return RetCode.OK`.
        // OK is the only value the oracle's function can answer, so the return is asserted rather than
        // discarded - a port that answered the previous code would be a different function.
        using Harness harness = new();

        Assert.Equal(RetCode.OK, harness.Worker.GetLastErrorCode());
        Assert.Equal(RetCode.OK, harness.Worker.ClearError());
        Assert.Equal(RetCode.OK, harness.Worker.GetLastErrorCode());

        // Idempotent, because the oracle's is: clearing a latch that is already clear is not an error.
        Assert.Equal(RetCode.OK, harness.Worker.ClearError());
        Assert.Equal(RetCode.OK, harness.Worker.GetLastErrorCode());
    }

    // ---------------------------------------------------------------------------------------------
    //  2. THE FINDING ITSELF - A STALE CODE MUST NOT SUPPRESS A LATER, INDEPENDENT FAILURE
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void AStaleCodeFromAnEarlierRunDoesNotSuppressALaterIndependentFailure()
    {
        // TWO RUNS, IDENTICAL, EACH FAILING THE SAME WAY. The epilogue's guard is
        // `if rtCode <> of_GetLastErrorCode() then Event OnError(...)` [:L397], so without the
        // per-execution clear the SECOND run finds its own code already latched by the FIRST and reports
        // nothing at all - the caller is told that a run which failed had no error. That is the whole
        // defect, and this is the assertion that fails if the clear is removed.
        using Harness harness = new();

        long first = harness.Worker.OnDoTask(TestContext.Current.CancellationToken);
        long second = harness.Worker.OnDoTask(TestContext.Current.CancellationToken);

        // [:L327-L328] neither source was set, on both runs.
        Assert.Equal(RetCode.E_INVALID_DATAOBJECT, first);
        Assert.Equal(RetCode.E_INVALID_DATAOBJECT, second);

        // BOTH runs reported. Two raises, same code, one per run.
        Assert.Equal(2, harness.Host.Errors.Count);
        Assert.All(
            harness.Host.Errors,
            recorded => Assert.Equal(RetCode.E_INVALID_DATAOBJECT, recorded.Code));

        // And each carries the arm's own diagnostic, which the run writes into the oracle's `sError`
        // local [:L327] and the epilogue reports [:L398].
        Assert.All(harness.Host.Errors, recorded => Assert.NotEqual(string.Empty, recorded.Text));
    }

    [Fact]
    public void ADifferentCodeOnTheSecondRunIsReportedWithNoDependenceOnTheFirst()
    {
        // The control for the test above. Two runs failing DIFFERENTLY are reported independently even
        // with a latch that never clears, because the codes differ - so this case alone would not detect
        // the defect. It is here to show that the matrix distinguishes the two situations rather than
        // passing for the same reason twice.
        using Harness harness = new();

        // Run 1 cannot even acquire a transaction, which raises E_INVALID_TRANSACTION [:L294].
        harness.Engine.ConnectSucceeds = false;
        Assert.Equal(
            RetCode.E_INVALID_TRANSACTION,
            harness.Worker.OnDoTask(TestContext.Current.CancellationToken));

        // Run 2 acquires one and then fails on the missing data object [:L327-L328].
        harness.Engine.ConnectSucceeds = true;
        Assert.Equal(
            RetCode.E_INVALID_DATAOBJECT,
            harness.Worker.OnDoTask(TestContext.Current.CancellationToken));

        Assert.Equal(
            [RetCode.E_INVALID_TRANSACTION, RetCode.E_INVALID_DATAOBJECT],
            harness.Host.Errors.Select(recorded => recorded.Code));
    }

    // ---------------------------------------------------------------------------------------------
    //  3. WHERE THE CLEAR SITS - AT THE HEAD, BEFORE THE ACQUISITION, AND NOT AT THE END
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void AnAcquisitionFailureStillLeavesItsOwnCodeLatched()
    {
        // THE ACQUISITION ARM IS ITSELF A RAISE SITE [:L293-L294], which is why the clear sits at the
        // head of the run rather than after step 1 - the oracle's own ordering
        // [n_cst_thread_task.sru:L240-L246], where the clear is the third statement of `of_execute` and
        // everything the body does comes after it.
        //
        // STATED HONESTLY: no test can distinguish those two placements, and this one does not claim to.
        // The acquisition arm raises UNCONDITIONALLY - it carries no de-duplication guard - so a clear
        // moved to just after it would produce the same observable outcome on every path. What IS
        // observable, and is what this asserts, is the consequence that makes the head placement safe:
        // the code the acquisition raised is latched and readable when the run ends, so a guard later in
        // the same run has the run's own code to compare against rather than a zero.
        using Harness harness = new();
        harness.Engine.ConnectSucceeds = false;

        Assert.Equal(
            RetCode.E_INVALID_TRANSACTION,
            harness.Worker.OnDoTask(TestContext.Current.CancellationToken));

        // The raise happened, and the latch holds what it raised.
        Assert.Equal(RetCode.E_INVALID_TRANSACTION, Assert.Single(harness.Host.Errors).Code);
        Assert.Equal(RetCode.E_INVALID_TRANSACTION, harness.Worker.GetLastErrorCode());
    }

    [Fact]
    public void TheLatchStillHoldsTheRunsOwnCodeWhenTheRunEnds()
    {
        // WHY THIS MATTERS RATHER THAN BEING AN IMPLEMENTATION DETAIL: the two epilogue guards
        // [:L389, :L397] read the latch AFTER the body has raised into it, so a clear moved to the END of
        // a run would leave those guards comparing against zero and within-run de-duplication would be
        // gone. The latch outliving its own run is therefore the observable form of "the clear is at the
        // head, not the tail".
        using Harness harness = new();

        Assert.Equal(

            RetCode.E_INVALID_DATAOBJECT,

            harness.Worker.OnDoTask(TestContext.Current.CancellationToken));
        Assert.Equal(RetCode.E_INVALID_DATAOBJECT, harness.Worker.GetLastErrorCode());
    }

    [Fact]
    public void AnExplicitClearBetweenRunsChangesNothingBecauseEachRunClearsItsOwn()
    {
        // Clearing by hand between two runs must be indistinguishable from not clearing, because the run
        // clears first thing anyway. A port whose clear was conditional - on the previous outcome, on a
        // reset, on anything - would fail this.
        using Harness harness = new();

        Assert.Equal(

            RetCode.E_INVALID_DATAOBJECT,

            harness.Worker.OnDoTask(TestContext.Current.CancellationToken));
        _ = harness.Worker.ClearError();
        Assert.Equal(
            RetCode.E_INVALID_DATAOBJECT,
            harness.Worker.OnDoTask(TestContext.Current.CancellationToken));

        Assert.Equal(2, harness.Host.Errors.Count);
    }

    [Fact]
    public void ASuccessfulRunLeavesTheLatchClear()
    {
        // The empty-update arm [:L338-L342]: `SetChanges` answering -1 with a zero row count is a
        // SUCCESS, so the epilogue takes its commit arm, nothing is raised, and the head-clear is the
        // only thing that touched the latch. Asserted because a latch that held a code after a clean run
        // would suppress the next run's identical failure - the same defect by another route.
        using Harness harness = new();
        harness.Worker.SetSqlSyntax("table(column=(type=long name=id))");
        harness.Adapter.CarrierCreateResult = DataWindowBufferStore.DataStoreSuccess;
        harness.Adapter.SetChangesResult = DataWindowBufferStore.DataStoreFailure;

        Assert.Equal(

            RetCode.OK,

            harness.Worker.OnDoTask(TestContext.Current.CancellationToken));
        Assert.Empty(harness.Host.Errors);
        Assert.Equal(RetCode.OK, harness.Worker.GetLastErrorCode());
    }

    // ---------------------------------------------------------------------------------------------
    //  THE HARNESS - A RECORDING ENGINE, A RECORDING HOST AND A CARRIER THAT REFUSES REAL WORK
    // ---------------------------------------------------------------------------------------------

    /// <summary>One recorded raise on the framework error channel.</summary>
    private sealed record RecordedError(long Code, string Text);

    /// <summary>
    /// A frozen clock. Both readings are overridden together, because the pool measures liveness and
    /// idle expiry as MONOTONIC elapsed time and a fake that answered only <c>GetUtcNow</c> would let
    /// those measurements escape onto the real <c>Stopwatch</c>.
    /// </summary>
    private sealed class FrozenClock : TimeProvider
    {
        internal static readonly FrozenClock Instance = new();

        private static readonly DateTimeOffset Fixed =
            new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Fixed;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => Fixed.UtcTicks;
    }

    /// <summary>
    /// The storage engine, recording and refusing. It opens no connection and composes no connection
    /// string, so the whole suite runs with no database of any kind (constraint C-E).
    /// </summary>
    private sealed class FakeEngine : ITransactionEngine
    {
        /// <summary>Whether <see cref="Connect"/> answers a success. Steered per test.</summary>
        internal bool ConnectSucceeds { get; set; } = true;

        internal int RollbackCalls { get; private set; }

        public int DbHandle { get; private set; }

        public string Dbms { get; private set; } = string.Empty;

        public bool AutoCommit { get; set; }

        public void ApplyConnectionFields(in TransactionData descriptor) => Dbms = descriptor.Dbms;

        public SqlState Connect(CancellationToken cancellationToken = default)
        {
            if (!ConnectSucceeds)
            {
                // A driver-shaped refusal. The handle stays zero, which is what the pooled transaction
                // reads back to answer its own liveness question.
                return SqlState.Failed(RetCode.SQLITE_CANTOPEN, "connect refused by the fake engine");
            }

            DbHandle = 1;
            return SqlState.Succeeded();
        }

        public SqlState Disconnect()
        {
            DbHandle = 0;
            return SqlState.Succeeded();
        }

        public SqlState Commit() => SqlState.Succeeded();

        public SqlState Rollback()
        {
            RollbackCalls++;
            return SqlState.Succeeded();
        }

        public SqlState Execute(string sqlCommand, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("No test in this suite issues a statement.");

        public SqlState Execute(in SqlCommandText command, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("No test in this suite issues a statement.");

        public void Dispose()
        {
        }
    }

    /// <summary>
    /// The threading substrate, recording every raise that reaches the framework error channel. That
    /// channel is what the epilogue's de-duplication guard decides to use or skip, so counting arrivals
    /// here is how suppression is measured.
    /// </summary>
    private sealed class RecordingHost : ISqlTaskHost
    {
        internal List<RecordedError> Errors { get; } = [];

        public bool IsMainThread => false;

        public bool IsCancelled => false;

        public int TaskIndex => 1;

        public ISqlTaskProxy? ParentTasking => null;

        public long GetTask(int index, out SqlTaskBase? task)
        {
            task = null;
            return RetCode.E_OUT_OF_BOUND;
        }

        public bool HasData(string name) => false;

        public object? GetData(string name) => null;

        public long SetData(string name, object? data) => RetCode.OK;

        public long OnPrepare() => RetCode.OK;

        public void OnUninit()
        {
        }

        public long OnError(long errCode, string errInfo)
        {
            Errors.Add(new RecordedError(errCode, errInfo));
            return RetCode.OK;
        }

        public long OnNotify(long notifyCode, long payload, string text) => RetCode.OK;
    }

    /// <summary>
    /// A data store that answers a real carrier and nothing else. The paths this suite drives call
    /// <c>OnInit</c> and read <c>Carrier</c>; every other member throws so an accidental widening of the
    /// exercised path is loud rather than quietly wrong.
    /// </summary>
    private sealed class FakeStore : ISqlDataStore
    {
        private readonly DataWindowCarrier _carrier =
            DataWindowCarrierFactory.Create(CarrierThreadAffinity.WorkerThread, FrozenClock.Instance);

        public DataWindowCarrier Carrier => _carrier;

        public string DataObject { get; set; } = string.Empty;

        public string GetSqlSelect() => string.Empty;

        public string Modify(string modificationScript) => string.Empty;

        public string Describe(string property) => "!";

        public long SetFilter(string? filter) => DataWindowBufferStore.DataStoreSuccess;

        public long SetSort(string? sort) => DataWindowBufferStore.DataStoreSuccess;

        public void ClearState() => _carrier.ClearState();

        public void OnInit(ICarrierParentTask parentTask) => _carrier.OnInit(parentTask);

        public ValueTask<long> RetrieveAsync(
            IReadOnlyList<object?> parameters,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("No test in this suite retrieves; C-05 owns retrieval.");

    }

    /// <summary>Hands out one store per call, as the production factory does.</summary>
    private sealed class FakeStoreFactory : ISqlDataStoreFactory
    {
        public ISqlDataStore Create(CarrierThreadAffinity affinity) => new FakeStore();
    }

    /// <summary>
    /// The update carrier. Only the members the exercised paths need answer; the update surfaces throw,
    /// because no test here executes an update statement and a silent stub would let one appear to.
    /// </summary>
    private sealed class FakeUpdateCarrier(
        ISqlDataStore store,
        long createResult,
        long setChangesResult) : ISqlUpdateCarrier
    {
        public ISqlDataStore Store => store;

        public IUpdateTarget Target =>
            throw new NotSupportedException("No test in this suite executes an update.");

        public IUpdateTargetMetadata TargetMetadata =>
            throw new NotSupportedException("No test in this suite prepares an update.");

        public IUpdateTargetModifier TargetModifier =>
            throw new NotSupportedException("No test in this suite prepares an update.");

        public IdentityTableSurfaces Identity =>
            throw new NotSupportedException("No test in this suite reads identity values.");

        public long Create(string sqlSyntax, out string errors)
        {
            errors = string.Empty;
            return createResult;
        }

        public long SetChanges(CarrierState? changes) => setChangesResult;

        public long SetTransObject(IPooledTransaction? transaction) =>
            DataWindowBufferStore.DataStoreSuccess;

        public ConcurrencyEvidence? CaptureConcurrencyEvidence() => null;

        /// <summary>Records the teardown's only observable effect [<c>:L378</c>].</summary>
        public void Dispose() => Disposals++;

        /// <summary>How many times the teardown disposed this carrier.</summary>
        internal int Disposals { get; private set; }
    }

    /// <summary>Adapts a store into the carrier above, with the two results a test may steer.</summary>
    private sealed class FakeCarrierAdapter : ISqlUpdateCarrierAdapter
    {
        /// <summary>What <c>data.Create(_sSQLSyntax, ref sError)</c> answers [<c>:L317</c>].</summary>
        internal long CarrierCreateResult { get; set; } = DataWindowBufferStore.DataStoreFailure;

        /// <summary>What <c>data.SetChanges(_blbUpdateData)</c> answers [<c>:L336</c>].</summary>
        internal long SetChangesResult { get; set; } = DataWindowBufferStore.DataStoreFailure;

        public ISqlUpdateCarrier Adapt(ISqlDataStore store) =>
            new FakeUpdateCarrier(store, CarrierCreateResult, SetChangesResult);
    }

    private sealed class Harness : IDisposable
    {
        internal Harness()
        {
            Engine = new FakeEngine();
            Host = new RecordingHost();
            Adapter = new FakeCarrierAdapter();

            IOptions<PersistenceOptions> accessor = Options.Create(new PersistenceOptions());

            Pool = new TransactionPool(
                accessor,
                FrozenClock.Instance,
                new PooledTransactionActivator(() => Engine, FrozenClock.Instance));

            Worker = new SqlUpdateTask(
                Host,
                Pool,
                new FakeStoreFactory(),
                new SqlRetrievalHookActivator(),
                Adapter,
                new ConflictDetector(SqlRedactor.Instance),
                FrozenClock.Instance,
                NullLogger<SqlUpdateTask>.Instance);
        }

        internal FakeEngine Engine { get; }

        internal RecordingHost Host { get; }

        internal FakeCarrierAdapter Adapter { get; }

        internal TransactionPool Pool { get; }

        internal SqlUpdateTask Worker { get; }

        public void Dispose()
        {
            Worker.Dispose();
            Pool.Dispose();
            Engine.Dispose();
        }
    }
}
