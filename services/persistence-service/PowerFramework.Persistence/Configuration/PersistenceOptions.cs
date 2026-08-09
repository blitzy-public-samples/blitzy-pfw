// ==================================================================================================
//  PersistenceOptions.cs - THE CONFIGURATION SURFACE OF THE POWERFRAMEWORK PERSISTENCE SERVICE
//  ------------------------------------------------------------------------------------------------
//  ROLE, AND WHY THIS FILE IS A SECURITY CONTROL RATHER THAN BOILERPLATE
//  The options pattern is the mechanism by which "no hardcoded secret is carried forward" becomes true
//  BY CONSTRUCTION rather than by review. Every value the legacy nailed into source arrives here
//  through configuration binding instead: the parts of the SQLite connection URI and the
//  transaction-pool keep-alive pair. One of those parts is a credential - the SQLite password - and the
//  shape of this file is what keeps it out of every settings file, every log record and every
//  characterization recording. A shortcut anywhere here re-opens the mandate it exists to close.
//
//  This is also the file every other file in this service reads its behaviour from. The connection
//  factory composes the URI from the Sqlite group, the transaction pool takes its idle lifetime from
//  the TransactionPool group, the query task takes its chunking and paging defaults from the Query
//  group, and inbound token validation takes its verification settings from the Jwt group.
//
//  THE BINDING SHAPE: FROM THE CONFIGURATION ROOT, NOT FROM A SERVICE-NAMED WRAPPER
//  PersistenceOptions binds from the ROOT, so each nested group binds from its like-named TOP-LEVEL
//  section - `Sqlite`, `TransactionPool`, `Query`, `Jwt`. There is deliberately no `Persistence`
//  wrapper section and no SectionName constant implying one, because the top-level shape is what makes
//  the environment-variable contract plain: `Sqlite__Password`, not `Persistence__Sqlite__Password`.
//  Every property name below equals its section or key name CHARACTER FOR CHARACTER, because a
//  mismatch does not error and does not warn - it binds silently to the default.
//
//  THE HOST OWNS THE LISTENER, AND THIS FILE DOES NOT RESTATE IT
//  The single endpoint on port 5101 with `Protocols: Http1AndHttp2` is declared in the standard
//  `Kestrel` section of appsettings.json and bound by the ASP.NET Core host itself. One port has to
//  carry HTTP/2 gRPC for the four published contracts AND HTTP/1.1 REST for the readiness probe and the
//  authentication proof at the same time, which is exactly what `Http1AndHttp2` states. Reading it from
//  configuration already satisfies "never hardcode a port", so no port or protocol property appears
//  here: two binders over one key is a silent-divergence risk, and a second port key would create the
//  settings-versus-options mismatch this file exists to avoid. That is why the number 5101 appears in
//  this documentation and in no executable line of this file.
//
//  LEGACY SOURCES - READ AS SPECIFICATION, NEVER EDITED (constraint C-C)
//  Every default below is the legacy value, and every one carries its ws_objects locator on the member
//  that reproduces it so a future reader can adjudicate it against the oracle rather than trusting a
//  comment. The eight files read are:
//    ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans_pool.sru        the keep-alive pair
//    ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru     the query defaults
//    ws_objects/pfw.utility.sqlite.pbl.src/n_sqlite.sru                   the two Open overloads
//    ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw                       the connection URI grammar
//    ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru             the two database types
//    ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru      the runtime-only settings
//    ws_objects/pfw.thread.ext.pbl.src/transactiondata.srs                the per-request descriptor
//    ws_objects/pfw.pbl.src/pfw.sra                                       the fail-fast posture
//
//  ============================ WHAT THIS FILE DELIBERATELY DOES NOT CONTAIN =======================
//  Each omission is a constraint discharged, and each is stated so a later reader sees a decision
//  rather than a gap.
//
//   * NO CREDENTIAL DEFAULT ANYWHERE (C-F). The one credential-shaped member, SqliteOptions.Password,
//     is nullable, has no initialiser, carries no [Required] attribute and is absent from every
//     settings file. Nothing in this file is logged and no type here overrides ToString().
//
//   * NO `record` OF ANY KIND (C-F). A record's synthesized ToString() prints every member, so a single
//     interpolated log line anywhere in the service would echo the SQLite password. Plain sealed
//     classes only, and no ToString() override.
//
//   * NO SQL SERVER OR ORACLE PROPERTY - not even nullable, not even unused (C-E). The legacy
//     transaction layer declares exactly two database types, DBT_MSSQL = 0 and DBT_ORACLE = 1
//     [n_cst_thread_trans.sru:L60-L61], and a case-insensitive search of that whole file for "sqlite"
//     returns zero matches. Neither engine has a schema, a connection string or any DDL anywhere in the
//     repository, so provisioning either would fabricate a database the legacy never had. Their
//     dialect-specific behaviour lives in Sql/Paging/ as pure string transforms that need no instance
//     of either.
//
//   * NO TRANSACTION DESCRIPTOR FIELD. transactiondata.srs declares nine fields - dbms, servername,
//     database, logid, logpass, dbparm, lock, autocommit, userparm [:L4-L12] - and every one of them
//     arrives PER REQUEST in the C-08 transaction descriptor, never as configuration. `logpass` is
//     write-only: never echoed in a response, never logged.
//
//   * NO DisableBind, NCharBind OR DBParm PROPERTY. Both flags are parsed at runtime out of the
//     per-request descriptor's DBParm string, with NCharBind taking effect only when DisableBind is 1
//     [n_cst_thread_task_sqlbase.sru:L127-L132]. Neither is a configuration key in the legacy and
//     neither becomes one here.
//
//   * NO TIMEOUT AND NO AUTO-COMMIT PROPERTY. SetCancelEvent, GetTimeout, SetTimeout(sec),
//     IsAutoCommit and SetAutoCommit are runtime API calls on the connection [n_sqlite.sru:L11-L15]
//     with no configuration key anywhere in the legacy. The Sqlite group below is exhaustive; adding
//     one would be an unrequested addition.
//
//   * NO SECTION, PROPERTY OR KEY FOR DesignSystem, Documents, Integration OR ScriptBridge, and no
//     signing, verification or mutual-TLS setting for one (C-D). Those four capability areas are out of
//     this phase entirely and are represented only as reserved routes on the Gateway.
//
//   * NO SIGNING KEY, IN ANY SPELLING (C-G). Exactly one signing secret exists in the whole system and
//     the Security service holds it as the sole issuer. This service holds VERIFICATION material only.
//
//   * NO CONFIGURATION-DRIVEN TYPE ACTIVATION (C-G). The legacy resolves two class-name settings into
//     live types with `Create Using` [n_cst_thread_task_sqlbase.sru:L198-L203]. Nothing here calls
//     Type.GetType or Activator.CreateInstance on a configured string; see
//     TransactionPoolOptions.TransactionClassName for the full ruling.
//
//   * NO CLOCK. No DateTime read, no TimeProvider member, no I/O of any kind - not in the types and not
//     in the validator. Program.cs registers the single injected TimeProvider seam that the transaction
//     pool's idle expiry shares; this file carries the expiry VALUE only. The legacy stamps its idle
//     start from the process CPU clock [n_cst_thread_trans_pool.sru:L97], which is the pool's concern
//     and one of the non-determinism sources seamed for characterization.
//
//   * NO SCREAMING_SNAKE IDENTIFIER IS DECLARED. This folder is outside every .editorconfig section
//     that relaxes the naming analyzers - those sections name eleven specific files that carry
//     preserved legacy constant spellings, and this is not one of them - and warnings are errors. The
//     legacy names `$SQL.TransPool.KeepAlive`, `$SQL.TransPool.KeepAliveExpireTime`,
//     `$SQL.TransPool.TransClass` and `KEEPALIVE_EXPIRE` are legacy CONFIGURATION KEY STRINGS and a
//     legacy CONSTANT NAME, not C# identifiers; each is bound to an ordinary PascalCase member and its
//     legacy spelling is recorded in that member's documentation.
//
//   * NO NEW PACKAGE REFERENCE (C-I). Everything used here - System.ComponentModel.DataAnnotations and
//     Microsoft.Extensions.Options - ships in the Microsoft.AspNetCore.App shared framework, so
//     Directory.Packages.props is untouched. In particular Microsoft.Extensions.Options.SourceGeneration
//     is NOT taken for its nested-member validation attribute; the explicit validator below is how that
//     gap is closed instead.
//
//   * NO REDACTION GROUP, AND ITS ABSENCE IS ENFORCED RATHER THAN INCIDENTAL. Statement redaction has
//     no configuration surface at all: Errors/SqlRedactor.cs accepts no enabled flag, exposes a shared
//     instance and applies masking unconditionally on the outward projection, and
//     shared/PowerFramework.Contracts.Tests/ServiceConfigurationCoherenceTests.cs pins this service's
//     authoritative option-leaf set at nineteen entries with no twentieth and asserts it against both
//     settings files. An earlier shape of the service did carry a `Redaction:Enabled` switch, and it was
//     removed precisely because it made a security property deployment-dependent - a stack brought up
//     with the switch off would put the complete generated statement, interpolated literals and all,
//     onto the network and into the log, which is the exposure the redactor exists to close. A group
//     here would therefore bind a key that no settings file declares and that no consumer reads, and
//     would invite an operator to switch off something that cannot be switched off. So it is absent on
//     purpose. DO NOT ADD IT.
//
//  ============================ BIDIRECTIONAL KEY PARITY, CHECKED KEY BY KEY ======================
//  appsettings.json is the declared source of truth for the key shape, and the two files agree
//  exactly. Nineteen option leaves, nineteen members:
//
//    Jwt:Authority                             -> JwtOptions.Authority
//    Jwt:Audience                              -> JwtOptions.Audience
//    Jwt:JwksPath                              -> JwtOptions.JwksPath
//    Jwt:RequireHttpsMetadata                  -> JwtOptions.RequireHttpsMetadata
//    Sqlite:DataDirectory                      -> SqliteOptions.DataDirectory
//    Sqlite:DatabaseFileName                   -> SqliteOptions.DatabaseFileName
//    Sqlite:Mode                               -> SqliteOptions.Mode
//    Sqlite:Check                              -> SqliteOptions.Check
//    Sqlite:Journal                            -> SqliteOptions.Journal
//    TransactionPool:KeepAlive                 -> TransactionPoolOptions.KeepAlive
//    TransactionPool:KeepAliveExpireSeconds    -> TransactionPoolOptions.KeepAliveExpireSeconds
//    TransactionPool:TransactionClassName      -> TransactionPoolOptions.TransactionClassName
//    Query:ChunkSize                           -> QueryOptions.ChunkSize
//    Query:MaxRows                             -> QueryOptions.MaxRows
//    Query:PageCounting                        -> QueryOptions.PageCounting
//    Query:Cache                               -> QueryOptions.Cache
//    Query:PageIndex                           -> QueryOptions.PageIndex
//    Query:PageSize                            -> QueryOptions.PageSize
//    Query:Paged                               -> QueryOptions.Paged
//
//  Three asymmetries, all deliberate and all documented on the member or type they concern:
//    1. Sqlite:Password       a member with NO key in either settings file. It carries a credential, so
//                             it is environment-only, and its absence is also what preserves the
//                             difference between "no password" and "an empty password" (C-F).
//    2. Logging, AllowedHosts and Kestrel   keys with no member here, because the host binds them
//                             natively.
//    3. Jwt:MetadataAddress and the four Jwt:Validate* toggles   read directly by Program.cs off the
//                             `Jwt` section with safe defaults, and absent from both settings files.
//                             They are not modelled for the same reason Kestrel is not: two binders over
//                             one key. A deployment can tighten them but never silently loosen them,
//                             because a missing key leaves the safe value in place.
//  appsettings.Development.json introduces NO member - it re-declares `Jwt:Authority` and
//  `Sqlite:DataDirectory`, both of which already exist here.
//
//  ============================ FAIL FAST, PRESERVED AS FAIL FAST ================================
//  Program.cs binds this type with validation on start, so a broken setting throws during startup and
//  the process never serves traffic. That is the point, and it is the framework's own posture: the
//  application object decodes a seven-field assert payload and then executes HALT CLOSE
//  [ws_objects/pfw.pbl.src/pfw.sra:L111-L144], and worker-session creation failure is fatal. Softening
//  a structural fault into warn-and-continue would be a behavioural change dressed up as robustness.
// ==================================================================================================

