// ==================================================================================================
//  DataWindowRuntime.cs - THE PROVISIONED RESULT-CARRIER RUNTIME
//  ------------------------------------------------------------------------------------------------
//  ROLE
//  The three seams through which a data-object NAME becomes a definition, a STATEMENT becomes a
//  result carrier, and a retrieval actually runs. Everything else about the result carrier - buffers,
//  item statuses, counts, the SQL-preview interception, the N-char rewrite - already belongs to
//  `Buffers/DataWindowBuffers.cs` and is neither reimplemented nor second-guessed here.
//
//  WHY THIS EXISTS, STATED AGAINST THE CONSTRAINT IT COULD BE MISREAD AS BREAKING (C-D)
//  An earlier revision shipped three implementations that materialised nothing, on the argument that
//  the legacy result carrier IS a DataWindow - `n_cst_thread_task_sqlbase_ds` is declared
//  `from datastore` [n_cst_thread_task_sqlbase_ds.sru:L4] - so materialising one requires a live
//  DataWindow engine, and that engine belongs to the deferred DesignSystem capability. The premise is
//  right about the legacy and wrong about what it implies here, and the distinction is the one the
//  migration draws everywhere else: what DesignSystem owns is the RENDERING half - window geometry,
//  DPI conversion, font measurement, painting. The DATA half - rows, columns, buffers, item statuses,
//  original values - is exactly what `Buffers/DataWindowBuffers.cs` already ports, in this project,
//  with no reference to any deferred type. These three seams do not build a DataWindow engine; they
//  bind the data half that already exists to the statement layer that already exists. No visual
//  object, no geometry, no font and no DPI value appears anywhere in this file.
//
//  WHAT A DEFINITION IS, AND WHERE ONE COMES FROM
//  The legacy assigns a name and the PowerBuilder runtime loads a compiled DataWindow out of the
//  target's library list: `ds.DataObject = dataObject` [n_cst_thread_task_sqlbase.sru:L558] is a
//  resolution against that list. There is no managed equivalent - the `.srd` objects live in the
//  read-only legacy tree and no .NET runtime can load them - so resolution is a CATALOGUE. The
//  published contract already anticipates this: `QuerySpec` carries `data_object`, `sql_syntax`,
//  `sql`, `filter` and `sort` side by side [persistence.v1.proto], so a caller may NAME a definition
//  or SUPPLY one outright. Two sources feed the catalogue and no third exists:
//    1. The one evidenced fixture, TRANSCRIBED as C# literals below, each carrying its locator.
//    2. Definitions registered at run time from a caller-supplied grid syntax, which is the
//       `sql_syntax` field's whole purpose.
//
//  LEGACY REFERENCE (read only - never edited, never built, never shipped: constraint C-C)
//      ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru
//          :L558       ds.DataObject = dataObject, the resolution this catalogue replaces
//          :L649-L702  Data.Retrieve(...), the call this runtime performs
//      ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru
//          :L120       Data.GetChild(sColName, ref dwcSrc) - a -1 result CONTINUES, it is not an error
//          :L554-L557  the DataWindow.Units probe that detects an unresolved data object
//          :L610       of_GridSyntaxFromSQL(sSQL, ref sError)
//          :L619       data.Create(sSQLSyntax, ref sError)
//          :L678       data.SetTransObject(TransObject)
//          :L746, :L769  the before- and after-retrieve events on the transaction object
//          :L777       the nRowCnt < 0 failure test - the DATASTORE alphabet, not the return codes
//          :L843-L852  of_Query, whose only caller reads row 1 column 1
//      ws_objects/pfw.tests.pbl.src/dw_sqlite.srd
//          :L2         release 12.5
//          :L8-L14     the six columns and the table specification, transcribed below
//          :L27        the page-scoped footer aggregate
//      ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L463-L469  the estate's only DDL
//
//  NOTHING IN THIS FILE READS THE LEGACY TREE. Every ws_objects/** path above appears in a comment,
//  and every fixture value below is a transcribed literal carrying the locator it came from. The root
//  .dockerignore excludes ws_objects/ from the build context, so a read would fail in a container and
//  in CI even where it happened to work locally.
//
//  WHAT THIS FILE DELIBERATELY DOES NOT DO
//    * It creates no table and runs no DDL. The schema is owned by the EF Core migration under
//      `Data/Migrations/`, and a runtime that created tables on demand would fabricate schema.
//    * It never deletes, recreates or reseeds anything, and issues no DROP - see the paired-capture
//      rule in `Data/SqliteTransactionEngine.cs`.
//    * It reads no clock and holds no state that outlives a store.
//    * It throws nothing across a seam boundary. Every fault becomes the negative the consuming
//      signature already documents, because the ported arms are written against those values.
// ==================================================================================================

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using PowerFramework.Contracts.Common.V1;
using PowerFramework.Persistence.Buffers;
using PowerFramework.Persistence.Tasks;
using PowerFramework.Persistence.Runtime;
using PowerFramework.Persistence.Transactions;

// ALIASED RATHER THAN IMPORTED WHOLESALE, AND THE ALIAS IS LOAD BEARING. Both
// PowerFramework.Shared.Kernel and PowerFramework.Contracts.Common.V1 declare a `RetCode`, and this file
// needs types from both namespaces - DwBuffer and ItemStatus from the contract, the return-code algebra
// from the kernel. The alias names the KERNEL one, which is the algebra every predicate in this service is
// written against; the wire enum is reached through its own qualified name where a projection needs it.
using Predicates = PowerFramework.Shared.Kernel.Predicates;
using RetCode = PowerFramework.Shared.Kernel.RetCode;

namespace PowerFramework.Persistence.Data
{
    /// <summary>
    /// Resolves a data-object name to its definition, and accepts definitions derived at run time from
    /// a caller-supplied grid syntax.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A SINGLETON WITH A CONCURRENT STORE, because a run-time registration made by one call must be
    /// visible to the next: a caller supplies a syntax on one request, is handed a task handle, and
    /// retrieves through it on later requests. A scoped catalogue would forget the definition between
    /// the two and the second call would report an unresolved data object for a definition the caller
    /// had just been told it owned.
    /// </para>
    /// <para>
    /// Names are compared ORDINALLY and case-insensitively. PowerBuilder object names are
    /// case-insensitive, so a culture-sensitive comparison could silently merge or separate two names
    /// depending on the host's locale - which is exactly the class of defect the determinism model
    /// exists to exclude.
    /// </para>
    /// </remarks>
    /// <summary>
    /// One column of a data-object definition as its SYNTAX declares it: position, name and declared type.
    /// </summary>
    /// <param name="Number">The ONE-BASED column position. Never translated to zero-based here.</param>
    /// <param name="Name">The column name the syntax carries.</param>
    /// <param name="DeclaredType">
    /// The type the DataWindow declares for the column, verbatim - <c>char(200)</c>, <c>decimal(2)</c>,
    /// <c>date</c>, <c>long</c>.
    /// </param>
    /// <remarks>
    /// <b>THE DECLARED TYPE IS CARRIED BECAUSE THE ORACLE'S TYPES DISAGREE WITH ITS OWN SCHEMA, AND THE
    /// DISAGREEMENT IS A PRESERVED DEFECT (AAP 0.6.4).</b> The evidenced DataWindow declares a
    /// 200-character address against a 50-character column, a two-place decimal salary against a REAL
    /// column and a date birth field against a TEXT column [<c>dw_sqlite.srd:L8-L14</c> against
    /// <c>w_test_sqlite.srw:L463-L469</c>]. A runtime that dropped the declared type could not reproduce
    /// those mismatches, and reproducing them is required rather than optional.
    /// </remarks>
    internal sealed record DeclaredDataObjectColumn(int Number, string Name, string DeclaredType);

    /// <summary>
    /// A resolved definition together with the columns its syntax declares.
    /// </summary>
    /// <param name="Definition">The retrieval definition: statement, sort, filter and processing.</param>
    /// <param name="Columns">The declared columns, in one-based column order.</param>
    /// <remarks>
    /// TWO SEPARATE FACTS, PAIRED RATHER THAN FUSED. The definition is what a retrieval needs; the columns
    /// are what a caller describing the result needs. Keeping them as one record means a resolution answers
    /// both questions at once, and keeping them as separate members means neither has to be derived from
    /// the other's text.
    /// </remarks>
    internal sealed record DataObjectDefinitionEntry(
        DataObjectDefinition Definition,
        IReadOnlyList<DeclaredDataObjectColumn> Columns);

