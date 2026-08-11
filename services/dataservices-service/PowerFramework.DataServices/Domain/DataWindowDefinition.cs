// ==================================================================================================
//  DataWindowDefinition.cs - THE HEADLESS DATAWINDOW DEFINITION MODEL
//  ------------------------------------------------------------------------------------------------
//  ROLE
//  The declarative half of a headless DataWindow: what objects it carries, what each object's type and
//  attributes are, and what its table specification says. `HeadlessDataWindowHost` is the behavioural
//  half and reads everything here through `Describe`.
//
//  WHY A DEFINITION TYPE RATHER THAN A PARSER
//  The legacy `.srd` files are the authoritative specification and are READ-ONLY (constraint C-C) -
//  they are the behavioural oracle, never a runtime input. `.dockerignore` excludes `ws_objects/` from
//  the build context, so a container that tried to read one would fail even where a local run happened
//  to succeed. Definitions are therefore TRANSCRIBED into `DataWindowCatalogue` as C# literals, each
//  carrying the `ws_objects/**` locator it came from, and this file is the shape they are transcribed
//  into. A `.srd` parser would be a second, unnecessary implementation of a format nothing else reads.
//
//  WHAT IS MODELLED AND WHAT IS NOT - THE HEADLESS / RENDERING SPLIT
//  AAP 0.3.5 splits every UI capability into a headless half that SHIPS IN DATASERVICES and a
//  rendering half that is DEFERRED to DesignSystem. This file is squarely on the headless side and the
//  boundary is visible in what the column record does and does not carry:
//    * CARRIED, because logic reads it: the column's name, its `ColType` (which the item-change
//      coercion switch dispatches on by its first five characters [se_cst_dw.sru:L228-L243]), its
//      update/key/identity/where-clause flags, its edit style and code table (the drop-down search and
//      context-menu models read both), its display format, and its tab sequence.
//    * NOT CARRIED, because only a renderer reads it: x, y, width, height, colour, font, border,
//      alignment, DPI units and every other geometry or appearance attribute. Not one appears below.
//  A reader checking whether the deferred boundary was respected can therefore check it by reading the
//  record definitions rather than by auditing call sites.
//
//  LEGACY REFERENCE (read only)
//      ws_objects/pfw.tests.pbl.src/dw_sqlite.srd
//          :L3   datawindow(... processing=1 ...)  the grid presentation style
//          :L8-L13   the six column declarations with their update and where-clause flags
//          :L14  retrieve / update / updatewhere=1 / updatekeyinplace=no / sort
//          :L15-L20  the six header text objects, declared BEFORE the columns
//          :L21-L26  the six column objects
//          :L27  the footer computed field, a PAGE-scoped aggregate
// ==================================================================================================

using PowerFramework.Shared.Kernel;

namespace PowerFramework.DataServices.Domain;

/// <summary>
/// What kind of DataWindow object a definition declares.
/// </summary>
/// <remarks>
/// COLUMNS ARE THE ONLY KIND THAT CONSUME A COLUMN NUMBER, which is why the kind has to be modelled
/// rather than inferred from the presence of a type string. A definition declaring six header texts
/// before its six columns [<c>dw_sqlite.srd:L15-L26</c>] still numbers its columns one to six, and a
/// host that numbered every object would answer every column-by-number lookup off by six.
/// </remarks>
public enum DataWindowObjectKind
{
    /// <summary>A data column. Consumes the next column number.</summary>
    Column = 0,

    /// <summary>A static text object. Consumes no column number.</summary>
    Text = 1,

    /// <summary>A computed field carrying an expression. Consumes no column number.</summary>
    Compute = 2,
}

/// <summary>
/// One entry of a drop-down column's code table: what the user sees and what is stored.
/// </summary>
/// <param name="Display">The displayed text.</param>
/// <param name="Data">The stored value.</param>
/// <remarks>
/// TWO FIELDS AND NOT ONE, because the display-to-data mapping is exactly what
/// <c>LookUpDisplay</c> resolves and what the drop-down search filters against. Collapsing them would
/// make a search over displayed text unable to produce the stored value the filter needs.
/// </remarks>
public sealed record DataWindowCodeTableEntry(string Display, string Data);