using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Microsoft.Extensions.Options;

namespace PowerFramework.Persistence.Configuration;

/// <summary>
/// The complete configuration surface of the Persistence service, bound from the configuration root.
/// </summary>
/// <remarks>
/// <para>
/// Four groups, each bound from its like-named TOP-LEVEL section: <c>Sqlite</c>,
/// <c>TransactionPool</c>, <c>Query</c> and <c>Jwt</c>. Root binding rather than a service-named
/// wrapper is what makes the environment-variable contract plain - <c>Sqlite__Password</c> rather than
/// <c>Persistence__Sqlite__Password</c> - so there is deliberately no <c>SectionName</c> constant on
/// this type and no wrapper section in any settings file.
/// </para>
/// <para>
/// Every group property is non-nullable and initialised to a fresh instance, so an omitted section
/// leaves that group's preserved legacy defaults in place rather than producing a null to dereference.
/// Every bound property is a plain <c>{ get; set; }</c> pair, because the configuration binder assigns
/// AFTER construction: an <c>init</c>-only or read-only property does not fail the build, it silently
/// keeps its default forever.
/// </para>
/// <para>
/// The listening endpoint is NOT here. Port 5101 and <c>Http1AndHttp2</c> are declared in the standard
/// <c>Kestrel</c> section and bound by the host, because one port must carry HTTP/2 gRPC and HTTP/1.1
/// REST simultaneously and the host is the component that can do that. Restating a port here would put
/// two binders over one key.
/// </para>
/// <para>
/// Validation is <see cref="PersistenceOptionsValidator"/>, which is required rather than optional:
/// <c>ValidateDataAnnotations</c> does not recurse into nested complex properties, so without an
/// explicit validator every annotation on the four groups below would be silently ignored and a
/// misconfigured service would start happily.
/// </para>
/// </remarks>
public sealed class PersistenceOptions
{
    /// <summary>
    /// The parts of the SQLite connection URI. Bound from the top-level <c>Sqlite</c> section.
    /// </summary>
    public SqliteOptions Sqlite { get; set; } = new();

    /// <summary>
    /// The pooled-transaction keep-alive settings. Bound from the top-level <c>TransactionPool</c>
    /// section.
    /// </summary>
    public TransactionPoolOptions TransactionPool { get; set; } = new();

    /// <summary>
    /// The preserved legacy retrieval defaults. Bound from the top-level <c>Query</c> section.
    /// </summary>
    public QueryOptions Query { get; set; } = new();

    /// <summary>
    /// Inbound bearer-token VERIFICATION settings. Bound from the top-level <c>Jwt</c> section.
    /// </summary>
    public JwtOptions Jwt { get; set; } = new();
}

// --------------------------------------------------------------------------------------------------
// GROUP 1 OF 4 - THE SQLITE CONNECTION, HELD AS ITS PARTS RATHER THAN AS A URI
// --------------------------------------------------------------------------------------------------

