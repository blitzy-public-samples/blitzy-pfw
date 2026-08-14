// ==============================================================================================
//  CompanyEntityTests - the characterization suite that pins the COMPANY schema: the entity, its
//  EF Core mapping, the DDL it emits, and the migration that carries it
//  --------------------------------------------------------------------------------------------
//  SYSTEM UNDER TEST  services/persistence-service/PowerFramework.Persistence/Data/CompanyEntity.cs
//                     services/persistence-service/PowerFramework.Persistence/Data/
//                         PowerFrameworkDbContext.cs   - OnModelCreating AND the design-time factory
//                     services/persistence-service/PowerFramework.Persistence/Data/Migrations/
//                         20260101000000_InitialCreate.cs and the model snapshot beside it
//  BEHAVIOURAL ORACLE ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L463-L469  the ENTIRE DDL
//                     ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L8-L14, L26    the DataWindow's
//                                                                              view of the same
//                                                                              six columns
//                     both READ ONLY per constraint C-C - read as specification, never edited.
//                     NEITHER IS RE-TRANSCRIBED HERE: every oracle value this file asserts against
//                     is read from DwSqliteFixture, which is this project's single transcription
//                     point, so a divergence between the two oracles is impossible to introduce by
//                     retyping one of them in this file.
//
//  WHY A PLAIN DATA CLASS NEEDS A SUITE AT ALL
//  --------------------------------------------------------------------------------------------
//  CompanyEntity has no branch, no guard and no computed member, so nothing here is testing logic.
//  What it IS testing is a set of four DELIBERATE DIVERGENCES from the DataWindow's declaration of
//  the same six columns, each of which looks exactly like a porting mistake and would be
//  "corrected" by any reviewer who had not read the two oracles side by side:
//
//      column   DataWindow declares       DDL declares       this file    the trap
//      ------   -----------------------   ----------------   ----------   ----------------------
//      name     char(100)  [srd:L9]       TEXT NOT NULL      string       no MaxLength
//      address  char(200)  [srd:L11]      CHAR(50)           string?      no MaxLength; the two
//                                         [srw:L467]                      declared widths DISAGREE
//      salary   decimal(2) [srd:L12]      REAL [srw:L468]    double?      not decimal?
//      birth    date       [srd:L13]      TEXT [srw:L469]    string?      not DateOnly?
//
//  In every case THE DDL WINS, because the DDL is what creates the table while the DataWindow's
//  declaration is what the six-column concurrency predicate is built from. The divergence is
//  OBSERVABLE - a decimal salary rounds where a REAL does not, and a parsed birth date normalises
//  where TEXT does not - so reconciling either side would silently invalidate every
//  characterization recording taken against this fixture. C-B forbids that reconciliation, and
//  these assertions are what make the prohibition enforceable rather than merely documented.
//
//  The four are asserted TWICE and deliberately so, because the two forms fail for different
//  reasons. The theory over DwSqliteFixture.TypeDivergenceMatrix walks the divergence set as DATA
//  and fails if the MAPPED COLUMN TYPE ever stops being the DDL's, or if a fifth divergence is
//  added to the fixture without a decision being recorded here. The hand-written cases below it
//  fail if the divergence stops being OBSERVABLE - if a value that a decimal would round starts
//  being rounded, or a birth string that no parser accepts starts being rejected. A type check
//  alone would pass against a property that had quietly acquired a rounding setter.
//
//  THE SECOND THING THIS SUITE PROTECTS: THAT THERE IS EXACTLY ONE ENTITY
//  --------------------------------------------------------------------------------------------
//  COMPANY is the only table with DDL evidence in the entire 544-object legacy repository. All
//  twelve DataWindow definitions were scanned and exactly one, dw_sqlite.srd, carries table-level
//  update settings; the single CREATE TABLE cited above is the only DDL of any kind. Constraint C-E
//  - no fabricated database - therefore means a SECOND entity type in this namespace is a scope
//  violation rather than a feature, and the entity-census case below is what notices one appearing.
//  The same census is applied to the MODEL, to the emitted schema and to the migration, because a
//  fabricated table can enter through any of the four and only one of them is a C# type.
//
//  THE THIRD THING: THE MAPPING IS PART OF THE SCHEMA CONTRACT, NOT AN IMPLEMENTATION DETAIL
//  --------------------------------------------------------------------------------------------
//  CompanyEntity's property names are .NET-cased while the DDL's column names are UPPERCASE, so the
//  mapping in OnModelCreating is the only thing making the two the same schema. Three of its
//  decisions would be "tidied" by a reader who had not read the DDL:
//
//    * AGE IS "INT" WHILE ID IS "INTEGER" [srw:L464 against srw:L466]. The DDL genuinely spells the
//      two differently, and SQLite treats them differently in exactly one place that matters: only
//      the exact type name INTEGER on a single-column INTEGER PRIMARY KEY makes the column an alias
//      for the rowid. Normalising AGE to INTEGER would be harmless; normalising ID to INT would
//      silently turn the primary key into an ordinary indexed column with its own storage. They are
//      asserted separately so neither can be normalised onto the other.
//    * THE COLUMN NAMES ARE UPPERCASE, matching the DDL rather than the CLR properties.
//    * CHAR(50) IS DECLARED THOUGH SQLITE ENFORCES NO WIDTH. The declared type is what the schema
//      text carries and what a reader of the database sees, so it is preserved verbatim - and it is
//      the DDL's 50 rather than the DataWindow's 200, which is divergence 1 above.
//
//  C-K - COLUMN ORDER IS OBSERVABLE, NOT COSMETIC, AND IS THEREFORE ASSERTED
//  --------------------------------------------------------------------------------------------
//  The fixture retrieves with "SELECT * FROM COMPANY" [dw_sqlite.srd:L14] and binds the result to
//  DataWindow column ids 1 through 6 [dw_sqlite.srd:L21-L26] BY POSITION, not by name. A star
//  projection returns columns in the table's declared order, so ANY reordering of the six columns
//  silently misaligns every retrieved row - name into age, address into salary - while every value
//  remains individually valid and every row count remains correct. That is a defect no row-count
//  assertion and no round-trip test would catch, which is why the order is pinned in three places
//  here: the entity's property declaration order, the emitted schema's column order as SQLite
//  itself reports it, and the migration operation's column order.
//
//  C-K - AUTOINCREMENT IS SUPPRESSED ON PURPOSE, AND THE SUPPRESSION IS PINNED
//  --------------------------------------------------------------------------------------------
//  Data/PowerFrameworkDbContext.cs marks Id ValueGeneratedOnAdd and then sets the SQLite value
//  generation strategy to None, which is what keeps the AUTOINCREMENT keyword out of the emitted
//  DDL. EF Core's default for an integer key would emit it, so this is a deliberate deviation from
//  the framework default and it must not be "restored":
//
//    * The legacy column is "ID INTEGER PRIMARY KEY NOT NULL" [srw:L464] with an auto-increment
//      COMMENT and no AUTOINCREMENT KEYWORD. In SQLite that exact spelling makes ID an ALIAS FOR
//      THE ROWID, and the rowid supplies the generated value - which is why the legacy's own four
//      batch inserts omit ID from their column list entirely [srw:L381-L388].
//    * Adding the keyword would change two observable things. It creates a sqlite_sequence side
//      table that the legacy schema does not have, and it makes generated values strictly
//      monotonic - so a value freed by deleting the highest row is NEVER reused, where rowid
//      aliasing DOES reuse it. Both are asserted below, the second by observing the reuse happen.
//
//  THE ONE PLACE A REAL PROVIDER IS EXERCISED, AND ITS RULES
//  --------------------------------------------------------------------------------------------
//  Three cases below emit the mapping's DDL into a live SQLite database, because the strongest
//  possible statement about a schema is the one SQLite makes after parsing it. Every one of them
//  uses a connection the TEST creates, holds and disposes, with Data Source=:memory: - so nothing
//  touches the configured data directory, nothing opens a file the service would open, and nothing
//  deletes anything. That is not fastidiousness: AAP 0.6.7's paired-capture rule requires the
//  persistence-db volume state to survive a legacy-side and a target-side recording untouched, so a
//  test that provisioned or reseeded a real database would invalidate the characterization model
//  rather than merely being untidy. The same rule is why the context is asserted to expose no
//  EnsureCreated, EnsureDeleted or Migrate of its own.
//
//  A NOTE ON WHAT THESE ASSERTIONS ARE EVIDENCE OF (finding DP-7)
//  --------------------------------------------------------------------------------------------
//  The DDL and the DataWindow declaration are both READABLE in the oracle exports, so every type
//  and nullability assertion here traces to a locator rather than being inferred from a closed
//  binary. What is NOT settled by the repository is how SQLite's dynamic typing renders a given
//  value through this entity end to end; that belongs to a paired legacy recording, which has not
//  been captured. This suite characterizes the .NET declaration, not a round trip through a
//  database.
//
//  C-F SELF-AUDIT: every value here is synthetic. No credential, key, token, password or file path
//  appears - which matters in this file specifically, because the legacy's own connection URI sits
//  four lines from the DDL at w_test_sqlite.srw:L456, carries an optional password parameter, and is
//  deliberately not reproduced anywhere in this project outside the connection factory's
//  configuration binding. The ONE connection string written below names an in-memory database and no
//  path, no credential and no option beyond the data source. The design-time factory is asserted to
//  produce an EMPTY connection string, which is the mechanical form of "no literal in source": a
//  factory that had one embedded would fail that case rather than merely being reviewed.
//
//  BINDING CONSTRAINTS AT THIS SITE
//    * DETERMINISTIC. No clock is read, no random value is drawn, no ambient culture is relied on,
//      and no case depends on another or on the order the runner picks. Two consecutive runs were
//      compared and produced identical outcomes for all forty-four cases.
//    * NO FILE OR NETWORK I/O. The three cases that use a real provider open an IN-MEMORY database
//      on a connection the test itself creates and disposes, so the whole of their storage lives and
//      dies inside the case. Nothing reads or writes a path, and no file is created or deleted.
//    * NO SHARED MUTABLE STATE. Each case builds its own harness, and each therefore gets its own
//      private database that no other case can observe - which is also what makes the suite safe to
//      run in parallel with the rest of this project.
//    * NULLABLE AND WARNINGS-AS-ERRORS ARE INHERITED, not restated, and the file builds clean under
//      both. No constant is DECLARED in the preserved SCREAMING_SNAKE style: the .editorconfig
//      naming suppressions for those spellings are scoped to production files and no test file is
//      among them, so such spellings may be REFERENCED here and never introduced.
//    * NO PERFORMANCE PROPERTY IS ASSERTED (AAP 0.8.5), because the repository publishes no target
//      to assert one against. There is no timing, no throughput claim, and - relatedly - no index,
//      whose only possible justification here would have been a performance one.
// ==============================================================================================