/// <summary>
/// One DataWindow object: a column, a text object or a computed field.
/// </summary>
/// <remarks>
/// <para>
/// A MUTABLE CLASS RATHER THAN A RECORD, because a definition is assembled attribute by attribute -
/// the transcription in <see cref="DataWindowCatalogue"/> reads as the <c>.srd</c> reads, one
/// attribute per line - and a positional record with fourteen parameters would make the transcription
/// unreadable and its correspondence to the source unverifiable.
/// </para>
/// <para>
/// EVERY STRING DEFAULTS TO EMPTY RATHER THAN NULL, matching what <c>Describe</c> answers for an
/// attribute an object carries but has not been given: PowerBuilder returns an empty string, which is
/// distinct from both the invalid-expression sentinel and the undetermined-value sentinel. Defaulting
/// to null would collapse a real answer into a missing one.
/// </para>
/// </remarks>
public sealed class DataWindowObjectDefinition
{
    /// <summary>Initializes a definition.</summary>
    /// <param name="name">The object's name, as <c>DataWindow.Objects</c> lists it.</param>
    /// <param name="kind">What kind of object this is.</param>
    /// <exception cref="ArgumentException"><paramref name="name"/> is blank.</exception>
    public DataWindowObjectDefinition(string name, DataWindowObjectKind kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Name = name;
        Kind = kind;
    }

    /// <summary>The object's name.</summary>
    public string Name { get; }

    /// <summary>What kind of object this is.</summary>
    public DataWindowObjectKind Kind { get; }

    /// <summary>
    /// The band the object sits in - <c>detail</c>, <c>header</c>, <c>footer</c> and so on.
    /// </summary>
    /// <remarks>
    /// A LOGICAL PLACEMENT, NOT A GEOMETRIC ONE. The band decides which objects a data operation
    /// touches and which a header probe finds; it says nothing about where anything is drawn, so it
    /// belongs on the headless side while every coordinate does not.
    /// </remarks>
    public string Band { get; set; } = "detail";

    /// <summary>
    /// The column's declared type, such as <c>number</c>, <c>char(100)</c>, <c>decimal(2)</c>,
    /// <c>date</c> or <c>datetime</c>.
    /// </summary>
    /// <remarks>
    /// THE FIRST FIVE CHARACTERS ARE LOAD BEARING AND THE FULL STRING MUST BE PRESERVED ANYWAY. The
    /// item-change protocol's default arm coerces by the first five characters of this string
    /// [<c>se_cst_dw.sru:L228-L243</c>], which is why <c>date</c> and <c>datetime</c> reach DIFFERENT
    /// arms - the first truncates to <c>date</c> and the second to <c>datet</c>. The parenthesised
    /// width or scale is preserved because callers describing a column expect the declared text back
    /// verbatim, including the width that disagrees with the database column it maps to.
    /// </remarks>
    public string ColType { get; set; } = string.Empty;

    /// <summary>The computed field's expression, or empty for any other kind.</summary>
    public string Expression { get; set; } = string.Empty;

    /// <summary>The display format.</summary>
    public string Format { get; set; } = string.Empty;

    /// <summary>The tab sequence, as a string because that is how it is described.</summary>
    public string TabSequence { get; set; } = string.Empty;

    /// <summary>The edit style - <c>edit</c>, <c>editmask</c>, <c>ddlb</c> or <c>dddw</c>.</summary>
    public string EditStyle { get; set; } = string.Empty;

    /// <summary>The edit mask, when the edit style is a mask.</summary>
    public string EditMaskMask { get; set; } = string.Empty;

    /// <summary>The drop-down DataWindow's name, when the edit style is a drop-down DataWindow.</summary>
    public string DropDownDataWindow { get; set; } = string.Empty;

    /// <summary>The drop-down's display column.</summary>
    public string DropDownDisplayColumn { get; set; } = string.Empty;

