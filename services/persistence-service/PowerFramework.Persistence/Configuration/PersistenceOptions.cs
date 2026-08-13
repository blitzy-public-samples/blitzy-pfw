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
//  ONE endpoint is declared in the standard `Kestrel` section of appsettings.json and bound by the
//  ASP.NET Core host itself: `Rest` at `https://+:5101` with `Protocols: Http1AndHttp2`, carrying the
//  readiness probe, the authentication proof AND the four published contracts C-05..C-08. It terminates
//  TLS, which is what makes one address serve both versions: ALPN negotiates `h2` or `http/1.1` per
//  connection, and 5101 is the port AAP 0.3.2.2 assigns this service. Reading it from configuration
//  already satisfies "never hardcode a port", so no port or protocol property appears here: two binders
//  over one key is a silent-divergence risk, and a second port key would create the
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
//  exactly. Twenty option leaves, twenty members:
//
//    Jwt:Authority                             -> JwtOptions.Authority
//    Jwt:MetadataAddress                       -> read directly in Program.cs, not bound here
//    Jwt:Audience                              -> JwtOptions.Audience
//    Jwt:RequireHttpsMetadata                  -> JwtOptions.RequireHttpsMetadata
//    Jwt:MetadataRefreshInterval               -> JwtOptions.MetadataRefreshInterval
//    Jwt:MetadataAutomaticRefreshInterval      -> JwtOptions.MetadataAutomaticRefreshInterval
//
//  THE TWO REFRESH INTERVALS ARE BOTH MODELLED AND READ DIRECTLY, WHICH IS A THIRD SHAPE AND IS
//  DELIBERATE. They are MODELLED so PersistenceOptionsValidator can refuse a value below the token
//  library's own floor at startup - the configuration manager otherwise throws on the FIRST
//  AUTHENTICATED REQUEST, long after a healthy startup - and they are READ FROM THE SAME SECTION
//  SNAPSHOT as the authority they qualify, for the reason the composition root records at its bearer
//  callback. Program.cs falls back to the same constants this type defaults to, so an absent key and an
//  unbound one cannot disagree.
//
//  THERE IS NO Jwt:JwksPath LEAF, AND ITS ABSENCE IS THE DECISION. Declaring, documenting and
//  validating one reads as though this service composed its own key-set address beneath the authority.
//  It does not: AddPersistenceAuthentication configures the stock bearer handler, and that handler
//  resolves the key set by fetching the authority's discovery document and following its published
//  `jwks_uri`. No code path would read such a leaf, so an operator who overrode it would change nothing
//  while believing a key-set address had moved - which is strictly worse than having no setting at all,
//  because the documented key inventory would then be false. ONE authoritative metadata flow is advertised
//  and it is the live one: standard discovery beneath Jwt:Authority, with Jwt:MetadataAddress as the
//  single override for a deployment that republishes the discovery document elsewhere. DO NOT
//  REINSTATE THE LEAF unless a code path is added that genuinely reads it.
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
using System.Security.Cryptography.X509Certificates;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using PowerFramework.Persistence.Concurrency;

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
/// The listening endpoint is NOT here. It is declared in the standard <c>Kestrel</c> section and bound
/// by the host: <c>Rest https://+:5101</c> with <c>Http1AndHttp2</c>, carrying the readiness probe, the
/// authentication proof and the four published gRPC contracts on the one port AAP 0.3.2.2 assigns.
/// Restating a port here would put two binders over one key.
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

    /// <summary>
    /// The trust anchor this service verifies Security's certificate against when it fetches the
    /// published key set. Bound from the top-level <c>InternalTls</c> section.
    /// </summary>
    /// <remarks>
    /// WHY THIS SERVICE NEEDS ONE AT ALL, SINCE IT CALLS NOBODY. It has exactly one outbound channel and
    /// it is the most consequential one in the system: the stock bearer handler's backchannel to
    /// Security's discovery document and published key set, which is what decides WHICH KEYS SIGN A
    /// VALID TOKEN. Security terminates TLS with a certificate issued by the local authority
    /// <c>docs/ARCHITECTURE.md</c> §9.3.1 generates, and that authority is in no container's
    /// operating-system trust store - so left on platform default trust the handler cannot fetch the key
    /// set, and every inbound token is refused for want of a key rather than on its merits. Setting the
    /// anchor NARROWS trust to it; nothing here relaxes validation.
    /// </remarks>
    public InternalTlsTrustOptions InternalTls { get; set; } = new();

    /// <summary>
    /// The bounds on server-held work handles: how many may exist, per caller and in total, and how long
    /// an untouched one survives. Bound from the top-level <c>Handles</c> section.
    /// </summary>
    /// <remarks>
    /// WHY THIS SECTION EXISTS AT ALL - IT IS THE COST OF THE BOUNDARY, NOT A FEATURE. In process a task
    /// and a transaction were held by REFERENCE, so an abandoned one was collected the moment its last
    /// reference went out of scope and the legacy needed no bound of any kind. Across a boundary a caller
    /// holds a NAME, and the object behind it is pinned by the server's own table until a release arrives -
    /// so a caller that crashes, times out or simply forgets leaves a live transaction, an open connection
    /// and, for a command or update task, a worker task alive for the life of the PROCESS. These four
    /// values are what make that failure mode bounded instead of terminal, and every one of them is a
    /// property of the boundary rather than of any legacy behaviour (constraints C-A, C-B).
    /// </remarks>
    public HandleLifecycleOptions Handles { get; set; } = new();

    /// <summary>
    /// Whether this deployment applies the pending migrations at startup. Bound from the top-level
    /// <c>Schema</c> section.
    /// </summary>
    /// <remarks>
    /// SEPARATE FROM <see cref="Sqlite"/> ON PURPOSE, AND THE DISTINCTION IS NOT COSMETIC. The
    /// <c>Sqlite</c> group is the EVIDENCED URI GRAMMAR and nothing else - its member set is pinned by a
    /// test precisely so a runtime concern with no place in that grammar cannot be added to it. Schema
    /// provisioning is a deployment decision about WHEN the schema is applied, not a part of the
    /// connection URI, so it is its own section for the same reason <see cref="Handles"/> is.
    /// </remarks>
    public SchemaOptions Schema { get; set; } = new();

    /// <summary>
    /// The data-object definitions this service can resolve by name. Bound from the top-level
    /// <c>DataObjects</c> section.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHY A DEFINITION IS CONFIGURATION AND NOT CODE. The legacy assigns a name and the PowerBuilder
    /// runtime loads the compiled DataWindow out of the target's library list -
    /// <c>ds.DataObject = dataObject</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru:L558</c>]. There is no managed
    /// equivalent: the <c>.srd</c> objects live in the read-only legacy tree and no .NET runtime can load
    /// one. So the resolution becomes an injected collaborator, and the material it resolves FROM is a
    /// deployment fact - which is exactly what an options section is for.
    /// </para>
    /// <para>
    /// A CALLER MAY ALSO SUPPLY A DEFINITION OUTRIGHT, and the published contract anticipates that:
    /// <c>QuerySpec</c> carries <c>data_object</c>, <c>sql_syntax</c>, <c>sql</c>, <c>filter</c> and
    /// <c>sort</c> side by side [<c>persistence.v1.proto</c>]. This section serves the first of those -
    /// the named case - and nothing here constrains the others.
    /// </para>
    /// <para>
    /// EMPTY IS LEGAL AND MEANS "NO NAMED DEFINITION RESOLVES". A deployment whose callers always supply
    /// a statement needs no entry, and the runtime then answers the interface's own documented negative
    /// for every name. It is not a validation failure, because refusing to start over an unused
    /// capability would be worse than serving the callers that do not need it.
    /// </para>
    /// </remarks>
    public IList<DataObjectOptions> DataObjects { get; } = [];
}