using System.Globalization;
using System.Reflection;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

using PowerFramework.Persistence.Data;
using Xunit;

namespace PowerFramework.Persistence.Tests;

/// <summary>
/// Characterization tests for the managed entity of COMPANY: its six-column shape, its nullability,
/// the four preserved DDL-versus-DataWindow divergences, and the absence of everything the
/// production header lists as deliberately not there.
/// </summary>
public sealed class CompanyEntityTests
{
    /// <summary>
    /// The six property names in the order the DDL declares the columns
    /// [<c>w_test_sqlite.srw:L463-L469</c>].
    /// </summary>
    private static readonly string[] DdlColumnOrder =
        ["Id", "Name", "Age", "Address", "Salary", "Birth"];

    /// <summary>
    /// The public instance properties in DECLARATION order, which is the order the compiler emits
    /// into metadata.
    /// </summary>
    /// <remarks>
    /// Ordered by metadata token rather than trusting the order
    /// <see cref="Type.GetProperties()"/> happens to return, because that order is not specified.
    /// </remarks>
    private static PropertyInfo[] DeclaredProperties() =>
        typeof(CompanyEntity)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .OrderBy(property => property.MetadataToken)
            .ToArray();

    // ==========================================================================================
    //  The mapping harness
    // ==========================================================================================

    /// <summary>
    /// The only connection string this file contains: an in-memory SQLite database, owned by
    /// whichever test opened it and gone the moment that test disposes it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// NO PATH, NO CREDENTIAL, NO OPTION (C-F). It names no file, so it cannot collide with the
    /// configured data directory, cannot open a database the service would open, and cannot be
    /// mistaken for the legacy's own URI - which sits four lines from the DDL at
    /// <c>w_test_sqlite.srw:L456</c>, carries an optional password parameter, and is deliberately
    /// reproduced nowhere in this project outside the connection factory's configuration binding.
    /// </para>
    /// <para>
    /// AN IN-MEMORY DATABASE IS PRIVATE TO ITS CONNECTION, which is what makes the cases below
    /// independent of one another and of every other suite in this project: two tests running
    /// concurrently cannot see each other's schema, and no state survives either of them. It is also
    /// why <see cref="MappingHarness"/> holds the connection OPEN for its whole lifetime - closing it
    /// destroys the database.
    /// </para>
    /// <para>
    /// AAP 0.6.7 - THIS IS THE REASON NOTHING HERE TOUCHES A REAL DATABASE. The paired-capture rule
    /// requires the <c>persistence-db</c> volume state to survive a legacy-side and a target-side
    /// recording for one workflow id UNTOUCHED, so a test that created, migrated, seeded or deleted a
    /// real database would invalidate the characterization comparison rather than merely being
    /// untidy. Nothing below deletes a file, and nothing below writes outside this connection.
    /// </para>
    /// </remarks>
    private const string InMemoryDataSource = "Data Source=:memory:";

    /// <summary>
    /// The provider name the SQLite provider registers under, used when materialising a migration
    /// so it is built for the same provider the service deploys with.
    /// </summary>
    private const string SqliteProviderName = "Microsoft.EntityFrameworkCore.Sqlite";

    /// <summary>
    /// A context bound to a private in-memory SQLite database, with the seams the mapping assertions
    /// need. Created and disposed by the test that uses it; owns both the connection and the context.
    /// </summary>
    /// <remarks>
    /// Constructed rather than resolved from a host, because the subject is
    /// <c>OnModelCreating</c> and the DDL it produces - not the service's composition. That keeps
    /// every case here independent of configuration, of the options binding and of the connection
    /// factory, all three of which are covered by their own suites.
    /// </remarks>
    private sealed class MappingHarness : IDisposable
    {
        internal MappingHarness()
        {
            Connection = new SqliteConnection(InMemoryDataSource);

            // An in-memory database exists only while its connection is open.
            Connection.Open();

            // The connection is supplied as an OBJECT rather than as a string, so EF Core does not
            // own it and will not dispose it out from under this harness.
            Context = new PowerFrameworkDbContext(
                new DbContextOptionsBuilder<PowerFrameworkDbContext>()
                    .UseSqlite(Connection)
                    .Options);
        }

        /// <summary>The test-owned connection. Open for the harness's whole lifetime.</summary>
        internal SqliteConnection Connection { get; }

        /// <summary>The context under test.</summary>
        internal PowerFrameworkDbContext Context { get; }

        /// <summary>
        /// The runtime model's view of <see cref="CompanyEntity"/>.
        /// </summary>
        internal IEntityType EntityType => Context.Model.FindEntityType(typeof(CompanyEntity))!;

        /// <summary>
        /// The DESIGN-TIME model's view of <see cref="CompanyEntity"/>.
        /// </summary>
        /// <remarks>
        /// Distinct from <see cref="EntityType"/> and not interchangeable with it. The runtime model
        /// is read-optimised and deliberately drops configuration only tooling needs, so asking it
        /// for seed data throws with a message naming this very accessor as the fix. Any assertion
        /// about what the model does NOT declare has to be made here, or it would be asserting the
        /// absence of something the runtime model never carried in the first place.
        /// </remarks>
        internal IEntityType DesignTimeEntityType =>
            Context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(CompanyEntity))!;

        /// <summary>
        /// Emits the mapping's own CREATE TABLE into the test-owned database.
        /// </summary>
        /// <remarks>
        /// The script comes from the MAPPING, through the same model differ the migration tooling
        /// uses, so what lands in the database is what <c>OnModelCreating</c> specifies - not
        /// hand-written DDL, and not <c>EnsureCreated</c>, which the production context is asserted
        /// never to call.
        /// </remarks>
        internal void EmitMappedSchema()
        {
            using SqliteCommand command = Connection.CreateCommand();
            command.CommandText = Context.Database.GenerateCreateScript();
            _ = command.ExecuteNonQuery();
        }

        /// <summary>Runs a scalar query against the test-owned database.</summary>
        /// <param name="sql">The statement to run. Always a literal from this file.</param>
        /// <returns>The scalar result, or <see langword="null"/>.</returns>
        internal object? Scalar(string sql)
        {
            using SqliteCommand command = Connection.CreateCommand();
            command.CommandText = sql;
            return command.ExecuteScalar();
        }

        /// <summary>
        /// Reads <c>pragma_table_info</c> for COMPANY: the schema as SQLITE ITSELF parsed it, in
        /// column-id order.
        /// </summary>
        /// <returns>
        /// One entry per column, ordered by <c>cid</c>: the declared name, the declared type, whether
        /// the column is NOT NULL, and its one-based position within the primary key (0 when it is
        /// not part of it).
        /// </returns>
        /// <remarks>
        /// THE MOST AUTHORITATIVE FORM AVAILABLE. A string comparison against generated DDL can pass
        /// on text that SQLite would parse differently; this reads back what the engine actually
        /// recorded, so the column ORDER it reports is the order a <c>SELECT *</c> projects - which
        /// is the property <c>dw_sqlite.srd:L14</c> depends on.
        /// </remarks>
        internal List<(string Name, string DeclaredType, bool NotNull, int KeyPosition)> TableInfo()
        {
            List<(string Name, string DeclaredType, bool NotNull, int KeyPosition)> columns = [];

            using SqliteCommand command = Connection.CreateCommand();
            command.CommandText =
                "SELECT name, type, [notnull], pk FROM pragma_table_info('COMPANY') ORDER BY cid";

            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                columns.Add((
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetInt32(2) != 0,
                    reader.GetInt32(3)));
            }

