// ==================================================================================================
//  PersistenceClient - DataServices' ONLY outbound edge to the Persistence service.
//  ------------------------------------------------------------------------------------------------
//  WHAT THIS FILE IS. The typed gRPC client covering four published contracts, entirely over the
//  GENERATED persistence.v1 client stubs:
//
//      C-05  persistence.v1.QueryService        retrieval, chunked and progressively delivered
//      C-06  persistence.v1.UpdateService       the optimistic-concurrency contract
//      C-07  persistence.v1.CommandService      statement execution
//      C-08  persistence.v1.TransactionService  session, autocommit, commit and rollback
//
//  WHY IT EXISTS AT ALL. The legacy PowerFramework is a LIBRARY: every call was in-process and
//  synchronous, and an in-process call CANNOT FAIL IN TRANSIT. Decomposition turned these calls into
//  network calls that can. This file is that new failure surface, made explicit and handled. Its
//  closest legacy ancestors are the four CALLER-SIDE proxies, whose own headers declare the execution
//  context as a contract rather than as commentary:
//
//      ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlquery.sru:L2    [运行在当前线程]
//      ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlupdate.sru:L2   [运行在当前线程]
//      ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlcommand.sru:L2  [运行在当前线程]
//      ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlbase.sru:L2     [运行在当前线程]
//
//  "runs on the calling thread" - against the worker-side twins which declare [运行在子线程], "runs on
//  the child thread". Every member below is the CALLER-SIDE half of one of those pairs, and each one
//  says so. The duality is preserved as an explicit boundary and is deliberately NOT flattened: the
//  managed expression of it is async request/response with a CancellationToken threaded through, so
//  this file never blocks a thread waiting for the far side. The legacy completion signal was a Win32
//  wait handle - of_iscommitted() is WaitForSingleObject(_hEvtCommitted,0) = 0
//  [n_cst_threading_task_sqlbase.sru:L177] - and its managed analogue is task completion, not a
//  blocking wait. There is no .Result, no .Wait(), no GetAwaiter().GetResult() and no async void
//  anywhere in this file.
//
//  WHY gRPC ON THIS EDGE (C-K). The legacy surface being replaced is ACTION-oriented, not
//  resource-oriented - Query, Update, Exec, Commit, Rollback, SQLErrText - so it is RPC-shaped. It
//  additionally needs three things at once that only Protobuf over gRPC supplies in the mandated
//  stack: compile-time contract enforcement over a surface whose thread affinity and buffer/status
//  model are load bearing; SERVER STREAMING, to reproduce the progressive result delivery of the
//  legacy recordset; and a status model rich enough to carry a four-value item-change alphabet, a
//  tri-valued veto, and a structured database error. JSON over REST would carry none of the three
//  without hand-written encoding on both sides.
//
//  WHAT CROSSES THIS BOUNDARY, AND WHAT DOES NOT
//  ------------------------------------------------------------------------------------------------
//  SQL TEXT, CLAUSE TEXT, FILTER AND SORT EXPRESSIONS ARE OPAQUE PAYLOAD HERE. This client does not
//  parse, validate, rewrite, escape, re-order or paginate any of them. The paged-SQL construction, the
//  n_sql clause parse and the DBT_MSSQL / DBT_ORACLE dialect dispatch are Persistence-internal
//  [n_cst_thread_task_sqlquery.sru:L303-L401, n_cst_thread_trans.sru:L60-L61, :L356-L359]. Attempting
//  any of it here would put a second SQL generator in the system.
//
//  NO STORAGE PROVIDER, NO DbContext, NO SQLite, NO EF Core, NO connection string, NO SQL execution
//  and NO paging rewriter (C-E). Exactly one service in this refactor holds a storage provider and it
//  is not this one; the project file does not even reference a data package. Data is reached ONLY
//  through C-05 through C-08.
//
//  THE GENERATED STUB IS THE WHOLE CROSS-SERVICE COUPLING (C-A). Nothing here references a type,
//  project or output belonging to services/persistence-service/** or services/security-service/**,
//  and there is no Gateway client: Gateway calls DataServices, never the reverse, and nothing but
//  DataServices calls Persistence. That keeps the topology layered and acyclic.
//
//  NOTHING IS GENERATED HERE EITHER. shared/PowerFramework.Contracts owns all .proto compilation
//  through a single Protobuf item with GrpcServices="Both", so this project declares no Protobuf item
//  at all - adding one produces 244 CS0436 diagnostics, each reporting that a locally generated type
//  conflicts with the imported type of the same name. No .proto is edited, no message type is
//  hand-written, and no local type duplicates a generated one.
//
//  HOW OUTCOMES ARE REPORTED, AND WHY IT IS NOT EXCEPTIONS
//  ------------------------------------------------------------------------------------------------
//  A CONTRACT STATUS IS RETURNED, NEVER THROWN. Every unary member answers the contract's own response
//  message unchanged, and the caller reads OperationStatus.ret_code from it. That is not a style
//  preference: the legacy return algebra is TRI-STATE, and throwing on "not OK" would destroy it.
//  A prevention (1) reads as SUCCEEDED because the predicate tests for zero or greater
//  [ws_objects/pfw.shared.pbl.src/issucceeded.srf:L11-L13]; CANCELLED (-2) is explicitly excluded from
//  failure while also failing the success test [isfailed.srf:L11-L13], so it is NEITHER; and null is
//  likewise neither. Classification therefore goes through the PORTED predicates of
//  PowerFramework.Shared.Kernel rather than an ad-hoc comparison written here, and a null is never
//  collapsed to zero - doing so converts "neither succeeded nor failed" into "succeeded".
//
//  ONLY TWO THINGS THROW. A transport failure surfaces as the gRPC RpcException it already is, and an
//  optimistic-concurrency conflict surfaces as PersistenceConflictException carrying the contract's
//  ConflictDetail INTACT. Argument nulls throw ArgumentNullException, because a null request is a
//  programming error rather than a domain outcome.
//
//  THE TWO RetCode FAMILIES, AND THE ONE PLACE THEY MEET
//  ------------------------------------------------------------------------------------------------
//  PowerFramework.Contracts.Common.V1.RetCode is a generated protobuf WRAPPER MESSAGE whose nested
//  Types.Value enum is the wire code. PowerFramework.Shared.Kernel.RetCode is the ported static class
//  of long constants. Their numeric values are declared to agree value for value, but that agreement
//  is a REVIEW-TIME INVARIANT WITH NO COMPILE-TIME ENFORCEMENT - the contracts project deliberately
//  holds no reference to the shared kernel. Nothing here blind-casts between them: the ONE conversion
//  lives in WidenToKernelRetCode, which states the invariant at the point it relies on it. The wire
//  enum is reached through the WireRetCode alias below; the kernel class is never named by its simple
//  name in this file, so the CS0104 ambiguity a flat-namespace migration invites cannot arise.
//
//  The same care applies to the modify style. Enums.SQL_MS_REPLACE = 1, SQL_MS_APPEND = 2 and
//  SQL_MS_PREPEND = 3 live in the shared kernel [ws_objects/pfw.shared.pbl.src/enums.sru:L718-L720],
//  while persistence.v1 declares its own SqlModifyStyle - in the PERSISTENCE file, not the common one
//  - carrying those three values plus a proto3 zero member the legacy has no equivalent for. The one
//  mapping lives in ToWireModifyStyle, and SQL_MS_UNSPECIFIED IS NEVER SENT as a real modification
//  style: an unmapped value is rejected locally with the legacy's own E_INVALID_ARGUMENT.
//
//  REDACTION - THE ONE FIELD THAT MAY NEVER BE LOGGED
//  ------------------------------------------------------------------------------------------------
//  common.v1.DbError mirrors ws_objects/pfw.thread.ext.pbl.src/dberrordata.srs field for field:
//  sqldbcode, sqlerrtext, sqlsyntax, buffer, row. THE sqlsyntax FIELD CARRIES THE COMPLETE GENERATED
//  STATEMENT INCLUDING INTERPOLATED LITERAL VALUES, and the legacy logger performed no redaction at
//  all. The evidence is exact: n_cst_thread_task_sqlcommand.sru:L110 passes sSQL - the statement AFTER
//  _of_SQLBindParams has spliced literal values into it [n_cst_thread_task_sqlbase.sru:L464] -
//  straight into the third argument of the database-error event, which is dberrordata.sqlsyntax. The
//  root cause is a connection setting: a bind-disabling flag means the runtime does not use bind
//  variables, so literals are interpolated [n_cst_thread_task_sqlbase.sru:L127-L132].
//
//  This file therefore NEVER logs, echoes, or places into an exception message the sqlsyntax value, at
//  any level, in any environment, in any ToString, and in no debug-only branch. It assumes the field
//  arrives already redacted or split into statement-plus-parameters by Persistence's own redactor and
//  treats it as sensitive regardless - defence in depth, because relying on the sender to have
//  redacted it is exactly how an unredacted statement reaches a log. sqlerrtext is withheld on the
//  same grounds, since driver text can quote the offending literal back. The numeric code, the buffer
//  and the row are recorded; the whole payload stays on the response for a caller entitled to it.
//  The transaction descriptor's logid, logpass and dbparm and every changeset or row payload are
//  never logged either.
//
//  RULES POSITION. review_rules returns exactly "No user rules provided." That is a finding rather
//  than latitude. The binding constraints are the twelve non-rule constraints C-A through C-L of the
//  transformation plan, its performance position, and its enterprise baseline. Those governing this
//  file are cited at the decisions they shape rather than restated at length here.
//
//  NO PERFORMANCE OBJECTIVE IS ASSERTED ANYWHERE IN THIS FILE. The repository publishes no
//  service-level agreement, no latency budget, no throughput target and no availability commitment, so
//  none may be claimed, met, or used to justify a choice. Three things in particular are NOT
//  optimizations and are not described as such: the resilience pipeline the composition root attaches
//  exists solely because an in-process call could not fail in transit and a network call can; server
//  streaming reproduces the legacy's progressive delivery; and the leading-@ statement prefix is a
//  preserved legacy affordance carried through verbatim.
// ==================================================================================================

using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.CompilerServices;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Grpc.Core;
using PowerFramework.Contracts.Common.V1;
using PowerFramework.Contracts.Persistence.V1;
using PowerFramework.Shared.Diagnostics;
using PowerFramework.Shared.Kernel;

// THE ONE NAME COLLISION IN THIS FILE, RESOLVED DELIBERATELY RATHER THAN DISCOVERED AS A BUILD BREAK.
// `RetCode` exists in BOTH imported contract-side and kernel-side namespaces, so a bare `RetCode` here
// would be CS0104 "ambiguous reference". This alias names the WIRE enum, which is what every response
// in this file carries. PowerFramework.Shared.Kernel is imported for `Predicates` - so that success and
// failure are classified by the ported tri-state algebra rather than by a comparison invented here -
// and for `Enums`, whose SQL_MS_* constants are the legacy modify-style values.
using WireRetCode = PowerFramework.Contracts.Common.V1.RetCode.Types.Value;

namespace PowerFramework.DataServices.Clients;

/// <summary>
/// Reports the optimistic-concurrency mismatch of contract <b>C-06</b>, carrying the contract's own
/// <see cref="ConflictDetail"/> intact so a caller can re-read and rebase, or surface it.
/// </summary>
/// <remarks>
/// <para>
/// THE MISMATCH ARRIVES AS THE gRPC STATUS <c>Aborted</c>, and the detail travels as a versioned BINARY
/// TRAILER rather than as a response field, because a gRPC status carries only a code and a message.
/// <c>Aborted</c> rather than <c>FailedPrecondition</c> is the contract's deliberate choice: the
/// operation MAY succeed if the caller re-reads and retries at a higher level, which is what
/// <c>Aborted</c> means, and it is the canonical gRPC mapping to HTTP 409 - which is how the REST
/// projection consumed by Gateway surfaces this same payload.
/// </para>
/// <para>
/// THE DETAIL IS SURFACED UNALTERED. It is not swallowed, not summarised, not downgraded to a generic
/// failure, and the update is NEVER re-attempted - by this client or by any resilience policy attached
/// to it. There is no silent overwrite anywhere in this system. Callers implement an explicit
/// retry-or-surface policy; this client's job is faithful transmission. The originating
/// <see cref="RpcException"/> is retained as the inner exception, so the raw status and the complete
/// trailer set remain available for anything this type does not project.
/// </para>
/// <para>
/// NET-NEW, SO THERE IS NO LEGACY PORT TO LOOK FOR. The legacy has no conflict branch at all: a
/// mismatch surfaces there as an update result other than 1 and a driver code of -1, reported as
/// <c>E_DB_ERROR</c> [ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru:L208-L210,
/// :L214, :L250]. <c>Aborted</c> plus <see cref="ConflictDetail"/> is a contract behaviour introduced
/// by the decomposition, so this type maps a new status rather than reproducing an old one.
/// </para>
/// <para>
/// WHY BOTH VALUE SETS TRAVEL PER ROW. <see cref="ConflictRow"/> carries current AND original values
/// because the legacy DataWindow marks <c>updatewhere=1</c> - the key-and-updateable-columns
/// concurrency mode - with every column marked
/// [ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L8-L14], so the failed statement compared the ORIGINAL
/// value of every marked column. A payload with only current values could not tell a caller which
/// column moved underneath it.
/// </para>
/// </remarks>
public sealed class PersistenceConflictException : Exception
{
    /// <summary>The message used when no more specific one is supplied.</summary>
    private const string DefaultMessage =
        "Persistence reported an optimistic-concurrency conflict. The submitted rows carried original "
        + "values that no longer match the current server state, so the update was not applied.";

    /// <summary>Creates the exception with the default message and no detail.</summary>
    public PersistenceConflictException()
        : base(DefaultMessage)
    {
    }

    /// <summary>Creates the exception with a caller-supplied message and no detail.</summary>
    /// <param name="message">The message.</param>
    public PersistenceConflictException(string? message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a caller-supplied message and cause, and no detail.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The cause.</param>
    public PersistenceConflictException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Creates the exception from a decoded conflict trailer, retaining the originating gRPC failure.
    /// </summary>
    /// <param name="conflict">
    /// The conflict detail exactly as it arrived. Stored by reference and never copied, edited,
    /// re-sorted or trimmed.
    /// </param>
    /// <param name="retCode">
    /// The reconciled outcome code the trailer carried, numerically a return code of the ported
    /// algebra, so that a caller classifying the failure need not decode the detail at all.
    /// </param>
    /// <param name="innerException">
    /// The originating failure, retained so its status and its complete trailer set stay reachable.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="conflict"/> or <paramref name="innerException"/> is <see langword="null"/>.
    /// </exception>
    public PersistenceConflictException(
        ConflictDetail conflict,
        long retCode,
        RpcException innerException)
        : base(BuildMessage(conflict, retCode), innerException)
    {
        ArgumentNullException.ThrowIfNull(conflict);
        ArgumentNullException.ThrowIfNull(innerException);

        Conflict = conflict;
        RetCode = retCode;
        Status = innerException.Status;
        Trailers = innerException.Trailers;
    }

    /// <summary>
    /// The conflict detail as received, or <see langword="null"/> when the exception was constructed
    /// without one.
    /// </summary>
    public ConflictDetail? Conflict { get; }

    /// <summary>The reconciled outcome code the trailer carried, or zero when none was decoded.</summary>
    public long RetCode { get; }

    /// <summary>The originating gRPC status.</summary>
    public Status Status { get; }

    /// <summary>The originating trailer set, so nothing this type does not project is lost.</summary>
    public Metadata? Trailers { get; }

    /// <summary>
    /// Builds the message from the counts the detail carries.
    /// </summary>
    /// <param name="conflict">The detail.</param>
    /// <param name="retCode">The reconciled outcome code.</param>
    /// <returns>The message.</returns>
    /// <remarks>
    /// COUNTS AND THE TABLE NAME ONLY. No column value, no original value and no statement text reaches
    /// the message: a conflict payload is caller data, and an exception message is the single most
    /// likely thing to be written to a log. The whole detail remains on <see cref="Conflict"/> for a
    /// caller entitled to it.
    /// </remarks>
    private static string BuildMessage(ConflictDetail? conflict, long retCode)
    {
        if (conflict is null)
        {
            return DefaultMessage;
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"Persistence reported an optimistic-concurrency conflict on update table "
            + $"'{conflict.UpdateTable}': {conflict.Rows.Count} conflicting row(s), "
            + $"{conflict.RowsExpected} row(s) expected and {conflict.RowsMatched} matched; "
            + $"retCode {retCode}. The update was not applied and was not retried.");
    }
}

/// <summary>
/// The typed gRPC client for contracts <b>C-05</b> <c>QueryService</c>, <b>C-06</b>
/// <c>UpdateService</c>, <b>C-07</b> <c>CommandService</c> and <b>C-08</b> <c>TransactionService</c> -
/// DataServices' only outbound edge to Persistence.
/// </summary>
/// <remarks>
/// <para>
/// CONSTRUCTOR INJECTION THROUGHOUT, AND NOTHING SELF-CONFIGURED. The four generated stubs, the token
/// provider and the logger all arrive as arguments. This type constructs no channel, reads no
/// configuration, reads no environment variable, sets no address and no deadline, contains no hostname
/// and no port literal, and holds no static mutable state. The composition root owns the channel, the
/// upstream address - which comes only from <c>DataServices:Persistence:Address</c> - and the
/// resilience pipeline, so an environment can move Persistence without touching this file.
/// </para>
/// <para>
/// IT PERFORMS NO INPUT OR OUTPUT DURING CONSTRUCTION, in a static initializer, or in any eager
/// warm-up path, which is what lets the host start with Persistence absent.
/// </para>
/// <para>
/// EVERY OUTBOUND CALL CARRIES A SECURITY-ISSUED BEARER CREDENTIAL, obtained through
/// <see cref="IServiceTokenProvider"/> (contract <b>C-01</b>). The provider is a REQUIRED constructor
/// argument, so this client cannot be constructed in a way that silently issues an unauthenticated
/// call. Every newly created boundary in this system is authenticated from the outset, including the
/// internal ones: the legacy library opened no listening socket and received no unsolicited request,
/// so decomposition creates every one of these edges from nothing, and an internal edge that trusted
/// its caller because "it is internal" would be a new unauthenticated surface. This service holds
/// VERIFICATION MATERIAL ONLY - it mints nothing, holds no signing key, and does not even reference a
/// library that could parse a token. The credential is treated as an opaque string.
/// </para>
/// <para>
/// THE SUBSTITUTION SEAM IS THE GENERATED STUB. Each generated client declares every method
/// <c>virtual</c> and exposes a protected parameterless constructor expressly so a test double can be
/// derived from it, so the whole outbound path - including the conflict path and the streaming path -
/// is exercisable with no channel, no host and no Persistence instance anywhere. This type is
/// additionally left UNSEALED with virtual operations so a consumer's own test may double it in turn;
/// that is a deliberate testability decision rather than an extension point, and derived types are
/// expected to override for substitution only.
/// </para>
/// <para>
/// THREAD SAFETY. The generated stubs and the token provider are safe for concurrent use, and the only
/// mutable state here is the in-flight task registry, which is a concurrent dictionary. Concurrent
/// calls on DIFFERENT tasks proceed independently; a second operation on a task already in flight
/// through this instance is rejected with the legacy's own busy code rather than being allowed to
/// corrupt in-flight state.
/// </para>
/// </remarks>
public class PersistenceClient
{
    /// <summary>
    /// The default rows-per-chunk of the legacy query task, preserved exactly.
    /// </summary>
    /// <remarks>
    /// PRESERVED LEGACY DEFAULT. <c>long _nChunkSize = 10000</c>, whose own trailing comment reads
    /// "Chunk row count,min:1000"
    /// [ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L34]. Published so a caller and
    /// a characterization test can name the legacy value instead of restating the literal.
    /// </remarks>
    public const long DefaultChunkSize = 10_000L;

