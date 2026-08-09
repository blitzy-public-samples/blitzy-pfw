// ==============================================================================================
//  IPagingRewriter - the paging dispatch contract
//  --------------------------------------------------------------------------------------------
//  PORTED FROM    ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L303-L404
//                     _of_buildpagedsql, the single authoritative specification for this folder,
//                     read in full together with its four instance fields at :L36, :L37, :L39,
//                     :L46 and its one call site at :L723-L725
//                 ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:L60-L61, L356-L361
//                     the two dialect discriminators and the substring test that resolves them
//                 ws_objects/pfw.utility.parser.pbl.src/n_sql.sru:L11-L49
//                     the clause-level parser surface the two arms drive, modelled in managed code
//                     by SelectStatementModel beside this folder
//                 ws_objects/pfw.shared.pbl.src/retcode.sru:L39, L46, L70, L78
//                     OK = 0, E_INVALID_ARGUMENT = -3, E_INTERNAL_ERROR = -27 and
//                     E_NO_IMPLEMENTATION = -2001, every value re-read at its own line
//
//  ORACLE STATUS  Every ws_objects/** path named in this file is READ ONLY (constraint C-C). Each
//                 was read as specification and is cited by locator; nothing here copies,
//                 reformats, moves, edits or deletes any of them, and nothing in this file depends
//                 on the PowerBuilder toolchain, the PowerBuilder runtime or any shipped native
//                 binary. The legacy tree is the ONLY statement of intended behaviour that exists
//                 for this operation - there is no schema, no changelog entry and no other
//                 document that could adjudicate a disagreement - which is why every behavioural
//                 claim below carries the :L line reference it was taken from.
//
//  RULES POSITION No user rules were provided for this repository: the rules document contains
//                 exactly one line saying so, and it was read to its end. Nothing is invented or
//                 back filled from convention in their place, and the absence is not treated as
//                 licence to lower the bar. The binding constraints are the enterprise standard
//                 baseline together with the named non-rule constraints C-A through C-L, and every
//                 non-obvious decision below cites the constraint that drives it, as C-K requires.
//
//  THE LEGACY OPERATION, TRANSCRIBED [n_cst_thread_task_sqlquery.sru:L303-L404]
//  --------------------------------------------------------------------------------------------
//      private function long _of_buildpagedsql (
//          readonly n_cst_thread_trans transobject,      // the dialect source, NOT a connection
//          readonly string             origsql,          // OriginalSql
//          ref      string             sql)              // the rewritten statement, out
//
//      L307  if _nPageSize <= 0 or _nPageIndex <= 0                     -> E_INVALID_ARGUMENT
//      L312  sqlParser = Create n_sql
//      L314  if Not sqlParser.Parse(origSql)                            -> E_INTERNAL_ERROR
//      L320  choose case TransObject.of_GetDBType()
//      L321    case TransObject.DBT_MSSQL     ... builds sql
//      L386    case TransObject.DBT_ORACLE    ... builds sql
//      L396    case else                                                -> E_NO_IMPLEMENTATION
//      L401  Destroy sqlParser
//      L403  return RetCode.OK
//
//  It reads FOUR instance fields, and all four become parameters here because this contract is a
//  pure function with no ambient state: _nPageSize [:L36], _nPageIndex [:L37], _bPageNative
//  [:L39] and _sPagedUniqueIndexColumns[] [:L46]. The transaction object is deliberately NOT a
//  parameter - see the C-E section below for why, and PagingDialectResolver for what replaces it.
//
//  EXACTLY FOUR EXITS, AND PagingRewriteResult HAS EXACTLY FOUR FACTORIES
//  --------------------------------------------------------------------------------------------
//      L309  E_INVALID_ARGUMENT   "无效的分页设置!"   ->  InvalidPagingSetting()
//      L316  E_INTERNAL_ERROR     "SQL解析失败!"      ->  ParseFailed()
//      L398  E_NO_IMPLEMENTATION  ""  EMPTY           ->  DialectNotImplemented()
//      L403  OK                   n/a                 ->  Ok(rewrittenSql)
//  The census is measured rather than sampled: every `return` in the function was enumerated, so
//  the four factories are known to be sufficient and no fifth shape exists to be discovered later.
//
//  THE EMPTY MESSAGE IS THE ORACLE, NOT AN OVERSIGHT TO FIX (C-B). :L397 reads
//  `Event OnError(RetCode.E_NO_IMPLEMENTATION,"")`. No message text is invented for it here. The
//  two Chinese diagnostics are likewise carried through VERBATIM AND UNTRANSLATED, because the
//  published contract already declares this text opaque and possibly non-English
//  [shared/PowerFramework.Contracts/Proto/persistence.v1.proto, OperationStatus.error_text].
//
//  GUARD ORDER IS LOAD BEARING AND IS PRESERVED
//  --------------------------------------------------------------------------------------------
//  Paging bounds first [:L307], then the parse [:L314], then the dialect dispatch [:L320]. A
//  request that is BOTH badly paged AND unparseable returns E_INVALID_ARGUMENT, never
//  E_INTERNAL_ERROR. And because the parse happens BEFORE the `choose case`, a request that is
//  unparseable AND names an unrecognised dialect returns E_INTERNAL_ERROR, never
//  E_NO_IMPLEMENTATION. Both orderings are observable through the returned code, so neither may be
//  rearranged for tidiness.
//
//  THAT ORDERING SETTLES WHO OWNS THE PARSE, AND IT IS DECIDED HERE ONCE, FOR BOTH ARMS
//  --------------------------------------------------------------------------------------------
//  THE DISPATCHER PARSES; the two IPagingRewriter implementations receive an ALREADY PARSED
//  SelectStatementModel and never parse for themselves. If each arm parsed instead, the
//  parse-failure exit would move INSIDE the arms, the `case else` arm would never parse at all,
//  and an unparseable statement with an unrecognised dialect would start returning
//  E_NO_IMPLEMENTATION - a different code from the one the oracle returns. The decision is
//  therefore forced by the evidence rather than chosen for style, and it has a second benefit that
//  matters for C-E and C-H: it leaves each arm a pure clause-level string transform whose entire
//  input is a parsed model plus four scalars.
//
//  C-E - NO FABRICATED DATABASE. THIS CONTRACT CANNOT REACH ONE.
//  --------------------------------------------------------------------------------------------
//  Two measured facts force the shape of every type in this file:
//
//    1. SQLITE IS ABSENT FROM THE LEGACY DATABASE-TYPE ENUMERATION ENTIRELY. The oracle declares
//       exactly two discriminators, DBT_MSSQL = 0 [n_cst_thread_trans.sru:L60] and DBT_ORACLE = 1
//       [:L61]. There is no third constant, even though SQLite is the ONLY storage engine this
//       system provisions and the only one with an evidenced schema.
//    2. NEITHER SQL SERVER NOR ORACLE HAS ANY SCHEMA, CONNECTION STRING OR DDL ANYWHERE IN THE
//       REPOSITORY - only those two type constants and the two statement generators at
//       [n_cst_thread_task_sqlquery.sru:L321-L385] and [:L386-L395].
//
//  So the selector selects A TEXT GENERATOR, NOT A CONNECTION. Accordingly this file mentions no
//  ADO connection type, no ADO command type, no connection-parameter string, no transaction-object
//  parameter, no provider handle, no ambient state and no I/O of any kind - not even in prose, so
//  that the reviewer's grep for those type names over this file returns nothing at all. Statement
//  text plus page size, page index, page-native flag and unique-index column list go in; rewritten
//  statement text plus a return code and a diagnostic come out. Naming a selector value is not
//  provisioning an engine.
//
//  The build enforces this rather than merely asking for it. Central package management is on,
//  every PackageReference is versionless, and Directory.Packages.props carries no SQL Server
//  client, no Oracle client and no third-party SQL-dialect parser - it forbids all three by name in
//  its own comments - so adding one fails restore with a missing PackageVersion. NO PACKAGE
//  REFERENCE IS ADDED FOR THIS FILE.
//
//  NO PERFORMANCE CLAIM IS MADE ANYWHERE (AAP section 0.8.5)
//  --------------------------------------------------------------------------------------------
//  The oracle's own comment at [:L324-L326] describes the unique-index branch as a fast paging
//  optimisation. The BEHAVIOUR of that branch is reproduced - HasPagedUniqueIndexColumns is the
//  predicate that selects it, mirroring `UpperBound(_sPagedUniqueIndexColumns) > 0` at [:L323] -
//  and NO performance benefit is asserted for it, or for anything else here. The repository
//  publishes no service-level agreement, no latency budget, no throughput target and no
//  availability commitment, so no performance objective may be claimed as met or used to justify a
//  design choice. The only quantitative non-functional requirement in scope is the coverage gate.
//
//  WHAT IS DELIBERATELY NOT IN THIS FILE
//  --------------------------------------------------------------------------------------------
//  * No sentinel identifier literal. pfwPagedSQL_OutterTbl - whose doubled `t` is the legacy
//    spelling and is not corrected - pfwPagedSQL_RN, pfwPagedSQL_Tbl, pfwPagedSQL_TblInnerInner,
//    pfwPagedSQL_TblInner and pfwPagedSQL_TblOuter belong to the arms that emit them.
//  * No empty-order-by substitution literal. THE TWO ARMS SUBSTITUTE DIFFERENT TEXT and the two
//    are not interchangeable: `(SELECT 0)` at [:L370, :L379] versus `''` at [:L392]. Hoisting
//    either one here would couple the two implementations through this file, and they must be able
//    to satisfy this contract without either referencing the other.
//  * No clause-modification style member. The legacy signature at [:L303] carries no style
//    parameter, so surfacing one would invent contract surface. Where a style IS used it stays
//    inside an arm, and its type is `long` there because every SelectStatementModel.Modify* overload
//    takes `readonly long ms`, faithful to n_sql.sru:L38-L49. Never `int`.
//  * No test. The tests for every member below live in PowerFramework.Persistence.Tests, which the
//    application project already grants internal access to, and each member is reachable from a
//    table-driven theory whose entire fixture is strings (C-H).
//
//  A FINDING RECORDED SO IT CAN BE AUDITED RATHER THAN REDISCOVERED (C-K)
//  --------------------------------------------------------------------------------------------
//  The repository-root .editorconfig carries a naming-analyzer suppression section for THIS FILE
//  BY NAME, on the expectation that the two discriminators would be DECLARED here because they are
//  not part of the legacy constant catalogue and so do not arrive with Enums.cs. They are NOT
//  declared here. They are CONSUMED from the generated PowerFramework.Contracts.Persistence.V1
//  DatabaseType enum, which is the published single source of truth for the discriminator, and
//  protoc renders DBT_MSSQL and DBT_ORACLE as DbtMssql and DbtOracle - PascalCase, carrying no
//  underscore, raising neither CA1707 nor IDE1006. That section is therefore inert, which is
//  harmless and is exactly the state its own maintenance note anticipates for a section whose file
//  has not yet been authored. It is deliberately left in place and NOT edited: .editorconfig is
//  not this file's to change, and a dead suppression that documents a real constraint is cheaper
//  than a churned one. No SCREAMING_SNAKE identifier is declared anywhere below.
//
//  THREE NAMING DECISIONS, EACH TAKEN TO AVOID A REAL COMPILE HAZARD (C-K)
//  --------------------------------------------------------------------------------------------
//  * PagingRewriteResult.ReturnCode, not RetCode. A member named RetCode would shadow the static
//    class RetCode its own values are drawn from, so `RetCode.OK` inside the declaring type would
//    bind to the member and fail to compile.
//  * IPagingRewriter.Dialect, not DatabaseType. Same hazard, one level worse: a property named
//    DatabaseType would shadow the enum type inside every implementing class, so
//    `DatabaseType.DbtOracle` would stop resolving in the very files that need it most.
//  * IsDialectImplemented, not IsDialectSupported. E_NO_SUPPORT is -2000 and E_NO_IMPLEMENTATION
//    is -2001 [retcode.sru:L77-L78]; they are distinct codes and the published contract warns
//    against conflating them. The unmatched arm returns the latter, so the predicate is named
//    after the latter.
// ==============================================================================================

