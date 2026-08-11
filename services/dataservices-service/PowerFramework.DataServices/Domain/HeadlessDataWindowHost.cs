// ==================================================================================================
//  HeadlessDataWindowHost.cs - THE PROVISIONED DataWindowServiceHost
//  ------------------------------------------------------------------------------------------------
//  ROLE
//  The headless half of a DataWindow: an in-memory, one-based row and column model plus the buffer,
//  status, selection, sort, filter and event-gate behaviour the ported service layer reads. It is what
//  `IDataWindowHostFactory` produces, and it is the object every headless model in this service is
//  initialised against.
//
//  WHY THIS EXISTS, STATED AGAINST THE CONSTRAINT IT COULD BE MISREAD AS BREAKING (C-D)
//  An earlier revision registered three `Unbound*` implementations that returned null, arguing that
//  binding a DataWindow needs the DesignSystem ancestry `se_cst_dw` inherits from `se_cst_datawindow`
//  [se_cst_dw.sru:L4, :L10] and that C-D forbids implementing a deferred service even partially. The
//  first clause is a real fact about the LEGACY inheritance graph; the conclusion does not follow, and
//  the AAP says so twice:
//    * AAP 0.2.1.3 Correction 3 resolves that very inheritance edge by instructing DataServices to
//      "define its own abstract host contract carrying only the members se_cst_dw actually consumes
//      from its parent, implement against that, and record se_cst_datawindow as REFERENCE-only". The
//      contract is `Domain/DataWindowServiceHost.cs`; this file is the "implement against that" half
//      that was missing.
//    * AAP 0.3.5 splits every UI capability into "a headless half that SHIPS IN DATASERVICES and a
//      rendering half that does not". A row and column model with buffers and item statuses is the
//      headless half by definition.
//  So this type is AAP-MANDATED DataServices work, not deferred DesignSystem work, and the boundary is
//  observable rather than asserted: see the next paragraph.
//
//  HOW A READER CHECKS THE DEFERRED BOUNDARY WAS RESPECTED
//  Nothing in this file computes, stores or answers a coordinate, a size, a colour, a font, a border, a
//  DPI conversion or a redraw. `SetRedraw` records the requested state and answers success because the
//  ported code calls it in pairs around a bulk operation and tests the result; it draws nothing,
//  because there is nothing here to draw with. The rendering half stays deferred behind Gateway's
//  reserved `/v1/design/**` route, exactly as AAP 0.4.4 specifies.
//
//  RETURN-VALUE CONVENTIONS - PowerBuilder's, NOT the return-code algebra's
//  These are the single easiest thing to get wrong in this file, so they are stated once here and
//  observed everywhere below:
//    * A DataWindow method answers 1 for SUCCESS and -1 for FAILURE. It does NOT answer RetCode.OK
//      (which is 0). A caller testing `<> 1` is testing for failure, and a port that returned 0 on
//      success would read as a failure at every such site.
//    * `Describe` answers three DISTINCT strings that must never be collapsed: "!" for an invalid
//      expression, "?" for a value that is undetermined across the rows asked about, and "" for an
//      attribute the object carries but has not been given.
//    * Row and column ordinals are ONE-BASED (hazard R9). Column index 0 addresses THE ROW rather than
//      a column, which is what makes `GetItemStatus(row, 0, buffer)` the row-status probe.
//    * A null value stays null. It is never coerced to zero or to an empty string, because the
//      item-change protocol's equality test has an explicit null-and-null arm [se_cst_dw.sru:L196-L207]
//      and the tri-state predicates depend on null being distinguishable.
//
//  LEGACY REFERENCE (read only - never edited, never parsed at run time: constraint C-C)
//      ws_objects/pfw.ui.controls.ext.pbl.src/se_cst_datawindow.*   the structural parent, REFERENCE
//      ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru
//          :L89-L96    the four pieces of cross-event state, which live on ValidationSession
//          :L152-L158  the focusless row move, gated on the grid presentation style
//          :L182-L253  the item-change micro-protocol, whose coercion switch reads ColType
//      ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc.sru
//          :L850       the header-band probe that tests Describe(...) <> "!"
//      ws_objects/pfw.tests.pbl.src/dw_sqlite.srd                   the transcribed definition
//
//  THREAD SAFETY
//  NOT thread safe, deliberately and by contract. One host serves one DataWindow handle, and the
//  published surface serialises work on a handle through its validation session. Locking here would
//  hide a caller that had violated that discipline rather than fix it, and it would serialise the
//  bidirectional event chain whose whole point is ordered delivery.
// ==================================================================================================

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using PowerFramework.Contracts.Common.V1;
using PowerFramework.Shared.Eventful;
using PowerFramework.Shared.Kernel;

namespace PowerFramework.DataServices.Domain;

/// <summary>
/// The provisioned headless <see cref="DataWindowServiceHost"/>: an in-memory row and column model with
/// the buffer, status, selection, sort, filter and event-gate behaviour the ported service layer reads.
/// </summary>
/// <remarks>
/// See this file's header for the return-value conventions, the AAP basis for shipping it, and how a
/// reader verifies that the deferred rendering boundary was respected.
/// </remarks>
public sealed class HeadlessDataWindowHost : DataWindowServiceHost
{
    /// <summary>The value a DataWindow method answers on success.</summary>
    /// <remarks>
    /// ONE, NOT <see cref="RetCode.OK"/>. See this file's header: a caller testing <c>&lt;&gt; 1</c> is
    /// testing for failure, so answering zero would read as a failure at every such site.
    /// </remarks>
    public const int Success = 1;

    /// <summary>The value a DataWindow method answers on failure.</summary>
    public const int Failure = -1;

    /// <summary>The first valid row ordinal, because rows are ONE-based.</summary>
    public const long FirstRow = 1L;

    /// <summary>The first valid column ordinal, because column 0 addresses the row itself.</summary>
    public const long FirstColumn = 1L;

    /// <summary>The column ordinal that addresses the ROW rather than a column.</summary>
    public const long RowStatusColumn = 0L;

    /// <summary>What <c>Describe</c> answers for an expression it cannot evaluate at all.</summary>
    /// <remarks>
    /// DISTINCT FROM <see cref="UndeterminedValueSentinel"/> AND FROM THE EMPTY STRING, and the three
    /// are never interchangeable. <c>n_cst_dwsvc.sru:L850</c> probes a header band by testing this exact
    /// value, so answering empty for an unknown object would make that probe report a band that does
    /// not exist.
    /// </remarks>
    public const string InvalidExpressionSentinel = "!";

    /// <summary>What <c>Describe</c> answers when a value is undetermined across the rows asked about.</summary>
    public const string UndeterminedValueSentinel = "?";

    /// <summary>The separator <c>DataWindow.Objects</c> joins object names with.</summary>
    private const string ObjectListSeparator = "\t";

    /// <summary>The definition this host serves.</summary>
    private readonly DataWindowDefinition _definition;

