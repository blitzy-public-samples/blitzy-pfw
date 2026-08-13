// ==============================================================================================
//  TransactionDataTests.cs - THE TRANSACTION DESCRIPTOR PARITY AND WRITE-ONLY-RULE SUITES
//  --------------------------------------------------------------------------------------------
//  UNDER TEST     services/persistence-service/PowerFramework.Persistence/Transactions/
//                     TransactionData.cs
//  ORACLE         ws_objects/pfw.thread.ext.pbl.src/transactiondata.srs               (READ ONLY)
//                 ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru            (READ ONLY)
//                 ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru     (READ ONLY)
//  CONTRACT       shared/PowerFramework.Contracts/Proto/persistence.v1.proto  (C-08 Transaction)
//
//  WHY THIS FILE EXISTS AT ALL. TransactionData.cs states, in the remarks on the type itself, that
//  "verification of the write-only rule is mandatory and must be a test rather than a claim", and
//  then enumerates the assertions the sibling test project must make. That obligation is discharged
//  here and nowhere else. The seven suites below are, in the type's own order:
//
//    0  The cleared state, the NINE-MEMBER CENSUS and the DECLARATION ORDER that is a wire contract.
//    1  ToString() never discloses the password - directly, and after the operation that MOVES it.
//    2  System.Text.Json carries neither the NAME nor the VALUE of the three ignored members.
//    3  A member can participate FULLY in equality and NOT AT ALL in rendering, simultaneously.
//    4  The seven-of-nine transfer, audited in BOTH directions, with the two omissions proven.
//    5  The four-cell veto-and-diagnostic matrix, including the cell that proves 2 does not veto.
//    6  The DBParm flag matrix, including the nesting case that makes NCharBind=1 alone inert.
//
//  ================ THE NINE-MEMBER ORDER IS A WIRE CONTRACT, NOT A STYLE CHOICE (C-K) =============
//  THE OBLIGATION THIS FILE CARRIES, STATED SO IT CANNOT BE MISREAD AS COSMETIC. The oracle declares
//  nine fields in one specific sequence [transactiondata.srs:L4-L12] and contract C-08 mirrors that
//  sequence POSITIONALLY, declaring `dbms = 1` through `userparm = 9` in exactly the same order
//  [shared/PowerFramework.Contracts/Proto/persistence.v1.proto, message TransactionDescriptor]. A
//  protobuf field number IS the wire identity of a field: renumber it and every previously encoded
//  message decodes into the wrong slot. So REORDERING THE PORTED TYPE'S MEMBERS - alphabetically, or
//  by grouping the credential-bearing ones, or by moving the lone boolean last where a C# author
//  would naturally put it - DESYNCHRONIZES the type, the wire contract and the oracle at once, and
//  NOTHING IN THE BUILD WOULD NOTICE, because C# resolves members by name and never by position.
//
//  Suite 0 is therefore the only mechanism that can catch it, and it asserts the correspondence in
//  BOTH available directions: reflectively over the C# type's own declaration order, and against the
//  generated descriptor's field numbers. The sibling TransactionServiceTests pins the PROTO order on
//  its own; what is pinned HERE is the CORRESPONDENCE between the ported type and that order, which
//  is the claim TransactionData.cs makes in its FIELD-ORDER AUDIT block and which no other test in
//  the repository covers.
//  ==========================================================================================
//
//  EVERY VALUE IN THIS FILE IS SYNTHETIC, AND WITH ONE DOCUMENTED EXCEPTION IS INVENTED HERE (C-F).
//  Not one password, account name, host name, connection string or parameter fragment is copied from
//  the legacy tree, from any of the eight catalogued in-source secret sites, or from any real system.
//  The password-shaped constants below are deliberately spelled so that they could not be mistaken
//  for a credential and so that a search of the repository finds them only in this file. They exist
//  to be searched FOR in rendered and serialized output - a test that asserts a secret is absent
//  needs a distinctive needle, and inventing the needle is the only way to have one without
//  importing a real secret. The single exception is the DBMS identifier, immediately below, and it is
//  an exception in the opposite direction: it is not invented BECAUSE it must not be.
//
//  THE ONE DELIBERATE EXCEPTION TO "INVENTED HERE" IS THE DBMS IDENTIFIER, AND IT IS AN EXCEPTION
//  BECAUSE C-E REQUIRES IT. No fabricated database means no fabricated DIALECT either, so the two
//  DBMS identifiers below are not invented: they are read off the generated contract enum members
//  for the ONLY TWO database types the repository evidences - DBT_MSSQL and DBT_ORACLE
//  [ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:L60-L61; persistence.v1.proto, enum
//  DatabaseType]. Reading them off the generated members rather than typing them out also discharges
//  AAP 0.7.2's instruction for this file - declare no SCREAMING_SNAKE identifier here, reference the
//  generated DBT_* / AC_* members instead - so this file declares no such identifier of its own.
//
//  NO DATABASE, NO CONNECTION, NO DataWindow AND NO NETWORK IS INVOLVED ANYWHERE IN THIS FILE (C-E).
//  The subject is an immutable value type; the two accessors take hooks, which are supplied here as
//  local delegates. That is the whole environment these suites need. A DBMS identifier is only ever
//  a value the descriptor CARRIES - nothing here composes a connection string, selects a provider,
//  resolves a dialect or performs any I/O, and the dialect-resolution rule the identifier feeds is
//  deliberately implemented one layer away, under Sql/Paging/.
//
//  NO PERFORMANCE PROPERTY IS ASSERTED ANYWHERE IN THIS FILE (AAP 0.8.5). There is no timing, no
//  throughput and no allocation assertion, because the repository publishes no latency budget, no
//  throughput target and no availability commitment against which such a claim could be made.
//
//  THE ORACLE'S DEFECTS AND ODDITIES ARE ASSERTED AS BEHAVIOUR, NOT FLAGGED AS BUGS (C-B). Six are
//  pinned below on purpose and none may be "fixed" in the subject to make a test read better:
//
//    * the veto test is a LITERAL EQUALITY against 1, so a deep prevention of 2 does NOT veto, even
//      though the very next function in the same legacy object uses the prevention predicate [:L429];
//    * a vetoed inbound call reports SUCCESS while writing nothing, so a caller cannot tell a vetoed
//      call from a completed one by its return value alone [:L343];
//    * the diagnostic slot is inspected TWICE, once inside the veto arm and once after it, and
//      collapsing them changes the outcome for a hook that vetoes AND reports [:L405, :L408];
//    * the parameterless outbound overload DISCARDS the return code, so a failure is invisible
//      through it [:L397];
//    * NCharBind is NESTED inside DisableBind, so NCharBind=1 alone is legal and entirely inert -
//      see the C-B block on suite 6 [n_cst_thread_task_sqlbase.sru:L127-L132];
//    * the two flag patterns are UNANCHORED and their comparison is TEXTUAL against "1", so a longer
//      keyword still matches and "=10" is true while "=2" is false.
//
//  Each is annotated at the point it is asserted, with the oracle line it came from, so that a future
//  reader cannot mistake any of them for a defect in this suite.
//
//  ORACLE STATUS
//  --------------------------------------------------------------------------------------------
//  Every ws_objects/** path named in this file is READ ONLY (constraint C-C). Each was read as
//  specification and is cited by locator; nothing here copies, reformats, moves, edits or deletes any
//  of them, and no test in this file loads, parses or executes any legacy artifact. The legacy tree
//  is the only statement of intended behaviour that exists for this structure, which is why every
//  behavioural expectation below carries the :L line reference it was taken from.
// ==============================================================================================

using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

using Google.Protobuf.Reflection;

using PowerFramework.Persistence.Transactions;

namespace PowerFramework.Persistence.Tests;

/// <summary>
/// The parity and disclosure suites for
/// <see cref="TransactionData"/> - the ported <c>transactiondata</c> structure, its two accessors,
/// its seven-of-nine transfer and its two <c>DBParm</c> flags.
/// </summary>
public sealed class TransactionDataTests
{
    // ==========================================================================================
    //  SYNTHETIC FIXTURES - INVENTED HERE, NOT IMPORTED (C-F)
    // ==========================================================================================

    /// <summary>
    /// The password needle. Distinctive enough to search for, and shaped so it cannot be mistaken
    /// for a real credential. It appears in this file only.
    /// </summary>
    private const string SyntheticPassword = "NEEDLE-logpass-K4Q9-synthetic-not-a-credential";

    /// <summary>
    /// The connection parameter needle. It carries BOTH flag keys set to <c>1</c> so that the same
    /// value serves the disclosure suites and demonstrates that the derived flag properties may be
    /// projected while the text they were parsed from stays hidden.
    /// </summary>
    private const string SyntheticDbParm =
        "DisableBind=1,NCharBind=1,Probe=NEEDLE-dbparm-V8N5-synthetic";

    /// <summary>The user parameter needle.</summary>
    private const string SyntheticUserParm = "NEEDLE-userparm-R2T6-synthetic";

    /// <summary>
    /// The DBMS identifier used throughout, and the ONE value in this file that is NOT invented here.
    /// </summary>
    /// <remarks>
    /// <para>
    /// C-E FORBIDS FABRICATING A DATABASE, AND THAT EXTENDS TO FABRICATING A DIALECT. The repository
    /// evidences exactly two database types and no more - <c>DBT_MSSQL = 0</c> and
    /// <c>DBT_ORACLE = 1</c> [ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:L60-L61] - and
    /// the published contract mirrors that with a two-member enum and no third value. So rather than
    /// invent a provider spelling, this fixture takes the identifier from the generated contract
    /// member for the first of the two, through <see cref="EvidencedDatabaseTypeName"/>.
    /// </para>
    /// <para>
    /// It also discharges AAP 0.7.2's instruction for this file: declare no SCREAMING_SNAKE
    /// identifier, reference the generated <c>DBT_*</c> / <c>AC_*</c> members instead. The text
    /// <c>DBT_MSSQL</c> appears in this assembly's output only because the generated member carries
    /// it, never because this file declared it.
    /// </para>
    /// <para>
    /// NOTHING RESOLVES A DIALECT FROM IT HERE. The legacy selector is a substring test on the
    /// upper-cased identifier [<c>:L356-L361</c>], and the port deliberately implements that one layer
    /// away under <c>Sql/Paging/</c> rather than on the descriptor, so this value is only ever
    /// carried, rendered and compared. No connection is opened.
    /// </para>
    /// </remarks>
    private static readonly string EvidencedDbms = EvidencedDatabaseTypeName(DatabaseType.DbtMssql);

    /// <summary>
    /// The OTHER evidenced database type, used to prove that a descriptor distinguishes the two
    /// dialects the repository actually has - and that there is no third one to exercise.
    /// </summary>
    private static readonly string EvidencedOtherDbms =
        EvidencedDatabaseTypeName(DatabaseType.DbtOracle);

    /// <summary>A synthetic server name.</summary>
    private const string SyntheticServerName = "synthetic-host-B2.invalid";

    /// <summary>A synthetic catalogue name.</summary>
    private const string SyntheticDatabase = "SYNTHETIC_CATALOGUE_C3";

    /// <summary>
    /// A synthetic account name. Rendered on purpose - an account name is not a secret and the
    /// response-side contract view carries it - which is what makes suite 1's assertions non-vacuous.
    /// </summary>
    private const string SyntheticLogId = "synthetic-account-D4";

    /// <summary>A synthetic isolation-level string. The legacy <c>lock</c> member is TEXT.</summary>
    private const string SyntheticLock = "SYNTHETIC ISOLATION E5";

    /// <summary>
    /// A descriptor with all nine members populated with distinguishable synthetic values, so that
    /// any member appearing where it should not is identifiable by sight.
    /// </summary>
    /// <returns>The populated descriptor.</returns>
    private static TransactionData FullyPopulated() => new()
    {
        Dbms = EvidencedDbms,
        ServerName = SyntheticServerName,
        Database = SyntheticDatabase,
        LogId = SyntheticLogId,
        LogPass = SyntheticPassword,
        DbParm = SyntheticDbParm,
        Lock = SyntheticLock,
        AutoCommit = true,
        UserParm = SyntheticUserParm,
    };

    /// <summary>
    /// A descriptor whose two NON-TRANSFERRED members hold values distinct from
    /// <see cref="FullyPopulated"/>'s, and whose seven transferred members are all cleared. It is the
    /// receiver side of every transfer audit: because its seven are empty and its two are set, a
    /// transfer that moved the wrong field set is visible in either direction.
    /// </summary>
    /// <returns>The receiver descriptor.</returns>
    private static TransactionData ReceiverWithOnlyTheTwoSet() => new()
    {
        AutoCommit = false,
        UserParm = "NEEDLE-receiver-userparm-W9Z1-synthetic",
    };