/// <summary>
/// One data-object definition: the six describe-able properties of a legacy DataWindow object, plus the
/// name it is resolved by.
/// </summary>
/// <remarks>
/// <para>
/// THE MEMBER SET IS THE ORACLE'S, NOT A CONVENIENT SUBSET. It mirrors the record the task layer
/// consumes field for field - statement, sort, filter, processing, arguments and units - because each is
/// a property the legacy reads back through <c>Describe</c> and each one has a consumer: the statement is
/// the retrieval, sort and filter are applied through the store, processing selects the changeset or
/// full-state transfer path, arguments drive the retrieval-argument matching, and units is the probe the
/// query task uses to detect a data object that did not load
/// [<c>n_cst_thread_task_sqlquery.sru:L554-L557</c>].
/// </para>
/// <para>
/// NO CREDENTIAL, NO PATH AND NO CONNECTION DETAIL APPEARS HERE (constraint C-F). A definition names a
/// statement and its shape; where that statement runs is <c>Sqlite</c>'s business and nothing else's.
/// </para>
/// </remarks>
public sealed class DataObjectOptions
{
    /// <summary>
    /// The name callers resolve this definition by. Required.
    /// </summary>
    /// <remarks>
    /// Matched ORDINALLY by the catalogue, because a data-object name is an opaque identifier a caller
    /// sends on the wire: folding case would let two distinct names collide on some hosts and not others.
    /// </remarks>
    [Required(AllowEmptyStrings = false)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The retrieval statement - the value <c>GetSQLSelect()</c> answers. Required.
    /// </summary>
    /// <remarks>
    /// A definition with no statement could retrieve nothing while appearing to resolve, which is the one
    /// shape worth refusing outright: the caller would receive a successful zero-row retrieval and have
    /// no way to tell it from an empty table.
    /// </remarks>
    [Required(AllowEmptyStrings = false)]
    public string SqlSelect { get; set; } = string.Empty;

    /// <summary>The sort expression, or empty for none.</summary>
    public string Sort { get; set; } = string.Empty;

    /// <summary>The filter expression, or empty for none.</summary>
    public string Filter { get; set; } = string.Empty;

    /// <summary>
    /// The value <c>Describe("DataWindow.Processing")</c> answers.
    /// </summary>
    /// <remarks>
    /// It selects the cross-thread transfer path: the sole evidenced fixture declares <c>processing=1</c>
    /// [<c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L3</c>], which takes the CHANGESET path, so 1 is the
    /// default here rather than a neutral zero.
    /// </remarks>
    public string Processing { get; set; } = "1";

    /// <summary>
    /// The retrieval arguments as <c>name{tab}type</c> pairs, or empty when the definition takes none.
    /// </summary>
    /// <remarks>
    /// The grammar is the oracle's and is parsed by the task layer's own parser, so nothing here
    /// re-specifies it. The evidenced fixture takes no arguments, so empty is the default.
    /// </remarks>
    public string Arguments { get; set; } = string.Empty;

    /// <summary>
    /// The value <c>Describe("DataWindow.Units")</c> answers. Must not be empty.
    /// </summary>
    /// <remarks>
    /// LOAD BEARING, AND THE REASON IT IS VALIDATED. The query task treats an EMPTY units answer as proof
    /// the data object did not resolve [<c>n_cst_thread_task_sqlquery.sru:L554-L557</c>], so a definition
    /// that resolved successfully while publishing an empty units value would be reported as unresolved by
    /// the very next line of the task that asked for it.
    /// </remarks>
    [Required(AllowEmptyStrings = false)]
    public string Units { get; set; } = "1";

    /// <summary>
    /// The table the update path writes to - <c>Describe("DataWindow.Table.UpdateTable")</c>. Empty when
    /// the definition is retrieve-only.
    /// </summary>
    /// <remarks>
    /// The sole evidenced fixture declares <c>update="COMPANY"</c>
    /// [<c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L14</c>]. EMPTY IS LEGAL AND MEANS RETRIEVE-ONLY:
    /// the update path refuses a definition with no update table, which is the same refusal the oracle
    /// issues [<c>n_cst_thread_task_sqlupdate.sru:L188-L192</c>], and a great many definitions are only
    /// ever retrieved from.
    /// </remarks>
    public string UpdateTable { get; set; } = string.Empty;

    /// <summary>
    /// The optimistic-concurrency mode - <c>Describe("DataWindow.Table.UpdateWhere")</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>0</c> compares the key columns only, <c>1</c> the key columns PLUS the original value of every
    /// updateable column, and <c>2</c> the key columns plus the original value of every column the caller
    /// actually changed.
    /// </para>
    /// <para>
    /// ONE IS THE DEFAULT BECAUSE IT IS WHAT THE EVIDENCE DECLARES: the fixture reads
    /// <c>updatewhere=1</c> [<c>dw_sqlite.srd:L14</c>], and all six of its columns are marked
    /// <c>updatewhereclause=yes</c> [<c>:L8-L14</c>], so the check spans all six originals. Defaulting to
    /// zero would silently weaken the concurrency check of every definition that did not restate it.
    /// </para>
    /// </remarks>
    [Range(0, 2)]
    public long UpdateWhere { get; set; } = 1L;

    /// <summary>
    /// Whether a key-column change is applied in place - <c>Describe("DataWindow.Table.UpdateKeyinPlace")</c>.
    /// </summary>
    /// <remarks>
    /// FALSE IS THE DEFAULT, MATCHING <c>updatekeyinplace=no</c> [<c>dw_sqlite.srd:L14</c>]. With it
    /// false, a key change becomes a delete plus an insert rather than an in-place update - and it is the
    /// exact trigger for the workaround the oracle documents against itself at
    /// <c>n_cst_thread_task_sqlupdate.sru:L151-L167</c>, which the fixture therefore exercises rather
    /// than leaving as a rare branch.
    /// </remarks>
    public bool UpdateKeyInPlace { get; set; }

    /// <summary>
    /// The definition's columns, in DataWindow column order. Position one in this list is column number
    /// one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHY THE COLUMNS ARE DECLARED RATHER THAN DISCOVERED. The whole <c>Concurrency/</c> layer is written
    /// against a describe surface - <c>DataWindow.Column.Count</c>, <c>&lt;name&gt;.Id</c>,
    /// <c>#n.Identity</c>, <c>#n.DBName</c> - and every one of those is a property of the DataWindow
    /// DEFINITION, not of the statement's result set. Declaring them is what lets the update contract be
    /// re-derived, validated and refused without a database, which is the property the per-service
    /// coverage gate depends on.
    /// </para>
    /// <para>
    /// EMPTY IS LEGAL. A retrieve-only definition needs no column declaration at all; the update path is
    /// the only consumer, and it refuses a definition that names an update table with no columns rather
    /// than generating a statement with an empty column list.
    /// </para>
    /// </remarks>
    public IList<DataObjectColumnOptions> Columns { get; } = [];
}

/// <summary>
/// One column of a data-object definition: its name, its database name, and the four update attributes
/// the legacy <c>.srd</c> declares per column.
/// </summary>
/// <remarks>
/// THE FOUR ATTRIBUTES ARE THE ORACLE'S OWN, spelled as the fixture spells them
/// [<c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L8-L14</c>]: <c>update</c>, <c>key</c>,
/// <c>identity</c> and <c>updatewhereclause</c>. They are separate booleans rather than a flags
/// enumeration because the modification script the update path applies at run time sets each one
/// independently, by name, one line at a time [<c>n_cst_thread_task_sqlupdate.sru:L103-L129</c>].
/// </remarks>
public sealed class DataObjectColumnOptions
{
    /// <summary>The column's DataWindow name - the name <c>&lt;name&gt;.Id</c> resolves. Required.</summary>
    [Required(AllowEmptyStrings = false)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The column's database name - the value <c>Describe("#n.DBName")</c> answers. Defaults to
    /// <see cref="Name"/> when left empty.
    /// </summary>
    /// <remarks>
    /// The fixture answers a plain unqualified <c>id</c> here [<c>dw_sqlite.srd:L8</c>], which is exactly
    /// why the identity resolver's table-prefix arm fails on it and its fallback arm is the measured path.
    /// Left empty this defaults to the DataWindow name, which is the ordinary case.
    /// </remarks>
    public string DbName { get; set; } = string.Empty;

    /// <summary>Whether the column participates in generated INSERT and UPDATE statements.</summary>
    public bool Update { get; set; }

    /// <summary>Whether the column is part of the update key.</summary>
    public bool Key { get; set; }

    /// <summary>
    /// Whether the column is the table's identity column, whose value the database assigns.
    /// </summary>
    /// <remarks>
    /// An identity column is EXCLUDED from a generated INSERT's column list - the database assigns it -
    /// and its assigned value is read back by the identity round trip
    /// [<c>n_cst_thread_task_sqlupdate.sru:L215-L247</c>].
    /// </remarks>
    public bool Identity { get; set; }

    /// <summary>
    /// Whether the column's ORIGINAL value may appear in a generated statement's where clause.
    /// </summary>
    /// <remarks>
    /// TRUE BY DEFAULT, matching every column of the sole evidenced fixture
    /// [<c>dw_sqlite.srd:L8-L14</c>]. It gates participation in the concurrency predicate: a column
    /// marked <see langword="false"/> is excluded from the where clause even under the key-and-updateable
    /// mode, which WEAKENS the check - so the default is the safe value rather than the neutral one.
    /// </remarks>
    public bool UpdateWhereClause { get; set; } = true;
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
    /// How often the idle sweep runs, in seconds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THIS IS A LIFECYCLE SETTING, NOT A PERFORMANCE ONE, and the distinction matters because no
    /// performance objective may be asserted anywhere in this refactor. The legacy subscribes its pool to
    /// the framework's idle notification inside the keep-alive branch
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans_pool.sru:L80</c>], so a retained
    /// transaction is collected once it has been idle for its expiry window. Without a periodic sweep on
    /// this side nothing ever fires that collection and a retained connection is held for the life of the
    /// process - a resource leak rather than a slow service.
    /// </para>
    /// <para>
    /// A NON-POSITIVE VALUE DISABLES THE SWEEP, which is the honest way to express "no sweeper" rather
    /// than a sentinel interval. Keep-alive being off disables it too, on the pool's own gate, so the
    /// sweep can never collect an entry the legacy would have retained.
    /// </para>
    /// </remarks>
    public double IdleSweepIntervalSeconds { get; set; } = DefaultIdleSweepIntervalSeconds;

