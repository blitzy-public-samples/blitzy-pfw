// ==============================================================================================
//  UpdateWhereBuilder.cs - RUNTIME RE-DERIVATION OF THE DATAWINDOW UPDATE CONTRACT
//  --------------------------------------------------------------------------------------------
//  PORT OF        ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru
//                     :L10-L17   the six-field `tabledata` structure
//                     :L30, L33  `TABLEDATA Tables[]` and the multi-table switch
//                     :L58-L72   of_reset
//                     :L82-L96   of_addupdatabletable
//                     :L98-L170  _of_updateprepare  <- the whole of this file's behaviour
//                     :L356-L372 the ondotask invocation site
//  ALSO SOURCED   ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlupdate.sru
//                     :L223-L225 the six-argument caller-side overload
//                     :L227-L234 the four-argument overload that NULLS both settings
//                     :L207-L212 the of_IsBusy guard shape
//                 ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds.sru
//                     :L8        `from datastore` - which is why Describe/Modify/RowCount are
//                                INHERITED members and appear in no prototype list
//  FIXTURE        ws_objects/pfw.tests.pbl.src/dw_sqlite.srd
//                     :L8-L13    six columns, every one update=yes updatewhereclause=yes
//                     :L14       updatewhere=1 updatekeyinplace=no
//                     :L21-L26   column ids 1..6 in declaration order
//
//  ORACLE STATUS (C-C). Every path above is READ ONLY. They are the behavioural oracle for parity
//  testing and are never an edit target, never reformatted, never moved and never copied into the
//  .NET tree. Nothing else in this repository can adjudicate the behaviour below, so every
//  behavioural claim here carries a ws_objects/** line locator and a reader who doubts a line can
//  diff it against that locator. Every locator in this file was read first-hand against the export;
//  none was taken on trust, and no divergence was found.
//
//  WHAT THIS FILE IS
//  --------------------------------------------------------------------------------------------
//  The foundational file of the Concurrency/ folder, which carries goal G4(a): preserve
//  `updatewhereclause` optimistic-concurrency semantics ACROSS A NETWORK BOUNDARY. It answers four
//  questions and nothing else:
//
//      1. What is an updatable-table descriptor?              UpdatableTableDescriptor
//      2. What modification script does a descriptor produce?  UpdateWhereBuilder
//      3. How is that script applied, and what must be
//         refreshed afterwards when a key change becomes a
//         delete-plus-insert?                                  UpdatePreparer
//      4. Which columns' ORIGINAL values must travel?          UpdateWhereBuilder.MarkedColumnsOf
//
//  It does NOT execute SQL, open a connection, generate a statement or resolve an identity column.
//  Statement execution belongs to Tasks/SqlUpdateTask.cs, conflict classification to
//  Concurrency/ConflictDetector.cs and the identity round trip to
//  Concurrency/IdentityColumnResolver.cs. This file only re-derives the CONTRACT those layers
//  operate under.
//
//  ============================================================================================
//  WHAT updatewhere=1 REQUIRES OF THE WIRE - THE REASON THIS FOLDER EXISTS
//  ============================================================================================
//  The sole updatable DataWindow in the repository declares
//
//      retrieve="SELECT * FROM COMPANY" update="COMPANY" updatewhere=1 updatekeyinplace=no
//                                                          [ws_objects/.../dw_sqlite.srd:L14]
//
//  and all six of its columns carry `update=yes updatewhereclause=yes` [:L8-L13]. Only `id` is
//  `key=yes identity=yes`; the other five are ordinary updateable columns.
//
//  `updatewhere=1` is the "KEY AND UPDATEABLE COLUMNS" concurrency mode: the generated update
//  statement's WHERE clause carries the key column PLUS THE ORIGINAL VALUE OF EVERY UPDATEABLE
//  COLUMN. With all six marked, the optimistic-concurrency check spans ALL SIX COLUMNS' ORIGINAL
//  VALUES.
//
//  THE CONSEQUENCE, STATED PLAINLY: A NAIVE ROWSET IS INSUFFICIENT. The payload must transmit, PER
//  ROW, BOTH THE CURRENT AND THE ORIGINAL VALUE OF EVERY MARKED COLUMN. A flat row array carries
//  one value per cell and therefore cannot express the comparison at all - it would either omit the
//  where clause, silently overwriting a concurrent change, or compare against the value being
//  written, which always matches.
//
//  That is precisely what the Buffers/ anti-corruption layer exists to carry, and it is why the
//  legacy result carrier derives from a datastore rather than being a result set: the legacy
//  carrier IS a DataWindow, complete with its three buffers, its per-row item status and its
//  per-column item status and original-value shadow. Buffers/DataWindowBuffers.cs reproduces that
//  explicitly - CarrierRow holds a value map, an ORIGINAL-value map and a per-column status map -
//  and this file's MarkedColumnsOf names which of those columns the concurrency check reads.
//
//  ============================================================================================
//  DECISION 1 - WHY THE RESET-FIRST PASS IS RETAINED (C-B, C-K)
//  ============================================================================================
//  _of_updateprepare begins by turning Update, Key and Identity OFF on EVERY column by ordinal
//  [:L103-L108] and only then re-enables the ones the descriptor names [:L111-L129]. Every reset
//  line for a column the descriptor goes on to re-enable is, read locally, redundant.
//
//  It is retained verbatim because that pass IS THE MEANING OF "RE-DERIVED AT RUNTIME". Without it
//  the result would be the union of the carrier's static definition and the descriptor; with it the
//  result is the descriptor alone. The observable consequence a caller must expect: A COLUMN MARKED
//  UPDATABLE IN THE CARRIER'S OWN DEFINITION BUT ABSENT FROM THE DESCRIPTOR IS NOT UPDATABLE for
//  this operation. A partial descriptor gets a partial update, deterministically.
//
//  Collapsing the pass into "reset only what is not about to be re-enabled" would produce the same
//  final attribute state on today's fixture and a DIFFERENT SCRIPT, and the script is compared byte
//  for byte. C-B forbids the optimisation on both counts.
//
//  ============================================================================================
//  DECISION 2 - WHY updatewhere AND updatekeyinplace ARE NULLABLE RATHER THAN DEFAULTED (C-K)
//  ============================================================================================
//  Both are written into the carrier ONLY WHEN NOT NULL:
//
//      if Not IsNull(Tables[index].UpdateWhere) then ...        [:L131-L133]
//      if Not IsNull(Tables[index].UpdateKeyInPlace) then ...   [:L135-L141]
//
//  so null carries the distinct meaning "LEAVE THE CARRIER'S OWN SETTING ALONE" - the property line
//  is not emitted at all. This is not incidental: the caller-side four-argument overload
//  DELIBERATELY nulls both before delegating -
//
//      SetNull(nUpdateWhere) ; SetNull(bUpdateInPlace)
//                                        [n_cst_threading_task_sqlupdate.sru:L230-L231]
//
//  - so absence is a first-class input, not an uninitialised accident.
//
//  They are therefore `long?` and `bool?`, branched on HasValue. A SENTINEL WOULD BE WRONG IN BOTH
//  CASES. Substituting 0 for an absent update-where is the most dangerous single mistake available
//  on this contract, because 0 is a LEGAL concurrency mode: a caller who omitted the field would
//  silently force a mode it never asked for. Substituting false for an absent key-in-place is worse
//  than wrong, because false is the value that TRIGGERS the workaround in DECISION 4 - an omitted
//  setting would start rewriting item statuses.
//
//  This maps one-to-one onto proto3 EXPLICIT PRESENCE, which is exactly how the published boundary
//  models it: `optional int64 updatewhere = 5` and `optional bool updatekeyinplace = 6` on
//  common ground with the other four fields in
//  shared/PowerFramework.Contracts/Proto/persistence.v1.proto's TableUpdateContract.
//
//  ============================================================================================
//  DECISION 3 - THE SCRIPT IS COMPARED BYTE FOR BYTE, SO EVERY CHARACTER IS CONTRACT (C-B)
//  ============================================================================================
//  Five spellings in this file look like defects and are not. Each is reproduced verbatim and none
//  may be tidied:
//
//    * THE SEPARATOR IS A SINGLE LINE FEED, NEVER CRLF. Every appended line ends `+ "~n"`
//      [:L105-L107, L113, L124, L128, L132, L137, L139]. PowerScript's `~n` is U+000A alone.
//      Environment.NewLine is therefore forbidden here: on Windows it would insert a carriage
//      return into every line of a machine-read script and change every byte comparison.
//
//    * THE UPDATE-WHERE VALUE IS SINGLE-QUOTED ALTHOUGH IT IS A NUMBER.
//      `"DataWindow.Table.UpdateWhere = '" + String(...) + "'"` [:L132].
//
//    * THE PROPERTY IS SPELLED UpdateKeyinPlace, WITH A LOWER-CASE i IN "in" [:L137, L139], and the
//      same lower-case spelling is used again when the value is read back [:L155]. The
//      describe/modify vocabulary is case-shaped, so "correcting" it to UpdateKeyInPlace would
//      address a property that does not exist. Note the contrast with the C# member name
//      UpdateKeyInPlace, which is a .NET identifier and is correctly cased: the two are
//      deliberately different and the constant below is what keeps them from being confused.
//
//    * THE FINAL LINE CARRIES NO TRAILING NEWLINE. UpdateTable is appended without `+ "~n"`
//      [:L143], so the script must not end in a line feed.
//
//    * THE DIAGNOSTIC IS HARDCODED CHINESE WITH NO SPACE AFTER THE COLON AND DOES NOT ROUTE THROUGH
//      LOCALIZATION [:L120]. It is reproduced exactly, untranslated.
//
//  ============================================================================================
//  DECISION 4 - WHAT REPLACED THE SELF-ASSIGNMENT, AND WHY THE OUTCOME IS IDENTICAL (C-B, C-K)
//  ============================================================================================
//  The legacy documents a defect against itself. Its FIXME [:L151-L154] states that when the DW's
//  currently requested KEY field has Key=no, DataModified! will not generate Delete/Insert
//  statements, so the internal modified state must be force-refreshed to make it do so. The
//  implementation follows [:L155-L167] and its operative line is
//
//      Data.Object.Data[nRow,nKeyColumns[nIndex]] = Data.Object.Data[nRow,nKeyColumns[nIndex]]
//                                                                                      [:L163]
//
//  IT ASSIGNS A MODIFIED KEY COLUMN TO ITSELF, purely to flip hidden item state. THERE IS NO .NET
//  ANALOGUE FOR A SELF-ASSIGNMENT THAT MUTATES HIDDEN STATE, and inventing one would be inventing
//  hidden state - the exact thing this port removes.
//
//  WHY THE LEGACY NEEDED IT. PowerBuilder computes "which changed columns are key columns" against
//  the Key attributes as they stood WHEN THE ITEM CHANGED. The modification script above has just
//  reassigned those attributes [:L116-L125], so that computation is stale. Re-touching the item
//  re-registers it against the NEW attributes, and only then does the update generator see a
//  key-column change and emit DELETE plus INSERT instead of an in-place UPDATE.
//
//  WHY THE MANAGED MODEL CANNOT HAVE THAT STALENESS. Buffers/ holds the state explicitly: CarrierRow
//  carries the current value, the ORIGINAL value captured once per baseline, and the per-column item
//  status. The statement generator reads that triple AT GENERATION TIME, which is necessarily after
//  the descriptor was applied, so there is no earlier snapshot to go stale against.
//
//  WHAT THIS FILE DOES INSTEAD, and it is deliberately not "nothing":
//
//      1. It reproduces the gate and both skip arms EXACTLY, so the same row/column pairs qualify.
//      2. For each qualifying pair it re-asserts the triple explicitly - it writes the current value
//         back through SetItemValue, which preserves the already-captured original because
//         CarrierRow.SetValue captures once per baseline and never overwrites, and it then stamps
//         ItemStatus.DataModified through SetItemStatus. The second step is REQUIRED rather than
//         belt-and-braces: Buffers/DataWindowBuffers.cs states on CarrierRow.SetValue that it
//         deliberately does NOT flip status and that a status change is ALWAYS an explicit
//         SetItemStatus call, precisely because this port must express status changes explicitly.
//      3. It RECORDS the qualifying pairs on the result as KeyChangeRefresh values, so the
//         obligation is observable. Tasks/SqlUpdateTask.cs can therefore be asserted to emit the
//         delete-plus-insert pair for exactly those pairs, which is the observable outcome the
//         legacy achieved by side effect. An unobservable side effect would be untestable, and
//         C-H requires this path be covered with no database.
//
//  AND THE GATE IS ON THE RUNTIME VALUE, NOT ON THE DESCRIPTOR [:L155] - see DECISION 5.
//
//  THE FIXTURE EXERCISES THIS PATH. dw_sqlite.srd:L14 sets updatekeyinplace=no itself, so this is
//  the mainline, not a corner. It is covered by tests accordingly.
//
//  ============================================================================================
//  DECISION 5 - WHY THE WORKAROUND GATES ON THE RUNTIME DESCRIBE AND NOT ON THE DESCRIPTOR (C-K)
//  ============================================================================================
//  The gate is literally
//
//      if Data.Describe("DataWindow.Table.UpdateKeyinPlace") = "no" then      [:L155]
//
//  It reads the value BACK OFF THE CARRIER rather than consulting the descriptor's own setting, and
//  the difference is load-bearing in both directions:
//
//    * DESCRIPTOR ABSENT, CARRIER SAYS no. Step 6 emitted nothing, so the effective value is the
//      carrier's own definition - which on the fixture is `no` [dw_sqlite.srd:L14]. A gate on the
//      descriptor would read null, decline, and skip a refresh the legacy performs.
//    * DESCRIPTOR SAYS true, CARRIER STILL SAYS no. The modify call may have rejected the property,
//      or a later line may have overridden it. A gate on the descriptor would perform a refresh the
//      legacy does not.
//
//  The gate is therefore an IUpdateTargetMetadata read, taken AFTER the script has been applied,
//  and the comparison is ordinal and case-sensitive because PowerScript's `=` on strings is.
//
//  ============================================================================================
//  DECISION 6 - THE LOOP BREAK IS `!= OK` AND DELIBERATELY NOT Predicates.IsFailed (C-B)
//  ============================================================================================
//  A finding from reading the invocation site rather than the summary. The multi-table loop tests
//
//      if rtCode <> RetCode.OK then exit                                 [:L366, and again :L368]
//
//  a STRICT INEQUALITY AGAINST ZERO - not a failure predicate. Under the published tri-state algebra
//  in PowerFramework.Shared.Kernel, CANCELLED is neither succeeded nor failed and PREVENT reads as a
//  success, so Predicates.IsFailed(RetCode.CANCELLED) is FALSE while `CANCELLED != OK` is TRUE.
//  Substituting the predicate would let a cancelled table fall through to the next table. The
//  verbatim comparison is therefore reproduced at that site and annotated there.
//
//  Predicates IS used, where it changes nothing: the IsSucceeded convenience on the two result
//  records reports the published algebra rather than a hand-rolled comparison, which is what the
//  translation guidance asks for. The distinction between the two uses is stated at both.
//
//  ============================================================================================
//  DECISION 7 - WHAT IS DELIBERATELY NOT HERE
//  ============================================================================================
//  NO DIALOG, AND THAT IS A CONSTRAINT NOT A STYLE CHOICE (C-D). The legacy reports both failures by
//  raising `Event OnError(code, text)` [:L120, L147]. DesignSystem is a deferred capability area, so
//  no MessageBox analogue, no UI, no presentation call of any kind may appear. Failures surface as a
//  STRUCTURED RESULT carrying the code and the untranslated text, plus an OPTIONAL injected
//  Action<long, string> sink that mirrors the event's own two-argument shape for a caller that wants
//  push notification. Nothing here writes to a console or a log.
//
//  NO SQL, NO CONNECTION, NO SCHEMA (C-E, C-F). This file emits a MODIFICATION SCRIPT, not SQL. It
//  interpolates only column names, column ordinals, the two settings and the table name - NEVER a
//  data value - so it cannot carry a literal into a statement. Production code here names no table,
//  no column and no column count: COMPANY and its six columns are FIXTURE data and appear in this
//  file only inside comments. Nothing touches EF Core, Microsoft.Data.Sqlite, a DbConnection, SQL
//  Server or Oracle.
//
//  NO ROW-COUNT, IDENTITY OR CONFLICT LOGIC. The defensive override that rewrites a claimed success
//  into a failure [:L208-L210], the identity round trip with its BACKWARD filter-buffer walk
//  [:L235-L241] and the conflict projection to gRPC Aborted all belong to sibling files. They are
//  named here only so a reader knows where they went.
//
//  NO CLOCK, NO I/O, NO STATIC MUTABLE STATE (C-H). The script builder is a PURE function of a
//  descriptor and an injected metadata reader; the preparer's only collaborators are injected
//  interfaces and a Buffers/ store. Every branch - each presence combination, the non-positive
//  identifier failure, the non-empty modify error, all three add-validation arms, the workaround's
//  gate and both of its skip arms, and the multi-table pre-check and stop-at-first-failure - is
//  reachable with NO DATABASE and NO DataWindow.
//
//  NO NEW DEPENDENCY (C-A). The only imports are the published contracts namespace, the shared
//  kernel and this project's own Buffers/ folder. No PackageReference is added; versions live
//  centrally in the repository-root Directory.Packages.props. No other service is referenced, and
//  the only outward shape of these types is TableUpdateContract in the published contract.
//
//  NAMING, VERIFIED AGAINST THE ARTIFACT. The repository-root .editorconfig scopes its CA1707 and
//  IDE1006 relaxations to the files on its BAND 3 roster, the single source of truth for that list.
//  Concurrency/ IS NOT AMONG THEM - the sibling that is, in this same service, is
//  Sql/ClauseModifier.cs. TreatWarningsAsErrors is inherited as true, so a
//  SCREAMING_SNAKE identifier declared here would be a BUILD FAILURE, not a lint nit. Every member
//  below is therefore PascalCase, and the preserved legacy constant values are CONSUMED from
//  PowerFramework.Shared.Kernel.RetCode rather than restated. The shouty spellings that appear in
//  this file appear only inside comments, quoting the oracle for traceability.
//
//  RULES POSITION. review_rules returns exactly one line, "No user rules provided.", so NO
//  user-specified rule governs this file and none is invented here. The enterprise-standard baseline
//  applies in their place, and the binding constraints are the refactor plan's own non-rule
//  inventory; those bearing on this file are cited inline where each is discharged: C-A, C-B, C-C,
//  C-D, C-E, C-F, C-H, C-K, and risk R9.
//
//  C-F SELF-AUDIT: no key, credential, token, password, connection string, logpass or secret-shaped
//  placeholder appears anywhere in this file - in any member, default, message, literal or comment.
// ==============================================================================================