    /// <summary>
    /// The TABLE-LEVEL update settings a definition declares, which arrive with the definition rather
    /// than from a caller.
    /// </summary>
    /// <param name="Table">
    /// The update table - <c>update="COMPANY"</c> [<c>dw_sqlite.srd:L14</c>] - or the empty string for a
    /// retrieve-only definition, which is what a definition DERIVED from a bare statement is.
    /// </param>
    /// <param name="UpdateWhereMode">
    /// The optimistic-concurrency mode - <c>updatewhere=1</c>, "key and updateable columns", the mode
    /// that puts every marked column's ORIGINAL value in the generated where-clause (AAP 0.6.3.2).
    /// </param>
    /// <param name="UpdateKeyInPlace">
    /// <c>updatekeyinplace</c>. <see langword="false"/> - which the evidenced fixture declares - means a
    /// key change is applied as delete-plus-insert rather than in place, and it is the trigger for the
    /// legacy's own self-assignment workaround [<c>n_cst_thread_task_sqlupdate.sru:L151-L167</c>].
    /// </param>
    /// <param name="KeyColumns">The columns forming the update key, by DataWindow column name.</param>
    /// <param name="IdentityColumn">The column the database assigns, or the empty string for none.</param>
    /// <remarks>
    /// <para>
    /// <b>WHY THE DEFINITION CARRIES THIS AT ALL, AND WHY IT IS NOT A CALLER CONCERN.</b> The legacy's
    /// single-table update path performs NO PREPARE - it goes straight to <c>_of_Update(data)</c>
    /// [<c>n_cst_thread_task_sqlupdate.sru:L370-L371</c>] - and it works because
    /// <c>ds.DataObject = name</c> LOADED the compiled DataWindow, whose own table specification already
    /// carries <c>update=</c>, <c>updatewhere=</c> and <c>updatekeyinplace=</c>
    /// [<c>n_cst_thread_task_sqlbase.sru:L558</c>, <c>dw_sqlite.srd:L14</c>]. The update path then reads
    /// them back through <c>Describe</c>, and its own guard is
    /// <c>Describe("DataWindow.Table.UpdateTable") &lt;&gt; "?" or _bMultiTableUpdate</c>. A port whose
    /// definitions carried only a statement would answer nothing to that describe, so every single-table
    /// update would refuse with the no-updatable-table database error - the correct answer to a question
    /// the port had failed to let the definition answer.
    /// </para>
    /// <para>
    /// THE PREPARE STEP STILL OVERRIDES ALL OF IT. <c>_of_updateprepare</c> resets update, key and
    /// identity to off on every column and re-enables from a caller's descriptor array
    /// [<c>:L104-L129</c>], so these are DEFAULTS carried by the definition and never a ceiling on what a
    /// caller may ask for.
    /// </para>
    /// </remarks>
    internal sealed record DataObjectUpdateSettings(
        string Table,
        long UpdateWhereMode,
        bool UpdateKeyInPlace,
        IReadOnlyList<string> KeyColumns,
        string IdentityColumn)
    {
        /// <summary>The settings a retrieve-only definition carries: no table, and nothing to update.</summary>
        /// <remarks>
        /// USED BY EVERY DERIVED DEFINITION, deliberately. <c>data.Create(syntax)</c> produces a
        /// DataWindow from a bare statement, and a bare statement declares no update table - so a derived
        /// definition must NOT inherit the evidenced fixture's contract, which would let an update reach
        /// a table the caller never named.
        /// </remarks>
        internal static DataObjectUpdateSettings RetrieveOnly { get; } =
            new(string.Empty, 0L, UpdateKeyInPlace: true, [], string.Empty);
    }

    internal sealed class DataObjectDefinitionCatalogue
    {
        /// <summary>
        /// The name of the one evidenced fixture.
        /// </summary>
        /// <remarks>
        /// <c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd</c> - the ONLY updatable DataWindow in the whole
        /// repository and therefore the golden master for the retrieval / validation / update triple.
        /// </remarks>
        internal const string EvidencedDataObject = "dw_sqlite";

        /// <summary>The evidenced fixture's retrieval statement [<c>dw_sqlite.srd:L14</c>].</summary>
        internal const string EvidencedSelect = "SELECT * FROM COMPANY";

        /// <summary>The evidenced fixture's sort expression [<c>dw_sqlite.srd:L14</c>].</summary>
        /// <remarks>
        /// Transcribed WITH its trailing space, because that is how the fixture spells it - a value that
        /// travels into log records and characterization recordings, where a tidied form would silently
        /// invalidate every stored comparison.
        /// </remarks>
        internal const string EvidencedSort = "age A salary A ";

        /// <summary>The evidenced fixture's update table [<c>dw_sqlite.srd:L14</c>].</summary>
        internal const string EvidencedUpdateTable = "COMPANY";

        /// <summary>
        /// The evidenced fixture's processing mode.
        /// </summary>
        /// <remarks>
        /// A grid DataWindow, which is what <c>DataWindow.Processing</c> answers for the fixture's
        /// presentation style. The value matters because the carrier's transfer-path discriminator is
        /// projected from it, and a crosstab or composite value would select a different transfer path.
        /// </remarks>
        internal const string EvidencedProcessing = "0";

        /// <summary>The unit system a resolved definition reports.</summary>
        /// <remarks>
        /// 🔴 LOAD BEARING, AND NOT A COSMETIC VALUE. The retrieval task detects an unresolved data
        /// object by probing <c>DataWindow.Units</c> and testing the describe failure sentinel
        /// [<c>n_cst_thread_task_sqlquery.sru:L554-L557</c>]. A definition whose units described as the
        /// sentinel would read as unresolved even though it had resolved, so every definition this
        /// catalogue produces reports a real unit system. Zero is PowerBuilder units, the default.
        /// </remarks>
        internal const string ResolvedUnits = "0";

        /// <summary>The retrieval-argument declaration a definition with no arguments reports.</summary>
        /// <remarks>
        /// EMPTY, and empty is a real answer rather than a missing one: the argument matcher reads this
        /// to discover how many retrieval arguments a definition declares, and the evidenced fixture
        /// declares none - its statement carries no argument reference at all.
        /// </remarks>
        internal const string NoArguments = "";

        /// <summary>The six columns of the evidenced fixture, in declaration order.</summary>
        /// <remarks>
        /// Transcribed from <c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L8-L14</c>, whose column
        /// order is the order the DDL declares [<c>w_test_sqlite.srw:L463-L469</c>]: the identifier
        /// column as key AND identity, then name, age, address, salary and birth date. Order is
        /// contract - the carrier is column-NUMBER indexed and one-based, so a reordering here would
        /// misalign every value the update contract compares.
        /// </remarks>
        internal static readonly string[] EvidencedColumns =
            ["id", "name", "age", "address", "salary", "birth"];

        /// <summary>
        /// The evidenced fixture's own table-level update contract [<c>dw_sqlite.srd:L8-L14</c>].
        /// </summary>
        /// <remarks>
        /// TRANSCRIBED, NOT CHOSEN. The table specification reads
        /// <c>update="COMPANY" updatewhere=1 updatekeyinplace=no</c>, and the identifier column is the one
        /// column marked both key and identity while all six carry <c>updatewhereclause=yes</c>. These are
        /// the settings the single-table update path reads back through <c>Describe</c>, so getting any of
        /// them wrong changes the generated statement rather than merely a report.
        /// </remarks>
        internal static DataObjectUpdateSettings EvidencedUpdateSettings { get; } = new(
            EvidencedUpdateTable,
            UpdateWhereMode: 1L,
            UpdateKeyInPlace: false,
            KeyColumns: ["id"],
            IdentityColumn: "id");

        /// <summary>Every registered definition, keyed by data-object name.</summary>
        private readonly Dictionary<string, DataObjectDefinition> _definitions =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>The table-level update settings of every definition that declares any, by name.</summary>
        /// <remarks>
        /// HELD BESIDE THE DEFINITIONS for the same reason the columns are: <see cref="DataObjectDefinition"/>
        /// is the RETRIEVAL contract the task layer consumes, and widening it with update settings would
        /// push them through every call that only wants a statement.
        /// </remarks>
        private readonly Dictionary<string, DataObjectUpdateSettings> _updateSettings =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Guards <see cref="_definitions"/> against concurrent registration and lookup.</summary>
        private readonly object _gate = new();

        /// <summary>Initializes the catalogue with the one evidenced definition.</summary>
        public DataObjectDefinitionCatalogue()
        {
            _updateSettings[EvidencedDataObject] = EvidencedUpdateSettings;

            _definitions[EvidencedDataObject] = new DataObjectDefinition(
                DataObject: EvidencedDataObject,
                SqlSelect: EvidencedSelect,
                Sort: EvidencedSort,
                Filter: string.Empty,
                Processing: EvidencedProcessing,
                Arguments: NoArguments,
                Units: ResolvedUnits);
        }

        /// <summary>The declared columns of every definition that carries them, by name.</summary>
        /// <remarks>
        /// <para>
        /// HELD BESIDE THE DEFINITIONS RATHER THAN INSIDE THEM, because <see cref="DataObjectDefinition"/>
        /// is the retrieval contract the task layer consumes and adding a presentation-shaped member to it
        /// would push column declarations through every call that only wants a statement.
        /// </para>
        /// <para>
        /// CASED THE SAME WAY <see cref="_definitions"/> IS, and that matters rather than being tidiness.
        /// A name resolves its definition case-insensitively, so an ordinally-cased column table would
        /// resolve the definition and MISS its columns for any spelling that differed in case - and the
        /// miss is silent, because the resolution below falls back to the evidenced fixture's own six
        /// columns. A caller would then be told about six columns of a table it never named.
        /// </para>
        /// </remarks>
        private readonly Dictionary<string, DeclaredDataObjectColumn[]> _columns =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>The monotonic counter every minted name is drawn from.</summary>
        private long _synthetic;