/// <summary>
/// The parts of the SQLite connection URI, held discretely. Bound from the top-level <c>Sqlite</c>
/// section.
/// </summary>
/// <remarks>
/// <para>
/// THE URI IS COMPOSED, NOT CONFIGURED, and that is why these are five parts plus a credential rather
/// than one string. <c>Data/SqliteConnectionFactory.cs</c> assembles
/// <c>&lt;DataDirectory&gt;/&lt;DatabaseFileName&gt;?mode=&lt;Mode&gt;[&amp;check[=quick]][&amp;journal=&lt;Journal&gt;]</c>
/// from them. There is deliberately no <c>Sqlite:Uri</c> setting: a pre-assembled string would be
/// unvalidatable, and once a password were appended to it the whole value would become a credential
/// that no error payload or log record could safely carry.
/// </para>
/// <para>
/// The grammar reproduced here comes from the one call site in the estate,
/// <c>ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L456</c>
/// - <c>Open("test.db?mode=rwc"/*[,password]*/)</c> - whose surrounding comments cite the SQLite URI
/// specification [<c>:L452</c>] and document the two framework extensions: the integrity check
/// [<c>:L454</c>] and the journal mode [<c>:L455</c>]. What is NOT reproduced is the
/// <c>FileDelete("test.db")</c> that precedes that open [<c>:L450</c>]: that is a test-harness action,
/// and repeating it on service start would destroy the very volume state the paired-capture parity rule
/// depends on.
/// </para>
/// <para>
/// SQLite IS THE ONLY ENGINE THIS GROUP DESCRIBES, and no other appears anywhere in this file. It is
/// the only storage engine with any evidence in the repository - one connection URI and one DDL
/// statement, both in that same file [<c>:L463-L469</c>].
/// </para>
/// </remarks>
public sealed class SqliteOptions
{
    /// <summary>
    /// The directory the database file lives in. Required, and defaults to
    /// <c>/var/lib/powerframework</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE ONE PART THAT IS DEPLOYMENT TOPOLOGY, which is why it is the only part the orchestration
    /// environment file templates. It is the mount point of the named <c>persistence-db</c> volume,
    /// attached to this service alone, and the value here must equal the writable data directory the
    /// service's <c>Dockerfile</c> declares. It must be a directory the image's NON-ROOT user can
    /// write to - never a root-owned location - because the container does not run as root.
    /// </para>
    /// <para>
    /// An absolute path rather than a relative one, so the database file survives container
    /// recreation. That is not a preference: the parity rule requires that for one workflow
    /// identifier the legacy-side and target-side recordings be taken against the SAME
    /// <c>persistence-db</c> volume state, with the volume neither recreated nor reseeded between
    /// them, or the paired recordings are not comparable at all.
    /// </para>
    /// <para>
    /// <c>appsettings.Development.json</c> overrides this to a temporary path outside the container
    /// mount for a loopback run, which is the one place the two topologies legitimately differ.
    /// </para>
    /// </remarks>
    [Required(
        AllowEmptyStrings = false,
        ErrorMessage =
            "must name the directory holding the SQLite database file. It is the mount point of the "
            + "persistence-db volume and must be writable by the image's non-root user. There is no "
            + "fallback: a service that cannot locate its only storage engine can serve no request.")]
    public string DataDirectory { get; set; } = "/var/lib/powerframework";

    /// <summary>
    /// The database file name. Required, and defaults to <c>test.db</c>.
    /// </summary>
    /// <remarks>
    /// The only database file name evidenced anywhere in the repository
    /// [<c>ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L456</c>]. It reads like a test artefact and
    /// it is retained anyway, because stored characterization comparisons resolve against it and a
    /// tidier name would invalidate every one of them.
    /// </remarks>
    [Required(
        AllowEmptyStrings = false,
        ErrorMessage =
            "must name the SQLite database file. The legacy name test.db is retained deliberately - "
            + "see ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L456 - so stored characterization "
            + "comparisons keep resolving.")]
    public string DatabaseFileName { get; set; } = "test.db";

    /// <summary>
    /// The URI <c>mode</c> parameter. Required, and defaults to <c>rwc</c> - read, write, create.
    /// </summary>
    /// <remarks>
    /// From <c>mode=rwc</c> in the one connection URI in the estate
    /// [<c>ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L456</c>]. Validated for presence only and
    /// deliberately NOT against a token set: no legacy evidence constrains the value beyond the one
    /// spelling it uses, so a token list here would invent a rule the oracle does not have.
    /// </remarks>
    [Required(
        AllowEmptyStrings = false,
        ErrorMessage =
            "must carry the SQLite URI mode parameter. The legacy value is rwc - read, write, create - "
            + "from ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L456.")]
    public string Mode { get; set; } = "rwc";

    /// <summary>
    /// The integrity check to run when the database is opened. THREE-STATE, and unset by default.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The framework extension documented at
    /// <c>ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L454</c> as <c>check[=quick]</c> has three
    /// genuinely distinct meanings, and the URI the connection factory composes must express whichever
    /// one is configured:
    /// </para>
    /// <list type="table">
    ///   <listheader>
    ///     <term>This setting</term>
    ///     <description>URI form, and behaviour on open</description>
    ///   </listheader>
    ///   <item>
    ///     <term><see langword="null"/> - the default</term>
    ///     <description>the parameter is OMITTED ENTIRELY, and no integrity check runs at all</description>
    ///   </item>
    ///   <item>
    ///     <term><see cref="SqliteIntegrityCheckMode.Full"/></term>
    ///     <description><c>check</c> present WITHOUT a value, which runs <c>PRAGMA integrity_check</c></description>
    ///   </item>
    ///   <item>
    ///     <term><see cref="SqliteIntegrityCheckMode.Quick"/></term>
    ///     <description><c>check=quick</c>, which runs <c>PRAGMA quick_check</c> instead</description>
    ///   </item>
    /// </list>
    /// <para>
    /// A BOOLEAN CANNOT EXPRESS THREE STATES, which is why this is a nullable enumeration and why the
    /// null must never be collapsed into "false" or into "empty". Collapsing it would fuse "run no
    /// check" with "run the full check" and silently change what happens on every open. The wider rule
    /// it follows is the migration's own: a null is never coerced to a zero.
    /// </para>
    /// <para>
    /// The configuration binder parses enumeration names case-insensitively, so <c>"quick"</c> in a
    /// settings file or <c>Sqlite__Check=quick</c> in the environment both bind to
    /// <see cref="SqliteIntegrityCheckMode.Quick"/>, and an empty environment value binds back to
    /// <see langword="null"/>.
    /// </para>
    /// </remarks>
    public SqliteIntegrityCheckMode? Check { get; set; }

    /// <summary>
    /// The URI <c>journal</c> parameter. Required, and defaults to <c>DELETE</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The second framework extension, documented at
    /// <c>ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L455</c> as
    /// <c>journal[=DELETE|TRUNCATE|PERSIST|MEMORY|WAL|OFF]</c> with the legacy default stated inline as
    /// <c>DELETE</c>. All six tokens are legal and are matched case-insensitively after trimming.
    /// </para>
    /// <para>
    /// <c>WAL</c> IS A LEGAL VALUE AND MUST NOT BECOME THE DEFAULT. A modern reader assumes write-ahead
    /// logging, and choosing it here because it is "better" would be exactly the silent behavioural
    /// improvement the migration forbids: journal mode changes crash-recovery semantics, file layout and
    /// reader/writer concurrency, all of which are observable. The legacy default is <c>DELETE</c>, so
    /// the default is <c>DELETE</c>. A deployment that wants write-ahead logging asks for it explicitly.
    /// </para>
    /// <para>
    /// Because the default is a legal token, the only way to fail validation is to supply a wrong one -
    /// which is the intended fail-fast. An explicitly EMPTY value fails too, deliberately: accepting it
    /// would compose a URI with a meaningless <c>journal=</c> parameter and mask a misconfiguration
    /// behind whatever the engine then chose.
    /// </para>
    /// </remarks>
    [Required(
        AllowEmptyStrings = false,
        ErrorMessage =
            "must be one of DELETE, TRUNCATE, PERSIST, MEMORY, WAL or OFF, matched case-insensitively "
            + "- see ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L455. The legacy default is DELETE, "
            + "and an empty value is refused rather than passed through as an empty URI parameter.")]
    public string Journal { get; set; } = "DELETE";

    /// <summary>
    /// The password for an encrypted database. Optional, has NO default, and is never logged.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PASSED AS THE SECOND ARGUMENT TO THE OPEN CALL, NOT AS A URI PARAMETER. The legacy binding
    /// declares two distinct overloads - <c>Open(readonly string uri)</c> and
    /// <c>Open(readonly string uri, readonly string password)</c>
    /// [<c>ws_objects/pfw.utility.sqlite.pbl.src/n_sqlite.sru:L17-L18</c>] - and the inline
    /// <c>/*[,password]*/</c> at <c>w_test_sqlite.srw:L456</c> sits in that second-argument position.
    /// The shorthand <c>mode=rwc[,password]</c> that appears in the migration plan is a rendering of
    /// that call shape and NOT a URI grammar. So this value MUST NEVER be appended to the composed URI,
    /// and must never be written into a connection string that is logged, returned in an error payload
    /// or captured into a characterization recording. If it is ever populated, the assembled connection
    /// string becomes a credential in its own right and is handled as one.
    /// </para>
    /// <para>
    /// ENVIRONMENT-ONLY, AND DELIBERATELY ABSENT FROM EVERY SETTINGS FILE. It is bound from
    /// <c>Sqlite__Password</c> and appears in neither <c>appsettings.json</c> nor
    /// <c>appsettings.Development.json</c>, which carry no secret of any kind. Adding
    /// <c>"Password": ""</c> to either would be a credential-shaped default AND would destroy the
    /// difference between "no password" and "an empty password" - which is why this property is
    /// nullable with no initialiser and carries no <c>[Required]</c> attribute. The asymmetry between
    /// this member and the settings files is therefore a decision, recorded here, and not an oversight
    /// to be tidied in either direction: the property is not dropped and the key is not added.
    /// </para>
    /// <para>
    /// Encrypted-database parity is out of scope for this phase in any case. The cipher-enabled library
    /// shipped in the repository is materially older than the plain one and its key-derivation and
    /// per-page integrity options are not reachable through any framework API, so there is no evidence
    /// of the settings an existing encrypted file was created with and the current provider cannot
    /// reproduce that page format. The setting exists so the surface is honest, not so it is exercised.
    /// </para>
    /// </remarks>
    public string? Password { get; set; }
}

