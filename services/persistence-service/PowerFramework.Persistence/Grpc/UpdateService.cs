// ==================================================================================================
//  Grpc/UpdateService.cs - CONTRACT C-06, persistence.v1.UpdateService
//  ------------------------------------------------------------------------------------------------
//  ROLE: A THIN ADAPTER, AND THE DESIGNATED RpcException(Aborted) THROW SITE.
//  Validate, translate, delegate, map. Every behaviour this file appears to have lives in a sibling
//  folder, and the division of labour is deliberate rather than incidental:
//
//      the six-field descriptor, its ordered collection and its
//      three-arm admission test                         ->  Concurrency/UpdateWhereBuilder.cs
//      every arm of `_of_update` - the three sentinels,
//      the veto discrimination, the two cancellation
//      checks, success-is-1, the defensive override and
//      the conflict narrowing                           ->  Concurrency/ConflictDetector.cs
//      the identity round trip and its two walks        ->  Concurrency/IdentityColumnResolver.cs
//      the orchestration, the branch and the epilogue    ->  Tasks/SqlUpdateTask.cs
//      the accumulators the response reports             ->  Tasks/TaskProxies/SqlUpdateTaskProxy.cs
//      the driver-error payload and its redaction        ->  Errors/DbErrorData.cs, Errors/SqlRedactor.cs
//      the changeset payload and its structural checks   ->  Buffers/ChangesetCodec.cs
//
//  What is genuinely THIS file's own is the boundary itself: the wire descriptor mapping with its
//  presence semantics, the handle-to-task correlation an in-process library never needed, the ONE
//  auditable place where the in-process return-code algebra becomes the wire enum, and the faithful
//  status-and-payload mapping - including the one status this service raises rather than reports.
//
//  WHY THIS SERVICE IS gRPC (constraint C-K)
//  Four properties of the legacy surface push the same way, and none of them is a preference.
//    1. IT IS ACTION-ORIENTED AND THEREFORE RPC-SHAPED, NOT RESOURCE-ORIENTED. Read the oracle's own
//       public prototypes and there is no noun to GET or PUT - of_reset, of_setupdatedata,
//       of_addupdatabletable, of_setautocommit, of_setdataobject, of_setmultitableupdate,
//       of_setsqlsyntax [ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru:L43-L52].
//       Those are verbs on a stateful task.
//    2. THREAD AFFINITY IS ENCODED IN THE SOURCES THEMSELVES, not inferred: the worker declares
//       `[运行在子线程]` - runs on the worker thread [:L2] - while its caller-side twin declares
//       `[运行在当前线程]` - runs on the calling thread
//       [ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlupdate.sru:L2]. The proxy pair is a
//       contract, and a strongly typed boundary is what keeps the two halves distinguishable.
//    3. THE ERROR PAYLOAD IS STRUCTURED, NOT A MESSAGE STRING. Five members, one of them an enum and
//       one a row ordinal [ws_objects/pfw.thread.ext.pbl.src/dberrordata.srs:L4-L8]. Flattening it
//       would discard the driver's numeric code, the offending buffer and the offending row.
//    4. THE MANDATED CONFLICT RESPONSE REQUIRES RICH STATUS DETAIL. A concurrency mismatch has to
//       carry a whole ConflictDetail - every conflicting row, with BOTH its current and its original
//       values - alongside a status code. Protocol buffers over gRPC carry all four of these with
//       compile-time contract enforcement; JSON over REST carries none of them well.
//
//  ============ Aborted -> HTTP 409, STATED EXPLICITLY BECAUSE THIS FILE IS WHERE IT ORIGINATES ====
//  On an optimistic-concurrency mismatch this file raises the gRPC status ABORTED - canonical numeric
//  code 10 - carrying common.v1.ConflictDetail inside the versioned binary trailer the Update method's
//  own descriptor names. Gateway's REST projection surfaces THE SAME payload as HTTP 409, and callers
//  implement an explicit retry-or-surface policy.
//
//  ABORTED RATHER THAN FAILED_PRECONDITION, deliberately: the operation MAY succeed if the caller
//  re-reads and retries at a higher level, which is exactly what Aborted means and what
//  FailedPrecondition does not. Aborted is also the canonical gRPC mapping to 409.
//
//  THE DETAIL TRAVELS ON THE STATUS AND NOT AS A RESPONSE FIELD. UpdateResponse has no conflict
//  member and must never grow one: two ways to report one condition lets a consumer handle one and
//  miss the other. THERE IS NO SILENT OVERWRITE ANYWHERE IN THIS SYSTEM, and no line below could
//  produce one - this file never re-attempts an update and never rewrites a conflict into a success.
//  ==========================================================================================
//
//  LEGACY SPECIFICATION (READ ONLY - never edited, never moved, never reformatted: constraint C-C)
//  Every behavioural claim in the comments below carries a ws_objects/** locator, because nothing else
//  in this repository can adjudicate it: the changelog stops at framework 3.0.7.2062 while the commit
//  history runs years later, and neither PowerBuilder project object would build as written.
//      ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru      the worker, 409 lines
//      ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlupdate.sru   the caller-side shape
//      ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlbase.sru     the shared busy guards
//      ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru        the worker substrate
//      ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru               the failure predicate
//      ws_objects/pfw.thread.ext.pbl.src/dberrordata.srs                      the driver-error payload
//      ws_objects/pfw.tests.pbl.src/dw_sqlite.srd                             the golden-master fixture
//      ws_objects/pfw.shared.pbl.src/retcode.sru                              the return-code algebra
//  LOCATOR CONVENTION: an unqualified [:Lnnn] names n_cst_thread_task_sqlupdate.sru, this contract's
//  primary oracle. Every reference to any other file is spelled in full, every time.
//
//  PRESERVED SEMANTICS, EACH ANNOTATED AT ITS POINT OF REPRODUCTION (constraint C-B)
//      the six-field declaration order                       ToDescriptorArguments, PrepareUpdate
//      absence versus value on the two optional settings      ToDescriptorArguments, PrepareUpdate
//      the three-arm admission test, empty identity legal     PrepareUpdate (delegated, not restated)
//      the multi-table flag and the indexed prepare           PrepareUpdate
//      the single-table path that never prepares              PrepareUpdate remarks
//      the wholesale replacement of the descriptor array      PrepareUpdate
//      additive counts, one identity block per table          Update
//      the forward-Primary / backward-Filter collection order Update (serialized as collected)
//      the veto / transaction-failure discrimination          ProjectStatus
//      success is literally 1, not the algebra's zero         Update remarks, ProjectStatus
//      the defensive override at SQL code exactly -1          Update remarks
//      the three update-table sentinels                       ProjectStatus
//      the E_BUSY guard on every mutator                      every mutating RPC
//      the boolean autocommit, unlike C-07's tri-valued enum  Update
//
//  WHAT THIS FILE DELIBERATELY DOES NOT DO
//    * It declares NO route, NO health endpoint and NO ping endpoint. /health is this service's only
//      anonymous surface and both routes live in Endpoints/, on port 5101 (constraint C-L).
//    * It adds NO <Protobuf> item to the project. shared/PowerFramework.Contracts owns the single
//      GrpcServices="Both" item; a second would compile the messages twice (constraint C-A).
//    * It references NO peer service project and exposes NO shared behaviour across the boundary. The
//      published contracts project is the only permitted cross-service coupling (constraint C-A).
//    * It mints NO token and holds NO signing key. Security is the sole issuer; this service holds
//      verification material only (constraints C-F, C-G).
//    * It opens NO connection to SQL Server or Oracle, and reads no connection string of any kind.
//      Only SQLite has evidence in this repository (constraint C-E).
//    * It declares NO route, handler, options type or placeholder for DesignSystem, Documents,
//      Integration or ScriptBridge, and throws NO NotImplementedException. Those four reserved routes
//      are declarations on GATEWAY's routing table alone (constraint C-D).
//    * It DECLARES no SCREAMING_SNAKE constant. This folder is outside every naming-suppression glob
//      in the repository .editorconfig while TreatWarningsAsErrors is true, so the preserved legacy
//      identifiers are CONSUMED from PowerFramework.Shared.Kernel and from the generated contract
//      types, never redeclared here - and no type below shadows DwBuffer, ItemStatus, DbError or
//      ConflictDetail.
//    * It never assigns DbError.Sqlsyntax, and it never sees an unredacted statement at all: both of
//      its database-error sources hand it an ALREADY-PROJECTED wire message (constraint C-F).
//    * It re-implements NO status mapping. The conflict projection is taken whole from
//      Concurrency/ConflictDetector.cs's one sanctioned helper, and no second conflict payload is
//      built anywhere below.
//
//  NO RULES GOVERN THIS FILE. review_rules reports that no user rules were provided, and that single
//  line is the complete document. That absence is not latitude: the enterprise-standard baseline
//  applies in their place - nullable reference types with warnings as errors, every collaborator
//  injected, no secret in source or settings, structured logging with the statement field redacted,
//  and versioned contracts as the only cross-service coupling. No rule is invented or back-filled.
// ==================================================================================================

using System.Collections.Concurrent;
using System.Globalization;
using Grpc.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PowerFramework.Contracts.Common.V1;
using PowerFramework.Contracts.Persistence.V1;
using PowerFramework.Persistence.Concurrency;
using PowerFramework.Persistence.Configuration;
using PowerFramework.Persistence.Data;
using PowerFramework.Persistence.Errors;
using PowerFramework.Persistence.Runtime;
using PowerFramework.Shared.Diagnostics;

// The generated C-06 service base, reached through an alias for two reasons. First, the mandated class
// name below is the contract's own service name, so within this file the bare name `UpdateService`
// resolves to the class declared here rather than to the generated static wrapper of the same name,
// and the alias is what makes the derivation unambiguous instead of resting on a scoping rule.
// Second, an alias puts the derivation in view at the class declaration rather than hiding a fully
// qualified name in it. This is the shape the sibling adapters use - see Grpc/TransactionService.cs
// and services/dataservices-service/.../Grpc/DataWindowService.cs.
using GeneratedUpdateServiceBase =
    global::PowerFramework.Contracts.Persistence.V1.UpdateService.UpdateServiceBase;

// THE NAME RetCode COLLIDES ACROSS THE TWO CODE SPACES THIS FILE STRADDLES, AND BOTH ARE NEEDED.
//   * RetCode is PowerFramework.Shared.Kernel.RetCode, a static class of `long` constants. It is the
//     IN-PROCESS algebra - what every collaborator below returns and what every guard tests.
//   * WireRetCode is PowerFramework.Contracts.Common.V1.RetCode.Types.Value, the generated PROTO ENUM.
//     It is the WIRE form and it appears in exactly one place: UpdateWireCodes.
// Importing PowerFramework.Contracts.Common.V1 above brings the wrapper MESSAGE named RetCode into
// scope, so without these aliases the bare name would be ambiguous at every use site.
using RetCode = PowerFramework.Shared.Kernel.RetCode;
using WireRetCode = global::PowerFramework.Contracts.Common.V1.RetCode.Types.Value;

namespace PowerFramework.Persistence.Grpc;

// --------------------------------------------------------------------------------------------------
//  PART 1 OF 4 - THE RET CODE CORRESPONDENCE, IN ONE AUDITABLE PLACE
// --------------------------------------------------------------------------------------------------

/// <summary>
/// The single place in this file where the in-process return-code algebra becomes the wire enum, and
/// the single place a wire <see cref="OperationStatus"/> is constructed.
/// </summary>
/// <remarks>
/// <para>
/// <b>THIS TYPE EXISTS BECAUSE OF A REVIEW INVARIANT WITH NO COMPILE-TIME ENFORCEMENT ANYWHERE IN THE
/// REPOSITORY.</b> <c>PowerFramework.Contracts</c> declares ZERO <c>ProjectReference</c> - deliberately
/// none to <c>PowerFramework.Shared.Kernel</c>, so that the published boundary cannot become a back
/// door for shared behaviour. The consequence is that the numeric agreement between the generated
/// <c>common.v1.RetCode.Value</c> enum and <c>shared/PowerFramework.Shared.Kernel/RetCode.cs</c> is
/// checked by NOTHING: no compiler, no analyzer, and no single-sided unit test. A divergence would be a
/// silent wire-compatibility defect.
/// </para>
/// <para>
/// <b>This project is the first and only place both are referenced together</b>, which is what makes
/// the correspondence auditable here and nowhere else. This file mixes in-process return codes with
/// generated status types on almost every path, so routing every conversion through one named member -
/// rather than scattering <c>(WireRetCode)</c> casts across five handlers - is what lets a reviewer
/// check the invariant by reading one line. The value-for-value assertion belongs to
/// <c>PowerFramework.Persistence.Tests</c>, not here.
/// </para>
/// <para>
/// <b>Why this is not the sibling's <c>TransactionWireCodes</c>, which lives in this same namespace.</b>
/// That type documents itself as the single conversion point of <c>Grpc/TransactionService.cs</c>, so
/// reaching into it from C-06 would contradict its own stated scope and couple two independently
/// versioned contracts through their status factories. The two also need different shapes: C-08 projects
/// an in-process <c>DbErrorData</c>, whereas every database error reaching THIS file has already been
/// projected onto the wire by a sanctioned mapper, so no overload here accepts the in-process payload at
/// all. Both re-type through the identical one-line conversion, and neither may be used from the other's
/// file.
/// </para>
/// </remarks>
internal static class UpdateWireCodes
{
    /// <summary>
    /// Re-types an in-process return code as its wire twin.
    /// </summary>
    /// <param name="code">
    /// A constant from <c>PowerFramework.Shared.Kernel.RetCode</c>, or a value returned by a
    /// collaborator that returns one.
    /// </param>
    /// <returns>The same numeric value in the generated enum's type.</returns>
    /// <remarks>
    /// <para>
    /// The double conversion is deliberate and is not redundant. The in-process constants are
    /// <see cref="long"/> because PowerBuilder's <c>Long</c> is what the oracle declares
    /// [<c>ws_objects/pfw.shared.pbl.src/retcode.sru:L39</c>]; the generated enum's underlying type is
    /// <see cref="int"/>, as protobuf enums always are. Narrowing first and re-typing second states both
    /// facts, whereas a single cast would hide the narrowing.
    /// </para>
    /// <para>
    /// <b>An unrecognised value is passed through rather than rejected.</b> Protobuf enums are open by
    /// design - an unknown number round-trips as itself - so a code this build does not know about
    /// reaches the caller intact instead of being flattened to a wrong-but-known one. Throwing here
    /// would convert a forward-compatibility event into an outage, and substituting a default would
    /// misreport the outcome.
    /// </para>
    /// </remarks>
    internal static WireRetCode ToWireRetCode(long code) => (WireRetCode)(int)code;

    /// <summary>
    /// Builds a status carrying only an outcome code.
    /// </summary>
    /// <param name="code">The in-process return code.</param>
    /// <returns>
    /// A status whose <c>ret_code</c> is always populated and whose other two members are absent - no
    /// diagnostic text and no driver detail.
    /// </returns>
    /// <remarks>
    /// The absence of <c>db_error</c> is meaningful rather than incidental: the contract states it is
    /// present ONLY when the legacy would also have fired its database-error event, which is not the
    /// case for a plain guard refusal.
    /// </remarks>
    internal static OperationStatus Status(long code) => new()
    {
        RetCode = ToWireRetCode(code),
    };

    /// <summary>
    /// Builds a status carrying an outcome code, the legacy's own diagnostic text and, when the legacy
    /// would also have fired its database-error event, the driver-level detail beneath it.
    /// </summary>
    /// <param name="code">The in-process return code.</param>
    /// <param name="errorText">
    /// The diagnostic, treated as OPAQUE DISPLAY TEXT. It is neither translated nor parsed, and it MAY
    /// BE NON-ENGLISH: several in-scope legacy arms synthesize Chinese diagnostics - among them
    /// <c>"没有可更新的表"</c> for the missing update table [<c>:L191</c>],
    /// <c>"没有设置可更新表!"</c> for an empty descriptor array [<c>:L360</c>],
    /// <c>"无效的更新数据!"</c> [<c>:L343</c>], <c>"无效的数据源对象!"</c> [<c>:L327</c>] and
    /// <c>"无效的列名:"</c> plus the column name [<c>:L120</c>]. The contract requires consumers to
    /// branch on <c>ret_code</c> instead of on this string, and an empty value is legitimate - the clean
    /// veto arm passes no message at all [<c>:L198</c>].
    /// <para>
    /// <see langword="null"/> normalises to the empty string, because the generated setter rejects null.
    /// </para>
    /// </param>
    /// <param name="dbError">
    /// The driver-level detail, or <see langword="null"/> when the legacy would have raised only its
    /// general error channel. <b>ALREADY PROJECTED AND ALREADY REDACTED</b> - see the remarks.
    /// </param>
    /// <returns>A status with the supplied members populated.</returns>
    /// <remarks>
    /// <para>
    /// <b>THE STATEMENT FIELD IS MASKED BEFORE IT EVER REACHES THIS METHOD (constraint C-F).</b> This
    /// parameter is the wire <see cref="DbError"/>, not the in-process payload, and the only two
    /// producers of one in this file are <c>UpdateOutcome.TryProjectDbError</c> and the task surface's
    /// latched error - both of which go through <c>DbErrorDataExtensions.ToDbError</c>, which reaches its
    /// redaction policy DIRECTLY rather than accepting one. The policy therefore cannot be weakened from
    /// a call site or from a container registration, which is stronger than a mandatory-redactor
    /// parameter would have been and is why no <c>ISqlRedactor</c> is injected into this service: an
    /// injected policy is a replaceable policy.
    /// </para>
    /// <para>
    /// <b><c>DbError.Sqlsyntax</c> is never assigned anywhere in this file.</b> The masking matters here
    /// because the oracle's field carries the COMPLETE generated statement - and with
    /// <c>DisableBind=1</c> the runtime interpolates values as literals rather than binding them
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru:L128</c>], so that text can
    /// carry live row data while the legacy logger redacts nothing at all.
    /// </para>
    /// <para>
    /// <b>Nothing secret can reach the text parameter from this file either.</b> Every call site below
    /// passes either a diagnostic produced by a collaborator or a fixed sentence that quotes no value;
    /// no descriptor, column value, connection string or payload is ever formatted into it.
    /// </para>
    /// </remarks>
    internal static OperationStatus Status(long code, string? errorText, DbError? dbError = null)
    {
        OperationStatus status = new()
        {
            RetCode = ToWireRetCode(code),
            ErrorText = errorText ?? string.Empty,
        };

        // Assigned conditionally rather than unconditionally: assigning null is harmless, but writing
        // the guard states the contract's own rule - db_error is present ONLY when the legacy would also
        // have fired OnDBError - at the one place a reader looks for it.
        if (dbError is not null)
        {
            status.DbError = dbError;
        }

        return status;
    }
}