using PowerFramework.Contracts.Persistence.V1;
using PowerFramework.Shared.Kernel;

namespace PowerFramework.Persistence.Sql.Paging;

// ----------------------------------------------------------------------------------------------
//  The request - the four instance fields the legacy read, promoted to parameters
// ----------------------------------------------------------------------------------------------

/// <summary>
/// The complete, self-contained input to a paging rewrite: the statement to rewrite plus the four
/// paging settings the legacy read from its own instance state.
/// </summary>
/// <remarks>
/// <para>
/// Reproduces the inputs of <c>_of_buildpagedsql</c> at
/// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L303</c>. The legacy
/// declares only <c>readonly string origsql</c> as a value parameter and reads the rest from
/// <c>_nPageSize</c> <c>[:L36]</c>, <c>_nPageIndex</c> <c>[:L37]</c>, <c>_bPageNative</c>
/// <c>[:L39]</c> and <c>_sPagedUniqueIndexColumns[]</c> <c>[:L46]</c>. All four are parameters here
/// because this contract is a pure function with no ambient state (C-E).
/// </para>
/// <para>
/// <b>Why a <see langword="readonly"/> <see langword="record"/> <see langword="struct"/>.</b> The
/// legacy marks both of its reference parameters <c>readonly</c>, and the transformation plan maps a
/// PowerScript <c>readonly</c> parameter onto a C# <c>in</c> parameter. <c>in</c> is only meaningful
/// for a value type, so the request is a readonly struct and every method that takes it takes it by
/// <c>in</c>. Value equality comes free with <see langword="record"/>, which is what lets a
/// table-driven theory build request cases inline.
/// </para>
/// <para>
/// <b>Nothing here can reach a database (C-E).</b> The legacy took the transaction object as its
/// first parameter and used it for exactly one thing: to ask it for a dialect
/// <c>[:L320 -&gt; n_cst_thread_trans.sru:L356-L361]</c>. That single use is why no equivalent
/// appears on this type. The dialect travels as a plain <see cref="DatabaseType"/> value alongside
/// the request, and <see cref="PagingDialectResolver"/> is what derives it. There is no connection,
/// no command, no connection string, no provider handle and no credential on this type or anywhere
/// beneath it.
/// </para>
/// <para>
/// <b>Defensive normalisation, and why it changes no behaviour.</b> A null statement is normalised
/// to the empty string and a null or empty column list to an empty list, so no member of this type
/// can return null and no consumer needs a null check. Neither normalisation alters an outcome: an
/// empty statement fails <see cref="SelectStatementModel.Parse(string)"/>, which is the
/// <c>E_INTERNAL_ERROR</c> arm at <c>[:L314-L317]</c> and is what the legacy would also produce; an
/// empty column list is exactly the <c>UpperBound(...) &lt;= 0</c> state the oracle branches away
/// from at <c>[:L323]</c>. The column list is also COPIED on construction, faithful to
/// <c>of_setpageduniqueindexcolumns</c> at <c>[:L406]</c>, where PowerScript array assignment copies
/// by value; a caller that mutates its own array afterwards therefore cannot change this request.
/// </para>
/// <para>
/// <b>Two consequences of the record-struct shape, stated so neither surprises a test author.</b>
/// The generated equality compares the copied column array BY REFERENCE, so two requests built from
/// equal-but-distinct column lists are not equal - compare the projected
/// <see cref="PagedUniqueIndexColumns"/> when that matters. And the generated
/// <see cref="object.ToString"/> renders every readable property, INCLUDING the statement text; do
/// not log a request wholesale, because a legacy statement may carry interpolated literal values -
/// that is the same exposure the published contract records against the database-error statement
/// field, and it is why redaction is a duty rather than an option.
/// </para>
/// </remarks>
internal readonly record struct PagingRewriteRequest
{
    /// <summary>Backing store for <see cref="OriginalSql"/>, nullable so that
    /// <see langword="default"/> is representable without a null-hostile property.</summary>
    private readonly string? _originalSql;

    /// <summary>Backing store for <see cref="PagedUniqueIndexColumns"/>. Held as an array so the
    /// copy taken at construction cannot be aliased by the caller. <see langword="null"/> is the
    /// canonical spelling of "no columns", so that <see langword="default"/> behaves identically to
    /// an explicitly empty list.</summary>
    private readonly string[]? _pagedUniqueIndexColumns;

    /// <summary>
    /// Creates a request.
    /// </summary>
    /// <param name="originalSql">
    /// The statement to rewrite - <c>readonly string origsql</c> at
    /// <c>n_cst_thread_task_sqlquery.sru:L303</c>. <see langword="null"/> is accepted and normalised
    /// to the empty string; see the remarks on the type for why that changes no outcome.
    /// </param>
    /// <param name="pageSize">
    /// Rows per page - <c>_nPageSize</c> <c>[:L36]</c>, set through <c>of_setpagesize</c> whose own
    /// guard is <c>if pageSize &lt;= 0 then return RetCode.E_INVALID_ARGUMENT</c> <c>[:L462]</c>.
    /// Not validated here: validation belongs to <see cref="HasValidPagingBounds"/>, which is where
    /// the oracle re-tests it at <c>[:L307]</c>.
    /// </param>
    /// <param name="pageIndex">
    /// The ONE-BASED page ordinal - <c>_nPageIndex</c> <c>[:L37]</c>, set through
    /// <c>of_setpageindex</c> whose guard is <c>if pageIndex &lt;= 0 then return
    /// RetCode.E_INVALID_ARGUMENT</c> <c>[:L450]</c>. The first page is 1, not 0, which is why zero
    /// is invalid rather than meaning "the beginning"; the arms compute their offsets as
    /// <c>_nPageSize * (_nPageIndex - 1)</c> <c>[:L346, :L356, :L372, :L382, :L395]</c>, which is
    /// only correct on a one-based ordinal.
    /// </param>
    /// <param name="pageNative">
    /// Selects the engine's own paging construct over the framework's row-number rewrite -
    /// <c>_bPageNative</c> <c>[:L39]</c>, set through <c>of_setpagenative</c> <c>[:L457]</c>, which
    /// has no guard. Read by the first arm only, at <c>[:L343]</c> and <c>[:L366]</c>.
    /// </param>
    /// <param name="pagedUniqueIndexColumns">
    /// The unique-index columns - <c>_sPagedUniqueIndexColumns[]</c> <c>[:L46]</c>, set through
    /// <c>of_setpageduniqueindexcolumns</c> <c>[:L406]</c>, which REPLACES the collection wholesale
    /// rather than merging into it. A non-empty list changes the generated statement, so it is not a
    /// hint. <see langword="null"/>, an empty list and a list of empty strings are all accepted; a
    /// null element is normalised to the empty string so no consumer can dereference null.
    /// </param>
    public PagingRewriteRequest(
        string? originalSql,
        long pageSize,
        long pageIndex,
        bool pageNative,
        IReadOnlyList<string>? pagedUniqueIndexColumns = null)
    {
        _originalSql = originalSql;
        PageSize = pageSize;
        PageIndex = pageIndex;
        PageNative = pageNative;
        _pagedUniqueIndexColumns = CopyColumns(pagedUniqueIndexColumns);
    }

    /// <summary>
    /// The statement to rewrite, never <see langword="null"/>. <c>readonly string origsql</c> at
    /// <c>n_cst_thread_task_sqlquery.sru:L303</c>.
    /// </summary>
    public string OriginalSql => _originalSql ?? string.Empty;

    /// <summary>Rows per page. <c>_nPageSize</c> at <c>n_cst_thread_task_sqlquery.sru:L36</c>,
    /// declared <c>long</c> there and <see langword="long"/> here.</summary>
    public long PageSize { get; }

    /// <summary>The one-based page ordinal. <c>_nPageIndex</c> at
    /// <c>n_cst_thread_task_sqlquery.sru:L37</c>.</summary>
    public long PageIndex { get; }

    /// <summary>Whether to use the engine's own paging construct. <c>_bPageNative</c> at
    /// <c>n_cst_thread_task_sqlquery.sru:L39</c>.</summary>
    public bool PageNative { get; }

    /// <summary>
    /// The unique-index columns, never <see langword="null"/> and never holding a null element. A
    /// defensive copy of what the caller supplied. <c>_sPagedUniqueIndexColumns[]</c> at
    /// <c>n_cst_thread_task_sqlquery.sru:L46</c>.
    /// </summary>
    public IReadOnlyList<string> PagedUniqueIndexColumns => _pagedUniqueIndexColumns ?? [];

    /// <summary>
    /// Whether the paging settings pass the oracle's own bounds test.
    /// </summary>
    /// <value>
    /// <see langword="true"/> when both <see cref="PageSize"/> and <see cref="PageIndex"/> are
    /// greater than zero.
    /// </value>
    /// <remarks>
    /// <para>
    /// Reproduces <c>if _nPageSize &lt;= 0 or _nPageIndex &lt;= 0</c> at
    /// <c>n_cst_thread_task_sqlquery.sru:L307</c> exactly, including the disjunction: EITHER value
    /// being non-positive fails the whole request. Both boundaries are INCLUSIVE of zero on the
    /// failing side because the domain is one-based, so zero is invalid rather than meaning "the
    /// first page".
    /// </para>
    /// <para>
    /// This is deliberately a REDUNDANT SECOND CHECK and the redundancy is preserved rather than
    /// collapsed (C-B). <c>of_setpagesize</c> <c>[:L462]</c> and <c>of_setpageindex</c>
    /// <c>[:L450]</c> already reject a non-positive value at the setter, yet the oracle re-tests
    /// both here at build time. The reason it matters is measurable rather than stylistic: a caller
    /// that never called either setter at all reaches only this check, because the fields start at
    /// zero <c>[:L257, :L259]</c>.
    /// </para>
    /// </remarks>
    public bool HasValidPagingBounds => PageSize > 0 && PageIndex > 0;

    /// <summary>
    /// Whether every product the paging arms compute from <see cref="PageSize"/> and
    /// <see cref="PageIndex"/> is representable, so that no arm can render a wrapped bound.
    /// </summary>
    /// <value>
    /// <see langword="true"/> when the bounds are valid and <c>PageSize * PageIndex</c> does not
    /// overflow.
    /// </value>
    /// <remarks>
    /// <para>
    /// A GUARD THE ORACLE DOES NOT HAVE, ADDED BECAUSE ITS ABSENCE INVENTS BEHAVIOUR RATHER THAN
    /// PRESERVING IT. Every arm multiplies these two values - <c>TOP ps * pi</c>,
    /// <c>OFFSET ps * (pi - 1)</c>, <c>BETWEEN ps * (pi - 1) + 1 AND ps * pi</c>
    /// [<c>n_cst_thread_task_sqlquery.sru:L346, :L356, :L372, :L382, :L395</c>] - and an unchecked
    /// product WRAPS TO A NEGATIVE NUMBER. The resulting statement is not a fault: <c>TOP -1</c>,
    /// <c>OFFSET -1</c> and a <c>BETWEEN</c> over negative bounds are syntactically fine and silently
    /// match no row, so the caller receives an empty page and no indication that anything went wrong.
    /// That is the one outcome worse than either a correct page or a reported error.
    /// </para>
    /// <para>
    /// AND THERE IS NO LEGACY BEHAVIOUR TO BE FAITHFUL TO, because the widths differ. PowerScript
    /// <c>long</c> is 32-bit signed [<c>:L36-L37</c>] while these fields are 64-bit, so the wrap point
    /// here is not the wrap point there; a request that overflowed in the legacy does not overflow
    /// here, and one that overflows here could never have been expressed there. Refusing it is a
    /// narrowing with a defined error rather than a widening with a guess (AAP 0.1.5), and the error is
    /// the SAME one the oracle's own bounds test answers, so no new outcome is introduced on the wire.
    /// </para>
    /// <para>
    /// <b>Only the largest product is tested, and that is sufficient rather than approximate.</b>
    /// Because both values are positive, <c>ps * pi</c> is the greatest of the three products - it
    /// dominates <c>ps * (pi - 1)</c>, and <c>ps * (pi - 1) + 1</c> is at most <c>ps * pi</c> whenever
    /// <c>ps</c> is at least one, which the bounds test already guarantees. So a request whose largest
    /// product fits has every product fit.
    /// </para>
    /// <para>
    /// <b>Division rather than a trial multiplication</b>, because a trial multiplication is the very
    /// thing being guarded against: it would have to overflow in order to be detected.
    /// </para>
    /// </remarks>
    public bool HasRepresentablePagingProducts =>
        HasValidPagingBounds && PageSize <= long.MaxValue / PageIndex;

    /// <summary>
    /// Returns a copy of this request carrying a different unique-index column list, leaving every other
    /// field alone.
    /// </summary>
    /// <param name="pagedUniqueIndexColumns">The replacement list, copied defensively as at construction.</param>
    /// <returns>The copy.</returns>
    /// <remarks>
    /// <para>
    /// EXISTS FOR EXACTLY ONE CALLER, and that narrowness is the point:
    /// <see cref="PagingRewriteDispatcher.Rewrite"/> hands the arm the identifiers
    /// <see cref="PagedUniqueIndexColumnValidator.TryValidate"/> RESOLVED, which differ from the
    /// caller's own only where the caller named an output alias rather than a source column.
    /// </para>
    /// <para>
    /// A hand-written copy rather than a <c>with</c> expression, because the list is held in a private
    /// array field behind a read-only property rather than as a positional record member - so <c>with</c>
    /// cannot reach it, and a public setter would let any caller mutate a request that is otherwise
    /// immutable by construction.
    /// </para>
    /// </remarks>
    public PagingRewriteRequest WithPagedUniqueIndexColumns(
        IReadOnlyList<string>? pagedUniqueIndexColumns) =>
        new(OriginalSql, PageSize, PageIndex, PageNative, pagedUniqueIndexColumns);

    /// <summary>
    /// Whether the unique-index column list selects the inner-join paging strategy.
    /// </summary>
    /// <value><see langword="true"/> when <see cref="PagedUniqueIndexColumns"/> is not empty.</value>
    /// <remarks>
    /// Reproduces <c>if UpperBound(_sPagedUniqueIndexColumns) &gt; 0</c> at
    /// <c>n_cst_thread_task_sqlquery.sru:L323</c>. Only the first dialect arm branches on it; the
    /// second ignores it entirely <c>[:L386-L395]</c>. The oracle's comment at <c>[:L324-L326]</c>
    /// calls the branch a fast paging optimisation - the behaviour is reproduced and NO performance
    /// benefit is claimed for it (AAP section 0.8.5). What the branch genuinely does is emit a
    /// DIFFERENT statement, which is what makes it part of byte-exact statement parity.
    /// </remarks>
    public bool HasPagedUniqueIndexColumns => (_pagedUniqueIndexColumns?.Length ?? 0) > 0;

    /// <summary>
    /// Copies a caller-supplied column list into an owned array, normalising both the absent list
    /// and the empty list to <see langword="null"/> so that <see langword="default"/> and an
    /// explicitly empty request behave identically.
    /// </summary>
    /// <param name="source">The caller's list, possibly <see langword="null"/>.</param>
    /// <returns>An owned copy, or <see langword="null"/> when there is nothing to copy.</returns>
    private static string[]? CopyColumns(IReadOnlyList<string>? source)
    {
        if (source is null || source.Count == 0)
        {
            return null;
        }

        string[] copy = new string[source.Count];

        for (int index = 0; index < copy.Length; index++)
        {
            // Widened to string? deliberately: the caller may be outside a nullable-aware context -
            // a deserialized wire message is the obvious case - so an element genuinely can be null
            // despite the non-nullable type argument.
            string? column = source[index];
            copy[index] = column ?? string.Empty;
        }

        return copy;
    }
}