    /// <summary>
    /// The smallest rows-per-chunk the legacy accepts: one thousand and one.
    /// </summary>
    /// <remarks>
    /// PRESERVED LEGACY GUARD, AND THE BOUNDARY IS THE POINT. The legacy test is
    /// <c>if chunkSize &lt;= 1000 then return RetCode.E_INVALID_ARGUMENT</c>
    /// [n_cst_thread_task_sqlquery.sru:L410]. The comparison is "less than or equal", so ONE THOUSAND
    /// ITSELF IS INVALID and one thousand and one is the smallest legal value. The neighbouring comment
    /// at :L34 says "min:1000" and therefore disagrees with the code by one; that inconsistency is
    /// legacy behaviour and is NOT corrected here - the code is what runs, so the code is what is
    /// reproduced.
    /// </remarks>
    public const long SmallestValidChunkSize = 1_001L;

    /// <summary>
    /// The select-statement index the two-argument legacy clause setters imply: one.
    /// </summary>
    /// <remarks>
    /// PRESERVED LEGACY ARITY. The caller-side proxy exposes both a two-argument clause setter and a
    /// three-argument one carrying an explicit index
    /// [ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlquery.sru:L73-L76], and the index is
    /// ONE-BASED: the worker rejects a value of zero or less outright rather than treating it as a
    /// default [n_cst_thread_task_sqlquery.sru:L271, :L288]. The convenience overloads below supply this
    /// value for the two-argument form so the index is never silently rebased.
    /// </remarks>
    public const int DefaultSelectIndex = 1;

    /// <summary>
    /// Whether the legacy query task counts pages by default: it does.
    /// </summary>
    /// <remarks>
    /// PRESERVED LEGACY DEFAULT. <c>PrivateWrite Boolean #PageCounting = true</c>
    /// [n_cst_threading_task_sqlquery.sru:L40], mirrored on the worker at
    /// [n_cst_thread_task_sqlquery.sru:L38].
    /// </remarks>
    public const bool DefaultPageCounting = true;

    /// <summary>
    /// The number of positional arguments the legacy statement-execution surface could accept: eleven.
    /// </summary>
    /// <remarks>
    /// HISTORICAL CONTEXT ONLY, AND DELIBERATELY NOT ENFORCED. The ceiling came from PowerScript's lack
    /// of variadic calls, which forced the legacy binding to be written as twelve overloads from
    /// <c>Exec(cmd)</c> to <c>Exec(cmd, arg1 .. arg11)</c>
    /// [ws_objects/pfw.utility.sqlite.pbl.src/n_sqlite.sru:L32-L43]. C# has native variadic support, so
    /// the contract may exceed the ceiling without any behavioural regression, and the parameter list on
    /// this client is an ordinary collection. This constant exists so a reader can recognise the number
    /// if it appears in a legacy recording; nothing here compares against it.
    /// </remarks>
    public const int LegacyExecArgumentCeiling = 11;

    /// <summary>
    /// The gRPC metadata key carrying the bearer credential.
    /// </summary>
    /// <remarks>
    /// Lower-case because gRPC metadata keys are case-insensitive by specification and are normalised to
    /// lower case on the wire.
    /// </remarks>
    private const string AuthorizationHeaderName = "authorization";

    /// <summary>
    /// The identity DataServices claims when it asks Security for a service token.
    /// </summary>
    /// <remarks>
    /// A NON-SECRET PROTOCOL IDENTIFIER, NOT A CREDENTIAL. It is a name, it authenticates nothing on its
    /// own, and it matches this service's own inbound audience so one identity describes the service in
    /// both directions. Everything genuinely sensitive - key material, the upstream addresses - binds
    /// from environment configuration in the composition root and never appears in this file (C-F).
    /// </remarks>
    private const string TokenSubject = "powerframework-dataservices";

    /// <summary>
    /// The single audience DataServices requests a token for when calling Persistence.
    /// </summary>
    /// <remarks>
    /// Follows the audience naming convention already fixed by each service's own inbound configuration,
    /// so Persistence's audience is its service name under the same prefix. The token contract carries
    /// ONE audience per request, deliberately, so a credential is never valid somewhere its holder did
    /// not intend it to be.
    /// </remarks>
    private const string PersistenceAudience = "powerframework-persistence";

    /// <summary>The scope requested for the reading half of this edge: C-05 and the C-08 reads.</summary>
    private const string ReadScope = "persistence.read";

    /// <summary>The scope requested for the writing half: C-06, C-07 and the C-08 state changes.</summary>
    private const string WriteScope = "persistence.write";

    /// <summary>
    /// The C-06 method whose rich-error binding declares how a conflict payload is carried.
    /// </summary>
    private const string UpdateMethodName = "Update";

    /// <summary>
    /// The token request made for a call that only reads.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>ONE SCOPE PER REQUEST, AND THE REQUEST IS CHOSEN BY THE OPERATION (constraint C-G).</b> A
    /// single credential asking for read AND write was attached to every call this client makes, which
    /// meant a retrieval carried the authority to update - so any weakness anywhere on the read path
    /// borrowed the write path's privileges. Each call now presents a credential for exactly what it is
    /// about to do, and the upstream enforces the same two names per RPC.
    /// </para>
    /// <para>
    /// Built once because <see cref="ServiceTokenRequest"/> validates on construction and copies its
    /// scope set defensively, which makes the instance immutable and safe to share.
    /// </para>
    /// <para>
    /// <b>A NARROWED GRANT IS NO LONGER PROCEEDED ON.</b> The token contract permits the issuer to grant
    /// less than was asked for, and it remains a successful token response - but with one scope per
    /// request, a narrowing can only mean the ONE scope the call requires was withheld, and continuing
    /// would send a request the upstream must refuse. See
    /// <see cref="CreateAuthenticatedCallOptionsAsync"/>.
    /// </para>
    /// </remarks>
    private static readonly ServiceTokenRequest ReadTokenRequest =
        new(TokenSubject, PersistenceAudience, [ReadScope]);

    /// <summary>
    /// The token request made for a call that changes stored data or shared transaction state.
    /// </summary>
    /// <remarks>
    /// It does NOT also carry <see cref="ReadScope"/>. Write does not imply read anywhere in this system,
    /// and a write-shaped RPC on the upstream requires only the write name, so adding the read name would
    /// be privilege this call has no use for.
    /// </remarks>
    private static readonly ServiceTokenRequest WriteTokenRequest =
        new(TokenSubject, PersistenceAudience, [WriteScope]);

    /// <summary>
    /// The rich-error binding declared on C-06's <c>Update</c>, read from the generated descriptor.
    /// </summary>
    /// <remarks>
    /// READ FROM THE DESCRIPTOR RATHER THAN HARDCODED, deliberately. A gRPC status carries only a code
    /// and a message, so the conflict detail travels as a versioned BINARY TRAILER, and the contract
    /// declares the trailer key, the accompanying status code and the payload type as a custom method
    /// option precisely so a client discovers all three from the descriptor instead of from a comment.
    /// Taking them from here means a contract revision that renames the key or moves the status is
    /// picked up without a matching edit in this file.
    /// <para>
    /// This is local reflection over generated metadata, not input or output, so evaluating it in a
    /// static initializer keeps startup free of any dependency on Persistence being reachable. Both
    /// lookups answer <see langword="null"/> rather than throwing when absent, so type initialization
    /// cannot fail here; an absent binding is handled where it is used.
    /// </para>
    /// </remarks>
    private static readonly RichErrorBinding? UpdateRichErrorBinding = ReadUpdateRichErrorBinding();

    /// <summary>
    /// The fully-qualified name of the rich-error payload type this client can decode.
    /// </summary>
    /// <remarks>Taken from the generated descriptor rather than written as a literal.</remarks>
    private static readonly string SupportedRichErrorPayloadType = RichErrorTrailer.Descriptor.FullName;

    /// <summary>The generated C-05 stub.</summary>
    private readonly QueryService.QueryServiceClient _query;

    /// <summary>The generated C-06 stub.</summary>
    private readonly UpdateService.UpdateServiceClient _update;

    /// <summary>The generated C-07 stub.</summary>
    private readonly CommandService.CommandServiceClient _command;

    /// <summary>The generated C-08 stub.</summary>
    private readonly TransactionService.TransactionServiceClient _transaction;

    /// <summary>Supplies the bearer credential for every outbound call.</summary>
    private readonly IServiceTokenProvider _tokenProvider;
    private readonly OutboundDeadlines _deadlines;

    /// <summary>The logger. Never receives a credential, a statement, a descriptor or a row payload.</summary>
    private readonly ILogger<PersistenceClient> _logger;

    /// <summary>
    /// The identifiers of the server-held tasks currently carrying an operation issued through this
    /// client instance.
    /// </summary>
    /// <remarks>
    /// THE BUSY GUARD, AND IT IS A PRESERVED LEGACY BEHAVIOUR RATHER THAN AN INVENTION. Every setter on
    /// every caller-side legacy proxy opens with <c>if of_IsBusy() then return RetCode.E_BUSY</c> - see
    /// [n_cst_threading_task_sqlbase.sru:L57, :L73, :L95, :L110, :L124, :L144, :L162, :L179, :L188],
    /// [n_cst_threading_task_sqlcommand.sru:L34, :L43], [n_cst_threading_task_sqlupdate.sru:L99, :L106,
    /// :L111, :L125, :L207, :L223, :L236, :L246] and
    /// [n_cst_threading_task_sqlquery.sru:L386, :L391, :L396, :L404] - and the predicate itself is a
    /// zero-timeout wait on the task's synchronization handle [n_cst_threading.sru:L310-L311]. Mutating
    /// a task's settings while its operation is running would corrupt in-flight state, which is what
    /// that guard exists to prevent.
    /// <para>
    /// TWO LEVELS, EXACTLY AS THE LEGACY HAD TWO. This registry reproduces the CALLER-SIDE half and
    /// covers operations issued through this instance; Persistence enforces its own, authoritative half
    /// - the worker-side <c>if #Running then return RetCode.E_BUSY</c>
    /// [n_cst_thread_task_sqlcommand.sru:L32] - for everything else, including a second client instance.
    /// Neither level is redundant, and neither is a substitute for the other.
    /// </para>
    /// <para>
    /// KEYED BY TASK IDENTIFIER because the task is the unit the legacy guard protected: the guards live
    /// on the TASK proxies. Session-scoped C-08 members carry no such guard for the same reason - the
    /// legacy transaction object has none, and the contract relocated commit and rollback from the task
    /// proxy to the session, so there is no task whose busy state could be tested. Persistence's own
    /// state machine governs those instead, refusing a commit or a rollback while autocommit is on
    /// [n_cst_thread_trans.sru:L240, :L185].
    /// </para>
    /// </remarks>
    private readonly ConcurrentDictionary<string, byte> _inFlightTasks = new(StringComparer.Ordinal);

