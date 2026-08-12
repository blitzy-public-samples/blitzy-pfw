// ==============================================================================================
//  ConflictDetectorTests.cs - THE UPDATE OUTCOME PARITY MATRICES
//  --------------------------------------------------------------------------------------------
//  UNDER TEST     services/persistence-service/PowerFramework.Persistence/Concurrency/
//                     ConflictDetector.cs
//  ORACLE         ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru      (READ ONLY)
//                 ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru               (READ ONLY)
//                 ws_objects/pfw.thread.ext.pbl.src/dberrordata.srs                      (READ ONLY)
//                 ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlupdate.sru   (READ ONLY)
//  FIXTURE        ws_objects/pfw.tests.pbl.src/dw_sqlite.srd                             (READ ONLY)
//
//  THE HEADLINE ASSERTION IN THIS FILE IS THE OVERRIDE MATRIX. Two verified behaviours invert what
//  a straightforward port produces - success is the literal 1 rather than the zero of the
//  return-code algebra, and a CLAIMED success is rewritten into a failure when the transaction's
//  SQL code is EXACTLY -1 - and an implementation that trusts the update call's own return value
//  reports success on a failed update. OverrideFiresOnlyForExactlyMinusOne is the test that catches
//  a "negative" or "non-zero" reading of that condition, and it must not be weakened: the -2, the
//  +1 and the 100 cases are the whole point.
//
//  ============ FIVE NUMERIC TESTS OVER TWO FIELDS. DO NOT MERGE ANY TWO OF THEM (C-B, C-K) ======
//  Merging any two would change behaviour in a way NO HAPPY-PATH TEST WOULD CATCH, which is why the
//  divergence is asserted EXECUTABLY by ThreeNumericReadingsOfOneSqlCodeDivergeAndAreNeverMerged
//  below and not merely described here. A comment cannot fail a build; that matrix can.
//
//      #  SITE                                LOCATOR                             CONDITION
//      1  the defensive override              sqlupdate.sru:L208                  SQLCode == -1
//                                                                                 EXACTLY (and
//                                                                                 rtCode == 1)
//      2  the transaction's own state test    n_cst_thread_trans.sru:L233         SQLCode <> 0 AND
//                                                                                 SQLCode <> 100
//                                                                                 (NON-ZERO
//                                                                                 EXCLUDING 100)
//      3  the transaction's failure predicate n_cst_thread_trans.sru:L337         SQLCode < 0
//                                                                                 (NEGATIVE)
//      4  the transaction's success predicate n_cst_thread_trans.sru:L340         SQLCode >= 0
//      5  the update success test             sqlupdate.sru:L214                  rtCode == 1
//                                                                                 EXACTLY, in the
//                                                                                 DATAWINDOW code
//                                                                                 space
//
//  THE WITNESSES THAT PROVE THE THREE READINGS OF SQLCode ARE THREE AND NOT ONE:
//      SQLCode = 100  ->  #1 no   #2 NO    #3 no    (100 reads as SUCCESS here and at #2)
//      SQLCode =   1  ->  #1 no   #2 YES   #3 no    (a driver warning: #2 fires, #1 and #3 do not)
//      SQLCode =  -2  ->  #1 no   #2 YES   #3 YES   (negative but NOT -1: #1 alone declines)
//      SQLCode =  -1  ->  #1 YES  #2 YES   #3 YES   (the only value all three agree on)
//      SQLCode =   0  ->  #1 no   #2 no    #3 no
//  Two SEPARATE fields are also in play and are never interchanged: the veto arm's payload reads
//  SQLDBCode [sqlupdate.sru:L197] while the defensive override reads SQLCode [:L208].
//
//  ============ THE DIVISION OF RESPONSIBILITY THIS FILE HOLDS THE SUBJECT TO (C-K) ==============
//  The conflict path is shared by three files and each owns exactly one third of it. This file
//  asserts the SUBJECT's third and asserts that it does not encroach on the other two:
//
//      Concurrency/ConflictDetector.cs   PRODUCES a typed outcome, populates
//                                        common.v1.ConflictDetail, and exposes the ONE sanctioned
//                                        projection TryProjectAborted. IT THROWS NO RpcException.
//                                        NoRpcExceptionEverEscapesTheDetector is the guard.
//      Program.cs                        owns the CENTRAL mismatch-to-Aborted mapping for the host
//                                        [Program.cs:L2022-L2024 passes an RpcException through
//                                        untouched].
//      Grpc/UpdateService.cs             THE DESIGNATED THROW SITE [its header, and :L2537].
//
//  A classifier that threw would decide the transport on its caller's behalf and there would then
//  be two spellings of one status, free to drift. So the outcome is DATA here, and every test below
//  asserts data rather than a caught exception.
//
//  NO DATABASE, NO DRIVER, NO LIVE TRANSACTION AND NO REAL CONCURRENT WRITER IS INVOLVED ANYWHERE
//  IN THIS FILE (C-E). Every surface the subject reads is injected and faked here, and the fakes
//  RECORD their interactions so the tests can assert not just the answers but which calls were
//  made, with which arguments, and IN WHAT ORDER - which is the only way the "both raises fire, in
//  order" and "the hook observes the pre-override value" properties are checkable at all. The
//  shared recorders in TestDoubles.cs carry that load: RecordingSqlRedactor is the redaction seam
//  and ScriptedPooledTransaction is the ordered call recorder, whose IsSqlCodeDefensiveOverride and
//  IsSqlCodeNonZeroExcludingNotFound members are readings #1 and #2 above.
//
//  NO SECRET, NO CREDENTIAL AND NO CONNECTION STRING APPEARS IN ANY DOUBLE HERE (C-F), and
//  NoCredentialShapedValueIsEverEchoed asserts that a credential-shaped statement cannot reach a
//  payload unmasked. NO PERFORMANCE ASSERTION APPEARS ANYWHERE IN THIS FILE (AAP 0.8.5): the
//  repository publishes no latency, throughput or availability target, so none is claimed.
//
//  THE FIXTURE IS TEST DATA, NOT PRODUCTION DATA. COMPANY, its six columns and their one-based ids
//  appear here because dw_sqlite.srd is the sole updatable DataWindow in the repository; the
//  production code under test names no table, no column and no column count.
//
//  A NOTE ON LINE CITATIONS. Every `:L` below was read off the oracle and verified by search, so a
//  few differ by a line or two from the ranges quoted in this file's brief - the first cancellation
//  check is at :L182 rather than :L186, which is `Data.of_ClearState()`. The VERIFIED number is
//  cited throughout, because a locator that does not resolve is worse than none.
// ==============================================================================================

// THE IMPORT SET IS DELIBERATELY MINIMAL AND MATCHES THE SUBJECT'S OWN DEPENDENCY SURFACE.
//   * Grpc.Core supplies Status, StatusCode and Metadata, which the sanctioned projection answers with.
//   * PowerFramework.Persistence.Concurrency is the subject.
// Everything else this file needs - the buffers, the error payloads, the contracts, the shared kernel and
// its RetCode alias - arrives through GlobalUsings.cs. NOTHING is imported from the transaction pool: the
// subject reads the driver's three code members through IUpdateTransaction and knows nothing of the pool,
// so a test that reached into it would be asserting against a coupling the subject does not have.
using Grpc.Core;
using PowerFramework.Persistence.Concurrency;

namespace PowerFramework.Persistence.Tests;

#region The recording fakes

/// <summary>
/// One recorded call on either error channel, in the order it was made.
/// </summary>
/// <param name="Channel">
/// <c>"OnDBError"</c> or <c>"OnError"</c> - the two SEPARATE legacy channels.
/// </param>
/// <param name="DbError">The five-member payload, for a database-error raise.</param>
/// <param name="Code">The return code, for a general raise.</param>
/// <param name="ErrorText">The message, for a general raise.</param>
internal readonly record struct RecordedRaise(
    string Channel,
    DbErrorData? DbError,
    long Code,
    string ErrorText);

/// <summary>
/// Records every raise on both channels, in order, and raises nothing itself.
/// </summary>
internal sealed class RecordingErrorSink : IUpdateErrorSink
{
    /// <summary>The raises, in the order they were made.</summary>
    internal List<RecordedRaise> Raises { get; } = [];

    /// <inheritdoc/>
    public void OnDbError(in DbErrorData error) =>
        Raises.Add(new RecordedRaise("OnDBError", error, 0L, string.Empty));

    /// <inheritdoc/>
    public void OnError(long code, string errorText) =>
        Raises.Add(new RecordedRaise("OnError", null, code, errorText));
}

/// <summary>
/// A transaction whose SQL code, driver code, error text and veto answer are all set by the test.
/// </summary>
internal sealed class RecordingTransaction : IUpdateTransaction
{
    /// <inheritdoc/>
    public long SqlCode { get; set; }

    /// <inheritdoc/>
    public long SqlDbCode { get; set; }

    /// <inheritdoc/>
    public string SqlErrText { get; set; } = string.Empty;

    /// <summary>What the before-update hook answers. <c>RetCode.PREVENT</c> is a veto.</summary>
    internal long BeforeUpdateResult { get; set; } = RetCode.OK;

    /// <summary>How many times the before-update hook was invoked.</summary>
    internal int BeforeUpdateCalls { get; private set; }

    /// <summary>Every value the after-update hook was handed, in order.</summary>
    internal List<long> AfterUpdateResults { get; } = [];

    /// <summary>Runs when the after-update hook fires, so a test can order it against the override.</summary>
    internal Action? OnAfterUpdateObserved { get; set; }

    /// <inheritdoc/>
    public bool IsFailed() => SqlCode < 0L;

    /// <summary>How many times the classification cleared this transaction's state.</summary>
    /// <remarks>
    /// COUNTED RATHER THAN APPLIED. A test sets the three SQL values to stage a specific arm, so a clear
    /// that actually reset them would erase the very staging - the observable a case needs is THAT the
    /// classification cleared, and when relative to the update, which the counter and the recorded hook
    /// order together give.
    /// </remarks>
    internal int ClearStateCalls { get; private set; }

    /// <inheritdoc/>
    public void ClearState() => ClearStateCalls++;

    /// <inheritdoc/>
    public long OnBeforeUpdate()
    {
        BeforeUpdateCalls++;

        return BeforeUpdateResult;
    }

    /// <inheritdoc/>
    public void OnAfterUpdate(long result)
    {
        AfterUpdateResults.Add(result);

        OnAfterUpdateObserved?.Invoke();
    }
}

/// <summary>
/// A carrier that records the clear-state call and every update invocation's two arguments.
/// </summary>
internal sealed class RecordingUpdateTarget : IUpdateTarget
{
    /// <summary>What the update answers, in the DATAWINDOW code space.</summary>
    internal long UpdateResult { get; set; } = DataWindowBufferStore.DataStoreSuccess;

    /// <summary>How many times the state was cleared.</summary>
    internal int ClearStateCalls { get; private set; }

    /// <summary>The two arguments of every update invocation, in order.</summary>
    internal List<(bool AcceptText, bool ResetFlag)> UpdateCalls { get; } = [];

    /// <summary>Runs when the update is invoked, so a test can cancel mid-flight.</summary>
    internal Action? OnUpdateInvoked { get; set; }

    /// <summary>
    /// The affected-row measurement this target answers, and <b>ONLY ONCE THE UPDATE HAS RUN</b>.
    /// </summary>
    /// <remarks>
    /// THE FAKE ENFORCES THE ORDERING THE CONTRACT REQUIRES rather than merely permitting it: the
    /// measurement is answered as <see langword="null"/> until <see cref="Update"/> has been invoked, which
    /// is what a real carrier does - it cannot know how many rows a predicate matched before submitting it.
    /// A classifier that read the evidence before running the update would therefore see nothing here, and
    /// the tests that assert the conflict arm would fail rather than pass on a value no real carrier could
    /// have supplied at that moment.
    /// </remarks>
    internal ConcurrencyEvidence? Evidence { get; set; }

    /// <summary>How many times the measurement was read.</summary>
    internal int CaptureConcurrencyEvidenceCalls { get; private set; }

    /// <inheritdoc/>
    public void ClearState() => ClearStateCalls++;

    /// <inheritdoc/>
    public ConcurrencyEvidence? CaptureConcurrencyEvidence()
    {
        CaptureConcurrencyEvidenceCalls++;

        return UpdateCalls.Count == 0 ? null : Evidence;
    }

    /// <inheritdoc/>
    public long Update(bool acceptText, bool resetFlag, CancellationToken cancellationToken = default)
    {
        UpdateCalls.Add((acceptText, resetFlag));

        OnUpdateInvoked?.Invoke();

        return UpdateResult;
    }
}

/// <summary>
/// The two identity surfaces on ONE object - which is what the legacy actually is, a single
/// <c>Data</c> carrier - answering just enough for the update table describe and the counts report.
/// </summary>
/// <remarks>
/// The identity round trip itself is exercised exhaustively by
/// <c>IdentityColumnResolverTests.cs</c>; this fake exists so the classifier's SUCCESS ARM can be
/// reached and its delegation observed, not to re-test the resolver.
/// </remarks>
internal sealed class FakeUpdateSurfaces : IIdentityColumnMetadata, IIdentityValueSource
{
    /// <summary>What the update-table describe answers.</summary>
    internal string UpdateTable { get; set; } = FixtureTableName;

    /// <summary>How many times the update table was described.</summary>
    internal int DescribeUpdateTableCalls { get; private set; }

    /// <summary>The counts the report reads.</summary>
    internal long Inserted { get; set; }

    /// <summary>The counts the report reads.</summary>
    internal long Updated { get; set; }

    /// <summary>The counts the report reads.</summary>
    internal long Deleted { get; set; }

    /// <summary>The sole updatable DataWindow's table name - FIXTURE DATA [dw_sqlite.srd:L14].</summary>
    internal const string FixtureTableName = "COMPANY";

    /// <inheritdoc/>
    public string DescribeUpdateTable()
    {
        DescribeUpdateTableCalls++;

        return UpdateTable;
    }

    /// <inheritdoc/>
    public int GetColumnCount() => 0;

    /// <inheritdoc/>
    public string DescribeColumnIdentity(string identityProperty) => string.Empty;

    /// <inheritdoc/>
    public string DescribeColumnDbName(string dbNameProperty) => string.Empty;

    /// <inheritdoc/>
    public long GetInsertedCount() => Inserted;

    /// <inheritdoc/>
    public long GetUpdatedCount() => Updated;

    /// <inheritdoc/>
    public long GetDeletedCount() => Deleted;

    /// <inheritdoc/>
    public long RowCount() => 0L;

    /// <inheritdoc/>
    public long FilteredCount() => 0L;

    /// <inheritdoc/>
    public ItemStatus GetItemStatus(long row, int columnIndex, DwBuffer buffer) =>
        ItemStatus.NotModified;

    /// <inheritdoc/>
    public long? GetItemNumber(long row, int columnNumber) => null;

    /// <inheritdoc/>
    public long? GetItemNumber(long row, int columnNumber, DwBuffer buffer, bool originalValue) => null;
}

/// <summary>
/// Which arm of <c>_of_update</c> a whole-surface sweep should drive.
/// </summary>
/// <remarks>
/// <para>
/// A PUBLIC MIRROR OF THE SUBJECT'S INTERNAL <c>UpdateOutcomeKind</c>, AND IT EXISTS FOR EXACTLY ONE
/// REASON: xUnit theory parameters travel through a public signature, and the subject's kind is
/// <see langword="internal"/> - correctly so, since nothing outside the service should switch on it.
/// Widening the production enum to satisfy a test would be the wrong direction of change, so the test
/// declares its own discriminator and maps across.
/// </para>
/// <para>
/// THE MEMBERS ARE NAMED FOR THE OUTCOME RATHER THAN FOR THE STAGING, and
/// <c>ConflictDetectorTests.KindOf</c> is the single place the mapping lives - so a kind added to the
/// subject without a member here fails that mapping rather than being silently unswept. There is
/// deliberately NO member mirroring the subject's reserved zero slot, because that slot is unreachable
/// through the sanctioned factories and a sweep over it would assert a state the subject cannot produce.
/// </para>
/// </remarks>
public enum UpdateArm
{
    /// <summary>The update reported the DataWindow success value [<c>:L214</c>, <c>:L248</c>].</summary>
    Succeeded,

    /// <summary>A clean veto, or either cancellation check [<c>:L182</c>, <c>:L201</c>, <c>:L212</c>].</summary>
    Cancelled,

    /// <summary>The transaction could not be acquired [<c>:L180</c>].</summary>
    InvalidTransaction,

    /// <summary>A sentinel table, a failing veto, or the else arm [<c>:L192</c>, <c>:L199</c>, <c>:L250</c>].</summary>
    DatabaseError,

    /// <summary>A measured optimistic-concurrency mismatch - the mandated narrowing.</summary>
    Conflict,

    /// <summary>A row flagged modified that supplied no updatable value.</summary>
    InvalidUpdateData,

    /// <summary>A constraint refusal the caller's payload controls.</summary>
    ConstraintViolation,
}

// THE REDACTION SEAM IS THE SHARED RecordingSqlRedactor FROM TestDoubles.cs, NOT A LOCAL ONE.
// It already provides everything the redaction obligation needs to be provable - an ordered
// Statements snapshot, a CallCount, a one-based OrdinalOf, and a Mask hook that turns the seam into
// a PASS-THROUGH so the single path stays observable even with a redactor that masks nothing - and
// it RECORDS AN EMPTY INPUT rather than short-circuiting before the log, which is precisely what
// makes the two empty-statement legacy raises [:L190, :L197] checkable as being on the same path
// (C-F). A second local spelling of that seam would be free to drift from it.