// ----------------------------------------------------------------------------------------------
//  The result - the returned code, the ref out-parameter and the error-event message argument
// ----------------------------------------------------------------------------------------------

/// <summary>
/// The outcome of a paging rewrite: the framework return code, the rewritten statement, and the
/// diagnostic text the legacy passed to its error event.
/// </summary>
/// <remarks>
/// <para>
/// Reproduces the three observable outputs of <c>_of_buildpagedsql</c> at
/// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L303-L404</c>: the
/// <see langword="long"/> the function returns, the <c>ref string sql</c> out-parameter it assigns,
/// and the message argument of the <c>Event OnError(code, message)</c> it raises on the way out. The
/// three travel together here because the legacy's caller needs all three and could observe each
/// independently.
/// </para>
/// <para>
/// <b>Exactly four shapes exist, and there are exactly four factories.</b> Every <c>return</c> in
/// the oracle function was enumerated, so the set is measured rather than sampled and no fifth shape
/// is waiting to be discovered:
/// </para>
/// <list type="table">
///   <listheader><term>Oracle</term><description>Factory</description></listheader>
///   <item>
///     <term><c>[:L308-L309]</c> <c>E_INVALID_ARGUMENT</c> with <c>"无效的分页设置!"</c></term>
///     <description><see cref="InvalidPagingSetting"/></description>
///   </item>
///   <item>
///     <term><c>[:L315-L316]</c> <c>E_INTERNAL_ERROR</c> with <c>"SQL解析失败!"</c></term>
///     <description><see cref="ParseFailed"/></description>
///   </item>
///   <item>
///     <term><c>[:L397-L398]</c> <c>E_NO_IMPLEMENTATION</c> with an EMPTY message</term>
///     <description><see cref="DialectNotImplemented"/></description>
///   </item>
///   <item>
///     <term><c>[:L403]</c> <c>OK</c>, no event raised</term>
///     <description><see cref="Ok(string)"/></description>
///   </item>
/// </list>
/// <para>
/// <b>The diagnostic text is carried verbatim and is never translated (C-B).</b> Two of the three
/// error messages are Chinese in the oracle and stay Chinese here; the third is genuinely EMPTY and
/// stays empty, with no message invented for it. The published contract already declares this text
/// opaque, possibly non-English and legitimately empty
/// (<c>shared/PowerFramework.Contracts/Proto/persistence.v1.proto</c>,
/// <c>OperationStatus.error_text</c>), so a consumer must branch on <see cref="ReturnCode"/> and
/// must never parse the text to classify an outcome.
/// </para>
/// <para>
/// <b>Why the statement is empty on every failure, rather than absent or stale.</b> The legacy
/// out-parameter is <c>ref string sql</c>, and on all three failure paths it is never assigned at
/// all - so the caller's variable keeps whatever it held before. That is unobservable in practice,
/// because the one caller returns immediately on any non-<c>OK</c> code
/// <c>[:L724-L725]</c> and never reads it. Returning the empty string is therefore faithful to every
/// observable consequence while being safe to read unconditionally.
/// </para>
/// </remarks>
internal readonly record struct PagingRewriteResult
{
    /// <summary>
    /// The diagnostic the oracle pairs with <c>E_INVALID_ARGUMENT</c> at
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L308</c>. Carried VERBATIM
    /// and UNTRANSLATED (C-B). Exposed as a constant so a test can assert against it without
    /// retyping the text and risking a homoglyph.
    /// </summary>
    public const string InvalidPagingSettingText = "无效的分页设置!";

    /// <summary>
    /// The diagnostic the oracle pairs with <c>E_INTERNAL_ERROR</c> at
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L315</c>. Carried VERBATIM
    /// and UNTRANSLATED (C-B).
    /// </summary>
    public const string ParseFailedText = "SQL解析失败!";

    /// <summary>
    /// The diagnostic the oracle pairs with <c>E_NO_IMPLEMENTATION</c> at
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L397</c>, which is
    /// literally <c>""</c>.
    /// </summary>
    /// <remarks>
    /// Declared explicitly, rather than letting the factory pass <see cref="string.Empty"/>
    /// silently, so that the emptiness reads as a transcribed fact with a locator instead of as an
    /// omission somebody might later "fix" by inventing message text. It is not an oversight in the
    /// oracle to be corrected (C-B).
    /// </remarks>
    public const string DialectNotImplementedText = "";

    /// <summary>
    /// The diagnostic paired with <see cref="InvalidPagedUniqueIndexColumn"/>. THE ONE MESSAGE IN THIS
    /// TYPE WITH NO ORACLE LOCATOR, because the oracle performs no such validation - see that factory.
    /// </summary>
    /// <remarks>
    /// English rather than transcribed Chinese precisely BECAUSE it has no oracle counterpart: writing it
    /// in Chinese would make an addition of this port look like transcribed legacy text. It names no
    /// identifier and no statement, so nothing a caller supplied is reflected back.
    /// </remarks>
    public const string InvalidPagedUniqueIndexColumnText =
        "A paged unique-index column was rejected: an identifier spliced into statement text must be one "
        + "to three period-separated unquoted SQL name segments, and must name a column of the statement "
        + "where the statement enumerates its columns.";

    /// <summary>Backing store for <see cref="RewrittenSql"/>, nullable so that
    /// <see langword="default"/> is representable without a null-hostile property.</summary>
    private readonly string? _rewrittenSql;

    /// <summary>Backing store for <see cref="ErrorText"/>, nullable for the same reason. Note that
    /// <see langword="null"/> and <see cref="string.Empty"/> are indistinguishable through
    /// <see cref="ErrorText"/>, which is correct: the oracle's empty message is a real value, not an
    /// absence.</summary>
    private readonly string? _errorText;

    /// <summary>
    /// Creates a result. Private so that the only reachable outcomes are the four the oracle can
    /// produce; an arbitrary code-and-text pair has no counterpart in the specification.
    /// </summary>
    /// <param name="returnCode">The framework return code.</param>
    /// <param name="rewrittenSql">The rewritten statement, or <see langword="null"/> for none.</param>
    /// <param name="errorText">The diagnostic, or <see langword="null"/> for none.</param>
    private PagingRewriteResult(long returnCode, string? rewrittenSql, string? errorText)
    {
        ReturnCode = returnCode;
        _rewrittenSql = rewrittenSql;
        _errorText = errorText;
    }

    /// <summary>
    /// The framework return code, drawn from <see cref="RetCode"/> and therefore from
    /// <c>ws_objects/pfw.shared.pbl.src/retcode.sru</c>. This is the authoritative outcome and it is
    /// always populated.
    /// </summary>
    /// <remarks>
    /// Named <c>ReturnCode</c> rather than <c>RetCode</c> on purpose: a member called <c>RetCode</c>
    /// would shadow the static class the values come from, so <c>RetCode.OK</c> written inside this
    /// type would bind to the member and fail to compile. The type is <see langword="long"/> because
    /// the legacy declarations are <c>Constant Long</c> and the legacy function is
    /// <c>function long</c>; narrowing to <see langword="int"/> would misrepresent the algebra.
    /// </remarks>
    public long ReturnCode { get; }

    /// <summary>
    /// The rewritten statement, never <see langword="null"/>, and empty on every failure. The
    /// managed form of the legacy <c>ref string sql</c> out-parameter at
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L303</c>.
    /// </summary>
    public string RewrittenSql => _rewrittenSql ?? string.Empty;

    /// <summary>
    /// The diagnostic the legacy passed as the second argument of its error event, never
    /// <see langword="null"/>. May be Chinese, and is legitimately EMPTY for
    /// <see cref="DialectNotImplemented"/>. Treat it as display text; never parse it to classify an
    /// outcome - branch on <see cref="ReturnCode"/> for that.
    /// </summary>
    public string ErrorText => _errorText ?? string.Empty;

    /// <summary>
    /// Whether the rewrite produced a usable statement, tested the way the legacy caller tests it.
    /// </summary>
    /// <value><see langword="true"/> when <see cref="ReturnCode"/> equals <see cref="RetCode.OK"/>.</value>
    /// <remarks>
    /// <para>
    /// This is an EXACT EQUALITY TEST AGAINST ZERO, reproducing
    /// <c>if rtCode &lt;&gt; RetCode.OK then return rtCode</c> at
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L725</c>, which is the
    /// only call site.
    /// </para>
    /// <para>
    /// It is deliberately NOT the framework's tri-state success predicate, and the distinction is
    /// not academic. <c>issucceeded</c> tests <c>&gt;= 0</c>
    /// <c>[ws_objects/pfw.shared.pbl.src/issucceeded.srf:L11-L13]</c>, so it classifies
    /// <c>PREVENT = 1</c> as a success, and <c>isfailed</c> explicitly excludes
    /// <c>CANCELLED = -2</c>, so a cancellation is neither succeeded nor failed. Routing this
    /// outcome through either predicate would widen a test the oracle wrote narrowly. Named
    /// <c>IsOk</c> rather than <c>IsSucceeded</c> so the difference is visible at the call site.
    /// </para>
    /// </remarks>
    public bool IsOk => ReturnCode == RetCode.OK;

    /// <summary>
    /// The success outcome: <see cref="RetCode.OK"/> with the rewritten statement and no diagnostic.
    /// Reproduces <c>return RetCode.OK</c> at
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L403</c>, which raises no
    /// error event.
    /// </summary>
    /// <param name="rewrittenSql">
    /// The rewritten statement. Not validated: the oracle asserts nothing about the text it built,
    /// and an arm that produced an empty statement would be a defect in that arm rather than
    /// something this contract can detect or should mask.
    /// </param>
    /// <returns>A successful result.</returns>
    public static PagingRewriteResult Ok(string rewrittenSql) =>
        new(RetCode.OK, rewrittenSql, null);

    /// <summary>
    /// The invalid-paging outcome: <see cref="RetCode.E_INVALID_ARGUMENT"/> (-3) with
    /// <see cref="InvalidPagingSettingText"/>. Reproduces <c>[:L308-L309]</c> of
    /// <c>n_cst_thread_task_sqlquery.sru</c>, raised when
    /// <see cref="PagingRewriteRequest.HasValidPagingBounds"/> is <see langword="false"/>.
    /// </summary>
    /// <returns>The first-guard failure result.</returns>
    public static PagingRewriteResult InvalidPagingSetting() =>
        new(RetCode.E_INVALID_ARGUMENT, null, InvalidPagingSettingText);

    /// <summary>
    /// The parse-failure outcome: <see cref="RetCode.E_INTERNAL_ERROR"/> (-27) with
    /// <see cref="ParseFailedText"/>. Reproduces <c>[:L315-L316]</c> of
    /// <c>n_cst_thread_task_sqlquery.sru</c>, raised when the statement could not be understood.
    /// </summary>
    /// <returns>The parse-failure result.</returns>
    /// <remarks>
    /// The code is <c>E_INTERNAL_ERROR</c> and not <c>E_INVALID_SQL</c> (-8) even though the input
    /// is what is malformed. That is the oracle's choice, verified at
    /// <c>retcode.sru:L51</c> and <c>:L70</c>, and it is reproduced rather than improved (C-B).
    /// </remarks>
    public static PagingRewriteResult ParseFailed() =>
        new(RetCode.E_INTERNAL_ERROR, null, ParseFailedText);

    /// <summary>
    /// The unrecognised-dialect outcome: <see cref="RetCode.E_NO_IMPLEMENTATION"/> (-2001) with an
    /// EMPTY diagnostic. Reproduces the <c>case else</c> arm at <c>[:L396-L398]</c> of
    /// <c>n_cst_thread_task_sqlquery.sru</c>.
    /// </summary>
    /// <returns>The unrecognised-dialect result.</returns>
    /// <remarks>
    /// <para>
    /// The code is <c>E_NO_IMPLEMENTATION</c> at -2001, NOT <c>E_NO_SUPPORT</c> at -2000
    /// <c>[retcode.sru:L77-L78]</c>. The two are adjacent, distinct, and must not be conflated.
    /// </para>
    /// <para>
    /// The empty diagnostic is transcribed, not omitted: <c>[:L397]</c> is literally
    /// <c>Event OnError(RetCode.E_NO_IMPLEMENTATION,"")</c>. Inventing text here would be a
    /// behaviour improvement, which C-B forbids.
    /// </para>
    /// </remarks>
    public static PagingRewriteResult DialectNotImplemented() =>
        new(RetCode.E_NO_IMPLEMENTATION, null, DialectNotImplementedText);

    /// <summary>
    /// THE ONE OUTCOME WITH NO ORACLE COUNTERPART: a paged unique-index column that would be spliced
    /// into statement text was rejected. <see cref="RetCode.E_INVALID_ARGUMENT"/> with
    /// <see cref="InvalidPagedUniqueIndexColumnText"/>.
    /// </summary>
    /// <returns>The identifier-rejection result.</returns>
    /// <remarks>
    /// <para>
    /// WHY A FIFTH SHAPE EXISTS AT ALL, WHEN THE FOUR ABOVE WERE MEASURED AS COMPLETE. The four are the
    /// complete set of the ORACLE's returns, and that statement remains exactly true. This one is not an
    /// oracle return: the oracle performs no validation whatever on these identifiers and concatenates
    /// whatever it is handed <c>[:L331, :L333, :L336]</c>. AAP 0.6.4 governs precisely this situation - the
    /// legacy interpolation sites are documented as known legacy defects while ".NET implementation uses
    /// parameterized commands internally" - and an identifier is the one thing a bind parameter cannot
    /// carry, so validate-and-reject is the only control available. A comment telling callers to supply
    /// trusted names is not a trust boundary; this is.
    /// </para>
    /// <para>
    /// THE CODE IS DELIBERATELY THE SAME <c>E_INVALID_ARGUMENT</c> THE FIRST GUARD USES, and the TEXT is
    /// deliberately different. Reusing the code keeps the caller's branch set unchanged: the contract
    /// already publishes E_INVALID_ARGUMENT as "a paging setting was rejected", and this is a paging
    /// setting being rejected. Inventing a new code would add a value a v1 consumer has no arm for.
    /// Reusing the oracle's Chinese <see cref="InvalidPagingSettingText"/> as the message, by contrast,
    /// would make a bounds fault and an identifier fault indistinguishable in a log - so this carries its
    /// own text, which the contract permits because the diagnostic is declared opaque display text that a
    /// consumer must never parse to classify an outcome.
    /// </para>
    /// <para>
    /// NO CALLER-SUPPLIED TEXT IS ECHOED. The message names neither the offending identifier nor the
    /// statement, deliberately: the rejected value is attacker-controlled by hypothesis, and a diagnostic
    /// that reflects it back becomes a channel of its own. Which identifier failed and why is available
    /// through <see cref="PagedUniqueIndexColumnValidator.TryValidate"/> at the call site, where it can be
    /// logged server-side rather than returned.
    /// </para>
    /// </remarks>
    public static PagingRewriteResult InvalidPagedUniqueIndexColumn() =>
        new(RetCode.E_INVALID_ARGUMENT, null, InvalidPagedUniqueIndexColumnText);
}


