// ==============================================================================================
//  IdentityColumnResolverTests.cs - THE IDENTITY ROUND-TRIP PARITY MATRICES
//  --------------------------------------------------------------------------------------------
//  UNDER TEST     services/persistence-service/PowerFramework.Persistence/Concurrency/
//                     IdentityColumnResolver.cs
//  ORACLE         ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru      (READ ONLY)
//                 ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlupdate.sru   (READ ONLY)
//  FIXTURE        ws_objects/pfw.tests.pbl.src/dw_sqlite.srd                            (READ ONLY)
//
//  THE HEADLINE ASSERTION IN THIS FILE IS AN ORDER ASSERTION, NOT A COUNT ASSERTION. The refactor
//  plan names the Filter buffer's backward collection loop
//  [n_cst_thread_task_sqlupdate.sru:L237] the single most dangerous line in the whole migration,
//  precisely because reversing it keeps the COUNT of collected values identical while pairing every
//  value with the WRONG ROW. A count-only assertion therefore cannot detect the regression, so FOUR
//  tests assert the Filter ORDER as a SEQUENCE and none of them may be weakened into a count, a set
//  or a sorted comparison:
//    * FilterValuesAreCollectedInDescendingRowOrder            - the direction, on staged rows
//    * BackwardCollectionRoundTripsThroughAForwardApplier      - the direction composed with its
//                                                                forward-applying counterpart
//    * TheFixtureRoundTripResolvesColumnOneAndReportsTheFilterArrayInverted
//                                                              - the direction in the ORACLE'S OWN
//                                                                data, which reads 7 then 6
//    * TheOrderedCallLogShowsTheArityAndTheWalkDirectionTogether
//                                                              - the direction and the accessor
//                                                                arity off one ordered call log
//  ReversingTheFixturesFilterSequenceIsUndetectableByCountOrContentAlone states the reason in
//  executable form: the reversed sequence has the same length and the same contents.
//
//  C-B / C-K - THE BACKWARD WALK IS PRESERVED LEGACY BEHAVIOUR WITH A DOCUMENTED REASON, AND IT IS
//  HALF OF A MATCHED PAIR:
//    * COLLECT BACKWARD, here. The oracle's own comment one line above the loop
//      [n_cst_thread_task_sqlupdate.sru:L235] states that the Filter buffer's data order is INVERTED
//      relative to the data source that called GetChanges. The loop is not a defect; it is the
//      compensation for that inversion.
//    * APPLY FORWARD, elsewhere. The write-back half walks BOTH buffers ASCENDING through
//      GetNextModified and consumes each array from its first element onward - see
//      PowerFramework.Persistence/Tasks/TaskProxies/SqlUpdateTaskProxy.cs (ApplyBothBuffers /
//      ApplyBuffer), mirroring n_cst_threading_task_sqlupdate.sru:L120-L205, and asserted in the
//      sibling SqlUpdateTaskProxyTests.cs.
//  Collect-backward composed with apply-forward is what lands each value on the row it belongs to.
//  NEITHER HALF MAKES SENSE ALONE: "correcting" this file's direction in isolation yields wrong
//  identity values that every count assertion in the suite would still accept.
//
//  C-K - AAP SECTION 0.4.5.4, THE ONE-BASED TRANSLATION RULE, AND WHY THIS UNIT IS ITS MOST
//  IMPORTANT SUBJECT. The plan requires that every ported loop either go through a CENTRALISED
//  one-based indexing helper or be INDIVIDUALLY AUDITED, because PowerBuilder arrays are one-based
//  and its upper-bound function answers the LAST VALID INDEX. The subject uses the helper
//  (OneBasedIndex.Range / UpperBound / EmptyUpperBound / ToZeroBased) and the named row and column
//  sentinels (ItemStatusMachine.FirstRowNumber / FirstColumnNumber / RowStatusColumn); this file is
//  the audit, and the "R9 - the one-based audit" region below is where it is written down. Its
//  reverse-iteration case is the one the plan singles out, so the tests here address rows in the
//  ONE-BASED domain throughout and let the fake perform the single rebasing.
//
//  NO DATABASE AND NO DataWindow IS INVOLVED ANYWHERE IN THIS FILE. Both surfaces the subject reads
//  through are injected and are faked by RecordingIdentitySource below, which records every
//  interaction so the tests can assert not just the answers but WHICH questions were asked, in what
//  order, and THROUGH WHICH OVERLOAD.
//
//  THE FIXTURE IS TEST DATA, NOT PRODUCTION DATA. COMPANY, its six columns and their one-based ids
//  appear here because dw_sqlite.srd is the sole updatable DataWindow in the repository; the
//  production code under test names no table, no column and no column count.
// ==============================================================================================

using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using Google.Protobuf;
using PowerFramework.Persistence.Concurrency;

namespace PowerFramework.Persistence.Tests;

/// <summary>
/// One row of a faked buffer: its own status, read the legacy way at column index zero, and the
/// numeric value its identity column holds.
/// </summary>
/// <param name="Status">The ROW's status, which the row guard tests against <c>NewModified!</c>.</param>
/// <param name="Value">
/// The identity column's value. <see langword="null"/> models a null item, which the subject must
/// preserve rather than coerce to zero.
/// </param>
internal readonly record struct FakeIdentityRow(ItemStatus Status, long? Value);

/// <summary>
/// One recorded status read: the row, the column index it was addressed by, and the buffer.
/// </summary>
internal readonly record struct StatusRead(long Row, int ColumnIndex, DwBuffer Buffer);

/// <summary>
/// One recorded read through the TWO-ARGUMENT numeric accessor.
/// </summary>
internal readonly record struct TwoArgumentValueRead(long Row, int ColumnNumber);

/// <summary>
/// One recorded read through the FOUR-ARGUMENT numeric accessor, carrying the two arguments the
/// short overload does not have.
/// </summary>
internal readonly record struct FourArgumentValueRead(
    long Row,
    int ColumnNumber,
    DwBuffer Buffer,
    bool OriginalValue);

/// <summary>
/// A fake implementing BOTH injected surfaces on one type - which is what the legacy actually is, a
/// single <c>Data</c> carrier - and recording every interaction.
/// </summary>
/// <remarks>
/// Rows are held in zero-based lists whose element at index <c>n</c> is ONE-BASED ROW <c>n + 1</c>.
/// That conversion happens HERE, in the test double, and deliberately nowhere in the subject: it is
/// the boundary between the legacy one-based row domain and C# storage, and keeping it in one visible
/// place is what makes the R9 assertions below meaningful.
/// </remarks>
internal sealed class RecordingIdentitySource : IIdentityColumnMetadata, IIdentityValueSource
{
    /// <summary>The answer to the update-table describe.</summary>
    internal string UpdateTable { get; set; } = string.Empty;

    /// <summary>The answer to the column-count describe. Zero models a describe that failed.</summary>
    internal int ColumnCount { get; set; }

    /// <summary>
    /// Identity answers keyed by the FULL describe property - <c>#1.Identity</c>, not <c>1</c> -
    /// because the caller composes the property. An absent key answers <c>"no"</c>.
    /// </summary>
    internal Dictionary<string, string> IdentityAnswers { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Database-name answers keyed by the FULL describe property - <c>#1.DBName</c>. An absent key
    /// answers the empty string.
    /// </summary>
    internal Dictionary<string, string> DbNameAnswers { get; } = new(StringComparer.Ordinal);

    /// <summary>The inserted count, which is also the precondition at <c>:L215</c>.</summary>
    internal long InsertedCount { get; set; }

    /// <summary>The updated count.</summary>
    internal long UpdatedCount { get; set; }

    /// <summary>The deleted count.</summary>
    internal long DeletedCount { get; set; }

    /// <summary>The Primary buffer's rows, element <c>n</c> being one-based row <c>n + 1</c>.</summary>
    internal List<FakeIdentityRow> PrimaryRows { get; } = [];

    /// <summary>The Filter buffer's rows, element <c>n</c> being one-based row <c>n + 1</c>.</summary>
    internal List<FakeIdentityRow> FilterRows { get; } = [];

    /// <summary>Every describe property asked for, in order - the evidence that a stage ran at all.</summary>
    internal List<string> DescribeReads { get; } = [];

    /// <summary>Every status read, in order, with the column index it was addressed by.</summary>
    internal List<StatusRead> StatusReads { get; } = [];

    /// <summary>Every read through the two-argument accessor, in order.</summary>
    internal List<TwoArgumentValueRead> TwoArgumentValueReads { get; } = [];

    /// <summary>Every read through the four-argument accessor, in order.</summary>
    internal List<FourArgumentValueRead> FourArgumentValueReads { get; } = [];

    /// <summary>How many times the inserted count was read. The oracle reads it TWICE.</summary>
    internal int InsertedCountReads { get; private set; }

    public string DescribeUpdateTable()
    {
        DescribeReads.Add(UpdateWhereBuilder.UpdateTableProperty);

        return UpdateTable;
    }

    public int GetColumnCount()
    {
        DescribeReads.Add(UpdateWhereBuilder.ColumnCountProperty);

        return ColumnCount;
    }

    public string DescribeColumnIdentity(string identityProperty)
    {
        DescribeReads.Add(identityProperty);

        return IdentityAnswers.TryGetValue(identityProperty, out string? answer)
            ? answer
            : UpdateWhereBuilder.NoLiteral;
    }

    public string DescribeColumnDbName(string dbNameProperty)
    {
        DescribeReads.Add(dbNameProperty);

        return DbNameAnswers.TryGetValue(dbNameProperty, out string? answer)
            ? answer
            : string.Empty;
    }

    public long GetInsertedCount()
    {
        InsertedCountReads++;

        return InsertedCount;
    }

    public long GetUpdatedCount() => UpdatedCount;

    public long GetDeletedCount() => DeletedCount;

    public long RowCount() => PrimaryRows.Count;

    public long FilteredCount() => FilterRows.Count;

    public ItemStatus GetItemStatus(long row, int columnIndex, DwBuffer buffer)
    {
        StatusReads.Add(new StatusRead(row, columnIndex, buffer));

        return RowsOf(buffer)[ToZeroBased(row, buffer)].Status;
    }

    public long? GetItemNumber(long row, int columnNumber)
    {
        TwoArgumentValueReads.Add(new TwoArgumentValueRead(row, columnNumber));

        // The two-argument form means "the Primary buffer's CURRENT value", which is exactly what
        // PowerBuilder's argument defaulting does.
        return PrimaryRows[ToZeroBased(row, DwBuffer.Primary)].Value;
    }

    public long? GetItemNumber(long row, int columnNumber, DwBuffer buffer, bool originalValue)
    {
        FourArgumentValueReads.Add(
            new FourArgumentValueRead(row, columnNumber, buffer, originalValue));

        return RowsOf(buffer)[ToZeroBased(row, buffer)].Value;
    }

    private List<FakeIdentityRow> RowsOf(DwBuffer buffer) =>
        buffer switch
        {
            DwBuffer.Primary => PrimaryRows,
            DwBuffer.Filter => FilterRows,
            _ => throw new ArgumentOutOfRangeException(
                nameof(buffer),
                buffer,
                "This fake models the Primary and Filter buffers only, which are the two the "
                    + "identity round trip reads."),
        };

    /// <summary>
    /// Converts a ONE-BASED legacy row number into this fake's zero-based storage index, throwing
    /// when the subject addresses a row outside the buffer.
    /// </summary>
    /// <remarks>
    /// THE THROW IS THE POINT. It turns any one-based off-by-one in the subject - a row 0, or a row
    /// <c>count + 1</c> - into an immediate, loud failure instead of a silently shifted result.
    /// </remarks>
    private int ToZeroBased(long row, DwBuffer buffer)
    {
        List<FakeIdentityRow> rows = RowsOf(buffer);

        if (row < ItemStatusMachine.FirstRowNumber || row > rows.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(row),
                row,
                $"Row numbers are ONE-BASED: {buffer} holds rows "
                    + $"{ItemStatusMachine.FirstRowNumber} through {rows.Count}. A row of 0 or of "
                    + "count + 1 means the subject rebased a one-based row number.");
        }

        return (int)(row - ItemStatusMachine.FirstRowNumber);
    }
}

/// <summary>
/// The discovery matrix, the two collection directions with the backward-collection round trip, the
/// three-level conditional gating, the emit guard, the accessor-arity asymmetry, the column-zero
/// status guard, the counts placement and the R9 one-based audit.
/// </summary>
public sealed class IdentityColumnResolverTests
{
    #region The sole evidenced fixture, CONSUMED from DwSqliteFixture rather than re-transcribed