    /// <summary>
    /// A descriptor whose ALL NINE members differ from <see cref="FullyPopulated"/>'s, so that every
    /// one of the nine is independently discriminating in a transfer audit.
    /// </summary>
    /// <returns>The all-distinct descriptor.</returns>
    /// <remarks>
    /// STRICTLY STRONGER THAN <see cref="ReceiverWithOnlyTheTwoSet"/> FOR COUNTING PURPOSES, and that
    /// is the only reason it exists. Against a receiver whose seven are CLEARED, a member that was
    /// erased and a member that was correctly overwritten are indistinguishable afterwards, so a fold
    /// that cleared rather than copied would still pass. Every member here holds a distinct non-empty
    /// value - the credential included, which is what makes the outbound direction's "the caller keeps
    /// its OWN" assertion mean something.
    /// </remarks>
    private static TransactionData ReceiverWithAllNineDistinct() => new()
    {
        // The OTHER evidenced dialect, so this member differs without a third one being invented (C-E).
        Dbms = EvidencedOtherDbms,
        ServerName = "synthetic-host-Y7.invalid",
        Database = "SYNTHETIC_CATALOGUE_Z8",
        LogId = "synthetic-account-Q1",
        LogPass = "NEEDLE-receiver-logpass-T5B3-synthetic-not-a-credential",
        DbParm = "DisableBind=0,Probe=NEEDLE-receiver-dbparm-J2L4-synthetic",
        Lock = "SYNTHETIC ISOLATION H6",
        AutoCommit = false,
        UserParm = "NEEDLE-receiver-userparm-W9Z1-synthetic",
    };

    /// <summary>
    /// The nine oracle members of one descriptor as a name-to-value map, with the credential read
    /// through its named door because the property has no getter.
    /// </summary>
    /// <param name="descriptor">The descriptor to project.</param>
    /// <returns>The nine values, keyed by member name, in the oracle's declaration order.</returns>
    /// <remarks>
    /// A <see cref="Dictionary{TKey, TValue}"/> preserves insertion order for a map that is only ever
    /// added to, which is what lets the transfer audits report the members they found in the oracle's
    /// own order rather than in an arbitrary one. The audits assert the key sequence against the
    /// shared nine-member declaration before relying on it, so the two cannot drift apart.
    /// </remarks>
    private static IReadOnlyDictionary<string, object?> ProjectTheNine(TransactionData descriptor) =>
        new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [nameof(TransactionData.Dbms)] = descriptor.Dbms,
            [nameof(TransactionData.ServerName)] = descriptor.ServerName,
            [nameof(TransactionData.Database)] = descriptor.Database,
            [nameof(TransactionData.LogId)] = descriptor.LogId,

