// ==================================================================================================
//  F-16 - THE DATA-OBJECT VALIDITY PROBE
//
//  WHY THIS FILE EXISTS. The oracle validates a named data object with ONE property read:
//
//      if data.Describe("DataWindow.Units") = "" then
//          Event OnError(RetCode.E_INVALID_ARGUMENT, "无效的DataObject")
//                                    [ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:
//                                     L553-L555]
//
//  That arm was ported faithfully and was nonetheless DEAD, because the production store answered the
//  describe FAILURE SENTINEL "!" for a store with no resolved definition rather than the empty string.
//  "!" is one character long, so the port's `.Length == 0` test was false for every unresolvable name:
//  the retrieval carried on, failed deep in the data path as RetCode.E_DB_ERROR, and the caller was told
//  the DATABASE had failed when they had simply named a DataWindow that does not exist.
//
//  THE TWO DESCRIBE FAILURES ARE DIFFERENT ANSWERS. "!" is PowerBuilder's answer for a property it does
//  not RECOGNISE; the empty string is its answer for a property it recognises and has nothing to report
//  for. That the oracle's arm exists at all - with its own diagnostic - is the proof that the legacy
//  answered EMPTY there, since otherwise the arm could never have fired in the legacy either.
//
//  WHAT THIS SUITE PINS, in three parts:
//    A. The production store's own answer - empty for each of the six RECOGNISED properties when no
//       definition resolved, and still "!" for one it does not recognise. Both halves, because a change
//       that answered empty for everything would break the sentinel contract the cache normalises.
//    B. The state the cache hands the probe. The retrieval and the update both read the store the cache
//       produced [n_cst_thread_task_sqlbase.sru:L538-L568], so that is where the emptiness has to be
//       observable rather than only on a store a test assigned by hand.
//    C. The update path's own boundary refusal, which is ADDED CODE - the oracle has no probe on that
//       side - carrying the oracle's own code and diagnostic. Documented at its point of reproduction in
//       Tasks/SqlUpdateTask.cs; asserted here.
//
//  NO DATABASE OF ANY KIND IS OPENED (constraint C-E). The store resolves definitions through an
//  injected runtime and the transaction engine is a fake that composes no connection string.
// ==================================================================================================

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PowerFramework.Persistence.Concurrency;
using PowerFramework.Persistence.Configuration;
using PowerFramework.Persistence.Tasks;
using PowerFramework.Persistence.Transactions;

namespace PowerFramework.Persistence.Tests;

/// <summary>
/// The <c>DataWindow.Units</c> validity probe, on both verbs.
/// </summary>
public sealed class DataObjectValidityProbeTests
{
    /// <summary>A name the runtime resolves.</summary>
    private const string ResolvedDataObject = "dw_sqlite";

    /// <summary>A name no runtime resolves, which is the whole subject.</summary>
    private const string UnresolvableDataObject = "dw_does_not_exist";

    /// <summary>The <c>DataWindow.Units</c> value the resolved definition carries.</summary>
    /// <remarks>
    /// <c>0</c> is PowerBuilder's own units value for pixels, and the fixture's own
    /// [<c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd</c>]. Any non-empty value would serve the test; the
    /// real one is used so the row reads as the fixture rather than as an arbitrary token.
    /// </remarks>
    private const string ResolvedUnits = "0";

    // ==============================================================================================
    //  PART A - THE PRODUCTION STORE'S OWN ANSWER
    // ==============================================================================================

    /// <summary>
    /// A store with no resolved definition answers EMPTY for every property the runtime recognises.
    /// </summary>
    /// <param name="property">The recognised property.</param>
    /// <remarks>
    /// <b><c>DataWindow.Units</c> IS THE ROW THE FINDING TURNS ON</b>, and the other five are here because
    /// the distinction being asserted is about RECOGNITION rather than about one property: a store that
    /// answered the sentinel for any recognised property would be making the same category error in a place
    /// a later probe might read.
    /// </remarks>
    [Theory]
    [InlineData("DataWindow.Units")]
    [InlineData("DataWindow.Processing")]
    [InlineData("DataWindow.Table.Arguments")]
    [InlineData("DataWindow.Table.Select")]
    [InlineData("DataWindow.Table.Sort")]
    [InlineData("DataWindow.Table.Filter")]
    public void AStoreWithNoDefinitionAnswersEmptyForARecognisedProperty(string property)
    {
        ISqlDataStore store = NewStore(out _);

        Assert.Equal(string.Empty, store.Describe(property));
    }