        /// <summary>
        /// The evidenced DataWindow's own declared columns, with the types it declares rather than the
        /// types its schema has.
        /// </summary>
        /// <remarks>
        /// <b>THREE OF THESE SIX DISAGREE WITH THE DDL, AND THE DISAGREEMENT IS REPRODUCED (AAP 0.6.4).</b>
        /// The address is declared 200 characters against a 50-character column, the salary a two-place
        /// decimal against a REAL, and the birth field a date against TEXT
        /// [<c>dw_sqlite.srd:L8-L14</c> against <c>w_test_sqlite.srw:L463-L469</c>]. These are the
        /// DataWindow's declarations, so they are what a caller describing the result is told.
        /// </remarks>
        internal static readonly DeclaredDataObjectColumn[] EvidencedDeclaredColumns =
        [
            // TRANSCRIBED FROM THE .srd, TYPE TOKEN FOR TYPE TOKEN. `number` is PowerBuilder's
            // DOUBLE-PRECISION DataWindow type, and both the identifier and the age column declare it -
            // even though the DDL makes them `INTEGER` and `INT` [w_test_sqlite.srw:L463-L469]. Writing
            // `long` here instead would silently reconcile a divergence the migration is required to
            // preserve, and it would do so where nothing could see it: the carrier would hold an integer
            // where the DataWindow says a number, and every value the update contract compares would be
            // of a type the oracle never produced (AAP 0.6.4).
            new(1, "id", "number"),
            new(2, "name", "char(100)"),
            new(3, "age", "number"),
            new(4, "address", "char(200)"),
            new(5, "salary", "decimal(2)"),
            new(6, "birth", "date"),
        ];

        /// <summary>
        /// Resolves a name to its definition.
        /// </summary>
        /// <param name="dataObject">The data-object name.</param>
        /// <param name="definition">Receives the definition, or <see langword="null"/> when unresolved.</param>
        /// <returns><see langword="true"/> when the name resolved.</returns>
        internal bool TryGet(string dataObject, [NotNullWhen(true)] out DataObjectDefinition? definition)
        {
            ArgumentNullException.ThrowIfNull(dataObject);

            lock (_gate)
            {
                return _definitions.TryGetValue(dataObject, out definition);
            }
        }

        /// <summary>
        /// Registers or replaces a definition derived at run time.
        /// </summary>
        /// <param name="definition">The definition to register.</param>
        /// <param name="columns">
        /// The columns the definition's syntax declares, or <see langword="null"/> when the caller has
        /// none to record.
        /// </param>
        /// <remarks>
        /// <para>
        /// REPLACEMENT IS DELIBERATE. A caller that supplies a second syntax under a name it already
        /// used is redefining its own definition, which is exactly what the legacy's
        /// <c>data.Create(...)</c> does to a datastore that already had one. Refusing the second
        /// registration would make a caller's own sequence of calls fail for a reason it could not see.
        /// </para>
        /// <para>
        /// <b>THE DECLARED COLUMNS TRAVEL WITH THE DEFINITION, and dropping them is not harmless.</b> A
        /// derived definition registered without them resolves through <see cref="TryResolve"/> to the
        /// EVIDENCED fixture's six columns, because that is the fallback - so a two-column statement
        /// would answer six columns of a table it never named, and a caller walking those columns for a
        /// drop-down child or an identity would be walking a different DataWindow's declarations. Both
        /// registration sites derive from a grid syntax and therefore have the declarations in hand.
        /// </para>
        /// </remarks>
        internal void Register(
            DataObjectDefinition definition,
            IReadOnlyList<DeclaredDataObjectColumn>? columns = null)
        {
            ArgumentNullException.ThrowIfNull(definition);

            lock (_gate)
            {
                _definitions[definition.DataObject] = definition;

                if (columns is { Count: > 0 })
                {
                    _columns[definition.DataObject] = [.. columns];
                }

                // A DERIVED DEFINITION IS RETRIEVE-ONLY UNLESS SOMETHING SAYS OTHERWISE. It came from a
                // bare statement, and a bare statement declares no update table - so it must not inherit
                // the evidenced fixture's contract and let an update reach a table nobody named. Written
                // rather than left absent so the resolution below never has to guess a default.
                if (!_updateSettings.ContainsKey(definition.DataObject))
                {
                    _updateSettings[definition.DataObject] = DataObjectUpdateSettings.RetrieveOnly;
                }
            }
        }

        /// <summary>
        /// Resolves a name to the table-level update settings its definition declares.
        /// </summary>
        /// <param name="dataObject">The data-object name.</param>
        /// <param name="settings">
        /// Receives the settings, or <see langword="null"/> when the name resolves to no definition at all.
        /// </param>
        /// <returns><see langword="true"/> when the name resolved.</returns>
        /// <remarks>
        /// A RESOLVED NAME ALWAYS ANSWERS, and a retrieve-only definition answers settings whose table is
        /// EMPTY rather than answering <see langword="false"/> - so a caller can tell "no such definition"
        /// from "that definition cannot be updated", which are different faults with different remedies.
        /// </remarks>
        internal bool TryResolveUpdateSettings(
            string dataObject,
            [NotNullWhen(true)] out DataObjectUpdateSettings? settings)
        {
            ArgumentNullException.ThrowIfNull(dataObject);

            lock (_gate)
            {
                if (!_definitions.ContainsKey(dataObject))
                {
                    settings = null;

                    return false;
                }

                settings = _updateSettings.TryGetValue(dataObject, out DataObjectUpdateSettings? declared)
                    ? declared
                    : DataObjectUpdateSettings.RetrieveOnly;

                return true;
            }
        }

        /// <summary>The prefix every runtime-derived data-object name carries.</summary>
        /// <remarks>
        /// A NAME A CALLER CANNOT HAVE CONFIGURED, so a derived definition can never collide with a named
        /// one and a reader can tell the two apart at a glance in a diagnostic.
        /// </remarks>
        internal const string SyntheticNamePrefix = "pfw_derived_";

        /// <summary>
        /// Resolves a name to its definition AND the columns its syntax declares.
        /// </summary>
        /// <param name="dataObject">The data-object name.</param>
        /// <param name="entry">The resolved pair, or <see langword="null"/> when unresolved.</param>
        /// <returns><see langword="true"/> when the name resolved.</returns>
        internal bool TryResolve(
            string dataObject,
            [NotNullWhen(true)] out DataObjectDefinitionEntry? entry)
        {
            ArgumentNullException.ThrowIfNull(dataObject);

            lock (_gate)
            {
                if (!_definitions.TryGetValue(dataObject, out DataObjectDefinition? definition))
                {
                    entry = null;

                    return false;
                }

                entry = new DataObjectDefinitionEntry(
                    definition,
                    _columns.TryGetValue(dataObject, out DeclaredDataObjectColumn[]? declared)
                        ? declared
                        : EvidencedDeclaredColumns);

                return true;
            }
        }

        /// <summary>
        /// Registers a definition derived at run time under a freshly minted name.
        /// </summary>
        /// <param name="entry">The derived definition and its declared columns.</param>
        /// <returns>The minted name, which the caller assigns to its store.</returns>
        /// <remarks>
        /// A MINTED NAME RATHER THAN A CALLER-SUPPLIED ONE, because a derived definition has no name in the
        /// oracle at all: <c>data.Create(syntax)</c> replaces a datastore's definition without naming it.
        /// Minting one lets every other member of this surface keep working by name, and the monotonic
        /// counter guarantees two derivations never share a name however identical their syntax.
        /// </remarks>
        internal string RegisterSynthetic(DataObjectDefinitionEntry entry)
        {
            ArgumentNullException.ThrowIfNull(entry);

            lock (_gate)
            {
                string minted = string.Concat(
                    SyntheticNamePrefix,
                    (++_synthetic).ToString(CultureInfo.InvariantCulture));

                _definitions[minted] = entry.Definition with { DataObject = minted };
                _columns[minted] = [.. entry.Columns];

                // Retrieve-only, for the reason recorded on Register: a derived definition names no
                // update table because the statement it was derived from names none.
                _updateSettings[minted] = DataObjectUpdateSettings.RetrieveOnly;

                return minted;
            }
        }
    }

    /// <summary>
    /// Associates a result store with the pooled transaction attached to it, and with the columns its
    /// definition declares.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHY AN ASSOCIATION RATHER THAN A FIELD ON THE STORE. The legacy fuses the two: a datastore holds
    /// its own transaction pointer because <c>SetTransObject</c> writes into the object itself. In this
    /// port the store's interface is the DEFINITION half and the carrier is the DATA half, and neither
    /// declares a transaction member - deliberately, because a transaction is a connection and a carrier
    /// is a rowset. This table is the join, and keeping it here rather than widening either interface is
    /// what stops a transaction handle leaking into the buffer model that the codecs serialize.
    /// </para>
    /// <para>
    /// A <see cref="ConditionalWeakTable{TKey, TValue}"/> rather than a dictionary, so an entry cannot
    /// outlive the store it describes. A task's datastore cache is released when its host is
    /// uninitialised, and a strong-keyed dictionary here would pin every store a process had ever
    /// created for the life of the process.
    /// </para>
    /// </remarks>
    internal sealed class DataWindowStoreBindings
    {
        /// <summary>The transaction attached to each store.</summary>
        private readonly ConditionalWeakTable<ISqlDataStore, IPooledTransaction> _transactions = [];

