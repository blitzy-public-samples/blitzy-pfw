// ==================================================================================================
//  ConflictDetector.cs - THE UPDATE OUTCOME CLASSIFIER, AND THE ORIGIN OF THE CONFLICT RESPONSE
//  ------------------------------------------------------------------------------------------------
//  ROLE
//  This file is the port of `_of_update`, the private function that decides what one update attempt
//  actually did [ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru:L172-L252]. It is
//  the most arm-dense file in this folder: twelve ordered steps, three sentinels, a two-way veto
//  discrimination, two cancellation checks, one defensive override that inverts a claimed success,
//  and - as the one NET-NEW addition - the optimistic-concurrency narrowing that produces the
//  system's `Aborted` / HTTP 409 response.
//
//  DIVISION OF LABOUR - THE DETECTOR CLASSIFIES, THE MAPPER MAPS, THE SERVICE THROWS (C-K)
//  Three files share the conflict path and each owns exactly one third of it:
//
//      Concurrency/ConflictDetector.cs   THIS FILE. Classifies the attempt into a typed
//                                        UpdateOutcome, populates common.v1.ConflictDetail, and
//                                        exposes ONE sanctioned projection onto the gRPC status.
//                                        IT THROWS NO RpcException, EVER.
//      Program.cs                        Owns the CENTRAL concurrency-mismatch-to-Aborted mapping
//                                        for the host.
//      Grpc/UpdateService.cs             The throw site. Calls the projection below and raises the
//                                        RpcException.
//
//  WHY THE DETECTOR PRODUCES A DETAIL INSTEAD OF THROWING (C-K). A classifier that threw would
//  decide the transport on the caller's behalf, and there would then be TWO spellings of the same
//  status - one here and one in the central mapper - free to drift. Worse, a thrown exception cannot
//  be composed: `UpdatePreparer.PrepareAndUpdate` drives this classifier through a `Func<long>` and
//  inspects the returned code to decide whether to continue to the next table
//  [n_cst_thread_task_sqlupdate.sru:L364-L369], which an in-flight exception would bypass entirely.
//  So the outcome is DATA, and `TryProjectAborted` below is the single place the data becomes a
//  status. Both the throw site and the central mapper call it, so they cannot disagree.
//
//  ============================ FOUR THINGS A STRAIGHTFORWARD PORT GETS WRONG ====================
//
//  1. SUCCESS IS THE LITERAL VALUE 1, NOT THE ZERO OF THE RETURN-CODE ALGEBRA (C-K).
//     The gate on the entire success path is `if rtCode = 1 then` [:L214], and everything else falls
//     to the else arm at [:L249-L250] and returns `E_DB_ERROR`. The value comes from the DataWindow
//     UPDATE CONTRACT - `Data.Update(...)` answers 1 for success and -1 for failure - which is a
//     completely separate code space from the framework's return-code algebra where
//     `RetCode.OK` is 0 and a negative value is a failure. Two mistakes follow from conflating them
//     and both invert the outcome:
//         * mapping the raw 1 onto the algebra would read as `RetCode.PREVENT`;
//         * testing `Predicates.IsSucceeded` against the raw result would classify -1 as a failure by
//           luck and 0 - which the DataWindow never returns as success - as a SUCCESS.
//     This file therefore keeps the two spaces in separate members: `UpdateOutcome.UpdateResult` is
//     the DataWindow value and `UpdateOutcome.Code` is the return code, and nothing converts between
//     them except the explicit arms below.
//
//  2. A CLAIMED SUCCESS IS DEFENSIVELY REWRITTEN INTO A FAILURE.
//         if TransObject.SQLCode = -1 and rtCode = 1 then rtCode = -1        [:L208-L210]
//     AN IMPLEMENTATION THAT TRUSTS THE UPDATE CALL'S OWN RETURN VALUE WILL REPORT SUCCESS ON A
//     FAILED UPDATE. The transaction's driver code overrides the operation's self-report. Both
//     conjuncts are load-bearing: the override fires ONLY on a claimed success, so a genuine -1 is
//     left alone rather than being rewritten twice.
//
//  3. THE AFTER-UPDATE HOOK FIRES BEFORE THE OVERRIDE, SO IT OBSERVES THE UNRECONCILED VALUE.
//     `TransObject.Event OnAfterUpdate(Data,rtCode)` sits at [:L206] and the rewrite at [:L208].
//     A hook that saw the reconciled value would be a behavioural change, so the order is preserved
//     and `UpdateOutcome.UpdateResultObservedByHook` records what the hook was handed.
//
//  4. CANCELLATION IS CHECKED TWICE AND BOTH CHECKS MATTER.
//     Before the update [:L182] and again after it [:L212]. A cancellation observed only AFTER a
//     successful update still yields `RetCode.CANCELLED`, and the success path is not taken - so the
//     identity round trip and the counts report do not fire. Collapsing the two into one check in
//     either position loses a distinct legacy outcome.
//
//  ================= THREE PREDICATES OVER ONE FIELD, PLUS TWO DIFFERING VETO ARMS ================
//  DO NOT UNIFY THESE INTO A SHARED HELPER. THE CONDITIONS GENUINELY DIFFER (C-B, C-K). The same
//  `SQLCode` field is tested three different ways in the oracle, at five locators:
//
//      SITE                                LOCATOR                              CONDITION
//      this file's defensive override       n_cst_thread_task_sqlupdate.sru:L208 EXACTLY -1
//                                                                               (and rtCode = 1)
//      the transaction object's override    n_cst_thread_trans.sru:L275          NON-ZERO
//                                                                               (and rtCode = 1)
//      the transaction's failure predicate  n_cst_thread_trans.sru:L337          NEGATIVE  (< 0)
//      the transaction's success predicate  n_cst_thread_trans.sru:L340          (>= 0)
//
//      this file's veto arm                 n_cst_thread_task_sqlupdate.sru:L196 the FAILURE
//                                                                               PREDICATE, i.e. < 0
//      the transaction object's veto arm    n_cst_thread_trans.sru:L267          NON-ZERO, tested
//                                                                               DIRECTLY
//
//  So a POSITIVE non-zero code - a driver warning - rewrites a claimed success at
//  `n_cst_thread_trans.sru:L275` but does NOT rewrite one here, and it makes the transaction-object
//  veto arm report a database error while leaving THIS file's veto arm reporting a clean
//  cancellation. Three conditions and two veto arms, not one condition with three spellings.
//  `n_cst_thread_trans.sru` is a SIBLING code path that this file does not implement; its two
//  differing conditions are recorded here only so that a future reader who finds them does not
//  "harmonize" the two files into agreement. The predicates this file consumes are named on
//  `IUpdateTransaction` with the locator for each.
//
//  ==================== THE CONFLICT RESPONSE IS AN ADDITION, NOT A PORT (C-K) ====================
//  THE LEGACY HAS NO CONFLICT DETECTION AT ALL. An exhaustive search of the update path finds no
//  rows-affected check of any kind: a concurrency mismatch is INDISTINGUISHABLE from any other update
//  failure, because the update returns something other than 1, the code becomes `E_DB_ERROR`
//  [:L250], and the caller rolls back [:L395]. The `Aborted` plus `ConflictDetail` discrimination is
//  therefore an addition the migration plan mandates, layered ON TOP of the preserved legacy arms
//  rather than replacing any of them. Three consequences, all enforced below:
//
//      * NO LEGACY ARM CHANGES. Every step keeps its exact behaviour and its exact code. A conflict
//        outcome still carries `RetCode.E_DB_ERROR`, because that is what the oracle returns; the
//        conflict is a NARROWING of the outcome KIND, not a new return code.
//      * NO GENUINE DATABASE ERROR IS SWALLOWED. A conflict is classified only on positive evidence
//        of a concurrency mismatch. A constraint violation, a connection fault and a syntax error
//        each stay a database error carrying the transaction's own code and text.
//      * THERE IS NO SILENT OVERWRITE ANYWHERE IN THIS SYSTEM. A mismatch is always reported, never
//        resolved by guessing, and `ConflictDetail` carries the CURRENT row state so the caller can
//        implement an explicit retry-or-surface policy. No last-writer-wins path exists here and no
//        member below would enable one.
//
//  WHAT `updatewhere=1` MEANS FOR THE PAYLOAD. The sole updatable DataWindow in the repository
//  declares `update="COMPANY" updatewhere=1 updatekeyinplace=no`
//  [ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L14] and marks all six of its columns
//  `update=yes updatewhereclause=yes` [:L8-L13]. Mode 1 is "key and updateable columns", so the
//  generated where clause carries the key column PLUS THE ORIGINAL VALUE OF EVERY UPDATEABLE COLUMN,
//  and the optimistic check therefore spans all six original values. That is why every conflict row
//  below carries BOTH the current and the original value of each marked column: with current values
//  alone a caller could not tell WHICH column moved underneath it. The per-row current-and-original
//  payload is read from the `Buffers/` anti-corruption layer and is not re-derived here.
//
//  THE FIXTURE IS TEST DATA, NOT PRODUCTION DATA (C-E). No table name, no column name and no column
//  count appears in this file. The marked columns arrive as an argument.
//
//  ========================== REDACTION IS MANDATORY AND SINGLE-PATH (C-F) ========================
//  The legacy `sqlsyntax` field carries the COMPLETE GENERATED STATEMENT INCLUDING INTERPOLATED
//  LITERAL VALUES [dberrordata.srs:L6], and the legacy logger performs no redaction at all. So:
//      * every statement that reaches a `DbErrorData` built here passes through the injected
//        `ISqlRedactor` first. Both legacy arms that raise from this function pass an EMPTY statement
//        [:L190, :L197], which makes the call a no-op - and it is made anyway, so that there is
//        exactly ONE path and a future arm that does carry a statement cannot bypass it;
//      * the projection onto the published `common.v1.DbError` goes ONLY through
//        `DbErrorDataExtensions.ToDbError`, never hand-rolled;
//      * `common.v1.ConflictDetail` has no statement field at all, so no statement can leak through
//        the conflict payload by construction;
//      * no connection string, password or `logpass` value appears anywhere in this file, and none
//        may reach a log. `logpass` is WRITE-ONLY system-wide.
//
//  A DIVERGENCE FROM THIS FILE'S OWN BRIEF, RECORDED BECAUSE THE SOURCE WINS. The brief specifies
//  `ToDbError(this DbErrorData, ISqlRedactor)`. The sibling actually declares
//  `internal static DbError ToDbError(this in DbErrorData error)` WITH NO REDACTOR PARAMETER, and
//  says why: an earlier shape did take one, and it was removed because ANY implementation satisfied
//  it, so the mandatory parameter was not a real control. The projection now owns the policy and
//  applies `SqlRedactor.Instance` unconditionally [Errors/SqlRedactor.cs, the ToDbError remarks].
//  This file therefore calls the real one-argument extension - which is strictly stronger than the
//  brief's shape - and still takes an `ISqlRedactor` through its constructor, because that is the
//  surface the statement passes through on its way INTO a `DbErrorData` and on its way to a log.
//
//  ================================ ORACLE STATUS AND SCOPE (C-C) =================================
//  Every `ws_objects/**` path cited in this file is READ ONLY. Each was read as specification and is
//  cited by locator; nothing here copies, reformats, moves, edits or deletes any of them, and nothing
//  in this file depends on the PowerBuilder toolchain, the PowerBuilder runtime or any shipped native
//  binary. The legacy tree is the only statement of intended behaviour that exists for this function,
//  which is why every behavioural claim below carries the `:L` line it was taken from.
//
//  WHAT THIS FILE DELIBERATELY DOES NOT DO
//    * No dialog, no message-box analogue and no user interface of any kind (C-D). The legacy's two
//      error raises become calls on an INJECTED SINK: only the delivery channel changes, and the
//      code, the text, the buffer and the row are preserved exactly.
//    * No authentication and no authorization logic (C-G). `Program.cs` owns the bearer wiring.
//    * No database, no connection, no `DbCommand` and no SQL text generation. The SQL code, the update
//      result, the veto outcome, the described update table and the affected-row evidence all arrive
//      as INPUTS, which is what makes every arm below reachable with no database and no live
//      concurrent writer (C-H).
//    * No accumulation of state across calls. The classifier runs ONCE PER UPDATE TABLE
//      [:L364-L369] and holds nothing between invocations - see `Classify`.
//    * NO INDEX ARITHMETIC AT ALL, WHICH IS HOW R9 IS DISCHARGED HERE. Nothing in this file converts
//      between the legacy's one-based row and column ordinals and a CLR index: ordinals are read,
//      compared against the centralized one-based constants on `Buffers/ItemStatusMachine`, and
//      passed through untouched. The row 0 in the sentinel payload is a deliberate literal meaning
//      "no particular row" [:L190] rather than an index, and the one place a bound is checked says
//      so. A silent off-by-one here would be indistinguishable from a behavioural regression, so the
//      safest amount of arithmetic is none.
//    * NO SCREAMING_SNAKE IDENTIFIER IS DECLARED. This folder sits outside the repository
//      `.editorconfig`'s scoped naming suppressions and `Directory.Build.props` sets
//      `TreatWarningsAsErrors`, so the preserved legacy constant spellings are CONSUMED from
//      `PowerFramework.Shared.Kernel.RetCode` and from the generated contract enums rather than
//      redeclared here.
// ==================================================================================================

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Grpc.Core;
using PowerFramework.Contracts.Common.V1;
using PowerFramework.Contracts.Persistence.V1;
using PowerFramework.Persistence.Buffers;
using PowerFramework.Persistence.Errors;

// THE RetCode NAME COLLIDES ACROSS THE TWO CODE SPACES THIS FILE STRADDLES, AND BOTH ARE NEEDED.
//   * `RetCode` is PowerFramework.Shared.Kernel.RetCode, a static class of `long` constants. It is the
//     IN-PROCESS algebra, and it is what `UpdateOutcome.Code` holds and what every arm below returns.
//   * `WireRetCode` is PowerFramework.Contracts.Common.V1.RetCode.Types.Value, the generated PROTO
//     ENUM. It is the WIRE form, and it appears only where a value is placed on a contract message.
// The two agree value for value by construction - the protocol definition lists every member with the
// retcode.sru line it came from - which is what makes the single narrowing conversion below safe.
using Predicates = PowerFramework.Shared.Kernel.Predicates;
using RetCode = PowerFramework.Shared.Kernel.RetCode;
using WireRetCode = PowerFramework.Contracts.Common.V1.RetCode.Types.Value;

namespace PowerFramework.Persistence.Concurrency;

#region The outcome vocabulary - five kinds, one of which the oracle could not express

/// <summary>
/// What one update attempt turned out to be. Four kinds reproduce a distinct legacy arm; the fifth
/// is the mandated narrowing of the fourth.
/// </summary>
/// <remarks>
/// <para>
/// THE KINDS ARE NOT A SEVERITY LADDER AND MUST NOT BE COMPARED WITH <c>&lt;</c> OR <c>&gt;</c>. They
/// name the arm the oracle took, and the numeric ordering below carries no meaning beyond giving
/// <see cref="None"/> the zero slot.
/// </para>
/// <para>
/// The mapping onto the return code the oracle returns is one-way and is not derivable in reverse:
/// <see cref="DatabaseError"/> and <see cref="Conflict"/> BOTH answer <c>RetCode.E_DB_ERROR</c>,
/// because the conflict narrowing deliberately does not invent a new code
/// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru:L250</c>]. That is why the
/// kind is carried alongside the code rather than being reconstructed from it.
/// </para>
/// </remarks>
internal enum UpdateOutcomeKind
{
    /// <summary>
    /// No outcome. NEVER produced by <see cref="ConflictDetector.Classify"/>.
    /// </summary>
    /// <remarks>
    /// PRESENT SO THAT A DEFAULT-INITIALISED VALUE CANNOT MASQUERADE AS A REAL OUTCOME, which matters
    /// here more than usual: the legacy's success code is <c>RetCode.OK</c>, which is zero, so a zero
    /// slot named <see cref="Succeeded"/> would make <c>default</c> read as a successful update. That
    /// is the same class of mistake as collapsing a null return code to zero, which turns "neither
    /// succeeded nor failed" into "succeeded". <see cref="UpdateOutcome"/> cannot be constructed
    /// without a kind, so this value is unreachable through the sanctioned factories.
    /// </remarks>
    None = 0,

    /// <summary>
    /// The update reported the DataWindow contract's success value and neither cancellation check
    /// fired: <c>if rtCode = 1 then ... return RetCode.OK</c> [<c>:L214</c>, <c>:L248</c>].
    /// </summary>
    Succeeded = 1,

