// ==============================================================================================
//  CompanyEntity - the managed entity for COMPANY, the ONLY table with DDL evidence anywhere in
//  the 544-object legacy repository
//  --------------------------------------------------------------------------------------------
//  PORTED FROM    ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L463-L469  the ENTIRE DDL
//                 ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L381-L400  the seeded literals
//                 ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L8-L14, L26    the DataWindow's view
//                                                                          of the same six columns
//  ORACLE STATUS  Both files are READ ONLY. They are the behavioural oracle for parity testing,
//                 never an edit target, and between them they are the ONLY statement of intended
//                 behaviour for this schema - there is no ERD, no migration history and no other
//                 DDL to adjudicate a disagreement. Every type decision below therefore carries
//                 the :L locator it was taken from, and both files were read as specification
//                 only: nothing here copies, reformats, moves or edits either of them.
//
//  THE DDL, RECONSTRUCTED FROM THE CONCATENATED PowerScript LITERAL
//  --------------------------------------------------------------------------------------------
//  The legacy statement is a single sqlitedb.Exec(...) call inside the CONNECT button's clicked
//  event, assembled from seven string literals joined with the PowerScript continuation operator.
//  Knowing the whole of it matters, because it means every property in this file is either one of
//  these six columns or a defect - there is no third category:
//
//      w_test_sqlite.srw:L463    "CREATE TABLE IF NOT EXISTS COMPANY("
//      w_test_sqlite.srw:L464    "ID INTEGER PRIMARY KEY NOT NULL,"   + /*自增列*/&
//      w_test_sqlite.srw:L465    "NAME           TEXT    NOT NULL,"
//      w_test_sqlite.srw:L466    "AGE            INT     NOT NULL,"
//      w_test_sqlite.srw:L467    "ADDRESS        CHAR(50),"
//      w_test_sqlite.srw:L468    "SALARY         REAL,"
//      w_test_sqlite.srw:L469    "BIRTH          TEXT)"
//
//  The inline comment on L464 reads "auto-increment column", which is the legacy's own annotation
//  of ID and corroborates the identity marking in the DataWindow below. Nullability follows the
//  DDL exactly and is not softened anywhere in this file: ID, NAME and AGE are NOT NULL, while
//  ADDRESS, SALARY and BIRTH carry no NOT NULL and are therefore nullable.
//
//  THE DATAWINDOW'S VIEW OF THE SAME SIX COLUMNS
//  --------------------------------------------------------------------------------------------
//  dw_sqlite.srd is the only updatable DataWindow in the repository, which makes it the
//  golden-master fixture for the whole retrieve / validate / update triple. Its table block
//  declares the same six columns a SECOND time, with its own types:
//
//      dw_sqlite.srd:L8    id       type=number      key=yes identity=yes   dbname="id"
//      dw_sqlite.srd:L9    name     type=char(100)                          dbname="name"
//      dw_sqlite.srd:L10   age      type=number                             dbname="age"
//      dw_sqlite.srd:L11   address  type=char(200)                          dbname="address"
//      dw_sqlite.srd:L12   salary   type=decimal(2)                         dbname="salary"
//      dw_sqlite.srd:L13   birth    type=date                               dbname="birth"
//      dw_sqlite.srd:L14   retrieve="SELECT * FROM COMPANY" update="COMPANY"
//                          updatewhere=1 updatekeyinplace=no sort="age A salary A "
//      dw_sqlite.srd:L26   birth carries editmask.ddcalendar=yes editmask.mask="yyyy-mm-dd"
//
//  All six columns carry update=yes AND updatewhereclause=yes [dw_sqlite.srd:L8-L13].
//
//  THE ONLY EVIDENCED SCHEMA - NO SECOND ENTITY MAY BE ADDED HERE
//  --------------------------------------------------------------------------------------------
//  COMPANY is the only table with DDL evidence in the entire repository. All twelve DataWindow
//  definitions were scanned and exactly one, dw_sqlite.srd, carries table-level update settings;
//  the single CREATE TABLE quoted above is the only DDL of any kind. No other table, lookup,
//  join, audit or history entity may be added to this folder, because inventing one would
//  fabricate a database the oracle cannot adjudicate. That is the no-fabricated-database
//  constraint stated as a file rule rather than an aspiration.
//
//  SQLITE IS THE ONLY PROVISIONED ENGINE - AND THE LEGACY'S OWN ENUMERATION OMITS IT ENTIRELY
//  --------------------------------------------------------------------------------------------
//  The legacy carries two mutually independent storage paths, and conflating them is precisely
//  how a fabricated schema would enter:
//
//    Path 1, the SQLite binding. The only path with a real, evidenced connection. It opens a
//    database through the URI grammar test.db?mode=rwc[,password] [w_test_sqlite.srw:L456] with
//    documented check and journal extensions [:L452-L455], and it carries the DDL above.
//
//    Path 2, the transaction-object path. ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru
//    :L60-L61 declares EXACTLY TWO database types under its "//Database type" comment,
//    DBT_MSSQL = 0 and DBT_ORACLE = 1, resolved at :L356-L361 by a bare substring test,
//    Pos(Upper(DBMS),"ORACLE") > 0, that returns Oracle or else falls through to SQL Server.
//    SQLite is absent from that enumeration altogether.
//
//  Neither SQL Server nor Oracle has a schema, a connection string or a line of DDL anywhere in
//  the repository - only those two constants and their two statement generators. They therefore
//  survive as pure string transforms under Sql/Paging/, testable with no instance of either
//  engine running, and NOT as an entity, a provider or a second DbContext. SQLite is the only
//  engine provisioned, and this is the only entity it is provisioned for.
//
//  THE OPTIMISTIC-CONCURRENCY CONTRACT THIS ENTITY PARTICIPATES IN
//  --------------------------------------------------------------------------------------------
//  updatewhere=1 [dw_sqlite.srd:L14] is the "key and updateable columns" concurrency mode: the
//  generated UPDATE carries the key column PLUS the ORIGINAL values of every updateable column in
//  its WHERE clause. Combined with all six columns being marked updatewhereclause=yes, the check
//  spans ALL SIX columns' original values, which is what Concurrency/UpdateWhereBuilder.cs
//  constructs and what makes a mismatch detectable rather than a silent overwrite.
//
//  Two consequences reach beyond this file and are recorded so they are not rediscovered:
//
//    * Id is BOTH key and identity [dw_sqlite.srd:L8], the property
//      Concurrency/IdentityColumnResolver.cs resolves at runtime by prefix-matching the update
//      table name against each column's database name.
//    * updatekeyinplace=no [dw_sqlite.srd:L14] means a key change is performed as delete plus
//      insert rather than an in-place update. The fixture sets it, so that path is exercised
//      rather than rare.
//
//  This entity is deliberately NOT the carrier of any of that. It models a row of COMPANY and
//  nothing else: original-value tracking, item status and the six-column predicate live in
//  Buffers/ and Concurrency/, because the legacy result carrier derives from a datastore and so
//  IS a DataWindow complete with buffers, which a plain entity is not and must not pretend to be.
//
//  FOUR DECLARATION DIVERGENCES BETWEEN THE DDL AND THE DATAWINDOW - ALL FOUR PRESERVED
//  --------------------------------------------------------------------------------------------
//  Three of these are named as defects to preserve; the fourth was found by diffing the two
//  declarations and is recorded here so its absence from that list is not read as permission to
//  reconcile it. In every case THE DDL WINS, because the DDL is what creates the table, while the
//  DataWindow's declaration is what the six-column concurrency predicate is built from. The
//  divergence is observable, so reconciling either side would silently invalidate every
//  characterization recording taken against this fixture. Each is annotated again at its own
//  property, with both locators, immediately below:
//
//      column   DataWindow declares          DDL declares        this file       ruling
//      ------   --------------------------   -----------------   -------------   ------------
//      name     char(100)  [srd:L9]          TEXT NOT NULL       string          no MaxLength
//                                            [srw:L465]                          (4th, unnamed)
//      address  char(200)  [srd:L11]         CHAR(50)            string?         no MaxLength
//                                            [srw:L467]
//      salary   decimal(2) [srd:L12]         REAL                double?         not decimal?
//                                            [srw:L468]
//      birth    date       [srd:L13]         TEXT                string?         not DateOnly?
//                          [srd:L26 mask]    [srw:L469]
//
//  ZERO DEPENDENCIES, WHICH IS A REQUIREMENT AND NOT A HAPPY ACCIDENT
//  --------------------------------------------------------------------------------------------
//  This file declares NO using directive of any kind: not one onto another folder of this
//  project, not one onto a shared library, and not one onto a package. long, string and double
//  are language keywords, and nothing else is needed. It is consequently the foundational type of
//  this project - it compiles before Configuration/, Errors/, Sql/, Buffers/, Concurrency/,
//  Tasks/, Transactions/ and Grpc/ exist, and it can never participate in a dependency cycle
//  with any of them.
//
//  Every piece of EF Core mapping belongs to Data/PowerFrameworkDbContext.cs's OnModelCreating:
//  the table name, the uppercase DDL column spellings COMPANY / ID / NAME / AGE / ADDRESS /
//  SALARY / BIRTH, the key, the identity generation and the column types. Keeping all of it there
//  is exactly what keeps this file dependency-free, buildable first, and free of any framework
//  attribute.
//
//  CONSTRAINT SELF-AUDIT
//  --------------------------------------------------------------------------------------------
//  No user rules exist for this project. review_rules returns exactly one line, "No user rules
//  provided", and that was verified directly rather than assumed. Their absence is not licence to
//  lower the bar: the enterprise-standard baseline and the named non-rule constraints bind in
//  their place and are honoured exactly as rules would be.
//
//  C-B  No behaviour improvement. All four divergences above are reproduced verbatim and
//       annotated, never reconciled: double? rather than decimal?, string? rather than DateOnly?,
//       and no length constraint on either Name or Address. Nothing in this file trims, pads,
//       rounds, parses, validates or normalises a value.
//  C-C  The legacy tree is read only. w_test_sqlite.srw and dw_sqlite.srd were read as the
//       specification and left untouched; both are cited by :L locator throughout. dw_sqlite.srd
//       is additionally one of ten legacy objects that end with a trailing space and no final
//       newline, so resaving it would corrupt the oracle - it is never opened for write.
//  C-D  Nothing from a deferred capability area. No XML or JSON serialization attribute, no
//       display name, no format string, no edit mask and no UI hint. The editmask.mask value at
//       dw_sqlite.srd:L26 is cited as EVIDENCE for the Birth type ruling and is deliberately not
//       reproduced as behaviour, because presentation formatting belongs to a deferred service.
//  C-E  SQLite only, one evidenced schema, no fabricated database. Exactly ONE entity is declared
//       in this folder and exactly six properties in it.
//  C-F  Nothing credential-shaped. No connection string, password, file path, key, token or
//       secret literal appears here, and none is needed: the SQLite URI grammar is owned by
//       Data/SqliteConnectionFactory.cs and bound from configuration.
//  C-H  Cheaply assertable. The type is logic-free by construction, so there is no branch, no
//       guard and no computed member that could go uncovered.
//  C-K  Every decision documented at its point of reproduction: this block for the schema
//       evidence, the SQLite-only position and the concurrency contract, plus a per-property
//       annotation for each of the four divergences.
//
//  DELIBERATELY ABSENT, EACH FOR A STATED REASON
//  --------------------------------------------------------------------------------------------
//    * [Table], [Column], [Key], [DatabaseGenerated], [Required], [MaxLength] and [StringLength],
//      and therefore any System.ComponentModel.DataAnnotations using. Two reasons, both binding.
//      Mapping lives in OnModelCreating, so an attribute here would be a second, competing source
//      of truth over the same six columns. And a length attribute is exactly the reconciliation
//      C-B forbids - it would impose the DataWindow's char(100) and char(200) bounds on columns
//      the DDL declares as unbounded TEXT and as CHAR(50) respectively.
//    * A record declaration, and any Equals, GetHashCode or IEquatable implementation. Value
//      equality would make two rows carrying identical column values indistinguishable, while
//      identity here is the ID primary key; EF Core tracks by key, and overriding equality is a
//      documented way to confuse a change tracker. A class with reference identity is correct.
//    * A concurrency token, [Timestamp] or a RowVersion property. The concurrency mechanism is
//      already fully specified by updatewhere=1 over the six original values, and there is no
//      version column in the DDL. Adding one would invent a column the table does not have.
//    * Audit columns, a soft-delete flag, a tenant discriminator and any navigation property or
//      foreign key. None appears in the DDL, and COMPANY is the only table, so there is nothing
//      for a navigation property to navigate to.
//    * A computed or derived member of any kind - no full-name, no age-from-birth, no formatted
//      salary. Each would be new behaviour, and the birth case would additionally require the
//      date parsing this file exists to refuse.
//    * A constructor, a validating setter, a private set, an init accessor and any argument
//      guard. EF Core materialises through the property setters, and a guard would impose
//      validation the column never had.
//    * Any SCREAMING_SNAKE identifier, and indeed any constant at all. The repository-root
//      .editorconfig scopes its CA1707 and IDE1006 relaxations to ten individually named files -
//      inside this project only Sql/ClauseModifier.cs and Sql/Paging/IPagingRewriter.cs are among
//      them - and Data/ is deliberately outside every one. Under the inherited
//      TreatWarningsAsErrors an underscored identifier here would be a build ERROR once the
//      analysis mode is raised. The uppercase DDL spellings are preserved as string literals in
//      the sibling DbContext's configuration instead, where they belong.
//    * A per-file licence header. The two source objects carry only a $PBExportHeader$ line and
//      no licence block, so there is nothing to carry over; the assembly-level copyright is set
//      once in the repository-root Directory.Build.props and the full notice lives in LICENSE and
//      NOTICE.
// ==============================================================================================

