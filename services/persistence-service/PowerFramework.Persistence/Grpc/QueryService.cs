// ==================================================================================================
//  Grpc/QueryService.cs - CONTRACT C-05, persistence.v1.QueryService
//  ------------------------------------------------------------------------------------------------
//  ROLE: A THIN ADAPTER, AND NOTHING MORE.
//  Validate, translate, delegate to Tasks/, map the result. This is the largest method surface of the
//  four contracts this service hosts - eleven RPCs - and yet every behaviour it appears to have
//  actually lives in a sibling folder:
//
//      the eighteen setters and their guard boundaries  ->  Tasks/SqlQueryTask.cs
//      the retrieval itself, chunking and counting      ->  Tasks/SqlQueryTask.cs (ExecuteAsync)
//      the one-based clause upsert and its guard        ->  Sql/ClauseModifier.cs
//      the structural-SQL refusal on a clause body      ->  Sql/ClauseModifier.cs (ValidateClauseBody)
//      the paged-identifier validation                  ->  Sql/Paging/IPagingRewriter.cs
//                                                           (PagedUniqueIndexColumnValidator)
//      the chunk arithmetic and its three defects       ->  Buffers/ChangesetCodec.cs
//      the single full-state image                      ->  Buffers/FullStateCodec.cs
//      the typed carrier payload                        ->  Buffers/DataWindowBuffers.cs
//      the driver-error payload and its redaction       ->  Errors/DbErrorData.cs, Errors/SqlRedactor.cs
//      the session, its reference count and its clock   ->  Transactions/TransactionPool.cs
//
//  What is genuinely THIS file's own is the boundary itself: the task-handle correlation the legacy
//  had no need for, the projection of the legacy's four result callbacks onto the five arms of one
//  server stream, and the refusal of the two things a newly created wire boundary must refuse that
//  the legacy never could - an out-of-domain clause modification style, and a handle that names
//  nothing.
//
//  WHY THIS SERVICE IS gRPC (constraint C-K)
//  The legacy surface being ported is ACTION-ORIENTED AND THEREFORE RPC-SHAPED, NOT
//  RESOURCE-ORIENTED. Read the oracle's own public prototypes and there is no collection to GET and
//  no entity to PUT - of_setsql, of_setwhereclause, of_setchunksize, of_setpaged, of_reset are verbs
//  on a stateful task object [ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L36-L70].
//  Three further properties push the same way, and the third is decisive for THIS contract:
//    * the database-error structure needs a STRUCTURED payload rather than a message string
//      [ws_objects/pfw.thread.ext.pbl.src/dberrordata.srs:L4-L8] - five fields, one of them a buffer
//      discriminator and one a row ordinal;
//    * the thirteen SQL task classes carry mandatory THREAD AFFINITY in their own source comments -
//      this contract's worker declares [运行在子线程] and its caller-side twin declares
//      [运行在当前线程] [n_cst_thread_task_sqlquery.sru:L2, n_cst_threading_task_sqlquery.sru:L2];
//    * SERVER STREAMING REPRODUCES THE PROGRESSIVE RESULT DELIVERY OF THE LEGACY RECORDSET. The
//      SQLite binding exposes a FORWARD-ONLY CURSOR and nothing else - IsValid(), IsEmpty(),
//      HasData(), NextRow() [ws_objects/pfw.utility.sqlite.pbl.src/n_sqliterecordset.sru:L11-L14] -
//      so a single materialised rowset would be the unfaithful shape, not merely a slower one.
//  Protocol buffers over gRPC carry all of it with compile-time contract enforcement; JSON over REST
//  would carry none of it well.
//
//  WHAT THIS FILE DELIBERATELY DOES NOT DO
//    * It declares NO route, NO health endpoint and NO ping endpoint. /health is this service's only
//      anonymous surface and both routes live in Endpoints/, on port 5101 (constraint C-L).
//    * It adds NO <Protobuf> item to the project. shared/PowerFramework.Contracts owns protocol-
//      definition compilation with a single GrpcServices="Both" item; a second would compile the
//      messages twice (constraint C-A).
//    * It references NO peer service project and exposes NO shared behaviour across the boundary. The
//      published contracts project is the only permitted cross-service coupling (constraint C-A).
//    * It mints NO token and holds NO signing key. Security is the sole issuer; this service holds
//      verification material only and validates inbound tokens against Security's published key set
//      (constraints C-F, C-G).
//    * It CONSTRUCTS NO SQL. Not a statement, not a clause, not a fragment, not a count wrapper. The
//      count statement's observable sentinels - the "1 AS _" column replacement [:L830] and the
//      "SELECT COUNT(1) AS CNT FROM ( ... ) pfwPagedSQL_Tbl" wrapper [:L834] - are built by
//      Tasks/SqlQueryTask.cs and are byte-exact parity criteria; nothing here concatenates SQL.
//    * It opens NO connection to SQL Server or Oracle. The two dialect values select PURE STRING
//      TRANSFORMS under Sql/Paging/ and nothing else, and SQLite is the only provisioned engine
//      (constraint C-E).
//    * It declares NO route, handler, options type or placeholder for DesignSystem, Documents,
//      Integration or ScriptBridge, and throws NO NotImplementedException. Those four reserved routes
//      are declarations on GATEWAY's routing table alone (constraint C-D).
//    * It DECLARES no SCREAMING_SNAKE constant. This folder is outside every naming-suppression glob
//      in the repository .editorconfig while TreatWarningsAsErrors is true, so the preserved legacy
//      identifiers are CONSUMED - the modification styles from Sql/ClauseModifier.cs, the return
//      codes from PowerFramework.Shared.Kernel, the buffer and item-status vocabularies from the
//      generated common.v1 types - and never redeclared here. No type below shadows a generated
//      contract type either.
//    * It re-implements NO status mapping and throws NO RpcException. persistence.v1.OperationStatus
//      is "the uniform outcome of every unary call in this file" by the contract's own words, and the
//      stream's status arm is its streaming twin, so every outcome - success, guard refusal, driver
//      failure, cancellation - is reported in ret_code. Nothing in C-05 produces Aborted; that is
//      C-06's concern.
//    * It surfaces NO receiver, NO move-data and NO has-receiver member. See the block below.
//
//  ============ THE THREE MEMBERS THAT ARE ABSENT ON PURPOSE, NOT BY OVERSIGHT ====================
//  The caller-side proxy publishes of_setreceiver [n_cst_threading_task_sqlquery.sru:L83],
//  of_movedata [:L81] and _of_hasreceiver [:L85], and NONE of the three appears on this contract or
//  in this file. They are the legacy MAIN-THREAD HAND-OVER path: they exist so that a DataWindow
//  living on the main thread can adopt the worker's carrier BY REFERENCE, and the event that carries
//  it has a body that is literally `Destroy Data` [:L268]. Object ownership transfer has no meaning
//  across a network boundary, where the two sides have separate heaps. Nothing observable is lost -
//  the DATA those members move is exactly what the data_chunk arm already carries - and the
//  protocol definition records the same omission for oncreatedata and ondatamove. Their absence is
//  therefore a documented gap in a presentational path, not a missing feature.
//  ==========================================================================================
//
//  ============ THE STREAMING HAZARD, WHICH IS THIS FILE'S MOST IMPORTANT CONSTRAINT =============
//  A gRPC call scope lives for the whole duration of the call, so a long-running server stream must
//  NOT capture a request-scoped resource - a DbContext above all - for its entire life. THIS FILE
//  CAPTURES NONE: it resolves no scoped service, opens no connection, constructs no DbContext and
//  holds no IServiceScope. Storage is reached only through the pooled session the task was bound to,
//  which is the connection-factory route rather than a per-request context, and every collaborator
//  injected below is safe for the process lifetime.
//
//  THE LEGACY CARRIES THE SAME DISCIPLINE FOR THE SAME REASON, and states it as a hazard in its own
//  documentation: a worker thread synchronously calling a main-thread object's string- or
//  blob-returning member can raise a memory exception when a Post message is pending against an
//  object that is itself destroyed, and a worker must null its main-thread reference on uninit to
//  release it [docs/PB多线程绕坑提示.md, hazards 1 and 2]. The managed analogue is deterministic
//  release: QueryStreamSink drops every queued payload reference the moment it has been written, and
//  the retrieval's finally block discards whatever is left, so no carrier state outlives the batch
//  that produced it. Cancellation is honoured at every batch boundary and answers RetCode.CANCELLED
//  rather than an exception, exactly as the changeset codec does between chunks.
//  ==========================================================================================
//
//  LEGACY SPECIFICATION (READ ONLY - never edited, never moved, never reformatted: constraint C-C)
//  Every behavioural claim in the comments below carries a ws_objects/** locator, because nothing
//  else in this repository can adjudicate it - the changelog stops at framework 3.0.7.2062 while the
//  commit history runs years later, and neither PowerBuilder project object would build as written.
//      ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru       the worker, 883 lines
//      ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlquery.sru    the caller-side twin
//      ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlbase.sru     the busy guards
//      ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru        the shared substrate
//      ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru               the session
//      ws_objects/pfw.thread.ext.pbl.src/dberrordata.srs                      the driver-error shape
//      ws_objects/pfw.utility.sqlite.pbl.src/n_sqliterecordset.sru            the forward-only cursor
//      ws_objects/pfw.shared.pbl.src/retcode.sru                              the return-code algebra
//      docs/PB多线程绕坑提示.md                                                 the two threading hazards
//  Unqualified [:Lnnn] references inside a member's comments name
//  n_cst_thread_task_sqlquery.sru, which is this contract's primary oracle; every reference to any
//  other file is spelled in full.
//
//  PRESERVED SEMANTICS, EACH ANNOTATED AT ITS POINT OF REPRODUCTION (constraint C-B)
//      the inclusive `<= 1000` chunk rejection, and the source comment that contradicts it  SetChunkSize
//      the 10000 default arriving from configuration rather than from a literal here        SetChunkSize
//      the deliberately INCONSISTENT guard boundaries across four numeric setters           ApplySpec
//      the one-based select index, where zero is invalid and never means "the first"        SetWhereClause
//      an empty clause is invalid, and space-only text is not                               SetWhereClause
//      E_BUSY from every mutator while a retrieval is in flight                             every setter
//      reset answering OK rather than the worker's own non-failing code                     Reset
//      reset clearing the row and page counts but NOT the record count                      Reset
//      the one-based chunk count and chunk index                                            QueryStreamSink
//      the fullState discriminator, which is a decoder selector and not a hint              QueryStreamSink
//      the -1/-1 not-counted sentinels, which are values and not an absence                 Count
//      the arithmetic count short-circuit that issues no statement at all                   Count
//      success read as the literal 1 on the counting path, not as the algebra's zero        Count
//      the tri-state algebra, where cancelled is neither succeeded nor failed               Reset, Query
//
//  NO RULES GOVERN THIS FILE. review_rules reports that no user rules were provided, which is not
//  latitude: the enterprise-standard baseline applies in their place - nullable reference types with
//  warnings as errors, no secret in source or settings, structured logging with the statement field
//  redacted, and versioned contracts as the only cross-service coupling.
// ==================================================================================================

using System.Collections.Concurrent;
using Grpc.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PowerFramework.Contracts.Persistence.V1;
using PowerFramework.Persistence.Buffers;
using PowerFramework.Persistence.Configuration;
using PowerFramework.Persistence.Errors;
using PowerFramework.Persistence.Runtime;
using PowerFramework.Persistence.Sql;
using PowerFramework.Persistence.Sql.Paging;
using PowerFramework.Persistence.Tasks;
using PowerFramework.Persistence.Transactions;
using PowerFramework.Shared.Diagnostics;

// The generated C-05 service base, reached through an alias for two reasons. First, the mandated class
// name below is the contract's own service name, so the bare name has to resolve to exactly one type
// and the alias is what guarantees which. Second, an alias makes the derivation visible at the class
// declaration instead of hiding a fully qualified name in it. This is the same shape the sibling
// adapters use in Grpc/TransactionService.cs and in DataServices' Grpc/DataWindowService.cs.
using GeneratedQueryServiceBase =
    global::PowerFramework.Contracts.Persistence.V1.QueryService.QueryServiceBase;

// The in-process algebra, aliased rather than namespace-imported so that a reader can see at every use
// site WHICH RetCode is meant. PowerFramework.Shared.Kernel.RetCode is a static class of `long`
// constants - the values every collaborator below returns and every guard tests. Its wire twin,
// PowerFramework.Contracts.Common.V1.RetCode.Types.Value, is NOT imported here at all: the single
// conversion between the two spaces lives in TransactionWireCodes, for the reason QueryWireCodes
// records, so this file never names the generated enum.
using Predicates = PowerFramework.Shared.Kernel.Predicates;
using RetCode = PowerFramework.Shared.Kernel.RetCode;

namespace PowerFramework.Persistence.Grpc;

// --------------------------------------------------------------------------------------------------
//  PART 1 OF 5 - THE OUTCOME PROJECTION, AND WHERE THE RET CODE CORRESPONDENCE LIVES
// --------------------------------------------------------------------------------------------------

/// <summary>
/// The single place a wire <see cref="OperationStatus"/> is constructed for contract C-05, and the
/// C-05 entry point to the one numeric conversion between the in-process return-code algebra and its
/// generated wire twin.
/// </summary>
/// <remarks>
/// <para>
/// <b>THE CONVERSION IS DELEGATED RATHER THAN REPEATED, AND THAT IS THE WHOLE POINT (review
/// invariant).</b> <c>PowerFramework.Contracts</c> declares ZERO <c>ProjectReference</c> - deliberately
/// none to <c>PowerFramework.Shared.Kernel</c>, so that the published boundary cannot become a back
/// door for shared behaviour. The consequence is that the numeric agreement between the generated
/// <c>common.v1.RetCode.Value</c> enum and <c>shared/PowerFramework.Shared.Kernel/RetCode.cs</c> is
/// checked by NOTHING: no compiler, no analyzer and no single-sided unit test. A divergence would be a
/// silent wire-compatibility defect. This project is the first and only place both are referenced
/// together, which makes the correspondence auditable here and nowhere else - so it must have exactly
/// ONE home in the project, and it already has one:
/// <see cref="TransactionWireCodes.ToWireRetCode(long)"/>.
/// </para>
/// <para>
/// This type therefore performs NO cast of its own. It is the query-side factory for the outcome
/// message, and every code it carries passes through that single conversion. A second cast declared
/// here - even an identical one - would create a second place for the invariant to drift, which is
/// precisely the failure the single-helper rule exists to prevent. The value-for-value assertion
/// belongs to <c>PowerFramework.Persistence.Tests</c>, not here.
/// </para>
/// <para>
/// <b>THE FOURTH OVERLOAD IS WHY THIS TYPE EXISTS AT ALL, RATHER THAN JUST THE DELEGATION.</b> The
/// legacy reports a database failure through TWO events, and both carry different things:
/// <c>OnError(code, msg)</c> for the framework-level outcome and
/// <c>OnDBError(sqldbcode, sqlerrtext, sqlsyntax, buffer, row)</c> for the driver-level detail. On a
/// database failure BOTH fire - visible directly on the counting path, where the driver payload is
/// published from the transaction's own code and text and the framework event carrying 检索失败
/// follows immediately [<c>:L851-L858</c>]. C-05 must therefore be able to answer with a code, a
/// diagnostic AND a driver payload at once, and no existing factory takes all three.
/// </para>
/// <para>
/// <b>THE DIAGNOSTIC IS OPAQUE DISPLAY TEXT AND MAY BE NON-ENGLISH (constraint C-B).</b> Many in-scope
/// arms synthesize Chinese diagnostics - 无效的分页设置! for an invalid paging setting [<c>:L308</c>],
/// SQL解析失败! for a parse failure [<c>:L315</c>], SQL参数绑定失败! for a binding failure
/// [<c>:L606</c>], SQL为空! for an empty statement [<c>:L616</c>] - and the contract requires consumers
/// to branch on <c>ret_code</c> rather than parse this string. Empty is legitimate: the
/// unsupported-dialect arm passes an empty message deliberately [<c>:L397</c>].
/// </para>
/// </remarks>
internal static class QueryWireCodes
{
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
    /// case for any of the argument guards this file reproduces.
    /// </remarks>
    internal static OperationStatus Status(long code) => new()
    {
        RetCode = TransactionWireCodes.ToWireRetCode(code),
    };

    /// <summary>
    /// Builds a status carrying an outcome code and a diagnostic.
    /// </summary>
    /// <param name="code">The in-process return code.</param>
    /// <param name="errorText">
    /// The diagnostic, treated as OPAQUE DISPLAY TEXT. A <see langword="null"/> is normalised to the
    /// empty string because the generated setter refuses one, and an absent diagnostic is a legitimate
    /// outcome rather than an error in its own right.
    /// </param>
    /// <returns>A status with both members populated and no driver detail.</returns>
    /// <remarks>
    /// <b>Nothing secret can reach this parameter from this file (constraint C-F).</b> Almost every call
    /// site below passes a fixed sentence that quotes no value: no statement text, no clause body, no
    /// identifier, no connection parameter and no handle value is ever formatted into one. THE SOLE
    /// EXCEPTION IS THE TERMINAL RELAY on the retrieval path, which passes the diagnostic the worker task
    /// raised - text this file did not write and cannot vouch for - and that one call site masks it
    /// through the redactor before it arrives here. This overload therefore does not redact: doing so
    /// twice would be harmless, since the mask is idempotent, but it would put the guarantee in the wrong
    /// place. The guarantee belongs where a text of unknown provenance enters, and there is exactly one
    /// such place.
    /// </remarks>
    internal static OperationStatus Status(long code, string? errorText) => new()
    {
        RetCode = TransactionWireCodes.ToWireRetCode(code),
        ErrorText = errorText ?? string.Empty,
    };

    /// <summary>
    /// Builds a status carrying an outcome code, a diagnostic and the driver-level detail beneath it.
    /// </summary>
    /// <param name="code">The in-process return code.</param>
    /// <param name="errorText">The diagnostic. See the two-argument overload.</param>
    /// <param name="dbError">
    /// The in-process driver payload, projected onto the wire by <c>Errors/SqlRedactor.cs</c>'s
    /// extension - the ONLY sanctioned producer of a wire <c>DbError</c> in this service.
    /// </param>
    /// <returns>A status with all three members populated.</returns>
    /// <remarks>
    /// <para>
    /// <b>THE STATEMENT FIELD IS MASKED BY THE PROJECTION AND NOT BY THIS FILE (constraint C-F).</b>
    /// <c>ToDbError</c> reaches its redaction policy directly rather than accepting one as an argument,
    /// so the masking cannot be weakened from a call site or from a container registration. That is
    /// stronger than a mandatory-redactor parameter would have been, and it is why no
    /// <c>ISqlRedactor</c> is injected anywhere in this file: an injected policy is a replaceable
    /// policy.
    /// </para>
    /// <para>
    /// <b><c>DbError.Sqlsyntax</c> is never assigned anywhere in this file.</b> The only way to emit
    /// unmasked statement text would be to read the in-process member and hand it somewhere directly,
    /// and no line below does. The masking matters here because the oracle's field carries the COMPLETE
    /// generated statement - and with <c>DisableBind=1</c> the runtime interpolates values as literals
    /// rather than binding them
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru:L128</c>], so that text can
    /// carry live row data while the legacy logger redacts nothing at all. The counting path is a
    /// SECOND, DISTINCT binding site from the retrieval path's, and it is the one whose statement this
    /// contract is most likely to answer with.
    /// </para>
    /// </remarks>
    internal static OperationStatus Status(long code, string? errorText, in DbErrorData dbError) => new()
    {
        RetCode = TransactionWireCodes.ToWireRetCode(code),
        ErrorText = errorText ?? string.Empty,
        DbError = dbError.ToDbError(),
    };
}

// --------------------------------------------------------------------------------------------------
//  PART 2 OF 5 - THE THREE INJECTED SEAMS
// --------------------------------------------------------------------------------------------------

/// <summary>
/// Observes the two failure events the worker task raises, so that the legacy's own diagnostics reach
/// the wire.
/// </summary>
/// <remarks>
/// <para>
/// <b>THIS SEAM EXISTS BECAUSE THE FAILURE DETAIL DOES NOT TRAVEL ON THE RESULT SINK.</b>
/// <c>Tasks/SqlQueryTask.ExecuteAsync</c> answers a bare <see cref="long"/>. Its diagnostics go
/// elsewhere: the framework-level message to <c>ISqlTaskHost.OnError(errCode, errInfo)</c> and the
/// driver-level payload to <c>ISqlTaskProxy.OnDbError(in DbErrorData)</c>, both declared in
/// <c>Tasks/SqlTaskBase.cs</c> and both reproducing the legacy's two-event reporting. Without an
/// observer the wire would carry a code and nothing else, and every one of the nine failure arms C-05
/// enumerates would arrive stripped of the text the legacy fired beside it.
/// </para>
/// <para>
/// <b>IT IS THE MANAGED READING OF THE PROXY'S OWN LAST-ERROR FIELD, not an invention.</b> The
/// caller-side twin keeps exactly this state and publishes exactly this accessor:
/// <c>_lastDBError</c>, cleared on prepare and on a successful reset
/// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlbase.sru:L65, :L208</c>] and read
/// back through <c>of_getlastdberrordata()</c>. This contract's server plays the caller-side role, so
/// it keeps the same field with the same lifetime.
/// </para>
/// </remarks>
internal interface IQueryFaultSink
{
    /// <summary>
    /// Records the framework-level failure event.
    /// </summary>
    /// <param name="code">The return code the legacy passed as the event's first argument.</param>
    /// <param name="errorText">
    /// The diagnostic the legacy passed as its second argument, verbatim and possibly non-English.
    /// </param>
    void OnError(long code, string errorText);

    /// <summary>
    /// Records the driver-level failure payload.
    /// </summary>
    /// <param name="error">
    /// The five-field structure of
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/dberrordata.srs:L4-L8</c>]: the driver code, its text, the
    /// offending statement, the offending buffer - the field is spelled <c>buffer</c> and its type is
    /// <c>dwbuffer</c> - and the offending row. Passed by <see langword="in"/> because it is a readonly
    /// record struct and copying it would be pure cost.
    /// </param>
    void OnDbError(in DbErrorData error);
}

/// <summary>
/// Produces the worker-side query task this adapter drives.
/// </summary>
/// <remarks>
/// <para>
/// <b>THIS ADAPTER MUST NOT CONSTRUCT A TASK ITSELF, AND THE REASON IS STRUCTURAL RATHER THAN
/// STYLISTIC (constraint C-H).</b> <c>Tasks/SqlQueryTask</c> takes twelve collaborators, three of which
/// are abstractions over the legacy threading substrate - <c>ISqlTaskHost</c>,
/// <c>IQueryTransactionSurface</c> and <c>IQueryDataWindowRuntime</c>. Composing them is composition-
/// root work, not adapter work: an adapter that newed up a host would be inventing the worker-thread
/// contract the legacy encodes in its own source comments, and it would put behaviour in the one file
/// whose job is to have none.
/// </para>
/// <para>
/// <b>The seam is also what keeps the per-service coverage gate reachable.</b> A test substitutes a
/// factory that answers a task built from its own doubles, so every guard, every translation and the
/// whole stream projection below is exercisable with no database, no thread pool and no container.
/// </para>
/// <para>
/// The returned task is UNBOUND: it carries no transaction descriptor and no settings. Binding it to
/// a session and applying the caller's specification are translation steps, so they are performed by
/// the adapter where their return codes are observable, rather than hidden inside the factory where a
/// rejection would have nowhere to go.
/// </para>
/// </remarks>
internal interface IQueryTaskFactory
{
    /// <summary>
    /// Creates one worker-side query task.
    /// </summary>
    /// <param name="faults">
    /// The observer the task's framework-level and driver-level failure events are reported to. The
    /// factory is responsible for wiring it into the task's host and proxy surfaces, because those are
    /// the objects it composes.
    /// </param>
    /// <returns>A task in its freshly constructed state, with configuration defaults applied.</returns>
    SqlQueryTask Create(IQueryFaultSink faults);
}

