// ==============================================================================================
//  DwSqliteFixture.cs - THE SINGLE TRANSCRIPTION POINT FOR THE REPOSITORY'S GOLDEN-MASTER
//  DATAWINDOW
//  --------------------------------------------------------------------------------------------
//  TRANSCRIBED FROM  ws_objects/pfw.tests.pbl.src/dw_sqlite.srd
//                        :L2         release 12.5
//                        :L3         processing=1  (the CHANGESET path, not full state)
//                        :L8-L13     the six columns, every dbname BARE
//                        :L14        retrieve / update / updatewhere / updatekeyinplace / sort
//                        :L21-L26    column ids 1..6 in declaration order
//                        :L26        birth editmask.ddcalendar=yes editmask.mask="yyyy-mm-dd"
//                        :L27        the footer compute, sum(salary for page)
//                    ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw
//                        :L381-L388  the four batch-seeded rows - EVERY sample literal below
//                        :L398-L400  the dynamic-insert literals, positional ? binding
//                        :L463-L469  the COMPANY DDL, the only DDL anywhere in the repository
//                    ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru
//                        :L10-L17    the six-field tabledata structure this file builds
//                        :L98-L145   the seven-step modification script derived below
//                        :L151-L167  the key-in-place refresh path the fixture EXERCISES
//                        :L215-L245  the identity round trip, Primary forward / Filter backward
//                    ws_objects/pfw.utility.sqlite.pbl.src/sqlitegetitemdouble.srf
//                        :L7-L14     GetItemNumber(row, col, buff, org) - the `org` flag IS the
//                                    original-value accessor, which is why every sample cell
//                                    below carries a current AND an original value
//
//  C-C - ORACLE STATUS. Every path above is READ ONLY. This file TRANSCRIBES them into C# test
//  data. It does not copy a .srd into services/, does not edit, move, reformat or regenerate any
//  of them, and no line of code here writes to any path under ws_objects/. A reader who doubts a
//  value below can diff it against the locator quoted beside it; every locator was read first
//  hand against the export and no divergence was found.
//
//  WHY THIS FILE EXISTS
//  --------------------------------------------------------------------------------------------
//  dw_sqlite.srd is the ONLY updatable DataWindow in the 544-object legacy estate: all twelve
//  DataWindow definitions were scanned and exactly one carries table-level update settings. It is
//  therefore the golden-master fixture for the whole retrieve / validate / update triple, and the
//  sibling tests in this project enumerate it as their principal case. Transcribing it once, here,
//  is what stops eight independent private copies from drifting: a copy that disagrees with the
//  oracle is a silent parity failure, and a copy that disagrees with ANOTHER copy is worse,
//  because both tests keep passing.
//
//  THE ONE PROPERTY THAT MATTERS MOST: EVERY dbname IS BARE [dw_sqlite.srd:L8-L13]
//  --------------------------------------------------------------------------------------------
//  The columns declare dbname="id", not dbname="company.id". That is not cosmetic. Identity
//  discovery lower-cases the update table name and appends a dot
//  [n_cst_thread_task_sqlupdate.sru:L217, ported at IdentityColumnResolver.BuildColumnDbNamePrefix]
//  producing the prefix "company.", and then compares it against the truncated database name of
//  every column marked identity [:L221]. NO BARE NAME CAN EVER MATCH THAT PREFIX. The identity
//  column on this fixture is therefore selected by the OTHER arm of that OR - the
//  "nothing chosen yet" fallback - and not by a prefix match. The bare names are encoded here
//  faithfully and must never be "improved" into qualified ones: qualifying them would move the
//  selection onto the prefix arm and silently retire the fallback arm from the suite's coverage.
//
//  C-B - FIVE SPELLINGS THAT LOOK LIKE MISTAKES AND ARE THE ORACLE'S OWN
//  --------------------------------------------------------------------------------------------
//    * THE SORT VALUE ENDS IN A SPACE: sort="age A salary A " [dw_sqlite.srd:L14]. It is
//      observable, and the changeset row-loss workaround is gated on the sort value being neither
//      "?" nor "" [n_cst_thread_task_sqlquery.sru:L148-L152 - the fix-me note at :L148-L149, the
//      describe at :L151 and the gate itself at :L152], so the exact text is load-bearing.
//    * THE FOURTH SEEDED ADDRESS ENDS IN A SPACE: 'Rich-Mond ' [w_test_sqlite.srw:L388].
//    * THE UPDATE-WHERE VALUE IS SINGLE-QUOTED although it is a number [:L132], while the
//      key-in-place setting beside it is NOT quoted [:L137, L139].
//    * THE PROPERTY IS SPELLED UpdateKeyinPlace, WITH A LOWER-CASE i in "in" [:L137, L139, L155].
//      The C# member spelling UpdateKeyInPlace is a .NET identifier and is deliberately different.
//    * THE SCRIPT'S LAST LINE CARRIES NO TRAILING NEWLINE [:L143].
//
//  C-E - NO FABRICATED DATABASE. Six columns and one table, both evidenced. No second table, no
//  extra column, no index, no foreign key, and no engine other than SQLite appears here. The
//  sample rows are the legacy's OWN seeded literals rather than invented data; the two places
//  where the oracle states no literal at all (a new key value, and the identity values a database
//  would assign) are called out at their point of use as fixture-authored.
//
//  C-F - NO SECRET OF ANY KIND. No password, no connection string, no credential, no token. The
//  legacy's own connection URI carries an optional password parameter [w_test_sqlite.srw:L456,
//  commented as /*[,password]*/] and NO VALUE FOR IT IS REPRODUCED OR IMPLIED anywhere below; this
//  file names no connection at all.
//
//  AAP 0.8.5 - NO PERFORMANCE OR VOLUME CLAIM. Four Primary rows and two Filter rows, chosen to
//  make each behaviour reachable exactly once. Nothing here sizes a workload, and no assertion
//  built on it may be read as a throughput or latency statement, because the repository publishes
//  no such target to compare against.
//
//  AAP 0.7.2 - WARNINGS ARE ERRORS AND NO SCREAMING_SNAKE IDENTIFIER IS DECLARED HERE. The
//  .editorconfig naming suppressions for the preserved legacy constant spellings are scoped to
//  nine PRODUCTION files and no test file is among them, so this file may REFERENCE RetCode.OK and
//  friends but may not DECLARE a constant in that style.
//
//  WHAT THIS FILE DELIBERATELY DOES NOT DO
//  --------------------------------------------------------------------------------------------
//  IT DOES NOT MAP A SAMPLE ROW ONTO CompanyEntity. Doing so would require choosing how a
//  decimal(2) salary becomes a REAL and how a date birth becomes TEXT - the two divergences listed
//  below - and that choice is a production concern belonging to the entity mapping, not a fixture
//  concern. A fixture that quietly decided it would make the divergence untestable by hiding it.
//  The divergences are published as DATA instead, so a test can assert the conversion the
//  production mapping actually performs.
//
//  IT DOES NOT RECORD THE DESCRIBE REQUESTS IT IS ASKED FOR. Two sibling files already own
//  recording doubles for that purpose. The seams here ANSWER from the transcription and nothing
//  more, so there is exactly one reason to read them.
// ==============================================================================================

using System.Globalization;
using PowerFramework.Persistence.Concurrency;

namespace PowerFramework.Persistence.Tests;

#region The transcribed shapes

/// <summary>
/// One column of the fixture's <c>table(...)</c> block, transcribed field for field from
/// <c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L8-L13</c>.
/// </summary>
/// <param name="Id">
/// The one-based column id [<c>dw_sqlite.srd:L21-L26</c>]. R9: this is the ordinal the describe
/// vocabulary uses, so <c>#1</c> addresses the FIRST column and it is never rebased.
/// </param>
/// <param name="Name">The DataWindow column name, as <c>name=</c> declares it.</param>
/// <param name="DbName">
/// The database column name, as <c>dbname=</c> declares it. BARE ON EVERY COLUMN - see the
/// file header for why that is the single most consequential value in this file.
/// </param>
/// <param name="DeclaredType">
/// The DataWindow's own declared type, verbatim and including its width or scale:
/// <c>number</c>, <c>char(100)</c>, <c>char(200)</c>, <c>decimal(2)</c>, <c>date</c>. It is a
/// STRING rather than an enumeration because four of the six carry a width the oracle states and
/// the DDL contradicts, and those contradictions are the subject of
/// <see cref="DwSqliteTypeDivergence"/>.
/// </param>
/// <param name="Update">Whether <c>update=yes</c> is declared. True on all six.</param>
/// <param name="UpdateWhereClause">
/// Whether <c>updatewhereclause=yes</c> is declared. True on all six, which is what makes the
/// <c>updatewhere=1</c> concurrency check span ALL SIX columns' original values.
/// </param>
/// <param name="Key">Whether <c>key=yes</c> is declared. True on <c>id</c> alone.</param>
/// <param name="Identity">Whether <c>identity=yes</c> is declared. True on <c>id</c> alone.</param>
internal sealed record DwSqliteColumn(
    int Id,
    string Name,
    string DbName,
    string DeclaredType,
    bool Update,
    bool UpdateWhereClause,
    bool Key,
    bool Identity);

/// <summary>
/// One column of the COMPANY DDL, transcribed from
/// <c>ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L463-L469</c>.
/// </summary>
/// <param name="Name">
/// The column name in the DDL's own UPPERCASE spelling. The spelling is preserved because the
/// entity mapping preserves it and a sibling asserts it; SQLite identifiers are case-insensitive
/// on comparison but the stored schema text is not.
/// </param>
/// <param name="DeclaredType">
/// The SQLite declared type verbatim: <c>INTEGER</c>, <c>TEXT</c>, <c>INT</c>, <c>CHAR(50)</c>,
/// <c>REAL</c>.
/// </param>
/// <param name="NotNull">Whether the column carries <c>NOT NULL</c>. True on ID, NAME and AGE.</param>
/// <param name="PrimaryKey">Whether the column carries <c>PRIMARY KEY</c>. True on ID alone.</param>
/// <param name="AutoIncrement">
/// Whether the legacy annotates the column as auto-increment. True on ID alone, on the strength of
/// the inline <c>/*自增列*/</c> comment at <c>w_test_sqlite.srw:L464</c> - the legacy's own words,
/// left untranslated here exactly as they are in the oracle. The four batch inserts at
/// <c>:L381-L388</c> corroborate it by omitting ID from their column list entirely.
/// </param>
internal sealed record DwSqliteDdlColumn(
    string Name,
    string DeclaredType,
    bool NotNull,
    bool PrimaryKey,
    bool AutoIncrement);

/// <summary>
/// One declaration divergence between the DataWindow's view of a column and the DDL that creates
/// it: A PRESERVED LEGACY DEFECT, published as data so a sibling can iterate the set as a theory
/// rather than restating it.
/// </summary>
/// <param name="ColumnName">The DataWindow column name the divergence is about.</param>
/// <param name="DataWindowType">What <c>dw_sqlite.srd</c> declares.</param>
/// <param name="DdlType">What the DDL declares. THE DDL WINS, because the DDL creates the table.</param>
/// <param name="DataWindowLocator">The <c>ws_objects/**</c> locator of the DataWindow declaration.</param>
/// <param name="DdlLocator">The <c>ws_objects/**</c> locator of the DDL declaration.</param>
/// <param name="Consequence">
/// What is observable because of the divergence, stated as an observation and NOT as a repair.
/// </param>
/// <remarks>
/// C-B - THESE ARE NOT DEFECTS TO FIX. Reconciling either side would change observable behaviour
/// and silently invalidate every characterization recording taken against this fixture, which is
/// exactly the silent correction the refactor forbids. They are recorded so a future reader cannot
/// mistake a pinned defect for a fixture error.
/// </remarks>
internal sealed record DwSqliteTypeDivergence(
    string ColumnName,
    string DataWindowType,
    string DdlType,
    string DataWindowLocator,
    string DdlLocator,
    string Consequence);

/// <summary>
/// One cell of a sample row: the CURRENT value, the ORIGINAL value and the column's item status.
/// </summary>
/// <param name="ColumnNumber">
/// The one-based column number, matching <see cref="DwSqliteColumn.Id"/>. R9: never rebased.
/// </param>
/// <param name="ColumnName">The column name, carried so a reader never has to count ordinals.</param>
/// <param name="Current">The value the row holds now. <see langword="null"/> is a value, not an absence.</param>
/// <param name="Original">
/// The value the database last saw. EQUAL TO <paramref name="Current"/> ON AN UNEDITED CELL, which
/// is what the carrier itself reports when no original was captured.
/// </param>
/// <param name="Status">
/// The column's item status. <see cref="ItemStatus.DataModified"/> on an edited cell,
/// <see cref="ItemStatus.NotModified"/> otherwise - the status a freshly retrieved column carries.
/// </param>
/// <remarks>
/// <para>
/// BOTH VALUES ARE CARRIED BECAUSE <c>updatewhere=1</c> REQUIRES BOTH ON THE WIRE. The generated
/// update statement's WHERE clause is built from the ORIGINAL values of every marked column
/// [<c>dw_sqlite.srd:L14</c> with <c>updatewhereclause=yes</c> on all six at <c>:L8-L13</c>], so a
/// payload carrying one value per cell cannot express the concurrency check at all. The oracle
/// addresses the pair through a single accessor whose last argument selects between them -
/// <c>GetItemNumber(row, col, buff, org)</c>
/// [<c>ws_objects/pfw.utility.sqlite.pbl.src/sqlitegetitemdouble.srf:L11, L14</c>] - and this
/// record is that pair at rest.
/// </para>
/// <para>
/// THE CLR TYPES FOLLOW AAP 0.4.5.2's TYPE TABLE, NOT THE DDL: <c>number</c> arrives as
/// <see cref="long"/>, <c>char(n)</c> as <see cref="string"/>, <c>decimal(2)</c> as
/// <see cref="decimal"/> WITH ITS SCALE, and <c>date</c> as <see cref="DateOnly"/>. All four are
/// among the published value arms the carrier can move across the boundary.
/// </para>
/// </remarks>
internal sealed record DwSqliteSampleCell(
    int ColumnNumber,
    string ColumnName,
    object? Current,
    object? Original,
    ItemStatus Status)
{
    /// <summary>
    /// Reports whether this cell was edited, by the same equality the carrier uses when it decides
    /// whether an original needs capturing.
    /// </summary>
    internal bool IsEdited => !Equals(Current, Original);
}

