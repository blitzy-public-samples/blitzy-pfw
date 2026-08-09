// ==============================================================================================
//  IdentityColumnResolver.cs - THE TWO-ARRAY IDENTITY ROUND TRIP
//  --------------------------------------------------------------------------------------------
//  PORT OF        ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru:L214-L248
//                     the identity block of `_of_update` (:L172-L252), which is the whole of the
//                     behaviour below
//  CALLER SIDE    ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlupdate.sru
//                     `IdColData` :L10-L14, `onupdated` :L66-L68,
//                     `onidentitycolumndataretrieved` :L71-L77,
//                     `of_refreshidentitydata` :L120-L205  (the APPLY half - NOT ported here)
//  CARRIER        ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds.sru:L73,L76,L79
//                     the three count getters the :L247 triple reads
//  FIXTURE        ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L8,L14,L21-L26
//                     the sole updatable DataWindow in the repository
//
//  ORACLE STATUS (C-C). Every path above is READ ONLY. They are the behavioural oracle for parity
//  testing and are never an edit target, never reformatted, never moved and never copied into the
//  .NET tree. Nothing else in this repository can adjudicate the behaviour below, so every
//  behavioural claim in this file carries a ws_objects/** line locator. A reader who doubts a line
//  here can diff it against the locator.
//
//  WHAT THIS FILE IS
//  --------------------------------------------------------------------------------------------
//  An auto-increment column's value is assigned by the database, not by the caller, so after a
//  successful insert the rows the caller holds do not yet carry the identity values the database
//  gave them. The legacy closes that gap with a round trip: the worker side COLLECTS the freshly
//  assigned values out of the update carrier and hands them back through one callback carrying two
//  `ref` array out-parameters, and the caller side APPLIES them to the rows they belong to.
//
//  THIS FILE IS THE COLLECT HALF, AND ONLY THAT HALF. It answers four questions and nothing else:
//
//      1. Should identity resolution run at all?           the inserted-count precondition [:L215]
//      2. Which column is the identity column?             run-time discovery       [:L217-L225]
//      3. Which values did the database assign, per buffer? the two collections      [:L228-L241]
//      4. Should anything be emitted?                      the emit guard            [:L242-L244]
//
//  plus the counts triple at [:L247], which fires on EVERY success and is deliberately not part of
//  the identity block. The APPLY half is `Tasks/TaskProxies/` - see THE WRITE-BACK CONTRACT below,
//  which is recorded here as documentation so that neither agent duplicates nor omits it.
//
//  ============================================================================================
//  DECISION 1 - THE BACKWARD FILTER LOOP IS CORRECT, AND HERE IS THE PROOF (C-K, C-B, R9)
//  ============================================================================================
//  The refactor plan names [:L237] THE SINGLE MOST DANGEROUS LINE IN THE WHOLE MIGRATION for
//  one-based-to-zero-based translation. It reads, literally:
//
//      for nIndex = nCount to 1 step -1                    [:L237]
//
//  while its Primary-buffer twin eleven lines earlier reads:
//
//      for nIndex = 1 to nCount                            [:L229]
//
//  IT LOOKS LIKE A BUG. IT IS NOT A BUG. Two independent pieces of first-hand evidence:
//
//  EVIDENCE 1 - THE ORACLE SAYS SO, IN A COMMENT SITTING IMMEDIATELY ABOVE THE LOOP. [:L235] is
//  verbatim:
//
//      //*过滤缓冲区的数据与数据源(调用GetChanges的对象)的过滤缓冲区的数据顺序是相反的
//
//  which states that the Filter buffer's row order is INVERTED relative to the data source - the
//  object that produced the changeset by calling GetChanges. The published contract carries the
//  same fact independently: common.v1.proto's DwBuffer declares DW_BUFFER_FILTER with the note
//  "ROW ORDER INVERTED".
//
//  EVIDENCE 2 - THE APPLY HALF WALKS FORWARD, WHICH IS WHAT MAKES BACKWARD COLLECTION CORRECT.
//  `of_refreshidentitydata` [n_cst_threading_task_sqlupdate.sru:L120-L205] consumes the Filter
//  array ASCENDING: it seeds `GetNextModified(0, Filter!)` [:L154 for the DataWindow arm, :L188 for
//  the DataStore arm], advances with `GetNextModified(nRow, Filter!)` [:L163, :L197], and indexes
//  the collected array with a counter that only ever increments [:L157-L160, :L191-L194], reading
//  `filterValues[i]` from i = 1 upward.
//
//      COLLECT BACKWARD  +  APPLY FORWARD  =  the inversion is undone.
//
//  Reverse the collection here and the SAME NUMBER OF VALUES still comes back, each paired with the
//  WRONG ROW. That is precisely why a row-count assertion cannot catch it, and precisely why the
//  regression guard in the sibling test file asserts the ORDER rather than the count. If a
//  reviewer, a linter or a future reader proposes "normalising" the descending loop into an
//  ascending one, this block is the answer: the loop is contract, not style.
//
//  ============================================================================================
//  DECISION 2 - THREE ASYMMETRIES BETWEEN THE TWO LOOPS, NOT ONE (C-B, C-K)
//  ============================================================================================
//  Flattening any one of the three breaks parity, so all three are preserved and each is annotated
//  where it occurs:
//
//      #  ASYMMETRY        PRIMARY [:L228-L233]                FILTER [:L234-L241]
//      1  direction        ascending, 1 to RowCount()          DESCENDING, FilteredCount() to 1
//      2  accessor arity   GetItemNumber(row, col)             GetItemNumber(row, col, Filter!, false)
//                          TWO arguments [:L231]               FOUR arguments [:L239]
//      3  buffer / count   Primary!, RowCount()      [:L230]   Filter!, FilteredCount()  [:L236,L238]
//
//  ASYMMETRY 2 IS REAL RATHER THAN COSMETIC, and it is the reason this file's injected value reader
//  declares the numeric accessor as TWO OVERLOADS rather than one method with defaulted arguments.
//  In PowerBuilder the two-argument form means "the Primary buffer's CURRENT value" by defaulting
//  both omitted arguments, and the four-argument form states the buffer and the original-value flag
//  explicitly. Collapsing them into a single four-argument call with `Primary!, false` would be
//  BEHAVIOURALLY equivalent and EVIDENTIALLY lossy: the parity suite could no longer prove which
//  form the port issues, and a later reader could not tell the two call sites apart. So the
//  overloads exist, the Primary loop calls the short one, the Filter loop calls the long one, and a
//  recording fake in the test project asserts exactly that.
//
//  ============================================================================================
//  DECISION 3 - DISCOVERY IS "FIRST WINS WITH A PREFIX OVERRIDE", NOT "FIRST MATCH WINS" (C-B)
//  ============================================================================================
//  The selection condition [:L221] is a two-armed OR, and the arms are not symmetric:
//
//      if Lower(Left(<DBName>, Len(prefix))) = prefix   or   nIdentityColumn = 0 then
//          nIdentityColumn = nIndex                                            [:L222]
//
//  Read the consequences in order, because the assignment is unconditional inside the `if`:
//
//      * A PREFIX MATCH ALWAYS ASSIGNS. It therefore OVERWRITES a column picked earlier by the
//        second arm. A late match beats an early non-match.
//      * A NON-MATCHING COLUMN ASSIGNS ONLY WHEN NOTHING IS SET YET. The first identity column
//        found is retained as a fallback, and a later non-matching one cannot displace it.
//      * Two prefix matches: THE LAST ONE WINS, because each assigns unconditionally.
//
//  So it is neither "first match wins" nor "last wins", and every one of those three orderings is
//  reachable. C-B forbids tidying it into one rule.
//
//  THE FIXTURE EXERCISES THE FALLBACK ARM, WHICH IS WHY THIS MATTERS IN PRACTICE RATHER THAN IN
//  THEORY. dw_sqlite.srd declares `update="COMPANY"` [:L14] so the prefix is "company.", and it
//  declares the identity column's database name as plain `dbname="id"` [:L8] - not "COMPANY.id".
//  Left("id", 8) is "id", which does not equal "company.", so the prefix arm FAILS on the sole
//  evidenced fixture and the column is taken by the fallback. The fallback is the measured path,
//  not the rare one.
//
//  ============================================================================================
//  DECISION 4 - COLLECTION IS CONDITIONAL AT THREE LEVELS, AND AN ABSENT RESULT IS NOT AN ERROR
//  ============================================================================================
//      LEVEL 1  rows must have been inserted at all                              [:L215]
//      LEVEL 2  an identity column must have been found                          [:L226]
//      LEVEL 3  at least one of the two arrays must be non-empty                 [:L242]
//
//  Failing any level emits NOTHING - not an empty payload, not a zero column id, not a fault. The
//  published contract states the same rule from the wire side: an absent identity block means
//  "none collected", never an error. <see cref="IdentityResolutionOutcome.Identity"/> is therefore
//  nullable, and null is a normal outcome.
//
//  THE COUNTS TRIPLE IS OUTSIDE ALL THREE LEVELS. [:L247] sits after the inserted-count block
//  closes at [:L246] and before the success return at [:L248], so it fires on EVERY success -
//  including an update that inserted nothing, found no identity column and collected no value. An
//  implementation that reports counts only alongside an identity payload loses the counts for every
//  pure update and every pure delete. That placement is reproduced exactly:
//  <see cref="IdentityResolutionOutcome.Counts"/> is NOT nullable.
//
//  ============================================================================================
//  DECISION 5 - WHY THE TYPES HERE ARE NOT NAMED FOR THE WIRE MESSAGES THEY FEED
//  ============================================================================================
//  The published boundary already declares `IdentityColumnData` (common.v1.proto Section 10) and
//  `UpdateCounts` (persistence.v1.proto), and protoc emits both into namespaces this project
//  imports. A second type of either name in PowerFramework.Persistence.Concurrency would make every
//  unqualified mention ambiguous - CS0104 - in `Grpc/UpdateService.cs` and in the test project,
//  whose GlobalUsings.cs makes both contract namespaces ambient. TreatWarningsAsErrors is inherited
//  as true from the repository-root Directory.Build.props, so that is a hard BUILD FAILURE rather
//  than a nuisance. The in-process shapes are therefore named
//  <see cref="ResolvedIdentityColumnData"/> and <see cref="UpdateRowCounts"/>, and the wire
//  messages are neither restated nor re-declared here. The sibling Buffers/ItemStatus.cs resolves
//  the identical problem the identical way for `ItemStatus`.
//
//  ============================================================================================
//  DECISION 6 - NULL NUMERIC VALUES ARE MODELLED, NOT COLLAPSED TO ZERO (C-B)
//  ============================================================================================
//  `GetItemNumber` answers null for a null item, and PowerScript stores that null into the `long`
//  array element - PowerBuilder value types carry null. The apply half then writes it through
//  `SetItem` [:L144] or the direct filter-buffer assignment [:L160], so a null propagates all the
//  way to the row.
//
//  A C# `long[]` cannot hold null and neither can protobuf `repeated int64`, so the collected
//  arrays here are `IReadOnlyList<long?>`: THE INFORMATION IS PRESERVED RATHER THAN GUESSED AT.
//  Collapsing null to zero would be the exact defect the plan's transformation rules forbid -
//  widening a contract with a guess - because zero is a legal identity value on a table whose
//  auto-increment seed is zero, so the two cases would become indistinguishable.
//
//  In practice a null cannot occur on the evidenced fixture: `id` is `key=yes identity=yes`
//  [dw_sqlite.srd:L8] over an INTEGER PRIMARY KEY, which the database always assigns. The
//  possibility is nonetheless modelled and made detectable through
//  <see cref="ResolvedIdentityColumnData.ContainsNullValue"/> so that the wire projection in
//  `Grpc/UpdateService.cs` can NARROW WITH A DEFINED ERROR rather than silently coerce. This file
//  deliberately performs no projection of its own: choosing a wire encoding for a null identity
//  value is the boundary's decision, not the collector's.
//
//  ============================================================================================
//  THE WRITE-BACK CONTRACT - RECORDED HERE, IMPLEMENTED IN Tasks/TaskProxies/ (scope boundary)
//  ============================================================================================
//  `of_refreshidentitydata` [n_cst_threading_task_sqlupdate.sru:L120-L205] is the APPLY half and
//  belongs to the caller-side proxy pair under `Tasks/TaskProxies/`. IT IS NOT IMPLEMENTED IN THIS
//  FILE. Its measured shape is recorded so the collection order produced here is consumed
//  correctly, and so that four quirks survive:
//
//    1. AN EMPTY COLLECTION RETURNS THE GENERIC FAILURE CODE, NOT SUCCESS.
//       `if nCount = 0 then return RetCode.FAILED` [:L128-L129]. Not E_DATA_NOT_FOUND, not OK.
//
//    2. THE LOOP BOUND IS READ FROM ENTRY [1] ONLY - A LATENT DEFECT TO PRESERVE, NOT FIX.
//       `n = UpperBound(_idColDatas[1].primaryValues)` [:L136, :L170] and the Filter equivalent
//       [:L151, :L185] measure the FIRST entry, while the inner loop indexes EVERY entry's array
//       with the same counter [:L143-L145, :L159-L161, :L177-L179, :L193-L195]. Multi-table update
//       appends one entry per table [:L71-L77], so when a later table's array is SHORTER than the
//       first's this indexes out of range. C-B forbids correcting it; the proxy agent reproduces it
//       and annotates it.
//
//    3. THE TRAVERSAL IS A GetNextModified WALK WITH A BAIL-OUT, NOT AN INDEX SCAN.
//       Seeded `GetNextModified(0, buffer)`, advanced `GetNextModified(nRow, buffer)`, terminated at
//       0, each visited row re-checked against the column-zero new-modified status [:L140, :L156,
//       :L174, :L190], with a running counter and `if i > n then exit` [:L142, :L158, :L176, :L192]
//       when more qualifying rows exist than collected values. The counter is EXPLICITLY reset
//       before the Filter loop [:L153, :L187] but relies on declaration-zero before the Primary
//       loop - an asymmetry, and reproducing it means not adding a reset the oracle does not have.
//
//    4. THE WRITE ACCESSORS ARE ASYMMETRIC, MIRRORING THE READ ACCESSORS HERE.
//       Primary applies through `SetItem(nRow, id, value)` [:L144, :L178]; Filter applies through
//       the direct filter-buffer data assignment `Object.Data.Filter[nRow, id] = value` [:L160,
//       :L194].
//
//  AND ONE DELIBERATE NON-PORT (C-D). The `DataWindow!` arm wraps the whole operation in
//  `dw.SetRedraw(false)` [:L134] / `dw.SetRedraw(true)` [:L166] while the structurally identical
//  `DataStore!` arm [:L167-L199] calls neither. Redraw suppression is presentational and belongs to
//  the deferred DesignSystem capability area, so the headless port follows the DataStore arm's
//  shape. Nothing in this file or in the proxy suppresses redraw, and that is a documented
//  non-port rather than an omission.
//
//  ============================================================================================
//  R9 - THE ONE-BASED AUDIT FOR THIS FILE, IN FULL
//  ============================================================================================
//  PowerBuilder arrays are ONE-BASED and `UpperBound` returns THE LAST VALID INDEX; C# collections
//  are zero-based and `Count` is ONE PAST THE END. Five sites in this file live in a one-based
//  domain, and every one is audited:
//
//      SITE                                    ORACLE      HOW IT IS HANDLED HERE
//      column scan, 1 to column count          [:L219]     OneBasedIndex.Range - the folder's
//                                                          centralized helper, int domain
//      row scan, 1 to RowCount(), ASCENDING    [:L229]     explicit long loop bounded below by
//                                                          ItemStatusMachine.FirstRowNumber
//      row scan, FilteredCount() to 1, DESCENDING [:L237]  explicit long loop bounded below by
//                                                          ItemStatusMachine.FirstRowNumber
//      append idiom x[UpperBound(x) + 1] = v   [:L231]     List.Add, whose landing index is
//                                              [:L239]     OneBasedIndex.AppendIndex by definition
//      emptiness test UpperBound(x) > 0        [:L242]     OneBasedIndex.UpperBound compared with
//                                                          OneBasedIndex.EmptyUpperBound
//
//  WHY THE ROW LOOPS DO NOT GO THROUGH OneBasedIndex.Range, WHICH WOULD LOOK TIDIER. That helper is
//  about ARRAY AND LIST INDICES and its own documentation says so; it is `int`-domain. ROW NUMBERS
//  ARE A DIFFERENT ONE-BASED DOMAIN - `long`, owned by ItemStatusMachine's FirstRowNumber - and
//  RowCount() and FilteredCount() both answer `long`. Routing a row number through an int-domain
//  helper would narrow it and introduce the very conversion this audit exists to prevent. The
//  sibling ItemStatusMachine.CountDeleteCountable makes the same choice for the same reason.
//
//  NOTHING HERE IS EVER REBASED. No `- 1` and no `+ 1` is applied to a row number, a column
//  ordinal or a column number anywhere in this file. Column ordinals pass through to the describe
//  vocabulary as they are, because that vocabulary is itself one-based - `#1.Identity` addresses the
//  first column - so a rebased ordinal would produce a syntactically valid describe that reads the
//  WRONG column.
//
//  ============================================================================================
//  WHAT IS DELIBERATELY NOT HERE
//  ============================================================================================
//  NO SQL, NO CONNECTION, NO STATEMENT TEXT (C-F). This file reads column metadata and numeric
//  values through injected surfaces. It builds no statement, logs no statement, and touches no
//  connection string, password or `logpass`. There is no key, credential, token or secret-shaped
//  placeholder anywhere in it, in any member, default, message or comment.
//
//  NO SCHEMA (C-E). No table name, no column name and no column count is named by the production
//  code below - the update table arrives from the describe surface at run time, exactly as the
//  oracle reads it at [:L188]/[:L217] rather than from a cached descriptor. COMPANY and its six
//  columns appear only in the sibling test file, as test data.
//
//  NO DIALOG, NO UI, NO GEOMETRY (C-D). The oracle's identity block raises no dialog, so there is
//  nothing to convert into a structured error here; the only presentational calls in the round trip
//  are the two redraw calls named above, and both are documented non-ports.
//
//  NO DATABASE AND NO DataWindow (C-H). Every branch below is reachable through the two injected
//  interfaces, which is what makes both iteration DIRECTIONS assertable with nothing running. Every
//  member is `internal` so the sibling test project reaches it through the
//  InternalsVisibleTo("PowerFramework.Persistence.Tests") item the application project declares.
//
//  NO CROSS-SERVICE COUPLING (C-A). The only outward shape is contract C-06's identity column and
//  its two value arrays, carried by common.v1.IdentityColumnData and projected in
//  `Grpc/UpdateService.cs`. This file references no peer service and no behavioural code crosses a
//  service boundary.
//
//  NO RETURN CODE, AND THEREFORE NO `RetCode` ALIAS. The name `RetCode` is declared TWICE in this
//  solution - PowerFramework.Shared.Kernel.RetCode is a static class of `long` constants carrying the
//  in-process algebra ported from ws_objects/pfw.shared.pbl.src/retcode.sru, while
//  PowerFramework.Contracts.Common.V1.RetCode is a generated protobuf wrapper whose nested enum is
//  the wire form, PascalCased by protoc so the two do not even spell their members alike. Importing
//  both unqualified is CS0104, which siblings resolve with a using-alias
//  (Buffers/DataWindowBuffers.cs and Concurrency/UpdateWhereBuilder.cs both do). THIS FILE NEEDS
//  NEITHER: the identity block of the oracle produces no return code at all - its outcomes are a
//  payload, an absence, and three counts - and the surrounding `return RetCode.OK` / `RetCode.E_DB_ERROR`
//  [:L248, :L250] belong to Tasks/SqlUpdateTask.cs, as does the write-back's `RetCode.FAILED`
//  [n_cst_threading_task_sqlupdate.sru:L129]. Neither namespace is imported here, so the collision
//  cannot arise; this is recorded because the absence of an alias is a deliberate finding rather than
//  an oversight.
//
//  NAMING (verified against the artifact). The repository-root .editorconfig scopes its CA1707 and
//  IDE1006 relaxations to the files on its BAND 3 roster - that roster is the single source of truth
//  for the list, so it is cited here rather than recounted - and THIS FOLDER IS NOT AMONG THEM, so
//  every member below is PascalCase. NO SCREAMING_SNAKE IDENTIFIER IS DECLARED IN THIS FILE - under
//  warnings-as-errors
//  one would break the build. The shouty spellings that appear do so only inside comments, quoting
//  the oracle and the .proto for traceability.
//
//  RULES POSITION. review_rules returns exactly one line, "No user rules provided.", so no
//  user-specified rule governs this file and none is invented in their place. The enterprise
//  baseline applies instead, and the binding constraints are the plan's own non-rule inventory;
//  those bearing on this file are cited inline where each is discharged: C-A, C-B, C-C, C-D, C-E,
//  C-F, C-H, C-K and risk R9.
// ==============================================================================================

