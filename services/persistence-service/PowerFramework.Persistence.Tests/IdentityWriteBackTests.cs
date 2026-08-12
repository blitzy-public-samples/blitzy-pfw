// ==================================================================================================
//  IdentityWriteBackTests.cs - THE APPLY HALF OF THE IDENTITY ROUND TRIP, AND THE R9 AUDIT THAT
//                              CLOSES THE PAIR
//  ------------------------------------------------------------------------------------------------
//  UNDER TEST     services/persistence-service/PowerFramework.Persistence/Tasks/TaskProxies/
//                     SqlUpdateTaskProxy.cs                     (the caller-side proxy, 344 legacy lines)
//  ORACLE         ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlupdate.sru   (READ ONLY)
//                     - the APPLY half, [运行在当前线程], RUNS ON THE CALLING THREAD [:L2]
//                 ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru      (READ ONLY)
//                     - the COLLECT half, [运行在子线程], RUNS ON THE WORKER THREAD [:L2]
//                 ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlbase.sru     (READ ONLY)
//                     - the bare last-error-wins sink this proxy OVERRIDES [:L44]
//                 ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds.sru     (READ ONLY)
//                     - the result carrier, the one main-thread object of the library [:L2]
//                 ws_objects/pfw.thread.ext.pbl.src/dberrordata.srs                      (READ ONLY)
//                     - the five-field error payload whose Row alone is rewritten
//                 ws_objects/pfw.common.pbl.src/makelong.srf                             (READ ONLY)
//                     - `ulong makelong(readonly uint low, readonly uint high)`; PowerBuilder's
//                       `uint` is SIXTEEN bits, which is why the packer takes two ushort operands
//  FIXTURE        ws_objects/pfw.tests.pbl.src/dw_sqlite.srd, reached through DwSqliteFixture.cs
//
//  ================================================================================================
//  🔴 THE ONE THING TO READ BEFORE ANYTHING ELSE: COLLECT BACKWARD, APPLY FORWARD (C-B, C-K, R9)
//  ================================================================================================
//  This file is the SECOND HALF OF A MATCHED PAIR, and neither half is meaningful alone.
//
//      HALF 1 - COLLECT.  IdentityColumnResolverTests.cs asserts that identity values are COLLECTED
//                         with the Primary buffer walked FORWARD
//                         [n_cst_thread_task_sqlupdate.sru:L229-L233] and the Filter buffer walked
//                         BACKWARD [n_cst_thread_task_sqlupdate.sru:L237].
//
//      HALF 2 - APPLY.    THIS FILE asserts that those values are APPLIED by walking FORWARD THROUGH
//                         BOTH buffers - Primary [n_cst_threading_task_sqlupdate.sru:L138, :L147]
//                         and Filter [n_cst_threading_task_sqlupdate.sru:L154, :L163], with the
//                         DataStore arm's structurally identical twins at [:L172, :L181] and
//                         [:L188, :L197].
//
//  WHY THE ASYMMETRY IS CORRECT. The oracle states the reason itself, on the line immediately above
//  the backward loop: "过滤缓冲区的数据与数据源(调用GetChanges的对象)的过滤缓冲区的数据顺序是相反的"
//  [n_cst_thread_task_sqlupdate.sru:L235] - the CARRIER's Filter buffer holds its rows in the
//  INVERSE of the order they occupy in the DATA SOURCE that called GetChanges. Walking the inverted
//  carrier backward produces a list in SOURCE order; applying that list forward onto the source
//  therefore lands every value on the row it belongs to. Backward collection composed with forward
//  application is the compensation, and each half looks like a defect in isolation.
//
//  ⚠️ UNDER NO CIRCUMSTANCES MAY EITHER DIRECTION BE "CORRECTED" (C-B). Flipping ONE of them writes
//  identity values onto the WRONG ROWS while every row COUNT still matches and the MULTISET of
//  values is still identical, so neither a count assertion nor an unordered comparison can detect
//  the regression. That is why the tests here assert PER-ROW VALUES IN ROW ORDER, and why
//  TheTwoSingleFlipsMisLandEveryValueWhileTheCountAndTheMultisetStayEqual EXECUTES both wrong
//  directions and proves the headline assertion fails under each.
//
//  ================================================================================================
//  AAP SECTION 0.4.5.4 - THE ONE-BASED TRANSLATION RULE, AND WHY THIS PAIR IS ITS AUDIT
//  ================================================================================================
//  The plan requires that EVERY ported loop either go through a CENTRALISED one-based indexing
//  helper or be INDIVIDUALLY AUDITED, because PowerBuilder arrays are one-based and its upper-bound
//  function answers THE LAST VALID INDEX while a C# list's Count is ONE PAST THE END. The plan names
//  the reverse-iteration case the single most dangerous line in the refactor.
//
//  THIS FILE AND IdentityColumnResolverTests.cs ARE THAT AUDIT, written down. The subject routes
//  every one-based site through OneBasedIndex (UpperBound / EmptyUpperBound / FirstIndex /
//  ToZeroBased / IsWithin) and ItemStatusMachine (FirstRowNumber / RowStatusColumn / BeforeFirstRow /
//  NoMoreModifiedRows); the cases below address rows and columns in the ONE-BASED domain throughout
//  and let the doubles perform the single rebasing, so an off-by-one in the subject shows up as a
//  wrong ROW rather than as an exception.
//
//  ================================================================================================
//  WHAT EACH REGION PROVES
//  ================================================================================================
//   1  THE COMPOSED ROUND TRIP - the REAL collector (IdentityColumnResolver) run over a carrier whose
//      Filter buffer is genuinely inverted, its output handed to the REAL proxy, and the values
//      applied onto a source object; asserted BY ROW IDENTITY, not by count. Includes the two
//      single-flip proofs and the fixture-driven case.
//   2  THE WRITE MECHANISM - Primary through the ITEM SETTER [:L144, :L178], Filter through the
//      DIRECT BUFFER EXPRESSION [:L160, :L194], asserted off ONE ORDERED CALL LOG so the mechanism
//      is proven rather than inferred from the outcome.
//   3  THE TWO ARMS - redraw suspended and restored on the DataWindow arm ONLY [:L134, :L166]; the
//      DataStore arm [:L167-L199] brackets nothing; the third arm answers E_INVALID_TYPE [:L201].
//   4  THE TABLE-1 BOUND - both walks bounded by _idColDatas[1]'s array length [:L136, :L151, :L170,
//      :L185] even in a multi-table run, observable when tables have differing insert counts, with
//      the preserved latent defect answering FAILED instead of raising.
//   5  THE FINALIZE GATE - the write-back auto-invoked from onfinalize ONLY when the last exit code
//      is RetCode.OK [:L303-L307], with the negative arm asserted too.
//   6  CALLER-SIDE ACCUMULATION - `+=` per table [:L66-L68] and append at UpperBound + 1 [:L73],
//      driven with two tables carrying DIFFERENT counts so a last-wins defect cannot pass.
//   7  THE LAST-ERROR ROW REWRITE - Row alone rewritten [:L325], over the PRIMARY buffer only
//      [:L321, :L328], every other field of the five left exactly as the worker set it, and
//      last-error-wins still holding afterwards.
//   8  THE NOTIFY CODE - Progress = 1, colliding numerically with the query proxy's MaxRows = 1
//      across two SEPARATE enumerations, and the payload packed current-in-the-LOW-word [:L32].
//   9  THE PRESERVED SETTER ASYMMETRY - the data-object assertion is UN-GATED [:L101] while the
//      SQL-syntax one is DEBUG-CONDITIONAL [:L113-L115]. Both asserted; neither harmonised.
//  10  THE ADD-UPDATABLE-TABLE ARITIES - the four-argument form leaves update-where and
//      key-in-place ABSENT rather than defaulted [:L227-L234], which is what C-06's proto3
//      `optional int64 updatewhere = 5` and `optional bool updatekeyinplace = 6` presence fields
//      carry on the wire.
//
//  ================================================================================================
//  CONSTRAINTS THIS FILE DISCHARGES
//  ================================================================================================
//   C-A  No type from another service appears here. The proxy, its write-back target seam and every
//        record consumed are Persistence-internal, reached through the application project's
//        InternalsVisibleTo item.
//   C-B  Every preserved legacy behaviour is asserted AS EXPECTED and commented with its locator:
//        apply-forward against collect-backward, the per-buffer write mechanism, the redraw
//        asymmetry between the two arms, the table-1 bound, the un-gated versus debug-gated
//        assertions, the absent-not-defaulted arity, and the notify-code collision.
//   C-D  Nothing for any deferred service. No rendering, no geometry, no DPI, no font: the redraw
//        seam is asserted as a NOTIFICATION SEQUENCE only, which is behaviour rather than painting.
//   C-E  No fabricated database. No connection is opened, no schema provisioned and no SQL Server or
//        Oracle target named; the carrier, the worker, the host and the transaction activator are
//        all doubles, and the two that must never be reached say so by throwing.
//   C-F  The Row rewrite must not touch SqlSyntax, so the redaction contract survives - asserted
//        FIELD BY FIELD rather than by comparing whole records. No credential literal appears here
//        in any form, including commented out.
//   C-H  SqlUpdateTaskProxy is 344 legacy lines of caller-side logic; without this file and its
//        counterpart the per-service 80 percent line gate is not reachable.
//   C-K  The collect-backward/apply-forward pairing is documented above with BOTH locators and a
//        cross-reference to IdentityColumnResolverTests.cs, and AAP Section 0.4.5.4's one-based rule
//        is recorded together with the statement that this pair IS the required audit.
//
//  DETERMINISM [AAP 0.6.7]. No real thread, no sleep, no Task.Delay, no ambient clock read: every
//  clock is the injected seam, every worker interaction goes through a double, and the suite
//  produces identical results on a second run by construction. NO LATENCY OR THROUGHPUT CLAIM IS
//  MADE ANYWHERE [AAP 0.8.5].
//
//  RULES POSITION. review_rules answers exactly one line, "No user rules provided." No user rule
//  governs this file and none is invented in its place; the enterprise baseline of AAP Section 0.7.2
//  applies instead - nullable and warnings-as-errors inherited from Directory.Build.props, and NO
//  SCREAMING_SNAKE identifier declared here, because this folder is outside the repository
//  .editorconfig's scoped naming relaxations. Preserved legacy spellings appear only inside comments
//  quoting the oracle and inside the verbatim assertion-message constants the subject publishes.
// ==================================================================================================

using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PowerFramework.Persistence.Concurrency;
using PowerFramework.Persistence.Configuration;
using PowerFramework.Persistence.Tasks;
using PowerFramework.Persistence.Tasks.TaskProxies;
using PowerFramework.Persistence.Transactions;
using PowerFramework.Shared.Diagnostics;

namespace PowerFramework.Persistence.Tests;

#region The ordered call log - so the MECHANISM is proven, not inferred from the outcome

/// <summary>
/// Which observable operation a recorded write-back call was.
/// </summary>
/// <remarks>
/// The four members are the complete set of things <c>of_refreshidentitydata</c>
/// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlupdate.sru:L120-L205</c>] does TO its
/// target. Keeping them in ONE ordered log rather than in four separate counters is what makes the
/// redraw BRACKET assertable: suspension must precede every write and restoration must follow every
/// write, which is a statement about order that per-member counters cannot express.
/// </remarks>
internal enum IdentityWriteBackCallKind
{
    /// <summary>
    /// <c>dw.SetRedraw(false)</c> [<c>:L134</c>] - repaint suspended.
    /// </summary>
    RedrawSuspended,

    /// <summary>
    /// <c>dw.SetRedraw(true)</c> [<c>:L166</c>] - repaint restored.
    /// </summary>
    RedrawRestored,

    /// <summary>
    /// <c>dw.SetItem(nRow, id, value)</c> [<c>:L144, :L178</c>] - the <c>Primary!</c> mechanism.
    /// </summary>
    ItemSetter,

    /// <summary>
    /// <c>dw.Object.Data.Filter[nRow, id] = value</c> [<c>:L160, :L194</c>] - the <c>Filter!</c>
    /// mechanism, which exists because the item setter takes no buffer argument and therefore cannot
    /// address a filtered row at all.
    /// </summary>
    FilterBufferExpression,
}

/// <summary>
/// One recorded call against a write-back target, in call order.
/// </summary>
/// <param name="Ordinal">
/// The one-based position of this call in the whole log. One-based deliberately: every ordinal in
/// this subject's vocabulary is one-based, and mixing a zero-based sequence number into an otherwise
/// one-based file is exactly the confusion AAP Section 0.4.5.4 warns about.
/// </param>
/// <param name="Kind">Which operation it was.</param>
/// <param name="Row">
/// The one-based row number the call addressed, or zero for the two redraw calls, which address no
/// row.
/// </param>
/// <param name="ColumnId">
/// The identity column's one-based ordinal exactly as the resolver produced it, or zero for the two
/// redraw calls. Carried as <see cref="long"/> because the subject never narrows it.
/// </param>
/// <param name="Value">The value written, which may legitimately be <see langword="null"/>.</param>
internal readonly record struct IdentityWriteBackCall(
    int Ordinal,
    IdentityWriteBackCallKind Kind,
    long Row,
    long ColumnId,
    long? Value);

/// <summary>
/// A write-back target that both RECORDS every call in order and APPLIES every write into real
/// buffers, so a test can assert the mechanism AND read the landed per-row values back out.
/// </summary>
/// <remarks>
/// <para>
/// <b>WHY IT DERIVES FROM THE BUFFER STORE INSTEAD OF FAKING THE THREE READS.</b> The write-back asks
/// its target three questions - the <c>Primary!</c> row count, the <c>Filter!</c> row count and a
/// row's status at column zero - and performs two kinds of write. A dictionary-backed double can
/// answer the questions, but it cannot then be READ BACK to show WHICH ROW ended up holding WHICH
/// VALUE, and that read-back is the only assertion that survives a flipped walk direction. Deriving
/// from <see cref="DataWindowBufferStore"/> gives real one-based buffers, so the applied values are
/// observable exactly where the legacy would leave them. <c>FakeDataWindowCarrier</c> in
/// <c>TestDoubles.cs</c> derives from the same base for the same reason on the READ side of the round
/// trip, and this type is its mirror image on the WRITE side.
/// </para>
/// <para>
/// <b>EVERY INTERFACE MEMBER IS IMPLEMENTED EXPLICITLY.</b> The base's members are
/// <see langword="internal"/>, and an internal member cannot implicitly implement an interface member,
/// so each one below forwards deliberately. It also keeps the two <c>SetItemValue</c> shapes - the
/// interface's three-argument write and the base's four-argument buffer write - from colliding in one
/// overload set, which matters because they mean different things.
/// </para>
/// <para>
/// <b>NO RENDERING (C-D).</b> <see cref="IIdentityWriteBackTarget.SetRedraw"/> paints nothing,
/// invalidates nothing and computes no geometry; it appends to the log. The oracle's DataWindow arm
/// brackets its body with the two calls while its DataStore arm does not [<c>:L134, :L166</c> versus
/// <c>:L167-L199</c>], and that difference is behaviour a caller can observe, so it is preserved as a
/// notification while the painting itself stays a deferred DesignSystem capability.
/// </para>
/// </remarks>
internal sealed class WriteBackTargetSpy : DataWindowBufferStore, IIdentityWriteBackTarget
{
    private readonly List<IdentityWriteBackCall> _calls = [];

    /// <summary>
    /// Initializes a target taking one of the oracle's three <c>TypeOf()</c> arms.
    /// </summary>
    /// <param name="kind">The arm [<c>:L131</c>].</param>
    internal WriteBackTargetSpy(IdentityWriteBackTargetKind kind) => Kind = kind;

    /// <summary>
    /// Which <c>TypeOf()</c> arm this target takes.
    /// </summary>
    public IdentityWriteBackTargetKind Kind { get; }