namespace PowerFramework.Persistence.Data
{
    /// <summary>
    /// One row of the <c>COMPANY</c> table, the only table with DDL evidence anywhere in the
    /// legacy repository [w_test_sqlite.srw:L463-L469].
    /// </summary>
    /// <remarks>
    /// <para>
    /// The six properties below are the six DDL columns, declared in DDL order, with nullability
    /// taken from the DDL and from nowhere else: <see cref="Id"/>, <see cref="Name"/> and
    /// <see cref="Age"/> are NOT NULL, while <see cref="Address"/>, <see cref="Salary"/> and
    /// <see cref="Birth"/> are nullable.
    /// </para>
    /// <para>
    /// Four of those columns are declared a second time, and differently, by the golden-master
    /// DataWindow at <c>dw_sqlite.srd:L8-L14</c>. Every one of those divergences is a defect this
    /// port preserves rather than corrects: the DDL creates the table, the DataWindow's
    /// declaration is what the six-column optimistic-concurrency predicate is built from, and the
    /// difference between them is observable in stored and returned values. Each is annotated on
    /// the property that reproduces it.
    /// </para>
    /// <para>
    /// This type is a plain data carrier with no logic, no attribute and no dependency of any
    /// kind. All EF Core mapping - the table name, the uppercase column spellings, the key, the
    /// identity generation and the column types - is configured in
    /// <c>Data/PowerFrameworkDbContext.cs</c>'s <c>OnModelCreating</c>. Original-value tracking,
    /// item status and the concurrency predicate itself live in <c>Buffers/</c> and
    /// <c>Concurrency/</c>, because the legacy result carrier derives from a datastore and so is a
    /// DataWindow complete with buffers, which this entity neither is nor imitates.
    /// </para>
    /// </remarks>
    public class CompanyEntity
    {
        /// <summary>
        /// The <c>ID</c> column, declared <c>ID INTEGER PRIMARY KEY NOT NULL</c>
        /// [w_test_sqlite.srw:L464]. The DataWindow agrees that this column is special and adds
        /// what the DDL only implies: <c>key=yes identity=yes</c> [dw_sqlite.srd:L8].
        /// </summary>
        /// <remarks>
        /// <para>
        /// <c>long</c> rather than <c>int</c>, for two independent reasons that agree. SQLite's
        /// <c>INTEGER</c> is a 64-bit signed value, so a narrower CLR type would be the one place
        /// in this file capable of losing a value the engine can legitimately store. And the
        /// legacy declares its own row and column identifiers as PowerBuilder <c>long</c>, which
        /// this port maps to C# <c>long</c> throughout, so widening here keeps this property in
        /// step with every signature that carries it.
        /// </para>
        /// <para>
        /// Non-nullable, because the column is NOT NULL and is the primary key. The inline
        /// PowerScript comment on <c>w_test_sqlite.srw:L464</c> reads "auto-increment column",
        /// which is the legacy's own annotation of this column and independently corroborates the
        /// <c>identity=yes</c> marking. Identity generation is configured in the sibling
        /// <c>OnModelCreating</c>, not asserted here, and it is what
        /// <c>Concurrency/IdentityColumnResolver.cs</c> resolves at runtime rather than reading
        /// off a static definition.
        /// </para>
        /// </remarks>
        public long Id { get; set; }

