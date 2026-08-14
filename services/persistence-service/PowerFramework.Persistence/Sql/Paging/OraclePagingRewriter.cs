// ==============================================================================================
//  OraclePagingRewriter - the Oracle arm of the paging dispatch, emitted BYTE EXACTLY
//  --------------------------------------------------------------------------------------------
//  PORTED FROM    ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L386-L395
//                     `case TransObject.DBT_ORACLE` - the WHOLE arm and nothing but the arm, read
//                     inside its enclosing function _of_buildpagedsql [:L303-L404] so that the two
//                     pre-dispatch guards, the single parse and the `case else` that surround it are
//                     ACCOUNTED FOR rather than assumed. None of those three is reproduced here;
//                     each belongs to the dispatch contract in IPagingRewriter.cs beside this file.
//                 ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:L61, L356-L361
//                     `constant long DBT_ORACLE = 1` and the substring test that selects it
//                 ws_objects/pfw.utility.parser.pbl.src/n_sql.sru:L24, L36, L48, L12
//                     hasorder(), getorder(), modifyorder(ms, neworder) and getsql() - the FOUR
//                     parser members this arm drives, and it drives them IN THAT ORDER
//                 ws_objects/pfw.shared.pbl.src/enums.sru:L718
//                     `Constant Long SQL_MS_REPLACE = 1`, CONSUMED and never re-declared
//                 ws_objects/pfw.shared.pbl.src/retcode.sru:L39
//                     OK = 0, reached only through PagingRewriteResult.Ok and never named directly
//
//  READ-ONLY POSITION (C-C). Every ws_objects/** path named above is READ ONLY. Each was read as
//  specification and is cited by :L locator throughout; nothing here copies, reformats, moves,
//  edits or deletes any of them, and nothing in this file depends on the PowerBuilder toolchain,
//  the PowerBuilder runtime or any shipped native binary. The legacy tree is the ONLY statement of
//  intended behaviour that exists for this operation - the repository holds no Oracle schema, no
//  changelog entry and no other document that could adjudicate a disagreement - which is why every
//  behavioural claim below carries the line it was taken from.
//
//  BINDING CONSTRAINTS AT THIS SITE
//  THE LEGACY ARM, TRANSCRIBED VERBATIM [n_cst_thread_task_sqlquery.sru:L386-L395]
//  --------------------------------------------------------------------------------------------
//      L386  case TransObject.DBT_ORACLE
//      L387      //ORACLE实现
//      L388      if sqlParser.HasOrder() then
//      L389          sOrderBy = sqlParser.GetOrder()
//      L390          sqlParser.ModifyOrder(Enums.SQL_MS_REPLACE,"")  //去掉ORDER BY语句
//      L391      else
//      L392          sOrderBy = "''"                                //原始结果集排序
//      L393      end if
//      L394      sql = "SELECT * FROM (SELECT * FROM (SELECT pfwPagedSQL_TblInnerInner.*,ROW_NUMBER()
//                       OVER (ORDER BY " + sOrderBy + ") AS pfwPagedSQL_RN FROM (" +
//                       sqlParser.GetSQL() + ") pfwPagedSQL_TblInnerInner) pfwPagedSQL_TblInner
//                       WHERE pfwPagedSQL_RN <= " + String(_nPageSize * _nPageIndex) +
//                       ") pfwPagedSQL_TblOuter " + &
//      L395              "WHERE pfwPagedSQL_RN BETWEEN " +
//                       String(_nPageSize * (_nPageIndex - 1) + 1) + " AND " +
//                       String(_nPageSize * _nPageIndex)
//
//  (L394 is a single physical source line; it is folded above for legibility only. The authoritative
//  text is the file, and the segments this class emits are enumerated one by one at their point of
//  emission below so that nothing depends on this fold being read correctly.)
//
//  ONE STRATEGY, AND EXACTLY ONE. The other arm [:L321-L385] has FOUR shapes, selected by whether a
//  unique-index column list was supplied [:L323] and whether native paging was requested [:L343,
//  :L366]. THIS ARM READS NEITHER FLAG. It has no page-native variant and no unique-index variant,
//  so PagingRewriteRequest.PageNative and PagingRewriteRequest.PagedUniqueIndexColumns are
//  deliberately NOT consulted below. That is measured from the arm, not assumed: `_bPageNative` and
//  `_sPagedUniqueIndexColumns` appear nowhere between :L386 and :L395.
//
//  FIVE THINGS THE OTHER ARM DOES THAT THIS ARM MUST NOT (C-B). Each is a real trap, because each
//  reads as a natural completion of the pattern and each would break byte-exact parity:
//    1. NO column-list capture and NO restore. `sOrigColumns = sqlParser.GetColumn()` [:L339] and
//       its two restores [:L348, :L358] are SQL-Server-only. GetColumn is never called here.
//    2. NO ModifyColumn and NO ModifyTable. [:L345, :L348, :L350, :L355, :L358, :L362] are all
//       SQL-Server-only. The ONLY clause this arm modifies is ORDER BY, and only to strip it.
//    3. NO trailing re-read of the statement. The other arm ends `sql = sqlParser.GetSQL()`
//       [:L364] because it mutated the model into its final shape. THIS ARM'S RESULT IS THE
//       CONCATENATION ITSELF - do not append a re-read by analogy.
//    4. NO ` ORDER BY pfwPagedSQL_RN` suffix. That is [:L383], SQL-Server-only. The outermost
//       SELECT here carries a WHERE and no ORDER BY at all.
//    5. NO OFFSET/FETCH and NO TOP. Neither `OFFSET`, nor `FETCH NEXT`, nor `ROWS ONLY`, nor `TOP`
//       appears in the Oracle dialect's output. Row limiting is done ENTIRELY by the row-number
//       predicates, twice: `<=` on the middle SELECT and `BETWEEN` on the outer one.
//
//  THE EMPTY-ORDER-BY SUBSTITUTION IS DIALECT SPLIT. THIS IS THE PARITY TRAP IN THIS FOLDER (C-K)
//  --------------------------------------------------------------------------------------------
//  When the parsed statement carries no ORDER BY, the two arms substitute DIFFERENT TEXT:
//
//      Oracle      - THIS FILE      ->  ''  two apostrophes, an empty string literal    [:L392]
//      SQL Server  - NOT THIS FILE  ->  a parenthesised scalar subquery selecting a constant.
//                                       ITS TEXT IS DELIBERATELY NOT SPELLED ANYWHERE IN
//                                       THIS FILE - read it at its locator instead    [:L370, :L379]
//
//  Both legacy comments read 原始结果集排序 - "original result-set ordering" - so the INTENT is
//  identical while the TEXT is not, which is exactly what makes the trap dangerous: a reader who
//  matches on intent will happily unify them and every byte-exact assertion in the folder will
//  still look plausible while being wrong. The substitution therefore lives HERE, as a constant of
//  THIS class, and:
//    * it is NOT hoisted into IPagingRewriter.cs. That file deliberately carries no clause literal
//      at all, so that neither arm has any reason to reach for the other's text; hoisting one of the
//      two would couple the arms through the very contract that exists to keep them independent, and
//      hoisting BOTH would put a SQL-Server literal on a contract the Oracle arm implements.
//    * it is NOT obtained by referencing the SQL Server rewriter. This file names that type nowhere,
//      takes no dependency on it in either direction, and does not quote its text.
//  ASSERTION FOR A REVIEWER OR A TEST, AND IT IS MECHANICALLY CHECKED: the SQL Server substitution
//  text appears NOWHERE in this file - not in code, and DELIBERATELY NOT IN PROSE EITHER, which is
//  exactly why every reference to it above and below is a LOCATOR rather than a quotation - while
//  `''` IS present, once, as the constant declared below. A grep of this file for the SQL Server text
//  therefore returns zero. The locator is authoritative and the quotation would not have been: the
//  read-only oracle is the single place either dialect's text is allowed to live.
//
//  THE ONE-SPACE FINDING, MEASURED RATHER THAN EYEBALLED (C-K)
//  --------------------------------------------------------------------------------------------
//  The legacy statement is built across a PowerScript `+ &` line continuation, and the space either
//  side of that continuation is load bearing, so it was measured with whitespace made visible
//  (`sed -n '386,395p' <file> | cat -A`) rather than read:
//
//      :L394 ends with   ... + ") pfwPagedSQL_TblOuter " + &      <- ONE trailing space, INSIDE the
//                                                                    literal, before the `+ &`
//      :L395 begins with "WHERE pfwPagedSQL_RN BETWEEN "          <- NO leading space
//
//  `&` is a line continuation, not a concatenation operator, so it contributes no character of its
//  own and the leading tabs indenting :L395 are source indentation outside the string literal.
//  THE RESULT THEREFORE CARRIES EXACTLY ONE SPACE between `pfwPagedSQL_TblOuter` and `WHERE` - not
//  two, and not zero. The emission below appends that single space as its own step, on its own line,
//  with its own comment, precisely so it cannot be lost in a later edit or silently doubled.
//
//  Two smaller whitespace facts from the same measurement, each equally load bearing and each
//  equally invisible to a casual read:
//    * `pfwPagedSQL_TblInnerInner.*,ROW_NUMBER()` has NO SPACE before the comma. It is not tidied.
//    * `ROW_NUMBER() OVER (ORDER BY ` has one space after `ROW_NUMBER()`, one after `OVER`, and one
//      after `BY`, and no space after the opening parenthesis.
//
//  C-E - NO FABRICATED DATABASE. THIS IS THE DEFINING CONSTRAINT ON THIS FILE.
//  --------------------------------------------------------------------------------------------
//  This class is A PURE STRING TRANSFORM: statement text plus a page size and a page index go in,
//  rewritten statement text comes out. Two measured facts force that and are recorded here so the
//  shape reads as a finding rather than a preference:
//
//    1. SQLITE IS ABSENT FROM THE LEGACY DATABASE-TYPE ENUMERATION ENTIRELY. The oracle declares
//       exactly two discriminators, DBT_MSSQL = 0 [n_cst_thread_trans.sru:L60] and DBT_ORACLE = 1
//       [:L61]. There is no third constant - even though SQLite is the only storage engine this
//       system provisions and the only one with an evidenced schema.
//    2. NEITHER SQL SERVER NOR ORACLE HAS ANY SCHEMA, CONNECTION STRING OR DDL ANYWHERE IN THE
//       REPOSITORY. All that exists for either is those two type constants and the two statement
//       generators at [n_cst_thread_task_sqlquery.sru:L321-L385] and [:L386-L395].
//
//  So the discriminator this class answers to selects A TEXT GENERATOR, NOT A CONNECTION. Nothing
//  here opens, holds, borrows or describes a connection of any kind: no Oracle client package and no
//  ODP.NET, no ADO connection or command type, no connection-parameter string, no provider handle,
//  no credential, no transaction object, no I/O and no ambient state. A reviewer's grep over this
//  file for any of those type names returns nothing at all, deliberately including in prose.
//
//  NO THIRD-PARTY SQL PARSER EITHER, AND THAT WAS A DECISION RATHER THAN AN OMISSION. The obvious
//  candidate - Microsoft's Transact-SQL script-object-model parser, named in full and rejected by
//  name at Directory.Packages.props:L377-L379 - was REJECTED precisely because it is
//  SQL-SERVER-DIALECT-SPECIFIC AND THEREFORE CANNOT SERVE THIS REWRITER AT ALL, and because no SQL
//  parser exists in the base class library. Its type name is deliberately not spelled here, for the
//  same reason no connection type name is: a reviewer's grep of this file for a forbidden dependency
//  must come back empty, and the rejected-package register is the one place that name belongs.
//  The acceptance criterion here is byte-exact output anyway, so a parser that normalised or
//  reformatted its input would actively DEFEAT the requirement rather than help meet it.
//  Clause-level parsing is done by the in-repository SelectStatementModel and by nothing else.
//
//  THE BUILD ENFORCES ALL OF THAT rather than merely asking for it: central package management is
//  on, every PackageReference in the project is versionless, and Directory.Packages.props carries no
//  Oracle client and no third-party SQL parser, so adding either fails restore with a missing
//  PackageVersion. NO PACKAGE REFERENCE IS ADDED FOR THIS FILE.
//
//  A PURE FUNCTION, AND NOT THREAD AFFINE
//  --------------------------------------------------------------------------------------------
//  This class holds NO field - only compile-time constants - so there is nothing to configure,
//  nothing to mutate and nothing to dispose. No static mutable state, no clock read, no environment
//  read, no file access, no randomness, no logging. The same inputs always produce the same text.
//
//  That is worth stating explicitly because it is the EXCEPTION in this service. Thirteen legacy SQL
//  task classes carry MANDATORY thread affinity in their own source comments - six worker-thread,
//  one main-thread and four calling-thread proxies - and the transformation plan preserves that
//  duality as an explicit marshalling boundary rather than flattening it. NOTHING IN THIS FOLDER IS
//  THREAD AFFINE. A single instance is safe to register as a singleton and to call concurrently from
//  any thread, and no marshalling boundary is required around it. The mutation this method performs
//  is confined to the SelectStatementModel instance it is HANDED, which the dispatch contract
//  creates per call [IPagingRewriter.cs, PagingRewriteDispatcher.Rewrite step 2], so two concurrent
//  calls share nothing.
//
//  NO ARRAY INDEXING, SO THE ONE-BASED HAZARD DOES NOT LAND HERE - STATED RATHER THAN LEFT AMBIGUOUS
//  --------------------------------------------------------------------------------------------
//  One-based to zero-based array translation is the most dangerous mechanical hazard in this
//  refactor: PowerBuilder arrays are one-based and UpperBound returns the LAST VALID INDEX, so a
//  mechanically ported loop is off by one in a way that is indistinguishable from a behavioural
//  regression. THIS FILE CONTAINS NO ARRAY, NO LIST, NO INDEXER AND NO LOOP OF ANY KIND, so the
//  hazard cannot land here and no indexing helper is needed. The neighbouring arm does index an
//  array [:L328-L338] and this one does not; the difference is recorded so nobody has to re-derive
//  it. Note that the PAGE INDEX is nonetheless ONE BASED - see the remarks on Rewrite - which is a
//  parameter convention and not an array-indexing concern.
//
//  NO PERFORMANCE CLAIM IS MADE ANYWHERE
//  --------------------------------------------------------------------------------------------
//  The repository publishes no service-level agreement, no latency budget, no throughput target and
//  no availability commitment, so no performance objective may be asserted, claimed as met, or used
//  to justify a choice here. The neighbouring arm's own legacy comment calls its unique-index branch
//  a fast paging optimisation [:L324-L326]; this arm carries no such comment and this file makes no
//  such claim, for itself or for the statement it emits. Behaviour is reproduced; nothing is tuned.
//
//  WHAT IS DELIBERATELY NOT IN THIS FILE
//  --------------------------------------------------------------------------------------------
//    * NO paging-bounds guard. `_nPageSize <= 0 or _nPageIndex <= 0 -> E_INVALID_ARGUMENT` with the
//      diagnostic 无效的分页设置! is [:L307-L310], which runs BEFORE the dispatch and therefore
//      belongs to PagingRewriteDispatcher. Duplicating it here would put one behaviour in two
//      places, and the two could then disagree about which code a badly paged request returns.
//    * NO parse and NO parse-failure arm. `Create n_sql` [:L312] and
//      `if Not sqlParser.Parse(origSql) -> E_INTERNAL_ERROR` with SQL解析失败! [:L314-L317] also run
//      before the dispatch. This class receives an ALREADY PARSED model; an arm that parsed for
//      itself would move that exit inside the arms and change what an unparseable statement returns
//      for an unrecognised dialect.
//    * NO `case else` arm. `E_NO_IMPLEMENTATION` with its deliberately EMPTY message [:L396-L398]
//      is the dispatcher's. Worth knowing while reading this file: SQLITE NEVER REACHES IT, because
//      of_getdbtype() is literally `if Pos(Upper(DBMS),"ORACLE") > 0 then return DBT_ORACLE else
//      return DBT_MSSQL` [n_cst_thread_trans.sru:L356-L361], so every engine name lacking "ORACLE",
//      SQLite included, routes to the SQL Server arm. That is the legacy contract and is NOT a
//      defect to fix (C-B).
//    * NO count wrapper. The `SELECT COUNT(1) AS CNT FROM (...)` form and its `1 AS _` column
//      replacement [:L826-L834] belong to the query task, not to a paging rewriter. The derived-table
//      alias that form uses is therefore ABSENT here, as is the alias the other arm's INNER JOIN
//      subquery uses - the one whose doubled `t` is the legacy spelling and is not corrected anywhere,
//      merely not used here. NEITHER SPELLING APPEARS IN THIS FILE, in code or in prose; read them at
//      their locators, [:L834] and [:L333, :L350, :L362] respectively. The FOUR aliases this arm does
//      emit are the four constants declared below, and there are no others.
//    * NO SCREAMING_SNAKE identifier. The repository-root .editorconfig scopes its CA1707 and
//      IDE1006 suppressions to the files on its BAND 3 roster - the single source of truth for that
//      list - and THIS FILE IS NOT ONE OF THEM, while
//      Directory.Build.props sets TreatWarningsAsErrors repository-wide - so such an identifier
//      declared here is a COMPILE ERROR, not a style debate. SQL_MS_REPLACE is consumed from
//      PowerFramework.Shared.Kernel.Enums and DBT_ORACLE from the generated contract enum, where
//      protoc renders it DbtOracle. The five constants below are the permitted exception IN FORM
//      ONLY: each legacy LITERAL is preserved exactly, while each C# IDENTIFIER is PascalCase.
//    * NO test IN THIS FILE. Tests for this class belong in PowerFramework.Persistence.Tests, to which
//      the application project already grants internal access, and every branch below is reachable from
//      a table-driven theory whose entire fixture is strings - no database, no connection, no client
//      package and no fixture object of any kind (C-H).
//      WHERE THEY LIVE: OraclePagingRewriterTests.cs holds the matrix for this class, and
//      PagingDispatcherAndCountTests.cs covers its selection through PagingRewriteDispatcher. The
//      triple-nested form, its three nesting aliases and the empty-order-by substitution below are all
//      pinned there. docs/PARITY.md section 6.2 states the rule that every expectation be derived from
//      the LEGACY generator at n_cst_thread_task_sqlquery.sru:L392-L398 rather than from this file.
// ==============================================================================================

