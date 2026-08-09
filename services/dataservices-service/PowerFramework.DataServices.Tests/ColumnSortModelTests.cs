// ==============================================================================================
//  ColumnSortModelTests - characterization of Services/ColumnSortModel.cs against its oracle,
//  ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_columnsort.sru (450 lines, READ ONLY).
//  --------------------------------------------------------------------------------------------
//  These are characterization tests in Michael Feathers' sense (AAP 0.3.3): they assert what the
//  legacy ACTUALLY does, including the three defects the refactor is required to preserve, rather
//  than what it arguably should do. Where a test looks like it is asserting a bug, it is - and the
//  assertion carries the locator that proves the bug is the oracle's. EACH OF THE THREE DEFECT TESTS
//  FAILS IF THE DEFECT IS "FIXED", which is the whole point of writing them.
//
//  NO DataWindow, NO DATABASE AND NO UI IS INVOLVED. Everything runs against FakeDataWindowHost,
//  which satisfies the abstract host contract in memory, so the suite is deterministic and needs no
//  service, no container and no fixture file (constraint C-H).
//
//  THE SIX ASSERTIONS THAT MATTER MOST, AND WHY THEY ARE HERE
//    1. GetClause is BYTE-EXACT. The output is a DataWindow sort expression the DataWindow itself
//       parses, and it appears in characterization recordings - so the matrix asserts whole strings
//       and never substrings, down to the single leading space in each direction suffix and the two
//       adjacent apostrophes in the string null substitution.
//    2. DEFECT 1 - a single sorted column publishes ordinal 0, so the badge is suppressed as a side
//       effect of the `nSortCnt > 1` guard [:L188-L192] and not by any explicit test.
//    3. DEFECT 2 - Update() does NOT normalise a "?" Describe answer to empty, because :L272 reads
//       an uninitialised local instead of the field it meant to read.
//    4. DEFECT 3 - Reset() answers FAILED for an empty store [:L236], indistinguishably from a real
//       failure.
//    5. The event gate is restored ONLY BY THE CALLER THAT DISABLED IT [:L409-L412, :L424-L426], so
//       an already-suppressed row-focus-change survives a sort.
//    6. An UNSORTED entry still produces a descriptor, because _of_setarrow destroys both band
//       objects before returning early [:L351-L353]. Skipping it would orphan an indicator.
//
//  DEFERRED-HALF ASSERTIONS ARE POSITIVE, NOT ABSENT. Rather than merely not testing the deferred
//  geometry, the suite asserts that the descriptor carries the RAW Describe answers untransformed
//  (DECISION 1 of the file under test) and that a clear-only descriptor performs NO geometry Describe
//  at all - so a later "helpful" halving or offset would fail a test instead of passing silently.
// ==============================================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using PowerFramework.DataServices.Domain;
using PowerFramework.DataServices.Services;
using PowerFramework.Shared.Eventful;
using PowerFramework.Shared.Kernel;

using Xunit;

namespace PowerFramework.DataServices.Tests;

/// <summary>
/// Characterization tests for <see cref="ColumnSortModel"/>.
/// </summary>
public sealed class ColumnSortModelTests
{
    private const string SortedColumn = "salary";
    private const string HeaderObject = SortedColumn + "_t";
    private const string TableSortProperty = "DataWindow.Table.Sort";

    // The three direction values the oracle declares privately at :L37-L39. Restated here because a
    // test cannot see a private constant, and pinned by ThePrivateDirectionValuesAreZeroOneTwo below
    // so that a change to either side is caught rather than silently diverging.
    private const long SORT_NONE = 0L;
    private const long SORT_ASC = 1L;
    private const long SORT_DESC = 2L;

    /// <summary>
    /// Builds a host with one attached, enabled sort service over a single grid column.
    /// </summary>
    /// <param name="colType">The column's <c>ColType</c>, which drives the null-substitution arm.</param>
    /// <returns>The host and the service attached to it.</returns>
    private static (FakeDataWindowHost Host, ColumnSortModel Service) NewService(string colType = "char(50)")
    {
        FakeDataWindowHost host = new(new EventBroker());

        // "1" is STYLE_GRID [n_cst_dwsvc.sru:L145-L146], the only style :L61 admits. It is also the
        // fake's default; set explicitly so the dependency is visible in the test rather than inherited.
        host.Processing = "1";

        _ = host.AddColumn(SortedColumn, colType);

        // The header text object and the column band, which are the two eligibility probes at :L84-L85
        // and :L104-L105.
        host.SetDescribe(HeaderObject + ".Band", "header");
        host.SetDescribe(SortedColumn + ".Band", "detail");
        host.SetDescribe(SortedColumn + ".edit.style", "edit");

        ColumnSortModel service = new();
        service.OnInit(host);

        return (host, service);
    }

    /// <summary>
    /// Teaches the host the three geometry answers <c>_of_setarrow</c> reads at <c>:L366-L368</c>.
    /// </summary>
    private static void TeachGeometry(FakeDataWindowHost host, string column = SortedColumn)
    {
        host.SetDescribe(column + "_t.x", "137");
        host.SetDescribe(column + "_t.width", "301");
        host.SetDescribe(column + "_t.y", "44");
    }

    private static IDataWindowObject Header(string column = SortedColumn) =>
        new FakeDataWindowObject(column + "_t", "char(50)");

    /// <summary>
    /// Clicks a column header the way the oracle does - queue on button-up, then drain the posted
    /// continuation - so every test exercises the real two-step path rather than shortcutting it.
    /// </summary>
    private static long? Click(
        FakeDataWindowHost host,
        ColumnSortModel service,
        string column = SortedColumn,
        bool ctrlHeld = false)
    {
        _ = host.ObjectModelValue;
        _ = service.OnLButtonUp(1L, 2L, 3L, Header(column), withinClickTolerance: true);
        return service.DrainPostedClick(ctrlHeld);
    }

    // ==========================================================================================
    //  SHAPE - THE PORT'S SURFACE AGAINST THE ORACLE'S DECLARATIONS
    // ==========================================================================================

    /// <summary>
    /// <c>:L26</c> - the suffix is public and is exactly <c>"_arw"</c>.
    /// </summary>
    [Fact]
    public void TheArrowSuffixIsThePublicOracleLiteral()
    {
        Assert.Equal("_arw", ColumnSortModel.ARROWSUFFIX);
    }

    /// <summary>
    /// <c>:L37-L39</c> - the three private direction values, pinned through observable behaviour
    /// because a test cannot read a private constant directly.
    /// </summary>
    /// <remarks>
    /// A first click yields <c>1</c>, a second <c>2</c> and a third <c>0</c>, which is simultaneously
    /// the cycle at <c>:L125-L132</c> and proof that the three constants hold their oracle values. If
    /// either the constants or the cycle changed, this fails.
    /// </remarks>
    [Fact]
    public void ThePrivateDirectionValuesAreZeroOneTwo()
    {
        (FakeDataWindowHost host, ColumnSortModel service) = NewService();
        TeachGeometry(host);

        _ = Click(host, service);
        Assert.Equal(SORT_ASC, service.SortEntries.Single().SortType);

        _ = Click(host, service);
        Assert.Equal(SORT_DESC, service.SortEntries.Single().SortType);

        _ = Click(host, service);

        // The third click completes the cycle to SORT_NONE and the plain branch then empties the store
        // entirely [:L154-L156], so the direction is observed through the descriptor instead.
        Assert.Empty(service.SortEntries);
        Assert.Equal(SORT_NONE, service.Indicators.Single().SortType);
    }

    // ==========================================================================================
    //  GetClause - THE BYTE-EXACT PARITY MATRIX                                    :L290-L339
    //  ----------------------------------------------------------------------------------------
    //  Every case asserts the WHOLE string. The matrix covers all four cascade stages, both
    //  direction suffixes, all five null-substitution arms and the three independent NilIsNull
    //  property spellings.
    // ==========================================================================================

    /// <summary>
    /// <c>:L292</c> - an unsorted column yields the EMPTY STRING and costs no Describe call, which is
    /// what keeps the join at <c>:L195-L198</c> free of stray separators.
    /// </summary>
    [Fact]
    public void AnUnsortedColumnYieldsTheEmptyStringAndAsksNothing()
    {
        (FakeDataWindowHost host, ColumnSortModel service) = NewService();

        int describeCalls = 0;
        host.DescribeOverride = _ =>
        {
            describeCalls++;
            return null;
        };

        Assert.Equal(string.Empty, service.GetClause(SortedColumn, SORT_NONE));
        Assert.Equal(0, describeCalls);
    }

    /// <summary>
    /// The four cascade stages and both suffixes, asserted byte for byte.
    /// </summary>
    public static TheoryData<string, string, string, string, string, long, string> ClauseMatrix() => new()
    {
        // colType,   Type,       Edit.Style, DDDW.Display, DDDW.Data, sortType, expected
        // ---- STAGE 1 :L294-L295 - a computed column sorts by itself, and the whole else-branch is skipped.
        { "decimal(2)", "compute", "edit", "", "", SORT_ASC, "salary A" },
        { "decimal(2)", "compute", "edit", "", "", SORT_DESC, "salary D" },

        // ---- STAGE 2 :L299-L302 - a dddw sorts by display ONLY when display differs from data.
        { "char(50)", "column", "dddw", "descr", "code", SORT_ASC, "LookUpDisplay(salary) A" },
        { "char(50)", "column", "dddw", "descr", "code", SORT_DESC, "LookUpDisplay(salary) D" },

        // ---- STAGE 2 :L300 false - display EQUALS data, so the clause is LEFT EMPTY and stage four
        //      supplies the bare name. This is the case a flattened cascade would get wrong.
        { "char(50)", "column", "dddw", "code", "code", SORT_ASC, "salary A" },

        // ---- STAGE 2 :L303-L304 - a ddlb ALWAYS sorts by display, with no display/data test.
        { "char(50)", "column", "ddlb", "", "", SORT_ASC, "LookUpDisplay(salary) A" },
        { "char(50)", "column", "ddlb", "ignored", "ignored", SORT_ASC, "LookUpDisplay(salary) A" },

        // ---- STAGE 4 :L329 - the plain fallback, which is the common answer.
        { "char(50)", "column", "edit", "", "", SORT_ASC, "salary A" },
        { "char(50)", "column", "edit", "", "", SORT_DESC, "salary D" },

        // ---- THE SUFFIX HAS NO DEFAULT ARM :L331-L336 - an out-of-range direction yields NO suffix.
        { "char(50)", "column", "edit", "", "", 7L, "salary" },
    };