// --------------------------------------------------------------------------------------------------
//  PART 2 OF 4 - THE BOUNDARY: WHAT ONE RUN ANSWERS, THE TASK SEAM, AND HANDLE CORRELATION
// --------------------------------------------------------------------------------------------------

/// <summary>
/// Everything one <c>Update</c> call answers: the run's reconciled outcome code, the classification
/// behind it when the update was actually attempted, the ACCUMULATED counts, the ordered per-table
/// identity blocks, the latched driver error and the run's own diagnostic text.
/// </summary>
/// <remarks>
/// <para>
/// <b>WHY THE CODE AND THE CLASSIFICATION ARE SEPARATE MEMBERS, AND WHY BOTH ARE NEEDED.</b> They
/// genuinely disagree, and the oracle is what makes them disagree. <c>_of_update</c>'s classification is
/// what one attempt turned out to be [<c>:L172-L252</c>], but <c>ondotask</c> can answer something else
/// entirely: it can fail before the update is ever reached - a failed transaction acquisition
/// [<c>:L292-L295</c>], a carrier it could not create [<c>:L317-L320</c>], a missing source
/// [<c>:L326-L330</c>], a rejected payload [<c>:L343-L345</c>], a transaction it could not attach
/// [<c>:L350-L354</c>] or an empty descriptor array under the multi-table switch [<c>:L359-L363</c>] -
/// and it can OVERWRITE a completed classification afterwards, because a cancellation observed after the
/// teardown replaces whatever the body answered [<c>:L381-L383</c>] and a failing autocommit replaces a
/// success [<c>:L386-L392</c>].
/// </para>
/// <para>
/// So <see cref="Code"/> is what the caller is told and <see cref="Outcome"/> is what happened, and a
/// consumer of this record must key the RESPONSE on the code while keying the SHAPE of the status on the
/// classification. Collapsing the two would either report a stale success or lose the arm that produced
/// the failure.
/// </para>
/// <para>
/// <b>THE COUNTS AND THE IDENTITY BLOCKS ARE ACCUMULATED, NOT PER ATTEMPT.</b> The worker fires its two
/// caller-side events ONCE PER TABLE [<c>:L243</c>, <c>:L247</c>, from the loop at <c>:L364-L369</c>] and
/// the caller-side proxy is the accumulator: it ADDS to running totals -
/// <c>_nRowsInserted += inserted</c> and likewise for updated and deleted
/// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlupdate.sru:L66-L68</c>] - and APPENDS
/// one identity record per firing at upper-bound-plus-one
/// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlupdate.sru:L73-L76</c>]. A single-table
/// response is therefore the degenerate case of a repeated one, which is exactly the cardinality the
/// contract's <c>repeated</c> identity field carries.
/// </para>
/// <para>
/// <b>THE DRIVER ERROR ARRIVES ALREADY PROJECTED, AND THAT IS A C-F DECISION RATHER THAN A CONVENIENCE.</b>
/// The member is the wire <see cref="Contracts.Common.V1.DbError"/> and not the in-process
/// <c>DbErrorData</c>, so no code in <c>Grpc/</c> ever holds an unredacted statement. An implementation
/// fills it from <c>SqlTaskProxyBase.GetLastDbErrorForWire()</c>, which the proxy documents as the one
/// sanctioned door, and answers <see langword="null"/> when nothing was latched - which is the state the
/// contract requires for a validation failure, where the legacy raises only its general channel.
/// </para>
/// </remarks>
internal sealed record UpdateRunResult
{
    /// <summary>
    /// What <c>ondotask</c> answered [<c>:L403</c>], AFTER its teardown, its cancellation rewrite and its
    /// commit-or-rollback epilogue. This is the authoritative outcome the caller is told.
    /// </summary>
    /// <value>
    /// <c>RetCode.OK</c> on success. Note that success is <c>RetCode.OK</c> HERE while the DataWindow
    /// update's own success value is <c>1</c> [<c>:L214</c>] - the two code spaces are separate and the
    /// classification keeps them separate.
    /// </value>
    internal required long Code { get; init; }

    /// <summary>
    /// The classification of the update attempt, or <see langword="null"/> when the update was never
    /// attempted at all.
    /// </summary>
    /// <value>
    /// <see langword="null"/> is a legitimate and common state, not a fault: every arm of
    /// <c>ondotask</c> that returns before <c>_of_Update</c> [<c>:L292-L363</c>] leaves nothing to
    /// classify. On the multi-table path this carries the LAST attempt's classification, because the loop
    /// stops at the first failure [<c>:L366</c>, <c>:L368</c>] and therefore the last attempt is the one
    /// that decided the run.
    /// </value>
    internal UpdateOutcome? Outcome { get; init; }

    /// <summary>
    /// The three row counts, accumulated across every table of the run - the caller-side proxy's running
    /// totals [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlupdate.sru:L66-L68</c>], read
    /// back through its three accessors [<c>:L214</c>, <c>:L217</c>, <c>:L220</c> of that file].
    /// </summary>
    /// <remarks>
    /// The default is the all-zero triple, which is the proxy's own state after a reset
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlupdate.sru:L90-L92</c>] and the
    /// correct value for a run that never reached the success arm.
    /// </remarks>
    internal UpdateRowCounts Counts { get; init; }

    /// <summary>
    /// The identity blocks, ONE PER UPDATE TABLE THAT COLLECTED ANY, IN THE ORDER THE TABLES WERE
    /// DECLARED. Empty means "none collected" and is never an error.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>ORDER IS CONTRACT AT TWO LEVELS AND BOTH BIND.</b> The blocks travel in accumulation order,
    /// and inside each block the two value arrays travel in COLLECTION order - the Primary buffer walked
    /// FORWARD [<c>:L228-L233</c>] and the Filter buffer walked BACKWARD [<c>:L237</c>], because the
    /// oracle's own comment immediately above that loop records that the filter buffer's row order is
    /// INVERTED relative to the data source [<c>:L235</c>]. It looks like a bug. It is not a bug.
    /// "Correcting" the direction produces wrong identity values that a row-count assertion would still
    /// pass. Nothing that consumes this member may sort, deduplicate, merge or reverse either level.
    /// </para>
    /// <para>
    /// Empty arises from any of three conditional levels failing - no rows were inserted [<c>:L215</c>],
    /// no identity column was found [<c>:L226</c>], or both collected arrays came back empty
    /// [<c>:L242</c>] - and an EMPTY BLOCK is never synthesised in place of no block, because the oracle's
    /// guard is on the CALL rather than on the contents.
    /// </para>
    /// </remarks>
    internal IReadOnlyList<ResolvedIdentityColumnData> Identity { get; init; } = [];

    /// <summary>
    /// The latched driver-level error, ALREADY PROJECTED ONTO THE WIRE AND ALREADY REDACTED, or
    /// <see langword="null"/> when the run latched none.
    /// </summary>
    /// <remarks>
    /// Filled from the proxy's <c>GetLastDbErrorForWire()</c>, the port of <c>of_getlastdberrordata</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlbase.sru:L47</c>] composed with the
    /// sealed redaction policy. It is what carries the acquisition failure's detail
    /// [<c>:L293</c>], which no classification exists for because that arm returns before
    /// <c>_of_Update</c> is reached.
    /// </remarks>
    internal DbError? LastDbError { get; init; }

    /// <summary>
    /// The run's own diagnostic text - the oracle's <c>sError</c> local [<c>:L286</c>], as the epilogue
    /// would have reported it [<c>:L398</c>].
    /// </summary>
    /// <remarks>
    /// Opaque display text that may be non-English, exactly as
    /// <see cref="UpdateWireCodes.Status(long, string?, DbError?)"/> documents. Empty is legitimate: the
    /// oracle leaves the local unwritten on several arms and passes no message at all on the clean-veto
    /// arm [<c>:L198</c>].
    /// </remarks>
    internal string ErrorText { get; init; } = string.Empty;
}

/// <summary>
/// One server-held update task, as this boundary needs to drive it: the caller-side mutators the
/// contract's <c>PrepareUpdate</c> and <c>Update</c> project onto, and one execution.
/// </summary>
/// <remarks>
/// <para>
/// <b>WHY THIS SEAM EXISTS AT ALL, AND WHY IT IS DECLARED HERE.</b> The legacy is a LIBRARY with no
/// process of its own, so it has no analogue for a task reached by name across a network. In process the
/// task is reached through a POINTER: the caller creates <c>n_cst_threading_task_sqlupdate</c>, drives it
/// through a series of <c>of_set*</c> calls that each answer a return code, starts it, and destroys it.
/// A pointer cannot be serialized, so the contract substitutes an opaque server-issued
/// <see cref="TaskHandle"/> and this interface is what a handle resolves to. That is the same reason
/// <c>Grpc/TransactionService.cs</c> declares its own session type locally, and the same reason both
/// belong in <c>Grpc/</c>: they are boundary vocabulary, not behaviour.
/// </para>
/// <para>
/// <b>IT MIRRORS THE CALLER-SIDE PROXY MEMBER FOR MEMBER, DELIBERATELY.</b> Every member below
/// corresponds to exactly one member of <c>Tasks/TaskProxies/SqlUpdateTaskProxy.cs</c>, with the same
/// name and the same meaning, so that a production adapter is a one-line forward per member with nothing
/// to decide. Keeping the shapes aligned is what stops this seam from acquiring behaviour of its own
/// (constraint C-H), and it is why the two <c>AddUpdatableTable</c> arities appear here as two members
/// rather than one member with defaults - the oracle declares two
/// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlupdate.sru:L59-L60</c>] and the
/// four-argument one is the PROOF that absence is a first-class input.
/// </para>
/// <para>
/// ⚠️ <b>E_BUSY IS THIS SEAM'S ANSWER AND NEVER THE ADAPTER'S.</b> Every caller-side mutator in the
/// oracle opens with <c>if of_IsBusy() then return RetCode.E_BUSY</c>
/// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlupdate.sru:L99</c>, <c>:L106</c>,
/// <c>:L111</c>, <c>:L207</c>, <c>:L223</c>, <c>:L236</c>, <c>:L246</c>, and the shared reset at
/// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlbase.sru:L57</c>], while the worker's own
/// reset tests its running flag instead [<c>:L60</c>]. Both answer the same code. The adapter therefore
/// adds NO gate of its own: inventing one would be a guard the oracle does not have in that position,
/// which C-B forbids.
/// </para>
/// <para>
/// <b>DISPOSAL IS THE WIRE FORM OF <c>Destroy</c>, AND THE FINALIZE HOOK IS NOT THIS SEAM'S BUSINESS.</b>
/// The oracle's <c>onfinalize</c> [<c>:L406-L408</c>] is fired by the task SUBSTRATE as part of teardown,
/// not by the caller, so an implementation sequences it and this boundary does not. Exposing it here
/// would move the substrate's job to the adapter, which would be less faithful rather than more.
/// </para>
/// </remarks>
internal interface IUpdateTaskSurface : IDisposable
{
    /// <summary>
    /// Clears every input - <c>of_reset</c> [<c>:L43</c>, <c>:L58-L72</c>] as the caller-side proxy
    /// expresses it [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlupdate.sru:L82-L97</c>].
    /// </summary>
    /// <returns>
    /// <c>RetCode.E_BUSY</c> while the task is running [<c>:L60</c>]; otherwise the code the reset chain
    /// answers [<c>:L71</c>].
    /// </returns>
    /// <remarks>
    /// <b>THE GUARD IS FIRST AND NOTHING IS CLEARED BEHIND IT.</b> The oracle returns before its first
    /// assignment, so a refused reset leaves the task's state entirely intact and cannot half-clear the
    /// inputs.
    /// </remarks>
    long Reset();

    /// <summary>
    /// Replaces the descriptor array with an empty one and turns the multi-table switch off - the
    /// descriptor half of <c>of_reset</c> [<c>:L63</c>, <c>:L67</c>].
    /// </summary>
    /// <returns><c>RetCode.OK</c>, or <c>RetCode.E_BUSY</c> when the task is busy.</returns>
    /// <remarks>
    /// <b>REPLACEMENT, NOT TRIMMING.</b> The oracle assigns a freshly declared empty array -
    /// <c>Tables = emptyTables</c> [<c>:L67</c>] - which is why the contract's repeated descriptor field
    /// replaces the array wholesale rather than appending to it. This is the one part of <c>of_reset</c>
    /// that <c>PrepareUpdate</c> needs on its own, and it is separate from <see cref="Reset"/> precisely
    /// because a prepare must not also discard the payload, the autocommit flag or the source
    /// [<c>:L62</c>, <c>:L64-L65</c>, <c>:L68-L69</c>].
    /// </remarks>
    long ResetUpdatableTables();