    /// <summary>
    /// Every call in the order it arrived.
    /// </summary>
    internal IReadOnlyList<IdentityWriteBackCall> Calls => _calls;

    /// <summary>
    /// The <c>Primary!</c> writes only, in call order.
    /// </summary>
    internal IReadOnlyList<IdentityWriteBackCall> ItemSetterCalls =>
        [.. _calls.Where(call => call.Kind == IdentityWriteBackCallKind.ItemSetter)];

    /// <summary>
    /// The <c>Filter!</c> writes only, in call order.
    /// </summary>
    internal IReadOnlyList<IdentityWriteBackCall> FilterExpressionCalls =>
        [.. _calls.Where(call => call.Kind == IdentityWriteBackCallKind.FilterBufferExpression)];

    /// <summary>
    /// The redraw arguments in call order - empty on the DataStore arm.
    /// </summary>
    internal IReadOnlyList<bool> RedrawArguments =>
        [.. _calls
            .Where(call => call.Kind is IdentityWriteBackCallKind.RedrawSuspended
                or IdentityWriteBackCallKind.RedrawRestored)
            .Select(call => call.Kind == IdentityWriteBackCallKind.RedrawRestored)];

    /// <summary>
    /// What <see cref="IIdentityWriteBackTarget.Describe"/> answers for the update-table property.
    /// Defaults to PowerBuilder's UNKNOWN marker, which is the answer that closes the capture gate.
    /// </summary>
    internal string UpdateTableAnswer { get; init; } = ConflictDetector.DescribeUnknownMarker;

    /// <summary>
    /// What <see cref="IIdentityWriteBackTarget.Describe"/> answers for the syntax property.
    /// </summary>
    internal string SyntaxAnswer { get; init; } = string.Empty;

    /// <summary>
    /// The changed-row count <see cref="IIdentityWriteBackTarget.GetChanges"/> reports.
    /// </summary>
    internal long ChangeRowCount { get; init; }

    /// <summary>
    /// How many times the changeset capture was asked for.
    /// </summary>
    internal int GetChangesCalls { get; private set; }

    /// <summary>
    /// Appends a row to a buffer and gives it a status, answering its ONE-BASED row number.
    /// </summary>
    /// <param name="buffer">The buffer to append to.</param>
    /// <param name="status">The row's status, which is what the write-back's new-row test reads.</param>
    /// <param name="token">
    /// A value written into <paramref name="tokenColumn"/> so a test can tell WHICH LOGICAL ROW this
    /// is after the values have been applied. This is what makes the round-trip assertion an identity
    /// assertion rather than a positional one.
    /// </param>
    /// <param name="tokenColumn">The column the token is written into.</param>
    /// <returns>The new row's one-based number within its buffer.</returns>
    internal long SeedRow(DwBuffer buffer, ItemStatus status, string token, int tokenColumn)
    {
        long row = AppendRow(buffer, status);
        _ = SetItemValue(row, tokenColumn, buffer, token);

        return row;
    }

    /// <summary>
    /// Reads the token a row carries.
    /// </summary>
    /// <param name="buffer">The buffer holding it.</param>
    /// <param name="row">The one-based row number.</param>
    /// <param name="tokenColumn">The token column.</param>
    /// <returns>The token, or the empty string when none was written.</returns>
    internal string TokenAt(DwBuffer buffer, long row, int tokenColumn) =>
        GetItemValue(row, tokenColumn, buffer) as string ?? string.Empty;

    /// <summary>
    /// Reads the identity value that LANDED on a row - the whole point of the round trip.
    /// </summary>
    /// <param name="buffer">The buffer holding the row.</param>
    /// <param name="row">The one-based row number.</param>
    /// <param name="identityColumn">The identity column's one-based ordinal.</param>
    /// <returns>The applied value, or <see langword="null"/> when nothing was applied.</returns>
    internal long? AppliedValueAt(DwBuffer buffer, long row, int identityColumn) =>
        GetItemValue(row, identityColumn, buffer) as long?;

    /// <summary>
    /// The landed identity values of one buffer, in ROW ORDER, as a sequence.
    /// </summary>
    /// <param name="buffer">The buffer to read.</param>
    /// <param name="identityColumn">The identity column's one-based ordinal.</param>
    /// <returns>One element per row, in ascending row order.</returns>
    /// <remarks>
    /// Deliberately a SEQUENCE and never a set: an unordered comparison is precisely what cannot
    /// detect a flipped walk direction.
    /// </remarks>
    internal IReadOnlyList<long?> AppliedValuesInRowOrder(DwBuffer buffer, int identityColumn)
    {
        long count = buffer == DwBuffer.Filter ? FilteredCount() : RowCount();
        List<long?> values = [];

        for (long row = ItemStatusMachine.FirstRowNumber; row <= count; row++)
        {
            values.Add(AppliedValueAt(buffer, row, identityColumn));
        }

        return values;
    }

    /// <summary>
    /// Maps each row's token to the identity value that landed on it, in row order.
    /// </summary>
    /// <param name="buffer">The buffer to read.</param>
    /// <param name="identityColumn">The identity column's one-based ordinal.</param>
    /// <param name="tokenColumn">The token column.</param>
    /// <returns>One entry per row, in ascending row order.</returns>
    internal IReadOnlyList<KeyValuePair<string, long?>> LandingByToken(
        DwBuffer buffer,
        int identityColumn,
        int tokenColumn)
    {
        long count = buffer == DwBuffer.Filter ? FilteredCount() : RowCount();
        List<KeyValuePair<string, long?>> landing = [];

        for (long row = ItemStatusMachine.FirstRowNumber; row <= count; row++)
        {
            landing.Add(
                new KeyValuePair<string, long?>(
                    TokenAt(buffer, row, tokenColumn),
                    AppliedValueAt(buffer, row, identityColumn)));
        }

        return landing;
    }

    /// <inheritdoc/>
    void IIdentityWriteBackTarget.SetRedraw(bool redraw) =>
        Record(
            redraw
                ? IdentityWriteBackCallKind.RedrawRestored
                : IdentityWriteBackCallKind.RedrawSuspended,
            ItemStatusMachine.BeforeFirstRow,
            0L,
            null);

    /// <inheritdoc/>
    long IIdentityWriteBackTarget.RowCount() => RowCount();

    /// <inheritdoc/>
    long IIdentityWriteBackTarget.FilteredCount() => FilteredCount();

    /// <inheritdoc/>
    ItemStatus IIdentityWriteBackTarget.GetItemStatus(long row, int columnIndex, DwBuffer buffer) =>
        GetItemStatus(row, columnIndex, buffer);

    /// <inheritdoc/>
    void IIdentityWriteBackTarget.SetItemValue(long row, long columnId, long? value)
    {
        Record(IdentityWriteBackCallKind.ItemSetter, row, columnId, value);

        // The legacy Primary write is SetItem(nRow, id, value) [:L144], which has NO buffer argument
        // and therefore always addresses Primary! - that is the whole reason the Filter write below
        // has to use a different mechanism.
        _ = SetItemValue(row, ColumnNumberOf(columnId), DwBuffer.Primary, value);
    }

    /// <inheritdoc/>
    void IIdentityWriteBackTarget.SetFilterBufferValue(long row, long columnId, long? value)
    {
        Record(IdentityWriteBackCallKind.FilterBufferExpression, row, columnId, value);

        _ = SetItemValue(row, ColumnNumberOf(columnId), DwBuffer.Filter, value);
    }

    /// <inheritdoc/>
    string IIdentityWriteBackTarget.Describe(string property) =>
        property == UpdateWhereBuilder.UpdateTableProperty ? UpdateTableAnswer : SyntaxAnswer;

    /// <inheritdoc/>
    long IIdentityWriteBackTarget.GetChanges(out CarrierState? changes)
    {
        GetChangesCalls++;
        changes = new CarrierState();

        return ChangeRowCount;
    }

    /// <summary>
    /// Narrows the identity column's ordinal into the column-number domain the buffer store addresses,
    /// refusing anything outside it rather than wrapping silently.
    /// </summary>
    /// <param name="columnId">The ordinal the subject passed through unchanged.</param>
    /// <returns>The same ordinal as an <see cref="int"/>.</returns>
    private static int ColumnNumberOf(long columnId)
    {
        // The subject deliberately never narrows the identity ordinal, so the narrowing happens HERE,
        // once, and is checked. A silent wrap would turn a wrong-column defect into a plausible one.
        ArgumentOutOfRangeException.ThrowIfLessThan(columnId, ItemStatusMachine.FirstColumnNumber);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(columnId, int.MaxValue);

        return (int)columnId;
    }

    private void Record(IdentityWriteBackCallKind kind, long row, long columnId, long? value) =>
        _calls.Add(new IdentityWriteBackCall(_calls.Count + 1, kind, row, columnId, value));
}

#endregion

/// <summary>
/// The apply half of the identity round trip, and the audit that closes the collect-backward /
/// apply-forward pair with <c>IdentityColumnResolverTests</c>.
/// </summary>
public sealed class IdentityWriteBackTests
{
    // ==============================================================================================
    //  THE SCENARIO VOCABULARY
    //  --------------------------------------------------------------------------------------------
    //  The identity column is the fixture's own: dw_sqlite.srd declares column ONE as
    //  `key=yes identity=yes name=id` [dw_sqlite.srd:L8], and it is the sole updatable DataWindow in
    //  the repository. The token column is the fixture's `name` column, chosen because it is the one
    //  non-identity column whose sample values are distinguishable per row.
    //
    //  THE FIXTURE IS TEST DATA, NOT PRODUCTION DATA. The subject names no table, no column and no
    //  column count; these ordinals exist so the cases run against the oracle's own shape rather than
    //  an invented one.
    // ==============================================================================================

    /// <summary>
    /// The identity column's one-based ordinal - the fixture's <c>id</c> [<c>dw_sqlite.srd:L8</c>].
    /// </summary>
    private const int IdentityColumn = DwSqliteFixture.IdColumnNumber;

    /// <summary>
    /// The column each staged row's identity token is written into - the fixture's <c>name</c>.
    /// </summary>
    private const int TokenColumn = DwSqliteFixture.NameColumnNumber;

    /// <summary>
    /// A second identity column ordinal, used only by the multi-table cases so the two blocks write
    /// DIFFERENT columns and a merged write cannot pass unnoticed.
    /// </summary>
    private const int SecondTableIdentityColumn = DwSqliteFixture.AgeColumnNumber;

    #region 1 - THE COMPOSED ROUND TRIP - the real collector composed with the real applier

    /// <summary>
    /// One staged <c>Filter!</c> row: the token that identifies it and the identity value the database
    /// assigned to it.
    /// </summary>
    /// <param name="Token">
    /// Written into the token column so a landed value can be attributed to a LOGICAL row rather than
    /// to a position - which is what makes the round-trip assertion an identity assertion.
    /// </param>
    /// <param name="Identity">
    /// The value the database assigned to this row. Never null here, because every staged filtered row
    /// is newly inserted; the null case is covered separately.
    /// </param>
    private readonly record struct StagedFilterRow(string Token, long Identity);

    /// <summary>
    /// One staged <c>Primary!</c> row: its token, its row status, and the identity value it must end up
    /// holding - <see langword="null"/> for a row the write-back must leave alone.
    /// </summary>
    /// <param name="Token">The token that identifies this row.</param>
    /// <param name="Status">
    /// The row's status, which is what the walk's modified test and the new-row test both read at
    /// column zero [<c>:L140</c>].
    /// </param>
    /// <param name="Identity">
    /// The value this row must end up holding, or <see langword="null"/> when it qualifies for none -
    /// a data-modified row is VISITED and skipped, and an unmodified row is never visited at all.
    /// </param>
    private readonly record struct StagedPrimaryRow(string Token, ItemStatus Status, long? Identity);

    /// <summary>
    /// The staged filter rows, in SOURCE order, each with the identity value the database assigned to
    /// it. The carrier holds these same rows INVERTED, which is the whole point.
    /// </summary>
    /// <remarks>
    /// Three rows rather than two: with three, a reversal leaves the middle element fixed, so a test
    /// that accidentally compares only the first and last elements would still notice, and the shape
    /// of the inversion is legible at a glance.
    /// </remarks>
    private static IReadOnlyList<StagedFilterRow> FilterRowsInSourceOrder { get; } =
    [
        new("filter-alpha", 601L),
        new("filter-beta", 602L),
        new("filter-gamma", 603L),
    ];

    /// <summary>
    /// The staged primary rows, in source order. Two are newly inserted and receive an identity; the
    /// two merely data-modified ones are VISITED by the walk and skipped, which is what proves the
    /// value cursor advances only on a qualifying row [<c>:L141</c>].
    /// </summary>
    private static IReadOnlyList<StagedPrimaryRow> PrimaryRowsInSourceOrder { get; } =
    [
        new("primary-edited", ItemStatus.DataModified, null),
        new("primary-new-one", ItemStatus.NewModified, 501L),
        new("primary-untouched", ItemStatus.NotModified, null),
        new("primary-new-two", ItemStatus.NewModified, 502L),
    ];

    /// <summary>
    /// The three <c>TypeOf()</c> arms, restated as a PUBLIC vocabulary so theory data can name one.
    /// </summary>
    /// <remarks>
    /// <b>WHY A SECOND ENUMERATION EXISTS, AND WHY IT CANNOT DRIFT.</b> The subject's own
    /// <c>IdentityWriteBackTargetKind</c> is <see langword="internal"/> - correctly, because the arm
    /// discrimination is a Persistence-internal seam (C-A) - and an internal type cannot appear in the
    /// signature of a public theory method. This mirror exists ONLY to carry the choice into
    /// <c>MemberData</c>, it is mapped by <see cref="KindOf"/>, and
    /// <see cref="TheArmVocabularyCoversEverySubjectArmExactly"/> pins it member for member against the
    /// subject's enumeration so a new arm there cannot leave this one silently behind.
    /// </remarks>
    public enum TargetArm
    {
        /// <summary>
        /// The <c>case DataWindow!</c> arm [<c>:L132</c>] - the one that brackets its body.
        /// </summary>
        DataWindow,

        /// <summary>
        /// The <c>case DataStore!</c> arm [<c>:L167</c>] - structurally identical, bracket-free.
        /// </summary>
        DataStore,

        /// <summary>
        /// The <c>case else</c> arm [<c>:L200-L201</c>].
        /// </summary>
        Other,
    }

    /// <summary>
    /// The two walk directions a buffer can be traversed in, so the flip proofs can name which one
    /// they are exercising instead of encoding it as a boolean.
    /// </summary>
    public enum WalkDirection
    {
        /// <summary>
        /// Ascending row order - what the APPLIER always uses [<c>:L147, :L163</c>], and what the
        /// COLLECTOR uses for <c>Primary!</c> [<c>n_cst_thread_task_sqlupdate.sru:L229</c>].
        /// </summary>
        Forward,

        /// <summary>
        /// Descending row order - what the COLLECTOR uses for <c>Filter!</c> and ONLY for
        /// <c>Filter!</c> [<c>n_cst_thread_task_sqlupdate.sru:L237</c>].
        /// </summary>
        Backward,
    }