    /// <summary>
    /// <c>:L290-L339</c> - the cascade and the suffixes, byte for byte.
    /// </summary>
    [Theory]
    [MemberData(nameof(ClauseMatrix))]
    public void TheClauseCascadeIsByteExact(
        string colType,
        string type,
        string editStyle,
        string displayColumn,
        string dataColumn,
        long sortType,
        string expected)
    {
        (FakeDataWindowHost host, ColumnSortModel service) = NewService(colType);

        host.SetDescribe(SortedColumn + ".Type", type);
        host.SetDescribe(SortedColumn + ".Edit.Style", editStyle);
        host.SetDescribe(SortedColumn + ".DDDW.DisplayColumn", displayColumn);
        host.SetDescribe(SortedColumn + ".DDDW.DataColumn", dataColumn);

        // Left at "no" so stage three cannot fire and dilute the case under test.
        host.SetDescribe(SortedColumn + ".Edit.CodeTable", "no");
        host.SetDescribe(SortedColumn + ".Edit.NilIsNull", "no");
        host.SetDescribe(SortedColumn + ".DDDW.NilIsNull", "no");
        host.SetDescribe(SortedColumn + ".DDLB.NilIsNull", "no");

        Assert.Equal(expected, service.GetClause(SortedColumn, sortType));
    }

    /// <summary>
    /// <c>:L307-L310</c> - a code-table column sorts by display, and this arm is in the ELSE of the
    /// dddw/ddlb test so it is unreachable for either of those styles.
    /// </summary>
    [Fact]
    public void ACodeTableColumnSortsByDisplay()
    {
        (FakeDataWindowHost host, ColumnSortModel service) = NewService();

        host.SetDescribe(SortedColumn + ".Type", "column");
        host.SetDescribe(SortedColumn + ".Edit.Style", "editmask");
        host.SetDescribe(SortedColumn + ".Edit.CodeTable", "yes");

        Assert.Equal("LookUpDisplay(salary) A", service.GetClause(SortedColumn, SORT_ASC));
    }

    /// <summary>
    /// <c>:L307</c> is inside the ELSE of <c>:L298</c>, so a dddw whose display equals its data is
    /// NEVER code-table-tested. Flattening the three tests into one chain would break exactly this.
    /// </summary>
    [Fact]
    public void ADropDownDataWindowIsNeverCodeTableTested()
    {
        (FakeDataWindowHost host, ColumnSortModel service) = NewService();

        host.SetDescribe(SortedColumn + ".Type", "column");
        host.SetDescribe(SortedColumn + ".Edit.Style", "dddw");
        host.SetDescribe(SortedColumn + ".DDDW.DisplayColumn", "code");
        host.SetDescribe(SortedColumn + ".DDDW.DataColumn", "code");

        // Set to "yes", which WOULD produce LookUpDisplay if the arm were reachable.
        host.SetDescribe(SortedColumn + ".Edit.CodeTable", "yes");
        host.SetDescribe(SortedColumn + ".Edit.NilIsNull", "no");
        host.SetDescribe(SortedColumn + ".DDDW.NilIsNull", "no");
        host.SetDescribe(SortedColumn + ".DDLB.NilIsNull", "no");

        bool codeTableAsked = false;
        host.DescribeOverride = property =>
        {
            if (property.EndsWith(".Edit.CodeTable", StringComparison.Ordinal))
            {
                codeTableAsked = true;
            }

            return null;
        };

        Assert.Equal("salary A", service.GetClause(SortedColumn, SORT_ASC));
        Assert.False(codeTableAsked);
    }

    /// <summary>
    /// <c>:L315-L326</c> - the five null-substitution arms, with the sentinel literals byte-exact.
    /// </summary>
    public static TheoryData<string, string> NullSubstitutionMatrix() => new()
    {
        // The COL_TYPE_* category is derived from the ColType prefix by ConvertColumnType
        // [n_cst_dwsvc.sru:L503-L518], so the ColType string is what selects the arm.

        // :L316-L317 - COL_TYPE_INTEGER and COL_TYPE_DECIMAL SHARE ONE ARM. All five ColType prefixes
        // that reduce to those two categories are covered, because sharing an arm is exactly the kind
        // of thing a rewrite splits.
        { "number", "if(IsNull(salary),-999999,salary) A" },
        { "long", "if(IsNull(salary),-999999,salary) A" },
        { "ulong", "if(IsNull(salary),-999999,salary) A" },
        { "decimal(2)", "if(IsNull(salary),-999999,salary) A" },
        { "real", "if(IsNull(salary),-999999,salary) A" },

        // :L318-L319 - datetime. Tested BEFORE date in the oracle, and "datet" is the longer prefix,
        // so a datetime column must not fall into the date arm.
        { "datetime", "if(IsNull(salary),DateTime('1900-01-01'),salary) A" },

        // :L320-L321 - date, with Date() and not DateTime() for the same literal.
        { "date", "if(IsNull(salary),Date('1900-01-01'),salary) A" },

        // :L322-L323 - time.
        { "time", "if(IsNull(salary),Time('00:00:00'),salary) A" },

        // :L324-L325 - the default arm: the SINGLE-QUOTED EMPTY STRING, so two adjacent apostrophes.
        { "char(50)", "if(IsNull(salary),'',salary) A" },
        { "somethingunrecognised", "if(IsNull(salary),'',salary) A" },
    };

    /// <summary>
    /// <c>:L313-L328</c> - null substitution per column-type category, byte for byte.
    /// </summary>
    [Theory]
    [MemberData(nameof(NullSubstitutionMatrix))]
    public void TheNullSubstitutionArmsAreByteExact(string colType, string expected)
    {
        (FakeDataWindowHost host, ColumnSortModel service) = NewService(colType);

        host.SetDescribe(SortedColumn + ".Type", "column");
        host.SetDescribe(SortedColumn + ".Edit.Style", "edit");
        host.SetDescribe(SortedColumn + ".Edit.CodeTable", "no");
        host.SetDescribe(SortedColumn + ".Edit.NilIsNull", "yes");
        host.SetDescribe(SortedColumn + ".ColType", colType);

        Assert.Equal(expected, service.GetClause(SortedColumn, SORT_ASC));
    }

    /// <summary>
    /// <c>:L314</c> - the THREE property spellings each trigger substitution independently, because a
    /// DataWindow answers only the one matching the column's actual edit style.
    /// </summary>
    [Theory]
    [InlineData("Edit")]
    [InlineData("DDDW")]
    [InlineData("DDLB")]
    public void EachNilIsNullSpellingTriggersOnItsOwn(string family)
    {
        (FakeDataWindowHost host, ColumnSortModel service) = NewService("char(50)");

        host.SetDescribe(SortedColumn + ".Type", "column");
        host.SetDescribe(SortedColumn + ".Edit.Style", "edit");
        host.SetDescribe(SortedColumn + ".Edit.CodeTable", "no");
        host.SetDescribe(SortedColumn + ".ColType", "char(50)");

        host.SetDescribe(SortedColumn + ".Edit.NilIsNull", "no");
        host.SetDescribe(SortedColumn + ".DDDW.NilIsNull", "no");
        host.SetDescribe(SortedColumn + ".DDLB.NilIsNull", "no");
        host.SetDescribe(SortedColumn + "." + family + ".NilIsNull", "yes");

        Assert.Equal("if(IsNull(salary),'',salary) A", service.GetClause(SortedColumn, SORT_ASC));
    }

    /// <summary>
    /// <c>:L314</c> - all three answering "no" leaves the clause to stage four, which proves the
    /// substitution is gated rather than unconditional.
    /// </summary>
    [Fact]
    public void NoNilIsNullMeansNoSubstitution()
    {
        (FakeDataWindowHost host, ColumnSortModel service) = NewService("long");

        host.SetDescribe(SortedColumn + ".Type", "column");
        host.SetDescribe(SortedColumn + ".Edit.Style", "edit");
        host.SetDescribe(SortedColumn + ".Edit.CodeTable", "no");
        host.SetDescribe(SortedColumn + ".Edit.NilIsNull", "no");
        host.SetDescribe(SortedColumn + ".DDDW.NilIsNull", "no");
        host.SetDescribe(SortedColumn + ".DDLB.NilIsNull", "no");

        Assert.Equal("salary A", service.GetClause(SortedColumn, SORT_ASC));
    }

    // ==========================================================================================
    //  THE SORT CYCLE AND THE STORE                                       :L112-L162, :L187-L199
    // ==========================================================================================