    /// <summary>
    /// Turns multi-table update on or off - <c>of_setmultitableupdate</c> [<c>:L51</c>,
    /// <c>:L265-L268</c>], proxied at
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlupdate.sru:L207-L212</c>].
    /// </summary>
    /// <param name="multiTable">The switch.</param>
    /// <returns><c>RetCode.OK</c>, or <c>RetCode.E_BUSY</c> when the task is busy.</returns>
    /// <remarks>
    /// 🔴 <b>THIS FLAG ALONE DECIDES WHETHER THE PREPARE STEP RUNS AT ALL.</b>
    /// <c>_of_UpdatePrepare</c> has exactly ONE caller, at [<c>:L365</c>], inside the multi-table branch;
    /// the single-table branch calls the update directly [<c>:L371</c>] and never prepares. So with the
    /// switch OFF the descriptor array is NOT APPLIED and the carrier's own static definition governs
    /// update, key, identity, update-where and key-in-place. That looks like a legacy oversight; it is the
    /// observable behaviour, so it is preserved and documented rather than "fixed" into applying the
    /// descriptors in both modes - which would change the generated statement for every existing
    /// single-table caller.
    /// </remarks>
    long SetMultiTableUpdate(bool multiTable);

    /// <summary>
    /// Appends one updatable-table descriptor, SIX-ARGUMENT FORM - <c>of_addupdatabletable</c>
    /// [<c>:L46</c>, <c>:L82-L96</c>], proxied at
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlupdate.sru:L223-L225</c>].
    /// </summary>
    /// <param name="name">The update table name. Empty is rejected [<c>:L84</c>].</param>
    /// <param name="updatableColumns">The columns to mark updatable. Empty is rejected [<c>:L84</c>].</param>
    /// <param name="keyColumns">The columns to mark as keys. Empty is rejected [<c>:L84</c>].</param>
    /// <param name="identityColumn">
    /// The identity column, or the empty string for none. <b>EMPTY IS LEGAL</b> - it is not one of the
    /// three validation arms, and the oracle simply omits its line [<c>:L127-L129</c>].
    /// </param>
    /// <param name="updateWhere">
    /// The update-where MODE, or <see langword="null"/> for ABSENT. A <c>long</c> and not a boolean: the
    /// oracle writes it through as a string-quoted NUMBER [<c>:L132</c>], and the sole evidenced fixture
    /// uses mode 1 [<c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L14</c>].
    /// </param>
    /// <param name="updateKeyInPlace">The key-in-place setting, or <see langword="null"/> for ABSENT.</param>
    /// <returns>
    /// <c>RetCode.OK</c>, <c>RetCode.E_INVALID_ARGUMENT</c> when the descriptor fails admission
    /// [<c>:L84</c>], or <c>RetCode.E_BUSY</c> when the task is busy.
    /// </returns>
    /// <remarks>
    /// ⚠️ <b>ABSENCE MUST SURVIVE AS ABSENCE AND MUST NEVER BE DEFAULTED.</b> The oracle emits each
    /// setting's line only when it is not null - <c>if Not IsNull(Tables[index].UpdateWhere)</c>
    /// [<c>:L131</c>] and <c>if Not IsNull(Tables[index].UpdateKeyInPlace)</c> [<c>:L135</c>] - so null
    /// carries the distinct meaning "LEAVE THE CARRIER'S OWN SETTING ALONE". Defaulting the mode to 0
    /// would silently force a concurrency mode the caller never asked for, and defaulting the key-in-place
    /// setting to <see langword="false"/> would emit a line the oracle omits.
    /// </remarks>
    long AddUpdatableTable(
        string name,
        IEnumerable<string> updatableColumns,
        IEnumerable<string> keyColumns,
        string identityColumn,
        long? updateWhere,
        bool? updateKeyInPlace);

    /// <summary>
    /// Appends one updatable-table descriptor leaving BOTH optional settings ABSENT - the FOUR-ARGUMENT
    /// form [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlupdate.sru:L60</c>,
    /// <c>:L227-L234</c>].
    /// </summary>
    /// <param name="name">The update table name.</param>
    /// <param name="updatableColumns">The columns to mark updatable.</param>
    /// <param name="keyColumns">The columns to mark as keys.</param>
    /// <param name="identityColumn">The identity column, or the empty string for none.</param>
    /// <returns>As the six-argument form.</returns>
    /// <remarks>
    /// <b>THIS OVERLOAD IS THE DECISIVE EVIDENCE THAT ABSENCE IS A SUPPORTED, DISTINCT INPUT.</b> The
    /// oracle's four-argument form declares two locals, calls <c>SetNull</c> on BOTH
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlupdate.sru:L230-L231</c>] and then
    /// delegates to the six-argument form [<c>:L233</c>] - it exists for no other purpose than to make
    /// both settings null. The script built for a descriptor added this way OMITS both the
    /// <c>DataWindow.Table.UpdateWhere</c> and the <c>DataWindow.Table.UpdateKeyinPlace</c> lines, so the
    /// carrier's own definition governs both.
    /// </remarks>
    long AddUpdatableTable(
        string name,
        IEnumerable<string> updatableColumns,
        IEnumerable<string> keyColumns,
        string identityColumn);

    /// <summary>
    /// Names the source object to build the carrier from - <c>of_setdataobject</c> [<c>:L50</c>,
    /// <c>:L259-L263</c>], proxied at
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlupdate.sru:L99-L104</c>].
    /// </summary>
    /// <param name="dataObject">The source-object name.</param>
    /// <returns><c>RetCode.OK</c>, or <c>RetCode.E_BUSY</c> when the task is busy.</returns>
    /// <remarks>
    /// ⚠️ <b>THE TWO SOURCES ARE MUTUALLY EXCLUSIVE AND EACH SETTER CLEARS THE OTHER</b> - the oracle
    /// clears the syntax here [<c>:L260</c>] and clears the source object in <see cref="SetSqlSyntax"/>
    /// [<c>:L271</c>]. That is what makes the three-way shaping branch decidable [<c>:L316-L331</c>]:
    /// syntax first, then source object, then the <c>E_INVALID_DATAOBJECT</c> arm with the diagnostic
    /// <c>"无效的数据源对象!"</c> [<c>:L327-L328</c>].
    /// </remarks>
    long SetDataObject(string dataObject);

    /// <summary>
    /// Sets the DataWindow syntax to build the carrier from - <c>of_setsqlsyntax</c> [<c>:L52</c>,
    /// <c>:L270-L274</c>], proxied at
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlupdate.sru:L111-L118</c>].
    /// </summary>
    /// <param name="sqlSyntax">The syntax.</param>
    /// <returns><c>RetCode.OK</c>, or <c>RetCode.E_BUSY</c> when the task is busy.</returns>
    /// <remarks>Clears the source object [<c>:L271</c>] - see <see cref="SetDataObject"/>.</remarks>
    long SetSqlSyntax(string sqlSyntax);

    /// <summary>
    /// Sets the changeset payload and the caller's own row count - <c>of_setupdatedata</c> [<c>:L44</c>,
    /// <c>:L74-L77</c>], proxied at
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlupdate.sru:L236-L239</c>].
    /// </summary>
    /// <param name="updateData">
    /// The typed carrier state, or <see langword="null"/> for the oracle's zero-length blob. The oracle's
    /// parameter is <c>ref blob</c> purely to avoid copying a large value across the call; a reference
    /// type needs no such device, so this seam takes it by value.
    /// </param>
    /// <param name="updateRows">
    /// The row count the sender measured. <b>LOAD-BEARING, NOT INFORMATIONAL</b>: it is the second
    /// conjunct of the arm that treats a rejected payload as a SUCCESS -
    /// <c>if rtCode = -1 and _nUpdateRows = 0 then rtCode = RetCode.OK</c> [<c>:L339-L342</c>] - so an
    /// empty update is a successful no-op while a corrupt one is <c>E_INVALID_DATA</c> [<c>:L343-L345</c>],
    /// and this value is the only thing that distinguishes them.
    /// </param>
    /// <returns>
    /// <c>RetCode.OK</c> [<c>:L76</c>], or <c>RetCode.E_BUSY</c> when the task is busy.
    /// </returns>
    /// <remarks>
    /// The payload's own structural obligations - exactly three buffer segments, one per buffer, in
    /// canonical order, with no row disagreeing with its segment's tag - are enforced by
    /// <c>Buffers/ChangesetCodec.cs</c> behind the carrier's apply step, and a rejection surfaces through
    /// the arm above. This boundary does not duplicate that check, because a second copy of it is a second
    /// place for it to disagree.
    /// </remarks>
    long SetUpdateData(CarrierState? updateData, long updateRows);

    /// <summary>
    /// Whether the task currently holds one of the two mutually exclusive update sources - a data object
    /// name or a SQL syntax string.
    /// </summary>
    /// <value>
    /// <see langword="true"/> when either source is installed; <see langword="false"/> when the task holds
    /// neither and would therefore take the oracle's <c>E_INVALID_DATAOBJECT</c> arm [<c>:L326-L330</c>] if
    /// it ran.
    /// </value>
    /// <remarks>
    /// <para>
    /// AN OBSERVATION, NOT A SETTER, AND IT EXISTS FOR THE PREPARE BOUNDARY. The two sources are set only
    /// through <see cref="SetDataObject"/> and <see cref="SetSqlSyntax"/>, each of which clears the other
    /// [<c>:L260</c>, <c>:L271</c>], so "holds neither" is a state the task can legitimately be in and the
    /// only way to leave it is another prepare. Exposing it lets that boundary answer the code the run
    /// WOULD answer instead of a success the caller cannot act on.
    /// </para>
    /// <para>
    /// NO BUSY GUARD, because it reads state rather than changing it - the same shape as the oracle's own
    /// caller-side accessors, none of which guards a read.
    /// </para>
    /// </remarks>
    bool HasUpdateSource { get; }

    /// <summary>
    /// The data-object name the task currently holds, or the empty string when it holds none.
    /// </summary>
    /// <remarks>
    /// AN OBSERVATION FOR THE PREPARE BOUNDARY'S DESCRIPTOR CHECK. A descriptor sent while the multi-table
    /// switch is off is never applied [<c>:L365</c> against <c>:L371</c>], so the definition this name
    /// resolves to is what actually governs the update - and comparing the two is the only way the boundary
    /// can tell an agreeing descriptor from one that names a different table entirely. Empty is ordinary: a
    /// task may hold a SQL syntax instead, or nothing yet.
    /// </remarks>
    string DataObject { get; }

    /// <summary>
    /// Sets whether the epilogue commits on success - <c>of_setautocommit</c> [<c>:L49</c>,
    /// <c>:L254-L257</c>], proxied at
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlupdate.sru:L106-L109</c>].
    /// </summary>
    /// <param name="autoCommit">The flag.</param>
    /// <returns><c>RetCode.OK</c> [<c>:L256</c>], or <c>RetCode.E_BUSY</c> when the task is busy.</returns>
    /// <remarks>
    /// ⚠️ <b>THIS IS A BOOLEAN AND C-07'S IS A THREE-VALUED ENUM. THE ASYMMETRY IS THE ORACLE'S AND IS
    /// PRESERVED (C-B).</b> The update task declares
    /// <c>of_setautocommit(readonly boolean autocommit)</c> [<c>:L49</c>] while the command task declares
    /// <c>of_setautocommit(readonly long autocommit)</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlcommand.sru:L28</c>]. Unifying them
    /// would WIDEN this contract by two values it has no behaviour for: the update task's autocommit is a
    /// plain if-test that either commits or does not [<c>:L386-L387</c>], with no third arm anywhere.
    /// </remarks>
    long SetAutoCommit(bool autoCommit);

    /// <summary>
    /// Performs one update - the <c>ondotask</c> event [<c>:L284-L404</c>] - and answers everything the
    /// response needs.
    /// </summary>
    /// <param name="cancellationToken">
    /// The caller's cancellation, unified with the substrate's own <c>of_IsCancelled()</c> poll. It is
    /// POLLED rather than thrown through, because the oracle RETURNS a code on every cancellation arm
    /// [<c>:L182</c>, <c>:L212</c>, <c>:L301</c>, <c>:L381-L383</c>] and its epilogue distinguishes that
    /// code from every other one in order to suppress a duplicate error raise [<c>:L396</c>].
    /// </param>
    /// <returns>The run's reconciled code, its classification, its accumulated counts and identity
    /// blocks, its latched driver error and its diagnostic text.</returns>
    /// <remarks>
    /// <b>THIS MEMBER NEVER THROWS FOR A DOMAIN OUTCOME AND NEVER PRODUCES AN <c>RpcException</c>.</b>
    /// Every failure is a code plus, where the oracle raises one, an error payload. The gRPC layer is the
    /// throw site; an implementation that threw would decide the transport on its caller's behalf and
    /// would bypass the multi-table loop's own inspection of the returned code [<c>:L366</c>, <c>:L368</c>].
    /// </remarks>
    UpdateRunResult Execute(CancellationToken cancellationToken);
}

/// <summary>
/// Creates one server-held update task bound to a transaction session - the wire form of the legacy
/// object creation that <c>CreateUpdateTask</c> exposes.
/// </summary>
/// <remarks>
/// <para>
/// <b>A FACTORY RATHER THAN A CONSTRUCTOR CALL, BECAUSE THE ADAPTER MUST NOT KNOW HOW A TASK IS BUILT.</b>
/// Building the worker/proxy pair means naming the substrate host, the transaction pool, the data-store
/// factory, the retrieval-hook activator, the carrier adapter, the classifier and the clock - seven
/// collaborators that belong to <c>Tasks/</c> and to the composition root. Injecting a factory keeps all
/// seven out of this file, which is what makes the whole boundary exercisable from a test with no
/// database, no thread and no carrier (constraint C-H). It is the same shape
/// <c>Tasks/SqlTaskBase.cs</c> uses for <c>ISqlDataStoreFactory</c>.
/// </para>
/// <para>
/// <b>THE SESSION IS RESOLVED BY THE FACTORY, NOT BY THE ADAPTER.</b> A task runs against a session
/// issued by C-08's <c>BeginSession</c>, and the pool reference behind that session is the factory's
/// concern. An unknown or already-ended session is <c>RetCode.E_INVALID_TRANSACTION</c>, matching the code
/// the oracle returns when asked to act on a transaction it cannot use
/// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:L113</c>].
/// </para>
/// <para>
/// <b>AND BECAUSE THE FACTORY OWNS THE SESSION, IT ALSO OWNS THE PUBLICATION WINDOW.</b> A task must not
/// become reachable after its session has begun retiring: <c>EndSession</c> marks the session closing
/// inside the session's lifecycle gate and only then walks the task registries, so a registration performed
/// outside that gate can land after the walk and leave a task holding a transaction the pool has already
/// taken back. The adapter therefore asks the factory for a publication window and registers inside it -
/// see <see cref="IUpdateTaskFactory.EnterPublicationAsync"/>.
/// </para>
/// </remarks>
internal interface IUpdateTaskFactory
{
    /// <summary>
    /// Creates a task for a session.
    /// </summary>
    /// <param name="sessionId">The opaque session identity from the request. Never empty.</param>
    /// <param name="task">The created task, or <see langword="null"/> when the code is not
    /// <c>RetCode.OK</c>.</param>
    /// <returns>
    /// <c>RetCode.OK</c> on success, or the code that explains the refusal - typically
    /// <c>RetCode.E_INVALID_TRANSACTION</c> for an unknown or ended session.
    /// </returns>
    long TryCreate(string sessionId, out IUpdateTaskSurface? task);

    /// <summary>
    /// Enters the lifecycle gate of the session a task was created against, so that the caller can publish
    /// the task atomically with respect to that session's closure.
    /// </summary>
    /// <param name="sessionId">The session the task was created against.</param>
    /// <param name="cancellationToken">The request's token. Awaiting the gate is cancellable.</param>
    /// <returns>
    /// A window that must be disposed as soon as the publication step is over, and that reports whether the
    /// session has already begun retiring.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>THE DEFAULT IMPLEMENTATION IS THE HONEST ANSWER FOR A FACTORY THAT TRACKS NO SESSIONS</b>, and
    /// there is a real such implementation: a test double built around a fake surface has no pool, no
    /// session registry and therefore no lifecycle to be atomic against. It answers an unguarded window
    /// that reports the session live, which reproduces exactly the behaviour such a double had before this
    /// member existed. The provisioned factory in <c>Program.cs</c> - the only one with a session registry -
    /// overrides it and holds the real gate.
    /// </para>
    /// <para>
    /// It is AWAITED rather than blocked on because another operation on the same transaction may hold the
    /// gate; a streaming retrieval holds it for the whole of its stream, and a blocking acquisition here
    /// would pin a request thread for that duration.
    /// </para>
    /// </remarks>
    ValueTask<UpdateTaskPublication> EnterPublicationAsync(
        string sessionId,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(UpdateTaskPublication.Unguarded);
}

/// <summary>
/// The window inside which an update task may be published into its registry: it holds the session's
/// lifecycle gate for as long as it lives, and reports whether that session has already begun retiring.
/// </summary>
/// <remarks>
/// <para>
/// <b>A WINDOW RATHER THAN A BOOLEAN, BECAUSE THE ANSWER AND THE ACT MUST NOT BE SEPARABLE.</b> Asking
/// "is this session still live?" and then registering is a check-then-act across a boundary whose callers
/// are concurrent requests: <c>EndSession</c> can mark the session closing and walk the registry in between,
/// and the task it missed then outlives the transaction it holds. Holding the gate for both steps is what
/// makes them one step. The type is a <see langword="readonly"/> <see langword="struct"/> so the ordinary
/// path allocates nothing.
/// </para>
/// <para>
/// <b>THE UNGUARDED VALUE IS <see langword="default"/>, AND IT REPORTS THE SESSION LIVE.</b> That is not a
/// silent weakening: a factory with no session registry has nothing to retire a session from, so there is
/// no closure for the publication to race. The only such factories are test doubles.
/// </para>
/// </remarks>
internal readonly struct UpdateTaskPublication : IDisposable
{
    /// <summary>The held gate, or <see langword="default"/> when this window guards nothing.</summary>
    private readonly TransactionGateScope _gate;

    /// <summary>
    /// Creates a window over a held gate.
    /// </summary>
    /// <param name="gate">The acquired lifecycle gate, released on disposal.</param>
    /// <param name="isSessionRetiring">Whether the session has already been marked closing.</param>
    internal UpdateTaskPublication(TransactionGateScope gate, bool isSessionRetiring)
    {
        _gate = gate;
        IsSessionRetiring = isSessionRetiring;
    }

    /// <summary>A window that holds nothing and reports the session live.</summary>
    internal static UpdateTaskPublication Unguarded => default;

    /// <summary>
    /// Whether the session began retiring before this window opened, so nothing may be published.
    /// </summary>
    /// <remarks>
    /// READ INSIDE THE WINDOW AND NOWHERE ELSE. It is a snapshot taken under the gate this window holds, so
    /// it stays true for as long as the window lives; read after disposal it would be a stale value with no
    /// mutual exclusion behind it.
    /// </remarks>
    internal bool IsSessionRetiring { get; }

    /// <summary>Releases the lifecycle gate. A no-op for the unguarded window.</summary>
    public void Dispose() => _gate.Dispose();
}

/// <summary>
/// One registered task: the wire handle, the session it was created against, and the task itself.
/// </summary>
/// <param name="TaskId">
/// The opaque server-issued identity. OPAQUE BY CONTRACT - consumers must not parse it, derive one or
/// attach meaning to its shape, because it replaces a pointer and a pointer has no structure a caller is
/// entitled to read. It is also NOT a credential and authorizes nothing (constraint C-F).
/// </param>
/// <param name="SessionId">
/// The session identity the task was created against, retained for diagnostics and for the log records
/// that correlate a task with its session. Never rendered into a response.
/// </param>
/// <param name="Task">The task this handle stands for.</param>
/// <remarks>
/// <para>
/// <b>A CLASS RATHER THAN A RECORD, AND IT CARRIES A LEASE.</b> It was a bare record, and that shape is
/// what let three handlers - prepare, update and release - each resolve the same handle and then act on
/// it with nothing serializing them. In process that could not happen: the oracle's caller-side guard is
/// <c>if of_IsBusy() then return RetCode.E_BUSY</c>
/// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlbase.sru:L57, :L73, :L95</c>], and the
/// caller reading it IS the thread that would go on to mutate, so the read and the act are one step.
/// Across this boundary the callers are concurrent requests, and the same two steps become a
/// check-then-act: two prepares can interleave their descriptor arrays, an update can start against a
/// half-replaced array, and a release can dispose a task an update is still running through.
/// </para>
/// <para>
/// The lease answers the oracle's own codes - <c>E_BUSY</c> while another operation owns the task,
/// <c>E_INVALID_HANDLE</c> once it has been released - so nothing observable changes except that the
/// answer and the action can no longer be separated. See <c>Grpc/TaskOperationLatch.cs</c> for the
/// state machine and for the disposal-handoff table it exists to state once.
/// </para>
/// <para>
/// Value equality is deliberately given up with the record shape. Nothing compared these entries: the
/// registry keys them by handle and every consumer is interested in identity, so structural equality was
/// a property of the declaration rather than of the design.
/// </para>
/// </remarks>
internal sealed class UpdateTaskEntry(string TaskId, string SessionId, IUpdateTaskSurface Task)
{
    private readonly TaskOperationLatch _latch = new();

    /// <summary>The opaque server-issued handle this entry answers to.</summary>
    internal string TaskId { get; } = TaskId ?? throw new ArgumentNullException(nameof(TaskId));

    /// <summary>The session the task was created against. Diagnostics only.</summary>
    internal string SessionId { get; } = SessionId ?? throw new ArgumentNullException(nameof(SessionId));

    /// <summary>The task every mutation and the update itself delegate to.</summary>
    internal IUpdateTaskSurface Task { get; } = Task ?? throw new ArgumentNullException(nameof(Task));

    /// <summary>
    /// Whether an operation currently owns the task. A DIAGNOSTIC READ - see
    /// <see cref="TaskOperationLatch.IsRunning"/>.
    /// </summary>
    internal bool IsRunning => _latch.IsRunning;

    /// <summary>Whether a release has been requested.</summary>
    internal bool IsReleased => _latch.IsReleased;

    /// <summary>
    /// Takes the operation lease for one prepare, update or reset.
    /// </summary>
    /// <returns>The lease outcome, which the call site maps straight onto a code.</returns>
    internal TaskLatchOutcome TryBeginOperation() => _latch.TryBegin();

    /// <summary>
    /// Gives the lease back.
    /// </summary>
    /// <returns><see langword="true"/> when disposal is now this caller's duty.</returns>
    internal bool EndOperation() => _latch.End();

    /// <summary>
    /// Records that the handle has been released.
    /// </summary>
    /// <returns><see langword="true"/> when disposal is the releaser's duty.</returns>
    internal bool RequestRelease() => _latch.RequestRelease();

    /// <summary>
    /// Disposes the task exactly once, whichever path gets here first.
    /// </summary>
    internal void DisposeTask()
    {
        if (_latch.TryClaimDisposal())
        {
            // Outside the latch: a task's teardown is arbitrary work and must not run inside a critical
            // section every reader also enters.
            Task.Dispose();
        }
    }