    /// <summary>The drop-down's data column.</summary>
    public string DropDownDataColumn { get; set; } = string.Empty;

    /// <summary>The database column this column maps to, in <c>table.column</c> form.</summary>
    public string DbName { get; set; } = string.Empty;

    /// <summary>Whether the column participates in generated updates.</summary>
    public bool Update { get; set; }

    /// <summary>
    /// Whether the column's ORIGINAL value joins the generated update predicate.
    /// </summary>
    /// <remarks>
    /// Under <c>updatewhere=1</c> every column carrying this flag contributes its original value to the
    /// predicate [<c>dw_sqlite.srd:L8-L14</c>], which is the whole optimistic-concurrency mechanism.
    /// </remarks>
    public bool UpdateWhereClause { get; set; }

    /// <summary>Whether the column is a key column.</summary>
    public bool Key { get; set; }

    /// <summary>Whether the column is the identity column.</summary>
    public bool Identity { get; set; }

    /// <summary>The code table, for a drop-down list box. Empty for every other kind.</summary>
    public IList<DataWindowCodeTableEntry> CodeTable { get; } = [];

    /// <summary>
    /// Answers one attribute of this object, or reports that it carries no such attribute.
    /// </summary>
    /// <param name="property">
    /// The attribute name, with the object name and its separating dot already removed.
    /// </param>
    /// <param name="value">The attribute's value on success; the empty string otherwise.</param>
    /// <returns><see langword="true"/> when this object recognises the attribute.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="property"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// UNRECOGNISED IS <see langword="false"/> RATHER THAN AN EMPTY STRING, and the caller turns that
    /// into the invalid-expression sentinel. The distinction is contract: an attribute an object
    /// carries but has not been given answers empty, and an attribute the object does not have at all
    /// answers <c>"!"</c>. A probe such as
    /// <c>Describe("DataWindow.Header.1.Height") &lt;&gt; "!"</c> [<c>n_cst_dwsvc.sru:L850</c>] tests
    /// exactly that difference, so collapsing the two would make it answer the wrong way.
    /// </para>
    /// <para>
    /// MATCHED CASE-INSENSITIVELY, because PowerBuilder property expressions are case insensitive and
    /// the ported call sites spell them inconsistently - <c>coltype</c> in one and <c>ColType</c> in
    /// another.
    /// </para>
    /// </remarks>
    public bool TryDescribe(string property, out string value)
    {
        ArgumentNullException.ThrowIfNull(property);

        value = property.ToLowerInvariant() switch
        {
            "name" => Name,
            "band" => Band,
            "coltype" => ColType,
            "type" => DescribeType(),
            "expression" => Expression,
            "format" => Format,
            "tabsequence" => TabSequence,
            "editstyle" or "edit.style" => EditStyle,
            "editmask.mask" => EditMaskMask,
            "dddw.name" => DropDownDataWindow,
            "dddw.displaycolumn" => DropDownDisplayColumn,
            "dddw.datacolumn" => DropDownDataColumn,
            "dbname" => DbName,
            "update" => DescribeFlag(Update),
            "updatewhereclause" => DescribeFlag(UpdateWhereClause),
            "key" => DescribeFlag(Key),
            "identity" => DescribeFlag(Identity),
            _ => string.Empty,
        };

        return property.ToLowerInvariant() is "name"
            or "band"
            or "coltype"
            or "type"
            or "expression"
            or "format"
            or "tabsequence"
            or "editstyle"
            or "edit.style"
            or "editmask.mask"
            or "dddw.name"
            or "dddw.displaycolumn"
            or "dddw.datacolumn"
            or "dbname"
            or "update"
            or "updatewhereclause"
            or "key"
            or "identity";
    }

    /// <summary>Renders the object kind as PowerBuilder's <c>Type</c> attribute spells it.</summary>
    /// <returns>The type name.</returns>
    private string DescribeType() => Kind switch
    {
        DataWindowObjectKind.Column => "column",
        DataWindowObjectKind.Compute => "compute",
        _ => "text",
    };