    /// <summary>
    /// A property the runtime does not recognise still answers the failure sentinel.
    /// </summary>
    /// <param name="property">The unrecognised property.</param>
    /// <remarks>
    /// <para>
    /// <b>THE OTHER HALF OF THE DISTINCTION, AND IT IS LOAD-BEARING.</b> Answering empty for everything
    /// would have made the probe fire correctly and destroyed the sentinel contract in the same change:
    /// <see cref="SqlTaskBase.NormalizeDescribeSentinel"/> exists precisely because a describe can answer
    /// <c>"!"</c> [<c>n_cst_thread_task_sqlbase.sru:L562, :L564</c>], and a store that never answered it
    /// would make that normalisation unreachable and its cache-hit comparison meaningless.
    /// </para>
    /// <para>
    /// The empty and whitespace rows are the argument guard, which answers the sentinel for a name that
    /// cannot be a property at all.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("DataWindow.Table")]
    [InlineData("DataWindow.Units.Extra")]
    [InlineData("datawindow.units")]
    [InlineData("Evaluate('1', 1)")]
    [InlineData("")]
    public void AnUnrecognisedPropertyStillAnswersTheFailureSentinel(string property)
    {
        ISqlDataStore store = NewStore(out _);

        Assert.Equal("!", store.Describe(property));
    }

    /// <summary>
    /// A store whose definition resolved answers the definition's own units, so a good name never trips
    /// the probe.
    /// </summary>
    /// <remarks>
    /// THE CONTROL FOR THE WHOLE SUITE. Without it, a store that answered empty unconditionally would pass
    /// every other row here while refusing every valid request in production.
    /// </remarks>
    [Fact]
    public void AResolvedDefinitionAnswersItsOwnUnitsAndDoesNotTripTheProbe()
    {
        ISqlDataStore store = NewStore(out ProbeRuntime runtime);
        runtime.Define(ResolvedDataObject);

        store.DataObject = ResolvedDataObject;

        Assert.Equal(ResolvedUnits, store.Describe("DataWindow.Units"));
        Assert.NotEqual(0, store.Describe("DataWindow.Units").Length);
    }

    /// <summary>
    /// Assigning an unresolvable name after a resolved one returns the store to the empty answer.
    /// </summary>
    /// <remarks>
    /// A CACHED STORE IS REUSED ACROSS RUNS, so the transition matters as much as the initial state: a
    /// store that kept the previous definition's units would let a second, invalid name pass the probe on
    /// the strength of the first name's answer.
    /// </remarks>
    [Fact]
    public void AnUnresolvableNameAfterAResolvedOneReturnsTheStoreToTheEmptyAnswer()
    {
        ISqlDataStore store = NewStore(out ProbeRuntime runtime);
        runtime.Define(ResolvedDataObject);

        store.DataObject = ResolvedDataObject;
        Assert.Equal(ResolvedUnits, store.Describe("DataWindow.Units"));

        store.DataObject = UnresolvableDataObject;

        Assert.Equal(string.Empty, store.Describe("DataWindow.Units"));
        Assert.Equal(UnresolvableDataObject, store.DataObject);
    }

    // ==============================================================================================
    //  PART B - THE STATE THE CACHE HANDS THE PROBE
    // ==============================================================================================

    /// <summary>
    /// The store the cache produces for an unresolvable name answers empty, which is what makes the
    /// retrieval's ported arm reachable.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>THE CACHE IS THE ONLY PATH EITHER VERB READS.</b> Both the retrieval and the update take their
    /// carrier from <c>GetCacheDataStore</c> when a data object is named
    /// [<c>n_cst_thread_task_sqlquery.sru:L303-L306</c>], and the cache ASSIGNS the name itself
    /// [<c>n_cst_thread_task_sqlbase.sru:L558</c>] - which is why the retrieval skips its own assignment on
    /// a cache hit. A store assigned by hand in a test therefore proves the store; only this proves the
    /// state the probe actually reads.
    /// </para>
    /// <para>
    /// The resolved half of the row is the control: the same cache, the same call, a name the runtime knows,
    /// and a non-empty answer.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheCachedStoreForAnUnresolvableNameAnswersEmpty()
    {
        using ProbeHarness harness = new();
        harness.Runtime.Define(ResolvedDataObject);

        using ProbeTask task = harness.NewProbeTask();

        ISqlDataStore resolved = task.CallGetCacheDataStore(ResolvedDataObject);
        Assert.Equal(ResolvedUnits, resolved.Describe("DataWindow.Units"));

        ISqlDataStore unresolvable = task.CallGetCacheDataStore(UnresolvableDataObject);
        Assert.Equal(string.Empty, unresolvable.Describe("DataWindow.Units"));
    }