using System.Collections.Immutable;
using System.Globalization;
using PowerFramework.Contracts.Common.V1;
using PowerFramework.Persistence.Buffers;

namespace PowerFramework.Persistence.Concurrency;

#region The resolved identity - the in-process mirror of `IdColData`

/// <summary>
/// One table's resolved identity round trip: the discovered identity column together with the values
/// the database assigned, kept as TWO SEPARATELY ORDERED ARRAYS because they come from two buffers
/// collected in two directions.
/// </summary>
/// <param name="IdentityColumnId">
/// The discovered identity column's ONE-BASED DataWindow column id - the ordinal the describe
/// vocabulary uses, NOT a zero-based array index. A real column is 1 or greater; zero never appears
/// here because the oracle only fires the callback when the ordinal is positive
/// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru:L226</c>].
/// </param>
/// <param name="PrimaryValues">
/// The identity values collected from the <c>Primary!</c> buffer, in FORWARD collection order
/// [<c>:L228-L233</c>]. Never <see langword="null"/>; empty is legal when the Filter array carried
/// the whole payload.
/// </param>
/// <param name="FilterValues">
/// The identity values collected from the <c>Filter!</c> buffer, in BACKWARD collection order
/// [<c>:L234-L241</c>]. Never <see langword="null"/>; empty is legal when the Primary array carried
/// the whole payload. THE ORDER IS THE ORACLE'S AND MUST BE CONSUMED AS COLLECTED - see DECISION 1
/// in the file header.
/// </param>
/// <remarks>
/// <para>
/// THE THREE MEMBERS ARE IN THE ORACLE'S OWN FIELD ORDER, and the record is declared positionally so
/// that the order is a syntactic property rather than a convention a later edit could reshuffle. The
/// legacy structure is
/// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlupdate.sru:L10-L14</c>:
/// </para>
/// <code>
/// type IdColData from structure
///     long        id
///     long        PrimaryValues[]
///     long        FilterValues[]
/// end type
/// </code>
/// <para>
/// It is also the order the published boundary serializes - <c>identity_column_id</c>,
/// <c>primary_values</c>, <c>filter_values</c> in <c>common.v1.proto</c> Section 10 - so this record
/// is the in-process shape that projection reads from. The projection itself lives in
/// <c>Grpc/UpdateService.cs</c>: this type carries no protobuf dependency and performs no encoding.
/// </para>
/// <para>
/// THE TWO ARRAYS ARE NEVER MERGED, RE-SORTED OR DEDUPLICATED. They are consumed by two DIFFERENT
/// buffers on the write-back side - Primary through the set-item accessor and Filter through the
/// direct filter-buffer assignment [<c>n_cst_threading_task_sqlupdate.sru:L144,L160</c>] - so a
/// merged array would be unusable and a re-sorted one would silently pair every Filter value with
/// the wrong row. The published contract states the same obligation for any service that relays the
/// payload.
/// </para>
/// <para>
/// ONE INSTANCE PER TABLE, ACCUMULATED BY THE PROXY - THE COLLECTION SHAPE THIS TYPE IMPLIES.
/// Multi-table update from one DataWindow is a real legacy capability: the descriptor is an ARRAY
/// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru:L30</c>, appended at
/// <c>:L86</c>], the prepare-and-update loop runs once per entry, and so the callback fires ONCE PER
/// TABLE. The caller side therefore accumulates these payloads into an ordered list, appending each
/// at <c>UpperBound + 1</c>
/// [<c>n_cst_threading_task_sqlupdate.sru:L45</c> declares <c>IDCOLDATA _idColDatas[]</c>, appended
/// at <c>:L71-L77</c>], and the list's ORDER is the table order. THAT ACCUMULATION IS
/// <c>Tasks/TaskProxies/</c>'s JOB, NOT THIS FILE'S - see THE WRITE-BACK CONTRACT in the file
/// header, whose quirk 2 is exactly a consequence of that list: the apply loop reads its bound from
/// entry <c>[1]</c> only while indexing every entry's arrays.
/// </para>
/// <para>
/// VALUE EQUALITY IS REFERENCE EQUALITY FOR THE TWO LISTS, which is worth stating because the
/// enclosing type is a record and a reader may expect otherwise. C# record equality calls
/// <see cref="object.Equals(object)"/> on each member, and
/// <see cref="IReadOnlyList{T}"/> implementations do not generally implement structural equality. A
/// test that compares payloads therefore compares the two sequences element by element, which is
/// what the sibling test file does; it is also the only comparison that can detect an ORDER defect,
/// since a structural comparison of unordered content could not.
/// </para>
/// <para>
/// WHY THE ELEMENT TYPE IS <c>long?</c> AND NOT <c>long</c>: see DECISION 6 in the file header. In
/// short, <c>GetItemNumber</c> answers null for a null item and PowerScript stores that null into
/// the array, so collapsing it to zero here would make a null indistinguishable from a legitimate
/// identity value of zero. <see cref="ContainsNullValue"/> makes the condition detectable so the
/// wire projection can narrow with a defined error instead of guessing.
/// </para>
/// </remarks>
internal sealed record ResolvedIdentityColumnData(
    long IdentityColumnId,
    IReadOnlyList<long?> PrimaryValues,
    IReadOnlyList<long?> FilterValues)
{
    /// <summary>
    /// Reports whether either collected array carries a null value, so that a consumer projecting to
    /// a wire representation that cannot express null can fail explicitly rather than coerce.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A null arises only when the identity column held a null item at collection time, which the
    /// oracle would have carried through to the row unchanged
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlupdate.sru:L144,L160</c>]. On
    /// the sole evidenced fixture it cannot happen: <c>id</c> is an
    /// <c>INTEGER PRIMARY KEY AUTOINCREMENT</c> marked <c>key=yes identity=yes</c>
    /// [<c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L8</c>], and the database always assigns it.
    /// </para>
    /// <para>
    /// This predicate deliberately does NOT decide what to do about it. Choosing an encoding, or
    /// choosing to fail, is the boundary's decision; the collector's job is to preserve the fact.
    /// </para>
    /// </remarks>
    internal bool ContainsNullValue =>
        PrimaryValues.Any(static value => value is null)
        || FilterValues.Any(static value => value is null);
}