    /// <summary>
    /// <c>RetCode.CANCELLED</c>, from any of THREE distinct arms: the cancellation check before the
    /// update [<c>:L182</c>], a CLEAN veto [<c>:L201</c>], or the cancellation check after the update
    /// [<c>:L212</c>].
    /// </summary>
    /// <remarks>
    /// Recall that <c>CANCELLED</c> is NEITHER succeeded nor failed in the legacy algebra -
    /// <c>IsSucceeded</c> tests <c>&gt;= 0</c> and <c>IsFailed</c> excludes the cancelled value
    /// explicitly - so this outcome cannot be folded into either of its neighbours. The caller's
    /// epilogue relies on the distinction: it rolls back but SUPPRESSES the error raise for exactly
    /// this code [<c>:L394-L400</c>].
    /// </remarks>
    Cancelled = 2,

    /// <summary>
    /// The transaction could not be acquired: <c>RetCode.E_INVALID_TRANSACTION</c> [<c>:L180</c>].
    /// </summary>
    /// <remarks>
    /// This arm runs BEFORE the cancellation check, so an attempt that is both cancelled and
    /// transaction-less reports the transaction fault rather than the cancellation. Reordering the two
    /// would change the reported code.
    /// </remarks>
    InvalidTransaction = 3,

    /// <summary>
    /// <c>RetCode.E_DB_ERROR</c>: the sentinel update table [<c>:L192</c>], a veto combined with a
    /// transaction failure [<c>:L199</c>], or any update result other than the success value
    /// [<c>:L250</c>].
    /// </summary>
    /// <remarks>
    /// THE ORACLE LUMPS ALL THREE TOGETHER, and so does this member. Only the third of them is
    /// eligible for the <see cref="Conflict"/> narrowing, because only there has a statement actually
    /// been generated and executed.
    /// </remarks>
    DatabaseError = 4,

    /// <summary>
    /// An optimistic-concurrency mismatch. THE ONE KIND WITH NO LEGACY COUNTERPART.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The oracle performs no rows-affected check anywhere in the update path, so it cannot tell a
    /// mismatch from any other update failure. This kind is the mandated narrowing of
    /// <see cref="DatabaseError"/>, and it carries the SAME return code -
    /// <c>RetCode.E_DB_ERROR</c> - precisely so that no existing arm changes.
    /// </para>
    /// <para>
    /// It is the only kind that projects onto a gRPC status other than the operation's own: see
    /// <see cref="ConflictDetector.TryProjectAborted"/>.
    /// </para>
    /// </remarks>
    Conflict = 5,

    /// <summary>
    /// The caller's payload flagged a row modified but supplied no updatable column value for it, so no
    /// assignment could be generated. <c>RetCode.E_INVALID_DATA</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE SECOND KIND WITH NO LEGACY COUNTERPART, AND FOR A CONDITION THE LEGACY CANNOT REACH. In
    /// process the runtime maintains a row's status and its columns' statuses together, so a row cannot
    /// claim to be modified while every column of it claims not to be. Across this boundary a producer
    /// composes both levels itself and may state a contradiction, which is a payload fault rather than a
    /// database one.
    /// </para>
    /// <para>
    /// IT CARRIES THE ORACLE'S OWN INVALID-UPDATE-DATA CODE - <c>RetCode.E_INVALID_DATA</c>, the code the
    /// task returns for a changeset it cannot apply [<c>n_cst_thread_task_sqlupdate.sru:L343-L344</c>] -
    /// rather than a new member, and Gateway already publishes that code as <c>400</c>. Reporting it as
    /// the <see cref="Conflict"/> narrowing instead named the wrong party and invited a retry that could
    /// never converge.
    /// </para>
    /// </remarks>
    InvalidUpdateData = 6,

    /// <summary>
    /// The storage engine refused a row because it violated a schema constraint the CALLER's payload
    /// controls - a required column left null, a duplicate key. <c>RetCode.E_INVALID_DATA</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 SPLIT OUT OF <see cref="DatabaseError"/>, WHICH IS WHERE IT USED TO LAND, AND THE MOVE IS THE
    /// WHOLE POINT. A row omitting a <c>NOT NULL</c> column reached the caller as
    /// <c>RetCode.E_DB_ERROR</c>, which Gateway correctly publishes as <b>HTTP 502</b> - so a caller who
    /// forgot a required field was told the DATABASE had failed and that the fault lay behind the
    /// gateway. It is the caller's payload, it is not retryable unchanged, and the corrective action is
    /// entirely theirs.
    /// </para>
    /// <para>
    /// IT CARRIES <c>RetCode.E_INVALID_DATA</c> - the same code the sibling payload fault above carries,
    /// and the code the oracle itself returns for a changeset it cannot apply
    /// [<c>n_cst_thread_task_sqlupdate.sru:L343-L344</c>] - so no new member enters the catalogue and
    /// Gateway's existing published mapping of that code to <c>400</c> already applies. A NEW kind rather
    /// than a reuse of <see cref="InvalidUpdateData"/>, because the two faults have different corrective
    /// actions and different diagnostics: that one says "this row expresses no update", this one names a
    /// constraint the schema imposes.
    /// </para>
    /// <para>
    /// ONLY THE CALLER-CONTROLLED CONSTRAINTS ARE CLASSIFIED HERE. A check constraint, a trigger, a
    /// commit hook or a virtual-table refusal is the SCHEMA's own logic failing rather than a value the
    /// caller can correct, so those keep <see cref="DatabaseError"/> and its 502. See
    /// <c>ConflictDetector.IsCallerConstraintViolation</c> for the exact set and the reason each member
    /// is in or out.
    /// </para>
    /// <para>
    /// THE DRIVER PAYLOAD STILL TRAVELS, because it is what names the offending column: SQLite reports
    /// <c>NOT NULL constraint failed: COMPANY.NAME</c>, which is SCHEMA METADATA rather than row data.
    /// The redactor's provider-envelope rule is what lets that identity survive while any value quoted
    /// inside the message is still masked.
    /// </para>
    /// </remarks>
    ConstraintViolation = 7,
}

#endregion

#region The injected surfaces - the transaction, the carrier and the two error channels

/// <summary>
/// The read-and-hook half of <c>n_cst_thread_trans</c> that <c>_of_update</c> consumes: the driver's
/// two code members, its error text, its two predicates, and the two update hooks.
/// </summary>
/// <remarks>
/// <para>
/// WHY THIS IS AN INTERFACE. In the oracle these are members of the transaction object obtained
/// through <c>of_GetTransObject(ref transObject)</c> [<c>:L180</c>], which derives from
/// PowerBuilder's built-in <c>transaction</c> type
/// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:L8</c>] and therefore inherits
/// <c>SQLCode</c>, <c>SQLDBCode</c> and <c>SQLErrText</c> rather than declaring them. Naming just the
/// members this function reads is what makes every arm of the classifier reachable with no database,
/// no driver and no live transaction (C-H).
/// </para>
/// <para>
/// IT IS DELIBERATELY NARROW. Connect, disconnect, commit, rollback, query, exec and the transaction
/// descriptor are all absent: none of them is reached from <c>_of_update</c>, and the commit-or-roll
/// back decision belongs to the CALLER's epilogue [<c>:L385-L401</c>] rather than to the classifier.
/// </para>
/// </remarks>
internal interface IUpdateTransaction
{
    /// <summary>
    /// The DataWindow-level SQL code, inherited from the built-in transaction type. Read by the
    /// defensive override, which tests it for EXACTLY <c>-1</c> [<c>:L208</c>].
    /// </summary>
    /// <value>
    /// The driver's own value. NOT a return code: see this file's header for the three different
    /// conditions the oracle applies to this one field, and why they must not be unified.
    /// </value>
    long SqlCode { get; }

    /// <summary>
    /// The driver's own numeric error code, read into the database-error payload the veto arm raises
    /// [<c>:L197</c>].
    /// </summary>
    /// <value>
    /// A provider code space rather than a return code - a <c>SQLITE_*</c> result code for the SQLite
    /// provider, and another provider's space for another provider.
    /// </value>
    long SqlDbCode { get; }

    /// <summary>
    /// The driver's error message text, read into the database-error payload the veto arm raises
    /// [<c>:L197</c>].
    /// </summary>
    /// <value>
    /// Opaque display text. Never parsed to classify an error, and never scrubbed - it may legitimately
    /// be non-English.
    /// </value>
    string SqlErrText { get; }

    /// <summary>
    /// The transaction's own failure predicate, <c>of_isfailed()</c>, which is
    /// <c>return (SQLCode &lt; 0)</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:L337</c>].
    /// </summary>
    /// <returns><see langword="true"/> when the SQL code is NEGATIVE.</returns>
    /// <remarks>
    /// <para>
    /// THIS IS THE VETO DISCRIMINATOR AND IT IS NOT THE SAME TEST AS THE OVERRIDE'S. The veto arm in
    /// this file calls this predicate [<c>n_cst_thread_task_sqlupdate.sru:L196</c>], so a POSITIVE
    /// non-zero code leaves a veto reporting a clean cancellation - whereas the transaction object's
    /// own veto arm tests non-zero directly [<c>n_cst_thread_trans.sru:L267</c>] and would report a
    /// database error for the same code. Implementations must return <c>SqlCode &lt; 0</c> and nothing
    /// else.
    /// </para>
    /// <para>
    /// THE COMPLEMENT IS DELIBERATELY ABSENT FROM THIS INTERFACE. The oracle also declares
    /// <c>of_issucceeded()</c>, which is <c>return (SQLCode &gt;= 0)</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:L340</c>, prototyped at
    /// <c>:L84</c>] - and a census of the whole <c>pfw.thread.ext</c> library finds it has ZERO CALL
    /// SITES: only <c>of_isfailed()</c> is ever invoked, at
    /// <c>n_cst_thread_task_sqlupdate.sru:L196</c> and <c>n_cst_thread_task_sqlquery.sru:L747</c>. So
    /// declaring it here would widen this interface with a member nothing reads and force every
    /// implementation to carry it, which is the opposite of the narrowing this interface exists for.
    /// Its condition is recorded in this file's header table, because the three SQL-code conditions are
    /// only legible as a set.
    /// </para>
    /// </remarks>
    bool IsFailed();

    /// <summary>
    /// Clears the transaction's five-value SQL state, <c>of_clearstate()</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru</c>, invoked from the update object
    /// at <c>n_cst_thread_task_sqlupdate.sru:L186</c> alongside the DataWindow's own clear].
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>THE ORACLE CLEARS BOTH, AND THE PORT USED TO CLEAR ONLY ONE.</b> <c>:L186</c> is a single
    /// statement that resets the state the whole classification then reads, and this port called it on
    /// the DataWindow target only - so the three SQL-code conditions this interface documents
    /// (<see cref="SqlCode"/>, <see cref="SqlDbCode"/>, <see cref="IsFailed"/>) were read against a
    /// state nothing in the service had ever written, and every one of them therefore answered its
    /// cleared value on every attempt.
    /// </para>
    /// <para>
    /// PER ATTEMPT, WHICH IS WHY IT IS ON THIS INTERFACE AT ALL. A pooled transaction outlives one
    /// update, so a stamp left by an earlier attempt would otherwise be read as this attempt's evidence -
    /// which is the specific way a stale failure turns into a wrong classification for a payload that
    /// was fine.
    /// </para>
    /// </remarks>
    void ClearState();

    /// <summary>
    /// The VETOABLE before-update hook, <c>Event OnBeforeUpdate(Data)</c> [<c>:L195</c>].
    /// </summary>
    /// <returns>
    /// The hook's return code, tested with <c>IsPrevented</c> - so <c>RetCode.PREVENT</c>, which is
    /// <c>1</c>, is a veto and every other value is not.
    /// </returns>
    /// <remarks>
    /// A VETO MEANS TWO DIFFERENT THINGS and the discriminator is <see cref="IsFailed"/>: a veto
    /// combined with a transaction failure yields a database error [<c>:L197-L199</c>], while a clean
    /// veto yields <c>RetCode.CANCELLED</c> [<c>:L201</c>]. The hook is passed the carrier in the
    /// oracle; nothing in this function reads the carrier through the hook's argument, so the argument
    /// is not reproduced on this signature.
    /// </remarks>
    long OnBeforeUpdate();

    /// <summary>
    /// The after-update hook, <c>Event OnAfterUpdate(Data,rtCode)</c> [<c>:L206</c>], fired with the
    /// update's result.
    /// </summary>
    /// <param name="result">
    /// THE UNRECONCILED DataWindow result, exactly as the update answered it. The defensive override
    /// runs AFTER this hook [<c>:L208-L210</c>], so a hook that observes <c>1</c> here may still see
    /// the attempt classified as a failure.
    /// </param>
    /// <remarks>
    /// Fires on EVERY update invocation, success or failure, and its own return value is discarded -
    /// the oracle declares it as a plain event with no return type
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:L15</c>], so it cannot veto
    /// anything and this signature returns <see langword="void"/> to say so.
    /// </remarks>
    void OnAfterUpdate(long result);
}

/// <summary>
/// The two operations <c>_of_update</c> performs on the carrier: clear its accumulated state, and
/// apply the changeset.
/// </summary>
/// <remarks>
/// <para>
/// The oracle's <c>data</c> parameter is an <c>n_cst_thread_task_sqlbase_ds</c>, which derives from
/// <c>datastore</c> and so carries the whole DataWindow surface. Only these two members are reached
/// from this function; the describe surface it also reads is
/// <see cref="IIdentityColumnMetadata"/>, which the sibling resolver already declares, and a single
/// production adapter implements BOTH interfaces on one object exactly as the legacy passes one
/// <c>Data</c>.
/// </para>
/// </remarks>
internal interface IUpdateTarget
{
    /// <summary>
    /// <c>Data.of_ClearState()</c> [<c>:L186</c>], performed before anything else the function does.
    /// </summary>
    /// <remarks>
    /// Clears the row-count accumulators and the rows-exceeded flag. It is NOT a buffer reset and must
    /// not be implemented as one: the caller owns the carrier's rows, and the oracle resets nothing
    /// here.
    /// </remarks>
    void ClearState();

    /// <summary>
    /// <c>Data.Update(true,false)</c> [<c>:L204</c>] - apply the changeset.
    /// </summary>
    /// <param name="acceptText">
    /// The oracle passes <see langword="true"/>: pending edit text is accepted into the buffer before
    /// the statements are generated.
    /// </param>
    /// <param name="resetFlag">
    /// The oracle passes <see langword="false"/>, and the consequence is a contract: THE CALLER OWNS
    /// THE CARRIER'S STATE AFTERWARDS. Item statuses and the original-value shadow survive the call,
    /// which is what makes a retry, an identity round trip and a conflict report possible at all.
    /// </param>
    /// <returns>
    /// THE DATAWINDOW UPDATE CONTRACT'S OWN VALUE, where <c>1</c> is success and <c>-1</c> is failure.
    /// NOT a return code - see this file's header.
    /// </returns>
    long Update(bool acceptText, bool resetFlag, CancellationToken cancellationToken = default);

    /// <summary>
    /// The affected-row measurement for the statements <see cref="Update"/> just ran, or
    /// <see langword="null"/> when nothing was measured.
    /// </summary>
    /// <returns>
    /// The evidence, whose counts describe THE STATEMENTS THIS ATTEMPT GENERATED and nothing earlier.
    /// <see langword="null"/> means "not measured", which is the oracle's own state - it performs no
    /// rows-affected check anywhere - and declines the conflict narrowing.
    /// </returns>
    /// <remarks>
    /// <para>
    /// 🔴 <b>READ AFTER THE UPDATE HAS EXECUTED, ON THE SAME TRANSACTION, AND NEVER BEFORE IT.</b> The
    /// measurement does not exist until the statements have run: an implementation cannot know how many
    /// rows a predicate matched until the predicate has been submitted. A pre-captured value is
    /// therefore always the PREVIOUS attempt's - null on the first - and reading one would make a
    /// zero-row optimistic miss indistinguishable from a clean success. That is why this is a member of
    /// the target rather than a field of <see cref="UpdateAttempt"/>: an attempt is an immutable input
    /// assembled before the update, and this value cannot be.
    /// </para>
    /// <para>
    /// IT IS PULLED BY THE CLASSIFIER RATHER THAN PUSHED BY THE EXECUTOR, because only the classifier
    /// knows the one moment at which it is both available and still authoritative - after
    /// <see cref="Update"/> returns and before any success is published or anything is committed.
    /// </para>
    /// <para>
    /// AN IMPLEMENTATION THAT CANNOT MEASURE ANSWERS <see langword="null"/> AND IS NO WORSE OFF THAN THE
    /// ORACLE. Every conflict arm therefore stays reachable with no database at all, which is what keeps
    /// the classification testable through substitution (constraint C-H).
    /// </para>
    /// </remarks>
    ConcurrencyEvidence? CaptureConcurrencyEvidence();
}