    // ==========================================================================================
    //  C-C - THE FIXTURE IS READ, NOT COPIED.
    //  ----------------------------------------------------------------------------------------
    //  Every fixture fact below resolves to DwSqliteFixture, which is the single transcription of
    //  ws_objects/pfw.tests.pbl.src/dw_sqlite.srd in this project. Re-spelling "COMPANY" or the six
    //  column names here would create a second transcription that could drift from the .srd without
    //  any test failing - and the one fact this whole file turns on, that every dbname is BARE
    //  [dw_sqlite.srd:L8-L13], is exactly the kind of detail a drifting copy loses.
    // ==========================================================================================

    /// <summary>
    /// The update table name the discovery prefix is derived from
    /// [<c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L14</c>], read from the fixture.
    /// </summary>
    private static string FixtureTable => DwSqliteFixture.UpdateTableName;

    /// <summary>
    /// The six column names in declaration order, whose ids are therefore 1..6
    /// [<c>dw_sqlite.srd:L8-L13, L21-L26</c>]. Only <c>id</c> carries
    /// <c>key=yes identity=yes</c> [<c>:L8</c>].
    /// </summary>
    private static IReadOnlyList<string> FixtureColumns => DwSqliteFixture.ColumnNames;

    /// <summary>
    /// A recording source preloaded with the fixture's own describe answers: six columns, <c>id</c>
    /// the sole identity column, and every database name the UNQUALIFIED spelling the definition
    /// declares [<c>:L8-L13</c>].
    /// </summary>
    /// <returns>The preloaded recording source.</returns>
    /// <remarks>
    /// THE ANSWERS COME FROM THE FIXTURE'S MAPS, WHICH ARE THEMSELVES KEYED BY THE COMPOSED DESCRIBE
    /// PROPERTY - so this source answers precisely the questions the subject asks, and a change to the
    /// property vocabulary cannot leave the two disagreeing silently.
    /// </remarks>
    private static RecordingIdentitySource FixtureSource()
    {
        RecordingIdentitySource source = new()
        {
            UpdateTable = FixtureTable,
            ColumnCount = DwSqliteFixture.ColumnCount,
        };

        foreach ((string property, string answer) in DwSqliteFixture.IdentityDescribeAnswers)
        {
            source.IdentityAnswers[property] = answer;
        }

        foreach ((string property, string answer) in DwSqliteFixture.DbNameDescribeAnswers)
        {
            source.DbNameAnswers[property] = answer;
        }

        return source;
    }

    private static void MarkIdentity(RecordingIdentitySource source, int ordinal, string dbName)
    {
        source.IdentityAnswers[IdentityColumnResolver.DescribeIdentityProperty(ordinal)] =
            UpdateWhereBuilder.YesLiteral;
        source.DbNameAnswers[IdentityColumnResolver.DescribeDbNameProperty(ordinal)] = dbName;
    }

    private static FakeIdentityRow NewRow(long? value) =>
        new(ItemStatus.NewModified, value);

    #endregion

    #region Discovery - the full selection-ordering matrix

    // ==========================================================================================
    //  THE SELECTION RULE, STATED TWICE SO IT CANNOT BE MISREAD.
    //  ----------------------------------------------------------------------------------------
    //      sColDBNamePrefix = Lower(sUpdateTable) + "."                                  [:L217]
    //      for nIndex = 1 to Column.Count                                                [:L219]
    //          if Describe("#N.Identity") = "yes" then                                   [:L220]
    //              if Lower(Left(Describe("#N.DBName"),Len(prefix))) = prefix
    //                 or nIdentityColumn = 0 then                                        [:L221]
    //                  nIdentityColumn = nIndex                                          [:L222]
    //
    //  READING ONE - a PREFIX MATCH assigns UNCONDITIONALLY, so it may overwrite an earlier pick:
    //                THE LAST PREFIX-MATCHING IDENTITY COLUMN WINS.
    //  READING TWO - a NON-MATCHING column assigns only while nothing has been chosen:
    //                THE FIRST IDENTITY COLUMN IS THE FALLBACK.
    //
    //  Both readings describe the SAME code. It is neither plain "first wins" nor plain "last wins",
    //  and the matrix below asserts the BEHAVIOUR rather than either paraphrase - which is why it
    //  includes the two cases that separate them: a bare column after a qualified one must NOT take
    //  over, and a qualified column after a bare one MUST.
    // ==========================================================================================

    /// <summary>
    /// The complete discovery matrix: one case per reachable path through the two nested conditions,
    /// each declared as a whole column list so the ORDER of the columns - which is what the rule turns
    /// on - is visible in the case itself.
    /// </summary>
    /// <remarks>
    /// <para>
    /// EVERY PARAMETER IS A PRIMITIVE OR AN ARRAY OF PRIMITIVES, deliberately: xUnit serializes theory
    /// data so each case is discovered, named and re-run individually, and a bespoke case type would
    /// forfeit that. The two arrays are parallel and INDEX <c>n</c> IS ONE-BASED COLUMN <c>n + 1</c>,
    /// which is the same single rebasing point the recording fake uses (R9).
    /// </para>
    /// <para>
    /// The database names are deliberately mixed in case, because both sides of the comparison are
    /// lower-cased [<c>:L217, :L221</c>] and a one-sided implementation would pass a same-case matrix.
    /// </para>
    /// <para>
    /// C-C - THE COLUMN LISTS HERE ARE SYNTHETIC PROBES, NOT A TRANSCRIPTION. They deliberately contain
    /// shapes the sole evidenced DataWindow does not - two identity columns, qualified database names, a
    /// name that merely contains the qualifier - because the selection rule has arms the fixture cannot
    /// reach. The FIXTURE'S OWN shape is asserted separately and read from
    /// <c>DwSqliteFixture</c>: see <c>DiscoveryOnTheFixtureTakesColumnOneThroughTheFallbackArm</c> and
    /// the principal case <c>TheFixtureRoundTripResolvesColumnOneAndReportsTheFilterArrayInverted</c>.
    /// Nothing in this matrix claims to describe <c>dw_sqlite.srd</c>.
    /// </para>
    /// </remarks>
    public static TheoryData<string, string, string[], string[], int> SelectionRuleMatrix
    {
        get
        {
            const string yes = UpdateWhereBuilder.YesLiteral;
            const string no = UpdateWhereBuilder.NoLiteral;

            return new TheoryData<string, string, string[], string[], int>
            {
                // ---------- the seven cases the folder requirements enumerate ----------

                // 1. One identity column carrying the qualifier: taken by the PREFIX arm [:L221].
                {
                    "one qualified identity column",
                    "COMPANY",
                    [no, no, no, yes],
                    ["id", "name", "age", "COMPANY.id"],
                    4
                },

                // 2. One identity column with a BARE name: taken by the FALLBACK arm `or ... = 0`.
                //    This is the fixture's own shape - see the dedicated test below.
                {
                    "one bare identity column",
                    "COMPANY",
                    [no, no, yes],
                    ["id", "name", "age"],
                    3
                },

                // 3. TWO qualified identity columns: the LATER one wins, because the prefix arm
                //    assigns unconditionally and therefore overwrites [:L222].
                {
                    "two qualified identity columns - the later wins",
                    "COMPANY",
                    [no, yes, no, no, yes],
                    ["id", "company.id", "age", "address", "COMPANY.SALARY"],
                    5
                },

                // 4. ★ THE CASE THAT SEPARATES THE TWO READINGS. The FIRST column is qualified and
                //    the SECOND is bare: the bare one CANNOT overwrite, because its arm only fires
                //    while nothing has been chosen [:L221]. A "last identity column wins"
                //    implementation answers 5 here and passes every other case in this matrix.
                {
                    "qualified first, bare second - the qualified first wins",
                    "COMPANY",
                    [no, yes, no, no, yes],
                    ["id", "company.id", "age", "address", "salary"],
                    2
                },

                // 5. The mirror of case 4: bare FIRST, qualified SECOND, so the prefix arm overwrites
                //    the fallback pick. A "first identity column wins" implementation answers 2 here.
                {
                    "bare first, qualified second - the qualified second wins",
                    "COMPANY",
                    [no, yes, no, no, yes],
                    ["id", "id", "age", "address", "company.salary"],
                    5
                },

                // 6. No identity column at all: nothing is resolved, which gates every stage below
                //    discovery off entirely [:L226].
                {
                    "no identity column at all",
                    "COMPANY",
                    [no, no, no, no, no, no],
                    ["id", "name", "age", "address", "salary", "birth"],
                    IdentityColumnResolver.NoIdentityColumn
                },

                // 7. A column that MATCHES THE PREFIX but is NOT an identity column is ignored
                //    entirely: the identity test is the OUTER condition [:L220], so the prefix test
                //    is never even reached for it.
                {
                    "a qualified column that is not an identity column is ignored",
                    "COMPANY",
                    [no, no, no, no, no, no],
                    ["company.id", "company.name", "age", "address", "salary", "birth"],
                    IdentityColumnResolver.NoIdentityColumn
                },

                // ---------- the fallback arm's own ordering ----------

                // Two BARE identity columns: the FIRST is kept, because the second's arm requires
                //  `nIdentityColumn = 0` and it is no longer zero [:L221].
                {
                    "two bare identity columns - the first is kept",
                    "COMPANY",
                    [no, yes, no, no, yes],
                    ["id", "id", "age", "address", "salary"],
                    2
                },

                // ---------- the SHAPE of the comparison: a Left()-length prefix test ----------

                // NOT A Contains. The qualifier appears INSIDE this name but not AT ITS START, so the
                //  truncation cannot equal it and the bare column 2 keeps the pick. A Contains-based
                //  implementation answers 5.
                {
                    "a name containing the qualifier but not starting with it does not match",
                    "COMPANY",
                    [no, yes, no, no, yes],
                    ["id", "id", "age", "address", "xcompany.salary"],
                    2
                },

                // NOT A FULL-NAME EQUALITY. This name is LONGER than the qualifier and still matches,
                //  because only the first Len(prefix) characters are compared [:L221]. An equality
                //  against the whole name answers 2.
                {
                    "a longer qualified name still matches on its first characters",
                    "COMPANY",
                    [no, yes, no, no, yes],
                    ["id", "id", "age", "address", "company.salary_amount"],
                    5
                },

                // THE QUALIFIER EXACTLY, with nothing after it, also matches - the boundary between
                //  the two readings above.
                {
                    "the bare qualifier itself matches",
                    "COMPANY",
                    [no, yes, no, no, yes],
                    ["id", "id", "age", "address", "company."],
                    5
                },

                // A NAME SHORTER THAN THE QUALIFIER cannot match, even though it is a prefix OF the
                //  qualifier: PowerScript's Left answers the whole short string [:L221 via LegacyLeft],
                //  and "company" is not "company.". A StartsWith test in the other direction answers 5.
                {
                    "a name shorter than the qualifier does not match",
                    "COMPANY",
                    [no, yes, no, no, yes],
                    ["id", "id", "age", "address", "company"],
                    2
                },

                // THE SAME LENGTH but a different final character: the dot is part of the comparison.
                {
                    "a same-length name differing in the separator does not match",
                    "COMPANY",
                    [no, yes, no, no, yes],
                    ["id", "id", "age", "address", "company_salary"],
                    2
                },

                // ---------- both sides are lower-cased ----------

                // THE TABLE SIDE is lower-cased [:L217]: a mixed-case update table still matches a
                //  lower-case qualified name.
                {
                    "the update table name is lower-cased before comparison",
                    "CoMpAnY",
                    [no, yes, no, no, yes],
                    ["id", "id", "age", "address", "company.salary"],
                    5
                },

                // THE COLUMN SIDE is lower-cased [:L221]: an upper-case qualified name still matches a
                //  lower-case update table.
                {
                    "the column database name is lower-cased before comparison",
                    "company",
                    [no, yes, no, no, yes],
                    ["id", "id", "age", "address", "COMPANY.SALARY"],
                    5
                },
            };
        }
    }