/// <summary>
/// The integrity check the framework's <c>check</c> URI extension selects when it is present.
/// </summary>
/// <remarks>
/// Two members for the two forms the legacy grammar documents
/// [<c>ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L454</c>]. The third state - no check at all - is
/// the absence of a value, carried by <see cref="SqliteOptions.Check"/> being <see langword="null"/>,
/// which is why there is no <c>None</c> member here: a <c>None</c> member and a null would be two
/// spellings of one state, and the connection factory would have to treat them identically while a
/// reader could not tell which one a deployment meant.
/// </remarks>
public enum SqliteIntegrityCheckMode
{
    /// <summary>
    /// A bare <c>check</c> parameter, which runs <c>PRAGMA integrity_check</c>: the complete check.
    /// </summary>
    Full,

    /// <summary>
    /// <c>check=quick</c>, which runs <c>PRAGMA quick_check</c>: the cheaper check that omits the
    /// index-content verification.
    /// </summary>
    Quick,
}


// --------------------------------------------------------------------------------------------------
// GROUP 2 OF 4 - THE POOLED-TRANSACTION KEEP-ALIVE PAIR, INCLUDING ITS UNIT CONVERSION
// --------------------------------------------------------------------------------------------------

/// <summary>
/// The pooled-transaction keep-alive settings. Bound from the top-level <c>TransactionPool</c> section.
/// </summary>
/// <remarks>
/// <para>
/// Three settings, exactly three, ported from the legacy pool's initialisation event
/// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans_pool.sru:L76-L83</c>].
/// </para>
/// <para>
/// A FOURTH LEGACY KEY EXISTS AND IS DELIBERATELY NOT MODELLED, recorded here so a later reader knows
/// it was seen and decided rather than missed. <c>$SQL.TransPool.Class</c>
/// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru:L198</c>, used at
/// <c>:L199-L203</c>] names the POOL implementation class, which is a different thing from
/// <see cref="TransactionClassName"/>'s <c>$SQL.TransPool.TransClass</c>, which names the TRANSACTION
/// OBJECT class. It is not modelled because pool construction is a dependency-injection registration in
/// this port rather than a runtime class lookup, and because the settings file fixes this section at
/// three settings. Two further legacy names that look like configuration are not configuration at all:
/// <c>$SQL.TransPool</c> [<c>:L194-L195</c>, <c>:L205</c>] and <c>$SQL.DataStoreCache</c>
/// [<c>:L531-L536</c>] are runtime shared-state slots holding live objects.
/// </para>
/// </remarks>
public sealed class TransactionPoolOptions
{
    /// <summary>
    /// The fallback idle lifetime in MILLISECONDS - <c>30000</c>, i.e. thirty seconds - used whenever
    /// the configured value resolves to a non-positive number of milliseconds.
    /// </summary>
    /// <remarks>
    /// Reproduces the legacy constant declared as <c>constant long KEEPALIVE_EXPIRE = 30000 //ms</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans_pool.sru:L53</c>], whose unit is stated
    /// in the legacy's own trailing comment and whose value also initialises the pool's expiry field
    /// [<c>:L59</c>]. The legacy spelling is <c>KEEPALIVE_EXPIRE</c>; it is renamed to PascalCase here
    /// because this file is outside every <c>.editorconfig</c> section that relaxes the naming
    /// analyzers and warnings are errors - the value, which is the observable part, is unchanged.
    /// </remarks>
    public const int DefaultKeepAliveExpireMilliseconds = 30_000;

    /// <summary>
    /// Whether a pooled transaction object is kept alive once its reference count reaches zero.
    /// Defaults to <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The legacy key is <c>$SQL.TransPool.KeepAlive</c>, read as a boolean at
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans_pool.sru:L76</c>. False is the LEGACY
    /// default rather than a cautious choice of ours: with the key absent the boolean read yields
    /// false, so nothing is kept alive.
    /// </para>
    /// <para>
    /// THE IDLE COLLECTOR IS SUBSCRIBED INSIDE THE SAME <c>if</c> BLOCK [<c>:L80</c>], so when this is
    /// false the legacy does not merely skip collection - it never registers the handler at all.
    /// <c>Transactions/TransactionPool.cs</c> must reproduce that exactly and register no idle
    /// collection when this is false, because a collector that runs while nothing is retained would
    /// touch objects the legacy never revisits.
    /// </para>
    /// </remarks>
    public bool KeepAlive { get; set; }