/// <summary>
/// The structured replacement for the oracle's two SEPARATE error channels,
/// <c>Event OnDBError(...)</c> and <c>Event OnError(...)</c>.
/// </summary>
/// <remarks>
/// <para>
/// BOTH CHANNELS ARE PRESERVED AS TWO MEMBERS BECAUSE THE ORACLE RAISES BOTH, IN ORDER, AND THEY DO
/// NOT CARRY THE SAME PAYLOAD. In the sentinel arm the database-error raise carries five positional
/// values and the general raise carries the SAME message [<c>:L190-L191</c>]; in the veto arm the
/// database-error raise carries the transaction's own code and text while the general raise carries an
/// EMPTY message [<c>:L197-L198</c>]. Collapsing the two into one call, or "tidying" the empty message
/// into the populated one, would change what a consumer observes (C-B).
/// </para>
/// <para>
/// ONLY THE DELIVERY CHANNEL CHANGES (C-D). The oracle's raises reach a PowerBuilder event and,
/// further up, a dialog; here they reach an injected sink. The code, the text, the buffer and the row
/// are preserved exactly, and no dialog, message box or user-interface analogue is created anywhere in
/// this file.
/// </para>
/// <para>
/// A SINK MUST NOT THROW. The oracle's raise is a plain event dispatch that cannot abort the function,
/// and every arm below continues to its own <c>return</c> after raising. An implementation that threw
/// would skip the return and change the observable outcome.
/// </para>
/// </remarks>
internal interface IUpdateErrorSink
{
    /// <summary>
    /// <c>Event OnDBError(sqldbcode, sqlerrtext, sqlsyntax, buffer, row)</c> - the five-positional
    /// database-error channel, raised at [<c>:L190</c>] and [<c>:L197</c>].
    /// </summary>
    /// <param name="error">
    /// The payload, ALREADY REDACTED. Taken by <see langword="in"/> because the legacy structure is a
    /// <c>readonly</c> parameter wherever it is passed by reference.
    /// </param>
    void OnDbError(in DbErrorData error);

    /// <summary>
    /// <c>Event OnError(code, message)</c> - the general error channel, raised at [<c>:L191</c>] and
    /// [<c>:L198</c>].
    /// </summary>
    /// <param name="code">
    /// The return code, which is <c>RetCode.E_DB_ERROR</c> at both sites. An IN-PROCESS algebra value.
    /// </param>
    /// <param name="errorText">
    /// The message. POPULATED in the sentinel arm and EMPTY in the veto arm - the difference is legacy
    /// behaviour and is reproduced rather than harmonized.
    /// </param>
    void OnError(long code, string errorText);
}

#endregion

#region The conflict evidence - the affected-row measurement, supplied as an INPUT

/// <summary>
/// One marked column, as the conflict payload names it: the column's name and its ONE-BASED ordinal.
/// </summary>
/// <param name="Name">
/// The column's name as the DataWindow declares it. Copied onto the payload verbatim; never
/// normalised, lower-cased or reordered.
/// </param>
/// <param name="Number">
/// The column's ONE-BASED ordinal, which is the domain every DataWindow column index lives in (R9).
/// Column index <c>0</c> is not a column - it addresses THE ROW ITSELF - so it is rejected here.
/// </param>
/// <remarks>
/// <para>
/// THE COLUMNS ARRIVE AS AN ARGUMENT AND ARE NEVER DERIVED FROM A HARDCODED SCHEMA (C-E). The sole
/// evidenced fixture happens to mark six columns
/// [<c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L8-L13</c>], but that is test data: nothing in this
/// file names a table, a column or a count.
/// </para>
/// <para>
/// The set to pass is the MARKED set - the columns carrying <c>updatewhereclause=yes</c> - because
/// under <c>updatewhere=1</c> those are precisely the columns whose ORIGINAL values formed the failed
/// statement's where clause. <c>UpdateWhereBuilder.MarkedColumnsOf</c> answers the names for a
/// descriptor; the ordinals come from the carrier's describe surface.
/// </para>
/// </remarks>
internal readonly record struct ConflictColumn(string Name, int Number)
{
    /// <summary>
    /// Whether this column can appear on a conflict payload: a non-blank name and a genuine one-based
    /// ordinal.
    /// </summary>
    /// <value>
    /// <see langword="false"/> for a blank name or for an ordinal below
    /// <see cref="ItemStatusMachine.FirstColumnNumber"/> - which includes <c>0</c>, the row-status
    /// index, and any negative value produced by rebasing a one-based ordinal by mistake.
    /// </value>
    internal bool IsAddressable =>
        !string.IsNullOrWhiteSpace(Name) && Number >= ItemStatusMachine.FirstColumnNumber;
}

/// <summary>
/// The affected-row measurement for one update attempt: what the failing statement targeted, what it
/// actually matched, whether the provider itself faulted, and the current state of the rows that
/// failed their check.
/// </summary>
/// <remarks>
/// <para>
/// THIS TYPE HAS NO LEGACY COUNTERPART AND THAT IS THE WHOLE POINT. The oracle measures nothing: an
/// exhaustive search of its update path finds no rows-affected check of any kind, so it cannot
/// distinguish a concurrency mismatch from a constraint violation. This record is the evidence the
/// mandated narrowing needs, and it is an INPUT rather than something the classifier goes and fetches
/// - which is what keeps every conflict arm reachable with no database and no real concurrent writer
/// (C-H).
/// </para>
/// <para>
/// IT IS SUPPLIED BY WHOEVER EXECUTED THE STATEMENTS, because only that code knows both numbers and
/// whether the provider faulted. Absence - a <see langword="null"/> evidence on the attempt - means
/// "nothing was measured", which is the ordinary case for every arm that fails BEFORE a statement is
/// generated.
/// </para>
/// </remarks>
internal sealed record ConcurrencyEvidence
{
    /// <summary>
    /// How many rows the failing statement expected to affect.
    /// </summary>
    /// <value>
    /// A count, not an index. For the classic single-row optimistic-concurrency failure this is
    /// <c>1</c>. Zero or negative means "not measured", and no conflict is classified from it.
    /// </value>
    internal long RowsExpected { get; init; }

    /// <summary>
    /// How many rows the failing statement actually matched.
    /// </summary>
    /// <value>
    /// For the classic failure this is <c>0</c>. Stating both numbers keeps "the row changed"
    /// distinguishable from "the row was deleted" without a second round trip, which is exactly what
    /// the published contract says the pair is for.
    /// </value>
    internal long RowsMatched { get; init; }

    /// <summary>
    /// Whether the provider itself reported a fault, in which case any shortfall between the two
    /// counts is NOT attributable to a concurrency mismatch.
    /// </summary>
    /// <value>
    /// <para>
    /// <see langword="true"/> for a constraint violation, a connection fault, a syntax error or any
    /// other provider-raised failure. <see langword="false"/> only when the statements were accepted
    /// and executed and the shortfall is therefore a genuine optimistic-concurrency miss.
    /// </para>
    /// <para>
    /// THIS FLAG IS WHAT KEEPS THE NARROWING FROM SWALLOWING GENUINE DATABASE ERRORS. It is checked in
    /// addition to the two counts, so evidence that is stale, partially populated or carried over from
    /// a faulted statement cannot be promoted into a conflict.
    /// </para>
    /// </value>
    internal bool ProviderFaulted { get; init; }

    /// <summary>
    /// How many rows the payload flagged modified while supplying no updatable column value for them,
    /// so that no assignment could be generated at all.
    /// </summary>
    /// <value>
    /// <para>
    /// Zero in every ordinary case, including an update that legitimately writes nothing because no row
    /// was modified - that row never reaches the walk. A positive count means the CALLER's payload
    /// contradicted itself: the row states <c>DataModified!</c> while every updatable column of it
    /// states <c>NotModified!</c>, or the row carries no updatable column at all.
    /// </para>
    /// <para>
    /// 🔴 <b>IT IS COUNTED SEPARATELY FROM THE TWO ROW COUNTS BECAUSE REPORTING IT AS A SHORTFALL WAS
    /// WRONG IN THE MOST MISLEADING WAY AVAILABLE.</b> Such a row used to be counted as a generated
    /// statement and recorded as unmatched, which made
    /// <see cref="ConflictDetector.IsConcurrencyMismatch"/> answer true and told the caller that another
    /// writer had changed a row nothing had touched - sending an integrator to look for a concurrency
    /// problem that did not exist. A payload that cannot express an update is the caller's own fault and
    /// is answered as one; only a statement that RAN and matched nothing is a concurrency miss.
    /// </para>
    /// </value>
    internal long RowsWithoutAssignableValues { get; init; }

    /// <summary>
    /// The rows that failed their concurrency check, each carrying its buffer, its ONE-BASED row
    /// ordinal, its current server-side status, and BOTH the current and the original value of every
    /// marked column.
    /// </summary>
    /// <value>
    /// Empty when no row state was captured. An empty list DECLINES the conflict classification - see
    /// <see cref="ConflictDetector.IsConcurrencyMismatch"/> - because the published contract states
    /// that an <c>Aborted</c> response never carries an empty row list, and an <c>Aborted</c> with
    /// nothing in it would tell the caller nothing it could act on.
    /// </value>
    /// <remarks>
    /// Build these with <see cref="ConflictDetector.ProjectConflictRow"/>, which reads the pair of
    /// values from the <c>Buffers/</c> anti-corruption layer rather than re-deriving either.
    /// </remarks>
    internal IReadOnlyList<ConflictRow> Rows { get; init; } = [];
}

#endregion


#region The attempt - every input one classification reads, and nothing else

/// <summary>
/// Everything one update attempt is classified from. ONE INSTANCE PER UPDATE TABLE.
/// </summary>
/// <remarks>
/// <para>
/// WHY A RECORD RATHER THAN AN EIGHT-PARAMETER SIGNATURE. Every member here is an input the oracle
/// reads at a named locator, and gathering them means a test can vary exactly one - the SQL code, the
/// update result, the veto outcome, the described update table, the affected-row evidence - while
/// holding the rest fixed. That is the property the per-service coverage gate depends on, because it is
/// what makes all of the following reachable with no database: three sentinels plus a legal name,
/// veto-with-failure and veto-without-failure, each cancellation check independently, update results
/// <c>1</c>, <c>0</c>, <c>2</c> and <c>-1</c>, SQL codes of exactly <c>-1</c>, another negative value,
/// a positive value and <c>0</c>, and the conflict-versus-genuine-error discrimination (C-H).
/// </para>
/// <para>
/// THE MULTI-TABLE SHAPE, WHICH IS WHY THIS IS PER TABLE AND NOT PER REQUEST. With the multi-table
/// switch on, the oracle loops <c>for nIndex = 1 to UpperBound(Tables)</c> calling
/// <c>_of_UpdatePrepare</c> and then <c>_of_Update</c> once per table and STOPPING AT THE FIRST FAILURE
/// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru:L356-L369</c>]; with it off,
/// the update runs ONCE with no prepare at all [<c>:L370-L371</c>], trusting the carrier's static
/// definition. `UpdateWhereBuilder.UpdatePreparer.PrepareAndUpdate` already implements that loop and
/// drives the classifier through its <c>Func&lt;long&gt;</c> update step, so one attempt is built per
/// iteration.
/// </para>
/// <para>
/// ACCUMULATION IS NOT THIS FILE'S JOB. The caller-side proxy accumulates the counts with <c>+=</c>
/// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlupdate.sru:L66-L68</c>] and APPENDS
/// one identity record per firing [<c>:L71-L77</c>], so per-invocation reporting is correct and the
/// classifier holds nothing between calls.
/// </para>
/// </remarks>
internal sealed record UpdateAttempt
{
    /// <summary>
    /// The transaction, or <see langword="null"/> to model a FAILED ACQUISITION.
    /// </summary>
    /// <value>
    /// <see langword="null"/> reproduces
    /// <c>if IsFailed(of_GetTransObject(ref transObject)) then return RetCode.E_INVALID_TRANSACTION</c>
    /// [<c>:L180</c>]. The oracle's out-parameter is left unset on that path, so a null reference is the
    /// faithful model of it and no separate flag is invented.
    /// </value>
    internal IUpdateTransaction? Transaction { get; init; }

    /// <summary>
    /// The carrier the state is cleared on and the update is invoked through.
    /// </summary>
    internal required IUpdateTarget Target { get; init; }

    /// <summary>
    /// The describe surface the update table is read through, paired with the value surface the
    /// identity round trip and the counts report are read through.
    /// </summary>
    /// <value>
    /// The sibling resolver's own vocabulary, reused deliberately: <c>Metadata</c> answers
    /// <c>Describe("DataWindow.Table.UpdateTable")</c> [<c>:L188</c>] and <c>Values</c> answers the
    /// counts and statuses the success path reads [<c>:L215-L247</c>]. A production adapter implements
    /// both on one object, exactly as the legacy passes one <c>Data</c>.
    /// </value>
    internal required IdentityTableSurfaces Identity { get; init; }

    /// <summary>
    /// The two error channels the sentinel and veto arms raise on.
    /// </summary>
    internal required IUpdateErrorSink Errors { get; init; }

    /// <summary>
    /// The cancellation signal, POLLED TWICE - once before the update [<c>:L182</c>] and once after it
    /// [<c>:L212</c>].
    /// </summary>
    /// <value>
    /// <para>
    /// The port of <c>of_IsCancelled()</c>, which polls a flag the caller-side proxy sets. A
    /// <see cref="CancellationToken"/> is the same shape - a flag set by the caller and polled by the
    /// worker - and is the mechanism the migration plan fixes for the legacy proxy pair, so it is used
    /// rather than a bespoke predicate.
    /// </para>
    /// <para>
    /// IT IS POLLED, NEVER THROWN THROUGH. <c>ThrowIfCancellationRequested</c> is deliberately not
    /// called: the oracle RETURNS a code on this path, and the caller's epilogue distinguishes that
    /// code from every other one in order to suppress a duplicate error raise [<c>:L396</c>]. An
    /// exception would bypass both.
    /// </para>
    /// <para>
    /// The default is <see cref="CancellationToken.None"/>, which never reports cancellation - so an
    /// attempt that does not supply one simply never takes either cancellation arm.
    /// </para>
    /// </value>
    internal CancellationToken Cancellation { get; init; }

    // ⚠️ THERE IS DELIBERATELY NO `Evidence` MEMBER HERE, AND ITS ABSENCE IS THE FIX RATHER THAN AN
    // OMISSION. The affected-row measurement cannot be an input of an attempt: an attempt is assembled
    // BEFORE the update runs, and the measurement does not exist until AFTER it has. A pre-captured
    // value is therefore always the previous attempt's - null on the first - so a zero-row optimistic
    // miss read as success. The measurement is pulled from
    // IUpdateTarget.CaptureConcurrencyEvidence() at the one moment it is authoritative: immediately
    // after Update returns, before any success is published and before anything is committed.
}

#endregion

#region The outcome - the classification, plus what the caller's epilogue needs to act on it

/// <summary>
/// What one update attempt turned out to be, in enough detail that the caller's commit-or-rollback
/// epilogue can act without re-deriving anything.
/// </summary>
/// <remarks>
/// <para>
/// WHAT THE EPILOGUE NEEDS, AND WHY EACH MEMBER IS HERE. The oracle's epilogue reads
/// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru:L385-L401</c>]:
/// </para>
/// <code>
/// if rtCode = RetCode.OK then
///     if _bAutoCommit then rtCode = of_Commit(true) ...
/// else
///     transObject.of_Rollback()
///     if rtCode &lt;&gt; RetCode.CANCELLED then
///         if rtCode &lt;&gt; of_GetLastErrorCode() then Event OnError(rtCode,sError)
///     end if
/// end if
/// </code>
/// <para>
/// So it needs three things from the classification and gets all three from members below:
/// whether the outcome was <c>RetCode.OK</c> (<see cref="IsSucceeded"/>, and its complement
/// <see cref="RequiresRollback"/>); whether it was the cancelled code
/// (<see cref="UpdateOutcomeKind.Cancelled"/>); and whether an error was ALREADY REPORTED on the
/// general channel, which the oracle establishes by comparing against the last reported code
/// (<see cref="ErrorReported"/>). Handing the epilogue a bare code would force it to re-derive the
/// third from a mutable side channel.
/// </para>
/// <para>
/// CONSTRUCTION IS THROUGH THE FACTORIES ONLY. The constructor is private, so there is no
/// default-constructed outcome and <see cref="UpdateOutcomeKind.None"/> is unreachable - which matters
/// because <c>RetCode.OK</c> is zero and a default outcome would otherwise read as a successful update.
/// </para>
/// </remarks>
internal sealed record UpdateOutcome
{
    /// <summary>
    /// Prevents construction outside the factories, so no outcome can exist without a kind and a code
    /// that agree.
    /// </summary>
    private UpdateOutcome()
    {
    }