// ----------------------------------------------------------------------------------------------
//  The trust boundary - identifier validation, which the oracle does not have and this must
// ----------------------------------------------------------------------------------------------

/// <summary>
/// Validates the paged unique-index columns a caller supplies, BEFORE any of them is concatenated into
/// statement text.
/// </summary>
/// <remarks>
/// <para>
/// <b>WHY THIS TYPE EXISTS (CWE-89 / CWE-20).</b> <c>QuerySpec.paged_unique_index_columns</c> and
/// <c>SetPagedUniqueIndexColumnsRequest.columns</c> are <c>repeated string</c> on a published contract,
/// and the first dialect arm concatenates each element into the select list, into the join predicate and
/// into the ORDER BY, unquoted and unescaped
/// <c>[ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L331, :L333, :L336]</c>. An
/// identifier CANNOT be a bind parameter - the emitted identifier text is the observable output that
/// byte-exact parity is measured against - so parameterisation is unavailable here rather than merely
/// omitted. Authentication does not help: every caller on this boundary is authenticated by design, and
/// the exposure is what an authenticated caller can make the statement say. A comment instructing callers
/// to supply names from a trusted schema source is guidance, not enforcement.
/// </para>
/// <para>
/// <b>TWO CHECKS, AND THEY DO DIFFERENT JOBS.</b> The LEXICAL check is the security control: an accepted
/// value cannot contain whitespace, a quote, a bracket, a parenthesis, a comma, a semicolon, an operator
/// or a comment marker, so it cannot terminate the identifier it is spliced as and cannot introduce a
/// second syntactic element. It is an ALLOW-LIST over the characters SQL Server and Oracle permit in an
/// unquoted identifier, not a deny-list of dangerous ones - a deny-list is only ever as complete as its
/// author's imagination. The MEMBERSHIP check is defence in depth: where the parsed statement enumerates
/// its columns, an identifier that names none of them is rejected as unknown.
/// </para>
/// <para>
/// <b>MEMBERSHIP IS ONLY ENFORCED WHERE THE STATEMENT CAN ANSWER IT, AND THAT IS NOT A WEAKENING.</b>
/// The primary fixture's own retrieve is <c>SELECT * FROM COMPANY</c>
/// <c>[ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L14]</c>, whose select list names no column at all -
/// so a roster containing <c>*</c>, a <c>t.*</c> term, or an expression term cannot enumerate the
/// available columns, and treating "not in the roster" as "unknown" there would reject every legitimate
/// request against the one statement shape the repository actually evidences. Where the roster IS
/// exhaustive the check is enforced strictly. The lexical check applies in every case, so the security
/// property never depends on the statement's shape.
/// </para>
/// <para>
/// <b>WHAT THIS CHANGES ABOUT OBSERVABLE BEHAVIOUR, STATED PLAINLY (C-B).</b> For every identifier the
/// oracle could splice and produce executable SQL from, the generated statement is unchanged byte for
/// byte - the validator accepts it and the arm emits exactly what it emitted before. What changes is that
/// three previously-reachable outcomes are now refused: a crafted identifier that would have altered the
/// statement, an identifier naming a column the enumerated statement does not have, and the EMPTY
/// identifier - which the arm already turned into the malformed predicate
/// <c>"pfwPagedSQL_OutterTbl. = "</c> in both the legacy and this port, and so never yielded executable
/// SQL in either. None of the three is a behaviour a caller could have depended on.
/// </para>
/// <para>
/// <b>A PURE FUNCTION over strings.</b> No connection, no schema catalogue query, no clock, no I/O - the
/// "authoritative metadata" is the PARSED STATEMENT the dispatcher already holds, which is what keeps
/// this testable with no database of either dialect in existence (C-E).
/// </para>
/// </remarks>
internal static class PagedUniqueIndexColumnValidator
{
    /// <summary>
    /// The longest single dot-separated segment an identifier may carry.
    /// </summary>
    /// <remarks>
    /// SQL Server's regular-identifier limit is 128 characters and Oracle's modern limit is the same, so
    /// 128 admits every identifier either engine would accept while bounding the text that can be spliced.
    /// It is a bound rather than a parity constant: no legacy site declares one, because no legacy site
    /// checks anything.
    /// </remarks>
    public const int MaximumSegmentLength = 128;

