// ==================================================================================================
//  DataWindowCatalogue.cs - THE TRANSCRIBED DATAWINDOW DEFINITIONS
//  ------------------------------------------------------------------------------------------------
//  ROLE
//  The set of DataWindow definitions this service can bind a handle to. `HeadlessDataWindowHostFactory`
//  resolves a handle through it, and a handle it does not carry resolves to nothing - which is what
//  keeps the published unknown-handle negative reachable.
//
//  WHY TRANSCRIBED RATHER THAN PARSED (constraint C-C)
//  The `.srd` files under `ws_objects/` are the behavioural oracle and are READ-ONLY. They are also
//  excluded from every container build context by the root `.dockerignore`, so a service that read one
//  at run time would work locally and fail in a container - the worst possible failure shape. Every
//  literal below is therefore transcribed, and every transcription carries the `ws_objects/**` locator
//  and line it came from so a reviewer can verify it against the oracle by reading rather than by
//  running. NOTHING IN THIS FILE OPENS A FILE.
//
//  WHY ONLY TWO DEFINITIONS
//  These are the two the estate evidences for the in-scope capability set. `dw_sqlite` is the ONLY
//  updatable DataWindow in all 12 definitions in the repository - it is the sole carrier of
//  `updatewhere=1 updatekeyinplace=no` [dw_sqlite.srd:L14] and therefore the golden-master fixture for
//  the whole retrieval/validation/update triple - and `dw_test_dwsvc` is the one the DataWindow service
//  layer's own tests drive, carrying the drop-down list box and drop-down DataWindow columns the
//  context-menu and drop-down-search models read. Inventing further definitions would fabricate
//  specification (constraint C-B).
//
//  REGISTRATION IS OPEN, WHICH IS WHY A HANDLE CAN BE ADDED WITHOUT EDITING THIS FILE
//  `Register` exists so a deployment that materialises further definitions can add them, and so the
//  published surface serves those handles unchanged. The catalogue is a SINGLETON precisely so a
//  registration made once is visible to every subsequent resolve.
// ==================================================================================================

using System.Diagnostics.CodeAnalysis;

namespace PowerFramework.DataServices.Domain;

/// <summary>
/// The DataWindow definitions this service can bind a handle to.
/// </summary>
/// <remarks>
/// THREAD SAFE FOR CONCURRENT RESOLVES AND REGISTRATIONS, unlike the hosts it produces definitions for.
/// The distinction is deliberate: a definition is immutable once registered and is shared by every
/// handle that resolves to it, whereas a host holds one handle's mutable row state and is serialised by
/// its own validation session.
/// </remarks>
public sealed class DataWindowCatalogue
{
    /// <summary>The evidenced updatable DataWindow's name [<c>dw_sqlite.srd</c>].</summary>
    public const string SqliteFixtureName = "dw_sqlite";

    /// <summary>The DataWindow service layer's own fixture name [<c>dw_test_dwsvc.srd</c>].</summary>
    public const string ServiceFixtureName = "dw_test_dwsvc";

    /// <summary>The registered definitions, by name.</summary>
    private readonly Dictionary<string, DataWindowDefinition> _definitions =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Guards the table, which registration mutates.</summary>
    private readonly object _gate = new();

    /// <summary>Initializes the catalogue with the two evidenced definitions.</summary>
    public DataWindowCatalogue()
    {
        Register(CreateSqliteFixture());
        Register(CreateServiceFixture());
    }

    /// <summary>Resolves a definition by name.</summary>
    /// <param name="name">The DataWindow name a handle carries.</param>
    /// <param name="definition">The definition on success; <see langword="null"/> otherwise.</param>
    /// <returns><see langword="true"/> when a definition carries that name.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// A MISS IS <see langword="false"/> RATHER THAN AN EXCEPTION, and it is what keeps the published
    /// unknown-handle negative reachable: the surface answers <c>RetCode.E_INVALID_HANDLE</c> for a
    /// handle no definition matches, and that is CONTRACT rather than a gap. Throwing here would turn a
    /// caller's typo into a fault instead of a defined answer.
    /// </remarks>
    public bool TryGet(string name, [NotNullWhen(true)] out DataWindowDefinition? definition)
    {
        ArgumentNullException.ThrowIfNull(name);

        lock (_gate)
        {
            return _definitions.TryGetValue(name, out definition);
        }
    }