    /// <summary>
    /// The shipped sweep interval in seconds.
    /// </summary>
    /// <remarks>
    /// Chosen as a fraction of the smallest expiry window a deployment is likely to configure, so an entry
    /// is collected within a bounded multiple of its own idle window rather than at an interval unrelated
    /// to it. It carries no latency or throughput claim.
    /// </remarks>
    public const double DefaultIdleSweepIntervalSeconds = 15.0;

    /// <summary>
    /// The sweep interval as a <see cref="TimeSpan"/>, or <see langword="null"/> when disabled.
    /// </summary>
    /// <returns>The interval, or <see langword="null"/>.</returns>
    public TimeSpan? ResolveIdleSweepInterval() =>
        IdleSweepIntervalSeconds > 0.0
            ? TimeSpan.FromSeconds(IdleSweepIntervalSeconds)
            : null;

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
/// One further setting that Program.cs reads off this same section is deliberately not modelled here:
/// an explicit metadata address. It is absent from both settings files and is read directly, and
/// modelling it would put two binders over one key, which is the same reason the listening endpoint is
/// not modelled either.
/// </para>
/// <para>
/// THE FOUR TOKEN-VALIDATION SWITCHES ARE MODELLED, AND THEY ARE MODELLED SO THEY CAN BE REFUSED. They
/// could be read directly with a safe default, which would let a deployment turn one OFF and the
/// host start healthy with a weakened boundary. Each of the four removes an entire class of
/// forgery, so none is a deployment choice; Program.cs assigns all four unconditionally and the
/// validator refuses a configured <see langword="false"/>. They remain visible here rather than being
/// deleted so that a deployment can still be audited for them by reading its settings file.
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

    // THERE IS DELIBERATELY NO JwksPath MEMBER HERE. See the key-parity block in this file's header
    // for the full reasoning: this service does not compose a key-set address at all. The stock bearer
    // handler configured by AddPersistenceAuthentication fetches the discovery document beneath
    // Authority and follows its published `jwks_uri`, so a key-set setting on this type would be bound,
    // documented and validated while governing nothing. Jwt:MetadataAddress is the one override that is
    // genuinely live, and it is read directly from configuration at the registration site because it
    // shapes the handler rather than this service's own behaviour.

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

    /// <summary>
    /// Whether an inbound credential's issuer is checked. Invariantly <see langword="true"/>; a
    /// configured <see langword="false"/> is refused at startup.
    /// </summary>
    /// <remarks>
    /// <para id="invariant">
    /// THIS SWITCH AND THE THREE BELOW ARE BOUND SO THEY CAN BE AUDITED, NOT SO THEY CAN BE TURNED OFF.
    /// Each removes a whole class of forgery when enabled: without issuer validation a credential from
    /// any issuer is accepted, so Security stops being the sole authority this boundary trusts; without
    /// audience validation a credential minted for another service is replayable here, which is what the
    /// one-audience-per-token rule of contract C-01 exists to prevent; without lifetime validation
    /// Security's short lifetimes bound nothing and a leaked credential is permanent; without
    /// signing-key validation the signature is not verified at all and any well-formed token is
    /// accepted. A host that starts with one of them off is an unauthenticated boundary wearing the
    /// shape of an authenticated one, which constraint C-G forbids. Program.cs therefore assigns all
    /// four literally, and <see cref="PersistenceOptionsValidator"/> refuses a configured
    /// <see langword="false"/> rather than ignoring it in silence - silence being the worse of the two,
    /// because an operator would believe the setting took effect.
    /// </para>
    /// </remarks>
    public bool ValidateIssuer { get; set; } = true;

    /// <summary>
    /// Whether an inbound credential's audience is checked against <see cref="Audience"/>. Invariantly
    /// <see langword="true"/>; a configured <see langword="false"/> is refused at startup.
    /// </summary>
    public bool ValidateAudience { get; set; } = true;

    /// <summary>
    /// Whether an inbound credential's validity window is enforced. Invariantly <see langword="true"/>;
    /// a configured <see langword="false"/> is refused at startup.
    /// </summary>
    public bool ValidateLifetime { get; set; } = true;

    /// <summary>
    /// Whether an inbound credential's signature is verified against the authority's published
    /// material. Invariantly <see langword="true"/>; a configured <see langword="false"/> is refused at
    /// startup.
    /// </summary>
    /// <remarks>
    /// A switch, not a value. It selects whether verification happens; it carries nothing used to
    /// perform it, which arrives from the authority at runtime. This is the only member in this type
    /// whose name contains the word "key", and the distinction is recorded so that a search for that
    /// word lands on an explanation rather than on a suspicion.
    /// </remarks>
    public bool ValidateIssuerSigningKey { get; set; } = true;