    /// <summary>
    /// Creates the client over already-configured generated stubs.
    /// </summary>
    /// <param name="queryClient">
    /// The generated C-05 stub. Injected rather than constructed so the composition root keeps ownership
    /// of the channel, the address and the resilience pipeline, and so a test can substitute a derived
    /// double.
    /// </param>
    /// <param name="updateClient">The generated C-06 stub, on the same terms.</param>
    /// <param name="commandClient">The generated C-07 stub, on the same terms.</param>
    /// <param name="transactionClient">The generated C-08 stub, on the same terms.</param>
    /// <param name="tokenProvider">
    /// Supplies the bearer credential for every outbound call. The sibling Security client is its
    /// implementation; token acquisition is never duplicated here. REQUIRED, so an unauthenticated call
    /// is unreachable by construction.
    /// </param>
    /// <param name="logger">
    /// The logger. It never receives a credential, a statement, a transaction descriptor, a changeset or
    /// a row payload.
    /// </param>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// PERFORMS NO INPUT OR OUTPUT. Nothing here connects, resolves, probes or warms anything up.
    /// </remarks>
    public PersistenceClient(
        QueryService.QueryServiceClient queryClient,
        UpdateService.UpdateServiceClient updateClient,
        CommandService.CommandServiceClient commandClient,
        TransactionService.TransactionServiceClient transactionClient,
        IServiceTokenProvider tokenProvider,
        ILogger<PersistenceClient> logger,
        OutboundDeadlines? deadlines = null)
    {
        ArgumentNullException.ThrowIfNull(queryClient);
        ArgumentNullException.ThrowIfNull(updateClient);
        ArgumentNullException.ThrowIfNull(commandClient);
        ArgumentNullException.ThrowIfNull(transactionClient);
        ArgumentNullException.ThrowIfNull(tokenProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _query = queryClient;
        _update = updateClient;
        _command = commandClient;
        _transaction = transactionClient;
        _tokenProvider = tokenProvider;
        _deadlines = deadlines ?? OutboundDeadlines.Default;
        _logger = logger;
    }

    // ==============================================================================================
    //  CONTRACT C-05 - persistence.v1.QueryService
    //  ----------------------------------------------------------------------------------------------
    //  The RETRIEVAL third of the retrieval / validation / update triple. Eleven operations: the task
    //  lifecycle, the settings the legacy exposed as of_Set* on its caller-side proxy, the streamed
    //  retrieval, and the count.
    //
    //  CALL ORDERING IS OBSERVABLE, so this client issues settings in the order the caller made them
    //  and never re-orders, coalesces or batches them. The clearest case: naming the source object
    //  CLEARS the statement syntax [n_cst_thread_task_sqlquery.sru:L423], so a caller that sets the
    //  syntax and then the source object ends with no syntax, while the reverse order keeps both. A
    //  client that "helpfully" merged the two settings into one request would silently change which
    //  statement runs.
    // ==============================================================================================

    /// <summary>
    /// Creates a server-held query task. Contract <b>C-05</b>. The wire form of the legacy object
    /// creation, and the caller-side half of the legacy proxy pair.
    /// </summary>
    /// <param name="request">
    /// The session the task belongs to and, optionally, the whole initial specification. Carrying the
    /// specification here is equivalent to creating the task and then issuing each setter, and it is the
    /// caller's choice which form to use.
    /// </param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The contract's response, carrying the outcome code and the task handle.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="RpcException">The call failed in transit or the upstream refused it.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <remarks>
    /// RETRY SAFETY: safe to retry, but each successful attempt creates a SEPARATE server-held task that
    /// must be released.
    /// </remarks>
    public virtual Task<CreateQueryTaskResponse> CreateQueryTaskAsync(
        CreateQueryTaskRequest request,
        CancellationToken cancellationToken) =>
        InvokeAsync(_query.CreateQueryTaskAsync, request, ReadTokenRequest, cancellationToken);

    /// <summary>
    /// Releases a server-held query task. Contract <b>C-05</b>. The wire form of the legacy
    /// <c>Destroy</c>.
    /// </summary>
    /// <param name="request">The task handle.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The contract's response.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="RpcException">The call failed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <remarks>
    /// DELIBERATELY NOT BUSY-GUARDED, and the asymmetry is the legacy's. The legacy guards
    /// <c>of_reset</c> and every setter but not the destructor, and guarding release here would leave a
    /// caller unable to clean up after an operation it could not complete. Persistence owns whether a
    /// release is admissible while its own work is running.
    /// <para>
    /// 🔴 RETRY SAFETY: <b>NOT SAFE.</b> A release CONSUMES the registration: the server removes it and
    /// then answers <c>E_INVALID_HANDLE</c> when there is nothing to remove
    /// [<c>Grpc/QueryService.ReleaseQueryTask</c>], and the caller-side cleanup path reads a non-OK answer
    /// as evidence of a leak - it records "the server may still be holding it"
    /// [<see cref="ReleaseWorkTaskAsync"/>]. So a replay after a SUCCESSFUL first attempt whose response
    /// was lost manufactures an operator warning asserting the opposite of the truth. Excluded from the
    /// replay-safe roster [<c>Clients/OutboundCallPolicy</c>]; a task whose release never arrives is
    /// reclaimed by the registry rather than by a retry.
    /// </para>
    /// </remarks>
    public virtual Task<ReleaseQueryTaskResponse> ReleaseQueryTaskAsync(
        ReleaseQueryTaskRequest request,
        CancellationToken cancellationToken) =>
        InvokeAsync(_query.ReleaseQueryTaskAsync, request, ReadTokenRequest, cancellationToken);

    /// <summary>
    /// Resets a query task's accumulated settings. Contract <b>C-05</b>, legacy <c>of_reset</c>.
    /// </summary>
    /// <param name="request">The task handle.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>
    /// The contract's response, or a locally produced busy rejection when an operation on the same task
    /// is in flight through this client.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="RpcException">The call failed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <remarks>
    /// Named for its task kind rather than plainly <c>Reset</c> because THREE of the four contracts
    /// declare a <c>Reset</c> method, on three different handles; one undifferentiated name here would
    /// make the wrong one reachable by mistake.
    /// </remarks>
    public virtual Task<ResetQueryTaskResponse> ResetQueryTaskAsync(
        ResetQueryTaskRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return RejectIfTaskBusy(request.Task, static status => new ResetQueryTaskResponse { Status = status })
            ?? InvokeAsync(_query.ResetAsync, request, ReadTokenRequest, cancellationToken);
    }

    /// <summary>
    /// Sets the rows-per-chunk of a streamed retrieval. Contract <b>C-05</b>, legacy
    /// <c>of_setchunksize</c>.
    /// </summary>
    /// <param name="request">The task handle and the requested rows per chunk.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>
    /// The contract's response, or a locally produced rejection carrying the legacy's own outcome code
    /// when the value is at or below one thousand, or when the task is busy through this client.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="RpcException">The call failed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <remarks>
    /// PRESERVED LEGACY GUARD, REPRODUCED AT THE SAME BOUNDARY AND WITH THE SAME CODE. The legacy test is
    /// <c>if chunkSize &lt;= 1000 then return RetCode.E_INVALID_ARGUMENT</c>
    /// [n_cst_thread_task_sqlquery.sru:L410], so <see cref="SmallestValidChunkSize"/> is the smallest
    /// legal value and <see cref="DefaultChunkSize"/> is what the task uses when nothing is set. Applying
    /// it here is not a client-side behaviour addition: the outcome a caller observes - that code, and no
    /// change to task state - is identical either way, and a rejected setting never reached the server's
    /// state in the legacy either.
    /// </remarks>
    public virtual Task<SetChunkSizeResponse> SetChunkSizeAsync(
        SetChunkSizeRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        static SetChunkSizeResponse Shape(OperationStatus status) => new() { Status = status };

        Task<SetChunkSizeResponse>? busy = RejectIfTaskBusy(request.Task, Shape);
        if (busy is not null)
        {
            return busy;
        }

        if (request.ChunkSize < SmallestValidChunkSize)
        {
            return Task.FromResult(Shape(InvalidArgument(
                "The requested chunk size is not accepted: the legacy guard rejects any value at or "
                + "below 1000 [ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L410], "
                + "so 1001 is the smallest legal value.")));
        }

        return InvokeAsync(_query.SetChunkSizeAsync, request, ReadTokenRequest, cancellationToken);
    }

    /// <summary>
    /// Sets the maximum number of rows a retrieval may return. Contract <b>C-05</b>, legacy
    /// <c>of_setmaxrows</c>.
    /// </summary>
    /// <param name="request">The task handle and the row ceiling. Zero means no ceiling.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>
    /// The contract's response, or a locally produced rejection carrying the legacy's own outcome code
    /// when the ceiling is negative, or when the task is busy through this client.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="RpcException">The call failed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <remarks>
    /// PRESERVED LEGACY GUARD, AND ITS EXACT BOUNDARY MATTERS. The legacy test is
    /// <c>if rows &lt; 0 then return RetCode.E_INVALID_ARGUMENT</c>
    /// [n_cst_thread_task_sqlquery.sru:L433] - strictly less than, so ZERO IS LEGAL and means no ceiling.
    /// That is a different boundary from the chunk-size guard above, which uses "less than or equal", and
    /// transposing the two would either reject a legal setting or accept an illegal one.
    /// </remarks>
    public virtual Task<SetMaxRowsResponse> SetMaxRowsAsync(
        SetMaxRowsRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        static SetMaxRowsResponse Shape(OperationStatus status) => new() { Status = status };

        Task<SetMaxRowsResponse>? busy = RejectIfTaskBusy(request.Task, Shape);
        if (busy is not null)
        {
            return busy;
        }

        if (request.MaxRows < 0L)
        {
            return Task.FromResult(Shape(InvalidArgument(
                "A negative row ceiling is not accepted "
                + "[ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L433]. Zero is "
                + "legal and means no ceiling.")));
        }

        return InvokeAsync(_query.SetMaxRowsAsync, request, ReadTokenRequest, cancellationToken);
    }

    /// <summary>
    /// Sets the where clause of one select statement, at an explicit ONE-BASED index. Contract
    /// <b>C-05</b>, legacy <c>of_setwhereclause</c>.
    /// </summary>
    /// <param name="request">
    /// The task handle and the clause specification: the one-based select index, the modification style,
    /// and the clause text.
    /// </param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>
    /// The contract's response, or a locally produced rejection carrying the legacy's own outcome code.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="RpcException">The call failed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <remarks>
    /// <para>
    /// PRESERVED LEGACY GUARD, BOTH CONDITIONS AND ONE CODE. The legacy test is
    /// <c>if selectIndex &lt;= 0 or clause = "" then return RetCode.E_INVALID_ARGUMENT</c>
    /// [n_cst_thread_task_sqlquery.sru:L271]. The index is ONE-BASED, so zero is an ERROR and not a
    /// request for a default, and an empty clause is refused rather than treated as "clear".
    /// </para>
    /// <para>
    /// UPSERT BY INDEX, WHICH THE CALLER MUST KNOW BECAUSE IT IS OBSERVABLE. The legacy stores clauses in
    /// a per-kind array of index, style and text, and its setter scans for a matching index and writes
    /// AT that position [:L273-L281], so setting the same index twice REPLACES rather than appends. This
    /// client neither caches nor merges clauses; the accumulation lives on the server-held task, exactly
    /// as it lived on the legacy worker.
    /// </para>
    /// <para>
    /// THE CLAUSE TEXT IS OPAQUE PAYLOAD (C-E). It is not parsed, escaped, quoted or rewritten here. The
    /// legacy splices it through its own statement parser [n_cst_thread_task_sqlquery.sru:L691], and that
    /// splicing - together with the injection exposure it carries when the connection disables bind
    /// variables - is Persistence's to own, because Persistence is the only service that generates SQL.
    /// </para>
    /// </remarks>
    public virtual Task<SetWhereClauseResponse> SetWhereClauseAsync(
        SetWhereClauseRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        static SetWhereClauseResponse Shape(OperationStatus status) => new() { Status = status };

        Task<SetWhereClauseResponse>? rejection =
            RejectIfTaskBusy(request.Task, Shape) ?? RejectIfClauseInvalid(request.Clause, Shape);

        return rejection ?? InvokeAsync(_query.SetWhereClauseAsync, request, ReadTokenRequest, cancellationToken);
    }

    /// <summary>
    /// Sets the where clause of the FIRST select statement - the two-argument legacy form. Contract
    /// <b>C-05</b>.
    /// </summary>
    /// <param name="task">The task handle.</param>
    /// <param name="modifyStyle">
    /// The legacy modification style, one of <c>Enums.SQL_MS_REPLACE</c>, <c>Enums.SQL_MS_APPEND</c> or
    /// <c>Enums.SQL_MS_PREPEND</c>. Any other value is rejected locally.
    /// </param>
    /// <param name="clause">The clause text, opaque to this client.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>
    /// The contract's response, or a locally produced rejection carrying the legacy's own outcome code.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="task"/> or <paramref name="clause"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="RpcException">The call failed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <remarks>
    /// WHY THIS OVERLOAD EXISTS AND WHY IT SUPPLIES ONE. The legacy caller-side proxy publishes the clause
    /// setter in TWO arities - with and without the select index
    /// [n_cst_threading_task_sqlquery.sru:L73-L76] - and the shorter one addresses the first statement.
    /// The contract carries only the explicit form, so the implied index is supplied HERE, once, as
    /// <see cref="DefaultSelectIndex"/>, rather than being left for each call site to remember. It is
    /// one and not zero: the index is one-based and zero is rejected outright.
    /// </remarks>
    public virtual Task<SetWhereClauseResponse> SetWhereClauseAsync(
        TaskHandle task,
        long modifyStyle,
        string clause,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(clause);

        static SetWhereClauseResponse Shape(OperationStatus status) => new() { Status = status };

        SqlModifyStyle? style = ToWireModifyStyle(modifyStyle);
        if (style is null)
        {
            return Task.FromResult(Shape(UnmappedModifyStyle(modifyStyle)));
        }

        return SetWhereClauseAsync(
            new SetWhereClauseRequest
            {
                Task = task,
                Clause = new SqlClauseSpec
                {
                    SelectIndex = DefaultSelectIndex,
                    ModifyStyle = style.Value,
                    Clause = clause,
                },
            },
            cancellationToken);
    }

    /// <summary>
    /// Sets the order-by clause of one select statement, at an explicit ONE-BASED index. Contract
    /// <b>C-05</b>, legacy <c>of_setorderbyclause</c>.
    /// </summary>
    /// <param name="request">The task handle and the clause specification.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>
    /// The contract's response, or a locally produced rejection carrying the legacy's own outcome code.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="RpcException">The call failed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <remarks>
    /// The same preserved guard as the where clause, at its own locator
    /// [n_cst_thread_task_sqlquery.sru:L288], with the same upsert-by-index storage [:L290-L298]. The two
    /// clause kinds are held in SEPARATE collections, so an index used by one does not collide with the
    /// same index used by the other.
    /// </remarks>
    public virtual Task<SetOrderByClauseResponse> SetOrderByClauseAsync(
        SetOrderByClauseRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        static SetOrderByClauseResponse Shape(OperationStatus status) => new() { Status = status };

        Task<SetOrderByClauseResponse>? rejection =
            RejectIfTaskBusy(request.Task, Shape) ?? RejectIfClauseInvalid(request.Clause, Shape);

        return rejection ?? InvokeAsync(_query.SetOrderByClauseAsync, request, ReadTokenRequest, cancellationToken);
    }

    /// <summary>
    /// Sets the order-by clause of the FIRST select statement - the two-argument legacy form. Contract
    /// <b>C-05</b>.
    /// </summary>
    /// <param name="task">The task handle.</param>
    /// <param name="modifyStyle">The legacy modification style.</param>
    /// <param name="clause">The clause text, opaque to this client.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>
    /// The contract's response, or a locally produced rejection carrying the legacy's own outcome code.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="task"/> or <paramref name="clause"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="RpcException">The call failed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <remarks>Supplies <see cref="DefaultSelectIndex"/> for the same reason as its where-clause twin.</remarks>
    public virtual Task<SetOrderByClauseResponse> SetOrderByClauseAsync(
        TaskHandle task,
        long modifyStyle,
        string clause,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(clause);

        static SetOrderByClauseResponse Shape(OperationStatus status) => new() { Status = status };

        SqlModifyStyle? style = ToWireModifyStyle(modifyStyle);
        if (style is null)
        {
            return Task.FromResult(Shape(UnmappedModifyStyle(modifyStyle)));
        }

        return SetOrderByClauseAsync(
            new SetOrderByClauseRequest
            {
                Task = task,
                Clause = new SqlClauseSpec
                {
                    SelectIndex = DefaultSelectIndex,
                    ModifyStyle = style.Value,
                    Clause = clause,
                },
            },
            cancellationToken);
    }

    /// <summary>
    /// Sets the five paging settings in one call. Contract <b>C-05</b>, legacy <c>of_setpaged</c>,
    /// <c>of_setpagesize</c>, <c>of_setpageindex</c>, <c>of_setpagenative</c> and
    /// <c>of_setpagecounting</c>.
    /// </summary>
    /// <param name="request">
    /// The task handle, whether paging is on, the page size, the ONE-BASED page index, whether the native
    /// implementation is used, and whether page counting is on.
    /// </param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The contract's response, or a busy rejection.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="RpcException">The call failed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <remarks>
    /// <para>
    /// PAGE COUNTING DEFAULTS TO TRUE IN THE LEGACY [n_cst_threading_task_sqlquery.sru:L40], and a caller
    /// reaching paging through THIS call is setting all five values explicitly, so it must send true to
    /// keep counting on. <see cref="DefaultPageCounting"/> names the legacy value.
    /// </para>
    /// <para>
    /// THE PAGE-SIZE AND PAGE-INDEX BOUNDS ARE DELIBERATELY NOT PRE-CHECKED HERE, and the reason is
    /// fidelity rather than omission. The legacy validates each of those settings in its OWN setter -
    /// page size and page index each reject a value of zero or less
    /// [n_cst_thread_task_sqlquery.sru:L462, :L450] - whereas this contract FUSES five independent
    /// legacy setters into one message whose scalar fields always carry a value. There is therefore no
    /// legacy behaviour that says what those bounds mean for a caller whose intent is to turn paging OFF,
    /// and inventing one here would reject a request the contract permits. Persistence applies the
    /// declared validation; the standing posture is to narrow with a defined error, never to widen - and
    /// equally never to fabricate a rejection.
    /// </para>
    /// </remarks>
    public virtual Task<SetPagingResponse> SetPagingAsync(
        SetPagingRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return RejectIfTaskBusy(request.Task, static status => new SetPagingResponse { Status = status })
            ?? InvokeAsync(_query.SetPagingAsync, request, ReadTokenRequest, cancellationToken);
    }

    /// <summary>
    /// Replaces the unique-index column list used by the paged-statement rewriter. Contract <b>C-05</b>,
    /// legacy <c>of_setpageduniqueindexcolumns</c>.
    /// </summary>
    /// <param name="request">
    /// The task handle and the column list. An EMPTY list on this call CLEARS the collection - the one
    /// place an empty repeated field means "clear" rather than "leave alone", because here it is the whole
    /// payload of a dedicated call.
    /// </param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The contract's response, or a busy rejection.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="RpcException">The call failed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <remarks>
    /// NO COLUMN VALIDATION HERE, AND THE ABSENCE IS A DELIBERATE DECISION RATHER THAN AN OVERSIGHT. The
    /// legacy setter validates NOTHING AT ALL - it assigns the array and returns success
    /// [n_cst_thread_task_sqlquery.sru:L406] - so a check written here would be new behaviour. The
    /// contract does declare an identifier check on these elements, because each one is spliced into a
    /// generated statement, and that check belongs to Persistence: it is the service that generates SQL,
    /// so it is where the trust boundary for a SQL identifier sits. Two entry points must not have two
    /// different trust boundaries, and a client-side copy of the rule would be exactly that.
    /// </remarks>
    public virtual Task<SetPagedUniqueIndexColumnsResponse> SetPagedUniqueIndexColumnsAsync(
        SetPagedUniqueIndexColumnsRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return RejectIfTaskBusy(
                request.Task,
                static status => new SetPagedUniqueIndexColumnsResponse { Status = status })
            ?? InvokeAsync(_query.SetPagedUniqueIndexColumnsAsync, request, ReadTokenRequest, cancellationToken);
    }

    /// <summary>
    /// Runs a retrieval and yields its messages AS THEY ARRIVE. Contract <b>C-05</b>, the server-streamed
    /// half of the legacy progressive delivery.
    /// </summary>
    /// <param name="request">
    /// The task handle and, optionally, a specification merged before the retrieval runs.
    /// </param>
    /// <param name="cancellationToken">Cancels the retrieval and tears the stream down.</param>
    /// <returns>
    /// The contract's messages in arrival order, each yielded as received. Every message is one of five
    /// alternatives: the total row count, a data chunk, a drop-down child chunk, the page totals, or the
    /// terminal outcome status.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="RpcException">The call failed in transit or the upstream refused it.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <remarks>
    /// <para>
    /// GENUINELY STREAMED, NOT MATERIALISED. Messages are yielded one at a time as they arrive and are
    /// never collected into a list first. That reproduces the legacy shape rather than improving on it:
    /// the legacy raises one chunk event per delivery,
    /// <c>ondatachunk(ref blob blbdata, long count, long current, boolean fullstate)</c>
    /// [ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlquery.sru:L13], and a consumer is
    /// entitled to observe the same progression. ALL FOUR of those arguments survive on
    /// <c>QueryDataChunk</c>: the payload as a typed carrier state, the total chunk count, this chunk's
    /// ONE-BASED index, and the full-state selector. The index being one-based is load bearing - reading
    /// it as zero-based is wrong by one for the whole stream - and the full-state flag is NOT a hint: it
    /// says WHICH serialization the payload is, a full-state image or a changeset, and a receiver cannot
    /// apply it without knowing which.
    /// </para>
    /// <para>
    /// AN ABSENT CARRIER STATE IS THE LEGACY'S ZERO-LENGTH BLOB and means "no payload, clear what you
    /// have"; a state carrying empty segments means "a carrier that legitimately holds no rows". The
    /// legacy receive side makes exactly that distinction
    /// [n_cst_threading_task_sqlquery.sru:L180-L231]. This client passes both through untouched and
    /// collapses neither.
    /// </para>
    /// <para>
    /// THE BUSY GUARD SPANS THE WHOLE ENUMERATION, not just the call that starts it. The task is marked
    /// in flight before the stream opens and released when enumeration ends - completion, an exception,
    /// cancellation, or a caller that stops early - so a setter cannot mutate the task's settings while
    /// its rows are still arriving, which is the state the legacy guard existed to prevent. A second
    /// retrieval on a task already streaming through this client is refused with the legacy's own busy
    /// code, delivered as a single terminal status message and no wire call at all.
    /// </para>
    /// <para>
    /// RETRY SAFETY: the operation is read-only, so restarting it from the beginning is safe, but it must
    /// NEVER be retried mid-stream - resuming a partly consumed stream would deliver a chunk sequence
    /// that never existed, with chunk indices that do not agree with what the caller already saw.
    /// </para>
    /// </remarks>
    public virtual async IAsyncEnumerable<QueryResponse> QueryAsync(
        QueryRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        string? taskId = NormalizeTaskId(request.Task);
        bool tracked = false;

        if (taskId is not null)
        {
            if (!_inFlightTasks.TryAdd(taskId, 0))
            {
                // A status-only stream is a legal shape on this contract - the terminal status is one of
                // the five alternatives the response carries - so the busy rejection is reported in the
                // same channel a real outcome would use rather than as an exception, and nothing is sent.
                yield return new QueryResponse { Status = Busy(taskId) };

                yield break;
            }

            tracked = true;
        }

        try
        {
            CallOptions options = await CreateAuthenticatedCallOptionsAsync(
                    OutboundCallClass.Stream,
                    ReadTokenRequest,
                    cancellationToken)
                .ConfigureAwait(false);

            using AsyncServerStreamingCall<QueryResponse> call = _query.Query(request, options);

            await foreach (QueryResponse response in call.ResponseStream
                .ReadAllAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                yield return response;
            }
        }
        finally
        {
            if (tracked)
            {
                _ = _inFlightTasks.TryRemove(taskId!, out _);
            }
        }
    }

    /// <summary>
    /// Counts the pages and records a paged retrieval would produce. Contract <b>C-05</b>, legacy
    /// <c>of_getpagecount</c> and <c>of_getrecordcount</c>.
    /// </summary>
    /// <param name="request">The task handle.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>
    /// The contract's response, carrying the page total, the record total, and whether counting actually
    /// happened. Or a busy rejection when an operation on the same task is in flight through this client.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="RpcException">The call failed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <remarks>
    /// READ THE "COUNTED" FLAG RATHER THAN INFERRING FROM THE TOTALS. The legacy does not always issue a
    /// counting statement: with a single page it INFERS the totals instead, and it suppresses counting
    /// entirely for a stored-procedure source or when page counting is off
    /// [n_cst_thread_task_sqlquery.sru:L818-L823]. Zero totals with counting suppressed and zero totals
    /// from a genuine count are different facts, which is why the contract carries the flag rather than
    /// leaving a caller to deduce it.
    /// </remarks>
    public virtual Task<CountResponse> CountAsync(
        CountRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return RejectIfTaskBusy(request.Task, static status => new CountResponse { Status = status })
            ?? InvokeAsync(_query.CountAsync, request, ReadTokenRequest, cancellationToken);
    }

    // ==============================================================================================
    //  CONTRACT C-06 - persistence.v1.UpdateService : THE OPTIMISTIC-CONCURRENCY CONTRACT
    //  ----------------------------------------------------------------------------------------------
    //  WHAT MUST CROSS THE WIRE PER ROW, AND WHY A PLAIN ROWSET WILL NOT DO. The sole updatable
    //  DataWindow in the repository declares
    //      update="COMPANY" updatewhere=1 updatekeyinplace=no      [ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L14]
    //  with all six columns marked update=yes updatewhereclause=yes [:L8-L14]. `updatewhere=1` is the
    //  KEY-AND-UPDATEABLE-COLUMNS concurrency mode, so the generated statement's where clause carries the
    //  key column PLUS THE ORIGINAL VALUES OF EVERY UPDATEABLE COLUMN. The payload must therefore carry,
    //  per row, BOTH the current and the original value of every marked column, together with the buffer
    //  and the item status - which is exactly the shape of common.v1.DataWindowRow.
    //
    //  The legacy carrier is not a rowset: n_cst_thread_task_sqlbase_ds derives from `datastore`, so it
    //  IS a DataWindow, complete with primary, delete and filter buffers and per-row and per-column item
    //  statuses, transferred as a changeset with a row count
    //  [n_cst_thread_task_sqlupdate.sru:L74]. A naive rowset would silently discard exactly the state the
    //  update depends on. THIS CLIENT PASSES THE CARRIER PAYLOAD THROUGH VERBATIM: it does not re-derive
    //  it, normalise it, compress it, re-order its segments or rows, or recompute the row count.
    //
    //  ALSO NOT ASSUMED: `updatekeyinplace=no` is what the fixture sets, so the legacy's own
    //  self-assignment workaround for it [n_cst_thread_task_sqlupdate.sru:L151-L167, the self-assignment
    //  at :L163, gated at :L155] is on the NORMAL path rather than a rare branch. That workaround lives
    //  in Persistence; nothing here assumes key-in-place is on.
    // ==============================================================================================

    /// <summary>
    /// Creates a server-held update task. Contract <b>C-06</b>.
    /// </summary>
    /// <param name="request">The session the task belongs to.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The contract's response, carrying the outcome code and the task handle.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="RpcException">The call failed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public virtual Task<CreateUpdateTaskResponse> CreateUpdateTaskAsync(
        CreateUpdateTaskRequest request,
        CancellationToken cancellationToken) =>
        InvokeAsync(_update.CreateUpdateTaskAsync, request, WriteTokenRequest, cancellationToken);