    /// <summary>
    /// 🔴 <b>THE SINGLE MOST VALUABLE ASSERTION IN THIS FILE.</b> The REAL collector is run over a
    /// carrier whose <c>Filter!</c> buffer is genuinely inverted, its output is handed to the REAL
    /// proxy, and the proxy applies it onto a source object - and EVERY ROW ENDS UP HOLDING THE
    /// IDENTITY VALUE THAT BELONGS TO IT, identified by the row's own token rather than by position.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the pairing asserted END TO END: collect Primary FORWARD and Filter BACKWARD
    /// [<c>n_cst_thread_task_sqlupdate.sru:L229, :L237</c>], then apply BOTH FORWARD
    /// [<c>n_cst_threading_task_sqlupdate.sru:L147, :L163</c>]. Nothing is hand-fed - the array the
    /// proxy receives is the array <see cref="IdentityColumnResolver"/> produced.
    /// </para>
    /// <para>
    /// <b>VERIFIED BY MUTATION, NOT BY CONFIDENCE.</b> Two single-line mutations were applied to the
    /// production code and each one made THIS test fail, after which both were reverted:
    /// </para>
    /// <list type="number">
    /// <item>
    /// <description>
    /// turning the collector's descending Filter loop into an ascending one in
    /// <c>Concurrency/IdentityColumnResolver.cs</c> - three failures in this file, including this test;
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// making the applier consume the Filter array in reverse in
    /// <c>Tasks/TaskProxies/SqlUpdateTaskProxy.cs</c>, which is what walking the rows backward amounts
    /// to - the same three failures.
    /// </description>
    /// </item>
    /// </list>
    /// <para>
    /// Every OTHER test in the module passed under both mutations, which is the point:
    /// <see cref="TheTwoSingleFlipsMisLandEveryValueWhileTheCountAndTheMultisetStayEqual"/> states in
    /// executable form why - the count and the multiset are invariant under a flip, so only an ordered
    /// per-row assertion can see one.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheRealCollectorComposedWithTheRealApplierLandsEveryValueOnItsOwnRow()
    {
        using WriteBackProxyHarness harness = new();

        FakeDataWindowCarrier carrier = StageCarrier();
        WriteBackTargetSpy source = StageSource(IdentityWriteBackTargetKind.DataStore);

        // THE REAL COLLECTOR. Primary forward, Filter backward - the subject's own code, not a
        // reproduction of it.
        ResolvedIdentityColumnData collected = CollectFrom(carrier);

        // The collector's Filter array is in SOURCE order precisely BECAUSE it walked the inverted
        // carrier backwards. Stated here so the expectation below cannot be misread as coincidence.
        Assert.Equal(
            [.. FilterRowsInSourceOrder.Select(row => (long?)row.Identity)],
            collected.FilterValues);

        // THE REAL APPLIER, through the proxy that received the collector's output verbatim.
        harness.Proxy.OnIdentityColumnDataRetrieved(
            collected.IdentityColumnId,
            collected.PrimaryValues,
            collected.FilterValues);

        Assert.Equal(RetCode.OK, harness.Proxy.RefreshIdentityData(source));

        // ★ THE ROUND-TRIP ASSERTION, BY ROW IDENTITY. Each token is paired with the identity value
        // the database assigned to THAT row, so the assertion fails if any value lands elsewhere -
        // and it cannot be satisfied by a coincidence of counts.
        Assert.Equal(
            [.. FilterRowsInSourceOrder.Select(
                row => new KeyValuePair<string, long?>(row.Token, row.Identity))],
            source.LandingByToken(DwBuffer.Filter, IdentityColumn, TokenColumn));

        // The same for Primary, where the two non-qualifying rows must have received NOTHING: the
        // walk visits the data-modified row and skips it, and never even visits the unmodified one.
        Assert.Equal(
            [.. PrimaryRowsInSourceOrder.Select(
                row => new KeyValuePair<string, long?>(row.Token, row.Identity))],
            source.LandingByToken(DwBuffer.Primary, IdentityColumn, TokenColumn));
    }

    /// <summary>
    /// The two single flips, COMPOSED ARITHMETICALLY OVER THE SAME DATA. Collecting <c>Filter!</c>
    /// forward, or applying it backward, mis-lands every value - while the COUNT and the MULTISET of
    /// applied values stay identical, which is why neither a count assertion nor an unordered
    /// comparison may ever replace the per-row assertion in
    /// <see cref="TheRealCollectorComposedWithTheRealApplierLandsEveryValueOnItsOwnRow"/>.
    /// </summary>
    /// <remarks>
    /// This case reasons over the value sequences rather than mutating the subject - the subject-level
    /// proof is the mutation record on that test. Its job is to make the INVARIANCE of the count and
    /// the multiset executable, so that a future reader tempted to simplify the headline assertion into
    /// a count finds the counter-example already written down.
    /// </remarks>
    /// <param name="collectDirection">The direction the collection half is simulated in.</param>
    /// <param name="applyDirection">The direction the application half is simulated in.</param>
    /// <param name="landsCorrectly">
    /// Whether the composition lands every value on its own row. TRUE only for the legacy's own
    /// pairing and for the double flip; FALSE for each single flip.
    /// </param>
    [Theory]
    [MemberData(nameof(WalkDirectionPairings))]
    public void TheTwoSingleFlipsMisLandEveryValueWhileTheCountAndTheMultisetStayEqual(
        WalkDirection collectDirection,
        WalkDirection applyDirection,
        bool landsCorrectly)
    {
        IReadOnlyList<long?> carrierOrderValues =
            [.. FilterRowsInSourceOrder.Reverse().Select(row => (long?)row.Identity)];

        // The collection half, simulated in the requested direction over the INVERTED carrier order.
        IReadOnlyList<long?> collected = collectDirection == WalkDirection.Backward
            ? [.. carrierOrderValues.Reverse()]
            : carrierOrderValues;

        // The application half, simulated in the requested direction over the source's rows.
        IReadOnlyList<long?> applied = applyDirection == WalkDirection.Backward
            ? [.. collected.Reverse()]
            : collected;

        IReadOnlyList<long?> correct = [.. FilterRowsInSourceOrder.Select(row => (long?)row.Identity)];

        // THE COUNT IS ALWAYS EQUAL, and so is the multiset - which is the entire reason the headline
        // test asserts an ORDERED per-row mapping.
        Assert.Equal(correct.Count, applied.Count);
        Assert.Equal(correct.Order(), applied.Order());

        if (landsCorrectly)
        {
            Assert.Equal(correct, applied);
        }
        else
        {
            Assert.NotEqual(correct, applied);
        }
    }

    /// <summary>
    /// The four direction pairings and whether each lands correctly.
    /// </summary>
    /// <remarks>
    /// The double flip lands correctly too, and saying so out loud is important: what the legacy fixes
    /// is the PAIRING, not either direction in isolation. C-B still forbids changing either half,
    /// because the oracle fixes both and a half-changed pair is the failure mode.
    /// </remarks>
    public static TheoryData<WalkDirection, WalkDirection, bool> WalkDirectionPairings =>
        new()
        {
            // The legacy's own pairing [n_cst_thread_task_sqlupdate.sru:L237 with
            // n_cst_threading_task_sqlupdate.sru:L163].
            { WalkDirection.Backward, WalkDirection.Forward, true },

            // SINGLE FLIP 1 - "correcting" the collector's backward walk.
            { WalkDirection.Forward, WalkDirection.Forward, false },

            // SINGLE FLIP 2 - "correcting" the applier's forward walk.
            { WalkDirection.Backward, WalkDirection.Backward, false },

            // The double flip, which lands correctly and is still forbidden.
            { WalkDirection.Forward, WalkDirection.Backward, true },
        };

    /// <summary>
    /// The same round trip over the ORACLE'S OWN DATA rather than staged data: the fixture's Filter
    /// buffer holds its two newly-inserted rows inverted, so the collector reports <c>7</c> then
    /// <c>6</c> and the applier puts <c>7</c> on filter row one.
    /// </summary>
    /// <remarks>
    /// Driven through <c>DwSqliteTargetMetadata</c> and <c>DwSqliteSampleRowSource</c> - the fixture's
    /// own implementations of the two injected surfaces - so the identity column is DISCOVERED at run
    /// time rather than asserted, exactly as <c>_of_update</c> discovers it
    /// [<c>n_cst_thread_task_sqlupdate.sru:L217-L225</c>].
    /// </remarks>
    [Fact]
    public void TheFixtureRoundTripAppliesTheInvertedFilterArrayForwardOntoTheSource()
    {
        using WriteBackProxyHarness harness = new();

        IdentityResolutionOutcome outcome = IdentityColumnResolver.Resolve(
            new DwSqliteTargetMetadata(),
            new DwSqliteSampleRowSource());

        ResolvedIdentityColumnData block = Assert.Single(outcome.Identity);

        // The fixture's own inverted array, restated so the expectation below is legible without
        // opening the fixture [dw_sqlite.srd, transcribed in DwSqliteFixture.SampleRows].
        Assert.Equal(DwSqliteFixture.ExpectedFilterIdentityValues, block.FilterValues);
        Assert.Equal<long?>([7L, 6L], block.FilterValues);
        Assert.Equal(DwSqliteFixture.ExpectedPrimaryIdentityValues, block.PrimaryValues);

        harness.Proxy.OnIdentityColumnDataRetrieved(
            block.IdentityColumnId,
            block.PrimaryValues,
            block.FilterValues);

        // A source shaped like the fixture's own buffers: four Primary rows with the third newly
        // inserted, and two newly inserted Filter rows.
        WriteBackTargetSpy source = new(IdentityWriteBackTargetKind.DataWindow);

        foreach (DwSqliteSampleRow row in DwSqliteFixture.SampleRows)
        {
            // The token names the row by buffer and one-based number, which is all the fixture cases
            // need: the landed values are asserted as ordered sequences per buffer below.
            _ = source.SeedRow(row.Buffer, row.Status, $"{row.Buffer}-{row.Row}", TokenColumn);
        }

        Assert.Equal(RetCode.OK, harness.Proxy.RefreshIdentityData(source));

        // Filter row ONE receives 7 and row TWO receives 6 - the forward application of a backward
        // collection. Reversing either half swaps these two.
        Assert.Equal<long?>([7L, 6L], source.AppliedValuesInRowOrder(DwBuffer.Filter, IdentityColumn));

        // Primary: only the fixture's third row is newly inserted, so only it receives the single
        // collected value, and the two data-modified rows around it receive nothing.
        Assert.Equal<long?>(
            [null, null, DwSqliteFixture.ExpectedPrimaryIdentityValues[0], null],
            source.AppliedValuesInRowOrder(DwBuffer.Primary, IdentityColumn));
    }

    /// <summary>
    /// The write-back applies onto a CALLER-SUPPLIED object [<c>:L54, :L120</c>] rather than onto
    /// anything the proxy owns: two different targets handed the same collected block each receive the
    /// same values independently, and neither sees the other's writes.
    /// </summary>
    [Fact]
    public void TheWriteBackAppliesOntoWhicheverObjectTheCallerSupplies()
    {
        using WriteBackProxyHarness harness = new();

        harness.Proxy.OnIdentityColumnDataRetrieved(IdentityColumn, [701L], []);

        WriteBackTargetSpy first = SingleNewRowSource();
        WriteBackTargetSpy second = SingleNewRowSource();

        Assert.Equal(RetCode.OK, harness.Proxy.RefreshIdentityData(first));

        Assert.Equal<long?>(701L, first.AppliedValueAt(DwBuffer.Primary, 1L, IdentityColumn));
        Assert.Null(second.AppliedValueAt(DwBuffer.Primary, 1L, IdentityColumn));

        Assert.Equal(RetCode.OK, harness.Proxy.RefreshIdentityData(second));

        Assert.Equal<long?>(701L, second.AppliedValueAt(DwBuffer.Primary, 1L, IdentityColumn));

        // The collected blocks are not consumed by application: the same block applied twice writes
        // twice, because the oracle's write-back neither clears nor marks its arrays [:L120-L205].
        Assert.Single(harness.Proxy.IdentityBlocks);
    }

    /// <summary>
    /// The proxy CONSUMES the three-field resolved-identity record
    /// <c>Concurrency/IdentityColumnResolver.cs</c> already declares - it does not re-declare the
    /// oracle's nested <c>IDCOLDATA</c> structure [<c>:L6-L7, :L10-L14</c>].
    /// </summary>
    /// <remarks>
    /// Asserted structurally rather than by inspection, in three steps: the accumulated element's type
    /// is declared in the <c>Concurrency</c> namespace rather than beside the proxy; that record's
    /// primary constructor carries exactly three parameters in the oracle's field order - the identity
    /// column id, the Primary values, the Filter values; and both value arrays are REFERENCE-IDENTICAL
    /// to the ones handed in, which is how the oracle's array assignment carries them [<c>:L75-L76</c>].
    /// </remarks>
    [Fact]
    public void TheAccumulatedBlockIsTheResolversThreeFieldRecordInTheOraclesFieldOrder()
    {
        using WriteBackProxyHarness harness = new();

        long?[] primaryValues = [801L];
        long?[] filterValues = [802L, 803L];

        harness.Proxy.OnIdentityColumnDataRetrieved(IdentityColumn, primaryValues, filterValues);

        ResolvedIdentityColumnData block = Assert.Single(harness.Proxy.IdentityBlocks);

        // The element type is the resolver's, declared in the Concurrency namespace - not a second
        // IdColData declared inside the proxy.
        Assert.Equal(
            typeof(IdentityColumnResolver).Namespace,
            typeof(ResolvedIdentityColumnData).Namespace);

        // THE ORACLE'S FIELD ORDER: `long id`, `long PrimaryValues[]`, `long FilterValues[]`
        // [n_cst_threading_task_sqlupdate.sru:L11-L13].
        ParameterInfo[] fields = typeof(ResolvedIdentityColumnData)
            .GetConstructors()
            .Single(candidate => candidate.GetParameters().Length == 3)
            .GetParameters();

        Assert.Equal(
            [
                nameof(ResolvedIdentityColumnData.IdentityColumnId),
                nameof(ResolvedIdentityColumnData.PrimaryValues),
                nameof(ResolvedIdentityColumnData.FilterValues),
            ],
            fields.Select(field => field.Name));

        // The two arrays travel BY REFERENCE, exactly as the oracle's array assignment carries them
        // [:L75-L76] - neither is copied, trimmed, sorted nor merged with the other.
        Assert.Same(primaryValues, block.PrimaryValues);
        Assert.Same(filterValues, block.FilterValues);
        Assert.Equal(IdentityColumn, block.IdentityColumnId);
    }

    #endregion

    #region 2 - THE WRITE MECHANISM DIFFERS PER BUFFER, AND THE ORDERED LOG PROVES IT

    /// <summary>
    /// ⚠️ <b>THE TWO WRITE ACCESSORS ARE ASYMMETRIC AND THE ASYMMETRY IS THE ORACLE'S (C-B).</b>
    /// <c>Primary!</c> is written through the ITEM SETTER <c>dw.SetItem(nRow, id, value)</c>
    /// [<c>:L144</c>] while <c>Filter!</c> is written through the DIRECT BUFFER EXPRESSION
    /// <c>dw.Object.Data.Filter[nRow, id] = value</c> [<c>:L160</c>] - two mechanisms for the same
    /// logical operation, in one function, sixteen lines apart. The item setter takes NO buffer
    /// argument, so a filtered row is simply not addressable through it.
    /// </summary>
    /// <remarks>
    /// Asserted off ONE ORDERED CALL LOG so the MECHANISM is proven rather than inferred: an
    /// implementation that routed both buffers through a single accessor with a buffer parameter would
    /// produce identical landed values and would fail only here.
    /// </remarks>
    [Fact]
    public void PrimaryGoesThroughTheItemSetterAndFilterThroughTheDirectBufferExpression()
    {
        using WriteBackProxyHarness harness = new();

        FakeDataWindowCarrier carrier = StageCarrier();
        WriteBackTargetSpy source = StageSource(IdentityWriteBackTargetKind.DataStore);
        ResolvedIdentityColumnData collected = CollectFrom(carrier);

        harness.Proxy.OnIdentityColumnDataRetrieved(
            collected.IdentityColumnId,
            collected.PrimaryValues,
            collected.FilterValues);

        Assert.Equal(RetCode.OK, harness.Proxy.RefreshIdentityData(source));

        // EVERY Primary write is an item-setter call and EVERY Filter write is a buffer-expression
        // call - asserted as the whole ordered log, so a single stray call of the wrong kind fails.
        Assert.Equal(
            [
                // Primary pass first [:L135-L149], one call per qualifying row, in row order.
                (IdentityWriteBackCallKind.ItemSetter, 2L, 501L),
                (IdentityWriteBackCallKind.ItemSetter, 4L, 502L),

                // Filter pass second [:L150-L165], through the OTHER mechanism.
                (IdentityWriteBackCallKind.FilterBufferExpression, 1L, 601L),
                (IdentityWriteBackCallKind.FilterBufferExpression, 2L, 602L),
                (IdentityWriteBackCallKind.FilterBufferExpression, 3L, 603L),
            ],
            source.Calls.Select(call => (call.Kind, call.Row, call.Value)));

        // The ordinals are contiguous and one-based, so "in call order" above is a real ordering claim
        // rather than an artefact of how the log was projected.
        Assert.Equal(
            [.. Enumerable.Range(1, source.Calls.Count)],
            source.Calls.Select(call => call.Ordinal));

        // The identity column ordinal is passed through UNCHANGED on both mechanisms - never rebased,
        // never narrowed (AAP Section 0.4.5.4).
        Assert.All(source.Calls, call => Assert.Equal((long)IdentityColumn, call.ColumnId));
    }