    /// <summary>
    /// The idle lifetime of a kept-alive pooled transaction, in SECONDS. Defaults to <c>0</c>, which
    /// resolves to <see cref="DefaultKeepAliveExpireMilliseconds"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE UNIT IS SECONDS HERE AND MILLISECONDS INTERNALLY, AND THAT IS A PRESERVED LEGACY QUIRK. The
    /// legacy reads <c>$SQL.TransPool.KeepAliveExpireTime</c> as a double and MULTIPLIES IT BY 1000
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans_pool.sru:L78</c>], so the configured
    /// value is in seconds while the constant it falls back to and every internal comparison are in
    /// milliseconds. Both halves are preserved, and the conversion happens in exactly one place:
    /// <see cref="ResolveKeepAliveExpireMilliseconds"/>.
    /// </para>
    /// <para>
    /// THE <c>...Seconds</c> SUFFIX IS LOAD-BEARING. Feeding this property milliseconds is an easy
    /// mistake that produces a lifetime a thousand times too long, changes nothing observable at
    /// startup, and surfaces only as connections that never expire. The name is the only defence
    /// against it, so it is part of the contract rather than a stylistic choice.
    /// </para>
    /// <para>
    /// THE DEFAULT OF ZERO IS THE LEGACY'S OWN "KEY ABSENT" READ. A missing key yields zero from the
    /// legacy's double read, which then trips the non-positive fallback at <c>:L79</c> and produces
    /// exactly the 30000 ms the field was initialised to at <c>:L59</c>. So the fallback is the LIVE
    /// default path rather than dead code, and zero here and thirty in an environment file are the same
    /// effective value stated two ways.
    /// </para>
    /// </remarks>
    public double KeepAliveExpireSeconds { get; set; }

    /// <summary>
    /// The name of the transaction-object class the pool should use. Defaults to an empty string,
    /// meaning "the default implementation".
    /// </summary>
    /// <remarks>
    /// <para>
    /// The legacy key is <c>$SQL.TransPool.TransClass</c>, consulted at
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans_pool.sru:L83</c> and ONLY when the
    /// pool's own <c>OnGetTransClsName</c> event yields null or empty [<c>:L82</c>]. The legacy field
    /// carries no initialiser [<c>:L57</c>], so its default is the empty string, and that is the
    /// default here.
    /// </para>
    /// <para>
    /// SECURITY RULING - RESOLUTION IS REGISTRY-FIRST AND ANY TYPE-NAME FALLBACK IS CONFINED TO THIS
    /// SERVICE'S OWN ASSEMBLY. The legacy pattern for a class-name setting is <c>Create Using</c> on the
    /// configured string [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans_pool.sru:L166-L170</c>,
    /// and the same idiom at <c>n_cst_thread_task_sqlbase.sru:L199-L203</c>], and the
    /// empty-versus-non-empty branch at that site is observable, so it IS reproduced - by
    /// <c>PooledTransactionActivator</c> in <c>Transactions/TransactionPool.cs</c>, which documents the
    /// four constraints it enforces. Unconstrained, loading a type named by external configuration is a
    /// new attack surface: a writable settings source becomes arbitrary code execution inside the only
    /// process that holds a storage provider. The four constraints close that route - an
    /// assembly-qualified name is refused outright so no assembly can ever be located or loaded,
    /// resolution is performed against the declaring assembly alone, the type must implement the
    /// service-internal pooled-transaction contract and be a concrete non-generic class, and only three
    /// fixed constructor shapes are accepted.
    /// </para>
    /// <para>
    /// THE RECOMMENDED POSTURE IS STILL TO LEAVE THIS EMPTY. An empty value means "use the registered
    /// default", which is a dependency-injection registration in <c>Program.cs</c>; a deployment that
    /// needs an alternative should register a named factory, which the activator consults BEFORE any
    /// reflection and which therefore never reaches the type-name fallback at all. The property remains
    /// deliberately NOT validated against a type name, both because a registry name is not a type name
    /// and because validating it would imply it must always be loadable.
    /// </para>
    /// </remarks>
    public string TransactionClassName { get; set; } = string.Empty;

    /// <summary>
    /// Converts <see cref="KeepAliveExpireSeconds"/> into the idle lifetime in milliseconds that the
    /// pool compares against, applying the legacy non-positive fallback.
    /// </summary>
    /// <returns>
    /// The configured lifetime in milliseconds, or <see cref="DefaultKeepAliveExpireMilliseconds"/>
    /// when the conversion yields a value at or below zero.
    /// </returns>
    /// <remarks>
    /// <para>
    /// REPRODUCES <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans_pool.sru:L78-L79</c> IN
    /// EXACTLY ONE PLACE - the multiplication by 1000 and then
    /// <c>if _nKeepAliveExpireTime &lt;= 0 then _nKeepAliveExpireTime = KEEPALIVE_EXPIRE</c>.
    /// <c>Transactions/TransactionPool.cs</c> must call this rather than re-deriving the conversion,
    /// because a second copy of a unit conversion is a second chance to get the unit wrong.
    /// </para>
    /// <para>
    /// A NON-POSITIVE VALUE IS LEGAL AND FALLS BACK; IT IS NOT AN ERROR. Zero and any negative value
    /// both behave as thirty seconds. <see cref="PersistenceOptionsValidator"/> therefore does NOT
    /// reject them - rejecting them would replace a preserved legacy behaviour with a validation
    /// failure the legacy never had, and would also make the shipped default of zero unstartable.
    /// </para>
    /// <para>
    /// Fractional milliseconds are TRUNCATED toward zero, because the legacy assigns the product into
    /// an integral field. A very large configured value SATURATES at
    /// <see cref="int.MaxValue"/> rather than throwing: a checked conversion would raise an overflow
    /// exception from inside a lifetime lookup, which turns a misconfigured number into a fault in an
    /// unrelated place, and saturation keeps the observable outcome "an idle lifetime longer than any
    /// process will live".
    /// </para>
    /// <para>
    /// THE TRUNCATION HAPPENS BEFORE THE ZERO TEST, NOT AFTER, BECAUSE THAT IS THE LEGACY'S OWN ORDER
    /// AND THE ORDER IS OBSERVABLE. The legacy assigns the product into its integral field on one line
    /// and only then compares THAT FIELD against zero on the next [<c>:L78-L79</c>], so a configured
    /// value whose product lands strictly between zero and one millisecond - anything under a
    /// thousandth of a second - becomes zero and takes the thirty-second fallback rather than an idle
    /// lifetime of zero. Testing the double product before truncating would instead yield an immediate
    /// expiry, which is a different behaviour reached by an easy misreading.
    /// </para>
    /// <para>
    /// One further conversion detail is recorded rather than guessed at: PowerScript's implicit
    /// conversion of a double into an integral field is not documented anywhere in this repository and
    /// is not observable through any framework API, so whether it rounds or truncates cannot be settled
    /// from the oracle. This port takes truncation as its documented contract. The two readings can only
    /// disagree for a configured value whose millisecond product carries a fraction, and they agree on
    /// every value any settings file or environment template in this repository declares.
    /// </para>
    /// <para>
    /// A not-a-number configured value also takes the fallback. That is stated because it needs to be:
    /// a comparison against not-a-number is false for every relational operator, so it would slip past
    /// a bare <c>&lt;= 0</c> test and be cast into an undefined integer.
    /// </para>
    /// <para>
    /// One legacy artifact is knowingly not reproduced. The legacy field is UNSIGNED
    /// [<c>:L59</c>], so a negative product would wrap to a huge positive value before reaching the
    /// <c>&lt;= 0</c> test, and whether the fallback was taken would depend on the runtime's
    /// double-to-unsigned conversion. That is unspecified rather than specified behaviour and is not
    /// observable through any framework API, so the documented and authoritative contract is the one
    /// implemented here: at or below zero reverts to 30000 ms.
    /// </para>
    /// </remarks>
    public int ResolveKeepAliveExpireMilliseconds()
    {
        double milliseconds = KeepAliveExpireSeconds * 1000d;

        // Not-a-number compares false against every relational operator, so it has to be caught
        // explicitly rather than left to slip past the comparison below and be cast into an undefined
        // integer. A product at or below zero cannot truncate to anything above zero, so it takes the
        // fallback without being cast at all - which is also what keeps a very large negative value
        // away from a conversion that has no defined result.
        if (double.IsNaN(milliseconds) || milliseconds <= 0d)
        {
            return DefaultKeepAliveExpireMilliseconds;
        }

        // Saturate rather than throw, and note this also absorbs positive infinity.
        if (milliseconds >= int.MaxValue)
        {
            return int.MaxValue;
        }

        // TRUNCATE FIRST, THEN TEST AGAINST ZERO - that is the legacy's own order, and the order
        // matters. A product strictly between zero and one millisecond truncates to zero and therefore
        // takes the fallback, exactly as the legacy does.
        int truncated = (int)milliseconds;
        return truncated <= 0 ? DefaultKeepAliveExpireMilliseconds : truncated;
    }
}


// --------------------------------------------------------------------------------------------------
// GROUP 3 OF 4 - THE PRESERVED LEGACY RETRIEVAL DEFAULTS
// --------------------------------------------------------------------------------------------------

/// <summary>
/// The retrieval defaults of the asynchronous SQL query task. Bound from the top-level <c>Query</c>
/// section.
/// </summary>
/// <remarks>
/// <para>
/// Every value in this group is the legacy reset value, taken from the query task's own reset routine
/// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L247-L267</c>], and each member
/// cites the exact line it comes from. None of them is a judgement of ours: the reset routine IS the
/// specification of what a freshly configured retrieval looks like.
/// </para>
/// <para>
/// THE RESET DEFAULTS AND THE RUNTIME SETTERS DISAGREE, AND BOTH ARE PRESERVED IN THEIR OWN PLACE. Four
/// of these settings also have a runtime setter with a guard, and three of those guards would reject the
/// reset value the same file establishes: the page index and page size reset to zero [<c>:L257</c>,
/// <c>:L259</c>] while their setters refuse any non-positive value [<c>:L450</c>, <c>:L462</c>]. That is
/// not a contradiction - a reset state is not a set operation - so the CONFIGURATION floor here is the
/// reset value, and each setter's stricter guard belongs to the published query contract's own
/// corresponding call rather than to startup validation. Validating the setter's rule here would make
/// the shipped settings file unstartable.
/// </para>
/// <para>
/// Six further fields are cleared by the same reset routine and are deliberately NOT configuration,
/// because each is per-request state rather than a setting: the hook class, the statement text, the
/// statement syntax, the data object, and the new sort and new filter [<c>:L250-L255</c>], along with
/// the where-clause, order-by-clause and paged-unique-index-column arrays [<c>:L264-L266</c>]. The
/// native-paging flag [<c>:L457</c>] is likewise absent: it has a setter but no reset entry and no
/// settings-file key.
/// </para>
/// <para>
/// Legacy widths, recorded so the difference is a decision rather than drift: the legacy declares the
/// chunk size, page index and page size setters over its 32-bit-or-wider integer type, and this port
/// narrows the three to <see cref="int"/> because that is sufficient for every evidenced path and reads
/// correctly with a range annotation, while the maximum row count keeps <see cref="long"/>. The WIRE
/// widths are decided independently by the published protocol definition; nothing in this file
/// constrains them.
/// </para>
/// </remarks>
public sealed class QueryOptions
{
    /// <summary>
    /// The number of rows fetched per chunk during progressive retrieval. Defaults to <c>10000</c>, and
    /// must be STRICTLY GREATER THAN 1000.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The default is the legacy reset value [<c>:L256</c>]. The floor is the legacy setter's own guard,
    /// <c>if chunkSize &lt;= 1000 then return RetCode.E_INVALID_ARGUMENT</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L410</c>], which is
    /// INCLUSIVE: 1000 itself is REJECTED and 1001 is the lowest legal value.
    /// </para>
    /// <para>
    /// THE OFF-BY-ONE HERE IS THE WHOLE POINT OF THE ANNOTATION. A range starting at 1000 would let a
    /// deployment start in a state the published contract's own chunk-size call rejects, so the service
    /// would come up healthy and then fail the first time anything re-stated its own configured value.
    /// The boundary is reproduced verbatim, not rounded.
    /// </para>
    /// <para>
    /// Note the deliberate contrast with <see cref="MaxRows"/>, whose setter rejects only values below
    /// zero [<c>:L433</c>] - so zero is accepted there and rejected here. The two boundaries differ in
    /// the legacy and differ here.
    /// </para>
    /// </remarks>
    [Range(
        1001,
        int.MaxValue,
        ErrorMessage =
            "must be strictly greater than 1000, so 1001 is the lowest legal value. The legacy setter "
            + "rejects any value at or below 1000 - see "
            + "ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L410 - and the "
            + "inclusive boundary is reproduced verbatim, so a service configured at 1000 would start "
            + "in a state its own published chunk-size call refuses.")]
    public int ChunkSize { get; set; } = 10000;