            // THROUGH THE NAMED DOOR. There is no getter to read, which is the write-only rule's
            // structural half - see the census suite. The test assembly reaches this internal member
            // only because the application project grants InternalsVisibleTo.
            [nameof(TransactionData.LogPass)] = descriptor.RevealLogPassForConnect(),
            [nameof(TransactionData.DbParm)] = descriptor.DbParm,
            [nameof(TransactionData.Lock)] = descriptor.Lock,
            [nameof(TransactionData.AutoCommit)] = descriptor.AutoCommit,
            [nameof(TransactionData.UserParm)] = descriptor.UserParm,
        };

    /// <summary>
    /// The names of the members of <paramref name="result"/> whose values came from
    /// <paramref name="source"/>, in the oracle's declaration order.
    /// </summary>
    /// <param name="result">The descriptor produced by a transfer.</param>
    /// <param name="source">The descriptor the transfer took its fields from.</param>
    /// <returns>The moved member names, in declaration order.</returns>
    /// <remarks>
    /// Only meaningful when every one of the nine differs between the source and the descriptor the
    /// transfer was applied to - otherwise a member that never moved would be counted as moved because
    /// it happened to agree already. Every caller establishes that precondition with an assertion
    /// before using the result, rather than assuming it.
    /// </remarks>
    private static string[] MembersTakenFromSource(TransactionData result, TransactionData source)
    {
        IReadOnlyDictionary<string, object?> resultValues = ProjectTheNine(result);
        IReadOnlyDictionary<string, object?> sourceValues = ProjectTheNine(source);

        return [.. TheNineInOracleOrder()
            .Select(member => member.Name)
            .Where(name => Equals(resultValues[name], sourceValues[name]))];
    }

    /// <summary>
    /// Every public instance property the type declares, keyed by name. Used by the reflection-based
    /// audits so each one states its expectation against a single shared reading of the surface.
    /// </summary>
    /// <returns>The property map.</returns>
    private static IReadOnlyDictionary<string, PropertyInfo> PublicInstanceProperties() =>
        typeof(TransactionData)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .ToDictionary(property => property.Name, StringComparer.Ordinal);

    /// <summary>
    /// The nine oracle members in the oracle's own declaration order, each paired with the type
    /// <c>transactiondata.srs</c> declares for it and with the C-08 field number that mirrors it.
    /// </summary>
    /// <returns>The nine expectations, in order.</returns>
    /// <remarks>
    /// ONE DECLARATION SHARED BY EVERY SUITE-0 AUDIT, so the census, the ordering audit, the
    /// field-number correspondence and the string-versus-boolean audit cannot disagree with one
    /// another about what the contract is. The order of this array IS the assertion the ordering
    /// audits make - see the wire-contract block in this file's header for why that is not cosmetic.
    /// </remarks>
    private static (string Name, Type Type, int FieldNumber)[] TheNineInOracleOrder() =>
    [
        // #  transactiondata.srs   persistence.v1.proto TransactionDescriptor
        // 1  string  dbms       L4    string dbms       = 1
        (nameof(TransactionData.Dbms), typeof(string), 1),

        // 2  string  servername L5    string servername = 2
        (nameof(TransactionData.ServerName), typeof(string), 2),

        // 3  string  database   L6    string database   = 3
        (nameof(TransactionData.Database), typeof(string), 3),

        // 4  string  logid      L7    string logid      = 4
        (nameof(TransactionData.LogId), typeof(string), 4),

        // 5  string  logpass    L8    string logpass    = 5   <- FIFTH, and write-only
        (nameof(TransactionData.LogPass), typeof(string), 5),

        // 6  string  dbparm     L9    string dbparm     = 6
        (nameof(TransactionData.DbParm), typeof(string), 6),

        // 7  string  lock       L10   string lock       = 7
        (nameof(TransactionData.Lock), typeof(string), 7),

        // 8  boolean autocommit L11   bool   autocommit = 8   <- the boolean is EIGHTH, not last
        (nameof(TransactionData.AutoCommit), typeof(bool), 8),

        // 9  string  userparm   L12   string userparm   = 9
        (nameof(TransactionData.UserParm), typeof(string), 9),
    ];

    /// <summary>
    /// The three members the type derives rather than stores - the presence flag and the two
    /// <c>DBParm</c> flags. Named here so the census below can prove there is no TENTH oracle member
    /// hiding among them.
    /// </summary>
    /// <returns>The three derived member names.</returns>
    private static string[] TheThreeDerivedMembers() =>
    [
        nameof(TransactionData.HasCredential),
        nameof(TransactionData.IsBindDisabled),
        nameof(TransactionData.IsNCharBindingEnabled),
    ];

    /// <summary>
    /// The PROTOCOL-DEFINITION name of one of the two evidenced database types, read off the
    /// generated enum member's own <see cref="OriginalNameAttribute"/>.
    /// </summary>
    /// <param name="databaseType">The evidenced database type to name.</param>
    /// <returns>
    /// The identifier exactly as the protocol definition spells it - which for the two evidenced
    /// types is the same spelling the oracle uses
    /// [ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:L60-L61].
    /// </returns>
    /// <remarks>
    /// <para>
    /// WHY READ IT RATHER THAN TYPE IT. Two constraints meet on this one line. C-E forbids inventing
    /// a dialect, so the identifier has to come from the two the repository evidences; AAP 0.7.2
    /// forbids this file declaring a SCREAMING_SNAKE identifier and directs the generated
    /// <c>DBT_*</c> / <c>AC_*</c> members be referenced instead. Reading the name off the generated
    /// member satisfies both at once, and it cannot drift: rename the member in the protocol
    /// definition and this returns the new spelling rather than a stale copy of the old one.
    /// </para>
    /// <para>
    /// The lookup is non-nullable-asserted on both steps because a missing member or a missing
    /// attribute would mean the generated enum no longer matches the protocol definition, which is a
    /// contract fault worth failing loudly on rather than degrading around.
    /// </para>
    /// </remarks>
    private static string EvidencedDatabaseTypeName(DatabaseType databaseType) =>
        typeof(DatabaseType)
            .GetField(databaseType.ToString(), BindingFlags.Public | BindingFlags.Static)!
            .GetCustomAttribute<OriginalNameAttribute>()!
            .Name;

    // ==========================================================================================
    //  SUITE 0 - THE CLEARED STATE, THE NINE-MEMBER CENSUS AND THE ORDER THAT IS A WIRE CONTRACT
    // ==========================================================================================

    /// <summary>
    /// <see cref="TransactionData.Empty"/> is the default value and not a second definition of the
    /// cleared state.
    /// </summary>
    [Fact]
    public void Empty_IsExactlyTheDefaultValue()
    {
        Assert.Equal(default, TransactionData.Empty);
    }

    /// <summary>
    /// The canonicalising backing fields make null, empty and unset ONE equivalence class - which is
    /// the property the equality suite below depends on, because the generated equality compares
    /// FIELDS rather than properties.
    /// </summary>
    [Fact]
    public void NullEmptyAndUnsetAreOneEquivalenceClass()
    {
        TransactionData unset = default;

        TransactionData assignedNulls = new()
        {
            Dbms = null,
            ServerName = null,
            Database = null,
            LogId = null,
            LogPass = null,
            DbParm = null,
            Lock = null,
            UserParm = null,
        };

        TransactionData assignedEmpties = new()
        {
            Dbms = string.Empty,
            ServerName = string.Empty,
            Database = string.Empty,
            LogId = string.Empty,
            LogPass = string.Empty,
            DbParm = string.Empty,
            Lock = string.Empty,
            UserParm = string.Empty,
        };

        Assert.Equal(unset, assignedNulls);
        Assert.Equal(unset, assignedEmpties);
        Assert.Equal(unset.GetHashCode(), assignedNulls.GetHashCode());
        Assert.Equal(unset.GetHashCode(), assignedEmpties.GetHashCode());
    }

    /// <summary>
    /// Every string member observes as <see cref="string.Empty"/> rather than <see langword="null"/>
    /// when cleared, matching PowerBuilder's initialisation of an unassigned <c>string</c>.
    /// </summary>
    [Fact]
    public void ClearedStringMembersObserveAsEmptyRatherThanNull()
    {
        TransactionData subject = default;

        Assert.Equal(string.Empty, subject.Dbms);
        Assert.Equal(string.Empty, subject.ServerName);
        Assert.Equal(string.Empty, subject.Database);
        Assert.Equal(string.Empty, subject.LogId);

        // THE CREDENTIAL IS READ THROUGH THE NAMED DOOR, because the property has no getter at all.
        // The test assembly can reach it only because the csproj grants InternalsVisibleTo, which is what
        // keeps the write-only posture testable instead of merely asserted.
        Assert.Equal(string.Empty, subject.RevealLogPassForConnect());
        Assert.False(subject.HasCredential);
        Assert.Equal(string.Empty, subject.DbParm);
        Assert.Equal(string.Empty, subject.Lock);
        Assert.Equal(string.Empty, subject.UserParm);
        Assert.False(subject.AutoCommit);
    }

    /// <summary>
    /// All nine oracle members are present, each with the type
    /// <c>transactiondata.srs</c> declares - the field-order audit recorded on the type, expressed as
    /// a test so a tenth member or a changed type cannot land unnoticed.
    /// </summary>
    [Fact]
    public void TheNineOracleMembersArePresentWithTheOraclesTypes()
    {
        IReadOnlyDictionary<string, PropertyInfo> properties = PublicInstanceProperties();

        // transactiondata.srs:L4-L12, in the oracle's own declaration order.
        (string Name, Type Type, int FieldNumber)[] expected = TheNineInOracleOrder();

        Assert.Equal(9, expected.Length);

        foreach ((string name, Type type, int _) in expected)
        {
            Assert.Contains(name, properties.Keys);
            Assert.Equal(type, properties[name].PropertyType);
        }

        // NINE MEMBERS, AND EXACTLY ONE OF THEM IS WRITE-ONLY. The census is the right place to pin this
        // because a reintroduced getter is precisely the kind of change that looks like a convenience and
        // reopens the credential-exposure finding. Every other member reads; this one does not.
        Assert.False(properties[nameof(TransactionData.LogPass)].CanRead);

        foreach ((string name, Type _, int _) in expected)
        {
            if (name != nameof(TransactionData.LogPass))
            {
                Assert.True(properties[name].CanRead, name);
            }
        }
    }

    /// <summary>
    /// THE ORDER, ASSERTED REFLECTIVELY OVER THE TYPE'S OWN MEMBERS. The nine appear in the C# type
    /// in the oracle's declaration order [<c>transactiondata.srs</c>:L4-L12], which contract C-08
    /// mirrors positionally as <c>dbms = 1</c> through <c>userparm = 9</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A REORDERING HERE IS A WIRE-CONTRACT BREAK, NOT A COSMETIC CHANGE, which is the whole reason
    /// this test exists - see the wire-contract block in this file's header. C# resolves members by
    /// name and never by position, so nothing else in the build can detect a reordering: the type
    /// would keep compiling, every other test in the repository would keep passing, and only the
    /// three-way correspondence between the oracle, this type and the field numbers would have
    /// silently come apart.
    /// </para>
    /// <para>
    /// WHY <see cref="MemberInfo.MetadataToken"/> AND NOT THE REFLECTION ORDER. The order in which
    /// <see cref="Type.GetProperties()"/> returns members is explicitly unspecified by the runtime,
    /// so relying on it would make this test's meaning depend on an implementation detail. Metadata
    /// tokens, by contrast, are assigned by the compiler as it emits members, so ordering by token
    /// recovers the DECLARATION order from the assembly itself. The three derived members are
    /// filtered out first, because they are declared interleaved with the nine - the presence flag
    /// sits between the credential and the parameter string, and the two flag properties sit after
    /// the transfer folds - and their positions are deliberately not contract.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheNineAppearInTheOraclesDeclarationOrder()
    {
        string[] expected = [.. TheNineInOracleOrder().Select(member => member.Name)];

        string[] declared = [.. typeof(TransactionData)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => !TheThreeDerivedMembers().Contains(property.Name, StringComparer.Ordinal))
            .OrderBy(property => property.MetadataToken)
            .Select(property => property.Name)];

        Assert.Equal(expected, declared);
    }

    /// <summary>
    /// The three-way correspondence: the ported type's declaration order, position for position,
    /// against the GENERATED DESCRIPTOR's field numbers for contract C-08's
    /// <c>TransactionDescriptor</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THIS IS THE ASSERTION THAT MAKES THE WIRE CONTRACT CHECKABLE (C-K). The two sides are read
    /// from two independent artifacts - the C# metadata of the ported type, and the descriptor
    /// protobuf itself compiles into the contracts assembly - so agreement between them cannot be an
    /// artefact of one file being copied from the other. The oracle is the third side, and it is
    /// pinned by the comments in <see cref="TheNineInOracleOrder"/>, each carrying the
    /// <c>transactiondata.srs</c> line the member came from.
    /// </para>
    /// <para>
    /// DELIBERATELY COMPLEMENTARY TO, NOT A DUPLICATE OF, the sibling suite for contract C-08, which
    /// pins the descriptor's own field order in isolation. What is asserted here is the
    /// CORRESPONDENCE - that member <c>n</c> of this type is field <c>n</c> on the wire - which no
    /// other test in the repository covers and which is the claim <c>TransactionData.cs</c> makes in
    /// its own FIELD-ORDER AUDIT block.
    /// </para>
    /// <para>
    /// The field numbers are asserted to be exactly 1 through 9 with no gap, because a gap would mean
    /// a slot had been reserved or retired and the positional mirror would no longer hold.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheNineMirrorTheContractsFieldNumbersPositionForPosition()
    {
        (string Name, Type Type, int FieldNumber)[] expected = TheNineInOracleOrder();

        // The C-08 descriptor, in field-number order, straight out of the generated contract.
        IList<FieldDescriptor> wireFields = TransactionDescriptor.Descriptor.Fields.InFieldNumberOrder();

        Assert.Equal(expected.Length, wireFields.Count);

        for (int position = 0; position < expected.Length; position++)
        {
            // 1..9 with no gap: the positional mirror only holds while the numbering is contiguous.
            Assert.Equal(position + 1, expected[position].FieldNumber);
            Assert.Equal(expected[position].FieldNumber, wireFields[position].FieldNumber);

            // The protocol definition spells its fields in the oracle's lower-case, so the comparison
            // is case-insensitive on purpose: `logpass` and `LogPass` are the same field, and the
            // difference is a naming convention rather than a contract difference. `servername`
            // against `ServerName` is the case that makes this necessary.
            Assert.Equal(
                expected[position].Name,
                wireFields[position].Name,
                ignoreCase: true);
        }
    }

    /// <summary>
    /// <see cref="TransactionData.LogPass"/> is member FIVE - asserted on its own rather than only as
    /// one row of the ordering audit, because its position is the one that carries an obligation.
    /// </summary>
    /// <remarks>
    /// Field 5 is the slot contract C-08's RESPONSE-side view keeps permanently
    /// <c>reserved</c> so no future field can be numbered into a credential's place. Pinning the
    /// position here is what ties the write-only rule to a specific wire slot rather than to a name
    /// that could be moved.
    /// </remarks>
    [Fact]
    public void TheCredentialIsMemberFive()
    {
        (string Name, Type Type, int FieldNumber)[] nine = TheNineInOracleOrder();

        // FIVE, one-based, exactly as the oracle declares it [transactiondata.srs:L8] and exactly as
        // the contract numbers it.
        Assert.Equal(nameof(TransactionData.LogPass), nine[4].Name);
        Assert.Equal(5, nine[4].FieldNumber);
        Assert.Equal(
            5,
            TransactionDescriptor.Descriptor.FindFieldByName("logpass").FieldNumber);
    }

    /// <summary>
    /// <see cref="TransactionData.AutoCommit"/> is the ONLY non-string member of the nine, it is a
    /// <see cref="bool"/>, and it is EIGHTH rather than last.
    /// </summary>
    /// <remarks>
    /// <para>
    /// EIGHT STRINGS AND ONE BOOLEAN, WITH THE BOOLEAN EIGHTH. That is the one detail of the layout a
    /// C# author is most likely to get wrong, because a trailing boolean is the more natural
    /// arrangement and moving it there costs nothing at compile time and breaks the positional mirror
    /// completely.
    /// </para>
    /// <para>
    /// A BOOLEAN, NOT THE THREE-VALUED COMMAND-LEVEL MODE. The oracle declares
    /// <c>boolean autocommit</c> [transactiondata.srs:L11] and C-08 mirrors it as a
    /// <c>bool</c>, so the descriptor carries two states. The THREE-valued autocommit mode - the
    /// generated <c>AC_OFF</c> / <c>AC_ON</c> / <c>AC_NATIVE</c> member set - is a per-statement
    /// concern belonging to contract C-07 and is deliberately NOT this descriptor's member. Its zero
    /// value is referenced below rather than spelled out, per AAP 0.7.2, and it lines up with the
    /// cleared descriptor's <see langword="false"/>: the legacy task layer defaults to the same
    /// off state and then ERASES the descriptor's flag outright
    /// [n_cst_thread_task_sqlbase.sru:L118-L119], which is why the two live at different levels.
    /// </para>
    /// </remarks>
    [Fact]
    public void AutoCommitIsTheOnlyNonStringMemberAndIsTheEighth()
    {
        (string Name, Type Type, int FieldNumber)[] nine = TheNineInOracleOrder();

        string[] nonString = [.. nine
            .Where(member => member.Type != typeof(string))
            .Select(member => member.Name)];

        string[] expectedNonString = [nameof(TransactionData.AutoCommit)];

        Assert.Equal(expectedNonString, nonString);
        Assert.Equal(9, nine.Length);

        // EIGHTH, one-based - and userparm, not the boolean, is the one that comes last.
        Assert.Equal(nameof(TransactionData.AutoCommit), nine[7].Name);
        Assert.Equal(typeof(bool), nine[7].Type);
        Assert.Equal(nameof(TransactionData.UserParm), nine[8].Name);

        // The reflected member agrees with the declaration, and the wire field agrees with both.
        Assert.Equal(
            typeof(bool),
            PublicInstanceProperties()[nameof(TransactionData.AutoCommit)].PropertyType);
        Assert.Equal(
            FieldType.Bool,
            TransactionDescriptor.Descriptor.FindFieldByName("autocommit").FieldType);

        // The cleared descriptor's flag is the off state, which is the state the generated C-07 mode
        // spells as its zero member. Referenced rather than re-declared (AAP 0.7.2).
        Assert.False(TransactionData.Empty.AutoCommit);
        Assert.Equal(0, (int)AutoCommitMode.AcOff);
    }

    /// <summary>
    /// THE FIXTURE INTEGRITY CHECK FOR C-E. The two DBMS identifiers this file uses really are the
    /// two evidenced database types, they are distinct, and neither is empty.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WITHOUT THIS, TWO OTHER SUITES COULD PASS VACUOUSLY. Several assertions elsewhere are of the
    /// form "the rendered output CONTAINS the DBMS identifier", and
    /// <see cref="string.Contains(string)"/> is trivially true for the empty string - so a fixture
    /// that silently resolved to nothing would turn those positive controls into no-ops while still
    /// reporting green. Asserting the fixture is non-empty is what keeps them honest.
    /// </para>
    /// <para>
    /// It is also the auditable statement of the C-E position: exactly two dialects are exercised in
    /// this file, they are the two the repository evidences, and their spellings come from the
    /// generated contract members rather than from anything typed here.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheOnlyTwoDialectsExercisedAreTheTwoTheRepositoryEvidences()
    {
        Assert.NotEmpty(EvidencedDbms);
        Assert.NotEmpty(EvidencedOtherDbms);
        Assert.NotEqual(EvidencedDbms, EvidencedOtherDbms);

        // The generated enum has exactly two members and no third dialect to reach for.
        Assert.Equal(2, Enum.GetValues<DatabaseType>().Length);

        // And the identifiers are the protocol definition's own spellings, which are also the
        // oracle's [n_cst_thread_trans.sru:L60-L61]. Asserted through the same accessor the fixtures
        // use, so a change to the generated member cannot leave the fixtures and this check disagreeing.
        Assert.Equal(EvidencedDatabaseTypeName(DatabaseType.DbtMssql), EvidencedDbms);
        Assert.Equal(EvidencedDatabaseTypeName(DatabaseType.DbtOracle), EvidencedOtherDbms);

        // A descriptor CARRIES either one without preferring or resolving either: no dialect selection
        // happens on this type, and no connection is opened anywhere in this file (C-E).
        TransactionData mssql = new() { Dbms = EvidencedDbms };
        TransactionData oracle = new() { Dbms = EvidencedOtherDbms };

        Assert.Equal(EvidencedDbms, mssql.Dbms);
        Assert.Equal(EvidencedOtherDbms, oracle.Dbms);
        Assert.NotEqual(mssql, oracle);
    }

    /// <summary>
    /// THERE IS NO TENTH MEMBER. The public instance surface is exactly the nine oracle members plus
    /// the three the type DERIVES, and nothing else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE CENSUS IS EXACT RATHER THAN A LOWER BOUND, and that is the point: a "contains all nine"
    /// assertion cannot detect an ADDED member, and an added member is the realistic failure. A
    /// connection timeout, a retry policy, a port, an application name or an encryption toggle would
    /// each look like an obvious improvement on a connection descriptor and each would be a NEW
    /// capability rather than a port, which C-B forbids. It would also break the positional mirror
    /// the moment anyone numbered it onto the wire.
    /// </para>
    /// <para>
    /// THE THREE DERIVED MEMBERS ARE NAMED, NOT PATTERN-MATCHED. Filtering by a naming convention -
    /// anything beginning with "Is" or "Has" - would let a tenth stored member slip in under a
    /// conforming name. Naming them means a fourth derived member is a deliberate edit to this test.
    /// </para>
    /// </remarks>
    [Fact]
    public void ThePublicSurfaceIsTheNineOracleMembersPlusExactlyThreeDerivedOnes()
    {
        string[] expected = [.. TheNineInOracleOrder()
            .Select(member => member.Name)
            .Concat(TheThreeDerivedMembers())
            .Order(StringComparer.Ordinal)];

        string[] actual = [.. PublicInstanceProperties()
            .Keys
            .Order(StringComparer.Ordinal)];

        Assert.Equal(expected, actual);
        Assert.Equal(12, actual.Length);
    }

    // ==========================================================================================
    //  SUITE 1 - ToString() NEVER DISCLOSES THE PASSWORD
    //  ----------------------------------------------------------------------------------------
    //  MANDATED ASSERTION 1 of the three the type enumerates: build a descriptor whose password
    //  member holds a distinctive synthetic value, call ToString(), assert the returned string does
    //  NOT contain that value - and assert the same for a descriptor built through
    //  WithConnectionFieldsFrom, since that is the operation that MOVES the member.
    // ==========================================================================================

    /// <summary>
    /// The rendered form contains neither the password value nor the member's NAME. The name matters
    /// as much as the value: a rendering that printed <c>LogPass = ***</c> would confirm the member
    /// exists and would still be a change in what the type discloses.
    /// </summary>
    [Fact]
    public void ToString_ContainsNeitherThePasswordValueNorItsMemberName()
    {
        string rendered = FullyPopulated().ToString();

        Assert.DoesNotContain(SyntheticPassword, rendered, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(TransactionData.LogPass), rendered, StringComparison.Ordinal);
    }

    /// <summary>
    /// The same holds for a descriptor produced by <see cref="TransactionData.WithConnectionFieldsFrom"/>
    /// - the operation that MOVES the password from one descriptor into another. This is the second
    /// half of mandated assertion 1, and it is a separate test because the transfer builds a NEW
    /// value through a <see langword="with"/> expression and could in principle render differently.
    /// </summary>
    [Fact]
    public void ToString_AfterTheTransferThatMovesThePassword_StillDisclosesNothing()
    {
        TransactionData moved = ReceiverWithOnlyTheTwoSet().WithConnectionFieldsFrom(FullyPopulated());

        // The password really did move - proven positively, so the assertion below is not vacuous.
        Assert.Equal(SyntheticPassword, moved.RevealLogPassForConnect());
        Assert.True(moved.HasCredential);

        string rendered = moved.ToString();

        Assert.DoesNotContain(SyntheticPassword, rendered, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(TransactionData.LogPass), rendered, StringComparison.Ordinal);
    }

    /// <summary>
    /// The connection parameter string and the user parameter are withheld on the same terms as the
    /// password - name and value both.
    /// </summary>
    [Fact]
    public void ToString_AlsoWithholdsDbParmAndUserParm_NameAndValue()
    {
        string rendered = FullyPopulated().ToString();

        Assert.DoesNotContain(SyntheticDbParm, rendered, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(TransactionData.DbParm), rendered, StringComparison.Ordinal);
        Assert.DoesNotContain(SyntheticUserParm, rendered, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(TransactionData.UserParm), rendered, StringComparison.Ordinal);
    }

    /// <summary>
    /// The POSITIVE control for suite 1. Rendering discloses exactly the six permitted members, in
    /// the declared order, in the record-shaped envelope - asserted as one exact string. Without
    /// this, every "does not contain" above would also pass for a <c>ToString</c> that
    /// returned the empty string.
    /// </summary>
    [Fact]
    public void ToString_RendersExactlyTheSixPermittedMembersInDeclarationOrder()
    {
        string expected =
            $"{nameof(TransactionData)} {{ " +
            $"{nameof(TransactionData.Dbms)} = {EvidencedDbms}, " +
            $"{nameof(TransactionData.ServerName)} = {SyntheticServerName}, " +
            $"{nameof(TransactionData.Database)} = {SyntheticDatabase}, " +
            $"{nameof(TransactionData.LogId)} = {SyntheticLogId}, " +
            $"{nameof(TransactionData.Lock)} = {SyntheticLock}, " +
            $"{nameof(TransactionData.AutoCommit)} = {true} }}";

        Assert.Equal(expected, FullyPopulated().ToString());
    }

    /// <summary>
    /// The cleared descriptor renders without throwing and discloses nothing, which is the claim the
    /// type makes about <see cref="TransactionData.Empty"/> being safe to render.
    /// </summary>
    [Fact]
    public void ToString_OnTheClearedDescriptor_RendersTheSixEmptyMembersAndDoesNotThrow()
    {
        string rendered = TransactionData.Empty.ToString();

        Assert.Equal(
            $"{nameof(TransactionData)} {{ Dbms = , ServerName = , Database = , LogId = , " +
            $"Lock = , AutoCommit = {false} }}",
            rendered);
    }

    /// <summary>
    /// Both rendering members are DECLARED on the type rather than synthesized by the compiler.
    /// Declaring only one would leave the other generated, and a generated
    /// <c>PrintMembers</c> renders all nine - a live disclosure the moment a later edit routed
    /// through it. <c>PrintMembers</c> must also stay PRIVATE, so that
    /// <see cref="TransactionData.ToString"/> is the only rendering surface a caller can reach.
    /// </summary>
    [Fact]
    public void BothRenderingMembersAreDeclaredOnTheType_AndPrintMembersIsPrivate()
    {
        MethodInfo? toString = typeof(TransactionData).GetMethod(
            nameof(ToString),
            BindingFlags.Instance | BindingFlags.Public,
            Type.EmptyTypes);

        Assert.NotNull(toString);
        Assert.Equal(typeof(TransactionData), toString.DeclaringType);

        MethodInfo? printMembers = typeof(TransactionData).GetMethod(
            "PrintMembers",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        Assert.NotNull(printMembers);
        Assert.True(printMembers.IsPrivate);
        Assert.Equal(typeof(TransactionData), printMembers.DeclaringType);
    }

    // ==========================================================================================
    //  SUITE 2 - System.Text.Json CARRIES NEITHER THE NAME NOR THE VALUE
    //  ----------------------------------------------------------------------------------------
    //  MANDATED ASSERTION 2: round-trip the descriptor through System.Text.Json and assert the
    //  serialized document contains neither the member name nor its value - the guarantee
    //  [JsonIgnore] provides and that ToString() alone does NOT. Asserted for the password, for
    //  DbParm and for UserParm, because all three carry the attribute.
    // ==========================================================================================

    /// <summary>
    /// Serialization withholds all three ignored members by NAME.
    /// </summary>
    [Fact]
    public void Json_CarriesNoneOfTheThreeIgnoredMemberNames()
    {
        string json = JsonSerializer.Serialize(FullyPopulated());

        Assert.DoesNotContain(nameof(TransactionData.LogPass), json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(nameof(TransactionData.DbParm), json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(nameof(TransactionData.UserParm), json, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Serialization withholds all three ignored members by VALUE. Checked separately from the names
    /// because a serializer configured with a naming policy could hide a name while still emitting
    /// its value.
    /// </summary>
    [Fact]
    public void Json_CarriesNoneOfTheThreeIgnoredMemberValues()
    {
        string json = JsonSerializer.Serialize(FullyPopulated());

        Assert.DoesNotContain(SyntheticPassword, json, StringComparison.Ordinal);
        Assert.DoesNotContain(SyntheticDbParm, json, StringComparison.Ordinal);
        Assert.DoesNotContain(SyntheticUserParm, json, StringComparison.Ordinal);
    }

    /// <summary>
    /// The POSITIVE control for suite 2: the six disclosable members ARE serialized, name and value.
    /// Without it the two assertions above would also pass for a type that serialized to <c>{}</c>.
    /// </summary>
    [Fact]
    public void Json_DoesCarryTheSixDisclosableMembers()
    {
        string json = JsonSerializer.Serialize(FullyPopulated());

        Assert.Contains(nameof(TransactionData.Dbms), json, StringComparison.Ordinal);
        Assert.Contains(EvidencedDbms, json, StringComparison.Ordinal);
        Assert.Contains(nameof(TransactionData.ServerName), json, StringComparison.Ordinal);
        Assert.Contains(SyntheticServerName, json, StringComparison.Ordinal);
        Assert.Contains(nameof(TransactionData.Database), json, StringComparison.Ordinal);
        Assert.Contains(SyntheticDatabase, json, StringComparison.Ordinal);
        Assert.Contains(nameof(TransactionData.LogId), json, StringComparison.Ordinal);
        Assert.Contains(SyntheticLogId, json, StringComparison.Ordinal);
        Assert.Contains(nameof(TransactionData.Lock), json, StringComparison.Ordinal);
        Assert.Contains(SyntheticLock, json, StringComparison.Ordinal);
        Assert.Contains(nameof(TransactionData.AutoCommit), json, StringComparison.Ordinal);
    }

    /// <summary>
    /// A serialization round trip DROPS the three ignored members and preserves the other six. This
    /// is the consequence callers must design around: a descriptor that has travelled through JSON is
    /// not a connectable descriptor, because its password and parameter string are gone.
    /// </summary>
    [Fact]
    public void Json_RoundTrip_DropsTheThreeIgnoredMembersAndPreservesTheOtherSix()
    {
        TransactionData original = FullyPopulated();

        TransactionData revived =
            JsonSerializer.Deserialize<TransactionData>(JsonSerializer.Serialize(original));

        Assert.Equal(EvidencedDbms, revived.Dbms);
        Assert.Equal(SyntheticServerName, revived.ServerName);
        Assert.Equal(SyntheticDatabase, revived.Database);
        Assert.Equal(SyntheticLogId, revived.LogId);
        Assert.Equal(SyntheticLock, revived.Lock);
        Assert.True(revived.AutoCommit);

        Assert.Equal(string.Empty, revived.RevealLogPassForConnect());
        Assert.False(revived.HasCredential);
        Assert.Equal(string.Empty, revived.DbParm);
        Assert.Equal(string.Empty, revived.UserParm);

        // And therefore the revived descriptor is NOT equal to the original - the three dropped
        // members participate in equality even though they do not travel.
        Assert.NotEqual(original, revived);
    }

    /// <summary>
    /// The transfer that MOVES the password produces a descriptor whose serialization is equally
    /// silent - the serialization counterpart of the second half of mandated assertion 1.
    /// </summary>
    [Fact]
    public void Json_AfterTheTransferThatMovesThePassword_StillCarriesNoneOfTheThree()
    {
        TransactionData moved = ReceiverWithOnlyTheTwoSet().WithConnectionFieldsFrom(FullyPopulated());

        string json = JsonSerializer.Serialize(moved);

        Assert.DoesNotContain(SyntheticPassword, json, StringComparison.Ordinal);
        Assert.DoesNotContain(SyntheticDbParm, json, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(TransactionData.LogPass), json, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Exactly three members carry <see cref="JsonIgnoreAttribute"/>, and they are exactly the three
    /// the write-only rule names. A fourth would be an undeclared narrowing of the contract; a
    /// missing one would be a leak.
    /// </summary>
    [Fact]
    public void ExactlyThreeMembersCarryJsonIgnore_AndTheyAreTheThreeNamedOnes()
    {
        string[] ignored = [.. PublicInstanceProperties()
            .Values
            .Where(property => property.GetCustomAttribute<JsonIgnoreAttribute>() is not null)
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)];

        string[] expected =
        [
            nameof(TransactionData.DbParm),
            nameof(TransactionData.LogPass),
            nameof(TransactionData.UserParm),
        ];

        Assert.Equal(expected, ignored);
    }

    /// <summary>
    /// The derived <c>DBParm</c> flag properties ARE serialized - and that is not a leak, because
    /// they carry two booleans rather than the parameter text they were parsed from. Recorded as a
    /// test so the distinction is deliberate and cannot be mistaken later for an oversight.
    /// </summary>
    [Fact]
    public void Json_CarriesTheDerivedFlagsButNotTheTextTheyWereParsedFrom()
    {
        string json = JsonSerializer.Serialize(FullyPopulated());

        Assert.Contains(nameof(TransactionData.IsBindDisabled), json, StringComparison.Ordinal);
        Assert.Contains(nameof(TransactionData.IsNCharBindingEnabled), json, StringComparison.Ordinal);
        Assert.DoesNotContain(SyntheticDbParm, json, StringComparison.Ordinal);
    }

    // ==========================================================================================
    //  SUITE 3 - FULL PARTICIPATION IN EQUALITY, ZERO PARTICIPATION IN RENDERING
    //  ----------------------------------------------------------------------------------------
    //  MANDATED ASSERTION 3: two descriptors differing ONLY in the password member are NOT equal,
    //  their GetHashCode results are PERMITTED to differ, and ToString renders the two IDENTICALLY.
    //  Those two properties held simultaneously are the reason this type is hand-written rather than
    //  a plain record - a redaction implemented by dropping the member from equality would silently
    //  merge two distinct connection identities in the transaction pool, which keys its
    //  reference-counted entries on whole-descriptor equality [n_cst_thread_trans_pool.sru:L138].
    // ==========================================================================================

    /// <summary>
    /// Two descriptors differing only in the password are NOT equal - through
    /// <see cref="object.Equals(object)"/>, through the strongly typed overload, and through both
    /// generated operators.
    /// </summary>
    [Fact]
    public void TwoDescriptorsDifferingOnlyInThePasswordAreNotEqual()
    {
        TransactionData first = FullyPopulated();
        TransactionData second = first with { LogPass = SyntheticPassword + "-VARIANT" };

        Assert.NotEqual(first, second);
        Assert.False(first.Equals(second));
        Assert.False(first.Equals((object)second));
        Assert.False(first == second);
        Assert.True(first != second);
    }

    /// <summary>
    /// The other half of the pair: the same two descriptors render IDENTICALLY. Asserted as string
    /// equality of the two rendered forms rather than as an absence check, because identity of output
    /// is the actual claim - it is what makes the difference undetectable through rendering.
    /// </summary>
    [Fact]
    public void TwoDescriptorsDifferingOnlyInThePasswordRenderIdentically()
    {
        TransactionData first = FullyPopulated();
        TransactionData second = first with { LogPass = SyntheticPassword + "-VARIANT" };

        Assert.Equal(first.ToString(), second.ToString());
    }

    /// <summary>
    /// <see cref="TransactionData.DbParm"/> and <see cref="TransactionData.UserParm"/> behave exactly
    /// as the password does: they distinguish descriptors for equality and are invisible to rendering.
    /// </summary>
    [Fact]
    public void TheOtherTwoWithheldMembersAlsoDistinguishForEqualityWhileNotRendering()
    {
        TransactionData baseline = FullyPopulated();

        TransactionData differentDbParm = baseline with { DbParm = "DisableBind=0,Probe=OTHER" };
        TransactionData differentUserParm = baseline with { UserParm = SyntheticUserParm + "-VARIANT" };

        Assert.NotEqual(baseline, differentDbParm);
        Assert.NotEqual(baseline, differentUserParm);

        // DbParm changes the DERIVED flags, and those are rendered by neither member - so the
        // rendered forms still match. UserParm is not rendered at all.
        Assert.Equal(baseline.ToString(), differentDbParm.ToString());
        Assert.Equal(baseline.ToString(), differentUserParm.ToString());
    }

    /// <summary>
    /// The guaranteed half of the hashing claim: EQUAL descriptors hash EQUALLY, and hashing a
    /// password-bearing descriptor and the cleared descriptor both complete without throwing.
    /// </summary>
    /// <remarks>
    /// The type says the hash codes of two descriptors differing only in the password are "permitted
    /// to differ", and permitted is the strongest thing that can honestly be asserted: hash inequality
    /// is not a contract for any type, and asserting it would be a probabilistic test dressed as a
    /// deterministic one. What IS a contract - equal values hash equally - is asserted here, and the
    /// load-bearing half of the claim, that the password participates in equality at all, is asserted
    /// by the test above.
    /// </remarks>
    [Fact]
    public void EqualDescriptorsHashEqually_AndHashingNeverThrows()
    {
        TransactionData first = FullyPopulated();
        TransactionData copy = first with { };

        Assert.Equal(first, copy);
        Assert.Equal(first.GetHashCode(), copy.GetHashCode());
        Assert.Equal(TransactionData.Empty.GetHashCode(), default(TransactionData).GetHashCode());
    }

    /// <summary>
    /// String comparison in the generated equality is ORDINAL and therefore case-sensitive, matching
    /// PowerBuilder's <c>=</c> on strings. A case-insensitive descriptor comparison would merge
    /// connection identities the legacy keeps apart.
    /// </summary>
    [Fact]
    public void EqualityIsOrdinalAndThereforeCaseSensitive()
    {
        TransactionData lower = new() { Database = "synthetic_catalogue" };
        TransactionData upper = new() { Database = "SYNTHETIC_CATALOGUE" };

        Assert.NotEqual(lower, upper);
    }

    /// <summary>
    /// A FAILED EQUALITY COMPARISON DISCLOSES NOTHING EITHER. The two descriptors that differ only in
    /// the password are compared with a deliberately failing assertion, and the resulting report -
    /// the closest thing to a "printed diff" this type can appear in - carries neither the value nor
    /// the member's name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THIS CLOSES THE LAST RENDERING ROUTE, AND IT IS A REAL ONE RATHER THAN A THEORETICAL ONE. The
    /// preceding tests prove that <see cref="TransactionData.ToString"/> is silent; this one proves
    /// that the DIFF a comparison failure produces goes THROUGH that method rather than around it.
    /// The distinction matters because a test framework's value formatter has two strategies: use the
    /// type's own <see cref="object.ToString"/> when it overrides one, or fall back to reflecting over
    /// the type's public properties and printing them. On the fallback strategy every readable
    /// credential-capable member would be printed into the failure report of any test that happened to
    /// compare two descriptors - which is a disclosure into CI logs, from an assertion whose author
    /// never mentioned the member at all.
    /// </para>
    /// <para>
    /// The type is protected against both strategies at once, which is why the assertion below can be
    /// unconditional: it overrides <see cref="TransactionData.ToString"/>, so the first strategy is
    /// redacted, and it removed the credential's GETTER, so the second strategy has nothing to read
    /// even if it were taken. Nothing in this file depends on knowing which strategy is in use.
    /// </para>
    /// <para>
    /// The report is inspected as a message AND as the whole rendered exception, so a formatter that
    /// attached the values somewhere other than the message would still be caught. The
    /// member-NAME check is applied to the message only: the full rendering includes a stack trace,
    /// and a stack trace legitimately contains the names of the methods in it.
    /// </para>
    /// </remarks>
    [Fact]
    public void AFailedComparisonReportDisclosesNeitherValueNorMemberName()
    {
        TransactionData first = FullyPopulated();
        TransactionData second = first with { LogPass = SyntheticPassword + "-VARIANT" };

        // The two ARE unequal, so this assertion really does fail and really does produce a report.
        Exception? report = Record.Exception(() => Assert.Equal(first, second));

        Assert.NotNull(report);

        // Not the value, not the variant of it, and not the member's name.
        Assert.DoesNotContain(SyntheticPassword, report.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(
            nameof(TransactionData.LogPass),
            report.Message,
            StringComparison.Ordinal);
        Assert.DoesNotContain(SyntheticPassword, report.ToString(), StringComparison.Ordinal);

        // The other two withheld members are covered on the same footing, since the formatter treats
        // all readable properties alike and these two ARE readable.
        Assert.DoesNotContain(SyntheticDbParm, report.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(SyntheticUserParm, report.Message, StringComparison.Ordinal);

        // POSITIVE CONTROL: the report is a real one about these descriptors, so the three absences
        // above are absences from something rather than from nothing.
        Assert.Contains(nameof(TransactionData), report.Message, StringComparison.Ordinal);
        Assert.Contains(EvidencedDbms, report.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// NO OPERATION ON THIS TYPE RAISES AN EXCEPTION AT ALL, so there is no exception message for a
    /// withheld member to escape through. Every public operation is driven over a descriptor holding
    /// all three needles, and each is asserted not to throw.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHY "IT NEVER THROWS" IS THE RIGHT SHAPE FOR THIS OBLIGATION. The write-only rule has to hold
    /// across every channel a value can leave by, and an exception message is one of them - a
    /// validation failure that helpfully quotes the offending input is exactly how credentials reach
    /// log aggregators. The type closes that channel structurally rather than by careful wording: it
    /// performs no argument validation, has no failure path, and therefore constructs no message that
    /// could quote anything. The accessors report through RETURN CODES, which is the oracle's own
    /// contract [n_cst_thread_trans.sru:L343-L419] and not a choice made for this purpose.
    /// </para>
    /// <para>
    /// THE HOSTILE INPUTS ARE INCLUDED DELIBERATELY, because "does not throw on the happy path" would
    /// be a weak claim. Nulls into every string member, a malformed parameter string, a descriptor
    /// used as its own transfer source, hashing the cleared value, and a hook that vetoes are all
    /// driven here, and none of them produces an exception either.
    /// </para>
    /// </remarks>
    [Fact]
    public void NoPublicOperationOnANeedleBearingDescriptorRaisesAnException()
    {
        TransactionData subject = FullyPopulated();

        List<(string Operation, Exception? Thrown)> outcomes = [];

        void Probe(string operation, Action body) => outcomes.Add((operation, Record.Exception(body)));

        // Construction, including every string member set to null, which the members accept.
        Probe("construct-with-nulls", () => _ = new TransactionData
        {
            Dbms = null,
            ServerName = null,
            Database = null,
            LogId = null,
            LogPass = null,
            DbParm = null,
            Lock = null,
            UserParm = null,
        });

        // Every readable member, plus the two named doors onto the credential.
        Probe(nameof(TransactionData.Dbms), () => _ = subject.Dbms);
        Probe(nameof(TransactionData.ServerName), () => _ = subject.ServerName);
        Probe(nameof(TransactionData.Database), () => _ = subject.Database);
        Probe(nameof(TransactionData.LogId), () => _ = subject.LogId);
        Probe(nameof(TransactionData.HasCredential), () => _ = subject.HasCredential);
        Probe("RevealLogPassForConnect", () => _ = subject.RevealLogPassForConnect());
        Probe(nameof(TransactionData.DbParm), () => _ = subject.DbParm);
        Probe(nameof(TransactionData.Lock), () => _ = subject.Lock);
        Probe(nameof(TransactionData.AutoCommit), () => _ = subject.AutoCommit);
        Probe(nameof(TransactionData.UserParm), () => _ = subject.UserParm);

        // The rendering, the hashing and both equality forms, on a needle-bearing value and on the
        // cleared one whose every backing field is null.
        Probe(nameof(TransactionData.ToString), () => _ = subject.ToString());
        Probe("Empty.ToString", () => _ = TransactionData.Empty.ToString());
        Probe(nameof(TransactionData.GetHashCode), () => _ = subject.GetHashCode());
        Probe("Empty.GetHashCode", () => _ = TransactionData.Empty.GetHashCode());
        Probe("Equals(TransactionData)", () => _ = subject.Equals(TransactionData.Empty));
        Probe("Equals(object)", () => _ = subject.Equals((object)TransactionData.Empty));
        Probe("Equals(null)", () => _ = subject.Equals(null));

        // The two derived flags over MALFORMED and over absent parameter text.
        Probe("flags-malformed", () =>
        {
            TransactionData malformed = subject with { DbParm = "DisableBind=,NCharBind" };
            _ = malformed.IsBindDisabled;
            _ = malformed.IsNCharBindingEnabled;
            malformed.ResolveDbParmFlags(out _, out _);
        });
        Probe("flags-cleared", () => TransactionData.Empty.ResolveDbParmFlags(out _, out _));

        // Both transfer folds, including the aliased case where a descriptor is its own source.
        Probe(nameof(TransactionData.WithConnectionFieldsFrom), () =>
            _ = TransactionData.Empty.WithConnectionFieldsFrom(subject));
        Probe(nameof(TransactionData.WithConnectionFieldsFromExcludingCredential), () =>
            _ = TransactionData.Empty.WithConnectionFieldsFromExcludingCredential(subject));
        Probe("fold-aliased", () => _ = subject.WithConnectionFieldsFrom(in subject));

        // Both accessors, with no hook and with a vetoing hook.
        Probe(nameof(TransactionData.SetTransactionData), () =>
        {
            TransactionData receiver = TransactionData.Empty;
            _ = TransactionData.SetTransactionData(ref receiver, subject);
        });
        Probe("SetTransactionData-vetoed", () =>
        {
            TransactionData receiver = TransactionData.Empty;
            _ = TransactionData.SetTransactionData(
                ref receiver,
                subject,
                (in TransactionData data) => RetCode.PREVENT);
        });
        Probe(nameof(TransactionData.GetTransactionData), () =>
        {
            TransactionData caller = TransactionData.Empty;
            string diagnostic = string.Empty;
            _ = subject.GetTransactionData(ref caller, ref diagnostic);
        });
        Probe("GetTransactionData-convenience", () => _ = subject.GetTransactionData());

        // NOT ONE of them throws, so no message exists that could quote a withheld member. Reported as
        // a named list so a future failure identifies WHICH operation started throwing.
        Assert.All(outcomes, outcome => Assert.Null(outcome.Thrown));

        // And the probe list really did exercise the surface rather than silently doing nothing.
        Assert.Equal(27, outcomes.Count);
    }

    // ==========================================================================================
    //  SUITE 4 - THE SEVEN-OF-NINE TRANSFER, AUDITED IN BOTH DIRECTIONS
    //  ----------------------------------------------------------------------------------------
    //  The oracle's inbound accessor [n_cst_thread_trans.sru:L345-L351] and outbound accessor
    //  [:L410-L416] move the SAME seven fields and touch NEITHER AutoCommit NOR UserParm. The port
    //  expresses both directions through one fold, so the audit below drives that fold directly and
    //  then drives each accessor, which is what proves the two directions cannot have drifted.
    // ==========================================================================================

    /// <summary>
    /// Asserts the seven connection fields came from <paramref name="source"/> and the two omitted
    /// members were kept from <paramref name="keeper"/> - the single audit both directions share.
    /// </summary>
    /// <param name="result">The descriptor produced by the transfer.</param>
    /// <param name="source">The descriptor the seven were supposed to come from.</param>
    /// <param name="keeper">The descriptor the two were supposed to be kept from.</param>
    private static void AssertSevenMovedAndTwoKept(
        in TransactionData result,
        in TransactionData source,
        in TransactionData keeper)
    {
        // The seven, in the oracle's assignment order [n_cst_thread_trans.sru:L345-L351].
        Assert.Equal(source.Dbms, result.Dbms);
        Assert.Equal(source.ServerName, result.ServerName);
        Assert.Equal(source.Database, result.Database);
        Assert.Equal(source.LogId, result.LogId);
        Assert.Equal(source.RevealLogPassForConnect(), result.RevealLogPassForConnect());
        Assert.Equal(source.DbParm, result.DbParm);
        Assert.Equal(source.Lock, result.Lock);

        // The two the legacy touches in NEITHER direction.
        Assert.Equal(keeper.AutoCommit, result.AutoCommit);
        Assert.Equal(keeper.UserParm, result.UserParm);
    }

    /// <summary>
    /// Asserts the SIX non-credential connection fields came from <paramref name="source"/> and that
    /// THREE members were kept from <paramref name="keeper"/> - the two the legacy never moves, plus the
    /// credential, which this port deliberately does not move outbound.
    /// </summary>
    /// <param name="result">The descriptor produced by the transfer.</param>
    /// <param name="source">The descriptor the six were supposed to come from.</param>
    /// <param name="keeper">The descriptor the three were supposed to be kept from.</param>
    /// <remarks>
    /// SEPARATE FROM THE INBOUND AUDIT, AND THE SEPARATION IS THE POINT. One audit serving both directions
    /// is the tempting economy, because the legacy's two accessors move the same seven fields - and
    /// the legacy really does move the password outbound [n_cst_thread_trans.sru:L414]. AAP 0.4.2.6 makes
    /// LogPass write-only: never echoed in a response, and an outbound accessor that returns it to its
    /// caller IS that echo. So the two directions are NOT symmetric here, and one helper for both
    /// would make the asymmetry invisible.
    /// </remarks>
    private static void AssertSixMovedAndThreeKept(
        in TransactionData result,
        in TransactionData source,
        in TransactionData keeper)
    {
        // The six, in the oracle's assignment order LESS the credential [:L410-L416].
        Assert.Equal(source.Dbms, result.Dbms);
        Assert.Equal(source.ServerName, result.ServerName);
        Assert.Equal(source.Database, result.Database);
        Assert.Equal(source.LogId, result.LogId);
        Assert.Equal(source.DbParm, result.DbParm);
        Assert.Equal(source.Lock, result.Lock);

        // The two the legacy touches in NEITHER direction.
        Assert.Equal(keeper.AutoCommit, result.AutoCommit);
        Assert.Equal(keeper.UserParm, result.UserParm);

        // AND THE THIRD: the caller keeps whatever credential it already held. Stated as an equality
        // against the keeper rather than against empty, because the outbound path must be a strict
        // NON-EVENT for this member - it neither discloses the connection's password nor destroys the
        // caller's own, and clearing would be its own behaviour change.
        Assert.Equal(keeper.RevealLogPassForConnect(), result.RevealLogPassForConnect());
        Assert.Equal(keeper.HasCredential, result.HasCredential);
    }

    /// <summary>
    /// The outbound path never hands the connection's own password back, whatever the caller held.
    /// </summary>
    /// <remarks>
    /// THE FINDING, ASSERTED DIRECTLY AND FROM BOTH STARTING STATES. A caller that held NO credential must
    /// not acquire one, which is the disclosure; a caller that held ITS OWN must keep exactly that, which
    /// is the non-destruction. Driven through the real accessor rather than the fold, so it also proves the
    /// accessor reaches for the right one of the two folds.
    /// </remarks>
    [Fact]
    public void GetTransactionData_NeverEchoesTheConnectionsPassword()
    {
        TransactionData connection = FullyPopulated();

        // 1. A caller holding nothing acquires nothing.
        TransactionData empty = ReceiverWithOnlyTheTwoSet();
        string diagnostic = string.Empty;

        Assert.Equal(RetCode.OK, connection.GetTransactionData(ref empty, ref diagnostic));
        Assert.Equal(string.Empty, empty.RevealLogPassForConnect());
        Assert.False(empty.HasCredential);

        // But the rest of the descriptor DID arrive, so the assertion above is not vacuous.
        Assert.Equal(connection.Dbms, empty.Dbms);
        Assert.Equal(connection.DbParm, empty.DbParm);

        // 2. A caller holding its own keeps exactly that, rather than being overwritten or cleared.
        // Needle-shaped like every other synthetic value in this file, so a search of the repository
        // finds it here and only here, and so it could not be mistaken for a credential (C-F).
        const string callersOwn = "NEEDLE-callers-own-logpass-M7X2-synthetic-not-a-credential";
        TransactionData own = ReceiverWithOnlyTheTwoSet() with { LogPass = callersOwn };

        Assert.Equal(RetCode.OK, connection.GetTransactionData(ref own, ref diagnostic));
        Assert.Equal(callersOwn, own.RevealLogPassForConnect());
        Assert.True(own.HasCredential);

        // 3. And the convenience overload, which fills a freshly cleared descriptor, cannot leak either.
        Assert.False(connection.GetTransactionData().HasCredential);
    }

    /// <summary>
    /// The two folds are named for their directions, and the credential moves in exactly one of them.
    /// </summary>
    [Fact]
    public void TheTwoFoldsAreDirectionalAndTheCredentialMovesOnlyInbound()
    {
        TransactionData source = FullyPopulated();
        TransactionData receiver = ReceiverWithOnlyTheTwoSet();

        TransactionData inbound = receiver.WithConnectionFieldsFrom(source);
        TransactionData outbound = receiver.WithConnectionFieldsFromExcludingCredential(source);

        Assert.Equal(source.RevealLogPassForConnect(), inbound.RevealLogPassForConnect());
        Assert.Equal(string.Empty, outbound.RevealLogPassForConnect());

        // AND THE CREDENTIAL IS THE ONLY DIFFERENCE between the two folds: clear it on the inbound
        // result and the two become equal. That is what makes this a single scoped omission rather than
        // a second, quietly different transfer.
        Assert.Equal(inbound with { LogPass = null }, outbound);
        Assert.NotEqual(inbound, outbound);
    }

    /// <summary>
    /// The excluding fold tolerates the source and the instance sharing storage, like its sibling.
    /// </summary>
    [Fact]
    public void WithConnectionFieldsFromExcludingCredential_IsSafeWhenTheSourceAndTheInstanceShareStorage()
    {
        TransactionData subject = FullyPopulated();

        Assert.Equal(subject, subject.WithConnectionFieldsFromExcludingCredential(in subject));
    }

    /// <summary>
    /// The fold itself: seven from the source, two from the instance it was called on.
    /// </summary>
    [Fact]
    public void WithConnectionFieldsFrom_MovesExactlySevenAndKeepsExactlyTwo()
    {
        TransactionData source = FullyPopulated();
        TransactionData receiver = ReceiverWithOnlyTheTwoSet();

        TransactionData result = receiver.WithConnectionFieldsFrom(source);

        AssertSevenMovedAndTwoKept(result, source, receiver);

        // Stated positively as well, because the two values were chosen to differ from the source's:
        // a transfer that moved all nine would have produced the source's own values here.
        Assert.NotEqual(source.AutoCommit, result.AutoCommit);
        Assert.NotEqual(source.UserParm, result.UserParm);
    }

    /// <summary>
    /// Aliasing is safe: a descriptor may be its own source, because the
    /// <see langword="with"/> expression reads every member it needs before anything is assigned.
    /// </summary>
    [Fact]
    public void WithConnectionFieldsFrom_IsSafeWhenTheSourceAndTheInstanceShareStorage()
    {
        TransactionData subject = FullyPopulated();

        Assert.Equal(subject, subject.WithConnectionFieldsFrom(in subject));
    }

    /// <summary>
    /// THE COUNT AUDIT. Exactly SEVEN of the nine move inbound and exactly SIX move outbound - counted
    /// over the nine rather than checked member by member, so an ADDED or REMOVED member changes the
    /// count and fails here.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHY A COUNT AS WELL AS THE MEMBER-BY-MEMBER AUDITS. The per-member helpers state which member
    /// should hold which value; they cannot state HOW MANY moved, so a fold that grew a tenth
    /// assignment would satisfy every one of them. The count is the assertion that catches the shape
    /// of the change rather than its content, and the two together are what make "seven of nine" and
    /// "six of nine" checkable claims instead of descriptions.
    /// </para>
    /// <para>
    /// THE RECEIVER HAS ALL NINE MEMBERS SET, WHICH IS STRICTLY STRONGER THAN THE CLEARED RECEIVER the
    /// neighbouring tests use. Against a cleared receiver, "moved" and "cleared then overwritten" look
    /// identical for a string member, and a fold that ERASED a member instead of copying it would
    /// still pass. Against a receiver whose nine all differ from the source's nine, every one of the
    /// nine is independently discriminating in both directions - which the first assertion below
    /// proves before relying on it.
    /// </para>
    /// <para>
    /// The expected member lists are derived from the shared nine-member declaration rather than typed
    /// out again, so this audit cannot disagree with the census about what the nine are.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheInboundFoldMovesExactlySevenOfTheNineAndTheOutboundExactlySix()
    {
        TransactionData source = FullyPopulated();
        TransactionData receiver = ReceiverWithAllNineDistinct();

        // PRECONDITION, asserted rather than assumed: not one of the nine already agrees, so every
        // "moved" verdict below is a real observation.
        Assert.Empty(MembersTakenFromSource(receiver, source));

        // The nine the projection covers really are the nine the census declares.
        Assert.Equal(
            [.. TheNineInOracleOrder().Select(member => member.Name)],
            [.. ProjectTheNine(source).Keys]);

        // INBOUND: the seven connection fields, in the oracle's own assignment order
        // [n_cst_thread_trans.sru:L345-L351].
        string[] expectedInbound =
        [
            nameof(TransactionData.Dbms),
            nameof(TransactionData.ServerName),
            nameof(TransactionData.Database),
            nameof(TransactionData.LogId),
            nameof(TransactionData.LogPass),
            nameof(TransactionData.DbParm),
            nameof(TransactionData.Lock),
        ];

        string[] inboundMoved =
            MembersTakenFromSource(receiver.WithConnectionFieldsFrom(source), source);

        Assert.Equal(7, inboundMoved.Length);
        Assert.Equal(expectedInbound, inboundMoved);

        // OUTBOUND: the same seven LESS the credential, which this port deliberately does not echo
        // [the oracle's own `data.LogPass = LogPass` at :L414 is the one line not reproduced].
        string[] expectedOutbound = [.. expectedInbound
            .Where(name => name != nameof(TransactionData.LogPass))];

        string[] outboundMoved = MembersTakenFromSource(
            receiver.WithConnectionFieldsFromExcludingCredential(source),
            source);

        Assert.Equal(6, outboundMoved.Length);
        Assert.Equal(expectedOutbound, outboundMoved);

        // AND THE COMPLEMENTS ARE THE MEMBERS THE RECEIVER KEPT: two inbound, three outbound.
        Assert.Equal(
            2,
            TheNineInOracleOrder().Length - inboundMoved.Length);
        Assert.Equal(
            3,
            TheNineInOracleOrder().Length - outboundMoved.Length);
    }

    /// <summary>
    /// THE EXCLUDED TWO ARE EXCLUDED IN BOTH DIRECTIONS, driven through the REAL ACCESSORS rather than
    /// the folds, and asserted as RETENTION of the prior value rather than merely as inequality with
    /// the source's.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHY RETENTION RATHER THAN INEQUALITY. "Not the source's value" is satisfied by a member that
    /// was cleared, and clearing is a different bug with the same symptom. Asserting the member still
    /// holds EXACTLY what it held before the call is the claim the oracle actually makes: it never
    /// writes these two, so whatever was there stays there
    /// [n_cst_thread_trans.sru:L345-L351 inbound, :L410-L416 outbound].
    /// </para>
    /// <para>
    /// WHY IT MATTERS THAT AUTOCOMMIT IN PARTICULAR DOES NOT TRAVEL. A descriptor is the transaction
    /// pool's reference-counting KEY [n_cst_thread_trans_pool.sru:L138], so the members that make it
    /// up define a connection IDENTITY. Autocommit is a SESSION control, not part of that identity -
    /// the legacy says so itself by erasing it from a stored descriptor immediately, under a comment
    /// that reads "erase parameters irrelevant to the connection target"
    /// [n_cst_thread_task_sqlbase.sru:L118-L119]. A transfer that carried it would therefore change
    /// per-statement commit behaviour on POOL REUSE: a caller taking a reference to an existing
    /// connection would silently inherit whichever commit policy the previous holder had set. The user
    /// parameter is excluded on exactly the same footing.
    /// </para>
    /// <para>
    /// The two directions are asserted separately and both are asserted, because an asymmetric port -
    /// carrying them one way and not the other - would be the most plausible way to get this wrong.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheTwoExcludedMembersAreRetainedInBothDirections()
    {
        // Distinguishable in both directions: the connection and the caller disagree on both members.
        TransactionData connection = FullyPopulated();
        TransactionData party = ReceiverWithAllNineDistinct();

        Assert.NotEqual(connection.AutoCommit, party.AutoCommit);
        Assert.NotEqual(connection.UserParm, party.UserParm);

        // APPLY-ONTO. of_settransdata writes the seven onto the receiver in place and never touches
        // these two, so the receiver's own values survive the call unchanged.
        TransactionData receiver = party;

        Assert.Equal(RetCode.OK, TransactionData.SetTransactionData(ref receiver, connection));
        Assert.Equal(party.AutoCommit, receiver.AutoCommit);
        Assert.Equal(party.UserParm, receiver.UserParm);

        // ...and the seven really did arrive, so the retention above is not the result of a transfer
        // that did nothing at all.
        Assert.Equal(connection.Dbms, receiver.Dbms);
        Assert.Equal(connection.RevealLogPassForConnect(), receiver.RevealLogPassForConnect());

        // READ-FROM. of_gettransdata fills the caller's descriptor from the connection and likewise
        // never touches these two.
        TransactionData caller = party;
        string diagnostic = string.Empty;

        Assert.Equal(RetCode.OK, connection.GetTransactionData(ref caller, ref diagnostic));
        Assert.Equal(party.AutoCommit, caller.AutoCommit);
        Assert.Equal(party.UserParm, caller.UserParm);

        // ...with the six arriving, and the caller's OWN credential retained rather than replaced -
        // the third exclusion, which belongs to this direction only.
        Assert.Equal(connection.Dbms, caller.Dbms);
        Assert.Equal(party.RevealLogPassForConnect(), caller.RevealLogPassForConnect());
    }

    /// <summary>
    /// INBOUND: <c>of_settransdata</c> moves the seven onto the receiver in place and leaves the
    /// receiver's own two alone [n_cst_thread_trans.sru:L345-L351].
    /// </summary>
    [Fact]
    public void SetTransactionData_Inbound_MovesTheSevenAndLeavesTheReceiversTwo()
    {
        TransactionData source = FullyPopulated();
        TransactionData receiver = ReceiverWithOnlyTheTwoSet();
        TransactionData receiverBefore = receiver;

        long code = TransactionData.SetTransactionData(ref receiver, source);

        Assert.Equal(RetCode.OK, code);
        AssertSevenMovedAndTwoKept(receiver, source, receiverBefore);
    }

    /// <summary>
    /// OUTBOUND: <c>of_gettransdata(ref, ref)</c> fills the caller's descriptor from the connection's
    /// own state, leaves the CALLER's two alone [n_cst_thread_trans.sru:L410-L416] AND leaves the caller's
    /// credential alone - the one line of the oracle's accessor this port deliberately does not
    /// reproduce [<c>:L414</c>, against AAP 0.4.2.6's write-only rule].
    /// </summary>
    [Fact]
    public void GetTransactionData_Outbound_MovesTheSixAndLeavesTheCallersThree()
    {
        TransactionData connection = FullyPopulated();
        TransactionData caller = ReceiverWithOnlyTheTwoSet();
        TransactionData callerBefore = caller;
        string diagnostic = string.Empty;

        long code = connection.GetTransactionData(ref caller, ref diagnostic);

        Assert.Equal(RetCode.OK, code);
        Assert.Equal(string.Empty, diagnostic);
        AssertSixMovedAndThreeKept(caller, connection, callerBefore);
    }

    /// <summary>
    /// The inbound accessor tolerates the receiver and the source sharing storage, on the same terms
    /// as the fold it delegates to.
    /// </summary>
    [Fact]
    public void SetTransactionData_IsSafeWhenTheReceiverAndTheSourceShareStorage()
    {
        TransactionData subject = FullyPopulated();
        TransactionData expected = subject;

        long code = TransactionData.SetTransactionData(ref subject, in subject);

        Assert.Equal(RetCode.OK, code);
        Assert.Equal(expected, subject);
    }

    /// <summary>
    /// There is NO way to move all nine (C-B). No <c>includeAll</c> parameter anywhere on the type,
    /// no <c>WithAllFieldsFrom</c> companion, and exactly one <c>With...</c> member taking exactly one
    /// parameter. A caller who genuinely wants all nine uses the plain value assignment, which reads
    /// differently at the call site - and that is the point.
    /// </summary>
    [Fact]
    public void NoMemberMovesAllNineFields()
    {
        MethodInfo[] withMethods = [.. typeof(TransactionData)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.Static)
            .Where(method => method.Name.StartsWith("With", StringComparison.Ordinal))];

        // TWO FOLDS NOW, ONE PER DIRECTION, and neither moves all nine. The count is pinned so a third
        // fold - or a resurrected all-nine one - cannot land unnoticed.
        Assert.Equal(2, withMethods.Length);
        Assert.Contains(nameof(TransactionData.WithConnectionFieldsFrom), withMethods.Select(m => m.Name));
        Assert.Contains(
            nameof(TransactionData.WithConnectionFieldsFromExcludingCredential),
            withMethods.Select(m => m.Name));
        Assert.All(withMethods, method => Assert.Single(method.GetParameters()));

        bool anyIncludeAllParameter = typeof(TransactionData)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.Static)
            .SelectMany(method => method.GetParameters())
            .Any(parameter => parameter.Name is not null
                && parameter.Name.Contains("includeAll", StringComparison.OrdinalIgnoreCase));

        Assert.False(anyIncludeAllParameter);
    }

    // ==========================================================================================
    //  SUITE 5 - THE FOUR-CELL VETO-AND-DIAGNOSTIC MATRIX
    //  ----------------------------------------------------------------------------------------
    //  THE ORACLE, VERBATIM [n_cst_thread_trans.sru:L402-L419]:
    //
    //      errInfo = ""                                              [:L402]
    //      if Event OnGetTransData(ref data,ref errInfo) = 1 then    [:L404]
    //          if errInfo <> "" then return RetCode.FAILED           [:L405]  <- FIRST inspection
    //          return RetCode.OK                                     [:L406]
    //      end if
    //      if errInfo <> "" then return RetCode.FAILED               [:L408]  <- SECOND inspection
    //      ... the seven-field copy ...                              [:L410-L416]
    //      return RetCode.OK                                         [:L418]
    //
    //  The diagnostic is inspected TWICE, and collapsing the two into one changes the outcome for a
    //  hook that vetoes AND reports. Every row of the theory below exists to pin one cell of that
    //  matrix; the rows with a hook result of 2 are the ones that prove the veto test is a LITERAL
    //  equality against 1 rather than the tri-valued prevention predicate, which the very next
    //  function in the same legacy object DOES use [:L429].
    // ==========================================================================================

    /// <summary>
    /// The whole outcome matrix, one row per cell. The result depends on the hook's return value and
    /// on the diagnostic TOGETHER - no single condition determines it.
    /// </summary>
    /// <param name="hookResult">What the hook returns.</param>
    /// <param name="diagnosticWritten">
    /// What the hook writes into the diagnostic slot. <see langword="null"/> means the hook writes
    /// <see langword="null"/>, which the oracle treats as NOT-non-empty; the empty string means it
    /// writes nothing distinguishable from the cleared slot.
    /// </param>
    /// <param name="expectedFailure">
    /// <see langword="true"/> when the call is expected to report <see cref="RetCode.FAILED"/>.
    /// </param>
    /// <param name="expectedCopy">
    /// <see langword="true"/> when the seven-field copy is expected to have happened.
    /// </param>
    [Theory]
    // hook = 1, diagnostic empty -> a CLEAN VETO: success reported, copy SKIPPED [:L406].
    [InlineData(1L, "", false, false)]
    // hook = 1, diagnostic non-empty -> FAILED, copy skipped. Requires the FIRST inspection [:L405].
    [InlineData(1L, "NEEDLE-diagnostic-veto-and-report", true, false)]
    // hook = 0, diagnostic non-empty -> FAILED, copy skipped. Requires the SECOND one [:L408].
    [InlineData(0L, "NEEDLE-diagnostic-report-without-veto", true, false)]
    // hook = 0, diagnostic empty -> the only cell that writes anything [:L410-L418].
    [InlineData(0L, "", false, true)]
    // hook = 2, diagnostic empty -> 2 IS NOT A VETO. A deep prevention satisfies the kernel's
    // prevention predicate and does NOT satisfy this literal equality, so the copy PROCEEDS.
    [InlineData(2L, "", false, true)]
    // hook = 2, diagnostic non-empty -> FAILED through the SECOND inspection, not the first.
    [InlineData(2L, "NEEDLE-diagnostic-deep-prevention", true, false)]
    // hook = -1 (a failure code) -> still not a veto, for the same reason 2 is not.
    [InlineData(-1L, "", false, true)]
    // A NULL diagnostic is "empty OR unset", because PowerBuilder evaluates a null condition as
    // false - so a nullable-oblivious hook cannot turn a null into a spurious failure.
    [InlineData(1L, null, false, false)]
    [InlineData(0L, null, false, true)]
    public void GetTransactionData_TheFourCellMatrix(
        long hookResult,
        string? diagnosticWritten,
        bool expectedFailure,
        bool expectedCopy)
    {
        TransactionData connection = FullyPopulated();
        TransactionData caller = ReceiverWithOnlyTheTwoSet();
        TransactionData callerBefore = caller;
        string diagnostic = string.Empty;

        long code = connection.GetTransactionData(
            ref caller,
            ref diagnostic,
            (ref TransactionData data, ref string errInfo) =>
            {
                errInfo = diagnosticWritten!;
                return hookResult;
            });

        Assert.Equal(expectedFailure ? RetCode.FAILED : RetCode.OK, code);

        if (expectedCopy)
        {
            AssertSixMovedAndThreeKept(caller, connection, callerBefore);
        }
        else
        {
            Assert.Equal(callerBefore, caller);
        }
    }

    /// <summary>
    /// The diagnostic slot is CLEARED before the hook runs [:L402], so a stale value left in it by an
    /// earlier call can never be mistaken for this call's report.
    /// </summary>
    [Fact]
    public void GetTransactionData_ClearsTheDiagnosticBeforeTheHookRuns()
    {
        TransactionData connection = FullyPopulated();
        TransactionData caller = default;
        string diagnostic = "NEEDLE-stale-diagnostic-from-an-earlier-call";
        string observedInsideTheHook = "not observed";

        long code = connection.GetTransactionData(
            ref caller,
            ref diagnostic,
            (ref TransactionData data, ref string errInfo) =>
            {
                observedInsideTheHook = errInfo;
                return 0L;
            });

        Assert.Equal(string.Empty, observedInsideTheHook);
        Assert.Equal(RetCode.OK, code);
        Assert.Equal(string.Empty, diagnostic);
    }

    /// <summary>
    /// On the VETOED path the framework's own copy is skipped, but whatever the hook itself wrote into
    /// the caller's descriptor SURVIVES - a hook may populate the descriptor and then veto to say "I
    /// have already done this".
    /// </summary>
    [Fact]
    public void GetTransactionData_VetoedPath_KeepsWhateverTheHookItselfWrote()
    {
        TransactionData connection = FullyPopulated();
        TransactionData caller = default;
        string diagnostic = string.Empty;

        TransactionData hookSupplied = new()
        {
            // The OTHER evidenced dialect, so the value visibly differs from the connection's own
            // without inventing a third one (C-E).
            Dbms = EvidencedOtherDbms,
            UserParm = "NEEDLE-hook-userparm-F3H8-synthetic",
        };

        long code = connection.GetTransactionData(
            ref caller,
            ref diagnostic,
            (ref TransactionData data, ref string errInfo) =>
            {
                data = hookSupplied;
                return 1L;
            });

        Assert.Equal(RetCode.OK, code);
        Assert.Equal(hookSupplied, caller);

        // And specifically NOT the connection's own seven - the copy really was skipped.
        Assert.NotEqual(connection.Dbms, caller.Dbms);
        Assert.Equal(string.Empty, caller.RevealLogPassForConnect());
    }

    /// <summary>
    /// The inbound accessor's veto: the hook returning exactly <c>1</c> skips the assignment ENTIRELY
    /// - not the seven, not the two - and STILL reports success [:L343]. A caller cannot distinguish a
    /// vetoed call from a completed one by its return value alone, which is precisely the legacy
    /// contract.
    /// </summary>
    [Fact]
    public void SetTransactionData_HookReturningOne_WritesNothingAndStillReportsSuccess()
    {
        TransactionData source = FullyPopulated();
        TransactionData receiver = ReceiverWithOnlyTheTwoSet();
        TransactionData receiverBefore = receiver;

        long code = TransactionData.SetTransactionData(
            ref receiver,
            source,
            (in TransactionData data) => 1L);

        Assert.Equal(RetCode.OK, code);
        Assert.Equal(receiverBefore, receiver);
    }

    /// <summary>
    /// Every OTHER hook result lets the copy proceed, and the result is success in every case - the
    /// inbound accessor has no failure path at all. The row for <c>2</c> is the one that proves the
    /// veto test is a literal equality rather than <c>IsPrevented</c>, and the row for
    /// <see cref="RetCode.PREVENT"/> is the same value spelled through the kernel constant, which
    /// makes the relationship explicit.
    /// </summary>
    /// <param name="hookResult">What the hook returns.</param>
    /// <param name="expectedVeto">Whether that result is expected to veto.</param>
    [Theory]
    [InlineData(0L, false)]
    [InlineData(2L, false)]
    [InlineData(-1L, false)]
    [InlineData(3L, false)]
    [InlineData(RetCode.PREVENT, true)]
    public void SetTransactionData_OnlyTheLiteralOneVetoes(long hookResult, bool expectedVeto)
    {
        TransactionData source = FullyPopulated();
        TransactionData receiver = ReceiverWithOnlyTheTwoSet();
        TransactionData receiverBefore = receiver;

        long code = TransactionData.SetTransactionData(
            ref receiver,
            source,
            (in TransactionData data) => hookResult);

        // RetCode.OK in EVERY case, vetoed or not [:L343 and :L353].
        Assert.Equal(RetCode.OK, code);

        if (expectedVeto)
        {
            Assert.Equal(receiverBefore, receiver);
        }
        else
        {
            AssertSevenMovedAndTwoKept(receiver, source, receiverBefore);
        }
    }

    /// <summary>
    /// A <see langword="null"/> hook stands in for an unimplemented PowerBuilder event, which returns
    /// <c>0</c> and therefore does not veto - on both accessors.
    /// </summary>
    [Fact]
    public void ANullHookBehavesAsAnUnimplementedEventOnBothAccessors()
    {
        TransactionData source = FullyPopulated();
        TransactionData receiver = ReceiverWithOnlyTheTwoSet();
        TransactionData receiverBefore = receiver;

        Assert.Equal(RetCode.OK, TransactionData.SetTransactionData(ref receiver, source, null));
        AssertSevenMovedAndTwoKept(receiver, source, receiverBefore);

        TransactionData caller = ReceiverWithOnlyTheTwoSet();
        TransactionData callerBefore = caller;
        string diagnostic = string.Empty;

        Assert.Equal(RetCode.OK, source.GetTransactionData(ref caller, ref diagnostic, null));
        AssertSixMovedAndThreeKept(caller, source, callerBefore);
    }

    /// <summary>
    /// The hook receives the descriptor the caller supplied, so it can inspect what is about to be
    /// stored before deciding whether to allow it.
    /// </summary>
    [Fact]
    public void SetTransactionData_HandsTheSuppliedDescriptorToTheHook()
    {
        TransactionData source = FullyPopulated();
        TransactionData receiver = default;
        TransactionData observed = default;

        _ = TransactionData.SetTransactionData(
            ref receiver,
            source,
            (in TransactionData data) =>
            {
                observed = data;
                return 0L;
            });

        Assert.Equal(source, observed);
    }

    /// <summary>
    /// THE PRESERVED LEGACY QUIRK (C-B): the parameterless outbound overload DISCARDS the return code
    /// [:L394-L400, the discarded call at :L397], so a failure is INVISIBLE through it. The caller
    /// receives a descriptor with nothing to distinguish success from failure - here, the cleared
    /// value, because the reporting hook wrote nothing into it.
    /// </summary>
    [Fact]
    public void GetTransactionData_ConvenienceOverload_MakesAFailureInvisible()
    {
        TransactionData connection = FullyPopulated();

        TransactionData returned = connection.GetTransactionData(
            (ref TransactionData data, ref string errInfo) =>
            {
                errInfo = "NEEDLE-diagnostic-that-nobody-will-ever-see";
                return 0L;
            });

        // The two-argument form reports the failure...
        TransactionData viaTheTwoArgumentForm = default;
        string diagnostic = string.Empty;
        long code = connection.GetTransactionData(
            ref viaTheTwoArgumentForm,
            ref diagnostic,
            (ref TransactionData data, ref string errInfo) =>
            {
                errInfo = "NEEDLE-diagnostic-that-nobody-will-ever-see";
                return 0L;
            });
        Assert.Equal(RetCode.FAILED, code);

        // ...and the convenience overload does not: it hands back the cleared descriptor, which is
        // indistinguishable from a connection whose fields happen to be empty.
        Assert.Equal(TransactionData.Empty, returned);
    }

    /// <summary>
    /// On the normal path the convenience overload returns the six non-credential connection fields with
    /// the two omitted members CLEARED, because the descriptor it fills starts life cleared [:L395] - and
    /// with the credential cleared too, because it starts cleared and nothing fills it.
    /// </summary>
    [Fact]
    public void GetTransactionData_ConvenienceOverload_ReturnsTheSixWithTheOthersCleared()
    {
        TransactionData connection = FullyPopulated();

        TransactionData returned = connection.GetTransactionData();

        AssertSixMovedAndThreeKept(returned, connection, TransactionData.Empty);
        Assert.False(returned.AutoCommit);
        Assert.Equal(string.Empty, returned.UserParm);

        // The connection HAS both set, so the two really were left at their cleared values rather
        // than copied - which is the seven-of-nine asymmetry seen from the convenience overload.
        Assert.True(connection.AutoCommit);
        Assert.Equal(SyntheticUserParm, connection.UserParm);
    }

    // ==========================================================================================
    //  SUITE 6 - THE DBParm FLAG MATRIX, INCLUDING THE NESTING CASE
    //  ----------------------------------------------------------------------------------------
    //  THE ORACLE, VERBATIM [n_cst_thread_task_sqlbase.sru:L127-L132]:
    //
    //      _bNCharBinding = false                                                        [:L127]
    //      if RegExpFind(_transData.DBParm,"DisableBind\s*=\s*(0|1)",2,true) = "1" then   [:L128]
    //          if RegExpFind(_transData.DBParm,"NCharBind\s*=\s*(0|1)",2,true) = "1" then [:L129]
    //              _bNCharBinding = true                                                 [:L130]
    //
    //  THE NESTING IS THE CONTRACT: NCharBind is consulted ONLY INSIDE the DisableBind=1 branch, so
    //  NCharBind=1 on its own is LEGAL AND ENTIRELY INERT. The rows below pin that, and they also pin
    //  three properties that look like defects and are not: the patterns are DELIBERATELY UNANCHORED,
    //  so a longer keyword ending in the same text also matches; the comparison is TEXTUAL against
    //  "1", so "=2" matches nothing at all while "=10" captures a "1" and is true; and matching is
    //  case-insensitive. Changing any of them would change which DBParm strings are recognised, which
    //  is exactly the silent behavioural change C-B forbids.
    //
    //  ============== C-B: THE NESTING IS PRESERVED, NOT CORRECTED. DO NOT FLATTEN IT. ==============
    //  TWO INDEPENDENT FLAGS WOULD BE THE MORE SENSIBLE DESIGN, AND THAT IS EXACTLY WHY THE
    //  TEMPTATION EXISTS. National-character binding reads like a property of the connection in its
    //  own right, and a flat `disableBind && ncharBind` computes the same answer for three of the four
    //  cells - which is what makes the fourth so easy to lose. The oracle nests the second test inside
    //  the first at [n_cst_thread_task_sqlbase.sru:L128-L132] and therefore NEVER CONSULTS the second
    //  key outside the first key's branch. Honouring it independently would change generated
    //  statements for every caller who set the second flag without the first. It is preserved legacy
    //  behaviour with its locator, and the `DisableBind=0,NCharBind=1` row below is the single row a
    //  naive independent-flags port gets wrong.
    //  ==========================================================================================
    //
    //  ====== C-K: WHY THIS PARSE IS WORTH A TEST AT ALL - THE SECURITY CONSEQUENCE, RECORDED ======
    //  `DisableBind=1` MEANS THE RUNTIME DOES NOT USE BIND VARIABLES. Values are INTERPOLATED INTO
    //  THE STATEMENT TEXT AS LITERALS instead of being bound, and that is THE MECHANICAL ROOT OF THE
    //  SQL-INJECTION EXPOSURE the migration plan analyses (AAP 0.2.1.4, which establishes that the
    //  legacy `regexpfind` binding is satisfied by the BCL and is not a library dependency to port,
    //  and AAP 0.6.4, which traces the exposure from this flag to the raw clause spliced in by the
    //  C-05 clause setters and to the unescaped filter interpolation in the drop-down search service).
    //  With binding disabled there is no bind boundary for a value to stay behind, so the generated
    //  statement carries live row data.
    //
    //  THE .NET IMPLEMENTATION PARAMETERIZES INTERNALLY WHILE PRESERVING THE OBSERVABLE GENERATED
    //  STATEMENT UNCHANGED. That combination is deliberate and is what AAP 0.1.5 permits: the safer
    //  mechanism is UNOBSERVABLE, so adopting it is allowed, while the statement text a caller can see
    //  must still match the oracle byte for byte. The legacy defect is DOCUMENTED, NOT CORRECTED (C-B).
    //
    //  WHAT THIS SUITE IS RESPONSIBLE FOR, AND WHAT IT IS NOT. It pins THE FLAG PARSE - the one signal
    //  that tells the layer above whether a published statement can contain data at all. The
    //  REDACTION obligation that follows from the flag is asserted by the sibling suite for
    //  Errors/SqlRedactor.cs, not here, because that is where the masking lives; splitting them this
    //  way keeps each suite's subject to one file. Neither obligation is optional and neither covers
    //  the other.
    //  ==========================================================================================
    // ==========================================================================================

    /// <summary>
    /// THE FLAG MATRIX, as member data. Every row is a connection parameter string paired with the two
    /// flag values the oracle's nested parse yields for it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// EXPRESSED AS MEMBER DATA RATHER THAN AS A LOOP INSIDE A SINGLE TEST, DELIBERATELY. A loop
    /// reports one result for the whole matrix, stops at the first failure and names no case, so a
    /// regression in one cell tells you only that "the matrix" broke. As member data each row is a
    /// separately named, separately reported case, every row still runs when one fails, and the
    /// failure message identifies the exact parameter string. That matters most for the one row a
    /// naive independent-flags port gets wrong, which would otherwise be indistinguishable from any
    /// other cell going red.
    /// </para>
    /// <para>
    /// <see cref="TheoryData{T1, T2, T3}"/> rather than
    /// <c>IEnumerable&lt;object[]&gt;</c> so the row shape is checked at COMPILE time: a row with the
    /// wrong arity or a transposed pair of booleans is a build error here, not a confusing runtime
    /// failure. The rows are grouped and commented by the property each group pins.
    /// </para>
    /// </remarks>
    public static TheoryData<string, bool, bool> DbParmFlagMatrix() => new()
    {
        // ---- ABSENT KEYS. Nothing to find, so both flags are false. -------------------------------
        { string.Empty, false, false },
        { "Probe=NEEDLE-no-flags-here", false, false },

        // ---- THE FOUR REQUIRED COMBINATIONS [:L128-L132] ----------------------------------------
        // 1. BOTH SET - the ONLY combination that enables national-character binding.
        { "DisableBind=1,NCharBind=1", true, true },

        // 2. OUTER SET, INNER ZERO - the inner key is reached and refused.
        { "DisableBind=1,NCharBind=0", true, false },

        // 3. OUTER ZERO, INNER SET - FALSE. ***THE ROW A NAIVE INDEPENDENT-FLAGS PORT GETS WRONG.***
        //    The oracle never reaches the inner test at all, so the inner key is INERT rather than
        //    merely overridden. Preserved, not corrected (C-B).
        { "DisableBind=0,NCharBind=1", false, false },

        // 4. NEITHER PRESENT - covered by the absent-keys group above, and again here explicitly so
        //    the four required combinations read as four rows in one place.
        { "CommitOnDisconnect=0", false, false },

        // ---- THE OUTER KEY ALONE, BOTH VALUES ----------------------------------------------------
        { "DisableBind=1", true, false },
        { "DisableBind=0", false, false },

        // ---- THE NESTING CASE ISOLATED: the inner key ALONE is legal and entirely inert. ----------
        { "NCharBind=1", false, false },
        { "NCharBind=0", false, false },

        // ---- ORDER IS IRRELEVANT: the parse searches, it does not read positionally. --------------
        { "NCharBind=1,DisableBind=1", true, true },

        // ---- WHITESPACE, from the oracle's own \s* on BOTH sides of the '='. ----------------------
        { "DisableBind = 1 , NCharBind = 1", true, true },
        { "DisableBind\t=\t1,NCharBind\t=\t1", true, true },
        { "DisableBind   =1,NCharBind=   1", true, true },

        // ---- CASE INSENSITIVITY, which is the specified port behaviour. ---------------------------
        { "disablebind=1,ncharbind=1", true, true },
        { "DISABLEBIND=1", true, false },
        { "DISABLEBIND = 1", true, false },
        { "DiSaBleBiNd=1,nChArBiNd=1", true, true },

        // ---- UNANCHORED ON PURPOSE: a longer keyword ending in the same text still matches. -------
        { "XDisableBind=1", true, false },
        { "DisableBind=1,MyNCharBind=1", true, true },

        // ---- TEXTUAL comparison against "1", not a numeric or boolean parse. ----------------------
        // "=2" captures nothing, because the pattern's group is (0|1)...
        { "DisableBind=2", false, false },

        // ...while "=10" captures the LEADING "1" and IS true, and "=01" captures the leading "0".
        { "DisableBind=10", true, false },
        { "DisableBind=01", false, false },

        // ---- MALFORMED OR TRUNCATED TEXT IS NOT AN ERROR CONDITION - it simply is not "1". --------
        { "DisableBind=", false, false },
        { "DisableBind", false, false },
        { "DisableBind=true", false, false },

        // ---- REALISTIC PARAMETER STRINGS CARRYING OTHER, UNRELATED PARAMETERS. --------------------
        // The keys have to be found IN CONTEXT, not only when they are the whole string. Both rows
        // carry unrelated parameters on both sides of the flags, and neither contains any credential,
        // account name or secret-shaped keyword (C-F) - a connection parameter string is exactly the
        // place a real one would hide, which is why these are synthetic and deliberately austere.
        { "ConnectString='DSN=SYNTHETIC',DisableBind=1,CommitOnDisconnect=0", true, false },
        {
            "ConnectString='DSN=SYNTHETIC',CommitOnDisconnect=0,DisableBind=1,"
                + "NCharBind=1,Probe=NEEDLE-context-dbparm-C8V3-synthetic",
            true,
            true
        },
    };

    /// <summary>
    /// The flag matrix. Both flags are asserted for every input, because the second one's value is
    /// meaningful only in relation to the first.
    /// </summary>
    /// <param name="dbParm">The connection parameter string.</param>
    /// <param name="expectedBindDisabled">The expected <c>DisableBind</c> result [:L128].</param>
    /// <param name="expectedNCharBinding">The expected <c>NCharBind</c> result [:L129-L130].</param>
    [Theory]
    [MemberData(nameof(DbParmFlagMatrix))]
    public void TheDbParmFlagMatrix(
        string dbParm,
        bool expectedBindDisabled,
        bool expectedNCharBinding)
    {
        TransactionData subject = new() { DbParm = dbParm };

        Assert.Equal(expectedBindDisabled, subject.IsBindDisabled);
        Assert.Equal(expectedNCharBinding, subject.IsNCharBindingEnabled);

        // The single-pass resolver must agree with the two properties in every cell, because it is
        // the one place the nested rule is written and they delegate to it.
        subject.ResolveDbParmFlags(out bool isBindDisabled, out bool isNCharBindingEnabled);
        Assert.Equal(expectedBindDisabled, isBindDisabled);
        Assert.Equal(expectedNCharBinding, isNCharBindingEnabled);
    }

    /// <summary>
    /// The nesting stated as its own assertion rather than only as matrix rows: enabling
    /// national-character binding requires the outer flag, and the SAME inner text flips from inert to
    /// effective purely because the outer flag changed.
    /// </summary>
    [Fact]
    public void NCharBindIsMeaninglessUnlessBindingIsDisabled()
    {
        TransactionData inert = new() { DbParm = "NCharBind=1" };
        Assert.False(inert.IsBindDisabled);
        Assert.False(inert.IsNCharBindingEnabled);

        TransactionData effective = inert with { DbParm = "NCharBind=1,DisableBind=1" };
        Assert.True(effective.IsBindDisabled);
        Assert.True(effective.IsNCharBindingEnabled);
    }

    /// <summary>
    /// The flags are computed from <see cref="TransactionData.DbParm"/> on every read rather than
    /// cached, so there is no stale state a <see langword="with"/> expression could leave behind.
    /// </summary>
    [Fact]
    public void TheFlagsAreComputedOnEveryReadRatherThanCached()
    {
        TransactionData subject = new() { DbParm = "DisableBind=1,NCharBind=1" };
        Assert.True(subject.IsBindDisabled);
        Assert.True(subject.IsNCharBindingEnabled);

        TransactionData rewritten = subject with { DbParm = "DisableBind=0,NCharBind=1" };
        Assert.False(rewritten.IsBindDisabled);
        Assert.False(rewritten.IsNCharBindingEnabled);

        // And the original is untouched, the type being an immutable value.
        Assert.True(subject.IsBindDisabled);
        Assert.True(subject.IsNCharBindingEnabled);
    }

    /// <summary>
    /// The cleared descriptor resolves both flags to <see langword="false"/>, so a descriptor that has
    /// never been given a parameter string cannot accidentally report that binding is disabled - the
    /// setting whose security consequence is the SQL-injection exposure the plan analyses.
    /// </summary>
    [Fact]
    public void TheClearedDescriptorReportsBothFlagsFalse()
    {
        TransactionData.Empty.ResolveDbParmFlags(
            out bool isBindDisabled,
            out bool isNCharBindingEnabled);

        Assert.False(isBindDisabled);
        Assert.False(isNCharBindingEnabled);
        Assert.False(TransactionData.Empty.IsBindDisabled);
        Assert.False(TransactionData.Empty.IsNCharBindingEnabled);
    }
}
