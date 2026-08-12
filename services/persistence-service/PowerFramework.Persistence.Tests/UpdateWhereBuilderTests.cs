// ==============================================================================================
//  UpdateWhereBuilderTests.cs - THE updatewhereclause PARITY MATRICES
//  --------------------------------------------------------------------------------------------
//  UNDER TEST     services/persistence-service/PowerFramework.Persistence/Concurrency/
//                     UpdateWhereBuilder.cs
//  ORACLE         ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru      (READ ONLY)
//                     :L10-L17   the six-field `tabledata` structure, in declaration order
//                     :L82-L96   of_addupdatabletable and its three validation arms
//                     :L98-L170  _of_updateprepare - the seven script steps, both failure arms and
//                                the self-assignment workaround
//                 ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlupdate.sru   (READ ONLY)
//                     :L227-L234 the four-argument overload that NULLS both optional settings
//                 ws_objects/pfw.utility.sqlite.pbl.src/sqlitegetitemdouble.srf          (READ ONLY)
//                     :L7-L8, L11, L14  the FOUR-ARGUMENT accessor and its `org` flag
//                 ws_objects/pfw.shared.pbl.src/retcode.sru                              (READ ONLY)
//                     :L46, L51, L70    E_INVALID_ARGUMENT, E_INVALID_SQL, E_INTERNAL_ERROR
//  FIXTURE        ws_objects/pfw.tests.pbl.src/dw_sqlite.srd                             (READ ONLY)
//                     :L8-L13    six columns, every one update=yes updatewhereclause=yes
//                     :L14       updatewhere=1 updatekeyinplace=no
//
//  C-C - THE ORACLE IS READ ONLY AND IS CONSUMED THROUGH ONE TRANSCRIPTION. Every path above is the
//  behavioural oracle: never edited, never reformatted, never moved, and never copied into the .NET
//  tree. This file cites them by line locator and takes its DATA from DwSqliteFixture.cs, which is
//  this project's single transcription of dw_sqlite.srd. A second transcription here would be a second
//  thing to keep true, and the drift would surface as a passing test asserting the wrong contract - so
//  the column names, the table name, the key and identity column, the column count, the two settings,
//  the descriptor builders, the describe answers, the sample rows and the expected modification script
//  are all consumed. Two literals are retained DELIBERATELY, each labelled where it appears, as the
//  only pins on that transcription: the seven-step script written out long hand, and the six column
//  names. A derivation can be self-consistently wrong; those two are what stop it.
//
//  WHY THESE ARE BYTE-EXACT ASSERTIONS. The modification script is machine-read by the carrier, so
//  every space, quote, letter case and line feed in it is contract. These tests therefore assert the
//  WHOLE expected string rather than substrings: a substring assertion would pass while a stray
//  carriage return, a reordered reset triple or a missing trailing-newline suppression silently
//  changed every byte in the parity recordings.
//
//  WHAT IS COVERED HERE, IN THE ORDER THE REGIONS APPEAR. The byte-exact script and its long-hand pin;
//  the presence matrix, where ABSENT emits no line and false is not absence; the identity column, where
//  empty is legal; failure arm 1, a key column whose described id is not positive; failure arm 2, a
//  non-empty modify result; the add-time validation arms and the ordered multi-table collection; the
//  updatekeyinplace=no key-change workaround with its runtime-described gate; the multi-table loop and
//  its `!= OK` break; the R9 one-based regression guard; the marked-column helper; THE updatewhere=1
//  PAYLOAD RULE, where both halves of every marked column must travel; the descriptor's shape and
//  argument guards; and the identifier grammar this boundary adds and the legacy does not have.
//
//  NO DATABASE AND NO DataWindow IS INVOLVED ANYWHERE IN THIS FILE (C-E). The describe and modify
//  surfaces are the two injected interfaces, faked below; the carrier is Buffers/DataWindowBufferStore,
//  which is an in-memory three-buffer model. No SQLite file, no connection, no dialect client, and no
//  SQL statement is generated anywhere. That is what makes the per-service coverage gate C-H requires
//  reachable for the whole of Concurrency/UpdateWhereBuilder.cs.
//
//  THE FIXTURE IS TEST DATA, NOT PRODUCTION DATA. COMPANY and its six columns appear here because
//  dw_sqlite.srd is the sole updatable DataWindow in the repository; the production code under test
//  names no table, no column and no column count.
//
//  AAP 0.8.5 - NO ASSERTION IN THIS FILE IS A PERFORMANCE STATEMENT. The repository publishes no SLA,
//  no latency budget and no throughput target, so none is asserted, implied or used to justify a
//  choice. The sample row set is six rows because six makes each behaviour reachable exactly once, not
//  because six sizes a workload.
//
//  C-F SELF-AUDIT: no key, credential, token, password, connection string or secret-shaped placeholder
//  appears anywhere in this file - in any descriptor, sample row, message, literal or comment.
//
//  RULES POSITION: review_rules returns exactly one line, "No user rules provided.", so no
//  user-specified rule governs this file and none is invented here. The enterprise-standard baseline
//  applies in their place; the binding non-rule constraints bearing on this file are cited inline where
//  each is discharged - C-B, C-C, C-E, C-F, C-H, C-K, AAP 0.8.5 and risk R9.
// ==============================================================================================

using System.Globalization;
using PowerFramework.Persistence.Concurrency;

namespace PowerFramework.Persistence.Tests;

/// <summary>
/// A fake carrier surface implementing both injected interfaces, recording every interaction so the
/// tests can assert not just the answer but WHICH questions were asked and in what order.
/// </summary>
internal sealed class FakeUpdateTarget : IUpdateTargetMetadata, IUpdateTargetModifier
{
    /// <summary>The answer to the column-count describe. Zero models a describe that failed.</summary>
    internal int ColumnCount { get; set; }

    /// <summary>
    /// Resolutions keyed by the FULL describe property - <c>salary.Id</c>, not <c>salary</c> - because
    /// the caller composes the property. An absent key resolves to zero, which is what PowerScript's
    /// <c>Long()</c> answers for the <c>"!"</c> or <c>"?"</c> a failed describe returns.
    /// </summary>
    internal Dictionary<string, int> ColumnIds { get; } = new(StringComparer.Ordinal);

    /// <summary>The raw answer to the key-in-place describe.</summary>
    internal string KeyInPlaceAnswer { get; set; } = "yes";

    /// <summary>The modify result. EMPTY MEANS SUCCESS.</summary>
    internal string ModifyResult { get; set; } = string.Empty;

    /// <summary>Every column-id property asked for, in order - the evidence that a build stopped.</summary>
    internal List<string> ColumnIdRequests { get; } = [];

    /// <summary>Every script applied, in order.</summary>
    internal List<string> AppliedScripts { get; } = [];

    /// <summary>How many times the key-in-place describe was read.</summary>
    internal int KeyInPlaceReads { get; private set; }

    public int GetColumnCount() => ColumnCount;

    public int GetColumnId(string columnIdProperty)
    {
        ColumnIdRequests.Add(columnIdProperty);

        return ColumnIds.TryGetValue(columnIdProperty, out int id) ? id : 0;
    }

    public string DescribeUpdateKeyInPlace()
    {
        KeyInPlaceReads++;

        return KeyInPlaceAnswer;
    }

    public string Modify(string modificationScript)
    {
        AppliedScripts.Add(modificationScript);

        return ModifyResult;
    }
}

/// <summary>
/// Byte-exact script parity, the presence matrix, both failure arms, the add-time validation arms, the
/// key-change workaround, the multi-table loop and the R9 one-based regression guard.
/// </summary>
public sealed class UpdateWhereBuilderTests
{
    #region The sole evidenced fixture - CONSUMED from DwSqliteFixture, never re-transcribed

    // ------------------------------------------------------------------------------------------
    //  WHY EVERY FIXTURE FACT BELOW IS A PROJECTION OF DwSqliteFixture AND NOT A LITERAL (C-C).
    //
    //  ws_objects/pfw.tests.pbl.src/dw_sqlite.srd is the read-only behavioural oracle and the SOLE
    //  updatable DataWindow in the repository, and DwSqliteFixture.cs is this project's ONE
    //  transcription of it. A second transcription here would be a second thing to keep true: the
    //  two could drift, and the drift would surface as a passing test asserting the wrong contract.
    //  So the six column names, the table name, the key and identity column, the column count, the
    //  two settings, the descriptor builders, the describe answers and the expected modification
    //  script are all CONSUMED. The aliases below exist only so the assertions read as prose; each
    //  one is a projection with no data of its own.
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// The six column names in declaration order, whose ids are therefore 1..6
    /// [<c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L8-L13, L21-L26</c>].
    /// </summary>
    private static IReadOnlyList<string> FixtureColumns => DwSqliteFixture.ColumnNames;

    /// <summary>The update table name [<c>dw_sqlite.srd:L14</c>].</summary>
    private const string FixtureTable = DwSqliteFixture.UpdateTableName;

    /// <summary>
    /// The only column carrying <c>key=yes identity=yes</c> [<c>dw_sqlite.srd:L8</c>].
    /// </summary>
    private const string FixtureKeyColumn = DwSqliteFixture.KeyColumnName;

    /// <summary>
    /// A target preloaded with the fixture's six columns and their one-based ids, and answering
    /// <c>"no"</c> to the key-in-place describe exactly as the fixture's static definition does
    /// [<c>dw_sqlite.srd:L14</c>].
    /// </summary>
    /// <returns>A fresh recording double per call, so one test's requests cannot leak into another.</returns>
    /// <remarks>
    /// <para>
    /// WHY THIS IS A LOCAL RECORDING DOUBLE RATHER THAN <see cref="DwSqliteTargetMetadata"/>. The
    /// fixture's own seam ANSWERS and deliberately DOES NOT RECORD, and several tests here assert
    /// not the answer but WHICH describe properties were asked for and in what order - the evidence
    /// that a build stopped at the offending column [<c>n_cst_thread_task_sqlupdate.sru:L121</c>], and
    /// that an unsafe name never reached describe at all. So the RECORDING behaviour is local while
    /// every ANSWER it gives is consumed from the fixture.
    /// </para>
    /// <para>
    /// THE KEY-IN-PLACE ANSWER IS THE FIXTURE'S OWN SETTING, projected through the same constant the
    /// script generator emits. <c>updatekeyinplace=no</c> [<c>dw_sqlite.srd:L14</c>] is therefore what
    /// the runtime describe answers, which is what puts the key-change refresh of
    /// <c>:L155-L167</c> ON THE MAINLINE for this fixture rather than in a corner.
    /// </para>
    /// </remarks>
    private static FakeUpdateTarget FixtureTarget()
    {
        FakeUpdateTarget target = new()
        {
            ColumnCount = DwSqliteFixture.ColumnCount,
            KeyInPlaceAnswer = DwSqliteFixture.UpdateKeyInPlace
                ? UpdateWhereBuilder.YesLiteral
                : UpdateWhereBuilder.NoLiteral,
        };

        // The describe answers, keyed by the exact composed property the production code requests.
        // R9: the ids are ONE-BASED and are the .srd's own `id=` attributes, never rebased here.
        foreach (KeyValuePair<string, int> answer in DwSqliteFixture.ColumnIdDescribeAnswers)
        {
            target.ColumnIds[answer.Key] = answer.Value;
        }

        return target;
    }

    /// <summary>
    /// The fixture's descriptor: all six columns updatable, <c>id</c> the sole key and the identity
    /// column, <c>updatewhere=1</c> and <c>updatekeyinplace=no</c> [<c>dw_sqlite.srd:L8-L14</c>].
    /// </summary>
    /// <returns>A fresh descriptor per call, consumed from the fixture's own builder.</returns>
    private static UpdatableTableDescriptor FixtureDescriptor() => DwSqliteFixture.Descriptor();

    /// <summary>
    /// Joins lines with the contract separator and NO trailing separator, which is the shape step 7
    /// produces [<c>n_cst_thread_task_sqlupdate.sru:L143</c>].
    /// </summary>
    /// <param name="lines">The script's lines, in order.</param>
    /// <returns>The assembled script.</returns>
    /// <remarks>
    /// THE SEPARATOR IS CONSUMED FROM THE PRODUCTION CONSTANT, which is a bare line feed:
    /// PowerScript's <c>~n</c> is U+000A alone [<c>:L105-L139</c>]. Writing
    /// <see cref="Environment.NewLine"/> here would pass on Linux and fail on Windows, which is the
    /// worst available failure mode for a byte-exact comparison.
    /// </remarks>
    private static string Script(params string[] lines)
    {
        return string.Join(UpdateWhereBuilder.LineSeparator, lines);
    }

    #endregion

    #region Byte-exact script parity

    [Fact]
    public void BuildModificationString_OverTheFixture_ProducesTheScriptByteForByte()
    {
        ModificationScriptResult result =
            UpdateWhereBuilder.BuildModificationString(FixtureDescriptor(), FixtureTarget());

        // CONSUMED, NOT RE-TYPED. The expectation is the fixture's own derivation from its
        // transcription of dw_sqlite.srd, so this assertion and the fixture cannot drift apart. The
        // sibling test below is the single place the seven steps are written out long hand, which is
        // what stops that derivation from being merely self-consistent.
        Assert.Equal(DwSqliteFixture.ExpectedModificationScript, result.Script);

        Assert.True(result.IsSucceeded);
        Assert.Equal(RetCode.OK, result.Code);
        Assert.Empty(result.ErrorText);

        // The resolved key-column ids travel on the result, because the key-change refresh gate at
        // :L156-L157 is `UpperBound(nKeyColumns) > 0` over exactly this list.
        Assert.Equal([DwSqliteFixture.IdColumnNumber], result.KeyColumnIds);
    }

    [Fact]
    public void TheFixturesDerivedScript_EqualsTheSevenStepsWrittenOutLongHand()
    {
        // WHY THIS TEST EXISTS AT ALL. DwSqliteFixture DERIVES its expected script from the
        // transcribed column table rather than storing a literal, which is what keeps it from
        // drifting from the transcription - but a derivation can be self-consistently wrong. This is
        // therefore the ONE place in this file where :L103-L143 is written out character by
        // character, pinning the derivation against an independent literal. It is a pin, not a second
        // transcription: every other assertion in this file consumes the fixture.
        string expected = Script(
            // STEP 1 - the reset triple per one-based ordinal, in Update/Key/Identity order [:L103-L108].
            "#1.Update = no", "#1.Key = no", "#1.Identity = no",
            "#2.Update = no", "#2.Key = no", "#2.Identity = no",
            "#3.Update = no", "#3.Key = no", "#3.Identity = no",
            "#4.Update = no", "#4.Key = no", "#4.Identity = no",
            "#5.Update = no", "#5.Key = no", "#5.Identity = no",
            "#6.Update = no", "#6.Key = no", "#6.Identity = no",
            // STEP 2 - updatable columns, by name, in descriptor order [:L111-L114].
            "id.Update = yes",
            "name.Update = yes",
            "age.Update = yes",
            "address.Update = yes",
            "salary.Update = yes",
            "birth.Update = yes",
            // STEP 3 - key columns [:L116-L125].
            "id.Key = yes",
            // STEP 4 - the identity column [:L127-L129].
            "id.Identity = yes",
            // STEP 5 - SINGLE-QUOTED although the value is a number [:L131-L133].
            "DataWindow.Table.UpdateWhere = '1'",
            // STEP 6 - LOWER-CASE i in "Keyin", and false emits `no` [:L135-L141].
            "DataWindow.Table.UpdateKeyinPlace = no",
            // STEP 7 - always emitted, single-quoted, NO trailing newline [:L143].
            "DataWindow.Table.UpdateTable = 'COMPANY'");

        Assert.Equal(expected, DwSqliteFixture.ExpectedModificationScript);

        // EIGHTEEN RESET LINES, counted rather than assumed: three per column across six columns
        // [:L103-L108]. A port that reset only the columns it was about to re-enable would emit
        // fewer and leave stale flags from a previous table's prepare.
        Assert.Equal(
            3 * DwSqliteFixture.ColumnCount,
            DwSqliteFixture.ExpectedModificationScriptLines
                .Count(line => line.StartsWith(UpdateWhereBuilder.ColumnOrdinalPrefix, StringComparison.Ordinal)));
    }