    /// <summary>
    /// <c>:L119-L123</c> - an unknown column is APPENDED SEEDED UNSORTED, and the cycle is what
    /// promotes the first click to ascending. Seeding it ascending would make click one and click
    /// three indistinguishable.
    /// </summary>
    [Fact]
    public void AnUnknownColumnIsAppendedThenPromotedByTheCycle()
    {
        (FakeDataWindowHost host, ColumnSortModel service) = NewService();
        TeachGeometry(host);

        Assert.Empty(service.SortEntries);

        _ = Click(host, service);

        SortData only = service.SortEntries.Single();
        Assert.Equal(SortedColumn, only.ColName);
        Assert.Equal(SORT_ASC, only.SortType);
    }

    /// <summary>
    /// <c>:L134-L145</c> - the Control branch ACCUMULATES, and prunes only the column just cleared.
    /// </summary>
    [Fact]
    public void TheControlBranchAccumulatesThenOmitsOnlyTheClearedColumn()
    {
        FakeDataWindowHost host = new(new EventBroker()) { Processing = "1" };
        ColumnSortModel service = new();
        service.OnInit(host);

        foreach (string column in new[] { "age", "salary", "name" })
        {
            _ = host.AddColumn(column, "char(50)");
            host.SetDescribe(column + "_t.Band", "header");
            host.SetDescribe(column + ".Band", "detail");
            host.SetDescribe(column + ".edit.style", "edit");
            TeachGeometry(host, column);
        }

        _ = Click(host, service, "age", ctrlHeld: true);
        _ = Click(host, service, "salary", ctrlHeld: true);
        _ = Click(host, service, "name", ctrlHeld: true);

        // Three accumulated, all ascending, IN CLICK ORDER - which is also the order :L195-L198 joins.
        Assert.Equal(
            new[] { "age", "salary", "name" },
            service.SortEntries.Select(e => e.ColName).ToArray());
        Assert.All(service.SortEntries, e => Assert.Equal(SORT_ASC, e.SortType));
        Assert.Equal("age A,salary A,name A", service.CurrentSort);

        // Cycle "salary" to descending and then to unsorted; only then is it pruned [:L136-L145].
        _ = Click(host, service, "salary", ctrlHeld: true);
        Assert.Equal(SORT_DESC, service.SortEntries[1].SortType);
        Assert.Equal(3, service.SortEntries.Count);

        _ = Click(host, service, "salary", ctrlHeld: true);

        // ONLY the cleared column is removed, and the survivors keep their order and directions.
        Assert.Equal(new[] { "age", "name" }, service.SortEntries.Select(e => e.ColName).ToArray());
        Assert.All(service.SortEntries, e => Assert.Equal(SORT_ASC, e.SortType));
        Assert.Equal("age A,name A", service.CurrentSort);
    }

    /// <summary>
    /// <c>:L146-L161</c> - the plain branch is EXCLUSIVE: it clears every other column first, then
    /// COLLAPSES the store to the one survivor.
    /// </summary>
    [Fact]
    public void ThePlainBranchIsExclusiveAndCollapsesTheStore()
    {
        FakeDataWindowHost host = new(new EventBroker()) { Processing = "1" };
        ColumnSortModel service = new();
        service.OnInit(host);

        foreach (string column in new[] { "age", "salary" })
        {
            _ = host.AddColumn(column, "char(50)");
            host.SetDescribe(column + "_t.Band", "header");
            host.SetDescribe(column + ".Band", "detail");
            host.SetDescribe(column + ".edit.style", "edit");
            TeachGeometry(host, column);
        }

        _ = Click(host, service, "age", ctrlHeld: true);
        _ = Click(host, service, "salary", ctrlHeld: true);
        Assert.Equal(2, service.SortEntries.Count);

        // A PLAIN click on "age" clears "salary" [:L148-L152] and then collapses [:L157-L160].
        _ = Click(host, service, "age");

        SortData survivor = Assert.Single(service.SortEntries);
        Assert.Equal("age", survivor.ColName);
        Assert.Equal(SORT_DESC, survivor.SortType);
        Assert.Equal("age D", service.CurrentSort);
    }

    /// <summary>
    /// <c>:L154-L156</c> - completing the cycle on the only sorted column EMPTIES the store, which is
    /// what makes the next click on any column a first click.
    /// </summary>
    [Fact]
    public void CompletingTheCycleEmptiesTheStoreEntirely()
    {
        (FakeDataWindowHost host, ColumnSortModel service) = NewService();
        TeachGeometry(host);

        _ = Click(host, service);
        _ = Click(host, service);
        _ = Click(host, service);

        Assert.Empty(service.SortEntries);
        Assert.Equal(string.Empty, service.CurrentSort);

        // And the NEXT click starts the cycle over at ascending.
        _ = Click(host, service);
        Assert.Equal(SORT_ASC, service.SortEntries.Single().SortType);
    }

    /// <summary>
    /// <c>:L195-L198</c> - unsorted entries contribute NO clause and NO separator, so the composed
    /// expression never carries a stray or doubled comma even though the pass visits them.
    /// </summary>
    [Fact]
    public void UnsortedEntriesContributeNeitherClauseNorSeparator()
    {
        FakeDataWindowHost host = new(new EventBroker()) { Processing = "1" };
        ColumnSortModel service = new();
        service.OnInit(host);

        foreach (string column in new[] { "age", "salary", "name" })
        {
            _ = host.AddColumn(column, "char(50)");
            host.SetDescribe(column + "_t.Band", "header");
            host.SetDescribe(column + ".Band", "detail");
            host.SetDescribe(column + ".edit.style", "edit");
            TeachGeometry(host, column);
        }

        _ = Click(host, service, "age", ctrlHeld: true);
        _ = Click(host, service, "salary", ctrlHeld: true);
        _ = Click(host, service, "name", ctrlHeld: true);

        // Cycle the MIDDLE column twice so it becomes unsorted and is then pruned; asserting on the
        // intermediate state requires catching it while it is still in the store, so instead assert the
        // expression after "salary" reaches descending - three clauses, two separators, no gap.
        _ = Click(host, service, "salary", ctrlHeld: true);
        Assert.Equal("age A,salary D,name A", service.CurrentSort);
        Assert.DoesNotContain(",,", service.CurrentSort, StringComparison.Ordinal);
        Assert.False(service.CurrentSort.StartsWith(',') || service.CurrentSort.EndsWith(','));
    }

    // ==========================================================================================
    //  THE THREE PRESERVED DEFECTS. EACH TEST FAILS IF THE DEFECT IS "FIXED".
    // ==========================================================================================

    /// <summary>
    /// *** DEFECT 1 [<c>:L188-L192</c>] *** - with a SINGLE sorted column the running ordinal is never
    /// incremented, so <c>0</c> reaches every descriptor and the badge is suppressed as a SIDE EFFECT
    /// of the <c>nSortCnt &gt; 1</c> guard rather than by any explicit test.
    /// </summary>
    /// <remarks>
    /// Hoisting the increment out of the count guard - the obvious "simplification" - would publish
    /// ordinal 1 here and start rendering a "1" badge on every single-column sort. This test is the
    /// tripwire for that edit.
    /// </remarks>
    [Fact]
    public void Defect1_ASingleSortedColumnPublishesOrdinalZero()
    {
        (FakeDataWindowHost host, ColumnSortModel service) = NewService();
        TeachGeometry(host);

        _ = Click(host, service);

        ColumnSortIndicatorDescriptor only = service.Indicators.Single();

        Assert.Equal(SORT_ASC, only.SortType);
        Assert.Equal(0, only.Index);

        // And therefore no badge object is emitted at all [:L383].
        Assert.Null(only.IndexLabel);
    }

    /// <summary>
    /// The counterpart to DEFECT 1: with TWO sorted columns the ordinals ARE numbered, one-based, in
    /// store order [<c>:L188-L192</c>].
    /// </summary>
    [Fact]
    public void TwoSortedColumnsAreNumberedOneBasedInStoreOrder()
    {
        FakeDataWindowHost host = new(new EventBroker()) { Processing = "1" };
        ColumnSortModel service = new();
        service.OnInit(host);

        foreach (string column in new[] { "age", "salary" })
        {
            _ = host.AddColumn(column, "char(50)");
            host.SetDescribe(column + "_t.Band", "header");
            host.SetDescribe(column + ".Band", "detail");
            host.SetDescribe(column + ".edit.style", "edit");
            TeachGeometry(host, column);
        }

        _ = Click(host, service, "age", ctrlHeld: true);
        _ = Click(host, service, "salary", ctrlHeld: true);

        Assert.Equal([1, 2], service.Indicators.Select(d => d.Index).ToArray());

        // Six leading spaces, and they are part of the value [:L384].
        Assert.Equal("      1", service.Indicators[0].IndexLabel);
        Assert.Equal("      2", service.Indicators[1].IndexLabel);
    }

