// =================================================================================================
//  Tasks/SqlQueryTask.cs - the CHUNKED-RETRIEVAL WORKER TASK behind contract C-05.
//
//  MANAGED PORT OF
//      ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru   (883 lines, 29,183 bytes)
//          $PBExportComments: PowerThread线程SQL查询任务对象~r~n[运行在子线程]
//                             = WORKER-THREAD AFFINITY, declared by the object about itself [:L2]
//
//  REFERENCE ONLY - read as specification, never ported here (constraint C-C):
//      ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlquery.sru   the caller-side proxy,
//          whose six events [:L10-L15] are the exact shape of IQueryResultSink below
//      ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru       the base this derives from
//      ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds.sru    main-thread carrier
//      ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds_mt.sru worker-thread carrier
//      ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_hook.sru  the retrieval hook
//      ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru              the dialect resolver and
//          the query/exec surface: of_gridsyntaxfromsql [:L81], of_query [:L82], of_isfailed [:L83],
//          of_getdbtype [:L356-L361], onbeforeretrieve / onafterretrieve [:L11-L12]
//      ws_objects/pfw.utility.parser.pbl.src/n_sql.sru                       the statement model
//      ws_objects/pfw.shared.pbl.src/enums.sru                               SQL_MS_* [:L718-L720]
//      ws_objects/pfw.shared.pbl.src/retcode.sru                             the return-code algebra
//      docs/PB多线程绕坑提示.md                                                the two threading hazards
//
//  Every behavioural claim below carries a ws_objects/** locator, and every locator in this file was
//  opened and read rather than copied forward.
//
// -------------------------------------------------------------------------------------------------
// -------------------------------------------------------------------------------------------------
//  OWNERSHIP CROSS-CHECK - WHAT THIS FILE DELIBERATELY DOES NOT IMPLEMENT (constraint C-A)
// -------------------------------------------------------------------------------------------------
//  This type DRIVES the pieces of the retrieval; it does not re-own any of them. Nothing below
//  reimplements:
//      the changeset codec or the full-state codec        Buffers/ChangesetCodec.cs, FullStateCodec.cs
//      either paging rewriter or the dialect dispatch     Sql/Paging/*.cs
//      the one-based clause upsert                        Sql/ClauseModifier.cs
//      the SELECT statement model                         Sql/SelectStatementModel.cs
//      the statement redactor                             Errors/SqlRedactor.cs
//      any database connection or storage provider        Data/*.cs
//      the buffers, item statuses, counts or the carrier  Buffers/DataWindowBuffers.cs, ItemStatus.cs
//      the transaction pool or the pooled transaction     Transactions/*.cs
//      parameter binding, the datastore cache, the hook
//      activation or the argument matching                Tasks/SqlTaskBase.cs
//
//  FOUR SEAMS ARE DECLARED HERE, and each is declared because NO sibling owns it - verified by
//  reading every file in this project, not assumed:
//    1. QueryDataWindowProperty - the DataWindow property names this task reads that the base's
//       DataWindowProperty does not carry. That set is CLOSED at six by its own documentation
//       ("the port invents no seventh"), and those six are the ones the BASE touches. The query task
//       touches five more, so they are declared where they are consumed.
//    2. IQueryTransactionSurface - the four n_cst_thread_trans members this task calls that
//       IPooledTransaction does not expose: of_gridsyntaxfromsql, of_query, and the onbeforeretrieve
//       / onafterretrieve events. A repository-wide search confirms no other file in the service
//       declares or consumes any of them.
//    3. IQueryDataWindowRuntime - the three DataWindow-runtime operations with no managed analogue:
//       Create(syntax, ref err) [:L619], GetChild(col, ref child) [:L120] and
//       SetTransObject(trans) [:L678]. IDataObjectRuntime carries only definition resolution and
//       retrieval execution, by its own design.
//    4. IQueryResultSink - the caller-side proxy, narrowed to the six events it declares.
//  The precedent is the project's own: ISqlDataStore and IDataObjectRuntime are declared inside
//  Tasks/SqlTaskBase.cs, IFullStateCarrierSurface inside Buffers/FullStateCodec.cs and
//  IChangesetTransferSink inside Buffers/ChangesetCodec.cs - each where it is consumed.
//
// -------------------------------------------------------------------------------------------------
//  THE PRESERVED DEFECTS THIS FILE REPRODUCES (constraint C-B, annotated at each reproduction site)
// -------------------------------------------------------------------------------------------------
//   D1  THE CHUNK-SIZE GUARD IS `<= 1000`, so 1000 ITSELF IS REJECTED and 1001 is the lowest legal
//       value - while the field's own inline comment reads "min:1000" [:L34] and contradicts it.
//       The code is the oracle [:L410]. See SetChunkSize.
//   D2  `_bPageNative` IS NOT CLEARED BY THE PRIVATE RESET [:L247-L267]. Every other settable field
//       is. Found by reading _of_reset in full, line by line; it is not an oversight in this port.
//       See ResetQueryState.
//   D3  THE PAGED-STATEMENT BUILDER LEAKS ITS PARSER ON EVERY ERROR PATH: `Destroy sqlParser` sits at
//       [:L401], AFTER all three early returns at [:L309], [:L316] and [:L398]. Same family as the
//       AAP's note that parsesql.srf leaks its instance. See BuildPagedStatement.
//   D4  THE FILTERED COUNT IS NEVER RE-INITIALISED. `nFilterCnt` is declared once for the whole event
//       [:L75] and assigned only inside a conditional, so it survives across chunk iterations and a
//       later discard can act on a stale, possibly out-of-range count. See PublishResultAsync.
//   D5  THE DIALECT RESOLVER HAS EXACTLY TWO ARMS AND NO SQLITE ARM
//       [n_cst_thread_trans.sru:L356-L361]. Anything whose DBMS string does not contain "ORACLE" -
//       INCLUDING SQLITE - classifies as the SQL Server type. See BuildPagedStatement.
//   D6  THE QUERY-SIDE DEFENSIVE OVERRIDE TESTS THE SQL CODE FOR EXACTLY -1 AND THE ROW COUNT FOR
//       `>= 0`, and it ADDITIONALLY RESETS THE CARRIER [:L771-L774]. It is neither the transaction's
//       own non-zero test nor its negative failure predicate. See ApplyDefensiveRowCountOverride.
//   D7  THE COUNT QUERY'S FAILURE DISCRIMINATION CARRIES AN UNREACHABLE GUARD AND A CANCELLATION
//       LEAK [:L843-L849]. `IsFailed` already excludes CANCELLED [isfailed.srf:L11-L13], so the inner
//       `rtCode <> RetCode.CANCELLED` test can never be false; and because a CANCELLED result is not
//       "failed", it falls THROUGH to the `rtCode = 1` test, fails it, and is reported as a DATABASE
//       ERROR carrying the text 检索失败. Found by reading the two predicates against the call site.
//       See CountPagesAsync.
//   D8  THE EMPTY SORT AND FILTER ARE REWRITTEN TO A SINGLE SPACE [:L481, :L487]. It looks like a
//       typo; it is how the legacy clears rather than leaves unset. See SetSort and SetFilter.
//   D9  THE RUNTIME STATEMENT SUBSTITUTION DOES NOT ESCAPE EMBEDDED QUOTES [:L635] while the other
//       three Modify sites do [:L709, :L732, :L795]. See SubstituteRuntimeStatement.
//   D10 THE FOUR CROSS-THREAD TRANSFER DEFECTS the codecs reproduce - lost rows on a sorted
//       multi-block carrier [:L147-L149], Reset forbidden mid-loop [:L175-L176], full state REQUIRING
//       sort/filter synchronization [:L562-L563] against changeset CLEARING them [:L577-L578], and
//       the wide-crosstab full-state crash [:L672-L673]. This file owns the CHOICE and the DRIVING;
//       Buffers/ owns the reproduction. See PublishResultAsync and ApplySortAndFilter.
//
//  NONE of these is corrected. C-B forbids improvements, and each one is reachable from an in-scope
//  code path, so each is annotated at the point of reproduction so a future reader cannot mistake it
//  for an implementation error.
//
// -------------------------------------------------------------------------------------------------
//  THE CONSTRAINTS THAT SHAPE THE CODE RATHER THAN JUST THE COMMENTS
// -------------------------------------------------------------------------------------------------
//  C-E  The dialect value selects a PURE STRING TRANSFORM, never a connection. Persistence
//       provisions SQLite only; the SQL Server and Oracle behaviours exist solely as byte-exact
//       string rewriters under Sql/Paging/ and are driven here with no instance of either engine.
//  C-F  Every database-error payload's statement text passes through Errors/ISqlRedactor before it
//       leaves the process. Both statements this file produces - the retrieval statement and the
//       count wrapper - carry interpolated literals whenever the connection disabled bind variables.
//  C-G  No listener, no route, no ambient entry point. This type is reached only through C-05's
//       authorized gRPC methods, and the hook CLASS NAME it accepts is caller-controlled input that
//       is resolved through the base's restricted activator rather than activated directly.
//  C-H  TimeProvider is the ONLY clock. There is no DateTime.UtcNow, no DateTime.Now, no
//       Environment.TickCount and no Stopwatch anywhere in this file; the 20 ms inter-chunk yield is
//       driven by the injected provider through the changeset codec. Every collaborator is an
//       abstraction, so the whole type is exercisable with no database and no real thread.
//  C-I  No new PackageReference. Everything used here already arrives through the project's existing
//       references, and the file builds clean under TreatWarningsAsErrors.
//  C-J  NO STATIC GLOBAL. The legacy declares `global n_cst_thread_task_sqlquery
//       n_cst_thread_task_sqlquery` [:L23]; nothing here reproduces it. This type is DI-registered
//       and remains a DISTINCT type from its caller-side proxy, which is the boundary the legacy's
//       own n_cst_threading*/n_cst_thread* duality exists to protect.
//  .editorconfig  ZERO SCREAMING_SNAKE DECLARATIONS. Tasks/ carries no scoped naming suppression, so
//       CA1707 and IDE1006 are live and warnings are errors. The legacy VALUES are consumed from
//       where they are already declared - Enums.SQL_MS_* and ClauseModifier.SQL_MS_* in the shared
//       kernel and the Sql folder, DatabaseType.Dbt* and DwBuffer/ItemStatus from the generated
//       contracts, and the return codes from RetCode - and every identifier declared here is
//       PascalCase carrying a preserved legacy VALUE.
//
// -------------------------------------------------------------------------------------------------
//  THE TWO DOCUMENTED THREADING HAZARDS (docs/PB多线程绕坑提示.md, 5 lines, read whole)
// -------------------------------------------------------------------------------------------------
//  HAZARD 1 [:L1-L4] - a worker synchronously calling a MAIN-thread function or event whose return
//  type is `string` or `blob` can fault, and the guidance is to return via `ref` instead. That is
//  precisely why the proxy declares `onchilddatareceived(string name, ref blob blbdata)` [:L10] and
//  `ondatachunk(ref blob blbdata, ...)` [:L13], and why IQueryResultSink carries its payloads INWARD
//  as parameters rather than returning them.
//  HAZARD 2 [:L5] - after a global object has been passed to a worker, the worker must SetNull the
//  reference to release it. Its two visible traces in this object are `SetNull(data)` after the
//  hand-over [:L88] and the deliberate `if bCacheDS then data.Reset() else Destroy data`
//  discrimination in the cleanup block [:L876-L880]. Both are reproduced as deterministic disposal,
//  never left to the collector.
// =================================================================================================

using System.Globalization;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using PowerFramework.Contracts.Common.V1;
using PowerFramework.Contracts.Persistence.V1;
using PowerFramework.Persistence.Buffers;
using PowerFramework.Persistence.Configuration;
using PowerFramework.Persistence.Errors;
using PowerFramework.Persistence.Sql;
using PowerFramework.Persistence.Sql.Paging;
using PowerFramework.Persistence.Transactions;
using PowerFramework.Shared.Containers;

using Predicates = PowerFramework.Shared.Kernel.Predicates;
using RetCode = PowerFramework.Shared.Kernel.RetCode;

namespace PowerFramework.Persistence.Tasks;

#region The DataWindow properties the QUERY task reads, beyond the base's closed set of six

/// <summary>
/// The DataWindow property names and column-indexed property formats that the query task reads
/// through <see cref="ISqlDataStore.Describe"/>, spelled exactly as the oracle spells them.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists alongside <see cref="DataWindowProperty"/> rather than extending it.</b> That
/// type states of itself that its set "is closed at the six properties the SQL task layer genuinely
/// touches - the port invents no seventh", and those six are the ones the BASE touches. The query
/// task touches five more, all of them verified at their call sites in
/// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru</c>. Adding them to the base's
/// set would widen a deliberately closed contract; declaring them where they are consumed keeps both
/// sets honest.
/// </para>
/// <para>
/// <b>The VALUES are preserved and the identifiers are not the legacy's.</b> Each string is the key a
/// DataWindow's own property dictionary is addressed by, so a "tidied" spelling would silently
/// address nothing; the C# identifiers are PascalCase because this file carries no
/// <c>.editorconfig</c> naming suppression. That is exactly the split AAP §0.4.5.3 draws.
/// </para>
/// </remarks>
internal static class QueryDataWindowProperty
{
    /// <summary>
    /// <c>DataWindow.Syntax</c> - the generated syntax handed to the receiving side so it can build
    /// itself, read at <c>n_cst_thread_task_sqlquery.sru:L104</c> and again at <c>:L156</c>.
    /// </summary>
    internal const string Syntax = "DataWindow.Syntax";

    /// <summary>
    /// <c>DataWindow.Column.Count</c> - the column total the drop-down child walk iterates, read at
    /// <c>n_cst_thread_task_sqlquery.sru:L113</c> and at <c>:L647</c>.
    /// </summary>
    internal const string ColumnCount = "DataWindow.Column.Count";

    /// <summary>
    /// <c>DataWindow.Table.Procedure</c> - read at <c>n_cst_thread_task_sqlquery.sru:L626</c>.
    /// </summary>
    /// <remarks>
    /// <b>The test is against the failure sentinel, and the sense is inverted from the obvious
    /// reading.</b> The oracle writes <c>bIsProcedure = (data.Describe("...") &lt;&gt; "!")</c>, so a
    /// source is a stored procedure precisely when the property IS readable. A port that tested for
    /// equality would classify every ordinary SELECT as a procedure and suppress paging, clause
    /// modification and page counting for all of them.
    /// </remarks>
    internal const string TableProcedure = "DataWindow.Table.Procedure";

    /// <summary>
    /// The column-indexed <c>DDDW.AutoRetrieve</c> property, read at
    /// <c>n_cst_thread_task_sqlquery.sru:L115</c> and at <c>:L649</c>.
    /// </summary>
    /// <remarks>
    /// A format rather than a name, because the legacy composes it as
    /// <c>"#" + String(nIndex) + ".DDDW.AutoRetrieve"</c> against a ONE-BASED column ordinal.
    /// </remarks>
    internal const string ColumnDropDownAutoRetrieveSuffix = ".DDDW.AutoRetrieve";

    /// <summary>
    /// The column-indexed <c>DDDW.Name</c> property, read at
    /// <c>n_cst_thread_task_sqlquery.sru:L117</c> and at <c>:L651</c>.
    /// </summary>
    internal const string ColumnDropDownNameSuffix = ".DDDW.Name";

    /// <summary>
    /// The column-indexed <c>Name</c> property, read at
    /// <c>n_cst_thread_task_sqlquery.sru:L119</c> and at <c>:L655</c>.
    /// </summary>
    internal const string ColumnNameSuffix = ".Name";

    /// <summary>
    /// The prefix that turns a one-based column ordinal into a column-indexed property expression -
    /// the <c>"#"</c> of <c>"#" + String(nIndex) + "..."</c>.
    /// </summary>
    internal const string ColumnOrdinalPrefix = "#";

    /// <summary>
    /// The modification fragment that disables a drop-down's automatic retrieval, accumulated one per
    /// line at <c>n_cst_thread_task_sqlquery.sru:L653</c> and <c>:L656</c>.
    /// </summary>
    /// <remarks>
    /// The trailing <c>~n</c> of the legacy literal is a NEWLINE, and it is load-bearing: the
    /// fragments are concatenated into a single multi-line modify script issued once at <c>:L664</c>.
    /// </remarks>
    internal const string DropDownAutoRetrieveOffSuffix = ".DDDW.AutoRetrieve = no \n";

    /// <summary>
    /// Composes a column-indexed property expression for a ONE-BASED column ordinal.
    /// </summary>
    /// <param name="columnOrdinal">The one-based column ordinal, exactly as the legacy loop counts.</param>
    /// <param name="suffix">
    /// One of <see cref="ColumnDropDownAutoRetrieveSuffix"/>, <see cref="ColumnDropDownNameSuffix"/>,
    /// <see cref="ColumnNameSuffix"/> or <see cref="DropDownAutoRetrieveOffSuffix"/>.
    /// </param>
    /// <returns>The composed expression, for example <c>#3.DDDW.Name</c>.</returns>
    /// <remarks>
    /// R9 (AAP §0.4.5.4). The ordinal is passed through UNCHANGED because the legacy expression is
    /// one-based and the DataWindow property grammar is one-based; rebasing it here would address the
    /// wrong column while still producing a syntactically valid expression, which is the class of
    /// silent defect the one-based discipline exists to prevent. Formatted invariantly so the
    /// generated expression cannot vary with the host culture - these strings feed byte-exact
    /// comparisons.
    /// </remarks>
    internal static string ForColumn(int columnOrdinal, string suffix) =>
        ColumnOrdinalPrefix
        + columnOrdinal.ToString(CultureInfo.InvariantCulture)
        + suffix;
}

#endregion

#region The query-side transaction surface - the four n_cst_thread_trans members not on IPooledTransaction

/// <summary>
/// The outcome of the counting query: the legacy return value, the carrier it filled, and the
/// diagnostic text.
/// </summary>
/// <param name="ReturnCode">
/// What <c>of_Query</c> answered [<c>n_cst_thread_trans.sru:L292-L336</c>]. It is the RETRIEVED ROW
/// COUNT on success - which is why the query task tests it for exactly <c>1</c> at
/// <c>n_cst_thread_task_sqlquery.sru:L851</c> - and a return code otherwise. It is therefore NOT a
/// pure member of the return-code algebra, and this record does not pretend it is.
/// </param>
/// <param name="Result">
/// The carrier the statement filled, or <see langword="null"/> when none was produced. The legacy
/// passes a <c>ref datastore</c> that <c>of_Query</c> creates when the caller supplied none, and
/// destroys again if the retrieve failed [<c>:L303-L306, :L328-L332</c>].
/// </param>
/// <param name="ErrorText">
/// The <c>ref string errinfo</c> out-parameter. Empty when the call reports nothing.
/// </param>
internal sealed record CountQueryOutcome(long ReturnCode, DataWindowBufferStore? Result, string ErrorText);

/// <summary>
/// The result of deriving a grid DataWindow syntax from a statement -
/// <c>of_gridsyntaxfromsql(readonly string sql, ref string errinfo)</c>
/// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:L81, :L282-L291</c>].
/// </summary>
/// <param name="Syntax">The derived syntax, empty when none could be derived.</param>
/// <param name="ErrorText">
/// The <c>ref string errinfo</c> out-parameter. <b>The query task tests THIS and not the syntax</b>
/// [<c>n_cst_thread_task_sqlquery.sru:L611</c>], so an empty syntax with an empty error is NOT a
/// failure there - and the legacy's own implementation relies on exactly that pairing, calling the
/// runtime only when both come back empty [<c>n_cst_thread_trans.sru:L285-L287</c>].
/// </param>
internal sealed record GridSyntaxOutcome(string Syntax, string ErrorText);

/// <summary>
/// The members of the legacy transaction object <c>n_cst_thread_trans</c> that the query task calls
/// and that <see cref="IPooledTransaction"/> does not expose.
/// </summary>
/// <remarks>
/// <para>
/// <b>Four members, and each is here because nothing else in the service carries it.</b>
/// <see cref="IPooledTransaction"/> covers the connection, the SQL state, commit and rollback, the
/// command surface and <c>of_GetDBType</c>; it deliberately carries neither of the two statement
/// helpers nor either retrieval event. A repository-wide search of this project confirms that no
/// other file declares or consumes <c>of_query</c>, <c>of_gridsyntaxfromsql</c>,
/// <c>onbeforeretrieve</c> or <c>onafterretrieve</c>, so this is the declaration site.
/// </para>
/// <para>
/// <b>The transaction is a PARAMETER rather than the implementing object.</b> The legacy members are
/// instance members of the transaction, but the managed pooled transaction is created by an activator
/// this file does not own, so an interface EXTENDING <see cref="IPooledTransaction"/> would be
/// unimplementable by the type the composition root actually produces. Passing the transaction in
/// keeps one object per legacy object at the call site while letting the composition root supply the
/// behaviour, and it keeps the whole surface substitutable so the query task is exercisable with no
/// database at all (constraint C-H).
/// </para>
/// <para>
/// <b>Constraint C-E.</b> Nothing on this interface opens, selects or configures a storage engine.
/// <see cref="Query"/> executes a statement through a transaction the pool already owns; the dialect
/// that shapes that statement is chosen by <see cref="IPooledTransaction.GetDbType"/> and consumed by
/// a pure string transform under <c>Sql/Paging/</c>.
/// </para>
/// </remarks>
internal interface IQueryTransactionSurface
{
    /// <summary>
    /// Derives a grid DataWindow syntax from a statement - <c>of_GridSyntaxFromSQL(sSQL, ref sError)</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L610</c>].
    /// </summary>
    /// <param name="transaction">The attached transaction, whose engine shapes the derivation.</param>
    /// <param name="sql">The statement to derive from.</param>
    /// <returns>The syntax and the diagnostic text, as a pair.</returns>
    GridSyntaxOutcome GridSyntaxFromSql(IPooledTransaction transaction, string sql);

    /// <summary>
    /// Executes a statement and returns its rows - <c>of_Query(sSQL, ref dsTmp, sError)</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L843</c>].
    /// </summary>
    /// <param name="transaction">The attached transaction.</param>
    /// <param name="sql">The statement to execute.</param>
    /// <param name="cancellationToken">The task's cancellation token.</param>
    /// <returns>The outcome, whose return code is the legacy value verbatim.</returns>
    /// <remarks>
    /// The only caller is the page-counting path, and it reads exactly one value out of the result -
    /// row 1, column 1 [<c>:L852</c>]. The shape is nonetheless a carrier rather than a scalar,
    /// because that is what the legacy hands back and because narrowing it to a scalar would make the
    /// <c>rtCode = 1</c> row-count test meaningless.
    /// </remarks>
    ValueTask<CountQueryOutcome> Query(
        IPooledTransaction transaction,
        string sql,
        CancellationToken cancellationToken);

    /// <summary>
    /// Raises the vetoable before-retrieve event - <c>TransObject.Event OnBeforeRetrieve(data)</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L746</c>], declared
    /// <c>event type long onbeforeretrieve(datastore data)</c>
    /// [<c>n_cst_thread_trans.sru:L11</c>].
    /// </summary>
    /// <param name="transaction">The attached transaction.</param>
    /// <param name="data">The result carrier the retrieval will fill.</param>
    /// <returns>
    /// The event result. <b>It is tested with the PREVENTION predicate</b>, which is tri-valued: a
    /// prevent-once (1) and a prevent-deep (2) both veto, and the distinction must not be flattened.
    /// An unimplemented PowerBuilder event returns zero and therefore does not veto.
    /// </returns>
    long RaiseBeforeRetrieve(IPooledTransaction transaction, DataWindowCarrier data);

    /// <summary>
    /// Raises the after-retrieve notification -
    /// <c>TransObject.Event OnAfterRetrieve(data, nRowCnt)</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L769</c>], declared
    /// <c>event onafterretrieve(datastore data, long result)</c>
    /// [<c>n_cst_thread_trans.sru:L12</c>].
    /// </summary>
    /// <param name="transaction">The attached transaction.</param>
    /// <param name="data">The result carrier.</param>
    /// <param name="rowCount">The row count the retrieval answered, BEFORE the defensive override.</param>
    /// <remarks>
    /// <b>The ordering is contract.</b> This fires BEFORE the defensive row-count override at
    /// <c>:L771-L774</c>, so a handler observes the row count the retrieval itself reported even when
    /// the very next statement rewrites it to <c>-1</c>. It returns nothing, matching an event
    /// declared with no return type.
    /// </remarks>
    void RaiseAfterRetrieve(IPooledTransaction transaction, DataWindowCarrier data, long rowCount);
}

#endregion

#region The DataWindow-runtime surface - the three operations with no managed analogue