    /// <summary>Registers a definition, replacing any of the same name.</summary>
    /// <param name="definition">The definition.</param>
    /// <exception cref="ArgumentNullException"><paramref name="definition"/> is <see langword="null"/>.</exception>
    public void Register(DataWindowDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        lock (_gate)
        {
            _definitions[definition.Name] = definition;
        }
    }

    /// <summary>The registered names, for a diagnostic that needs to list what is bindable.</summary>
    /// <returns>The names, in registration order within the table's own ordering.</returns>
    public IReadOnlyList<string> Names()
    {
        lock (_gate)
        {
            return [.. _definitions.Keys];
        }
    }

    /// <summary>
    /// Builds the evidenced updatable DataWindow, transcribed from
    /// <c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd</c>.
    /// </summary>
    /// <returns>The definition.</returns>
    private static DataWindowDefinition CreateSqliteFixture()
    {
        DataWindowDefinition definition = new(SqliteFixtureName)
        {
            // :L3 - datawindow(... processing=1 ...). 1 is the GRID presentation style, which is also
            // what makes the focusless row move at se_cst_dw.sru:L152-L158 reachable.
            Processing = "1",

            // :L14 - retrieve, update table, concurrency mode, key handling and sort. The sort's
            // TRAILING SPACE is part of the literal and is carried verbatim.
            RetrieveStatement = "SELECT * FROM COMPANY",
            UpdateTable = "COMPANY",
            UpdateWhere = "1",
            UpdateKeyInPlace = "no",
            TableSort = "age A salary A ",
        };

        // :L15-L20 - the six header text objects, declared BEFORE the columns exactly as the definition
        // declares them. They consume no column number, which is why the column numbering below still
        // runs one to six.
        _ = definition.AddText("id_t", "header");
        _ = definition.AddText("name_t", "header");
        _ = definition.AddText("age_t", "header");
        _ = definition.AddText("address_t", "header");
        _ = definition.AddText("salary_t", "header");
        _ = definition.AddText("birth_t", "header");

        // :L8 + :L21 - id, the key AND identity column. Column number 1.
        DataWindowObjectDefinition id = definition.AddColumn("id", "number");
        id.Update = true;
        id.UpdateWhereClause = true;
        id.Key = true;
        id.Identity = true;
        id.DbName = "COMPANY.id";
        id.Format = "[general]";
        id.TabSequence = "10";

        // :L9 + :L22 - name, char(100). Column number 2.
        DataWindowObjectDefinition name = definition.AddColumn("name", "char(100)");
        name.Update = true;
        name.UpdateWhereClause = true;
        name.DbName = "COMPANY.name";
        name.Format = "[general]";
        name.TabSequence = "20";

        // :L10 + :L23 - age, number. Column number 3.
        DataWindowObjectDefinition age = definition.AddColumn("age", "number");
        age.Update = true;
        age.UpdateWhereClause = true;
        age.DbName = "COMPANY.age";
        age.Format = "[general]";
        age.TabSequence = "30";

        // :L11 + :L24 - address, char(200). Column number 4.
        //
        // ⚠ THE WIDTH DISAGREES WITH THE DATABASE AND THAT IS A PRESERVED DEFECT. The only DDL in the
        // repository declares this column as 50 characters [w_test_sqlite.srw:L463-L469] while the
        // DataWindow declares 200. The mismatch is legacy behaviour reproduced on the Persistence side
        // too, and "correcting" either number here would be a behavioural change (constraint C-B).
        DataWindowObjectDefinition address = definition.AddColumn("address", "char(200)");
        address.Update = true;
        address.UpdateWhereClause = true;
        address.DbName = "COMPANY.address";
        address.Format = "[general]";
        address.TabSequence = "40";

        // :L12 + :L25 - salary, decimal(2). Column number 5. The database column is a REAL, which is the
        // second of the three preserved type mismatches.
        DataWindowObjectDefinition salary = definition.AddColumn("salary", "decimal(2)");
        salary.Update = true;
        salary.UpdateWhereClause = true;
        salary.DbName = "COMPANY.salary";
        salary.Format = "[general]";
        salary.TabSequence = "50";

        // :L13 + :L26 - birth, date, with an edit mask and therefore the editmask edit style. The
        // database column is TEXT, which is the third preserved mismatch.
        //
        // "date" TRUNCATES TO "date" AND REACHES A DIFFERENT COERCION ARM FROM "datetime", which
        // truncates to "datet" [se_cst_dw.sru:L228-L243]. The five-character truncation is why the two
        // must stay spelled exactly as the definition spells them.
        DataWindowObjectDefinition birth = definition.AddColumn("birth", "date");
        birth.Update = true;
        birth.UpdateWhereClause = true;
        birth.DbName = "COMPANY.birth";
        birth.Format = "[general]";
        birth.TabSequence = "60";
        birth.EditStyle = "editmask";
        birth.EditMaskMask = "yyyy-mm-dd";

        // :L27 - the footer computed field. A PAGE-SCOPED aggregate, and its format is UPPER CASE here
        // while every column's is lower case - transcribed as found rather than normalised.
        DataWindowObjectDefinition compute = definition.AddCompute(
            "compute_1",
            "footer",
            "sum(salary for page)");
        compute.Format = "[GENERAL]";

        return definition;
    }