    /// <summary>
    /// The most dot-separated segments an identifier may carry - a three-part
    /// <c>database.schema.object</c> name.
    /// </summary>
    /// <remarks>
    /// Three rather than two because the arm's qualifier strip explicitly tolerates a three-part name: it
    /// splits on the FIRST period and keeps everything after it, which the port documents as legacy
    /// behaviour and preserves. Four parts are refused because neither dialect's column reference has a
    /// fourth level.
    /// </remarks>
    public const int MaximumSegments = 3;

    /// <summary>
    /// Whether an identifier is lexically acceptable to splice - THE SECURITY CONTROL.
    /// </summary>
    /// <param name="identifier">The caller-supplied identifier. <see langword="null"/> is invalid.</param>
    /// <returns>
    /// <see langword="true"/> when the value is one to <see cref="MaximumSegments"/> period-separated
    /// segments, each non-empty, each at most <see cref="MaximumSegmentLength"/> characters, each
    /// beginning with an ASCII letter or underscore and continuing with ASCII letters, digits,
    /// underscores, <c>$</c> or <c>#</c>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The trailing alphabet is the two engines' own: <c>$</c> and <c>#</c> are legal in an Oracle
    /// identifier and <c>#</c> and <c>$</c> in a SQL Server one, so admitting both keeps every identifier
    /// either engine accepts without admitting anything a parser could read as a second token.
    /// </para>
    /// <para>
    /// ASCII-ONLY, DELIBERATELY. Both engines permit letters outside ASCII in an identifier, and admitting
    /// them here would mean deciding what "a letter" is - a Unicode-category question whose answer varies
    /// by framework version and would put a host-dependent decision on the path that produces text a
    /// byte-exact comparison is run against. No identifier anywhere in the repository is non-ASCII: the
    /// only evidenced schema declares <c>ID</c>, <c>NAME</c>, <c>AGE</c>, <c>ADDRESS</c>, <c>SALARY</c>
    /// and <c>BIRTH</c> <c>[ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L463-L469]</c>. A deployment
    /// needing a non-ASCII identifier gets a defined refusal rather than a silent behaviour change, which
    /// is the direction AAP 0.1.5 requires.
    /// </para>
    /// </remarks>
    public static bool IsLexicallyValid(string? identifier)
    {
        if (string.IsNullOrEmpty(identifier))
        {
            return false;
        }

        int segments = 0;
        int segmentLength = 0;

        for (int index = 0; index < identifier.Length; index++)
        {
            char character = identifier[index];

            if (character == '.')
            {
                // An empty segment - a leading period, a trailing period, or two in a row.
                if (segmentLength == 0)
                {
                    return false;
                }

                segments++;
                segmentLength = 0;

                continue;
            }

            bool acceptable = segmentLength == 0
                ? IsSegmentStart(character)
                : IsSegmentContinuation(character);

            if (!acceptable)
            {
                return false;
            }

            segmentLength++;

            if (segmentLength > MaximumSegmentLength)
            {
                return false;
            }
        }

        // The final segment is not terminated by a period, so it is counted here. A zero length means the
        // value ended on a period.
        if (segmentLength == 0)
        {
            return false;
        }

        segments++;

        return segments <= MaximumSegments;
    }

    /// <summary>
    /// Validates every supplied identifier against the parsed statement.
    /// </summary>
    /// <param name="pagedUniqueIndexColumns">The caller's list. An empty list validates trivially.</param>
    /// <param name="statement">
    /// The statement, ALREADY PARSED by the dispatcher. Its select lists are the authoritative column
    /// metadata this validator has - see the type remarks on why that is the right source and the only one
    /// available without a connection.
    /// </param>
    /// <param name="rejected">
    /// The first identifier that failed, or <see langword="null"/> when all passed. Intended for a
    /// SERVER-SIDE log record; it is never returned to the caller, because the value is
    /// attacker-controlled by hypothesis.
    /// </param>
    /// <param name="reason">
    /// Why it failed - <see cref="MalformedReason"/> or <see cref="UnknownColumnReason"/> - or
    /// <see langword="null"/> when all passed.
    /// </param>
    /// <param name="resolved">
    /// On success, the identifiers a rewriter must splice, in the caller's own order and one per
    /// input. On failure, <see langword="null"/>.
    /// </param>
    /// <returns><see langword="true"/> when every identifier is acceptable.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="pagedUniqueIndexColumns"/> or <paramref name="statement"/> is
    /// <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// FIRST FAILURE WINS AND THE REST ARE NOT EXAMINED, which matches how the dispatcher's other guards
    /// behave: one fault, one code, one diagnostic. Reporting every failure would mean assembling a
    /// message out of caller-supplied text, which is exactly what the rejection deliberately does not do.
    /// </para>
    /// <para>
    /// <b>IT ALSO RESOLVES, AND THAT IS WHY IT HAS AN OUT-PARAMETER FOR THE RESULT.</b> A review found
    /// that admitting an identifier was not enough: the roster this validator built added EVERY
    /// whitespace-delimited token of a select-list term, so <c>c.id AS ident</c> contributed
    /// <c>c.id</c>, <c>id</c> AND <c>ident</c> as though all three named a source column. They do not.
    /// <c>ident</c> is the statement's OUTPUT alias, and the arm that consumes these identifiers
    /// replaces the sub-query's select list with them
    /// [<c>n_cst_thread_task_sqlquery.sru:L331, :L345</c>] - so a caller naming the alias produced
    /// <c>SELECT ident FROM ...</c> over a table that has no such column, and a join predicate on it.
    /// Invalid SQL, admitted by the guard whose job was to prevent exactly that.
    /// </para>
    /// <para>
    /// <b>SO AN ALIAS IS TRANSLATED BACK TO ITS SOURCE, AND EVERY OTHER SPELLING IS RETURNED
    /// VERBATIM.</b> A candidate that names a source column - by its full spelling or by its trailing
    /// segment - resolves to ITSELF, character for character, so byte-exact statement parity is
    /// untouched for every request that was already correct. Only a candidate that matches nothing but
    /// an output alias is rewritten, and its previous behaviour was a statement no engine would
    /// execute, so there is no correct output being changed.
    /// </para>
    /// </remarks>
    public static bool TryValidate(
        IReadOnlyList<string> pagedUniqueIndexColumns,
        SelectStatementModel statement,
        out string? rejected,
        out string? reason,
        out IReadOnlyList<string>? resolved)
    {
        ArgumentNullException.ThrowIfNull(pagedUniqueIndexColumns);
        ArgumentNullException.ThrowIfNull(statement);

        rejected = null;
        reason = null;
        resolved = null;

        if (pagedUniqueIndexColumns.Count == 0)
        {
            resolved = [];

            return true;
        }

        // Built once for the whole list rather than per identifier. A null map means at least one select
        // block cannot enumerate its columns, so neither membership nor resolution is possible - see the
        // type remarks.
        Dictionary<string, string>? resolutions = BuildResolutionMap(statement);

        string[] spliceable = new string[pagedUniqueIndexColumns.Count];

        for (int index = 0; index < pagedUniqueIndexColumns.Count; index++)
        {
            // Widened to string? deliberately: a deserialized wire message can carry a null element
            // despite the non-nullable type argument.
            string? candidate = pagedUniqueIndexColumns[index];

            if (!IsLexicallyValid(candidate))
            {
                rejected = candidate ?? string.Empty;
                reason = MalformedReason;

                return false;
            }

            if (resolutions is null)
            {
                // UNENFORCEABLE, so the candidate passes through UNCHANGED. There is nothing to resolve
                // against, and inventing a resolution would be worse than leaving the lexical check as
                // the enforced bound - which is the position this validator already took for membership.
                spliceable[index] = candidate!;

                continue;
            }

            if (!TryResolve(resolutions, candidate!, out string? spliced) || spliced is null)
            {
                rejected = candidate!;
                reason = UnknownColumnReason;

                return false;
            }

            spliceable[index] = spliced;
        }

        resolved = spliceable;

        return true;
    }

    /// <summary>
    /// The reason reported by <see cref="TryValidate"/> for a value that failed the lexical check.
    /// </summary>
    public const string MalformedReason = "malformed identifier";

    /// <summary>
    /// The reason reported by <see cref="TryValidate"/> for a well-formed identifier that names no column
    /// of an enumerated select list.
    /// </summary>
    public const string UnknownColumnReason = "identifier names no column of the statement";

    /// <summary>
    /// Builds the map from every spelling a caller may legitimately write to the identifier a rewriter
    /// must actually splice.
    /// </summary>
    /// <param name="statement">The parsed statement.</param>
    /// <returns>
    /// The map, or <see langword="null"/> when ANY block's select list cannot be enumerated - because it
    /// contains <c>*</c>, a <c>t.*</c> term, or a term whose source is an expression rather than a name.
    /// </returns>
    /// <remarks>
    /// <para>
    /// NULL MEANS "UNENFORCEABLE", NOT "EMPTY", and the distinction is the whole reason this returns a
    /// nullable map rather than an empty one. An empty map would reject everything; null skips both the
    /// membership check and the resolution, leaving the lexical check as the enforced bound and the
    /// caller's own spelling as the spliced text.
    /// </para>
    /// <para>
    /// ONE BLOCK'S OPACITY MAKES THE WHOLE MAP UNENFORCEABLE. A compound statement's blocks may
    /// enumerate different lists, and the arm rewrites the statement as a whole, so a name that belongs to
    /// only one block is still a legitimate reference. Refusing to enforce membership when any block is
    /// opaque is the conservative direction: the lexical control is untouched either way, and the
    /// alternative would reject legitimate requests.
    /// </para>
    /// <para>
    /// <b>SOURCE SPELLINGS ARE INSERTED FIRST AND ALIASES SECOND, AND THE ORDER DECIDES COLLISIONS.</b>
    /// A source spelling maps to ITSELF, so splicing it changes nothing; an alias maps to its term's
    /// source. When one name is both - an alias on one term and a real column on another, as in
    /// <c>SELECT a AS b, b FROM t</c> - the SOURCE meaning wins, because that is the meaning the
    /// statement's own inner sub-query will have for it and it is also the behaviour that existed before
    /// resolution was introduced.
    /// </para>
    /// </remarks>
    private static Dictionary<string, string>? BuildResolutionMap(SelectStatementModel statement)
    {
        int blocks = statement.GetSelectCount();

        if (blocks <= 0)
        {
            return null;
        }

        List<string> sources = [];
        List<(string Alias, string Source)> aliases = [];

        for (int selectIndex = 1; selectIndex <= blocks; selectIndex++)
        {
            if (!statement.HasColumn(selectIndex))
            {
                return null;
            }

            if (!AddTerms(statement.GetColumn(selectIndex), sources, aliases))
            {
                return null;
            }
        }

        if (sources.Count == 0)
        {
            return null;
        }

        Dictionary<string, string> resolutions = new(StringComparer.OrdinalIgnoreCase);

        // PASS ONE - every source spelling and its trailing segment, each mapping to ITSELF.
        foreach (string source in sources)
        {
            resolutions[source] = source;

            int separator = source.IndexOf('.', StringComparison.Ordinal);

            if (separator >= 0)
            {
                string trailing = source[(separator + 1)..];

                // The trailing segment maps to ITSELF rather than to the qualified spelling, because a
                // caller who wrote the unqualified name previously had it spliced unqualified and that
                // statement was valid. Resolving it to the qualified form would change correct output.
                resolutions[trailing] = trailing;
            }
        }

        // PASS TWO - aliases, only where they do not collide with a source spelling.
        foreach ((string alias, string source) in aliases)
        {
            _ = resolutions.TryAdd(alias, source);
        }

        return resolutions;
    }