        /// <summary>
        /// The <c>NAME</c> column, declared <c>NAME TEXT NOT NULL</c> [w_test_sqlite.srw:L465].
        /// </summary>
        /// <remarks>
        /// <para>
        /// FOURTH DIVERGENCE, PRESERVED. The DataWindow declares this same column as
        /// <c>char(100)</c> [dw_sqlite.srd:L9] over a column the DDL declares as unbounded
        /// <c>TEXT</c> [w_test_sqlite.srw:L465]. This one is not among the three divergences
        /// called out in the migration plan - it was found by diffing the two declarations
        /// column by column - and it is recorded here precisely so that its absence from that
        /// list is not mistaken for permission to reconcile it.
        /// </para>
        /// <para>
        /// The DDL wins, exactly as it does for the three named divergences: this is a plain
        /// <c>string</c> with NO <c>MaxLength</c>, NO <c>StringLength</c> and no length
        /// configuration of any kind. The DataWindow imposes its 100-character bound at its own
        /// layer, as an edit-buffer limit rather than a storage constraint, and that is where the
        /// bound stays. Adding it here would move a presentation-layer limit into the storage
        /// model and reject values the legacy stores without complaint.
        /// </para>
        /// <para>
        /// Initialised to <see cref="string.Empty"/>. That is the correct non-null representation
        /// of a NOT NULL text column and it is also load-bearing for the build: nullable reference
        /// types are enabled and warnings are errors repository-wide, so an uninitialised
        /// non-nullable reference property would emit CS8618 and fail the build. EF Core
        /// materialises through this setter, so the default is only ever observed on an
        /// instance the application constructed itself and has not yet populated.
        /// </para>
        /// </remarks>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// The <c>AGE</c> column, declared <c>AGE INT NOT NULL</c> [w_test_sqlite.srw:L466]. The
        /// DataWindow declares it as <c>type=number</c> [dw_sqlite.srd:L10], which is
        /// PowerBuilder's generic numeric and does not contradict the DDL.
        /// </summary>
        /// <remarks>
        /// <c>long</c>, non-nullable. SQLite applies INTEGER affinity to <c>INT</c> exactly as it
        /// does to <c>INTEGER</c> and stores both as a 64-bit signed value, so <c>long</c> is the
        /// faithful width here for the same reason it is on <see cref="Id"/>, and it keeps the two
        /// integer columns of this table consistent with each other and with the PowerBuilder
        /// <c>long</c> mapping this port uses throughout. No range check, no plausibility guard
        /// and no unsigned narrowing is applied: the column has no CHECK constraint, so imposing
        /// one here would be validation the legacy never performed.
        /// </remarks>
        public long Age { get; set; }

