// ==================================================================================================
//  DataObjectCatalog - WHERE A DATA-OBJECT NAME BECOMES A DEFINITION
//  ------------------------------------------------------------------------------------------------
//  WHAT THIS FILE REPLACES, AND WHY THE REPLACEMENT IS CONFIGURATION RATHER THAN CODE
//
//  The legacy assigns a name and the PowerBuilder runtime loads the compiled DataWindow out of the
//  target's library list: `ds.DataObject = dataObject`
//  [ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru:L558]. That assignment is a
//  RESOLUTION against a library, and there is no managed equivalent - the `.srd` objects live in the
//  read-only legacy tree (AAP 0.2.2.1) and no .NET runtime can load one.
//
//  `IDataObjectRuntime`'s own documentation settles what to do about that: "resolution becomes an
//  injected collaborator", and "the published contract already anticipates this: QuerySpec carries
//  data_object, sql_syntax, sql, filter and sort side by side, so a caller may name a definition or
//  supply one outright". This file serves the NAMED case. The material it resolves from is a
//  deployment fact, so it arrives through the options pattern exactly as AAP 0.4.5.5 requires of every
//  value the legacy hardcoded.
//
//  WHAT THIS IS NOT
//  ------------------------------------------------------------------------------------------------
//  It is not a DataWindow engine and it implements no part of the deferred DesignSystem capability
//  (constraint C-D). A definition here is SIX STRINGS - statement, sort, filter, processing, arguments
//  and units - which are exactly the six values the task layer reads back through `Describe`. Nothing
//  here lays out a control, measures a font, converts a unit or paints anything, and nothing here
//  parses a `.srd`.
//
//  ORDINAL RESOLUTION, AND WHY
//  ------------------------------------------------------------------------------------------------
//  A data-object name arrives from a caller on the wire, so it is an opaque identifier and is matched
//  byte for byte. Folding case would let two distinct names collide on some hosts and not others, and
//  would make which definition a caller reached depend on how it happened to spell the name. The
//  options validator separately REFUSES two entries whose names differ only by case, so the strict
//  matching here cannot become a source of silent misses.
// ==================================================================================================

using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Options;
using PowerFramework.Persistence.Configuration;
using PowerFramework.Persistence.Tasks;

namespace PowerFramework.Persistence.Runtime;

/// <summary>
/// The source a data-object NAME is resolved against.
/// </summary>
/// <remarks>
/// Declared as its own seam rather than folded into <see cref="IDataObjectRuntime"/> because the two
/// answer different questions: this one is "what is the shape of the thing called <c>X</c>", which is
/// pure configuration lookup, and that one adds "now execute a retrieval against a storage engine".
/// Separating them is what lets the resolution half be exercised with no database at all.
/// </remarks>
internal interface IDataObjectCatalog
{
    /// <summary>
    /// The number of definitions this catalogue carries.
    /// </summary>
    /// <remarks>
    /// Published so the composition root's startup report and the readiness probe can both state a fact
    /// about the catalogue without enumerating it. A count is not a name, so nothing about a deployment's
    /// data model leaks through it.
    /// </remarks>
    int Count { get; }

    /// <summary>
    /// Resolves a name to its definition.
    /// </summary>
    /// <param name="dataObject">The name to resolve.</param>
    /// <param name="definition">
    /// Receives the definition, or <see langword="null"/> when the name is not carried.
    /// </param>
    /// <returns><see langword="true"/> when the name resolved.</returns>
    bool TryResolve(string dataObject, [NotNullWhen(true)] out DataObjectDefinition? definition);

    /// <summary>
    /// Resolves a name to its UPDATE contract - the column list and the three table-level settings the
    /// update path reads back through <c>Describe</c>.
    /// </summary>
    /// <param name="dataObject">The name to resolve.</param>
    /// <param name="contract">
    /// Receives the contract, or <see langword="null"/> when the name is not carried.
    /// </param>
    /// <returns><see langword="true"/> when the name resolved.</returns>
    /// <remarks>
    /// A SECOND MEMBER RATHER THAN A WIDER <see cref="DataObjectDefinition"/>, because that record is the
    /// retrieval side's contract - the six properties the query path describes - and it is constructed by
    /// name in several places. The update side needs a column list that the retrieval side has no use for,
    /// so the two are resolved separately from one frozen source. A resolved name always answers both, and
    /// a definition that declares no update table answers a contract whose table is empty rather than a
    /// negative, so a caller can tell "no such definition" from "that definition is retrieve-only".
    /// </remarks>
    bool TryResolveUpdateContract(
        string dataObject,
        [NotNullWhen(true)] out DataObjectUpdateContract? contract);
}