/// <summary>
/// Runs one retrieval, publishing its result into a sink.
/// </summary>
/// <remarks>
/// <para>
/// <b>ONE METHOD, ONE LINE, AND IT IS A SEAM RATHER THAN A LAYER (constraint C-H).</b> The execution
/// this indirection covers is <c>Tasks/SqlQueryTask.ExecuteAsync</c> - the port of <c>event ondotask</c>
/// [<c>:L500-L882</c>] - which needs a transaction, a data store, a paging rewriter and a codec before
/// it can produce a single row. Naming it as a seam lets the ENTIRE stream projection below - the
/// one-based chunk counters, the full-state discriminator, the ordered emission of five different arms,
/// cancellation mid-stream and the terminal failure detail - be driven directly and asserted exactly,
/// which is the only way those properties are testable without a storage engine.
/// </para>
/// <para>
/// It deliberately does NOT abstract the task's setters. Those are called on the concrete type so that
/// this file cannot quietly acquire a second implementation of a guard that already has one; the
/// duplication a façade would invite is the exact failure mode constraint C-B is about.
/// </para>
/// </remarks>
internal interface IQueryRetrievalRunner
{
    /// <summary>
    /// Executes the retrieval.
    /// </summary>
    /// <param name="task">The configured, session-bound task.</param>
    /// <param name="sink">The destination the row count, chunks, children and page totals publish to.</param>
    /// <param name="cancellationToken">
    /// The caller's token. Cancellation is a RETURN CODE on this contract, never an exception, so an
    /// implementation answers <c>RetCode.CANCELLED</c> rather than throwing.
    /// </param>
    /// <returns>
    /// <c>RetCode.OK</c>, <c>RetCode.CANCELLED</c>, or one of the nine failure codes C-05 enumerates.
    /// </returns>
    Task<long> RunAsync(SqlQueryTask task, IQueryResultSink sink, CancellationToken cancellationToken);
}

/// <summary>
/// The production runner: it forwards to the worker task and adds nothing.
/// </summary>
/// <remarks>
/// Deliberately trivial. Every decision the retrieval makes - the hand-over predicate, the codec
/// discriminator, the chunk arithmetic, the cancellation points, the counting gate and its arithmetic
/// short-circuit - already lives in <c>Tasks/SqlQueryTask.cs</c> beside the oracle locators that
/// justify it. Anything added here would be a second copy of one of them.
/// </remarks>
internal sealed class QueryRetrievalRunner : IQueryRetrievalRunner
{
    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">
    /// <paramref name="task"/> or <paramref name="sink"/> is <see langword="null"/>.
    /// </exception>
    public Task<long> RunAsync(
        SqlQueryTask task,
        IQueryResultSink sink,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(sink);

        return task.ExecuteAsync(sink, cancellationToken);
    }
}

// --------------------------------------------------------------------------------------------------
//  PART 3 OF 5 - THE HANDLE CORRELATION AND THE CALLER-SIDE STATE THE LEGACY KEPT IN FIELDS
// --------------------------------------------------------------------------------------------------

/// <summary>
/// The three count values the legacy publishes to its caller, plus whether a counting statement was
/// actually issued.
/// </summary>
/// <param name="RowCount">
/// The row count of the whole result, published FIRST and before any row [<c>:L83</c>]. The managed
/// reading of <c>of_getrowcount()</c>
/// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlquery.sru:L78</c>].
/// </param>
/// <param name="PageCount">
/// The total page count, or <c>-1</c> when counting did not happen. The managed reading of
/// <c>of_getpagecount()</c> [<c>n_cst_threading_task_sqlquery.sru:L70</c>].
/// </param>
/// <param name="RecordCount">
/// The total record count, or <c>-1</c> when counting did not happen. The managed reading of
/// <c>of_getrecordcount()</c> [<c>n_cst_threading_task_sqlquery.sru:L71</c>].
/// </param>
/// <param name="Counted">
/// Whether a counting statement was issued. FALSE both when counting was skipped entirely and when the
/// totals were inferred arithmetically. This is the ONE member with no legacy field behind it: the
/// legacy caller cannot tell the two apart, and the contract adds the distinction rather than leaving a
/// consumer to deduce it from a missing statement.
/// </param>
internal readonly record struct QueryCountSnapshot(
    long RowCount,
    long PageCount,
    long RecordCount,
    bool Counted);

/// <summary>
/// The failure detail recorded for one task, captured atomically.
/// </summary>
/// <param name="Code">The framework-level code, or <c>RetCode.OK</c> when no failure was reported.</param>
/// <param name="ErrorText">The framework-level diagnostic, verbatim. Empty when none was reported.</param>
/// <param name="HasDbError">
/// Whether the driver-level event fired. The contract requires <c>db_error</c> to be present ONLY when
/// the legacy would also have fired <c>OnDBError</c>, so this flag - and not a value test on the payload
/// - is what decides whether the wire status carries one.
/// </param>
/// <param name="DbError">The driver-level payload. Meaningless unless <paramref name="HasDbError"/>.</param>
internal readonly record struct QueryFaultSnapshot(
    long Code,
    string ErrorText,
    bool HasDbError,
    DbErrorData DbError);

/// <summary>
/// Records the last framework-level and driver-level failure the worker task reported, reproducing the
/// caller-side proxy's own last-error field and its lifetime.
/// </summary>
/// <remarks>
/// <para>
/// <b>LAST WINS, AND THAT IS THE LEGACY'S BEHAVIOUR RATHER THAN A SIMPLIFICATION.</b> The proxy holds a
/// single <c>_lastDBError</c> structure and assigns it on every driver event
/// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlbase.sru:L65, :L208</c>], so a second
/// failure overwrites the first and only the most recent is readable. Accumulating a list here would
/// expose a history the legacy never had.
/// </para>
/// <para>
/// Guarded by a lock even though the retrieval publishes from a single logical flow: the events arrive
/// from the worker-affine task while the adapter reads on the call's own flow, and the two are only
/// ordered by an await. The lock is a LEAF - it never acquires another - so no lock-ordering cycle is
/// possible with the entry that owns it.
/// </para>
/// </remarks>
internal sealed class QueryFaultRecorder : IQueryFaultSink
{
    private readonly Lock _gate = new();
    private long _code = RetCode.OK;
    private string _errorText = string.Empty;
    private DbErrorData _dbError = DbErrorData.Empty;
    private bool _hasDbError;

    /// <inheritdoc />
    public void OnError(long code, string errorText)
    {
        lock (_gate)
        {
            _code = code;

            // Normalised because the generated string setter refuses null and because an absent
            // diagnostic is a legitimate legacy outcome - the unsupported-dialect arm passes an empty
            // message deliberately [:L397].
            _errorText = errorText ?? string.Empty;
        }
    }

    /// <inheritdoc />
    public void OnDbError(in DbErrorData error)
    {
        lock (_gate)
        {
            _dbError = error;
            _hasDbError = true;
        }
    }

    /// <summary>
    /// Reads the recorded failure as one atomic value.
    /// </summary>
    /// <returns>The snapshot. Its code is <c>RetCode.OK</c> when nothing was reported.</returns>
    internal QueryFaultSnapshot Snapshot()
    {
        lock (_gate)
        {
            return new QueryFaultSnapshot(_code, _errorText, _hasDbError, _dbError);
        }
    }

    /// <summary>
    /// Forgets the recorded failure.
    /// </summary>
    /// <remarks>
    /// Called at the start of every retrieval and after a successful reset, which are exactly the two
    /// moments the legacy clears its own field - on prepare
    /// [<c>n_cst_threading_task_sqlbase.sru:L208</c>] and after the worker accepted the reset
    /// [<c>:L65</c>].
    /// </remarks>
    internal void Clear()
    {
        lock (_gate)
        {
            _code = RetCode.OK;
            _errorText = string.Empty;
            _dbError = DbErrorData.Empty;
            _hasDbError = false;
        }
    }
}

/// <summary>
/// One server-held query task: the wire handle, the session it runs against, the worker task itself,
/// the failure recorder attached to it, and the caller-side counts the legacy kept in private fields.
/// </summary>
/// <remarks>
/// <para>
/// <b>THE LEGACY HAD NO EQUIVALENT OF THE HANDLE, WHICH IS WHY THIS TYPE IS NEW.</b> In process the
/// task's state is reached through a POINTER - it holds its chunk size, paging, clause arrays and
/// unique-index columns as private instance fields [<c>:L25-L47</c>] and a caller configures it through
/// a series of setters that each answer a code. A pointer cannot be serialized, so the wire substitute
/// is an opaque server-issued identifier, and this object is the only thing that maps that identifier
/// onto the task. Correlation is therefore EXPLICIT and never ambient: there is no thread-local, no
/// asynchronous-local and no per-connection implicit state anywhere in this file.
/// </para>
/// <para>
/// <b>THE COUNTS ARE THE PROXY'S THREE FIELDS, AND THEIR CLEARING ASYMMETRY IS A PRESERVED DEFECT
/// (constraint C-B).</b> The caller-side twin keeps <c>_nRowCount</c>, <c>_nPageCount</c> and
/// <c>_nRecordCount</c> [<c>n_cst_threading_task_sqlquery.sru:L46-L50</c>] and clears them in TWO
/// different places with DIFFERENT membership:
/// </para>
/// <code>
///   of_reset()   clears _nRowCount [:L292] and _nPageCount [:L293]   and NOT _nRecordCount
///   OnPrepare()  clears _nRecordCount [:L507], _nPageCount [:L508] and _nRowCount [:L509]
/// </code>
/// <para>
/// The reset really does leave the record count standing. It looks like an oversight and it is
/// observable, so <see cref="ClearAfterReset"/> reproduces it exactly while <see cref="TryBeginRun"/>
/// - the prepare-equivalent - clears all three. Harmonising them would silently change what a
/// <c>Count</c> call answers after a reset.
/// </para>
/// <para>
/// <b>DISPOSAL IS DEFERRED WHILE A RETRIEVAL IS IN FLIGHT, WHICH IS THE MANAGED FORM OF HAZARD 2.</b>
/// The legacy's release discipline is that the adopter takes the payload FIRST and only then does the
/// sender drop its reference [<c>:L87-L89</c>], and its threading documentation warns that destroying an
/// object with work still pending against it raises a memory exception
/// [<c>docs/PB多线程绕坑提示.md</c>, hazard 1]. So a release request unregisters the handle immediately -
/// the contract requires a second release to answer <c>E_INVALID_HANDLE</c> rather than succeed
/// silently - while the actual disposal is handed to whichever of the two paths finishes last. No
/// use-after-dispose is reachable, and no running retrieval is torn out from under its own stream.
/// </para>
/// </remarks>
internal sealed class QueryTaskEntry : IDisposable
{
    // THE COUNT FIELDS' OWN GATE, AND NOTHING ELSE'S. The running / released / disposed machine moved to
    // TaskOperationLatch, which is shared with the update and command contracts so its disposal-handoff
    // table exists in exactly one place. This gate is a leaf over four fields and never runs a
    // collaborator, so the two locks cannot form a cycle: nothing below takes the latch while holding
    // this gate, and the latch takes nothing at all.
    private readonly Lock _gate = new();
    private readonly TaskOperationLatch _latch = new();
    private long _rowCount;
    private long _pageCount;
    private long _recordCount;
    private bool _counted;

    /// <summary>
    /// Creates the entry over an already-created task.
    /// </summary>
    /// <param name="taskId">The opaque handle value. Server-issued and never parsed.</param>
    /// <param name="sessionId">
    /// The session this task runs against, retained for diagnostics only. A task handle is scoped to
    /// one service and one instance, exactly as the legacy object's pointer was.
    /// </param>
    /// <param name="task">The worker task. This entry owns its lifetime.</param>
    /// <param name="faults">The recorder wired into that task's failure events.</param>
    /// <exception cref="ArgumentNullException">A required collaborator is <see langword="null"/>.</exception>
    internal QueryTaskEntry(
        string taskId,
        string sessionId,
        SqlQueryTask task,
        QueryFaultRecorder faults)
    {
        TaskId = taskId ?? throw new ArgumentNullException(nameof(taskId));
        SessionId = sessionId ?? throw new ArgumentNullException(nameof(sessionId));
        Task = task ?? throw new ArgumentNullException(nameof(task));
        Faults = faults ?? throw new ArgumentNullException(nameof(faults));
    }

    /// <summary>The opaque handle value this entry answers to.</summary>
    internal string TaskId { get; }

    /// <summary>The session handle value the task was bound to.</summary>
    internal string SessionId { get; }

    /// <summary>The worker task every setter and the retrieval delegate to.</summary>
    internal SqlQueryTask Task { get; }

    /// <summary>The failure recorder attached to the task's two error events.</summary>
    internal QueryFaultRecorder Faults { get; }

    /// <summary>
    /// The caller identity this task is attributed to, for the per-caller ceiling.
    /// </summary>
    /// <value>
    /// The token subject, or the resolver's unattributed bucket. Set once by the registry at registration
    /// and never rendered into a response.
    /// </value>
    internal string Principal { get; set; } = HandlePrincipalResolver.Unattributed;

    /// <summary>
    /// When a call last named this task, as UTC ticks read from the injected clock.
    /// </summary>
    /// <remarks>
    /// Written and read through <see cref="System.Threading.Volatile"/> by the registry: it is touched from
    /// arbitrary request threads and read by the reclaim pass on another, and a torn 64-bit read would make
    /// a fresh handle look ancient.
    /// </remarks>
    internal long LastActivityTicks;

    /// <summary>
    /// Whether a retrieval is in flight for this task.
    /// </summary>
    /// <remarks>
    /// The managed reading of the caller-side <c>of_IsBusy()</c>, which is the guard the legacy has LIVE
    /// on every mutator [<c>n_cst_threading_task_sqlbase.sru:L57, :L73, :L95, :L110, :L124, :L144,
    /// :L162, :L179, :L188</c>]. It is deliberately the ADAPTER's latch and not the task's own
    /// <c>IsRunning</c>: this one is raised before the caller's specification is merged, so a setter
    /// arriving during that merge is refused rather than racing it, and the worker's own
    /// <c>#Running</c> guard [<c>:L237</c>] still stands behind it.
    /// </remarks>
    internal bool IsRunning => _latch.IsRunning;

    /// <summary>
    /// Whether a release has been requested for this task.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Read to distinguish the two reasons a run or a mutation can be refused. The handle is unregistered
    /// before the release is recorded, so in the ordinary case a released task is simply not resolvable and
    /// this property is never consulted; it exists for the narrow interleaving where a caller resolved the
    /// handle a moment before another call released it, and it is what stops that caller being told the
    /// task is BUSY when the truth is that it is GONE.
    /// </para>
    /// <para>
    /// <b>A CHEAP EARLY-OUT AND NOT A GUARD.</b> The authoritative refusal is
    /// <see cref="TryBeginExclusive"/>'s, which answers <see cref="TaskLatchOutcome.Gone"/> and
    /// <see cref="TaskLatchOutcome.Busy"/> as one indivisible step. Reading this property and then acting
    /// would be exactly the check-then-act the lease removes, so it is read only to shape a refusal that
    /// has already been decided.
    /// </para>
    /// </remarks>
    internal bool IsReleased => _latch.IsReleased;

    /// <summary>
    /// Reads the caller-side counts as one atomic value.
    /// </summary>
    internal QueryCountSnapshot Counts
    {
        get
        {
            lock (_gate)
            {
                return new QueryCountSnapshot(_rowCount, _pageCount, _recordCount, _counted);
            }
        }
    }

    /// <summary>
    /// Latches the task as running and clears the caller-side state, refusing when it is already
    /// running, already released or already disposed.
    /// </summary>
    /// <returns>
    /// <see cref="TaskLatchOutcome.Acquired"/> when the caller owns the run and must call
    /// <see cref="EndRun"/>; otherwise the reason it was refused, which the call site maps straight onto a
    /// code without reading any further state.
    /// </returns>
    /// <remarks>
    /// The clear is the prepare-equivalent, so ALL THREE counts go and the recorded failure goes with
    /// them [<c>:L507-L509</c>, and <c>n_cst_threading_task_sqlbase.sru:L208</c>]. It happens AFTER the
    /// lease is taken, which is what makes it safe to perform outside the latch: no other operation can
    /// be inside this entry once the lease is held, so nothing can observe the interval between the two
    /// steps except a <c>Count</c> reader, which is a bare accessor with no lease in the oracle either.
    /// </remarks>
    internal TaskLatchOutcome TryBeginRun()
    {
        TaskLatchOutcome outcome = _latch.TryBegin();

        if (outcome != TaskLatchOutcome.Acquired)
        {
            return outcome;
        }

        lock (_gate)
        {
            _rowCount = 0L;
            _pageCount = 0L;
            _recordCount = 0L;
            _counted = false;
        }

        Faults.Clear();

        return TaskLatchOutcome.Acquired;
    }

    /// <summary>
    /// Takes the lease for a MUTATION rather than a run - a reset or a setter.
    /// </summary>
    /// <returns>
    /// <see cref="TaskLatchOutcome.Acquired"/> when the caller owns the task and must call
    /// <see cref="EndExclusive"/>; otherwise the reason it was refused.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>THE SAME LEASE A RUN TAKES, AND DELIBERATELY SO.</b> The legacy guards every mutator with
    /// <c>of_IsBusy()</c> [<c>n_cst_threading_task_sqlbase.sru:L57, :L73, :L95, :L110, :L124, :L144,
    /// :L162, :L179, :L188</c>] so that a mutation and a retrieval cannot overlap. Sharing one lease
    /// additionally stops two MUTATIONS overlapping, which in process could not happen - the caller was
    /// one thread - and across a boundary can: two setters merging halves of two different requests'
    /// specifications into one task is the same corruption, arrived at from the other side.
    /// </para>
    /// <para>
    /// It does NOT clear the counts. A reset clears a documented subset through
    /// <see cref="ClearAfterReset"/> and a setter clears nothing at all; clearing here would silently
    /// change what a <c>Count</c> call answers after a mere setter.
    /// </para>
    /// </remarks>
    internal TaskLatchOutcome TryBeginExclusive() => _latch.TryBegin();

    /// <summary>
    /// Unlatches the run.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when a release arrived while the retrieval was in flight, so the caller
    /// must now dispose; otherwise <see langword="false"/>.
    /// </returns>
    internal bool EndRun() => _latch.End();

    /// <summary>
    /// Unlatches a mutation. Identical to <see cref="EndRun"/>, named for its call site.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when a release arrived while the mutation was in flight, so the caller must
    /// now dispose; otherwise <see langword="false"/>.
    /// </returns>
    internal bool EndExclusive() => _latch.End();

    /// <summary>
    /// Marks the entry released.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the caller must dispose now, and <see langword="false"/> when
    /// disposal belongs to the retrieval still in flight or has already happened.
    /// </returns>
    internal bool Release() => _latch.RequestRelease();

    /// <summary>
    /// Records the row count of the whole result.
    /// </summary>
    /// <param name="rowCount">
    /// The value the legacy publishes as <c>OnDataReceived(rowCount)</c> [<c>:L83</c>]. It counts the
    /// PRIMARY buffer only; filtered rows are folded into the chunk arithmetic instead.
    /// </param>
    internal void RecordRowCount(long rowCount)
    {
        lock (_gate)
        {
            _rowCount = rowCount;
        }
    }

    /// <summary>
    /// Records the paging totals and derives whether a counting statement was issued.
    /// </summary>
    /// <param name="pageCount">The legacy <c>pagecount</c> argument, or <c>-1</c>.</param>
    /// <param name="recordCount">The legacy <c>recordcount</c> argument, or <c>-1</c>.</param>
    /// <remarks>
    /// <para>
    /// <b>THE `counted` FLAG IS DERIVED HERE BECAUSE THE LEGACY EVENT DOES NOT CARRY IT.</b>
    /// <c>OnPageReceived(pagecount, recordcount)</c> [<c>:L865</c>] has exactly two arguments, and it
    /// fires on EVERY path - counted, inferred and skipped alike. The derivation reuses the worker's own
    /// pure predicates rather than restating them, so there is no second copy of either rule:
    /// </para>
    /// <list type="number">
    ///   <item><description>
    ///   both totals equal to the not-counted sentinel means the three-condition gate at [<c>:L818</c>]
    ///   refused - a stored-procedure source, paging off, or counting off - and its else-arm assigned
    ///   <c>-1</c> to both [<c>:L861-L862</c>];
    ///   </description></item>
    ///   <item><description>
    ///   otherwise the arithmetic short-circuit at [<c>:L820-L823</c>] is asked, through the worker's own
    ///   <c>TryInferPageCounts</c>, whether THIS row count on THIS page could have been inferred without
    ///   a statement. When it could, no statement was issued.
    ///   </description></item>
    /// </list>
    /// <para>
    /// The order matters: the sentinel test comes first because an unpaged task has a zero page size, and
    /// the empty-first-page disjunct would otherwise claim an inference the legacy never performed.
    /// </para>
    /// </remarks>
    internal void RecordPageCounts(long pageCount, long recordCount)
    {
        lock (_gate)
        {
            _pageCount = pageCount;
            _recordCount = recordCount;

            bool skipped = pageCount == SqlQueryTask.NotCountedValue
                && recordCount == SqlQueryTask.NotCountedValue;

            _counted = !skipped
                && !SqlQueryTask.TryInferPageCounts(
                    _rowCount,
                    Task.PageSize,
                    Task.PageIndex,
                    out _,
                    out _);
        }
    }

    /// <summary>
    /// Clears the caller-side state a successful reset clears - and only that state.
    /// </summary>
    /// <remarks>
    /// <b>THE RECORD COUNT IS DELIBERATELY LEFT STANDING (constraint C-B).</b> The legacy reset names
    /// <c>_nRowCount</c> [<c>:L292</c>] and <c>_nPageCount</c> [<c>:L293</c>] and does not name
    /// <c>_nRecordCount</c>, although its own prepare handler clears all three [<c>:L507-L509</c>].
    /// Adding a clear here would be a silent behavioural change. The recorded failure IS cleared, because
    /// the base proxy's reset clears its last-error field at [<c>n_cst_threading_task_sqlbase.sru:L65</c>].
    /// The derived <c>counted</c> flag is cleared with the page count it describes: it has no legacy
    /// field of its own, and leaving it set beside a zeroed page count would assert a statement was
    /// issued for a total that no longer exists.
    /// </remarks>
    internal void ClearAfterReset()
    {
        lock (_gate)
        {
            _rowCount = 0L;
            _pageCount = 0L;
            _counted = false;
            Faults.Clear();
        }
    }