        /// <summary>
        /// The <c>ADDRESS</c> column, declared <c>ADDRESS CHAR(50)</c>
        /// [w_test_sqlite.srw:L467] - no NOT NULL, therefore nullable.
        /// </summary>
        /// <remarks>
        /// <para>
        /// FIRST NAMED DIVERGENCE, PRESERVED. The DataWindow declares this same column as
        /// <c>char(200)</c> [dw_sqlite.srd:L11] against the DDL's <c>CHAR(50)</c>
        /// [w_test_sqlite.srw:L467] - a four-fold disagreement, and the one that looks most like
        /// a bug. It is not a bug; it is one of the three divergences this port is required to
        /// preserve. The property is therefore <c>string?</c> with NO length constraint of any
        /// kind: no <c>MaxLength</c>, no <c>StringLength</c>, no <c>HasMaxLength</c> in the
        /// sibling configuration, and no trimming or padding anywhere.
        /// </para>
        /// <para>
        /// The nuance that makes the ruling safe, and that must not be misstated: SQLite DOES NOT
        /// ENFORCE <c>CHAR(n)</c> LENGTH. A declared type containing "CHAR" is assigned TEXT
        /// affinity and the parenthesised length is ignored entirely, so the engine neither
        /// truncates at 50 nor pads to 50. This is consequently a latent DECLARATION divergence
        /// between two descriptions of one column, and NOT a runtime truncation. A 60-character
        /// address round-trips intact through the legacy and must round-trip intact here.
        /// </para>
        /// <para>
        /// The oracle proves the no-padding, no-trimming half of that directly. One seeded literal
        /// is <c>'Rich-Mond '</c> [w_test_sqlite.srw:L388], WITH a trailing space inside the
        /// quotes, and it survives verbatim precisely because SQLite treats the column as plain
        /// text. That single space is characterization evidence: this property must never be
        /// trimmed, on read, on write, in a setter, or in a mapping.
        /// </para>
        /// </remarks>
        public string? Address { get; set; }