#endregion

#region The counts triple - `OnUpdated`, which fires on every success

/// <summary>
/// The inserted, updated and deleted row counts one successful update produced, in the oracle's own
/// argument order.
/// </summary>
/// <param name="Inserted">
/// Rows inserted, from the carrier's inserted-count getter
/// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds.sru:L73</c>].
/// </param>
/// <param name="Updated">
/// Rows updated, from the carrier's updated-count getter [<c>:L76</c>].
/// </param>
/// <param name="Deleted">
/// Rows deleted, from the carrier's deleted-count getter [<c>:L79</c>].
/// </param>
/// <remarks>
/// <para>
/// Mirrors the three arguments of
/// <c>tasking.Event OnUpdated(Data.of_GetInsertedCount(), Data.of_GetUpdatedCount(),
/// Data.of_GetDeletedCount())</c>
/// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru:L247</c>], declared as
/// <c>event onupdated(long inserted, long updated, long deleted)</c>
/// [<c>n_cst_threading_task_sqlupdate.sru:L18</c>].
/// </para>
/// <para>
/// C-B - THIS TRIPLE IS PER INVOCATION AND IS NEVER ACCUMULATED HERE. The caller-side handler
/// accumulates across firings with <c>+=</c>
/// [<c>n_cst_threading_task_sqlupdate.sru:L66-L68</c>], so a multi-table update SUMS its per-table
/// counts on the CALLER side. Accumulating in this file would double-count them. The running totals
/// are read back through the caller's three getters [<c>:L214-L221</c>] and reset by
/// <c>of_reset</c> [<c>:L90-L92</c>]; both belong to <c>Tasks/TaskProxies/</c>.
/// </para>
/// <para>
/// Named for the domain rather than for the wire message it feeds: the published
/// <c>persistence.v1.UpdateCounts</c> carries the same three fields, and declaring a second type of
/// that name in a namespace this project imports alongside the generated one would be CS0104 under
/// warnings-as-errors. See DECISION 5 in the file header.
/// </para>
/// <para>
/// A <see langword="readonly record struct"/> because it is three numbers with no identity of their
/// own, so a heap allocation per successful update would buy nothing.
/// </para>
/// </remarks>
internal readonly record struct UpdateRowCounts(long Inserted, long Updated, long Deleted);

#endregion

#region The outcome - the ORDERED identity blocks plus one mandatory counts triple

/// <summary>
/// Everything the success arm of the legacy update reports: the identity blocks it collected, in
/// order, and the counts triple ALWAYS.
/// </summary>
/// <remarks>
/// <para>
/// The shape mirrors the two callbacks the oracle fires inside
/// <c>if rtCode = 1 then</c>
/// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru:L214-L248</c>], and their
/// NESTING is the whole point of the type: one is three levels deep inside conditionals and the
/// other is not nested at all.
/// </para>
/// <code>
/// if rtCode = 1 then                                                  [:L214]
///     if Data.of_GetInsertedCount() &gt; 0 then                       [:L215]
///         ... discover ...                                            [:L217-L225]
///         if nIdentityColumn &gt; 0 then                               [:L226]
///             ... collect Primary forward, Filter backward ...        [:L228-L241]
///             if UpperBound(primary) &gt; 0 or UpperBound(filter) &gt; 0 then   [:L242]
///                 OnIdentityColumnDataRetrieved(...)                  [:L243]
///             end if
///         end if
///     end if
///     OnUpdated(inserted, updated, deleted)                           [:L247]
///     return RetCode.OK                                               [:L248]
/// else
///     return RetCode.E_DB_ERROR                                       [:L250]
/// end if
/// </code>
/// <para>
/// THE FAILURE ARM IS NOT MODELLED HERE. <c>rtCode &lt;&gt; 1</c> returns a database error at
/// [<c>:L249-L250</c>] without firing either callback, and that arm - together with the defensive
/// rewrite at [<c>:L208-L210</c>] that turns a claimed success into a failure when the transaction's
/// own driver code disagrees - belongs to <c>Tasks/SqlUpdateTask.cs</c>. An instance of this type
/// therefore only ever exists on the success path, which is why <see cref="Counts"/> is not nullable.
/// </para>
/// <para>
/// <b>THE IDENTITY SIDE IS A LIST, NOT AN OPTIONAL SINGLETON, AND THE CARDINALITY IS THE LEGACY'S.</b>
/// The code block above is ONE call of <c>_of_Update</c>, and one call fires the identity callback at
/// most once - but the task that drives it calls it ONCE PER UPDATE TABLE:
/// </para>
/// <code>
/// if _bMultiTableUpdate then
///     nCount = UpperBound(Tables)                                     [:L358]
///     for nIndex = 1 to nCount                                        [:L364]
///         rtCode = _of_UpdatePrepare(data,nIndex)                      [:L365]
///         rtCode = _of_Update(data)                                   [:L367]
///     next
/// else
///     rtCode = _of_Update(data)                                       [:L371]
/// end if
/// </code>
/// <para>
/// and the caller-side proxy APPENDS every firing to an ordered array rather than replacing anything -
/// <c>IDCOLDATA _idColDatas[]</c> [<c>n_cst_threading_task_sqlupdate.sru:L45</c>],
/// <c>nIndex = UpperBound(_idColDatas) + 1</c> [<c>:L73-L76</c>] - and then REPLAYS EVERY ELEMENT when
/// it writes the generated values back onto the caller's DataWindow
/// [<c>:L128</c>, <c>:L142-L195</c>]. Each element carries its OWN column ordinal, because
/// <c>_of_UpdatePrepare</c> re-describes the carrier per table [<c>:L98-L145</c>] so the discovered
/// column legitimately differs between them. Modelling this side as one optional payload would silently
/// keep the first table's identities and DROP THE REST - data loss that produces a completely plausible
/// result, since no count reported here would disagree with it.
/// </para>
/// </remarks>
internal sealed record IdentityResolutionOutcome
{
    /// <summary>
    /// The identity blocks collected, one per update table that collected any, IN TABLE ORDER. EMPTY
    /// when the oracle would have fired NOTHING.
    /// </summary>
    /// <remarks>
    /// <para>
    /// C-B - EMPTY IS A NORMAL OUTCOME AND NOT AN ERROR, and it arises from any of the three
    /// conditional levels failing: no rows were inserted [<c>:L215</c>], no identity column was found
    /// [<c>:L226</c>], or both collected arrays came back empty [<c>:L242</c>]. The published
    /// contract records the same reading from the wire side, where an empty repeated identity field
    /// means "none collected".
    /// </para>
    /// <para>
    /// AN EMPTY BLOCK IS NEVER PRODUCED IN PLACE OF NO BLOCK. The oracle's emit guard is a guard on
    /// the CALL, not on the contents, so a block whose two arrays are both empty has no legacy
    /// counterpart and must not be synthesised. That distinction survives here because the absence is
    /// expressed by the LIST being shorter, never by an element with nothing in it.
    /// </para>
    /// <para>
    /// ORDER IS CONTRACT AT THIS LEVEL AS WELL AS INSIDE EACH BLOCK. The elements are ordered by the
    /// table order the update was prepared with, because that is the order the oracle accumulated them
    /// in and the order it replays them in. <see cref="ImmutableArray{T}"/> rather than a mutable list
    /// so that an accumulated outcome cannot be re-ordered after the fact by a consumer - which is the
    /// one mutation that would pair identity values with the wrong column while leaving every count
    /// intact.
    /// </para>
    /// <para>
    /// A single-table update - which is every update the sole evidenced fixture performs
    /// [<c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L14</c>, one <c>update="COMPANY"</c>] - yields
    /// exactly one element when it collected anything, so the common case is unchanged in substance.
    /// </para>
    /// </remarks>
    internal ImmutableArray<ResolvedIdentityColumnData> Identity { get; init; } = [];

    /// <summary>
    /// The counts triple, ALWAYS present on a successful update.
    /// </summary>
    /// <remarks>
    /// C-B - THIS FIRES EVEN WHEN NO IDENTITY WORK HAPPENED. [<c>:L247</c>] sits outside the
    /// inserted-count block that closes at [<c>:L246</c>] and inside the success arm, so a pure
    /// update, a pure delete, and an insert into a table with no identity column all still report
    /// their counts. Reporting counts only alongside <see cref="Identity"/> would lose them for
    /// every one of those cases.
    /// </remarks>
    internal UpdateRowCounts Counts { get; init; }
}

#endregion

#region The per-table surfaces one multi-table update is driven from

/// <summary>
/// One update table's pair of injected surfaces, as the multi-table loop sees the carrier after that
/// table's prepare step has run.
/// </summary>
/// <param name="Metadata">The describe surface for this table's pass.</param>
/// <param name="Values">The row surface for this table's pass.</param>
/// <remarks>
/// <para>
/// WHY THIS IS A PAIR PER TABLE AND NOT ONE PAIR FOR THE WHOLE UPDATE, even though the legacy has a
/// single <c>Data</c> carrier throughout. <c>_of_UpdatePrepare</c> RE-DESCRIBES that one carrier before
/// each table's update: it resets update, key and identity to off on every column by ordinal
/// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru:L103-L108</c>] and then
/// re-enables only the ones this table declares [<c>:L110-L145</c>]. So the ANSWERS the describe
/// surface gives change from one iteration to the next, and the discovered identity column legitimately
/// differs per table. A single surface pair could not express that, and a test could not drive it.
/// </para>
/// <para>
/// A production adapter over <see cref="DataWindowCarrier"/> implements both interfaces on one object
/// and supplies the same object for both members, exactly as the legacy passes one <c>Data</c> - which
/// is why the two members are permitted to be the same instance.
/// </para>
/// </remarks>
internal readonly record struct IdentityTableSurfaces(
    IIdentityColumnMetadata Metadata,
    IIdentityValueSource Values);

#endregion

#region The injected surfaces - describe on one side, counts, statuses and values on the other