#endregion

/// <summary>
/// The parity matrices for the update outcome classifier.
/// </summary>
public sealed class ConflictDetectorTests
{
    /// <summary>The redactor the subject under test is constructed with.</summary>
    /// <remarks>
    /// THE SHARED SEAM FROM TestDoubles.cs. Its default masking replaces any non-empty statement with
    /// <see cref="RecordingSqlRedactor.MaskedMarker"/>, and an empty statement is recorded and then
    /// returned unchanged - so a raise carrying no statement still proves it went through the seam.
    /// </remarks>
    private readonly RecordingSqlRedactor _redactor = new();

    /// <summary>The subject under test.</summary>
    private readonly ConflictDetector _detector;

    /// <summary>The recording sink both error channels reach.</summary>
    private readonly RecordingErrorSink _sink = new();

    /// <summary>The transaction, defaulting to a healthy one that does not veto.</summary>
    private readonly RecordingTransaction _transaction = new();

    /// <summary>The carrier, defaulting to a successful update.</summary>
    private readonly RecordingUpdateTarget _target = new();

    /// <summary>The describe and value surfaces, defaulting to the fixture's table name.</summary>
    private readonly FakeUpdateSurfaces _surfaces = new();

    /// <summary>
    /// The cancellation signal the subject polls twice. Not read-only: a test sets it before building an
    /// attempt, which is what lets the two checks be exercised independently.
    /// </summary>
    private CancellationToken _cancellation;

    /// <summary>Builds the subject.</summary>
    public ConflictDetectorTests() => _detector = new ConflictDetector(_redactor);

    /// <summary>
    /// Builds an attempt from the shared fakes, with the transaction and cancellation overridable.
    /// </summary>
    /// <param name="transaction">
    /// <see langword="false"/> to model a FAILED ACQUISITION by supplying no transaction at all.
    /// </param>
    /// <param name="evidence">The affected-row measurement, or <see langword="null"/>.</param>
    /// <returns>The attempt.</returns>
    /// <remarks>
    /// THE CANCELLATION SIGNAL IS A FIELD RATHER THAN A PARAMETER ON PURPOSE. xUnit's analyzer flags
    /// every call to a method that accepts a <see cref="CancellationToken"/> unless the test's own token
    /// is passed, and the token here is the SUBJECT of the test rather than the test's own cancellation -
    /// so it is set on <see cref="_cancellation"/> instead of threaded through this builder.
    /// </remarks>
    private UpdateAttempt Attempt(
        bool transaction = true,
        ConcurrencyEvidence? evidence = null)
    {
        // THE MEASUREMENT IS INSTALLED ON THE TARGET, NOT ON THE ATTEMPT, because that is where the
        // classifier reads it from and when: after the update has run. An attempt is assembled BEFORE the
        // update and cannot carry a measurement that does not exist yet - see
        // IUpdateTarget.CaptureConcurrencyEvidence.
        _target.Evidence = evidence;

        return new UpdateAttempt
        {
            Transaction = transaction ? _transaction : null,
            Target = _target,
            Identity = new IdentityTableSurfaces(_surfaces, _surfaces),
            Errors = _sink,
            Cancellation = _cancellation,
        };
    }

    /// <summary>
    /// Asserts a reference is present and returns it, since <c>Assert.NotNull</c> answers nothing.
    /// </summary>
    /// <typeparam name="T">The reference type.</typeparam>
    /// <param name="value">The candidate.</param>
    /// <returns>The non-null value.</returns>
    private static T Required<T>(T? value)
        where T : class
    {
        Assert.NotNull(value);

        return value;
    }

    /// <summary>
    /// Asserts a nullable value type is present and returns its value.
    /// </summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="value">The candidate.</param>
    /// <returns>The contained value.</returns>
    private static T RequiredValue<T>(T? value)
        where T : struct
    {
        Assert.NotNull(value);

        return value.Value;
    }

    #region Step 1 - transaction acquisition [:L180]

    /// <summary>
    /// A failed acquisition answers <c>E_INVALID_TRANSACTION</c> and does nothing else at all.
    /// </summary>
    [Fact]
    public void FailedTransactionAcquisitionReturnsInvalidTransactionAndTouchesNothing()
    {
        UpdateOutcome outcome = _detector.Classify(Attempt(transaction: false));

        Assert.Equal(UpdateOutcomeKind.InvalidTransaction, outcome.Kind);
        Assert.Equal(RetCode.E_INVALID_TRANSACTION, outcome.Code);
        Assert.False(outcome.UpdateInvoked);
        Assert.False(outcome.IsSucceeded);
        Assert.True(outcome.RequiresRollback);

        // The oracle returns before Data.of_ClearState() [:L186] and raises nothing on either channel.
        Assert.Equal(0, _target.ClearStateCalls);
        Assert.Empty(_target.UpdateCalls);
        Assert.Empty(_sink.Raises);
        Assert.Null(outcome.DbError);
        Assert.False(outcome.ErrorReported);
    }

    /// <summary>
    /// THE TRANSACTION CHECK RUNS BEFORE THE CANCELLATION CHECK, so an attempt that is both cancelled
    /// and transaction-less reports the transaction fault [<c>:L180</c> precedes <c>:L182</c>].
    /// </summary>
    [Fact]
    public void TransactionCheckPrecedesTheFirstCancellationCheck()
    {
        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();
        _cancellation = cancelled.Token;

        UpdateOutcome outcome =
            _detector.Classify(Attempt(transaction: false));

        Assert.Equal(UpdateOutcomeKind.InvalidTransaction, outcome.Kind);
        Assert.Equal(RetCode.E_INVALID_TRANSACTION, outcome.Code);
    }

    #endregion

    #region Step 4 - the THREE sentinels, both raises, exact payload [:L188-L193]

    /// <summary>
    /// THE SENTINEL MATRIX. All THREE describe answers the oracle rejects, as member data.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE SET IS EXACTLY THE ORACLE'S DISJUNCTION AND IS NOT A SAMPLE OF IT:
    /// <c>if sUpdateTable = "" or sUpdateTable = "!" or sUpdateTable = "?"</c> [<c>:L189</c>]. A PORT
    /// THAT CHECKS ONLY FOR EMPTY MISSES TWO THIRDS OF THE CONDITION, and the two it misses are the
    /// PowerBuilder describe-failure markers - <c>!</c> meaning the property could not be read and
    /// <c>?</c> meaning it does not apply - so the arm they guard is reached precisely when the
    /// modification script did not take.
    /// </para>
    /// <para>
    /// MEMBER DATA RATHER THAN INLINE DATA, so the same three values drive both the end-to-end arm and
    /// the predicate matrix from ONE declaration. Two lists of sentinels could disagree; one cannot.
    /// </para>
    /// </remarks>
    /// <returns>The three sentinels.</returns>
    public static TheoryData<string> Sentinels() =>
    [
        string.Empty,
        ConflictDetector.DescribeErrorMarker,
        ConflictDetector.DescribeUnknownMarker,
    ];

    /// <summary>
    /// The sentinel predicate's full matrix: the three rejected answers, a null, values that only LOOK
    /// like a sentinel, and the fixture's legal table name.
    /// </summary>
    /// <returns>Each candidate paired with whether the oracle treats it as "no updatable table".</returns>
    public static TheoryData<string?, bool> SentinelCandidates() =>
        new()
        {
            { string.Empty, true },
            { ConflictDetector.DescribeErrorMarker, true },
            { ConflictDetector.DescribeUnknownMarker, true },
            { null, true },
            { " ", false },
            { "!x", false },
            { "??", false },
            { " ! ", false },
            { FakeUpdateSurfaces.FixtureTableName, false },
        };

    /// <summary>
    /// All THREE sentinel answers fire BOTH raises, in order, with the exact legacy payload, and return
    /// the database-error code.
    /// </summary>
    /// <param name="sentinel">The describe answer: empty, the error marker, or the unknown marker.</param>
    [Theory]
    [MemberData(nameof(Sentinels))]
    public void EverySentinelUpdateTableFiresBothRaisesInOrderWithTheLegacyPayload(string sentinel)
    {
        _surfaces.UpdateTable = sentinel;

        UpdateOutcome outcome = _detector.Classify(Attempt());

        // `Data.of_ClearState()` [:L186] happens BEFORE the describe, so it still ran.
        Assert.Equal(1, _target.ClearStateCalls);
        Assert.Equal(1, _surfaces.DescribeUpdateTableCalls);

        // BOTH raises, IN ORDER: OnDBError [:L190] then OnError [:L191].
        Assert.Equal(2, _sink.Raises.Count);
        Assert.Equal("OnDBError", _sink.Raises[0].Channel);
        Assert.Equal("OnError", _sink.Raises[1].Channel);

        // `Event OnDBError(-1,"没有可更新的表","",Primary!,0)` [:L190], member for member.
        DbErrorData payload = RequiredValue(_sink.Raises[0].DbError);
        Assert.Equal(-1L, payload.SqlDbCode);

        // ASSERTED AGAINST THE SIBLING'S CONSTANT, NEVER A LOCALLY TYPED LITERAL - a second spelling of
        // the diagnostic is exactly what DbErrorMessages exists to prevent.
        Assert.Equal(DbErrorMessages.NoUpdatableTable, payload.SqlErrText);
        Assert.Equal(string.Empty, payload.SqlSyntax);
        Assert.Equal(DwBuffer.Primary, payload.Buffer);
        Assert.Equal(0L, payload.Row);

        // `Event OnError(RetCode.E_DB_ERROR,"没有可更新的表")` [:L191] - THE SAME MESSAGE.
        Assert.Equal(RetCode.E_DB_ERROR, _sink.Raises[1].Code);
        Assert.Equal(DbErrorMessages.NoUpdatableTable, _sink.Raises[1].ErrorText);

        // `return RetCode.E_DB_ERROR` [:L192], and the update never ran.
        Assert.Equal(UpdateOutcomeKind.DatabaseError, outcome.Kind);
        Assert.Equal(RetCode.E_DB_ERROR, outcome.Code);
        Assert.True(outcome.ErrorReported);
        Assert.Equal(DbErrorMessages.NoUpdatableTable, outcome.ErrorText);
        Assert.Equal(sentinel, outcome.UpdateTable);
        Assert.False(outcome.UpdateInvoked);
        Assert.Empty(_target.UpdateCalls);
        Assert.Equal(0, _transaction.BeforeUpdateCalls);
    }

    /// <summary>
    /// A LEGAL table name proceeds past the sentinel arm to the veto hook and the update.
    /// </summary>
    [Fact]
    public void ALegalUpdateTableProceedsToTheUpdate()
    {
        UpdateOutcome outcome = _detector.Classify(Attempt());

        Assert.Equal(UpdateOutcomeKind.Succeeded, outcome.Kind);
        Assert.Equal(1, _transaction.BeforeUpdateCalls);
        Assert.Single(_target.UpdateCalls);
        Assert.Empty(_sink.Raises);
        Assert.Equal(FakeUpdateSurfaces.FixtureTableName, outcome.UpdateTable);
    }

    /// <summary>
    /// The sentinel predicate itself: three sentinels, and values that only LOOK like them.
    /// </summary>
    /// <param name="candidate">The describe answer.</param>
    /// <param name="expected">Whether the oracle treats it as "no updatable table".</param>
    [Theory]
    [MemberData(nameof(SentinelCandidates))]
    public void SentinelPredicateMatchesExactlyTheThreeLegacyAnswers(string? candidate, bool expected) =>
        // PowerScript's `=` on strings is EXACT, so " " and "!x" are not sentinels and must not become
        // ones by trimming [:L189].
        Assert.Equal(expected, ConflictDetector.IsSentinelUpdateTable(candidate));

    /// <summary>
    /// THE CHINESE DIAGNOSTIC IS INTACT, CODE POINT BY CODE POINT, and it has exactly ONE spelling in the
    /// system.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 THIS GUARDS AGAINST MOJIBAKE, WHICH IS A FAILURE MODE NO OTHER ASSERTION IN THIS FILE WOULD
    /// CATCH. Every other test compares the payload against
    /// <c>DbErrorMessages.NoUpdatableTable</c>, so if that constant were corrupted - a source file
    /// re-saved in a single-byte code page, a build pipeline without a UTF-8 assumption, a copy through a
    /// terminal that mangled the bytes - the comparison would still SUCCEED, because both sides would be
    /// corrupt in the same way. Pinning the SEVEN CODE POINTS is what makes the corruption visible.
    /// </para>
    /// <para>
    /// THE EXPECTED VALUES ARE THE ORACLE'S OWN, read from
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru:L190-L191</c> where the same
    /// text appears as the third argument of the database-error raise and the second of the general raise.
    /// They are written as escapes rather than as glyphs precisely so that THIS file's own encoding cannot
    /// affect the expectation - an escape survives any code page.
    /// </para>
    /// <para>
    /// AND ONE SPELLING, NOT TWO. The constant is the single declaration; this file's only glyph
    /// occurrences are inside comments citing the oracle line, which is traceability rather than a second
    /// executable spelling that could drift.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheNoUpdatableTableDiagnosticIsByteForByteTheOraclesAndHasOneSpelling()
    {
        // 没有可更新的表 - "there is no updatable table" [:L190, :L191], as code points.
        const string Expected = "\u6ca1\u6709\u53ef\u66f4\u65b0\u7684\u8868";

        Assert.Equal(Expected, DbErrorMessages.NoUpdatableTable);
        Assert.Equal(7, DbErrorMessages.NoUpdatableTable.Length);

        // THE UTF-8 ENCODING IS THE 21 BYTES THE ORACLE FILE CARRIES - three bytes per code point, and no
        // replacement character anywhere, which is what a lossy round trip would have left behind.
        Assert.Equal(21, System.Text.Encoding.UTF8.GetByteCount(DbErrorMessages.NoUpdatableTable));
        Assert.DoesNotContain('\uFFFD', DbErrorMessages.NoUpdatableTable);
        Assert.DoesNotContain('?', DbErrorMessages.NoUpdatableTable);

        // AND THE FACTORY CARRIES THE SAME CONSTANT, so the payload and the general-channel message cannot
        // diverge from each other [:L190 vs :L191].
        Assert.Equal(Expected, DbErrorData.NoUpdatableTable().SqlErrText);

        // The other four members of that raise, stated together so the whole argument list is pinned in one
        // place: code -1, an EMPTY statement, the Primary buffer, and row 0.
        DbErrorData sentinel = DbErrorData.NoUpdatableTable();

        Assert.Equal(-1L, sentinel.SqlDbCode);
        Assert.Equal(string.Empty, sentinel.SqlSyntax);
        Assert.Equal(DwBuffer.Primary, sentinel.Buffer);
        Assert.Equal(0L, sentinel.Row);
    }

    #endregion

    #region Step 5 - the veto arm and its two-way discrimination [:L195-L202]

    /// <summary>
    /// THE VETO MATRIX. Every hook answer paired with the arm the oracle takes for it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE DISCRIMINATOR IS <c>IsPrevented</c>, WHICH IS AN EQUALITY TEST AGAINST
    /// <c>RetCode.PREVENT</c> - AND PREVENT IS 1, A POSITIVE VALUE. So a veto is not "the hook
    /// failed": <c>RetCode.FAILED</c> is <c>-1</c> and does NOT veto, and neither does
    /// <c>RetCode.CANCELLED</c> at <c>-2</c>. Collapsing prevention into a boolean failure test would
    /// turn every one of the non-vetoing rows below into a veto and skip the update entirely.
    /// </para>
    /// <para>
    /// The value <c>2</c> is included deliberately: it is the deep-prevention value in the event
    /// broker's tri-valued veto protocol, and it is NOT a prevention to <c>IsPrevented</c>, which
    /// matches on 1 alone.
    /// </para>
    /// </remarks>
    /// <returns>Each hook answer paired with whether the oracle treats it as a veto.</returns>
    public static TheoryData<long, bool> VetoHookAnswers() =>
        new()
        {
            { RetCode.PREVENT, true },
            { RetCode.OK, false },
            { RetCode.FAILED, false },
            { RetCode.CANCELLED, false },
            { 2L, false },
            { -28L, false },
            { long.MaxValue, false },
        };

    /// <summary>
    /// SQL codes the transaction's failure predicate DECLINES, so a veto carrying one is a CLEAN veto.
    /// </summary>
    /// <remarks>
    /// The predicate is <c>SQLCode &lt; 0</c> [n_cst_thread_trans.sru:L337], so zero and every positive
    /// value are declined - INCLUDING 100 and INCLUDING 1, both of which the transaction object's own
    /// veto arm would treat as a failure because that arm tests NON-ZERO instead
    /// [n_cst_thread_trans.sru:L267]. The two arms genuinely disagree and must not be unified.
    /// </remarks>
    /// <returns>The declined codes.</returns>
    public static TheoryData<long> SqlCodesTheFailurePredicateDeclines() =>
    [
        0L,
        1L,
        100L,
        long.MaxValue,
    ];

    /// <summary>
    /// SQL codes the transaction's failure predicate ACCEPTS, so a veto carrying one is a DATABASE ERROR.
    /// </summary>
    /// <returns>The accepted codes, spanning the override code and values either side of it.</returns>
    public static TheoryData<long> SqlCodesTheFailurePredicateAccepts() =>
    [
        -1L,
        -2L,
        -5L,
        long.MinValue,
    ];