    /// <summary>
    /// The caller identity this task is attributed to, for the per-caller ceiling.
    /// </summary>
    /// <value>
    /// The token subject, or the resolver's unattributed bucket. Set once by the registry at registration
    /// and never rendered into a response.
    /// </value>
    /// <remarks>
    /// A MUTABLE MEMBER ON A RECORD, deliberately, and it does not join the positional parameter list: the
    /// three positional members are the ORACLE's - the handle, its session and the task - while this one and
    /// the stamp below are properties of the BOUNDARY the registry owns. Keeping them out of the
    /// constructor keeps the record's shape a statement about the port rather than about the quota.
    /// </remarks>
    internal string Principal { get; set; } = HandlePrincipalResolver.Unattributed;

    /// <summary>
    /// When a call last named this task, as UTC ticks read from the injected clock.
    /// </summary>
    /// <remarks>
    /// Written and read through <see cref="System.Threading.Volatile"/> by the registry, because it is
    /// touched from arbitrary request threads and read by the reclaim pass on another.
    /// </remarks>
    internal long LastActivityTicks;
}

/// <summary>
/// The process-wide table mapping wire task handles onto the tasks they stand for.
/// </summary>
/// <remarks>
/// <para>
/// <b>THIS TABLE IS THE ONLY THING THE LEGACY DID NOT NEED.</b> In process the caller holds the task by
/// reference for as long as it wants it; across a boundary the caller holds a name, and something has to
/// map the name onto the object. Nothing else about task lifetime is added here - creation and teardown
/// remain the factory's and the task's.
/// </para>
/// <para>
/// <b>Ordinal comparison, deliberately.</b> A handle is an opaque server-issued token, so it is matched
/// byte for byte; culture-sensitive or case-insensitive matching would let two distinct tokens collide on
/// some hosts and not others.
/// </para>
/// <para>
/// <b>A handle is scoped to ONE service and ONE instance.</b> A query task handle is not valid here, and a
/// handle from another Persistence instance is not valid at all - task state is in-process, exactly as the
/// legacy object's was. Every unresolvable handle is one outcome rather than three: absent, blank and
/// stale all answer <c>RetCode.E_INVALID_HANDLE</c>, because distinguishing them would tell a caller which
/// of its guesses was closer to a live handle and would answer a question the oracle has no answer for.
/// </para>
/// </remarks>
internal sealed class UpdateTaskRegistry
{
    private readonly ConcurrentDictionary<string, UpdateTaskEntry> _tasks = new(StringComparer.Ordinal);

    /// <summary>The ceiling on live update tasks, per caller and in total.</summary>
    private readonly HandleQuota _quota;

    /// <summary>The one clock. Stamps activity and measures idleness.</summary>
    private readonly TimeProvider _time;

    /// <summary>Resolves the caller a new task is attributed to.</summary>
    private readonly HandlePrincipalResolver _principals;

    /// <summary>Optional structured logger, for reclaimed and drained tasks.</summary>
    private readonly ILogger<UpdateTaskRegistry>? _logger;

    /// <summary>
    /// Creates the registry over its ceilings and its clock.
    /// </summary>
    /// <param name="options">The bound settings the ceilings and the idle window come from.</param>
    /// <param name="time">The one clock, shared with the pool and the pooled transaction.</param>
    /// <param name="principals">
    /// Resolves the caller a new task is attributed to. Optional so the registry is constructible without a
    /// host, in which case every task is unattributed and only the total ceiling applies.
    /// </param>
    /// <param name="logger">Optional structured logger.</param>
    /// <exception cref="ArgumentNullException">A required collaborator is <see langword="null"/>.</exception>
    public UpdateTaskRegistry(
        IOptions<PersistenceOptions> options,
        TimeProvider time,
        HandlePrincipalResolver? principals = null,
        ILogger<UpdateTaskRegistry>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        HandleLifecycleOptions handles = options.Value.Handles;

        _quota = new HandleQuota("update task", handles.MaxTotalPerRegistry, handles.MaxPerPrincipal);
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _principals = principals ?? new HandlePrincipalResolver();
        _logger = logger;
    }

    /// <summary>
    /// The number of live tasks. Exposed for diagnostics and for assertions in tests.
    /// </summary>
    internal int Count => _tasks.Count;

    /// <summary>
    /// The session identity of every live task, so the session registry can pin what is still in use.
    /// </summary>
    internal IReadOnlyCollection<string> LiveSessionIds =>
        [.. _tasks.Values.Select(entry => entry.SessionId)];

    /// <summary>
    /// Issues a handle for a freshly created task and records it under that handle.
    /// </summary>
    /// <param name="sessionId">The session the task was created against.</param>
    /// <param name="task">The created task.</param>
    /// <returns>The registered entry, whose identity is the handle to return to the caller.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="task"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// <b>The identity is a fresh version-4 GUID rendered without separators</b>, which satisfies the
    /// contract's opacity requirement: it is unparseable, it discloses nothing about the task behind it,
    /// and it is not guessable from another handle. Deriving it from a counter would make it both
    /// parseable and predictable.
    /// </para>
    /// <para>
    /// <b>It is NOT a clock read</b>, so it does not touch this service's determinism posture - version 4
    /// GUIDs are random rather than time-based, and no wall clock is consulted anywhere in this file. A
    /// handle is nonetheless a non-deterministic value by construction, so a characterization comparison
    /// must mask it on BOTH sides.
    /// </para>
    /// <para>
    /// The insertion cannot collide in practice and is not permitted to overwrite in principle: a failure
    /// to add would mean a repeated GUID, which is a structural fault rather than a recoverable one, so the
    /// loop re-issues rather than silently replacing a live task some other caller still holds.
    /// </para>
    /// </remarks>
    internal UpdateTaskEntry? Register(string sessionId, IUpdateTaskSurface task, out string diagnostic)
    {
        ArgumentNullException.ThrowIfNull(task);

        // THE CEILING IS TESTED BEFORE THE HANDLE IS MINTED, so a refusal registers nothing and the caller
        // disposes the task it built.
        string principal = _principals.Resolve();

        if (!_quota.TryReserve(principal, out diagnostic))
        {
            return null;
        }

        while (true)
        {
            UpdateTaskEntry entry = new(Guid.NewGuid().ToString("N"), sessionId, task)
            {
                Principal = principal,
            };

            Volatile.Write(ref entry.LastActivityTicks, _time.GetUtcNow().UtcTicks);

            if (_tasks.TryAdd(entry.TaskId, entry))
            {
                return entry;
            }
        }
    }

    /// <summary>
    /// Resolves a wire handle onto its entry.
    /// </summary>
    /// <param name="handle">
    /// The handle from the request. <see langword="null"/>, an empty identity and an unknown identity are
    /// all treated identically - see this type's remarks.
    /// </param>
    /// <param name="entry">The resolved entry, or <see langword="null"/> when unresolved.</param>
    /// <returns><see langword="true"/> when <paramref name="handle"/> names a live task.</returns>
    internal bool TryResolve(TaskHandle? handle, out UpdateTaskEntry? entry)
    {
        string? taskId = handle?.TaskId;

        if (string.IsNullOrEmpty(taskId))
        {
            entry = null;

            return false;
        }

        if (!_tasks.TryGetValue(taskId, out entry))
        {
            return false;
        }

        // A HANDLE IN USE IS NOT AN ABANDONED HANDLE: refreshed on every call that names this task.
        Volatile.Write(ref entry.LastActivityTicks, _time.GetUtcNow().UtcTicks);

        return true;
    }

    /// <summary>
    /// Removes an entry from the table, so that its handle is single-use.
    /// </summary>
    /// <param name="taskId">The identity to remove.</param>
    /// <param name="entry">The removed entry, or <see langword="null"/> when this call did not remove it.</param>
    /// <returns>
    /// <see langword="true"/> when this call is the one that removed it. Concurrent callers releasing the
    /// same handle therefore produce exactly one removal, and the loser sees the same unknown-handle
    /// outcome as any other stale handle - which is what stops one task being disposed twice.
    /// </returns>
    internal bool TryRemove(string taskId, out UpdateTaskEntry? entry)
    {
        if (!_tasks.TryRemove(taskId, out entry) || entry is null)
        {
            return false;
        }

        // Returned by whichever call won the removal, so a concurrent second release cannot return it
        // twice and a caller cannot free quota it never held.
        _quota.Release(entry.Principal);

        return true;
    }

    /// <summary>
    /// Removes and disposes every task idle for longer than <paramref name="window"/>.
    /// </summary>
    /// <param name="now">The reclaim pass's single clock read.</param>
    /// <param name="window">How long a task may go untouched before it is considered abandoned.</param>
    /// <returns>How many tasks were reclaimed.</returns>
    /// <remarks>
    /// AN IDLE TASK IS ALMOST NEVER A RUNNING ONE, AND THE HANDOFF COSTS NOTHING TO BE SURE. Every C-06
    /// operation is unary and each one refreshes the stamp on the way in, so a task in use is not normally
    /// near the window - which is far longer than any update this contract declares can take. "Not normally"
    /// is not "never" though: an update against a contended file-backed store can outrun the window, and a
    /// direct teardown would then destroy an object with work still pending against it, which is hazard 1
    /// [<c>docs/PB多线程绕坑提示.md</c>]. The release/disposal handoff makes the reclaim identical to an
    /// explicit release: whichever of the two paths finishes last performs the teardown, exactly once.
    /// </remarks>
    internal int ReclaimIdle(DateTimeOffset now, TimeSpan window)
    {
        long threshold = now.UtcTicks - window.Ticks;
        int reclaimed = 0;

        foreach (UpdateTaskEntry candidate in _tasks.Values)
        {
            if (Volatile.Read(ref candidate.LastActivityTicks) > threshold
                || !TryRemove(candidate.TaskId, out UpdateTaskEntry? removed)
                || removed is null)
            {
                continue;
            }

            // THE RELEASE/DISPOSAL HANDOFF, NOT A DIRECT TEARDOWN. Reaching straight for the worker's
            // Dispose - which is what this loop used to do - tears the task down even while an operation
            // owns it, and destroying an object with work still pending against it is hazard 1
            // [docs/PB多线程绕坑提示.md]. RequestRelease records the release and answers whether
            // disposal is THIS caller's duty: false while an operation is in flight, in which case that
            // operation's EndOperation disposes on its way out. Exactly one of the two disposes, always.
            if (removed.RequestRelease())
            {
                removed.DisposeTask();
            }

            reclaimed++;

            _logger?.LogWarning(
                "Reclaimed an abandoned update task held by caller {Principal} against session "
                + "{SessionId}. The handle value is deliberately not recorded.",
                LogSafeText.Render(removed.Principal),
                LogSafeText.Render(removed.SessionId));
        }

        return reclaimed;
    }


    /// <summary>
    /// Removes and disposes every task owned by a retired transaction session.
    /// </summary>
    /// <param name="sessionId">The session that has ended.</param>
    /// <returns>The number of tasks retired.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="sessionId"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// <b>A TASK CANNOT OUTLIVE THE SESSION IT WAS CREATED AGAINST, AND LEAVING ONE BEHIND IS NOT MERELY
    /// UNTIDY.</b> Every task in this table holds the session's pooled transaction, so a task that
    /// survives its session names a transaction the pool has already taken back - and the pool may hand
    /// that same transaction to a DIFFERENT session as soon as its reference count drops
    /// [<c>n_cst_thread_trans_pool.sru:L94-L114</c>]. A later call on the stale handle would then write
    /// through somebody else's transaction, which is a data-integrity fault rather than a leak.
    /// </para>
    /// <para>
    /// REMOVAL THROUGH <see cref="TryRemove"/> AND NOT PAST IT, so the handle ceiling this registry
    /// reserved on registration is released with the entry. Reaching into the dictionary directly would
    /// retire the task and keep its slot reserved for the life of the process - and because the ceiling is
    /// per principal, the caller whose session ended is precisely the caller that would later be refused a
    /// handle it is entitled to.
    /// </para>
    /// <para>
    /// Removal comes before disposal for each entry, so a concurrent call on the same handle loses the
    /// race in the table rather than reaching a half-disposed task.
    /// </para>
    /// </remarks>
    internal int PurgeSession(string sessionId)
    {
        ArgumentNullException.ThrowIfNull(sessionId);

        int retired = 0;

        foreach (UpdateTaskEntry candidate in _tasks.Values)
        {
            if (!string.Equals(candidate.SessionId, sessionId, StringComparison.Ordinal))
            {
                continue;
            }

            if (!TryRemove(candidate.TaskId, out UpdateTaskEntry? removed) || removed is null)
            {
                continue;
            }

            // THE RELEASE/DISPOSAL HANDOFF, NOT A DIRECT TEARDOWN. Reaching straight for the worker's
            // Dispose - which is what this loop used to do - tears the task down even while an operation
            // owns it, and destroying an object with work still pending against it is hazard 1
            // [docs/PB多线程绕坑提示.md]. RequestRelease records the release and answers whether
            // disposal is THIS caller's duty: false while an operation is in flight, in which case that
            // operation's EndOperation disposes on its way out. Exactly one of the two disposes, always.
            if (removed.RequestRelease())
            {
                removed.DisposeTask();
            }

            retired++;
        }

        return retired;
    }

    /// <summary>
    /// Removes and disposes every live task, whatever its age.
    /// </summary>
    /// <returns>How many tasks were released.</returns>
    /// <remarks>
    /// FOR SHUTDOWN, AND IT MUST RUN BEFORE THE SESSION REGISTRY DRAINS: a task borrows the transaction its
    /// session owns, and an update task additionally holds a worker task.
    /// </remarks>
    internal int Drain()
    {
        int drained = 0;

        foreach (UpdateTaskEntry candidate in _tasks.Values)
        {
            if (!TryRemove(candidate.TaskId, out UpdateTaskEntry? removed) || removed is null)
            {
                continue;
            }

            // THE RELEASE/DISPOSAL HANDOFF, NOT A DIRECT TEARDOWN. Reaching straight for the worker's
            // Dispose - which is what this loop used to do - tears the task down even while an operation
            // owns it, and destroying an object with work still pending against it is hazard 1
            // [docs/PB多线程绕坑提示.md]. RequestRelease records the release and answers whether
            // disposal is THIS caller's duty: false while an operation is in flight, in which case that
            // operation's EndOperation disposes on its way out. Exactly one of the two disposes, always.
            if (removed.RequestRelease())
            {
                removed.DisposeTask();
            }

            drained++;
        }

        return drained;
    }
}

// --------------------------------------------------------------------------------------------------
//  PART 3 OF 4 - THE ADAPTER AND ITS TRANSLATIONS
// --------------------------------------------------------------------------------------------------

/// <summary>
/// Serves contract C-06, <c>persistence.v1.UpdateService</c>: the update third of the
/// retrieval / validation / update triple, and the contract that carries optimistic-concurrency
/// semantics across the boundary.
/// </summary>
/// <remarks>
/// <para>
/// <b>EVERY METHOD IS AUTHORIZED, AND THE ENFORCEMENT IS DELIBERATELY NOT HERE (constraint C-G).</b>
/// <c>Program.cs</c> installs a fallback authorization policy requiring an authenticated principal, so an
/// endpoint with no authorization metadata of its own is a CLOSED door rather than an open one. This class
/// therefore carries no <c>[Authorize]</c> attribute - it would be redundant - and, more to the point, it
/// carries NO <c>[AllowAnonymous]</c> and no per-method escape of any kind. Inbound tokens are VALIDATED
/// by the stock bearer handler against Security's published key set; this service holds VERIFICATION
/// MATERIAL ONLY, mints nothing, and no signing key appears anywhere in this file.
/// </para>
/// <para>
/// <b>EVERY COLLABORATOR ARRIVES BY CONSTRUCTOR INJECTION (constraint C-H).</b> Nothing is reached
/// statically except the two sanctioned projections that are deliberately static so they cannot be
/// substituted, nothing is newed up internally except the response messages, and NO CLOCK IS READ - there
/// is no <c>DateTime.UtcNow</c>, no <c>Environment.TickCount</c> and no <c>Stopwatch</c> below. That is
/// what keeps the per-service coverage gate reachable through substitution and what keeps paired
/// characterization recordings comparable.
/// </para>
/// <para>
/// <b>THE ONE STATUS THIS SERVICE RAISES RATHER THAN REPORTS.</b> <c>persistence.v1.OperationStatus</c> is
/// the uniform outcome of every unary call in the protocol definition, so a guard refusal, a driver
/// failure and a cancellation are all REPORTED in <c>ret_code</c>. A concurrency mismatch is the single
/// exception, and it is an exception by contract rather than by preference: the detail cannot ride on a
/// response field without creating a second way to report one condition, so it rides on the status, and a
/// status can only be raised. See <see cref="Update"/>.
/// </para>
/// </remarks>
// ============ THE SCOPE THIS CONTRACT REQUIRES (constraint C-G) ============
// C-06 generates and executes INSERT, UPDATE and DELETE statements
// [ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru:L204], so every RPC on this
// contract needs the WRITE scope - including the task lifecycle and the prepare, because a
// descriptor array is the thing that decides WHICH table the update lands on.
//
// The route mapping in Program.cs additionally requires an authenticated principal, and the
// fallback policy would close the door even if the mapping forgot to. This attribute is the
// LEAST-PRIVILEGE half: authentication alone would let a credential minted for one contract
// reach all four.
[Authorize(Policy = PersistenceAuthorizationPolicies.Write)]
internal sealed class UpdateService : GeneratedUpdateServiceBase
{
    /// <summary>
    /// The diagnostic for a handle that names no live task. One sentence, quoting no value, for the
    /// absent, blank and stale cases alike - see <see cref="UpdateTaskRegistry"/> for why they are one
    /// outcome.
    /// </summary>
    private const string UnknownTaskDiagnostic =
        "The update task handle does not name a live task on this Persistence instance. A handle is "
        + "server-issued, single-use on release, and not portable across instances.";

