// ==============================================================================================
//  DbErrorDataTests - the characterization suite that pins
//  PowerFramework.Persistence.Errors.DbErrorData
//  --------------------------------------------------------------------------------------------
//  SYSTEM UNDER TEST  services/persistence-service/PowerFramework.Persistence/Errors/DbErrorData.cs
//  BEHAVIOURAL ORACLE ws_objects/pfw.thread.ext.pbl.src/dberrordata.srs (11 lines, READ ONLY)
//                     ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru
//                     ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlbase.sru
//                     ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru
//                     ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlupdate.sru
//                     ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds.sru
//                     all READ ONLY per constraint C-C - read as specification, never edited
//
//  WHAT THIS SUITE IS ACTUALLY PROTECTING
//  --------------------------------------------------------------------------------------------
//  DbErrorData looks like a plain five-member data carrier, and that appearance is the risk. TWO
//  of its five members are NOT auto-properties: the string members project a canonicalised backing
//  field so that the CLEARED STATE has exactly one representation. The legacy clears its retained
//  error by assigning a freshly declared structure - the `DBERRORDATA emptyData` idiom at
//  n_cst_threading_task_sqlbase.sru:L55 and :L206, assigned at :L65 and :L208 - and PowerBuilder
//  initialises `long` to 0 and `string` to "". Every "was an error recorded?" check in the ported
//  service rests on that state comparing equal however it was spelled.
//
//  A regression here would be SILENT. Replace either accessor with a plain auto-property and each
//  instance still reads back exactly as expected when inspected member by member - only the
//  equality between two differently-spelled cleared values breaks, and only at the call sites that
//  compare. That is precisely why the equality cases below are not incidental coverage but the
//  centre of this suite.
//
//  THE Buffer MEMBER IS A PLAIN AUTO-PROPERTY, AND THAT IS LOAD-BEARING TOO
//  --------------------------------------------------------------------------------------------
//  It needs no canonicalisation only because DwBuffer.Primary is the published enum's ZERO member,
//  so `default` already observes as the buffer PowerBuilder itself defaults to. That agreement is
//  a property of the CONTRACT rather than of this type, and it is the kind of thing a later edit to
//  common.v1.proto could break from a distance with nothing in the build to notice. The two domain
//  guards below - one for DwBuffer, one for ItemStatus - exist to notice, by asserting the member
//  count and the zero value directly against the generated enums.
//
//  Buffer is deliberately NOT one of those two, and the domain tests below are what keep it that
//  way. DwBuffer.Primary is the published enum's ZERO member, so a struct's all-bits-zero default
//  already observes as Primary and stores the same value an explicit assignment stores - the
//  canonicalisation the strings need is unnecessary there. That holds only while the contract keeps
//  Primary at zero, which is why the numeric value is asserted rather than assumed.
//
//  THE ONE ASSERTION THAT CANNOT BE WRITTEN CARELESSLY
//  --------------------------------------------------------------------------------------------
//  The Chinese diagnostic is compared against a literal written INDEPENDENTLY in this file rather
//  than against DbErrorMessages.NoUpdatableTable. Asserting a constant against itself would pass
//  for any value at all, including a value with one wrong CJK character - a difference that is
//  invisible in review and fails every parity comparison against the oracle. The independent
//  literal here is the guard, so it must stay independent: do not "simplify" it to a reference to
//  the constant.
//
//  C-F SELF-AUDIT: every value in this file is synthetic. No credential, key, token, password,
//  connection string or certificate appears, and no statement text captured from a real log is
//  used as a fixture - which matters here because DbErrorData.SqlSyntax is precisely the member
//  that carries interpolated literals in the legacy.
//
// ==============================================================================================

using System.Text.Json;
using PowerFramework.Contracts.Common.V1;
using PowerFramework.Persistence.Errors;
using Xunit;

namespace PowerFramework.Persistence.Tests;

/// <summary>
/// Characterization tests for <see cref="DbErrorData"/> and its companion
/// <see cref="DbErrorMessages"/>.
/// </summary>
public sealed class DbErrorDataTests
{
    /// <summary>
    /// The Chinese diagnostic, written out INDEPENDENTLY of
    /// <see cref="DbErrorMessages.NoUpdatableTable"/> so that comparing the two is a real check
    /// rather than a tautology. Transcribed from
    /// [ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru:L190].
    /// </summary>
    private const string ExpectedNoUpdatableTableText = "没有可更新的表";

    // ==========================================================================================
    //  THE CLEARED STATE - the port of `DBERRORDATA emptyData`
    // ==========================================================================================

    /// <summary>
    /// <c>default(DbErrorData)</c> must observe exactly the state a freshly declared PowerBuilder
    /// structure carries: zero code, empty text, empty statement, Primary buffer, zero row
    /// [n_cst_threading_task_sqlbase.sru:L55, L65].
    /// </summary>
    [Fact]
    public void Default_ObservesTheLegacyClearedState()
    {
        DbErrorData sut = default;

        Assert.Equal(0L, sut.SqlDbCode);
        Assert.Equal(string.Empty, sut.SqlErrText);
        Assert.Equal(string.Empty, sut.SqlSyntax);
        Assert.Equal(DwBuffer.Primary, sut.Buffer);
        Assert.Equal(0L, sut.Row);
    }