/// <summary>
/// One column of a resolved definition, frozen: its DataWindow name, its ONE-BASED ordinal, its database
/// name, and the four update attributes.
/// </summary>
/// <param name="Name">The DataWindow column name - what <c>&lt;name&gt;.Id</c> resolves.</param>
/// <param name="Number">
/// The ONE-BASED column ordinal. Position one in a declaration is column number one, matching every
/// legacy DataWindow ordinal and the carrier's own indexing (AAP 0.4.5.4 R9).
/// </param>
/// <param name="DbName">The value <c>Describe("#n.DBName")</c> answers.</param>
/// <param name="Update">Whether the column appears in a generated INSERT or UPDATE.</param>
/// <param name="Key">Whether the column is part of the update key.</param>
/// <param name="Identity">Whether the database assigns the column's value.</param>
/// <param name="UpdateWhereClause">
/// Whether the column's ORIGINAL value may appear in a generated where clause.
/// </param>
internal sealed record DataObjectColumn(
    string Name,
    int Number,
    string DbName,
    bool Update,
    bool Key,
    bool Identity,
    bool UpdateWhereClause);

/// <summary>
/// A resolved definition's update contract: the table, the concurrency mode, the key-in-place setting and
/// the columns.
/// </summary>
/// <param name="UpdateTable">
/// The value <c>Describe("DataWindow.Table.UpdateTable")</c> answers. EMPTY MEANS RETRIEVE-ONLY.
/// </param>
/// <param name="UpdateWhere">
/// The value <c>Describe("DataWindow.Table.UpdateWhere")</c> answers - <c>0</c> key columns only,
/// <c>1</c> key plus updateable, <c>2</c> key plus modified.
/// </param>
/// <param name="UpdateKeyInPlace">
/// Whether <c>Describe("DataWindow.Table.UpdateKeyinPlace")</c> answers <c>yes</c>.
/// </param>
/// <param name="Columns">The columns in DataWindow order, each carrying its own one-based ordinal.</param>
internal sealed record DataObjectUpdateContract(
    string UpdateTable,
    long UpdateWhere,
    bool UpdateKeyInPlace,
    IReadOnlyList<DataObjectColumn> Columns);

/// <summary>
/// The shipped catalogue: the definitions the <c>DataObjects</c> configuration section declares.
/// </summary>
/// <remarks>
/// <para>
/// FROZEN AT CONSTRUCTION, WHICH MAKES A LOOKUP ALLOCATION-FREE AND A DEFINITION IMMUTABLE. The
/// definitions are read once out of the bound options and projected into a frozen dictionary, so a later
/// mutation of the options instance cannot change what a retrieval already in flight resolves. The
/// projected records are themselves immutable, so handing the same instance to two concurrent callers is
/// safe without copying.
/// </para>
/// <para>
/// EVERY VALUE IS TRIMMED ON THE WAY IN, and that is not cosmetic: a settings file that acquired
/// trailing whitespace around a statement would otherwise produce a statement the engine rejects, and a
/// name with surrounding space would be a name no caller could ever send. The one value NOT trimmed is
/// the sort expression's shape, because the evidenced fixture's sort is
/// <c>"age A salary A "</c> - trailing space included
/// [<c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L14</c>] - and normalising it would be a behavioural
/// change to a value the oracle publishes verbatim.
/// </para>
/// <para>
/// AN EMPTY CATALOGUE IS A LEGAL DEPLOYMENT, not a broken one. A caller that supplies its own statement
/// through <c>QuerySpec.sql</c> never resolves a name, so a deployment serving only such callers declares
/// no definition; every name then answers the documented negative. The composition root reports the count
/// at startup so the state is visible rather than inferred.
/// </para>
/// </remarks>
internal sealed class ConfiguredDataObjectCatalog : IDataObjectCatalog
{
    /// <summary>The definitions, keyed by name.</summary>
    private readonly FrozenDictionary<string, DataObjectDefinition> _definitions;

    /// <summary>The update contracts, keyed by the same name under the same comparer.</summary>
    private readonly FrozenDictionary<string, DataObjectUpdateContract> _updateContracts;