    /// <summary>
    /// Which arm the classification took.
    /// </summary>
    internal required UpdateOutcomeKind Kind { get; init; }

    /// <summary>
    /// The return code the oracle's <c>_of_update</c> returns for this arm - an IN-PROCESS
    /// <c>PowerFramework.Shared.Kernel.RetCode</c> value.
    /// </summary>
    /// <value>
    /// <c>RetCode.OK</c> [<c>:L248</c>], <c>RetCode.CANCELLED</c> [<c>:L182</c>, <c>:L201</c>,
    /// <c>:L212</c>], <c>RetCode.E_INVALID_TRANSACTION</c> [<c>:L180</c>] or
    /// <c>RetCode.E_DB_ERROR</c> [<c>:L192</c>, <c>:L199</c>, <c>:L250</c>]. A conflict carries
    /// <c>RetCode.E_DB_ERROR</c> UNCHANGED, because the narrowing must not alter any legacy arm.
    /// </value>
    internal required long Code { get; init; }

    /// <summary>
    /// Whether the update was actually invoked.
    /// </summary>
    /// <value>
    /// <see langword="false"/> for the transaction fault, the first cancellation check, the sentinel
    /// arm and both veto arms - every one of which returns before [<c>:L204</c>].
    /// </value>
    internal bool UpdateInvoked { get; init; }

    /// <summary>
    /// The RECONCILED DataWindow update result: the value after the defensive override has been applied.
    /// </summary>
    /// <value>
    /// <c>1</c> for success and <c>-1</c> for failure IN THE DATAWINDOW CODE SPACE, not the return-code
    /// algebra. <c>0</c> when the update was never invoked, which is the value the oracle's local
    /// <c>rtCode</c> still holds at that point. Never pass this to a return-code predicate.
    /// </value>
    internal long UpdateResult { get; init; }

    /// <summary>
    /// The UNRECONCILED result the after-update hook was handed [<c>:L206</c>], before the defensive
    /// override at [<c>:L208-L210</c>].
    /// </summary>
    /// <value>
    /// Equal to <see cref="UpdateResult"/> unless <see cref="DefensiveOverrideApplied"/> is
    /// <see langword="true"/>, in which case this is <c>1</c> and <see cref="UpdateResult"/> is
    /// <c>-1</c>. Recorded so the hook ordering is checkable rather than merely asserted in a comment.
    /// </value>
    internal long UpdateResultObservedByHook { get; init; }

    /// <summary>
    /// Whether the defensive override rewrote a claimed success into a failure [<c>:L208-L210</c>].
    /// </summary>
    /// <value>
    /// <see langword="true"/> only when the transaction's SQL code was EXACTLY <c>-1</c> AND the update
    /// answered <c>1</c>. A different negative code, a positive code, a zero code, or an update result
    /// other than <c>1</c> all leave this <see langword="false"/>.
    /// </value>
    internal bool DefensiveOverrideApplied { get; init; }

    /// <summary>
    /// The update table the attempt targeted, exactly as describe answered it [<c>:L188</c>].
    /// </summary>
    /// <value>
    /// Carried through to the conflict payload, where it is the only way a caller can tell WHICH of
    /// several targeted tables a mismatch came from. Empty on the arms that return before the describe.
    /// It may legitimately be a sentinel on the sentinel arm - the raw answer is preserved rather than
    /// normalised away.
    /// </value>
    internal string UpdateTable { get; init; } = string.Empty;

    /// <summary>
    /// The database-error payload for this outcome, or <see langword="null"/> when there is none.
    /// </summary>
    /// <value>
    /// <para>
    /// Populated on all three database-error arms and on a conflict. ALREADY REDACTED: the statement
    /// passed through the injected redactor before the payload was built.
    /// </para>
    /// <para>
    /// PRESENCE HERE DOES NOT MEAN THE ERROR WAS RAISED - see <see cref="ErrorReported"/>. On the else
    /// arm at [<c>:L250</c>] the oracle raises NOTHING and merely returns the code, so the payload is
    /// carried as data for the epilogue and for the wire response while no sink call is made.
    /// </para>
    /// </value>
    internal DbErrorData? DbError { get; init; }

    /// <summary>
    /// Whether the general error channel was already raised for this outcome.
    /// </summary>
    /// <value>
    /// <para>
    /// <see langword="true"/> on the sentinel arm [<c>:L191</c>] and the failing-veto arm
    /// [<c>:L198</c>]. <see langword="false"/> everywhere else, INCLUDING the else arm at
    /// [<c>:L250</c>] and a conflict, because the oracle raises nothing there.
    /// </para>
    /// <para>
    /// This is the port of the epilogue's <c>if rtCode &lt;&gt; of_GetLastErrorCode()</c> suppression
    /// [<c>:L397</c>]: a caller must not raise a second time for an outcome that already reported.
    /// </para>
    /// </value>
    internal bool ErrorReported { get; init; }

    /// <summary>
    /// The message passed on the general error channel, when one was raised.
    /// </summary>
    /// <value>
    /// The no-updatable-table diagnostic on the sentinel arm [<c>:L191</c>], and DELIBERATELY EMPTY on
    /// the failing-veto arm [<c>:L198</c>]. Empty when nothing was raised. The asymmetry between the two
    /// raising arms is legacy behaviour and is reproduced rather than harmonized (C-B).
    /// </value>
    internal string ErrorText { get; init; } = string.Empty;

    /// <summary>
    /// The conflict payload, populated only for <see cref="UpdateOutcomeKind.Conflict"/>.
    /// </summary>
    /// <value>
    /// Carries every row that failed its check with BOTH its current and its original marked-column
    /// values, the targeted update table, and the expected-versus-matched row counts.
    /// <see langword="null"/> for every other kind.
    /// </value>
    internal ConflictDetail? Conflict { get; init; }

    /// <summary>
    /// The identity blocks and the counts triple, populated only for
    /// <see cref="UpdateOutcomeKind.Succeeded"/>.
    /// </summary>
    /// <value>
    /// The sibling resolver's outcome, delegated to rather than reimplemented. The identity list is
    /// empty when the oracle would have fired nothing, and the counts are present on EVERY success -
    /// including a pure update and a pure delete, because the counts callback sits outside the
    /// inserted-count block [<c>:L247</c>].
    /// </value>
    internal IdentityResolutionOutcome? Identity { get; init; }

    /// <summary>
    /// Whether the attempt succeeded, tested the way the epilogue tests it.
    /// </summary>
    /// <value>
    /// <c>Code == RetCode.OK</c>, DELIBERATELY NOT <c>Predicates.IsSucceeded(Code)</c>. The oracle's
    /// epilogue is <c>if rtCode = RetCode.OK then</c> [<c>:L385</c>], and under the published tri-state
    /// algebra <c>IsSucceeded</c> is true for <c>RetCode.PREVENT</c> as well - so substituting the
    /// predicate would let a prevention commit.
    /// </value>
    internal bool IsSucceeded => Code == RetCode.OK;

    /// <summary>
    /// Whether the caller's epilogue must roll back.
    /// </summary>
    /// <value>
    /// The exact complement of <see cref="IsSucceeded"/>, because the oracle's rollback is the ELSE of
    /// the same test [<c>:L394-L395</c>] - so a cancellation rolls back too, and only the success arm
    /// reaches the commit.
    /// </value>
    internal bool RequiresRollback => !IsSucceeded;

    /// <summary>
    /// The return code in its WIRE form, for placing on a contract message.
    /// </summary>
    /// <value>
    /// The same numeric value as <see cref="Code"/>, converted to the generated proto enum. The two
    /// domains agree member for member by construction, which is what makes the conversion a
    /// re-typing rather than a mapping.
    /// </value>
    internal WireRetCode WireCode => (WireRetCode)Code;

    // ----------------------------------------------------------------------------------------------
    //  THE FACTORIES - ONE PER LEGACY ARM, EACH NAMING ITS LOCATOR
    //  --------------------------------------------------------------------------------------------
    //  One factory per outcome the oracle can produce, and no more. Each pins the code its arm returns
    //  so a kind and a code cannot be paired incorrectly at a call site, which is the one mistake that
    //  would let a conflict report a code the legacy never returns.
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// <c>return RetCode.E_INVALID_TRANSACTION</c> - the transaction could not be acquired
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru:L180</c>].
    /// </summary>
    /// <returns>The outcome for that arm.</returns>
    /// <remarks>
    /// Nothing is raised on either error channel here, and nothing is cleared: the oracle returns before
    /// <c>Data.of_ClearState()</c> at [<c>:L186</c>]. The SEPARATE transaction-acquisition failure in
    /// <c>ondotask</c> DOES raise both channels [<c>:L292-L295</c>], but that is the caller's arm and not
    /// this function's - conflating the two would invent a raise this arm does not perform.
    /// </remarks>
    internal static UpdateOutcome InvalidTransaction() => new()
    {
        Kind = UpdateOutcomeKind.InvalidTransaction,
        Code = RetCode.E_INVALID_TRANSACTION,
    };

    /// <summary>
    /// <c>return RetCode.CANCELLED</c> - from either cancellation check or from a clean veto
    /// [<c>:L182</c>, <c>:L201</c>, <c>:L212</c>].
    /// </summary>
    /// <param name="updateInvoked">
    /// <see langword="false"/> for the first check and the clean veto; <see langword="true"/> for the
    /// second check, which runs after the update has already been applied.
    /// </param>
    /// <param name="updateResult">The reconciled DataWindow result, or <c>0</c> if never invoked.</param>
    /// <param name="observedByHook">The unreconciled result the after-update hook was handed.</param>
    /// <param name="overrideApplied">Whether the defensive override had already fired.</param>
    /// <param name="updateTable">The described update table, empty when the describe had not run.</param>
    /// <returns>The outcome for that arm.</returns>
    /// <remarks>
    /// A CANCELLATION AFTER A SUCCESSFUL UPDATE IS STILL A CANCELLATION, and the success path is NOT
    /// taken - so the identity round trip and the counts report do not fire even though the rows were
    /// written. The update's own result is retained here so a caller can see what happened before the
    /// cancellation was observed; the epilogue still rolls back [<c>:L394-L395</c>] and still suppresses
    /// the error raise for this code [<c>:L396</c>].
    /// </remarks>
    internal static UpdateOutcome Cancelled(
        bool updateInvoked = false,
        long updateResult = 0L,
        long observedByHook = 0L,
        bool overrideApplied = false,
        string? updateTable = null) => new()
        {
            Kind = UpdateOutcomeKind.Cancelled,
            Code = RetCode.CANCELLED,
            UpdateInvoked = updateInvoked,
            UpdateResult = updateResult,
            UpdateResultObservedByHook = observedByHook,
            DefensiveOverrideApplied = overrideApplied,
            UpdateTable = updateTable ?? string.Empty,
        };

    /// <summary>
    /// <c>return RetCode.E_DB_ERROR</c> - the sentinel arm [<c>:L192</c>], the failing-veto arm
    /// [<c>:L199</c>], or the else arm [<c>:L250</c>].
    /// </summary>
    /// <param name="error">The redacted payload, or <see langword="null"/> when there is none.</param>
    /// <param name="errorReported">
    /// Whether the general channel was raised - <see langword="true"/> for the two raising arms and
    /// <see langword="false"/> for the else arm, which raises nothing.
    /// </param>
    /// <param name="errorText">
    /// The message that WAS raised: the diagnostic on the sentinel arm and DELIBERATELY EMPTY on the
    /// failing-veto arm.
    /// </param>
    /// <param name="updateTable">The described update table, or empty when the describe had not run.</param>
    /// <param name="updateInvoked">Whether the update ran before this outcome was reached.</param>
    /// <param name="updateResult">The reconciled DataWindow result, or <c>0</c> if never invoked.</param>
    /// <param name="observedByHook">The unreconciled result the after-update hook was handed.</param>
    /// <param name="overrideApplied">Whether the defensive override rewrote a claimed success.</param>
    /// <returns>The outcome for that arm.</returns>
    internal static UpdateOutcome DatabaseError(
        DbErrorData? error,
        bool errorReported,
        string? errorText = null,
        string? updateTable = null,
        bool updateInvoked = false,
        long updateResult = 0L,
        long observedByHook = 0L,
        bool overrideApplied = false) => new()
        {
            Kind = UpdateOutcomeKind.DatabaseError,
            Code = RetCode.E_DB_ERROR,
            DbError = error,
            ErrorReported = errorReported,
            ErrorText = errorText ?? string.Empty,
            UpdateTable = updateTable ?? string.Empty,
            UpdateInvoked = updateInvoked,
            UpdateResult = updateResult,
            UpdateResultObservedByHook = observedByHook,
            DefensiveOverrideApplied = overrideApplied,
        };

    /// <summary>
    /// An optimistic-concurrency mismatch: the else arm at [<c>:L250</c>], NARROWED. THE ONE OUTCOME
    /// WITH NO LEGACY COUNTERPART.
    /// </summary>
    /// <param name="conflict">The populated payload. Its row list is never empty.</param>
    /// <param name="error">The redacted payload carrying the transaction's own code and text.</param>
    /// <param name="updateTable">The described update table.</param>
    /// <param name="updateResult">The reconciled DataWindow result.</param>
    /// <param name="observedByHook">The unreconciled result the after-update hook was handed.</param>
    /// <param name="overrideApplied">Whether the defensive override rewrote a claimed success.</param>
    /// <returns>The outcome for that arm.</returns>
    /// <remarks>
    /// <para>
    /// THE CODE IS <c>RetCode.E_DB_ERROR</c>, UNCHANGED FROM THE ARM THIS NARROWS. The narrowing changes
    /// the KIND and adds a payload; it does not invent a return code, because the oracle's arm returns
    /// that code and no legacy arm may change.
    /// </para>
    /// <para>
    /// <see cref="ErrorReported"/> is <see langword="false"/> for the same reason it is on the else arm:
    /// the oracle raises nothing there, so neither does this.
    /// </para>
    /// </remarks>
    internal static UpdateOutcome ConflictDetected(
        ConflictDetail conflict,
        DbErrorData? error,
        string? updateTable = null,
        long updateResult = 0L,
        long observedByHook = 0L,
        bool overrideApplied = false)
    {
        ArgumentNullException.ThrowIfNull(conflict);

        return new UpdateOutcome
        {
            Kind = UpdateOutcomeKind.Conflict,
            Code = RetCode.E_DB_ERROR,
            Conflict = conflict,
            DbError = error,
            ErrorReported = false,
            UpdateTable = updateTable ?? string.Empty,
            UpdateInvoked = true,
            UpdateResult = updateResult,
            UpdateResultObservedByHook = observedByHook,
            DefensiveOverrideApplied = overrideApplied,
        };
    }

    /// <summary>
    /// The caller's payload flagged a row modified but supplied no updatable column value for it.
    /// <b>A REFUSAL THIS BOUNDARY OWNS, NOT AN ARM OF THE ORACLE.</b>
    /// </summary>
    /// <param name="rowsWithoutAssignableValues">
    /// How many rows were in that state. Carried on the diagnostic so an operator can tell one
    /// contradictory row from a whole payload of them; the row's VALUES are never named.
    /// </param>
    /// <param name="updateTable">The described update table.</param>
    /// <param name="updateResult">The reconciled DataWindow result.</param>
    /// <param name="observedByHook">The unreconciled result the after-update hook was handed.</param>
    /// <param name="overrideApplied">Whether the defensive override rewrote a claimed success.</param>
    /// <returns>The outcome for that arm.</returns>
    /// <remarks>
    /// <para>
    /// THE CODE IS THE ORACLE'S OWN INVALID-UPDATE-DATA CODE, <c>RetCode.E_INVALID_DATA</c>
    /// [<c>n_cst_thread_task_sqlupdate.sru:L343-L344</c>], so no new member enters the catalogue and the
    /// existing published mapping - <c>400</c>, "the caller's payload is at fault" - already applies.
    /// </para>
    /// <para>
    /// <see cref="ErrorReported"/> IS <see langword="TRUE"/>, unlike the conflict narrowing, because this
    /// arm DOES raise the general error channel: the condition is invisible in the row counts - nothing
    /// ran, so nothing is missing from them - and an operator with no record at all could not tell this
    /// refusal from a caller that simply sent an empty changeset.
    /// </para>
    /// <para>
    /// <see cref="RequiresRollback"/> follows from the code and is correct: a payload carrying one
    /// contradictory row may also carry rows that DID apply, and applying part of a refused payload is
    /// exactly the partial write the epilogue's rollback exists to prevent.
    /// </para>
    /// </remarks>
    internal static UpdateOutcome InvalidUpdateData(
        long rowsWithoutAssignableValues,
        string? updateTable = null,
        long updateResult = 0L,
        long observedByHook = 0L,
        bool overrideApplied = false) => new()
        {
            Kind = UpdateOutcomeKind.InvalidUpdateData,
            Code = RetCode.E_INVALID_DATA,
            ErrorReported = true,
            ErrorText = string.Format(
                CultureInfo.InvariantCulture,
                InvalidUpdateDataFormat,
                rowsWithoutAssignableValues),
            UpdateTable = updateTable ?? string.Empty,
            UpdateInvoked = true,
            UpdateResult = updateResult,
            UpdateResultObservedByHook = observedByHook,
            DefensiveOverrideApplied = overrideApplied,
        };

    /// <summary>
    /// The diagnostic the invalid-payload arm reports, with the offending row count substituted.
    /// </summary>
    /// <remarks>
    /// IT NAMES THE RULE AND THE COUNT AND NOTHING ELSE (C-F). No column name, no value and no statement
    /// appears in it, so it is safe on both the caller channel and the operator channel, and it tells a
    /// caller exactly which field to add.
    /// </remarks>
    internal const string InvalidUpdateDataFormat =
        "{0} row(s) were flagged modified but supplied no updatable column value, so no assignment "
        + "could be generated. A row's own item status is honoured for every column it supplies when "
        + "no per-column status is sent; a row that marks every column NotModified, or that carries no "
        + "updatable column, cannot express an update.";

    /// <summary>
    /// The storage engine refused a row on a constraint the caller's payload controls.
    /// </summary>
    /// <param name="error">The driver payload, already redacted, which names the offending column.</param>
    /// <param name="updateTable">The described update table.</param>
    /// <param name="updateResult">The value the DataWindow update answered.</param>
    /// <param name="observedByHook">The value the after-update hook observed.</param>
    /// <param name="overrideApplied">Whether the defensive override rewrote a claimed success.</param>
    /// <returns>The outcome.</returns>
    /// <remarks>
    /// <para>
    /// <see cref="ErrorReported"/> IS <see langword="FALSE"/>, matching the arm this splits out of: the
    /// oracle raises nothing at [<c>:L249-L250</c>] and lets the epilogue decide by comparing against the
    /// last reported code [<c>:L397</c>]. Setting it true here would make the epilogue skip a raise the
    /// oracle performs.
    /// </para>
    /// <para>
    /// <see cref="ErrorText"/> IS EMPTY for the same reason, and the diagnosis travels on the driver
    /// payload where the provider put it. Synthesising a second sentence here would put this file in the
    /// business of composing driver text it did not author.
    /// </para>
    /// </remarks>
    internal static UpdateOutcome ConstraintViolation(
        DbErrorData error,
        string? updateTable = null,
        long updateResult = 0L,
        long observedByHook = 0L,
        bool overrideApplied = false) => new()
        {
            Kind = UpdateOutcomeKind.ConstraintViolation,
            Code = RetCode.E_INVALID_DATA,
            DbError = error,
            ErrorReported = false,
            ErrorText = string.Empty,
            UpdateTable = updateTable ?? string.Empty,
            UpdateInvoked = true,
            UpdateResult = updateResult,
            UpdateResultObservedByHook = observedByHook,
            DefensiveOverrideApplied = overrideApplied,
        };

    /// <summary>
    /// <c>return RetCode.OK</c> - the update answered the DataWindow contract's success value and
    /// neither cancellation check fired [<c>:L214</c>, <c>:L248</c>].
    /// </summary>
    /// <param name="identity">The identity blocks and the counts triple.</param>
    /// <param name="updateTable">The described update table.</param>
    /// <param name="observedByHook">
    /// The result the after-update hook was handed, which on this arm equals the reconciled result
    /// because the override cannot have fired.
    /// </param>
    /// <returns>The outcome for that arm.</returns>
    /// <remarks>
    /// THE RECONCILED RESULT IS PINNED TO THE DATAWINDOW SUCCESS VALUE, not to <c>RetCode.OK</c>. The two
    /// numbers differ - <c>1</c> against <c>0</c> - and keeping them in separate members is what stops
    /// the two code spaces bleeding into each other.
    /// </remarks>
    internal static UpdateOutcome Succeeded(
        IdentityResolutionOutcome identity,
        string? updateTable = null,
        long observedByHook = DataWindowBufferStore.DataStoreSuccess)
    {
        ArgumentNullException.ThrowIfNull(identity);

        return new UpdateOutcome
        {
            Kind = UpdateOutcomeKind.Succeeded,
            Code = RetCode.OK,
            Identity = identity,
            UpdateTable = updateTable ?? string.Empty,
            UpdateInvoked = true,
            UpdateResult = DataWindowBufferStore.DataStoreSuccess,
            UpdateResultObservedByHook = observedByHook,
            DefensiveOverrideApplied = false,
        };
    }

    /// <summary>
    /// Projects <see cref="DbError"/> onto the published wire message, through the ONLY sanctioned
    /// conversion.
    /// </summary>
    /// <param name="dbError">The projected message when one exists.</param>
    /// <returns>
    /// <see langword="true"/> when this outcome carries a payload; <see langword="false"/> when it does
    /// not, in which case <paramref name="dbError"/> is <see langword="null"/>.
    /// </returns>
    /// <remarks>
    /// <b>THE MAPPING IS NEVER HAND-ROLLED (C-F).</b> It goes through
    /// <c>DbErrorDataExtensions.ToDbError</c>, which applies the redactor unconditionally and copies the
    /// other four members through untouched. A hand-written five-field assignment would satisfy the
    /// compiler perfectly and reintroduce exactly the statement leak the split exists to prevent.
    /// </remarks>
    internal bool TryProjectDbError(out DbError? dbError)
    {
        // AN EMPTY PAYLOAD IS NO PAYLOAD, AND TREATING IT AS ONE HID REAL DIAGNOSTICS. The classifier's
        // last arm builds its payload from the TRANSACTION's own code and text
        // [n_cst_thread_task_sqlupdate.sru:L249-L250], and those are 0 and empty whenever the failure was
        // raised through the CARRIER's database-error channel instead - which is exactly what a constraint
        // violation on a generated INSERT does. The payload was then non-null and entirely default, and
        // because a non-null payload wins over the run's latched one at the projection, a fully populated
        // driver error (code, text, buffer, row) was shadowed by an empty one and the response carried
        // `db_error: {}`. The presence of the payload IS the signal a consumer reads, so a default-valued
        // payload must not be published as one; answering false here lets the caller fall back to what the
        // run actually latched.
        if (DbError is null || DbError.Value == DbErrorData.Empty)
        {
            dbError = null;

            return false;
        }

        DbErrorData payload = DbError.Value;

        dbError = payload.ToDbError();

        return true;
    }
}