/// <summary>
/// The read half of the legacy carrier's inherited <c>Describe</c> surface that identity discovery
/// needs: the update table, the column count, and the two per-column properties the scan reads.
/// </summary>
/// <remarks>
/// <para>
/// WHY THIS INTERFACE EXISTS. In the oracle these are <c>Describe</c> calls on the
/// <c>n_cst_thread_task_sqlbase_ds</c> parameter, which derives from <c>datastore</c>
/// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds.sru:L8</c>] and so INHERITS
/// them - which is why they appear in no prototype list and why nothing in this project's
/// <c>Buffers/</c> folder exposes them. Naming the four reads as an interface is what makes
/// <see cref="IdentityColumnResolver.DiscoverIdentityColumn"/> a pure function whose every branch is
/// reachable with no DataWindow and no database (C-H).
/// </para>
/// <para>
/// WHY IT IS NOT <see cref="IUpdateTargetMetadata"/>, THE SIBLING DESCRIBE SURFACE IN THIS FOLDER.
/// That interface exposes exactly the three properties runtime re-derivation needs and deliberately
/// offers NO general <c>Describe(string)</c> escape hatch, so it cannot answer
/// <c>DataWindow.Table.UpdateTable</c>, <c>#n.Identity</c> or <c>#n.DBName</c>. Widening it would
/// hand its own consumers reads they have no oracle for. The two interfaces are therefore siblings
/// rather than one general one, they share the <see cref="GetColumnCount"/> member by coincidence of
/// both needing it, AND ONE CLASS MAY IMPLEMENT BOTH - which is what the legacy actually is, a single
/// <c>Data</c> object.
/// </para>
/// <para>
/// THE TWO PER-COLUMN MEMBERS RETURN THE RAW DESCRIBE STRING, UNCOERCED, because both comparisons in
/// the oracle are string comparisons: <c>= "yes"</c> [<c>:L220</c>] and a lower-cased prefix equality
/// [<c>:L221</c>]. Coercing the identity property to a <see cref="bool"/> would have to invent a
/// mapping for the <c>"!"</c> and <c>"?"</c> that describe answers when a property cannot be read,
/// and whichever way it mapped them it would change which column is chosen.
/// </para>
/// </remarks>
internal interface IIdentityColumnMetadata
{
    /// <summary>
    /// Reproduces <c>Describe("DataWindow.Table.UpdateTable")</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru:L188</c>], whose result
    /// the identity prefix is built from at [<c>:L217</c>].
    /// </summary>
    /// <returns>
    /// The update table name exactly as describe answers it, uncoerced and un-lower-cased.
    /// </returns>
    /// <remarks>
    /// READ AT RUN TIME, NOT TAKEN FROM A CACHED DESCRIPTOR. The oracle reads the value the
    /// modification script wrote [<c>:L143</c>], which is what makes the discovered column
    /// independent of the identity column NAMED in the request's table contract - a distinction the
    /// published contract calls out explicitly. The empty, <c>"!"</c> and <c>"?"</c> answers are
    /// rejected earlier, at [<c>:L189</c>], by <c>Tasks/SqlUpdateTask.cs</c>; this surface does not
    /// re-check them.
    /// </remarks>
    string DescribeUpdateTable();

    /// <summary>
    /// Reproduces <c>Long(Describe("DataWindow.Column.Count"))</c> [<c>:L218</c>].
    /// </summary>
    /// <returns>
    /// The number of columns, which is also their LAST VALID ONE-BASED ORDINAL. Zero when the count
    /// cannot be read, in which case the scan visits nothing - matching <c>for nIndex = 1 to 0</c>.
    /// </returns>
    /// <remarks>
    /// IMPLEMENTORS MUST REPRODUCE POWERSCRIPT'S <c>Long()</c> COERCION: the oracle wraps the read in
    /// <c>Long(...)</c>, which answers zero for text that is not a number - including <c>"!"</c> and
    /// <c>"?"</c>. <see cref="UpdateWhereBuilder.CoerceDescribedNumber"/> is that coercion, published
    /// by the sibling so an implementor does not have to re-derive it.
    /// </remarks>
    int GetColumnCount();

    /// <summary>
    /// Reproduces <c>Describe("#" + String(nIndex) + ".Identity")</c> [<c>:L220</c>], RAW.
    /// </summary>
    /// <param name="identityProperty">
    /// The describe property, ALREADY COMPOSED by the caller - for the third column this receives
    /// <c>#3.Identity</c>. Never <see langword="null"/>.
    /// </param>
    /// <returns>
    /// The property's text value exactly as describe answers it, typically <c>"yes"</c> or
    /// <c>"no"</c>.
    /// </returns>
    /// <remarks>
    /// THE CALLER COMPOSES THE PROPERTY, NOT THE IMPLEMENTOR, so the exact describe string the oracle
    /// builds stays visible at the call site and diffable against its locator. An implementor
    /// receives a complete property name and must not compose one of its own.
    /// </remarks>
    string DescribeColumnIdentity(string identityProperty);

    /// <summary>
    /// Reproduces <c>Describe("#" + String(nIndex) + ".DBName")</c> [<c>:L221</c>], RAW.
    /// </summary>
    /// <param name="dbNameProperty">
    /// The describe property, ALREADY COMPOSED by the caller - for the third column this receives
    /// <c>#3.DBName</c>. Never <see langword="null"/>.
    /// </param>
    /// <returns>
    /// The column's database name exactly as describe answers it, in whatever letter case the
    /// DataWindow definition used and with or without a table qualifier. The caller lower-cases and
    /// truncates it; an implementor must not.
    /// </returns>
    /// <remarks>
    /// The sole evidenced fixture answers a plain <c>id</c> here, unqualified
    /// [<c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L8</c>], which is exactly why the prefix arm
    /// fails on it and the fallback arm is the measured path. See DECISION 3 in the file header.
    /// </remarks>
    string DescribeColumnDbName(string dbNameProperty);
}

/// <summary>
/// The read half of the legacy carrier's row surface that identity collection needs: the three
/// update counts, the two buffer counts, the row status, and the numeric value accessor IN BOTH OF
/// ITS ARITIES.
/// </summary>
/// <remarks>
/// <para>
/// Every member maps one-for-one onto a call the oracle makes on its <c>Data</c> parameter inside
/// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru:L214-L247</c>], and the
/// surface is deliberately no wider than that. The concrete implementation over
/// <see cref="DataWindowCarrier"/> is trivial - every member has a direct counterpart on
/// <see cref="DataWindowBufferStore"/> or on the carrier itself - and it belongs to the
/// <c>Tasks/</c> layer that owns the carrier, not here (C-H: this file must be exercisable with no
/// carrier at all).
/// </para>
/// <para>
/// THE NUMERIC ACCESSOR IS TWO OVERLOADS, AND THAT IS THE POINT. See DECISION 2 in the file header:
/// the Primary loop issues the TWO-ARGUMENT form [<c>:L231</c>] and the Filter loop issues the
/// FOUR-ARGUMENT form [<c>:L239</c>]. Two overloads rather than one method with defaulted arguments,
/// because C# optional arguments are erased at the call site into a single four-argument call, which
/// would make the two forms indistinguishable to a recording fake and would destroy the parity suite's
/// ability to prove which one the port issues.
/// </para>
/// <para>
/// AN IMPLEMENTOR MUST NOT MAKE THE TWO OVERLOADS DISAGREE. The two-argument form means exactly
/// "the <see cref="DwBuffer.Primary"/> buffer's CURRENT value", so implementing it as
/// <c>GetItemNumber(row, columnNumber, DwBuffer.Primary, originalValue: false)</c> is correct and is
/// what the PowerBuilder defaulting rules do. What it must NOT do is read a different buffer, read an
/// original value, or cache.
/// </para>
/// </remarks>
internal interface IIdentityValueSource
{
    /// <summary>
    /// Reproduces <c>Data.of_GetInsertedCount()</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds.sru:L73</c>], read at
    /// [<c>n_cst_thread_task_sqlupdate.sru:L215</c>] as the precondition and again at [<c>:L247</c>]
    /// as the first count.
    /// </summary>
    /// <returns>The number of rows inserted by the update that just succeeded.</returns>
    /// <remarks>
    /// C-B - READ TWICE, NOT CACHED. The oracle calls it once for the precondition and once for the
    /// counts triple, and this port does the same, because a carrier is free to answer differently
    /// between the two calls and flattening them would change what the second one reports.
    /// </remarks>
    long GetInsertedCount();

    /// <summary>
    /// Reproduces <c>Data.of_GetUpdatedCount()</c> [<c>n_cst_thread_task_sqlbase_ds.sru:L76</c>],
    /// read at [<c>n_cst_thread_task_sqlupdate.sru:L247</c>].
    /// </summary>
    /// <returns>The number of rows updated by the update that just succeeded.</returns>
    long GetUpdatedCount();

    /// <summary>
    /// Reproduces <c>Data.of_GetDeletedCount()</c> [<c>n_cst_thread_task_sqlbase_ds.sru:L79</c>],
    /// read at [<c>n_cst_thread_task_sqlupdate.sru:L247</c>].
    /// </summary>
    /// <returns>The number of rows deleted by the update that just succeeded.</returns>
    long GetDeletedCount();

    /// <summary>
    /// Reproduces <c>Data.RowCount()</c> [<c>n_cst_thread_task_sqlupdate.sru:L228</c>]: the upper
    /// bound of the ASCENDING Primary-buffer scan.
    /// </summary>
    /// <returns>
    /// The number of rows in the <see cref="DwBuffer.Primary"/> buffer, which is also their last valid
    /// ONE-BASED row number. Zero is legal and makes the scan visit nothing.
    /// </returns>
    long RowCount();

    /// <summary>
    /// Reproduces <c>Data.FilteredCount()</c> [<c>n_cst_thread_task_sqlupdate.sru:L236</c>]: the
    /// STARTING point of the DESCENDING Filter-buffer scan.
    /// </summary>
    /// <returns>
    /// The number of rows in the <see cref="DwBuffer.Filter"/> buffer, which is also their last valid
    /// ONE-BASED row number. Zero is legal and makes the scan visit nothing.
    /// </returns>
    /// <remarks>
    /// C-B - THIS IS THE FILTERED COUNT, NOT THE ROW COUNT. The oracle reassigns its loop variable
    /// from <c>RowCount()</c> to <c>FilteredCount()</c> between the two scans [<c>:L228</c> then
    /// [<c>:L236</c>], and the two buffers are counted independently: a heavily filtered result has a
    /// large filtered count and may have a small row count, or the reverse.
    /// </remarks>
    long FilteredCount();

    /// <summary>
    /// Reproduces <c>Data.GetItemStatus(row, columnIndex, buffer)</c> as the oracle calls it at
    /// [<c>n_cst_thread_task_sqlupdate.sru:L230</c>] and [<c>:L238</c>].
    /// </summary>
    /// <param name="row">The ONE-BASED row number within <paramref name="buffer"/>.</param>
    /// <param name="columnIndex">
    /// The column index in the legacy's own domain: <see cref="ItemStatusMachine.RowStatusColumn"/>
    /// for THE ROW'S OWN status, or a one-based column number for a single column's status.
    /// </param>
    /// <param name="buffer">The buffer to read from.</param>
    /// <returns>That row's or column's item status.</returns>
    /// <remarks>
    /// <para>
    /// THE THREE-ARGUMENT SHAPE IS PRESERVED, INCLUDING THE COLUMN INDEX, even though both call sites
    /// in this file pass the row-status sentinel. Narrowing the member to
    /// <c>GetRowStatus(row, buffer)</c> would hide the distinction the oracle actually makes: the same
    /// object reads a genuine per-column status a few lines away with a real one-based column number
    /// [<c>:L162</c>], and the ONLY thing separating the two readings is whether the index is zero.
    /// </para>
    /// <para>
    /// NO CALLER IN THIS FILE PASSES A BARE <c>0</c>. Both pass
    /// <see cref="ItemStatusMachine.RowStatusColumn"/>, which the sibling <c>Buffers/ItemStatus.cs</c>
    /// declares precisely so that the sentinel is never spelled as a literal - and the sibling test
    /// file asserts that, through a fake that records the index it was handed.
    /// </para>
    /// </remarks>
    ItemStatus GetItemStatus(long row, int columnIndex, DwBuffer buffer);