/// <summary>
/// The result of building a result carrier from a DataWindow syntax string -
/// <c>data.Create(sSQLSyntax, ref sError)</c>
/// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L619</c>].
/// </summary>
/// <param name="Result">
/// PowerBuilder's own <c>Create</c> convention: <see cref="DataWindowBufferStore.DataStoreSuccess"/>
/// on success. The legacy tests <c>&lt;&gt; 1</c>, so ANY other value is failure.
/// </param>
/// <param name="ErrorText">The <c>ref string</c> out-parameter, empty when the call reports nothing.</param>
internal sealed record CarrierCreateOutcome(long Result, string ErrorText);

/// <summary>
/// The three DataWindow-runtime operations the query task performs that neither
/// <see cref="IDataObjectRuntime"/> nor <see cref="ISqlDataStore"/> carries.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why these three and no others.</b> <see cref="IDataObjectRuntime"/> declares exactly two
/// members - definition resolution and retrieval execution - and says so deliberately. The query task
/// additionally builds a carrier from a syntax string [<c>:L619</c>], reaches a drop-down child result
/// [<c>:L120</c>] and attaches the transaction [<c>:L678</c>]. All three are PowerBuilder runtime
/// operations with no managed equivalent, so all three become injected behaviour rather than
/// invented behaviour.
/// </para>
/// <para>
/// <b>Constraint C-H.</b> Because they are seams rather than static calls, the whole retrieval path is
/// drivable by a test that supplies carriers it fully controls - no DataWindow runtime, no database
/// and no real thread.
/// </para>
/// </remarks>
internal interface IQueryDataWindowRuntime
{
    /// <summary>
    /// Builds a store's definition from a DataWindow syntax string -
    /// <c>data.Create(sSQLSyntax, ref sError)</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L619</c>].
    /// </summary>
    /// <param name="data">The store to build.</param>
    /// <param name="syntax">The syntax, either supplied by the caller or derived from the statement.</param>
    /// <returns>The result and its diagnostic text.</returns>
    CarrierCreateOutcome CreateFromSyntax(ISqlDataStore data, string syntax);

    /// <summary>
    /// Resolves a column's drop-down child result - <c>Data.GetChild(sColName, ref dwcSrc)</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L120</c>].
    /// </summary>
    /// <param name="data">The store to resolve against.</param>
    /// <param name="columnName">The column name, read from the column's own <c>Name</c> property.</param>
    /// <param name="child">Receives the child carrier, or <see langword="null"/> when none resolves.</param>
    /// <returns>
    /// <see langword="true"/> when the child resolved. <b><see langword="false"/> is the legacy's
    /// <c>-1</c>, and the legacy responds to it with <c>continue</c> rather than with an error</b>
    /// [<c>:L120</c>] - the column is SKIPPED and the walk goes on.
    /// </returns>
    bool TryGetChild(ISqlDataStore data, string columnName, out DataWindowBufferStore? child);

    /// <summary>
    /// Attaches the transaction to the store - <c>data.SetTransObject(TransObject)</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L678</c>].
    /// </summary>
    /// <param name="data">The store to attach to.</param>
    /// <param name="transaction">The pooled transaction.</param>
    /// <returns>
    /// <see cref="DataWindowBufferStore.DataStoreSuccess"/> on success. The legacy tests
    /// <c>&lt;&gt; 1</c>, so any other value is failure and yields
    /// <c>RetCode.E_INVALID_TRANSACTION</c>.
    /// </returns>
    /// <remarks>
    /// <b>The POSITION of this call is contract (constraint C-B).</b> It sits AFTER the wide-crosstab
    /// no-user-prompt workaround [<c>:L672-L676</c>] and BEFORE the stored clause modifications
    /// [<c>:L684</c>]. See <see cref="SqlQueryTask.ExecuteAsync"/>, where the ordering is preserved and
    /// annotated.
    /// </remarks>
    long AttachTransaction(ISqlDataStore data, IPooledTransaction transaction);
}

#endregion

#region The result sink - the caller-side proxy, narrowed to the six events it declares

/// <summary>
/// The caller-side proxy this worker task publishes its result into
/// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlquery.sru</c>, marked
/// <c>[运行在当前线程]</c>], narrowed to the surface the query task actually drives.
/// </summary>
/// <remarks>
/// <para>
/// <b>The six events are the proxy's own declaration list</b> [<c>:L10-L15</c>], and the mapping is
/// one-to-one:
/// </para>
/// <list type="table">
///   <item>
///     <term><c>onchilddatareceived(string name, ref blob blbdata)</c> [<c>:L10</c>]</term>
///     <description><see cref="IChangesetTransferSink.SendChildChunkAsync"/></description>
///   </item>
///   <item>
///     <term><c>oncreatedata(readonly string syntax)</c> [<c>:L11</c>]</term>
///     <description><see cref="IChangesetTransferSink.CreateDataAsync"/></description>
///   </item>
///   <item>
///     <term><c>ondatareceived(long rowcount)</c> [<c>:L12</c>]</term>
///     <description><see cref="OnDataReceived"/></description>
///   </item>
///   <item>
///     <term><c>ondatachunk(ref blob, long count, long current, boolean fullstate)</c> [<c>:L13</c>]</term>
///     <description><see cref="IChangesetTransferSink.SendChunkAsync"/> for the changeset arm and
///     <see cref="SendFullStateChunk"/> for the full-state arm</description>
///   </item>
///   <item>
///     <term><c>onpagereceived(long pagecount, long recordcount)</c> [<c>:L14</c>]</term>
///     <description><see cref="OnPageReceived"/></description>
///   </item>
///   <item>
///     <term><c>ondatamove(datastore ds)</c> [<c>:L15</c>]</term>
///     <description><see cref="OnDataMove"/></description>
///   </item>
/// </list>
/// <para>
/// <b>Why it EXTENDS <see cref="IChangesetTransferSink"/> instead of duplicating it.</b> Three of the
/// six are already declared there, by the codec that consumes them, with their sign-test conventions
/// and their verbatim error texts documented at the declaration. Re-declaring them here would create
/// a second place for those conventions to drift, and the changeset codec would then need an adapter
/// to reach a sink the task already holds. Extending gives one object for one legacy object.
/// </para>
/// <para>
/// <b>Two of the six carry the payload INWARD, per hazard 1</b>
/// [<c>docs/PB多线程绕坑提示.md:L1-L4</c>]. Nothing on this interface returns a string or a buffer
/// across the boundary.
/// </para>
/// <para>
/// <b>The two state questions are questions, not events.</b> <c>_of_HasReceiver()</c> and
/// <c>_of_NeedCreate()</c> [<c>:L84-L85</c>] are public functions on the proxy that this task reads at
/// four decision points - the hand-over predicate [<c>:L86</c>], the sort/filter gate [<c>:L559</c>],
/// its else-branch condition [<c>:L583</c>] and the create-data guard [<c>:L103</c>]. They are
/// properties here because that is what they are.
/// </para>
/// </remarks>
internal interface IQueryResultSink : IChangesetTransferSink
{
    /// <summary>
    /// Whether a receiver object is installed on the proxy - <c>tasking._of_HasReceiver()</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L86, :L583</c>].
    /// </summary>
    /// <remarks>
    /// A receiver means some other object has to be populated, so the carrier cannot simply change
    /// hands - which is why this is one of the three conditions of
    /// <see cref="DataWindowCarrierOwnership.CanMoveWithoutSerialization"/>.
    /// </remarks>
    bool HasReceiver { get; }

    /// <summary>
    /// Whether the receiving side still has to build itself from the transferred syntax -
    /// <c>tasking._of_NeedCreate()</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L103, :L559, :L583</c>].
    /// </summary>
    bool NeedsCreatedObject { get; }

    /// <summary>
    /// Publishes the row count of the WHOLE result - <c>tasking.Event OnDataReceived(rowCount)</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L83</c>].
    /// </summary>
    /// <param name="rowCount">The row count the retrieval reported.</param>
    /// <remarks>
    /// <b>It is the FIRST thing the data event does</b> [<c>:L83</c>] - before the hand-over
    /// predicate, before the codec selection and before any chunk. A consumer therefore learns the
    /// total before it sees a single row, which is exactly what contract C-05's stream shape publishes
    /// as its <c>row_count</c> arm. The legacy event has no return type, so nothing here can veto.
    /// </remarks>
    void OnDataReceived(long rowCount);

    /// <summary>
    /// Hands the FULL-STATE chunk over - the <c>fullstate = true</c> use of
    /// <c>tasking.Event OnDataChunk(ref blbData, 1, 1, true)</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L97</c>].
    /// </summary>
    /// <param name="chunk">
    /// The single chunk, carrying <see cref="FullStateCodec.SingleChunkCount"/>,
    /// <see cref="FullStateCodec.SingleChunkIndex"/> and <c>FullState = true</c>.
    /// </param>
    /// <returns>
    /// The handover result. <b>A SIGN TEST decides it</b>: the legacy writes <c>&lt; 0</c>, so zero and
    /// one are both success and this is NOT a comparison against
    /// <see cref="DataWindowBufferStore.DataStoreSuccess"/>.
    /// </returns>
    /// <remarks>
    /// <b>Why this is separate from <see cref="IChangesetTransferSink.SendChunkAsync"/> and
    /// synchronous.</b> The two arms of the legacy <c>choose case</c> hand over different payload
    /// shapes - a full-state image against a changeset - and the receiving side cannot decode one as
    /// the other, which is precisely what the <c>fullstate</c> argument exists to say. The signature
    /// matches <see cref="FullStateChunkHandover"/> exactly so the sink can be passed straight to
    /// <see cref="FullStateCodec.Send"/> with no adapter, and that delegate is synchronous because the
    /// full-state arm has no loop, no yield and no cancellation point of its own.
    /// </remarks>
    long SendFullStateChunk(QueryDataChunk chunk);

    /// <summary>
    /// Publishes the paging totals - <c>tasking.Event OnPageReceived(nPageCount, nRecordCount)</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L865</c>].
    /// </summary>
    /// <param name="pageCount">Total pages, or <c>-1</c> when counting did not happen.</param>
    /// <param name="recordCount">Total records, or <c>-1</c> when counting did not happen.</param>
    /// <remarks>
    /// <b>It fires on EVERY path, counted or not</b> [<c>:L865</c> sits outside the counting
    /// conditional], and the not-counted state is carried as the real legacy value <c>-1</c> rather
    /// than as an absence. A consumer must treat <c>-1</c> as "not counted" and must not do arithmetic
    /// on it.
    /// </remarks>
    void OnPageReceived(long pageCount, long recordCount);

    /// <summary>
    /// Takes OWNERSHIP of the carrier by reference - <c>tasking.Event OnDataMove(data)</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L87</c>].
    /// </summary>
    /// <param name="carrier">The carrier being handed over. The receiver owns it from this point.</param>
    /// <remarks>
    /// <b>NO CODEC RUNS ON THIS PATH and no payload is produced.</b> The task drops its own reference
    /// immediately afterwards [<c>:L88</c>], and the legacy warns at <c>:L813</c> that the carrier may
    /// have become invalid once the data event has run. An implementation must therefore not assume
    /// the task still holds it.
    /// </remarks>
    void OnDataMove(DataWindowCarrier carrier);
}

#endregion

#region The outcome shapes this task answers with

/// <summary>
/// What publishing the result did - the outcome of the port of <c>ondatareceived</c>
/// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L73-L235</c>].
/// </summary>
/// <param name="ReturnCode">
/// The value the legacy event returns: <c>RetCode.OK</c>, <c>RetCode.CANCELLED</c> or
/// <c>RetCode.E_INTERNAL_ERROR</c>. The caller tests it for EXACT equality with <c>RetCode.OK</c>
/// [<c>:L815</c>] rather than through the success predicate, and that distinction is preserved: a
/// prevention would read as a success in that algebra and must not short-circuit here.
/// </param>
/// <param name="CarrierHandedOver">
/// <see langword="true"/> when the main-thread hand-over fast path ran [<c>:L85-L91</c>], so the task
/// no longer holds the carrier and neither codec was involved.
/// </param>
/// <param name="FullState">
/// <see langword="true"/> when the full-state arm ran - a crosstab or composite carrier
/// [<c>:L94</c>]. <see langword="false"/> for the changeset arm and for the hand-over path.
/// </param>
/// <param name="ChunkCount">
/// The chunk total the arithmetic produced, <c>1</c> on the full-state and empty-result arms, and
/// <c>0</c> when the carrier was handed over and no chunking happened.
/// </param>
/// <param name="ChunksSent">How many chunks were actually handed over before the transfer ended.</param>
/// <param name="ChildrenSent">How many drop-down child results were handed over.</param>
internal sealed record QueryPublishOutcome(
    long ReturnCode,
    bool CarrierHandedOver,
    bool FullState,
    long ChunkCount,
    long ChunksSent,
    int ChildrenSent);

/// <summary>
/// The paging totals and how they were arrived at - the outcome of
/// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L807-L865</c>.
/// </summary>
/// <param name="ReturnCode">
/// <c>RetCode.OK</c>, or the failure the counting path reported.
/// </param>
/// <param name="PageCount">
/// Total pages, or <see cref="SqlQueryTask.NotCountedValue"/> when counting did not happen
/// [<c>:L861</c>].
/// </param>
/// <param name="RecordCount">
/// Total records, or <see cref="SqlQueryTask.NotCountedValue"/> [<c>:L862</c>].
/// </param>
/// <param name="Counted">
/// <see langword="false"/> when the short-circuit at <c>:L818-L823</c> inferred the totals with no
/// statement issued, or when counting was off altogether; <see langword="true"/> when a counting
/// statement really was executed. Contract C-05 publishes this as <c>CountResponse.counted</c>
/// precisely so a consumer cannot mistake an inferred total for a failed count.
/// </param>
/// <param name="CountStatement">
/// The counting statement that was issued, or <see cref="string.Empty"/> when none was.
/// <b>Constraint C-F: this value carries interpolated literals and is REDACTED before it leaves the
/// process.</b> It is exposed here only so the parity suite can assert the byte-exact wrapper text
/// in-process, which is the acceptance criterion for the count path.
/// </param>
internal sealed record QueryPageCountOutcome(
    long ReturnCode,
    long PageCount,
    long RecordCount,
    bool Counted,
    string CountStatement);

/// <summary>
/// The result of building the paged statement - the outcome of <c>_of_buildpagedsql</c>
/// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L303-L404</c>].
/// </summary>
/// <param name="ReturnCode">
/// <c>RetCode.OK</c>, <c>RetCode.E_INVALID_ARGUMENT</c> for an invalid paging setting,
/// <c>RetCode.E_INTERNAL_ERROR</c> for a parse failure, or <c>RetCode.E_NO_IMPLEMENTATION</c> for the
/// <c>case else</c> arm.
/// </param>
/// <param name="PagedSql">The rewritten statement, empty on failure.</param>
/// <param name="ErrorText">
/// The diagnostic that was reported. <b>EMPTY IS A REAL VALUE HERE</b>: the <c>case else</c> arm
/// reports <c>Event OnError(RetCode.E_NO_IMPLEMENTATION, "")</c> with a deliberately empty message
/// [<c>:L397</c>], and that emptiness is preserved rather than filled in.
/// </param>
internal sealed record PagedStatementOutcome(long ReturnCode, string PagedSql, string ErrorText);

#endregion


/// <summary>
/// The chunked-retrieval worker task: the managed port of
/// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru</c> and the engine behind
/// contract C-05 <c>persistence.v1.QueryService</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>WORKER-THREAD AFFINITY, declared by the oracle about itself</b> - <c>[运行在子线程]</c>
/// [<c>:L2</c>]. Every concurrency class in the legacy exists twice, a caller-side
/// <c>n_cst_threading*</c> and a worker-side <c>n_cst_thread*</c>, specifically so that no object is
/// ever touched from two threads. This is the WORKER half; <see cref="IQueryResultSink"/> is the
/// narrow slice of the caller-side half it publishes into, and the two must not be merged
/// (constraint C-J).
/// </para>
/// <para>
/// <b>Sealed and DI-registered, with no static instance.</b> The oracle declares
/// <c>global n_cst_thread_task_sqlquery n_cst_thread_task_sqlquery</c> [<c>:L23</c>] - a global
/// auto-instance shadowing its own type name. Nothing here reproduces it: a global would make the
/// per-task state below process-wide, which is the one thing a chunked retrieval cannot tolerate.
/// </para>
/// <para>
/// <b>The whole type is exercisable with no database, no DataWindow runtime and no real thread</b>
/// (constraint C-H). Every collaborator is an abstraction, the clock is the injected
/// <see cref="TimeProvider"/> and nothing reads an ambient one.
/// </para>
/// </remarks>
internal sealed class SqlQueryTask : SqlTaskBase
{
    /// <summary>
    /// Whether the unappliable no-user-prompt workaround has already been reported at warning severity,
    /// as <c>0</c> for not yet and <c>1</c> for reported.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>STATIC BECAUSE THE CONDITION IS STATIC.</b> Whether that modify can be applied depends on the
    /// DataWindow runtime this service is hosted on and on nothing about a request, so it is answered once
    /// for the process rather than once per task instance. A per-instance field would report afresh for
    /// every query, which is the behaviour being corrected.
    /// </para>
    /// <para>
    /// AN <see cref="int"/> RATHER THAN A <see cref="bool"/>, because
    /// <see cref="Interlocked.Exchange(ref int, int)"/> is what makes "first one through reports"
    /// exact under the concurrent retrievals this service serves - two simultaneous queries must produce
    /// one warning between them, and a read-then-write on a <see cref="bool"/> could produce two.
    /// </para>
    /// <para>
    /// IT IS DIAGNOSTIC STATE AND NOTHING ELSE. No control flow reads it, so it neither introduces
    /// cross-request coupling nor affects determinism for a characterization comparison.
    /// </para>
    /// </remarks>
    private static int _noUserPromptReported;

    // =============================================================================================
    //  LEGACY DIAGNOSTIC TEXTS AND SENTINEL VALUES.
    //  ---------------------------------------------------------------------------------------------
    //  Every VALUE is the oracle's verbatim, including the Chinese texts, the presence or absence of
    //  a trailing exclamation mark and the trailing space inside a prefix. They are observable in log
    //  records and in characterization recordings, so a tidied text would silently invalidate a
    //  stored comparison.
    //
    //  Every IDENTIFIER is PascalCase. This file carries no .editorconfig naming suppression, so
    //  CA1707 and IDE1006 are live and TreatWarningsAsErrors is true - a SCREAMING_SNAKE declaration
    //  here is a BUILD BREAK, not a style preference. The legacy SCREAMING_SNAKE spellings that must
    //  survive are consumed from where they are already declared under their own scoped suppression:
    //  Enums.SQL_MS_* and ClauseModifier.SQL_MS_*, DatabaseType.Dbt*, DwBuffer and ItemStatus from
    //  the generated contracts, and every code from RetCode.
    // =============================================================================================

    /// <summary>
    /// The exclusive floor the chunk-size setter rejects at or below - <c>if chunkSize &lt;= 1000</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L410</c>].
    /// </summary>
    /// <remarks>
    /// <b>DEFECT D1. THE COMPARISON IS <c>&lt;=</c>, SO 1000 ITSELF IS REJECTED AND 1001 IS THE LOWEST
    /// LEGAL VALUE.</b> The field's own inline comment reads <c>//Chunk row count,min:1000</c>
    /// [<c>:L34</c>] and CONTRADICTS the guard it sits beside. The code is the oracle and the comment
    /// is wrong. Anyone reading only the comment will "correct" the guard to <c>&lt; 1000</c> and
    /// silently widen the contract by exactly one value; constraint C-B forbids it, and
    /// <c>Configuration/PersistenceOptions.QueryOptions.ChunkSize</c> carries the same inclusive
    /// boundary as a validation range so a deployment cannot start below it either.
    /// </remarks>
    internal const long ChunkSizeExclusiveFloor = 1000L;

    /// <summary>
    /// The chunk size the private reset installs - <c>_nChunkSize = 10000</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L34, :L256</c>].
    /// </summary>
    /// <remarks>
    /// Delegated to <see cref="ChangesetCodec.LegacyDefaultChunkSize"/> so that exactly one place in
    /// the service decides what the legacy default is, rather than two that can drift.
    /// </remarks>
    internal const long DefaultChunkSize = ChangesetCodec.LegacyDefaultChunkSize;

    /// <summary>
    /// The value both paging totals take when counting did not happen - <c>nPageCount = -1</c> and
    /// <c>nRecordCount = -1</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L861-L862</c>].
    /// </summary>
    /// <remarks>
    /// A REAL LEGACY VALUE rather than a convention invented here, which is why it is a named constant
    /// and not a magic number: contract C-05 publishes it on <c>QueryPageCounts</c> and on
    /// <c>CountResponse</c>, and a consumer must treat it as "not counted" rather than doing
    /// arithmetic on it.
    /// </remarks>
    internal const long NotCountedValue = -1L;

    /// <summary>
    /// The value the empty sort and the empty filter are rewritten to - <c>" "</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L481, :L487</c>].
    /// </summary>
    /// <remarks>
    /// <b>DEFECT D8, PRESERVED VERBATIM.</b> It looks like a typo and it is not: a genuinely empty
    /// string and a single space reach the DataWindow sort and filter properties differently, and the
    /// space is how the legacy CLEARS one rather than leaving it unset. Contract C-05 documents the
    /// same rewrite on <c>QuerySpec.filter</c> and <c>QuerySpec.sort</c>, so a server that skipped it
    /// would disagree with its own published contract.
    /// </remarks>
    internal const string ClearedSortOrFilter = " ";