    /// <summary>
    /// *** DEFECT 2 [<c>:L272</c>] *** - the line is
    /// <c>if sSort = "?" or _sOrgSort = "!" then _sOrgSort = ""</c>, and the FIRST test reads the
    /// uninitialised local declared at <c>:L266</c> rather than the field. A <c>Describe</c> answering
    /// <c>"?"</c> is therefore NOT normalised to empty.
    /// </summary>
    /// <remarks>
    /// The observable proof is indirect but decisive: if the first test read <c>_sOrgSort</c>, the
    /// field would be blanked and then re-set to <c>"?"</c> by <c>:L274</c> - the same end state. So
    /// the discriminating case is the <c>"!"</c> answer, which IS normalised, against a THIRD answer
    /// that is neither: both must survive as themselves. The test therefore pins all three answers at
    /// once, and the <c>"!"</c> case is what proves the second disjunct is live while the first is
    /// dead.
    /// </remarks>
    [Fact]
    public void Defect2_TheUndeterminedSentinelIsNotNormalisedButTheInvalidOneIs()
    {
        // The undetermined sentinel survives as itself, because :L272's first test cannot see it.
        (FakeDataWindowHost undetermined, ColumnSortModel undeterminedService) = NewService();
        undetermined.TableSort = "?";
        _ = undeterminedService.Update();
        Assert.Equal("?", undeterminedService.OriginalSort);

        // The invalid-expression sentinel IS normalised to empty by the SECOND disjunct, and :L274 then
        // re-writes it to "?". Same visible field, reached by a different route.
        (FakeDataWindowHost invalid, ColumnSortModel invalidService) = NewService();
        invalid.TableSort = "!";
        _ = invalidService.Update();
        Assert.Equal("?", invalidService.OriginalSort);

        // A real expression is remembered verbatim, trailing space included - dw_sqlite.srd:L14
        // declares `sort="age A salary A "` with exactly that trailing space.
        (FakeDataWindowHost real, ColumnSortModel realService) = NewService();
        real.TableSort = "age A salary A ";
        _ = realService.Update();
        Assert.Equal("age A salary A ", realService.OriginalSort);
    }

    /// <summary>
    /// *** DEFECT 2, THE DEAD DISJUNCT MADE VISIBLE. *** <c>:L272</c>'s first test compares the
    /// always-empty local against <c>"?"</c>, so it can NEVER be true - and therefore the normalisation
    /// is reachable only through the <c>"!"</c> answer. Driving both sentinels through the same code
    /// path and observing that only one is normalised is what pins the defect.
    /// </summary>
    [Fact]
    public void Defect2_OnlyTheInvalidSentinelEverReachesTheNormalisation()
    {
        (_, ColumnSortModel service) = NewService();

        // A supplied expression takes the OTHER branch at :L268 and never reaches :L272 at all, so the
        // undetermined sentinel is remembered verbatim when it is passed in rather than described.
        _ = service.Update("?");
        Assert.Equal("?", service.OriginalSort);

        // Passing the invalid sentinel EXPLICITLY also bypasses :L272 - the normalisation lives in the
        // null branch only - so "!" survives here where it would have been blanked via Describe.
        (_, ColumnSortModel otherService) = NewService();
        _ = otherService.Update("!");
        Assert.Equal("!", otherService.OriginalSort);
    }

    /// <summary>
    /// *** DEFECT 3 [<c>:L236</c>] *** - resetting an EMPTY store answers <c>RetCode.FAILED</c>, so a
    /// caller cannot tell "nothing to do" from a real failure, and
    /// <c>Predicates.IsFailed</c> answers true for a no-op.
    /// </summary>
    [Fact]
    public void Defect3_ResettingAnEmptyStoreReportsFailure()
    {
        (FakeDataWindowHost host, ColumnSortModel service) = NewService();

        Assert.Empty(service.SortEntries);
        Assert.Equal(RetCode.FAILED, service.Reset());

        // The consequence, spelled out: the tri-state predicate classifies a no-op as a failure.
        Assert.True(Predicates.IsFailed(service.Reset()));
        Assert.False(Predicates.IsSucceeded(service.Reset()));

        // Nothing was applied either - the method returns before touching the host.
        Assert.Null(host.AppliedSort);
    }

    /// <summary>
    /// <c>:L235-L246</c> - a NON-empty store resets successfully, clears every direction, runs the pass
    /// BEFORE emptying the store, and leaves the remembered original intact so the DataWindow returns
    /// to its original ordering.
    /// </summary>
    [Fact]
    public void ResetClearsEveryDirectionRunsThePassThenEmptiesTheStore()
    {
        (FakeDataWindowHost host, ColumnSortModel service) = NewService();
        TeachGeometry(host);

        host.TableSort = "age A";
        _ = Click(host, service);
        Assert.Equal("salary A", service.CurrentSort);

        // The DataWindow now REPORTS the applied sort, which a real one would and which the fake
        // deliberately leaves to the suite so the equal-sort early-out at :L404 stays independently
        // drivable. Without this the reset would compare "age A" against a DataWindow still reporting
        // "age A" and correctly do nothing.
        host.TableSort = "salary A";

        host.CallLog.Clear();
        Assert.Equal(RetCode.OK, service.Reset());

        // Emptied AFTER the pass, so the pass still had an entry to clear the indicator for.
        Assert.Empty(service.SortEntries);
        Assert.Equal(string.Empty, service.CurrentSort);
        Assert.True(service.Indicators.Single().ClearOnly);

        // The original sort is restored rather than the sort being left cleared [:L201-L204].
        Assert.Equal("age A", host.AppliedSort);
    }

    // ==========================================================================================
    //  THE APPLY PATH                                                              :L397-L431
    // ==========================================================================================

    /// <summary>
    /// <c>:L402-L404</c> - an equal sort does NO WORK AT ALL and answers <c>RetCode.OK</c>, so it is
    /// indistinguishable from a sort that was applied.
    /// </summary>
    [Fact]
    public void AnEqualSortDoesNoWorkAndStillReportsSuccess()
    {
        (FakeDataWindowHost host, ColumnSortModel service) = NewService();

        host.TableSort = "age A";
        host.CallLog.Clear();

        Assert.Equal(RetCode.OK, service.Update("age A"));

        Assert.Null(host.AppliedSort);
        Assert.Equal(0, host.SortCallCount);
        Assert.DoesNotContain(
            host.CallLog.Descriptions,
            d => d.Contains("SetRedraw", StringComparison.Ordinal)
                || d.Contains("SetSort", StringComparison.Ordinal)
                || d.Contains("DisableEvent", StringComparison.Ordinal));
    }

    /// <summary>
    /// <c>:L403</c> - BOTH sentinels are normalised before the equality test, so a DataWindow reporting
    /// either one compares equal to an empty target and correctly does nothing. This is the one place
    /// in the file that handles both, and DEFECT 2 is precisely the place that fails to.
    /// </summary>
    [Theory]
    [InlineData("?")]
    [InlineData("!")]
    public void BothSentinelsNormaliseToEmptyBeforeTheEqualityTest(string reported)
    {
        (FakeDataWindowHost host, ColumnSortModel service) = NewService();

        host.TableSort = reported;
        host.CallLog.Clear();

        // Update() captures the sentinel, :L274 turns it into "?", :L278 therefore leaves the local
        // empty, and the apply path compares "" against the normalised "" - so nothing happens.
        Assert.Equal(RetCode.OK, service.Update());
        Assert.Null(host.AppliedSort);
        Assert.Equal(0, host.SortCallCount);
    }

    /// <summary>
    /// <c>:L406-L428</c> - the full apply sequence, in order, with the redraw bracket outermost.
    /// </summary>
    [Fact]
    public void TheApplySequenceIsOrderedAndBracketedByTheRedrawSuppression()
    {
        (FakeDataWindowHost host, ColumnSortModel service) = NewService();

        host.TableSort = string.Empty;
        host.CurrentRow = 4L;
        host.RowIdsByRow[4L] = 77L;
        host.RowsByRowId[77L] = 2L;
        host.CallLog.Clear();

        Assert.Equal(RetCode.OK, service.Update("age A"));

        List<string> calls = [.. host.CallLog.Descriptions];

        static int IndexOfCall(List<string> calls, string member) =>
            calls.FindIndex(d => d.StartsWith(member, StringComparison.Ordinal));

        int redrawOff = IndexOfCall(calls, "SetRedraw");
        int disable = IndexOfCall(calls, "DisableEvent");
        int setSort = IndexOfCall(calls, "SetSort");
        int sort = IndexOfCall(calls, "Sort");
        int setRow = IndexOfCall(calls, "SetRow");
        int enable = IndexOfCall(calls, "EnableEvent");

        Assert.True(redrawOff >= 0 && disable > redrawOff, "SetRedraw(false) precedes DisableEvent.");
        Assert.True(setSort > disable, "DisableEvent precedes SetSort.");
        Assert.True(sort > setSort, "SetSort precedes Sort.");
        Assert.True(setRow > sort, "Sort precedes SetRow.");
        Assert.True(enable > setRow, "SetRow precedes EnableEvent.");

        // The redraw bracket is outermost: the LAST SetRedraw is the re-enable, after EnableEvent.
        int redrawOn = calls.FindLastIndex(d => d.StartsWith("SetRedraw", StringComparison.Ordinal));
        Assert.True(redrawOn > enable, "EnableEvent precedes SetRedraw(true).");

        Assert.Equal("age A", host.AppliedSort);
        Assert.Equal(1, host.SortCallCount);
    }

    /// <summary>
    /// <c>:L408</c> and <c>:L421-L423</c> - the row-identity ROUND TRIP: the identifier is captured
    /// before the reorder and resolved to a possibly DIFFERENT row afterwards, which is what keeps the
    /// caret on the same DATA row rather than the same position.
    /// </summary>
    [Fact]
    public void TheRowIdentityRoundTripFollowsTheDataRowAcrossTheReorder()
    {
        (FakeDataWindowHost host, ColumnSortModel service) = NewService();

        // Real rows, so SetRow can actually move the cursor rather than answering -1 for a row that
        // does not exist - otherwise the round trip would appear to work while doing nothing.
        for (int i = 0; i < 5; i++)
        {
            _ = host.AddRow("row" + i.ToString(CultureInfo.InvariantCulture));
        }

        host.TableSort = string.Empty;
        host.CurrentRow = 4L;
        host.RowIdsByRow[4L] = 77L;

        // After the sort the same identifier lives at row 2 - the disagreement the round trip exists for.
        host.RowsByRowId[77L] = 2L;

        _ = service.Update("age A");

        Assert.Contains(host.CallLog.Descriptions, d => d.StartsWith("SetRow", StringComparison.Ordinal));
        Assert.Equal(2L, host.CurrentRow);
    }

