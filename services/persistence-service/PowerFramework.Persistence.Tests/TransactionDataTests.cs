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
//  here and nowhere else. The six suites below are, in the type's own order:
//
//    1  ToString() never discloses the password - directly, and after the operation that MOVES it.
//    2  System.Text.Json carries neither the NAME nor the VALUE of the three ignored members.
//    3  A member can participate FULLY in equality and NOT AT ALL in rendering, simultaneously.
//    4  The seven-of-nine transfer, audited in BOTH directions, with the two omissions proven.
//    5  The four-cell veto-and-diagnostic matrix, including the cell that proves 2 does not veto.
//    6  The DBParm flag matrix, including the nesting case that makes NCharBind=1 alone inert.
//
//  EVERY VALUE IN THIS FILE IS SYNTHETIC AND IS INVENTED HERE (C-F). Not one password, account
//  name, host name, connection string or parameter fragment is copied from the legacy tree, from
//  any of the eight catalogued in-source secret sites, or from any real system. The password-shaped
//  constants below are deliberately spelled so that they could not be mistaken for a credential and
//  so that a search of the repository finds them only in this file. They exist to be searched FOR in
//  rendered and serialized output - a test that asserts a secret is absent needs a distinctive
//  needle, and inventing the needle is the only way to have one without importing a real secret.
//
//  NO DATABASE, NO CONNECTION, NO DataWindow AND NO NETWORK IS INVOLVED ANYWHERE IN THIS FILE. The
//  subject is an immutable value type; the two accessors take hooks, which are supplied here as
//  local delegates. That is the whole environment these suites need.
//
//  THE ORACLE'S DEFECTS ARE ASSERTED AS BEHAVIOUR, NOT FLAGGED AS BUGS (C-B). Three of them are
//  pinned below on purpose and must not be "fixed" in the subject to make a test read better: the
//  veto test is a literal equality against 1 so a deep prevention of 2 does NOT veto; a vetoed
//  inbound call reports SUCCESS while writing nothing; and the parameterless outbound overload
//  DISCARDS the return code so a failure is invisible through it.
// ==============================================================================================