    /// <summary>
    /// A veto WITH a transaction failure raises the transaction's own code and text on the database
    /// channel and an EMPTY message on the general channel [<c>:L196-L199</c>].
    /// </summary>
    [Fact]
    public void VetoWithTransactionFailureRaisesTheTransactionsPayloadAndAnEmptyGeneralMessage()
    {
        _transaction.BeforeUpdateResult = RetCode.PREVENT;

        // of_IsFailed() is SQLCode < 0 [n_cst_thread_trans.sru:L337].
        _transaction.SqlCode = -5L;
        _transaction.SqlDbCode = 19L;
        _transaction.SqlErrText = "constraint failed";

        UpdateOutcome outcome = _detector.Classify(Attempt());

        Assert.Equal(2, _sink.Raises.Count);
        Assert.Equal("OnDBError", _sink.Raises[0].Channel);
        Assert.Equal("OnError", _sink.Raises[1].Channel);

        DbErrorData payload = RequiredValue(_sink.Raises[0].DbError);
        Assert.Equal(19L, payload.SqlDbCode);
        Assert.Equal("constraint failed", payload.SqlErrText);
        Assert.Equal(string.Empty, payload.SqlSyntax);
        Assert.Equal(DwBuffer.Primary, payload.Buffer);
        Assert.Equal(0L, payload.Row);

        // `Event OnError(RetCode.E_DB_ERROR,"")` [:L198] - EMPTY, unlike the sentinel arm's raise, and
        // the asymmetry is legacy behaviour that must not be harmonized.
        Assert.Equal(RetCode.E_DB_ERROR, _sink.Raises[1].Code);
        Assert.Equal(string.Empty, _sink.Raises[1].ErrorText);

        Assert.Equal(UpdateOutcomeKind.DatabaseError, outcome.Kind);
        Assert.Equal(RetCode.E_DB_ERROR, outcome.Code);
        Assert.True(outcome.ErrorReported);
        Assert.Equal(string.Empty, outcome.ErrorText);
        Assert.False(outcome.UpdateInvoked);
        Assert.Empty(_target.UpdateCalls);
    }

    /// <summary>
    /// A CLEAN veto yields <c>CANCELLED</c> and raises NOTHING [<c>:L201</c>].
    /// </summary>
    /// <param name="sqlCode">
    /// A code the failure predicate declines: zero, and a POSITIVE non-zero value - which the
    /// transaction object's OWN veto arm would treat as a failure [n_cst_thread_trans.sru:L267], and
    /// which this arm deliberately does not.
    /// </param>
    [Theory]
    [MemberData(nameof(SqlCodesTheFailurePredicateDeclines))]
    public void CleanVetoYieldsCancelledAndRaisesNothing(long sqlCode)
    {
        _transaction.BeforeUpdateResult = RetCode.PREVENT;
        _transaction.SqlCode = sqlCode;

        UpdateOutcome outcome = _detector.Classify(Attempt());

        Assert.Equal(UpdateOutcomeKind.Cancelled, outcome.Kind);
        Assert.Equal(RetCode.CANCELLED, outcome.Code);
        Assert.Empty(_sink.Raises);
        Assert.False(outcome.ErrorReported);
        Assert.Null(outcome.DbError);
        Assert.False(outcome.UpdateInvoked);
        Assert.Empty(_target.UpdateCalls);

        // CANCELLED is NEITHER succeeded nor failed in the legacy algebra, yet the epilogue still rolls
        // back for it [:L394-L395] while suppressing the raise [:L396].
        Assert.False(outcome.IsSucceeded);
        Assert.True(outcome.RequiresRollback);
        Assert.False(Predicates.IsFailed(outcome.Code));
        Assert.False(Predicates.IsSucceeded(outcome.Code));
    }

    /// <summary>
    /// THE VETO DISCRIMINATION IS AN EQUALITY TEST AGAINST <c>PREVENT</c> AND WAS NOT COLLAPSED INTO A
    /// BOOLEAN FAILURE. Only <c>RetCode.PREVENT</c> vetoes; every other answer proceeds to the update.
    /// </summary>
    /// <param name="hookResult">The hook's answer.</param>
    /// <param name="expectVeto">Whether the oracle treats that answer as a veto.</param>
    /// <remarks>
    /// <para>
    /// THE ROWS THAT MATTER MOST ARE THE NEGATIVE ONES. <c>RetCode.FAILED</c> is <c>-1</c> and
    /// <c>RetCode.CANCELLED</c> is <c>-2</c>; a port that read the hook through a failure test - or
    /// through a truthiness test on a non-zero answer - would veto on both and NEVER RUN THE UPDATE. The
    /// oracle tests <c>IsPrevented</c> [<c>:L195</c>], which is equality against
    /// <c>RetCode.PREVENT</c> = 1, so both proceed.
    /// </para>
    /// <para>
    /// AND THE SUBJECT'S BEHAVIOUR IS CROSS-CHECKED AGAINST THE SHARED KERNEL PREDICATE ITSELF rather
    /// than against a re-derived comparison, so this matrix cannot drift from the published contract the
    /// subject actually consumes.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(VetoHookAnswers))]
    public void PreventionIsAnEqualityTestAndNotABooleanFailure(long hookResult, bool expectVeto)
    {
        // The subject's discriminator, stated as the contract rather than as a comparison.
        Assert.Equal(expectVeto, Predicates.IsPrevented(hookResult));

        _transaction.BeforeUpdateResult = hookResult;

        // A HEALTHY TRANSACTION, so a veto lands on the CLEAN arm and is distinguishable from the
        // database-error arm by its code alone.
        _transaction.SqlCode = 0L;

        UpdateOutcome outcome = _detector.Classify(Attempt());

        Assert.Equal(1, _transaction.BeforeUpdateCalls);

        if (expectVeto)
        {
            // `return RetCode.CANCELLED` [:L201] - and the update never ran.
            Assert.Equal(UpdateOutcomeKind.Cancelled, outcome.Kind);
            Assert.Equal(RetCode.CANCELLED, outcome.Code);
            Assert.Empty(_target.UpdateCalls);
            Assert.False(outcome.UpdateInvoked);

            return;
        }

        Assert.Single(_target.UpdateCalls);
        Assert.Equal(UpdateOutcomeKind.Succeeded, outcome.Kind);
    }

    /// <summary>
    /// The veto's SECOND arm is the transaction's own failure predicate, and it is NEGATIVITY - so a
    /// negative code makes a veto a database error while a zero or positive one leaves it a clean
    /// cancellation.
    /// </summary>
    /// <param name="sqlCode">A code the failure predicate ACCEPTS.</param>
    /// <remarks>
    /// PAIRED WITH <see cref="CleanVetoYieldsCancelledAndRaisesNothing"/>, WHICH DRIVES THE DECLINED
    /// CODES. Together the two matrices cover the discriminator across its whole sign range, which is
    /// what proves the arm turns on <c>SQLCode &lt; 0</c> [n_cst_thread_trans.sru:L337] rather than on
    /// the NON-ZERO reading its sibling at [n_cst_thread_trans.sru:L267] uses - the codes <c>1</c> and
    /// <c>100</c> in the declined matrix are the witnesses for that.
    /// </remarks>
    [Theory]
    [MemberData(nameof(SqlCodesTheFailurePredicateAccepts))]
    public void VetoWithANegativeSqlCodeIsADatabaseErrorAcrossTheWholeNegativeRange(long sqlCode)
    {
        _transaction.BeforeUpdateResult = RetCode.PREVENT;
        _transaction.SqlCode = sqlCode;
        _transaction.SqlDbCode = 19L;
        _transaction.SqlErrText = "constraint failed";

        UpdateOutcome outcome = _detector.Classify(Attempt());

        Assert.Equal(UpdateOutcomeKind.DatabaseError, outcome.Kind);
        Assert.Equal(RetCode.E_DB_ERROR, outcome.Code);

        // BOTH raises, and the general one carries the EMPTY message [:L198] (C-B).
        Assert.Equal(2, _sink.Raises.Count);
        Assert.Equal(string.Empty, _sink.Raises[1].ErrorText);

        // The payload reads SQLDBCode [:L197], NOT the SQLCode that drove the discrimination - two
        // separate fields, never interchanged.
        Assert.Equal(19L, RequiredValue(_sink.Raises[0].DbError).SqlDbCode);

        Assert.False(outcome.UpdateInvoked);
        Assert.Empty(_target.UpdateCalls);
    }

    #endregion

    #region Step 6 - the update invocation's two arguments [:L204]

    /// <summary>
    /// The update is invoked with ACCEPT-TEXT TRUE and RESET-FLAG FALSE, and the carrier is never reset.
    /// </summary>
    [Fact]
    public void UpdateIsInvokedWithAcceptTextTrueAndResetFlagFalseAndNothingIsReset()
    {
        _detector.Classify(Attempt());

        (bool acceptText, bool resetFlag) = Assert.Single(_target.UpdateCalls);
        Assert.True(acceptText);
        Assert.False(resetFlag);

        // THE CALLER OWNS THE CARRIER'S STATE AFTERWARDS, and the surface makes that structural: the
        // update target interface EXPOSES NO RESET AT ALL, so no arm of the classifier could reset the
        // carrier even by mistake.
        Assert.DoesNotContain(
            typeof(IUpdateTarget).GetMembers(),
            member => member.Name.Contains("Reset", StringComparison.Ordinal));

        // of_ClearState is NOT a reset: it clears the row-count accumulators only, and it runs exactly
        // once, before the describe [:L186].
        Assert.Equal(1, _target.ClearStateCalls);
    }

    #endregion

    #region Steps 7 and 8 - hook ordering and the defensive override [:L206-L210]

    /// <summary>
    /// THE AFTER-UPDATE HOOK OBSERVES THE PRE-OVERRIDE VALUE: with a SQL code of exactly <c>-1</c> and an
    /// update result of <c>1</c>, the hook sees <c>1</c> while the outcome is a failure.
    /// </summary>
    [Fact]
    public void AfterUpdateHookObservesTheValueBeforeTheDefensiveOverride()
    {
        _transaction.SqlCode = -1L;
        _target.UpdateResult = DataWindowBufferStore.DataStoreSuccess;

        UpdateOutcome outcome = _detector.Classify(Attempt());

        // `TransObject.Event OnAfterUpdate(Data,rtCode)` [:L206] fires BEFORE the rewrite at [:L208].
        Assert.Equal(DataWindowBufferStore.DataStoreSuccess, Assert.Single(_transaction.AfterUpdateResults));
        Assert.Equal(DataWindowBufferStore.DataStoreSuccess, outcome.UpdateResultObservedByHook);

        // The reconciled value, and the outcome built from it.
        Assert.True(outcome.DefensiveOverrideApplied);
        Assert.Equal(DataWindowBufferStore.DataStoreFailure, outcome.UpdateResult);
        Assert.Equal(UpdateOutcomeKind.DatabaseError, outcome.Kind);
        Assert.Equal(RetCode.E_DB_ERROR, outcome.Code);
        Assert.False(outcome.IsSucceeded);
    }

    /// <summary>
    /// THE WHOLE CALL SEQUENCE, ON THE SHARED ORDERED RECORDER: state is cleared, then the veto hook runs,
    /// then the update, then the after-update hook - and the hook is handed the UNRECONCILED value.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ORDER IS THE SUBJECT HERE, NOT CONTENT, so the assertion is made on the shared
    /// <c>ScriptedPooledTransaction</c>'s ordered journal rather than on a captured value: its
    /// <c>Precedes</c> answers "did A happen before B" directly, which is the only form in which
    /// <c>:L186</c> before <c>:L195</c> before <c>:L204</c> before <c>:L206</c> is checkable. A
    /// content-only spy would pass against any permutation of the four.
    /// </para>
    /// <para>
    /// AND THE SHARED DOUBLE'S <c>ClearState</c> IS THE FAITHFUL ONE - it zeroes the SQL state exactly as
    /// <c>of_clearstate</c> does [n_cst_thread_trans.sru:L363]. So the override's input has to be stamped
    /// through the UPDATE SEAM, which is where a real carrier stamps the driver's answer, and this case is
    /// therefore reachable by a composition that could actually exist in production.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheOrderedRecorderShowsClearThenVetoThenUpdateThenTheHook()
    {
        ScriptedPooledTransaction recorder = new();
        RecordingUpdateTarget target = new() { UpdateResult = DataWindowBufferStore.DataStoreSuccess };

        // THE DRIVER'S ANSWER ARRIVES WITH THE UPDATE, after the clear, which is when a real carrier learns
        // it. Written straight onto the code member the subject reads through IUpdateTransaction, so the
        // staging touches nothing outside the subject's own dependency surface.
        target.OnUpdateInvoked = () =>
            recorder.SqlCode = ConflictDetector.DefensiveOverrideSqlCode;

        UpdateOutcome outcome = _detector.Classify(
            new UpdateAttempt
            {
                Transaction = recorder,
                Target = target,
                Identity = new IdentityTableSurfaces(_surfaces, _surfaces),
                Errors = _sink,
                Cancellation = CancellationToken.None,
            });

        // `Data.of_ClearState()` [:L186] precedes the veto hook [:L195].
        Assert.True(recorder.Precedes(TransactionCallKind.ClearState, TransactionCallKind.BeforeUpdate));

        // The veto hook precedes the after-update hook [:L206], with the update in between [:L204].
        Assert.True(recorder.Precedes(TransactionCallKind.BeforeUpdate, TransactionCallKind.AfterUpdate));

        // Each hook fires EXACTLY ONCE per attempt.
        Assert.Equal(1, recorder.CountOf(TransactionCallKind.ClearState));
        Assert.Equal(1, recorder.CountOf(TransactionCallKind.BeforeUpdate));
        Assert.Equal(1, recorder.CountOf(TransactionCallKind.AfterUpdate));

        // THE HOOK OBSERVED THE PRE-OVERRIDE VALUE [:L206 before :L208].
        Assert.Equal(DataWindowBufferStore.DataStoreSuccess, recorder.AfterUpdateObserved);
        Assert.Equal(DataWindowBufferStore.DataStoreSuccess, outcome.UpdateResultObservedByHook);

        // AND THE OVERRIDE THEN FIRED on the stamped code, turning the claimed success into a failure.
        Assert.True(outcome.DefensiveOverrideApplied);
        Assert.Equal(DataWindowBufferStore.DataStoreFailure, outcome.UpdateResult);
        Assert.False(outcome.IsSucceeded);
    }

    /// <summary>
    /// The hook fires on EVERY invocation, including a failing one, and exactly once.
    /// </summary>
    [Fact]
    public void AfterUpdateHookFiresOnceEvenWhenTheUpdateFailed()
    {
        _target.UpdateResult = DataWindowBufferStore.DataStoreFailure;

        _detector.Classify(Attempt());

        Assert.Equal(DataWindowBufferStore.DataStoreFailure, Assert.Single(_transaction.AfterUpdateResults));
    }

    /// <summary>
    /// THE HEADLINE PARITY MATRIX'S DATA. Every SQL code that could plausibly be read as "the driver
    /// failed", paired with the update result and the reconciled answer the oracle produces.
    /// </summary>
    /// <returns>
    /// Rows of (SQL code, update result, override expected, reconciled result, success expected).
    /// </returns>
    /// <remarks>
    /// MEMBER DATA RATHER THAN INLINE DATA so the rows can carry the reasoning above them and so a
    /// future reader adding a code has ONE place to add it. The five distinct SQL codes here - <c>-1</c>,
    /// <c>-2</c>, <c>0</c>, <c>1</c> and <c>100</c> - are exactly the witnesses named in this file's
    /// header, and <c>100</c> is the one that is easiest to omit and most costly to omit.
    /// </remarks>
    public static TheoryData<long, long, bool, long, bool> DefensiveOverrideMatrix() =>
        new()
        {
            // EXACTLY -1 with a CLAIMED SUCCESS: the one combination that rewrites.
            { -1L, 1L, true, -1L, false },

            // -1 with a genuine failure: the second conjunct declines, so nothing is rewritten twice.
            { -1L, -1L, false, -1L, false },

            // -1 with a result that is neither: still no rewrite, and still not a success, because
            // success is the LITERAL 1 and 0 is not it.
            { -1L, 0L, false, 0L, false },

            // NEGATIVE BUT NOT -1. The failure predicate would accept this; :L208 does not.
            { -2L, 1L, false, 1L, true },
            { long.MinValue, 1L, false, 1L, true },

            // POSITIVE AND NON-ZERO. The transaction object's own override at
            // n_cst_thread_trans.sru:L275 WOULD rewrite for this; :L208 does not.
            { 1L, 1L, false, 1L, true },

            // 100 - THE VALUE THAT READS AS SUCCESS ELSEWHERE. n_cst_thread_trans.sru:L233 excludes it
            // from its non-zero test precisely because it is the not-found code rather than a fault, and
            // :L208 leaves a claimed success standing for it too.
            { 100L, 1L, false, 1L, true },

            // The ordinary healthy success, which must survive untouched.
            { 0L, 1L, false, 1L, true },
        };