using System.Globalization;
using System.Text;
using PowerFramework.Contracts.Persistence.V1;
using PowerFramework.Shared.Kernel;

namespace PowerFramework.Persistence.Sql.Paging;

/// <summary>
/// The Oracle dialect's paging rewriter: the managed form of the
/// <c>case TransObject.DBT_ORACLE</c> arm at
/// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L386-L395</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>One strategy, triple nested.</b> Where the SQL Server arm has four shapes, this arm has
/// exactly one: a three-deep <c>SELECT</c> in which the innermost level is the caller's own
/// statement with its <c>ORDER BY</c> stripped, the middle level projects an
/// <c>ROW_NUMBER()</c> window over the order-by expression and caps it with <c>&lt;=</c>, and the
/// outer level narrows to the requested page with <c>BETWEEN</c>. The nesting is NOT simplified:
/// two row-number predicates over what looks like one range is exactly what the oracle emits, and
/// collapsing it would change the statement text that parity is measured on.
/// </para>
/// <para>
/// <b>A pure string transform (C-E).</b> Nothing here reaches a database. No Oracle client package
/// is referenced, no connection is opened or held, and no schema is assumed - the repository
/// contains no Oracle schema, connection string or DDL at all, only the discriminator constant and
/// this generator. The whole class is therefore exercisable from a table-driven theory whose only
/// fixture is strings.
/// </para>
/// <para>
/// <b>Stateless, shareable and not thread affine.</b> The type holds no field, only compile-time
/// constants, so one instance may be registered as a singleton and called concurrently. Unlike the
/// legacy SQL task classes, which carry mandatory thread affinity in their own source comments,
/// nothing in this folder needs a marshalling boundary.
/// </para>
/// <para>
/// <b>No performance claim.</b> Behaviour is reproduced; no latency, throughput or efficiency
/// benefit is asserted for this rewriter or for the statement it emits, because the repository
/// publishes no target against which such a claim could be made.
/// </para>
/// </remarks>
internal sealed class OraclePagingRewriter : IPagingRewriter
{
    /// <summary>
    /// The alias of the INNERMOST derived table - the one wrapping the caller's own statement -
    /// spelled exactly as <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L394</c>
    /// spells it. It appears TWICE in the emitted statement: once qualifying the <c>.*</c>
    /// projection, once as the alias itself.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Declared INDEPENDENTLY of <see cref="InnerTableAlias"/> and deliberately NOT composed from
    /// it, even though this value is that one plus <c>Inner</c>. Composing them would make one
    /// legacy identifier's spelling depend on the other's, so a single edit could silently move both.
    /// </para>
    /// <para>
    /// <b>A grep hazard worth knowing before counting occurrences:</b> this value CONTAINS
    /// <see cref="InnerTableAlias"/> as a prefix, so a naive search for the shorter name over the
    /// emitted statement finds three matches, not the one that is genuinely the middle table's
    /// alias. Count the longer name first, or match on the trailing delimiter.
    /// </para>
    /// <para>
    /// Exposed as a constant so a test can assert the exact spelling without retyping it. The
    /// SCREAMING form is the VALUE, not the identifier, so no naming diagnostic applies.
    /// </para>
    /// </remarks>
    public const string InnerInnerTableAlias = "pfwPagedSQL_TblInnerInner";