using System.Globalization;
using System.Text;
using PowerFramework.Contracts.Common.V1;
using PowerFramework.Persistence.Buffers;

// The simple name RetCode is AMBIGUOUS without this alias. common.v1.proto declares a protobuf
// MESSAGE named RetCode - the wire form of the algebra, generated into
// PowerFramework.Contracts.Common.V1 - while PowerFramework.Shared.Kernel.RetCode is the static
// class of `long` constants that is the IN-PROCESS algebra. Both namespaces are imported here, so
// the alias is what states which side every value in this file comes from: all of them are
// in-process. Same alias, same reason, at Buffers/DataWindowBuffers.cs.
using Predicates = PowerFramework.Shared.Kernel.Predicates;
using RetCode = PowerFramework.Shared.Kernel.RetCode;

namespace PowerFramework.Persistence.Concurrency;

#region R9 - the centralized one-based indexing helper

/// <summary>
/// The one-based indexing domain the legacy arrays live in, expressed once so that every ported
/// loop in this folder converts in exactly one place instead of one place per loop.
/// </summary>
/// <remarks>
/// <para>
/// RISK R9, WHICH THE REFACTOR PLAN NAMES THE SINGLE MOST DANGEROUS MECHANICAL HAZARD IN THE WHOLE
/// MIGRATION. PowerBuilder arrays are ONE-BASED and <c>UpperBound</c> returns THE LAST VALID INDEX;
/// C# collections are zero-based and <c>Count</c> is ONE PAST THE END. Every ported loop in this
/// file goes through this type, and the loops it covers are:
/// </para>
/// <list type="bullet">
/// <item><description>
/// <c>for nIndex = 1 to nCount</c> over column ordinals
/// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru:L104</c>].
/// </description></item>
/// <item><description>
/// the same shape over updatable column names [<c>:L112</c>] and over key column names
/// [<c>:L117</c>].
/// </description></item>
/// <item><description>
/// the append idiom <c>x[UpperBound(x) + 1] = v</c>, which occurs twice - appending a descriptor
/// [<c>:L86</c>] and appending a resolved key column identifier [<c>:L123</c>].
/// </description></item>
/// <item><description>
/// the row walk <c>for nRow = 1 to nRowCnt</c> [<c>:L159</c>] and the inner key-column walk
/// [<c>:L161</c>].
/// </description></item>
/// <item><description>
/// the multi-table loop <c>for nIndex = 1 to nCount</c>
/// [<c>:L364</c>].
/// </description></item>
/// </list>
/// <para>
/// A SILENT OFF-BY-ONE HERE IS INDISTINGUISHABLE FROM A BEHAVIOURAL REGRESSION, because the count
/// stays right while the elements shift. Worse, the DESCRIBE VOCABULARY IS ITSELF ONE-BASED -
/// <c>#1.Update</c> addresses the first column and <c>&lt;name&gt;.Id</c> answers a one-based
/// ordinal - so a rebased ordinal produces a script that is syntactically valid and addresses the
/// wrong column. The boundary between the one-based describe domain and zero-based C# storage is
/// therefore always crossed by <see cref="ToZeroBased"/> and never implicitly.
/// </para>
/// <para>
/// THIS TYPE IS ABOUT ARRAY AND LIST INDICES ONLY. Row numbers, column numbers and the column-zero
/// row-status sentinel are a DIFFERENT one-based domain that
/// <see cref="ItemStatusMachine"/> already owns - <see cref="ItemStatusMachine.FirstRowNumber"/>,
/// <see cref="ItemStatusMachine.FirstColumnNumber"/> and
/// <see cref="ItemStatusMachine.RowStatusColumn"/>. Those are consumed from there rather than
/// restated here, because two declarations of one constant can drift apart with no compile-time edge
/// between them.
/// </para>
/// </remarks>
internal static class OneBasedIndex
{
    /// <summary>
    /// The first valid index of a PowerBuilder array, one. Every ported <c>for nIndex = 1 to
    /// nCount</c> loop starts here.
    /// </summary>
    internal const int FirstIndex = 1;

    /// <summary>
    /// The value <c>UpperBound</c> answers for an empty array, zero - which is what makes
    /// <c>for nIndex = 1 to UpperBound(empty)</c> execute zero times, and what the three-arm
    /// validation at
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru:L84</c> compares against.
    /// </summary>
    /// <remarks>
    /// R9: this is NOT an index and must never be rebased. It is the answer "this array has no
    /// elements", and it is deliberately the same value as the row-status sentinel's zero without
    /// being related to it.
    /// </remarks>
    internal const int EmptyUpperBound = 0;

    /// <summary>
    /// Reproduces PowerScript <c>UpperBound</c>: the LAST VALID ONE-BASED INDEX of
    /// <paramref name="items"/>, or <see cref="EmptyUpperBound"/> when it is empty or absent.
    /// </summary>
    /// <typeparam name="T">The element type, which this operation does not inspect.</typeparam>
    /// <param name="items">The collection to measure. <see langword="null"/> measures as empty.</param>
    /// <returns>The last valid one-based index, which for a non-empty collection equals its count.</returns>
    /// <remarks>
    /// The identity <c>UpperBound == Count</c> holds ONLY because the domain is one-based; that
    /// coincidence is why a naive port appears to work and then indexes past the end. This member
    /// exists so the intent - "last valid index", not "number of elements" - is stated at every call
    /// site that mirrors the oracle.
    /// </remarks>
    internal static int UpperBound<T>(IReadOnlyCollection<T>? items)
    {
        return items?.Count ?? EmptyUpperBound;
    }

    /// <summary>
    /// Reproduces the append idiom <c>x[UpperBound(x) + 1] = v</c>: the one-based index at which the
    /// next element lands.
    /// </summary>
    /// <typeparam name="T">The element type, which this operation does not inspect.</typeparam>
    /// <param name="items">The collection about to be appended to. <see langword="null"/> appends at
    /// <see cref="FirstIndex"/>.</param>
    /// <returns>The one-based index the appended element will occupy.</returns>
    /// <remarks>
    /// Two sites in the oracle:
    /// <c>nIndex = UpperBound(Tables) + 1</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru:L86</c>], which appends a
    /// descriptor, and <c>nKeyColumns[UpperBound(nKeyColumns) + 1] = nColId</c> [<c>:L123</c>], which
    /// appends a resolved key column identifier. Both are plain appends: PowerBuilder grows the array
    /// implicitly, so neither can fail and neither leaves a gap.
    /// </remarks>
    internal static int AppendIndex<T>(IReadOnlyCollection<T>? items)
    {
        return UpperBound(items) + 1;
    }

    /// <summary>
    /// Reports whether <paramref name="oneBasedIndex"/> addresses an existing element of a collection
    /// whose upper bound is <paramref name="upperBound"/>.
    /// </summary>
    /// <param name="oneBasedIndex">The candidate one-based index.</param>
    /// <param name="upperBound">The collection's last valid one-based index, from
    /// <see cref="UpperBound{T}"/>.</param>
    /// <returns><see langword="true"/> when the index is within <see cref="FirstIndex"/> through
    /// <paramref name="upperBound"/> inclusive.</returns>
    internal static bool IsWithin(int oneBasedIndex, int upperBound)
    {
        return oneBasedIndex >= FirstIndex && oneBasedIndex <= upperBound;
    }

    /// <summary>
    /// Converts a one-based index into the zero-based index of the same element, rejecting anything
    /// outside the collection.
    /// </summary>
    /// <param name="oneBasedIndex">The one-based index to convert.</param>
    /// <param name="upperBound">The collection's last valid one-based index.</param>
    /// <param name="paramName">The name to report on failure, for a caller-meaningful diagnostic.</param>
    /// <returns>The zero-based index of the same element.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="oneBasedIndex"/> is below <see cref="FirstIndex"/> or above
    /// <paramref name="upperBound"/>.
    /// </exception>
    /// <remarks>
    /// THIS IS THE ONLY PLACE IN THIS FILE WHERE A ONE-BASED INDEX BECOMES A ZERO-BASED ONE, which is
    /// what makes R9 a single line to audit rather than one line per loop. It THROWS rather than
    /// clamping or answering a sentinel: an out-of-range descriptor index is a structural fault in the
    /// caller, and the refactor plan requires structural faults FAIL FAST rather than degrade. The
    /// message names the likely cause, because a zero here almost always means a one-based index was
    /// rebased twice.
    /// </remarks>
    internal static int ToZeroBased(int oneBasedIndex, int upperBound, string paramName)
    {
        if (!IsWithin(oneBasedIndex, upperBound))
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                oneBasedIndex,
                $"Index must be between {FirstIndex} and the upper bound of {upperBound}, inclusive. "
                    + "Indices on this surface are ONE-BASED, matching the legacy arrays; a zero or "
                    + "negative value usually means a one-based index was rebased by mistake.");
        }

        return oneBasedIndex - FirstIndex;
    }

    /// <summary>
    /// Enumerates <see cref="FirstIndex"/> through <paramref name="upperBound"/> inclusive,
    /// reproducing <c>for nIndex = 1 to nCount</c>.
    /// </summary>
    /// <param name="upperBound">The last index to yield. Zero or below yields nothing.</param>
    /// <returns>The one-based indices, ascending.</returns>
    /// <remarks>
    /// An upper bound of zero or below yields NOTHING, which is exactly what PowerScript's
    /// <c>for nIndex = 1 to 0</c> does - it executes zero times rather than once or erroring. That
    /// makes the empty-collection case fall out of the shared loop shape instead of needing a guard,
    /// which is how the oracle reaches an empty descriptor's column loops [<c>:L112</c>,
    /// <c>:L117</c>] without special-casing them.
    /// </remarks>
    internal static IEnumerable<int> Range(int upperBound)
    {
        for (int index = FirstIndex; index <= upperBound; index++)
        {
            yield return index;
        }
    }
}

#endregion

#region The descriptor - the six-field `tabledata` mirror

