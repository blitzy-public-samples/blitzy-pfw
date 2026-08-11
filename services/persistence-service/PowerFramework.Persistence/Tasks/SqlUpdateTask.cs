// ==================================================================================================
//  SqlUpdateTask.cs - THE UPDATE-ORCHESTRATION WORKER TASK BEHIND CONTRACT C-06
//  ------------------------------------------------------------------------------------------------
//  ROLE
//  The managed port of `n_cst_thread_task_sqlupdate`
//  [ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru, 409 lines], marked
//  `[运行在子线程]` - WORKER-THREAD AFFINITY [:L2]. It derives from `SqlTaskBase`, exactly as the
//  oracle derives from `n_cst_thread_task_sqlbase` [:L4], and it is the engine behind contract
//  C-06 `persistence.v1.UpdateService`.
//
//  THIS FILE IS THE ORCHESTRATOR AND NOTHING ELSE. The ALGORITHMS of the update protocol live in
//  the sibling `Concurrency/` folder and are DRIVEN from here, never re-implemented:
//
//      Concurrency/UpdateWhereBuilder.cs     the six-field descriptor, its ordered collection and
//                                            its three-arm admission test [:L82-L96]; the
//                                            byte-comparable seven-step modification script and the
//                                            `updatekeyinplace=no` self-assignment workaround
//                                            [:L98-L170]; AND the prepare-versus-update BRANCH
//                                            itself, as `UpdatePreparer.PrepareAndUpdate`
//                                            [:L356-L372].
//      Concurrency/ConflictDetector.cs       every arm of `_of_update` [:L172-L252] - the three
//                                            sentinels, the veto discrimination, the two
//                                            cancellation checks, success-is-1, the defensive
//                                            override, and the conflict narrowing.
//      Concurrency/IdentityColumnResolver.cs the identity round trip [:L215-L247] - discovery, the
//                                            FORWARD primary walk, the BACKWARD filter walk, the
//                                            emit guard and the counts.
//      Buffers/DataWindowBuffers.cs          the carrier: buffers, item statuses, counts, affinity.
//      Buffers/ChangesetCodec.cs             the changeset payload the update blob carries.
//      Errors/DbErrorData.cs                 the five-member database-error payload.
//
//  WHAT IS LEFT FOR THIS FILE, AND IT IS EXACTLY THE ORACLE'S `ondotask` [:L284-L404] PLUS ITS
//  STATE: the field block [:L30-L38], the four setters [:L254-L274], the reset [:L58-L72], the
//  finalize [:L406-L408], the ORDER of the sequence, the BRANCH CONDITIONS, the CARRIER LIFECYCLE
//  and the COMMIT/ROLLBACK EPILOGUE. Nothing else.
//
//  ============================ RULES POSITION (UR4) =============================================
//  NO USER RULES WERE PROVIDED. `review_rules` returns exactly one line saying so, and that line is
//  the complete document. No rule is invented, inferred or back-filled, and no file enters scope
//  because of one. The binding constraints are therefore the enterprise-standard baseline plus the
//  named non-rule constraints C-A through C-L, and each non-obvious decision below cites the
//  constraint that drives it, as C-K requires.
//
//  ============================ SEVEN THINGS A STRAIGHTFORWARD PORT GETS WRONG ===================
//
//  1. 🔴 THE PREPARE STEP RUNS ONLY ON THE MULTI-TABLE PATH.
//     This is the single most surprising fact in the whole object and it comes from reading the
//     invocation site rather than the summary. `_of_UpdatePrepare` HAS EXACTLY ONE CALLER, at
//     [:L365], INSIDE the multi-table branch:
//
//         if _bMultiTableUpdate then
//             nCount = UpperBound(Tables)
//             if nCount = 0 then sError = "没有设置可更新表!" ; rtCode = E_INVALID_ARGUMENT ; exit
//             for nIndex = 1 to nCount
//                 rtCode = _of_UpdatePrepare(data,nIndex) ; if rtCode <> OK then exit
//                 rtCode = _of_Update(data)              ; if rtCode <> OK then exit
//             next
//         else
//             rtCode = _of_Update(data)          <-- _of_UpdatePrepare IS NEVER CALLED   [:L370-L372]
//         end if
//
//     ON THE SINGLE-TABLE PATH THE DATAWINDOW'S OWN STATIC DEFINITION GOVERNS - its `update=`,
//     `updatewhere=`, `updatekeyinplace=` and per-column flags are used exactly as authored, and the
//     descriptor array is not consulted at all. Calling the prepare unconditionally would reset and
//     re-derive every column's update/key/identity flags on a path where the oracle leaves them
//     alone: a behavioural change dressed as consistency, which C-B forbids. AND THE SINGLE-TABLE
//     PATH IS THE ONE THE GOLDEN-MASTER FIXTURE EXERCISES
//     [ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L14], so this is the common case rather than a
//     corner. Both paths are covered by tests. The branch itself is owned by
//     `UpdatePreparer.PrepareAndUpdate` and is DRIVEN from `RunUpdateBody` below.
//
//  2. THE SINGLE-PASS BLOCK IS NOT A LOOP, AND ITS FIRST `exit` LEAVES THE CODE AT `OK`.
//     The body is wrapped in `do ... loop while false` [:L300, :L373] - a breakable block that
//     executes once. It is expressed here as a method with early returns, never as a loop. The
//     subtle consequence is at [:L301]: a cancellation observed there `exit`s with `rtCode` still
//     holding the `RetCode.OK` assigned at [:L298], and it is the SEPARATE check at [:L381-L383],
//     AFTER the teardown, that rewrites it to `CANCELLED`. Returning `CANCELLED` from that first
//     check would skip the teardown ordering the oracle guarantees.
//
//  3. `SetSort("")` IS LOAD-BEARING, NOT CLEANUP.
//     `data.SetSort("")` at [:L334] carries the comment
//     `//*去掉排序条件,保证数据按拷贝的顺序被提交` - remove the sort condition so the data is
//     committed in COPY ORDER. Row order determines statement order, so dropping this call changes
//     the sequence of generated statements. It is not tidying and it is not optional.
//
//  4. A `SetChanges` RESULT OF -1 WITH ZERO UPDATE ROWS IS A SUCCESS.
//     [:L337-L346] has two arms and both must survive. `-1` together with an update-row count of 0
//     is the "no update data" case and answers `RetCode.OK`; anything else is
//     `RetCode.E_INVALID_DATA` with the diagnostic `无效的更新数据!`. Collapsing the first arm into a
//     failure turns an empty update into an error.
//
//  5. TWO CODE SPACES MEET IN THIS FILE AND MUST NEVER BE CONFLATED (C-B).
//     `Create`, `SetChanges`, `SetTransObject` and `Update` answer the DATAWINDOW convention - 1 for
//     success, -1 for failure - while everything else answers the framework RETURN-CODE ALGEBRA
//     where `RetCode.OK` is 0 and a negative value is a failure. The oracle reuses one `rtCode`
//     local for both [:L336, :L365]; this port keeps them in separate locals with distinct names,
//     compares DataWindow results against `DataWindowBufferStore.DataStoreSuccess` /
//     `DataStoreFailure`, and never applies `Predicates.IsSucceeded` to a DataWindow value.
//
//  6. NEVER DESTROY A CACHED CARRIER - RESET IT.
//     `if bCacheDS then data.Reset() else if IsValid(data) then Destroy data` [:L375-L379]. The
//     datastore cache in `SqlTaskBase` holds LIVE references keyed by data-object name, so
//     destroying a cached carrier leaves a dangling entry that every later task on the same thread
//     would resolve. Expressed here as a `try`/`finally` whose teardown branches on ownership.
//
//  7. THE EPILOGUE'S DE-DUPLICATION GUARD IS A REAL BEHAVIOUR.
//     `if rtCode <> of_GetLastErrorCode() then Event OnError(...)` appears TWICE [:L389, :L397] and
//     suppresses a second report when the failure has already been reported - which the prepare and
//     the classifier both do on their own arms. The latch it reads lives in the SUBSTRATE's error
//     event, `_lastErrCode = errCode`
//     [ws_objects/pfw.thread.pbl.src/n_cst_thread_task.sru:L189-L190], read back by
//     `of_GetLastErrorCode()` [:L597]. `ISqlTaskHost` models no such member, so the latch is
//     reproduced by overriding the virtual `OnError` here: every raise in the whole update path -
//     this file's own, the error sink handed to `ConflictDetector`, the callback handed to
//     `UpdatePreparer`, and `SqlTaskBase`'s - dispatches through that override, exactly as every
//     PowerScript raise dispatches through the substrate's event.
//     NOTE THE ASYMMETRY at [:L394-L400]: a cancellation IS rolled back but is NEVER reported.
//
//  ============================ CONSTRAINT LEDGER (C-A … C-L) ====================================
//  C-A  Siblings plus the four shared projects only - Contracts, Shared.Kernel, Shared.Diagnostics
//       and Shared.Containers - as `PowerFramework.Persistence.csproj` declares. No peer service is
//       referenced and no new `PackageReference` is introduced.
//  C-B  Every preserved behaviour is annotated at its point of reproduction with a
//       `ws_objects/**` locator: the multi-table-only prepare, the first-`exit`-leaves-OK subtlety,
//       `SetSort("")` for copy order, the `-1`-with-zero-rows success arm, the two code spaces, the
//       cached-reset-versus-owned-destroy teardown, and the de-duplication guard.
//  C-C  Nothing under `ws_objects/**` is edited, moved, deleted or reformatted. Every behavioural
//       claim in this file carries a locator that was verified by opening the file.
//  C-D  No `n_scriptinvoker` and no `pfw.utility.regexp` analogue appears. The oracle's variadic
//       escape hatch has no analogue to port, and this object needs no pattern matching at all.
//  C-E  SQLite only, and nothing here opens a connection or selects an engine. Statement execution
//       reaches this file exclusively through the injected `IUpdateTarget` seam.
//  C-F  No credential literal appears. `TransactionData.LogPass` is never read, never logged and
//       never echoed - this file does not touch the descriptor at all. Every database-error payload
//       leaves through `ConflictDetector.Redact`, the one sanctioned redaction path, and the only
//       payload this file authors itself carries an EMPTY statement by the oracle's own hand
//       [:L293].
//  C-G  No listener and no route. This type is reached only through C-06's authorized gRPC methods.
//  C-H  `TimeProvider` is the single clock in the system and THIS FILE READS NO CLOCK AT ALL - the
//       oracle has no time-dependent behaviour, so there is no `DateTime.UtcNow`, no
//       `Environment.TickCount` and no `Stopwatch` anywhere below. Every collaborator is an
//       injected abstraction, so the whole orchestration is exercisable with NO DATABASE, NO REAL
//       THREAD and NO CARRIER.
//  C-I  Builds clean under `TreatWarningsAsErrors`; adds no package.
//  C-J  No static global. The oracle declares
//       `global n_cst_thread_task_sqlupdate n_cst_thread_task_sqlupdate` [:L21]; this is a
//       DI-registered instance type with no static state, and it is DISTINCT from its caller-side
//       proxy, whose two events it reaches through `ISqlUpdateTaskProxy`.
//  C-K  The multi-table-only prepare, the backward filter iteration and why it is correct, and the
//       two overrides that invert the obvious result are all documented at length.
//  C-L  The environment's operational shape is untouched by this file.
//
//  .editorconfig: `Tasks/` is OUTSIDE the scoped naming suppressions - the only suppression in this
//  project is `Sql/ClauseModifier.cs` - so this file declares ZERO SCREAMING_SNAKE identifiers. The
//  legacy constant VALUES it needs are consumed from `RetCode`, from the generated `common.v1` and
//  `persistence.v1` types, and from the siblings, under their PascalCase spellings (AAP §0.4.5.3).
//
//  `Configuration/PersistenceOptions.cs` is DELIBERATELY NOT REFERENCED. It carries SQLite,
//  transaction-pool, query and JWT settings and nothing the update protocol reads; the oracle's
//  update path has no configurable value at all. An unused reference would be an unused coupling.
//
//  ============================ OWNERSHIP CROSS-CHECK ============================================
//  This file implements NONE of the following, by design:
//      the modification-string builder            -> Concurrency/UpdateWhereBuilder.cs
//      the table descriptor and its collection    -> Concurrency/UpdateWhereBuilder.cs
//      the prepare-versus-update branch           -> UpdatePreparer.PrepareAndUpdate
//      the self-assignment workaround             -> UpdatePreparer.ApplyKeyChangeRefresh
//      the conflict classifier and its arms       -> Concurrency/ConflictDetector.cs
//      the identity resolver and its two walks    -> Concurrency/IdentityColumnResolver.cs
//      any codec                                  -> Buffers/ChangesetCodec.cs
//      any redactor                               -> Errors/SqlRedactor.cs
//      any connection or engine selection         -> Data/ and Transactions/
//      the caller-side accumulation               -> Tasks/TaskProxies/ (see ISqlUpdateTaskProxy)
//      any `RpcException`                         -> Grpc/UpdateService.cs is the throw site
// ==================================================================================================

using Microsoft.Extensions.Logging;
using PowerFramework.Contracts.Persistence.V1;
using PowerFramework.Persistence.Buffers;
using PowerFramework.Persistence.Concurrency;
using PowerFramework.Persistence.Errors;
using PowerFramework.Persistence.Transactions;

// The framework return-code algebra and its tri-state predicates, aliased rather than imported by
// namespace. Tasks/SqlTaskBase.cs aliases the same two names to resolve a genuine collision with the
// generated `common.v1.RetCode`; here the aliases are what let this file avoid importing
// `PowerFramework.Contracts.Common.V1` AT ALL - the generated `DwBuffer`, `ItemStatus`,
// `ConflictDetail` and `DbError` types travel through this orchestration inside sibling payloads and
// are never named here, so importing that namespace would be an unused coupling.
using Predicates = PowerFramework.Shared.Kernel.Predicates;
using RetCode = PowerFramework.Shared.Kernel.RetCode;

