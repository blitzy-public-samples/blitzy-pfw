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
//  a "negative" or "non-zero" reading of that condition, and it must not be weakened: the -2 and
//  the +1 cases are the whole point, because `n_cst_thread_trans.sru:L275` tests NON-ZERO and
//  `:L337` tests NEGATIVE and both are deliberately different from `sqlupdate:L208`.
//
//  NO DATABASE, NO DRIVER, NO LIVE TRANSACTION AND NO REAL CONCURRENT WRITER IS INVOLVED ANYWHERE
//  IN THIS FILE. Every surface the subject reads is injected and faked here, and the fakes RECORD
//  their interactions so the tests can assert not just the answers but which calls were made, with
//  which arguments, and IN WHAT ORDER - which is the only way the "both raises fire, in order" and
//  "the hook observes the pre-override value" properties are checkable at all.
//
//  THE FIXTURE IS TEST DATA, NOT PRODUCTION DATA. COMPANY, its six columns and their one-based ids
//  appear here because dw_sqlite.srd is the sole updatable DataWindow in the repository; the
//  production code under test names no table, no column and no column count.
// ==============================================================================================

using System.Diagnostics.CodeAnalysis;
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
/// A redactor that records what it was handed and can be switched to PASS THROUGH unchanged, so the
/// single redaction path is provable even with a redactor that masks nothing.
/// </summary>
internal sealed class RecordingRedactor : ISqlRedactor
{
    /// <summary>Every statement handed to the redactor, in order.</summary>
    internal List<string> Redacted { get; } = [];

    /// <summary>When set, the input is returned unchanged.</summary>
    internal bool PassThrough { get; set; }

    /// <inheritdoc/>
    public string Redact([AllowNull] string statement)
    {
        Redacted.Add(statement ?? string.Empty);

        return PassThrough ? statement ?? string.Empty : SqlRedactor.Instance.Redact(statement);
    }
}

#endregion

/// <summary>
/// The parity matrices for the update outcome classifier.
/// </summary>
public sealed class ConflictDetectorTests
{
    /// <summary>The redactor the subject under test is constructed with.</summary>
    private readonly RecordingRedactor _redactor = new();

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
    /// All THREE sentinel answers fire BOTH raises, in order, with the exact legacy payload, and return
    /// the database-error code.
    /// </summary>
    /// <param name="sentinel">The describe answer: empty, the error marker, or the unknown marker.</param>
    [Theory]
    [InlineData("")]
    [InlineData("!")]
    [InlineData("?")]
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
    [InlineData("", true)]
    [InlineData("!", true)]
    [InlineData("?", true)]
    [InlineData(null, true)]
    [InlineData(" ", false)]
    [InlineData("!x", false)]
    [InlineData("??", false)]
    [InlineData(" ! ", false)]
    [InlineData("COMPANY", false)]
    public void SentinelPredicateMatchesExactlyTheThreeLegacyAnswers(string? candidate, bool expected) =>
        // PowerScript's `=` on strings is EXACT, so " " and "!x" are not sentinels and must not become
        // ones by trimming [:L189].
        Assert.Equal(expected, ConflictDetector.IsSentinelUpdateTable(candidate));

    #endregion