    /// <summary>
    /// THE HEADLINE PARITY MATRIX. The override fires for EXACTLY <c>-1</c> paired with a CLAIMED
    /// SUCCESS, and for nothing else.
    /// </summary>
    /// <param name="sqlCode">The transaction's SQL code.</param>
    /// <param name="updateResult">The DataWindow update result.</param>
    /// <param name="expectedOverride">Whether the rewrite is expected to fire.</param>
    /// <param name="expectedResult">The reconciled result.</param>
    /// <param name="expectedSuccess">Whether the outcome succeeds.</param>
    /// <remarks>
    /// DO NOT WEAKEN THIS MATRIX, AND DO NOT REMOVE A ROW FROM IT. Each row rules out one specific
    /// misreading of <c>if TransObject.SQLCode = -1 and rtCode = 1</c> [<c>:L208</c>]:
    /// <list type="bullet">
    /// <item><description>
    /// <c>(-2, 1)</c> rules out reading the condition as "NEGATIVE". That reading is real - it is the
    /// transaction's own failure predicate [n_cst_thread_trans.sru:L337] - and it is a different test.
    /// </description></item>
    /// <item><description>
    /// <c>(1, 1)</c> and <c>(100, 1)</c> rule out reading it as "NON-ZERO". That reading is also real -
    /// it is the transaction object's own override [n_cst_thread_trans.sru:L275] and its state test
    /// [n_cst_thread_trans.sru:L233] - and <c>100</c> is the value that separates those two from each
    /// other, because :L233 excludes it and :L275 does not.
    /// </description></item>
    /// <item><description>
    /// <c>(-1, -1)</c> and <c>(-1, 0)</c> rule out dropping the SECOND conjunct. The override fires only
    /// on a CLAIMED SUCCESS, so a genuine failure is left exactly as the update reported it and is never
    /// rewritten twice.
    /// </description></item>
    /// <item><description>
    /// <c>(0, 1)</c> is the ordinary healthy success, which must survive untouched.
    /// </description></item>
    /// </list>
    /// See <see cref="ThreeNumericReadingsOfOneSqlCodeDivergeAndAreNeverMerged"/> for the matrix that
    /// asserts the three readings really are three.
    /// </remarks>
    [Theory]
    [MemberData(nameof(DefensiveOverrideMatrix))]
    public void OverrideFiresOnlyForExactlyMinusOnePairedWithAClaimedSuccess(
        long sqlCode,
        long updateResult,
        bool expectedOverride,
        long expectedResult,
        bool expectedSuccess)
    {
        _transaction.SqlCode = sqlCode;
        _target.UpdateResult = updateResult;

        UpdateOutcome outcome = _detector.Classify(Attempt());

        Assert.Equal(expectedOverride, outcome.DefensiveOverrideApplied);
        Assert.Equal(expectedResult, outcome.UpdateResult);
        Assert.Equal(updateResult, outcome.UpdateResultObservedByHook);
        Assert.Equal(expectedSuccess, outcome.IsSucceeded);
        Assert.Equal(
            expectedSuccess ? UpdateOutcomeKind.Succeeded : UpdateOutcomeKind.DatabaseError,
            outcome.Kind);
    }

    /// <summary>
    /// THE THREE READINGS OF ONE <c>SQLCode</c> FIELD, ASSERTED TO BE THREE. Every witness value where
    /// the readings disagree, so a future change that merges any two of them fails here.
    /// </summary>
    /// <returns>
    /// Rows of (SQL code, the exactly-minus-one reading, the non-zero-excluding-100 reading, the
    /// negativity reading).
    /// </returns>
    /// <remarks>
    /// THE ROWS ARE CHOSEN SO THAT NO TWO COLUMNS ARE EQUAL DOWN THE WHOLE MATRIX. That is the property
    /// that makes a merge detectable: if a maintainer replaced reading #1 with reading #2, the
    /// <c>1</c> and <c>-2</c> rows would fail; if they replaced #1 with #3, the <c>-2</c> row would fail;
    /// and if they replaced #2 with #3, the <c>1</c> and <c>100</c> rows would fail.
    /// </remarks>
    public static TheoryData<long, bool, bool, bool> SqlCodeReadingWitnesses() =>
        new()
        {
            //        code          #1 == -1   #2 <>0 and <>100   #3 < 0
            { -1L, true, true, true },
            { -2L, false, true, true },
            { long.MinValue, false, true, true },
            { 0L, false, false, false },
            { 1L, false, true, false },
            { 100L, false, false, false },
            { 101L, false, true, false },
        };

    /// <summary>
    /// The three numeric readings the oracle applies to <c>SQLCode</c> are genuinely DIFFERENT TESTS, and
    /// the subject's override consumes the narrowest of them.
    /// </summary>
    /// <param name="sqlCode">The witness code.</param>
    /// <param name="isExactlyMinusOne">
    /// Reading #1 - <c>SQLCode = -1</c> exactly [<c>:L208</c>], which is what the override under test uses.
    /// </param>
    /// <param name="isNonZeroExcludingNotFound">
    /// Reading #2 - <c>SQLCode &lt;&gt; 0 and SQLCode &lt;&gt; 100</c> [n_cst_thread_trans.sru:L233].
    /// </param>
    /// <param name="isNegative">
    /// Reading #3 - <c>SQLCode &lt; 0</c>, the failure predicate [n_cst_thread_trans.sru:L337].
    /// </param>
    /// <remarks>
    /// <para>
    /// 🔴 WHY THIS TEST EXISTS AT ALL (C-B, C-K). The three conditions look like three spellings of one
    /// idea - "the driver reported a problem" - and a maintainer tidying them into a shared helper would
    /// change behaviour that NO HAPPY-PATH TEST WOULD CATCH: every one of them answers the same thing for
    /// a healthy <c>0</c> and for the common <c>-1</c>, so a suite that exercised only those two values
    /// would still pass. The disagreement only shows at <c>1</c>, <c>-2</c> and <c>100</c>, which is
    /// exactly what this matrix pins.
    /// </para>
    /// <para>
    /// READINGS #2 AND #3 BELONG TO <c>n_cst_thread_trans</c>, A SIBLING PATH THE SUBJECT DOES NOT
    /// IMPLEMENT. They are asserted here against the shared transaction double - whose
    /// <c>IsSqlCodeNonZeroExcludingNotFound</c> and <c>FailurePredicate</c> are those two conditions -
    /// so that the record of their difference is executable rather than a comment, and so that the
    /// subject's own reading is shown to be the narrowest of the three rather than merely "one of them".
    /// </para>
    /// <para>
    /// THE SHARED DOUBLE IS USED HERE PRECISELY BECAUSE ITS <c>ClearState</c> IS FAITHFUL: it zeroes the
    /// SQL state exactly as the transaction object's own <c>of_clearstate</c> does
    /// [n_cst_thread_trans.sru:L363], which is what the subject invokes at STEP 3. The staging therefore
    /// goes through the UPDATE SEAM, which is where a real carrier stamps the driver's answer - so this
    /// case reaches the override with state that a production composition could actually present.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(SqlCodeReadingWitnesses))]
    public void ThreeNumericReadingsOfOneSqlCodeDivergeAndAreNeverMerged(
        long sqlCode,
        bool isExactlyMinusOne,
        bool isNonZeroExcludingNotFound,
        bool isNegative)
    {
        // The shared ordered call recorder, whose two exposed readings ARE conditions #1 and #2.
        ScriptedPooledTransaction shared = new() { SqlCode = sqlCode };

        Assert.Equal(isExactlyMinusOne, shared.IsSqlCodeDefensiveOverride());
        Assert.Equal(isNonZeroExcludingNotFound, shared.IsSqlCodeNonZeroExcludingNotFound());

        // Condition #3, read through the double's failure predicate - the same member the subject's veto
        // arm consumes as IUpdateTransaction.IsFailed.
        Assert.Equal(isNegative, shared.IsFailed());

        // AND #1 IS WHAT THE SUBJECT'S OVERRIDE ACTUALLY USES. Staged through the update seam, because
        // STEP 3 clears the transaction's state before the override ever reads it.
        _target.OnUpdateInvoked = () => _transaction.SqlCode = sqlCode;
        _target.UpdateResult = DataWindowBufferStore.DataStoreSuccess;

        UpdateOutcome outcome = _detector.Classify(Attempt());

        Assert.Equal(isExactlyMinusOne, outcome.DefensiveOverrideApplied);

        // THE SUBJECT'S READING IS THE NARROWEST OF THE THREE: whenever it fires, both of the others
        // would too - but not the converse, which is what the -2, 1 and 100 rows prove.
        if (isExactlyMinusOne)
        {
            Assert.True(isNonZeroExcludingNotFound);
            Assert.True(isNegative);
        }

        // The claimed success survives for every code except the one the override matches.
        Assert.Equal(!isExactlyMinusOne, outcome.IsSucceeded);
    }

    /// <summary>
    /// The subject's own override constant IS the oracle's <c>-1</c>, and it is CONSUMED from the shared
    /// kernel and the subject rather than re-typed as a local literal.
    /// </summary>
    /// <remarks>
    /// TWO SEPARATE FIELDS, NEVER INTERCHANGED. The override reads <c>SQLCode</c> [<c>:L208</c>] while
    /// the veto arm's payload reads <c>SQLDBCode</c> [<c>:L197</c>]; this asserts the subject's constant
    /// against the shared double's independent declaration of the same value, so the two cannot drift.
    /// </remarks>
    [Fact]
    public void TheOverrideConstantHasOneSpellingAcrossTheServiceAndItsDoubles()
    {
        Assert.Equal(-1L, ConflictDetector.DefensiveOverrideSqlCode);
        Assert.Equal(
            ConflictDetector.DefensiveOverrideSqlCode,
            ScriptedPooledTransaction.DefensiveOverrideSqlCode);

        // The DataWindow code space, which is NOT the return-code algebra: 1 is success and -1 failure,
        // and RetCode.OK is 0 and therefore NOT the success value here.
        Assert.Equal(1L, DataWindowBufferStore.DataStoreSuccess);
        Assert.Equal(-1L, DataWindowBufferStore.DataStoreFailure);
        Assert.NotEqual(RetCode.OK, DataWindowBufferStore.DataStoreSuccess);

        // AND THE ONE VALUE THAT READS AS SUCCESS AT n_cst_thread_trans.sru:L233 IS 100, not 0.
        Assert.Equal(100L, ScriptedPooledTransaction.NotFoundSqlCode);
    }

    #endregion

    #region Steps 2 and 9 - the two cancellation checks [:L182, :L212]

    /// <summary>
    /// Cancelled BEFORE the update: <c>CANCELLED</c>, and the update is NEVER invoked.
    /// </summary>
    [Fact]
    public void CancellationBeforeTheUpdateSkipsTheUpdateEntirely()
    {
        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();
        _cancellation = cancelled.Token;

        UpdateOutcome outcome = _detector.Classify(Attempt());

        Assert.Equal(UpdateOutcomeKind.Cancelled, outcome.Kind);
        Assert.Equal(RetCode.CANCELLED, outcome.Code);
        Assert.False(outcome.UpdateInvoked);
        Assert.Empty(_target.UpdateCalls);

        // The oracle returns before of_ClearState [:L186] and before the describe [:L188].
        Assert.Equal(0, _target.ClearStateCalls);
        Assert.Equal(0, _surfaces.DescribeUpdateTableCalls);
        Assert.Empty(_sink.Raises);
    }

    /// <summary>
    /// Cancelled ONLY AFTER the update: still <c>CANCELLED</c>, even though the rows were written and the
    /// update reported success [<c>:L212</c>].
    /// </summary>
    [Fact]
    public void CancellationAfterASuccessfulUpdateStillYieldsCancelled()
    {
        using CancellationTokenSource cancelled = new();
        _cancellation = cancelled.Token;

        // The flag flips DURING the update, so the first check passes and the second fails - which is the
        // only way to exercise the two checks independently.
        _target.OnUpdateInvoked = cancelled.Cancel;
        _target.UpdateResult = DataWindowBufferStore.DataStoreSuccess;
        _surfaces.Inserted = 3L;

        UpdateOutcome outcome = _detector.Classify(Attempt());

        Assert.Equal(UpdateOutcomeKind.Cancelled, outcome.Kind);
        Assert.Equal(RetCode.CANCELLED, outcome.Code);

        // The update DID run and DID succeed, and the hook saw it.
        Assert.True(outcome.UpdateInvoked);
        Assert.Single(_target.UpdateCalls);
        Assert.Equal(DataWindowBufferStore.DataStoreSuccess, outcome.UpdateResult);
        Assert.Single(_transaction.AfterUpdateResults);

        // THE SUCCESS PATH IS NOT TAKEN: no identity block and no counts report, even for written rows.
        Assert.Null(outcome.Identity);
        Assert.Empty(_sink.Raises);
    }

    /// <summary>
    /// The second cancellation check runs AFTER the defensive override, so a cancelled attempt still
    /// records that the rewrite fired.
    /// </summary>
    [Fact]
    public void CancellationAfterTheUpdateStillRecordsTheOverride()
    {
        using CancellationTokenSource cancelled = new();
        _cancellation = cancelled.Token;

        _target.OnUpdateInvoked = cancelled.Cancel;
        _transaction.SqlCode = -1L;
        _target.UpdateResult = DataWindowBufferStore.DataStoreSuccess;

        UpdateOutcome outcome = _detector.Classify(Attempt());

        Assert.Equal(UpdateOutcomeKind.Cancelled, outcome.Kind);
        Assert.True(outcome.DefensiveOverrideApplied);
        Assert.Equal(DataWindowBufferStore.DataStoreFailure, outcome.UpdateResult);
        Assert.Equal(DataWindowBufferStore.DataStoreSuccess, outcome.UpdateResultObservedByHook);
    }

    #endregion

    #region Steps 10 and 11 - success is the literal 1, and the delegation [:L214-L248]

    /// <summary>
    /// SUCCESS IS THE LITERAL <c>1</c>. Zero is a FAILURE here, which is the opposite of the return-code
    /// algebra, and <c>2</c> and <c>-1</c> are failures too.
    /// </summary>
    /// <param name="updateResult">The DataWindow update result.</param>
    /// <param name="expectSuccess">
    /// Whether the oracle's gate admits it. The parameter is a <see cref="bool"/> rather than the outcome
    /// kind because the kind is <c>internal</c> - reached through <c>InternalsVisibleTo</c> - and a public
    /// theory method cannot declare a less accessible parameter type.
    /// </param>
    [Theory]
    [InlineData(1L, true)]
    [InlineData(0L, false)]
    [InlineData(2L, false)]
    [InlineData(-1L, false)]
    [InlineData(long.MaxValue, false)]
    public void OnlyTheLiteralOneIsSuccess(long updateResult, bool expectSuccess)
    {
        _target.UpdateResult = updateResult;

        UpdateOutcome outcome = _detector.Classify(Attempt());

        Assert.Equal(
            expectSuccess ? UpdateOutcomeKind.Succeeded : UpdateOutcomeKind.DatabaseError,
            outcome.Kind);
        Assert.Equal(expectSuccess ? RetCode.OK : RetCode.E_DB_ERROR, outcome.Code);

        // The raw DataWindow result must never be handed to a return-code predicate: IsSucceeded tests
        // >= 0, so it reads 0 and 2 as successes while the oracle reads both as failures.
        Assert.True(Predicates.IsSucceeded(0L));
        Assert.True(Predicates.IsSucceeded(2L));
    }

    /// <summary>
    /// The success arm delegates the identity round trip and the counts report to the sibling resolver,
    /// and the counts are present on EVERY success - including one with no inserted rows [<c>:L247</c>].
    /// </summary>
    [Fact]
    public void SuccessDelegatesTheCountsReportEvenWithNoInsertedRows()
    {
        _surfaces.Inserted = 0L;
        _surfaces.Updated = 7L;
        _surfaces.Deleted = 2L;

        UpdateOutcome outcome = _detector.Classify(Attempt());

        Assert.Equal(UpdateOutcomeKind.Succeeded, outcome.Kind);
        Assert.Equal(RetCode.OK, outcome.Code);
        Assert.True(outcome.IsSucceeded);
        Assert.False(outcome.RequiresRollback);

        IdentityResolutionOutcome identity = Required(outcome.Identity);
        Assert.Equal(0L, identity.Counts.Inserted);
        Assert.Equal(7L, identity.Counts.Updated);
        Assert.Equal(2L, identity.Counts.Deleted);

        // No inserted rows means the oracle would have fired NO identity callback [:L215].
        Assert.Empty(identity.Identity);

        Assert.Null(outcome.DbError);
        Assert.Empty(_sink.Raises);
        Assert.Equal(DataWindowBufferStore.DataStoreSuccess, outcome.UpdateResult);
    }

    #endregion

    #region The else arm and the NET-NEW conflict discrimination [:L249-L250]

    /// <summary>
    /// The else arm RAISES NOTHING and carries the transaction's own code and text as DATA, which is what
    /// lets the caller's epilogue decide whether to report it [<c>:L397</c>].
    /// </summary>
    [Fact]
    public void TheElseArmRaisesNothingAndCarriesTheTransactionsCodeAndText()
    {
        // ⚠ THE CODE CHOSEN HERE MATTERS. This row used to send SQLITE_CONSTRAINT_PRIMARYKEY (1555),
        // which is now RECLASSIFIED as a caller payload fault - see the constraint rows below - so the
        // else arm is exercised with a genuine SERVER-SIDE failure instead. SQLITE_IOERR is exactly that:
        // nothing a corrected payload could avoid.
        _target.UpdateResult = DataWindowBufferStore.DataStoreFailure;
        _transaction.SqlDbCode = RetCode.SQLITE_IOERR;
        _transaction.SqlErrText = "disk I/O error";

        UpdateOutcome outcome = _detector.Classify(Attempt());

        Assert.Equal(UpdateOutcomeKind.DatabaseError, outcome.Kind);
        Assert.Equal(RetCode.E_DB_ERROR, outcome.Code);

        // The oracle raises nothing at [:L250].
        Assert.Empty(_sink.Raises);
        Assert.False(outcome.ErrorReported);
        Assert.Equal(string.Empty, outcome.ErrorText);

        DbErrorData payload = RequiredValue(outcome.DbError);
        Assert.Equal(RetCode.SQLITE_IOERR, payload.SqlDbCode);
        Assert.Equal("disk I/O error", payload.SqlErrText);
        Assert.Equal(DwBuffer.Primary, payload.Buffer);
        Assert.Equal(0L, payload.Row);
        Assert.True(outcome.UpdateInvoked);
    }

