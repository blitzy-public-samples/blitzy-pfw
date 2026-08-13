// ==============================================================================================
//  TransactionData - the nine-field CONNECTION descriptor that keys the transaction pool
//  --------------------------------------------------------------------------------------------
//  PORTED FROM    ws_objects/pfw.thread.ext.pbl.src/transactiondata.srs:L3-L13
//                     the nine-field PowerBuilder structure this record mirrors, in full
//                 ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:L343-L354
//                     of_settransdata - the INBOUND transfer, its veto arm, and the seven fields
//                 ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:L394-L400
//                     of_gettransdata() - the no-argument overload that DISCARDS the return code
//                 ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:L402-L419
//                     of_gettransdata(ref, ref) - the OUTBOUND transfer and its TWO error checks
//                 ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru:L113-L135
//                     the whole-structure early-out, the autocommit erasure, and the DBParm parse
//                 ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans_pool.sru:L134-L146
//                     of_addref - proof that whole-structure value equality IS the pool key
//                 ws_objects/pfw.shared.pbl.src/retcode.sru:L39, L42, L43
//                     the return codes these accessors produce, consumed and never redeclared
//
//  ORACLE STATUS  Every ws_objects/** path named in this file is READ ONLY (constraint C-C). Each
//                 was read as specification and is cited by locator; nothing here copies,
//                 reformats, moves, edits or deletes any of them, and nothing in the .NET tree
//                 depends on the PowerBuilder toolchain, the PowerBuilder runtime or any shipped
//                 native binary. The legacy tree is the ONLY statement of intended behaviour that
//                 exists for this structure - there is no schema, no changelog entry and no other
//                 document that could adjudicate a disagreement - which is why every behavioural
//                 claim below carries the :L line reference it was taken from.
//
//  WHAT THIS TYPE IS, STATED CORRECTLY
//  --------------------------------------------------------------------------------------------
//  It is the CONNECTION AND CONFIGURATION DESCRIPTOR: the set of values that identifies which
//  database to connect to, with which credentials, under which connection parameters. It is the
//  value the transaction pool keys its reference-counted entries on
//  [n_cst_thread_trans_pool.sru:L138], and the value a SQL task stores and compares against to
//  decide whether a reconnection is needed at all [n_cst_thread_task_sqlbase.sru:L114].
//
//  A NOTE ON THE ORACLE'S OWN COMMENT, SO IT MISLEADS NOBODY ELSE. transactiondata.srs:L2 is a
//  COPY-PASTE DEFECT: its export comment describes the structure as the database-ERROR structure
//  and is BYTE-IDENTICAL to dberrordata.srs:L2. Verified by reading both lines directly. The wrong
//  description is deliberately NOT propagated into any comment or XML document in this file, and
//  the published contract refuses to propagate it either [persistence.v1.proto:L1944-L1948]. The
//  defect itself is left exactly where it is, in a read-only file (C-C). It is cosmetic: no
//  behaviour anywhere depends on an export comment.
//
//  THE STRUCTURE, VERBATIM FROM THE ORACLE [transactiondata.srs:L3-L13]
//  --------------------------------------------------------------------------------------------
//      global type transactiondata from structure
//          string      dbms         [:L4]   ->  string  Dbms
//          string      servername   [:L5]   ->  string  ServerName
//          string      database     [:L6]   ->  string  Database
//          string      logid        [:L7]   ->  string  LogId        credential-adjacent, readable
//          string      logpass      [:L8]   ->  string  LogPass      WRITE-ONLY - see below
//          string      dbparm       [:L9]   ->  string  DbParm       carries the two flags
//          string      lock         [:L10]  ->  string  Lock         isolation level, NOT a monitor
//          boolean     autocommit   [:L11]  ->  bool    AutoCommit   the only non-string field
//          string      userparm     [:L12]  ->  string  UserParm
//      end type
//
//  EXACTLY NINE MEMBERS, IN THIS ORDER, AND THERE IS NO TENTH. EIGHT STRINGS PLUS ONE BOOLEAN,
//  with the BOOLEAN EIGHTH and userparm NINTH - not the other way round, which is the one ordering
//  a reader is likely to get wrong, since a trailing boolean is the more natural C# layout.
//
//  THE ORDER IS A WIRE CONTRACT IN DISGUISE. It is mirrored POSITIONALLY by contract C-08:
//  persistence.v1.proto declares dbms = 1 through userparm = 9 in exactly this sequence
//  [persistence.v1.proto:L1988-L2046]. A "tidier" alphabetical or grouped ordering here would be a
//  silent breaking change, because the two sides are only checkable against each other - and
//  against the oracle - while all three read the same way. The three-way field-order audit was
//  performed and recorded in the FIELD-ORDER AUDIT block further down this file.
//
//  THE TWO CONNECTION FLAGS THAT BELONG CONCEPTUALLY TO DbParm ARE NOT MEMBERS. They are DERIVED
//  from it by the accessors at the bottom of this file, exactly as the legacy derives them
//  [n_cst_thread_task_sqlbase.sru:L127-L132], and the contract likewise attaches them one level up
//  on ConnectionParameterFlags rather than adding a tenth field [persistence.v1.proto:L1804-L1845].
//  Deriving rather than storing is what keeps the nine-field mirror exact.
//
//  ================= LogPass IS WRITE-ONLY. THIS IS THE PRIMARY CONSTRAINT (C-F) ==================
//  It may be SUPPLIED and CONSUMED BY THE CONNECT PATH. It is observable NOWHERE ELSE: THE PROPERTY
//  HAS NO GETTER, so it is never in ToString(), never in a print member, never in a log record,
//  never emitted by a serializer, never returned by an accessor, and it carries no default, no
//  placeholder and no example value anywhere in this file.
//
//  THE MISSING GETTER IS THE LOAD-BEARING PART, AND IT WAS ADDED LAST. Everything else in this
//  section was already true and none of it mattered, because `descriptor.LogPass` was a public read:
//  nothing had to be defeated, a caller could simply ask and then interpolate the answer wherever it
//  liked. Suppressing the DEFAULT rendering paths protects against accident; removing the getter
//  protects against the ordinary case. Two named doors replace it - RevealLogPassForConnect(), which
//  is internal and says at every call site what it is doing, and HasCredential, which answers the
//  presence question without disclosing the answer.
//
//  WHY THE ENFORCEMENT IS STRUCTURAL RATHER THAN CONVENTIONAL, AND WHY IT DECIDED THE TYPE'S SHAPE.
//  A C# record's COMPILER-GENERATED ToString() prints EVERY property, and its generated
//  PrintMembers is the mechanism that does it. That generated printer is the realistic way a
//  credential escapes this system: one interpolated diagnostic, one structured-log scope that
//  formats the descriptor as a single argument, and the password is in a log file. No attribute
//  fixes that - [JsonIgnore] governs serializers and has no effect whatsoever on ToString(). So
//  this type declares BOTH its own ToString() override AND its own PrintMembers, which suppresses
//  generation of both, and neither renders the member. That is also why the nine members are
//  written as INIT-ONLY PROPERTIES rather than as a positional parameter list: a positional record
//  makes suppressing the generated printer awkward and makes the omission easy to reintroduce.
//
//  THE PUBLISHED CONTRACT ENFORCES THE SAME RULE THE SAME WAY, one level up: the request-side
//  TransactionDescriptor HAS the field, and the response-side TransactionDescriptorView SIMPLY
//  DOES NOT CONTAIN IT, with its slot permanently `reserved` [persistence.v1.proto:L1965-L1987 and
//  L2048-L2085]. A response cannot carry the password because there is nowhere on the wire to put
//  it. Grpc/TransactionService.cs therefore treats the field as INBOUND-ONLY when it maps C-08.
//
//  THE LEGACY GENUINELY ROUND-TRIPS IT, WHICH IS WHY THIS MATTERS RATHER THAN BEING THEORETICAL,
//  AND IT IS THE ONE PLACE THIS PORT DOES NOT REPRODUCE A LEGACY LINE. Both outbound accessors move
//  it: `data.LogPass = LogPass` [n_cst_thread_trans.sru:L414], and the no-argument overload
//  delegates to that one [:L397]. THAT LINE IS DELIBERATELY NOT PORTED. Handing a credential back to
//  whoever asked for a descriptor is precisely the echo AAP 0.4.2.6 forbids, and across a service
//  boundary the asker may not be the process that supplied it. So the transfer splits in two:
//    * INBOUND keeps all seven, because that IS the connect path and the credential's whole purpose
//      is to reach the connection [WithConnectionFieldsFrom].
//    * OUTBOUND moves SIX and leaves the caller's own seventh untouched - neither disclosing the
//      connection's password nor destroying the caller's
//      [WithConnectionFieldsFromExcludingCredential].
//  The published contract enforces the identical rule independently, its response-side view having
//  no slot for the field at all [persistence.v1.proto:L1965-L1987], so a port that DID move the
//  value outbound would be contradicting the contract as well as the plan. This is a narrowing of a
//  newly created surface rather than a change to an existing wire format, because there was none:
//  PowerBuilder's move handed a password between two objects inside one process that already held
//  it, and C-08 turns the same accessor into a network response.
//
//  NEVER LOGGED, AND THAT OBLIGATION IS INHERITED. No member of this type is written to a logger
//  from this file - this file takes no logging dependency at all - and the type must never be
//  handed to a logger as a single formatted argument anywhere. That applies to Tasks/, to Grpc/
//  and to Program.cs as much as to this file: `logger.LogDebug("{Descriptor}", transData)` calls
//  ToString(), which is safe here by construction, but a hand-rolled diagnostic that reaches the
//  member directly is not. Two neighbours are covered by the same rule for the reason recorded on
//  each of them: DbParm and UserParm are credential-CAPABLE by the contract's own analysis.
//  ==========================================================================================
//
//  C-B SELF-AUDIT: NOTHING IS ADDED THAT THE LEGACY LACKS, AND NOTHING AWKWARD IS TIDIED
//  --------------------------------------------------------------------------------------------
//  The legacy has nine fields, so this record has nine members. There is deliberately no
//  connection-string property, no timeout, no retry policy, no provider enumeration, no port, no
//  application name and no encryption toggle, however useful any of them would look on a
//  connection descriptor. Each would be a NEW capability rather than a port, which C-B forbids.
//
//  Three preserved oddities are reproduced rather than corrected, each annotated where it happens:
//    1. SEVEN OF NINE. Both legacy accessors move seven fields and touch NEITHER AutoCommit NOR
//       UserParm [n_cst_thread_trans.sru:L345-L351 and :L410-L416]. See WithConnectionFieldsFrom.
//    2. THE VETO TEST IS A LITERAL `= 1`, not the tri-state prevention predicate, so a hook
//       returning 2 does NOT veto [:L343, :L404]. See the veto annotation on each accessor.
//    3. THE OUTBOUND ERROR-STRING CHECK RUNS TWICE, once inside the veto arm and once after it
//       [:L405 and :L408]. Collapsing them changes the outcome for a hook that vetoes AND reports.
//  A fourth is reproduced on the convenience overload: the no-argument getter DISCARDS the return
//  code and hands back the descriptor regardless of failure [:L397].
//
//  C-D RULING FOR THIS FILE: nothing from a deferred service. No XML or JSON parser, no HTTP, no
//  FTP, no WebSocket, no MQTT, no UI, no theming, no script invoker and no dynamic-invocation shim
//  appears here or is reachable from here. The one regular-expression dependency is satisfied by
//  System.Text.RegularExpressions in the BCL, and the legacy `regexpfind` binding is confirmed NOT
//  a library dependency to port (AAP 0.2.1.4).
//
//  C-E RULING FOR THIS FILE: no database is fabricated and none is opened. This descriptor may
//  CARRY a SQL Server or Oracle DBMS string, because the legacy structure can hold one, but this
//  file composes no connection string for any engine, references no provider and performs no I/O.
//  Only SQLite is provisioned in this phase and its URI grammar belongs to
//  Data/SqliteConnectionFactory.cs.
//
//  C-A RULING FOR THIS FILE: intra-project dependencies are ZERO by design. This file compiles
//  before Configuration/, Errors/, Tasks/, Grpc/ and Program.cs exist, exactly as the sibling
//  Errors/DbErrorData.cs does, and it references only the BCL and PowerFramework.Shared.Kernel. No
//  peer service is referenced; the published contracts project is the only sanctioned cross-service
//  edge in this system and no behavioural code crosses a service boundary.
//
//  C-F SELF-AUDIT: no credential, key, token, password, connection string, certificate or
//  secret-shaped placeholder appears anywhere in this file, in any comment, default, literal or
//  example. That exclusion is deliberately total: no conventional stand-in word, no change-me
//  prompt, no commented-out sample, and no well-known administrator account name either. The only
//  string literals this file contains are the two flag patterns, the single digit they are compared
//  against, and the separators the redacted renderer appends. Every string member's only default is
//  the empty string, and every value a deployment actually uses arrives through an options type
//  bound from the orchestration secret layer.
//
//  RULES POSITION
//  --------------------------------------------------------------------------------------------
//  review_rules returns exactly one line, "No user rules provided.", so NO user-specified rule
//  governs this file. That is a finding, not latitude, and nothing is invented or back-filled from
//  convention in its place. The enterprise-standard baseline applies instead - nullable reference
//  types on, warnings as errors, no secret in source, deterministic and trivially testable - and
//  the binding constraints are the refactor plan's own non-rule inventory, of which C-A, C-B, C-C,
//  C-D, C-E, C-F, C-H and C-K bite on this file and are each discharged at the point they are
//  cited above.
//
//  No performance property is asserted anywhere in this file and no decision here is justified by
//  one: the repository publishes no latency budget, no throughput target and no availability
//  commitment, so there is no baseline against which such a claim could be made. The type is a
//  small readonly value struct because the legacy artifact is a structure, not because a
//  measurement said so.
// ==============================================================================================