    /// <summary>
    /// The default floor between two on-demand key-set refreshes: five seconds.
    /// </summary>
    /// <remarks>
    /// Bounds how long this boundary keeps refusing a correctly signed token after Security rotates its
    /// signing key. Not lower, because this floor is also the only rate limit on the refresh a REJECTED
    /// token provokes. Stated identically on all three verification boundaries, so one rotation converges
    /// at one rate across the estate rather than at three.
    /// </remarks>
    public static readonly TimeSpan DefaultMetadataRefreshInterval = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The default interval at which the cached key set is refreshed even when every token validates:
    /// five minutes, the library's own minimum.
    /// </summary>
    /// <remarks>
    /// The library's default is TWELVE HOURS, and this interval is the only thing that ever drops a
    /// RETIRED key, because a successful validation provokes no refresh.
    /// </remarks>
    public static readonly TimeSpan DefaultMetadataAutomaticRefreshInterval = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The shortest time that must pass before a key-set refresh requested by a failed validation is
    /// actually performed. Defaults to <see cref="DefaultMetadataRefreshInterval"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 <b>ROTATION INVERTS THIS BOUNDARY'S VERDICTS FOR AS LONG AS THIS INTERVAL.</b> When Security
    /// rotates its signing key and key identifier, a token minted BEFORE the rotation keeps validating
    /// against the cached set while one minted AFTER it is refused <c>401</c> with <c>IDX10503</c> naming a
    /// key identifier that matched nothing. The handler requests a refresh on exactly that failure, but the
    /// configuration manager will not fetch again until this interval has elapsed since its last fetch - so
    /// the library's five-minute default made the inversion last minutes.
    /// </para>
    /// <para>
    /// <b>A BOUND RATHER THAN AN OVERLAP, WHICH IS AN AAP CONSTRAINT.</b> The alternative is for the issuer
    /// to publish the superseded key beside the new one until consumers converge - but AAP 0.6.6.3 fixes
    /// exactly one signing secret in the estate and Security's published set is built from that single key,
    /// so an overlap would add a second slot of issuer key material rather than a setting. The window is
    /// bounded and documented instead: <c>docs/SECRETS.md</c> section 4.2.1 carries the rotation runbook
    /// and <c>docs/ARCHITECTURE.md</c> section 9.6 tabulates both intervals.
    /// </para>
    /// <para>
    /// Read from the same configuration snapshot as the authority it qualifies, for the reason the
    /// composition root records at its bearer callback, and validated against
    /// <see cref="BaseConfigurationManager.MinimumRefreshInterval"/> so a value the configuration manager
    /// would throw on becomes a refusal to start rather than a first-request failure.
    /// </para>
    /// </remarks>
    public TimeSpan MetadataRefreshInterval { get; set; } = DefaultMetadataRefreshInterval;

    /// <summary>
    /// How often the cached key set is refreshed in the background, independently of any validation
    /// failure. Defaults to <see cref="DefaultMetadataAutomaticRefreshInterval"/>.
    /// </summary>
    /// <remarks>
    /// The half of rotation <see cref="MetadataRefreshInterval"/> cannot bound: a token signed by the
    /// RETIRED key still validates against the cached set and a success provokes no refresh, so only this
    /// interval retires it. Validated against
    /// <see cref="BaseConfigurationManager.MinimumAutomaticRefreshInterval"/>.
    /// </remarks>
    public TimeSpan MetadataAutomaticRefreshInterval { get; set; } = DefaultMetadataAutomaticRefreshInterval;

    /// <summary>
    /// The caller identities permitted to reach this service's four contracts. Bound from
    /// <c>Jwt:PermittedCallers</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// AUTHENTICATION IS NOT AUTHORIZATION, AND THIS IS THE SUBJECT HALF OF THE DIFFERENCE. Every contract
    /// protected by "an authenticated user" and nothing more would let any holder of any token minted
    /// for this audience call all four - including a caller with no business here at all. The AAP
    /// fixes the call graph as layered and acyclic: nothing but DataServices calls Persistence. This roster
    /// is that statement made enforceable, and the scope half is enforced alongside it, because either
    /// alone leaves a hole (CWE-862, CWE-863).
    /// </para>
    /// <para>
    /// COMPARED ORDINALLY against the token's subject claim, matching how the issuer compares an identity
    /// everywhere else. The subject is read from <c>sub</c> or, when the bearer handler maps inbound claims,
    /// from the framework's name-identifier claim type - whichever is present.
    /// </para>
    /// <para>
    /// AN EMPTY ROSTER REFUSES EVERY CALLER, and validation requires at least one entry so that state is
    /// unreachable through configuration. That is deliberate: reading an empty list as "permit everyone"
    /// would be a fail-open default, which is the exact shape of the defect this setting closes.
    /// </para>
    /// <para>
    /// IDENTITIES ONLY. There is no member here that could hold a certificate, a key or a secret - the
    /// token's signature establishes that the subject is genuine, and this only records which subjects are
    /// welcome.
    /// </para>
    /// <para>
    /// EMPTY BY DEFAULT, AND THE SETTINGS FILE SUPPLIES THE VALUE - which is not a stylistic choice. The
    /// configuration binder POPULATES an existing collection rather than replacing it, so a non-empty
    /// default would ACCUMULATE with whatever a deployment declares: an operator narrowing the roster to
    /// one identity would silently still permit the built-in one as well. An empty default plus a required
    /// minimum length makes the declared value the whole value, and makes a deployment that forgets it fail
    /// to start rather than run on an invisible built-in permission.
    /// </para>
    /// </remarks>
    [MinLength(1)]
    public IList<string> PermittedCallers { get; } = [];
}


// --------------------------------------------------------------------------------------------------
// GROUP 5 OF 5 - THE INTERNAL TRUST ANCHOR, WHICH IS A PATH AND NEVER MATERIAL
// --------------------------------------------------------------------------------------------------

/// <summary>
/// The trust anchor internal TLS is verified against. Bound from the top-level <c>InternalTls</c>
/// section. One path, and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// A ROOT CERTIFICATE IS PUBLIC MATERIAL, SO THE PATH IS NOT ABOUT CONFIDENTIALITY. It is that the
/// anchor is a DEPLOYMENT artefact - one local authority per environment, rotated on its own schedule,
/// mounted read-only from the orchestration layer. Embedding one in a settings file would pin every
/// environment to a single authority and make rotation a code change.
/// </para>
/// <para>
/// ONE MEMBER, AND NO KEY PATH. Verifying a chain needs only the public root. A trust anchor with a
/// private key beside it would mean this service could ISSUE certificates for the internal topology,
/// which is a capability the sole-issuer topology forbids it (constraint C-G).
/// </para>
/// </remarks>
public sealed class InternalTlsTrustOptions
{
    /// <summary>
    /// Path to the PEM-encoded certificate authority bundle Security's certificate is verified against.
    /// Empty means platform default trust.
    /// </summary>
    /// <remarks>
    /// The file may carry one certificate or a concatenated chain of them; every certificate it carries
    /// becomes an acceptable root for internal traffic, and nothing else does.
    /// </remarks>
    public string TrustedCaPath { get; set; } = string.Empty;

    /// <summary>
    /// Whether this deployment narrows internal trust to a mounted anchor.
    /// </summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(TrustedCaPath);
    /// <summary>
    /// How the revocation status of an internal peer's certificate is checked. One of
    /// <see cref="RevocationModes.NoCheck"/>, <see cref="RevocationModes.Offline"/> or
    /// <see cref="RevocationModes.Online"/>, compared case-insensitively.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 <b>CONFIGURABLE, AND THE SHIPPED DEFAULT IS THE ONLY VALUE THE DOCUMENTED TOPOLOGY CAN ANSWER.</b>
    /// This was a hardcoded <c>NoCheck</c>, which meant a deployment whose authority DOES publish revocation
    /// information had no way to ask for it and a stolen peer certificate stayed acceptable until it expired
    /// (CWE-295). It is a setting now. What it is NOT is a setting whose default can be the strict value:
    /// the local authority the documented recipe generates publishes no distribution point and runs no
    /// responder, and that was MEASURED rather than assumed - building a chain for a leaf it issued under
    /// <c>CustomRootTrust</c> succeeds under <c>NoCheck</c> and FAILS under both <c>Offline</c> and
    /// <c>Online</c> with <c>RevocationStatusUnknown | OfflineRevocation</c>. Shipping a strict default
    /// would therefore refuse every internal peer on a clean bring-up and hold every dependent behind an
    /// unsatisfiable health gate.
    /// </para>
    /// <para>
    /// AN INDETERMINATE STATUS IS A REFUSAL UNDER THE STRICTER MODES, NEVER A PASS. The chain policy sets no
    /// verification flag that ignores a revocation failure, so a deployment that selects <c>Offline</c> or
    /// <c>Online</c> gets a genuine check whose unknown answer refuses the peer - which is the only reading
    /// under which selecting the mode means anything at all.
    /// </para>
    /// <para>
    /// WHAT SUBSTITUTES FOR REVOCATION WHILE THIS IS <c>NoCheck</c> IS CERTIFICATE LIFETIME, and the
    /// documented issuance recipe is <c>-days 30</c> for both the authority and every leaf. The operational
    /// surfaces state the production recommendation - issue from an authority that publishes a distribution
    /// point or a responder and set this to <c>Online</c> - and the emergency procedure for a compromise
    /// under <c>NoCheck</c>, which is to replace the anchor and restart rather than to revoke.
    /// </para>
    /// </remarks>
    [Required(AllowEmptyStrings = false)]
    public string RevocationMode { get; set; } = RevocationModes.NoCheck;