    /// <summary>
    /// Builds the catalogue from the bound settings.
    /// </summary>
    /// <param name="options">This service's bound configuration.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// A duplicate name cannot arrive here - the options validator refuses the section before the host
    /// starts - but the projection still uses an indexer rather than <c>Add</c>, because a constructor
    /// that throws on a value the validator already guarantees would be dead code that a reader has to
    /// reason about.
    /// </remarks>
    public ConfiguredDataObjectCatalog(IOptions<PersistenceOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        Dictionary<string, DataObjectDefinition> definitions = new(StringComparer.Ordinal);
        Dictionary<string, DataObjectUpdateContract> updateContracts = new(StringComparer.Ordinal);

        foreach (DataObjectOptions declared in options.Value.DataObjects)
        {
            if (declared is null)
            {
                continue;
            }

            string name = (declared.Name ?? string.Empty).Trim();

            if (name.Length == 0)
            {
                continue;
            }

            updateContracts[name] = ProjectUpdateContract(declared);

            definitions[name] = new DataObjectDefinition(
                DataObject: name,
                SqlSelect: (declared.SqlSelect ?? string.Empty).Trim(),
                Sort: declared.Sort ?? string.Empty,
                Filter: declared.Filter ?? string.Empty,
                Processing: (declared.Processing ?? string.Empty).Trim(),
                Arguments: declared.Arguments ?? string.Empty,
                Units: (declared.Units ?? string.Empty).Trim());
        }

        _definitions = definitions.ToFrozenDictionary(StringComparer.Ordinal);
        _updateContracts = updateContracts.ToFrozenDictionary(StringComparer.Ordinal);
    }

    /// <inheritdoc/>
    public int Count => _definitions.Count;

    /// <inheritdoc/>
    public bool TryResolve(
        string dataObject,
        [NotNullWhen(true)] out DataObjectDefinition? definition)
    {
        ArgumentNullException.ThrowIfNull(dataObject);

        return _definitions.TryGetValue(dataObject, out definition);
    }

    /// <inheritdoc/>
    public bool TryResolveUpdateContract(
        string dataObject,
        [NotNullWhen(true)] out DataObjectUpdateContract? contract)
    {
        ArgumentNullException.ThrowIfNull(dataObject);

        return _updateContracts.TryGetValue(dataObject, out contract);
    }

    /// <summary>
    /// Freezes one declaration's update half.
    /// </summary>
    /// <param name="declared">The bound declaration.</param>
    /// <returns>The frozen contract.</returns>
    /// <remarks>
    /// <para>
    /// THE ORDINAL IS THE DECLARATION POSITION PLUS ONE, and that single line is the whole one-based
    /// conversion for the update path. A null entry cannot appear here - the validator refuses the section
    /// - but it is skipped defensively rather than dereferenced, because skipping it would shift every
    /// later ordinal and the resulting statements would name the wrong columns while running successfully.
    /// So a null is counted as it is skipped: the ordinal comes from the loop index, not from the output
    /// list's length.
    /// </para>
    /// <para>
    /// AN EMPTY DATABASE NAME DEFAULTS TO THE DATAWINDOW NAME, which is the ordinary case and is what a
    /// definition that never restated it means.
    /// </para>
    /// </remarks>
    private static DataObjectUpdateContract ProjectUpdateContract(DataObjectOptions declared)
    {
        List<DataObjectColumn> columns = new(declared.Columns.Count);

        for (int index = 0; index < declared.Columns.Count; index++)
        {
            DataObjectColumnOptions? column = declared.Columns[index];

            if (column is null)
            {
                continue;
            }

            string columnName = (column.Name ?? string.Empty).Trim();

            if (columnName.Length == 0)
            {
                continue;
            }

            string dbName = (column.DbName ?? string.Empty).Trim();

            columns.Add(
                new DataObjectColumn(
                    Name: columnName,
                    Number: index + 1,
                    DbName: dbName.Length == 0 ? columnName : dbName,
                    Update: column.Update,
                    Key: column.Key,
                    Identity: column.Identity,
                    UpdateWhereClause: column.UpdateWhereClause));
        }

        return new DataObjectUpdateContract(
            UpdateTable: (declared.UpdateTable ?? string.Empty).Trim(),
            UpdateWhere: declared.UpdateWhere,
            UpdateKeyInPlace: declared.UpdateKeyInPlace,
            Columns: columns);
    }
}
