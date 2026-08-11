// ==================================================================================================
//  Grpc/TransactionService.cs - CONTRACT C-08, persistence.v1.TransactionService
//  ------------------------------------------------------------------------------------------------
//  ROLE: A THIN ADAPTER, AND NOTHING MORE.
//  Validate, translate, delegate to Transactions/, map the result. Every behaviour this file appears
//  to have actually lives in a sibling folder:
//
//      the nine-field descriptor and its two folds  ->  Transactions/TransactionData.cs
//      reference counting, parking and idle expiry  ->  Transactions/TransactionPool.cs
//      connect, commit, rollback, liveness, dialect ->  Transactions/TransactionPool.cs
//                                                       (IPooledTransaction / PooledTransaction)
//      the driver-error payload and its redaction   ->  Errors/DbErrorData.cs, Errors/SqlRedactor.cs
//      the derived carrier syntax                   ->  Tasks/SqlQueryTask.cs
//                                                       (IQueryTransactionSurface)
//
//  What is genuinely THIS file's own is the boundary itself: the wire descriptor mapping, the
//  handle-to-reference-index correlation the legacy had no need for, and the one auditable place
//  where the in-process return-code algebra becomes the wire enum.
//
//  WHY THIS SERVICE IS gRPC (constraint C-K)
//  The legacy surface being ported is ACTION-ORIENTED AND THEREFORE RPC-SHAPED, NOT
//  RESOURCE-ORIENTED. Read the oracle's own public prototypes and the shape is unmistakable -
//  of_connect, of_disconnect, of_commit, of_rollback, of_autocommit, of_exec, of_isconnected,
//  of_setbroken, of_clearstate [ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:L73-L100].
//  Those are verbs on a session, not representations of a resource, so there is no collection to
//  GET and no entity to PUT. Three further properties push the same way: the database-error
//  structure requires a STRUCTURED payload rather than a message string
//  [ws_objects/pfw.thread.ext.pbl.src/dberrordata.srs:L4-L8], the thirteen SQL task classes carry
//  mandatory thread affinity in their own source comments, and the outcome model has to carry a
//  tri-state algebra rather than a boolean. Protocol buffers over gRPC carry all of it with
//  compile-time contract enforcement; JSON over REST would carry none of it well.
//
//  WHAT THIS FILE DELIBERATELY DOES NOT DO
//    * It declares NO route, NO health endpoint and NO ping endpoint. /health is this service's only
//      anonymous surface and both routes live in Endpoints/, on port 5101 (constraint C-L).
//    * It adds NO <Protobuf> item to the project. shared/PowerFramework.Contracts owns the single
//      GrpcServices="Both" item; a second would compile the messages twice (constraint C-A).
//    * It references NO peer service project and exposes NO shared behaviour across the boundary.
//      The published contracts project is the only permitted cross-service coupling (constraint C-A).
//    * It mints NO token and holds NO signing key. Security is the sole issuer; this service holds
//      verification material only (constraints C-F, C-G).
//    * It opens NO connection to SQL Server or Oracle. The two DatabaseType values select PURE
//      STRING TRANSFORMS under Sql/Paging/ and nothing else (constraint C-E).
//    * It declares NO route, handler, options type or placeholder for DesignSystem, Documents,
//      Integration or ScriptBridge, and throws NO NotImplementedException. Those four reserved
//      routes are declarations on GATEWAY's routing table alone (constraint C-D).
//    * It DECLARES no SCREAMING_SNAKE constant. This folder is outside every naming-suppression glob
//      in the repository .editorconfig while TreatWarningsAsErrors is true, so the preserved legacy
//      identifiers are CONSUMED from PowerFramework.Shared.Kernel and from the generated contract
//      enums, never redeclared here.
//    * It re-implements NO status mapping and throws NO RpcException. persistence.v1.OperationStatus
//      is "the uniform outcome of every unary call in this file" by the contract's own words, so
//      every outcome - success, guard refusal, driver failure - is reported in ret_code. Nothing in
//      C-08 produces Aborted; that is C-06's concern.
//
//  ============ logpass IS WRITE-ONLY, AND THE ENFORCEMENT HERE IS STRUCTURAL (constraint C-F) =====
//  It arrives on BeginSessionRequest.descriptor and it leaves this process by exactly one route: into
//  the connection, through TransactionData's named inbound fold. It appears in NO response, NO log
//  record, NO exception message and NO ToString() output.
//
//  THE CONCRETE HAZARD THIS FILE IS WRITTEN AGAINST: a C# record's compiler-generated ToString()
//  PRINTS EVERY PROPERTY, so a single `$"... {descriptor}"` in a log call or an exception message
//  would disclose the password with nothing to warn the author. Two independent controls close it:
//    1. NO DESCRIPTOR IS EVER INTERPOLATED into a log or exception string anywhere below. Logging
//       names individual non-secret fields, one structured parameter at a time.
//    2. TransactionData overrides ToString() and PrintMembers so that logpass, dbparm and userparm
//       have no printing path at all - not even a redaction marker.
//  And the contract closes it a third time, independently: TransactionDescriptorView reserves slots
//  5, 6 and 9 permanently, so a response has nowhere on the wire to put any of the three.
//  ==========================================================================================
//
//  LEGACY SPECIFICATION (READ ONLY - never edited, never moved, never reformatted: constraint C-C)
//  Every behavioural claim in the comments below carries a ws_objects/** locator, because nothing
//  else in this repository can adjudicate it - the changelog stops at framework 3.0.7.2062 while the
//  commit history runs years later, and neither PowerBuilder project object would build as written.
//      ws_objects/pfw.thread.ext.pbl.src/transactiondata.srs             the nine-field structure
//      ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru          the session behaviour
//      ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans_pool.sru     reference counting
//      ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlbase.sru  the caller-side copies
//      ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru   the acquisition sequence
//      ws_objects/pfw.thread.ext.pbl.src/dberrordata.srs                 the driver-error structure
//      ws_objects/pfw.shared.pbl.src/retcode.sru                         the return-code algebra
//  Unqualified [:Lnnn] references inside a member's comments name n_cst_thread_trans.sru, which is
//  this contract's primary oracle; every reference to any other file is spelled in full.
//
//  PRESERVED SEMANTICS, EACH ANNOTATED AT ITS POINT OF REPRODUCTION (constraint C-B)
//      the nine-field order, boolean eighth and userparm ninth   ToDescriptor
//      userparm never copied, autocommit captured-then-erased    ToDescriptor
//      the one-based reference index and its bounds guard        BeginSession, EndSession
//      the acquisition sequence and its tri-state hole           BeginSession
//      the never-copied autocommit on the outbound accessor      GetTransactionData
//      rollback preserving all five statement status values      Rollback
//      rollback AND commit returning FAILED under autocommit     Rollback, Commit
//      auto-rollback defaulting to TRUE when unset               Commit
//      SQLCode-based predicates, where 100 reads as SUCCEEDED    GetSessionState
//      the ORACLE-substring dialect accessor, MSSQL as fallback  GetDatabaseType
//
//  NO RULES GOVERN THIS FILE. review_rules reports that no user rules were provided, which is not
//  latitude: the enterprise-standard baseline applies in their place - nullable reference types with
//  warnings as errors, no secret in source or settings, structured logging with the statement field
//  redacted, and versioned contracts as the only cross-service coupling.
// ==================================================================================================

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Grpc.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PowerFramework.Contracts.Common.V1;
using PowerFramework.Contracts.Persistence.V1;
using PowerFramework.Persistence.Authorization;
using PowerFramework.Persistence.Configuration;
using PowerFramework.Persistence.Errors;
using PowerFramework.Persistence.Runtime;
using PowerFramework.Persistence.Sql;
using PowerFramework.Persistence.Tasks;
using PowerFramework.Persistence.Transactions;

// The generated C-08 service base, reached through an alias for two reasons. First, the mandated
// class name below is the contract's own service name, so the bare name has to resolve to exactly
// one type and the alias is what guarantees which. Second, an alias makes the derivation visible at
// the class declaration instead of hiding a fully qualified name in it. This is the same shape the
// sibling adapter uses at services/dataservices-service/.../Grpc/DataWindowService.cs.
using GeneratedTransactionServiceBase =
    global::PowerFramework.Contracts.Persistence.V1.TransactionService.TransactionServiceBase;

// THE NAME RetCode COLLIDES ACROSS THE TWO CODE SPACES THIS FILE STRADDLES, AND BOTH ARE NEEDED.
//   * RetCode is PowerFramework.Shared.Kernel.RetCode, a static class of `long` constants. It is the
//     IN-PROCESS algebra - what every collaborator below returns and what every guard tests.
//   * WireRetCode is PowerFramework.Contracts.Common.V1.RetCode.Types.Value, the generated PROTO
//     ENUM. It is the WIRE form and it appears in exactly one place: TransactionWireCodes.
// Importing PowerFramework.Contracts.Common.V1 above brings the wrapper MESSAGE named RetCode into
// scope, so without these two aliases the bare name would be ambiguous at every use site.
using Predicates = PowerFramework.Shared.Kernel.Predicates;
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
/// none to <c>PowerFramework.Shared.Kernel</c>, so that the published boundary cannot become a
/// back door for shared behaviour. The consequence is that the numeric agreement between the generated
/// <c>common.v1.RetCode.Value</c> enum and
/// <c>shared/PowerFramework.Shared.Kernel/RetCode.cs</c> is checked by NOTHING: no compiler, no
/// analyzer, and no single-sided unit test. A divergence would be a silent wire-compatibility defect.
/// </para>
/// <para>
/// <b>This project is the first and only place both are referenced together</b>, which is what makes
/// the correspondence auditable here and nowhere else. Routing every conversion through one named
/// member - rather than scattering <c>(WireRetCode)</c> casts across thirteen handlers - is what lets
/// a reviewer check the invariant by reading one line. The value-for-value assertion belongs to
/// <c>PowerFramework.Persistence.Tests</c>, not here.
/// </para>
/// <para>
/// <b>Both spaces are generated from the same oracle</b>, which is why the conversion is a re-typing
/// rather than a mapping: every member of each was transcribed from
/// <c>ws_objects/pfw.shared.pbl.src/retcode.sru:L39-L79</c>, and the protocol definition lists the
/// originating line beside each of its members.
/// </para>
/// </remarks>
internal static class TransactionWireCodes
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
    /// [<c>retcode.sru:L39</c>]; the generated enum's underlying type is <see cref="int"/>, as
    /// protobuf enums always are. Narrowing first and re-typing second states both facts, whereas a
    /// single cast would hide the narrowing.
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
    /// A status whose <c>ret_code</c> is always populated and whose other two members are absent -
    /// no diagnostic text and no driver detail.
    /// </returns>
    /// <remarks>
    /// The absence of <c>db_error</c> is meaningful rather than incidental: the contract states it is
    /// present ONLY when the legacy would also have fired its database-error event, which is not the
    /// case for any of the guards this file reproduces.
    /// </remarks>
    internal static OperationStatus Status(long code) => new()
    {
        RetCode = ToWireRetCode(code),
    };

    /// <summary>
    /// Builds a status carrying an outcome code and the legacy's own diagnostic text.
    /// </summary>
    /// <param name="code">The in-process return code.</param>
    /// <param name="errorText">
    /// The diagnostic, treated as OPAQUE DISPLAY TEXT. It may be non-English and it is neither
    /// translated nor parsed: many in-scope legacy arms synthesize Chinese diagnostics, and the
    /// contract requires consumers to branch on <c>ret_code</c> instead of on this string.
    /// </param>
    /// <returns>A status with both members populated and no driver detail.</returns>
    /// <remarks>
    /// <b>Nothing secret can reach this parameter from this file.</b> Every call site below passes
    /// either a diagnostic produced by a collaborator or a fixed sentence that quotes no value; no
    /// descriptor, field value or connection string is ever formatted into it (constraint C-F).
    /// </remarks>
    internal static OperationStatus Status(long code, string errorText) => new()
    {
        RetCode = ToWireRetCode(code),
        ErrorText = errorText,
    };

    /// <summary>
    /// Builds a status carrying an outcome code and the driver-level detail beneath it.
    /// </summary>
    /// <param name="code">The in-process return code.</param>
    /// <param name="dbError">
    /// The in-process driver payload. Projected onto the wire by
    /// <c>Errors/SqlRedactor.cs</c>'s extension, which is the ONLY sanctioned producer of a wire
    /// <see cref="DbError"/> in this service.
    /// </param>
    /// <returns>
    /// A status with <c>ret_code</c> and <c>db_error</c> populated and no separate diagnostic text.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>THE STATEMENT FIELD IS MASKED BY THE PROJECTION AND NOT BY THIS FILE (constraint C-F).</b>
    /// <c>ToDbError</c> reaches its redaction policy directly rather than taking one as an argument,
    /// so the masking cannot be weakened from a call site or from a container registration. That is
    /// stronger than a mandatory-redactor parameter would have been, and it is why no
    /// <c>ISqlRedactor</c> is injected into this service: an injected policy is a replaceable policy.
    /// </para>
    /// <para>
    /// <b><c>DbError.Sqlsyntax</c> is never assigned anywhere in this file.</b> The only way to emit
    /// unmasked statement text would be to read the in-process member and hand it somewhere directly,
    /// and no line below does. The masking matters here because the oracle's field carries the
    /// COMPLETE generated statement - and with <c>DisableBind=1</c> the runtime interpolates values as
    /// literals rather than binding them
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru:L128</c>], so that text can
    /// carry live row data while the legacy logger redacts nothing at all.
    /// </para>
    /// </remarks>
    internal static OperationStatus Status(long code, in DbErrorData dbError) => new()
    {
        RetCode = ToWireRetCode(code),
        DbError = dbError.ToDbError(),
    };
}

// --------------------------------------------------------------------------------------------------
//  PART 2 OF 4 - THE SESSION, AND EXPLICIT CORRELATION TO THE ONE-BASED REFERENCE INDEX
// --------------------------------------------------------------------------------------------------

/// <summary>
/// One server-held transaction session: the wire handle, the ONE-BASED pool reference index it
/// stands for, the descriptor that produced it, and the borrowed transaction it drives.
/// </summary>
/// <remarks>
/// <para>
/// <b>THE LEGACY HAD NO EQUIVALENT, WHICH IS WHY THIS TYPE IS NEW.</b> The oracle hands each task a
/// transaction DESCRIPTOR BY VALUE - <c>of_settransdata(readonly transactiondata)</c>
/// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru:L113</c>] - and the pool
/// resolves it to a connection by whole-descriptor equality
/// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans_pool.sru:L138</c>]. Reproducing that
/// literally across a network boundary would mean transmitting the whole descriptor, INCLUDING ITS
/// LOG-PASSWORD FIELD, on every query, update and command call - and therefore into every recording
/// of one. The contract resolves the descriptor ONCE and passes an opaque handle thereafter, which
/// changes the number of times credential material crosses the wire from "every call" to "once"
/// (constraint C-F).
/// </para>
/// <para>
/// <b>CORRELATION IS EXPLICIT AND NEVER AMBIENT (constraint C-J).</b> There is no thread-local, no
/// asynchronous-local and no per-connection implicit state anywhere in this service: a caller names
/// its session on every request, and this object is the only thing that maps that name onto a pool
/// index. Nothing here is portable across service instances, and the contract says so - a handle is
/// server-issued, unparseable, and NOT a credential, authorizing nothing.
/// </para>
/// <para>
/// <b>The reference index is ONE-BASED and is stored exactly as the pool issued it.</b> This is the
/// migration plan's named R9 hazard: PowerBuilder arrays are one-based and its upper-bound function
/// returns the LAST VALID INDEX, so <c>of_addref</c> appends at <c>nCount + 1</c> and returns that
/// [<c>n_cst_thread_trans_pool.sru:L143-L151</c>]. The value is never decremented, never adjusted and
/// never "normalised" on the way in or out.
/// </para>
/// </remarks>
internal sealed class TransactionSession
{
    /// <summary>
    /// Creates a session record.
    /// </summary>
    /// <param name="sessionId">The opaque, server-issued wire identity.</param>
    /// <param name="referenceIndex">
    /// The ONE-BASED pool reference index returned by <c>AddRef</c>. Stored verbatim.
    /// </param>
    /// <param name="descriptor">
    /// The nine-field descriptor this session was opened with. It carries the credential and is
    /// therefore never rendered, never logged and never projected onto a response - see the
    /// file-level note on write-only handling.
    /// </param>
    /// <param name="transaction">The borrowed pooled transaction this session drives.</param>
    /// <param name="gate">
    /// The mutual-exclusion gate for <paramref name="transaction"/>. Supplied by the registry rather
    /// than created here, because it must be SHARED by every session that borrowed the same
    /// transaction - see <see cref="Gate"/>.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="sessionId"/>, <paramref name="transaction"/> or <paramref name="gate"/> is
    /// <see langword="null"/>.
    /// </exception>
    internal TransactionSession(
        string sessionId,
        PoolLease lease,
        in TransactionData descriptor,
        IPooledTransaction transaction,
        TransactionGate gate)
    {
        SessionId = sessionId ?? throw new ArgumentNullException(nameof(sessionId));
        Lease = lease;
        Descriptor = descriptor;
        Transaction = transaction ?? throw new ArgumentNullException(nameof(transaction));
        Gate = gate ?? throw new ArgumentNullException(nameof(gate));
    }

