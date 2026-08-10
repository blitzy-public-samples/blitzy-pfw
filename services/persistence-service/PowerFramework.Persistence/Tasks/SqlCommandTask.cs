// =================================================================================================
//  Tasks/SqlCommandTask.cs - the WORKER-THREAD command-execution task behind contract C-07.
//
//  MANAGED PORT OF
//      ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlcommand.sru   (116 lines, read whole)
//          $PBExportComments: 线程SQL命令任务对象~r~n[运行在子线程]  = WORKER-THREAD AFFINITY
//
//  REFERENCE ONLY - read as specification, never ported here:
//      ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlcommand.sru  (58 lines, read whole)
//          the CALLER-SIDE proxy. Its busy guards, its DEBUG assertion and its class-name event all
//          belong to Tasks/TaskProxies/ - see "THE PROXY PAIR IS NOT COLLAPSED" below.
//      ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru        the base, ported in
//                                                                            Tasks/SqlTaskBase.cs
//      ws_objects/pfw.thread.pbl.src/n_cst_thread_task.sru                   the threading substrate
//      ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru               of_Exec / of_GetDBType,
//                                                                            ported in Transactions/
//      ws_objects/pfw.thread.ext.pbl.src/dberrordata.srs                      the error structure,
//                                                                            ported in Errors/
//      ws_objects/pfw.utility.sqlite.pbl.src/n_sqlite.sru                     the arity ceiling
//      ws_objects/pfw.shared.pbl.src/retcode.sru                              the return-code algebra
//
//  Every behavioural claim below and in the body carries a ws_objects/** locator, because the legacy
//  tree is the ONLY statement of intended behaviour that exists (constraint C-C, AAP §0.1.4). Bare
//  `:Lnn` locators refer to n_cst_thread_task_sqlcommand.sru; anything else is named in full.
//
// -------------------------------------------------------------------------------------------------
//  WHAT THIS FILE OWNS, AND WHAT IT ONLY DRIVES
// -------------------------------------------------------------------------------------------------
//  OWNS   the statement, the tri-valued auto-commit mode, the reset, and the execution sequence of
//         `event ondotask` [:L60-L115] with its two epilogues.
//
//  DRIVES but does not implement (constraint C-A; verified against the oracle rather than assumed):
//      * statement execution, positional `?` binding at the provider, multi-statement batch
//        execution and error-text retrieval - all of these are the pooled transaction's `Exec` and
//        `SQLErrText` [n_cst_thread_trans.sru:L219-L240], ported in Transactions/TransactionPool.cs
//      * the leading-`@` mode - see "THE LEADING `@` IS NOT SQL" below
//      * the dialect resolver `of_GetDBType` [n_cst_thread_trans.sru:L356-L362], also in Transactions/
//      * parameter storage, the placeholder scan and literal rendering - `SqlTaskBase.BindParams`
//      * the DbError payload shape and its redaction - Errors/DbErrorData.cs, Errors/SqlRedactor.cs
//      * the connection and its URI grammar - Data/SqliteConnectionFactory.cs. Nothing here opens,
//        selects or names a connection (constraint C-E).
//
// -------------------------------------------------------------------------------------------------
//  THE PROXY PAIR IS NOT COLLAPSED  (AAP §0.4.5.4, §0.6.5 - constraints C-J, C-K)
// -------------------------------------------------------------------------------------------------
//  The legacy ships every concurrency capability TWICE: a worker-side object annotated
//  [运行在子线程] and a caller-side proxy annotated [运行在当前线程]. For commands those are
//  n_cst_thread_task_sqlcommand (this file) and n_cst_threading_task_sqlcommand (Tasks/TaskProxies/).
//  The duality is a CONTRACT, not an implementation detail, so the two are separate managed types.
//
//  Everything in the 58-line proxy is deliberately absent here and is accounted for so that nothing
//  in it is lost:
//      proxy :L9   `string #type = "sqlcommand"`               instance discriminator  -> proxy
//      proxy :L11  the global auto-instance                     forbidden by C-J        -> neither
//      proxy :L17  `constant string TASK_TYPE = "sqlcommand"`   task-type token         -> proxy
//      proxy :L19-L21 AC_OFF/AC_ON/AC_NATIVE re-exported as aliases of THIS object's constants
//                                                              see the .editorconfig note below
//      proxy :L26,:L31 `_of_gettask()` returning `_Task`        proxy's typed accessor  -> proxy
//      proxy :L34  `if of_IsBusy() then return RetCode.E_BUSY`  caller-side busy guard  -> proxy
//      proxy :L36-L38 `#IF DEFINED DEBUG THEN Assert(Len(sql) > 0,"Len(sql) <= 0") #END IF`
//                                                              debug assertion         -> proxy
//      proxy :L40,:L45 delegation to this object's setters      forwarding              -> proxy
//      proxy :L56  `ongettaskclsname` -> "n_cst_thread_task_sqlcommand"  class name     -> proxy
//
//  Consequence worth stating: PowerFramework.Shared.Diagnostics is a ProjectReference of this
//  project because the PROXY needs Assert. This file does not, so it does not import it - every
//  using below is used, which is the rule the whole port is held to.
//
// -------------------------------------------------------------------------------------------------
//  .editorconfig: WHY THE AUTO-COMMIT TRIPLE IS NOT DECLARED HERE
// -------------------------------------------------------------------------------------------------
//  The oracle declares the triple as public constants on this object [:L16-L18]. `Tasks/` is OUTSIDE
//  the scoped CA1707/IDE1006 suppressions in .editorconfig - those exist only for the handful of
//  files listed there by path - and TreatWarningsAsErrors is true, so a SCREAMING_SNAKE declaration
//  in this file would be a BUILD BREAK, not a style opinion.
//
//  The resolution is the one C-07 already provides: the triple is declared once, in the published
//  protocol definition, as `enum AutoCommitMode { AC_OFF = 0; AC_ON = 1; AC_NATIVE = 2; }`
//  [shared/PowerFramework.Contracts/Proto/persistence.v1.proto:L1720-L1727], and this file REFERENCES
//  the generated enum. THE VALUES ARE PRESERVED (0/1/2); ONLY THE DECLARATION SITE MOVES. The same
//  applies to DBT_MSSQL/DBT_ORACLE [same file, :L2031-L2035] and to DwBuffer [common.v1.proto:L655].
//
// -------------------------------------------------------------------------------------------------
//  THE LEADING `@` IS NOT SQL  (constraint C-K)
// -------------------------------------------------------------------------------------------------
//  A leading `@` on a statement handed to the transaction NAMES A DATAWINDOW OBJECT and the rest of
//  the string is that object's name - `if Left(sql,1) = "@" then ds.DataObject = Mid(sql,2)`
//  [n_cst_thread_trans.sru:L309]. Two things follow, and both are easy to get wrong:
//      1. It lives in the QUERY path, not the command path. `of_Exec` [n_cst_thread_trans.sru:L219]
//         has no such test, so a command statement beginning with `@` is passed to the provider
//         verbatim and is not treated specially anywhere.
//      2. Reading it as "a SQL prefix to strip" would synthesize a nonsensical statement. It is a
//         DATA-OBJECT SELECTOR, and it is documented here rather than implemented here.
//
// -------------------------------------------------------------------------------------------------
//  THE ARITY CEILINGS ARE RECORDED, NOT ENFORCED  (AAP §0.2.1.4 - constraints C-D, C-K)
// -------------------------------------------------------------------------------------------------
//  Three ceilings exist in the oracle, and all three are artefacts of PowerScript being unable to
//  forward an arbitrary-length argument list rather than of any behavioural rule:
//      11  n_sqlite.sru:L33-L44                       `Exec(cmd)` .. `Exec(cmd, arg1 .. arg11)`
//      20  n_cst_thread_task_sqlbase.sru:L690-L692    the datastore retrieve unroll, `case 20`
//       8  n_cst_thread_trans.sru:L465-L466           the transaction retrieve unroll, `case 8`
//  Past each ceiling the oracle falls back to n_scriptinvoker, a dynamic-invocation object that
//  belongs to the DEFERRED ScriptBridge service. C# has native variadic support, so the workaround
//  has no analogue to port and Persistence acquires NO ScriptBridge coupling (constraint C-D). The
//  ceilings are therefore legacy LIMITS the .NET contract may exceed without behavioural regression:
//  they are recorded as named constants below so a reader can find them, and nothing enforces them.
//
// -------------------------------------------------------------------------------------------------
//  `SQLCode = 100` READS AS SUCCESS  (constraints C-B, C-K)
// -------------------------------------------------------------------------------------------------
//  `if SQLCode <> 0 and SQLCode <> 100 then return RetCode.E_DB_ERROR`
//  [n_cst_thread_trans.sru:L233-L235]. One hundred is "not found" and is NOT a failure.
//  THAT TEST IS ALREADY DISCHARGED UPSTREAM, in PooledTransaction.Exec, so this file must test the
//  RETURNED CODE with the return-code predicates and must never re-derive success from SQLCode -
//  a second, simpler `!= 0` test here would silently turn every empty result into a database error.
//
// -------------------------------------------------------------------------------------------------
//  SECRETS AND REDACTION  (constraint C-F)
// -------------------------------------------------------------------------------------------------
//  The failure epilogue raises the database-error event with `sSQL` - the FULLY BOUND, FULLY
//  INTERPOLATED statement - as the `sqlsyntax` member [:L110], and the legacy logger performs no
//  redaction at all. In this port:
//      * the payload is built by Errors/DbErrorData.FromStatement and reaches the wire ONLY through
//        `ToDbError()`, which applies SqlRedactor.Instance unconditionally and takes no policy
//        argument a call site could weaken [Errors/SqlRedactor.cs];
//      * every statement this file writes to a log record goes through SqlRedactor.Instance first;
//      * TransactionData.LogPass is WRITE-ONLY and is never read, echoed or logged here;
//      * no credential literal of any kind appears in this file. The repository-wide sweep found
//        eight in-source secret sites, all in test, demo or legacy-browser regions that map to
//        DEFERRED services (AAP §0.6.6). The posture is never replicate, document, rotate - and the
//        read-only rule (C-C) forbids editing those files, so nothing here touches them.
//
// -------------------------------------------------------------------------------------------------
//  CLOCK AND TESTABILITY  (constraint C-H)
// -------------------------------------------------------------------------------------------------
//  `SqlTaskBase.Clock` is the single injected clock. Nothing in this file reads DateTime.UtcNow,
//  DateTime.Now, Environment.TickCount or a Stopwatch. Every collaborator - the substrate, the pool,
//  the pooled transaction - is an abstraction, so the whole of this file is exercisable with NO
//  DATABASE and NO REAL THREAD; the project's InternalsVisibleTo("PowerFramework.Persistence.Tests")
//  is what makes the internal surface reachable from the suite the coverage gate is measured on.
//
// -------------------------------------------------------------------------------------------------
//  ACCESSIBILITY
// -------------------------------------------------------------------------------------------------
//  The type is internal because SqlTaskBase, ISqlTaskHost, TransactionPool, IPooledTransaction and
//  DbErrorData all are, so a public member mentioning any of them would be a CS0051/CS0053
//  inconsistent-accessibility error. It is sealed because the oracle object is a LEAF - no object
//  anywhere in ws_objects/** derives from n_cst_thread_task_sqlcommand.
//
//  Rules position. No user rules were provided for this project: review_rules returns exactly one
//  line, "No user rules provided." The binding constraints in their place are C-A .. C-L
//  (AAP §0.7.3) and the enterprise baseline (AAP §0.7.2), each cited above and in the body where it
//  bites. Nothing in this file exists because of an invented or inferred rule.
// =================================================================================================