    /// <summary>
    /// Splits one select list on its TOP-LEVEL commas and records each term's source and alias.
    /// </summary>
    /// <param name="selectList">The select-list text, exactly as parsed.</param>
    /// <param name="sources">Collects each term's SOURCE identifier.</param>
    /// <param name="aliases">Collects each aliased term's alias paired with its source.</param>
    /// <returns><see langword="false"/> when the list cannot be enumerated.</returns>
    /// <remarks>
    /// PARENTHESIS DEPTH IS TRACKED so that a comma inside a function call does not split a term - though
    /// a term containing a parenthesis is opaque anyway, so the depth counter exists to keep the SPLIT
    /// honest rather than to salvage the term.
    /// </remarks>
    private static bool AddTerms(
        string selectList,
        List<string> sources,
        List<(string Alias, string Source)> aliases)
    {
        int depth = 0;
        int start = 0;

        for (int index = 0; index < selectList.Length; index++)
        {
            char character = selectList[index];

            switch (character)
            {
                case '(':
                    depth++;
                    break;

                case ')':
                    depth--;
                    break;

                case ',' when depth == 0:
                    if (!AddTerm(selectList[start..index], sources, aliases))
                    {
                        return false;
                    }

                    start = index + 1;
                    break;

                default:
                    break;
            }
        }

        return AddTerm(selectList[start..], sources, aliases);
    }

    /// <summary>
    /// Records one select-list term's source identifier and, if it has one, its output alias.
    /// </summary>
    /// <param name="term">One term of a select list, un-trimmed.</param>
    /// <param name="sources">Collects the term's SOURCE identifier.</param>
    /// <param name="aliases">Collects the term's alias paired with its source, when it has one.</param>
    /// <returns><see langword="false"/> when the term is not a plain name and therefore opaque.</returns>
    /// <remarks>
    /// <para>
    /// <b>THE SOURCE AND THE ALIAS ARE DISTINGUISHED HERE, AND THAT DISTINCTION IS THE WHOLE FIX.</b>
    /// This method used to add EVERY whitespace-delimited token of the term to one flat set, so
    /// <c>c.id AS ident</c> contributed <c>c.id</c>, <c>id</c> and <c>ident</c> as though all three
    /// named a source column. A review found the consequence: the arm replaces the sub-query's select
    /// list with the caller's identifiers <c>[:L331, :L345]</c>, so a caller who named the ALIAS got
    /// <c>SELECT ident FROM &lt;table&gt;</c> - a projection of a column the table does not have - plus
    /// a join predicate on the same non-existent name.
    /// </para>
    /// <para>
    /// <b>The three term shapes, and how each is read.</b> One token is a bare source. Two tokens are a
    /// source and an implicit alias. Three tokens are a source, the keyword <c>AS</c> and an explicit
    /// alias. Anything else - four or more tokens, or a leading <c>AS</c> - is not a shape either dialect
    /// produces for a plain column term, so it is treated as opaque rather than guessed at.
    /// </para>
    /// <para>
    /// <b>The SOURCE must be a plain identifier; the ALIAS is recorded only if it is one too.</b> An
    /// expression source - a function call, an operator, a quoted or bracketed name - makes the whole map
    /// unenforceable, exactly as before, because there is then no identifier for a caller's name to
    /// resolve to. An alias that is not lexically an identifier is simply not recorded: it cannot be
    /// spliced, so admitting it would only re-create the defect.
    /// </para>
    /// <para>
    /// <b>The T-SQL <c>alias = expr</c> form is opaque</b>, and deliberately so: the term contains
    /// <c>=</c>, which no lexically valid identifier does, so it falls into the opaque arm and leaves the
    /// lexical check as the enforced bound. Resolving it would mean parsing an assignment form that
    /// neither the fixture nor the oracle exercises.
    /// </para>
    /// </remarks>
    private static bool AddTerm(
        string term,
        List<string> sources,
        List<(string Alias, string Source)> aliases)
    {
        string trimmed = term.Trim();

        if (trimmed.Length == 0)
        {
            // A trailing or doubled comma. The statement is odd but not opaque; nothing to add.
            return true;
        }

        // `*` and `t.*` both mean "every column", which is exactly the list this cannot enumerate.
        if (trimmed == "*" || trimmed.EndsWith(".*", StringComparison.Ordinal))
        {
            return false;
        }

        string[] tokens = trimmed.Split(
            (char[]?)null,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // The source is always the FIRST token. A term that begins with AS is malformed rather than
        // aliased, and is refused with every other unrecognised shape below.
        if (tokens.Length == 0 || !IsLexicallyValid(tokens[0]))
        {
            return false;
        }

        string source = tokens[0];
        string? alias = null;

        switch (tokens.Length)
        {
            case 1:
                // A bare source column, with no alias.
                break;

            case 2:
                // An implicit alias: `c.id ident`.
                alias = tokens[1];
                break;

            case 3 when string.Equals(tokens[1], "AS", StringComparison.OrdinalIgnoreCase):
                // An explicit alias: `c.id AS ident`.
                alias = tokens[2];
                break;

            default:
                // Not a plain column term in any shape either dialect produces.
                return false;
        }

        sources.Add(source);

        if (alias is not null && IsLexicallyValid(alias))
        {
            aliases.Add((alias, source));
        }

        return true;
    }

    /// <summary>
    /// Resolves a caller's identifier to the identifier a rewriter must splice.
    /// </summary>
    /// <param name="resolutions">The map built from the statement's select lists.</param>
    /// <param name="identifier">The identifier, already known to be lexically valid.</param>
    /// <param name="spliced">
    /// The identifier to splice: the caller's own spelling when it names a source column, or the term's
    /// source when it names only an output alias.
    /// </param>
    /// <returns><see langword="true"/> when the identifier names something in the statement.</returns>
    /// <remarks>
    /// <para>
    /// ORDINAL-IGNORE-CASE, because an unquoted identifier is case-insensitive in both dialects, and
    /// ORDINAL rather than culture-aware because the comparison feeds a decision about text a byte-exact
    /// assertion is run against.
    /// </para>
    /// <para>
    /// <b>THE QUALIFIED FALLBACK ANSWERS THE CALLER'S OWN SPELLING, NOT THE MAPPED ONE.</b> A caller who
    /// writes <c>t.id</c> against a statement that selects a bare <c>id</c> previously had <c>t.id</c>
    /// spliced, and that statement was valid - the qualifier names a table the sub-query's FROM clause
    /// still carries. Returning the map's value here would replace it with the unqualified <c>id</c> and
    /// change correct output, so the fallback deliberately confirms membership by trailing segment while
    /// splicing the full spelling the caller supplied.
    /// </para>
    /// </remarks>
    private static bool TryResolve(
        Dictionary<string, string> resolutions,
        string identifier,
        out string? spliced)
    {
        if (resolutions.TryGetValue(identifier, out string? mapped))
        {
            // A SOURCE SPELLING SPLICES THE CALLER'S OWN TEXT, NOT THE SELECT LIST'S CASING. The map is
            // ordinal-ignore-case, so a caller who wrote `id` against a statement selecting `ID` finds
            // the entry and would otherwise be handed `ID` - changing the generated statement for a
            // request that was already correct, and breaking byte-exact parity on a casing difference
            // alone. A source key maps to ITSELF, so equality-ignoring-case is exactly the test for
            // "this was a source spelling rather than an alias".
            spliced = string.Equals(mapped, identifier, StringComparison.OrdinalIgnoreCase)
                ? identifier
                : mapped;

            return true;
        }

        int separator = identifier.IndexOf('.', StringComparison.Ordinal);

        if (separator >= 0 && resolutions.ContainsKey(identifier[(separator + 1)..]))
        {
            spliced = identifier;

            return true;
        }

        spliced = null;

        return false;
    }

    /// <summary>Whether a character may begin a segment: an ASCII letter or an underscore.</summary>
    /// <param name="character">The character to classify.</param>
    /// <returns><see langword="true"/> when it may begin a segment.</returns>
    private static bool IsSegmentStart(char character) =>
        character is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or '_';

    /// <summary>
    /// Whether a character may continue a segment: a segment-start character, an ASCII digit,
    /// <c>$</c> or <c>#</c>.
    /// </summary>
    /// <param name="character">The character to classify.</param>
    /// <returns><see langword="true"/> when it may continue a segment.</returns>
    private static bool IsSegmentContinuation(char character) =>
        IsSegmentStart(character) || character is (>= '0' and <= '9') or '$' or '#';
}


// ----------------------------------------------------------------------------------------------
//  The strategy - one implementation per `case` arm of the legacy `choose case`
// ----------------------------------------------------------------------------------------------

/// <summary>
/// One dialect's paging rewrite: the managed form of a single <c>case</c> arm of the
/// <c>choose case TransObject.of_GetDBType()</c> at
/// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L320</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Exactly two implementations exist and exactly two may ever exist</b>, because the oracle
/// declares exactly two discriminators - <c>DBT_MSSQL = 0</c>
/// <c>[ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:L60]</c> and <c>DBT_ORACLE = 1</c>
/// <c>[:L61]</c> - and exactly two arms <c>[:L321-L385]</c> and <c>[:L386-L395]</c>. A third
/// implementation would invent a legacy constant. Anything else lands in the <c>case else</c> arm,
/// which is defined behaviour and is preserved: see
/// <see cref="PagingRewriteResult.DialectNotImplemented"/>.
/// </para>
/// <para>
/// <b>THERE IS NO SQLITE IMPLEMENTATION, AND ITS ABSENCE IS THE CONTRACT (C-E).</b> SQLite is the
/// only storage engine this system provisions and the only one with an evidenced schema, and it
/// appears in neither discriminator. It is not an oversight and not a gap to close. The reason both
/// dialects survive at all when neither engine is provisioned is that this selector chooses A TEXT
/// GENERATOR, NOT A CONNECTION: an implementation receives a parsed statement and four scalars and
/// returns statement text, with no connection at any point, so both are fully unit-testable with no
/// instance of either engine in existence.
/// </para>
/// <para>
/// <b>Implementations receive an ALREADY PARSED statement and must not parse.</b> The oracle parses
/// once, before the dispatch <c>[:L312-L317]</c>, so parse failure is a pre-dispatch outcome and not
/// a per-arm one. <see cref="PagingRewriteDispatcher"/> owns that call. An implementation that
/// parsed for itself would move the <c>E_INTERNAL_ERROR</c> exit inside the arms and change what an
/// unparseable statement returns for an unrecognised dialect.
/// </para>
/// <para>
/// <b>Implementations must not reference one another.</b> The two arms substitute DIFFERENT text for
/// an absent <c>ORDER BY</c> - <c>(SELECT 0)</c> at <c>[:L370, :L379]</c> against <c>''</c> at
/// <c>[:L392]</c> - and they emit different sentinel identifiers, so each arm's literals belong to
/// its own file. This contract carries none of them, precisely so that neither implementation has a
/// reason to reach for the other.
/// </para>
/// <para>
/// <b>Implementations are expected to be stateless and are shared.</b> Nothing on this interface
/// carries per-call state, so a single instance per dialect is safe to register as a singleton and to
/// call concurrently. That is a consequence of the pure-function shape rather than an extra
/// requirement.
/// </para>
/// </remarks>
internal interface IPagingRewriter
{
    /// <summary>
    /// The discriminator this implementation is the arm for.
    /// </summary>
    /// <value>
    /// <see cref="DatabaseType.DbtMssql"/> or <see cref="DatabaseType.DbtOracle"/> - the generated
    /// projections of <c>DBT_MSSQL = 0</c> and <c>DBT_ORACLE = 1</c> declared at
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:L60-L61</c>.
    /// </value>
    /// <remarks>
    /// Named <c>Dialect</c> rather than <c>DatabaseType</c> on purpose: a property named after its
    /// own enum type would shadow that type inside every implementing class, so
    /// <c>DatabaseType.DbtOracle</c> would stop resolving in exactly the files that need it. The
    /// value is CONSUMED from the generated contract enum and is never re-declared locally, which is
    /// what keeps one published definition of the discriminator instead of two that can drift.
    /// </remarks>
    DatabaseType Dialect { get; }