    /// <summary>
    /// Builds the DataWindow service layer's fixture, transcribed from
    /// <c>ws_objects/pfw.tests.pbl.src/dw_test_dwsvc.srd</c>.
    /// </summary>
    /// <returns>The definition.</returns>
    /// <remarks>
    /// NO UPDATE TABLE AT ALL, which is the point of having it alongside the updatable one: the ported
    /// paths that describe an update table must handle its absence, and a catalogue carrying only an
    /// updatable definition would never exercise that arm.
    /// </remarks>
    private static DataWindowDefinition CreateServiceFixture()
    {
        DataWindowDefinition definition = new(ServiceFixtureName)
        {
            // :L14 - the definition declares a sort and no update table. The trailing space is again
            // part of the literal.
            TableSort = "n1 A n2 A n3 A ",
        };

        // :L8-L10 - the three numeric columns, numbers 1 to 3.
        for (int ordinal = 1; ordinal <= 3; ordinal++)
        {
            DataWindowObjectDefinition numeric = definition.AddColumn(
                $"n{ordinal}",
                "decimal(2)");
            numeric.UpdateWhereClause = true;
            numeric.Format = "[general]";
            numeric.TabSequence = (ordinal * 10).ToString(
                System.Globalization.CultureInfo.InvariantCulture);
        }

        // :L11 - s1, a drop-down LIST BOX carrying a code table. Column number 4. The three entries are
        // Simplified Chinese display text against three-letter codes, transcribed verbatim: the display
        // text is what a drop-down search filters on and the code is what is stored.
        DataWindowObjectDefinition s1 = definition.AddColumn("s1", "char(100)");
        s1.UpdateWhereClause = true;
        s1.Format = "[general]";
        s1.TabSequence = "40";
        s1.EditStyle = "ddlb";
        s1.CodeTable.Add(new DataWindowCodeTableEntry("新建", "NEW"));
        s1.CodeTable.Add(new DataWindowCodeTableEntry("确认", "CFD"));
        s1.CodeTable.Add(new DataWindowCodeTableEntry("审核", "ADT"));

        // :L12-L13 - s2 and s3, both drop-down DATAWINDOWS over dw_test_dwsvc_dddw, displaying dsp and
        // storing dat. Columns 5 and 6.
        //
        // THEIR CHILD DATAWINDOW IS NOT MATERIALISED, and GetChild answers failure for them - which is
        // the documented not-a-child branch every ported call site already handles. See
        // HeadlessDataWindowHost.GetChild for why that is the contract rather than a gap.
        foreach ((string column, string tabSequence) in
            new[] { ("s2", "50"), ("s3", "60") })
        {
            DataWindowObjectDefinition dropDown = definition.AddColumn(column, "char(100)");
            dropDown.UpdateWhereClause = true;
            dropDown.Format = "[general]";
            dropDown.TabSequence = tabSequence;
            dropDown.EditStyle = "dddw";
            dropDown.DropDownDataWindow = "dw_test_dwsvc_dddw";
            dropDown.DropDownDisplayColumn = "dsp";
            dropDown.DropDownDataColumn = "dat";
        }

        return definition;
    }
}