    /// <summary>
    /// The STABLE pool handle this session holds, safe across the calls that name it.
    /// </summary>
    /// <remarks>
    /// A LEASE RATHER THAN THE ONE-BASED POOL POSITION, and the reason is that a session's whole purpose is
    /// to be named by a LATER request. The pool's removal renumbers every later position - a preserved
    /// legacy defect its positional API still reproduces - so a session holding an ordinal would be
    /// silently repointed at another session's transaction by an unrelated EndSession in between. A lease
    /// is never reused, so an outlived session resolves nothing rather than resolving a stranger.
    /// </remarks>
    internal PoolLease Lease { get; }

    /// <summary>
    /// Whether this session has begun retiring, so no further operation may touch its transaction.
    /// </summary>
    /// <remarks>
    /// <b>WRITTEN AND READ ONLY UNDER <see cref="Gate"/>, WHICH IS WHAT MAKES THE LIFECYCLE ATOMIC.</b>
    /// Retiring the handle from the registry is not enough on its own: a request that resolved the session
    /// a moment earlier is already past that check and would enter the gate AFTER the release and operate
    /// on a handed-back transaction. Because the flag is set inside the same gate the release happens in,
    /// and every operation tests it inside that gate before touching anything, the two cannot interleave.
    /// </remarks>
    internal bool IsClosing { get; private set; }

    /// <summary>
    /// Marks this session as retiring. The caller MUST already hold <see cref="Gate"/>.
    /// </summary>
    internal void MarkClosing() => IsClosing = true;

    /// <summary>
    /// The opaque wire identity of this session.
    /// </summary>
    internal string SessionId { get; }

    /// <summary>
    /// The descriptor this session was opened with, held for the outbound accessor to read from.
    /// </summary>
    /// <value>
    /// The full nine fields as supplied, credential included. Only the six-field non-credential fold
    /// is ever read out of it, and only onto a view type that has no slot for the other three.
    /// </value>
    internal TransactionData Descriptor { get; }

    /// <summary>
    /// The borrowed pooled transaction. Owned by the pool, never disposed by this service.
    /// </summary>
    internal IPooledTransaction Transaction { get; }

    /// <summary>
    /// The caller identity this session is attributed to, for the per-caller ceiling.
    /// </summary>
    /// <value>
    /// The token subject, or the resolver's unattributed bucket. Set once by the registry at
    /// registration and never rendered into a response.
    /// </value>
    /// <remarks>
    /// SETTABLE BY THE REGISTRY RATHER THAN A CONSTRUCTOR ARGUMENT, so that the identity - which is a
    /// property of the BOUNDARY the registry owns - does not enter the constructor of a record whose other
    /// five members are all the oracle's. Two call sites in this file construct sessions and neither is
    /// interested in a quota.
    /// </remarks>
    internal string Principal { get; set; } = HandlePrincipalResolver.Unattributed;

    /// <summary>
    /// When a call last named this session, as UTC ticks read from the injected clock.
    /// </summary>
    /// <remarks>
    /// WRITTEN AND READ THROUGH <see cref="System.Threading.Volatile"/> BY THE REGISTRY, because it is
    /// touched on every resolve from arbitrary request threads and read by the reclaim pass on another. A
    /// torn read of a 64-bit field is possible on a 32-bit runtime and would make a fresh handle look
    /// ancient.
    /// </remarks>
    internal long LastActivityTicks;

    /// <summary>
    /// Serializes every operation on <see cref="Transaction"/>, reproducing the oracle's thread
    /// affinity.
    /// </summary>
    /// <value>
    /// A mutual-exclusion gate held for the duration of one collaborator call. <b>KEYED ON THE
    /// TRANSACTION AND NOT ON THE SESSION</b>, so every session sharing one pooled connection shares
    /// one gate.
    /// </value>
    /// <remarks>
    /// <para>
    /// <b>THREAD AFFINITY IS CONTRACT, NOT COMMENTARY, AND FLATTENING IT WOULD BE A REGRESSION THE
    /// LEGACY COULD NOT HAVE HAD.</b> The oracle's concurrency design is a proxy pair in which every
    /// class exists twice - a caller-side <c>n_cst_threading*</c> and a worker-side
    /// <c>n_cst_thread*</c> - specifically so that NO OBJECT IS EVER TOUCHED FROM TWO THREADS, and the
    /// required execution context is recorded in the objects' own source comments. A gRPC server is
    /// concurrent by nature, so without this gate two simultaneous calls would touch one transaction
    /// object from two threads - something the oracle structurally prevents. This is the explicit
    /// marshalling boundary the migration plan requires in place of the proxy pair, not an
    /// optimisation and not a new capability.
    /// </para>
    /// <para>
    /// <b>WHY THE GATE CANNOT BE PER-SESSION, WHICH IS THE SUBTLE PART.</b> The pool is
    /// REFERENCE COUNTED and keys its entries on WHOLE-descriptor equality
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans_pool.sru:L136-L146</c>], so two
    /// sessions opened with equal descriptors receive the SAME reference index and are handed the SAME
    /// transaction object [<c>:L158-L172</c>]. A per-session gate would leave those two sessions
    /// racing on one object while appearing to be synchronized. The oracle has no such race because
    /// each pool lives on ONE worker thread - it is stored per-thread under a named framework datum
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru:L194-L206</c>] - so every
    /// referrer of an entry is on that same thread by construction. A process-wide pool has to
    /// reproduce that guarantee explicitly, and per-transaction is the granularity that does.
    /// </para>
    /// <para>
    /// <b>Keying on the reference index instead would be wrong for a second, independent reason:</b> the
    /// pool COMPACTS its array when an entry is removed [<c>n_cst_thread_trans_pool.sru:L101-L114</c>],
    /// so a live index can come to address a different entry. That is a preserved legacy hazard, and a
    /// gate keyed on the index would inherit it and mis-pair the exclusion. Object identity does not
    /// shift.
    /// </para>
    /// <para>
    /// The five-value state preservation around a rollback is the concrete case that makes all of this
    /// necessary: it saves, mutates and restores the statement status
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:L163-L183</c>], so a concurrent
    /// reader landing between the save and the restore would observe state no single-threaded caller
    /// could ever see.
    /// </para>
    /// <para>
    /// 🔴 <b>HOLDABLE ACROSS AN <see langword="await"/>, WHICH IS WHY IT IS NOT A
    /// <see cref="System.Threading.Lock"/>.</b> One of the operations that must hold it is
    /// asynchronous - C-05's <c>Query</c> streams its result, so its SQL execution and its result
    /// capture span continuations - and a <c>Lock</c> cannot be held across one. That is precisely how
    /// the streaming retrieval came to run UNGATED while every sibling operation was gated, and an
    /// operation outside the gate makes the gate meaningless for the ones inside it, because the object
    /// it protects is shared. See <c>Grpc/TransactionGate.cs</c> for the full reasoning.
    /// </para>
    /// </remarks>
    internal TransactionGate Gate { get; }
}

/// <summary>
/// The process-wide table mapping opaque session handles onto <see cref="TransactionSession"/>
/// records. Registered as a singleton; injected, never reached statically.
/// </summary>
/// <remarks>
/// <para>
/// <b>A SINGLETON IS REQUIRED RATHER THAN CONVENIENT.</b> gRPC service instances are resolved per
/// call, so a table held by the service itself would be discarded between
/// <c>BeginSession</c> and the first operation and every handle would be unknown on arrival. Holding
/// it here, injected, keeps the lifetime correct while leaving the correlation explicit
/// (constraint C-J) and leaving the service substitutable in a test (constraint C-H).
/// </para>
/// <para>
/// <b>This is the ONLY cross-call state in the service, and it holds nothing the pool does not
/// already hold.</b> Reference counting, parking and idle expiry stay entirely inside
/// <c>Transactions/TransactionPool.cs</c>, which is where the oracle keeps them
/// [<c>n_cst_thread_trans_pool.sru:L86-L226</c>]. This table adds only the handle-to-index mapping
/// that a network boundary needs and an in-process library did not.
/// </para>
/// <para>
/// <b>Ordinal comparison, deliberately.</b> A handle is an opaque server-issued token, so it is
/// matched byte for byte; culture-sensitive or case-insensitive matching would let two distinct
/// tokens collide on some hosts and not others.
/// </para>
/// </remarks>
internal sealed class TransactionSessionRegistry
{
    private readonly ConcurrentDictionary<string, TransactionSession> _sessions =
        new(StringComparer.Ordinal);

    /// <summary>The ceiling on live sessions, per caller and in total.</summary>
    private readonly HandleQuota _quota;

    /// <summary>The pool every session's teardown hands its reference back to.</summary>
    private readonly TransactionPool _pool;

    /// <summary>The one clock. Stamps activity and measures idleness.</summary>
    private readonly TimeProvider _time;

    /// <summary>Resolves the caller a new session is attributed to.</summary>
    private readonly HandlePrincipalResolver _principals;

    /// <summary>Optional structured logger, for reclaimed and drained sessions.</summary>
    private readonly ILogger<TransactionSessionRegistry>? _logger;

    /// <summary>
    /// One mutual-exclusion gate per BORROWED TRANSACTION, shared by every session that holds it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A weak-keyed table, so a gate never outlives the connection it guards.</b> The pool destroys
    /// an entry's transaction when its reference count reaches zero and keep-alive is off, or when a
    /// collection pass reaps a parked entry
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans_pool.sru:L104-L109</c>,
    /// <c>:L216-L219</c>]. A strong-keyed dictionary would hold every transaction the process ever
    /// borrowed alive forever; <see cref="ConditionalWeakTable{TKey, TValue}"/> holds its keys weakly
    /// and drops the pair as soon as the pool lets go, so the table's size tracks the pool's rather
    /// than the process's history.
    /// </para>
    /// <para>
    /// It compares keys by REFERENCE IDENTITY, which is exactly the comparison wanted here: the point
    /// is to exclude concurrent access to one object, not to one value.
    /// <see cref="ConditionalWeakTable{TKey, TValue}.GetValue"/> is atomic, so two sessions racing to
    /// take the first gate for one transaction receive the same gate.
    /// </para>
    /// </remarks>
    private readonly ConditionalWeakTable<IPooledTransaction, TransactionGate> _gates = [];

    /// <summary>
    /// Creates the registry over its ceilings, its clock and the pool its teardown returns references to.
    /// </summary>
    /// <param name="options">The bound settings the ceilings and the idle window come from.</param>
    /// <param name="time">The one clock, shared with the pool and the pooled transaction.</param>
    /// <param name="pool">
    /// The reference-counted pool. REQUIRED, because a session's teardown is a pool release: a registry
    /// that could remove a session without returning its reference would turn an abandoned handle into a
    /// pinned connection, which is the failure this bound exists to prevent.
    /// </param>
    /// <param name="principals">
    /// Resolves the caller a new session is attributed to. Optional so the registry is constructible
    /// without a host, in which case every session is unattributed and only the total ceiling applies.
    /// </param>
    /// <param name="logger">Optional structured logger.</param>
    /// <exception cref="ArgumentNullException">A required collaborator is <see langword="null"/>.</exception>
    /// <remarks>
    /// PUBLIC ON AN INTERNAL TYPE, WHICH IS NOT AN ACCESSIBILITY MISTAKE. The container activates this
    /// singleton through <c>ActivatorUtilities</c>, which considers only PUBLIC constructors - an internal
    /// one is invisible to it, and the failure is not a compile error but a startup exception naming a type
    /// with no suitable constructor. The type itself stays internal, so the effective reach is unchanged;
    /// only the activator can see the door. Every service class in this file is shaped the same way.
    /// </remarks>
    public TransactionSessionRegistry(
        IOptions<PersistenceOptions> options,
        TimeProvider time,
        TransactionPool pool,
        HandlePrincipalResolver? principals = null,
        ILogger<TransactionSessionRegistry>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        HandleLifecycleOptions handles = options.Value.Handles;

        _quota = new HandleQuota("transaction session", handles.MaxTotalPerRegistry, handles.MaxPerPrincipal);
        _pool = pool ?? throw new ArgumentNullException(nameof(pool));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _principals = principals ?? new HandlePrincipalResolver();
        _logger = logger;
    }

    /// <summary>
    /// The number of live sessions. Exposed for diagnostics and for assertions in tests.
    /// </summary>
    internal int Count => _sessions.Count;

    /// <summary>
    /// Issues a handle for a freshly acquired pool reference and records the session under it.
    /// </summary>
    /// <param name="referenceIndex">The ONE-BASED pool reference index, stored verbatim.</param>
    /// <param name="descriptor">The descriptor the session was opened with.</param>
    /// <param name="transaction">The borrowed pooled transaction.</param>
    /// <returns>The registered session, whose identity is the handle to return to the caller.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="transaction"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>The identity is a fresh version-4 GUID rendered without separators</b>, which satisfies the
    /// contract's opacity requirement: it is unparseable, it discloses nothing about the pool index
    /// behind it, and it is not guessable from another handle. Deriving it from the reference index or
    /// from a counter would make it both parseable and predictable.
    /// </para>
    /// <para>
    /// <b>It is NOT a clock read</b>, so it does not touch this service's determinism posture - version
    /// 4 GUIDs are random rather than time-based, and no wall clock is consulted anywhere in this file.
    /// A handle is nonetheless a non-deterministic value by construction, so a characterization
    /// comparison must mask it on BOTH sides, exactly as it must mask every other opaque identity.
    /// </para>
    /// <para>
    /// The insertion cannot collide in practice and is not permitted to overwrite in principle: a
    /// failure to add would mean a repeated GUID, which is a structural fault rather than a
    /// recoverable one, and the loop below simply re-issues rather than silently replacing a live
    /// session that some other caller still holds.
    /// </para>
    /// </remarks>
    internal TransactionSession? Register(
        PoolLease lease,
        in TransactionData descriptor,
        IPooledTransaction transaction,
        out string diagnostic)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        // THE CEILING IS TESTED BEFORE THE HANDLE IS MINTED, and a refusal registers nothing - so the
        // caller can hand its pool reference straight back. Reserving after minting would leave a live
        // entry to unwind on the refusal path, which is exactly the kind of unwinding that gets missed.
        string principal = _principals.Resolve();

        if (!_quota.TryReserve(principal, out diagnostic))
        {
            return null;
        }

        // One gate per borrowed transaction, created on first use and shared thereafter. Two sessions
        // opened with equal descriptors reach this line with the SAME transaction instance and so leave
        // it holding the SAME gate - which is the whole point; see TransactionSession.Gate.
        TransactionGate gate = _gates.GetValue(transaction, static _ => new TransactionGate());