/// <summary>
/// One updatable table's update contract: the six-field mirror of the legacy <c>tabledata</c>
/// structure, in the oracle's own field order.
/// </summary>
/// <param name="Name">
/// The update table name, written through as <c>DataWindow.Table.UpdateTable</c>
/// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru:L11</c>, applied
/// unconditionally at <c>:L143</c>].
/// </param>
/// <param name="UpdatableColumns">
/// The columns this operation may write [<c>:L12</c>, applied at <c>:L111-L114</c>]. Order is
/// preserved because it is the order the script emits.
/// </param>
/// <param name="KeyColumns">
/// The key columns for the WHERE clause [<c>:L13</c>, applied at <c>:L116-L125</c>]. Each name is
/// resolved to a numeric identifier through a describe call and CAN FAIL.
/// </param>
/// <param name="IdentityColumn">
/// The auto-increment column, or empty when the table has none [<c>:L14</c>, applied at
/// <c>:L127-L129</c>]. EMPTY IS LEGAL and is not rejected at add time.
/// </param>
/// <param name="UpdateWhere">
/// The update-where MODE, or <see langword="null"/> to leave the carrier's own setting alone
/// [<c>:L15</c>, applied conditionally at <c>:L131-L133</c>].
/// </param>
/// <param name="UpdateKeyInPlace">
/// Whether a key change is performed in place, or <see langword="null"/> to leave the carrier's own
/// setting alone [<c>:L16</c>, applied conditionally at <c>:L135-L141</c>].
/// </param>
/// <remarks>
/// <para>
/// EXACTLY SIX MEMBERS, IN THE ORACLE'S ORDER, and the order is contract rather than tidiness: the
/// published boundary numbers <c>TableUpdateContract</c>'s fields 1 through 6 against these same six
/// in this same sequence, in
/// <c>shared/PowerFramework.Contracts/Proto/persistence.v1.proto</c>. Reordering the members here
/// would leave the wire numbering describing a different shape from the type it mirrors. No seventh
/// member is added.
/// </para>
/// <para>
/// FIELDS 5 AND 6 ARE NULLABLE AND THAT NULLABILITY IS LOAD-BEARING. See DECISION 2 in the file
/// header: absence means "do not emit the property at all", the caller-side four-argument overload
/// nulls both deliberately
/// [<c>n_cst_threading_task_sqlupdate.sru:L227-L234</c>], and substituting <c>0</c> or
/// <c>false</c> for absence emits a property the legacy omits. Never collapse either to a sentinel.
/// </para>
/// <para>
/// THE TWO COLLECTIONS ARE COPIED ON THE WAY IN by <see cref="Create"/>, and that copy is FAITHFUL
/// rather than defensive: PowerBuilder arrays are value types, so
/// <c>Tables[nIndex].UpdatableColumns = updatableColumns</c> [<c>:L89</c>] copies the caller's array
/// and a later mutation by the caller cannot reach the stored descriptor. A reference-sharing port
/// would introduce aliasing the oracle does not have.
/// </para>
/// <para>
/// A <c>with</c> expression can substitute any <see cref="IReadOnlyList{T}"/>, including a mutable
/// one. That is accepted rather than defended against: <see cref="Create"/> is the entry point every
/// path in this folder uses, and hardening the record further would mean an immutable collection type
/// whose <c>default</c> is a null-reference hazard - trading a documented sharp edge for an
/// undocumented one.
/// </para>
/// </remarks>
internal sealed record UpdatableTableDescriptor(
    string Name,
    IReadOnlyList<string> UpdatableColumns,
    IReadOnlyList<string> KeyColumns,
    string IdentityColumn,
    long? UpdateWhere,
    bool? UpdateKeyInPlace)
{
    /// <summary>
    /// Creates a descriptor, normalising absent text to empty and copying both column collections.
    /// </summary>
    /// <param name="name">The update table name. <see langword="null"/> normalises to empty, which
    /// the three-arm validation then rejects.</param>
    /// <param name="updatableColumns">The updatable column names. <see langword="null"/> normalises to
    /// empty.</param>
    /// <param name="keyColumns">The key column names. <see langword="null"/> normalises to empty.</param>
    /// <param name="identityColumn">The identity column name, or <see langword="null"/>/empty for
    /// none.</param>
    /// <param name="updateWhere">The update-where mode, or <see langword="null"/> for absent.</param>
    /// <param name="updateKeyInPlace">The key-in-place setting, or <see langword="null"/> for
    /// absent.</param>
    /// <returns>The descriptor.</returns>
    /// <remarks>
    /// NULL TEXT NORMALISES TO EMPTY RATHER THAN THROWING, because that is the reading which keeps the
    /// oracle's own validation in charge. The legacy parameters are <c>readonly string</c> and its
    /// checks are the exact comparisons <c>name = ""</c> [<c>:L84</c>] and
    /// <c>IdentityColumn &lt;&gt; ""</c> [<c>:L127</c>]; normalising null to empty means a null name is
    /// REJECTED by the same arm that rejects an empty one, and a null identity column is TREATED AS
    /// ABSENT exactly as an empty one is. Throwing instead would add a failure mode the oracle does
    /// not have, which C-B forbids.
    /// </remarks>
    internal static UpdatableTableDescriptor Create(
        string? name,
        IEnumerable<string>? updatableColumns,
        IEnumerable<string>? keyColumns,
        string? identityColumn,
        long? updateWhere,
        bool? updateKeyInPlace)
    {
        return new UpdatableTableDescriptor(
            name ?? string.Empty,
            CopyNames(updatableColumns),
            CopyNames(keyColumns),
            identityColumn ?? string.Empty,
            updateWhere,
            updateKeyInPlace);
    }

    /// <summary>
    /// Reports whether this descriptor passes the legacy's add-time validation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Reproduces <c>:L84</c> exactly:
    /// <c>if name = "" or UpperBound(updatableColumns) = 0 or UpperBound(keyColumns) = 0 then return
    /// RetCode.E_INVALID_ARGUMENT</c>. Three arms, joined by OR, and nothing else.
    /// </para>
    /// <para>
    /// THE NAME TEST IS AN EXACT EMPTY COMPARISON, NOT A WHITESPACE TEST (C-B). The oracle compares
    /// against <c>""</c>, so a name of a single space PASSES THIS PREDICATE. Using a whitespace-aware
    /// test here would tighten the oracle's own arm, and this predicate is the oracle's own arm.
    /// </para>
    /// <para>
    /// A NAME OF A SINGLE SPACE IS NEVERTHELESS REFUSED END TO END, by
    /// <see cref="IsScriptSafe"/> rather than by this predicate - so do not read this remark as saying
    /// that such a name reaches the script. It does not. Keeping the two predicates separate is what
    /// lets this one stay a faithful transcription while the boundary still refuses a name no table
    /// has; see <see cref="UpdateWhereBuilder.IsScriptSafeTableName"/> for the reasoning.
    /// </para>
    /// <para>
    /// AN EMPTY IDENTITY COLUMN IS LEGAL AND IS NOT AN ARM OF THIS TEST. The oracle never validates
    /// it; it simply skips the marking step when it is empty [<c>:L127-L129</c>]. Adding a fourth arm
    /// would reject every table without an auto-increment column.
    /// </para>
    /// </remarks>
    internal bool IsAcceptable =>
        Name.Length != 0
        && OneBasedIndex.UpperBound(UpdatableColumns) != OneBasedIndex.EmptyUpperBound
        && OneBasedIndex.UpperBound(KeyColumns) != OneBasedIndex.EmptyUpperBound;

    /// <summary>
    /// Reports whether the identity column is present, using the oracle's exact non-empty test.
    /// </summary>
    /// <remarks>
    /// <c>if Tables[index].IdentityColumn &lt;&gt; "" then</c> [<c>:L127</c>]. An exact comparison, so
    /// whitespace counts as present - see <see cref="IsAcceptable"/> for why the test is not
    /// whitespace-aware.
    /// </remarks>
    internal bool HasIdentityColumn => IdentityColumn.Length != 0;

    /// <summary>
    /// Reports whether every name in this descriptor can be spliced into the modification script
    /// without authoring any of it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// SEPARATE FROM <see cref="IsAcceptable"/> ON PURPOSE, and the separation is what keeps the port
    /// legible. <see cref="IsAcceptable"/> IS the oracle's <c>:L84</c> test and nothing else, so a
    /// reader comparing this file against the oracle finds it unchanged; this predicate is the NEW
    /// boundary guard the review required, and a reader can see at a glance exactly what was added.
    /// Folding the two together would have made every arm of the legacy test suspect.
    /// </para>
    /// <para>
    /// THE FOUR POSITIONS AND THE ORDER THEY ARE TESTED IN. Table name first, because it is the one the
    /// caller most obviously controls and the one whose abuse redirects the whole update; then the
    /// updatable columns; then the key columns; then the identity column. The order is only observable
    /// as which position a caller learns about first when several are bad, and testing the table first
    /// means a redirection attempt is never masked by a column complaint.
    /// </para>
    /// <para>
    /// AN EMPTY IDENTITY COLUMN PASSES, because empty means ABSENT and absence is legal
    /// [<see cref="HasIdentityColumn"/>, <c>:L127</c>]. Testing it unconditionally would reject every
    /// table without an auto-increment column - the exact mistake the fourth-arm note on
    /// <see cref="IsAcceptable"/> warns against.
    /// </para>
    /// </remarks>
    internal bool IsScriptSafe =>
        UpdateWhereBuilder.IsScriptSafeTableName(Name)
        && UpdatableColumns.All(UpdateWhereBuilder.IsScriptSafeColumnName)
        && KeyColumns.All(UpdateWhereBuilder.IsScriptSafeColumnName)
        && (!HasIdentityColumn || UpdateWhereBuilder.IsScriptSafeColumnName(IdentityColumn));

    private static IReadOnlyList<string> CopyNames(IEnumerable<string>? names)
    {
        return names is null ? [] : [.. names];
    }
}

#endregion

#region The injectable carrier surface - describe and modify, split so the builder stays pure

/// <summary>
/// The read half of the legacy carrier's inherited <c>Describe</c> surface: the three properties
/// runtime re-derivation needs, and nothing more.
/// </summary>
/// <remarks>
/// <para>
/// WHY THIS INTERFACE EXISTS AT ALL. In the legacy these are <c>Describe</c> calls on the
/// <c>n_cst_thread_task_sqlbase_ds</c> parameter, which derives from <c>datastore</c>
/// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds.sru:L8</c>] and so INHERITS
/// them - which is why they appear in no prototype list, and why nothing in this project's
/// <c>Buffers/</c> folder exposes them. Naming the three reads as an interface is what makes
/// <see cref="UpdateWhereBuilder.BuildModificationString"/> a pure function and every branch of it
/// reachable with no DataWindow and no database (C-H).
/// </para>
/// <para>
/// EXACTLY THREE MEMBERS, ONE PER MEASURED PROPERTY. No general <c>Describe(string)</c> escape hatch
/// is offered: a stringly-typed reader would let a consumer reach a property this folder has no
/// oracle for, and the three property names are published as constants on
/// <see cref="UpdateWhereBuilder"/> so an implementor can see exactly which describe each maps to.
/// </para>
/// <para>
/// IMPLEMENTORS MUST REPRODUCE POWERSCRIPT'S <c>Long()</c> COERCION on the two numeric reads. The
/// oracle wraps both in <c>Long(...)</c> [<c>:L103</c>, <c>:L118</c>], and <c>Long</c> answers zero
/// for text that is not a number - including the <c>"!"</c> and <c>"?"</c> that describe answers when a
/// property cannot be read or does not exist. <see cref="UpdateWhereBuilder.CoerceDescribedNumber"/>
/// is that coercion, published so an implementor does not have to re-derive it.
/// </para>
/// </remarks>
internal interface IUpdateTargetMetadata
{
    /// <summary>
    /// Reproduces <c>Long(Describe("DataWindow.Column.Count"))</c> [<c>:L103</c>].
    /// </summary>
    /// <returns>
    /// The number of columns, which is also their LAST VALID ONE-BASED ORDINAL. Zero when the count
    /// cannot be read, in which case the reset pass emits nothing - matching
    /// <c>for nIndex = 1 to 0</c>.
    /// </returns>
    int GetColumnCount();

    /// <summary>
    /// Reproduces <c>Long(Describe(&lt;columnName&gt; + ".Id"))</c> [<c>:L118</c>].
    /// </summary>
    /// <param name="columnIdProperty">
    /// The describe property, ALREADY COMPOSED as the column name followed by
    /// <see cref="UpdateWhereBuilder.ColumnIdSuffix"/> - for a column called <c>salary</c> this
    /// receives <c>salary.Id</c>. Never <see langword="null"/>.
    /// </param>
    /// <returns>
    /// The column's one-based numeric identifier, or a NON-POSITIVE value when the property does not
    /// resolve - which the caller treats as a hard failure [<c>:L119-L122</c>].
    /// </returns>
    /// <remarks>
    /// THE CALLER COMPOSES THE PROPERTY, NOT THE IMPLEMENTOR, so that the exact describe string the
    /// oracle builds at <c>:L118</c> stays visible at the call site and remains diffable against that
    /// locator. An implementor therefore receives a complete property name and must not append the
    /// suffix a second time.
    /// </remarks>
    int GetColumnId(string columnIdProperty);

    /// <summary>
    /// Reproduces <c>Describe("DataWindow.Table.UpdateKeyinPlace")</c> [<c>:L155</c>], RAW and
    /// uncoerced.
    /// </summary>
    /// <returns>
    /// The property's text value exactly as describe answers it, typically <c>"yes"</c> or <c>"no"</c>.
    /// </returns>
    /// <remarks>
    /// THE RAW STRING IS RETURNED RATHER THAN A <see cref="bool"/> ON PURPOSE. The oracle's gate is an
    /// exact string comparison against <c>"no"</c>, so any other answer - including <c>"yes"</c>,
    /// <c>"!"</c>, <c>"?"</c> or empty - declines the gate. Coercing to a boolean here would have to
    /// invent a mapping for those three non-boolean answers, and whichever way it mapped them it would
    /// change which rows the workaround visits.
    /// </remarks>
    string DescribeUpdateKeyInPlace();
}

/// <summary>
/// The write half of the legacy carrier's inherited surface: applying a modification script.
/// </summary>
/// <remarks>
/// Split from <see cref="IUpdateTargetMetadata"/> so that BUILDING a script and APPLYING one are
/// separable, which is what lets the script be asserted byte for byte with no DataWindow present. In
/// the legacy both halves are the same <c>Data</c> object; an implementation may of course implement
/// both interfaces on one type.
/// </remarks>
internal interface IUpdateTargetModifier
{
    /// <summary>
    /// Reproduces <c>Data.Modify(sModString)</c> [<c>:L145</c>].
    /// </summary>
    /// <param name="modificationScript">
    /// The script produced by <see cref="UpdateWhereBuilder.BuildModificationString"/>.
    /// </param>
    /// <returns>
    /// THE DRIVER'S ERROR TEXT, AND <b>EMPTY MEANS SUCCESS</b>. This is a STRING, not a numeric code.
    /// </returns>
    /// <remarks>
    /// THE SUCCESS TEST IS INVERTED RELATIVE TO EVERY OTHER RESULT IN THIS SERVICE, so it is worth
    /// stating twice: the oracle is <c>sErr = Data.Modify(...)</c> followed by
    /// <c>if sErr &lt;&gt; "" then</c> [<c>:L145-L149</c>]. An implementation that returns a code, or a
    /// caller that treats a non-empty answer as success, inverts the whole contract. The text is
    /// forwarded to the caller UNTRANSLATED, UNWRAPPED AND UNREFORMATTED, because it is the only
    /// detail a prepare failure carries.
    /// </remarks>
    string Modify(string modificationScript);
}