    /// <summary>
    /// The maximum number of rows a retrieval returns, where <c>0</c> means unlimited. Defaults to
    /// <c>0</c>.
    /// </summary>
    /// <remarks>
    /// The default is the legacy reset value [<c>:L261</c>]. The legacy setter rejects only a value
    /// below zero, <c>if rows &lt; 0 then return RetCode.E_INVALID_ARGUMENT</c> [<c>:L433</c>], so zero
    /// is legal and carries the meaning "no limit". The annotation is expressed over the
    /// double-argument range constructor because the attribute has no 64-bit integer constructor; the
    /// effective rule is simply "not negative".
    /// </remarks>
    [Range(
        0,
        long.MaxValue,
        ErrorMessage =
            "must not be negative; 0 means unlimited. The legacy setter rejects only values below zero "
            + "- see ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L433 - which is "
            + "deliberately a different boundary from ChunkSize's.")]
    public long MaxRows { get; set; }

    /// <summary>
    /// Whether a paged retrieval also computes the total row count. Defaults to
    /// <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// The legacy reset value [<c>:L260</c>], and the one boolean in this group whose legacy default is
    /// true. Its setter applies no validation of any kind [<c>:L440</c>], so both values are legal and
    /// nothing here constrains it.
    /// </remarks>
    public bool PageCounting { get; set; } = true;

    /// <summary>
    /// Whether retrieved results are cached. Defaults to <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// The legacy reset value [<c>:L262</c>]. Its setter applies no validation [<c>:L417</c>], so both
    /// values are legal.
    /// </remarks>
    public bool Cache { get; set; }

    /// <summary>
    /// The one-based page to retrieve when paging is enabled. Defaults to <c>0</c>.
    /// </summary>
    /// <remarks>
    /// The legacy reset value [<c>:L257</c>]. Zero is the reset state and therefore legal as
    /// configuration, even though the legacy SETTER refuses any non-positive value
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L450</c>]; that guard
    /// belongs to the published paging call, which is where a caller states an actual page. Configured
    /// zero is what "paging not configured" looks like, and it pairs with <see cref="Paged"/> being
    /// false.
    /// </remarks>
    [Range(
        0,
        int.MaxValue,
        ErrorMessage =
            "must not be negative. Zero is the legacy reset state - see "
            + "ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L257 - and means "
            + "\"paging not configured\"; the setter's stricter non-positive guard at :L450 belongs to "
            + "the published paging call rather than to startup.")]
    public int PageIndex { get; set; }

    /// <summary>
    /// The number of rows per page when paging is enabled. Defaults to <c>0</c>.
    /// </summary>
    /// <remarks>
    /// The legacy reset value [<c>:L259</c>], with the same reset-versus-setter relationship as
    /// <see cref="PageIndex"/>: the setter refuses a non-positive value [<c>:L462</c>], while zero is
    /// the reset state and is what an unpaged configuration carries.
    /// </remarks>
    [Range(
        0,
        int.MaxValue,
        ErrorMessage =
            "must not be negative. Zero is the legacy reset state - see "
            + "ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L259 - and is what an "
            + "unpaged configuration carries; the setter's stricter non-positive guard at :L462 belongs "
            + "to the published paging call rather than to startup.")]
    public int PageSize { get; set; }

    /// <summary>
    /// Whether retrieval is paged at all. Defaults to <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// The legacy reset value [<c>:L258</c>]. Its setter applies no validation [<c>:L445</c>], so both
    /// values are legal. This is the flag that makes <see cref="PageIndex"/> and
    /// <see cref="PageSize"/> meaningful, which is why their configured zeroes are coherent rather
    /// than contradictory.
    /// </remarks>
    public bool Paged { get; set; }
}

// --------------------------------------------------------------------------------------------------
// GROUP 4 OF 4 - INBOUND TOKEN VERIFICATION. VERIFICATION MATERIAL ONLY, NEVER A SIGNING KEY.
// --------------------------------------------------------------------------------------------------

/// <summary>
/// Inbound bearer-token verification settings. Bound from the top-level <c>Jwt</c> section.
/// </summary>
/// <remarks>
/// <para>
/// This group exists because decomposition creates this service's first-ever ingress, and a created
/// boundary is authenticated from the outset. The stock bearer handler resolves the Security service's
/// discovery document and published key set beneath the configured authority with zero bespoke
/// retrieval code, which is exactly why Security publishes over HTTP: the security-critical path stays
/// framework code rather than hand-written code.
/// </para>
/// <para>
/// THERE IS NO SIGNING KEY HERE, IN ANY SPELLING, AND THAT IS STRUCTURAL. Exactly one signing secret
/// exists in the whole system and the Security service holds it as the SOLE ISSUER; the other three
/// services hold VERIFICATION material only. A signing key on this type would create a second issuer
/// and break the sole-issuer topology outright, so there is no signing key, no issuer signing key, no
/// symmetric key, no secret key and no client secret on this type - and no reference anywhere in this
/// file to the environment name under which Security's own signing secret travels.
/// </para>
/// <para>
/// The health route is anonymous so the orchestration readiness gate can reach it before any token
/// exists; the ping route requires a token and answers unauthorized without one. Neither of those
/// properties is configurable, which is why neither appears as a member here.
/// </para>
/// <para>
/// Two further settings that Program.cs reads off this same section are deliberately not modelled here:
/// an explicit metadata address, and the four individual token-validation toggles. They are absent from
/// both settings files and are read directly with safe defaults, so a deployment can tighten them but
/// never silently loosen them by omission - and modelling them would put two binders over one key,
/// which is the same reason the listening endpoint is not modelled either.
/// </para>
/// </remarks>
public sealed class JwtOptions
{
    /// <summary>
    /// The Security service's base address, beneath which its discovery document and key set are
    /// published. Required; defaults to an empty string.
    /// </summary>
    /// <remarks>
    /// The default is empty rather than a plausible URL, because a real address baked in here would be
    /// an invented value that looks configured - and would let a deployment that forgot to set it
    /// silently point at whatever host that literal happened to name. Validation is what makes its
    /// absence fatal, and the shipped settings file supplies the in-network address.
    /// </remarks>
    [Required(
        AllowEmptyStrings = false,
        ErrorMessage =
            "must name the Security service's base address, beneath which its discovery document and "
            + "published key set live. Without it inbound tokens cannot be verified against any key "
            + "set and every authenticated request would be refused, so the host refuses to start "
            + "instead.")]
    public string Authority { get; set; } = string.Empty;

    /// <summary>
    /// The audience value this service requires in an inbound token. Required; defaults to an empty
    /// string.
    /// </summary>
    /// <remarks>
    /// Presence is enforced, which goes one step beyond the stated minimum, and the reason is worth
    /// recording: a bearer configuration with no audience either rejects every token or has audience
    /// validation switched off, and switching it off would weaken a boundary that must be authenticated
    /// from the outset. Failing at startup is strictly better than either outcome, because both of them
    /// look like a healthy service.
    /// </remarks>
    [Required(
        AllowEmptyStrings = false,
        ErrorMessage =
            "must name the audience this service requires in an inbound token. An empty audience "
            + "either rejects every token or forces audience validation off, and neither is acceptable "
            + "on a boundary that must be authenticated from the outset.")]
    public string Audience { get; set; } = string.Empty;