    /// <summary>
    /// The diagnostic for a request that names no transaction session.
    /// </summary>
    /// <remarks>
    /// An update task runs against a session issued by C-08's <c>BeginSession</c>; the legacy equivalent
    /// is a task that was handed a transaction descriptor it could not resolve, which the oracle refuses
    /// with the same code [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:L113</c>].
    /// </remarks>
    private const string MissingSessionDiagnostic =
        "CreateUpdateTask requires a transaction session handle issued by BeginSession.";

    /// <summary>
    /// The diagnostic for a create whose session began retiring before the task could be published.
    /// </summary>
    /// <remarks>
    /// A DISTINCT MESSAGE FROM <see cref="MissingSessionDiagnostic"/>, because the caller's position is
    /// different: it DID supply a handle and that handle WAS live when it sent the request. No task handle
    /// was issued, so there is nothing for it to release and its only recourse is a new session. The code is
    /// <c>E_INVALID_TRANSACTION</c> - the one the oracle answers for a transaction it cannot use
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:L113</c>] - and no handle value is
    /// quoted (constraint C-F).
    /// </remarks>
    private const string SessionClosingOnCreateDiagnostic =
        "The transaction session named by this request began retiring before the update task could be "
        + "published against it, so no task was created and no handle was issued. Open a new session and "
        + "retry.";

    /// <summary>
    /// The diagnostic accompanying <c>E_BUSY</c> when another operation already owns the task.
    /// </summary>
    /// <remarks>
    /// It says RETRY, because <c>E_BUSY</c> is the retryable refusal: the same call unchanged succeeds once
    /// the operation in flight finishes. That is the distinction from <see cref="UnknownTaskDiagnostic"/>,
    /// which no retry can ever satisfy, and it is the reason the two are separate codes rather than one
    /// generic failure [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlbase.sru:L57</c>].
    /// </remarks>
    private const string TaskBusyDiagnostic =
        "Another prepare, update or reset is in flight for this update task, so the request was refused. "
        + "Retry once it has completed.";

    /// <summary>
    /// The diagnostic for a request whose descriptor names a different update table from the one the data
    /// object's own definition declares, while multi-table update is off.
    /// </summary>
    /// <remarks>
    /// IT NAMES THE REMEDY AND QUOTES NOTHING. The refusal exists because that combination can never take
    /// effect and would otherwise be reported as a success while the update reached a different table
    /// entirely; the message therefore has to tell a caller which of the two things it meant, and it does
    /// so without echoing a table name, a column name or a value (constraint C-F) - the caller knows both
    /// names already, and a log record must not carry either.
    /// </remarks>
    internal const string DescriptorsWithoutMultiTableDiagnostic =
        "A descriptor in this request names a different update table from the one the data object's own "
        + "definition declares, while multi_table_update is false. In that mode the descriptor array is "
        + "never applied - the data object's definition governs the update table, the key columns and the "
        + "identity column - so the write would have reached the table the descriptor did NOT name and been "
        + "reported as a success. Set multi_table_update to true to have the descriptors applied, send a "
        + "descriptor that agrees with the definition, or send no descriptors at all.";

    private readonly IUpdateTaskFactory _factory;
    private readonly UpdateTaskRegistry _tasks;

    /// <summary>The definition registry a descriptor's update table is compared against.</summary>
    private readonly DataObjectDefinitionCatalogue _definitions;

    private readonly ILogger<UpdateService>? _logger;

    /// <summary>
    /// Creates the adapter over its collaborators.
    /// </summary>
    /// <param name="factory">
    /// The task factory - the wire form of the legacy object creation. It owns the seven collaborators a
    /// worker/proxy pair needs and resolves the session; this service only calls into it.
    /// </param>
    /// <param name="tasks">The handle-to-task table. Must be the process singleton.</param>
    /// <param name="logger">
    /// Optional structured logger. Optional rather than required so a unit test can construct the service
    /// with nothing but its behavioural collaborators.
    /// </param>
    /// <exception cref="ArgumentNullException">A required collaborator is <see langword="null"/>.</exception>
    /// <remarks>
    /// <b>NO <c>ISqlRedactor</c> IS INJECTED, AND THAT IS THE STRONGER CHOICE (constraint C-F).</b> The
    /// sanctioned projection in <c>Errors/SqlRedactor.cs</c> reaches its redaction policy directly rather
    /// than accepting one, precisely so the masking of the statement field cannot be weakened from a call
    /// site or from a container registration. Injecting a redactor here would make the policy replaceable,
    /// which is the opposite of the guarantee - and in any case every database error reaching this file has
    /// already been projected, so there is nothing here left to redact.
    /// </remarks>
    public UpdateService(
        IUpdateTaskFactory factory,
        UpdateTaskRegistry tasks,
        DataObjectDefinitionCatalogue definitions,
        ILogger<UpdateService>? logger = null)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _tasks = tasks ?? throw new ArgumentNullException(nameof(tasks));