    /// <summary>
    /// Neither string member may ever observe as <see langword="null"/>. A
    /// <see cref="NullReferenceException"/> raised while reporting a database error would replace
    /// the diagnostic the caller needs with a defect in the diagnostic path itself.
    /// </summary>
    [Fact]
    public void Default_StringMembersAreNeverNull()
    {
        DbErrorData sut = default;

        Assert.NotNull(sut.SqlErrText);
        Assert.NotNull(sut.SqlSyntax);
    }

    /// <summary>
    /// <see cref="DbErrorData.Empty"/> is defined as <see langword="default"/>, so the two cannot
    /// disagree. This test exists to pin that they are the same value rather than two independently
    /// maintained ones.
    /// </summary>
    [Fact]
    public void Empty_EqualsDefault()
    {
        Assert.Equal(default, DbErrorData.Empty);
        Assert.True(DbErrorData.Empty == default);
    }

    /// <summary>
    /// The canonicalisation payoff, and the assertion most likely to regress: an instance built
    /// with EXPLICIT empty strings and an explicit Primary buffer must compare equal to the
    /// cleared state. Record-struct equality compares fields rather than properties, so this only
    /// holds because the initializers fold "empty" and "absent" onto one stored value.
    /// </summary>
    [Fact]
    public void ExplicitlyEmptyInstance_EqualsEmpty()
    {
        DbErrorData explicitlyEmpty = new()
        {
            SqlDbCode = 0,
            SqlErrText = "",
            SqlSyntax = "",
            Buffer = DwBuffer.Primary,
            Row = 0,
        };

        Assert.Equal(DbErrorData.Empty, explicitlyEmpty);
        Assert.True(explicitlyEmpty == DbErrorData.Empty);
        Assert.Equal(DbErrorData.Empty.GetHashCode(), explicitlyEmpty.GetHashCode());
    }

    /// <summary>
    /// <b>The guard against DB-01 recurring.</b> The published <see cref="DwBuffer"/> domain must be
    /// EXACTLY the three legacy DataWindow buffers, with <see cref="DwBuffer.Primary"/> on zero so
    /// that an unassigned member observes as the buffer PowerBuilder itself defaults to. A synthetic
    /// fourth member would grow a three-value legacy domain, shift every real value, and move this
    /// type's cleared state off Primary - which is precisely the defect this assertion exists to
    /// catch, because nothing else in the build would.
    /// </summary>
    /// <remarks>
    /// The domain is asserted through <see cref="Enum.GetValues{TEnum}"/> rather than by naming the
    /// three members, so that ADDING a member fails this test rather than slipping past it.
    /// </remarks>
    [Fact]
    public void DwBufferDomain_IsExactlyTheThreeLegacyBuffers_WithPrimaryOnZero()
    {
        DwBuffer[] domain = Enum.GetValues<DwBuffer>();

        Assert.Equal(3, domain.Length);
        Assert.Equal([DwBuffer.Primary, DwBuffer.Delete, DwBuffer.Filter], domain);
        Assert.Equal(0, (int)DwBuffer.Primary);
        Assert.Equal(DwBuffer.Primary, default(DwBuffer));
    }

    /// <summary>
    /// The companion half of the same guard. The published <see cref="ItemStatus"/> domain must be
    /// EXACTLY the four <c>dwItemStatus</c> literals, in PowerBuilder's own declaration order, with
    /// <see cref="ItemStatus.NotModified"/> on zero. <c>New!</c> is carried even though it is never
    /// named literally anywhere in the in-scope legacy sources, because it is a legal runtime status
    /// and a three-member domain would make some real rows unrepresentable on the wire.
    /// </summary>
    /// <remarks>
    /// This test pins the property the plain auto-property depends on. If a synthetic
    /// <c>UNSPECIFIED = 0</c> were ever reintroduced to the contract, every real value would shift
    /// up by one, <c>default(DbErrorData).Buffer</c> would silently stop observing as
    /// <c>Primary</c>, and the cleared-state equivalence class asserted below would split - none of
    /// which would produce a compiler diagnostic. Asserting the numeric zero rather than only the
    /// symbolic name is what makes that regression fail here instead of in a characterization
    /// comparison much later.
    /// </remarks>
    [Fact]
    public void ItemStatusDomain_IsExactlyTheFourLegacyStatuses_WithNotModifiedOnZero()
    {
        ItemStatus[] domain = Enum.GetValues<ItemStatus>();

        Assert.Equal(4, domain.Length);
        Assert.Equal(
            [ItemStatus.NotModified, ItemStatus.DataModified, ItemStatus.New, ItemStatus.NewModified],
            domain);
        Assert.Equal(0, (int)ItemStatus.NotModified);
        Assert.Equal(ItemStatus.NotModified, default(ItemStatus));
    }