    /// <summary>
    /// A constraint the CALLER's payload controls is classified as a payload fault, not a database error.
    /// </summary>
    /// <param name="sqlDbCode">The driver's own result code.</param>
    /// <param name="providerText">The message the provider reported.</param>
    /// <remarks>
    /// <para>
    /// 🔴 <b>THE FINDING.</b> A row omitting a <c>NOT NULL</c> column reached the caller as
    /// <c>E_DB_ERROR</c>, which Gateway correctly publishes as HTTP 502 - so a caller who forgot a
    /// required field was told the DATABASE had failed and that the fault lay behind the gateway. The
    /// DataWindow definition declares no required flag on any column, so this refusal is the ONLY place
    /// in the system that knows the column is required, which is why the classification has to happen
    /// here rather than in a pre-check further out.
    /// </para>
    /// <para>
    /// THE DRIVER PAYLOAD STILL TRAVELS, because it is what names the offending column. That is asserted
    /// too: an outcome carrying the right code and no payload would leave a caller with a 400 and no way
    /// to tell WHICH column was rejected, which is half the defect unfixed.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(RetCode.SQLITE_CONSTRAINT_NOTNULL, "NOT NULL constraint failed: COMPANY.NAME")]
    [InlineData(RetCode.SQLITE_CONSTRAINT_UNIQUE, "UNIQUE constraint failed: COMPANY.id")]
    [InlineData(RetCode.SQLITE_CONSTRAINT_PRIMARYKEY, "UNIQUE constraint failed: COMPANY.id")]
    [InlineData(RetCode.SQLITE_CONSTRAINT_FOREIGNKEY, "FOREIGN KEY constraint failed")]
    [InlineData(RetCode.SQLITE_MISMATCH, "datatype mismatch")]
    public void ACallerControlledConstraintRefusalIsAPayloadFaultAndNotADatabaseError(
        long sqlDbCode,
        string providerText)
    {
        _target.UpdateResult = DataWindowBufferStore.DataStoreFailure;
        _transaction.SqlDbCode = sqlDbCode;
        _transaction.SqlErrText = providerText;

        UpdateOutcome outcome = _detector.Classify(Attempt());

        Assert.Equal(UpdateOutcomeKind.ConstraintViolation, outcome.Kind);
        Assert.Equal(RetCode.E_INVALID_DATA, outcome.Code);

        // The oracle raises nothing on this arm either, and the epilogue decides [:L397].
        Assert.Empty(_sink.Raises);
        Assert.False(outcome.ErrorReported);
        Assert.Equal(string.Empty, outcome.ErrorText);

        // THE IDENTITY OF THE FAILING COLUMN TRAVELS. Without the payload a caller gets a 400 that does
        // not say which column, which is the other half of the reported defect.
        DbErrorData payload = RequiredValue(outcome.DbError);
        Assert.Equal(sqlDbCode, payload.SqlDbCode);
        Assert.Equal(providerText, payload.SqlErrText);
        Assert.True(outcome.UpdateInvoked);
    }

    /// <summary>
    /// A constraint the caller CANNOT correct stays a database error.
    /// </summary>
    /// <param name="sqlDbCode">The driver's own result code.</param>
    /// <remarks>
    /// <b>THE CONTROL THAT KEEPS THE RECLASSIFICATION HONEST.</b> A check constraint, a trigger, a commit
    /// hook, a function or a virtual-table refusal is the SCHEMA's own logic failing - a caller cannot
    /// read the predicate and cannot know what would satisfy it - so blaming them with a 400 would invite
    /// a retry that can never converge. The BARE constraint code is here for a different reason: a
    /// provider that reports only the base code has not said WHICH constraint failed, so treating it as a
    /// caller fault would be a guess (AAP 0.1.5).
    /// </remarks>
    [Theory]
    [InlineData(RetCode.SQLITE_CONSTRAINT)]
    [InlineData(RetCode.SQLITE_CONSTRAINT_CHECK)]
    [InlineData(RetCode.SQLITE_CONSTRAINT_TRIGGER)]
    [InlineData(RetCode.SQLITE_CONSTRAINT_COMMITHOOK)]
    [InlineData(RetCode.SQLITE_CONSTRAINT_FUNCTION)]
    [InlineData(RetCode.SQLITE_CONSTRAINT_VTAB)]
    [InlineData(RetCode.SQLITE_IOERR)]
    [InlineData(RetCode.SQLITE_BUSY)]
    [InlineData(RetCode.SQLITE_READONLY)]
    public void AConstraintTheCallerCannotCorrectStaysADatabaseError(long sqlDbCode)
    {
        _target.UpdateResult = DataWindowBufferStore.DataStoreFailure;
        _transaction.SqlDbCode = sqlDbCode;
        _transaction.SqlErrText = "a server-side refusal";

        UpdateOutcome outcome = _detector.Classify(Attempt());

        Assert.Equal(UpdateOutcomeKind.DatabaseError, outcome.Kind);
        Assert.Equal(RetCode.E_DB_ERROR, outcome.Code);
    }

    /// <summary>
    /// A measured concurrency mismatch classifies as the CONFLICT outcome, carrying the current row state
    /// - and it keeps the legacy return code, because the narrowing changes no legacy arm.
    /// </summary>
    [Fact]
    public void AMeasuredMismatchClassifiesAsAConflictAndKeepsTheLegacyCode()
    {
        _target.UpdateResult = DataWindowBufferStore.DataStoreFailure;

        ConflictRow row = BuildFixtureConflictRow();

        UpdateOutcome outcome = _detector.Classify(
            Attempt(
                evidence: new ConcurrencyEvidence
                {
                    RowsExpected = 1L,
                    RowsMatched = 0L,
                    ProviderFaulted = false,
                    Rows = [row],
                }));

        Assert.Equal(UpdateOutcomeKind.Conflict, outcome.Kind);

        // THE CODE IS UNCHANGED FROM THE ARM THIS NARROWS.
        Assert.Equal(RetCode.E_DB_ERROR, outcome.Code);
        Assert.False(outcome.ErrorReported);
        Assert.Empty(_sink.Raises);

        ConflictDetail detail = Required(outcome.Conflict);
        Assert.Equal(FakeUpdateSurfaces.FixtureTableName, detail.UpdateTable);
        Assert.Equal(1L, detail.RowsExpected);
        Assert.Equal(0L, detail.RowsMatched);

        // NEVER EMPTY ON AN ABORTED RESPONSE, and both value sets travel so a caller can tell WHICH
        // column moved underneath it - which is what updatewhere=1 requires [dw_sqlite.srd:L14].
        ConflictRow reported = Assert.Single(detail.Rows);
        Assert.Equal(row.Row, reported.Row);
        Assert.Equal(row.Buffer, reported.Buffer);
        Assert.NotEmpty(reported.CurrentValues);
        Assert.Equal(reported.CurrentValues.Count, reported.OriginalValues.Count);
    }

    /// <summary>
    /// THE ROWS ARE CLONED, so mutating the evidence after classification cannot change the response.
    /// </summary>
    [Fact]
    public void ConflictRowsAreClonedSoLaterMutationCannotChangeTheResponse()
    {
        _target.UpdateResult = DataWindowBufferStore.DataStoreFailure;

        ConflictRow row = BuildFixtureConflictRow();

        UpdateOutcome outcome = _detector.Classify(
            Attempt(
                evidence: new ConcurrencyEvidence
                {
                    RowsExpected = 1L,
                    RowsMatched = 0L,
                    Rows = [row],
                }));

        row.Row = 999L;
        row.CurrentValues.Clear();

        ConflictRow reported = Assert.Single(Required(outcome.Conflict).Rows);
        Assert.NotEqual(999L, reported.Row);
        Assert.NotEmpty(reported.CurrentValues);
    }

    /// <summary>
    /// GENUINE DATABASE ERRORS ARE NOT SWALLOWED. Each of the four declining conditions leaves the outcome
    /// as the database error the oracle reports.
    /// </summary>
    /// <param name="rowsExpected">The targeted row count.</param>
    /// <param name="rowsMatched">The matched row count.</param>
    /// <param name="providerFaulted">
    /// Whether the provider itself faulted - a constraint violation, a connection fault or a syntax error.
    /// </param>
    /// <param name="withRows">Whether any row state was captured.</param>
    [Theory]
    // A constraint violation: the provider faulted, and the shortfall is incidental.
    [InlineData(1L, 0L, true, true)]
    // A connection fault: the provider faulted and nothing matched.
    [InlineData(5L, 0L, true, true)]
    // A syntax error: the provider faulted and nothing was even targeted.
    [InlineData(0L, 0L, true, false)]
    // Nothing was targeted, so nothing could have been missed.
    [InlineData(0L, 0L, false, true)]
    // Every targeted row matched - not a mismatch at all.
    [InlineData(1L, 1L, false, true)]
    // MORE rows matched than were targeted - a statement-construction fault, not a retryable conflict.
    [InlineData(1L, 2L, false, true)]
    // A shortfall with no captured rows is not reportable: an Aborted with an empty row list would tell
    // the caller nothing it could act on.
    [InlineData(1L, 0L, false, false)]
    public void GenuineDatabaseErrorsAreNotClassifiedAsConflicts(
        long rowsExpected,
        long rowsMatched,
        bool providerFaulted,
        bool withRows)
    {
        _target.UpdateResult = DataWindowBufferStore.DataStoreFailure;
        _transaction.SqlDbCode = 19L;
        _transaction.SqlErrText = "provider reported a failure";

        UpdateOutcome outcome = _detector.Classify(
            Attempt(
                evidence: new ConcurrencyEvidence
                {
                    RowsExpected = rowsExpected,
                    RowsMatched = rowsMatched,
                    ProviderFaulted = providerFaulted,
                    Rows = withRows ? [BuildFixtureConflictRow()] : [],
                }));

        Assert.Equal(UpdateOutcomeKind.DatabaseError, outcome.Kind);
        Assert.Equal(RetCode.E_DB_ERROR, outcome.Code);
        Assert.Null(outcome.Conflict);

        // The legacy's own code and text survive, which is the whole point of not swallowing it.
        DbErrorData payload = RequiredValue(outcome.DbError);
        Assert.Equal(19L, payload.SqlDbCode);
        Assert.Equal("provider reported a failure", payload.SqlErrText);
    }

    /// <summary>
    /// 🔴 A MEASURED MISMATCH IS A CONFLICT EVEN WHEN THE UPDATE CLAIMED SUCCESS - and the measurement is
    /// still ignored on every arm that never reached a statement.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE FIRST ASSERTION IS THE WHOLE POINT AND IT IS THE ONE A CLASSIFIER GETS WRONG.
    /// <c>Data.Update(true,false)</c> answers <c>1</c> whenever every generated statement executed without
    /// a DBMS ERROR, and a statement that matched ZERO rows is not a DBMS error on any provider - so the
    /// classic optimistic miss arrives wearing a CLAIMED SUCCESS. Classifying the measurement only on the
    /// failure arm would report that miss as a success, publish the identity round trip and the counts, and
    /// let the caller's epilogue COMMIT: the caller would be told its edit applied when nothing was
    /// written.
    /// </para>
    /// <para>
    /// THE REMAINING ASSERTIONS ARE THE OTHER HALF OF THE SAME CLAIM. An arm that returns before a
    /// statement is generated has nothing to measure, so a populated measurement must not promote it: a
    /// failed acquisition, a sentinel update table and a clean veto each keep the outcome the oracle gives
    /// them.
    /// </para>
    /// </remarks>
    [Fact]
    public void AMeasuredMismatchNarrowsAClaimedSuccessButNeverAPreStatementArm()
    {
        ConcurrencyEvidence mismatch = new()
        {
            RowsExpected = 1L,
            RowsMatched = 0L,
            Rows = [BuildFixtureConflictRow()],
        };

        // A CLAIMED SUCCESS OVER A ZERO-ROW MATCH IS A CONFLICT, NOT A SUCCESS.
        _target.UpdateResult = DataWindowBufferStore.DataStoreSuccess;

        UpdateOutcome narrowed = _detector.Classify(Attempt(evidence: mismatch));

        Assert.Equal(UpdateOutcomeKind.Conflict, narrowed.Kind);

        // THE LEGACY CODE IS UNCHANGED, so the epilogue rolls back rather than commits.
        Assert.Equal(RetCode.E_DB_ERROR, narrowed.Code);
        Assert.False(narrowed.IsSucceeded);
        Assert.True(narrowed.RequiresRollback);

        // NOTHING IS PUBLISHED FROM A CONFLICT: no identity round trip and no counts report.
        Assert.Null(narrowed.Identity);
        Assert.NotNull(narrowed.Conflict);

        // AND THE MEASUREMENT WAS READ AFTER THE UPDATE RAN, which is the only moment it exists. The fake
        // answers null until the update has been invoked, so a classifier reading it earlier would have
        // seen nothing and this assertion would not hold.
        Assert.Single(_target.UpdateCalls);
        Assert.True(_target.CaptureConcurrencyEvidenceCalls >= 1);

        // A sentinel.
        _surfaces.UpdateTable = "!";
        Assert.Equal(
            UpdateOutcomeKind.DatabaseError,
            _detector.Classify(Attempt(evidence: mismatch)).Kind);
        _surfaces.UpdateTable = FakeUpdateSurfaces.FixtureTableName;

        // A clean veto.
        _transaction.BeforeUpdateResult = RetCode.PREVENT;
        Assert.Equal(
            UpdateOutcomeKind.Cancelled,
            _detector.Classify(Attempt(evidence: mismatch)).Kind);
        _transaction.BeforeUpdateResult = RetCode.OK;

        // A failed acquisition.
        Assert.Equal(
            UpdateOutcomeKind.InvalidTransaction,
            _detector.Classify(Attempt(transaction: false, evidence: mismatch)).Kind);
    }

    /// <summary>
    /// The mismatch predicate itself, including the absent-evidence decline.
    /// </summary>
    [Fact]
    public void MismatchPredicateDeclinesAbsentEvidence() =>
        Assert.False(ConflictDetector.IsConcurrencyMismatch(null));