/// <summary>
/// One sample row: its buffer, its one-based row number, its row-level item status and its six
/// cells.
/// </summary>
/// <param name="Row">
/// The one-based row number WITHIN <paramref name="Buffer"/>. R9: never rebased, and the numbers
/// restart at one in each buffer exactly as the carrier's own row numbering does.
/// </param>
/// <param name="Buffer">
/// The buffer the row lives in. <see cref="DwBuffer.Filter"/> rows are declared in the order the
/// carrier holds them, WHICH IS INVERTED RELATIVE TO THE SOURCE
/// [<c>n_cst_thread_task_sqlupdate.sru:L235</c>] - see
/// <see cref="DwSqliteFixture.ExpectedFilterIdentityValues"/>.
/// </param>
/// <param name="Status">
/// The row's own status, read in the oracle at column index zero
/// [<c>n_cst_thread_task_sqlupdate.sru:L160, L230</c>].
/// </param>
/// <param name="Cells">The six cells, in column-number order.</param>
/// <remarks>
/// EQUALITY ON THIS RECORD IS REFERENCE EQUALITY FOR <paramref name="Cells"/>, because a record's
/// synthesized equality compares the list REFERENCE rather than its contents. That is stated
/// rather than worked around: nothing in this file or its consumers compares two sample rows for
/// value equality, and substituting a structural collection to make an unused comparison work
/// would be adding a type to satisfy nobody.
/// </remarks>
internal sealed record DwSqliteSampleRow(
    long Row,
    DwBuffer Buffer,
    ItemStatus Status,
    IReadOnlyList<DwSqliteSampleCell> Cells)
{
    /// <summary>
    /// Returns the cell for a one-based column number.
    /// </summary>
    /// <param name="columnNumber">The one-based column number. R9: never rebased.</param>
    /// <returns>The cell.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// No cell carries <paramref name="columnNumber"/>.
    /// </exception>
    internal DwSqliteSampleCell CellAt(int columnNumber)
    {
        foreach (DwSqliteSampleCell cell in Cells)
        {
            if (cell.ColumnNumber == columnNumber)
            {
                return cell;
            }
        }

        throw new ArgumentOutOfRangeException(
            nameof(columnNumber),
            columnNumber,
            "The fixture declares six columns numbered 1 through 6.");
    }

    /// <summary>
    /// Returns the cell for a column name, compared ordinally and case-sensitively because the
    /// transcribed names are the DataWindow's own lower-case spellings.
    /// </summary>
    /// <param name="columnName">The DataWindow column name.</param>
    /// <returns>The cell.</returns>
    /// <exception cref="ArgumentOutOfRangeException">No cell carries <paramref name="columnName"/>.</exception>
    internal DwSqliteSampleCell CellOf(string columnName)
    {
        foreach (DwSqliteSampleCell cell in Cells)
        {
            if (string.Equals(cell.ColumnName, columnName, StringComparison.Ordinal))
            {
                return cell;
            }
        }

        throw new ArgumentOutOfRangeException(
            nameof(columnName),
            columnName,
            "The fixture declares the columns id, name, age, address, salary and birth.");
    }

    /// <summary>
    /// Reports whether the row was inserted and then edited, which is the exact status the identity
    /// round trip collects on [<c>n_cst_thread_task_sqlupdate.sru:L230, L238</c>].
    /// </summary>
    internal bool IsNewModified => ItemStatusMachine.IsNewRow(Status);
}

#endregion

/// <summary>
/// The repository's golden-master DataWindow, transcribed once: its header facts, its six columns,
/// the COMPANY DDL it is bound against, the four declaration divergences between the two, the
/// update contract it produces, the modification script that contract emits, and a small set of
/// sample rows carrying current AND original values.
/// </summary>
/// <remarks>
/// <para>
/// TRANSCRIPTION, NOT TRANSLATION. Every value in this type is a value read out of
/// <c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd</c> or
/// <c>ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw</c>, and each carries the locator it came
/// from. Two values are the exception and both are labelled as such where they are declared: the
/// new key value on the key-edited sample row, and the identity values a database would have
/// assigned to the inserted sample rows. The oracle states neither, because the legacy test window
/// has no key-editing control and never records an assigned identity.
/// </para>
/// <para>
/// THE EXPECTED MODIFICATION SCRIPT IS DERIVED FROM THE TRANSCRIPTION RATHER THAN WRITTEN OUT, so
/// the two cannot drift: change a column name and the script changes with it.
/// <see cref="DwSqliteFixtureTests"/> then compares that derivation against what
/// <see cref="UpdateWhereBuilder.BuildModificationString"/> actually produces, which is what makes
/// the derivation trustworthy rather than merely self-consistent.
/// </para>
/// <para>
/// INTERNAL, NOT PUBLIC, and deliberately so: it hands out
/// <see cref="UpdatableTableDescriptor"/> and <see cref="UpdateRowCounts"/>, which are internal to
/// the application assembly and reachable here only because that assembly grants this one access.
/// A public surface could not name them.
/// </para>
/// </remarks>
internal static class DwSqliteFixture
{
    #region The script vocabulary this file has to spell for itself

