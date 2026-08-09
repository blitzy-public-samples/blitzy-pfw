// ==============================================================================================
//  ItemStatusTests - the characterization suite that pins
//  PowerFramework.Persistence.Buffers.ItemStatusMachine and ItemStatusExtensions
//  --------------------------------------------------------------------------------------------
//  SYSTEM UNDER TEST  services/persistence-service/PowerFramework.Persistence/Buffers/ItemStatus.cs
//  BEHAVIOURAL ORACLE ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds_mt.sru
//                     ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru
//                     ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlupdate.sru
//                     ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru
//                     ws_objects/pfw.tests.pbl.src/dw_sqlite.srd  (the golden-master fixture)
//                     all READ ONLY per constraint C-C - read as specification, never edited
//
//  WHAT THIS SUITE IS ACTUALLY PROTECTING
//  --------------------------------------------------------------------------------------------
//  Three properties of this file are individually invisible and jointly load-bearing, and each one
//  would break SILENTLY:
//
//    1. THE VALUE ZERO CARRIES THREE UNRELATED MEANINGS - a column index meaning "the row itself",
//       a traversal seed meaning "before the first row", and a traversal result meaning "no more
//       modified rows". Risk R9 in the refactor plan names one-based translation the single most
//       dangerous mechanical hazard in the migration. A blanket minus-one applied to any of the
//       three shifts every row or column by one while leaving every COUNT intact, so no row-count
//       or column-count assertion can detect it. The sentinel and no-rebase cases below are that
//       detection.
//
//    2. THE THREE CLASSIFICATION PREDICATES ARE NOT VARIATIONS OF ONE RULE. They are three
//       separate legacy tests with three separate case lists, and two of the five statuses are
//       classified differently by each. Collapsing any pair - or "completing" a case list that
//       looks short - changes which rows produce SQL and which rows receive an identity value.
//       Every predicate is therefore asserted against EVERY member of the status domain rather
//       than only against the members it accepts.
//
//    3. THE TRAVERSAL IS LAZY, ITS VALIDATION IS EAGER, AND ITS VIEW IS LIVE. All three are
//       stated in the production remarks and none is observable from a call that simply counts the
//       rows it yields.
//
//  A NOTE ON WHAT THESE ASSERTIONS ARE EVIDENCE OF (finding DP-7)
//  --------------------------------------------------------------------------------------------
//  ItemStatus.cs is a port of PowerScript, not of a closed binary: the case lists, the loop bounds
//  and the sentinel values are all readable in the oracle exports cited above, so the assertions
//  here are TRACEABLE TO A LOCATOR rather than inferred. Where an assertion pins a decision the
//  oracle does not settle - the rejection of an out-of-domain buffer number, and the eager/lazy
//  split - it says so at the assertion. This suite is a characterization of the .NET port; the
//  paired legacy recording is what would make it a Golden-Master comparison, and that capture has
//  not been taken.
//
//  C-F SELF-AUDIT: every value here is a small synthetic status or row number. No credential, key,
//  token, password, connection string or captured statement text appears.
//
//  RULES POSITION: review_rules returns exactly one line, "No user rules provided.", so no
//  user-specified rule governs this file and none is invented. The enterprise-standard baseline
//  applies instead: deterministic, no I/O, no clock, no shared mutable state, every test
//  independent of every other. No performance property is asserted anywhere, because the
//  repository publishes no latency, throughput or availability target to assert against.
// ==============================================================================================

using System.Reflection;

using PowerFramework.Contracts.Common.V1;
using PowerFramework.Persistence.Buffers;
using Xunit;

namespace PowerFramework.Persistence.Tests;

/// <summary>
/// Characterization tests for the DataWindow item-status machine: its three zero-valued sentinels,
/// its row-versus-column status addressing, its three classification predicates and its two
/// traversals.
/// </summary>
public sealed class ItemStatusTests
{
    /// <summary>
    /// The six-row buffer used by the traversal cases. Rows 2, 4 and 6 are the modified ones, and
    /// the shape is chosen so that a modified row is neither first nor last-only: an off-by-one in
    /// either direction changes the yielded set rather than merely its length.
    /// </summary>
    private static ItemStatus[] SixRowBuffer() =>
    [
        ItemStatus.NotModified,   // row 1
        ItemStatus.DataModified,  // row 2 - modified
        ItemStatus.New,           // row 3
        ItemStatus.NewModified,   // row 4 - modified
        ItemStatus.NotModified,   // row 5
        ItemStatus.DataModified,  // row 6 - modified
    ];

    /// <summary>
    /// Reads <paramref name="rows"/> by ONE-BASED row number, which is the contract every accessor
    /// delegate in this file is handed.
    /// </summary>
    private static Func<long, ItemStatus> OneBasedReader(ItemStatus[] rows) =>
        rowNumber => rows[(int)rowNumber - 1];

    // ==========================================================================================
    //  DECISION 1 - the domain enums are the published ones, not local redeclarations
    // ==========================================================================================

    /// <summary>
    /// The status and buffer domains are the PUBLISHED protobuf enums, and this project declares no
    /// competing type of either name.
    /// </summary>
    /// <remarks>
    /// DECISION 1 in the production header: a second type named <c>ItemStatus</c> in
    /// <c>PowerFramework.Persistence.Buffers</c> would make the simple name ambiguous - CS0104 - in
    /// every consumer importing both namespaces, and under the inherited
    /// <c>TreatWarningsAsErrors</c> that is a hard build failure across the service. It would also
    /// fork the wire agreement, because the numeric values would then live in two places with no
    /// compile-time edge between them. Asserting the ABSENCE of the redeclaration is the only way
    /// to notice it being added, since adding it would break consumers rather than this file.
    /// </remarks>
    [Fact]
    public void TheStatusAndBufferDomains_AreDeclaredOnceByThePublishedContract()
    {
        Assert.Equal("PowerFramework.Contracts", typeof(ItemStatus).Assembly.GetName().Name);
        Assert.Equal("PowerFramework.Contracts", typeof(DwBuffer).Assembly.GetName().Name);
        Assert.Equal("PowerFramework.Contracts.Common.V1", typeof(ItemStatus).Namespace);

        Assembly persistence = typeof(ItemStatusMachine).Assembly;
        Assert.Equal("PowerFramework.Persistence", persistence.GetName().Name);

        string[] competing = persistence
            .GetTypes()
            .Where(type => type.Name is "ItemStatus" or "DwBuffer")
            .Select(type => type.FullName ?? type.Name)
            .ToArray();

        Assert.Empty(competing);
    }

