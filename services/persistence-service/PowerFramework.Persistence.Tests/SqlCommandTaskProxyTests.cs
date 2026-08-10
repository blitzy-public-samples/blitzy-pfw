// ==============================================================================================
//  SqlCommandTaskProxyTests - the parity suite that pins the caller-side COMMAND proxy
//  --------------------------------------------------------------------------------------------
//  SYSTEM UNDER TEST
//      services/persistence-service/PowerFramework.Persistence/Tasks/TaskProxies/SqlCommandTaskProxy.cs
//  BEHAVIOURAL ORACLE
//      ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlcommand.sru   (58 lines)
//      ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlcommand.sru      (116 lines)
//      ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlbase.sru      (222 lines)
//      ws_objects/pfw.thread.pbl.src/n_cst_threading_task.sru
//      ws_objects/pfw.thread.pbl.src/n_cst_threading.sru
//      all READ ONLY per constraint C-C - read as specification, never edited.
//      Locators written bare as [:Lnnn] are into n_cst_threading_task_sqlcommand.sru; every other
//      file is named in full at the point of citation.
//
//  WHAT THIS SUITE IS ACTUALLY PROTECTING
//  --------------------------------------------------------------------------------------------
//  The proxy is 64 lines of code and every one of them looks removable. Four behaviours would be
//  "tidied" by a maintainer reading it without the oracle beside them, so each is PINNED here by a
//  test whose NAME says the behaviour is deliberate:
//
//    1  THE DEBUG/RELEASE SPLIT. The statement setter's assertion sits inside the oracle's own
//       `#IF DEFINED DEBUG` block [:L36-L38], so an empty statement raises AssertionFailure in a
//       Debug build and PASSES the proxy in a Release build, where the WORKER rejects it with the
//       return code E_INVALID_SQL [n_cst_thread_task_sqlcommand.sru:L45]. Promoting the assertion
//       to a runtime guard would replace a documented return code with an exception; deleting it
//       would drop a diagnostic the oracle ships. Both arms are asserted below, each compiled into
//       the configuration it belongs to, so THIS FILE PROVES THE SPLIT IN BOTH BUILDS.
//
//    2  THE UNVALIDATED AUTO-COMMIT SETTER. Three lines after the assertion, a THREE-VALUED
//       enumeration is accepted with no range check, no membership test and no diagnostic
//       [:L43-L46]. The out-of-range theory below is a pinning test, not a curiosity: it exists so
//       that adding the "obvious" argument check breaks a build.
//
//    3  THE BUSY GUARD IS REFUSAL, NOT VALIDATION. It comes FIRST on both setters [:L34, :L43], so
//       a busy proxy handed an empty statement answers E_BUSY and NOT E_INVALID_SQL. Reordering the
//       guard below the assertion would change that answer in Debug builds only.
//
//    4  THE PROXY/WORKER PAIR IS NOT FUSIBLE. The inherited two-sided reset
//       [n_cst_threading_task_sqlbase.sru:L53-L68] abandons on the worker's refusal and returns the
//       worker's code VERBATIM, leaving caller-side state untouched [:L62 before :L64]. That
//       failure mode needs two state sets, so the test for it is the executable proof that a single
//       fused async method could not express this pair. It is reachable ONLY through a substituted
//       worker, because SqlTaskBase.Reset always answers OK.
//
//  NO DATABASE, NO REAL THREAD, NO NETWORK AND NO DataWindow RUNTIME IS INVOLVED ANYWHERE IN THIS
//  FILE (constraint C-H). Every collaborator is a hand-written double declared at the bottom: the
//  threading substrate, the worker's own host, the pooled-transaction activator and the result-carrier
//  factory. The clock is a fixed TimeProvider that nothing advances, which is exactly the point - the
//  subject reads no clock, and a test that needed one would prove otherwise.
//
//  EVERY VALUE HERE IS SYNTHETIC (constraint C-F). No password, account, host or connection string is
//  copied from the legacy tree or from any catalogued in-source secret site, and no statement below
//  reaches a provider.
// ==============================================================================================

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using PowerFramework.Persistence.Configuration;
using PowerFramework.Persistence.Tasks;
using PowerFramework.Persistence.Tasks.TaskProxies;
using PowerFramework.Persistence.Transactions;
using PowerFramework.Shared.Diagnostics;