    /// <summary>
    /// The <c>" = "</c> that joins a property to its value in the modification script
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru:L105-L143</c>].
    /// </summary>
    /// <remarks>
    /// SPELLED HERE BECAUSE THE PRODUCTION CONSTANT IS PRIVATE. Every other token of the script -
    /// the ordinal prefix, the three attribute suffixes, the two literals, the three property names
    /// and the line separator - is published on <see cref="UpdateWhereBuilder"/> and is consumed
    /// from there rather than duplicated. These two are the only ones this file must restate, and
    /// the self-consistency test compares the assembled result against the real builder precisely
    /// so that a restatement cannot silently disagree with it.
    /// </remarks>
    private const string Assignment = " = ";

    /// <summary>
    /// The single quote wrapped around the update-where value and the update table name
    /// [<c>:L132</c>, <c>:L143</c>]. The key-in-place value beside them is NOT quoted [<c>:L137</c>].
    /// </summary>
    private const string ValueQuote = "'";

    #endregion

    #region Header facts - dw_sqlite.srd:L2, L3, L14, L26, L27

    /// <summary>The DataWindow object's name, from its export header [<c>dw_sqlite.srd:L1</c>].</summary>
    internal const string DataObjectName = "dw_sqlite";

    /// <summary>
    /// The DataWindow release the export declares: <c>release 12.5;</c> [<c>dw_sqlite.srd:L2</c>].
    /// </summary>
    /// <remarks>
    /// A STRING, NOT A NUMBER, and not a version to compare. It records that the sole updatable
    /// fixture was authored against DataWindow release 12.5 while the application object declares
    /// the Appeon PowerBuilder 2021 runtime <c>21.0.0.1311</c>
    /// [<c>ws_objects/pfw.pbl.src/pfw.sra:L34</c>] - a skew that is part of the oracle and is
    /// recorded rather than resolved.
    /// </remarks>
    internal const string Release = "12.5";

    /// <summary>
    /// The <c>processing=1</c> attribute of the <c>datawindow(...)</c> block
    /// [<c>dw_sqlite.srd:L3</c>].
    /// </summary>
    /// <remarks>
    /// THIS VALUE SELECTS THE CROSS-THREAD TRANSFER PATH, WHICH IS WHY IT IS TRANSCRIBED. Only the
    /// crosstab and composite presentation styles select the full-state path
    /// [<see cref="DataWindowProcessing.SelectsFullStateTransfer"/>, and the oracle's own
    /// <c>case 4,5 //Crosstab or Composite datawindow</c> at
    /// <c>n_cst_thread_task_sqlquery.sru:L560-L561</c>]; every other value selects the CHANGESET
    /// path, which the oracle notes does NOT require sort and filter synchronisation
    /// [<c>:L577-L578</c>] while the full-state path DOES [<c>:L562-L563</c>]. No performance claim
    /// is made or implied here; the difference recorded is behavioural. The fixture is a plain
    /// tabular DataWindow, so it takes the changeset path - and since it is the only updatable
    /// DataWindow in the repository, NO REPOSITORY DATAWINDOW SELECTS THE FULL-STATE PATH AT ALL.
    /// The full-state codec is therefore exercised by constructed carriers rather than by this
    /// fixture, and that is a statement about coverage, not a gap in the transcription.
    /// </remarks>
    internal const long ProcessingValue = 1L;

    /// <summary>
    /// The retrieval statement the table block declares, verbatim [<c>dw_sqlite.srd:L14</c>].
    /// </summary>
    internal const string RetrieveStatement = "SELECT * FROM COMPANY";

    /// <summary>
    /// The update table, verbatim and in its UPPERCASE spelling [<c>dw_sqlite.srd:L14</c>].
    /// </summary>
    /// <remarks>
    /// THE CASE MATTERS TWICE OVER. It is emitted into the modification script exactly as spelled
    /// [<c>n_cst_thread_task_sqlupdate.sru:L143</c>], and it is LOWER-CASED to build the identity
    /// discovery prefix [<c>:L217</c>] - so this one value feeds both a case-preserving and a
    /// case-folding path. See <see cref="ColumnDbNamePrefix"/>.
    /// </remarks>
    internal const string UpdateTableName = "COMPANY";

    /// <summary>
    /// The <c>updatewhere=1</c> concurrency mode [<c>dw_sqlite.srd:L14</c>]: "key and updateable
    /// columns", so the generated WHERE clause carries the key column PLUS the ORIGINAL VALUE OF
    /// EVERY UPDATEABLE COLUMN.
    /// </summary>
    /// <remarks>
    /// Combined with <c>updatewhereclause=yes</c> on all six columns [<c>:L8-L13</c>], the check
    /// spans ALL SIX columns' original values. That is the reason every
    /// <see cref="DwSqliteSampleCell"/> carries an original as well as a current value, and the
    /// reason a flat rowset cannot express this fixture's update at all. It equals
    /// <see cref="UpdateWhereBuilder.KeyAndUpdatableColumnsMode"/>, and the self-consistency test
    /// asserts that rather than leaving the two numerically coincident.
    /// </remarks>
    internal const long UpdateWhereMode = 1L;

    /// <summary>
    /// The <c>updatekeyinplace=no</c> setting [<c>dw_sqlite.srd:L14</c>]: a key change is performed
    /// as DELETE plus INSERT rather than as an in-place UPDATE.
    /// </summary>
    /// <remarks>
    /// THE FIXTURE SETS IT, SO THE KEY-CHANGE REFRESH PATH IS THE MAINLINE AND NOT A CORNER. The
    /// oracle documents a defect against itself here: its own fix-me comment states that when the
    /// requested key field is not marked as a key the modified state will not generate the delete
    /// and insert statements, so the internal modified state must be force-refreshed
    /// [<c>n_cst_thread_task_sqlupdate.sru:L151-L154</c>], which it does by assigning each modified
    /// key column TO ITSELF [<c>:L163</c>]. <see cref="SampleRows"/> therefore includes a row whose
    /// KEY column is edited, so that path has data to run against.
    /// </remarks>
    internal const bool UpdateKeyInPlace = false;

    /// <summary>
    /// The sort expression, verbatim [<c>dw_sqlite.srd:L14</c>]: <c>"age A salary A "</c>.
    /// </summary>
    /// <remarks>
    /// C-B - THE TRAILING SPACE IS PART OF THE VALUE AND IS PRESERVED. It is observable, and the
    /// changeset row-loss workaround is gated on the sort value being NEITHER <c>"?"</c> NOR the
    /// empty string [<c>n_cst_thread_task_sqlquery.sru:L148-L152</c>: the fix-me note at
    /// <c>:L148-L149</c>, the <c>DataWindow.Table.Sort</c> describe at <c>:L151</c> and the gate
    /// itself at <c>:L152</c>], so a trimmed value would still
    /// pass that gate while no longer being the oracle's text. Note also that the export puts TWO
    /// spaces before <c>sort=</c> in the table block; that is separator whitespace between
    /// attributes rather than part of any value, so it is recorded here in prose and not in a
    /// constant.
    /// </remarks>
    internal const string SortExpression = "age A salary A ";

    /// <summary>
    /// The footer computed field's expression, verbatim [<c>dw_sqlite.srd:L27</c>].
    /// </summary>
    /// <remarks>
    /// A PAGE-SCOPED AGGREGATE, which is why it is transcribed even though this fixture's own tests
    /// never evaluate it: it is one of the forms the net-new DataWindow expression evaluator must
    /// cover, and it is evidence that the expression engine is reachable from the sole updatable
    /// fixture rather than only from the demo windows.
    /// </remarks>
    internal const string FooterComputeExpression = "sum(salary for page)";

    /// <summary>
    /// The <c>birth</c> column's edit mask, verbatim [<c>dw_sqlite.srd:L26</c>], which also carries
    /// <c>editmask.ddcalendar=yes</c>.
    /// </summary>
    /// <remarks>
    /// THIS MASK IS THE ONLY THING PINNING THE TEXT SHAPE OF A DATE IN THIS SCHEMA. The DDL stores
    /// BIRTH as <c>TEXT</c> [<c>w_test_sqlite.srw:L469</c>] while the DataWindow declares
    /// <c>date</c> [<c>dw_sqlite.srd:L13</c>], and the four seeded literals are written in exactly
    /// this shape [<c>w_test_sqlite.srw:L382-L388</c>]. See the <c>birth</c> entry of
    /// <see cref="TypeDivergences"/>.
    /// </remarks>
    internal const string BirthEditMask = "yyyy-mm-dd";

    /// <summary>
    /// The only column carrying <c>key=yes</c> [<c>dw_sqlite.srd:L8</c>].
    /// </summary>
    internal const string KeyColumnName = "id";

    /// <summary>
    /// The only column carrying <c>identity=yes</c> [<c>dw_sqlite.srd:L8</c>]. The SAME column as
    /// <see cref="KeyColumnName"/>, which is what makes the identity round trip and the key-change
    /// refresh path meet on one column.
    /// </summary>
    internal const string IdentityColumnName = "id";

    /// <summary>
    /// The number of columns, which is also their LAST VALID ONE-BASED ORDINAL
    /// [<c>dw_sqlite.srd:L8-L13</c>, ids at <c>:L21-L26</c>]. It is what
    /// <c>Long(Describe("DataWindow.Column.Count"))</c> answers for this fixture
    /// [<c>n_cst_thread_task_sqlupdate.sru:L103</c>], and therefore how many reset triples step 1
    /// of the modification script emits.
    /// </summary>
    internal const int ColumnCount = 6;

    /// <summary>The one-based ordinal of <c>id</c> [<c>dw_sqlite.srd:L21</c>].</summary>
    internal const int IdColumnNumber = 1;

    /// <summary>The one-based ordinal of <c>name</c> [<c>dw_sqlite.srd:L22</c>].</summary>
    internal const int NameColumnNumber = 2;

    /// <summary>The one-based ordinal of <c>age</c> [<c>dw_sqlite.srd:L23</c>].</summary>
    internal const int AgeColumnNumber = 3;

    /// <summary>The one-based ordinal of <c>address</c> [<c>dw_sqlite.srd:L24</c>].</summary>
    internal const int AddressColumnNumber = 4;

    /// <summary>The one-based ordinal of <c>salary</c> [<c>dw_sqlite.srd:L25</c>].</summary>
    internal const int SalaryColumnNumber = 5;

    /// <summary>The one-based ordinal of <c>birth</c> [<c>dw_sqlite.srd:L26</c>].</summary>
    internal const int BirthColumnNumber = 6;

    /// <summary>
    /// The ordinal <see cref="IdentityColumnResolver.DiscoverIdentityColumn"/> arrives at for this
    /// fixture, WHICH IT REACHES THROUGH THE FALLBACK ARM AND NOT THROUGH A PREFIX MATCH.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The scan tests two things per column: that the column answers <c>"yes"</c> to
    /// <c>#n.Identity</c> [<c>n_cst_thread_task_sqlupdate.sru:L220</c>], and that the truncated
    /// lower-cased <c>#n.DBName</c> equals <see cref="ColumnDbNamePrefix"/> [<c>:L221</c>]. It then
    /// assigns when EITHER the prefix matched OR nothing has been chosen yet.
    /// </para>
    /// <para>
    /// On this fixture only column 1 answers <c>"yes"</c>, and its database name is the BARE
    /// <c>"id"</c> [<c>dw_sqlite.srd:L8</c>], whose first eight characters cannot equal
    /// <c>"company."</c>. The prefix arm therefore never fires and column 1 is chosen because
    /// nothing had been chosen yet. Qualifying the transcribed database names would move the
    /// selection onto the prefix arm, produce the same number, and retire the fallback arm from the
    /// suite's coverage without any test failing - which is exactly why the bare names are
    /// load-bearing.
    /// </para>
    /// </remarks>
    internal const int ExpectedDiscoveredIdentityColumnNumber = IdColumnNumber;

    /// <summary>
    /// The <c>processing</c> attribute as the carrier models it, so a consumer can ask the
    /// production type which transfer path the fixture selects instead of comparing the raw number.
    /// </summary>
    internal static DataWindowProcessing Processing => new(ProcessingValue);

    /// <summary>
    /// The identity-discovery prefix for this fixture, built through the production helper so the
    /// two cannot disagree: <c>Lower("COMPANY") + "."</c>
    /// [<c>n_cst_thread_task_sqlupdate.sru:L217</c>].
    /// </summary>
    internal static string ColumnDbNamePrefix =>
        IdentityColumnResolver.BuildColumnDbNamePrefix(UpdateTableName);

    #endregion


    #region The six columns - dw_sqlite.srd:L8-L13

    /// <summary>
    /// The six columns in declaration order, so their positions are their one-based ids
    /// [<c>dw_sqlite.srd:L8-L13</c>, ids confirmed at <c>:L21-L26</c>].
    /// </summary>
    /// <remarks>
    /// <para>
    /// C-B - EVERY <c>DbName</c> IS BARE, AND THAT IS TRANSCRIPTION RATHER THAN OVERSIGHT. The
    /// export declares <c>dbname="id"</c>, not <c>dbname="company.id"</c>, on all six columns. The
    /// consequence is spelled out on <see cref="ExpectedDiscoveredIdentityColumnNumber"/>.
    /// </para>
    /// <para>
    /// C-E - SIX COLUMNS AND NO MORE. The DDL creates six columns
    /// [<c>w_test_sqlite.srw:L463-L469</c>] and the DataWindow declares the same six; adding a
    /// seventh here would fabricate schema the oracle cannot adjudicate.
    /// </para>
    /// </remarks>
    internal static IReadOnlyList<DwSqliteColumn> Columns { get; } =
    [
        // :L8 - the ONLY column carrying key=yes identity=yes.
        new(
            IdColumnNumber,
            "id",
            "id",
            "number",
            Update: true,
            UpdateWhereClause: true,
            Key: true,
            Identity: true),

        // :L9 - char(100), which the DDL contradicts with an unbounded TEXT NOT NULL.
        new(
            NameColumnNumber,
            "name",
            "name",
            "char(100)",
            Update: true,
            UpdateWhereClause: true,
            Key: false,
            Identity: false),

        // :L10
        new(
            AgeColumnNumber,
            "age",
            "age",
            "number",
            Update: true,
            UpdateWhereClause: true,
            Key: false,
            Identity: false),

        // :L11 - char(200) against a CHAR(50) column.
        new(
            AddressColumnNumber,
            "address",
            "address",
            "char(200)",
            Update: true,
            UpdateWhereClause: true,
            Key: false,
            Identity: false),

        // :L12 - decimal(2) against a REAL column.
        new(
            SalaryColumnNumber,
            "salary",
            "salary",
            "decimal(2)",
            Update: true,
            UpdateWhereClause: true,
            Key: false,
            Identity: false),

        // :L13 - date against a TEXT column, shaped only by the edit mask at :L26.
        new(
            BirthColumnNumber,
            "birth",
            "birth",
            "date",
            Update: true,
            UpdateWhereClause: true,
            Key: false,
            Identity: false),
    ];

    /// <summary>
    /// The six column names in declaration order, which is both the descriptor's updatable-column
    /// order and the order step 2 of the modification script emits.
    /// </summary>
    internal static IReadOnlyList<string> ColumnNames { get; } = [.. Columns.Select(column => column.Name)];

    /// <summary>
    /// The key columns: <c>id</c> alone [<c>dw_sqlite.srd:L8</c>].
    /// </summary>
    /// <remarks>
    /// A LIST WITH ONE ELEMENT RATHER THAN A SCALAR, because the oracle's contract field is an array
    /// [<c>n_cst_thread_task_sqlupdate.sru:L13</c>] and its add-time validation rejects an EMPTY one
    /// [<c>:L84</c>]. Flattening it to a scalar here would misrepresent a shape that genuinely
    /// supports several key columns.
    /// </remarks>
    internal static IReadOnlyList<string> KeyColumnNames { get; } = [KeyColumnName];

    /// <summary>
    /// The six columns as a theory data source: id, name, database name, declared type, and the four
    /// flags.
    /// </summary>
    /// <remarks>
    /// <para>
    /// EVERY ARGUMENT IS A PRIMITIVE, DELIBERATELY. A theory argument has to survive xUnit's
    /// serialization for a test to be discoverable and individually runnable, so this source hands
    /// out <see cref="int"/>, <see cref="string"/> and <see cref="bool"/> rather than the
    /// <see cref="DwSqliteColumn"/> record. A consumer that wants the whole record reads
    /// <see cref="Columns"/> directly.
    /// </para>
    /// <para>
    /// DECLARED <see langword="public"/> ON AN INTERNAL TYPE, which is not a contradiction: the
    /// containing type is internal, so the effective reach of this member is still this assembly.
    /// The declaration matters because a theory data source is resolved by REFLECTION, and every
    /// other data source in this project is a public static member; matching that shape keeps one
    /// idiom for a consumer to copy. The same applies to <see cref="TypeDivergenceMatrix"/>.
    /// </para>
    /// </remarks>
    public static TheoryData<int, string, string, string, bool, bool, bool, bool> ColumnMatrix
    {
        get
        {
            TheoryData<int, string, string, string, bool, bool, bool, bool> matrix = new();

            foreach (DwSqliteColumn column in Columns)
            {
                matrix.Add(
                    column.Id,
                    column.Name,
                    column.DbName,
                    column.DeclaredType,
                    column.Update,
                    column.UpdateWhereClause,
                    column.Key,
                    column.Identity);
            }

            return matrix;
        }
    }

    /// <summary>
    /// Returns a column by name, compared ordinally because the transcribed names are the
    /// DataWindow's own lower-case spellings.
    /// </summary>
    /// <param name="columnName">The DataWindow column name.</param>
    /// <returns>The transcribed column.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="columnName"/> is not one of the fixture's six columns.
    /// </exception>
    internal static DwSqliteColumn ColumnOf(string columnName)
    {
        foreach (DwSqliteColumn column in Columns)
        {
            if (string.Equals(column.Name, columnName, StringComparison.Ordinal))
            {
                return column;
            }
        }

        throw new ArgumentOutOfRangeException(
            nameof(columnName),
            columnName,
            "The fixture declares the columns id, name, age, address, salary and birth.");
    }

    #endregion

    #region The COMPANY DDL - w_test_sqlite.srw:L463-L469

    /// <summary>
    /// The table the DDL creates, in its own UPPERCASE spelling
    /// [<c>w_test_sqlite.srw:L463</c>]. The same spelling the DataWindow's <c>update=</c> attribute
    /// uses, so <see cref="UpdateTableName"/> and this are equal - and they are declared separately
    /// because they are two independent statements in two different files that HAPPEN to agree.
    /// </summary>
    internal const string DdlTableName = "COMPANY";

    /// <summary>
    /// The six DDL columns in declaration order [<c>w_test_sqlite.srw:L463-L469</c>], the only DDL
    /// of any kind anywhere in the repository.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE UPPERCASE SPELLINGS ARE PRESERVED. The statement is assembled from seven PowerScript
    /// literals joined with the continuation operator, and its column names are written in upper
    /// case; the entity mapping preserves them and a sibling asserts them.
    /// </para>
    /// <para>
    /// NULLABILITY FOLLOWS THE DDL EXACTLY AND IS NOT SOFTENED: ID, NAME and AGE carry
    /// <c>NOT NULL</c>, while ADDRESS, SALARY and BIRTH do not and are therefore nullable. ID is the
    /// <c>PRIMARY KEY</c> and the legacy annotates it as the auto-increment column with an inline
    /// <c>/*自增列*/</c> comment [<c>:L464</c>], corroborated by the four batch inserts omitting ID
    /// from their column list [<c>:L381-L388</c>].
    /// </para>
    /// </remarks>
    internal static IReadOnlyList<DwSqliteDdlColumn> DdlColumns { get; } =
    [
        // :L464 - "ID INTEGER PRIMARY KEY NOT NULL," + /*自增列*/
        new("ID", "INTEGER", NotNull: true, PrimaryKey: true, AutoIncrement: true),

        // :L465 - "NAME           TEXT    NOT NULL,"
        new("NAME", "TEXT", NotNull: true, PrimaryKey: false, AutoIncrement: false),

        // :L466 - "AGE            INT     NOT NULL,"
        new("AGE", "INT", NotNull: true, PrimaryKey: false, AutoIncrement: false),

        // :L467 - "ADDRESS        CHAR(50),"
        new("ADDRESS", "CHAR(50)", NotNull: false, PrimaryKey: false, AutoIncrement: false),

        // :L468 - "SALARY         REAL,"
        new("SALARY", "REAL", NotNull: false, PrimaryKey: false, AutoIncrement: false),

        // :L469 - "BIRTH          TEXT)"
        new("BIRTH", "TEXT", NotNull: false, PrimaryKey: false, AutoIncrement: false),
    ];

    /// <summary>
    /// Returns a DDL column by its UPPERCASE name.
    /// </summary>
    /// <param name="ddlColumnName">The DDL column name. Compared case-INSENSITIVELY, because SQLite
    /// compares identifiers that way and a consumer holding a DataWindow column name has the
    /// lower-case spelling.</param>
    /// <returns>The transcribed DDL column.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="ddlColumnName"/> is not one of the six DDL columns.
    /// </exception>
    internal static DwSqliteDdlColumn DdlColumnOf(string ddlColumnName)
    {
        foreach (DwSqliteDdlColumn column in DdlColumns)
        {
            if (string.Equals(column.Name, ddlColumnName, StringComparison.OrdinalIgnoreCase))
            {
                return column;
            }
        }

        throw new ArgumentOutOfRangeException(
            nameof(ddlColumnName),
            ddlColumnName,
            "The DDL declares the columns ID, NAME, AGE, ADDRESS, SALARY and BIRTH.");
    }

    #endregion

    #region The four declaration divergences - PRESERVED LEGACY DEFECTS

    /// <summary>
    /// The four places where the DataWindow's declaration of a column and the DDL that creates it
    /// disagree. ALL FOUR ARE PRESERVED, NONE IS RECONCILED.
    /// </summary>
    /// <remarks>
    /// <para>
    /// C-B, C-K - WHY THESE ARE DATA AND NOT A FIX. The divergence is observable, so reconciling
    /// either side would silently invalidate every characterization recording taken against this
    /// fixture. In every case THE DDL WINS at the database, because the DDL is what creates the
    /// table, while the DataWindow's declaration is what the six-column concurrency predicate is
    /// built from - so both statements are live at once, and that is the defect.
    /// </para>
    /// <para>
    /// THE FIRST THREE ARE NAMED IN THE REFACTOR PLAN AS DEFECTS TO PRESERVE. THE FOURTH IS NOT: the
    /// <c>name</c> divergence was found by diffing the two declarations, and it is recorded here so
    /// that its absence from the plan's list is never read as permission to reconcile it.
    /// </para>
    /// <para>
    /// THE ORDER IS THE PLAN'S OWN NUMBERING - address, salary, birth, then name - so a reader can
    /// line the theory's output up against the document.
    /// </para>
    /// </remarks>
    internal static IReadOnlyList<DwSqliteTypeDivergence> TypeDivergences { get; } =
    [
        new(
            "address",
            "char(200)",
            "CHAR(50)",
            "ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L11",
            "ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L467",
            "The DataWindow accepts four times what the column declares, so a value between 51 and "
                + "200 characters is enterable and reaches the database. SQLite does not enforce a "
                + "CHAR width, so it is stored rather than rejected, and the seeded literals are far "
                + "shorter than either bound - which is why nothing in the legacy ever surfaces it."),

        new(
            "salary",
            "decimal(2)",
            "REAL",
            "ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L12",
            "ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L468",
            "A fixed two-place decimal is declared over a binary floating-point column, so a value "
                + "that is exact in the DataWindow need not be exact in storage. The four seeded "
                + "literals all carry exactly two places, so the fixture reads back cleanly and the "
                + "divergence stays latent."),

        new(
            "birth",
            "date",
            "TEXT",
            "ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L13",
            "ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L469",
            "A typed date is declared over untyped text, so nothing at the database enforces a date "
                + "shape at all. The only thing pinning that shape is the column's own edit mask, "
                + "yyyy-mm-dd [dw_sqlite.srd:L26], which the seeded literals follow."),

        new(
            "name",
            "char(100)",
            "TEXT NOT NULL",
            "ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L9",
            "ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L465",
            "The fourth divergence, unnamed in the refactor plan's body and recorded rather than "
                + "reconciled: the DataWindow caps the column at 100 characters while the DDL is "
                + "unbounded, so the narrower bound is the DataWindow's and exists only in the "
                + "presentation layer. The DDL's NOT NULL is the stricter half of the same "
                + "disagreement, and it is the half the database enforces."),
    ];

    /// <summary>
    /// The four divergences as a theory data source: column name, DataWindow type, DDL type, and
    /// both locators.
    /// </summary>
    /// <remarks>
    /// FIVE STRINGS, SO EVERY ARGUMENT SERIALIZES. The consequence text is deliberately NOT among
    /// them: it is prose for a reader, not a value to assert on, and carrying it into every theory
    /// name would make the test output unreadable. A consumer that wants it reads
    /// <see cref="TypeDivergences"/>.
    /// </remarks>
    public static TheoryData<string, string, string, string, string> TypeDivergenceMatrix
    {
        get
        {
            TheoryData<string, string, string, string, string> matrix = new();

            foreach (DwSqliteTypeDivergence divergence in TypeDivergences)
            {
                matrix.Add(
                    divergence.ColumnName,
                    divergence.DataWindowType,
                    divergence.DdlType,
                    divergence.DataWindowLocator,
                    divergence.DdlLocator);
            }

            return matrix;
        }
    }

    #endregion


    #region The update contract - the six-field descriptor, in both presence shapes

    /// <summary>
    /// The fixture's update contract with BOTH optional settings STATED: table <c>COMPANY</c>, all
    /// six columns updatable, <c>id</c> as the sole key column and as the identity column,
    /// <c>UpdateWhere</c> = 1 and <c>UpdateKeyInPlace</c> = <see langword="false"/>.
    /// </summary>
    /// <returns>The descriptor.</returns>
    /// <remarks>
    /// <para>
    /// THIS IS THE SIX-ARGUMENT SHAPE [<c>n_cst_thread_task_sqlupdate.sru:L82-L96</c>], every field
    /// of it taken from <c>dw_sqlite.srd:L8-L14</c>. A FRESH DESCRIPTOR PER CALL rather than a cached
    /// one: the record copies both column collections on the way in, but a consumer may still reach
    /// a substituted collection through a <c>with</c> expression, and handing every test the same
    /// instance would let one test's <c>with</c> result be mistaken for the fixture.
    /// </para>
    /// <para>
    /// THE UPDATABLE-COLUMN ORDER IS DECLARATION ORDER, which is observable: step 2 of the
    /// modification script emits one line per updatable column IN DESCRIPTOR ORDER
    /// [<c>:L111-L114</c>], so reordering here would change a byte-compared script.
    /// </para>
    /// </remarks>
    internal static UpdatableTableDescriptor Descriptor()
    {
        return UpdatableTableDescriptor.Create(
            UpdateTableName,
            ColumnNames,
            KeyColumnNames,
            IdentityColumnName,
            UpdateWhereMode,
            UpdateKeyInPlace);
    }

    /// <summary>
    /// The same contract with BOTH optional settings ABSENT, which is the shape the four-argument
    /// add-table overload produces.
    /// </summary>
    /// <returns>The descriptor, with <c>UpdateWhere</c> and <c>UpdateKeyInPlace</c> both
    /// <see langword="null"/>.</returns>
    /// <remarks>
    /// <para>
    /// ABSENCE IS A FIRST-CLASS INPUT, NOT AN UNINITIALISED ACCIDENT. The caller-side four-argument
    /// overload declares two locals, calls <c>SetNull</c> on BOTH and only then delegates to the
    /// six-argument form [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlupdate.sru:L227-L234</c>],
    /// and the script generator emits each setting line ONLY when its field is not null
    /// [<c>n_cst_thread_task_sqlupdate.sru:L131-L141</c>]. So absence means "LEAVE THE CARRIER'S OWN
    /// SETTING ALONE" and the property line does not appear at all.
    /// </para>
    /// <para>
    /// WHY THE FIXTURE NEEDS THIS VARIANT AT ALL. Substituting <c>0</c> for an absent update-where
    /// is the most dangerous single mistake available on this contract, because <c>0</c> is a LEGAL
    /// concurrency mode; substituting <see langword="false"/> for an absent key-in-place is worse,
    /// because <see langword="false"/> is the value that TRIGGERS the key-change refresh. A test can
    /// only catch either substitution if it has a descriptor that states neither, and this is it.
    /// </para>
    /// <para>
    /// THE FIXTURE'S EFFECTIVE BEHAVIOUR IS UNCHANGED BY THE ABSENCE, WHICH IS THE POINT. The
    /// carrier's own definition already says <c>updatewhere=1 updatekeyinplace=no</c>
    /// [<c>dw_sqlite.srd:L14</c>], so the effective mode is the same either way - and the key-change
    /// refresh still fires, because its gate reads the value BACK OFF THE CARRIER rather than
    /// consulting the descriptor [<c>n_cst_thread_task_sqlupdate.sru:L155</c>].
    /// </para>
    /// </remarks>
    internal static UpdatableTableDescriptor DescriptorWithAbsentSettings()
    {
        return UpdatableTableDescriptor.Create(
            UpdateTableName,
            ColumnNames,
            KeyColumnNames,
            IdentityColumnName,
            updateWhere: null,
            updateKeyInPlace: null);
    }

    #endregion

    #region The expected modification script - DERIVED, never written out

    /// <summary>
    /// Assembles the seven-step modification script for this fixture from the transcribed data.
    /// </summary>
    /// <param name="includeOptionalSettings">
    /// <see langword="true"/> for <see cref="Descriptor"/>'s shape, which emits steps 5 and 6;
    /// <see langword="false"/> for <see cref="DescriptorWithAbsentSettings"/>'s shape, which emits
    /// neither.
    /// </param>
    /// <returns>The script's lines, in order, WITHOUT separators.</returns>
    /// <remarks>
    /// <para>
    /// THE SEVEN STEPS, IN THE ORACLE'S ORDER [<c>n_cst_thread_task_sqlupdate.sru:L98-L145</c>]:
    /// </para>
    /// <list type="number">
    ///   <item><description>
    ///     ONE RESET TRIPLE PER COLUMN ORDINAL, in Update / Key / Identity order, for
    ///     <c>#1</c> through <c>#6</c> [<c>:L103-L108</c>]. EIGHTEEN LINES, and every one of them is
    ///     redundant for a column that step 2 or 3 re-enables - which is exactly why they must be
    ///     reproduced: the pass IS the meaning of "the contract is re-derived at runtime", and the
    ///     script is compared byte for byte.
    ///   </description></item>
    ///   <item><description>
    ///     <c>&lt;column&gt;.Update = yes</c> per updatable column, BY NAME and in descriptor order
    ///     [<c>:L111-L114</c>].
    ///   </description></item>
    ///   <item><description>
    ///     <c>id.Key = yes</c> per key column [<c>:L116-L125</c>], each name resolved through a
    ///     describe first.
    ///   </description></item>
    ///   <item><description>
    ///     <c>id.Identity = yes</c>, emitted only because the fixture's identity column is non-empty
    ///     [<c>:L127-L129</c>].
    ///   </description></item>
    ///   <item><description>
    ///     <c>DataWindow.Table.UpdateWhere = '1'</c> - SINGLE-QUOTED although the value is a number
    ///     [<c>:L131-L133</c>].
    ///   </description></item>
    ///   <item><description>
    ///     <c>DataWindow.Table.UpdateKeyinPlace = no</c> - NOT quoted, spelled with a LOWER-CASE
    ///     <c>i</c> in "in", and emitting the word <c>no</c> rather than a number
    ///     [<c>:L135-L141</c>].
    ///   </description></item>
    ///   <item><description>
    ///     <c>DataWindow.Table.UpdateTable = 'COMPANY'</c> - unconditional, single-quoted, and
    ///     appended WITHOUT a newline [<c>:L143</c>].
    ///   </description></item>
    /// </list>
    /// <para>
    /// EVERY NUMBER IS FORMATTED INVARIANTLY. The script is machine-read, and a culture-sensitive
    /// conversion could substitute digits or a different negative sign, producing a script that
    /// addresses the wrong column or sets the wrong mode.
    /// </para>
    /// </remarks>
    private static List<string> BuildModificationScriptLines(bool includeOptionalSettings)
    {
        List<string> lines = [];

        // STEP 1 - the reset triple per ONE-BASED ordinal [:L103-L108]. R9: the describe vocabulary
        // is itself one-based, so #1 addresses the first column and the ordinal is emitted exactly
        // as the transcribed column id with no rebasing anywhere.
        foreach (DwSqliteColumn column in Columns)
        {
            string ordinal = UpdateWhereBuilder.ColumnOrdinalPrefix
                + column.Id.ToString(CultureInfo.InvariantCulture);

            lines.Add(
                ordinal + UpdateWhereBuilder.UpdateAttributeSuffix + Assignment
                    + UpdateWhereBuilder.NoLiteral);
            lines.Add(
                ordinal + UpdateWhereBuilder.KeyAttributeSuffix + Assignment
                    + UpdateWhereBuilder.NoLiteral);
            lines.Add(
                ordinal + UpdateWhereBuilder.IdentityAttributeSuffix + Assignment
                    + UpdateWhereBuilder.NoLiteral);
        }

        // STEP 2 - the updatable columns, by name, in descriptor order [:L111-L114].
        foreach (string columnName in ColumnNames)
        {
            lines.Add(
                columnName + UpdateWhereBuilder.UpdateAttributeSuffix + Assignment
                    + UpdateWhereBuilder.YesLiteral);
        }

        // STEP 3 - the key columns [:L116-L125].
        foreach (string keyColumnName in KeyColumnNames)
        {
            lines.Add(
                keyColumnName + UpdateWhereBuilder.KeyAttributeSuffix + Assignment
                    + UpdateWhereBuilder.YesLiteral);
        }

        // STEP 4 - the identity column, which the oracle emits only when the field is non-empty
        // [:L127-L129]. It is non-empty on this fixture [dw_sqlite.srd:L8], so the line appears.
        lines.Add(
            IdentityColumnName + UpdateWhereBuilder.IdentityAttributeSuffix + Assignment
                + UpdateWhereBuilder.YesLiteral);

        if (includeOptionalSettings)
        {
            // STEP 5 - SINGLE-QUOTED although the value is a number [:L131-L133].
            lines.Add(
                UpdateWhereBuilder.UpdateWhereProperty
                    + Assignment
                    + ValueQuote
                    + UpdateWhereMode.ToString(CultureInfo.InvariantCulture)
                    + ValueQuote);

            // STEP 6 - NOT quoted, and emitted as the word rather than as a number [:L135-L141].
            // The property name carries the oracle's lower-case `i`; it is consumed from the
            // production constant rather than respelled here.
            lines.Add(
                UpdateWhereBuilder.UpdateKeyInPlaceProperty
                    + Assignment
                    + (UpdateKeyInPlace ? UpdateWhereBuilder.YesLiteral : UpdateWhereBuilder.NoLiteral));
        }

        // STEP 7 - unconditional, single-quoted, and the LAST line [:L143]. The absence of a
        // trailing separator is expressed by the join in ScriptOf, not here.
        lines.Add(
            UpdateWhereBuilder.UpdateTableProperty
                + Assignment
                + ValueQuote
                + UpdateTableName
                + ValueQuote);

        return lines;
    }

    /// <summary>
    /// Joins script lines the way step 7 leaves them: separated by a single LINE FEED and with NO
    /// trailing separator.
    /// </summary>
    /// <param name="lines">The lines, in order.</param>
    /// <returns>The assembled script.</returns>
    /// <remarks>
    /// THE SEPARATOR IS <see cref="UpdateWhereBuilder.LineSeparator"/>, WHICH IS A BARE LINE FEED.
    /// PowerScript's <c>~n</c> is U+000A alone [<c>:L105-L139</c>], so
    /// <see cref="Environment.NewLine"/> is forbidden here: on Windows it would insert a carriage
    /// return into every line of a machine-read script and change every byte comparison.
    /// </remarks>
    private static string ScriptOf(IEnumerable<string> lines)
    {
        return string.Join(UpdateWhereBuilder.LineSeparator, lines);
    }

    /// <summary>
    /// The expected modification script's lines for <see cref="Descriptor"/>: eighteen reset lines,
    /// six update lines, one key line, one identity line, the two setting lines and the update
    /// table line.
    /// </summary>
    internal static IReadOnlyList<string> ExpectedModificationScriptLines =>
        BuildModificationScriptLines(includeOptionalSettings: true);

    /// <summary>
    /// The expected BYTE-EXACT modification script for <see cref="Descriptor"/>.
    /// </summary>
    /// <remarks>
    /// DERIVED FROM THE TRANSCRIPTION, NOT WRITTEN OUT, so the two cannot drift; and cross-checked
    /// against <see cref="UpdateWhereBuilder.BuildModificationString"/> by
    /// <see cref="DwSqliteFixtureTests"/>, so the derivation cannot be merely self-consistent.
    /// </remarks>
    internal static string ExpectedModificationScript => ScriptOf(ExpectedModificationScriptLines);

    /// <summary>
    /// The expected script's lines for <see cref="DescriptorWithAbsentSettings"/>: the same lines
    /// LESS the update-where line and the key-in-place line.
    /// </summary>
    internal static IReadOnlyList<string> ExpectedModificationScriptLinesWithAbsentSettings =>
        BuildModificationScriptLines(includeOptionalSettings: false);

    /// <summary>
    /// The expected BYTE-EXACT modification script for <see cref="DescriptorWithAbsentSettings"/>.
    /// </summary>
    internal static string ExpectedModificationScriptWithAbsentSettings =>
        ScriptOf(ExpectedModificationScriptLinesWithAbsentSettings);

    #endregion

    #region The describe answers - every property the two production readers ask for

    /// <summary>
    /// What <c>Long(Describe(&lt;column&gt;.Id))</c> answers for each of the fixture's columns
    /// [<c>n_cst_thread_task_sqlupdate.sru:L118</c>], keyed by the exact property string the
    /// production code composes.
    /// </summary>
    /// <remarks>
    /// KEYED BY THE COMPOSED PROPERTY, NOT BY THE COLUMN NAME, and built through
    /// <see cref="UpdateWhereBuilder.ColumnIdSuffix"/> rather than by appending a literal - so a test
    /// that preloads a double from this map is asserting against the property string the production
    /// code will actually request. The comparison is ORDINAL because PowerScript's string equality
    /// is.
    /// </remarks>
    internal static IReadOnlyDictionary<string, int> ColumnIdDescribeAnswers { get; } =
        Columns.ToDictionary(
            column => column.Name + UpdateWhereBuilder.ColumnIdSuffix,
            column => column.Id,
            StringComparer.Ordinal);

    /// <summary>
    /// What <c>Describe("#n.Identity")</c> answers for each ordinal
    /// [<c>n_cst_thread_task_sqlupdate.sru:L220</c>]: the LOWER-CASE <c>"yes"</c> on <c>id</c> and
    /// <c>"no"</c> on the other five.
    /// </summary>
    /// <remarks>
    /// THE ANSWERS ARE THE PRODUCTION LITERALS, and the identity test is an ORDINAL comparison
    /// against <c>"yes"</c> rather than a boolean parse - so <c>"Yes"</c> would fail it and the
    /// column would be skipped. Reproducing the exact casing is what keeps the candidate set right.
    /// </remarks>
    internal static IReadOnlyDictionary<string, string> IdentityDescribeAnswers { get; } =
        Columns.ToDictionary(
            column => IdentityColumnResolver.DescribeIdentityProperty(column.Id),
            column => column.Identity ? UpdateWhereBuilder.YesLiteral : UpdateWhereBuilder.NoLiteral,
            StringComparer.Ordinal);

    /// <summary>
    /// What <c>Describe("#n.DBName")</c> answers for each ordinal
    /// [<c>n_cst_thread_task_sqlupdate.sru:L221</c>]: THE BARE DATABASE NAME, unqualified
    /// [<c>dw_sqlite.srd:L8-L13</c>].
    /// </summary>
    /// <remarks>
    /// THIS MAP IS WHERE THE BARE-NAME CONSEQUENCE BECOMES EXECUTABLE. Not one of these answers
    /// begins with <see cref="ColumnDbNamePrefix"/>, so the prefix arm of the discovery scan never
    /// fires on this fixture and the identity column is chosen by the fallback arm. Do not qualify
    /// these values; see <see cref="ExpectedDiscoveredIdentityColumnNumber"/>.
    /// </remarks>
    internal static IReadOnlyDictionary<string, string> DbNameDescribeAnswers { get; } =
        Columns.ToDictionary(
            column => IdentityColumnResolver.DescribeDbNameProperty(column.Id),
            column => column.DbName,
            StringComparer.Ordinal);

    /// <summary>
    /// What PowerBuilder answers when a describe names a property it cannot read.
    /// </summary>
    /// <remarks>
    /// <c>"!"</c> is PowerBuilder's answer for an invalid property, and the production readers are
    /// built for it: the numeric coercion turns both <c>"!"</c> and <c>"?"</c> into zero
    /// [<see cref="UpdateWhereBuilder.CoerceDescribedNumber"/>], and the identity test simply fails
    /// against a non-<c>"yes"</c> answer. The seams below answer this rather than an empty string or
    /// an exception, so a consumer asking for a property this fixture does not have gets the
    /// oracle's own answer.
    /// </remarks>
    internal const string InvalidPropertyAnswer = "!";

    #endregion


    #region The sample rows - current AND original values, per marked column

    /// <summary>
    /// Returns a column by its one-based number, WITHOUT rebasing: the list is scanned for a
    /// matching <see cref="DwSqliteColumn.Id"/> rather than indexed at <c>number - 1</c>.
    /// </summary>
    /// <param name="columnNumber">The one-based column number. R9: never rebased.</param>
    /// <returns>The transcribed column.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// No column carries <paramref name="columnNumber"/>.
    /// </exception>
    /// <remarks>
    /// R9 - THE SCAN IS THE POINT. One-based to zero-based translation is the single most dangerous
    /// mechanical hazard in this refactor, and a fixture that indexed <c>Columns[columnNumber - 1]</c>
    /// would be asserting the very convention under test. Scanning by the transcribed id keeps the
    /// ordinal domain one-based end to end.
    /// </remarks>
    internal static DwSqliteColumn ColumnAt(int columnNumber)
    {
        foreach (DwSqliteColumn column in Columns)
        {
            if (column.Id == columnNumber)
            {
                return column;
            }
        }

        throw new ArgumentOutOfRangeException(
            nameof(columnNumber),
            columnNumber,
            "The fixture declares six columns numbered 1 through 6.");
    }

    /// <summary>
    /// An unedited cell: current equals original, and the column's status is the one a freshly
    /// retrieved column carries.
    /// </summary>
    /// <param name="columnNumber">The one-based column number.</param>
    /// <param name="value">The value, which is both the current and the original.</param>
    /// <returns>The cell.</returns>
    private static DwSqliteSampleCell Unchanged(int columnNumber, object? value)
    {
        return new DwSqliteSampleCell(
            columnNumber,
            ColumnAt(columnNumber).Name,
            value,
            value,
            ItemStatus.NotModified);
    }

    /// <summary>
    /// An edited cell: a current value the database has not seen, an original value it has, and the
    /// status the oracle's own key-column guard tests for
    /// [<c>n_cst_thread_task_sqlupdate.sru:L162</c>].
    /// </summary>
    /// <param name="columnNumber">The one-based column number.</param>
    /// <param name="current">The value the row holds now.</param>
    /// <param name="original">The value the database last saw.</param>
    /// <returns>The cell.</returns>
    private static DwSqliteSampleCell Edited(int columnNumber, object? current, object? original)
    {
        return new DwSqliteSampleCell(
            columnNumber,
            ColumnAt(columnNumber).Name,
            current,
            original,
            ItemStatus.DataModified);
    }

    /// <summary>
    /// Six sample rows - four in <see cref="DwBuffer.Primary"/> and two in
    /// <see cref="DwBuffer.Filter"/> - each carrying the CURRENT and the ORIGINAL value of all six
    /// marked columns plus its row and column item statuses.
    /// </summary>
    /// <remarks>
    /// <para>
    /// EVERY LITERAL IS THE LEGACY'S OWN. The four batch inserts at
    /// <c>ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L381-L388</c> supply
    /// <c>('Paul',32,'California',20000.00,'1991-05-11')</c>,
    /// <c>('Allen',25,'Texas',15000.88,'1980-05-11')</c>,
    /// <c>('Teddy',23,'Norway',20000.32,'1995-10-11')</c> and
    /// <c>('Mark',25,'Rich-Mond ',65000.16,'1988-07-28')</c>; the dynamic loop at
    /// <c>:L396-L406</c> supplies <c>("Paul",32,"California",20000,"1999-05-08")</c>. Nothing here is
    /// invented data.
    /// </para>
    /// <para>
    /// C-B - THE FOURTH SEEDED ADDRESS ENDS IN A SPACE: <c>'Rich-Mond '</c> [<c>:L388</c>]. It is
    /// transcribed with the space. An ADDRESS of <c>CHAR(50)</c> and a DataWindow <c>char(200)</c>
    /// neither trim nor reject it, so the space is observable on a round trip and in every
    /// concurrency comparison built from the original value.
    /// </para>
    /// <para>
    /// TWO VALUES ARE FIXTURE-AUTHORED AND BOTH ARE LABELLED WHERE THEY APPEAR. The identity values
    /// on the three inserted rows are the numbers the auto-increment column would have assigned after
    /// the four batch rows - the oracle inserts without ID [<c>:L381</c>] and never records what came
    /// back - and the new key value on the key-edited row has no oracle literal at all, because the
    /// legacy test window carries no key-editing control.
    /// </para>
    /// <para>
    /// AAP 0.8.5 - THE SET IS SMALL ON PURPOSE. Six rows make each behaviour reachable exactly once:
    /// an untouched row, a row with two ordinary columns edited, an inserted-and-edited row, a row
    /// whose KEY column is edited, and two filtered inserted rows whose carrier order is INVERTED
    /// relative to the source [<c>n_cst_thread_task_sqlupdate.sru:L235</c>]. Nothing here sizes a
    /// workload and no assertion built on it may be read as a throughput statement.
    /// </para>
    /// </remarks>
    internal static IReadOnlyList<DwSqliteSampleRow> SampleRows { get; } =
    [
        // ----------------------------------------------------------------------------------------
        // PRIMARY 1 - the first seeded row, UNTOUCHED [w_test_sqlite.srw:L382].
        // Original equals current on every column, which is what the carrier reports when no
        // original was captured, and what a freshly retrieved row looks like.
        // ----------------------------------------------------------------------------------------
        new(
            1L,
            DwBuffer.Primary,
            ItemStatus.NotModified,
            [
                Unchanged(IdColumnNumber, 1L),
                Unchanged(NameColumnNumber, "Paul"),
                Unchanged(AgeColumnNumber, 32L),
                Unchanged(AddressColumnNumber, "California"),
                Unchanged(SalaryColumnNumber, 20000.00m),
                Unchanged(BirthColumnNumber, new DateOnly(1991, 5, 11)),
            ]),

        // ----------------------------------------------------------------------------------------
        // PRIMARY 2 - the second seeded row [:L384] with TWO NON-KEY COLUMNS EDITED. The edited-to
        // values are the third seeded row's own literals [:L386], so the row demonstrates a
        // current/original divergence without introducing a value the oracle never wrote.
        //
        // THIS IS THE ROW THAT PROVES A FLAT ROWSET IS INSUFFICIENT: the WHERE clause of its UPDATE
        // carries the ORIGINAL 'Texas' and 15000.88, while the SET list carries the current values.
        // ----------------------------------------------------------------------------------------
        new(
            2L,
            DwBuffer.Primary,
            ItemStatus.DataModified,
            [
                Unchanged(IdColumnNumber, 2L),
                Unchanged(NameColumnNumber, "Allen"),
                Unchanged(AgeColumnNumber, 25L),
                Edited(AddressColumnNumber, "Norway", "Texas"),
                Edited(SalaryColumnNumber, 20000.32m, 15000.88m),
                Unchanged(BirthColumnNumber, new DateOnly(1980, 5, 11)),
            ]),

        // ----------------------------------------------------------------------------------------
        // PRIMARY 3 - INSERTED AND EDITED, carrying the dynamic loop's literals [:L400].
        // Its id is 5: the first value the auto-increment column assigns after the four batch rows.
        // FIXTURE-AUTHORED, because the oracle inserts without ID and never records what came back.
        //
        // A row that did not exist has no value the database last saw, so original equals current
        // throughout and the row's NEWNESS is carried by its ROW status alone - which is exactly
        // what the identity round trip reads, at column index zero [:L230].
        // ----------------------------------------------------------------------------------------
        new(
            3L,
            DwBuffer.Primary,
            ItemStatus.NewModified,
            [
                Unchanged(IdColumnNumber, 5L),
                Unchanged(NameColumnNumber, "Paul"),
                Unchanged(AgeColumnNumber, 32L),
                Unchanged(AddressColumnNumber, "California"),
                Unchanged(SalaryColumnNumber, 20000.00m),
                Unchanged(BirthColumnNumber, new DateOnly(1999, 5, 8)),
            ]),

        // ----------------------------------------------------------------------------------------
        // PRIMARY 4 - the fourth seeded row [:L388], including the TRAILING SPACE in 'Rich-Mond ',
        // WITH ITS KEY COLUMN EDITED. The new key value 44 is FIXTURE-AUTHORED: the oracle states
        // none, because its test window has no key-editing control.
        //
        // THIS IS THE ROW THE updatekeyinplace=no PATH NEEDS. The refresh gate requires the ROW to
        // be DataModified! [:L160] AND the KEY COLUMN to be DataModified! [:L162]; both hold here,
        // so the pair qualifies and the update must generate DELETE plus INSERT rather than an
        // in-place UPDATE. dw_sqlite.srd:L14 sets updatekeyinplace=no itself, so this is the
        // mainline rather than a corner.
        // ----------------------------------------------------------------------------------------
        new(
            4L,
            DwBuffer.Primary,
            ItemStatus.DataModified,
            [
                Edited(IdColumnNumber, 44L, 4L),
                Unchanged(NameColumnNumber, "Mark"),
                Unchanged(AgeColumnNumber, 25L),
                Unchanged(AddressColumnNumber, "Rich-Mond "),
                Unchanged(SalaryColumnNumber, 65000.16m),
                Unchanged(BirthColumnNumber, new DateOnly(1988, 7, 28)),
            ]),

        // ----------------------------------------------------------------------------------------
        // FILTER 1 and FILTER 2 - two INSERTED AND EDITED rows that the current filter excludes,
        // carrying the third seeded row's literals [:L386] and the dynamic loop's [:L400], with the
        // next two auto-increment values.
        //
        // THE ORDER BELOW IS THE CARRIER'S ORDER, WHICH IS INVERTED RELATIVE TO THE SOURCE
        // [n_cst_thread_task_sqlupdate.sru:L235]. That is why the oracle walks this buffer BACKWARDS
        // when it collects identity values [:L237], and why ExpectedFilterIdentityValues reads
        // 7 then 6. A port that "corrected" the direction would produce wrong identity values that a
        // row-count assertion would not catch.
        // ----------------------------------------------------------------------------------------
        new(
            1L,
            DwBuffer.Filter,
            ItemStatus.NewModified,
            [
                Unchanged(IdColumnNumber, 6L),
                Unchanged(NameColumnNumber, "Teddy"),
                Unchanged(AgeColumnNumber, 23L),
                Unchanged(AddressColumnNumber, "Norway"),
                Unchanged(SalaryColumnNumber, 20000.32m),
                Unchanged(BirthColumnNumber, new DateOnly(1995, 10, 11)),
            ]),

        new(
            2L,
            DwBuffer.Filter,
            ItemStatus.NewModified,
            [
                Unchanged(IdColumnNumber, 7L),
                Unchanged(NameColumnNumber, "Paul"),
                Unchanged(AgeColumnNumber, 32L),
                Unchanged(AddressColumnNumber, "California"),
                Unchanged(SalaryColumnNumber, 20000.00m),
                Unchanged(BirthColumnNumber, new DateOnly(1999, 5, 8)),
            ]),
    ];

    /// <summary>
    /// The sample rows in <see cref="DwBuffer.Primary"/>, in ascending row order.
    /// </summary>
    internal static IReadOnlyList<DwSqliteSampleRow> PrimarySampleRows { get; } =
        [.. SampleRows.Where(row => row.Buffer == DwBuffer.Primary)];

    /// <summary>
    /// The sample rows in <see cref="DwBuffer.Filter"/>, IN THE CARRIER'S OWN ORDER - which is
    /// inverted relative to the source [<c>n_cst_thread_task_sqlupdate.sru:L235</c>].
    /// </summary>
    internal static IReadOnlyList<DwSqliteSampleRow> FilterSampleRows { get; } =
        [.. SampleRows.Where(row => row.Buffer == DwBuffer.Filter)];

    /// <summary>
    /// The identity values the round trip collects from <see cref="DwBuffer.Primary"/>, scanning
    /// ASCENDING and taking only rows whose own status is <see cref="ItemStatus.NewModified"/>
    /// [<c>n_cst_thread_task_sqlupdate.sru:L228-L233</c>].
    /// </summary>
    /// <remarks>
    /// DERIVED FROM <see cref="SampleRows"/>, so adding an inserted row updates the expectation with
    /// it. <see cref="ItemStatus.New"/> is deliberately NOT collected: the oracle's guard is an
    /// EQUALITY test against <c>NewModified!</c> alone, so a row inserted but never edited does not
    /// participate.
    /// </remarks>
    internal static IReadOnlyList<long?> ExpectedPrimaryIdentityValues { get; } =
    [
        .. PrimarySampleRows
            .Where(row => row.IsNewModified)
            .OrderBy(row => row.Row)
            .Select(row => (long?)row.CellAt(IdColumnNumber).Current),
    ];

    /// <summary>
    /// The identity values the round trip collects from <see cref="DwBuffer.Filter"/>, scanning
    /// BACKWARDS [<c>n_cst_thread_task_sqlupdate.sru:L237</c>].
    /// </summary>
    /// <remarks>
    /// THE DESCENDING ORDER IS THE WHOLE POINT AND IS NOT A TIDYING CHOICE. The Filter buffer's row
    /// order is documented as INVERTED relative to the source [<c>:L235</c>], so walking it backwards
    /// yields the values in source order. It looks like a defect, it is not, and "correcting" the
    /// direction produces wrong identity values that a row-count assertion would still pass.
    /// </remarks>
    internal static IReadOnlyList<long?> ExpectedFilterIdentityValues { get; } =
    [
        .. FilterSampleRows
            .Where(row => row.IsNewModified)
            .OrderByDescending(row => row.Row)
            .Select(row => (long?)row.CellAt(IdColumnNumber).Current),
    ];

    /// <summary>
    /// The inserted, updated and deleted counts the sample rows imply.
    /// </summary>
    /// <remarks>
    /// A FIXTURE DEFINITION DERIVED FROM THE ROW STATUSES, NOT AN ORACLE-MANDATED TRIPLE. The legacy
    /// reads these three off the carrier after the update has run
    /// [<c>n_cst_thread_task_sqlupdate.sru:L247</c>], so no literal exists to transcribe. Derived
    /// here as: inserted is every <see cref="ItemStatus.NewModified"/> row in either buffer, updated
    /// is every <see cref="ItemStatus.DataModified"/> row, and deleted is zero because the sample
    /// carries no Delete-buffer row.
    /// </remarks>
    internal static UpdateRowCounts SampleRowCounts { get; } = new(
        Inserted: SampleRows.Count(row => row.Status == ItemStatus.NewModified),
        Updated: SampleRows.Count(row => row.Status == ItemStatus.DataModified),
        Deleted: 0L);

    /// <summary>
    /// Builds a carrier holding the sample rows with their originals captured, their current values
    /// applied and their statuses stamped.
    /// </summary>
    /// <returns>The populated carrier.</returns>
    /// <exception cref="InvalidOperationException">
    /// A sample row's declared row number disagrees with the position the carrier appended it at,
    /// which would mean <see cref="SampleRows"/> has been reordered.
    /// </exception>
    /// <remarks>
    /// <para>
    /// THE THREE-PASS SHAPE IS FORCED BY HOW THE CARRIER CAPTURES ORIGINALS, and it also happens to
    /// be the legacy's own sequence. A row's original is captured ONCE PER BASELINE, on the first
    /// write after it [<see cref="CarrierRow.SetValue"/>], so the only way to end up with a genuine
    /// current/original pair is: write the ORIGINAL values, baseline, then write the CURRENT ones.
    /// The baseline step is the legacy's <c>ds.ResetUpdate()</c> immediately after data lands
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlquery.sru:L223, L240</c>].
    /// </para>
    /// <para>
    /// STATUSES ARE STAMPED EXPLICITLY, IN A PASS OF THEIR OWN. Writing a value does NOT flip any
    /// item status in this port - the legacy achieved that by side effect through a self-assignment
    /// [<c>n_cst_thread_task_sqlupdate.sru:L163</c>] and the refactor requires status changes be
    /// expressed explicitly - so every status this fixture wants is set through
    /// <see cref="DataWindowBufferStore.SetItemStatus"/>.
    /// </para>
    /// <para>
    /// A FRESH CARRIER PER CALL. The carrier is mutable by design, so a shared instance would let one
    /// test's edits leak into the next.
    /// </para>
    /// </remarks>
    internal static DataWindowBufferStore SampleCarrier()
    {
        DataWindowProcessing processing = Processing;
        DataWindowBufferStore store = new() { Processing = processing };

        // PASS 1 - admit each row and write the values the database last saw.
        foreach (DwSqliteSampleRow row in SampleRows)
        {
            long appendedAt = store.AppendRow(row.Buffer, row.Status);

            // R9: AppendRow answers the new row's ONE-BASED number within its buffer, and the
            // transcribed row numbers restart at one per buffer for the same reason. A mismatch means
            // SampleRows was reordered, which would silently misalign every expectation derived from
            // it - so it fails loudly here instead.
            if (appendedAt != row.Row)
            {
                throw new InvalidOperationException(
                    "The sample rows must be declared in buffer and row order. Row "
                        + row.Row.ToString(CultureInfo.InvariantCulture)
                        + " of buffer "
                        + row.Buffer
                        + " was appended at position "
                        + appendedAt.ToString(CultureInfo.InvariantCulture)
                        + ".");
            }

            foreach (DwSqliteSampleCell cell in row.Cells)
            {
                store.SetItemValue(row.Row, cell.ColumnNumber, row.Buffer, cell.Original);
            }
        }

        // PASS 2 - the data has landed, so baseline it: originals are cleared and every column now
        // reports its current value as its original, which is what a freshly retrieved row looks
        // like.
        store.ResetUpdate();

        // PASS 3 - apply the edits, which captures each edited cell's original, then stamp the
        // statuses the fixture declares.
        foreach (DwSqliteSampleRow row in SampleRows)
        {
            store.SetItemStatus(row.Row, ItemStatusMachine.RowStatusColumn, row.Buffer, row.Status);

            foreach (DwSqliteSampleCell cell in row.Cells)
            {
                if (cell.IsEdited)
                {
                    store.SetItemValue(row.Row, cell.ColumnNumber, row.Buffer, cell.Current);
                }

                if (cell.Status != ItemStatus.NotModified)
                {
                    store.SetItemStatus(row.Row, cell.ColumnNumber, row.Buffer, cell.Status);
                }
            }
        }

        return store;
    }

    #endregion
}