    /// <summary>
    /// Resolves <see cref="RevocationMode"/> to the platform value the chain policy is built with.
    /// </summary>
    /// <param name="configurationKeyPrefix">
    /// The configuration path of this group, quoted into the failure so an operator can find the offending
    /// key without reading source.
    /// </param>
    /// <returns>The resolved mode.</returns>
    /// <exception cref="InvalidOperationException">
    /// The configured value names no recognised mode. Structural, and therefore fatal: guessing a mode
    /// would either silently weaken the check a deployment asked for or silently refuse every peer, and
    /// both are worse than not starting. The value IS quoted, because a mode name is not a secret and an
    /// operator fixing a typo needs to see what was read.
    /// </exception>
    internal X509RevocationMode ResolveRevocationMode(string configurationKeyPrefix)
    {
        string configured = RevocationMode?.Trim() ?? string.Empty;

        if (RevocationModes.Matches(configured, RevocationModes.NoCheck))
        {
            return X509RevocationMode.NoCheck;
        }

        if (RevocationModes.Matches(configured, RevocationModes.Offline))
        {
            return X509RevocationMode.Offline;
        }

        if (RevocationModes.Matches(configured, RevocationModes.Online))
        {
            return X509RevocationMode.Online;
        }

        throw new InvalidOperationException(
            $"'{configurationKeyPrefix}:{nameof(RevocationMode)}' is set to '{RevocationMode}', which "
                + "names no recognised revocation posture, so this service will not start. Set one of "
                + $"{string.Join(", ", RevocationModes.Recognised)}, or remove the key to accept the "
                + "default. Note that the stricter two require an authority that publishes a certificate "
                + "revocation list or runs a responder: against one that does not, every peer is refused "
                + "with an indeterminate revocation status, which is a refusal by design.");
    }

    /// <summary>The revocation postures this group accepts.</summary>
    /// <remarks>
    /// Declared here rather than as loose strings so that the settings file, the validator, the resolver
    /// and the failure message cannot spell them three different ways.
    /// </remarks>
    public static class RevocationModes
    {
        /// <summary>No revocation check is performed. The shipped default.</summary>
        public const string NoCheck = "NoCheck";

        /// <summary>Only cached revocation information is consulted.</summary>
        public const string Offline = "Offline";

        /// <summary>Revocation information is fetched from the authority.</summary>
        public const string Online = "Online";

        /// <summary>Every accepted spelling, in the order a failure message lists them.</summary>
        public static IReadOnlyList<string> Recognised { get; } = [NoCheck, Offline, Online];

        /// <summary>Reports whether a configured value is recognised.</summary>
        /// <param name="candidate">The configured value, which may be <see langword="null"/>.</param>
        /// <returns><see langword="true"/> when it names a mode.</returns>
        public static bool IsRecognised(string? candidate) =>
            candidate is not null
                && Recognised.Any(mode => Matches(candidate.Trim(), mode));

        /// <summary>Compares a configured value against one mode, case-insensitively.</summary>
        /// <param name="candidate">The configured value.</param>
        /// <param name="mode">The mode to compare against.</param>
        /// <returns><see langword="true"/> when they name the same mode.</returns>
        internal static bool Matches(string candidate, string mode) =>
            string.Equals(candidate, mode, StringComparison.OrdinalIgnoreCase);
    }

}

/// <summary>
/// The bounds on server-held work handles: the per-caller and total ceilings on live handles, how long an
/// untouched handle survives, and how often abandoned ones are looked for.
/// </summary>
/// <remarks>
/// <para>
/// EVERY MEMBER IS A PROPERTY OF THE BOUNDARY AND NONE PORTS A LEGACY SETTING (constraints C-B, C-K). The
/// legacy has no analogue to look up, because it had no handles: a caller held a task and a transaction by
/// reference, and an abandoned one was collected when the reference left scope. There is therefore no
/// oracle value to preserve here, which is precisely why each default below is justified in its own
/// remarks rather than cited to a locator - a citation would be a fabrication.
/// </para>
/// <para>
/// THE CEILINGS ARE REFUSALS, NOT EVICTIONS. Reaching one refuses the CREATION of a new handle with
/// <c>RetCode.E_BUSY</c>; it never takes a handle away from the caller that already holds one.
/// Evicting a live handle to admit a new one would let one caller destroy another's in-flight work, which
/// is a worse failure than refusing the newcomer - and <c>E_BUSY</c> is a code the legacy already uses for
/// "not now", so no new value enters a consumer's branch set.
/// </para>
/// <para>
/// THE EXPIRY IS A SWEEP, NOT A TIMER PER HANDLE. One periodic pass costs one clock read and one pass over
/// four tables; a timer per handle would allocate one per create and fire on the thread pool at a moment
/// nothing correlates with. The sweep interval is therefore the RESOLUTION of the expiry rather than a
/// second policy: a handle survives its idle window plus up to one interval.
/// </para>
/// </remarks>
public sealed class HandleLifecycleOptions
{
    /// <summary>The ceiling on live handles in one registry when nothing is configured.</summary>
    /// <remarks>
    /// FIVE HUNDRED AND TWELVE, chosen as a bound that no legitimate deployment of this topology reaches
    /// and every runaway does. Only DataServices calls this service (the call graph is layered and
    /// acyclic), and it acquires one session and one task per operation and releases both - so the live
    /// count tracks CONCURRENT operations, not cumulative ones. A number this size is therefore
    /// unreachable by correct use and reached quickly by a caller that never releases, which is exactly
    /// the discrimination a ceiling exists to make.
    /// </remarks>
    public const int DefaultMaxTotalPerRegistry = 512;

    /// <summary>The ceiling on live handles held by one caller identity when nothing is configured.</summary>
    /// <remarks>
    /// A QUARTER OF THE TOTAL, so that one runaway caller cannot consume the whole registry and starve the
    /// others. With a single legitimate caller today this ceiling is the operative one and the total is the
    /// backstop; the ordering is deliberate, because the per-caller refusal names the caller at fault
    /// whereas a total refusal names only the service.
    /// </remarks>
    public const int DefaultMaxPerPrincipal = 128;

    /// <summary>The idle lifetime of an untouched handle when nothing is configured, in seconds.</summary>
    /// <remarks>
    /// NINE HUNDRED SECONDS - fifteen minutes - which is far longer than any operation this contract
    /// declares can legitimately take and far shorter than the process lifetime an abandoned handle would
    /// otherwise get. It is deliberately generous: reclaiming a handle a caller still intends to use is a
    /// worse fault than holding an abandoned one for a quarter of an hour, and the reclaim path
    /// additionally refuses to touch a retrieval that is provably in flight.
    /// </remarks>
    public const int DefaultIdleExpirySeconds = 900;

    /// <summary>How often abandoned handles are looked for when nothing is configured, in seconds.</summary>
    /// <remarks>
    /// SIXTY SECONDS, which makes the reclaim's resolution one minute against a fifteen-minute window -
    /// coarse enough that the sweep is free and fine enough that the window means what it says.
    /// </remarks>
    public const int DefaultSweepIntervalSeconds = 60;

    /// <summary>
    /// The maximum number of live handles one registry may hold. Defaults to
    /// <see cref="DefaultMaxTotalPerRegistry"/>.
    /// </summary>
    /// <remarks>
    /// PER REGISTRY RATHER THAN ACROSS ALL FOUR, because the four hold different things at different
    /// costs and one shared counter would let a flood of query tasks refuse a session that a correct
    /// caller needs. The four registries are the transaction sessions, the query tasks, the update tasks
    /// and the command tasks.
    /// </remarks>
    [Range(1, int.MaxValue, ErrorMessage = "must be at least 1.")]
    public int MaxTotalPerRegistry { get; set; } = DefaultMaxTotalPerRegistry;