        /// <summary>
        /// The <c>SALARY</c> column, declared <c>SALARY REAL</c> [w_test_sqlite.srw:L468] - no
        /// NOT NULL, therefore nullable.
        /// </summary>
        /// <remarks>
        /// <para>
        /// SECOND NAMED DIVERGENCE, PRESERVED. The DataWindow declares this same column as
        /// <c>decimal(2)</c> [dw_sqlite.srd:L12], a fixed two-place decimal, against the DDL's
        /// <c>REAL</c> [w_test_sqlite.srw:L468], which carries REAL affinity and is stored as an
        /// IEEE-754 binary64 value. <c>double?</c> is the faithful mapping of the DDL.
        /// </para>
        /// <para>
        /// WHY <c>decimal?</c> IS REJECTED, and it is a rejection rather than a preference. The
        /// seeded literals include <c>15000.88</c> [w_test_sqlite.srw:L384] and <c>65000.16</c>
        /// [w_test_sqlite.srw:L388], and NEITHER is exactly representable in binary floating
        /// point. The legacy therefore stores and returns a value that differs from the decimal
        /// literal that was written, which makes the REAL-versus-<c>decimal(2)</c> divergence
        /// concretely observable in stored and returned values rather than merely cosmetic.
        /// Mapping to <c>decimal?</c> would silently correct that rounding - a behaviour
        /// improvement, and improvements are forbidden here. It would also break every stored
        /// characterization comparison taken against this fixture, since the recorded values are
        /// the binary ones.
        /// </para>
        /// <para>
        /// Independently corroborated by the legacy's own accessor for this fixture:
        /// <c>ws_objects/pfw.utility.sqlite.pbl.src/sqlitegetitemdouble.srf</c> declares both of
        /// its overloads as <c>global function double</c> [:L7-L8] and returns
        /// <c>GetItemNumber(...)</c> straight through [:L11, :L14]. The legacy reads this column as
        /// a double, so this port does too.
        /// </para>
        /// <para>
        /// No rounding, no scale enforcement and no currency formatting is applied. The
        /// <c>decimal(2)</c> scale belongs to the DataWindow layer, and the footer aggregate
        /// <c>sum(salary for page)</c> [dw_sqlite.srd:L27] is evaluated by the DataServices
        /// expression engine, not here.
        /// </para>
        /// </remarks>
        public double? Salary { get; set; }