using Microsoft.Extensions.Logging;
using PowerFramework.Contracts.Common.V1;
using PowerFramework.Contracts.Persistence.V1;
using PowerFramework.Persistence.Errors;
using PowerFramework.Persistence.Transactions;

// The generated contract carries a RetCode MESSAGE and Shared.Kernel carries the RetCode CONSTANT
// CATALOGUE. Both are in scope here, so the alias states which one every bare `RetCode` means -
// the same alias every other file in this project uses.
using Predicates = PowerFramework.Shared.Kernel.Predicates;
using RetCode = PowerFramework.Shared.Kernel.RetCode;

namespace PowerFramework.Persistence.Tasks;

/// <summary>
/// The worker-thread task that executes one SQL command under a tri-valued auto-commit mode - the
/// managed port of <c>n_cst_thread_task_sqlcommand</c>
/// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlcommand.sru</c>, 116 lines].
/// </summary>
/// <remarks>
/// <para>
/// <b>Worker-thread affinity is a contract.</b> The oracle annotates itself
/// <c>[运行在子线程]</c> [<c>:L2</c>], and its caller-side counterpart
/// <c>n_cst_threading_task_sqlcommand</c> annotates itself <c>[运行在当前线程]</c>
/// [<c>n_cst_threading_task_sqlcommand.sru:L2</c>]. The two are separate managed types and must stay
/// separate; see the file banner for the full accounting of the proxy's members.
/// </para>
/// <para>
/// <b>This type drives contract C-07 <c>persistence.v1.CommandService</c>.</b> Its four mutators and
/// its execution entry point correspond to the service's <c>Reset</c>, <c>SetAutoCommit</c>,
/// <c>SetSql</c> and <c>Exec</c> methods
/// [<c>shared/PowerFramework.Contracts/Proto/persistence.v1.proto:L1943-L1962</c>]. It registers no
/// route and opens no listener of its own (constraint C-G): it is reachable only through those
/// authorized gRPC methods.
/// </para>
/// <para>
/// <b>Usage.</b> Install the connection descriptor through the inherited
/// <c>SetTransData</c>, add any parameters through the inherited <c>AddParam</c>, then set the
/// statement and the mode and call <see cref="OnDoTask"/>:
/// </para>
/// <code>
/// task.SetSql("UPDATE COMPANY SET SALARY = :salary WHERE ID = :id");
/// task.SetAutoCommit(AutoCommitMode.AcOn);
/// long result = task.OnDoTask();
/// if (Predicates.IsSucceeded(result)) { /* committed, because the mode was AC_ON */ }
/// </code>
/// </remarks>
internal sealed class SqlCommandTask : SqlTaskBase
{
    // ---------------------------------------------------------------------------------------------
    //  Verbatim legacy message text. Preserved character for character, Chinese punctuation and
    //  trailing exclamation mark included, because these strings are observable: they travel in the
    //  general-error event, land in log records, and appear in characterization recordings, where a
    //  reworded or translated form would silently invalidate every stored comparison.
    //
    //  NOTE, and it is a preserved inconsistency rather than an oversight: neither string routes
    //  through localization. The equivalent messages in the DataWindow service layer DO route
    //  through I18N, and the column-expression engine's 28 messages do NOT. That inconsistency is
    //  legacy behaviour and is reproduced rather than harmonized (constraint C-B, AAP §0.2.1.3).
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The diagnostic raised when the statement is empty - <c>"无效的SQL!"</c>, verbatim
    /// [<c>:L66</c>].
    /// </summary>
    internal const string InvalidSqlMessage = "无效的SQL!";