    /// <summary>
    /// Disposes the worker task. Idempotent.
    /// </summary>
    public void Dispose()
    {
        if (!_latch.TryClaimDisposal())
        {
            return;
        }

        // Outside the latch: the task's own teardown must not run under a lock this file's readers also
        // take, and by this point no other path can reach it - the handle is unregistered and the
        // disposal has been claimed.
        Task.Dispose();
    }
}

/// <summary>
/// The process-wide table mapping opaque task handles onto server-held query tasks.
/// </summary>
/// <remarks>
/// <para>
/// Registered as a singleton, because task state is IN-PROCESS exactly as the legacy object's was: a
/// handle is not portable across service instances and the contract says so. Keyed with an ordinal
/// comparer because a handle is an opaque token rather than text, so culture-sensitive comparison would
/// be both wrong and slower.
/// </para>
/// <para>
/// <b>A handle is NOT a credential and authorizes nothing (constraint C-F).</b> Authentication on this
/// edge is the bearer configuration <c>Program.cs</c> installs; this table only answers whether a token
/// names something that exists.
/// </para>
/// </remarks>
internal sealed class QueryTaskRegistry
{
    private readonly ConcurrentDictionary<string, QueryTaskEntry> _tasks = new(StringComparer.Ordinal);

    /// <summary>The ceiling on live query tasks, per caller and in total.</summary>
    private readonly HandleQuota _quota;

    /// <summary>The one clock. Stamps activity and measures idleness.</summary>
    private readonly TimeProvider _time;

    /// <summary>Resolves the caller a new task is attributed to.</summary>
    private readonly HandlePrincipalResolver _principals;

    /// <summary>Optional structured logger, for reclaimed and drained tasks.</summary>
    private readonly ILogger<QueryTaskRegistry>? _logger;

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
    public QueryTaskRegistry(
        IOptions<PersistenceOptions> options,
        TimeProvider time,
        HandlePrincipalResolver? principals = null,
        ILogger<QueryTaskRegistry>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        HandleLifecycleOptions handles = options.Value.Handles;

        _quota = new HandleQuota("query task", handles.MaxTotalPerRegistry, handles.MaxPerPrincipal);
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _principals = principals ?? new HandlePrincipalResolver();
        _logger = logger;
    }

    /// <summary>The number of live tasks. Exposed for diagnostics and for assertions in tests.</summary>
    internal int Count => _tasks.Count;

    /// <summary>
    /// The session identity of every live task, so the session registry can pin what is still in use.
    /// </summary>
    /// <remarks>
    /// SNAPSHOTTED RATHER THAN LAZY. The reclaim pass reads this to decide which sessions are spoken for,
    /// and an enumeration that observed a concurrent release midway could pin nothing while a task was
    /// still live - or, worse, fail to pin one.
    /// </remarks>
    internal IReadOnlyCollection<string> LiveSessionIds => [.. _tasks.Values.Select(entry => entry.SessionId)];

    /// <summary>
    /// Mints a handle for a task and registers it.
    /// </summary>
    /// <param name="sessionId">The session the task is bound to.</param>
    /// <param name="task">The worker task.</param>
    /// <param name="faults">The recorder wired into that task's failure events.</param>
    /// <param name="diagnostic">Receives the diagnostic text when the call refuses; empty on success.</param>
    /// <returns>The registered entry, carrying the minted handle value.</returns>
    /// <exception cref="InvalidOperationException">
    /// The minted identifier collided with a live one. Unreachable in practice with a version-4 GUID and
    /// reported rather than silently overwriting a live task, because overwriting would orphan a task
    /// that another call may already be streaming.
    /// </exception>
    /// <remarks>
    /// A GUID in its compact form, matching the shape the sibling session registry mints. The value is
    /// opaque BY CONTRACT: consumers must not parse it, derive one, reuse one across services or attach
    /// meaning to its shape - it replaces a pointer, and a pointer has no structure a caller is entitled
    /// to read. A string rather than an integer both leaves the representation to the server and removes
    /// the invitation to guess a neighbouring value.
    /// </remarks>
    internal QueryTaskEntry? Register(
        string sessionId,
        SqlQueryTask task,
        QueryFaultRecorder faults,
        out string diagnostic)
    {
        // THE CEILING IS TESTED BEFORE THE HANDLE IS MINTED, so a refusal registers nothing and the caller
        // can dispose the task it built. Reserving after minting would leave a live entry to unwind.
        string principal = _principals.Resolve();

        if (!_quota.TryReserve(principal, out diagnostic))
        {
            return null;
        }

        QueryTaskEntry entry = new(Guid.NewGuid().ToString("N"), sessionId, task, faults)
        {
            Principal = principal,
        };

        Volatile.Write(ref entry.LastActivityTicks, _time.GetUtcNow().UtcTicks);

        if (!_tasks.TryAdd(entry.TaskId, entry))
        {
            _quota.Release(principal);

            throw new InvalidOperationException(
                "A newly minted query-task handle collided with a live one, so the task was not "
                + "registered. The handle value is deliberately not quoted here.");
        }

        return entry;
    }

    /// <summary>
    /// Resolves a wire handle.
    /// </summary>
    /// <param name="handle">The handle from the request. May be <see langword="null"/> or blank.</param>
    /// <param name="entry">The entry when resolved; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the handle names a live task.</returns>
    /// <remarks>
    /// <para>
    /// A missing, blank, stale, foreign or unknown handle all answer the same way, and the caller turns
    /// that into <c>E_INVALID_HANDLE</c>. Discriminating between them would leak whether a value was
    /// ever valid.
    /// </para>
    /// <para>
    /// <b>FOREIGN IS NOW ACTUALLY ONE OF THOSE OUTCOMES.</b> This remark claimed it before the ownership
    /// comparison existed, and the claim was aspirational: a handle that leaked to another authenticated
    /// caller resolved for that caller, who could then stream another principal's rows. The comparison is
    /// against <see cref="HandlePrincipalResolver.IsCaller"/>, the same identity the quota attributed the
    /// task to.
    /// </para>
    /// <para>
    /// <b>THE STAMP IS NOT REFRESHED FOR A FOREIGN CALLER, AND THE ORDER IS THE REASON THE CHECK SITS
    /// ABOVE IT.</b> Refreshing first would let a caller holding a leaked handle keep another principal's
    /// task alive indefinitely - a resource it cannot use but can prevent the reclaim pass from
    /// collecting.
    /// </para>
    /// </remarks>
    internal bool TryResolve(TaskHandle? handle, out QueryTaskEntry? entry)
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

        if (!_principals.IsCaller(entry.Principal))
        {
            // COLLAPSED INTO THE NOT-FOUND ANSWER, out-parameter and all, so a caller cannot tell a handle
            // it does not own from one that never existed.
            entry = null;

            return false;
        }

        // A HANDLE IN USE IS NOT AN ABANDONED HANDLE: refreshed on every call that names this task.
        Volatile.Write(ref entry.LastActivityTicks, _time.GetUtcNow().UtcTicks);

        return true;
    }

    /// <summary>
    /// Removes a handle from the table, so that a release is single-use.
    /// </summary>
    /// <param name="handle">The handle from the request.</param>
    /// <param name="entry">The removed entry when the handle named one.</param>
    /// <returns><see langword="true"/> when this call is the one that removed it.</returns>
    /// <remarks>
    /// Two concurrent releases of the same handle therefore produce exactly one removal, and the loser
    /// sees the same unknown-handle outcome as any other stale value - which is precisely what the
    /// contract asks for: releasing an already-released handle is <c>E_INVALID_HANDLE</c>, not a silent
    /// success.
    /// </remarks>
    internal bool TryRemove(TaskHandle? handle, out QueryTaskEntry? entry)
    {
        string? taskId = handle?.TaskId;

        if (string.IsNullOrEmpty(taskId))
        {
            entry = null;

            return false;
        }

        // OWNERSHIP IS TESTED BEFORE THE REMOVAL, on the entry still in the table, and a foreign caller
        // therefore removes nothing. This is the release path's own check rather than a reliance on a
        // resolve above it: C-05's release names the handle directly, so there is no earlier lookup for the
        // check to live in, and a foreign release would otherwise dispose a task another caller is using -
        // a denial of service that needs only a leaked handle, no ability to read anything.
        if (_tasks.TryGetValue(taskId, out entry) && !_principals.IsCaller(entry.Principal))
        {
            entry = null;

            return false;
        }

        return TryRemoveUnchecked(taskId, out entry);
    }

    /// <summary>
    /// Removes a handle WITHOUT testing ownership - the maintenance path.
    /// </summary>
    /// <param name="taskId">The identity to remove.</param>
    /// <param name="entry">The removed entry when this call removed one.</param>
    /// <returns><see langword="true"/> when this call is the one that removed it.</returns>
    /// <remarks>
    /// <b>UNCHECKED BY DESIGN, AND THE NAME SAYS SO SO THAT NO CALLER-FACING PATH REACHES IT BY
    /// ACCIDENT.</b> The reclaim pass, the shutdown drain and the session purge all act on the SERVICE's
    /// behalf, not a caller's: they run on a background thread or during host shutdown, where there is no
    /// request and therefore no caller identity to compare against. An ownership test here would resolve
    /// every stored principal against the unattributed sentinel and collect nothing, turning the abandoned
    /// -handle ceiling into a leak. Private, so the boundary is enforced by the compiler.
    /// </remarks>
    private bool TryRemoveUnchecked(string taskId, out QueryTaskEntry? entry)
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
    /// Removes and disposes every task idle for longer than <paramref name="window"/> and not streaming.
    /// </summary>
    /// <param name="now">The reclaim pass's single clock read.</param>
    /// <param name="window">How long a task may go untouched before it is considered abandoned.</param>
    /// <returns>How many tasks were reclaimed.</returns>
    /// <remarks>
    /// <para>
    /// A RETRIEVAL IN FLIGHT IS NEVER RECLAIMED, WHATEVER ITS AGE. <see cref="QueryTaskEntry.IsRunning"/>
    /// is the adapter's own latch, raised for the whole of a streamed retrieval, and a retrieval is the one
    /// operation on this service that can legitimately run for longer than the idle window without any
    /// further call naming its handle. Reclaiming one would dispose a task mid-stream.
    /// </para>
    /// <para>
    /// THE DISPOSAL IS THE RELEASE PATH'S OWN, NOT A SECOND ONE:
    /// <see cref="QueryTaskEntry.Release"/> decides, and only a task that has not already been disposed is
    /// disposed here - which is what makes an abandoned task's teardown identical to a released one's.
    /// </para>
    /// </remarks>
    internal int ReclaimIdle(
        DateTimeOffset now,
        TimeSpan window,
        TimeSpan? uncommittedWorkWindow = null,
        IReadOnlySet<string>? sessionIdsHoldingUncommittedWork = null)
    {
        long threshold = now.UtcTicks - window.Ticks;
        long uncommittedThreshold = uncommittedWorkWindow is { } shorter
            ? now.UtcTicks - shorter.Ticks
            : threshold;

        int reclaimed = 0;

        foreach (QueryTaskEntry candidate in _tasks.Values)
        {
            // WHICH WINDOW APPLIES IS DECIDED BY THE SESSION THIS HANDLE PINS. A handle against a session
            // holding uncommitted work is what keeps that session from being reclaimed, so leaving it in
            // the generic window would defeat the shorter one entirely - the session pass cannot reach a
            // pinned session however dirty it is. A running task is exempt either way: the guard below
            // already skips one, and that is what keeps a legitimately long retrieval safe.
            long applicable =
                sessionIdsHoldingUncommittedWork?.Contains(candidate.SessionId) == true
                    ? uncommittedThreshold
                    : threshold;

            if (candidate.IsRunning
                || Volatile.Read(ref candidate.LastActivityTicks) > applicable
                || !TryRemoveUnchecked(candidate.TaskId, out QueryTaskEntry? removed)
                || removed is null)
            {
                continue;
            }

            if (removed.Release())
            {
                removed.Dispose();
            }

            reclaimed++;

            _logger?.LogWarning(
                "Reclaimed an abandoned query task held by caller {Principal} against session "
                + "{SessionId}. The handle value is deliberately not recorded.",
                LogSafeText.Render(removed.Principal),
                LogSafeText.Render(removed.SessionId));
        }

        return reclaimed;
    }

    /// <summary>
    /// Removes and disposes every live task, whatever its age.
    /// </summary>
    /// <returns>How many tasks were released.</returns>
    /// <remarks>
    /// FOR SHUTDOWN, AND IT MUST RUN BEFORE THE SESSION REGISTRY DRAINS: a task borrows the transaction its
    /// session owns. A running task IS drained here, unlike in the reclaim pass - the host is stopping, so
    /// the alternative is not a completed retrieval but an abandoned worker.
    /// </remarks>
    internal int Drain()
    {
        int drained = 0;

        foreach (QueryTaskEntry candidate in _tasks.Values)
        {
            if (!TryRemoveUnchecked(candidate.TaskId, out QueryTaskEntry? removed) || removed is null)
            {
                continue;
            }

            if (removed.Release())
            {
                removed.Dispose();
            }

            drained++;
        }

        return drained;
    }

    /// <summary>
    /// Removes and disposes every task owned by a retired transaction session.
    /// </summary>
    /// <param name="sessionId">The session that has ended.</param>
    /// <returns>The number of tasks retired.</returns>
    /// <remarks>
    /// <para>
    /// <b>A TASK CANNOT OUTLIVE THE SESSION IT WAS CREATED AGAINST, AND LEAVING ONE BEHIND IS NOT MERELY
    /// UNTIDY.</b> Every task in this table holds the session's pooled transaction, so a task that
    /// survives its session pins a transaction the pool has already handed back - and the pool may hand
    /// that same transaction to a different session the moment its reference count drops. A later call on
    /// the stale handle would then write through another session's transaction. Purging on session end is
    /// what makes the handle's lifetime actually bounded by the session's.
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

        foreach (QueryTaskEntry candidate in _tasks.Values)
        {
            if (!string.Equals(candidate.SessionId, sessionId, StringComparison.Ordinal))
            {
                continue;
            }

            // THROUGH TryRemoveUnchecked, NOT THROUGH THE DICTIONARY, and the difference is a leaked
            // quota. The
            // handle ceiling is reserved on registration and released by whichever call wins the removal
            // [HandleQuota], so a purge that reached past this member would retire the task and leave its
            // slot reserved forever - and the ceiling is per-principal, so the caller whose session ended
            // is exactly the caller that would eventually be refused a new handle it is entitled to.
            if (!TryRemoveUnchecked(candidate.TaskId, out QueryTaskEntry? removed) || removed is null)
            {
                continue;
            }

            // The same two-step release the explicit release path performs: the reference is dropped
            // and disposal happens only when this was the last one, so a run still in flight is not
            // disposed underneath itself.
            if (removed.Release())
            {
                removed.Dispose();
            }

            retired++;
        }

        return retired;
    }
}

// --------------------------------------------------------------------------------------------------
//  PART 4 OF 5 - THE STREAM PROJECTION: FOUR LEGACY CALLBACKS ONTO FIVE WIRE ARMS
// --------------------------------------------------------------------------------------------------

/// <summary>
/// Publishes one retrieval's progressive result onto the C-05 server stream.
/// </summary>
/// <remarks>
/// <para>
/// <b>THE STREAM IS A ONEOF OVER THE LEGACY'S OWN CALLBACKS, WHICH IS WHY IT IS SHAPED THIS WAY
/// (constraint C-K).</b> The legacy does not RETURN a result; it raises a sequence of events on the
/// caller-side proxy object, and that proxy declares them explicitly
/// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlquery.sru</c>]:
/// </para>
/// <code>
///   event onchilddatareceived(string name, ref blob blbdata)                              [:L10]
///   event ondatareceived(long rowcount)                                                   [:L12]
///   event type long ondatachunk(ref blob blbdata, long count, long current, boolean fullstate) [:L13]
///   event onpagereceived(long pagecount, long recordcount)                                [:L14]
/// </code>
/// <para>
/// Each becomes one arm of <c>QueryResponse.payload</c>, so the stream reproduces the legacy's
/// PROGRESSIVE DELIVERY rather than collapsing it into one terminal reply. Two further events -
/// <c>oncreatedata(syntax)</c> [<c>:L11</c>] and <c>ondatamove(datastore)</c> [<c>:L15</c>] - are
/// deliberately NOT projected, because both pass an in-process DataWindow object or exist purely to hand
/// ownership of one to the caller; <c>ondatamove</c>'s body is literally <c>Destroy Data</c>. The DATA
/// those events move is exactly what the chunk arm already carries.
/// </para>
/// <para>
/// ======== FIVE OF THE TEN SINK MEMBERS ARE SYNCHRONOUS, AND THAT DICTATES THE DESIGN ==========
/// </para>
/// <para>
/// <c>OnDataReceived</c>, <c>OnPageReceived</c>, <c>OnDataMove</c>, <c>ReportError</c> and - most
/// consequentially - <c>SendFullStateChunk</c> are <see langword="void"/> or synchronous, because their
/// legacy originals are synchronous events. A gRPC stream write is asynchronous and MUST NOT be blocked
/// on: <c>WriteAsync(...).GetAwaiter().GetResult()</c> inside a synchronous callback is the classic
/// sync-over-async deadlock, and on a server request thread it is exactly the hazard this contract's
/// streaming note warns about.
/// </para>
/// <para>
/// So every emission is APPENDED TO AN ORDERED QUEUE, and the queue is drained at every point the flow
/// is already asynchronous - each chunk and each child hand-over - plus once at the tail. Ordering is
/// therefore exact: the row count that the legacy publishes first [<c>:L83</c>] goes out ahead of the
/// chunks, and the paging totals that it publishes last [<c>:L865</c>] go out behind them, with no
/// message ever overtaking another. At most one full-state image and a handful of small messages are ever
/// buffered, and the legacy materialises that same image whole [<c>:L95</c>].
/// </para>
/// <para>
/// <b>DRAINING IS DETERMINISTIC RELEASE, WHICH IS HAZARD 2 EXPRESSED IN MANAGED TERMS.</b> Each response
/// is DEQUEUED before it is written, so no payload survives the write that consumed it, and
/// <see cref="Discard"/> empties whatever a failed or cancelled retrieval left behind. The legacy's own
/// discipline is the same: the adopter takes the payload and only then does the sender drop its
/// reference [<c>:L87-L89</c>, <c>:L187</c>], and its threading notes require a worker to null a
/// main-thread reference on uninit to release it [<c>docs/PB多线程绕坑提示.md</c>, hazard 2].
/// </para>
/// <para>
/// <b>NO SCOPED RESOURCE IS CAPTURED ANYWHERE IN THIS TYPE.</b> It holds a stream writer, an entry, a
/// token and a queue. There is no DbContext, no connection, no scope and no disposable storage handle,
/// so nothing here can outlive a batch, and the stream's lifetime cannot pin a per-request resource.
/// </para>
/// <para>
/// <b>THE TOKEN IS OBSERVED BEFORE EACH WRITE AND NEVER PASSED TO ONE, AND THAT IS A HARD REQUIREMENT
/// OF THE SERVER STACK.</b> The ASP.NET Core writer implements only the single-argument
/// <c>WriteAsync(T)</c>; a call supplying a token resolves to the default interface method on
/// <c>Grpc.Core.IAsyncStreamWriter&lt;T&gt;</c>, which throws
/// <see cref="NotSupportedException"/> whenever the token can be cancelled - and a call context's token
/// always can. Every write below therefore uses the one-argument form, exactly as the sibling
/// DataServices adapter documents at its own writer.
/// </para>
/// </remarks>
internal sealed class QueryStreamSink : IQueryResultSink, IDisposable
{
    private readonly IServerStreamWriter<QueryResponse> _stream;
    private readonly QueryTaskEntry _entry;
    private readonly CancellationToken _cancellationToken;
    private readonly int _maxMessageBytes;
    private readonly Queue<QueryResponse> _pending = new();
    private readonly Lock _queueGate = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private bool _broken;
    private bool _sendCeilingExceeded;
    private int _sendCeilingBytes;
    private bool _disposed;