    // ==============================================================================================
    //  PART C - THE UPDATE PATH'S BOUNDARY REFUSAL
    // ==============================================================================================

    /// <summary>
    /// An update naming a data object that resolves to nothing is refused with the oracle's own
    /// no-source code and diagnostic.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>THIS IS THE FINDING'S UPDATE HALF.</b> Before the refusal existed, the run proceeded past step 3
    /// with a store holding no definition, no statement and no expressions, and failed deep in the data
    /// path as <c>RetCode.E_DB_ERROR</c> - which the gateway correctly projects as HTTP 502, telling a
    /// caller that the database had failed for a mistake that was entirely theirs.
    /// </para>
    /// <para>
    /// <b>NOTHING IS INVENTED.</b> <c>E_INVALID_DATAOBJECT</c> and 无效的数据源对象! are the code and the
    /// text this same method already answers when NEITHER source was set
    /// [<c>n_cst_thread_task_sqlupdate.sru:L327-L328</c>], and an unresolvable name is the same fault as an
    /// unset one - the run has no data source. The refusal is a narrowing with a defined error rather than a
    /// widening with a guess (AAP 0.1.5).
    /// </para>
    /// <para>
    /// THE DIAGNOSTIC REACHES THE FRAMEWORK ERROR CHANNEL, not only the return value, because that is what
    /// the epilogue reports [<c>:L397-L398</c>] and what the caller-side proxy relays.
    /// </para>
    /// </remarks>
    [Fact]
    public void AnUpdateNamingAnUnresolvableDataObjectIsRefusedWithTheNoSourceCode()
    {
        using ProbeHarness harness = new();
        using SqlUpdateTask worker = harness.NewUpdateTask();

        Assert.Equal(RetCode.OK, worker.SetDataObject(UnresolvableDataObject));

        long result = worker.OnDoTask(TestContext.Current.CancellationToken);

        Assert.Equal(RetCode.E_INVALID_DATAOBJECT, result);

        (long code, string text) = Assert.Single(harness.Host.Errors);
        Assert.Equal(RetCode.E_INVALID_DATAOBJECT, code);
        Assert.Equal(SqlUpdateTask.InvalidDataObjectMessage, text);
    }

    /// <summary>
    /// The refusal is the probe's and not the empty-payload arm's: a resolvable name gets past step 3.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>THE CONTROL THAT MAKES THE ROW ABOVE MEAN SOMETHING.</b> A run with no changeset fails too, and
    /// for a different reason - so without this row the assertion above would pass just as well for a probe
    /// that refused every update. Here the name resolves, the probe is silent, and the run reaches the
    /// changeset arm and answers ITS code instead.
    /// </para>
    /// <para>
    /// The expected code is the carrier's own <c>SetChanges</c> refusal
    /// [<c>n_cst_thread_task_sqlupdate.sru:L336-L339</c>], which the fake carrier is steered to produce -
    /// so the assertion is "past step 3", stated as the next arm's code rather than as an absence.
    /// </para>
    /// </remarks>
    [Fact]
    public void AResolvableNameIsNotRefusedByTheProbeAndReachesTheNextArm()
    {
        using ProbeHarness harness = new();
        harness.Runtime.Define(ResolvedDataObject);

        using SqlUpdateTask worker = harness.NewUpdateTask();

        Assert.Equal(RetCode.OK, worker.SetDataObject(ResolvedDataObject));

        long result = worker.OnDoTask(TestContext.Current.CancellationToken);

        Assert.NotEqual(RetCode.E_INVALID_DATAOBJECT, result);
        Assert.DoesNotContain(
            harness.Host.Errors,
            recorded => recorded.Text == SqlUpdateTask.InvalidDataObjectMessage);
    }