    #region Step 5 - the veto arm and its two-way discrimination [:L195-L202]

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
    [InlineData(0L)]
    [InlineData(1L)]
    [InlineData(100L)]
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
    /// A hook answer that is not a prevention proceeds to the update - including <c>RetCode.OK</c> and a
    /// NEGATIVE code, neither of which <c>IsPrevented</c> matches.
    /// </summary>
    /// <param name="hookResult">The hook's answer.</param>
    [Theory]
    [InlineData(0L)]
    [InlineData(2L)]
    [InlineData(-1L)]
    public void ANonPreventingHookProceedsToTheUpdate(long hookResult)
    {
        _transaction.BeforeUpdateResult = hookResult;

        UpdateOutcome outcome = _detector.Classify(Attempt());

        Assert.Single(_target.UpdateCalls);
        Assert.Equal(UpdateOutcomeKind.Succeeded, outcome.Kind);
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
    /// THE HEADLINE PARITY MATRIX. The override fires for EXACTLY <c>-1</c> paired with a CLAIMED
    /// SUCCESS, and for nothing else.
    /// </summary>
    /// <param name="sqlCode">The transaction's SQL code.</param>
    /// <param name="updateResult">The DataWindow update result.</param>
    /// <param name="expectedOverride">Whether the rewrite is expected to fire.</param>
    /// <param name="expectedResult">The reconciled result.</param>
    /// <param name="expectedSuccess">Whether the outcome succeeds.</param>
    /// <remarks>
    /// DO NOT WEAKEN THIS MATRIX. The <c>(-2, 1)</c> case proves the test is EXACTLY <c>-1</c> and not
    /// "negative", and the <c>(1, 1)</c> case proves it is not "non-zero". Both readings are real
    /// elsewhere in the oracle and are deliberately different:
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:L275</c> tests NON-ZERO and
    /// <c>:L337</c> tests NEGATIVE, while
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru:L208</c> - the site under
    /// test - tests exactly <c>-1</c>. Three conditions over one field, never unified.
    /// </remarks>
    [Theory]
    [InlineData(-1L, 1L, true, -1L, false)]
    [InlineData(-1L, -1L, false, -1L, false)]
    [InlineData(-2L, 1L, false, 1L, true)]
    [InlineData(1L, 1L, false, 1L, true)]
    [InlineData(0L, 1L, false, 1L, true)]
    [InlineData(-1L, 0L, false, 0L, false)]
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
        _target.UpdateResult = DataWindowBufferStore.DataStoreFailure;
        _transaction.SqlDbCode = 1555L;
        _transaction.SqlErrText = "UNIQUE constraint failed: COMPANY.id";

        UpdateOutcome outcome = _detector.Classify(Attempt());

        Assert.Equal(UpdateOutcomeKind.DatabaseError, outcome.Kind);
        Assert.Equal(RetCode.E_DB_ERROR, outcome.Code);

        // The oracle raises nothing at [:L250].
        Assert.Empty(_sink.Raises);
        Assert.False(outcome.ErrorReported);
        Assert.Equal(string.Empty, outcome.ErrorText);

        DbErrorData payload = RequiredValue(outcome.DbError);
        Assert.Equal(1555L, payload.SqlDbCode);
        Assert.Equal("UNIQUE constraint failed: COMPANY.id", payload.SqlErrText);
        Assert.Equal(DwBuffer.Primary, payload.Buffer);
        Assert.Equal(0L, payload.Row);
        Assert.True(outcome.UpdateInvoked);
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
        // anyway, so a future arm that does carry a statement cannot bypass it.
        Assert.Equal(string.Empty, Assert.Single(_redactor.Redacted));
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

        _redactor.PassThrough = passThrough;

        DbErrorData masked = _detector.Redact(
            DbErrorData.FromStatement(-1L, "conflict", Statement, DwBuffer.Primary, 4L));

        // The injected redactor was consulted on the SINGLE path.
        Assert.Equal(Statement, Assert.Single(_redactor.Redacted));

        // The other four members are copied through untouched (C-B).
        Assert.Equal(-1L, masked.SqlDbCode);
        Assert.Equal("conflict", masked.SqlErrText);
        Assert.Equal(DwBuffer.Primary, masked.Buffer);
        Assert.Equal(4L, masked.Row);

        // THE WIRE PROJECTION MASKS REGARDLESS, because it applies its own policy and takes no redactor -
        // so a pass-through registration cannot weaken it.
        UpdateOutcome outcome = UpdateOutcome.DatabaseError(masked, errorReported: false);

        Assert.True(outcome.TryProjectDbError(out DbError? wire));
        Assert.DoesNotContain(Literal, Required(wire).Sqlsyntax, StringComparison.Ordinal);
        Assert.DoesNotContain("OldName", wire!.Sqlsyntax, StringComparison.Ordinal);
        Assert.Contains(SqlRedactor.DefaultPlaceholder, wire.Sqlsyntax, StringComparison.Ordinal);

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