    /// <summary>
    /// <see cref="DbErrorData.Buffer"/> needs no canonicalisation now that
    /// <see cref="DwBuffer.Primary"/> is the enum's zero member: setting it explicitly and leaving
    /// it unset must produce the same instance, with no bridging code in the accessor.
    /// </summary>
    /// <remarks>
    /// The numeric assertion is deliberate and is the substance of the test. AAP 0.4.5.3 forbids
    /// renumbering a legacy value because these numbers appear in serialized payloads, log records
    /// and characterization recordings; a sentinel inserted at zero would shift all three members
    /// up by one, and nothing but an assertion on the number itself would catch it.
    /// </remarks>
    [Fact]
    public void Buffer_ExplicitPrimary_EqualsTheClearedState()
    {
        DbErrorData sut = new() { Buffer = DwBuffer.Primary };

        Assert.Equal(DwBuffer.Primary, sut.Buffer);
        Assert.Equal(DbErrorData.Empty, sut);
    }

    /// <summary>
    /// The other two buffers must round-trip untouched and must NOT compare equal to the cleared
    /// state. Stated separately from the cleared-state assertions because an accessor that swallowed
    /// a real buffer would be invisible to them.
    /// </summary>
    [Theory]
    [InlineData(DwBuffer.Delete)]
    [InlineData(DwBuffer.Filter)]
    public void Buffer_NonPrimaryBuffers_RoundTripAndAreNotTheClearedState(DwBuffer buffer)
    {
        DbErrorData sut = new() { Buffer = buffer };

        Assert.Equal(buffer, sut.Buffer);
        Assert.NotEqual(DbErrorData.Empty, sut);
    }

    /// <summary>
    /// The ordinal half of the same guard, asserted on the NUMBERS rather than on the domain
    /// membership, because a sentinel inserted at zero would keep every member name intact while
    /// shifting every value.
    /// </summary>
    /// <remarks>
    /// The numeric assertion is deliberate and is the substance of the test. AAP 0.4.5.3 forbids
    /// renumbering a legacy value because these numbers appear in serialized payloads, log records
    /// and characterization recordings; a sentinel inserted at zero would shift all three members
    /// up by one, and nothing but an assertion on the number itself would catch it.
    /// </remarks>
    [Fact]
    public void Buffer_ZeroMemberIsPrimary_SoTheClearedStateNeedsNoFold()
    {
        Assert.Equal(0, (int)DwBuffer.Primary);
        Assert.Equal(1, (int)DwBuffer.Delete);
        Assert.Equal(2, (int)DwBuffer.Filter);

        DbErrorData sut = new() { Buffer = DwBuffer.Primary };

        Assert.Equal(DwBuffer.Primary, sut.Buffer);
        Assert.Equal(DwBuffer.Primary, default(DbErrorData).Buffer);
        Assert.Equal(DbErrorData.Empty, sut);
    }

    /// <summary>
    /// The companion agreement for the status domain: <see cref="ItemStatus.NotModified"/> occupies
    /// zero, so the four legacy statuses keep their natural numbers and no sentinel displaces them.
    /// </summary>
    [Fact]
    public void ItemStatus_ZeroMemberIsNotModified_AndTheDomainIsUnshifted()
    {
        Assert.Equal(0, (int)ItemStatus.NotModified);
        Assert.Equal(1, (int)ItemStatus.DataModified);
        Assert.Equal(2, (int)ItemStatus.New);
        Assert.Equal(3, (int)ItemStatus.NewModified);
    }

    /// <summary>
    /// Every spelling of the cleared state must be one equivalence class. Stated as a single test
    /// because the failure mode is a partition into several classes, which no per-member assertion
    /// would reveal.
    /// </summary>
    [Fact]
    public void AllSpellingsOfTheClearedState_AreOneEquivalenceClass()
    {
        DbErrorData[] clearedSpellings =
        [
            default,
            DbErrorData.Empty,
            new DbErrorData(),
            new DbErrorData { SqlErrText = "", SqlSyntax = "", Buffer = DwBuffer.Primary },
            new DbErrorData { SqlErrText = null, SqlSyntax = null, Buffer = default },
            DbErrorData.FromTransaction(0, ""),
            DbErrorData.FromTransaction(0, null),
            DbErrorData.FromStatement(0, "", "", DwBuffer.Primary, 0),
            DbErrorData.FromStatement(0, null, null, default, 0),
        ];

        foreach (DbErrorData spelling in clearedSpellings)
        {
            Assert.Equal(DbErrorData.Empty, spelling);
        }
    }

    // ==========================================================================================
    //  THE MEASURED RAISE SHAPES
    // ==========================================================================================