    /// <summary>
    /// The diagnostic raised when parameter binding fails - <c>"SQL参数绑定失败!"</c>, verbatim
    /// [<c>:L83</c>].
    /// </summary>
    internal const string BindArgumentFailureMessage = "SQL参数绑定失败!";

    // ---------------------------------------------------------------------------------------------
    //  Documented legacy facts. Each is a value a reader will look for; none is enforced here.
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The character that, in the transaction's QUERY path, marks the rest of the string as a
    /// DataWindow object name rather than as SQL -
    /// <c>if Left(sql,1) = "@" then ds.DataObject = Mid(sql,2)</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:L309</c>].
    /// </summary>
    /// <remarks>
    /// <b>Documented, deliberately not implemented here (constraint C-K).</b> The command path
    /// <c>of_Exec</c> [<c>n_cst_thread_trans.sru:L219</c>] carries no such test, so a command
    /// statement beginning with this character is passed to the provider verbatim. The constant
    /// exists so that a reader surfacing the mode for C-07 can find the correct semantic - a
    /// DATA-OBJECT SELECTOR - instead of guessing that it is a prefix to strip.
    /// </remarks>
    internal const char DataWindowObjectPrefix = '@';

    /// <summary>
    /// The provider code that means "not found" and READS AS SUCCESS -
    /// <c>if SQLCode &lt;&gt; 0 and SQLCode &lt;&gt; 100 then return RetCode.E_DB_ERROR</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:L233-L235</c>].
    /// </summary>
    /// <remarks>
    /// <b>The test is discharged upstream and must NOT be repeated here (constraint C-B).</b>
    /// <c>PooledTransaction.Exec</c> reproduces it, so by the time a code reaches this file's
    /// epilogues a hundred has already been folded into <see cref="RetCode.OK"/>. This file therefore
    /// branches on the RETURNED CODE through
    /// <see cref="Predicates.IsSucceeded(long?)"/> and never re-derives success from a provider code.
    /// The constant is declared so the reading is discoverable from the task that depends on it.
    /// </remarks>
    internal const long SqlCodeNotFound = 100L;

    /// <summary>
    /// The legacy positional-argument ceiling of the SQLite binding's command entry point -
    /// <c>Exec(readonly string cmd, any arg1 .. any arg11)</c>
    /// [<c>ws_objects/pfw.utility.sqlite.pbl.src/n_sqlite.sru:L33-L44</c>].
    /// </summary>
    /// <remarks>
    /// <b>Recorded, NOT enforced (AAP §0.2.1.4, constraint C-D).</b> Eleven is the top of a
    /// hand-written overload ladder, and the ladder exists only because PowerScript cannot forward an
    /// arbitrary-length argument list. C# can, so the ceiling is a legacy limit this port may exceed
    /// without behavioural regression, and nothing in this file rejects a twelfth parameter.
    /// </remarks>
    internal const int LegacySqliteExecArgumentCeiling = 11;

    /// <summary>
    /// The legacy positional-argument ceiling of the base task's datastore retrieve unroll -
    /// <c>case 20</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru:L690-L692</c>].
    /// </summary>
    /// <remarks>
    /// Recorded for the same reason as <see cref="LegacySqliteExecArgumentCeiling"/>: past it the
    /// oracle falls back to <c>n_scriptinvoker</c>, which belongs to the deferred ScriptBridge
    /// service, so the fallback has no analogue to port and Persistence acquires no coupling to it.
    /// </remarks>
    internal const int LegacyDataStoreRetrieveArgumentCeiling = 20;

    /// <summary>
    /// The legacy positional-argument ceiling of the transaction object's retrieve unroll -
    /// <c>case 8</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:L465-L466</c>].
    /// </summary>
    /// <remarks>
    /// Recorded for the same reason as <see cref="LegacySqliteExecArgumentCeiling"/>.
    /// </remarks>
    internal const int LegacyTransactionRetrieveArgumentCeiling = 8;

    // ---------------------------------------------------------------------------------------------
    //  Instance state, mirroring the legacy private block [:L20-L22] exactly and adding nothing to
    //  it beyond the running flag explained on IsRunning.
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Diagnostics sink for this task's own records.
    /// </summary>
    /// <remarks>
    /// Held in addition to the one handed to the base because the base's is private. Constraint C-F
    /// governs everything written through it: no statement reaches a record unredacted, and the
    /// connection descriptor's write-only password is never touched.
    /// </remarks>
    private readonly ILogger _logger;

    /// <summary>
    /// Mirrors <c>long _nAutoCommit = AC_OFF</c> [<c>:L21</c>].
    /// </summary>
    private AutoCommitMode _autoCommit = AutoCommitMode.AcOff;

    /// <summary>
    /// Mirrors <c>string _sSQL</c> [<c>:L22</c>], which PowerBuilder initialises to the empty string.
    /// </summary>
    /// <remarks>
    /// Nullable, and that is faithful rather than defensive - see <see cref="SetSql"/> for the
    /// three-valued comparison that lets a null statement be stored.
    /// </remarks>
    private string? _sql = string.Empty;