    /// <summary>
    /// A syntax-driven update is untouched by the probe, which belongs to the named-source arm alone.
    /// </summary>
    /// <remarks>
    /// The three shaping arms are mutually exclusive and each source setter clears the other
    /// [<c>:L316-L331</c>]. A probe that had been placed outside the named arm would refuse every
    /// syntax-driven update, which is the second way this change could have gone wrong.
    /// </remarks>
    [Fact]
    public void ASyntaxDrivenUpdateIsUntouchedByTheProbe()
    {
        using ProbeHarness harness = new();
        harness.Carrier.CarrierCreateResult = DataWindowBufferStore.DataStoreSuccess;

        using SqlUpdateTask worker = harness.NewUpdateTask();

        Assert.Equal(RetCode.OK, worker.SetSqlSyntax("release 12.5;\r\ntable(column=(type=long))"));

        long result = worker.OnDoTask(TestContext.Current.CancellationToken);

        Assert.NotEqual(RetCode.E_INVALID_DATAOBJECT, result);
        Assert.DoesNotContain(
            harness.Host.Errors,
            recorded => recorded.Text == SqlUpdateTask.InvalidDataObjectMessage);
    }

    /// <summary>Builds a bare production store over a runtime that resolves nothing yet.</summary>
    /// <param name="runtime">The runtime, so a caller can define a name on it.</param>
    /// <returns>The store.</returns>
    private static ISqlDataStore NewStore(out ProbeRuntime runtime)
    {
        runtime = new ProbeRuntime();

        return new SqlDataStoreFactory(runtime, ProbeClock.Instance)
            .Create(CarrierThreadAffinity.WorkerThread);
    }

    /// <summary>A definition runtime that resolves only what a test defines on it.</summary>
    /// <remarks>
    /// It opens nothing and reads nothing from disk: a managed runtime cannot load a compiled
    /// <c>.srd</c>, which is why the production factory takes this seam at all.
    /// </remarks>
    private sealed class ProbeRuntime : IDataObjectRuntime
    {
        private readonly Dictionary<string, DataObjectDefinition> _definitions =
            new(StringComparer.Ordinal);

        /// <summary>Makes a name resolvable.</summary>
        /// <param name="dataObject">The name.</param>
        internal void Define(string dataObject) =>
            _definitions[dataObject] = new DataObjectDefinition(
                dataObject,
                "SELECT * FROM COMPANY",
                Sort: string.Empty,
                Filter: string.Empty,
                Processing: "1",
                Arguments: string.Empty,
                Units: ResolvedUnits);

        /// <inheritdoc/>
        public bool TryResolveDefinition(
            string dataObject,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out DataObjectDefinition? definition) =>
            _definitions.TryGetValue(dataObject, out definition);

        /// <inheritdoc/>
        public ValueTask<long> RetrieveAsync(
            ISqlDataStore data,
            IReadOnlyList<object?> parameters,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("No test in this suite retrieves; C-05 owns retrieval.");
    }

    /// <summary>A stopped clock, so nothing here depends on wall time.</summary>
    private sealed class ProbeClock : TimeProvider
    {
        /// <summary>The one instance.</summary>
        internal static readonly ProbeClock Instance = new();

        private static readonly DateTimeOffset Fixed = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        /// <inheritdoc/>
        public override DateTimeOffset GetUtcNow() => Fixed;

        /// <inheritdoc/>
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        /// <inheritdoc/>
        public override long GetTimestamp() => Fixed.UtcTicks;
    }

    /// <summary>A concrete task, only so the base's cache is reachable.</summary>
    /// <remarks>
    /// <see cref="SqlTaskBase"/> declares no abstract member - it is abstract as a class - so the subclass
    /// adds nothing but the one accessor. It exists because the cache is <c>protected</c>, and reaching it
    /// through a real verb would drag that verb's whole collaborator set in for no gain.
    /// </remarks>
    private sealed class ProbeTask(
        ISqlTaskHost host,
        TransactionPool transactionPool,
        ISqlDataStoreFactory dataStoreFactory,
        ISqlRetrievalHookActivator hookActivator,
        TimeProvider timeProvider)
        : SqlTaskBase(
            host,
            transactionPool,
            dataStoreFactory,
            hookActivator,
            timeProvider,
            NullLogger<ProbeTask>.Instance)
    {
        /// <summary>Reaches the base's cache.</summary>
        /// <param name="dataObject">The name to resolve.</param>
        /// <returns>The cached store.</returns>
        internal ISqlDataStore CallGetCacheDataStore(string dataObject) => GetCacheDataStore(dataObject);
    }