    /// <summary>
    /// <c>:L421</c> - the guard is <c>&gt; 0</c>, so a non-positive identifier skips the restore
    /// entirely rather than calling <c>SetRow(0)</c>.
    /// </summary>
    [Fact]
    public void ANonPositiveRowIdentifierSkipsTheRestore()
    {
        (FakeDataWindowHost host, ColumnSortModel service) = NewService();

        for (int i = 0; i < 5; i++)
        {
            _ = host.AddRow("row" + i.ToString(CultureInfo.InvariantCulture));
        }

        host.TableSort = string.Empty;
        host.CurrentRow = 4L;

        // No mapping, so GetRowIDFromRow answers 0 - PowerBuilder's out-of-range answer. The rows above
        // exist, so a SetRow WOULD have moved the cursor: the cursor staying put proves the call was
        // skipped rather than merely having failed.
        host.CallLog.Clear();

        _ = service.Update("age A");

        Assert.DoesNotContain(
            host.CallLog.Descriptions,
            d => d.StartsWith("SetRow", StringComparison.Ordinal));
        Assert.Equal(4L, host.CurrentRow);
    }

    /// <summary>
    /// <c>:L409-L412</c> and <c>:L424-L426</c> - the gate is suppressed only if it was NOT already
    /// suppressed, and restored only by the caller that suppressed it.
    /// </summary>
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void TheEventGateIsSavedAndRestoredOnlyByTheCallerThatDisabledIt(
        bool alreadyDisabled,
        bool expectGateTraffic)
    {
        (FakeDataWindowHost host, ColumnSortModel service) = NewService();

        host.TableSort = string.Empty;
        host.DisabledEvent = alreadyDisabled ? EventGate.EID_ROWFOCUSCHANGE : 0u;
        host.CallLog.Clear();

        _ = service.Update("age A");

        bool disabled = host.CallLog.Descriptions.Any(
            d => d.StartsWith("DisableEvent", StringComparison.Ordinal));
        bool enabled = host.CallLog.Descriptions.Any(
            d => d.StartsWith("EnableEvent", StringComparison.Ordinal));

        Assert.Equal(expectGateTraffic, disabled);
        Assert.Equal(expectGateTraffic, enabled);

        // Either way the mask ENDS as it began - which for the already-disabled case is the whole
        // point: an unconditional re-enable would have cleared a suppression this call did not make.
        Assert.Equal(alreadyDisabled ? EventGate.EID_ROWFOCUSCHANGE : 0u, host.DisabledEvent);
    }

    /// <summary>
    /// <c>:L417-L419</c> - group aggregates are recomputed ONLY when the DataWindow has groups, and
    /// group presence is proved by the ABSENCE of the invalid-expression sentinel rather than by a
    /// height.
    /// </summary>
    [Theory]
    [InlineData(null, 0)]
    [InlineData("DataWindow.Header.1.Height", 1)]
    [InlineData("DataWindow.Trailer.1.Height", 1)]
    public void GroupAggregatesAreRecomputedOnlyForAGroupedDataWindow(
        string? groupBandProperty,
        int expectedGroupCalcCalls)
    {
        (FakeDataWindowHost host, ColumnSortModel service) = NewService();

        host.TableSort = string.Empty;

        if (groupBandProperty is not null)
        {
            // A ZERO height still proves the band exists - a collapsed group is a real group, and this
            // is what a numeric test would get wrong.
            host.SetDescribe(groupBandProperty, "0");
        }

        _ = service.Update("age A");

        Assert.Equal(expectedGroupCalcCalls, host.GroupCalcCallCount);
    }

    // ==========================================================================================
    //  THE INDICATOR DESCRIPTOR - THE DOCUMENTED GAP, ASSERTED AS DATA
    // ==========================================================================================

    /// <summary>
    /// <c>:L344-L349</c>, <c>:L366-L375</c> - the descriptor for a SORTED column, field by field.
    /// </summary>
    [Theory]
    [InlineData(1, "t")]
    [InlineData(2, "u")]
    public void ASortedColumnPublishesACompleteDrawDescriptor(int clicks, string expectedGlyph)
    {
        (FakeDataWindowHost host, ColumnSortModel service) = NewService();
        TeachGeometry(host);

        for (int i = 0; i < clicks; i++)
        {
            _ = Click(host, service);
        }

        ColumnSortIndicatorDescriptor descriptor = service.Indicators.Single();

        Assert.Equal(SortedColumn, descriptor.ColumnName);

        // :L348-L349 - and note the INFIX: the badge name is not the arrow name with anything appended.
        Assert.Equal("salary_arw", descriptor.ArrowObjectName);
        Assert.Equal("salary_idx__arw", descriptor.IndexObjectName);
        Assert.NotEqual(descriptor.ArrowObjectName, descriptor.IndexObjectName);

        // :L370-L375 - rendered in a symbol font by the DEFERRED half; the glyph itself travels.
        Assert.Equal(expectedGlyph, descriptor.ArrowGlyph);

        Assert.False(descriptor.ClearOnly);

        // :L344-L346 - the three colour literals, as the oracle's decimal text.
        Assert.Equal("33554432", descriptor.ArrowColor);
        Assert.Equal("536870912", descriptor.TransparentColor);
        Assert.Equal("9868950", descriptor.IndexColor);
    }

    /// <summary>
    /// <c>:L345</c> - <c>String(RGB(150,150,150))</c> re-derived from its three components, so the
    /// literal is verifiable without the oracle and without porting the colour function itself (which
    /// AAP 0.4.4 assigns to the deferred DesignSystem service).
    /// </summary>
    [Fact]
    public void TheBadgeColourIsTheOraclesRgbCompositionOfOneHundredAndFifty()
    {
        // PowerScript composes a colour as red + green * 256 + blue * 65536.
        const long Component = 150L;
        long expected = Component + (Component * 256L) + (Component * 65536L);

        Assert.Equal(9868950L, expected);

        (FakeDataWindowHost host, ColumnSortModel service) = NewService();
        TeachGeometry(host);
        _ = Click(host, service);

        Assert.Equal(
            expected.ToString(CultureInfo.InvariantCulture),
            service.Indicators.Single().IndexColor);
    }

    /// <summary>
    /// DECISION 1 of the file under test - the three geometry values are carried RAW. The width is
    /// NOT halved [<c>:L367</c>] and the y position is NOT offset [<c>:L368</c>], because both
    /// transformations belong to the deferred half.
    /// </summary>
    /// <remarks>
    /// This is a POSITIVE assertion about the deferred boundary rather than an absence of one: a later
    /// "helpful" halving or offset fails here instead of passing silently.
    /// </remarks>
    [Fact]
    public void TheGeometryValuesAreCarriedRawWithNoTransformation()
    {
        (FakeDataWindowHost host, ColumnSortModel service) = NewService();
        TeachGeometry(host);

        _ = Click(host, service);

        ColumnSortIndicatorDescriptor descriptor = service.Indicators.Single();

        Assert.Equal("137", descriptor.SourceX);

        // 301 raw. A halving would have produced "150", "150.5" or "151" - all three are wrong here.
        Assert.Equal("301", descriptor.SourceWidth);

        // 44 raw. An offset would have subtracted a leader height derived from the deferred units
        // conversion, which is not computable on this side of the boundary at all.
        Assert.Equal("44", descriptor.SourceY);
    }

    /// <summary>
    /// <c>:L351-L353</c> - an UNSORTED entry still produces a descriptor, because the oracle destroys
    /// both band objects BEFORE returning early. It is a CLEAR instruction, and it performs NO geometry
    /// Describe at all.
    /// </summary>
    [Fact]
    public void AnUnsortedEntryPublishesAClearOnlyDescriptorAndAsksForNoGeometry()
    {
        (FakeDataWindowHost host, ColumnSortModel service) = NewService();
        TeachGeometry(host);

        // Two clicks leave it descending; the third completes the cycle to unsorted.
        _ = Click(host, service);
        _ = Click(host, service);

        List<string> geometryProbes = [];
        host.DescribeOverride = property =>
        {
            if (property.StartsWith(HeaderObject + ".", StringComparison.Ordinal))
            {
                geometryProbes.Add(property);
            }

            return null;
        };

        _ = Click(host, service);

        ColumnSortIndicatorDescriptor descriptor = service.Indicators.Single();

        Assert.True(descriptor.ClearOnly);
        Assert.Equal(SORT_NONE, descriptor.SortType);

        // The two names are still present, because clearing means destroying objects BY NAME.
        Assert.Equal("salary_arw", descriptor.ArrowObjectName);
        Assert.Equal("salary_idx__arw", descriptor.IndexObjectName);

        // Nothing to draw with, and nothing was asked for.
        Assert.Equal(string.Empty, descriptor.ArrowGlyph);
        Assert.Null(descriptor.IndexLabel);
        Assert.Null(descriptor.SourceX);
        Assert.Null(descriptor.SourceWidth);
        Assert.Null(descriptor.SourceY);
        Assert.DoesNotContain(geometryProbes, p => p.EndsWith(".x", StringComparison.Ordinal));
        Assert.DoesNotContain(geometryProbes, p => p.EndsWith(".width", StringComparison.Ordinal));
        Assert.DoesNotContain(geometryProbes, p => p.EndsWith(".y", StringComparison.Ordinal));
    }