    /// <summary>
    /// The alias of the MIDDLE derived table - the one carrying the row-number projection and the
    /// <c>&lt;=</c> cap - spelled exactly as <c>[:L394]</c> spells it. It appears ONCE.
    /// </summary>
    /// <remarks>
    /// See the grep hazard on <see cref="InnerInnerTableAlias"/>: this name is a strict prefix of
    /// that one.
    /// </remarks>
    public const string InnerTableAlias = "pfwPagedSQL_TblInner";

    /// <summary>
    /// The alias of the OUTERMOST derived table - the one the page-narrowing <c>BETWEEN</c> filters
    /// - spelled exactly as <c>[:L394]</c> spells it. It appears ONCE.
    /// </summary>
    /// <remarks>
    /// <b>Not to be confused with the SQL Server arm's <c>INNER JOIN</c> subquery alias</b>, which is
    /// spelled differently in three ways at once - a doubled <c>t</c>, the two words in the opposite
    /// order, and no <c>Tbl</c> prefix - and which belongs to <c>[:L333]</c>, <c>[:L350]</c> and
    /// <c>[:L362]</c>. That spelling appears nowhere in this dialect's output and, deliberately,
    /// nowhere in this file either; read it at those locators in the read-only oracle. NEITHER
    /// spelling is corrected - they are simply different identifiers in different statements (C-B).
    /// </remarks>
    public const string OuterTableAlias = "pfwPagedSQL_TblOuter";