namespace PowerFramework.Persistence.Tests;

/// <summary>
/// The parity suite for <c>Tasks/TaskProxies/SqlCommandTaskProxy.cs</c>.
/// </summary>
public sealed class SqlCommandTaskProxyTests
{
    /// <summary>A synthetic statement. Invented here; not copied from the legacy tree.</summary>
    private const string SomeStatement = "UPDATE COMPANY SET AGE = 41 WHERE ID = 1";

    #region Identity - the two spellings of one marker [:L9, :L17] and the worker name [:L56]

    /// <summary>
    /// The marker is exactly <c>"sqlcommand"</c> - the value the oracle spells twice, as
    /// <c>string #type</c> [<c>:L9</c>] and as <c>constant string TASK_TYPE</c> [<c>:L17</c>].
    /// </summary>
    [Fact]
    public void TaskTypeMarkerIsExactlySqlcommand()
    {
        Assert.Equal("sqlcommand", SqlCommandTaskProxy.TaskTypeMarker);
    }

    /// <summary>
    /// The worker name is exactly the legacy class-name string [<c>:L56</c>]. It is a KEY, not a
    /// description: the substrate resolves a worker by it
    /// [<c>ws_objects/pfw.thread.pbl.src/n_cst_threading_task.sru:L188</c>] and it appears in
    /// characterization recordings, so a "tidied" .NET-style name would resolve nothing AND invalidate
    /// stored comparisons.
    /// </summary>
    [Fact]
    public void WorkerTaskClassNameIsExactlyTheLegacySpelling()
    {
        Assert.Equal("n_cst_thread_task_sqlcommand", SqlCommandTaskProxy.LegacyWorkerClassName);
    }

    /// <summary>
    /// Initialization hands that exact string to the substrate - the behavioural half of the
    /// assertion above, covering the wiring rather than the constant.
    /// </summary>
    [Fact]
    public void InitializeHandsTheLegacyWorkerClassNameToTheSubstrate()
    {
        using Harness harness = new();

        Assert.Equal(RetCode.OK, harness.Proxy.Initialize());
        Assert.Equal("n_cst_thread_task_sqlcommand", harness.Host.RequestedWorkerClassName);
    }

    /// <summary>
    /// A PREVENT answer from the substrate ABORTS attachment, because the oracle tests its ancestor's
    /// result with a LITERAL inequality against OK
    /// [<c>n_cst_threading_task_sqlbase.sru:L215</c>] and NOT with <c>IsSucceeded</c>, which would
    /// call PREVENT a success. Pinned through this type because the coupling - no worker attached, so
    /// every later call fails fast - is what a caller actually observes.
    /// </summary>
    [Fact]
    public void InitializeAbortsOnPreventWithoutAttachingAWorker()
    {
        using Harness harness = new();
        harness.Host.OnInitResult = RetCode.PREVENT;

        Assert.Equal(RetCode.PREVENT, harness.Proxy.Initialize());
        Assert.Throws<InvalidOperationException>(() => harness.Proxy.SetSql(SomeStatement));
    }

    #endregion

    #region The busy guard, FIRST on both setters [:L34, :L43]

    /// <summary>
    /// The statement setter is refused with <see cref="RetCode.E_BUSY"/> while busy, and the worker is
    /// never touched [<c>:L34</c> returns before <c>:L40</c>].
    /// </summary>
    [Fact]
    public void SetSqlIsRefusedWithEBusyWhileBusyAndInstallsNothing()
    {
        using Harness harness = new();
        Assert.Equal(RetCode.OK, harness.Proxy.Initialize());
        harness.Host.IsControllerBusy = true;

        Assert.Equal(RetCode.E_BUSY, harness.Proxy.SetSql(SomeStatement));
        Assert.Equal(string.Empty, harness.Worker.Sql);
    }

