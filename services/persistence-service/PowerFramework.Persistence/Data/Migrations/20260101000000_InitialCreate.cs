// ==================================================================================================
//  20260101000000_InitialCreate - THE ONE AND ONLY EF CORE MIGRATION IN THIS SYSTEM
//
//  It creates COMPANY, the single table for which DDL evidence exists anywhere in the 544-object
//  legacy estate. Nothing below is a design choice; every token is pinned by the oracle.
//
//  THE AUTHORITATIVE SOURCE - ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L463-L469, inside the
//  CONNECT button's clicked event, seven PowerScript literals joined with the continuation operator:
//
//      "CREATE TABLE IF NOT EXISTS COMPANY("
//      "ID INTEGER PRIMARY KEY NOT NULL,"   + /*自增列*/
//      "NAME           TEXT    NOT NULL,"
//      "AGE            INT     NOT NULL,"
//      "ADDRESS        CHAR(50),"
//      "SALARY         REAL,"
//      "BIRTH          TEXT)"
//
//  This is the ONLY DDL in the repository. Measured, not assumed: a case-insensitive search for
//  "CREATE TABLE" across ws_objects/** returns exactly two hits, both in this one file - the statement
//  at :L463 and a MessageBox title at :L470. "CREATE INDEX" and "CREATE UNIQUE" return ZERO hits
//  anywhere, which is the positive evidence behind the no-index rule below rather than an assumption
//  that indexes were merely overlooked.
//
//  THIS IS A GENERATED FILE AND IS TREATED AS ONE. Its body is verbatim what
//  `dotnet ef migrations add InitialCreate --output-dir Data/Migrations` emits for the model in
//  Data/PowerFrameworkDbContext.cs on EF Core 10.0.10 / SDK 10.0.302. Only comments were added, and
//  only the ones the constraints below require. Nothing is restructured, no helper is extracted and
//  no personal formatting is imposed, so a regeneration diffs cleanly against it. The generator's
//  UTF-8 BOM is the single byte-level departure: the repository-root .editorconfig sets
//  `charset = utf-8` for every non-legacy file, which is a BOM-stripping rule, and every other .cs
//  file in the tree is BOM-free.
//
//  NO HAND-WRITTEN LOGIC LIVES HERE - no helper method, no conditional, no loop, no parsing. That is
//  deliberate: generated migration code is conventionally excluded from coverage measurement, and
//  anything added here would be logic that the per-service 80% line gate then has to carry.
//
//  -----------------------------------------------------------------------------------------------
//  "CREATE TABLE IF NOT EXISTS" BECOMES A PLAIN CREATE TABLE, AND THAT IS CORRECT
//
//  The legacy guards re-execution with IF NOT EXISTS [:L463]. EF migrations obtain the same
//  idempotency from the __EFMigrationsHistory table, which records what has already been applied, so
//  the resulting schema is identical. This is the ONE intentional mechanical substitution in the
//  file. It is documented rather than worked around: an idiomatic CreateTable is right, and a
//  hand-rolled Sql("CREATE TABLE IF NOT EXISTS ...") chasing a literal token match would not be.
//
//  The named PK_COMPANY constraint and the quoted identifiers are likewise idiomatic generator
//  output. They leave the schema semantics untouched - "ID" INTEGER ... PRIMARY KEY is still the
//  rowid alias the legacy relies on - and are not edited for cosmetic proximity to the DDL text.
//
//  -----------------------------------------------------------------------------------------------
//  AUTOINCREMENT IS ABSENT, AND THAT WAS VERIFIED BY MEASUREMENT RATHER THAN ASSERTED
//
//  The legacy writes plain "ID INTEGER PRIMARY KEY NOT NULL" with NO AUTOINCREMENT keyword [:L464],
//  relying on SQLite's implicit INTEGER PRIMARY KEY rowid aliasing. Searches for "AUTOINCREMENT" and
//  for "sqlite_sequence" across the legacy tree both return ZERO hits, so the legacy demonstrably
//  never used it. EF Core's SQLite provider emits it BY CONVENTION for a generated integer key, and
//  that is an observable divergence on two counts, not a keyword preference: it creates a
//  sqlite_sequence side table the legacy schema does not have, and it makes ids monotonic so a
//  deleted rowid is never reused.
//
//  Data/PowerFrameworkDbContext.cs suppresses the convention in model configuration. The three facts
//  that matter here were confirmed against the schema this migration actually produces, by scripting
//  it and applying it to a scratch database:
//      * the CreateTable call below carries no annotations argument at all, so no
//        Sqlite:Autoincrement annotation reaches the operation;
//      * the applied database contains COMPANY and __EFMigrationsHistory and NO sqlite_sequence
//        table, and the stored DDL read back from sqlite_master contains no AUTOINCREMENT; and
//      * rowid reuse behaves as the legacy does - inserting two rows, deleting id 2 and inserting
//        again yields id 2 a second time.
//  No correction is therefore needed in this migration, and none is applied.
//
//  One measured caveat about the mechanism, recorded because it is easy to misread as a fault in this
//  file: the suppression is expressed as property metadata, and EF's model-snapshot writer maps that
//  property back to ValueGeneratedOnAdd without preserving the strategy, so a snapshot round trip
//  reconstitutes the provider default. The operation emitted below is unaffected - it is produced
//  from the model, and the schema above is what it creates.
//
//  -----------------------------------------------------------------------------------------------
//  SQLITE ONLY, AND NO SQLCIPHER
//
//  The legacy transaction object enumerates exactly two database types, DBT_MSSQL = 0 and
//  DBT_ORACLE = 1 [ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:L60-L61], resolved at
//  :L356-L360; SQLite appears nowhere in that enumeration - a case-insensitive search for "sqlite"
//  across that entire file returns zero hits - and neither SQL Server nor Oracle has any schema,
//  connection string or DDL anywhere in the repository. Those two dialects survive only as pure
//  string transforms under Sql/Paging/, which need no instance of either. Hence no second migration,
//  no second provider and no dialect-specific annotation.
//
//  Encrypted SQLite is out of Phase-1 scope: the shipped cipher library is materially older than the
//  plain one, and its key-derivation and per-page-integrity options are not reachable through any
//  framework API, so an encrypted page format cannot be reproduced faithfully. There is accordingly
//  no cipher pragma here - and no PRAGMA of any kind. The legacy's journal and check URI extensions
//  [:L452-L455] belong to Data/SqliteConnectionFactory.cs, and the SetAutoCommit(true)/(false)
//  bracketing around the DDL [:L461, :L473] has no migration analogue because EF runs migrations in
//  its own transaction. No native extension is loaded either, though n_sqlite.sru:L20-L21 offers the
//  overloads. The native provider comes solely from the central SQLitePCLRaw.bundle_e_sqlite3 pin;
//  the sqlite3.dll and sqlite3.cipher.dll at the repository root are read-only legacy artifacts and
//  are never loaded.
//
//  -----------------------------------------------------------------------------------------------
//  ADDITIVE APPLICATION ONLY. THIS MIGRATION NEVER DESTROYS DATA.
//
//  Nothing here truncates, reseeds, drops outside the generated Down, or deletes a file, and no seed
//  data is declared. The reason is the characterization model: for a given workflow id the
//  legacy-side and target-side recordings must be captured against the SAME persistence-db volume
//  state, with the volume neither recreated nor reseeded between them, or the paired recordings are
//  not comparable. Repeatability is Golden-Master characterization's one hard prerequisite.
//
//  The concrete anti-pattern is in the oracle itself: w_test_sqlite.srw:L450 calls
//  FileDelete("test.db") before every connect, so the legacy harness destroys the database file on
//  each run. That is precisely the behaviour the .NET side must not adopt.
//
//  Down's DropTable is EF's normal rollback artifact and is left exactly as generated - not deleted,
//  not emptied, not extended. Nothing in the service invokes a rollback at startup; whether a
//  migration is applied at all is Program.cs's decision, and it must stay additive.
//
//  -----------------------------------------------------------------------------------------------
//  NOTHING IS ADDED THAT THE DDL DOES NOT HAVE
//
//  No index, no unique constraint, no check constraint, no default value, no foreign key, no
//  collation, no computed column, no column comment annotation and no seed row. In particular
//  dw_sqlite.srd:L14's sort="age A salary A " is a DataWindow-side sort order, not a database index,
//  and no index is inferred from it. No connection string, file name or credential appears here
//  either: the design-time factory in Data/PowerFrameworkDbContext.cs calls UseSqlite() with no
//  arguments precisely so the tooling needs no literal. The COMPANY and column spellings are
//  preserved as string literals and generator-emitted member names, so no C# constant is declared
//  and no naming-analyzer relaxation is required - no migration file is on the .editorconfig roster
//  that carries those relaxations.
// ==================================================================================================

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PowerFramework.Persistence.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // COLUMN ORDER IS A WIRE CONTRACT, NOT COSMETICS, AND IS THE MOST CONSEQUENTIAL FACT IN
            // THIS METHOD. dw_sqlite.srd:L14 retrieves with "SELECT * FROM COMPANY", and :L21-L26
            // bind DataWindow column ids 1 through 6 BY POSITION to id, name, age, address, salary
            // and birth. A table created in any other order silently binds every value to the wrong
            // DataWindow column - a defect no row-count assertion would catch. The order below is
            // ID, NAME, AGE, ADDRESS, SALARY, BIRTH, matching the DDL, and it was verified against
            // the applied schema by reading the columns back in ordinal order.
            migrationBuilder.CreateTable(
                name: "COMPANY",
                columns: table => new
                {
                    // :L464 "ID INTEGER PRIMARY KEY NOT NULL," + /*自增列*/ - the inline PowerScript
                    // comment reads "auto-increment column", and dw_sqlite.srd:L8 marks the column
                    // key=yes identity=yes. INTEGER, NOT NULL, and no AUTOINCREMENT: see the header.
                    ID = table.Column<long>(type: "INTEGER", nullable: false),

                    // :L465 "NAME           TEXT    NOT NULL," - FOURTH PRESERVED DIVERGENCE. The
                    // DataWindow declares char(100) [dw_sqlite.srd:L9]; the DDL writes unbounded
                    // TEXT and the DDL wins, so there is no 100-character bound here. The bound is
                    // an edit-buffer limit at the DataWindow's own layer and stays there.
                    NAME = table.Column<string>(type: "TEXT", nullable: false),

                    // :L466 "AGE            INT     NOT NULL," - the type token is INT, NOT INTEGER.
                    // SQLite's type affinity treats the two as equivalent; the emitted token still
                    // has to match what the oracle wrote.
                    AGE = table.Column<long>(type: "INT", nullable: false),

                    // :L467 "ADDRESS        CHAR(50)," - PRESERVED DIVERGENCE. The DataWindow
                    // declares char(200) [dw_sqlite.srd:L11]; the DDL writes CHAR(50) and the DDL
                    // wins. Not widened to 200, and no max length is imposed. Nullable, as the DDL
                    // has no NOT NULL on it.
                    ADDRESS = table.Column<string>(type: "CHAR(50)", nullable: true),

                    // :L468 "SALARY         REAL," - PRESERVED DIVERGENCE. The DataWindow declares
                    // decimal(2) [dw_sqlite.srd:L12]; the DDL writes REAL and the DDL wins, so the
                    // CLR type is double and NOT decimal, with no precision or scale facet.
                    SALARY = table.Column<double>(type: "REAL", nullable: true),

                    // :L469 "BIRTH          TEXT)" - PRESERVED DIVERGENCE. The DataWindow declares
                    // date with a yyyy-mm-dd edit mask [dw_sqlite.srd:L13, :L26]; the DDL writes
                    // TEXT and the DDL wins, so the CLR type is string and there is no value
                    // converter to DateOnly or DateTime.
                    BIRTH = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    // ID alone, matching "ID INTEGER PRIMARY KEY NOT NULL" [:L464] and the single
                    // key=yes marking at dw_sqlite.srd:L8. NOT NULL lands on exactly ID, NAME and
                    // AGE; ADDRESS, SALARY and BIRTH are nullable, which is the null profile the
                    // six-column optimistic-concurrency comparison in Concurrency/ is built against.
                    table.PrimaryKey("PK_COMPANY", x => x.ID);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // EF's normal rollback artifact, exactly as generated. See the additive-only note in the
            // header: this is never invoked automatically, and nothing is added to it.
            migrationBuilder.DropTable(
                name: "COMPANY");
        }
    }
}