namespace PowerFramework.Persistence.Tasks;

#region The update-only carrier seam - what `data` is asked for that the retrieval store cannot answer

/// <summary>
/// The update-capable view of one result carrier: the four DataWindow operations the update path
/// performs on it, plus the four collaborator surfaces the sibling <c>Concurrency/</c> algorithms are
/// driven through.
/// </summary>
/// <remarks>
/// <para>
/// <b>WHY THIS SEAM EXISTS RATHER THAN A WIDER <see cref="ISqlDataStore"/>.</b> The oracle's local
/// <c>data</c> is a <c>n_cst_thread_task_sqlbase_ds</c>, which is declared <c>from datastore</c>
/// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds.sru:L4</c>], so ONE PowerBuilder
/// object is simultaneously the row/buffer carrier, the DataWindow-definition surface AND the
/// statement executor. In the port those roles have three different owners:
/// <c>Buffers/DataWindowBuffers.cs</c> owns the carrier, <see cref="ISqlDataStore"/> owns the
/// RETRIEVAL half of the definition surface, and this interface adds the UPDATE half - which nothing
/// else owns and which <see cref="ISqlDataStore.Describe"/> deliberately answers <c>"!"</c> for,
/// because the update properties are not in its closed set.
/// </para>
/// <para>
/// <b>THE FOUR OPERATIONS ANSWER THE DATAWINDOW CONVENTION, NOT THE RETURN-CODE ALGEBRA.</b> Each
/// returns <see cref="DataWindowBufferStore.DataStoreSuccess"/> (1) or
/// <see cref="DataWindowBufferStore.DataStoreFailure"/> (-1). See point 5 of this file's header for
/// why the two code spaces are kept apart.
/// </para>
/// <para>
/// <b>DISPOSAL MEANS "DESTROY WHAT THIS TASK OWNS", AND NOTHING MORE.</b> The oracle's teardown is
/// <c>if bCacheDS then data.Reset() else if IsValid(data) then Destroy data</c>
/// [<c>n_cst_thread_task_sqlupdate.sru:L375-L379</c>], so an adapter handed a CACHED store must be a
/// lightweight view whose lifetime is the cached store's: the orchestrator resets such a carrier and
/// never disposes it, and disposing a view over a cached store must not tear the cached store down.
/// </para>
/// <para>
/// Every member is an abstraction rather than a concrete type precisely so the whole orchestration
/// runs with no database, no real thread and no carrier (C-H).
/// </para>
/// </remarks>
internal interface ISqlUpdateCarrier : IDisposable
{
    /// <summary>
    /// The retrieval-side definition surface and, through
    /// <see cref="ISqlDataStore.Carrier"/>, the buffer store itself.
    /// </summary>
    /// <remarks>
    /// Reached for three things and no others: the data-object assignment
    /// [<c>n_cst_thread_task_sqlupdate.sru:L324</c>], the sort drop [<c>:L334</c>], and the carrier
    /// the sibling preparer walks [<c>:L158-L165</c>].
    /// </remarks>
    ISqlDataStore Store { get; }

    /// <summary>
    /// The update executor and state-clearer the classifier drives - <c>Data.of_ClearState()</c>
    /// [<c>n_cst_thread_task_sqlupdate.sru:L186</c>] and <c>Data.Update(true,false)</c>
    /// [<c>:L204</c>].
    /// </summary>
    IUpdateTarget Target { get; }

    /// <summary>
    /// The describe surface the modification script is built against - the column count, the
    /// <c>&lt;column&gt;.Id</c> resolution and the runtime <c>DataWindow.Table.UpdateKeyinPlace</c>
    /// value [<c>n_cst_thread_task_sqlupdate.sru:L103</c>, <c>:L118</c>, <c>:L155</c>].
    /// </summary>
    IUpdateTargetMetadata TargetMetadata { get; }

    /// <summary>
    /// The modify surface the script is applied through - <c>sErr = Data.Modify(sModString)</c>
    /// [<c>n_cst_thread_task_sqlupdate.sru:L145</c>], following the
    /// EMPTY-STRING-MEANS-SUCCESS convention.
    /// </summary>
    IUpdateTargetModifier TargetModifier { get; }

    /// <summary>
    /// The two surfaces the identity round trip reads: the describe side that discovers the identity
    /// column, and the value side that walks <c>Primary!</c> forward and <c>Filter!</c> backward
    /// [<c>n_cst_thread_task_sqlupdate.sru:L217-L241</c>].
    /// </summary>
    /// <remarks>
    /// ⚠️ The backward walk at <c>:L237</c> is correct and is the single most dangerous line in this
    /// refactor for one-based-to-zero-based translation (AAP §0.4.5.4 R9). The oracle's own comment at
    /// <c>:L235</c> records why: the filter buffer's row order is THE REVERSE of the source's
    /// (<c>过滤缓冲区的数据与数据源...顺序是相反的</c>). It looks like a bug. It is not a bug.
    /// "Correcting" the direction produces wrong identity values that a row-count assertion would not
    /// catch. The walk itself belongs to <see cref="IdentityColumnResolver"/>; this member is only how
    /// it is reached.
    /// </remarks>
    IdentityTableSurfaces Identity { get; }

    /// <summary>
    /// Builds the carrier from a DataWindow syntax string - <c>data.Create(_sSQLSyntax, ref sError)</c>
    /// [<c>n_cst_thread_task_sqlupdate.sru:L317</c>].
    /// </summary>
    /// <param name="sqlSyntax">The DataWindow syntax to build from. Never empty when this is called.</param>
    /// <param name="errors">
    /// Receives the driver's diagnostic. The oracle passes its <c>sError</c> local BY REFERENCE, so
    /// whatever the driver writes becomes the text the epilogue may later report [<c>:L398</c>]; an
    /// implementation writes the empty string when it has nothing to say.
    /// </param>
    /// <returns>
    /// <see cref="DataWindowBufferStore.DataStoreSuccess"/> or
    /// <see cref="DataWindowBufferStore.DataStoreFailure"/>. Anything other than success yields
    /// <see cref="RetCode.E_INVALID_SQL"/> [<c>:L318</c>].
    /// </returns>
    long Create(string sqlSyntax, out string errors);

    /// <summary>
    /// Applies the changeset payload - <c>data.SetChanges(_blbUpdateData)</c>
    /// [<c>n_cst_thread_task_sqlupdate.sru:L336</c>].
    /// </summary>
    /// <param name="changes">
    /// The payload, or <see langword="null"/> for the oracle's zero-length <c>Blob("")</c>.
    /// </param>
    /// <returns>
    /// <see cref="DataWindowBufferStore.DataStoreSuccess"/> when the payload was applied, and
    /// <see cref="DataWindowBufferStore.DataStoreFailure"/> when there was nothing to apply or the
    /// payload was rejected - which is exactly what
    /// <see cref="IChangesetPayloadCodec.TryApply"/> already answers, so an implementation delegates
    /// to the sibling codec rather than decoding anything itself.
    /// </returns>
    /// <remarks>
    /// The caller distinguishes the two reasons for a failure by the update-row count, because the
    /// oracle does: <c>-1</c> with zero rows is the "no update data" SUCCESS arm [<c>:L339-L342</c>].
    /// </remarks>
    long SetChanges(CarrierState? changes);

    /// <summary>
    /// Attaches the transaction the statements will run on -
    /// <c>data.SetTransObject(transObject)</c> [<c>n_cst_thread_task_sqlupdate.sru:L350</c>].
    /// </summary>
    /// <param name="transaction">The pooled transaction acquired by the base.</param>
    /// <returns>
    /// <see cref="DataWindowBufferStore.DataStoreSuccess"/> or
    /// <see cref="DataWindowBufferStore.DataStoreFailure"/>. Anything other than success yields
    /// <see cref="RetCode.E_INVALID_TRANSACTION"/> with the diagnostic
    /// <c>设置事务对象失败!</c> [<c>:L351-L352</c>].
    /// </returns>
    long SetTransObject(IPooledTransaction? transaction);

    /// <summary>
    /// The affected-row measurement for the optimistic-concurrency narrowing, or
    /// <see langword="null"/> when nothing was measured.
    /// </summary>
    /// <returns>
    /// The evidence <see cref="ConflictDetector.IsConcurrencyMismatch"/> requires, or
    /// <see langword="null"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>THE ORACLE MEASURES NOTHING.</b> An exhaustive search of its update path finds no
    /// rows-affected check of any kind, so the narrowing is a documented ADDITION rather than a ported
    /// behaviour, and <see langword="null"/> - the oracle's own state - leaves the failure arm a plain
    /// database error carrying the transaction's own code and text. Nothing regresses when an
    /// implementation cannot measure.
    /// </para>
    /// <para>
    /// <b>WHY IT IS READ BEFORE THE UPDATE RATHER THAN AFTER.</b>
    /// <see cref="UpdateAttempt.Evidence"/> is an immutable input by design, because that is what
    /// keeps every conflict arm reachable with no database and no concurrent writer (C-H). Measuring
    /// beforehand is possible precisely because of what <c>updatewhere=1</c> means: the concurrency
    /// predicate is the key column PLUS THE ORIGINAL VALUE OF EVERY MARKED COLUMN, and the
    /// <c>Buffers/</c> anti-corruption layer already carries both the current and the original value
    /// of every marked column per row - on the sole evidenced fixture all six of them
    /// [<c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L8-L14</c>]. An implementation that can only
    /// measure during execution answers <see langword="null"/> here and is no worse off than the
    /// oracle.
    /// </para>
    /// </remarks>
    ConcurrencyEvidence? CaptureConcurrencyEvidence();
}

/// <summary>
/// Turns a store obtained from <see cref="SqlTaskBase"/>'s datastore cache or its affinity-keyed
/// factory into the update-capable carrier the orchestration drives.
/// </summary>
/// <remarks>
/// <para>
/// The oracle has no adapter because it has no split: <c>of_GetCacheDS</c> and
/// <c>Create n_cst_thread_task_sqlbase_ds[_mt]</c> both answer an object that is already
/// update-capable [<c>n_cst_thread_task_sqlupdate.sru:L305</c>, <c>:L309-L311</c>]. This seam is what
/// re-joins the two halves the port separated, and keeping it injectable is what lets the whole
/// orchestration be driven with no carrier at all (C-H).
/// </para>
/// <para>
/// ⚠️ <b>THE `_mt` NAMING TRAP, RECORDED HERE BECAUSE THIS IS WHERE AFFINITY IS ROUTED.</b>
/// <c>n_cst_thread_task_sqlbase_ds</c> is the MAIN-thread carrier and
/// <c>n_cst_thread_task_sqlbase_ds_mt</c> is the WORKER-thread carrier that DERIVES FROM IT
/// [<c>n_cst_thread_task_sqlbase_ds_mt.sru:L4</c>] - <c>_mt</c> does NOT mean "main thread". The
/// selection itself lives in <see cref="SqlTaskBase.CreateDataStore"/>, which is
/// [<c>n_cst_thread_task_sqlupdate.sru:L307-L314</c>]'s peer and the third of the library's three
/// affinity-selection sites, the others being
/// [<c>n_cst_thread_task_sqlbase.sru:L553-L557</c>] and
/// [<c>n_cst_thread_task_sqlquery.sru:L541-L548</c>].
/// </para>
/// </remarks>
internal interface ISqlUpdateCarrierAdapter
{
    /// <summary>
    /// Wraps <paramref name="store"/> as an update-capable carrier.
    /// </summary>
    /// <param name="store">The store from the cache or the affinity-keyed factory.</param>
    /// <returns>A carrier view over that store.</returns>
    ISqlUpdateCarrier Adapt(ISqlDataStore store);
}

#endregion

#region The transaction's two update events, and the adapter onto the pooled transaction

/// <summary>
/// The two vetoable/observing update events the legacy transaction object declares -
/// <c>event type long onbeforeupdate(datastore data)</c> and
/// <c>event onafterupdate(datastore data, long result)</c>
/// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:L15-L16</c>].
/// </summary>
/// <remarks>
/// <para>
/// <b>BOTH ARE DECLARED WITH NO SCRIPT IN THE ORACLE</b>, which is why the defaults below are what
/// they are: PowerBuilder answers <c>0</c> for a <c>long</c> event with no implementation, and
/// <see cref="Predicates.IsPrevented(long?)"/> tests equality against <see cref="RetCode.PREVENT"/>
/// (1), so an unimplemented before-update hook is NOT a veto. Subclasses of the transaction object
/// override them; that is the extension point this interface preserves.
/// </para>
/// <para>
/// They are modelled here rather than on <see cref="IPooledTransaction"/> because the pooled
/// transaction is a CONNECTION abstraction - connect, commit, rollback, exec, SQL state - and these
/// two events belong to the update protocol. The classifier consumes them through
/// <see cref="IUpdateTransaction"/>, and <see cref="PooledUpdateTransaction"/> below is the join.
/// </para>
/// </remarks>
internal interface ISqlUpdateTransactionHooks
{
    /// <summary>
    /// The vetoable before-update hook - <c>TransObject.Event OnBeforeUpdate(Data)</c>
    /// [<c>n_cst_thread_task_sqlupdate.sru:L195</c>].
    /// </summary>
    /// <returns>
    /// <see cref="RetCode.PREVENT"/> to veto; anything else continues. The default is the value
    /// PowerBuilder answers for an unimplemented <c>long</c> event.
    /// </returns>
    /// <remarks>
    /// A veto is discriminated by the transaction's OWN failure predicate into either a database
    /// error or a CLEAN CANCELLATION [<c>:L196-L201</c>]. That discrimination belongs to
    /// <see cref="ConflictDetector"/>; this member only produces the veto.
    /// </remarks>
    long OnBeforeUpdate() => SqlUpdateTask.UnimplementedEventResult;