    /// <summary>
    /// The alias of the generated row-number column, spelled exactly as <c>[:L394]</c> spells it.
    /// It appears THREE times in the emitted statement: once where it is defined by <c>AS</c>, once
    /// in the middle level's <c>&lt;=</c> predicate, and once in the outer level's <c>BETWEEN</c>
    /// predicate.
    /// </summary>
    /// <remarks>
    /// The same spelling is used by the SQL Server arm at <c>[:L355]</c>, <c>[:L356]</c>,
    /// <c>[:L381]</c>, <c>[:L382]</c> and <c>[:L383]</c>. It is nonetheless declared here rather
    /// than shared, because sharing it would make this file depend on that one - which the dispatch
    /// contract explicitly forbids - and because a single shared declaration would imply the two
    /// arms must always agree on it, which is a constraint the oracle does not impose.
    /// </remarks>
    public const string RowNumberColumnAlias = "pfwPagedSQL_RN";

    /// <summary>
    /// The expression substituted for the window's <c>ORDER BY</c> when the parsed statement carries
    /// no <c>ORDER BY</c> of its own: two apostrophes, an empty string literal, exactly as
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L392</c> spells it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>THIS VALUE IS DIALECT SPLIT AND IS THE PARITY TRAP IN THIS FOLDER.</b> The Oracle arm
    /// substitutes <c>''</c> at <c>[:L392]</c>; the SQL Server arm substitutes an entirely different
    /// expression - a parenthesised scalar subquery selecting a constant, quoted nowhere in this file
    /// and readable at <c>[:L370]</c> and <c>[:L379]</c>. Both legacy comments read
    /// <c>原始结果集排序</c> - original
    /// result-set ordering - so the two share an INTENT while sharing no TEXT, which is precisely
    /// what makes unifying them tempting and wrong. Using one for both silently breaks byte-exact
    /// parity while leaving every assertion looking plausible.
    /// </para>
    /// <para>
    /// <b>It therefore stays here and is not hoisted.</b> Not into the dispatch contract, which
    /// deliberately carries no clause literal so that neither arm has a reason to reach for the
    /// other's text, and not into any shared constant. The SQL Server text is not referenced from
    /// this file in ANY form - not by a using directive, not by a type reference, and not even as a
    /// quotation inside this documentation. Its absence is therefore a greppable, mechanically
    /// checked property of the file rather than a claim about it.
    /// </para>
    /// <para>
    /// Semantically the two are both stable sort keys that impose no ordering - a constant per row -
    /// which is what lets the window function be well formed without disturbing the caller's own
    /// row order. That equivalence of PURPOSE is exactly why the difference in TEXT has to be
    /// written down rather than reasoned away.
    /// </para>
    /// </remarks>
    public const string AbsentOrderBySubstitution = "''";

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// The generated projection of <c>constant long DBT_ORACLE = 1</c> at
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:L61</c>, CONSUMED from the
    /// published contract enum rather than re-declared, so exactly one definition of the
    /// discriminator exists and the two cannot drift. <c>protoc</c> renders <c>DBT_ORACLE</c> as
    /// <c>DbtOracle</c>, which carries no underscore and therefore raises no naming diagnostic in a
    /// file the analyzer suppressions do not cover.
    /// </para>
    /// <para>
    /// An expression-bodied property rather than a stored field, so the type keeps its no-field,
    /// nothing-to-mutate shape.
    /// </para>
    /// </remarks>
    public DatabaseType Dialect => DatabaseType.DbtOracle;

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// <see langword="false"/>, AND THAT IS A TRANSCRIBED FACT ABOUT THE ORACLE'S ARM RATHER THAN A
    /// CHOICE. <c>[:L386-L395]</c> never reads <c>_sPagedUniqueIndexColumns</c> at all - the collection
    /// simply does not appear in the arm - so this dialect emits no identifier a caller supplied and has no
    /// concatenation site to protect.
    /// </para>
    /// <para>
    /// The consequence is deliberate: <see cref="PagingRewriteDispatcher"/> does NOT validate the
    /// collection when this arm is selected, so a request carrying a malformed column reaches this arm and
    /// is answered exactly as before, with the collection ignored. Rejecting it would be a narrowing with
    /// no injection to prevent, and it would change an outcome the legacy defines (C-B).
    /// </para>
    /// </remarks>
    public bool ConsumesPagedUniqueIndexColumns => false;

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// Reproduces <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L386-L395</c>
    /// in full and reproduces nothing else. The oracle's arm contains no <c>return</c> of its own, so
    /// this method has exactly one exit and it is always
    /// <see cref="PagingRewriteResult.Ok(string)"/>; a failure result from here would have no
    /// counterpart in the specification.
    /// </para>
    /// <para>
    /// <b>THE CALL ORDER IS THE CONTRACT, NOT MERELY THE CALL SET.</b> Four parser members are
    /// driven, in this order and no other:
    /// </para>
    /// <list type="number">
    /// <item><description><see cref="SelectStatementModel.HasOrder()"/> - <c>[:L388]</c>, the
    /// branch.</description></item>
    /// <item><description><see cref="SelectStatementModel.GetOrder()"/> - <c>[:L389]</c>, read
    /// BEFORE the strip, because after the strip there would be nothing left to
    /// read.</description></item>
    /// <item><description><see cref="SelectStatementModel.ModifyOrder(long, string)"/> -
    /// <c>[:L390]</c>, the strip.</description></item>
    /// <item><description><see cref="SelectStatementModel.GetSql()"/> - <c>[:L394]</c>, read INSIDE
    /// the concatenation and therefore AFTER the strip, which is what makes the innermost derived
    /// table carry no <c>ORDER BY</c>.</description></item>
    /// </list>
    /// <para>
    /// Reordering steps 2 and 3 loses the order-by expression; reordering steps 3 and 4 leaves an
    /// <c>ORDER BY</c> inside the innermost derived table. Either produces a statement that still
    /// parses and still looks right, which is why the ordering is written down here as well as
    /// implemented below.
    /// </para>
    /// <para>
    /// <b>The page index is ONE BASED</b>, which is the legacy convention and is preserved: the
    /// lower bound is computed as <c>pageSize * (pageIndex - 1) + 1</c> <c>[:L395]</c>, so the first
    /// page starts at row 1 rather than row 0. This is a parameter convention, not array indexing -
    /// see the header note on why the one-based translation hazard does not land in this file.
    /// </para>
    /// <para>
    /// <b>The statement model is MUTATED</b>, by design: the strip at step 3 changes it, and the
    /// dispatch contract states that a model is handed to an arm for modification rather than as
    /// read-only. Nothing is restored afterwards, because the oracle restores nothing here - the
    /// restores at <c>[:L348]</c>, <c>[:L358]</c> and <c>[:L360]</c> belong to the other arm. The
    /// caller must therefore treat the model as spent, which is safe because the dispatcher creates
    /// one per call.
    /// </para>
    /// <para>
    /// <b>Neither <see cref="PagingRewriteRequest.PageNative"/> nor
    /// <see cref="PagingRewriteRequest.PagedUniqueIndexColumns"/> is consulted</b>, because this arm
    /// reads neither flag: <c>_bPageNative</c> and <c>_sPagedUniqueIndexColumns</c> appear nowhere
    /// between <c>[:L386]</c> and <c>[:L395]</c>. Oracle has one strategy, not four.
    /// </para>
    /// <para>
    /// <b>The paging bounds are NOT re-checked here.</b> The
    /// <c>pageSize &lt;= 0 or pageIndex &lt;= 0</c> guard is <c>[:L307-L310]</c>, which runs before
    /// the dispatch and belongs to <see cref="PagingRewriteDispatcher"/>. Duplicating it would put
    /// one behaviour in two places that could then disagree. A caller who bypasses the dispatcher and
    /// supplies non-positive values gets the arithmetic those values imply, exactly as the legacy
    /// would have if its own guard were removed.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="statement"/> is
    /// <see langword="null"/>.</exception>
    public PagingRewriteResult Rewrite(in PagingRewriteRequest request, SelectStatementModel statement)
    {
        // A managed-only reference contract with NO legacy analogue: PowerScript would simply fault
        // on a null object reference, and the dispatcher never passes null, so this cannot change any
        // outcome the oracle can produce. It converts an unavoidable NullReferenceException into a
        // precise, actionable one, matching the guard the dispatch contract applies to its own
        // reference parameter. It is NOT one of the legacy guards - those are enumerated in the
        // header and all three stay on the dispatcher.
        ArgumentNullException.ThrowIfNull(statement);

        // ------------------------------------------------------------------------------------------
        // [:L388-L393] Resolve the window's ORDER BY expression, and strip the statement's own.
        // ------------------------------------------------------------------------------------------
        string effectiveOrderBy;

        if (statement.HasOrder())                                             // [:L388]
        {
            // [:L389] Read BEFORE the strip below. The clause BODY only - GetOrder returns the text
            // after `ORDER BY`, without the introducer - which is exactly what belongs inside
            // `OVER (ORDER BY ...)`.
            effectiveOrderBy = statement.GetOrder();

            // [:L390] `sqlParser.ModifyOrder(Enums.SQL_MS_REPLACE,"")` //去掉ORDER BY语句
            //
            // Replacing with empty text STRIPS the clause, so the innermost derived table below
            // carries no ORDER BY - which is the point: a derived table's own ordering is not
            // meaningful, and the ordering it carried has been lifted into the window function
            // instead. `string.Empty` is the oracle's `""`; the two are the same value.
            //
            // The boolean result is DISCARDED, exactly as [:L390] discards it. The discard is written
            // explicitly rather than left implicit so that it reads as a transcribed decision. The
            // oracle does not test it and neither does this port: introducing a failure path here
            // would invent an exit the specification does not have.
            //
            // SQL_MS_REPLACE is CONSUMED from the shared kernel - the managed form of
            // `Constant Long SQL_MS_REPLACE = 1` at ws_objects/pfw.shared.pbl.src/enums.sru:L718 -
            // and is never re-declared. Its parameter type is `long`, faithful to
            // `modifyorder(readonly long ms, readonly string neworder)` at n_sql.sru:L48; never int.
            _ = statement.ModifyOrder(Enums.SQL_MS_REPLACE, string.Empty);
        }
        else
        {
            // [:L392] `sOrderBy = "''"` //原始结果集排序
            //
            // THE DIALECT-SPLIT SUBSTITUTION. Two apostrophes - NOT the scalar subquery the SQL Server
            // arm substitutes at [:L370] and [:L379], whose text is deliberately quoted nowhere in
            // this file. See the note on AbsentOrderBySubstitution for why the two must never be
            // unified, and why a locator rather than a quotation is used to point at the other one.
            //
            // NOTE THE ASYMMETRY, which is the oracle's: this branch performs NO strip. There is no
            // ORDER BY to remove, so ModifyOrder is not called at all - not even with empty text.
            // That is observable through the model handed back to the caller, so it is preserved.
            effectiveOrderBy = AbsentOrderBySubstitution;
        }

        // ------------------------------------------------------------------------------------------
        // [:L394-L395] The row-number bounds.
        // ------------------------------------------------------------------------------------------
        // The oracle evaluates `String(_nPageSize * _nPageIndex)` TWICE, once per line, from operands
        // neither line can have changed; computing it once and emitting it twice therefore produces
        // identical text. This is a statement about equal operands, not an optimisation - no
        // performance benefit is claimed for it.
        //
        // Arithmetic is done in `long`, the mapping the transformation plan gives PowerBuilder's
        // `long`, and it is CHECKED.
        //
        // THE UNCHECKED READING IS WRONG. The argument for it runs: the oracle
        // has no overflow check, so adding one invents a failure mode the specification does not have.
        // It does not hold, for two reasons. First, an unchecked product does not preserve legacy
        // behaviour - it INVENTS behaviour of its own: `PageSize * PageIndex` wraps to a NEGATIVE
        // number, and `BETWEEN <negative> AND <negative>` is a statement that silently matches no row
        // rather than one that reports a fault, which is the worst of the three possible outcomes.
        // Second, the widths differ: PowerScript `long` is 32-bit [:L36-L37] while these fields are
        // 64-bit, so the wrap point is not the legacy's either - there is no legacy behaviour here to
        // be faithful to.
        //
        // THE GUARD THAT MATTERS IS UPSTREAM, in PagingRewriteRequest.HasRepresentablePagingProducts,
        // which the dispatcher tests BEFORE any statement is parsed and answers the same
        // invalid-paging outcome the oracle's own bounds test answers [:L307-L310]. `checked` here is
        // the backstop for a caller that reaches this arm directly - which is to say a test - so that
        // such a caller gets an exception rather than a plausible-looking statement built on a
        // negative bound.
        long upperRowNumber = checked(request.PageSize * request.PageIndex);        // [:L394], [:L395]
        long lowerRowNumber = checked((request.PageSize * (request.PageIndex - 1)) + 1);  // ONE BASED

        // INVARIANT CULTURE IS MANDATORY. PowerBuilder's `String(long)` emits plain digits with no
        // group separator and a culture-independent sign, so a culture-sensitive conversion here
        // would break byte-exact parity on a host whose current culture formats integers
        // differently - a defect that would pass every test on the developer's machine and fail in
        // one region. Formatting to a string up front also keeps every Append below a string append,
        // so no implicit, culture-sensitive number conversion can creep in later.
        string upperRowNumberText = upperRowNumber.ToString(CultureInfo.InvariantCulture);
        string lowerRowNumberText = lowerRowNumber.ToString(CultureInfo.InvariantCulture);

        // ------------------------------------------------------------------------------------------
        // [:L394-L395] The statement, emitted segment by segment.
        // ------------------------------------------------------------------------------------------
        // Each Append below corresponds to one piece of the two legacy source lines, in source order,
        // so this block can be diffed against the file character by character. The oracle's literals
        // are split only where a sentinel constant or an interpolated value falls inside them; no
        // character is added, removed, reordered or re-spaced. Measured with `cat -A`, not eyeballed.
        StringBuilder builder = new();

        // "SELECT * FROM (SELECT * FROM (SELECT " + <inner-inner alias> + ".*,ROW_NUMBER() OVER (ORDER BY "
        // THREE opening parentheses, one per nesting level. NO SPACE before the comma after `.*` -
        // that is how [:L394] spells it and it is not tidied (C-B). One space after `ROW_NUMBER()`,
        // one after `OVER`, none after `(`, and one after `BY` so the expression that follows is
        // separated by exactly one.
        builder.Append("SELECT * FROM (SELECT * FROM (SELECT ");
        builder.Append(InnerInnerTableAlias);
        builder.Append(".*,ROW_NUMBER() OVER (ORDER BY ");

        // + sOrderBy +   the clause body from [:L389], or the '' substitution from [:L392]
        builder.Append(effectiveOrderBy);

        // ") AS " + <row-number alias> + " FROM ("
        // The `)` closes `OVER (ORDER BY ...`; the trailing `(` opens the innermost derived table.
        builder.Append(") AS ");
        builder.Append(RowNumberColumnAlias);
        builder.Append(" FROM (");

        // + sqlParser.GetSQL() +
        // READ HERE, INSIDE THE CONCATENATION, AND THEREFORE AFTER THE STRIP ABOVE. This single call
        // position is what guarantees the innermost derived table carries no ORDER BY. Hoisting it
        // above the strip would be a one-line change that still compiles, still parses and is wrong.
        //
        // THE UNTERMINATED READ, AND THE DISTINCTION IS NOT COSMETIC. This text becomes the INNERMOST
        // DERIVED TABLE - the very next character appended is `)` and three more nesting levels of
        // statement follow it - so a trailing `;` retained here would terminate the whole generated
        // statement inside the first parenthesis and silently discard the row-number projection, both
        // WHERE clauses and every alias. A review found exactly that. The caller's own terminator is
        // re-attached at the OUTERMOST end below.
        builder.Append(statement.GetSqlWithoutTerminator());

        // ") " + <inner-inner alias> + ") " + <inner alias> + " WHERE " + <row-number alias> + " <= "
        // The first `)` closes the innermost derived table and is followed by its alias; the second
        // closes the middle one and is followed by ITS alias. The middle level's cap is `<=`, an
        // upper bound only - the lower bound is applied one level out, by the BETWEEN below.
        builder.Append(") ");
        builder.Append(InnerInnerTableAlias);
        builder.Append(") ");
        builder.Append(InnerTableAlias);
        builder.Append(" WHERE ");
        builder.Append(RowNumberColumnAlias);
        builder.Append(" <= ");

        // + String(_nPageSize * _nPageIndex) +
        builder.Append(upperRowNumberText);

        // ") " + <outer alias>
        builder.Append(") ");
        builder.Append(OuterTableAlias);

        // THE ONE SPACE. [:L394] ends `... + ") pfwPagedSQL_TblOuter " + &` with the space INSIDE the
        // literal, and [:L395] begins `"WHERE ...` with none - `&` being a line continuation that
        // contributes no character of its own. So exactly ONE space separates the outer alias from
        // WHERE: not two, and not zero. Appended as its own statement, on its own line, so it cannot
        // be silently doubled by an edit to either neighbour or lost when one of them is re-wrapped.
        builder.Append(' ');

        // [:L395] "WHERE " + <row-number alias> + " BETWEEN " + String(_nPageSize * (_nPageIndex - 1) + 1)
        //         + " AND " + String(_nPageSize * _nPageIndex)
        // The outer predicate names the SAME row-number column, which is legal because the middle
        // level projected it and the derived table therefore exposes it. Note there is NO trailing
        // ORDER BY on this outermost SELECT - the other arm appends one at [:L383] and this one does
        // not - and no OFFSET, FETCH or TOP anywhere in the dialect.
        builder.Append("WHERE ");
        builder.Append(RowNumberColumnAlias);
        builder.Append(" BETWEEN ");
        builder.Append(lowerRowNumberText);
        builder.Append(" AND ");
        builder.Append(upperRowNumberText);

        // THE CALLER'S TERMINATOR, RE-ATTACHED AT THE OUTERMOST END AND NOWHERE ELSE. Empty for a
        // statement that carried none, which is every statement the legacy fixtures supply, so this
        // appends nothing at all on the measured path and the byte-exact expectations are unchanged.
        // For a statement that DID carry one, this is the only position where a terminator is still a
        // terminator rather than a truncation.
        builder.Append(statement.StatementTerminator);

        // [:L403] `return RetCode.OK`, reached at [:L399] once the arm falls out of the choose case.
        // THE RESULT IS THE CONCATENATION ITSELF. The other arm finishes with a re-read of the model
        // [:L364] because it mutated the model into its final shape; this one built its result
        // directly, so a re-read here would DISCARD the entire statement just assembled and return
        // the caller's stripped input instead.
        return PagingRewriteResult.Ok(builder.ToString());
    }
}