using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

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

    /// <summary>A synthetic DBMS identifier. Deliberately not any real provider's spelling.</summary>
    private const string SyntheticDbms = "SYNTHETIC-DBMS-A1";

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
        Dbms = SyntheticDbms,
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
    /// Every public instance property the type declares, keyed by name. Used by the reflection-based
    /// audits so each one states its expectation against a single shared reading of the surface.
    /// </summary>
    /// <returns>The property map.</returns>
    private static IReadOnlyDictionary<string, PropertyInfo> PublicInstanceProperties() =>
        typeof(TransactionData)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .ToDictionary(property => property.Name, StringComparer.Ordinal);

    // ==========================================================================================
    //  SUITE 0 - THE CLEARED STATE, WHICH EVERY OTHER SUITE RESTS ON
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
        Assert.Equal(string.Empty, subject.LogPass);
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
        (string Name, Type Type)[] expected =
        [
            (nameof(TransactionData.Dbms), typeof(string)),
            (nameof(TransactionData.ServerName), typeof(string)),
            (nameof(TransactionData.Database), typeof(string)),
            (nameof(TransactionData.LogId), typeof(string)),
            (nameof(TransactionData.LogPass), typeof(string)),
            (nameof(TransactionData.DbParm), typeof(string)),
            (nameof(TransactionData.Lock), typeof(string)),
            (nameof(TransactionData.AutoCommit), typeof(bool)),
            (nameof(TransactionData.UserParm), typeof(string)),
        ];

        foreach ((string name, Type type) in expected)
        {
            Assert.Contains(name, properties.Keys);
            Assert.Equal(type, properties[name].PropertyType);
        }
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
        Assert.Equal(SyntheticPassword, moved.LogPass);

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
    /// this, every "does not contain" above would also pass for a <see cref="ToString"/> that
    /// returned the empty string.
    /// </summary>
    [Fact]
    public void ToString_RendersExactlyTheSixPermittedMembersInDeclarationOrder()
    {
        string expected =
            $"{nameof(TransactionData)} {{ " +
            $"{nameof(TransactionData.Dbms)} = {SyntheticDbms}, " +
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
        Assert.Contains(SyntheticDbms, json, StringComparison.Ordinal);
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

        Assert.Equal(SyntheticDbms, revived.Dbms);
        Assert.Equal(SyntheticServerName, revived.ServerName);
        Assert.Equal(SyntheticDatabase, revived.Database);
        Assert.Equal(SyntheticLogId, revived.LogId);
        Assert.Equal(SyntheticLock, revived.Lock);
        Assert.True(revived.AutoCommit);

        Assert.Equal(string.Empty, revived.LogPass);
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
        Assert.Equal(source.LogPass, result.LogPass);
        Assert.Equal(source.DbParm, result.DbParm);
        Assert.Equal(source.Lock, result.Lock);

        // The two the legacy touches in NEITHER direction.
        Assert.Equal(keeper.AutoCommit, result.AutoCommit);
        Assert.Equal(keeper.UserParm, result.UserParm);
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
    /// own state and leaves the CALLER's two alone [n_cst_thread_trans.sru:L410-L416].
    /// </summary>
    [Fact]
    public void GetTransactionData_Outbound_MovesTheSevenAndLeavesTheCallersTwo()
    {
        TransactionData connection = FullyPopulated();
        TransactionData caller = ReceiverWithOnlyTheTwoSet();
        TransactionData callerBefore = caller;
        string diagnostic = string.Empty;

        long code = connection.GetTransactionData(ref caller, ref diagnostic);

        Assert.Equal(RetCode.OK, code);
        Assert.Equal(string.Empty, diagnostic);
        AssertSevenMovedAndTwoKept(caller, connection, callerBefore);
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

        MethodInfo only = Assert.Single(withMethods);
        Assert.Equal(nameof(TransactionData.WithConnectionFieldsFrom), only.Name);
        Assert.Single(only.GetParameters());

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
            AssertSevenMovedAndTwoKept(caller, connection, callerBefore);
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
            Dbms = "SYNTHETIC-DBMS-FROM-THE-HOOK",
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
        Assert.Equal(string.Empty, caller.LogPass);
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
        AssertSevenMovedAndTwoKept(caller, source, callerBefore);
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
    /// On the normal path the convenience overload returns the seven connection fields with the two
    /// omitted members CLEARED, because the descriptor it fills starts life cleared [:L395].
    /// </summary>
    [Fact]
    public void GetTransactionData_ConvenienceOverload_ReturnsTheSevenWithTheTwoCleared()
    {
        TransactionData connection = FullyPopulated();

        TransactionData returned = connection.GetTransactionData();

        AssertSevenMovedAndTwoKept(returned, connection, TransactionData.Empty);
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
    // ==========================================================================================

    /// <summary>
    /// The flag matrix. Both flags are asserted for every input, because the second one's value is
    /// meaningful only in relation to the first.
    /// </summary>
    /// <param name="dbParm">The connection parameter string.</param>
    /// <param name="expectedBindDisabled">The expected <c>DisableBind</c> result [:L128].</param>
    /// <param name="expectedNCharBinding">The expected <c>NCharBind</c> result [:L129-L130].</param>
    [Theory]
    // Absent keys.
    [InlineData("", false, false)]
    [InlineData("Probe=NEEDLE-no-flags-here", false, false)]
    // The outer key alone, both values.
    [InlineData("DisableBind=1", true, false)]
    [InlineData("DisableBind=0", false, false)]
    // THE NESTING CASE: the inner key alone is legal and INERT.
    [InlineData("NCharBind=1", false, false)]
    // ...and it stays inert when the outer key is present but zero.
    [InlineData("DisableBind=0,NCharBind=1", false, false)]
    // Both set - the only combination that enables national-character binding.
    [InlineData("DisableBind=1,NCharBind=1", true, true)]
    [InlineData("NCharBind=1,DisableBind=1", true, true)]
    [InlineData("DisableBind=1,NCharBind=0", true, false)]
    // The oracle's own whitespace tolerance, from the \s* on both sides of the '='.
    [InlineData("DisableBind = 1 , NCharBind = 1", true, true)]
    [InlineData("DisableBind\t=\t1,NCharBind\t=\t1", true, true)]
    // Case insensitivity, which is the specified port behaviour.
    [InlineData("disablebind=1,ncharbind=1", true, true)]
    [InlineData("DISABLEBIND=1", true, false)]
    // UNANCHORED ON PURPOSE: a longer keyword ending in the same text still matches. Both keys.
    [InlineData("XDisableBind=1", true, false)]
    [InlineData("DisableBind=1,MyNCharBind=1", true, true)]
    // TEXTUAL comparison against "1": "=2" matches nothing, so the flag is false...
    [InlineData("DisableBind=2", false, false)]
    // ...while "=10" matches the leading "1" and IS true, and "=01" matches the leading "0".
    [InlineData("DisableBind=10", true, false)]
    [InlineData("DisableBind=01", false, false)]
    // Malformed or truncated text is not an error condition - it simply is not "1".
    [InlineData("DisableBind=", false, false)]
    [InlineData("DisableBind", false, false)]
    [InlineData("DisableBind=true", false, false)]
    // A realistic longer parameter string, to show the keys are found in context.
    [InlineData("ConnectString='DSN=SYNTHETIC',DisableBind=1,CommitOnDisconnect=0", true, false)]
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