    /// <summary>
    /// Releases a server-held update task. Contract <b>C-06</b>.
    /// </summary>
    /// <param name="request">The task handle.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The contract's response.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="RpcException">The call failed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <remarks>
    /// 🔴 RETRY SAFETY: <b>NOT SAFE</b>, for the reason given on <see cref="ReleaseQueryTaskAsync"/>: the
    /// release consumes its own registration and a replay answers <c>E_INVALID_HANDLE</c>
    /// [<c>Grpc/UpdateService.ReleaseUpdateTask</c>], which the cleanup path records as a suspected leak.
    /// </remarks>
    public virtual Task<ReleaseUpdateTaskResponse> ReleaseUpdateTaskAsync(
        ReleaseUpdateTaskRequest request,
        CancellationToken cancellationToken) =>
        InvokeAsync(_update.ReleaseUpdateTaskAsync, request, WriteTokenRequest, cancellationToken);

    /// <summary>
    /// Resets an update task, clearing its descriptor array and accumulated settings. Contract
    /// <b>C-06</b>, legacy <c>of_reset</c>.
    /// </summary>
    /// <param name="request">The task handle.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The contract's response, or a busy rejection.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="RpcException">The call failed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <remarks>
    /// The legacy reset assigns a FRESH EMPTY descriptor array and clears the autocommit and multi-table
    /// flags, the source, the payload and the row count
    /// [n_cst_threading_task_sqlupdate.sru:L82-L97, n_cst_thread_task_sqlupdate.sru:L58-L72], and it is
    /// itself busy-guarded [n_cst_threading_task_sqlbase.sru:L57].
    /// </remarks>
    public virtual Task<ResetUpdateTaskResponse> ResetUpdateTaskAsync(
        ResetUpdateTaskRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return RejectIfTaskBusy(request.Task, static status => new ResetUpdateTaskResponse { Status = status })
            ?? InvokeAsync(_update.ResetAsync, request, WriteTokenRequest, cancellationToken);
    }

    /// <summary>
    /// Declares the update-table contracts. Contract <b>C-06</b>, legacy <c>of_addupdatabletable</c> and
    /// <c>of_setmultitableupdate</c>.
    /// </summary>
    /// <param name="request">
    /// The task handle, the REPEATED table contract, the multi-table switch, and optionally the source
    /// object or the statement syntax.
    /// </param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>
    /// The contract's response, or a locally produced rejection carrying the legacy's own outcome code
    /// when a declared table fails the legacy validation, when the multi-table switch is on with an empty
    /// array, or when the task is busy through this client.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="RpcException">The call failed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <remarks>
    /// <para>
    /// MULTI-TABLE UPDATE FROM ONE CARRIER IS A REAL LEGACY CAPABILITY, NOT A THEORETICAL ONE. The legacy
    /// holds its descriptors as an ARRAY [n_cst_thread_task_sqlupdate.sru:L30], appends at the end
    /// [:L86], and has an explicit switch [:L265]. The request therefore carries a REPEATED contract, and
    /// this client never collapses it to a single table. ORDER IS SIGNIFICANT and is transmitted as the
    /// caller declared it: with the switch on, the worker loops the array IN ARRAY ORDER, preparing and
    /// updating once per table and STOPPING AT THE FIRST FAILURE [:L364-L369], so a later table is not
    /// attempted after an earlier one fails.
    /// </para>
    /// <para>
    /// EACH TABLE CARRIES SIX FIELDS IN THE LEGACY'S OWN ORDER - name, updatable columns, key columns,
    /// identity column, update-where and key-in-place [:L10-L17]. The validation reproduced here is
    /// exactly the legacy's:
    /// <c>if name = "" or UpperBound(updatableColumns) = 0 or UpperBound(keyColumns) = 0 then return
    /// RetCode.E_INVALID_ARGUMENT</c> [:L84]. WHAT IT DOES NOT CHECK IS EQUALLY PART OF THE CONTRACT: the
    /// identity column may legitimately be EMPTY, and update-where and key-in-place are UNCHECKED.
    /// </para>
    /// <para>
    /// UPDATE-WHERE IS AN INTEGER, NOT A BOOLEAN. A value of 1 selects the key-and-updateable-columns
    /// concurrency mode; the field is typed as the legacy typed it, so a caller cannot lose the
    /// distinction between mode 1 and any other mode.
    /// </para>
    /// <para>
    /// THE LAST TWO SETTINGS ARE PRESENCE-TRACKED, AND THE UNSET STATE IS NOT A DEFAULT. The legacy
    /// caller-side proxy publishes the add in TWO arities - the six-argument form and a four-argument form
    /// taking only name, updatable columns, key columns and identity column
    /// [n_cst_threading_task_sqlupdate.sru:L59-L60] - and the shorter form leaves update-where and
    /// key-in-place UNSET rather than defaulted. This client transmits the presence exactly as the caller
    /// set it and NEVER substitutes a value for an absent one; the carrier's own definition governs an
    /// absent setting.
    /// </para>
    /// <para>
    /// A BEHAVIOUR THE PLAN PROSE DOES NOT MENTION AND THE SOURCE DOES: the prepare step has exactly ONE
    /// caller, inside the multi-table branch [:L365], and the single-table branch calls the update
    /// directly [:L371] and never prepares at all. So WITH THE SWITCH OFF THE DESCRIPTOR ARRAY IS NOT
    /// APPLIED and the carrier's static definition governs. That looks like a legacy oversight; it is the
    /// observable behaviour, so it is preserved and documented rather than "fixed". WITH THE SWITCH ON, an
    /// empty array IS an error - <c>E_INVALID_ARGUMENT</c> [:L356-L363] - and that rejection is reproduced
    /// here.
    /// </para>
    /// </remarks>
    public virtual Task<PrepareUpdateResponse> PrepareUpdateAsync(
        PrepareUpdateRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        static PrepareUpdateResponse Shape(OperationStatus status) => new() { Status = status };

        Task<PrepareUpdateResponse>? busy = RejectIfTaskBusy(request.Task, Shape);
        if (busy is not null)
        {
            return busy;
        }

        // The multi-table arm only. With the switch OFF an empty array is ORDINARY, because the legacy
        // never applies the descriptors in that mode at all [:L365, :L371].
        if (request.MultiTableUpdate && request.Tables.Count == 0)
        {
            return Task.FromResult(Shape(InvalidArgument(
                "Multi-table update is requested but no updatable table was declared "
                + "[ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru:L356-L363].")));
        }

        // ONE-BASED REPORTING ON PURPOSE, AND IN EXACTLY ONE PLACE. The enumeration is ordinary
        // zero-based C#, but the ordinal NAMED IN THE DIAGNOSTIC counts from one so it can be read against
        // the legacy array it corresponds to without a mental rebase - PowerBuilder arrays are one-based
        // and its own upper-bound function returns the LAST VALID INDEX rather than one past the end. No
        // index crossing the wire is rebased anywhere in this file: the select index of a clause is
        // one-based on the wire and is transmitted exactly as the caller supplied it. A repeated protobuf
        // field cannot contain a null element - the generated collection refuses one on insertion - so
        // there is no per-element null check to write here.
        for (int index = 0; index < request.Tables.Count; index++)
        {
            TableUpdateContract table = request.Tables[index];
            int reportedOrdinal = index + 1;

            if (string.IsNullOrEmpty(table.Name)
                || table.Updatablecolumns.Count == 0
                || table.Keycolumns.Count == 0)
            {
                return Task.FromResult(Shape(InvalidArgument(string.Create(
                    CultureInfo.InvariantCulture,
                    $"Update table {reportedOrdinal} must declare a name, at least one updatable column "
                    + $"and at least one key column "
                    + $"[ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru:L84]. The "
                    + $"identity column may legitimately be empty, and the update-where and key-in-place "
                    + $"settings are deliberately unchecked."))));
            }
        }

        return InvokeAsync(_update.PrepareUpdateAsync, request, WriteTokenRequest, cancellationToken);
    }

    /// <summary>
    /// Applies a changeset. Contract <b>C-06</b>. A concurrency mismatch is surfaced as
    /// <see cref="PersistenceConflictException"/> carrying the contract's detail INTACT.
    /// </summary>
    /// <param name="request">
    /// The task handle, the carrier state, the row count, and optionally the autocommit setting. The
    /// carrier state is transmitted VERBATIM.
    /// </param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>
    /// The contract's response - the reconciled outcome status, the three counts, and one identity block
    /// per update table - or a busy rejection when an operation on the same task is in flight through this
    /// client.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="PersistenceConflictException">
    /// The update was refused by the optimistic-concurrency check and the conflict detail was decoded.
    /// </exception>
    /// <exception cref="RpcException">
    /// The call failed for any other reason, INCLUDING an aborted status whose conflict detail could not
    /// be decoded - in which case the status and the complete trailer set are rethrown unchanged rather
    /// than being reported as a conflict a caller cannot act on.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <remarks>
    /// <para>
    /// THE OUTCOME MAY NOT BE INFERRED FROM THE ABSENCE OF AN EXCEPTION. Two legacy behaviours the server
    /// owns make that unsafe, and both are reconciled into the status this response carries. FIRST,
    /// UPDATE SUCCESS IS THE VALUE 1, NOT THE ZERO of the return-code algebra
    /// [ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru:L214], and the call is made with
    /// accept-text true and the reset flag FALSE [:L204], so the caller still owns the carrier state
    /// afterwards. SECOND, A CLAIMED SUCCESS IS DEFENSIVELY REWRITTEN INTO A FAILURE when the
    /// transaction's own driver code indicates an error [:L208-L210] - an implementation that trusted the
    /// update's self-report would report success on a failed update. Read the contract's own status and
    /// counts.
    /// </para>
    /// <para>
    /// THE IDENTITY ROUND TRIP: TWO ARRAYS, AND THE SECOND ONE ARRIVES REVERSED. Each identity block
    /// carries the discovered column's ONE-BASED ordinal, the values collected from the PRIMARY buffer in
    /// FORWARD order [:L228-L231], and the values collected from the FILTER buffer in BACKWARD order.
    /// <b>The backward direction is not a defect.</b> The legacy iterates
    /// <c>for nIndex = nCount to 1 step -1</c> [:L237] because - as its own comment at [:L235] states
    /// outright - the filter buffer's row order is INVERTED relative to the source. "Correcting" the
    /// direction produces wrong identity values that a row-count assertion would still pass, which is why
    /// this client carries both arrays through EXACTLY AS RECEIVED, in the order received, and neither
    /// re-sorts, de-duplicates nor concatenates them. The identity column itself is discovered by
    /// prefix-matching the lower-cased update table name against each column's database name with a
    /// FIRST-WINS fallback [:L221]; that resolution is Persistence's, and the returned ordinal is
    /// surfaced unchanged.
    /// </para>
    /// <para>
    /// THE BLOCKS ARE REPEATED, ONE PER UPDATE TABLE THAT COLLECTED ANY, IN THE DECLARED TABLE ORDER. The
    /// legacy caller-side proxy APPENDS a block per firing rather than replacing anything
    /// [n_cst_threading_task_sqlupdate.sru:L73-L76] and replays every one of them [:L142-L195]. An empty
    /// list means "none collected" and is never an error.
    /// </para>
    /// <para>
    /// A CONFLICT IS NEITHER SWALLOWED NOR RETRIED. The mismatch arrives as the gRPC status
    /// <c>Aborted</c>; the detail is decoded from the binary trailer the method's own descriptor names and
    /// is surfaced unaltered so the REST projection can present it as HTTP 409 with the same payload. This
    /// client does not re-attempt the update, does not downgrade the status to a generic failure, and
    /// contains no path that could overwrite the conflicting row. NO RESILIENCE POLICY MAY RETRY IT
    /// EITHER: an automatic retry of an aborted update IS a silent overwrite. The composition root
    /// attaches the pipeline, and the gRPC client factory retries only what a retry-throttling policy
    /// names, which never includes <c>Aborted</c>; nothing in this file adds a retry of its own.
    /// </para>
    /// <para>
    /// OTHER FAILURE ARMS THE SERVER OWNS, LISTED SO THEY ARE NOT MISTAKEN FOR TRANSPORT FAULTS: an empty
    /// or placeholder update table synthesizes a DATABASE ERROR and returns the database-error code
    /// [:L188-L193] - it arrives as the contract's structured error on this response, not as a client-side
    /// argument exception; and a VETOED before-update hook means two different things, discriminated by
    /// the transaction's own failure predicate - a veto combined with a transaction failure yields a
    /// database error, while a clean veto yields CANCELLED [:L195-L202]. Cancelled is NEITHER succeeded
    /// nor failed in the ported algebra, so it cannot be folded into either.
    /// </para>
    /// <para>
    /// RETRY SAFETY: NOT SAFE TO RETRY BLINDLY. Re-sending a changeset after an unknown outcome may apply
    /// it twice; after a conflict it must not be re-sent at all without re-reading and rebasing.
    /// </para>
    /// </remarks>
    public virtual async Task<UpdateResponse> UpdateAsync(
        UpdateRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        string? taskId = NormalizeTaskId(request.Task);

        if (taskId is not null && !_inFlightTasks.TryAdd(taskId, 0))
        {
            return new UpdateResponse { Status = Busy(taskId) };
        }

        try
        {
            CallOptions options = await CreateAuthenticatedCallOptionsAsync(
                    OutboundCallClass.Unary,
                    WriteTokenRequest,
                    cancellationToken)
                .ConfigureAwait(false);

            UpdateResponse response;
            try
            {
                response = await _update.UpdateAsync(request, options).ConfigureAwait(false);
            }
            catch (RpcException exception) when (exception.StatusCode == StatusCode.Aborted)
            {
                ConflictDetail? conflict = TryReadConflictDetail(exception, out long conflictRetCode);
                if (conflict is null)
                {
                    // THE PAYLOAD IS STILL NOT LOST. Rethrowing preserves the status and the complete
                    // trailer set, so a caller can decode whatever did arrive. What is NOT done here is
                    // inventing a detail-less conflict, which would report a conflict a caller cannot act
                    // on, or swallowing the status, which would let a refused update look successful.
                    _logger.LogWarning(
                        "Persistence answered Aborted for an update on task {TaskId} but no decodable "
                        + "conflict detail accompanied it. The status and trailers are rethrown unchanged "
                        + "rather than being reported as a conflict without its detail, and the update is "
                        + "not retried.",
                        LogSafeText.Render(taskId));

                    throw;
                }

                _logger.LogWarning(
                    "Optimistic-concurrency conflict on task {TaskId}: {ConflictRowCount} row(s) on "
                    + "update table {UpdateTable}; {RowsExpected} row(s) expected, {RowsMatched} matched; "
                    + "retCode {RetCode}. The conflict is surfaced with its detail so the caller can "
                    + "re-read and rebase or surface it; it is never retried and never overwritten.",
                    LogSafeText.Render(taskId),
                    conflict.Rows.Count,
                    conflict.UpdateTable,
                    conflict.RowsExpected,
                    conflict.RowsMatched,
                    conflictRetCode);

                throw new PersistenceConflictException(conflict, conflictRetCode, exception);
            }

            LogUpdateOutcome(taskId, response);

            return response;
        }
        finally
        {
            if (taskId is not null)
            {
                _ = _inFlightTasks.TryRemove(taskId, out _);
            }
        }
    }