        while (true)
        {
            TransactionSession session = new(
                Guid.NewGuid().ToString("N"),
                lease,
                in descriptor,
                transaction,
                gate)
            {
                Principal = principal,
            };

            Volatile.Write(ref session.LastActivityTicks, _time.GetUtcNow().UtcTicks);

            if (_sessions.TryAdd(session.SessionId, session))
            {
                return session;
            }
        }
    }

    /// <summary>
    /// Resolves a wire handle onto its session.
    /// </summary>
    /// <param name="handle">
    /// The handle from the request. <see langword="null"/>, an empty identity and an unknown identity
    /// are all treated identically - see the remarks.
    /// </param>
    /// <param name="session">The resolved session, or <see langword="null"/> when unresolved.</param>
    /// <returns>
    /// <see langword="true"/> when <paramref name="handle"/> names a live session.
    /// </returns>
    /// <remarks>
    /// <b>An absent handle, a blank handle and a stale handle are ONE outcome, not three.</b> The
    /// contract fixes the code for an unknown or already-ended session at
    /// <c>E_INVALID_TRANSACTION</c>, matching what the oracle returns when asked to act on a
    /// transaction it cannot use [<c>n_cst_thread_trans.sru:L113</c>]. Distinguishing the three would
    /// tell a caller which of its guesses was closer to a live handle, and it would answer a question
    /// the oracle has no answer for.
    /// </remarks>
    internal bool TryResolve(SessionHandle? handle, out TransactionSession? session) =>
        TryResolve(handle?.SessionId, out session);

    /// <summary>
    /// Resolves a session from its identity alone, without a wire handle to unwrap.
    /// </summary>
    /// <param name="sessionId">The identity to look up. A null or empty value never resolves.</param>
    /// <param name="session">The live session on success; <see langword="null"/> otherwise.</param>
    /// <returns><see langword="true"/> when a live session carries this identity.</returns>
    /// <remarks>
    /// THE ACTUAL LOOKUP, AND THE HANDLE OVERLOAD IS A THIN UNWRAP OVER IT. Callers inside the service
    /// hold a wire <see cref="SessionHandle"/> and use the overload above; callers in the composition
    /// root hold only the identity string a task request carried, and fabricating a wire message purely
    /// to index a dictionary would put a contract type into a place that has no wire concern at all.
    /// Both paths reach the same table and return the same three-outcomes-collapsed-to-one answer.
    /// </remarks>
    internal bool TryResolve(string? sessionId, out TransactionSession? session)
    {
        if (string.IsNullOrEmpty(sessionId))
        {
            session = null;
            return false;
        }

        if (!_sessions.TryGetValue(sessionId, out session))
        {
            return false;
        }

        // A HANDLE IN USE IS NOT AN ABANDONED HANDLE. The stamp is refreshed on every resolve, which is
        // every call that names this session, so the reclaim pass measures time since the caller was last
        // heard from rather than time since the session was opened.
        Volatile.Write(ref session.LastActivityTicks, _time.GetUtcNow().UtcTicks);

        return true;
    }

    /// <summary>
    /// Removes a session from the table, so that its handle is single-use.
    /// </summary>
    /// <param name="sessionId">The identity to remove.</param>
    /// <returns>
    /// <see langword="true"/> when this call is the one that removed it. Concurrent callers ending the
    /// same session therefore produce exactly one removal, and the loser sees the same unknown-session
    /// outcome as any other stale handle.
    /// </returns>
    internal bool TryRemove(string sessionId)
    {
        if (!_sessions.TryRemove(sessionId, out TransactionSession? removed) || removed is null)
        {
            return false;
        }

        // THE RESERVATION IS RETURNED BY WHICHEVER CALL WON THE REMOVAL, so a concurrent second release
        // cannot return it twice and a caller cannot free quota it never held.
        _quota.Release(removed.Principal);

        return true;
    }

    /// <summary>
    /// Hands a removed session's pool reference back - the teardown half of ending a session.
    /// </summary>
    /// <param name="session">The session, ALREADY removed from this table.</param>
    /// <returns>
    /// <c>OK</c>, <c>E_OUT_OF_BOUND</c> when the reference index no longer names a pool slot, or the
    /// first non-OK code the two pool operations produced.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="session"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// ONE IMPLEMENTATION, THREE CALLERS. <c>EndSession</c> reaches it for an explicit end, the reclaim
    /// pass for an abandoned session and the drain for shutdown. It lived inside <c>EndSession</c> until
    /// the reclaim pass needed it too, and copying it would have left three places for the pool protocol
    /// below to drift.
    /// </para>
    /// <para>
    /// THE TWO POOL OPERATIONS ARE THE ORACLE'S, IN ITS ORDER. <c>of_Release</c> hands the borrowed object
    /// back and disconnects only on the LAST reference
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru:L141</c>], and
    /// <c>of_RemoveRef</c> then drops the reference [<c>:L716</c>]. The second is performed even when the
    /// first reported a problem, because leaving the reference taken would pin the entry for the life of
    /// the process; the FIRST non-OK code is what the caller is told.
    /// </para>
    /// <para>
    /// THE INDEX GUARD IS NOT DEFENSIVE PADDING, AND BOTH HALVES OF IT MATTER. The oracle writes
    /// <c>index &lt;= 0 || index &gt; UpperBound</c> identically at three entry points - <c>of_removeref</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans_pool.sru:L89</c>], <c>of_release</c>
    /// [<c>:L120</c>] and <c>of_get</c> [<c>:L154</c>] - and all three answer <c>E_OUT_OF_BOUND</c>. It is
    /// checked here as well as inside the pool because the pool's own guard protects the POOL, while this
    /// one lets the boundary report the code for a session whose index was invalidated by ANOTHER session's
    /// departure: the pool COMPACTS its array on removal [<c>:L101-L114</c>], a preserved legacy hazard.
    /// </para>
    /// <para>
    /// INDICES ARE ONE-BASED AND ARE NEVER SILENTLY TRANSLATED HERE - the migration plan's named R9 hazard.
    /// PowerBuilder's upper bound is the LAST VALID INDEX of a one-based array, so for three entries it is
    /// 3 and the valid indices are 1, 2 and 3; reading it as a zero-based last index would make the highest
    /// entry unreachable, and subtracting one on the way in would make index 1 fail. Zero is therefore an
    /// ERROR here and not "the first entry".
    /// </para>
    /// </remarks>
    internal long Teardown(TransactionSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        // THE LEASE IS TESTED, NOT AN ORDINAL, AND THAT IS THE POINT OF THE HANDLE. The pool COMPACTS its
        // array on removal [<c>:L101-L114</c>] - a preserved legacy hazard - so an ordinal captured when the
        // session opened may by now name ANOTHER session's transaction. A lease is never reused, so an
        // outlived handle resolves nothing rather than resolving a stranger, and this guard reports the
        // contract's out-of-bound code for a handle that no longer names an entry.
        if (!session.Lease.IsValid)
        {
            return RetCode.E_OUT_OF_BOUND;
        }

        using (session.Gate.Enter())
        {
            IPooledTransaction? borrowed = session.Transaction;
            long released = _pool.Release(session.Lease, ref borrowed);
            long removed = _pool.RemoveRef(session.Lease);

            return released != RetCode.OK ? released : removed;
        }
    }

    /// <summary>
    /// Removes and tears down every session idle for longer than <paramref name="window"/> and not pinned
    /// by a live task.
    /// </summary>
    /// <param name="now">The reclaim pass's single clock read.</param>
    /// <param name="window">How long a session may go untouched before it is considered abandoned.</param>
    /// <param name="pinnedSessionIds">
    /// The sessions a live query, update or command task still names. Never reclaimed regardless of age.
    /// </param>
    /// <returns>How many sessions were reclaimed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="pinnedSessionIds"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// A PINNED SESSION IS NOT MERELY SKIPPED - IT IS NOT IDLE. A task working against a session is using
    /// that session, and the fact that no C-08 call has named it recently says nothing about whether it is
    /// abandoned. Reclaiming one would hand its transaction back underneath a working task, which would
    /// turn a cleanup into data loss.
    /// </remarks>
    internal int ReclaimIdle(DateTimeOffset now, TimeSpan window, IReadOnlySet<string> pinnedSessionIds)
    {
        ArgumentNullException.ThrowIfNull(pinnedSessionIds);

        long threshold = now.UtcTicks - window.Ticks;
        int reclaimed = 0;

        foreach (TransactionSession candidate in _sessions.Values)
        {
            if (pinnedSessionIds.Contains(candidate.SessionId)
                || Volatile.Read(ref candidate.LastActivityTicks) > threshold
                || !TryRemove(candidate.SessionId))
            {
                continue;
            }

            long code = Teardown(candidate);
            reclaimed++;

            _logger?.LogWarning(
                "Reclaimed an abandoned transaction session held by caller {Principal} against pool lease "
                + "{PoolLease}; the pool release answered {ReturnCode}. The session handle value is "
                + "deliberately not recorded.",
                candidate.Principal,
                candidate.Lease.Id,
                code);
        }

        return reclaimed;
    }

    /// <summary>
    /// Removes and tears down every live session, whatever its age.
    /// </summary>
    /// <returns>How many sessions were released.</returns>
    /// <remarks>
    /// FOR SHUTDOWN, AND IT MUST RUN AFTER THE TASK REGISTRIES HAVE DRAINED. A task borrows the transaction
    /// its session owns, so releasing sessions first would leave a task holding a returned reference. The
    /// reclaimer owns that ordering.
    /// </remarks>
    internal int Drain()
    {
        int drained = 0;

        foreach (TransactionSession candidate in _sessions.Values)
        {
            if (!TryRemove(candidate.SessionId))
            {
                continue;
            }

            _ = Teardown(candidate);
            drained++;
        }

        return drained;
    }
}

// --------------------------------------------------------------------------------------------------
//  PART 3 OF 4 - THE ADAPTER, ITS COLLABORATORS AND ITS TRANSLATIONS
// --------------------------------------------------------------------------------------------------

/// <summary>
/// Serves contract C-08, <c>persistence.v1.TransactionService</c>: session lifecycle, autocommit
/// control, commit, rollback, liveness, dialect resolution and state inspection over the nine-field
/// transaction descriptor.
/// </summary>
/// <remarks>
/// <para>
/// <b>EVERY METHOD IS AUTHORIZED, AND THE ENFORCEMENT IS DELIBERATELY NOT HERE (constraint C-G).</b>
/// <c>Program.cs</c> installs a fallback authorization policy requiring an authenticated principal, so
/// an endpoint with no authorization metadata of its own is a CLOSED door rather than an open one.
/// This class therefore carries no <c>[Authorize]</c> attribute - it would be redundant - and, more to
/// the point, it carries NO <c>[AllowAnonymous]</c> and no per-method escape of any kind. Inbound
/// tokens are validated by the stock bearer handler against Security's published key set; this service
/// holds VERIFICATION MATERIAL ONLY and no signing key appears anywhere in this file.
/// </para>
/// <para>
/// <b>EVERY COLLABORATOR ARRIVES BY CONSTRUCTOR INJECTION (constraint C-H).</b> Nothing is reached
/// statically, nothing is newed up internally, and no clock is read - which is what keeps the
/// per-service coverage gate reachable through substitution and what lets a fake time provider drive
/// the pool's idle expiry and the transaction's liveness cache deterministically from a test.
/// </para>
/// <para>
/// <b>NO WALL CLOCK IS READ IN THIS FILE.</b> There is no <c>DateTime.Now</c>, no
/// <c>Environment.TickCount</c> and no <c>Stopwatch</c>. The oracle has three clock reads in this
/// area - the pool's idle start time
/// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans_pool.sru:L97</c>], the
/// last-successful-connection stamp
/// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:L107</c> and <c>:L212</c>] and the
/// liveness cache [<c>:L198</c>] - and all three live behind the single injected
/// <see cref="TimeProvider"/> seam that the pool and the pooled transaction share. Adding a fourth
/// read here would put one clock outside the seam and silently break paired characterization
/// recordings.
/// </para>
/// </remarks>
// ====== THE ONE CONTRACT ANNOTATED PER RPC RATHER THAN PER CLASS (constraint C-G) ======
// C-08 straddles the read/write line, and a class-level policy would have to pick the wrong side
// for half its surface. Its session, descriptor and state READERS need only the read scope; its
// commit, rollback, auto-commit, clear-state and set-broken operations change durable state or the
// pooled object every later caller shares, and need the write scope. Each of the thirteen therefore
// carries its own attribute, and there is deliberately NO class-level attribute: adding one would
// AND itself with every method policy, so a write-scoped caller would also have to hold read.
internal sealed class TransactionService : GeneratedTransactionServiceBase
{
    private readonly TransactionPool _pool;
    private readonly TransactionSessionRegistry _sessions;

    /// <summary>The query-task handle table, purged when a session ends.</summary>
    private readonly QueryTaskRegistry _queryTasks;

    /// <summary>The update-task handle table, purged when a session ends.</summary>
    private readonly UpdateTaskRegistry _updateTasks;

    /// <summary>The command-task handle table, purged when a session ends.</summary>
    private readonly CommandTaskRegistry _commandTasks;
    private readonly IQueryTransactionSurface _querySurface;
    private readonly ILogger<TransactionService>? _logger;

    /// <summary>
    /// Creates the adapter over its four collaborators.
    /// </summary>
    /// <param name="pool">
    /// The reference-counted transaction pool - the port of
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans_pool.sru</c>. It owns reference
    /// counting, parking, idle expiry and the clock seam; this service only calls into it.
    /// </param>
    /// <param name="sessions">The handle-to-reference-index table. Must be the process singleton.</param>
    /// <param name="querySurface">
    /// The transaction-scoped seam that derives a carrier syntax from a statement - the existing port of
    /// <c>of_gridsyntaxfromsql</c> [<c>n_cst_thread_trans.sru:L81</c>, <c>:L90</c>, <c>:L282-L290</c>].
    /// <b>ONLY its syntax-derivation member is consumed here</b>; retrieval and counting belong to C-05
    /// and are never called from this service.
    /// <para>
    /// <b>REUSED RATHER THAN DUPLICATED, DELIBERATELY.</b> The function being ported is a member of the
    /// legacy TRANSACTION object, not of a task, so it belongs on this contract - and the port of it
    /// already exists on <c>Tasks/</c>'s seam, taking a pooled transaction as its argument precisely so
    /// it can be driven against any session. Declaring a second single-member interface here for the
    /// same operation would put two abstractions over one function, give the container two registrations
    /// to keep in step, and create two places for the derivation to drift. Consuming the one that exists
    /// is the choice that keeps a single implementation of a single legacy member.
    /// </para>
    /// </param>
    /// <param name="logger">
    /// Optional structured logger. Optional rather than required so a unit test can construct the
    /// service with nothing but its behavioural collaborators.
    /// </param>
    /// <exception cref="ArgumentNullException">A required collaborator is <see langword="null"/>.</exception>
    /// <remarks>
    /// <b>NO <c>ISqlRedactor</c> IS INJECTED, AND THAT IS THE STRONGER CHOICE (constraint C-F).</b>
    /// The sanctioned projection in <c>Errors/SqlRedactor.cs</c> reaches its redaction policy directly
    /// rather than accepting one, precisely so the masking of the statement field cannot be weakened
    /// from a call site or from a container registration. Injecting a redactor here would make the
    /// policy replaceable, which is the opposite of the guarantee.
    /// </remarks>
    public TransactionService(
        TransactionPool pool,
        TransactionSessionRegistry sessions,
        IQueryTransactionSurface querySurface,
        QueryTaskRegistry queryTasks,
        UpdateTaskRegistry updateTasks,
        CommandTaskRegistry commandTasks,
        ILogger<TransactionService>? logger = null)
    {
        _pool = pool ?? throw new ArgumentNullException(nameof(pool));
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _querySurface = querySurface ?? throw new ArgumentNullException(nameof(querySurface));
        _queryTasks = queryTasks ?? throw new ArgumentNullException(nameof(queryTasks));
        _updateTasks = updateTasks ?? throw new ArgumentNullException(nameof(updateTasks));
        _commandTasks = commandTasks ?? throw new ArgumentNullException(nameof(commandTasks));
        _logger = logger;
    }