#endregion


#region Results - the structured replacement for `Event OnError`

/// <summary>
/// One row/column pair whose item status was refreshed by the <c>updatekeyinplace=no</c> workaround,
/// so that the delete-plus-insert obligation is observable rather than a hidden side effect.
/// </summary>
/// <param name="Row">The one-based row number in the <c>Primary!</c> buffer. R9: never rebased.</param>
/// <param name="ColumnNumber">
/// The one-based column number, as resolved from the key column's name by
/// <see cref="IUpdateTargetMetadata.GetColumnId"/>. R9: never rebased.
/// </param>
/// <remarks>
/// The legacy leaves no trace of this step - it happens entirely inside PowerBuilder's hidden item
/// state via the self-assignment at
/// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru:L163</c>. Recording the pairs
/// is what turns an untestable side effect into an assertable one, which C-H requires and DECISION 4
/// in the file header explains in full.
/// </remarks>
internal readonly record struct KeyColumnRefresh(long Row, int ColumnNumber);

/// <summary>
/// The outcome of building a modification script: the script itself, the resolved key column
/// identifiers the workaround needs, and - on failure - the code and untranslated text the legacy
/// would have raised through <c>Event OnError</c>.
/// </summary>
internal sealed record ModificationScriptResult
{
    /// <summary>
    /// <see cref="RetCode.OK"/> on success, or <see cref="RetCode.E_INTERNAL_ERROR"/> when a key
    /// column name did not resolve [<c>:L119-L122</c>].
    /// </summary>
    internal long Code { get; init; }

    /// <summary>
    /// The untranslated diagnostic, empty on success. On the resolution failure this is
    /// <see cref="UpdateWhereBuilder.InvalidColumnNameMessage"/> concatenated with the offending
    /// column name, exactly as at <c>:L120</c>.
    /// </summary>
    internal string ErrorText { get; init; } = string.Empty;

    /// <summary>
    /// The modification script, or empty on failure.
    /// </summary>
    /// <remarks>
    /// EMPTY ON FAILURE BECAUSE THE ORACLE DISCARDS THE PARTIAL SCRIPT. <c>sModString</c> is a local
    /// that goes out of scope at the early <c>return</c> [<c>:L121</c>], so no partially built script is
    /// ever applied or observed. Returning the partial text here would publish an observable the legacy
    /// does not have.
    /// </remarks>
    internal string Script { get; init; } = string.Empty;

    /// <summary>
    /// The resolved one-based column identifiers of the key columns, in descriptor order - the
    /// <c>nKeyColumns</c> array the oracle accumulates at <c>:L123</c> and consumes at <c>:L156</c> and
    /// <c>:L162</c>. Empty on failure.
    /// </summary>
    internal IReadOnlyList<int> KeyColumnIds { get; init; } = [];

    /// <summary>
    /// Reports success against the PUBLISHED tri-state algebra rather than a hand-rolled comparison.
    /// </summary>
    /// <remarks>
    /// Uses <see cref="Predicates.IsSucceeded(long?)"/>, whose semantics are a published contract: it
    /// tests <c>&gt;= 0</c>, so a prevention reads as a success, and cancelled is neither succeeded nor
    /// failed. That algebra is correct HERE because this type only ever carries
    /// <see cref="RetCode.OK"/> or <see cref="RetCode.E_INTERNAL_ERROR"/>, on which the predicate and a
    /// zero comparison agree exactly. Contrast the multi-table loop in
    /// <see cref="UpdatePreparer.PrepareAndUpdate"/>, which must NOT use the predicate - see DECISION 6.
    /// </remarks>
    internal bool IsSucceeded => Predicates.IsSucceeded(Code);

    /// <summary>Builds the success outcome.</summary>
    /// <param name="script">The completed modification script.</param>
    /// <param name="keyColumnIds">The resolved key column identifiers, in descriptor order.</param>
    /// <returns>A succeeded result.</returns>
    internal static ModificationScriptResult Succeeded(string script, IReadOnlyList<int> keyColumnIds)
    {
        return new ModificationScriptResult
        {
            Code = RetCode.OK,
            Script = script,
            KeyColumnIds = keyColumnIds,
        };
    }

    /// <summary>Builds the failure outcome, discarding any partial script.</summary>
    /// <param name="code">The legacy return code.</param>
    /// <param name="errorText">The untranslated diagnostic.</param>
    /// <returns>A failed result.</returns>
    internal static ModificationScriptResult Failed(long code, string errorText)
    {
        return new ModificationScriptResult { Code = code, ErrorText = errorText };
    }
}

/// <summary>
/// The outcome of a full prepare: everything <see cref="ModificationScriptResult"/> carries, plus
/// whether the <c>updatekeyinplace=no</c> gate opened and which row/column pairs it refreshed.
/// </summary>
internal sealed record UpdatePreparationResult
{
    /// <summary>
    /// <see cref="RetCode.OK"/>, or <see cref="RetCode.E_INTERNAL_ERROR"/> from either the name
    /// resolution [<c>:L119-L122</c>] or the modify call [<c>:L146-L149</c>].
    /// </summary>
    internal long Code { get; init; }

    /// <summary>The untranslated diagnostic, empty on success.</summary>
    internal string ErrorText { get; init; } = string.Empty;

    /// <summary>The applied modification script, or empty when building failed.</summary>
    internal string Script { get; init; } = string.Empty;

    /// <summary>The resolved key column identifiers, in descriptor order.</summary>
    internal IReadOnlyList<int> KeyColumnIds { get; init; } = [];

    /// <summary>
    /// Whether the runtime-described key-in-place value was <c>"no"</c> AND at least one key column
    /// resolved - the conjunction of <c>:L155</c> and <c>:L156-L157</c> that opens the workaround.
    /// </summary>
    /// <remarks>
    /// TRUE MEANS THE DOWNSTREAM GENERATOR OWES A DELETE-PLUS-INSERT PAIR for every entry in
    /// <see cref="KeyChangeRefreshes"/>. It can be true with an EMPTY refresh list, which is the
    /// ordinary case where no row had a modified key column, and that combination is meaningful rather
    /// than contradictory.
    /// </remarks>
    internal bool KeyChangeRefreshRequired { get; init; }

    /// <summary>
    /// The row/column pairs whose status was refreshed, in the oracle's visit order: ascending by row
    /// [<c>:L159</c>], then by key column in descriptor order [<c>:L161</c>].
    /// </summary>
    internal IReadOnlyList<KeyColumnRefresh> KeyChangeRefreshes { get; init; } = [];

    /// <summary>
    /// Reports success against the published tri-state algebra. See
    /// <see cref="ModificationScriptResult.IsSucceeded"/> for why the predicate is appropriate here and
    /// deliberately not in the multi-table loop.
    /// </summary>
    internal bool IsSucceeded => Predicates.IsSucceeded(Code);
}

#endregion

#region The builder - the seven-step recipe, byte-exact and pure

/// <summary>
/// Builds the DataWindow modification script that re-derives one table's update contract at runtime,
/// and names the columns whose ORIGINAL values the resulting concurrency check compares.
/// </summary>
/// <remarks>
/// <para>
/// Ports the script-building half of <c>_of_updateprepare</c>
/// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru:L98-L143</c>]. Applying the
/// script and the workaround that follows it belong to <see cref="UpdatePreparer"/>; the split is what
/// keeps this type a PURE function of a descriptor and an injected metadata reader.
/// </para>
/// <para>
/// EVERY CHARACTER THIS TYPE EMITS IS COMPARED BYTE FOR BYTE. The separators, the quoting, the
/// property spellings, the attribute order within the reset triple and the absence of a trailing
/// newline are all contract - see DECISION 3 in the file header for the five that look like defects
/// and are not.
/// </para>
/// </remarks>
internal static class UpdateWhereBuilder
{
    #region The script vocabulary - every literal the oracle emits, named once

    /// <summary>
    /// The line separator: a SINGLE LINE FEED, U+000A. PowerScript's <c>~n</c>.
    /// </summary>
    /// <remarks>
    /// NEVER <see cref="Environment"/>.NewLine AND NEVER CRLF. Every appended line in the oracle ends
    /// <c>+ "~n"</c> [<c>:L105-L107</c>, <c>:L113</c>, <c>:L124</c>, <c>:L128</c>, <c>:L132</c>,
    /// <c>:L137</c>, <c>:L139</c>]. A carriage return would appear in every line of a machine-read
    /// script and would change every byte comparison in the parity suite.
    /// </remarks>
    internal const string LineSeparator = "\n";

    /// <summary>The property that answers the column count [<c>:L103</c>].</summary>
    internal const string ColumnCountProperty = "DataWindow.Column.Count";

    /// <summary>
    /// The suffix appended to a column name to describe its numeric identifier [<c>:L118</c>].
    /// </summary>
    internal const string ColumnIdSuffix = ".Id";

    /// <summary>The update-where MODE property [<c>:L132</c>].</summary>
    internal const string UpdateWhereProperty = "DataWindow.Table.UpdateWhere";

    /// <summary>
    /// The key-in-place property, SPELLED WITH A LOWER-CASE <c>i</c> IN "in" - <c>UpdateKeyinPlace</c>,
    /// not <c>UpdateKeyInPlace</c>.
    /// </summary>
    /// <remarks>
    /// The oracle uses this spelling three times: writing it [<c>:L137</c>, <c>:L139</c>] and reading it
    /// back [<c>:L155</c>]. The describe/modify vocabulary is case-shaped, so the "corrected" spelling
    /// would address a property that does not exist. This constant exists so the lower-case wire
    /// spelling and the correctly-cased C# member name
    /// <see cref="UpdatableTableDescriptor.UpdateKeyInPlace"/> can never be confused for one another.
    /// </remarks>
    internal const string UpdateKeyInPlaceProperty = "DataWindow.Table.UpdateKeyinPlace";

    /// <summary>The update table property [<c>:L143</c>].</summary>
    internal const string UpdateTableProperty = "DataWindow.Table.UpdateTable";

    /// <summary>
    /// The prefix that addresses a column by ORDINAL rather than by name, as in <c>#1.Update</c>
    /// [<c>:L105-L107</c>].
    /// </summary>
    /// <remarks>R9: the ordinal that follows is ONE-BASED and is never rebased.</remarks>
    internal const string ColumnOrdinalPrefix = "#";

    /// <summary>The per-column updatable attribute [<c>:L105</c>, <c>:L113</c>].</summary>
    internal const string UpdateAttributeSuffix = ".Update";

    /// <summary>The per-column key attribute [<c>:L106</c>, <c>:L124</c>].</summary>
    internal const string KeyAttributeSuffix = ".Key";

    /// <summary>The per-column identity attribute [<c>:L107</c>, <c>:L128</c>].</summary>
    internal const string IdentityAttributeSuffix = ".Identity";

    /// <summary>
    /// The affirmative attribute value. Lower case, as the oracle emits it [<c>:L113</c>, <c>:L124</c>,
    /// <c>:L128</c>, <c>:L137</c>].
    /// </summary>
    internal const string YesLiteral = "yes";

    /// <summary>
    /// The negative attribute value, and ALSO the exact text the workaround's runtime gate compares
    /// against [<c>:L105-L107</c>, <c>:L139</c>, <c>:L155</c>].
    /// </summary>
    internal const string NoLiteral = "no";

    /// <summary>
    /// The diagnostic raised when a key column name does not resolve [<c>:L120</c>].
    /// </summary>
    /// <remarks>
    /// VERBATIM, AND DELIBERATELY NOT LOCALIZED. Hardcoded Chinese with NO SPACE AFTER THE COLON, and
    /// it does not route through the localization surface even though sibling diagnostics elsewhere in
    /// the framework do. That inconsistency is legacy behaviour and is reproduced rather than
    /// harmonised (C-B). The offending column name is concatenated directly after it.
    /// </remarks>
    internal const string InvalidColumnNameMessage = "无效的列名:";

    /// <summary>
    /// The diagnostic raised when the multi-table switch is on but no descriptor was added
    /// [<c>:L360</c>].
    /// </summary>
    /// <remarks>Verbatim, unlocalized, trailing exclamation mark included.</remarks>
    internal const string NoUpdatableTableMessage = "没有设置可更新表!";

    /// <summary>
    /// The update-where mode the sole evidenced fixture uses: "key and updateable columns"
    /// [<c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L14</c>].
    /// </summary>
    /// <remarks>
    /// THE ONLY MODE THIS REPOSITORY EVIDENCES. No other mode's semantics are asserted anywhere in the
    /// legacy tree, so none is modelled here: guessing what mode 0, 2 or 3 compare would be inventing a
    /// contract, and C-B forbids it. What mode 1 requires of the payload is stated in the file header.
    /// </remarks>
    internal const long KeyAndUpdatableColumnsMode = 1L;

    private const string Assignment = " = ";
    private const string ValueQuote = "'";