            return columns;
        }

        /// <summary>Disposes the context and then the connection that outlived it.</summary>
        public void Dispose()
        {
            Context.Dispose();
            Connection.Dispose();
        }
    }

    /// <summary>
    /// The six DDL columns as a theory source, paired with the entity property each maps to.
    /// </summary>
    /// <remarks>
    /// BUILT FROM <see cref="DwSqliteFixture.DdlColumns"/> AND NOT RETYPED (C-C). The oracle facts -
    /// the column name, its declared type, its NOT NULL marking and whether it is the primary key -
    /// come from the project's single transcription of
    /// <c>ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L463-L469</c>, so this file cannot disagree
    /// with the oracle without the fixture disagreeing first. The property name is the .NET side of
    /// the pairing and is the only value this member contributes itself: it is the DDL name in
    /// Pascal case, which is exactly the correspondence <c>OnModelCreating</c> declares one
    /// <c>HasColumnName</c> at a time.
    /// </remarks>
    public static TheoryData<string, string, bool, bool, string> MappedColumnMatrix
    {
        get
        {
            TheoryData<string, string, bool, bool, string> matrix = new();

            foreach (DwSqliteDdlColumn column in DwSqliteFixture.DdlColumns)
            {
                matrix.Add(
                    column.Name,
                    column.DeclaredType,
                    column.NotNull,
                    column.PrimaryKey,
                    column.Name[..1] + column.Name[1..].ToLowerInvariant());
            }

            return matrix;
        }
    }

    // ==========================================================================================
    //  Shape
    // ==========================================================================================

    /// <summary>
    /// The entity declares exactly six public read-write properties, in the DDL's own column order.
    /// </summary>
    /// <remarks>
    /// Six is the whole census: every property is one of the six columns and there is no seventh
    /// category. The ORDER is asserted as well as the set, because the DDL's order is the order the
    /// six-column optimistic-concurrency predicate is built in - <c>updatewhere=1</c> with all six
    /// columns marked <c>updatewhereclause=yes</c> [<c>dw_sqlite.srd:L8-L14</c>] - and a reordering
    /// would change the generated WHERE clause while leaving every value intact.
    /// </remarks>
    [Fact]
    public void TheEntity_DeclaresExactlyTheSixDdlColumnsInDdlOrder()
    {
        PropertyInfo[] properties = DeclaredProperties();

        Assert.Equal(DdlColumnOrder, properties.Select(property => property.Name).ToArray());
        Assert.All(properties, property => Assert.True(property.CanRead && property.CanWrite));
        Assert.All(properties, property => Assert.True(property.GetMethod!.IsPublic));
        Assert.All(properties, property => Assert.True(property.SetMethod!.IsPublic));
    }

    /// <summary>
    /// Each property carries the CLR type the DDL implies, including the two that deliberately do
    /// not match the DataWindow.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>Id</c> and <c>Age</c> are <see cref="long"/> against <c>INTEGER</c> and <c>INT</c>: SQLite
    /// stores an INTEGER as up to eight bytes, so the wider type is the faithful one and it keeps the
    /// identity round trip from truncating on a table that outgrows 32 bits.
    /// </para>
    /// <para>
    /// <c>Salary</c> is <see cref="double"/> and NOT <see cref="decimal"/>, because the DDL says
    /// <c>REAL</c> [<c>srw:L468</c>] even though the DataWindow says <c>decimal(2)</c>
    /// [<c>srd:L12</c>]. <c>Birth</c> is <see cref="string"/> and NOT
    /// <see cref="DateOnly"/>, because the DDL says <c>TEXT</c> [<c>srw:L469</c>] even though the
    /// DataWindow says <c>date</c> [<c>srd:L13</c>] and dresses it with a calendar and a
    /// <c>yyyy-mm-dd</c> edit mask [<c>srd:L26</c>]. Both are the preserved divergence, not an
    /// oversight: a decimal would round a value the column does not round, and a parsed date would
    /// normalise text the column stores verbatim.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryProperty_CarriesTheTypeTheDdlImpliesRatherThanTheDataWindowsType()
    {
        CompanyEntity entity = new();

        Assert.Equal(typeof(long), entity.GetType().GetProperty("Id")!.PropertyType);
        Assert.Equal(typeof(string), entity.GetType().GetProperty("Name")!.PropertyType);
        Assert.Equal(typeof(long), entity.GetType().GetProperty("Age")!.PropertyType);
        Assert.Equal(typeof(string), entity.GetType().GetProperty("Address")!.PropertyType);

        // The two preserved type divergences.
        Assert.Equal(typeof(double?), entity.GetType().GetProperty("Salary")!.PropertyType);
        Assert.NotEqual(typeof(decimal?), entity.GetType().GetProperty("Salary")!.PropertyType);
        Assert.Equal(typeof(string), entity.GetType().GetProperty("Birth")!.PropertyType);
        Assert.NotEqual(typeof(DateOnly?), entity.GetType().GetProperty("Birth")!.PropertyType);
    }

    /// <summary>
    /// Nullability follows the DDL's <c>NOT NULL</c> markings exactly and is not softened anywhere.
    /// </summary>
    /// <remarks>
    /// <c>ID</c>, <c>NAME</c> and <c>AGE</c> carry <c>NOT NULL</c> [<c>srw:L464-L466</c>], while
    /// <c>ADDRESS</c>, <c>SALARY</c> and <c>BIRTH</c> carry no such marking [<c>srw:L467-L469</c>]
    /// and are therefore nullable. Read through <see cref="NullabilityInfoContext"/> so that the
    /// reference-type annotations are checked and not merely the value-type ones - the three
    /// nullable columns split two-to-one across reference and value types, so a test that only looked
    /// at <see cref="Nullable{T}"/> would miss two of the three.
    /// </remarks>
    [Fact]
    public void Nullability_MirrorsTheDdlNotNullMarkingsColumnForColumn()
    {
        NullabilityInfoContext context = new();
        Dictionary<string, NullabilityState> writeState = DeclaredProperties()
            .ToDictionary(property => property.Name, property => context.Create(property).WriteState);

        Assert.Equal(NullabilityState.NotNull, writeState["Id"]);
        Assert.Equal(NullabilityState.NotNull, writeState["Name"]);
        Assert.Equal(NullabilityState.NotNull, writeState["Age"]);

        Assert.Equal(NullabilityState.Nullable, writeState["Address"]);
        Assert.Equal(NullabilityState.Nullable, writeState["Salary"]);
        Assert.Equal(NullabilityState.Nullable, writeState["Birth"]);
    }

    /// <summary>
    /// A fresh entity's defaults are the ones the production file states: an empty
    /// <see cref="CompanyEntity.Name"/> and nothing else set.
    /// </summary>
    /// <remarks>
    /// <c>Name</c> is initialised to <see cref="string.Empty"/> because the column is
    /// <c>TEXT NOT NULL</c>: an empty string is the correct non-null representation of "not yet
    /// assigned", and leaving it null would contradict the annotation on the very same property. The
    /// three nullable properties are left null, which is what their columns permit. Nothing is
    /// defaulted to a sentinel value.
    /// </remarks>
    [Fact]
    public void AFreshEntity_HasAnEmptyNameAndNoOtherValueSet()
    {
        CompanyEntity entity = new();

        Assert.Equal(0L, entity.Id);
        Assert.Equal(string.Empty, entity.Name);
        Assert.NotNull(entity.Name);
        Assert.Equal(0L, entity.Age);
        Assert.Null(entity.Address);
        Assert.Null(entity.Salary);
        Assert.Null(entity.Birth);
    }

    /// <summary>
    /// Every property round-trips the value assigned to it, unmodified.
    /// </summary>
    /// <remarks>
    /// The entity trims nothing, pads nothing, rounds nothing, parses nothing and normalises
    /// nothing - stated in the production header and asserted here with values chosen so that each
    /// of those transformations would be visible: surrounding whitespace on <c>Name</c>, a
    /// non-canonical date spelling on <c>Birth</c>, and a salary with more precision than the
    /// DataWindow's <c>decimal(2)</c> would keep.
    /// </remarks>
    [Fact]
    public void EveryProperty_RoundTripsItsValueWithoutTransformingIt()
    {
        CompanyEntity entity = new()
        {
            Id = 4_294_967_296L,
            Name = "  Ada  Lovelace  ",
            Age = -1L,
            Address = "  12 Mill Lane  ",
            Salary = 1234.56789d,
            Birth = "1815/12/10",
        };

        Assert.Equal(4_294_967_296L, entity.Id);
        Assert.Equal("  Ada  Lovelace  ", entity.Name);
        Assert.Equal(-1L, entity.Age);
        Assert.Equal("  12 Mill Lane  ", entity.Address);
        Assert.Equal(1234.56789d, entity.Salary);
        Assert.Equal("1815/12/10", entity.Birth);
    }

    // ==========================================================================================
    //  The four preserved divergences: first as the fixture's own data, then as behaviour
    // ==========================================================================================

    /// <summary>
    /// For every one of the four divergences, the MAPPED COLUMN TYPE is the DDL's and not the
    /// DataWindow's, and the two are genuinely different.
    /// </summary>
    /// <param name="columnName">The DataWindow column name the divergence is about.</param>
    /// <param name="dataWindowType">
    /// What <c>dw_sqlite.srd</c> declares. RECORDED, and asserted NOT to be what the mapping uses.
    /// </param>
    /// <param name="ddlType">
    /// What <c>w_test_sqlite.srw</c> declares. On <c>name</c> this carries the <c>NOT NULL</c>
    /// marking alongside the type, because the nullability is the second half of that particular
    /// disagreement.
    /// </param>
    /// <param name="dataWindowLocator">The read-only locator of the DataWindow declaration.</param>
    /// <param name="ddlLocator">The read-only locator of the DDL declaration.</param>
    /// <remarks>
    /// <para>
    /// C-B, C-C - WHAT THIS CASE IS FOR. The divergence set is DATA, transcribed once in
    /// <see cref="DwSqliteFixture.TypeDivergences"/> from the two read-only oracles, and iterated
    /// here rather than restated. Two failures are therefore possible and both are the ones worth
    /// having: the mapping stops agreeing with the DDL on one of the four columns, or a FIFTH
    /// divergence is transcribed into the fixture and reaches this file without anyone deciding what
    /// the mapping should do about it. A reader who reconciled a divergence "helpfully" would land
    /// here, with both locators printed.
    /// </para>
    /// <para>
    /// THE DDL IS AUTHORITATIVE BECAUSE THE DDL CREATES THE TABLE. The DataWindow's declaration is
    /// not thereby irrelevant - it is what the six-column optimistic-concurrency predicate is built
    /// from [<c>dw_sqlite.srd:L14</c>, <c>updatewhere=1</c>] - which is precisely why both statements
    /// stay live and the disagreement is a preserved defect rather than a resolved question.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(DwSqliteFixture.TypeDivergenceMatrix), MemberType = typeof(DwSqliteFixture))]
    public void EachPreservedDivergence_MapsToTheDdlTypeAndNotTheDataWindowType(
        string columnName,
        string dataWindowType,
        string ddlType,
        string dataWindowLocator,
        string ddlLocator)
    {
        // C-C: both sides of the divergence must cite the read-only oracle, never a copy of it.
        Assert.StartsWith("ws_objects/", dataWindowLocator, StringComparison.Ordinal);
        Assert.StartsWith("ws_objects/", ddlLocator, StringComparison.Ordinal);

        DwSqliteDdlColumn ddlColumn = DwSqliteFixture.DdlColumnOf(columnName);
        Assert.Contains(ddlColumn.DeclaredType, ddlType, StringComparison.Ordinal);

        using MappingHarness harness = new();
        IProperty mapped = harness.EntityType.FindProperty(PropertyNameOf(columnName))!;
        string mappedColumnType = mapped.GetColumnType()!;

        Assert.True(
            string.Equals(ddlColumn.DeclaredType, mappedColumnType, StringComparison.Ordinal),
            $"Column '{columnName}' is mapped as '{mappedColumnType}' but the DDL declares "
                + $"'{ddlColumn.DeclaredType}' at {ddlLocator}. The DDL creates the table, so the DDL "
                + "wins; the DataWindow's disagreeing declaration at "
                + $"{dataWindowLocator} is a preserved defect and not a mapping instruction.");

        Assert.False(
            string.Equals(dataWindowType, mappedColumnType, StringComparison.OrdinalIgnoreCase),
            $"Column '{columnName}' is mapped as the DataWindow's '{dataWindowType}' "
                + $"({dataWindowLocator}) rather than the DDL's '{ddlColumn.DeclaredType}' "
                + $"({ddlLocator}). Reconciling the mapping onto the DataWindow's declaration is the "
                + "silent correction C-B forbids.");

        // The two really do differ, which is what makes this a divergence rather than a note.
        Assert.NotEqual(dataWindowType, ddlType);

        // NULLABILITY FOLLOWS THE DDL TOO, which is the half of the `name` divergence that a type
        // comparison alone would miss: the DataWindow's char(100) says nothing about null, while the
        // DDL's TEXT NOT NULL does, and the mapping honours the DDL.
        Assert.Equal(!ddlColumn.NotNull, mapped.IsNullable);
    }

    /// <summary>
    /// The entity property that carries a given DataWindow or DDL column.
    /// </summary>
    /// <param name="columnName">A column name in either oracle's spelling; matched case-insensitively.</param>
    /// <returns>The <see cref="CompanyEntity"/> property name.</returns>
    /// <remarks>
    /// The correspondence is mechanical - the DDL's name in Pascal case - and is resolved against the
    /// entity's own declared properties rather than computed, so a property that was renamed would
    /// fail here instead of silently matching a computed string.
    /// </remarks>
    private static string PropertyNameOf(string columnName) =>
        DeclaredProperties()
            .Single(property =>
                string.Equals(property.Name, columnName, StringComparison.OrdinalIgnoreCase))
            .Name;

    /// <summary>
    /// <see cref="CompanyEntity.Salary"/> keeps precision a <c>decimal(2)</c> would have rounded
    /// away.
    /// </summary>
    /// <remarks>
    /// The DIVERGENCE MADE OBSERVABLE. The DataWindow declares two decimal places
    /// [<c>srd:L12</c>]; the DDL declares <c>REAL</c> [<c>srw:L468</c>]. A value with five decimal
    /// places therefore survives here and would not have survived a <c>decimal?</c> property that
    /// had been "corrected" to match the DataWindow. Also asserted: the value is NOT rounded to two
    /// places on the way in, which is the specific transformation the DataWindow's declaration would
    /// have implied.
    /// </remarks>
    [Fact]
    public void Salary_PreservesPrecisionBecauseTheDdlSaysRealRatherThanDecimalTwo()
    {
        CompanyEntity entity = new() { Salary = 1234.56789d };

        Assert.Equal(1234.56789d, entity.Salary);
        Assert.NotEqual(1234.57d, entity.Salary);

        // A REAL also admits the values a fixed-scale decimal cannot represent at all.
        entity.Salary = double.MaxValue;
        Assert.Equal(double.MaxValue, entity.Salary);
    }

    /// <summary>
    /// <see cref="CompanyEntity.Birth"/> stores whatever text it is given, including text no date
    /// parser would accept.
    /// </summary>
    /// <remarks>
    /// The DIVERGENCE MADE OBSERVABLE. The DataWindow declares <c>type=date</c> with a
    /// <c>yyyy-mm-dd</c> edit mask [<c>srd:L13</c>, <c>srd:L26</c>]; the DDL declares <c>TEXT</c>
    /// [<c>srw:L469</c>]. A <see cref="DateOnly"/> property would have rejected or normalised every
    /// value below. The edit mask is cited as EVIDENCE for the type ruling and is deliberately not
    /// reproduced as behaviour, because presentation formatting belongs to a deferred capability
    /// area (C-D).
    /// </remarks>
    [Theory]
    [InlineData("1815-12-10")]
    [InlineData("1815/12/10")]
    [InlineData("10 December 1815")]
    [InlineData("not a date at all")]
    [InlineData("")]
    [InlineData("   ")]
    public void Birth_StoresArbitraryTextBecauseTheDdlSaysTextRatherThanDate(string value)
    {
        CompanyEntity entity = new() { Birth = value };

        Assert.Equal(value, entity.Birth);
    }

    /// <summary>
    /// Neither <see cref="CompanyEntity.Name"/> nor <see cref="CompanyEntity.Address"/> imposes a
    /// length limit, and both accept text longer than the DataWindow's declared width.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The third and fourth divergences. <c>Name</c> is <c>char(100)</c> in the DataWindow
    /// [<c>srd:L9</c>] against unbounded <c>TEXT</c> in the DDL [<c>srw:L465</c>]; <c>Address</c> is
    /// <c>char(200)</c> in the DataWindow [<c>srd:L11</c>] against <c>CHAR(50)</c> in the DDL
    /// [<c>srw:L467</c>] - the only case where the two oracles declare two different FINITE widths,
    /// and the one the production header records because it is absent from the named-defect list.
    /// </para>
    /// <para>
    /// A <c>MaxLength</c> or <c>StringLength</c> attribute here would impose the DataWindow's bounds
    /// on columns the DDL declares otherwise, which is exactly the reconciliation C-B forbids - and
    /// it would also be a second, competing source of truth against the mapping in
    /// <c>OnModelCreating</c>.
    /// </para>
    /// </remarks>
    [Fact]
    public void NeitherTextColumn_ImposesTheDataWindowsDeclaredWidth()
    {
        string longName = new('n', 500);
        string longAddress = new('a', 500);

        CompanyEntity entity = new() { Name = longName, Address = longAddress };

        Assert.Equal(longName, entity.Name);
        Assert.Equal(longAddress, entity.Address);
        Assert.Equal(500, entity.Name.Length);
        Assert.Equal(500, entity.Address!.Length);
    }

    // ==========================================================================================
    //  What is deliberately absent
    // ==========================================================================================

    /// <summary>
    /// The entity carries NO mapping, validation or serialization attribute of any kind - only the
    /// nullability metadata the compiler emits.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two binding reasons, both in the production header. Mapping lives in
    /// <c>Data/PowerFrameworkDbContext.cs</c>'s <c>OnModelCreating</c>, so an attribute here would be
    /// a second source of truth over the same six columns. And a length attribute is specifically
    /// the reconciliation C-B forbids.
    /// </para>
    /// <para>
    /// Measured: the only attributes present are
    /// <c>System.Runtime.CompilerServices.NullableContextAttribute</c> and
    /// <c>System.Runtime.CompilerServices.NullableAttribute</c>, both compiler-emitted from
    /// <c>Nullable</c> being enabled repository-wide. The assertion filters that namespace out and
    /// requires the remainder to be empty, so it tolerates future compiler metadata while still
    /// failing on any attribute a human adds.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheEntity_CarriesNoMappingOrValidationAttribute()
    {
        static string[] AuthoredAttributes(IEnumerable<object> attributes) =>
            attributes
                .Select(attribute => attribute.GetType().FullName ?? attribute.GetType().Name)
                .Where(name => !name.StartsWith("System.Runtime.CompilerServices.", StringComparison.Ordinal))
                .ToArray();

        Assert.Empty(AuthoredAttributes(typeof(CompanyEntity).GetCustomAttributes(false)));

        foreach (PropertyInfo property in DeclaredProperties())
        {
            Assert.Empty(AuthoredAttributes(property.GetCustomAttributes(false)));
        }
    }

    /// <summary>
    /// The entity is a CLASS with reference identity, not a record, so two rows carrying identical
    /// column values remain distinguishable.
    /// </summary>
    /// <remarks>
    /// Value equality would make two rows with identical values indistinguishable, while identity
    /// here is the <c>ID</c> primary key [<c>dw_sqlite.srd:L8</c> <c>key=yes identity=yes</c>]. EF
    /// Core tracks by key, and overriding equality is a documented way to confuse a change tracker.
    /// The record-specific members are asserted absent by name because that is what distinguishes a
    /// record from a class at the metadata level.
    /// </remarks>
    [Fact]
    public void TheEntity_IsAClassWithReferenceIdentityRatherThanARecord()
    {
        Type type = typeof(CompanyEntity);

        Assert.True(type.IsClass);
        Assert.False(type.IsAbstract);
        Assert.Equal(typeof(object), type.BaseType);
        Assert.Empty(type.GetInterfaces());

        // The compiler-generated members that would exist on a record.
        Assert.Null(type.GetMethod("<Clone>$", BindingFlags.Public | BindingFlags.Instance));
        Assert.Null(type.GetMethod("PrintMembers", BindingFlags.NonPublic | BindingFlags.Instance));
        Assert.Null(type.GetMethod("op_Equality", BindingFlags.Public | BindingFlags.Static));

        // Neither Equals nor GetHashCode nor ToString is overridden.
        foreach (string name in new[] { "Equals", "GetHashCode", "ToString" })
        {
            Assert.DoesNotContain(
                name,
                type.GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.Instance)
                    .Select(method => method.Name));
        }

        CompanyEntity first = new() { Id = 1L, Name = "Ada", Age = 36L };
        CompanyEntity second = new() { Id = 1L, Name = "Ada", Age = 36L };

        Assert.NotSame(first, second);
        Assert.NotEqual<object>(first, second);
    }

    /// <summary>
    /// The entity declares nothing but the six property accessors: no computed member, no navigation
    /// property, no concurrency token and no constructor other than the parameterless one EF Core
    /// materialises through.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each absence has its own reason in the production header. A computed member - a full name, an
    /// age derived from birth, a formatted salary - would be new behaviour, and the birth case would
    /// additionally require the date parsing this file exists to refuse. A navigation property has
    /// nothing to navigate to, since COMPANY is the only table. A <c>RowVersion</c> or
    /// <c>[Timestamp]</c> would invent a column the DDL does not have, and it is unnecessary because
    /// the concurrency mechanism is already fully specified by <c>updatewhere=1</c> over the six
    /// original values [<c>dw_sqlite.srd:L14</c>].
    /// </para>
    /// <para>
    /// Twelve declared instance methods and six instance fields: one getter, one setter and one
    /// backing field per column, and nothing else.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheEntity_DeclaresNothingButTheSixAccessorsAndAParameterlessConstructor()
    {
        Type type = typeof(CompanyEntity);

        string[] declaredMethods = type
            .GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.Instance)
            .Select(method => method.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            DdlColumnOrder
                .SelectMany(column => new[] { "get_" + column, "set_" + column })
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray(),
            declaredMethods);

        Assert.Equal(
            DdlColumnOrder.Length,
            type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Length);

        ConstructorInfo[] constructors = type.GetConstructors();
        Assert.Single(constructors);
        Assert.Empty(constructors[0].GetParameters());
        Assert.True(constructors[0].IsPublic);

        // No init-only accessor: EF Core materialises through ordinary property setters.
        Assert.All(
            DeclaredProperties(),
            property => Assert.DoesNotContain(
                typeof(System.Runtime.CompilerServices.IsExternalInit),
                property.SetMethod!.ReturnParameter.GetRequiredCustomModifiers()));
    }

    /// <summary>
    /// The <c>Data</c> namespace holds exactly ONE entity, which is the whole of the evidenced
    /// schema.
    /// </summary>
    /// <remarks>
    /// C-E, no fabricated database, stated as an enforceable census rather than as an aspiration. Only
    /// one table has DDL evidence anywhere in the repository, so a second entity type appearing beside
    /// this one would be inventing a table the oracle cannot adjudicate. The other two evidenced
    /// database ENGINES - SQL Server as 0 and Oracle as 1
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:L60-L61</c>], with SQLite absent
    /// from that enumeration altogether - survive elsewhere as pure-string paging rewriters and
    /// deliberately not as an entity, a provider or a second context.
    /// </remarks>
    [Fact]
    public void TheDataNamespace_HoldsExactlyOneEntityBecauseOnlyOneTableHasDdlEvidence()
    {
        Type[] entities = typeof(CompanyEntity).Assembly
            .GetTypes()
            .Where(type => type.Namespace == "PowerFramework.Persistence.Data")
            .Where(type => type.IsClass && !type.IsAbstract && type.IsPublic)
            .ToArray();

        Assert.Equal([typeof(CompanyEntity)], entities);
    }

    /// <summary>
    /// The salary and identifier widths are exercised at the boundaries the columns actually admit,
    /// so a narrower CLR type could not pass this suite.
    /// </summary>
    /// <remarks>
    /// The identity column is both key and identity [<c>dw_sqlite.srd:L8</c>], and the identity round
    /// trip sends generated values back to the caller
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru:L215-L245</c>], so a
    /// 32-bit identifier would silently truncate on a table that outgrew it. Asserted with values on
    /// both sides of the 32-bit boundary, because a truncation defect is invisible below it.
    /// </remarks>
    [Theory]
    [InlineData(0L)]
    [InlineData(1L)]
    [InlineData(2_147_483_647L)]
    [InlineData(2_147_483_648L)]
    [InlineData(long.MaxValue)]
    public void TheIdentifierAndAge_HoldValuesBeyondThirtyTwoBits(long value)
    {
        CompanyEntity entity = new() { Id = value, Age = value };

        Assert.Equal(value, entity.Id);
        Assert.Equal(value, entity.Age);
        Assert.Equal(
            value.ToString(CultureInfo.InvariantCulture),
            entity.Id.ToString(CultureInfo.InvariantCulture));
    }

    // ==========================================================================================
    //  The EF Core mapping - the only thing that makes the entity and the DDL one schema
    // ==========================================================================================

    /// <summary>
    /// The mapping targets the one evidenced table, spelled as the DDL spells it, in no schema.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The table name is asserted against <see cref="DwSqliteFixture.DdlTableName"/> - the project's
    /// transcription of <c>w_test_sqlite.srw:L463</c> - rather than against a literal typed here, so a
    /// disagreement with the oracle cannot be introduced in this file (C-C). The DataWindow's own
    /// <c>update="COMPANY"</c> [<c>dw_sqlite.srd:L14</c>] independently agrees, and the fixture keeps
    /// the two as separate values precisely because they are two statements in two files that happen
    /// to coincide.
    /// </para>
    /// <para>
    /// NO SCHEMA IS SET, and that is correct rather than an omission: SQLite has no schema namespace,
    /// and naming one would emit DDL the engine cannot honour.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheMapping_TargetsTheOnlyEvidencedTableAndDeclaresNoSchema()
    {
        using MappingHarness harness = new();

        Assert.Equal(DwSqliteFixture.DdlTableName, harness.EntityType.GetTableName());
        Assert.Equal(DwSqliteFixture.UpdateTableName, harness.EntityType.GetTableName());
        Assert.Null(harness.EntityType.GetSchema());

        // C-E: one table in the model, because one table has DDL evidence.
        Assert.Single(harness.Context.Model.GetEntityTypes());
        Assert.Equal(typeof(CompanyEntity), Assert.Single(harness.Context.Model.GetEntityTypes()).ClrType);
    }

    /// <summary>
    /// Every DDL column is mapped by name, by declared type, by nullability, and - for the identifier
    /// alone - as the key.
    /// </summary>
    /// <param name="ddlColumnName">The DDL's own UPPERCASE column name.</param>
    /// <param name="ddlDeclaredType">The DDL's declared type, verbatim.</param>
    /// <param name="notNull">Whether the DDL marks the column <c>NOT NULL</c>.</param>
    /// <param name="primaryKey">Whether the DDL marks the column <c>PRIMARY KEY</c>.</param>
    /// <param name="propertyName">The <see cref="CompanyEntity"/> property carrying the column.</param>
    /// <remarks>
    /// <para>
    /// FOUR ASSERTIONS PER COLUMN, DRIVEN FROM THE FIXTURE'S TRANSCRIPTION OF THE DDL (C-C). The
    /// column NAME matters because the entity's properties are .NET-cased and the DDL's columns are
    /// upper case, so the mapping is the only thing making them the same schema. The declared TYPE
    /// matters because it is the text the schema carries. NULLABILITY matters because it is the
    /// constraint the database enforces. And the KEY matters because it is what the whole
    /// optimistic-concurrency contract is built around.
    /// </para>
    /// <para>
    /// AGE IS <c>INT</c> WHILE ID IS <c>INTEGER</c> and this case is what keeps them apart. The
    /// distinction is load bearing in exactly one place: only the exact type name <c>INTEGER</c> on a
    /// single-column integer primary key makes that column an alias for the rowid, which is where the
    /// generated identifier comes from. Normalising the two onto one spelling would either be
    /// harmless or would silently give the primary key its own storage, and nothing at the call site
    /// says which - so neither is permitted.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(MappedColumnMatrix))]
    public void EachDdlColumn_IsMappedByNameTypeNullabilityAndKey(
        string ddlColumnName,
        string ddlDeclaredType,
        bool notNull,
        bool primaryKey,
        string propertyName)
    {
        using MappingHarness harness = new();

        IProperty mapped = harness.EntityType.FindProperty(propertyName)!;
        Assert.NotNull(mapped);

        Assert.Equal(ddlColumnName, mapped.GetColumnName());
        Assert.Equal(ddlDeclaredType, mapped.GetColumnType());
        Assert.Equal(!notNull, mapped.IsNullable);

        bool isKeyColumn = harness.EntityType
            .FindPrimaryKey()!
            .Properties
            .Any(property => property.Name == propertyName);
        Assert.Equal(primaryKey, isKeyColumn);
    }

    /// <summary>
    /// The mapping declares exactly the six DDL columns and their exact type spellings, with
    /// <c>INT</c> and <c>INTEGER</c> left distinct.
    /// </summary>
    /// <remarks>
    /// The per-column theory above proves each column is mapped correctly; this proves the SET is
    /// closed - a seventh mapped column would be a fabricated schema (C-E) and would pass every
    /// per-column case. Compared as unordered sets, because the model's property enumeration is key
    /// first then alphabetical rather than declaration order; ORDER is asserted where it is actually
    /// observable, against the emitted schema, below.
    /// </remarks>
    [Fact]
    public void TheMapping_DeclaresExactlyTheSixDdlColumnsWithTheirExactTypeSpellings()
    {
        using MappingHarness harness = new();

        IProperty[] mapped = [.. harness.EntityType.GetProperties()];

        Assert.Equal(
            DwSqliteFixture.DdlColumns.Select(column => column.Name).OrderBy(name => name, StringComparer.Ordinal),
            mapped.Select(property => property.GetColumnName()).OrderBy(name => name, StringComparer.Ordinal));

        Assert.Equal(DwSqliteFixture.ColumnCount, mapped.Length);

        // The INT / INTEGER distinction the DDL makes, asserted as a difference and not merely as
        // two values: a normalisation would make these two equal.
        string idType = harness.EntityType.FindProperty(nameof(CompanyEntity.Id))!.GetColumnType()!;
        string ageType = harness.EntityType.FindProperty(nameof(CompanyEntity.Age))!.GetColumnType()!;

        Assert.Equal(DwSqliteFixture.DdlColumnOf("ID").DeclaredType, idType);
        Assert.Equal(DwSqliteFixture.DdlColumnOf("AGE").DeclaredType, ageType);
        Assert.NotEqual(idType, ageType);
    }

    /// <summary>
    /// The identifier is the whole of the key, and its value is generated by the database.
    /// </summary>
    /// <remarks>
    /// <c>ID</c> is the only column the DDL marks <c>PRIMARY KEY</c> [<c>srw:L464</c>] and the only
    /// one the DataWindow marks <c>key=yes identity=yes</c> [<c>dw_sqlite.srd:L8</c>], so a composite
    /// key or a second key would contradict both oracles at once. Database generation is what makes
    /// the identity round trip meaningful: the caller sends no identifier and reads back the one the
    /// database assigned [<c>n_cst_thread_task_sqlupdate.sru:L215-L245</c>], which is exactly why the
    /// legacy's own batch inserts omit ID from their column list [<c>srw:L381-L388</c>].
    /// </remarks>
    [Fact]
    public void TheKey_IsTheIdentifierAloneAndIsDatabaseGenerated()
    {
        using MappingHarness harness = new();

        IKey key = Assert.Single(harness.EntityType.GetKeys());
        Assert.Same(harness.EntityType.FindPrimaryKey(), key);

        IProperty keyProperty = Assert.Single(key.Properties);
        Assert.Equal(nameof(CompanyEntity.Id), keyProperty.Name);
        Assert.Equal(DwSqliteFixture.DdlColumnOf("ID").Name, keyProperty.GetColumnName());

        Assert.Equal(ValueGenerated.OnAdd, keyProperty.ValueGenerated);

        // And nothing else is generated: five columns whose values are the caller's alone.
        Assert.All(
            harness.EntityType.GetProperties().Where(property => property != keyProperty),
            property => Assert.Equal(ValueGenerated.Never, property.ValueGenerated));
    }

    /// <summary>
    /// The context exposes exactly one <see cref="DbSet{TEntity}"/>, and it is over
    /// <see cref="CompanyEntity"/>.
    /// </summary>
    /// <remarks>
    /// C-E as a census on the context rather than on the namespace. A second set would either name a
    /// second entity - a fabricated table - or name the same one twice, and both are worth failing on.
    /// </remarks>
    [Fact]
    public void TheContext_ExposesExactlyOneDbSetAndItIsOverTheOnlyEvidencedEntity()
    {
        PropertyInfo[] sets = [.. typeof(PowerFrameworkDbContext)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.PropertyType.IsGenericType
                && property.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>))];

        PropertyInfo set = Assert.Single(sets);
        Assert.Equal(typeof(DbSet<CompanyEntity>), set.PropertyType);
        Assert.Equal(nameof(PowerFrameworkDbContext.Companies), set.Name);

        using MappingHarness harness = new();
        Assert.NotNull(harness.Context.Companies);
    }

    /// <summary>
    /// The model adds no index, no relationship, no query filter, no value converter and no seed data
    /// on top of the six evidenced columns.
    /// </summary>
    /// <remarks>
    /// <para>
    /// EVERY ITEM IN THIS LIST IS AN INVENTED SCHEMA OBJECT IF IT APPEARS (C-E). An index would be a
    /// structure the DDL does not create - and would additionally be a performance claim, which AAP
    /// 0.8.5 forbids outright since the repository publishes no target to justify one against. A
    /// foreign key or navigation would require a second table. A query filter would silently remove
    /// rows from every retrieval, which would break parity against the legacy's own
    /// <c>SELECT * FROM COMPANY</c> [<c>dw_sqlite.srd:L14</c>] in a way no row-level assertion would
    /// localise. Seed data would put rows in the database that the paired characterization captures
    /// did not agree on (AAP 0.6.7).
    /// </para>
    /// <para>
    /// A VALUE CONVERTER IS SINGLED OUT because it is the specific tool a reader would reach for to
    /// "fix" two of the four preserved divergences - a decimal converter on <c>SALARY</c> or a date
    /// converter on <c>BIRTH</c> - and doing so is precisely the silent correction C-B forbids.
    /// </para>
    /// <para>
    /// SEED DATA AND QUERY FILTERS ARE READ FROM THE DESIGN-TIME MODEL, not the runtime one. The
    /// runtime model is read-optimised and throws when asked for seed data, naming the design-time
    /// accessor in its own message, so an assertion made against the runtime model would either
    /// throw or assert the absence of something that was never carried there.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheModel_AddsNoIndexRelationshipQueryFilterValueConverterOrSeedData()
    {
        using MappingHarness harness = new();

        Assert.Empty(harness.EntityType.GetIndexes());
        Assert.Empty(harness.EntityType.GetForeignKeys());
        Assert.Empty(harness.EntityType.GetReferencingForeignKeys());
        Assert.Empty(harness.EntityType.GetNavigations());
        Assert.Empty(harness.EntityType.GetSkipNavigations());

        Assert.All(
            harness.EntityType.GetProperties(),
            property => Assert.Null(property.GetValueConverter()));

        IEntityType designTime = harness.DesignTimeEntityType;
        Assert.Empty(designTime.GetDeclaredQueryFilters());
        Assert.Empty(designTime.GetSeedData());
    }

    // ==========================================================================================
    //  The schema the mapping emits, read back from SQLite itself
    // ==========================================================================================

    /// <summary>
    /// The emitted schema declares the six columns IN DDL ORDER, each with the DDL's own declared
    /// type and NOT NULL marking, and with the identifier as the sole key column.
    /// </summary>
    /// <remarks>
    /// <para>
    /// C-K - ORDER IS OBSERVABLE AND THIS IS WHERE IT IS PINNED. The fixture retrieves with
    /// <c>SELECT * FROM COMPANY</c> [<c>dw_sqlite.srd:L14</c>] and binds the result to DataWindow
    /// column ids 1 through 6 [<c>dw_sqlite.srd:L21-L26</c>] BY POSITION. A star projection returns
    /// columns in the table's declared order, so reordering the six silently feeds name into age and
    /// address into salary while every individual value stays valid and every row count stays
    /// correct. No round-trip test and no row-count assertion would catch that; this does.
    /// </para>
    /// <para>
    /// READ THROUGH <c>pragma_table_info</c> RATHER THAN BY MATCHING GENERATED TEXT, because the
    /// engine's own parse of the schema is the authority on what the schema means. The expected order
    /// is <see cref="DwSqliteFixture.DdlColumns"/>, which is the transcription of
    /// <c>w_test_sqlite.srw:L463-L469</c> in its declaration order, so this case compares the
    /// database against the oracle rather than against a literal typed here (C-C).
    /// </para>
    /// </remarks>
    [Fact]
    public void TheEmittedSchema_DeclaresTheSixColumnsInDdlOrderWithTheDdlsTypesAndNullability()
    {
        using MappingHarness harness = new();
        harness.EmitMappedSchema();

        List<(string Name, string DeclaredType, bool NotNull, int KeyPosition)> columns = harness.TableInfo();

        Assert.Equal(DwSqliteFixture.ColumnCount, columns.Count);

        // ORDER, TYPE AND NULLABILITY TOGETHER, position by position.
        Assert.Equal(
            DwSqliteFixture.DdlColumns.Select(column =>
                (column.Name, column.DeclaredType, column.NotNull)),
            columns.Select(column => (column.Name, column.DeclaredType, column.NotNull)));

        // The identifier is the first column AND the only key column, which is what makes the
        // single-column INTEGER primary key a rowid alias.
        Assert.Equal(DwSqliteFixture.DdlColumnOf("ID").Name, columns[0].Name);
        Assert.Equal(1, columns[0].KeyPosition);
        Assert.All(columns.Skip(1), column => Assert.Equal(0, column.KeyPosition));
    }

    /// <summary>
    /// The emitted schema carries no <c>AUTOINCREMENT</c> keyword, and applying it creates no
    /// <c>sqlite_sequence</c> table.
    /// </summary>
    /// <remarks>
    /// <para>
    /// C-K - WHY THE SUPPRESSION EXISTS AND MUST NOT BE "RESTORED". EF Core's default for an integer
    /// key on SQLite is to emit <c>AUTOINCREMENT</c>; <c>Data/PowerFrameworkDbContext.cs</c>
    /// deliberately sets the SQLite value generation strategy to <c>None</c> to stop it. The legacy
    /// column is <c>ID INTEGER PRIMARY KEY NOT NULL</c> [<c>srw:L464</c>] - an auto-increment COMMENT
    /// and no keyword - which in SQLite makes ID an alias for the rowid, and the rowid is what
    /// supplies the value the legacy's own inserts omit [<c>srw:L381-L388</c>].
    /// </para>
    /// <para>
    /// The keyword changes two observable things, so its absence is behaviour and not formatting. It
    /// creates a <c>sqlite_sequence</c> side table the legacy schema does not have, asserted here.
    /// And it makes generated values strictly monotonic, asserted in the case below by observing a
    /// freed value being reused - which under <c>AUTOINCREMENT</c> could not happen.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheEmittedSchema_CarriesNoAutoincrementKeywordAndCreatesNoSequenceTable()
    {
        using MappingHarness harness = new();

        // The generation strategy that suppresses it, and the DDL that results.
        Assert.Equal(
            SqliteValueGenerationStrategy.None,
            harness.EntityType.FindProperty(nameof(CompanyEntity.Id))!.GetValueGenerationStrategy());

        string script = harness.Context.Database.GenerateCreateScript();
        Assert.DoesNotContain("AUTOINCREMENT", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "\"ID\" INTEGER NOT NULL CONSTRAINT \"PK_COMPANY\" PRIMARY KEY",
            script,
            StringComparison.Ordinal);

        harness.EmitMappedSchema();

        // Read back the schema text SQLite stored, which is the parse rather than the input.
        string storedSchema = Assert.IsType<string>(
            harness.Scalar("SELECT sql FROM sqlite_master WHERE type = 'table' AND name = 'COMPANY'"));
        Assert.DoesNotContain("AUTOINCREMENT", storedSchema, StringComparison.OrdinalIgnoreCase);

        // No side table. AUTOINCREMENT would have created one named sqlite_sequence.
        Assert.Equal(
            0L,
            Assert.IsType<long>(harness.Scalar(
                "SELECT count(*) FROM sqlite_master WHERE name = 'sqlite_sequence'")));

        // And the emitted schema holds exactly one table, which is the whole of it (C-E).
        Assert.Equal(
            1L,
            Assert.IsType<long>(harness.Scalar("SELECT count(*) FROM sqlite_master WHERE type = 'table'")));
    }

    /// <summary>
    /// A generated identifier freed by deleting the highest row is REUSED by the next insert, which
    /// is rowid behaviour and is what <c>AUTOINCREMENT</c> would have prevented.
    /// </summary>
    /// <remarks>
    /// <para>
    /// C-K - THE SUPPRESSION ASSERTED AS BEHAVIOUR RATHER THAN AS ABSENT TEXT. Under a plain
    /// <c>INTEGER PRIMARY KEY</c> the column aliases the rowid, and SQLite's next rowid is one past
    /// the current maximum - so removing the maximum makes that value available again. Under
    /// <c>AUTOINCREMENT</c> the next value is drawn from <c>sqlite_sequence</c> instead and never
    /// goes backwards. This case fails the moment the keyword returns, including through some future
    /// EF Core default change that a text search of this file would not anticipate.
    /// </para>
    /// <para>
    /// NOT A CLAIM THAT REUSE IS DESIRABLE. It is the legacy's behaviour, so it is the behaviour that
    /// is preserved (C-B). The identifier the legacy hands back through the identity round trip
    /// [<c>n_cst_thread_task_sqlupdate.sru:L215-L245</c>] is whatever the rowid supplied, and a port
    /// that quietly made identifiers monotonic would diverge from the oracle on any workflow that
    /// deletes.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheIdentifier_ReusesAValueFreedByADeleteBecauseItAliasesTheRowid()
    {
        using MappingHarness harness = new();
        harness.EmitMappedSchema();

        CompanyEntity first = new() { Name = "first", Age = 1L };
        CompanyEntity second = new() { Name = "second", Age = 2L };
        harness.Context.Companies.AddRange(first, second);
        _ = harness.Context.SaveChanges();

        // The database assigned both, ascending from one.
        Assert.Equal(1L, first.Id);
        Assert.Equal(2L, second.Id);

        harness.Context.Companies.Remove(second);
        _ = harness.Context.SaveChanges();

        CompanyEntity third = new() { Name = "third", Age = 3L };
        _ = harness.Context.Companies.Add(third);
        _ = harness.Context.SaveChanges();

        // THE FREED VALUE COMES BACK. Under AUTOINCREMENT this would have been 3.
        Assert.Equal(second.Id, third.Id);
        Assert.Equal(2L, third.Id);
    }

    // ==========================================================================================
    //  The migration, the snapshot, and the design-time factory
    // ==========================================================================================

    /// <summary>
    /// The service carries exactly one migration, and it creates exactly the table the mapping
    /// describes - same name, same six columns in the same order, same types, same nullability, one
    /// key and no other constraint.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHY THE MIGRATION IS ASSERTED SEPARATELY FROM THE MAPPING. They are two artifacts that can
    /// disagree: the mapping is what the running service believes the schema is, while the migration
    /// is what actually gets applied to the database. A drift between them produces a service that
    /// queries columns the table does not have, and it is invisible until deployment. Because the
    /// migration is also the ONLY provisioning path this service has - the context is asserted below
    /// to call neither <c>EnsureCreated</c> nor <c>Migrate</c> itself - it is the single point where
    /// a fabricated table would enter the database (C-E).
    /// </para>
    /// <para>
    /// The operation is materialised for the SQLite provider, the one the service deploys with, so
    /// what is inspected is what that provider would emit.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheOnlyMigration_CreatesExactlyTheTableTheMappingDescribes()
    {
        using MappingHarness harness = new();

        IMigrationsAssembly migrations = harness.Context.GetService<IMigrationsAssembly>();
        KeyValuePair<string, TypeInfo> only = Assert.Single(migrations.Migrations);
        Assert.EndsWith("_InitialCreate", only.Key, StringComparison.Ordinal);

        Migration migration = migrations.CreateMigration(only.Value, SqliteProviderName);
        CreateTableOperation create =
            Assert.IsType<CreateTableOperation>(Assert.Single(migration.UpOperations));

        Assert.Equal(DwSqliteFixture.DdlTableName, create.Name);
        Assert.Null(create.Schema);

        // Name, declared type and nullability, in DDL order, against the oracle transcription.
        Assert.Equal(
            DwSqliteFixture.DdlColumns.Select(column =>
                (column.Name, column.DeclaredType, Nullable: !column.NotNull)),
            create.Columns.Select(column =>
                (column.Name, DeclaredType: column.ColumnType!, Nullable: column.IsNullable)));

        // AND AGAINST THE MAPPING ITSELF, COLUMN BY COLUMN, closing the loop the test name claims.
        // The two assertions above prove the migration matches the ORACLE and a sibling case proves
        // the mapping does; this proves the two match EACH OTHER directly, so neither can drift
        // through a change that happened to keep both consistent with a stale reading of the DDL.
        foreach (AddColumnOperation column in create.Columns)
        {
            IProperty mapped = harness.EntityType
                .GetProperties()
                .Single(property => string.Equals(
                    property.GetColumnName(),
                    column.Name,
                    StringComparison.Ordinal));

            Assert.Equal(mapped.GetColumnType(), column.ColumnType);
            Assert.Equal(mapped.IsNullable, column.IsNullable);

            // BOTH CLR TYPES ARE UNWRAPPED BEFORE COMPARISON, because the two artifacts spell
            // optionality differently and neither spelling is wrong: the model reports Salary as
            // double? while the migration reports it as double with a separate nullable flag. The
            // flag itself is compared on the line above, so unwrapping here compares the underlying
            // type without letting that difference in spelling read as a drift.
            Assert.Equal(
                Nullable.GetUnderlyingType(mapped.ClrType) ?? mapped.ClrType,
                Nullable.GetUnderlyingType(column.ClrType) ?? column.ClrType);
        }

        // NO AUTOINCREMENT ANNOTATION on the identifier column: the SQLite provider adds one when
        // the value generation strategy asks for it, and the mapping asks for None.
        AddColumnOperation id = create.Columns.Single(column =>
            string.Equals(column.Name, DwSqliteFixture.DdlColumnOf("ID").Name, StringComparison.Ordinal));
        Assert.Empty(id.GetAnnotations());

        // One key over the identifier, and nothing else - no unique constraint, no check constraint,
        // no foreign key, because there is no second table for one to point at (C-E).
        Assert.NotNull(create.PrimaryKey);
        Assert.Equal(DwSqliteFixture.DdlColumnOf("ID").Name, Assert.Single(create.PrimaryKey.Columns));
        Assert.Empty(create.ForeignKeys);
        Assert.Empty(create.UniqueConstraints);
        Assert.Empty(create.CheckConstraints);

        // The migration is reversible, and it drops only what it created.
        DropTableOperation drop =
            Assert.IsType<DropTableOperation>(Assert.Single(migration.DownOperations));
        Assert.Equal(DwSqliteFixture.DdlTableName, drop.Name);
    }

    /// <summary>
    /// The model snapshot agrees with <c>OnModelCreating</c>, so there is no pending model change.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE DRIFT THIS CATCHES IS THE COMMONEST EF CORE DEFECT THERE IS. The snapshot is a generated
    /// record of what the model looked like when the last migration was written; editing
    /// <c>OnModelCreating</c> without adding a migration leaves the two disagreeing, and the tooling
    /// only mentions it the next time someone runs it. Asserted with EF Core's OWN model differ - the
    /// same component <c>dotnet ef migrations add</c> uses to decide what to write - so this case
    /// says exactly what the tool would say, and an empty difference set means a freshly generated
    /// migration would be empty.
    /// </para>
    /// <para>
    /// The snapshot's model is finalised and then initialised for design time before comparison,
    /// because a snapshot is authored as a mutable model and the differ compares RELATIONAL models,
    /// which only exist once a model has been finalised.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheModelSnapshot_AgreesWithOnModelCreatingSoNoMigrationIsPending()
    {
        using MappingHarness harness = new();

        IMigrationsAssembly migrations = harness.Context.GetService<IMigrationsAssembly>();
        Assert.NotNull(migrations.ModelSnapshot);

        IModel snapshotModel = migrations.ModelSnapshot.Model;
        if (snapshotModel is IMutableModel mutable)
        {
            snapshotModel = mutable.FinalizeModel();
        }

        snapshotModel = harness.Context
            .GetService<IModelRuntimeInitializer>()
            .Initialize(snapshotModel, designTime: true, validationLogger: null);

        IReadOnlyList<MigrationOperation> differences = harness.Context
            .GetService<IMigrationsModelDiffer>()
            .GetDifferences(
                snapshotModel.GetRelationalModel(),
                harness.Context.GetService<IDesignTimeModel>().Model.GetRelationalModel());

        Assert.Empty(differences);
    }

    /// <summary>
    /// The design-time factory configures SQLite and NOTHING ELSE: no connection string, therefore no
    /// literal to leak.
    /// </summary>
    /// <remarks>
    /// <para>
    /// C-F AS A MECHANICAL CHECK RATHER THAN A REVIEW NOTE. <c>IDesignTimeDbContextFactory</c> exists
    /// so <c>dotnet ef</c> can build a context without starting the application, and the obvious way
    /// to write one is to hardcode a development connection string - which is how credentials and
    /// machine paths reach version control. The factory declared beside the context in
    /// <c>Data/PowerFrameworkDbContext.cs</c> calls <c>UseSqlite()</c> with no argument at all: the
    /// provider is enough for the tooling to generate a migration, and the resulting connection string
    /// is empty. A factory that acquired a literal would fail this case.
    /// </para>
    /// <para>
    /// Exactly one such factory is asserted, because a second would be a second answer to "how is
    /// this context built at design time" and the tooling picks without asking.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheDesignTimeFactory_ConfiguresSqliteWithNoConnectionStringLiteral()
    {
        Type[] factories = [.. typeof(PowerFrameworkDbContext).Assembly
            .GetTypes()
            .Where(type => !type.IsAbstract
                && typeof(IDesignTimeDbContextFactory<PowerFrameworkDbContext>).IsAssignableFrom(type))];

        Type factoryType = Assert.Single(factories);
        Assert.Equal(typeof(PowerFrameworkDbContext).Namespace, factoryType.Namespace);
        Assert.True(factoryType.IsSealed);
        Assert.Empty(Assert.Single(factoryType.GetConstructors()).GetParameters());

        IDesignTimeDbContextFactory<PowerFrameworkDbContext> factory =
            (IDesignTimeDbContextFactory<PowerFrameworkDbContext>)Activator.CreateInstance(factoryType)!;

        using PowerFrameworkDbContext context = factory.CreateDbContext([]);

        Assert.Equal(SqliteProviderName, context.Database.ProviderName);

        string connectionString = context.Database.GetDbConnection().ConnectionString;
        Assert.True(
            string.IsNullOrEmpty(connectionString),
            "The design-time factory produced the connection string "
                + $"'{connectionString}'. It must call UseSqlite() with no argument so that no "
                + "connection string, path or credential is embedded in source (C-F).");

        // The model is still fully buildable without one, which is the whole point: the tooling needs
        // the mapping, not a database.
        Assert.Equal(
            DwSqliteFixture.DdlTableName,
            context.Model.FindEntityType(typeof(CompanyEntity))!.GetTableName());
    }

    /// <summary>
    /// The context declares no schema-provisioning member of its own, and constructing it provisions
    /// nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// AAP 0.6.7 - THE SHARED-VOLUME RULE MAKES THIS A CORRECTNESS PROPERTY, NOT PEDANTRY. A legacy
    /// side and a target side recording for one workflow id must be captured against the SAME
    /// <c>persistence-db</c> volume state, with nothing recreating or reseeding it in between, or the
    /// paired recordings are not comparable at all. An <c>EnsureCreated</c>, <c>EnsureDeleted</c> or
    /// <c>Database.Migrate</c> call reachable from the context - and therefore from every code path
    /// that resolves one - could silently do exactly that. Provisioning belongs to the deployment
    /// step, which applies the migration asserted above.
    /// </para>
    /// <para>
    /// ASSERTED TWO WAYS, because either alone is weak. The member census shows the context declares
    /// nothing but its one set accessor and <c>OnModelCreating</c>, so there is no other method that
    /// could contain such a call. The behavioural half then shows that constructing the context and
    /// materialising its model against a live, EMPTY database leaves that database empty - which is
    /// what rules out a call inside the two members that do exist.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheContext_DeclaresNoProvisioningMemberAndProvisionsNothingWhenBuilt()
    {
        string[] declared = [.. typeof(PowerFrameworkDbContext)
            .GetMethods(BindingFlags.DeclaredOnly
                | BindingFlags.Public
                | BindingFlags.NonPublic
                | BindingFlags.Instance
                | BindingFlags.Static)
            .Select(method => method.Name)
            .OrderBy(name => name, StringComparer.Ordinal)];

        Assert.Equal<string>(["OnModelCreating", "get_Companies"], declared);

        ConstructorInfo constructor = Assert.Single(typeof(PowerFrameworkDbContext).GetConstructors());
        Assert.Equal(
            typeof(DbContextOptions<PowerFrameworkDbContext>),
            Assert.Single(constructor.GetParameters()).ParameterType);

        using MappingHarness harness = new();

        // Force OnModelCreating to run, which is the only authored code the construction path reaches.
        Assert.NotNull(harness.EntityType);

        // The database is still empty: nothing created a table, a history row or anything else.
        Assert.Equal(
            0L,
            Assert.IsType<long>(harness.Scalar("SELECT count(*) FROM sqlite_master")));
    }
}