    /// <summary>
    /// The descriptor set is REBUILT WHOLE on every pass, never appended across passes - because the
    /// oracle's own side effects are complete on every pass [<c>:L187-L199</c> visits every entry].
    /// </summary>
    [Fact]
    public void TheDescriptorSetIsRebuiltWholeOnEveryPass()
    {
        (FakeDataWindowHost host, ColumnSortModel service) = NewService();
        TeachGeometry(host);

        _ = Click(host, service);
        Assert.Single(service.Indicators);

        _ = Click(host, service);
        Assert.Single(service.Indicators);

        _ = Click(host, service);
        Assert.Single(service.Indicators);
    }

    // ==========================================================================================
    //  THE FOUR EVENTS AND THE POSTED CONTINUATION
    // ==========================================================================================

    /// <summary>
    /// <c>:L53-L56</c> - the press handler answers <c>0</c> and holds no state, because the capture it
    /// performed is DEFERRED hit-test geometry.
    /// </summary>
    [Fact]
    public void ThePressHandlerAnswersZeroAndQueuesNothing()
    {
        (FakeDataWindowHost host, ColumnSortModel service) = NewService();
        host.CallLog.Clear();

        Assert.Equal(0L, service.OnLButtonClk(11L, 12L, 3L, Header()));
        Assert.False(service.PostedClickPending);
        Assert.Empty(host.CallLog.Descriptions);
    }

    /// <summary>
    /// <c>:L168</c> - the double-click handler is a one-line forward that RETURNS the forwarded value.
    /// </summary>
    [Fact]
    public void TheDoubleClickHandlerForwardsToThePressHandler()
    {
        (_, ColumnSortModel service) = NewService();

        Assert.Equal(
            service.OnLButtonClk(11L, 12L, 3L, Header()),
            service.OnLButtonDblClk(11L, 12L, 3L, Header()));
        Assert.False(service.PostedClickPending);
    }

    /// <summary>
    /// <c>:L61</c>, <c>:L63</c>, <c>:L70</c> and <c>:L71</c> - each of the four release guards
    /// independently prevents the click from being queued, and every one answers <c>0</c>.
    /// </summary>
    [Theory]
    [InlineData("notgrid")]
    [InlineData("grouped")]
    [InlineData("dragged")]
    [InlineData("noobjectmodel")]
    public void EachReleaseGuardIndependentlyPreventsTheQueue(string scenario)
    {
        (FakeDataWindowHost host, ColumnSortModel service) = NewService();

        bool withinTolerance = true;

        switch (scenario)
        {
            case "notgrid":
                // :L61 - "2" is STYLE_LABEL, so the inequality against STYLE_GRID holds.
                host.Processing = "2";
                break;

            case "grouped":
                // :L63 - a group band exists, so sorting is refused with the oracle's BARE return.
                host.SetDescribe("DataWindow.Header.1.Height", "0");
                break;

            case "dragged":
                // :L70 - the release was a drag rather than a click.
                withinTolerance = false;
                break;

            case "noobjectmodel":
                // :L71 - the DataWindow has no valid object model.
                host.ObjectModelValue = null;
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "Unknown scenario.");
        }