    /// <summary>
    /// ★ THE SELECTION RULE, ASSERTED AS BEHAVIOUR. Each case declares a whole column list and the
    /// ONE-BASED ordinal the oracle's scan arrives at [<c>:L219-L225</c>].
    /// </summary>
    /// <param name="description">The case's description, which names it in the test output.</param>
    /// <param name="updateTable">The update table the qualifier is derived from [<c>:L217</c>].</param>
    /// <param name="identityAnswers">
    /// What each column answers to <c>#n.Identity</c>, index <c>n</c> being one-based column
    /// <c>n + 1</c>.
    /// </param>
    /// <param name="dbNames">What each column answers to <c>#n.DBName</c>, in the same order.</param>
    /// <param name="expectedColumnId">
    /// The expected one-based column id, or <see cref="IdentityColumnResolver.NoIdentityColumn"/>.
    /// </param>
    [Theory]
    [MemberData(nameof(SelectionRuleMatrix))]
    public void DiscoveryAppliesThePrefixOverrideOverAFirstFoundFallback(
        string description,
        string updateTable,
        string[] identityAnswers,
        string[] dbNames,
        int expectedColumnId)
    {
        Assert.False(string.IsNullOrWhiteSpace(description));

        // A malformed case would silently assert the wrong thing, so the two parallel arrays are
        // required to agree before anything is exercised.
        Assert.Equal(identityAnswers.Length, dbNames.Length);

        RecordingIdentitySource source = new()
        {
            UpdateTable = updateTable,
            ColumnCount = identityAnswers.Length,
        };

        // R9 - the ONE rebasing point: index n of the declared list is one-based column n + 1.
        foreach (int ordinal in OneBasedIndex.Range(identityAnswers.Length))
        {
            int slot = OneBasedIndex.ToZeroBased(ordinal, identityAnswers.Length, nameof(ordinal));

            source.IdentityAnswers[IdentityColumnResolver.DescribeIdentityProperty(ordinal)] =
                identityAnswers[slot];
            source.DbNameAnswers[IdentityColumnResolver.DescribeDbNameProperty(ordinal)] =
                dbNames[slot];
        }

        Assert.Equal(expectedColumnId, IdentityColumnResolver.DiscoverIdentityColumn(source));
    }

    /// <summary>
    /// C-B - THE COMPARISON IS A PREFIX TEST OF EXACTLY THE QUALIFIER'S LENGTH, and this states that
    /// directly against the two helpers the scan is built from rather than through a whole scan.
    /// </summary>
    /// <remarks>
    /// The matrix above proves the CONSEQUENCES - that a containing name loses and a longer qualified
    /// name wins. This proves the MECHANISM, so a reader can see that the rule is
    /// <c>Lower(Left(name, Len(prefix))) = prefix</c> [<c>:L221</c>] and not a substring search, a
    /// culture-aware comparison, or an equality against the whole name.
    /// </remarks>
    [Fact]
    public void ThePrefixTestTruncatesToTheQualifierLengthAndComparesLowerCasedOnBothSides()
    {
        string prefix = IdentityColumnResolver.BuildColumnDbNamePrefix("CoMpAnY");

        // The table side is already lower-cased by the builder, so the qualifier itself is the yardstick.
        Assert.Equal("company.", prefix);

        // A qualified name of ANY length beyond the qualifier matches on its first characters only.
        Assert.Equal(
            prefix,
            IdentityColumnResolver.LegacyLeft("COMPANY.SALARY_AMOUNT", prefix.Length)
                .ToLowerInvariant());

        // A name that merely CONTAINS the qualifier does not, because the truncation starts at the
        // beginning - which is the difference between this test and a Contains.
        Assert.Contains(prefix, "xcompany.salary", StringComparison.Ordinal);
        Assert.NotEqual(
            prefix,
            IdentityColumnResolver.LegacyLeft("xcompany.salary", prefix.Length).ToLowerInvariant());

        // A name SHORTER than the qualifier cannot match even when it is a prefix of it, because
        // PowerScript's Left answers the whole short string rather than padding it.
        Assert.StartsWith("company", prefix, StringComparison.Ordinal);
        Assert.NotEqual(
            prefix,
            IdentityColumnResolver.LegacyLeft("COMPANY", prefix.Length).ToLowerInvariant());
    }

    /// <summary>
    /// C-B - the identity test is an ORDINAL string comparison against the literal <c>"yes"</c>
    /// [<c>:L220</c>], so a differently cased or non-boolean answer fails it and the column is
    /// skipped. Widening this would change the candidate set and so could change which column wins.
    /// </summary>
    [Theory]
    [InlineData("Yes")]
    [InlineData("YES")]
    [InlineData("no")]
    [InlineData("!")]
    [InlineData("?")]
    [InlineData("")]
    [InlineData("true")]
    public void DiscoveryAcceptsOnlyTheExactLowerCaseYesLiteral(string answer)
    {
        RecordingIdentitySource source = new() { UpdateTable = FixtureTable, ColumnCount = 6 };
        source.IdentityAnswers[IdentityColumnResolver.DescribeIdentityProperty(2)] = answer;
        source.DbNameAnswers[IdentityColumnResolver.DescribeDbNameProperty(2)] = "COMPANY.id";

        Assert.Equal(
            IdentityColumnResolver.NoIdentityColumn,
            IdentityColumnResolver.DiscoverIdentityColumn(source));
    }

    /// <summary>
    /// A column count of zero - which is what the <c>Long()</c> coercion answers for a failed
    /// describe - makes the scan visit nothing, matching <c>for nIndex = 1 to 0</c> [<c>:L219</c>].
    /// </summary>
    [Fact]
    public void DiscoveryVisitsNothingWhenTheColumnCountIsZero()
    {
        RecordingIdentitySource source = new() { UpdateTable = FixtureTable, ColumnCount = 0 };
        MarkIdentity(source, 1, "COMPANY.id");

        Assert.Equal(
            IdentityColumnResolver.NoIdentityColumn,
            IdentityColumnResolver.DiscoverIdentityColumn(source));

        // The update table and the column count are read [:L217-L218]; no per-column property is.
        Assert.Equal(
            [UpdateWhereBuilder.UpdateTableProperty, UpdateWhereBuilder.ColumnCountProperty],
            source.DescribeReads);
    }

    /// <summary>
    /// FIXTURE PARITY, AND THE FINDING THAT MAKES THE FALLBACK ARM THE MEASURED PATH. The sole
    /// updatable DataWindow in the repository declares <c>update="COMPANY"</c>
    /// [<c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L14</c>] and gives its identity column the
    /// UNQUALIFIED database name <c>id</c> [<c>:L8</c>]. <c>Left("id", 8)</c> is <c>"id"</c>, which
    /// does not equal <c>"company."</c>, so the prefix arm FAILS on the fixture and column 1 is taken
    /// by the fallback. The fallback is therefore the exercised path, not a rare branch.
    /// </summary>
    [Fact]
    public void DiscoveryOnTheFixtureTakesColumnOneThroughTheFallbackArm()
    {
        RecordingIdentitySource source = FixtureSource();

        Assert.Equal(1, IdentityColumnResolver.DiscoverIdentityColumn(source));

        // Prove it really was the fallback: the prefix does not match the fixture's database name.
        string prefix = IdentityColumnResolver.BuildColumnDbNamePrefix(FixtureTable);
        Assert.NotEqual(
            prefix,
            IdentityColumnResolver.LegacyLeft("id", prefix.Length).ToLowerInvariant());
    }

    /// <summary>
    /// R9 - every describe property the scan composes addresses a ONE-BASED ordinal within
    /// <c>1..count</c>: no <c>#0</c> and no <c>#7</c> for a six-column definition [<c>:L219</c>].
    /// </summary>
    [Fact]
    public void DiscoveryComposesOnlyOneBasedOrdinalsWithinTheColumnCount()
    {
        RecordingIdentitySource source = FixtureSource();

        IdentityColumnResolver.DiscoverIdentityColumn(source);

        List<string> expected =
        [
            UpdateWhereBuilder.UpdateTableProperty,
            UpdateWhereBuilder.ColumnCountProperty,
        ];

        foreach (int ordinal in OneBasedIndex.Range(DwSqliteFixture.ColumnCount))
        {
            expected.Add(IdentityColumnResolver.DescribeIdentityProperty(ordinal));

            // C-B - ONLY THE MARKED COLUMN REACHES THE DATABASE-NAME READ. The oracle's identity test
            // is the OUTER `if` [:L220] and the prefix test the INNER one [:L221], so a column that
            // answers anything but "yes" is skipped entirely and its #n.DBName is never asked for.
            // The expectation is built from the fixture's own answers rather than from a hardcoded
            // ordinal, so it follows the .srd if the identity column ever moves.
            if (string.Equals(
                    DwSqliteFixture.IdentityDescribeAnswers[
                        IdentityColumnResolver.DescribeIdentityProperty(ordinal)],
                    UpdateWhereBuilder.YesLiteral,
                    StringComparison.Ordinal))
            {
                expected.Add(IdentityColumnResolver.DescribeDbNameProperty(ordinal));
            }
        }

        Assert.Equal(expected, source.DescribeReads);

        // R9 - neither the ordinal BELOW the first column nor the one PAST the last is ever composed,
        // which are the two shapes a rebased loop would produce. Both are spelled from the production
        // vocabulary rather than as literals so they track the describe grammar.
        Assert.DoesNotContain(
            IdentityPropertyOf(ItemStatusMachine.FirstColumnNumber - 1),
            source.DescribeReads);
        Assert.DoesNotContain(
            IdentityPropertyOf(DwSqliteFixture.ColumnCount + 1),
            source.DescribeReads);
    }

    /// <summary>
    /// Composes an identity describe property for an ordinal the production helper refuses, so the
    /// tests can assert that such a property is NEVER asked for.
    /// </summary>
    /// <param name="ordinal">The ordinal, which may legitimately be out of range here.</param>
    /// <returns>The property string.</returns>
    /// <remarks>
    /// <see cref="IdentityColumnResolver.DescribeIdentityProperty"/> throws below the first column, and
    /// rightly so - which is exactly why a negative assertion cannot call it. This helper spells the
    /// same grammar from the same two constants instead of hardcoding <c>"#0.Identity"</c>.
    /// </remarks>
    private static string IdentityPropertyOf(int ordinal) =>
        UpdateWhereBuilder.ColumnOrdinalPrefix
            + ordinal.ToString(CultureInfo.InvariantCulture)
            + UpdateWhereBuilder.IdentityAttributeSuffix;

    #endregion

    #region ★ THE PRINCIPAL CASE - the whole round trip over the fixture's own rows

    // ==========================================================================================
    //  THE GOLDEN MASTER, DRIVEN THROUGH THE FIXTURE'S OWN SEAMS.
    //  ----------------------------------------------------------------------------------------
    //  Every other test in this file stages rows to isolate one behaviour. This one stages nothing:
    //  it hands the subject DwSqliteTargetMetadata and DwSqliteSampleRowSource - the fixture's own
    //  implementations of the two injected surfaces - and asserts the payload the sole updatable
    //  DataWindow in the repository produces.
    //
    //  THREE FACTS OF THE FIXTURE MAKE THIS THE CASE THAT MATTERS MOST:
    //    1. Its identity column's database name is the BARE "id" [dw_sqlite.srd:L8], so the ordinal is
    //       reached through the FALLBACK arm. The prefix arm is the EXCEPTIONAL path here, not the
    //       normal one, and a reader who assumes otherwise misreads the whole unit.
    //    2. Its Filter buffer holds its two newly-inserted rows in the INVERTED order
    //       [n_cst_thread_task_sqlupdate.sru:L235], so the expected sequence reads 7 THEN 6 - the
    //       backward walk's signature, in the fixture's own data rather than in staged data.
    //    3. Its counts are not all equal, so an argument-order transposition at :L247 is visible.
    // ==========================================================================================

    /// <summary>
    /// ★ The complete identity round trip over <c>dw_sqlite.srd</c>: the fallback-resolved ordinal,
    /// the Primary array in source order, the Filter array in INVERTED order, and the counts triple.
    /// </summary>
    [Fact]
    public void TheFixtureRoundTripResolvesColumnOneAndReportsTheFilterArrayInverted()
    {
        DwSqliteTargetMetadata metadata = new();
        DwSqliteSampleRowSource values = new();

        IdentityResolutionOutcome outcome = IdentityColumnResolver.Resolve(metadata, values);

        ResolvedIdentityColumnData payload = Assert.Single(outcome.Identity);

        // 1. THE ORDINAL, reached through the fallback arm because every dbname is bare [:L8-L13].
        Assert.Equal(
            DwSqliteFixture.ExpectedDiscoveredIdentityColumnNumber,
            payload.IdentityColumnId);
        Assert.Equal(DwSqliteFixture.IdColumnNumber, (int)payload.IdentityColumnId);

        // The identity column really is the one the definition names, and it really is column one.
        Assert.Equal(
            DwSqliteFixture.IdentityColumnName,
            FixtureColumns[
                OneBasedIndex.ToZeroBased(
                    DwSqliteFixture.IdColumnNumber,
                    DwSqliteFixture.ColumnCount,
                    nameof(DwSqliteFixture.IdColumnNumber))]);

        // 2. THE TWO ARRAYS, as SEQUENCES. The Filter array is the inverted one, and asserting it as an
        //    ordered sequence is the only assertion that can fail when the walk direction is flipped.
        Assert.Equal(DwSqliteFixture.ExpectedPrimaryIdentityValues, payload.PrimaryValues);
        Assert.Equal(DwSqliteFixture.ExpectedFilterIdentityValues, payload.FilterValues);

        // Spelled out, so the inversion is legible without opening the fixture: the Filter buffer's
        // row 1 holds id 6 and its row 2 holds id 7, and the BACKWARD walk therefore reports 7 first.
        Assert.Equal<long?>([7L, 6L], payload.FilterValues);
        Assert.True(
            payload.FilterValues.Count > 1,
            "A single-element Filter array could not distinguish the two walk directions, so the "
                + "fixture must keep more than one newly-inserted filtered row.");

        // 3. THE COUNTS, on the success path regardless of the identity payload [:L247].
        Assert.Equal(DwSqliteFixture.SampleRowCounts, outcome.Counts);

        // No null is involved on this fixture: `id` is INTEGER PRIMARY KEY AUTOINCREMENT, so the
        // database always assigns it [w_test_sqlite.srw:L463-L469].
        Assert.False(payload.ContainsNullValue);
    }