    /// <summary>
    /// The <c>Primary!</c> pass runs BEFORE the <c>Filter!</c> pass [<c>:L135-L149</c> then
    /// <c>:L150-L165</c>], and the two passes never interleave.
    /// </summary>
    [Fact]
    public void ThePrimaryPassCompletesBeforeTheFilterPassBegins()
    {
        using WriteBackProxyHarness harness = new();

        WriteBackTargetSpy source = StageSource(IdentityWriteBackTargetKind.DataStore);
        harness.Proxy.OnIdentityColumnDataRetrieved(
            IdentityColumn,
            [501L, 502L],
            [601L, 602L, 603L]);

        Assert.Equal(RetCode.OK, harness.Proxy.RefreshIdentityData(source));

        int lastItemSetter = source.Calls
            .Where(call => call.Kind == IdentityWriteBackCallKind.ItemSetter)
            .Max(call => call.Ordinal);

        int firstFilterExpression = source.Calls
            .Where(call => call.Kind == IdentityWriteBackCallKind.FilterBufferExpression)
            .Min(call => call.Ordinal);

        Assert.True(
            lastItemSetter < firstFilterExpression,
            "The oracle runs the Primary pass to completion and only then starts the Filter pass, so "
                + "no buffer-expression write may precede the last item-setter write.");
    }

    /// <summary>
    /// A null identity value is carried through both mechanisms rather than coerced to zero: the
    /// collector's item read answers null for a null item and the oracle stores that null into the
    /// array it hands over [<c>n_cst_thread_task_sqlupdate.sru:L231, :L239</c>].
    /// </summary>
    [Fact]
    public void ANullIdentityValueTravelsThroughBothMechanismsUncoerced()
    {
        using WriteBackProxyHarness harness = new();

        WriteBackTargetSpy source = StageSource(IdentityWriteBackTargetKind.DataStore);
        harness.Proxy.OnIdentityColumnDataRetrieved(IdentityColumn, [null], [null]);

        Assert.Equal(RetCode.OK, harness.Proxy.RefreshIdentityData(source));

        Assert.Null(Assert.Single(source.ItemSetterCalls).Value);
        Assert.Null(Assert.Single(source.FilterExpressionCalls).Value);

        // NOT ZERO. Coercing null to zero would silently claim the database assigned a key of zero.
        Assert.All(source.Calls, call => Assert.NotEqual<long?>(0L, call.Value));
    }

    #endregion

    #region 3 - THE THREE TYPE ARMS, AND THE REDRAW BRACKET ON EXACTLY ONE OF THEM

    /// <summary>
    /// ⚠️ <b>REDRAW IS SUSPENDED ON THE <c>DataWindow!</c> ARM ONLY (C-B, C-D).</b> That arm brackets
    /// its whole body with <c>SetRedraw(false)</c> [<c>:L134</c>] and <c>SetRedraw(true)</c>
    /// [<c>:L166</c>]; the structurally identical <c>DataStore!</c> arm [<c>:L167-L199</c>] calls
    /// NEITHER. Both arms are asserted, because a shared code path with a conditional would make the
    /// difference invisible.
    /// </summary>
    /// <param name="arm">Which arm the target takes.</param>
    /// <param name="expectedRedrawArguments">
    /// The redraw arguments the arm must produce, in order - suspend then restore, or nothing at all.
    /// </param>
    [Theory]
    [MemberData(nameof(RedrawBracketArms))]
    public void RedrawIsBracketedOnTheDataWindowArmAndOnNoOther(
        TargetArm arm,
        bool[] expectedRedrawArguments)
    {
        using WriteBackProxyHarness harness = new();

        WriteBackTargetSpy source = StageSource(KindOf(arm));
        harness.Proxy.OnIdentityColumnDataRetrieved(IdentityColumn, [501L, 502L], [601L]);

        Assert.Equal(RetCode.OK, harness.Proxy.RefreshIdentityData(source));

        Assert.Equal(expectedRedrawArguments, source.RedrawArguments);

        // Whichever arm was taken, the WRITES are identical - which is what makes the redraw calls the
        // only observable difference between the two arms.
        Assert.Equal(2, source.ItemSetterCalls.Count);
        Assert.Single(source.FilterExpressionCalls);
    }

    /// <summary>
    /// The two arms that reach the write-back body, with the redraw arguments each must produce.
    /// </summary>
    public static TheoryData<TargetArm, bool[]> RedrawBracketArms =>
        new()
        {
            // `case DataWindow!` - suspend [:L134] ... restore [:L166].
            { TargetArm.DataWindow, [false, true] },

            // `case DataStore!` - neither [:L167-L199].
            { TargetArm.DataStore, [] },
        };

    /// <summary>
    /// The public arm vocabulary this file carries into theory data matches the subject's internal
    /// enumeration member for member, so neither can gain an arm without the other noticing.
    /// </summary>
    [Fact]
    public void TheArmVocabularyCoversEverySubjectArmExactly()
    {
        Assert.Equal(
            Enum.GetNames<IdentityWriteBackTargetKind>(),
            Enum.GetNames<TargetArm>());

        // The mapping really answers a distinct subject arm for each member - no two collapse.
        Assert.Equal(
            Enum.GetValues<IdentityWriteBackTargetKind>(),
            Enum.GetValues<TargetArm>().Select(KindOf));
    }

    /// <summary>
    /// On the <c>DataWindow!</c> arm the suspension precedes EVERY write and the restoration follows
    /// EVERY write, which is what "brackets the whole body" means [<c>:L134, :L166</c>].
    /// </summary>
    [Fact]
    public void TheDataWindowArmSuspendsBeforeTheFirstWriteAndRestoresAfterTheLast()
    {
        using WriteBackProxyHarness harness = new();

        WriteBackTargetSpy source = StageSource(IdentityWriteBackTargetKind.DataWindow);
        harness.Proxy.OnIdentityColumnDataRetrieved(
            IdentityColumn,
            [501L, 502L],
            [601L, 602L, 603L]);

        Assert.Equal(RetCode.OK, harness.Proxy.RefreshIdentityData(source));

        IdentityWriteBackCall suspension = Assert.Single(
            source.Calls,
            call => call.Kind == IdentityWriteBackCallKind.RedrawSuspended);
        IdentityWriteBackCall restoration = Assert.Single(
            source.Calls,
            call => call.Kind == IdentityWriteBackCallKind.RedrawRestored);

        IReadOnlyList<IdentityWriteBackCall> writes =
            [.. source.Calls.Where(call =>
                call.Kind is IdentityWriteBackCallKind.ItemSetter
                    or IdentityWriteBackCallKind.FilterBufferExpression)];

        Assert.Equal(5, writes.Count);
        Assert.All(writes, write => Assert.True(write.Ordinal > suspension.Ordinal));
        Assert.All(writes, write => Assert.True(write.Ordinal < restoration.Ordinal));

        // Neither redraw call addresses a row, because neither has one to address.
        Assert.Equal(ItemStatusMachine.BeforeFirstRow, suspension.Row);
        Assert.Equal(ItemStatusMachine.BeforeFirstRow, restoration.Row);
    }

    /// <summary>
    /// The restoration happens EVEN ON THE FAILURE PATH. The oracle always falls through to its
    /// restore [<c>:L166</c>] because it has no early exit; the port's preserved out-of-range failure
    /// does have one, so the restore sits in a <c>finally</c> - which reproduces "the restore always
    /// happens" rather than a stricter or a weaker rule.
    /// </summary>
    [Fact]
    public void TheDataWindowArmRestoresRedrawEvenWhenThePreservedBoundFails()
    {
        using WriteBackProxyHarness harness = new();

        // Block one is LONGER than block two, so the shared cursor reaches past block two's array -
        // the preserved latent defect, which answers FAILED.
        harness.Proxy.OnIdentityColumnDataRetrieved(IdentityColumn, [901L, 902L], []);
        harness.Proxy.OnIdentityColumnDataRetrieved(SecondTableIdentityColumn, [911L], []);

        WriteBackTargetSpy source = StageSource(IdentityWriteBackTargetKind.DataWindow);

        Assert.Equal(RetCode.FAILED, harness.Proxy.RefreshIdentityData(source));

        Assert.Equal([false, true], source.RedrawArguments);
    }

    /// <summary>
    /// The third arm answers <see cref="RetCode.E_INVALID_TYPE"/> [<c>:L200-L201</c>] and touches the
    /// target in NO way - no redraw, no write, no status read.
    /// </summary>
    [Fact]
    public void AnUnsupportedTargetAnswersInvalidTypeAndIsNeverTouched()
    {
        using WriteBackProxyHarness harness = new();

        harness.Proxy.OnIdentityColumnDataRetrieved(IdentityColumn, [501L], []);

        WriteBackTargetSpy source = StageSource(IdentityWriteBackTargetKind.Other);

        Assert.Equal(RetCode.E_INVALID_TYPE, harness.Proxy.RefreshIdentityData(source));

        Assert.Empty(source.Calls);
    }

    /// <summary>
    /// The empty-collection answer is the GENERIC FAILURE and it is tested BEFORE the type dispatch
    /// [<c>:L128-L129</c> before <c>:L131</c>], so an unsupported target with nothing collected answers
    /// <see cref="RetCode.FAILED"/> rather than <see cref="RetCode.E_INVALID_TYPE"/>. The ORDER of the
    /// two tests is observable, and it is preserved.
    /// </summary>
    /// <param name="arm">Which arm the target would have taken.</param>
    [Theory]
    [InlineData(TargetArm.DataWindow)]
    [InlineData(TargetArm.DataStore)]
    [InlineData(TargetArm.Other)]
    public void NothingCollectedAnswersFailedWhateverArmTheTargetWouldHaveTaken(TargetArm arm)
    {
        using WriteBackProxyHarness harness = new();

        WriteBackTargetSpy source = StageSource(KindOf(arm));

        Assert.Equal(RetCode.FAILED, harness.Proxy.RefreshIdentityData(source));
        Assert.Empty(source.Calls);
    }

    /// <summary>
    /// A null target answers <see cref="RetCode.E_INVALID_OBJECT"/> [<c>:L126</c>], and it does so
    /// AFTER the busy test and BEFORE the collection test.
    /// </summary>
    [Fact]
    public void ANullTargetAnswersInvalidObject()
    {
        using WriteBackProxyHarness harness = new();

        Assert.Equal(RetCode.E_INVALID_OBJECT, harness.Proxy.RefreshIdentityData(null!));
    }

    /// <summary>
    /// A busy task refuses the write-back with <see cref="RetCode.E_BUSY"/> [<c>:L125</c>], which is
    /// the FIRST test of all - it wins even over the invalid-object arm.
    /// </summary>
    [Fact]
    public void ABusyTaskRefusesTheWriteBackBeforeAnyOtherTest()
    {
        using WriteBackProxyHarness harness = new();

        harness.Proxy.OnIdentityColumnDataRetrieved(IdentityColumn, [501L], []);
        harness.Host.MakeBusy();

        WriteBackTargetSpy source = StageSource(IdentityWriteBackTargetKind.DataStore);

        Assert.Equal(RetCode.E_BUSY, harness.Proxy.RefreshIdentityData(source));
        Assert.Equal(RetCode.E_BUSY, harness.Proxy.RefreshIdentityData(null!));
        Assert.Empty(source.Calls);
    }

    #endregion

    #region 4 - BOTH WALKS ARE BOUNDED BY TABLE ONE'S ARRAY LENGTH, EVEN IN A MULTI-TABLE RUN

    /// <summary>
    /// ⚠️ <b>A LATENT DEFECT PRESERVED, NOT FIXED (C-B).</b> Both passes take their bound from
    /// <c>_idColDatas[1]</c> ONLY - <c>n = UpperBound(_idColDatas[1].primaryValues)</c> [<c>:L136,
    /// :L170</c>] and its Filter twin [<c>:L151, :L185</c>] - and then index EVERY table's array with
    /// that same cursor [<c>:L143-L145, :L159-L161</c>]. When table one's array is SHORTER, the longer
    /// table's surplus values are simply never applied, and that is observable.
    /// </summary>
    /// <remarks>
    /// The two blocks deliberately name DIFFERENT identity columns, so each block's write is
    /// attributable to it and a merged write cannot pass.
    /// </remarks>
    [Fact]
    public void TableOnesShorterArrayBoundsEveryOtherTablesWalk()
    {
        using WriteBackProxyHarness harness = new();

        // Table 1 collected ONE primary value; table 2 collected THREE. The bound is table 1's.
        harness.Proxy.OnIdentityColumnDataRetrieved(IdentityColumn, [901L], []);
        harness.Proxy.OnIdentityColumnDataRetrieved(
            SecondTableIdentityColumn,
            [911L, 912L, 913L],
            []);

        WriteBackTargetSpy source = StageSource(IdentityWriteBackTargetKind.DataStore);

        Assert.Equal(RetCode.OK, harness.Proxy.RefreshIdentityData(source));

        // ONE qualifying row served, not two: the walk broke at `if i > n then exit` [:L142] after the
        // first row, because table one's bound is one. Table two's second and third values are LOST,
        // and that loss is the preserved behaviour.
        Assert.Equal(
            [
                (2L, (long)IdentityColumn, (long?)901L),
                (2L, (long)SecondTableIdentityColumn, (long?)911L),
            ],
            source.ItemSetterCalls.Select(call => (call.Row, call.ColumnId, call.Value)));

        // Row 4 - the second qualifying row - received NOTHING from either table.
        Assert.Null(source.AppliedValueAt(DwBuffer.Primary, 4L, IdentityColumn));
        Assert.Null(source.AppliedValueAt(DwBuffer.Primary, 4L, SecondTableIdentityColumn));
    }

    /// <summary>
    /// The mirror case: table one's array is LONGER, so the shared cursor reaches past a later table's
    /// shorter array. PowerBuilder raises an array-boundary runtime error there; the port answers
    /// <see cref="RetCode.FAILED"/> instead, which keeps the member inside the return-code algebra its
    /// callers expect. THE BOUND ITSELF IS NOT WIDENED, because widening it would change which rows
    /// receive values.
    /// </summary>
    /// <param name="tableOneValues">Table one's collected values, which supply the bound.</param>
    /// <param name="tableTwoValues">Table two's collected values, indexed by table one's cursor.</param>
    /// <param name="expectedCode">The answer.</param>
    /// <param name="expectedItemSetterCalls">
    /// How many item-setter calls happen before the answer - non-zero on the failing case, because the
    /// oracle applies what it can and only then walks off the end.
    /// </param>
    [Theory]
    [MemberData(nameof(TableOneBoundMatrix))]
    public void TheSharedCursorIsAlwaysTableOnesEvenWhenThatReachesPastAnotherTable(
        long?[] tableOneValues,
        long?[] tableTwoValues,
        long expectedCode,
        int expectedItemSetterCalls)
    {
        using WriteBackProxyHarness harness = new();

        harness.Proxy.OnIdentityColumnDataRetrieved(IdentityColumn, tableOneValues, []);
        harness.Proxy.OnIdentityColumnDataRetrieved(SecondTableIdentityColumn, tableTwoValues, []);

        WriteBackTargetSpy source = StageSource(IdentityWriteBackTargetKind.DataStore);

        Assert.Equal(expectedCode, harness.Proxy.RefreshIdentityData(source));
        Assert.Equal(expectedItemSetterCalls, source.ItemSetterCalls.Count);
    }