    // ==========================================================================================
    //  The R9 sentinel surface
    // ==========================================================================================

    /// <summary>
    /// The five named values hold exactly the numbers the legacy passes, and the three zero-valued
    /// ones are three separate declarations rather than one shared constant.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>GetItemStatus(nRow,0,Delete!)</c>
    /// [<c>n_cst_thread_task_sqlbase_ds_mt.sru:L56</c>] fixes the column sentinel;
    /// <c>nRow = GetNextModified(0,Primary!)</c>
    /// [<c>n_cst_threading_task_sqlupdate.sru:L138</c>] fixes the seed;
    /// <c>do while(nRow &gt; 0)</c> [<c>:L139</c>] fixes the terminator; and
    /// <c>for nRow = DeletedCount() to 1 step -1</c>
    /// [<c>n_cst_thread_task_sqlbase_ds_mt.sru:L55</c>] fixes the first row number. The first
    /// column number is fixed by the fixture, whose six columns carry ids 1 through 6
    /// [<c>dw_sqlite.srd:L21-L26</c>].
    /// </para>
    /// <para>
    /// The two traversal sentinels are asserted through SEPARATE names even though both are zero,
    /// because they are separate contracts - one an argument, the other a result - and a future
    /// change to either must not silently change the other. The TYPES are asserted too: the column
    /// sentinel is an <see cref="int"/> because a column index is, while both row sentinels are
    /// <see cref="long"/> because a row number is.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheFiveSentinels_HoldTheirMeasuredValuesAndTheirMeasuredWidths()
    {
        Assert.Equal(0, ItemStatusMachine.RowStatusColumn);
        Assert.Equal(1, ItemStatusMachine.FirstColumnNumber);
        Assert.Equal(1L, ItemStatusMachine.FirstRowNumber);
        Assert.Equal(0L, ItemStatusMachine.BeforeFirstRow);
        Assert.Equal(0L, ItemStatusMachine.NoMoreModifiedRows);

        // The three zero-valued sentinels agree numerically and are nonetheless three declarations.
        Assert.Equal(ItemStatusMachine.RowStatusColumn, (int)ItemStatusMachine.BeforeFirstRow);
        Assert.Equal(ItemStatusMachine.BeforeFirstRow, ItemStatusMachine.NoMoreModifiedRows);

        Type machine = typeof(ItemStatusMachine);
        Assert.Equal(
            typeof(int),
            machine.GetField("RowStatusColumn", BindingFlags.NonPublic | BindingFlags.Static)!.FieldType);
        Assert.Equal(
            typeof(long),
            machine.GetField("BeforeFirstRow", BindingFlags.NonPublic | BindingFlags.Static)!.FieldType);
        Assert.Equal(
            typeof(long),
            machine.GetField("NoMoreModifiedRows", BindingFlags.NonPublic | BindingFlags.Static)!.FieldType);
    }