    /// <summary>
    /// The observing after-update hook - <c>TransObject.Event OnAfterUpdate(Data,rtCode)</c>
    /// [<c>n_cst_thread_task_sqlupdate.sru:L206</c>].
    /// </summary>
    /// <param name="result">
    /// The DataWindow update result AS THE UPDATE ITSELF ANSWERED IT, before the defensive override at
    /// <c>:L208-L210</c> reconciles it. The ordering is contract: a hook that saw the reconciled value
    /// would be a behavioural change (C-B).
    /// </param>
    void OnAfterUpdate(long result)
    {
        // The oracle declares this event with no script, so the default observes and does nothing.
        // Written as an explicit empty body rather than omitted so that the "no script" fact is
        // legible at the point a reader looks for the behaviour.
    }
}

/// <summary>
/// Joins a pooled transaction and its optional update hooks onto the surface
/// <see cref="ConflictDetector"/> classifies against.
/// </summary>
/// <remarks>
/// A pure adapter: it holds no state of its own, decides nothing, and re-derives nothing. Note in
/// particular that <see cref="IsFailed"/> forwards to <see cref="IPooledTransaction.IsSqlFailed"/>,
/// which is <c>SQLCode &lt; 0</c> - the oracle's <c>of_IsFailed()</c>
/// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:L337</c>] - and NOT the non-zero test
/// the transaction object's own veto arm uses at <c>:L267</c>. The two genuinely disagree for a
/// positive code and must not be unified.
/// </remarks>
internal sealed class PooledUpdateTransaction : IUpdateTransaction
{
    private readonly IPooledTransaction _transaction;
    private readonly ISqlUpdateTransactionHooks? _hooks;

    /// <summary>
    /// Creates the adapter.
    /// </summary>
    /// <param name="transaction">The pooled transaction the base acquired.</param>
    /// <param name="hooks">
    /// The update hooks, or <see langword="null"/> when the transaction implements neither - which is
    /// the oracle's own state, both events being script-less.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="transaction"/> is <see langword="null"/>.</exception>
    internal PooledUpdateTransaction(
        IPooledTransaction transaction,
        ISqlUpdateTransactionHooks? hooks = null)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        _transaction = transaction;
        _hooks = hooks;
    }

    /// <inheritdoc/>
    public long SqlCode => _transaction.SqlCode;

    /// <inheritdoc/>
    public long SqlDbCode => _transaction.SqlDbCode;

    /// <inheritdoc/>
    public string SqlErrText => _transaction.SqlErrText;

    /// <inheritdoc/>
    public bool IsFailed() => _transaction.IsSqlFailed();

    /// <inheritdoc/>
    public long OnBeforeUpdate() =>
        _hooks is null ? SqlUpdateTask.UnimplementedEventResult : _hooks.OnBeforeUpdate();

    /// <inheritdoc/>
    public void OnAfterUpdate(long result) => _hooks?.OnAfterUpdate(result);
}

#endregion

#region The caller-side proxy's two events - fired per invocation, ACCUMULATED ELSEWHERE

/// <summary>
/// The two events the caller-side proxy declares -
/// <c>event onupdated(long inserted, long updated, long deleted)</c> and
/// <c>event onidentitycolumndataretrieved(long id, ref long primaryvalues[], ref long filtervalues[])</c>
/// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlupdate.sru:L18-L19</c>].
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>ACCUMULATION IS THE PROXY'S JOB, NOT THIS TASK'S, AND THE DISTINCTION IS OBSERVABLE.</b> The
/// proxy's own handlers accumulate: <c>_nRowsInserted += inserted</c> and its two siblings
/// [<c>:L66-L69</c>], and one <c>IdColData</c> record APPENDED PER FIRING
/// [<c>:L71-L77</c>]. On the multi-table path the worker fires both events once PER TABLE
/// [<c>n_cst_thread_task_sqlupdate.sru:L243</c>, <c>:L247</c> inside the loop at <c>:L364-L369</c>],
/// so the totals only exist on the caller side. This task therefore fires per invocation and sums
/// NOTHING - <c>Tasks/TaskProxies/</c> owns the sums, and
/// <see cref="IdentityColumnResolver.ResolveTables"/> is the function that performs them.
/// </para>
/// <para>
/// Hazard 1 of <c>docs/PB多线程绕坑提示.md</c> shapes the identity signature: a worker thread calling a
/// main-thread function that RETURNS a string or blob risks a memory fault, and the guidance is to
/// pass such payloads back by reference instead - which is exactly why the legacy event declares
/// <c>ref long primaryvalues[]</c> and <c>ref long filtervalues[]</c> rather than returning them.
/// Here the arrays travel inward as read-only lists, which carries the same guarantee without
/// exposing a mutable buffer.
/// </para>
/// <para>
/// The element type is nullable because a null identity is representable: <c>GetItemNumber</c> answers
/// null for a null item and PowerScript stores that null into the array it hands to the callback
/// [<c>n_cst_threading_task_sqlupdate.sru:L144</c>, <c>:L160</c>], and the published contract's
/// element type is <c>NullableInt64</c> for the same reason.
/// </para>
/// </remarks>
internal interface ISqlUpdateTaskProxy
{
    /// <summary>
    /// The row-count report - <c>tasking.Event OnUpdated(...)</c>
    /// [<c>n_cst_thread_task_sqlupdate.sru:L247</c>].
    /// </summary>
    /// <param name="inserted">Rows inserted by this invocation.</param>
    /// <param name="updated">Rows updated by this invocation.</param>
    /// <param name="deleted">Rows deleted by this invocation.</param>
    /// <remarks>
    /// Fires on EVERY success, including a pure update and a pure delete, because the oracle's call
    /// sits OUTSIDE the inserted-count block that gates the identity work [<c>:L215</c> versus
    /// <c>:L247</c>].
    /// </remarks>
    void OnUpdated(long inserted, long updated, long deleted);

    /// <summary>
    /// The identity round-trip report - <c>tasking.Event OnIdentityColumnDataRetrieved(...)</c>
    /// [<c>n_cst_thread_task_sqlupdate.sru:L243</c>].
    /// </summary>
    /// <param name="identityColumnId">
    /// The discovered identity column's ONE-BASED ordinal. Always positive: the oracle only fires when
    /// the ordinal is greater than zero [<c>:L226</c>].
    /// </param>
    /// <param name="primaryValues">
    /// Values collected from the <c>Primary!</c> buffer in FORWARD order [<c>:L228-L233</c>].
    /// </param>
    /// <param name="filterValues">
    /// Values collected from the <c>Filter!</c> buffer in BACKWARD order [<c>:L237</c>], because that
    /// buffer's row order is the reverse of the source's [<c>:L235</c>].
    /// </param>
    /// <remarks>
    /// Fires only when at least one of the two lists is non-empty - an OR, not an AND
    /// [<c>:L242</c>]. The guard is applied by <see cref="IdentityColumnResolver"/>, which answers an
    /// EMPTY block list when it declines, so this task fires exactly as often as the oracle does.
    /// </remarks>
    void OnIdentityColumnDataRetrieved(
        long identityColumnId,
        IReadOnlyList<long?> primaryValues,
        IReadOnlyList<long?> filterValues);
}

#endregion


#region The task

/// <summary>
/// The worker-thread task that orchestrates one DataWindow update - the managed port of
/// <c>n_cst_thread_task_sqlupdate</c>
/// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru</c>], and the engine behind
/// contract C-06 <c>persistence.v1.UpdateService</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>WORKER-THREAD AFFINITY IS A CONTRACT, NOT COMMENTARY.</b> The oracle's export comment marks this
/// object <c>[运行在子线程]</c> [<c>:L2</c>], and its caller-side counterpart
/// <c>n_cst_threading_task_sqlupdate</c> is marked <c>[运行在当前线程]</c>
/// [<c>n_cst_threading_task_sqlupdate.sru:L2</c>]. The pair is DELIBERATELY NOT FLATTENED into one
/// async method (AAP §0.4.5.4): this type is the worker half, it fires the caller half's two events
/// through <see cref="ISqlUpdateTaskProxy"/>, and it performs none of the caller half's accumulation.
/// </para>
/// <para>
/// <b>LIFECYCLE.</b> <see cref="Reset"/> is the port of <c>of_reset</c> [<c>:L58-L72</c>],
/// <see cref="OnDoTask"/> of the <c>ondotask</c> event [<c>:L284-L404</c>], and
/// <see cref="OnFinalize"/> of the <c>onfinalize</c> event [<c>:L406-L408</c>].
/// </para>
/// <para>
/// <b>NO GLOBAL (C-J).</b> The oracle declares
/// <c>global n_cst_thread_task_sqlupdate n_cst_thread_task_sqlupdate</c> [<c>:L21</c>] - an auto-instance
/// that shadows its own type name. This type has no static mutable state and is registered in the
/// container instead.
/// </para>
/// </remarks>
internal sealed class SqlUpdateTask : SqlTaskBase
{
    // ---------------------------------------------------------------------------------------------
    //  Legacy diagnostic TEXT, verbatim, under PascalCase identifiers.
    //  Each is the exact string the oracle assigns to its `sError` local, which the epilogue may then
    //  report through the error event [:L398]. They are NOT routed through localization, because the
    //  oracle does not route them - preserving that is C-B.
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// <c>sError = "无效的数据源对象!"</c> [<c>:L327</c>] - neither a syntax nor a data object was set.
    /// </summary>
    internal const string InvalidDataObjectMessage = "无效的数据源对象!";

    /// <summary>
    /// <c>sError = "无效的更新数据!"</c> [<c>:L343</c>] - the changeset payload was rejected and the
    /// update-row count was not zero.
    /// </summary>
    internal const string InvalidUpdateDataMessage = "无效的更新数据!";

    /// <summary>
    /// <c>sError = "设置事务对象失败!"</c> [<c>:L351</c>] - the carrier refused the transaction.
    /// </summary>
    internal const string SetTransObjectFailedMessage = "设置事务对象失败!";