    /// <summary>
    /// The TWO-ARGUMENT numeric accessor: reproduces <c>Data.GetItemNumber(row, columnNumber)</c>
    /// [<c>n_cst_thread_task_sqlupdate.sru:L231</c>] exactly, with NO buffer argument and NO
    /// original-value argument.
    /// </summary>
    /// <param name="row">The ONE-BASED row number in the <see cref="DwBuffer.Primary"/> buffer.</param>
    /// <param name="columnNumber">
    /// The ONE-BASED column number. R9: never rebased - this is the discovered identity column's
    /// ordinal as the describe vocabulary numbers it.
    /// </param>
    /// <returns>
    /// The column's current numeric value, or <see langword="null"/> when the item is null. See
    /// DECISION 6 in the file header for why null is preserved rather than coerced to zero.
    /// </returns>
    /// <remarks>
    /// PowerBuilder defaults the two omitted arguments to the Primary buffer and the current (not
    /// original) value, which is why this overload names neither. It is used by the Primary loop ONLY.
    /// </remarks>
    long? GetItemNumber(long row, int columnNumber);

    /// <summary>
    /// The FOUR-ARGUMENT numeric accessor: reproduces
    /// <c>Data.GetItemNumber(row, columnNumber, Filter!, false)</c>
    /// [<c>n_cst_thread_task_sqlupdate.sru:L239</c>] exactly, naming the buffer and the
    /// original-value flag explicitly.
    /// </summary>
    /// <param name="row">The ONE-BASED row number within <paramref name="buffer"/>.</param>
    /// <param name="columnNumber">The ONE-BASED column number. R9: never rebased.</param>
    /// <param name="buffer">
    /// The buffer to read from. The measured call site passes <see cref="DwBuffer.Filter"/>.
    /// </param>
    /// <param name="originalValue">
    /// <see langword="false"/> to read the CURRENT value, <see langword="true"/> to read the value the
    /// column held at the last baseline. The measured call site passes <see langword="false"/>.
    /// </param>
    /// <returns>
    /// The requested numeric value, or <see langword="null"/> when the item is null.
    /// </returns>
    /// <remarks>
    /// C-B - THE MEASURED CALL PASSES <see langword="false"/>, AND THAT IS NOT REDUNDANT. The
    /// optimistic-concurrency where clause reads ORIGINAL values because <c>updatewhere=1</c> compares
    /// them [<c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L8-L14</c>], whereas this read wants the
    /// value the database JUST ASSIGNED, which is by definition the current one. Passing
    /// <see langword="true"/> here would return the pre-insert value - typically null - and the round
    /// trip would write nulls back over the rows it was meant to fill in.
    /// </remarks>
    long? GetItemNumber(long row, int columnNumber, DwBuffer buffer, bool originalValue);
}

#endregion

#region The resolver - discovery, the two collections, the emit guard and the counts

/// <summary>
/// The collect half of the identity round trip: discovers the identity column at run time, collects
/// the values the database assigned from the Primary buffer FORWARD and the Filter buffer BACKWARD,
/// applies the oracle's emit guard, and reports the counts triple.
/// </summary>
/// <remarks>
/// <para>
/// Ports
/// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru:L214-L248</c> - the identity
/// block of <c>_of_update</c> - and nothing else. The write-back half is documented in this file's
/// header and implemented under <c>Tasks/TaskProxies/</c>.
/// </para>
/// <para>
/// EVERY MEMBER IS A PURE FUNCTION OF ITS ARGUMENTS AND THE TWO INJECTED SURFACES. There is no static
/// mutable state, no clock read, no I/O and no ordering dependency between calls, which is what makes
/// both iteration DIRECTIONS assertable with no database and no DataWindow (C-H). The stages are
/// exposed individually as well as through <see cref="Resolve"/> so that a table-driven theory can
/// reach one branch at a time.
/// </para>
/// </remarks>
internal static class IdentityColumnResolver
{
    #region The vocabulary - every literal the oracle reads, named once

    /// <summary>
    /// The value the oracle's identity-column variable holds before any column has been chosen, and
    /// the value that means "no identity column was found".
    /// </summary>
    /// <remarks>
    /// <para>
    /// The oracle declares <c>long nIdentityColumn</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru:L174</c>], which
    /// PowerScript zero-initialises, and it tests that zero TWICE for two different purposes: as the
    /// fallback arm's condition <c>or nIdentityColumn = 0</c> [<c>:L221</c>], and as the gate on
    /// collection <c>if nIdentityColumn &gt; 0 then</c> [<c>:L226</c>].
    /// </para>
    /// <para>
    /// R9: THIS IS NOT AN INDEX AND MUST NEVER BE REBASED. Column ordinals are one-based, so zero is
    /// outside their domain by design - which is exactly what makes it usable as "nothing chosen". It
    /// is deliberately the same value as <see cref="ItemStatusMachine.RowStatusColumn"/> without being
    /// related to it: that zero means "the row itself" and is an ARGUMENT, this one means "no column"
    /// and is a RESULT.
    /// </para>
    /// </remarks>
    internal const int NoIdentityColumn = 0;

    /// <summary>
    /// The describe attribute suffix that answers a column's database name, <c>.DBName</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru:L221</c>].
    /// </summary>
    /// <remarks>
    /// Declared here rather than on <see cref="UpdateWhereBuilder"/> because no other file in this
    /// service reads it: the sibling writes <c>.Update</c>, <c>.Key</c> and <c>.Identity</c> and reads
    /// <c>.Id</c>, and identity discovery is the only measured reader of <c>.DBName</c> anywhere in
    /// the in-scope estate. THE LETTER CASE IS THE ORACLE'S - a capital B and a capital N - and the
    /// describe vocabulary is case-insensitive in PowerBuilder, so the spelling is preserved for
    /// diffability rather than for correctness.
    /// </remarks>
    internal const string DbNameAttributeSuffix = ".DBName";

    /// <summary>
    /// The separator the oracle appends to the lower-cased update table name to form the
    /// database-name prefix, <c>"."</c> [<c>:L217</c>].
    /// </summary>
    internal const string TableQualifierSeparator = ".";

    #endregion

    #region Describe property composition - the caller composes, the implementor does not

    /// <summary>
    /// Composes the identity describe property for a column ordinal:
    /// <c>"#" + String(ordinal) + ".Identity"</c> [<c>:L220</c>].
    /// </summary>
    /// <param name="columnOrdinal">The ONE-BASED column ordinal. R9: never rebased.</param>
    /// <returns>The complete describe property, for example <c>#3.Identity</c>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="columnOrdinal"/> is below <see cref="ItemStatusMachine.FirstColumnNumber"/>.
    /// </exception>
    /// <remarks>
    /// The two literals are taken from <see cref="UpdateWhereBuilder"/> rather than restated, because
    /// the sibling already publishes them and two declarations of one literal can drift apart with no
    /// compile-time edge between them. Formatted with the invariant culture: a describe property is a
    /// machine string, so no culture-specific digit shape may enter it.
    /// </remarks>
    internal static string DescribeIdentityProperty(int columnOrdinal)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(
            columnOrdinal,
            ItemStatusMachine.FirstColumnNumber);