    /// <summary>
    /// <see cref="DbErrorData.NoUpdatableTable"/> must reproduce
    /// <c>Event OnDBError(-1,"...","",Primary!,0)</c>
    /// [n_cst_thread_task_sqlupdate.sru:L190] exactly, including the synthetic <c>-1</c>.
    /// </summary>
    [Fact]
    public void NoUpdatableTable_ReproducesTheMeasuredShape()
    {
        DbErrorData sut = DbErrorData.NoUpdatableTable();

        Assert.Equal(-1L, sut.SqlDbCode);
        Assert.Equal(ExpectedNoUpdatableTableText, sut.SqlErrText);
        Assert.Equal(string.Empty, sut.SqlSyntax);
        Assert.Equal(DwBuffer.Primary, sut.Buffer);
        Assert.Equal(0L, sut.Row);
    }

    /// <summary>
    /// The CJK guard. The constant is compared against a literal written independently in this
    /// file, so a single wrong or transposed character fails here rather than silently reaching a
    /// parity comparison.
    /// </summary>
    [Fact]
    public void NoUpdatableTableMessage_MatchesTheOracleTextCharacterForCharacter()
    {
        Assert.Equal(ExpectedNoUpdatableTableText, DbErrorMessages.NoUpdatableTable);
        Assert.Equal(7, DbErrorMessages.NoUpdatableTable.Length);
    }

    /// <summary>
    /// The synthetic <c>-1</c> is a provider-code-space value the framework invented, not a return
    /// code. It is pinned separately because the tempting "cleanup" is to express it as a shared
    /// kernel constant, which would misrepresent which code space the member carries.
    /// </summary>
    [Fact]
    public void NoUpdatableTable_CodeIsTheSyntheticMinusOne()
    {
        Assert.Equal(-1L, DbErrorData.NoUpdatableTable().SqlDbCode);
    }

    /// <summary>
    /// <see cref="DbErrorData.FromTransaction"/> covers the five sites that pass an empty statement
    /// with the cleared buffer and row [n_cst_thread_task_sqlupdate.sru:L197, :L293;
    /// n_cst_thread_task_sqlquery.sru:L521, :L748; n_cst_thread_task_sqlcommand.sru:L71].
    /// </summary>
    [Theory]
    [InlineData(-2147217900L, "synthetic provider message")]
    [InlineData(1L, "synthetic constraint violation")]
    [InlineData(0L, "")]
    public void FromTransaction_CarriesCodeAndTextAndDefaultsTheRest(long code, string text)
    {
        DbErrorData sut = DbErrorData.FromTransaction(code, text);

        Assert.Equal(code, sut.SqlDbCode);
        Assert.Equal(text, sut.SqlErrText);
        Assert.Equal(string.Empty, sut.SqlSyntax);
        Assert.Equal(DwBuffer.Primary, sut.Buffer);
        Assert.Equal(0L, sut.Row);
    }

    /// <summary>
    /// <see cref="DbErrorData.FromStatement"/> must round-trip all five members, including the
    /// non-Primary buffers and non-zero row that only the datastore forwarding path can supply
    /// [n_cst_thread_task_sqlbase_ds.sru:L159].
    /// </summary>
    [Theory]
    [InlineData(-2147217873L, "synthetic message", "UPDATE COMPANY SET NAME = ?", DwBuffer.Delete, 3L)]
    [InlineData(-2147217873L, "synthetic message", "DELETE FROM COMPANY WHERE ID = ?", DwBuffer.Filter, 17L)]
    [InlineData(19L, "synthetic message", "INSERT INTO COMPANY (NAME) VALUES (?)", DwBuffer.Primary, 1L)]
    public void FromStatement_RoundTripsAllFiveMembers(
        long code,
        string text,
        string statement,
        DwBuffer buffer,
        long row)
    {
        DbErrorData sut = DbErrorData.FromStatement(code, text, statement, buffer, row);

        Assert.Equal(code, sut.SqlDbCode);
        Assert.Equal(text, sut.SqlErrText);
        Assert.Equal(statement, sut.SqlSyntax);
        Assert.Equal(buffer, sut.Buffer);
        Assert.Equal(row, sut.Row);
    }

    /// <summary>
    /// The shape at [n_cst_thread_task_sqlquery.sru:L855], which is the one statement-carrying site
    /// that passes <c>row = 1</c> rather than <c>0</c>.
    /// </summary>
    [Fact]
    public void FromStatement_PreservesTheRowOneShape()
    {
        DbErrorData sut = DbErrorData.FromStatement(
            -1,
            "synthetic retrieve failure",
            "SELECT * FROM COMPANY WHERE ID = ?",
            DwBuffer.Primary,
            1);

        Assert.Equal(1L, sut.Row);
        Assert.Equal(DwBuffer.Primary, sut.Buffer);
    }