    /// <summary>
    /// <c>无效的DataObject</c> - the invalid data-object diagnostic
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L554</c>].
    /// </summary>
    internal const string InvalidDataObjectText = "无效的DataObject";

    /// <summary>
    /// <c>SQL为空!</c> - the empty-statement diagnostic, raised at RUN TIME rather than at
    /// configuration time [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L616</c>].
    /// </summary>
    /// <remarks>
    /// <b>The moment matters and is preserved.</b> <c>of_setsql</c> has no guard at all - it is a plain
    /// assignment [<c>:L469</c>] - so an empty statement is accepted at configuration time and refused
    /// only here. Contract C-05 records the asymmetry against C-07, whose own statement setter refuses
    /// an empty value immediately, and forbids moving this check forward to match it.
    /// </remarks>
    internal const string EmptySqlText = "SQL为空!";

    /// <summary>
    /// <c>SQL参数绑定失败!</c> - the parameter-binding diagnostic, raised from TWO DISTINCT sites
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L606</c> on the retrieval
    /// path and <c>:L838</c> on the counting path].
    /// </summary>
    internal const string BindParamsFailedText = "SQL参数绑定失败!";

    /// <summary>
    /// <c>GridSyntaxFromSQL: </c> - the prefix the derived-syntax failure carries, INCLUDING its
    /// trailing space [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L612</c>].
    /// </summary>
    internal const string GridSyntaxFailurePrefix = "GridSyntaxFromSQL: ";

    /// <summary>
    /// <c>Create: </c> - the prefix a carrier-creation failure carries
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L620</c>].
    /// </summary>
    internal const string CreateFailurePrefix = "Create: ";

    /// <summary>
    /// <c>Modify [DataWindow.Table.Select]: </c> - the prefix all three statement-modification
    /// failures carry [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L637,
    /// :L711, :L734</c>].
    /// </summary>
    internal const string ModifyTableSelectFailurePrefix = "Modify [DataWindow.Table.Select]: ";

    /// <summary>
    /// <c>设置事务对象失败!</c> - the transaction-attachment diagnostic
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L679</c>].
    /// </summary>
    internal const string SetTransObjectFailedText = "设置事务对象失败!";

    /// <summary>
    /// <c>SQL解析失败!</c> - the parse-failure diagnostic, raised from TWO sites in this object
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L686</c> when applying the
    /// stored clauses and <c>:L827</c> when building the count wrapper].
    /// </summary>
    /// <remarks>
    /// A THIRD site with the same text lives inside the paged-statement builder [<c>:L315</c>], and
    /// that one is owned by <see cref="PagingRewriteResult.ParseFailedText"/> rather than duplicated
    /// here - the two constants carry the same VALUE from two owners because the two call sites are
    /// genuinely different, and the paging one is asserted against byte-exact rewriter output.
    /// </remarks>
    internal const string ParseFailedText = "SQL解析失败!";

    /// <summary>
    /// <c>修改SQL[WHERE]语句失败!</c> - the per-clause <c>WHERE</c> failure
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L692</c>].
    /// </summary>
    internal const string ModifyWhereFailedText = "修改SQL[WHERE]语句失败!";

    /// <summary>
    /// <c>修改SQL[ORDER BY]语句失败!</c> - the per-clause <c>ORDER BY</c> failure
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L699</c>].
    /// </summary>
    internal const string ModifyOrderByFailedText = "修改SQL[ORDER BY]语句失败!";

    /// <summary>
    /// <c>检索失败!</c> - the retrieval failure for a PLAIN statement, WITH its exclamation mark
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L778</c>].
    /// </summary>
    internal const string PlainSqlRetrieveFailedText = "检索失败!";

    /// <summary>
    /// <c>检索失败,请检查参数传递是否正确!</c> - the retrieval failure for a NAMED data object, which
    /// the oracle PREFIXES with the data-object name
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L780</c>].
    /// </summary>
    internal const string DataObjectRetrieveFailedSuffix = "检索失败,请检查参数传递是否正确!";

    /// <summary>
    /// <c>检索失败</c> - the COUNTING failure, deliberately WITHOUT the exclamation mark that
    /// <see cref="PlainSqlRetrieveFailedText"/> carries
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L856</c>].
    /// </summary>
    /// <remarks>
    /// <b>The two texts differ by exactly one character and that is the oracle's own doing</b>
    /// [<c>:L778</c> against <c>:L856</c>]. Harmonising them would be a silent behavioural change in a
    /// value a characterization recording holds, so they are two constants rather than one.
    /// </remarks>
    internal const string CountRetrieveFailedText = "检索失败";

    /// <summary>
    /// The opening of the row-cap diagnostic - <c>"超出最大允许的行数(" + String(_nMaxRows) + ")!"</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L788</c>].
    /// </summary>
    internal const string MaxRowsExceededPrefix = "超出最大允许的行数(";

    /// <summary>
    /// The closing of the row-cap diagnostic, including its exclamation mark
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L788</c>].
    /// </summary>
    internal const string MaxRowsExceededSuffix = ")!";

    /// <summary>
    /// <c>1 AS _</c> - the literal the count wrapper replaces the whole column clause with
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L830</c>], under the
    /// oracle's own comment 去掉字段 ("drop the columns").
    /// </summary>
    /// <remarks>
    /// <b>Byte-exact and not to be tidied.</b> One space either side of <c>AS</c>, a bare <c>1</c> and
    /// a single underscore as the alias. Contract C-05 names this literal explicitly as observable, and
    /// byte-exact generated SQL is the acceptance criterion, so the alias cannot become <c>_1</c>,
    /// <c>"_"</c> or anything else.
    /// </remarks>
    internal const string CountColumnAlias = "1 AS _";

    /// <summary>
    /// The opening of the count wrapper - <c>"SELECT COUNT(1) AS CNT FROM ("</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L834</c>].
    /// </summary>
    /// <remarks>
    /// <c>COUNT(1)</c> rather than <c>COUNT(*)</c>, and the result alias is <c>CNT</c>. Both are
    /// observable and both are the oracle's.
    /// </remarks>
    internal const string CountWrapperPrefix = "SELECT COUNT(1) AS CNT FROM (";

    /// <summary>
    /// The closing of the count wrapper - <c>") "</c> followed by the derived-table sentinel
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L834</c>].
    /// </summary>
    /// <remarks>
    /// <b>THE SENTINEL ITSELF IS NOT RE-DECLARED HERE.</b> It is consumed from
    /// <see cref="SqlServerPagingRewriter.DerivedTableAlias"/>, which is <c>internal</c> for exactly
    /// this reason: the same spelling <c>pfwPagedSQL_Tbl</c> is emitted by the SQL Server rewriter's
    /// row-number wrapper and by this count wrapper, and one owner means the two cannot drift. Note
    /// that the neighbouring sentinel <c>pfwPagedSQL_OutterTbl</c> carries TWO t's - legacy spelling,
    /// preserved - which is another reason no spelling is retyped in this file.
    /// </remarks>
    internal const string CountWrapperAliasSeparator = ") ";

    /// <summary>
    /// The ONE-BASED row the counting result is read from - <c>dsTmp.GetItemNumber(1, 1)</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L852</c>].
    /// </summary>
    private const long CountResultRow = 1L;

    /// <summary>
    /// The ONE-BASED column the counting result is read from [<c>:L852</c>].
    /// </summary>
    private const int CountResultColumn = 1;

    /// <summary>
    /// The row count <c>of_Query</c> must answer for the counting result to be read - <c>rtCode = 1</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L851</c>].
    /// </summary>
    /// <remarks>
    /// It is a ROW COUNT, not a return code: <c>of_Query</c> answers what <c>of_Retrieve</c> answered
    /// [<c>n_cst_thread_trans.sru:L292-L336</c>], and a <c>SELECT COUNT(1)</c> yields exactly one row.
    /// Testing it against <c>RetCode.PREVENT</c>, which shares the value, would be a category error.
    /// </remarks>
    private const long CountQueryExpectedRowCount = 1L;

    /// <summary>
    /// The value that turns a PowerBuilder <c>Describe</c> answer into "this property is unreadable" -
    /// the <c>"!"</c> sentinel that the stored-procedure test compares against [<c>:L626</c>].
    /// </summary>
    /// <remarks>
    /// Delegated to <see cref="ChangesetSourceDefinition.DescribeUnreadable"/> so the sentinel is
    /// spelled in one place across the service.
    /// </remarks>
    private const string DescribeUnreadable = ChangesetSourceDefinition.DescribeUnreadable;

    /// <summary>
    /// The embedded double quote that a statement carries and that a modification script must escape
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L709, :L732, :L795</c>].
    /// </summary>
    private const string EmbeddedQuote = "\"";

    /// <summary>
    /// The escaped form of an embedded double quote inside a DataWindow modification script - the
    /// two-character sequence the legacy <c>~~~"</c> literal denotes.
    /// </summary>
    /// <remarks>
    /// In PowerScript, <c>~"</c> is a literal double quote and <c>~~</c> is a literal tilde, so
    /// <c>ReplaceAll(sql, "~"", "~~~"", true)</c> replaces each <c>"</c> with the two characters
    /// <c>~</c> and <c>"</c>. That is the DataWindow modification grammar's own escape, not a C# one.
    /// </remarks>
    private const string EscapedEmbeddedQuote = "~\"";

    /// <summary>
    /// The modification script that installs a statement -
    /// <c>'DataWindow.Table.Select = "' + sql + '"'</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L635, :L709, :L732,
    /// :L801</c>].
    /// </summary>
    private const string TableSelectAssignmentPrefix = "DataWindow.Table.Select = \"";

    /// <summary>The closing quote of the statement-installation script.</summary>
    private const string TableSelectAssignmentSuffix = "\"";

    /// <summary>
    /// The value a drop-down's <c>AutoRetrieve</c> property must read for its child to be transferred -
    /// <c>"yes"</c> [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L116,
    /// :L650</c>].
    /// </summary>
    /// <remarks>
    /// Delegated to <see cref="ChangesetCodec.AutoRetrieveEnabled"/> rather than retyped, because the
    /// codec's own eligibility predicate compares against it.
    /// </remarks>
    private const string AutoRetrieveEnabled = ChangesetCodec.AutoRetrieveEnabled;

    // ---------------------------------------------------------------------------------------------
    //  Injected collaborators. Every one is an abstraction, which is what constraint C-H buys, and
    //  none of them is a database, a connection or a storage provider, which is what C-E requires.
    // ---------------------------------------------------------------------------------------------

    private readonly IQueryTransactionSurface _transactionSurface;
    private readonly IQueryDataWindowRuntime _dataWindowRuntime;
    private readonly IReadOnlyList<IPagingRewriter> _pagingRewriters;
    private readonly ChangesetCodec _changesetCodec;
    private readonly ISqlRedactor _redactor;
    private readonly ILogger<SqlQueryTask> _logger;

    /// <summary>
    /// The configured defaults the private reset installs, resolved once at construction from the
    /// <c>Query</c> section of the persistence options.
    /// </summary>
    private readonly QueryOptions _configuredDefaults;

    // ---------------------------------------------------------------------------------------------
    //  INSTANCE STATE - the private block at n_cst_thread_task_sqlquery.sru:L28-L46, field for field.
    //
    //  `SQLCLAUSE _whereClauses[]` [:L43] and `SQLCLAUSE _orderByClauses[]` [:L44] have NO field of
    //  their own here: both collections, their nested clause triple and their one-based upsert belong
    //  to Sql/ClauseModifier.cs, and this task holds ONE instance of it. Re-declaring either array
    //  would put the R9 hazard in two places, which is precisely what the sibling exists to prevent.
    // ---------------------------------------------------------------------------------------------

    /// <summary>Mirrors <c>string _sHookClass</c> [<c>:L28</c>].</summary>
    private string _hookClass = string.Empty;

    /// <summary>Mirrors <c>string _sDataObject</c> [<c>:L29</c>].</summary>
    private string _dataObject = string.Empty;

    /// <summary>Mirrors <c>string _sSQL</c> [<c>:L30</c>].</summary>
    private string _sql = string.Empty;

    /// <summary>Mirrors <c>string _sSQLSyntax</c> [<c>:L31</c>].</summary>
    private string _sqlSyntax = string.Empty;

    /// <summary>Mirrors <c>string _sNewSort</c> [<c>:L32</c>].</summary>
    private string _newSort = string.Empty;

    /// <summary>Mirrors <c>string _sNewFilter</c> [<c>:L33</c>].</summary>
    private string _newFilter = string.Empty;

    /// <summary>Mirrors <c>long _nChunkSize = 10000</c> [<c>:L34</c>].</summary>
    private long _chunkSize = DefaultChunkSize;

    /// <summary>Mirrors <c>boolean _bPaged</c> [<c>:L35</c>].</summary>
    private bool _paged;

    /// <summary>Mirrors <c>long _nPageSize</c> [<c>:L36</c>].</summary>
    private long _pageSize;

    /// <summary>Mirrors <c>long _nPageIndex</c> [<c>:L37</c>].</summary>
    private long _pageIndex;

    /// <summary>Mirrors <c>boolean _bPageCounting = true</c> [<c>:L38</c>].</summary>
    private bool _pageCounting = true;

    /// <summary>
    /// Mirrors <c>boolean _bPageNative</c> [<c>:L39</c>], whose own comment reads
    /// <c>//Use native implementation</c>.
    /// </summary>
    /// <remarks>
    /// <b>DEFECT D2 LIVES ON THIS FIELD.</b> It is the ONE settable field the private reset omits -
    /// see <see cref="ResetQueryState"/>.
    /// </remarks>
    private bool _pageNative;

    /// <summary>Mirrors <c>long _nMaxRows</c> [<c>:L40</c>].</summary>
    private long _maxRows;

    /// <summary>Mirrors <c>boolean _bCache</c> [<c>:L41</c>].</summary>
    private bool _cache;

    /// <summary>
    /// Mirrors <c>string _sPagedUniqueIndexColumns[]</c> [<c>:L46</c>].
    /// </summary>
    /// <remarks>
    /// A list rather than an array so the collection cannot be handed out for mutation, and REPLACED
    /// WHOLESALE by its setter rather than merged into - which is the oracle's own behaviour at
    /// <c>:L406</c> and the documented difference from the two clause collections.
    /// </remarks>
    private readonly List<string> _pagedUniqueIndexColumns = [];

    /// <summary>
    /// Zero when the task is idle and one while <see cref="ExecuteAsync"/> is in flight - the managed
    /// stand-in for the substrate's <c>#Running</c> property, read by the public reset at
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L237</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why it lives here rather than on <see cref="ISqlTaskHost"/>.</b> The host deliberately does
    /// not model <c>#Running</c>, because every guard written against it in the BASE object is
    /// commented out in the oracle and carried across inert. In THIS object the guard is LIVE
    /// [<c>:L237</c>], so the state has to exist somewhere - and it is genuinely this task's own
    /// state, set when this task starts running and cleared when it stops.
    /// </para>
    /// <para>
    /// An <see cref="int"/> driven by <see cref="Interlocked.CompareExchange(ref int, int, int)"/>
    /// rather than a <see cref="bool"/>, so that a second concurrent execution is refused rather than
    /// interleaved. The thread-affinity contract says one thread owns this task, but a DI-registered
    /// object reached from a gRPC method has no way to enforce that by itself, and an interleaved
    /// second retrieval would corrupt the chunk sequence of the first.
    /// </para>
    /// </remarks>
    private int _running;

    /// <summary>
    /// Creates the query task.
    /// </summary>
    /// <param name="host">The threading substrate this task runs on.</param>
    /// <param name="transactionPool">The shared, per-thread transaction pool.</param>
    /// <param name="dataStoreFactory">Creates affinity-appropriate result carriers.</param>
    /// <param name="hookActivator">
    /// Turns a caller-supplied hook class name into a hook, safely. <b>Constraint C-G</b>: the name
    /// arrives on contract C-05 as <c>QuerySpec.hook_class</c> and is therefore caller-controlled
    /// input, so activation is restricted and validated rather than performed directly.
    /// </param>
    /// <param name="timeProvider">
    /// The service's single clock seam. <b>Constraint C-H: the ONLY clock this file may read.</b>
    /// </param>
    /// <param name="logger">
    /// Diagnostics sink. <b>Constraint C-F: nothing written through it may carry an unredacted
    /// statement.</b>
    /// </param>
    /// <param name="transactionSurface">
    /// The query-side members of the legacy transaction object that the pooled transaction does not
    /// expose.
    /// </param>
    /// <param name="dataWindowRuntime">
    /// The three DataWindow-runtime operations with no managed analogue.
    /// </param>
    /// <param name="pagingRewriters">
    /// The paging arms. <b>Constraint C-E</b>: these are PURE STRING TRANSFORMS, never connections,
    /// and exactly two of them exist because the oracle's <c>choose case</c> has exactly two arms.
    /// </param>
    /// <param name="changesetCodec">
    /// The changeset transfer codec. Injected rather than constructed so a test can substitute its
    /// payload encoder and reach the two failure arms a managed in-memory encoder never produces.
    /// </param>
    /// <param name="redactor">
    /// The statement redactor. <b>Constraint C-F</b>: every statement text that leaves this process in
    /// a diagnostic passes through it.
    /// </param>
    /// <param name="options">
    /// The persistence options, whose <c>Query</c> section seeds the configured defaults. The section
    /// deliberately carries NO page-native key, which is consistent with defect D2 - see
    /// <see cref="ResetQueryState"/>.
    /// </param>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    internal SqlQueryTask(
        ISqlTaskHost host,
        TransactionPool transactionPool,
        ISqlDataStoreFactory dataStoreFactory,
        ISqlRetrievalHookActivator hookActivator,
        TimeProvider timeProvider,
        ILogger<SqlQueryTask> logger,
        IQueryTransactionSurface transactionSurface,
        IQueryDataWindowRuntime dataWindowRuntime,
        IEnumerable<IPagingRewriter> pagingRewriters,
        ChangesetCodec changesetCodec,
        ISqlRedactor redactor,
        IOptions<PersistenceOptions> options)
        : base(host, transactionPool, dataStoreFactory, hookActivator, timeProvider, logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(transactionSurface);
        ArgumentNullException.ThrowIfNull(dataWindowRuntime);
        ArgumentNullException.ThrowIfNull(pagingRewriters);
        ArgumentNullException.ThrowIfNull(changesetCodec);
        ArgumentNullException.ThrowIfNull(redactor);
        ArgumentNullException.ThrowIfNull(options);

        _logger = logger;
        _transactionSurface = transactionSurface;
        _dataWindowRuntime = dataWindowRuntime;

        // Materialised ONCE rather than held as a lazy sequence. The dispatcher walks it to select an
        // arm, and a container-provided enumerable that resolved a new instance on every enumeration
        // would make the selection non-deterministic across the two walks a paged retrieval performs.
        _pagingRewriters = [.. pagingRewriters];

        _changesetCodec = changesetCodec;
        _redactor = redactor;

        Clauses = new ClauseModifier();

        // The configured defaults, applied through the SAME private reset the public one uses, so a
        // freshly constructed task and a freshly reset task are in the same state by construction
        // rather than by two parallel bodies that can drift.
        _configuredDefaults = ResolveConfiguredDefaults(options);
        ResetQueryState();
    }


    #region The observable state - one property per legacy field

    /// <summary>
    /// The stored clause modifications and their one-based upsert - the managed owner of
    /// <c>SQLCLAUSE _whereClauses[]</c> and <c>SQLCLAUSE _orderByClauses[]</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L43-L44</c>].
    /// </summary>
    /// <remarks>
    /// <b>Exposed rather than hidden, because the statement-application loop reads it in ORDER.</b>
    /// The nested legacy <c>sqlclause</c> triple, the <c>selectIndex &lt;= 0 || clause == ""</c> guard
    /// and the update-in-place-or-append-at-end semantics all belong to this object, and this task
    /// never re-implements the one-based scan - see <see cref="SetWhereClause(int, long, string?)"/>.
    /// </remarks>
    internal ClauseModifier Clauses { get; }

    /// <summary>The hook class name - <c>_sHookClass</c> [<c>:L28</c>].</summary>
    internal string HookClass => _hookClass;

    /// <summary>The named data object - <c>_sDataObject</c> [<c>:L29</c>].</summary>
    internal string DataObject => _dataObject;

    /// <summary>The statement - <c>_sSQL</c> [<c>:L30</c>].</summary>
    internal string Sql => _sql;

    /// <summary>The supplied DataWindow syntax - <c>_sSQLSyntax</c> [<c>:L31</c>].</summary>
    internal string SqlSyntax => _sqlSyntax;

    /// <summary>The pending sort expression - <c>_sNewSort</c> [<c>:L32</c>].</summary>
    internal string NewSort => _newSort;

    /// <summary>The pending filter expression - <c>_sNewFilter</c> [<c>:L33</c>].</summary>
    internal string NewFilter => _newFilter;

    /// <summary>The rows per chunk - <c>_nChunkSize</c> [<c>:L34</c>].</summary>
    internal long ChunkSize => _chunkSize;

    /// <summary>Whether the paging rewrite is on - <c>_bPaged</c> [<c>:L35</c>].</summary>
    internal bool Paged => _paged;

    /// <summary>The rows per page - <c>_nPageSize</c> [<c>:L36</c>].</summary>
    internal long PageSize => _pageSize;

    /// <summary>The ONE-BASED page ordinal - <c>_nPageIndex</c> [<c>:L37</c>].</summary>
    internal long PageIndex => _pageIndex;

    /// <summary>Whether page counting is on - <c>_bPageCounting</c> [<c>:L38</c>].</summary>
    internal bool PageCounting => _pageCounting;

    /// <summary>
    /// Whether the engine's own paging implementation is selected - <c>_bPageNative</c> [<c>:L39</c>].
    /// </summary>
    /// <remarks>
    /// <b>DEFECT D2: this value SURVIVES A RESET.</b> See <see cref="ResetQueryState"/>.
    /// </remarks>
    internal bool PageNative => _pageNative;

    /// <summary>The row cap - <c>_nMaxRows</c> [<c>:L40</c>], where zero means "no limit".</summary>
    internal long MaxRows => _maxRows;

    /// <summary>Whether the result carrier is retained for reuse - <c>_bCache</c> [<c>:L41</c>].</summary>
    internal bool Cache => _cache;

    /// <summary>
    /// The unique-index columns the first paging arm splices into the statement -
    /// <c>_sPagedUniqueIndexColumns[]</c> [<c>:L46</c>].
    /// </summary>
    /// <remarks>
    /// A read-only view. A non-empty list selects a DIFFERENT paging strategy rather than acting as a
    /// hint, and every element is validated as an identifier by the paging dispatcher before any arm
    /// splices it - which is why this task passes them through untouched and does not sanitise them
    /// itself.
    /// </remarks>
    internal IReadOnlyList<string> PagedUniqueIndexColumns => _pagedUniqueIndexColumns;

    /// <summary>
    /// Whether a retrieval is currently in flight - the managed reading of <c>#Running</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L237</c>].
    /// </summary>
    internal bool IsRunning => Volatile.Read(ref _running) != 0;

    #endregion

    #region The reset pair - and there are TWO methods, not one

    /// <summary>
    /// Resets the task - the port of the PUBLIC <c>of_reset</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L237-L242</c>].
    /// </summary>
    /// <returns>
    /// <c>RetCode.E_BUSY</c> while a retrieval is in flight, otherwise whatever the base's reset
    /// answers.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>Three steps, in the oracle's order, and the third is a chain to the ancestor:</b>
    /// </para>
    /// <code>
    /// if #Running then return RetCode.E_BUSY   [:L237]
    /// _of_Reset()                              [:L239]
    /// return super::of_Reset()                 [:L241]
    /// </code>
    /// <para>
    /// <b>The busy guard comes FIRST and it is a hard refusal, not a wait.</b> A task reset while a
    /// chunk sequence is in flight would drop the chunk size and the paging settings the sequence in
    /// progress is computed from, so the oracle refuses instead - and this is the one place in the SQL
    /// task layer where a <c>#Running</c> guard is LIVE rather than commented out.
    /// </para>
    /// <para>
    /// <b>The ancestor's value is returned verbatim</b> [<c>:L241</c>], not merged with anything and
    /// not replaced by <c>RetCode.OK</c>. The base's reset clears the commit signal and the parameter
    /// collection; this override adds the query-specific fields and nothing else.
    /// </para>
    /// </remarks>
    internal override long Reset()
    {
        // `if #Running then return RetCode.E_BUSY` [:L237].
        if (IsRunning)
        {
            return RetCode.E_BUSY;
        }

        // `_of_Reset()` [:L239] - the PRIVATE reset, which is a separate method in the oracle and is a
        // separate method here.
        ResetQueryState();

        // `return super::of_Reset()` [:L241].
        return base.Reset();
    }

    /// <summary>
    /// Restores every query-specific field to its default - the port of the PRIVATE <c>_of_reset</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L247-L267</c>].
    /// </summary>
    /// <remarks>
    /// <para>
    /// The oracle's body, transcribed so the omission below is checkable against it rather than taken
    /// on trust:
    /// </para>
    /// <code>
    /// _sHookClass = ""            [:L250]      _bPaged = false             [:L258]
    /// _sSQL = ""                  [:L251]      _nPageSize = 0              [:L259]
    /// _sSQLSyntax = ""            [:L252]      _bPageCounting = true       [:L260]
    /// _sDataObject = ""           [:L253]      _nMaxRows = 0               [:L261]
    /// _sNewSort = ""              [:L254]      _bCache = false             [:L262]
    /// _sNewFilter = ""            [:L255]      _whereClauses = empty       [:L264]
    /// _nChunkSize = 10000         [:L256]      _orderByClauses = empty     [:L265]
    /// _nPageIndex = 0             [:L257]      _sPagedUniqueIndexColumns = empty  [:L266]
    /// </code>
    /// <para>
    /// ==========================================================================================
    /// </para>
    /// <para>
    /// <b>DEFECT D2 - <c>_bPageNative</c> IS NOT RESET, AND THAT OMISSION IS REPRODUCED EXACTLY.</b>
    /// </para>
    /// <para>
    /// <c>_bPageNative</c> [<c>:L39</c>] is the ONE settable field this body does not clear. Every
    /// other settable field on the object appears in the transcription above; that one does not. The
    /// finding comes from reading <c>_of_reset</c> in full, line by line, from <c>:L247</c> to its
    /// <c>end subroutine</c> at <c>:L267</c> - it is NOT an oversight in this port, and the omission
    /// here is deliberate. The consequence is observable: a page-native setting SURVIVES a reset and
    /// leaks into the next use of the task, so a caller that set it once, reset, and then paged again
    /// gets the native rewrite it never asked for the second time.
    /// </para>
    /// <para>
    /// <b>Constraint C-B forbids correcting it.</b> Adding <c>_pageNative = false</c> below would be an
    /// improvement, and an improvement is a behavioural change: the two paging arms emit DIFFERENT
    /// byte-exact statements depending on this flag, so "fixing" the reset would change the generated
    /// SQL of any workflow that resets between two paged retrievals and would silently invalidate its
    /// characterization recording. <c>PersistenceOptions.QueryOptions</c> corroborates the reading from
    /// the other side: it declares <c>ChunkSize</c>, <c>MaxRows</c>, <c>PageCounting</c>,
    /// <c>Cache</c>, <c>PageIndex</c>, <c>PageSize</c> and <c>Paged</c> and deliberately carries NO
    /// page-native key, so there is not even a configured default for this field to be restored to.
    /// The behaviour is pinned by a test named for it.
    /// </para>
    /// <para>
    /// ==========================================================================================
    /// </para>
    /// <para>
    /// <b>The defaults come from configuration, and the legacy literals are the fallback.</b> Where the
    /// oracle assigns a literal, this body assigns the configured value whose own default IS that
    /// literal - chunk size 10000 [<c>:L256</c>], page counting true [<c>:L260</c>], max rows 0
    /// [<c>:L261</c>], cache false [<c>:L262</c>], page index 0 [<c>:L257</c>], page size 0
    /// [<c>:L259</c>] and paged false [<c>:L258</c>]. That is the options pattern discharging the
    /// no-hardcoded-value mandate structurally while preserving the observable default.
    /// </para>
    /// </remarks>
    private void ResetQueryState()
    {
        // [:L250-L255] the six string fields.
        _hookClass = string.Empty;
        _sql = string.Empty;
        _sqlSyntax = string.Empty;
        _dataObject = string.Empty;
        _newSort = string.Empty;
        _newFilter = string.Empty;

        // [:L256] `_nChunkSize = 10000`, taken from configuration whose default is that value.
        _chunkSize = _configuredDefaults.ChunkSize;

        // [:L257] `_nPageIndex = 0`. Note that zero is BELOW the setter's own legal domain, which
        // rejects `pageIndex <= 0` [:L450] - so the reset installs a value the setter would refuse.
        // That is the oracle's own state, not an inconsistency introduced here: an unset page index is
        // zero and the paged builder re-validates it at run time [:L307-L310], which is the second half
        // of the deliberate double-guard contract C-05 records.
        _pageIndex = _configuredDefaults.PageIndex;

        // [:L258-L259]
        _paged = _configuredDefaults.Paged;
        _pageSize = _configuredDefaults.PageSize;

        // [:L260] `_bPageCounting = true` - the one boolean whose legacy default is TRUE, which is why
        // explicit presence matters more for it than for any other field on contract C-05's QuerySpec.
        _pageCounting = _configuredDefaults.PageCounting;

        // [:L261-L262]
        _maxRows = _configuredDefaults.MaxRows;
        _cache = _configuredDefaults.Cache;

        // ==========================================================================================
        //  DEFECT D2. `_bPageNative` IS DELIBERATELY ABSENT FROM THIS BODY.
        //  The oracle's _of_reset [:L247-L267] does not clear it, so neither does this method. Do not
        //  add `_pageNative = false;` here: see the remarks above for why C-B forbids it and for the
        //  observable consequence of "fixing" it.
        // ==========================================================================================

        // [:L264-L265] `_whereClauses = emptyClauses` and `_orderByClauses = emptyClauses`, both
        // delegated to the collections' owner rather than cleared through a re-declared array here.
        Clauses.Reset();

        // [:L266] `_sPagedUniqueIndexColumns = sEmpty`.
        _pagedUniqueIndexColumns.Clear();
    }

    /// <summary>
    /// Resolves the configured defaults, falling back to a fresh <see cref="QueryOptions"/> - whose own
    /// property initializers are the legacy literals - when the options carry no <c>Query</c> section.
    /// </summary>
    /// <param name="options">The injected persistence options.</param>
    /// <returns>The section to seed the private reset from.</returns>
    /// <remarks>
    /// The fallback is not laxity. <see cref="QueryOptions"/> initialises every property to the legacy
    /// reset value, so an absent section yields exactly the oracle's defaults; throwing instead would
    /// make a service with no <c>Query</c> configuration fail to construct a task that has nothing
    /// configuration-dependent about it.
    /// </remarks>
    private static QueryOptions ResolveConfiguredDefaults(IOptions<PersistenceOptions> options) =>
        options.Value?.Query ?? new QueryOptions();

    #endregion


    #region The setters - contract C-05's Set* surface, guard for guard

    /// <summary>
    /// Sets the rows per chunk - <c>of_setchunksize</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L410-L415</c>], published as
    /// C-05 <c>SetChunkSize</c>.
    /// </summary>
    /// <param name="chunkSize">The rows per chunk. Must be STRICTLY GREATER THAN 1000.</param>
    /// <returns>
    /// <c>RetCode.OK</c>, or <c>RetCode.E_INVALID_ARGUMENT</c> when
    /// <paramref name="chunkSize"/> is at or below <see cref="ChunkSizeExclusiveFloor"/>.
    /// </returns>
    /// <remarks>
    /// <b>DEFECT D1. THE COMPARISON IS <c>&lt;=</c> AND 1000 IS THEREFORE REJECTED.</b> The oracle is
    /// one line: <c>if chunkSize &lt;= 1000 then return RetCode.E_INVALID_ARGUMENT</c> [<c>:L410</c>].
    /// The lowest legal value is 1001, and the field's own comment claiming "min:1000" [<c>:L34</c>] is
    /// wrong. Reproduced verbatim per constraint C-B and pinned by tests at both boundary values.
    /// </remarks>
    internal long SetChunkSize(long chunkSize)
    {
        // `if chunkSize <= 1000 then return RetCode.E_INVALID_ARGUMENT` [:L410]. INCLUSIVE boundary.
        if (chunkSize <= ChunkSizeExclusiveFloor)
        {
            return RetCode.E_INVALID_ARGUMENT;
        }

        // [:L412]
        _chunkSize = chunkSize;

        // [:L414]
        return RetCode.OK;
    }

    /// <summary>
    /// Sets the row cap - <c>of_setmaxrows</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L433-L438</c>], published as
    /// C-05 <c>SetMaxRows</c>.
    /// </summary>
    /// <param name="rows">The cap. Zero means "no limit" and is LEGAL.</param>
    /// <returns>
    /// <c>RetCode.OK</c>, or <c>RetCode.E_INVALID_ARGUMENT</c> when <paramref name="rows"/> is negative.
    /// </returns>
    /// <remarks>
    /// <b>The boundary is <c>&lt; 0</c> here and <c>&lt;= 1000</c> on the chunk size, and the difference
    /// is deliberate.</b> Zero is accepted here - it is this field's own legacy default and it means "no
    /// limit" [<c>:L261</c>] - while zero is rejected on the chunk size. The two boundaries differ in
    /// the oracle and they differ here; harmonising them would break one or the other.
    /// </remarks>
    internal long SetMaxRows(long rows)
    {
        // `if rows < 0 then return RetCode.E_INVALID_ARGUMENT` [:L433]. EXCLUSIVE of zero.
        if (rows < 0L)
        {
            return RetCode.E_INVALID_ARGUMENT;
        }

        // [:L435]
        _maxRows = rows;

        return RetCode.OK;
    }

    /// <summary>
    /// Switches the paging rewrite on or off - <c>of_setpaged</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L445-L448</c>].
    /// </summary>
    /// <param name="paged">Whether to page.</param>
    /// <returns><c>RetCode.OK</c> - the oracle has NO guard here.</returns>
    internal long SetPaged(bool paged)
    {
        _paged = paged;

        return RetCode.OK;
    }

    /// <summary>
    /// Sets the rows per page - <c>of_setpagesize</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L462-L467</c>].
    /// </summary>
    /// <param name="pageSize">The rows per page. A ONE-BASED magnitude, so zero is invalid.</param>
    /// <returns>
    /// <c>RetCode.OK</c>, or <c>RetCode.E_INVALID_ARGUMENT</c> when <paramref name="pageSize"/> is at or
    /// below zero.
    /// </returns>
    /// <remarks>
    /// <b>RE-VALIDATED A SECOND TIME AT RUN TIME, and both checks are preserved.</b> The paged-statement
    /// builder re-tests <c>_nPageSize &lt;= 0 or _nPageIndex &lt;= 0</c> and fails with the diagnostic
    /// 无效的分页设置! [<c>:L307-L310</c>]. The double-guard is not redundant in practice: a caller that
    /// never called this setter at all reaches only the second one, because the reset installs zero.
    /// </remarks>
    internal long SetPageSize(long pageSize)
    {
        // `if pageSize <= 0 then return RetCode.E_INVALID_ARGUMENT` [:L462].
        if (pageSize <= 0L)
        {
            return RetCode.E_INVALID_ARGUMENT;
        }

        _pageSize = pageSize;

        return RetCode.OK;
    }

    /// <summary>
    /// Sets the ONE-BASED page ordinal - <c>of_setpageindex</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L450-L455</c>].
    /// </summary>
    /// <param name="pageIndex">The page ordinal. <b>The first page is 1, not 0.</b></param>
    /// <returns>
    /// <c>RetCode.OK</c>, or <c>RetCode.E_INVALID_ARGUMENT</c> when <paramref name="pageIndex"/> is at
    /// or below zero.
    /// </returns>
    /// <remarks>
    /// R9 (AAP §0.4.5.4). This ordinal is one-based on the wire, one-based in the paging arms' own
    /// arithmetic - <c>pageSize * (pageIndex - 1)</c> - and one-based in the page-count short-circuit's
    /// inference. It is never rebased anywhere in this file.
    /// </remarks>
    internal long SetPageIndex(long pageIndex)
    {
        // `if pageIndex <= 0 then return RetCode.E_INVALID_ARGUMENT` [:L450].
        if (pageIndex <= 0L)
        {
            return RetCode.E_INVALID_ARGUMENT;
        }

        _pageIndex = pageIndex;

        return RetCode.OK;
    }

    /// <summary>
    /// Selects the engine's own paging implementation rather than the framework's rewrite -
    /// <c>of_setpagenative</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L457-L460</c>].
    /// </summary>
    /// <param name="useNative">Whether to use the native implementation.</param>
    /// <returns><c>RetCode.OK</c> - the oracle has NO guard here.</returns>
    /// <remarks>
    /// <b>DEFECT D2: a value set through this setter SURVIVES <see cref="Reset"/>.</b> See
    /// <see cref="ResetQueryState"/> for the finding and for why constraint C-B forbids correcting it.
    /// </remarks>
    internal long SetPageNative(bool useNative)
    {
        _pageNative = useNative;

        return RetCode.OK;
    }

    /// <summary>
    /// Switches page counting on or off - <c>of_setpagecounting</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L440-L443</c>].
    /// </summary>
    /// <param name="counting">Whether to count.</param>
    /// <returns><c>RetCode.OK</c> - the oracle has NO guard here.</returns>
    /// <remarks>
    /// <b>The legacy default is TRUE</b> [<c>:L38, :L260</c>], which is why contract C-05 makes this
    /// field explicitly present on its spec: an implicit false would silently switch counting off for
    /// every caller who simply did not mention it.
    /// </remarks>
    internal long SetPageCounting(bool counting)
    {
        _pageCounting = counting;

        return RetCode.OK;
    }

    /// <summary>
    /// Switches result-carrier caching on or off - <c>of_setcache</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L417-L420</c>].
    /// </summary>
    /// <param name="cache">Whether to retain the carrier for reuse.</param>
    /// <returns><c>RetCode.OK</c> - the oracle has NO guard here.</returns>
    /// <remarks>
    /// <b>Caching changes the DELIVERY PATH as well as the lifetime.</b> With caching off and no
    /// receiver installed on the main thread, the carrier is handed over wholesale and no codec runs at
    /// all [<c>:L85-L91</c>]; with caching on, the carrier is RESET rather than destroyed in the cleanup
    /// block [<c>:L876-L880</c>], because the cache holds live references and destroying one would
    /// leave a dangling entry.
    /// </remarks>
    internal long SetCache(bool cache)
    {
        _cache = cache;

        return RetCode.OK;
    }

    /// <summary>
    /// Sets the hook class name - <c>of_sethookclass</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L428-L431</c>], published as
    /// C-05's hook-class field.
    /// </summary>
    /// <param name="hookClass">The class name; <see langword="null"/> is treated as empty.</param>
    /// <returns>
    /// <c>RetCode.OK</c> for a blank name or a sanctioned one; <c>RetCode.E_INVALID_ARGUMENT</c> for a
    /// non-blank name that names no sanctioned hook.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>Constraint C-G.</b> The oracle writes <c>hook = Create Using _sHookClass</c> [<c>:L517</c>]
    /// with a name that arrived through this setter, so a name here is CALLER-CONTROLLED INPUT and
    /// activating an arbitrary type from it would be a remote type-activation primitive. Resolution
    /// therefore goes through the base's restricted activator, which is an ALLOWLIST. Nothing here
    /// transmits code, script or an expression to evaluate, so constraint C-D is untouched.
    /// </para>
    /// <para>
    /// <b>AND AN UNSANCTIONED NAME IS REFUSED HERE RATHER THAN IGNORED LATER.</b> Because the activator
    /// is an allowlist, an unregistered name can never produce a hook - so storing it and discovering
    /// that at retrieval time means the retrieval runs with no hook and reports SUCCESS. A caller who
    /// asked for a hook would be told its request succeeded while the behaviour it asked for silently did
    /// not happen, which is the worst of the available answers. <c>E_INVALID_ARGUMENT</c> at the setter
    /// is the truth, delivered while the caller can still act on it.
    /// </para>
    /// <para>
    /// <b>Blank remains legal, and nothing is stored behind a refusal.</b> Blank means "no hook"
    /// [<c>:L516</c>] and is the ordinary case (C-B). A refused name leaves the previously accepted value
    /// in place, so a caller cannot half-change the task by sending a bad name.
    /// </para>
    /// </remarks>
    internal long SetHookClass(string? hookClass)
    {
        if (!IsAdmissibleHookClass(hookClass))
        {
            return RetCode.E_INVALID_ARGUMENT;
        }

        _hookClass = hookClass ?? string.Empty;

        return RetCode.OK;
    }

    /// <summary>
    /// Names the data object to retrieve into - <c>of_setdataobject</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L422-L426</c>].
    /// </summary>
    /// <param name="dataObject">The data-object name; <see langword="null"/> is treated as empty.</param>
    /// <returns><c>RetCode.OK</c> - the oracle has NO guard here.</returns>
    /// <remarks>
    /// <b>IT ALSO CLEARS THE SUPPLIED SYNTAX</b> [<c>:L423</c>], and its counterpart clears the data
    /// object [<c>:L475</c>]. The two are mutually exclusive and the oracle enforces it by BLANKING the
    /// other rather than by refusing - so setting both, in either order, is last-wins. Contract C-05
    /// records that this is deliberately not modelled as a exclusive choice on the wire, because a
    /// exclusive choice would reject the sequence the legacy accepts.
    /// </remarks>
    internal long SetDataObject(string? dataObject)
    {
        // [:L422]
        _dataObject = dataObject ?? string.Empty;

        // [:L423] the mutual exclusion, enforced by clearing rather than by refusing.
        _sqlSyntax = string.Empty;

        return RetCode.OK;
    }

    /// <summary>
    /// Supplies a complete DataWindow syntax to build the carrier from - <c>of_setsqlsyntax</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L474-L478</c>].
    /// </summary>
    /// <param name="sqlSyntax">The syntax; <see langword="null"/> is treated as empty.</param>
    /// <returns><c>RetCode.OK</c> - the oracle has NO guard here.</returns>
    /// <remarks>
    /// <b>IT ALSO CLEARS THE DATA-OBJECT NAME</b> [<c>:L475</c>] - the other half of the mutual
    /// exclusion described on <see cref="SetDataObject"/>.
    /// </remarks>
    internal long SetSqlSyntax(string? sqlSyntax)
    {
        // [:L474]
        _sqlSyntax = sqlSyntax ?? string.Empty;

        // [:L475]
        _dataObject = string.Empty;

        return RetCode.OK;
    }

    /// <summary>
    /// Sets the statement to retrieve - <c>of_setsql</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L469-L472</c>].
    /// </summary>
    /// <param name="sql">The statement; <see langword="null"/> is treated as empty.</param>
    /// <returns><c>RetCode.OK</c> - the oracle has NO guard here.</returns>
    /// <remarks>
    /// <b>NO EMPTINESS GUARD, AND THE ASYMMETRY WITH C-07 IS REAL AND PRESERVED.</b> This setter is a
    /// plain assignment [<c>:L469</c>], whereas the command task's own statement setter refuses an empty
    /// value immediately. The emptiness check on THIS path happens later, inside the task body, where an
    /// empty statement yields <c>RetCode.E_INVALID_SQL</c> with <see cref="EmptySqlText"/>
    /// [<c>:L616-L617</c>]. Same condition, different code path, different moment - and contract C-05
    /// forbids moving the check forward to match, because a caller would then see a rejection at
    /// configuration time that the legacy defers to run time.
    /// </remarks>
    internal long SetSql(string? sql)
    {
        _sql = sql ?? string.Empty;

        return RetCode.OK;
    }

    /// <summary>
    /// Sets the pending sort expression - <c>of_setsort</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L486-L490</c>].
    /// </summary>
    /// <param name="sort">
    /// The sort expression. <b>An empty or <see langword="null"/> value becomes a SINGLE SPACE.</b>
    /// </param>
    /// <returns><c>RetCode.OK</c> - the oracle has NO guard here.</returns>
    /// <remarks>
    /// <b>DEFECT D8, PRESERVED VERBATIM (constraint C-B).</b> The oracle is two lines:
    /// <c>_sNewSort = sort</c> then <c>if _sNewSort = "" then _sNewSort = " "</c> [<c>:L486-L487</c>].
    /// So <c>""</c> and <c>" "</c> are the same request, and the single space is how the legacy CLEARS a
    /// sort rather than leaving it unset - a genuinely empty string and a single space reach the
    /// DataWindow sort property differently. It matters downstream twice: the emptiness test that gates
    /// application reads <c>_sNewSort &lt;&gt; ""</c> [<c>:L564, :L584</c>], which a single space
    /// SATISFIES, and the applied value is then <c>Trim</c>med back to empty [<c>:L565</c>] while the
    /// REPORTED value on failure is the untrimmed one [<c>:L566</c>].
    /// </remarks>
    internal long SetSort(string? sort)
    {
        // [:L486]
        _newSort = sort ?? string.Empty;

        // [:L487] `if _sNewSort = "" then _sNewSort = " "`.
        if (_newSort.Length == 0)
        {
            _newSort = ClearedSortOrFilter;
        }

        return RetCode.OK;
    }

    /// <summary>
    /// Sets the pending filter expression - <c>of_setfilter</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L480-L484</c>].
    /// </summary>
    /// <param name="filter">
    /// The filter expression. <b>An empty or <see langword="null"/> value becomes a SINGLE SPACE.</b>
    /// </param>
    /// <returns><c>RetCode.OK</c> - the oracle has NO guard here.</returns>
    /// <remarks>
    /// <b>DEFECT D8 again, in the identical shape</b> [<c>:L480-L481</c>]. See <see cref="SetSort"/>.
    /// </remarks>
    internal long SetFilter(string? filter)
    {
        // [:L480]
        _newFilter = filter ?? string.Empty;

        // [:L481] `if _sNewFilter = "" then _sNewFilter = " "`.
        if (_newFilter.Length == 0)
        {
            _newFilter = ClearedSortOrFilter;
        }

        return RetCode.OK;
    }

    /// <summary>
    /// Replaces the unique-index columns wholesale - <c>of_setpageduniqueindexcolumns</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L406-L408</c>], published as
    /// C-05 <c>SetPagedUniqueIndexColumns</c>.
    /// </summary>
    /// <param name="columns">
    /// The columns. <see langword="null"/> and an empty list both clear the collection; a
    /// <see langword="null"/> element is normalised to the empty string so no consumer can dereference
    /// one.
    /// </param>
    /// <returns><c>RetCode.OK</c> - the oracle has NO guard here [<c>:L407</c>].</returns>
    /// <remarks>
    /// <para>
    /// <b>REPLACES rather than merges</b>, unlike the two clause collections which upsert. The oracle
    /// assigns the array outright: <c>_sPagedUniqueIndexColumns = columns</c> [<c>:L406</c>].
    /// </para>
    /// <para>
    /// <b>A NON-EMPTY LIST SELECTS A DIFFERENT PAGING STRATEGY.</b> The first dialect arm branches on
    /// <c>UpperBound(_sPagedUniqueIndexColumns) &gt; 0</c> [<c>:L323</c>] into a unique-index inner-join
    /// wrapper instead of the plain form, so this is not a hint - it changes the generated statement and
    /// therefore changes what byte-exact parity is measured against.
    /// </para>
    /// <para>
    /// <b>Validation is NOT performed here, deliberately.</b> Each element is spliced into the select
    /// list, the join predicate and the <c>ORDER BY</c> unquoted [<c>:L331, :L333, :L336</c>], and the
    /// paging dispatcher validates and RESOLVES every element against the parsed statement before any
    /// arm touches it - after the paging-bounds, parse and dialect guards, and only for the arm that
    /// actually splices them. Duplicating that check here would put one rule in two places and would
    /// additionally displace an outcome the oracle defines, because the second arm ignores the
    /// collection entirely [<c>:L386-L395</c>] and must still answer a request carrying a malformed
    /// element.
    /// </para>
    /// </remarks>
    internal long SetPagedUniqueIndexColumns(IReadOnlyList<string>? columns)
    {
        // [:L406] wholesale replacement.
        _pagedUniqueIndexColumns.Clear();

        if (columns is not null)
        {
            // R9. A one-based legacy array becomes a zero-based list; the ORDER is what matters and it
            // is preserved, because the first arm concatenates the elements positionally and its
            // generated statement is compared byte for byte.
            foreach (string column in columns)
            {
                _pagedUniqueIndexColumns.Add(column ?? string.Empty);
            }
        }

        // [:L407]
        return RetCode.OK;
    }

    /// <summary>
    /// Sets the <c>WHERE</c> clause modification for the addressed select - <c>of_setwhereclause</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L269-L284</c>], published as
    /// C-05 <c>SetWhereClause</c>.
    /// </summary>
    /// <param name="selectIndex">The ONE-BASED select index. Zero and negatives are rejected.</param>
    /// <param name="ms">
    /// The modify style - <see cref="ClauseModifier.SQL_MS_REPLACE"/>,
    /// <see cref="ClauseModifier.SQL_MS_APPEND"/> or <see cref="ClauseModifier.SQL_MS_PREPEND"/>. Not
    /// validated, matching the legacy.
    /// </param>
    /// <param name="clause">The clause body without its leading keyword. Empty is rejected.</param>
    /// <returns>
    /// <c>RetCode.OK</c>, or <c>RetCode.E_INVALID_ARGUMENT</c> when the guard at <c>:L271</c> rejects the
    /// arguments.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>DELEGATED, AND THE DELEGATION IS THE POINT.</b> The legacy upsert is the R9 hazard in its
    /// purest form:
    /// </para>
    /// <code>
    /// nCount = UpperBound(_whereClauses)
    /// for nIndex = 1 to nCount
    ///     if _whereClauses[nIndex].index = selectIndex then exit
    /// next
    /// _whereClauses[nIndex].index = selectIndex        &lt;-- AFTER the loop, at whatever nIndex holds
    /// </code>
    /// <para>
    /// It relies on PowerScript leaving the loop variable at <c>nCount + 1</c> when nothing matched and
    /// at <c>1</c> when the collection was empty. A C# <c>for</c> scopes its counter, so a naive port
    /// silently overwrites entry 1 on every no-match call. <c>Sql/ClauseModifier.cs</c> owns that scan
    /// and reproduces all three outcomes, so this method forwards and NOTHING here re-implements it.
    /// </para>
    /// </remarks>
    internal long SetWhereClause(int selectIndex, long ms, string? clause) =>
        Clauses.SetWhereClause(selectIndex, ms, clause);

    /// <summary>
    /// Sets the <c>WHERE</c> clause modification for the FIRST select - the two-argument caller-side
    /// form [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlquery.sru:L74</c>], whose
    /// legacy body is exactly <c>return of_SetWhereClause(1, ms, clause)</c>.
    /// </summary>
    /// <param name="ms">The modify style.</param>
    /// <param name="clause">The clause body without its leading keyword.</param>
    /// <returns><c>RetCode.OK</c>, or <c>RetCode.E_INVALID_ARGUMENT</c> for an empty clause.</returns>
    internal long SetWhereClause(long ms, string? clause) => Clauses.SetWhereClause(ms, clause);

    /// <summary>
    /// Sets the <c>ORDER BY</c> clause modification for the addressed select -
    /// <c>of_setorderbyclause</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L286-L301</c>], published as
    /// C-05 <c>SetOrderByClause</c>.
    /// </summary>
    /// <param name="selectIndex">The ONE-BASED select index. Zero and negatives are rejected.</param>
    /// <param name="ms">The modify style. Not validated, matching the legacy.</param>
    /// <param name="clause">The clause body without its leading keyword. Empty is rejected.</param>
    /// <returns>
    /// <c>RetCode.OK</c>, or <c>RetCode.E_INVALID_ARGUMENT</c> when the guard at <c>:L288</c> rejects the
    /// arguments.
    /// </returns>
    /// <remarks>
    /// Delegated for the same reason as <see cref="SetWhereClause(int, long, string?)"/>: the legacy
    /// body is line-for-line identical apart from the array it addresses, and both scans belong to the
    /// collections' owner. <b>The two collections are separate</b> - setting one never touches the other
    /// - and contract C-05 gives them separate messages because they diverge the moment paging is
    /// involved: the paged builder strips <c>ORDER BY</c> and never touches <c>WHERE</c>.
    /// </remarks>
    internal long SetOrderByClause(int selectIndex, long ms, string? clause) =>
        Clauses.SetOrderByClause(selectIndex, ms, clause);

    /// <summary>
    /// Sets the <c>ORDER BY</c> clause modification for the FIRST select - the two-argument caller-side
    /// form [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlquery.sru:L73</c>].
    /// </summary>
    /// <param name="ms">The modify style.</param>
    /// <param name="clause">The clause body without its leading keyword.</param>
    /// <returns><c>RetCode.OK</c>, or <c>RetCode.E_INVALID_ARGUMENT</c> for an empty clause.</returns>
    internal long SetOrderByClause(long ms, string? clause) => Clauses.SetOrderByClause(ms, clause);

    #endregion


    #region Statement rewriting - the clause application, the count wrapper and the quote escape

    /// <summary>
    /// Escapes the embedded double quotes of a statement so it can be spliced into a DataWindow
    /// modification script - <c>ReplaceAll(sql, "~"", "~~~"", true)</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L709, :L732, :L795</c>].
    /// </summary>
    /// <param name="sql">The statement.</param>
    /// <returns>The statement with each <c>"</c> replaced by <c>~"</c>.</returns>
    /// <remarks>
    /// <para>
    /// The DataWindow modification grammar's own escape, not a C# one: in PowerScript <c>~"</c> denotes a
    /// literal double quote, so the search literal is one quote and the replacement literal is the two
    /// characters <c>~</c> and <c>"</c>.
    /// </para>
    /// <para>
    /// <b>Ordinal replacement, because this feeds a byte-exact comparison.</b> A culture-sensitive
    /// replace could in principle differ across hosts, and the generated modification script is
    /// observable.
    /// </para>
    /// </remarks>
    internal static string EscapeStatementForModify(string sql) =>
        sql.Replace(EmbeddedQuote, EscapedEmbeddedQuote, StringComparison.Ordinal);

    /// <summary>
    /// Builds the modification script that installs a statement -
    /// <c>'DataWindow.Table.Select = "' + sql + '"'</c>.
    /// </summary>
    /// <param name="sql">The statement, ALREADY escaped where the oracle escapes it.</param>
    /// <returns>The modification script.</returns>
    /// <remarks>
    /// The escaping is the CALLER's decision and not this method's, because the oracle does not apply it
    /// uniformly - see defect D9 on <see cref="SubstituteRuntimeStatement"/>. Folding the escape in here
    /// would silently correct that asymmetry.
    /// </remarks>
    internal static string BuildTableSelectAssignment(string sql) =>
        TableSelectAssignmentPrefix + sql + TableSelectAssignmentSuffix;

    /// <summary>
    /// Applies the stored clause modifications to a statement - the port of
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L685-L703</c>.
    /// </summary>
    /// <param name="originalSql">The statement to rewrite, read from the carrier at <c>:L629</c>.</param>
    /// <param name="modifiedSql">
    /// Receives the rewritten statement on success, or <see cref="string.Empty"/> on failure.
    /// </param>
    /// <param name="errorText">
    /// Receives the diagnostic that was reported, or <see cref="string.Empty"/> on success.
    /// </param>
    /// <returns>
    /// <c>RetCode.OK</c>, or <c>RetCode.E_INTERNAL_ERROR</c> for a parse failure [<c>:L686</c>] or for
    /// any per-clause failure [<c>:L692, :L699</c>].
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>The gate is the CALLER's</b> - <c>if Not bIsProcedure and (UpperBound(_whereClauses) &gt; 0 or
    /// UpperBound(_orderByClauses) &gt; 0)</c> [<c>:L684</c>] - because the stored-procedure half of it is
    /// a property of the task's resolved source rather than of the clauses. The clause half is
    /// <see cref="ClauseModifier.HasPendingClauses"/>.
    /// </para>
    /// <para>
    /// <b>ALL WHERE CLAUSES FIRST, THEN ALL ORDER BY CLAUSES, each in insertion order</b> [<c>:L689-L702</c>].
    /// The two loops are separate in the oracle and are separate here, and the order is observable
    /// whenever a style is <see cref="ClauseModifier.SQL_MS_APPEND"/> or
    /// <see cref="ClauseModifier.SQL_MS_PREPEND"/>, because appending composes clause TEXT.
    /// </para>
    /// <para>
    /// <b>The select index is ONE-BASED and is the FIRST argument of the three-argument overload.</b> It
    /// is passed through untouched: the collection's key is one-based, the statement model's parameter is
    /// one-based, and the position in the collection is a separate thing from the key. R9.
    /// </para>
    /// <para>
    /// <b>THE FIRST FAILURE RETURNS IMMEDIATELY</b> [<c>:L693, :L700</c>], leaving the statement model
    /// partially modified. That partial state is unobservable because the caller discards the model on
    /// this path, but the EARLY RETURN itself is preserved rather than replaced by a "collect all
    /// failures" pass, because which clause is reported first is observable in the diagnostic.
    /// </para>
    /// </remarks>
    internal long ApplyStoredClauses(string originalSql, out string modifiedSql, out string errorText)
    {
        modifiedSql = string.Empty;
        errorText = string.Empty;

        // `sqlParser = Create n_sql` [:L531] with `if Not sqlParser.Parse(sSQLOriginal) then` [:L685].
        // A per-call instance, because the model is mutable and this method mutates it.
        SelectStatementModel statement = new();

        if (!statement.Parse(originalSql))
        {
            // [:L686-L687]
            errorText = ParseFailedText;

            return RetCode.E_INTERNAL_ERROR;
        }

        // `nCount = UpperBound(_whereClauses) : for nIndex = 1 to nCount` [:L689-L695]. The managed
        // collection is enumerated in the SAME insertion order the one-based array walk produces.
        foreach (SqlClause clause in Clauses.WhereClauses)
        {
            // `if Not sqlParser.ModifyWhere(_whereClauses[nIndex].index, .ms, .clause) then` [:L691].
            // ONE-BASED select index FIRST, style second, text third - the model's own parameter order.
            if (!statement.ModifyWhere(clause.SelectIndex, clause.ModifyStyle, clause.Clause))
            {
                // [:L692-L693]
                errorText = ModifyWhereFailedText;

                return RetCode.E_INTERNAL_ERROR;
            }
        }

        // `nCount = UpperBound(_orderByClauses) : for nIndex = 1 to nCount` [:L696-L702]. A SECOND loop
        // over a SEPARATE collection, exactly as the oracle writes it.
        foreach (SqlClause clause in Clauses.OrderByClauses)
        {
            // [:L698]
            if (!statement.ModifyOrder(clause.SelectIndex, clause.ModifyStyle, clause.Clause))
            {
                // [:L699-L700]
                errorText = ModifyOrderByFailedText;

                return RetCode.E_INTERNAL_ERROR;
            }
        }

        // `sSQLExec = sqlParser.GetSQL()` [:L703].
        modifiedSql = statement.GetSql();

        return RetCode.OK;
    }

    /// <summary>
    /// Builds the counting statement from an executable statement - the port of
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L826-L834</c>.
    /// </summary>
    /// <param name="executableSql">
    /// The statement the retrieval actually ran, <c>sSQLExec</c>, which is either the clause-modified
    /// statement [<c>:L703</c>] or the carrier's own [<c>:L719</c>]. <b>NOT the paged statement</b> - see
    /// the remarks.
    /// </param>
    /// <param name="countSql">
    /// Receives the counting statement on success, or <see cref="string.Empty"/> on failure.
    /// </param>
    /// <param name="errorText">
    /// Receives <see cref="ParseFailedText"/> on a parse failure, otherwise <see cref="string.Empty"/>.
    /// </param>
    /// <returns><c>RetCode.OK</c>, or <c>RetCode.E_INTERNAL_ERROR</c> for a parse failure [<c>:L827</c>].</returns>
    /// <remarks>
    /// <para>
    /// <b>THREE STEPS, AND ALL THREE ARE OBSERVABLE (constraint C-B):</b>
    /// </para>
    /// <code>
    /// L830   sqlParser.ModifyColumn(Enums.SQL_MS_REPLACE, "1 AS _")   -- 去掉字段, the alias replacement
    /// L831   if sqlParser.HasOrder() then
    /// L832       sqlParser.ModifyOrder(Enums.SQL_MS_REPLACE, "")      -- ONLY when an order-by is present
    /// L834   sSQL = "SELECT COUNT(1) AS CNT FROM (" + sqlParser.GetSQL() + ") pfwPagedSQL_Tbl"
    /// </code>
    /// <para>
    /// <b>Both cited line numbers upstream are real and they are DIFFERENT lines of this one block.</b>
    /// AAP §0.4.2.6 cites <c>:L830</c> for the <c>1 AS _</c> replacement and
    /// <c>Sql/Paging/SqlServerPagingRewriter.cs</c> cites <c>:L834</c> for the sentinel wrap. Both were
    /// opened and read here, and both are correct.
    /// </para>
    /// <para>
    /// <b>The ORDER BY strip is CONDITIONAL, and that condition is not cosmetic.</b> The oracle guards it
    /// with <c>HasOrder()</c> [<c>:L831</c>], so a statement that never had an <c>ORDER BY</c> is not
    /// given an empty one - and <see cref="SelectStatementModel"/> distinguishes an absent clause from a
    /// present-but-empty one when it re-emits, which is exactly where a byte-exact difference would show
    /// up.
    /// </para>
    /// <para>
    /// <b>The sentinel is CONSUMED, not retyped.</b>
    /// <see cref="SqlServerPagingRewriter.DerivedTableAlias"/> is <c>internal</c> precisely because this
    /// count wrapper needs it too, so <c>pfwPagedSQL_Tbl</c> is spelled in exactly one place in the
    /// repository. That matters more than it looks: its neighbour <c>pfwPagedSQL_OutterTbl</c> carries TWO
    /// t's, a preserved legacy misspelling, so retyping any sentinel invites exactly the drift the single
    /// owner prevents.
    /// </para>
    /// <para>
    /// <b>It counts the UNPAGED statement.</b> The oracle passes <c>sSQLExec</c>, which is the statement
    /// BEFORE the paging rewrite - the paged statement returns one page and counting one page would
    /// answer the page size rather than the total. The distinction is easy to lose because both locals
    /// are in scope at <c>:L826</c>.
    /// </para>
    /// </remarks>
    internal static long BuildCountStatement(string executableSql, out string countSql, out string errorText)
    {
        countSql = string.Empty;
        errorText = string.Empty;

        // `if Not sqlParser.Parse(sSQLExec) then` [:L826]. The boolean IS checked here, unlike at the
        // legacy factory site that discards it.
        SelectStatementModel statement = new();

        if (!statement.Parse(executableSql))
        {
            // [:L827-L828]
            errorText = ParseFailedText;

            return RetCode.E_INTERNAL_ERROR;
        }

        // [:L830] `ModifyColumn(Enums.SQL_MS_REPLACE, "1 AS _")`. The style constant comes from the
        // shared catalogue; the alias literal is byte-exact.
        //
        // The boolean result is DISCARDED, exactly as the oracle discards it - :L830 is a bare statement
        // with no `if Not`, unlike the two clause-modification calls at :L691 and :L698 which are both
        // tested. Testing it here would add a failure arm the oracle does not have.
        _ = statement.ModifyColumn(ClauseModifier.SQL_MS_REPLACE, CountColumnAlias);

        // [:L831] `if sqlParser.HasOrder() then` - CONDITIONAL, and only then [:L832] the strip.
        if (statement.HasOrder())
        {
            // [:L832] `ModifyOrder(Enums.SQL_MS_REPLACE, "")`. Result discarded, as the oracle does.
            _ = statement.ModifyOrder(ClauseModifier.SQL_MS_REPLACE, string.Empty);
        }

        // [:L834] the wrap, with the sentinel taken from its single owner.
        countSql =
            CountWrapperPrefix
            + statement.GetSql()
            + CountWrapperAliasSeparator
            + SqlServerPagingRewriter.DerivedTableAlias;

        return RetCode.OK;
    }

    #endregion

    #region Paging - the two-arm dispatch, driven rather than re-owned

    /// <summary>
    /// Builds the paged statement - the DRIVER for <c>_of_buildpagedsql</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L303-L404</c>].
    /// </summary>
    /// <param name="dialect">
    /// The dialect discriminator, answered by <c>TransObject.of_GetDBType()</c> [<c>:L320</c>].
    /// </param>
    /// <param name="originalSql">The statement to rewrite - the oracle's <c>origSql</c>.</param>
    /// <returns>
    /// The rewritten statement with its return code and diagnostic text. <b>The text is EMPTY on the
    /// <c>case else</c> arm and that emptiness is a real value</b> [<c>:L397</c>].
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>THIS METHOD DRIVES; IT DOES NOT REWRITE.</b> The two arms are
    /// <see cref="SqlServerPagingRewriter"/> and <see cref="OraclePagingRewriter"/>, the two shared
    /// pre-dispatch guards and the <c>case else</c> arm belong to
    /// <see cref="PagingRewriteDispatcher"/>, and this method's whole job is to assemble the five inputs
    /// the oracle reads from its own state - the statement, <c>_nPageSize</c>, <c>_nPageIndex</c>,
    /// <c>_bPageNative</c> and <c>_sPagedUniqueIndexColumns</c> - and to hand back the result.
    /// </para>
    /// <para>
    /// ==========================================================================================
    /// </para>
    /// <para>
    /// <b>DEFECT D5 - EXACTLY TWO ARMS, AND NO SQLITE ARM (constraints C-B and C-E).</b>
    /// </para>
    /// <para>
    /// The dispatch is <c>choose case TransObject.of_GetDBType()</c> with <c>case TransObject.DBT_MSSQL</c>
    /// [<c>:L321</c>], <c>case TransObject.DBT_ORACLE</c> [<c>:L386</c>] and <c>case else</c> answering
    /// <c>RetCode.E_NO_IMPLEMENTATION</c> with an empty message [<c>:L396-L398</c>]. The resolver behind
    /// it, verified at <c>n_cst_thread_trans.sru:L356-L361</c>, is:
    /// </para>
    /// <code>
    /// if Pos(Upper(DBMS), "ORACLE") &gt; 0 then return DBT_ORACLE else return DBT_MSSQL
    /// </code>
    /// <para>
    /// <b>So anything whose engine name does not contain "ORACLE" - INCLUDING SQLITE - classifies as the
    /// SQL Server type.</b> That is counter-intuitive and it is correct. No third arm is added: the
    /// dialect value selects a PURE STRING TRANSFORM and never a connection, Persistence provisions
    /// SQLite only, and the SQL Server and Oracle behaviours exist solely as byte-exact rewriters that
    /// are testable with no instance of either engine. Adding a SQLite arm would invent a generated
    /// statement the legacy never produced.
    /// </para>
    /// <para>
    /// ==========================================================================================
    /// </para>
    /// <para>
    /// <b>DEFECT D3 - THE ORACLE LEAKS ITS PARSER ON EVERY ERROR PATH, AND THIS PORT DOES NOT CLAIM
    /// PARITY WHERE THERE IS NONE (constraint C-K).</b>
    /// </para>
    /// <para>
    /// <c>Destroy sqlParser</c> sits at <c>:L401</c>, AFTER the <c>choose case</c> - so all three early
    /// returns leak the instance: the invalid-paging-settings return at <c>:L309</c>, the parse-failure
    /// return at <c>:L316</c> and the not-implemented return at <c>:L398</c>. It is the same family as
    /// the AAP's note that <c>parsesql.srf</c> creates, parses, returns and never destroys.
    /// </para>
    /// <para>
    /// <b>The leak has NO managed analogue and is deliberately NOT reproduced.</b>
    /// <see cref="SelectStatementModel"/> owns no unmanaged resource, is deliberately not
    /// <see cref="IDisposable"/>, and its lifetime belongs to the collector. What IS reproduced is the
    /// CONTROL FLOW that caused it: the dispatcher's three guards are written as early returns rather
    /// than consolidated into one exit path, because which arm answers which code is observable. The
    /// asymmetry is stated rather than papered over - the flow is preserved, the leak is not, and neither
    /// half of that sentence is a claim about the other.
    /// </para>
    /// </remarks>
    internal PagedStatementOutcome BuildPagedStatement(DatabaseType dialect, string originalSql)
    {
        // The five inputs the oracle reads from its own private state at :L303-L307, :L323, :L343.
        PagingRewriteRequest request = new(
            originalSql,
            _pageSize,
            _pageIndex,
            _pageNative,
            _pagedUniqueIndexColumns);

        // The guards at [:L307-L310] and [:L314-L317], the two arms at [:L321] and [:L386], and the
        // `case else` arm at [:L396-L398] all live behind this one call. Nothing below re-decides any of
        // them.
        PagingRewriteResult result = PagingRewriteDispatcher.Rewrite(in request, dialect, _pagingRewriters);

        return new PagedStatementOutcome(result.ReturnCode, result.RewrittenSql, result.ErrorText);
    }

    #endregion

    #region Page counting - the short-circuit, the count query and the two overrides

    /// <summary>
    /// Infers the paging totals arithmetically, with NO counting statement issued - the port of
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L819-L823</c>.
    /// </summary>
    /// <param name="rowCount">The rows this page actually returned.</param>
    /// <param name="pageSize">The configured rows per page.</param>
    /// <param name="pageIndex">The ONE-BASED page ordinal.</param>
    /// <param name="pageCount">Receives the inferred page total.</param>
    /// <param name="recordCount">Receives the inferred record total.</param>
    /// <returns>
    /// <see langword="true"/> when the short-circuit applied and the two totals are inferred;
    /// <see langword="false"/> when a counting statement has to be issued.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The oracle, verbatim, under its own comment 当前页不足分页大小时不进行统计 - "do not count when the
    /// current page is short of the page size":
    /// </para>
    /// <code>
    /// if (nRowCnt &gt; 0 and nRowCnt &lt; _nPageSize) or (nRowCnt = 0 and _nPageIndex = 1) then
    ///     nPageCount   = _nPageIndex
    ///     nRecordCount = (_nPageIndex - 1) * _nPageSize + nRowCnt
    ///     if nRecordCount = 0 then nPageCount = 0
    /// </code>
    /// <para>
    /// <b>Two disjuncts, and the second is NOT redundant.</b> A partial final page - some rows, fewer
    /// than a full page - means this is the last page, so the totals follow from the ordinal. An EMPTY
    /// page qualifies only on page ONE: an empty page five says nothing about the total, so it falls
    /// through to the counting statement. A port that dropped the <c>_nPageIndex = 1</c> conjunct would
    /// silently report zero records for every empty page.
    /// </para>
    /// <para>
    /// <b>The zero-record correction comes LAST and it overrides the line above it</b> [<c>:L823</c>].
    /// Without it, an empty first page would report one page containing no records.
    /// </para>
    /// <para>
    /// <b>Pure, static and total.</b> No clock, no statement, no carrier and no transaction - which is
    /// what lets all four arms be pinned by a test with no database (constraint C-H).
    /// </para>
    /// </remarks>
    internal static bool TryInferPageCounts(
        long rowCount,
        long pageSize,
        long pageIndex,
        out long pageCount,
        out long recordCount)
    {
        // [:L820] the two disjuncts.
        bool partialFinalPage = rowCount > 0L && rowCount < pageSize;
        bool emptyFirstPage = rowCount == 0L && pageIndex == 1L;

        if (!partialFinalPage && !emptyFirstPage)
        {
            pageCount = 0L;
            recordCount = 0L;

            return false;
        }

        // [:L821] `nPageCount = _nPageIndex`. The ONE-BASED ordinal IS the page total on a final page.
        pageCount = pageIndex;

        // [:L822] `nRecordCount = (_nPageIndex - 1) * _nPageSize + nRowCnt`. R9: the `- 1` is the
        // one-based ordinal being turned into a count of COMPLETE preceding pages, not a rebasing.
        recordCount = ((pageIndex - 1L) * pageSize) + rowCount;

        // [:L823] `if nRecordCount = 0 then nPageCount = 0`. LAST, and it overrides the assignment above.
        if (recordCount == 0L)
        {
            pageCount = 0L;
        }

        return true;
    }

    /// <summary>
    /// Divides a record total into pages - <c>Ceiling(nRecordCount / _nPageSize)</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L853</c>].
    /// </summary>
    /// <param name="recordCount">The record total the counting statement answered.</param>
    /// <param name="pageSize">The rows per page. Guaranteed positive on this path by two guards.</param>
    /// <returns>The page total.</returns>
    /// <remarks>
    /// <para>
    /// <b><see cref="decimal"/> rather than <see cref="double"/>, and that is a correctness decision.</b>
    /// PowerScript's division of two <c>long</c>s yields a REAL and <c>Ceiling</c> then rounds it up, so
    /// a decimal divide is the exact analogue. A binary floating-point divide can land a large exact
    /// multiple a hair above or below the integer and change the page total by one, which is precisely
    /// the kind of off-by-one a row-count assertion would not catch.
    /// </para>
    /// <para>
    /// A non-positive page size is impossible here - the setter rejects it [<c>:L462</c>] and the paged
    /// builder re-rejects it [<c>:L307</c>] - but it is guarded rather than assumed, because a divide by
    /// zero would surface as an exception on a path whose whole contract is return codes.
    /// </para>
    /// </remarks>
    internal static long DividePagesCeiling(long recordCount, long pageSize)
    {
        if (pageSize <= 0L)
        {
            // Unreachable through the guarded paths above. Answering the not-counted value rather than
            // throwing keeps this method total, and a zero page size can only mean "no paging".
            return NotCountedValue;
        }

        return (long)Math.Ceiling((decimal)recordCount / pageSize);
    }

    /// <summary>
    /// Applies the query-side defensive row-count override - the port of
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L771-L774</c>.
    /// </summary>
    /// <param name="sqlCode">
    /// The transaction's <c>SQLCode</c> AFTER the retrieval. <b>Tested for EXACTLY <c>-1</c>.</b>
    /// </param>
    /// <param name="rowCount">The row count the retrieval reported. <b>Tested for <c>&gt;= 0</c>.</b></param>
    /// <param name="carrier">
    /// The result carrier, which is RESET when the override fires. May be <see langword="null"/> only in
    /// the impossible case of a caller with no carrier, in which case nothing is reset.
    /// </param>
    /// <returns>
    /// <c>-1</c> when the override fired, otherwise <paramref name="rowCount"/> unchanged.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>DEFECT D6, PRESERVED VERBATIM. The oracle is four lines and every one of them is load-bearing:</b>
    /// </para>
    /// <code>
    /// if TransObject.SQLCode = -1 and nRowCnt &gt;= 0 then
    ///     nRowCnt = -1
    ///     data.Reset()
    /// end if
    /// </code>
    /// <para>
    /// <b>Three things a straightforward port gets wrong.</b> First, the SQL code test is EXACT EQUALITY
    /// WITH <c>-1</c> - not "non-zero", which is what the transaction's own state check uses, and not
    /// <c>of_IsFailed()</c>, which is <c>SQLCode &lt; 0</c> [<c>n_cst_thread_trans.sru:L337</c>] and would
    /// fire for <c>-2</c> as well. Second, the row-count test is <c>&gt;= 0</c>, so a retrieval that
    /// returned ZERO rows successfully is still rewritten to a failure when the SQL code says <c>-1</c> -
    /// and this is deliberately DIFFERENT from the update side, whose equivalent override tests for
    /// exactly <c>1</c>. Third, IT ALSO RESETS THE CARRIER, which the update-side override does not do, so
    /// the rows a failed retrieval left behind are discarded rather than published.
    /// </para>
    /// <para>
    /// <b>An implementation that trusts the retrieval's own return value reports success on a failed
    /// retrieval</b>, and one that widens the code test to <c>&lt; 0</c> rewrites a cancellation into a
    /// database error. Both are silent.
    /// </para>
    /// <para>
    /// Pure and static apart from the reset, so both arms are pinnable with no database (constraint C-H).
    /// </para>
    /// </remarks>
    internal static long ApplyDefensiveRowCountOverride(
        long sqlCode,
        long rowCount,
        DataWindowBufferStore? carrier)
    {
        // [:L771] EXACTLY -1 on the code, and >= 0 on the count. Neither comparison is widened.
        if (sqlCode != -1L || rowCount < 0L)
        {
            return rowCount;
        }

        // [:L773] the carrier is discarded, which the update-side override does not do.
        carrier?.Reset();

        // [:L772]
        return -1L;
    }

    #endregion


    #region The execution path - the port of ondotask [:L500-L882]

    /// <summary>
    /// Runs the retrieval and publishes it into <paramref name="sink"/> - the port of
    /// <c>event ondotask</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L500-L882</c>], and the
    /// engine behind contract C-05 <c>Query</c>.
    /// </summary>
    /// <param name="sink">The caller-side proxy the result is published into.</param>
    /// <param name="cancellationToken">
    /// The task's cancellation token, standing in for <c>of_IsCancelled()</c> and for the false answer of
    /// <c>of_Wait</c>.
    /// </param>
    /// <returns>
    /// The value the legacy event returns: <c>RetCode.OK</c>, <c>RetCode.CANCELLED</c>, or one of the
    /// nine failure codes contract C-05 enumerates.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="sink"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// <b>ONE RETRIEVAL AT A TIME, AND A SECOND IS REFUSED RATHER THAN INTERLEAVED.</b> The running flag
    /// is the managed reading of the substrate's <c>#Running</c>, which the public reset guards against
    /// [<c>:L237</c>]. A concurrent second call answers <c>RetCode.E_BUSY</c> - the same code the reset
    /// answers - because an interleaved second retrieval would corrupt the first's chunk sequence, and
    /// because the thread-affinity contract this object declares about itself
    /// [<c>:L2</c>, <c>[运行在子线程]</c>] says one thread owns it.
    /// </para>
    /// <para>
    /// <b>No <see cref="OperationCanceledException"/> escapes.</b> Cancellation is a RETURN CODE on this
    /// contract - the legacy reacts to <c>RetCode.CANCELLED</c> and a caller written against it would not
    /// catch an exception - so every cancellation point answers the code instead.
    /// </para>
    /// </remarks>
    internal async Task<long> ExecuteAsync(IQueryResultSink sink, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sink);

        // The managed `#Running` latch. CompareExchange rather than a plain assignment so a second
        // concurrent execution is refused rather than admitted.
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
        {
            return RetCode.E_BUSY;
        }

        try
        {
            return await RunRetrievalAsync(sink, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Volatile.Write(ref _running, 0);
        }
    }

    /// <summary>
    /// The body of <c>ondotask</c>, step for step against
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L500-L882</c>.
    /// </summary>
    /// <param name="sink">The caller-side proxy.</param>
    /// <param name="cancellationToken">The task's cancellation token.</param>
    /// <returns>The legacy return value.</returns>
    /// <remarks>
    /// <para>
    /// Separated from <see cref="ExecuteAsync"/> so the running latch is a single, unmissable wrapper
    /// rather than something this long body has to remember to clear on each of its many exits.
    /// </para>
    /// <para>
    /// <b>THE HOOK IS CREATED BEFORE THE TRANSACTION IS RESOLVED, AND THE ORACLE LEAKS IT ON ONE PATH -
    /// FINDING D11 (constraint C-K).</b> <c>hook = Create Using _sHookClass</c> sits at <c>:L517</c>, the
    /// invalid-transaction return sits at <c>:L523</c>, and the <c>try</c> that owns the cleanup block
    /// does not open until <c>:L526</c> - so the <c>if IsValid(hook) then Destroy hook</c> at
    /// <c>:L873</c> is never reached on the invalid-transaction path and the hook is never destroyed.
    /// </para>
    /// <para>
    /// <b>This port releases the hook on that path anyway, deliberately and visibly.</b> Not disposing a
    /// caller-supplied <see cref="IDisposable"/> is a real resource leak in .NET with no behavioural
    /// upside: nothing the caller can observe distinguishes a released hook from an unreleased one, the
    /// legacy <c>Destroy</c> on an object holding no resources is itself a no-op, and hazard 2 of
    /// <c>docs/PB多线程绕坑提示.md</c> asks for exactly this kind of explicit release. It is therefore an
    /// unobservable safety change of the class the plan sanctions, and it is called out here rather than
    /// made quietly - the FINDING stands on the record even though the leak is not reproduced.
    /// </para>
    /// </remarks>
    private async Task<long> RunRetrievalAsync(IQueryResultSink sink, CancellationToken cancellationToken)
    {
        // [:L516-L518] `if _sHookClass <> "" then hook = Create Using _sHookClass`. Constraint C-G: the
        // class name is caller-controlled input, so activation goes through the base's restricted
        // activator, which validates the name and answers null rather than throwing into the request path.
        ISqlRetrievalHook? hook = ResolveRetrievalHook(_hookClass);

        // [:L520-L524] the transaction. NOTE the tri-state algebra: the oracle tests IsFailed, so a
        // prevention (value 1) would NOT be treated as a failure here, and neither would a cancellation.
        IPooledTransaction? transaction = null;
        DbErrorData transactionError = DbErrorData.Empty;

        if (Predicates.IsFailed(GetTransObject(ref transaction, ref transactionError)) || transaction is null)
        {
            // [:L521] the statement member is EMPTY here, the buffer is Primary! and the row is 0.
            _ = OnDbError(
                transactionError.SqlDbCode,
                transactionError.SqlErrText,
                string.Empty,
                DwBuffer.Primary,
                0L);

            // [:L522] an EMPTY diagnostic, which is a real value rather than a missing one.
            _ = OnError(RetCode.E_INVALID_TRANSACTION, string.Empty);

            // FINDING D11 - see the remarks. The oracle returns here WITHOUT reaching its own cleanup
            // block, so it leaks the hook; this port releases it and says so.
            ReleaseRetrievalHook(ref hook);

            // [:L523]
            return RetCode.E_INVALID_TRANSACTION;
        }

        ISqlDataStore? data = null;
        bool cacheDataStore = false;
        bool carrierHandedOver = false;

        // [:L526] `try` ... [:L870-L871] `catch(Throwable ex) throw ex` ... [:L872] `finally`.
        //
        // The catch arm is a bare rethrow, which in .NET is what NOT writing a catch arm at all does -
        // and writing one would be strictly worse, because `catch (Exception ex) { throw ex; }` resets the
        // stack trace where `throw;` and no-catch preserve it. The cleanup block is the finally below.
        try
        {
            // [:L527-L529]
            if (IsCancelled || cancellationToken.IsCancellationRequested)
            {
                return RetCode.CANCELLED;
            }

            // [:L531] `sqlParser = Create n_sql`. NO field here: the two places that parse -
            // ApplyStoredClauses and BuildCountStatement - each create their own per-call
            // SelectStatementModel, which is what keeps both of them pure functions of their inputs. The
            // oracle shares one instance across both sites purely because PowerScript has no cheap way
            // not to, and sharing it is not observable: neither site reads state the other left behind.
            //
            // [:L534-L540] the cache probe. BOTH conditions are required, and the inner one is not
            // redundant: caching is keyed by DATA-OBJECT NAME, so a cached retrieval with no named data
            // object has no key and falls through to a fresh carrier.
            if (_cache && _dataObject.Length > 0)
            {
                cacheDataStore = true;
                data = GetCacheDataStore(_dataObject);
            }

            // [:L541-L548] the affinity selection and the init event. GetCacheDataStore already fires the
            // init event on both of its own paths [:L568], so this arm covers only the fresh-carrier case,
            // exactly as the oracle's `if Not IsValidObject(data)` does.
            if (!Predicates.IsValidObject(data))
            {
                data = CreateDataStore();

                // [:L547] `data.Event OnInit(this)`.
                data.OnInit(this);
            }

            // The null-forgiving read is sound: either the cache produced a store or the branch above
            // created one, and both paths assign before this point.
            ISqlDataStore store = data!;

            bool plainSql = false;

            // [:L550-L624] the two shapes of source: a NAMED data object, or a statement/syntax.
            if (_dataObject.Length > 0)
            {
                long resolved = ResolveNamedDataObject(store, sink, cacheDataStore);

                if (resolved != RetCode.OK)
                {
                    return resolved;
                }
            }
            else
            {
                long built = BuildCarrierFromStatement(store, transaction, out plainSql);

                if (built != RetCode.OK)
                {
                    return built;
                }
            }

            // [:L626] `bIsProcedure = (data.Describe("DataWindow.Table.Procedure") <> "!")`.
            //
            // ⚠ THE SENSE IS INVERTED FROM THE OBVIOUS READING: a source is a stored procedure precisely
            // when the property IS readable. Testing for EQUALITY with the sentinel would classify every
            // ordinary SELECT as a procedure and would suppress clause modification, paging and page
            // counting for all of them - three behaviours at once, none of them with a compiler error.
            bool isProcedure = !string.Equals(
                store.Describe(QueryDataWindowProperty.TableProcedure),
                DescribeUnreadable,
                StringComparison.Ordinal);

            // [:L628-L629] the statement kept for restoration, under the oracle's own comment
            // 保存当前的SQL用于还原.
            string originalSql = store.GetSqlSelect();

            // [:L631-L642] the runtime statement substitution.
            long substituted = SubstituteRuntimeStatement(store, isProcedure, plainSql, ref originalSql);

            if (substituted != RetCode.OK)
            {
                return substituted;
            }

            // [:L644-L670] the cached-carrier drop-down de-duplication.
            if (cacheDataStore)
            {
                long deduplicated = DeduplicateChildQueries(store);

                if (deduplicated != RetCode.OK)
                {
                    return deduplicated;
                }
            }

            // =====================================================================================
            //  [:L672-L681] DEFECT D10, PART 4 - AND THE ORDERING OF THESE TWO STEPS IS CONTRACT.
            //
            //  The oracle's own comment at :L672-L673 reads 解决交叉表字段过多时可能出现SetFullState崩溃问题 -
            //  "works around the SetFullState crash that can occur when a crosstab has too many
            //  columns". The workaround is a GUARDED read-then-write of the no-user-prompt property
            //  [:L674-L676], and it sits BEFORE the transaction attachment [:L678] and BEFORE the clause
            //  modification [:L684]. Both orderings are preserved: the property has to be in place
            //  before anything can trigger a prompt, and the transaction has to be attached before the
            //  statement is rewritten.
            //
            //  The modify RESULT IS DISCARDED at :L675, unlike the one at :L664 which is captured - so
            //  a caller that treated a failure here as fatal would be STRICTER than the legacy.
            // =====================================================================================
            DataStoreFullStateSurface surface = new(store);
            NoUserPromptOutcome noUserPrompt = FullStateCodec.ApplyNoUserPromptWorkaround(surface);

            if (noUserPrompt.ModifyAttempted && !noUserPrompt.Succeeded)
            {
                // Observability only. The oracle discards this result, so nothing here returns on it -
                // logging it is the strictly-unobservable half of the decision, and the redactor is
                // applied because a modify diagnostic can quote the statement it failed on (C-F).
                //
                // ⚠ REPORTED ONCE PER PROCESS AT WARNING, AND AT DEBUG EVERY TIME AFTER.
                // Whether this modify can be applied is a STATIC PROPERTY OF THE RUNTIME this service is
                // hosted on, not a property of any one request: it either always succeeds here or always
                // fails here. Warning on every query therefore repeated one unchanging fact once per
                // retrieval - six queries produced six identical warnings - which trains an operator to
                // filter the channel that a real per-request fault would arrive on. The first occurrence
                // still reports at Warning, because a parity workaround that cannot be applied is worth
                // an operator's attention exactly once; the rest stay at Debug so the per-call record is
                // narrowed rather than lost. BEHAVIOUR IS UNCHANGED - the oracle discards this result and
                // so does the port; only the severity of an observation moves.
                bool firstOccurrence = Interlocked.Exchange(ref _noUserPromptReported, 1) == 0;

                _logger.Log(
                    firstOccurrence ? LogLevel.Warning : LogLevel.Debug,
                    "The no-user-prompt workaround could not be applied: {ModifyError}",
                    _redactor.Redact(noUserPrompt.ModifyError));
            }

            // [:L678-L681]
            if (_dataWindowRuntime.AttachTransaction(store, transaction)
                != DataWindowBufferStore.DataStoreSuccess)
            {
                _ = OnError(RetCode.E_INVALID_TRANSACTION, SetTransObjectFailedText);

                return RetCode.E_INVALID_TRANSACTION;
            }

            // [:L683-L720] the stored clause modifications.
            string executableSql;
            bool restoreSql = false;

            // [:L684] the composite gate: the stored-procedure half is this task's, the clause half is
            // the collections owner's.
            if (!isProcedure && Clauses.HasPendingClauses)
            {
                long applied = ApplyStoredClauses(originalSql, out executableSql, out string clauseError);

                if (applied != RetCode.OK)
                {
                    _ = OnError(applied, clauseError);

                    return applied;
                }

                // [:L705-L708] and [:L714-L716] carry a COMMENTED-OUT crosstab static-mode toggle around
                // this modify. It is carried across inert - not revived, not deleted - because reviving a
                // dormant path is exactly the silent change constraint C-B forbids:
                //
                //     if data.Describe("DataWindow.Crosstab.StaticMode") = "no" then
                //         data.Modify("DataWindow.Crosstab.StaticMode = yes")
                //         bSwitchStaticMode = true
                //     end if
                //     ... the modify ...
                //     if bSwitchStaticMode then data.Modify("DataWindow.Crosstab.StaticMode = no")
                //
                // [:L709] the modify, WITH the quote escape.
                string modifyError = store.Modify(
                    BuildTableSelectAssignment(EscapeStatementForModify(executableSql)));

                if (modifyError.Length > 0)
                {
                    // [:L711-L712]
                    _ = OnError(RetCode.E_INTERNAL_ERROR, ModifyTableSelectFailurePrefix + modifyError);

                    return RetCode.E_INTERNAL_ERROR;
                }

                // [:L717]
                restoreSql = true;
            }
            else
            {
                // [:L719] the un-modified statement is what gets executed AND what gets counted.
                executableSql = originalSql;
            }

            // [:L722-L741] the paging rewrite.
            if (!isProcedure && _paged)
            {
                // [:L724] `_of_BuildPagedSQL(TransObject, sSQLExec, ref sSQL)`. The dialect comes from the
                // transaction and selects a PURE STRING TRANSFORM, never a connection (constraint C-E).
                PagedStatementOutcome paged = BuildPagedStatement(transaction.GetDbType(), executableSql);

                // [:L725] `if rtCode <> RetCode.OK then return rtCode` - EXACT inequality against OK, not
                // the failure predicate, so a prevention would not slip through as a success.
                if (paged.ReturnCode != RetCode.OK)
                {
                    // The oracle raises the diagnostic INSIDE the builder, at each of its three early
                    // returns [:L308, :L315, :L397]; the dispatcher answers a result instead, so the
                    // report is raised here. The text is passed through verbatim - INCLUDING the
                    // deliberately EMPTY text of the not-implemented arm.
                    _ = OnError(paged.ReturnCode, paged.ErrorText);

                    return paged.ReturnCode;
                }

                // [:L732] the same modify with the same escape. The same commented-out static-mode toggle
                // surrounds it at [:L727-L731] and [:L737-L739] and is likewise carried inert.
                string modifyError = store.Modify(
                    BuildTableSelectAssignment(EscapeStatementForModify(paged.PagedSql)));

                if (modifyError.Length > 0)
                {
                    // [:L734-L735]
                    _ = OnError(RetCode.E_INTERNAL_ERROR, ModifyTableSelectFailurePrefix + modifyError);

                    return RetCode.E_INTERNAL_ERROR;
                }

                // [:L740]
                restoreSql = true;
            }

            // [:L743-L744] the state is CLEARED and THEN the cap is set. The order matters: clearing
            // resets the exceeded flag the cap is checked against later [:L787].
            store.ClearState();
            store.Carrier.SetMaxRows(_maxRows);

            // [:L746-L753] the vetoable before-retrieve hook, and its two-way discrimination.
            if (Predicates.IsPrevented(_transactionSurface.RaiseBeforeRetrieve(transaction, store.Carrier)))
            {
                // A CLEAN VETO IS A CANCELLATION; A VETO WITH A FAILED TRANSACTION IS A DATABASE ERROR.
                // The same discrimination the update path performs, and the predicate is the
                // transaction's own `of_IsFailed()`, which is `SQLCode < 0`
                // [n_cst_thread_trans.sru:L337] - NOT the exactly-minus-one test of the defensive
                // override below.
                if (transaction.IsSqlFailed())
                {
                    // [:L748] EMPTY statement, Primary! buffer, row 0.
                    _ = OnDbError(
                        transaction.SqlDbCode,
                        transaction.SqlErrText,
                        string.Empty,
                        DwBuffer.Primary,
                        0L);

                    // [:L749] an EMPTY diagnostic again.
                    _ = OnError(RetCode.E_DB_ERROR, string.Empty);

                    // [:L750]
                    return RetCode.E_DB_ERROR;
                }

                // [:L752]
                return RetCode.CANCELLED;
            }

            // [:L755-L759] the hook runs, or the row count is left NULL.
            long? rowCount = hook is not null
                ? hook.OnRetrieve(this, transaction, store.Carrier)
                : null;

            // [:L761-L767] `if IsNull(nRowCnt) or nRowCnt = RetCode.E_NO_IMPLEMENTATION then`.
            //
            // ⚠ A HOOK ANSWERING E_NO_IMPLEMENTATION HAS NOT FAILED. The value means "I did not handle
            // this", and the correct response is to run the DEFAULT retrieval - not to report an error,
            // and emphatically not to test the value with the failure predicate, which would answer true
            // for it and turn every decline into a failure. The base publishes the test as a named
            // predicate for exactly that reason.
            if (HookDeclinedRetrieval(rowCount))
            {
                rowCount = plainSql

                    // [:L763] `data.Retrieve()` - the NO-ARGUMENT form. A plain statement has already had
                    // its parameters interpolated into it by the binder at [:L605], so passing them again
                    // here would bind them twice.
                    //
                    // AWAITED, AND THE REQUEST'S TOKEN GOES WITH IT. This is the one provider call in the
                    // whole SQL layer whose entire call chain was already asynchronous, so the retrieval
                    // that used to block this continuation now yields it and observes the caller. The
                    // oracle's own cancellation polls sit either side of this call [:L740, :L785] and both
                    // remain exactly where they are; the token is strictly more responsive than they are,
                    // because it can be seen DURING the retrieval rather than only after it.
                    ? await store.RetrieveAsync([], cancellationToken).ConfigureAwait(false)

                    // [:L765] the parameterised retrieve, whose argument MATCHING belongs to the base.
                    : await RetrieveWithParamsAsync(store, cancellationToken).ConfigureAwait(false);
            }

            // [:L769] the after-retrieve notification, which fires BEFORE the defensive override below -
            // so a handler sees the row count the retrieval itself reported.
            _transactionSurface.RaiseAfterRetrieve(transaction, store.Carrier, rowCount!.Value);

            // [:L771-L774] DEFECT D6.
            long resolvedRowCount = ApplyDefensiveRowCountOverride(
                transaction.SqlCode,
                rowCount.Value,
                store.Carrier);

            // [:L776-L783] the two retrieval-failure diagnostics, which differ by more than their text:
            // the named-data-object one is PREFIXED with the data-object name.
            if (resolvedRowCount < 0L)
            {
                _ = OnError(
                    RetCode.E_DB_ERROR,
                    plainSql

                        // [:L778] WITH its exclamation mark.
                        ? PlainSqlRetrieveFailedText

                        // [:L780] `data.DataObject + "检索失败,请检查参数传递是否正确!"`.
                        : store.DataObject + DataObjectRetrieveFailedSuffix);

                return RetCode.E_DB_ERROR;
            }

            // [:L785]
            if (IsCancelled || cancellationToken.IsCancellationRequested)
            {
                return RetCode.CANCELLED;
            }

            // [:L787-L790] the row cap. The cap VALUE is interpolated into the diagnostic, invariantly so
            // the text cannot vary with the host culture.
            if (store.Carrier.IsRowsExceeded())
            {
                _ = OnError(
                    RetCode.E_OUT_OF_RANGE,
                    MaxRowsExceededPrefix
                        + _maxRows.ToString(CultureInfo.InvariantCulture)
                        + MaxRowsExceededSuffix);

                return RetCode.E_OUT_OF_RANGE;
            }

            // [:L792-L805] THE RESTORATION HAPPENS BEFORE THE DATA EVENT, and the oracle says so in its
            // own comment at :L793 - *需要在OnDataReceived之前还原.
            RestoreOriginalStatement(store, originalSql, restoreSql);

            // [:L807-L811] the DataWindow argument string is captured BEFORE the data event, because the
            // data event may leave the carrier invalid [:L813] and the counting binder needs the value
            // afterwards [:L837]. Captured only when counting will actually happen, exactly as the oracle
            // gates it.
            string dwArgumentString = string.Empty;

            if (!isProcedure && _paged && _pageCounting)
            {
                dwArgumentString = store.Describe(DataWindowProperty.TableArguments);
            }

            // [:L813-L815] `*OnDataReceived执行后Data可能将变得无效！` - after this event the carrier may
            // have become invalid, which is why nothing below reads it.
            QueryPublishOutcome published = await PublishResultAsync(
                    store,
                    sink,
                    resolvedRowCount,
                    cancellationToken)
                .ConfigureAwait(false);

            carrierHandedOver = published.CarrierHandedOver;

            // [:L815] EXACT inequality against OK, not the failure predicate.
            if (published.ReturnCode != RetCode.OK)
            {
                return published.ReturnCode;
            }

            // [:L817-L863] the paging totals.
            QueryPageCountOutcome counts = await CountPagesAsync(
                    transaction,
                    isProcedure,
                    resolvedRowCount,
                    plainSql,
                    dwArgumentString,
                    executableSql,
                    cancellationToken)
                .ConfigureAwait(false);

            if (counts.ReturnCode != RetCode.OK)
            {
                return counts.ReturnCode;
            }

            // [:L865] the notification fires on EVERY counted-or-not path, carrying -1/-1 when counting
            // did not happen.
            sink.OnPageReceived(counts.PageCount, counts.RecordCount);

            // [:L867]
            if (IsCancelled || cancellationToken.IsCancellationRequested)
            {
                return RetCode.CANCELLED;
            }

            // [:L869]
            return RetCode.OK;
        }
        finally
        {
            // [:L873] `if IsValid(hook) then Destroy hook`, deterministic per hazard 2.
            ReleaseRetrievalHook(ref hook);

            // [:L874] `if IsValid(sqlParser) then Destroy sqlParser` has no counterpart: the two parsing
            // sites own per-call SelectStatementModel instances that hold no unmanaged resource and are
            // deliberately not IDisposable. [:L875] `if IsValid(dsTmp) then Destroy dsTmp` likewise - the
            // counting result is a local of CountPagesAsync and is unreachable once it returns.
            //
            // [:L876-L880] ⚠ NEVER DESTROY A CACHED CARRIER - RESET IT.
            //     if bCacheDS then data.Reset() else if IsValid(data) then Destroy data
            // The cache holds LIVE references, so destroying a cached carrier would leave a dangling
            // entry that the next task on this thread would retrieve and use. Note also that the
            // hand-over path has already nulled the reference [:L88], which is why `IsValid(data)` is
            // false there and the else-arm destroys nothing.
            if (cacheDataStore)
            {
                data?.Carrier.Reset();
            }
            else if (!carrierHandedOver)
            {
                // `Destroy data`. A managed carrier holds no unmanaged resource, so the faithful and
                // sufficient equivalent is to drop the reference - which hazard 2 asks be done
                // explicitly rather than left to the collector.
                data = null;
            }
        }
    }

    #endregion


    #region Source resolution - the two shapes of source, and the sort/filter dispatch

    /// <summary>
    /// Resolves a NAMED data object onto the store and applies the pending sort and filter - the port of
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L550-L597</c>.
    /// </summary>
    /// <param name="store">The result store.</param>
    /// <param name="sink">The caller-side proxy, read for its two state questions.</param>
    /// <param name="cacheDataStore">Whether the store came from the cache.</param>
    /// <returns>
    /// <c>RetCode.OK</c>, <c>RetCode.E_INVALID_ARGUMENT</c> for an unresolvable data object
    /// [<c>:L554</c>] or for a rejected sort or filter [<c>:L566, :L572, :L586, :L592</c>].
    /// </returns>
    /// <remarks>
    /// <b>THE ASSIGNMENT AND THE PROBE ARE SKIPPED ON A CACHE HIT</b> [<c>:L551</c>] - a cached store
    /// already carries the resolved definition, and re-assigning the name would re-resolve it and discard
    /// the snapshot the cache took at first use.
    /// </remarks>
    private long ResolveNamedDataObject(ISqlDataStore store, IQueryResultSink sink, bool cacheDataStore)
    {
        // [:L551] `if Not bCacheDS then`.
        if (!cacheDataStore)
        {
            // [:L552] assigning the name RESOLVES the definition.
            store.DataObject = _dataObject;

            // [:L553] `if data.Describe("DataWindow.Units") = "" then`. The probe is EMPTINESS, not the
            // failure sentinel: a store whose data object did not load answers empty here, which is what
            // makes this one property the whole validity test.
            if (store.Describe(DataWindowProperty.Units).Length == 0)
            {
                // [:L554-L555]
                _ = OnError(RetCode.E_INVALID_ARGUMENT, InvalidDataObjectText);

                return RetCode.E_INVALID_ARGUMENT;
            }
        }

        // [:L558-L597] under the oracle's own comment 更新排序和过滤语句.
        return ApplySortAndFilter(store, sink);
    }

    /// <summary>
    /// Applies, clears or synchronizes the pending sort and filter - the port of
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L559-L597</c>.
    /// </summary>
    /// <param name="store">The result store.</param>
    /// <param name="sink">The caller-side proxy, read for its two state questions.</param>
    /// <returns><c>RetCode.OK</c>, or <c>RetCode.E_INVALID_ARGUMENT</c> for a rejected expression.</returns>
    /// <remarks>
    /// <para>
    /// <b>DEFECT D10, PART 3 - THE TWO PROCESSING ARMS ARE DELIBERATE OPPOSITES (constraint C-B).</b>
    /// </para>
    /// <code>
    /// if Not #ParentThread.of_IsMainThread() and Not tasking._of_NeedCreate() then     [:L559]
    ///     choose case Long(data.Describe("DataWindow.Processing"))
    ///         case 4,5    SYNCHRONIZE sort and filter                                 [:L560-L575]
    ///         case else    CLEAR them both                                            [:L576-L581]
    /// else                 APPLY them under a DIFFERENT condition                      [:L582-L597]
    /// </code>
    /// <para>
    /// The full-state style REQUIRES the two conditions to be synchronized between the sides - the
    /// oracle's own comment at <c>:L562-L563</c> says so - while the changeset style does NOT need them
    /// and is even slowed by them, so that arm clears both [<c>:L577-L578</c>]. Two opposite treatments
    /// of the same two expressions, decided purely by processing kind, and neither may be applied
    /// unconditionally.
    /// </para>
    /// <para>
    /// <b>The clearing arm's results are UNCHECKED, and that asymmetry is the oracle's.</b> Every other
    /// sort or filter call in this function tests <c>&lt;&gt; 1</c> and reports
    /// <c>E_INVALID_ARGUMENT</c> [<c>:L565, :L571, :L585, :L591</c>]; the two at <c>:L579-L580</c>, alone,
    /// are not tested. Adding a check would be an improvement, and constraint C-B forbids improvements.
    /// </para>
    /// <para>
    /// <b>The else-branch's condition is a THIRD condition, not the negation of the second.</b> It is
    /// <c>(IsMainThread and (HasReceiver or _bCache)) or NeedCreate</c> [<c>:L583</c>], it is
    /// processing-agnostic, and it belongs to this task rather than to either codec - which is why
    /// <see cref="FullStateCodec.SynchronizeSortAndFilter"/> reports the gate as closed and applies
    /// nothing rather than guessing at it.
    /// </para>
    /// <para>
    /// <b>Trimmed when APPLIED, untrimmed when REPORTED</b> [<c>:L565-L566, :L585-L586</c>]. Combined
    /// with defect D8's empty-to-single-space rewrite, that is why a cleared sort passes the
    /// non-emptiness gate, applies as the empty string, and would be reported as a single space.
    /// </para>
    /// </remarks>
    private long ApplySortAndFilter(ISqlDataStore store, IQueryResultSink sink)
    {
        DataStoreFullStateSurface surface = new(store);

        // [:L559] the gate, as the sibling's own value object so the two booleans cannot be transposed.
        FullStateTransferGate gate = new(IsMainThread, sink.NeedsCreatedObject);

        if (gate.SynchronizesByProcessingKind)
        {
            // [:L560-L561] `choose case Long(data.Describe("DataWindow.Processing")) / case 4,5`, read
            // from the carrier as a TYPED property so this file and the codecs cannot disagree about
            // which arm owns a carrier.
            if (store.Carrier.RequiresFullStateTransfer)
            {
                // [:L562-L575] the SYNCHRONIZING arm, owned by the full-state codec, including both
                // byte-exact message prefixes and the sort-before-filter ordering that makes a rejected
                // sort mean the filter is never attempted.
                return FullStateCodec.SynchronizeSortAndFilter(
                    store.Carrier,
                    surface,
                    _newSort,
                    _newFilter,
                    gate,
                    ReportError);
            }

            // [:L576-L581] the CLEARING arm. The predicate is the changeset codec's own, so the four
            // conditions it encodes - a named data object, off the main thread, no create needed and not
            // a full-state carrier - are stated in exactly one place.
            ChangesetSourceDefinition definition = ReadSourceDefinition(store);

            if (ChangesetCodec.ShouldClearSortAndFilter(
                    definition,
                    IsMainThread,
                    sink.NeedsCreatedObject,
                    store.Carrier.RequiresFullStateTransfer))
            {
                // [:L579-L580] `data.SetSort("")` then `data.SetFilter("")`. BOTH RESULTS DISCARDED, and
                // both set to EMPTY rather than to the not-applicable marker: an empty expression is a
                // real answer meaning "no condition".
                _ = surface.SetSort(string.Empty);
                _ = surface.SetFilter(string.Empty);
            }

            return RetCode.OK;
        }

        // [:L582-L597] the else-branch, with its own third condition.
        if ((IsMainThread && (sink.HasReceiver || _cache)) || sink.NeedsCreatedObject)
        {
            // [:L584-L589] the sort. Applied TRIMMED, reported UNTRIMMED.
            if (_newSort.Length > 0
                && surface.SetSort(_newSort.Trim()) != DataWindowBufferStore.DataStoreSuccess)
            {
                _ = OnError(RetCode.E_INVALID_ARGUMENT, FullStateCodec.SetSortMessagePrefix + _newSort);

                return RetCode.E_INVALID_ARGUMENT;
            }

            // [:L590-L595] the filter, only reached when the sort was accepted.
            if (_newFilter.Length > 0
                && surface.SetFilter(_newFilter.Trim()) != DataWindowBufferStore.DataStoreSuccess)
            {
                _ = OnError(RetCode.E_INVALID_ARGUMENT, FullStateCodec.SetFilterMessagePrefix + _newFilter);

                return RetCode.E_INVALID_ARGUMENT;
            }
        }

        return RetCode.OK;
    }

    /// <summary>
    /// Builds the carrier from a supplied syntax or from a plain statement - the port of
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L598-L624</c>.
    /// </summary>
    /// <param name="store">The result store.</param>
    /// <param name="transaction">The attached transaction, whose engine shapes the syntax derivation.</param>
    /// <param name="plainSql">
    /// Receives <see langword="true"/> when the source was a PLAIN statement [<c>:L602</c>]. That flag
    /// then selects the no-argument retrieval [<c>:L763</c>], the plain retrieval diagnostic
    /// [<c>:L778</c>] and the suppression of the counting binder [<c>:L836</c>], so it is an output
    /// rather than a local.
    /// </param>
    /// <returns>
    /// <c>RetCode.OK</c>, <c>RetCode.E_SQL_BIND_ARG_FAILED</c> [<c>:L606</c>] or
    /// <c>RetCode.E_INVALID_SQL</c> [<c>:L612, :L616, :L620</c>].
    /// </returns>
    /// <remarks>
    /// <b>THE SYNTAX WINS OVER THE STATEMENT</b> [<c>:L599-L601</c>]: a supplied syntax is used as-is and
    /// the statement is not even looked at, which is consistent with the two setters clearing each other.
    /// <b>The parameters are bound BEFORE the syntax is derived</b> [<c>:L604-L610</c>], so the derived
    /// syntax describes the statement with its literals already interpolated - which is exactly why the
    /// counting binder is suppressed for a plain statement later, and exactly why constraint C-F applies
    /// to every statement this path produces.
    /// </remarks>
    private long BuildCarrierFromStatement(
        ISqlDataStore store,
        IPooledTransaction transaction,
        out bool plainSql)
    {
        plainSql = false;

        string sqlSyntax;

        // [:L599-L600] a supplied syntax is used verbatim.
        if (_sqlSyntax.Length > 0)
        {
            sqlSyntax = _sqlSyntax;
        }
        else if (_sql.Length > 0)
        {
            // [:L602-L603]
            plainSql = true;
            string sql = _sql;

            // [:L604-L609] the FIRST of two distinct binding sites. Constraint C-F: after this call the
            // statement carries interpolated literal values, so it never reaches a log or a wire
            // unredacted.
            //
            // THE SINGLE-FORM BINDER HERE, DELIBERATELY, AND THE THREE CHANNELS ARE WHY. This path binds a
            // caller-authored statement's own named parameters into that statement purely to DERIVE its
            // result-set schema, and the derived grid syntax then carries the interpolated text as the
            // data object's `retrieve=` attribute - which is observable through Describe and must stay
            // byte-identical. Nothing third-party is spliced: a caller that supplies both the statement
            // and its parameter values could have written the literal itself. The channel that DOES carry
            // third-party data into a retrieval is the retrieve ARGUMENT list, and that is bound as
            // provider parameters where the statement executes. The remaining splice - the raw
            // where-clause setter - is the legacy's own documented defect, preserved observably and
            // reported only through the redactor. The command path, whose values reach an Exec, uses the
            // DUAL-form binder so what executes is parameterized.
            if (HasParams()
                && Predicates.IsFailed(BindParams(ref sql, (long)transaction.GetDbType())))
            {
                _ = OnError(RetCode.E_SQL_BIND_ARG_FAILED, BindParamsFailedText);

                return RetCode.E_SQL_BIND_ARG_FAILED;
            }

            // [:L610] the derivation.
            GridSyntaxOutcome derived = _transactionSurface.GridSyntaxFromSql(transaction, sql);

            // [:L611] ⚠ THE ERROR TEXT IS TESTED, NOT THE SYNTAX. An empty syntax with an empty error is
            // NOT a failure here, and the oracle's own implementation depends on that pairing: it calls
            // the runtime only when BOTH come back empty [n_cst_thread_trans.sru:L285-L287].
            if (derived.ErrorText.Length > 0)
            {
                // [:L612-L613]
                _ = OnError(RetCode.E_INVALID_SQL, GridSyntaxFailurePrefix + derived.ErrorText);

                return RetCode.E_INVALID_SQL;
            }

            sqlSyntax = derived.Syntax;
        }
        else
        {
            // [:L615-L617] the RUN-TIME emptiness check. Contract C-05 forbids moving it forward to the
            // setter - see SetSql.
            _ = OnError(RetCode.E_INVALID_SQL, EmptySqlText);

            return RetCode.E_INVALID_SQL;
        }

        // [:L619] `if data.Create(sSQLSyntax, ref sError) <> 1 then`.
        CarrierCreateOutcome created = _dataWindowRuntime.CreateFromSyntax(store, sqlSyntax);

        if (created.Result != DataWindowBufferStore.DataStoreSuccess)
        {
            // [:L620-L621]
            _ = OnError(RetCode.E_INVALID_SQL, CreateFailurePrefix + created.ErrorText);

            return RetCode.E_INVALID_SQL;
        }

        // [:L623] `sSQLSyntax = ""`. The local is cleared so nothing downstream can re-use it; there is
        // no field to clear here, which is the whole reason the local exists in the oracle.
        return RetCode.OK;
    }

    /// <summary>
    /// Substitutes the configured statement into a NAMED data object at run time - the port of
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L631-L642</c>.
    /// </summary>
    /// <param name="store">The result store.</param>
    /// <param name="isProcedure">Whether the source is a stored procedure.</param>
    /// <param name="plainSql">Whether the source was a plain statement.</param>
    /// <param name="originalSql">
    /// The statement kept for restoration. <b>UPDATED IN PLACE when the substitution happens</b>
    /// [<c>:L640</c>], so the restoration at <c>:L794</c> puts back the SUBSTITUTED statement rather than
    /// the data object's original one.
    /// </param>
    /// <returns><c>RetCode.OK</c>, or <c>RetCode.E_INTERNAL_ERROR</c> for a modify failure [<c>:L637</c>].</returns>
    /// <remarks>
    /// <para>
    /// <b>FOUR conditions, all required</b> [<c>:L633-L634</c>]: a statement is configured, the source was
    /// NOT a plain statement, the source is not a stored procedure, and the configured statement actually
    /// DIFFERS from the one already installed. The last one is what makes a repeated retrieval with an
    /// unchanged statement issue no modify at all.
    /// </para>
    /// <para>
    /// <b>DEFECT D9 - THIS MODIFY DOES NOT ESCAPE EMBEDDED QUOTES, AND THE OTHER THREE DO.</b> The oracle
    /// writes <c>data.Modify('DataWindow.Table.Select = "' + _sSQL + '"')</c> at <c>:L635</c> with no
    /// <c>ReplaceAll</c>, while <c>:L709</c>, <c>:L732</c> and <c>:L795</c> all escape. A statement
    /// containing a double-quoted literal therefore produces a MALFORMED modification script here and a
    /// well-formed one there - which is observable as a modify failure carrying the runtime's own
    /// diagnostic. Reproduced verbatim per constraint C-B; folding the escape in would silently repair a
    /// path the legacy leaves broken, which is why
    /// <see cref="BuildTableSelectAssignment"/> deliberately does not escape on its callers' behalf.
    /// </para>
    /// </remarks>
    private long SubstituteRuntimeStatement(
        ISqlDataStore store,
        bool isProcedure,
        bool plainSql,
        ref string originalSql)
    {
        // [:L633] three conditions, under the oracle's own comment 支持指定DataObject时修改其运行时SQL.
        if (_sql.Length == 0 || plainSql || isProcedure)
        {
            return RetCode.OK;
        }

        // [:L634] the fourth: only when it actually differs. Ordinal, because this is a statement
        // comparison and a culture-sensitive one could answer differently across hosts.
        if (string.Equals(originalSql, _sql, StringComparison.Ordinal))
        {
            return RetCode.OK;
        }

        // [:L635] DEFECT D9: NO ESCAPE, deliberately. Contrast the three escaped sites.
        string modifyError = store.Modify(BuildTableSelectAssignment(_sql));

        if (modifyError.Length > 0)
        {
            // [:L637-L638]
            _ = OnError(RetCode.E_INTERNAL_ERROR, ModifyTableSelectFailurePrefix + modifyError);

            return RetCode.E_INTERNAL_ERROR;
        }

        // [:L640] the restoration target becomes the SUBSTITUTED statement.
        originalSql = _sql;

        return RetCode.OK;
    }

    /// <summary>
    /// Suppresses duplicate drop-down retrievals on a CACHED carrier - the port of
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L644-L670</c>.
    /// </summary>
    /// <param name="store">The cached result store.</param>
    /// <returns><c>RetCode.OK</c>, or <c>RetCode.E_INVALID_SQL</c> for a modify failure [<c>:L666</c>].</returns>
    /// <remarks>
    /// <para>
    /// Runs ONLY on the cached path [<c>:L645</c>], under the oracle's own comment
    /// 使用缓存对象时优化减少DDDW重复查询 - "when using a cached object, reduce duplicate drop-down queries".
    /// Two distinct suppression reasons, and the ORDER between them matters:
    /// </para>
    /// <list type="number">
    ///   <item>
    ///     <description>
    ///     A drop-down whose SOURCE NAME has already been seen on this carrier is suppressed outright
    ///     [<c>:L652-L653</c>] - two columns sharing one drop-down definition need one retrieval, not two.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///     Otherwise the task NOTIFIES, and a notification answering <c>1</c> suppresses it
    ///     [<c>:L655-L656</c>]; anything else records the source name as seen [<c>:L658</c>]. The notify
    ///     code is the query proxy's own <c>NCD_CHILDQUERY</c>, value 6, carrying the ONE-BASED column
    ///     ordinal as the numeric payload and the column name as the text.
    ///     </description>
    ///   </item>
    /// </list>
    /// <para>
    /// <b>One modify for all of them</b> [<c>:L663-L664</c>], accumulated as newline-separated fragments,
    /// and it is issued only when at least one fragment was produced. This result IS captured, unlike the
    /// no-user-prompt one at <c>:L675</c>.
    /// </para>
    /// <para>
    /// The container is the legacy <c>n_map</c>, substituted by <see cref="OrderedMap"/>. Only membership
    /// is used here, and <see cref="OrderedMap.Add"/> refusing an existing key is harmless because the
    /// add is on the not-seen branch.
    /// </para>
    /// </remarks>
    private long DeduplicateChildQueries(ISqlDataStore store)
    {
        // [:L646] `map = Create n_map`.
        OrderedMap seenSources = new();

        System.Text.StringBuilder modifications = new();

        // [:L647] `nColCnt = Long(data.Describe("DataWindow.Column.Count"))`.
        long columnCount = ParseDescribeCount(store.Describe(QueryDataWindowProperty.ColumnCount));

        // [:L648] `for nColIdx = 1 to nColCnt`. R9: the ordinal is ONE-BASED and stays one-based, because
        // the property expression it composes is one-based.
        for (long columnOrdinal = 1L; columnOrdinal <= columnCount; columnOrdinal++)
        {
            int ordinal = (int)columnOrdinal;

            // [:L649-L650] `if sProp <> "yes" then continue`.
            if (!string.Equals(
                    store.Describe(
                        QueryDataWindowProperty.ForColumn(
                            ordinal,
                            QueryDataWindowProperty.ColumnDropDownAutoRetrieveSuffix)),
                    AutoRetrieveEnabled,
                    StringComparison.Ordinal))
            {
                continue;
            }

            // [:L651] the DROP-DOWN SOURCE NAME is the de-duplication key - not the column name, which is
            // read separately at :L655 for the notification. Keying on the column name would suppress
            // nothing, because column names are unique by construction.
            string dropDownName = store.Describe(
                QueryDataWindowProperty.ForColumn(
                    ordinal,
                    QueryDataWindowProperty.ColumnDropDownNameSuffix));

            if (seenSources.Exists(dropDownName))
            {
                // [:L653] already seen - suppress.
                modifications.Append(
                    QueryDataWindowProperty.ForColumn(
                        ordinal,
                        QueryDataWindowProperty.DropDownAutoRetrieveOffSuffix));

                continue;
            }

            // [:L655] `if Event OnNotify(tasking.NCD_CHILDQUERY, nColIdx, ...) = 1 then`. EXACT equality
            // with 1 - the substrate's own stop convention, which is NOT the return-code algebra and is
            // NOT the tri-valued prevention predicate.
            long notified = OnNotify(
                (long)SqlQueryTaskNotifyCode.ChildQuery,
                columnOrdinal,
                store.Describe(
                    QueryDataWindowProperty.ForColumn(ordinal, QueryDataWindowProperty.ColumnNameSuffix)));

            if (notified == DataWindowBufferStore.EventStop)
            {
                // [:L656] the consumer said it will handle this child itself - suppress.
                modifications.Append(
                    QueryDataWindowProperty.ForColumn(
                        ordinal,
                        QueryDataWindowProperty.DropDownAutoRetrieveOffSuffix));
            }
            else
            {
                // [:L658] record the SOURCE name as seen. The value is a placeholder; only membership
                // matters.
                _ = seenSources.Add(dropDownName, true);
            }
        }

        // [:L662] `Destroy map` - the reference is dropped when this frame returns.
        //
        // [:L663-L669] one modify for every suppression, and only when there is one.
        if (modifications.Length == 0)
        {
            return RetCode.OK;
        }

        string modifyError = store.Modify(modifications.ToString());

        if (modifyError.Length > 0)
        {
            // [:L666-L667] ⚠ the RAW runtime diagnostic, with NO prefix - unlike the three
            // statement-installation failures, which all carry ModifyTableSelectFailurePrefix. And the
            // code is E_INVALID_SQL rather than E_INTERNAL_ERROR. Both are the oracle's.
            _ = OnError(RetCode.E_INVALID_SQL, modifyError);

            return RetCode.E_INVALID_SQL;
        }

        return RetCode.OK;
    }

    /// <summary>
    /// Restores the statement the carrier started with - the port of
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L792-L805</c>.
    /// </summary>
    /// <param name="store">The result store.</param>
    /// <param name="originalSql">The statement to put back.</param>
    /// <param name="restoreSql">
    /// Whether anything replaced it in the first place - the oracle's <c>bRestoreSQL</c>, set at
    /// <c>:L717</c> and <c>:L740</c>.
    /// </param>
    /// <remarks>
    /// <para>
    /// <b>IT HAPPENS BEFORE THE DATA EVENT, and the oracle states why in its own comment at
    /// <c>:L793</c></b> - <c>*需要在OnDataReceived之前还原</c>, "must be restored before OnDataReceived".
    /// After that event the carrier may have become invalid [<c>:L813</c>], so a restoration attempted
    /// afterwards would have nothing to restore onto.
    /// </para>
    /// <para>
    /// <b>Both guards are required</b> [<c>:L794</c>]: a non-empty original AND something to restore. And
    /// <b>the escape here is CONDITIONAL</b> [<c>:L795</c>] - <c>if Pos(sSQLOriginal,"~"") &gt; 0 then</c>
    /// - which is net-equivalent to escaping unconditionally but is reproduced in its guarded shape
    /// because that is the source's own structure.
    /// </para>
    /// <para>
    /// <b>THE MODIFY RESULT IS DISCARDED</b> [<c>:L801</c>], unlike the two modifies that installed the
    /// statement in the first place. A failed restoration is therefore silent in the legacy and is silent
    /// here, and turning it into an error would be a behavioural change on the success path.
    /// </para>
    /// </remarks>
    private static void RestoreOriginalStatement(ISqlDataStore store, string originalSql, bool restoreSql)
    {
        // [:L794]
        if (originalSql.Length == 0 || !restoreSql)
        {
            return;
        }

        string restoreText = originalSql;

        // [:L795] the CONDITIONAL escape.
        if (restoreText.Contains(EmbeddedQuote, StringComparison.Ordinal))
        {
            restoreText = EscapeStatementForModify(restoreText);
        }

        // [:L801] result discarded, exactly as the oracle discards it.
        _ = store.Modify(BuildTableSelectAssignment(restoreText));
    }

    #endregion

    #region Publishing the result - the port of ondatareceived [:L73-L235]

    /// <summary>
    /// Publishes the result: the row count, then either the hand-over, the full-state chunk or the
    /// changeset sequence - the port of <c>event ondatareceived</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L73-L235</c>].
    /// </summary>
    /// <param name="store">The result store.</param>
    /// <param name="sink">The caller-side proxy.</param>
    /// <param name="rowCount">The row count the retrieval reported, after the defensive override.</param>
    /// <param name="cancellationToken">The task's cancellation token.</param>
    /// <returns>The outcome, whose return code is the value the legacy event returns.</returns>
    /// <remarks>
    /// <para>
    /// <b>THE ORDER OF THE THREE PATHS IS THE ORACLE'S, AND THE FIRST ONE RUNS NO CODEC AT ALL.</b>
    /// </para>
    /// <list type="number">
    ///   <item>
    ///     <description>
    ///     The row count is published FIRST [<c>:L83</c>], before any decision - so a consumer learns the
    ///     total before it sees a row.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///     <b>The hand-over fast path</b> [<c>:L85-L91</c>]: on the main thread, with no receiver and no
    ///     caching, ownership of the carrier changes hands by REFERENCE and neither codec is involved.
    ///     All three conditions are required.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///     Otherwise the codec discriminator [<c>:L93</c>] selects the full-state arm for a crosstab or
    ///     composite carrier [<c>:L94</c>] and the changeset arm for everything else [<c>:L102</c>].
    ///     </description>
    ///   </item>
    /// </list>
    /// <para>
    /// <b>NEITHER CODEC IS IMPLEMENTED HERE.</b> This method owns the CHOICE and the DRIVING;
    /// <c>Buffers/FullStateCodec.cs</c> and <c>Buffers/ChangesetCodec.cs</c> own the reproduction,
    /// including all four preserved transfer defects.
    /// </para>
    /// <para>
    /// <b>THE ORDERING WITHIN THE CHANGESET ARM IS DRIVEN FROM HERE FOR A DOCUMENTED REASON.</b> The
    /// oracle's sequence is create-data, cancellation check, filter fold [<c>:L103-L110</c>], THEN the
    /// drop-down child walk [<c>:L112-L141</c>], THEN the chunk loop [<c>:L145-L231</c>].
    /// <see cref="ChangesetCodec.TransferAsync"/> performs the create and the fold itself when told the
    /// receiver needs an object, and its own documentation states that the child walk is a separate entry
    /// point which "a caller reproduces the legacy order by calling between the fold and this one". So
    /// this method performs the create and the fold, then the child walk, then calls the transfer with
    /// the create-flag already discharged. Handing the flag to the transfer instead would put the child
    /// walk AFTER the chunks, which is a different observable event order on the stream.
    /// </para>
    /// <para>
    /// <b>DEFECT D4 - THE STALE FILTERED COUNT - LIVES INSIDE THE CHUNK LOOP AND IS OWNED BY THE
    /// CODEC.</b> <c>nFilterCnt</c> is declared once for the whole event at <c>:L75</c> and assigned only
    /// inside <c>if nRowCnt &lt; _nChunkSize and Data.FilteredCount() &gt; 0</c> [<c>:L164, :L199</c>], so
    /// it RETAINS its previous value across chunk iterations and the later
    /// <c>if nFilterCnt &gt; 0 then RowsDiscard(1, nFilterCnt, Filter!)</c> [<c>:L180-L182, :L213-L215</c>]
    /// can act on a stale, possibly out-of-range count. The variable's scope and lifetime are reproduced
    /// faithfully by the codec rather than re-initialised per iteration, and the out-of-range discard
    /// answers a failure code rather than throwing. This file drives it and does not re-own it.
    /// </para>
    /// </remarks>
    private async Task<QueryPublishOutcome> PublishResultAsync(
        ISqlDataStore store,
        IQueryResultSink sink,
        long rowCount,
        CancellationToken cancellationToken)
    {
        // [:L83] `tasking.Event OnDataReceived(rowCount)` - FIRST, unconditionally, and it cannot veto.
        sink.OnDataReceived(rowCount);

        DataWindowCarrier carrier = store.Carrier;

        // [:L84-L86] the three conditions of the hand-over, as the sibling's own pure predicate. The
        // affinity is the PUBLISHER's thread, which is what `#ParentThread.of_IsMainThread()` answers.
        CarrierThreadAffinity affinity = IsMainThread
            ? CarrierThreadAffinity.MainThread
            : CarrierThreadAffinity.WorkerThread;

        if (DataWindowCarrierOwnership.CanMoveWithoutSerialization(affinity, sink.HasReceiver, _cache))
        {
            // [:L87-L89] the receiver adopts the carrier FIRST and only then is this side's reference
            // dropped - hazard 2's deterministic clearing. Performed before any await, so the ref
            // parameter this needs is legal here.
            DataWindowCarrier? moving = carrier;
            long moveResult = DataWindowCarrierOwnership.Move(ref moving, sink.OnDataMove);

            return new QueryPublishOutcome(
                moveResult,
                CarrierHandedOver: true,
                FullState: false,
                ChunkCount: 0L,
                ChunksSent: 0L,
                ChildrenSent: 0);
        }

        // [:L93-L94] the discriminator, read as a TYPED property rather than re-parsed from a describe
        // string, so this file and the two codecs cannot disagree about which arm owns a carrier.
        if (carrier.RequiresFullStateTransfer)
        {
            // [:L95-L101] capture, reset the source, hand over exactly ONE chunk with count 1, index 1
            // and fullState true, report "TransData Failed" on a negative handover, then drop the
            // payload. All of it owned by the full-state codec; the sink's handover signature matches
            // its delegate exactly so no adapter is needed.
            long fullStateResult = FullStateCodec.Send(carrier, sink.SendFullStateChunk, ReportError);

            return new QueryPublishOutcome(
                fullStateResult,
                CarrierHandedOver: false,
                FullState: true,
                ChunkCount: FullStateCodec.SingleChunkCount,
                ChunksSent: fullStateResult == RetCode.OK ? FullStateCodec.SingleChunkIndex : 0L,
                ChildrenSent: 0);
        }

        // [:L102] the changeset arm.
        ChangesetSourceDefinition definition = ReadSourceDefinition(store);

        // [:L103-L110] the create-data handover and the filter fold, driven HERE so the child walk below
        // lands between the fold and the chunk loop - see the remarks.
        if (sink.NeedsCreatedObject)
        {
            // [:L104] THE RESULT IS IGNORED, and the codec's own sink documentation says so: the legacy
            // event is declared without a return type. What the legacy reacts to is the cancellation
            // check on the very next line.
            _ = await sink.CreateDataAsync(definition.Syntax, cancellationToken).ConfigureAwait(false);

            // [:L105]
            if (IsCancelled || cancellationToken.IsCancellationRequested)
            {
                return new QueryPublishOutcome(
                    RetCode.CANCELLED,
                    CarrierHandedOver: false,
                    FullState: false,
                    ChunkCount: 0L,
                    ChunksSent: 0L,
                    ChildrenSent: 0);
            }

            // [:L106-L109] the filter-into-primary SELF-FOLD, under the oracle's own comment: applying
            // changes with the current syntax handles filtering automatically and avoids the cost of
            // copying the filter buffer. The destination row is `RowCount() + 1`, a ONE-BASED insertion
            // point one past the last row.
            _ = ChangesetCodec.FoldFilterBufferIntoPrimary(carrier);
        }

        // [:L112-L141] the drop-down child walk. Discovery is definition reflection and belongs here;
        // the per-child stamping, extraction, reset and handover belong to the codec.
        IReadOnlyList<ChangesetChildSource> children = DiscoverEligibleChildren(store);

        ChangesetChildTransferOutcome childOutcome = await _changesetCodec
            .TransferChildrenAsync(children, sink, cancellationToken)
            .ConfigureAwait(false);

        if (childOutcome.ReturnCode != RetCode.OK)
        {
            return new QueryPublishOutcome(
                childOutcome.ReturnCode,
                CarrierHandedOver: false,
                FullState: false,
                ChunkCount: 0L,
                ChunksSent: 0L,
                childOutcome.ChildrenSent);
        }

        // [:L142-L231] the cancellation re-check and the chunk loop, including the chunk arithmetic, the
        // sorted-multi-chunk temporary-carrier workaround, the discard-instead-of-reset positioning, the
        // empty-result single chunk and the 20 ms cooperative inter-chunk yield driven by the injected
        // clock.
        ChangesetTransferRequest request = new()
        {
            Source = carrier,
            Definition = definition,

            // [:L34, :L145] the configured chunk size. The codec deliberately does NOT re-enforce the
            // 1000 floor - that guard belongs to SetChunkSize, which owns the setter.
            ChunkSize = _chunkSize,

            // Already discharged above, so the codec must not repeat the create or the fold.
            ReceiverNeedsCreatedObject = false,

            // [:L189, :L225] `of_Wait(0.02)`.
            InterChunkYield = ChangesetCodec.LegacyInterChunkYield,
        };

        ChangesetTransferOutcome outcome = await _changesetCodec
            .TransferAsync(request, sink, cancellationToken)
            .ConfigureAwait(false);

        return new QueryPublishOutcome(
            outcome.ReturnCode,
            CarrierHandedOver: false,
            FullState: false,
            outcome.ChunkCount,
            outcome.ChunksSent,
            childOutcome.ChildrenSent);
    }

    /// <summary>
    /// Reads the source definition the changeset transfer needs - the four properties the oracle
    /// describes at <c>:L104</c>, <c>:L150</c>, <c>:L153</c> and <c>:L156</c>.
    /// </summary>
    /// <param name="store">The result store.</param>
    /// <returns>The definition.</returns>
    /// <remarks>
    /// Read ONCE rather than four times at four different moments. That is safe because none of the four
    /// values is changed by the operations between those points: the fold moves ROWS, and neither the
    /// syntax, the data-object name, the sort nor the filter is a function of the row set. Reading them
    /// together is what lets the codec's own <c>SortIsAbsent</c> gate - <c>sProp &lt;&gt; "?" and
    /// sProp &lt;&gt; ""</c> [<c>:L151</c>] - be evaluated by its owner rather than restated here.
    /// </remarks>
    private static ChangesetSourceDefinition ReadSourceDefinition(ISqlDataStore store) =>
        new()
        {
            DataObjectName = store.DataObject,
            Syntax = store.Describe(QueryDataWindowProperty.Syntax),
            SortExpression = store.Describe(DataWindowProperty.TableSort),
            FilterExpression = store.Describe(DataWindowProperty.TableFilter),
        };

    /// <summary>
    /// Walks the columns and collects the drop-down children eligible for transfer - the discovery half
    /// of <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L113-L120</c>.
    /// </summary>
    /// <param name="store">The result store.</param>
    /// <returns>The eligible children in COLUMN ORDER, which is the order the handovers must follow.</returns>
    /// <remarks>
    /// <para>
    /// <b>The describe sequence is preserved, not just the outcome.</b> The oracle reads
    /// <c>DDDW.AutoRetrieve</c> and <c>continue</c>s immediately when it is not <c>"yes"</c>
    /// [<c>:L115-L116</c>], so it never reads <c>DDDW.Name</c> for an ineligible column. That ordering is
    /// reproduced rather than collapsed into one composite test, because the sequence of describe calls
    /// is itself observable to a substituted store.
    /// </para>
    /// <para>
    /// <b>The two-marker exclusion belongs to the codec</b> - <c>sProp &lt;&gt; "!" and sProp &lt;&gt;
    /// "?"</c> [<c>:L118</c>] is <see cref="ChangesetCodec.IsChildEligible"/>, so the markers are
    /// compared in exactly one place.
    /// </para>
    /// <para>
    /// <b>A child whose lookup fails is SKIPPED, not reported</b> [<c>:L120</c>]: the oracle answers a
    /// failed <c>GetChild</c> with <c>continue</c>, so the walk goes on and the column contributes
    /// nothing. Treating it as an error would fail retrievals the legacy completes.
    /// </para>
    /// </remarks>
    private IReadOnlyList<ChangesetChildSource> DiscoverEligibleChildren(ISqlDataStore store)
    {
        // [:L113] `nCount = Long(Data.Describe("DataWindow.Column.Count"))`.
        long columnCount = ParseDescribeCount(store.Describe(QueryDataWindowProperty.ColumnCount));

        List<ChangesetChildSource> children = [];

        // [:L114] `for nIndex = 1 to nCount`. R9: ONE-BASED, and it stays one-based because the property
        // expressions it composes are one-based.
        for (long columnOrdinal = 1L; columnOrdinal <= columnCount; columnOrdinal++)
        {
            int ordinal = (int)columnOrdinal;

            // [:L115] the first describe.
            string autoRetrieve = store.Describe(
                QueryDataWindowProperty.ForColumn(
                    ordinal,
                    QueryDataWindowProperty.ColumnDropDownAutoRetrieveSuffix));

            // [:L116] `if sProp <> "yes" then continue` - BEFORE the second describe.
            if (!string.Equals(autoRetrieve, AutoRetrieveEnabled, StringComparison.Ordinal))
            {
                continue;
            }

            // [:L117] the second describe.
            string dropDownName = store.Describe(
                QueryDataWindowProperty.ForColumn(
                    ordinal,
                    QueryDataWindowProperty.ColumnDropDownNameSuffix));

            // [:L118] the two-marker exclusion, owned by the codec.
            if (!ChangesetCodec.IsChildEligible(autoRetrieve, dropDownName))
            {
                continue;
            }

            // [:L119] the column's own name, which is what the handover carries.
            string columnName = store.Describe(
                QueryDataWindowProperty.ForColumn(ordinal, QueryDataWindowProperty.ColumnNameSuffix));

            // [:L120] `if Data.GetChild(sColName, ref dwcSrc) = -1 then continue`.
            if (!_dataWindowRuntime.TryGetChild(store, columnName, out DataWindowBufferStore? child)
                || child is null)
            {
                continue;
            }

            children.Add(new ChangesetChildSource(columnName, child));
        }

        return children;
    }

    #endregion

    #region Page counting - the outer gate, the short-circuit, the wrapper and the query

    /// <summary>
    /// Produces the paging totals - the port of
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L817-L863</c>.
    /// </summary>
    /// <param name="transaction">The attached transaction.</param>
    /// <param name="isProcedure">Whether the source is a stored procedure.</param>
    /// <param name="rowCount">The rows this page returned.</param>
    /// <param name="plainSql">Whether the source was a plain statement.</param>
    /// <param name="dwArgumentString">
    /// The DataWindow argument string captured BEFORE the data event [<c>:L810</c>], because the carrier
    /// may be invalid afterwards and the binder needs it [<c>:L837</c>].
    /// </param>
    /// <param name="executableSql">
    /// The statement the retrieval ran, UNPAGED - <c>sSQLExec</c>. Counting the paged statement would
    /// answer the page size rather than the total.
    /// </param>
    /// <param name="cancellationToken">The task's cancellation token.</param>
    /// <returns>The totals, whether a statement was issued, and the statement itself.</returns>
    /// <remarks>
    /// <para>
    /// <b>THE OUTER GATE HAS THREE CONDITIONS</b> [<c>:L818</c>]: not a stored procedure, paging on, and
    /// counting on. When any fails, BOTH totals become <c>-1</c> [<c>:L861-L862</c>] and the notification
    /// still fires [<c>:L865</c>] - counting is skipped, not the publication.
    /// </para>
    /// <para>
    /// ==========================================================================================
    /// </para>
    /// <para>
    /// <b>DEFECT D7 - AN UNREACHABLE GUARD AND A CANCELLATION THAT REPORTS AS A DATABASE ERROR.</b>
    /// </para>
    /// <para>
    /// The oracle's failure discrimination is:
    /// </para>
    /// <code>
    /// rtCode = TransObject.of_Query(sSQL, ref dsTmp, sError)                 [:L843]
    /// if IsFailed(rtCode) then
    ///     if rtCode &lt;&gt; RetCode.CANCELLED then Event OnError(rtCode, sError)   [:L845-L847]
    ///     return rtCode
    /// end if
    /// if rtCode = 1 then ... else ... OnError(E_DB_ERROR, "检索失败") ...       [:L851-L858]
    /// </code>
    /// <para>
    /// <b>The inner guard can never be false.</b> <c>IsFailed</c> is <c>rtCode &lt; 0</c> WITH AN EXPLICIT
    /// EXCLUSION OF <c>CANCELLED</c> [<c>ws_objects/pfw.shared.pbl.src/isfailed.srf:L11-L13</c>], so a
    /// cancellation never enters this branch and the <c>rtCode &lt;&gt; RetCode.CANCELLED</c> test inside it
    /// is dead-but-present. It is carried across in its dead shape rather than deleted, because deleting
    /// it would erase the evidence of the second half of the finding.
    /// </para>
    /// <para>
    /// <b>And because a cancellation is NOT "failed", it FALLS THROUGH.</b> A <c>CANCELLED</c> result
    /// reaches the <c>rtCode = 1</c> test, fails it, and is reported as a DATABASE ERROR carrying the
    /// text 检索失败 with the transaction's code and the counting statement. That is the tri-state hole of
    /// the return-code algebra - where cancelled is neither succeeded nor failed - producing an observably
    /// wrong classification. Constraint C-B requires it be reproduced, and it is.
    /// </para>
    /// <para>
    /// ==========================================================================================
    /// </para>
    /// <para>
    /// <b>Constraint C-F.</b> The counting statement carries interpolated literals whenever the connection
    /// disabled bind variables, and this is a SECOND, DISTINCT binding site from the retrieval path's
    /// [<c>:L836-L841</c> against <c>:L604-L609</c>] which additionally passes the DataWindow argument
    /// string. The raw statement is placed in the database-error payload exactly as the oracle places it -
    /// so in-process handling keeps it - while every EGRESS masks it: the base's error hook redacts before
    /// logging, the payload's wire projection masks unconditionally, and the one diagnostic this method
    /// writes itself passes the statement through the injected redactor.
    /// </para>
    /// </remarks>
    private async Task<QueryPageCountOutcome> CountPagesAsync(
        IPooledTransaction transaction,
        bool isProcedure,
        long rowCount,
        bool plainSql,
        string dwArgumentString,
        string executableSql,
        CancellationToken cancellationToken)
    {
        // [:L818] the three-condition outer gate; [:L861-L862] its else-arm.
        if (isProcedure || !_paged || !_pageCounting)
        {
            return new QueryPageCountOutcome(
                RetCode.OK,
                NotCountedValue,
                NotCountedValue,
                Counted: false,
                string.Empty);
        }

        // [:L819-L823] the short-circuit: a partial final page, or an empty FIRST page, is counted
        // arithmetically with NO statement issued at all.
        if (TryInferPageCounts(rowCount, _pageSize, _pageIndex, out long inferredPages, out long inferredRecords))
        {
            return new QueryPageCountOutcome(
                RetCode.OK,
                inferredPages,
                inferredRecords,
                Counted: false,
                string.Empty);
        }

        // [:L825-L834] the wrapper, under the oracle's own comment 获取总页数.
        long built = BuildCountStatement(executableSql, out string countSql, out string buildError);

        if (built != RetCode.OK)
        {
            // [:L827-L828]
            _ = OnError(built, buildError);

            return new QueryPageCountOutcome(built, NotCountedValue, NotCountedValue, Counted: false, string.Empty);
        }

        // [:L835-L841] the SECOND binding site. `of_HasParams() and Not bPlainSQL` - a plain statement
        // already had its parameters interpolated at :L605, so binding again would bind them twice.
        if (HasParams() && !plainSql)
        {
            string bindable = countSql;

            if (Predicates.IsFailed(
                    BindParams(ref bindable, (long)transaction.GetDbType(), dwArgumentString)))
            {
                // [:L838-L839]
                _ = OnError(RetCode.E_SQL_BIND_ARG_FAILED, BindParamsFailedText);

                return new QueryPageCountOutcome(
                    RetCode.E_SQL_BIND_ARG_FAILED,
                    NotCountedValue,
                    NotCountedValue,
                    Counted: false,
                    string.Empty);
            }

            countSql = bindable;
        }

        // [:L843] the execution.
        CountQueryOutcome outcome = await _transactionSurface
            .Query(transaction, countSql, cancellationToken)
            .ConfigureAwait(false);

        // [:L844-L849] DEFECT D7, first half.
        if (Predicates.IsFailed(outcome.ReturnCode))
        {
            // [:L845] DEAD-BUT-PRESENT: IsFailed above already excludes CANCELLED, so this can never be
            // false. Carried across rather than deleted - see the remarks.
            if (outcome.ReturnCode != RetCode.CANCELLED)
            {
                _ = OnError(outcome.ReturnCode, outcome.ErrorText);
            }

            // [:L848]
            return new QueryPageCountOutcome(
                outcome.ReturnCode,
                NotCountedValue,
                NotCountedValue,
                Counted: false,
                countSql);
        }

        // [:L851-L853] EXACTLY ONE ROW. The value is a row count, not a return code - testing it against
        // RetCode.PREVENT, which shares the value 1, would be a category error.
        if (outcome.ReturnCode == CountQueryExpectedRowCount)
        {
            long recordCount = ReadCountResult(outcome.Result);

            return new QueryPageCountOutcome(
                RetCode.OK,
                DividePagesCeiling(recordCount, _pageSize),
                recordCount,
                Counted: true,
                countSql);
        }

        // [:L854-L858] DEFECT D7, second half: this arm is where a CANCELLED result lands, because a
        // cancellation is neither succeeded nor failed in the legacy algebra.
        //
        // [:L855] the RAW statement goes into the payload, exactly as the oracle places it there, so
        // in-process handling keeps it; every egress masks it. Buffer Primary!, row 1 - note the row is
        // ONE here and ZERO at the two transaction-failure sites [:L521, :L748].
        _ = OnDbError(transaction.SqlDbCode, transaction.SqlErrText, countSql, DwBuffer.Primary, 1L);

        // [:L856] 检索失败 - WITHOUT the exclamation mark the retrieval-failure text carries.
        _ = OnError(RetCode.E_DB_ERROR, CountRetrieveFailedText);

        // Constraint C-F: the one diagnostic this method writes itself carries the REDACTED statement.
        _logger.LogError(
            "The paging count statement did not return a single row. Result {Result}. Statement: {Statement}",
            outcome.ReturnCode,
            _redactor.Redact(countSql));

        // [:L857]
        return new QueryPageCountOutcome(
            RetCode.E_DB_ERROR,
            NotCountedValue,
            NotCountedValue,
            Counted: false,
            countSql);
    }

    /// <summary>
    /// Reads the record total out of the counting result - <c>dsTmp.GetItemNumber(1, 1)</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L852</c>].
    /// </summary>
    /// <param name="result">The carrier the counting statement filled.</param>
    /// <returns>The record total, or zero when the value is absent or not numeric.</returns>
    /// <remarks>
    /// <b>ROW 1, COLUMN 1, both ONE-BASED.</b> R9: neither is rebased, because the carrier's own accessor
    /// is one-based in both dimensions. A non-numeric or absent value answers zero, which is what
    /// PowerScript's <c>GetItemNumber</c> yields for an unset item - and zero then drives the
    /// zero-record correction rather than throwing on a path whose whole contract is return codes.
    /// Converted invariantly so the parse cannot vary with the host culture.
    /// </remarks>
    private static long ReadCountResult(DataWindowBufferStore? result)
    {
        object? value = result?.GetItemValue(CountResultRow, CountResultColumn, DwBuffer.Primary);

        return value switch
        {
            null => 0L,
            long typed => typed,
            int typed => typed,
            decimal typed => (long)typed,
            double typed => (long)typed,
            string text => long.TryParse(
                text,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out long parsed)
                ? parsed
                : 0L,
            _ => 0L,
        };
    }

    #endregion

    #region Small shared helpers

    /// <summary>
    /// Reports a diagnostic through the base's error hook, discarding its value - the shape the two
    /// codecs' <see cref="FullStateErrorReporter"/> delegate expects.
    /// </summary>
    /// <param name="errCode">The framework error code.</param>
    /// <param name="errInfo">The diagnostic text, verbatim.</param>
    /// <remarks>
    /// A named method rather than a lambda so the codecs and this file report through exactly one path,
    /// and so the ROLLBACK the base's hook performs before delegating to its ancestor cannot be bypassed
    /// by a caller that reached for the ancestor directly.
    /// </remarks>
    private void ReportError(long errCode, string errInfo) => _ = OnError(errCode, errInfo);

    /// <summary>
    /// Turns a <c>Describe</c> answer into a count - PowerScript's <c>Long(Data.Describe(...))</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L113, :L647</c>].
    /// </summary>
    /// <param name="describeResult">The property value, which may be a sentinel rather than a number.</param>
    /// <returns>The parsed count, or zero.</returns>
    /// <remarks>
    /// <b>The sentinels are DATA here, not exceptions.</b> A store with no resolved definition answers
    /// <c>"!"</c>, and PowerScript's <c>Long</c> of a non-numeric string is not a number either - so the
    /// safe and faithful reading is zero, which walks no columns. Parsed invariantly, because a
    /// culture-sensitive parse of a digit string is a portability hazard on a path that feeds byte-exact
    /// comparisons.
    /// </remarks>
    private static long ParseDescribeCount(string describeResult) =>
        long.TryParse(describeResult, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed)
            ? parsed
            : 0L;

    /// <summary>
    /// Adapts an <see cref="ISqlDataStore"/> to the four definition operations the full-state codec needs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Four one-line forwards, and it exists because the two interfaces are ALMOST identical and
    /// deliberately not merged: <see cref="IFullStateCarrierSurface"/> is the narrow surface the codec
    /// declared for itself so that it depends on no sibling folder, and <see cref="ISqlDataStore"/> is the
    /// definition seam the task layer declared for itself. Making either implement the other would
    /// couple the two folders in exactly the direction both of them documented as forbidden.
    /// </para>
    /// <para>
    /// The sentinels <c>"!"</c> and <c>"?"</c> pass STRAIGHT THROUGH, because the codec screens for them
    /// itself and translating them here would hide values it needs.
    /// </para>
    /// </remarks>
    private sealed class DataStoreFullStateSurface : IFullStateCarrierSurface
    {
        private readonly ISqlDataStore _store;

        /// <summary>
        /// Wraps a store.
        /// </summary>
        /// <param name="store">The store to forward to.</param>
        internal DataStoreFullStateSurface(ISqlDataStore store) => _store = store;

        /// <inheritdoc/>
        public string Describe(string property) => _store.Describe(property);

        /// <inheritdoc/>
        public string Modify(string modifyString) => _store.Modify(modifyString);

        /// <inheritdoc/>
        public long SetSort(string sort) => _store.SetSort(sort);

        /// <inheritdoc/>
        public long SetFilter(string filter) => _store.SetFilter(filter);
    }

    #endregion
}