        return UpdateWhereBuilder.ColumnOrdinalPrefix
            + columnOrdinal.ToString(CultureInfo.InvariantCulture)
            + UpdateWhereBuilder.IdentityAttributeSuffix;
    }

    /// <summary>
    /// Composes the database-name describe property for a column ordinal:
    /// <c>"#" + String(ordinal) + ".DBName"</c> [<c>:L221</c>].
    /// </summary>
    /// <param name="columnOrdinal">The ONE-BASED column ordinal. R9: never rebased.</param>
    /// <returns>The complete describe property, for example <c>#3.DBName</c>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="columnOrdinal"/> is below <see cref="ItemStatusMachine.FirstColumnNumber"/>.
    /// </exception>
    internal static string DescribeDbNameProperty(int columnOrdinal)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(
            columnOrdinal,
            ItemStatusMachine.FirstColumnNumber);

        return UpdateWhereBuilder.ColumnOrdinalPrefix
            + columnOrdinal.ToString(CultureInfo.InvariantCulture)
            + DbNameAttributeSuffix;
    }

    #endregion

    #region The prefix and the PowerScript Left() accommodation

    /// <summary>
    /// Builds the column database-name prefix the discovery scan matches against:
    /// <c>Lower(updateTable) + "."</c> [<c>:L217</c>].
    /// </summary>
    /// <param name="updateTable">
    /// The update table name as <see cref="IIdentityColumnMetadata.DescribeUpdateTable"/> answered it,
    /// in whatever letter case the modification script wrote.
    /// </param>
    /// <returns>
    /// The lower-cased table name followed by a dot. NEVER empty: an empty table name still yields
    /// <c>"."</c>, whose length of one is what the truncation below then uses.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="updateTable"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <see cref="string.ToLowerInvariant"/> reproduces PowerScript's <c>Lower</c>. Invariant rather
    /// than current-culture deliberately: a database identifier is machine text, and the
    /// current-culture form would fold the Turkish dotted capital I to a dotless lower-case i and so
    /// change which column matched depending on the container's locale. That would make the discovered
    /// identity column locale-dependent, which no legacy behaviour justifies.
    /// </para>
    /// <para>
    /// AN EMPTY TABLE NAME CANNOT REACH THIS FUNCTION THROUGH THE LEGACY PATH, because
    /// <c>Tasks/SqlUpdateTask.cs</c> rejects the empty, <c>"!"</c> and <c>"?"</c> answers at
    /// [<c>:L189</c>] before the identity block runs. It is handled anyway - yielding a bare
    /// separator - because this member is independently callable and a guard that throws would invent
    /// a failure mode the oracle does not have.
    /// </para>
    /// </remarks>
    internal static string BuildColumnDbNamePrefix(string updateTable)
    {
        ArgumentNullException.ThrowIfNull(updateTable);

        return updateTable.ToLowerInvariant() + TableQualifierSeparator;
    }

    /// <summary>
    /// Reproduces PowerScript <c>Left(text, length)</c>: the leading <paramref name="length"/>
    /// characters, or THE WHOLE STRING when it is shorter than that.
    /// </summary>
    /// <param name="text">
    /// The text to truncate, as a describe call answered it. <see langword="null"/> is treated as
    /// empty - see the remarks.
    /// </param>
    /// <param name="length">
    /// The number of characters to take. Zero or negative yields <see cref="string.Empty"/>, matching
    /// PowerScript.
    /// </param>
    /// <returns>The truncated text.</returns>
    /// <remarks>
    /// <para>
    /// R9-ADJACENT TRANSLATION ACCOMMODATION, NOT A BEHAVIOUR CHANGE.
    /// <see cref="string.Substring(int, int)"/> THROWS when the requested length exceeds the string,
    /// whereas PowerScript's <c>Left</c> simply answers the whole string. The oracle relies on that
    /// silence: the prefix is the table name plus a dot, so on the sole evidenced fixture
    /// <c>Left("id", 8)</c> is asked for eight characters of a two-character database name
    /// [<c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L8</c>, <c>:L14</c>]. A direct
    /// <c>Substring</c> port would throw on the fixture's own happy path. The guard therefore restores
    /// the oracle's behaviour rather than adding leniency to it.
    /// </para>
    /// <para>
    /// NULL IS TREATED AS EMPTY, AND HERE IS WHAT THE ORACLE WOULD HAVE DONE. Describe never answers
    /// null in practice - it answers <c>"!"</c> or <c>"?"</c> when a property cannot be read - so this
    /// arm is unreachable through the legacy path. Were it reachable, PowerScript's null propagation
    /// would make the comparison at [<c>:L221</c>] null, and an <c>if</c> over a null condition takes
    /// the false branch, so the prefix arm would fail. Mapping null to empty produces the same
    /// observable outcome for the prefix arm - empty never equals a prefix that always ends in a dot -
    /// while keeping the fallback arm reachable, which is the reading that cannot silently lose a
    /// column. It is recorded here rather than left implicit because it is the one place in this file
    /// where PowerScript's three-valued behaviour is approximated rather than reproduced.
    /// </para>
    /// </remarks>
    internal static string LegacyLeft(string? text, int length)
    {
        if (length <= 0 || string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        // PowerScript Left answers the WHOLE string when it is shorter than the requested length.
        // Substring would throw here, which is why this branch exists.
        return length >= text.Length ? text : text[..length];
    }

    #endregion

    #region Discovery - first wins, with a prefix override

    /// <summary>
    /// Discovers the identity column at run time by scanning every column's describe properties, with
    /// the oracle's first-wins-plus-prefix-override selection ordering preserved exactly.
    /// </summary>
    /// <param name="metadata">The describe surface.</param>
    /// <returns>
    /// The chosen column's ONE-BASED ordinal, or <see cref="NoIdentityColumn"/> when no column is
    /// marked as an identity column at all.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="metadata"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Ports [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru:L217-L225</c>]:
    /// </para>
    /// <code>
    /// sColDBNamePrefix = Lower(sUpdateTable) + "."                                       [:L217]
    /// nCount = Long(Data.Describe("DataWindow.Column.Count"))                            [:L218]
    /// for nIndex = 1 to nCount                                                           [:L219]
    ///     if Data.Describe("#" + String(nIndex) + ".Identity") = "yes" then               [:L220]
    ///         if Lower(Left(Data.Describe("#" + String(nIndex) + ".DBName"),
    ///                       Len(sColDBNamePrefix))) = sColDBNamePrefix
    ///            or nIdentityColumn = 0 then                                             [:L221]
    ///             nIdentityColumn = nIndex                                               [:L222]
    ///         end if
    ///     end if
    /// next                                                                               [:L225]
    /// </code>
    /// <para>
    /// C-B - THE SELECTION ORDERING IS INTENTIONAL LEGACY BEHAVIOUR AND IS REPRODUCED, NOT TIDIED.
    /// The <c>if</c> at [<c>:L221</c>] is a two-armed OR whose body assigns UNCONDITIONALLY, so all
    /// three of the following are reachable and none of them is "first match wins":
    /// </para>
    /// <list type="bullet">
    ///   <item><description>
    ///     A PREFIX MATCH ALWAYS ASSIGNS, so a later matching column OVERWRITES a column that an
    ///     earlier non-matching one had claimed.
    ///   </description></item>
    ///   <item><description>
    ///     A NON-MATCHING COLUMN ASSIGNS ONLY WHEN NOTHING IS SET YET, so the first identity column
    ///     found is retained as a fallback and no later non-matching column can displace it.
    ///   </description></item>
    ///   <item><description>
    ///     TWO PREFIX MATCHES: THE LAST ONE WINS, because each assigns unconditionally.
    ///   </description></item>
    /// </list>
    /// <para>
    /// C-B - THE IDENTITY TEST IS A STRING COMPARISON AGAINST THE LITERAL <c>"yes"</c>
    /// [<c>:L220</c>], not a boolean parse. It is ORDINAL and therefore CASE-SENSITIVE, matching
    /// PowerScript's <c>=</c> on strings: describe answers the lower-case <c>"yes"</c> or <c>"no"</c>,
    /// and any other answer - including <c>"!"</c>, <c>"?"</c>, <c>"Yes"</c> and empty - simply fails
    /// the test and the column is skipped. Accepting <c>"Yes"</c> would widen the set of candidate
    /// columns, which could change which column is chosen.
    /// </para>
    /// <para>
    /// C-B - THE PREFIX COMPARISON LOWER-CASES ONE SIDE AND TRUNCATES IT, RATHER THAN ASKING WHETHER
    /// THE NAME STARTS WITH THE PREFIX. The two are equivalent for every input, but the ported form is
    /// the one that stays diffable against its locator, and the truncation is where the PowerScript
    /// <c>Left</c> accommodation in <see cref="LegacyLeft"/> is load-bearing: the fixture asks for
    /// eight characters of a two-character name.
    /// </para>
    /// <para>
    /// R9 - THE SCAN IS ONE-BASED, THROUGH THIS FOLDER'S CENTRALIZED HELPER.
    /// <see cref="OneBasedIndex.Range"/> yields <see cref="OneBasedIndex.FirstIndex"/> through the
    /// column count INCLUSIVE, reproducing <c>for nIndex = 1 to nCount</c>, and it yields NOTHING for a
    /// count of zero or below, which is exactly what PowerScript's <c>for nIndex = 1 to 0</c> does. The
    /// ordinal is then passed to the describe vocabulary UNCHANGED, because that vocabulary is itself
    /// one-based - <c>#1.Identity</c> addresses the first column - so a rebased ordinal would compose a
    /// syntactically valid property that reads the WRONG column.
    /// </para>
    /// <para>
    /// THE UPDATE TABLE IS READ FROM THE DESCRIBE SURFACE, NOT FROM A CACHED DESCRIPTOR (C-E). The
    /// oracle reads the value its own modification script wrote [<c>:L143</c>, read back at
    /// [<c>:L188</c>], prefixed at [<c>:L217</c>]), which is what makes the discovered column
    /// independent of the identity column NAMED in the request's table contract. This function names no
    /// table and no column of its own.
    /// </para>
    /// </remarks>
    internal static int DiscoverIdentityColumn(IIdentityColumnMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        // `sColDBNamePrefix = Lower(sUpdateTable) + "."` [:L217]. Read at run time, never cached.
        string columnDbNamePrefix = BuildColumnDbNamePrefix(metadata.DescribeUpdateTable());

        // `nCount = Long(Data.Describe("DataWindow.Column.Count"))` [:L218]. An implementor supplies
        // the Long() coercion, so a failed describe arrives here as zero and the scan visits nothing.
        int columnCount = metadata.GetColumnCount();

        // PowerScript's zero-initialised `long nIdentityColumn` [:L174]. Both of its meanings - "not
        // chosen yet" for the fallback arm and "not found" for the caller - are this one value.
        int identityColumn = NoIdentityColumn;

        // R9: `for nIndex = 1 to nCount` [:L219], one-based and inclusive, through the folder's helper.
        foreach (int columnOrdinal in OneBasedIndex.Range(columnCount))
        {
            // `if Data.Describe("#" + String(nIndex) + ".Identity") = "yes" then` [:L220].
            // ORDINAL and therefore CASE-SENSITIVE, matching PowerScript's string equality.
            if (!string.Equals(
                    metadata.DescribeColumnIdentity(DescribeIdentityProperty(columnOrdinal)),
                    UpdateWhereBuilder.YesLiteral,
                    StringComparison.Ordinal))
            {
                continue;
            }

            // `Lower(Left(Data.Describe("#" + String(nIndex) + ".DBName"), Len(prefix)))` [:L221].
            // LegacyLeft supplies PowerScript's short-string behaviour, on which the fixture depends.
            string truncatedDbName = LegacyLeft(
                metadata.DescribeColumnDbName(DescribeDbNameProperty(columnOrdinal)),
                columnDbNamePrefix.Length);

            bool prefixMatches = string.Equals(
                truncatedDbName.ToLowerInvariant(),
                columnDbNamePrefix,
                StringComparison.Ordinal);

            // ================================================================================
            // C-B - THE ORDERING CONSEQUENCE, PRESERVED VERBATIM FROM [:L221-L222].
            // A PREFIX MATCH ASSIGNS UNCONDITIONALLY and therefore OVERWRITES a column an earlier
            // non-matching column had claimed. A NON-MATCHING COLUMN ASSIGNS ONLY WHEN NOTHING IS SET
            // YET, so the first identity column found survives as a fallback. This is neither
            // "first match wins" nor "last wins", and it must not be simplified into either.
            // ================================================================================
            if (prefixMatches || identityColumn == NoIdentityColumn)
            {
                identityColumn = columnOrdinal;
            }
        }

        return identityColumn;
    }

    #endregion

    #region Collection - Primary FORWARD

    /// <summary>
    /// Collects the identity values the database assigned to newly inserted rows of the
    /// <see cref="DwBuffer.Primary"/> buffer, scanning ASCENDING.
    /// </summary>
    /// <param name="values">The row surface.</param>
    /// <param name="identityColumnNumber">
    /// The discovered identity column's ONE-BASED ordinal. R9: never rebased.
    /// </param>
    /// <returns>
    /// The collected values in ASCENDING row order. Empty when no Primary row qualifies, which is a
    /// normal outcome - the Filter array may carry the whole payload.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="values"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="identityColumnNumber"/> is below
    /// <see cref="ItemStatusMachine.FirstColumnNumber"/> - the row-status sentinel is rejected, because
    /// a value is never addressed by it.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Ports [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru:L228-L233</c>]:
    /// </para>
    /// <code>
    /// nCount = Data.RowCount()                                                          [:L228]
    /// for nIndex = 1 to nCount                                                          [:L229]
    ///     if Data.GetItemStatus(nIndex,0,Primary!) = NewModified! then                    [:L230]
    ///         nPrimaryIdValues[UpperBound(nPrimaryIdValues) + 1] =
    ///             Data.GetItemNumber(nIndex,nIdentityColumn)                             [:L231]
    ///     end if
    /// next                                                                              [:L233]
    /// </code>
    /// <para>
    /// C-B - THE ROW GUARD READS THE ROW'S OWN STATUS, AT COLUMN INDEX ZERO [<c>:L230</c>], and it is
    /// an EQUALITY test against <c>NewModified!</c> alone. <see cref="ItemStatusMachine.IsNewRow"/> is
    /// that predicate and is CONSUMED rather than re-implemented, which also means
    /// <see cref="ItemStatus.New"/> is deliberately excluded: a row inserted but never edited does not
    /// participate. No bare <c>0</c> is passed here -
    /// <see cref="ItemStatusMachine.RowStatusColumn"/> is the named sentinel, and the sibling declares
    /// it precisely so that callers never spell it as a literal.
    /// </para>
    /// <para>
    /// C-B - THE TWO-ARGUMENT ACCESSOR IS USED, NOT THE FOUR-ARGUMENT ONE [<c>:L231</c>]. That is
    /// asymmetry 2 of the three the file header enumerates; see
    /// <see cref="CollectFilterValues"/> for its counterpart.
    /// </para>
    /// <para>
    /// R9 - THE SCAN IS A ONE-BASED <c>long</c> LOOP, bounded below by
    /// <see cref="ItemStatusMachine.FirstRowNumber"/> and above INCLUSIVELY by the row count, so
    /// neither row 0 nor row <c>count + 1</c> is ever touched. It does not go through
    /// <see cref="OneBasedIndex.Range"/> because that helper is the <c>int</c> ARRAY-INDEX domain while
    /// row numbers are the <c>long</c> ROW domain - see the R9 audit in the file header. A row count of
    /// zero visits nothing, matching <c>for nIndex = 1 to 0</c>.
    /// </para>
    /// <para>
    /// R9 - THE APPEND IS THE ONE-BASED APPEND IDIOM. <c>x[UpperBound(x) + 1] = v</c> [<c>:L231</c>]
    /// places each value at the one-based index <see cref="OneBasedIndex.AppendIndex{T}"/> reports for
    /// the list as it stands, which is precisely what <see cref="List{T}.Add"/> does. PowerBuilder grows
    /// the array implicitly, so the append cannot fail and cannot leave a gap.
    /// </para>
    /// </remarks>
    internal static IReadOnlyList<long?> CollectPrimaryValues(
        IIdentityValueSource values,
        int identityColumnNumber)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentOutOfRangeException.ThrowIfLessThan(
            identityColumnNumber,
            ItemStatusMachine.FirstColumnNumber);

        List<long?> collected = [];

        // `nCount = Data.RowCount()` [:L228]. The PRIMARY row count - contrast CollectFilterValues,
        // which reads the FILTERED count instead.
        long rowCount = values.RowCount();

        // R9: `for nIndex = 1 to nCount` [:L229] - ASCENDING, one-based, inclusive on both bounds.
        for (long row = ItemStatusMachine.FirstRowNumber; row <= rowCount; row++)
        {
            // `if Data.GetItemStatus(nIndex,0,Primary!) = NewModified! then` [:L230]. The ROW's status,
            // addressed by the named column-zero sentinel; never a literal 0.
            if (!ItemStatusMachine.IsNewRow(
                    values.GetItemStatus(row, ItemStatusMachine.RowStatusColumn, DwBuffer.Primary)))
            {
                continue;
            }

            // `Data.GetItemNumber(nIndex,nIdentityColumn)` [:L231]. THE TWO-ARGUMENT FORM: no buffer
            // argument and no original-value argument, exactly as the oracle writes it. Null is
            // preserved rather than coerced to zero - see DECISION 6 in the file header.
            collected.Add(values.GetItemNumber(row, identityColumnNumber));
        }

        return collected;
    }

    #endregion

    #region Collection - Filter BACKWARD  (the single most dangerous line in the refactor)

    /// <summary>
    /// Collects the identity values the database assigned to newly inserted rows of the
    /// <see cref="DwBuffer.Filter"/> buffer, scanning DESCENDING - which is correct, and is proven so
    /// in the remarks.
    /// </summary>
    /// <param name="values">The row surface.</param>
    /// <param name="identityColumnNumber">
    /// The discovered identity column's ONE-BASED ordinal. R9: never rebased.
    /// </param>
    /// <returns>
    /// The collected values in DESCENDING row order, which is REVERSE-SOURCE order relative to the
    /// Filter buffer's own indexing. The sequence is returned AS COLLECTED and must not be sorted,
    /// reversed or normalised by any consumer. Empty when no Filter row qualifies.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="values"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="identityColumnNumber"/> is below
    /// <see cref="ItemStatusMachine.FirstColumnNumber"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Ports [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru:L234-L241</c>]:
    /// </para>
    /// <code>
    /// //Filter                                                                          [:L234]
    /// //*(the inversion comment - see below)                                             [:L235]
    /// nCount = Data.FilteredCount()                                                     [:L236]
    /// for nIndex = nCount to 1 step -1                                                  [:L237]
    ///     if Data.GetItemStatus(nIndex,0,Filter!) = NewModified! then                     [:L238]
    ///         nFilterIdValues[UpperBound(nFilterIdValues) + 1] =
    ///             Data.GetItemNumber(nIndex,nIdentityColumn,Filter!,false)               [:L239]
    ///     end if
    /// next                                                                              [:L241]
    /// </code>
    /// <para>
    /// WHY THE DESCENDING LOOP IS CORRECT - THE TWO-LOCATOR PROOF (C-K). The refactor plan names
    /// [<c>:L237</c>] the single most dangerous line in the whole migration for one-based-to-zero-based
    /// translation: it looks like a bug, it is not a bug, and "correcting" it produces wrong identity
    /// values that a row-count assertion WOULD NOT CATCH.
    /// </para>
    /// <list type="number">
    ///   <item><description>
    ///     THE ORACLE STATES THE REASON IMMEDIATELY ABOVE THE LOOP. [<c>:L235</c>] reads
    ///     <c>//*过滤缓冲区的数据与数据源(调用GetChanges的对象)的过滤缓冲区的数据顺序是相反的</c> - the
    ///     Filter buffer's row order is INVERTED relative to the data source that produced the
    ///     changeset by calling <c>GetChanges</c>. The published contract carries the same fact
    ///     independently, annotating <c>DW_BUFFER_FILTER</c> as row-order-inverted.
    ///   </description></item>
    ///   <item><description>
    ///     THE WRITE-BACK HALF APPLIES THESE VALUES FORWARD, WHICH IS WHAT MAKES BACKWARD COLLECTION
    ///     CORRECT. <c>of_refreshidentitydata</c>
    ///     [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlupdate.sru:L120-L205</c>,
    ///     whose body runs to <c>:L204</c> with <c>end function</c> at <c>:L205</c>]
    ///     seeds <c>GetNextModified(0, Filter!)</c> [<c>:L154</c>, <c>:L188</c>], advances ASCENDING
    ///     [<c>:L163</c>, <c>:L197</c>], and consumes the collected array from index 1 upward with a
    ///     counter that only increments [<c>:L157-L160</c>, <c>:L191-L194</c>].
    ///     COLLECT BACKWARD PLUS APPLY FORWARD IS WHAT RE-ALIGNS THE INVERTED ORDER.
    ///   </description></item>
    /// </list>
    /// <para>
    /// Reverse this loop and the SAME NUMBER of values still comes back, each paired with the WRONG
    /// ROW - which is why the sibling test file asserts the ORDER, and why a count-only assertion is
    /// not a guard. If a reviewer or a linter suggests normalising the direction, these two locators
    /// are the answer.
    /// </para>
    /// <para>
    /// C-B - ASYMMETRY 1 OF 3: THE DIRECTION. Its Primary twin at [<c>:L229</c>] ascends; see
    /// <see cref="CollectPrimaryValues"/>.
    /// </para>
    /// <para>
    /// C-B - ASYMMETRY 2 OF 3: THE FOUR-ARGUMENT ACCESSOR [<c>:L239</c>], naming
    /// <see cref="DwBuffer.Filter"/> and the original-value flag <see langword="false"/> explicitly,
    /// against the two-argument form the Primary loop uses. The <see langword="false"/> is not
    /// redundant: the round trip wants the value the database JUST ASSIGNED, and the original value
    /// would be the pre-insert one.
    /// </para>
    /// <para>
    /// C-B - ASYMMETRY 3 OF 3: THE COUNT AND THE BUFFER. This scan is bounded by
    /// <see cref="IIdentityValueSource.FilteredCount"/> [<c>:L236</c>], NOT by the row count, and it
    /// reads the <see cref="DwBuffer.Filter"/> status [<c>:L238</c>], not the Primary one. The oracle
    /// reassigns the same local between the two scans, which is exactly the kind of reuse a careless
    /// port drops.
    /// </para>
    /// <para>
    /// R9 - THE DESCENDING SCAN IS A ONE-BASED <c>long</c> LOOP, starting AT the filtered count and
    /// terminating AT <see cref="ItemStatusMachine.FirstRowNumber"/> inclusive, exactly as
    /// <c>nCount to 1 step -1</c> does. It never touches row 0 and never touches
    /// <c>count + 1</c>. A filtered count of zero visits nothing, because the loop's first test
    /// already fails - matching <c>for nIndex = 0 to 1 step -1</c>, which PowerScript executes zero
    /// times.
    /// </para>
    /// <para>
    /// R9 - VALUES ARE APPENDED IN ITERATION ORDER, so the resulting list's one-based index 1 holds the
    /// value from the HIGHEST qualifying row number. That is the oracle's own arrangement
    /// [<c>:L239</c>] and is what the forward-applying write-back consumes.
    /// </para>
    /// </remarks>
    internal static IReadOnlyList<long?> CollectFilterValues(
        IIdentityValueSource values,
        int identityColumnNumber)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentOutOfRangeException.ThrowIfLessThan(
            identityColumnNumber,
            ItemStatusMachine.FirstColumnNumber);

        List<long?> collected = [];

        // `nCount = Data.FilteredCount()` [:L236]. THE FILTERED COUNT, NOT THE ROW COUNT.
        long filteredCount = values.FilteredCount();

        // ====================================================================================
        // ★ THE SINGLE MOST DANGEROUS LINE IN THE REFACTOR - AND IT IS CORRECT AS WRITTEN.
        //
        //     `for nIndex = nCount to 1 step -1`   [n_cst_thread_task_sqlupdate.sru:L237]
        //
        // DO NOT "CORRECT" THIS DIRECTION. Two locators prove it:
        //   1. n_cst_thread_task_sqlupdate.sru:L235 - the oracle's own comment, sitting immediately
        //      above this loop, states that the Filter buffer's row order is INVERTED relative to the
        //      data source that produced the changeset.
        //   2. n_cst_threading_task_sqlupdate.sru:L120-L205 - the write-back half APPLIES these
        //      values FORWARD, walking GetNextModified(0, Filter!) ascending and consuming this list
        //      from its first element onward. COLLECT BACKWARD + APPLY FORWARD undoes the inversion.
        //
        // Reversing this loop keeps the COUNT identical while pairing every value with the wrong row,
        // which is exactly why a row-count assertion cannot detect the regression.
        //
        // R9: one-based in both bounds. Starts AT filteredCount, terminates AT FirstRowNumber.
        // ====================================================================================
        for (long row = filteredCount; row >= ItemStatusMachine.FirstRowNumber; row--)
        {
            // `if Data.GetItemStatus(nIndex,0,Filter!) = NewModified! then` [:L238]. The ROW's status
            // in the FILTER buffer, addressed by the named column-zero sentinel; never a literal 0.
            if (!ItemStatusMachine.IsNewRow(
                    values.GetItemStatus(row, ItemStatusMachine.RowStatusColumn, DwBuffer.Filter)))
            {
                continue;
            }

            // `Data.GetItemNumber(nIndex,nIdentityColumn,Filter!,false)` [:L239]. THE FOUR-ARGUMENT
            // FORM, naming the buffer and the original-value flag explicitly. Appended in ITERATION
            // order - never sorted, never reversed, never normalised.
            collected.Add(
                values.GetItemNumber(
                    row,
                    identityColumnNumber,
                    DwBuffer.Filter,
                    originalValue: false));
        }

        return collected;
    }

    #endregion

    #region The emit guard - fire only when at least one array is non-empty

    /// <summary>
    /// Runs both collections for a discovered identity column and applies the oracle's emit guard,
    /// answering <see langword="null"/> when the oracle would have fired NOTHING.
    /// </summary>
    /// <param name="values">The row surface.</param>
    /// <param name="identityColumnNumber">
    /// The discovered identity column's ONE-BASED ordinal, from
    /// <see cref="DiscoverIdentityColumn"/>.
    /// </param>
    /// <returns>
    /// The payload when at least one of the two arrays is non-empty; otherwise
    /// <see langword="null"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="values"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="identityColumnNumber"/> is below
    /// <see cref="ItemStatusMachine.FirstColumnNumber"/>. <see cref="NoIdentityColumn"/> is REJECTED
    /// rather than silently answering null, because the oracle gates on it BEFORE collecting
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru:L226</c>] - reaching this
    /// function with no discovered column is a caller fault, and the plan requires structural faults
    /// fail fast rather than degrade.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Ports the guard at
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru:L242-L244</c>]:
    /// </para>
    /// <code>
    /// if UpperBound(nPrimaryIdValues) &gt; 0 or UpperBound(nFilterIdValues) &gt; 0 then       [:L242]
    ///     tasking.Event OnIdentityColumnDataRetrieved(nIdentityColumn,
    ///                       ref nPrimaryIdValues, ref nFilterIdValues)                       [:L243]
    /// end if                                                                                 [:L244]
    /// </code>
    /// <para>
    /// C-B - IT IS AN <c>OR</c>, NOT AN <c>AND</c>, so ONE non-empty array is enough and the other is
    /// transmitted EMPTY rather than omitted, merged or null-collapsed. Both single-array cases are
    /// real: a result with nothing filtered out has an empty Filter array, and one whose inserted rows
    /// were all filtered out has an empty Primary array.
    /// </para>
    /// <para>
    /// C-B - BOTH ARRAYS EMPTY EMITS NOTHING AT ALL. No empty payload is synthesised, because the
    /// oracle's guard is on the CALL rather than on the contents, and the published contract reads an
    /// absent identity block as "none collected" rather than as an error.
    /// </para>
    /// <para>
    /// THE TWO ARRAYS ARE RETURNED SEPARATELY BECAUSE THE ORACLE PASSES THEM AS TWO <c>ref</c> ARRAY
    /// OUT-PARAMETERS ALONGSIDE THE COLUMN ID [<c>:L243</c>, declared at
    /// <c>n_cst_threading_task_sqlupdate.sru:L19</c>]. C# expresses that as one returned record rather
    /// than three <c>out</c> parameters, which keeps the trio indivisible; what it must never become is
    /// one merged array, because the two are applied to two DIFFERENT buffers through two DIFFERENT
    /// write accessors on the write-back side.
    /// </para>
    /// <para>
    /// R9 - THE EMPTINESS TEST IS THE ORACLE'S OWN <c>UpperBound(x) &gt; 0</c>, expressed through this
    /// folder's centralized helper rather than as a <c>Count</c> comparison, so the one-based reading
    /// stays explicit: <see cref="OneBasedIndex.UpperBound{T}"/> answers the LAST VALID INDEX and
    /// <see cref="OneBasedIndex.EmptyUpperBound"/> is what it answers for an empty collection.
    /// </para>
    /// <para>
    /// THE COLLECTION ORDER OF THE TWO CALLS IS THE ORACLE'S: Primary first [<c>:L228-L233</c>], Filter
    /// second [<c>:L234-L241</c>]. It is preserved because a recording implementation of
    /// <see cref="IIdentityValueSource"/> observes it, and because a future reader diffing this function
    /// against its locator should not have to reorder anything mentally.
    /// </para>
    /// </remarks>
    internal static ResolvedIdentityColumnData? CollectIdentityData(
        IIdentityValueSource values,
        int identityColumnNumber)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentOutOfRangeException.ThrowIfLessThan(
            identityColumnNumber,
            ItemStatusMachine.FirstColumnNumber);

        // Primary FIRST, Filter SECOND - the oracle's own order [:L228-L233] then [:L234-L241].
        IReadOnlyList<long?> primaryValues = CollectPrimaryValues(values, identityColumnNumber);
        IReadOnlyList<long?> filterValues = CollectFilterValues(values, identityColumnNumber);

        // `if UpperBound(nPrimaryIdValues) > 0 or UpperBound(nFilterIdValues) > 0 then` [:L242].
        // AN OR, NOT AN AND. R9: expressed through the one-based helper, not as a Count comparison.
        bool anyCollected =
            OneBasedIndex.UpperBound(primaryValues) > OneBasedIndex.EmptyUpperBound
            || OneBasedIndex.UpperBound(filterValues) > OneBasedIndex.EmptyUpperBound;

        if (!anyCollected)
        {
            // Both empty: the oracle fires NOTHING [:L242-L244]. No empty payload is synthesised.
            return null;
        }

        // The three members are in the oracle's `IdColData` field order
        // [n_cst_threading_task_sqlupdate.sru:L10-L14]. The ordinal widens from the int column domain
        // to the long the legacy structure and the wire message both declare.
        return new ResolvedIdentityColumnData(identityColumnNumber, primaryValues, filterValues);
    }

    #endregion

    #region The counts triple - read once per invocation, never accumulated

    /// <summary>
    /// Reads the inserted, updated and deleted counts the successful update produced.
    /// </summary>
    /// <param name="values">The row surface.</param>
    /// <returns>The three counts exactly as the carrier reports them.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="values"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// Ports
    /// <c>tasking.Event OnUpdated(Data.of_GetInsertedCount(), Data.of_GetUpdatedCount(),
    /// Data.of_GetDeletedCount())</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru:L247</c>], reading the
    /// three getters in the oracle's argument order.
    /// </para>
    /// <para>
    /// C-B - REPORTED UNMODIFIED AND UNACCUMULATED. The values pass through with no clamping, no
    /// coercion of a negative and no summing; accumulation across firings is the caller-side handler's
    /// job, which does it with <c>+=</c> [<c>n_cst_threading_task_sqlupdate.sru:L66-L68</c>]. Summing
    /// here as well would double-count every multi-table update.
    /// </para>
    /// </remarks>
    internal static UpdateRowCounts ReadCounts(IIdentityValueSource values)
    {
        ArgumentNullException.ThrowIfNull(values);

        // Argument order is the oracle's: inserted, updated, deleted [:L247].
        return new UpdateRowCounts(
            values.GetInsertedCount(),
            values.GetUpdatedCount(),
            values.GetDeletedCount());
    }

    #endregion

    #region The whole success arm - the three conditional levels plus the unconditional counts

    /// <summary>
    /// Runs the complete identity round-trip collection for one successful update: the inserted-count
    /// precondition, run-time discovery, both collections, the emit guard, and the counts triple.
    /// </summary>
    /// <param name="metadata">The describe surface. In the legacy this is the same object as
    /// <paramref name="values"/>.</param>
    /// <param name="values">The row surface.</param>
    /// <returns>
    /// The outcome for THIS ONE TABLE: <see cref="IdentityResolutionOutcome.Identity"/> holds exactly
    /// one block, or is EMPTY whenever the oracle would have fired nothing, and
    /// <see cref="IdentityResolutionOutcome.Counts"/> is ALWAYS populated. A multi-table update is
    /// driven through <see cref="ResolveTables(IReadOnlyList{IdentityTableSurfaces})"/>, which calls
    /// this once per table and accumulates.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="metadata"/> or <paramref name="values"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Ports the success arm of <c>_of_update</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru:L214-L248</c>] end to
    /// end. The FAILURE arm [<c>:L249-L250</c>], the update call itself [<c>:L204</c>], the
    /// before-update and after-update hooks [<c>:L195</c>, <c>:L206</c>], the defensive
    /// success-to-failure rewrite [<c>:L208-L210</c>] and the two cancellation checks [<c>:L182</c>,
    /// <c>:L212</c>] all belong to <c>Tasks/SqlUpdateTask.cs</c>; this function is only ever reached
    /// once that code has established <c>rtCode = 1</c>.
    /// </para>
    /// <para>
    /// C-B - THE INSERTED-COUNT PRECONDITION IS REPRODUCED, INCLUDING ITS SHORT-CIRCUIT.
    /// <c>if Data.of_GetInsertedCount() &gt; 0 then</c> [<c>:L215</c>] gates the ENTIRE identity block,
    /// so a pure update and a pure delete perform NO DISCOVERY AND NO COLLECTION AT ALL - not even the
    /// column-count describe. That is observable through the injected surfaces, and the sibling test
    /// file asserts it by counting the describe reads.
    /// </para>
    /// <para>
    /// C-B - THE COUNTS TRIPLE FIRES REGARDLESS, BECAUSE OF WHERE THE ORACLE PUTS IT. [<c>:L247</c>]
    /// sits AFTER the inserted-count block closes at [<c>:L246</c>] and BEFORE the success return at
    /// [<c>:L248</c>], so it is inside the success arm and outside every one of the three identity
    /// conditions. An implementation that reported counts only alongside an identity payload would lose
    /// them for every pure update, every pure delete, and every insert into a table with no identity
    /// column. The read is placed last here for the same reason it is last there: the counts describe
    /// the update as a whole, not the identity work.
    /// </para>
    /// <para>
    /// C-B - THE INSERTED COUNT IS READ TWICE, ONCE FOR THE PRECONDITION AND ONCE FOR THE TRIPLE,
    /// exactly as the oracle does at [<c>:L215</c>] and [<c>:L247</c>]. It is deliberately NOT cached
    /// between them: a carrier may answer differently, and hoisting the read would change what the
    /// triple reports.
    /// </para>
    /// <para>
    /// TWO PARAMETERS, NOT ONE, AND ONE OBJECT MAY BE BOTH. The legacy has a single <c>Data</c>
    /// carrier, but splitting the describe surface from the row surface is what lets a test drive
    /// discovery with no rows and collection with no describe. The sibling
    /// <see cref="UpdatePreparer"/> splits its two surfaces the same way for the same reason, and a
    /// production adapter over <see cref="DataWindowCarrier"/> implements both on one type.
    /// </para>
    /// </remarks>
    internal static IdentityResolutionOutcome Resolve(
        IIdentityColumnMetadata metadata,
        IIdentityValueSource values)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(values);

        ResolvedIdentityColumnData? identity = null;

        // LEVEL 1 - `if Data.of_GetInsertedCount() > 0 then` [:L215]. Nothing below runs for a pure
        // update or a pure delete, not even the column-count describe.
        if (values.GetInsertedCount() > 0)
        {
            // Run-time discovery [:L217-L225].
            int identityColumn = DiscoverIdentityColumn(metadata);

            // LEVEL 2 - `if nIdentityColumn > 0 then` [:L226].
            if (identityColumn > NoIdentityColumn)
            {
                // LEVEL 3 - both collections plus the emit guard [:L228-L244]. Null means the oracle
                // fired nothing.
                identity = CollectIdentityData(values, identityColumn);
            }
        }

        // `tasking.Event OnUpdated(...)` [:L247]. OUTSIDE all three levels, INSIDE the success arm:
        // this fires even when the inserted count was zero and no identity work happened.
        //
        // ONE CALL OF `_of_Update` FIRES THE IDENTITY CALLBACK AT MOST ONCE [:L243], so this list holds
        // one element or none. The N-element case is produced by ResolveTables, which is the
        // multi-table loop [:L358-L369], not by this function - and keeping the two apart is what makes
        // "at most once per table" checkable rather than assumed.
        return new IdentityResolutionOutcome
        {
            Identity = identity is null ? [] : [identity],
            Counts = ReadCounts(values),
        };
    }

    /// <summary>
    /// Runs the complete multi-table update loop: the identity round trip once per table, accumulated
    /// in table order, with the counts summed across the tables the way the caller-side proxy sums
    /// them.
    /// </summary>
    /// <param name="tables">
    /// The per-table surface pairs, in the order the tables were declared. Each element is the carrier
    /// as it stands after that table's prepare step - see <see cref="IdentityTableSurfaces"/> for why
    /// that has to be per table rather than once.
    /// </param>
    /// <returns>
    /// One outcome for the whole update: <see cref="IdentityResolutionOutcome.Identity"/> carries one
    /// block per table that collected any, IN TABLE ORDER, and
    /// <see cref="IdentityResolutionOutcome.Counts"/> carries the accumulated triple.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="tables"/> is <see langword="null"/>, or any element carries a
    /// <see langword="null"/> surface.
    /// </exception>
    /// <remarks>
    /// <para>
    /// THIS IS THE CALLER SIDE OF THE PROXY PAIR, AND IT IS WHERE ACCUMULATION BELONGS. The worker's
    /// task loop drives one <c>_of_Update</c> per table
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru:L358-L369</c>], and the
    /// caller-side proxy is what turns that sequence of firings into the two things a response needs:
    /// </para>
    /// <code>
    /// event onidentitycolumndataretrieved(long id, ref long primaryvalues[], ref long filtervalues[]);
    ///     nIndex = UpperBound(_idColDatas) + 1              [n_cst_threading_task_sqlupdate.sru:L73]
    ///     _idColDatas[nIndex].id = id                       [:L74]
    ///     _idColDatas[nIndex].primaryValues = primaryValues [:L75]
    ///     _idColDatas[nIndex].filterValues = filterValues   [:L76]
    ///
    /// event onupdated(long inserted, long updated, long deleted);
    ///     _nRowsInserted += inserted                        [:L66]
    ///     _nRowsUpdated  += updated                         [:L67]
    ///     _nRowsDeleted  += deleted                         [:L68]
    /// </code>
    /// <para>
    /// C-B - APPEND, NEVER REPLACE, AND SUM, NEVER OVERWRITE. Both halves above are accumulating
    /// handlers, and reproducing either as an assignment is a data-loss defect that returns a plausible
    /// answer: identities would keep only the last table's block, and the counts would report only the
    /// last table's rows. <see cref="Resolve"/> deliberately does neither, which is why its own remarks
    /// say the counts are never accumulated there - accumulation is THIS function's job, exactly as it
    /// is the proxy's and not the worker's.
    /// </para>
    /// <para>
    /// THE SINGLE-TABLE ARM IS THE SAME CODE PATH. The oracle's <c>else</c> branch calls
    /// <c>_of_Update</c> once [<c>:L371</c>], which is what a one-element <paramref name="tables"/>
    /// produces here - so there is no separate single-table implementation to keep in step, and a
    /// one-table update through this function is byte-for-byte the outcome
    /// <see cref="Resolve"/> alone would have produced.
    /// </para>
    /// <para>
    /// AN EMPTY TABLE LIST IS NOT THIS FUNCTION'S ERROR TO RAISE. The oracle rejects it one level up,
    /// before any update runs - <c>if nCount = 0 then ... rtCode = RetCode.E_INVALID_ARGUMENT</c>
    /// [<c>:L359-L362</c>] - and that arm belongs to <c>Tasks/SqlUpdateTask.cs</c> together with the
    /// rest of the task loop. Reached with an empty list this function answers the honest empty
    /// outcome rather than inventing a second diagnostic for the same condition.
    /// </para>
    /// </remarks>
    internal static IdentityResolutionOutcome ResolveTables(IReadOnlyList<IdentityTableSurfaces> tables)
    {
        ArgumentNullException.ThrowIfNull(tables);

        // `IDCOLDATA _idColDatas[]` [n_cst_threading_task_sqlupdate.sru:L45]. Built in order and frozen
        // at the end, so the accumulated order cannot be disturbed afterwards.
        ImmutableArray<ResolvedIdentityColumnData>.Builder blocks =
            ImmutableArray.CreateBuilder<ResolvedIdentityColumnData>();

        long inserted = 0L;
        long updated = 0L;
        long deleted = 0L;

        // `for nIndex = 1 to nCount` [:L364]. The index is zero-based here only because it addresses a
        // CLR list slot; the ORDER it walks is the legacy's table order, which is the part that is
        // contract (R9).
        for (int index = 0; index < tables.Count; index++)
        {
            IdentityTableSurfaces table = tables[index];

            ArgumentNullException.ThrowIfNull(table.Metadata, nameof(tables));
            ArgumentNullException.ThrowIfNull(table.Values, nameof(tables));

            // `rtCode = _of_Update(data)` [:L367]. One firing at most, appended [:L73-L76].
            IdentityResolutionOutcome perTable = Resolve(table.Metadata, table.Values);

            blocks.AddRange(perTable.Identity);

            // `+=` on all three [:L66-L68].
            inserted += perTable.Counts.Inserted;
            updated += perTable.Counts.Updated;
            deleted += perTable.Counts.Deleted;
        }

        return new IdentityResolutionOutcome
        {
            Identity = blocks.ToImmutable(),
            Counts = new UpdateRowCounts(inserted, updated, deleted),
        };
    }

    #endregion
}

#endregion