        /// <summary>The column names each store's definition declares, in one-based column order.</summary>
        private readonly ConditionalWeakTable<ISqlDataStore, string[]> _columns = [];

        /// <summary>
        /// Records the transaction attached to a store, replacing any prior attachment.
        /// </summary>
        /// <param name="store">The store.</param>
        /// <param name="transaction">The transaction.</param>
        internal void AttachTransaction(ISqlDataStore store, IPooledTransaction transaction)
        {
            ArgumentNullException.ThrowIfNull(store);
            ArgumentNullException.ThrowIfNull(transaction);

            _transactions.Remove(store);
            _transactions.Add(store, transaction);
        }

        /// <summary>
        /// Reads the transaction attached to a store.
        /// </summary>
        /// <param name="store">The store.</param>
        /// <returns>The transaction, or <see langword="null"/> when none is attached.</returns>
        internal IPooledTransaction? GetTransaction(ISqlDataStore store)
        {
            ArgumentNullException.ThrowIfNull(store);

            return _transactions.TryGetValue(store, out IPooledTransaction? transaction)
                ? transaction
                : null;
        }

        /// <summary>
        /// Records the column names a store's definition declares.
        /// </summary>
        /// <param name="store">The store.</param>
        /// <param name="columns">The column names, in one-based column order.</param>
        internal void SetColumns(ISqlDataStore store, string[] columns)
        {
            ArgumentNullException.ThrowIfNull(store);
            ArgumentNullException.ThrowIfNull(columns);

            _columns.Remove(store);
            _columns.Add(store, columns);
        }

        /// <summary>
        /// Reads the column names a store's definition declares.
        /// </summary>
        /// <param name="store">The store.</param>
        /// <returns>The column names, or an empty array when none were recorded.</returns>
        internal string[] GetColumns(ISqlDataStore store)
        {
            ArgumentNullException.ThrowIfNull(store);

            return _columns.TryGetValue(store, out string[]? columns) ? columns : [];
        }
    }

    /// <summary>
    /// Derives a grid DataWindow syntax from a statement, and reads the column list back out of one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A PURE STRING TRANSFORM IN BOTH DIRECTIONS, which is what makes it testable with no database and
    /// no provider. The shape produced is the grid form PowerBuilder's own syntax generator emits,
    /// narrowed to the two parts anything in this service reads back: the table's retrieval statement
    /// and its column list. Nothing here emits geometry, colour, font or DPI - those belong to the
    /// deferred rendering half and their absence is what keeps this transform inside constraint C-D.
    /// </para>
    /// <para>
    /// The emitted text is deliberately stable and ordinal: it travels into characterization recordings
    /// through the SQL-preview channel, so a reordering or a whitespace change would invalidate stored
    /// comparisons.
    /// </para>
    /// </remarks>
    internal static class GridSyntax
    {
        /// <summary>The prefix of the table clause.</summary>
        internal const string TablePrefix = "table(";

        /// <summary>The prefix of a column declaration inside the table clause.</summary>
        internal const string ColumnPrefix = "column=(name=";

        /// <summary>The prefix of the retrieval clause inside the table clause.</summary>
        internal const string SelectPrefix = "retrieve=\"";

        /// <summary>The fixed presentation clause every emitted syntax carries.</summary>
        /// <remarks>
        /// The grid style, named and nothing more. A style token is required for the text to be a
        /// well-formed DataWindow syntax at all; every attribute that would describe how the grid LOOKS
        /// is deliberately absent, because this service renders nothing.
        /// </remarks>
        internal const string StyleClause = "style(type=grid)";

        /// <summary>
        /// Builds a grid syntax from a statement and its column list.
        /// </summary>
        /// <param name="sql">The retrieval statement.</param>
        /// <param name="columns">The column names, in one-based column order.</param>
        /// <returns>The syntax text.</returns>
        internal static string From(string sql, IReadOnlyList<string> columns)
        {
            ArgumentNullException.ThrowIfNull(sql);
            ArgumentNullException.ThrowIfNull(columns);

            StringBuilder syntax = new(TablePrefix);

            foreach (string column in columns)
            {
                _ = syntax.Append(ColumnPrefix).Append(column).Append(") ");
            }

            // The statement is quoted, and any embedded quote is doubled so the clause stays parseable.
            // Statements reaching here are composed by this service's own statement model, so an
            // embedded quote is rare - but a syntax that could not round trip one would be a defect
            // that only showed up on a caller's data.
            _ = syntax
                .Append(SelectPrefix)
                .Append(sql.Replace("\"", "\"\"", StringComparison.Ordinal))
                .Append("\") ")
                .Append(StyleClause);

            return syntax.ToString();
        }

        /// <summary>
        /// Reads the column list back out of a grid syntax.
        /// </summary>
        /// <param name="syntax">The syntax text.</param>
        /// <returns>The column names in declaration order; empty when the text declares none.</returns>
        /// <remarks>
        /// Tolerant by design, and that is the legacy behaviour rather than laxity: PowerBuilder's
        /// <c>Create</c> reports a diagnostic for a malformed syntax and leaves the datastore without a
        /// definition, it does not raise. A caller-supplied syntax that declares no column therefore
        /// yields an empty list, which the caller of this method turns into that same diagnostic.
        /// </remarks>
        /// <summary>
        /// Builds a grid syntax from a statement and its DECLARED columns, types included.
        /// </summary>
        /// <param name="sql">The retrieval statement.</param>
        /// <param name="columns">The declared columns, in one-based column order.</param>
        /// <returns>The syntax text.</returns>
        /// <remarks>
        /// THE TYPE IS EMITTED, WHICH IS THE WHOLE DIFFERENCE FROM <see cref="From"/>. A syntax carrying
        /// only names cannot round-trip a declared type, so a definition derived from it would silently
        /// lose the oracle's own type declarations - including the three that disagree with the schema and
        /// are preserved deliberately.
        /// </remarks>
        internal static string Compose(string sql, IReadOnlyList<DeclaredDataObjectColumn> columns)
        {
            ArgumentNullException.ThrowIfNull(sql);
            ArgumentNullException.ThrowIfNull(columns);

            StringBuilder syntax = new(TablePrefix);

            foreach (DeclaredDataObjectColumn column in columns)
            {
                _ = syntax
                    .Append(ColumnPrefix)
                    .Append(column.Name)
                    .Append(" dbname=\"")
                    .Append(column.Name)
                    .Append("\" type=")
                    .Append(column.DeclaredType)
                    .Append(") ");
            }

            _ = syntax
                .Append(SelectPrefix)
                .Append(sql)
                .Append("\" ) ")
                .Append(StyleClause);

            return syntax.ToString();
        }

        /// <summary>
        /// Reads a grid syntax back into a definition and its declared columns.
        /// </summary>
        /// <param name="syntax">The syntax text.</param>
        /// <param name="entry">The parsed pair, or <see langword="null"/> when the text will not parse.</param>
        /// <param name="error">The refusal reason, or the empty string on success.</param>
        /// <returns><see langword="true"/> when the text parsed.</returns>
        /// <remarks>
        /// <para>
        /// THE ROUND TRIP IS THE POINT. A caller composes a syntax, a datastore is created from it, and the
        /// definition behind it has to be recoverable - so this is the inverse of <see cref="Compose"/> and
        /// is asserted against it rather than against a hand-written sample.
        /// </para>
        /// <para>
        /// A REFUSAL NAMES THE RULE AND NEVER THE TEXT. The syntax can carry a statement, and a statement
        /// can carry interpolated literal values, so echoing the input into a diagnostic would put caller
        /// data in a log record (constraint C-F).
        /// </para>
        /// </remarks>
        internal static bool TryParse(
            string syntax,
            [NotNullWhen(true)] out DataObjectDefinitionEntry? entry,
            out string error)
        {
            ArgumentNullException.ThrowIfNull(syntax);

            entry = null;

            if (!syntax.StartsWith(TablePrefix, StringComparison.Ordinal))
            {
                error = "The text does not begin with a table clause, so it is not a DataWindow syntax.";

                return false;
            }

            string select = SelectOf(syntax);

            if (select.Length == 0)
            {
                error = "The table clause declares no retrieval statement.";

                return false;
            }

            List<DeclaredDataObjectColumn> declared = [];
            int cursor = 0;

            while (true)
            {
                int start = syntax.IndexOf(ColumnPrefix, cursor, StringComparison.Ordinal);

                if (start < 0)
                {
                    break;
                }

                start += ColumnPrefix.Length;

                int nameEnd = syntax.IndexOf(' ', start);

                if (nameEnd < 0)
                {
                    break;
                }

                string name = syntax[start..nameEnd];
                string type = string.Empty;
                int typeAt = syntax.IndexOf(" type=", nameEnd, StringComparison.Ordinal);

                if (typeAt >= 0)
                {
                    typeAt += " type=".Length;

                    int typeEnd = syntax.IndexOf(')', typeAt);

                    if (typeEnd > typeAt)
                    {
                        type = syntax[typeAt..typeEnd];
                    }
                }

                declared.Add(new DeclaredDataObjectColumn(declared.Count + 1, name, type));

                cursor = nameEnd;
            }

            if (declared.Count == 0)
            {
                error = "The table clause declares no column.";

                return false;
            }

            error = string.Empty;

            entry = new DataObjectDefinitionEntry(
                new DataObjectDefinition(
                    DataObject: string.Empty,
                    SqlSelect: select,
                    Sort: string.Empty,
                    Filter: string.Empty,
                    Processing: DataObjectDefinitionCatalogue.EvidencedProcessing,
                    Arguments: DataObjectDefinitionCatalogue.NoArguments,
                    Units: DataObjectDefinitionCatalogue.ResolvedUnits),
                declared);

            return true;
        }