    // ==============================================================================================
    //  CONTRACT C-07 - persistence.v1.CommandService
    //  ----------------------------------------------------------------------------------------------
    //  TWO PLACEHOLDER GRAMMARS EXIST IN THE LEGACY AND THEY ARE NOT THE SAME GRAMMAR. Conflating them
    //  would corrupt statements in a way no unit test would obviously catch, so both are recorded:
    //
    //    THE SQLITE BINDING uses a POSITIONAL `?` and a leading `@` prefix on the statement. Evidenced
    //    at ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L312-L313
    //        Exec("UPDATE COMPANY SET SALARY = SALARY + 100 WHERE NAME = ? ", "Paul")
    //    and at :L396-L398, whose Chinese comment states that the `@` prefix caches the statement.
    //
    //    THE TASK LAYER USES A DIFFERENT GRAMMAR ENTIRELY: named parameters prefixed with a colon -
    //    `constant string ARG_PREFIX = ":"` [n_cst_thread_task_sqlbase.sru:L396] - where `?` appears
    //    only as one of the word delimiters that terminate a name [:L424]. Names are lower-cased
    //    [:L437]; an EMPTY parameter name matches the first unreplaced placeholder POSITIONALLY and
    //    stops after one substitution [:L460, :L472], which is what the legacy's unnamed add reduces to
    //    [n_cst_threading_task_sqlbase.sru:L138]; MIXING named and unnamed parameters is rejected with
    //    E_INVALID_ARGUMENT [:L407]; an unclosed quote is E_INVALID_ARGUMENT [:L451]; and any
    //    placeholder left unreplaced is E_OUT_OF_BOUND [:L477-L479].
    //
    //  THIS CLIENT IMPLEMENTS NEITHER GRAMMAR. The statement text and the parameter names are opaque
    //  payload, carried to Persistence exactly as supplied - it is the service that binds and executes,
    //  so it is the one place either grammar may be interpreted. THE LEADING `@` PREFIX IS CARRIED
    //  THROUGH VERBATIM AS PART OF THE STATEMENT TEXT, and forwarding it is what makes the mode
    //  REACHABLE: Persistence removes the selector before the statement reaches the provider and treats
    //  it as the statement-caching execution mode C-07 preserves (AAP 0.4.3), so a client that stripped
    //  the character here would silently cancel a mode the caller selected. NO performance claim is
    //  attached to it at either end: it is a preserved legacy affordance, not an optimization.
    // ==============================================================================================

    /// <summary>
    /// Creates a server-held command task. Contract <b>C-07</b>.
    /// </summary>
    /// <param name="request">The session the task belongs to.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The contract's response, carrying the outcome code and the task handle.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="RpcException">The call failed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public virtual Task<CreateCommandTaskResponse> CreateCommandTaskAsync(
        CreateCommandTaskRequest request,
        CancellationToken cancellationToken) =>
        InvokeAsync(_command.CreateCommandTaskAsync, request, WriteTokenRequest, cancellationToken);

    /// <summary>
    /// Releases a server-held command task. Contract <b>C-07</b>.
    /// </summary>
    /// <param name="request">The task handle.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The contract's response.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="RpcException">The call failed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <remarks>
    /// 🔴 RETRY SAFETY: <b>NOT SAFE</b>, for the reason given on <see cref="ReleaseQueryTaskAsync"/>: the
    /// release consumes its own registration and a replay answers <c>E_INVALID_HANDLE</c>
    /// [<c>Grpc/CommandService.ReleaseCommandTask</c>].
    /// </remarks>
    public virtual Task<ReleaseCommandTaskResponse> ReleaseCommandTaskAsync(
        ReleaseCommandTaskRequest request,
        CancellationToken cancellationToken) =>
        InvokeAsync(_command.ReleaseCommandTaskAsync, request, WriteTokenRequest, cancellationToken);

    /// <summary>
    /// Resets a command task. Contract <b>C-07</b>, legacy <c>of_reset</c>.
    /// </summary>
    /// <param name="request">The task handle.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The contract's response, or a busy rejection.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="RpcException">The call failed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <remarks>
    /// The legacy reset clears the autocommit setting back to OFF and empties the statement, and it is
    /// itself guarded by the worker-side running check
    /// [ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlcommand.sru:L32-L38].
    /// </remarks>
    public virtual Task<ResetCommandTaskResponse> ResetCommandTaskAsync(
        ResetCommandTaskRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return RejectIfTaskBusy(request.Task, static status => new ResetCommandTaskResponse { Status = status })
            ?? InvokeAsync(_command.ResetAsync, request, WriteTokenRequest, cancellationToken);
    }

    /// <summary>
    /// Sets a command task's autocommit MODE. Contract <b>C-07</b>, legacy <c>of_setautocommit</c>.
    /// </summary>
    /// <param name="request">The task handle and the mode.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The contract's response, or a busy rejection.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="RpcException">The call failed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <remarks>
    /// THE AUTOCOMMIT SETTING ON THIS CONTRACT IS TRI-VALUED, NOT BOOLEAN, AND THE THREE VALUES MEAN
    /// THREE DIFFERENT THINGS [n_cst_thread_task_sqlcommand.sru:L16-L18]:
    /// <c>AC_OFF</c> = 0; <c>AC_ON</c> = 1, which BEGINS A TRANSACTION and commits automatically; and
    /// <c>AC_NATIVE</c> = 2, which DOES NOT BEGIN A TRANSACTION and instead drives the connection's own
    /// autocommit switch around the statement [:L88, :L95-L97, :L105-L108]. CONTRAST CONTRACT C-06, where the equivalent
    /// setting is a PLAIN BOOLEAN [n_cst_thread_task_sqlupdate.sru:L254], and contract C-08, where the
    /// session-level setting is also boolean. The two must never be conflated, and this tri-state must
    /// never be flattened to a bool: doing so would silently merge "do not begin a transaction" into
    /// "off", which is a different statement-execution shape.
    /// </remarks>
    public virtual Task<SetCommandAutoCommitResponse> SetCommandAutoCommitAsync(
        SetCommandAutoCommitRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return RejectIfTaskBusy(
                request.Task,
                static status => new SetCommandAutoCommitResponse { Status = status })
            ?? InvokeAsync(_command.SetAutoCommitAsync, request, WriteTokenRequest, cancellationToken);
    }

    /// <summary>
    /// Sets a command task's statement. Contract <b>C-07</b>, legacy <c>of_setsql</c>.
    /// </summary>
    /// <param name="request">The task handle and the statement text, opaque to this client.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>
    /// The contract's response, or a locally produced rejection carrying the legacy's own outcome code
    /// when the statement is empty, or when the task is busy through this client.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="RpcException">The call failed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <remarks>
    /// PRESERVED LEGACY ERROR-CODE FIDELITY, AND THE CODE IS THE UNOBVIOUS PART. An empty statement yields
    /// <c>E_INVALID_SQL</c> and NOT <c>E_INVALID_ARGUMENT</c>
    /// [n_cst_thread_task_sqlcommand.sru:L45], and the same code is raised again from the execution path
    /// when the statement is still empty there [:L66-L67]. Substituting a "more sensible" argument code
    /// would change an outcome a characterization recording can compare.
    /// </remarks>
    public virtual Task<SetCommandSqlResponse> SetCommandSqlAsync(
        SetCommandSqlRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        static SetCommandSqlResponse Shape(OperationStatus status) => new() { Status = status };

        Task<SetCommandSqlResponse>? busy = RejectIfTaskBusy(request.Task, Shape);
        if (busy is not null)
        {
            return busy;
        }

        if (string.IsNullOrEmpty(request.Sql))
        {
            return Task.FromResult(Shape(new OperationStatus
            {
                RetCode = WireRetCode.EInvalidSql,
                ErrorText =
                    "An empty statement is refused with the invalid-statement code rather than the "
                    + "invalid-argument one "
                    + "[ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlcommand.sru:L45].",
            }));
        }

        return InvokeAsync(_command.SetSqlAsync, request, WriteTokenRequest, cancellationToken);
    }

    /// <summary>
    /// Executes a command task's statement. Contract <b>C-07</b>, legacy <c>of_exec</c>.
    /// </summary>
    /// <param name="request">
    /// The task handle and, optionally, the statement, the positional or named parameters, and the
    /// autocommit mode for this execution.
    /// </param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>
    /// The contract's response - the outcome status, the affected row count, the driver codes, the driver
    /// text and whether the work was committed - or a busy rejection.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="RpcException">The call failed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <remarks>
    /// <para>
    /// THE PARAMETER LIST IS AN ORDINARY COLLECTION, AND THAT IS DELIBERATE. The legacy's ELEVEN-ARGUMENT
    /// CEILING was an artefact of PowerScript's lack of variadic calls, which forced the binding to be
    /// written as twelve overloads [ws_objects/pfw.utility.sqlite.pbl.src/n_sqlite.sru:L32-L43]. C# has
    /// native variadic support, so the contract may exceed the ceiling with no behavioural regression;
    /// <see cref="LegacyExecArgumentCeiling"/> records the number as history and nothing compares against
    /// it. The task layer separately unrolls a forwarding call to twenty positional arguments before
    /// falling back to dynamic invocation [n_cst_thread_task_sqlbase.sru:L690-L692] - the same artefact,
    /// with the same conclusion.
    /// </para>
    /// <para>
    /// ERROR CODES THE SERVER RAISES ON THIS PATH, RECORDED SO NONE IS "IMPROVED" ON THE WAY BACK: an
    /// empty statement is <c>E_INVALID_SQL</c> [n_cst_thread_task_sqlcommand.sru:L66-L67]; a missing
    /// transaction is <c>E_INVALID_TRANSACTION</c> [:L70-L74]; and a parameter-bind failure is
    /// <c>E_SQL_BIND_ARG_FAILED</c> [:L83-L84]. Each arrives on the contract's own status.
    /// </para>
    /// <para>
    /// A "NO ROWS" OUTCOME IS NOT A FAILURE. The legacy treats driver code 100 as ordinary alongside zero
    /// [n_cst_thread_trans.sru:L233], so a caller must not read an empty affected-row count as an error.
    /// </para>
    /// <para>
    /// RETRY SAFETY: NOT SAFE TO RETRY BLINDLY - a statement may be neither idempotent nor transactional,
    /// and the native autocommit mode deliberately runs outside a transaction.
    /// </para>
    /// </remarks>
    public virtual async Task<ExecResponse> ExecAsync(
        ExecRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        string? taskId = NormalizeTaskId(request.Task);

        if (taskId is not null && !_inFlightTasks.TryAdd(taskId, 0))
        {
            return new ExecResponse { Status = Busy(taskId) };
        }

        try
        {
            CallOptions options = await CreateAuthenticatedCallOptionsAsync(
                    OutboundCallClass.Unary,
                    WriteTokenRequest,
                    cancellationToken)
                .ConfigureAwait(false);

            ExecResponse response = await _command.ExecAsync(request, options).ConfigureAwait(false);

            LogDatabaseFailure(taskId, response.Status);

            return response;
        }
        finally
        {
            if (taskId is not null)
            {
                _ = _inFlightTasks.TryRemove(taskId, out _);
            }
        }
    }

    /// <summary>
    /// Executes a statement with its parameters in one call - the convenience form of
    /// <see cref="ExecAsync(ExecRequest, CancellationToken)"/>. Contract <b>C-07</b>.
    /// </summary>
    /// <param name="task">The task handle.</param>
    /// <param name="sql">
    /// The statement text, carried VERBATIM. A leading <c>@</c> is part of the text and is neither
    /// stripped nor interpreted here.
    /// </param>
    /// <param name="parameters">
    /// The parameters in the order the caller supplied them, or <see langword="null"/> for none. The order
    /// is preserved because an unnamed parameter is matched POSITIONALLY by the task layer
    /// [n_cst_thread_task_sqlbase.sru:L460].
    /// </param>
    /// <param name="autoCommit">
    /// The autocommit mode for this execution, or <see langword="null"/> to leave the task's own setting
    /// in force. Absence is transmitted as absence and is never replaced by a default, because the three
    /// modes are three distinct behaviours.
    /// </param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The contract's response, or a busy rejection.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="task"/> or <paramref name="sql"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="RpcException">The call failed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public virtual Task<ExecResponse> ExecAsync(
        TaskHandle task,
        string sql,
        IEnumerable<PositionalParameter>? parameters,
        AutoCommitMode? autoCommit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(sql);

        ExecRequest request = new()
        {
            Task = task,
            Sql = sql,
        };

        if (parameters is not null)
        {
            // ADDED IN ITERATION ORDER, AND THE ORDER IS THE CONTRACT. Nothing here sorts, de-duplicates
            // or renames a parameter: an unnamed one is positional at the far end, so re-ordering would
            // bind values to the wrong placeholders.
            request.Parameters.AddRange(parameters);
        }

        if (autoCommit is not null)
        {
            request.Autocommit = autoCommit.Value;
        }