    /// <summary>
    /// Creates the sink over one call's response stream.
    /// </summary>
    /// <param name="stream">The server stream writer for this call.</param>
    /// <param name="entry">
    /// The task entry whose caller-side counts and failure record this sink updates as the legacy events
    /// arrive.
    /// </param>
    /// <param name="cancellationToken">
    /// The call's token. Observed before every write and answered as <c>RetCode.CANCELLED</c>, because
    /// cancellation is a return code on this contract and an exception escaping into the codec would be a
    /// regression the legacy could not have had.
    /// </param>
    /// <param name="maxMessageBytes">
    /// The configured gRPC send ceiling in bytes - <c>Ingress:MaxSendMessageBytes</c>, the same value
    /// <c>Program.cs</c> writes onto <c>GrpcServiceOptions.MaxSendMessageSize</c>. A queued response larger
    /// than this is refused HERE rather than being handed to a writer that would reject it, so that the
    /// refusal is a known condition with a name instead of an opaque write fault.
    /// </param>
    /// <exception cref="ArgumentNullException">A required collaborator is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxMessageBytes"/> is not positive.</exception>
    internal QueryStreamSink(
        IServerStreamWriter<QueryResponse> stream,
        QueryTaskEntry entry,
        CancellationToken cancellationToken,
        int maxMessageBytes)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _entry = entry ?? throw new ArgumentNullException(nameof(entry));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxMessageBytes);
        _cancellationToken = cancellationToken;
        _maxMessageBytes = maxMessageBytes;
    }

    /// <summary>
    /// Whether a main-thread receiver is attached - and on the wire the answer is always
    /// <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>TRUE IS THE FAITHFUL ANSWER, NOT A CONVENIENT ONE.</b> This member is one of the three
    /// conditions of the legacy hand-over fast path, which is taken only when the publisher is on the
    /// main thread, no receiver is attached and caching is off
    /// [<c>:L85-L91</c>]. A stream consumer IS a receiver: it has to be POPULATED with data, so the
    /// carrier cannot simply change hands - which is precisely the reason the legacy predicate exists.
    /// Answering <see langword="false"/> would claim there is nobody to serialize for.
    /// </para>
    /// <para>
    /// The affinity condition already forecloses the path from a worker-affine publisher, so this answer
    /// is belt and braces rather than the only guard. Stating it explicitly is what makes
    /// <see cref="OnDataMove"/> unreachable BY CONTRACT instead of by accident.
    /// </para>
    /// </remarks>
    public bool HasReceiver => true;

    /// <summary>
    /// Whether the receiver needs a carrier created from the transferred syntax - and on the wire the
    /// answer is always <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>FALSE, BECAUSE TRUE WOULD DESTROY THE PAYLOAD THE CONTRACT MANDATES.</b> When the legacy
    /// receiver needs an object, the publisher raises <c>oncreatedata(syntax)</c> and then FOLDS THE
    /// FILTER BUFFER INTO THE PRIMARY BUFFER [<c>:L103-L110</c>], under its own note that applying a
    /// changeset to an object built from the current syntax handles filtering automatically. That fold is
    /// observable: it moves Filter! rows into Primary!.
    /// </para>
    /// <para>
    /// The wire payload is a typed <c>CarrierState</c> carrying EXACTLY THREE SEGMENTS - one per buffer,
    /// in canonical order - and the contract requires a receiver to reject a row whose own tag disagrees
    /// with its segment, because the Filter buffer's row order is INVERTED relative to the source
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru:L235-L238</c>]. Folding
    /// would hand the far side Filter! rows inside the Primary segment, which is data corruption that
    /// returns a plausible row count. And the create-data event is one of the two the contract
    /// deliberately does not project, because it passes an in-process object.
    /// </para>
    /// </remarks>
    public bool NeedsCreatedObject => false;

    /// <summary>
    /// Publishes the row count of the whole result.
    /// </summary>
    /// <param name="rowCount">
    /// The legacy <c>ondatareceived(rowcount)</c> argument [<c>:L12</c>], raised at [<c>:L83</c>] FIRST,
    /// unconditionally and before any decision about how the data will travel.
    /// </param>
    /// <remarks>
    /// Recorded on the entry as well as emitted, because the legacy caller keeps it in a field its
    /// <c>of_getrowcount()</c> accessor reads back [<c>n_cst_threading_task_sqlquery.sru:L78</c>], and
    /// because the derivation of the <c>counted</c> flag needs it.
    /// </remarks>
    public void OnDataReceived(long rowCount)
    {
        _entry.RecordRowCount(rowCount);

        Enqueue(new QueryResponse
        {
            RowCount = new QueryRowCount { RowCount = rowCount },
        });
    }

    /// <summary>
    /// Answers the create-data hand-over, which this sink is never asked to perform.
    /// </summary>
    /// <param name="syntax">The carrier syntax the legacy would have transferred. Not projected.</param>
    /// <param name="cancellationToken">Unused: no work is performed.</param>
    /// <returns><c>RetCode.OK</c>.</returns>
    /// <remarks>
    /// <b>UNREACHABLE BY CONTRACT, AND TOTAL ANYWAY.</b> The retrieval consults
    /// <see cref="NeedsCreatedObject"/> before it calls this [<c>:L103</c>], and that answer is a constant
    /// <see langword="false"/>. The member is nevertheless implemented rather than left to throw, because
    /// a sink that throws would convert a future call into an outage; and it answers success because the
    /// legacy event is declared WITHOUT a return type, so its result is not testable there and the
    /// retrieval discards it [<c>:L104</c>].
    /// </remarks>
    public ValueTask<long> CreateDataAsync(string syntax, CancellationToken cancellationToken) =>
        ValueTask.FromResult(RetCode.OK);

    /// <summary>
    /// Publishes one changeset chunk and drains everything queued ahead of it.
    /// </summary>
    /// <param name="chunk">
    /// The chunk the codec produced: its typed carrier state, the TOTAL chunk count and this chunk's
    /// ONE-BASED index. The legacy call is
    /// <c>OnDataChunk(ref blbData, nChunkCnt, nChunkIdx, false)</c> [<c>:L183, :L219</c>], and the
    /// arithmetic behind the total is observable contract:
    /// <c>chunk_count = ceiling((rowCount + filteredCount) / chunkSize)</c> [<c>:L145</c>], which sums
    /// BOTH buffers so a heavily filtered result produces more chunks than its row count alone suggests.
    /// </param>
    /// <param name="cancellationToken">The codec's token.</param>
    /// <returns>
    /// <c>RetCode.OK</c> when written, <c>RetCode.CANCELLED</c> when the call was cancelled, and a
    /// negative code when the write failed - which is what the legacy's <c>&lt; 0</c> sign test at
    /// [<c>:L183</c>] reacts to by reporting TransData Failed and abandoning the sequence.
    /// </returns>
    /// <remarks>
    /// <b>THE ONE-BASEDNESS IS CARRIED THROUGH UNTOUCHED (hazard R9).</b> The first chunk is 1, not 0, and
    /// a zero-based reading would be wrong by one for the whole stream. The values are taken from the
    /// codec's own chunk object, which already validated that the index lies within the count, so nothing
    /// here recomputes either number.
    /// </remarks>
    public async ValueTask<long> SendChunkAsync(
        ChangesetChunk chunk,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(chunk);

        Enqueue(new QueryResponse
        {
            DataChunk = new QueryDataChunk
            {
                // An ABSENT state is the legacy's zero-length blob and tells the receiver to CLEAR what
                // it holds; it is NOT the same statement as a state carrying three empty segments. The
                // empty-result arm produces exactly this, passing (Blob(""), 1, 1, false) at [:L230].
                State = chunk.State,
                ChunkCount = chunk.ChunkCount,
                ChunkIndex = chunk.ChunkIndex,

                // Always false on this path, read from the chunk rather than written as a literal so the
                // two cannot drift.
                FullState = chunk.FullState,
            },
        });

        return await FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Publishes one drop-down child result and drains everything queued ahead of it.
    /// </summary>
    /// <param name="payload">
    /// The child's column name and its typed carrier state. The legacy call is
    /// <c>OnChildDataReceived(sColName, ref blbData)</c> [<c>:L10</c>], raised at [<c>:L134</c>] for each
    /// auto-retrieving drop-down column, and the name is resolved from the column's own Name property
    /// [<c>:L119</c>]. Children are INTERLEAVED with the main chunks, not gathered before or after them.
    /// </param>
    /// <param name="cancellationToken">The codec's token.</param>
    /// <returns>The same three outcomes as <see cref="SendChunkAsync"/>.</returns>
    public async ValueTask<long> SendChildChunkAsync(
        ChangesetChildPayload payload,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(payload);

        Enqueue(new QueryResponse
        {
            ChildDataChunk = new QueryChildDataChunk
            {
                Name = payload.ColumnName,
                State = payload.State,
            },
        });

        return await FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Accepts the single full-state image a crosstab or composite carrier travels as.
    /// </summary>
    /// <param name="chunk">
    /// The chunk the full-state codec built, already carrying <c>chunk_count = 1</c>,
    /// <c>chunk_index = 1</c> and <c>full_state = true</c> - the legacy
    /// <c>OnDataChunk(ref blbData, 1, 1, true)</c> [<c>:L97</c>]. It is the PUBLISHED message type, so no
    /// projection is needed and none is performed: re-projecting it would be an opportunity to change one
    /// of the three values.
    /// </param>
    /// <returns>
    /// <c>RetCode.OK</c>, or a negative code once the stream is known to be broken. The codec applies a
    /// SIGN TEST here rather than an equality test - zero and one are both success [<c>:L97</c>] - so
    /// answering the algebra's zero is correct.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>SYNCHRONOUS BY NECESSITY, SO THE WRITE IS QUEUED RATHER THAN AWAITED.</b> The legacy event is
    /// synchronous and the codec's <c>Send</c> is a synchronous method, so there is no await point to
    /// hang a write on. Blocking on the write would be sync-over-async on a request thread. The image is
    /// therefore queued and goes out at the tail drain, ahead of the paging totals that follow it.
    /// </para>
    /// <para>
    /// <b>THE CONSEQUENCE IS STATED RATHER THAN HIDDEN:</b> a write failure discovered at the tail cannot
    /// retroactively make this hand-over report TransData Failed, so it surfaces as the tail drain's own
    /// failure code on the terminal status instead. Nothing is lost - the failure is still reported with
    /// the same code the legacy uses, <c>E_INTERNAL_ERROR</c> - and it is reported by the one place that
    /// can actually observe it.
    /// </para>
    /// <para>
    /// <b>A THIRD PRESERVED DEFECT TRAVELS WITH THIS PATH (constraint C-B):</b> full-state transfer
    /// crashes when a crosstab has too many columns, and it additionally requires the sort and filter
    /// conditions to be synchronized between the two sides, whereas a changeset does not and is faster
    /// without them. That is why the legacy prefers changesets everywhere it can, and why the discriminator
    /// exists at all. Neither is repaired here.
    /// </para>
    /// </remarks>
    public long SendFullStateChunk(QueryDataChunk chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);

        lock (_queueGate)
        {
            if (_broken)
            {
                return RetCode.E_INTERNAL_ERROR;
            }
        }

        Enqueue(new QueryResponse { DataChunk = chunk });

        return RetCode.OK;
    }

    /// <summary>
    /// Publishes the paging totals, which the legacy raises LAST and on every path.
    /// </summary>
    /// <param name="pageCount">
    /// The legacy <c>pagecount</c> argument, or <c>-1</c> when counting did not happen [<c>:L861</c>].
    /// </param>
    /// <param name="recordCount">
    /// The legacy <c>recordcount</c> argument, or <c>-1</c> when counting did not happen [<c>:L862</c>].
    /// </param>
    /// <remarks>
    /// <b>-1 IS A VALUE, NOT AN ABSENCE (constraint C-B).</b> The notification fires at [<c>:L865</c>]
    /// whether counting happened or not, so a consumer must treat <c>-1</c> as "not counted" and must not
    /// do arithmetic on it. Recorded on the entry as well as emitted, because the legacy caller keeps
    /// both in fields its accessors read back, and because <c>Count</c> answers from exactly those
    /// fields.
    /// </remarks>
    public void OnPageReceived(long pageCount, long recordCount)
    {
        _entry.RecordPageCounts(pageCount, recordCount);

        Enqueue(new QueryResponse
        {
            PageCounts = new QueryPageCounts
            {
                PageCount = pageCount,
                RecordCount = recordCount,
            },
        });
    }

    /// <summary>
    /// Accepts the hand-over of a carrier by reference - which cannot happen on this contract.
    /// </summary>
    /// <param name="carrier">
    /// The carrier the legacy would have handed to a main-thread receiver. Deliberately not read.
    /// </param>
    /// <remarks>
    /// <b>UNREACHABLE, AND IMPLEMENTED AS A NO-OP THAT EMITS NOTHING.</b> The hand-over is taken only when
    /// the publisher is main-thread affine, no receiver is attached and caching is off [<c>:L85-L91</c>],
    /// and <see cref="HasReceiver"/> is a constant <see langword="true"/> here. The legacy event's own
    /// body is <c>Destroy Data</c> [<c>:L268</c>] - it exists to drop the sender's reference once the
    /// adopter has taken it - so doing nothing is also the faithful managed answer: this sink never
    /// adopted anything, and the retrieval's own finally block owns the carrier's release. Emitting a wire
    /// message here would invent an arm the contract does not declare.
    /// </remarks>
    public void OnDataMove(DataWindowCarrier carrier)
    {
    }

    /// <summary>
    /// Records a failure the codec or the retrieval reported.
    /// </summary>
    /// <param name="returnCode">The code, for example <c>E_INTERNAL_ERROR</c> for a failed hand-over.</param>
    /// <param name="errorText">
    /// The diagnostic, verbatim - TransData Failed for a refused chunk [<c>:L184</c>], or
    /// <c>[column] GetChanges Failed</c> for a refused child [<c>:L130</c>].
    /// </param>
    /// <remarks>
    /// Recorded rather than emitted immediately, because the legacy's error event is a SEPARATE channel
    /// from its data events and the wire's terminal status is written once, by the retrieval's caller,
    /// after the outcome code is known. Emitting here as well would put two status arms on one stream.
    /// </remarks>
    public void ReportError(long returnCode, string errorText) =>
        _entry.Faults.OnError(returnCode, errorText);

    /// <summary>
    /// Writes every queued response, in order, and empties the queue as it goes.
    /// </summary>
    /// <param name="cancellationToken">
    /// The token to observe. Checked before each write and never handed to one.
    /// </param>
    /// <returns>
    /// <c>RetCode.OK</c> when the queue drained, <c>RetCode.CANCELLED</c> when cancellation stopped it,
    /// and <c>RetCode.E_INTERNAL_ERROR</c> when a write failed or the stream is already known broken.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The write gate is required by gRPC, which forbids concurrent writes to one response stream. It is
    /// acquired WITHOUT the token: a cancellable wait would throw
    /// <see cref="OperationCanceledException"/> into a codec that expects a return code, and the wait is
    /// uncontended in practice because a retrieval publishes from a single logical flow.
    /// </para>
    /// <para>
    /// A failed write marks the stream BROKEN and every later drain answers immediately without touching
    /// it. That is what turns a vanished peer into the legacy's own outcome - a negative hand-over result,
    /// which the codec reports as TransData Failed and then abandons the sequence [<c>:L183-L185</c>] -
    /// rather than into an exception surfacing from the middle of a chunk loop.
    /// </para>
    /// </remarks>
    internal async Task<long> FlushAsync(CancellationToken cancellationToken)
    {
        await _writeGate.WaitAsync().ConfigureAwait(false);

        try
        {
            while (true)
            {
                if (cancellationToken.IsCancellationRequested || _cancellationToken.IsCancellationRequested)
                {
                    return RetCode.CANCELLED;
                }

                QueryResponse? next = Dequeue();

                if (next is null)
                {
                    return RetCode.OK;
                }

                // 🔴 THE SEND CEILING IS TESTED BEFORE THE WRITE, AND EXCEEDING IT IS NOT A BROKEN STREAM.
                //
                //    A message larger than the configured ceiling NEVER REACHES THE WIRE, so the transport
                //    and the peer are both still perfectly healthy - which is precisely why the previous
                //    behaviour was a wrong answer rather than a reported failure. The writer's own rejection
                //    arrived as an exception, the catch below treated every exception as a vanished peer,
                //    marked the stream broken, and the broken flag then made WriteTerminalStatusAsync
                //    short-circuit. The call therefore ended with gRPC OK carrying ZERO chunks: byte for
                //    byte indistinguishable from a retrieval of an empty table, for a request that had
                //    matched a hundred thousand rows.
                //
                //    Testing here separates the two conditions at the only point that can tell them apart.
                //    The offending message is dropped - it is undeliverable by definition - the condition is
                //    recorded STICKILY for the boundary to report, and the stream is left writable so the
                //    terminal status still goes out. The code returned is E_BUSY, which is the code this
                //    estate already uses for a ceiling met one hop up at the DataServices receive limit and
                //    which maps to ResourceExhausted and then to 429, so both boundaries answer the same
                //    thing for the same condition.
                //
                //    THE CODEC STILL SEES ONLY `< 0` AND STILL REPORTS ITS OWN TransData Failed
                //    [n_cst_thread_task_sqlquery.sru:L183-L185], which is preserved verbatim (constraint
                //    C-B). The ceiling is a PORT-INTRODUCED bound with no legacy counterpart, so its
                //    reporting belongs at the service boundary rather than inside a ported codec, and the
                //    sticky flag is how it gets there without rewriting the codec's oracle-faithful arm.
                int messageBytes = next.CalculateSize();

                if (messageBytes > _maxMessageBytes)
                {
                    RecordSendCeilingExceeded(messageBytes);

                    return RetCode.E_BUSY;
                }

                try
                {
                    // SINGLE-ARGUMENT WriteAsync, deliberately - see the type's remarks for why the
                    // two-argument overload throws on this server stack.
                    await _stream.WriteAsync(next).ConfigureAwait(false);
                }
                catch (RpcException exhausted)
                    when (exhausted.StatusCode == StatusCode.ResourceExhausted)
                {
                    // DEFENSIVE, AND IT COSTS NOTHING TO BE RIGHT TWICE. The pre-flight above uses the
                    // same serialized length the writer measures, so this arm should be unreachable; it
                    // exists because the two accountings are maintained independently - a framing or
                    // header allowance counted on one side and not the other would put a message just over
                    // the writer's edge and just under ours. Classifying it as the ceiling rather than as
                    // a vanished peer keeps the stream writable and the answer truthful either way. The
                    // status detail is NOT read: it is transport text, and this arm already knows the
                    // condition.
                    RecordSendCeilingExceeded(messageBytes);

                    return RetCode.E_BUSY;
                }
                catch (Exception exception)
                    when (exception is not OutOfMemoryException and not StackOverflowException)
                {
                    MarkBroken();

                    // The exception is deliberately NOT rethrown and NOT formatted into the recorded
                    // diagnostic: the codec's sign test is the legacy's own reaction to a refused
                    // hand-over, and a response payload must never reach a log or a message (C-F).
                    _entry.Faults.OnError(RetCode.E_INTERNAL_ERROR, exception.Message);

                    return RetCode.E_INTERNAL_ERROR;
                }
            }
        }
        finally
        {
            _ = _writeGate.Release();
        }
    }

    /// <summary>
    /// Writes the terminal failure detail behind everything already queued, and ends the emission.
    /// </summary>
    /// <param name="status">The outcome, already carrying its diagnostic and any driver payload.</param>
    /// <param name="cancellationToken">Observed, and never handed to the write.</param>
    /// <returns>
    /// <c>RetCode.OK</c> when written, <c>RetCode.CANCELLED</c> when the caller had gone, and
    /// <c>RetCode.E_INTERNAL_ERROR</c> when the stream was already broken or the write failed.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>QUEUED RATHER THAN WRITTEN DIRECTLY, so that ordering survives even here.</b> The legacy's error
    /// events fire MID-RETRIEVE, after chunks may already have been delivered
    /// [<c>:L172, :L184, :L220</c>], and the consumer needs the failure IN BAND with the data it has
    /// already accepted - which only holds if anything still queued goes out AHEAD of the failure rather
    /// than being dropped by it.
    /// </para>
    /// <para>
    /// A broken stream answers immediately without touching the writer: there is nowhere to put a status
    /// whose peer has gone, and the failure has already been recorded on the task's own fault record.
    /// </para>
    /// </remarks>
    internal async Task<long> WriteTerminalStatusAsync(
        OperationStatus status,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(status);

        if (IsBroken)
        {
            return RetCode.E_INTERNAL_ERROR;
        }

        Enqueue(new QueryResponse { Status = status });

        return await FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Whether a write has already failed, so the tail drain can be skipped.
    /// </summary>
    internal bool IsBroken
    {
        get
        {
            lock (_queueGate)
            {
                return _broken;
            }
        }
    }

    /// <summary>
    /// Whether a response this retrieval built exceeded the configured gRPC send ceiling.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>STICKY, AND READ BY THE BOUNDARY RATHER THAN BY THE CODEC.</b> The codec that discovers the
    /// refusal answers the legacy's own <c>E_INTERNAL_ERROR</c> / <c>TransData Failed</c> and the fault
    /// recorder is last-wins, so neither the returned code nor the recorded diagnostic can carry this
    /// condition out. The flag can: it is set once, never cleared, and the handler consults it after the
    /// run to replace the outward code and diagnostic with the defined error while leaving every ported
    /// behaviour on the way there untouched.
    /// </para>
    /// <para>
    /// The stream is deliberately NOT marked broken when this is set, because nothing was ever written -
    /// which is what allows the terminal status to be delivered in band.
    /// </para>
    /// </remarks>
    internal bool SendCeilingExceeded
    {
        get
        {
            lock (_queueGate)
            {
                return _sendCeilingExceeded;
            }
        }
    }

    /// <summary>
    /// The size in bytes of the largest response that exceeded the ceiling, or zero when none did.
    /// </summary>
    /// <remarks>
    /// FOR THE OPERATOR'S RECORD ONLY, AND NEVER FOR THE CALLER'S DIAGNOSTIC. A byte count carries no row
    /// value and no statement text, so it is safe to log; publishing it to a caller alongside the ceiling
    /// would nonetheless disclose how close an arbitrary request came to a declared bound, which is the
    /// posture this service takes everywhere else it refuses on capacity.
    /// </remarks>
    internal int SendCeilingBytes
    {
        get
        {
            lock (_queueGate)
            {
                return _sendCeilingBytes;
            }
        }
    }

    /// <summary>The ceiling this sink enforces, in bytes.</summary>
    internal int MaxMessageBytes => _maxMessageBytes;

    /// <summary>
    /// Drops every queued payload without writing it.
    /// </summary>
    /// <remarks>
    /// Called from the retrieval's finally block. This is the deterministic release hazard 2 asks for: a
    /// cancelled or failed retrieval must not leave carrier state reachable from a queue that nobody will
    /// drain [<c>docs/PB多线程绕坑提示.md</c>, hazard 2].
    /// </remarks>
    internal void Discard()
    {
        lock (_queueGate)
        {
            _pending.Clear();
        }
    }

    /// <summary>
    /// Releases the write gate and drops any remaining payload. Idempotent.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        Discard();
        _writeGate.Dispose();
    }

    private void Enqueue(QueryResponse response)
    {
        lock (_queueGate)
        {
            _pending.Enqueue(response);
        }
    }

    private QueryResponse? Dequeue()
    {
        lock (_queueGate)
        {
            return _pending.TryDequeue(out QueryResponse? response) ? response : null;
        }
    }

    private void MarkBroken()
    {
        lock (_queueGate)
        {
            _broken = true;
            _pending.Clear();
        }
    }

    /// <summary>
    /// Records that a response exceeded the ceiling, keeping the largest offending size seen.
    /// </summary>
    /// <param name="messageBytes">The serialized size of the refused response.</param>
    /// <remarks>
    /// THE QUEUE IS CLEARED, THE BROKEN FLAG IS NOT SET. Everything still queued behind an undeliverable
    /// chunk belongs to the same over-sized sequence and would be refused for the same reason, so draining
    /// it would only repeat the measurement; and the retrieval is abandoned from here anyway, because the
    /// codec's sign test reacts to the returned code. Leaving the stream writable is the whole point - the
    /// terminal status is a few dozen bytes and must still reach the caller.
    /// </remarks>
    private void RecordSendCeilingExceeded(int messageBytes)
    {
        lock (_queueGate)
        {
            _sendCeilingExceeded = true;

            if (messageBytes > _sendCeilingBytes)
            {
                _sendCeilingBytes = messageBytes;
            }

            _pending.Clear();
        }
    }
}

// --------------------------------------------------------------------------------------------------
//  PART 5 OF 5 - THE ADAPTER
// --------------------------------------------------------------------------------------------------