    /// <summary>
    /// The path, RELATIVE to <see cref="Authority"/>, at which the Security service publishes its key
    /// set. Required; defaults to <c>/.well-known/jwks.json</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The default is the standard well-known location, spelled exactly. A typo here does not fail
    /// loudly - it produces a key-set address that resolves to nothing, and every token validation in
    /// the service then fails for a reason that looks nothing like a configuration mistake. So the
    /// spelling is part of the contract, and the validator additionally refuses a value that is not
    /// rooted, because a value without a leading slash composes against the authority as a sibling
    /// rather than as a path.
    /// </para>
    /// <para>
    /// Relative rather than absolute on purpose: this service composes its key-set address from the
    /// authority plus this path, so the two values move together and a change of authority cannot leave
    /// a stale absolute key-set URL behind.
    /// </para>
    /// </remarks>
    [Required(
        AllowEmptyStrings = false,
        ErrorMessage =
            "must carry the path, relative to Jwt:Authority, at which the Security service publishes "
            + "its key set. The standard value is /.well-known/jwks.json; a wrong path breaks every "
            + "token validation in this service without producing a configuration error.")]
    public string JwksPath { get; set; } = "/.well-known/jwks.json";

    /// <summary>
    /// Whether discovery metadata must be retrieved over a transport-secured connection. Defaults to
    /// <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// Defaults to on, and a missing key therefore leaves the safe value in place: a deployment can
    /// tighten this but never loosen it by omission. Relaxing it anywhere other than a developer's
    /// loopback run weakens the newly created boundary, because verification material fetched over an
    /// unsecured connection can be substituted in transit - and substituting the key set is
    /// substituting the identity of every caller.
    /// </remarks>
    public bool RequireHttpsMetadata { get; set; } = true;
}


// --------------------------------------------------------------------------------------------------
// VALIDATION - WHERE THE FAIL-FAST POSTURE ACTUALLY LIVES
// --------------------------------------------------------------------------------------------------

/// <summary>
/// Validates a bound <see cref="PersistenceOptions"/> graph, accumulating every broken rule.
/// </summary>
/// <remarks>
/// <para>
/// AN EXPLICIT VALIDATOR IS REQUIRED HERE RATHER THAN OPTIONAL, and the reason is one framework fact:
/// <c>ValidateDataAnnotations</c> does NOT recurse into nested complex properties. Every annotation on
/// the four groups would therefore be silently ignored, and a service configured with an empty
/// authority or a chunk size of 1000 would start perfectly happily. The attribute that would fix that
/// recursion ships in a source-generation package this service may not take, so the recursion is done
/// here instead - one call per group, which is exactly the shape that gap requires.
/// </para>
/// <para>
/// THIS IS WHERE THE FRAMEWORK'S FAIL-FAST POSTURE IS PRESERVED. <c>Program.cs</c> registers this
/// validator with validation on start, so a broken setting throws during startup and the process never
/// serves a request. The legacy behaves the same way for a structural fault: its application object
/// decodes a seven-field assert payload split on a line separator and then executes <c>HALT CLOSE</c>
/// [<c>ws_objects/pfw.pbl.src/pfw.sra:L111-L144</c>, the halt itself at <c>:L143</c>], and worker-session
/// creation failure is fatal rather than degraded. Softening a structural configuration fault into
/// warn-and-continue would be a behavioural change dressed up as robustness, and it is forbidden.
/// </para>
/// <para>
/// EVERY FAILURE IS ACCUMULATED, NEVER SHORT-CIRCUITED, so one restart reveals every misconfiguration
/// instead of one per attempt. Each message names the configuration key path it belongs to - for
/// instance <c>Query:ChunkSize</c> - because a message an operator cannot act on is a message that
/// costs a second restart. Message composition is <c>path</c> + <c>":"</c> + member + <c>" "</c> +
/// text, which is why every annotation in this file carries an explicit message phrased as a
/// continuation beginning with "must".
/// </para>
/// <para>
/// NO I/O, NO CLOCK, NO AMBIENT STATE. There is no directory existence check, no directory creation, no
/// request to the key-set endpoint and no time read anywhere in this type. "The data directory is
/// present" means THE SETTING IS SUPPLIED, not that the path exists on disk: checking the filesystem
/// would make a unit test environment-dependent and could fail a healthy service for a transient
/// reason, on a startup path that has no way to retry. Shape is validated; reachability is not.
/// </para>
/// <para>
/// It is <c>public</c> so both <c>Program.cs</c> and the sibling test project can construct it directly
/// and assert on its result without booting a host, which is what keeps the per-service coverage gate
/// reachable for every branch below.
/// </para>
/// </remarks>
public sealed class PersistenceOptionsValidator : IValidateOptions<PersistenceOptions>
{
    /// <summary>
    /// The six journal modes the legacy URI extension accepts, in the order the legacy comment lists
    /// them.
    /// </summary>
    /// <remarks>
    /// From <c>journal[=DELETE|TRUNCATE|PERSIST|MEMORY|WAL|OFF]</c> at
    /// <c>ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L455</c>. Compared case-insensitively and
    /// ordinally: the tokens are ASCII keywords of a URI parameter, so a culture-sensitive comparison
    /// could disagree with the engine that ultimately parses them.
    /// </remarks>
    private static readonly string[] JournalModes =
        ["DELETE", "TRUNCATE", "PERSIST", "MEMORY", "WAL", "OFF"];

    /// <summary>
    /// Validates one bound instance.
    /// </summary>
    /// <param name="name">
    /// The named options instance being validated, or <see langword="null"/> or empty for the default
    /// instance. A name is folded into every message so a fault in a named instance is attributable;
    /// the default instance produces bare key paths such as <c>Query:ChunkSize</c>, because those are
    /// the paths an operator actually edits.
    /// </param>
    /// <param name="options">The bound instance to validate.</param>
    /// <returns>
    /// <see cref="ValidateOptionsResult.Success"/> when no rule is broken, otherwise a failure result
    /// carrying one message per broken rule.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    public ValidateOptionsResult Validate(string? name, PersistenceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string> failures = [];
        string prefix = string.IsNullOrEmpty(name) ? string.Empty : string.Concat("[", name, "]");

        // --- Sqlite: the three required parts, the journal token set, and the three-state check -----
        string path = string.Concat(prefix, "Sqlite");
        if (EnsureSectionBound(options.Sqlite, path, failures))
        {
            AppendAnnotationFailures(options.Sqlite, path, failures);
            AppendJournalFailure(options.Sqlite.Journal, path, failures);
            AppendIntegrityCheckFailure(options.Sqlite.Check, path, failures);

            // Password is deliberately unchecked in every direction. It is optional, its absence is a
            // meaningful state distinct from an empty value, and its content is a credential that may
            // not be echoed into a failure message even to say it is malformed.
        }

        // --- TransactionPool: NO RULE, AND NONE MAY BE ADDED ----------------------------------------
        // Every member of this group is legal across its whole domain. KeepAlive is a boolean whose
        // both values the legacy accepts. TransactionClassName's empty default means "use the
        // registered default implementation", and a non-empty value is carried for parity only and is
        // never resolved into a type, so there is nothing to validate it against. And
        // KeepAliveExpireSeconds AT OR BELOW ZERO IS LEGAL: the legacy falls back to 30000 ms for it
        // [ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans_pool.sru:L79] rather than rejecting it,
        // that fallback is the live default path because the shipped value is zero, and rejecting it
        // here would both invent a validation the legacy never had and make the shipped settings file
        // unstartable. The section is therefore only checked for having been bound at all.
        _ = EnsureSectionBound(options.TransactionPool, string.Concat(prefix, "TransactionPool"), failures);

        // --- Query: the four numeric floors, all carried as annotations ------------------------------
        path = string.Concat(prefix, "Query");
        if (EnsureSectionBound(options.Query, path, failures))
        {
            AppendAnnotationFailures(options.Query, path, failures);

            // The three booleans are deliberately unvalidated: their legacy setters apply no guard at
            // all, so both values are legal for each and a rule here would be an addition.
        }