    /// <summary>The threading substrate, recording every raise on the framework error channel.</summary>
    private sealed class ProbeHost : ISqlTaskHost
    {
        /// <summary>What reached the error channel, in arrival order.</summary>
        internal List<(long Code, string Text)> Errors { get; } = [];

        /// <inheritdoc/>
        public bool IsMainThread => false;

        /// <inheritdoc/>
        public bool IsCancelled => false;

        /// <inheritdoc/>
        public int TaskIndex => 1;

        /// <inheritdoc/>
        public ISqlTaskProxy? ParentTasking => null;

        /// <inheritdoc/>
        public long GetTask(int index, out SqlTaskBase? task)
        {
            task = null;
            return RetCode.E_OUT_OF_BOUND;
        }

        /// <inheritdoc/>
        public bool HasData(string name) => false;

        /// <inheritdoc/>
        public object? GetData(string name) => null;

        /// <inheritdoc/>
        public long SetData(string name, object? data) => RetCode.OK;

        /// <inheritdoc/>
        public long OnPrepare() => RetCode.OK;

        /// <inheritdoc/>
        public void OnUninit()
        {
            // The substrate has nothing to release: it holds no thread and no synchronization primitive.
        }

        /// <inheritdoc/>
        public long OnError(long errCode, string errInfo)
        {
            Errors.Add((errCode, errInfo));
            return RetCode.OK;
        }

        /// <inheritdoc/>
        public long OnNotify(long notifyCode, long payload, string text) => RetCode.OK;
    }

    /// <summary>A storage engine that connects without opening anything (constraint C-E).</summary>
    private sealed class ProbeEngine : ITransactionEngine
    {
        /// <inheritdoc/>
        public int DbHandle { get; private set; }

        /// <inheritdoc/>
        public string Dbms { get; private set; } = string.Empty;

        /// <inheritdoc/>
        public bool AutoCommit { get; set; }

        /// <inheritdoc/>
        public void ApplyConnectionFields(in TransactionData descriptor) => Dbms = descriptor.Dbms;

        /// <inheritdoc/>
        public SqlState Connect(CancellationToken cancellationToken = default)
        {
            DbHandle = 1;
            return SqlState.Succeeded();
        }

        /// <inheritdoc/>
        public SqlState Disconnect()
        {
            DbHandle = 0;
            return SqlState.Succeeded();
        }

        /// <inheritdoc/>
        public SqlState Commit() => SqlState.Succeeded();

        /// <inheritdoc/>
        public SqlState Rollback() => SqlState.Succeeded();