        // REQUIRED RATHER THAN OPTIONAL, because it is what the prepare boundary compares a descriptor's
        // update table against. An optional dependency here would let the misdirection check silently
        // disappear on a container that did not happen to register the catalogue - a safety check that
        // no-ops on misconfiguration is worse than one that fails to start.
        _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
        _logger = logger;
    }

    /// <summary>
    /// Projects one run's outcome onto the wire status, mapping the classification EXHAUSTIVELY.
    /// </summary>
    /// <param name="result">The run's result.</param>
    /// <returns>The status to report.</returns>
    /// <exception cref="InvalidOperationException">
    /// The classification carries a kind this mapping does not recognise - a structural fault, never a
    /// domain outcome. See the remarks.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>THE CODE ALWAYS COMES FROM THE RUN AND NEVER FROM THE CLASSIFICATION.</b> The two disagree
    /// whenever the epilogue intervenes - a cancellation observed after the teardown replaces whatever the
    /// body answered [<c>:L381-L383</c>], and a failing autocommit replaces a success [<c>:L386-L392</c>] -
    /// so reading the code off the classification would report a success the caller did not get. What the
    /// classification decides is the SHAPE of the status: whether a diagnostic accompanies the code, and
    /// whether the driver-level detail does.
    /// </para>
    /// <para>
    /// <b>NO ARM SILENTLY SWALLOWS ANYTHING.</b> The five real kinds are mapped by name and the residual
    /// arm THROWS rather than defaulting, so a kind added to the classification in future is a loud
    /// failure at the boundary instead of a status that looks plausible. <c>UpdateOutcomeKind.None</c>
    /// reaches that arm and is unreachable by construction - the outcome's factories require a kind and
    /// none of them produces it - which is exactly why a throw is the right answer for it: it is a
    /// structural fault, and the framework's own posture for a structural fault is to stop rather than to
    /// continue degraded [<c>ws_objects/pfw.pbl.src/pfw.sra:L111-L144</c>].
    /// </para>
    /// <para>
    /// <b>THE DRIVER DETAIL IS TAKEN FROM A SANCTIONED PRODUCER OR NOT AT ALL (constraint C-F).</b>
    /// First choice is the classification's own projection, which goes through
    /// <c>DbErrorDataExtensions.ToDbError</c>; second is the error the run latched, which the task surface
    /// has already projected through the same mapper. Nothing here assembles a payload field by field, and
    /// <c>DbError.Sqlsyntax</c> is never assigned.
    /// </para>
    /// </remarks>
    /// <summary>
    /// Takes an entry's operation lease, or produces the refusal to report.
    /// </summary>
    /// <param name="entry">The resolved entry.</param>
    /// <param name="refusal">The status to report, valid only when this returns <see langword="false"/>.</param>
    /// <returns><see langword="true"/> when the caller owns the task and MUST call <see cref="ReleaseLease"/>.</returns>
    /// <remarks>
    /// <b>ONE PLACE FOR THE THREE HANDLERS THAT NEED IT.</b> Reset, prepare and update all take the same
    /// lease and report the same two refusals, and writing the mapping out three times would give three
    /// places for one of them to answer the wrong code. <c>E_BUSY</c> is retryable and
    /// <c>E_INVALID_HANDLE</c> is not, so the distinction is advice a caller acts on rather than
    /// bookkeeping - which is why the lease reports WHICH rather than leaving the call site to read the
    /// state again and race.
    /// </remarks>
    private static bool TryLease(UpdateTaskEntry entry, out OperationStatus refusal)
    {
        TaskLatchOutcome lease = entry.TryBeginOperation();

        if (lease == TaskLatchOutcome.Acquired)
        {
            refusal = null!;

            return true;
        }

        refusal = lease == TaskLatchOutcome.Gone
            ? UpdateWireCodes.Status(RetCode.E_INVALID_HANDLE, UnknownTaskDiagnostic)
            : UpdateWireCodes.Status(RetCode.E_BUSY, TaskBusyDiagnostic);

        return false;
    }

    /// <summary>
    /// Gives an entry's operation lease back, disposing the task when a release arrived meanwhile.
    /// </summary>
    /// <param name="entry">The entry whose lease this caller holds.</param>
    /// <remarks>
    /// ALWAYS FROM A <see langword="finally"/>. An operation that returns without releasing pins the task,
    /// and every later call on that handle then answers <c>E_BUSY</c> for the life of the process.
    /// </remarks>
    private static void ReleaseLease(UpdateTaskEntry entry)
    {
        if (entry.EndOperation())
        {
            entry.DisposeTask();
        }
    }

    /// <summary>
    /// Decides whether a descriptor sent with the multi-table switch off - and therefore never applied -
    /// contradicts the definition that WILL govern the update, and composes the refusal when it does.
    /// </summary>
    /// <param name="request">The prepare request.</param>
    /// <param name="entry">The task the request addresses.</param>
    /// <param name="refusal">
    /// Receives the status to answer. Undefined when this method answers <see langword="false"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the request must be refused; <see langword="false"/> when every
    /// descriptor agrees with the definition, or when no comparison is possible.
    /// </returns>
    /// <remarks>
    /// <para>
    /// TWO CONTRADICTIONS ARE REFUSED, EACH WITH THE CODE ITS OWN PATH ALREADY USES:
    /// </para>
    /// <para>
    /// (1) A DIFFERENT UPDATE TABLE - <c>E_INVALID_ARGUMENT</c>. This is the misdirection: the write lands
    /// in the table the definition declares while the caller named another, and the response says success.
    /// The comparison is case-insensitive, because a table name is an identifier and SQLite compares
    /// identifiers without regard to case - treating <c>company</c> and <c>COMPANY</c> as different tables
    /// would refuse a descriptor that names the very table the definition declares. Ordinal-ignore-case
    /// rather than culture-aware, because an identifier's identity must not depend on the host's culture.
    /// </para>
    /// <para>
    /// (2) A COLUMN NAME THE DEFINITION DOES NOT DECLARE - <c>E_INTERNAL_ERROR</c> with 无效的列名: and the
    /// name, which is the oracle's own arm and wording for a column name that will not resolve
    /// [<c>n_cst_thread_task_sqlupdate.sru:L118-L122</c>]. It is the SAME answer this descriptor would get
    /// with the switch ON, arriving one call earlier - so the two paths agree rather than one accepting what
    /// the other refuses. Only EXISTENCE is checked, never the update, key or identity FLAGS: a caller may
    /// legitimately declare a subset, and with the switch off the definition's own flags govern anyway.
    /// </para>
    /// <para>
    /// THE GOVERNING DATA OBJECT IS THE REQUEST'S OWN WHEN IT NAMES ONE, and otherwise the one the task
    /// already holds - because the two source setters are presence-gated, so an unstated source survives
    /// from an earlier prepare and is what the update will actually run against.
    /// </para>
    /// <para>
    /// A REQUEST NAMING A SQL SYNTAX IS NEVER COMPARED. The syntax setter clears the data object
    /// [<c>:L271</c>], so whatever the task held is about to stop governing, and both the update table and
    /// the column model then come from the syntax - which this boundary does not parse. Comparing against a
    /// name that is about to be cleared would refuse a request for a reason that no longer applies to it.
    /// </para>
    /// <para>
    /// AN UNRESOLVABLE DEFINITION IS NEVER COMPARED EITHER, and a retrieve-only one is exempt from the TABLE
    /// test specifically. That is a deliberate limit rather than an oversight: neither can misdirect a write,
    /// because an update against either fails for want of an update table before any statement reaches the
    /// storage engine. Refusing them here would narrow the contract without protecting anything.
    /// </para>
    /// </remarks>
    private bool TryRefuseInertDescriptor(
        PrepareUpdateRequest request,
        UpdateTaskEntry entry,
        out OperationStatus refusal)
    {
        refusal = null!;

        if (request.HasSqlSyntax)
        {
            return false;
        }

        string dataObject = request.HasDataObject ? request.DataObject : entry.Task.DataObject;

        if (dataObject.Length == 0
            || !_definitions.TryResolve(dataObject, out DataObjectDefinitionEntry? definition)
            || !_definitions.TryResolveUpdateSettings(dataObject, out DataObjectUpdateSettings? settings))
        {
            return false;
        }

        foreach (TableUpdateContract table in request.Tables)
        {
            if (settings.Table.Length != 0
                && !string.Equals(table.Name, settings.Table, StringComparison.OrdinalIgnoreCase))
            {
                // THE LOG NAMES NEITHER TABLE (constraint C-F). A caller knows both names already, so the
                // record carries only the fact and the count.
                _logger?.LogWarning(
                    "PrepareUpdate refused {TableCount} update-table descriptor(s) on task {TaskId}: "
                    + "multi-table update is off, so the descriptor array is never applied and the data "
                    + "object's own definition governs - and a descriptor names a different update table "
                    + "from the one that definition declares. No table name, column name or value is "
                    + "recorded.",
                    request.Tables.Count,
                    LogSafeText.Render(entry.TaskId));

                refusal = UpdateWireCodes.Status(
                    RetCode.E_INVALID_ARGUMENT,
                    DescriptorsWithoutMultiTableDiagnostic);

                return true;
            }

            if (!TryFindUndeclaredColumn(table, definition, out string undeclared))
            {
                continue;
            }

            // THE COLUMN NAME IS NOT LOGGED EITHER, for the same reason the table name is not: the caller
            // sent it and gets it back in the response, and a log record is read by someone who did not.
            _logger?.LogWarning(
                "PrepareUpdate refused an update-table descriptor on task {TaskId}: it names a column the "
                + "governing data object's definition does not declare. No table name, column name or "
                + "value is recorded.",
                LogSafeText.Render(entry.TaskId));

            refusal = UpdateWireCodes.Status(
                RetCode.E_INTERNAL_ERROR,
                UpdateWhereBuilder.InvalidColumnNameMessage + undeclared);

            return true;
        }

        return false;
    }

    /// <summary>
    /// Finds the first column name a descriptor declares that its definition does not.
    /// </summary>
    /// <param name="table">The descriptor.</param>
    /// <param name="definition">The governing definition and its declared columns.</param>
    /// <param name="undeclared">Receives the offending name, or the empty string when every name resolves.</param>
    /// <returns><see langword="true"/> when a name does not resolve.</returns>
    /// <remarks>
    /// THE ORACLE'S OWN VISIT ORDER - updatable columns, then key columns, then the identity column
    /// [<c>:L111-L129</c>] - so the name reported is the one the oracle would have failed on first. An EMPTY
    /// identity column is legal and is not a name at all [<c>:L127-L129</c>], so it is skipped rather than
    /// refused; an empty entry in either ARRAY is refused, because the oracle's script grammar cannot carry
    /// one and its own guard already rejects it.
    /// </remarks>
    private static bool TryFindUndeclaredColumn(
        TableUpdateContract table,
        DataObjectDefinitionEntry definition,
        out string undeclared)
    {
        foreach (string column in table.Updatablecolumns)
        {
            if (!Declares(definition, column))
            {
                undeclared = column;

                return true;
            }
        }

        foreach (string column in table.Keycolumns)
        {
            if (!Declares(definition, column))
            {
                undeclared = column;

                return true;
            }
        }

        if (table.Identitycolumn.Length != 0 && !Declares(definition, table.Identitycolumn))
        {
            undeclared = table.Identitycolumn;

            return true;
        }

        undeclared = string.Empty;

        return false;

        static bool Declares(DataObjectDefinitionEntry definition, string column)
        {
            foreach (DeclaredDataObjectColumn declared in definition.Columns)
            {
                if (string.Equals(declared.Name, column, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }

    private static OperationStatus ProjectStatus(UpdateRunResult result)
    {
        // ============ THE RUN'S DIAGNOSTIC IS MASKED ONCE, HERE, FOR EVERY ARM BELOW ================
        // The run's text is whatever the worker raised into the caller-side collector, and ONE of the
        // arms that raises is the transaction's own message: `Event OnError(rtCode, transObject.SQLErrText)`
        // [ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru:L390, reproduced at
        // Tasks/SqlUpdateTask.cs]. What SQLite puts in that message routinely includes the caller's own
        // data - a uniqueness violation names the duplicated column, a constraint or type failure quotes
        // the offending value - so relaying it verbatim would publish row data through a field documented
        // as opaque display text. The legacy could publish it safely because it published nothing: it is a
        // library, and the message never left the process.
        //
        // MASKED ONCE RATHER THAN PER ARM, because five of the six arms below pass this same text and a
        // per-arm mask would be five places for one of them to be forgotten. The mask is LITERAL-SCOPED,
        // so the framework-authored sentences that also reach here - 无效的SQL!, SQL参数绑定失败!,
        // 没有设置可更新表!, 无效的更新数据!, 无效的数据源对象! and 无效的列名: plus a column name - quote no
        // literal and arrive byte for byte (constraints C-F, C-B). The driver PAYLOAD is masked separately
        // and already, by the one sanctioned projection in Errors/SqlRedactor.cs.
        string errorText = SqlRedactor.Instance.Redact(result.ErrorText);

        // ============ THE UPDATE WAS NEVER ATTEMPTED, WHICH IS AN ORDINARY OUTCOME ==================
        // Every arm of ondotask that returns before _of_Update leaves nothing to classify: the failed
        // transaction acquisition [:L292-L295], the cancellation check before the body [:L301], the
        // carrier that could not be created [:L317-L320], the missing source [:L326-L330], the payload
        // arms [:L336-L346], the transaction attachment [:L350-L354] and the empty descriptor array under
        // the multi-table switch [:L359-L363]. The run's own code and text are the whole answer, and the
        // latched driver error is what carries the acquisition failure's detail [:L293].
        if (result.Outcome is not { } outcome)
        {
            return UpdateWireCodes.Status(result.Code, errorText, result.LastDbError);
        }

        // The classification's own payload when it has one; otherwise whatever the run latched. Both are
        // already-projected wire messages produced by the one sanctioned mapper.
        DbError? dbError = outcome.TryProjectDbError(out DbError? projected)
            ? projected
            : result.LastDbError;

        return outcome.Kind switch
        {
            // [:L214, :L248] The update answered the DataWindow contract's success value - which is 1,
            // NOT the zero of the return-code algebra - and neither cancellation check fired. A successful
            // classification carries no driver payload by construction, and the only way the code can be
            // anything but OK here is the epilogue: a cancellation seen after the teardown, or a failing
            // autocommit whose raise is on the GENERAL channel only [:L390], never on the database one.
            // So this arm passes the run's text and deliberately no detail.
            UpdateOutcomeKind.Succeeded =>
                UpdateWireCodes.Status(result.Code, errorText),

            // CANCELLED, from any of three distinct arms: the check before the update [:L182], a CLEAN
            // VETO [:L201], or the check after it [:L212] - plus the epilogue's own rewrite [:L381-L383].
            // NO TEXT AND NO DETAIL, and both omissions are the oracle's: the clean veto raises nothing at
            // all, and the epilogue explicitly SUPPRESSES the error raise for exactly this code
            // [:L396-L400]. Recall CANCELLED is NEITHER succeeded nor failed in the legacy algebra, so it
            // cannot be folded into either neighbour.
            UpdateOutcomeKind.Cancelled =>
                UpdateWireCodes.Status(result.Code),

            // [:L180] and, for the ondotask-level twin of the same condition, [:L292-L295]. The oracle
            // raises BOTH channels there - the database detail and then the general code with the driver's
            // own text - so both travel.
            UpdateOutcomeKind.InvalidTransaction =>
                UpdateWireCodes.Status(result.Code, errorText, dbError),

            // E_DB_ERROR, which the oracle reaches three different ways and lumps together: the sentinel
            // update table [:L188-L193], A VETO COMBINED WITH A TRANSACTION FAILURE [:L196-L199], and any
            // update result other than the success value [:L249-L250]. The first two raise both channels;
            // the third raises neither itself and the epilogue supplies the text [:L398].
            //
            // ⚠️ THE VETO DISCRIMINATION IS THE CLASSIFIER'S AND IS NOT FLATTENED HERE. A veto whose
            // transaction reports failure - `SQLCode < 0`
            // [ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:L337] - arrives on THIS arm, while
            // a clean veto arrives on the cancelled arm above. Two arms, two statuses, exactly as the
            // oracle keeps them.
            UpdateOutcomeKind.DatabaseError =>
                UpdateWireCodes.Status(result.Code, errorText, dbError),

            // UNREACHABLE ON THIS PATH, AND NOT AN OVERSIGHT. Update() raises a conflict as the Aborted
            // status before reaching here, so the only way to arrive is a conflict classification whose
            // detail is missing - which the outcome's factory forbids. Reporting the narrowing's own code,
            // which is E_DB_ERROR unchanged, is the correct non-lossy answer for it: the contract states
            // an Aborted response NEVER carries an empty row list, so raising Aborted with nothing in it
            // would report a conflict the caller cannot act on, and reporting a success would be the
            // silent overwrite this system does not have.
            UpdateOutcomeKind.Conflict =>
                UpdateWireCodes.Status(result.Code, errorText, dbError),

            // E_INVALID_DATA - the caller's payload flagged a row modified and supplied no updatable
            // column value for it, so no assignment could be generated. THE DIAGNOSTIC TRAVELS AND NO
            // DRIVER DETAIL DOES, and both halves matter: the text names the rule and the offending row
            // COUNT so a caller can see which field to add, while there is no database error to carry
            // because no statement ever reached the storage engine. Kept apart from the conflict arm above
            // on purpose - a caller that read this as a concurrency mismatch would re-read, rebase and
            // resend the same unusable payload for ever.
            UpdateOutcomeKind.InvalidUpdateData =>
                UpdateWireCodes.Status(result.Code, errorText),

            // 🔴 E_INVALID_DATA AGAIN, AND THE DRIVER DETAIL TRAVELS WITH IT - which is the difference
            // from the arm above and the reason the two are separate kinds. The storage engine refused a
            // row on a constraint the caller's payload controls, and the provider's own message is what
            // NAMES the offending column: `NOT NULL constraint failed: COMPANY.NAME`. That is schema
            // metadata rather than row data, and the redactor's provider-envelope rule is what lets it
            // survive masking while a value quoted inside the message does not.
            //
            // This arm used to be UpdateOutcomeKind.DatabaseError, which publishes as HTTP 502 - so a
            // caller who omitted a required column was told the DATABASE had failed. The classifier's own
            // predicate decides membership; only constraints a corrected payload can satisfy arrive here.
            UpdateOutcomeKind.ConstraintViolation =>
                UpdateWireCodes.Status(result.Code, errorText, dbError),

            _ => throw new InvalidOperationException(
                "The update classification carried an outcome kind this boundary does not map: "
                + outcome.Kind.ToString()
                + ". Every kind must be mapped explicitly, because a default status would misreport an "
                + "outcome the caller cannot detect. UpdateOutcomeKind.None is unreachable through the "
                + "sanctioned factories and reaching it is a structural fault."),
        };
    }

    // ==============================================================================================
    //  PART 4 OF 4 - THE FIVE RPCs, IN THE ORDER THE CONTRACT DECLARES THEM
    //  --------------------------------------------------------------------------------------------
    //  Every handler completes SYNCHRONOUSLY and returns a completed task. That is not a shortcut: the
    //  task surface is synchronous because the oracle's update path is - `ondotask` is a straight-line
    //  event [:L284-L404] - and inventing an asynchronous seam would add a continuation the legacy has
    //  no equivalent of while making the worker's thread affinity harder rather than easier to keep.
    //
    //  FOUR OF THE FIVE NEVER THROW. persistence.v1.OperationStatus is the uniform outcome of every
    //  unary call in the protocol definition, so an outcome is REPORTED rather than raised: a caller
    //  reads ret_code and branches on its SPECIFIC VALUE, never on a two-way success test, because the
    //  algebra is tri-state - PREVENT reads as a success and CANCELLED is neither succeeded nor failed.
    //  Update is the exception, and the ONLY exception, for the contract reason recorded on it.
    // ==============================================================================================

    /// <summary>
    /// Creates a server-held update task bound to a transaction session - the wire form of the legacy
    /// object creation.
    /// </summary>
    /// <param name="request">The session the task will run against.</param>
    /// <param name="context">
    /// The call context. Its cancellation token is threaded into the wait for the session's publication
    /// window, so a caller that goes away while queued behind another operation stops waiting.
    /// </param>
    /// <returns>The outcome and, on success, the task handle.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// <b>THERE IS NO LEGACY ANALOGUE FOR THIS CALL, WHICH IS WHY IT EXISTS.</b> In process the caller
    /// creates <c>n_cst_threading_task_sqlupdate</c> and holds it by reference; across a boundary it holds
    /// a name instead. The task is STATEFUL - its descriptor array, autocommit flag, multi-table switch,
    /// source and payload all live on the object between calls [<c>:L27-L39</c>] - so a stateless
    /// alternative could not express the per-setter return code, the reset guard or incremental
    /// reconfiguration between runs.
    /// </para>
    /// <para>
    /// A missing or blank session is <c>RetCode.E_INVALID_TRANSACTION</c>, the code the oracle answers when
    /// asked to act on a transaction it cannot use
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:L113</c>]. A factory that answered
    /// success without producing a task is a structural impossibility rather than a domain outcome, and it
    /// is reported as <c>RetCode.E_INVALID_OBJECT</c> - the code the pool uses when it cannot produce an
    /// object [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans_pool.sru:L174</c>] - rather than
    /// dereferenced.
    /// </para>
    /// <para>
    /// <b>PUBLICATION IS ATOMIC WITH THE SESSION'S LIVENESS, AND ONLY PUBLICATION IS.</b> Building the task
    /// touches nothing but the unpublished task, so it runs outside the session's lifecycle gate. The step
    /// that has to be atomic is the one that makes the task REACHABLE: <c>EndSession</c> marks its session
    /// closing inside that gate and only then walks this registry, so a registration performed outside the
    /// gate could land after the walk and leave a task holding a transaction the pool has already taken
    /// back - and may already have handed to a different session. The window comes from the factory, which
    /// is this contract's session authority; see <see cref="IUpdateTaskFactory.EnterPublicationAsync"/> and
    /// <see cref="UpdateTaskPublication"/>. Either the registration precedes the closing mark and the walk
    /// finds the task, or it reaches the gate after the mark and is refused with
    /// <c>E_INVALID_TRANSACTION</c>; there is no third interleaving.
    /// </para>
    /// </remarks>
    public override async Task<CreateUpdateTaskResponse> CreateUpdateTask(
        CreateUpdateTaskRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        string? sessionId = request.Session?.SessionId;

        if (string.IsNullOrEmpty(sessionId))
        {
            return new CreateUpdateTaskResponse
            {
                Status = UpdateWireCodes.Status(
                    RetCode.E_INVALID_TRANSACTION,
                    MissingSessionDiagnostic),
            };
        }

        long created = _factory.TryCreate(sessionId, out IUpdateTaskSurface? task);

        if (created != RetCode.OK || task is null)
        {
            // Defensive ownership transfer. The factory contract states that a refusal yields a null
            // surface, but if one ever arrives alongside a non-OK code this adapter does not register it,
            // so nothing else in the process would ever release it. Disposing it here keeps the pool
            // reference count honest, matching the oracle's decrement on release
            // [ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans_pool.sru:L174]. On the sanctioned
            // path this is a no-op because the surface is null.
            task?.Dispose();

            // The session identity is opaque and is not a credential, so naming it in a log is safe; no
            // descriptor, connection string or credential is reachable from this method at all.
            _logger?.LogWarning(
                "CreateUpdateTask refused a request for session {SessionId} with code {ReturnCode}.",
                LogSafeText.Render(sessionId),
                created);

            return new CreateUpdateTaskResponse
            {
                Status = UpdateWireCodes.Status(
                    created == RetCode.OK ? RetCode.E_INVALID_OBJECT : created),
            };
        }

        // ONE DISPOSAL PATH FOR EVERY ARM THAT DOES NOT PUBLISH, and it replaces three separate explicit
        // drops. Nothing else in the process can reach an unpublished task, so failing to dispose it would
        // pin its pool reference for the life of the process
        // [ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans_pool.sru:L174]. The gate wait below is
        // cancellable, so a caller that goes away while queued is now one of those arms - and it is
        // precisely the arm an explicit per-branch drop would have missed.
        bool published = false;

        try
        {
            UpdateTaskEntry? entry;
            string quotaDiagnostic;

            // ------------------------------------------------------------------------------------------
            //  THE PUBLICATION, AND THE ONLY GATED STEP IN THIS HANDLER. See the remarks above and
            //  IUpdateTaskFactory.EnterPublicationAsync for why the liveness test and the registration have
            //  to be one step rather than two.
            // ------------------------------------------------------------------------------------------
            using (UpdateTaskPublication publication =
                await _factory.EnterPublicationAsync(sessionId, context.CancellationToken)
                    .ConfigureAwait(false))
            {
                if (publication.IsSessionRetiring)
                {
                    _logger?.LogWarning(
                        "CreateUpdateTask did not publish a task on session {SessionId} because the session "
                        + "began retiring first.",
                        LogSafeText.Render(sessionId));

                    return new CreateUpdateTaskResponse
                    {
                        Status = UpdateWireCodes.Status(
                            RetCode.E_INVALID_TRANSACTION,
                            SessionClosingOnCreateDiagnostic),
                    };
                }

                entry = _tasks.Register(sessionId, task, out quotaDiagnostic);
            }

            if (entry is null)
            {
                _logger?.LogWarning(
                    "CreateUpdateTask refused a task on session {SessionId} because a handle ceiling was "
                    + "reached: {Diagnostic}",
                    LogSafeText.Render(sessionId),
                    quotaDiagnostic);

                return new CreateUpdateTaskResponse
                {
                    // E_BUSY - the oracle's own "not now", so no new value enters a consumer's branch set.
                    Status = UpdateWireCodes.Status(RetCode.E_BUSY, quotaDiagnostic),
                };
            }

            published = true;

            _logger?.LogDebug(
                "CreateUpdateTask issued update task {TaskId} on session {SessionId}.",
                LogSafeText.Render(entry.TaskId),
                LogSafeText.Render(entry.SessionId));

            return new CreateUpdateTaskResponse
            {
                Status = UpdateWireCodes.Status(RetCode.OK),
                Task = new TaskHandle { TaskId = entry.TaskId },
            };
        }
        finally
        {
            if (!published)
            {
                task.Dispose();
            }
        }
    }

    /// <summary>
    /// Destroys a server-held update task - the wire form of the legacy <c>Destroy</c>.
    /// </summary>
    /// <param name="request">The task to release.</param>
    /// <param name="context">The call context. Not consulted.</param>
    /// <returns>A completed task carrying the outcome.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// <b>THE HANDLE IS RETIRED BEFORE THE TASK IS DISPOSED, AND THE ORDER IS WHAT MAKES IT SINGLE-USE.</b>
    /// Two callers releasing the same handle race on the removal; exactly one wins and disposes, and the
    /// loser sees the same unknown-handle outcome as any other stale handle. Disposing first and removing
    /// afterwards would let both callers dispose one task.
    /// </para>
    /// <para>
    /// The task's own finalize hook [<c>:L406-L408</c>] is the substrate's business and is sequenced by the
    /// implementation behind the surface, not here - see <see cref="IUpdateTaskSurface"/>.
    /// </para>
    /// </remarks>
    public override Task<ReleaseUpdateTaskResponse> ReleaseUpdateTask(
        ReleaseUpdateTaskRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!_tasks.TryResolve(request.Task, out UpdateTaskEntry? resolved) || resolved is null)
        {
            return Task.FromResult(new ReleaseUpdateTaskResponse
            {
                Status = UpdateWireCodes.Status(RetCode.E_INVALID_HANDLE, UnknownTaskDiagnostic),
            });
        }

        if (!_tasks.TryRemove(resolved.TaskId, out UpdateTaskEntry? removed) || removed is null)
        {
            return Task.FromResult(new ReleaseUpdateTaskResponse
            {
                Status = UpdateWireCodes.Status(RetCode.E_INVALID_HANDLE, UnknownTaskDiagnostic),
            });
        }

        // ⚠ THE TEARDOWN IS NOT UNCONDITIONAL ANY MORE, AND THAT IS THE FIX. Disposing here regardless is
        // exactly the teardown-with-work-still-pending the legacy's own threading notes warn about
        // [docs/PB多线程绕坑提示.md, hazard 1]: an update running through this task would have had its
        // surface disposed underneath it mid-statement. The release now RECORDS itself and disposes only
        // when nothing is in flight; otherwise the duty passes to the operation that is, which performs it
        // as it exits. Exactly one of the two disposes, on every interleaving.
        if (removed.RequestRelease())
        {
            removed.DisposeTask();
        }

        _logger?.LogDebug(
            "ReleaseUpdateTask retired update task {TaskId} on session {SessionId}.",
            LogSafeText.Render(removed.TaskId),
            LogSafeText.Render(removed.SessionId));

        return Task.FromResult(new ReleaseUpdateTaskResponse
        {
            Status = UpdateWireCodes.Status(RetCode.OK),
        });
    }

    /// <summary>
    /// Clears every input on a task - <c>of_reset</c> [<c>:L43</c>, <c>:L58-L72</c>].
    /// </summary>
    /// <param name="request">The task to reset.</param>
    /// <param name="context">The call context. Not consulted.</param>
    /// <returns>A completed task carrying the outcome.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <b>E_BUSY WHILE RUNNING IS THE TASK'S OWN ANSWER AND IS NOT RE-IMPLEMENTED HERE (constraint C-B).</b>
    /// The oracle's guard is the first statement of the function - <c>if #Running then return
    /// RetCode.E_BUSY</c> [<c>:L60</c>] - and its caller-side twin tests <c>of_IsBusy()</c> instead
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlbase.sru:L57</c>]. Both answer the same
    /// code, and both live behind the surface. Adding a gate at this boundary would be a guard the oracle
    /// does not have in this position.
    /// </remarks>
    public override Task<ResetUpdateTaskResponse> Reset(
        ResetUpdateTaskRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!_tasks.TryResolve(request.Task, out UpdateTaskEntry? entry) || entry is null)
        {
            return Task.FromResult(new ResetUpdateTaskResponse
            {
                Status = UpdateWireCodes.Status(RetCode.E_INVALID_HANDLE, UnknownTaskDiagnostic),
            });
        }

        // The lease, not a second copy of the task's own E_BUSY. The task still answers E_BUSY from
        // [:L60] when it is running - that guard is untouched and is still the one a caller who skips this
        // boundary meets - but the lease is what stops this reset overlapping a prepare or an update in the
        // first place, which no guard inside the task can do because both would be inside it.
        if (!TryLease(entry, out OperationStatus refusal))
        {
            return Task.FromResult(new ResetUpdateTaskResponse { Status = refusal });
        }

        try
        {
            long reset = entry.Task.Reset();

            return Task.FromResult(new ResetUpdateTaskResponse
            {
                Status = UpdateWireCodes.Status(reset),
            });
        }
        finally
        {
            ReleaseLease(entry);
        }
    }

    /// <summary>
    /// Records the descriptor array, the multi-table switch and the carrier source on a task -
    /// <c>of_addupdatabletable</c> [<c>:L46</c>, <c>:L82-L96</c>], <c>of_setmultitableupdate</c>
    /// [<c>:L51</c>, <c>:L265-L268</c>], <c>of_setdataobject</c> [<c>:L50</c>, <c>:L259-L263</c>] and
    /// <c>of_setsqlsyntax</c> [<c>:L52</c>, <c>:L270-L274</c>], folded into one round trip.
    /// </summary>
    /// <param name="request">The descriptor array, the switch and at most one of the two sources.</param>
    /// <param name="context">The call context. Not consulted: the handler is synchronous and total.</param>
    /// <returns>A completed task carrying the outcome.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// ============ THE SIX-FIELD DESCRIPTOR, IN THE ORACLE'S OWN DECLARATION ORDER ==================
    /// </para>
    /// <para>
    /// The structure is declared at [<c>:L10-L17</c>] with its fields at [<c>:L11-L16</c>], and the
    /// projection below reads them in exactly that order, field for field, with nothing reordered,
    /// renamed or added:
    /// </para>
    /// <code>
    ///   string   name                [:L11]  ->  Name
    ///   string   updatablecolumns[]  [:L12]  ->  Updatablecolumns
    ///   string   keycolumns[]        [:L13]  ->  Keycolumns
    ///   string   identitycolumn      [:L14]  ->  Identitycolumn
    ///   long     updatewhere         [:L15]  ->  Updatewhere      (OPTIONAL PRESENCE)
    ///   boolean  updatekeyinplace    [:L16]  ->  Updatekeyinplace (OPTIONAL PRESENCE)
    /// </code>
    /// <para>
    /// ⚠️ <b>ABSENCE IS A SUPPORTED, DISTINCT INPUT AND IS NEVER DEFAULTED TO A VALUE.</b> The last two
    /// members are genuinely nullable and the oracle proves it in code rather than by convention: each is
    /// written into the modification string ONLY when it is not null [<c>:L131</c>, <c>:L135</c>], so null
    /// carries the distinct meaning "leave the carrier's own setting alone". The decisive evidence that this
    /// is API surface rather than an uninitialised accident is the FOUR-ARGUMENT overload of
    /// <c>of_addupdatabletable</c>, whose entire body declares two locals, calls <c>SetNull</c> on both and
    /// delegates [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlupdate.sru:L227-L234</c>].
    /// The dispatch below therefore reads PRESENCE and routes to the matching arity: neither present takes
    /// the four-argument member, and otherwise the six-argument member receives per-field nullables. An
    /// implicit-default reading would be actively dangerous here - the mode is an <c>int64</c> and 0 is a
    /// LEGAL update-where mode, so a caller who omitted the field would silently force a concurrency mode
    /// it never asked for, which is the worst possible failure on this particular contract.
    /// </para>
    /// <para>
    /// ============ MULTI-TABLE UPDATE IS REAL LEGACY CAPABILITY, NOT A THEORETICAL ONE ==============
    /// </para>
    /// <para>
    /// Three independent proofs: the oracle holds <c>TABLEDATA Tables[]</c> as an ARRAY [<c>:L30</c>],
    /// there is an explicit switch with its own setter [<c>:L51</c>, <c>:L265-L268</c>], and the prepare
    /// function takes a ONE-BASED index [<c>:L47</c>, <c>:L98</c>] driven by
    /// <c>for nIndex = 1 to nCount</c> [<c>:L364</c>]. So the contract carries the repeated descriptor, the
    /// flag and the index, and the loop below preserves ARRAY ORDER because the worker prepares and updates
    /// per table in that order and STOPS AT THE FIRST FAILURE [<c>:L366</c>, <c>:L368</c>].
    /// </para>
    /// <para>
    /// 🔴 <b>THIS CALL RECORDS THE DESCRIPTORS; IT DOES NOT FORCE A PREPARE, BECAUSE THE ORACLE HAS NONE ON
    /// THE SINGLE-TABLE PATH.</b> <c>_of_UpdatePrepare</c> has exactly ONE caller, at [<c>:L365</c>], inside
    /// the multi-table branch. The single-table branch calls the update DIRECTLY [<c>:L371</c>] and never
    /// prepares at all, so with the switch off the descriptor array is recorded and NOT APPLIED, and the
    /// carrier's own static definition governs update, key, identity, update-where and key-in-place. That
    /// looks like a legacy oversight; it is the observable behaviour, so it is preserved and documented
    /// rather than "fixed" into applying the descriptors in both modes.
    /// </para>
    /// <para>
    /// <b>THE ARRAY IS REPLACED WHOLESALE, MATCHING THE ORACLE'S RESET.</b> <c>Tables = emptyTables</c>
    /// assigns a freshly declared empty array [<c>:L67</c>], so this call clears before it appends. Only the
    /// descriptor half of <c>of_reset</c> runs - the payload, the autocommit flag and the source
    /// [<c>:L62</c>, <c>:L64-L65</c>, <c>:L68-L69</c>] are untouched, because a prepare that discarded the
    /// payload would make the documented call order unusable.
    /// </para>
    /// <para>
    /// <b>THE THREE-ARM ADMISSION TEST IS DELEGATED, NOT RESTATED (constraint C-A, C-H).</b> Empty name OR
    /// empty updatable columns OR empty key columns yields <c>RetCode.E_INVALID_ARGUMENT</c> [<c>:L84</c>],
    /// and <b>AN EMPTY IDENTITY COLUMN IS LEGAL</b> - it is not one of the arms and the oracle simply omits
    /// its line [<c>:L127-L129</c>]. <c>Concurrency/UpdateWhereBuilder.cs</c> already reproduces all three
    /// arms exactly, so this boundary calls it and reports what it answers. Re-checking here would create a
    /// second copy of the test and a way for the two to disagree.
    /// </para>
    /// <para>
    /// <b>INDICES ARE ONE-BASED IN EVERY DIAGNOSTIC AND ARE NEVER SILENTLY REBASED.</b> The enumeration
    /// below is ordinary zero-based C#, but the ordinal NAMED IN A DIAGNOSTIC counts from one so a reader
    /// can line it up against the legacy array without a mental rebase - PowerBuilder arrays are one-based
    /// and its upper-bound function answers the LAST VALID INDEX rather than one past the end. This is the
    /// migration plan's named R9 hazard, and the one-based first index is taken from
    /// <c>OneBasedIndex.FirstIndex</c> rather than written as a literal so the convention has one
    /// definition.
    /// </para>
    /// <para>
    /// <b>WHAT <c>updatewhere = 1</c> REQUIRES OF THE PAYLOAD, RECORDED HERE SO IT IS NOT LOST IN
    /// TRANSLATION.</b> Mode 1 is the "key and updateable columns" concurrency mode, so the generated WHERE
    /// clause carries the key column PLUS THE ORIGINAL VALUE OF EVERY UPDATEABLE COLUMN. On the sole
    /// evidenced fixture all six columns are marked <c>update=yes updatewhereclause=yes</c>
    /// [<c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L8-L13</c>] against
    /// <c>updatewhere=1 updatekeyinplace=no</c> [<c>:L14</c> of that file], so the check spans all six
    /// columns' ORIGINAL values and the payload must be able to express, per row, BOTH the current and the
    /// original value of every marked column. That obligation is discharged by the changeset carried on
    /// <see cref="Update"/> and owned by <c>Concurrency/UpdateWhereBuilder.cs</c> and <c>Buffers/</c>; this
    /// method's share of it is to transmit the mode faithfully, including its absence.
    /// </para>
    /// <para>
    /// <b>THE TWO SOURCES ARE MUTUALLY EXCLUSIVE AND EACH SETTER CLEARS THE OTHER</b> [<c>:L260</c>,
    /// <c>:L271</c>]. They are applied source-object first and syntax second, so that a request carrying
    /// both leaves the SYNTAX in place - which is the preference the oracle's own shaping branch expresses,
    /// testing the syntax first and only then the source object [<c>:L316-L331</c>]. Applying them the other
    /// way round would leave the source object standing and silently take the other arm.
    /// </para>
    /// </remarks>
    public override Task<PrepareUpdateResponse> PrepareUpdate(
        PrepareUpdateRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!_tasks.TryResolve(request.Task, out UpdateTaskEntry? entry) || entry is null)
        {
            return Task.FromResult(new PrepareUpdateResponse
            {
                Status = UpdateWireCodes.Status(RetCode.E_INVALID_HANDLE, UnknownTaskDiagnostic),
            });
        }

        // THE OPERATION LEASE, TAKEN BEFORE ANYTHING ABOUT THIS TASK IS DECIDED. The oracle puts its
        // caller-side busy guard first in every mutator [n_cst_threading_task_sqlbase.sru:L57, :L73,
        // :L95], so E_BUSY precedes even a malformed-request refusal; that ordering is preserved here.
        if (!TryLease(entry, out OperationStatus refusal))
        {
            return Task.FromResult(new PrepareUpdateResponse { Status = refusal });
        }

        try
        {
            // WITH THE SWITCH ON, AN EMPTY ARRAY IS AN ERROR [:L359-L363] - E_INVALID_ARGUMENT with the
            // oracle's own diagnostic, consumed from Concurrency/UpdateWhereBuilder.cs rather than retyped
            // here: a single transposed character in a CJK literal is invisible in review and fails every
            // parity comparison. WITH THE SWITCH OFF AN EMPTY ARRAY IS ORDINARY, because the descriptors are
            // then never applied [:L365 versus :L371].
            //
            // NOTE the oracle's own arm sits at RUN time rather than at prepare time, so a caller that skips
            // this call entirely still meets the same refusal from the task. Reproducing it here as well is
            // the contract's own reading and mirrors the legacy's habit of guarding one condition on both
            // sides - the command task checks its empty statement in the setter AND again in the worker.
            if (request.MultiTableUpdate && request.Tables.Count == 0)
            {
                return Task.FromResult(new PrepareUpdateResponse
                {
                    Status = UpdateWireCodes.Status(
                        RetCode.E_INVALID_ARGUMENT,
                        UpdateWhereBuilder.NoUpdatableTableMessage),
                });
            }

            // ==========================================================================================
            //  🔴 THE MIRROR OF THE ARM ABOVE, AND THE ONE THAT CLOSES A SILENT-MISDIRECTION HAZARD.
            //
            //  WITH THE SWITCH OFF THE DESCRIPTOR ARRAY IS NEVER APPLIED. That is the oracle's own shape,
            //  not a shortfall: _of_UpdatePrepare has exactly ONE caller, at
            //  [ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru:L365], inside the
            //  multi-table branch, and the single-table branch calls the update directly [:L371] and lets
            //  the carrier's own compiled definition govern the update table, the key columns and the
            //  identity column. The behaviour is preserved and is documented on the contract field
            //  [persistence.v1.proto, multi_table_update].
            //
            //  WHAT DOES NOT SURVIVE THE MOVE TO A NETWORK BOUNDARY IS ACCEPTING SUCH A REQUEST SILENTLY.
            //  In process the caller could see the datastore it had loaded and therefore knew which table
            //  the update would reach. A remote caller cannot: it sends a descriptor naming table X,
            //  receives RetCode.OK, and the subsequent Update writes to whatever table the data object
            //  declares - reporting success. That is a write the caller believes went somewhere else, with
            //  nothing in the response to reveal it. And it is unconditionally dead weight rather than a
            //  timing question: THIS RPC IS THE ONLY PLACE THE SWITCH CAN BE SET, and it REPLACES the array
            //  wholesale [:L67], so a descriptor sent with the switch off can never be applied by any later
            //  call either.
            //
            //  SO THE MISDIRECTION IS REFUSED AND NOTHING ELSE IS. The refusal is scoped to the descriptor
            //  that NAMES A DIFFERENT TABLE from the one the request's own data object declares, because
            //  that - and only that - is the case where the write lands somewhere the caller did not ask
            //  for. A descriptor that AGREES with the definition is admitted exactly as before: it is still
            //  inert, but inert and agreeing is not a misdirection, and it is the shape a caller that
            //  derives its descriptor FROM the definition necessarily sends. That shape is not
            //  hypothetical - it is what this system's own DataServices consumer sends on every update, and
            //  refusing it would break the only cross-service update path in the estate while protecting
            //  nobody.
            //
            //  WHAT IS DELIBERATELY NOT REFUSED, so the narrowing stays the minimum that closes the hazard:
            //    * a descriptor whose COLUMN sets differ from the definition's. The write still lands in the
            //      table the caller named; only which columns are updatable, keyed or identity differs, and
            //      the definition's own answer there is the legacy's. On the multi-table path those names
            //      ARE resolved and an unknown one is refused with 无效的列名: by the preparer itself.
            //    * a data object that resolves to no definition, or to a retrieve-only one. Neither can
            //      misdirect a write: the update fails on its own for want of an update table.
            //    * a request whose source is a SQL SYNTAX rather than a data object. Its update table comes
            //      from the syntax, which this boundary does not parse, so there is nothing to compare
            //      against and a refusal would be a guess.
            //
            //  E_INVALID_ARGUMENT is the code the sibling arm above already uses for a descriptor array that
            //  cannot be honoured, and the diagnostic quotes NEITHER table name (constraint C-F): a caller
            //  knows both already, and a log record must not carry either.
            // ==========================================================================================
            if (!request.MultiTableUpdate
                && request.Tables.Count > 0
                && TryRefuseInertDescriptor(request, entry, out OperationStatus descriptorRefusal))
            {
                return Task.FromResult(new PrepareUpdateResponse { Status = descriptorRefusal });
            }

            // ==========================================================================================
            //  🔴 A PREPARE THAT WOULD LEAVE THE TASK WITH NO SOURCE IS A SUCCESS THE CALLER CANNOT ACT ON.
            //
            //  The two sources are mutually exclusive and each setter clears the other [:L260, :L271], and
            //  THIS RPC IS THE ONLY PLACE EITHER CAN BE SET - Update carries the payload and nothing else,
            //  and Reset only clears. So a task holding neither has exactly one reachable future: its Update
            //  takes the oracle's own no-source arm and answers E_INVALID_DATAOBJECT with 无效的数据源对象!
            //  [:L326-L330]. Answering OK here and that code one call later tells the caller its
            //  configuration was accepted when nothing about it can ever succeed - which is precisely the
            //  "success it cannot act on" this arm removes.
            //
            //  THE CODE THE RUN WOULD ANSWER IS ANSWERED HERE, VERBATIM - the same constant and the same
            //  diagnostic, consumed from the task type that owns them rather than retyped, because a
            //  transposed character in a CJK literal is invisible in review and fails every parity
            //  comparison. This is the legacy's own habit of guarding one condition on both sides, which the
            //  two arms above already follow, and the run-time arm is untouched: a caller that skips this
            //  call entirely still meets it there.
            //
            //  AHEAD OF THE CLEAR, SO THE REFUSAL CHANGES NOTHING - the same atomicity the two arms above
            //  have. A refusal that had already replaced the descriptor array would leave the task in a state
            //  the caller did not ask for and cannot see.
            //
            //  WHAT IS NOT REFUSED, AND WHY NO WORKING SEQUENCE CAN BREAK. The test is on the state this
            //  request would LEAVE, not on the request's fields alone: a prepare that names no source is
            //  admitted whenever the task already holds one, because both setters are presence-gated and an
            //  unstated source survives. That covers the descriptors-only prepare a multi-table caller sends
            //  after naming its source - the only incremental order that can work anyway, since this call
            //  REPLACES the descriptor array wholesale [:L67] and so a source-only prepare sent afterwards
            //  would discard the descriptors.
            // ==========================================================================================
            if (!request.HasDataObject && !request.HasSqlSyntax && !entry.Task.HasUpdateSource)
            {
                _logger?.LogWarning(
                    "PrepareUpdate refused on task {TaskId}: the request names neither a data object nor a "
                    + "SQL syntax and the task holds neither, so no update could ever run against it. "
                    + "Nothing was cleared and no descriptor was recorded.",
                    LogSafeText.Render(entry.TaskId));

                return Task.FromResult(new PrepareUpdateResponse
                {
                    Status = UpdateWireCodes.Status(
                        RetCode.E_INVALID_DATAOBJECT,
                        // Qualified rather than imported: this file deliberately holds no using for the
                        // task namespace, so that nothing in Grpc/ reaches a worker type by accident.
                        Tasks.SqlUpdateTask.InvalidDataObjectMessage),
                });
            }

            // `Tables = emptyTables` [:L67] - the descriptor array is REPLACED, not appended to. Answers
            // E_BUSY while the task is running, in which case nothing has been cleared.
            long cleared = entry.Task.ResetUpdatableTables();

            if (cleared != RetCode.OK)
            {
                return Task.FromResult(new PrepareUpdateResponse
                {
                    Status = UpdateWireCodes.Status(cleared),
                });
            }

            // [:L265-L268] set AFTER the clear, because the clear turns it off [:L63].
            long switched = entry.Task.SetMultiTableUpdate(request.MultiTableUpdate);

            if (switched != RetCode.OK)
            {
                return Task.FromResult(new PrepareUpdateResponse
                {
                    Status = UpdateWireCodes.Status(switched),
                });
            }

            for (int index = 0; index < request.Tables.Count; index++)
            {
                TableUpdateContract table = request.Tables[index];

                // R9: reporting ordinal only. Nothing crossing the wire is rebased, and the append itself is
                // the collection's own one-based `UpperBound + 1` [:L86].
                int ordinal = index + OneBasedIndex.FirstIndex;

                // THE PRESENCE DISPATCH. Neither setting present takes the four-argument member - the oracle's
                // SetNull form - and otherwise the six-argument member receives each setting as present-or-null
                // independently, because the oracle tests the two nulls SEPARATELY [:L131, :L135] and a caller
                // may legitimately state one and omit the other.
                long added = table.HasUpdatewhere || table.HasUpdatekeyinplace
                    ? entry.Task.AddUpdatableTable(
                        table.Name,
                        table.Updatablecolumns,
                        table.Keycolumns,
                        table.Identitycolumn,
                        table.HasUpdatewhere ? table.Updatewhere : null,
                        table.HasUpdatekeyinplace ? table.Updatekeyinplace : null)
                    : entry.Task.AddUpdatableTable(
                        table.Name,
                        table.Updatablecolumns,
                        table.Keycolumns,
                        table.Identitycolumn);

                if (added != RetCode.OK)
                {
                    // STOPS AT THE FIRST REFUSAL, leaving the descriptors accepted so far in place - which is
                    // the oracle's own shape: its add function returns before appending [:L84] and its
                    // multi-table loop exits on the first non-OK code [:L366, :L368].
                    //
                    // The diagnostic names the ORDINAL and nothing else. The oracle carries NO message on this
                    // arm at all, so the text is a boundary diagnostic rather than a ported one; it quotes no
                    // table name, no column name and no value, so nothing sensitive can reach a log or a
                    // response through it (constraint C-F).
                    _logger?.LogWarning(
                        "PrepareUpdate refused update table {Ordinal} of {TableCount} on task {TaskId} with "
                        + "code {ReturnCode}.",
                        ordinal,
                        request.Tables.Count,
                        LogSafeText.Render(entry.TaskId),
                        added);

                    return Task.FromResult(new PrepareUpdateResponse
                    {
                        Status = UpdateWireCodes.Status(
                            added,
                            string.Create(
                                CultureInfo.InvariantCulture,
                                $"Update table {ordinal} was refused. A descriptor must declare a name, at "
                                + $"least one updatable column and at least one key column "
                                + $"[ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru:L84]; "
                                + $"the identity column may legitimately be empty, and the update-where and "
                                + $"key-in-place settings are deliberately unchecked.")),
                    });
                }
            }

            // The source object first, then the syntax - see this member's remarks for why the order is what it
            // is. Each is presence-gated, so a request that names neither leaves whatever the task already
            // holds, and a task that ends up holding neither meets the oracle's own E_INVALID_DATAOBJECT arm
            // when it runs [:L326-L330].
            if (request.HasDataObject)
            {
                long source = entry.Task.SetDataObject(request.DataObject);

                if (source != RetCode.OK)
                {
                    return Task.FromResult(new PrepareUpdateResponse
                    {
                        Status = UpdateWireCodes.Status(source),
                    });
                }
            }

            if (request.HasSqlSyntax)
            {
                long syntax = entry.Task.SetSqlSyntax(request.SqlSyntax);

                if (syntax != RetCode.OK)
                {
                    return Task.FromResult(new PrepareUpdateResponse
                    {
                        Status = UpdateWireCodes.Status(syntax),
                    });
                }
            }

            _logger?.LogDebug(
                "PrepareUpdate recorded {TableCount} descriptor(s) on task {TaskId}; multi-table update is "
                + "{MultiTableUpdate}.",
                request.Tables.Count,
                LogSafeText.Render(entry.TaskId),
                request.MultiTableUpdate);

            return Task.FromResult(new PrepareUpdateResponse
            {
                Status = UpdateWireCodes.Status(RetCode.OK),
            });
        }
        finally
        {
            ReleaseLease(entry);
        }
    }

    /// <summary>
    /// Applies a changeset - the <c>ondotask</c> event [<c>:L284-L404</c>] - and answers the reconciled
    /// outcome, the accumulated counts and the identity round trip. <b>THIS IS THE
    /// <see cref="RpcException"/> THROW SITE FOR THE ABORTED STATUS.</b>
    /// </summary>
    /// <param name="request">The task, the changeset, its row count and optionally the autocommit setting.</param>
    /// <param name="context">
    /// The call context. <b>CONSULTED, unlike the other four handlers</b>: its cancellation token is the
    /// wire form of the caller-side cancellation the oracle polls through <c>of_IsCancelled()</c>.
    /// </param>
    /// <returns>A completed task carrying the outcome, and - when the outcome is OK - the counts and the
    /// identity blocks.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="request"/> or <paramref name="context"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="RpcException">
    /// The update was refused by the optimistic-concurrency check. The status is
    /// <see cref="StatusCode.Aborted"/> and the trailers carry <c>common.v1.ConflictDetail</c> - see the
    /// remarks.
    /// </exception>
    /// <remarks>
    /// <para>
    /// ============ ON CONCURRENCY MISMATCH: Aborted, AND NO SILENT OVERWRITE ANYWHERE ================
    /// </para>
    /// <para>
    /// A mismatch is raised as the gRPC status <c>Aborted</c> - canonical numeric code 10 - carrying
    /// <c>common.v1.ConflictDetail</c> in the versioned BINARY TRAILER the Update method's own descriptor
    /// names, and Gateway's REST projection surfaces the same payload as <b>HTTP 409</b>. Callers implement
    /// an explicit retry-or-surface policy; nothing in this method re-attempts the update, downgrades the
    /// status, or rewrites a refusal into a success.
    /// </para>
    /// <para>
    /// <b>THE PROJECTION IS TAKEN WHOLE FROM THE ONE SANCTIONED HELPER AND IS NOT RE-IMPLEMENTED HERE.</b>
    /// <c>ConflictDetector.TryProjectAborted</c> answers the status and the trailers and deliberately raises
    /// nothing itself, so that this throw site and any central mapper agree on ONE spelling instead of each
    /// inventing its own - and so that the classifier stays callable from the multi-table loop, which an
    /// in-flight exception would abandon. No second conflict payload is built anywhere in this file, and
    /// <c>UpdateResponse</c> has no conflict member to put one in: two ways to report one condition would let
    /// a consumer handle one and miss the other.
    /// </para>
    /// <para>
    /// <b>A CONFLICT WITHOUT A DETAIL IS NOT RAISED AS ABORTED.</b> The outcome's factory rejects a null
    /// detail, so the case cannot arise through the sanctioned path; if it ever did, the helper answers
    /// false, the flow falls through to <see cref="ProjectStatus"/>, and the narrowing's own code -
    /// <c>RetCode.E_DB_ERROR</c>, unchanged by the narrowing - is reported instead. That is deliberate: the
    /// contract states an Aborted response NEVER carries an empty row list, because an Aborted with nothing
    /// in it tells the caller nothing it can act on.
    /// </para>
    /// <para>
    /// ============ FOUR BEHAVIOURS A STRAIGHTFORWARD PORT GETS BACKWARDS (constraint C-B) ============
    /// </para>
    /// <para>
    /// All four are reproduced by the collaborators and are recorded here because this is where their
    /// consequences reach the wire, and because a reader of the response shape needs to know why the outcome
    /// cannot be reconstructed from any single field:
    /// </para>
    /// <list type="number">
    /// <item><description>
    /// <b>SUCCESS IS THE VALUE 1, NOT THE ZERO OF THE RETURN-CODE ALGEBRA.</b> The gate on the entire
    /// success path is <c>if rtCode = 1 then</c> [<c>:L214</c>], with anything else falling through to
    /// <c>RetCode.E_DB_ERROR</c> [<c>:L249-L250</c>]. The update itself is invoked with ACCEPT-TEXT TRUE AND
    /// RESET-FLAG FALSE - <c>Data.Update(true,false)</c> [<c>:L204</c>] - so <b>THE CALLER OWNS THE
    /// CARRIER'S STATE AFTERWARDS</b> and this response must not be read as implying the server reset it.
    /// Item statuses and the original-value shadow survive the call, which is what makes a retry, an
    /// identity round trip and a conflict report possible at all.
    /// </description></item>
    /// <item><description>
    /// <b>A CLAIMED SUCCESS IS DEFENSIVELY REWRITTEN INTO A FAILURE</b> -
    /// <c>if TransObject.SQLCode = -1 and rtCode = 1 then rtCode = -1</c> [<c>:L208-L210</c>]. An
    /// implementation that trusted the update call's own return value would report success on a failed
    /// update. The test is against SQL code EXACTLY <c>-1</c>, and it is deliberately distinct from the
    /// transaction object's own NON-ZERO test and from its NEGATIVE failure predicate
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:L337</c>] - three different comparisons
    /// a careless port would merge, which is why the classifier keeps them apart and this boundary reads only
    /// the reconciled result.
    /// </description></item>
    /// <item><description>
    /// <b>THE AFTER-UPDATE HOOK FIRES WITH THE UNRECONCILED VALUE, BEFORE THE REWRITE.</b>
    /// <c>TransObject.Event OnAfterUpdate(Data,rtCode)</c> sits at [<c>:L206</c>] and the rewrite at
    /// [<c>:L208-L210</c>], so a hook observes <c>1</c> on a run this response reports as a failure. The
    /// ordering is contract; the classification carries both values so neither is lost.
    /// </description></item>
    /// <item><description>
    /// <b>A VETO MEANS TWO DIFFERENT THINGS</b> [<c>:L195-L202</c>], discriminated by the transaction's OWN
    /// failure predicate: a veto combined with a transaction failure yields a DATABASE ERROR while a CLEAN
    /// VETO yields CANCELLED. The two arrive on two different arms of <see cref="ProjectStatus"/> and are
    /// never collapsed, because a consumer must be able to tell a deliberate cancellation from a failure -
    /// and CANCELLED is neither succeeded nor failed in the legacy algebra, so it cannot be folded into
    /// either.
    /// </description></item>
    /// </list>
    /// <para>
    /// <b>THE THREE UPDATE-TABLE SENTINELS.</b> An empty, <c>"!"</c> or <c>"?"</c> update table fires BOTH
    /// error channels and then returns the database-error code [<c>:L188-L193</c>]: the synthesized payload
    /// is <c>OnDBError(-1, "没有可更新的表", "", Primary!, 0)</c> - code <c>-1</c>, the no-updatable-table
    /// diagnostic, an EMPTY statement, the <c>Primary!</c> buffer and row <c>0</c> meaning "no particular
    /// row" - alongside <c>OnError(RetCode.E_DB_ERROR, "没有可更新的表")</c> with the same text.
    /// <c>Concurrency/ConflictDetector.cs</c> owns that arm and builds the payload through
    /// <c>DbErrorData.NoUpdatableTable()</c>, so this file surfaces the result faithfully and retypes
    /// neither the code nor the literal.
    /// </para>
    /// <para>
    /// ============ THE COUNTS AND THE IDENTITY ROUND TRIP ============================================
    /// </para>
    /// <para>
    /// <b>THE COUNTS ARE ADDITIVE AND THE IDENTITY BLOCKS ARE ONE PER TABLE.</b> The worker fires
    /// <c>OnUpdated(inserted, updated, deleted)</c> [<c>:L247</c>] and
    /// <c>OnIdentityColumnDataRetrieved(id, ref primaryValues, ref filterValues)</c> [<c>:L243</c>] once per
    /// table, and the caller-side proxy ADDS to running totals and APPENDS one record per firing
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlupdate.sru:L66-L68</c>,
    /// <c>:L73-L76</c>]. A single-table response is therefore the degenerate case of a repeated one.
    /// </para>
    /// <para>
    /// <b>BOTH GATES ON THE IDENTITY BLOCK ARE PRESERVED, AND AN EMPTY IDENTITY PAYLOAD IS A VALID
    /// SUCCESS.</b> The block runs only when the inserted count is greater than zero [<c>:L215</c>], and the
    /// callback fires only when at least one of the two arrays is non-empty [<c>:L242-L244</c>] - with a
    /// third gate in between requiring that an identity column was actually discovered [<c>:L226</c>]. An
    /// empty list means "none collected" and is never an error.
    /// </para>
    /// <para>
    /// ⚠️ <b>THE ARRAYS ARE SERIALIZED IN COLLECTED ORDER AND ARE NEVER SORTED, MERGED OR DEDUPLICATED.</b>
    /// The resolver walks the <c>Primary!</c> buffer FORWARD [<c>:L228-L233</c>] and the <c>Filter!</c>
    /// buffer BACKWARD [<c>:L237</c>], because the oracle's own comment immediately above the reverse loop
    /// records that the filter buffer's row order is INVERTED relative to the data source [<c>:L235</c>].
    /// The migration plan calls this the single most dangerous line in the refactor for one-based-to-
    /// zero-based translation: it looks like a bug, it is not a bug, and "correcting" the direction produces
    /// wrong identity values that a row-count assertion would still pass. The loop below therefore projects
    /// each block through the resolver's own single mapper and appends in sequence - it performs no ordering
    /// decision of its own, at either level.
    /// </para>
    /// <para>
    /// <b>THE IDENTITY COLUMN ON THE RESPONSE IS THE DISCOVERED ONE, NOT THE REQUESTED ONE.</b> It is found
    /// at RUN time by prefix-matching the lower-cased update table name against each column's database name,
    /// with a FIRST-WINS fallback [<c>:L217-L225</c>], so it can legitimately differ from the identity column
    /// named in a descriptor and a consumer must READ it rather than assume it.
    /// </para>
    /// <para>
    /// <b>COUNTS AND IDENTITY ARE POPULATED ONLY WHEN THE OUTCOME IS OK</b>, which is the contract's own
    /// rule, and the test is on the RUN's reconciled code rather than on the classification - so a run whose
    /// update succeeded but whose autocommit then failed [<c>:L386-L392</c>], or which was cancelled after
    /// the teardown [<c>:L381-L383</c>], reports the failure with no counts rather than counts a caller
    /// could mistake for committed work.
    /// </para>
    /// <para>
    /// <b>THE TWO MUTATORS ARE E_BUSY-GUARDED AND ARE APPLIED IN A FIXED ORDER.</b> Autocommit first, then
    /// the payload, each reported immediately if refused. The autocommit setting is presence-gated because
    /// <see langword="false"/> is both a legal value AND the legacy default [<c>:L32</c>, restored by the
    /// reset at <c>:L62</c>], so presence is the only way for a caller to say "leave it alone" - and it is a
    /// BOOLEAN here, deliberately unlike C-07's three-valued enum.
    /// </para>
    /// </remarks>
    public override Task<UpdateResponse> Update(UpdateRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        if (!_tasks.TryResolve(request.Task, out UpdateTaskEntry? entry) || entry is null)
        {
            return Task.FromResult(new UpdateResponse
            {
                Status = UpdateWireCodes.Status(RetCode.E_INVALID_HANDLE, UnknownTaskDiagnostic),
            });
        }

        // THE OPERATION LEASE, TAKEN BEFORE ANYTHING ABOUT THIS TASK IS DECIDED. The oracle puts its
        // caller-side busy guard first in every mutator [n_cst_threading_task_sqlbase.sru:L57, :L73,
        // :L95], so E_BUSY precedes even a malformed-request refusal; that ordering is preserved here.
        if (!TryLease(entry, out OperationStatus refusal))
        {
            return Task.FromResult(new UpdateResponse { Status = refusal });
        }

        try
        {
            // [:L49, :L254-L257] `of_setautocommit(readonly boolean autocommit)`. PRESENCE-GATED: unset leaves
            // the task's own setting alone, because false is a legal value and also the default [:L32].
            if (request.HasAutocommit)
            {
                long autoCommit = entry.Task.SetAutoCommit(request.Autocommit);

                if (autoCommit != RetCode.OK)
                {
                    return Task.FromResult(new UpdateResponse
                    {
                        Status = UpdateWireCodes.Status(autoCommit),
                    });
                }
            }

            // [:L44, :L74-L77] `of_setupdatedata(ref blob blbdata, readonly long rows)`. The row count travels
            // with the payload because it is load-bearing: a rejected changeset with a ZERO count is the
            // oracle's SUCCESS arm [:L339-L342] while a non-zero count is E_INVALID_DATA [:L343-L345]. An unset
            // payload is the oracle's zero-length blob and is passed through as null rather than substituted.
            long payload = entry.Task.SetUpdateData(request.UpdateData, request.UpdateRows);

            if (payload != RetCode.OK)
            {
                return Task.FromResult(new UpdateResponse
                {
                    Status = UpdateWireCodes.Status(payload),
                });
            }

            UpdateRunResult result = entry.Task.Execute(context.CancellationToken);

            // ============ THE THROW SITE. THE ONLY ONE IN THIS FILE, AND THE ONLY RAISED STATUS ===========
            // The projection is consumed whole - status and trailers - and raised exactly as its own
            // documentation prescribes. Nothing here builds a status, chooses a code, or assembles a payload.
            if (result.Outcome is { Kind: UpdateOutcomeKind.Conflict } conflict
                && ConflictDetector.TryProjectAborted(conflict, out RichErrorProjection projection))
            {
                // COUNTS AND ROW ORDINALS ONLY. No column value, no statement text and no original-value
                // shadow is logged, because the conflict payload carries live row data by design and this log
                // record must not become a second copy of it (constraint C-F).
                _logger?.LogWarning(
                    "Optimistic-concurrency conflict on task {TaskId}: {ConflictRowCount} row(s) on update "
                    + "table {UpdateTable}; {RowsExpected} row(s) expected, {RowsMatched} matched. Reported as "
                    + "Aborted with its detail so the caller can re-read and rebase or surface it; it is "
                    + "neither retried nor overwritten.",
                    LogSafeText.Render(entry.TaskId),
                    conflict.Conflict?.Rows.Count ?? 0,
                    conflict.Conflict?.UpdateTable ?? string.Empty,
                    conflict.Conflict?.RowsExpected ?? 0L,
                    conflict.Conflict?.RowsMatched ?? 0L);

                throw new RpcException(projection.Status, projection.Trailers);
            }

            UpdateResponse response = new()
            {
                Status = ProjectStatus(result),
            };

            // The contract's own rule: the counts are present when ret_code is OK. Tested on the RUN's
            // reconciled code - see this member's remarks for the two epilogue arms that make that matter.
            if (result.Code == RetCode.OK)
            {
                // [:L247] fired on EVERY success, including a pure update and a pure delete, because the
                // oracle's call sits OUTSIDE the inserted-count block that closes at [:L246]. So a zero triple
                // is a real answer here rather than a missing one.
                response.Counts = new UpdateCounts
                {
                    Inserted = result.Counts.Inserted,
                    Updated = result.Counts.Updated,
                    Deleted = result.Counts.Deleted,
                };

                // ⚠️ ORDER-PRESERVING BY CONSTRUCTION, AT BOTH LEVELS. The blocks are appended in the order
                // they were accumulated - which is table order - and each block's two arrays are carried by the
                // resolver's own single mapper, which preserves every position and every null and never merges
                // the two. Nothing here sorts, reverses, deduplicates or concatenates anything.
                foreach (ResolvedIdentityColumnData block in result.Identity)
                {
                    response.Identity.Add(block.ToIdentityColumnData());
                }
            }

            _logger?.LogDebug(
                "Update on task {TaskId} answered {ReturnCode}; {IdentityBlockCount} identity block(s), "
                + "{Inserted} inserted, {Updated} updated, {Deleted} deleted.",
                LogSafeText.Render(entry.TaskId),
                result.Code,
                response.Identity.Count,
                response.Counts?.Inserted ?? 0L,
                response.Counts?.Updated ?? 0L,
                response.Counts?.Deleted ?? 0L);

            return Task.FromResult(response);
        }
        finally
        {
            ReleaseLease(entry);
        }
    }
}