    /// <summary>
    /// A statement that arrives empty must still canonicalise, so a statement-carrying factory
    /// called with no statement is indistinguishable from one of the empty-statement sites.
    /// </summary>
    [Fact]
    public void FromStatement_EmptyStatementCanonicalises()
    {
        DbErrorData viaStatement = DbErrorData.FromStatement(5, "synthetic busy", "", DwBuffer.Primary, 0);
        DbErrorData viaTransaction = DbErrorData.FromTransaction(5, "synthetic busy");

        Assert.Equal(viaTransaction, viaStatement);
    }

    // ==========================================================================================
    //  NULL TEXT REACHING THE FACTORIES - the DB-04 contract
    //  ----------------------------------------------------------------------------------------
    //  Both factories accept nullable text because the real producer is an ADO.NET provider, whose
    //  message and statement are nullable references. The point of these cases is not that null
    //  becomes empty - that is the canonicalisation already covered above - but that a caller CAN
    //  HAND THEM A NULL AT ALL, with no coercion and no suppression at the call site. If the
    //  parameters were ever narrowed back to non-nullable `string`, these calls would stop
    //  compiling, and under TreatWarningsAsErrors that is a build failure rather than a warning.
    // ==========================================================================================

    /// <summary>
    /// <see cref="DbErrorData.FromTransaction"/> must accept a null message and canonicalise it, so
    /// that a provider error with no text is the same value as one with an empty text.
    /// </summary>
    [Fact]
    public void FromTransaction_AcceptsNullText_AndCanonicalisesIt()
    {
        string? absentProviderText = null;

        DbErrorData viaNull = DbErrorData.FromTransaction(-1, absentProviderText);

        Assert.Equal(string.Empty, viaNull.SqlErrText);
        Assert.Equal(DbErrorData.FromTransaction(-1, ""), viaNull);
    }

    /// <summary>
    /// <see cref="DbErrorData.FromStatement"/> must accept a null message AND a null statement on
    /// the same terms. Both are separately nullable at the provider, so both are exercised here.
    /// </summary>
    [Fact]
    public void FromStatement_AcceptsNullTextAndNullStatement_AndCanonicalisesBoth()
    {
        string? absentProviderText = null;
        string? absentStatement = null;

        DbErrorData viaNulls = DbErrorData.FromStatement(
            -1,
            absentProviderText,
            absentStatement,
            DwBuffer.Filter,
            7);

        Assert.Equal(string.Empty, viaNulls.SqlErrText);
        Assert.Equal(string.Empty, viaNulls.SqlSyntax);
        Assert.Equal(DwBuffer.Filter, viaNulls.Buffer);
        Assert.Equal(7L, viaNulls.Row);
        Assert.Equal(DbErrorData.FromStatement(-1, "", "", DwBuffer.Filter, 7), viaNulls);
    }

    /// <summary>
    /// The object-initializer path must accept null on both string members too, since
    /// <c>with</c> expressions and direct initialization are how the ported consumers build these
    /// values. This is the assertion that pins the <c>[AllowNull]</c> annotation on the accessors.
    /// </summary>
    [Fact]
    public void Initializer_AcceptsNullOnBothStringMembers()
    {
        string? absent = null;

        DbErrorData sut = new() { SqlErrText = absent, SqlSyntax = absent };

        Assert.Equal(string.Empty, sut.SqlErrText);
        Assert.Equal(string.Empty, sut.SqlSyntax);
        Assert.Equal(DbErrorData.Empty, sut);

        DbErrorData populated = DbErrorData.FromStatement(9, "text", "SELECT 1", DwBuffer.Primary, 3);
        DbErrorData nulled = populated with { SqlErrText = absent, SqlSyntax = absent };

        Assert.Equal(string.Empty, nulled.SqlErrText);
        Assert.Equal(string.Empty, nulled.SqlSyntax);
        Assert.Equal(9L, nulled.SqlDbCode);
        Assert.Equal(3L, nulled.Row);
    }

    // ==========================================================================================
    //  THE MUTATION AND MARSHALLING SHAPES THE PORTED CONSUMERS DEPEND ON
    // ==========================================================================================

    /// <summary>
    /// The expression the row remap in <c>Tasks/TaskProxies/</c> is written as. The legacy assigns
    /// <c>_lastDBError.row = nRow</c> and touches nothing else
    /// [n_cst_threading_task_sqlupdate.sru:L325, :L337], so the <c>with</c> expression must change
    /// <see cref="DbErrorData.Row"/> and leave the other four members intact.
    /// </summary>
    [Fact]
    public void WithExpression_ChangesRowAndNothingElse()
    {
        DbErrorData original = DbErrorData.FromStatement(
            -2147217873,
            "synthetic message",
            "UPDATE COMPANY SET AGE = ?",
            DwBuffer.Filter,
            2);

        DbErrorData remapped = original with { Row = 42 };

        Assert.Equal(42L, remapped.Row);
        Assert.Equal(original.SqlDbCode, remapped.SqlDbCode);
        Assert.Equal(original.SqlErrText, remapped.SqlErrText);
        Assert.Equal(original.SqlSyntax, remapped.SqlSyntax);
        Assert.Equal(original.Buffer, remapped.Buffer);
        Assert.NotEqual(original, remapped);
        Assert.Equal(2L, original.Row);
    }