#endregion


#region The projection - the one sanctioned way a conflict becomes a gRPC status

/// <summary>
/// A conflict expressed as the gRPC status plus the trailing metadata that carries its detail.
/// </summary>
/// <param name="Status">
/// <c>StatusCode.Aborted</c> with a detail message that names the trailer and quotes no data.
/// </param>
/// <param name="Trailers">
/// One entry: the binary trailer whose key the published contract fixes, holding a serialized
/// <c>common.v1.RichErrorTrailer</c>.
/// </param>
/// <remarks>
/// <para>
/// WHY A STATUS PLUS TRAILERS AND NOT AN EXCEPTION. This file classifies and never throws - see the
/// division of labour in the file header - so the projection hands back the two values a throw site
/// needs and lets that site decide how to raise them. A gRPC server method raises them as
/// <c>new RpcException(projection.Status, projection.Trailers)</c>; an interceptor or the central mapper
/// can equally attach them to a context.
/// </para>
/// <para>
/// WHY THE DETAIL TRAVELS IN A TRAILER AT ALL. A gRPC status carries only a code and a message, neither
/// of which can hold a <c>ConflictDetail</c>. The published contract therefore defines exactly ONE
/// mechanism - a versioned binary trailer - and binds it to the RPC through a custom method option, so a
/// client discovers the key, the status code and the payload type from the descriptor rather than from
/// prose. <c>google.rpc.Status</c> with <c>Any</c> would be the idiomatic alternative and was
/// deliberately rejected: it needs a package the dependency inventory does not list.
/// </para>
/// </remarks>
internal readonly record struct RichErrorProjection(Status Status, Metadata Trailers);

#endregion

#region The classifier

/// <summary>
/// Classifies one update attempt into a typed outcome, reproducing <c>_of_update</c> arm for arm and
/// adding the mandated optimistic-concurrency narrowing.
/// </summary>
/// <remarks>
/// <para>
/// STATELESS AND THREAD-SAFE. The only instance state is the injected redactor, which is contracted to
/// be pure and is registered as a singleton, so one detector serves concurrent requests and successive
/// tables of one multi-table update without carrying anything between calls.
/// </para>
/// <para>
/// See the file header for the four things a straightforward port gets wrong, the five locators of the
/// three SQL-code conditions and two veto arms, and why the conflict response is an addition rather than
/// a port.
/// </para>
/// </remarks>
internal sealed class ConflictDetector
{
    // ----------------------------------------------------------------------------------------------
    //  THE VOCABULARY - every literal the oracle compares against, named exactly once
    //  --------------------------------------------------------------------------------------------
    //  PascalCase throughout. This folder is outside the repository .editorconfig's scoped naming
    //  suppressions, so a SCREAMING_SNAKE identifier declared here would fail the build under
    //  TreatWarningsAsErrors - and none is needed, because the preserved legacy spellings are consumed
    //  from PowerFramework.Shared.Kernel.RetCode and from the generated contract enums.
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// PowerBuilder's ERROR marker, one of the three answers <c>Describe</c> gives for an update table
    /// that cannot be used [<c>:L189</c>].
    /// </summary>
    internal const string DescribeErrorMarker = "!";

    /// <summary>
    /// PowerBuilder's UNKNOWN marker, the third such answer [<c>:L189</c>].
    /// </summary>
    internal const string DescribeUnknownMarker = "?";

    /// <summary>
    /// The <c>acceptText</c> argument the oracle passes to the update [<c>:L204</c>].
    /// </summary>
    internal const bool UpdateAcceptsText = true;

    /// <summary>
    /// The <c>resetFlag</c> argument the oracle passes to the update [<c>:L204</c>]. FALSE, so THE CALLER
    /// OWNS THE CARRIER'S STATE AFTERWARDS and nothing here resets it.
    /// </summary>
    internal const bool UpdateResetsFlags = false;

    /// <summary>
    /// The SQL code the defensive override tests for, and the value it rewrites a claimed success to
    /// [<c>:L208-L209</c>].
    /// </summary>
    /// <remarks>
    /// EXACTLY <c>-1</c>, NOT "NEGATIVE" AND NOT "NON-ZERO". The transaction object's own override tests
    /// non-zero [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:L275</c>] and its failure
    /// predicate tests negative [<c>:L337</c>]; all three conditions are genuine and none may be unified
    /// with another. It shares its numeric value with <c>RetCode.FAILED</c> and with the DataWindow
    /// failure value by coincidence, not by meaning: this constant lives in the DRIVER's code space.
    /// </remarks>
    internal const long DefensiveOverrideSqlCode = -1L;

    /// <summary>
    /// The status detail accompanying an aborted update. Names the mechanism and QUOTES NO DATA.
    /// </summary>
    /// <remarks>
    /// No table name, no column name, no row value and no statement text appears here. A status detail is
    /// the least controlled string in a gRPC response - it reaches logs, proxies and client-side error
    /// messages - so the payload stays in the trailer where the redaction and presence rules apply
    /// (C-F).
    /// </remarks>
    internal const string AbortedStatusDetail =
        "The update was aborted by an optimistic-concurrency mismatch. The conflicting rows travel as "
        + "common.v1.ConflictDetail in the binary trailer named by the Update method's rich-error "
        + "option; re-read the affected rows and retry, or surface the conflict. No row was overwritten.";

    /// <summary>
    /// The RPC whose descriptor carries the rich-error binding this file projects onto: C-06's
    /// <c>persistence.v1.UpdateService.Update</c>.
    /// </summary>
    internal const string UpdateMethodName = "Update";