        return ExecAsync(request, cancellationToken);
    }

    // ==============================================================================================
    //  CONTRACT C-08 - persistence.v1.TransactionService
    //  ----------------------------------------------------------------------------------------------
    //  THE DESCRIPTOR MIRRORS ws_objects/pfw.thread.ext.pbl.src/transactiondata.srs FIELD FOR FIELD AND
    //  IN ORDER - nine fields: dbms, servername, database, logid, logpass, dbparm, lock, autocommit,
    //  userparm.
    //
    //  THE LOG PASSWORD IS WRITE-ONLY, AND THAT IS ENFORCED STRUCTURALLY RATHER THAN BY CONVENTION. It
    //  may be SENT on the session-begin descriptor and it never comes back: the read model
    //  TransactionDescriptorView RESERVES the field numbers and names of the password, the connection
    //  parameters and the user parameters, so there is no field for them to arrive in. NOTE THE HISTORY,
    //  because it shows the rule is a deliberate narrowing rather than a port: the LEGACY reader DOES
    //  copy the password outbound - of_gettransdata copies seven of the nine fields and the password is
    //  among them [n_cst_thread_trans.sru:L402-L423]. This client never logs it, never echoes it, never
    //  puts it in a message or a diagnostic, and never expects it on a response. The log identity and
    //  the connection parameters are treated the same way.
    //
    //  THE TWO CONNECTION-PARAMETER FLAGS STAY DISTINCT, because their legacy parsing is NESTED and the
    //  nesting carries meaning: the national-character binding flag is only honoured when the
    //  bind-disabling flag is also set [n_cst_thread_task_sqlbase.sru:L127-L132]. That parsing belongs to
    //  Persistence and is not replicated here, but no convenience on this client collapses the two flags
    //  into one, because a collapsed pair cannot express the legacy's dependent state.
    //
    //  THE DATABASE-TYPE ENUMERATION HAS EXACTLY TWO MEMBERS - MSSQL as 0 and ORACLE as 1
    //  [n_cst_thread_trans.sru:L60-L61], resolved by inspecting the driver name [:L356-L359]. SQLITE IS
    //  DELIBERATELY ABSENT FROM IT and is not added: the paging rewriter dispatches on this enumeration
    //  and answers E_NO_IMPLEMENTATION for anything else [n_cst_thread_task_sqlquery.sru:L396-L398].
    //
    //  NO BUSY GUARD ON THIS CONTRACT, and the reason is in the legacy rather than in convenience: the
    //  guards live on the TASK proxies, the legacy transaction object has none, and the contract relocated
    //  commit and rollback from the task proxy to the session - so there is no task whose busy state could
    //  be tested. Persistence's own state machine governs instead, and two of its answers are worth
    //  knowing before reading a result: COMMIT AND ROLLBACK BOTH ANSWER "FAILED" WHILE AUTOCOMMIT IS ON
    //  [n_cst_thread_trans.sru:L240, :L185], and driver code 100 - no rows - IS NOT AN ERROR [:L233].
    // ==============================================================================================

    /// <summary>
    /// Begins a database session. Contract <b>C-08</b>, the wire form of the legacy transaction-object
    /// setup.
    /// </summary>
    /// <param name="request">
    /// The nine-field descriptor, the two connection-parameter flags, and the pool keep-alive settings.
    /// </param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The contract's response, carrying the outcome code and the session handle.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="RpcException">The call failed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <remarks>
    /// <para>
    /// THE DESCRIPTOR IS THE CALLER'S TO POPULATE, FROM CONFIGURATION OR FROM ITS OWN CALLER - NEVER FROM
    /// A LITERAL IN THIS FILE. There is no connection string, no driver name, no server name, no database
    /// name, no log identity, no password and no connection-parameter value anywhere in this client (C-F).
    /// </para>
    /// <para>
    /// NOTHING ABOUT THE DESCRIPTOR IS LOGGED - not the password, not the log identity, not the connection
    /// parameters, and not the remaining fields either, because a diagnostic that named the server and
    /// database of a failing session would still be describing someone's deployment. The only record this
    /// member writes is that a session was requested.
    /// </para>
    /// <para>
    /// RETRY SAFETY: safe to retry, but each successful attempt begins a SEPARATE session that must be
    /// ended. The legacy pool is reference counted with an idle expiry [n_cst_thread_trans_pool.sru], so
    /// an abandoned session is a leak rather than a harmless duplicate.
    /// </para>
    /// </remarks>
    public virtual Task<BeginSessionResponse> BeginSessionAsync(
        BeginSessionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        _logger.LogDebug(
            "Requesting a Persistence session. The transaction descriptor is deliberately not recorded: "
            + "its log password is write-only, and its log identity and connection parameters are "
            + "withheld on the same grounds.");

        return InvokeAsync(_transaction.BeginSessionAsync, request, ReadTokenRequest, cancellationToken);
    }

    /// <summary>
    /// Ends a database session and releases its pool reference. Contract <b>C-08</b>.
    /// </summary>
    /// <param name="request">The session handle.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The contract's response.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="RpcException">The call failed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public virtual Task<EndSessionResponse> EndSessionAsync(
        EndSessionRequest request,
        CancellationToken cancellationToken) =>
        InvokeAsync(_transaction.EndSessionAsync, request, ReadTokenRequest, cancellationToken);

    // ==============================================================================================
    //  THE HANDLE LIFECYCLE, COMPOSED - the five members below
    //  ----------------------------------------------------------------------------------------------
    //  Every operating call on C-05 and C-06 names a server-held task handle, and until these members
    //  existed nothing in this service created one: `Grpc/DataWindowService.cs` sent a DEFAULT handle and
    //  Persistence refused it with E_INVALID_HANDLE before it reached a statement. The six lifecycle
    //  operations above were all published and none was called.
    //
    //  These compose them into one acquisition and one release, so a call site is an `await using` rather
    //  than three nested try/finally blocks - and so the release ordering (task, THEN session) is stated
    //  once instead of at each site. See Clients/PersistenceWorkScope.cs for why the release obligation is
    //  the part that has to be structural.
    // ==============================================================================================

    /// <summary>
    /// Opens a C-05 retrieval scope: a transaction session, a query task on it, and the obligation to
    /// release both.
    /// </summary>
    /// <param name="spec">The retrieval specification the task is created with.</param>
    /// <param name="cancellationToken">Cancels the acquisition.</param>
    /// <returns>
    /// The scope. Test <see cref="PersistenceWorkScope.IsAcquired"/> before using it, and dispose it on
    /// every path - INCLUDING a refusal, which may still be holding a session.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="spec"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// THE SPEC TRAVELS ON THE CREATE CALL, NOT ON THE RETRIEVE CALL, because that is where C-05 puts it:
    /// <c>CreateQueryTaskRequest</c> carries both the session and the spec, and <c>QueryRequest</c> carries
    /// only the handle and a spec that may refine it. Creating the task with the spec is what lets
    /// Persistence validate the source object once, at creation, rather than per stream.
    /// </para>
    /// <para>
    /// INTERNAL AND VIRTUAL. Internal because the scope type it answers is internal - a scope is this
    /// service's own composition of the published operations, not a published operation itself, and
    /// widening it would put a type with a release obligation on a public surface. Virtual so a test can
    /// substitute the whole acquisition through the friend-assembly grant - but the DEFAULT implementation
    /// calls the six published operations, which is what keeps them exercised by every test that drives a
    /// retrieval rather than only by a test written for them.
    /// </para>
    /// </remarks>
    internal virtual async Task<PersistenceWorkScope> OpenQueryScopeAsync(
        QuerySpec spec,
        BeginSessionRequest session,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(session);

        (SessionHandle? opened, long code, string text) = await BeginWorkSessionAsync(
                session,
                cancellationToken)
            .ConfigureAwait(false);

        if (opened is null)
        {
            return PersistenceWorkScope.Refused(this, PersistenceWorkKind.Query, code, text);
        }

        CreateQueryTaskResponse created = await CreateQueryTaskAsync(
                new CreateQueryTaskRequest { Session = opened, Spec = spec },
                cancellationToken)
            .ConfigureAwait(false);

        long taskCode = created.Status is null
            ? PowerFramework.Shared.Kernel.RetCode.E_INTERNAL_ERROR
            : (long)created.Status.RetCode;

        if (taskCode != PowerFramework.Shared.Kernel.RetCode.OK)
        {
            return PersistenceWorkScope.SessionOnly(
                this,
                PersistenceWorkKind.Query,
                opened,
                taskCode,
                created.Status?.ErrorText ?? string.Empty);
        }

        if (created.Task is null || string.IsNullOrWhiteSpace(created.Task.TaskId))
        {
            return PersistenceWorkScope.SessionOnly(
                this,
                PersistenceWorkKind.Query,
                opened,
                PersistenceWorkScope.MissingHandleCode,
                PersistenceWorkScope.MissingTaskHandleText);
        }

        return new PersistenceWorkScope(
            this,
            PersistenceWorkKind.Query,
            opened,
            created.Task,
            PowerFramework.Shared.Kernel.RetCode.OK,
            string.Empty);
    }

    /// <summary>
    /// Opens a C-06 update scope: a transaction session, an update task on it, and the obligation to
    /// release both.
    /// </summary>
    /// <param name="cancellationToken">Cancels the acquisition.</param>
    /// <returns>The scope, on the same terms as <see cref="OpenQueryScopeAsync"/>.</returns>
    /// <remarks>
    /// NO SPEC, BECAUSE C-06 HAS NONE ON CREATION. <c>CreateUpdateTaskRequest</c> carries the session
    /// alone; an update task's contract - the updatable tables, the key columns, the identity column - is
    /// installed afterwards through <c>PrepareUpdate</c>, and its rows arrive on the update call itself.
    /// </remarks>
    internal virtual async Task<PersistenceWorkScope> OpenUpdateScopeAsync(
        BeginSessionRequest session,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        (SessionHandle? opened, long code, string text) = await BeginWorkSessionAsync(
                session,
                cancellationToken)
            .ConfigureAwait(false);

        if (opened is null)
        {
            return PersistenceWorkScope.Refused(this, PersistenceWorkKind.Update, code, text);
        }

        CreateUpdateTaskResponse created = await CreateUpdateTaskAsync(
                new CreateUpdateTaskRequest { Session = opened },
                cancellationToken)
            .ConfigureAwait(false);

        long taskCode = created.Status is null
            ? PowerFramework.Shared.Kernel.RetCode.E_INTERNAL_ERROR
            : (long)created.Status.RetCode;

        if (taskCode != PowerFramework.Shared.Kernel.RetCode.OK)
        {
            return PersistenceWorkScope.SessionOnly(
                this,
                PersistenceWorkKind.Update,
                opened,
                taskCode,
                created.Status?.ErrorText ?? string.Empty);
        }

        if (created.Task is null || string.IsNullOrWhiteSpace(created.Task.TaskId))
        {
            return PersistenceWorkScope.SessionOnly(
                this,
                PersistenceWorkKind.Update,
                opened,
                PersistenceWorkScope.MissingHandleCode,
                PersistenceWorkScope.MissingTaskHandleText);
        }

        return new PersistenceWorkScope(
            this,
            PersistenceWorkKind.Update,
            opened,
            created.Task,
            PowerFramework.Shared.Kernel.RetCode.OK,
            string.Empty);
    }

    /// <summary>
    /// Begins a transaction session, or reports why it could not be begun.
    /// </summary>
    /// <param name="session">The session request the caller composed, descriptor and flags included.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The handle when one was issued, together with the outcome and its diagnostic.</returns>
    /// <remarks>
    /// <para>
    /// THE DESCRIPTOR IS THE CALLER'S TO COMPOSE, AND THIS METHOD DELIBERATELY DOES NOT INVENT ONE. It used
    /// to send an empty descriptor unconditionally, on the reasoning that a descriptor names a database and
    /// carries a password and both are Persistence's business alone. That reasoning is right about the
    /// DEFAULT and wrong as a hard rule, and it had two consequences worth naming: it made
    /// <c>DataServices:PersistenceSession</c> - a bound, validated options group - dead configuration that
    /// a validator still enforced, and it dropped the two connection-parameter FLAGS, which are not
    /// credentials at all but per-session behaviour that decides whether the upstream binds parameters or
    /// interpolates literals into the statement text.
    /// </para>
    /// <para>
    /// THE DEFAULT IS STILL EMPTY, so nothing is weakened by the change: with no
    /// <c>DataServices:PersistenceSession</c> configured, the composed descriptor names no database and
    /// holds no credential, and Persistence resolves its own connection from its own options exactly as
    /// before (AAP 0.6.6). A deployment that DOES configure the group gets what C-08 publishes - the
    /// descriptor mirroring <c>transactiondata.srs</c> field for field with <c>logpass</c> write-only - and
    /// no value reaches this code as a literal: every field arrives through the options pattern from the
    /// orchestration secret layer (constraint C-F).
    /// </para>
    /// <para>
    /// THE PASSWORD TRAVELS ON THIS CALL AND ON NO OTHER, which is the reason a session exists as a concept:
    /// it is resolved ONCE here so that the field does not appear on every later task-scoped call, is never
    /// logged, never echoed, and is not retained after the response.
    /// </para>
    /// </remarks>
    private async Task<(SessionHandle? Session, long ReturnCode, string ErrorText)> BeginWorkSessionAsync(
        BeginSessionRequest session,
        CancellationToken cancellationToken)
    {
        BeginSessionResponse begun = await BeginSessionAsync(session, cancellationToken)
            .ConfigureAwait(false);

        long code = begun.Status is null ? PowerFramework.Shared.Kernel.RetCode.E_INTERNAL_ERROR : (long)begun.Status.RetCode;

        if (code != PowerFramework.Shared.Kernel.RetCode.OK)
        {
            return (null, code, begun.Status?.ErrorText ?? string.Empty);
        }

        if (begun.Session is null || string.IsNullOrWhiteSpace(begun.Session.SessionId))
        {
            return (
                null,
                PersistenceWorkScope.MissingHandleCode,
                PersistenceWorkScope.MissingSessionHandleText);
        }

        return (begun.Session, PowerFramework.Shared.Kernel.RetCode.OK, string.Empty);
    }

    /// <summary>
    /// Releases a task through whichever contract owns it, recording rather than raising a failure.
    /// </summary>
    /// <param name="kind">Which contract owns the task.</param>
    /// <param name="task">The task to release.</param>
    /// <returns>A task representing the release.</returns>
    /// <remarks>
    /// <para>
    /// A FAILURE IS RECORDED AND SWALLOWED, and the reason is that this runs on the fault path. Raising here
    /// would REPLACE the operation's own result: a successful retrieval whose cleanup hiccuped would be
    /// reported as a failure, and a failing one would have its real cause replaced by its cleanup's. The
    /// record carries the code so an operator can see a registry leaking.
    /// </para>
    /// <para>
    /// CANCELLATION IS NOT PROPAGATED INTO THE RELEASE. It is reached BECAUSE the caller's token was
    /// cancelled, so passing that token would cancel the very call that undoes the acquisition - and the
    /// handle would then be held for the process's lifetime. The release is issued with
    /// <see cref="CancellationToken.None"/> deliberately.
    /// </para>
    /// </remarks>
    internal async Task ReleaseWorkTaskAsync(PersistenceWorkKind kind, TaskHandle task)
    {
        ArgumentNullException.ThrowIfNull(task);

        try
        {
            OperationStatus? status = kind == PersistenceWorkKind.Query
                ? (await ReleaseQueryTaskAsync(
                        new ReleaseQueryTaskRequest { Task = task },
                        CancellationToken.None)
                    .ConfigureAwait(false)).Status
                : (await ReleaseUpdateTaskAsync(
                        new ReleaseUpdateTaskRequest { Task = task },
                        CancellationToken.None)
                    .ConfigureAwait(false)).Status;

            if (status is not null && status.RetCode != WireRetCode.Ok)
            {
                _logger.LogWarning(
                    "Releasing the {WorkKind} task {TaskId} answered {ReturnCode}, so the server may still "
                    + "be holding it. The operation's own result is unaffected: a cleanup failure is "
                    + "recorded rather than raised, because raising it would replace the answer the caller "
                    + "is entitled to.",
                    kind,
                    LogSafeText.Render(task.TaskId),
                    status.RetCode);
            }
        }
        catch (RpcException failure)
        {
            _logger.LogWarning(
                "Releasing the {WorkKind} task {TaskId} failed with gRPC status {StatusCode}, so the "
                + "server may still be holding it. Only the status code is recorded; the failure is not "
                + "raised, for the reason above.",
                kind,
                LogSafeText.Render(task.TaskId),
                failure.StatusCode);
        }
    }

    /// <summary>
    /// Ends a transaction session, recording rather than raising a failure.
    /// </summary>
    /// <param name="session">The session to end.</param>
    /// <returns>A task representing the release.</returns>
    /// <remarks>On the same terms as <see cref="ReleaseWorkTaskAsync"/>.</remarks>
    internal async Task EndWorkSessionAsync(SessionHandle session)
    {
        ArgumentNullException.ThrowIfNull(session);

        try
        {
            EndSessionResponse ended = await EndSessionAsync(
                    new EndSessionRequest { Session = session },
                    CancellationToken.None)
                .ConfigureAwait(false);

            if (ended.Status is not null && ended.Status.RetCode != WireRetCode.Ok)
            {
                _logger.LogWarning(
                    "Ending session {SessionId} answered {ReturnCode}, so its pooled-transaction reference "
                    + "may still be held. Recorded rather than raised.",
                    LogSafeText.Render(session.SessionId),
                    ended.Status.RetCode);
            }
        }
        catch (RpcException failure)
        {
            _logger.LogWarning(
                "Ending session {SessionId} failed with gRPC status {StatusCode}, so its "
                + "pooled-transaction reference may still be held. Recorded rather than raised.",
                LogSafeText.Render(session.SessionId),
                failure.StatusCode);
        }
    }

    /// <summary>
    /// Reads a session's descriptor through the REDACTED read model. Contract <b>C-08</b>, legacy
    /// <c>of_gettransdata</c>.
    /// </summary>
    /// <param name="request">The session handle.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>
    /// The contract's response. Its descriptor is the VIEW type, which carries no password, no connection
    /// parameters and no user parameters - those field numbers are reserved, so the omission is structural
    /// and cannot be undone by a caller.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="RpcException">The call failed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <remarks>
    /// The view additionally exposes the two connection-parameter flags as flags, which is the only part
    /// of the connection parameters a caller may see - the parsed answer rather than the parameter string
    /// it was parsed from.
    /// <para>RETRY SAFETY: SAFE - read-only.</para>
    /// </remarks>
    public virtual Task<GetTransactionDataResponse> GetTransactionDataAsync(
        GetTransactionDataRequest request,
        CancellationToken cancellationToken) =>
        InvokeAsync(_transaction.GetTransactionDataAsync, request, ReadTokenRequest, cancellationToken);

    /// <summary>
    /// Sets a session's autocommit switch. Contract <b>C-08</b>.
    /// </summary>
    /// <param name="request">The session handle and the switch.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The contract's response.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="RpcException">The call failed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <remarks>
    /// A PLAIN BOOLEAN HERE, DELIBERATELY UNLIKE CONTRACT C-07's TRI-VALUED MODE. The session switch is
    /// the connection's own autocommit state; C-07's <c>AC_NATIVE</c> is a statement-execution shape that
    /// manipulates this switch around one statement
    /// [ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlcommand.sru:L88, :L105-L107]. Conflating the
    /// two would lose that distinction. Turning this switch ON makes commit and rollback answer "failed"
    /// [n_cst_thread_trans.sru:L240, :L185], which is preserved behaviour rather than an error condition.
    /// </remarks>
    public virtual Task<SetTransactionAutoCommitResponse> SetTransactionAutoCommitAsync(
        SetTransactionAutoCommitRequest request,
        CancellationToken cancellationToken) =>
        InvokeAsync(_transaction.SetAutoCommitAsync, request, WriteTokenRequest, cancellationToken);

    /// <summary>
    /// 🔴 COMMITS OR ROLLS BACK a session according to its statement status - the ported
    /// <c>of_autocommit</c> checkpoint. Contract <b>C-08</b>.
    /// </summary>
    /// <param name="request">The session to check point.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The contract's response.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="RpcException">The call failed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <remarks>
    /// <para>
    /// <b>THE NAME READS LIKE A SETTINGS QUERY AND THE BEHAVIOUR IS THE OPPOSITE, WHICH IS WHY THIS
    /// SUMMARY LEADS WITH THE VERBS.</b> It described itself as reading the autocommit switch, which is
    /// what <see cref="SetTransactionAutoCommitAsync"/>'s counterpart would do; the operation actually
    /// ROLLS BACK on a non-zero statement status - only when autocommit is off - and otherwise COMMITS with
    /// auto-rollback enabled, over shared work
    /// [<c>Grpc/TransactionService.AutoCommit</c>, <c>n_cst_thread_trans.sru:L370-L381</c>].
    /// </para>
    /// <para>
    /// 🔴 RETRY SAFETY: <b>NOT SAFE.</b> A replay after a lost response commits or rolls back a SECOND
    /// time, against whatever the session has accumulated since. It belongs with <c>Commit</c> and
    /// <c>Rollback</c> and is excluded from the replay-safe roster alongside them
    /// [<c>Clients/OutboundCallPolicy</c>]; the mistaken read-only claim here is what had admitted it.
    /// </para>
    /// </remarks>
    public virtual Task<AutoCommitResponse> AutoCommitAsync(
        AutoCommitRequest request,
        CancellationToken cancellationToken) =>
        InvokeAsync(_transaction.AutoCommitAsync, request, WriteTokenRequest, cancellationToken);

    /// <summary>
    /// Commits a session's work. Contract <b>C-08</b>, legacy <c>of_commit</c>.
    /// </summary>
    /// <param name="request">
    /// The session handle and, optionally, whether a failed commit should roll back automatically.
    /// </param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The contract's response.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="RpcException">The call failed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <remarks>
    /// PRESERVED LEGACY BEHAVIOUR TO READ RATHER THAN TREAT AS AN ERROR: a commit answers "failed" while
    /// autocommit is on [n_cst_thread_trans.sru:L240], and answers the invalid-transaction code once the
    /// session has been marked broken [:L241].
    /// <para>RETRY SAFETY: NOT SAFE - a commit whose outcome is unknown must be resolved by reading state,
    /// not by committing again.</para>
    /// </remarks>
    public virtual Task<CommitResponse> CommitAsync(
        CommitRequest request,
        CancellationToken cancellationToken) =>
        InvokeAsync(_transaction.CommitAsync, request, WriteTokenRequest, cancellationToken);

    /// <summary>
    /// Commits a session's work with automatic rollback on failure - the no-argument legacy form. Contract
    /// <b>C-08</b>.
    /// </summary>
    /// <param name="session">The session handle.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The contract's response.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="session"/> is <see langword="null"/>.</exception>
    /// <exception cref="RpcException">The call failed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <remarks>
    /// PRESERVED LEGACY ARITY AND ITS PRESERVED DEFAULT. The legacy no-argument commit delegates to the
    /// automatic-rollback form with TRUE - <c>public function long of_commit ();return of_Commit(true)</c>
    /// [n_cst_thread_trans.sru:L383], mirrored on the caller-side proxy
    /// [n_cst_threading_task_sqlbase.sru:L200] - so this overload sends that value explicitly rather than
    /// leaving the optional field absent, where its meaning would be the server's to choose.
    /// </remarks>
    public virtual Task<CommitResponse> CommitAsync(
        SessionHandle session,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        return CommitAsync(
            new CommitRequest { Session = session, AutoRollback = true },
            cancellationToken);
    }

    /// <summary>
    /// Rolls a session's work back. Contract <b>C-08</b>, legacy <c>of_rollback</c>.
    /// </summary>
    /// <param name="request">The session handle.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The contract's response.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="RpcException">The call failed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <remarks>
    /// Answers "failed" while autocommit is on [n_cst_thread_trans.sru:L185] and the invalid-transaction
    /// code once the session is broken [:L186] - both preserved, neither an exception.
    /// </remarks>
    public virtual Task<RollbackResponse> RollbackAsync(
        RollbackRequest request,
        CancellationToken cancellationToken) =>
        InvokeAsync(_transaction.RollbackAsync, request, WriteTokenRequest, cancellationToken);

    /// <summary>
    /// Reads whether a session's connection is live. Contract <b>C-08</b>.
    /// </summary>
    /// <param name="request">The session handle.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>
    /// The contract's response, carrying both the answer and whether the connection was actually PROBED to
    /// obtain it - a cached answer and a probed one are different facts.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="RpcException">The call failed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <remarks>RETRY SAFETY: SAFE - read-only.</remarks>
    public virtual Task<IsConnectedResponse> IsConnectedAsync(
        IsConnectedRequest request,
        CancellationToken cancellationToken) =>
        InvokeAsync(_transaction.IsConnectedAsync, request, ReadTokenRequest, cancellationToken);

    /// <summary>
    /// Reads a session's database type. Contract <b>C-08</b>, legacy <c>of_getdbtype</c>.
    /// </summary>
    /// <param name="request">The session handle.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The contract's response, carrying one of the enumeration's TWO members.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="RpcException">The call failed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <remarks>
    /// TWO MEMBERS, AND THE LEGACY RESOLUTION IS A SUBSTRING TEST WITH A DEFAULT: it answers ORACLE when
    /// the driver name contains "ORACLE" and MSSQL otherwise [n_cst_thread_trans.sru:L356-L359], so MSSQL
    /// is what an unrecognised driver reports rather than an error. SQLITE IS NOT A MEMBER of this
    /// enumeration and is not added [:L60-L61].
    /// <para>RETRY SAFETY: SAFE - read-only.</para>
    /// </remarks>
    public virtual Task<GetDatabaseTypeResponse> GetDatabaseTypeAsync(
        GetDatabaseTypeRequest request,
        CancellationToken cancellationToken) =>
        InvokeAsync(_transaction.GetDatabaseTypeAsync, request, ReadTokenRequest, cancellationToken);

    /// <summary>
    /// Reads a session's last driver state. Contract <b>C-08</b>.
    /// </summary>
    /// <param name="request">The session handle.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>
    /// The contract's response: the driver codes, the affected-row count, the driver text, the return
    /// data, and the three predicates the legacy published.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="RpcException">The call failed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <remarks>
    /// THE PREDICATES ARE CARRIED RATHER THAN LEFT TO BE DERIVED, because the legacy publishes them as
    /// functions with their own definitions - failed is a driver code below zero
    /// [n_cst_thread_trans.sru:L337] and succeeded is zero or above [:L340] - and re-deriving them at each
    /// call site is how a boundary quietly drifts. A caller wanting the ported algebra instead should read
    /// the outcome code from the status and hand it to <c>Predicates</c>.
    /// <para>RETRY SAFETY: SAFE - read-only.</para>
    /// </remarks>
    public virtual Task<GetSessionStateResponse> GetSessionStateAsync(
        GetSessionStateRequest request,
        CancellationToken cancellationToken) =>
        InvokeAsync(_transaction.GetSessionStateAsync, request, ReadTokenRequest, cancellationToken);

    /// <summary>
    /// Clears a session's last driver state. Contract <b>C-08</b>, legacy <c>of_clearstate</c>.
    /// </summary>
    /// <param name="request">The session handle.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The contract's response.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="RpcException">The call failed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <remarks>
    /// The legacy update path clears the state before each attempt
    /// [n_cst_thread_task_sqlupdate.sru:L186], so a stale driver code cannot be read as this attempt's.
    /// </remarks>
    public virtual Task<ClearStateResponse> ClearStateAsync(
        ClearStateRequest request,
        CancellationToken cancellationToken) =>
        InvokeAsync(_transaction.ClearStateAsync, request, WriteTokenRequest, cancellationToken);

    /// <summary>
    /// Marks a session broken so it is not reused. Contract <b>C-08</b>.
    /// </summary>
    /// <param name="request">The session handle.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The contract's response.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="RpcException">The call failed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <remarks>
    /// ONE-WAY AND DELIBERATELY SO. The legacy has no un-break path: once broken, commit and rollback both
    /// answer the invalid-transaction code [n_cst_thread_trans.sru:L186, :L241], and the pool's business is
    /// to stop handing the session out. No "unbreak" member is offered here, because none exists to port.
    /// </remarks>
    public virtual Task<SetBrokenResponse> SetBrokenAsync(
        SetBrokenRequest request,
        CancellationToken cancellationToken) =>
        InvokeAsync(_transaction.SetBrokenAsync, request, WriteTokenRequest, cancellationToken);

    /// <summary>
    /// Derives a grid presentation syntax from a statement. Contract <b>C-08</b>, legacy
    /// <c>of_gridsyntaxfromsql</c>.
    /// </summary>
    /// <param name="request">The session handle and the statement, both opaque to this client.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The contract's response, carrying the syntax or the failure.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="RpcException">The call failed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <remarks>
    /// THE DERIVATION IS THE SERVER'S, NOT THIS CLIENT'S. The legacy asks its own transaction object to
    /// produce the syntax [n_cst_thread_trans.sru:L385-L387], which requires a live connection to describe
    /// the statement's result shape. Nothing about the statement is parsed here.
    /// <para>RETRY SAFETY: SAFE - it derives a description and changes no data.</para>
    /// </remarks>
    public virtual Task<GridSyntaxFromSqlResponse> GridSyntaxFromSqlAsync(
        GridSyntaxFromSqlRequest request,
        CancellationToken cancellationToken) =>
        InvokeAsync(_transaction.GridSyntaxFromSqlAsync, request, ReadTokenRequest, cancellationToken);

    // ==============================================================================================
    //  INTERNALS - authentication, invocation, the conflict decode, the busy guard, the two enum
    //  conversions, and the redaction-safe diagnostics.
    // ==============================================================================================

    /// <summary>
    /// Reads the rich-error binding C-06's <c>Update</c> declares, from the generated descriptor.
    /// </summary>
    /// <returns>The binding, or <see langword="null"/> when the method or the option is absent.</returns>
    /// <remarks>
    /// Local reflection over generated metadata; no input or output, and no throwing path, so it is safe
    /// in a static initializer. Every accessor named here is GENERATED from the protocol definition, which
    /// is what keeps the contract's own decoding rules out of hand-written code.
    /// </remarks>
    private static RichErrorBinding? ReadUpdateRichErrorBinding()
    {
        MethodDescriptor? method = UpdateService.Descriptor.FindMethodByName(UpdateMethodName);
        MethodOptions? options = method?.GetOptions();

        return options?.GetExtension(CommonV1Extensions.RichError);
    }

    /// <summary>
    /// Widens a wire outcome code to the numeric form the ported predicates classify.
    /// </summary>
    /// <param name="value">The wire code carried on a contract status.</param>
    /// <returns>The same value as a numeric outcome code.</returns>
    /// <remarks>
    /// THE ONE PLACE THE TWO RetCode FAMILIES MEET, AND THE CONVERSION IS STATED RATHER THAN ASSUMED. The
    /// generated wire enum and the ported kernel constants are declared to agree value for value, but that
    /// agreement is a REVIEW-TIME INVARIANT WITH NO COMPILE-TIME EDGE: the contracts project deliberately
    /// holds no reference to the shared kernel, so no compiler checks it for anyone. Keeping the widening
    /// here means there is exactly one line to inspect when that invariant is reviewed, instead of a cast
    /// at every call site. Nothing converts in the other direction - a status this client PRODUCES is built
    /// from the wire enum directly, so a kernel constant is never narrowed onto the wire.
    /// </remarks>
    private static long WidenToKernelRetCode(WireRetCode value) => (long)value;

    /// <summary>
    /// Maps a legacy modification style onto the wire enumeration.
    /// </summary>
    /// <param name="modifyStyle">
    /// The legacy value: <c>Enums.SQL_MS_REPLACE</c>, <c>Enums.SQL_MS_APPEND</c> or
    /// <c>Enums.SQL_MS_PREPEND</c>.
    /// </param>
    /// <returns>
    /// The wire member, or <see langword="null"/> when the value is not one of the legacy three - which the
    /// caller turns into the legacy's own invalid-argument outcome.
    /// </returns>
    /// <remarks>
    /// THE ONE PLACE THE MODIFY-STYLE FAMILIES MEET. The legacy declares exactly three styles and NO ZERO
    /// [ws_objects/pfw.shared.pbl.src/enums.sru:L718-L720], while proto3 requires every enumeration's first
    /// member to be zero - so the wire type carries an extra unspecified member that is a MECHANICAL
    /// ARTEFACT AND NOT A LEGACY CONSTANT. It has no oracle locator and must never acquire one.
    /// <para>
    /// THE UNSPECIFIED MEMBER IS THEREFORE NEVER RETURNED FROM HERE AND NEVER REACHES THE WIRE AS A REAL
    /// MODIFICATION STYLE. An unmapped value is refused with the invalid-argument code rather than being
    /// silently defaulted to "replace": the legacy never validates this argument at all
    /// [n_cst_thread_task_sqlquery.sru:L271-L281, :L288-L298], so there is NO legacy behaviour to copy
    /// here, and that absence is precisely why the handling has to be defined rather than guessed.
    /// Defaulting would WIDEN the contract with a guess; the standing posture is to narrow it with a
    /// defined error.
    /// </para>
    /// </remarks>
    private static SqlModifyStyle? ToWireModifyStyle(long modifyStyle) => modifyStyle switch
    {
        Enums.SQL_MS_REPLACE => SqlModifyStyle.SqlMsReplace,
        Enums.SQL_MS_APPEND => SqlModifyStyle.SqlMsAppend,
        Enums.SQL_MS_PREPEND => SqlModifyStyle.SqlMsPrepend,
        _ => null,
    };

    /// <summary>
    /// Builds the legacy invalid-argument outcome with a diagnostic message.
    /// </summary>
    /// <param name="detail">
    /// The diagnostic. It names the oracle locator the rejection reproduces and NEVER quotes the rejected
    /// value, because a clause, a statement or a column name is caller data.
    /// </param>
    /// <returns>The status.</returns>
    /// <remarks>
    /// THE CODE IS THE CONTRACT AND THE TEXT IS ONLY A DIAGNOSTIC. The legacy returns a bare numeric code
    /// with no accompanying text for every guard reproduced in this file, so a caller must branch on the
    /// code and never on the message.
    /// </remarks>
    private static OperationStatus InvalidArgument(string detail) => new()
    {
        RetCode = WireRetCode.EInvalidArgument,
        ErrorText = detail,
    };

    /// <summary>
    /// Builds the invalid-argument outcome for a modification style outside the legacy three.
    /// </summary>
    /// <param name="modifyStyle">The rejected value, which is a protocol constant rather than caller data.</param>
    /// <returns>The status.</returns>
    private static OperationStatus UnmappedModifyStyle(long modifyStyle) => InvalidArgument(string.Create(
        CultureInfo.InvariantCulture,
        $"The modification style {modifyStyle} is not one of the legacy three - replace (1), append (2) "
        + $"or prepend (3) [ws_objects/pfw.shared.pbl.src/enums.sru:L718-L720]. It is refused rather than "
        + $"defaulted, and the protocol's unspecified member is never sent as a modification style."));

    /// <summary>
    /// Builds the legacy busy outcome for a task already carrying an operation through this client.
    /// </summary>
    /// <param name="taskId">The task identifier, a protocol handle rather than caller data.</param>
    /// <returns>The status.</returns>
    /// <remarks>
    /// The code is the legacy's own: every caller-side proxy setter answers it while its task is running
    /// [ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlbase.sru:L57].
    /// </remarks>
    private static OperationStatus Busy(string taskId) => new()
    {
        RetCode = WireRetCode.EBusy,
        ErrorText = string.Create(
            CultureInfo.InvariantCulture,
            $"Task {taskId} is already carrying an operation issued through this client, so the request "
            + $"was refused rather than allowed to mutate in-flight state "
            + $"[ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlbase.sru:L57]."),
    };

    /// <summary>
    /// Reduces a task handle to a usable identifier.
    /// </summary>
    /// <param name="task">The handle, possibly absent.</param>
    /// <returns>
    /// The identifier, or <see langword="null"/> when there is none to track - an absent handle or an empty
    /// identifier.
    /// </returns>
    /// <remarks>
    /// AN UNIDENTIFIED REQUEST IS FORWARDED RATHER THAN REJECTED HERE. Whether a handle is required is the
    /// contract's question and Persistence's to answer with its own code; inventing a client-side rejection
    /// would pre-empt an outcome this file has no oracle for.
    /// </remarks>
    private static string? NormalizeTaskId(TaskHandle? task)
    {
        string? id = task?.TaskId;

        return string.IsNullOrEmpty(id) ? null : id;
    }

    /// <summary>
    /// Produces a completed busy rejection when the request's task is already in flight through this
    /// client.
    /// </summary>
    /// <typeparam name="TResponse">The contract response type being shaped.</typeparam>
    /// <param name="task">The task handle from the request.</param>
    /// <param name="shape">Wraps the status in the contract's own response message.</param>
    /// <returns>
    /// A completed task carrying the rejection, or <see langword="null"/> when the operation may proceed.
    /// </returns>
    /// <remarks>
    /// A CHECK RATHER THAN A RESERVATION, deliberately. A setter is a single call that either happens or
    /// does not, so it does not need to hold the task for a span the way a retrieval or an update does;
    /// reserving it here would make two concurrent setters exclude each other, which the legacy - whose
    /// guard tests the RUNNING OPERATION and not other setters - does not do.
    /// </remarks>
    private Task<TResponse>? RejectIfTaskBusy<TResponse>(
        TaskHandle? task,
        Func<OperationStatus, TResponse> shape)
    {
        string? taskId = NormalizeTaskId(task);

        if (taskId is null || !_inFlightTasks.ContainsKey(taskId))
        {
            return null;
        }

        return Task.FromResult(shape(Busy(taskId)));
    }

    /// <summary>
    /// Produces a completed rejection when a clause specification fails the legacy guard.
    /// </summary>
    /// <typeparam name="TResponse">The contract response type being shaped.</typeparam>
    /// <param name="clause">The clause specification from the request.</param>
    /// <param name="shape">Wraps the status in the contract's own response message.</param>
    /// <returns>
    /// A completed task carrying the rejection, or <see langword="null"/> when the specification is
    /// acceptable.
    /// </returns>
    /// <remarks>
    /// BOTH CONDITIONS AND ONE CODE, EXACTLY AS THE LEGACY WROTE IT:
    /// <c>if selectIndex &lt;= 0 or clause = "" then return RetCode.E_INVALID_ARGUMENT</c>
    /// [ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L271, :L288]. An absent
    /// specification is refused by the same code, because a clause setter with nothing to set cannot
    /// satisfy either condition. The unspecified modification style is refused here too, so it cannot reach
    /// the wire as a real style.
    /// </remarks>
    private static Task<TResponse>? RejectIfClauseInvalid<TResponse>(
        SqlClauseSpec? clause,
        Func<OperationStatus, TResponse> shape)
    {
        if (clause is null)
        {
            return Task.FromResult(shape(InvalidArgument(
                "A clause specification is required: the legacy setter refuses an empty clause outright "
                + "[ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L271].")));
        }

        if (clause.SelectIndex <= 0 || string.IsNullOrEmpty(clause.Clause))
        {
            return Task.FromResult(shape(InvalidArgument(
                "The select index must be one or greater - it is ONE-BASED, so zero is an error rather "
                + "than a request for a default - and the clause text must not be empty "
                + "[ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L271, :L288]. The "
                + "clause text itself is deliberately not quoted here.")));
        }

        if (clause.ModifyStyle == SqlModifyStyle.SqlMsUnspecified)
        {
            return Task.FromResult(shape(UnmappedModifyStyle((long)clause.ModifyStyle)));
        }

        return null;
    }

    /// <summary>
    /// Obtains a bearer credential and packages it as the call options every outbound call uses.
    /// </summary>
    /// <param name="cancellationToken">Cancels the token acquisition and the call it is built for.</param>
    /// <returns>Call options carrying the authorization header and the caller's cancellation token.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <remarks>
    /// <para>
    /// THE CREDENTIAL TRAVELS AS CALL METADATA RATHER THAN AS gRPC CALL CREDENTIALS, and the choice is
    /// forced rather than stylistic: call credentials are refused on an insecure channel unless the channel
    /// explicitly opts out of that protection, so a client built that way would fail outright wherever the
    /// topology is plain HTTP. A metadata header works on every channel the composition root can configure,
    /// and the header value is treated as an opaque string throughout - this service parses no token and
    /// mints none.
    /// </para>
    /// <para>
    /// THE DEADLINE COMES FROM THE COMPOSITION ROOT, WHICH IS WHAT MAKES IT LEGITIMATE. This file
    /// previously set none, on the reasoning that a duration invented here would have no derivation and
    /// that the policy belonged where the resilience pipeline is configured. Both halves of that were
    /// right; what was missing was the policy. It now exists as <c>DataServices:Resilience:Persistence</c>,
    /// the unary bound is the same setting that configures that pipeline's total request timeout, and the
    /// stream bound is derived from Persistence's own handle idle expiry - so no duration originates here.
    /// </para>
    /// <para>
    /// WHY CANCELLATION ALONE WAS NOT ENOUGH, since it was already threaded through every member. A
    /// cancellation token bounds THIS process's willingness to wait and tells the upstream nothing. When
    /// this caller goes away in a way the transport has not yet noticed - a half-open connection, a
    /// container killed mid-request - Persistence keeps working and keeps the query, update, command or
    /// transaction handle alive, and only its own idle sweep eventually reclaims it. Those handles are
    /// bounded per principal and globally, so the abandoned work consumes admission capacity a live caller
    /// then cannot get. The deadline is what puts the bound on the wire where the server can act on it.
    /// </para>
    /// <para>
    /// NEITHER THE CREDENTIAL NOR THE SCOPE STRINGS ARE EVER LOGGED (constraint C-F). The granted set is
    /// READ - which the token contract obliges a caller to do instead of assuming its request was honoured
    /// in full - and reported by NAME COUNT only.
    /// </para>
    /// <para>
    /// <b>A GRANT MISSING THE REQUIRED SCOPE IS REFUSED HERE, BY EXACT NAME.</b> The previous shape
    /// compared scope COUNTS and proceeded on a narrowing, which is wrong in two independent ways: a count
    /// cannot tell WHICH scope was withheld, and an issuer that granted a completely different scope of the
    /// same cardinality would have satisfied the comparison entirely. Since each request now asks for
    /// exactly the one name the call needs, absence of that name means the call cannot succeed, and sending
    /// it anyway would trade a clear local refusal for an opaque upstream one - or, on an upstream that
    /// checked authentication but not authorization, for a call that should never have been made.
    /// </para>
    /// <para>
    /// The refusal is an <see cref="RpcException"/> carrying
    /// <see cref="StatusCode.PermissionDenied"/> - the same status the upstream would answer, so a caller
    /// handles one shape rather than two, and the message names the scope without reproducing any part of
    /// the credential.
    /// </para>
    /// </remarks>
    private async Task<CallOptions> CreateAuthenticatedCallOptionsAsync(
        OutboundCallClass callClass,
        ServiceTokenRequest tokenRequest,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ServiceToken token = await _tokenProvider
            .GetTokenAsync(tokenRequest, cancellationToken)
            .ConfigureAwait(false);

        // EXACT, ORDINAL AND CASE-SENSITIVE, because scope names are case-sensitive strings in the OAuth
        // framework. Every requested name must be present; there is no prefix match, no wildcard and no
        // implication between read and write.
        foreach (string required in tokenRequest.Scopes)
        {
            if (!token.GrantedScopes.Contains(required, StringComparer.Ordinal))
            {
                _logger.LogWarning(
                    "Security withheld a required scope for the Persistence audience: {GrantedScopeCount} "
                    + "of {RequestedScopeCount} requested name(s) were granted, so the call was refused "
                    + "before it was sent. Neither the credential nor any scope name is logged.",
                    token.GrantedScopes.Count,
                    tokenRequest.Scopes.Count);

                throw new RpcException(new Status(
                    StatusCode.PermissionDenied,
                    "The credential issued for the Persistence audience does not carry the scope this "
                    + "operation requires, so the call was refused before it was sent."));
            }
        }

        Metadata headers = new()
        {
            { AuthorizationHeaderName, string.Concat(token.TokenType, " ", token.AccessToken) },
        };

        return new CallOptions(
            headers: headers,
            deadline: _deadlines.DeadlineFor(callClass),
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Runs one authenticated unary call: acquire the credential, invoke the stub, return the contract's
    /// own response message unchanged.
    /// </summary>
    /// <typeparam name="TRequest">The request message type.</typeparam>
    /// <typeparam name="TResponse">The response message type.</typeparam>
    /// <param name="operation">The generated stub method to invoke.</param>
    /// <param name="request">The request message.</param>
    /// <param name="tokenRequest">
    /// The credential this operation needs - <see cref="ReadTokenRequest"/> or
    /// <see cref="WriteTokenRequest"/>. Named at every call site rather than defaulted, so a new member
    /// cannot acquire write authority by forgetting to say what it needs.
    /// </param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The response, exactly as the upstream produced it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// This helper exists so that credential attachment and argument validation are written once rather
    /// than thirty-five times, which is the only way to be sure every member really does attach one. IT
    /// ADDS NO BEHAVIOUR OF ITS OWN: no status is caught here, no payload is inspected, no response is
    /// transformed and nothing is retried. Members that must translate a failure or emit a diagnostic do so
    /// in their own bodies, where the translation is visible.
    /// </remarks>
    private async Task<TResponse> InvokeAsync<TRequest, TResponse>(
        Func<TRequest, CallOptions, AsyncUnaryCall<TResponse>> operation,
        TRequest request,
        ServiceTokenRequest tokenRequest,
        CancellationToken cancellationToken)
        where TRequest : class
    {
        ArgumentNullException.ThrowIfNull(request);

        CallOptions options = await CreateAuthenticatedCallOptionsAsync(
                OutboundCallClass.Unary,
                tokenRequest,
                cancellationToken)
            .ConfigureAwait(false);

        return await operation(request, options).ConfigureAwait(false);
    }

    /// <summary>
    /// Decodes the conflict detail from a failed call's trailers, using the binding the descriptor declares.
    /// </summary>
    /// <param name="exception">The failed call.</param>
    /// <param name="retCode">
    /// Receives the reconciled outcome code the trailer carried, or zero when nothing was decoded.
    /// </param>
    /// <returns>
    /// The conflict detail, or <see langword="null"/> when this call declares no rich-error contract, when
    /// the status is not the one the binding names, when the payload type is one this client cannot decode,
    /// when the trailer is absent, when it carries a database failure rather than a conflict, or when it
    /// cannot be parsed.
    /// </returns>
    /// <remarks>
    /// <para>
    /// EVERY ONE OF THOSE "NO" ANSWERS IS SAFE, because the caller's response to <see langword="null"/> is
    /// to RETHROW the original exception with its status and its complete trailer set intact. Nothing is
    /// swallowed on any path through this method: the worst case is that a caller sees the raw gRPC failure
    /// rather than a typed conflict, and the payload remains available on it.
    /// </para>
    /// <para>
    /// THE TRAILER KEY, THE STATUS AND THE PAYLOAD TYPE ALL COME FROM THE DESCRIPTOR, not from a literal
    /// written here. The key's trailing binary marker is not decoration: gRPC transports a metadata key with
    /// that suffix as binary and permits only text under any other, so a protobuf payload under a
    /// non-binary key would be corrupted in transit - which is why the byte-valued accessor is the one used
    /// to read it.
    /// </para>
    /// <para>
    /// A DATABASE FAILURE IN THE TRAILER IS NOT DECODED INTO ANYTHING LOGGABLE HERE. The other arm of the
    /// payload carries a statement field that would hold the complete generated statement with its literal
    /// values interpolated, so it is left on the exception for a caller entitled to it and is neither logged
    /// nor echoed.
    /// </para>
    /// </remarks>
    private ConflictDetail? TryReadConflictDetail(RpcException exception, out long retCode) =>
        TryReadConflictDetail(exception, UpdateRichErrorBinding, out retCode);

    /// <summary>
    /// Decodes a conflict payload against a supplied binding rather than the descriptor's own.
    /// </summary>
    /// <param name="exception">The gRPC failure to read the trailer from.</param>
    /// <param name="binding">
    /// The binding to honour. Production passes the one read from the generated descriptor; a test passes a
    /// drifted one to drive the refusal arms.
    /// </param>
    /// <param name="retCode">Receives the widened code the payload declared, or zero.</param>
    /// <returns>The detail, or <see langword="null"/> on any of the refusal arms.</returns>
    /// <remarks>
    /// WHY THIS OVERLOAD EXISTS. The three refusal arms below defend against a DRIFT between the generated
    /// contract and this client - a changed trailer key, a changed status code, a changed payload type. They
    /// are unreachable through the public surface precisely because no drift exists today, which would leave
    /// the guarantee they encode ("surface the failure unchanged; never parse bytes as a type they may not
    /// be") asserted only by intent. Taking the binding as a parameter makes each arm drivable from a test
    /// without weakening the production property that the binding is read once, from the descriptor, and
    /// never written as a literal.
    /// </remarks>
    internal ConflictDetail? TryReadConflictDetail(
        RpcException exception,
        RichErrorBinding? binding,
        out long retCode)
    {
        ArgumentNullException.ThrowIfNull(exception);

        retCode = 0;

        if (binding is null || string.IsNullOrEmpty(binding.TrailerKey))
        {
            _logger.LogWarning(
                "The update operation's descriptor declares no rich-error binding, so a conflict trailer "
                + "cannot be located. This indicates a drift between the generated contract and this "
                + "client; the gRPC failure is surfaced unchanged.");

            return null;
        }

        if (binding.GrpcStatusCode != (int)exception.StatusCode)
        {
            return null;
        }

        if (!string.Equals(binding.PayloadType, SupportedRichErrorPayloadType, StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "The update operation's rich-error binding names payload type {DeclaredPayloadType}, which "
                + "this client cannot decode; it understands {SupportedPayloadType}. The gRPC failure is "
                + "surfaced unchanged rather than being parsed as a type it may not be.",
                binding.PayloadType,
                SupportedRichErrorPayloadType);

            return null;
        }

        byte[]? raw = exception.Trailers?.GetValueBytes(binding.TrailerKey);
        if (raw is null)
        {
            return null;
        }

        RichErrorTrailer trailer;
        try
        {
            trailer = RichErrorTrailer.Parser.ParseFrom(raw);
        }
        catch (InvalidProtocolBufferException parseFailure)
        {
            // Described rather than attached. A protobuf parse failure quotes the bytes and field numbers
            // it choked on, and those bytes come from the UPSTREAM's trailer - so the message is another
            // party's content, arriving on the one path where that party was already failing.
            _logger.LogWarning(
                "The conflict trailer on an aborted update could not be parsed as "
                + "{SupportedPayloadType}. The gRPC failure is surfaced unchanged, with its trailers "
                + "intact. FaultTypes={FaultTypes}",
                SupportedRichErrorPayloadType,
                ExceptionChain.DescribeTypes(parseFailure));

            return null;
        }

        retCode = trailer.RetCode;

        return trailer.DetailCase == RichErrorTrailer.DetailOneofCase.Conflict ? trailer.Conflict : null;
    }

    /// <summary>
    /// Records the outcome of an update, without reproducing anything sensitive.
    /// </summary>
    /// <param name="taskId">The task identifier, or <see langword="null"/> when the request carried none.</param>
    /// <param name="response">The response as received.</param>
    /// <remarks>
    /// <para>
    /// THE OUTCOME IS CLASSIFIED WITH THE PORTED TRI-STATE ALGEBRA rather than an ad-hoc comparison, so that
    /// a prevention still reads as succeeded and a cancellation reads as neither. The wire code is widened
    /// explicitly through the single conversion this file declares.
    /// </para>
    /// <para>
    /// THE IDENTITY DIAGNOSTIC IS A COUNT, NOT A PRESENCE TEST, because the field is REPEATED - one ordered
    /// block per update table. A presence test would report true unconditionally, since a generated repeated
    /// field is an empty collection rather than null, so it would have stopped diagnosing anything. The
    /// count is the fact worth recording: zero means no block was collected, and a count that disagrees with
    /// the number of tables prepared is the shape of a relay that dropped one.
    /// </para>
    /// </remarks>
    private void LogUpdateOutcome(string? taskId, UpdateResponse response)
    {
        OperationStatus? status = response.Status;

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            long outcome = status is null ? 0L : WidenToKernelRetCode(status.RetCode);
            UpdateCounts? counts = response.Counts;

            _logger.LogDebug(
                "Update on task {TaskId} returned retCode {RetCode} (succeeded: {Succeeded}, cancelled: "
                + "{Cancelled}); {RowsInserted} inserted, {RowsUpdated} updated, {RowsDeleted} deleted; "
                + "{IdentityBlockCount} identity block(s), one per update table, each carrying its primary "
                + "and filter arrays in the order received.",
                LogSafeText.Render(taskId),
                outcome,
                Predicates.IsSucceeded(outcome),
                Predicates.IsCancelled(outcome),
                counts?.Inserted ?? 0L,
                counts?.Updated ?? 0L,
                counts?.Deleted ?? 0L,
                response.Identity.Count);
        }

        LogDatabaseFailure(taskId, status);
    }

    /// <summary>
    /// Records a structured database failure carried on a contract status, with the statement REDACTED.
    /// </summary>
    /// <param name="taskId">The task identifier, or <see langword="null"/> when the request carried none.</param>
    /// <param name="status">The status as received, possibly absent.</param>
    /// <remarks>
    /// THE REDACTION RULE, APPLIED AT THE ONE PLACE A DATABASE ERROR IS EVER WRITTEN OUT. The error's
    /// statement field carries the complete generated statement INCLUDING INTERPOLATED LITERAL VALUES - the
    /// legacy passes the statement produced after parameter values were spliced into it straight into the
    /// database-error event [ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlcommand.sru:L110,
    /// n_cst_thread_task_sqlbase.sru:L464] - and the legacy logger performed no redaction at all. It is
    /// therefore never logged, never echoed and never reconstructed from its parts for a message here, at any
    /// level and in any environment. The driver TEXT is withheld on the same grounds, because driver text can
    /// quote the offending literal back. Only the numeric driver code, the offending buffer and the offending
    /// row are recorded; the whole payload stays on the response for a caller entitled to it.
    /// </remarks>
    private void LogDatabaseFailure(string? taskId, OperationStatus? status)
    {
        DbError? error = status?.DbError;

        if (error is null)
        {
            return;
        }

        _logger.LogWarning(
            "Persistence reported a database failure on task {TaskId}: sqldbcode {SqlDbCode}, buffer "
            + "{Buffer}, row {Row}. The statement text and the driver text are redacted and are not "
            + "logged.",
            LogSafeText.Render(taskId),
            error.Sqldbcode,
            error.Buffer,
            error.Row);
    }
}