#region The two seams the production readers consume

/// <summary>
/// The fixture's DESCRIBE surface: it answers every property
/// <see cref="UpdateWhereBuilder.BuildModificationString"/> and
/// <see cref="IdentityColumnResolver.DiscoverIdentityColumn"/> ask for, from the transcription and
/// from nothing else.
/// </summary>
/// <remarks>
/// <para>
/// WHY ONE TYPE IMPLEMENTS BOTH INTERFACES. They overlap on <c>GetColumnCount</c> and both read the
/// same DataWindow, so splitting them would mean two objects that must agree about the fixture's
/// column count - a way for a test to be wrong that nothing would report. In the legacy both are
/// <c>Describe</c> calls on ONE carrier
/// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds.sru:L8</c>, which derives from
/// <c>datastore</c>], so one object answering both is also the faithful shape.
/// </para>
/// <para>
/// IT ANSWERS AND DOES NOT RECORD. Two sibling files already own recording doubles for asserting
/// WHICH properties were asked for; this seam exists so that a test which only needs the fixture's
/// ANSWERS does not have to restate them. There is therefore exactly one reason to read it.
/// </para>
/// <para>
/// AN UNKNOWN PROPERTY ANSWERS <see cref="DwSqliteFixture.InvalidPropertyAnswer"/> RATHER THAN
/// THROWING, because that is what PowerBuilder answers and both production readers are built for
/// it: the numeric coercion turns it into zero, which is the non-positive value the key-column arm
/// rejects [<c>n_cst_thread_task_sqlupdate.sru:L119-L122</c>], and the identity test simply fails
/// against it. Throwing would substitute an exception for a documented failure path.
/// </para>
/// </remarks>
internal sealed class DwSqliteTargetMetadata : IUpdateTargetMetadata, IIdentityColumnMetadata
{
    /// <summary>
    /// Answers <c>Long(Describe("DataWindow.Column.Count"))</c>
    /// [<c>n_cst_thread_task_sqlupdate.sru:L103, L218</c>].
    /// </summary>
    /// <returns>Six, which is also the last valid one-based ordinal.</returns>
    public int GetColumnCount()
    {
        return DwSqliteFixture.ColumnCount;
    }