    /// <summary>
    /// The maximum number of live handles in one registry attributable to a single caller identity.
    /// Defaults to <see cref="DefaultMaxPerPrincipal"/>.
    /// </summary>
    /// <remarks>
    /// THE IDENTITY IS THE TOKEN'S SUBJECT, and every inbound call carries one because every contract on
    /// this service requires an authenticated principal. A call that somehow reaches a registry with no
    /// identity is attributed to a single reserved bucket rather than exempted, so an unattributed flood
    /// is bounded too.
    /// </remarks>
    [Range(1, int.MaxValue, ErrorMessage = "must be at least 1.")]
    public int MaxPerPrincipal { get; set; } = DefaultMaxPerPrincipal;

    /// <summary>
    /// How long a handle survives without being named by any call, in SECONDS. Defaults to
    /// <see cref="DefaultIdleExpirySeconds"/>.
    /// </summary>
    /// <remarks>
    /// THE CLOCK BEHIND IT IS THE INJECTED <see cref="TimeProvider"/> AND NOTHING ELSE, so a
    /// characterization run and a unit test both drive expiry deterministically. Every handle's activity
    /// stamp is refreshed each time a call resolves it, so a handle in active use is never near expiry.
    /// </remarks>
    [Range(1, int.MaxValue, ErrorMessage = "must be at least 1 second.")]
    public int IdleExpirySeconds { get; set; } = DefaultIdleExpirySeconds;

    /// <summary>
    /// How often the reclaim pass runs, in SECONDS. Defaults to
    /// <see cref="DefaultSweepIntervalSeconds"/>.
    /// </summary>
    [Range(1, int.MaxValue, ErrorMessage = "must be at least 1 second.")]
    public int SweepIntervalSeconds { get; set; } = DefaultSweepIntervalSeconds;

    /// <summary>The idle lifetime as a <see cref="TimeSpan"/>.</summary>
    public TimeSpan IdleExpiry => TimeSpan.FromSeconds(IdleExpirySeconds);

    /// <summary>The sweep interval as a <see cref="TimeSpan"/>.</summary>
    public TimeSpan SweepInterval => TimeSpan.FromSeconds(SweepIntervalSeconds);
}


// --------------------------------------------------------------------------------------------------
// GROUP 6 - SCHEMA PROVISIONING: ONE SWITCH, ADDITIVE ONLY, OFF BY DEFAULT
// --------------------------------------------------------------------------------------------------

/// <summary>
/// Whether this deployment applies the pending migrations when the process starts. Bound from the
/// top-level <c>Schema</c> section. One member, and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// WHY THIS SECTION EXISTS - THE ONE-COMMAND BRING-UP WAS NOT ACHIEVABLE WITHOUT IT. This service
/// deliberately creates no schema of its own, so on a FRESH <c>persistence-db</c> volume its readiness
/// probe reports the <c>COMPANY</c> table absent for ever and the Compose health condition holds
/// DataServices and Gateway back behind it. The documented bring-up is a single command
/// (<c>docker compose --env-file .env up --build -d</c>, constraints C-J and C-L) and it could not reach
/// a healthy stack: an operator had to run <c>dotnet ef database update</c> out of band from a checkout,
/// with the SDK and the <c>dotnet-ef</c> tool installed, neither of which the runtime image carries.
/// A switch here is what closes that gap without adding a fifth container, which C-D forbids.
/// </para>
/// <para>
/// THE DEFAULT IS <see langword="false"/>, AND THAT IS THE LOAD-BEARING CHOICE RATHER THAN A TIMID ONE.
/// Three things depend on it. A parity run must be able to rely on the volume being untouched between the
/// legacy-side and target-side captures of one workflow identifier (AAP 0.6.7), so the schema step must be
/// something a characterization operator switches ON deliberately and can leave off. Every existing
/// deployment and every service-level test boots this same composition root, so an opt-out default is
/// what keeps their behaviour identical to before this section existed. And a service that mutates its
/// own storage on every restart, unasked, is exactly the surprise the fail-fast posture is meant to avoid.
/// The orchestration manifest turns it ON explicitly, in one place, where an operator reading the
/// bring-up can see it.
/// </para>
/// <para>
/// WHAT THE SWITCH MAY AND MAY NOT DO. It selects <c>Database.Migrate</c> and NOTHING ELSE:
/// no <c>EnsureCreated</c>, no <c>EnsureDeleted</c>, no <c>DROP</c>, no <c>DELETE</c>, no seed and no
/// file removal, anywhere on the path it enables. Migrate is additive and idempotent - it applies the
/// migrations the history table does not already record and does nothing at all when there are none - so
/// turning it on cannot destroy or reseed a volume even mid-capture. <c>SchemaProvisioner</c> holds that
/// path, and its own suite asserts the absence of every destructive construct by scanning its source.
/// </para>
/// <para>
/// NO LEGACY ANALOGUE EXISTS FOR THIS MEMBER, and saying so is the honest position rather than an
/// omission (constraints C-B, C-K). The legacy library has no schema step at all: the one DDL statement in
/// the estate sits inside a test window's button handler
/// [<c>ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L463-L469</c>], which is harness setup and not
/// framework behaviour. So there is no oracle value to preserve here and no locator to cite for the
/// default - the default is justified by the paragraph above instead.
/// </para>
/// </remarks>
public sealed class SchemaOptions
{
    /// <summary>
    /// Whether the pending migrations are applied at startup. Defaults to <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A PLAIN BOOLEAN, WITH BOTH VALUES LEGAL AND NEITHER VALIDATED. There is nothing for a rule to
    /// assert: <see langword="false"/> is the shipped code default and <see langword="true"/> is what the
    /// orchestration manifest sets, and a deployment is entitled to either. The validator therefore checks
    /// this section for having been BOUND and applies no rule to the value, the same treatment
    /// <see cref="TransactionPoolOptions"/> receives and for the same reason.
    /// </para>
    /// <para>
    /// WHEN IT IS ON, FAILURE IS FATAL. An unapplicable migration means this service cannot serve a
    /// retrieval or an update, so the process terminates with a named cause rather than starting and
    /// answering every request with a storage error - the fail-fast posture the oracle's own
    /// <c>HALT CLOSE</c> establishes [<c>ws_objects/pfw.pbl.src/pfw.sra:L143</c>]. WHEN IT IS OFF, nothing
    /// happens at all: no connection is opened, no file is created and no log record beyond one
    /// information line saying the step was skipped and naming the key that enables it.
    /// </para>
    /// </remarks>
    public bool ApplyMigrationsOnStartup { get; set; }
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

        // --- Jwt: presence of both required strings -------------------------------------------------
        path = string.Concat(prefix, "Jwt");
        if (EnsureSectionBound(options.Jwt, path, failures))
        {
            AppendAnnotationFailures(options.Jwt, path, failures);
            AppendDisabledValidationFailures(options.Jwt, path, failures);
            AppendMetadataRefreshFailures(options.Jwt, path, failures);

            // RequireHttpsMetadata is unvalidated by design: false is a legal value that a developer's
            // loopback run legitimately needs, and the defence against it reaching a deployed stack is
            // that it defaults to true and a settings file is reviewed, not that startup refuses it. The
            // four switches above are a different case entirely and are refused rather than reviewed -
            // each removes a class of forgery, and unlike metadata transport there is no topology in
            // which turning one off is legitimate.
        }

        // --- Handles: four positive numbers, plus one relationship between two of them ---------------
        path = string.Concat(prefix, "Handles");
        if (EnsureSectionBound(options.Handles, path, failures))
        {
            AppendAnnotationFailures(options.Handles, path, failures);
            AppendHandleCeilingFailure(options.Handles, path, failures);
        }