    /// <summary>Renders a boolean attribute as PowerBuilder describes one.</summary>
    /// <param name="flag">The flag.</param>
    /// <returns><c>"yes"</c> or <c>"no"</c>.</returns>
    /// <remarks>
    /// <c>"yes"</c> AND <c>"no"</c>, NOT <c>"1"</c> AND <c>"0"</c>. The update-prepare path compares
    /// described flags against those exact words, and <c>updatewhere</c> is the one table-level setting
    /// that genuinely describes as a NUMBER - which is why it lives on the table specification below
    /// as a string rather than passing through here.
    /// </remarks>
    private static string DescribeFlag(bool flag) => flag ? "yes" : "no";
}

/// <summary>
/// One headless DataWindow definition: its table specification and its objects, in declaration order.
/// </summary>
/// <remarks>
/// <para>
/// DECLARATION ORDER IS PRESERVED AND IS CONTRACT. <c>DataWindow.Objects</c> answers a
/// tab-separated list in declaration order, and column numbers are assigned in that same order across
/// the objects that are columns. A definition that reordered its objects would renumber its columns.
/// </para>
/// <para>
/// THE TABLE SPECIFICATION IS FOUR STRINGS AND NOT A PARSED MODEL, because every consumer describes
/// them and compares the text: the update-prepare path reads <c>UpdateKeyInPlace</c> and branches on
/// the word, and <c>UpdateWhere</c> is compared as the character <c>"1"</c>. Parsing them into
/// booleans and enums would force every consumer to render them back.
/// </para>
/// </remarks>
public sealed class DataWindowDefinition
{
    /// <summary>Initializes a definition.</summary>
    /// <param name="name">The DataWindow's name, as a handle resolves it.</param>
    /// <exception cref="ArgumentException"><paramref name="name"/> is blank.</exception>
    public DataWindowDefinition(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Name = name;
    }

    /// <summary>The DataWindow's name.</summary>
    public string Name { get; }

    /// <summary>
    /// The presentation-style code, as <c>DataWindow.Processing</c> answers it.
    /// </summary>
    /// <remarks>
    /// A LOGICAL DISCRIMINATOR DESPITE ITS NAME. <c>1</c> is the grid style, and the ported row-move
    /// path branches on it to decide whether a row can be moved without moving focus
    /// [<c>se_cst_dw.sru:L152-L158</c>]. It selects behaviour, not appearance, so it belongs here.
    /// </remarks>
    public string Processing { get; set; } = "0";

    /// <summary>Whether the DataWindow is read only, as <c>DataWindow.ReadOnly</c> answers it.</summary>
    public string ReadOnly { get; set; } = "no";

    /// <summary>The retrieval statement, as <c>DataWindow.Table.Select</c> answers it.</summary>
    public string RetrieveStatement { get; set; } = string.Empty;

    /// <summary>The update table, as <c>DataWindow.Table.UpdateTable</c> answers it.</summary>
    public string UpdateTable { get; set; } = string.Empty;

    /// <summary>
    /// The concurrency mode, as <c>DataWindow.Table.UpdateWhere</c> answers it.
    /// </summary>
    /// <remarks>
    /// <c>"1"</c> is key-plus-updatable-columns: the generated predicate carries the key column PLUS
    /// the ORIGINAL value of every column flagged <c>UpdateWhereClause</c> [<c>dw_sqlite.srd:L14</c>].
    /// </remarks>
    public string UpdateWhere { get; set; } = string.Empty;

    /// <summary>The key handling, as <c>DataWindow.Table.UpdateKeyInPlace</c> answers it.</summary>
    /// <remarks>
    /// <c>"no"</c> means a key change is a delete plus an insert rather than an in-place update, and it
    /// is the trigger for the legacy's own documented self-assignment workaround
    /// [<c>n_cst_thread_task_sqlupdate.sru:L151-L167</c>].
    /// </remarks>
    public string UpdateKeyInPlace { get; set; } = string.Empty;