    /// <summary>
    /// The narrowed equivalent of the substrate's <c>#Running</c> flag; see
    /// <see cref="IsRunning"/> for why it is owned here.
    /// </summary>
    private bool _running;

    /// <summary>
    /// Creates the task.
    /// </summary>
    /// <param name="host">The threading substrate this task runs on.</param>
    /// <param name="transactionPool">The shared pooled-transaction store.</param>
    /// <param name="dataStoreFactory">
    /// Creates affinity-appropriate result carriers. Required by the base even though a command
    /// produces no result set: the oracle's own inheritance puts the carrier machinery on the base
    /// [<c>n_cst_thread_task_sqlbase.sru</c>], and a command task inherits it unused rather than
    /// opting out of it.
    /// </param>
    /// <param name="hookActivator">Turns a caller-supplied hook class name into a hook, safely.</param>
    /// <param name="timeProvider">
    /// The service's single clock seam. <b>Constraint C-H: the only clock this file may read, and it
    /// reads it only through the base's <c>Clock</c>.</b>
    /// </param>
    /// <param name="logger">
    /// Diagnostics sink, forwarded to the base and retained for this task's own records.
    /// </param>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// The oracle declares <c>global n_cst_thread_task_sqlcommand n_cst_thread_task_sqlcommand</c>
    /// [<c>:L10</c>] - a global auto-instance shadowing its own type name. <b>Constraint C-J: there is
    /// no static, no singleton and no ambient instance here.</b> The type is constructed per use and
    /// every collaborator arrives through this constructor, which is also what makes the whole file
    /// testable with no database and no real thread.
    /// </remarks>
    internal SqlCommandTask(
        ISqlTaskHost host,
        TransactionPool transactionPool,
        ISqlDataStoreFactory dataStoreFactory,
        ISqlRetrievalHookActivator hookActivator,
        TimeProvider timeProvider,
        ILogger<SqlCommandTask> logger)
        : base(host, transactionPool, dataStoreFactory, hookActivator, timeProvider, logger)
    {
        // The base validates every argument, `logger` included, and throws before this line runs, so
        // the assignment cannot capture a null. Re-validating here would be dead code.
        _logger = logger;
    }

    #region Read-only observation seams - they report state and change none

    /// <summary>
    /// The currently installed auto-commit mode - the direct read of <c>_nAutoCommit</c>
    /// [<c>:L21</c>].
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A testability seam, and a narrow one on purpose (constraint C-H).</b> The oracle's field is
    /// private with no accessor, but three behaviours turn on its value and none of them is observable
    /// from outside without it: the default being <see cref="AutoCommitMode.AcOff"/> [<c>:L21</c>],
    /// <see cref="Reset"/> restoring that default [<c>:L34</c>], and an unmatched value surviving
    /// <see cref="SetAutoCommit"/> unvalidated [<c>:L40-L43</c>]. It follows the same shape as the
    /// base's own <c>HasTransactionReference</c> and <c>HasCommitSignal</c> seams.
    /// </para>
    /// <para>
    /// The value may be OUTSIDE the three declared enumerators. A proto3 enum is an open
    /// <see cref="int"/> in C#, and <see cref="SetAutoCommit"/> validates nothing, so
    /// <c>(AutoCommitMode)7</c> is a value this property can legitimately return.
    /// </para>
    /// </remarks>
    internal AutoCommitMode AutoCommitSetting => _autoCommit;

    /// <summary>
    /// The currently installed statement - the direct read of <c>_sSQL</c> [<c>:L22</c>].
    /// </summary>
    /// <remarks>
    /// <b>A testability seam (constraint C-H)</b>, needed because <see cref="Reset"/> clearing the
    /// statement [<c>:L35</c>] is a claim about ABSENCE, and a claim about absence cannot be verified
    /// through <see cref="OnDoTask"/> - which answers <see cref="RetCode.E_INVALID_SQL"/> both for a
    /// statement that was never set and for one that was cleared. Nullable because
    /// <see cref="SetSql"/> stores <see langword="null"/> when it is given one.
    /// </remarks>
    internal string? Sql => _sql;

    /// <summary>
    /// Whether this task is currently inside its own task body - the narrowed equivalent of the
    /// substrate's <c>#Running</c> flag
    /// [<c>ws_objects/pfw.thread.pbl.src/n_cst_thread_task.sru:L79</c>].
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why the flag is owned HERE and not on the substrate seam.</b> <c>#Running</c> is an instance
    /// attribute OF THE TASK, not of the thread - it is declared
    /// <c>PrivateWrite Boolean #Running = false</c> on <c>n_cst_thread_task</c> [<c>:L79</c>], set true
    /// in that object's <c>onstart</c> [<c>:L164</c>] and false in its <c>onstop</c> [<c>:L177</c>].
    /// <c>Tasks/SqlTaskBase.cs</c> deliberately does not model it on <c>ISqlTaskHost</c>, and says why:
    /// every guard written against it IN THE BASE OBJECT is commented out in the oracle
    /// [<c>n_cst_thread_task_sqlbase.sru:L113, :L253, :L343, :L361</c>] and is carried across inert, so
    /// declaring an unread member there would invite exactly the revival constraint C-B forbids.
    /// </para>
    /// <para>
    /// <b>This object's guard is different: it is LIVE.</b> <c>of_reset</c> opens with
    /// <c>if #Running then return RetCode.E_BUSY</c> [<c>:L32</c>], uncommented, and the sibling query
    /// and update tasks do the same [<c>n_cst_thread_task_sqlquery.sru:L237</c>,
    /// <c>n_cst_thread_task_sqlupdate.sru:L60</c>]. So the flag genuinely has to exist for this type,
    /// and the faithful home for it is the type that declares it in the oracle - this one.
    /// </para>
    /// <para>
    /// <b>The narrowing, stated rather than hidden.</b> The oracle's flag is true across the whole
    /// <c>onstart</c>-to-<c>onstop</c> span, which brackets <c>onprepare</c> as well as the task body;
    /// this one is true for the duration of <see cref="OnDoTask"/> alone, because the managed substrate
    /// models <c>OnPrepare</c> and <c>OnUninit</c> but has no <c>onstart</c>/<c>onstop</c> pair to hang
    /// a wider span on. The uncovered window is not reachable through the sanctioned path: the
    /// caller-side proxy refuses a reset while busy BEFORE delegating here
    /// [<c>n_cst_threading_task_sqlbase.sru:L53-L57</c>], so this guard is the second of two and is
    /// defence in depth rather than the only line.
    /// </para>
    /// </remarks>
    internal bool IsRunning => _running;

    #endregion

    #region Mutators - the port of of_setautocommit and of_setsql