    /// <summary>
    /// The three shapes a two-table run can take, distinguished by which array is longer.
    /// </summary>
    public static TheoryData<long?[], long?[], long, int> TableOneBoundMatrix =>
        new()
        {
            // EQUAL LENGTHS - the ordinary case. Two qualifying rows, two blocks, four writes.
            { [901L, 902L], [911L, 912L], RetCode.OK, 4 },

            // TABLE ONE SHORTER - the bound stops the walk early and table two's surplus is dropped.
            { [901L], [911L, 912L], RetCode.OK, 2 },

            // TABLE ONE LONGER - the cursor reaches past table two on the SECOND qualifying row, so
            // the first row's two writes have already happened when FAILED is answered.
            { [901L, 902L], [911L], RetCode.FAILED, 3 },
        };

    /// <summary>
    /// An EMPTY array for a buffer skips that buffer's whole pass [<c>:L137, :L152</c>] rather than
    /// failing, which is the "nothing was collected for this buffer" reading - and the OTHER buffer
    /// still runs.
    /// </summary>
    [Theory]
    [MemberData(nameof(EmptyBufferArrays))]
    public void AnEmptyArrayForOneBufferSkipsOnlyThatBuffersPass(
        long?[] primaryValues,
        long?[] filterValues,
        int expectedItemSetterCalls,
        int expectedFilterExpressionCalls)
    {
        using WriteBackProxyHarness harness = new();

        harness.Proxy.OnIdentityColumnDataRetrieved(IdentityColumn, primaryValues, filterValues);

        WriteBackTargetSpy source = StageSource(IdentityWriteBackTargetKind.DataStore);

        Assert.Equal(RetCode.OK, harness.Proxy.RefreshIdentityData(source));

        Assert.Equal(expectedItemSetterCalls, source.ItemSetterCalls.Count);
        Assert.Equal(expectedFilterExpressionCalls, source.FilterExpressionCalls.Count);
    }

    /// <summary>
    /// The four combinations of empty and non-empty value arrays.
    /// </summary>
    public static TheoryData<long?[], long?[], int, int> EmptyBufferArrays =>
        new()
        {
            // Both populated - the staged source has two qualifying Primary rows and three Filter rows.
            { [501L, 502L], [601L, 602L, 603L], 2, 3 },

            // Primary only - the Filter pass is skipped entirely.
            { [501L, 502L], [], 2, 0 },

            // Filter only - the Primary pass is skipped entirely.
            { [], [601L, 602L, 603L], 0, 3 },

            // Neither - both passes skipped, and the answer is still OK because a block EXISTS.
            { [], [], 0, 0 },
        };

    #endregion

    #region 5 - THE FINALIZE GATE - auto-invoked ONLY on a successful exit code

    /// <summary>
    /// The write-back is auto-invoked from the finalize hook when the last exit code is
    /// <see cref="RetCode.OK"/> and an update object is retained [<c>:L303-L307</c>]. The gate is a
    /// literal EQUALITY against OK, not a success predicate.
    /// </summary>
    [Fact]
    public void TheFinalizeHookRunsTheWriteBackWhenTheRunSucceeded()
    {
        using WriteBackProxyHarness harness = new();

        WriteBackTargetSpy source = StageSource(IdentityWriteBackTargetKind.DataWindow);

        // Retained WITHOUT handing over data, so nothing but the write-back is exercised.
        Assert.Equal(RetCode.OK, harness.Proxy.SetUpdateObject(source, applyData: false));
        harness.Proxy.OnIdentityColumnDataRetrieved(IdentityColumn, [501L, 502L], [601L]);

        harness.Host.LastExitCodeValue = RetCode.OK;
        harness.Proxy.OnFinalize();

        // The values landed, and the DataWindow arm's bracket happened - so it really was the
        // write-back that ran and not something weaker.
        Assert.Equal<long?>(501L, source.AppliedValueAt(DwBuffer.Primary, 2L, IdentityColumn));
        Assert.Equal<long?>(502L, source.AppliedValueAt(DwBuffer.Primary, 4L, IdentityColumn));
        Assert.Equal<long?>(601L, source.AppliedValueAt(DwBuffer.Filter, 1L, IdentityColumn));
        Assert.Equal([false, true], source.RedrawArguments);
    }

    /// <summary>
    /// ⚠️ <b>THE NEGATIVE ARM, WHICH MATTERS AS MUCH AS THE POSITIVE ONE.</b> For ANY exit code other
    /// than <see cref="RetCode.OK"/> the hook does nothing at all [<c>:L303</c>] - including for
    /// <see cref="RetCode.PREVENT"/>, which the tri-state algebra classifies as a SUCCESS
    /// [<c>ws_objects/pfw.shared.pbl.src/issucceeded.srf:L11-L13</c>]. The oracle compares for equality
    /// with OK rather than asking the predicate, so a prevention does NOT trigger the write-back, and
    /// substituting the predicate here would be a behavioural change.
    /// </summary>
    /// <param name="exitCode">The exit code the host reports.</param>
    [Theory]
    [MemberData(nameof(NonSuccessExitCodes))]
    public void TheFinalizeHookSkipsTheWriteBackForEveryOtherExitCode(long exitCode)
    {
        using WriteBackProxyHarness harness = new();

        WriteBackTargetSpy source = StageSource(IdentityWriteBackTargetKind.DataWindow);

        Assert.Equal(RetCode.OK, harness.Proxy.SetUpdateObject(source, applyData: false));
        harness.Proxy.OnIdentityColumnDataRetrieved(IdentityColumn, [501L, 502L], [601L]);

        harness.Host.LastExitCodeValue = exitCode;
        harness.Proxy.OnFinalize();

        // NO write-back at all: not a partial one, not a bracketed empty one.
        Assert.Empty(source.Calls);
        Assert.Null(source.AppliedValueAt(DwBuffer.Primary, 2L, IdentityColumn));
    }

    /// <summary>
    /// The exit codes that must NOT trigger the write-back, including the two that a predicate-based
    /// gate would wrongly admit.
    /// </summary>
    public static TheoryData<long> NonSuccessExitCodes =>
        new()
        {
            // The ordinary failure.
            RetCode.FAILED,

            // ⚠️ A PREVENTION IS CLASSIFIED AS A SUCCESS BY THE ALGEBRA AND STILL DOES NOT QUALIFY
            // HERE, because the oracle's gate is `= RetCode.OK` [:L303].
            RetCode.PREVENT,

            // Neither succeeded nor failed - the tri-state hole.
            RetCode.CANCELLED,

            // A representative error code from the contiguous block.
            RetCode.E_INVALID_ARGUMENT,

            // The busy code, which is what a still-running task would report.
            RetCode.E_BUSY,
        };

    /// <summary>
    /// With NO retained object the hook does nothing even on a successful exit code [<c>:L304</c>] -
    /// and <see cref="SqlUpdateTaskProxy.Reset"/> is what releases the retention [<c>:L94</c>], which
    /// is the mirror of the worker-side release discipline the threading notes require.
    /// </summary>
    [Fact]
    public void ResetReleasesTheRetainedObjectSoTheFinalizeHookHasNothingToApplyOnto()
    {
        using WriteBackProxyHarness harness = new();

        WriteBackTargetSpy source = StageSource(IdentityWriteBackTargetKind.DataWindow);

        Assert.Equal(RetCode.OK, harness.Proxy.SetUpdateObject(source, applyData: false));
        harness.Proxy.OnIdentityColumnDataRetrieved(IdentityColumn, [501L], []);
        harness.Host.LastExitCodeValue = RetCode.OK;

        Assert.Equal(RetCode.OK, harness.Proxy.Reset());

        harness.Proxy.OnFinalize();

        Assert.Empty(source.Calls);

        // Reset cleared the blocks too, so even an explicit call now answers the empty-collection
        // failure rather than applying anything [:L93 with :L129].
        Assert.Equal(RetCode.FAILED, harness.Proxy.RefreshIdentityData(source));
    }

    #endregion

    #region 6 - CALLER-SIDE ACCUMULATION - the worker fires per table, the totals live only here

    /// <summary>
    /// 🔴 <b>THE THREE COUNTERS ACCUMULATE ADDITIVELY ACROSS A MULTI-TABLE RUN</b>
    /// [<c>:L66-L68</c>]. The worker fires <c>OnUpdated</c> ONCE PER TABLE
    /// [<c>n_cst_thread_task_sqlupdate.sru:L247</c>, from the multi-table loop at <c>:L363-L368</c>,
    /// which calls <c>_of_Update</c> at <c>:L366</c> once per descriptor] and sums
    /// nothing itself, so if this side assigned instead of adding, the totals would not exist anywhere.
    /// </summary>
    /// <remarks>
    /// Driven with two tables whose counts DIFFER in all three positions, so an assignment defect
    /// cannot coincide with the correct answer, and so a transposition of the three arguments is
    /// visible as well.
    /// </remarks>
    [Theory]
    [MemberData(nameof(MultiTableCountRuns))]
    public void TheThreeCountersAccumulateAdditivelyRatherThanTakingTheLastTablesValues(
        long[][] perTableCounts,
        long expectedInserted,
        long expectedUpdated,
        long expectedDeleted)
    {
        using WriteBackProxyHarness harness = new();

        foreach (long[] counts in perTableCounts)
        {
            harness.Proxy.OnUpdated(counts[0], counts[1], counts[2]);
        }

        Assert.Equal(expectedInserted, harness.Proxy.GetRowsInserted());
        Assert.Equal(expectedUpdated, harness.Proxy.GetRowsUpdated());
        Assert.Equal(expectedDeleted, harness.Proxy.GetRowsDeleted());

        // A self-check of the theory data itself: a mistyped expectation above would otherwise assert
        // the wrong thing silently, and the sums are what "accumulate additively" means.
        Assert.Equal(perTableCounts.Sum(counts => counts[0]), expectedInserted);
        Assert.Equal(perTableCounts.Sum(counts => counts[1]), expectedUpdated);
        Assert.Equal(perTableCounts.Sum(counts => counts[2]), expectedDeleted);

        if (perTableCounts.Length > 1)
        {
            long[] lastTable = perTableCounts[^1];

            // A LAST-WINS DEFECT WOULD ANSWER THE LAST TABLE'S TRIPLE. Compared as a TRIPLE rather
            // than member by member, because a table reporting zero in one position legitimately makes
            // that one member coincide with the total - the fourth theory row is exactly that shape,
            // and a per-member non-equality check would fail on it while the accumulation was correct.
            Assert.NotEqual(
                (lastTable[0], lastTable[1], lastTable[2]),
                (harness.Proxy.GetRowsInserted(),
                    harness.Proxy.GetRowsUpdated(),
                    harness.Proxy.GetRowsDeleted()));
        }
    }

    /// <summary>
    /// The per-table count runs, in the oracle's argument order - inserted, updated, deleted
    /// [<c>n_cst_thread_task_sqlupdate.sru:L247</c>].
    /// </summary>
    public static TheoryData<long[][], long, long, long> MultiTableCountRuns =>
        new()
        {
            // A SINGLE-TABLE RUN: the totals equal that table's own counts, un-accumulated.
            { [[3L, 5L, 7L]], 3L, 5L, 7L },

            // TWO TABLES WITH DIFFERENT COUNTS in every position - the required shape.
            { [[3L, 5L, 7L], [11L, 13L, 17L]], 14L, 18L, 24L },

            // THREE TABLES, to show the accumulation is not a two-term special case.
            { [[1L, 2L, 3L], [10L, 20L, 30L], [100L, 200L, 300L]], 111L, 222L, 333L },

            // A ZERO-BEARING TABLE still contributes: a pure update reports no inserts and no deletes
            // and the oracle's call sits OUTSIDE the inserted-count block [:L215 versus :L247].
            { [[0L, 4L, 0L], [2L, 0L, 6L]], 2L, 4L, 6L },
        };

    /// <summary>
    /// Identity blocks are APPENDED ONE PER TABLE at <c>UpperBound(_idColDatas) + 1</c> [<c>:L73</c>],
    /// preserving TABLE ORDER - which is what makes the write-back's <c>_idColDatas[1]</c> read address
    /// the FIRST reported table.
    /// </summary>
    [Fact]
    public void IdentityBlocksAreAppendedOnePerTableInTableOrder()
    {
        using WriteBackProxyHarness harness = new();

        harness.Proxy.OnIdentityColumnDataRetrieved(IdentityColumn, [901L], [902L]);
        harness.Proxy.OnIdentityColumnDataRetrieved(SecondTableIdentityColumn, [911L], []);
        harness.Proxy.OnIdentityColumnDataRetrieved(DwSqliteFixture.SalaryColumnNumber, [], [921L]);

        // THREE firings, THREE blocks, IN ORDER - the append lands one past the last valid index every
        // time, which is what List.Add does by definition (AAP Section 0.4.5.4).
        Assert.Equal(
            [IdentityColumn, SecondTableIdentityColumn, DwSqliteFixture.SalaryColumnNumber],
            harness.Proxy.IdentityBlocks.Select(block => (int)block.IdentityColumnId));

        // Position ONE is the first table's block, which is the one the write-back's bound comes from.
        Assert.Equal(
            (long)IdentityColumn,
            harness.Proxy.IdentityBlocks[
                OneBasedIndex.ToZeroBased(
                    OneBasedIndex.FirstIndex,
                    OneBasedIndex.UpperBound(harness.Proxy.IdentityBlocks),
                    nameof(OneBasedIndex.FirstIndex))].IdentityColumnId);

        // The append position IS the one-based append index, stated through the helper the subject
        // itself routes through rather than as an arithmetic claim.
        Assert.Equal(3, OneBasedIndex.UpperBound(harness.Proxy.IdentityBlocks));
    }

    /// <summary>
    /// A single-table run yields ONE block and un-accumulated counts equal to that table's own.
    /// </summary>
    [Fact]
    public void ASingleTableRunYieldsOneBlockAndUnaccumulatedCounts()
    {
        using WriteBackProxyHarness harness = new();

        harness.Proxy.OnUpdated(2L, 0L, 1L);
        harness.Proxy.OnIdentityColumnDataRetrieved(IdentityColumn, [501L, 502L], []);

        Assert.Single(harness.Proxy.IdentityBlocks);
        Assert.Equal(2L, harness.Proxy.GetRowsInserted());
        Assert.Equal(0L, harness.Proxy.GetRowsUpdated());
        Assert.Equal(1L, harness.Proxy.GetRowsDeleted());
    }