    /// <summary>
    /// The auto-commit setter is refused the same way [<c>:L43</c> returns before <c>:L45</c>], and the
    /// worker keeps the legacy default <c>AC_OFF</c>
    /// [<c>n_cst_thread_task_sqlcommand.sru:L21</c>].
    /// </summary>
    [Fact]
    public void SetAutoCommitIsRefusedWithEBusyWhileBusyAndInstallsNothing()
    {
        using Harness harness = new();
        Assert.Equal(RetCode.OK, harness.Proxy.Initialize());
        harness.Host.IsControllerBusy = true;

        Assert.Equal(RetCode.E_BUSY, harness.Proxy.SetAutoCommit(AutoCommitMode.AcNative));
        Assert.Equal(AutoCommitMode.AcOff, harness.Worker.AutoCommitSetting);
    }

    /// <summary>
    /// PINNED ORDERING (protecting behaviour 3 in the banner): a busy proxy handed the EMPTY statement
    /// answers <see cref="RetCode.E_BUSY"/> and NOT <see cref="RetCode.E_INVALID_SQL"/>, in both
    /// builds, because the guard precedes both the assertion and the delegation.
    /// </summary>
    [Fact]
    public void TheBusyGuardIsRefusalNotValidation_EmptyStatementWhileBusyAnswersEBusy()
    {
        using Harness harness = new();
        Assert.Equal(RetCode.OK, harness.Proxy.Initialize());
        harness.Host.IsControllerBusy = true;

        Assert.Equal(RetCode.E_BUSY, harness.Proxy.SetSql(string.Empty));
    }

    /// <summary>
    /// The busy predicate is a COMPOSITION, not a flag
    /// [<c>ws_objects/pfw.thread.pbl.src/n_cst_threading_task.sru:L305-L307</c>]: while the task is
    /// RUNNING the answer comes from the sync signal, INVERTED - busy while the signal is CLEAR - which
    /// makes a mutator legal from inside a framework callback and refused from outside one. Pinned
    /// through this type because both setters depend on it.
    /// </summary>
    [Theory]
    [InlineData(true, RetCode.OK)]
    [InlineData(false, RetCode.E_BUSY)]
    public void WhileRunningTheGuardAnswersFromTheSyncSignalAndIsInverted(
        bool syncSignalSet,
        long expected)
    {
        using Harness harness = new();
        Assert.Equal(RetCode.OK, harness.Proxy.Initialize());

        // The controller's own busy state is irrelevant once the task is running [:L305].
        harness.Host.IsControllerBusy = true;
        harness.Host.IsRunning = true;
        harness.Host.IsSyncSignalSet = syncSignalSet;

        Assert.Equal(expected, harness.Proxy.SetSql(SomeStatement));
    }

    #endregion

    #region Delegation is VERBATIM in both directions [:L40, :L45]

    /// <summary>
    /// The caller's statement reaches the worker completely unchanged - not trimmed, not normalized,
    /// not rewritten - and the worker's code comes back verbatim [<c>:L40</c>].
    /// </summary>
    /// <param name="statement">The statement to forward.</param>
    [Theory]
    [InlineData("SELECT * FROM COMPANY")]
    [InlineData("   leading and trailing spaces are NOT trimmed   ")]
    [InlineData("@dw_sqlite")]
    [InlineData("INSERT INTO COMPANY (NAME) VALUES (?)")]
    [InlineData("DELETE FROM COMPANY;DELETE FROM COMPANY")]
    public void SetSqlForwardsTheCallerValueUnchangedAndReturnsTheWorkerCode(string statement)
    {
        using Harness harness = new();
        Assert.Equal(RetCode.OK, harness.Proxy.Initialize());

        Assert.Equal(RetCode.OK, harness.Proxy.SetSql(statement));
        Assert.Equal(statement, harness.Worker.Sql);
    }