    /// <summary>
    /// Installs the auto-commit mode - the port of <c>of_setautocommit</c> [<c>:L40-L43</c>].
    /// </summary>
    /// <param name="autoCommit">
    /// The mode. <b>Any value is accepted, including one outside the three declared enumerators</b> -
    /// see the remarks.
    /// </param>
    /// <returns><see cref="RetCode.OK"/>, always. The oracle has no failing arm here.</returns>
    /// <remarks>
    /// <para>
    /// <b>⚠ THIS METHOD VALIDATES NOTHING, AND THAT IS PRESERVED ON PURPOSE (constraint C-B).</b> The
    /// oracle's body is two statements - assign, return OK - with no range check, no membership test
    /// and no diagnostic [<c>:L40-L43</c>]. A value outside <c>{0, 1, 2}</c> is stored happily and then
    /// <b>SILENTLY BEHAVES AS <see cref="AutoCommitMode.AcOff"/></b>, because none of the arms in
    /// <see cref="OnDoTask"/>'s epilogues matches it: the success epilogue is an
    /// <c>if</c>/<c>elseif</c> with NO <c>else</c> [<c>:L94-L103</c>] so nothing is committed, and the
    /// failure epilogue's <c>else</c> arm rolls back [<c>:L107-L109</c>] exactly as it would for
    /// <c>AC_OFF</c>. Adding an argument check here would be correcting a legacy defect, which the
    /// refactor forbids.
    /// </para>
    /// <para>
    /// <b>If C-07's generated enum constrains the value, that is the CONTRACT's doing, not this
    /// file's.</b> <c>SetCommandAutoCommitRequest.autocommit</c> is typed
    /// <c>AutoCommitMode</c> [<c>persistence.v1.proto:L1761-L1764</c>], but a proto3 enum field is
    /// OPEN - an unrecognised numeric value is carried through rather than rejected - so the wire
    /// cannot be relied on to filter, and the in-process path must tolerate an unmatched value
    /// regardless. It does.
    /// </para>
    /// <para>
    /// <b>Why the parameter is the contract enum rather than the oracle's <c>long</c>.</b> The oracle
    /// signature is <c>readonly long autocommit</c> [<c>:L40</c>]. Nothing is lost by naming the
    /// generated type instead: it is an open <see cref="int"/>-backed enum, so
    /// <c>SetAutoCommit((AutoCommitMode)7)</c> reproduces the unvalidated <c>long</c> path exactly,
    /// while the ordinary call site gains a type that cannot be confused with an unrelated number. The
    /// triple's declaration site had to move anyway - see the .editorconfig note in the file banner.
    /// </para>
    /// <para>
    /// <b>The caller-side busy guard is NOT duplicated here.</b> <c>of_setautocommit</c> on the proxy
    /// opens with <c>if of_IsBusy() then return RetCode.E_BUSY</c>
    /// [<c>n_cst_threading_task_sqlcommand.sru:L43</c>]; the worker's own setter has no such guard, and
    /// this port reproduces that asymmetry rather than tidying it.
    /// </para>
    /// </remarks>
    internal long SetAutoCommit(AutoCommitMode autoCommit)
    {
        // [:L40] `_nAutoCommit = autoCommit` - the whole body, verbatim. No validation, by design.
        _autoCommit = autoCommit;

        // [:L42]
        return RetCode.OK;
    }

    /// <summary>
    /// Installs the statement - the port of <c>of_setsql</c> [<c>:L45-L50</c>].
    /// </summary>
    /// <param name="sql">
    /// The statement. The EMPTY STRING is rejected; <see langword="null"/> is not - see the remarks.
    /// </param>
    /// <returns>
    /// <see cref="RetCode.E_INVALID_SQL"/> when <paramref name="sql"/> is the empty string, otherwise
    /// <see cref="RetCode.OK"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>The guard is a THREE-VALUED comparison, and reproducing it is the whole subtlety here
    /// (constraint C-B).</b> The oracle reads <c>if sql = "" then return RetCode.E_INVALID_SQL</c>
    /// [<c>:L45</c>]. In PowerBuilder, comparing a NULL string with <c>=</c> yields NULL, and a NULL
    /// condition takes the FALSE branch - so a null statement does NOT trip this guard: it is stored,
    /// and the method answers <see cref="RetCode.OK"/>. Writing
    /// <c>string.IsNullOrEmpty</c> here would fold null into empty and change the observable return
    /// code, which is why the test below is an equality test against the empty string alone.
    /// </para>
    /// <para>
    /// <b>Where the null then lands.</b> <see cref="OnDoTask"/>'s own emptiness guard is the same
    /// comparison [<c>:L65</c>] and falls through for the same reason, so the null reaches the
    /// transaction's <c>of_Exec</c>, whose first line tests
    /// <c>if sqlCmd = "" or IsNull(sqlCmd)</c> and answers
    /// <see cref="RetCode.E_INVALID_ARGUMENT"/>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:L219</c>]. A null statement is
    /// therefore a DATABASE-ERROR-shaped failure, not an invalid-SQL-shaped one, and that distinction
    /// survives this port.
    /// </para>
    /// <para>
    /// <b>Unreachable from C-07, and kept anyway.</b> <c>SetCommandSqlRequest.sql</c> is a proto3
    /// <c>string</c> field [<c>persistence.v1.proto:L1771-L1774</c>], and a proto3 scalar string is
    /// never null - an unset field arrives as the empty string. So only an in-process caller can reach
    /// the null path. It is preserved because the parity model characterizes the in-process surface,
    /// not just the wire surface.
    /// </para>
    /// <para>
    /// <b>Neither the proxy's busy guard nor its debug assertion belongs here.</b>
    /// <c>if of_IsBusy() then return RetCode.E_BUSY</c> and
    /// <c>#IF DEFINED DEBUG THEN Assert(Len(sql) &gt; 0,"Len(sql) &lt;= 0") #END IF</c> are the
    /// caller-side object's [<c>n_cst_threading_task_sqlcommand.sru:L34, :L36-L38</c>] and are ported
    /// in <c>Tasks/TaskProxies/</c>.
    /// </para>
    /// </remarks>
    internal long SetSql(string? sql)
    {
        // [:L45] `if sql = "" then return RetCode.E_INVALID_SQL`. Ordinal equality against the empty
        // string ONLY - a pattern match rather than string.IsNullOrEmpty, so that null falls through
        // exactly as PowerBuilder's NULL-propagating comparison makes it fall through.
        if (sql is "")
        {
            return RetCode.E_INVALID_SQL;
        }

        // [:L47]
        _sql = sql;

        // [:L49]
        return RetCode.OK;
    }

    #endregion

    #region Reset - the port of of_reset