    /// <summary>The definition-level sort, as <c>DataWindow.Table.Sort</c> answers it.</summary>
    /// <remarks>
    /// TRAILING WHITESPACE IS PART OF THE VALUE. The evidenced definition's sort is
    /// <c>"age A salary A "</c> with a trailing space [<c>dw_sqlite.srd:L14</c>], and trimming it would
    /// change what a caller describing this DataWindow reads back.
    /// </remarks>
    public string TableSort { get; set; } = string.Empty;

    /// <summary>Every object, in declaration order.</summary>
    public IList<DataWindowObjectDefinition> Objects { get; } = [];

    /// <summary>Adds a data column, which consumes the next column number.</summary>
    /// <param name="name">The column name.</param>
    /// <param name="colType">The declared column type.</param>
    /// <returns>The added definition, so its attributes can be set.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> is blank.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="colType"/> is <see langword="null"/>.</exception>
    public DataWindowObjectDefinition AddColumn(string name, string colType)
    {
        ArgumentNullException.ThrowIfNull(colType);

        DataWindowObjectDefinition column = new(name, DataWindowObjectKind.Column)
        {
            ColType = colType,
        };

        Objects.Add(column);

        return column;
    }

    /// <summary>Adds a static text object, which consumes no column number.</summary>
    /// <param name="name">The object name.</param>
    /// <param name="band">The band it sits in.</param>
    /// <returns>The added definition.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> is blank.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="band"/> is <see langword="null"/>.</exception>
    public DataWindowObjectDefinition AddText(string name, string band)
    {
        ArgumentNullException.ThrowIfNull(band);

        DataWindowObjectDefinition text = new(name, DataWindowObjectKind.Text) { Band = band };

        Objects.Add(text);

        return text;
    }

    /// <summary>Adds a computed field, which consumes no column number.</summary>
    /// <param name="name">The object name.</param>
    /// <param name="band">The band it sits in.</param>
    /// <param name="expression">The expression it computes.</param>
    /// <returns>The added definition.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> is blank.</exception>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="band"/> or <paramref name="expression"/> is <see langword="null"/>.
    /// </exception>
    public DataWindowObjectDefinition AddCompute(string name, string band, string expression)
    {
        ArgumentNullException.ThrowIfNull(band);
        ArgumentNullException.ThrowIfNull(expression);

        DataWindowObjectDefinition compute = new(name, DataWindowObjectKind.Compute)
        {
            Band = band,
            Expression = expression,
        };

        Objects.Add(compute);

        return compute;
    }

    /// <summary>The data columns, in declaration order, so index plus one is the column number.</summary>
    /// <returns>The columns.</returns>
    public IReadOnlyList<DataWindowObjectDefinition> Columns() =>
        [.. Objects.Where(static candidate => candidate.Kind == DataWindowObjectKind.Column)];

    /// <summary>
    /// Resolves a column's ONE-BASED number from its name.
    /// </summary>
    /// <param name="columnName">The column name, matched case-insensitively.</param>
    /// <returns>
    /// The one-based number, or <see cref="RetCode.E_OBJECT_NOT_FOUND"/> when no column carries that
    /// name - a column IS a DataWindow object, so that is the code for the miss.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="columnName"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// A NEGATIVE ANSWER RATHER THAN ZERO OR AN EXCEPTION, because the update-prepare path tests a
    /// resolved identifier for being non-positive and reports an internal error
    /// [<c>n_cst_thread_task_sqlupdate.sru:L118-L122</c>] - so the failure has to be a value a caller
    /// can test, and zero is a legitimate ordinal elsewhere (column index zero addresses the ROW).
    /// </remarks>
    public long NumberOf(string columnName)
    {
        ArgumentNullException.ThrowIfNull(columnName);

        IReadOnlyList<DataWindowObjectDefinition> columns = Columns();

        for (int index = 0; index < columns.Count; index++)
        {
            if (string.Equals(columns[index].Name, columnName, StringComparison.OrdinalIgnoreCase))
            {
                return index + 1;
            }
        }

        return RetCode.E_OBJECT_NOT_FOUND;
    }
}