    /// <summary>
    /// PINNED SPLIT (protecting behaviour 1 in the banner). In a Release build the empty statement
    /// PASSES the proxy untouched and the WORKER answers <see cref="RetCode.E_INVALID_SQL"/>
    /// [<c>n_cst_thread_task_sqlcommand.sru:L45</c>] - a return code, not a throw. In a Debug build the
    /// oracle's own <c>#IF DEFINED DEBUG</c> assertion [<c>:L36-L38</c>] trips first with the legacy
    /// message text and the worker is never reached.
    /// </summary>
    [Fact]
    public void AnEmptyStatementBehavesDifferentlyInTheTwoBuilds()
    {
        using Harness harness = new();
        Assert.Equal(RetCode.OK, harness.Proxy.Initialize());

#if DEBUG
        AssertionFailure failure =
            Assert.Throws<AssertionFailure>(() => harness.Proxy.SetSql(string.Empty));
        Assert.Contains("Len(sql) <= 0", failure.Message, StringComparison.Ordinal);
        Assert.Equal(string.Empty, harness.Worker.Sql);
#else
        Assert.Equal(RetCode.E_INVALID_SQL, harness.Proxy.SetSql(string.Empty));
        Assert.Equal(string.Empty, harness.Worker.Sql);
#endif
    }

    /// <summary>
    /// The same split for <see langword="null"/>, which is the subtler half. In PowerScript
    /// <c>Len(NULL)</c> is NULL and a NULL condition takes the FALSE branch, so a null statement FAILS
    /// the Debug assertion exactly as the empty string does; while in Release it reaches the worker,
    /// whose guard is an equality against the EMPTY STRING alone and therefore lets null through
    /// [<c>n_cst_thread_task_sqlcommand.sru:L45-L49</c>].
    /// </summary>
    [Fact]
    public void ANullStatementBehavesDifferentlyInTheTwoBuilds()
    {
        using Harness harness = new();
        Assert.Equal(RetCode.OK, harness.Proxy.Initialize());

#if DEBUG
        AssertionFailure failure = Assert.Throws<AssertionFailure>(() => harness.Proxy.SetSql(null));
        Assert.Contains("Len(sql) <= 0", failure.Message, StringComparison.Ordinal);
#else
        Assert.Equal(RetCode.OK, harness.Proxy.SetSql(null));
        Assert.Null(harness.Worker.Sql);
#endif
    }

    #endregion

    #region The auto-commit triple - values, and the PRESERVED absence of validation

    /// <summary>
    /// The generated enum carries the oracle's own numbers
    /// [<c>n_cst_thread_task_sqlcommand.sru:L16-L18</c>], published once at
    /// [<c>shared/PowerFramework.Contracts/Proto/persistence.v1.proto:L1720-L1727</c>]. Only the
    /// declaration site moved; the values did not.
    /// </summary>
    [Fact]
    public void TheGeneratedEnumCarriesTheLegacyNumericValues()
    {
        Assert.Equal(0, (int)AutoCommitMode.AcOff);
        Assert.Equal(1, (int)AutoCommitMode.AcOn);
        Assert.Equal(2, (int)AutoCommitMode.AcNative);
    }

    /// <summary>
    /// All three declared modes are accepted and reach the worker unchanged [<c>:L45</c>].
    /// </summary>
    /// <param name="mode">The mode to install.</param>
    [Theory]
    [InlineData(AutoCommitMode.AcOff)]
    [InlineData(AutoCommitMode.AcOn)]
    [InlineData(AutoCommitMode.AcNative)]
    public void SetAutoCommitAcceptsAllThreeDeclaredModes(AutoCommitMode mode)
    {
        using Harness harness = new();
        Assert.Equal(RetCode.OK, harness.Proxy.Initialize());

        Assert.Equal(RetCode.OK, harness.Proxy.SetAutoCommit(mode));
        Assert.Equal(mode, harness.Worker.AutoCommitSetting);
    }