    /// <summary>
    /// The remap's own guard: it returns without touching the payload when the ordinal is not
    /// positive [n_cst_threading_task_sqlupdate.sru:L315]. Expressed here as the observable fact a
    /// consumer branches on, so the guard cannot be dropped silently.
    /// </summary>
    [Theory]
    [InlineData(0L, false)]
    [InlineData(-1L, false)]
    [InlineData(1L, true)]
    [InlineData(2L, true)]
    public void Row_IsRemappableOnlyWhenPositive(long row, bool expectedRemappable)
    {
        DbErrorData sut = DbErrorData.FromStatement(-1, "synthetic", "", DwBuffer.Primary, row);

        Assert.Equal(expectedRemappable, sut.Row > 0);
    }

    /// <summary>
    /// Mirrors <c>of_gettransobject(ref n_cst_thread_trans, ref dberrordata)</c>
    /// [n_cst_thread_task_sqlbase.sru:L148], including the partial population at :L175-L176 where a
    /// failed connect sets only the code and the text and leaves the other three members cleared.
    /// This test exists mainly to prove the <see langword="ref"/> usage compiles at all.
    /// </summary>
    [Fact]
    public void RefParameter_PartialPopulationMatchesFromTransaction()
    {
        DbErrorData carrier = default;

        long returned = PopulateOnConnectFailure(ref carrier, -2147217843, "synthetic connect failure");

        Assert.Equal(-7L, returned);
        Assert.Equal(DbErrorData.FromTransaction(-2147217843, "synthetic connect failure"), carrier);
    }

    /// <summary>
    /// The value-copy semantics the legacy relies on: <c>_lastDBError = err</c> copies
    /// [n_cst_threading_task_sqlbase.sru:L44] and <c>return _lastDBError</c> returns a copy [:L47],
    /// so a later mutation of one must not be visible through the other.
    /// </summary>
    [Fact]
    public void AssignmentCopies_SoMutationDoesNotAlias()
    {
        DbErrorData retained = DbErrorData.FromTransaction(5, "synthetic busy");
        DbErrorData observed = retained;

        retained = retained with { Row = 9 };

        Assert.Equal(0L, observed.Row);
        Assert.Equal(9L, retained.Row);
    }

    /// <summary>
    /// Last error wins: a second error replaces the first wholesale
    /// [n_cst_threading_task_sqlbase.sru:L44]. There is no accumulation and no history, and this
    /// test pins that the type offers nothing that would enable one.
    /// </summary>
    [Fact]
    public void SecondErrorReplacesTheFirstWholesale()
    {
        DbErrorData retained = DbErrorData.NoUpdatableTable();
        DbErrorData second = DbErrorData.FromTransaction(5, "synthetic busy");

        retained = second;

        Assert.Equal(second, retained);
        Assert.NotEqual(ExpectedNoUpdatableTableText, retained.SqlErrText);
    }

    /// <summary>
    /// Both legacy reset points assign a freshly declared structure
    /// [n_cst_threading_task_sqlbase.sru:L65 in <c>of_reset</c> and :L208 in <c>onprepare</c>], so
    /// clearing a populated payload must return it to exactly the cleared state.
    /// </summary>
    [Fact]
    public void AssigningEmpty_RestoresTheClearedState()
    {
        DbErrorData retained = DbErrorData.FromStatement(
            -1,
            "synthetic",
            "SELECT 1",
            DwBuffer.Filter,
            4);

        retained = DbErrorData.Empty;

        Assert.Equal(default, retained);
        Assert.Equal(string.Empty, retained.SqlErrText);
        Assert.Equal(string.Empty, retained.SqlSyntax);
        Assert.Equal(DwBuffer.Primary, retained.Buffer);
    }

    // ==========================================================================================
    //  THE SERIALIZED ORDER
    // ==========================================================================================

    /// <summary>
    /// The renderer names the members in declaration order, which is the oracle's order
    /// [dberrordata.srs:L4-L8] and the wire mirror's field order. Asserted as relative positions
    /// rather than as one exact string so the test pins the ORDER without becoming a brittle snapshot.
    /// </summary>
    /// <remarks>
    /// <c>ToString()</c> IS HAND-WRITTEN RATHER THAN COMPILER-GENERATED, and this test is why the
    /// hand-written one still names every member: withholding the statement's VALUE is the point, and
    /// dropping the member NAME with it would have made the diagnostic unreadable and broken the wire
    /// mirror's ordering guarantee at the same time. The withholding itself is asserted separately below.
    /// </remarks>
    [Fact]
    public void ToString_RendersMembersInLegacyDeclarationOrder()
    {
        string rendered = DbErrorData.FromStatement(
            -1,
            "synthetic",
            "SELECT 1",
            DwBuffer.Delete,
            2).ToString();

        int code = rendered.IndexOf("SqlDbCode", StringComparison.Ordinal);
        int text = rendered.IndexOf("SqlErrText", StringComparison.Ordinal);
        int syntax = rendered.IndexOf("SqlSyntax", StringComparison.Ordinal);
        int buffer = rendered.IndexOf("Buffer", StringComparison.Ordinal);
        int row = rendered.IndexOf("Row", StringComparison.Ordinal);

        Assert.True(code >= 0 && text >= 0 && syntax >= 0 && buffer >= 0 && row >= 0);
        Assert.True(code < text, "SqlDbCode must precede SqlErrText");
        Assert.True(text < syntax, "SqlErrText must precede SqlSyntax");
        Assert.True(syntax < buffer, "SqlSyntax must precede Buffer");
        Assert.True(buffer < row, "Buffer must precede Row");
    }