    /// <summary>
    /// Answers <c>Long(Describe(&lt;column&gt;.Id))</c> [<c>:L118</c>].
    /// </summary>
    /// <param name="columnIdProperty">
    /// The composed property, for example <c>id.Id</c>. Matched ORDINALLY.
    /// </param>
    /// <returns>
    /// The column's one-based id, or zero for a property this fixture does not carry - the value
    /// PowerScript's <c>Long()</c> yields for an invalid describe, and the value the key-column arm
    /// rejects as non-positive.
    /// </returns>
    public int GetColumnId(string columnIdProperty)
    {
        ArgumentNullException.ThrowIfNull(columnIdProperty);

        return DwSqliteFixture.ColumnIdDescribeAnswers.TryGetValue(columnIdProperty, out int columnId)
            ? columnId
            : UpdateWhereBuilder.CoerceDescribedNumber(DwSqliteFixture.InvalidPropertyAnswer);
    }

    /// <summary>
    /// Answers <c>Describe("DataWindow.Table.UpdateKeyinPlace")</c> [<c>:L155</c>].
    /// </summary>
    /// <returns>
    /// The LOWER-CASE <c>"no"</c>, because the fixture's own definition says
    /// <c>updatekeyinplace=no</c> [<c>dw_sqlite.srd:L14</c>].
    /// </returns>
    /// <remarks>
    /// THE CASE IS LOAD-BEARING. The oracle's gate is the exact comparison
    /// <c>Describe(...) = "no"</c>, and PowerScript's string equality is case-sensitive, so
    /// <c>"No"</c> or <c>"NO"</c> would close the gate and skip the key-change refresh entirely.
    /// The answer is composed from the production literals rather than typed as a string here.
    /// </remarks>
    public string DescribeUpdateKeyInPlace()
    {
        return DwSqliteFixture.UpdateKeyInPlace
            ? UpdateWhereBuilder.YesLiteral
            : UpdateWhereBuilder.NoLiteral;
    }