    /// <summary>
    /// PINNED GAP (protecting behaviour 2 in the banner). The oracle's body is a busy guard and a
    /// delegation, nothing else [<c>:L43-L46</c>], so an out-of-range value is stored happily by BOTH
    /// halves of the pair and then silently behaves as <c>AC_OFF</c> - the worker's success epilogue is
    /// an <c>if</c>/<c>elseif</c> with no <c>else</c> [<c>n_cst_thread_task_sqlcommand.sru:L94-L103</c>]
    /// and its failure epilogue rolls back [<c>:L107-L109</c>]. A proto3 enum is an OPEN
    /// <see cref="int"/> in C#, so the wire cannot be relied on to filter either. <b>Adding the obvious
    /// argument check must break this test.</b>
    /// </summary>
    /// <param name="raw">A numeric value outside the three declared enumerators.</param>
    [Theory]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    public void SetAutoCommitValidatesNothing_APinningTestForThePreservedGap(int raw)
    {
        using Harness harness = new();
        Assert.Equal(RetCode.OK, harness.Proxy.Initialize());

        Assert.Equal(RetCode.OK, harness.Proxy.SetAutoCommit((AutoCommitMode)raw));
        Assert.Equal((AutoCommitMode)raw, harness.Worker.AutoCommitSetting);
    }

    #endregion

    #region The INHERITED two-sided reset, exercised THROUGH this type

    /// <summary>
    /// The reset delegates to the worker and then clears the caller side, in that order
    /// [<c>n_cst_threading_task_sqlbase.sru:L61</c> then <c>:L64-L65</c>], and answers
    /// <see cref="RetCode.OK"/> rather than the worker's code [<c>:L67</c>].
    /// </summary>
    [Fact]
    public void TheInheritedResetClearsBothSidesAndAnswersOk()
    {
        using Harness harness = new();
        Assert.Equal(RetCode.OK, harness.Proxy.Initialize());
        Assert.Equal(RetCode.OK, harness.Proxy.SetSql(SomeStatement));
        Assert.Equal(RetCode.OK, harness.Proxy.SetAutoCommit(AutoCommitMode.AcNative));
        Assert.Equal(RetCode.OK, harness.Proxy.AddParam(41L));
        Assert.True(harness.Proxy.HasParams());

        Assert.Equal(RetCode.OK, harness.Proxy.Reset());

        // Worker side [n_cst_thread_task_sqlcommand.sru:L34-L35] - the statement clears to the EMPTY
        // STRING, not to null, and the mode returns to the legacy default.
        Assert.Equal(string.Empty, harness.Worker.Sql);
        Assert.Equal(AutoCommitMode.AcOff, harness.Worker.AutoCommitSetting);

        // Caller side [:L64].
        Assert.False(harness.Proxy.HasParams());
    }

    /// <summary>
    /// The reset guards on its OWN busy state first
    /// [<c>n_cst_threading_task_sqlbase.sru:L57</c>], so a refused reset touches neither side.
    /// </summary>
    [Fact]
    public void TheInheritedResetIsRefusedWithEBusyWhileBusy()
    {
        using Harness harness = new();
        Assert.Equal(RetCode.OK, harness.Proxy.Initialize());
        Assert.Equal(RetCode.OK, harness.Proxy.AddParam(41L));
        harness.Host.IsControllerBusy = true;

        Assert.Equal(RetCode.E_BUSY, harness.Proxy.Reset());
        Assert.True(harness.Proxy.HasParams());
    }

    /// <summary>
    /// PINNED NON-FUSIBILITY (protecting behaviour 4 in the banner). On the worker's refusal the proxy
    /// returns the WORKER's code verbatim and ABANDONS before clearing its own state
    /// [<c>n_cst_threading_task_sqlbase.sru:L62</c> precedes <c>:L64</c>]. Reachable only through a
    /// substituted worker, because <c>SqlTaskBase.Reset</c> always answers OK - which is itself the
    /// reason the worker has to be a seam.
    /// </summary>
    [Fact]
    public void TheWorkerRefusalPathLeavesCallerSideStateUntouched()
    {
        using Harness harness = new(substituteRefusingWorker: true);
        Assert.Equal(RetCode.OK, harness.Proxy.Initialize());
        Assert.Equal(RetCode.OK, harness.Proxy.AddParam("synthetic-parameter"));
        Assert.True(harness.Proxy.HasParams());

        Assert.Equal(RetCode.E_BUSY, harness.Proxy.Reset());

        // A single fused async method has ONE state set and therefore could not have this failure mode.
        Assert.True(harness.Proxy.HasParams());
    }