    /// <summary>
    /// The multi-table flag is held on BOTH sides: the setter writes the proxy's own copy AND forwards
    /// to the worker [<c>:L209, :L211</c>]. The local copy is observable through the capture gate, which
    /// forces a changeset capture even when the update table answers the UNKNOWN marker [<c>:L254</c>].
    /// </summary>
    /// <param name="multiTable">Whether multi-table update is switched on.</param>
    /// <param name="expectedCaptures">Whether the capture happens for an unknown update table.</param>
    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 1)]
    public void TheMultiTableFlagIsWrittenOnBothSidesAndDrivesTheCaptureGate(
        bool multiTable,
        int expectedCaptures)
    {
        using WriteBackProxyHarness harness = new();

        Assert.Equal(RetCode.OK, harness.Proxy.SetMultiTableUpdate(multiTable));

        // The worker's copy [:L211].
        Assert.Equal(multiTable, harness.Worker.MultiTableUpdate);

        // The proxy's own copy, observed through the gate: with the update table answering "?" the
        // capture happens ONLY because the flag forces it - the condition is an OR [:L254].
        WriteBackTargetSpy source = new(IdentityWriteBackTargetKind.DataWindow)
        {
            UpdateTableAnswer = ConflictDetector.DescribeUnknownMarker,
            SyntaxAnswer = "release 12.5;",
            ChangeRowCount = 4L,
        };

        Assert.Equal(RetCode.OK, harness.Proxy.SetUpdateObject(source));

        Assert.Equal(expectedCaptures, source.GetChangesCalls);

        // The syntax and the payload count are forwarded whether or not the gate opened [:L257-L258],
        // which is why the worker sees the definition in both rows of this theory.
        Assert.Equal("release 12.5;", harness.Worker.SqlSyntax);
        Assert.Equal(multiTable ? 4L : 0L, harness.Worker.GetUpdateRows());
    }

    /// <summary>
    /// The retained object survives an unsupported second target: the third arm returns
    /// <see cref="RetCode.E_INVALID_TYPE"/> WITHOUT retaining [<c>:L270-L271</c>], so whatever was
    /// retained before stays retained - observable through the finalize hook still finding it.
    /// </summary>
    [Fact]
    public void AnUnsupportedTargetLeavesAPreviouslyRetainedObjectInPlace()
    {
        using WriteBackProxyHarness harness = new();

        WriteBackTargetSpy retained = StageSource(IdentityWriteBackTargetKind.DataStore);
        WriteBackTargetSpy rejected = StageSource(IdentityWriteBackTargetKind.Other);

        Assert.Equal(RetCode.OK, harness.Proxy.SetUpdateObject(retained, applyData: false));
        Assert.Equal(RetCode.E_INVALID_TYPE, harness.Proxy.SetUpdateObject(rejected, applyData: false));

        harness.Proxy.OnIdentityColumnDataRetrieved(IdentityColumn, [501L, 502L], []);
        harness.Host.LastExitCodeValue = RetCode.OK;
        harness.Proxy.OnFinalize();

        // The FIRST target received the write-back; the rejected one was never touched.
        Assert.Equal(2, retained.ItemSetterCalls.Count);
        Assert.Empty(rejected.Calls);
    }

    #endregion

    #region 7 - THE LAST-ERROR ROW REWRITE - Row alone, over the PRIMARY buffer alone

    /// <summary>
    /// The proxy OVERRIDES the base's last-error-wins sink - whose entire body is
    /// <c>_lastDBError = err</c> [<c>n_cst_threading_task_sqlbase.sru:L44</c>] - to rewrite ONLY
    /// <c>Row</c>, translating the worker's ORDINAL AMONG MODIFIED ROWS into an actual buffer row
    /// [<c>:L323-L325</c>].
    /// </summary>
    /// <remarks>
    /// ⚠️ The ordinal counts EVERY modified row, not only newly-modified ones [<c>:L323</c>] - which is
    /// the difference from the write-back's walk, and it is deliberate: the worker's ordinal counts the
    /// same population, so the two must agree or the translation lands on the wrong row.
    /// </remarks>
    /// <param name="reportedOrdinal">The ordinal among modified rows the worker reported.</param>
    /// <param name="expectedRow">The buffer row it must be rewritten to.</param>
    [Theory]
    [MemberData(nameof(OrdinalToRowTranslations))]
    public void TheRowIsRewrittenFromAnOrdinalAmongModifiedRowsToAnActualRow(
        long reportedOrdinal,
        long expectedRow)
    {
        using WriteBackProxyHarness harness = new();

        WriteBackTargetSpy source = StageSource(IdentityWriteBackTargetKind.DataStore);
        Assert.Equal(RetCode.OK, harness.Proxy.SetUpdateObject(source, applyData: false));

        harness.PublishDbError(new DbErrorData { SqlDbCode = -100L, Row = reportedOrdinal });

        Assert.Equal(expectedRow, harness.Proxy.GetLastDbErrorData().Row);
    }

    /// <summary>
    /// The staged source's Primary buffer is row 1 data-modified, row 2 newly-modified, row 3
    /// untouched, row 4 data-modified - so the modified population is rows 1, 2 and 4, and the ordinals
    /// one, two and three map onto them in order.
    /// </summary>
    public static TheoryData<long, long> OrdinalToRowTranslations =>
        new()
        {
            // Ordinal one is the FIRST modified row, which is row one.
            { 1L, 1L },

            // Ordinal two skips nothing yet - row two is also modified.
            { 2L, 2L },

            // ★ Ordinal three lands on row FOUR, because row three is unmodified and the walk never
            //   visits it. An implementation that counted rows rather than modified rows answers 3.
            { 3L, 4L },

            // AN ORDINAL BEYOND THE MODIFIED POPULATION is left exactly as reported: the loop simply
            // ends [:L322] and nothing is rewritten.
            { 4L, 4L },
            { 99L, 99L },
        };

    /// <summary>
    /// 🔴 <b>EVERY OTHER FIELD OF THE FIVE IS LEFT EXACTLY AS THE WORKER SET IT (C-F).</b> Asserted
    /// FIELD BY FIELD - <c>SqlDbCode</c>, <c>SqlErrText</c>, <c>SqlSyntax</c> and <c>Buffer</c> - because
    /// a rewrite that also touched <c>SqlSyntax</c> would bypass the redaction contract, and comparing
    /// whole records would not say WHICH field moved.
    /// </summary>
    [Fact]
    public void OnlyTheRowIsRewrittenAndTheOtherFourFieldsAreUntouched()
    {
        using WriteBackProxyHarness harness = new();

        WriteBackTargetSpy source = StageSource(IdentityWriteBackTargetKind.DataStore);
        Assert.Equal(RetCode.OK, harness.Proxy.SetUpdateObject(source, applyData: false));

        // The statement text is the one field that carries interpolated literals in the legacy, so it
        // is the field the redaction contract protects. It must arrive and leave IDENTICAL.
        DbErrorData reported = new()
        {
            SqlDbCode = -8175L,
            SqlErrText = "column mismatch",
            SqlSyntax = "UPDATE COMPANY SET address = 'Norway' WHERE id = 2 AND address = 'Texas'",
            Buffer = DwBuffer.Filter,
            Row = 3L,
        };

        harness.PublishDbError(reported);

        DbErrorData latched = harness.Proxy.GetLastDbErrorData();

        // THE ONE FIELD THAT MOVED.
        Assert.Equal(4L, latched.Row);
        Assert.NotEqual(reported.Row, latched.Row);

        // THE FOUR THAT DID NOT, each asserted on its own.
        Assert.Equal(reported.SqlDbCode, latched.SqlDbCode);
        Assert.Equal(reported.SqlErrText, latched.SqlErrText);
        Assert.Equal(reported.SqlSyntax, latched.SqlSyntax);
        Assert.Equal(reported.Buffer, latched.Buffer);

        // Stated positively as well: the latched payload is the reported one with Row replaced, which
        // is exactly what a with-expression on one member means.
        Assert.Equal(reported with { Row = 4L }, latched);
    }

    /// <summary>
    /// The translation walks the <c>Primary!</c> buffer ONLY [<c>:L321, :L328</c>] - never the
    /// <c>Filter!</c> or <c>Delete!</c> buffer - even when the reported error names one of those as the
    /// offending buffer. That gap is the oracle's, and the <c>Buffer</c> field is carried through
    /// untouched beside the Primary-derived row.
    /// </summary>
    /// <param name="buffer">The buffer the worker named.</param>
    [Theory]
    [InlineData(DwBuffer.Primary)]
    [InlineData(DwBuffer.Filter)]
    [InlineData(DwBuffer.Delete)]
    public void TheTranslationAlwaysUsesThePrimaryBufferWhateverBufferTheErrorNames(DwBuffer buffer)
    {
        using WriteBackProxyHarness harness = new();

        // A source whose OTHER two buffers hold modified rows in a DIFFERENT arrangement, so a
        // translation that consulted them would answer differently.
        WriteBackTargetSpy source = StageSource(IdentityWriteBackTargetKind.DataStore);
        _ = source.SeedRow(DwBuffer.Delete, ItemStatus.DataModified, "deleted-one", TokenColumn);
        _ = source.SeedRow(DwBuffer.Delete, ItemStatus.DataModified, "deleted-two", TokenColumn);

        Assert.Equal(RetCode.OK, harness.Proxy.SetUpdateObject(source, applyData: false));

        harness.PublishDbError(new DbErrorData { Buffer = buffer, Row = 3L });

        DbErrorData latched = harness.Proxy.GetLastDbErrorData();

        // Row FOUR is the third MODIFIED PRIMARY row. The Filter buffer's third row does not exist and
        // the Delete buffer's does not either, so any other answer means another buffer was walked.
        Assert.Equal(4L, latched.Row);
        Assert.Equal(buffer, latched.Buffer);
    }

    /// <summary>
    /// A non-positive reported row is left alone [<c>:L315</c>]: it means "no row", and the ordinal
    /// count starts at one, so there is nothing to match.
    /// </summary>
    /// <param name="reportedRow">The non-positive row.</param>
    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void ANonPositiveReportedRowIsNotTranslated(long reportedRow)
    {
        using WriteBackProxyHarness harness = new();

        WriteBackTargetSpy source = StageSource(IdentityWriteBackTargetKind.DataStore);
        Assert.Equal(RetCode.OK, harness.Proxy.SetUpdateObject(source, applyData: false));

        harness.PublishDbError(new DbErrorData { SqlDbCode = -1L, Row = reportedRow });

        Assert.Equal(reportedRow, harness.Proxy.GetLastDbErrorData().Row);
    }

    /// <summary>
    /// LAST-ERROR-WINS STILL HOLDS AFTER THE OVERRIDE, because the override calls the ancestor FIRST
    /// [<c>:L310</c>]: three errors in, only the third is retained - with ITS row translated and the
    /// earlier two gone entirely.
    /// </summary>
    [Fact]
    public void ThreeErrorsLeaveOnlyTheThirdRetainedWithItsRowTranslated()
    {
        using WriteBackProxyHarness harness = new();

        WriteBackTargetSpy source = StageSource(IdentityWriteBackTargetKind.DataStore);
        Assert.Equal(RetCode.OK, harness.Proxy.SetUpdateObject(source, applyData: false));

        harness.PublishDbError(new DbErrorData { SqlDbCode = -1L, SqlErrText = "first", Row = 1L });
        Assert.Equal(1L, harness.Proxy.GetLastDbErrorData().Row);

        harness.PublishDbError(new DbErrorData { SqlDbCode = -2L, SqlErrText = "second", Row = 2L });
        Assert.Equal(2L, harness.Proxy.GetLastDbErrorData().Row);

        harness.PublishDbError(new DbErrorData { SqlDbCode = -3L, SqlErrText = "third", Row = 3L });

        DbErrorData latched = harness.Proxy.GetLastDbErrorData();

        // Wholly replaced - the earlier codes and texts are gone - and the THIRD ordinal is the one
        // translated, onto Primary row four.
        Assert.Equal(-3L, latched.SqlDbCode);
        Assert.Equal("third", latched.SqlErrText);
        Assert.Equal(4L, latched.Row);
    }

    /// <summary>
    /// With NO retained object the error is still LATCHED but not translated [<c>:L314</c>] - the
    /// ancestor's assignment happens first and the guard only skips the rewrite.
    /// </summary>
    [Fact]
    public void AnErrorIsLatchedWithoutTranslationWhenNoObjectIsRetained()
    {
        using WriteBackProxyHarness harness = new();

        harness.PublishDbError(new DbErrorData { SqlDbCode = -5L, SqlErrText = "no object", Row = 3L });

        DbErrorData latched = harness.Proxy.GetLastDbErrorData();

        Assert.Equal(-5L, latched.SqlDbCode);
        Assert.Equal("no object", latched.SqlErrText);
        Assert.Equal(3L, latched.Row);
    }

    #endregion

    #region 8 - THE SINGLE NOTIFY CODE, ITS NUMERIC COLLISION, AND THE LOW-WORD PAYLOAD

    /// <summary>
    /// ⚠️ <b>THE COLLISION IS PRESERVED PER-CLASS NAMESPACING, NOT A DEFECT (C-B, C-K).</b> This
    /// object declares exactly one notify code, <c>Constant Long NCD_PROGRESS = 1</c> [<c>:L32</c>],
    /// and the sibling query proxy declares <c>Constant Long NCD_MAXROWS = 1</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlquery.sru:L28</c>] - THE SAME
    /// NUMBER for an entirely unrelated notification. In PowerBuilder they never collide because each
    /// is a per-class constant; a single merged .NET enumeration WOULD alias them, and a caller
    /// switching on the merged value would read a row-cap breach as an update progress report.
    /// </summary>
    /// <remarks>
    /// The two enumerations are CONSUMED from <c>Buffers/DataWindowBuffers.cs</c>; neither is
    /// re-declared by the proxy, which is asserted here through their declaring namespace.
    /// </remarks>
    [Fact]
    public void TheProgressCodeIsOneAndCollidesNumericallyWithTheQueryProxysMaxRowsCode()
    {
        // The VALUE, preserved.
        Assert.Equal(1L, (long)SqlUpdateTaskNotifyCode.Progress);

        // THE COLLISION, asserted rather than avoided.
        Assert.Equal((long)SqlQueryTaskNotifyCode.MaxRows, (long)SqlUpdateTaskNotifyCode.Progress);

        // TWO SEPARATE TYPES, which is what keeps the collision harmless.
        Assert.NotEqual(typeof(SqlUpdateTaskNotifyCode), typeof(SqlQueryTaskNotifyCode));

        // Both consumed from Buffers/, not re-declared in TaskProxies/.
        Assert.Equal(
            typeof(ItemStatusMachine).Namespace,
            typeof(SqlUpdateTaskNotifyCode).Namespace);
        Assert.Equal(
            typeof(SqlUpdateTaskNotifyCode).Namespace,
            typeof(SqlQueryTaskNotifyCode).Namespace);

        // The update vocabulary has exactly ONE member, matching the oracle's single constant.
        Assert.Single(Enum.GetValues<SqlUpdateTaskNotifyCode>());
    }

    /// <summary>
    /// The payload is <c>MakeLong(current, total)</c> with CURRENT IN THE LOW WORD and total in the
    /// high word [<c>:L32</c>], and the caller-side decode reads it back in that order.
    /// </summary>
    /// <param name="current">The statement number reached.</param>
    /// <param name="total">The statement count expected.</param>
    [Theory]
    [MemberData(nameof(ProgressPayloads))]
    public void TheProgressPayloadCarriesCurrentInTheLowWordAndTotalInTheHigh(int current, int total)
    {
        // Packed exactly as Buffers/DataWindowBuffers.cs packs it, through the ported primitive
        // [ws_objects/pfw.common.pbl.src/makelong.srf]. PowerBuilder's `uint` is SIXTEEN bits, which is
        // why the operands are ushort.
        long payload = unchecked((long)Bits.MakeLong((ushort)current, (ushort)total));

        Assert.True(
            SqlUpdateTaskProxy.TryDecodeProgress(
                (long)SqlUpdateTaskNotifyCode.Progress,
                payload,
                out long decodedCurrent,
                out long decodedTotal));

        Assert.Equal(current, decodedCurrent);
        Assert.Equal(total, decodedTotal);

        // THE HALVES, NAMED. Current is the LOW word; total is the HIGH word. Asserted against the
        // primitives themselves so a transposed pack cannot pass by round-tripping.
        Assert.Equal(Bits.LoWord(unchecked((uint)payload)), (ushort)decodedCurrent);
        Assert.Equal(Bits.HiWord(unchecked((uint)payload)), (ushort)decodedTotal);

        // A TRANSPOSED PACK IS OBSERVABLY DIFFERENT whenever the two differ, which is what makes the
        // low-word claim a real claim.
        if (current != total)
        {
            Assert.NotEqual(
                payload,
                unchecked((long)Bits.MakeLong((ushort)total, (ushort)current)));
        }
    }

    /// <summary>
    /// Progress payloads, including the boundary values of the sixteen-bit halves.
    /// </summary>
    public static TheoryData<int, int> ProgressPayloads =>
        new()
        {
            // The first statement of a run - the oracle always emits this one [:L37].
            { 1, 1 },

            // An ordinary mid-run report where the two halves differ.
            { 3, 10 },

            // A zero total, which the oracle emits when nothing was counted.
            { 1, 0 },

            // THE SIXTEEN-BIT CEILING of each half, which is where the legacy's packing truncates.
            { ushort.MaxValue, ushort.MaxValue },
            { ushort.MaxValue, 1 },
            { 1, ushort.MaxValue },
        };

    /// <summary>
    /// The decode refuses ANY other notify code and leaves both outputs cleared, so a query task's
    /// row-cap notification carrying the same number is never decoded as update progress.
    /// </summary>
    /// <param name="wparam">The notify code offered.</param>
    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]

    // 2 is the query task's own DataReceived code, which shares this file's payload shape and must
    // still be refused [n_cst_threading_task_sqlquery.sru:L29].
    [InlineData((long)SqlQueryTaskNotifyCode.DataReceived)]

    // A higher query code, to show the refusal is not a "greater than one" test.
    [InlineData((long)SqlQueryTaskNotifyCode.ChildQuery)]
    public void TheDecodeRefusesEveryNotifyCodeOtherThanProgress(long wparam)
    {
        Assert.False(
            SqlUpdateTaskProxy.TryDecodeProgress(wparam, 0x000A0003L, out long current, out long total));

        Assert.Equal(0L, current);
        Assert.Equal(0L, total);
    }

    #endregion

    #region 9 - THE PRESERVED SETTER ASSERTION ASYMMETRY, AND THE DELEGATING SETTERS

    /// <summary>
    /// ⚠️ 🔴 <b>THE ASSERTION ASYMMETRY IS PRESERVED LEGACY BEHAVIOUR AND MUST NOT BE HARMONISED
    /// (C-B).</b> The data-object setter's assertion is UN-GATED - <c>Assert(Len(dataObject) &gt; 0,
    /// "Len(dataObject) &lt;= 0")</c> at [<c>:L101</c>], outside any conditional-compilation block, so
    /// it fires in a RELEASE build - while the SQL-syntax setter wraps the same shape of assertion in
    /// <c>#IF DEFINED DEBUG</c> [<c>:L113-L115</c>]. All locators were opened and confirmed, and the
    /// data-object one is the outlier.
    /// </summary>
    /// <remarks>
    /// Gating the first would REMOVE a release-build check the oracle performs; un-gating the second
    /// would ADD one it does not. Both are therefore asserted as they are, in one test, so a future
    /// reader meeting the inconsistency finds it recorded rather than apparently accidental.
    /// </remarks>
    [Fact]
    public void TheDataObjectAssertionIsUnGatedWhileTheSqlSyntaxAssertionIsDebugConditional()
    {
        using WriteBackProxyHarness harness = new();

        // THE UN-GATED ONE - it fires in EVERY configuration, including this file's Release build.
        AssertionFailure failure =
            Assert.Throws<AssertionFailure>(() => harness.Proxy.SetDataObject(string.Empty));

        // The legacy message text is preserved verbatim, because it is what a caller sees [:L101].
        Assert.Contains("Len(dataObject) <= 0", failure.Message, StringComparison.Ordinal);

        // Nothing reached the worker, so the refusal really did short-circuit the delegation.
        Assert.Equal(string.Empty, harness.Worker.DataObject);

        // THE DEBUG-CONDITIONAL ONE - present in a Debug build, absent in a Release build, exactly as
        // the oracle's conditional-compilation block dictates [:L113-L115].
#if DEBUG
        AssertionFailure gated =
            Assert.Throws<AssertionFailure>(() => harness.Proxy.SetSqlSyntax(string.Empty));

        Assert.Contains("Len(sqlSyntax) <= 0", gated.Message, StringComparison.Ordinal);
#else
        Assert.Equal(RetCode.OK, harness.Proxy.SetSqlSyntax(string.Empty));
        Assert.Equal(string.Empty, harness.Worker.SqlSyntax);
#endif
    }

    /// <summary>
    /// A NON-EMPTY data object passes the un-gated assertion and reaches the worker [<c>:L103</c>],
    /// which proves the assertion guards emptiness rather than the call itself.
    /// </summary>
    [Fact]
    public void ANamedDataObjectPassesTheAssertionAndReachesTheWorker()
    {
        using WriteBackProxyHarness harness = new();

        Assert.Equal(RetCode.OK, harness.Proxy.SetDataObject(DwSqliteFixture.DataObjectName));

        Assert.Equal(DwSqliteFixture.DataObjectName, harness.Worker.DataObject);
    }

    /// <summary>
    /// The auto-commit setter forwards its boolean verbatim [<c>:L108</c>], both ways.
    /// </summary>
    /// <param name="autoCommit">The value to forward.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheAutoCommitSetterForwardsItsBooleanToTheWorker(bool autoCommit)
    {
        using WriteBackProxyHarness harness = new();

        Assert.Equal(RetCode.OK, harness.Proxy.SetAutoCommit(autoCommit));

        Assert.Equal(autoCommit, harness.Worker.AutoCommit);
    }

    /// <summary>
    /// The update-data setter takes its payload BY REFERENCE [<c>:L61, :L236-L239</c>] - the shape
    /// hazard 1 of <c>docs/PB多线程绕坑提示.md</c> [<c>:L1-L4</c>] requires, preserved even though a
    /// managed reference would not fault - and forwards both the payload and its row count.
    /// </summary>
    [Fact]
    public void TheUpdateDataSetterForwardsTheReferencePayloadAndItsRowCount()
    {
        using WriteBackProxyHarness harness = new();

        CarrierState? payload = new();

        Assert.Equal(RetCode.OK, harness.Proxy.SetUpdateData(ref payload, 12L));

        Assert.Equal(12L, harness.Worker.GetUpdateRows());

        // The local is untouched by the call, because the oracle only READS through its reference.
        Assert.NotNull(payload);
    }

    /// <summary>
    /// The update-object setter has TWO arities and the one-argument form defaults the apply flag to
    /// TRUE [<c>:L277</c>], which is observable as a changeset capture the two-argument form can
    /// suppress.
    /// </summary>
    [Fact]
    public void TheUpdateObjectSetterHasTwoAritiesAndTheShortOneAppliesData()
    {
        using WriteBackProxyHarness harness = new();

        WriteBackTargetSpy applying = new(IdentityWriteBackTargetKind.DataStore)
        {
            UpdateTableAnswer = DwSqliteFixture.UpdateTableName,
            SyntaxAnswer = "release 12.5;",
            ChangeRowCount = 6L,
        };

        // ONE ARGUMENT - applyData defaults to true, so the capture happens.
        Assert.Equal(RetCode.OK, harness.Proxy.SetUpdateObject(applying));
        Assert.Equal(1, applying.GetChangesCalls);
        Assert.Equal(6L, harness.Worker.GetUpdateRows());

        WriteBackTargetSpy retainingOnly = new(IdentityWriteBackTargetKind.DataStore)
        {
            UpdateTableAnswer = DwSqliteFixture.UpdateTableName,
            SyntaxAnswer = "release 12.5;",
            ChangeRowCount = 9L,
        };

        // TWO ARGUMENTS with the flag CLEAR - retained, nothing captured, and the worker keeps the
        // previous payload count because nothing was forwarded.
        Assert.Equal(RetCode.OK, harness.Proxy.SetUpdateObject(retainingOnly, applyData: false));
        Assert.Equal(0, retainingOnly.GetChangesCalls);
        Assert.Equal(6L, harness.Worker.GetUpdateRows());
    }

    #endregion

    #region 10 - THE ADD-UPDATABLE-TABLE ARITIES - absent is not defaulted (C-06 proto3 presence)

    /// <summary>
    /// 🔴 <b>THE FOUR-ARGUMENT FORM LEAVES BOTH OPTIONAL SETTINGS ABSENT, NEVER DEFAULTED.</b> The
    /// oracle declares two locals and calls <c>SetNull</c> on each before delegating
    /// [<c>:L227-L234</c>], so the descriptor carries NO update-where mode and NO key-in-place setting
    /// at all.
    /// </summary>
    /// <remarks>
    /// This feeds C-06's proto3 presence fields - <c>optional int64 updatewhere = 5</c> and
    /// <c>optional bool updatekeyinplace = 6</c> in <c>persistence.v1.proto</c> - where ABSENT and
    /// DEFAULT are genuinely different on the wire. Collapsing absence into <c>0</c> would silently
    /// write concurrency mode zero, which is "key columns only" rather than the caller's intent, and
    /// collapsing it into <c>false</c> would claim an in-place key update the caller never asked for.
    /// </remarks>
    [Fact]
    public void TheFourArgumentArityLeavesUpdateWhereAndKeyInPlaceNullRatherThanDefaulted()
    {
        using WriteBackProxyHarness harness = new();

        Assert.Equal(
            RetCode.OK,
            harness.Proxy.AddUpdatableTable(
                DwSqliteFixture.UpdateTableName,
                DwSqliteFixture.ColumnNames,
                DwSqliteFixture.KeyColumnNames,
                DwSqliteFixture.IdentityColumnName));

        UpdatableTableDescriptor descriptor = Assert.Single(harness.Worker.Tables.Descriptors);

        // ★ NULL, NOT ZERO AND NOT FALSE.
        Assert.Null(descriptor.UpdateWhere);
        Assert.Null(descriptor.UpdateKeyInPlace);

        // Spelled out against the defaults they must NOT have collapsed into, so the assertion above
        // cannot be weakened by accident.
        Assert.False(descriptor.UpdateWhere.HasValue);
        Assert.False(descriptor.UpdateKeyInPlace.HasValue);
        Assert.NotEqual<long?>(default(long), descriptor.UpdateWhere);
        Assert.NotEqual<bool?>(default(bool), descriptor.UpdateKeyInPlace);

        // The four values that WERE supplied arrive intact.
        Assert.Equal(DwSqliteFixture.UpdateTableName, descriptor.Name);
        Assert.Equal(DwSqliteFixture.ColumnNames, descriptor.UpdatableColumns);
        Assert.Equal(DwSqliteFixture.KeyColumnNames, descriptor.KeyColumns);
        Assert.Equal(DwSqliteFixture.IdentityColumnName, descriptor.IdentityColumn);
    }

    /// <summary>
    /// The six-argument form sets BOTH settings [<c>:L223-L224</c>], including the fixture's own
    /// values - <c>updatewhere=1</c> and <c>updatekeyinplace=no</c> [<c>dw_sqlite.srd:L14</c>].
    /// </summary>
    /// <param name="updateWhere">The concurrency mode.</param>
    /// <param name="updateKeyInPlace">Whether a key change is applied in place.</param>
    [Theory]
    [MemberData(nameof(PresentSettingCombinations))]
    public void TheSixArgumentArityCarriesBothSettingsThrough(long updateWhere, bool updateKeyInPlace)
    {
        using WriteBackProxyHarness harness = new();

        Assert.Equal(
            RetCode.OK,
            harness.Proxy.AddUpdatableTable(
                DwSqliteFixture.UpdateTableName,
                DwSqliteFixture.ColumnNames,
                DwSqliteFixture.KeyColumnNames,
                DwSqliteFixture.IdentityColumnName,
                updateWhere,
                updateKeyInPlace));

        UpdatableTableDescriptor descriptor = Assert.Single(harness.Worker.Tables.Descriptors);

        Assert.Equal(updateWhere, descriptor.UpdateWhere);
        Assert.Equal(updateKeyInPlace, descriptor.UpdateKeyInPlace);
    }

    /// <summary>
    /// The setting combinations, including the fixture's own pair and the value zero - which is the
    /// case that proves ABSENT and PRESENT-ZERO are distinguishable.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>ONLY MODE ONE HAS EVIDENCED SEMANTICS, AND THIS FILE ASSERTS TRANSPORT RATHER THAN
    /// MEANING.</b> <c>Concurrency/UpdateWhereBuilder.cs</c> models mode 1 - "key and updateable
    /// columns" [<c>dw_sqlite.srd:L14</c>] - and deliberately models NO OTHER, because no other mode's
    /// behaviour is evidenced anywhere in the legacy tree and guessing one would invent a contract
    /// (C-B). The other values below are therefore carried as OPAQUE numbers whose only asserted
    /// property is that they arrive unchanged; nothing here claims what any of them compares.
    /// </remarks>
    public static TheoryData<long, bool> PresentSettingCombinations =>
        new()
        {
            // THE FIXTURE'S OWN: `updatewhere=1 updatekeyinplace=no` [dw_sqlite.srd:L14].
            { UpdateWhereBuilder.KeyAndUpdatableColumnsMode, false },

            // PRESENT ZERO - indistinguishable from absence unless presence is modelled, which is why
            // the two arities must not collapse into one.
            { 0L, false },
            { 0L, true },

            // An opaque higher value with key-in-place on, carried unchanged.
            { 2L, true },
        };

    /// <summary>
    /// A present zero is NOT the same as an absent setting, asserted by building both descriptors
    /// through the two arities and comparing them.
    /// </summary>
    [Fact]
    public void APresentZeroIsDistinguishableFromAnAbsentSetting()
    {
        using WriteBackProxyHarness harness = new();

        Assert.Equal(
            RetCode.OK,
            harness.Proxy.AddUpdatableTable(
                DwSqliteFixture.UpdateTableName,
                DwSqliteFixture.ColumnNames,
                DwSqliteFixture.KeyColumnNames,
                DwSqliteFixture.IdentityColumnName));

        Assert.Equal(
            RetCode.OK,
            harness.Proxy.AddUpdatableTable(
                DwSqliteFixture.UpdateTableName,
                DwSqliteFixture.ColumnNames,
                DwSqliteFixture.KeyColumnNames,
                DwSqliteFixture.IdentityColumnName,
                0L,
                false));

        IReadOnlyList<UpdatableTableDescriptor> descriptors = harness.Worker.Tables.Descriptors;

        Assert.Equal(2, descriptors.Count);
        Assert.NotEqual(descriptors[0].UpdateWhere, descriptors[1].UpdateWhere);
        Assert.NotEqual(descriptors[0].UpdateKeyInPlace, descriptors[1].UpdateKeyInPlace);

        // The two descriptors differ ONLY in the two optional members, which is the point.
        Assert.Equal(descriptors[0].Name, descriptors[1].Name);
        Assert.Equal(descriptors[0].UpdatableColumns, descriptors[1].UpdatableColumns);
        Assert.Equal(descriptors[0].KeyColumns, descriptors[1].KeyColumns);
        Assert.Equal(descriptors[0].IdentityColumn, descriptors[1].IdentityColumn);
    }

    /// <summary>
    /// The descriptor built here is the SAME six-field record <c>Concurrency/UpdateWhereBuilder.cs</c>
    /// declares, in the SAME ORDER as the oracle's structure - <c>name</c>,
    /// <c>updatablecolumns[]</c>, <c>keycolumns[]</c>, <c>identitycolumn</c>, <c>updatewhere</c>,
    /// <c>updatekeyinplace</c> [<c>n_cst_thread_task_sqlupdate.sru:L10-L17</c>] - and the last two are
    /// NULLABLE so absence is representable at all.
    /// </summary>
    [Fact]
    public void TheDescriptorIsTheSharedSixFieldRecordInTheOraclesFieldOrder()
    {
        ParameterInfo[] fields = typeof(UpdatableTableDescriptor)
            .GetConstructors()
            .Single(candidate => candidate.GetParameters().Length == 6)
            .GetParameters();

        Assert.Equal(
            [
                nameof(UpdatableTableDescriptor.Name),
                nameof(UpdatableTableDescriptor.UpdatableColumns),
                nameof(UpdatableTableDescriptor.KeyColumns),
                nameof(UpdatableTableDescriptor.IdentityColumn),
                nameof(UpdatableTableDescriptor.UpdateWhere),
                nameof(UpdatableTableDescriptor.UpdateKeyInPlace),
            ],
            fields.Select(field => field.Name));

        // THE TWO NULLABLE MEMBERS, which is what carries proto3 presence.
        Assert.Equal(typeof(long?), fields[4].ParameterType);
        Assert.Equal(typeof(bool?), fields[5].ParameterType);

        // Declared in Concurrency/, consumed here - not re-declared by the proxy.
        Assert.Equal(
            typeof(UpdateWhereBuilder).Namespace,
            typeof(UpdatableTableDescriptor).Namespace);
    }

    /// <summary>
    /// Every setter and both arities are refused while the task is busy [<c>:L99, :L106, :L111, :L207,
    /// :L223, :L236, :L246</c>], and the four-argument arity inherits its guard from the six-argument
    /// one it delegates to [<c>:L233</c>] rather than declaring its own.
    /// </summary>
    [Fact]
    public void EverySetterAndBothAritiesAreRefusedWhileBusy()
    {
        using WriteBackProxyHarness harness = new();

        harness.Host.MakeBusy();

        WriteBackTargetSpy source = StageSource(IdentityWriteBackTargetKind.DataStore);
        CarrierState? payload = new();

        Assert.Equal(RetCode.E_BUSY, harness.Proxy.SetDataObject(DwSqliteFixture.DataObjectName));
        Assert.Equal(RetCode.E_BUSY, harness.Proxy.SetAutoCommit(true));
        Assert.Equal(RetCode.E_BUSY, harness.Proxy.SetSqlSyntax("release 12.5;"));
        Assert.Equal(RetCode.E_BUSY, harness.Proxy.SetMultiTableUpdate(true));
        Assert.Equal(RetCode.E_BUSY, harness.Proxy.SetUpdateData(ref payload, 1L));
        Assert.Equal(RetCode.E_BUSY, harness.Proxy.SetUpdateObject(source));
        Assert.Equal(RetCode.E_BUSY, harness.Proxy.SetUpdateObject(source, applyData: false));

        Assert.Equal(
            RetCode.E_BUSY,
            harness.Proxy.AddUpdatableTable(
                DwSqliteFixture.UpdateTableName,
                DwSqliteFixture.ColumnNames,
                DwSqliteFixture.KeyColumnNames,
                DwSqliteFixture.IdentityColumnName));

        Assert.Equal(
            RetCode.E_BUSY,
            harness.Proxy.AddUpdatableTable(
                DwSqliteFixture.UpdateTableName,
                DwSqliteFixture.ColumnNames,
                DwSqliteFixture.KeyColumnNames,
                DwSqliteFixture.IdentityColumnName,
                UpdateWhereBuilder.KeyAndUpdatableColumnsMode,
                false));

        // Nothing was admitted and nothing was retained.
        Assert.Empty(harness.Worker.Tables.Descriptors);
        Assert.Empty(source.Calls);
    }

    #endregion

    #region The staging helpers - one source shape and one carrier shape, shared by every case

    /// <summary>
    /// Maps this file's public arm vocabulary onto the subject's internal one.
    /// </summary>
    /// <param name="arm">The arm named by theory data.</param>
    /// <returns>The subject's own arm value.</returns>
    /// <remarks>
    /// The default arm throws rather than folding an unknown member onto a plausible one: a silent fold
    /// would let a newly added arm be tested as if it were an existing one.
    /// </remarks>
    private static IdentityWriteBackTargetKind KindOf(TargetArm arm) => arm switch
    {
        TargetArm.DataWindow => IdentityWriteBackTargetKind.DataWindow,
        TargetArm.DataStore => IdentityWriteBackTargetKind.DataStore,
        TargetArm.Other => IdentityWriteBackTargetKind.Other,
        _ => throw new ArgumentOutOfRangeException(
            nameof(arm),
            arm,
            "The oracle's write-back dispatches on exactly three arms - DataWindow!, DataStore! and "
                + "the case else [n_cst_threading_task_sqlupdate.sru:L131-L201] - so a fourth member "
                + "means this vocabulary and the subject's have diverged."),
    };

    /// <summary>
    /// Builds the SOURCE object - the caller-owned DataWindow or DataStore the write-back applies onto -
    /// with the rows of <see cref="PrimaryRowsInSourceOrder"/> and
    /// <see cref="FilterRowsInSourceOrder"/> in SOURCE ORDER.
    /// </summary>
    /// <param name="kind">Which <c>TypeOf()</c> arm the target takes [<c>:L131</c>].</param>
    /// <returns>The staged source.</returns>
    /// <remarks>
    /// Every filter row is newly modified, because a filtered row that the update inserted is exactly
    /// the population the oracle collects identity values for; the primary rows deliberately MIX the
    /// three statuses so the walk's visit-and-skip behaviour is exercised by every case that uses this
    /// shape.
    /// </remarks>
    private static WriteBackTargetSpy StageSource(IdentityWriteBackTargetKind kind)
    {
        WriteBackTargetSpy source = new(kind);

        foreach ((string token, ItemStatus status, long? _) in PrimaryRowsInSourceOrder)
        {
            _ = source.SeedRow(DwBuffer.Primary, status, token, TokenColumn);
        }

        foreach ((string token, long _) in FilterRowsInSourceOrder)
        {
            _ = source.SeedRow(DwBuffer.Filter, ItemStatus.NewModified, token, TokenColumn);
        }

        return source;
    }

    /// <summary>
    /// Builds the WORKER-SIDE CARRIER the collector reads from - the object
    /// <c>n_cst_thread_task_sqlbase_ds</c> stands for - with its <c>Primary!</c> buffer in source order
    /// and its <c>Filter!</c> buffer INVERTED.
    /// </summary>
    /// <returns>The staged carrier, holding the identity values the database assigned.</returns>
    /// <remarks>
    /// 🔴 <b>THE INVERSION IS THE WHOLE POINT, AND IT IS THE ORACLE'S OWN STATEMENT OF FACT.</b> The
    /// carrier's <c>Filter!</c> buffer holds its rows in the inverse of the order they occupy in the
    /// data source that called <c>GetChanges</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru:L235</c>]. Reproducing that
    /// inversion HERE, in the double, is what makes the composed round trip a real test of the pairing
    /// rather than a restatement of one half of it.
    /// </remarks>
    private static FakeDataWindowCarrier StageCarrier()
    {
        FakeDataWindowCarrier carrier = new();

        // Primary: the SAME order as the source, because that buffer is not inverted.
        foreach ((string token, ItemStatus status, long? identity) in PrimaryRowsInSourceOrder)
        {
            long row = carrier.AppendRow(DwBuffer.Primary, status);
            _ = carrier.SetItemValue(row, TokenColumn, DwBuffer.Primary, token);

            if (identity.HasValue)
            {
                _ = carrier.SetItemValue(row, IdentityColumn, DwBuffer.Primary, identity.Value);
            }
        }

        // Filter: REVERSED, which is the carrier's documented order relative to the source.
        foreach ((string token, long identity) in FilterRowsInSourceOrder.Reverse())
        {
            long row = carrier.AppendRow(DwBuffer.Filter, ItemStatus.NewModified);
            _ = carrier.SetItemValue(row, TokenColumn, DwBuffer.Filter, token);
            _ = carrier.SetItemValue(row, IdentityColumn, DwBuffer.Filter, identity);
        }

        // The inserted count gates the whole identity path on the worker side
        // [n_cst_thread_task_sqlupdate.sru:L215], so it is set to the population that is actually new.
        carrier.RowsInserted =
            PrimaryRowsInSourceOrder.Count(row => row.Status == ItemStatus.NewModified)
            + FilterRowsInSourceOrder.Count;

        return carrier;
    }

    /// <summary>
    /// Runs the REAL collector over a staged carrier - Primary FORWARD
    /// [<c>n_cst_thread_task_sqlupdate.sru:L229</c>], Filter BACKWARD [<c>:L237</c>].
    /// </summary>
    /// <param name="carrier">The carrier to collect from.</param>
    /// <returns>The collected block, which is never empty for a staged carrier.</returns>
    private static ResolvedIdentityColumnData CollectFrom(FakeDataWindowCarrier carrier)
    {
        ResolvedIdentityColumnData? collected =
            IdentityColumnResolver.CollectIdentityData(carrier, IdentityColumn);

        // The emit guard fires when EITHER array is non-empty [:L242], and a staged carrier always has
        // both, so a null here means the staging itself broke rather than the subject.
        Assert.NotNull(collected);

        return collected;
    }

    /// <summary>
    /// A minimal source: ONE newly-modified primary row and no filtered rows.
    /// </summary>
    /// <returns>The staged source, on the DataStore arm so nothing brackets its body.</returns>
    private static WriteBackTargetSpy SingleNewRowSource()
    {
        WriteBackTargetSpy source = new(IdentityWriteBackTargetKind.DataStore);
        _ = source.SeedRow(DwBuffer.Primary, ItemStatus.NewModified, "only-row", TokenColumn);

        return source;
    }

    #endregion

    #region The harness and its doubles - no thread, no clock, no connection, no schema

    /// <summary>
    /// The proxy under test with its worker attached, wired entirely from doubles.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>NO DATABASE, NO CONNECTION, NO SCHEMA (C-E).</b> The transaction pool is real because the
    /// worker's constructor requires one, but its activator throws if reached; the data-store factory and
    /// the carrier adapter throw too, because no case here runs the worker's task body. The one thing
    /// the worker IS used for is holding the values the delegating setters forward, which is exactly what
    /// those tests assert.
    /// </para>
    /// <para>
    /// <b>NO REAL CLOCK (AAP 0.6.7).</b> Every collaborator that takes a <see cref="TimeProvider"/> gets
    /// the injected fake seam, so nothing in this file can read ambient time even indirectly.
    /// </para>
    /// </remarks>
    private sealed class WriteBackProxyHarness : IDisposable
    {
        internal WriteBackProxyHarness()
        {
            Clock = new FakeTimeProvider();

            IOptions<PersistenceOptions> options = Options.Create(new PersistenceOptions());

            Pool = new TransactionPool(options, Clock, new UnreachableTransactionActivator());

            Worker = new SqlUpdateTask(
                new UnreachableTaskHost(),
                Pool,
                new UnreachableDataStoreFactory(),
                new SqlRetrievalHookActivator(),
                new UnreachableCarrierAdapter(),
                new ConflictDetector(SqlRedactor.Instance),
                Clock,
                NullLogger<SqlUpdateTask>.Instance);

            Host = new ScriptedProxyHost { Task = Worker };

            Proxy = new SqlUpdateTaskProxy(
                Host,
                NullLogger<SqlUpdateTaskProxy>.Instance,
                Clock);

            // Composition, not construction: until the init event has run the proxy holds no worker
            // reference, so every member that needs one would throw.
            Assert.Equal(RetCode.OK, Proxy.Initialize());
        }

        internal FakeTimeProvider Clock { get; }

        internal ScriptedProxyHost Host { get; }

        internal SqlUpdateTaskProxy Proxy { get; }

        internal SqlUpdateTask Worker { get; }

        internal TransactionPool Pool { get; }

        /// <summary>
        /// Publishes a database error through the channel the worker uses - the interface member, so the
        /// proxy's override is reached exactly as it is in production.
        /// </summary>
        /// <param name="error">The payload the worker reported.</param>
        internal void PublishDbError(DbErrorData error) =>
            ((ISqlTaskProxy)Proxy).OnDbError(in error);

        public void Dispose()
        {
            Proxy.Dispose();
            Worker.Dispose();
            Pool.Dispose();
        }
    }

    /// <summary>
    /// The task-substrate surface the proxy's base drives, with the two things the cases here need to
    /// script: the busy state and the last exit code.
    /// </summary>
    private sealed class ScriptedProxyHost : ISqlTaskProxyHost
    {
        public SqlTaskBase? Task { get; init; }

        /// <summary>
        /// The exit code <c>of_GetLastExitCode()</c> answers, which is the finalize hook's gate
        /// [<c>:L303</c>]. Defaults to the FAILURE code so a case must opt IN to the write-back.
        /// </summary>
        internal long LastExitCodeValue { get; set; } = RetCode.FAILED;

        internal string RequestedWorkerClassName { get; private set; } = string.Empty;

        public bool IsRunning { get; private set; }

        public bool IsControllerBusy { get; private set; }

        public bool IsSyncSignalSet { get; private set; }

        public bool IsCancelled => false;

        public CancellationToken Cancellation => CancellationToken.None;

        public long LastExitCode => LastExitCodeValue;

        public long LastErrorCode => RetCode.OK;

        public string LastErrorInfo => string.Empty;

        public ulong TaskId => 1UL;

        public int TaskIndex => 1;

        public string TaskClassName => RequestedWorkerClassName;

        /// <summary>
        /// Makes the task report busy, which is what every guarded member tests first.
        /// </summary>
        internal void MakeBusy()
        {
            IsRunning = false;
            IsControllerBusy = true;
        }

        public void RaiseSyncSignal() => IsSyncSignalSet = true;

        public void ClearSyncSignal() => IsSyncSignalSet = false;

        public long OnInit(string workerClassName)
        {
            RequestedWorkerClassName = workerClassName;

            return RetCode.OK;
        }

        public long OnPrepare() => RetCode.OK;

        public long Cancel() => RetCode.OK;

        public long SetWorkerDelayFor(double seconds) => RetCode.OK;

        public long SetWorkerSkip(bool skip) => RetCode.OK;
    }

    /// <summary>
    /// The worker's own host. Inert by construction: no case in this file runs the worker's task body,
    /// so every member answers the quietest legal thing rather than throwing.
    /// </summary>
    private sealed class UnreachableTaskHost : ISqlTaskHost
    {
        public bool IsMainThread => true;

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

        public long OnError(long errCode, string errInfo) => RetCode.OK;

        public long OnNotify(long notifyCode, long payload, string text) => RetCode.OK;
    }

    /// <summary>
    /// A data-store factory that must never be reached, and says so (C-E).
    /// </summary>
    private sealed class UnreachableDataStoreFactory : ISqlDataStoreFactory
    {
        public ISqlDataStore Create(CarrierThreadAffinity affinity) =>
            throw new NotSupportedException(
                "No case in IdentityWriteBackTests runs the worker's task body, so no data store may "
                    + "be created. Reaching this means a case started an update instead of driving the "
                    + "caller-side proxy directly.");
    }

    /// <summary>
    /// A carrier adapter that must never be reached, and says so (C-E).
    /// </summary>
    private sealed class UnreachableCarrierAdapter : ISqlUpdateCarrierAdapter
    {
        public ISqlUpdateCarrier Adapt(ISqlDataStore store) =>
            throw new NotSupportedException(
                "No case in IdentityWriteBackTests runs the worker's task body, so no update carrier "
                    + "may be adapted.");
    }

    /// <summary>
    /// A pooled-transaction activator that must never be reached, and says so (C-E): nothing here opens
    /// a transaction, names a database or provisions a schema.
    /// </summary>
    private sealed class UnreachableTransactionActivator : IPooledTransactionActivator
    {
        public IPooledTransaction CreateDefault() =>
            throw new NotSupportedException(
                "No case in IdentityWriteBackTests opens a transaction, so no pooled transaction may "
                    + "be activated.");

        public IPooledTransaction Create(string className) =>
            throw new NotSupportedException(
                "No case in IdentityWriteBackTests opens a transaction, so no pooled transaction may "
                    + "be activated.");
    }

    #endregion
}