    /// <summary>
    /// Answers <c>Describe("DataWindow.Table.UpdateTable")</c> [<c>:L188</c>], which the oracle reads
    /// back off the carrier after its own script wrote it [<c>:L143</c>].
    /// </summary>
    /// <returns>
    /// <c>COMPANY</c>, in the UPPERCASE spelling the fixture declares - which the discovery scan then
    /// lower-cases to build its prefix [<c>:L217</c>].
    /// </returns>
    public string DescribeUpdateTable()
    {
        return DwSqliteFixture.UpdateTableName;
    }

    /// <summary>
    /// Answers <c>Describe("#n.Identity")</c> [<c>:L220</c>].
    /// </summary>
    /// <param name="identityProperty">The composed property, for example <c>#1.Identity</c>.</param>
    /// <returns>
    /// <c>"yes"</c> for <c>id</c>, <c>"no"</c> for the other five, and
    /// <see cref="DwSqliteFixture.InvalidPropertyAnswer"/> for an ordinal this fixture does not
    /// carry.
    /// </returns>
    public string DescribeColumnIdentity(string identityProperty)
    {
        ArgumentNullException.ThrowIfNull(identityProperty);

        return DwSqliteFixture.IdentityDescribeAnswers.TryGetValue(identityProperty, out string? answer)
            ? answer
            : DwSqliteFixture.InvalidPropertyAnswer;
    }

    /// <summary>
    /// Answers <c>Describe("#n.DBName")</c> [<c>:L221</c>].
    /// </summary>
    /// <param name="dbNameProperty">The composed property, for example <c>#1.DBName</c>.</param>
    /// <returns>
    /// THE BARE DATABASE NAME [<c>dw_sqlite.srd:L8-L13</c>], or
    /// <see cref="DwSqliteFixture.InvalidPropertyAnswer"/> for an ordinal this fixture does not
    /// carry.
    /// </returns>
    /// <remarks>
    /// NOT ONE OF THESE ANSWERS BEGINS WITH <see cref="DwSqliteFixture.ColumnDbNamePrefix"/>, so the
    /// prefix arm of the discovery scan never fires and the identity column is chosen by the fallback
    /// arm. That is the fixture's behaviour, not a shortcoming of this seam.
    /// </remarks>
    public string DescribeColumnDbName(string dbNameProperty)
    {
        ArgumentNullException.ThrowIfNull(dbNameProperty);

        return DwSqliteFixture.DbNameDescribeAnswers.TryGetValue(dbNameProperty, out string? answer)
            ? answer
            : DwSqliteFixture.InvalidPropertyAnswer;
    }
}

/// <summary>
/// The fixture's ROW surface: it answers the counts, the item statuses and the numeric item values
/// that the identity round trip reads, from <see cref="DwSqliteFixture.SampleRows"/>.
/// </summary>
/// <remarks>
/// <para>
/// STATELESS AND READ-ONLY, which is what makes it safe to hand out freely: it holds no rows of its
/// own and mutates nothing, so two tests using it cannot interfere. A test that needs to MUTATE rows
/// uses <see cref="DwSqliteFixture.SampleCarrier"/> instead, which answers with a fresh carrier each
/// time.
/// </para>
/// <para>
/// IT DELIBERATELY DOES NOT WRAP THE CARRIER. Reading the sample rows directly means an assertion
/// failure points at the transcription rather than at the carrier's admission logic, which keeps a
/// failing identity test diagnosable. The carrier is separately proved to reproduce the same rows by
/// <see cref="DwSqliteFixtureTests"/>.
/// </para>
/// </remarks>
internal sealed class DwSqliteSampleRowSource : IIdentityValueSource
{
    /// <summary>
    /// Answers the inserted count [<c>n_cst_thread_task_sqlupdate.sru:L247</c>].
    /// </summary>
    /// <returns><see cref="DwSqliteFixture.SampleRowCounts"/>'s inserted count.</returns>
    public long GetInsertedCount()
    {
        return DwSqliteFixture.SampleRowCounts.Inserted;
    }

    /// <summary>Answers the updated count [<c>:L247</c>].</summary>
    /// <returns><see cref="DwSqliteFixture.SampleRowCounts"/>'s updated count.</returns>
    public long GetUpdatedCount()
    {
        return DwSqliteFixture.SampleRowCounts.Updated;
    }

    /// <summary>Answers the deleted count [<c>:L247</c>].</summary>
    /// <returns>Zero: the sample carries no Delete-buffer row.</returns>
    public long GetDeletedCount()
    {
        return DwSqliteFixture.SampleRowCounts.Deleted;
    }

    /// <summary>
    /// Answers <c>RowCount()</c> [<c>:L228</c>]: the number of PRIMARY sample rows.
    /// </summary>
    /// <returns>Four.</returns>
    public long RowCount()
    {
        return DwSqliteFixture.PrimarySampleRows.Count;
    }

    /// <summary>
    /// Answers <c>FilteredCount()</c> [<c>:L234</c>]: the number of FILTER sample rows.
    /// </summary>
    /// <returns>Two.</returns>
    public long FilteredCount()
    {
        return DwSqliteFixture.FilterSampleRows.Count;
    }

    /// <summary>
    /// Answers <c>GetItemStatus(row, columnIndex, buffer)</c> [<c>:L160, L162, L230, L236</c>].
    /// </summary>
    /// <param name="row">The one-based row number within <paramref name="buffer"/>. R9: never rebased.</param>
    /// <param name="columnIndex">
    /// Zero for the ROW's own status, or a one-based column number for a column's status.
    /// </param>
    /// <param name="buffer">The buffer to read.</param>
    /// <returns>The status.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="buffer"/> carries no row <paramref name="row"/>, or the fixture carries no
    /// such column.
    /// </exception>
    /// <remarks>
    /// THE COLUMN-INDEX SPLIT IS THE PRODUCTION ONE, taken through
    /// <see cref="ItemStatusMachine.TryGetColumnNumber"/> rather than by comparing against a bare
    /// zero, so the sentinel is spelled once for the whole solution.
    /// </remarks>
    public ItemStatus GetItemStatus(long row, int columnIndex, DwBuffer buffer)
    {
        DwSqliteSampleRow target = SampleRowAt(row, buffer);

        return ItemStatusMachine.TryGetColumnNumber(columnIndex, out int columnNumber)
            ? target.CellAt(columnNumber).Status
            : target.Status;
    }