    /// <summary>
    /// PINNED TRI-STATE BOUNDARY. The abandon test is
    /// <c>IsFailed(rtCode)</c> [<c>n_cst_threading_task_sqlbase.sru:L62</c>], NOT a hand-rolled
    /// <c>&lt; 0</c>, and that predicate EXCLUDES <see cref="RetCode.CANCELLED"/> explicitly
    /// [<c>ws_objects/pfw.shared.pbl.src/isfailed.srf:L11-L13</c>] while
    /// <see cref="RetCode.PREVENT"/> is a positive code it never matched. So a worker answering either
    /// one does NOT take the abandon arm: the caller-side clear still happens, and the method still
    /// answers <see cref="RetCode.OK"/> rather than the worker's code [<c>:L67</c>]. Replacing the
    /// predicate with a sign test would break the CANCELLED case only - which is exactly the kind of
    /// silent regression this suite exists to catch.
    /// </summary>
    /// <param name="workerAnswer">
    /// A code that is not a failure by the return-code algebra's own definition.
    /// </param>
    [Theory]
    [InlineData(RetCode.CANCELLED)]
    [InlineData(RetCode.PREVENT)]
    public void ANonFailingWorkerAnswerStillClearsTheCallerSideAndStillReportsOk(long workerAnswer)
    {
        using Harness harness = new(substituteRefusingWorker: true, resetAnswer: workerAnswer);
        Assert.Equal(RetCode.OK, harness.Proxy.Initialize());
        Assert.Equal(RetCode.OK, harness.Proxy.AddParam("synthetic-parameter"));
        Assert.True(harness.Proxy.HasParams());

        Assert.Equal(RetCode.OK, harness.Proxy.Reset());
        Assert.False(harness.Proxy.HasParams());
    }

    #endregion

    #region Structural faults fail fast - the port of the oracle's untested `return _Task` [:L31]

    /// <summary>
    /// Both setters fail fast when no worker is attached. The oracle's accessor tests nothing and its
    /// callers dereference immediately [<c>:L31</c>, <c>:L40</c>, <c>:L45</c>], which in PowerBuilder
    /// is a runtime error the framework turns into termination
    /// [<c>ws_objects/pfw.pbl.src/pfw.sra:L111-L144</c>] - so an exception is the faithful expression
    /// and no legacy return code is converted into a throw.
    /// </summary>
    [Fact]
    public void BothSettersThrowWhenNoWorkerIsAttached()
    {
        using Harness harness = new();

        Assert.Throws<InvalidOperationException>(() => harness.Proxy.SetSql(SomeStatement));
        Assert.Throws<InvalidOperationException>(
            () => harness.Proxy.SetAutoCommit(AutoCommitMode.AcOn));
    }