    /// <summary>The broker the service layer reads off this host.</summary>
    private readonly EventBroker _eventful;

    /// <summary>The three buffers, each a list of rows in buffer order.</summary>
    private readonly Dictionary<DwBuffer, List<HeadlessRow>> _buffers = new()
    {
        [DwBuffer.Primary] = [],
        [DwBuffer.Delete] = [],
        [DwBuffer.Filter] = [],
    };

    /// <summary>The object model, one wrapper per definition object, built once.</summary>
    private readonly Dictionary<string, HeadlessDataWindowObject> _objects =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The selected primary-buffer rows, by row identity rather than by ordinal.</summary>
    /// <remarks>
    /// KEYED ON ROW IDENTITY, NOT ON ORDINAL, so a sort or an insert does not silently move the
    /// selection onto a different row. The legacy's own selection survives a sort, and keying on the
    /// ordinal would break that in a way only a sorted fixture would reveal.
    /// </remarks>
    private readonly HashSet<long> _selected = [];

    /// <summary>The next row identity to hand out.</summary>
    private long _nextRowId = FirstRow;

    /// <summary>The disabled-event bitmask.</summary>
    private uint _disabledEvents;

    /// <summary>The current row, or zero when there is none.</summary>
    private long _currentRow;

    /// <summary>The focused column's ordinal, or zero when there is none.</summary>
    private long _focusedColumn;

    /// <summary>The runtime sort expression, which starts as the definition's.</summary>
    private string _sort;

    /// <summary>The runtime filter expression.</summary>
    private string _filter = string.Empty;

    /// <summary>Whether redraw is enabled. Recorded, never acted on - see this file's header.</summary>
    private bool _redraw = true;

    /// <summary>Whether this host holds focus.</summary>
    private bool _focused;

    /// <summary>Initializes a host over one definition.</summary>
    /// <param name="definition">The definition to serve.</param>
    /// <param name="eventful">
    /// The broker the service layer reads off this host, or <see langword="null"/> for a fresh one.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="definition"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// THE BROKER IS ACCEPTED RATHER THAN ALWAYS CREATED, because the service base reads it off the host
    /// - <c>#Eventful = dw.Eventful</c> [<c>n_cst_dwsvc.sru:L86</c>] - and several models initialised
    /// against ONE host must share ONE broker or a subscription made through one model would be
    /// invisible to the topic another triggers.
    /// </remarks>
    public HeadlessDataWindowHost(DataWindowDefinition definition, EventBroker? eventful = null)
    {
        ArgumentNullException.ThrowIfNull(definition);

        _definition = definition;
        _eventful = eventful ?? new EventBroker();
        _sort = definition.TableSort;

        foreach (DataWindowObjectDefinition declared in definition.Objects)
        {
            _objects[declared.Name] = new HeadlessDataWindowObject(
                declared,
                NumberOfDeclared(definition, declared),
                this);
        }
    }

    /// <summary>The definition this host serves.</summary>
    public DataWindowDefinition Definition => _definition;

    /// <inheritdoc/>
    public override EventBroker Eventful => _eventful;

    /// <inheritdoc/>
    /// <remarks>
    /// THE HOST ITSELF, which is what the legacy's untyped object handle amounts to once the pointer is
    /// gone. Callers use it only to test identity and to pass the model back into another call, so
    /// answering a distinct wrapper would let two references to one DataWindow compare unequal.
    /// </remarks>
    public override object? ObjectModel => this;

    /// <inheritdoc/>
    /// <remarks>
    /// AN UNKNOWN NAME STILL ANSWERS AN OBJECT, and that is the legacy's shape rather than a shortcut.
    /// PowerBuilder hands back a reference whose <c>Describe</c> answers the invalid-expression
    /// sentinel for everything, which is how a ported probe tests for absence without a null check.
    /// Returning null here would turn every such probe into a null-reference fault.
    /// </remarks>
    public override IDataWindowObject GetObjectAttribute(string dwoName)
    {
        ArgumentNullException.ThrowIfNull(dwoName);

        return _objects.TryGetValue(dwoName, out HeadlessDataWindowObject? known)
            ? known
            : new HeadlessDataWindowObject(
                new DataWindowObjectDefinition(
                    dwoName.Length == 0 ? "unnamed" : dwoName,
                    DataWindowObjectKind.Text),
                columnNumber: 0L,
                host: this,
                recognised: false);
    }

    /// <inheritdoc/>
    public override string Describe(string property)
    {
        ArgumentNullException.ThrowIfNull(property);

        // The DataWindow-level keys, each one measured in a ported source. Matched case-insensitively
        // because PowerBuilder property expressions are, and the ported call sites spell them
        // inconsistently.
        switch (property.ToLowerInvariant())
        {
            case "datawindow.processing":
                return _definition.Processing;

            case "datawindow.readonly":
                return _definition.ReadOnly;

            case "datawindow.objects":
                return string.Join(
                    ObjectListSeparator,
                    _definition.Objects.Select(static declared => declared.Name));

            case "datawindow.column.count":
                return _definition.Columns().Count.ToString(CultureInfo.InvariantCulture);

            case "datawindow.table.select":
                return _definition.RetrieveStatement;

            case "datawindow.table.updatetable":
                return _definition.UpdateTable;

            case "datawindow.table.updatewhere":
                return _definition.UpdateWhere;

            case "datawindow.table.updatekeyinplace":
                return _definition.UpdateKeyInPlace;

            case "datawindow.table.sort":
                return _definition.TableSort;

            default:
                break;
        }

        // A per-object property expression: everything up to the FIRST dot names the object and the
        // remainder is the property, which is why a multi-part property such as `dddw.displaycolumn`
        // survives the split intact.
        int separator = property.IndexOf('.', StringComparison.Ordinal);

        if (separator <= 0 || separator >= property.Length - 1)
        {
            return InvalidExpressionSentinel;
        }

        if (_objects.TryGetValue(property[..separator], out HeadlessDataWindowObject? target)
            && target.Definition.TryDescribe(property[(separator + 1)..], out string value))
        {
            return value;
        }

        // Unknown object, or a property this object does not recognise. Every group-band probe lands
        // here too, which is exactly what makes `Describe("DataWindow.Header.1.Height") <> "!"`
        // [n_cst_dwsvc.sru:L850] answer false for a definition that declares no group bands.
        return InvalidExpressionSentinel;
    }

    /// <inheritdoc/>
    public override long RowCount() => _buffers[DwBuffer.Primary].Count;

    /// <inheritdoc/>
    public override long GetRow() => _currentRow;