    /// <summary>
    /// ★ THE FALSIFICATION CONTROL FOR THE PRINCIPAL CASE. Reversing the fixture's expected Filter
    /// sequence keeps its LENGTH and its CONTENTS identical while changing which row each value belongs
    /// to - so a suite that asserted a count, a length or a set would pass against the reversed walk.
    /// </summary>
    /// <remarks>
    /// This test asserts a property of the EXPECTATIONS rather than of the subject, and that is
    /// deliberate: it is the executable form of the refactor plan's warning that "correcting" the
    /// backward walk at <c>n_cst_thread_task_sqlupdate.sru:L237</c> produces wrong identity values that
    /// a row-count assertion cannot catch. If the fixture ever degenerated to one filtered row, or to
    /// two rows sharing an id, the guard below would fail and say so instead of letting the headline
    /// assertion quietly lose its power.
    /// </remarks>
    [Fact]
    public void ReversingTheFixturesFilterSequenceIsUndetectableByCountOrContentAlone()
    {
        IReadOnlyList<long?> expected = DwSqliteFixture.ExpectedFilterIdentityValues;
        long?[] reversed = [.. expected.Reverse()];

        // A COUNT assertion cannot tell them apart.
        Assert.Equal(expected.Count, reversed.Length);

        // Neither can a SET or an order-insensitive comparison. C-B - THIS IS THE ONLY SORTED
        // COMPARISON IN THE FILE AND IT IS DELIBERATE: it is applied to the two EXPECTATIONS to
        // demonstrate that sorting hides the defect, never to a value the subject produced. A sorted
        // or set-based assertion on a subject result would be the exact violation this test warns of.
        Assert.Equal(expected.Order(), reversed.Order());

        // Only the ORDER does - which is why every Filter assertion in this file is a sequence.
        Assert.NotEqual(expected, reversed);
    }

    #endregion


    #region Collection direction - the headline regression guards

    /// <summary>
    /// The Primary buffer is collected ASCENDING [<c>:L228-L233</c>], so the resulting sequence is in
    /// increasing row order.
    /// </summary>
    [Fact]
    public void PrimaryValuesAreCollectedInAscendingRowOrder()
    {
        RecordingIdentitySource source = FixtureSource();

        // Rows 1..4, of which 1, 3 and 4 are NewModified! and row 2 is not.
        source.PrimaryRows.AddRange(
        [
            NewRow(101L),
            new FakeIdentityRow(ItemStatus.DataModified, 999L),
            NewRow(103L),
            NewRow(104L),
        ]);

        IReadOnlyList<long?> collected = IdentityColumnResolver.CollectPrimaryValues(source, 1);

        Assert.Equal<long?>([101L, 103L, 104L], collected);
    }