    /// <summary>
    /// A worker of the WRONG task type is a DIFFERENT fault from an absent one and gets its own
    /// diagnostic, which names both the marker and the class the substrate should have inserted.
    /// </summary>
    [Fact]
    public void BothSettersThrowWhenTheAttachedWorkerIsOfAnotherTaskType()
    {
        using Harness harness = new(substituteRefusingWorker: true);
        Assert.Equal(RetCode.OK, harness.Proxy.Initialize());

        foreach (Action call in new Action[]
        {
            () => harness.Proxy.SetSql(SomeStatement),
            () => harness.Proxy.SetAutoCommit(AutoCommitMode.AcOn),
        })
        {
            InvalidOperationException failure = Assert.Throws<InvalidOperationException>(call);
            Assert.Contains("sqlcommand", failure.Message, StringComparison.Ordinal);
            Assert.Contains(
                "n_cst_thread_task_sqlcommand",
                failure.Message,
                StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The constructor fails fast on every null collaborator. Fail-fast on a structural fault is the
    /// legacy posture and is never softened into a warning-and-continue.
    /// </summary>
    [Fact]
    public void TheConstructorFailsFastOnEveryNullCollaborator()
    {
        FakeProxyHost host = new();

        Assert.Throws<ArgumentNullException>(
            () => new SqlCommandTaskProxy(
                null!,
                NullLogger<SqlCommandTaskProxy>.Instance,
                FixedClock.Instance));
        Assert.Throws<ArgumentNullException>(
            () => new SqlCommandTaskProxy(host, null!, FixedClock.Instance));
        Assert.Throws<ArgumentNullException>(
            () => new SqlCommandTaskProxy(
                host,
                NullLogger<SqlCommandTaskProxy>.Instance,
                null!));
    }

    /// <summary>
    /// The proxy and the worker are DISTINCT types, neither assignable to the other - the executable
    /// statement of constraint C-J, so that a composition root's author registers BOTH halves of the
    /// pair rather than assuming one substitutes for the other.
    /// </summary>
    [Fact]
    public void TheProxyAndTheWorkerAreDistinctTypes()
    {
        using Harness harness = new();

        Assert.NotSame((object)harness.Proxy, harness.Worker);
        Assert.False(typeof(SqlCommandTask).IsAssignableFrom(typeof(SqlCommandTaskProxy)));
        Assert.False(typeof(SqlCommandTaskProxy).IsAssignableFrom(typeof(SqlCommandTask)));
    }

    #endregion

    #region Doubles - no database, no real thread, no network

    /// <summary>
    /// Wires a proxy, a real command worker, and optionally a substituted worker of another task type
    /// whose reset refuses.
    /// </summary>
    private sealed class Harness : IDisposable
    {
        internal Harness(bool substituteRefusingWorker = false, long resetAnswer = RetCode.E_BUSY)
        {
            Pool = new TransactionPool(
                Options.Create(new PersistenceOptions()),
                FixedClock.Instance,
                new UnreachableTransactionActivator());

            Worker = new SqlCommandTask(
                new FakeTaskHost(),
                Pool,
                new UnreachableDataStoreFactory(),
                new SqlRetrievalHookActivator(),
                FixedClock.Instance,
                NullLogger<SqlCommandTask>.Instance);

            RefusingWorker = substituteRefusingWorker
                ? new RefusingSqlTask(
                    new FakeTaskHost(),
                    Pool,
                    new UnreachableDataStoreFactory(),
                    new SqlRetrievalHookActivator(),
                    FixedClock.Instance,
                    resetAnswer)
                : null;

            Host = new FakeProxyHost { Task = RefusingWorker ?? (SqlTaskBase)Worker };

            Proxy = new SqlCommandTaskProxy(
                Host,
                NullLogger<SqlCommandTaskProxy>.Instance,
                FixedClock.Instance);
        }

        internal TransactionPool Pool { get; }

        internal SqlCommandTask Worker { get; }

        internal RefusingSqlTask? RefusingWorker { get; }

        internal FakeProxyHost Host { get; }

        internal SqlCommandTaskProxy Proxy { get; }

        public void Dispose()
        {
            Proxy.Dispose();
            RefusingWorker?.Dispose();
            Worker.Dispose();
            Pool.Dispose();
        }
    }

    /// <summary>
    /// The threading substrate seam, with the busy inputs and the init answer made settable.
    /// </summary>
    private sealed class FakeProxyHost : ISqlTaskProxyHost
    {
        public bool IsRunning { get; set; }

        public bool IsControllerBusy { get; set; }

        public bool IsSyncSignalSet { get; set; }

        public bool IsCancelled { get; private set; }

        public CancellationToken Cancellation => CancellationToken.None;

        public long LastExitCode => RetCode.OK;

        public long LastErrorCode => RetCode.OK;

        public string LastErrorInfo => string.Empty;

        public ulong TaskId => 1UL;

        public int TaskIndex => 1;

        public string TaskClassName => SqlCommandTaskProxy.LegacyWorkerClassName;

        public SqlTaskBase? Task { get; set; }

        /// <summary>The class name the proxy asked the substrate to insert.</summary>
        internal string? RequestedWorkerClassName { get; private set; }

        /// <summary>The answer the substrate gives to the init request.</summary>
        internal long OnInitResult { get; set; } = RetCode.OK;

        public void RaiseSyncSignal() => IsSyncSignalSet = true;

        public void ClearSyncSignal() => IsSyncSignalSet = false;

        public long OnInit(string workerClassName)
        {
            RequestedWorkerClassName = workerClassName;
            return OnInitResult;
        }

        public long OnPrepare() => RetCode.OK;

        public long Cancel()
        {
            IsCancelled = true;
            return RetCode.OK;
        }

        public long SetWorkerDelayFor(double seconds) => RetCode.OK;

        public long SetWorkerSkip(bool skip) => RetCode.OK;
    }

    /// <summary>The worker's own substrate seam. Inert: no test here runs a task body.</summary>
    private sealed class FakeTaskHost : ISqlTaskHost
    {
        public bool IsMainThread => false;

        public bool IsCancelled => false;

        public int TaskIndex => 1;

        public ISqlTaskProxy? ParentTasking => null;

        public long GetTask(int index, out SqlTaskBase? task)
        {
            task = null;
            return RetCode.E_INVALID_ARGUMENT;
        }

        public bool HasData(string name) => false;

        public object? GetData(string name) => null;

        public long SetData(string name, object? data) => RetCode.OK;

        public long OnPrepare() => RetCode.OK;

        public void OnUninit()
        {
            // Nothing to release: this double holds no cross-boundary reference.
        }

        public long OnError(long errCode, string errInfo) => RetCode.OK;

        public long OnNotify(long notifyCode, long payload, string text) => RetCode.OK;
    }

    /// <summary>
    /// A worker of a DIFFERENT task type whose reset answers a caller-chosen code - the only way to
    /// reach the proxy's worker-refusal arm at all, since <c>SqlTaskBase.Reset</c> always answers OK,
    /// and the only way to drive a non-failing non-OK code through the tri-state boundary.
    /// </summary>
    private sealed class RefusingSqlTask : SqlTaskBase
    {
        private readonly long _resetAnswer;

        internal RefusingSqlTask(
            ISqlTaskHost host,
            TransactionPool transactionPool,
            ISqlDataStoreFactory dataStoreFactory,
            ISqlRetrievalHookActivator hookActivator,
            TimeProvider timeProvider,
            long resetAnswer)
            : base(
                host,
                transactionPool,
                dataStoreFactory,
                hookActivator,
                timeProvider,
                NullLogger<RefusingSqlTask>.Instance)
        {
            _resetAnswer = resetAnswer;
        }

        internal override long Reset() => _resetAnswer;
    }

    /// <summary>
    /// A pooled-transaction activator that is never reached. No test here opens a connection
    /// (constraint C-E), so an invocation would mean the suite had stopped testing what it claims to.
    /// </summary>
    private sealed class UnreachableTransactionActivator : IPooledTransactionActivator
    {
        public IPooledTransaction CreateDefault() =>
            throw new NotSupportedException("No test in this suite opens a transaction.");

        public IPooledTransaction Create(string className) =>
            throw new NotSupportedException("No test in this suite opens a transaction.");
    }

    /// <summary>
    /// A result-carrier factory that is never reached. A command produces no result set, and no test
    /// here runs a task body.
    /// </summary>
    private sealed class UnreachableDataStoreFactory : ISqlDataStoreFactory
    {
        public ISqlDataStore Create(CarrierThreadAffinity affinity) =>
            throw new NotSupportedException("No test in this suite creates a result carrier.");
    }

    /// <summary>
    /// A clock nothing advances. The subject reads no clock, and a test that needed one would prove
    /// otherwise (constraint C-H).
    /// </summary>
    private sealed class FixedClock : TimeProvider
    {
        internal static readonly FixedClock Instance = new();

        public override DateTimeOffset GetUtcNow() => new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    }

    #endregion
}