/// <summary>
/// The outcome of applying one setting, carrying the code the collaborator answered and - only for a
/// refusal this boundary owns rather than inherits - the diagnostic that explains it.
/// </summary>
/// <param name="Code">The in-process return code.</param>
/// <param name="ErrorText">
/// The diagnostic, or <see langword="null"/> when the refusal came from a delegated legacy guard.
/// </param>
/// <remarks>
/// <b>THE NULL IS THE POINT, AND IT IS FIDELITY RATHER THAN LAZINESS (constraint C-B).</b> The legacy
/// setter guards answer a bare code and no message whatever - <c>if chunkSize &lt;= 1000 then return
/// RetCode.E_INVALID_ARGUMENT</c> [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L410</c>]
/// is the entire body, and the two clause setters are the same shape
/// [<c>:L271</c>, <c>:L288</c>]. Inventing text for those refusals would put a diagnostic on the wire that
/// the legacy never produced. Text is therefore carried only for the three refusals that have no legacy
/// counterpart because the legacy had no listener to refuse anything: an absent clause, an out-of-domain
/// modification style, and a parameter with no readable value.
/// </remarks>
internal readonly record struct QuerySettingOutcome(long Code, string? ErrorText)
{
    /// <summary>The outcome of a setting that was accepted.</summary>
    internal static QuerySettingOutcome Accepted { get; } = new(RetCode.OK, null);

    /// <summary>Whether the setting was accepted.</summary>
    /// <remarks>
    /// An EXACT comparison against <c>OK</c> and deliberately not the tri-state success predicate. Every
    /// setter this wraps answers either <c>OK</c> or <c>E_INVALID_ARGUMENT</c>, so widening the test to
    /// "not failed" would accept a hypothetical <c>PREVENT</c> as a successful configuration - and a
    /// prevention satisfies that predicate, which is exactly the tri-state hole the algebra carries.
    /// </remarks>
    internal bool IsAccepted => Code == RetCode.OK;

    /// <summary>Wraps a delegated setter's bare code, with no diagnostic of this boundary's own.</summary>
    /// <param name="code">The code the setter answered.</param>
    /// <returns>The outcome.</returns>
    internal static QuerySettingOutcome From(long code) => new(code, null);

    /// <summary>Builds a refusal this boundary owns.</summary>
    /// <param name="code">The code to answer.</param>
    /// <param name="errorText">The diagnostic. Never echoes a rejected value (constraint C-F).</param>
    /// <returns>The outcome.</returns>
    internal static QuerySettingOutcome Refused(long code, string? errorText) => new(code, errorText);

    /// <summary>Projects the outcome onto the wire.</summary>
    /// <returns>
    /// A status carrying the code, and the diagnostic only when this outcome has one. Never a driver
    /// payload: no setting refusal in this contract would have fired the legacy's database-error event.
    /// </returns>
    internal OperationStatus ToStatus() =>
        ErrorText is null
            ? QueryWireCodes.Status(Code)
            : QueryWireCodes.Status(Code, ErrorText);
}