    /// <summary>
    /// ★★ THE SINGLE MOST IMPORTANT TEST IN THIS FILE. The Filter buffer is collected DESCENDING
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru:L237</c>], because the
    /// buffer's row order is INVERTED relative to the data source that produced the changeset - which
    /// the oracle states in its own comment one line above, at [<c>:L235</c>].
    /// </summary>
    /// <remarks>
    /// WHY THIS ASSERTS THE ORDER AND NOT THE COUNT, AND WHY IT MUST STAY THAT WAY. Reversing the
    /// subject's loop would return the SAME THREE VALUES and therefore the SAME COUNT, while pairing
    /// every one of them with the wrong row once the write-back applies them forward. A count
    /// assertion, a set comparison or an <c>OrderBy</c> anywhere in this test would silently accept
    /// that regression. If a future edit makes this test fail, the correct response is to restore the
    /// descending loop, NOT to relax the assertion.
    /// </remarks>
    [Fact]
    public void FilterValuesAreCollectedInDescendingRowOrder()
    {
        RecordingIdentitySource source = FixtureSource();

        // Rows 1..5 of the Filter buffer; rows 2, 3 and 5 qualify. Values are deliberately chosen so
        // that the value encodes its own row number: row n holds 200 + n.
        source.FilterRows.AddRange(
        [
            new FakeIdentityRow(ItemStatus.NotModified, 201L),
            NewRow(202L),
            NewRow(203L),
            new FakeIdentityRow(ItemStatus.DataModified, 204L),
            NewRow(205L),
        ]);

        IReadOnlyList<long?> collected = IdentityColumnResolver.CollectFilterValues(source, 1);

        // DESCENDING: row 5 first, then row 3, then row 2.
        Assert.Equal<long?>([205L, 203L, 202L], collected);

        // And the rows really were visited high to low, which is the property the values alone could
        // not prove if two rows happened to share a value.
        Assert.Equal(
            [5L, 4L, 3L, 2L, 1L],
            source.StatusReads.Where(read => read.Buffer == DwBuffer.Filter)
                .Select(read => read.Row));
    }

    /// <summary>
    /// ★★ THE ROUND-TRIP PROOF: COLLECT BACKWARD + APPLY FORWARD PUTS EVERY VALUE ON THE ROW THE
    /// ORACLE WOULD HAVE PUT IT ON, and reversing the collection corrupts the assignment while
    /// leaving the count untouched.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The applier stub below is the shape of the write-back half
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlupdate.sru:L151-L165</c>]: it
    /// walks the Filter buffer's qualifying rows ASCENDING, consuming the collected list from its
    /// first element onward. It is a stub on purpose - the real applier is
    /// <c>PowerFramework.Persistence/Tasks/TaskProxies/SqlUpdateTaskProxy.cs</c>'s
    /// <c>ApplyBothBuffers</c>, which is asserted by the sibling <c>SqlUpdateTaskProxyTests.cs</c> -
    /// and it exists here only to close the argument that the backward collection is correct. COLLECT
    /// BACKWARD AND APPLY FORWARD IS ONE MECHANISM SPLIT ACROSS TWO FILES; this test is the half that
    /// shows the two compose.
    /// </para>
    /// <para>
    /// The second half of the test is the negative control. It feeds the REVERSED collection through
    /// the same forward applier and asserts that the number of assignments is IDENTICAL while the
    /// pairing is wrong. That is the concrete demonstration of why a row-count assertion cannot catch
    /// this defect, and it is why the positive assertion above compares sequences.
    /// </para>
    /// </remarks>
    [Fact]
    public void BackwardCollectionRoundTripsThroughAForwardApplier()
    {
        RecordingIdentitySource source = FixtureSource();

        // The data source that produced the changeset held its filtered rows in the opposite order,
        // so the row that is LAST here is the one whose identity belongs FIRST on the far side.
        source.FilterRows.AddRange([NewRow(301L), NewRow(302L), NewRow(303L)]);

        IReadOnlyList<long?> collected = IdentityColumnResolver.CollectFilterValues(source, 1);
        Assert.Equal<long?>([303L, 302L, 301L], collected);

        Dictionary<long, long?> applied = ApplyForward(source.FilterRows, collected);

        // Row 1 receives the FIRST collected element, which came from row 3, and so on. Every value
        // lands on the row the inversion says it belongs to.
        Assert.Equal<long?>(303L, applied[1L]);
        Assert.Equal<long?>(302L, applied[2L]);
        Assert.Equal<long?>(301L, applied[3L]);

        // NEGATIVE CONTROL: the same count, an entirely wrong pairing.
        IReadOnlyList<long?> reversed = [.. collected.Reverse()];
        Dictionary<long, long?> misapplied = ApplyForward(source.FilterRows, reversed);

        Assert.Equal(applied.Count, misapplied.Count);
        Assert.NotEqual(applied[1L], misapplied[1L]);
        Assert.NotEqual(applied[3L], misapplied[3L]);
    }

    /// <summary>
    /// The write-back half's traversal shape, reduced to what this argument needs: visit the
    /// qualifying rows ASCENDING and consume the collected values in order.
    /// </summary>
    private static Dictionary<long, long?> ApplyForward(
        List<FakeIdentityRow> rows,
        IReadOnlyList<long?> values)
    {
        Dictionary<long, long?> applied = [];
        int next = 0;

        for (long row = ItemStatusMachine.FirstRowNumber; row <= rows.Count; row++)
        {
            if (!ItemStatusMachine.IsNewRow(rows[(int)(row - ItemStatusMachine.FirstRowNumber)].Status))
            {
                continue;
            }

            // `if i > n then exit` [n_cst_threading_task_sqlupdate.sru:L158] - more qualifying rows
            // than collected values bails out rather than indexing past the end.
            if (next >= values.Count)
            {
                break;
            }

            applied[row] = values[next];
            next++;
        }

        return applied;
    }

    #endregion

    #region The three asymmetries - arity, buffer and count

    /// <summary>
    /// C-B - asymmetry 2 of 3. Primary values are read through the TWO-ARGUMENT accessor
    /// [<c>:L231</c>] and Filter values through the FOUR-ARGUMENT one naming
    /// <see cref="DwBuffer.Filter"/> and the original-value flag <see langword="false"/>
    /// [<c>:L239</c>]. Proven through the recording fake, because the two overloads are the only
    /// evidence a consumer has of which form the port issues.
    /// </summary>
    [Fact]
    public void PrimaryUsesTheTwoArgumentAccessorAndFilterUsesTheFourArgumentOne()
    {
        RecordingIdentitySource source = FixtureSource();
        source.InsertedCount = 2;
        source.PrimaryRows.AddRange([NewRow(401L), NewRow(402L)]);
        source.FilterRows.AddRange([NewRow(501L)]);

        IdentityResolutionOutcome outcome = IdentityColumnResolver.Resolve(source, source);

        // ONE call of the per-table resolve fires the callback at most once [:L243], so exactly one
        // ordered block - the N-block case belongs to ResolveTables.
        _ = Assert.Single(outcome.Identity);

        // Exactly two short-form reads, both naming the discovered one-based column and no buffer.
        Assert.Equal(
            [new TwoArgumentValueRead(1L, 1), new TwoArgumentValueRead(2L, 1)],
            source.TwoArgumentValueReads);

        // Exactly one long-form read, naming Filter and the CURRENT value.
        FourArgumentValueRead filterRead = Assert.Single(source.FourArgumentValueReads);
        Assert.Equal(new FourArgumentValueRead(1L, 1, DwBuffer.Filter, false), filterRead);
    }

    /// <summary>
    /// ★ ARITY AND DIRECTION IN ONE ORDERED TRACE, taken through the shared carrier double so the two
    /// properties are read off a SINGLE call log rather than from two separate lists.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>FakeDataWindowCarrier</c> is a real <c>DataWindowBufferStore</c> that records every numeric
    /// read as an ordered <c>RecordedValueRead</c> carrying the call's sequence number, its row, its
    /// buffer, the original-value flag AND whether the FOUR-ARGUMENT overload was used. That last field
    /// is what makes this test possible: the two overloads are otherwise indistinguishable from their
    /// results, and the oracle uses a different one per buffer -
    /// <c>GetItemNumber(nIndex,nIdentityColumn)</c> for Primary [<c>:L231</c>] and
    /// <c>GetItemNumber(nIndex,nIdentityColumn,Filter!,false)</c> for Filter [<c>:L239</c>]. The
    /// distinction matters beyond tidiness: only the long form can name a buffer or ask for an ORIGINAL
    /// value, which is why <c>sqlitegetitemdouble.srf</c> wraps the long form rather than the short one.
    /// </para>
    /// <para>
    /// C-E - NO DATABASE IS INVOLVED. The carrier is an in-memory buffer store; nothing here opens a
    /// connection or touches a file.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheOrderedCallLogShowsTheArityAndTheWalkDirectionTogether()
    {
        FakeDataWindowCarrier carrier = new();

        // Two newly-inserted Primary rows and three newly-inserted Filter rows. Each value encodes its
        // own row number, so a misread row is visible in the value as well as in the log.
        foreach (long value in (long[])[11L, 12L])
        {
            long row = carrier.SeedRetrievedRow(
                DwBuffer.Primary,
                (DwSqliteFixture.IdColumnNumber, value));
            carrier.SetRowStatus(row, DwBuffer.Primary, ItemStatus.NewModified);
        }

        foreach (long value in (long[])[21L, 22L, 23L])
        {
            long row = carrier.SeedRetrievedRow(
                DwBuffer.Filter,
                (DwSqliteFixture.IdColumnNumber, value));
            carrier.SetRowStatus(row, DwBuffer.Filter, ItemStatus.NewModified);
        }

        // Seeding writes values, and those writes are not numeric READS - but clearing the log makes
        // the trace below unambiguously the subject's own.
        carrier.ClearValueReads();
        carrier.RowsInserted = 5L;
        carrier.RowsUpdated = 1L;
        carrier.RowsDeleted = 0L;

        IdentityResolutionOutcome outcome = IdentityColumnResolver.Resolve(FixtureSource(), carrier);

        ResolvedIdentityColumnData payload = Assert.Single(outcome.Identity);
        Assert.Equal<long?>([11L, 12L], payload.PrimaryValues);

        // DESCENDING, because the Filter buffer is walked backward [:L237].
        Assert.Equal<long?>([23L, 22L, 21L], payload.FilterValues);

        IReadOnlyList<RecordedValueRead> log = carrier.ValueReads;

        // The log is the CALL ORDER: five reads, sequence numbers 1..5 with no gaps.
        Assert.Equal([1, 2, 3, 4, 5], log.Select(static read => read.Ordinal));

        // PRIMARY FIRST, ASCENDING, THROUGH THE TWO-ARGUMENT FORM.
        Assert.Equal(
            [(DwBuffer.Primary, 1L, false), (DwBuffer.Primary, 2L, false)],
            log.Take(2).Select(static read => (read.Buffer, read.Row, read.FourArgument)));

        // FILTER SECOND, DESCENDING, THROUGH THE FOUR-ARGUMENT FORM.
        Assert.Equal(
            [(DwBuffer.Filter, 3L, true), (DwBuffer.Filter, 2L, true), (DwBuffer.Filter, 1L, true)],
            log.Skip(2).Select(static read => (read.Buffer, read.Row, read.FourArgument)));

        // Every read names the discovered identity column, and NOT ONE asks for an ORIGINAL value: the
        // oracle passes the original-value flag as false [:L239] because the identity value it wants is
        // the one the database just assigned, which exists only in the current value.
        Assert.All(
            log,
            read =>
            {
                Assert.Equal(DwSqliteFixture.IdColumnNumber, read.ColumnNumber);
                Assert.False(read.OriginalValue);
            });
        Assert.Equal(0, carrier.OriginalValueReadCount);

        // The counts triple travels alongside, in the oracle's own argument order [:L247].
        Assert.Equal(new UpdateRowCounts(5L, 1L, 0L), outcome.Counts);
    }


    /// <summary>
    /// C-B - asymmetry 3 of 3. The Filter scan is bounded by the FILTERED count and the Primary scan
    /// by the ROW count [<c>:L228</c> then <c>:L236</c>], and each reads only its own buffer's status
    /// [<c>:L230</c>, <c>:L238</c>]. Buffers of different sizes prove the two bounds are not shared.
    /// </summary>
    [Fact]
    public void EachScanIsBoundedByItsOwnBufferCountAndReadsOnlyItsOwnBuffer()
    {
        RecordingIdentitySource source = FixtureSource();
        source.InsertedCount = 1;
        source.PrimaryRows.AddRange([NewRow(1L), NewRow(2L)]);
        source.FilterRows.AddRange([NewRow(3L), NewRow(4L), NewRow(5L), NewRow(6L)]);

        IdentityColumnResolver.Resolve(source, source);

        Assert.Equal(
            [1L, 2L],
            source.StatusReads.Where(read => read.Buffer == DwBuffer.Primary)
                .Select(read => read.Row));

        Assert.Equal(
            [4L, 3L, 2L, 1L],
            source.StatusReads.Where(read => read.Buffer == DwBuffer.Filter)
                .Select(read => read.Row));

        // No third buffer is ever touched: Delete! plays no part in the identity round trip.
        Assert.DoesNotContain(DwBuffer.Delete, source.StatusReads.Select(read => read.Buffer));
    }

    #endregion

    #region The column-zero row-status guard

    /// <summary>
    /// C-B - both scans select rows by the ROW's own status, addressed at column index zero
    /// [<c>:L230</c>, <c>:L238</c>], and the equality is against <c>NewModified!</c> ALONE, so
    /// <see cref="ItemStatus.New"/>, <see cref="ItemStatus.DataModified"/> and
    /// <see cref="ItemStatus.NotModified"/> rows are all excluded from both buffers.
    /// </summary>
    [Fact]
    public void OnlyNewModifiedRowsAreCollectedFromEitherBuffer()
    {
        RecordingIdentitySource source = FixtureSource();

        source.PrimaryRows.AddRange(
        [
            new FakeIdentityRow(ItemStatus.NotModified, 11L),
            new FakeIdentityRow(ItemStatus.DataModified, 12L),
            new FakeIdentityRow(ItemStatus.New, 13L),
            NewRow(14L),
        ]);
        // THE SAME FOUR STATUSES IN THE FILTER BUFFER, so the guard is shown to be identical on both
        // sides rather than inferred from the Primary scan. DataModified! and NotModified! are the two
        // the folder requirements name explicitly, and New! - inserted but not yet edited - is the
        // third near miss, since only NewModified! satisfies the equality at [:L230, :L238].
        source.FilterRows.AddRange(
        [
            new FakeIdentityRow(ItemStatus.New, 21L),
            new FakeIdentityRow(ItemStatus.NotModified, 22L),
            new FakeIdentityRow(ItemStatus.DataModified, 23L),
            NewRow(24L),
        ]);

        Assert.Equal<long?>([14L], IdentityColumnResolver.CollectPrimaryValues(source, 1));

        // Row 4 is the only qualifying Filter row, so the descending walk yields it alone.
        Assert.Equal<long?>([24L], IdentityColumnResolver.CollectFilterValues(source, 1));

        // And the three near misses really were VISITED and REJECTED rather than never reached: the
        // scan reads every row's status in both buffers and filters on the value, exactly as the
        // oracle's `if` inside the loop does.
        Assert.Equal(
            [1L, 2L, 3L, 4L],
            source.StatusReads.Where(read => read.Buffer == DwBuffer.Primary)
                .Select(read => read.Row));
        Assert.Equal(
            [4L, 3L, 2L, 1L],
            source.StatusReads.Where(read => read.Buffer == DwBuffer.Filter)
                .Select(read => read.Row));
    }

    /// <summary>
    /// EVERY status read addresses the ROW rather than a column, and NO BARE ZERO is handed to a
    /// column-status accessor: each recorded index satisfies
    /// <see cref="ItemStatusMachine.AddressesRowStatus"/> and equals the named sentinel
    /// <see cref="ItemStatusMachine.RowStatusColumn"/>.
    /// </summary>
    [Fact]
    public void EveryStatusReadAddressesTheRowThroughTheNamedSentinel()
    {
        RecordingIdentitySource source = FixtureSource();
        source.InsertedCount = 3;
        source.PrimaryRows.AddRange([NewRow(1L), NewRow(2L)]);
        source.FilterRows.AddRange([NewRow(3L)]);

        IdentityColumnResolver.Resolve(source, source);

        Assert.NotEmpty(source.StatusReads);
        Assert.All(
            source.StatusReads,
            read =>
            {
                Assert.Equal(ItemStatusMachine.RowStatusColumn, read.ColumnIndex);
                Assert.True(ItemStatusMachine.AddressesRowStatus(read.ColumnIndex));
                Assert.False(ItemStatusMachine.TryGetColumnNumber(read.ColumnIndex, out _));
            });
    }

    #endregion

    #region The three conditional levels

    /// <summary>
    /// LEVEL 1 - an inserted count of zero short-circuits the ENTIRE identity block
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru:L215</c>]: no describe is
    /// issued, no status is read, no value is read, and no payload is produced - YET THE COUNTS ARE
    /// STILL REPORTED [<c>:L247</c>].
    /// </summary>
    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void AZeroInsertedCountSkipsDiscoveryAndCollectionButStillReportsCounts(long inserted)
    {
        RecordingIdentitySource source = FixtureSource();
        source.InsertedCount = inserted;
        source.UpdatedCount = 7L;
        source.DeletedCount = 3L;
        source.PrimaryRows.AddRange([NewRow(1L)]);
        source.FilterRows.AddRange([NewRow(2L)]);

        IdentityResolutionOutcome outcome = IdentityColumnResolver.Resolve(source, source);

        Assert.Empty(outcome.Identity);
        Assert.Empty(source.DescribeReads);
        Assert.Empty(source.StatusReads);
        Assert.Empty(source.TwoArgumentValueReads);
        Assert.Empty(source.FourArgumentValueReads);

        Assert.Equal(new UpdateRowCounts(inserted, 7L, 3L), outcome.Counts);
    }

    /// <summary>
    /// LEVEL 2 - rows were inserted but NO identity column exists [<c>:L226</c>], so discovery runs
    /// and collection does not. The counts are still reported.
    /// </summary>
    [Fact]
    public void AMissingIdentityColumnSkipsCollectionButStillReportsCounts()
    {
        RecordingIdentitySource source = new()
        {
            UpdateTable = FixtureTable,
            ColumnCount = 6,
            InsertedCount = 4L,
            UpdatedCount = 1L,
            DeletedCount = 2L,
        };
        source.PrimaryRows.AddRange([NewRow(1L)]);

        IdentityResolutionOutcome outcome = IdentityColumnResolver.Resolve(source, source);

        Assert.Empty(outcome.Identity);
        Assert.NotEmpty(source.DescribeReads);
        Assert.Empty(source.StatusReads);
        Assert.Equal(new UpdateRowCounts(4L, 1L, 2L), outcome.Counts);
    }

    /// <summary>
    /// C-B - the inserted count is read TWICE, once for the precondition [<c>:L215</c>] and once for
    /// the counts triple [<c>:L247</c>], and it is deliberately not hoisted into a single read.
    /// </summary>
    [Fact]
    public void TheInsertedCountIsReadTwicePerResolve()
    {
        RecordingIdentitySource source = FixtureSource();
        source.InsertedCount = 1L;
        source.PrimaryRows.AddRange([NewRow(1L)]);

        IdentityColumnResolver.Resolve(source, source);

        Assert.Equal(2, source.InsertedCountReads);
    }

    #endregion

    #region The emit guard

    /// <summary>
    /// The four reachable combinations of the guard's two operands
    /// <c>UpperBound(primary) &gt; 0 or UpperBound(filter) &gt; 0</c> [<c>:L242</c>], with whether the
    /// identity callback fires.
    /// </summary>
    /// <remarks>
    /// THE GUARD IS AN <c>OR</c>, SO ONLY ONE OF THE FOUR IS SILENT. Stating all four as one matrix is
    /// what makes the operator itself the subject: an <c>AND</c> would answer <see langword="false"/>
    /// for the two single-sided cases, and an unguarded emit would answer <see langword="true"/> for
    /// the empty one.
    /// </remarks>
    public static TheoryData<string, int, int, bool> EmitGuardMatrix =>
        new()
        {
            { "primary only", 2, 0, true },
            { "filter only", 0, 3, true },
            { "both buffers", 2, 3, true },
            { "neither buffer", 0, 0, false },
        };

    /// <summary>
    /// LEVEL 3 - the identity callback fires when AT LEAST ONE array is non-empty, and stays silent
    /// only when both are empty [<c>:L242-L244</c>].
    /// </summary>
    /// <param name="description">The case's description, which names it in the test output.</param>
    /// <param name="newPrimaryRows">How many newly-modified Primary rows to stage.</param>
    /// <param name="newFilterRows">How many newly-modified Filter rows to stage.</param>
    /// <param name="expectedToFire">Whether the payload is expected.</param>
    [Theory]
    [MemberData(nameof(EmitGuardMatrix))]
    public void TheIdentityCallbackFiresWhenEitherArrayIsNonEmpty(
        string description,
        int newPrimaryRows,
        int newFilterRows,
        bool expectedToFire)
    {
        Assert.False(string.IsNullOrWhiteSpace(description));

        RecordingIdentitySource source = FixtureSource();

        // A non-qualifying row in each buffer, so that "empty" means the GUARD saw nothing rather than
        // the buffer being empty - the two are different states and only the first is under test here.
        source.PrimaryRows.Add(new FakeIdentityRow(ItemStatus.DataModified, 1L));
        source.FilterRows.Add(new FakeIdentityRow(ItemStatus.NotModified, 2L));

        for (int index = 0; index < newPrimaryRows; index++)
        {
            source.PrimaryRows.Add(NewRow(100L + index));
        }

        for (int index = 0; index < newFilterRows; index++)
        {
            source.FilterRows.Add(NewRow(200L + index));
        }

        source.InsertedCount = newPrimaryRows + newFilterRows + 1L;

        ResolvedIdentityColumnData? payload = IdentityColumnResolver.CollectIdentityData(
            source,
            DwSqliteFixture.IdColumnNumber);

        IdentityResolutionOutcome outcome = IdentityColumnResolver.Resolve(source, source);

        Assert.Equal(expectedToFire, payload is not null);
        Assert.Equal(expectedToFire, outcome.Identity.Length == 1);

        // Whether it fired or not, the counts are reported either way [:L247].
        Assert.Equal(source.InsertedCount, outcome.Counts.Inserted);
    }

    /// <summary>
    /// LEVEL 3 - both arrays empty emits NOTHING AT ALL [<c>:L242-L244</c>]. No empty payload is
    /// synthesised, because the oracle's guard is on the CALL rather than on the contents.
    /// </summary>
    [Fact]
    public void BothArraysEmptyEmitsNothing()
    {
        RecordingIdentitySource source = FixtureSource();
        source.InsertedCount = 2L;
        source.PrimaryRows.AddRange([new FakeIdentityRow(ItemStatus.DataModified, 1L)]);
        source.FilterRows.AddRange([new FakeIdentityRow(ItemStatus.NotModified, 2L)]);

        Assert.Null(IdentityColumnResolver.CollectIdentityData(source, 1));
        Assert.Empty(IdentityColumnResolver.Resolve(source, source).Identity);
    }

    /// <summary>
    /// The guard is an <c>OR</c>, so ONE non-empty array is enough - and the other travels EMPTY,
    /// neither null-collapsed nor merged into the first [<c>:L242</c>].
    /// </summary>
    [Fact]
    public void APrimaryOnlyPayloadIsEmittedWithAnEmptyFilterArray()
    {
        RecordingIdentitySource source = FixtureSource();
        source.PrimaryRows.AddRange([NewRow(601L), NewRow(602L)]);

        ResolvedIdentityColumnData? payload = IdentityColumnResolver.CollectIdentityData(source, 1);

        Assert.NotNull(payload);
        Assert.Equal<long?>([601L, 602L], payload.PrimaryValues);
        Assert.NotNull(payload.FilterValues);
        Assert.Empty(payload.FilterValues);
    }

    /// <summary>
    /// The mirror case: everything inserted was filtered out, so the Primary array is empty and the
    /// Filter array carries the whole payload - still emitted, still separate [<c>:L242</c>].
    /// </summary>
    [Fact]
    public void AFilterOnlyPayloadIsEmittedWithAnEmptyPrimaryArray()
    {
        RecordingIdentitySource source = FixtureSource();
        source.FilterRows.AddRange([NewRow(701L), NewRow(702L)]);

        ResolvedIdentityColumnData? payload = IdentityColumnResolver.CollectIdentityData(source, 1);

        Assert.NotNull(payload);
        Assert.NotNull(payload.PrimaryValues);
        Assert.Empty(payload.PrimaryValues);

        // Still DESCENDING, even when it is the only array present.
        Assert.Equal<long?>([702L, 701L], payload.FilterValues);
    }

    /// <summary>
    /// Collection is attempted on BOTH buffers before the guard is evaluated, in the oracle's own
    /// order - Primary [<c>:L228-L233</c>] then Filter [<c>:L234-L241</c>].
    /// </summary>
    [Fact]
    public void BothBuffersAreScannedBeforeTheGuardIsEvaluated()
    {
        RecordingIdentitySource source = FixtureSource();
        source.PrimaryRows.AddRange([new FakeIdentityRow(ItemStatus.NotModified, 1L)]);
        source.FilterRows.AddRange([new FakeIdentityRow(ItemStatus.NotModified, 2L)]);

        Assert.Null(IdentityColumnResolver.CollectIdentityData(source, 1));

        Assert.Equal(
            [DwBuffer.Primary, DwBuffer.Filter],
            source.StatusReads.Select(read => read.Buffer));
    }

    /// <summary>
    /// Reaching the collector with no discovered column is a caller fault, not a quiet null: the
    /// oracle gates on the ordinal BEFORE collecting [<c>:L226</c>], so a structural fault fails fast.
    /// </summary>
    [Theory]
    [InlineData(IdentityColumnResolver.NoIdentityColumn)]
    [InlineData(-1)]
    public void CollectionRejectsANonColumnOrdinal(int ordinal)
    {
        RecordingIdentitySource source = FixtureSource();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => IdentityColumnResolver.CollectIdentityData(source, ordinal));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => IdentityColumnResolver.CollectPrimaryValues(source, ordinal));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => IdentityColumnResolver.CollectFilterValues(source, ordinal));
    }

    #endregion

    #region The counts triple

    /// <summary>
    /// C-B - the triple reports the carrier's three getters UNMODIFIED and is NOT accumulated here:
    /// resolving twice reports the same values rather than doubled ones, because accumulation across
    /// firings belongs to the caller-side handler's <c>+=</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlupdate.sru:L66-L68</c>].
    /// </summary>
    [Fact]
    public void CountsAreReportedPerInvocationAndNeverAccumulated()
    {
        RecordingIdentitySource source = FixtureSource();
        source.InsertedCount = 5L;
        source.UpdatedCount = 11L;
        source.DeletedCount = 2L;
        source.PrimaryRows.AddRange([NewRow(1L)]);

        UpdateRowCounts first = IdentityColumnResolver.Resolve(source, source).Counts;
        UpdateRowCounts second = IdentityColumnResolver.Resolve(source, source).Counts;

        Assert.Equal(new UpdateRowCounts(5L, 11L, 2L), first);
        Assert.Equal(first, second);
    }

    /// <summary>
    /// The triple's member ORDER is the oracle's argument order - inserted, updated, deleted
    /// [<c>:L247</c>] - which a positional deconstruction proves.
    /// </summary>
    [Fact]
    public void CountsPreserveTheOraclesArgumentOrder()
    {
        RecordingIdentitySource source = new() { InsertedCount = 1L, UpdatedCount = 2L, DeletedCount = 3L };

        (long inserted, long updated, long deleted) = IdentityColumnResolver.ReadCounts(source);

        Assert.Equal(1L, inserted);
        Assert.Equal(2L, updated);
        Assert.Equal(3L, deleted);
    }

    #endregion

    #region R9 - the one-based audit

    // ==========================================================================================
    //  C-K - THIS REGION IS THE AUDIT AAP SECTION 0.4.5.4 REQUIRES, AND ITS MOST IMPORTANT SUBJECT.
    //  ----------------------------------------------------------------------------------------
    //  The plan states that one-based to zero-based translation is the single most dangerous
    //  mechanical hazard in the refactor - PowerBuilder arrays are one-based and its upper-bound
    //  function answers the LAST VALID INDEX, whereas a CLR array is zero-based with a length one past
    //  the end - and it requires every ported loop either to go through a CENTRALISED one-based
    //  indexing helper or to be INDIVIDUALLY AUDITED. It names the reverse-iteration cases the most
    //  dangerous of all, and cites this very Filter walk [n_cst_thread_task_sqlupdate.sru:L237].
    //
    //  THE SUBJECT TAKES THE FIRST OPTION and this region is the audit of it:
    //    * the helper is OneBasedIndex (Range, UpperBound, EmptyUpperBound, IsWithin, ToZeroBased),
    //    * the sentinels are ItemStatusMachine.FirstRowNumber, FirstColumnNumber and RowStatusColumn,
    //    * and the only rebasing anywhere in this file is inside the recording fake, which THROWS on a
    //      row of 0 or of count + 1 - so an off-by-one surfaces as a loud failure rather than as a
    //      quietly shifted result.
    //  The tests below pin the three shapes an off-by-one takes: the single-row boundary, a full
    //  six-row walk in both directions, and the empty buffer that must visit nothing at all.
    // ==========================================================================================

    /// <summary>
    /// R9 - a single row in each buffer is addressed as row 1 in BOTH directions: never row 0 and
    /// never row 2. The fake throws on either, so the assertion is that this does not throw plus the
    /// exact recorded row numbers.
    /// </summary>
    [Fact]
    public void ASingleRowIsAddressedAsRowOneInBothDirections()
    {
        RecordingIdentitySource source = FixtureSource();
        source.InsertedCount = 1L;
        source.PrimaryRows.AddRange([NewRow(11L)]);
        source.FilterRows.AddRange([NewRow(22L)]);

        IdentityResolutionOutcome outcome = IdentityColumnResolver.Resolve(source, source);

        ResolvedIdentityColumnData block = Assert.Single(outcome.Identity);

        Assert.Equal<long?>([11L], block.PrimaryValues);
        Assert.Equal<long?>([22L], block.FilterValues);
        Assert.All(source.StatusReads, read => Assert.Equal(1L, read.Row));
    }

    /// <summary>
    /// R9 - six rows in each buffer produce row numbers strictly inside <c>1..6</c> in both
    /// directions, and the two sequences are exact reverses of one another. Six because the sole
    /// evidenced fixture has six columns [<c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L21-L26</c>],
    /// which makes it the natural width for this audit.
    /// </summary>
    [Fact]
    public void SixRowsProduceOneBasedIndicesInsideTheCountInBothDirections()
    {
        RecordingIdentitySource source = FixtureSource();
        source.InsertedCount = 6L;

        for (long value = 1L; value <= 6L; value++)
        {
            source.PrimaryRows.Add(NewRow(value));
            source.FilterRows.Add(NewRow(100L + value));
        }

        IdentityColumnResolver.Resolve(source, source);

        long[] primaryRows =
            [.. source.StatusReads.Where(read => read.Buffer == DwBuffer.Primary).Select(read => read.Row)];
        long[] filterRows =
            [.. source.StatusReads.Where(read => read.Buffer == DwBuffer.Filter).Select(read => read.Row)];

        Assert.Equal([1L, 2L, 3L, 4L, 5L, 6L], primaryRows);
        Assert.Equal([6L, 5L, 4L, 3L, 2L, 1L], filterRows);

        // Neither 0 nor count + 1 is ever touched, in either direction.
        Assert.All(
            primaryRows.Concat(filterRows),
            row => Assert.InRange(row, ItemStatusMachine.FirstRowNumber, 6L));
    }

    /// <summary>
    /// R9 - an empty buffer visits nothing in either direction, matching <c>for nIndex = 1 to 0</c>
    /// and <c>for nIndex = 0 to 1 step -1</c>, both of which PowerScript executes zero times.
    /// </summary>
    [Fact]
    public void EmptyBuffersVisitNothingInEitherDirection()
    {
        RecordingIdentitySource source = FixtureSource();

        Assert.Empty(IdentityColumnResolver.CollectPrimaryValues(source, 1));
        Assert.Empty(IdentityColumnResolver.CollectFilterValues(source, 1));
        Assert.Empty(source.StatusReads);
    }

    #endregion

    #region The payload shape, and null preservation

    /// <summary>
    /// The payload's three members are in the oracle's <c>IdColData</c> field order - id, Primary,
    /// Filter [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlupdate.sru:L10-L14</c>] -
    /// which a positional deconstruction proves, and the id is the ONE-BASED column ordinal.
    /// </summary>
    [Fact]
    public void ThePayloadPreservesTheOraclesFieldOrderAndTheOneBasedColumnId()
    {
        RecordingIdentitySource source = FixtureSource();
        MarkIdentity(source, 4, "COMPANY.id");
        source.InsertedCount = 1L;
        source.PrimaryRows.AddRange([NewRow(801L)]);
        source.FilterRows.AddRange([NewRow(901L), NewRow(902L)]);

        ResolvedIdentityColumnData payload =
            Assert.Single(IdentityColumnResolver.Resolve(source, source).Identity);

        (long id, IReadOnlyList<long?> primary, IReadOnlyList<long?> filter) = payload;

        Assert.Equal(4L, id);
        Assert.Equal<long?>([801L], primary);
        Assert.Equal<long?>([902L, 901L], filter);

        // The id is the ONE-BASED describe ordinal, so it addresses a real column.
        Assert.InRange(id, ItemStatusMachine.FirstColumnNumber, DwSqliteFixture.ColumnCount);
    }

    /// <summary>
    /// C-B - a null numeric value is PRESERVED, not coerced to zero, so that a null cannot be
    /// confused with a legitimate identity value of zero. It is preserved all the way onto the wire by
    /// <see cref="ResolvedIdentityColumnData.ToIdentityColumnData"/>, and
    /// <see cref="ResolvedIdentityColumnData.ContainsNullValue"/> reports the condition for a caller
    /// that wants to know without projecting.
    /// </summary>
    [Fact]
    public void NullNumericValuesArePreservedAndReported()
    {
        RecordingIdentitySource source = FixtureSource();
        source.PrimaryRows.AddRange([NewRow(0L), NewRow(null)]);

        ResolvedIdentityColumnData payload =
            Assert.IsType<ResolvedIdentityColumnData>(
                IdentityColumnResolver.CollectIdentityData(source, 1));

        // Zero and null are DISTINCT, which is exactly what coercion would have destroyed.
        Assert.Equal<long?>([0L, null], payload.PrimaryValues);
        Assert.True(payload.ContainsNullValue);
    }

    /// <summary>
    /// A null in the FILTER array is reported too, and a payload with no null anywhere reports false.
    /// </summary>
    [Fact]
    public void ContainsNullValueInspectsBothArrays()
    {
        RecordingIdentitySource withFilterNull = FixtureSource();
        withFilterNull.PrimaryRows.AddRange([NewRow(1L)]);
        withFilterNull.FilterRows.AddRange([NewRow(null)]);

        ResolvedIdentityColumnData? flagged =
            IdentityColumnResolver.CollectIdentityData(withFilterNull, 1);
        Assert.NotNull(flagged);
        Assert.True(flagged.ContainsNullValue);

        RecordingIdentitySource withoutNull = FixtureSource();
        withoutNull.PrimaryRows.AddRange([NewRow(1L)]);
        withoutNull.FilterRows.AddRange([NewRow(2L)]);

        ResolvedIdentityColumnData? clean =
            IdentityColumnResolver.CollectIdentityData(withoutNull, 1);
        Assert.NotNull(clean);
        Assert.False(clean.ContainsNullValue);
    }

    #endregion

    #region The describe vocabulary and the PowerScript Left() accommodation

    /// <summary>
    /// The two describe properties are composed exactly as the oracle composes them,
    /// <c>"#" + String(ordinal) + ".Identity"</c> [<c>:L220</c>] and <c>... + ".DBName"</c>
    /// [<c>:L221</c>], with the ordinal passed through UNCHANGED because the describe vocabulary is
    /// itself one-based.
    /// </summary>
    [Theory]
    [InlineData(1, "#1.Identity", "#1.DBName")]
    [InlineData(6, "#6.Identity", "#6.DBName")]
    [InlineData(42, "#42.Identity", "#42.DBName")]
    public void DescribePropertiesAreComposedFromTheOneBasedOrdinal(
        int ordinal,
        string expectedIdentity,
        string expectedDbName)
    {
        Assert.Equal(expectedIdentity, IdentityColumnResolver.DescribeIdentityProperty(ordinal));
        Assert.Equal(expectedDbName, IdentityColumnResolver.DescribeDbNameProperty(ordinal));
    }

    /// <summary>
    /// R9 - an ordinal below <see cref="ItemStatusMachine.FirstColumnNumber"/> is rejected rather than
    /// composed, because <c>#0.Identity</c> addresses no column and a zero here almost always means a
    /// one-based ordinal was rebased.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void DescribePropertyCompositionRejectsANonColumnOrdinal(int ordinal)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => IdentityColumnResolver.DescribeIdentityProperty(ordinal));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => IdentityColumnResolver.DescribeDbNameProperty(ordinal));
    }

    /// <summary>
    /// The prefix is <c>Lower(updateTable) + "."</c> [<c>:L217</c>], and an empty table name still
    /// yields the bare separator rather than an empty string - which is what makes the truncation
    /// length at least one.
    /// </summary>
    [Theory]
    [InlineData("COMPANY", "company.")]
    [InlineData("company", "company.")]
    [InlineData("CoMpAnY", "company.")]
    [InlineData("dbo.COMPANY", "dbo.company.")]
    [InlineData("", ".")]
    public void ThePrefixIsTheLowerCasedTableNameFollowedByADot(string table, string expected)
    {
        Assert.Equal(expected, IdentityColumnResolver.BuildColumnDbNamePrefix(table));
    }

    /// <summary>
    /// <see cref="IdentityColumnResolver.LegacyLeft"/> reproduces PowerScript <c>Left</c>, including
    /// the behaviour the oracle actually depends on: A REQUEST LONGER THAN THE STRING ANSWERS THE
    /// WHOLE STRING rather than throwing, which is the fixture's own happy path -
    /// <c>Left("id", 8)</c> against the prefix <c>"company."</c>.
    /// </summary>
    [Theory]
    [InlineData("COMPANY.id", 8, "COMPANY.")]
    [InlineData("id", 8, "id")]
    [InlineData("id", 2, "id")]
    [InlineData("id", 1, "i")]
    [InlineData("id", 0, "")]
    [InlineData("id", -3, "")]
    [InlineData("", 8, "")]
    [InlineData(null, 8, "")]
    public void LegacyLeftMatchesPowerScriptIncludingTheShortStringCase(
        string? text,
        int length,
        string expected)
    {
        Assert.Equal(expected, IdentityColumnResolver.LegacyLeft(text, length));
    }

    /// <summary>
    /// The short-string case is not hypothetical: the fixture's unqualified database name is shorter
    /// than the prefix built from its own update table, so a direct <c>Substring</c> port would throw
    /// on the measured path.
    /// </summary>
    [Fact]
    public void TheFixtureItselfExercisesTheShortStringCase()
    {
        string prefix = IdentityColumnResolver.BuildColumnDbNamePrefix(FixtureTable);

        Assert.True(prefix.Length > "id".Length);
        Assert.Equal("id", IdentityColumnResolver.LegacyLeft("id", prefix.Length));
    }

    #endregion

    #region Argument validation - structural faults fail fast

    /// <summary>
    /// Every entry point rejects a null surface, because a missing injected surface is a structural
    /// fault in the caller and the plan requires structural faults fail fast rather than degrade.
    /// </summary>
    [Fact]
    public void EveryEntryPointRejectsANullSurface()
    {
        RecordingIdentitySource source = FixtureSource();

        Assert.Throws<ArgumentNullException>(
            () => IdentityColumnResolver.DiscoverIdentityColumn(null!));
        Assert.Throws<ArgumentNullException>(
            () => IdentityColumnResolver.CollectPrimaryValues(null!, 1));
        Assert.Throws<ArgumentNullException>(
            () => IdentityColumnResolver.CollectFilterValues(null!, 1));
        Assert.Throws<ArgumentNullException>(
            () => IdentityColumnResolver.CollectIdentityData(null!, 1));
        Assert.Throws<ArgumentNullException>(() => IdentityColumnResolver.ReadCounts(null!));
        Assert.Throws<ArgumentNullException>(
            () => IdentityColumnResolver.Resolve(null!, source));
        Assert.Throws<ArgumentNullException>(
            () => IdentityColumnResolver.Resolve(source, null!));
        Assert.Throws<ArgumentNullException>(
            () => IdentityColumnResolver.BuildColumnDbNamePrefix(null!));
        Assert.Throws<ArgumentNullException>(
            () => IdentityColumnResolver.ResolveTables(null!));

        // A null surface INSIDE an element is the same structural fault as a null argument, and it is
        // reported against the collection parameter because that is what the caller passed.
        ArgumentNullException missingMetadata = Assert.Throws<ArgumentNullException>(
            () => IdentityColumnResolver.ResolveTables([new IdentityTableSurfaces(null!, source)]));
        ArgumentNullException missingValues = Assert.Throws<ArgumentNullException>(
            () => IdentityColumnResolver.ResolveTables([new IdentityTableSurfaces(source, null!)]));

        Assert.Equal("tables", missingMetadata.ParamName);
        Assert.Equal("tables", missingValues.ParamName);
    }

    #endregion

    #region Multi-table - N ORDERED identity blocks, which a singular outcome could not carry

    // ==========================================================================================
    //  THE CARDINALITY THIS REGION EXISTS TO PIN
    //  ----------------------------------------------------------------------------------------
    //  `_of_Update` fires the identity callback AT MOST ONCE [:L243], but the task that drives it
    //  calls it ONCE PER UPDATE TABLE:
    //
    //      if _bMultiTableUpdate then
    //          nCount = UpperBound(Tables)                                          [:L358]
    //          for nIndex = 1 to nCount                                             [:L364]
    //              rtCode = _of_UpdatePrepare(data,nIndex)                          [:L365]
    //              rtCode = _of_Update(data)                                        [:L367]
    //          next
    //
    //  and the caller-side proxy APPENDS every firing to an ordered array and later REPLAYS EVERY
    //  ELEMENT [n_cst_threading_task_sqlupdate.sru:L45, L73-L76, L128, L142-L195]. The counts are
    //  accumulated with `+=` in the same place [:L66-L68].
    //
    //  SO THE SOURCE CARDINALITY IS 0..N ORDERED, AND EACH ELEMENT CARRIES ITS OWN COLUMN ORDINAL -
    //  because `_of_UpdatePrepare` re-describes the one carrier per table [:L103-L145], so the
    //  discovered column legitimately differs between them. A singular outcome would have kept one
    //  block and dropped the rest: DATA LOSS THAT RETURNS A PLAUSIBLE ANSWER, since no count would
    //  disagree with it. Every test below would pass against a singular model only if it happened to
    //  be given one table, which is exactly why the multi-table cases are stated explicitly.
    // ==========================================================================================

    /// <summary>
    /// Three tables that each collect identities yield THREE blocks, in the order the tables were
    /// supplied, each carrying its own column ordinal and its own two arrays.
    /// </summary>
    [Fact]
    public void ThreeTablesYieldThreeBlocksInTableOrderEachWithItsOwnColumn()
    {
        IdentityResolutionOutcome outcome = IdentityColumnResolver.ResolveTables(
        [
            TableCollecting(identityOrdinal: 1, primary: [11L], filter: [12L]),
            TableCollecting(identityOrdinal: 3, primary: [21L, 22L], filter: []),
            TableCollecting(identityOrdinal: 6, primary: [], filter: [31L, 32L, 33L]),
        ]);

        Assert.Equal(3, outcome.Identity.Length);

        // ORDER, asserted as a sequence rather than as a set: the ordinals are what identify which
        // table each block came from, so a re-ordering is detectable here and nowhere else.
        Assert.Equal([1L, 3L, 6L], outcome.Identity.Select(static block => block.IdentityColumnId));

        Assert.Equal<long?>([11L], outcome.Identity[0].PrimaryValues);
        Assert.Equal<long?>([12L], outcome.Identity[0].FilterValues);

        Assert.Equal<long?>([21L, 22L], outcome.Identity[1].PrimaryValues);
        Assert.Empty(outcome.Identity[1].FilterValues);

        Assert.Empty(outcome.Identity[2].PrimaryValues);
        Assert.Equal<long?>([31L, 32L, 33L], outcome.Identity[2].FilterValues);
    }

    /// <summary>
    /// A table that collects NOTHING contributes NO block, so the surviving blocks stay contiguous and
    /// in order - the list is shorter rather than carrying an empty element.
    /// </summary>
    /// <remarks>
    /// This is the emit guard [<c>:L242-L244</c>] holding across the loop. Synthesising a placeholder
    /// block for the middle table would give it a legacy counterpart it does not have, and would make
    /// the block index look like a table index - which it is not, and must not become.
    /// </remarks>
    [Fact]
    public void ATableThatCollectsNothingContributesNoBlock()
    {
        RecordingIdentitySource barren = FixtureSource();
        barren.InsertedCount = 2L;
        barren.PrimaryRows.AddRange([new FakeIdentityRow(ItemStatus.NotModified, 99L)]);

        IdentityResolutionOutcome outcome = IdentityColumnResolver.ResolveTables(
        [
            TableCollecting(identityOrdinal: 1, primary: [11L], filter: []),
            new IdentityTableSurfaces(barren, barren),
            TableCollecting(identityOrdinal: 5, primary: [31L], filter: []),
        ]);

        Assert.Equal(2, outcome.Identity.Length);
        Assert.Equal([1L, 5L], outcome.Identity.Select(static block => block.IdentityColumnId));
    }

    /// <summary>
    /// The counts are SUMMED across the tables, never overwritten by the last one - the caller-side
    /// handler accumulates with <c>+=</c> [<c>n_cst_threading_task_sqlupdate.sru:L66-L68</c>].
    /// </summary>
    [Fact]
    public void TheCountsAreAccumulatedAcrossTablesRatherThanOverwritten()
    {
        RecordingIdentitySource first = FixtureSource();
        first.InsertedCount = 1L;
        first.UpdatedCount = 2L;
        first.DeletedCount = 3L;

        RecordingIdentitySource second = FixtureSource();
        second.InsertedCount = 10L;
        second.UpdatedCount = 20L;
        second.DeletedCount = 30L;

        IdentityResolutionOutcome outcome = IdentityColumnResolver.ResolveTables(
        [
            new IdentityTableSurfaces(first, first),
            new IdentityTableSurfaces(second, second),
        ]);

        Assert.Equal(new UpdateRowCounts(11L, 22L, 33L), outcome.Counts);
    }

    /// <summary>
    /// The single-table arm goes through the same code path and produces exactly what
    /// <see cref="IdentityColumnResolver.Resolve"/> alone produces - so there is no second
    /// implementation to keep in step. The oracle's own <c>else</c> branch is one call [<c>:L371</c>].
    /// </summary>
    [Fact]
    public void OneTableProducesTheSameOutcomeAsThePerTableResolve()
    {
        IdentityTableSurfaces only = TableCollecting(identityOrdinal: 1, primary: [7L], filter: [8L]);

        IdentityResolutionOutcome throughTheLoop = IdentityColumnResolver.ResolveTables([only]);
        IdentityResolutionOutcome direct = IdentityColumnResolver.Resolve(only.Metadata, only.Values);

        // Re-reading the same surfaces is safe: the recording fake answers from stored state rather
        // than consuming it, so the second pass sees the same rows the first did.
        Assert.Equal(direct.Counts, throughTheLoop.Counts);

        ResolvedIdentityColumnData expected = Assert.Single(direct.Identity);
        ResolvedIdentityColumnData actual = Assert.Single(throughTheLoop.Identity);

        Assert.Equal(expected.IdentityColumnId, actual.IdentityColumnId);
        Assert.Equal(expected.PrimaryValues, actual.PrimaryValues);
        Assert.Equal(expected.FilterValues, actual.FilterValues);
    }

    /// <summary>
    /// No table at all answers the empty outcome rather than a second diagnostic: the oracle rejects an
    /// empty table list one level up, in the task loop [<c>:L359-L362</c>], not here.
    /// </summary>
    [Fact]
    public void NoTablesAnswersTheEmptyOutcome()
    {
        IdentityResolutionOutcome outcome = IdentityColumnResolver.ResolveTables([]);

        Assert.Empty(outcome.Identity);
        Assert.Equal(default, outcome.Counts);
    }

    /// <summary>
    /// The accumulated block list cannot be re-ordered by a consumer after the fact, because it is an
    /// immutable array rather than a mutable list. Re-ordering is the one mutation that would pair
    /// identity values with the wrong column while leaving every count intact.
    /// </summary>
    [Fact]
    public void TheAccumulatedBlockListIsImmutable()
    {
        // The member is internal, reachable through InternalsVisibleTo, so the lookup has to say so -
        // the default overload searches public members only and would answer null.
        PropertyInfo identity = typeof(IdentityResolutionOutcome).GetProperty(
            nameof(IdentityResolutionOutcome.Identity),
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;

        Assert.NotNull(identity);
        Assert.Equal(typeof(ImmutableArray<ResolvedIdentityColumnData>), identity.PropertyType);

        // INIT-ONLY, so an accumulated outcome cannot be reassigned after construction either. An
        // `init` accessor is a setter whose return parameter carries the IsExternalInit required
        // modifier - which is how the distinction from a plain `set` is expressed in metadata.
        MethodInfo setter = Assert.IsAssignableFrom<MethodInfo>(identity.SetMethod);

        Assert.Contains(typeof(IsExternalInit), setter.ReturnParameter.GetRequiredCustomModifiers());
    }

    /// <summary>
    /// Builds one table's surfaces: an identity column at <paramref name="identityOrdinal"/>, and rows
    /// whose identity values are <paramref name="primary"/> in the Primary buffer and
    /// <paramref name="filter"/> in the Filter buffer.
    /// </summary>
    /// <param name="identityOrdinal">The one-based column ordinal to mark as the identity column.</param>
    /// <param name="primary">The Primary buffer's identity values, in collection order.</param>
    /// <param name="filter">
    /// The Filter buffer's identity values, in the order the resolver must REPORT them. Supplied
    /// reversed to the fake, because the Filter buffer is walked BACKWARD [<c>:L237</c>] - which keeps
    /// each test's expectation readable while still driving the inverted walk.
    /// </param>
    /// <returns>The surfaces, with one object serving both roles as the legacy's single carrier does.</returns>
    private static IdentityTableSurfaces TableCollecting(
        int identityOrdinal,
        long?[] primary,
        long?[] filter)
    {
        RecordingIdentitySource source = new()
        {
            UpdateTable = FixtureTable,
            ColumnCount = DwSqliteFixture.ColumnCount,
            InsertedCount = primary.Length + filter.Length,
        };

        foreach (int ordinal in OneBasedIndex.Range(DwSqliteFixture.ColumnCount))
        {
            source.DbNameAnswers[IdentityColumnResolver.DescribeDbNameProperty(ordinal)] =
                DwSqliteFixture.ColumnAt(ordinal).DbName;
        }

        // The bare database name keeps this table on the FALLBACK arm, so the ordinal each fake table
        // reports is the one the caller asked for rather than one the prefix arm reassigned.
        MarkIdentity(source, identityOrdinal, DwSqliteFixture.ColumnAt(identityOrdinal).DbName);

        source.PrimaryRows.AddRange(primary.Select(NewRow));

        // Reversed on the way in, so that the BACKWARD walk reports them in the supplied order.
        source.FilterRows.AddRange(filter.Reverse().Select(NewRow));

        return new IdentityTableSurfaces(source, source);
    }

    #endregion

    #region The wire projection - presence, position and the two-array separation

    /// <summary>
    /// A null identity value travels as an element that is PRESENT and carries NO VALUE - never as
    /// zero, and never by dropping the element.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE THREE-WAY DISTINCTION IS THE WHOLE POINT, and only one of the three is obvious. A value of
    /// zero and a null are DIFFERENT because zero is a legal identity on a table seeded at zero, so
    /// coercing null to zero makes the two indistinguishable. A null and an ABSENT ELEMENT are also
    /// different, and that difference is the more dangerous one: the apply half indexes the array
    /// POSITIONALLY against the rows it writes back
    /// [<c>n_cst_threading_task_sqlupdate.sru:L143-L145</c>], so dropping one element does not lose one
    /// value - it misaligns every value after it.
    /// </para>
    /// <para>
    /// This test asserts all three in one array so no pair can be conflated.
    /// </para>
    /// </remarks>
    [Fact]
    public void ANullIdentityValueTravelsAsAPresentElementCarryingNoValue()
    {
        ResolvedIdentityColumnData resolved = new(4L, [0L, null, 7L], []);

        IdentityColumnData wire = resolved.ToIdentityColumnData();

        Assert.Equal(4L, wire.IdentityColumnId);

        // POSITION IS PRESERVED: three values in, three elements out, in the same order.
        Assert.Equal(3, wire.PrimaryValues.Count);

        Assert.True(wire.PrimaryValues[0].HasValue);
        Assert.Equal(0L, wire.PrimaryValues[0].Value);

        // The null element is PRESENT and carries nothing. Reading `.Value` here would answer the
        // protobuf default of 0, which is precisely why `HasValue` is the question that must be asked.
        Assert.False(wire.PrimaryValues[1].HasValue);

        Assert.True(wire.PrimaryValues[2].HasValue);
        Assert.Equal(7L, wire.PrimaryValues[2].Value);
    }

    /// <summary>
    /// The two arrays are never merged, never swapped and never reordered.
    /// </summary>
    /// <remarks>
    /// PRIMARY AND FILTER ARE COLLECTED BY TWO WALKS IN TWO DIRECTIONS - the Filter buffer is walked
    /// BACKWARD because its row order is inverted relative to the source
    /// [<c>n_cst_thread_task_sqlupdate.sru:L235,L237</c>] - and the apply half consumes them as two
    /// separate positional sequences. Concatenating them would produce one array that indexes correctly
    /// for neither buffer, and swapping them would write each buffer's identities into the other.
    /// Deliberately different lengths, so a merge would be visible as a count rather than only as an
    /// ordering.
    /// </remarks>
    [Fact]
    public void ThePrimaryAndFilterArraysStaySeparateAndKeepTheirOwnOrder()
    {
        ResolvedIdentityColumnData resolved = new(1L, [10L, 20L, 30L], [900L, 901L]);

        IdentityColumnData wire = resolved.ToIdentityColumnData();

        Assert.Equal<long?>([10L, 20L, 30L], Unwrap(wire.PrimaryValues));
        Assert.Equal<long?>([900L, 901L], Unwrap(wire.FilterValues));

        // Neither array acquired the other's content, which a merge would have produced as a count of 5.
        Assert.Equal(3, wire.PrimaryValues.Count);
        Assert.Equal(2, wire.FilterValues.Count);
    }

    /// <summary>
    /// An empty array projects as an empty array - not as absent, and not as one default element.
    /// </summary>
    /// <remarks>
    /// "Nothing was collected for this buffer" is the ordinary outcome for a buffer with no
    /// newly-modified rows, and it must stay distinguishable from "one row whose identity was null".
    /// </remarks>
    [Fact]
    public void AnEmptyArrayProjectsAsEmptyRatherThanAsOneNullElement()
    {
        IdentityColumnData wire = new ResolvedIdentityColumnData(1L, [], []).ToIdentityColumnData();

        Assert.Empty(wire.PrimaryValues);
        Assert.Empty(wire.FilterValues);

        // And the contrasting case, so the two readings are pinned against each other rather than
        // separately: one null element is a COUNT OF ONE with no value.
        IdentityColumnData oneNull =
            new ResolvedIdentityColumnData(1L, [null], []).ToIdentityColumnData();

        Assert.Single(oneNull.PrimaryValues);
        Assert.False(oneNull.PrimaryValues[0].HasValue);
    }

    /// <summary>
    /// The projection survives a protobuf round trip, which is the only test that proves the wire
    /// ENCODING carries presence rather than merely the in-memory message.
    /// </summary>
    /// <remarks>
    /// A message can answer <c>HasValue</c> correctly in memory and still lose the distinction on the
    /// wire if the field is not declared <c>optional</c> - proto3 omits a default-valued implicit field
    /// entirely, so an unset element and an element carrying 0 would serialize identically and the
    /// decoder could not tell them apart. Serializing and re-parsing is what closes that gap.
    /// </remarks>
    [Fact]
    public void PresenceSurvivesSerializationAndNotJustTheInMemoryMessage()
    {
        ResolvedIdentityColumnData resolved = new(2L, [null, 0L, null, 5L], [null]);

        IdentityColumnData parsed =
            IdentityColumnData.Parser.ParseFrom(resolved.ToIdentityColumnData().ToByteArray());

        Assert.Equal(2L, parsed.IdentityColumnId);
        Assert.Equal<long?>([null, 0L, null, 5L], Unwrap(parsed.PrimaryValues));
        Assert.Equal<long?>([null], Unwrap(parsed.FilterValues));
    }

    /// <summary>
    /// Every value the collector can produce round-trips, including the extremes.
    /// </summary>
    /// <param name="value">The identity value, or <see langword="null"/>.</param>
    [Theory]
    [InlineData(0L)]
    [InlineData(1L)]
    [InlineData(-1L)]
    [InlineData(long.MinValue)]
    [InlineData(long.MaxValue)]
    [InlineData(null)]
    public void EveryRepresentableIdentityValueRoundTrips(long? value)
    {
        IdentityColumnData parsed = IdentityColumnData.Parser.ParseFrom(
            new ResolvedIdentityColumnData(1L, [value], []).ToIdentityColumnData().ToByteArray());

        Assert.Equal<long?>([value], Unwrap(parsed.PrimaryValues));
    }

    /// <summary>
    /// The projection is the one that the resolver's own output feeds, end to end.
    /// </summary>
    /// <remarks>
    /// EVERY OTHER TEST IN THIS REGION CONSTRUCTS THE PAYLOAD BY HAND, which proves the mapper but not
    /// that it fits what the collector actually produces. This one drives the real collection path -
    /// including the Filter buffer's backward walk - and projects its output, so the two halves are
    /// pinned together.
    /// </remarks>
    [Fact]
    public void TheCollectorsOwnOutputProjectsWithItsOrderIntact()
    {
        IdentityTableSurfaces surfaces = TableCollecting(4, [801L], [902L, 901L]);

        ResolvedIdentityColumnData payload =
            Assert.Single(IdentityColumnResolver.ResolveTables([surfaces]).Identity);

        IdentityColumnData wire = payload.ToIdentityColumnData();

        Assert.Equal(4L, wire.IdentityColumnId);
        Assert.Equal<long?>([801L], Unwrap(wire.PrimaryValues));
        Assert.Equal<long?>([902L, 901L], Unwrap(wire.FilterValues));
    }

    /// <summary>
    /// Unwraps a projected array so a test can assert the value-versus-null distinction directly.
    /// </summary>
    /// <param name="wrapped">The projected elements.</param>
    /// <returns>One nullable value per element, in order.</returns>
    private static long?[] Unwrap(IEnumerable<NullableInt64> wrapped) =>
        [.. wrapped.Select(static element => element.HasValue ? element.Value : (long?)null)];

    #endregion
}