    /// <summary>
    /// The rich-error binding, READ FROM THE GENERATED DESCRIPTOR rather than restated as literals.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>WHY THE DESCRIPTOR AND NOT A CONSTANT (C-K).</b> The published contract declares the trailer
    /// key, the status code and the payload type as a custom method option on the <c>Update</c> RPC
    /// precisely so a consumer can discover all three without reading a comment. Reading them back here
    /// means the protocol definition remains the single source: change the key there and this file
    /// follows, whereas a literal copy could drift silently and a client keyed on the descriptor would
    /// then look under a key nothing writes to.
    /// </para>
    /// <para>
    /// <b>LAZY, SO A STRUCTURAL FAULT SURFACES AS ITSELF.</b> Resolution validates the binding and throws
    /// if it is missing or inconsistent - a fail-fast posture rather than a silent fallback, matching the
    /// framework's own [<c>ws_objects/pfw.pbl.src/pfw.sra:L111-L144</c>]. A static field initializer
    /// would have wrapped that in a <c>TypeInitializationException</c> and hidden the message;
    /// <see cref="Lazy{T}"/> caches and rethrows the original exception on every access.
    /// </para>
    /// </remarks>
    private static readonly Lazy<RichErrorBinding> UpdateRichErrorBindingResolver =
        new(ResolveUpdateRichErrorBinding, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// The redactor every statement passes through on its way into a payload or a log line.
    /// </summary>
    private readonly ISqlRedactor _redactor;

    /// <summary>
    /// Creates a detector.
    /// </summary>
    /// <param name="redactor">
    /// The statement redactor. Injected so composition can register one instance for the process and a
    /// test can drive the single redaction path - including with a pass-through implementation, which
    /// must still route through the same path.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="redactor"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// THE INJECTED REDACTOR CANNOT WEAKEN THE WIRE PATH, AND THAT IS DELIBERATE. It masks the statement
    /// on its way INTO a <see cref="DbErrorData"/> and would mask one on its way to a log; the projection
    /// onto the published <c>DbError</c> applies <c>SqlRedactor.Instance</c> itself and takes no redactor
    /// argument at all, so no registration and no argument can put an unmasked statement on the network.
    /// </remarks>
    internal ConflictDetector(ISqlRedactor redactor)
    {
        ArgumentNullException.ThrowIfNull(redactor);

        _redactor = redactor;
    }

    /// <summary>
    /// The metadata key the conflict detail is sent under, as the <c>Update</c> RPC's descriptor declares
    /// it.
    /// </summary>
    /// <value>
    /// A key ending in <c>-bin</c>. The suffix is not decoration: gRPC transports a <c>-bin</c> key as
    /// binary and base64-encodes it on the wire, while a key without it may carry ASCII only - so a
    /// serialized protobuf payload under a non-<c>-bin</c> key would be corrupted in transit.
    /// </value>
    /// <exception cref="InvalidOperationException">
    /// The generated descriptor carries no usable rich-error binding.
    /// </exception>
    internal static string RichErrorTrailerKey => UpdateRichErrorBindingResolver.Value.TrailerKey;

    /// <summary>
    /// The gRPC status code the trailer accompanies, as the descriptor declares it.
    /// </summary>
    /// <value>
    /// <c>10</c>, the canonical numeric code for <c>Aborted</c> - which is the canonical gRPC mapping to
    /// HTTP 409 and the code Gateway's REST projection surfaces as 409.
    /// </value>
    /// <exception cref="InvalidOperationException">
    /// The generated descriptor carries no usable rich-error binding.
    /// </exception>
    internal static StatusCode RichErrorStatusCode =>
        (StatusCode)UpdateRichErrorBindingResolver.Value.GrpcStatusCode;


    // ----------------------------------------------------------------------------------------------
    //  THE CLASSIFICATION - _of_update, step by step, in the oracle's own order
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Classifies one update attempt, reproducing
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru:L172-L252</c> arm for arm.
    /// </summary>
    /// <param name="attempt">The inputs for this table's attempt.</param>
    /// <returns>
    /// The typed outcome. Never <see langword="null"/>, and its <c>Kind</c> is never
    /// <see cref="UpdateOutcomeKind.None"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="attempt"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// <b>THIS METHOD THROWS NO <c>RpcException</c> AND NEVER WILL.</b> A conflict is an ordinary,
    /// expected outcome with a defined response, so it is returned as data; the throw site is
    /// <c>Grpc/UpdateService.cs</c>, which calls <see cref="TryProjectAborted"/>. Fail-fast applies to
    /// STRUCTURAL faults - a null argument, an unusable descriptor - and NOT to a data-level conflict.
    /// </para>
    /// <para>
    /// <b>IT IS SIDE-EFFECT-FREE WITH RESPECT TO ACCUMULATED STATE</b>, so calling it twice in sequence
    /// produces two independent outcomes. That is required by the multi-table shape, where it runs once
    /// per table and the loop stops at the first failure [<c>:L356-L369</c>]; the caller-side proxy is
    /// what accumulates the counts with <c>+=</c> and appends one identity record per firing
    /// [<c>n_cst_threading_task_sqlupdate.sru:L66-L77</c>].
    /// </para>
    /// <para>
    /// The two calls it DOES make that change something are the oracle's own:
    /// <see cref="IUpdateTarget.ClearState"/> [<c>:L186</c>] and <see cref="IUpdateTarget.Update"/>
    /// [<c>:L204</c>]. Nothing here resets the carrier, because the update is invoked with the reset flag
    /// clear. The third call, <see cref="IUpdateTarget.CaptureConcurrencyEvidence"/>, is a pure read.
    /// </para>
    /// <para>
    /// 🔴 <b>THE ONE ORDERING THAT IS NOT THE ORACLE'S, AND WHY IT SITS WHERE IT DOES.</b> The
    /// affected-row measurement is read from the target immediately after the update returns - steps 9a
    /// and 9b below - and a shortfall is classified as a conflict BEFORE the success arm at
    /// [<c>:L214</c>] can be taken. It has to be in that order: the DataWindow update contract answers
    /// success for a statement that matched zero rows, so a mismatch reaches this method wearing a
    /// claimed success, and classifying after the success arm would publish the identity round trip,
    /// publish the counts, and let the caller's epilogue commit. The narrowing itself is an ADDITION the
    /// oracle cannot reach at all - it measures nothing - and it changes no legacy arm: the returned code
    /// is still <c>RetCode.E_DB_ERROR</c>, so the epilogue still rolls back (AAP 0.6.3.8).
    /// </para>
    /// </remarks>
    internal UpdateOutcome Classify(UpdateAttempt attempt)
    {
        ArgumentNullException.ThrowIfNull(attempt);

        // STEP 1 [:L180] - `if IsFailed(of_GetTransObject(ref transObject)) then return
        //                   RetCode.E_INVALID_TRANSACTION`
        //
        // FIRST, AND BEFORE THE CANCELLATION CHECK. An attempt that is both cancelled and
        // transaction-less reports the transaction fault, because that is the arm the oracle reaches
        // first. Nothing is raised on either channel and nothing is cleared - this returns before
        // [:L186].
        IUpdateTransaction? transaction = attempt.Transaction;

        if (transaction is null)
        {
            return UpdateOutcome.InvalidTransaction();
        }

        // STEP 2 [:L182] - `if of_IsCancelled() then return RetCode.CANCELLED`
        //
        // THE FIRST OF TWO CANCELLATION CHECKS. Polled, never thrown through: the oracle RETURNS a code
        // here and the caller's epilogue keys its raise-suppression on that code [:L396].
        if (attempt.Cancellation.IsCancellationRequested)
        {
            return UpdateOutcome.Cancelled();
        }

        // STEP 3 [:L186] - `Data.of_ClearState()`
        //
        // BEFORE ANYTHING ELSE THE FUNCTION DOES, and in particular before the update table is described.
        // The oracle's [:L184] assignment of the caller-side proxy has no analogue to reproduce: the
        // proxy's two callbacks are consumed through the outcome rather than through a captured handle.
        attempt.Target.ClearState();

        // 🔴 AND THE TRANSACTION'S STATE IS CLEARED TOO, WHICH IT WAS NOT.
        //
        // The oracle's `of_ClearState()` resets the five-value SQL state the rest of this function then
        // reads - SQLCode, SQLDBCode, SQLNRows, SQLErrText, SQLReturnData - and this port cleared the
        // DataWindow target only. The consequence was not a stale read: it was that the transaction's
        // state was never written by ANYTHING in the service, so `transaction.SqlDbCode` and
        // `transaction.SqlErrText` answered their cleared values on every attempt, and the failure payload
        // built from them at the else arm below was ALWAYS EMPTY. A caller whose row was refused by the
        // storage engine therefore received a database error carrying no code and no text - the driver's
        // own diagnosis reached the wire only by the separate route of the latched error event.
        //
        // CLEARING HERE IS WHAT MAKES STAMPING SAFE. A pooled transaction outlives one update, so without
        // a per-attempt clear a stamp from an earlier attempt would be read as this attempt's evidence.
        //
        // IT DOES NOT REVIVE THE DEFENSIVE OVERRIDE BY THE BACK DOOR. That override fires only when the
        // SQL code is negative AND the DataWindow claimed success [:L208-L210], and the carrier stamps
        // only on the path where it returns the DataWindow FAILURE value - so the conjunction stays
        // unreachable and no previously-successful update changes its answer.
        transaction.ClearState();

        // STEP 4 [:L188-L193] - the THREE sentinels, with BOTH raises firing in order
        //
        // `sUpdateTable = Data.Describe("DataWindow.Table.UpdateTable")` [:L188] - read at RUN TIME, from
        // the value the modification script wrote [:L143], and NOT from a cached descriptor. This is the
        // value Concurrency/UpdateWhereBuilder.cs put there.
        string updateTable = attempt.Identity.Metadata.DescribeUpdateTable();

        if (IsSentinelUpdateTable(updateTable))
        {
            // `Event OnDBError(-1,"没有可更新的表","",Primary!,0)` [:L190]. The five positional values come
            // from the sibling factory so the code, the diagnostic, the empty statement, the Primary
            // buffer and row 0 have exactly ONE spelling in this service. THE DIAGNOSTIC IS NOT RETYPED
            // HERE: DbErrorMessages.NoUpdatableTable is the single declaration of it, and a locally typed
            // literal would be a second one free to drift.
            //
            // ROW 0 IS A DELIBERATE LITERAL AND NOT AN INDEX (R9). Every DataWindow row ordinal in this
            // system is one-based, so 0 cannot name a row; the oracle passes it, together with Primary!,
            // to mean "not attributable to any one row".
            DbErrorData sentinelError = Redact(DbErrorData.NoUpdatableTable());

            attempt.Errors.OnDbError(sentinelError);

            // `Event OnError(RetCode.E_DB_ERROR,"没有可更新的表")` [:L191] - THE SAME MESSAGE as the payload
            // above. Contrast the veto arm below, which raises an EMPTY message; the asymmetry is legacy
            // behaviour and is reproduced rather than harmonized (C-B).
            attempt.Errors.OnError(RetCode.E_DB_ERROR, DbErrorMessages.NoUpdatableTable);

            // `return RetCode.E_DB_ERROR` [:L192]. NOT eligible for the conflict narrowing: no statement
            // was generated, so there is nothing a concurrency mismatch could have been measured on.
            return UpdateOutcome.DatabaseError(
                sentinelError,
                errorReported: true,
                errorText: DbErrorMessages.NoUpdatableTable,
                updateTable: updateTable);
        }

        // STEP 5 [:L195-L202] - the VETOABLE before-update hook, and the two-way discrimination
        //
        // `if IsPrevented(TransObject.Event OnBeforeUpdate(Data)) then` [:L195]. Predicates.IsPrevented is
        // used rather than a hand-rolled comparison because its semantics are a published contract: it is
        // an equality test against RetCode.PREVENT and it returns false for null, so a hook that answers
        // null is NOT a veto.
        long beforeUpdate = transaction.OnBeforeUpdate();

        if (Predicates.IsPrevented(beforeUpdate))
        {
            // `if TransObject.of_IsFailed() then` [:L196] - THE DISCRIMINATOR IS THE TRANSACTION'S OWN
            // FAILURE PREDICATE, which is SQLCode < 0 [n_cst_thread_trans.sru:L337]. The transaction
            // object's own veto arm tests NON-ZERO instead [n_cst_thread_trans.sru:L267], so the two arms
            // genuinely disagree for a positive code and must not be unified.
            if (transaction.IsFailed())
            {
                // `Event OnDBError(TransObject.SQLDBCode,TransObject.SQLErrText,"",Primary!,0)` [:L197] -
                // the transaction's OWN code and text, an empty statement, Primary! and row 0.
                DbErrorData vetoError =
                    Redact(DbErrorData.FromTransaction(transaction.SqlDbCode, transaction.SqlErrText));

                attempt.Errors.OnDbError(vetoError);

                // `Event OnError(RetCode.E_DB_ERROR,"")` [:L198] - AN EMPTY MESSAGE, deliberately. The
                // sentinel arm above passes its diagnostic on this channel; this arm passes nothing, so
                // the text a consumer sees differs between two arms that return the same code. That is
                // the oracle's behaviour and "improving" it would be a behavioural change (C-B).
                attempt.Errors.OnError(RetCode.E_DB_ERROR, string.Empty);

                // `return RetCode.E_DB_ERROR` [:L199]. Not eligible for the narrowing: the update never
                // ran.
                return UpdateOutcome.DatabaseError(
                    vetoError,
                    errorReported: true,
                    errorText: string.Empty,
                    updateTable: updateTable);
            }

            // `return RetCode.CANCELLED` [:L201] - A CLEAN VETO IS A CANCELLATION, NOT A FAILURE, and it
            // raises NOTHING on either channel. Recall CANCELLED is neither succeeded nor failed in the
            // legacy algebra, so it cannot be folded into either neighbour.
            return UpdateOutcome.Cancelled(updateTable: updateTable);
        }

        // STEP 6 [:L204] - `rtCode = Data.Update(true,false)`
        //
        // ACCEPT-TEXT TRUE, RESET-FLAG FALSE. The consequence of the second argument is a contract rather
        // than a detail: THE CALLER OWNS THE CARRIER'S STATE AFTERWARDS. Item statuses and the
        // original-value shadow survive the call, which is what makes the identity round trip, a retry and
        // the conflict report below possible at all - and it is why nothing in this method resets the
        // carrier.
        // THE ATTEMPT'S TOKEN IS HANDED TO THE UPDATE, so a changeset carrying many rows stops at the next
        // row after the caller goes rather than running the whole walk out. It is the SAME token the two
        // poll points above read, so a cancellation is seen consistently wherever this method looks for it,
        // and an abandoned walk answers the DataWindow failure sentinel - which the epilogue turns into the
        // rollback that discards the rows already applied.
        long updateResult = attempt.Target.Update(
            UpdateAcceptsText,
            UpdateResetsFlags,
            attempt.Cancellation);

        // STEP 7 [:L206] - `TransObject.Event OnAfterUpdate(Data,rtCode)`
        //
        // FIRES WITH THE UNRECONCILED VALUE, AND BEFORE THE OVERRIDE BELOW. The ordering is contract: a
        // hook must observe what the update itself answered, not what the reconciliation turned it into.
        // Its return value is discarded because the oracle declares it as a plain event that cannot veto.
        long observedByHook = updateResult;

        transaction.OnAfterUpdate(observedByHook);

        // STEP 8 [:L208-L210] - THE DEFENSIVE OVERRIDE
        //
        //     if TransObject.SQLCode = -1 and rtCode = 1 then rtCode = -1
        //
        // AN IMPLEMENTATION THAT TRUSTS THE UPDATE CALL'S OWN RETURN VALUE WILL REPORT SUCCESS ON A FAILED
        // UPDATE: the driver's code overrides the operation's self-report. BOTH CONJUNCTS MATTER. The test
        // is EXACTLY -1 - not "negative" and not "non-zero" - so a code of -2 or of 1 leaves a claimed
        // success standing here, even though the transaction object's own override would rewrite it for a
        // non-zero code [n_cst_thread_trans.sru:L275]. And it fires only on a CLAIMED SUCCESS, so a
        // genuine failure is left exactly as the update reported it.
        bool overrideApplied =
            transaction.SqlCode == DefensiveOverrideSqlCode
            && updateResult == DataWindowBufferStore.DataStoreSuccess;

        if (overrideApplied)
        {
            updateResult = DataWindowBufferStore.DataStoreFailure;
        }

        // STEP 9 [:L212] - `if of_IsCancelled() then return RetCode.CANCELLED`
        //
        // THE SECOND CANCELLATION CHECK, AFTER THE UPDATE HAS ALREADY BEEN APPLIED. A cancellation
        // observed only here still yields CANCELLED, and the success path is NOT taken - so the identity
        // round trip and the counts report do not fire even for rows that were written. The caller's
        // epilogue rolls back [:L395] and suppresses the raise for this code [:L396].
        if (attempt.Cancellation.IsCancellationRequested)
        {
            return UpdateOutcome.Cancelled(
                updateInvoked: true,
                updateResult: updateResult,
                observedByHook: observedByHook,
                overrideApplied: overrideApplied,
                updateTable: updateTable);
        }

        // ============ STEP 9a: THE MEASUREMENT, READ HERE AND NOWHERE ELSE ========================
        // 🔴 AFTER THE STATEMENTS HAVE RUN, ON THE SAME TRANSACTION, AND BEFORE ANY SUCCESS ARM.
        //
        // The evidence describes what THIS attempt's statements affected, so it cannot be read before
        // STEP 6 - there would be nothing to read - and it must not be read after a success has been
        // published, because by then the caller's epilogue has already committed. This single line is
        // the ordering the whole optimistic-concurrency contract rests on.
        //
        // THE ORACLE MEASURES NOTHING ANYWHERE IN ITS UPDATE PATH, so a null answer is its own state and
        // the arms below then behave exactly as it does.
        ConcurrencyEvidence? evidence = attempt.Target.CaptureConcurrencyEvidence();

        // ============ STEP 9b: A SHORTFALL IS A CONFLICT *BEFORE* IT CAN BE A SUCCESS =============
        // 🔴 TESTED AHEAD OF STEP 10, WHICH IS THE POINT. `Data.Update(true,false)` answers 1 whenever
        // every generated statement executed without a DBMS ERROR, and a statement that matched ZERO
        // rows is not a DBMS error on any provider - the driver returns a count of zero and raises
        // nothing. So under `updatewhere=1` the classic optimistic miss arrives here as a CLAIMED
        // SUCCESS, and a classifier that reached STEP 10 first would report it as one, publish the
        // identity round trip and the counts, and let the epilogue COMMIT. The row would not have been
        // overwritten - the predicate carried the originals, so nothing matched - but the caller would
        // be told its edit applied when it did not, which is the same class of harm.
        //
        // IT MUST NOT SWALLOW A GENUINE DATABASE ERROR, and it cannot: IsConcurrencyMismatch requires
        // positive evidence, declines whenever the provider itself faulted, and declines an empty row
        // list. A constraint violation, a connection fault and a syntax error therefore stay database
        // errors carrying the legacy's own code and text on the else arm below.
        //
        // THE RETURN CODE IS THE LEGACY'S OWN, RetCode.E_DB_ERROR - so RequiresRollback is true, the
        // epilogue rolls back rather than commits, and no legacy arm changes. Only the outcome's KIND
        // narrows and a payload is added (AAP 0.6.3.8).
        // ============ STEP 9c: A CONTRADICTORY PAYLOAD IS THE CALLER'S FAULT, TESTED FIRST ========
        // 🔴 AHEAD OF THE CONFLICT TEST, WHICH IS THE POINT. A row the payload flagged modified while
        // supplying no updatable column value generates no statement at all - so it can neither have
        // been overwritten nor have lost a race, and classifying it as a concurrency mismatch told the
        // caller another writer had changed a row that nothing had touched. It is answered as the
        // payload fault it is, with the oracle's own invalid-update-data code, and the general error
        // channel is raised because nothing in the row counts records the condition.
        if (evidence is { RowsWithoutAssignableValues: > 0L })
        {
            UpdateOutcome invalid = UpdateOutcome.InvalidUpdateData(
                evidence.RowsWithoutAssignableValues,
                updateTable,
                updateResult,
                observedByHook,
                overrideApplied);

            attempt.Errors.OnError(invalid.Code, invalid.ErrorText);

            return invalid;
        }

        if (IsConcurrencyMismatch(evidence))
        {
            DbErrorData mismatchError =
                Redact(DbErrorData.FromTransaction(transaction.SqlDbCode, transaction.SqlErrText));

            return UpdateOutcome.ConflictDetected(
                BuildConflictDetail(evidence, updateTable),
                mismatchError,
                updateTable,
                updateResult,
                observedByHook,
                overrideApplied);
        }

        // STEP 10 [:L214] - `if rtCode = 1 then`
        //
        // SUCCESS IS THE LITERAL VALUE 1, IN THE DATAWINDOW CODE SPACE. It is NOT RetCode.OK, which is 0,
        // and 0 is therefore a FAILURE here. Predicates.IsSucceeded must never be applied to this value:
        // it tests >= 0 and would classify 0 and 2 as successes.
        if (updateResult == DataWindowBufferStore.DataStoreSuccess)
        {
            // STEP 11 [:L215-L248] - the identity round trip and the counts report, DELEGATED
            //
            // The sibling resolver owns all three conditional levels - rows must have been inserted
            // [:L215], an identity column must have been discovered [:L226], and at least one of the two
            // collected arrays must be non-empty [:L242] - as well as the FORWARD primary walk [:L228-233]
            // and the BACKWARD filter walk [:L237], which runs backwards because the filter buffer's row
            // order is inverted relative to the source [:L235]. None of that is reimplemented here.
            //
            // The counts triple fires on EVERY success, including a pure update and a pure delete, because
            // the oracle's counts callback sits OUTSIDE the inserted-count block [:L247]. The resolver
            // reproduces that too.
            IdentityResolutionOutcome identity =
                IdentityColumnResolver.Resolve(attempt.Identity.Metadata, attempt.Identity.Values);

            // `return RetCode.OK` [:L248].
            return UpdateOutcome.Succeeded(identity, updateTable, observedByHook);
        }

        // THE ELSE ARM [:L249-L250] - `else return RetCode.E_DB_ERROR`
        //
        // THE ORACLE RAISES NOTHING HERE. It returns the code and lets the caller's epilogue decide
        // whether to report it, which the epilogue does by comparing against the last reported code
        // [:L397]. So ErrorReported is false on both branches below, and the payload each carries is DATA
        // for the epilogue and for the wire response rather than something that was raised.
        //
        // The payload carries the transaction's OWN code and text, which is what the wire contract's
        // OperationStatus.db_error field is for. Building it changes nothing observable - no channel is
        // called - and it is the only place those two values are still in scope.
        DbErrorData failureError =
            Redact(DbErrorData.FromTransaction(transaction.SqlDbCode, transaction.SqlErrText));

        // ==========================================================================================
        //  🔴 A CALLER-CONTROLLED CONSTRAINT REFUSAL IS SPLIT OFF BEFORE THE DATABASE-ERROR ARM.
        //
        //  A row omitting a NOT NULL column, or duplicating a key, is the CALLER's payload being wrong -
        //  and it used to arrive on the arm below as RetCode.E_DB_ERROR, which Gateway correctly
        //  publishes as HTTP 502. So a caller who forgot a required field was told the database had
        //  failed and that the fault lay behind the gateway; nothing in the answer said which column, and
        //  nothing said the caller could fix it. That is the finding.
        //
        //  IT IS TESTED HERE AND NOT EARLIER, because the discriminator is the DRIVER's own result code,
        //  which only exists once the statement has been attempted. Nothing about the ordering changes:
        //  the conflict narrowing above still runs first, so a shortfall is still a conflict, and this
        //  test only ever reclassifies what would otherwise have been an unspecific database error.
        //
        //  THE PAYLOAD IS THE ALREADY-REDACTED ONE, deliberately - the same object the arm below carries.
        //  Its message is what names the offending column, and the redactor's provider-envelope rule is
        //  what lets that identity survive while a value quoted inside the message is still masked.
        // ==========================================================================================
        if (IsCallerConstraintViolation(transaction.SqlDbCode))
        {
            return UpdateOutcome.ConstraintViolation(
                failureError,
                updateTable: updateTable,
                updateResult: updateResult,
                observedByHook: observedByHook,
                overrideApplied: overrideApplied);
        }

        // ⚠️ THE NARROWING IS NOT REPEATED HERE, AND THAT IS DELIBERATE. STEP 9b above already tested
        // the measurement, on BOTH the claimed-success and the reported-failure path, because it runs
        // before this branch splits. A second test here would be dead code that implied the arm above
        // covered only successes - and a reader who added one would be tempted to weaken the first.
        //
        // So a DataWindow-reported failure whose evidence shows a shortfall has ALREADY returned a
        // conflict; anything reaching this line is a genuine database error carrying the transaction's
        // own code and text.
        return UpdateOutcome.DatabaseError(
            failureError,
            errorReported: false,
            errorText: string.Empty,
            updateTable: updateTable,
            updateInvoked: true,
            updateResult: updateResult,
            observedByHook: observedByHook,
            overrideApplied: overrideApplied);
    }


    // ----------------------------------------------------------------------------------------------
    //  THE PREDICATES AND THE PAYLOAD BUILDERS
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Whether a driver result code names a constraint the CALLER's payload controls.
    /// </summary>
    /// <param name="sqlDbCode">
    /// The driver's own result code as the transaction reports it, already mapped through
    /// <c>SqliteConnectionFactory.MapSqliteResultCode</c> so an extended code wins where the provider
    /// supplied one.
    /// </param>
    /// <returns><see langword="true"/> for a constraint a corrected payload can satisfy.</returns>
    /// <remarks>
    /// <para>
    /// <b>THE SET IS DELIBERATELY NARROW, AND EVERY MEMBERSHIP DECISION IS A JUDGEMENT ABOUT WHO CAN FIX
    /// IT.</b> The question this predicate answers is not "was a constraint involved" - it is "can the
    /// caller correct this by sending different values". Only where the answer is yes does the outcome
    /// become a 400, because telling a caller to fix something they cannot fix is worse than telling them
    /// the server failed.
    /// </para>
    /// <para>IN, because a corrected payload satisfies each one:</para>
    /// <list type="bullet">
    /// <item><description>
    /// <see cref="RetCode.SQLITE_CONSTRAINT_NOTNULL"/> - a required column was null or absent. This is
    /// the reported case: <c>NAME TEXT NOT NULL</c> and <c>AGE INT NOT NULL</c> are the only DDL in the
    /// repository [<c>ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L463-L469</c>], and the DataWindow
    /// definition declares no required flag on any column, so this refusal is the ONLY place in the
    /// system that knows the column is required.
    /// </description></item>
    /// <item><description>
    /// <see cref="RetCode.SQLITE_CONSTRAINT_UNIQUE"/> and
    /// <see cref="RetCode.SQLITE_CONSTRAINT_PRIMARYKEY"/> - a duplicate value. The caller chose it.
    /// </description></item>
    /// <item><description>
    /// <see cref="RetCode.SQLITE_CONSTRAINT_FOREIGNKEY"/> - a reference to a row that does not exist. The
    /// caller chose the reference. No evidenced schema declares one, so this member is here for
    /// consistency of the rule rather than for a path the fixture reaches.
    /// </description></item>
    /// <item><description>
    /// <see cref="RetCode.SQLITE_MISMATCH"/> - a value whose type the target column cannot accept. The
    /// caller sent the value. Note this is reachable only for a rowid-typed column, because SQLite's type
    /// affinity converts rather than refuses everywhere else - which is precisely why the type check on
    /// the DataServices write path exists rather than being left to the engine.
    /// </description></item>
    /// </list>
    /// <para>OUT, because no payload the caller can send would satisfy them:</para>
    /// <list type="bullet">
    /// <item><description>
    /// <see cref="RetCode.SQLITE_CONSTRAINT_CHECK"/> and <see cref="RetCode.SQLITE_CONSTRAINT_TRIGGER"/> -
    /// the SCHEMA's own logic rejected the row. A caller cannot read the predicate and cannot know what
    /// would satisfy it, so this stays a 502 and sends an operator to look at the schema.
    /// </description></item>
    /// <item><description>
    /// <see cref="RetCode.SQLITE_CONSTRAINT_COMMITHOOK"/>, <see cref="RetCode.SQLITE_CONSTRAINT_FUNCTION"/>
    /// and <see cref="RetCode.SQLITE_CONSTRAINT_VTAB"/> - server-side extension points failing. Nothing
    /// about the caller's values is implicated.
    /// </description></item>
    /// <item><description>
    /// The BARE <see cref="RetCode.SQLITE_CONSTRAINT"/> with no refinement. A provider that reports only
    /// the base code has not said WHICH constraint failed, so classifying it as a caller fault would be a
    /// guess - and AAP 0.1.5 requires narrowing with a defined error rather than widening with one. It
    /// keeps the unspecific 502 it always had.
    /// </description></item>
    /// </list>
    /// </remarks>
    internal static bool IsCallerConstraintViolation(long sqlDbCode) => sqlDbCode
        is RetCode.SQLITE_CONSTRAINT_NOTNULL
        or RetCode.SQLITE_CONSTRAINT_UNIQUE
        or RetCode.SQLITE_CONSTRAINT_PRIMARYKEY
        or RetCode.SQLITE_CONSTRAINT_FOREIGNKEY
        or RetCode.SQLITE_MISMATCH;

    /// <summary>
    /// Whether the described update table is one of the THREE answers the oracle treats as "no updatable
    /// table" [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru:L189</c>].
    /// </summary>
    /// <param name="updateTable">The raw describe answer.</param>
    /// <returns><see langword="true"/> for the empty string, <c>"!"</c> or <c>"?"</c>.</returns>
    /// <remarks>
    /// <para>
    /// <b>THREE SENTINELS, NOT ONE.</b> The oracle is
    /// <c>if sUpdateTable = "" or sUpdateTable = "!" or sUpdateTable = "?" then</c>. The latter two are
    /// PowerBuilder's own error and unknown markers, which <c>Describe</c> answers when a property cannot
    /// be read - so a carrier with no update table and a carrier whose describe FAILED both land here, and
    /// checking only the empty string would let a failed describe proceed to generate statements against a
    /// table named <c>"!"</c>.
    /// </para>
    /// <para>
    /// <b>THE COMPARISON IS EXACT AND ORDINAL, AND IS DELIBERATELY NOT TRIMMED.</b> PowerScript's
    /// <c>=</c> on strings is an exact comparison, so a value of <c>" "</c> or <c>"!x"</c> is NOT a
    /// sentinel to the oracle and must not become one here. <see langword="null"/> is folded onto the
    /// empty string because a describe surface that answers null is answering "nothing", which is the same
    /// state PowerBuilder's empty string expresses.
    /// </para>
    /// </remarks>
    internal static bool IsSentinelUpdateTable(string? updateTable)
    {
        if (string.IsNullOrEmpty(updateTable))
        {
            return true;
        }

        return string.Equals(updateTable, DescribeErrorMarker, StringComparison.Ordinal)
            || string.Equals(updateTable, DescribeUnknownMarker, StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the supplied evidence positively establishes an optimistic-concurrency mismatch.
    /// </summary>
    /// <param name="evidence">
    /// The measurement, or <see langword="null"/> when nothing was measured - which is the ordinary case
    /// for every arm that fails before a statement is generated.
    /// </param>
    /// <returns>
    /// <see langword="true"/> only when all four conditions hold; <see langword="false"/> otherwise, which
    /// leaves the outcome as the database error the oracle reports.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>FOUR CONDITIONS, ALL REQUIRED, AND EACH ONE IS A SEPARATE WAY TO AVOID SWALLOWING A GENUINE
    /// DATABASE ERROR.</b>
    /// </para>
    /// <list type="number">
    /// <item>
    /// EVIDENCE EXISTS. An absent measurement can say nothing, so it declines.
    /// </item>
    /// <item>
    /// THE PROVIDER DID NOT FAULT. A constraint violation, a connection fault and a syntax error all set
    /// that flag, and every one of them can also leave a shortfall between the two counts - so without
    /// this condition a genuine error with an incidental shortfall would be reported as a retryable
    /// conflict and the real diagnosis would be lost.
    /// </item>
    /// <item>
    /// SOMETHING WAS ACTUALLY TARGETED. A statement expecting zero rows cannot have missed any, so a
    /// non-positive expected count declines.
    /// </item>
    /// <item>
    /// THE MATCH FELL SHORT, AND ROW STATE WAS CAPTURED. Equal counts are not a mismatch; a shortfall with
    /// no captured rows is not reportable, because the published contract states that an <c>Aborted</c>
    /// response never carries an empty row list - an <c>Aborted</c> with nothing in it would tell a caller
    /// nothing it could act on, so falling back to the database error is the honest answer.
    /// </item>
    /// </list>
    /// <para>
    /// A count HIGHER than expected is deliberately not a conflict either: more rows matched than the
    /// where clause targeted is a statement-construction fault rather than a concurrency miss, and mapping
    /// it to a retryable status would invite a retry loop that cannot converge.
    /// </para>
    /// </remarks>
    internal static bool IsConcurrencyMismatch(
        [NotNullWhen(true)] ConcurrencyEvidence? evidence)
    {
        if (evidence is null)
        {
            return false;
        }

        if (evidence.ProviderFaulted)
        {
            return false;
        }

        if (evidence.RowsExpected <= 0L)
        {
            return false;
        }

        return evidence.RowsMatched < evidence.RowsExpected && evidence.Rows.Count > 0;
    }

    /// <summary>
    /// Builds the <c>common.v1.ConflictDetail</c> payload for a mismatch.
    /// </summary>
    /// <param name="evidence">The measurement, whose row list is non-empty.</param>
    /// <param name="updateTable">
    /// The table the failing statement targeted, as describe answered it. Carried because MULTI-TABLE
    /// UPDATE FROM ONE DATAWINDOW IS A REAL LEGACY CAPABILITY - the update contract is re-derived at run
    /// time from an ARRAY of table descriptors [<c>:L98-L145</c>], driven once per table [<c>:L364</c>] -
    /// so without it a caller could not tell which of several targeted tables the conflict came from.
    /// </param>
    /// <returns>The payload.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="evidence"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// <b>THE ROWS ARE CLONED.</b> Protocol-buffer messages are mutable, so copying references would let a
    /// caller mutate the evidence after classification and silently change what the response reports. A
    /// clone makes the payload independent of whatever built it.
    /// </para>
    /// <para>
    /// <b>NO SILENT OVERWRITE ANYWHERE IN THIS SYSTEM.</b> This payload exists so that a mismatch is
    /// always reported rather than resolved by guessing: the caller re-reads and retries, or surfaces the
    /// conflict. There is no last-writer-wins path here and no field below would enable one.
    /// </para>
    /// <para>
    /// Both counts travel, which keeps "the row changed" distinguishable from "the row was deleted"
    /// without a second round trip.
    /// </para>
    /// </remarks>
    internal static ConflictDetail BuildConflictDetail(ConcurrencyEvidence evidence, string? updateTable)
    {
        ArgumentNullException.ThrowIfNull(evidence);

        ConflictDetail detail = new()
        {
            UpdateTable = updateTable ?? string.Empty,
            RowsExpected = evidence.RowsExpected,
            RowsMatched = evidence.RowsMatched,
        };

        foreach (ConflictRow row in evidence.Rows)
        {
            ArgumentNullException.ThrowIfNull(row, nameof(evidence));

            detail.Rows.Add(row.Clone());
        }

        return detail;
    }

    /// <summary>
    /// Reads one conflicting row out of the <c>Buffers/</c> anti-corruption layer: its status and, per
    /// marked column, BOTH its current and its original value.
    /// </summary>
    /// <param name="store">The carrier holding the row. Read, never modified.</param>
    /// <param name="buffer">
    /// Which buffer the row is in. Remember the Filter buffer's row order is INVERTED relative to the
    /// source [<c>:L235</c>], so a consumer must not assume the ordinal counts the same way across
    /// buffers.
    /// </param>
    /// <param name="row">The ONE-BASED row ordinal (R9).</param>
    /// <param name="columns">
    /// The marked columns, in the order they should appear on the payload. Supplied by the caller so that
    /// no table name, column name or column count is ever hardcoded here (C-E). The set to pass is the
    /// UNION of the key columns and the columns marked for the concurrency predicate, because those are
    /// exactly the columns whose comparison decided the conflict.
    /// </param>
    /// <param name="storageValues">
    /// The row's CURRENT values as STORAGE holds them, keyed by one-based column number, read by the
    /// caller on the same connection and transaction the failed statement ran on.
    /// <see langword="null"/> when the row could not be reread - it no longer exists, or no key column is
    /// installed to address it by - in which case no current value is projected at all.
    /// </param>
    /// <returns>The projected row.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="store"/> or <paramref name="columns"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="row"/> is below the first one-based row, or a column is not addressable.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// A stored value's runtime type is outside the published value arms, so it cannot be expressed on the
    /// contract.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>WHY BOTH VALUES TRAVEL, AND WHY NEITHER IS RE-DERIVED HERE.</b> Under <c>updatewhere=1</c> the
    /// generated where clause carries the key column plus THE ORIGINAL VALUE OF EVERY MARKED COLUMN
    /// [<c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L8-L14</c>], so the original-value shadow IS what
    /// the failed statement compared. It is part of the carrier's state - the legacy result carrier derives
    /// from a datastore, so it is a DataWindow complete with buffers and item statuses - and is not
    /// recomputable from the current row. This method reads it through the buffer store rather than
    /// reconstructing it.
    /// </para>
    /// <para>
    /// <b>THE ROW STATUS IS READ THE LEGACY WAY, WITH COLUMN INDEX ZERO.</b> Index zero is not a column;
    /// it addresses THE ROW ITSELF. The per-column statuses a few lines below use genuine one-based
    /// ordinals, and the whole distinction is carried by whether the index is zero.
    /// </para>
    /// <para>
    /// <b>NULL IS A VALUE, NOT AN ABSENCE.</b> It is projected as the published null marker rather than
    /// coerced to zero or omitted, because collapsing null onto empty or zero would match rows the legacy
    /// would not - silently widening the where clause and overwriting a row that should have conflicted.
    /// </para>
    /// <para>
    /// FAIL-FAST ON A STRUCTURAL FAULT. An out-of-range row or an inexpressible value is a defect in the
    /// caller, not a data condition, so it throws rather than emitting a half-populated payload that would
    /// send a caller round a retry loop against rows that were never described.
    /// </para>
    /// </remarks>
    internal static ConflictRow ProjectConflictRow(
        DataWindowBufferStore store,
        DwBuffer buffer,
        long row,
        IReadOnlyList<ConflictColumn> columns,
        IReadOnlyDictionary<int, object?>? storageValues = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(columns);

        // R9 BOUNDARY. Row ordinals on this surface are one-based, matching the legacy; the store rejects
        // anything outside its buffer's range itself, and this pre-check names the mistake that produces a
        // non-positive value - a one-based ordinal rebased by accident.
        if (row < ItemStatusMachine.FirstRowNumber)
        {
            throw new ArgumentOutOfRangeException(
                nameof(row),
                row,
                "Conflict row ordinals are ONE-BASED, matching every legacy DataWindow row ordinal. A "
                    + "zero or negative value usually means a one-based ordinal was rebased by mistake.");
        }

        ConflictRow projected = new()
        {
            Buffer = buffer,
            Row = row,

            // `GetItemStatus(row, 0, buffer)` - column index zero addresses THE ROW.
            ItemStatus = store.GetItemStatus(row, ItemStatusMachine.RowStatusColumn, buffer),
        };

        foreach (ConflictColumn column in columns)
        {
            if (!column.IsAddressable)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(columns),
                    column.Number,
                    "Every conflict column needs a non-blank name and a genuine ONE-BASED ordinal. "
                        + "Column index 0 addresses the row itself and is not a column, and a negative "
                        + "ordinal usually means a one-based ordinal was rebased by mistake.");
            }

            // 🔴 THE CURRENT VALUES COME FROM STORAGE WHENEVER STORAGE COULD BE READ, and only then from
            // the carrier. The contract declares this member as the CURRENT SERVER-SIDE state - the state
            // a retry would be rebased onto - so echoing the caller's own submitted value here would
            // send it round a retry loop resubmitting the identical values for ever. The reread is the
            // caller's job because only the executor holds the connection the failed statement ran on;
            // this method reports what it is given and never re-derives it.
            //
            // A NULL DICTIONARY MEANS THE ROW COULD NOT BE READ - it no longer exists, or no key column
            // was installed to address it by - and NO current value is projected at all. The pairing of a
            // populated original set against an EMPTY current set is what distinguishes "the row was
            // deleted" from "the row changed", which is the same distinction the counts pair carries.
            if (storageValues is not null)
            {
                projected.CurrentValues.Add(
                    ProjectColumnValue(
                        column,
                        storageValues.TryGetValue(column.Number, out object? stored) ? stored : null,
                        store.GetItemStatus(row, column.Number, buffer)));
            }

            // THE ORIGINAL-VALUE SHADOW - WHAT THE CALLER BELIEVED WAS CURRENT, and literally what the
            // failed statement's where clause carried under updatewhere=1. It is read from the CARRIER
            // deliberately: it is the caller's own submitted state, it is not recomputable from storage,
            // and comparing it against the storage values above is the whole diagnostic value of this
            // payload. The store answers the current value for a column that was never modified, which is
            // exactly what the legacy where clause would have carried for it.
            projected.OriginalValues.Add(
                ProjectColumnValue(
                    column,
                    store.GetItemOriginalValue(row, column.Number, buffer),
                    store.GetItemStatus(row, column.Number, buffer)));
        }

        return projected;
    }


    /// <summary>
    /// Projects one column's value onto the published <c>common.v1.ColumnValue</c>.
    /// </summary>
    /// <param name="column">The column's name and one-based ordinal, copied through verbatim.</param>
    /// <param name="value">The stored value. <see langword="null"/> is a value, not an absence.</param>
    /// <param name="itemStatus">The column's own item status, read with its genuine ordinal.</param>
    /// <returns>The projected value.</returns>
    /// <exception cref="InvalidOperationException">
    /// The value's runtime type is outside the published arms.
    /// </exception>
    /// <remarks>
    /// The conversion is delegated to <see cref="CarrierValue.TryToWire"/>, the <c>Buffers/</c> layer's own
    /// projection, so this file introduces no second spelling of the value mapping. A type it cannot
    /// express is a structural fault: emitting a placeholder would put a value on the wire that the caller
    /// would compare against and act on.
    /// </remarks>
    private static ColumnValue ProjectColumnValue(
        ConflictColumn column,
        object? value,
        ItemStatus itemStatus)
    {
        if (!CarrierValue.TryToWire(value, out AnyValue? wire) || wire is null)
        {
            throw new InvalidOperationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Column {column.Number} holds a value of a runtime type that the published "
                    + $"common.v1.AnyValue cannot express, so the conflict payload cannot be built. The "
                    + $"value itself is deliberately not quoted here."));
        }