        // --- Schema: NO RULE, AND NONE MAY BE ADDED -------------------------------------------------
        // One boolean, both values legal: false is the shipped code default and true is what the
        // orchestration manifest sets so the documented single-command bring-up reaches a healthy stack.
        // A rule here could only refuse one of the two positions a deployment is entitled to hold. The
        // section is therefore checked for having been bound at all, exactly as TransactionPool is - and
        // for the same reason: a null section would be a NullReferenceException on the provisioning path
        // rather than a message naming a key.
        _ = EnsureSectionBound(options.Schema, string.Concat(prefix, "Schema"), failures);

        // --- DataObjects: each entry complete, and no name declared twice -------------------------
        AppendDataObjectFailures(options.DataObjects, string.Concat(prefix, "DataObjects"), failures);

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    /// <summary>
    /// Appends a failure when the per-caller ceiling exceeds the total ceiling.
    /// </summary>
    /// <param name="handles">The bound handle-lifecycle settings.</param>
    /// <param name="configurationPath">The section's configuration path, for the message.</param>
    /// <param name="failures">The failure list to append to.</param>
    /// <remarks>
    /// A PER-CALLER CEILING ABOVE THE TOTAL IS UNREACHABLE, AND AN UNREACHABLE LIMIT IS WORSE THAN NO
    /// LIMIT: an operator reading the settings would believe a per-caller bound applied when the total
    /// would always refuse first, so the per-caller refusal - the one that names the caller at fault -
    /// could never be produced. The two annotations above already refuse a non-positive value in either
    /// member; this is the one rule that is about the PAIR rather than about either number alone.
    /// </remarks>
    private static void AppendHandleCeilingFailure(
        HandleLifecycleOptions handles,
        string configurationPath,
        List<string> failures)
    {
        if (handles.MaxPerPrincipal > handles.MaxTotalPerRegistry)
        {
            failures.Add(string.Format(
                CultureInfo.InvariantCulture,
                "{0}:MaxPerPrincipal must not exceed {0}:MaxTotalPerRegistry, because a per-caller "
                + "ceiling above the total can never be the limit that refuses and an operator would "
                + "believe it applied.",
                configurationPath));
        }
    }

    /// <summary>
    /// Appends a failure per malformed or duplicated data-object definition.
    /// </summary>
    /// <param name="definitions">The declared definitions.</param>
    /// <param name="configurationPath">The section's configuration path, for the messages.</param>
    /// <param name="failures">The failure list to append to.</param>
    /// <remarks>
    /// <para>
    /// AN EMPTY SECTION IS NOT A FAILURE. A deployment whose callers always supply a statement outright
    /// needs no named definition, and refusing to start over an unused capability would be worse than
    /// serving the callers that do not need it. What IS refused is a definition that would resolve and
    /// then misbehave: one with no name, one with no statement, one publishing an empty units value - which
    /// the query task reads as proof the object did not load - and a name declared twice, where which
    /// entry won would be an implementation detail deciding what a caller retrieved.
    /// </para>
    /// <para>
    /// The duplicate check folds case even though resolution is ORDINAL, deliberately: two entries
    /// differing only by case are almost certainly a typo rather than two definitions, and the one a
    /// caller reached would then depend on exactly how it spelled the name. Refusing is the answer that
    /// cannot be got wrong.
    /// </para>
    /// </remarks>
    private static void AppendDataObjectFailures(
        IList<DataObjectOptions> definitions,
        string configurationPath,
        List<string> failures)
    {
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        for (int index = 0; index < definitions.Count; index++)
        {
            DataObjectOptions? definition = definitions[index];
            string entryPath = string.Concat(
                configurationPath,
                ":",
                index.ToString(CultureInfo.InvariantCulture));

            if (definition is null)
            {
                failures.Add(
                    $"Configuration key '{entryPath}' bound to null. A definition entry declared as an "
                    + "explicit null cannot be inspected, so it would resolve nothing while occupying a "
                    + "position in the list. Remove the entry or complete it.");

                continue;
            }

            AppendAnnotationFailures(definition, entryPath, failures);

            if (string.IsNullOrWhiteSpace(definition.Name))
            {
                // The annotation above already named it; nothing further can be said about an entry with
                // no identity, and a duplicate check on a blank name would report a second, misleading
                // failure for the same mistake.
                continue;
            }

            if (!seen.Add(definition.Name.Trim()))
            {
                failures.Add(
                    $"Configuration key '{entryPath}:{nameof(DataObjectOptions.Name)}' repeats a "
                    + "data-object name an earlier entry already declares, ignoring case. One of the two "
                    + "would be unreachable and which one took effect would be an implementation detail "
                    + "deciding what a caller retrieved, so the section is refused rather than merged. "
                    + "The name is deliberately not echoed here.");
            }

            AppendDataObjectColumnFailures(definition, entryPath, failures);
        }
    }

    /// <summary>
    /// Appends one failure per structural defect in a definition's column list and update settings.
    /// </summary>
    /// <param name="definition">The bound definition.</param>
    /// <param name="entryPath">The definition's configuration path, for the messages.</param>
    /// <param name="failures">The failure list to append to.</param>
    /// <remarks>
    /// <para>
    /// EVERY RULE HERE GUARDS A STATEMENT THE UPDATE PATH WOULD OTHERWISE GENERATE WRONG, and each is
    /// refused at startup rather than at the first update because the consequence of each is a statement
    /// that runs successfully against the wrong rows. A duplicated column name makes
    /// <c>&lt;name&gt;.Id</c> ambiguous, so which ordinal a where clause carried would be an
    /// implementation detail. Two identity columns make the identity round trip's first-wins fallback
    /// decide silently which one a caller is told about. An update table with no columns generates a
    /// statement with an empty column list. And an update table with no key column, under any concurrency
    /// mode, generates an UPDATE whose where clause matches EVERY row of the table.
    /// </para>
    /// <para>
    /// A RETRIEVE-ONLY DEFINITION IS FULLY LEGAL and reaches none of these rules: with no update table
    /// declared, the three update-shape rules are skipped and only the per-column rules - which guard the
    /// describe surface generally - apply. No message echoes a column name or a table name.
    /// </para>
    /// </remarks>
    private static void AppendDataObjectColumnFailures(
        DataObjectOptions definition,
        string entryPath,
        List<string> failures)
    {
        string columnsPath = string.Concat(entryPath, ":", nameof(DataObjectOptions.Columns));
        HashSet<string> seenColumns = new(StringComparer.OrdinalIgnoreCase);
        int identityColumns = 0;
        int keyColumns = 0;

        for (int index = 0; index < definition.Columns.Count; index++)
        {
            DataObjectColumnOptions? column = definition.Columns[index];
            string columnPath = string.Concat(
                columnsPath,
                ":",
                index.ToString(CultureInfo.InvariantCulture));

            if (column is null)
            {
                failures.Add(
                    $"Configuration key '{columnPath}' bound to null. A column declared as an explicit "
                    + "null still occupies a DataWindow column ordinal, so every column after it would "
                    + "be numbered one higher than the definition says. Remove the entry or complete it.");

                continue;
            }

            AppendAnnotationFailures(column, columnPath, failures);

            if (string.IsNullOrWhiteSpace(column.Name))
            {
                continue;
            }

            if (!seenColumns.Add(column.Name.Trim()))
            {
                failures.Add(
                    $"Configuration key '{columnPath}:{nameof(DataObjectColumnOptions.Name)}' repeats a "
                    + "column name an earlier column already declares, ignoring case. The update path "
                    + "resolves a column's ordinal from its name, so a repeated name would make which "
                    + "ordinal a where clause carried an implementation detail. The name is deliberately "
                    + "not echoed here.");
            }

            if (column.Identity)
            {
                identityColumns++;
            }

            if (column.Key)
            {
                keyColumns++;
            }
        }

        if (identityColumns > 1)
        {
            failures.Add(
                $"Configuration key '{columnsPath}' declares {identityColumns.ToString(CultureInfo.InvariantCulture)} "
                + "identity columns. A table has at most one, and the identity round trip resolves the "
                + "column with a FIRST-WINS fallback - so a second one would silently decide which value "
                + "a caller is told the database assigned. Mark exactly one column, or none.");
        }

        if (definition.UpdateTable.Trim().Length == 0)
        {
            // Retrieve-only, which is the ordinary case. The three shape rules below are about a
            // statement that will never be generated.
            return;
        }

        if (seenColumns.Count == 0)
        {
            failures.Add(
                $"Configuration key '{entryPath}:{nameof(DataObjectOptions.UpdateTable)}' names an update "
                + $"table while '{columnsPath}' declares no usable column. The update path builds INSERT, "
                + "UPDATE and DELETE statements from the column list, so it would have nothing to write "
                + "and nothing to match on. Declare the columns, or clear the update table to make the "
                + "definition retrieve-only.");
        }
        else if (keyColumns == 0)
        {
            failures.Add(
                $"Configuration key '{columnsPath}' declares no key column while "
                + $"'{entryPath}:{nameof(DataObjectOptions.UpdateTable)}' names an update table. Every "
                + "concurrency mode builds its where clause from the key columns first, so with none an "
                + "UPDATE or DELETE would match every row of the table rather than one. Mark at least one "
                + "column as the key.");
        }

        if (definition.UpdateWhere != UpdateWhereBuilder.KeyAndUpdatableColumnsMode)
        {
            failures.Add(
                $"Configuration key '{entryPath}:{nameof(DataObjectOptions.UpdateWhere)}' declares mode "
                + $"{definition.UpdateWhere.ToString(CultureInfo.InvariantCulture)} on a definition that "
                + "names an update table. The key-and-updateable-columns mode - value "
                + $"{UpdateWhereBuilder.KeyAndUpdatableColumnsMode.ToString(CultureInfo.InvariantCulture)} "
                + "- is the only one whose comparison semantics the legacy tree evidences anywhere, so no "
                + "other mode is modelled and the update path refuses one rather than generating a weaker "
                + "where clause. This is refused at startup rather than at the first update because the "
                + "difference between the modes is WHICH ROWS a statement matches, and a weaker check "
                + "succeeds silently. A retrieve-only definition may declare any mode, because none of it "
                + "is read.");
        }
    }