    /// <summary>
    /// Zero addresses the ROW's own status; every legal column number addresses that COLUMN.
    /// </summary>
    /// <remarks>
    /// The decisive evidence that this is a real distinction rather than an unexercised convention
    /// is two adjacent lines of one function: <c>n_cst_thread_task_sqlupdate.sru:L160</c> reads the
    /// row's status with a literal zero, and <c>:L162</c> reads a specific column's status with a
    /// one-based number drawn from the key-column array. The only difference between the two calls
    /// is whether the index is zero.
    /// </remarks>
    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(6, false)]
    [InlineData(7, false)]
    [InlineData(int.MaxValue, false)]
    public void AddressesRowStatus_IsTrueForZeroAndForNothingElse(int columnIndex, bool expected) =>
        Assert.Equal(expected, ItemStatusMachine.AddressesRowStatus(columnIndex));

    /// <summary>
    /// A legal column index resolves to the SAME number, with no arithmetic applied.
    /// </summary>
    /// <remarks>
    /// R9, and the reason the method exists at all. A legacy one-based column number is already the
    /// number this service's contracts carry - <c>ColumnValue.column_id</c> is documented as a
    /// one-based ordinal whose zero is reserved for the row - so a minus-one here would shift every
    /// column by one while leaving every column count intact. The fixture's own ids 1 through 6
    /// [<c>dw_sqlite.srd:L21-L26</c>] are included among the cases for exactly that reason.
    /// </remarks>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(int.MaxValue)]
    public void TryGetColumnNumber_PassesAColumnNumberThroughUnchanged(int columnIndex)
    {
        Assert.True(ItemStatusMachine.TryGetColumnNumber(columnIndex, out int columnNumber));
        Assert.Equal(columnIndex, columnNumber);
    }

    /// <summary>
    /// The row sentinel reports failure and echoes the sentinel back rather than a plausible column
    /// number.
    /// </summary>
    /// <remarks>
    /// Deliberate, and documented on the parameter: a caller that ignores the false return must not
    /// read column ONE here. Echoing zero makes such a caller wrong in a way its own validation can
    /// catch, whereas echoing one would make it wrong in a way nothing can.
    /// </remarks>
    [Fact]
    public void TryGetColumnNumber_ReportsFailureForTheRowSentinelAndEchoesItBack()
    {
        Assert.False(ItemStatusMachine.TryGetColumnNumber(ItemStatusMachine.RowStatusColumn, out int columnNumber));
        Assert.Equal(ItemStatusMachine.RowStatusColumn, columnNumber);
        Assert.NotEqual(ItemStatusMachine.FirstColumnNumber, columnNumber);
    }

    /// <summary>
    /// A NEGATIVE column index is rejected by both addressing members, naming the caller's own
    /// parameter and carrying the offending value.
    /// </summary>
    /// <remarks>
    /// This is the R9 tripwire. The domain is the sentinel zero plus the one-based numbers above it,
    /// with no gap, so a negative value is the only way to fall outside - and the commonest way to
    /// produce one is to have rebased a one-based column number. The mistake in the other direction,
    /// column one becoming zero, is undetectable because zero is legal; this is the half that CAN be
    /// caught, which is why the diagnostic names the hazard.
    /// </remarks>
    [Theory]
    [InlineData(-1)]
    [InlineData(-2)]
    [InlineData(int.MinValue)]
    public void BothAddressingMembers_RejectANegativeColumnIndex(int columnIndex)
    {
        ArgumentOutOfRangeException fromPredicate = Assert.Throws<ArgumentOutOfRangeException>(
            () => ItemStatusMachine.AddressesRowStatus(columnIndex));
        Assert.Equal("columnIndex", fromPredicate.ParamName);
        Assert.Equal(columnIndex, fromPredicate.ActualValue);

        ArgumentOutOfRangeException fromResolver = Assert.Throws<ArgumentOutOfRangeException>(
            () => ItemStatusMachine.TryGetColumnNumber(columnIndex, out _));
        Assert.Equal("columnIndex", fromResolver.ParamName);
        Assert.Equal(columnIndex, fromResolver.ActualValue);
    }

    // ==========================================================================================
    //  The three classification predicates, each against the WHOLE domain
    // ==========================================================================================

    /// <summary>
    /// Delete-countability accepts exactly <c>NotModified</c> and <c>DataModified</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Reproduces <c>case NotModified!,DataModified!</c>
    /// [<c>n_cst_thread_task_sqlbase_ds_mt.sru:L57</c>] - two statuses, no default arm.
    /// </para>
    /// <para>
    /// C-B - THE EXCLUSIONS ARE THE POINT OF THE TEST. <c>NewModified</c> is absent from the legacy
    /// case list, so a row inserted, edited and then deleted before the update ran does NOT count:
    /// it never reached the database, so it generates no statement, so counting it would inflate the
    /// denominator of the progress notification [<c>:L39</c>] and make this service's progress
    /// stream diverge from the legacy's. <c>New</c> is absent for the same reason. Both are asserted
    /// FALSE here so that "completing" the case list fails a test rather than passing review.
    /// </para>
    /// <para>
    /// THE FOUR ROWS ARE THE WHOLE DOMAIN. <c>ItemStatus</c> carries exactly the four legacy statuses
    /// with <c>NotModified</c> at zero and no sentinel member, so there is no fifth declared value to
    /// screen. The fifth row is an out-of-domain numeric cast - the only way a value outside the
    /// domain can reach a predicate at all, whether from a foreign payload carrying an unknown enum
    /// number or from a caller casting an integer - and it answers FALSE because a status the legacy
    /// cannot represent is in none of the legacy's case lists.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(ItemStatus.NotModified, true)]
    [InlineData(ItemStatus.DataModified, true)]
    [InlineData(ItemStatus.NewModified, false)]
    [InlineData(ItemStatus.New, false)]
    [InlineData((ItemStatus)4, false)]
    public void IsDeleteCountable_AcceptsExactlyTheTwoStatusesTheLegacyCaseListNames(
        ItemStatus status,
        bool expected) =>
        Assert.Equal(expected, ItemStatusMachine.IsDeleteCountable(status));

    /// <summary>
    /// New-row identity participation accepts <c>NewModified</c> ONLY.
    /// </summary>
    /// <remarks>
    /// The legacy test is <c>= NewModified!</c>, an equality comparison against one status, and it
    /// appears six times across the update path
    /// [<c>n_cst_threading_task_sqlupdate.sru:L140,L156,L174,L190</c>,
    /// <c>n_cst_thread_task_sqlupdate.sru:L230,L238</c>]. <c>New</c> is asserted FALSE deliberately:
    /// widening this to "any new row" would look like a tidy-up and would change which rows the
    /// identity write-back touches. The domain enum carries <c>New</c> because it is a legal runtime
    /// status a payload must be able to express, not because any legacy test matches it. The final row
    /// is an out-of-domain cast rather than a declared member, because the domain is exactly the four
    /// legacy statuses and carries no sentinel.
    /// </remarks>
    [Theory]
    [InlineData(ItemStatus.NewModified, true)]
    [InlineData(ItemStatus.New, false)]
    [InlineData(ItemStatus.DataModified, false)]
    [InlineData(ItemStatus.NotModified, false)]
    [InlineData((ItemStatus)4, false)]
    public void IsNewRow_AcceptsNewModifiedAloneAndNotThePlainNewStatus(ItemStatus status, bool expected) =>
        Assert.Equal(expected, ItemStatusMachine.IsNewRow(status));

    /// <summary>
    /// Modification accepts <c>DataModified</c> and <c>NewModified</c>.
    /// </summary>
    /// <remarks>
    /// The pair behind <c>GetNextModified</c> and behind the <c>ModifiedCount()</c> the legacy seeds
    /// its progress total from [<c>n_cst_thread_task_sqlbase_ds_mt.sru:L52</c>]. The same pair is
    /// what the query path STAMPS onto rows before extracting a changeset, at six sites every one of
    /// which passes column index zero [<c>n_cst_thread_task_sqlquery.sru:L123,L127,L162,L168,L197,L202</c>].
    /// The final row is an out-of-domain cast rather than a declared member, because the domain is
    /// exactly the four legacy statuses and carries no sentinel.
    /// </remarks>
    [Theory]
    [InlineData(ItemStatus.DataModified, true)]
    [InlineData(ItemStatus.NewModified, true)]
    [InlineData(ItemStatus.NotModified, false)]
    [InlineData(ItemStatus.New, false)]
    [InlineData((ItemStatus)4, false)]
    public void IsModified_AcceptsTheDataModifiedAndNewModifiedPair(ItemStatus status, bool expected) =>
        Assert.Equal(expected, ItemStatusMachine.IsModified(status));

    /// <summary>
    /// The three predicates are three DIFFERENT rules: they overlap on one status and disagree on
    /// two, so no pair can be collapsed into the other and none is the complement of another.
    /// </summary>
    /// <remarks>
    /// Stated as a relationship rather than as three separate truth tables because the failure mode
    /// worth catching is a refactor that notices two predicates "look similar" and unifies them. The
    /// exact disagreements: <c>NotModified</c> is delete-countable but not modified, and
    /// <c>NewModified</c> is modified and a new row but NOT delete-countable.
    /// </remarks>
    [Fact]
    public void TheThreePredicates_AreThreeDistinctRulesRatherThanVariationsOfOne()
    {
        // They overlap on exactly one status.
        ItemStatus[] domain = Enum.GetValues<ItemStatus>();
        ItemStatus[] overlap = domain
            .Where(status => ItemStatusMachine.IsDeleteCountable(status) && ItemStatusMachine.IsModified(status))
            .ToArray();
        Assert.Equal([ItemStatus.DataModified], overlap);

        // NotModified separates delete-countability from modification.
        Assert.True(ItemStatusMachine.IsDeleteCountable(ItemStatus.NotModified));
        Assert.False(ItemStatusMachine.IsModified(ItemStatus.NotModified));

        // NewModified separates modification from delete-countability, and is the only new row.
        Assert.True(ItemStatusMachine.IsModified(ItemStatus.NewModified));
        Assert.False(ItemStatusMachine.IsDeleteCountable(ItemStatus.NewModified));
        Assert.True(ItemStatusMachine.IsNewRow(ItemStatus.NewModified));

        // No predicate is the negation of another anywhere in the domain.
        Assert.Contains(
            domain,
            status => ItemStatusMachine.IsDeleteCountable(status) == ItemStatusMachine.IsModified(status));
        Assert.Contains(
            domain,
            status => ItemStatusMachine.IsDeleteCountable(status) != ItemStatusMachine.IsModified(status));

        // IsNewRow is strictly narrower than IsModified rather than unrelated to it.
        Assert.DoesNotContain(
            domain,
            status => ItemStatusMachine.IsNewRow(status) && !ItemStatusMachine.IsModified(status));
    }

    /// <summary>
    /// A status OUTSIDE the declared domain classifies as false by all three predicates and does not
    /// throw.
    /// </summary>
    /// <remarks>
    /// That is exactly what a PowerScript <c>choose case</c> with no <c>case else</c> arm does with
    /// an unmatched value: nothing happens and control falls through. It matters on a network
    /// boundary specifically, because protobuf carries an unrecognised enum value through as its
    /// number rather than rejecting it, so a newer peer's status can reach these predicates. Failing
    /// closed - false, no exception - keeps such a row out of the update rather than aborting the
    /// whole operation.
    /// </remarks>
    [Theory]
    [InlineData(5)]
    [InlineData(99)]
    [InlineData(-1)]
    public void EveryPredicate_FailsClosedForAStatusOutsideTheDeclaredDomain(int rawStatus)
    {
        ItemStatus status = (ItemStatus)rawStatus;

        Assert.False(ItemStatusMachine.IsDeleteCountable(status));
        Assert.False(ItemStatusMachine.IsNewRow(status));
        Assert.False(ItemStatusMachine.IsModified(status));
    }

    /// <summary>
    /// The fluent extension forwarders agree with the machine for EVERY member of the domain and add
    /// no rule of their own.
    /// </summary>
    /// <remarks>
    /// The forwarders exist so a call site can read <c>status.IsModified()</c>, and each body is a
    /// single call into the machine. The risk they carry is a second copy of a case list, which is
    /// the part of the file with parity significance - so the assertion compares the two surfaces
    /// across the whole domain rather than sampling it.
    /// </remarks>
    [Fact]
    public void TheExtensionForwarders_AgreeWithTheMachineAcrossTheWholeDomain()
    {
        foreach (ItemStatus status in Enum.GetValues<ItemStatus>())
        {
            Assert.Equal(ItemStatusMachine.IsDeleteCountable(status), status.IsDeleteCountable());
            Assert.Equal(ItemStatusMachine.IsNewRow(status), status.IsNewRow());
            Assert.Equal(ItemStatusMachine.IsModified(status), status.IsModified());
        }

        // Including outside the domain, where the forwarders must also fail closed.
        Assert.False(((ItemStatus)99).IsDeleteCountable());
        Assert.False(((ItemStatus)99).IsNewRow());
        Assert.False(((ItemStatus)99).IsModified());
    }

    // ==========================================================================================
    //  The delete walk - n_cst_thread_task_sqlbase_ds_mt.sru:L55-L60
    // ==========================================================================================

    /// <summary>
    /// The delete walk counts the delete-countable rows and nothing else.
    /// </summary>
    /// <remarks>
    /// Four of the six fixture rows are delete-countable: rows 1 and 5 are <c>NotModified</c> and
    /// rows 2 and 6 are <c>DataModified</c>, while row 3 (<c>New</c>) and row 4
    /// (<c>NewModified</c>) are not. The result is DELIBERATELY not the whole update total: the
    /// legacy seeds that with <c>ModifiedCount()</c> at <c>:L52</c> and increments it inside this
    /// loop, so the total is the caller's modified count PLUS this value. That composition belongs
    /// to the Tasks layer along with the <c>CPU()</c>-based notification rate limit at <c>:L53</c>.
    /// </remarks>
    [Fact]
    public void CountDeleteCountable_CountsOnlyTheRowsTheLegacyCaseListAdmits()
    {
        ItemStatus[] rows = SixRowBuffer();

        Assert.Equal(4L, ItemStatusMachine.CountDeleteCountable(rows.Length, OneBasedReader(rows)));
    }

    /// <summary>
    /// An empty delete buffer yields zero and never touches the accessor.
    /// </summary>
    /// <remarks>
    /// <c>DeletedCount()</c> is legitimately zero on any update that deletes nothing, which is the
    /// common case, so this is the ordinary path rather than an edge case. The descending loop
    /// [<c>:L55</c>] simply does not execute.
    /// </remarks>
    [Fact]
    public void CountDeleteCountable_ReturnsZeroForAnEmptyBufferWithoutReadingAnyRow()
    {
        int reads = 0;

        long counted = ItemStatusMachine.CountDeleteCountable(
            0L,
            _ =>
            {
                reads++;
                return ItemStatus.DataModified;
            });

        Assert.Equal(0L, counted);
        Assert.Equal(0, reads);
    }

    /// <summary>
    /// The walk reads every row exactly ONCE and walks DOWNWARD, from the last row to
    /// <c>FirstRowNumber</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both properties are preserved from <c>for nRow = DeletedCount() to 1 step -1</c>
    /// [<c>n_cst_thread_task_sqlbase_ds_mt.sru:L55</c>], and the legacy calls
    /// <c>GetItemStatus</c> once per iteration with no caching.
    /// </para>
    /// <para>
    /// A COUNT CANNOT DEPEND ON ORDER, so this is the only assertion that can detect the direction
    /// at all - which is precisely why it is here. The reverse iteration is retained so the loop
    /// stays diffable against its locator line for line and so the whole file remains inside the
    /// one-based indexing domain (R9), and the assertion records that intent in a form that a
    /// "simplifying" rewrite to an ascending loop would fail. Note that the terminating bound is
    /// <c>FirstRowNumber</c> and not zero: row 0 is never read.
    /// </para>
    /// </remarks>
    [Fact]
    public void CountDeleteCountable_ReadsEachRowOnceInDescendingOrder()
    {
        ItemStatus[] rows = SixRowBuffer();
        List<long> visited = [];

        long counted = ItemStatusMachine.CountDeleteCountable(
            rows.Length,
            rowNumber =>
            {
                visited.Add(rowNumber);
                return rows[(int)rowNumber - 1];
            });

        Assert.Equal(4L, counted);
        Assert.Equal([6L, 5L, 4L, 3L, 2L, 1L], visited);
        Assert.DoesNotContain(ItemStatusMachine.BeforeFirstRow, visited);
        Assert.Equal(ItemStatusMachine.FirstRowNumber, visited[^1]);
    }

    /// <summary>
    /// The delete walk rejects a null accessor and a negative count.
    /// </summary>
    [Fact]
    public void CountDeleteCountable_RejectsANullAccessorAndANegativeCount()
    {
        ArgumentNullException nullAccessor = Assert.Throws<ArgumentNullException>(
            () => ItemStatusMachine.CountDeleteCountable(1L, null!));
        Assert.Equal("deleteRowStatusAt", nullAccessor.ParamName);

        ArgumentOutOfRangeException negative = Assert.Throws<ArgumentOutOfRangeException>(
            () => ItemStatusMachine.CountDeleteCountable(-1L, _ => ItemStatus.NotModified));
        Assert.Equal("deletedCount", negative.ParamName);
    }

    // ==========================================================================================
    //  GetNextModified - the sentinel contract
    // ==========================================================================================

    /// <summary>
    /// Seeding with <c>BeforeFirstRow</c> finds the first modified row, and each result seeds the
    /// next call until the walk terminates on <c>NoMoreModifiedRows</c>.
    /// </summary>
    /// <remarks>
    /// This is the legacy idiom in full
    /// [<c>n_cst_threading_task_sqlupdate.sru:L138-L148</c>]: seed with a literal zero, loop while
    /// the row number is strictly positive, and re-seed from the previous result. The two zeros are
    /// different contracts sharing a value - the seed is an ARGUMENT, the terminator a RESULT - and
    /// the single piece of arithmetic in the implementation adds one to the seed to reach
    /// <c>FirstRowNumber</c>, which is an advance inside a one-based domain rather than a conversion
    /// out of one.
    /// </remarks>
    [Fact]
    public void GetNextModified_WalksTheModifiedRowsInAscendingOrderAndThenReportsExhaustion()
    {
        ItemStatus[] rows = SixRowBuffer();
        Func<long, ItemStatus> read = OneBasedReader(rows);

        long first = ItemStatusMachine.GetNextModified(
            ItemStatusMachine.BeforeFirstRow, DwBuffer.Primary, rows.Length, read);
        long second = ItemStatusMachine.GetNextModified(first, DwBuffer.Primary, rows.Length, read);
        long third = ItemStatusMachine.GetNextModified(second, DwBuffer.Primary, rows.Length, read);
        long exhausted = ItemStatusMachine.GetNextModified(third, DwBuffer.Primary, rows.Length, read);

        Assert.Equal(2L, first);
        Assert.Equal(4L, second);
        Assert.Equal(6L, third);
        Assert.Equal(ItemStatusMachine.NoMoreModifiedRows, exhausted);
    }

    /// <summary>
    /// The seed advances by exactly one, so a row that IS modified is not returned as its own
    /// successor.
    /// </summary>
    /// <remarks>
    /// The single most valuable property of the seed arithmetic, and the one a minus-one would
    /// destroy in a way the yielded COUNT would still look plausible for. Row 2 is modified; seeding
    /// with 2 must return 4, not 2, or the legacy's <c>do while</c> would never terminate.
    /// </remarks>
    [Fact]
    public void GetNextModified_AdvancesPastTheSeedRowEvenWhenThatRowIsItselfModified()
    {
        ItemStatus[] rows = SixRowBuffer();

        Assert.Equal(
            4L, ItemStatusMachine.GetNextModified(2L, DwBuffer.Primary, rows.Length, OneBasedReader(rows)));
    }

    /// <summary>
    /// A buffer with no modified row, and an empty buffer, both report exhaustion immediately.
    /// </summary>
    [Fact]
    public void GetNextModified_ReportsExhaustionForABufferWithNothingModifiedAndForAnEmptyOne()
    {
        ItemStatus[] unmodified =
            [ItemStatus.NotModified, ItemStatus.New, ItemStatus.NotModified];

        Assert.Equal(
            ItemStatusMachine.NoMoreModifiedRows,
            ItemStatusMachine.GetNextModified(
                ItemStatusMachine.BeforeFirstRow, DwBuffer.Primary, unmodified.Length, OneBasedReader(unmodified)));

        Assert.Equal(
            ItemStatusMachine.NoMoreModifiedRows,
            ItemStatusMachine.GetNextModified(
                ItemStatusMachine.BeforeFirstRow, DwBuffer.Primary, 0L, _ => ItemStatus.DataModified));
    }

    /// <summary>
    /// A seed at or beyond the last row short-circuits, so <see cref="long.MaxValue"/> is safe and
    /// the increment cannot overflow.
    /// </summary>
    /// <remarks>
    /// The at-or-beyond-end test runs BEFORE the increment, which is what removes any possibility of
    /// wrapping. Asserted with the extreme value rather than merely with a value past the end,
    /// because overflow is the failure this ordering exists to prevent.
    /// </remarks>
    [Theory]
    [InlineData(6L)]
    [InlineData(7L)]
    [InlineData(long.MaxValue)]
    public void GetNextModified_ShortCircuitsAtOrBeyondTheLastRowWithoutOverflowing(long afterRowNumber)
    {
        ItemStatus[] rows = SixRowBuffer();
        int reads = 0;

        long result = ItemStatusMachine.GetNextModified(
            afterRowNumber,
            DwBuffer.Primary,
            rows.Length,
            rowNumber =>
            {
                reads++;
                return rows[(int)rowNumber - 1];
            });

        Assert.Equal(ItemStatusMachine.NoMoreModifiedRows, result);
        Assert.Equal(0, reads);
    }

    /// <summary>
    /// All THREE legacy buffers are accepted, and the same walk serves each.
    /// </summary>
    /// <remarks>
    /// The measured call sites use <c>Primary!</c> and <c>Filter!</c> - the legacy writes the walk
    /// out twice per host type - and <c>Delete!</c> is accepted because the legacy <c>dwbuffer</c>
    /// domain has exactly three members and restricting it further would invent a rule the legacy
    /// does not have. A consumer that walked only the Primary buffer would silently skip the
    /// filtered-out rows the legacy visits.
    /// </remarks>
    [Theory]
    [InlineData(DwBuffer.Primary)]
    [InlineData(DwBuffer.Delete)]
    [InlineData(DwBuffer.Filter)]
    public void GetNextModified_ServesEveryOneOfTheThreeLegacyBuffersIdentically(DwBuffer buffer)
    {
        ItemStatus[] rows = SixRowBuffer();

        Assert.Equal(
            2L, ItemStatusMachine.GetNextModified(
                ItemStatusMachine.BeforeFirstRow, buffer, rows.Length, OneBasedReader(rows)));
    }

    /// <summary>
    /// Any buffer value outside the declared three-member domain is rejected.
    /// </summary>
    /// <remarks>
    /// <para>
    /// NOT a behavioural addition: it refuses a state the legacy cannot represent. The legacy
    /// <c>dwbuffer</c> domain has exactly three members and no way to express "unset", and the
    /// published contract carries exactly those three - <c>DW_BUFFER_PRIMARY = 0</c>,
    /// <c>DELETE = 1</c>, <c>FILTER = 2</c>. Narrowing the contract with a defined error rather than
    /// widening it with a guess - here, rather than silently defaulting to the Primary buffer - is the
    /// refactor's standing rule for such a case, and it is a DECISION rather than a measurement.
    /// </para>
    /// <para>
    /// THERE IS NO SENTINEL MEMBER TO SCREEN, WHICH IS ITSELF THE POINT. A
    /// <c>DW_BUFFER_UNSPECIFIED = 0</c> would have added a state the legacy cannot express AND pushed
    /// <c>Primary</c> to 1, so every reader would have had to fold a wire number back onto the legacy
    /// alphabet - and the first one that forgot would read Primary as Delete with no error anywhere.
    /// Zero is <c>Primary</c> instead, which is the buffer every legacy call site means when it does
    /// not say otherwise, and it is a VALID argument: the rows below start at 3 for that reason.
    /// </para>
    /// <para>
    /// So the only value that can reach the screen from outside the domain is an out-of-range numeric
    /// cast - a wire payload carrying an unknown enum number, or a caller casting an arbitrary
    /// integer - and every such row is asserted here, including the number immediately above the
    /// domain and a negative one.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData((DwBuffer)3)]
    [InlineData((DwBuffer)4)]
    [InlineData((DwBuffer)77)]
    [InlineData((DwBuffer)(-1))]
    public void GetNextModified_RejectsAnyBufferValueOutsideTheThreeMemberDomain(DwBuffer buffer)
    {
        ArgumentOutOfRangeException failure = Assert.Throws<ArgumentOutOfRangeException>(
            () => ItemStatusMachine.GetNextModified(
                ItemStatusMachine.BeforeFirstRow, buffer, 3L, _ => ItemStatus.DataModified));

        Assert.Equal("buffer", failure.ParamName);
        Assert.Equal(buffer, failure.ActualValue);
    }

    /// <summary>
    /// The walk rejects a null accessor and either negative ordinal, naming the offending parameter.
    /// </summary>
    [Fact]
    public void GetNextModified_RejectsANullAccessorAndEitherNegativeOrdinal()
    {
        ArgumentNullException nullAccessor = Assert.Throws<ArgumentNullException>(
            () => ItemStatusMachine.GetNextModified(0L, DwBuffer.Primary, 1L, null!));
        Assert.Equal("rowStatusAt", nullAccessor.ParamName);

        ArgumentOutOfRangeException negativeSeed = Assert.Throws<ArgumentOutOfRangeException>(
            () => ItemStatusMachine.GetNextModified(-1L, DwBuffer.Primary, 1L, _ => ItemStatus.DataModified));
        Assert.Equal("afterRowNumber", negativeSeed.ParamName);

        ArgumentOutOfRangeException negativeCount = Assert.Throws<ArgumentOutOfRangeException>(
            () => ItemStatusMachine.GetNextModified(0L, DwBuffer.Primary, -1L, _ => ItemStatus.DataModified));
        Assert.Equal("rowCount", negativeCount.ParamName);
    }

    // ==========================================================================================
    //  EnumerateModifiedRows - the ergonomic form of the same walk
    // ==========================================================================================

    /// <summary>
    /// The traversal yields exactly the modified rows, ascending, and never yields the exhaustion
    /// sentinel.
    /// </summary>
    /// <remarks>
    /// The ergonomic form of the idiom the legacy writes out six times
    /// [<c>n_cst_threading_task_sqlupdate.sru:L138-L148</c>, <c>:L154-L164</c>, <c>:L172-L182</c>,
    /// <c>:L188-L198</c>, <c>:L321-L329</c>, <c>:L333-L341</c>]. The set is asserted rather than the
    /// count, because a count of three would also be produced by an off-by-one that yielded rows 1,
    /// 3 and 5.
    /// </remarks>
    [Fact]
    public void EnumerateModifiedRows_YieldsExactlyTheModifiedRowNumbersInAscendingOrder()
    {
        ItemStatus[] rows = SixRowBuffer();

        long[] visited = ItemStatusMachine
            .EnumerateModifiedRows(DwBuffer.Primary, rows.Length, OneBasedReader(rows))
            .ToArray();

        Assert.Equal([2L, 4L, 6L], visited);
        Assert.DoesNotContain(ItemStatusMachine.NoMoreModifiedRows, visited);
        Assert.All(visited, rowNumber => Assert.True(rowNumber >= ItemStatusMachine.FirstRowNumber));
    }

    /// <summary>
    /// A buffer with nothing modified, and an empty buffer, both yield an empty sequence rather than
    /// a single zero.
    /// </summary>
    /// <remarks>
    /// The distinction matters because the underlying walk COMMUNICATES exhaustion with the value
    /// zero. An implementation that forwarded the sentinel into the sequence would hand its caller a
    /// row number that addresses no row - and row zero would then be read as the row-status sentinel
    /// somewhere downstream.
    /// </remarks>
    [Fact]
    public void EnumerateModifiedRows_YieldsNothingRatherThanTheSentinelWhenThereIsNothingToYield()
    {
        ItemStatus[] unmodified = [ItemStatus.NotModified, ItemStatus.New];

        Assert.Empty(
            ItemStatusMachine.EnumerateModifiedRows(DwBuffer.Filter, unmodified.Length, OneBasedReader(unmodified)));
        Assert.Empty(
            ItemStatusMachine.EnumerateModifiedRows(DwBuffer.Primary, 0L, _ => ItemStatus.DataModified));
    }

    /// <summary>
    /// Every row is read exactly once across a full walk.
    /// </summary>
    /// <remarks>
    /// Matching the legacy, whose loop calls <c>GetItemStatus</c> once per iteration and caches
    /// nothing. Six reads for six rows: the walk neither re-reads a row it has already classified nor
    /// skips one.
    /// </remarks>
    [Fact]
    public void EnumerateModifiedRows_ReadsEveryRowExactlyOnceAcrossAFullWalk()
    {
        ItemStatus[] rows = SixRowBuffer();
        List<long> reads = [];

        _ = ItemStatusMachine
            .EnumerateModifiedRows(
                DwBuffer.Primary,
                rows.Length,
                rowNumber =>
                {
                    reads.Add(rowNumber);
                    return rows[(int)rowNumber - 1];
                })
            .ToArray();

        Assert.Equal([1L, 2L, 3L, 4L, 5L, 6L], reads);
    }

    /// <summary>
    /// The walk is LAZY: no row is read until the sequence is enumerated.
    /// </summary>
    /// <remarks>
    /// Explicitly stated in the production remarks and invisible to any test that only inspects the
    /// yielded rows. Asserted at three points - after the call, after one step, and after a full
    /// enumeration - because laziness is only meaningful if the first step does bounded work.
    /// </remarks>
    [Fact]
    public void EnumerateModifiedRows_ReadsNothingUntilItIsEnumerated()
    {
        ItemStatus[] rows = SixRowBuffer();
        int reads = 0;

        IEnumerable<long> lazy = ItemStatusMachine.EnumerateModifiedRows(
            DwBuffer.Primary,
            rows.Length,
            rowNumber =>
            {
                reads++;
                return rows[(int)rowNumber - 1];
            });

        Assert.Equal(0, reads);

        long firstModified = lazy.First();

        Assert.Equal(2L, firstModified);
        Assert.Equal(2, reads);

        _ = lazy.ToArray();

        Assert.Equal(8, reads);
    }

    /// <summary>
    /// Argument validation is EAGER even though the walk is lazy: a bad argument is reported by the
    /// call, not by the first enumeration step.
    /// </summary>
    /// <remarks>
    /// A C# iterator method defers its whole body until the first step, which would report a null
    /// accessor or a negative count at a point far from the mistake - often in a different stack
    /// frame entirely. The production code separates the two by validating, then delegating to a
    /// local iterator function. This is an implementation decision rather than legacy behaviour, and
    /// it is asserted because the "simplification" that would undo it - making the whole method an
    /// iterator - compiles and passes every other test in this file.
    /// </remarks>
    [Fact]
    public void EnumerateModifiedRows_ValidatesEagerlyRatherThanAtTheFirstEnumerationStep()
    {
        ArgumentNullException nullAccessor = Assert.Throws<ArgumentNullException>(
            () => ItemStatusMachine.EnumerateModifiedRows(DwBuffer.Primary, 3L, null!));
        Assert.Equal("rowStatusAt", nullAccessor.ParamName);

        ArgumentOutOfRangeException negativeCount = Assert.Throws<ArgumentOutOfRangeException>(
            () => ItemStatusMachine.EnumerateModifiedRows(DwBuffer.Primary, -1L, _ => ItemStatus.DataModified));
        Assert.Equal("rowCount", negativeCount.ParamName);

        ArgumentOutOfRangeException badBuffer = Assert.Throws<ArgumentOutOfRangeException>(
            () => ItemStatusMachine.EnumerateModifiedRows((DwBuffer)3, 3L, _ => ItemStatus.DataModified));
        Assert.Equal("buffer", badBuffer.ParamName);
    }

    /// <summary>
    /// The sequence is a LIVE VIEW rather than a snapshot: a status changed mid-walk is observed.
    /// </summary>
    /// <remarks>
    /// Exactly as in the legacy, whose loop re-reads the buffer on every iteration - which is what
    /// makes the update path able to stamp a status and have the walk respond. Here row 5 is promoted
    /// to modified while the walk sits on row 2, and the walk goes on to yield it. A caller needing a
    /// stable list must materialise one, and that is what the production remarks say.
    /// </remarks>
    [Fact]
    public void EnumerateModifiedRows_ObservesAStatusChangedDuringTheWalk()
    {
        ItemStatus[] rows = SixRowBuffer();
        List<long> visited = [];

        foreach (long rowNumber in ItemStatusMachine.EnumerateModifiedRows(
                     DwBuffer.Primary, rows.Length, OneBasedReader(rows)))
        {
            visited.Add(rowNumber);

            if (rowNumber == 2L)
            {
                // Row 5 was NotModified when the walk began.
                rows[4] = ItemStatus.DataModified;
            }
        }

        Assert.Equal([2L, 4L, 5L, 6L], visited);
    }

    /// <summary>
    /// The traversal and the single-step walk describe the SAME set of rows.
    /// </summary>
    /// <remarks>
    /// The traversal is built on the single-step walk, so agreement is expected - and asserting it
    /// is what would catch the two drifting apart if either grew its own bounds handling. Compared
    /// across several buffer shapes rather than one, so that the agreement is not an artefact of a
    /// single fixture.
    /// </remarks>
    [Fact]
    public void EnumerateModifiedRows_DescribesTheSameRowsAsRepeatedSingleSteps()
    {
        ItemStatus[][] shapes =
        [
            SixRowBuffer(),
            [ItemStatus.DataModified],
            [ItemStatus.NotModified],
            [ItemStatus.NewModified, ItemStatus.NewModified],
            [ItemStatus.New, (ItemStatus)4, ItemStatus.DataModified],
            [],
        ];

        foreach (ItemStatus[] rows in shapes)
        {
            Func<long, ItemStatus> read = OneBasedReader(rows);

            List<long> stepped = [];
            long rowNumber = ItemStatusMachine.GetNextModified(
                ItemStatusMachine.BeforeFirstRow, DwBuffer.Primary, rows.Length, read);
            while (rowNumber > ItemStatusMachine.NoMoreModifiedRows)
            {
                stepped.Add(rowNumber);
                rowNumber = ItemStatusMachine.GetNextModified(rowNumber, DwBuffer.Primary, rows.Length, read);
            }

            Assert.Equal(
                stepped,
                ItemStatusMachine.EnumerateModifiedRows(DwBuffer.Primary, rows.Length, read).ToList());
        }
    }

    // ==========================================================================================
    //  Shape
    // ==========================================================================================

    /// <summary>
    /// Both types are static and hold no state, so the machine is safe to call concurrently and
    /// carries no ordering dependency between calls.
    /// </summary>
    /// <remarks>
    /// Load-bearing rather than cosmetic: the legacy SQL task layer is worker-thread-affine, and a
    /// static mutable field here would be a concurrency defect the legacy does not have. The
    /// predicates are additionally order-independent because the golden-master fixture is SORTED -
    /// <c>sort="age A salary A "</c> [<c>dw_sqlite.srd:L14</c>] - so a row number in the primary
    /// parity path does not correlate with insertion order.
    /// </remarks>
    [Fact]
    public void BothTypes_AreStatelessStaticClasses()
    {
        foreach (Type type in new[] { typeof(ItemStatusMachine), typeof(ItemStatusExtensions) })
        {
            Assert.True(type.IsAbstract && type.IsSealed, $"{type.Name} should be a static class.");

            FieldInfo[] mutableState = type
                .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .Where(field => !field.IsLiteral && !field.IsInitOnly)
                .ToArray();

            Assert.Empty(mutableState);
        }
    }
}