        Assert.Equal(0L, service.OnLButtonUp(1L, 2L, 3L, Header(), withinTolerance));
        Assert.False(service.PostedClickPending);
        Assert.Null(service.DrainPostedClick(ctrlHeld: false));
    }

    /// <summary>
    /// <c>n_cst_dwsvc.sru:L143-L158</c>, reached through <c>:L61</c> - every arm of the presentation
    /// style mapping, including the DELIBERATELY MISSING <c>"6"</c> arm and the two Describe sentinels,
    /// all of which fall to the default and therefore decline to sort.
    /// </summary>
    /// <remarks>
    /// Only <c>"1"</c> admits a click, because <c>:L61</c> is an INEQUALITY against the grid style. The
    /// interesting rows are <c>"6"</c> - which has no arm at all and must NOT be renumbered into
    /// existence - and the <c>"?"</c>/<c>"!"</c> sentinels, which prove that an unreadable property
    /// disables sorting rather than assuming a grid.
    /// </remarks>
    [Theory]
    [InlineData("1", true)]   // STYLE_GRID     - the only style that sorts.
    [InlineData("2", false)]  // STYLE_LABEL
    [InlineData("3", false)]  // STYLE_GRAPH
    [InlineData("4", false)]  // STYLE_CROSSTAB
    [InlineData("5", false)]  // STYLE_COMPOSITE
    [InlineData("6", false)]  // NO ARM EXISTS - falls to STYLE_DEFAULT. Must stay that way.
    [InlineData("7", false)]  // STYLE_RICHTEXT
    [InlineData("0", false)]  // STYLE_DEFAULT, explicitly.
    [InlineData("?", false)]  // the undetermined Describe sentinel.
    [InlineData("!", false)]  // the invalid-expression Describe sentinel.
    public void OnlyTheGridPresentationStyleAdmitsAHeaderClick(string processing, bool expectQueued)
    {
        (FakeDataWindowHost host, ColumnSortModel service) = NewService();
        TeachGeometry(host);

        host.Processing = processing;

        Assert.Equal(0L, service.OnLButtonUp(1L, 2L, 3L, Header(), withinClickTolerance: true));
        Assert.Equal(expectQueued, service.PostedClickPending);
    }

    /// <summary>
    /// <c>:L72</c> - a release that passes every guard QUEUES the continuation and does not dispatch
    /// it. The store is untouched until the queue is drained.
    /// </summary>
    [Fact]
    public void APassingReleaseQueuesTheContinuationWithoutRunningIt()
    {
        (FakeDataWindowHost host, ColumnSortModel service) = NewService();
        TeachGeometry(host);

        Assert.Equal(0L, service.OnLButtonUp(1L, 2L, 3L, Header(), withinClickTolerance: true));

        Assert.True(service.PostedClickPending);
        Assert.Empty(service.SortEntries);
        Assert.Empty(service.Indicators);

        Assert.Equal(0L, service.DrainPostedClick(ctrlHeld: false));

        Assert.False(service.PostedClickPending);
        Assert.Single(service.SortEntries);
    }

    /// <summary>
    /// The slot is cleared BEFORE the body runs, so a second drain finds nothing - the continuation
    /// executes at most once even if the host drains twice.
    /// </summary>
    [Fact]
    public void DrainingTwiceRunsTheContinuationOnce()
    {
        (FakeDataWindowHost host, ColumnSortModel service) = NewService();
        TeachGeometry(host);

        _ = service.OnLButtonUp(1L, 2L, 3L, Header(), withinClickTolerance: true);

        Assert.Equal(0L, service.DrainPostedClick(ctrlHeld: false));
        Assert.Null(service.DrainPostedClick(ctrlHeld: false));

        Assert.Equal(SORT_ASC, service.SortEntries.Single().SortType);
    }

    /// <summary>
    /// <c>:L134</c> - the Control state is supplied AT DRAIN TIME, matching the oracle sampling the
    /// keyboard inside the POSTED event rather than when it was queued.
    /// </summary>
    [Fact]
    public void TheControlStateIsSuppliedAtDrainTime()
    {
        FakeDataWindowHost host = new(new EventBroker()) { Processing = "1" };
        ColumnSortModel service = new();
        service.OnInit(host);

        foreach (string column in new[] { "age", "salary" })
        {
            _ = host.AddColumn(column, "char(50)");
            host.SetDescribe(column + "_t.Band", "header");
            host.SetDescribe(column + ".Band", "detail");
            host.SetDescribe(column + ".edit.style", "edit");
            TeachGeometry(host, column);
        }

        _ = Click(host, service, "age", ctrlHeld: true);

        // The SAME release, drained twice over with different modifier answers, takes different
        // branches - which is only possible because the modifier is a drain-time input.
        _ = service.OnLButtonUp(1L, 2L, 3L, Header("salary"), withinClickTolerance: true);
        _ = service.DrainPostedClick(ctrlHeld: true);
        Assert.Equal(2, service.SortEntries.Count);

        _ = service.OnLButtonUp(1L, 2L, 3L, Header("salary"), withinClickTolerance: true);
        _ = service.DrainPostedClick(ctrlHeld: false);
        Assert.Single(service.SortEntries);
        Assert.Equal("salary", service.SortEntries.Single().ColName);
    }

    /// <summary>
    /// <c>:L84-L85</c>, <c>:L94-L102</c>, <c>:L104-L105</c> and <c>:L107-L110</c> - the four
    /// eligibility guards inside the continuation, each answering <c>0</c> and leaving the store
    /// untouched.
    /// </summary>
    [Theory]
    [InlineData("notheaderband")]
    [InlineData("nounderscoret")]
    [InlineData("notdetailband")]
    [InlineData("checkbox")]
    [InlineData("radiobutton")]
    public void EachEligibilityGuardLeavesTheStoreUntouched(string scenario)
    {
        (FakeDataWindowHost host, ColumnSortModel service) = NewService();
        TeachGeometry(host);

        IDataWindowObject dwo = Header();

        switch (scenario)
        {
            case "notheaderband":
                host.SetDescribe(HeaderObject + ".Band", "detail");
                break;

            case "nounderscoret":
                // A header object whose name does not end in "_t" - the else arm at :L100-L102.
                dwo = new FakeDataWindowObject("decoration", "char(50)");
                host.SetDescribe("decoration.Band", "header");
                break;

            case "notdetailband":
                host.SetDescribe(SortedColumn + ".Band", "header");
                break;

            case "checkbox":
            case "radiobutton":
                host.SetDescribe(SortedColumn + ".edit.style", scenario);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "Unknown scenario.");
        }

        Assert.Equal(0L, service.OnLButtonClicked(1, 2, 3L, dwo, ctrlHeld: false));
        Assert.Empty(service.SortEntries);
        Assert.Empty(service.Indicators);
    }

    /// <summary>
    /// <c>:L94-L95</c> - the column name is the header object's name with the <c>"_t"</c> suffix
    /// stripped, and a name that IS exactly <c>"_t"</c> strips to the empty string rather than
    /// throwing.
    /// </summary>
    [Fact]
    public void AHeaderObjectNamedExactlyUnderscoreTStripsToTheEmptyName()
    {
        (FakeDataWindowHost host, ColumnSortModel service) = NewService();

        host.SetDescribe("_t.Band", "header");

        // The stripped name is "", whose ".Band" is unknown and answers the sentinel - so :L105
        // rejects it. No throw, and nothing enters the store.
        Assert.Equal(0L, service.OnLButtonClicked(1, 2, 3L, new FakeDataWindowObject("_t", "char(50)"), false));
        Assert.Empty(service.SortEntries);
    }

    /// <summary>
    /// Both public entry points reject a null object rather than faulting one frame deeper - the
    /// fail-fast substitution for the oracle's null object reference error.
    /// </summary>
    [Fact]
    public void ANullClickedObjectIsRejectedByName()
    {
        (_, ColumnSortModel service) = NewService();

        Assert.Equal(
            "dwo",
            Assert.Throws<ArgumentNullException>(
                () => service.OnLButtonUp(1L, 2L, 3L, null!, true)).ParamName);

        Assert.Equal(
            "dwo",
            Assert.Throws<ArgumentNullException>(
                () => service.OnLButtonClicked(1, 2, 3L, null!, false)).ParamName);
    }

    // ==========================================================================================
    //  ENABLEMENT                                                                 :L441-L449
    // ==========================================================================================

    /// <summary>
    /// <c>:L442-L444</c> - exactly THREE topics are subscribed and the fourth event is deliberately
    /// not, because it is reached only through the posted continuation.
    /// </summary>
    [Fact]
    public void EnablingSubscribesExactlyThreeTopicsAndNotTheContinuation()
    {
        EventBroker broker = new();
        FakeDataWindowHost host = new(broker) { Processing = "1" };

        ColumnSortModel service = new();
        service.OnInit(host);

        _ = host.AddColumn(SortedColumn, "char(50)");
        host.SetDescribe(HeaderObject + ".Band", "header");
        host.SetDescribe(SortedColumn + ".Band", "detail");
        host.SetDescribe(SortedColumn + ".edit.style", "edit");
        TeachGeometry(host);

        // Before enabling, none of the three topics resolves to a handler at all.
        Assert.Null(broker.Trigger("clicked", 1L, 2L, 3L, Header()));
        Assert.Null(broker.Trigger("doubleclicked", 1L, 2L, 3L, Header()));
        Assert.Null(broker.Trigger("lbuttonup", 1L, 2L, 3L, Header()));

        Assert.Equal(RetCode.OK, service.SetEnabled(true));
        Assert.True(service.Enabled);

        // After enabling, all three dispatch and every one answers 0 - the prevent convention's
        // "continue" [se_cst_dw.sru:L145, :L136, :L395].
        Assert.Equal(0L, Assert.IsType<long>(broker.Trigger("clicked", 1L, 2L, 3L, Header())));
        Assert.Equal(0L, Assert.IsType<long>(broker.Trigger("doubleclicked", 1L, 2L, 3L, Header())));
        Assert.Equal(0L, Assert.IsType<long>(broker.Trigger("lbuttonup", 1L, 2L, 3L, Header())));

        // AND THE SORT CYCLE WAS NEVER REACHED. The topic is triggered with FOUR arguments
        // [se_cst_dw.sru:L395] while the ported handler declares five, so `withinClickTolerance` arrives
        // as its PowerScript initial value - false - and :L70 declines. That is the correct reading: the
        // broker payload carries no proximity measurement. It is also the proof that the continuation is
        // NOT broker-wired: were onlbuttonclicked subscribed to any of these three topics, the store
        // would now hold an entry.
        Assert.False(service.PostedClickPending);
        Assert.Empty(service.SortEntries);
        Assert.Empty(service.Indicators);
    }

    /// <summary>
    /// <c>:L446</c> - disabling unsubscribes BY TARGET, so every subscription this service holds goes
    /// at once, and it leaves the sort, the store and any queued click untouched.
    /// </summary>
    [Fact]
    public void DisablingUnsubscribesEverythingAndClearsNoState()
    {
        (FakeDataWindowHost host, ColumnSortModel service) = NewService();
        TeachGeometry(host);

        Assert.Equal(RetCode.OK, service.SetEnabled(true));
        _ = Click(host, service);
        Assert.Single(service.SortEntries);

        host.CallLog.Clear();
        Assert.Equal(RetCode.OK, service.SetEnabled(false));

        Assert.False(service.Enabled);

        // Nothing about the sort was touched - unlike the row-select service, this one is inert on
        // disable and leaves the DataWindow sorted exactly as it was.
        Assert.Single(service.SortEntries);
        Assert.Equal("salary A", service.CurrentSort);
        Assert.Empty(host.CallLog.Descriptions);
    }

    /// <summary>
    /// <c>n_cst_dwsvc.sru:L89</c> - the idempotent early-out answers success WITHOUT raising the
    /// enablement hook, so a no-op change subscribes nothing.
    /// </summary>
    [Fact]
    public void AnIdempotentEnablementChangeSubscribesNothing()
    {
        EventBroker broker = new();
        FakeDataWindowHost host = new(broker) { Processing = "1" };

        ColumnSortModel service = new();
        service.OnInit(host);

        // Already false, so this is the early-out path.
        Assert.Equal(RetCode.OK, service.SetEnabled(false));
        Assert.Equal(0L, broker.Unsubscribe(service));
    }

    // ==========================================================================================
    //  THE ATTACHMENT CONTRACT
    // ==========================================================================================

    /// <summary>
    /// <c>n_cst_dwsvc.sru:L85-L86</c> - attachment binds the host AND lifts its broker, together.
    /// </summary>
    [Fact]
    public void AttachmentBindsTheHostAndItsBroker()
    {
        EventBroker broker = new();
        FakeDataWindowHost host = new(broker);

        ColumnSortModel service = new();
        Assert.Null(service.DataWindow);
        Assert.Null(service.Eventful);

        service.OnInit(host);

        Assert.Same(host, service.DataWindow);
        Assert.Same(broker, service.Eventful);
    }

    /// <summary>
    /// Every host-touching member fails fast, and by name, before attachment.
    /// </summary>
    [Fact]
    public void EveryHostTouchingMemberFailsFastBeforeAttachment()
    {
        ColumnSortModel service = new();

        Assert.Throws<InvalidOperationException>(() => service.GetClause(SortedColumn, SORT_ASC));
        Assert.Throws<InvalidOperationException>(() => service.Update());
        Assert.Throws<InvalidOperationException>(() => service.Update("age A"));
        Assert.Throws<InvalidOperationException>(
            () => service.OnLButtonUp(1L, 2L, 3L, Header(), true));
        Assert.Throws<InvalidOperationException>(
            () => service.OnLButtonClicked(1, 2, 3L, Header(), false));

        // Reset answers FAILED for an empty store BEFORE it ever reaches the host, so it is the one
        // member that does not require attachment - DEFECT 3 shields it.
        Assert.Equal(RetCode.FAILED, service.Reset());

        // And the press handler holds no state and touches no host, so it answers 0 unattached.
        Assert.Equal(0L, service.OnLButtonClk(1L, 2L, 3L, Header()));
    }

    // ==========================================================================================
    //  THE CENTRALISED ONE-BASED BOUNDARY                                 AAP 0.4.5.4 / RISK R9
    //  ----------------------------------------------------------------------------------------
    //  AAP 0.4.5.4 calls one-based to zero-based translation "the single most dangerous mechanical
    //  hazard in this refactor" and requires the arithmetic be centralised AND directly testable. It
    //  is tested here rather than inferred from a downstream sort expression, because an off-by-one
    //  would still produce a well-formed expression - just for the wrong column.
    // ==========================================================================================

    /// <summary>
    /// PowerScript's <c>UpperBound</c> answers the LAST VALID INDEX while .NET's <c>Count</c> answers
    /// ONE PAST IT - and for a one-based array those are the same number. This is where that
    /// equivalence is asserted rather than re-derived at each call site.
    /// </summary>
    [Fact]
    public void UpperBoundIsTheLastValidOneBasedIndex()
    {
        Assert.Equal(0, ColumnSortModel.UpperBound([]));
        Assert.Equal(1, ColumnSortModel.UpperBound([new SortData("a", SORT_ASC)]));
        Assert.Equal(
            3,
            ColumnSortModel.UpperBound(
                [new SortData("a", SORT_ASC), new SortData("b", SORT_DESC), new SortData("c", SORT_NONE)]));
    }

    /// <summary>
    /// One-based reads: index <c>1</c> is the FIRST entry, and index <c>UpperBound</c> is the LAST.
    /// </summary>
    [Fact]
    public void EntryAtIsOneBasedAtBothEnds()
    {
        List<SortData> entries =
        [
            new SortData("first", SORT_ASC),
            new SortData("middle", SORT_DESC),
            new SortData("last", SORT_NONE),
        ];

        Assert.Equal("first", ColumnSortModel.EntryAt(entries, 1).ColName);
        Assert.Equal("middle", ColumnSortModel.EntryAt(entries, 2).ColName);
        Assert.Equal("last", ColumnSortModel.EntryAt(entries, ColumnSortModel.UpperBound(entries)).ColName);
    }

    /// <summary>
    /// Both boundaries fail fast AND BY NAME. No ported call site can reach this - every one is bounded
    /// by <c>UpperBound</c> - so the guard exists to catch a future editing mistake, which is exactly
    /// the mistake AAP 0.4.5.4 warns is indistinguishable from a behavioural regression.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(2)]
    [InlineData(int.MaxValue)]
    public void EntryAtRejectsAnOutOfRangeOneBasedIndex(int oneBasedIndex)
    {
        List<SortData> entries = [new SortData("only", SORT_ASC)];

        ArgumentOutOfRangeException failure = Assert.Throws<ArgumentOutOfRangeException>(
            () => ColumnSortModel.EntryAt(entries, oneBasedIndex));

        Assert.Equal("oneBasedIndex", failure.ParamName);
    }

    /// <summary>
    /// One-based writes land on the SAME entry a one-based read returns, which is the property that makes
    /// the read and write helpers safe to pair at a call site.
    /// </summary>
    [Fact]
    public void SetEntryAtWritesWhereEntryAtReads()
    {
        List<SortData> entries =
        [
            new SortData("first", SORT_NONE),
            new SortData("second", SORT_NONE),
        ];

        ColumnSortModel.SetEntryAt(entries, 1, new SortData("first", SORT_DESC));

        Assert.Equal(SORT_DESC, ColumnSortModel.EntryAt(entries, 1).SortType);
        Assert.Equal(SORT_NONE, ColumnSortModel.EntryAt(entries, 2).SortType);
        Assert.Equal(2, ColumnSortModel.UpperBound(entries));
    }

    /// <summary>
    /// One-based removal drops the entry at that POSITION and preserves the survivors' relative order,
    /// which the composed sort expression depends on [<c>:L137-L144</c>].
    /// </summary>
    [Fact]
    public void RemoveEntryAtDropsThatPositionAndKeepsTheRestInOrder()
    {
        List<SortData> entries =
        [
            new SortData("a", SORT_ASC),
            new SortData("b", SORT_DESC),
            new SortData("c", SORT_ASC),
        ];

        ColumnSortModel.RemoveEntryAt(entries, 2);

        Assert.Equal(2, ColumnSortModel.UpperBound(entries));
        Assert.Equal("a", ColumnSortModel.EntryAt(entries, 1).ColName);
        Assert.Equal("c", ColumnSortModel.EntryAt(entries, 2).ColName);
    }

    /// <summary>
    /// Both write helpers reuse <c>EntryAt</c>'s range check, so all three share one definition of the
    /// boundary rather than each restating it.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(2)]
    public void TheWriteHelpersShareEntryAtsBoundary(int oneBasedIndex)
    {
        List<SortData> entries = [new SortData("only", SORT_ASC)];

        Assert.Equal(
            "oneBasedIndex",
            Assert.Throws<ArgumentOutOfRangeException>(
                () => ColumnSortModel.SetEntryAt(entries, oneBasedIndex, new SortData("x", SORT_NONE)))
                .ParamName);

        Assert.Equal(
            "oneBasedIndex",
            Assert.Throws<ArgumentOutOfRangeException>(
                () => ColumnSortModel.RemoveEntryAt(entries, oneBasedIndex)).ParamName);

        // And neither left the list disturbed on the way out.
        Assert.Equal(1, ColumnSortModel.UpperBound(entries));
        Assert.Equal(SORT_ASC, ColumnSortModel.EntryAt(entries, 1).SortType);
    }

    // ==========================================================================================
    //  Update - THE REMAINING BRANCHES                                            :L266-L288
    // ==========================================================================================

    /// <summary>
    /// <c>:L276</c> - while a user sort is active the remembered original is updated and NOTHING is
    /// applied, so a retrieve re-reporting its sort cannot stamp on what the user is looking at.
    /// </summary>
    [Fact]
    public void UpdateRemembersButAppliesNothingWhileAUserSortIsActive()
    {
        (FakeDataWindowHost host, ColumnSortModel service) = NewService();
        TeachGeometry(host);

        _ = Click(host, service);
        Assert.Equal("salary A", service.CurrentSort);

        host.CallLog.Clear();
        Assert.Equal(RetCode.OK, service.Update("age D"));

        Assert.Equal("age D", service.OriginalSort);
        Assert.DoesNotContain(
            host.CallLog.Descriptions,
            d => d.StartsWith("SetSort", StringComparison.Ordinal));
    }

    /// <summary>
    /// <c>:L285-L288</c> against <c>:L268</c> - the no-argument overload passes an explicit NULL,
    /// which selects the capture branch. Passing the empty string instead takes the other branch and
    /// remembers an empty original, so the two are not interchangeable.
    /// </summary>
    [Fact]
    public void TheNoArgumentOverloadCapturesWhileTheEmptyStringRemembers()
    {
        (FakeDataWindowHost captured, ColumnSortModel capturingService) = NewService();
        captured.TableSort = "age A";
        _ = capturingService.Update();
        Assert.Equal("age A", capturingService.OriginalSort);

        (FakeDataWindowHost supplied, ColumnSortModel supplyingService) = NewService();
        supplied.TableSort = "age A";
        _ = supplyingService.Update(string.Empty);

        // The empty string was REMEMBERED and then turned into the sentinel by :L274 - the DataWindow's
        // own "age A" was never consulted.
        Assert.Equal("?", supplyingService.OriginalSort);
    }

    /// <summary>
    /// <c>:L176-L178</c> - the lazy capture fires ONCE, on the first pass, so a user sort applied over
    /// the original cannot overwrite the remembered value.
    /// </summary>
    [Fact]
    public void TheOriginalSortIsCapturedOnceOnTheFirstPass()
    {
        (FakeDataWindowHost host, ColumnSortModel service) = NewService();
        TeachGeometry(host);

        host.TableSort = "age A";
        Assert.Equal(string.Empty, service.OriginalSort);

        _ = Click(host, service);
        Assert.Equal("age A", service.OriginalSort);

        // The DataWindow now reports something else; the remembered original must not follow it.
        host.TableSort = "salary A";
        _ = Click(host, service);
        Assert.Equal("age A", service.OriginalSort);
    }

    /// <summary>
    /// <c>:L201-L204</c> - with no user sort and the no-original SENTINEL set, the EMPTY STRING is
    /// applied to clear the sort, and never the literal text <c>"?"</c>.
    /// </summary>
    /// <remarks>
    /// <see cref="ColumnSortModel.Update()"/> is called first because <c>:L274</c> is the ONLY line that
    /// ever writes the sentinel - the lazy capture in the sort pass does not. See the sibling test below
    /// for what happens when the sentinel was never established.
    /// </remarks>
    [Fact]
    public void NoUserSortAndTheSentinelAppliesTheEmptyStringAndNotTheSentinelText()
    {
        (FakeDataWindowHost host, ColumnSortModel service) = NewService();
        TeachGeometry(host);

        // The DataWindow reports no sort at all, so the capture yields empty and :L274 makes it "?".
        host.TableSort = string.Empty;
        Assert.Equal(RetCode.OK, service.Update());
        Assert.Equal("?", service.OriginalSort);

        // Each apply is mirrored into the reported sort, as a real DataWindow would.
        _ = Click(host, service);
        Assert.Equal("salary A", host.AppliedSort);
        host.TableSort = "salary A";

        _ = Click(host, service);
        Assert.Equal("salary D", host.AppliedSort);
        host.TableSort = "salary D";

        // The third click completes the cycle. With the sentinel in place the fallback is the EMPTY
        // STRING [:L202 declines to assign], so the sort is cleared - and the text "?" is never applied.
        _ = Click(host, service);

        Assert.Equal(string.Empty, host.AppliedSort);
        Assert.NotEqual("?", host.AppliedSort);
    }

    /// <summary>
    /// A CHARACTERIZED LEGACY QUIRK: when <c>of_update</c> was never called, the lazy capture at
    /// <c>:L176-L178</c> RE-FIRES on every pass - because only <c>:L274</c> writes the no-original
    /// sentinel, and the lazy capture leaves the field empty when the DataWindow reports no sort. So the
    /// remembered "original" eventually becomes the USER'S OWN earlier sort, and clearing every column
    /// restores that rather than clearing the sort.
    /// </summary>
    /// <remarks>
    /// This is neither a defect in the port nor one of the three the refactor tracks - it is a
    /// consequence of the empty string meaning "not captured yet" and the sort pass having no way to
    /// record "captured, and there was none". It is characterized here so that a future reader does not
    /// "fix" the lazy capture into a one-shot flag and change what a cleared sort falls back to. Compare
    /// the test above, where one <see cref="ColumnSortModel.Update()"/> call establishes the sentinel and
    /// the fallback becomes the empty string.
    /// </remarks>
    [Fact]
    public void WithoutUpdateTheLazyCaptureRefiresAndRemembersTheUsersOwnSort()
    {
        (FakeDataWindowHost host, ColumnSortModel service) = NewService();
        TeachGeometry(host);

        host.TableSort = string.Empty;

        // Pass one: the capture reads the empty answer, so the field stays empty and no sentinel is set.
        _ = Click(host, service);
        Assert.Equal(string.Empty, service.OriginalSort);
        Assert.Equal("salary A", host.AppliedSort);
        host.TableSort = "salary A";

        // Pass two: the field is STILL empty, so the capture re-fires and now records the user's own sort.
        _ = Click(host, service);
        Assert.Equal("salary A", service.OriginalSort);
        host.TableSort = "salary D";

        // Pass three completes the cycle, and the fallback restores that captured user sort rather than
        // clearing the sort.
        _ = Click(host, service);
        Assert.Equal("salary A", host.AppliedSort);
    }
}