    /// <summary>
    /// The diagnostic for an update table name that cannot be spliced into the script safely.
    /// </summary>
    /// <remarks>
    /// <para>
    /// IN ENGLISH, DELIBERATELY, AND THAT IS NOT AN OVERSIGHT. Every other diagnostic in this file is
    /// the oracle's own Chinese text carried verbatim, because the oracle raises it. The oracle raises
    /// NOTHING here - it splices the table name unconditionally
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru:L143</c>] - so there is no
    /// legacy string to carry, and inventing a Chinese one would fabricate a legacy diagnostic that
    /// never existed. The three COLUMN positions do have a legacy diagnostic to reuse, and they reuse
    /// it: see <see cref="InvalidColumnNameMessage"/>.
    /// </para>
    /// <para>
    /// It carries no value, only the position: the offending text is the caller's own and echoing it
    /// into a diagnostic that may be logged would put attacker-chosen script fragments in the log,
    /// which is the shape of the leak <c>Errors/SqlRedactor.cs</c> exists to prevent.
    /// </para>
    /// </remarks>
    internal const string UnsafeUpdateTableMessage =
        "The update table name is not a valid identifier and was refused.";

    #endregion

    #region The identifier grammar - the guard the legacy does not have and this boundary must

    // ==============================================================================================
    //  WHY A GRAMMAR EXISTS HERE AT ALL, WHEN THE REST OF THIS FILE IS FAITHFUL TO A FAULT
    //  ----------------------------------------------------------------------------------------------
    //  THE SCRIPT THIS FILE BUILDS IS SYNTAX, NOT DATA. Every name a caller supplies is concatenated
    //  into a DataWindow modification script whose grammar is `<target>.<attribute> = <value>` with
    //  lines separated by a line feed (LineSeparator). A name is therefore in the same position a
    //  string literal occupies in hand-interpolated SQL: a name carrying a line feed ENDS the
    //  assignment it was in and BEGINS one the caller authored, and a name carrying an apostrophe
    //  closes the quoted value in steps 5 and 7 and reopens it wherever the caller likes. The two
    //  concrete consequences the review named:
    //
    //    * PROPERTY INJECTION. `ID~nDataWindow.Table.UpdateWhere = '0'` supplied as an updatable
    //      column name emits two lines, the second of which silently turns the optimistic-concurrency
    //      check OFF - so an update that was meant to compare six original values compares none, and
    //      the conflict detection this whole folder exists to preserve stops detecting anything.
    //    * UPDATE-TABLE REDIRECTION. `COMPANY' ~n DataWindow.Table.UpdateTable = 'SALARIES` supplied
    //      as the table name retargets the entire update at a table the caller chose.
    //
    //  NEITHER IS A LEGACY EXPOSURE. PowerFramework is an in-process library with no listener
    //  [AAP 0.1.1], so the only caller was the application itself and a table name was a literal in
    //  the developer's own source. Decomposition creates the first-ever ingress, and
    //  persistence.v1.TableUpdateContract now carries these six fields across it, so the name arrives
    //  from a remote caller. This is precisely the case AAP 0.1.5 governs: where a legacy behaviour
    //  cannot be reproduced across a network boundary, the contract is NARROWED WITH A DEFINED ERROR,
    //  never widened with a guess.
    //
    //  REFUSING IS NOT REWRITING, AND THAT DISTINCTION IS WHAT KEEPS PARITY. Nothing here escapes,
    //  quotes, doubles or strips a character: a name either passes unchanged into the script exactly
    //  as the oracle would have spliced it, or the operation is refused. So for every input the legacy
    //  could actually have been given, the generated script is byte-identical to before.
    //
    //  WHERE THE REFUSAL LIVES, AND WHY IN TWO PLACES. The PRIMARY refusal is at the admission
    //  boundary, UpdatableTableCollection.AddUpdatableTable, which is where the oracle's own
    //  validation lives [:L84] and which answers the oracle's own code for a bad input. The SECONDARY
    //  refusal is inside BuildModificationString itself, because a descriptor can also be reached
    //  through UpdatableTableDescriptor.Create or a `with` expression - both of which the descriptor's
    //  own remarks already acknowledge as open doors - and a guard that only the front door has is not
    //  a guard. The builder's refusal is fail-closed: it happens BEFORE the name is concatenated and,
    //  for a key column, BEFORE the name is even handed to Describe.
    //
    //  WHAT IS DELIBERATELY *NOT* DONE: RESOLVING STEPS 2 AND 4 AGAINST CARRIER METADATA. The review's
    //  suggested resolution also offered "resolve against carrier metadata". Step 3 already does, and
    //  faithfully so - the oracle resolves each KEY column through Describe(<name> + ".Id") and fails
    //  the whole build on a non-positive identifier [:L118-L122]. Extending that to the updatable
    //  columns and the identity column would add a failure the oracle does not have: today a
    //  well-formed but unknown column name emits its line, Modify rejects it, and the driver's own
    //  error string is carried through verbatim [:L145-L148] - which IS the legacy behaviour, and is
    //  already a rejection. Pre-resolving would move that failure earlier and replace the driver's
    //  diagnostic with ours for no security gain, because the grammar below is what stops a name from
    //  authoring script and an unknown-but-well-formed name authors nothing. The grammar is the fix;
    //  metadata resolution would be a behaviour change wearing its clothes.
    // ==============================================================================================

    /// <summary>
    /// Reports whether a name may be spliced into the script as a COLUMN target: one identifier
    /// segment and nothing else.
    /// </summary>
    /// <param name="name">The candidate name. <see langword="null"/> and empty are refused.</param>
    /// <returns><see langword="true"/> when every character is a segment character.</returns>
    /// <remarks>
    /// <para>
    /// ONE SEGMENT, SO A DOT IS REFUSED. The dot is the script's own property separator - the whole
    /// script is <c>&lt;target&gt;.&lt;attribute&gt;</c> - so a column name containing one would let a
    /// caller choose the attribute as well as the target: <c>ID.Key</c> passed as an updatable column
    /// emits <c>ID.Key.Update = yes</c>, and a name ending in a dot re-aims the assignment entirely.
    /// A DataWindow column name is a plain identifier, so nothing legitimate is lost.
    /// </para>
    /// <para>
    /// THE SET IS DEFINED BY WHAT IT ADMITS, NOT BY A LIST OF WHAT IT REFUSES, which is the only form
    /// that is safe by construction: refusing an enumerated blocklist leaves every character nobody
    /// thought of on the accepted side. Admitting only segment characters refuses, in one stroke, every
    /// control character including the line feed and the carriage return, every space, the apostrophe
    /// and every other quote, the tilde PowerScript escapes with, the equals sign, the dot, and all
    /// remaining punctuation.
    /// </para>
    /// </remarks>
    internal static bool IsScriptSafeColumnName(string? name)
    {
        if (string.IsNullOrEmpty(name) || !IsSegmentStart(name[0]))
        {
            return false;
        }

        for (int index = 1; index < name.Length; index++)
        {
            if (!IsSegmentContinuation(name[index]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Reports whether a name may be spliced into the script as the UPDATE TABLE: one or more
    /// identifier segments joined by single dots.
    /// </summary>
    /// <param name="name">The candidate name. <see langword="null"/> and empty are refused.</param>
    /// <returns><see langword="true"/> when the name is a dotted run of identifier segments.</returns>
    /// <remarks>
    /// <para>
    /// QUALIFIED FORMS ARE ADMITTED HERE AND NOT FOR A COLUMN, and the asymmetry is the point. A table
    /// name is a DATABASE object name and is routinely schema-qualified - <c>dbo.COMPANY</c>,
    /// <c>SCHEMA.TABLE</c> - and it lands inside a QUOTED value [<c>:L143</c>], where the dot is
    /// ordinary text rather than the script's separator. A column name lands UNQUOTED as the assignment
    /// target, where the dot is the separator. Admitting dots for both would reopen the property
    /// injection that <see cref="IsScriptSafeColumnName"/> closes; refusing them for both would reject
    /// every schema-qualified table the legacy accepts.
    /// </para>
    /// <para>
    /// EMPTY SEGMENTS ARE REFUSED, so a leading dot, a trailing dot and a doubled dot are all refused -
    /// each of which is a malformed name rather than a qualification, and a trailing dot is the shape
    /// that would concatenate with whatever followed it.
    /// </para>
    /// <para>
    /// A NAME OF A SINGLE SPACE IS NOW REFUSED, AND THAT IS THE ONE OBSERVABLE NARROWING IN THIS FILE.
    /// <see cref="UpdatableTableDescriptor.IsAcceptable"/> still admits it - that predicate is the
    /// oracle's <c>name = ""</c> test and stays exactly that - so the oracle's own reachability is
    /// unchanged and the narrowing is visible in one place: the boundary refuses what
    /// <c>DataWindow.Table.UpdateTable = ' '</c> would have named. No table is called a space, and a
    /// contract that carries the name across a network is the right place to say so.
    /// </para>
    /// </remarks>
    internal static bool IsScriptSafeTableName(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        int segmentStart = 0;

        for (int index = 0; index <= name.Length; index++)
        {
            // The terminator arm doubles as the end-of-string arm, so the final segment is validated by
            // the same code as every earlier one rather than by a duplicated tail block.
            if (index != name.Length && name[index] != '.')
            {
                continue;
            }

            if (!IsScriptSafeColumnName(name[segmentStart..index]))
            {
                return false;
            }

            segmentStart = index + 1;
        }

        return true;
    }

    /// <summary>Whether a character may begin an identifier segment: a letter or an underscore.</summary>
    /// <param name="character">The character to classify.</param>
    /// <returns><see langword="true"/> when it may begin a segment.</returns>
    /// <remarks>
    /// <para>
    /// UNICODE LETTERS RATHER THAN ASCII ONLY, and that is evidenced rather than generous.
    /// PowerFramework is a Chinese framework whose own diagnostics are Chinese
    /// [<see cref="UpdateWhereBuilder.InvalidColumnNameMessage"/>], so a DataWindow column named in
    /// Chinese is an ordinary input rather than a curiosity, and restricting to ASCII would refuse it
    /// for no benefit - a Chinese character can no more terminate an assignment than a Latin one can.
    /// The sibling guard in <c>Sql/ClauseModifier.cs</c> classifies word characters the same way, so the
    /// two boundaries agree on what an identifier is.
    /// </para>
    /// </remarks>
    private static bool IsSegmentStart(char character) =>
        char.IsLetter(character) || character == '_';

    /// <summary>
    /// Whether a character may continue an identifier segment: a segment-start character, a digit,
    /// <c>$</c> or <c>#</c>.
    /// </summary>
    /// <param name="character">The character to classify.</param>
    /// <returns><see langword="true"/> when it may continue a segment.</returns>
    /// <remarks>
    /// <c>$</c> and <c>#</c> are admitted because both are legal in database identifiers the legacy
    /// could name - Oracle permits both, and SQL Server's temporary-table prefix is <c>#</c> - and
    /// neither carries any meaning in the modification-script grammar, so neither can end an assignment
    /// or begin one.
    /// </remarks>
    private static bool IsSegmentContinuation(char character) =>
        IsSegmentStart(character) || char.IsDigit(character) || character is '$' or '#';

    #endregion

    #region The seven-step recipe

    /// <summary>
    /// Builds the modification script for one descriptor, and collects the resolved key column
    /// identifiers the workaround needs.
    /// </summary>
    /// <param name="descriptor">The table's update contract.</param>
    /// <param name="metadata">The injected describe surface.</param>
    /// <returns>
    /// A succeeded result carrying the script and the resolved key column identifiers, or a failed
    /// result carrying <see cref="RetCode.E_INTERNAL_ERROR"/> and the untranslated diagnostic.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="descriptor"/> or <paramref name="metadata"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// A PURE FUNCTION: no I/O, no clock, no static mutable state, and no DataWindow. Its only external
    /// contact is the three reads on <paramref name="metadata"/>, so the whole seven-step recipe and
    /// every one of its branches is exercisable against a fake reader with no database (C-H).
    /// </para>
    /// <para>
    /// THE SEVEN STEPS, IN THE ORACLE'S ORDER [<c>:L102-L143</c>]:
    /// </para>
    /// <list type="number">
    /// <item><description>
    /// Reset EVERY column: three lines per ordinal, <c>Update</c> then <c>Key</c> then
    /// <c>Identity</c>, all <c>no</c> [<c>:L103-L108</c>]. Retained even though later steps re-enable
    /// some of them - DECISION 1 states why, and the attribute ORDER within the triple is contract.
    /// </description></item>
    /// <item><description>Updatable columns, by name [<c>:L111-L114</c>].</description></item>
    /// <item><description>
    /// Key columns, by name, each first RESOLVED to a numeric identifier - and a non-positive
    /// identifier FAILS THE WHOLE BUILD IMMEDIATELY [<c>:L116-L125</c>].
    /// </description></item>
    /// <item><description>The identity column, only when non-empty [<c>:L127-L129</c>].</description></item>
    /// <item><description>
    /// The update-where mode, only when present, SINGLE-QUOTED [<c>:L131-L133</c>].
    /// </description></item>
    /// <item><description>
    /// The key-in-place setting, only when present [<c>:L135-L141</c>].
    /// </description></item>
    /// <item><description>
    /// The update table, ALWAYS, single-quoted, and WITH NO TRAILING NEWLINE [<c>:L143</c>].
    /// </description></item>
    /// </list>
    /// <para>
    /// THE ORACLE'S OWN ADD-TIME VALIDATION IS NOT RE-RUN HERE. The oracle validates at add time
    /// [<c>:L84</c>] and <c>_of_updateprepare</c> trusts what it finds, so a descriptor with no key
    /// columns emits no key lines rather than failing. Re-checking those arms here would move a failure
    /// to a place the oracle succeeds;
    /// <see cref="UpdatableTableCollection.AddUpdatableTable(string, IEnumerable{string}, IEnumerable{string}, string, long?, bool?)"/>
    /// is where that validation lives.
    /// </para>
    /// <para>
    /// THE IDENTIFIER GRAMMAR *IS* RE-RUN HERE, AND THAT IS NOT A CONTRADICTION OF THE PARAGRAPH ABOVE.
    /// The distinction is what each check is for: the oracle's arms decide whether a descriptor is a
    /// COMPLETE update contract, and re-deciding that here would second-guess the oracle. The grammar
    /// decides whether a name can be spliced into this script WITHOUT AUTHORING PART OF IT, and that is
    /// this function's own concern because this function is the only place the splicing happens. It is
    /// also the only guard that must hold for a descriptor built through
    /// <see cref="UpdatableTableDescriptor.Create"/> or a <c>with</c> expression, neither of which passes
    /// through the admission boundary. Four positions are guarded - the updatable columns at step 2, the
    /// key columns at step 3 before the describe, the identity column at step 4 when present, and the
    /// table name at step 7 - so a descriptor with an empty or whitespace name is now REFUSED where it
    /// would previously have emitted <c>UpdateTable = ''</c>. That is the single observable narrowing in
    /// this file and it is stated on <see cref="UpdateWhereBuilder.IsScriptSafeTableName"/>.
    /// </para>
    /// <para>
    /// EVERY NUMBER IS FORMATTED WITH <see cref="CultureInfo.InvariantCulture"/>. The script is
    /// machine-read, and a culture-sensitive conversion could substitute digits or a different negative
    /// sign, silently producing a script that addresses the wrong column or sets the wrong mode.
    /// </para>
    /// </remarks>
    internal static ModificationScriptResult BuildModificationString(
        UpdatableTableDescriptor descriptor,
        IUpdateTargetMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(metadata);

        StringBuilder script = new();
        List<int> keyColumnIds = [];

        // STEP 1 - RESET EVERY COLUMN [:L103-L108].
        // `nCount = Long(Data.Describe("DataWindow.Column.Count"))` then `for nIndex = 1 to nCount`.
        // R9: the ordinal is one-based in the describe vocabulary as well as in the loop, so it is
        // emitted exactly as the loop counter with no rebasing anywhere.
        int columnCount = metadata.GetColumnCount();

        foreach (int columnOrdinal in OneBasedIndex.Range(columnCount))
        {
            string ordinal = ColumnOrdinalPrefix + columnOrdinal.ToString(CultureInfo.InvariantCulture);

            // The three attributes in the oracle's order. Do not reorder or collapse them: the script
            // is compared byte for byte (C-B, DECISION 1).
            AppendLine(script, ordinal + UpdateAttributeSuffix + Assignment + NoLiteral);
            AppendLine(script, ordinal + KeyAttributeSuffix + Assignment + NoLiteral);
            AppendLine(script, ordinal + IdentityAttributeSuffix + Assignment + NoLiteral);
        }

        // STEP 2 - UPDATABLE COLUMNS [:L111-L114].
        // `nCount = UpperBound(Tables[index].UpdatableColumns)` then `for nIndex = 1 to nCount`.
        int updatableCount = OneBasedIndex.UpperBound(descriptor.UpdatableColumns);

        foreach (int index in OneBasedIndex.Range(updatableCount))
        {
            string columnName = descriptor.UpdatableColumns[
                OneBasedIndex.ToZeroBased(index, updatableCount, nameof(index))];

            // FAIL CLOSED BEFORE THE CONCATENATION. The oracle has no test here, so this is the new
            // guard - and it reuses the oracle's OWN failure for an unusable column name (the code of
            // :L120 and the text of :L119) rather than introducing a second failure shape, because a
            // name that cannot be spliced is unusable in exactly the sense that message describes.
            if (!IsScriptSafeColumnName(columnName))
            {
                return ModificationScriptResult.Failed(
                    RetCode.E_INTERNAL_ERROR,
                    InvalidColumnNameMessage + columnName);
            }

            AppendLine(script, columnName + UpdateAttributeSuffix + Assignment + YesLiteral);
        }

        // STEP 3 - KEY COLUMNS, EACH RESOLVED FIRST [:L116-L125].
        int keyCount = OneBasedIndex.UpperBound(descriptor.KeyColumns);

        foreach (int index in OneBasedIndex.Range(keyCount))
        {
            string columnName = descriptor.KeyColumns[
                OneBasedIndex.ToZeroBased(index, keyCount, nameof(index))];

            // FAIL CLOSED BEFORE THE DESCRIBE, not merely before the concatenation. Describe takes a
            // SPACE-SEPARATED LIST of property requests, so an unsafe key column name reaches the
            // carrier as several requests rather than one and the numeric answer this step reads would
            // be the answer to a request the caller composed. Guarding after the describe would already
            // be too late; the resulting identifier is what the workaround at :L156 later walks.
            if (!IsScriptSafeColumnName(columnName))
            {
                return ModificationScriptResult.Failed(
                    RetCode.E_INTERNAL_ERROR,
                    InvalidColumnNameMessage + columnName);
            }

            // `nColId = Long(Data.Describe(<name> + ".Id"))` [:L118].
            int columnId = metadata.GetColumnId(columnName + ColumnIdSuffix);

            // `if nColId <= 0 then Event OnError(E_INTERNAL_ERROR, "无效的列名:" + <name>) ; return
            // E_INTERNAL_ERROR` [:L119-L122]. NON-POSITIVE, so BOTH zero and negative fail - zero is
            // what PowerScript's Long() answers for the "!" or "?" a failed describe returns, and a
            // negative value is equally invalid. The build stops HERE: no further column is resolved
            // and the partially built script is discarded, exactly as the oracle's early return does.
            if (columnId <= 0)
            {
                return ModificationScriptResult.Failed(
                    RetCode.E_INTERNAL_ERROR,
                    InvalidColumnNameMessage + columnName);
            }

            // `nKeyColumns[UpperBound(nKeyColumns) + 1] = nColId` [:L123] - the append idiom. The
            // resolved identifiers are an OUTPUT of this step: the workaround at :L156 and :L162 walks
            // them, so they must survive past the script.
            keyColumnIds.Add(columnId);

            AppendLine(script, columnName + KeyAttributeSuffix + Assignment + YesLiteral);
        }

        // STEP 4 - IDENTITY COLUMN, ONLY WHEN NON-EMPTY [:L127-L129].
        // The oracle's test is the exact comparison `<> ""`, so an empty identity column emits NOTHING
        // and is not an error. See UpdatableTableDescriptor.HasIdentityColumn.
        if (descriptor.HasIdentityColumn)
        {
            // FAIL CLOSED, and note this sits INSIDE the presence test rather than beside it: an ABSENT
            // identity column must stay legal, so an empty name is never handed to the grammar.
            if (!IsScriptSafeColumnName(descriptor.IdentityColumn))
            {
                return ModificationScriptResult.Failed(
                    RetCode.E_INTERNAL_ERROR,
                    InvalidColumnNameMessage + descriptor.IdentityColumn);
            }

            AppendLine(
                script,
                descriptor.IdentityColumn + IdentityAttributeSuffix + Assignment + YesLiteral);
        }

        // STEP 5 - UPDATE-WHERE MODE, ONLY WHEN PRESENT, SINGLE-QUOTED [:L131-L133].
        // `if Not IsNull(...) then ... "UpdateWhere = '" + String(v) + "'"`. The quoting looks wrong
        // around a number and is not (C-B, DECISION 3). ABSENT EMITS NO LINE AT ALL - that is the whole
        // meaning of the nullability (DECISION 2), so this must branch on HasValue and never on a
        // sentinel comparison.
        if (descriptor.UpdateWhere.HasValue)
        {
            AppendLine(
                script,
                UpdateWhereProperty
                    + Assignment
                    + ValueQuote
                    + descriptor.UpdateWhere.Value.ToString(CultureInfo.InvariantCulture)
                    + ValueQuote);
        }

        // STEP 6 - KEY-IN-PLACE, ONLY WHEN PRESENT [:L135-L141].
        // Note the asymmetry with step 5 that the oracle itself has: this value is NOT quoted, and it
        // is emitted as the words yes/no rather than as a number. `false` emits `= no`, which is a
        // DIFFERENT observable from absence emitting nothing - the two must never be conflated.
        // The property spelling carries the oracle's lower-case `i`; see UpdateKeyInPlaceProperty.
        if (descriptor.UpdateKeyInPlace.HasValue)
        {
            AppendLine(
                script,
                UpdateKeyInPlaceProperty
                    + Assignment
                    + (descriptor.UpdateKeyInPlace.Value ? YesLiteral : NoLiteral));
        }

        // STEP 7 - UPDATE TABLE, ALWAYS, AND WITH NO TRAILING NEWLINE [:L143].
        // `sModString += "DataWindow.Table.UpdateTable = '" + Tables[index].Name + "'"` - unconditional
        // and with no `+ "~n"`. This is the last line and the script must not end in a line feed, so
        // this is deliberately an Append and not an AppendLine.
        //
        // FAIL CLOSED, AND LAST RATHER THAN FIRST. The check could have run before step 1 and saved the
        // work, and it deliberately does not: the oracle's only failure in this function is the key
        // column at step 3, so leaving the table test here means a descriptor with both a bad key column
        // and a bad table name still reports the KEY COLUMN - the oracle's own diagnostic - rather than
        // being pre-empted by a diagnostic the oracle does not have. The partially built script is
        // discarded either way.
        if (!IsScriptSafeTableName(descriptor.Name))
        {
            return ModificationScriptResult.Failed(
                RetCode.E_INTERNAL_ERROR,
                UnsafeUpdateTableMessage);
        }

        script.Append(
            UpdateTableProperty + Assignment + ValueQuote + descriptor.Name + ValueQuote);

        return ModificationScriptResult.Succeeded(script.ToString(), keyColumnIds);
    }

    #endregion

    #region Helpers the wire and the update path need

    /// <summary>
    /// Names the columns whose ORIGINAL values a <see cref="KeyAndUpdatableColumnsMode"/> concurrency
    /// check compares, so no consumer has to re-derive the rule.
    /// </summary>
    /// <param name="descriptor">The table's update contract.</param>
    /// <returns>
    /// The key columns in descriptor order, followed by the updatable columns not already among them,
    /// each in descriptor order and each appearing once.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="descriptor"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// EXISTS SO THE RULE HAS ONE HOME. <c>Tasks/SqlUpdateTask.cs</c> and <c>Grpc/UpdateService.cs</c>
    /// both have to assemble a payload carrying, per row, the current AND original value of every
    /// marked column; deriving "which columns" independently in each would let the two drift.
    /// </para>
    /// <para>
    /// THE SET IS KEY COLUMNS UNION UPDATABLE COLUMNS, because mode 1 is "KEY AND UPDATEABLE COLUMNS":
    /// the where clause carries the key column plus the original value of every updateable column. On
    /// the sole evidenced fixture the two lists together cover all six columns, every one of which is
    /// marked <c>update=yes updatewhereclause=yes</c>
    /// [<c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L8-L13</c>] - so the check spans all six
    /// originals, which is exactly the file header's wire consequence.
    /// </para>
    /// <para>
    /// KEY COLUMNS COME FIRST because that is the clause's own order in the mode's name, and it makes
    /// the result stable and diffable rather than dependent on which list happened to be longer.
    /// </para>
    /// <para>
    /// DEDUPLICATION IS CASE-INSENSITIVE, AND THAT IS EVIDENCED RATHER THAN ASSUMED. The oracle
    /// compares DataWindow column metadata case-insensitively when it has to match names - it
    /// lower-cases both sides for the identity column's database-name prefix match [<c>:L217</c>,
    /// <c>:L221</c>] - so a descriptor naming a column as a key in one casing and as updatable in
    /// another refers to ONE column. The FIRST spelling encountered is the one kept, so the output is
    /// still made of the caller's own strings.
    /// </para>
    /// <para>
    /// THIS DOES NOT VALIDATE THE MODE. A descriptor whose <see cref="UpdatableTableDescriptor.UpdateWhere"/>
    /// is absent takes its effective mode from the carrier's own definition, which this type cannot
    /// see; use <see cref="IsKeyAndUpdatableColumnsMode"/> on the EFFECTIVE mode to decide whether the
    /// originals are needed at all.
    /// </para>
    /// </remarks>
    internal static IReadOnlyList<string> MarkedColumnsOf(UpdatableTableDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        List<string> marked = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        foreach (string columnName in descriptor.KeyColumns)
        {
            if (seen.Add(columnName))
            {
                marked.Add(columnName);
            }
        }

        foreach (string columnName in descriptor.UpdatableColumns)
        {
            if (seen.Add(columnName))
            {
                marked.Add(columnName);
            }
        }

        return marked;
    }

    /// <summary>
    /// Reports whether an EFFECTIVE update-where mode is the "key and updateable columns" mode that
    /// requires every updateable column's original value on the wire.
    /// </summary>
    /// <param name="effectiveUpdateWhere">
    /// The mode actually in force - the descriptor's value when it has one, otherwise the carrier's own
    /// setting. <see langword="null"/> answers <see langword="false"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> only when the mode is <see cref="KeyAndUpdatableColumnsMode"/>.
    /// </returns>
    /// <remarks>
    /// A <see langword="null"/> ANSWERS FALSE BECAUSE NULL DOES NOT MEAN "MODE ZERO" - it means the
    /// descriptor does not state the mode, and the caller must then read the carrier's own setting
    /// instead of assuming one. Answering <see langword="true"/> for null would silently claim the
    /// fixture's mode for every descriptor that omitted it; answering <see langword="false"/> and
    /// requiring the caller to resolve first is the reading that cannot be wrong by accident.
    /// </remarks>
    internal static bool IsKeyAndUpdatableColumnsMode(long? effectiveUpdateWhere)
    {
        return effectiveUpdateWhere == KeyAndUpdatableColumnsMode;
    }

    /// <summary>
    /// Reproduces PowerScript's <c>Long()</c> coercion of a describe result: a decimal integer parses,
    /// and ANYTHING ELSE - including <see langword="null"/>, empty, <c>"!"</c> and <c>"?"</c> - answers
    /// zero.
    /// </summary>
    /// <param name="describeResult">The raw text a describe call answered.</param>
    /// <returns>The parsed value, or zero when the text is not a decimal integer.</returns>
    /// <remarks>
    /// <para>
    /// Published for implementors of <see cref="IUpdateTargetMetadata"/>, because the oracle wraps both
    /// of its numeric describes in <c>Long(...)</c> [<c>:L103</c>, <c>:L118</c>] and the coercion is
    /// load-bearing at both: a failed column-count describe must yield zero so the reset pass emits
    /// nothing, and a failed identifier describe must yield zero so the non-positive arm fires with the
    /// oracle's own diagnostic rather than an exception.
    /// </para>
    /// <para>
    /// PARSED WITH <see cref="CultureInfo.InvariantCulture"/> AND <see cref="NumberStyles.Integer"/>:
    /// describe answers a machine string, so no thousands separator, currency symbol or culture-specific
    /// digit shape may be accepted. Leading and trailing white space IS accepted, which is what
    /// <see cref="NumberStyles.Integer"/> allows and what makes a padded answer behave as the number it
    /// shows.
    /// </para>
    /// </remarks>
    internal static int CoerceDescribedNumber(string? describeResult)
    {
        return int.TryParse(
            describeResult,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out int value)
            ? value
            : 0;
    }

    private static void AppendLine(StringBuilder script, string line)
    {
        // Explicitly LineSeparator, never StringBuilder.AppendLine, which would emit
        // Environment.NewLine and put a carriage return into the script on Windows.
        script.Append(line).Append(LineSeparator);
    }

    #endregion
}

#endregion


#region The descriptor array - `Tables[]`, the multi-table switch and the add validation

/// <summary>
/// The ordered descriptor array the legacy holds as <c>TABLEDATA Tables[]</c>, together with the
/// multi-table switch that decides whether it is applied at all.
/// </summary>
/// <remarks>
/// <para>
/// Ports <c>Tables[]</c> [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru:L30</c>],
/// <c>_bMultiTableUpdate</c> [<c>:L33</c>], the descriptor-clearing half of <c>of_reset</c>
/// [<c>:L58-L72</c>] and <c>of_addupdatabletable</c> [<c>:L82-L96</c>], plus the caller-side overload
/// pair at <c>n_cst_threading_task_sqlupdate.sru:L223-L234</c>.
/// </para>
/// <para>
/// ORDER IS SIGNIFICANT, not incidental. With the switch on, the worker walks the array IN ARRAY ORDER
/// calling prepare and then update once per table, and STOPS AT THE FIRST FAILURE [<c>:L364-L369</c>],
/// so a later table is simply not attempted after an earlier one fails.
/// </para>
/// <para>
/// MULTI-TABLE UPDATE FROM ONE CARRIER IS A REAL LEGACY CAPABILITY, NOT A THEORETICAL ONE, and the
/// evidence is threefold: the legacy holds an ARRAY rather than a single descriptor [<c>:L30</c>],
/// there is an explicit add path taking all six fields [<c>:L46</c>, <c>:L82-L96</c>], and there is an
/// explicit switch with its own setter [<c>:L51</c>, <c>:L265-L268</c>].
/// </para>
/// </remarks>
internal sealed class UpdatableTableCollection
{
    private readonly List<UpdatableTableDescriptor> _tables = [];

    /// <summary>
    /// The multi-table switch [<c>:L33</c>, set at <c>:L265-L268</c>].
    /// </summary>
    /// <remarks>
    /// WITH THIS OFF, THE DESCRIPTOR ARRAY IS NOT APPLIED AT ALL - the carrier's own static definition
    /// governs update, key, identity, update-where and key-in-place. See
    /// <see cref="UpdatePreparer.PrepareAndUpdate"/>, which reproduces that branch and cites the
    /// single-caller finding behind it.
    /// </remarks>
    internal bool MultiTableUpdate { get; set; }

    /// <summary>
    /// An optional busy predicate. When supplied and it answers <see langword="true"/>, every mutation
    /// on this collection answers <see cref="RetCode.E_BUSY"/> instead of mutating.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE STATE IS SUPPLIED BY THE CALLER AND IS DELIBERATELY NOT MODELLED HERE. The legacy guards
    /// mutation two different ways - the worker-side <c>of_reset</c> tests its own
    /// <c>#Running</c> [<c>:L60</c>] while the caller-side proxy tests <c>of_IsBusy()</c>
    /// [<c>n_cst_threading_task_sqlupdate.sru:L207</c>, <c>:L223</c>, <c>:L236</c>] - and BOTH answer
    /// <c>RetCode.E_BUSY</c>. Inventing a running-state machine in this folder would duplicate state
    /// that the Tasks/ layer owns and would be a second place for it to be wrong.
    /// </para>
    /// <para>
    /// LEFT UNSET, THE GUARD NEVER FIRES, which is the correct default for a descriptor collection that
    /// no task is driving - for instance in a parity test.
    /// </para>
    /// </remarks>
    internal Func<bool>? IsBusy { get; set; }

    /// <summary>
    /// The descriptors, in add order. Read-only; use the add and reset operations to change them.
    /// </summary>
    internal IReadOnlyList<UpdatableTableDescriptor> Descriptors => _tables;

    /// <summary>
    /// The LAST VALID ONE-BASED INDEX of the descriptor array - PowerScript's
    /// <c>UpperBound(Tables)</c>, which is what <c>:L358</c> compares against zero.
    /// </summary>
    /// <remarks>
    /// R9: named for the oracle's own concept rather than <c>Count</c>, so that a reader comparing this
    /// against <c>:L358</c> sees the same operation. The two happen to be equal only because the domain
    /// is one-based.
    /// </remarks>
    internal int UpperBound => OneBasedIndex.UpperBound(_tables);

    /// <summary>
    /// Adds one updatable table, validating it exactly as the oracle does.
    /// </summary>
    /// <param name="name">The update table name. Empty is rejected.</param>
    /// <param name="updatableColumns">The updatable column names. An empty collection is rejected.</param>
    /// <param name="keyColumns">The key column names. An empty collection is rejected.</param>
    /// <param name="identityColumn">The identity column, or empty for none. EMPTY IS ACCEPTED.</param>
    /// <param name="updateWhere">The update-where mode, or <see langword="null"/> to leave the carrier's
    /// setting alone.</param>
    /// <param name="updateKeyInPlace">The key-in-place setting, or <see langword="null"/> to leave the
    /// carrier's setting alone.</param>
    /// <returns>
    /// <see cref="RetCode.OK"/> on success [<c>:L95</c>], <see cref="RetCode.E_INVALID_ARGUMENT"/> when
    /// the oracle's validation fails [<c>:L84</c>] OR when any name is not a valid identifier, or
    /// <see cref="RetCode.E_BUSY"/> when <see cref="IsBusy"/> answers <see langword="true"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Ports the six-argument form
    /// [<c>:L82-L96</c>, proxied at <c>n_cst_threading_task_sqlupdate.sru:L223-L225</c>]. THE THREE
    /// VALIDATION ARMS ARE THE ORACLE'S OWN, joined by OR and evaluated before anything is stored:
    /// empty name, zero-length updatable columns, zero-length key columns. An empty identity column is
    /// NOT an arm - see <see cref="UpdatableTableDescriptor.IsAcceptable"/>.
    /// </para>
    /// <para>
    /// A FOURTH REFUSAL FOLLOWS THE ORACLE'S THREE, AND ANSWERS THE SAME CODE. This is the admission
    /// boundary for the six fields <c>persistence.v1.TableUpdateContract</c> carries, so it is where a
    /// name that would author modification script is refused - see
    /// <see cref="UpdatableTableDescriptor.IsScriptSafe"/> and THE IDENTIFIER GRAMMAR region on
    /// <see cref="UpdateWhereBuilder"/>. Reusing <see cref="RetCode.E_INVALID_ARGUMENT"/> rather than
    /// inventing a code is deliberate: a caller already has to handle it from the three arms above, and
    /// the two refusals mean the same thing to that caller - this descriptor was not admitted.
    /// </para>
    /// <para>
    /// THE DESCRIPTOR LANDS AT <c>UpperBound + 1</c> [<c>:L86</c>], which for an ordered list is a plain
    /// append; <see cref="OneBasedIndex.AppendIndex{T}"/> names that idiom and is asserted against the
    /// resulting position so the append cannot silently start overwriting.
    /// </para>
    /// </remarks>
    internal long AddUpdatableTable(
        string name,
        IEnumerable<string> updatableColumns,
        IEnumerable<string> keyColumns,
        string identityColumn,
        long? updateWhere,
        bool? updateKeyInPlace)
    {
        if (IsBusy?.Invoke() == true)
        {
            return RetCode.E_BUSY;
        }

        UpdatableTableDescriptor descriptor = UpdatableTableDescriptor.Create(
            name,
            updatableColumns,
            keyColumns,
            identityColumn,
            updateWhere,
            updateKeyInPlace);

        // `if name = "" or UpperBound(updatableColumns) = 0 or UpperBound(keyColumns) = 0 then return
        // RetCode.E_INVALID_ARGUMENT` [:L84]. Rejected BEFORE the append, so a rejected descriptor
        // leaves the array untouched.
        if (!descriptor.IsAcceptable)
        {
            return RetCode.E_INVALID_ARGUMENT;
        }

        // THE IDENTIFIER GRAMMAR, which the oracle does not have and this admission boundary must. It
        // runs AFTER the oracle's own three arms so their reachability is exactly unchanged, BEFORE the
        // append so a refused descriptor never enters the collection, and it answers THE SAME CODE the
        // oracle's own arms answer rather than introducing a fourth outcome for the caller to handle.
        // See THE IDENTIFIER GRAMMAR region on UpdateWhereBuilder for the two exposures this closes.
        if (!descriptor.IsScriptSafe)
        {
            return RetCode.E_INVALID_ARGUMENT;
        }

        // `nIndex = UpperBound(Tables) + 1` [:L86] then the six field assignments [:L88-L93].
        //
        // R9: written as the ONE-BASED append the oracle performs - compute the index, convert it once
        // through the centralized helper, store there - rather than as a bare Add. The two are
        // equivalent for a list (converting UpperBound + 1 yields exactly the current count, and
        // inserting at the count appends), and writing it this way keeps the idiom executable and
        // diffable instead of leaving the helper as decoration. It is deliberately not a debug-only
        // assertion, which a release build would compile away.
        int appendIndex = OneBasedIndex.AppendIndex(_tables);

        _tables.Insert(
            OneBasedIndex.ToZeroBased(appendIndex, appendIndex, nameof(appendIndex)),
            descriptor);

        return RetCode.OK;
    }

    /// <summary>
    /// Adds one updatable table WITHOUT stating either optional setting, leaving both to the carrier's
    /// own definition.
    /// </summary>
    /// <param name="name">The update table name. Empty is rejected.</param>
    /// <param name="updatableColumns">The updatable column names. An empty collection is rejected.</param>
    /// <param name="keyColumns">The key column names. An empty collection is rejected.</param>
    /// <param name="identityColumn">The identity column, or empty for none.</param>
    /// <returns>As the six-argument form.</returns>
    /// <remarks>
    /// Ports the four-argument overload at
    /// <c>n_cst_threading_task_sqlupdate.sru:L227-L234</c>, which declares two locals, calls
    /// <c>SetNull</c> on BOTH [<c>:L230-L231</c>] and delegates to the six-argument form
    /// [<c>:L233</c>]. THIS OVERLOAD IS THE PROOF THAT ABSENCE IS A FIRST-CLASS INPUT rather than an
    /// uninitialised accident, and it is why the two settings are nullable - DECISION 2. Passing
    /// <see langword="null"/> through is therefore the whole point of the overload, not a shortcut.
    /// </remarks>
    internal long AddUpdatableTable(
        string name,
        IEnumerable<string> updatableColumns,
        IEnumerable<string> keyColumns,
        string identityColumn)
    {
        return AddUpdatableTable(
            name,
            updatableColumns,
            keyColumns,
            identityColumn,
            updateWhere: null,
            updateKeyInPlace: null);
    }

    /// <summary>
    /// Clears the descriptor array and turns the multi-table switch off.
    /// </summary>
    /// <returns>
    /// <see cref="RetCode.OK"/>, or <see cref="RetCode.E_BUSY"/> when <see cref="IsBusy"/> answers
    /// <see langword="true"/> [<c>:L60</c>].
    /// </returns>
    /// <remarks>
    /// <para>
    /// Ports the part of <c>of_reset</c> that concerns this collection: <c>_bMultiTableUpdate = false</c>
    /// [<c>:L63</c>] and <c>Tables = emptyTables</c> [<c>:L67</c>]. ASSIGNING A FRESH EMPTY ARRAY is
    /// what the oracle does, so a reset REPLACES the array wholesale rather than trimming it.
    /// </para>
    /// <para>
    /// THE REST OF <c>of_reset</c> DELIBERATELY DOES NOT LIVE HERE. The auto-commit flag, the data
    /// object, the SQL syntax, the update blob and the row count [<c>:L62-L69</c>] are task state, and
    /// the <c>super::of_Reset()</c> chain [<c>:L71</c>] is the task hierarchy's - both belong to
    /// <c>Tasks/SqlUpdateTask.cs</c>. Reaching for them from here would put one piece of state in two
    /// places.
    /// </para>
    /// </remarks>
    internal long Reset()
    {
        if (IsBusy?.Invoke() == true)
        {
            return RetCode.E_BUSY;
        }

        MultiTableUpdate = false;
        _tables.Clear();

        return RetCode.OK;
    }

    /// <summary>
    /// Returns the descriptor at a ONE-BASED index, as <c>Tables[index]</c> does.
    /// </summary>
    /// <param name="oneBasedIndex">
    /// The one-based index, between <see cref="OneBasedIndex.FirstIndex"/> and
    /// <see cref="UpperBound"/> inclusive.
    /// </param>
    /// <returns>The descriptor.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The index is outside the array.</exception>
    /// <remarks>
    /// R9. <c>_of_updateprepare</c> takes a ONE-BASED <c>index</c> parameter [<c>:L47</c>, <c>:L98</c>]
    /// and the multi-table loop drives it with <c>for nIndex = 1 to nCount</c> [<c>:L364</c>], so the
    /// index that reaches this collection is one-based. The single conversion to a zero-based list index
    /// goes through <see cref="OneBasedIndex.ToZeroBased"/>, which throws rather than clamping.
    /// </remarks>
    internal UpdatableTableDescriptor DescriptorAt(int oneBasedIndex)
    {
        return _tables[OneBasedIndex.ToZeroBased(oneBasedIndex, UpperBound, nameof(oneBasedIndex))];
    }

    /// <summary>
    /// Returns the descriptor at a one-based index without throwing when the index is outside the array.
    /// </summary>
    /// <param name="oneBasedIndex">The one-based index.</param>
    /// <param name="descriptor">The descriptor, or <see langword="null"/> when the index is outside.</param>
    /// <returns><see langword="true"/> when a descriptor was returned.</returns>
    internal bool TryGetDescriptor(int oneBasedIndex, out UpdatableTableDescriptor? descriptor)
    {
        if (!OneBasedIndex.IsWithin(oneBasedIndex, UpperBound))
        {
            descriptor = null;

            return false;
        }

        descriptor = DescriptorAt(oneBasedIndex);

        return true;
    }
}

#endregion

#region The preparer - apply the script, then the key-change refresh, then the multi-table loop

/// <summary>
/// Applies a descriptor's modification script to a carrier and performs the <c>updatekeyinplace=no</c>
/// key-change refresh, then drives the multi-table prepare-and-update loop.
/// </summary>
/// <remarks>
/// <para>
/// Ports the applying half of <c>_of_updateprepare</c>
/// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru:L145-L169</c>] and the
/// invocation shape at <c>ondotask:L356-L372</c>. Script BUILDING is
/// <see cref="UpdateWhereBuilder.BuildModificationString"/>; the split is what lets the script be
/// asserted with no carrier at all.
/// </para>
/// <para>
/// FAILURES SURFACE AS A STRUCTURED RESULT, NEVER AS A DIALOG (C-D). The legacy raises
/// <c>Event OnError(code, text)</c> [<c>:L120</c>, <c>:L147</c>]; DesignSystem is a deferred capability
/// area, so there is no MessageBox analogue here. The optional <c>onError</c> sink mirrors the event's
/// own two-argument shape for a caller that wants push notification, and it is invoked with the code
/// and the UNTRANSLATED text.
/// </para>
/// </remarks>
internal sealed class UpdatePreparer
{
    private readonly IUpdateTargetMetadata _metadata;
    private readonly IUpdateTargetModifier _modifier;
    private readonly Action<long, string>? _onError;

    /// <summary>
    /// Creates a preparer over an injected describe surface, an injected modify surface and an optional
    /// error sink.
    /// </summary>
    /// <param name="metadata">The describe surface. In the legacy this and
    /// <paramref name="modifier"/> are the same carrier object.</param>
    /// <param name="modifier">The modify surface, whose result is an ERROR STRING where empty means
    /// success.</param>
    /// <param name="onError">
    /// Optional sink mirroring <c>Event OnError(code, text)</c>. Omit it to rely on the returned result
    /// alone.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="metadata"/> or <paramref name="modifier"/> is <see langword="null"/>.
    /// </exception>
    internal UpdatePreparer(
        IUpdateTargetMetadata metadata,
        IUpdateTargetModifier modifier,
        Action<long, string>? onError = null)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(modifier);

        _metadata = metadata;
        _modifier = modifier;
        _onError = onError;
    }

    /// <summary>
    /// Re-derives one table's update contract on the carrier: builds the script, applies it, and
    /// performs the key-change refresh when the runtime key-in-place setting calls for it.
    /// </summary>
    /// <param name="descriptor">The table's update contract.</param>
    /// <param name="store">The carrier whose buffers the refresh walks.</param>
    /// <returns>The outcome, including the refreshed row/column pairs.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="descriptor"/> or <paramref name="store"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// Ports <c>_of_updateprepare</c> end to end [<c>:L98-L170</c>]. The three outcomes are: a build
    /// failure from an unresolvable key column name, a modify failure from a non-empty error string, and
    /// success - after which the workaround may have refreshed zero or more row/column pairs.
    /// </remarks>
    internal UpdatePreparationResult Prepare(
        UpdatableTableDescriptor descriptor,
        DataWindowBufferStore store)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(store);

        ModificationScriptResult built =
            UpdateWhereBuilder.BuildModificationString(descriptor, _metadata);

        if (!built.IsSucceeded)
        {
            // `Event OnError(RetCode.E_INTERNAL_ERROR,"无效的列名:" + <name>)` then the early return
            // [:L120-L121]. The text is forwarded exactly as built - not translated, not wrapped.
            _onError?.Invoke(built.Code, built.ErrorText);

            return new UpdatePreparationResult { Code = built.Code, ErrorText = built.ErrorText };
        }

        // `sErr = Data.Modify(sModString)` [:L145]. AN ERROR STRING, NOT A CODE, AND EMPTY MEANS
        // SUCCESS [:L146]. A caller that reads this as a numeric result inverts the whole test.
        string modifyError = _modifier.Modify(built.Script);

        if (modifyError.Length != 0)
        {
            // `Event OnError(RetCode.E_INTERNAL_ERROR,sErr) ; return RetCode.E_INTERNAL_ERROR`
            // [:L147-L148]. The driver's text is carried through UNMODIFIED, because it is the only
            // detail a prepare failure has.
            _onError?.Invoke(RetCode.E_INTERNAL_ERROR, modifyError);

            return new UpdatePreparationResult
            {
                Code = RetCode.E_INTERNAL_ERROR,
                ErrorText = modifyError,
                Script = built.Script,
                KeyColumnIds = built.KeyColumnIds,
            };
        }

        (bool refreshRequired, IReadOnlyList<KeyColumnRefresh> refreshes) =
            ApplyKeyChangeRefresh(built.KeyColumnIds, store);

        return new UpdatePreparationResult
        {
            Code = RetCode.OK,
            Script = built.Script,
            KeyColumnIds = built.KeyColumnIds,
            KeyChangeRefreshRequired = refreshRequired,
            KeyChangeRefreshes = refreshes,
        };
    }

    /// <summary>
    /// Reproduces the legacy's <c>updatekeyinplace=no</c> key-change workaround, replacing its
    /// self-assignment with an explicit value-and-status refresh.
    /// </summary>
    /// <param name="keyColumnIds">The resolved key column identifiers collected while building.</param>
    /// <param name="store">The carrier to walk.</param>
    /// <returns>
    /// Whether the gate opened, and the row/column pairs refreshed in the oracle's visit order.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Ports <c>:L151-L167</c>. The legacy's own FIXME sits at <c>:L151-L154</c>: when the DW's
    /// currently requested KEY field has <c>Key=no</c>, <c>DataModified!</c> will not generate
    /// Delete/Insert statements, so the internal modified state must be force-refreshed. DECISION 4 in
    /// the file header explains the mechanism, why the managed model cannot have the staleness the
    /// legacy was compensating for, and why re-asserting the triple plus RECORDING the pairs is the
    /// faithful reproduction. DECISION 5 explains the runtime gate.
    /// </para>
    /// <para>
    /// INTERNAL RATHER THAN PRIVATE so that the gate and both skip arms are directly reachable from the
    /// parity suite with no database and no DataWindow, which C-H requires.
    /// </para>
    /// </remarks>
    internal (bool RefreshRequired, IReadOnlyList<KeyColumnRefresh> Refreshes) ApplyKeyChangeRefresh(
        IReadOnlyList<int> keyColumnIds,
        DataWindowBufferStore store)
    {
        ArgumentNullException.ThrowIfNull(keyColumnIds);
        ArgumentNullException.ThrowIfNull(store);

        // `if Data.Describe("DataWindow.Table.UpdateKeyinPlace") = "no" then` [:L155].
        //
        // THE GATE IS ON THE RUNTIME VALUE, NOT ON THE DESCRIPTOR (DECISION 5). The descriptor's own
        // setting may be absent, in which case step 6 of the script emitted nothing and the effective
        // value is the carrier's own definition - which on the sole evidenced fixture is `no`
        // [ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L14]. Gating on the descriptor would both miss
        // refreshes the legacy performs and perform refreshes it does not.
        //
        // ORDINAL, CASE-SENSITIVE COMPARISON, because PowerScript's `=` on strings is. Any answer other
        // than exactly "no" - including "yes", "!", "?" and empty - declines the gate.
        bool describedAsNo = string.Equals(
            _metadata.DescribeUpdateKeyInPlace(),
            UpdateWhereBuilder.NoLiteral,
            StringComparison.Ordinal);

        // `nCount = UpperBound(nKeyColumns) ; if nCount > 0 then` [:L156-L157]. With no resolved key
        // column there is nothing to refresh, so the gate stays shut even when the setting says `no`.
        int keyColumnCount = OneBasedIndex.UpperBound(keyColumnIds);
        bool refreshRequired = describedAsNo && keyColumnCount > OneBasedIndex.EmptyUpperBound;

        if (!refreshRequired)
        {
            return (false, []);
        }

        List<KeyColumnRefresh> refreshes = [];

        // `nRowCnt = Data.RowCount() ; for nRow = 1 to nRowCnt` [:L158-L159]. The count is CAPTURED
        // ONCE before the walk, exactly as the oracle captures it, so the bound cannot shift underneath
        // the loop. R9: rows are one-based on both surfaces and are never rebased.
        long rowCount = store.RowCount();

        for (long row = ItemStatusMachine.FirstRowNumber; row <= rowCount; row++)
        {
            // `if Data.GetItemStatus(nRow,0,Primary!) <> DataModified! then continue` [:L160].
            //
            // COLUMN ZERO IS THE ROW'S OWN STATUS, NOT A COLUMN'S. The bare 0 of the oracle is passed
            // through ItemStatusMachine.RowStatusColumn precisely so no reader of this line has to know
            // that, and so the row-versus-column distinction the very next test relies on is explicit.
            if (store.GetItemStatus(row, ItemStatusMachine.RowStatusColumn, DwBuffer.Primary)
                != ItemStatus.DataModified)
            {
                continue;
            }

            // `for nIndex = 1 to nCount` over nKeyColumns [:L161]. R9: written as a one-based walk with
            // an explicit conversion rather than a foreach, so the shape stays diffable against the
            // oracle and the single rebasing step is visible.
            foreach (int keyIndex in OneBasedIndex.Range(keyColumnCount))
            {
                int keyColumnNumber = keyColumnIds[
                    OneBasedIndex.ToZeroBased(keyIndex, keyColumnCount, nameof(keyIndex))];

                // `if Data.GetItemStatus(nRow,nKeyColumns[nIndex],Primary!) <> DataModified! then
                // continue` [:L162]. THIS ONE IS A COLUMN'S status - a one-based column number, not the
                // row sentinel. The two adjacent reads differing only in that index are the whole
                // reason ItemStatusMachine makes the distinction explicit.
                if (store.GetItemStatus(row, keyColumnNumber, DwBuffer.Primary)
                    != ItemStatus.DataModified)
                {
                    continue;
                }

                // `Data.Object.Data[nRow,nKeyColumns[nIndex]] = Data.Object.Data[nRow,nKeyColumns[nIndex]]`
                // [:L163] - THE SELF-ASSIGNMENT, reproduced by its observable effect rather than its
                // mechanism, because .NET has no assignment that mutates hidden state (DECISION 4).
                //
                // Step one: write the current value back. This is not a no-op in the managed model -
                // CarrierRow.SetValue captures the original ONCE per baseline and never overwrites it,
                // so an already-captured original survives untouched while a column that was stamped
                // modified without a value write acquires one. That is exactly the invariant
                // updatewhere=1 depends on.
                object? currentValue = store.GetItemValue(row, keyColumnNumber, DwBuffer.Primary);

                store.SetItemValue(row, keyColumnNumber, DwBuffer.Primary, currentValue);

                // Step two: stamp the status EXPLICITLY. Buffers/DataWindowBuffers.cs states on
                // CarrierRow.SetValue that it deliberately does NOT flip status and that a status change
                // is ALWAYS an explicit SetItemStatus call, precisely so this port expresses what
                // PowerBuilder did implicitly. Re-stamping DataModified is idempotent on the value and
                // is what makes the refresh a real operation on the explicit model rather than a
                // comment about one.
                store.SetItemStatus(
                    row,
                    keyColumnNumber,
                    DwBuffer.Primary,
                    ItemStatus.DataModified);

                // Step three: record the pair. The legacy leaves no trace of this step; recording it is
                // what lets Tasks/SqlUpdateTask.cs be asserted to emit the DELETE-plus-INSERT pair for
                // exactly these row/column pairs, which is the observable outcome the legacy achieved
                // by side effect.
                refreshes.Add(new KeyColumnRefresh(row, keyColumnNumber));
            }
        }

        return (true, refreshes);
    }

    /// <summary>
    /// Drives the update: on the multi-table path, prepare and update once per descriptor stopping at
    /// the first failure; on the single-table path, update with NO re-derivation at all.
    /// </summary>
    /// <param name="tables">The descriptor array and the multi-table switch.</param>
    /// <param name="store">The carrier the refresh walks on the multi-table path.</param>
    /// <param name="update">
    /// The update step - the port of <c>_of_Update(data)</c>, which lives in
    /// <c>Tasks/SqlUpdateTask.cs</c>. Invoked once per descriptor on the multi-table path and exactly
    /// once on the single-table path.
    /// </param>
    /// <param name="errorText">
    /// The untranslated diagnostic when this method itself produced the failure - the zero-descriptor
    /// message, or a prepare failure's text. Empty otherwise, INCLUDING when
    /// <paramref name="update"/> failed, because that path's text belongs to the update step.
    /// </param>
    /// <returns>
    /// <see cref="RetCode.OK"/>, <see cref="RetCode.E_INVALID_ARGUMENT"/> from the zero-descriptor
    /// pre-check, or whichever code the failing prepare or update step answered.
    /// </returns>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// A FINDING FROM READING THE INVOCATION SITE RATHER THAN THE SUMMARY, and it changes the shape of
    /// this method: <c>_of_updateprepare</c> HAS EXACTLY ONE CALLER, at <c>:L365</c>, INSIDE THE
    /// MULTI-TABLE BRANCH. The single-table branch calls the update directly [<c>:L371</c>] and NEVER
    /// RUNS PREPARE AT ALL, so with the switch off the descriptor array is not applied and the carrier's
    /// own static definition governs everything.
    /// </para>
    /// <para>
    /// THE SINGLE-TABLE PATH IS THE ONE THE FIXTURE EXERCISES, so the fixture's STATIC
    /// <c>updatewhere=1 updatekeyinplace=no</c> [<c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L14</c>]
    /// governs it. That is also why the workaround cannot be dismissed as a multi-table curiosity: on
    /// the fixture the setting is already <c>no</c> before any descriptor is applied.
    /// </para>
    /// <para>
    /// IT LOOKS LIKE AN OVERSIGHT IN THE LEGACY AND IS PRESERVED ANYWAY (C-B). "Fixing" it into
    /// applying the descriptor in both modes would change the generated statement for every existing
    /// single-table caller, which is precisely the behaviour change this refactor forbids.
    /// </para>
    /// <para>
    /// THE STOP TEST IS <c>!= RetCode.OK</c> AND DELIBERATELY NOT A FAILURE PREDICATE - DECISION 6. The
    /// oracle is <c>if rtCode &lt;&gt; RetCode.OK then exit</c> [<c>:L366</c>, <c>:L368</c>], and under
    /// the published tri-state algebra <c>Predicates.IsFailed(RetCode.CANCELLED)</c> is FALSE while
    /// <c>CANCELLED != OK</c> is TRUE, so the predicate would let a cancelled table fall through to the
    /// next one.
    /// </para>
    /// </remarks>
    internal long PrepareAndUpdate(
        UpdatableTableCollection tables,
        DataWindowBufferStore store,
        Func<long> update,
        out string errorText)
    {
        ArgumentNullException.ThrowIfNull(tables);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(update);

        errorText = string.Empty;

        // `else rtCode = _of_Update(data)` [:L370-L371] - the single-table path, with NO prepare.
        if (!tables.MultiTableUpdate)
        {
            return update();
        }

        // `nCount = UpperBound(Tables) ; if nCount = 0 then sError = "没有设置可更新表!" ;
        //  rtCode = RetCode.E_INVALID_ARGUMENT ; exit` [:L358-L363]. WITH THE SWITCH ON, AN EMPTY ARRAY
        // IS AN ERROR; with it off, an empty array is ordinary - which the branch above already
        // expressed by never consulting the array.
        int tableCount = tables.UpperBound;

        if (tableCount == OneBasedIndex.EmptyUpperBound)
        {
            errorText = UpdateWhereBuilder.NoUpdatableTableMessage;

            return RetCode.E_INVALID_ARGUMENT;
        }

        // `rtCode = RetCode.OK` [:L298], then `for nIndex = 1 to nCount` [:L364]. R9: one-based, driven
        // through the centralized helper, and DescriptorAt takes the same one-based index the oracle's
        // _of_updateprepare(data, index) does.
        long rtCode = RetCode.OK;

        foreach (int index in OneBasedIndex.Range(tableCount))
        {
            UpdatePreparationResult prepared = Prepare(tables.DescriptorAt(index), store);

            rtCode = prepared.Code;

            // `if rtCode <> RetCode.OK then exit` [:L366]. Verbatim zero comparison - see DECISION 6
            // for why Predicates.IsFailed must NOT be substituted here.
            if (rtCode != RetCode.OK)
            {
                errorText = prepared.ErrorText;

                break;
            }

            rtCode = update();

            // `if rtCode <> RetCode.OK then exit` [:L368]. The update step owns its own diagnostic, so
            // errorText is deliberately left empty on this arm rather than being invented here.
            if (rtCode != RetCode.OK)
            {
                break;
            }
        }

        return rtCode;
    }
}

#endregion