        internal static string[] ColumnsOf(string syntax)
        {
            ArgumentNullException.ThrowIfNull(syntax);

            List<string> columns = [];
            int position = 0;

            while (true)
            {
                int start = syntax.IndexOf(ColumnPrefix, position, StringComparison.OrdinalIgnoreCase);

                if (start < 0)
                {
                    break;
                }

                start += ColumnPrefix.Length;

                int end = syntax.IndexOf(')', start);

                if (end < 0)
                {
                    break;
                }

                string name = syntax[start..end].Trim();

                if (name.Length > 0)
                {
                    columns.Add(name);
                }

                position = end + 1;
            }

            return [.. columns];
        }

        /// <summary>
        /// Reads the retrieval statement back out of a grid syntax.
        /// </summary>
        /// <param name="syntax">The syntax text.</param>
        /// <returns>The statement, or the empty string when the text declares none.</returns>
        internal static string SelectOf(string syntax)
        {
            ArgumentNullException.ThrowIfNull(syntax);

            int start = syntax.IndexOf(SelectPrefix, StringComparison.OrdinalIgnoreCase);

            if (start < 0)
            {
                return string.Empty;
            }

            start += SelectPrefix.Length;

            int end = syntax.LastIndexOf('"');

            return end > start
                ? syntax[start..end].Replace("\"\"", "\"", StringComparison.Ordinal)
                : string.Empty;
        }
    }

    /// <summary>
    /// The provisioned <see cref="IDataObjectRuntime"/>: it resolves a name against the catalogue and
    /// runs a retrieval against the store's attached transaction.
    /// </summary>
    internal sealed class SqliteDataObjectRuntime : IDataObjectRuntime
    {
        /// <summary>The definition catalogue.</summary>
        private readonly DataObjectDefinitionCatalogue _catalogue;

        /// <summary>The store-to-transaction association.</summary>
        private readonly DataWindowStoreBindings _bindings;

        /// <summary>Records retrieval faults the datastore alphabet alone would not explain.</summary>
        private readonly ILogger<SqliteDataObjectRuntime> _logger;

        /// <summary>
        /// The DEPLOYMENT's own catalogue, bound from the <c>DataObjects</c> configuration section, or
        /// <see langword="null"/> when no such catalogue is registered.
        /// </summary>
        /// <remarks>
        /// OPTIONAL SO A TEST CAN CONSTRUCT THIS RUNTIME WITH THE REGISTRY ALONE, which several do, while
        /// the composition root supplies both. It is a SECOND source rather than a replacement: the
        /// registry beside it holds the evidenced fixture and every definition derived at run time, and
        /// neither population can be expressed as the other.
        /// </remarks>
        private readonly IDataObjectCatalog? _configured;