using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

using PowerFramework.Shared.Kernel;

namespace PowerFramework.Persistence.Transactions;

/// <summary>
/// The caller-supplied hook consulted before a descriptor is applied to a connection target - the
/// port of the legacy <c>Event OnSetTransData(data)</c>
/// [ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:L343].
/// </summary>
/// <param name="data">
/// The descriptor about to be applied. Passed <see langword="in"/> because the legacy parameter is
/// declared <c>readonly</c> [:L343], and AAP 0.4.5.2 maps a <c>readonly</c> PowerScript parameter
/// onto an <see langword="in"/> parameter.
/// </param>
/// <returns>
/// <c>1</c> to VETO the transfer, in which case the assignment is skipped entirely and
/// <see cref="RetCode.OK"/> is returned. ANY OTHER VALUE, including <c>0</c> and including
/// <c>2</c>, allows the transfer to proceed.
/// </returns>
/// <remarks>
/// <para>
/// Modelled as a delegate the caller supplies rather than as a C# <see langword="event"/> or as a
/// subscription on the shared event broker, deliberately - and note the choice is on the merits, not
/// on availability: this service does reference PowerFramework.Shared.Eventful, for the threading
/// broker the task proxies dispatch through. The legacy construct here is a PowerBuilder user event
/// on the transaction object with at most one implementation, so a single optional callback is the
/// faithful shape; a multicast <see langword="event"/> or a broker topic would each introduce a
/// subscriber-ordering question the legacy does not have.
/// </para>
/// <para>
/// <b>A hook that is not supplied behaves exactly as an unimplemented PowerBuilder event.</b> An
/// <c>Event</c> call with no script returns <c>0</c>, so <see langword="null"/> means "no veto"
/// rather than "veto" or "error". Passing <see langword="null"/> is therefore the normal case, not
/// a degenerate one.
/// </para>
/// </remarks>
public delegate long SetTransactionDataHook(in TransactionData data);

/// <summary>
/// The caller-supplied hook consulted before a descriptor is read back out of a connection target
/// - the port of the legacy <c>Event OnGetTransData(ref data, ref errInfo)</c>
/// [ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:L404].
/// </summary>
/// <param name="data">
/// The caller's descriptor, which the hook MAY populate or amend. Passed by
/// <see langword="ref"/> because the legacy passes it <c>ref</c> [:L404], so a hook that vetoes
/// can still leave its own values in place - and those values survive, since the vetoed path skips
/// only the framework's own seven-field copy.
/// </param>
/// <param name="errInfo">
/// The diagnostic slot, cleared to <see cref="string.Empty"/> before the hook runs [:L402]. A hook
/// reports failure by writing a non-empty string here; the value it writes is inspected TWICE, on
/// the vetoed path and again on the allowed path [:L405, :L408].
/// </param>
/// <returns>
/// <c>1</c> to VETO the copy. Any other value allows it. See
/// <see cref="TransactionData.GetTransactionData(ref TransactionData, ref string, GetTransactionDataHook?)"/>
/// for the full four-way outcome matrix, which depends on this value AND on
/// <paramref name="errInfo"/> together.
/// </returns>
/// <remarks>
/// <para>
/// <paramref name="errInfo"/> is declared as a non-nullable <see cref="string"/> by
/// <see langword="ref"/>, so an implementation is steered towards
/// <see cref="string.Empty"/> rather than <see langword="null"/> for "no error". The consuming
/// accessor nonetheless treats <see langword="null"/> as "no error", because that is what
/// PowerBuilder does: comparing a null string with <c>&lt;&gt; ""</c> yields null, and PowerBuilder
/// evaluates a null condition as false, so a null diagnostic does NOT trigger the failure arm at
/// [:L405] or [:L408].
/// </para>
/// <para>
/// A hook must not retain the <see langword="ref"/> it is given beyond the call. The descriptor is
/// a value type held in the caller's storage; capturing a reference to it is not expressible in C#
/// and copying it out is the intended way to keep a snapshot.
/// </para>
/// </remarks>
public delegate long GetTransactionDataHook(ref TransactionData data, ref string errInfo);