/// <summary>
/// Serves contract C-05, <c>persistence.v1.QueryService</c>: the task lifecycle, the eighteen
/// per-task settings, the streaming retrieval and the paging totals.
/// </summary>
/// <remarks>
/// <para>
/// <b>EVERY METHOD IS AUTHORIZED, AND THE ENFORCEMENT IS DELIBERATELY NOT HERE (constraint C-G).</b>
/// <c>Program.cs</c> installs a fallback authorization policy requiring an authenticated principal, so
/// an endpoint with no authorization metadata of its own is a CLOSED door rather than an open one. This
/// class therefore carries no <c>[Authorize]</c> attribute - it would be redundant - and, more to the
/// point, it carries NO <c>[AllowAnonymous]</c> and no per-method escape of any kind. Inbound tokens are
/// validated by the stock bearer handler against Security's published key set; this service holds
/// VERIFICATION MATERIAL ONLY and no signing key appears anywhere in this file.
/// </para>
/// <para>
/// <b>EVERY COLLABORATOR ARRIVES BY CONSTRUCTOR INJECTION (constraint C-H).</b> Nothing is reached
/// statically, nothing is newed up internally except the per-call sink, no clock is read and no
/// configuration is bound. That is what keeps the per-service coverage gate reachable through
/// substitution: a test drives the whole surface with a factory and a runner of its own.
/// </para>
/// <para>
/// <b>NO WALL CLOCK IS READ IN THIS FILE.</b> There is no <c>DateTime.Now</c>, no
/// <c>Environment.TickCount</c> and no <c>Stopwatch</c>. The retrieval's own clock reads - the
/// twenty-millisecond cooperative inter-chunk yield and the pool's idle expiry - live behind the single
/// injected <see cref="TimeProvider"/> seam the task and the pool share. Adding a read here would put
/// one clock outside that seam and silently break paired characterization recordings.
/// </para>
/// <para>
/// <b>ELEVEN RPCs COVER EIGHTEEN LEGACY SETTERS, AND THE ARITHMETIC IS DELIBERATE - NOTHING IS
/// MISSING.</b> Four settings get a dedicated call because a caller adjusts them between runs: the chunk
/// size, the row cap, the two clause collections, the five paging values as one group, and the
/// unique-index column list. The remaining TEN - cache, data object, SQL syntax, SQL, hook class, filter,
/// sort, paged, page native and page counting - are CONFIGURATION rather than operations, so they arrive
/// as fields of <c>QuerySpec</c> on the create and query calls, applied by <see cref="ApplySpec"/> in the
/// contract's own field order with each setter's own guard delegated. One model, two access paths to the
/// same state - not two models. A reader looking for <c>SetSql</c> as an RPC will not find one, and that
/// is the contract's shape rather than an omission.
/// </para>
/// <para>
/// <b>THE DIAGNOSTICS THIS FILE WRITES ITSELF ARE ITS OWN, AND THE DELEGATED ONES CARRY NO TEXT.</b> The
/// legacy setter guards answer a bare code and no message - <c>if chunkSize &lt;= 1000 then return
/// RetCode.E_INVALID_ARGUMENT</c> [<c>:L410</c>] is the whole body - so a refusal that came from a
/// collaborator is reported with the bare code, exactly as the legacy reports it. Text is added only for
/// the refusals that have NO legacy counterpart because the legacy had no listener to refuse anything:
/// an unknown handle, an unknown session and an out-of-domain modification style. None of that text ever
/// echoes a rejected value (constraint C-F).
/// </para>
/// </remarks>
// ============ THE SCOPE THIS CONTRACT REQUIRES, AND HOW IT IS KEPT (constraint C-G) ============
// C-05 is the RETRIEVAL contract: the legacy task it ports generates SELECT statements and nothing
// else [ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru], so every RPC here - the
// task lifecycle and the setters included - needs the READ scope and no more. The task handle and
// its configuration are per-caller server state, not stored data.
//
// ⚠ BUT "THE TASK GENERATES ONLY SELECT" IS A STATEMENT ABOUT THE LEGACY TASK, NOT A GUARANTEE
// ABOUT THIS SURFACE, AND READING IT AS ONE WAS A REAL DEFECT. Three of this contract's inputs are
// caller-authored SQL rather than task-generated: QuerySpec.sql, QuerySpec.sql_syntax (whose
// `retrieve="..."` clause is what executes) and SqlClauseSpec.clause. The oracle's setters guard
// none of them, because a library has no caller whose rights are narrower than the process's - and
// across this boundary there is one. Left ungated, a credential minted for `persistence.read`
// reached INSERT, DELETE, DDL, PRAGMA, ATTACH and, because the provider executes every statement in
// a batch it is handed, whole batches spliced behind a semicolon. So the scope is ENFORCED
// rather than assumed, at two named places:
//
//    * Sql/ReadOnlyStatementGuard.cs gates QuerySpec.sql and QuerySpec.sql_syntax, admitting ONE
//      statement that begins with SELECT, WITH or VALUES and refusing everything named above;
//    * Sql/ClauseModifier.cs gates SqlClauseSpec.clause with its own, narrower, fragment grammar.
//
// Both REFUSE rather than rewrite - byte-exact statement parity is the acceptance criterion - and
// both answer a code this contract already had. Neither moves the oracle's own run-time emptiness
// and malformedness checks forward to configuration time (constraint C-B). The published grammar
// lives on persistence.v1.QuerySpec.sql, .sql_syntax and SqlClauseSpec.clause, and the two guards
// are the implementation of that text; the three must not drift.
//
// The route mapping in Program.cs additionally requires an authenticated principal, and the
// fallback policy would close the door even if the mapping forgot to. This attribute is the
// LEAST-PRIVILEGE half: authentication alone would let a credential minted for one contract
// reach all four.
[Authorize(Policy = PersistenceAuthorizationPolicies.Read)]
internal sealed class QueryService : GeneratedQueryServiceBase
{
    /// <summary>
    /// Answered when a request names a session that C-08 never issued, or that has already ended.
    /// </summary>
    /// <remarks>
    /// Matches the code the legacy returns when it is asked to act on a transaction it cannot use
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:L113</c>]. The value is not quoted,
    /// so the text cannot leak whether a handle was ever valid.
    /// </remarks>
    private const string UnknownSessionText =
        "The request named a transaction session this instance does not hold, so no query task was "
        + "created. Begin a session first; a session handle is not portable across instances. The "
        + "handle value is deliberately not quoted here.";

    /// <summary>
    /// Answered when the session a task belongs to began retiring before the retrieval reached the
    /// transaction.
    /// </summary>
    /// <remarks>
    /// A DISTINCT MESSAGE FROM THE UNKNOWN-SESSION ONE, deliberately: the handle WAS live when the caller
    /// sent it, so telling it the session was never known would send it looking for a mistake it did not
    /// make. The code is the one the legacy answers for a transaction it cannot use
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:L113</c>], and no handle value is
    /// quoted (constraint C-F).
    /// </remarks>
    private const string SessionClosingText =
        "The transaction session this task belongs to was ending as the retrieval reached the transaction, "
        + "so no statement was issued. Open a new session and retry; nothing was retrieved and no state "
        + "was changed.";

    /// <summary>
    /// Answered when the session a task was being created against began retiring before the task could be
    /// published.
    /// </summary>
    /// <remarks>
    /// A THIRD MESSAGE RATHER THAN A REUSE OF EITHER OF THE TWO ABOVE, because the caller's position is
    /// different again: its session handle was live when it sent the request, and no task handle was ever
    /// issued, so there is nothing for it to release and nothing to retry with except a NEW session. The
    /// code is <c>E_INVALID_TRANSACTION</c> - the one the legacy answers for a transaction it cannot use
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:L113</c>] - and no handle value is
    /// quoted (constraint C-F).
    /// </remarks>
    private const string SessionClosingOnCreateText =
        "The transaction session named by this request began retiring before the query task could be "
        + "published against it, so no task was created and no handle was issued. Open a new session and "
        + "retry.";

    /// <summary>
    /// Answered when a caller-supplied statement is not something a read-scoped caller may ask this
    /// contract to run.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>IT NAMES THE RULE AND NEVER THE REJECTED TEXT (constraint C-F).</b> Echoing the statement back
    /// would put a caller-authored value - and, on a statement that was already parameter-bound, an
    /// interpolated literal - into a field documented as opaque display text, which is exactly what
    /// <c>Errors/SqlRedactor.cs</c> exists to prevent. Naming the rule instead tells a caller what to
    /// change without quoting anything, and it is a fixed sentence, so it passes through the redactor
    /// byte for byte.
    /// </para>
    /// <para>
    /// It is DELIBERATELY not phrased as an authorization failure. The caller's credential is valid and
    /// its scope is the one this contract requires; what it asked for is outside that scope's capability.
    /// <c>E_INVALID_SQL</c> - the code this contract already answers for a statement it will not run
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L612, :L616, :L620</c>] - is
    /// therefore the honest answer, and it introduces no new value into a consumer's branch set.
    /// </para>
    /// </remarks>
    private const string InadmissibleStatementText =
        "The supplied statement is not admissible on this contract. C-05 is published under the read "
        + "scope, so it accepts ONE statement beginning with SELECT, WITH or VALUES, and refuses data "
        + "manipulation, data definition, PRAGMA, ATTACH, DETACH, transaction control, procedure "
        + "execution, extension loading, SQL comments and any second statement after a semicolon. Send "
        + "state-changing statements to the write-scoped C-07 command contract instead. The rejected "
        + "text is deliberately not quoted here.";

    /// <summary>
    /// Answered when a caller-supplied DataWindow syntax declares a retrieval a read-scoped caller may
    /// not ask this contract to run.
    /// </summary>
    /// <remarks>
    /// A DISTINCT MESSAGE FROM <see cref="InadmissibleStatementText"/>, because the caller sent a
    /// different field and the offending text is nested inside it: telling it that "the statement" was
    /// refused when it supplied a syntax blob would send it looking at the wrong request field.
    /// </remarks>
    private const string InadmissibleSyntaxText =
        "The retrieval statement declared by the supplied DataWindow syntax is not admissible on this "
        + "contract. C-05 is published under the read scope, so the syntax's retrieve clause must carry "
        + "ONE statement beginning with SELECT, WITH or VALUES, and may not carry data manipulation, "
        + "data definition, PRAGMA, ATTACH, DETACH, transaction control, procedure execution, extension "
        + "loading, SQL comments or any second statement after a semicolon. The rejected text is "
        + "deliberately not quoted here.";

    /// <summary>Answered when a request names a task handle this instance does not hold.</summary>
    private const string UnknownTaskText =
        "The request named a query task this instance does not hold, so nothing was done. A task handle "
        + "is server-issued, single-use on release, and not portable across instances. The handle value "
        + "is deliberately not quoted here.";

    /// <summary>Answered when a mutation arrives while a retrieval is in flight for the same task.</summary>
    /// <remarks>
    /// The legacy guards every mutator with <c>if of_IsBusy() then return RetCode.E_BUSY</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlbase.sru:L57, :L73, :L95, :L110,
    /// :L124, :L144, :L162, :L179, :L188</c>], and the worker's reset repeats it against its own running
    /// flag [<c>:L237</c>]. It is a DISTINCT outcome from a failure: retrying the same call unchanged
    /// succeeds once the retrieval finishes, and flattening the two would remove that distinction.
    /// </remarks>
    private const string TaskBusyText =
        "A retrieval is in flight for this query task, so the request was refused. Retry once the "
        + "stream has completed.";

    /// <summary>
    /// Answered when a response this retrieval built was larger than the configured gRPC send ceiling.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>THE CONDITION THIS REPLACES WAS A WRONG ANSWER, NOT AN UNREPORTED FAILURE (issue MAJ-2).</b> A
    /// chunk size the caller is free to choose determines how many rows go into ONE response message, and a
    /// message over the ceiling cannot be written - so the retrieval had to end. What it used to end as was
    /// gRPC <c>OK</c> carrying zero chunks, which is byte for byte the shape of a retrieval against an
    /// empty table: measured on a hundred-thousand-row table, a chunk size of 100000 answered
    /// <c>rows: [], rowCount: 0, final: true</c> through the REST projection with HTTP 200. This message
    /// is what a caller now receives instead, and its code is <c>E_BUSY</c>, which the projection maps to
    /// <c>ResourceExhausted</c> and then to HTTP 429 - the same answer the DataServices receive ceiling
    /// already gives one hop up, so the two boundaries agree.
    /// </para>
    /// <para>
    /// <b>IT NAMES THE SETTING AND THE REMEDY BUT QUOTES NO NUMBER.</b> The ceiling and the offending
    /// size both go to the operator's log, where they identify the cause; publishing them here would tell
    /// an unauthenticated-in-effect caller how close an arbitrary request came to a declared bound, which
    /// is the posture every other capacity refusal on this contract takes. The remedy does not need the
    /// numbers: a smaller chunk size produces smaller messages monotonically, so retrying downward
    /// converges without knowing where the edge is.
    /// </para>
    /// <para>
    /// <b>NO ROWS WERE DELIVERED IS STATED EXPLICITLY, because it is the one thing a caller cannot infer.
    /// </b> The refusal can land on the FIRST chunk or on the fifth, and a caller that has already
    /// accepted four must know that what it holds is partial. The sentence covers both by describing the
    /// retrieval as incomplete rather than claiming a count.
    /// </para>
    /// </remarks>
    private const string SendCeilingExceededText =
        "The retrieval was abandoned because a single response message built from the requested chunk size "
        + "exceeded this service's configured gRPC send ceiling (Ingress:MaxSendMessageBytes). The "
        + "retrieval is INCOMPLETE and whatever chunks preceded this status are a partial result that must "
        + "not be read as a whole one. Retrieve again with a smaller SetChunkSize so that each chunk fits "
        + "the ceiling, or raise the ceiling in this service's configuration. The ceiling and the size of "
        + "the refused message are recorded in this service's own log and are deliberately not quoted "
        + "here.";

    /// <summary>Answered when a clause carries a modification style outside the published domain.</summary>
    /// <remarks>
    /// A proto3 enum field is OPEN: an unrecognised number is neither rejected by the parser nor folded
    /// to zero, so a request carrying 99, or -1, or the unspecified zero arrives intact. The legacy
    /// offers no validation to copy - it dispatches on the style with no default arm and an unrecognised
    /// value silently modifies nothing - so the refusal is DEFINED here rather than inherited, which
    /// narrows the contract with an error instead of widening it with a guess.
    /// </remarks>
    private const string InvalidModifyStyleText =
        "The clause modification style is outside the published domain, so no clause text was applied. "
        + "Send replace, append or prepend; the unspecified value is a protocol artifact and is never "
        + "treated as replace.";

    /// <summary>Answered when a clause setter arrives with no clause at all.</summary>
    private const string MissingClauseText =
        "The request carried no clause, so nothing was applied.";

    /// <summary>Answered when a positional parameter carries no readable value.</summary>
    /// <remarks>
    /// An unset value arm means "no value supplied", which the contract states is NOT the same as an
    /// explicit null - and a value outside the published grammar is refused rather than coerced. The
    /// text names neither the parameter nor its value (constraint C-F).
    /// </remarks>
    private const string InvalidParameterText =
        "A positional parameter carried no readable value, so no parameter was bound. Send a typed "
        + "value, or the explicit null arm for a null. Neither the parameter name nor its value is "
        + "quoted here.";

    private readonly TransactionSessionRegistry _sessions;
    private readonly QueryTaskRegistry _tasks;
    private readonly IQueryTaskFactory _taskFactory;
    private readonly IQueryRetrievalRunner _runner;
    private readonly int _maxSendMessageBytes;
    private readonly ILogger<QueryService>? _logger;

    /// <summary>
    /// Creates the adapter over its four collaborators.
    /// </summary>
    /// <param name="sessions">
    /// The session table C-08's <c>BeginSession</c> populates. REUSED rather than duplicated: the legacy
    /// hands each task a transaction DESCRIPTOR BY VALUE
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru:L113</c>], and resolving the
    /// descriptor once through a session rather than transmitting it - LOG PASSWORD INCLUDED - on every
    /// query is what keeps credential material off this contract entirely (constraint C-F). Declaring a
    /// second session table here would give the container two registrations to keep in step and create
    /// two places for a handle to be valid.
    /// </param>
    /// <param name="tasks">The task table. Must be the process singleton.</param>
    /// <param name="taskFactory">The seam that composes a worker task. See its own remarks.</param>
    /// <param name="runner">The seam that executes a retrieval. See its own remarks.</param>
    /// <param name="logger">
    /// Optional structured logger. Optional rather than required so a unit test can construct the
    /// service with nothing but its behavioural collaborators.
    /// </param>
    /// <param name="ingress">
    /// The bound ingress group, read for ONE value: the gRPC send ceiling this adapter's streams must
    /// respect. Optional for the same reason the logger is, and it falls back to the group's own declared
    /// default rather than to a literal, so the ceiling has exactly one authority whether it was injected
    /// or not.
    /// </param>
    /// <exception cref="ArgumentNullException">A required collaborator is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// <b>THE SEND CEILING IS READ HERE BECAUSE A STREAM HAS TO KNOW IT (issue MAJ-2).</b> The value is
    /// written onto <c>GrpcServiceOptions.MaxSendMessageSize</c> in <c>Program.cs</c> from this same
    /// group, so the writer would reject an over-sized response anyway - but it rejects it as an
    /// EXCEPTION, indistinguishable at the catch site from a peer that has gone, and a peer that has gone
    /// leaves nowhere to write a terminal status. The sink therefore measures each response against the
    /// ceiling itself and refuses it as a NAMED condition, which is what lets the failure be reported in
    /// band instead of ending the call with a successful-looking empty stream.
    /// </para>
    /// <b>NO <c>ISqlRedactor</c> AND NO <c>IOptions&lt;PersistenceOptions&gt;</c> ARE INJECTED, AND BOTH
    /// OMISSIONS ARE DELIBERATE.</b> The redaction of the statement field is reached directly by the
    /// sanctioned projection in <c>Errors/SqlRedactor.cs</c> precisely so it cannot be weakened from a
    /// call site or a registration, and an injected policy is a replaceable policy (constraint C-F). The
    /// configured query defaults - the 10000 chunk size, the true page-counting flag and the rest - are
    /// applied by <c>Tasks/SqlQueryTask</c>'s own constructor from that same options type, so binding
    /// them a second time here would give one setting two sources; an unused injection is an unused
    /// coupling.
    /// </remarks>
    public QueryService(
        TransactionSessionRegistry sessions,
        QueryTaskRegistry tasks,
        IQueryTaskFactory taskFactory,
        IQueryRetrievalRunner runner,
        ILogger<QueryService>? logger = null,
        IOptions<IngressOptions>? ingress = null)
    {
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _tasks = tasks ?? throw new ArgumentNullException(nameof(tasks));
        _taskFactory = taskFactory ?? throw new ArgumentNullException(nameof(taskFactory));
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _logger = logger;

        // The group's own default, not a literal: an uninjected ceiling is the SAME number a configured
        // one defaults to, so a test and a deployment enforce the same bound.
        _maxSendMessageBytes = (ingress?.Value ?? new IngressOptions()).MaxSendMessageBytes;
    }

    // ----------------------------------------------------------------------------------------------
    //  THE TASK LIFECYCLE - THE WIRE FORM OF CREATE, DESTROY AND RESET
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Creates a server-held query task bound to a session, optionally applying an initial
    /// configuration.
    /// </summary>
    /// <param name="request">The session handle and the optional specification.</param>
    /// <param name="context">The call context. Not read: this call performs no I/O.</param>
    /// <returns>
    /// The handle when the task was created, and the offending setting's own code when the
    /// specification was rejected.
    /// </returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// Three steps, each observable, in this order. The session is resolved first, because a task with no
    /// transaction cannot run at all. The descriptor is then bound through the task's own
    /// <c>SetTransData</c>, which is the port of <c>of_settransdata</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru:L113</c>] and which erases the
    /// descriptor's auto-commit flag on the way in. Only then is the specification merged.
    /// </para>
    /// <para>
    /// <b>A REJECTED SETTING FAILS THE WHOLE CALL WITH THAT SETTING'S OWN CODE, and the task is not
    /// registered.</b> That is the property the fat-stateless-call alternative could not provide: the
    /// legacy returns <c>E_INVALID_ARGUMENT</c> from the INDIVIDUAL setter, so a caller learns WHICH
    /// setting was wrong. The half-configured task is disposed on the spot rather than leaked, which is
    /// hazard 2's deterministic release applied to a failure path.
    /// </para>
    /// <para>
    /// <b>PUBLICATION IS ATOMIC WITH THE SESSION'S LIVENESS, AND ONLY PUBLICATION IS.</b> Resolving the
    /// session, building the task and merging the specification all touch nothing but the unpublished task
    /// itself, so they run outside the session's lifecycle gate. The step that MUST be atomic is the one
    /// that makes the task reachable: <c>EndSession</c> marks its session closing inside that gate and only
    /// then walks the registries, so a registration performed outside the gate could land after the walk
    /// and leave a task holding a transaction the pool has already taken back. Testing
    /// <c>IsClosing</c> and registering inside one acquisition removes that interleaving - either the
    /// registration precedes the mark and the walk finds it, or it reaches the gate after the mark and is
    /// refused.
    /// </para>
    /// <para>
    /// The gate is AWAITED rather than blocked on, because a streaming retrieval on the same transaction
    /// holds it for the whole of its stream and a blocking acquisition would pin a request thread for that
    /// duration. Nothing observable changes.
    /// </para>
    /// </remarks>
    public override async Task<CreateQueryTaskResponse> CreateQueryTask(
        CreateQueryTaskRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        if (!_sessions.TryResolve(request.Session, out TransactionSession? session) || session is null)
        {
            _logger?.LogWarning(
                "A query task could not be created because the named transaction session is not held by "
                + "this instance.");

            return new CreateQueryTaskResponse
            {
                Status = QueryWireCodes.Status(RetCode.E_INVALID_TRANSACTION, UnknownSessionText),
            };
        }

        QueryFaultRecorder faults = new();
        SqlQueryTask task = _taskFactory.Create(faults);
        bool registered = false;

        try
        {
            // The descriptor BY VALUE, exactly as the legacy hands it over
            // [ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru:L113]. Its whole-structure
            // equality check answers OK for a descriptor it already holds, so re-binding the same session
            // is free, and it erases the auto-commit flag on the way in.
            //
            // THIS LOCAL CARRIES THE CREDENTIAL AND IS NEVER INTERPOLATED (constraint C-F). It is passed
            // straight into the task and read nowhere else: it reaches no log record, no exception message
            // and no response. The type also overrides ToString and PrintMembers so that its log-password,
            // connection-parameter and user-parameter fields have no printing path at all - which is what
            // makes a stray `$"... {descriptor}"` impossible rather than merely discouraged.
            TransactionData descriptor = session.Descriptor;

            long bound = task.SetTransData(descriptor);

            if (bound != RetCode.OK)
            {
                return new CreateQueryTaskResponse
                {
                    Status = QueryWireCodes.Status(bound),
                };
            }

            QuerySettingOutcome applied = ApplySpec(task, request.Spec);

            if (!applied.IsAccepted)
            {
                return new CreateQueryTaskResponse
                {
                    Status = applied.ToStatus(),
                };
            }

            QueryTaskEntry? entry;
            string quotaDiagnostic;

            // ------------------------------------------------------------------------------------------
            //  THE PUBLICATION, AND THE ONLY PART OF THIS HANDLER THAT IS GATED. See the remarks above for
            //  why the liveness test and the registration have to be one step rather than two.
            // ------------------------------------------------------------------------------------------
            using (await session.Gate.EnterAsync(context.CancellationToken).ConfigureAwait(false))
            {
                if (session.IsClosing)
                {
                    _logger?.LogWarning(
                        "A query task was not published because its transaction session began retiring "
                        + "first. The handle value is deliberately not recorded.");

                    return new CreateQueryTaskResponse
                    {
                        Status = QueryWireCodes.Status(
                            RetCode.E_INVALID_TRANSACTION,
                            SessionClosingOnCreateText),
                    };
                }

                entry = _tasks.Register(
                    session.SessionId,
                    task,
                    faults,
                    out quotaDiagnostic);
            }

            if (entry is null)
            {
                // A CEILING REFUSAL LEAVES `registered` FALSE, so the finally block below disposes the task
                // this call built - the same deterministic drop every other refusal arm gets. E_BUSY is the
                // oracle's own "not now", so no new value enters a consumer's branch set.
                _logger?.LogWarning(
                    "CreateQueryTask refused a task because a handle ceiling was reached: {Diagnostic}",
                    quotaDiagnostic);

                return new CreateQueryTaskResponse
                {
                    Status = QueryWireCodes.Status(RetCode.E_BUSY, quotaDiagnostic),
                };
            }

            registered = true;

            _logger?.LogDebug(
                "Created a query task against session {SessionId}.",
                LogSafeText.Render(entry.SessionId));

            return new CreateQueryTaskResponse
            {
                Status = QueryWireCodes.Status(RetCode.OK),
                Task = new TaskHandle { TaskId = entry.TaskId },
            };
        }
        finally
        {
            if (!registered)
            {
                // Nothing else can reach it, so it is released here and now rather than left for a
                // collection cycle - the deterministic drop hazard 2 asks for.
                task.Dispose();
            }
        }
    }

    /// <summary>
    /// Destroys a server-held query task and releases its carrier.
    /// </summary>
    /// <param name="request">The handle to release.</param>
    /// <param name="context">The call context. Not read.</param>
    /// <returns><c>OK</c> when this call released it, and <c>E_INVALID_HANDLE</c> otherwise.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// <b>RELEASING AN ALREADY-RELEASED HANDLE IS <c>E_INVALID_HANDLE</c>, NOT A SILENT SUCCESS</b>, which
    /// is the contract's own rule and the reason the removal is what decides the outcome: two concurrent
    /// releases produce exactly one removal and the loser is told the handle is unknown.
    /// </para>
    /// <para>
    /// <b>THERE IS NO BUSY GUARD HERE, DELIBERATELY (constraint C-B).</b> The legacy's nine busy guards sit
    /// on MUTATORS, and destruction is not one of them - <c>Destroy</c> is unguarded. Refusing a release
    /// while a retrieval is in flight would invent a rejection the legacy does not have, and it would also
    /// let a caller that has abandoned a stream keep a task alive indefinitely. Instead the handle goes
    /// immediately and the DISPOSAL is deferred to whichever of the two paths finishes last, so nothing is
    /// torn out from under its own stream and no use-after-dispose is reachable.
    /// </para>
    /// </remarks>
    public override Task<ReleaseQueryTaskResponse> ReleaseQueryTask(
        ReleaseQueryTaskRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        if (!_tasks.TryRemove(request.Task, out QueryTaskEntry? entry))
        {
            return Task.FromResult(new ReleaseQueryTaskResponse
            {
                Status = QueryWireCodes.Status(RetCode.E_INVALID_HANDLE, UnknownTaskText),
            });
        }

        if (entry!.Release())
        {
            entry.Dispose();
        }

        return Task.FromResult(new ReleaseQueryTaskResponse
        {
            Status = QueryWireCodes.Status(RetCode.OK),
        });
    }

    /// <summary>
    /// Resets a task to its configured defaults.
    /// </summary>
    /// <param name="request">The handle to reset.</param>
    /// <param name="context">The call context. Not read.</param>
    /// <returns><c>OK</c>, <c>E_BUSY</c> while a retrieval is in flight, or <c>E_INVALID_HANDLE</c>.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// The shape is the caller-side proxy's own reset, step for step
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlbase.sru:L57-L67</c>] with the query
    /// proxy's additions [<c>n_cst_threading_task_sqlquery.sru:L283-L294</c>]:
    /// </para>
    /// <code>
    ///   if of_IsBusy() then return RetCode.E_BUSY        [:L57]   the caller-side guard
    ///   rtCode = task.of_Reset()                         [:L61]   DELEGATED to the worker
    ///   if IsFailed(rtCode) then return rtCode           [:L62]   the worker's code, VERBATIM
    ///   ... clear the caller-side state ...              [:L64-L65]
    ///   return RetCode.OK                                [:L67]   NOT rtCode
    /// </code>
    /// <para>
    /// <b>THREE THINGS A STRAIGHTFORWARD PORT GETS WRONG, ALL PRESERVED (constraint C-B).</b> First, the
    /// abandonment test is <c>Predicates.IsFailed</c> and not <c>&lt; 0</c>, and the difference is real:
    /// <c>IsFailed</c> excludes <c>CANCELLED</c> explicitly, so a cancelled worker does NOT abandon and the
    /// clear below still happens. Second, the successful return is the literal <c>OK</c> and not the
    /// worker's code, so a non-failing non-zero answer such as <c>PREVENT</c> is reported as success -
    /// which is consistent with the algebra, where a prevention satisfies the success predicate. Third,
    /// the clear leaves the RECORD count standing; see <see cref="QueryTaskEntry.ClearAfterReset"/>.
    /// </para>
    /// <para>
    /// The worker's own guard is not bypassed by the one above it. Both are live in the legacy at different
    /// layers - the caller-side <c>of_IsBusy()</c> and the worker's <c>#Running</c> [<c>:L237</c>] - and
    /// this is the ONE place in the SQL task layer where the worker's running guard is live rather than
    /// commented out, so reproducing both is faithful rather than redundant.
    /// </para>
    /// </remarks>
    public override Task<ResetQueryTaskResponse> Reset(
        ResetQueryTaskRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        if (!_tasks.TryResolve(request.Task, out QueryTaskEntry? entry) || entry!.IsReleased)
        {
            return Task.FromResult(new ResetQueryTaskResponse
            {
                Status = QueryWireCodes.Status(RetCode.E_INVALID_HANDLE, UnknownTaskText),
            });
        }

        // [:L57] the caller-side busy guard - TAKEN AS A LEASE, not read. The oracle's `if of_IsBusy()
        // then return E_BUSY` cannot be raced in process because the caller and the task are one thread of
        // control; here two requests can both read "not busy" and both go on to reset a task the other is
        // configuring or running. Same codes, same order, now indivisible.
        TaskLatchOutcome lease = entry.TryBeginExclusive();

        if (lease != TaskLatchOutcome.Acquired)
        {
            return Task.FromResult(new ResetQueryTaskResponse
            {
                Status = lease == TaskLatchOutcome.Gone
                    ? QueryWireCodes.Status(RetCode.E_INVALID_HANDLE, UnknownTaskText)
                    : QueryWireCodes.Status(RetCode.E_BUSY, TaskBusyText),
            });
        }

        try
        {
            // [:L61] delegated; the worker's [:L237] guard stands behind this one.
            long rtCode = entry.Task.Reset();

            // [:L62] the worker's code verbatim, and CANCELLED deliberately falls through.
            if (Predicates.IsFailed(rtCode))
            {
                return Task.FromResult(new ResetQueryTaskResponse
                {
                    Status = QueryWireCodes.Status(rtCode),
                });
            }

            // [:L64-L65] and [:L292-L293].
            entry.ClearAfterReset();

            // [:L67] the literal OK, NOT rtCode.
            return Task.FromResult(new ResetQueryTaskResponse
            {
                Status = QueryWireCodes.Status(RetCode.OK),
            });
        }
        finally
        {
            // A release that arrived while this reset was in flight could not dispose the task - the
            // teardown-with-work-pending hazard - so the duty passed to whichever path finished last, and
            // this is that path.
            if (entry.EndExclusive())
            {
                entry.Dispose();
            }
        }
    }

    // ----------------------------------------------------------------------------------------------
    //  THE NUMERIC SETTERS - AND THE GUARD BOUNDARIES THAT MUST NOT BE HARMONISED
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Sets the chunk row count.
    /// </summary>
    /// <param name="request">The handle and the requested chunk size.</param>
    /// <param name="context">The call context. Not read.</param>
    /// <returns>
    /// <c>OK</c>, <c>E_INVALID_ARGUMENT</c> for a value at or below one thousand, <c>E_BUSY</c> while a
    /// retrieval is in flight, or <c>E_INVALID_HANDLE</c>.
    /// </returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// ================= THE COUNTER-INTUITIVE GUARD, DELEGATED AND NOT RESTATED =================
    /// </para>
    /// <para>
    /// The oracle is one line: <c>if chunkSize &lt;= 1000 then return RetCode.E_INVALID_ARGUMENT</c>
    /// [<c>:L410</c>]. <b>THE COMPARISON IS <c>&lt;=</c> AND NOT <c>&lt;</c>, SO ONE THOUSAND ITSELF IS
    /// INVALID AND THE MINIMUM LEGAL VALUE IS ONE THOUSAND AND ONE.</b>
    /// </para>
    /// <para>
    /// <b>ANNOTATED THIS LOUDLY BECAUSE THE ORACLE CONTRADICTS ITSELF, AND THE CODE WINS.</b> The field's
    /// own inline comment reads <c>Chunk row count,min:1000</c> [<c>:L34</c>], which says one thousand is
    /// the minimum while the code beside it rejects exactly that value. Anyone reading only the comment
    /// will "correct" the guard to <c>&lt; 1000</c> and silently widen the contract by one value. Do not.
    /// The boundary is inclusive (constraint C-B).
    /// </para>
    /// <para>
    /// <b>THE GUARD IS NOT WRITTEN HERE AND MUST NOT BE.</b> It lives in
    /// <c>Tasks/SqlQueryTask.SetChunkSize</c> beside that locator, and this method delegates to it, so
    /// there is exactly one boundary in the service and no possibility of the two disagreeing. The
    /// DEFAULT is likewise not written here: it is <c>10000</c> [<c>:L34</c>], declared in the
    /// <c>Query</c> section of <c>appsettings.json</c> and applied to every task by its own constructor
    /// from <c>PersistenceOptions.Query.ChunkSize</c> - configuration, never a literal in this file.
    /// </para>
    /// <para>
    /// And the boundary is deliberately DIFFERENT from its three numeric neighbours - max rows guards
    /// <c>&lt; 0</c> so zero is legal, while page size and page index guard <c>&lt;= 0</c> because they
    /// are one-based. Three shapes across four setters; harmonising any of them changes observable
    /// behaviour.
    /// </para>
    /// </remarks>
    public override Task<SetChunkSizeResponse> SetChunkSize(
        SetChunkSizeRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        return Task.FromResult(new SetChunkSizeResponse
        {
            Status = MutateWithStatus(
                request.Task,
                task => QuerySettingOutcome.From(task.SetChunkSize(request.ChunkSize))),
        });
    }

    /// <summary>
    /// Sets the maximum number of rows a retrieval may return.
    /// </summary>
    /// <param name="request">The handle and the requested cap.</param>
    /// <param name="context">The call context. Not read.</param>
    /// <returns><c>OK</c>, <c>E_INVALID_ARGUMENT</c>, <c>E_BUSY</c> or <c>E_INVALID_HANDLE</c>.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// <b>THE BOUNDARY IS <c>&lt; 0</c>, SO ZERO IS LEGAL HERE</b> - and zero is the field's own legacy
    /// default, meaning no limit [<c>:L433</c>]. That is deliberately NOT the same boundary as the chunk
    /// size above, and the two must not be made uniform (constraint C-B). The guard itself is delegated to
    /// <c>Tasks/SqlQueryTask.SetMaxRows</c>; exceeding the cap at run time is a separate outcome,
    /// <c>E_OUT_OF_RANGE</c> with the cap interpolated into its diagnostic [<c>:L787-L790</c>].
    /// </remarks>
    public override Task<SetMaxRowsResponse> SetMaxRows(
        SetMaxRowsRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        return Task.FromResult(new SetMaxRowsResponse
        {
            Status = MutateWithStatus(
                request.Task,
                task => QuerySettingOutcome.From(task.SetMaxRows(request.MaxRows))),
        });
    }

    // ----------------------------------------------------------------------------------------------
    //  THE CLAUSE SETTERS - THE ONE-BASED INDEX, THE DELEGATED GUARD, AND THE INJECTION SITE
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Upserts a WHERE clause for one select block.
    /// </summary>
    /// <param name="request">The handle and the clause specification.</param>
    /// <param name="context">The call context. Not read.</param>
    /// <returns>
    /// <c>OK</c>, <c>E_INVALID_ARGUMENT</c> for a non-positive select index, an empty clause, an
    /// out-of-domain modification style or a clause body carrying SQL structure, <c>E_BUSY</c> while a
    /// retrieval is in flight, or <c>E_INVALID_HANDLE</c>.
    /// </returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// The legacy signature is
    /// <c>of_setwhereclause(readonly integer selectindex, readonly long ms, readonly string clause)</c>
    /// [<c>:L36</c>], and its guard is one line:
    /// <c>if selectIndex &lt;= 0 or clause = "" then return RetCode.E_INVALID_ARGUMENT</c> [<c>:L271</c>].
    /// </para>
    /// <para>
    /// <b>THE SELECT INDEX IS ONE-BASED AND ZERO IS INVALID - IT DOES NOT MEAN "THE FIRST SELECT"
    /// (hazard R9).</b> The caller-side twin publishes TWO arities, <c>(ms, clause)</c> and
    /// <c>(selectindex, ms, clause)</c> [<c>n_cst_threading_task_sqlquery.sru:L73-L76</c>], and the
    /// two-argument form implicitly targets block one. The wire carries the index explicitly with ONE as
    /// the documented default a caller sends, so both arities remain expressible - but the server does NOT
    /// substitute one for a zero. Doing so would widen the legacy guard by exactly the value it rejects.
    /// </para>
    /// <para>
    /// <b>THE GUARD IS DELEGATED AND NOT RESTATED.</b> <c>Sql/ClauseModifier</c> owns the index-and-empty
    /// test, the ONE-BASED update-in-place-or-append-at-end upsert keyed on the index [<c>:L271-L283</c>],
    /// and the structural-SQL refusal on the clause body. Restating any of them here would create a second
    /// boundary that can drift from the first.
    /// </para>
    /// <para>
    /// <b>WHAT IS NOT DELEGATED IS THE MODIFICATION STYLE, AND THAT REFUSAL BELONGS HERE.</b> A proto3
    /// enum field is open, so an unrecognised number arrives intact; <c>ClauseModifier.ToModifyStyle</c>
    /// passes it through unchanged BY DESIGN and its own documentation assigns the rejection to this file,
    /// because a value that escapes the domain must be refused at the boundary rather than reaching a code
    /// path chosen by elimination.
    /// </para>
    /// <para>
    /// ============================= THIS IS THE SQL-INJECTION SITE =============================
    /// Stated plainly rather than quietly repaired. The clause arrives as a RAW STRING and is spliced into
    /// the statement: the parser rewrites it [<c>:L691</c>], the rewritten text is read back
    /// [<c>:L703</c>] and it reaches the carrier's select property [<c>:L709</c>]. The exposure is
    /// mechanical - the connection parameter string is parsed for a bind-disabling flag
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru:L128</c>], and
    /// <c>DisableBind=1</c> means the runtime interpolates values as literals instead of binding them. The
    /// implementation uses parameterized commands internally while keeping the OBSERVABLE GENERATED
    /// STATEMENT unchanged, and the site is documented as a known legacy defect. Nothing observable is
    /// altered here (constraint C-B), and a bind parameter cannot protect SQL STRUCTURE anyway, which is
    /// why the boundary refusal in <c>ClauseModifier</c> exists beside it.
    /// </para>
    /// </remarks>
    public override Task<SetWhereClauseResponse> SetWhereClause(
        SetWhereClauseRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        return Task.FromResult(new SetWhereClauseResponse
        {
            Status = MutateWithStatus(
                request.Task,
                task => ApplyWhereClause(task, request.Clause)),
        });
    }

    /// <summary>
    /// Upserts an ORDER BY clause for one select block.
    /// </summary>
    /// <param name="request">The handle and the clause specification.</param>
    /// <param name="context">The call context. Not read.</param>
    /// <returns>The same outcomes as <see cref="SetWhereClause"/>.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// Identical validation to the WHERE setter, on a SEPARATE array with its own identical guard
    /// [<c>:L288-L300</c>], which is why the contract gives the two calls separate messages rather than
    /// sharing one: WHERE and ORDER BY diverge the moment paging is involved, because the paged builder
    /// STRIPS the ORDER BY and never touches the WHERE [<c>:L389-L390</c>].
    /// </para>
    /// <para>
    /// <b>AN ASYMMETRY WORTH KNOWING ABOUT AND NOT WORTH "FIXING" (constraint C-B):</b> the legacy's own
    /// internal paging path DOES pass an empty clause to the parser's order-modification entry point, in
    /// order to strip an existing ORDER BY [<c>:L390</c>, <c>:L832</c>]. That is an internal call that does
    /// not go through the public setter, so the public guard stands as written and an empty clause remains
    /// invalid on this contract.
    /// </para>
    /// </remarks>
    public override Task<SetOrderByClauseResponse> SetOrderByClause(
        SetOrderByClauseRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        return Task.FromResult(new SetOrderByClauseResponse
        {
            Status = MutateWithStatus(
                request.Task,
                task => ApplyOrderByClause(task, request.Clause)),
        });
    }

    // ----------------------------------------------------------------------------------------------
    //  THE PAGING SETTERS
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Applies the five paging settings in one call.
    /// </summary>
    /// <param name="request">The handle and the five values.</param>
    /// <param name="context">The call context. Not read.</param>
    /// <returns>
    /// <c>OK</c>, the first rejected setting's own code, <c>E_BUSY</c> or <c>E_INVALID_HANDLE</c>.
    /// </returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// Five legacy setters - <c>of_setpaged</c>, <c>of_setpagesize</c>, <c>of_setpageindex</c>,
    /// <c>of_setpagenative</c> and <c>of_setpagecounting</c> [<c>:L45-L49</c>] - are grouped into one call
    /// because the legacy RE-VALIDATES size and index TOGETHER at run time [<c>:L308-L310</c>], so a caller
    /// that sets one without the other has configured a task that cannot run.
    /// </para>
    /// <para>
    /// <b>APPLIED IN THE CONTRACT'S OWN FIELD ORDER AND ABANDONED AT THE FIRST REFUSAL</b>, so the answer
    /// names which setting was wrong - the same property the individual legacy setters give a caller.
    /// Earlier settings in the sequence remain applied, exactly as five successive legacy calls would leave
    /// them; the legacy has no transactional setter group and inventing one here would be a behaviour
    /// change.
    /// </para>
    /// <para>
    /// <b>PAGE SIZE AND PAGE INDEX ARE BOTH ONE-BASED AND BOTH GUARD <c>&lt;= 0</c></b> [<c>:L450</c>,
    /// <c>:L462</c>], and both are re-validated a second time by the paged-statement builder with the
    /// diagnostic 无效的分页设置! [<c>:L308</c>]. Both checks are preserved rather than collapsed, because a
    /// caller that never called the setter at all reaches only the second one. <b>PAGE COUNTING DEFAULTS TO
    /// TRUE</b> [<c>:L38</c>], so a caller using this call rather than the specification message is setting
    /// it explicitly and must send true to keep counting on.
    /// </para>
    /// </remarks>
    public override Task<SetPagingResponse> SetPaging(
        SetPagingRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        return Task.FromResult(new SetPagingResponse
        {
            Status = MutateWithStatus(request.Task, task => ApplyPaging(task, request)),
        });
    }

    /// <summary>
    /// Replaces the unique-index column list the inner-join paging strategy is built from.
    /// </summary>
    /// <param name="request">The handle and the replacement list.</param>
    /// <param name="context">The call context. Not read.</param>
    /// <returns><c>OK</c>, <c>E_BUSY</c> or <c>E_INVALID_HANDLE</c>.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// <b>THE COLLECTION IS REPLACED WHOLESALE</b> [<c>:L406</c>], unlike the two clause collections which
    /// are upserted - the legacy assigns the array rather than merging it. An empty list on THIS call
    /// therefore CLEARS it, which is the one place an empty repeated field means "clear" rather than "leave
    /// alone", because here it is the entire payload of a dedicated call rather than one field of a merge
    /// specification.
    /// </para>
    /// <para>
    /// <b>A NON-EMPTY LIST CHANGES THE GENERATED STATEMENT, so it is not a hint.</b> The first dialect arm
    /// branches on the list being non-empty [<c>:L323</c>] into a unique-index inner-join wrapper instead
    /// of the plain form, which changes what byte-exact statement parity is compared against.
    /// </para>
    /// <para>
    /// <b>EACH ELEMENT IS AN IDENTIFIER SPLICED INTO SQL, AND THE VALIDATION IS DELEGATED (CWE-89).</b> The
    /// arm concatenates every element into the select list, the join predicate and the ORDER BY, unquoted
    /// and unescaped [<c>:L331, :L333, :L336</c>]. An identifier cannot travel as a bind parameter - the
    /// emitted text IS the observable output parity is measured against - so
    /// <c>Sql/Paging/PagedUniqueIndexColumnValidator</c> vets each element at the point of use, where the
    /// statement it will be spliced into is available to check membership against. Nothing here re-validates
    /// it, and nothing here quotes a rejected element back to a caller.
    /// </para>
    /// </remarks>
    public override Task<SetPagedUniqueIndexColumnsResponse> SetPagedUniqueIndexColumns(
        SetPagedUniqueIndexColumnsRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        if (!TryAcceptUniqueIndexColumns([.. request.Columns], out string? refusal))
        {
            return Task.FromResult(new SetPagedUniqueIndexColumnsResponse
            {
                Status = QueryWireCodes.Status(RetCode.E_INVALID_ARGUMENT, refusal),
            });
        }

        return Task.FromResult(new SetPagedUniqueIndexColumnsResponse
        {
            Status = MutateWithStatus(
                request.Task,
                task => QuerySettingOutcome.From(
                    task.SetPagedUniqueIndexColumns([.. request.Columns]))),
        });
    }

    /// <summary>
    /// Whether a unique-index column list can be accepted onto a task at all.
    /// </summary>
    /// <param name="columns">The list as it arrived on the wire.</param>
    /// <param name="refusal">Receives the diagnostic when the list is refused.</param>
    /// <returns><see langword="true"/> when the list is acceptable.</returns>
    /// <remarks>
    /// <para>
    /// <b>A DELIBERATE NARROWING OF THE ORACLE, AND ONE THAT CANNOT REFUSE A REQUEST THAT WOULD HAVE
    /// WORKED.</b> The legacy setter is a single assignment that always answers success
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L406</c>], so a caller learned
    /// nothing here and discovered its mistake later, as a failed RETRIEVAL, on a statement it never saw.
    /// Both refusals below name a list that is incapable of producing a statement any engine would run:
    /// </para>
    /// <para>
    /// A BLANK OR MALFORMED IDENTIFIER is already refused at the point of use by
    /// <see cref="PagedUniqueIndexColumnValidator"/>, which vets every element before the rewriter splices
    /// it unquoted into the select list, the join predicate and the ORDER BY [<c>:L331, :L333, :L336</c>].
    /// So this is the SAME lexical rule brought forward to the call that supplied the value, not a second
    /// and possibly divergent one - the check is literally that type's own
    /// <see cref="PagedUniqueIndexColumnValidator.IsLexicallyValid"/>.
    /// </para>
    /// <para>
    /// A DUPLICATE is refused because the rewriter appends each element once per occurrence, so a repeated
    /// identifier appears twice in the inner select list and makes the join predicate that references it
    /// ambiguous. There is no correct output being rejected: the statement was already unexecutable.
    /// </para>
    /// <para>
    /// <b>MEMBERSHIP IS NOT CHECKED HERE, AND THAT IS NOT AN OMISSION.</b> Whether an identifier names a
    /// column of the statement can only be decided against the statement, and no statement is in hand at
    /// setter time - the select text may be set afterwards, or replaced. Membership therefore stays where it
    /// is enforceable, at the point of use, which is also where the alias resolution lives. This member
    /// refuses exactly what is refusable without a statement and no more.
    /// </para>
    /// <para>
    /// C-F: the rejected identifier is NOT quoted back. It is caller-supplied text destined for a SQL
    /// statement, and the diagnostic names the POSITION instead, which is enough to fix the call.
    /// </para>
    /// </remarks>
    private static bool TryAcceptUniqueIndexColumns(
        IReadOnlyList<string> columns,
        out string? refusal)
    {
        refusal = null;

        // An EMPTY list is the documented CLEAR and is always acceptable.
        if (columns.Count == 0)
        {
            return true;
        }

        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        for (int index = 0; index < columns.Count; index++)
        {
            // Widened to string? deliberately: a deserialized wire message can carry a null element
            // despite the non-nullable type argument - the same allowance the validator makes.
            string? candidate = columns[index];

            if (!PagedUniqueIndexColumnValidator.IsLexicallyValid(candidate))
            {
                refusal =
                    $"The unique-index column at position {index + 1} of {columns.Count} is not a usable "
                    + "SQL identifier, so no paged statement could be generated from this list. Each "
                    + "element must be a plain identifier, optionally qualified with periods, and the "
                    + "value itself is not quoted back here because it is caller-supplied text bound for a "
                    + "SQL statement. Supply an empty list to clear the setting.";

                return false;
            }

            if (!seen.Add(candidate!))
            {
                refusal =
                    $"The unique-index column at position {index + 1} of {columns.Count} repeats an "
                    + "earlier element of the same list. The paged statement appends each element once per "
                    + "occurrence, so a repeated identifier would appear twice in the inner select list "
                    + "and make the join predicate that references it ambiguous. Compared without regard "
                    + "to case, because SQL identifiers are.";

                return false;
            }
        }

        return true;
    }

    // ----------------------------------------------------------------------------------------------
    //  THE RETRIEVAL - THE ONE STREAMING METHOD
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Runs the retrieval and publishes its result progressively.
    /// </summary>
    /// <param name="request">The handle, plus settings merged over the task for this run.</param>
    /// <param name="responseStream">The server stream every arm is written to.</param>
    /// <param name="context">The call context, whose token is the authority on when to stop.</param>
    /// <returns>A task that completes when the stream has been fully written.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// <b>SERVER STREAMING IS THE FAITHFUL SHAPE, NOT MERELY THE EFFICIENT ONE (constraint C-K).</b> The
    /// legacy publishes a result through a SEQUENCE of events rather than returning one, and the storage
    /// binding beneath it exposes a FORWARD-ONLY CURSOR - <c>IsValid()</c>, <c>IsEmpty()</c>,
    /// <c>HasData()</c>, <c>NextRow()</c>
    /// [<c>ws_objects/pfw.utility.sqlite.pbl.src/n_sqliterecordset.sru:L11-L14</c>]. One fat response after
    /// everything completed would defeat the entire purpose of chunking, which exists so that a large
    /// result never has to be materialised whole on either side.
    /// </para>
    /// <para>
    /// ============ THE STREAMING-SCOPE DISCIPLINE, WHICH THIS METHOD IS BUILT AROUND ==============
    /// </para>
    /// <para>
    /// A gRPC call scope lives for the whole duration of the call, so this method must not capture a
    /// request-scoped resource for the life of the stream. <b>IT CAPTURES NONE.</b> It resolves nothing
    /// from a scope, constructs no <c>DbContext</c>, opens no connection and holds no
    /// <c>IServiceScope</c>: the only storage the retrieval touches is reached through the pooled
    /// transaction the task was bound to at creation, which is the connection-factory route rather than a
    /// per-request context. What this method owns for the call's duration is a queue, a stream writer and
    /// a latch.
    /// </para>
    /// <para>
    /// <b>RELEASE IS DETERMINISTIC AT EVERY BATCH BOUNDARY.</b> Each response is dequeued before it is
    /// written, so no payload survives its own write; the sink is discarded in the finally block, so a
    /// cancelled or failed retrieval leaves nothing reachable; and the entry's latch is dropped there too,
    /// disposing the task when a release arrived mid-flight. That is hazard 2 of
    /// <c>docs/PB多线程绕坑提示.md</c> expressed in managed terms - the legacy requires a worker to null its
    /// main-thread reference on uninit for exactly the same reason.
    /// </para>
    /// <para>
    /// <b>CANCELLATION IS A RETURN CODE, NEVER AN EXCEPTION.</b> Two sources reach here and they behave
    /// differently on purpose. A SERVER-SIDE cancellation - the managed reading of
    /// <c>of_IsCancelled()</c>, which the retrieval re-checks after the page-count step [<c>:L867</c>] -
    /// leaves the transport intact, so <c>CANCELLED</c> is reported on the terminal status arm where a
    /// consumer can see it. A CLIENT cancellation has already taken the transport away, so nothing further
    /// is written and the method simply returns: attempting a write on a stream whose peer has gone would
    /// raise where the legacy raised nothing.
    /// </para>
    /// <para>
    /// <b>THE TERMINAL STATUS ARM IS WRITTEN ONLY ON FAILURE.</b> The contract declares it as terminal
    /// FAILURE detail, and its reason for existing is that the legacy's error events fire MID-RETRIEVE,
    /// after chunks may already have been delivered [<c>:L172, :L184, :L220</c>], so a consumer needs the
    /// failure in band with the data it has already accepted. A successful stream simply ends.
    /// </para>
    /// <para>
    /// <b>AND NO <c>RpcException</c> IS THROWN.</b> <c>OperationStatus</c> is the uniform outcome of this
    /// file by the contract's own words, the status arm is its streaming twin, and the consumer's own
    /// client treats a status-only stream as a legal shape. Mapping a return code onto a transport status
    /// belongs to the host's central mapping, not to eleven handlers - which is also why a mapping
    /// installed there later needs no change here.
    /// </para>
    /// </remarks>
    public override async Task Query(
        QueryRequest request,
        IServerStreamWriter<QueryResponse> responseStream,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(responseStream);
        ArgumentNullException.ThrowIfNull(context);

        CancellationToken cancellationToken = context.CancellationToken;

        if (!_tasks.TryResolve(request.Task, out QueryTaskEntry? entry))
        {
            await WriteGuardStatusAsync(
                    responseStream,
                    QueryWireCodes.Status(RetCode.E_INVALID_HANDLE, UnknownTaskText),
                    cancellationToken)
                .ConfigureAwait(false);

            return;
        }

        // The caller-side busy guard, raised BEFORE the specification is merged so a setter arriving during
        // the merge is refused rather than racing it. The worker's own latch stands behind it and answers
        // the same code if a second execution somehow reaches it.
        TaskLatchOutcome lease = entry!.TryBeginRun();

        if (lease != TaskLatchOutcome.Acquired)
        {
            // TWO REASONS, TWO CODES, AND THE LEASE ALREADY DECIDED WHICH. A run is refused either because
            // one is already in flight - the caller-side busy guard - or because the task has been
            // released, and telling a caller its task is BUSY when the truth is that it is GONE would send
            // it into a retry loop that can never succeed. The reason travels back WITH the refusal rather
            // than being read again afterwards, because that second read is itself a race: a release
            // landing between the two would report GONE for a task that was merely busy, and vice versa.
            OperationStatus refusal = lease == TaskLatchOutcome.Gone
                ? QueryWireCodes.Status(RetCode.E_INVALID_HANDLE, UnknownTaskText)
                : QueryWireCodes.Status(RetCode.E_BUSY, TaskBusyText);

            await WriteGuardStatusAsync(responseStream, refusal, cancellationToken)
                .ConfigureAwait(false);

            return;
        }

        // 🔴 THE TRANSACTION GATE, TAKEN FOR THE WHOLE OF THE RETRIEVAL. C-05 shares one pooled
        // transaction object with C-06, C-07 and C-08 - the pool keys its entries on WHOLE-descriptor
        // equality, so two sessions opened with equal descriptors are handed the SAME object
        // [ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans_pool.sru:L136-L172] - and the legacy is
        // free of any race on it only because each pool lives on ONE worker thread [:L194-L206]. A
        // concurrent server has to reproduce that guarantee explicitly, and a retrieval that ran outside
        // the gate while its siblings ran inside it made the gate meaningless for all of them: the
        // transaction's own SQL code, error text and statement status are shared mutable state, and its
        // rollback path saves, mutates and restores five of them [n_cst_thread_trans.sru:L163-L183].
        //
        // IT IS TAKEN ASYNCHRONOUSLY, which is the reason the gate is not a Lock: this method awaits
        // inside its critical section - the retrieval streams - so a Lock could not span it. See
        // Grpc/TransactionGate.cs.
        //
        // THE SESSION IS RESOLVED FROM THE TASK'S OWN RECORD rather than from the request, so a caller
        // cannot name one session's task while presenting another's handle. An unresolvable session means
        // the session ended between the task's creation and now, which is the same refusal as a closing one.
        if (!_sessions.TryResolve(entry.SessionId, out TransactionSession? session) || session is null)
        {
            await WriteGuardStatusAsync(
                    responseStream,
                    QueryWireCodes.Status(RetCode.E_INVALID_TRANSACTION, UnknownSessionText),
                    cancellationToken)
                .ConfigureAwait(false);

            if (entry.EndRun())
            {
                entry.Dispose();
            }

            return;
        }

        using QueryStreamSink sink = new(responseStream, entry, cancellationToken, _maxSendMessageBytes);

        try
        {
            using TransactionGateScope gate = await session.Gate
                .EnterAsync(cancellationToken)
                .ConfigureAwait(false);

            // 🔴 THE LIVENESS TEST BELONGS INSIDE THE GATE. EndSession marks the session closing under
            // this same gate BEFORE it hands the transaction back, so either this retrieval is inside the
            // gate first and runs to completion, or the teardown is and this sees the flag. A test taken
            // outside the gate - or before it - is the check-then-act this ordering removes.
            if (session.IsClosing)
            {
                _ = await sink
                    .WriteTerminalStatusAsync(
                        QueryWireCodes.Status(RetCode.E_INVALID_TRANSACTION, SessionClosingText),
                        cancellationToken)
                    .ConfigureAwait(false);

                return;
            }

            QuerySettingOutcome merged = ApplySpec(entry.Task, request.Spec);
            long outcome = merged.Code;

            if (merged.IsAccepted)
            {
                outcome = await _runner
                    .RunAsync(entry.Task, sink, cancellationToken)
                    .ConfigureAwait(false);

                // The tail drain: the row count of an empty result, the single full-state image, and the
                // paging totals all arrive through synchronous callbacks with no await point of their own,
                // so this is where they go out. A drain failure is reported only when the retrieval itself
                // succeeded, because the retrieval's own code is the more specific answer.
                //
                // IT RUNS INSIDE THE GATE TOO, because it publishes state the retrieval captured from the
                // transaction: releasing the gate before the capture is complete would let a sibling
                // operation mutate that state between the execution and the report.
                long tail = await sink.FlushAsync(cancellationToken).ConfigureAwait(false);

                if (outcome == RetCode.OK)
                {
                    outcome = tail;
                }
            }

            if (outcome == RetCode.OK)
            {
                return;
            }

            OperationStatus status;

            if (!merged.IsAccepted)
            {
                // A BOUNDARY refusal: its text is this file's own and there is no driver payload behind
                // it, because no statement was ever issued.
                status = merged.ToStatus();
            }
            else if (sink.SendCeilingExceeded)
            {
                // ==========================================================================================
                //  THE SEND CEILING IS REPORTED HERE, AND IT DELIBERATELY OVERRIDES THE CODEC'S OWN ANSWER.
                //
                //  The codec that met the refusal answered the legacy's E_INTERNAL_ERROR with
                //  `TransData Failed` [n_cst_thread_task_sqlquery.sru:L183-L185] and that arm is preserved
                //  verbatim, because it is what the oracle does when a hand-over is refused. But the
                //  ceiling is a PORT-INTRODUCED bound with no legacy counterpart at all - the legacy hands
                //  a blob to an in-process event and has no message size - so relaying the ported code
                //  would describe a port-only condition in the oracle's vocabulary and tell the caller
                //  nothing it can act on. E_BUSY plus this file's own sentence describes the actual
                //  condition, and it is the code the sibling ceiling one hop up already answers.
                //
                //  NO db_error IS ATTACHED, and that is what makes the projection work: DataServices
                //  raises a failing status carrying no driver payload as the CALL status
                //  [Grpc/DataWindowService.cs, the Status arm of the retrieve loop], which is how this
                //  reaches a REST caller as 429 rather than as an empty success.
                // ==========================================================================================
                status = QueryWireCodes.Status(RetCode.E_BUSY, SendCeilingExceededText);

                // THE CAUSE GOES TO THE OPERATOR, WHICH IS THE HALF THAT WAS MISSING. Previously the only
                // trace of this condition anywhere was "a retrieval ended with return code -27", with no
                // record of WHY - a whole-log search for a size or a ceiling returned nothing. Both numbers
                // are byte counts: they carry no row value and no statement text, so recording them is
                // safe under constraint C-F, and they are what an operator needs to decide between telling
                // the caller to chunk smaller and raising the bound.
                _logger?.LogWarning(
                    "A retrieval built a response of {MessageBytes} bytes, which exceeds the configured "
                    + "gRPC send ceiling of {CeilingBytes} bytes (Ingress:MaxSendMessageBytes). The "
                    + "retrieval was abandoned and the caller was told to reduce its chunk size. No "
                    + "response payload is recorded.",
                    sink.SendCeilingBytes,
                    sink.MaxMessageBytes);
            }
            else
            {
                QueryFaultSnapshot fault = entry.Faults.Snapshot();

                // THE DIAGNOSTIC IS MASKED ON ITS WAY OUT, and it is the ONE text on this contract that
                // arrives from outside this file. Every other diagnostic this service emits is a fixed
                // sentence it wrote itself; this one is whatever the worker task raised, and two of the
                // task layer's arms raise statement-bearing text rather than a sentence: the carrier's
                // select-property modification failure forwards the runtime's own message, which quotes
                // the whole rejected `DataWindow.Table.Select='...'` assignment and therefore the
                // generated statement inside it, and the driver's message echoes offending values - a
                // uniqueness violation names the duplicate key, a constraint failure names the column
                // and its value. Relaying that verbatim would hand a caller row data and statement text
                // through a field documented as opaque display text.
                //
                // WHAT SURVIVES AND WHAT DOES NOT. The redactor masks literals and comment bodies ONLY,
                // so the message's wording, its shape and its return code are unchanged and a
                // diagnostic that quotes no value passes through byte for byte - which is every fixed
                // sentence the legacy raises. What the caller loses is exactly the material it must not
                // have had. The legacy performs no redaction anywhere, so this is a REQUIRED ADDITION
                // rather than a ported behaviour (AAP 0.6.3.8), and it is applied here at the egress
                // rather than at the recorder so the recorder stays a faithful reproduction of the
                // proxy's own last-error field.
                // 🔴 PROVIDER-DIAGNOSTIC POLICY, so this field and the `db_error.sqlerrtext` attached below
                // disclose at the SAME depth - they did not before, because Microsoft.Data.Sqlite's envelope
                // presents the result code as a numeric literal and the diagnosis as a quoted string, and
                // the strict policy masked both.
                //
                // THE `Modify` REJECTION IS STILL FULLY MASKED, WHICH IS WHY THE WIDENING IS SAFE HERE OF
                // ALL PLACES. The paragraph above notes that this text may be the whole rejected
                // `DataWindow.Table.Select='...'` assignment with the generated statement inside it. That
                // string is not the provider's envelope, so it does not match the shape test and takes the
                // strict path unchanged - the envelope must match the WHOLE input or not at all. Only a
                // genuine driver envelope keeps its wrapper, and its interior is masked even then. See
                // Errors/SqlRedactor.cs, RedactProviderDiagnostic, for the field-class policy.
                string diagnostic = SqlRedactor.Instance.RedactProviderDiagnostic(fault.ErrorText);

                // db_error is attached ONLY when the driver event actually fired, which is what the
                // contract requires: it is absent on a validation failure, and that is the normal case for
                // every guard this file reproduces. The projection masks the statement field
                // unconditionally (constraint C-F).
                status = fault.HasDbError
                    ? QueryWireCodes.Status(outcome, diagnostic, fault.DbError)
                    : QueryWireCodes.Status(outcome, diagnostic);
            }

            _ = await sink.WriteTerminalStatusAsync(status, cancellationToken).ConfigureAwait(false);

            // ONLY THE NUMERIC CODE IS LOGGED (constraint C-F). The diagnostic is not written here even in
            // its masked form: the task's own error hook already logged it once at the point it was
            // raised, so a second record would only be a second opportunity to leak whatever the mask
            // did not catch, and it would say nothing the first record did not.
            _logger?.LogWarning(
                "A retrieval ended with return code {ReturnCode}; the diagnostic was relayed to the "
                + "caller and is deliberately not logged.",
                outcome);
        }
        finally
        {
            // Deterministic release, in this order: drop anything the retrieval left queued, then drop the
            // latch - which disposes the task when a release arrived while it was in flight.
            sink.Discard();

            if (entry.EndRun())
            {
                entry.Dispose();
            }
        }
    }

    // ----------------------------------------------------------------------------------------------
    //  THE COUNT READERS
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Answers the paging totals of the most recent retrieval on this task.
    /// </summary>
    /// <param name="request">The handle to read.</param>
    /// <param name="context">The call context. Not read: this call performs no I/O whatsoever.</param>
    /// <returns>The page total, the record total, and whether a counting statement was issued.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// <b>THIS IS A READER, NOT AN OPERATION, AND THAT IS WHAT THE LEGACY SURFACE IS.</b> The legacy count
    /// surface is three caller-side accessors over three private fields - <c>of_getpagecount()</c>,
    /// <c>of_getrecordcount()</c> and <c>of_getrowcount()</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlquery.sru:L70, :L71, :L78</c>] -
    /// populated by the <c>onpagereceived</c> and <c>ondatareceived</c> events. So this method reads what
    /// the last run published and ISSUES NO STATEMENT OF ITS OWN. The third reader has no field on
    /// <c>CountResponse</c>; the row count travels on the stream's own arm instead, so nothing is lost by
    /// not duplicating it here.
    /// </para>
    /// <para>
    /// <b>NO BUSY GUARD, DELIBERATELY (constraint C-B).</b> The legacy's nine busy guards are on MUTATORS;
    /// the three accessors have none, and a field read cannot fail. While a retrieval is in flight the
    /// values read as the prepare-equivalent left them - all three cleared [<c>:L507-L509</c>] - which is
    /// exactly what the legacy caller would observe at the same moment.
    /// </para>
    /// <para>
    /// <b>-1 IS A VALUE AND NOT AN ABSENCE (constraint C-B).</b> The legacy assigns <c>-1</c> to BOTH totals
    /// on the else-branch of its three-condition gate [<c>:L861-L862</c>] - taken when the task is not
    /// paged, when counting is switched off, or when the source is a stored procedure [<c>:L818</c>] - so a
    /// consumer must treat <c>-1</c> as "not counted" and must not do arithmetic on it. It is emphatically
    /// not zero, which would claim a real total of no pages.
    /// </para>
    /// <para>
    /// <b>THE ARITHMETIC SHORT-CIRCUIT IS OBSERVABLE THROUGH <c>counted</c>.</b> When the current page came
    /// back shorter than the page size, or empty on page one, the legacy issues NO counting statement at
    /// all: the page count becomes the current page index, the record count becomes
    /// <c>(pageIndex - 1) * pageSize + rowsReturned</c>, and a zero record count forces a zero page count
    /// [<c>:L820-L823</c>]. Those totals are real and usable, which is why a consumer must not infer from a
    /// missing count query that counting FAILED - and why <c>counted</c> is a field rather than something a
    /// caller is left to deduce.
    /// </para>
    /// <para>
    /// <b>THE COUNTING STATEMENT ITSELF IS BUILT ELSEWHERE AND ITS SPELLINGS ARE PARITY CRITERIA.</b> The
    /// legacy replaces the column list with the literal <c>1 AS _</c> [<c>:L830</c>], strips any ORDER BY
    /// [<c>:L831-L833</c>] and wraps the result as
    /// <c>SELECT COUNT(1) AS CNT FROM ( ... ) pfwPagedSQL_Tbl</c> [<c>:L834</c>]. Byte-exact generated SQL
    /// is the acceptance criterion, so none of those spellings may be tidied - and none of them appears in
    /// this file, because nothing here constructs SQL. Note also, as a documentation point rather than a
    /// branch this file owns, that <b>the counting path reads success as the LITERAL VALUE 1</b> rather than
    /// as the zero of the return-code algebra [<c>:L851</c>], and that a cancellation is not "failed" under
    /// the tri-state predicate so it FALLS THROUGH that test and is reported as a database error carrying
    /// 检索失败 [<c>:L845-L858</c>]. Both belong to <c>Tasks/</c>, and both are preserved there.
    /// </para>
    /// <para>
    /// On an unknown handle both totals answer the not-counted sentinel rather than zero, because zero
    /// would assert a real total that was never computed.
    /// </para>
    /// </remarks>
    public override Task<CountResponse> Count(CountRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        if (!_tasks.TryResolve(request.Task, out QueryTaskEntry? entry))
        {
            return Task.FromResult(new CountResponse
            {
                Status = QueryWireCodes.Status(RetCode.E_INVALID_HANDLE, UnknownTaskText),
                PageCount = SqlQueryTask.NotCountedValue,
                RecordCount = SqlQueryTask.NotCountedValue,
                Counted = false,
            });
        }

        QueryCountSnapshot counts = entry!.Counts;

        return Task.FromResult(new CountResponse
        {
            // Reading the fields succeeded, which is the outcome this call reports. A failed retrieval is
            // reported on the retrieval's own stream, and the accessors it left behind cannot themselves
            // fail - the legacy's cannot either.
            Status = QueryWireCodes.Status(RetCode.OK),
            PageCount = counts.PageCount,
            RecordCount = counts.RecordCount,
            Counted = counts.Counted,
        });
    }

    // ----------------------------------------------------------------------------------------------
    //  THE TRANSLATIONS - EVERY GUARD IS DELEGATED EXCEPT THE THREE THIS BOUNDARY OWNS
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Resolves a handle, applies the caller-side busy guard, and runs one mutation against the task.
    /// </summary>
    /// <param name="handle">The handle from the request.</param>
    /// <param name="mutation">The mutation to apply once the handle and the latch have been checked.</param>
    /// <returns>The wire status for the whole call.</returns>
    /// <remarks>
    /// <para>
    /// <b>ONE PLACE FOR THE TWO CHECKS EVERY MUTATOR SHARES.</b> Every setter and the reset need the same
    /// two answers before they can do anything - is the handle live, and is a retrieval in flight - and the
    /// legacy writes the second one out nine times, once per mutator
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlbase.sru:L57, :L73, :L95, :L110,
    /// :L124, :L144, :L162, :L179, :L188</c>]. Writing it nine times here as well would give nine places
    /// for it to be forgotten; the reset states it inline instead, because its full shape has three more
    /// steps that only it has.
    /// </para>
    /// <para>
    /// <c>E_BUSY</c> is a DISTINCT outcome from a failure, and flattening the two would remove a
    /// distinction a caller can act on: retrying the same call unchanged succeeds once the retrieval
    /// finishes.
    /// </para>
    /// </remarks>
    private OperationStatus MutateWithStatus(
        TaskHandle? handle,
        Func<SqlQueryTask, QuerySettingOutcome> mutation)
    {
        if (!_tasks.TryResolve(handle, out QueryTaskEntry? entry) || entry!.IsReleased)
        {
            return QueryWireCodes.Status(RetCode.E_INVALID_HANDLE, UnknownTaskText);
        }

        // The caller-side `if of_IsBusy() then return RetCode.E_BUSY` - TAKEN AS A LEASE. Reading the latch
        // and then applying the mutation is a check-then-act that lets a setter land on a task a retrieval
        // has already started merging its specification from, or lets two setters interleave.
        TaskLatchOutcome lease = entry.TryBeginExclusive();

        if (lease != TaskLatchOutcome.Acquired)
        {
            return lease == TaskLatchOutcome.Gone
                ? QueryWireCodes.Status(RetCode.E_INVALID_HANDLE, UnknownTaskText)
                : QueryWireCodes.Status(RetCode.E_BUSY, TaskBusyText);
        }

        try
        {
            // THE MUTATION RUNS WITHOUT ANY LOCK HELD, which is the whole reason the exclusion is a lease
            // rather than a lock around the delegate. Holding the entry's lock across caller-supplied code
            // would put arbitrary work inside a critical section that every reader also enters; a lease
            // gives the same mutual exclusion with none of that.
            return mutation(entry.Task).ToStatus();
        }
        finally
        {
            if (entry.EndExclusive())
            {
                entry.Dispose();
            }
        }
    }

    /// <summary>
    /// Writes a status-only stream for a refusal that happened before the retrieval could begin.
    /// </summary>
    /// <param name="responseStream">The server stream.</param>
    /// <param name="status">The refusal.</param>
    /// <param name="cancellationToken">Observed, and deliberately not passed to the write.</param>
    /// <returns>A task that completes when the single response has been written, or skipped.</returns>
    /// <remarks>
    /// A status-only stream is a legal shape on this contract - the terminal status is one of the five
    /// alternatives the response carries - so a refusal is reported in the same channel a real outcome
    /// would use rather than as an exception. The write is skipped outright when the caller has already
    /// gone, because a stream whose peer has vanished has nowhere to put it.
    /// </remarks>
    private static async Task WriteGuardStatusAsync(
        IServerStreamWriter<QueryResponse> responseStream,
        OperationStatus status,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        // SINGLE-ARGUMENT WriteAsync: the two-argument overload is not implemented by this server stack
        // and throws for any cancellable token.
        await responseStream
            .WriteAsync(new QueryResponse { Status = status })
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Validates a clause modification style against the published domain.
    /// </summary>
    /// <param name="wireStyle">The style as it arrived.</param>
    /// <param name="modifyStyle">The in-process style constant, valid only when this returns true.</param>
    /// <returns><see langword="true"/> when the style is one of the three legal values.</returns>
    /// <remarks>
    /// <para>
    /// <b>A PROTO3 ENUM FIELD IS OPEN, SO NAMING THE VALUES BUYS DOCUMENTATION AND NEVER ENFORCEMENT.</b>
    /// An unrecognised number is not rejected by the parser and is not folded to zero: it is preserved and
    /// handed to the application, so a request carrying 99, or -1, or the unspecified zero arrives intact
    /// and deserializes without error.
    /// </para>
    /// <para>
    /// The conversion is <c>Sql/ClauseModifier.ToModifyStyle</c>, which passes an unrecognised value
    /// through UNCHANGED by design and documents that the refusal belongs to this file - which keeps the
    /// conversion total and the transport concern with the transport. The three legal constants are
    /// CONSUMED from that class rather than redeclared, because this folder is outside every
    /// naming-suppression glob and their preserved screaming-snake spellings could not be declared here.
    /// </para>
    /// <para>
    /// <b>THE UNSPECIFIED VALUE IS REFUSED BY THE SAME RULE AND IS NEVER TREATED AS REPLACE:</b> it is a
    /// proto3 mechanical artifact rather than a legacy constant. The legacy offers no validation to copy -
    /// its setters guard only the index and the emptiness of the clause and then dispatch on the style with
    /// no default arm, so an unrecognised style silently modifies nothing - which is why defining the
    /// rejection narrows the contract with an error instead of widening it with a guess.
    /// </para>
    /// </remarks>
    private static bool TryResolveModifyStyle(SqlModifyStyle wireStyle, out long modifyStyle)
    {
        modifyStyle = ClauseModifier.ToModifyStyle(wireStyle);

        return modifyStyle == ClauseModifier.SQL_MS_REPLACE
            || modifyStyle == ClauseModifier.SQL_MS_APPEND
            || modifyStyle == ClauseModifier.SQL_MS_PREPEND;
    }

    /// <summary>
    /// Applies one WHERE clause specification.
    /// </summary>
    /// <param name="task">The task to configure.</param>
    /// <param name="clause">The specification. May be <see langword="null"/> when the caller omitted it.</param>
    /// <returns>The outcome, carrying this boundary's own text only for a refusal the legacy has no
    /// counterpart for.</returns>
    /// <remarks>
    /// The one-based select index and the clause body are passed through UNTOUCHED to
    /// <c>Tasks/SqlQueryTask.SetWhereClause</c>, which forwards to <c>Sql/ClauseModifier</c> - the owner of
    /// the <c>selectIndex &lt;= 0 or clause = ""</c> guard [<c>:L271</c>], of the one-based upsert keyed on
    /// the index, and of the structural-SQL refusal. A zero index is NOT rewritten to one, and space-only
    /// text is NOT rejected, because the legacy guard compares against the empty string and nothing more.
    /// </remarks>
    private static QuerySettingOutcome ApplyWhereClause(SqlQueryTask task, SqlClauseSpec? clause)
    {
        if (clause is null)
        {
            return QuerySettingOutcome.Refused(RetCode.E_INVALID_ARGUMENT, MissingClauseText);
        }

        if (!TryResolveModifyStyle(clause.ModifyStyle, out long modifyStyle))
        {
            return QuerySettingOutcome.Refused(RetCode.E_INVALID_ARGUMENT, InvalidModifyStyleText);
        }

        return QuerySettingOutcome.From(
            task.SetWhereClause(clause.SelectIndex, modifyStyle, clause.Clause));
    }

    /// <summary>
    /// Applies one ORDER BY clause specification.
    /// </summary>
    /// <param name="task">The task to configure.</param>
    /// <param name="clause">The specification. May be <see langword="null"/>.</param>
    /// <returns>The outcome.</returns>
    /// <remarks>
    /// Identical to the WHERE path, against the separate array with its own identical guard [<c>:L288</c>].
    /// </remarks>
    private static QuerySettingOutcome ApplyOrderByClause(SqlQueryTask task, SqlClauseSpec? clause)
    {
        if (clause is null)
        {
            return QuerySettingOutcome.Refused(RetCode.E_INVALID_ARGUMENT, MissingClauseText);
        }

        if (!TryResolveModifyStyle(clause.ModifyStyle, out long modifyStyle))
        {
            return QuerySettingOutcome.Refused(RetCode.E_INVALID_ARGUMENT, InvalidModifyStyleText);
        }

        return QuerySettingOutcome.From(
            task.SetOrderByClause(clause.SelectIndex, modifyStyle, clause.Clause));
    }

    /// <summary>
    /// Applies the five paging settings in the contract's own field order.
    /// </summary>
    /// <param name="task">The task to configure.</param>
    /// <param name="request">The request carrying the five values.</param>
    /// <returns>The first refusal, or acceptance.</returns>
    /// <remarks>
    /// Each setter's own guard is delegated: paged and native have none, page size and page index both
    /// reject <c>&lt;= 0</c> because they are one-based [<c>:L450, :L462</c>], and page counting has none.
    /// Abandoning at the first refusal is what lets the answer name the offending setting.
    /// </remarks>
    private static QuerySettingOutcome ApplyPaging(SqlQueryTask task, SetPagingRequest request)
    {
        QuerySettingOutcome applied = QuerySettingOutcome.From(task.SetPaged(request.Paged));

        if (!applied.IsAccepted)
        {
            return applied;
        }

        applied = QuerySettingOutcome.From(task.SetPageSize(request.PageSize));

        if (!applied.IsAccepted)
        {
            return applied;
        }

        applied = QuerySettingOutcome.From(task.SetPageIndex(request.PageIndex));

        if (!applied.IsAccepted)
        {
            return applied;
        }

        applied = QuerySettingOutcome.From(task.SetPageNative(request.PageNative));

        if (!applied.IsAccepted)
        {
            return applied;
        }

        return QuerySettingOutcome.From(task.SetPageCounting(request.PageCounting));
    }

    /// <summary>
    /// Merges a specification onto a task, as if each legacy setter had been called in field order.
    /// </summary>
    /// <param name="task">The task to configure.</param>
    /// <param name="spec">The specification, or <see langword="null"/> when the caller sent none.</param>
    /// <returns>The first refusal, or acceptance.</returns>
    /// <remarks>
    /// <para>
    /// <b>EXPLICIT PRESENCE IS LOAD-BEARING, NOT DECORATION.</b> Every scalar on the specification is
    /// optional, and an UNSET field LEAVES THE TASK'S EXISTING VALUE ALONE. Without that, merely omitting a
    /// field would silently overwrite state - and for two of them the implicit default is not even a legal
    /// value: the chunk size defaults to 10000 [<c>:L34</c>] and zero would be rejected by its own guard,
    /// while page counting defaults to TRUE [<c>:L38</c>] so an implicit false would silently switch
    /// counting off for every caller who simply did not mention it. Each block below is therefore gated on
    /// the field's presence and never on its value.
    /// </para>
    /// <para>
    /// <b>A REPEATED FIELD LEFT EMPTY LIKEWISE LEAVES THE COLLECTION UNCHANGED</b>, because repeated fields
    /// have no explicit-presence form. Clearing a collection is what the reset is for [<c>:L237</c>].
    /// </para>
    /// <para>
    /// <b>THE ORDER IS THE CONTRACT'S FIELD ORDER, AND TWO SETTINGS INTERACT.</b> Setting the data object
    /// blanks the syntax [<c>:L419-L420</c>] and setting the syntax blanks the data object
    /// [<c>:L474-L475</c>], so with both present the later assignment wins - exactly as two successive
    /// legacy calls would behave. That is why neither is modelled as a mutually exclusive choice: the legacy
    /// accepts both in either order.
    /// </para>
    /// </remarks>
    private static QuerySettingOutcome ApplySpec(SqlQueryTask task, QuerySpec? spec)
    {
        if (spec is null)
        {
            return QuerySettingOutcome.Accepted;
        }

        QuerySettingOutcome applied;

        // 1 - of_setcache [:L58]. No guard.
        if (spec.HasCache)
        {
            applied = QuerySettingOutcome.From(task.SetCache(spec.Cache));

            if (!applied.IsAccepted)
            {
                return applied;
            }
        }

        // 2 - of_setdataobject [:L59]. Blanks the syntax [:L419-L420].
        if (spec.HasDataObject)
        {
            applied = QuerySettingOutcome.From(task.SetDataObject(spec.DataObject));

            if (!applied.IsAccepted)
            {
                return applied;
            }
        }

        // 3 - of_setsqlsyntax [:L68]. Blanks the data object [:L474-L475].
        if (spec.HasSqlSyntax)
        {
            // 🔴 THE READ-SCOPE ADMISSIBILITY TEST, BEFORE THE SETTER AND NOT AFTER IT. A syntax blob
            // carries its own retrieval statement in a `retrieve="..."` clause, and that clause is what
            // the DataWindow runtime registers and this service ultimately executes - so the syntax path
            // reaches the provider exactly as the statement path does, and needs the same gate. Testing
            // BEFORE the setter is what makes the refusal total: the setter also BLANKS the data-object
            // name [:L475], so calling it and then refusing would leave the task with neither source.
            if (!ReadOnlyStatementGuard.IsAdmissibleSyntax(spec.SqlSyntax))
            {
                return QuerySettingOutcome.Refused(RetCode.E_INVALID_SQL, InadmissibleSyntaxText);
            }

            applied = QuerySettingOutcome.From(task.SetSqlSyntax(spec.SqlSyntax));

            if (!applied.IsAccepted)
            {
                return applied;
            }
        }

        // 4 - of_setsql [:L67]. THE ORACLE'S SETTER HAS NO GUARD AT ALL, and the asymmetry with the
        // command contract is real and preserved: it is a plain assignment [:L468] while the command task
        // rejects an empty statement immediately. The EMPTINESS check on this path still happens later,
        // inside the task body, where an empty statement answers E_INVALID_SQL with the diagnostic
        // SQL为空! [:L616-L617] - moving THAT forward would make a caller see a rejection at configuration
        // time that the legacy defers to run time (constraint C-B).
        //
        // 🔴 WHAT IS NEW HERE IS NOT THE EMPTINESS CHECK BUT THE READ-SCOPE ADMISSIBILITY TEST, and it is
        // a different question with a different justification. This whole contract is published under the
        // READ scope, and the legacy could not have needed a gate because a library has no caller whose
        // rights are narrower than the process's. Across this boundary it does: without the test, a
        // credential minted for `persistence.read` reaches INSERT, DELETE, DDL, PRAGMA and - because the
        // provider executes every statement in a batch it is handed - whole batches spliced behind a
        // semicolon. That is a REQUIRED ADDITION in the sense AAP 0.4.2.6 uses for the statement
        // redactor, and it narrows the contract with a DEFINED error rather than widening it with a guess
        // (AAP 0.1.5, constraint C-G). Sql/ReadOnlyStatementGuard.cs owns the grammar and states exactly
        // what it does not touch; an empty or malformed statement stays admissible here.
        if (spec.HasSql)
        {
            if (!ReadOnlyStatementGuard.IsAdmissibleStatement(spec.Sql))
            {
                return QuerySettingOutcome.Refused(RetCode.E_INVALID_SQL, InadmissibleStatementText);
            }

            applied = QuerySettingOutcome.From(task.SetSql(spec.Sql));

            if (!applied.IsAccepted)
            {
                return applied;
            }
        }

        // 5 - of_sethookclass [:L60]. A CLASS NAME, not a type: the task instantiates and destroys the hook
        // around the retrieve, and a hook that answers E_NO_IMPLEMENTATION - or no hook at all - means "not
        // handled, use the default retrieval". The name is surfaced; the activation is delegated. This is
        // NOT a script-invocation or dynamic-evaluation facility and must not be read as one: nothing here
        // transmits code, script or an expression to evaluate, which would be deferred territory
        // (constraint C-D).
        if (spec.HasHookClass)
        {
            applied = QuerySettingOutcome.From(task.SetHookClass(spec.HookClass));

            if (!applied.IsAccepted)
            {
                return applied;
            }
        }

        // 6 - of_setfilter [:L69]. A DOCUMENTED QUIRK, PRESERVED VERBATIM (constraint C-B): the legacy
        // rewrites an EMPTY filter to a SINGLE SPACE [:L481], so "" and " " are the same request. It looks
        // like a typo and is not - the space is how the legacy clears a filter rather than leaving it
        // unset. The rewrite lives in the setter, not here.
        if (spec.HasFilter)
        {
            applied = QuerySettingOutcome.From(task.SetFilter(spec.Filter));

            if (!applied.IsAccepted)
            {
                return applied;
            }
        }

        // 7 - of_setsort [:L70]. Carries the IDENTICAL empty-to-single-space rewrite [:L487].
        if (spec.HasSort)
        {
            applied = QuerySettingOutcome.From(task.SetSort(spec.Sort));

            if (!applied.IsAccepted)
            {
                return applied;
            }
        }

        // 8 - of_setmaxrows [:L61]. Guard `< 0`, so ZERO IS LEGAL and means no limit [:L433].
        if (spec.HasMaxRows)
        {
            applied = QuerySettingOutcome.From(task.SetMaxRows(spec.MaxRows));

            if (!applied.IsAccepted)
            {
                return applied;
            }
        }

        // 9 - of_setchunksize [:L57]. Guard `<= 1000`, so ONE THOUSAND ITSELF IS INVALID [:L410] - a
        // different boundary from field 8 on purpose, and the field's own comment contradicts it [:L34].
        if (spec.HasChunkSize)
        {
            applied = QuerySettingOutcome.From(task.SetChunkSize(spec.ChunkSize));

            if (!applied.IsAccepted)
            {
                return applied;
            }
        }

        // 10 - of_setpaged [:L63]. No guard.
        if (spec.HasPaged)
        {
            applied = QuerySettingOutcome.From(task.SetPaged(spec.Paged));

            if (!applied.IsAccepted)
            {
                return applied;
            }
        }

        // 11 - of_setpagesize [:L66]. Guard `<= 0`: a ONE-BASED domain, unlike field 8 [:L462].
        if (spec.HasPageSize)
        {
            applied = QuerySettingOutcome.From(task.SetPageSize(spec.PageSize));

            if (!applied.IsAccepted)
            {
                return applied;
            }
        }

        // 12 - of_setpageindex [:L64]. Guard `<= 0`: ONE-BASED, so the first page is 1 and not 0 [:L450].
        if (spec.HasPageIndex)
        {
            applied = QuerySettingOutcome.From(task.SetPageIndex(spec.PageIndex));

            if (!applied.IsAccepted)
            {
                return applied;
            }
        }

        // 13 - of_setpagenative [:L65]. No guard.
        if (spec.HasPageNative)
        {
            applied = QuerySettingOutcome.From(task.SetPageNative(spec.PageNative));

            if (!applied.IsAccepted)
            {
                return applied;
            }
        }

        // 14 - of_setpagecounting [:L62]. LEGACY DEFAULT IS TRUE [:L38].
        if (spec.HasPageCounting)
        {
            applied = QuerySettingOutcome.From(task.SetPageCounting(spec.PageCounting));

            if (!applied.IsAccepted)
            {
                return applied;
            }
        }

        // 15 - of_setwhereclause [:L53], UPSERTED on the select index rather than appended, so the same
        // index sent twice in one specification is last-wins [:L273-L281].
        foreach (SqlClauseSpec clause in spec.WhereClauses)
        {
            applied = ApplyWhereClause(task, clause);

            if (!applied.IsAccepted)
            {
                return applied;
            }
        }

        // 16 - of_setorderbyclause [:L54]. Identical upsert semantics on a separate array [:L290-L298].
        foreach (SqlClauseSpec clause in spec.OrderByClauses)
        {
            applied = ApplyOrderByClause(task, clause);

            if (!applied.IsAccepted)
            {
                return applied;
            }
        }

        // 17 - of_setpageduniqueindexcolumns [:L56]. REPLACES the collection wholesale [:L405-L406], which
        // is why an empty list here has to mean "leave alone" rather than "clear": on a merge specification
        // an empty repeated field is indistinguishable from an absent one, and the dedicated call is where
        // clearing is expressible.
        if (spec.PagedUniqueIndexColumns.Count > 0)
        {
            applied = QuerySettingOutcome.From(
                task.SetPagedUniqueIndexColumns([.. spec.PagedUniqueIndexColumns]));

            if (!applied.IsAccepted)
            {
                return applied;
            }
        }

        // 18 - of_addparam [n_cst_thread_task_sqlbase.sru:L67], APPENDED in order. The name is lowercased by
        // the legacy on entry [:L256] and the value is a discriminated union, never a bare string, because
        // the legacy parameter is typed `any` and its conversion to statement text dispatches on the runtime
        // type. An unset value arm means "no value supplied", which is NOT the same statement as an explicit
        // null, so it is refused rather than coerced - and the refusal quotes neither the name nor the value
        // (constraint C-F).
        foreach (PositionalParameter parameter in spec.Parameters)
        {
            if (!CarrierValue.TryFromWire(parameter.Value, out object? value))
            {
                return QuerySettingOutcome.Refused(RetCode.E_INVALID_ARGUMENT, InvalidParameterText);
            }

            applied = QuerySettingOutcome.From(task.SetParam(parameter.Name, value));

            if (!applied.IsAccepted)
            {
                return applied;
            }
        }

        return QuerySettingOutcome.Accepted;
    }
}