    /// <summary>
    /// Answers the TWO-ARGUMENT <c>GetItemNumber(row, column)</c>, which reads the CURRENT value of
    /// the PRIMARY buffer [<c>:L231</c>].
    /// </summary>
    /// <param name="row">The one-based row number. R9: never rebased.</param>
    /// <param name="columnNumber">The one-based column number. R9: never rebased.</param>
    /// <returns>The value, or <see langword="null"/> when the cell is not an integral number.</returns>
    /// <remarks>
    /// THE TWO ACCESSORS ARE GENUINELY DIFFERENT IN THE ORACLE AND ARE KEPT DIFFERENT HERE. The
    /// Primary collection uses this two-argument form [<c>:L231</c>] while the Filter collection uses
    /// the four-argument one [<c>:L239</c>]; both are reproduced rather than unified, because the
    /// asymmetry is the oracle's.
    /// </remarks>
    public long? GetItemNumber(long row, int columnNumber)
    {
        return GetItemNumber(row, columnNumber, DwBuffer.Primary, originalValue: false);
    }

    /// <summary>
    /// Answers the FOUR-ARGUMENT <c>GetItemNumber(row, column, buffer, original)</c> [<c>:L239</c>,
    /// and see <c>ws_objects/pfw.utility.sqlite.pbl.src/sqlitegetitemdouble.srf:L11, L14</c> for the
    /// same shape].
    /// </summary>
    /// <param name="row">The one-based row number. R9: never rebased.</param>
    /// <param name="columnNumber">The one-based column number. R9: never rebased.</param>
    /// <param name="buffer">The buffer to read.</param>
    /// <param name="originalValue">
    /// <see langword="true"/> for the value the database last saw, <see langword="false"/> for the
    /// value the row holds now. THIS FLAG IS THE WHOLE REASON EVERY SAMPLE CELL CARRIES A PAIR.
    /// </param>
    /// <returns>The value, or <see langword="null"/> when the cell is not an integral number.</returns>
    public long? GetItemNumber(long row, int columnNumber, DwBuffer buffer, bool originalValue)
    {
        DwSqliteSampleCell cell = SampleRowAt(row, buffer).CellAt(columnNumber);

        return ToItemNumber(originalValue ? cell.Original : cell.Current);
    }

    /// <summary>
    /// Finds a sample row by buffer and one-based row number.
    /// </summary>
    /// <param name="row">The one-based row number within <paramref name="buffer"/>.</param>
    /// <param name="buffer">The buffer.</param>
    /// <returns>The sample row.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// No sample row carries that buffer and row number.
    /// </exception>
    /// <remarks>
    /// THROWS RATHER THAN ANSWERING A DEFAULT, matching the carrier's own out-of-range behaviour.
    /// An identity scan that walked past the row count would otherwise collect silent nulls, which
    /// is precisely the failure a one-based translation error produces.
    /// </remarks>
    private static DwSqliteSampleRow SampleRowAt(long row, DwBuffer buffer)
    {
        foreach (DwSqliteSampleRow candidate in DwSqliteFixture.SampleRows)
        {
            if (candidate.Buffer == buffer && candidate.Row == row)
            {
                return candidate;
            }
        }

        throw new ArgumentOutOfRangeException(
            nameof(row),
            row,
            "The fixture carries four Primary rows and two Filter rows, both numbered from one.");
    }

    /// <summary>
    /// Projects a sample cell value onto the integral domain the identity accessors answer in.
    /// </summary>
    /// <param name="value">The cell value.</param>
    /// <returns>
    /// The value when it is an integral number, otherwise <see langword="null"/>.
    /// </returns>
    /// <remarks>
    /// ONLY THE INTEGRAL ARMS ARE HANDLED, AND THAT IS EVIDENCE-LED RATHER THAN LAZY. The single
    /// measured use of these accessors is the IDENTITY COLUMN, which the fixture declares
    /// <c>type=number</c> [<c>dw_sqlite.srd:L8</c>] and carries here as <see cref="long"/>. No
    /// conversion from the text, decimal or date columns is evidenced anywhere, so inventing one -
    /// truncating a decimal, parsing a date - would be fabricating behaviour the oracle does not
    /// have. <see langword="null"/> is a legal answer on this contract: the resolver publishes a
    /// null-detecting member precisely because a collected identity value can be null.
    /// </remarks>
    private static long? ToItemNumber(object? value)
    {
        return value switch
        {
            long integral => integral,
            int integral => integral,
            short integral => integral,
            byte integral => integral,
            _ => null,
        };
    }
}

#endregion


#region Self-consistency - the transcription checked against the PRODUCTION code, not against itself

/// <summary>
/// Proves that the transcription above agrees with the production code that consumes it: the derived
/// modification script against what <see cref="UpdateWhereBuilder"/> actually emits, the descriptor
/// against the six-field contract, the bare database names against the identity discovery arm they
/// force, the sample rows against the carrier, and the derived identity expectations against the two
/// collection walks.
/// </summary>
/// <remarks>
/// <para>
/// WHY A FIXTURE FILE CARRIES TESTS AT ALL. A derivation that is only self-consistent proves nothing:
/// if <see cref="DwSqliteFixture.ExpectedModificationScript"/> were asserted against a hand-written
/// copy of itself, both could be wrong together. Every assertion here therefore runs the transcription
/// through PRODUCTION code and compares the answer. The checks are cheap - no database, no container,
/// no clock - which is why they belong beside the data rather than in a sibling.
/// </para>
/// <para>
/// THESE TESTS ARE NOT A SUBSTITUTE FOR THE SIBLING SUITES. They assert that the FIXTURE is faithful.
/// Whether the production code is faithful to the oracle is asserted by the sibling files that own
/// each behaviour, and where a sibling's expectation ever disagrees with this transcription, THE
/// ORACLE DECIDES - re-read the locator, not the test.
/// </para>
/// </remarks>
public sealed class DwSqliteFixtureTests
{
    #region The derived modification script

    [Fact]
    public void ExpectedModificationScript_IsExactlyWhatTheBuilderEmitsForTheFixture()
    {
        ModificationScriptResult result = UpdateWhereBuilder.BuildModificationString(
            DwSqliteFixture.Descriptor(),
            new DwSqliteTargetMetadata());

        Assert.True(result.IsSucceeded);
        Assert.Equal(RetCode.OK, result.Code);
        Assert.Empty(result.ErrorText);
        Assert.Equal(DwSqliteFixture.ExpectedModificationScript, result.Script);

        // The resolved key column identifiers are an OUTPUT of step 3 and the key-change refresh
        // walks them [n_cst_thread_task_sqlupdate.sru:L156, L162], so the fixture's single key column
        // must resolve to its one-based ordinal.
        Assert.Equal<int>([DwSqliteFixture.IdColumnNumber], result.KeyColumnIds);
    }

    [Fact]
    public void ExpectedModificationScriptLines_MatchTheBuilderLineForLine()
    {
        ModificationScriptResult result = UpdateWhereBuilder.BuildModificationString(
            DwSqliteFixture.Descriptor(),
            new DwSqliteTargetMetadata());

        // Split on the contract separator rather than on Environment.NewLine: the script is joined
        // with a bare line feed [:L105-L139].
        string[] emitted = result.Script.Split(UpdateWhereBuilder.LineSeparator);

        Assert.Equal<string>(DwSqliteFixture.ExpectedModificationScriptLines, emitted);
    }

    [Fact]
    public void ExpectedModificationScript_KeepsTheOraclesFourVisibleSpellings()
    {
        string script = DwSqliteFixture.ExpectedModificationScript;

        // 1 - A BARE LINE FEED, never CRLF [:L105-L139].
        Assert.DoesNotContain("\r", script, StringComparison.Ordinal);

        // 2 - the update-where value is SINGLE-QUOTED although it is a number [:L132].
        Assert.Contains("DataWindow.Table.UpdateWhere = '1'", script, StringComparison.Ordinal);

        // 3 - the key-in-place property carries a LOWER-CASE i in "in" and its value is NOT quoted
        //     [:L137, L139].
        Assert.Contains("DataWindow.Table.UpdateKeyinPlace = no", script, StringComparison.Ordinal);

        // 4 - the last line is the update table and there is NO trailing newline [:L143].
        Assert.EndsWith("DataWindow.Table.UpdateTable = 'COMPANY'", script, StringComparison.Ordinal);
        Assert.False(script.EndsWith('\n'));
    }

    [Fact]
    public void ExpectedModificationScript_OpensWithTheEighteenResetLines()
    {
        // Step 1 emits three lines per column ordinal, in Update / Key / Identity order, for #1
        // through #6 [:L103-L108]. Every one of them is redundant for a column that step 2 or 3
        // re-enables, and all eighteen are reproduced because the pass IS the meaning of
        // "re-derived at runtime" - and because the script is compared byte for byte.
        Assert.Equal<string>(
            [
                "#1.Update = no", "#1.Key = no", "#1.Identity = no",
                "#2.Update = no", "#2.Key = no", "#2.Identity = no",
                "#3.Update = no", "#3.Key = no", "#3.Identity = no",
                "#4.Update = no", "#4.Key = no", "#4.Identity = no",
                "#5.Update = no", "#5.Key = no", "#5.Identity = no",
                "#6.Update = no", "#6.Key = no", "#6.Identity = no",
            ],
            DwSqliteFixture.ExpectedModificationScriptLines.Take(3 * DwSqliteFixture.ColumnCount));
    }

    [Fact]
    public void ExpectedModificationScriptWithAbsentSettings_OmitsEXACTLYTheTwoSettingLines()
    {
        ModificationScriptResult result = UpdateWhereBuilder.BuildModificationString(
            DwSqliteFixture.DescriptorWithAbsentSettings(),
            new DwSqliteTargetMetadata());

        Assert.True(result.IsSucceeded);
        Assert.Equal(DwSqliteFixture.ExpectedModificationScriptWithAbsentSettings, result.Script);

        // ABSENCE EMITS NO LINE AT ALL - not a line carrying a default. That is the whole meaning of
        // the two nullable fields [:L131-L141], and it is what the four-argument overload asks for
        // [n_cst_threading_task_sqlupdate.sru:L230-L231].
        Assert.DoesNotContain(
            UpdateWhereBuilder.UpdateWhereProperty,
            result.Script,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            UpdateWhereBuilder.UpdateKeyInPlaceProperty,
            result.Script,
            StringComparison.Ordinal);

        // And nothing ELSE differs between the two shapes.
        Assert.Equal<string>(
            ["DataWindow.Table.UpdateWhere = '1'", "DataWindow.Table.UpdateKeyinPlace = no"],
            DwSqliteFixture.ExpectedModificationScriptLines.Except(
                DwSqliteFixture.ExpectedModificationScriptLinesWithAbsentSettings,
                StringComparer.Ordinal));
    }

    #endregion

    #region The six-field contract, in both presence shapes

    [Fact]
    public void Descriptor_CarriesTheSixFieldsTheOracleDeclares()
    {
        UpdatableTableDescriptor descriptor = DwSqliteFixture.Descriptor();

        Assert.Equal(DwSqliteFixture.UpdateTableName, descriptor.Name);
        Assert.Equal<string>(DwSqliteFixture.ColumnNames, descriptor.UpdatableColumns);
        Assert.Equal<string>([DwSqliteFixture.KeyColumnName], descriptor.KeyColumns);
        Assert.Equal(DwSqliteFixture.IdentityColumnName, descriptor.IdentityColumn);
        Assert.Equal<long?>(DwSqliteFixture.UpdateWhereMode, descriptor.UpdateWhere);
        Assert.True(descriptor.UpdateKeyInPlace.HasValue);
        Assert.False(descriptor.UpdateKeyInPlace);

        // It passes both admission gates: the oracle's own three-armed validation [:L84] and the
        // identifier grammar the boundary adds.
        Assert.True(descriptor.IsAcceptable);
        Assert.True(descriptor.IsScriptSafe);
        Assert.True(descriptor.HasIdentityColumn);
    }

    [Fact]
    public void DescriptorWithAbsentSettings_StatesNeitherOptionalSetting()
    {
        UpdatableTableDescriptor descriptor = DwSqliteFixture.DescriptorWithAbsentSettings();

        Assert.False(descriptor.UpdateWhere.HasValue);
        Assert.False(descriptor.UpdateKeyInPlace.HasValue);

        // Absence is not a validation failure: the four-argument overload is a legal way to add a
        // table [n_cst_threading_task_sqlupdate.sru:L227-L234].
        Assert.True(descriptor.IsAcceptable);
        Assert.True(descriptor.IsScriptSafe);

        // Everything else is identical to the stated shape.
        Assert.Equal(DwSqliteFixture.Descriptor().Name, descriptor.Name);
        Assert.Equal<string>(DwSqliteFixture.Descriptor().UpdatableColumns, descriptor.UpdatableColumns);
    }

    [Fact]
    public void Descriptor_MarksAllSixColumns_SoTheConcurrencyCheckSpansAllSixOriginals()
    {
        UpdatableTableDescriptor descriptor = DwSqliteFixture.Descriptor();

        // updatewhere=1 is "key AND updateable columns", and all six columns carry
        // updatewhereclause=yes [dw_sqlite.srd:L8-L14] - so the WHERE clause is built from all six
        // originals, which is why every sample cell carries a pair.
        Assert.Equal<string>(
            DwSqliteFixture.ColumnNames,
            UpdateWhereBuilder.MarkedColumnsOf(descriptor));
        Assert.True(UpdateWhereBuilder.IsKeyAndUpdatableColumnsMode(descriptor.UpdateWhere));
        Assert.Equal(UpdateWhereBuilder.KeyAndUpdatableColumnsMode, DwSqliteFixture.UpdateWhereMode);
    }

    #endregion

    #region The bare database names, and the discovery arm they force