        /// <inheritdoc/>
        public SqlState Execute(string sqlCommand, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("No test in this suite issues a statement.");

        /// <inheritdoc/>
        public SqlState Execute(in SqlCommandText command, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("No test in this suite issues a statement.");

        /// <inheritdoc/>
        public void Dispose()
        {
            // Nothing was opened, so nothing is closed. Declared because the interface requires it.
        }
    }

    /// <summary>
    /// A carrier that answers the store and the two results the exercised arms read, and throws on
    /// everything else.
    /// </summary>
    /// <param name="store">The store the task's cache produced - the object the probe reads.</param>
    /// <param name="createResult">What <c>Create</c> answers [<c>:L317</c>].</param>
    /// <param name="setChangesResult">What <c>SetChanges</c> answers [<c>:L336</c>].</param>
    /// <remarks>
    /// THE UPDATE SURFACES THROW RATHER THAN RETURNING A DEFAULT, so a change that carried a run past the
    /// arms this suite is about fails loudly instead of appearing to succeed.
    /// </remarks>
    private sealed class ProbeCarrier(
        ISqlDataStore store,
        long createResult,
        long setChangesResult) : ISqlUpdateCarrier
    {
        /// <inheritdoc/>
        public ISqlDataStore Store => store;

        /// <inheritdoc/>
        public IUpdateTarget Target =>
            throw new NotSupportedException("No test in this suite executes an update.");

        /// <inheritdoc/>
        public IUpdateTargetMetadata TargetMetadata =>
            throw new NotSupportedException("No test in this suite prepares an update.");

        /// <inheritdoc/>
        public IUpdateTargetModifier TargetModifier =>
            throw new NotSupportedException("No test in this suite prepares an update.");

        /// <inheritdoc/>
        public IdentityTableSurfaces Identity =>
            throw new NotSupportedException("No test in this suite reads identity values.");

        /// <inheritdoc/>
        public long Create(string sqlSyntax, out string errors)
        {
            errors = string.Empty;
            return createResult;
        }

        /// <inheritdoc/>
        public long SetChanges(CarrierState? changes) => setChangesResult;

        /// <inheritdoc/>
        public long SetTransObject(IPooledTransaction? transaction) =>
            DataWindowBufferStore.DataStoreSuccess;

        /// <inheritdoc/>
        public ConcurrencyEvidence? CaptureConcurrencyEvidence() => null;

        /// <inheritdoc/>
        public void Dispose()
        {
            // The carrier holds no unmanaged resource; the teardown's only observable effect is elsewhere.
        }
    }

    /// <summary>Wraps whatever store the task's cache produced into the carrier above.</summary>
    private sealed class ProbeCarrierAdapter : ISqlUpdateCarrierAdapter
    {
        /// <summary>What <c>data.Create(_sSQLSyntax, ref sError)</c> answers [<c>:L317</c>].</summary>
        internal long CarrierCreateResult { get; set; } = DataWindowBufferStore.DataStoreFailure;

        /// <summary>What <c>data.SetChanges(_blbUpdateData)</c> answers [<c>:L336</c>].</summary>
        internal long SetChangesResult { get; set; } = DataWindowBufferStore.DataStoreFailure;

        /// <inheritdoc/>
        public ISqlUpdateCarrier Adapt(ISqlDataStore store) =>
            new ProbeCarrier(store, CarrierCreateResult, SetChangesResult);
    }

    /// <summary>The collaborator set both verbs need, with no database anywhere in it.</summary>
    private sealed class ProbeHarness : IDisposable
    {
        private readonly List<IDisposable> _owned = [];

        /// <summary>Builds the environment.</summary>
        internal ProbeHarness()
        {
            Runtime = new ProbeRuntime();
            Host = new ProbeHost();
            Carrier = new ProbeCarrierAdapter();
            StoreFactory = new SqlDataStoreFactory(Runtime, ProbeClock.Instance);
            HookActivator = new SqlRetrievalHookActivator();

            Pool = new TransactionPool(
                Options.Create(new PersistenceOptions()),
                ProbeClock.Instance,
                new PooledTransactionActivator(() => new ProbeEngine(), ProbeClock.Instance));
        }

        /// <summary>The definition runtime, so a test can make a name resolvable.</summary>
        internal ProbeRuntime Runtime { get; }

        /// <summary>The substrate, read for the diagnostics that reached the error channel.</summary>
        internal ProbeHost Host { get; }

        /// <summary>The carrier adapter, steered for the arms past the probe.</summary>
        internal ProbeCarrierAdapter Carrier { get; }

        /// <summary>The production store factory - the object whose describe answer is the subject.</summary>
        internal SqlDataStoreFactory StoreFactory { get; }

        /// <summary>The retrieval-hook activator, which resolves nothing in this suite.</summary>
        internal SqlRetrievalHookActivator HookActivator { get; }

        /// <summary>The transaction pool, over the fake engine.</summary>
        internal TransactionPool Pool { get; }

        /// <summary>Builds a task whose only purpose is to reach the base's cache.</summary>
        /// <returns>The task.</returns>
        internal ProbeTask NewProbeTask()
        {
            ProbeTask task = new(Host, Pool, StoreFactory, HookActivator, ProbeClock.Instance);
            _owned.Add(task);

            return task;
        }

        /// <summary>Builds the real update worker over the production store factory.</summary>
        /// <returns>The worker.</returns>
        internal SqlUpdateTask NewUpdateTask()
        {
            SqlUpdateTask worker = new(
                Host,
                Pool,
                StoreFactory,
                HookActivator,
                Carrier,
                new ConflictDetector(SqlRedactor.Instance),
                ProbeClock.Instance,
                NullLogger<SqlUpdateTask>.Instance);

            _owned.Add(worker);

            return worker;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            foreach (IDisposable owned in _owned)
            {
                owned.Dispose();
            }

            Pool.Dispose();
        }
    }
}