    [Fact]
    public void BuildModificationString_UsesLineFeedOnly_AndNeverACarriageReturn()
    {
        ModificationScriptResult result =
            UpdateWhereBuilder.BuildModificationString(FixtureDescriptor(), FixtureTarget());

        Assert.DoesNotContain('\r', result.Script);
        Assert.Equal("\n", UpdateWhereBuilder.LineSeparator);

        // Every separator is exactly one line feed, so the line count is one more than the separators.
        Assert.Equal(
            result.Script.Split('\n').Length - 1,
            result.Script.Count(character => character == '\n'));
    }

    [Fact]
    public void BuildModificationString_DoesNotEndWithANewline_BecauseStep7SuppressesIt()
    {
        ModificationScriptResult result =
            UpdateWhereBuilder.BuildModificationString(FixtureDescriptor(), FixtureTarget());

        Assert.DoesNotMatch("[\r\n]$", result.Script);
        Assert.EndsWith("DataWindow.Table.UpdateTable = 'COMPANY'", result.Script, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildModificationString_EmitsTheUpdateTableUnconditionally_ForEveryNameItAdmits()
    {
        // :L143 IS UNCONDITIONAL, and this is what "unconditional" now means: the line is emitted with
        // no regard to whether any OTHER field was stated - no updatable-column count, no key-column
        // count, no identity column, neither optional setting, and a column count of zero so the reset
        // pass emits nothing at all. The only thing that can stop it is the name itself failing the
        // identifier grammar, which the sibling test below covers.
        UpdatableTableDescriptor descriptor = UpdatableTableDescriptor.Create(
            "COMPANY",
            ["a"],
            ["a"],
            string.Empty,
            updateWhere: null,
            updateKeyInPlace: null);

        FakeUpdateTarget target = new() { ColumnCount = 0 };
        target.ColumnIds["a" + UpdateWhereBuilder.ColumnIdSuffix] = 1;

        ModificationScriptResult result =
            UpdateWhereBuilder.BuildModificationString(descriptor, target);

        Assert.True(result.IsSucceeded);
        Assert.EndsWith("DataWindow.Table.UpdateTable = 'COMPANY'", result.Script, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildModificationString_RefusesAnEmptyUpdateTableRatherThanEmittingAnEmptyQuotedName()
    {
        // THE NARROWED ARM, AND WHY THE NARROWING IS THE POINT. This case previously succeeded and
        // emitted `DataWindow.Table.UpdateTable = ''`, which the legacy would also have emitted - the
        // builder trusted add-time validation to have rejected an empty name first. Across a network
        // boundary that trust is misplaced: persistence.v1.TableUpdateContract carries the name from a
        // remote caller, and a descriptor can also be built through Create or a `with` expression
        // without passing the admission boundary at all. So the builder is fail-closed and refuses.
        UpdatableTableDescriptor descriptor = UpdatableTableDescriptor.Create(
            string.Empty,
            ["a"],
            ["a"],
            string.Empty,
            updateWhere: null,
            updateKeyInPlace: null);

        FakeUpdateTarget target = new() { ColumnCount = 0 };
        target.ColumnIds["a" + UpdateWhereBuilder.ColumnIdSuffix] = 1;

        ModificationScriptResult result =
            UpdateWhereBuilder.BuildModificationString(descriptor, target);

        Assert.False(result.IsSucceeded);
        Assert.Equal(RetCode.E_INTERNAL_ERROR, result.Code);
        Assert.Equal(UpdateWhereBuilder.UnsafeUpdateTableMessage, result.ErrorText);

        // AND THE DIAGNOSTIC CARRIES NO SCRIPT FRAGMENT. The offending text is the caller's own, so
        // echoing it into a message that may be logged would put caller-chosen script in the log.
        Assert.DoesNotContain("DataWindow", result.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildModificationString_EmitsTheKeyColumnsInDescriptorOrder()
    {
        UpdatableTableDescriptor descriptor = UpdatableTableDescriptor.Create(
            FixtureTable,
            FixtureColumns,
            ["salary", "id", "birth"],
            identityColumn: string.Empty,
            updateWhere: null,
            updateKeyInPlace: null);

        ModificationScriptResult result =
            UpdateWhereBuilder.BuildModificationString(descriptor, FixtureTarget());

        Assert.True(result.IsSucceeded);
        Assert.Contains(
            Script("salary.Key = yes", "id.Key = yes", "birth.Key = yes"),
            result.Script,
            StringComparison.Ordinal);

        // The resolved ids come back in the SAME order, because :L162 walks them positionally.
        Assert.Equal([5, 1, 6], result.KeyColumnIds);
    }

    #endregion

    #region The presence matrix - absent emits NO line, and false is not absent

    public static TheoryData<long?, bool?, string[]> PresenceMatrix =>
        new()
        {
            // Both present. false emits `= no`, proving false is not conflated with absence.
            {
                1L,
                false,
                new[] { "DataWindow.Table.UpdateWhere = '1'", "DataWindow.Table.UpdateKeyinPlace = no" }
            },
            // Both present, key-in-place true.
            {
                0L,
                true,
                new[] { "DataWindow.Table.UpdateWhere = '0'", "DataWindow.Table.UpdateKeyinPlace = yes" }
            },
            // Update-where present, key-in-place ABSENT - no key-in-place line at all.
            { 2L, null, new[] { "DataWindow.Table.UpdateWhere = '2'" } },
            // Update-where ABSENT, key-in-place present.
            { null, false, new[] { "DataWindow.Table.UpdateKeyinPlace = no" } },
            // Both ABSENT - neither line appears.
            { null, null, Array.Empty<string>() },
        };

    [Theory]
    [MemberData(nameof(PresenceMatrix))]
    public void BuildModificationString_EmitsASettingLineOnlyWhenThatSettingIsPresent(
        long? updateWhere,
        bool? updateKeyInPlace,
        string[] expectedSettingLines)
    {
        UpdatableTableDescriptor descriptor = UpdatableTableDescriptor.Create(
            FixtureTable,
            FixtureColumns,
            [FixtureKeyColumn],
            identityColumn: string.Empty,
            updateWhere,
            updateKeyInPlace);

        ModificationScriptResult result =
            UpdateWhereBuilder.BuildModificationString(descriptor, FixtureTarget());

        string[] settingLines = [.. result.Script
            .Split(UpdateWhereBuilder.LineSeparator)
            .Where(line =>
                line.StartsWith(UpdateWhereBuilder.UpdateWhereProperty, StringComparison.Ordinal)
                || line.StartsWith(UpdateWhereBuilder.UpdateKeyInPlaceProperty, StringComparison.Ordinal))];

        Assert.Equal(expectedSettingLines, settingLines);
    }

    [Fact]
    public void BuildModificationString_AbsentUpdateWhere_EmitsNoUpdateWhereLineWhatsoever()
    {
        UpdatableTableDescriptor descriptor = UpdatableTableDescriptor.Create(
            FixtureTable,
            FixtureColumns,
            [FixtureKeyColumn],
            identityColumn: string.Empty,
            updateWhere: null,
            updateKeyInPlace: null);

        ModificationScriptResult result =
            UpdateWhereBuilder.BuildModificationString(descriptor, FixtureTarget());

        // Not "= '0'", not "= ''" - the substring must be wholly absent. A sentinel port would have
        // emitted a line here, which is the specific failure DECISION 2 guards against.
        Assert.DoesNotContain(
            UpdateWhereBuilder.UpdateWhereProperty,
            result.Script,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            UpdateWhereBuilder.UpdateKeyInPlaceProperty,
            result.Script,
            StringComparison.Ordinal);
    }

    [Fact]
    public void UpdateKeyInPlaceProperty_KeepsTheOraclesLowerCaseSpelling()
    {
        // :L137, :L139 and :L155 all spell it with a lower-case i in "in". "Correcting" it would
        // address a property that does not exist.
        Assert.Equal("DataWindow.Table.UpdateKeyinPlace", UpdateWhereBuilder.UpdateKeyInPlaceProperty);
        Assert.DoesNotContain(
            "UpdateKeyInPlace",
            UpdateWhereBuilder.UpdateKeyInPlaceProperty,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1L, "DataWindow.Table.UpdateWhere = '1'")]
    [InlineData(0L, "DataWindow.Table.UpdateWhere = '0'")]
    [InlineData(-7L, "DataWindow.Table.UpdateWhere = '-7'")]
    [InlineData(long.MaxValue, "DataWindow.Table.UpdateWhere = '9223372036854775807'")]
    public void BuildModificationString_SingleQuotesTheUpdateWhereValue_InvariantlyFormatted(
        long updateWhere,
        string expectedLine)
    {
        UpdatableTableDescriptor descriptor = UpdatableTableDescriptor.Create(
            FixtureTable,
            FixtureColumns,
            [FixtureKeyColumn],
            identityColumn: string.Empty,
            updateWhere,
            updateKeyInPlace: null);

        ModificationScriptResult result =
            UpdateWhereBuilder.BuildModificationString(descriptor, FixtureTarget());

        Assert.Contains(expectedLine, result.Script, StringComparison.Ordinal);
    }

    #endregion

    #region The identity column - empty is legal and emits nothing

    [Fact]
    public void BuildModificationString_EmptyIdentityColumn_EmitsNoIdentityYesLine()
    {
        UpdatableTableDescriptor descriptor = UpdatableTableDescriptor.Create(
            FixtureTable,
            FixtureColumns,
            [FixtureKeyColumn],
            identityColumn: string.Empty,
            updateWhere: null,
            updateKeyInPlace: null);

        ModificationScriptResult result =
            UpdateWhereBuilder.BuildModificationString(descriptor, FixtureTarget());

        Assert.True(result.IsSucceeded);
        Assert.DoesNotContain(".Identity = yes", result.Script, StringComparison.Ordinal);

        // The reset pass still turned Identity off on all six, so the attribute IS mentioned - just
        // never affirmatively. This is what distinguishes "no identity column" from "no reset pass".
        Assert.Equal(6, CountOccurrences(result.Script, ".Identity = no"));
    }

    [Fact]
    public void BuildModificationString_WhitespaceIdentityColumn_IsPresentToTheOraclesTestAndRefusedByTheGrammar()
    {
        // BOTH HALVES IN ONE TEST, BECAUSE THE INTERESTING FACT IS THAT THEY DISAGREE. :L127 is the
        // exact comparison `<> ""`, so a single space counts as PRESENT - and HasIdentityColumn is
        // asserted here to prove that transcription is untouched. A whitespace-aware presence test
        // would have skipped the column entirely and diverged from the oracle.
        UpdatableTableDescriptor descriptor = UpdatableTableDescriptor.Create(
            FixtureTable,
            FixtureColumns,
            [FixtureKeyColumn],
            identityColumn: " ",
            updateWhere: null,
            updateKeyInPlace: null);

        Assert.True(descriptor.HasIdentityColumn);

        // Being PRESENT is what sends it to the grammar, and the grammar refuses it: a space is not an
        // identifier, so ` .Identity = yes` - a line naming no column at all - is never emitted.
        ModificationScriptResult result =
            UpdateWhereBuilder.BuildModificationString(descriptor, FixtureTarget());

        Assert.False(result.IsSucceeded);
        Assert.Equal(RetCode.E_INTERNAL_ERROR, result.Code);
        Assert.Equal(UpdateWhereBuilder.InvalidColumnNameMessage + " ", result.ErrorText);
        Assert.DoesNotContain(".Identity = yes", result.Script, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildModificationString_AnAbsentIdentityColumnNeverReachesTheGrammar()
    {
        // THE ORDERING THAT MATTERS: the grammar sits INSIDE the presence test, so an empty identity
        // column - which is legal and means "this table has no auto-increment column" [:L127] - is not
        // handed to a check that would refuse it for being empty. Getting this backwards would reject
        // every table without an identity column, which is most of them.
        UpdatableTableDescriptor descriptor = UpdatableTableDescriptor.Create(
            FixtureTable,
            FixtureColumns,
            [FixtureKeyColumn],
            identityColumn: string.Empty,
            updateWhere: null,
            updateKeyInPlace: null);

        Assert.False(descriptor.HasIdentityColumn);

        ModificationScriptResult result =
            UpdateWhereBuilder.BuildModificationString(descriptor, FixtureTarget());

        Assert.True(result.IsSucceeded);
        Assert.DoesNotContain(".Identity = yes", result.Script, StringComparison.Ordinal);
    }

    #endregion

    #region Failure arm 1 - a key column whose described id is not positive

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void BuildModificationString_NonPositiveColumnId_FailsWithTheVerbatimDiagnostic(int resolvedId)
    {
        FakeUpdateTarget target = FixtureTarget();
        target.ColumnIds["salary" + UpdateWhereBuilder.ColumnIdSuffix] = resolvedId;

        UpdatableTableDescriptor descriptor = UpdatableTableDescriptor.Create(
            FixtureTable,
            FixtureColumns,
            ["id", "salary", "birth"],
            identityColumn: string.Empty,
            updateWhere: 1L,
            updateKeyInPlace: false);

        ModificationScriptResult result =
            UpdateWhereBuilder.BuildModificationString(descriptor, target);

        Assert.False(result.IsSucceeded);
        Assert.Equal(RetCode.E_INTERNAL_ERROR, result.Code);

        // NOT E_INVALID_SQL AND NOT E_INVALID_ARGUMENT. The oracle answers E_INTERNAL_ERROR for an
        // unresolvable column name [:L120-L121], which reads oddly for what is really bad caller
        // input - and is reproduced exactly, because the code travels to a caller that branches on it.
        Assert.NotEqual(RetCode.E_INVALID_SQL, result.Code);
        Assert.NotEqual(RetCode.E_INVALID_ARGUMENT, result.Code);

        // Exact string: the prefix, no space after the colon, then the offending column name. Asserted
        // BOTH against the literal and against the application's companion constant, so there is
        // exactly one spelling of it in the system and this test would catch a drift in either.
        string offendingColumn = DwSqliteFixture.ColumnAt(DwSqliteFixture.SalaryColumnNumber).Name;

        Assert.Equal("无效的列名:salary", result.ErrorText);
        Assert.Equal("无效的列名:", UpdateWhereBuilder.InvalidColumnNameMessage);
        Assert.Equal(
            UpdateWhereBuilder.InvalidColumnNameMessage + offendingColumn,
            result.ErrorText);
        Assert.StartsWith(UpdateWhereBuilder.InvalidColumnNameMessage, result.ErrorText, StringComparison.Ordinal);
        Assert.EndsWith(offendingColumn, result.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildModificationString_NonPositiveColumnId_StopsBuildingAtThatColumn()
    {
        FakeUpdateTarget target = FixtureTarget();
        target.ColumnIds["salary" + UpdateWhereBuilder.ColumnIdSuffix] = 0;

        UpdatableTableDescriptor descriptor = UpdatableTableDescriptor.Create(
            FixtureTable,
            FixtureColumns,
            ["id", "salary", "birth"],
            identityColumn: string.Empty,
            updateWhere: 1L,
            updateKeyInPlace: false);

        ModificationScriptResult result =
            UpdateWhereBuilder.BuildModificationString(descriptor, target);

        // `birth.Id` was NEVER asked for: the oracle returns immediately at :L121.
        Assert.Equal(["id.Id", "salary.Id"], target.ColumnIdRequests);

        // And the partially built script is discarded, exactly as sModString goes out of scope.
        Assert.Empty(result.Script);
        Assert.Empty(result.KeyColumnIds);
    }

    #endregion

    #region Failure arm 2 - a non-empty modify result

    [Fact]
    public void Prepare_NonEmptyModifyResult_FailsCarryingThatTextUnmodified()
    {
        const string driverText = "Attribute UpdateWhere not found.";

        FakeUpdateTarget target = FixtureTarget();
        target.ModifyResult = driverText;

        List<(long Code, string Text)> raised = [];
        UpdatePreparer preparer = new(target, target, (code, text) => raised.Add((code, text)));

        UpdatePreparationResult result = preparer.Prepare(FixtureDescriptor(), new DataWindowBufferStore());

        Assert.False(result.IsSucceeded);
        Assert.Equal(RetCode.E_INTERNAL_ERROR, result.Code);
        Assert.Equal(driverText, result.ErrorText);

        // THE CODE IS E_INTERNAL_ERROR AND DELIBERATELY NOT E_INVALID_SQL, asserted from both sides
        // because the two are easy to confuse and the confusion would be invisible. The oracle raises
        // `Event OnError(RetCode.E_INTERNAL_ERROR,sErr)` [:L147] even though the text came from the
        // carrier's own attribute parser - a failed Modify is a STRUCTURAL fault in the modification
        // script, not a malformed SQL statement, and this file emits no SQL at all.
        Assert.NotEqual(RetCode.E_INVALID_SQL, result.Code);
        Assert.NotEqual(RetCode.E_DB_ERROR, result.Code);
        Assert.NotEqual(RetCode.E_INVALID_ARGUMENT, result.Code);

        // Forwarded through the OnError-shaped sink with the same two arguments and no reformatting.
        Assert.Equal([(RetCode.E_INTERNAL_ERROR, driverText)], raised);

        // The workaround never runs, because the oracle returns before reaching :L155.
        Assert.False(result.KeyChangeRefreshRequired);
        Assert.Equal(0, target.KeyInPlaceReads);
    }

    [Fact]
    public void Prepare_EmptyModifyResult_MeansSuccess()
    {
        // The inverted convention, asserted explicitly: empty is NOT a failure.
        FakeUpdateTarget target = FixtureTarget();
        target.ModifyResult = string.Empty;

        UpdatePreparer preparer = new(target, target);

        UpdatePreparationResult result = preparer.Prepare(FixtureDescriptor(), new DataWindowBufferStore());

        Assert.True(result.IsSucceeded);
        Assert.Equal(RetCode.OK, result.Code);
        Assert.Empty(result.ErrorText);
        Assert.Single(target.AppliedScripts);
    }

    [Fact]
    public void Prepare_BuildFailure_RaisesOnErrorAndNeverAppliesAScript()
    {
        FakeUpdateTarget target = FixtureTarget();
        target.ColumnIds.Remove("id" + UpdateWhereBuilder.ColumnIdSuffix);

        List<(long Code, string Text)> raised = [];
        UpdatePreparer preparer = new(target, target, (code, text) => raised.Add((code, text)));

        UpdatePreparationResult result = preparer.Prepare(FixtureDescriptor(), new DataWindowBufferStore());

        Assert.Equal(RetCode.E_INTERNAL_ERROR, result.Code);
        Assert.Equal("无效的列名:id", result.ErrorText);
        Assert.Equal([(RetCode.E_INTERNAL_ERROR, "无效的列名:id")], raised);
        Assert.Empty(target.AppliedScripts);
    }

    [Fact]
    public void Prepare_WithoutAnErrorSink_StillReportsFailureThroughTheResult()
    {
        FakeUpdateTarget target = FixtureTarget();
        target.ModifyResult = "boom";

        UpdatePreparer preparer = new(target, target);

        UpdatePreparationResult result = preparer.Prepare(FixtureDescriptor(), new DataWindowBufferStore());

        Assert.Equal(RetCode.E_INTERNAL_ERROR, result.Code);
        Assert.Equal("boom", result.ErrorText);
    }

    #endregion

    #region Add-time validation - the three arms, and the identity column that is NOT an arm

    public static TheoryData<string, string[], string[]> RejectedDescriptors =>
        new()
        {
            // Arm 1 - empty name [:L84].
            { string.Empty, new[] { "id" }, new[] { "id" } },
            // Arm 2 - zero-length updatable columns [:L84].
            { FixtureTable, Array.Empty<string>(), new[] { "id" } },
            // Arm 3 - zero-length key columns [:L84].
            { FixtureTable, new[] { "id" }, Array.Empty<string>() },
        };

    [Theory]
    [MemberData(nameof(RejectedDescriptors))]
    public void AddUpdatableTable_AnyOfTheThreeArms_ReturnsInvalidArgumentAndStoresNothing(
        string name,
        string[] updatableColumns,
        string[] keyColumns)
    {
        UpdatableTableCollection tables = new();

        long code = tables.AddUpdatableTable(name, updatableColumns, keyColumns, "id", 1L, false);

        Assert.Equal(RetCode.E_INVALID_ARGUMENT, code);

        // Rejected BEFORE the append, so the array is untouched.
        Assert.Equal(OneBasedIndex.EmptyUpperBound, tables.UpperBound);
        Assert.Empty(tables.Descriptors);
    }

    [Fact]
    public void AddUpdatableTable_EmptyIdentityColumn_IsAcceptedBecauseItIsNotAValidationArm()
    {
        UpdatableTableCollection tables = new();

        long code = tables.AddUpdatableTable(
            FixtureTable,
            FixtureColumns,
            [FixtureKeyColumn],
            identityColumn: string.Empty,
            updateWhere: 1L,
            updateKeyInPlace: false);

        Assert.Equal(RetCode.OK, code);
        Assert.Equal(1, tables.UpperBound);
        Assert.False(tables.DescriptorAt(1).HasIdentityColumn);

        // And it emits no affirmative Identity line, which is the observable half of the same fact.
        ModificationScriptResult result =
            UpdateWhereBuilder.BuildModificationString(tables.DescriptorAt(1), FixtureTarget());

        Assert.DoesNotContain(".Identity = yes", result.Script, StringComparison.Ordinal);
    }

    [Fact]
    public void AddUpdatableTable_WhitespaceName_PassesTheOraclesArmAndIsThenRefusedByTheGrammar()
    {
        // THE ONE OBSERVABLE NARROWING IN THIS FILE, ASSERTED FROM BOTH SIDES SO NEITHER CAN DRIFT.
        // IsAcceptable is the oracle's `name = ""` arm and STILL ADMITS a single space - that
        // transcription is unchanged and is asserted directly. IsScriptSafe is the new boundary guard
        // and refuses it, because a table name of one space is not a table any database has and the
        // contract now carries the name from a remote caller.
        UpdatableTableDescriptor probe = UpdatableTableDescriptor.Create(
            " ",
            FixtureColumns,
            [FixtureKeyColumn],
            "id",
            updateWhere: null,
            updateKeyInPlace: null);

        Assert.True(probe.IsAcceptable);
        Assert.False(probe.IsScriptSafe);

        UpdatableTableCollection tables = new();

        long code = tables.AddUpdatableTable(" ", FixtureColumns, [FixtureKeyColumn], "id");

        // THE SAME CODE THE ORACLE'S OWN ARMS ANSWER, deliberately - a caller already handles it, and
        // the two refusals mean the same thing: this descriptor was not admitted.
        Assert.Equal(RetCode.E_INVALID_ARGUMENT, code);

        // AND NOTHING WAS STORED, so a refused descriptor cannot be reached through DescriptorAt.
        Assert.Equal(0, tables.UpperBound);
    }

    [Fact]
    public void AddUpdatableTable_FourArgumentOverload_NullsBothOptionalSettings()
    {
        UpdatableTableCollection tables = new();

        Assert.Equal(
            RetCode.OK,
            tables.AddUpdatableTable(FixtureTable, FixtureColumns, [FixtureKeyColumn], DwSqliteFixture.IdentityColumnName));

        UpdatableTableDescriptor descriptor = tables.DescriptorAt(1);

        // SetNull(nUpdateWhere) ; SetNull(bUpdateInPlace)
        // [n_cst_threading_task_sqlupdate.sru:L230-L231].
        Assert.Null(descriptor.UpdateWhere);
        Assert.Null(descriptor.UpdateKeyInPlace);
    }

    [Fact]
    public void AddUpdatableTable_AppendsInOrderAtUpperBoundPlusOne()
    {
        UpdatableTableCollection tables = new();

        tables.AddUpdatableTable("first", ["a"], ["a"], string.Empty);
        tables.AddUpdatableTable("second", ["a"], ["a"], string.Empty);
        tables.AddUpdatableTable("third", ["a"], ["a"], string.Empty);

        Assert.Equal(3, tables.UpperBound);

        // R9: one-based access, so index 1 is the FIRST descriptor and index 3 the last.
        Assert.Equal("first", tables.DescriptorAt(1).Name);
        Assert.Equal("second", tables.DescriptorAt(2).Name);
        Assert.Equal("third", tables.DescriptorAt(3).Name);
    }

    [Fact]
    public void AddUpdatableTable_TwoDescriptorsCoexistInOrder_WithTheMultiTableSwitchOn()
    {
        // MULTI-TABLE UPDATE FROM ONE DATAWINDOW IS A REAL LEGACY CAPABILITY, NOT A THEORETICAL ONE.
        // The oracle holds the descriptors in an ARRAY - `TABLEDATA Tables[]` [:L30] - appends at
        // UpperBound+1 [:L86], resets it wholesale [:L58, L67], and drives it with a switch of its own
        // [`_bMultiTableUpdate` :L33]. The contract therefore carries a REPEATED table-update
        // descriptor, so two descriptors coexisting in order is the shape that has to work.
        UpdatableTableCollection tables = new() { MultiTableUpdate = true };

        // The evidenced table first, then a second one, so the ordering assertion is not symmetric.
        Assert.Equal(
            RetCode.OK,
            tables.AddUpdatableTable(
                FixtureTable,
                FixtureColumns,
                DwSqliteFixture.KeyColumnNames,
                DwSqliteFixture.IdentityColumnName,
                DwSqliteFixture.UpdateWhereMode,
                DwSqliteFixture.UpdateKeyInPlace));

        Assert.Equal(
            RetCode.OK,
            tables.AddUpdatableTable("DEPARTMENT", ["dept_id", "dept_name"], ["dept_id"], "dept_id"));

        // THE FLAG IS SET AND STAYS SET - adding does not clear it, and only Reset turns it off.
        Assert.True(tables.MultiTableUpdate);

        // BOTH COEXIST, IN APPEND ORDER. R9: index 1 is the FIRST descriptor, not the second.
        Assert.Equal(2, tables.UpperBound);
        Assert.Equal(2, tables.Descriptors.Count);
        Assert.Equal(FixtureTable, tables.DescriptorAt(1).Name);
        Assert.Equal("DEPARTMENT", tables.DescriptorAt(2).Name);
        Assert.Equal([FixtureTable, "DEPARTMENT"], tables.Descriptors.Select(descriptor => descriptor.Name));

        // AND THEY KEEP THEIR OWN SETTINGS. The second used the four-argument overload, so both of its
        // optional fields are ABSENT while the first's are STATED - the two descriptors are
        // independent rather than sharing one prepare's worth of state.
        Assert.Equal(1L, tables.DescriptorAt(1).UpdateWhere);
        Assert.False(tables.DescriptorAt(1).UpdateKeyInPlace);
        Assert.Null(tables.DescriptorAt(2).UpdateWhere);
        Assert.Null(tables.DescriptorAt(2).UpdateKeyInPlace);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(4)]
    public void DescriptorAt_OutsideTheOneBasedRange_Throws(int oneBasedIndex)
    {
        UpdatableTableCollection tables = new();

        tables.AddUpdatableTable("first", ["a"], ["a"], string.Empty);
        tables.AddUpdatableTable("second", ["a"], ["a"], string.Empty);
        tables.AddUpdatableTable("third", ["a"], ["a"], string.Empty);

        // Index 0 is the classic double-rebasing symptom and must fail loudly rather than silently
        // answer the first descriptor.
        Assert.Throws<ArgumentOutOfRangeException>(() => tables.DescriptorAt(oneBasedIndex));
        Assert.False(tables.TryGetDescriptor(oneBasedIndex, out UpdatableTableDescriptor? descriptor));
        Assert.Null(descriptor);
    }

    [Fact]
    public void TryGetDescriptor_InsideTheRange_ReturnsTheDescriptor()
    {
        UpdatableTableCollection tables = new();

        tables.AddUpdatableTable("only", ["a"], ["a"], string.Empty);

        Assert.True(tables.TryGetDescriptor(1, out UpdatableTableDescriptor? descriptor));
        Assert.NotNull(descriptor);
        Assert.Equal("only", descriptor.Name);
    }

    [Fact]
    public void Reset_ClearsTheArrayAndTurnsTheMultiTableSwitchOff()
    {
        UpdatableTableCollection tables = new() { MultiTableUpdate = true };

        tables.AddUpdatableTable(FixtureTable, FixtureColumns, [FixtureKeyColumn], DwSqliteFixture.IdentityColumnName);

        Assert.Equal(RetCode.OK, tables.Reset());

        // `_bMultiTableUpdate = false` [:L63] and `Tables = emptyTables` [:L67].
        Assert.False(tables.MultiTableUpdate);
        Assert.Equal(OneBasedIndex.EmptyUpperBound, tables.UpperBound);
    }

    [Fact]
    public void MutationsAnswerBusy_WhenTheCallerSuppliedBusyGuardIsSet()
    {
        UpdatableTableCollection tables = new() { IsBusy = () => true };

        // `if #Running then return RetCode.E_BUSY` [:L60] and the proxy's of_IsBusy guards
        // [n_cst_threading_task_sqlupdate.sru:L207, L223, L236].
        Assert.Equal(
            RetCode.E_BUSY,
            tables.AddUpdatableTable(FixtureTable, FixtureColumns, [FixtureKeyColumn], DwSqliteFixture.IdentityColumnName, 1L, false));
        Assert.Equal(
            RetCode.E_BUSY,
            tables.AddUpdatableTable(FixtureTable, FixtureColumns, [FixtureKeyColumn], DwSqliteFixture.IdentityColumnName));
        Assert.Equal(RetCode.E_BUSY, tables.Reset());
        Assert.Equal(OneBasedIndex.EmptyUpperBound, tables.UpperBound);
    }

    [Fact]
    public void MutationsProceed_WhenNoBusyGuardWasSupplied()
    {
        // Unset means never busy, which is the correct default for a collection no task is driving.
        UpdatableTableCollection tables = new();

        Assert.Null(tables.IsBusy);
        Assert.Equal(RetCode.OK, tables.AddUpdatableTable(FixtureTable, FixtureColumns, [FixtureKeyColumn], DwSqliteFixture.IdentityColumnName));
    }

    #endregion

    #region The updatekeyinplace=no key-change workaround - all with NO database

    /// <summary>
    /// Builds an in-memory carrier with <paramref name="rowStatuses"/> rows in the primary buffer, each
    /// row's column statuses taken from <paramref name="columnStatuses"/>.
    /// </summary>
    private static DataWindowBufferStore CarrierWith(
        ItemStatus[] rowStatuses,
        Dictionary<(long Row, int Column), ItemStatus>? columnStatuses = null,
        Dictionary<(long Row, int Column), object?>? values = null)
    {
        DataWindowBufferStore store = new();

        foreach (ItemStatus rowStatus in rowStatuses)
        {
            store.AppendRow(DwBuffer.Primary, rowStatus);
        }

        foreach (((long row, int column), ItemStatus status) in
            columnStatuses ?? new Dictionary<(long, int), ItemStatus>())
        {
            store.SetItemStatus(row, column, DwBuffer.Primary, status);
        }

        foreach (((long row, int column), object? value) in
            values ?? new Dictionary<(long, int), object?>())
        {
            store.SetItemValue(row, column, DwBuffer.Primary, value);
        }

        return store;
    }

    [Theory]
    [InlineData("no", true)]
    [InlineData("yes", false)]
    [InlineData("NO", false)]
    [InlineData("No", false)]
    [InlineData("!", false)]
    [InlineData("?", false)]
    [InlineData("", false)]
    public void ApplyKeyChangeRefresh_GatesOnTheRuntimeDescribeExactlyAndCaseSensitively(
        string describedValue,
        bool expectedRequired)
    {
        // `if Data.Describe("DataWindow.Table.UpdateKeyinPlace") = "no" then` [:L155]. PowerScript's `=`
        // on strings is case-sensitive, so only the exact lower-case "no" opens the gate.
        FakeUpdateTarget target = FixtureTarget();
        target.KeyInPlaceAnswer = describedValue;

        UpdatePreparer preparer = new(target, target);

        DataWindowBufferStore store = CarrierWith(
            [ItemStatus.DataModified],
            new Dictionary<(long, int), ItemStatus> { [(1L, 1)] = ItemStatus.DataModified });

        (bool refreshRequired, IReadOnlyList<KeyColumnRefresh> refreshes) =
            preparer.ApplyKeyChangeRefresh([1], store);

        Assert.Equal(expectedRequired, refreshRequired);
        Assert.Equal(expectedRequired ? 1 : 0, refreshes.Count);
    }

    [Fact]
    public void ApplyKeyChangeRefresh_IgnoresTheDescriptor_AndReadsTheCarrierInstead()
    {
        // The descriptor says TRUE, the carrier still describes "no": the refresh MUST run, because the
        // gate is on the runtime value (DECISION 5).
        FakeUpdateTarget target = FixtureTarget();
        target.KeyInPlaceAnswer = "no";

        UpdatePreparer preparer = new(target, target);

        UpdatableTableDescriptor descriptor = UpdatableTableDescriptor.Create(
            FixtureTable,
            FixtureColumns,
            [FixtureKeyColumn],
            FixtureKeyColumn,
            updateWhere: 1L,
            updateKeyInPlace: true);

        DataWindowBufferStore store = CarrierWith(
            [ItemStatus.DataModified],
            new Dictionary<(long, int), ItemStatus> { [(1L, 1)] = ItemStatus.DataModified });

        UpdatePreparationResult result = preparer.Prepare(descriptor, store);

        Assert.True(result.KeyChangeRefreshRequired);
        Assert.Equal([new KeyColumnRefresh(1L, 1)], result.KeyChangeRefreshes);

        // The converse: descriptor says false but the carrier describes "yes" - no refresh.
        FakeUpdateTarget permissive = FixtureTarget();
        permissive.KeyInPlaceAnswer = "yes";

        UpdatePreparationResult declined =
            new UpdatePreparer(permissive, permissive).Prepare(FixtureDescriptor(), store);

        Assert.False(declined.KeyChangeRefreshRequired);
        Assert.Empty(declined.KeyChangeRefreshes);
    }

    [Fact]
    public void ApplyKeyChangeRefresh_WithNoResolvedKeyColumns_KeepsTheGateShut()
    {
        // `nCount = UpperBound(nKeyColumns) ; if nCount > 0 then` [:L156-L157].
        FakeUpdateTarget target = FixtureTarget();

        UpdatePreparer preparer = new(target, target);

        DataWindowBufferStore store = CarrierWith([ItemStatus.DataModified]);

        (bool refreshRequired, IReadOnlyList<KeyColumnRefresh> refreshes) =
            preparer.ApplyKeyChangeRefresh([], store);

        Assert.False(refreshRequired);
        Assert.Empty(refreshes);
    }

    [Theory]
    [InlineData(ItemStatus.NotModified)]
    [InlineData(ItemStatus.New)]
    [InlineData(ItemStatus.NewModified)]
    public void ApplyKeyChangeRefresh_SkipsRowsWhoseColumnZeroStatusIsNotDataModified(ItemStatus rowStatus)
    {
        // `if Data.GetItemStatus(nRow,0,Primary!) <> DataModified! then continue` [:L160]. Column ZERO is
        // the ROW's status. Note NewModified! is skipped too, which is correct: an inserted row already
        // generates an INSERT, so it needs no delete-plus-insert rewrite.
        FakeUpdateTarget target = FixtureTarget();

        UpdatePreparer preparer = new(target, target);

        DataWindowBufferStore store = CarrierWith(
            [rowStatus],
            new Dictionary<(long, int), ItemStatus> { [(1L, 1)] = ItemStatus.DataModified });

        (bool refreshRequired, IReadOnlyList<KeyColumnRefresh> refreshes) =
            preparer.ApplyKeyChangeRefresh([1], store);

        Assert.True(refreshRequired);
        Assert.Empty(refreshes);
    }

    [Theory]
    [InlineData(ItemStatus.NotModified)]
    [InlineData(ItemStatus.New)]
    [InlineData(ItemStatus.NewModified)]
    public void ApplyKeyChangeRefresh_SkipsKeyColumnsWhoseOwnStatusIsNotDataModified(
        ItemStatus columnStatus)
    {
        // `if Data.GetItemStatus(nRow,nKeyColumns[nIndex],Primary!) <> DataModified! then continue`
        // [:L162]. A one-based COLUMN number this time, not the row sentinel.
        FakeUpdateTarget target = FixtureTarget();

        UpdatePreparer preparer = new(target, target);

        DataWindowBufferStore store = CarrierWith(
            [ItemStatus.DataModified],
            new Dictionary<(long, int), ItemStatus> { [(1L, 1)] = columnStatus });

        (bool refreshRequired, IReadOnlyList<KeyColumnRefresh> refreshes) =
            preparer.ApplyKeyChangeRefresh([1], store);

        Assert.True(refreshRequired);
        Assert.Empty(refreshes);
    }

    [Fact]
    public void ApplyKeyChangeRefresh_RefreshesExactlyTheQualifyingRowColumnPairs_InVisitOrder()
    {
        FakeUpdateTarget target = FixtureTarget();

        UpdatePreparer preparer = new(target, target);

        // Four rows. Row 2 is not data-modified, so it is skipped entirely even though both of its key
        // columns are. Row 1 qualifies on column 1 only, row 3 on both, row 4 on neither.
        DataWindowBufferStore store = CarrierWith(
            [
                ItemStatus.DataModified,
                ItemStatus.NewModified,
                ItemStatus.DataModified,
                ItemStatus.DataModified,
            ],
            new Dictionary<(long, int), ItemStatus>
            {
                [(1L, 1)] = ItemStatus.DataModified,
                [(1L, 3)] = ItemStatus.NotModified,
                [(2L, 1)] = ItemStatus.DataModified,
                [(2L, 3)] = ItemStatus.DataModified,
                [(3L, 1)] = ItemStatus.DataModified,
                [(3L, 3)] = ItemStatus.DataModified,
            });

        (bool refreshRequired, IReadOnlyList<KeyColumnRefresh> refreshes) =
            preparer.ApplyKeyChangeRefresh([1, 3], store);

        Assert.True(refreshRequired);

        // Ascending by row [:L159], then key columns in descriptor order [:L161].
        Assert.Equal(
            [
                new KeyColumnRefresh(1L, 1),
                new KeyColumnRefresh(3L, 1),
                new KeyColumnRefresh(3L, 3),
            ],
            refreshes);
    }

    [Fact]
    public void ApplyKeyChangeRefresh_PreservesTheOriginalValueWhileReAssertingTheStatus()
    {
        // This is the assertion that the self-assignment replacement is faithful: the original value the
        // concurrency check compares against MUST survive the refresh untouched, and the column must
        // still read as modified afterwards.
        FakeUpdateTarget target = FixtureTarget();

        UpdatePreparer preparer = new(target, target);

        DataWindowBufferStore store = new();
        store.AppendRow(DwBuffer.Primary, ItemStatus.DataModified);

        // The row was retrieved with 10 and then edited to 20, so original is 10 and current is 20.
        store.SetItemValue(1L, 1, DwBuffer.Primary, 10L);
        store.RowAt(1L, DwBuffer.Primary).Baseline();
        store.SetItemValue(1L, 1, DwBuffer.Primary, 20L);

        // Baseline() also resets the ROW's own status to NotModified, so BOTH statuses have to be
        // stamped to model an edited row - the row's via the column-zero sentinel and the column's via
        // its one-based number. Getting this wrong in the harness is precisely the confusion
        // ItemStatusMachine's explicit row-versus-column addressing exists to prevent, and the
        // production gate at :L160 correctly declined until both were set.
        store.SetItemStatus(
            1L,
            ItemStatusMachine.RowStatusColumn,
            DwBuffer.Primary,
            ItemStatus.DataModified);
        store.SetItemStatus(1L, 1, DwBuffer.Primary, ItemStatus.DataModified);

        Assert.Equal(10L, store.GetItemOriginalValue(1L, 1, DwBuffer.Primary));

        (bool refreshRequired, IReadOnlyList<KeyColumnRefresh> refreshes) =
            preparer.ApplyKeyChangeRefresh([1], store);

        Assert.True(refreshRequired);
        Assert.Equal([new KeyColumnRefresh(1L, 1)], refreshes);

        // The current value is unchanged, the ORIGINAL is still 10 - not clobbered to 20 - and the
        // column still reads modified. Without all three the generated DELETE would carry the wrong
        // where clause.
        Assert.Equal(20L, store.GetItemValue(1L, 1, DwBuffer.Primary));
        Assert.Equal(10L, store.GetItemOriginalValue(1L, 1, DwBuffer.Primary));
        Assert.Equal(
            ItemStatus.DataModified,
            store.GetItemStatus(1L, 1, DwBuffer.Primary));
    }

    [Fact]
    public void ApplyKeyChangeRefresh_CapturesAnOriginalForAColumnStampedWithoutAValueWrite()
    {
        // A column can be stamped modified by a codec without a value ever having gone through
        // SetValue. Writing the current value back is what gives such a column an explicit original,
        // which is why the write is not a no-op in the managed model.
        FakeUpdateTarget target = FixtureTarget();

        UpdatePreparer preparer = new(target, target);

        DataWindowBufferStore store = new();
        store.AppendRow(DwBuffer.Primary, ItemStatus.DataModified);
        store.SetItemStatus(1L, 2, DwBuffer.Primary, ItemStatus.DataModified);

        preparer.ApplyKeyChangeRefresh([2], store);

        // Original equals current, which is the consistent answer and not a fabricated one.
        Assert.Equal(
            store.GetItemValue(1L, 2, DwBuffer.Primary),
            store.GetItemOriginalValue(1L, 2, DwBuffer.Primary));
    }

    [Fact]
    public void Prepare_OverTheFixture_RunsTheWorkaroundBecauseTheFixtureSetsKeyInPlaceToNo()
    {
        // dw_sqlite.srd:L14 sets updatekeyinplace=no itself, so this path is the MAINLINE.
        FakeUpdateTarget target = FixtureTarget();

        UpdatePreparer preparer = new(target, target);

        DataWindowBufferStore store = CarrierWith(
            [ItemStatus.DataModified],
            new Dictionary<(long, int), ItemStatus> { [(1L, 1)] = ItemStatus.DataModified });

        UpdatePreparationResult result = preparer.Prepare(FixtureDescriptor(), store);

        Assert.True(result.IsSucceeded);
        Assert.True(result.KeyChangeRefreshRequired);
        Assert.Equal([new KeyColumnRefresh(1L, 1)], result.KeyChangeRefreshes);
        Assert.Equal([1], result.KeyColumnIds);

        // The gate is read exactly once per prepare, and only after the script was applied.
        Assert.Equal(1, target.KeyInPlaceReads);
        Assert.Single(target.AppliedScripts);
    }

    [Fact]
    public void Prepare_WithNoModifiedRows_ReportsTheGateOpenWithAnEmptyRefreshList()
    {
        // Gate open with zero refreshes is meaningful, not contradictory: no row had a modified key.
        FakeUpdateTarget target = FixtureTarget();

        UpdatePreparer preparer = new(target, target);

        UpdatePreparationResult result =
            preparer.Prepare(FixtureDescriptor(), CarrierWith([ItemStatus.NotModified]));

        Assert.True(result.IsSucceeded);
        Assert.True(result.KeyChangeRefreshRequired);
        Assert.Empty(result.KeyChangeRefreshes);
    }

    #endregion

    #region The multi-table loop and the single-table path

    [Fact]
    public void PrepareAndUpdate_SingleTablePath_RunsNoPrepareAtAll()
    {
        // `else rtCode = _of_Update(data)` [:L370-L371]. _of_updateprepare has exactly ONE caller and
        // it is in the multi-table branch, so with the switch off the descriptor array is NOT applied.
        FakeUpdateTarget target = FixtureTarget();

        UpdatePreparer preparer = new(target, target);

        UpdatableTableCollection tables = new() { MultiTableUpdate = false };
        tables.AddUpdatableTable(FixtureTable, FixtureColumns, [FixtureKeyColumn], DwSqliteFixture.IdentityColumnName, 1L, false);

        int updateCalls = 0;

        long code = preparer.PrepareAndUpdate(
            tables,
            new DataWindowBufferStore(),
            () =>
            {
                updateCalls++;

                return RetCode.OK;
            },
            out string errorText);

        Assert.Equal(RetCode.OK, code);
        Assert.Equal(1, updateCalls);
        Assert.Empty(errorText);

        // No script was ever applied and no describe was ever read: prepare did not run.
        Assert.Empty(target.AppliedScripts);
        Assert.Empty(target.ColumnIdRequests);
        Assert.Equal(0, target.KeyInPlaceReads);
    }

    [Fact]
    public void PrepareAndUpdate_SingleTablePath_IgnoresAnEmptyDescriptorArray()
    {
        // With the switch OFF an empty array is ordinary, not an error - the branch never consults it.
        FakeUpdateTarget target = FixtureTarget();

        UpdatePreparer preparer = new(target, target);

        long code = preparer.PrepareAndUpdate(
            new UpdatableTableCollection { MultiTableUpdate = false },
            new DataWindowBufferStore(),
            () => RetCode.OK,
            out string errorText);

        Assert.Equal(RetCode.OK, code);
        Assert.Empty(errorText);
    }

    [Fact]
    public void PrepareAndUpdate_MultiTableWithNoDescriptors_FailsWithTheVerbatimChineseText()
    {
        // `sError = "没有设置可更新表!" ; rtCode = RetCode.E_INVALID_ARGUMENT` [:L359-L363].
        FakeUpdateTarget target = FixtureTarget();

        UpdatePreparer preparer = new(target, target);

        int updateCalls = 0;

        long code = preparer.PrepareAndUpdate(
            new UpdatableTableCollection { MultiTableUpdate = true },
            new DataWindowBufferStore(),
            () =>
            {
                updateCalls++;

                return RetCode.OK;
            },
            out string errorText);

        Assert.Equal(RetCode.E_INVALID_ARGUMENT, code);
        Assert.Equal("没有设置可更新表!", errorText);
        Assert.Equal(UpdateWhereBuilder.NoUpdatableTableMessage, errorText);
        Assert.Equal(0, updateCalls);
    }

    [Fact]
    public void PrepareAndUpdate_MultiTable_PreparesThenUpdatesOncePerDescriptorInOneBasedOrder()
    {
        // `for nIndex = 1 to nCount { prepare(index) ; update() }` [:L364-L369].
        FakeUpdateTarget target = FixtureTarget();

        UpdatePreparer preparer = new(target, target);

        UpdatableTableCollection tables = new() { MultiTableUpdate = true };
        tables.AddUpdatableTable("first", ["id"], ["id"], string.Empty, 1L, false);
        tables.AddUpdatableTable("second", ["name"], ["name"], string.Empty, 1L, false);
        tables.AddUpdatableTable("third", ["age"], ["age"], string.Empty, 1L, false);

        int updateCalls = 0;

        long code = preparer.PrepareAndUpdate(
            tables,
            new DataWindowBufferStore(),
            () =>
            {
                updateCalls++;

                return RetCode.OK;
            },
            out string errorText);

        Assert.Equal(RetCode.OK, code);
        Assert.Equal(3, updateCalls);
        Assert.Empty(errorText);

        // Three scripts, in array order, each naming its own table.
        Assert.Equal(3, target.AppliedScripts.Count);
        Assert.EndsWith("= 'first'", target.AppliedScripts[0], StringComparison.Ordinal);
        Assert.EndsWith("= 'second'", target.AppliedScripts[1], StringComparison.Ordinal);
        Assert.EndsWith("= 'third'", target.AppliedScripts[2], StringComparison.Ordinal);
    }

    [Fact]
    public void PrepareAndUpdate_MultiTable_StopsAtTheFirstPrepareFailure()
    {
        FakeUpdateTarget target = FixtureTarget();

        // The second table's key column does not resolve, so its prepare fails.
        target.ColumnIds.Remove("name" + UpdateWhereBuilder.ColumnIdSuffix);

        UpdatePreparer preparer = new(target, target);

        UpdatableTableCollection tables = new() { MultiTableUpdate = true };
        tables.AddUpdatableTable("first", ["id"], ["id"], string.Empty, 1L, false);
        tables.AddUpdatableTable("second", ["name"], ["name"], string.Empty, 1L, false);
        tables.AddUpdatableTable("third", ["age"], ["age"], string.Empty, 1L, false);

        int updateCalls = 0;

        long code = preparer.PrepareAndUpdate(
            tables,
            new DataWindowBufferStore(),
            () =>
            {
                updateCalls++;

                return RetCode.OK;
            },
            out string errorText);

        Assert.Equal(RetCode.E_INTERNAL_ERROR, code);
        Assert.Equal("无效的列名:name", errorText);

        // The first table updated; the third was never attempted.
        Assert.Equal(1, updateCalls);
        Assert.Single(target.AppliedScripts);
    }

    [Fact]
    public void PrepareAndUpdate_MultiTable_StopsAtTheFirstUpdateFailure()
    {
        FakeUpdateTarget target = FixtureTarget();

        UpdatePreparer preparer = new(target, target);

        UpdatableTableCollection tables = new() { MultiTableUpdate = true };
        tables.AddUpdatableTable("first", ["id"], ["id"], string.Empty, 1L, false);
        tables.AddUpdatableTable("second", ["name"], ["name"], string.Empty, 1L, false);
        tables.AddUpdatableTable("third", ["age"], ["age"], string.Empty, 1L, false);

        int updateCalls = 0;

        long code = preparer.PrepareAndUpdate(
            tables,
            new DataWindowBufferStore(),
            () =>
            {
                updateCalls++;

                return updateCalls == 2 ? RetCode.E_DB_ERROR : RetCode.OK;
            },
            out string errorText);

        Assert.Equal(RetCode.E_DB_ERROR, code);
        Assert.Equal(2, updateCalls);

        // Two prepares ran - the third table was never prepared.
        Assert.Equal(2, target.AppliedScripts.Count);

        // The update step owns its own diagnostic, so this method leaves errorText empty on that arm.
        Assert.Empty(errorText);
    }

    [Fact]
    public void PrepareAndUpdate_MultiTable_BreaksOnCancelled_WhichAFailurePredicateWouldHaveMissed()
    {
        // DECISION 6. The oracle's test is `rtCode <> RetCode.OK` [:L366, :L368], and
        // Predicates.IsFailed(RetCode.CANCELLED) is FALSE under the published tri-state algebra - so a
        // predicate-based port would let a cancelled table fall through to the next one. This test is
        // the regression guard for exactly that substitution.
        Assert.False(Predicates.IsFailed(RetCode.CANCELLED));
        Assert.NotEqual(RetCode.OK, RetCode.CANCELLED);

        FakeUpdateTarget target = FixtureTarget();

        UpdatePreparer preparer = new(target, target);

        UpdatableTableCollection tables = new() { MultiTableUpdate = true };
        tables.AddUpdatableTable("first", ["id"], ["id"], string.Empty, 1L, false);
        tables.AddUpdatableTable("second", ["name"], ["name"], string.Empty, 1L, false);

        int updateCalls = 0;

        long code = preparer.PrepareAndUpdate(
            tables,
            new DataWindowBufferStore(),
            () =>
            {
                updateCalls++;

                return RetCode.CANCELLED;
            },
            out _);

        Assert.Equal(RetCode.CANCELLED, code);
        Assert.Equal(1, updateCalls);
    }

    [Fact]
    public void PrepareAndUpdate_MultiTable_BreaksOnPrevent_WhichIsAlsoNotOK()
    {
        // The same guard from the other side: PREVENT reads as a SUCCESS under the algebra but is still
        // `!= OK`, so it too must break the loop.
        Assert.True(Predicates.IsSucceeded(RetCode.PREVENT));
        Assert.NotEqual(RetCode.OK, RetCode.PREVENT);

        FakeUpdateTarget target = FixtureTarget();

        UpdatePreparer preparer = new(target, target);

        UpdatableTableCollection tables = new() { MultiTableUpdate = true };
        tables.AddUpdatableTable("first", ["id"], ["id"], string.Empty, 1L, false);
        tables.AddUpdatableTable("second", ["name"], ["name"], string.Empty, 1L, false);

        int updateCalls = 0;

        long code = preparer.PrepareAndUpdate(
            tables,
            new DataWindowBufferStore(),
            () =>
            {
                updateCalls++;

                return RetCode.PREVENT;
            },
            out _);

        Assert.Equal(RetCode.PREVENT, code);
        Assert.Equal(1, updateCalls);
    }

    #endregion

    #region R9 - the one-based regression guard

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(6)]
    [InlineData(37)]
    public void BuildModificationString_EmitsExactlyThreeResetLinesPerColumn_IndexedOneToCount(
        int columnCount)
    {
        FakeUpdateTarget target = new() { ColumnCount = columnCount };
        target.ColumnIds["k" + UpdateWhereBuilder.ColumnIdSuffix] = 1;

        UpdatableTableDescriptor descriptor = UpdatableTableDescriptor.Create(
            FixtureTable,
            ["k"],
            ["k"],
            identityColumn: string.Empty,
            updateWhere: null,
            updateKeyInPlace: null);

        ModificationScriptResult result =
            UpdateWhereBuilder.BuildModificationString(descriptor, target);

        string[] resetLines = [.. result.Script
            .Split(UpdateWhereBuilder.LineSeparator)
            .Where(line => line.StartsWith(UpdateWhereBuilder.ColumnOrdinalPrefix, StringComparison.Ordinal))];

        Assert.Equal(3 * columnCount, resetLines.Length);

        // Every ordinal 1..count appears exactly three times, in Update/Key/Identity order.
        for (int ordinal = 1; ordinal <= columnCount; ordinal++)
        {
            string prefix = "#" + ordinal.ToString(CultureInfo.InvariantCulture);

            Assert.Equal(prefix + ".Update = no", resetLines[((ordinal - 1) * 3) + 0]);
            Assert.Equal(prefix + ".Key = no", resetLines[((ordinal - 1) * 3) + 1]);
            Assert.Equal(prefix + ".Identity = no", resetLines[((ordinal - 1) * 3) + 2]);
        }

        // NO #0 - the symptom of an accidental rebase - and NO #<count+1> - the symptom of using Count
        // as an inclusive bound on a one-based domain.
        Assert.DoesNotContain("#0.", result.Script, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "#" + (columnCount + 1).ToString(CultureInfo.InvariantCulture) + ".",
            result.Script,
            StringComparison.Ordinal);
    }

    [Fact]
    public void BuildModificationString_ZeroColumnCount_EmitsNoResetLines()
    {
        // `for nIndex = 1 to 0` executes zero times, which is how a failed column-count describe behaves
        // once Long() has coerced it to zero.
        FakeUpdateTarget target = new() { ColumnCount = 0 };
        target.ColumnIds["k" + UpdateWhereBuilder.ColumnIdSuffix] = 1;

        UpdatableTableDescriptor descriptor = UpdatableTableDescriptor.Create(
            FixtureTable,
            ["k"],
            ["k"],
            identityColumn: string.Empty,
            updateWhere: null,
            updateKeyInPlace: null);

        ModificationScriptResult result =
            UpdateWhereBuilder.BuildModificationString(descriptor, target);

        Assert.DoesNotContain(UpdateWhereBuilder.ColumnOrdinalPrefix, result.Script, StringComparison.Ordinal);
        Assert.Equal(
            Script("k.Update = yes", "k.Key = yes", "DataWindow.Table.UpdateTable = 'COMPANY'"),
            result.Script);
    }

    [Theory]
    [InlineData(0, 3)]
    [InlineData(-1, 3)]
    [InlineData(4, 3)]
    [InlineData(1, 0)]
    public void OneBasedIndex_ToZeroBased_RejectsAnythingOutsideTheDomain(int oneBasedIndex, int upperBound)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => OneBasedIndex.ToZeroBased(oneBasedIndex, upperBound, "index"));
    }

    [Theory]
    [InlineData(1, 3, 0)]
    [InlineData(2, 3, 1)]
    [InlineData(3, 3, 2)]
    public void OneBasedIndex_ToZeroBased_SubtractsExactlyOne(int oneBasedIndex, int upperBound, int expected)
    {
        Assert.Equal(expected, OneBasedIndex.ToZeroBased(oneBasedIndex, upperBound, "index"));
    }

    [Fact]
    public void OneBasedIndex_UpperBoundAndAppendIndex_MatchThePowerScriptIdioms()
    {
        Assert.Equal(OneBasedIndex.EmptyUpperBound, OneBasedIndex.UpperBound<string>(null));
        Assert.Equal(OneBasedIndex.EmptyUpperBound, OneBasedIndex.UpperBound(Array.Empty<string>()));
        Assert.Equal(3, OneBasedIndex.UpperBound(new[] { "a", "b", "c" }));

        Assert.Equal(OneBasedIndex.FirstIndex, OneBasedIndex.AppendIndex<string>(null));
        Assert.Equal(OneBasedIndex.FirstIndex, OneBasedIndex.AppendIndex(Array.Empty<string>()));
        Assert.Equal(4, OneBasedIndex.AppendIndex(new[] { "a", "b", "c" }));
    }

    [Fact]
    public void OneBasedIndex_Range_YieldsOneToUpperBoundInclusive_AndNothingForAnEmptyBound()
    {
        Assert.Equal([1, 2, 3], OneBasedIndex.Range(3));
        Assert.Equal([1], OneBasedIndex.Range(1));
        Assert.Empty(OneBasedIndex.Range(OneBasedIndex.EmptyUpperBound));
        Assert.Empty(OneBasedIndex.Range(-5));
    }

    [Theory]
    [InlineData(1, 3, true)]
    [InlineData(3, 3, true)]
    [InlineData(0, 3, false)]
    [InlineData(4, 3, false)]
    [InlineData(1, 0, false)]
    public void OneBasedIndex_IsWithin_BoundsTheOneBasedDomain(int oneBasedIndex, int upperBound, bool expected)
    {
        Assert.Equal(expected, OneBasedIndex.IsWithin(oneBasedIndex, upperBound));
    }

    #endregion

    #region The marked-column helper and the mode constant

    [Fact]
    public void MarkedColumnsOf_OverTheFixture_SpansAllSixColumnsWithTheKeyFirst()
    {
        // updatewhere=1 is "key and updateable columns", and on the fixture all six carry
        // update=yes updatewhereclause=yes [dw_sqlite.srd:L8-L13], so the check spans all six originals.
        IReadOnlyList<string> marked = UpdateWhereBuilder.MarkedColumnsOf(FixtureDescriptor());

        // CONSUMED FROM THE FIXTURE. `id` is the sole key column so it leads, and because it is also
        // the first updatable column the deduplicated result happens to equal declaration order here -
        // which is asserted rather than assumed, and the sibling test below pins the key-first rule on
        // a descriptor where the two orders genuinely differ.
        Assert.Equal(DwSqliteFixture.ColumnNames, marked);

        // An independent literal, as the one pin on the fixture's column-name transcription.
        Assert.Equal(["id", "name", "age", "address", "salary", "birth"], marked);

        // COUNT THEM. Six, not five: a naive assertion that merely checked "the key column is in
        // there" would pass on a payload that omitted an updateable column's original value, and the
        // omission would silently widen the concurrency window rather than fail.
        Assert.Equal(DwSqliteFixture.ColumnCount, marked.Count);
        Assert.Equal(6, marked.Count);
        Assert.Equal(FixtureColumns.Count, marked.Count);
    }

    [Fact]
    public void MarkedColumnsOf_PutsKeyColumnsFirstAndDeduplicatesCaseInsensitively()
    {
        UpdatableTableDescriptor descriptor = UpdatableTableDescriptor.Create(
            FixtureTable,
            ["Salary", "name", "ID"],
            ["id", "salary"],
            identityColumn: string.Empty,
            updateWhere: 1L,
            updateKeyInPlace: null);

        IReadOnlyList<string> marked = UpdateWhereBuilder.MarkedColumnsOf(descriptor);

        // Key columns first in their own order, then only the updatable columns not already seen. The
        // FIRST spelling encountered wins, so the output is made of the caller's own strings.
        Assert.Equal(["id", "salary", "name"], marked);
    }

    [Fact]
    public void MarkedColumnsOf_AnEmptyDescriptorYieldsNothing()
    {
        UpdatableTableDescriptor descriptor = UpdatableTableDescriptor.Create(
            FixtureTable,
            updatableColumns: null,
            keyColumns: null,
            identityColumn: null,
            updateWhere: null,
            updateKeyInPlace: null);

        Assert.Empty(UpdateWhereBuilder.MarkedColumnsOf(descriptor));
        Assert.Empty(descriptor.IdentityColumn);
        Assert.False(descriptor.IsAcceptable);
    }

    [Theory]
    [InlineData(1L, true)]
    [InlineData(0L, false)]
    [InlineData(2L, false)]
    [InlineData(null, false)]
    public void IsKeyAndUpdatableColumnsMode_OnlyMatchesModeOne_AndNullIsNotModeZero(
        long? effectiveMode,
        bool expected)
    {
        Assert.Equal(expected, UpdateWhereBuilder.IsKeyAndUpdatableColumnsMode(effectiveMode));
        Assert.Equal(1L, UpdateWhereBuilder.KeyAndUpdatableColumnsMode);
    }

    [Theory]
    [InlineData("6", 6)]
    [InlineData("1", 1)]
    [InlineData("-3", -3)]
    [InlineData("  4  ", 4)]
    [InlineData("!", 0)]
    [InlineData("?", 0)]
    [InlineData("", 0)]
    [InlineData(null, 0)]
    [InlineData("yes", 0)]
    [InlineData("1.5", 0)]
    [InlineData("1,000", 0)]
    public void CoerceDescribedNumber_ReproducesPowerScriptLongOnADescribeResult(
        string? describeResult,
        int expected)
    {
        // Long("!") and Long("?") are zero, which is what makes a failed describe fall into the oracle's
        // own non-positive arm rather than throwing. A thousands separator is rejected because a
        // describe answers a machine string.
        Assert.Equal(expected, UpdateWhereBuilder.CoerceDescribedNumber(describeResult));
    }

    #endregion

    #region The updatewhere=1 payload rule - BOTH halves of every marked column travel (G4a, C-B)

    // ==========================================================================================
    //  THE CONTRACT THIS REGION EXISTS FOR, AND WHY IT IS THE HARDEST ONE IN THE REFACTOR.
    //
    //  The sole updatable DataWindow in the repository declares
    //
    //      update="COMPANY" updatewhere=1 updatekeyinplace=no
    //                                       [ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L14]
    //
    //  and ALL SIX of its columns carry `update=yes updatewhereclause=yes` [:L8-L13].
    //
    //  `updatewhere=1` is the "KEY AND UPDATEABLE COLUMNS" concurrency mode: the generated statement's
    //  WHERE clause carries the key column PLUS THE ORIGINAL VALUE OF EVERY UPDATEABLE COLUMN. With
    //  all six marked, the optimistic-concurrency check therefore spans ALL SIX COLUMNS' ORIGINAL
    //  VALUES - which is goal G4(a): preserve that semantic ACROSS A NETWORK BOUNDARY.
    //
    //  THE CONSEQUENCE, WHICH IS WHAT THESE TESTS ASSERT: THE PAYLOAD MUST TRANSMIT, PER ROW, BOTH
    //  THE CURRENT AND THE ORIGINAL VALUE OF EVERY MARKED COLUMN. A payload with ONE VALUE PER CELL
    //  cannot express the contract at all. It has exactly two options and both are wrong:
    //
    //    * omit the WHERE clause, silently overwriting a concurrent change - the one outcome the
    //      refactor forbids outright; or
    //    * compare against the value being written, which always matches, so the check never fires.
    //
    //  That is the whole reason Buffers/ exists as an ANTI-CORRUPTION LAYER rather than a flat rowset,
    //  and it is why the legacy result carrier derives from a datastore
    //  [ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds.sru:L8] - the legacy carrier IS
    //  a DataWindow, complete with three buffers, per-row and per-column item status, and an
    //  original-value shadow. CarrierRow reproduces that triple explicitly.
    //
    //  THE ORG FLAG IS THE ORACLE'S OWN MECHANISM FOR READING THE SECOND HALF. The legacy accessor is
    //  FOUR-ARGUMENT:
    //
    //      GetItemNumber(row, col, buff, org)
    //                    [ws_objects/pfw.utility.sqlite.pbl.src/sqlitegetitemdouble.srf:L7-L8, L11, L14]
    //
    //  where `org` selects the ORIGINAL rather than the current value; the identity round trip uses the
    //  same four-argument shape with org false [n_cst_thread_task_sqlupdate.sru:L239]. In this port that
    //  boolean becomes a MEMBER CHOICE rather than a parameter - GetItemValue is the org=false read and
    //  GetItemOriginalValue is the org=true read - because a bare bool at a call site says nothing
    //  about which half it selects. The pairing is asserted below in both directions so the two members
    //  cannot be confused, and NEITHER of them is a substitute for the other.
    //
    //  C-E: NO DATABASE IS INVOLVED. Every assertion below runs over
    //  DwSqliteFixture.SampleCarrier(), an in-memory three-buffer model. No SQLite file, no
    //  connection, no dialect client, and no statement is generated anywhere in this region - the
    //  statement generator is Tasks/SqlUpdateTask's and the conflict projection is
    //  Concurrency/ConflictDetector's.
    // ==========================================================================================

    [Theory]
    [MemberData(nameof(DwSqliteFixture.ColumnMatrix), MemberType = typeof(DwSqliteFixture))]
    public void EveryMarkedColumn_CarriesBothItsCurrentAndItsOriginalValue_OnEveryRow(
        int id,
        string name,
        string dbName,
        string declaredType,
        bool update,
        bool updateWhereClause,
        bool key,
        bool identity)
    {
        // THE THEORY RUNS OVER ALL SIX COLUMNS, CONSUMED FROM THE FIXTURE'S OWN MATRIX, so a column
        // added to or removed from the transcription changes the number of cases here automatically.
        DwSqliteColumn column = DwSqliteFixture.ColumnAt(id);

        Assert.Equal(column.Name, name);
        Assert.Equal(column.DbName, dbName);
        Assert.Equal(column.DeclaredType, declaredType);

        // THIS COLUMN IS MARKED, WHICH IS THE PRECONDITION FOR THE REST OF THE ASSERTION. Every one of
        // the six carries both attributes [dw_sqlite.srd:L8-L13]; only `id` is additionally the key and
        // the identity column [:L8].
        Assert.True(update);
        Assert.True(updateWhereClause);
        Assert.Equal(id == DwSqliteFixture.IdColumnNumber, key);
        Assert.Equal(id == DwSqliteFixture.IdColumnNumber, identity);

        // AND IT PARTICIPATES IN THE CONCURRENCY CHECK, because updatewhere=1 spans the key column plus
        // every updateable column.
        Assert.Contains(
            column.Name,
            UpdateWhereBuilder.MarkedColumnsOf(DwSqliteFixture.Descriptor()));

        DataWindowBufferStore carrier = DwSqliteFixture.SampleCarrier();

        foreach (DwSqliteSampleRow row in DwSqliteFixture.SampleRows)
        {
            DwSqliteSampleCell cell = row.CellAt(id);

            // THE org=false READ - the current value, which is what the SET list carries.
            object? current = carrier.GetItemValue(row.Row, id, row.Buffer);

            // THE org=true READ - the ORIGINAL value, which is what the WHERE clause carries. This is
            // the four-argument accessor's second half [sqlitegetitemdouble.srf:L11].
            object? original = carrier.GetItemOriginalValue(row.Row, id, row.Buffer);

            // BOTH HALVES ARE PRESENT FOR EVERY MARKED COLUMN ON EVERY ROW, in every buffer. Not "the
            // current value plus an original where one happens to have been captured" - the carrier
            // answers the current value when nothing was captured, so both reads always answer.
            Assert.True(CarrierValue.AreEquivalent(cell.Current, current));
            Assert.True(CarrierValue.AreEquivalent(cell.Original, original));
        }
    }

    [Fact]
    public void TheOrgTrueAndOrgFalseReadsDiverge_OnAModifiedRow_AndBothTravel()
    {
        // THE ROW THAT PROVES A FLAT ROWSET IS INSUFFICIENT. The second sample row is stamped
        // DataModified and has two ordinary columns edited, so its current and original values differ
        // on those two and agree on the other four [see DwSqliteFixture.SampleRows, PRIMARY 2].
        DwSqliteSampleRow modified = DwSqliteFixture.SampleRows.First(row =>
            row.Buffer == DwBuffer.Primary && row.Status == ItemStatus.DataModified);

        DataWindowBufferStore carrier = DwSqliteFixture.SampleCarrier();

        int divergedColumns = 0;

        foreach (DwSqliteSampleCell cell in modified.Cells)
        {
            object? current = carrier.GetItemValue(modified.Row, cell.ColumnNumber, modified.Buffer);
            object? original =
                carrier.GetItemOriginalValue(modified.Row, cell.ColumnNumber, modified.Buffer);

            if (cell.IsEdited)
            {
                // THE TWO READS DIVERGE, AND BOTH ARE READABLE. An implementation that answered the
                // current value from both accessors would pass every assertion built on an untouched
                // row and fail exactly here - which is why this case is asserted separately.
                Assert.False(CarrierValue.AreEquivalent(current, original));
                Assert.True(CarrierValue.AreEquivalent(cell.Current, current));
                Assert.True(CarrierValue.AreEquivalent(cell.Original, original));

                divergedColumns++;
            }
            else
            {
                // AND ON AN UNEDITED COLUMN THEY AGREE, which is the state a naive port looks correct
                // in. The carrier answers the current value when no original was captured, so
                // "unchanged since the last baseline" and "no original exists" are the same statement.
                Assert.True(CarrierValue.AreEquivalent(current, original));
            }
        }

        // The fixture edits exactly two columns on this row, so the divergence is genuinely exercised
        // rather than accidentally absent.
        Assert.Equal(2, divergedColumns);
    }

    [Fact]
    public void ThePayloadCarriesSixCurrentAndSixOriginalValues_ForEveryMarkedColumn()
    {
        // THE WIRE SHAPE THAT EXPRESSES THE CONTRACT. DataWindowRow declares BOTH
        // `repeated ColumnValue columns = 4` and `repeated ColumnValue original_values = 5`
        // [shared/PowerFramework.Contracts/Proto/common.v1.proto], and this test projects the carrier
        // into it to assert that the carrier can actually fill both - which is the only reason the
        // second repeated field is there.
        IReadOnlyList<string> marked =
            UpdateWhereBuilder.MarkedColumnsOf(DwSqliteFixture.Descriptor());

        // COUNT THEM: SIX, NOT FIVE. A payload that carried the key column's original plus the four
        // it happened to notice would pass a naive assertion and leave one column out of the
        // concurrency check, silently widening the window rather than failing.
        Assert.Equal(DwSqliteFixture.ColumnCount, marked.Count);
        Assert.Equal(6, marked.Count);

        DataWindowBufferStore carrier = DwSqliteFixture.SampleCarrier();

        foreach (DwSqliteSampleRow sampleRow in DwSqliteFixture.SampleRows)
        {
            DataWindowRow payload = new()
            {
                Buffer = sampleRow.Buffer,
                Row = sampleRow.Row,
                ItemStatus = carrier.GetItemStatus(
                    sampleRow.Row, ItemStatusMachine.RowStatusColumn, sampleRow.Buffer),
            };

            foreach (string columnName in marked)
            {
                DwSqliteColumn column = DwSqliteFixture.ColumnOf(columnName);

                // The org=false read fills `columns`, the org=true read fills `original_values`.
                object? current = carrier.GetItemValue(sampleRow.Row, column.Id, sampleRow.Buffer);
                object? original =
                    carrier.GetItemOriginalValue(sampleRow.Row, column.Id, sampleRow.Buffer);

                Assert.True(CarrierValue.TryToWire(current, out AnyValue? currentWire));
                Assert.True(CarrierValue.TryToWire(original, out AnyValue? originalWire));
                Assert.NotNull(currentWire);
                Assert.NotNull(originalWire);

                payload.Columns.Add(new ColumnValue
                {
                    ColumnName = column.Name,
                    ColumnId = column.Id,
                    Value = currentWire,
                    ItemStatus = carrier.GetItemStatus(sampleRow.Row, column.Id, sampleRow.Buffer),
                });

                payload.OriginalValues.Add(new ColumnValue
                {
                    ColumnName = column.Name,
                    ColumnId = column.Id,
                    Value = originalWire,
                });
            }

            // BOTH REPEATED FIELDS ARE FULLY POPULATED, one entry per marked column, and they name the
            // same six columns in the same order - so a consumer can pair them positionally or by name
            // and get the same answer either way.
            Assert.Equal(6, payload.Columns.Count);
            Assert.Equal(6, payload.OriginalValues.Count);
            Assert.Equal(
                DwSqliteFixture.ColumnNames,
                payload.Columns.Select(value => value.ColumnName));
            Assert.Equal(
                DwSqliteFixture.ColumnNames,
                payload.OriginalValues.Select(value => value.ColumnName));
            Assert.Equal(
                payload.Columns.Select(value => value.ColumnId),
                payload.OriginalValues.Select(value => value.ColumnId));

            // AND THE EDITED CELLS ARRIVE AS TWO DIFFERENT WIRE VALUES, which is the observable proof
            // that both halves survived the projection rather than being written twice from one read.
            foreach (DwSqliteSampleCell cell in sampleRow.Cells.Where(cell => cell.IsEdited))
            {
                ColumnValue currentValue =
                    payload.Columns.Single(value => value.ColumnId == cell.ColumnNumber);
                ColumnValue originalValue =
                    payload.OriginalValues.Single(value => value.ColumnId == cell.ColumnNumber);

                Assert.NotEqual(currentValue.Value, originalValue.Value);
            }
        }
    }

    [Fact]
    public void OneValuePerCellCannotExpressTheContract_WhichIsWhyBuffersIsAnAntiCorruptionLayer()
    {
        // C-K - THE NEGATIVE STATEMENT, MADE EXECUTABLE. This test does not exercise production code;
        // it demonstrates the property that forced the design, so that a future reader considering
        // "why not just send the rows?" finds the answer as a failing premise rather than as prose.
        DwSqliteSampleRow modified = DwSqliteFixture.SampleRows.First(row =>
            row.Buffer == DwBuffer.Primary && row.Status == ItemStatus.DataModified);

        DataWindowBufferStore carrier = DwSqliteFixture.SampleCarrier();

        // A FLAT ROWSET: one value per cell, which is all a result set can carry.
        Dictionary<int, object?> flat = DwSqliteFixture.ColumnNames
            .Select(DwSqliteFixture.ColumnOf)
            .ToDictionary(
                column => column.Id,
                column => carrier.GetItemValue(modified.Row, column.Id, modified.Buffer));

        Assert.Equal(6, flat.Count);

        // THE INFORMATION THE WHERE CLAUSE NEEDS IS SIMPLY NOT IN THERE. For each edited cell the flat
        // rowset holds the value being WRITTEN, so a where clause built from it would compare a column
        // against its own new value - a comparison that matches whatever the row now holds and
        // therefore never detects a concurrent change.
        foreach (DwSqliteSampleCell cell in modified.Cells.Where(cell => cell.IsEdited))
        {
            object? fromFlat = flat[cell.ColumnNumber];
            object? original =
                carrier.GetItemOriginalValue(modified.Row, cell.ColumnNumber, modified.Buffer);

            Assert.True(CarrierValue.AreEquivalent(cell.Current, fromFlat));
            Assert.False(CarrierValue.AreEquivalent(fromFlat, original));
        }

        // WHEREAS THE CARRIER HOLDS BOTH HALVES, which is the difference the anti-corruption layer buys.
        foreach (DwSqliteSampleCell cell in modified.Cells)
        {
            Assert.True(CarrierValue.AreEquivalent(
                cell.Current, carrier.GetItemValue(modified.Row, cell.ColumnNumber, modified.Buffer)));
            Assert.True(CarrierValue.AreEquivalent(
                cell.Original,
                carrier.GetItemOriginalValue(modified.Row, cell.ColumnNumber, modified.Buffer)));
        }
    }

    [Fact]
    public void TheFixturesOwnSettingsPutBothHalvesAndTheRefreshOnTheMainline()
    {
        // C-B - THE FIXTURE EXERCISES THE AWKWARD PATH, NOT THE EASY ONE, and this test says so in one
        // place so the claim is checkable rather than asserted in a comment. dw_sqlite.srd:L14 sets
        // BOTH of the settings that make this contract hard:
        //
        //   updatewhere=1        -> the check spans the key column plus every updateable column's
        //                           ORIGINAL value, so both halves must travel;
        //   updatekeyinplace=no  -> a key change is a DELETE plus an INSERT, which is what makes the
        //                           force-refresh of :L151-L167 mainline rather than a rare branch.
        Assert.Equal(UpdateWhereBuilder.KeyAndUpdatableColumnsMode, DwSqliteFixture.UpdateWhereMode);
        Assert.True(UpdateWhereBuilder.IsKeyAndUpdatableColumnsMode(DwSqliteFixture.UpdateWhereMode));
        Assert.False(DwSqliteFixture.UpdateKeyInPlace);

        // ALL SIX COLUMNS ARE MARKED, read straight off the transcription rather than off the
        // descriptor, so the two are cross-checked against each other.
        Assert.Equal(6, DwSqliteFixture.ColumnCount);
        Assert.All(DwSqliteFixture.Columns, column =>
        {
            Assert.True(column.Update);
            Assert.True(column.UpdateWhereClause);
        });

        // AND THE PREPARE OVER THIS FIXTURE REPORTS THE REFRESH GATE OPEN, because the runtime describe
        // answers `no` [:L155] - the fixture's own setting, not a value this test arranged.
        FakeUpdateTarget target = FixtureTarget();

        Assert.Equal(UpdateWhereBuilder.NoLiteral, target.KeyInPlaceAnswer);

        UpdatePreparer preparer = new(target, target);
        UpdatePreparationResult result =
            preparer.Prepare(DwSqliteFixture.Descriptor(), DwSqliteFixture.SampleCarrier());

        Assert.True(result.IsSucceeded);
        Assert.True(result.KeyChangeRefreshRequired);

        // The key-edited sample row is the one that qualifies, and it does so on the key column.
        DwSqliteSampleRow keyEdited = DwSqliteFixture.SampleRows.Single(row =>
            row.Buffer == DwBuffer.Primary
            && row.Status == ItemStatus.DataModified
            && row.CellAt(DwSqliteFixture.IdColumnNumber).IsEdited);

        Assert.Contains(
            new KeyColumnRefresh(keyEdited.Row, DwSqliteFixture.IdColumnNumber),
            result.KeyChangeRefreshes);
    }

    #endregion

    #region Descriptor shape and argument guards

    [Fact]
    public void Descriptor_DeclaresExactlySixFieldsInTheOraclesOwnOrder()
    {
        // THE POSITIONAL CONSTRUCTOR IS THE ASSERTION. Constructing through it - rather than through
        // Create, which names its parameters - is what pins the ORDER, because a reordered record
        // would either fail to compile here or bind the wrong value to the wrong field.
        //
        // The order is the legacy structure's own [n_cst_thread_task_sqlupdate.sru:L10-L17]:
        //     string  name              -> Name
        //     string  updatablecolumns[] -> UpdatableColumns
        //     string  keycolumns[]       -> KeyColumns
        //     string  identitycolumn     -> IdentityColumn
        //     long    updatewhere        -> UpdateWhere
        //     boolean updatekeyinplace   -> UpdateKeyInPlace
        //
        // C-K: THE ORDER IS CONTRACT, NOT TIDINESS. The published boundary numbers
        // TableUpdateContract's fields 1 through 6 against these same six in this same sequence
        // [shared/PowerFramework.Contracts/Proto/persistence.v1.proto], so reordering the members here
        // would leave the wire numbering describing a different shape from the type it mirrors.
        UpdatableTableDescriptor descriptor = new(
            DwSqliteFixture.UpdateTableName,
            DwSqliteFixture.ColumnNames,
            DwSqliteFixture.KeyColumnNames,
            DwSqliteFixture.IdentityColumnName,
            DwSqliteFixture.UpdateWhereMode,
            DwSqliteFixture.UpdateKeyInPlace);

        Assert.Equal(FixtureTable, descriptor.Name);
        Assert.Equal(FixtureColumns, descriptor.UpdatableColumns);
        Assert.Equal([FixtureKeyColumn], descriptor.KeyColumns);
        Assert.Equal(FixtureKeyColumn, descriptor.IdentityColumn);
        Assert.Equal(UpdateWhereBuilder.KeyAndUpdatableColumnsMode, descriptor.UpdateWhere);
        Assert.False(descriptor.UpdateKeyInPlace);

        // SIX MEMBERS AND NO SEVENTH. Counted off the record's own primary constructor, so adding a
        // member without extending the wire contract fails here rather than at the boundary.
        Assert.Equal(
            6,
            typeof(UpdatableTableDescriptor)
                .GetConstructors()
                .Max(constructor => constructor.GetParameters().Length));

        // And the deconstruction agrees, position for position.
        (string name, IReadOnlyList<string> updatable, IReadOnlyList<string> keys,
            string identity, long? updateWhere, bool? keyInPlace) = descriptor;

        Assert.Equal(FixtureTable, name);
        Assert.Equal(FixtureColumns, updatable);
        Assert.Equal([FixtureKeyColumn], keys);
        Assert.Equal(FixtureKeyColumn, identity);
        Assert.Equal(1L, updateWhere);
        Assert.False(keyInPlace);
    }

    [Fact]
    public void Descriptor_AbsentIsDistinguishableFromDefault_WhichIsProto3ExplicitPresence()
    {
        // C-K - WHY ABSENT-VERSUS-DEFAULT MATTERS ON EXACTLY THESE TWO FIELDS.
        //
        // The oracle writes each of them into the carrier ONLY when it is not null
        // [n_cst_thread_task_sqlupdate.sru:L131-L133, L135-L141], so null carries the distinct meaning
        // "LEAVE THE CARRIER'S OWN SETTING ALONE" - the property line is not emitted at all. That is
        // not an uninitialised accident: the caller-side four-argument overload calls SetNull on BOTH
        // before delegating [n_cst_threading_task_sqlupdate.sru:L227-L234], so absence is a
        // first-class input.
        //
        // A SENTINEL WOULD BE WRONG IN BOTH CASES, and differently wrong in each:
        //   * 0 IS A LEGAL UPDATE-WHERE MODE, so substituting it for absence silently forces a
        //     concurrency mode the caller never asked for.
        //   * false IS THE VALUE THAT TRIGGERS THE KEY-CHANGE REFRESH [:L155], so substituting it for
        //     absence would start rewriting item statuses on a caller that stated nothing.
        //
        // This is one-to-one with proto3 EXPLICIT PRESENCE, which is how the published boundary models
        // it: `optional int64` and `optional bool` on TableUpdateContract, where HasField is the wire
        // counterpart of HasValue here.
        UpdatableTableDescriptor absent = DwSqliteFixture.DescriptorWithAbsentSettings();

        // NULL ROUND-TRIPS AS NULL. Not zero, not false.
        Assert.Null(absent.UpdateWhere);
        Assert.Null(absent.UpdateKeyInPlace);
        Assert.False(absent.UpdateWhere.HasValue);
        Assert.False(absent.UpdateKeyInPlace.HasValue);

        // AND IS DISTINGUISHABLE FROM THE DEFAULT, asserted against the defaults themselves so the
        // statement cannot be read as merely "it is null".
        Assert.NotEqual(0L, absent.UpdateWhere);
        Assert.NotEqual(false, absent.UpdateKeyInPlace);
        Assert.NotEqual(absent.UpdateWhere, (long?)0L);
        Assert.NotEqual(absent.UpdateKeyInPlace, (bool?)false);

        // A DESCRIPTOR THAT STATES THE DEFAULTS IS A DIFFERENT DESCRIPTOR, and the record's own
        // equality says so - which is what makes the distinction observable rather than asserted.
        UpdatableTableDescriptor stated = absent with { UpdateWhere = 0L, UpdateKeyInPlace = false };

        Assert.NotEqual(absent, stated);
        Assert.Equal(0L, stated.UpdateWhere);
        Assert.False(stated.UpdateKeyInPlace);

        // THE OBSERVABLE HALF: absence emits NEITHER property line, while stating the defaults emits
        // BOTH. Both expectations are consumed from the fixture.
        ModificationScriptResult absentScript =
            UpdateWhereBuilder.BuildModificationString(absent, FixtureTarget());

        Assert.Equal(DwSqliteFixture.ExpectedModificationScriptWithAbsentSettings, absentScript.Script);
        Assert.DoesNotContain(UpdateWhereBuilder.UpdateWhereProperty, absentScript.Script, StringComparison.Ordinal);
        Assert.DoesNotContain(UpdateWhereBuilder.UpdateKeyInPlaceProperty, absentScript.Script, StringComparison.Ordinal);

        ModificationScriptResult statedScript =
            UpdateWhereBuilder.BuildModificationString(stated, FixtureTarget());

        Assert.Contains("DataWindow.Table.UpdateWhere = '0'", statedScript.Script, StringComparison.Ordinal);
        Assert.Contains("DataWindow.Table.UpdateKeyinPlace = no", statedScript.Script, StringComparison.Ordinal);
    }

    [Fact]
    public void Descriptor_PreservesTheSixFieldsAndCopiesBothCollections()
    {
        List<string> updatable = ["a", "b"];
        List<string> keys = ["a"];

        UpdatableTableDescriptor descriptor =
            UpdatableTableDescriptor.Create("t", updatable, keys, "a", 1L, true);

        // PowerBuilder arrays are value types, so :L89 and :L90 copy. A later caller mutation must not
        // reach the stored descriptor.
        updatable.Add("c");
        keys.Clear();

        Assert.Equal("t", descriptor.Name);
        Assert.Equal(["a", "b"], descriptor.UpdatableColumns);
        Assert.Equal(["a"], descriptor.KeyColumns);
        Assert.Equal("a", descriptor.IdentityColumn);
        Assert.Equal(1L, descriptor.UpdateWhere);
        Assert.True(descriptor.UpdateKeyInPlace);
        Assert.True(descriptor.IsAcceptable);
        Assert.True(descriptor.HasIdentityColumn);
    }

    [Fact]
    public void Descriptor_NormalisesNullTextToEmptySoTheOraclesOwnValidationDecides()
    {
        UpdatableTableDescriptor descriptor = UpdatableTableDescriptor.Create(
            name: null,
            updatableColumns: ["a"],
            keyColumns: ["a"],
            identityColumn: null,
            updateWhere: null,
            updateKeyInPlace: null);

        Assert.Empty(descriptor.Name);
        Assert.Empty(descriptor.IdentityColumn);

        // A null name is rejected by the same arm that rejects an empty one, rather than throwing.
        Assert.False(descriptor.IsAcceptable);
        Assert.False(descriptor.HasIdentityColumn);
    }

    [Fact]
    public void BuildModificationString_RejectsNullArguments()
    {
        Assert.Throws<ArgumentNullException>(
            () => UpdateWhereBuilder.BuildModificationString(null!, FixtureTarget()));
        Assert.Throws<ArgumentNullException>(
            () => UpdateWhereBuilder.BuildModificationString(FixtureDescriptor(), null!));
        Assert.Throws<ArgumentNullException>(() => UpdateWhereBuilder.MarkedColumnsOf(null!));
    }

    [Fact]
    public void UpdatePreparer_RejectsNullCollaborators()
    {
        FakeUpdateTarget target = FixtureTarget();

        Assert.Throws<ArgumentNullException>(() => new UpdatePreparer(null!, target));
        Assert.Throws<ArgumentNullException>(() => new UpdatePreparer(target, null!));

        UpdatePreparer preparer = new(target, target);

        Assert.Throws<ArgumentNullException>(
            () => preparer.Prepare(null!, new DataWindowBufferStore()));
        Assert.Throws<ArgumentNullException>(() => preparer.Prepare(FixtureDescriptor(), null!));
        Assert.Throws<ArgumentNullException>(() => preparer.ApplyKeyChangeRefresh(null!, new DataWindowBufferStore()));
        Assert.Throws<ArgumentNullException>(() => preparer.ApplyKeyChangeRefresh([1], null!));
        Assert.Throws<ArgumentNullException>(
            () => preparer.PrepareAndUpdate(null!, new DataWindowBufferStore(), () => RetCode.OK, out _));
        Assert.Throws<ArgumentNullException>(
            () => preparer.PrepareAndUpdate(new UpdatableTableCollection(), null!, () => RetCode.OK, out _));
        Assert.Throws<ArgumentNullException>(
            () => preparer.PrepareAndUpdate(new UpdatableTableCollection(), new DataWindowBufferStore(), null!, out _));
    }

    [Fact]
    public void ResultRecords_ReportSuccessThroughThePublishedAlgebra()
    {
        ModificationScriptResult succeeded = ModificationScriptResult.Succeeded("script", [1, 2]);

        Assert.True(succeeded.IsSucceeded);
        Assert.Equal(RetCode.OK, succeeded.Code);
        Assert.Equal("script", succeeded.Script);
        Assert.Equal([1, 2], succeeded.KeyColumnIds);
        Assert.Empty(succeeded.ErrorText);

        ModificationScriptResult failed =
            ModificationScriptResult.Failed(RetCode.E_INTERNAL_ERROR, "text");

        Assert.False(failed.IsSucceeded);
        Assert.Equal("text", failed.ErrorText);
        Assert.Empty(failed.Script);
        Assert.Empty(failed.KeyColumnIds);

        UpdatePreparationResult empty = new();

        Assert.True(empty.IsSucceeded);
        Assert.Empty(empty.Script);
        Assert.Empty(empty.ErrorText);
        Assert.Empty(empty.KeyColumnIds);
        Assert.Empty(empty.KeyChangeRefreshes);
        Assert.False(empty.KeyChangeRefreshRequired);
    }

    [Fact]
    public void KeyColumnRefresh_CarriesTheOneBasedRowAndColumn()
    {
        KeyColumnRefresh refresh = new(7L, 3);

        Assert.Equal(7L, refresh.Row);
        Assert.Equal(3, refresh.ColumnNumber);
        Assert.Equal(new KeyColumnRefresh(7L, 3), refresh);
    }

    #endregion


    #region The identifier grammar - the guard the legacy does not have and this boundary must

    /// <summary>
    /// Every character sequence that could end the assignment it is in, or begin one the caller wrote.
    /// </summary>
    /// <remarks>
    /// EACH CASE IS A DISTINCT MECHANISM RATHER THAN A VARIATION, because a blocklist-shaped guard
    /// passes a list like this and still fails on the character nobody listed. The grammar admits only
    /// segment characters, so these all fall out of one rule - and the matrix is what proves it.
    /// </remarks>
    public static TheoryData<string, string?> RefusedNames =>
        new()
        {
            { "null", null },
            { "empty", "" },
            { "a single space - not a column, and not a table", " " },
            { "leading space", " id" },
            { "trailing space", "id " },
            { "interior space, which would make it two script tokens", "my id" },
            { "a line feed, which is the script's own line separator", "id\nname" },
            { "a carriage return", "id\rname" },
            { "a tab", "id\tname" },
            { "a null character", "id\0name" },
            { "an apostrophe, which closes a quoted value", "id'" },
            { "a double quote", "id\"" },
            { "a backtick", "id`" },
            { "a tilde, PowerScript's escape character", "id~n" },
            { "an equals sign, the assignment operator itself", "id=name" },
            { "a leading dot", ".id" },
            { "a trailing dot", "id." },
            { "a semicolon", "id;" },
            { "a comma, which would make it a list", "id,name" },
            { "a digit first, which is not an identifier", "1id" },
            { "an opening bracket", "id[1]" },
            { "a parenthesis", "count(id)" },
            { "a hyphen", "my-id" },
            { "an asterisk", "*" },
            { "PROPERTY INJECTION - a second assignment that disables concurrency checking", "age\nDataWindow.Table.UpdateWhere = '0'" },
            { "PROPERTY INJECTION - an attribute hijack on a real column", "id.Key = no\nid.Update" },
            { "UPDATE-TABLE REDIRECTION - closes the quote and retargets the update", "COMPANY' \nDataWindow.Table.UpdateTable = 'SALARIES" },
        };

    /// <param name="because">Why the name is refused, so a failure message names the mechanism.</param>
    /// <param name="name">The candidate name.</param>
    [Theory]
    [MemberData(nameof(RefusedNames))]
    public void TheColumnGrammarRefusesAnythingThatIsNotOneIdentifierSegment(string because, string? name)
    {
        Assert.False(UpdateWhereBuilder.IsScriptSafeColumnName(name), because);
    }

    /// <param name="because">Why the name is refused, so a failure message names the mechanism.</param>
    /// <param name="name">The candidate name.</param>
    /// <remarks>
    /// THE TABLE GRAMMAR IS NOT LOOSER EXCEPT IN ONE RESPECT. It admits dots BETWEEN segments so a
    /// schema-qualified name passes, and this theory drives the SAME matrix to prove that is the only
    /// difference - every other refusal above holds for a table name too. The three dot cases in the
    /// matrix are malformed qualification rather than qualification, so they are refused here as well.
    /// </remarks>
    [Theory]
    [MemberData(nameof(RefusedNames))]
    public void TheTableGrammarRefusesTheSameFormsExceptWellFormedQualification(string because, string? name)
    {
        Assert.False(UpdateWhereBuilder.IsScriptSafeTableName(name), because);
    }

    /// <summary>
    /// Well-formed dotted names: refused as a COLUMN, admitted as a TABLE.
    /// </summary>
    /// <remarks>
    /// SEPARATE FROM <see cref="RefusedNames"/> BECAUSE THEY ARE NOT UNIVERSALLY REFUSED, and folding
    /// them in would have forced the table theory to make an exception - which is how a matrix stops
    /// proving anything. Each of these is a legitimate schema-qualified table name AND an attribute
    /// hijack when used as a column: <c>id.Key</c> passed as an updatable column emits
    /// <c>id.Key.Update = yes</c>, aiming the assignment at a property rather than a column.
    /// </remarks>
    public static TheoryData<string> DottedNames =>
        [
            "id.Key",
            "id.Update",
            "id.Identity",
            "dbo.COMPANY",
            "a.b.c",
        ];

    /// <param name="name">The dotted name.</param>
    [Theory]
    [MemberData(nameof(DottedNames))]
    public void ADottedNameIsRefusedAsAColumnAndAdmittedAsATable(string name)
    {
        Assert.False(UpdateWhereBuilder.IsScriptSafeColumnName(name));
        Assert.True(UpdateWhereBuilder.IsScriptSafeTableName(name));
    }

    /// <param name="name">The dotted name.</param>
    /// <remarks>
    /// AND THE ASYMMETRY IS WIRED IN, not merely available on the predicates: the same name that is a
    /// perfectly good table is refused when it arrives in any of the three column positions.
    /// </remarks>
    [Theory]
    [MemberData(nameof(DottedNames))]
    public void ADottedNameIsRefusedInEveryColumnPositionAndAcceptedAsTheTable(string name)
    {
        UpdatableTableCollection tables = new();

        Assert.Equal(
            RetCode.E_INVALID_ARGUMENT,
            tables.AddUpdatableTable(FixtureTable, [name], ["id"], string.Empty));
        Assert.Equal(
            RetCode.E_INVALID_ARGUMENT,
            tables.AddUpdatableTable(FixtureTable, ["age"], [name], string.Empty));
        Assert.Equal(
            RetCode.E_INVALID_ARGUMENT,
            tables.AddUpdatableTable(FixtureTable, ["age"], ["id"], name));
        Assert.Equal(0, tables.UpperBound);

        Assert.Equal(
            RetCode.OK,
            tables.AddUpdatableTable(name, ["age"], ["id"], string.Empty));
        Assert.Equal(name, tables.DescriptorAt(1).Name);
    }

    /// <summary>
    /// Every ordinary name, including the awkward ones, still passes.
    /// </summary>
    /// <remarks>
    /// A GUARD THAT REFUSES LEGITIMATE INPUT IS A BEHAVIOUR CHANGE, so the accepting half of the
    /// grammar is pinned as carefully as the refusing half. The fixture's own six columns are here
    /// because they are the only column names in the repository with evidence behind them
    /// [<c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L8-L13</c>].
    /// </remarks>
    public static TheoryData<string> AdmittedColumnNames =>
        [
            "id",
            "name",
            "age",
            "address",
            "salary",
            "birth",
            "ID",
            "NAME",
            "_internal",
            "updated_at",
            "column1",
            "col_2_b",
            "x$y",
            "temp#1",
            "aVeryLongButPerfectlyOrdinaryColumnNameThatGoesOnForAWhile",
            "\u5217\u540d",
            "n\u00e4me",
        ];

    /// <param name="name">The candidate name.</param>
    [Theory]
    [MemberData(nameof(AdmittedColumnNames))]
    public void TheColumnGrammarAdmitsEveryOrdinaryColumnName(string name)
    {
        Assert.True(UpdateWhereBuilder.IsScriptSafeColumnName(name));

        // AND EVERY COLUMN NAME IS ALSO A LEGAL TABLE NAME, since an unqualified table name is exactly
        // one segment. The reverse does not hold, which the qualification theory below covers.
        Assert.True(UpdateWhereBuilder.IsScriptSafeTableName(name));
    }

    /// <summary>
    /// Schema-qualified table names pass; malformed qualification does not.
    /// </summary>
    public static TheoryData<string, bool> QualifiedTableNames =>
        new()
        {
            { "COMPANY", true },
            { "dbo.COMPANY", true },
            { "SCHEMA.TABLE", true },
            { "server.db.dbo.COMPANY", true },
            { "_s._t", true },
            { "dbo..COMPANY", false },
            { "dbo.", false },
            { ".COMPANY", false },
            { "dbo.1COMPANY", false },
            { "dbo.COMPANY'", false },
            { "dbo. COMPANY", false },
        };

    /// <param name="name">The candidate table name.</param>
    /// <param name="admitted">Whether it should be admitted.</param>
    [Theory]
    [MemberData(nameof(QualifiedTableNames))]
    public void TheTableGrammarAdmitsQualificationAndRefusesMalformedQualification(string name, bool admitted)
    {
        Assert.Equal(admitted, UpdateWhereBuilder.IsScriptSafeTableName(name));
    }

    [Fact]
    public void AQualifiedNameIsATableNameAndNeverAColumnName()
    {
        // THE ASYMMETRY, STATED DIRECTLY. A table name lands inside a QUOTED value where a dot is
        // ordinary text; a column name lands UNQUOTED as the assignment target, where a dot chooses the
        // attribute. Admitting dots for both would reopen exactly the injection the column rule closes.
        Assert.True(UpdateWhereBuilder.IsScriptSafeTableName("dbo.COMPANY"));
        Assert.False(UpdateWhereBuilder.IsScriptSafeColumnName("dbo.COMPANY"));
    }

    /// <summary>
    /// The four positions a name occupies, each refused at the admission boundary.
    /// </summary>
    /// <remarks>
    /// DRIVEN THROUGH AddUpdatableTable RATHER THAN THROUGH THE PREDICATE, so this asserts the guard is
    /// actually WIRED IN at each of the four positions rather than merely available. A guard the front
    /// door does not call is not a guard.
    /// </remarks>
    public static TheoryData<string, string, string[], string[], string> InjectedPositions =>
        new()
        {
            {
                "the table name - update-table redirection",
                "COMPANY' \nDataWindow.Table.UpdateTable = 'SALARIES",
                ["age"],
                ["id"],
                "id"
            },
            {
                "an updatable column - property injection disabling the concurrency check",
                FixtureTable,
                ["age\nDataWindow.Table.UpdateWhere = '0'"],
                ["id"],
                "id"
            },
            {
                "a key column - attribute hijack",
                FixtureTable,
                ["age"],
                ["id.Key = no\nage"],
                "id"
            },
            {
                "the identity column",
                FixtureTable,
                ["age"],
                ["id"],
                "id\nDataWindow.Table.UpdateKeyinPlace = yes"
            },
        };

    /// <param name="position">Which position carries the injection.</param>
    /// <param name="table">The table name.</param>
    /// <param name="updatable">The updatable columns.</param>
    /// <param name="keys">The key columns.</param>
    /// <param name="identity">The identity column.</param>
    [Theory]
    [MemberData(nameof(InjectedPositions))]
    public void TheAdmissionBoundaryRefusesAnInjectionInAnyOfTheFourPositions(
        string position,
        string table,
        string[] updatable,
        string[] keys,
        string identity)
    {
        UpdatableTableCollection tables = new();

        long code = tables.AddUpdatableTable(table, updatable, keys, identity);

        Assert.Equal(RetCode.E_INVALID_ARGUMENT, code);
        Assert.Equal(0, tables.UpperBound);
        Assert.False(tables.MultiTableUpdate, position);

        // AND THE SIX-ARGUMENT FORM REFUSES IT TOO, since the four-argument form delegates to it and a
        // caller stating both optional settings must not find a wider door.
        Assert.Equal(
            RetCode.E_INVALID_ARGUMENT,
            tables.AddUpdatableTable(table, updatable, keys, identity, 1L, false));
        Assert.Equal(0, tables.UpperBound);
    }

    /// <param name="position">Which position carries the injection.</param>
    /// <param name="table">The table name.</param>
    /// <param name="updatable">The updatable columns.</param>
    /// <param name="keys">The key columns.</param>
    /// <param name="identity">The identity column.</param>
    /// <remarks>
    /// THE SECOND DOOR. A descriptor can be built through Create or a <c>with</c> expression without
    /// ever passing the admission boundary - the descriptor's own remarks acknowledge both as open - so
    /// the builder is fail-closed independently. This drives the identical matrix straight into
    /// <c>BuildModificationString</c> to prove it.
    /// </remarks>
    [Theory]
    [MemberData(nameof(InjectedPositions))]
    public void TheBuilderRefusesAnInjectionEvenWhenTheDescriptorBypassedTheBoundary(
        string position,
        string table,
        string[] updatable,
        string[] keys,
        string identity)
    {
        UpdatableTableDescriptor descriptor = UpdatableTableDescriptor.Create(
            table,
            updatable,
            keys,
            identity,
            updateWhere: null,
            updateKeyInPlace: null);

        FakeUpdateTarget target = FixtureTarget();

        // Make every key column resolvable so a refusal cannot be mistaken for the oracle's own
        // non-positive-identifier failure at :L119.
        foreach (string key in keys)
        {
            target.ColumnIds[key + UpdateWhereBuilder.ColumnIdSuffix] = 1;
        }

        ModificationScriptResult result =
            UpdateWhereBuilder.BuildModificationString(descriptor, target);

        Assert.False(result.IsSucceeded, position);
        Assert.Equal(RetCode.E_INTERNAL_ERROR, result.Code);

        // NOTHING THE CALLER WROTE REACHED THE SCRIPT. The failed result carries an empty script, so
        // there is no half-built text for a caller to hand to Modify by mistake.
        Assert.Equal(string.Empty, result.Script);
    }

    [Fact]
    public void AnUnsafeKeyColumnNameNeverReachesDescribe()
    {
        // DESCRIBE TAKES A SPACE-SEPARATED LIST OF PROPERTY REQUESTS, so an unsafe key column name
        // arrives at the carrier as SEVERAL requests and the number this step reads would be the answer
        // to a request the caller composed. Guarding after the describe would already be too late,
        // which is why the guard sits above it - and why this test asserts on the recorded requests
        // rather than only on the result.
        UpdatableTableDescriptor descriptor = UpdatableTableDescriptor.Create(
            FixtureTable,
            ["age"],
            ["id .Update"],
            identityColumn: string.Empty,
            updateWhere: null,
            updateKeyInPlace: null);

        FakeUpdateTarget target = FixtureTarget();

        ModificationScriptResult result =
            UpdateWhereBuilder.BuildModificationString(descriptor, target);

        Assert.False(result.IsSucceeded);
        Assert.Empty(target.ColumnIdRequests);
        Assert.Equal(
            UpdateWhereBuilder.InvalidColumnNameMessage + "id .Update",
            result.ErrorText);
    }

    [Fact]
    public void AKeyColumnFailureIsReportedBeforeATableNameFailure()
    {
        // PRECEDENCE, AND WHY IT IS DELIBERATE. The table check sits at step 7 rather than before step 1
        // so that a descriptor which is bad in two ways still reports the KEY COLUMN - the oracle's own
        // diagnostic at :L119 - rather than being pre-empted by a diagnostic the oracle does not have.
        UpdatableTableDescriptor descriptor = UpdatableTableDescriptor.Create(
            "COMPANY'",
            ["age"],
            ["id\nname"],
            identityColumn: string.Empty,
            updateWhere: null,
            updateKeyInPlace: null);

        ModificationScriptResult result =
            UpdateWhereBuilder.BuildModificationString(descriptor, FixtureTarget());

        Assert.False(result.IsSucceeded);
        Assert.StartsWith(
            UpdateWhereBuilder.InvalidColumnNameMessage,
            result.ErrorText,
            StringComparison.Ordinal);
        Assert.NotEqual(UpdateWhereBuilder.UnsafeUpdateTableMessage, result.ErrorText);
    }

    [Fact]
    public void TheGrammarChangesNothingForTheFixtureItself()
    {
        // THE REGRESSION STATEMENT. Refusing is not rewriting: for every input the legacy could actually
        // have been given, the generated script is byte-identical to what it was before the guard
        // existed. The fixture descriptor is that input, and the byte-exact parity region above pins its
        // full text - this asserts only that the guard admits it, so a future tightening of the grammar
        // fails here loudly rather than silently changing the fixture's behaviour.
        UpdatableTableDescriptor descriptor = FixtureDescriptor();

        Assert.True(descriptor.IsAcceptable);
        Assert.True(descriptor.IsScriptSafe);

        ModificationScriptResult result =
            UpdateWhereBuilder.BuildModificationString(descriptor, FixtureTarget());

        Assert.True(result.IsSucceeded);
        Assert.EndsWith(
            "DataWindow.Table.UpdateTable = 'COMPANY'",
            result.Script,
            StringComparison.Ordinal);
    }

    [Fact]
    public void IsScriptSafeTestsAllFourPositionsAndNotJustTheFirstItFinds()
    {
        // A SHORT-CIRCUITING CONJUNCTION IS ONLY CORRECT IF EVERY CLAUSE IS REACHED WHEN THE EARLIER
        // ONES PASS. This walks the four positions one at a time with the other three known good, which
        // is the only shape that catches a missing clause.
        Assert.False(
            UpdatableTableDescriptor.Create("bad name", ["age"], ["id"], "", null, null).IsScriptSafe);
        Assert.False(
            UpdatableTableDescriptor.Create(FixtureTable, ["age", "bad name"], ["id"], "", null, null)
                .IsScriptSafe);
        Assert.False(
            UpdatableTableDescriptor.Create(FixtureTable, ["age"], ["id", "bad name"], "", null, null)
                .IsScriptSafe);
        Assert.False(
            UpdatableTableDescriptor.Create(FixtureTable, ["age"], ["id"], "bad name", null, null)
                .IsScriptSafe);

        // And all four good together pass, so the predicate is not simply always false.
        Assert.True(
            UpdatableTableDescriptor.Create(FixtureTable, ["age"], ["id"], "id", null, null)
                .IsScriptSafe);
    }

    #endregion


    private static int CountOccurrences(string text, string value)
    {
        int count = 0;
        int index = text.IndexOf(value, StringComparison.Ordinal);

        while (index >= 0)
        {
            count++;
            index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal);
        }

        return count;
    }
}