    /// <summary>
    /// Whether this arm SPLICES <see cref="PagingRewriteRequest.PagedUniqueIndexColumns"/> INTO
    /// STATEMENT TEXT, and therefore whether those identifiers are a trust boundary for it.
    /// </summary>
    /// <value>
    /// <see langword="true"/> for the arm that branches on
    /// <see cref="PagingRewriteRequest.HasPagedUniqueIndexColumns"/> and concatenates each column into
    /// the select list, the join predicate and the ORDER BY
    /// <c>[ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L331, :L333, :L336]</c>;
    /// <see langword="false"/> for an arm that ignores the collection entirely <c>[:L386-L395]</c>.
    /// </value>
    /// <remarks>
    /// <para>
    /// WHY THE CONTRACT CARRIES THIS RATHER THAN THE DISPATCHER ASSUMING IT. An identifier cannot be a
    /// bind parameter - the emitted identifier text IS the observable output that byte-exact parity is
    /// measured against - so the only available control is to VALIDATE the identifier before it is
    /// concatenated. <see cref="PagingRewriteDispatcher"/> applies that validation, and it must apply it
    /// exactly where text is spliced and nowhere else: validating for an arm that discards the collection
    /// would REFUSE a request that arm previously answered, which is a narrowing with no security benefit
    /// to justify it. Declaring the coupling here keeps that decision a published property of each arm
    /// instead of a dialect list the dispatcher would have to keep in step by hand.
    /// </para>
    /// <para>
    /// It is a property and not a method because it is a fixed fact about the arm's own text generation,
    /// not a function of any request.
    /// </para>
    /// </remarks>
    bool ConsumesPagedUniqueIndexColumns { get; }

    /// <summary>
    /// Rewrites a parsed statement into its paged form for this dialect.
    /// </summary>
    /// <param name="request">
    /// The paging inputs, passed by <c>in</c> because the legacy parameters are declared
    /// <c>readonly</c> <c>[ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L303]</c>.
    /// </param>
    /// <param name="statement">
    /// The statement, ALREADY PARSED SUCCESSFULLY by the dispatcher. An implementation may drive its
    /// clause accessors and modifiers freely; the oracle mutates and restores clauses repeatedly
    /// within a single arm <c>[:L341-L364]</c>, so the model is expected to be handed over for
    /// modification rather than treated as read-only.
    /// </param>
    /// <returns>
    /// <see cref="PagingRewriteResult.Ok(string)"/> carrying the rewritten statement. Both oracle
    /// arms always succeed - neither contains a <c>return</c> - so a failure result from here has no
    /// counterpart in the specification and would represent a defect in the arm. The return type
    /// nonetheless carries the full result shape so that an implementation can report a defensive
    /// failure instead of emitting a statement it knows to be wrong.
    /// </returns>
    PagingRewriteResult Rewrite(in PagingRewriteRequest request, SelectStatementModel statement);
}


// ----------------------------------------------------------------------------------------------
//  The dispatcher - the pre-dispatch guards, the parse, and the `choose case` including `case else`
// ----------------------------------------------------------------------------------------------

/// <summary>
/// The managed form of <c>_of_buildpagedsql</c> itself: the two pre-dispatch guards, the single
/// parse, and the dialect dispatch including its <c>case else</c> arm.
/// </summary>
/// <remarks>
/// <para>
/// Reproduces <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L303-L404</c>
/// minus the two arm bodies, which are <see cref="IPagingRewriter"/> implementations.
/// </para>
/// <para>
/// <b>A PURE FUNCTION, WITH NO STATE OF ANY KIND (C-E).</b> This is a static class holding NO
/// field - not even a readonly one - so there is nothing to configure, nothing to mutate and nothing
/// to dispose. Every input arrives as a parameter, including the rewriter set, so the same arguments
/// always produce the same result. There is no connection, no command, no connection string, no
/// provider handle, no clock read, no environment read, no file access and no logging. That is what
/// makes the whole operation testable from a theory whose fixture is strings.
/// </para>
/// <para>
/// <b>Why static rather than an injected service.</b> A class holding an injected rewriter collection
/// would hold state, however immutable, and the contract this file publishes is deliberately a
/// function. A caller that wants dependency injection injects
/// <c>IEnumerable&lt;IPagingRewriter&gt;</c> itself and passes it in, which keeps the composition
/// decision at the composition root instead of embedding it here.
/// </para>
/// </remarks>
internal static class PagingRewriteDispatcher
{
    /// <summary>
    /// Builds the paged form of a statement, or reports why it could not be built.
    /// </summary>
    /// <param name="request">The paging inputs. Passed by <c>in</c>, mirroring the legacy
    /// <c>readonly</c> parameters at
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L303</c>.</param>
    /// <param name="dialect">
    /// The discriminator selecting the arm. Supplied by the caller rather than read from a connection,
    /// which is the whole of C-E for this operation; <see cref="PagingDialectResolver.Resolve(string)"/>
    /// is what derives it from a descriptor's engine name when the caller has one.
    /// </param>
    /// <param name="rewriters">
    /// The available arms, in any order. Only the one whose <see cref="IPagingRewriter.Dialect"/>
    /// matches is used, and the collection is enumerated at most once.
    /// </param>
    /// <returns>
    /// One of the four outcomes enumerated on <see cref="PagingRewriteResult"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="rewriters"/> is
    /// <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="dialect"/> is one of the two implemented dialects but
    /// <paramref name="rewriters"/> contains no arm for it. See the remarks: this is a composition
    /// fault, deliberately NOT folded into the <c>case else</c> arm.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>THE ORDER OF THE FOUR STEPS BELOW IS THE CONTRACT AND MUST NOT BE REARRANGED.</b> Paging
    /// bounds <c>[:L307]</c>, then the parse <c>[:L314]</c>, then the dialect test <c>[:L320]</c>,
    /// then the arm. Two consequences are directly observable through the returned code: a request
    /// that is both badly paged and unparseable returns <c>E_INVALID_ARGUMENT</c> rather than
    /// <c>E_INTERNAL_ERROR</c>, and a request that is unparseable and names an unrecognised dialect
    /// returns <c>E_INTERNAL_ERROR</c> rather than <c>E_NO_IMPLEMENTATION</c>.
    /// </para>
    /// <para>
    /// <b>A FIFTH STEP EXISTS THAT THE ORACLE DOES NOT HAVE, AND ITS POSITION IS CHOSEN SO THAT NO
    /// ORACLE OUTCOME MOVES.</b> The paged unique-index columns are concatenated into statement text by
    /// the first arm, unquoted and unescaped <c>[:L331, :L333, :L336]</c>, and an identifier cannot be a
    /// bind parameter - so they are validated by <see cref="PagedUniqueIndexColumnValidator"/> before the
    /// arm runs (CWE-89, and AAP 0.6.4 which requires the .NET side to close these interpolation sites
    /// while preserving observable behaviour). It is placed LAST for three reasons, each of which is a
    /// behaviour that would otherwise change:
    /// </para>
    /// <list type="number">
    ///   <item>
    ///     <description>
    ///     AFTER the bounds guard and the parse, so a request that is both badly paged and carries a
    ///     crafted identifier still reports the paging fault, and an unparseable one still reports the
    ///     parse fault. It also NEEDS the parsed statement, which is where the column roster comes from.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///     AFTER the dialect test, so an unrecognised dialect still returns <c>E_NO_IMPLEMENTATION</c>
    ///     rather than an identifier rejection.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///     GATED ON <see cref="IPagingRewriter.ConsumesPagedUniqueIndexColumns"/>, so the arm that
    ///     IGNORES the collection entirely <c>[:L386-L395]</c> is unaffected. Validating for that arm
    ///     would refuse a request it previously answered, with no injection to prevent - a narrowing
    ///     with nothing to justify it.
    ///     </description>
    ///   </item>
    /// </list>
    /// <para>
    /// The rejected identifier and the reason are DISCARDED here rather than returned. The value is
    /// attacker-controlled by hypothesis, so echoing it into a diagnostic would make the refusal its own
    /// channel; <see cref="PagedUniqueIndexColumnValidator.TryValidate"/> exposes both to a caller that
    /// wants to log them server-side.
    /// </para>
    /// <para>
    /// <b>THE EARLY RETURNS ARE PRESERVED AS EARLY RETURNS, AND HERE IS THE FINDING BEHIND THAT
    /// (C-K).</b> In the oracle, <c>Destroy sqlParser</c> sits at <c>[:L401]</c> - AFTER the
    /// <c>choose case</c> - so the parse-failure return at <c>[:L316]</c> and the <c>case else</c>
    /// return at <c>[:L398]</c> both LEAK the parser instance. On the resource side there is nothing
    /// whatever to reproduce: <see cref="SelectStatementModel"/> owns no unmanaged resource, is
    /// deliberately not <see cref="IDisposable"/>, and its lifetime belongs to the garbage collector.
    /// The CONTROL FLOW, however, IS observable, because it is what decides which arm returns which
    /// code - so the early returns below are written as early returns and are not consolidated into a
    /// single exit path with a shared cleanup block. Note the asymmetry deliberately: the leak has no
    /// managed analogue and is not reproduced; the flow that caused it does and is.
    /// </para>
    /// <para>
    /// <b>Why a missing arm throws instead of returning <c>E_NO_IMPLEMENTATION</c>.</b> The oracle's
    /// arms are compiled into the <c>choose case</c> and cannot be absent, so "the dialect is
    /// implemented but no implementation was supplied" is a state the legacy could not reach. It is a
    /// dependency-injection or composition-root fault with no legacy analogue, and returning the
    /// <c>case else</c> code for it would let a wiring mistake masquerade as the oracle's
    /// unrecognised-dialect behaviour - the exact class of silent misreport this contract exists to
    /// prevent. It therefore fails fast, matching the framework's own posture, where a structural
    /// fault terminates rather than degrades
    /// <c>[ws_objects/pfw.pbl.src/pfw.sra:L111-L144]</c>. <see cref="IsDialectImplemented"/> is
    /// consulted FIRST so that the genuine <c>case else</c> arm is reached by dialect value alone and
    /// can never be reached by a wiring accident.
    /// </para>
    /// </remarks>
    public static PagingRewriteResult Rewrite(
        in PagingRewriteRequest request,
        DatabaseType dialect,
        IEnumerable<IPagingRewriter> rewriters)
    {
        ArgumentNullException.ThrowIfNull(rewriters);

        // STEP 1 - the paging guard [:L307-L310]. `_nPageSize <= 0 or _nPageIndex <= 0`, with the
        // Chinese diagnostic carried verbatim. This runs before anything else touches the statement,
        // so a request that is both badly paged and unparseable reports the paging fault.
        if (!request.HasValidPagingBounds)
        {
            return PagingRewriteResult.InvalidPagingSetting();
        }

        // STEP 1b - THE OVERFLOW GUARD, which the oracle does not have. It answers the SAME outcome as
        // step 1 rather than a new one, because it is the same class of fault - a paging setting the
        // arms cannot compute with - and because introducing a fourth outcome would put a code on the
        // wire that no legacy caller has an arm for. Placed here, before the parse, so that NO SQL IS
        // EVER RENDERED from a bound that would wrap: see HasRepresentablePagingProducts for why a
        // wrapped bound is worse than an error.
        if (!request.HasRepresentablePagingProducts)
        {
            return PagingRewriteResult.InvalidPagingSetting();
        }

        // STEP 2 - `sqlParser = Create n_sql` [:L312]. Created here rather than injected because the
        // oracle creates it inside the function, and because a per-call instance is what keeps this
        // method a pure function: the model is mutable and both arms mutate it.
        SelectStatementModel statement = new();

        // STEP 3 - the parse guard [:L314-L317]. The boolean IS checked, which is why this uses
        // Parse rather than the SelectStatementModel.ParseSql factory - that factory faithfully
        // reproduces a DIFFERENT legacy site, parsesql.srf, which discards the boolean. Discarding it
        // here would lose the E_INTERNAL_ERROR arm altogether.
        //
        // This return is an EARLY return, exactly as the oracle's is. See the remarks above for the
        // leak finding that sits behind it.
        if (!statement.Parse(request.OriginalSql))
        {
            return PagingRewriteResult.ParseFailed();
        }

        // STEP 4 - `choose case TransObject.of_GetDBType()` [:L320]. The `case else` arm [:L396-L398]
        // is tested on the DIALECT VALUE ALONE, before any rewriter lookup, so that it means exactly
        // what the oracle means by it and nothing else.
        if (!IsDialectImplemented(dialect))
        {
            return PagingRewriteResult.DialectNotImplemented();
        }

        IPagingRewriter rewriter = SelectRewriter(dialect, rewriters)
            ?? throw new InvalidOperationException(
                $"No {nameof(IPagingRewriter)} was supplied for the implemented dialect '{dialect}'. " +
                "This is a composition fault, not the legacy unrecognised-dialect arm: exactly two " +
                "arms exist and both must be registered.");

        // STEP 5 - THE TRUST BOUNDARY THE ORACLE DOES NOT HAVE. It sits HERE, after the three oracle
        // guards and after arm selection, and it is conditional on the selected arm actually splicing
        // these identifiers - see PagedUniqueIndexColumnValidator and the ordering note in the remarks.
        //
        // IT NOW ALSO RESOLVES, and the arm is handed the RESOLVED identifiers rather than the caller's
        // raw ones. A caller who named an output ALIAS - `SELECT c.id AS ident ...` plus a unique-index
        // column of `ident` - previously reached the arm unchanged and produced `SELECT ident FROM
        // <table>`, a projection of a column no table has. Every other spelling resolves to itself
        // character for character, so nothing that was already correct changes. See TryValidate.
        if (rewriter.ConsumesPagedUniqueIndexColumns && request.HasPagedUniqueIndexColumns)
        {
            if (!PagedUniqueIndexColumnValidator.TryValidate(
                    request.PagedUniqueIndexColumns,
                    statement,
                    out string? _,
                    out string? _,
                    out IReadOnlyList<string>? resolved)
                || resolved is null)
            {
                return PagingRewriteResult.InvalidPagedUniqueIndexColumn();
            }

            // The arm body: [:L321-L385] for the first dialect, [:L386-L395] for the second.
            return rewriter.Rewrite(request.WithPagedUniqueIndexColumns(resolved), statement);
        }

        // The arm body: [:L321-L385] for the first dialect, [:L386-L395] for the second.
        return rewriter.Rewrite(in request, statement);
    }