    // ----------------------------------------------------------------------------------------------
    //  THE DESCRIPTOR MAPPING - THE HEART OF THIS FILE
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Maps the wire descriptor onto the in-process descriptor, field for field, IN THE ORACLE'S OWN
    /// DECLARATION ORDER.
    /// </summary>
    /// <param name="wire">The request-side descriptor. Carries the credential.</param>
    /// <returns>The nine-field in-process descriptor.</returns>
    /// <remarks>
    /// <para>
    /// <b>NINE FIELDS, IN THE ORDER <c>transactiondata.srs</c> DECLARES THEM AT [:L4-L12].</b>
    /// EIGHT STRINGS PLUS ONE BOOLEAN, and the assignment order below is the structure's order rather
    /// than any convenient regrouping:
    /// </para>
    /// <code>
    ///   string   dbms        [:L4]     string   dbparm      [:L9]
    ///   string   servername  [:L5]     string   lock        [:L10]
    ///   string   database    [:L6]     boolean  autocommit  [:L11]   EIGHTH
    ///   string   logid       [:L7]     string   userparm    [:L12]   NINTH
    ///   string   logpass     [:L8]
    /// </code>
    /// <para>
    /// <b>THE BOOLEAN IS EIGHTH AND <c>userparm</c> IS NINTH - not the other way round.</b> A trailing
    /// boolean is the more natural layout and is therefore the ordering a reader is most likely to get
    /// wrong. Order is part of the contract: the protocol definition numbers the two fields 8 and 9 to
    /// match, so a transposition here would silently swap two wire slots. Verified field for field
    /// against the generated message rather than assumed.
    /// </para>
    /// <para>
    /// <b>LOCATOR CONVENTION, NARROWED FOR THIS MEMBER ONLY.</b> Elsewhere in this file an unqualified
    /// <c>[:Lnnn]</c> names <c>n_cst_thread_trans.sru</c>. Inside the nine-field list below it names
    /// <c>transactiondata.srs</c> instead, because that is the file the field list mirrors and repeating
    /// its name on all nine lines would bury the numbering. Every reference to any OTHER oracle file in
    /// this member - including the two seven-field accessor ranges in <c>n_cst_thread_trans.sru</c> - is
    /// spelled in full, so nothing here is ambiguous.
    /// </para>
    /// <para>
    /// ============ THE COPY ASYMMETRY IS REPRODUCED AND MUST NOT BE "COMPLETED" (constraint C-B) ====
    /// </para>
    /// <para>
    /// Three distinct copy shapes were measured in the oracle, and they disagree with each other:
    /// </para>
    /// <list type="number">
    /// <item>
    /// <description>
    /// <b>SEVEN fields, in BOTH directions.</b> <c>of_settransdata</c>
    /// [<c>n_cst_thread_trans.sru:L343-L354</c>] and <c>of_gettransdata</c> [<c>:L402-L419</c>] each
    /// move dbms, servername, database, logid, logpass, dbparm and lock - and touch NEITHER
    /// <c>autocommit</c> NOR <c>userparm</c>.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <b>EIGHT fields</b> when the caller-side proxy copies from a raw transaction object
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlbase.sru:L76-L83</c>] - the same
    /// seven PLUS <c>autocommit</c> at <c>:L83</c>, still excluding <c>userparm</c>.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <b>SIX fields</b> in the six-string overload [<c>n_cst_threading_task_sqlbase.sru:L97-L102</c>]
    /// - no lock, no autocommit, no userparm.
    /// </description>
    /// </item>
    /// </list>
    /// <para>
    /// <b>THE NET INVARIANT: <c>userparm</c> IS NEVER COPIED BY ANYTHING, ANYWHERE. And
    /// <c>autocommit</c> MAY BE CAPTURED INTO THE DESCRIPTOR BUT IS NEVER APPLIED OR READ BACK BY THE
    /// TRANSACTION OBJECT'S OWN TRANSFER.</b> A third, independent quirk on the same field makes the
    /// second half concrete: when a SQL task is handed a descriptor it ERASES the flag outright -
    /// <c>_transData.AutoCommit = false</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru:L119</c>], under a comment
    /// at <c>:L118</c> stating that parameters irrelevant to the connection target are wiped. So the
    /// oracle itself treats the flag as a SESSION CONTROL rather than part of the connection identity,
    /// and per-session autocommit is set through <c>SetAutoCommit</c> instead.
    /// </para>
    /// <para>
    /// <b>Both members are nonetheless carried here, because THE STRUCTURE is what is being mirrored.</b>
    /// A consumer setting either through the descriptor is expressing something the structure can hold,
    /// and the pool keys its reference-counted entries on WHOLE-descriptor equality
    /// [<c>n_cst_thread_trans_pool.sru:L138</c>] - so two sessions differing only in
    /// <c>userparm</c> are two distinct pool entries even though nothing ever reads the field. Dropping
    /// either member to match the accessors would change pooling identity.
    /// </para>
    /// <para>
    /// <b>THE SEVEN-FIELD TRANSFER IS NOT PERFORMED HERE, AND IS NOT RE-IMPLEMENTED.</b> This function
    /// stops at building the nine-field value. The seven-field fold is
    /// <c>TransactionData.WithConnectionFieldsFrom</c>, and the pool applies it when it materializes
    /// the entry [<c>n_cst_thread_trans_pool.sru:L171</c>] by way of
    /// <c>IPooledTransaction.ApplyTransactionData</c>. This service must therefore NOT apply it a
    /// second time - doing so would be a duplicate transfer the oracle does not perform.
    /// </para>
    /// <para>
    /// <b>The credential travels INBOUND only.</b> It is assigned here from the request and it leaves
    /// this process only into the connection; the descriptor is never rendered, never logged and never
    /// projected onto a response.
    /// </para>
    /// </remarks>
    private static TransactionData ToDescriptor(TransactionDescriptor wire) => new()
    {
        // 1 - string dbms [transactiondata.srs:L4]. ALSO THE DIALECT SELECTOR - see GetDatabaseType.
        Dbms = wire.Dbms,

        // 2 - string servername [:L5]
        ServerName = wire.Servername,

        // 3 - string database [:L6]
        Database = wire.Database,

        // 4 - string logid [:L7]. An ACCOUNT NAME, not a secret, so the response view carries it too.
        LogId = wire.Logid,

        // 5 - string logpass [:L8]. WRITE-ONLY. Assigned here and never read back out: the response
        //     view has no slot for it, no log statement below names it, and the in-process type has no
        //     printing path for it. This is the only line in the file that touches it.
        LogPass = wire.Logpass,

        // 6 - string dbparm [:L9]. Carries the two connection flags the oracle extracts by regular
        //     expression [n_cst_thread_task_sqlbase.sru:L128-L129]. Treated as sensitive: it exists on
        //     the request shape only, and slot 6 of the response view is permanently reserved.
        DbParm = wire.Dbparm,

        // 7 - string lock [:L10]. The isolation-level STRING, not a synchronization object.
        Lock = wire.Lock,

        // 8 - boolean autocommit [:L11]. EIGHTH, not last. CAPTURED HERE AND NEVER APPLIED BY THE
        //     TRANSACTION OBJECT'S OWN TRANSFER: neither seven-field accessor moves it
        //     [n_cst_thread_trans.sru:L343-L354 and :L402-L419], and the task layer erases it outright
        //     [n_cst_thread_task_sqlbase.sru:L119]. Preserved, not corrected (C-B).
        AutoCommit = wire.Autocommit,

        // 9 - string userparm [:L12]. NINTH and last. NEVER COPIED BY ANYTHING, ANYWHERE in the
        //     oracle - not by the seven-field pair, not by the eight-field proxy copy, not by the
        //     six-string overload. It is carried because whole-descriptor equality keys the pool
        //     [n_cst_thread_trans_pool.sru:L138], so it participates in pooling identity while no
        //     accessor ever reads it. Do NOT "complete" the mapping by teaching anything to copy it.
        UserParm = wire.Userparm,
    };

    /// <summary>
    /// Projects a session's descriptor onto the RESPONSE-side view, which structurally cannot carry a
    /// credential.
    /// </summary>
    /// <param name="outbound">
    /// The descriptor produced by the outbound accessor - six non-credential connection fields taken
    /// from the session's own descriptor.
    /// </param>
    /// <returns>The view, plus the typed non-sensitive projection of the connection-parameter string.</returns>
    /// <remarks>
    /// <para>
    /// <b>THREE SLOTS DO NOT EXIST ON THIS TYPE AT ALL.</b> The view reserves 5, 6 and 9 permanently -
    /// <c>logpass</c> because it is a password, <c>dbparm</c> because a provider connection string
    /// routinely carries credentials, and <c>userparm</c> because the oracle describes it as free-form
    /// and it can therefore carry anything a caller put there. Reserving the numbers AND the names
    /// makes reintroducing any of them a compile error rather than a review miss, so this function
    /// cannot leak them even by mistake (constraint C-F).
    /// </para>
    /// <para>
    /// <b><c>autocommit</c> IS REPORTED AS THE OUTBOUND ACCESSOR PRODUCED IT, WHICH MEANS IT DOES NOT
    /// REFLECT THE LIVE FLAG - AND THAT IS THE ORACLE'S OWN BEHAVIOUR (constraint C-B).</b>
    /// <c>of_gettransdata</c> copies SEVEN fields and <c>autocommit</c> is not among them
    /// [<c>n_cst_thread_trans.sru:L410-L416</c>], so the caller's structure keeps whatever it already
    /// held - and for the freshly declared structure the oracle passes in [<c>:L395</c>] that is the
    /// cleared value. Reading the live flag off the connection instead would be more useful and would
    /// be exactly the behaviour improvement that is forbidden: the oracle has NO accessor that reports
    /// it. A caller wanting live session state calls <c>GetSessionState</c>, which reports what the
    /// oracle's own predicates report.
    /// </para>
    /// <para>
    /// <b>The flags are the sanctioned replacement for the withheld connection string</b>, and they are
    /// derived by the descriptor's own single-pass resolver rather than re-parsed here - so the nesting
    /// rule is inherited rather than restated: the national-character flag is consulted ONLY inside the
    /// bind-disabling branch [<c>n_cst_thread_task_sqlbase.sru:L127-L132</c>], which means
    /// <c>nchar_bind</c> true on its own is legal and has no effect.
    /// </para>
    /// </remarks>
    private static TransactionDescriptorView ToView(in TransactionData outbound)
    {
        outbound.ResolveDbParmFlags(out bool isBindDisabled, out bool isNCharBindingEnabled);

        return new TransactionDescriptorView
        {
            // Field numbers 1, 2, 3, 4, 7 and 8 - aligned with the request-side descriptor so the
            // correspondence to the oracle stays checkable at a glance and the forbidden slots are
            // visibly, deliberately empty rather than mysteriously missing.
            Dbms = outbound.Dbms,
            Servername = outbound.ServerName,
            Database = outbound.Database,
            Logid = outbound.LogId,
            Lock = outbound.Lock,
            Autocommit = outbound.AutoCommit,

            // Field 10 - the typed, closed, non-sensitive allowlist that replaces the withheld
            // connection string. A consumer no longer has to re-implement the oracle's regular
            // expressions over an opaque string to learn whether binding was disabled.
            Flags = new ConnectionParameterFlags
            {
                DisableBind = isBindDisabled,
                NcharBind = isNCharBindingEnabled,
            },
        };
    }

    // ----------------------------------------------------------------------------------------------
    //  THE GUARDS - VALIDATE, THEN DELEGATE. NOTHING BELOW DECIDES AN OUTCOME ON ITS OWN.
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// The one sentence returned when a request names no session, an empty session, or a session that
    /// has already ended.
    /// </summary>
    /// <remarks>
    /// A fixed sentence that quotes no value, so it can never disclose a handle, a descriptor field or
    /// a connection string (constraint C-F).
    /// </remarks>
    private const string UnknownSessionDiagnostic =
        "The request named no live transaction session. A session handle is issued by BeginSession, is "
        + "valid only on the instance that issued it, and is single-use: EndSession retires it.";

    /// <summary>
    /// The one sentence returned when a caller-supplied statement is not something a read-scoped caller
    /// may ask this service to prepare.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>IT NAMES THE RULE AND NEVER THE REJECTED TEXT (constraint C-F).</b> Echoing the statement would
    /// put a caller-authored value into a field documented as opaque display text; naming the rule tells a
    /// caller what to change without quoting anything, and because it is a fixed sentence it passes
    /// through <c>Errors/SqlRedactor.cs</c> byte for byte.
    /// </para>
    /// <para>
    /// The wording is deliberately the same rule as C-05's, because it IS the same rule with the same
    /// implementation behind it - <c>Sql/ReadOnlyStatementGuard.cs</c>. Two differently worded statements
    /// of one grammar would be two things to drift.
    /// </para>
    /// </remarks>
    private const string InadmissibleStatementDiagnostic =
        "The supplied statement is not admissible on this contract. Syntax derivation is published under "
        + "the read scope, so it accepts ONE statement beginning with SELECT, WITH or VALUES, and refuses "
        + "data manipulation, data definition, PRAGMA, ATTACH, DETACH, transaction control, procedure "
        + "execution, extension loading, SQL comments and any second statement after a semicolon. Send "
        + "state-changing statements to the write-scoped C-07 command contract instead. The rejected text "
        + "is deliberately not quoted here.";

    /// <summary>
    /// The one sentence returned when the caller's request was cancelled before this service issued the
    /// connect that <c>BeginSession</c> exists to issue.
    /// </summary>
    /// <remarks>
    /// A fixed sentence quoting no value, for the same reason as
    /// <see cref="UnknownSessionDiagnostic"/> (constraint C-F).
    /// </remarks>
    private const string CancelledDiagnostic =
        "The request was cancelled before a connection was opened. No pool reference is held and no "
        + "session was created, so there is nothing for the caller to end.";

    /// <summary>
    /// Checks a stored reference index against the pool's current bounds, reproducing the guard the
    /// oracle repeats at every pool entry point.
    /// </summary>
    /// <param name="referenceIndex">The ONE-BASED index held by the session.</param>
    /// <returns>
    /// <c>RetCode.OK</c> when the index is in range, otherwise <c>RetCode.E_OUT_OF_BOUND</c>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>THE GUARD IS <c>index &lt;= 0 || index &gt; UpperBound</c>, AND BOTH HALVES MATTER.</b> The
    /// oracle writes it identically at three entry points -
    /// <c>of_removeref</c> [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans_pool.sru:L89</c>],
    /// <c>of_release</c> [<c>:L120</c>] and <c>of_get</c> [<c>:L154</c>] - and all three return
    /// <c>E_OUT_OF_BOUND</c>.
    /// </para>
    /// <para>
    /// <b>INDICES ARE ONE-BASED AND ARE NEVER SILENTLY TRANSLATED AT THIS BOUNDARY.</b> This is the
    /// migration plan's named R9 hazard. PowerBuilder's upper bound is the LAST VALID INDEX of a
    /// one-based array, so for three entries it is 3 and the valid indices are 1, 2 and 3 - reading it
    /// as a zero-based last index would make the highest entry unreachable, and subtracting one on the
    /// way in would make index 1 fail. Zero is therefore an ERROR here and not "the first entry".
    /// </para>
    /// <para>
    /// It is checked here as well as inside the pool deliberately: the pool's own guard protects the
    /// pool, and this one lets the boundary report <c>E_OUT_OF_BOUND</c> for a session whose index has
    /// been invalidated by another session's departure - the pool COMPACTS its array on removal
    /// [<c>:L101-L114</c>], a preserved legacy hazard, so a live handle's index can fall out of range
    /// without this session doing anything at all.
    /// </para>
    /// </remarks>
    private long GuardLease(PoolLease lease) =>
        !lease.IsValid || !_pool.IsLeaseLive(lease) ? RetCode.E_OUT_OF_BOUND : RetCode.OK;

    /// <summary>
    /// Refuses a request whose explicit connection flags disagree with what the supplied
    /// connection-parameter string actually yields.
    /// </summary>
    /// <param name="descriptor">The descriptor built from the request.</param>
    /// <param name="requested">The explicit flags, or <see langword="null"/> when the caller sent none.</param>
    /// <param name="diagnostic">The refusal text, or the empty string on agreement.</param>
    /// <returns>
    /// <c>RetCode.OK</c> when the flags are absent or agree, otherwise
    /// <c>RetCode.E_INVALID_ARGUMENT</c>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>WHY A DISAGREEMENT IS REFUSED RATHER THAN RESOLVED SILENTLY IN EITHER DIRECTION.</b> Both
    /// flags are DERIVED from the connection-parameter string by the oracle's own regular expressions
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru:L128-L129</c>], and nothing
    /// in the port overrides that derivation - the string is what the connection is opened with and is
    /// part of the pool's whole-descriptor identity. So honouring an explicit flag that disagrees would
    /// require REWRITING the caller's connection string, and ignoring it would let a caller believe it
    /// had disabled binding when it had not.
    /// </para>
    /// <para>
    /// Either of those is a silent divergence in GENERATED STATEMENT TEXT, which is precisely what the
    /// parity criterion measures. The migration plan's rule for exactly this case is to NARROW the
    /// contract with a DEFINED ERROR rather than widen it with a guess, so the disagreement is surfaced
    /// at the boundary where a caller can see and fix it.
    /// </para>
    /// <para>
    /// <b>Agreement is evaluated through the descriptor's own single-pass resolver</b>, so the nesting
    /// rule is inherited rather than restated: the national-character flag is read ONLY inside the
    /// bind-disabling branch [<c>:L127-L132</c>], which means <c>nchar_bind</c> true with
    /// <c>disable_bind</c> false resolves to false and a caller sending that pair is refused - exactly
    /// as it should be, because the oracle would silently have ignored the second flag.
    /// </para>
    /// </remarks>
    private static long GuardConnectionFlags(
        in TransactionData descriptor,
        ConnectionParameterFlags? requested,
        out string diagnostic)
    {
        diagnostic = string.Empty;

        if (requested is null)
        {
            // Absent means "take them from the connection-parameter string by the legacy's own regular
            // expressions", which is what the descriptor already does.
            return RetCode.OK;
        }

        descriptor.ResolveDbParmFlags(out bool isBindDisabled, out bool isNCharBindingEnabled);

        if (requested.DisableBind == isBindDisabled && requested.NcharBind == isNCharBindingEnabled)
        {
            return RetCode.OK;
        }

        // The message names the two flags and reports only booleans - never the connection-parameter
        // string itself, which is treated as sensitive (constraint C-F).
        diagnostic =
            "The explicit connection flags disagree with the flags the supplied connection-parameter "
            + "string resolves to, and this service derives both from that string exactly as the legacy "
            + "does, so neither reading can be honoured without changing the other. Requested "
            + $"disable_bind={requested.DisableBind} nchar_bind={requested.NcharBind}; the string "
            + $"resolves to disable_bind={isBindDisabled} nchar_bind={isNCharBindingEnabled}. Note that "
            + "nchar_bind is consulted only when disable_bind is set. Send the flags matching the "
            + "string, or omit them.";

        return RetCode.E_INVALID_ARGUMENT;
    }

    /// <summary>
    /// Refuses a request whose pool settings ask for a configuration this instance's pool is not
    /// running.
    /// </summary>
    /// <param name="requested">The requested settings, or <see langword="null"/> when the caller sent none.</param>
    /// <param name="diagnostic">The refusal text, or the empty string on agreement.</param>
    /// <returns>
    /// <c>RetCode.OK</c> when the settings are absent or agree, otherwise
    /// <c>RetCode.E_INVALID_ARGUMENT</c>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>THE POOL IS A PROCESS SINGLETON CONFIGURED AT STARTUP, SO A PER-REQUEST SETTING CANNOT
    /// RECONFIGURE IT.</b> That mirrors the oracle, which reads all three settings ONCE at
    /// initialization from named framework data
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans_pool.sru:L76-L83</c>] and never
    /// re-reads them. Silently discarding a supplied setting would be an invisible behaviour change -
    /// the responses would all look correct while parking, expiry or liveness caching behaved
    /// differently from what the caller asked for - so a disagreement is refused with a defined error
    /// instead.
    /// </para>
    /// <para>
    /// <b>Each comparison reproduces the oracle's own conditionality rather than testing every field
    /// unconditionally.</b> The expiry window and the transaction class are read INSIDE the keep-alive
    /// branch [<c>:L76-L83</c>], so when keep-alive is off they are not consulted at all and a
    /// disagreement in them is not a disagreement in behaviour. The expiry conversion is delegated to
    /// <see cref="TransactionPoolOptions.ResolveKeepAliveExpireMilliseconds"/> rather than re-derived,
    /// which is what carries the oracle's "a non-positive value means use the default" convention
    /// [<c>:L78-L79</c>, default at <c>:L53</c>] into the comparison. The class name is compared only
    /// when non-empty, because the oracle falls back to configuration on null-or-empty [<c>:L83</c>].
    /// </para>
    /// <para>
    /// <b>The liveness-cache window is the one setting a parity run most wants and the one this build
    /// cannot vary.</b> The contract's rule is that an absent window means the server's configured
    /// value, a non-positive window means never answer from the cache, and a positive window replaces
    /// the configured one - but the pooled transaction holds the oracle's window as a fixed value
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:L198</c>], so only that value can
    /// be honoured. An explicit window equal to it is accepted; anything else is refused by name, so
    /// the caller learns immediately instead of receiving cached liveness answers it believed it had
    /// disabled.
    /// </para>
    /// </remarks>
    private long GuardPoolSettings(PoolKeepAliveSettings? requested, out string diagnostic)
    {
        diagnostic = string.Empty;

        if (requested is null)
        {
            // Absent means "use the server's configured values, which default to the legacy's own".
            return RetCode.OK;
        }

        if (requested.KeepAlive != _pool.IsKeepAliveEnabled)
        {
            diagnostic =
                "The requested keep_alive setting disagrees with this instance's transaction pool, "
                + "which reads it once at startup exactly as the legacy pool does. Requested "
                + $"keep_alive={requested.KeepAlive}; configured keep_alive={_pool.IsKeepAliveEnabled}. "
                + "Configure the pool through the TransactionPool options section, or omit the setting.";

            return RetCode.E_INVALID_ARGUMENT;
        }

        // [:L76-L83] The window and the class name are read INSIDE the keep-alive branch, so with
        // keep-alive off neither is consulted and neither can disagree in behaviour.
        if (requested.KeepAlive)
        {
            // The conversion and the non-positive fallback are the options type's, not this file's.
            int requestedExpiry = new TransactionPoolOptions
            {
                KeepAliveExpireSeconds = requested.KeepAliveExpireSeconds,
            }.ResolveKeepAliveExpireMilliseconds();

            if (requestedExpiry != _pool.KeepAliveExpireMilliseconds)
            {
                diagnostic =
                    "The requested keep-alive expiry disagrees with this instance's transaction pool. "
                    + $"Requested {requestedExpiry} ms; configured "
                    + $"{_pool.KeepAliveExpireMilliseconds} ms. A non-positive value on the request "
                    + "means \"use the default\", matching the legacy. Configure the pool through the "
                    + "TransactionPool options section, or omit the setting.";

                return RetCode.E_INVALID_ARGUMENT;
            }

            // [:L83] Compared only when non-empty, because the oracle falls back to configuration on
            // null-or-empty rather than treating the empty string as a request for an unnamed class.
            if (!string.IsNullOrEmpty(requested.TransactionClass)
                && !string.Equals(
                    requested.TransactionClass,
                    _pool.TransactionClassName,
                    StringComparison.Ordinal))
            {
                diagnostic =
                    "The requested transaction class disagrees with this instance's transaction pool, "
                    + "which resolves the class once at startup. Configure it through the "
                    + "TransactionPool options section, or omit the setting.";

                return RetCode.E_INVALID_ARGUMENT;
            }
        }

        // Presence is read BEFORE the value, because absence and an explicit zero are the two OPPOSITE
        // ends of this field's range and a plain proto3 integer could not tell them apart.
        if (requested.HasLivenessCacheWindowMs
            && requested.LivenessCacheWindowMs != PooledTransaction.LivenessCacheWindowMilliseconds)
        {
            diagnostic =
                "The requested liveness-cache window cannot be honoured: this service keeps the "
                + "legacy window of "
                + $"{PooledTransaction.LivenessCacheWindowMilliseconds} ms, so a connection is reported "
                + "as live without being probed while the last successful connection is that recent. "
                + $"Requested {requested.LivenessCacheWindowMs} ms. Omit the field to accept the legacy "
                + "window; the probed flag on IsConnectedResponse reports which answers came from the "
                + "cache.";

            return RetCode.E_INVALID_ARGUMENT;
        }

        return RetCode.OK;
    }

    // ==============================================================================================
    //  PART 4 OF 4 - THE THIRTEEN RPCs, IN THE ORDER THE CONTRACT DECLARES THEM
    //  --------------------------------------------------------------------------------------------
    //  Every handler completes SYNCHRONOUSLY and returns a completed task. That is not a shortcut: the
    //  collaborators are synchronous because the oracle's transaction members are, and inventing an
    //  asynchronous seam would add a continuation the legacy has no equivalent of - while making the
    //  per-transaction gate unsafe to hold across it.
    //
    //  NO HANDLER THROWS RpcException. persistence.v1.OperationStatus is, by the contract's own words,
    //  the uniform outcome of every unary call in this file, so an outcome is REPORTED rather than
    //  raised: a caller reads ret_code and branches on its specific value, never on a two-way success
    //  test, because the algebra is tri-state - PREVENT reads as a success and CANCELLED is neither.
    //  Status mapping is not re-implemented here, and nothing in C-08 produces Aborted.
    // ==============================================================================================

    /// <summary>
    /// Opens a session - <c>of_connect</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:L73</c>, <c>:L111-L143</c>] over the
    /// reference-counted pool.
    /// </summary>
    /// <param name="request">The nine-field descriptor plus the optional flag and pool settings.</param>
    /// <param name="context">The call context. Not consulted: the handler is synchronous and total.</param>
    /// <returns>A completed task carrying the outcome and, on success, the session handle.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// <b>THE ACQUISITION SEQUENCE IS THE ORACLE'S, STEP FOR STEP</b>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru:L148-L184</c>]: take a
    /// reference, get the entry's transaction, and connect it only if it is not already live. The
    /// reuse-if-not-broken arm at [<c>:L153-L162</c>] has no analogue here because a fresh session
    /// holds no prior index to reuse - that arm is what a SECOND session on an equal descriptor gets
    /// from the pool for free, since <c>AddRef</c> returns the existing index [<c>:L138-L141</c>].
    /// </para>
    /// <para>
    /// <b>THE REFERENCE IS RELEASED ON EVERY FAILURE PATH.</b> A reference taken and not released would
    /// pin a pool entry for the life of the process, and the oracle releases its own on the equivalent
    /// paths [<c>:L121-L124</c>, <c>:L715-L717</c>].
    /// </para>
    /// <para>
    /// ============ THE TRI-STATE HOLE IN THE CONNECT TEST, PRESERVED VERBATIM (constraint C-B) =====
    /// </para>
    /// <para>
    /// The oracle tests the connect result with <c>IsFailed</c> and NOT with an equality against zero
    /// [<c>:L174</c>]. That predicate is <c>rtCode &lt; 0</c> with <c>CANCELLED</c> EXPLICITLY EXCLUDED
    /// [<c>ws_objects/pfw.shared.pbl.src/isfailed.srf:L11-L13</c>,
    /// <c>ws_objects/pfw.shared.pbl.src/retcode.sru:L44-L45</c>], so a CLEANLY VETOED before-connect
    /// hook - which returns <c>CANCELLED</c> [<c>n_cst_thread_trans.sru:L124</c>] - PASSES the test, and
    /// the oracle proceeds to report success over a transaction that never connected. The failure then
    /// surfaces on the next operation instead.
    /// </para>
    /// <para>
    /// That hole is reproduced here rather than closed. It is a defect and it is the oracle's, so
    /// "fixing" it would be the silent behaviour correction that is forbidden - and it is reachable
    /// only when a hook vetoes the connect, which no default configuration does. A veto that ALSO left
    /// a driver error behind takes the other arm at [<c>n_cst_thread_trans.sru:L123</c>] and yields
    /// <c>E_DB_ERROR</c>, which IS a failure, so the two veto outcomes stay distinct exactly as the
    /// oracle keeps them.
    /// </para>
    /// <para>
    /// A genuine connect failure is reported as <c>E_INVALID_TRANSACTION</c> - the code the oracle's
    /// acquisition returns [<c>:L177</c>], not the code <c>of_connect</c> returned - with the driver's
    /// own numeric code and message beneath it in <c>db_error</c>, captured exactly as the oracle
    /// captures it [<c>:L175-L176</c>]. A session already marked broken reaches the same arm, because
    /// <c>of_connect</c> refuses it with <c>E_INVALID_TRANSACTION</c> [<c>:L113</c>] and that IS a
    /// failure by the predicate.
    /// </para>
    /// <para>
    /// <b>CANCELLATION: THIS IS THE ONLY RPC ON THIS CONTRACT THAT OBSERVES THE REQUEST'S TOKEN.</b> It
    /// is the only one that issues new work, so it is the only one where a caller who has already
    /// disconnected should stop the service from starting something. A cancelled request answers
    /// <c>RetCode.CANCELLED</c>, holds no pool reference and creates no session, and it does so through
    /// an arm of its own rather than through the connect's return value - because the connect's
    /// <c>CANCELLED</c> reads as neither succeeded nor failed and that hole is the oracle's, reserved for
    /// the oracle's own cause. Commit, rollback, auto-commit and <c>EndSession</c> deliberately do NOT
    /// observe the token: each completes or undoes work already begun, and abandoning one would leave a
    /// unit of work neither applied nor reverted, or leak a pool reference for the life of the process.
    /// </para>
    /// </remarks>
    [Authorize(Policy = PersistenceAuthorizationPolicies.Read)]
    public override Task<BeginSessionResponse> BeginSession(
        BeginSessionRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The ONLY handler on this contract that reads its context, and it reads exactly one member of
        // it - see the cancellation block below the acquisition for why this verb and no other.
        ArgumentNullException.ThrowIfNull(context);

        if (request.Descriptor_ is null)
        {
            return Task.FromResult(new BeginSessionResponse
            {
                Status = TransactionWireCodes.Status(
                    RetCode.E_INVALID_ARGUMENT,
                    "BeginSession requires a transaction descriptor."),
            });
        }

        TransactionData descriptor = ToDescriptor(request.Descriptor_);

        long guard = GuardConnectionFlags(in descriptor, request.Flags, out string diagnostic);
        if (guard == RetCode.OK)
        {
            guard = GuardPoolSettings(request.KeepAlive, out diagnostic);
        }

        if (guard != RetCode.OK)
        {
            // Named fields only - the descriptor is NEVER interpolated into a log message, because a
            // record's generated renderer would print every property including the credential.
            _logger?.LogWarning(
                "BeginSession refused a request for {Dbms} on {ServerName}/{Database} with code "
                + "{ReturnCode}.",
                descriptor.Dbms,
                descriptor.ServerName,
                descriptor.Database,
                guard);

            return Task.FromResult(new BeginSessionResponse
            {
                Status = TransactionWireCodes.Status(guard, diagnostic),
            });
        }

        // [n_cst_thread_task_sqlbase.sru:L165] `_nTransRefIdx = transPool.of_AddRef(_transData)`.
        // ONE-BASED: the oracle appends at UpperBound + 1 and returns that
        // [n_cst_thread_trans_pool.sru:L143-L151]. Stored verbatim; never decremented.
        PoolLease lease = _pool.AddRefLease(in descriptor);

        // Defensive, and it states the one-based invariant at the boundary: the oracle's own guards
        // treat a non-positive index as out of bounds [n_cst_thread_trans_pool.sru:L89, :L120, :L154],
        // so zero is an error here rather than "the first entry".
        if (!lease.IsValid)
        {
            return Task.FromResult(new BeginSessionResponse
            {
                Status = TransactionWireCodes.Status(RetCode.E_OUT_OF_BOUND),
            });
        }

        // [:L168] `rtCode = transPool.of_Get(_nTransRefIdx, ref transObject)`. IsSucceeded is the
        // SHARED KERNEL predicate the oracle uses at [:L169] - under which PREVENT reads as a success -
        // and NOT the transaction object's SQLCode predicates. The two are not interchangeable.
        long acquired = _pool.Get(lease, out IPooledTransaction? borrowed);

        if (!Predicates.IsSucceeded(acquired) || borrowed is null)
        {
            _ = _pool.RemoveRef(lease);

            return Task.FromResult(new BeginSessionResponse
            {
                // [:L183] the pool's own code is returned. A succeeded-but-null answer cannot arise from
                // the pool as written, and reporting E_INVALID_OBJECT for it matches the code the pool
                // uses when it cannot produce a transaction object [n_cst_thread_trans_pool.sru:L174].
                Status = TransactionWireCodes.Status(
                    Predicates.IsSucceeded(acquired) ? RetCode.E_INVALID_OBJECT : acquired),
            });
        }

        // [:L173] `if Not _transObject.of_IsConnected() then` - the liveness check gates the connect, so
        // a pooled connection that is still live is NOT reconnected.
        if (!borrowed.IsConnected())
        {
            // =====================================================================================
            //  THE REQUEST'S CANCELLATION IS OBSERVED HERE, AND NOT INSIDE THE CONNECT
            //
            //  This is the one verb on this contract that ISSUES NEW WORK - every other verb either
            //  completes work already begun (commit), undoes it (rollback), or reads state - so this
            //  is the one place where a caller who has already gone should stop the service from
            //  starting something. Observing the token before the call is also the ONLY cancellation
            //  the connect could honour: Microsoft.Data.Sqlite is a synchronous provider with no
            //  interrupt, so an open already in flight cannot be abandoned, and a token handed to it
            //  could change nothing that this arm does not already decide.
            //
            //  WHY THE TOKEN IS NOT PASSED TO Connect ITSELF. `IPooledTransaction.Connect` answers
            //  RetCode.CANCELLED when its own token is signalled, and the test below is IsFailed,
            //  under which CANCELLED IS NEITHER SUCCEEDED NOR FAILED
            //  [ws_objects/pfw.shared.pbl.src/isfailed.srf:L11-L13]. That tri-state hole is a
            //  PRESERVED ORACLE DEFECT (constraint C-B, and see this member's remarks): the oracle
            //  reaches it only when a before-connect hook vetoes [n_cst_thread_trans.sru:L124], and
            //  it then registers a session over a transaction that never connected. Routing a REQUEST
            //  cancellation through that same code would EXTEND the hole to a cause the oracle never
            //  had, which is not preservation - it is a new defect wearing preservation's clothes.
            //  Keeping the two causes apart is what this separate arm buys.
            // =====================================================================================
            if (context.CancellationToken.IsCancellationRequested)
            {
                // The reference taken above is released on this path exactly as on every other
                // failure path, so a cancelled BeginSession pins no pool entry.
                _ = _pool.RemoveRef(lease);

                return Task.FromResult(new BeginSessionResponse
                {
                    Status = TransactionWireCodes.Status(RetCode.CANCELLED, CancelledDiagnostic),
                });
            }

            // [:L174] IsFailed, deliberately - see the tri-state note in this member's remarks.
            if (Predicates.IsFailed(borrowed.Connect()))
            {
                // [:L175-L176] SQLDBCode and SQLErrText, and nothing else. Captured by the transaction
                // object's own accessor rather than reassembled here, and projected onto the wire by the
                // sanctioned mapping, which masks the statement field unconditionally.
                DbErrorData captured = borrowed.CaptureError();

                _ = _pool.RemoveRef(lease);

                _logger?.LogWarning(
                    "BeginSession could not connect to {Dbms} on {ServerName}/{Database}; driver code "
                    + "{SqlDbCode}.",
                    descriptor.Dbms,
                    descriptor.ServerName,
                    descriptor.Database,
                    captured.SqlDbCode);

                return Task.FromResult(new BeginSessionResponse
                {
                    // [:L177] E_INVALID_TRANSACTION - the acquisition's code, with the driver detail
                    // beneath it.
                    Status = TransactionWireCodes.Status(RetCode.E_INVALID_TRANSACTION, in captured),
                });
            }
        }

        TransactionSession? session =
            _sessions.Register(lease, in descriptor, borrowed, out string quotaDiagnostic);

        if (session is null)
        {
            // A PER-CALLER CEILING REFUSAL, AND THE POOL REFERENCE GOES STRAIGHT BACK. The registry tests
            // the ceiling BEFORE it mints a handle, so nothing was registered and this arm has only the
            // reference to unwind - which it does immediately, because a leaked reference would pin a
            // connection for the life of the process.
            //
            // E_BUSY IS THE ORACLE'S OWN "NOT NOW", so a ceiling refusal introduces no new value into a
            // consumer's branch set - the same code and the same shape the sibling task registries answer
            // a ceiling with. The diagnostic names the ceiling and never the caller's credential.
            _ = _pool.RemoveRef(lease);

            _logger?.LogWarning(
                "BeginSession refused a session because a handle ceiling was reached: {Diagnostic}",
                quotaDiagnostic);

            return Task.FromResult(new BeginSessionResponse
            {
                Status = TransactionWireCodes.Status(RetCode.E_BUSY, quotaDiagnostic),
            });
        }

        _logger?.LogDebug(
            "BeginSession opened session {SessionId} on pool lease {Lease} for "
            + "{Dbms} on {ServerName}/{Database}.",
            session.SessionId,
            session.Lease.Id,
            descriptor.Dbms,
            descriptor.ServerName,
            descriptor.Database);

        return Task.FromResult(new BeginSessionResponse
        {
            // [:L180] RetCode.OK
            Status = TransactionWireCodes.Status(RetCode.OK),
            Session = new SessionHandle { SessionId = session.SessionId },
        });
    }

    /// <summary>
    /// Ends a session - <c>of_disconnect</c> [<c>n_cst_thread_trans.sru:L74</c>] as the pool expresses
    /// it: the REFERENCE is released, and whether the connection closes is the pool's decision.
    /// </summary>
    /// <param name="request">The session to end.</param>
    /// <param name="context">
    /// The call context. Not consulted - see the uncancellable-wait paragraph below for why a teardown does
    /// not take its caller's token.
    /// </param>
    /// <returns>The outcome.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// <b>TWO POOL OPERATIONS, IN THE ORACLE'S ORDER, BECAUSE A SESSION'S END IS BOTH OF THEM.</b> The
    /// oracle separates them across a task's lifetime: <c>of_releasetransobject</c> hands the borrowed
    /// object back through <c>of_Release</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru:L137-L146</c>], and the
    /// task's own teardown drops the reference through <c>of_RemoveRef</c> [<c>:L715-L717</c>]. A
    /// session ending is the point at which both have happened, so both are performed here and in that
    /// order.
    /// </para>
    /// <para>
    /// <b>A SUCCESSFUL EndSession DOES NOT IMPLY A CLOSED SOCKET, and a caller must not infer one.</b>
    /// <c>of_Release</c> disconnects only when this is the LAST reference
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans_pool.sru:L123-L126</c>], and
    /// <c>of_RemoveRef</c> then either PARKS the entry with an idle stamp - when keep-alive is on and
    /// the connection is not broken [<c>:L94-L100</c>] - or disconnects, destroys and compacts it out
    /// of the array [<c>:L101-L114</c>]. Both decisions are the pool's; this service makes neither.
    /// </para>
    /// <para>
    /// <b>THE CLOSING MARK COMES FIRST, BEFORE THE RETIREMENT AND BEFORE THE PURGE, AND THE ORDER IS THE
    /// WHOLE OF THE LIFECYCLE GUARANTEE.</b> Retiring the handle stops a caller RESOLVING the session; it
    /// does nothing about a caller that resolved it a moment earlier and is still on its way to the gate,
    /// and it does nothing about a create that is between building a task and publishing it. Marking the
    /// session closing inside the gate closes both windows at once, because every operating path and every
    /// publication path tests that flag inside the same gate:
    /// </para>
    /// <list type="number">
    ///   <item><description>
    ///   an operation already inside the gate finishes first - this call blocks behind it - so nothing is
    ///   torn out from under a statement in flight;
    ///   </description></item>
    ///   <item><description>
    ///   an operation not yet in the gate enters after the mark, reads <c>IsClosing</c> and refuses with
    ///   <c>E_INVALID_TRANSACTION</c> rather than touching a transaction on its way back to the pool;
    ///   </description></item>
    ///   <item><description>
    ///   a create that publishes BEFORE the mark is seen by the purge below, and a create that reaches the
    ///   gate AFTER the mark refuses - so no task can be published into a registry the purge has already
    ///   walked. Marking AFTER the purge, which is what this handler used to do, left exactly that gap.
    ///   </description></item>
    /// </list>
    /// <para>
    /// <b>The handle is retired before either pool call.</b> Retiring is what makes a handle single-use
    /// under concurrency: a second EndSession naming the same handle loses the removal race and reports the
    /// unknown-session outcome rather than releasing a second reference the caller never took. It follows
    /// the mark rather than preceding it because the mark is the safety property and the removal is only
    /// the bookkeeping; a second EndSession that marks an already-marked session changes nothing.
    /// </para>
    /// <para>
    /// <b>THE GATE IS AWAITED RATHER THAN BLOCKED ON.</b> A streaming retrieval holds the gate for the whole
    /// of its stream, so a blocking acquisition here would pin a request thread for that entire duration.
    /// Awaiting yields the thread instead, which changes no observable outcome and no ordering.
    /// </para>
    /// <para>
    /// <b>BUT THE WAIT IS UNCANCELLABLE, AND THAT IS DELIBERATE RATHER THAN AN OVERSIGHT.</b> This handler is
    /// a TEARDOWN, and its steps are not independently meaningful: abandoning it between the closing mark and
    /// the release would leave a session that is retired from its registry, marked unusable, purged of its
    /// tasks - and still holding a pool reference nothing will ever return, because no later call can name it.
    /// A caller that goes away mid-teardown therefore gets the teardown finished on its behalf rather than a
    /// half-dismantled session and a pinned connection. It is also why the call context is still not
    /// consulted here; the only cancellable waits in this service are the ones whose abandonment leaves no
    /// residue.
    /// </para>
    /// <para>
    /// The bounds guard runs before the pool calls so that an index invalidated by another session's
    /// departure is reported as <c>E_OUT_OF_BOUND</c> - the code the oracle's own guards return - rather
    /// than acting on whatever entry the compacted array now holds at that position.
    /// </para>
    /// </remarks>
    [Authorize(Policy = PersistenceAuthorizationPolicies.Read)]
    public override async Task<EndSessionResponse> EndSession(
        EndSessionRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!_sessions.TryResolve(request.Session, out TransactionSession? session) || session is null)
        {
            return new EndSessionResponse
            {
                Status = TransactionWireCodes.Status(
                    RetCode.E_INVALID_TRANSACTION,
                    UnknownSessionDiagnostic),
            };
        }

        // ------------------------------------------------------------------------------------------
        //  MARK CLOSING FIRST, INSIDE THE GATE. Everything below - the retirement, the purge and the
        //  release - is bookkeeping that this single flag makes safe. Waiting for the gate here also
        //  waits out any operation currently in flight on the transaction, so the purge below can never
        //  meet a running statement.
        // ------------------------------------------------------------------------------------------
        using (await session.Gate.EnterAsync(CancellationToken.None).ConfigureAwait(false))
        {
            session.MarkClosing();
        }

        // Retire next, so the handle is single-use even under a concurrent second EndSession.
        if (!_sessions.TryRemove(session.SessionId))
        {
            return new EndSessionResponse
            {
                Status = TransactionWireCodes.Status(
                    RetCode.E_INVALID_TRANSACTION,
                    UnknownSessionDiagnostic),
            };
        }

        // ------------------------------------------------------------------------------------------
        //  PURGE THE SESSION'S TASKS BEFORE THE TRANSACTION GOES BACK TO THE POOL.
        //
        //  Every query, update and command task was created against THIS session and holds its borrowed
        //  transaction. Releasing the transaction while those handles are still in their tables leaves
        //  them naming a transaction the pool has taken back - and the pool may hand that same
        //  transaction to a different session as soon as its reference count drops, so a later call on a
        //  stale handle would write through somebody else's transaction. Purging FIRST closes that
        //  window: by the time the release below runs, no handle can reach the transaction.
        //
        //  It runs before the lease guard as well, because the tasks must go whatever the lease turns out
        //  to be - the session is already retired from its own registry at this point, so its handles can
        //  never be legitimately used again regardless of what the pool says.
        //
        //  AND IT RUNS AFTER THE CLOSING MARK, which is what makes the walk exhaustive. A create that had
        //  not yet published when the mark went up now refuses at the gate instead of publishing, so this
        //  is the LAST set of tasks that can exist for this session - there is no later arrival for a
        //  second walk to catch. Each entry goes through its registry's release/disposal handoff rather
        //  than a direct teardown, so an operation still in flight disposes itself on exit instead of
        //  being disposed underneath itself [docs/PB多线程绕坑提示.md, hazard 1].
        // ------------------------------------------------------------------------------------------
        int retiredQueries = _queryTasks.PurgeSession(session.SessionId);
        int retiredUpdates = _updateTasks.PurgeSession(session.SessionId);
        int retiredCommands = _commandTasks.PurgeSession(session.SessionId);

        if (_logger?.IsEnabled(LogLevel.Debug) == true
            && retiredQueries + retiredUpdates + retiredCommands > 0)
        {
            _logger.LogDebug(
                "EndSession purged {QueryTaskCount} query, {UpdateTaskCount} update and "
                + "{CommandTaskCount} command task(s) owned by session {SessionId}.",
                retiredQueries,
                retiredUpdates,
                retiredCommands,
                session.SessionId);
        }

        long guard = GuardLease(session.Lease);
        if (guard != RetCode.OK)
        {
            return new EndSessionResponse
            {
                Status = TransactionWireCodes.Status(guard),
            };
        }

        long rtCode;

        using (await session.Gate.EnterAsync(CancellationToken.None).ConfigureAwait(false))
        {
            // [n_cst_thread_task_sqlbase.sru:L141] of_Release - hands the borrowed object back and
            // disconnects only on the last reference. The reference is passed by `ref` because the
            // oracle's signature is `ref n_cst_thread_trans` and the pool nulls the caller's handle
            // [n_cst_thread_trans_pool.sru:L129].
            //
            // The session was marked closing in the FIRST gate acquisition above, before the retirement
            // and before the purge, so by the time this release runs nothing can be operating on the
            // transaction and nothing can have been published against it since the purge walked.
            IPooledTransaction? borrowed = session.Transaction;
            long released = _pool.Release(session.Lease, ref borrowed);

            // [n_cst_thread_task_sqlbase.sru:L716] of_RemoveRef - the decrement. Performed even when the
            // release reported a problem, because leaving the reference taken would pin the entry for
            // the life of the process; the FIRST non-OK code is what the caller is told.
            long removed = _pool.RemoveRef(session.Lease);

            rtCode = released != RetCode.OK ? released : removed;
        }

        _logger?.LogDebug(
            "EndSession retired session {SessionId} on pool lease {Lease} with code {ReturnCode}.",
            session.SessionId,
            session.Lease.Id,
            rtCode);

        return new EndSessionResponse
        {
            Status = TransactionWireCodes.Status(rtCode),
        };
    }

    /// <summary>
    /// Reads a session's descriptor back - <c>of_gettransdata</c>, BOTH overloads
    /// [<c>n_cst_thread_trans.sru:L92</c>, <c>:L93</c>, <c>:L394-L419</c>] - onto the response view,
    /// WHICH HAS NO PASSWORD FIELD.
    /// </summary>
    /// <param name="request">The session to read.</param>
    /// <param name="context">The call context. Not consulted.</param>
    /// <returns>A completed task carrying the outcome and the descriptor view.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// <b>THE OUTBOUND TRANSFER IS THE DESCRIPTOR'S OWN, NOT RE-IMPLEMENTED HERE.</b> The accessor
    /// clears the diagnostic before doing anything [<c>:L402</c>], moves the connection fields
    /// [<c>:L410-L416</c>] and reports <c>FAILED</c> when it produced diagnostic text
    /// [<c>:L405</c>, <c>:L408</c>] - all of which lives in <c>Transactions/TransactionData.cs</c>. This
    /// handler supplies the receiver, forwards the outcome, and projects the result.
    /// </para>
    /// <para>
    /// <b>THE RECEIVER IS A FRESHLY CLEARED VALUE, WHICH IS WHAT THE ORACLE PASSES</b> - its by-value
    /// overload declares an unassigned structure and hands it to the by-reference one
    /// [<c>:L394-L399</c>]. That detail is load-bearing rather than incidental: the transfer moves six
    /// of the nine members, so the three it does not move come from the RECEIVER, and a receiver that
    /// was anything other than cleared would report values this session never held.
    /// </para>
    /// <para>
    /// <b>THREE MEMBERS CANNOT REACH THE CALLER, AND ONE MORE IS ABSENT BY THE ORACLE'S OWN
    /// ASYMMETRY.</b> The credential, the connection-parameter string and the free-form extra string
    /// have no slot on the view at all (constraint C-F); <c>autocommit</c> is present on the view but is
    /// never copied by the transfer [<c>:L410-L416</c>], so it reports the receiver's cleared value
    /// rather than the live flag. Both facts are preserved deliberately - see <c>ToView</c>.
    /// </para>
    /// <para>
    /// The by-value overload's own quirk - it DISCARDS the return code and returns the structure
    /// regardless [<c>:L397-L399</c>] - is not reproduced at this boundary, because the two overloads
    /// are ONE RPC on the wire and the by-reference overload is the one that reports. Discarding the
    /// code would mean answering OK for a transfer that failed, which the contract's uniform status
    /// makes unnecessary: both outcomes are distinguishable through <c>ret_code</c> and
    /// <c>error_text</c> without inspecting the descriptor.
    /// </para>
    /// </remarks>
    [Authorize(Policy = PersistenceAuthorizationPolicies.Read)]
    public override Task<GetTransactionDataResponse> GetTransactionData(
        GetTransactionDataRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!_sessions.TryResolve(request.Session, out TransactionSession? session) || session is null)
        {
            return Task.FromResult(new GetTransactionDataResponse
            {
                Status = TransactionWireCodes.Status(
                    RetCode.E_INVALID_TRANSACTION,
                    UnknownSessionDiagnostic),
            });
        }

        // [:L395] a freshly declared - therefore cleared - receiver, and [:L394] a cleared diagnostic.
        TransactionData outbound = TransactionData.Empty;
        string errInfo = string.Empty;

        long rtCode;

        bool closing;

        using (session.Gate.Enter())
        {
            closing = session.IsClosing;

            if (!closing)
            {
                rtCode = session.Descriptor.GetTransactionData(ref outbound, ref errInfo);
            }
            else
            {
                rtCode = RetCode.E_INVALID_TRANSACTION;
            }
        }

        if (closing)
        {
            return Task.FromResult(new GetTransactionDataResponse
            {
                Status = TransactionWireCodes.Status(
                    RetCode.E_INVALID_TRANSACTION,
                    UnknownSessionDiagnostic),
            });
        }

        return Task.FromResult(new GetTransactionDataResponse
        {
            Status = TransactionWireCodes.Status(rtCode, errInfo),
            Descriptor_ = ToView(in outbound),
        });
    }

    /// <summary>
    /// Sets the session's autocommit flag directly.
    /// </summary>
    /// <param name="request">The session and the flag.</param>
    /// <param name="context">The call context. Not consulted.</param>
    /// <returns>A completed task carrying the outcome.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// <b>A NET-NEW OPERATION THE BOUNDARY REQUIRES, NOT A PROJECTION OF A LEGACY FUNCTION
    /// (constraint C-K).</b> <c>AutoCommit</c> is not a framework member at all: it is the INHERITED
    /// PowerBuilder <c>transaction</c> property, and the class is declared <c>from transaction</c>
    /// [<c>n_cst_thread_trans.sru:L4</c>]. In process it is therefore read and written by direct
    /// property access - read at [<c>:L185</c>], [<c>:L240</c>], [<c>:L371</c>] and [<c>:L376</c>], and
    /// written at
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlcommand.sru:L89</c>, <c>:L96</c>,
    /// <c>:L106</c>] as one arm toggles it around a statement. A property assignment has no wire
    /// representation, so the setter has to become an RPC. There is consequently no legacy return code
    /// to reproduce, and <c>OK</c> is the only honest answer once the assignment has been made.
    /// </para>
    /// <para>
    /// <b>TWO THINGS THIS IS DELIBERATELY NOT.</b> It is NOT <c>of_settransdata</c>, which carries seven
    /// of the descriptor's nine fields and TOUCHES NEITHER <c>autocommit</c> NOR <c>userparm</c>
    /// [<c>:L343-L354</c>] - routing this call through the descriptor would silently do nothing. And it
    /// is NOT <c>of_autocommit</c> [<c>:L370-L381</c>], which is a commit-or-rollback OPERATION selected
    /// by the statement status and is the separate <c>AutoCommit</c> RPC below.
    /// </para>
    /// <para>
    /// A plain boolean, correctly so: the underlying property is genuinely two-valued and is not C-07's
    /// three-valued per-statement mode.
    /// </para>
    /// </remarks>
    [Authorize(Policy = PersistenceAuthorizationPolicies.Write)]
    public override Task<SetTransactionAutoCommitResponse> SetAutoCommit(
        SetTransactionAutoCommitRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!_sessions.TryResolve(request.Session, out TransactionSession? session) || session is null)
        {
            return Task.FromResult(new SetTransactionAutoCommitResponse
            {
                Status = TransactionWireCodes.Status(
                    RetCode.E_INVALID_TRANSACTION,
                    UnknownSessionDiagnostic),
            });
        }

        bool closing;

        using (session.Gate.Enter())
        {
            closing = session.IsClosing;

            if (!closing)
            {
                session.Transaction.AutoCommit = request.Autocommit;
            }
        }

        if (closing)
        {
            return Task.FromResult(new SetTransactionAutoCommitResponse
            {
                Status = TransactionWireCodes.Status(
                    RetCode.E_INVALID_TRANSACTION,
                    UnknownSessionDiagnostic),
            });
        }

        return Task.FromResult(new SetTransactionAutoCommitResponse
        {
            Status = TransactionWireCodes.Status(RetCode.OK),
        });
    }

    /// <summary>
    /// Commits or rolls back according to the statement status - <c>of_autocommit</c>
    /// [<c>n_cst_thread_trans.sru:L88</c>, <c>:L370-L381</c>].
    /// </summary>
    /// <param name="request">The session to check point.</param>
    /// <param name="context">The call context. Not consulted.</param>
    /// <returns>A completed task carrying the outcome.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// <b>THREE ARMS, PRESERVED IN THE ORACLE'S ORDER</b> and implemented by the pooled transaction
    /// rather than by this handler: a NON-ZERO statement status ROLLS BACK - but only when autocommit is
    /// off - and returns <c>E_DB_ERROR</c> [<c>:L370-L375</c>]; otherwise, with autocommit off, it
    /// COMMITS with auto-rollback enabled [<c>:L376-L378</c>]; otherwise it is a no-op success
    /// [<c>:L380</c>].
    /// </para>
    /// <para>
    /// The condition is <c>SQLCode &lt;&gt; 0</c> and NOT the transaction's failure predicate, so a
    /// driver code of 100 takes the ERROR arm here even though the same value reads as SUCCEEDED through
    /// <c>of_issucceeded</c> [<c>:L340</c>]. Those are two different tests on one field and the oracle
    /// uses each in its own place; conflating them would commit where the oracle rolls back.
    /// </para>
    /// <para>
    /// <b>The driver detail accompanies an <c>E_DB_ERROR</c> and nothing else.</b> The contract carries
    /// <c>db_error</c> only where the oracle would also have reported at the driver level, so the
    /// guard-shaped outcomes above carry a code alone.
    /// </para>
    /// </remarks>
    [Authorize(Policy = PersistenceAuthorizationPolicies.Write)]
    public override Task<AutoCommitResponse> AutoCommit(
        AutoCommitRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!_sessions.TryResolve(request.Session, out TransactionSession? session) || session is null)
        {
            return Task.FromResult(new AutoCommitResponse
            {
                Status = TransactionWireCodes.Status(
                    RetCode.E_INVALID_TRANSACTION,
                    UnknownSessionDiagnostic),
            });
        }

        return Task.FromResult(new AutoCommitResponse
        {
            Status = RunAndProject(session, static transaction => transaction.AutoCommitCheckpoint()),
        });
    }

    /// <summary>
    /// Commits - <c>of_commit</c>, both overloads [<c>n_cst_thread_trans.sru:L79</c>, <c>:L89</c>,
    /// <c>:L240-L257</c>, <c>:L383</c>].
    /// </summary>
    /// <param name="request">The session and the optional auto-rollback argument.</param>
    /// <param name="context">The call context. Not consulted.</param>
    /// <returns>A completed task carrying the outcome.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// <b>AN UNSET AUTO-ROLLBACK ARGUMENT MEANS TRUE, NOT FALSE.</b> The oracle's no-argument overload
    /// is literally <c>return of_Commit(true)</c> [<c>:L383</c>], so the default is TRUE - and a plain
    /// proto3 boolean would have defaulted to false and inverted it. Explicit field presence is what
    /// makes the correct default expressible, which is why presence is tested before the value.
    /// </para>
    /// <para>
    /// <b>COMMIT FAILS WHILE AUTOCOMMIT IS ON, WITH PLAIN <c>FAILED</c> AND NOT AN <c>E_</c> CODE</b> -
    /// <c>if AutoCommit then return RetCode.FAILED</c> [<c>:L240</c>]. So committing a session that is
    /// already autocommitting is an ERROR rather than a harmless no-op. The very next line returns a
    /// DIFFERENT code for a different condition - <c>if _bBroken then return
    /// RetCode.E_INVALID_TRANSACTION</c> [<c>:L241</c>] - and the order between them is observable: an
    /// autocommitting session that is ALSO broken reports <c>FAILED</c>, because the autocommit test
    /// comes first. Both guards live in the pooled transaction; flattening either into a generic failure
    /// would remove a distinction a caller needs, which is "you did not need to commit" versus "this
    /// session is unusable".
    /// </para>
    /// <para>
    /// On a driver failure the rollback is CONDITIONAL on the argument [<c>:L249-L254</c>] and the
    /// outcome is <c>E_DB_ERROR</c> either way, with the driver detail beneath it.
    /// </para>
    /// </remarks>
    [Authorize(Policy = PersistenceAuthorizationPolicies.Write)]
    public override Task<CommitResponse> Commit(CommitRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!_sessions.TryResolve(request.Session, out TransactionSession? session) || session is null)
        {
            return Task.FromResult(new CommitResponse
            {
                Status = TransactionWireCodes.Status(
                    RetCode.E_INVALID_TRANSACTION,
                    UnknownSessionDiagnostic),
            });
        }

        // [:L383] PRESENCE FIRST, THEN THE VALUE. Absent means TRUE.
        bool autoRollback = !request.HasAutoRollback || request.AutoRollback;

        return Task.FromResult(new CommitResponse
        {
            Status = RunAndProject(session, transaction => transaction.Commit(autoRollback)),
        });
    }

    /// <summary>
    /// Rolls back - <c>of_rollback</c> [<c>n_cst_thread_trans.sru:L76</c>, <c>:L185-L191</c>].
    /// </summary>
    /// <param name="request">The session to roll back.</param>
    /// <param name="context">The call context. Not consulted.</param>
    /// <returns>A completed task carrying the outcome.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// <b>ROLLBACK UNDER AUTOCOMMIT RETURNS <c>FAILED</c>. IT IS NOT A NO-OP</b> -
    /// <c>if AutoCommit then return RetCode.FAILED</c> [<c>:L185</c>] - and the line after it returns a
    /// DIFFERENT code for a broken session, <c>E_INVALID_TRANSACTION</c> [<c>:L186</c>]. The second
    /// guard is easy to miss because it sits immediately below the first and answers differently; both
    /// are expressible here because <c>ret_code</c> is the full return-code space rather than a boolean.
    /// The identical pair guards <c>of_commit</c> at [<c>:L240-L241</c>].
    /// </para>
    /// <para>
    /// ============ THE ROLLBACK PRESERVES FIVE STATEMENT STATUS VALUES AROUND ITSELF ================
    /// </para>
    /// <para>
    /// <c>_of_cleanrollback</c> SAVES <c>SQLCode</c>, <c>SQLDBCode</c>, <c>SQLNRows</c>,
    /// <c>SQLErrText</c> and <c>SQLReturnData</c> [<c>:L166-L170</c>], fires the before-rollback hook
    /// [<c>:L172</c>], performs the rollback [<c>:L174</c>], RESTORES ALL FIVE [<c>:L176-L180</c>], and
    /// only then fires the after-rollback hook [<c>:L182</c>]. <b>So a caller still reads the ORIGINAL
    /// error after rolling back</b>, which is the entire point: an implementation that let the rollback
    /// overwrite the status would leave a caller unable to see why it rolled back at all.
    /// </para>
    /// <para>
    /// That sequence is NOT flattened and NOT re-implemented here - it lives in the pooled transaction,
    /// which is where the oracle keeps it. This response carries no SQL state to echo, so there is
    /// nothing here to get wrong; the RESTORED values are observable through <c>GetSessionState</c>,
    /// which is what makes the preservation checkable across the boundary at all. The per-transaction
    /// gate is what stops a concurrent reader from observing the window between the save and the
    /// restore.
    /// </para>
    /// </remarks>
    [Authorize(Policy = PersistenceAuthorizationPolicies.Write)]
    public override Task<RollbackResponse> Rollback(RollbackRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!_sessions.TryResolve(request.Session, out TransactionSession? session) || session is null)
        {
            return Task.FromResult(new RollbackResponse
            {
                Status = TransactionWireCodes.Status(
                    RetCode.E_INVALID_TRANSACTION,
                    UnknownSessionDiagnostic),
            });
        }

        return Task.FromResult(new RollbackResponse
        {
            Status = RunAndProject(session, static transaction => transaction.Rollback()),
        });
    }

    /// <summary>
    /// Runs one transaction operation under the session's gate and projects its outcome onto a status,
    /// attaching the driver detail when - and only when - the outcome is a database error.
    /// </summary>
    /// <param name="session">The session whose transaction and gate to use.</param>
    /// <param name="operation">
    /// The transaction member to invoke. Synchronous by requirement: it runs while the gate is held.
    /// </param>
    /// <returns>The projected status.</returns>
    /// <remarks>
    /// <para>
    /// <b>WHY <c>db_error</c> IS ATTACHED ONLY FOR <c>E_DB_ERROR</c>.</b> The contract carries the
    /// driver payload only where the oracle would also have reported at the driver level, and every
    /// other outcome these three operations can produce is a GUARD - autocommit conflict, broken
    /// session, unknown handle - which the oracle reports without any driver detail at all. Attaching a
    /// payload to a guard would report a driver code of zero as though the driver had spoken.
    /// </para>
    /// <para>
    /// <b>The capture happens INSIDE the gate, which is not incidental.</b> The five statement status
    /// values are exactly what a concurrent operation would overwrite, so reading them after releasing
    /// the gate could attach another call's error to this call's outcome. It is also why the capture is
    /// the transaction's own accessor rather than five separate property reads: one call, one consistent
    /// snapshot [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru:L175-L176</c>].
    /// </para>
    /// <para>
    /// The comparison is an equality against the specific code and NOT a two-way failure test, because
    /// the algebra is tri-state: a failure test would also catch <c>FAILED</c> from the autocommit guard
    /// and would miss nothing useful, while <c>CANCELLED</c> would fall on neither side of it.
    /// </para>
    /// </remarks>
    private static OperationStatus RunAndProject(
        TransactionSession session,
        Func<IPooledTransaction, long> operation)
    {
        long rtCode;
        DbErrorData captured;

        using (session.Gate.Enter())
        {
            if (session.IsClosing)
            {
                // THE SESSION IS RETIRING, so its transaction has been or is about to be handed back and no
                // operation may touch it. Refused with the same code and diagnostic an unknown session
                // receives, because from the caller's point of view the handle it named is gone.
                return TransactionWireCodes.Status(
                    RetCode.E_INVALID_TRANSACTION,
                    UnknownSessionDiagnostic);
            }

            rtCode = operation(session.Transaction);

            captured = rtCode == RetCode.E_DB_ERROR
                ? session.Transaction.CaptureError()
                : DbErrorData.Empty;
        }

        return rtCode == RetCode.E_DB_ERROR
            ? TransactionWireCodes.Status(rtCode, in captured)
            : TransactionWireCodes.Status(rtCode);
    }

    /// <summary>
    /// Reports whether the session's connection is live - <c>of_isconnected</c>
    /// [<c>n_cst_thread_trans.sru:L77</c>, <c>:L193-L218</c>] - and whether the answer came from the
    /// liveness cache or from an actual probe.
    /// </summary>
    /// <param name="request">The session to test.</param>
    /// <param name="context">The call context. Not consulted.</param>
    /// <returns>A completed task carrying the outcome, the verdict and the probe flag.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// <b>THE VERDICT IS FALSE WITHOUT ANY PROBE IN TWO CASES</b> - when no successful connection has
    /// ever been stamped, or the session is broken [<c>:L196</c>], and when the two-stage check hook
    /// reports a failure [<c>:L197</c>].
    /// </para>
    /// <para>
    /// ============ THE PROBE FLAG EXISTS BECAUSE THE CACHE IS OBSERVABLE ONLY BY ITS ABSENCE ========
    /// </para>
    /// <para>
    /// <c>if CPU() - _nLastConnOK &lt; 10000 then return true</c> [<c>:L198</c>] answers TRUE WITHOUT
    /// TOUCHING THE CONNECTION while the last success is recent - strictly less than, so a delta of
    /// exactly the window re-probes. Nothing about the RESULT distinguishes that from a real probe, so a
    /// parity run that cannot see the difference cannot reproduce it; the flag is the only signal, which
    /// is why it is reported rather than inferred. When a probe does run it is a two-stage affair - a
    /// test hook first, and when that yields nothing a literal one-row select whose text DIFFERS BY
    /// DIALECT [<c>:L200-L210</c>] - and its outcome either refreshes the stamp or zeroes it
    /// [<c>:L211-L215</c>].
    /// </para>
    /// <para>
    /// <b>The flag is reported by the transaction, not deduced here.</b> Deducing it - from a
    /// statement-status delta, say - would be a guess with false negatives, and putting a guess on the
    /// wire is worse than the small widening of the collaborator's own interface that reporting it
    /// truthfully requires. The window itself is fixed at the oracle's value in this build, which is why
    /// <see cref="GuardPoolSettings"/> refuses a request that asks for a different one.
    /// </para>
    /// </remarks>
    [Authorize(Policy = PersistenceAuthorizationPolicies.Read)]
    public override Task<IsConnectedResponse> IsConnected(
        IsConnectedRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!_sessions.TryResolve(request.Session, out TransactionSession? session) || session is null)
        {
            return Task.FromResult(new IsConnectedResponse
            {
                Status = TransactionWireCodes.Status(
                    RetCode.E_INVALID_TRANSACTION,
                    UnknownSessionDiagnostic),
            });
        }

        bool connected;
        bool probed;

        bool closing;

        using (session.Gate.Enter())
        {
            closing = session.IsClosing;

            if (!closing)
            {
                connected = session.Transaction.IsConnected(out probed);
            }
            else
            {
                connected = false;
                probed = false;
            }
        }

        if (closing)
        {
            return Task.FromResult(new IsConnectedResponse
            {
                Status = TransactionWireCodes.Status(
                    RetCode.E_INVALID_TRANSACTION,
                    UnknownSessionDiagnostic),
            });
        }

        return Task.FromResult(new IsConnectedResponse
        {
            // The verdict is the payload; the call itself succeeded whichever way it came out, so a
            // disconnected session is OK-with-connected-false and not an error.
            Status = TransactionWireCodes.Status(RetCode.OK),
            Connected = connected,
            Probed = probed,
        });
    }

    /// <summary>
    /// Resolves the session's paging dialect - <c>of_getdbtype</c>
    /// [<c>n_cst_thread_trans.sru:L86</c>, <c>:L356-L361</c>].
    /// </summary>
    /// <param name="request">The session to resolve.</param>
    /// <param name="context">The call context. Not consulted.</param>
    /// <returns>A completed task carrying the outcome and the dialect.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// <b>THE RESOLVER IS A SUBSTRING TEST ON THE DESCRIPTOR'S <c>dbms</c> FIELD, AND MSSQL IS THE
    /// FALLBACK FOR EVERYTHING UNRECOGNISED</b> -
    /// <c>if Pos(Upper(DBMS),"ORACLE") &gt; 0 then return DBT_ORACLE else return DBT_MSSQL</c>
    /// [<c>:L356-L361</c>]. The upper-casing makes the test case-insensitive and it is a SUBSTRING
    /// test, not an equality, so a value merely CONTAINING the marker resolves to Oracle.
    /// </para>
    /// <para>
    /// <b>SQLITE IS DELIBERATELY ABSENT FROM THE ENUMERATION, SO A SQLITE DBMS CLASSIFIES AS
    /// <c>DBT_MSSQL</c>.</b> The oracle declares exactly two database types
    /// [<c>:L60-L61</c>] and this resolver CANNOT produce a third, so there is nothing to special-case
    /// and no value to add. Adding a third would look like a correction and would change which paging
    /// rewriter every SQLite caller selects.
    /// </para>
    /// <para>
    /// <b>THE TWO VALUES SELECT PURE STRING TRANSFORMS AND NOTHING ELSE (constraint C-E).</b> They
    /// choose between the rewriters under <c>Sql/Paging/</c>, which take a statement in and hand a
    /// statement out. Neither value causes a connection to SQL Server or Oracle from anywhere in this
    /// file, and no connection string, provider or schema for either exists anywhere in the repository.
    /// </para>
    /// <para>
    /// Resolved by the transaction rather than re-derived from the stored descriptor, so the single
    /// implementation of the substring test stays single.
    /// </para>
    /// </remarks>
    [Authorize(Policy = PersistenceAuthorizationPolicies.Read)]
    public override Task<GetDatabaseTypeResponse> GetDatabaseType(
        GetDatabaseTypeRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!_sessions.TryResolve(request.Session, out TransactionSession? session) || session is null)
        {
            return Task.FromResult(new GetDatabaseTypeResponse
            {
                Status = TransactionWireCodes.Status(
                    RetCode.E_INVALID_TRANSACTION,
                    UnknownSessionDiagnostic),
            });
        }

        DatabaseType databaseType;

        bool closing;

        using (session.Gate.Enter())
        {
            closing = session.IsClosing;

            databaseType = closing ? default : session.Transaction.GetDbType();
        }

        if (closing)
        {
            return Task.FromResult(new GetDatabaseTypeResponse
            {
                Status = TransactionWireCodes.Status(
                    RetCode.E_INVALID_TRANSACTION,
                    UnknownSessionDiagnostic),
            });
        }

        return Task.FromResult(new GetDatabaseTypeResponse
        {
            Status = TransactionWireCodes.Status(RetCode.OK),
            DatabaseType = databaseType,
        });
    }

    /// <summary>
    /// Reports the five statement status values and the three predicates computed from them -
    /// <c>of_isfailed</c>, <c>of_issucceeded</c> and <c>of_isbroken</c>
    /// [<c>n_cst_thread_trans.sru:L83</c>, <c>:L84</c>, <c>:L100</c>].
    /// </summary>
    /// <param name="request">The session to inspect.</param>
    /// <param name="context">The call context. Not consulted.</param>
    /// <returns>A completed task carrying the outcome and the session's state.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// ==== THE TRANSACTION'S PREDICATES ARE OVER SQLCode, NOT OVER THE RETURN-CODE ALGEBRA ==========
    /// </para>
    /// <para>
    /// <c>of_isfailed</c> is EXACTLY <c>SQLCode &lt; 0</c> [<c>:L337</c>] and <c>of_issucceeded</c> is
    /// EXACTLY <c>SQLCode &gt;= 0</c> [<c>:L340</c>]. <b>THEREFORE A DRIVER CODE OF 100 - "nothing
    /// found" - READS AS SUCCEEDED HERE</b>, and the same reading appears independently in
    /// <c>of_exec</c>, whose error test is <c>SQLCode &lt;&gt; 0 and SQLCode &lt;&gt; 100</c>
    /// [<c>:L233</c>].
    /// </para>
    /// <para>
    /// <b>THESE MUST NOT BE CONFLATED WITH THE SHARED KERNEL'S <c>IsSucceeded</c> / <c>IsFailed</c>,
    /// WHOSE SEMANTICS DIFFER.</b> That pair operates on the RETURN-CODE algebra, where <c>PREVENT</c>
    /// satisfies the success test so A PREVENTION READS AS A SUCCESS, and where <c>CANCELLED</c> is
    /// EXCLUDED from failure while also failing the success test - so it is NEITHER. The transaction's
    /// two predicates, by contrast, partition at zero and are exact complements. Both appear in this
    /// file and they are never substituted for one another: <c>BeginSession</c> uses the KERNEL
    /// predicates because it is testing return codes from the pool and from <c>of_connect</c>, and this
    /// member reports the TRANSACTION predicates because it is describing a driver field. Deciding this
    /// member's answer with <c>Predicates.IsFailed</c> would give a different verdict for exactly the
    /// values that matter, including 100.
    /// </para>
    /// <para>
    /// <b>Both predicates are reported rather than one, and computed by the transaction rather than
    /// re-derived here</b>, so a consumer cannot re-derive them with the wrong boundary. Three code
    /// spaces meet in this message and none of them is the others: <c>sql_code</c> is the runtime's,
    /// <c>sql_db_code</c> is the driver's, and <c>ret_code</c> on the status is the framework's.
    /// </para>
    /// <para>
    /// <b>A broken session is permanently unusable</b>: connect, commit and rollback all refuse it with
    /// <c>E_INVALID_TRANSACTION</c> [<c>:L113</c>, <c>:L241</c>], [<c>:L186</c>]. Only a successful
    /// connection clears the flag, through the connection-succeeded hook [<c>:L107-L108</c>], and
    /// reading the flag itself fires the check hook when it is not already set [<c>:L531-L535</c>] - so
    /// this is an INSPECTION WITH AN EFFECT, which is why it runs under the gate.
    /// </para>
    /// </remarks>
    [Authorize(Policy = PersistenceAuthorizationPolicies.Read)]
    public override Task<GetSessionStateResponse> GetSessionState(
        GetSessionStateRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!_sessions.TryResolve(request.Session, out TransactionSession? session) || session is null)
        {
            return Task.FromResult(new GetSessionStateResponse
            {
                Status = TransactionWireCodes.Status(
                    RetCode.E_INVALID_TRANSACTION,
                    UnknownSessionDiagnostic),
            });
        }

        GetSessionStateResponse response;

        using (session.Gate.Enter())
        {
            if (session.IsClosing)
            {
                // THE CLOSING GUARD. This handler looks like a pure read, but it is not: the broken
                // reading below calls the CHECK HOOK, which may condemn the transaction here and now
                // [:L531-L535]. On a retiring session that would condemn an object the pool has taken back
                // and may already have lent to somebody else. Refused with the unknown-session code,
                // because the handle the caller named is gone.
                return Task.FromResult(new GetSessionStateResponse
                {
                    Status = TransactionWireCodes.Status(
                        RetCode.E_INVALID_TRANSACTION,
                        UnknownSessionDiagnostic),
                });
            }

            IPooledTransaction transaction = session.Transaction;

            response = new GetSessionStateResponse
            {
                Status = TransactionWireCodes.Status(RetCode.OK),

                // The five values, in the order the oracle saves and restores them
                // [:L166-L170, :L176-L180]. Note the generated member is SqlNrows: the legacy field
                // spelling is a single lower-case token, so the generator PascalCases it whole.
                SqlCode = transaction.SqlCode,
                SqlDbCode = transaction.SqlDbCode,
                SqlNrows = transaction.SqlNRows,

                // 🔴 MASKED, AND IT IS THE ONLY MEMBER HERE THAT NEEDS TO BE. This is the provider's own
                // message, and what SQLite puts in it routinely includes the caller's data - a uniqueness
                // violation names the duplicated column, a constraint or type failure quotes the offending
                // value. The legacy could publish it safely because it published nothing: it is a library
                // and the field never left the process. Across this boundary it reaches a network peer, so
                // the same literal-scoped policy the statement field always had covers it too. A message
                // quoting no value is byte-identical after masking, so a caller reading this for display
                // loses nothing it was entitled to (constraints C-F, C-B).
                SqlErrText = SqlRedactor.Instance.Redact(transaction.SqlErrText),

                // NOT MASKED, DELIBERATELY. The return data is a stored-procedure OUT value the caller
                // itself asked for - it is the RESULT of the operation rather than a diagnostic about it,
                // and masking a result would break the operation instead of protecting anything.
                SqlReturnData = transaction.SqlReturnData,

                // [:L340] SQLCode >= 0 - which is why 100 is a success.
                Succeeded = transaction.IsSqlSucceeded(),

                // [:L337] SQLCode < 0. Also the discriminator the update veto uses to choose between a
                // database error and a clean cancellation, so its exact boundary matters beyond here.
                Failed = transaction.IsSqlFailed(),

                // [:L100] and its check-hook side effect at [:L531-L535].
                Broken = transaction.IsBroken(),
            };
        }

        return Task.FromResult(response);
    }

    /// <summary>
    /// Clears the five statement status values - <c>of_clearstate</c>
    /// [<c>n_cst_thread_trans.sru:L87</c>, <c>:L363-L368</c>].
    /// </summary>
    /// <param name="request">The session to clear.</param>
    /// <param name="context">The call context. Not consulted.</param>
    /// <returns>A completed task carrying the outcome.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Zeroes the three numeric values and blanks the two strings [<c>:L363-L367</c>]. The oracle calls
    /// it at the head of nearly every operation [<c>:L120</c>, <c>:L222</c>, <c>:L264</c>], so a caller
    /// rarely needs it; it is carried because the oracle exposes it publicly. There is no failure mode -
    /// it is a subroutine with no return value - so <c>OK</c> is the only answer.
    /// </remarks>
    [Authorize(Policy = PersistenceAuthorizationPolicies.Write)]
    public override Task<ClearStateResponse> ClearState(
        ClearStateRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!_sessions.TryResolve(request.Session, out TransactionSession? session) || session is null)
        {
            return Task.FromResult(new ClearStateResponse
            {
                Status = TransactionWireCodes.Status(
                    RetCode.E_INVALID_TRANSACTION,
                    UnknownSessionDiagnostic),
            });
        }

        bool closing;

        using (session.Gate.Enter())
        {
            // THE CLOSING GUARD, and this handler needs it as much as the ones that route through
            // RunAndProject: clearing state MUTATES the transaction, so on a session that is retiring it
            // would clear a transaction the pool has already taken back - and, with keep-alive on, one that
            // has already been handed to a different caller.
            closing = session.IsClosing;

            if (!closing)
            {
                session.Transaction.ClearState();
            }
        }

        return Task.FromResult(new ClearStateResponse
        {
            Status = closing
                ? TransactionWireCodes.Status(RetCode.E_INVALID_TRANSACTION, UnknownSessionDiagnostic)
                : TransactionWireCodes.Status(RetCode.OK),
        });
    }

    /// <summary>
    /// Marks the session permanently unusable - <c>of_setbroken</c>
    /// [<c>n_cst_thread_trans.sru:L99</c>, <c>:L526-L529</c>].
    /// </summary>
    /// <param name="request">The session to condemn.</param>
    /// <param name="context">The call context. Not consulted.</param>
    /// <returns>A completed task carrying the outcome.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <b>ONE-WAY.</b> Nothing in this contract clears the flag: only a successful connection does,
    /// through the connection-succeeded hook [<c>:L107-L108</c>]. Once set, connect, commit and rollback
    /// all refuse the session with <c>E_INVALID_TRANSACTION</c> [<c>:L113</c>, <c>:L241</c>,
    /// <c>:L186</c>], and the pool destroys and re-creates the entry's transaction the next time it is
    /// asked for one [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans_pool.sru:L158-L170</c>].
    /// </remarks>
    [Authorize(Policy = PersistenceAuthorizationPolicies.Write)]
    public override Task<SetBrokenResponse> SetBroken(
        SetBrokenRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!_sessions.TryResolve(request.Session, out TransactionSession? session) || session is null)
        {
            return Task.FromResult(new SetBrokenResponse
            {
                Status = TransactionWireCodes.Status(
                    RetCode.E_INVALID_TRANSACTION,
                    UnknownSessionDiagnostic),
            });
        }

        return Task.FromResult(new SetBrokenResponse
        {
            Status = RunAndProject(session, static transaction => transaction.SetBroken()),
        });
    }

    /// <summary>
    /// Derives a grid carrier syntax from a statement - <c>of_gridsyntaxfromsql</c>, both overloads
    /// [<c>n_cst_thread_trans.sru:L81</c>, <c>:L90</c>, <c>:L282-L290</c>, <c>:L386-L388</c>].
    /// </summary>
    /// <param name="request">The session and the statement.</param>
    /// <param name="context">The call context. Not consulted.</param>
    /// <returns>A completed task carrying the outcome and the derived syntax.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// <b>THE ORACLE REPORTS FAILURE THROUGH A <c>ref string</c> DIAGNOSTIC RATHER THAN A CODE</b> - the
    /// function's own return value is the SYNTAX, and the diagnostic travels out through a reference
    /// parameter [<c>:L282-L290</c>]. The diagnostic text therefore arrives on the wire as
    /// <c>error_text</c>, verbatim and OPAQUE: it may be non-English and it is neither translated nor
    /// parsed.
    /// </para>
    /// <para>
    /// ============ THE TWO LEGACY CONSUMERS DISAGREE ON THE FAILURE TEST, AND THE CONTRACT TAKES
    /// THE UNION ============
    /// </para>
    /// <para>
    /// The accessor itself decides NOTHING - it has no return code, only a syntax and a diagnostic - so
    /// its two consumers each pick their own test, and they are not the same test:
    /// </para>
    /// <list type="bullet">
    /// <item>
    /// <description>
    /// <c>of_query</c> tests the DERIVED SYNTAX for emptiness and ignores the diagnostic entirely -
    /// <c>if sSqlSyntax = "" then errInfo = "E_INVALID_SQL"; return RetCode.E_INVALID_SQL</c>
    /// [<c>:L312-L316</c>], which also OVERWRITES the diagnostic with the code's own name.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// the query task tests the DIAGNOSTIC for emptiness and ignores the syntax -
    /// <c>if sError &lt;&gt; "" then ... return RetCode.E_INVALID_SQL</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L610-L614</c>].
    /// </description>
    /// </item>
    /// </list>
    /// <para>
    /// <b>SO THIS RPC FAILS WHEN EITHER TEST FAILS: an empty syntax OR a non-empty diagnostic.</b> The
    /// union is the only reading that never reports success to a caller that one of the two legacy
    /// consumers would have failed - and it is the reading the published contract states, describing the
    /// syntax as empty on failure and a non-empty diagnostic as what becomes
    /// <c>E_INVALID_SQL</c>. Taking either test alone would let the OTHER consumer's failure case return
    /// <c>OK</c> across the boundary, which is a divergence the accessor's own silence would hide.
    /// </para>
    /// <para>
    /// An empty statement is refused with the same code before any derivation is attempted
    /// [<c>:L296-L299</c>].
    /// </para>
    /// <para>
    /// The derivation itself is the query surface's - this handler neither parses SQL nor builds a
    /// carrier definition. Only the syntax-derivation member of that seam is used here; retrieval and
    /// counting belong to C-05 and are deliberately not duplicated onto this contract, because two
    /// contracts for one operation would be two places to drift (constraint C-A).
    /// </para>
    /// </remarks>
    [Authorize(Policy = PersistenceAuthorizationPolicies.Read)]
    public override Task<GridSyntaxFromSqlResponse> GridSyntaxFromSql(
        GridSyntaxFromSqlRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!_sessions.TryResolve(request.Session, out TransactionSession? session) || session is null)
        {
            return Task.FromResult(new GridSyntaxFromSqlResponse
            {
                Status = TransactionWireCodes.Status(
                    RetCode.E_INVALID_TRANSACTION,
                    UnknownSessionDiagnostic),
            });
        }

        string sql = request.Sql;

        // [:L296-L299] `if sql = "" then errInfo = "E_INVALID_SQL"; return RetCode.E_INVALID_SQL` - the
        // consuming path's guard, reproduced including its diagnostic, which the oracle sets to the
        // code's own name rather than to a sentence.
        if (string.IsNullOrEmpty(sql))
        {
            return Task.FromResult(new GridSyntaxFromSqlResponse
            {
                Status = TransactionWireCodes.Status(RetCode.E_INVALID_SQL, nameof(RetCode.E_INVALID_SQL)),
            });
        }

        // 🔴 THE READ-SCOPE ADMISSIBILITY TEST, ON THE SECOND SURFACE THAT TAKES A CALLER-AUTHORED
        // STATEMENT. This RPC is published under the READ scope and its argument goes to the provider, so
        // it needs the same gate C-05's QuerySpec.sql does - a guard applied to one of the two and not the
        // other would leave the read scope reaching arbitrary SQL through whichever one was missed.
        //
        // THE DERIVATION PREPARES RATHER THAN EXECUTES - the surface asks the provider for a schema only -
        // so this test is defence in depth rather than the sole barrier. That is the reason it is here
        // rather than absent: "the provider will not step it" is a property of a collaborator this
        // handler does not own, and a guard that depends on one is a guard that breaks when the
        // collaborator changes. The refusal is the same code and the same fixed sentence C-05 answers, so
        // no new value enters a consumer's branch set (constraints C-G, C-F).
        if (!ReadOnlyStatementGuard.IsAdmissibleStatement(sql))
        {
            return Task.FromResult(new GridSyntaxFromSqlResponse
            {
                Status = TransactionWireCodes.Status(
                    RetCode.E_INVALID_SQL,
                    InadmissibleStatementDiagnostic),
            });
        }

        GridSyntaxOutcome derived;

        using (session.Gate.Enter())
        {
            derived = _querySurface.GridSyntaxFromSql(session.Transaction, sql);
        }

        // THE UNION OF THE TWO LEGACY TESTS - see this member's remarks. An empty syntax is of_query's
        // failure [:L313-L316]; a non-empty diagnostic is the query task's
        // [n_cst_thread_task_sqlquery.sru:L611-L613]. Either one fails the call, so a caller can never
        // be told OK for a derivation that one of the two consumers would have rejected.
        if (string.IsNullOrEmpty(derived.Syntax) || !string.IsNullOrEmpty(derived.ErrorText))
        {
            return Task.FromResult(new GridSyntaxFromSqlResponse
            {
                // The diagnostic travels verbatim when there is one; when there is none, the fallback is
                // the code's own name, which is exactly what the oracle writes into the diagnostic on
                // its empty-syntax arm [:L314]. The syntax is left empty on the response, because the
                // contract describes it as empty on failure - even in the case where a syntax WAS
                // derived alongside a diagnostic, which is the arm the query task rejects.
                Status = TransactionWireCodes.Status(
                    RetCode.E_INVALID_SQL,
                    string.IsNullOrEmpty(derived.ErrorText)
                        ? nameof(RetCode.E_INVALID_SQL)
                        : derived.ErrorText),
            });
        }

        return Task.FromResult(new GridSyntaxFromSqlResponse
        {
            // Both tests passed, so the diagnostic is empty by construction and there is nothing to
            // carry alongside the success.
            Status = TransactionWireCodes.Status(RetCode.OK),
            Syntax = derived.Syntax,
        });
    }
}