        /// <summary>
        /// The <c>BIRTH</c> column, declared <c>BIRTH TEXT</c> [w_test_sqlite.srw:L469] - no NOT
        /// NULL, therefore nullable.
        /// </summary>
        /// <remarks>
        /// <para>
        /// THIRD NAMED DIVERGENCE, PRESERVED. The DataWindow declares this same column as
        /// <c>type=date</c> [dw_sqlite.srd:L13] and dresses it with a calendar drop-down and the
        /// mask <c>"yyyy-mm-dd"</c> [dw_sqlite.srd:L26], while the DDL declares plain <c>TEXT</c>
        /// [w_test_sqlite.srw:L469]. The property is <c>string?</c>, faithful to the DDL.
        /// </para>
        /// <para>
        /// WHY <c>DateOnly?</c> AND <c>DateTime?</c> ARE BOTH REJECTED. SQLite has no date type
        /// and performs NO date validation on a TEXT column, so the legacy stores and returns
        /// whatever string it is given, verbatim and in whatever format. A date-typed mapping
        /// would make the data-access layer THROW on a value the legacy returns successfully -
        /// an unparseable string, a differently ordered format, a stray blank - which imposes
        /// validation the column never had and converts a readable row into a hard failure. That
        /// is a behaviour change dressed up as type safety, and it is exactly what this port
        /// forbids.
        /// </para>
        /// <para>
        /// The observed seeded values happen to be well-formed ISO strings - <c>'1991-05-11'</c>,
        /// <c>'1980-05-11'</c>, <c>'1995-10-11'</c> and <c>'1988-07-28'</c>
        /// [w_test_sqlite.srw:L382-L388], plus <c>'1999-05-08'</c> from the parameterised
        /// insert loop [:L400] - but "the values we happened to see are parseable" is not a
        /// column constraint, and treating it as one is how a latent failure gets built in.
        /// </para>
        /// <para>
        /// The mask at <c>dw_sqlite.srd:L26</c> is cited above as EVIDENCE for this ruling and is
        /// deliberately not reproduced as behaviour: date formatting and parsing belong to the
        /// DataServices layer, and an edit mask is presentation, which is a deferred concern. This
        /// property therefore carries no format string, no culture, no parse attempt and no
        /// conversion.
        /// </para>
        /// </remarks>
        public string? Birth { get; set; }
    }
}