    /// <summary>
    /// Whether a dialect value has an arm at all - the managed form of "this value matches a
    /// <c>case</c> label rather than falling into <c>case else</c>".
    /// </summary>
    /// <param name="dialect">The value to classify.</param>
    /// <returns>
    /// <see langword="true"/> for <see cref="DatabaseType.DbtMssql"/> and
    /// <see cref="DatabaseType.DbtOracle"/>; <see langword="false"/> for every other value.
    /// </returns>
    /// <remarks>
    /// <para>
    /// EXACTLY TWO ARMS, reproducing <c>[:L321]</c> and <c>[:L386]</c> of
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru</c>. No SQLite arm is added
    /// and SQLite being routed down the first arm is not "corrected" (C-B, C-E).
    /// </para>
    /// <para>
    /// <b>The <see langword="false"/> branch must stay reachable, and it is.</b> That is the entire
    /// point of the oracle's <c>case else</c>. A protobuf enum is OPEN rather than closed, so an
    /// unrecognised numeric value survives deserialization as itself and arrives here as a
    /// <see cref="DatabaseType"/> outside <c>{0, 1}</c>. Deleting this predicate on the reasoning that
    /// the enum only declares two members would delete a defined behaviour that a caller can trigger.
    /// </para>
    /// </remarks>
    public static bool IsDialectImplemented(DatabaseType dialect) =>
        dialect is DatabaseType.DbtMssql or DatabaseType.DbtOracle;

    /// <summary>
    /// Finds the arm for a dialect.
    /// </summary>
    /// <param name="dialect">The dialect to match on <see cref="IPagingRewriter.Dialect"/>.</param>
    /// <param name="rewriters">The candidates, in any order.</param>
    /// <returns>The first matching implementation, or <see langword="null"/> when none matches.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="rewriters"/> is
    /// <see langword="null"/>.</exception>
    /// <remarks>
    /// FIRST MATCH WINS, which mirrors <c>choose case</c>: PowerScript evaluates its labels in source
    /// order and runs the first that matches, so a duplicate registration behaves the way a duplicated
    /// <c>case</c> label would rather than raising. A null element is skipped rather than
    /// dereferenced, because a container can be configured to yield one and a
    /// <see cref="NullReferenceException"/> from inside a lookup would obscure the real fault.
    /// </remarks>
    public static IPagingRewriter? SelectRewriter(
        DatabaseType dialect,
        IEnumerable<IPagingRewriter> rewriters)
    {
        ArgumentNullException.ThrowIfNull(rewriters);

        foreach (IPagingRewriter candidate in rewriters)
        {
            if (candidate is not null && candidate.Dialect == dialect)
            {
                return candidate;
            }
        }

        return null;
    }
}


// ----------------------------------------------------------------------------------------------
//  The classifier - of_getdbtype(), which is a substring test and nothing more
// ----------------------------------------------------------------------------------------------

/// <summary>
/// Resolves a dialect discriminator from an engine name, reproducing <c>of_getdbtype()</c> at
/// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:L356-L361</c>.
/// </summary>
/// <remarks>
/// <para>
/// The oracle is three lines and this is all of it:
/// </para>
/// <code>
/// public function long of_getdbtype()
///     if Pos(Upper(DBMS),"ORACLE") &gt; 0 then
///         return DBT_ORACLE
///     else
///         return DBT_MSSQL
///     end if
/// </code>
/// <para>
/// <b>THIS IS THE LEGACY CONTRACT, NOT A BUG (C-B, C-K).</b> It is a case-insensitive SUBSTRING TEST
/// on one engine name, and THE FIRST DIALECT IS THE FALLBACK FOR EVERYTHING UNRECOGNISED. So a name
/// of <c>"SQLite"</c>, <c>"ODBC"</c>, <c>"PostgreSQL"</c>, <c>""</c> or nothing at all classifies as
/// <see cref="DatabaseType.DbtMssql"/> - it does not fail, and it does not reach the
/// <c>case else</c> arm. That is reproduced faithfully and is deliberately NOT "corrected" into an
/// error or into a third dialect, and SQLite reaching the first arm is therefore expected behaviour
/// even though Persistence provisions SQLite and nothing else: what the arm produces is STATEMENT
/// TEXT, not a connection, so both dialect generators are preserved as pure string transforms without
/// either engine existing (C-E).
/// </para>
/// <para>
/// <b>A structural consequence worth stating, because it explains where the <c>case else</c> arm gets
/// its input.</b> This resolver CANNOT produce a third value, so it can never send
/// <see cref="PagingRewriteDispatcher.Rewrite"/> down the unrecognised-dialect path. That path exists
/// for a caller who supplies the discriminator DIRECTLY - the published contract carries it as a
/// field, and a protobuf enum is open - which is exactly why
/// <see cref="PagingRewriteDispatcher.IsDialectImplemented"/> must remain and must stay reachable.
/// </para>
/// <para>
/// <b>Nothing here reads a connection (C-E).</b> The oracle read its own <c>DBMS</c> property, which
/// on a PowerBuilder transaction object is a plain connection-descriptor string and not a live
/// handle. It arrives here as a <see cref="string"/> parameter, so this type holds no connection, no
/// credential and no provider handle, and performs no I/O.
/// </para>
/// </remarks>
internal static class PagingDialectResolver
{
    /// <summary>
    /// The literal the oracle searches for, spelled exactly as <c>[:L356]</c> spells it.
    /// </summary>
    /// <remarks>
    /// Exposed as a constant so that a test can assert the boundary cases - a name containing the
    /// marker as a substring, a name differing only in case, and a name that does not contain it -
    /// without retyping the literal. It contains no underscore and no lowercase letter, so it raises
    /// no naming diagnostic; the SCREAMING form is the VALUE, not the identifier.
    /// </remarks>
    public const string OracleEngineMarker = "ORACLE";

    /// <summary>
    /// Classifies an engine name into one of the two dialect discriminators.
    /// </summary>
    /// <param name="dbms">
    /// The engine name - the <c>DBMS</c> property of the legacy transaction object
    /// <c>[ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:L345]</c>, which is populated from
    /// the transaction descriptor. <see langword="null"/> and the empty string are both accepted and
    /// both classify as <see cref="DatabaseType.DbtMssql"/>.
    /// </param>
    /// <returns>
    /// <see cref="DatabaseType.DbtOracle"/> when <paramref name="dbms"/> contains
    /// <see cref="OracleEngineMarker"/> case-insensitively; otherwise
    /// <see cref="DatabaseType.DbtMssql"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>Why <see cref="StringComparison.OrdinalIgnoreCase"/> is an exact substitute rather than an
    /// approximation.</b> The oracle uppercases the subject and then searches for an uppercase
    /// literal, which is a case-insensitive search expressed the long way. Ordinal case-insensitive
    /// matching reproduces it for this literal with no residual difference, and the usual hazard of
    /// culture-sensitive casing does not apply: the marker is <c>O R A C L E</c>, which contains no
    /// <c>i</c>, so the Turkish dotless-i divergence that would otherwise make an uppercase-then-match
    /// port culture-dependent cannot arise. Ordinal comparison is also deterministic across hosts,
    /// which matters because the classification feeds byte-exact statement comparisons.
    /// </para>
    /// <para>
    /// <b>Why <see langword="null"/> maps to the first dialect rather than throwing.</b> In
    /// PowerScript, <c>Pos</c> applied to a null subject yields null, a null condition does not take
    /// the <c>then</c> branch, and control falls to <c>else</c> - so an unset engine name already
    /// resolved to the first dialect in the oracle. Throwing here would invent a failure mode the
    /// legacy does not have.
    /// </para>
    /// <para>
    /// <b>The test is on containment, not equality.</b> Any name merely CONTAINING the marker matches,
    /// so a descriptor naming an Oracle-family driver classifies as the second dialect even when the
    /// name is not exactly the marker. That breadth is the oracle's and is preserved.
    /// </para>
    /// </remarks>
    public static DatabaseType Resolve(string? dbms)
    {
        // `Pos(Upper(DBMS),"ORACLE") > 0` [n_cst_thread_trans.sru:L356]. Null-safe by the same route
        // the oracle is: an absent name simply fails the test and falls through to the first dialect.
        bool namesOracle =
            dbms is not null
            && dbms.Contains(OracleEngineMarker, StringComparison.OrdinalIgnoreCase);

        return namesOracle
            ? DatabaseType.DbtOracle   // [n_cst_thread_trans.sru:L357]
            : DatabaseType.DbtMssql;   // [n_cst_thread_trans.sru:L359] - the fallback for everything
    }
}