    /// <summary>
    /// The two private canonicalising backing fields must not leak into the rendered form, which
    /// would both duplicate a member and expose the storage representation.
    /// </summary>
    [Fact]
    public void ToString_DoesNotRenderTheBackingFields()
    {
        string rendered = DbErrorData.FromTransaction(1, "synthetic").ToString();

        Assert.DoesNotContain("_sqlErrText", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("_sqlSyntax", rendered, StringComparison.Ordinal);
    }

    // ==========================================================================================
    //  THE CONTAINMENT OF THE RAW PAYLOAD - three doors, each closed and each tested
    // ==========================================================================================

    /// <summary>
    /// The raw payload type is <see langword="internal"/>, so no consumer outside this assembly can
    /// hold it - and therefore cannot print it, serialize it or hand it anywhere.
    /// </summary>
    /// <remarks>
    /// THE FIRST OF THE THREE DOORS. As a public type this payload could cross an assembly boundary
    /// carrying the complete generated statement with its interpolated literal values; the only shape
    /// that may now cross is the redacted wire <c>DbError</c>. This suite can still construct one only
    /// because the csproj grants it <c>InternalsVisibleTo</c>, which is what makes the containment
    /// testable rather than merely asserted - and note that <c>ToDbError</c> and the payload overload of
    /// <c>SqlRedactor.Redact</c> followed the payload down to internal for the same reason.
    /// </remarks>
    [Fact]
    public void TheRawPayloadIsInternalSoItCannotLeaveThisAssembly()
    {
        Assert.False(typeof(DbErrorData).IsPublic);
        Assert.True(typeof(DbErrorData).IsNotPublic);

        // The two members that take or return it went with it, so there is no public signature left
        // through which an outside caller could obtain one.
        Assert.False(typeof(DbErrorDataExtensions).IsPublic);
        Assert.DoesNotContain(
            typeof(SqlRedactor)
                .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance),
            method => method.ReturnType == typeof(DbErrorData));

        // But the STRING member of the abstraction stays public: nothing about containing the payload
        // narrows the redactor itself, which every caller still needs.
        Assert.True(typeof(ISqlRedactor).IsPublic);
        Assert.True(typeof(SqlRedactor).IsPublic);
    }