    /// <summary>
    /// A ROW THE PAYLOAD FLAGGED MODIFIED WITHOUT SUPPLYING AN UPDATABLE COLUMN VALUE IS THE CALLER'S
    /// FAULT, AND IT IS ANSWERED BEFORE THE CONFLICT TEST.
    /// </summary>
    [Fact]
    public void APayloadWithNoAssignableValuesIsAnInvalidDataRefusalRatherThanAConflict()
    {
        // Nothing ran for such a row, so it can neither have been overwritten nor have lost a race.
        // Classifying it as a concurrency mismatch told the caller another writer had changed a row that
        // nothing had touched, and no amount of re-reading and rebasing could ever make the same payload
        // apply.
        UpdateOutcome outcome = _detector.Classify(Attempt(evidence: new ConcurrencyEvidence
        {
            RowsExpected = 0L,
            RowsMatched = 0L,
            RowsWithoutAssignableValues = 1L,
        }));

        Assert.Equal(UpdateOutcomeKind.InvalidUpdateData, outcome.Kind);
        Assert.Equal(RetCode.E_INVALID_DATA, outcome.Code);

        // NO CONFLICT PAYLOAD AND NO DATABASE PAYLOAD: there is no contended row to describe and no
        // statement ever reached the storage engine.
        Assert.Null(outcome.Conflict);
        Assert.Null(outcome.DbError);

        // The general channel IS raised, because the condition is invisible in the row counts - an
        // operator with no record could not tell this refusal from an empty changeset. And ONLY the
        // general channel: there is no driver payload to put on the database one.
        Assert.True(outcome.ErrorReported);
        RecordedRaise raise = Assert.Single(_sink.Raises);
        Assert.Equal("OnError", raise.Channel);
        Assert.Equal(RetCode.E_INVALID_DATA, raise.Code);
        Assert.Equal(outcome.ErrorText, raise.ErrorText);

        // THE DIAGNOSTIC NAMES THE RULE AND THE COUNT AND NOTHING ELSE (C-F).
        Assert.Contains("no updatable column value", raise.ErrorText, StringComparison.Ordinal);
        Assert.Contains("1 row(s)", raise.ErrorText, StringComparison.Ordinal);
        Assert.DoesNotContain("SELECT", raise.ErrorText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE ", raise.ErrorText, StringComparison.OrdinalIgnoreCase);

        // And the epilogue rolls back, because a payload carrying one contradictory row may carry rows
        // that did apply.
        Assert.False(outcome.IsSucceeded);
        Assert.True(outcome.RequiresRollback);
    }

    /// <summary>
    /// The payload refusal wins over a shortfall measured in the same attempt.
    /// </summary>
    [Fact]
    public void APayloadRefusalIsTestedAheadOfAConcurrencyShortfall()
    {
        // A payload may carry one contradictory row AND one genuinely contended row. The contradiction is
        // the caller's own and is reported first, because a 409 would send the caller to re-read and
        // resend a payload that can never apply.
        UpdateOutcome outcome = _detector.Classify(Attempt(evidence: new ConcurrencyEvidence
        {
            RowsExpected = 1L,
            RowsMatched = 0L,
            RowsWithoutAssignableValues = 1L,
            Rows = [BuildFixtureConflictRow()],
        }));

        Assert.Equal(UpdateOutcomeKind.InvalidUpdateData, outcome.Kind);
        Assert.Null(outcome.Conflict);
    }

    /// <summary>
    /// The measurement's own predicate is unchanged: a payload refusal is not a mismatch.
    /// </summary>
    [Fact]
    public void TheMismatchPredicateIsUnaffectedByThePayloadRefusalCount()
    {
        // The two facts are independent and the predicate keeps answering the question it always answered.
        Assert.False(ConflictDetector.IsConcurrencyMismatch(new ConcurrencyEvidence
        {
            RowsExpected = 0L,
            RowsMatched = 0L,
            RowsWithoutAssignableValues = 3L,
        }));

        Assert.True(ConflictDetector.IsConcurrencyMismatch(new ConcurrencyEvidence
        {
            RowsExpected = 1L,
            RowsMatched = 0L,
            RowsWithoutAssignableValues = 1L,
            Rows = [BuildFixtureConflictRow()],
        }));
    }

    #endregion

    #region The projection onto the gRPC status

    /// <summary>
    /// A conflict projects onto <c>Aborted</c> plus the binary trailer, and the CLASSIFIER ITSELF NEVER
    /// THROWS.
    /// </summary>
    [Fact]
    public void ConflictProjectsOntoAbortedWithTheRichErrorTrailer()
    {
        _target.UpdateResult = DataWindowBufferStore.DataStoreFailure;

        UpdateOutcome outcome = _detector.Classify(
            Attempt(
                evidence: new ConcurrencyEvidence
                {
                    RowsExpected = 1L,
                    RowsMatched = 0L,
                    Rows = [BuildFixtureConflictRow()],
                }));

        Assert.True(ConflictDetector.TryProjectAborted(outcome, out RichErrorProjection projection));

        // Aborted is the canonical gRPC mapping to HTTP 409, and it is what the descriptor declares.
        Assert.Equal(StatusCode.Aborted, projection.Status.StatusCode);
        Assert.Equal(StatusCode.Aborted, ConflictDetector.RichErrorStatusCode);
        Assert.Equal(ConflictDetector.AbortedStatusDetail, projection.Status.Detail);

        // THE STATUS DETAIL QUOTES NO DATA - no table name, no column name, no row value.
        Assert.DoesNotContain(
            FakeUpdateSurfaces.FixtureTableName,
            projection.Status.Detail,
            StringComparison.Ordinal);

        // The trailer key comes from the Update method's own descriptor option, and the `-bin` suffix is
        // what makes gRPC transport the payload as binary rather than corrupting it as ASCII.
        Assert.EndsWith("-bin", ConflictDetector.RichErrorTrailerKey, StringComparison.Ordinal);

        byte[]? raw = projection.Trailers.GetValueBytes(ConflictDetector.RichErrorTrailerKey);
        RichErrorTrailer trailer = RichErrorTrailer.Parser.ParseFrom(Required(raw));

        Assert.Equal(RichErrorTrailer.DetailOneofCase.Conflict, trailer.DetailCase);
        Assert.Equal(outcome.Conflict, trailer.Conflict);

        // A client that only wants to CLASSIFY the failure need not decode the payload at all.
        Assert.Equal(RetCode.E_DB_ERROR, trailer.RetCode);
        Assert.Equal(WireRetCode.EDbError, outcome.WireCode);
    }

    /// <summary>
    /// Every other kind declines the projection, so a non-conflict is never reported as <c>Aborted</c>.
    /// </summary>
    [Fact]
    public void NonConflictOutcomesDeclineTheAbortedProjection()
    {
        // A success.
        Assert.False(
            ConflictDetector.TryProjectAborted(
                _detector.Classify(Attempt()),
                out RichErrorProjection succeeded));
        Assert.Equal(default, succeeded);

        // A database error.
        _target.UpdateResult = DataWindowBufferStore.DataStoreFailure;
        Assert.False(
            ConflictDetector.TryProjectAborted(_detector.Classify(Attempt()), out _));

        // A cancellation.
        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();
        _cancellation = cancelled.Token;
        Assert.False(
            ConflictDetector.TryProjectAborted(
                _detector.Classify(Attempt()),
                out _));
    }

    /// <summary>
    /// The four descriptor-binding validations, each reachable without a broken descriptor.
    /// </summary>
    [Fact]
    public void RichErrorBindingValidationRejectsEveryInconsistentShape()
    {
        // Absent, and present-but-keyless.
        Assert.Throws<InvalidOperationException>(
            () => ConflictDetector.ValidateRichErrorBinding(null));
        Assert.Throws<InvalidOperationException>(
            () => ConflictDetector.ValidateRichErrorBinding(new RichErrorBinding()));

        // A key that gRPC will not transport as binary.
        Assert.Throws<InvalidOperationException>(
            () => ConflictDetector.ValidateRichErrorBinding(
                new RichErrorBinding { TrailerKey = "powerframework-rich-error-v1" }));

        // A status code other than Aborted.
        Assert.Throws<InvalidOperationException>(
            () => ConflictDetector.ValidateRichErrorBinding(
                new RichErrorBinding
                {
                    TrailerKey = ConflictDetector.RichErrorTrailerKey,
                    GrpcStatusCode = (int)StatusCode.FailedPrecondition,
                }));

        // A payload type this projection does not serialize.
        Assert.Throws<InvalidOperationException>(
            () => ConflictDetector.ValidateRichErrorBinding(
                new RichErrorBinding
                {
                    TrailerKey = ConflictDetector.RichErrorTrailerKey,
                    GrpcStatusCode = (int)StatusCode.Aborted,
                    PayloadType = "common.v1.DbError",
                }));

        // The real one passes.
        RichErrorBinding valid = ConflictDetector.ValidateRichErrorBinding(
            new RichErrorBinding
            {
                TrailerKey = ConflictDetector.RichErrorTrailerKey,
                GrpcStatusCode = (int)StatusCode.Aborted,
                PayloadType = RichErrorTrailer.Descriptor.FullName,
            });

        Assert.Equal(ConflictDetector.RichErrorTrailerKey, valid.TrailerKey);
    }

    #endregion

    #region Redaction - one path, and it is the only route to the wire

    /// <summary>
    /// Every payload leaving the classifier has passed through the INJECTED redactor, even though both
    /// raising arms carry an empty statement.
    /// </summary>
    [Fact]
    public void EveryPayloadPassesThroughTheInjectedRedactor()
    {
        _surfaces.UpdateTable = string.Empty;

        _detector.Classify(Attempt());

        // The sentinel payload's statement is empty [:L190], so masking is a no-op - and the call is made
        // anyway, so a future arm that does carry a statement cannot bypass it. THE SHARED SEAM RECORDS
        // THE EMPTY INPUT rather than short-circuiting ahead of its log, which is what makes an
        // empty-statement raise provably on the same single path as a statement-bearing one.
        Assert.Equal(string.Empty, Assert.Single(_redactor.Statements));
        Assert.Equal(1, _redactor.CallCount);
    }

    /// <summary>
    /// A statement carrying a recognisable literal never leaves the redaction path unmasked, and the wire
    /// projection is the only route to a <c>DbError</c>.
    /// </summary>
    /// <param name="passThrough">
    /// When set, the INJECTED redactor masks nothing - and the wire projection still masks, because it
    /// applies its own policy rather than the injected one.
    /// </param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AStatementLiteralNeverReachesTheWireUnmasked(bool passThrough)
    {
        const string Literal = "SuperSecretName";
        const string Statement =
            "UPDATE COMPANY SET name = 'SuperSecretName' WHERE id = 7 AND name = 'OldName'";

        // PASS-THROUGH IS EXPRESSED AS AN IDENTITY MASK, which is the shared seam's own hook for it: the
        // statement is still recorded, and it is returned unchanged, so the case models a registration
        // that masks NOTHING and proves the wire projection masks anyway.
        if (passThrough)
        {
            _redactor.Mask = static statement => statement;
        }

        DbErrorData masked = _detector.Redact(
            DbErrorData.FromStatement(-1L, "conflict", Statement, DwBuffer.Primary, 4L));

        // The injected redactor was consulted on the SINGLE path.
        Assert.Equal(Statement, Assert.Single(_redactor.Statements));

        // AND THE INJECTED SEAM'S OWN ANSWER IS WHAT LANDED ON THE PAYLOAD, which is what makes the seam
        // load-bearing rather than merely observed: with the identity mask the statement survives, and
        // with the default mask it is replaced.
        Assert.Equal(
            passThrough ? Statement : RecordingSqlRedactor.MaskedMarker,
            masked.SqlSyntax);

        // The other four members are copied through untouched (C-B).
        Assert.Equal(-1L, masked.SqlDbCode);
        Assert.Equal("conflict", masked.SqlErrText);
        Assert.Equal(DwBuffer.Primary, masked.Buffer);
        Assert.Equal(4L, masked.Row);

        // THE WIRE PROJECTION MASKS REGARDLESS, because it applies its own policy and takes no redactor -
        // so a pass-through registration cannot weaken it.
        UpdateOutcome outcome = UpdateOutcome.DatabaseError(masked, errorReported: false);

        Assert.True(outcome.TryProjectDbError(out DbError? wire));

        // NEITHER LITERAL REACHES THE WIRE ON EITHER PATH - which is the whole obligation (C-F).
        Assert.DoesNotContain(Literal, Required(wire).Sqlsyntax, StringComparison.Ordinal);
        Assert.DoesNotContain("OldName", wire!.Sqlsyntax, StringComparison.Ordinal);

        if (passThrough)
        {
            // THE INJECTED SEAM MASKED NOTHING, so the real statement reached the projection - and the
            // projection's OWN policy is what removed the literals. This is the case that proves the
            // second line of defence exists and is not merely a pass-along of the first.
            Assert.Contains(SqlRedactor.DefaultPlaceholder, wire.Sqlsyntax, StringComparison.Ordinal);
        }
        else
        {
            // THE INJECTED SEAM MASKED FIRST, so the projection received the seam's marker rather than a
            // statement. The marker survives verbatim, which is the observable that proves the injected
            // registration is the one that acted - a projection that re-derived its own masking from the
            // original would have produced the placeholder instead.
            Assert.Equal(RecordingSqlRedactor.MaskedMarker, wire.Sqlsyntax);
        }

        // The four unmasked members survive the projection.
        Assert.Equal(-1L, wire.Sqldbcode);
        Assert.Equal("conflict", wire.Sqlerrtext);
        Assert.Equal(DwBuffer.Primary, wire.Buffer);
        Assert.Equal(4L, wire.Row);
    }

    /// <summary>
    /// No credential-shaped value is ever echoed: the conflict payload has no statement field at all, and
    /// nothing in the outcome carries a connection parameter.
    /// </summary>
    [Fact]
    public void NoCredentialShapedValueIsEverEchoed()
    {
        const string Passphrase = "a-log-password-value";

        _target.UpdateResult = DataWindowBufferStore.DataStoreFailure;
        _transaction.SqlErrText = "unable to open database file";

        UpdateOutcome outcome = _detector.Classify(
            Attempt(
                evidence: new ConcurrencyEvidence
                {
                    RowsExpected = 1L,
                    RowsMatched = 0L,
                    Rows = [BuildFixtureConflictRow()],
                }));

        string rendered = Required(outcome.Conflict).ToString()
            + RequiredValue(outcome.DbError).ToString()
            + ConflictDetector.AbortedStatusDetail;

        Assert.DoesNotContain(Passphrase, rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("logpass", rendered, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", rendered, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// An outcome with no payload declines the wire projection rather than inventing an empty one.
    /// </summary>
    [Fact]
    public void AnOutcomeWithNoPayloadDeclinesTheWireProjection()
    {
        UpdateOutcome outcome = _detector.Classify(Attempt());

        Assert.False(outcome.TryProjectDbError(out DbError? wire));
        Assert.Null(wire);
    }

    /// <summary>
    /// EVERY ARM THAT PRODUCES A PAYLOAD PRODUCED IT THROUGH THE INJECTED REDACTOR. Driven across all
    /// four payload-bearing arms, so no unredacted route out of the subject exists (C-F).
    /// </summary>
    /// <param name="arm">Which payload-bearing arm to drive.</param>
    /// <remarks>
    /// <para>
    /// THE OBLIGATION IS ABOUT THE PATH, NOT ABOUT THE CONTENT. Three of these four arms carry an EMPTY
    /// statement, because that is what the oracle passes [<c>:L190</c>, <c>:L197</c>] - so masking them is
    /// a no-op and a content assertion would prove nothing. What matters is that the call is made ANYWAY,
    /// so that a future arm which does carry a statement inherits the single path instead of needing to
    /// remember it. The shared seam records an empty input, which is what makes "the call was made"
    /// observable at all.
    /// </para>
    /// <para>
    /// THE ARMS THAT PRODUCE NO PAYLOAD ARE COVERED BY THE COMPANION CASE BELOW, which asserts the seam
    /// is NOT reached for them - because an arm that redacted without producing anything would be a
    /// spurious call, not a safer one.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(PayloadBearingArms))]
    public void EveryPayloadBearingArmRoutesThroughTheInjectedRedactor(UpdateArm arm)
    {
        UpdateOutcome outcome = DriveArm(arm);

        Assert.Equal(KindOf(arm), outcome.Kind);

        // THE SEAM WAS REACHED, EXACTLY ONCE, FOR AN ARM THAT PRODUCES A PAYLOAD.
        Assert.Equal(1, _redactor.CallCount);

        // AND THE PAYLOAD THE ARM PUBLISHED IS THE REDACTED ONE. Every one of these arms passes an empty
        // statement, so the observable is that the statement is empty rather than absent - a payload built
        // around the seam would still carry whatever the caller supplied.
        DbErrorData payload = RequiredValue(outcome.DbError);

        Assert.Equal(string.Empty, payload.SqlSyntax);
        Assert.Equal(string.Empty, Assert.Single(_redactor.Statements));

        // NO ARM PUTS A STATEMENT ON THE CONFLICT PAYLOAD, because ConflictDetail HAS NO STATEMENT FIELD -
        // so the conflict route cannot leak one by construction. ASSERTED AGAINST THE GENERATED DESCRIPTOR
        // rather than against an instance, so it is the CONTRACT that is checked: a field added to
        // common.v1.ConflictDetail carrying statement text would fail here at once.
        if (outcome.Conflict is not null)
        {
            Assert.DoesNotContain(
                "sqlsyntax",
                ConflictDetail.Descriptor.Fields.InFieldNumberOrder().Select(field => field.Name),
                StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// The arms that produce NO payload do not reach the redaction seam at all - a spurious call is not a
    /// safer one.
    /// </summary>
    /// <param name="arm">Which payload-free arm to drive.</param>
    [Theory]
    [MemberData(nameof(PayloadFreeArms))]
    public void ArmsThatProduceNoPayloadDoNotReachTheRedactionSeam(UpdateArm arm)
    {
        UpdateOutcome outcome = DriveArm(arm);

        Assert.Equal(KindOf(arm), outcome.Kind);
        Assert.Null(outcome.DbError);
        Assert.Equal(0, _redactor.CallCount);
    }

    #endregion

    #region The whole-surface sweeps - reachability, exclusivity, no throw, no silent overwrite

    /// <summary>
    /// The four legacy outcome kinds, which are the ones this file's brief names.
    /// </summary>
    /// <returns>Success, clean cancellation, database error and concurrency conflict.</returns>
    /// <remarks>
    /// THE ORACLE HAS THREE OF THESE AND THE FOURTH IS THE MANDATED NARROWING. Success
    /// [<c>:L248</c>], cancellation [<c>:L182</c>, <c>:L201</c>, <c>:L212</c>] and database error
    /// [<c>:L192</c>, <c>:L199</c>, <c>:L250</c>] are the oracle's own arms; the conflict is the addition,
    /// and it deliberately carries the SAME return code as the database error so that no legacy arm
    /// changes.
    /// </remarks>
    public static TheoryData<UpdateArm> FourNamedOutcomes() =>
    [
        UpdateArm.Succeeded,
        UpdateArm.Cancelled,
        UpdateArm.DatabaseError,
        UpdateArm.Conflict,
    ];

    /// <summary>Every kind the subject can actually produce, the four named ones plus the three additions.</summary>
    /// <returns>The seven reachable kinds - <c>None</c> is deliberately absent.</returns>
    public static TheoryData<UpdateArm> EveryReachableOutcome() =>
    [
        UpdateArm.Succeeded,
        UpdateArm.Cancelled,
        UpdateArm.InvalidTransaction,
        UpdateArm.DatabaseError,
        UpdateArm.Conflict,
        UpdateArm.InvalidUpdateData,
        UpdateArm.ConstraintViolation,
    ];

    /// <summary>The arms that publish a <c>DbErrorData</c> payload.</summary>
    /// <returns>The payload-bearing kinds.</returns>
    public static TheoryData<UpdateArm> PayloadBearingArms() =>
    [
        UpdateArm.DatabaseError,
        UpdateArm.Conflict,
        UpdateArm.ConstraintViolation,
    ];

    /// <summary>The arms that publish no payload at all.</summary>
    /// <returns>The payload-free kinds.</returns>
    public static TheoryData<UpdateArm> PayloadFreeArms() =>
    [
        UpdateArm.Succeeded,
        UpdateArm.Cancelled,
        UpdateArm.InvalidTransaction,
        UpdateArm.InvalidUpdateData,
    ];

    /// <summary>
    /// THE FOUR NAMED OUTCOMES - success, clean cancellation, database error and concurrency conflict -
    /// are each reachable and each distinct, which is the discrimination this file's brief names.
    /// </summary>
    /// <param name="arm">The named outcome to reach.</param>
    /// <remarks>
    /// <para>
    /// THREE OF THE FOUR ARE THE ORACLE'S OWN ARMS AND THE FOURTH IS THE MANDATED NARROWING. The
    /// narrowing is what makes the distinctness worth asserting separately from the code: a conflict
    /// carries the SAME return code as a database error [<c>:L250</c>], deliberately, so that no legacy
    /// arm changes. The KIND is therefore the only thing that tells them apart, and a port that
    /// reconstructed the kind from the code could not.
    /// </para>
    /// <para>
    /// THE CANCELLATION IS THE CLEAN VETO HERE, one of its three arms - and it is worth restating that
    /// <c>CANCELLED</c> is NEITHER succeeded nor failed in the legacy algebra, so it is not a shading of
    /// either neighbour but a third state in its own right.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(FourNamedOutcomes))]
    public void TheFourNamedOutcomesAreEachReachableAndEachDistinct(UpdateArm arm)
    {
        UpdateOutcome outcome = DriveArm(arm);

        Assert.Equal(KindOf(arm), outcome.Kind);

        // Exactly one of the four reads as a success, and it is the one whose code is RetCode.OK.
        Assert.Equal(arm == UpdateArm.Succeeded, outcome.IsSucceeded);
        Assert.Equal(arm == UpdateArm.Succeeded, outcome.Code == RetCode.OK);

        // The conflict is the ONLY one of the four carrying a populated detail, and the only one that
        // projects onto a status other than the operation's own.
        Assert.Equal(arm == UpdateArm.Conflict, outcome.Conflict is not null);
        Assert.Equal(
            arm == UpdateArm.Conflict,
            ConflictDetector.TryProjectAborted(outcome, out _));

        // AND THE CONFLICT IS DISTINGUISHABLE FROM THE DATABASE ERROR BY KIND ALONE, because the two
        // share a code by design.
        if (arm is UpdateArm.Conflict or UpdateArm.DatabaseError)
        {
            Assert.Equal(RetCode.E_DB_ERROR, outcome.Code);
            Assert.True(outcome.RequiresRollback);
        }

        // The cancellation is neither succeeded nor failed in the legacy algebra - the tri-state hole.
        if (arm == UpdateArm.Cancelled)
        {
            Assert.False(Predicates.IsSucceeded(outcome.Code));
            Assert.False(Predicates.IsFailed(outcome.Code));
        }
    }

    /// <summary>
    /// Every outcome kind is REACHABLE through <c>Classify</c>, and each is MUTUALLY EXCLUSIVE of every
    /// other - one classification answers exactly one kind and one code.
    /// </summary>
    /// <param name="expected">The kind to reach.</param>
    /// <remarks>
    /// <para>
    /// EXCLUSIVITY IS ASSERTED BY ELIMINATION rather than by inspecting one field: every other kind is
    /// checked to be absent, so a future outcome type that reported two states at once - a flags
    /// enumeration, say, or a pair of independent booleans - would fail here. The kinds are NOT a severity
    /// ladder and are never compared with an inequality, which is why the elimination is written as a set
    /// difference rather than as a range.
    /// </para>
    /// <para>
    /// THE ZERO SLOT IS UNREACHABLE AND THAT IS THE POINT. <c>None</c> exists so a default-initialised
    /// value cannot masquerade as a real outcome, which matters here more than usual because the legacy's
    /// success code is <c>RetCode.OK</c> = 0 - so a zero slot named for success would make
    /// <c>default</c> read as a successful update.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(EveryReachableOutcome))]
    public void EveryOutcomeKindIsReachableAndMutuallyExclusive(UpdateArm arm)
    {
        UpdateOutcomeKind expected = KindOf(arm);
        UpdateOutcome outcome = DriveArm(arm);

        Assert.Equal(expected, outcome.Kind);
        Assert.NotEqual(UpdateOutcomeKind.None, outcome.Kind);

        // EXCLUSIVITY BY ELIMINATION - the outcome is not simultaneously anything else.
        foreach (UpdateOutcomeKind other in Enum.GetValues<UpdateOutcomeKind>())
        {
            if (other == expected)
            {
                continue;
            }

            Assert.NotEqual(other, outcome.Kind);
        }

        // ONE KIND MAPS ONTO ONE CODE, and the mapping is the oracle's. DatabaseError and Conflict share
        // E_DB_ERROR deliberately [:L250], which is exactly why the kind is carried alongside the code
        // rather than reconstructed from it.
        Assert.Equal(ExpectedCodeFor(expected), outcome.Code);

        // Success and failure are decided by the code, never by the kind's ordinal.
        Assert.Equal(expected == UpdateOutcomeKind.Succeeded, outcome.IsSucceeded);
        Assert.Equal(expected != UpdateOutcomeKind.Succeeded, outcome.RequiresRollback);
    }

    /// <summary>
    /// NO <c>RpcException</c> EVER ESCAPES THE DETECTOR, on any arm. The subject classifies and projects;
    /// <c>Grpc/UpdateService.cs</c> is the designated throw site and <c>Program.cs</c> owns the central
    /// mapping (C-K).
    /// </summary>
    /// <param name="arm">The arm to drive.</param>
    /// <remarks>
    /// <para>
    /// WHY THIS IS A TEST AND NOT A CONVENTION. A classifier that threw would decide the transport on its
    /// caller's behalf, and the caller here is a loop: the multi-table driver inspects the RETURNED CODE to
    /// decide whether to continue to the next table [<c>:L364-L369</c>], and an in-flight exception would
    /// bypass that decision entirely - silently turning a per-table outcome into a whole-update abort.
    /// So "returns data" is a behavioural requirement, not a style preference.
    /// </para>
    /// <para>
    /// THE CONFLICT ARM IS THE ONE AT RISK, because it is the arm whose eventual destination IS an
    /// <c>RpcException(Aborted)</c>. It still only produces a projection: the status and trailers come back
    /// as a value that the throw site raises.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(EveryReachableOutcome))]
    public void NoRpcExceptionEverEscapesTheDetector(UpdateArm arm)
    {
        UpdateOutcome outcome = DriveArm(arm);

        Assert.Equal(KindOf(arm), outcome.Kind);

        // The projection is a VALUE on every arm - it answers false rather than throwing for the six
        // non-conflict kinds, and answers a projection rather than throwing for the conflict.
        bool projected = ConflictDetector.TryProjectAborted(outcome, out RichErrorProjection projection);

        Assert.Equal(arm == UpdateArm.Conflict, projected);

        if (!projected)
        {
            Assert.Equal(default, projection);

            return;
        }

        // THE SANCTIONED PROJECTION IS THE ONE PLACE THE DATA BECOMES A STATUS, and it is still only data:
        // a Status and a Metadata, for the throw site to raise.
        Assert.Equal(StatusCode.Aborted, projection.Status.StatusCode);
        Assert.Equal(ConflictDetector.AbortedStatusDetail, projection.Status.Detail);
        Assert.NotNull(projection.Trailers.Get(ConflictDetector.RichErrorTrailerKey));

        // The canonical gRPC-to-HTTP mapping Gateway projects as 409.
        Assert.Equal(StatusCode.Aborted, ConflictDetector.RichErrorStatusCode);
    }

    /// <summary>
    /// THERE IS NO SILENT OVERWRITE ANYWHERE. No combination of update result, SQL code and measured
    /// shortfall yields a SUCCESS once a concurrency mismatch is evident (AAP 0.6.3.8).
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 THE DANGEROUS CASE IS A SHORTFALL THAT ARRIVES AS A CLAIMED SUCCESS, AND IT IS THE NORMAL CASE.
    /// A statement that matched ZERO rows is not a DBMS error on any provider - the driver returns a count
    /// of zero and raises nothing - so under <c>updatewhere=1</c> the classic optimistic miss reaches the
    /// classifier with the DataWindow's success value. A classifier that tested the result before the
    /// measurement would report it as a success, publish the counts, and let the caller's epilogue COMMIT.
    /// The row would not in fact have been overwritten, because the predicate carried the ORIGINAL values
    /// and therefore matched nothing - but the caller would be told its edit applied when it did not,
    /// which is the same class of harm and is what this sweep exists to prevent.
    /// </para>
    /// <para>
    /// THE SWEEP IS EXHAUSTIVE OVER THE CROSS PRODUCT of the update results and SQL codes that reach this
    /// point, so no single row can be "the one that got through". Every cell asserts the same three
    /// things: the kind is a conflict, the code is the LEGACY code rather than an invented one, and the
    /// detail carries the current row state a caller needs to choose between retrying and surfacing.
    /// </para>
    /// </remarks>
    [Fact]
    public void NoCombinationOfResultAndSqlCodeTurnsAMismatchIntoASuccess()
    {
        long[] updateResults =
        [
            DataWindowBufferStore.DataStoreSuccess,
            DataWindowBufferStore.DataStoreFailure,
            0L,
        ];

        long[] sqlCodes = [0L, -1L, -2L, 1L, 100L];

        foreach (long updateResult in updateResults)
        {
            foreach (long sqlCode in sqlCodes)
            {
                RunOneMismatch(updateResult, sqlCode);
            }
        }
    }

    /// <summary>
    /// Drives one mismatch cell of <see cref="NoCombinationOfResultAndSqlCodeTurnsAMismatchIntoASuccess"/>
    /// on FRESH fakes, so no cell can be contaminated by its predecessor.
    /// </summary>
    /// <param name="updateResult">The DataWindow update result.</param>
    /// <param name="sqlCode">The SQL code stamped during the update.</param>
    private static void RunOneMismatch(long updateResult, long sqlCode)
    {
        RecordingSqlRedactor redactor = new();
        ConflictDetector detector = new(redactor);
        RecordingErrorSink sink = new();
        RecordingTransaction transaction = new();
        FakeUpdateSurfaces surfaces = new();

        RecordingUpdateTarget target = new()
        {
            UpdateResult = updateResult,

            // A MEASURED SHORTFALL: two rows were expected to match and none did.
            Evidence = new ConcurrencyEvidence
            {
                RowsExpected = 2L,
                RowsMatched = 0L,
                Rows = [BuildFixtureConflictRow()],
            },
        };

        // STAGED THROUGH THE UPDATE SEAM, which is where a real carrier stamps the driver's answer, and
        // which is after the classification has cleared the transaction's state.
        target.OnUpdateInvoked = () => transaction.SqlCode = sqlCode;

        UpdateOutcome outcome = detector.Classify(
            new UpdateAttempt
            {
                Transaction = transaction,
                Target = target,
                Identity = new IdentityTableSurfaces(surfaces, surfaces),
                Errors = sink,
                Cancellation = CancellationToken.None,
            });

        // NEVER A SUCCESS, on any cell.
        Assert.NotEqual(UpdateOutcomeKind.Succeeded, outcome.Kind);
        Assert.False(outcome.IsSucceeded);
        Assert.NotEqual(RetCode.OK, outcome.Code);

        // ALWAYS THE CONFLICT NARROWING, carrying the LEGACY code rather than an invented one [:L250].
        Assert.Equal(UpdateOutcomeKind.Conflict, outcome.Kind);
        Assert.Equal(RetCode.E_DB_ERROR, outcome.Code);
        Assert.True(outcome.RequiresRollback);

        // AND THE DETAIL CARRIES THE CURRENT ROW STATE, which is what makes an explicit retry-or-surface
        // policy possible at all - a conflict with an empty detail would leave a caller with nothing to
        // decide on.
        ConflictDetail detail = Assert.IsType<ConflictDetail>(outcome.Conflict);

        Assert.Equal(2L, detail.RowsExpected);
        Assert.Equal(0L, detail.RowsMatched);
        Assert.Equal(FakeUpdateSurfaces.FixtureTableName, detail.UpdateTable);

        ConflictRow row = Assert.Single(detail.Rows);

        Assert.NotEmpty(row.CurrentValues);
        Assert.NotEmpty(row.OriginalValues);

        // The identity round trip and the counts report do NOT fire, because the success arm was not taken.
        Assert.Null(outcome.Identity);
    }

    /// <summary>
    /// Drives <c>Classify</c> down the arm that produces one specific outcome kind, on the shared fakes.
    /// </summary>
    /// <param name="kind">The kind to reach.</param>
    /// <returns>The outcome.</returns>
    /// <remarks>
    /// ONE PLACE THAT KNOWS HOW TO REACH EACH ARM, so the sweeps above assert on outcomes rather than each
    /// restating the staging. Every arm is reached by the SAME public entry point the production caller
    /// uses; nothing here reaches past <c>Classify</c> into an internal step.
    /// </remarks>
    private UpdateOutcome DriveArm(UpdateArm arm)
    {
        switch (arm)
        {
            case UpdateArm.Succeeded:
                break;

            case UpdateArm.Cancelled:
                // A CLEAN VETO [:L201] - one of the three arms that answer CANCELLED.
                _transaction.BeforeUpdateResult = RetCode.PREVENT;
                _transaction.SqlCode = 0L;
                break;

            case UpdateArm.InvalidTransaction:
                // No transaction at all [:L180].
                return _detector.Classify(Attempt(transaction: false));

            case UpdateArm.DatabaseError:
                // THE ELSE ARM [:L249-L250], with a driver code that is NOT a caller-correctable
                // constraint, so it stays an unspecific database error.
                _target.UpdateResult = DataWindowBufferStore.DataStoreFailure;
                _target.OnUpdateInvoked = () =>
                {
                    _transaction.SqlDbCode = RetCode.SQLITE_IOERR;
                    _transaction.SqlErrText = "disk I/O error";
                };
                break;

            case UpdateArm.Conflict:
                _target.Evidence = new ConcurrencyEvidence
                {
                    RowsExpected = 2L,
                    RowsMatched = 0L,
                    Rows = [BuildFixtureConflictRow()],
                };
                break;

            case UpdateArm.InvalidUpdateData:
                _target.Evidence = new ConcurrencyEvidence
                {
                    RowsExpected = 1L,
                    RowsMatched = 1L,
                    RowsWithoutAssignableValues = 1L,
                };
                break;

            case UpdateArm.ConstraintViolation:
                // A CALLER-CORRECTABLE constraint, which is split out of the database-error arm.
                _target.UpdateResult = DataWindowBufferStore.DataStoreFailure;
                _target.OnUpdateInvoked = () =>
                {
                    _transaction.SqlDbCode = RetCode.SQLITE_CONSTRAINT_NOTNULL;
                    _transaction.SqlErrText = "NOT NULL constraint failed: COMPANY.NAME";
                };
                break;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(arm),
                    arm,
                    "UpdateOutcomeKind.None is deliberately unreachable through the sanctioned factories, "
                        + "so no arm drives it.");
        }

        return _detector.Classify(Attempt(evidence: _target.Evidence));
    }

    /// <summary>
    /// Maps the public sweep discriminator onto the subject's internal outcome kind.
    /// </summary>
    /// <param name="arm">The arm.</param>
    /// <returns>The kind the subject must report for it.</returns>
    /// <remarks>
    /// THE SINGLE PLACE THE TWO ENUMERATIONS MEET, so an outcome kind added to the subject without a
    /// matching sweep member is caught here rather than being silently left unswept. The arms are mapped
    /// by NAME rather than by ordinal - a positional map would break silently the moment either
    /// enumeration gained a member in the middle.
    /// </remarks>
    private static UpdateOutcomeKind KindOf(UpdateArm arm) => arm switch
    {
        UpdateArm.Succeeded => UpdateOutcomeKind.Succeeded,
        UpdateArm.Cancelled => UpdateOutcomeKind.Cancelled,
        UpdateArm.InvalidTransaction => UpdateOutcomeKind.InvalidTransaction,
        UpdateArm.DatabaseError => UpdateOutcomeKind.DatabaseError,
        UpdateArm.Conflict => UpdateOutcomeKind.Conflict,
        UpdateArm.InvalidUpdateData => UpdateOutcomeKind.InvalidUpdateData,
        UpdateArm.ConstraintViolation => UpdateOutcomeKind.ConstraintViolation,
        _ => throw new ArgumentOutOfRangeException(
            nameof(arm),
            arm,
            "Every UpdateArm must name an outcome kind the subject can actually produce."),
    };

    /// <summary>
    /// The return code the oracle pairs with each outcome kind.
    /// </summary>
    /// <param name="kind">The kind.</param>
    /// <returns>The code, with its locator in the comment beside it.</returns>
    /// <remarks>
    /// A LOOKUP RATHER THAN A CALCULATION, because the mapping is NOT derivable in either direction: two
    /// kinds share <c>E_DB_ERROR</c> and two others share <c>E_INVALID_DATA</c>, so nothing about a code
    /// identifies a kind and the ordinal of a kind says nothing about its code.
    /// </remarks>
    private static long ExpectedCodeFor(UpdateOutcomeKind kind) => kind switch
    {
        UpdateOutcomeKind.Succeeded => RetCode.OK,                                    // [:L248]
        UpdateOutcomeKind.Cancelled => RetCode.CANCELLED,                             // [:L182,:L201,:L212]
        UpdateOutcomeKind.InvalidTransaction => RetCode.E_INVALID_TRANSACTION,        // [:L180]
        UpdateOutcomeKind.DatabaseError => RetCode.E_DB_ERROR,                        // [:L192,:L199,:L250]
        UpdateOutcomeKind.Conflict => RetCode.E_DB_ERROR,                             // the narrowing
        UpdateOutcomeKind.InvalidUpdateData => RetCode.E_INVALID_DATA,                // [:L343-L344]
        UpdateOutcomeKind.ConstraintViolation => RetCode.E_INVALID_DATA,              // [:L343-L344]
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "No code is paired with None."),
    };

    #endregion

    #region The multi-table shape [:L356-L369]

    /// <summary>
    /// Classifying twice in sequence produces INDEPENDENT outcomes with no accumulated state, and the
    /// caller can stop at the first failure.
    /// </summary>
    [Fact]
    public void ClassifyingTwiceProducesIndependentOutcomes()
    {
        // Table one succeeds.
        _surfaces.Inserted = 0L;
        _surfaces.Updated = 4L;

        UpdateOutcome first = _detector.Classify(Attempt());

        Assert.Equal(UpdateOutcomeKind.Succeeded, first.Kind);
        Assert.Equal(4L, Required(first.Identity).Counts.Updated);

        // Table two fails, exactly as the loop's second iteration would.
        _target.UpdateResult = DataWindowBufferStore.DataStoreFailure;
        _surfaces.Updated = 9L;

        UpdateOutcome second = _detector.Classify(Attempt());

        Assert.Equal(UpdateOutcomeKind.DatabaseError, second.Kind);

        // NOTHING ACCUMULATED: the first outcome is unchanged, and the second carries no counts of its own
        // because it did not succeed. ACCUMULATION IS THE CALLER'S JOB - the legacy proxy does it with
        // `+=` [n_cst_threading_task_sqlupdate.sru:L66-L68].
        Assert.Equal(4L, Required(first.Identity).Counts.Updated);
        Assert.Null(second.Identity);

        // Two independent invocations, and the loop's stop test is `!= RetCode.OK`.
        Assert.Equal(2, _target.UpdateCalls.Count);
        Assert.NotEqual(RetCode.OK, second.Code);
        Assert.Equal(RetCode.OK, first.Code);
    }

    #endregion

    #region Guards, arguments and the conflict-row projection

    /// <summary>
    /// The constructor and the classifier both reject a null argument, which is a STRUCTURAL fault.
    /// </summary>
    [Fact]
    public void NullArgumentsAreRejected()
    {
        Assert.Throws<ArgumentNullException>(() => new ConflictDetector(null!));
        Assert.Throws<ArgumentNullException>(() => _detector.Classify(null!));
        Assert.Throws<ArgumentNullException>(
            () => ConflictDetector.BuildConflictDetail(null!, "COMPANY"));
        Assert.Throws<ArgumentNullException>(
            () => ConflictDetector.ProjectConflictRow(null!, DwBuffer.Primary, 1L, []));
        Assert.Throws<ArgumentNullException>(
            () => ConflictDetector.ProjectConflictRow(new DataWindowBufferStore(), DwBuffer.Primary, 1L, null!));
        Assert.Throws<ArgumentNullException>(() => ConflictDetector.TryProjectAborted(null!, out _));
    }

    /// <summary>
    /// A CLASSIFIED CONFLICT NEVER THROWS. It is an expected data-level outcome with a defined response,
    /// so fail-fast does not apply to it.
    /// </summary>
    [Fact]
    public void AConflictIsClassifiedWithoutThrowing()
    {
        _target.UpdateResult = DataWindowBufferStore.DataStoreFailure;

        UpdateOutcome outcome = _detector.Classify(
            Attempt(
                evidence: new ConcurrencyEvidence
                {
                    RowsExpected = 2L,
                    RowsMatched = 1L,
                    Rows = [BuildFixtureConflictRow()],
                }));

        Assert.Equal(UpdateOutcomeKind.Conflict, outcome.Kind);
        Assert.Equal(2L, Required(outcome.Conflict).RowsExpected);
        Assert.Equal(1L, outcome.Conflict!.RowsMatched);
    }

    /// <summary>
    /// The conflict-row projection reports the CURRENT values from STORAGE and the ORIGINAL values from the
    /// buffer store, keeps null distinct from zero, and reads the ROW status with column index zero.
    /// </summary>
    /// <remarks>
    /// 🔴 THE TWO SIDES COME FROM TWO DIFFERENT PLACES, AND THAT IS THE CONTRACT. <c>current_values</c> is
    /// declared as the CURRENT SERVER-SIDE state a retry would be rebased onto, so it is read from storage;
    /// <c>original_values</c> is what the caller believed was current and literally what the failed
    /// <c>updatewhere=1</c> predicate carried, so it is read from the carrier. This exercise deliberately
    /// makes all three differ - carrier-current, carrier-original and storage - so that a projection which
    /// echoed the carrier's current value back as the server's state could not pass.
    /// </remarks>
    [Fact]
    public void ProjectConflictRowReportsStorageCurrentValuesAndCarrierOriginals()
    {
        DataWindowBufferStore store = new();

        store.AppendRow(DwBuffer.Primary, ItemStatus.NotModified);
        store.SetItemValue(1L, 1, DwBuffer.Primary, 7L);
        store.SetItemValue(1L, 2, DwBuffer.Primary, "original");

        // Baseline first, so the shadow starts from the retrieved state, THEN move the current value on -
        // the store captures the original exactly once per baseline period. The row status is set AFTER the
        // baseline because baselining deliberately clears it, which is the retrieved state it represents.
        store.RowAt(1L, DwBuffer.Primary).Baseline();
        store.SetItemValue(1L, 2, DwBuffer.Primary, "current");
        store.SetItemValue(1L, 3, DwBuffer.Primary, null);
        store.SetItemStatus(1L, 0, DwBuffer.Primary, ItemStatus.DataModified);

        // FIXTURE COLUMN NAMES [dw_sqlite.srd:L8-L13] - test data, never production data.
        ConflictColumn[] columns =
        [
            new ConflictColumn("id", 1),
            new ConflictColumn("name", 2),
            new ConflictColumn("address", 3),
        ];

        // WHAT STORAGE HOLDS - the winning writer's state, which is none of the three values above for
        // column 2 and is a genuine stored NULL for column 3.
        Dictionary<int, object?> storage = new()
        {
            [1] = 7L,
            [2] = "someone else",
            [3] = null,
        };

        ConflictRow projected =
            ConflictDetector.ProjectConflictRow(store, DwBuffer.Primary, 1L, columns, storage);

        Assert.Equal(DwBuffer.Primary, projected.Buffer);
        Assert.Equal(1L, projected.Row);

        // The ROW's status, read the legacy way at column index zero.
        Assert.Equal(ItemStatus.DataModified, projected.ItemStatus);

        Assert.Equal(3, projected.CurrentValues.Count);
        Assert.Equal(3, projected.OriginalValues.Count);

        Assert.Equal("name", projected.CurrentValues[1].ColumnName);
        Assert.Equal(2L, projected.CurrentValues[1].ColumnId);

        // 🔴 THE STORED VALUE, NOT THE CALLER'S SUBMITTED ONE. A caller rebasing a retry on "current"
        // would resubmit its own edit and conflict again for ever.
        Assert.Equal("someone else", projected.CurrentValues[1].Value.StringValue);

        // THE ORIGINAL-VALUE SHADOW IS WHAT updatewhere=1 COMPARED, read from the CARRIER, and it differs
        // from both the caller's current value and the stored one.
        Assert.Equal("original", projected.OriginalValues[1].Value.StringValue);

        // A column neither side changed answers the same value on both sides.
        Assert.Equal(7L, projected.CurrentValues[0].Value.Int64Value);
        Assert.Equal(7L, projected.OriginalValues[0].Value.Int64Value);

        // NULL IS A VALUE, NOT AN ABSENCE, and is never coerced to zero or empty - on either side.
        Assert.True(projected.CurrentValues[2].Value.IsNull);
        Assert.True(projected.CurrentValues[2].HasItemStatus);
        Assert.True(projected.OriginalValues[2].Value.IsNull);

        // AND WHEN STORAGE COULD NOT BE READ, NO CURRENT VALUE IS INVENTED. The other writer deleted the
        // row: the originals still travel so the caller can see what it believed, and the empty current set
        // is what distinguishes "the row is gone" from "the row changed".
        ConflictRow deleted =
            ConflictDetector.ProjectConflictRow(store, DwBuffer.Primary, 1L, columns);

        Assert.Empty(deleted.CurrentValues);
        Assert.Equal(3, deleted.OriginalValues.Count);
    }

    /// <summary>
    /// One-based row and column ordinals are enforced (R9), and a value the contract cannot express is a
    /// structural fault rather than a placeholder.
    /// </summary>
    [Fact]
    public void ProjectConflictRowEnforcesOneBasedOrdinalsAndRejectsInexpressibleValues()
    {
        DataWindowBufferStore store = new();
        store.AppendRow(DwBuffer.Primary, ItemStatus.NotModified);

        ConflictColumn[] valid = [new ConflictColumn("id", 1)];

        // Row 0 is not a row: every legacy row ordinal is one-based.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ConflictDetector.ProjectConflictRow(store, DwBuffer.Primary, 0L, valid));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ConflictDetector.ProjectConflictRow(store, DwBuffer.Primary, -1L, valid));

        // Column index 0 addresses the ROW, so it is not addressable as a column.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ConflictDetector.ProjectConflictRow(
                store,
                DwBuffer.Primary,
                1L,
                [new ConflictColumn("id", 0)]));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ConflictDetector.ProjectConflictRow(
                store,
                DwBuffer.Primary,
                1L,
                [new ConflictColumn("   ", 1)]));

        Assert.True(new ConflictColumn("id", 1).IsAddressable);
        Assert.False(new ConflictColumn("id", 0).IsAddressable);
        Assert.False(new ConflictColumn(string.Empty, 1).IsAddressable);

        // A runtime type outside the published value arms cannot be put on the contract, on EITHER side.
        // Storage first, because a stored value is the side a caller acts on.
        Assert.Throws<InvalidOperationException>(
            () => ConflictDetector.ProjectConflictRow(
                store,
                DwBuffer.Primary,
                1L,
                valid,
                new Dictionary<int, object?> { [1] = Guid.NewGuid() }));

        // And the carrier's own original-value shadow.
        store.SetItemValue(1L, 1, DwBuffer.Primary, 0L);
        store.RowAt(1L, DwBuffer.Primary).Baseline();
        store.SetItemValue(1L, 1, DwBuffer.Primary, Guid.NewGuid());
        store.RowAt(1L, DwBuffer.Primary).Baseline();
        store.SetItemValue(1L, 1, DwBuffer.Primary, 1L);

        Assert.Throws<InvalidOperationException>(
            () => ConflictDetector.ProjectConflictRow(store, DwBuffer.Primary, 1L, valid));
    }

    /// <summary>
    /// The reserved zero kind is never produced, and the four legacy codes are exactly the ones the oracle
    /// returns.
    /// </summary>
    [Fact]
    public void EveryProducedOutcomePairsItsKindWithTheOraclesCode()
    {
        Assert.Equal(RetCode.E_INVALID_TRANSACTION, UpdateOutcome.InvalidTransaction().Code);
        Assert.Equal(RetCode.CANCELLED, UpdateOutcome.Cancelled().Code);
        Assert.Equal(RetCode.E_DB_ERROR, UpdateOutcome.DatabaseError(null, errorReported: false).Code);
        Assert.Equal(
            RetCode.OK,
            UpdateOutcome.Succeeded(new IdentityResolutionOutcome()).Code);
        Assert.Equal(
            RetCode.E_DB_ERROR,
            UpdateOutcome.ConflictDetected(new ConflictDetail(), null).Code);

        Assert.Throws<ArgumentNullException>(() => UpdateOutcome.Succeeded(null!));
        Assert.Throws<ArgumentNullException>(() => UpdateOutcome.ConflictDetected(null!, null));

        // None is unreachable through the factories, which is exactly why it exists.
        Assert.NotEqual(UpdateOutcomeKind.None, UpdateOutcome.Cancelled().Kind);

        // The wire enum and the in-process algebra agree value for value.
        Assert.Equal(WireRetCode.Ok, UpdateOutcome.Succeeded(new IdentityResolutionOutcome()).WireCode);
        Assert.Equal(WireRetCode.Cancelled, UpdateOutcome.Cancelled().WireCode);
        Assert.Equal(
            WireRetCode.EInvalidTransaction,
            UpdateOutcome.InvalidTransaction().WireCode);
    }

    /// <summary>
    /// AN ABSENT UPDATE TABLE BECOMES AN EMPTY STRING ON EVERY PAYLOAD, never the text "null" and never a
    /// dereference - and a marked column the storage read did not answer projects a NULL VALUE rather than
    /// throwing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THESE ARE THE ARMS A CALLER REACHES WHEN SOMETHING WAS UNAVAILABLE, WHICH IS WHY THEY MATTER. The
    /// update table is unavailable on the paths that fail before the describe is meaningful; a marked
    /// column's storage value is unavailable when the row was DELETED underneath the caller rather than
    /// merely changed. Both are real states, and in both the payload must still be well formed - a
    /// consumer that received the string "null" as a table name, or a projection that threw partway
    /// through building a conflict row, would turn a recoverable answer into an unrecoverable one.
    /// </para>
    /// <para>
    /// THE EMPTY CURRENT VALUE PAIRED WITH A POPULATED ORIGINAL IS THE SIGNAL, and it is the same
    /// distinction the counts pair carries: an original with no current means the row is gone, whereas an
    /// original that differs from its current means the row moved.
    /// </para>
    /// </remarks>
    [Fact]
    public void AnAbsentUpdateTableAndAnUnreadableColumnBothProduceAWellFormedPayload()
    {
        ConcurrencyEvidence evidence = new()
        {
            RowsExpected = 1L,
            RowsMatched = 0L,
            Rows = [BuildFixtureConflictRow()],
        };

        // BuildConflictDetail with NO table name - the default arm of its nullable parameter.
        ConflictDetail detail = ConflictDetector.BuildConflictDetail(evidence, null);

        Assert.Equal(string.Empty, detail.UpdateTable);
        Assert.Equal(1L, detail.RowsExpected);
        Assert.Equal(0L, detail.RowsMatched);

        // The two payload-fault factories with their default table name, for the same reason.
        UpdateOutcome invalid = UpdateOutcome.InvalidUpdateData(1L);

        Assert.Equal(string.Empty, invalid.UpdateTable);
        Assert.Equal(RetCode.E_INVALID_DATA, invalid.Code);

        UpdateOutcome constraint = UpdateOutcome.ConstraintViolation(
            DbErrorData.FromTransaction(RetCode.SQLITE_CONSTRAINT_NOTNULL, "NOT NULL constraint failed"));

        Assert.Equal(string.Empty, constraint.UpdateTable);
        Assert.Equal(RetCode.E_INVALID_DATA, constraint.Code);

        // A MARKED COLUMN THE STORAGE READ DID NOT ANSWER. The dictionary is present - so a current set is
        // projected - but it omits column 2, which must therefore project a null value rather than fault.
        DataWindowBufferStore store = new();

        store.AppendRow(DwBuffer.Primary, ItemStatus.NotModified);
        store.SetItemValue(1L, 1, DwBuffer.Primary, 1L);
        store.SetItemValue(1L, 2, DwBuffer.Primary, "Paul");
        store.SetItemStatus(1L, 0, DwBuffer.Primary, ItemStatus.DataModified);

        ConflictRow row = ConflictDetector.ProjectConflictRow(
            store,
            DwBuffer.Primary,
            1L,
            [new ConflictColumn("id", 1), new ConflictColumn("name", 2)],
            new Dictionary<int, object?> { [1] = 1L });

        // BOTH marked columns appear in the current set; the unanswered one carries no value.
        Assert.Equal(2, row.CurrentValues.Count);
        Assert.Equal(2, row.OriginalValues.Count);

        ColumnValue unanswered = Assert.Single(row.CurrentValues, value => value.ColumnId == 2L);

        // AN EXPLICIT NULL, NOT AN ABSENT FIELD. The contract models "no value" as `is_null = true` rather
        // than as an unset oneof, so a consumer reading the payload can tell "the storage answered nothing"
        // apart from "this column was never asked about" - the latter would not appear in the set at all.
        Assert.Equal("name", unanswered.ColumnName);
        Assert.True(unanswered.Value.IsNull);

        // AND THE ANSWERED ONE STILL CARRIES ITS VALUE, so the null is attributable to the one column
        // rather than to the whole read having been abandoned.
        ColumnValue answered = Assert.Single(row.CurrentValues, value => value.ColumnId == 1L);

        Assert.False(answered.Value.IsNull);
        Assert.Equal(AnyValue.KindOneofCase.Int64Value, answered.Value.KindCase);
        Assert.Equal(1L, answered.Value.Int64Value);
    }

    /// <summary>
    /// Builds one conflicting row over the fixture's shape - TEST DATA, and the only place a table or
    /// column name appears in this exercise.
    /// </summary>
    /// <returns>The row.</returns>
    /// <remarks>
    /// THE CURRENT VALUES ARE SUPPLIED AS STORAGE VALUES, which is the only way a real executor produces
    /// them: the carrier holds what the CALLER submitted, so the payload's current set has to come from a
    /// reread on the connection the failed statement ran on. The winning writer's name differs from the
    /// caller's, which is what makes the row a conflict rather than a no-op.
    /// </remarks>
    private static ConflictRow BuildFixtureConflictRow()
    {
        DataWindowBufferStore store = new();

        store.AppendRow(DwBuffer.Primary, ItemStatus.NotModified);
        store.SetItemValue(1L, 1, DwBuffer.Primary, 1L);
        store.SetItemValue(1L, 2, DwBuffer.Primary, "Paul");
        store.SetItemStatus(1L, 0, DwBuffer.Primary, ItemStatus.DataModified);

        return ConflictDetector.ProjectConflictRow(
            store,
            DwBuffer.Primary,
            1L,
            [new ConflictColumn("id", 1), new ConflictColumn("name", 2)],
            new Dictionary<int, object?> { [1] = 1L, [2] = "Someone else" });
    }

    #endregion
}