/// <summary>
/// The in-process CONNECTION AND CONFIGURATION DESCRIPTOR: nine values identifying which database
/// to connect to, with which credentials, under which connection parameters. This is the port of
/// the PowerBuilder structure <c>transactiondata</c>
/// [ws_objects/pfw.thread.ext.pbl.src/transactiondata.srs:L3-L13].
/// </summary>
/// <remarks>
/// <para>
/// <b>A <see langword="readonly record struct"/> because the legacy artifact is a structure.</b>
/// PowerBuilder structures are value types with value semantics, so a value type reproduces them
/// without ceremony, and the compiler-generated value equality is not a convenience here but a
/// requirement - see the equality note below. The sibling <c>Errors/DbErrorData.cs</c> made the
/// same choice for <c>dberrordata.srs</c>, and the two read consistently on purpose.
/// </para>
/// <para>
/// <b>The nine members are declared in the ORACLE'S OWN ORDER, and that order is contract.</b>
/// Contract C-08 mirrors it positionally, declaring <c>dbms = 1</c> through <c>userparm = 9</c>
/// [persistence.v1.proto:L1988-L2046]. Reordering them here - alphabetically, or by grouping the
/// credential-bearing members together, or by moving the lone boolean last where a C# author would
/// naturally put it - would desynchronize this type, the wire contract and the oracle
/// simultaneously, and nothing in the build would notice.
/// </para>
/// <para>
/// <b>EQUALITY IS LOAD-BEARING, AND IT COVERS ALL NINE MEMBERS INCLUDING
/// <see cref="LogPass"/>.</b> Two independent legacy sites compare whole descriptors by value: the
/// transaction pool uses one as its reference-counting KEY,
/// <c>if _transactions[index].TransData = TransData then</c>
/// [n_cst_thread_trans_pool.sru:L138], and a SQL task short-circuits reconnection with
/// <c>if _transData = transData then return RetCode.OK</c>
/// [n_cst_thread_task_sqlbase.sru:L114]. Two descriptors that differ ONLY in their password are
/// therefore different connection identities and must NOT compare equal - collapsing them would
/// hand a caller a pooled connection opened under someone else's credentials.
/// </para>
/// <para>
/// <b><see cref="GetHashCode"/> incorporates <see cref="LogPass"/> too, and that is correct rather
/// than a leak.</b> A hash is a lossy, non-invertible digest, not a disclosure: it has to include
/// the member precisely because equality does, or equal-hash-implies-equal-value would fail and
/// every hashed lookup keyed on a descriptor would break. The rule this type enforces is that the
/// password is never RENDERED or EMITTED, which is a different thing from never being READ
/// internally.
/// </para>
/// <para>
/// <b>Both are COMPILER-GENERATED, deliberately, rather than hand-written.</b> A hand-written
/// <see cref="Equals(TransactionData)"/> would have to enumerate nine members and would silently
/// stop being complete the moment anyone added a tenth; the generated implementation cannot forget
/// a member, because it is derived from the type's fields. It compares FIELDS rather than
/// properties, which is exactly why the canonicalising backing fields below matter - they are what
/// make every spelling of the cleared state one equivalence class.
/// </para>
/// <para>
/// <b>String comparison is ORDINAL, which matches the oracle.</b> The generated equality uses
/// <see cref="EqualityComparer{T}.Default"/>, and PowerBuilder's <c>=</c> on strings is likewise
/// case-sensitive and culture-independent. A case-insensitive descriptor comparison would merge
/// connection identities the legacy keeps apart.
/// </para>
/// <para>
/// <b>FIELD-ORDER AUDIT, performed and recorded as required.</b> All three declarations were placed
/// side by side member for member and agree exactly:
/// </para>
/// <code>
///   #   transactiondata.srs      persistence.v1.proto C-08     this type
///   1   string  dbms       L4    string dbms       = 1         string Dbms
///   2   string  servername L5    string servername = 2         string ServerName
///   3   string  database   L6    string database   = 3         string Database
///   4   string  logid      L7    string logid      = 4         string LogId
///   5   string  logpass    L8    string logpass    = 5         string LogPass    (write-only)
///   6   string  dbparm     L9    string dbparm     = 6         string DbParm
///   7   string  lock       L10   string lock       = 7         string Lock
///   8   boolean autocommit L11   bool   autocommit = 8         bool   AutoCommit
///   9   string  userparm   L12   string userparm   = 9         string UserParm
/// </code>
/// <para>
/// <b>THE ASSERTIONS THE SIBLING TEST PROJECT MUST MAKE AGAINST THIS TYPE.</b> Verification of the
/// write-only rule is mandatory and must be a test rather than a claim, so
/// <c>PowerFramework.Persistence.Tests</c> - which reaches this assembly's internal seams through
/// the <c>InternalsVisibleTo</c> item in the project file - asserts each of the following. They are
/// listed here so that the obligation travels with the type it constrains:
/// </para>
/// <list type="number">
/// <item>
/// <description>
/// Build a descriptor whose password member holds a distinctive synthetic value, call
/// <see cref="ToString"/>, and assert the returned string does NOT contain that value. Assert the
/// same for a descriptor built through <see cref="WithConnectionFieldsFrom"/>, since that is the
/// operation that MOVES the member.
/// </description>
/// </item>
/// <item>
/// <description>
/// Round-trip the same descriptor through <c>System.Text.Json</c> and assert the serialized
/// document contains neither the member name nor its value - the guarantee that
/// <see cref="JsonIgnoreAttribute"/> provides and that <see cref="ToString"/> alone does not.
/// Assert the same for <see cref="DbParm"/> and <see cref="UserParm"/>.
/// </description>
/// </item>
/// <item>
/// <description>
/// Build two descriptors differing ONLY in the password member and assert they are NOT equal, that
/// their <see cref="GetHashCode"/> results are permitted to differ, and that
/// <see cref="ToString"/> renders the two IDENTICALLY - which is the pair of properties this type
/// exists to hold simultaneously: full participation in equality, zero participation in rendering.
/// </description>
/// </item>
/// </list>
/// <para>
/// The remaining suites those tests carry are the seven-field transfer audit in both directions,
/// the four-cell veto and diagnostic matrix including the case that proves a hook returning
/// <c>2</c> does not veto, and the <see cref="DbParm"/> flag matrix including the nesting case.
/// Each is described on the member it exercises.
/// </para>
/// </remarks>
public readonly partial record struct TransactionData
{
    // ------------------------------------------------------------------------------------------
    //  THE CANONICALISING BACKING FIELDS, AND WHY THEY ARE NOT AUTO-PROPERTIES
    //  ----------------------------------------------------------------------------------------
    //  These eight fields exist to make ONE state - the cleared descriptor - have exactly ONE
    //  representation, so that every spelling of "empty" compares equal to every other. The
    //  mechanism matters: the compiler-generated equality of a record struct compares the type's
    //  FIELDS, not its properties, so two instances agree only if their stored values agree.
    //  Storing null for "empty" and projecting it on read is therefore what makes all of the
    //  following one equivalence class:
    //
    //      default(TransactionData)
    //      TransactionData.Empty
    //      new TransactionData()
    //      new TransactionData { Dbms = "", ServerName = "", Database = "", LogId = "",
    //                            LogPass = "", DbParm = "", Lock = "", UserParm = "" }
    //
    //  Without the canonicalisation the explicit spelling would store empty strings verbatim and
    //  would compare UNEQUAL to the first two - and it would break SILENTLY, because each instance
    //  individually reads back exactly as expected and only the comparison between two
    //  differently-spelled cleared values goes wrong. That comparison is the pool key
    //  [n_cst_thread_trans_pool.sru:L138], so the failure mode is a duplicate pool entry per
    //  spelling rather than an exception.
    //
    //  THERE IS A SECOND, INDEPENDENT REASON, and it applies to every one of the eight. Under the
    //  repository's inherited nullable context a plain non-nullable auto-property on a STRUCT
    //  produces no compiler warning at all, yet `default` still holds a null reference and hands
    //  that null to consumers - a struct's default is all-bits-zero and no accessor runs to prevent
    //  it. The projection below removes that possibility structurally: no string member can ever
    //  observe as null, on any instance, however it was produced. A NullReferenceException while
    //  building a connection string from a descriptor would be a poor place for one.
    //
    //  AutoCommit IS NOT ONE OF THEM, AND THAT IS THE POINT. PowerBuilder initialises an
    //  unassigned `boolean` to false [transactiondata.srs:L11], which is already what a struct's
    //  all-bits-zero default gives a `bool`, so a plain auto-property is exact and a ninth
    //  canonicalising field would be dead weight.
    // ------------------------------------------------------------------------------------------

    /// <summary>Stores <see langword="null"/> for the empty DBMS identifier, projected on read.</summary>
    private readonly string? _dbms;

    /// <summary>Stores <see langword="null"/> for the empty server name, projected on read.</summary>
    private readonly string? _serverName;

    /// <summary>Stores <see langword="null"/> for the empty database name, projected on read.</summary>
    private readonly string? _database;

    /// <summary>Stores <see langword="null"/> for the empty login identifier, projected on read.</summary>
    private readonly string? _logId;

    /// <summary>
    /// Stores <see langword="null"/> for the empty password, projected on read. Participates in
    /// equality and hashing like every other field, and in no rendering path whatsoever.
    /// </summary>
    private readonly string? _logPass;

    /// <summary>Stores <see langword="null"/> for the empty parameter string, projected on read.</summary>
    private readonly string? _dbParm;

    /// <summary>
    /// Stores <see langword="null"/> for the empty isolation-level string, projected on read.
    /// <b>This is the legacy <c>lock</c> STRING</b> [transactiondata.srs:L10] - an isolation-level
    /// name such as the ones a provider accepts - and NOT a synchronization object. Nothing in this
    /// type synchronizes anything; it is an immutable value.
    /// </summary>
    private readonly string? _lock;

    /// <summary>Stores <see langword="null"/> for the empty user parameter, projected on read.</summary>
    private readonly string? _userParm;

    // ------------------------------------------------------------------------------------------
    //  MEMBER 1 of 9 - transactiondata.srs:L4  `string dbms`
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// The DBMS identifier, exactly as the legacy transaction object's <c>DBMS</c> property carries
    /// it [transactiondata.srs:L4].
    /// </summary>
    /// <value>
    /// <para>
    /// <b>This member is also the DIALECT SELECTOR, and the selection rule is a substring test on
    /// its UPPER-CASED text.</b> The legacy resolves it as
    /// <c>if Pos(Upper(DBMS),"ORACLE") &gt; 0 then return DBT_ORACLE else return DBT_MSSQL</c>
    /// [n_cst_thread_trans.sru:L356-L361]. Two consequences follow and both are counter-intuitive
    /// enough to be worth stating: the test is a SUBSTRING match, so any identifier merely
    /// containing the word resolves to Oracle; and EVERYTHING ELSE - including SQLite, which is the
    /// only engine this phase actually provisions - classifies as SQL Server, because there is no
    /// third arm. That is why the published <c>DatabaseType</c> enum has exactly two members and no
    /// SQLite value [persistence.v1.proto:L1732-L1792].
    /// </para>
    /// <para>
    /// <b>The resolution itself is deliberately NOT implemented on this type.</b> It belongs to the
    /// paging layer that consumes it, under <c>Sql/Paging/</c>, where the two dialect strategies and
    /// the <c>case else</c> arm that yields a not-implemented code live together. Duplicating the
    /// substring test here would create two definitions of one rule that could drift apart, and
    /// this type would gain a dependency on the generated contract enums for no behavioural reason.
    /// The rule is documented here because this member is its only input.
    /// </para>
    /// <para>
    /// Reads as non-null, accepts <see langword="null"/> - see the backing-field note above. The
    /// empty identifier observes as <see cref="string.Empty"/>, matching PowerBuilder's
    /// initialisation of an unassigned <c>string</c>.
    /// </para>
    /// </value>
    [AllowNull]
    public string Dbms
    {
        get => _dbms ?? string.Empty;
        init => _dbms = string.IsNullOrEmpty(value) ? null : value;
    }

    // ------------------------------------------------------------------------------------------
    //  MEMBER 2 of 9 - transactiondata.srs:L5  `string servername`
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// The server name, as the legacy transaction object's <c>ServerName</c> property carries it
    /// [transactiondata.srs:L5].
    /// </summary>
    /// <value>
    /// <para>
    /// Opaque to this type: whether it names a host, an instance, a file path or a provider-specific
    /// alias is the provider's business, and the legacy imposes no grammar on it. It is one of the
    /// SEVEN members that move on a transfer [n_cst_thread_trans.sru:L346, :L411], and therefore one
    /// of the values that makes up a connection identity in the pool.
    /// </para>
    /// <para>
    /// Reads as non-null, accepts <see langword="null"/>, on the same terms as
    /// <see cref="Dbms"/>.
    /// </para>
    /// </value>
    [AllowNull]
    public string ServerName
    {
        get => _serverName ?? string.Empty;
        init => _serverName = string.IsNullOrEmpty(value) ? null : value;
    }

    // ------------------------------------------------------------------------------------------
    //  MEMBER 3 of 9 - transactiondata.srs:L6  `string database`
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// The database name, as the legacy transaction object's <c>Database</c> property carries it
    /// [transactiondata.srs:L6].
    /// </summary>
    /// <value>
    /// <para>
    /// One of the SEVEN transferred members [n_cst_thread_trans.sru:L347, :L412]. Note that the
    /// legacy field name shadows nothing here: the member is named after the field, not after any
    /// .NET type.
    /// </para>
    /// <para>
    /// Reads as non-null, accepts <see langword="null"/>, on the same terms as
    /// <see cref="Dbms"/>.
    /// </para>
    /// </value>
    [AllowNull]
    public string Database
    {
        get => _database ?? string.Empty;
        init => _database = string.IsNullOrEmpty(value) ? null : value;
    }

    // ------------------------------------------------------------------------------------------
    //  MEMBER 4 of 9 - transactiondata.srs:L7  `string logid`
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// The login identifier - an ACCOUNT NAME - as the legacy transaction object's <c>LogID</c>
    /// property carries it [transactiondata.srs:L7].
    /// </summary>
    /// <value>
    /// <para>
    /// <b>Credential-adjacent but readable, and that distinction is the contract's, not this file's.</b>
    /// An account name is not a secret, so unlike <see cref="LogPass"/> it appears on the
    /// response-side view of contract C-08 [persistence.v1.proto:L2001-L2006] and is rendered by
    /// <see cref="ToString"/>. It is nonetheless CONFIGURATION and never a literal: it reaches a
    /// service through its options type bound from the orchestration layer.
    /// </para>
    /// <para>
    /// <b>It carries no default value, deliberately (C-F).</b> Not an administrator account name,
    /// not a provider's conventional sysadmin login, not a sample. The cleared state is
    /// <see cref="string.Empty"/> and nothing else, so a misconfigured deployment fails to connect
    /// rather than silently attempting a well-known account.
    /// </para>
    /// <para>
    /// One of the SEVEN transferred members [n_cst_thread_trans.sru:L348, :L413]. Reads as non-null,
    /// accepts <see langword="null"/>.
    /// </para>
    /// </value>
    [AllowNull]
    public string LogId
    {
        get => _logId ?? string.Empty;
        init => _logId = string.IsNullOrEmpty(value) ? null : value;
    }

    // ------------------------------------------------------------------------------------------
    //  MEMBER 5 of 9 - transactiondata.srs:L8  `string logpass`
    //  ----------------------------------------------------------------------------------------
    //  THE WRITE-ONLY MEMBER. Settable, consumable by the connect path, observable nowhere else.
    //  The full rationale is in this file's header; the enforcement is spread across exactly four
    //  places and all four are required:
    //      1. the attribute below            - stops a serializer emitting it
    //      2. ToString()                     - overridden so no generated printer exists
    //      3. PrintMembers(StringBuilder)    - declared so no generated one is synthesized either
    //      4. no logging dependency anywhere in this file
    //  Equality and hashing are NOT on that list, and must not be: see the type-level remarks.
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// The login password [transactiondata.srs:L8]. <b>WRITE-ONLY BY POLICY: supply it, let the
    /// connect path consume it, and never render, log, echo or serialize it.</b>
    /// </summary>
    /// <value>
    /// <para>
    /// <b>Excluded from <see cref="ToString"/> and from the print member.</b> A record's generated
    /// printer emits every property, which is the realistic way a credential escapes a system -
    /// one structured-log scope that formats the descriptor as a single argument is enough. This
    /// type therefore declares its own <see cref="ToString"/> AND its own print member, and neither
    /// renders this value. <see cref="JsonIgnoreAttribute"/> does NOT fix rendering and is not
    /// relied on to: it governs serializers only, and it is applied here IN ADDITION to the
    /// overrides rather than instead of them.
    /// </para>
    /// <para>
    /// <b>Excluded from serialization in BOTH directions, which is deliberate rather than
    /// incidental.</b> The inbound path for this member is contract C-08's protobuf request message
    /// and the options type bound from the orchestration secret layer - never JSON - so suppressing
    /// JSON in both directions costs nothing real and removes a whole class of accident: a
    /// diagnostics endpoint, a cached state dump or a message-broker envelope that happens to
    /// serialize a descriptor cannot emit it. <c>Grpc/TransactionService.cs</c> maps the field as
    /// INBOUND-ONLY, matching the contract, whose response-side view does not contain the field at
    /// all [persistence.v1.proto:L1965-L1987].
    /// </para>
    /// <para>
    /// <b>Included in equality and in hashing, which is required and is not a leak.</b> The
    /// transaction pool keys reference-counted entries on whole-descriptor equality
    /// [n_cst_thread_trans_pool.sru:L138]; two descriptors differing only in this member are
    /// different connection identities, and merging them would hand a caller a connection opened
    /// under other credentials. A hash is a lossy, non-invertible digest, so including the member
    /// discloses nothing while keeping equal-hash-implies-equal-value true.
    /// </para>
    /// <para>
    /// <b>It has no default, no placeholder and no example value anywhere (C-F).</b> The cleared
    /// state is <see cref="string.Empty"/>. No sample appears in this file, in a comment, or
    /// commented out. Reads as non-null, accepts <see langword="null"/>, so a caller may hand the
    /// initializer an unset configuration value without pre-coercing it.
    /// </para>
    /// </value>
    [AllowNull]
    [JsonIgnore]
    public string LogPass
    {
        // NO GETTER. THIS IS THE STRUCTURAL HALF OF "WRITE-ONLY", AND IT IS WHAT WAS MISSING.
        //
        // Everything above this property was already true - the renderer omits it, the print member
        // omits it, the serializer is told to omit it, the wire response has no slot for it - and none
        // of it mattered, because `descriptor.LogPass` was a public read. A caller did not have to
        // defeat any of those mechanisms; it could simply ask, and then interpolate the answer into a
        // string, a log line or a response of its own devising. Suppressing the DEFAULT rendering paths
        // while leaving the value readable protects against accident and not against the ordinary case.
        //
        // AAP 0.4.2.6 is unambiguous: `logpass` is WRITE-ONLY - never echoed in a response, never
        // logged. An init-only property with no getter is that sentence expressed as a type: the value
        // can be supplied and it cannot be observed, and there is no discipline to remember.
        //
        // THE TWO THINGS THAT STILL NEED IT KEEP WORKING, EACH THROUGH A NAMED DOOR:
        //   * The connect path reads it through RevealLogPassForConnect(), which is internal and whose
        //     name states at every call site exactly what is being done and why.
        //   * A caller that needs to know whether one was supplied - to decide between integrated and
        //     credentialed authentication, say - asks HasCredential, which answers the QUESTION without
        //     disclosing the ANSWER.
        //
        // EQUALITY AND HASHING ARE UNAFFECTED, which matters because the transaction pool keys
        // reference-counted entries on whole-descriptor equality [n_cst_thread_trans_pool.sru:L138]. A
        // record's generated Equals and GetHashCode compare FIELDS, not properties, so _logPass still
        // participates and two descriptors differing only in their password remain different connection
        // identities. Removing the getter narrows OBSERVATION, not IDENTITY.
        init => _logPass = string.IsNullOrEmpty(value) ? null : value;
    }

    /// <summary>
    /// Whether a login password was supplied, WITHOUT disclosing it.
    /// </summary>
    /// <value>
    /// <see langword="true"/> when <see cref="LogPass"/> was set to a non-empty value.
    /// </value>
    /// <remarks>
    /// <para>
    /// THE QUESTION WITHOUT THE ANSWER. Deciding between integrated and credentialed authentication, or
    /// reporting that a configuration binding produced nothing, needs to know only whether a password is
    /// present - and that is the only legitimate reason anything ever read the member. Answering the
    /// question directly is what makes the missing getter costless rather than obstructive.
    /// </para>
    /// <para>
    /// A PRESENCE FLAG IS NOT A DISCLOSURE. It reveals one bit that every failed connection already
    /// reveals by its own error, it cannot be inverted, and it is deliberately NOT a length: a length is
    /// a genuine reduction in the work of guessing, and no caller needs one.
    /// </para>
    /// <para>
    /// Safe to render and to serialize, and therefore not excluded from either - the cleared state reads
    /// <see langword="false"/> because the backing field normalises an empty string to
    /// <see langword="null"/>.
    /// </para>
    /// <para>
    /// <b>NAMED <c>HasCredential</c> AND NOT <c>HasLogPass</c>, WHICH IS NOT COSMETIC.</b> This member DOES
    /// serialize, and the type's standing guarantee is that neither the credential's value NOR ITS MEMBER
    /// NAME appears in rendered or serialized output - a document containing <c>"HasLogPass"</c> would
    /// break the second half of that guarantee, and a sibling test would fail, correctly. Choosing a name
    /// that does not contain the member's own name is what lets the flag be freely serializable.
    /// </para>
    /// </remarks>
    public bool HasCredential => _logPass is not null;

    /// <summary>
    /// Reveals the login password to the connect path. <b>The only read of this value in the system.</b>
    /// </summary>
    /// <returns>The password, or <see cref="string.Empty"/> when none was supplied.</returns>
    /// <remarks>
    /// <para>
    /// <b>NAMED FOR ITS ONE PURPOSE SO THAT NO CALL SITE IS AMBIGUOUS.</b> A property read reads as
    /// incidental; <c>RevealLogPassForConnect()</c> reads as a decision, and a reviewer scanning for
    /// credential handling finds every occurrence by searching for one distinctive word. That is the
    /// entire reason it is a method with an awkward name rather than a getter with a comment.
    /// </para>
    /// <para>
    /// <b>INTERNAL, so the value cannot leave this assembly.</b> Opening a connection is Persistence's
    /// own work - it is the only service that holds a storage provider (AAP 0.1.1) - so nothing outside
    /// this assembly has any business reading it. <c>Grpc/TransactionService.cs</c> maps C-08's field as
    /// INBOUND-ONLY, and the response-side view has no slot for it at all
    /// [<c>persistence.v1.proto:L1965-L1987</c>], so there is nowhere outward for a revealed value to go
    /// even inside this assembly.
    /// </para>
    /// <para>
    /// <b>The returned string must not be logged, rendered, stored or echoed</b> - the obligation the
    /// getter used to carry silently now travels with an explicit name. It is also what the INBOUND fold
    /// uses to move the credential onto the connection's own descriptor, which is part of the connect
    /// path and is the one transfer the legacy genuinely performs
    /// [<c>n_cst_thread_trans.sru:L347</c>].
    /// </para>
    /// </remarks>
    internal string RevealLogPassForConnect() => _logPass ?? string.Empty;

    // ------------------------------------------------------------------------------------------
    //  MEMBER 6 of 9 - transactiondata.srs:L9  `string dbparm`
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// The connection parameter string [transactiondata.srs:L9]. <b>This is the carrier of the two
    /// connection flags</b> exposed by <see cref="IsBindDisabled"/> and
    /// <see cref="IsNCharBindingEnabled"/>.
    /// </summary>
    /// <value>
    /// <para>
    /// <b>Treated as credential-CAPABLE, so it is excluded from rendering and from serialization
    /// alongside <see cref="LogPass"/>.</b> That classification is the published contract's own
    /// analysis rather than a local invention: provider parameter grammars routinely admit
    /// credential material - a password keyword, an access token, or an entire nested connection
    /// string - and neither the legacy nor this contract constrains what a caller puts here, so the
    /// response-side view of C-08 reserves the slot permanently and carries the typed flag allowlist
    /// instead [persistence.v1.proto:L2018-L2027, L2054-L2077]. Only the mandatory exclusion of
    /// <see cref="LogPass"/> is a hard requirement on this type; extending the same treatment to
    /// this member is the consistent reading of that requirement, and it is recorded here rather
    /// than left implicit.
    /// </para>
    /// <para>
    /// <b>Kept as an opaque string, verbatim.</b> The legacy stores it whole and extracts the two
    /// flags from it by regular expression rather than parsing it into parts
    /// [n_cst_thread_task_sqlbase.sru:L128-L129]; this port does exactly the same, so a parameter
    /// this service does not understand still reaches the provider unchanged. No canonicalisation,
    /// no reordering, no keyword normalisation and no validation is performed, because performing
    /// any of them would change what the provider receives (C-B).
    /// </para>
    /// <para>
    /// One of the SEVEN transferred members [n_cst_thread_trans.sru:L350, :L415]. Reads as non-null,
    /// accepts <see langword="null"/>.
    /// </para>
    /// </value>
    [AllowNull]
    [JsonIgnore]
    public string DbParm
    {
        get => _dbParm ?? string.Empty;
        init => _dbParm = string.IsNullOrEmpty(value) ? null : value;
    }

    // ------------------------------------------------------------------------------------------
    //  MEMBER 7 of 9 - transactiondata.srs:L10  `string lock`
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// The isolation-level string, as the legacy transaction object's <c>Lock</c> property carries
    /// it [transactiondata.srs:L10].
    /// </summary>
    /// <value>
    /// <para>
    /// <b>A STRING naming an isolation level - not a synchronization primitive.</b> The name is the
    /// oracle's, so it is preserved; the resemblance to the C# <c>lock</c> keyword and to
    /// <c>System.Threading.Lock</c> is coincidental, and nothing in this type synchronizes anything.
    /// The member spelling <c>Lock</c> is legal C# because the keyword is lower-case, and no bare
    /// <c>lock</c> identifier is ever emitted for it.
    /// </para>
    /// <para>
    /// Opaque and provider-specific: the legacy imposes no grammar and validates nothing, and
    /// neither does this port. It is the SEVENTH and last of the transferred members
    /// [n_cst_thread_trans.sru:L351, :L416], and it is carried on the response-side view of C-08
    /// [persistence.v1.proto:L2030-L2031] because an isolation level is not sensitive.
    /// </para>
    /// <para>
    /// Reads as non-null, accepts <see langword="null"/>.
    /// </para>
    /// </value>
    [AllowNull]
    public string Lock
    {
        get => _lock ?? string.Empty;
        init => _lock = string.IsNullOrEmpty(value) ? null : value;
    }

    // ------------------------------------------------------------------------------------------
    //  MEMBER 8 of 9 - transactiondata.srs:L11  `boolean autocommit`
    //  ----------------------------------------------------------------------------------------
    //  EIGHTH, NOT LAST. The lone boolean sits between two strings, which is the one ordering a C#
    //  author is most likely to "tidy" by moving it to the end. It must not move: contract C-08
    //  mirrors this position positionally [persistence.v1.proto:L2037].
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// The session autocommit flag [transactiondata.srs:L11] - the only non-string member, and the
    /// EIGHTH of nine rather than the last.
    /// </summary>
    /// <value>
    /// <para>
    /// <b>A plain <see cref="bool"/>, and it must NOT be conflated with the tri-valued
    /// per-statement autocommit mode.</b> The legacy structure field is a <c>boolean</c>, while the
    /// SQL task layer carries a separate three-valued setting for per-statement commit policy. The
    /// published contract rules explicitly that the two are different domains and must not be
    /// unified [persistence.v1.proto:L1250-L1253], the reason being that the task layer's third
    /// value has no analogue a boolean could express. Typing this member as that enum would import
    /// a state the structure cannot hold.
    /// </para>
    /// <para>
    /// <b>It is NEVER copied by either legacy accessor.</b> Both move seven fields and touch neither
    /// this member nor <see cref="UserParm"/> [n_cst_thread_trans.sru:L345-L351, :L410-L416]. See
    /// <see cref="WithConnectionFieldsFrom"/>, where that asymmetry is reproduced and annotated.
    /// </para>
    /// <para>
    /// <b>And it is FORCIBLY ERASED when a descriptor is handed to a SQL task</b>, which is a
    /// second, independent quirk on the same member: the task assigns
    /// <c>_transData.AutoCommit = false</c> immediately after storing the descriptor
    /// [n_cst_thread_task_sqlbase.sru:L119], under a comment stating that parameters irrelevant to
    /// the connection target are erased [:L118]. So a descriptor's autocommit flag does not survive
    /// into a task, and per-statement commit policy is carried by contract C-07 instead. Preserved,
    /// not corrected (C-B). That erasure is performed by the task layer under <c>Tasks/</c>, not
    /// here: this type stores what it is given.
    /// </para>
    /// <para>
    /// <b>The cleared value is <see langword="false"/> and needs no accessor logic to make it so.</b>
    /// PowerBuilder initialises an unassigned <c>boolean</c> to false, which is already what a
    /// struct's all-bits-zero default gives, so a plain auto-property is exact.
    /// </para>
    /// </value>
    public bool AutoCommit { get; init; }

    // ------------------------------------------------------------------------------------------
    //  MEMBER 9 of 9 - transactiondata.srs:L12  `string userparm`
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// The provider-specific extra parameter string [transactiondata.srs:L12] - NINTH and last.
    /// </summary>
    /// <value>
    /// <para>
    /// <b>Free-form by the legacy's own design, and therefore treated as credential-CAPABLE</b> on
    /// exactly the footing recorded for <see cref="DbParm"/>: it can carry whatever a caller chose
    /// to put in it, so the response-side view of C-08 reserves its slot permanently
    /// [persistence.v1.proto:L2042-L2044, L2061-L2063], and this type excludes it from rendering and
    /// from serialization.
    /// </para>
    /// <para>
    /// <b>The second of the two members NEITHER legacy accessor copies</b>
    /// [n_cst_thread_trans.sru:L345-L351, :L410-L416]. It is excluded on the same footing as
    /// <see cref="AutoCommit"/> - it is not part of the connection identity the pool keys on - and
    /// <see cref="WithConnectionFieldsFrom"/> reproduces that.
    /// </para>
    /// <para>
    /// Reads as non-null, accepts <see langword="null"/>.
    /// </para>
    /// </value>
    [AllowNull]
    [JsonIgnore]
    public string UserParm
    {
        get => _userParm ?? string.Empty;
        init => _userParm = string.IsNullOrEmpty(value) ? null : value;
    }

    // ------------------------------------------------------------------------------------------
    //  THE CLEARED VALUE
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// The cleared descriptor: all eight string members empty and <see cref="AutoCommit"/>
    /// <see langword="false"/>. This is the port of the legacy idiom of declaring a fresh
    /// <c>TRANSACTIONDATA</c> and using it as-is, which is what the no-argument getter does before
    /// filling it [n_cst_thread_trans.sru:L395].
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately defined as <see langword="default"/> rather than as an explicitly constructed
    /// instance. Defining it as <see langword="default"/> removes any possibility of the named value
    /// and the type's own default disagreeing - there is nothing to keep in step, because they are
    /// the same value by construction. An explicit initializer would be a SECOND definition of the
    /// cleared state, and a future edit to one and not the other would be invisible. The sibling
    /// <c>Errors/DbErrorData.Empty</c> is defined the same way, so the two read consistently.
    /// </para>
    /// <para>
    /// It is safe to render and to compare: <c>Empty.ToString()</c> discloses nothing because the
    /// cleared password is the empty string, and <c>Empty.GetHashCode()</c> does not throw because
    /// the generated hash routes every field through
    /// <see cref="EqualityComparer{T}.Default"/>, which maps <see langword="null"/> to zero.
    /// </para>
    /// </remarks>
    public static TransactionData Empty => default;

    // ------------------------------------------------------------------------------------------
    //  THE SEVEN-OF-NINE TRANSFER - THE MEASURED ASYMMETRY, REPRODUCED AND NOT COMPLETED
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// Returns a copy of this descriptor with the SEVEN CONNECTION FIELDS taken from
    /// <paramref name="source"/> and this descriptor's own <see cref="AutoCommit"/> and
    /// <see cref="UserParm"/> LEFT UNTOUCHED - the exact field set both legacy accessors move
    /// [ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:L345-L351 and :L410-L416].
    /// </summary>
    /// <param name="source">The descriptor to take the seven connection fields from.</param>
    /// <returns>
    /// A new descriptor: <see cref="Dbms"/>, <see cref="ServerName"/>, <see cref="Database"/>,
    /// <see cref="LogId"/>, <see cref="LogPass"/>, <see cref="DbParm"/> and <see cref="Lock"/> from
    /// <paramref name="source"/>; <see cref="AutoCommit"/> and <see cref="UserParm"/> from this
    /// instance.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>THE NAME SAYS ConnectionFields BECAUSE IT IS SEVEN OF NINE, NOT ALL NINE.</b> The omission
    /// has to be visible from the call site, which is why this is not called
    /// <c>WithFieldsFrom</c> or <c>CopyFrom</c>. A reader who sees only the call must be able to
    /// tell that two members did not move.
    /// </para>
    /// <para>
    /// <b>THIS IS THE INBOUND TRANSFER ONLY. THE OUTBOUND DIRECTION MOVES SIX, NOT SEVEN</b>, and it has
    /// its own fold:
    /// <see cref="WithConnectionFieldsFromExcludingCredential(in TransactionData)"/>. One fold used to
    /// serve both, on the reasoning that the legacy's two accessors move the same seven fields so a
    /// shared fold could not drift - and the legacy DOES move the password outbound
    /// [<c>n_cst_thread_trans.sru:L414</c>]. The two directions are nonetheless no longer symmetric,
    /// because AAP 0.4.2.6 makes <see cref="LogPass"/> write-only: never echoed in a response. An
    /// outbound accessor that hands the password back to its caller IS that echo, whatever the caller
    /// then does with it.
    /// </para>
    /// <para>
    /// Inbound, the receiver takes all seven from the supplied descriptor
    /// [<see cref="SetTransactionData"/>], and that is the connect path - the credential's whole purpose
    /// is to reach the connection. Outbound, the caller's descriptor takes SIX from the connection's own
    /// state and keeps whatever it already held in the seventh
    /// [<see cref="GetTransactionData(ref TransactionData, ref string, GetTransactionDataHook?)"/>].
    /// </para>
    /// <para>
    /// <b>The asymmetry is deliberately expressed as two named methods rather than as a flag</b>, so no
    /// call site can pick the wrong direction by passing the wrong boolean, and so a reader of either
    /// call site can see which one it is without opening this file.
    /// </para>
    /// <para>
    /// <b>WHY AutoCommit AND UserParm ARE OMITTED - measured evidence, not speculation.</b> A SQL
    /// task assigns <c>_transData.AutoCommit = false</c> immediately after storing a descriptor
    /// [n_cst_thread_task_sqlbase.sru:L119], under a comment that reads "erase parameters irrelevant
    /// to the connection target" [:L118]. That is the legacy telling us what it thinks the flag is:
    /// a SESSION CONTROL, not part of the connection identity. And the connection identity is
    /// exactly what these seven fields are for - the transaction pool keys its reference-counted
    /// entries on whole-descriptor equality [n_cst_thread_trans_pool.sru:L138], so a member that
    /// varies per session while addressing the same target has no business travelling with a
    /// connection. <see cref="UserParm"/> is excluded on the same footing. What looks like an
    /// incomplete mapping is an intelligible invariant.
    /// </para>
    /// <para>
    /// <b>IT IS NOT TO BE COMPLETED (C-B).</b> There is deliberately no <c>includeAll</c> parameter,
    /// no <c>WithAllFieldsFrom</c> companion and no overload that moves nine, because any of them
    /// would let a caller reintroduce behaviour the legacy does not have. A caller that genuinely
    /// wants all nine already has the plain assignment - the type is a value - and that is a
    /// different operation with a different name at the call site, which is the point.
    /// </para>
    /// <para>
    /// <b>Aliasing is safe.</b> The <see langword="with"/> expression reads every member it needs and
    /// constructs the result before anything is assigned, so passing a descriptor that shares
    /// storage with the caller's target produces the same answer as passing a copy.
    /// </para>
    /// </remarks>
    public TransactionData WithConnectionFieldsFrom(in TransactionData source) => this with
    {
        // The seven, in the oracle's own assignment order [n_cst_thread_trans.sru:L345-L351].
        Dbms = source.Dbms,
        ServerName = source.ServerName,
        Database = source.Database,
        LogId = source.LogId,

        // THE CREDENTIAL MOVES INBOUND AND ONLY INBOUND, through the named reveal rather than through a
        // property read - see RevealLogPassForConnect and the OUTBOUND fold below.
        LogPass = source.RevealLogPassForConnect(),
        DbParm = source.DbParm,
        Lock = source.Lock,

        // AutoCommit AND UserParm ARE ABSENT ON PURPOSE. The legacy touches neither, in either
        // direction [n_cst_thread_trans.sru:L345-L351 inbound, :L410-L416 outbound]. Do NOT add
        // them: doing so would silently change which values make up a connection identity, and the
        // task layer erases the flag anyway [n_cst_thread_task_sqlbase.sru:L119]. Legacy behaviour,
        // deliberately preserved (C-B). See this method's remarks for the measured reasoning.
    };

    /// <summary>
    /// Returns a copy of this descriptor with the SIX NON-CREDENTIAL connection fields taken from
    /// <paramref name="source"/> - everything
    /// <see cref="WithConnectionFieldsFrom(in TransactionData)"/> moves EXCEPT <see cref="LogPass"/>,
    /// which is left exactly as this descriptor already held it.
    /// </summary>
    /// <param name="source">The descriptor to take the six non-credential connection fields from.</param>
    /// <returns>
    /// A new descriptor: <see cref="Dbms"/>, <see cref="ServerName"/>, <see cref="Database"/>,
    /// <see cref="LogId"/>, <see cref="DbParm"/> and <see cref="Lock"/> from <paramref name="source"/>;
    /// <see cref="LogPass"/>, <see cref="AutoCommit"/> and <see cref="UserParm"/> from this instance.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>THIS IS THE OUTBOUND FOLD, AND ITS OMISSION IS THE POINT OF ITS NAME.</b> The legacy's outbound
    /// accessor moves the password back to its caller [<c>n_cst_thread_trans.sru:L414</c>]. AAP 0.4.2.6
    /// forbids that: <see cref="LogPass"/> is write-only, never echoed in a response. Handing a
    /// credential back to whoever asked for a descriptor is the echo, regardless of what the caller
    /// intends to do with it - and across a service boundary the caller may not be the process that
    /// supplied it.
    /// </para>
    /// <para>
    /// <b>WHY THE RECEIVER KEEPS ITS OWN VALUE RATHER THAN BEING CLEARED.</b> Clearing would be a second,
    /// larger behaviour change: a caller that supplied a password, read the descriptor back and then
    /// re-supplied it would find its own credential silently erased, and would either fail to reconnect
    /// or - worse - reconnect with an empty password. Leaving the seventh member untouched means the
    /// outbound path is a strict NON-EVENT for the credential: it neither discloses nor destroys.
    /// </para>
    /// <para>
    /// <b>THIS IS A NARROWING OF A NEWLY CREATED SURFACE, NOT A CHANGE TO AN EXISTING WIRE FORMAT.</b>
    /// PowerFramework is an in-process library with no listener (AAP 0.1.1), so the legacy's outbound
    /// move handed a password from one object to another inside a single process that already held it.
    /// C-08 turns that same accessor into a network response, at which point the same move becomes
    /// disclosure - which is exactly the class of case AAP 0.1.5 governs: narrow with a defined behaviour
    /// rather than widen with a guess. The published contract enforces the same rule independently, its
    /// response-side view having no slot for the field at all
    /// [<c>persistence.v1.proto:L1965-L1987</c>], so a port that moved the value here would be
    /// contradicting the contract as well as the plan.
    /// </para>
    /// <para>
    /// <b>Aliasing is safe</b>, for the same reason as the inbound fold: the <see langword="with"/>
    /// expression reads every member it needs before anything is assigned.
    /// </para>
    /// </remarks>
    public TransactionData WithConnectionFieldsFromExcludingCredential(in TransactionData source) =>
        this with
        {
            // The oracle's own assignment order [n_cst_thread_trans.sru:L410-L416], LESS the credential.
            Dbms = source.Dbms,
            ServerName = source.ServerName,
            Database = source.Database,
            LogId = source.LogId,

            // LogPass IS ABSENT ON PURPOSE AND MUST STAY ABSENT. The legacy moves it here
            // [n_cst_thread_trans.sru:L414]; AAP 0.4.2.6 forbids echoing it. Adding it back would
            // reintroduce the credential-exposure finding this method exists to close, and would also
            // contradict the published contract, whose response-side view has no field for it.
            DbParm = source.DbParm,
            Lock = source.Lock,

            // AutoCommit AND UserParm are absent for the SEPARATE reason recorded on the inbound fold:
            // the legacy touches neither in either direction. Two different omissions, two different
            // reasons, deliberately not conflated.
        };

    // ------------------------------------------------------------------------------------------
    //  THE INBOUND ACCESSOR - of_settransdata [n_cst_thread_trans.sru:L343-L354]
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// Applies the seven connection fields of <paramref name="data"/> to
    /// <paramref name="receiver"/>, honouring an optional veto hook. The port of
    /// <c>of_settransdata</c>
    /// [ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:L343-L354].
    /// </summary>
    /// <param name="receiver">
    /// The descriptor being updated in place - the .NET stand-in for the legacy transaction object's
    /// own connection properties, which is what <c>of_settransdata</c> assigns into. Its
    /// <see cref="AutoCommit"/> and <see cref="UserParm"/> are left exactly as they were.
    /// </param>
    /// <param name="data">
    /// The descriptor supplying the seven fields. Passed <see langword="in"/> because the legacy
    /// parameter is <c>readonly</c> [:L343].
    /// </param>
    /// <param name="onSetTransData">
    /// The optional veto hook. <see langword="null"/> - the normal case - behaves exactly as an
    /// unimplemented PowerBuilder event, which returns <c>0</c> and therefore does not veto.
    /// </param>
    /// <returns>
    /// <see cref="RetCode.OK"/> in every case. The legacy has no failure path here: it returns
    /// <c>RetCode.OK</c> both when vetoed [:L343] and when the copy completes [:L353].
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>THE VETO TEST IS A LITERAL EQUALITY AGAINST 1, AND THE PREVENTION PREDICATE IS
    /// DELIBERATELY NOT USED.</b> The oracle reads
    /// <c>if Event OnSetTransData(data) = 1 then return RetCode.OK</c> [:L343]. That is NOT
    /// <c>IsPrevented(...)</c>, and the difference is observable: the shared kernel's prevention
    /// algebra is tri-valued, so a hook returning <c>2</c> - a deep prevention - would satisfy a
    /// prevention predicate but does NOT satisfy this equality and therefore does NOT veto here.
    /// </para>
    /// <para>
    /// This is a measured distinction rather than an assumption about the author's intent. The very
    /// next function in the same legacy object DOES use the predicate:
    /// <c>if IsPrevented(Event OnBeforeRetrieve(ds))</c> [n_cst_thread_trans.sru:L429]. The two
    /// spellings sit a few lines apart, so the narrow equality here is a choice the oracle made, and
    /// C-B requires it be reproduced rather than harmonised. Reaching for
    /// <c>Predicates.IsPrevented</c> would widen the veto to a second value and change behaviour for
    /// every hook that returns it.
    /// </para>
    /// <para>
    /// <b>A VETO SKIPS THE ASSIGNMENT ENTIRELY AND STILL REPORTS SUCCESS.</b> Nothing on
    /// <paramref name="receiver"/> is written - not the seven, not the two - and the return value is
    /// success rather than a cancellation code. A caller cannot distinguish a vetoed call from a
    /// completed one by its return value alone, which is precisely the legacy contract.
    /// </para>
    /// <para>
    /// <b>Declared <see langword="static"/> with a <see langword="ref"/> receiver, because the legacy
    /// mutates in place and this type is immutable.</b> The alternative shapes are worse: an
    /// instance method cannot assign <c>this</c> on a readonly struct, and returning a new value
    /// would lose the "nothing was written" guarantee that the vetoed path depends on and that the
    /// parity tests assert directly. The two parameters must not alias, though the implementation is
    /// safe if they do - see <see cref="WithConnectionFieldsFrom"/>.
    /// </para>
    /// <para>
    /// The commented-out busy guard at the head of the SQL task's own same-named function
    /// [n_cst_thread_task_sqlbase.sru:L113] belongs to that layer, is inert in the oracle, and is
    /// deliberately not revived here.
    /// </para>
    /// </remarks>
    public static long SetTransactionData(
        ref TransactionData receiver,
        in TransactionData data,
        SetTransactionDataHook? onSetTransData = null)
    {
        // [:L343] `if Event OnSetTransData(data) = 1 then return RetCode.OK`
        // A null hook stands in for an unimplemented PowerBuilder event, which returns 0.
        // The comparison is against the literal 1 and nothing else - see the remarks above for why
        // Predicates.IsPrevented is NOT used here even though a prevention is what 1 means.
        if (onSetTransData is not null && onSetTransData(in data) == 1)
        {
            // Vetoed: the assignment is skipped ENTIRELY and success is reported [:L343].
            return RetCode.OK;
        }

        // [:L345-L351] the seven-field copy. AutoCommit and UserParm stay as the receiver had them.
        receiver = receiver.WithConnectionFieldsFrom(in data);

        // [:L353]
        return RetCode.OK;
    }

    // ------------------------------------------------------------------------------------------
    //  THE OUTBOUND ACCESSOR - of_gettransdata(ref, ref) [n_cst_thread_trans.sru:L402-L419]
    //  ----------------------------------------------------------------------------------------
    //  THE DIAGNOSTIC IS INSPECTED TWICE. That is the whole subtlety of this function, and it is
    //  reproduced literally below. The oracle, verbatim:
    //
    //      errInfo = ""                                                          [:L402]
    //      if Event OnGetTransData(ref data,ref errInfo) = 1 then                [:L404]
    //          if errInfo <> "" then return RetCode.FAILED                       [:L405]
    //          return RetCode.OK                                                 [:L406]
    //      end if
    //      if errInfo <> "" then return RetCode.FAILED                           [:L408]
    //      data.DBMS = DBMS  ... data.Lock = Lock                                [:L410-L416]
    //      return RetCode.OK                                                     [:L418]
    //
    //  Collapsing the two checks into one - by hoisting [:L405] out of the veto arm, or by testing
    //  once after it - changes the outcome for a hook that vetoes AND reports, so both stay.
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// Fills <paramref name="data"/>'s seven connection fields from this descriptor, honouring an
    /// optional veto hook and its diagnostic slot. The port of
    /// <c>of_gettransdata(ref transactiondata, ref string)</c>
    /// [ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:L402-L419].
    /// </summary>
    /// <param name="data">
    /// The caller's descriptor, filled in place. Its <see cref="AutoCommit"/> and
    /// <see cref="UserParm"/> are LEFT UNTOUCHED, exactly as the oracle leaves them. The hook, if
    /// supplied, receives this same reference and may populate it itself - and on the vetoed path
    /// whatever it wrote is what the caller keeps, because only the framework's own copy is skipped.
    /// </param>
    /// <param name="errInfo">
    /// The diagnostic slot. CLEARED to <see cref="string.Empty"/> before the hook runs [:L402], then
    /// inspected TWICE - see this method's remarks and the block comment above it.
    /// </param>
    /// <param name="onGetTransData">
    /// The optional veto hook. <see langword="null"/> behaves as an unimplemented PowerBuilder event,
    /// returning <c>0</c> and therefore not vetoing.
    /// </param>
    /// <returns>
    /// <see cref="RetCode.FAILED"/> when <paramref name="errInfo"/> is non-empty at either
    /// inspection point; otherwise <see cref="RetCode.OK"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>THE FOUR-WAY OUTCOME MATRIX, which is what the parity suite pins.</b> The result depends on
    /// the hook's return value and on the diagnostic TOGETHER, and no single condition determines it:
    /// </para>
    /// <list type="table">
    /// <listheader>
    /// <term>hook result &#215; diagnostic</term>
    /// <description>outcome</description>
    /// </listheader>
    /// <item>
    /// <term><c>1</c>, diagnostic empty</term>
    /// <description>
    /// <see cref="RetCode.OK"/>, and the seven-field copy is SKIPPED [:L406]. A clean veto.
    /// </description>
    /// </item>
    /// <item>
    /// <term><c>1</c>, diagnostic non-empty</term>
    /// <description>
    /// <see cref="RetCode.FAILED"/>, copy skipped [:L405]. This is the cell that requires the FIRST
    /// check to exist: a hook may both veto and report.
    /// </description>
    /// </item>
    /// <item>
    /// <term>anything else, diagnostic non-empty</term>
    /// <description>
    /// <see cref="RetCode.FAILED"/>, copy skipped [:L408]. This is the cell that requires the SECOND
    /// check: a hook may report a failure WITHOUT vetoing, and the copy must not proceed.
    /// </description>
    /// </item>
    /// <item>
    /// <term>anything else, diagnostic empty</term>
    /// <description>
    /// <see cref="RetCode.OK"/> and the seven fields are copied [:L410-L418]. The normal path, and
    /// the only one that writes anything.
    /// </description>
    /// </item>
    /// </list>
    /// <para>
    /// <b>"Anything else" INCLUDES 2.</b> As on the inbound accessor, the test is a literal equality
    /// against <c>1</c> rather than the tri-valued prevention predicate [:L404], so a hook returning
    /// a deep-prevention value does not veto - it falls into the third or fourth row above. The
    /// contrasting use of <c>IsPrevented(...)</c> a few lines later in the same legacy object
    /// [:L429] is what establishes that the narrow equality is deliberate.
    /// </para>
    /// <para>
    /// <b>An empty diagnostic is tested as "empty OR unset", which is what PowerBuilder does.</b>
    /// Comparing a null string with <c>&lt;&gt; ""</c> yields null in PowerScript, and PowerBuilder
    /// evaluates a null condition as false, so a null diagnostic does NOT take the failure arm. Using
    /// <see cref="string.IsNullOrEmpty"/> reproduces both halves of that in one expression, and it
    /// also means a nullable-oblivious hook cannot turn a null into a spurious failure.
    /// </para>
    /// <para>
    /// <b>The seven-field copy is the SAME fold the inbound accessor uses</b>, applied in the other
    /// direction: <c>data</c> takes the seven from this instance. See
    /// <see cref="WithConnectionFieldsFrom"/> for why the two omitted members are omitted.
    /// </para>
    /// </remarks>
    public long GetTransactionData(
        ref TransactionData data,
        ref string errInfo,
        GetTransactionDataHook? onGetTransData = null)
    {
        // [:L402] `errInfo = ""` - cleared BEFORE the hook runs, so a stale diagnostic from an
        // earlier call can never be mistaken for this call's.
        errInfo = string.Empty;

        // [:L404] the literal `= 1` veto test. See the remarks: 2 does NOT veto.
        if (onGetTransData is not null && onGetTransData(ref data, ref errInfo) == 1)
        {
            // [:L405] FIRST diagnostic inspection - INSIDE the veto arm. A hook that vetoes AND
            // reports fails; a hook that vetoes cleanly succeeds. IsNullOrEmpty reproduces
            // PowerBuilder's `<> ""` including its treatment of a null as not-non-empty.
            if (!string.IsNullOrEmpty(errInfo))
            {
                return RetCode.FAILED;
            }

            // [:L406] a clean veto reports success and copies NOTHING.
            return RetCode.OK;
        }

        // [:L408] SECOND diagnostic inspection - AFTER the veto arm, and a genuinely separate test.
        // It catches a hook that reported a failure without vetoing. Do NOT merge this with the
        // check above: hoisting either one changes the first two rows of the matrix in the remarks.
        if (!string.IsNullOrEmpty(errInfo))
        {
            return RetCode.FAILED;
        }

        // [:L410-L416] the outbound copy - SIX FIELDS, NOT THE SEVEN THE ORACLE MOVES. The caller's
        // AutoCommit and UserParm survive for the legacy's own reason; the caller's LogPass survives
        // because AAP 0.4.2.6 makes it write-only, so this accessor must not hand it back. The oracle's
        // `data.LogPass = LogPass` [:L414] is the one line of this accessor that is deliberately NOT
        // reproduced, and WithConnectionFieldsFromExcludingCredential is where that is recorded in full.
        data = data.WithConnectionFieldsFromExcludingCredential(this);

        // [:L418]
        return RetCode.OK;
    }

    // ------------------------------------------------------------------------------------------
    //  THE CONVENIENCE OVERLOAD - of_gettransdata() [n_cst_thread_trans.sru:L394-L400]
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// Returns a freshly cleared descriptor filled with this descriptor's seven connection fields,
    /// <b>DISCARDING the return code of the underlying call</b>. The port of the parameterless
    /// <c>of_gettransdata()</c>
    /// [ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:L394-L400].
    /// </summary>
    /// <param name="onGetTransData">
    /// The optional veto hook, forwarded unchanged to
    /// <see cref="GetTransactionData(ref TransactionData, ref string, GetTransactionDataHook?)"/>.
    /// </param>
    /// <returns>
    /// The descriptor, <b>whether or not the underlying call succeeded</b>. On a vetoed or failed
    /// call this is whatever the hook left in it, which for a hook that wrote nothing is the cleared
    /// value <see cref="Empty"/> - NOT this instance's connection fields.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>A PRESERVED LEGACY QUIRK, ANNOTATED RATHER THAN FIXED (C-B).</b> The oracle declares a
    /// local diagnostic and a local structure, calls the two-argument form, and then simply
    /// <c>return data</c> - the <c>long</c> the call produced is never assigned or examined
    /// [:L394-L400, the discarded call at :L397]. So a failure is INVISIBLE through this overload: a
    /// caller receives a descriptor that may be partially filled, entirely empty, or filled by the
    /// hook, with nothing to distinguish those cases.
    /// </para>
    /// <para>
    /// <b>It deliberately does NOT throw and does NOT propagate.</b> Making the convenience overload
    /// raise on failure, or return a nullable, or expose the code through an
    /// <see langword="out"/> parameter, would each be an improvement the legacy does not have - and
    /// callers that rely on getting a value back unconditionally would change behaviour. A caller
    /// that needs to know whether the call succeeded must use the two-argument overload, which is
    /// exactly the choice the oracle offers.
    /// </para>
    /// <para>
    /// The discard is written as an explicit <c>_ =</c> so that the omission is deliberate on its
    /// face and cannot be read as a forgotten assignment.
    /// </para>
    /// </remarks>
    public TransactionData GetTransactionData(GetTransactionDataHook? onGetTransData = null)
    {
        // [:L394] `string sErrInfo` - PowerBuilder initialises an unassigned string to "".
        string errInfo = string.Empty;

        // [:L395] `TRANSACTIONDATA data` - a freshly declared, unassigned structure, which is the
        // cleared state. Written as `default` for the reason recorded on Empty.
        TransactionData data = default;

        // [:L397] THE QUIRK: the return code is DISCARDED. Not examined, not propagated, not logged.
        _ = GetTransactionData(ref data, ref errInfo, onGetTransData);

        // [:L399] the descriptor is returned regardless of what the call reported.
        return data;
    }

    // ------------------------------------------------------------------------------------------
    //  THE TWO DBParm FLAGS - THE NESTED PARSE [n_cst_thread_task_sqlbase.sru:L127-L132]
    //  ----------------------------------------------------------------------------------------
    //  THE ORACLE, VERBATIM. The nesting is the whole point and is reproduced literally:
    //
    //      _bNCharBinding = false                                                        [:L127]
    //      if RegExpFind(_transData.DBParm,"DisableBind\s*=\s*(0|1)",2,true) = "1" then   [:L128]
    //          if RegExpFind(_transData.DBParm,"NCharBind\s*=\s*(0|1)",2,true) = "1" then [:L129]
    //              _bNCharBinding = true                                                 [:L130]
    //          end if                                                                    [:L131]
    //      end if                                                                        [:L132]
    //
    //  THE RegExpFind OVERLOAD IN USE is `(string str, string pattern, int index, boolean matchcase)`
    //  [ws_objects/pfw.utility.regexp.pbl.src/regexpfind.srf:L11], and the `2` selects the FIRST
    //  CAPTURE GROUP rather than the whole match - which is why the result is compared to "1" and not
    //  to "DisableBind=1". Regex.Match plus Groups[1] is the direct equivalent, and .NET's Match
    //  returns the first match just as the legacy call does.
    //
    //  THE PATTERN STRINGS ARE BYTE-IDENTICAL TO THE ORACLE'S and must stay so. In particular they
    //  are DELIBERATELY UNANCHORED: no `^`, no `\b`, no `[;\s]` guard. That means a longer keyword
    //  ending in the same text also matches, which is a real property of the oracle's behaviour, not
    //  an oversight to tidy - adding a word boundary here would change which DBParm strings are
    //  recognised and is exactly the kind of silent behavioural change C-B forbids.
    //
    //  WHITESPACE AND CASE. `\s*` on both sides of the `=` is the oracle's own tolerance. Case
    //  insensitivity is the specified port behaviour, expressed here through RegexOptions.IgnoreCase.
    //  Recorded honestly, because it is the one detail the repository cannot adjudicate on its own:
    //  the legacy passes `matchcase = true` to a CLOSED-SOURCE PBNI function whose flag semantics
    //  appear in no document, header or comment anywhere in the tree - `regexpfind.srf` is a bare
    //  `native "pfw.dll"` prototype - and there are only three RegExpFind call sites in the entire
    //  repository, none of which disambiguates it. Case-insensitive is also the reading consistent
    //  with PowerBuilder's own treatment of DBParm keywords, so canonical DBParm strings behave
    //  identically either way and only non-canonical casing can distinguish the two readings.
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// The <c>DisableBind</c> flag parsed out of <see cref="DbParm"/> - <see langword="true"/> only
    /// when the captured value is exactly <c>"1"</c>
    /// [ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru:L128].
    /// </summary>
    /// <value>
    /// <para>
    /// =============== THIS IS THE MECHANICAL ROOT OF THE SQL-INJECTION EXPOSURE (C-K) ===============
    /// </para>
    /// <para>
    /// <b><c>DisableBind=1</c> MEANS THE RUNTIME DOES NOT USE BIND VARIABLES.</b> Values are
    /// INTERPOLATED INTO THE STATEMENT TEXT AS LITERALS instead of being bound, so there is no bind
    /// boundary for a value to stay behind. Two consequences follow and neither is hypothetical.
    /// First, it is why a raw clause spliced into a statement by the C-05 clause setters is an
    /// injection site at all. Second, it is why the generated-statement field of a database error
    /// must be redacted: the legacy places the COMPLETE generated statement into that field and its
    /// logger redacts nothing, so with binding disabled that field carries live row data.
    /// </para>
    /// <para>
    /// <b>The .NET implementation uses PARAMETERIZED COMMANDS internally while preserving the
    /// OBSERVABLE generated statement unchanged.</b> That combination is deliberate: the safer
    /// mechanism is unobservable, so adopting it is permitted, while the statement text a caller can
    /// see must still match the oracle byte for byte. <b>This is a known legacy defect being
    /// DOCUMENTED, NOT CORRECTED (C-B)</b>, and each affected site is annotated where it occurs
    /// rather than silently changed.
    /// </para>
    /// <para>
    /// <b>The offending statement text reaches diagnostics only through
    /// <c>Errors/SqlRedactor.cs</c>, which masks it unconditionally.</b> There is deliberately no
    /// self-conversion on the error payload type, no hand-rolled mapping that bypasses the redactor,
    /// and no redactor argument on the outward projection that could be replaced by a pass-through.
    /// This property is the signal that tells that layer whether the statement it is about to publish
    /// can contain data at all.
    /// </para>
    /// <para>
    /// ==========================================================================================
    /// </para>
    /// <para>
    /// Computed from <see cref="DbParm"/> on every read rather than cached, because the descriptor is
    /// an immutable value: there is no state to keep in step, and a cached copy would be a second
    /// definition of the same fact. Absent key, <c>0</c>, or malformed text all yield
    /// <see langword="false"/> - only a captured <c>"1"</c> is true.
    /// </para>
    /// </value>
    public bool IsBindDisabled
    {
        get
        {
            ResolveDbParmFlags(out bool isBindDisabled, out _);
            return isBindDisabled;
        }
    }

    /// <summary>
    /// The <c>NCharBind</c> flag parsed out of <see cref="DbParm"/> - <see langword="true"/> only
    /// when <see cref="IsBindDisabled"/> is ALSO <see langword="true"/>
    /// [ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru:L129-L130].
    /// </summary>
    /// <value>
    /// <para>
    /// <b>NATIONAL-CHARACTER BINDING IS MEANINGLESS UNLESS BINDING IS DISABLED, and that dependency
    /// is contract rather than an implementation accident.</b> The oracle consults
    /// <c>NCharBind</c> ONLY INSIDE the <c>DisableBind=1</c> branch [:L128-L132], so
    /// <c>NCharBind=1</c> on its own has NO EFFECT WHATSOEVER. Sending it alone is legal and inert,
    /// which matches the legacy exactly and is stated the same way in the published contract
    /// [persistence.v1.proto:L1809-L1817, L1841-L1844].
    /// </para>
    /// <para>
    /// <b>The two tests are kept NESTED rather than flattened into a single conjunction.</b> A flat
    /// <c>&amp;&amp;</c> would compute the same answer, and that is exactly why it is the wrong shape
    /// here: it hides the dependency, and the next reader would have no way to tell that the second
    /// flag is subordinate rather than independent. A reading that honoured national-character
    /// binding independently would change generated statements for every caller who set the second
    /// flag without the first (C-B). Do not simplify it.
    /// </para>
    /// <para>
    /// Computed on every read, on the same terms as <see cref="IsBindDisabled"/>.
    /// </para>
    /// </value>
    public bool IsNCharBindingEnabled
    {
        get
        {
            ResolveDbParmFlags(out _, out bool isNCharBindingEnabled);
            return isNCharBindingEnabled;
        }
    }

    /// <summary>
    /// Resolves BOTH <see cref="DbParm"/> flags in one pass - the single literal reproduction of
    /// [ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru:L127-L132]. The two
    /// properties above delegate here, so the nested rule is written exactly once.
    /// </summary>
    /// <param name="isBindDisabled">
    /// Receives the <c>DisableBind</c> result [:L128]. See <see cref="IsBindDisabled"/> for the
    /// security consequence of this flag being set.
    /// </param>
    /// <param name="isNCharBindingEnabled">
    /// Receives the <c>NCharBind</c> result [:L129-L130]. <see langword="false"/> whenever
    /// <paramref name="isBindDisabled"/> is <see langword="false"/>, because the oracle never
    /// consults the second key outside the first key's branch.
    /// </param>
    /// <remarks>
    /// <para>
    /// Exposed alongside the two properties rather than instead of them, because the two shapes serve
    /// different callers: a consumer that needs one flag reads a property, and a consumer that
    /// projects both onto the contract's flag message - where they travel together as one allowlist
    /// [persistence.v1.proto:L1818-L1845] - takes them both in one call and matches the legacy's
    /// single-pass evaluation exactly.
    /// </para>
    /// <para>
    /// Both patterns are source-generated and therefore compiled once at build time, with the pattern
    /// text declared in exactly one place each. That keeps the strings byte-identical to the oracle's
    /// and keeps the build warning-clean, which matters because warnings are errors here.
    /// </para>
    /// </remarks>
    public void ResolveDbParmFlags(out bool isBindDisabled, out bool isNCharBindingEnabled)
    {
        // [:L127] `_bNCharBinding = false` - the default is established BEFORE any test, so every
        // path below that does not explicitly set it leaves it false.
        isNCharBindingEnabled = false;

        // [:L128] the outer test.
        isBindDisabled = CapturedFlagIsOne(DisableBindPattern(), DbParm);
        if (isBindDisabled)
        {
            // [:L129] THE NESTED TEST. It is nested because the oracle nests it, and because that
            // nesting IS the contract: NCharBind is meaningless unless binding is disabled. Do not
            // flatten this into `isBindDisabled && ...` - see IsNCharBindingEnabled for why.
            if (CapturedFlagIsOne(NCharBindPattern(), DbParm))
            {
                // [:L130]
                isNCharBindingEnabled = true;
            }
        }
    }

    /// <summary>
    /// Applies one of the two flag patterns to a connection parameter string and reports whether the
    /// FIRST CAPTURE GROUP of the FIRST match is exactly the text <c>"1"</c>.
    /// </summary>
    /// <param name="pattern">The source-generated flag pattern to apply.</param>
    /// <param name="dbParm">The connection parameter string to search.</param>
    /// <returns>
    /// <see langword="true"/> only for a captured <c>"1"</c>. No match, a captured <c>0</c>, or any
    /// other captured text yields <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This is the port of the legacy <c>RegExpFind(..., 2, true) = "1"</c> idiom, and the comparison
    /// really is TEXTUAL rather than numeric: the legacy compares the captured substring to the
    /// string <c>"1"</c> [n_cst_thread_task_sqlbase.sru:L128-L129], so no parsing, no
    /// <see cref="bool"/> conversion and no culture is involved. The comparison is
    /// <see cref="StringComparison.Ordinal"/> for the same reason - a single ASCII digit has no
    /// culture-sensitive interpretation, and stating it explicitly keeps the intent unambiguous.
    /// </para>
    /// <para>
    /// A non-matching pattern is not an error condition: the oracle's function returns a value that
    /// simply is not <c>"1"</c>, and the surrounding <c>if</c> falls through. Reporting
    /// <see langword="false"/> reproduces that without introducing an exception path the legacy does
    /// not have.
    /// </para>
    /// </remarks>
    private static bool CapturedFlagIsOne(Regex pattern, string dbParm)
    {
        Match match = pattern.Match(dbParm);

        // Groups[1] is the `(0|1)` capture - the legacy's index 2, where index 1 is the whole match.
        return match.Success && string.Equals(match.Groups[1].Value, "1", StringComparison.Ordinal);
    }

    /// <summary>
    /// The <c>DisableBind</c> pattern, byte-identical to the oracle's
    /// [ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru:L128]. Unanchored on purpose
    /// - see the block comment above <see cref="IsBindDisabled"/>.
    /// </summary>
    /// <returns>The compiled, source-generated pattern.</returns>
    [GeneratedRegex(@"DisableBind\s*=\s*(0|1)", RegexOptions.IgnoreCase)]
    private static partial Regex DisableBindPattern();

    /// <summary>
    /// The <c>NCharBind</c> pattern, byte-identical to the oracle's
    /// [ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru:L129]. Consulted only inside
    /// the <c>DisableBind</c> branch - see <see cref="IsNCharBindingEnabled"/>.
    /// </summary>
    /// <returns>The compiled, source-generated pattern.</returns>
    [GeneratedRegex(@"NCharBind\s*=\s*(0|1)", RegexOptions.IgnoreCase)]
    private static partial Regex NCharBindPattern();

    // ------------------------------------------------------------------------------------------
    //  THE REDACTED RENDERING - THE FOUR-PART ENFORCEMENT OF THE WRITE-ONLY RULE, PARTS 2 AND 3
    //  ----------------------------------------------------------------------------------------
    //  BOTH MEMBERS BELOW ARE DECLARED SO THAT NEITHER IS GENERATED. A record's compiler-generated
    //  ToString() renders every property, and it does so by calling a generated
    //  `PrintMembers(StringBuilder)`. Declaring ToString() suppresses generation of the first;
    //  declaring PrintMembers suppresses generation of the second. Declaring only one would leave
    //  the other synthesized - harmless today, and a live leak the moment a later edit routes
    //  through it. Both are therefore written out, and ToString() CALLS PrintMembers so that the
    //  redaction lives in exactly one place and neither member is dead code.
    //
    //  WHAT IS RENDERED: THE SAME SIX MEMBERS THE RESPONSE-SIDE CONTRACT IS WILLING TO EMIT. That is
    //  the rule, and it is auditable rather than a matter of taste - contract C-08's
    //  TransactionDescriptorView carries six of the nine fields and reserves three slots
    //  permanently [persistence.v1.proto:L2048-L2085]. Rendered here: Dbms, ServerName, Database,
    //  LogId, Lock, AutoCommit. Omitted here: the password, the connection parameter string and the
    //  user parameter string - the same three, for the same reasons, recorded on each member.
    //
    //  THE OMITTED MEMBERS ARE NOT MENTIONED AT ALL, not even as a redaction marker. That is
    //  deliberate: the audit rule for this file is that those identifiers do not appear anywhere in
    //  the printing path, so there is no line for a later edit to "improve" into printing a value,
    //  and a reviewer can verify the rule with a single search rather than by reading logic.
    //
    //  NOTHING IS LOST FOR DIAGNOSTICS. The six rendered members identify the connection target
    //  completely - which DBMS, which server, which database, which account, which isolation level,
    //  which session flag - which is everything a person debugging a connection needs. The two
    //  DBParm-derived flags are separately reachable as typed booleans that disclose nothing, and
    //  they are the only content of that string any consumer has a demonstrated need for.
    //
    //  C-B NOTE: choosing what this renders is NOT a behaviour change. PowerBuilder structures have
    //  no string conversion at all, so there is no legacy rendering to preserve or improve; the
    //  entire member is an affordance .NET adds, and C-F governs what it may contain.
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// Renders the descriptor for diagnostics, <b>with the three credential-capable members
    /// omitted</b>. Replaces the compiler-generated implementation, which would render every member.
    /// </summary>
    /// <returns>
    /// A record-shaped string carrying exactly the six members contract C-08's response-side view is
    /// willing to emit: <see cref="Dbms"/>, <see cref="ServerName"/>, <see cref="Database"/>,
    /// <see cref="LogId"/>, <see cref="Lock"/> and <see cref="AutoCommit"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>This override is load-bearing, not cosmetic.</b> It is the reason a structured-log call
    /// such as <c>logger.LogDebug("{Descriptor}", transData)</c> is safe: that call formats the value
    /// by calling this method. Remove or weaken it and every such call site becomes a credential
    /// disclosure, silently and at once, with nothing in the build to notice.
    /// </para>
    /// <para>
    /// <b>Two descriptors that differ only in a redacted member render IDENTICALLY.</b> That is the
    /// intended pair of properties held together - full participation in equality, zero participation
    /// in rendering - and the sibling test project asserts exactly that.
    /// </para>
    /// <para>
    /// The output shape deliberately mirrors what a record generates, so that a reader who has seen
    /// other record types in this codebase is not surprised, and so that the omission is the only
    /// difference.
    /// </para>
    /// </remarks>
    public override string ToString()
    {
        StringBuilder builder = new();

        builder.Append(nameof(TransactionData));
        builder.Append(" { ");

        if (PrintMembers(builder))
        {
            builder.Append(' ');
        }

        builder.Append('}');

        return builder.ToString();
    }

    /// <summary>
    /// Appends the six renderable members to <paramref name="builder"/>. Declared explicitly so that
    /// the compiler does NOT synthesize a version that renders all nine.
    /// </summary>
    /// <param name="builder">The buffer to append to.</param>
    /// <returns>
    /// <see langword="true"/> always, because at least one member is always appended. The
    /// <see cref="bool"/> return exists to match the shape the record pattern expects, where it
    /// signals whether a separating space is needed before the closing brace.
    /// </returns>
    /// <remarks>
    /// Private, matching the accessibility the compiler would have used for a record struct. It is
    /// reachable from the sibling test project only indirectly, through
    /// <see cref="ToString"/>, which is the surface those tests should assert against anyway - it is
    /// the surface every accidental disclosure would travel through.
    /// </remarks>
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Dbms = ");
        builder.Append(Dbms);

        builder.Append(", ServerName = ");
        builder.Append(ServerName);

        builder.Append(", Database = ");
        builder.Append(Database);

        // An ACCOUNT NAME is not a secret and the response-side contract view carries it
        // [persistence.v1.proto:L2001-L2006], so it is rendered. Its neighbour is not.
        builder.Append(", LogId = ");
        builder.Append(LogId);

        // The isolation-level STRING, not a synchronization object [transactiondata.srs:L10].
        builder.Append(", Lock = ");
        builder.Append(Lock);

        builder.Append(", AutoCommit = ");
        builder.Append(AutoCommit);

        // AND THAT IS ALL SIX. THREE MEMBERS ARE ABSENT BY DESIGN and are not named here at all -
        // not even as a redaction marker - so that a search of this file finds no printing path for
        // any of them. See the block comment above ToString() for which three and why.
        return true;
    }
}