        // --- Jwt: presence of all three strings, plus the key-set path's shape ----------------------
        path = string.Concat(prefix, "Jwt");
        if (EnsureSectionBound(options.Jwt, path, failures))
        {
            AppendAnnotationFailures(options.Jwt, path, failures);
            AppendJwksPathFailure(options.Jwt.JwksPath, path, failures);

            // RequireHttpsMetadata is unvalidated by design: false is a legal value that a developer's
            // loopback run legitimately needs, and the defence against it reaching a deployed stack is
            // that it defaults to true and a settings file is reviewed, not that startup refuses it.
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    /// <summary>
    /// Records a failure when a group was bound to an explicit null, and reports whether it is safe to
    /// inspect.
    /// </summary>
    /// <param name="section">The bound group, which may be null only through an explicit null binding.</param>
    /// <param name="configurationPath">The group's configuration path, for the message.</param>
    /// <param name="failures">The failure list to append to.</param>
    /// <returns>
    /// <see langword="true"/> when the group is present and may be inspected; otherwise
    /// <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Every group property is non-nullable and initialised to a fresh instance, so an ABSENT section
    /// leaves the defaults in place and this check passes. It exists for the two shapes that can still
    /// defeat that: a configuration document declaring the group as an explicit null, and a caller
    /// assembling the graph by hand in a test. Reporting it is strictly better than dereferencing it -
    /// the fault becomes an attributable startup message rather than a null-reference exception with no
    /// configuration path attached to it.
    /// </para>
    /// <para>
    /// The not-null-when-true annotation lets a caller dereference the group after a true result
    /// without a second null check. That is not cosmetic: without it, every call site needs a redundant
    /// re-check to satisfy nullable analysis, and the false half of such a check is a branch no test
    /// can ever reach.
    /// </para>
    /// </remarks>
    private static bool EnsureSectionBound(
        [NotNullWhen(true)] object? section,
        string configurationPath,
        List<string> failures)
    {
        if (section is not null)
        {
            return true;
        }

        failures.Add(string.Concat(
            configurationPath,
            " was bound to null. Omit the section entirely to accept its preserved legacy defaults, ",
            "rather than declaring it as a null value."));
        return false;
    }

    /// <summary>
    /// Runs the data annotations declared on one group and appends one message per violation, qualified
    /// by the group's configuration path.
    /// </summary>
    /// <param name="section">The bound group to validate.</param>
    /// <param name="configurationPath">The group's configuration path, for the message.</param>
    /// <param name="failures">The failure list to append to.</param>
    /// <remarks>
    /// Called once per group precisely BECAUSE annotation validation does not recurse. Annotations
    /// carry the rules they express cleanly - presence and numeric range - and everything that an
    /// attribute cannot say is written out explicitly in <see cref="Validate"/>. A result with no
    /// member names still produces a message, attributed to the group rather than dropped, because a
    /// silently discarded failure is worse than an imprecisely attributed one.
    /// </remarks>
    private static void AppendAnnotationFailures(
        object section,
        string configurationPath,
        List<string> failures)
    {
        ValidationContext context = new(section);
        List<ValidationResult> results = [];
        if (Validator.TryValidateObject(section, context, results, validateAllProperties: true))
        {
            return;
        }

        foreach (ValidationResult result in results)
        {
            string message = result.ErrorMessage ?? "is invalid.";
            bool attributed = false;

            foreach (string member in result.MemberNames)
            {
                attributed = true;
                failures.Add(string.Concat(configurationPath, ":", member, " ", message));
            }

            if (!attributed)
            {
                failures.Add(string.Concat(configurationPath, " ", message));
            }
        }
    }

    /// <summary>
    /// Appends a failure when the journal mode is not one of the six legacy tokens.
    /// </summary>
    /// <param name="journal">The configured journal mode.</param>
    /// <param name="configurationPath">The Sqlite group's configuration path, for the message.</param>
    /// <param name="failures">The failure list to append to.</param>
    /// <remarks>
    /// <para>
    /// Trimmed and compared case-insensitively, so <c>wal</c>, <c>WAL</c> and <c>" WAL "</c> all pass -
    /// the value ends up in a URI parameter, where surrounding whitespace is an authoring artefact
    /// rather than meaning.
    /// </para>
    /// <para>
    /// A blank value is left to the group's <c>[Required]</c> annotation, which has already reported it
    /// by the time this runs. Checking it again here would produce two messages for one fault, and a
    /// duplicated failure trains an operator to skim the list.
    /// </para>
    /// <para>
    /// The offending value IS quoted in the message, because a journal mode is a URI keyword and not a
    /// credential. Nothing in the Sqlite group is quoted except this one and, in the check below, an
    /// enumeration number.
    /// </para>
    /// </remarks>
    private static void AppendJournalFailure(
        string journal,
        string configurationPath,
        List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(journal))
        {
            return;
        }

        if (JournalModes.Contains(journal.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        failures.Add(string.Concat(
            configurationPath,
            ":Journal must be one of DELETE, TRUNCATE, PERSIST, MEMORY, WAL or OFF, matched ",
            "case-insensitively - the six tokens the legacy URI extension documents at ",
            "ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L455. The legacy default is DELETE, and WAL ",
            "is legal but is deliberately not the default. Supplied: \"",
            journal,
            "\"."));
    }

    /// <summary>
    /// Appends a failure when the integrity-check mode is present but is not one of the two declared
    /// values.
    /// </summary>
    /// <param name="check">The configured integrity-check mode, or <see langword="null"/> when absent.</param>
    /// <param name="configurationPath">The Sqlite group's configuration path, for the message.</param>
    /// <param name="failures">The failure list to append to.</param>
    /// <remarks>
    /// <para>
    /// NULL IS LEGAL AND IS LEFT EXACTLY AS IT IS. It is the third state of a three-state setting - the
    /// check parameter omitted from the URI entirely - so it returns without comment and without being
    /// coerced into anything.
    /// </para>
    /// <para>
    /// THE CHECK IS NOT REDUNDANT WITH THE ENUMERATION TYPE. Configuration binding will happily produce
    /// an undeclared value from a numeric string, and the connection factory would then be asked to
    /// compose a URI parameter for a mode that does not exist. Catching it here means a mistyped mode
    /// fails startup naming the setting, instead of surfacing later as an exception from inside URI
    /// composition with no configuration path attached. The message quotes the numeric value only, which
    /// is what an operator needs in order to find the offending line.
    /// </para>
    /// </remarks>
    private static void AppendIntegrityCheckFailure(
        SqliteIntegrityCheckMode? check,
        string configurationPath,
        List<string> failures)
    {
        if (check is null || Enum.IsDefined(check.Value))
        {
            return;
        }

        failures.Add(string.Concat(
            configurationPath,
            ":Check must be either Full - a bare check parameter, running PRAGMA integrity_check - or ",
            "Quick, running PRAGMA quick_check, or must be omitted entirely to run no check at all. ",
            "Those are the three states the legacy grammar at ",
            "ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L454 documents, and there is deliberately ",
            "no fallback for an unrecognised value. Supplied numeric value: ",
            ((int)check.Value).ToString(CultureInfo.InvariantCulture),
            "."));
    }

    /// <summary>
    /// Appends a failure when the key-set path is present but is not rooted.
    /// </summary>
    /// <param name="jwksPath">The configured key-set path.</param>
    /// <param name="configurationPath">The Jwt group's configuration path, for the message.</param>
    /// <param name="failures">The failure list to append to.</param>
    /// <remarks>
    /// The value is composed RELATIVE to the configured authority, so a path without a leading slash
    /// resolves against the authority's last segment rather than against its root, and the resulting
    /// address quietly points somewhere no key set is published. Every token validation then fails for
    /// a reason that looks nothing like a configuration mistake, which is precisely why this is a
    /// startup failure rather than a runtime surprise. A blank value is left to the group's
    /// <c>[Required]</c> annotation, which has already reported it.
    /// </remarks>
    private static void AppendJwksPathFailure(
        string jwksPath,
        string configurationPath,
        List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(jwksPath) || jwksPath.StartsWith('/'))
        {
            return;
        }

        failures.Add(string.Concat(
            configurationPath,
            ":JwksPath must begin with \"/\", because it is composed relative to Jwt:Authority. The ",
            "standard value is /.well-known/jwks.json. Supplied: \"",
            jwksPath,
            "\"."));
    }
}