    /// <summary>
    /// Returns the task to its post-construction state - the port of <c>of_reset</c>
    /// [<c>:L32-L38</c>].
    /// </summary>
    /// <returns>
    /// <see cref="RetCode.E_BUSY"/> while the task is running, otherwise whatever
    /// <see cref="SqlTaskBase.Reset"/> answers - which is <see cref="RetCode.OK"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>The order of the four steps is contract, not convenience.</b> The busy guard comes FIRST and
    /// returns before anything is touched [<c>:L32</c>], so a refused reset leaves the mode and the
    /// statement exactly as they were - a reset that cleared half its state and then reported
    /// <see cref="RetCode.E_BUSY"/> would be a behavioural change. The ancestor comes LAST
    /// [<c>:L37</c>], and its return value is this method's return value.
    /// </para>
    /// <para>
    /// <b>Both halves of the state restoration are observable and both are asserted by C-07.</b> The
    /// mode goes back to <see cref="AutoCommitMode.AcOff"/> [<c>:L34</c>] and the statement is cleared
    /// to the EMPTY STRING rather than to null [<c>:L35</c>]. The published contract states the same
    /// thing - a caller that reset a task and then ran it without setting the mode again gets
    /// <c>AC_OFF</c> [<c>persistence.v1.proto:L1755-L1757</c>] - so drifting on either would break a
    /// documented promise.
    /// </para>
    /// <para>
    /// <b>What the ancestor contributes.</b> <see cref="SqlTaskBase.Reset"/> resets the commit signal if
    /// one was created and clears the parameter collection
    /// [<c>n_cst_thread_task_sqlbase.sru:L242-L248</c>]. Note that the base's own reset has NO busy
    /// guard - the oracle's is commented out at <c>:L253</c> and is carried across inert - so this
    /// method's guard is the only live one on the worker side.
    /// </para>
    /// </remarks>
    internal override long Reset()
    {
        // [:L32] `if #Running then return RetCode.E_BUSY` - LIVE in this object, unlike the base's.
        // See IsRunning for why the flag is owned here and what its span is.
        if (_running)
        {
            return RetCode.E_BUSY;
        }

        // [:L34] back to the declared default, which is AC_OFF and not "leave it alone".
        _autoCommit = AutoCommitMode.AcOff;

        // [:L35] `_sSQL = ""` - the EMPTY STRING, deliberately not null. SetSql can store null, so
        // reset is the one place the field is guaranteed to become empty rather than absent.
        _sql = string.Empty;

        // [:L37] `return super::of_Reset()` - the ancestor runs last and supplies the answer.
        return base.Reset();
    }

    #endregion

    #region The execution sequence - the port of event ondotask

