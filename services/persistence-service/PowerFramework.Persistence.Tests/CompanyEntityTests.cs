// ==============================================================================================
//  CompanyEntityTests - the characterization suite that pins
//  PowerFramework.Persistence.Data.CompanyEntity
//  --------------------------------------------------------------------------------------------
//  SYSTEM UNDER TEST  services/persistence-service/PowerFramework.Persistence/Data/CompanyEntity.cs
//  BEHAVIOURAL ORACLE ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L463-L469  the ENTIRE DDL
//                     ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L8-L14, L26    the DataWindow's
//                                                                              view of the same
//                                                                              six columns
//                     both READ ONLY per constraint C-C - read as specification, never edited
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
//  THE SECOND THING THIS SUITE PROTECTS: THAT THERE IS EXACTLY ONE ENTITY
//  --------------------------------------------------------------------------------------------
//  COMPANY is the only table with DDL evidence in the entire 544-object legacy repository. All
//  twelve DataWindow definitions were scanned and exactly one, dw_sqlite.srd, carries table-level
//  update settings; the single CREATE TABLE cited above is the only DDL of any kind. Constraint C-E
//  - no fabricated database - therefore means a SECOND entity type in this namespace is a scope
//  violation rather than a feature, and the entity-census case below is what notices one appearing.
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
//  C-F SELF-AUDIT: every value here is synthetic. No credential, key, token, password, connection
//  string or file path appears - which matters in this file specifically, because the legacy's own
//  connection URI sits four lines from the DDL at w_test_sqlite.srw:L456 and is deliberately not
//  reproduced anywhere in this project outside the connection factory's configuration binding.
//
//  RULES POSITION: review_rules returns exactly one line, "No user rules provided.", so no
//  user-specified rule governs this file and none is invented. The enterprise-standard baseline
//  applies instead: deterministic, no I/O, no clock, no shared mutable state, every test
//  independent. No performance property is asserted, because the repository publishes none.
// ==============================================================================================

using System.Globalization;
using System.Reflection;

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
    //  The four preserved divergences, each asserted as behaviour rather than as a type
    // ==========================================================================================

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
}