    /// <summary>
    /// The value PowerBuilder answers for a <c>long</c> event that has no script - the state both
    /// transaction update events are declared in
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:L15-L16</c>].
    /// </summary>
    /// <remarks>
    /// Numerically equal to <see cref="RetCode.OK"/>, and deliberately spelled as its own constant: it
    /// is not a return code being propagated, it is the absence of a handler, and
    /// <see cref="Predicates.IsPrevented(long?)"/> reads it as "no veto".
    /// </remarks>
    internal const long UnimplementedEventResult = 0L;

    // The three trailing arguments of `Event OnDBError(code, text, "", Primary!, 0)` [:L293, and its
    // peers at :L190 and :L197] are NOT restated in this file. DbErrorData.FromTransaction already
    // spells the empty statement, the Primary buffer and row 0 once, and the sibling declares that it
    // does so precisely to mirror those argument lists. A second spelling here would be a second thing
    // free to drift - and note that row 0 is a DELIBERATE LITERAL AND NOT AN INDEX (R9): every
    // DataWindow row ordinal in this system is one-based, so 0 cannot name a row and means "no
    // particular row".

    // ---------------------------------------------------------------------------------------------
    //  Injected collaborators. Every one is an abstraction, which is the whole of C-H: the
    //  orchestration below is exercisable with no database, no real thread and no carrier.
    // ---------------------------------------------------------------------------------------------

    private readonly ISqlUpdateCarrierAdapter _carrierAdapter;
    private readonly ConflictDetector _conflictDetector;
    private readonly ISqlUpdateTaskProxy? _updateProxy;
    private readonly ISqlUpdateTransactionHooks? _transactionHooks;
    private readonly ILogger _logger;

    /// <summary>The error channel the siblings raise through - see <see cref="UpdateErrorSink"/>.</summary>
    private readonly UpdateErrorSink _errorSink;

    // ---------------------------------------------------------------------------------------------
    //  Instance state, mirroring the oracle's private block [:L30-L38] field for field.
    //
    //  `TABLEDATA Tables[]` [:L30] and `boolean _bMultiTableUpdate` [:L33] are BOTH held by the
    //  sibling UpdatableTableCollection, which already declares the ordered descriptor array, the
    //  multi-table switch and the three-arm admission test. Declaring either again here would be the
    //  second spelling of a piece of state that must have exactly one.
    // ---------------------------------------------------------------------------------------------

    private readonly UpdatableTableCollection _tables = new();

    /// <summary><c>boolean _bAutoCommit</c> [<c>:L32</c>].</summary>
    private bool _autoCommit;

    /// <summary><c>string _sDataObject</c> [<c>:L34</c>].</summary>
    private string _dataObject = string.Empty;

    /// <summary><c>string _sSQLSyntax</c> [<c>:L35</c>].</summary>
    private string _sqlSyntax = string.Empty;

    /// <summary>
    /// <c>blob _blbUpdateData</c> [<c>:L37</c>], as the payload type the published contract carries.
    /// </summary>
    /// <remarks>
    /// <see langword="null"/> is the oracle's <c>Blob("")</c>. Cleared DETERMINISTICALLY the moment it
    /// has been applied [<c>:L348</c>], again on finalize [<c>:L406</c>], again on reset [<c>:L68</c>]
    /// and again on disposal - never left for the collector. That discipline is the same one
    /// <c>docs/PB多线程绕坑提示.md</c> hazard 2 prescribes for cross-thread references, applied to a
    /// payload that can be arbitrarily large.
    /// </remarks>
    private CarrierState? _updateData;

    /// <summary><c>long _nUpdateRows</c> [<c>:L38</c>].</summary>
    private long _updateRows;

    /// <summary>
    /// The substrate's <c>_lastErrCode</c> latch
    /// [<c>ws_objects/pfw.thread.pbl.src/n_cst_thread_task.sru:L89, :L189</c>], which
    /// <c>of_GetLastErrorCode()</c> reads back [<c>:L597</c>] and the epilogue's de-duplication guard
    /// compares against [<c>n_cst_thread_task_sqlupdate.sru:L389, :L397</c>].
    /// </summary>
    /// <remarks>
    /// The companion <c>_lastErrInfo</c> [<c>:L90, :L190</c>] is deliberately NOT held: nothing in this
    /// object reads <c>of_GetLastErrorInfo()</c>, and an unheld field cannot drift.
    /// </remarks>
    private long _lastErrorCode;

    /// <summary>
    /// The classification the most recent run produced, or <see langword="null"/> before the first run
    /// reaches the classifier.
    /// </summary>
    /// <remarks>
    /// NOT AN ORACLE FIELD, AND NOT A BEHAVIOURAL ADDITION EITHER. In the legacy the classification is
    /// consumed in-process by the caller that raised the task, so it never needed a home. Across a
    /// service boundary the same information has to be READ BACK by the wire layer, which mints the
    /// conflict status from it - so it is latched here rather than recomputed there from a return code
    /// that has already lost the distinction between a conflict, a veto and a database error.
    /// </remarks>
    private UpdateOutcome? _lastOutcome;

    /// <summary>
    /// The terminal diagnostic the most recent run finished with, or the empty string.
    /// </summary>
    /// <remarks>
    /// The same value the oracle passes into its epilogue [<c>:L385-L399</c>], latched for the same
    /// reason as <see cref="_lastOutcome"/>: the wire layer reports it, and it is not otherwise
    /// recoverable from the return code.
    /// </remarks>
    private string _lastErrorText = string.Empty;

    /// <summary>
    /// Creates the task.
    /// </summary>
    /// <param name="host">The threading substrate seam.</param>
    /// <param name="transactionPool">The transaction pool the base borrows from.</param>
    /// <param name="dataStoreFactory">The affinity-keyed store factory the base creates through.</param>
    /// <param name="hookActivator">The retrieval-hook activator the base resolves through.</param>
    /// <param name="carrierAdapter">
    /// Turns a store from the base's cache or factory into the update-capable carrier this
    /// orchestration drives.
    /// </param>
    /// <param name="conflictDetector">
    /// The classifier that owns every arm of <c>_of_update</c> [<c>:L172-L252</c>], and - through
    /// <see cref="ConflictDetector.Redact"/> - the one sanctioned redaction path every database-error
    /// payload leaves through (C-F).
    /// </param>
    /// <param name="timeProvider">
    /// The system's single clock, handed to the base. THIS TYPE READS NO CLOCK: the oracle's update
    /// path has no time-dependent behaviour, so there is no <c>DateTime.UtcNow</c>, no
    /// <c>Environment.TickCount</c> and no <c>Stopwatch</c> anywhere in this file (C-H).
    /// </param>
    /// <param name="logger">The structured log channel.</param>
    /// <param name="updateProxy">
    /// The caller-side proxy's two events, or <see langword="null"/> for an unattached task - the state
    /// <c>#ParentTasking</c> can legitimately be in, which is why the base checks validity before
    /// forwarding its own event too.
    /// </param>
    /// <param name="transactionHooks">
    /// The transaction's before/after-update hooks, or <see langword="null"/> when neither is
    /// implemented - the oracle's own state, both events being script-less.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="carrierAdapter"/>, <paramref name="conflictDetector"/> or
    /// <paramref name="logger"/> is <see langword="null"/>, or any argument the base requires is.
    /// </exception>
    internal SqlUpdateTask(
        ISqlTaskHost host,
        TransactionPool transactionPool,
        ISqlDataStoreFactory dataStoreFactory,
        ISqlRetrievalHookActivator hookActivator,
        ISqlUpdateCarrierAdapter carrierAdapter,
        ConflictDetector conflictDetector,
        TimeProvider timeProvider,
        ILogger<SqlUpdateTask> logger,
        ISqlUpdateTaskProxy? updateProxy = null,
        ISqlUpdateTransactionHooks? transactionHooks = null)
        : base(host, transactionPool, dataStoreFactory, hookActivator, timeProvider, logger)
    {
        ArgumentNullException.ThrowIfNull(carrierAdapter);
        ArgumentNullException.ThrowIfNull(conflictDetector);
        ArgumentNullException.ThrowIfNull(logger);

        _carrierAdapter = carrierAdapter;
        _conflictDetector = conflictDetector;
        _logger = logger;
        _updateProxy = updateProxy;
        _transactionHooks = transactionHooks;
        _errorSink = new UpdateErrorSink(this);

        // `UpdatableTableCollection.IsBusy` is DELIBERATELY LEFT UNSET, and the reason is behavioural.
        // The oracle's worker-side `of_addupdatabletable` [:L82] and `of_setmultitableupdate` [:L265]
        // carry NO busy guard at all - the guards live on the CALLER-SIDE proxy, written against its
        // own `of_IsBusy()` [n_cst_threading_task_sqlupdate.sru:L207, :L223]. Wiring the collection's
        // gate to this task's running flag would ADD a guard the worker does not have, which C-B
        // forbids. The one running guard the worker DOES have is in of_reset [:L60], and it is applied
        // by Reset() below rather than delegated.
    }

    /// <summary>
    /// The substrate's <c>#Running</c> property, as an injected predicate.
    /// </summary>
    /// <value>
    /// A predicate answering whether the task is executing, or <see langword="null"/> - treated as
    /// "not running" - which is the state a task constructed outside a thread is in.
    /// </value>
    /// <remarks>
    /// <para>
    /// <b>WHY A PREDICATE RATHER THAN A HOST MEMBER.</b> <c>#Running</c> is the substrate's own
    /// property, set by its start event [<c>ws_objects/pfw.thread.pbl.src/n_cst_thread_task.sru:L164</c>],
    /// and <see cref="ISqlTaskHost"/> deliberately models no such member - every guard written against
    /// it in <c>n_cst_thread_task_sqlbase</c> is COMMENTED OUT in the oracle and carried across inert.
    /// This object is the one place where a <c>#Running</c> guard is LIVE [<c>:L60</c>], so the seam is
    /// declared here, in the same shape the sibling <see cref="UpdatableTableCollection.IsBusy"/> uses,
    /// rather than widening a shared interface that every other implementor would then have to answer.
    /// </para>
    /// <para>
    /// Keeping it a predicate is also what lets the guard be exercised with no real thread (C-H).
    /// </para>
    /// </remarks>
    internal Func<bool>? IsRunning { get; set; }

    /// <summary>
    /// The ordered descriptor array and the multi-table switch -
    /// <c>TABLEDATA Tables[]</c> [<c>:L30</c>] and <c>boolean _bMultiTableUpdate</c> [<c>:L33</c>].
    /// </summary>
    /// <remarks>
    /// Exposed so that the gRPC layer can project C-06's <c>repeated TableUpdateContract</c> onto it
    /// and so the parity suite can assert the admission test, WITHOUT this file declaring a second
    /// descriptor type. The collection is the single owner of both pieces of state.
    /// </remarks>
    internal UpdatableTableCollection Tables => _tables;

    /// <summary>
    /// Whether the epilogue commits on success - <c>boolean _bAutoCommit</c> [<c>:L32</c>].
    /// </summary>
    internal bool AutoCommit => _autoCommit;

    /// <summary>
    /// The multi-table switch, read straight off the descriptor collection that owns it.
    /// </summary>
    /// <remarks>
    /// 🔴 This flag alone decides whether the prepare step runs AT ALL [<c>:L356</c> versus
    /// <c>:L370</c>]. See point 1 of this file's header.
    /// </remarks>
    internal bool MultiTableUpdate => _tables.MultiTableUpdate;

    /// <summary>The data-object name - <c>string _sDataObject</c> [<c>:L34</c>].</summary>
    internal string DataObject => _dataObject;

    /// <summary>The DataWindow syntax - <c>string _sSQLSyntax</c> [<c>:L35</c>].</summary>
    internal string SqlSyntax => _sqlSyntax;

    /// <summary>
    /// The row count that accompanied the changeset payload - <c>of_getupdaterows</c>
    /// [<c>:L45, :L79</c>].
    /// </summary>
    /// <returns>The count as set by <see cref="SetUpdateData"/>.</returns>
    /// <remarks>
    /// Load-bearing, and not merely informational: it is the second conjunct of the
    /// "no update data is a SUCCESS" arm [<c>:L339</c>].
    /// </remarks>
    internal long GetUpdateRows() => _updateRows;

    /// <summary>
    /// The last code reported through the error channel - <c>of_GetLastErrorCode()</c>
    /// [<c>ws_objects/pfw.thread.pbl.src/n_cst_thread_task.sru:L597</c>].
    /// </summary>
    /// <returns>The latched code, or zero when nothing has been reported.</returns>
    /// <remarks>
    /// Read by the epilogue's de-duplication guard [<c>n_cst_thread_task_sqlupdate.sru:L389, :L397</c>]
    /// and by nothing else. See point 7 of this file's header for why the latch is reproduced in
    /// <see cref="OnError"/>.
    /// </remarks>
    internal long GetLastErrorCode() => _lastErrorCode;

    /// <summary>
    /// The outcome the classifier produced for the LAST update invocation, or <see langword="null"/> when
    /// no invocation reached the classifier.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PUBLISHED BECAUSE THE OUTCOME IS RICHER THAN THE CODE IT COLLAPSES TO, and one consumer needs the
    /// difference: contract C-06's <c>Aborted</c> response carries a whole <c>ConflictDetail</c>, and the
    /// only place that detail exists is on the outcome. <c>OnDoTask</c> answers a return code, so without
    /// this the conflict arm would be classified and then thrown away one frame later.
    /// </para>
    /// <para>
    /// "LAST" IS EXACT ON BOTH PATHS. The single-table path invokes the classifier once. The multi-table
    /// path invokes it once per descriptor and STOPS AT THE FIRST FAILURE
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru:L356-L369</c>], so the last
    /// outcome recorded is always the one that decided the run's result.
    /// </para>
    /// <para>
    /// IT IS NOT AN ACCUMULATOR AND MUST NOT BECOME ONE. Accumulation across the multi-table loop belongs
    /// to the caller-side proxy [<c>n_cst_threading_task_sqlupdate.sru:L66-L69</c>]; this member reports
    /// one classification.
    /// </para>
    /// </remarks>

    /// Clears the error latch - <c>of_clearerror()</c>
    /// [<c>ws_objects/pfw.thread.pbl.src/n_cst_thread_task.sru:L607-L611</c>].
    /// </summary>
    /// <returns><c>RetCode.OK</c>, which is the only value the oracle's function can answer.</returns>
    /// <remarks>
    /// <para>
    /// <b>THE LATCH IS PER EXECUTION, AND WITHOUT THIS IT WAS PER OBJECT.</b> The substrate clears it at
    /// the head of EVERY execution - <c>of_ClearError()</c> is the third statement of <c>of_execute</c>,
    /// after the running and skip guards and before <c>OnStart</c>
    /// [<c>n_cst_thread_task.sru:L240-L246</c>] - so the epilogue's de-duplication guard
    /// [<c>n_cst_thread_task_sqlupdate.sru:L389, :L397</c>] only ever compares against a code THIS run
    /// raised. A port that latches but never clears silently changes what that guard means: a second,
    /// entirely independent failure carrying the same code as an earlier run's is read as a duplicate and
    /// its error event is never raised, so the caller is told nothing at all went wrong on the run that
    /// failed. The longer a task is reused, the more codes accumulate that can never be reported again.
    /// </para>
    /// <para>
    /// <b>WITHIN-RUN DE-DUPLICATION IS UNAFFECTED, WHICH IS THE WHOLE POINT.</b> The clear happens once,
    /// before the body; every raise inside the body still writes the latch and the two epilogue guards
    /// still read it, so a code the body has already reported is still not reported twice. Clearing per
    /// raise instead would destroy that, and clearing at the end of a run would leave the latch zero for a
    /// guard that has not run yet.
    /// </para>
    /// <para>
    /// <b>ONLY THE CODE IS CLEARED, BECAUSE ONLY THE CODE IS HELD.</b> The oracle's function also assigns
    /// <c>_lastErrInfo = ""</c> [<c>:L608</c>], and this object deliberately holds no companion field -
    /// nothing here reads <c>of_GetLastErrorInfo()</c>, and an unheld field cannot drift. See the remark
    /// on <see cref="_lastErrorCode"/>.
    /// </para>
    /// </remarks>
    internal long ClearError()
    {
        _lastErrorCode = 0L;

        return RetCode.OK;
    }


    #region The inputs - the four setters and the payload, each a verbatim port of one function

    /// <summary>
    /// Sets the changeset payload and its row count - <c>of_setupdatedata</c> [<c>:L44, :L74-L77</c>].
    /// </summary>
    /// <param name="updateData">
    /// The payload, or <see langword="null"/> for the oracle's zero-length blob. The oracle's parameter
    /// is <c>ref blob</c> purely to avoid copying a large value across the call
    /// [<c>docs/PB多线程绕坑提示.md</c> hazard 1]; a reference type needs no such device.
    /// </param>
    /// <param name="rows">
    /// The row count the sender measured with <c>GetChanges</c>
    /// [<c>n_cst_threading_task_sqlupdate.sru:L255, :L258</c>].
    /// </param>
    /// <returns><see cref="RetCode.OK"/>, unconditionally, exactly as the oracle answers [<c>:L76</c>].</returns>
    /// <remarks>
    /// NO BUSY GUARD, DELIBERATELY. The oracle's worker-side function has none; the guard lives on the
    /// caller-side proxy [<c>n_cst_threading_task_sqlupdate.sru:L236</c>]. Adding one here would change
    /// behaviour (C-B).
    /// </remarks>
    internal long SetUpdateData(CarrierState? updateData, long rows)
    {
        // [:L74-L75] both members are written unconditionally; a negative or zero count is legal and is
        // exactly what the "no update data" arm at [:L339] keys on.
        _updateData = updateData;
        _updateRows = rows;

        return RetCode.OK;
    }

    /// <summary>
    /// Sets whether the epilogue commits on success - <c>of_setautocommit</c> [<c>:L49, :L254-L257</c>].
    /// </summary>
    /// <param name="autoCommit">The flag.</param>
    /// <returns><see cref="RetCode.OK"/> [<c>:L256</c>].</returns>
    internal long SetAutoCommit(bool autoCommit)
    {
        _autoCommit = autoCommit;

        return RetCode.OK;
    }

    /// <summary>
    /// Names the data object to update through - <c>of_setdataobject</c> [<c>:L50, :L259-L263</c>].
    /// </summary>
    /// <param name="dataObject">The data-object name.</param>
    /// <returns><see cref="RetCode.OK"/> [<c>:L262</c>].</returns>
    /// <remarks>
    /// ⚠️ <b>THE TWO SOURCES ARE MUTUALLY EXCLUSIVE AND EACH SETTER CLEARS THE OTHER.</b> The oracle
    /// clears the syntax here [<c>:L260</c>] and clears the data object in
    /// <see cref="SetSqlSyntax"/> [<c>:L271</c>]. That is what makes the three-way shaping branch at
    /// [<c>:L316-L331</c>] decidable: syntax first, then data object, then the
    /// <see cref="RetCode.E_INVALID_DATAOBJECT"/> arm. Dropping either clear would let both be set and
    /// silently prefer the syntax.
    /// </remarks>
    internal long SetDataObject(string dataObject)
    {
        // [:L259] assigned as given - the oracle applies no trimming and no validation, and the
        // emptiness test that matters happens at the shaping branch instead.
        _dataObject = dataObject ?? string.Empty;

        // [:L260]
        _sqlSyntax = string.Empty;

        return RetCode.OK;
    }

    /// <summary>
    /// Turns multi-table update on or off - <c>of_setmultitableupdate</c> [<c>:L51, :L265-L268</c>].
    /// </summary>
    /// <param name="multiTable">The switch.</param>
    /// <returns><see cref="RetCode.OK"/> [<c>:L267</c>].</returns>
    /// <remarks>
    /// 🔴 A REAL FLAG WITH ITS OWN SETTER, and the one input that decides whether the prepare step runs
    /// at all. Multi-table update from one DataWindow is genuine legacy capability - the descriptor is
    /// an ARRAY with an add path [<c>:L82-L96</c>] - which is why C-06's <c>PrepareUpdate</c> carries a
    /// <c>repeated</c> six-field descriptor. The state itself is held by the collection that owns the
    /// array, so there is exactly one copy of it.
    /// </remarks>
    internal long SetMultiTableUpdate(bool multiTable)
    {
        _tables.MultiTableUpdate = multiTable;

        return RetCode.OK;
    }

    /// <summary>
    /// Sets the DataWindow syntax to build the carrier from - <c>of_setsqlsyntax</c>
    /// [<c>:L52, :L270-L274</c>].
    /// </summary>
    /// <param name="sqlSyntax">The syntax.</param>
    /// <returns><see cref="RetCode.OK"/> [<c>:L273</c>].</returns>
    internal long SetSqlSyntax(string sqlSyntax)
    {
        // [:L270]
        _sqlSyntax = sqlSyntax ?? string.Empty;

        // [:L271] - the mirror of SetDataObject's clear. See its remarks.
        _dataObject = string.Empty;

        return RetCode.OK;
    }

    /// <summary>
    /// Appends an updatable-table descriptor, six-argument form - <c>of_addupdatabletable</c>
    /// [<c>:L46, :L82-L96</c>].
    /// </summary>
    /// <param name="name">The update table name.</param>
    /// <param name="updatableColumns">The columns to mark updatable.</param>
    /// <param name="keyColumns">The columns to mark as keys.</param>
    /// <param name="identityColumn">The identity column, or the empty string for none.</param>
    /// <param name="updateWhere">
    /// The concurrency mode, or <see langword="null"/> for ABSENT. <c>1</c> is the
    /// "key and updateable columns" mode the sole evidenced fixture uses
    /// [<c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L14</c>].
    /// </param>
    /// <param name="updateKeyInPlace">The key-in-place setting, or <see langword="null"/> for ABSENT.</param>
    /// <returns>
    /// <see cref="RetCode.OK"/>, or <see cref="RetCode.E_INVALID_ARGUMENT"/> when the descriptor fails
    /// admission [<c>:L84</c>].
    /// </returns>
    /// <remarks>
    /// <para>
    /// The three-arm admission test - empty name OR empty updatable-column array OR empty key-column
    /// array - and the one-based append are owned by <see cref="UpdatableTableCollection"/>. AN EMPTY
    /// IDENTITY COLUMN STAYS LEGAL: it is not one of the three arms, and the script simply omits its
    /// line [<c>:L127-L129</c>].
    /// </para>
    /// <para>
    /// ⚠️ <b>PRESENCE ON THE LAST TWO MEMBERS IS LOAD-BEARING END TO END.</b> The caller-side proxy
    /// declares this function in TWO arities [<c>n_cst_threading_task_sqlupdate.sru:L59-L60</c>], and
    /// the four-argument form <c>SetNull</c>s BOTH values before delegating [<c>:L227-L234</c>]; the
    /// prepare then tests <c>IsNull(...)</c> on each before emitting its line
    /// [<c>n_cst_thread_task_sqlupdate.sru:L131, :L135</c>]. That is why the descriptor's members are
    /// <see cref="long"/>? and <see cref="bool"/>?, and why C-06's <c>TableUpdateContract</c> uses
    /// proto3 <c>optional</c> presence on <c>updatewhere</c> and <c>updatekeyinplace</c>. AN ABSENT
    /// SETTING MUST NOT BECOME A DEFAULTED ONE - defaulting <c>updatewhere</c> to 0 would silently
    /// switch the concurrency mode, and defaulting <c>updatekeyinplace</c> to false would emit a line
    /// the oracle omits.
    /// </para>
    /// </remarks>
    internal long AddUpdatableTable(
        string name,
        IEnumerable<string> updatableColumns,
        IEnumerable<string> keyColumns,
        string identityColumn,
        long? updateWhere,
        bool? updateKeyInPlace) =>
        _tables.AddUpdatableTable(
            name,
            updatableColumns,
            keyColumns,
            identityColumn,
            updateWhere,
            updateKeyInPlace);

    /// <summary>
    /// Appends an updatable-table descriptor leaving both optional settings ABSENT - the four-argument
    /// form the caller-side proxy declares
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlupdate.sru:L60, :L227-L234</c>].
    /// </summary>
    /// <param name="name">The update table name.</param>
    /// <param name="updatableColumns">The columns to mark updatable.</param>
    /// <param name="keyColumns">The columns to mark as keys.</param>
    /// <param name="identityColumn">The identity column, or the empty string for none.</param>
    /// <returns>
    /// <see cref="RetCode.OK"/>, or <see cref="RetCode.E_INVALID_ARGUMENT"/> when the descriptor fails
    /// admission.
    /// </returns>
    /// <remarks>
    /// The built modification script OMITS BOTH the <c>DataWindow.Table.UpdateWhere</c> and the
    /// <c>DataWindow.Table.UpdateKeyinPlace</c> lines for a descriptor added this way, so the carrier's
    /// own definition governs both - which on the sole evidenced fixture already says
    /// <c>updatewhere=1 updatekeyinplace=no</c>.
    /// </remarks>
    internal long AddUpdatableTable(
        string name,
        IEnumerable<string> updatableColumns,
        IEnumerable<string> keyColumns,
        string identityColumn) =>
        _tables.AddUpdatableTable(name, updatableColumns, keyColumns, identityColumn);

    /// <summary>
    /// Replaces the descriptor array with an empty one and turns the multi-table switch off - the
    /// descriptor half of <c>of_reset</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru:L63, :L67</c>].
    /// </summary>
    /// <returns>
    /// <see cref="RetCode.OK"/> once the array is empty, or <see cref="RetCode.E_BUSY"/> while the task
    /// is running [<c>:L60</c>].
    /// </returns>
    /// <remarks>
    /// <para>
    /// REPLACEMENT, NOT TRIMMING - the oracle assigns a freshly declared empty array
    /// [<c>:L67</c>], which is why the contract's repeated descriptor field replaces the set wholesale.
    /// It is separated from <see cref="Reset"/> because a prepare needs exactly this much of
    /// <c>of_reset</c> and must NOT also discard the payload, the auto-commit choice or the source
    /// [<c>:L62, :L64-L65, :L68-L69</c>]. Two prepares naming one table must leave ONE table.
    /// </para>
    /// <para>
    /// <b>The guard is the WORKER's running flag, applied here rather than delegated.</b> The collection's
    /// own <c>IsBusy</c> gate is deliberately left unset - see the constructor for why - so the guard
    /// <c>of_reset</c> carries at [<c>:L60</c>] is applied at this level, exactly as <see cref="Reset"/>
    /// applies it. Nothing is cleared behind a refusal.
    /// </para>
    /// </remarks>
    internal long ResetUpdatableTables()
    {
        // [:L60] the guard is FIRST, and a refusal leaves the descriptor set entirely intact.
        if (IsRunning?.Invoke() == true)
        {
            return RetCode.E_BUSY;
        }

        // [:L63, :L67] the switch off and the empty array, in the oracle's own pairing.
        return _tables.Reset();
    }

    #endregion

    #region Reset and finalize - the two lifecycle hooks that clear the payload

    /// <summary>
    /// Clears every input and chains to the base - <c>of_reset</c> [<c>:L43, :L58-L72</c>].
    /// </summary>
    /// <returns>
    /// <see cref="RetCode.E_BUSY"/> while the task is running [<c>:L60</c>]; otherwise whatever the
    /// base's reset answers [<c>:L71</c>].
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>THE GUARD IS FIRST AND NOTHING IS CLEARED BEHIND IT.</b> A running task answers
    /// <see cref="RetCode.E_BUSY"/> with its state entirely intact - the oracle returns before its
    /// first assignment - so a refused reset cannot half-clear the inputs.
    /// </para>
    /// <para>
    /// <b>THE BASE IS CHAINED LAST, AND ITS RESULT IS THE ANSWER.</b> <c>return super::of_Reset()</c>
    /// [<c>:L71</c>] is a RETURN, not a fire-and-forget call, so the base's own outcome - it resets the
    /// commit signal and the SQL parameter collection - is what a caller sees.
    /// </para>
    /// </remarks>
    internal override long Reset()
    {
        // [:L60] `if #Running then return RetCode.E_BUSY`. A null predicate reads as "not running",
        // which is the state a task constructed outside a thread is in.
        if (IsRunning?.Invoke() == true)
        {
            return RetCode.E_BUSY;
        }

        // [:L62]
        _autoCommit = false;

        // [:L63] the multi-table switch, cleared at the position the oracle clears it. The collection's
        // own Reset below re-asserts it, which is idempotent; writing it here keeps the field order
        // diffable against the oracle rather than folding two statements into one.
        _tables.MultiTableUpdate = false;

        // [:L64]
        _dataObject = string.Empty;

        // [:L65]
        _sqlSyntax = string.Empty;

        // [:L67] `Tables = emptyTables` - the oracle assigns a fresh empty local over the array. The
        // collection's Reset is the same operation and it also re-clears the switch. Its result is
        // DISCARDED because the oracle's assignment has none, and its own busy gate is deliberately
        // unset (see the constructor) so it cannot refuse here.
        _ = _tables.Reset();

        // [:L68] `_blbUpdateData = Blob("")` - deterministic clearing of the payload.
        _updateData = null;

        // [:L69]
        _updateRows = 0L;

        // [:L71]
        return base.Reset();
    }

    /// <summary>
    /// Releases the changeset payload - the <c>onfinalize</c> event [<c>:L406-L408</c>].
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>call super::onfinalize</c> [<c>:L406</c>] has nothing to reach: the substrate DECLARES
    /// <c>onfinalize</c> with no script
    /// [<c>ws_objects/pfw.thread.pbl.src/n_cst_thread_task.sru:L25</c>] and
    /// <c>n_cst_thread_task_sqlbase</c> does not override it, so the ancestor call is a no-op and
    /// <see cref="SqlTaskBase"/> correctly declares no finalize hook to chain to.
    /// </para>
    /// <para>
    /// <b>DETERMINISTIC, NEVER LEFT TO THE COLLECTOR.</b> The payload is the largest thing this task
    /// holds and it has already been applied by the time this runs, so it is released at a defined
    /// point. That is the same discipline <c>docs/PB多线程绕坑提示.md</c> hazard 2 prescribes for
    /// cross-thread references, and it is why the release also happens at <see cref="Reset"/>,
    /// immediately after the payload is applied [<c>:L348</c>], and on disposal.
    /// </para>
    /// </remarks>
    internal void OnFinalize()
    {
        // [:L406]
        _updateData = null;

        // [:L407]
        _updateRows = 0L;
    }

    #endregion


    #region The orchestration - `ondotask` [:L284-L404], in the oracle's order

    /// <summary>
    /// Performs one update - the <c>ondotask</c> event [<c>:L284-L404</c>].
    /// </summary>
    /// <param name="cancellationToken">
    /// The caller's cancellation, unified with the substrate's own <c>of_IsCancelled()</c> poll. See
    /// <see cref="CancellationBridge"/>.
    /// </param>
    /// <returns>
    /// <see cref="RetCode.OK"/> on success; <see cref="RetCode.CANCELLED"/> when cancellation was
    /// observed; otherwise the code of whichever arm failed -
    /// <see cref="RetCode.E_INVALID_TRANSACTION"/>, <see cref="RetCode.E_INVALID_SQL"/>,
    /// <see cref="RetCode.E_INVALID_DATAOBJECT"/>, <see cref="RetCode.E_INVALID_DATA"/>,
    /// <see cref="RetCode.E_INVALID_ARGUMENT"/>, <see cref="RetCode.E_INTERNAL_ERROR"/> or
    /// <see cref="RetCode.E_DB_ERROR"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <c>call super::ondotask</c> [<c>:L284</c>] has nothing to reach: the substrate declares
    /// <c>ondotask</c> with no script
    /// [<c>ws_objects/pfw.thread.pbl.src/n_cst_thread_task.sru:L19</c>], so the ancestor call is a
    /// no-op and <see cref="SqlTaskBase"/> declares no hook to chain to.
    /// </para>
    /// <para>
    /// <b>THIS METHOD NEVER THROWS FOR A DOMAIN OUTCOME AND NEVER PRODUCES AN <c>RpcException</c></b>
    /// (C-G, and the division of labour <c>Concurrency/ConflictDetector.cs</c> documents). Every
    /// failure is a return code plus, where the oracle raises one, an event on the error channel. The
    /// gRPC layer is the throw site; a classifier or a task that threw would decide the transport on
    /// its caller's behalf and would bypass the multi-table loop's own inspection of the returned code
    /// [<c>:L366, :L368</c>].
    /// </para>
    /// </remarks>
    internal long OnDoTask(CancellationToken cancellationToken = default)
    {
        // =========================================================================================
        //  STEP 0 [n_cst_thread_task.sru:L246] - CLEAR THE ERROR LATCH, BEFORE THE ACQUISITION
        //
        //  `of_ClearError()`, the substrate's third statement in of_execute and the one that makes the
        //  epilogue's de-duplication guard mean "already reported ON THIS RUN" rather than "reported at
        //  some point in this object's life". The managed substrate models no of_execute - see point 7 of
        //  this file's header - so this entry point is where the oracle's per-execution clear belongs, and
        //  it must precede STEP 1 because the acquisition arm below is itself a raise site.
        //
        //  The oracle discards the return and so does this call site; the value exists because the
        //  oracle's function has one, not because anything reads it.
        // =========================================================================================
        _ = ClearError();

        // =========================================================================================
        //  STEP 1 [:L292-L296] - ACQUIRE THE TRANSACTION, BEFORE ANYTHING ELSE AND OUTSIDE THE BLOCK
        //
        //  `if IsFailed(of_GetTransObject(ref transObject,ref dbErrData)) then`. Note the two-argument
        //  overload: the oracle wants the DIAGNOSTIC as well as the outcome, because both raises below
        //  carry it. Tested with the tri-state failure predicate exactly as the oracle tests it, so a
        //  prevention - which reads as a success in that algebra - would NOT take this arm.
        // =========================================================================================
        // The two run latches are cleared FIRST, before the transaction is even acquired, so that a run
        // which fails at acquisition cannot report the previous run's classification or diagnostic. They
        // are observation surfaces for the wire layer rather than oracle state, so clearing them here is
        // invisible to behaviour.
        _lastOutcome = null;
        _lastErrorText = string.Empty;

        IPooledTransaction? transaction = null;
        DbErrorData acquisitionError = DbErrorData.Empty;

        if (Predicates.IsFailed(GetTransObject(ref transaction, ref acquisitionError)))
        {
            // [:L293] `Event OnDBError(dbErrData.SQLDBCode,dbErrData.SQLErrText,"",Primary!,0)`.
            // Routed through the error sink so that this file has exactly ONE database-error path and
            // every payload on it passes through ConflictDetector.Redact (C-F) - even this one, whose
            // statement the oracle leaves EMPTY, so that a future arm cannot reach the channel unmasked.
            _errorSink.OnDbError(
                DbErrorData.FromTransaction(acquisitionError.SqlDbCode, acquisitionError.SqlErrText));

            // [:L294] `Event OnError(RetCode.E_INVALID_TRANSACTION,dbErrData.SQLErrText)` - the SAME
            // text on both channels here, unlike the veto arm inside the classifier which passes an
            // empty message on this one. The asymmetry is the oracle's and is preserved (C-B).
            RaiseError(RetCode.E_INVALID_TRANSACTION, acquisitionError.SqlErrText);

            // [:L295] - returns immediately: no carrier was acquired, so there is nothing to tear down
            // and the commit/rollback epilogue is NOT reached. A rollback here would act on a
            // transaction the task never successfully borrowed.
            return RetCode.E_INVALID_TRANSACTION;
        }

        using CancellationBridge cancellation = new(() => IsCancelled, cancellationToken);

        UpdateRun run = new();

        // [:L298] `rtCode = RetCode.OK` - the value the FIRST `exit` at [:L301] leaves in place, which
        // is why that arm returns OK rather than CANCELLED. See point 2 of this file's header.
        long rtCode;

        try
        {
            // [:L300-L373] the `do ... loop while false` single-pass block, expressed as a method with
            // early returns. It is NOT emulated as a loop: nothing here iterates.
            rtCode = RunUpdateBody(transaction, cancellation, run);
        }
        finally
        {
            // [:L375-L379] the teardown runs whatever the body answered, including when it threw. The
            // oracle's teardown sits after the block and is unconditional.
            TeardownCarrier(run);
        }

        // [:L381-L383] `if of_IsCancelled() then rtCode = RetCode.CANCELLED` - AFTER the teardown, and
        // it OVERWRITES whatever the body answered. A cancellation observed only here therefore
        // discards a success as well as a failure.
        if (cancellation.IsCancelled())
        {
            rtCode = RetCode.CANCELLED;
        }

        // [:L385-L403]
        return CompleteUpdate(rtCode, transaction, run.ErrorText);
    }

    /// <summary>
    /// The single-pass body of the orchestration - <c>do ... loop while false</c> [<c>:L300-L373</c>].
    /// </summary>
    /// <param name="transaction">The transaction acquired by step 1.</param>
    /// <param name="cancellation">The unified cancellation source.</param>
    /// <param name="run">The per-run state the epilogue and the teardown read back.</param>
    /// <returns>The code the oracle's <c>rtCode</c> holds when the block exits.</returns>
    private long RunUpdateBody(
        IPooledTransaction? transaction,
        CancellationBridge cancellation,
        UpdateRun run)
    {
        // [:L301] `if of_IsCancelled() then exit`.
        //
        // ⚠️ RETURNS OK, NOT CANCELLED, AND THAT IS NOT A BUG. `exit` breaks the single-pass block
        // leaving `rtCode` at the OK assigned at [:L298]; the rewrite to CANCELLED happens at
        // [:L381-L383], AFTER the teardown. Returning CANCELLED from here would reach the rollback arm
        // of the epilogue by a different route and would report a code the oracle only reaches later.
        if (cancellation.IsCancelled())
        {
            return RetCode.OK;
        }

        // =========================================================================================
        //  STEP 2 [:L303-L314] - ACQUIRE THE CARRIER: THE CACHE FIRST, THEN AFFINITY-KEYED CREATION
        // =========================================================================================

        // [:L303-L306] `if _sDataObject <> "" then bCacheDS = true ; data = of_GetCacheDS(_sDataObject)`.
        //
        // NOTE THE ORDER: the ownership flag is set BEFORE the lookup and is never cleared, so a lookup
        // that answered an invalid object would leave the flag claiming a cached carrier while the
        // branch below created a fresh one - and the teardown would then Reset it instead of destroying
        // it. THAT COMBINATION IS UNREACHABLE, and by construction rather than by luck: the cache
        // ALWAYS answers a valid store, creating one on a miss, and it fires the carrier's init on both
        // paths [n_cst_thread_task_sqlbase.sru:L538-L568, ported at SqlTaskBase.GetCacheDataStore]. The
        // shape is reproduced exactly as written so the reasoning stays checkable.
        if (_dataObject.Length != 0)
        {
            run.CachedCarrier = true;
            run.Carrier = _carrierAdapter.Adapt(GetCacheDataStore(_dataObject));
        }

        // [:L307-L312] `if Not IsValidObject(data) then ... Create n_cst_thread_task_sqlbase_ds[_mt]`.
        //
        // The affinity decision itself is the base's - see SqlTaskBase.CreateDataStore, which is the
        // third of the library's three selection sites and carries the `_mt` trap in full. Reached here
        // whenever no data object was named, which is the syntax-driven path.
        if (!Predicates.IsValidObject(run.Carrier))
        {
            run.Carrier = _carrierAdapter.Adapt(CreateDataStore());

            // [:L313] `data.Event OnInit(this)` - ON THE CREATE PATH ONLY. The cache fires it itself at
            // [n_cst_thread_task_sqlbase.sru:L568], so firing it here as well would initialise a cached
            // carrier twice.
            run.Carrier.Store.OnInit(this);
        }

        ISqlUpdateCarrier carrier = run.Carrier!;

        // =========================================================================================
        //  STEP 3 [:L316-L331] - SHAPE THE CARRIER. Three arms, in this order, and the order matters
        //  because the two source setters clear each other (see SetDataObject's remarks).
        // =========================================================================================
        if (_sqlSyntax.Length != 0)
        {
            // [:L317] `if data.Create(_sSQLSyntax,ref sError) <> 1 then`. THE DATAWINDOW CONVENTION:
            // compared against 1, never through a return-code predicate.
            long createResult = carrier.Create(_sqlSyntax, out string createErrors);

            // The oracle passes `sError` BY REFERENCE, so whatever the driver writes lands in the local
            // the epilogue may later report [:L398] - on success as well as on failure. Assigned
            // unconditionally for that reason.
            run.ErrorText = createErrors;

            if (createResult != DataWindowBufferStore.DataStoreSuccess)
            {
                // [:L318]
                return RetCode.E_INVALID_SQL;
            }
        }
        else if (_dataObject.Length != 0)
        {
            // [:L322-L325] `if Not bCacheDS then data.DataObject = _sDataObject`.
            //
            // SKIPPED FOR A CACHED CARRIER, because the cache assigned the name when it created the
            // entry [n_cst_thread_task_sqlbase.sru:L558] and re-assigning it would re-resolve the
            // definition and discard the sort and filter the cache just restored.
            //
            // ⚠️ THE INNER ASSIGNMENT IS UNREACHABLE BY CONSTRUCTION IN BOTH THE ORACLE AND HERE, AND IS
            // REPRODUCED ANYWAY (C-B). Reaching it needs a non-empty data object - which is exactly what
            // set the ownership flag at [:L304] - together with that flag being false, and the flag is
            // never cleared. The oracle carries the same defensive pair, so removing it would drop a
            // reproduction rather than remove dead weight, and restructuring it would change which of
            // the three shaping arms a future caller lands in. It is therefore the one part of this file
            // no test can cover, and this comment is why.
            if (!run.CachedCarrier)
            {
                carrier.Store.DataObject = _dataObject;
            }
        }
        else
        {
            // [:L327-L328] neither source was set.
            run.ErrorText = InvalidDataObjectMessage;

            return RetCode.E_INVALID_DATAOBJECT;
        }

        // =========================================================================================
        //  STEP 4 [:L333-L334] - DROP THE SORT. LOAD-BEARING, NOT CLEANUP.
        //
        //      //*去掉排序条件,保证数据按拷贝的顺序被提交
        //      data.SetSort("")
        //
        //  "Remove the sort condition so the data is committed in COPY ORDER." Row order determines the
        //  order the update statements are generated in, so a carrier that kept its sort would emit the
        //  same statements in a different sequence - observably different behaviour, and a different
        //  outcome under a unique constraint or a trigger. The result is DISCARDED because the oracle
        //  discards it.
        // =========================================================================================
        _ = carrier.Store.SetSort(string.Empty);

        // =========================================================================================
        //  STEP 5 [:L336-L346] - APPLY THE CHANGESET. TWO ARMS ON FAILURE, AND BOTH MUST SURVIVE.
        // =========================================================================================
        long changesResult = carrier.SetChanges(_updateData);

        // [:L337] `if rtCode <> 1 then`. The oracle reuses its single `rtCode` local for the DataWindow
        // value here; this port holds it separately so the two code spaces cannot be conflated.
        if (changesResult != DataWindowBufferStore.DataStoreSuccess)
        {
            // [:L338-L342] `//无更新数据` - `if rtCode = -1 and _nUpdateRows = 0 then rtCode = OK ; exit`.
            //
            // ⚠️ AN EMPTY UPDATE IS A SUCCESS, NOT AN ERROR. BOTH conjuncts are load-bearing: the result
            // must be exactly -1, and the row count exactly zero. A -1 with a NON-zero count means the
            // payload was expected to carry rows and did not, which is the failure arm below.
            if (changesResult == DataWindowBufferStore.DataStoreFailure && _updateRows == 0L)
            {
                return RetCode.OK;
            }

            // [:L343-L344]
            run.ErrorText = InvalidUpdateDataMessage;

            return RetCode.E_INVALID_DATA;
        }

        // [:L348] `_blbUpdateData = Blob("")` - RELEASED THE MOMENT IT HAS BEEN APPLIED, at the exact
        // point the oracle releases it rather than at the end of the run. Deterministic, never left to
        // the collector.
        _updateData = null;

        // =========================================================================================
        //  STEP 6 [:L350-L354] - ATTACH THE TRANSACTION TO THE CARRIER
        // =========================================================================================
        if (carrier.SetTransObject(transaction) != DataWindowBufferStore.DataStoreSuccess)
        {
            // [:L351-L352]
            run.ErrorText = SetTransObjectFailedMessage;

            return RetCode.E_INVALID_TRANSACTION;
        }

        // =========================================================================================
        //  STEP 7 [:L356-L372] - 🔴 THE BRANCH: PREPARE RUNS ONLY ON THE MULTI-TABLE PATH
        //
        //  `UpdatePreparer.PrepareAndUpdate` owns this branch in full: the switch test [:L356], the
        //  zero-descriptor pre-check with its verbatim diagnostic [:L358-L363], the one-based
        //  PREPARE-THEN-EXECUTE-PER-TABLE ordering [:L364-L369] - so table n+1's prepare runs after
        //  table n's execution - the `<> RetCode.OK` stop test that must NOT be a failure predicate,
        //  and the single-table arm that calls the update DIRECTLY and NEVER PREPARES [:L370-L372].
        //
        //  What is orchestrated here is only the wiring: which metadata and modifier surface the
        //  preparer describes and modifies through, which carrier its key-change refresh walks, which
        //  error channel its diagnostics reach, and what the update step is.
        //
        //  The oracle's `exit` statements break the FOR loop, not the enclosing single-pass block, so a
        //  failure still falls through to the teardown and the epilogue - which is exactly what
        //  returning the code from here does.
        // =========================================================================================
        UpdatePreparer preparer = new(carrier.TargetMetadata, carrier.TargetModifier, RaiseError);

        _logger.LogDebug(
            "Update task driving the {Path} path with {TableCount} descriptor(s).",
            _tables.MultiTableUpdate ? MultiTablePathName : SingleTablePathName,
            _tables.UpperBound);

        long branchResult = preparer.PrepareAndUpdate(
            _tables,
            carrier.Store.Carrier,
            () => ExecuteUpdate(carrier, transaction, cancellation),
            out string branchErrorText);

        // ASSIGNED ONLY WHEN NON-EMPTY, because the oracle's `sError` is a single local that the
        // zero-descriptor arm writes [:L360] and that the prepare and update arms leave ALONE
        // [:L365-L368]. Overwriting it unconditionally would erase whatever `Create` wrote into it at
        // step 3. On the prepare-failure arm the sibling does supply its text, and it is inert there:
        // the preparer has already raised the code through RaiseError, so the epilogue's
        // de-duplication guard suppresses the second report and the text is never read.
        if (branchErrorText.Length != 0)
        {
            run.ErrorText = branchErrorText;
        }

        return branchResult;
    }

    /// <summary>
    /// One invocation of <c>_of_Update(data)</c> [<c>:L172-L252</c>], delegated whole to
    /// <see cref="ConflictDetector.Classify"/>.
    /// </summary>
    /// <param name="carrier">The shaped carrier.</param>
    /// <param name="transaction">The attached transaction.</param>
    /// <param name="cancellation">The unified cancellation source.</param>
    /// <returns>The classified outcome's return code.</returns>
    /// <remarks>
    /// <para>
    /// <b>NOTHING IN HERE DECIDES ANYTHING.</b> The classifier owns the transaction-less arm
    /// [<c>:L180</c>], both cancellation checks [<c>:L182, :L212</c>], the state clear [<c>:L186</c>],
    /// the three update-table sentinels and their TWO raises [<c>:L188-L193</c>], the veto
    /// discrimination [<c>:L195-L202</c>], the accept-text-true/reset-flag-false invocation
    /// [<c>:L204</c>], the after-update hook firing BEFORE the override [<c>:L206</c>], the defensive
    /// override [<c>:L208-L210</c>], the success-is-literally-1 gate [<c>:L214</c>], the delegated
    /// identity round trip [<c>:L215-L247</c>] and the else arm [<c>:L249-L250</c>]. This method
    /// assembles the inputs and publishes the two caller-side events.
    /// </para>
    /// <para>
    /// Invoked ONCE on the single-table path and once PER DESCRIPTOR on the multi-table path, which is
    /// why the per-invocation publication below sums nothing.
    /// </para>
    /// </remarks>
    private long ExecuteUpdate(
        ISqlUpdateCarrier carrier,
        IPooledTransaction? transaction,
        CancellationBridge cancellation)
    {
        // Bridge the substrate's discrete `of_IsCancelled()` poll into the token IMMEDIATELY BEFORE the
        // classification, so that the classifier's own two checks [:L182, :L212] observe a cancellation
        // the host has already signalled. See CancellationBridge for why the two sources are unified
        // rather than consulted separately.
        _ = cancellation.IsCancelled();

        UpdateAttempt attempt = new()
        {
            // [:L180] a null transaction is the classifier's InvalidTransaction arm, which is the same
            // arm `IsFailed(of_GetTransObject(...))` takes. Step 1 of OnDoTask has already guaranteed a
            // non-null transaction on every reachable path; the conditional is defensive and honest
            // rather than decorative, because the classifier's contract admits null.
            Transaction = transaction is null
                ? null
                : new PooledUpdateTransaction(transaction, _transactionHooks),
            Target = carrier.Target,
            Identity = carrier.Identity,
            Errors = _errorSink,
            Cancellation = cancellation.Token,

            // The optimistic-concurrency measurement, read ONCE here because the attempt is an
            // immutable input. Null - the oracle's own state, since it measures nothing anywhere - keeps
            // the failure arm a plain database error. See ISqlUpdateCarrier.CaptureConcurrencyEvidence.
            Evidence = carrier.CaptureConcurrencyEvidence(),
        };

        UpdateOutcome outcome = _conflictDetector.Classify(attempt);

        // LATCHED FOR THE CALLER-SIDE SURFACE, AND LAST-WINS IS THE ORACLE'S SHAPE. On the multi-table
        // path the loop stops at the first failure [:L366, :L368], so the LAST attempt is the one that
        // decided the run - which is exactly what the published contract's classification field carries.
        _lastOutcome = outcome;

        PublishOutcome(outcome);

        return outcome.Code;
    }

    /// <summary>
    /// The classification of the most recent update attempt, or <see langword="null"/> when none was made.
    /// </summary>
    /// <value>
    /// <see langword="null"/> is a legitimate and common state rather than a fault: every arm of the run
    /// that returns before <c>_of_Update</c> [<c>:L292-L363</c>] leaves nothing to classify. On the
    /// multi-table path this carries the LAST attempt's classification, because the loop stops at the
    /// first failure and therefore the last attempt is the one that decided the run.
    /// </value>
    /// <remarks>
    /// READ BY THE CALLER-SIDE SURFACE, WRITTEN ONLY BY THE CLASSIFIER'S CALL SITE. It is deliberately not
    /// cleared by <see cref="Reset"/>: the oracle's reset clears the task's INPUTS [<c>:L58-L72</c>] and
    /// says nothing about a classification, and clearing it here would make a caller that read its result
    /// after resetting for the next run see nothing where the oracle shows the previous outcome.
    /// </remarks>
    /// <remarks>
    /// AN OBSERVATION SEAM, AND A NECESSARY ONE RATHER THAN A CONVENIENCE. It follows the same shape as
    /// <see cref="GetUpdateRows"/> and <see cref="GetLastErrorCode"/>: the oracle keeps the equivalent in
    /// private fields and publishes it through events, and the caller-side surface this task sits behind
    /// has to answer with the whole classification rather than only its code. A null value therefore means
    /// "no update has run", which is distinct from an update that ran and failed - that one carries an
    /// outcome whose kind says so, and a caller must not read the absence as a success.
    /// </remarks>
    internal UpdateOutcome? LastOutcome => _lastOutcome;

    /// <summary>
    /// The terminal diagnostic the last run latched, or the empty string when none was recorded.
    /// </summary>
    /// <value>
    /// EMPTY IS THE NORMAL STATE AND NOT AN OMISSION: a run that succeeded has nothing to report, and the
    /// wire layer distinguishes "no text" from "no run" by reading <see cref="LastOutcome"/> beside it.
    /// An outcome-carried text is preferred over a branch text wherever both exist, because the classifier
    /// produced it and the branch texts are set only on arms the classifier never reaches.
    /// </value>
    internal string LastErrorText => _lastErrorText;

    /// <summary>
    /// Fires the caller-side proxy's two events for one classified invocation -
    /// <c>tasking.Event OnIdentityColumnDataRetrieved(...)</c> [<c>:L243</c>] then
    /// <c>tasking.Event OnUpdated(...)</c> [<c>:L247</c>].
    /// </summary>
    /// <param name="outcome">The classified outcome.</param>
    /// <remarks>
    /// <para>
    /// <b>SUCCESS ONLY, AND IN THE ORACLE'S ORDER.</b> Both calls sit inside the
    /// <c>if rtCode = 1 then</c> arm [<c>:L214</c>], so a cancellation observed after the update
    /// [<c>:L212</c>] publishes NOTHING even for rows that were written, and the identity report
    /// precedes the counts report.
    /// </para>
    /// <para>
    /// ⚠️ <b>NOTHING IS ACCUMULATED HERE.</b> The identity block list the resolver answers holds AT MOST
    /// ONE element per invocation, and the emit guard - at least one of the two arrays non-empty
    /// [<c>:L242</c>] - has already been applied by the resolver, which answers an EMPTY list when it
    /// declines. The counts fire on every success because the oracle's call sits outside the
    /// inserted-count block. Summing across the multi-table loop belongs to
    /// <c>Tasks/TaskProxies/</c> [<c>n_cst_threading_task_sqlupdate.sru:L66-L69, :L71-L77</c>].
    /// </para>
    /// </remarks>
    private void PublishOutcome(UpdateOutcome outcome)
    {
        // [:L214] - the whole publication is inside the success arm.
        if (outcome.Kind != UpdateOutcomeKind.Succeeded)
        {
            return;
        }

        if (outcome.Identity is not { } identity)
        {
            // ⚠️ UNREACHABLE, AND DELIBERATELY NOT A DEREFERENCE. UpdateOutcome.Succeeded requires the
            // resolution and rejects null for it, so a Succeeded outcome always carries one; the member
            // is nullable because the OTHER four outcome kinds carry no resolution at all. Answering
            // rather than dereferencing keeps a contract change from becoming a null-reference fault at
            // the point a caller-side event would have fired. Like the shaping arm above, it is
            // uncoverable by construction, and that is the reason.
            return;
        }

        // `tasking = #ParentTasking` [:L184] followed by unconditional triggers. An unattached task has
        // no proxy to receive them, checked with the same predicate the base uses for its own event.
        ISqlUpdateTaskProxy? proxy = _updateProxy;

        if (!Predicates.IsValidObject(proxy))
        {
            return;
        }

        // [:L243] at most one block, already guarded by the resolver.
        foreach (ResolvedIdentityColumnData block in identity.Identity)
        {
            proxy!.OnIdentityColumnDataRetrieved(
                block.IdentityColumnId,
                block.PrimaryValues,
                block.FilterValues);
        }

        // [:L247] on EVERY success - a pure update and a pure delete included.
        proxy!.OnUpdated(
            identity.Counts.Inserted,
            identity.Counts.Updated,
            identity.Counts.Deleted);
    }

    #endregion


    #region The epilogue - teardown [:L375-L379], then commit or rollback [:L385-L401]

    /// <summary>
    /// Releases the carrier - <c>if bCacheDS then data.Reset() else if IsValid(data) then Destroy data</c>
    /// [<c>:L375-L379</c>].
    /// </summary>
    /// <param name="run">The per-run state carrying the carrier and its ownership.</param>
    /// <remarks>
    /// ⚠️ <b>NEVER DESTROY A CACHED CARRIER - RESET IT.</b> The datastore cache in
    /// <see cref="SqlTaskBase"/> holds LIVE references keyed by data-object name, published into the
    /// owning thread's keyed data [<c>n_cst_thread_task_sqlbase.sru:L531-L537</c>], so destroying a
    /// cached carrier leaves a dangling entry that every later task on the same thread would resolve
    /// and use. Resetting it clears the rows and leaves the entry sound, which is exactly why the cache
    /// exists.
    /// </remarks>
    private void TeardownCarrier(UpdateRun run)
    {
        ISqlUpdateCarrier? carrier = run.Carrier;

        if (run.CachedCarrier)
        {
            // [:L375-L376] `data.Reset()`. NOT disposed: the cache still owns it. Guarded on validity
            // because the ownership flag is set before the lookup [:L304] and the body may have returned
            // before a carrier existed at all - the oracle reaches its unguarded `data.Reset()` only
            // through a path where the cache has already answered.
            if (Predicates.IsValidObject(carrier))
            {
                _ = carrier!.Store.Carrier.Reset();

                _logger.LogDebug("Update task reset its CACHED carrier for data object {DataObject}.", _dataObject);
            }
        }
        else if (Predicates.IsValidObject(carrier))
        {
            // [:L378] `if IsValid(data) then Destroy data` - the ONLY arm that disposes, because it is
            // the only arm that owns.
            carrier!.Dispose();

            _logger.LogDebug("Update task disposed its OWNED carrier.");
        }

        // Hazard 2 of docs/PB多线程绕坑提示.md, applied to the run's own handle: the reference is dropped
        // at a defined point rather than left for the collector, so a torn-down carrier cannot be
        // reached again through this state object.
        run.Carrier = null;
    }

    /// <summary>
    /// Commits or rolls back and reports - <c>[:L385-L403]</c>.
    /// </summary>
    /// <param name="rtCode">The code the body answered, after the cancellation rewrite.</param>
    /// <param name="transaction">The transaction acquired by step 1.</param>
    /// <param name="errorText">The oracle's <c>sError</c> local as the body left it.</param>
    /// <returns>The final code - which the commit may have REPLACED [<c>:L387</c>].</returns>
    /// <remarks>
    /// <para>
    /// <b>THE SUCCESS TEST IS EXACT EQUALITY WITH <see cref="RetCode.OK"/>, NOT A PREDICATE</b>
    /// [<c>:L385</c>]. Under the published tri-state algebra <c>IsSucceeded(RetCode.PREVENT)</c> is
    /// TRUE, so a predicate here would commit on a prevention; and <c>IsFailed(RetCode.CANCELLED)</c> is
    /// FALSE, so a predicate on the other side would let a cancellation fall through to the commit arm.
    /// </para>
    /// <para>
    /// <b>THE COMMIT'S RESULT OVERWRITES <c>rtCode</c></b> [<c>:L387</c>], so a successful update whose
    /// commit failed answers the COMMIT's code, and that is what the caller sees.
    /// </para>
    /// <para>
    /// ⚠️ <b>THE DE-DUPLICATION GUARD.</b> <c>if rtCode &lt;&gt; of_GetLastErrorCode() then</c> appears
    /// on BOTH arms [<c>:L389, :L397</c>] and suppresses a second report when the failure has already
    /// been reported - which the prepare does at [<c>:L120, :L147</c>] and the classifier does on its
    /// sentinel and veto arms. Without it the same failure reaches a consumer twice.
    /// </para>
    /// <para>
    /// <b>AND NOTE THE ASYMMETRY</b> [<c>:L394-L400</c>]: a cancellation IS rolled back but is NEVER
    /// reported as an error. A cancellation is not a failure in this algebra, and reporting it would
    /// turn an orderly stop into a fault.
    /// </para>
    /// </remarks>
    private long CompleteUpdate(long rtCode, IPooledTransaction? transaction, string errorText)
    {
        // Latched here because this is the ONE place every arm of the run converges on with its terminal
        // diagnostic in hand. An outcome-carried text is preferred when the classifier produced one,
        // because it is the more specific of the two - the branch texts above it are set only on arms the
        // classifier never reaches.
        _lastErrorText = _lastOutcome is { ErrorText.Length: > 0 } classified
            ? classified.ErrorText
            : errorText;

        // [:L385] EXACT equality. See the remarks.
        if (rtCode == RetCode.OK)
        {
            // [:L386]
            if (_autoCommit)
            {
                // [:L387] `rtCode = of_Commit(true)` - AUTO-ROLLBACK TRUE, and the result REPLACES the
                // code. The base's Commit also propagates the commit signal backwards to sibling tasks,
                // which is the behaviour `of_Commit` carries at
                // [n_cst_thread_task_sqlbase.sru, its commit function and its oncommitted event].
                rtCode = Commit(autoRollback: true);

                // [:L388] the tri-state failure predicate, deliberately: a commit that answered a
                // prevention is NOT a failure here.
                if (Predicates.IsFailed(rtCode))
                {
                    // [:L389] the de-duplication guard.
                    if (rtCode != GetLastErrorCode())
                    {
                        // [:L390] `Event OnError(rtCode,transObject.SQLErrText)` - THE TRANSACTION'S OWN
                        // text on this arm, not the body's `sError`, because the failure came from the
                        // commit rather than from anything the body did.
                        RaiseError(rtCode, transaction?.SqlErrText ?? string.Empty);
                    }
                    else
                    {
                        _logger.LogDebug(
                            "Update task suppressed a duplicate commit-failure report for code {Code}.",
                            rtCode);
                    }
                }
            }
        }
        else
        {
            // [:L395] `transObject.of_Rollback()` - ON THE TRANSACTION OBJECT DIRECTLY, and NOT through
            // the task's own rollback. They reach the same object here, but the oracle's choice is
            // preserved: the task's rollback additionally validates its own attachment and would answer
            // E_INVALID_TRANSACTION for a detached task, which is not the arm this line takes. The
            // result is DISCARDED because the oracle discards it - an unrollbackable transaction does
            // not change the code the caller sees.
            _ = transaction?.Rollback();

            // [:L396] a cancellation is rolled back but never reported.
            if (rtCode != RetCode.CANCELLED)
            {
                // [:L397] the same de-duplication guard as the commit arm.
                if (rtCode != GetLastErrorCode())
                {
                    // [:L398] `Event OnError(rtCode,sError)` - the BODY's text on this arm.
                    RaiseError(rtCode, errorText);
                }
                else
                {
                    _logger.LogDebug(
                        "Update task suppressed a duplicate failure report for code {Code}.",
                        rtCode);
                }
            }
        }

        // [:L403]
        return rtCode;
    }

    #endregion

    #region The error channel - the substrate's latch, reproduced where every raise passes through

    /// <summary>
    /// Raises the framework error event - <c>Event OnError(errCode, errInfo)</c.
    /// </summary>
    /// <param name="errCode">The framework return code.</param>
    /// <param name="errorText">The untranslated diagnostic.</param>
    /// <remarks>
    /// The single internal spelling of a raise, so that this file, the error sink handed to
    /// <see cref="ConflictDetector"/> and the callback handed to <see cref="UpdatePreparer"/> all reach
    /// the same virtual method - which is what makes the latch in <see cref="OnError"/> complete. The
    /// hook's own return value is discarded here because none of the oracle's raise sites in this object
    /// inspects it [<c>:L120, :L147, :L191, :L198, :L294, :L390, :L398</c>].
    /// </remarks>
    private void RaiseError(long errCode, string errorText) => _ = OnError(errCode, errorText);

    /// <summary>
    /// Latches the reported code and chains to the base - the substrate's
    /// <c>_lastErrCode = errCode</c>
    /// [<c>ws_objects/pfw.thread.pbl.src/n_cst_thread_task.sru:L189</c>].
    /// </summary>
    /// <param name="errCode">The framework return code.</param>
    /// <param name="errInfo">The diagnostic text.</param>
    /// <returns>Whatever the base answers, unchanged.</returns>
    /// <remarks>
    /// <para>
    /// <b>THE LATCH BELONGS HERE BECAUSE OF WHERE THE ORACLE PUTS IT.</b> PowerScript latches inside the
    /// SUBSTRATE's error event, so EVERY raise in the inheritance chain passes through it whichever
    /// override started it. <see cref="ISqlTaskHost"/> models no such member - it is the substrate seam
    /// and adding one would oblige every implementor to answer it - so the same universality is achieved
    /// by overriding the virtual hook at the most-derived level: every raise, including the base's own,
    /// dispatches here first.
    /// </para>
    /// <para>
    /// <b>THE LATCH PRECEDES THE CHAIN</b>, matching the oracle's order [<c>:L189-L190</c>] then its
    /// forward at [<c>:L195</c>]. It matters: the base's own override rolls the transaction back before
    /// forwarding [<c>n_cst_thread_task_sqlbase.sru:L731-L734</c>], and a latch written after that would
    /// record a code the rollback might already have superseded.
    /// </para>
    /// <para>
    /// Only the CODE is latched. The oracle also latches the text into <c>_lastErrInfo</c>
    /// [<c>:L190</c>], but nothing in this object reads <c>of_GetLastErrorInfo()</c>, and holding a value
    /// no one reads is a field that can only drift.
    /// </para>
    /// </remarks>
    protected override long OnError(long errCode, string errInfo)
    {
        _lastErrorCode = errCode;

        return base.OnError(errCode, errInfo);
    }

    #endregion

    #region Disposal - the .NET half of the deterministic-clearing contract

    /// <inheritdoc/>
    /// <remarks>
    /// Releases the changeset payload before chaining, so the largest thing this task holds is gone at a
    /// defined point on every exit path - the same discipline
    /// <c>docs/PB多线程绕坑提示.md</c> hazard 2 prescribes, and the fourth place this file applies it
    /// after [<c>:L348</c>], [<c>:L68</c>] and [<c>:L406</c>]. The carrier is NOT touched here: it is
    /// owned per run and is already released by <see cref="TeardownCarrier"/>, which is the only code
    /// that knows whether this task owned it.
    /// </remarks>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _updateData = null;
            _updateRows = 0L;
        }

        base.Dispose(disposing);
    }

    #endregion

    #region The private collaborators - the run state, the error sink and the cancellation bridge

    /// <summary>The log label for the multi-table branch.</summary>
    private const string MultiTablePathName = "multi-table";

    /// <summary>The log label for the single-table branch.</summary>
    private const string SingleTablePathName = "single-table";

    /// <summary>
    /// The mutable state of one <see cref="OnDoTask"/> run - the oracle's <c>data</c>, <c>bCacheDS</c>
    /// and <c>sError</c> locals [<c>:L286-L288</c>].
    /// </summary>
    /// <remarks>
    /// Gathered into one object rather than plumbed as <c>ref</c> parameters so that the body, the
    /// teardown and the epilogue read the same three values without the signature noise - and so that a
    /// test can inspect what a partially completed run left behind. It is per-run and never shared, so
    /// it carries no synchronization.
    /// </remarks>
    private sealed class UpdateRun
    {
        /// <summary>
        /// The acquired carrier - <c>n_cst_thread_task_sqlbase_ds data</c> [<c>:L288</c>].
        /// </summary>
        internal ISqlUpdateCarrier? Carrier { get; set; }

        /// <summary>
        /// Whether the carrier came from the datastore cache - <c>boolean bCacheDS</c> [<c>:L287</c>].
        /// </summary>
        /// <remarks>
        /// Set BEFORE the lookup and never cleared, exactly as the oracle sets it [<c>:L304</c>]. It is
        /// the sole input to the teardown's reset-versus-destroy decision [<c>:L375</c>].
        /// </remarks>
        internal bool CachedCarrier { get; set; }

        /// <summary>
        /// The diagnostic the epilogue may report - <c>string sError</c> [<c>:L286</c>].
        /// </summary>
        /// <remarks>
        /// Written by the syntax build [<c>:L317</c>], the two shaping failures [<c>:L327, :L343</c>],
        /// the transaction attach [<c>:L351</c>] and the zero-descriptor pre-check [<c>:L360</c>]; read
        /// once, at [<c>:L398</c>], and only when the de-duplication guard admits the report.
        /// </remarks>
        internal string ErrorText { get; set; } = string.Empty;
    }

    /// <summary>
    /// The two error channels the sibling algorithms raise through, forwarded onto this task's own
    /// events.
    /// </summary>
    /// <param name="owner">The task that owns the channels.</param>
    /// <remarks>
    /// <para>
    /// A nested type rather than an interface implemented by the task itself, for two reasons. The
    /// database-error channel would otherwise force a public
    /// <c>OnDbError(in DbErrorData)</c> beside the base's five-argument
    /// <see cref="SqlTaskBase.OnDbError"/>, and the framework-error channel would collide with the
    /// protected virtual <see cref="OnError"/> that the latch depends on.
    /// </para>
    /// <para>
    /// <b>C-F.</b> Every payload crossing this channel is passed through
    /// <see cref="ConflictDetector.Redact"/> - the one sanctioned redaction path in the service, which
    /// masks the statement member and touches nothing else. Redaction is idempotent, so a payload the
    /// classifier already masked is unchanged by the second pass; what the second pass buys is that no
    /// producer can reach the channel unmasked.
    /// </para>
    /// </remarks>
    private sealed class UpdateErrorSink(SqlUpdateTask owner) : IUpdateErrorSink
    {
        /// <inheritdoc/>
        public void OnDbError(in DbErrorData error)
        {
            DbErrorData masked = owner._conflictDetector.Redact(error);

            // The base's five-argument event is the oracle's `Event OnDBError(...)`
            // [n_cst_thread_task_sqlbase.sru:L85-L98]: it publishes the payload to the caller-side proxy
            // and logs it. Its result - the preserved literal 3 - is discarded because no raise site in
            // the update object inspects it [:L190, :L197, :L293].
            _ = owner.OnDbError(
                masked.SqlDbCode,
                masked.SqlErrText,
                masked.SqlSyntax,
                masked.Buffer,
                masked.Row);
        }

        /// <inheritdoc/>
        public void OnError(long code, string errorText) => owner.RaiseError(code, errorText);
    }

    /// <summary>
    /// Unifies the caller's cancellation token with the substrate's <c>of_IsCancelled()</c> poll into
    /// one source, so that every legacy poll site and the classifier's own two checks agree.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>WHY A BRIDGE IS NEEDED AT ALL.</b> The oracle polls a predicate
    /// [<c>:L301, :L381</c>, and inside the classifier at <c>:L182, :L212</c>], whereas
    /// <see cref="ConflictDetector"/> and the <c>Buffers/</c> codecs take a
    /// <see cref="CancellationToken"/> - which is the right shape for .NET and the wrong shape to poll a
    /// property with. This type owns the join: it polls the predicate on demand and, the first time the
    /// predicate answers true, cancels a source linked to the caller's token so that both spellings
    /// agree from then on.
    /// </para>
    /// <para>
    /// <b>WHAT IT DOES NOT PROMISE.</b> A host whose cancellation is not itself token-backed is observed
    /// at the NEXT poll rather than instantaneously. That is not a narrowing: the oracle's own polls are
    /// equally discrete, and in the composed system the substrate's flag and the caller's token are the
    /// same request-scoped cancellation, so the two are already one thing.
    /// </para>
    /// <para>
    /// It reads NO clock. There is no timer, no deadline and no <see cref="TimeProvider"/> use anywhere
    /// in this type, which is what keeps every cancellation arm deterministic in a test (C-H).
    /// </para>
    /// </remarks>
    private sealed class CancellationBridge : IDisposable
    {
        private readonly Func<bool> _hostPoll;
        private readonly CancellationTokenSource _source;

        /// <summary>
        /// Creates the bridge.
        /// </summary>
        /// <param name="hostPoll">The substrate's <c>of_IsCancelled()</c> predicate.</param>
        /// <param name="callerToken">The caller's cancellation token.</param>
        /// <exception cref="ArgumentNullException"><paramref name="hostPoll"/> is <see langword="null"/>.</exception>
        internal CancellationBridge(Func<bool> hostPoll, CancellationToken callerToken)
        {
            ArgumentNullException.ThrowIfNull(hostPoll);

            _hostPoll = hostPoll;
            _source = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
        }

        /// <summary>The unified token, which is what the siblings are handed.</summary>
        internal CancellationToken Token => _source.Token;

        /// <summary>
        /// Polls both sources - the port of <c>of_IsCancelled()</c> at every site this file reaches it.
        /// </summary>
        /// <returns><see langword="true"/> when cancellation has been requested by either source.</returns>
        internal bool IsCancelled()
        {
            if (_source.IsCancellationRequested)
            {
                return true;
            }

            if (!_hostPoll())
            {
                return false;
            }

            // Latch the host's answer into the token so the siblings see it too. Safe to call while the
            // source is alive, and this is the only place it is called from.
            _source.Cancel();

            return true;
        }

        /// <inheritdoc/>
        public void Dispose() => _source.Dispose();
    }

    #endregion
}

#endregion