    [Fact]
    public void EveryColumnDbNameIsBare_SoIdentityDiscoveryTakesTheFallbackArm()
    {
        string prefix = DwSqliteFixture.ColumnDbNamePrefix;

        // Lower("COMPANY") + "." [n_cst_thread_task_sqlupdate.sru:L217].
        Assert.Equal("company.", prefix);

        // NOT ONE database name is qualified [dw_sqlite.srd:L8-L13]: each is exactly its column name.
        foreach (DwSqliteColumn column in DwSqliteFixture.Columns)
        {
            Assert.Equal(column.Name, column.DbName);
        }

        Assert.DoesNotContain(
            DwSqliteFixture.Columns,
            column => column.DbName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

        // The prefix arm therefore never fires, and the identity column is still found - by the
        // "nothing chosen yet" arm of the same OR [:L221-L222].
        int discovered = IdentityColumnResolver.DiscoverIdentityColumn(new DwSqliteTargetMetadata());

        Assert.Equal(DwSqliteFixture.ExpectedDiscoveredIdentityColumnNumber, discovered);
        Assert.Equal(DwSqliteFixture.IdColumnNumber, discovered);
        Assert.NotEqual(IdentityColumnResolver.NoIdentityColumn, discovered);
    }

    [Theory]
    [MemberData(nameof(DwSqliteFixture.ColumnMatrix), MemberType = typeof(DwSqliteFixture))]
    public void EveryColumnIsUpdatableAndCarriesTheUpdateWhereClause(
        int id,
        string name,
        string dbName,
        string declaredType,
        bool update,
        bool updateWhereClause,
        bool key,
        bool identity)
    {
        DwSqliteColumn column = DwSqliteFixture.ColumnAt(id);

        Assert.Equal(column.Name, name);
        Assert.Equal(column.DbName, dbName);
        Assert.Equal(column.DeclaredType, declaredType);
        Assert.NotEmpty(declaredType);

        // The database name is BARE, so it equals the column name on every column.
        Assert.Equal(name, dbName);

        // All six carry update=yes AND updatewhereclause=yes [dw_sqlite.srd:L8-L13].
        Assert.True(update);
        Assert.True(updateWhereClause);

        // Only id carries key=yes identity=yes [:L8].
        if (id == DwSqliteFixture.IdColumnNumber)
        {
            Assert.True(key);
            Assert.True(identity);
            Assert.Equal(DwSqliteFixture.KeyColumnName, name);
            Assert.Equal(DwSqliteFixture.IdentityColumnName, name);
        }
        else
        {
            Assert.False(key);
            Assert.False(identity);
        }
    }

    [Fact]
    public void TheSixColumnOrdinalsAreOneBasedAndContiguous()
    {
        // R9: ids 1..6 as the export declares them [dw_sqlite.srd:L21-L26], and the describe
        // vocabulary reads the same ordinals, so nothing here is ever rebased.
        Assert.Equal<int>(
            [
                DwSqliteFixture.IdColumnNumber,
                DwSqliteFixture.NameColumnNumber,
                DwSqliteFixture.AgeColumnNumber,
                DwSqliteFixture.AddressColumnNumber,
                DwSqliteFixture.SalaryColumnNumber,
                DwSqliteFixture.BirthColumnNumber,
            ],
            DwSqliteFixture.Columns.Select(column => column.Id));

        Assert.Equal<int>([1, 2, 3, 4, 5, 6], DwSqliteFixture.Columns.Select(column => column.Id));
        Assert.Equal(
            DwSqliteFixture.ColumnCount,
            new DwSqliteTargetMetadata().GetColumnCount());
    }

    #endregion

    #region The DDL and the four preserved divergences

    [Fact]
    public void TheDdlTranscribesSixUppercaseColumnsWithTheirNullability()
    {
        // The DDL's own UPPERCASE spellings [w_test_sqlite.srw:L463-L469], in declaration order.
        Assert.Equal<string>(
            ["ID", "NAME", "AGE", "ADDRESS", "SALARY", "BIRTH"],
            DwSqliteFixture.DdlColumns.Select(column => column.Name));

        // The same six columns as the DataWindow declares, in the same order - two independent
        // statements in two files that agree on the roster.
        Assert.Equal<string>(
            DwSqliteFixture.ColumnNames.Select(name => name.ToUpperInvariant()),
            DwSqliteFixture.DdlColumns.Select(column => column.Name));

        // NOT NULL on ID, NAME and AGE only; nullability is not softened anywhere.
        Assert.Equal<string>(
            ["ID", "NAME", "AGE"],
            DwSqliteFixture.DdlColumns.Where(column => column.NotNull).Select(column => column.Name));

        // ID alone is the PRIMARY KEY, and the legacy annotates it as the auto-increment column
        // [:L464, /*自增列*/].
        DwSqliteDdlColumn id = DwSqliteFixture.DdlColumnOf("ID");
        Assert.Equal("INTEGER", id.DeclaredType);
        Assert.True(id.PrimaryKey);
        Assert.True(id.AutoIncrement);
        Assert.Single(DwSqliteFixture.DdlColumns, column => column.PrimaryKey);
        Assert.Single(DwSqliteFixture.DdlColumns, column => column.AutoIncrement);
    }

    [Theory]
    [MemberData(nameof(DwSqliteFixture.TypeDivergenceMatrix), MemberType = typeof(DwSqliteFixture))]
    public void EachDivergenceNamesAColumnDeclaredTwiceWithTwoDifferentTypes(
        string columnName,
        string dataWindowType,
        string ddlType,
        string dataWindowLocator,
        string ddlLocator)
    {
        DwSqliteColumn column = DwSqliteFixture.ColumnOf(columnName);
        DwSqliteDdlColumn ddlColumn = DwSqliteFixture.DdlColumnOf(columnName);

        // Each side of the divergence is the type its own file declares.
        Assert.Equal(column.DeclaredType, dataWindowType);
        Assert.Contains(ddlColumn.DeclaredType, ddlType, StringComparison.Ordinal);

        // And the two genuinely differ, which is what makes it a divergence rather than a note.
        Assert.NotEqual(dataWindowType, ddlType);

        // THE LOCATORS ARE CHECKED ARITHMETICALLY, not merely for shape. The table block's column
        // list starts at dw_sqlite.srd:L8 and the DDL's at w_test_sqlite.srw:L464, both in the same
        // order, so column n is declared at srd line 7 + n and at srw line 463 + n. A locator that
        // drifted off its column would fail here.
        Assert.Equal(
            "ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L"
                + (7 + column.Id).ToString(CultureInfo.InvariantCulture),
            dataWindowLocator);
        Assert.Equal(
            "ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L"
                + (463 + column.Id).ToString(CultureInfo.InvariantCulture),
            ddlLocator);
    }

    [Fact]
    public void AllFourDivergencesArePresentAndEachCarriesItsConsequence()
    {
        // The refactor plan's own numbering: address, salary, birth, then the fourth it does not
        // name. All four are recorded, none is reconciled.
        Assert.Equal<string>(
            ["address", "salary", "birth", "name"],
            DwSqliteFixture.TypeDivergences.Select(divergence => divergence.ColumnName));

        foreach (DwSqliteTypeDivergence divergence in DwSqliteFixture.TypeDivergences)
        {
            Assert.NotEmpty(divergence.Consequence);
        }
    }

    #endregion

    #region The sample rows, the carrier and the identity round trip

    [Fact]
    public void ProcessingOne_SelectsTheChangesetPathAndNotTheFullStatePath()
    {
        Assert.Equal(1L, DwSqliteFixture.ProcessingValue);

        // Only the crosstab and composite styles select the full-state path, so the sole updatable
        // DataWindow in the repository takes the CHANGESET path [dw_sqlite.srd:L3].
        Assert.False(DwSqliteFixture.Processing.SelectsFullStateTransfer);
        Assert.False(DwSqliteFixture.SampleCarrier().RequiresFullStateTransfer);
    }

    [Fact]
    public void SampleCarrier_ReproducesEveryCurrentValue_EveryOriginal_AndEveryStatus()
    {
        DataWindowBufferStore carrier = DwSqliteFixture.SampleCarrier();

        foreach (DwSqliteSampleRow row in DwSqliteFixture.SampleRows)
        {
            // The row's own status is read at column index zero [n_cst_thread_task_sqlupdate.sru:L160].
            Assert.Equal(
                row.Status,
                carrier.GetItemStatus(row.Row, ItemStatusMachine.RowStatusColumn, row.Buffer));

            foreach (DwSqliteSampleCell cell in row.Cells)
            {
                Assert.Equal(
                    cell.Current,
                    carrier.GetItemValue(row.Row, cell.ColumnNumber, row.Buffer));

                // THE ORIGINAL IS THE POINT: updatewhere=1 builds its WHERE clause from these.
                Assert.Equal(
                    cell.Original,
                    carrier.GetItemOriginalValue(row.Row, cell.ColumnNumber, row.Buffer));

                Assert.Equal(
                    cell.Status,
                    carrier.GetItemStatus(row.Row, cell.ColumnNumber, row.Buffer));
            }
        }
    }

    [Fact]
    public void EditedCellsDivergeAndUneditedCellsDoNot()
    {
        foreach (DwSqliteSampleRow row in DwSqliteFixture.SampleRows)
        {
            foreach (DwSqliteSampleCell cell in row.Cells)
            {
                if (cell.Status == ItemStatus.DataModified)
                {
                    Assert.True(cell.IsEdited);
                    Assert.NotEqual(cell.Original, cell.Current);
                }
                else
                {
                    Assert.False(cell.IsEdited);
                    Assert.Equal(cell.Original, cell.Current);
                }
            }
        }
    }

    [Fact]
    public void ExactlyOneSampleRowEditsTheKeyColumn_WhichIsWhatTheKeyInPlacePathNeeds()
    {
        DwSqliteSampleRow keyEdited = Assert.Single(
            DwSqliteFixture.PrimarySampleRows,
            row => row.CellOf(DwSqliteFixture.KeyColumnName).IsEdited);

        // The refresh gate requires the ROW to be DataModified! [:L160] AND the KEY COLUMN to be
        // DataModified! [:L162]; both hold on this row, so the pair qualifies.
        Assert.Equal(ItemStatus.DataModified, keyEdited.Status);
        Assert.Equal(
            ItemStatus.DataModified,
            keyEdited.CellOf(DwSqliteFixture.KeyColumnName).Status);

        // And the gate itself is open, because the fixture's own definition says
        // updatekeyinplace=no [dw_sqlite.srd:L14] - read back off the carrier, case-sensitively
        // [:L155].
        Assert.False(DwSqliteFixture.UpdateKeyInPlace);
        Assert.Equal(
            UpdateWhereBuilder.NoLiteral,
            new DwSqliteTargetMetadata().DescribeUpdateKeyInPlace());
    }

    [Fact]
    public void IdentityValues_AreCollectedAscendingFromPrimaryAndBackwardsFromFilter()
    {
        DwSqliteSampleRowSource source = new();

        Assert.Equal<long?>(
            DwSqliteFixture.ExpectedPrimaryIdentityValues,
            IdentityColumnResolver.CollectPrimaryValues(source, DwSqliteFixture.IdColumnNumber));

        IReadOnlyList<long?> filterValues =
            IdentityColumnResolver.CollectFilterValues(source, DwSqliteFixture.IdColumnNumber);

        Assert.Equal<long?>(DwSqliteFixture.ExpectedFilterIdentityValues, filterValues);

        // THE INVERSION, ASSERTED RATHER THAN DESCRIBED. The Filter buffer's row order is inverted
        // relative to the source [:L235], so the backward walk [:L237] yields the carrier's rows in
        // reverse. "Correcting" the direction would produce wrong identity values that a row-count
        // assertion would still pass.
        Assert.Equal<long?>(
            DwSqliteFixture.FilterSampleRows
                .Select(row => (long?)row.CellAt(DwSqliteFixture.IdColumnNumber).Current)
                .Reverse(),
            filterValues);

        // Only inserted-and-edited rows participate: the guard is an EQUALITY test against
        // NewModified! [:L230, L238], so nothing else is collected.
        Assert.Equal<long?>([5L], DwSqliteFixture.ExpectedPrimaryIdentityValues);
        Assert.Equal<long?>([7L, 6L], DwSqliteFixture.ExpectedFilterIdentityValues);
    }

    [Fact]
    public void SampleRowCounts_AreWhatTheResolverReadsFromTheSampleSource()
    {
        DwSqliteSampleRowSource source = new();

        Assert.Equal(
            DwSqliteFixture.SampleRowCounts,
            IdentityColumnResolver.ReadCounts(source));

        // Derived from the row statuses: three inserted, two updated, none deleted.
        Assert.Equal(3L, source.GetInsertedCount());
        Assert.Equal(2L, source.GetUpdatedCount());
        Assert.Equal(0L, source.GetDeletedCount());

        // Four Primary rows and two Filter rows, and the two counts are read through DIFFERENT
        // accessors in the oracle - RowCount() for Primary [:L228] and FilteredCount() for Filter
        // [:L236].
        Assert.Equal(4L, source.RowCount());
        Assert.Equal(2L, source.FilteredCount());
    }

    #endregion

    #region The whitespace the oracle carries, which a tidying edit would silently eat

    [Fact]
    public void TheTwoTrailingSpacesTheOracleCarriesSurviveTranscription()
    {
        // 1 - the sort value [dw_sqlite.srd:L14]. The changeset row-loss workaround is gated on this
        // value being neither "?" nor empty [n_cst_thread_task_sqlquery.sru:L151-L152], so the exact
        // text is load-bearing.
        Assert.Equal("age A salary A ", DwSqliteFixture.SortExpression);
        Assert.EndsWith(" ", DwSqliteFixture.SortExpression, StringComparison.Ordinal);

        // 2 - the fourth seeded address [w_test_sqlite.srw:L388]. Neither CHAR(50) nor char(200)
        // trims it, so the space reaches the database and every comparison built from the original.
        DwSqliteSampleCell address = DwSqliteFixture.PrimarySampleRows
            .Single(row => row.Row == 4L)
            .CellOf("address");

        Assert.Equal("Rich-Mond ", address.Current);
        Assert.Equal("Rich-Mond ", address.Original);
    }

    [Fact]
    public void TheTranscribedHeaderFactsAreTheOraclesOwnText()
    {
        Assert.Equal("dw_sqlite", DwSqliteFixture.DataObjectName);
        Assert.Equal("12.5", DwSqliteFixture.Release);
        Assert.Equal("SELECT * FROM COMPANY", DwSqliteFixture.RetrieveStatement);
        Assert.Equal("COMPANY", DwSqliteFixture.UpdateTableName);
        Assert.Equal("sum(salary for page)", DwSqliteFixture.FooterComputeExpression);
        Assert.Equal("yyyy-mm-dd", DwSqliteFixture.BirthEditMask);

        // The update table and the DDL table are two independent statements that agree, and the key
        // column and the identity column are ONE column [dw_sqlite.srd:L8, L14].
        Assert.Equal(DwSqliteFixture.DdlTableName, DwSqliteFixture.UpdateTableName);
        Assert.Equal(DwSqliteFixture.KeyColumnName, DwSqliteFixture.IdentityColumnName);
    }

    [Fact]
    public void AnUnknownDescribeAnswersTheOraclesInvalidPropertyMarker()
    {
        DwSqliteTargetMetadata metadata = new();

        // PowerBuilder answers "!" for a property it cannot read, and the production readers are
        // built for it: the numeric coercion turns it into zero, which the key-column arm rejects as
        // non-positive [n_cst_thread_task_sqlupdate.sru:L119-L122].
        Assert.Equal(DwSqliteFixture.InvalidPropertyAnswer, metadata.DescribeColumnIdentity("#7.Identity"));
        Assert.Equal(DwSqliteFixture.InvalidPropertyAnswer, metadata.DescribeColumnDbName("#7.DBName"));
        Assert.Equal(0, metadata.GetColumnId("nosuchcolumn" + UpdateWhereBuilder.ColumnIdSuffix));

        // A property the fixture DOES carry answers from the transcription.
        Assert.Equal(
            DwSqliteFixture.IdColumnNumber,
            metadata.GetColumnId(DwSqliteFixture.KeyColumnName + UpdateWhereBuilder.ColumnIdSuffix));
        Assert.Equal(
            UpdateWhereBuilder.YesLiteral,
            metadata.DescribeColumnIdentity(
                IdentityColumnResolver.DescribeIdentityProperty(DwSqliteFixture.IdColumnNumber)));
        Assert.Equal(
            "id",
            metadata.DescribeColumnDbName(
                IdentityColumnResolver.DescribeDbNameProperty(DwSqliteFixture.IdColumnNumber)));
    }

    #endregion
}

#endregion