        /// <summary>Initializes the runtime.</summary>
        /// <param name="catalogue">The definition catalogue.</param>
        /// <param name="bindings">The store-to-transaction association.</param>
        /// <param name="logger">The logger retrieval faults are recorded through.</param>
        /// <param name="configured">
        /// The deployment's configured catalogue, or <see langword="null"/> when none is registered.
        /// </param>
        public SqliteDataObjectRuntime(
            DataObjectDefinitionCatalogue catalogue,
            DataWindowStoreBindings bindings,
            ILogger<SqliteDataObjectRuntime> logger,
            IDataObjectCatalog? configured = null)
        {
            _catalogue = catalogue ?? throw new ArgumentNullException(nameof(catalogue));
            _bindings = bindings ?? throw new ArgumentNullException(nameof(bindings));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _configured = configured;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// An unresolved name answers <see langword="false"/> and leaves the store exactly as
        /// PowerBuilder leaves a datastore whose data object failed to load: no statement, no
        /// expressions, and every describe answering the failure sentinel. That state is a REACHED
        /// behaviour rather than a gap - the retrieval task detects it through its
        /// <c>DataWindow.Units</c> probe [<c>n_cst_thread_task_sqlquery.sru:L554-L557</c>] - so the
        /// negative arm is preserved rather than eliminated by binding a real catalogue.
        /// </remarks>
        public bool TryResolveDefinition(
            string dataObject,
            [NotNullWhen(true)] out DataObjectDefinition? definition)
        {
            ArgumentNullException.ThrowIfNull(dataObject);

            // THE DEPLOYMENT'S OWN CATALOGUE IS CONSULTED FIRST, AND THE ORDER IS THE WHOLE DESIGN. The
            // registry below is seeded with the one EVIDENCED fixture and is where a definition derived
            // at run time lands; a configured definition is a deployment FACT, so it must be able to
            // override the built-in default. The two populations cannot otherwise collide: every
            // run-time registration is minted under the `pfw_derived_` prefix, which the options
            // validator's own naming rules mean a caller cannot have configured.
            if (_configured is not null && _configured.TryResolve(dataObject, out definition))
            {
                return true;
            }

            return _catalogue.TryGet(dataObject, out definition);
        }

        /// <inheritdoc/>
        /// <remarks>
        /// <para>
        /// THE RETURN VALUE IS THE DATASTORE ALPHABET AND NOT THE RETURN-CODE ALGEBRA: a row count, with
        /// a negative for failure, tested by the task as <c>nRowCnt &lt; 0</c>
        /// [<c>n_cst_thread_task_sqlquery.sru:L777</c>]. Zero is a genuine success meaning no row matched
        /// and must never be used to signal a fault.
        /// </para>
        /// <para>
        /// PARAMETERS ARE BOUND, NEVER INTERPOLATED. The legacy interpolates whenever the connection
        /// disabled bind variables, and binding here is the unobservable safety improvement the migration
        /// explicitly permits: the statement the caller composed is executed as composed, and only the
        /// argument values travel as parameters.
        /// </para>
        /// <para>
        /// THE THREE-PASS FILL IS FORCED BY HOW THE CARRIER CAPTURES ORIGINALS. A row's original is
        /// captured on the first write after a baseline, so values are written, then
        /// <see cref="DataWindowBufferStore.ResetUpdate"/> baselines them - which is the legacy's own
        /// <c>ds.ResetUpdate()</c> immediately after data lands - leaving every column reporting its
        /// current value as its original. That is precisely what a freshly retrieved row must look like,
        /// and it is what makes the <c>updatewhere=1</c> concurrency predicate meaningful afterwards.
        /// </para>
        /// </remarks>
        public async ValueTask<long> RetrieveAsync(
            ISqlDataStore data,
            IReadOnlyList<object?> parameters,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(data);
            ArgumentNullException.ThrowIfNull(parameters);

            // OBSERVED BEFORE ANY WORK AND AGAIN INSIDE THE READ LOOP. A retrieval is the longest-running
            // call on this surface, and a token checked only on entry would leave a cancelled request
            // reading rows until the result set ended. Cancellation is reported as the datastore failure
            // value rather than raised, because that is the alphabet this member's signature declares and
            // the ported arms read [n_cst_thread_task_sqlquery.sru:L777].
            if (cancellationToken.IsCancellationRequested)
            {
                return DataWindowBufferStore.DataStoreFailure;
            }

            string statement = data.GetSqlSelect();

            if (statement.Length == 0)
            {
                // No statement means no resolved definition, which is the unresolved-data-object state
                // rather than a provider fault. The datastore failure value is the honest answer: a zero
                // here would read as "retrieved successfully, no rows matched".
                return DataWindowBufferStore.DataStoreFailure;
            }

            IPooledTransaction? transaction = _bindings.GetTransaction(data);

            if (transaction is null)
            {
                _logger.LogError(
                    "A retrieval was attempted on a result store with no transaction attached, so it "
                        + "could not reach the storage engine.");

                return DataWindowBufferStore.DataStoreFailure;
            }

            // BOTH HALVES ARE ASKED: whether a SQLite engine is behind this transaction at all, and
            // whether that engine has an open connection to make a command on. A transaction that was
            // attached but never connected passes the first and fails the second, and it is the ordinary
            // shape of a caller that opened a session and reached C-05 before C-08 connected it - so it
            // has to answer in the datastore alphabet rather than escape as an exception through a
            // signature that declares a row count.
            if (!transaction.TryGetEngineCapability(out ISqliteCommandSource? commands)
                || !commands.CanCreateCommand)
            {
                // Reported as the datastore FAILURE value and never as zero rows. Zero would read as
                // "retrieved successfully, nothing matched", which is the most damaging answer available:
                // a caller would then apply an empty result over data it had.
                _logger.LogError(
                    "A retrieval was attempted through a transaction whose engine offers no usable "
                        + "command source, so it could not reach the storage engine.");

                return DataWindowBufferStore.DataStoreFailure;
            }

            string[] columns = _bindings.GetColumns(data);

            if (columns.Length == 0)
            {
                columns = DataObjectDefinitionCatalogue.EvidencedColumns;
            }

            // THE DECLARED TYPES, RESOLVED BESIDE THE NAMES, because a retrieved value must be coerced to
            // the type the DATAWINDOW declares rather than accepted as the type the PROVIDER chose. The two
            // disagree on the evidenced fixture - `id` and `age` are `type=number`, PowerBuilder's
            // double-precision type, against `INTEGER` and `INT` columns - and that disagreement is one of
            // the divergences the migration preserves (AAP 0.6.4). An absent or short declaration coerces
            // nothing, which leaves the provider's own type exactly as before.
            IReadOnlyList<DeclaredDataObjectColumn> declared =
                _catalogue.TryResolve(data.DataObject, out DataObjectDefinitionEntry? entry)
                    ? entry.Columns
                    : [];

            try
            {
                return await FillAsync(
                        data,
                        commands,
                        statement,
                        parameters,
                        columns,
                        declared,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (SqliteException failure)
            {
                // The fault is reported through the carrier's own database-error channel, which is where
                // the ported arms read it from, and the statement text is NOT included: it may carry
                // interpolated literal values and the channel that publishes it applies the service's
                // redaction policy of its own accord.
                _ = data.Carrier.OnDbError(
                    SqliteConnectionFactory.MapSqliteResultCode(
                        failure.SqliteErrorCode,
                        failure.SqliteExtendedErrorCode),
                    failure.Message,
                    string.Empty,
                    DwBuffer.Primary,
                    0L);

                _logger.LogError(failure, "A retrieval failed inside the storage engine.");

                return DataWindowBufferStore.DataStoreFailure;
            }
        }

        /// <summary>
        /// Runs the statement and fills the carrier from its result.
        /// </summary>
        /// <param name="data">The store whose carrier is filled.</param>
        /// <param name="commands">The command source the attached transaction exposed.</param>
        /// <param name="statement">The retrieval statement.</param>
        /// <param name="parameters">The matched retrieval arguments, in one-based legacy order.</param>
        /// <param name="columns">The column names, in one-based column order.</param>
        /// <returns>The retrieved row count, or the datastore failure value.</returns>
        private static async ValueTask<long> FillAsync(
            ISqlDataStore data,
            ISqliteCommandSource commands,
            string statement,
            IReadOnlyList<object?> parameters,
            string[] columns,
            IReadOnlyList<DeclaredDataObjectColumn> declared,
            CancellationToken cancellationToken)
        {
            // The retrieve-start hook fires BEFORE any row lands, which is the position the worker-side
            // carrier's own override depends on for its progress counter.
            if (Predicates.IsPrevented(data.Carrier.OnRetrieveStart()))
            {
                // A prevented retrieval is a veto rather than a fault, and the legacy's own convention
                // for a vetoed retrieval is that no row is delivered. Zero rows, not a failure.
                return 0L;
            }

            long rows = 0L;

            using (SqliteCommand command = CreateCommand(commands, statement, parameters))
            using (SqliteDataReader reader =
                (SqliteDataReader)await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            {
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    long row = data.Carrier.AppendRow(DwBuffer.Primary, ItemStatus.NotModified);

                    for (int column = 0; column < columns.Length && column < reader.FieldCount; column++)
                    {
                        // ONE-BASED COLUMN NUMBERS on the carrier against ZERO-BASED ordinals on the
                        // reader. Named rather than written inline because one-based to zero-based
                        // translation is the single most dangerous mechanical hazard in this port.
                        int columnNumber = column + 1;

                        _ = data.Carrier.SetItemValue(
                            row,
                            columnNumber,
                            DwBuffer.Primary,
                            ReadValue(
                                reader,
                                column,
                                column < declared.Count ? declared[column].DeclaredType : string.Empty));
                    }

                    rows++;

                    // The per-row hook fires after the row is complete, and its result is DISCARDED by
                    // the legacy - it is a progress notification rather than a veto.
                    _ = data.Carrier.OnRetrieveRow(row);
                }
            }

            // The baseline. Every column now reports its current value as its original, which is what a
            // freshly retrieved row looks like and what the concurrency predicate is measured against.
            _ = data.Carrier.ResetUpdate();

            return rows;
        }

        /// <summary>
        /// Builds a command over the transaction's connection with the arguments bound positionally.
        /// </summary>
        /// <param name="commands">The command source the attached transaction exposed.</param>
        /// <param name="statement">The statement text.</param>
        /// <param name="parameters">The matched retrieval arguments, in one-based legacy order.</param>
        /// <returns>The command, which the caller owns and disposes.</returns>
        /// <remarks>
        /// POSITIONAL BINDING, PRESERVED AS POSITIONAL. The legacy's retrieval arguments and its command
        /// path both use <c>?</c> placeholders resolved by POSITION, so the parameters are named
        /// <c>@p1</c>, <c>@p2</c> and so on in argument order and the placeholders are rewritten in the
        /// same order. Rewriting rather than relying on provider support for <c>?</c> keeps the binding
        /// explicit and makes an arity mismatch visible as a provider error naming the parameter.
        /// </remarks>
        /// <summary>
        /// Reads one column, coerced to the type the DataWindow declares rather than the type the provider
        /// chose.
        /// </summary>
        /// <param name="reader">The open reader.</param>
        /// <param name="ordinal">The ZERO-BASED provider ordinal.</param>
        /// <param name="declaredType">
        /// The DataWindow type token, or the empty string when the definition declares none - in which
        /// case the provider's own type is taken unchanged.
        /// </param>
        /// <returns>The value, or <see langword="null"/> for a database null.</returns>
        /// <remarks>
        /// <para>
        /// <b>THE DATAWINDOW'S TYPE WINS, AND THAT IS THE DIVERGENCE THE MIGRATION PRESERVES.</b> The
        /// evidenced fixture declares <c>id</c> and <c>age</c> as <c>type=number</c> - PowerBuilder's
        /// double-precision DataWindow type - over an <c>INTEGER</c> and an <c>INT</c> column, declares a
        /// two-place <c>decimal</c> over a <c>REAL</c>, and declares a <c>date</c> over <c>TEXT</c>
        /// [<c>dw_sqlite.srd:L8-L14</c> against <c>w_test_sqlite.srw:L463-L469</c>]. A retrieval that
        /// accepted the provider's types would answer a <see cref="long"/> where the oracle answers a
        /// <see cref="double"/>, which changes what the update contract compares and what a
        /// characterization recording holds (AAP 0.6.4).
        /// </para>
        /// <para>
        /// THE FAMILY IS THE PART BEFORE THE PARENTHESIS, because a token may carry a width -
        /// <c>char(100)</c>, <c>decimal(2)</c>. Compared case-insensitively and ORDINALLY: the <c>.srd</c>
        /// spells these lower case, and a culture-aware comparison would make the mapping locale
        /// dependent.
        /// </para>
        /// <para>
        /// A COERCION THE PROVIDER REFUSES FALLS BACK TO THE PROVIDER'S OWN VALUE rather than failing the
        /// retrieval. A declared type that cannot be produced from the stored value is a definition-versus-
        /// schema disagreement, and the oracle answers such a row rather than abandoning the retrieval.
        /// </para>
        /// </remarks>
        private static object? ReadValue(SqliteDataReader reader, int ordinal, string declaredType)
        {
            if (reader.IsDBNull(ordinal))
            {
                return null;
            }

            if (declaredType.Length == 0)
            {
                return reader.GetValue(ordinal);
            }

            int width = declaredType.IndexOf('(', StringComparison.Ordinal);
            string family = width < 0 ? declaredType : declaredType[..width];

            try
            {
                return family.ToLowerInvariant() switch
                {
                    "long" or "int" or "integer" or "ulong" or "uint" => reader.GetInt64(ordinal),
                    "decimal" or "dec" => reader.GetDecimal(ordinal),
                    "real" or "double" or "number" => reader.GetDouble(ordinal),
                    "date" => DateOnly.FromDateTime(reader.GetDateTime(ordinal)),
                    "datetime" => reader.GetDateTime(ordinal),
                    "time" => TimeOnly.FromDateTime(reader.GetDateTime(ordinal)),
                    "blob" => reader.GetFieldValue<byte[]>(ordinal),
                    _ => reader.GetString(ordinal),
                };
            }
            catch (Exception failure)
                when (failure is InvalidCastException or FormatException or OverflowException)
            {
                return reader.GetValue(ordinal);
            }
        }

        private static SqliteCommand CreateCommand(
            ISqliteCommandSource commands,
            string statement,
            IReadOnlyList<object?> parameters)
        {
            SqliteCommand command = commands.CreateCommand();

            if (parameters.Count == 0)
            {
                command.CommandText = statement;

                return command;
            }

            StringBuilder bound = new(statement.Length + (parameters.Count * 4));
            int next = 0;

            foreach (char character in statement)
            {
                if (character == '?' && next < parameters.Count)
                {
                    next++;
                    _ = bound.Append("@p").Append(next.ToString(CultureInfo.InvariantCulture));
                    continue;
                }

                _ = bound.Append(character);
            }

            command.CommandText = bound.ToString();

            for (int index = 0; index < parameters.Count; index++)
            {
                _ = command.Parameters.AddWithValue(
                    "@p" + (index + 1).ToString(CultureInfo.InvariantCulture),
                    parameters[index] ?? DBNull.Value);
            }

            return command;
        }
    }

    /// <summary>
    /// The provisioned <see cref="IQueryDataWindowRuntime"/>: it materialises a result carrier from a
    /// grid syntax, resolves a column's child result, and attaches a transaction to a store.
    /// </summary>
    internal sealed class SqliteQueryDataWindowRuntime : IQueryDataWindowRuntime
    {
        /// <summary>The diagnostic a syntax that declares no column reports.</summary>
        internal const string NoColumnsText =
            "The supplied DataWindow syntax declares no column, so no result carrier could be built "
            + "from it. A grid syntax must declare at least one column and a retrieval statement.";

        /// <summary>The diagnostic a syntax that declares no statement reports.</summary>
        internal const string NoSelectText =
            "The supplied DataWindow syntax declares no retrieval statement, so no result carrier could "
            + "be built from it. A grid syntax must carry its statement in a retrieve clause.";

        /// <summary>The prefix of the name a run-time definition is registered under.</summary>
        /// <remarks>
        /// A definition derived from a caller-supplied syntax needs a name, because the store resolves by
        /// name. The name is derived from the syntax itself so that the same syntax always yields the
        /// same definition - which is what keeps a retry idempotent - and it is prefixed so a reader of a
        /// log record can tell a derived definition from a catalogued one at a glance.
        /// </remarks>
        internal const string DerivedDataObjectPrefix = "pfw_derived_";

        /// <summary>The declared-type token a drop-down DataWindow column carries.</summary>
        /// <remarks>
        /// A PREFIX RATHER THAN AN EQUALITY, because the declaration names the child DataWindow after the
        /// token - <c>dddw_name</c> declares a drop-down whose child is the <c>name</c> DataWindow - so an
        /// equality test would match nothing a real definition writes.
        /// </remarks>
        internal const string DropDownDataWindowType = "dddw";

        /// <summary>The definition catalogue derived definitions are registered into.</summary>
        private readonly DataObjectDefinitionCatalogue _catalogue;

        /// <summary>The store-to-transaction association.</summary>
        private readonly DataWindowStoreBindings _bindings;

        /// <summary>Initializes the runtime.</summary>
        /// <param name="catalogue">The definition catalogue.</param>
        /// <param name="bindings">The store-to-transaction association.</param>
        public SqliteQueryDataWindowRuntime(
            DataObjectDefinitionCatalogue catalogue,
            DataWindowStoreBindings bindings)
        {
            _catalogue = catalogue ?? throw new ArgumentNullException(nameof(catalogue));
            _bindings = bindings ?? throw new ArgumentNullException(nameof(bindings));
        }

        /// <inheritdoc/>
        /// <remarks>
        /// The syntax is parsed, registered as a definition and then ASSIGNED to the store, in that
        /// order, because assignment is what makes the store read its statement, sort and filter back out
        /// - the legacy's <c>ds.DataObject = name</c> in one step rather than two. A malformed syntax
        /// yields the failure value paired with a diagnostic, which is exactly what
        /// <c>data.Create(sSQLSyntax, ref sError)</c> answers [<c>:L619</c>].
        /// </remarks>
        public CarrierCreateOutcome CreateFromSyntax(ISqlDataStore data, string syntax)
        {
            ArgumentNullException.ThrowIfNull(data);
            ArgumentNullException.ThrowIfNull(syntax);

            string[] columns = GridSyntax.ColumnsOf(syntax);

            if (columns.Length == 0)
            {
                return new CarrierCreateOutcome(DataWindowBufferStore.DataStoreFailure, NoColumnsText);
            }

            string select = GridSyntax.SelectOf(syntax);

            if (select.Length == 0)
            {
                return new CarrierCreateOutcome(DataWindowBufferStore.DataStoreFailure, NoSelectText);
            }

            string name = DerivedDataObjectPrefix + DerivedName(syntax);

            // THE DECLARED TYPES ARE PARSED OUT AND KEPT, not just the names. The names alone are enough
            // to bind a retrieval, which is why the binding table takes only names - but the DECLARED TYPE
            // is what tells a caller whether a column carries a drop-down child and what type a retrieved
            // value must be coerced to. A parse that fails here cannot fail the call: the two guards above
            // have already established that this text carries columns and a statement, so the declarations
            // are recorded when they are available and the definition still registers when they are not.
            IReadOnlyList<DeclaredDataObjectColumn>? declared =
                GridSyntax.TryParse(syntax, out DataObjectDefinitionEntry? parsed, out _)
                    ? parsed.Columns
                    : null;

            _catalogue.Register(
                new DataObjectDefinition(
                    DataObject: name,
                    SqlSelect: select,
                    Sort: string.Empty,
                    Filter: string.Empty,
                    Processing: DataObjectDefinitionCatalogue.EvidencedProcessing,
                    Arguments: DataObjectDefinitionCatalogue.NoArguments,
                    Units: DataObjectDefinitionCatalogue.ResolvedUnits),
                declared);

            _bindings.SetColumns(data, columns);

            data.DataObject = name;

            // Success is 1 on this channel and not the return-code algebra's zero. The legacy tests the
            // result against 1, so an implementation answering RetCode.OK would read as a failure.
            return new CarrierCreateOutcome(DataWindowBufferStore.DataStoreSuccess, string.Empty);
        }

        /// <inheritdoc/>
        /// <remarks>
        /// <para>
        /// <b>FALSE IS A NORMAL ANSWER HERE, NOT AN ERROR.</b> The legacy call answers <c>-1</c> for a
        /// column that has no drop-down child result, and the legacy responds to that with <c>continue</c>
        /// rather than an error [<c>n_cst_thread_task_sqlquery.sru:L120</c>] - the column is SKIPPED and
        /// the walk goes on. The one evidenced fixture declares no drop-down child on any of its six
        /// columns [<c>dw_sqlite.srd:L8-L14</c>], so against that fixture EVERY column answers false and a
        /// caller's walk completes normally.
        /// </para>
        /// <para>
        /// <b>THE DECLARED TYPE IS WHAT DECIDES, so a definition that does declare one resolves.</b>
        /// PowerBuilder spells a drop-down DataWindow column's edit style into the syntax, so the
        /// declaration is the only evidence available on this side of the boundary and testing it is what
        /// keeps the answer the definition's rather than this method's. Answering false unconditionally
        /// would be right for the evidenced fixture and silently wrong for any other, and the failure mode
        /// is the bad one: a child that exists would be skipped without a diagnostic.
        /// </para>
        /// <para>
        /// A child is created EMPTY, because it is the receive-side carrier the worker fills from the
        /// child payload it sends [<c>:L120-L135</c>]. It is not synthesised for a column that declares no
        /// child - inventing one there would hand a caller a carrier to read values out of and find empty,
        /// which is a silently wrong answer rather than a skipped column.
        /// </para>
        /// </remarks>
        public bool TryGetChild(ISqlDataStore data, string columnName, out DataWindowBufferStore? child)
        {
            ArgumentNullException.ThrowIfNull(data);
            ArgumentNullException.ThrowIfNull(columnName);

            child = null;

            if (columnName.Length == 0
                || !_catalogue.TryResolve(data.DataObject, out DataObjectDefinitionEntry? entry))
            {
                return false;
            }

            foreach (DeclaredDataObjectColumn column in entry.Columns)
            {
                if (!string.Equals(column.Name, columnName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!column.DeclaredType.StartsWith(DropDownDataWindowType, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                child = new DataWindowBufferStore();

                return true;
            }

            return false;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// THE POSITION OF THIS CALL IS CONTRACT. It sits AFTER the wide-crosstab no-user-prompt
        /// workaround [<c>:L672-L676</c>] and BEFORE the stored clause modifications [<c>:L684</c>], and
        /// <c>SqlQueryTask.ExecuteAsync</c> preserves that ordering. This member only records the
        /// attachment; it does not reorder anything.
        /// </remarks>
        public long AttachTransaction(ISqlDataStore data, IPooledTransaction transaction)
        {
            ArgumentNullException.ThrowIfNull(data);
            ArgumentNullException.ThrowIfNull(transaction);

            _bindings.AttachTransaction(data, transaction);

            // Success is 1 here too: the legacy tests `<> 1`, so any other value is failure and yields
            // RetCode.E_INVALID_TRANSACTION.
            return DataWindowBufferStore.DataStoreSuccess;
        }

        /// <summary>
        /// Derives a stable name from a syntax text.
        /// </summary>
        /// <param name="syntax">The syntax text.</param>
        /// <returns>The derived name fragment.</returns>
        /// <remarks>
        /// A NON-CRYPTOGRAPHIC ORDINAL HASH, AND THAT IS DELIBERATE RATHER THAN LAZY. The value is a
        /// cache key over text this service composed, never a credential, never an integrity check and
        /// never anything a caller is entitled to attach meaning to. Formatted as invariant hexadecimal
        /// so the same syntax yields the same name on every host and in every locale, which is what makes
        /// a retry idempotent and a characterization recording reproducible.
        /// </remarks>
        private static string DerivedName(string syntax) =>
            string.GetHashCode(syntax, StringComparison.Ordinal)
                .ToString("x8", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The provisioned <see cref="IQueryTransactionSurface"/>: it derives a grid syntax from a
    /// statement, runs the page-counting query, and raises the transaction object's two retrieval
    /// events.
    /// </summary>
    internal sealed class SqliteQueryTransactionSurface : IQueryTransactionSurface
    {
        /// <summary>The diagnostic a statement that no provider would prepare reports.</summary>
        internal const string SyntaxDerivationFailedText =
            "A grid DataWindow syntax could not be derived from the supplied statement, because the "
            + "storage engine would not prepare it. Correct the statement; its text is not reproduced "
            + "here because it can carry interpolated literal values.";

        /// <summary>The column name a count result reports.</summary>
        /// <remarks>
        /// The count wrapper the retrieval task builds aliases its projection, and the single consumer of
        /// this surface's query reads row 1 column 1 [<c>:L852</c>] rather than a name - so this value
        /// exists to give the carrier a well-formed one-column definition and nothing more.
        /// </remarks>
        internal const string CountColumnName = "count";

        /// <summary>The diagnostic a transaction with no command source reports.</summary>
        internal const string NoCommandSourceText =
            "The attached transaction's engine offers no command source, so this query could not be "
            + "executed against the storage engine.";

        /// <summary>Records query faults the outcome's own code would not explain.</summary>
        private readonly ILogger<SqliteQueryTransactionSurface> _logger;

        /// <summary>Initializes the surface.</summary>
        /// <param name="logger">The logger query faults are recorded through.</param>
        /// <remarks>
        /// NO CLOCK IS TAKEN, AND ITS ABSENCE IS DELIBERATE. Nothing here reads a clock: the count carrier
        /// this surface builds is a plain buffer store rather than the transfer-capable carrier, and every
        /// tick this service compares belongs to the pool. Taking a TimeProvider it never read would
        /// advertise a determinism seam that did not exist.
        /// </remarks>
        public SqliteQueryTransactionSurface(ILogger<SqliteQueryTransactionSurface> logger) =>
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        /// <inheritdoc/>
        /// <remarks>
        /// The column list is read from the provider's own result schema rather than parsed out of the
        /// statement, which is what makes this correct for a projection, a join, an alias or a wildcard
        /// alike. The statement is PREPARED and its schema read WITHOUT executing it, so deriving a
        /// syntax costs no rows and cannot change data.
        /// </remarks>
        public GridSyntaxOutcome GridSyntaxFromSql(IPooledTransaction transaction, string sql)
        {
            ArgumentNullException.ThrowIfNull(transaction);
            ArgumentNullException.ThrowIfNull(sql);

            // NO USABLE COMMAND SOURCE IS A DIFFERENT REFUSAL FROM A STATEMENT THE ENGINE WOULD NOT
            // PREPARE, and the two carry different texts on purpose: this one names the seam that could
            // not be crossed, while SyntaxDerivationFailedText below blames the statement. Reporting an
            // unconnected transaction as a bad statement would send a reader to correct text that was
            // never the problem. Neither echoes the engine's own not-connected sentence: the two sides
            // report the same condition from opposite ends, and conflating them would hide which
            // refused.
            if (!transaction.TryGetEngineCapability(out ISqliteCommandSource? commands)
                || !commands.CanCreateCommand)
            {
                return new GridSyntaxOutcome(string.Empty, NoCommandSourceText);
            }

            try
            {
                using SqliteCommand command = commands.CreateCommand();
                command.CommandText = sql;

                using SqliteDataReader reader = command.ExecuteReader(
                    System.Data.CommandBehavior.SchemaOnly);

                // A STATEMENT WITH NO RESULT COLUMN CANNOT YIELD A GRID DATAWINDOW, and the provider does
                // not object to being asked: preparing a DELETE or an UPDATE succeeds and answers a schema
                // of zero fields. Composing a syntax from that produces a table with a retrieve statement
                // and no column at all - a definition whose describe answers would then claim a carrier
                // shape that can hold nothing, which is worse than a refusal because it fails later and
                // somewhere else.
                if (reader.FieldCount == 0)
                {
                    return new GridSyntaxOutcome(string.Empty, SyntaxDerivationFailedText);
                }

                string[] columns = new string[reader.FieldCount];

                for (int ordinal = 0; ordinal < reader.FieldCount; ordinal++)
                {
                    columns[ordinal] = reader.GetName(ordinal);
                }

                return new GridSyntaxOutcome(GridSyntax.From(sql, columns), string.Empty);
            }
            catch (SqliteException failure)
            {
                _logger.LogError(
                    failure,
                    "A grid DataWindow syntax could not be derived because the storage engine refused "
                        + "to prepare the statement.");

                // Empty syntax paired with a non-empty diagnostic, which is how the outcome type spells
                // "no syntax, and here is why" - the consumer tests the text rather than the emptiness.
                return new GridSyntaxOutcome(string.Empty, SyntaxDerivationFailedText);
            }
        }

        /// <inheritdoc/>
        /// <remarks>
        /// <para>
        /// The shape handed back is a CARRIER rather than a scalar even though the only caller reads one
        /// value out of it, because that is what the legacy answers and because narrowing it to a scalar
        /// would make the <c>rtCode = 1</c> row-count test meaningless.
        /// </para>
        /// <para>
        /// SUCCESS ON THIS CHANNEL IS THE ROW COUNT, so a result carrying one row answers <c>1</c>. That
        /// is the legacy value verbatim and is deliberately NOT <see cref="RetCode.OK"/>.
        /// </para>
        /// </remarks>
        public ValueTask<CountQueryOutcome> Query(
            IPooledTransaction transaction,
            string sql,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(transaction);
            ArgumentNullException.ThrowIfNull(sql);

            // A cancelled call reports cancellation rather than an outcome it never waited for.
            cancellationToken.ThrowIfCancellationRequested();

            // E_INVALID_TRANSACTION AND NOT E_NO_IMPLEMENTATION, AND NOT THE BARE DATASTORE FAILURE. A
            // SQLite engine IS present here - it is simply not usable - so the unsupported-dialect answer
            // would be a lie about which engine backs the transaction, and the datastore failure value
            // says only "something went wrong" where a nameable condition exists. The consumer tests this
            // with the legacy failure predicate [SqlQueryTask, the count path], under which -7 and -1 are
            // both failures, so naming the cause costs the caller nothing and tells it what to fix.
            if (!transaction.TryGetEngineCapability(out ISqliteCommandSource? commands)
                || !commands.CanCreateCommand)
            {
                return ValueTask.FromResult(new CountQueryOutcome(
                    RetCode.E_INVALID_TRANSACTION,
                    null,
                    NoCommandSourceText));
            }

            try
            {
                using SqliteCommand command = commands.CreateCommand();
                command.CommandText = sql;

                using SqliteDataReader reader = command.ExecuteReader();

                DataWindowBufferStore result = new();
                long rows = 0L;

                while (reader.Read())
                {
                    long row = result.AppendRow(DwBuffer.Primary, ItemStatus.NotModified);

                    for (int ordinal = 0; ordinal < reader.FieldCount; ordinal++)
                    {
                        _ = result.SetItemValue(
                            row,
                            ordinal + 1,
                            DwBuffer.Primary,
                            reader.IsDBNull(ordinal) ? null : reader.GetValue(ordinal));
                    }

                    rows++;
                }

                _ = result.ResetUpdate();

                return ValueTask.FromResult(new CountQueryOutcome(rows, result, string.Empty));
            }
            catch (SqliteException failure)
            {
                _logger.LogError(failure, "A page-counting query failed inside the storage engine.");

                return ValueTask.FromResult(new CountQueryOutcome(
                    DataWindowBufferStore.DataStoreFailure,
                    null,
                    failure.Message));
            }
        }

        /// <inheritdoc/>
        /// <remarks>
        /// CONTINUE, AND THAT IS THE FAITHFUL ANSWER RATHER THAN A PLACEHOLDER. The legacy declares
        /// <c>onbeforeretrieve</c> with NO SCRIPT [<c>n_cst_thread_trans.sru:L11</c>], PowerBuilder
        /// answers zero for an unimplemented <c>long</c> event, and the caller tests the result with the
        /// PREVENTION predicate - so an unimplemented hook does not veto. A subclass of the transaction
        /// object is the legacy's extension point, and nothing in this service subclasses it, so
        /// preventing nothing is exactly what the oracle does.
        /// </remarks>
        public long RaiseBeforeRetrieve(IPooledTransaction transaction, DataWindowCarrier data)
        {
            ArgumentNullException.ThrowIfNull(transaction);
            ArgumentNullException.ThrowIfNull(data);

            return RetCode.OK;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// A notification with no return contract and no script in the oracle
        /// [<c>n_cst_thread_trans.sru:L12</c>], so genuinely nothing. Its POSITION is still contract: it
        /// fires before the defensive row-count override at <c>:L771-L774</c>, and the task preserves
        /// that.
        /// </remarks>
        public void RaiseAfterRetrieve(IPooledTransaction transaction, DataWindowCarrier data, long rowCount)
        {
            ArgumentNullException.ThrowIfNull(transaction);
            ArgumentNullException.ThrowIfNull(data);
        }
    }
}