    /// <summary>
    /// Appends one failure per token-validation switch a deployment has turned off.
    /// </summary>
    /// <param name="jwt">The bound inbound-token group.</param>
    /// <param name="configurationPath">The group's configuration path, for the message.</param>
    /// <param name="failures">The failure list to append to.</param>
    /// <remarks>
    /// <para>
    /// REFUSED RATHER THAN REVIEWED, AND THAT IS THE DIFFERENCE FROM EVERY OTHER BOOLEAN IN THIS FILE.
    /// The transaction-pool and query booleans are legal across their whole domain because their legacy
    /// setters applied no guard, and metadata transport has a legitimate loopback case. These four have
    /// neither property: each removes an entire class of forgery, and there is no topology in which
    /// turning one off is a legitimate deployment choice. Program.cs assigns all four literally, so a
    /// configured <see langword="false"/> would take no effect - and a setting silently ignored is worse
    /// than one honoured, because an operator would believe it applied. Refusing to start states plainly
    /// that the value is neither honoured nor honourable.
    /// </para>
    /// <para>
    /// Each message names the exact key and what the switch protects, so the remedy is a one-line edit
    /// rather than an investigation. No message quotes any configured value other than the boolean
    /// itself.
    /// </para>
    /// </remarks>
    private static void AppendDisabledValidationFailures(
        JwtOptions jwt,
        string configurationPath,
        List<string> failures)
    {
        Append(
            jwt.ValidateIssuer,
            nameof(JwtOptions.ValidateIssuer),
            "a credential minted by any issuer whatsoever would be accepted, so Security would no "
                + "longer be the sole authority this boundary trusts");

        Append(
            jwt.ValidateAudience,
            nameof(JwtOptions.ValidateAudience),
            "a credential minted for a different service would be replayable here, which is precisely "
                + "what the one-audience-per-token rule of contract C-01 exists to prevent");

        Append(
            jwt.ValidateLifetime,
            nameof(JwtOptions.ValidateLifetime),
            "an expired credential would be accepted indefinitely, so the short lifetimes Security "
                + "mints would bound nothing");

        Append(
            jwt.ValidateIssuerSigningKey,
            nameof(JwtOptions.ValidateIssuerSigningKey),
            "the signature would not be verified at all, so any well-formed token would be accepted");

        void Append(bool enabled, string member, string consequence)
        {
            if (enabled)
            {
                return;
            }

            failures.Add(string.Concat(
                configurationPath,
                ":",
                member,
                " is false. This switch is invariant and cannot be turned off: with it disabled, ",
                consequence,
                ". The bearer handler is configured with it enabled regardless of this value, so the "
                    + "setting would not take effect - and a setting that is silently ignored is worse "
                    + "than one that is honoured, which is why the host refuses to start instead. Remove "
                    + "the key or set it to true."));
        }
    }

    /// <summary>
    /// Appends one failure per key-set refresh interval a deployment has set below the floor the token
    /// library itself enforces.
    /// </summary>
    /// <param name="jwt">The bound verification group being validated.</param>
    /// <param name="configurationPath">The group's configuration path, for the message.</param>
    /// <param name="failures">The failure list to append to.</param>
    /// <remarks>
    /// <para>
    /// THE FLOORS ARE READ FROM THE LIBRARY RATHER THAN RESTATED AS LITERALS, so the rule cannot drift
    /// away from the behaviour it guards. <c>BaseConfigurationManager</c> throws
    /// <see cref="ArgumentOutOfRangeException"/> - IDX10107 for the requested-refresh floor and IDX10108
    /// for the background interval - and it throws while the bearer handler builds its configuration
    /// manager, which happens on the FIRST AUTHENTICATED REQUEST rather than at startup. A host that
    /// started healthy and then failed every authenticated call is exactly the shape of fault a startup
    /// check exists to convert into a refusal to start.
    /// </para>
    /// <para>
    /// The third rule is a relationship rather than a range: a requested-refresh floor LONGER than the
    /// background interval makes a rotation converge more slowly for a caller presenting a new token than
    /// for one presenting nothing at all, which inverts the ordering an operator would expect.
    /// </para>
    /// </remarks>
    private static void AppendMetadataRefreshFailures(
        JwtOptions jwt,
        string configurationPath,
        List<string> failures)
    {
        if (jwt.MetadataRefreshInterval < BaseConfigurationManager.MinimumRefreshInterval)
        {
            failures.Add(string.Concat(
                configurationPath,
                ":",
                nameof(JwtOptions.MetadataRefreshInterval),
                " is ",
                jwt.MetadataRefreshInterval.ToString(),
                ", which is below the ",
                BaseConfigurationManager.MinimumRefreshInterval.ToString(),
                " minimum the token library enforces. It would be rejected while the bearer handler ",
                "builds its configuration manager - on the first authenticated request, not at startup, ",
                "so this host would report healthy and then fail every authenticated call."));
        }

        if (jwt.MetadataAutomaticRefreshInterval
            < BaseConfigurationManager.MinimumAutomaticRefreshInterval)
        {
            failures.Add(string.Concat(
                configurationPath,
                ":",
                nameof(JwtOptions.MetadataAutomaticRefreshInterval),
                " is ",
                jwt.MetadataAutomaticRefreshInterval.ToString(),
                ", which is below the ",
                BaseConfigurationManager.MinimumAutomaticRefreshInterval.ToString(),
                " minimum the token library enforces, and would be rejected on the first authenticated ",
                "request rather than at startup."));
        }
        else if (jwt.MetadataRefreshInterval > jwt.MetadataAutomaticRefreshInterval)
        {
            failures.Add(string.Concat(
                configurationPath,
                ":",
                nameof(JwtOptions.MetadataRefreshInterval),
                " is ",
                jwt.MetadataRefreshInterval.ToString(),
                ", which is longer than ",
                nameof(JwtOptions.MetadataAutomaticRefreshInterval),
                " (",
                jwt.MetadataAutomaticRefreshInterval.ToString(),
                "). The first is the floor on a refresh a REJECTED token asks for and the second is the ",
                "background interval, so a floor above it makes a rotation converge more slowly for a ",
                "caller presenting a new token than for one presenting nothing at all."));
        }
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
}