        return new ColumnValue
        {
            ColumnName = column.Name,
            ColumnId = column.Number,
            Value = wire,

            // Set explicitly rather than left absent: the server HAS read a status by the time it builds a
            // conflict row, and the contract makes this field presence-tracked so that "read and equal to
            // NotModified" stays distinguishable from "not read".
            ItemStatus = itemStatus,
        };
    }

    /// <summary>
    /// Masks the statement carried by a payload, so that every payload leaving this file has passed through
    /// the injected redactor exactly once.
    /// </summary>
    /// <param name="error">The payload to mask.</param>
    /// <returns>
    /// The payload with its statement masked and its other four members copied through untouched.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>A NO-OP TODAY, AND CALLED ANYWAY (C-F).</b> Both raising arms of <c>_of_update</c> pass an EMPTY
    /// statement [<c>:L190</c>, <c>:L197</c>], and the else arm's payload is built from the transaction's
    /// code and text alone - so there is nothing to mask on any current path. It is routed through
    /// regardless so that there is exactly ONE path, and a future arm that does carry a statement cannot
    /// reach a payload without passing through it.
    /// </para>
    /// <para>
    /// <b>ONLY THE STATEMENT CHANGES (C-B).</b> The provider code, the message text - including the one
    /// hardcoded Chinese diagnostic the legacy synthesizes - the buffer and the one-based row are not
    /// inspected, not normalised and not scrubbed. The non-destructive mutation guarantees that: every
    /// member of the payload has an <c>init</c> accessor, so the named member changes and the rest are
    /// copied by definition, with no opportunity for one to be dropped by omission.
    /// </para>
    /// <para>
    /// The concrete redactor offers the same convenience on itself, but this file holds the ABSTRACTION -
    /// which deliberately exposes one string member so it cannot grow into a general-purpose payload
    /// sanitizer - so the mutation is performed here.
    /// </para>
    /// <para>
    /// INTERNAL RATHER THAN PRIVATE, ON PURPOSE (C-H). Every arm that reaches it today passes an empty
    /// statement, so a test driven only through <see cref="Classify"/> could assert nothing about masking.
    /// Exposing it to the parity suite - which the project's <c>InternalsVisibleTo</c> already admits -
    /// lets the single redaction path be exercised with a payload that DOES carry a statement, including
    /// with a pass-through redactor, so the path itself is proven rather than assumed.
    /// </para>
    /// </remarks>
    internal DbErrorData Redact(in DbErrorData error) =>
        error with { SqlSyntax = _redactor.Redact(error.SqlSyntax) };

    // ----------------------------------------------------------------------------------------------
    //  THE PROJECTION - the single sanctioned conversion of an outcome into a gRPC status
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Projects a conflict outcome onto the gRPC <c>Aborted</c> status and the binary trailer carrying its
    /// detail. THE ONLY SANCTIONED SPELLING OF THAT CONVERSION.
    /// </summary>
    /// <param name="outcome">The outcome to project.</param>
    /// <param name="projection">
    /// The status and trailers when the outcome is a conflict; <see langword="default"/> otherwise.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when <paramref name="outcome"/> is
    /// <see cref="UpdateOutcomeKind.Conflict"/> and carries a detail; <see langword="false"/> for every
    /// other kind, which must be reported through the operation's own status instead.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="outcome"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// The generated descriptor carries no usable rich-error binding.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>THIS METHOD RAISES NOTHING (C-K).</b> It returns the two values a throw site needs and lets that
    /// site raise them, so <c>Grpc/UpdateService.cs</c> and <c>Program.cs</c>'s central mapper agree on one
    /// spelling instead of each inventing its own. A conflict is an expected outcome with a defined
    /// response, and the classifier that produced it must remain callable from the multi-table loop, which
    /// an in-flight exception would abandon.
    /// </para>
    /// <para>
    /// <b>ABORTED RATHER THAN FAILED_PRECONDITION</b>, deliberately: the operation MAY succeed if the
    /// caller re-reads and retries at a higher level, which is precisely what <c>Aborted</c> means and what
    /// <c>FailedPrecondition</c> does not - and <c>Aborted</c> is the canonical gRPC mapping to the HTTP
    /// 409 that Gateway's REST projection surfaces.
    /// </para>
    /// <para>
    /// <b>THE DETAIL TRAVELS ON THE STATUS AND NOT AS A RESPONSE FIELD.</b> A conflict is not a successful
    /// call with a bad outcome, and a second reporting channel would let a consumer handle one and miss the
    /// other. One mechanism only: this status plus this trailer.
    /// </para>
    /// <para>
    /// The trailer also carries the reconciled return code, so a client that only wants to CLASSIFY the
    /// failure need not decode the payload at all.
    /// </para>
    /// </remarks>
    internal static bool TryProjectAborted(UpdateOutcome outcome, out RichErrorProjection projection)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        if (outcome.Kind != UpdateOutcomeKind.Conflict || outcome.Conflict is null)
        {
            projection = default;

            return false;
        }

        RichErrorTrailer trailer = new()
        {
            Conflict = outcome.Conflict,

            // Numerically a RetCode.Value, which for a conflict is E_DB_ERROR - the code the oracle's own
            // arm returns, unchanged by the narrowing.
            RetCode = outcome.Code,
        };

        Metadata trailers = [];

        // Add(string, byte[]) is the BINARY overload, which is what the `-bin` key requires: gRPC
        // base64-transports a `-bin` entry, whereas a non-binary entry may carry ASCII only and would
        // corrupt a serialized message.
        trailers.Add(RichErrorTrailerKey, trailer.ToByteArray());

        projection = new RichErrorProjection(
            new Status(RichErrorStatusCode, AbortedStatusDetail),
            trailers);

        return true;
    }

    /// <summary>
    /// Reads the rich-error binding off the generated <c>Update</c> method descriptor and validates it.
    /// </summary>
    /// <returns>The binding.</returns>
    /// <exception cref="InvalidOperationException">
    /// The method, its options or the binding is missing, or the binding is internally inconsistent.
    /// </exception>
    /// <remarks>
    /// Reading only; every check lives in <see cref="ValidateRichErrorBinding"/> so that all four failure
    /// arms are reachable from a test without a deliberately broken descriptor.
    /// </remarks>
    private static RichErrorBinding ResolveUpdateRichErrorBinding()
    {
        MethodDescriptor? method = UpdateService.Descriptor.FindMethodByName(UpdateMethodName);

        MethodOptions? options = method?.GetOptions();

        return ValidateRichErrorBinding(options?.GetExtension(CommonV1Extensions.RichError));
    }

    /// <summary>
    /// Validates a rich-error binding against what this projection actually emits.
    /// </summary>
    /// <param name="binding">The binding read off a descriptor, or <see langword="null"/> if absent.</param>
    /// <returns><paramref name="binding"/> when every check passes.</returns>
    /// <exception cref="InvalidOperationException">
    /// The binding is absent, carries no key, carries a key that is not binary-transported, names a status
    /// code other than <c>Aborted</c>, or names a payload type this projection does not serialize.
    /// </exception>
    /// <remarks>
    /// <para>
    /// FAIL FAST ON A STRUCTURAL FAULT, WHICH THIS IS. If the descriptor cannot say how a conflict should
    /// be reported then the conflict response cannot be produced correctly at all, and the framework's own
    /// posture for a structural fault is to stop rather than continue degraded
    /// [<c>ws_objects/pfw.pbl.src/pfw.sra:L111-L144</c>]. Falling back to a hardcoded key would be the
    /// worst outcome available: the service would answer under a key the descriptor does not advertise, and
    /// a conforming client reading the descriptor would find nothing. A DATA-LEVEL CONFLICT, BY CONTRAST,
    /// NEVER TERMINATES ANYTHING - it is an expected outcome with a defined response.
    /// </para>
    /// <para>
    /// FOUR CHECKS, BECAUSE EACH FAILS DIFFERENTLY. The key must be present; it must end in <c>-bin</c>, or
    /// the payload is corrupted in transit rather than rejected; the status code must be the one this
    /// projection emits, or the descriptor and the response disagree about what a client should catch; and
    /// the payload type must be the message this projection serializes, or a client parses the bytes with
    /// the wrong parser.
    /// </para>
    /// <para>
    /// NO MESSAGE BELOW QUOTES A VALUE THAT COULD CARRY DATA. The declared key, status code and payload
    /// type are contract metadata rather than payload, and nothing else is interpolated.
    /// </para>
    /// </remarks>
    internal static RichErrorBinding ValidateRichErrorBinding(RichErrorBinding? binding)
    {
        if (binding is null || binding.TrailerKey.Length == 0)
        {
            throw new InvalidOperationException(
                "The generated descriptor for persistence.v1.UpdateService.Update carries no rich-error "
                + "binding, so the mandated optimistic-concurrency conflict response could not name the "
                + "trailer its detail travels in. The binding is declared as a custom method option in "
                + "shared/PowerFramework.Contracts/Proto/persistence.v1.proto; restore it and rebuild the "
                + "contracts project.");
        }

        if (!binding.TrailerKey.EndsWith(BinaryTrailerSuffix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The rich-error trailer key declared on persistence.v1.UpdateService.Update does not end "
                + "in '" + BinaryTrailerSuffix + "'. gRPC transports only a '" + BinaryTrailerSuffix
                + "' key as binary; under any other key a serialized protocol-buffer payload is corrupted "
                + "in transit rather than rejected, so the conflict detail would arrive unparseable.");
        }

        if (binding.GrpcStatusCode != (int)StatusCode.Aborted)
        {
            throw new InvalidOperationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The rich-error binding on persistence.v1.UpdateService.Update declares gRPC status "
                    + $"code {binding.GrpcStatusCode}, but the mandated conflict response is Aborted "
                    + $"({(int)StatusCode.Aborted}), which is the canonical mapping to HTTP 409. The "
                    + $"descriptor and this projection must agree, or a client cannot know which status "
                    + $"to catch."));
        }

        if (!string.Equals(
                binding.PayloadType,
                RichErrorTrailer.Descriptor.FullName,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The rich-error binding on persistence.v1.UpdateService.Update names payload type '"
                + binding.PayloadType + "', but this projection serializes '"
                + RichErrorTrailer.Descriptor.FullName
                + "'. A client would parse the trailer bytes with the wrong parser.");
        }

        return binding;
    }

    /// <summary>
    /// The suffix gRPC requires on a metadata key whose value is binary.
    /// </summary>
    /// <remarks>
    /// Declared here rather than in the vocabulary block above because it is a property of the gRPC
    /// metadata format rather than of the legacy oracle, and it is read only by the binding validation.
    /// </remarks>
    private const string BinaryTrailerSuffix = "-bin";
}

#endregion