    /// <summary>
    /// <c>ToString()</c> never renders the statement text - which the compiler-generated renderer did,
    /// in full.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE SECOND DOOR, AND THE ONE THAT NEEDED NO BYPASS TO OPEN. A record's generated renderer prints
    /// every member, so <c>logger.LogError("update failed: {Error}", dbErrData)</c> - the single most
    /// natural line anyone would write at a database-error site - emitted the whole statement into the
    /// log. The redactor could not help: it was never called.
    /// </para>
    /// <para>
    /// The statement below is shaped like a real interpolated one so that the assertion is meaningful:
    /// it carries a quoted name, a bare number and a blob, which are the three literal forms
    /// <c>DisableBind=1</c> produces.
    /// </para>
    /// </remarks>
    [Fact]
    public void ToString_NeverRendersTheStatementText()
    {
        const string statement =
            "UPDATE COMPANY SET NAME = 'Alice', SALARY = 12345, ADDRESS = 0xDEADBEEF WHERE ID = 7";

        string rendered = DbErrorData
            .FromStatement(-1, "constraint violated", statement, DwBuffer.Primary, 1)
            .ToString();

        Assert.DoesNotContain(statement, rendered, StringComparison.Ordinal);

        foreach (string fragment in new[] { "Alice", "12345", "0xDEADBEEF", "UPDATE", "COMPANY", "SET" })
        {
            Assert.DoesNotContain(fragment, rendered, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// What the renderer DOES say about the statement: that one was present, and how long it was.
    /// </summary>
    /// <remarks>
    /// PRESENCE AND LENGTH ARE STRUCTURE, NOT DATA, and they are what a reader needs in order to
    /// correlate a local diagnostic with the redacted wire payload for the same failure. An absent
    /// statement renders distinctly from a present one, because six of the nine legacy raise sites leave
    /// it empty and "no statement" is a materially different diagnostic from "a statement I am not
    /// showing you".
    /// </remarks>
    [Fact]
    public void ToString_RendersThePresenceAndLengthOfTheStatementAndNothingElseAboutIt()
    {
        const string statement = "SELECT 1";

        string present = DbErrorData
            .FromStatement(-1, "synthetic", statement, DwBuffer.Primary, 1)
            .ToString();

        Assert.Contains("<withheld, 8 chars>", present, StringComparison.Ordinal);

        string absent = DbErrorData.FromTransaction(-1, "synthetic").ToString();

        Assert.Contains("<none>", absent, StringComparison.Ordinal);
        Assert.DoesNotContain("withheld", absent, StringComparison.Ordinal);
    }

    /// <summary>
    /// The other four members render in full, including the driver's message text (C-B).
    /// </summary>
    /// <remarks>
    /// WITHHOLDING MORE WOULD PROTECT NOTHING AND COST SOMETHING. The outward projection copies the
    /// provider code, the message, the buffer and the row through untouched, so those four are already
    /// published on the wire; hiding them locally would make the local diagnostic strictly less useful
    /// than the payload it exists to help interpret. That includes the one Chinese diagnostic the legacy
    /// synthesizes [n_cst_thread_task_sqlupdate.sru:L190], which is asserted by name.
    /// </remarks>
    [Fact]
    public void ToString_RendersTheOtherFourMembersInFull()
    {
        string rendered = new DbErrorData
        {
            SqlDbCode = -28,
            SqlErrText = DbErrorMessages.NoUpdatableTable,
            SqlSyntax = "SELECT 1",
            Buffer = DwBuffer.Filter,
            Row = 42,
        }.ToString();

        Assert.Contains("-28", rendered, StringComparison.Ordinal);
        Assert.Contains(DbErrorMessages.NoUpdatableTable, rendered, StringComparison.Ordinal);
        Assert.Contains("Filter", rendered, StringComparison.Ordinal);
        Assert.Contains("42", rendered, StringComparison.Ordinal);
    }

    /// <summary>
    /// Serializing the payload cannot emit the statement, even from inside this assembly.
    /// </summary>
    /// <remarks>
    /// THE THIRD DOOR. Internal visibility stops an outside consumer, but every member had a public
    /// getter, so any serializer reached inside this assembly wrote the statement out - a leak reached
    /// through a different default rather than a different intention. <c>[JsonIgnore]</c> on the member
    /// closes it. The other four members still serialize, so the attribute is scoped to the one value
    /// that must not travel unmasked rather than disabling serialization wholesale.
    /// </remarks>
    [Fact]
    public void SerializingThePayloadCannotEmitTheStatement()
    {
        const string statement = "UPDATE COMPANY SET NAME = 'Alice' WHERE ID = 7";

        string json = JsonSerializer.Serialize(
            DbErrorData.FromStatement(-1, "synthetic", statement, DwBuffer.Delete, 3));

        Assert.DoesNotContain("Alice", json, StringComparison.Ordinal);
        Assert.DoesNotContain("SqlSyntax", json, StringComparison.Ordinal);

        // The rest of the payload is unaffected: this is a scoped exclusion, not a blanket one.
        Assert.Contains("SqlErrText", json, StringComparison.Ordinal);
        Assert.Contains("synthetic", json, StringComparison.Ordinal);
    }

    /// <summary>
    /// The sanctioned door stays open and still masks: containment did not narrow the wire path.
    /// </summary>
    [Fact]
    public void TheSanctionedOutwardPathStillWorksAndStillMasks()
    {
        DbError wire = DbErrorData
            .FromStatement(-1, "synthetic", "UPDATE COMPANY SET NAME = 'Alice'", DwBuffer.Primary, 1)
            .ToDbError();

        Assert.DoesNotContain("Alice", wire.Sqlsyntax, StringComparison.Ordinal);
        Assert.Contains(SqlRedactor.DefaultPlaceholder, wire.Sqlsyntax, StringComparison.Ordinal);
        Assert.Equal("synthetic", wire.Sqlerrtext);
    }

    /// <summary>
    /// Mirrors the shape of <c>of_gettransobject(ref …, ref dberrordata)</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru:L148</c>] and the partial
    /// population at <c>:L175-L176</c>.
    /// </summary>
    /// <param name="dbErrData">The payload the failure populates, passed by reference as the oracle does.</param>
    /// <param name="code">The provider code to record.</param>
    /// <param name="text">The provider message text to record.</param>
    /// <returns>
    /// The numeric value of <c>RetCode.E_INVALID_TRANSACTION</c>, mirroring <c>:L177</c> without
    /// restating the preserved constant spelling in this file.
    /// </returns>
    private static long PopulateOnConnectFailure(ref DbErrorData dbErrData, long code, string text)
    {
        dbErrData = dbErrData with { SqlDbCode = code, SqlErrText = text };
        return -7;
    }
}