    /// <summary>
    /// Executes the installed statement under the installed auto-commit mode - the port of
    /// <c>event ondotask</c> [<c>:L60-L115</c>].
    /// </summary>
    /// <returns>
    /// The statement's own result code once the epilogues have run: one of
    /// <see cref="RetCode.E_INVALID_SQL"/>, <see cref="RetCode.E_INVALID_TRANSACTION"/>,
    /// <see cref="RetCode.CANCELLED"/>, <see cref="RetCode.E_SQL_BIND_ARG_FAILED"/>, or whatever the
    /// pooled transaction's <c>Exec</c> answered - possibly REPLACED by a commit failure on the
    /// <see cref="AutoCommitMode.AcOn"/> path [<c>:L99</c>].
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>The ten steps run in the oracle's order and the order is load-bearing.</b> An empty statement
    /// short-circuits BEFORE the transaction is acquired [<c>:L65</c> precedes <c>:L70</c>], so a task
    /// with no statement never takes a reference from the pool; and cancellation short-circuits BEFORE
    /// binding [<c>:L76</c> precedes <c>:L81</c>], so a cancelled task never interpolates a literal.
    /// Reordering either would change what the system does with the pool and with caller data.
    /// </para>
    /// <para>
    /// <b>⚠ THE AC_NATIVE INVERSION.</b> The mode whose source comment reads 不开启事务 - "does NOT open
    /// a transaction" - is implemented by turning PowerBuilder's own <c>AutoCommit</c> flag <b>ON</b>
    /// [<c>:L88-L90</c>]. That is counter-intuitive and it is correct: with the runtime's
    /// <c>AutoCommit</c> true the runtime does not open an explicit transaction, so each statement
    /// stands alone and is durable the moment the provider accepts it. A reader who assumes
    /// <c>AC_NATIVE</c> means "leave the flag alone" produces the OPPOSITE transactional behaviour.
    /// The flag is restored to false afterwards on BOTH paths - success [<c>:L96</c>] and failure
    /// [<c>:L106</c>].
    /// </para>
    /// <para>
    /// <b>⚠ THE AC_OFF SUCCESS ARM IS A DELIBERATE NO-OP.</b> The success epilogue is an
    /// <c>if</c>/<c>elseif</c> with no <c>else</c> [<c>:L94-L103</c>]: on
    /// <see cref="AutoCommitMode.AcOff"/> nothing is committed, because the CALLER owns the
    /// transaction and its session governs when the work becomes durable. An "obvious" commit here
    /// would change transactional semantics for every caller batching statements under one session.
    /// </para>
    /// <para>
    /// <b>⚠ THE AC_NATIVE FAILURE ARM PERFORMS NO ROLLBACK OF ITS OWN, BUT THE PATH STILL ROLLS
    /// BACK.</b> This is the finding most likely to be got wrong in either direction. The arm itself
    /// only restores the flag [<c>:L105-L106</c>] and skips the <c>else</c> arm's
    /// <c>transObject.of_Rollback()</c> [<c>:L108</c>], because there was no explicit transaction to
    /// roll back. But the general-error event fired immediately afterwards [<c>:L111</c>] is overridden
    /// on the base to ROLL BACK FIRST and only then chain to its ancestor
    /// [<c>n_cst_thread_task_sqlbase.sru:L730-L734</c>]. So the observable rollback counts on the
    /// failure path are: <see cref="AutoCommitMode.AcNative"/> exactly ONE, every other mode exactly
    /// TWO. Suppressing the second to make the arm "consistent" would be a behavioural change, and so
    /// would adding a rollback to the native arm.
    /// </para>
    /// <para>
    /// <b>Where the committed notification comes from.</b> On
    /// <see cref="AutoCommitMode.AcNative"/> this method raises it directly [<c>:L97</c>] because the
    /// runtime has already made the work durable and there is nothing left to commit. On
    /// <see cref="AutoCommitMode.AcOn"/> it is raised by the base's commit helper, and only if the
    /// commit succeeded [<c>n_cst_thread_task_sqlbase.sru:L235-L237</c>]. On
    /// <see cref="AutoCommitMode.AcOff"/> it is never raised. Those three readings are exactly what
    /// C-07's <c>ExecResponse.committed</c> field documents
    /// [<c>persistence.v1.proto:L1887-L1903</c>].
    /// </para>
    /// <para>
    /// <b>The ancestor task handler.</b> The oracle inlines <c>call super::ondotask</c> ahead of even
    /// its local declarations [<c>:L60</c>], and that call is an OBSERVABLE NO-OP: the event is
    /// DECLARED on the substrate [<c>ws_objects/pfw.thread.pbl.src/n_cst_thread_task.sru:L19</c>] and
    /// implemented NOWHERE above this object - the only three implementations in the whole repository
    /// are the three concrete SQL tasks themselves
    /// [<c>n_cst_thread_task_sqlquery.sru:L500</c>, <c>:L60</c> here,
    /// <c>n_cst_thread_task_sqlupdate.sru:L284</c>], and the intermediate
    /// <c>n_cst_thread_task_sqlbase</c> supplies none. <c>Tasks/SqlTaskBase.cs</c> correspondingly
    /// declares no <c>OnDoTask</c>, so nothing runs before this body and the faithful port of the
    /// ancestor call is this paragraph plus the note at the top of the body - not an invented base
    /// member, which would add a hook the oracle does not have.
    /// </para>
    /// </remarks>
    internal long OnDoTask()
    {
        // -----------------------------------------------------------------------------------------
        // STEP 1 - [:L60] `call super::ondotask`. Reproduced as a no-op, with the evidence in the
        // remarks above: the ancestor declares the event and implements no body for it, so the call
        // returns null and changes nothing. Nothing may be inserted ahead of this comment, because
        // "the ancestor runs first" is the ordering the oracle states even when it is vacuous.
        // -----------------------------------------------------------------------------------------

        // [:L61-L63] the four locals, in the oracle's own declaration order. `sql` starts as the empty
        // string because PowerBuilder initialises `string sSQL` that way, and it is only overwritten at
        // the snapshot point below.
        long rtCode;
        string sql = string.Empty;
        IPooledTransaction? transaction = null;
        DbErrorData dbErrorData = DbErrorData.Empty;

        // The running flag is raised for the whole body and lowered in the finally, so a reset racing
        // this call is refused. See IsRunning for the span this covers and the span it does not.
        _running = true;

        try
        {
            // -------------------------------------------------------------------------------------
            // STEP 2 - [:L65-L68] the emptiness guard, BEFORE the transaction is acquired.
            // -------------------------------------------------------------------------------------
            // The same three-valued comparison as SetSql's: only the empty string trips it, so a null
            // statement falls through here exactly as it does in the oracle and is answered by the
            // transaction instead. See SetSql for the full reasoning.
            if (_sql is "")
            {
                // [:L66] the general-error event carries the verbatim Chinese diagnostic. Its return
                // value is discarded, as the oracle discards it.
                _ = OnError(RetCode.E_INVALID_SQL, InvalidSqlMessage);

                // [:L67]
                return RetCode.E_INVALID_SQL;
            }

            // -------------------------------------------------------------------------------------
            // STEP 3 - [:L70-L74] acquire the pooled transaction.
            // -------------------------------------------------------------------------------------
            // IsFailed, not `<> RetCode.OK`: the tri-state algebra is preserved everywhere, and a
            // prevented acquisition reads as a success rather than as a failure
            // [ws_objects/pfw.shared.pbl.src/issucceeded.srf, isfailed.srf].
            if (Predicates.IsFailed(GetTransObject(ref transaction, ref dbErrorData)))
            {
                // [:L71] the database-error event, and note all three of its unusual arguments: the
                // statement is the EMPTY STRING because an acquisition failure has no statement, the
                // buffer is Primary, and the row is 0. The base's handler builds the DbErrorData
                // payload, forwards it to the caller-side proxy and logs it REDACTED (constraint C-F).
                _ = OnDbError(
                    dbErrorData.SqlDbCode,
                    dbErrorData.SqlErrText,
                    string.Empty,
                    DwBuffer.Primary,
                    0L);

                // [:L72] then the general error, carrying the acquired payload's text rather than a
                // synthesized one.
                _ = OnError(RetCode.E_INVALID_TRANSACTION, dbErrorData.SqlErrText);

                // [:L73]
                return RetCode.E_INVALID_TRANSACTION;
            }

            // A NARROWING WITH A DEFINED ERROR, and it is unreachable by the pool's own contract.
            // TransactionPool.Get only answers a success code with a borrowed instance in hand, and
            // GetTransObject's reuse arm only succeeds on an instance it has just validated, so a
            // non-failed acquisition always leaves a transaction here. The oracle has no equivalent
            // guard - PowerBuilder would dereference a null object and fault - so this arm converts an
            // unreachable fault into the SAME code the reachable acquisition failure already returns,
            // rather than letting a NullReferenceException escape as an opaque transport error. That is
            // the sanctioned direction: narrow with a defined error, never widen with a guess
            // (AAP §0.1.5). It also gives the compiler the definite non-null it needs below.
            if (transaction is null)
            {
                _ = OnError(RetCode.E_INVALID_TRANSACTION, dbErrorData.SqlErrText);
                return RetCode.E_INVALID_TRANSACTION;
            }

            // -------------------------------------------------------------------------------------
            // STEP 4 - [:L76-L78] cancellation, BEFORE any parameter is interpolated.
            // -------------------------------------------------------------------------------------
            if (IsCancelled)
            {
                // [:L77] CANCELLED is neither succeeded nor failed under the tri-state algebra, and no
                // event is raised here - the oracle returns silently.
                return RetCode.CANCELLED;
            }

            // -------------------------------------------------------------------------------------
            // STEP 5 - [:L80-L86] snapshot the statement, then bind if there is anything to bind.
            // -------------------------------------------------------------------------------------
            // [:L80] `sSQL = _sSQL`. The snapshot matters: binding rewrites the copy IN PLACE, and the
            // installed statement must survive intact so the task can be re-run.
            //
            // The null projection is observationally free. A PowerBuilder NULL would propagate through
            // the binding helper and into of_Exec, whose first line answers E_INVALID_ARGUMENT for
            // `sqlCmd = ""` and for `IsNull(sqlCmd)` ALIKE
            // [n_cst_thread_trans.sru:L219] - so an empty string reaches the same outcome at the only
            // place the value is consumed, while keeping the local non-nullable for the `ref string`
            // binding contract. Null is unreachable from C-07 in any case: a proto3 string field never
            // yields it.
            sql = _sql ?? string.Empty;

            // [:L81] `if of_HasParams() then` - binding is skipped entirely when the collection is
            // empty, so a parameterless statement is never scanned or rewritten.
            if (HasParams())
            {
                // [:L82] the dialect is resolved from the transaction, not configured here. It selects
                // LITERAL FORMATS for the interpolation and NOTHING ELSE - it never selects a
                // connection (constraint C-E), and its two arms are the oracle's only two:
                // `if Pos(Upper(DBMS),"ORACLE") > 0 then DBT_ORACLE else DBT_MSSQL`
                // [n_cst_thread_trans.sru:L356-L362], which is why SQLite classifies as DBT_MSSQL.
                if (Predicates.IsFailed(BindParams(ref sql, (long)transaction.GetDbType())))
                {
                    // [:L83] the verbatim Chinese diagnostic again. The statement is deliberately NOT
                    // included: a half-bound statement is the most literal-dense form it ever takes,
                    // and the oracle does not surface it here either.
                    _ = OnError(RetCode.E_SQL_BIND_ARG_FAILED, BindArgumentFailureMessage);

                    if (_logger.IsEnabled(LogLevel.Warning))
                    {
                        // Constraint C-F: the statement reaches the record only through the redactor,
                        // which has no disabled mode. Guarded so the projection is not computed when
                        // the level is off.
                        _logger.LogWarning(
                            "SQL parameter binding failed under auto-commit mode {AutoCommitMode}. Statement: {SqlSyntax}",
                            _autoCommit,
                            SqlRedactor.Instance.Redact(sql));
                    }

                    // [:L84]
                    return RetCode.E_SQL_BIND_ARG_FAILED;
                }
            }

            // -------------------------------------------------------------------------------------
            // STEP 6 - [:L88-L90] THE INVERSION. See the remarks above; this is the single most
            // counter-intuitive line in the file.
            // -------------------------------------------------------------------------------------
            if (_autoCommit == AutoCommitMode.AcNative)
            {
                // AC_NATIVE means 不开启事务 - "do not open a transaction" - and it is achieved by
                // turning the connection's own auto-commit ON, which is what stops the runtime opening
                // an explicit one.
                transaction.AutoCommit = true;
            }

            // -------------------------------------------------------------------------------------
            // STEP 7 - [:L92] execute. Positional binding, batch execution and error-text retrieval
            // all live behind this call, in Transactions/ - this task drives them, it does not
            // implement them. The SQLCode = 100 reading is already folded into the answer.
            // -------------------------------------------------------------------------------------
            rtCode = transaction.Exec(sql);

            // -------------------------------------------------------------------------------------
            // STEPS 8 AND 9 - [:L94-L112] the two epilogues.
            // -------------------------------------------------------------------------------------
            if (Predicates.IsSucceeded(rtCode))
            {
                // [:L95] the native arm.
                if (_autoCommit == AutoCommitMode.AcNative)
                {
                    // [:L96] restore the flag the step-6 inversion raised.
                    transaction.AutoCommit = false;

                    // [:L97] the committed notification fires WITHOUT a commit, because the runtime
                    // already made the work durable. This is the only place in the file that raises it
                    // directly.
                    OnCommitted();
                }

                // [:L98] the on arm.
                else if (_autoCommit == AutoCommitMode.AcOn)
                {
                    // [:L99] `of_Commit(true)` - auto-rollback TRUE, and the commit's answer REPLACES
                    // the statement's. So a statement that succeeded and then failed to commit is
                    // reported as a failure, which is the honest reading.
                    rtCode = Commit(autoRollback: true);

                    // [:L100-L102]
                    if (Predicates.IsFailed(rtCode))
                    {
                        // [:L101] the transaction's own error text, not a synthesized message. Note
                        // that this event rolls back AGAIN on the base - the commit helper's
                        // auto-rollback has already run - and that double rollback is legacy behaviour.
                        _ = OnError(rtCode, transaction.SqlErrText);
                    }
                }

                // ⚠ NO `else`, AND THAT IS THE POINT [:L94-L103]. On AC_OFF - and on any value outside
                // the three enumerators, which SetAutoCommit accepts unvalidated - NOTHING HAPPENS
                // HERE. No commit, no notification, no diagnostic. The caller owns the transaction and
                // its session decides when the work becomes durable. Writing the "obvious" commit into
                // this position would change transactional semantics for every batching caller, so the
                // arm is left empty deliberately and this comment is its implementation.
            }
            else
            {
                // [:L105] the native arm of the failure epilogue.
                if (_autoCommit == AutoCommitMode.AcNative)
                {
                    // [:L106] restore the flag - and ⚠ PERFORM NO ROLLBACK, because there was no
                    // explicit transaction to roll back. The general-error event below still rolls
                    // back, on the base [n_cst_thread_task_sqlbase.sru:L730-L734], so this path's
                    // observable rollback count is exactly one rather than zero. See the remarks.
                    transaction.AutoCommit = false;
                }
                else
                {
                    // [:L108] every other mode, AC_OFF and any unmatched value included, rolls back
                    // HERE - and then again through the general-error event below, for two in total.
                    // The transaction's own rollback is called rather than the task's helper, matching
                    // `transObject.of_Rollback()`; the two are equivalent at this point because the
                    // acquisition above has already attached the instance.
                    _ = transaction.Rollback();
                }

                // [:L110] ⚠ THE FOLDER'S CLEAREST REDACTION SITE (constraint C-F). The third argument
                // is the FULLY BOUND, FULLY INTERPOLATED statement, and the legacy logger performs no
                // redaction at all. The base's handler builds the payload with
                // DbErrorData.FromStatement, forwards it to the caller-side proxy, and writes it to the
                // log only through SqlRedactor; the sole sanctioned path to the wire, `ToDbError()`,
                // masks this field unconditionally and takes no policy argument a call site could
                // weaken. Buffer and row are Primary and 0, exactly as the oracle passes them.
                _ = OnDbError(
                    transaction.SqlDbCode,
                    transaction.SqlErrText,
                    sql,
                    DwBuffer.Primary,
                    0L);

                // [:L111] and then the general error, carrying the code the statement returned.
                _ = OnError(rtCode, transaction.SqlErrText);
            }

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                // Constraint C-F again: redacted, always, and computed only when the level is on. The
                // descriptor's LogPass is write-only and is not read here or anywhere else in this file.
                _logger.LogDebug(
                    "SQL command finished with {ResultCode} under auto-commit mode {AutoCommitMode}. Statement: {SqlSyntax}",
                    rtCode,
                    _autoCommit,
                    SqlRedactor.Instance.Redact(sql));
            }

            // -------------------------------------------------------------------------------------
            // STEP 10 - [:L114] the statement's code, or the commit's if the on arm replaced it.
            // -------------------------------------------------------------------------------------
            return rtCode;
        }
        finally
        {
            // Lowered on every exit, the early returns and any exception included, so a task that
            // faulted does not stay permanently busy and permanently un-resettable.
            _running = false;
        }
    }

    #endregion
}