    /// <inheritdoc/>
    public override int SetRow(long row)
    {
        if (!IsRowInRange(row, DwBuffer.Primary))
        {
            return Failure;
        }

        _currentRow = row;

        return Success;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// THE EMPTY STRING WHEN NOTHING IS FOCUSED, not a sentinel and not null. PowerBuilder answers an
    /// empty column name off a DataWindow with no focused column, and the ported sites test for
    /// emptiness rather than for a marker.
    /// </remarks>
    public override string GetColumnName()
    {
        IReadOnlyList<DataWindowObjectDefinition> columns = _definition.Columns();

        return _focusedColumn >= FirstColumn && _focusedColumn <= columns.Count
            ? columns[(int)(_focusedColumn - 1)].Name
            : string.Empty;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// ALWAYS SUCCEEDS ON A HEADLESS HOST, AND THAT IS CORRECT RATHER THAN CONVENIENT. Accepting text
    /// commits an EDIT CONTROL's pending text into the buffer, and a host with no edit control has no
    /// pending text to reject - so there is no validation for it to fail. The ported sites that test
    /// this result for <c>-1</c> [<c>se_cst_dw.sru:L387-L393</c>] are testing for a rejected edit, and
    /// manufacturing one here would invent a failure the legacy cannot produce in this configuration.
    /// </remarks>
    public override int AcceptText() => Success;

    /// <inheritdoc/>
    /// <remarks>
    /// RECORDED, NOT ACTED ON. See this file's header: there is nothing here to draw with, and the
    /// ported code calls this in pairs around a bulk operation and tests the result, so the state is
    /// retained and success is answered. Retaining it rather than ignoring it means a caller reading its
    /// own request back sees what it asked for.
    /// </remarks>
    public override int SetRedraw(bool enable)
    {
        _redraw = enable;

        return Success;
    }

    /// <summary>Whether redraw is currently enabled, as last requested.</summary>
    public bool IsRedrawEnabled => _redraw;

    /// <inheritdoc/>
    /// <remarks>
    /// ROW IDENTITY IS STABLE ACROSS SORTS AND INSERTS, which is the entire reason it exists: the ported
    /// selection and expression paths hold a row across an operation that renumbers ordinals, and an
    /// identity that moved with the ordinal would silently point at a different row afterwards.
    /// </remarks>
    public override long GetRowIDFromRow(long row) =>
        IsRowInRange(row, DwBuffer.Primary) ? _buffers[DwBuffer.Primary][(int)(row - 1)].Id : Failure;

    /// <inheritdoc/>
    public override long GetRowFromRowID(long rowId)
    {
        List<HeadlessRow> primary = _buffers[DwBuffer.Primary];

        for (int index = 0; index < primary.Count; index++)
        {
            if (primary[index].Id == rowId)
            {
                return index + 1;
            }
        }

        return Failure;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// THE EXPRESSION IS STORED AND NOT APPLIED, because PowerBuilder separates the two: <c>SetSort</c>
    /// installs the expression and <c>Sort</c> performs the reordering. Applying it here would make a
    /// caller that installs a sort and then decides not to apply it observe a reordered buffer.
    /// </remarks>
    public override int SetSort(string sort)
    {
        ArgumentNullException.ThrowIfNull(sort);

        _sort = sort;

        return Success;
    }

    /// <summary>The installed sort expression.</summary>
    public string InstalledSort => _sort;

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// SORTS THE PRIMARY BUFFER BY THE INSTALLED EXPRESSION, in the legacy's own
    /// <c>column A|D</c> grammar and left to right, with a null sorting FIRST on ascending. Selection
    /// survives because it is keyed on row identity rather than on ordinal, and the current row is
    /// re-derived from its identity for the same reason.
    /// </para>
    /// <para>
    /// AN UNPARSEABLE OR EMPTY EXPRESSION IS A NO-OP THAT REPORTS SUCCESS, matching PowerBuilder: a
    /// DataWindow with no sort installed answers success and leaves the order alone. Failing would make
    /// the common "clear the sort" call look like an error.
    /// </para>
    /// </remarks>
    public override int Sort()
    {
        (long Column, bool Ascending)[] terms = ParseSortTerms(_sort);

        if (terms.Length == 0)
        {
            return Success;
        }

        long currentId = _currentRow >= FirstRow ? GetRowIDFromRow(_currentRow) : 0L;

        List<HeadlessRow> primary = _buffers[DwBuffer.Primary];

        // A STABLE sort, so rows equal under every term keep their relative order - which is what
        // PowerBuilder does and what makes a two-key sort over a fixture reproducible.
        List<HeadlessRow> ordered = [.. primary
            .Select(static (row, index) => (Row: row, Index: index))
            .OrderBy(static pair => pair, new SortTermComparer(terms))
            .Select(static pair => pair.Row)];

        primary.Clear();
        primary.AddRange(ordered);

        if (currentId != 0L)
        {
            long moved = GetRowFromRowID(currentId);
            _currentRow = moved > 0L ? moved : _currentRow;
        }

        return Success;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// NO GROUP BANDS ARE DECLARED BY ANY DEFINITION IN THE ESTATE, so there are no group aggregates to
    /// recompute and success is the honest answer. This is not a stub: the operation is complete for
    /// every definition that exists, and a definition that declared group bands would carry them on
    /// <see cref="DataWindowDefinition.Objects"/> where this method would find them. Answering failure
    /// instead would make the ported paired <c>SetRedraw</c>/<c>GroupCalc</c> sequence report an error
    /// for a DataWindow that simply has no groups.
    /// </remarks>
    public override int GroupCalc() => Success;

    /// <inheritdoc/>
    public override bool IsEventDisabled(uint evt) => (_disabledEvents & evt) == evt && evt != 0U;

    /// <inheritdoc/>
    /// <remarks>
    /// ANSWERS THE MASK AS IT WAS BEFORE THE CHANGE, which is what lets a caller restore exactly what it
    /// found rather than guessing. The legacy's own disable returns the prior mask for that purpose.
    /// </remarks>
    public override long DisableEvent(uint evt)
    {
        uint previous = _disabledEvents;

        _disabledEvents |= evt;

        return previous;
    }

    /// <inheritdoc/>
    public override int EnableEvent(uint evt)
    {
        _disabledEvents &= ~evt;

        return Success;
    }

    /// <inheritdoc/>
    public override int SelectRow(long row, bool select)
    {
        // ROW ZERO IS THE DOCUMENTED WILDCARD, not an out-of-range ordinal: PowerBuilder selects or
        // deselects EVERY row for it, and the ported "clear the selection" call is exactly
        // `SelectRow(0, false)`. Treating it as out of range would break that call.
        if (row == 0L)
        {
            _selected.Clear();

            if (select)
            {
                foreach (HeadlessRow candidate in _buffers[DwBuffer.Primary])
                {
                    _ = _selected.Add(candidate.Id);
                }
            }

            return Success;
        }

        if (!IsRowInRange(row, DwBuffer.Primary))
        {
            return Failure;
        }

        long id = _buffers[DwBuffer.Primary][(int)(row - 1)].Id;

        _ = select ? _selected.Add(id) : _selected.Remove(id);

        return Success;
    }

    /// <inheritdoc/>
    public override bool IsSelected(long row) =>
        IsRowInRange(row, DwBuffer.Primary)
        && _selected.Contains(_buffers[DwBuffer.Primary][(int)(row - 1)].Id);

    /// <inheritdoc/>
    /// <remarks>
    /// THE SEARCH STARTS AFTER <paramref name="startRow"/>, NOT AT IT, which is what makes the ported
    /// <c>row = GetSelectedRow(row)</c> loop terminate instead of returning the same row forever. Zero
    /// means "no further selected row", which is the loop's exit condition.
    /// </remarks>
    public override long GetSelectedRow(long startRow)
    {
        List<HeadlessRow> primary = _buffers[DwBuffer.Primary];

        for (long row = Math.Max(startRow, 0L) + 1L; row <= primary.Count; row++)
        {
            if (_selected.Contains(primary[(int)(row - 1)].Id))
            {
                return row;
            }
        }

        return 0L;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// THE FOCUSED COLUMN'S OBJECT, OR NULL WHEN NOTHING IS FOCUSED. Null is a legitimate answer here
    /// rather than a defect: a DataWindow that has never been focused has no focused object, and the
    /// ported sites null-check this result precisely because of that.
    /// </remarks>
    public override object? GetFocusedObject()
    {
        IReadOnlyList<DataWindowObjectDefinition> columns = _definition.Columns();

        return _focusedColumn >= FirstColumn && _focusedColumn <= columns.Count
            ? GetObjectAttribute(columns[(int)(_focusedColumn - 1)].Name)
            : null;
    }

    /// <inheritdoc/>
    public override int SetFocus()
    {
        _focused = true;

        // Focus lands on the first column of the current row, or on the first row when there is none,
        // which is where PowerBuilder puts it on a DataWindow being focused for the first time.
        if (_currentRow < FirstRow && _buffers[DwBuffer.Primary].Count > 0)
        {
            _currentRow = FirstRow;
        }

        if (_focusedColumn < FirstColumn && _definition.Columns().Count > 0)
        {
            _focusedColumn = FirstColumn;
        }

        return Success;
    }

    /// <summary>Whether this host currently holds focus.</summary>
    public bool HasFocus => _focused;

    /// <summary>The focused column's ONE-BASED ordinal, or zero when none is focused.</summary>
    public long FocusedColumn => _focusedColumn;

    /// <summary>Moves the edit focus to one column of the current row.</summary>
    /// <param name="columnNumber">The ONE-BASED column ordinal, or zero to focus nothing.</param>
    /// <returns><see cref="Success"/>, or <see cref="Failure"/> for an out-of-range ordinal.</returns>
    /// <remarks>
    /// NOT PART OF THE HOST CONTRACT, because <c>se_cst_dw</c> never moves focus itself - the runtime
    /// does, and the chain observes the result. It exists so the published surface can reproduce a
    /// focus move a caller reports, and so a test can put the host into the state an item-change event
    /// arrives in.
    /// </remarks>
    public int SetColumnFocus(long columnNumber)
    {
        if (columnNumber == 0L)
        {
            _focusedColumn = 0L;

            return Success;
        }

        if (columnNumber < FirstColumn || columnNumber > _definition.Columns().Count)
        {
            return Failure;
        }

        _focusedColumn = columnNumber;

        return Success;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// COLUMN ZERO ADDRESSES THE ROW, which is what makes this the row-status probe as well as the
    /// column-status probe. An out-of-range address answers <see cref="ItemStatus.NotModified"/> rather
    /// than throwing, matching PowerBuilder, because the ported sites read a status for a row they are
    /// about to test for existence.
    /// </remarks>
    public override ItemStatus GetItemStatus(long row, long columnId, DwBuffer buffer)
    {
        if (!TryRow(row, buffer, out HeadlessRow? target))
        {
            return ItemStatus.NotModified;
        }

        return columnId == RowStatusColumn ? target.Status : target.StatusOf(columnId);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// SETTING THE ROW STATUS DOES NOT CASCADE TO ITS COLUMNS, AND SETTING A COLUMN'S DOES NOT PROMOTE
    /// THE ROW'S. That asymmetry is PowerBuilder's, and both halves are load bearing: the update walk
    /// enumerates rows by ROW status while the generated SET list is built from COLUMN statuses, so a
    /// cascade in either direction would change which columns a generated statement writes.
    /// </remarks>
    public override int SetItemStatus(long row, long columnId, DwBuffer buffer, ItemStatus status)
    {
        if (!TryRow(row, buffer, out HeadlessRow? target))
        {
            return Failure;
        }

        if (columnId == RowStatusColumn)
        {
            target.Status = status;

            return Success;
        }

        return target.SetStatusOf(columnId, status) ? Success : Failure;
    }

    /// <inheritdoc/>
    public override int SetItem(long row, long columnId, string? value) =>
        SetItem(row, columnId, (object?)value);

    /// <inheritdoc/>
    public override int SetItem(long row, long columnId, decimal? value) =>
        SetItem(row, columnId, (object?)value);

    /// <inheritdoc/>
    public override int SetItem(long row, long columnId, long? value) =>
        SetItem(row, columnId, (object?)value);

    /// <inheritdoc/>
    public override int SetItem(long row, long columnId, DateTime? value) =>
        SetItem(row, columnId, (object?)value);

    /// <inheritdoc/>
    public override int SetItem(long row, long columnId, DateOnly? value) =>
        SetItem(row, columnId, (object?)value);

    /// <inheritdoc/>
    public override int SetItem(long row, long columnId, TimeOnly? value) =>
        SetItem(row, columnId, (object?)value);

    /// <inheritdoc/>
    /// <remarks>
    /// THE ONE REAL IMPLEMENTATION, WHICH EVERY TYPED OVERLOAD FORWARDS TO. The typed overloads exist
    /// because PowerScript resolves <c>SetItem</c> by argument type and the ported call sites are typed;
    /// the STORED value is the boxed original in every case, because coercion belongs to the
    /// item-change protocol's own switch [<c>se_cst_dw.sru:L228-L243</c>] and coercing here would apply
    /// it twice.
    /// </remarks>
    public override int SetItem(long row, long columnId, object? value)
    {
        if (!TryRow(row, DwBuffer.Primary, out HeadlessRow? target))
        {
            return Failure;
        }

        if (columnId < FirstColumn || columnId > _definition.Columns().Count)
        {
            return Failure;
        }

        // Null is stored as null. See this file's header: it is never coerced to zero or to an empty
        // string, because the item-change equality test has an explicit null-and-null arm.
        target.SetValue(columnId, value);

        return Success;
    }

    /// <inheritdoc/>
    public override string? GetItemString(long row, string column) =>
        ReadItem(row, column) switch
        {
            null => null,
            string text => text,
            object other => Convert.ToString(other, CultureInfo.InvariantCulture),
        };

    /// <inheritdoc/>
    public override decimal? GetItemDecimal(long row, string column) =>
        ReadItem(row, column) is { } value && value is not string { Length: 0 }
            ? Convert.ToDecimal(value, CultureInfo.InvariantCulture)
            : null;

    /// <inheritdoc/>
    public override double? GetItemNumber(long row, string column) =>
        ReadItem(row, column) is { } value && value is not string { Length: 0 }
            ? Convert.ToDouble(value, CultureInfo.InvariantCulture)
            : null;

    /// <inheritdoc/>
    public override DateTime? GetItemDateTime(long row, string column) => ReadItem(row, column) switch
    {
        null => null,
        DateTime moment => moment,
        DateOnly day => day.ToDateTime(TimeOnly.MinValue),
        string text when DateTime.TryParse(
            text,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out DateTime parsed) => parsed,
        _ => null,
    };

    /// <inheritdoc/>
    public override DateOnly? GetItemDate(long row, string column) => ReadItem(row, column) switch
    {
        null => null,
        DateOnly day => day,
        DateTime moment => DateOnly.FromDateTime(moment),
        string text when DateOnly.TryParse(
            text,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out DateOnly parsed) => parsed,
        _ => null,
    };

    /// <inheritdoc/>
    public override TimeOnly? GetItemTime(long row, string column) => ReadItem(row, column) switch
    {
        null => null,
        TimeOnly time => time,
        DateTime moment => TimeOnly.FromDateTime(moment),
        string text when TimeOnly.TryParse(
            text,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out TimeOnly parsed) => parsed,
        _ => null,
    };

    /// <inheritdoc/>
    public override int SetItem(long row, string column, string? value) =>
        SetItem(row, _definition.NumberOf(column), (object?)value);

    /// <inheritdoc/>
    public override int SetItem(long row, string column, decimal? value) =>
        SetItem(row, _definition.NumberOf(column), (object?)value);

    /// <inheritdoc/>
    public override int SetItem(long row, string column, long? value) =>
        SetItem(row, _definition.NumberOf(column), (object?)value);

    /// <inheritdoc/>
    public override int SetItem(long row, string column, DateTime? value) =>
        SetItem(row, _definition.NumberOf(column), (object?)value);

    /// <inheritdoc/>
    public override int SetItem(long row, string column, DateOnly? value) =>
        SetItem(row, _definition.NumberOf(column), (object?)value);

    /// <inheritdoc/>
    public override int SetItem(long row, string column, TimeOnly? value) =>
        SetItem(row, _definition.NumberOf(column), (object?)value);

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// SUPPORTS THE EQUALITY GRAMMAR THE PORTED CALL SITES USE - <c>column = literal</c>, with
    /// <c>and</c> joining terms and a quoted or bare literal - and answers zero for anything else.
    /// ZERO IS THE LEGACY'S "NOT FOUND", and a malformed expression answering not-found rather than
    /// failing is what PowerBuilder does for an expression it cannot evaluate over the range.
    /// </para>
    /// <para>
    /// The general expression language lives in <c>Expressions/DataWindowExpressionEvaluator.cs</c>, and
    /// it is NOT reached from here: this method is the row-locating primitive the evaluator itself is
    /// built above, so calling into the evaluator would be a cycle.
    /// </para>
    /// </remarks>
    public override long Find(string expression, long start, long end)
    {
        ArgumentNullException.ThrowIfNull(expression);

        (long Column, string Value)[] terms = ParseFindTerms(expression);

        if (terms.Length == 0)
        {
            return 0L;
        }

        List<HeadlessRow> primary = _buffers[DwBuffer.Primary];

        long first = Math.Max(start, FirstRow);
        long last = Math.Min(end, primary.Count);

        for (long row = first; row <= last; row++)
        {
            HeadlessRow candidate = primary[(int)(row - 1)];

            if (terms.All(term => string.Equals(
                Convert.ToString(candidate.ValueOf(term.Column), CultureInfo.InvariantCulture)
                    ?? string.Empty,
                term.Value,
                StringComparison.Ordinal)))
            {
                return row;
            }
        }

        return 0L;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// INSERTS BEFORE <paramref name="row"/>, AND ZERO APPENDS. That is PowerBuilder's contract and the
    /// ported sites depend on the zero case; the inserted row's status is
    /// <see cref="ItemStatus.NewModified"/>, which is what makes it generate an INSERT rather than an
    /// UPDATE on the next update.
    /// </remarks>
    public override long InsertRow(long row)
    {
        List<HeadlessRow> primary = _buffers[DwBuffer.Primary];

        if (row < 0L || row > primary.Count + 1L)
        {
            return Failure;
        }

        HeadlessRow inserted = new(_nextRowId++, _definition.Columns().Count)
        {
            Status = ItemStatus.NewModified,
        };

        if (row == 0L)
        {
            primary.Add(inserted);

            return primary.Count;
        }

        primary.Insert((int)(row - 1), inserted);

        return row;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// THE DISPLAY VALUE FOR A CODE-TABLE ENTRY, ONE-BASED. This is the code-table read the drop-down
    /// paths use, and it is deliberately NOT the item value: <c>GetValue</c> walks the column's declared
    /// code table rather than its data.
    /// </para>
    /// <para>
    /// AN EMPTY STRING FOR AN UNKNOWN COLUMN OR AN INDEX PAST THE END, matching PowerBuilder, because
    /// the ported sites walk the table until they read an empty string - so a sentinel or an exception
    /// would turn a normal loop termination into a fault.
    /// </para>
    /// </remarks>
    public override string GetValue(string column, long index)
    {
        ArgumentNullException.ThrowIfNull(column);

        if (!_objects.TryGetValue(column, out HeadlessDataWindowObject? target))
        {
            return string.Empty;
        }

        IList<DataWindowCodeTableEntry> table = target.Definition.CodeTable;

        return index >= FirstRow && index <= table.Count
            ? table[(int)(index - 1)].Data
            : string.Empty;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// A DROP-DOWN DATAWINDOW'S CHILD IS NOT MATERIALISED HERE, AND THE REFUSAL IS THE CONTRACT RATHER
    /// THAN A GAP. A child DataWindow is a SECOND DataWindow with its own definition, its own retrieval
    /// and its own transaction; the legacy resolves it out of the containing control's object tree,
    /// which a headless host does not have. The ported call sites all test this result and take a
    /// documented not-a-child branch [<c>n_cst_dwsvc.sru</c>], so answering failure drives them down the
    /// arm the legacy drives them down for a column that has no child.
    /// </para>
    /// <para>
    /// The reference is set to <see langword="null"/> on failure and never left as the caller passed it,
    /// so a caller that ignored the return value cannot read a stale child.
    /// </para>
    /// </remarks>
    public override int GetChild(string column, ref IDataWindowChild? child)
    {
        ArgumentNullException.ThrowIfNull(column);

        child = null;

        return Failure;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// THE FILTER EXPRESSION IS STORED AND NOT APPLIED, exactly as <see cref="SetSort"/> stores without
    /// sorting: PowerBuilder's <c>SetFilter</c> installs and <c>Filter</c> applies. The base class's
    /// <c>Filter</c> is inherited unchanged, which keeps the filtering semantics in the one place that
    /// already documents them.
    /// </remarks>
    public int SetFilter(string filter)
    {
        ArgumentNullException.ThrowIfNull(filter);

        _filter = filter;

        return Success;
    }

    /// <summary>The installed filter expression.</summary>
    public string InstalledFilter => _filter;

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// MOVES EVERY ROW THE INSTALLED FILTER EXCLUDES OUT OF THE PRIMARY BUFFER AND INTO THE FILTER
    /// BUFFER, which is what PowerBuilder's filter does: it does not hide rows, it relocates them, and
    /// the Filter buffer is a real buffer an update walks. Rows already in the Filter buffer are
    /// re-tested and return when the filter no longer excludes them.
    /// </para>
    /// <para>
    /// THE FILTER BUFFER'S ROW ORDER IS INVERTED RELATIVE TO THE SOURCE, and that is reproduced rather
    /// than tidied: the legacy documents it and iterates that buffer BACKWARDS because of it
    /// [<c>n_cst_thread_task_sqlupdate.sru:L235-L237</c>]. A newly excluded row is therefore inserted at
    /// the FRONT. Ordering it the obvious way would produce wrong identity values that a row-count
    /// assertion would not catch (hazard R9).
    /// </para>
    /// <para>
    /// AN EMPTY FILTER RESTORES EVERY FILTERED ROW AND REPORTS SUCCESS. That is the documented way to
    /// clear a filter, so failing would make the clear look like an error.
    /// </para>
    /// </remarks>
    protected override int FilterCore()
    {
        (long Column, string Value)[] terms = ParseFindTerms(_filter);

        List<HeadlessRow> primary = _buffers[DwBuffer.Primary];
        List<HeadlessRow> filtered = _buffers[DwBuffer.Filter];

        bool excludesNothing = _filter.Length == 0 || terms.Length == 0;

        // Returning first, so a row that the new filter admits is back in the primary buffer before the
        // exclusion pass runs and cannot be tested twice in one call.
        for (int index = filtered.Count - 1; index >= 0; index--)
        {
            if (excludesNothing || Admits(filtered[index], terms))
            {
                primary.Add(filtered[index]);
                filtered.RemoveAt(index);
            }
        }

        if (!excludesNothing)
        {
            for (int index = primary.Count - 1; index >= 0; index--)
            {
                if (!Admits(primary[index], terms))
                {
                    // INSERTED AT THE FRONT. See the remarks: the Filter buffer's order is inverted.
                    filtered.Insert(0, primary[index]);
                    primary.RemoveAt(index);
                }
            }
        }

        if (_currentRow > primary.Count)
        {
            _currentRow = primary.Count;
        }

        return Success;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// MOVES THE ROW INTO THE DELETE BUFFER RATHER THAN DISCARDING IT, because that buffer is what the
    /// update walk reads to generate DELETE statements. Discarding the row would make the deletion
    /// invisible to the next update and the row would silently survive in storage.
    /// </para>
    /// <para>
    /// A ROW THAT WAS NEVER IN STORAGE IS DISCARDED INSTEAD. A <see cref="ItemStatus.NewModified"/> row
    /// has no database row to delete, so moving it into the Delete buffer would generate a DELETE for a
    /// row that does not exist - which is what PowerBuilder avoids by dropping it outright.
    /// </para>
    /// </remarks>
    protected override int DeleteRowCore(long row)
    {
        if (!TryRow(row, DwBuffer.Primary, out HeadlessRow? target))
        {
            return Failure;
        }

        _buffers[DwBuffer.Primary].RemoveAt((int)(row - 1));
        _ = _selected.Remove(target.Id);

        if (target.Status != ItemStatus.NewModified)
        {
            _buffers[DwBuffer.Delete].Add(target);
        }

        if (_currentRow > _buffers[DwBuffer.Primary].Count)
        {
            _currentRow = _buffers[DwBuffer.Primary].Count;
        }

        return Success;
    }

    /// <summary>Whether one row satisfies every term of a parsed filter.</summary>
    /// <param name="candidate">The row.</param>
    /// <param name="terms">The parsed terms.</param>
    /// <returns><see langword="true"/> when the row is admitted.</returns>
    private static bool Admits(HeadlessRow candidate, (long Column, string Value)[] terms) =>
        terms.All(term => string.Equals(
            Convert.ToString(candidate.ValueOf(term.Column), CultureInfo.InvariantCulture)
                ?? string.Empty,
            term.Value,
            StringComparison.Ordinal));

    /// <summary>Appends a row to a buffer with an explicit status, for retrieval and for fixtures.</summary>
    /// <param name="buffer">The buffer to append to.</param>
    /// <param name="status">The row's status.</param>
    /// <param name="values">The column values, in column order.</param>
    /// <returns>The appended row's ONE-BASED ordinal.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="values"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// NOT PART OF THE HOST CONTRACT, because <c>se_cst_dw</c> never populates a DataWindow - retrieval
    /// does, on the Persistence side, and the result arrives as a changeset. This is how a retrieval
    /// result or a transcribed fixture lands in the model, and it is the only way rows enter a buffer
    /// other than <see cref="InsertRow"/>.
    /// </remarks>
    public long AppendRow(DwBuffer buffer, ItemStatus status, params object?[] values)
    {
        ArgumentNullException.ThrowIfNull(values);

        List<HeadlessRow> target = _buffers[buffer];

        HeadlessRow appended = new(_nextRowId++, _definition.Columns().Count) { Status = status };

        for (int index = 0; index < values.Length; index++)
        {
            appended.SetValue(index + 1, values[index]);
        }

        // The ORIGINAL values are baselined here, which is what makes an update's predicate carry
        // originals rather than current values. A row appended as NewModified has no original state to
        // baseline - it does not exist in storage - so only a settled row is baselined.
        if (status == ItemStatus.NotModified)
        {
            appended.Baseline();
        }

        target.Add(appended);

        if (buffer == DwBuffer.Primary && _currentRow < FirstRow)
        {
            _currentRow = FirstRow;
        }

        return target.Count;
    }

    /// <summary>Reads one item's ORIGINAL value, which is what an update predicate is built from.</summary>
    /// <param name="row">The ONE-BASED row ordinal.</param>
    /// <param name="columnNumber">The ONE-BASED column ordinal.</param>
    /// <param name="buffer">The buffer to read from.</param>
    /// <returns>The original value, or <see langword="null"/> when there is none.</returns>
    public object? GetItemOriginalValue(long row, long columnNumber, DwBuffer buffer) =>
        TryRow(row, buffer, out HeadlessRow? target) ? target.OriginalOf(columnNumber) : null;

    /// <summary>The number of rows in one buffer.</summary>
    /// <param name="buffer">The buffer.</param>
    /// <returns>The count.</returns>
    public long RowCountOf(DwBuffer buffer) => _buffers[buffer].Count;

    /// <summary>Resolves the declared ONE-BASED column number of one object, or zero for a non-column.</summary>
    /// <param name="definition">The whole definition.</param>
    /// <param name="declared">The object to number.</param>
    /// <returns>The one-based column number, or zero.</returns>
    private static long NumberOfDeclared(
        DataWindowDefinition definition,
        DataWindowObjectDefinition declared) =>
        declared.Kind == DataWindowObjectKind.Column ? definition.NumberOf(declared.Name) : 0L;

    /// <summary>Parses the legacy <c>column A|D</c> sort grammar into column ordinals and directions.</summary>
    /// <param name="sort">The sort expression.</param>
    /// <returns>The terms, left to right. Empty when nothing parses.</returns>
    private (long Column, bool Ascending)[] ParseSortTerms(string sort)
    {
        List<(long Column, bool Ascending)> terms = [];

        // Split on comma and on whitespace, because the legacy writes both `age A salary A ` and
        // `age A, salary A` and neither form may be rejected.
        string[] tokens = sort.Split(
            [' ', ',', '\t'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        for (int index = 0; index + 1 < tokens.Length; index += 2)
        {
            long column = _definition.NumberOf(tokens[index]);

            if (column < FirstColumn)
            {
                continue;
            }

            terms.Add((column, !tokens[index + 1].StartsWith('D')
                && !tokens[index + 1].StartsWith('d')));
        }

        return [.. terms];
    }

    /// <summary>Parses the equality grammar <see cref="Find"/> accepts.</summary>
    /// <param name="expression">The expression.</param>
    /// <returns>The terms. Empty when the expression is not in the accepted grammar.</returns>
    private (long Column, string Value)[] ParseFindTerms(string expression)
    {
        List<(long Column, string Value)> terms = [];

        foreach (string clause in expression.Split(
            [" and ", " AND "],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int equals = clause.IndexOf('=', StringComparison.Ordinal);

            if (equals <= 0 || equals >= clause.Length - 1)
            {
                return [];
            }

            long column = _definition.NumberOf(clause[..equals].Trim());

            if (column < FirstColumn)
            {
                return [];
            }

            string literal = clause[(equals + 1)..].Trim();

            // A quoted literal is unwrapped, and only a MATCHED pair, so a value that legitimately
            // contains a quote survives.
            if (literal.Length >= 2
                && (literal[0] == '\'' || literal[0] == '"')
                && literal[^1] == literal[0])
            {
                literal = literal[1..^1];
            }

            terms.Add((column, literal));
        }

        return [.. terms];
    }

    /// <summary>Reads one item's current value by column name.</summary>
    /// <param name="row">The ONE-BASED row ordinal.</param>
    /// <param name="column">The column name.</param>
    /// <returns>The value, or <see langword="null"/>.</returns>
    private object? ReadItem(long row, string column)
    {
        ArgumentNullException.ThrowIfNull(column);

        long columnNumber = _definition.NumberOf(column);

        return columnNumber >= FirstColumn && TryRow(row, DwBuffer.Primary, out HeadlessRow? target)
            ? target.ValueOf(columnNumber)
            : null;
    }

    /// <summary>Whether a ONE-BASED row ordinal addresses an existing row of one buffer.</summary>
    /// <param name="row">The ordinal.</param>
    /// <param name="buffer">The buffer.</param>
    /// <returns><see langword="true"/> when the row exists.</returns>
    private bool IsRowInRange(long row, DwBuffer buffer) =>
        row >= FirstRow && row <= _buffers[buffer].Count;

    /// <summary>Resolves a row by its ONE-BASED ordinal.</summary>
    /// <param name="row">The ordinal.</param>
    /// <param name="buffer">The buffer.</param>
    /// <param name="target">The row on success; <see langword="null"/> otherwise.</param>
    /// <returns><see langword="true"/> when the row exists.</returns>
    private bool TryRow(long row, DwBuffer buffer, [NotNullWhen(true)] out HeadlessRow? target)
    {
        if (!IsRowInRange(row, buffer))
        {
            target = null;

            return false;
        }

        target = _buffers[buffer][(int)(row - 1)];

        return true;
    }

    /// <summary>Orders rows by a parsed sort term list, stably.</summary>
    /// <param name="terms">The terms, left to right.</param>
    /// <remarks>
    /// A NULL SORTS FIRST ON ASCENDING, which is SQLite's ordering and the ordering the evidenced
    /// fixture's sort was written against. The row's original index is the final tie-break, which is
    /// what makes the sort STABLE without materialising a separate index.
    /// </remarks>
    private sealed class SortTermComparer((long Column, bool Ascending)[] terms)
        : IComparer<(HeadlessRow Row, int Index)>
    {
        /// <inheritdoc/>
        public int Compare((HeadlessRow Row, int Index) x, (HeadlessRow Row, int Index) y)
        {
            foreach ((long column, bool ascending) in terms)
            {
                int verdict = CompareValues(x.Row.ValueOf(column), y.Row.ValueOf(column));

                if (verdict != 0)
                {
                    return ascending ? verdict : -verdict;
                }
            }

            return x.Index.CompareTo(y.Index);
        }

        /// <summary>Compares two item values, with null ordering first.</summary>
        /// <param name="left">The left value.</param>
        /// <param name="right">The right value.</param>
        /// <returns>A negative, zero or positive verdict.</returns>
        private static int CompareValues(object? left, object? right)
        {
            if (left is null)
            {
                return right is null ? 0 : -1;
            }

            if (right is null)
            {
                return 1;
            }

            // Numeric values compare NUMERICALLY rather than as text, because a text comparison would
            // order 10 before 9 and the evidenced fixture sorts on two numeric columns.
            if (IsNumeric(left) && IsNumeric(right))
            {
                return Convert.ToDecimal(left, CultureInfo.InvariantCulture)
                    .CompareTo(Convert.ToDecimal(right, CultureInfo.InvariantCulture));
            }

            return left is IComparable comparable && left.GetType() == right.GetType()
                ? comparable.CompareTo(right)
                : string.CompareOrdinal(
                    Convert.ToString(left, CultureInfo.InvariantCulture),
                    Convert.ToString(right, CultureInfo.InvariantCulture));
        }

        /// <summary>Whether a value is one of the numeric shapes an item can hold.</summary>
        /// <param name="value">The value.</param>
        /// <returns><see langword="true"/> when it is numeric.</returns>
        private static bool IsNumeric(object value) =>
            value is byte or sbyte or short or ushort or int or uint or long or ulong
                or float or double or decimal;
    }

    /// <summary>One row: an identity, a row status, and per-column current and original values.</summary>
    /// <param name="id">The stable row identity.</param>
    /// <param name="columnCount">How many columns the row carries.</param>
    /// <remarks>
    /// CURRENT AND ORIGINAL ARE BOTH HELD, and that is what the optimistic-concurrency contract needs:
    /// under <c>updatewhere=1</c> the generated predicate carries the ORIGINAL value of every marked
    /// column, so a row that held only its current values could not produce a predicate at all.
    /// </remarks>
    private sealed class HeadlessRow(long id, int columnCount)
    {
        /// <summary>The current values, indexed zero-based internally and ONE-based externally.</summary>
        private readonly object?[] _values = new object?[Math.Max(columnCount, 0)];

        /// <summary>The original values.</summary>
        private readonly object?[] _originals = new object?[Math.Max(columnCount, 0)];

        /// <summary>The per-column statuses.</summary>
        private readonly ItemStatus[] _statuses = new ItemStatus[Math.Max(columnCount, 0)];

        /// <summary>The stable row identity.</summary>
        internal long Id { get; } = id;

        /// <summary>The row status.</summary>
        internal ItemStatus Status { get; set; } = ItemStatus.NotModified;

        /// <summary>Reads one column's current value.</summary>
        /// <param name="columnNumber">The ONE-BASED ordinal.</param>
        /// <returns>The value, or <see langword="null"/> when out of range.</returns>
        internal object? ValueOf(long columnNumber) =>
            IsAddressable(columnNumber) ? _values[columnNumber - 1] : null;

        /// <summary>Reads one column's original value.</summary>
        /// <param name="columnNumber">The ONE-BASED ordinal.</param>
        /// <returns>The value, or <see langword="null"/> when out of range.</returns>
        internal object? OriginalOf(long columnNumber) =>
            IsAddressable(columnNumber) ? _originals[columnNumber - 1] : null;

        /// <summary>Reads one column's status.</summary>
        /// <param name="columnNumber">The ONE-BASED ordinal.</param>
        /// <returns>The status, or <see cref="ItemStatus.NotModified"/> when out of range.</returns>
        internal ItemStatus StatusOf(long columnNumber) =>
            IsAddressable(columnNumber) ? _statuses[columnNumber - 1] : ItemStatus.NotModified;

        /// <summary>Writes one column's current value.</summary>
        /// <param name="columnNumber">The ONE-BASED ordinal.</param>
        /// <param name="value">The value.</param>
        /// <remarks>
        /// THE STATUS IS NOT TOUCHED, and that is PowerBuilder's behaviour rather than an omission:
        /// <c>SetItem</c> writes a value and the runtime's own edit cycle is what marks the item
        /// modified. Marking it here would make every programmatic write generate an UPDATE column, and
        /// the ported paths that write a value back to RESTORE it [<c>se_cst_dw.sru:L221-L226</c>] would
        /// then leave the row dirtier than they found it.
        /// </remarks>
        internal void SetValue(long columnNumber, object? value)
        {
            if (IsAddressable(columnNumber))
            {
                _values[columnNumber - 1] = value;
            }
        }

        /// <summary>Writes one column's status.</summary>
        /// <param name="columnNumber">The ONE-BASED ordinal.</param>
        /// <param name="status">The status.</param>
        /// <returns><see langword="true"/> when the ordinal was addressable.</returns>
        internal bool SetStatusOf(long columnNumber, ItemStatus status)
        {
            if (!IsAddressable(columnNumber))
            {
                return false;
            }

            _statuses[columnNumber - 1] = status;

            return true;
        }

        /// <summary>Copies every current value into the original slot and clears every status.</summary>
        /// <remarks>
        /// THE MANAGED FORM OF <c>ResetUpdate()</c>. It is what a retrieval performs once its rows have
        /// landed, and it is the moment the originals an update predicate carries come into existence.
        /// </remarks>
        internal void Baseline()
        {
            Array.Copy(_values, _originals, _values.Length);
            Array.Clear(_statuses);

            Status = ItemStatus.NotModified;
        }

        /// <summary>Whether a ONE-BASED column ordinal addresses a column of this row.</summary>
        /// <param name="columnNumber">The ordinal.</param>
        /// <returns><see langword="true"/> when addressable.</returns>
        private bool IsAddressable(long columnNumber) =>
            columnNumber >= FirstColumn && columnNumber <= _values.Length;
    }

    /// <summary>One DataWindow object, as the host exposes it.</summary>
    /// <param name="definition">The object's definition.</param>
    /// <param name="columnNumber">Its ONE-BASED column number, or zero for a non-column.</param>
    /// <param name="host">The host it belongs to.</param>
    /// <param name="recognised">
    /// Whether the host actually declares it. An unrecognised object answers the invalid-expression
    /// sentinel for every property, which is how a ported probe tests for absence.
    /// </param>
    private sealed class HeadlessDataWindowObject(
        DataWindowObjectDefinition definition,
        long columnNumber,
        HeadlessDataWindowHost host,
        bool recognised = true) : IDataWindowObject
    {
        /// <summary>The object's definition.</summary>
        internal DataWindowObjectDefinition Definition { get; } = definition;

        /// <inheritdoc/>
        /// <remarks>
        /// THE COLUMN NUMBER, BOXED, WHICH IS WHAT THE LEGACY'S <c>any</c> HOLDS. A non-column answers
        /// zero rather than null, because the ported sites arithmetic on it and null would fault.
        /// </remarks>
        public object? ID => columnNumber;

        /// <inheritdoc/>
        public string Name => Definition.Name;

        /// <inheritdoc/>
        public string ColType => recognised ? Definition.ColType : InvalidExpressionSentinel;

        /// <inheritdoc/>
        public string Type => recognised
            ? Definition.TryDescribe("type", out string type) ? type : string.Empty
            : InvalidExpressionSentinel;

        /// <inheritdoc/>
        public IDataWindowValueBuffer Primary { get; } =
            new HeadlessValueBuffer(host, columnNumber);
    }

    /// <summary>One object's primary-buffer values, addressed by ONE-BASED row.</summary>
    /// <param name="host">The host to read from.</param>
    /// <param name="columnNumber">The ONE-BASED column ordinal.</param>
    private sealed class HeadlessValueBuffer(HeadlessDataWindowHost host, long columnNumber)
        : IDataWindowValueBuffer
    {
        /// <inheritdoc/>
        /// <remarks>
        /// NULL FOR AN OUT-OF-RANGE ROW RATHER THAN AN EXCEPTION, because this indexer is the legacy's
        /// <c>dwo.Primary[row]</c> and the ported sites read it for a row they are about to test.
        /// </remarks>
        public object? this[long row] =>
            host.TryRow(row, DwBuffer.Primary, out HeadlessRow? target)
                ? target.ValueOf(columnNumber)
                : null;
    }
}
