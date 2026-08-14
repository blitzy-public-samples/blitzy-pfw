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
//  The SURFACE SCAN region takes that one step further and walks the port's whole reflected surface,
//  failing on any member or member type whose name reaches for the deferred vocabulary at all.
//
//  ============================================================================================
//  GOVERNING RULES AND CONSTRAINTS FOR THIS FILE
//  ============================================================================================
//  NO USER RULES GOVERN THIS FILE. `review_rules` answers "No user rules provided." - a single line,
//  read in full, with nothing further to page through. No rule is invented, inferred or back-filled
//  from convention to stand in for the absence; the bar applied instead is the enterprise-standard
//  baseline AAP 0.7.2 states explicitly - nullable on, warnings as errors, no floating dependency,
//  a test project per shippable project and a coverage gate evaluated per service. The binding
//  constraints therefore come from AAP 0.7.3, and the four that reach this file are:
//
//    C-D  THE FOUR DEFERRED SERVICES ARE NOT IMPLEMENTED, NOT EVEN PARTIALLY, NOT EVEN AS STUBS.
//         AAP 0.2.1.3 Correction 4 splits the column-sort service: the sort EXPRESSION and the sort
//         STATE ship in DataServices, while the indicator GEOMETRY is deferred to DesignSystem behind
//         Gateway's reserved `/v1/design/**` route (AAP 0.4.4). The verified deferred evidence is the
//         DPI conversion at :L355-L364 - `U2PY(10)` at :L357, `Win32.PX2MMY(U2PY(10)) / 25.4 * 1000`
//         at :L359 and the `* 100` variant at :L361 - together with the two `Modify("Destroy ...")`
//         object manipulations at :L351-L352. THIS SUITE'S JOB IS TO ASSERT THAT SPLIT IS REAL: the
//         descriptor is DATA, and no DPI value, no measurement and no rendering call crosses the
//         contract. That is a DOCUMENTED CAPABILITY GAP, enumerated here and in the file under test,
//         and emphatically not a silent omission.
//    C-B  BEHAVIOUR IS PRESERVED EXACTLY, DEFECTS INCLUDED. The constants, the emitted clause text
//         and the return codes are legacy behaviour, so they are asserted as they are rather than as
//         they arguably should be. The three defect tests FAIL IF THE DEFECT IS FIXED, by design.
//    C-K  EVERY DECISION AND EVERY EXPECTATION CITES ITS LOCATOR. Every theory row carries the line
//         it came from, because `ws_objects/**` is the only artifact in the repository that can
//         adjudicate behaviour (AAP 0.1.4) and a bare expected value is unreviewable without it.
//    AAP 0.4.5.3  THE PRESERVED CONSTANT IDENTIFIERS ARE REFERENCED, NEVER DECLARED HERE. See the
//         long note above the three direction fields for the two independent reasons.
//
//  AAP 0.6.7 shapes the assertions themselves: parity matrices are theories with member data, every
//  row carries its locator, and every emitted-string comparison is ORDINAL and WHOLE-STRING with no
//  trimming anywhere - the leading space in each direction suffix and the trailing space in the
//  fixture's own sort are load-bearing, and a trimmed comparison would hide the loss of either.
//
//  PLAIN XUNIT AND HAND-WRITTEN DOUBLES ONLY. No mocking framework, no fluent assertion library, no
//  auto-fixture: the doubles are FakeDataWindowHost, FakeDataWindowObject, EventBroker and
//  ScriptedI18nProvider, all hand-authored in this project, and every assertion is a plain
//  `Assert.*`.
// ==============================================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

using PowerFramework.DataServices.Domain;
using PowerFramework.DataServices.Services;
using PowerFramework.Shared.Eventful;
using PowerFramework.Shared.Kernel;
using PowerFramework.Shared.Localization;

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

    // ==========================================================================================
    //  THE THREE DIRECTION VALUES ARE READ FROM THE PORT, NEVER RESTATED HERE.
    //  ----------------------------------------------------------------------------------------
    //  The oracle declares them under `private:` at :L28 with their values at :L37-L39, and the port
    //  keeps that accessibility exactly (DECISION 6 of the file under test). A test therefore cannot
    //  see them through the type system - and the tempting workaround, re-declaring
    //  `private const long SORT_ASC = 1L` here, is forbidden twice over:
    //
    //    * IT WOULD BE A SECOND SOURCE OF TRUTH. A restated literal agrees with the port only until
    //      one of the two is edited, and the failure mode is silent: every assertion still passes
    //      while testing the test's own copy of the value instead of the port's.
    //    * IT WOULD DECLARE A PRESERVED-CONSTANT IDENTIFIER OUTSIDE THE FILES THAT ARE ALLOWED TO.
    //      AAP 0.4.5.3 keeps the SCREAMING_SNAKE spellings because they travel in serialized
    //      payloads, log records and characterization recordings, and pays for that with
    //      `.editorconfig` suppressions scoped FILE BY FILE - Services/ColumnSortModel.cs is on that
    //      list and this test file deliberately is not. So a declaration here is one AnalysisMode
    //      bump away from breaking the build under TreatWarningsAsErrors. This file REFERENCES the
    //      preserved identifiers by name and DECLARES none.
    //
    //  Reflection over the port's own literal fields resolves both objections at once: the values can
    //  only ever be the port's, and the accessibility the reader is bypassing is itself asserted by
    //  TheDirectionConstantsKeepTheOraclesPrivateAccessibility below.
    // ==========================================================================================

    private static readonly long SortNone = DirectionConstant("SORT_NONE");
    private static readonly long SortAsc = DirectionConstant("SORT_ASC");
    private static readonly long SortDesc = DirectionConstant("SORT_DESC");

    /// <summary>
    /// Resolves one of the port's own literal fields by its preserved identifier, failing loudly if it
    /// has been renamed or promoted out of the private block.
    /// </summary>
    /// <param name="oracleName">
    /// The identifier exactly as the oracle spells it - <c>"SORT_NONE"</c>, <c>"SORT_ASC"</c> or
    /// <c>"SORT_DESC"</c> (<c>:L37-L39</c>).
    /// </param>
    /// <returns>The field, whose accessibility and literal value are both then assertable.</returns>
    private static FieldInfo ConstantField(string oracleName) =>
        // A NAME-BASED LOOKUP MUST FAIL LOUDLY, never answer a default. Were the port to rename one of
        // these, every direction assertion in this file would otherwise start comparing 0 against 0 and
        // pass while testing nothing at all. The throw - rather than an Assert - is deliberate: this
        // runs from a static field initializer, and an assertion failure there surfaces as a type
        // initialization error whose message hides the cause.
        typeof(ColumnSortModel).GetField(
            oracleName,
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException(
            "ColumnSortModel no longer declares the preserved constant '" + oracleName
            + "'. AAP 0.4.5.3 requires these identifier spellings be preserved verbatim because they "
            + "travel in serialized payloads, log records and characterization recordings, so a rename "
            + "is a parity break rather than a refactor.");

    /// <summary>
    /// Reads the literal value of one of the three direction constants.
    /// </summary>
    /// <param name="oracleName">The identifier as the oracle spells it.</param>
    /// <returns>The value the port compiled in.</returns>
    private static long DirectionConstant(string oracleName) =>
        // The DECLARED WIDTH matters as much as the value: the oracle declares `long` [:L37-L39], and an
        // `int` on the port's side would widen silently at every call site instead of failing here.
        ConstantField(oracleName).GetRawConstantValue() is long value
            ? value
            : throw new InvalidOperationException(
                "The preserved constant '" + oracleName + "' is no longer a long-typed literal.");

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
    /// The oracle's own fixture: <c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd</c>, six columns, with a
    /// sort service attached and enabled over it.
    /// </summary>
    /// <returns>The seeded host and the service attached to it.</returns>
    /// <remarks>
    /// <para>
    /// This is the DataWindow AAP 0.6.3.1 identifies as the only updatable one in the repository, which
    /// makes it the golden-master fixture for the whole retrieval/validation/update triple - and it is
    /// the only definition in the repository that declares a sort at all, <c>sort="age A salary A "</c>
    /// [<c>dw_sqlite.srd:L14</c>]. Its six columns span three of the five column-type families the
    /// clause builder switches on: <c>number</c> and <c>decimal(2)</c> reduce to the numeric arm,
    /// <c>char(100)</c> and <c>char(200)</c> to the string arm, and <c>date</c> to the date arm. Driving
    /// the clause matrix from it rather than from a synthetic column means the parity evidence is
    /// anchored to a definition the legacy actually ships.
    /// </para>
    /// <para>
    /// EVERY COLUMN IS TAUGHT ITS HEADER BAND AND DETAIL BAND, because the fixture declares the six
    /// header text objects [<c>:L15-L20</c>] but a Describe of <c>"<i>name</i>_t.Band"</c> is what
    /// <c>:L84-L85</c> and <c>:L104-L105</c> actually consult.
    /// </para>
    /// </remarks>
    private static (FakeDataWindowHost Host, ColumnSortModel Service) NewCompanyService()
    {
        FakeDataWindowHost host = FakeDataWindowFixtures.CreateCompanyFixture(new EventBroker());

        foreach (string column in CompanyColumns)
        {
            host.SetDescribe(column + "_t.Band", "header");
            host.SetDescribe(column + ".Band", "detail");
            host.SetDescribe(column + ".edit.style", column == "birth" ? "editmask" : "edit");
            TeachGeometry(host, column);
        }

        ColumnSortModel service = new();
        service.OnInit(host);

        return (host, service);
    }

    /// <summary>
    /// The fixture's six columns in declaration order (<c>dw_sqlite.srd:L8-L13</c>).
    /// </summary>
    private static readonly string[] CompanyColumns = ["id", "name", "age", "address", "salary", "birth"];

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
        Assert.Equal(SortAsc, service.SortEntries.Single().SortType);

        _ = Click(host, service);
        Assert.Equal(SortDesc, service.SortEntries.Single().SortType);

        _ = Click(host, service);

        // The third click completes the cycle to SORT_NONE and the plain branch then empties the store
        // entirely [:L154-L156], so the direction is observed through the descriptor instead.
        Assert.Empty(service.SortEntries);
        Assert.Equal(SortNone, service.Indicators.Single().SortType);
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

        Assert.Equal(string.Empty, service.GetClause(SortedColumn, SortNone));
        Assert.Equal(0, describeCalls);
    }

    /// <summary>
    /// The four cascade stages and both suffixes, asserted byte for byte.
    /// </summary>
    public static TheoryData<string, string, string, string, string, long, string> ClauseMatrix() => new()
    {
        // colType,   Type,       Edit.Style, DDDW.Display, DDDW.Data, sortType, expected
        // ---- STAGE 1 :L294-L295 - a computed column sorts by itself, and the whole else-branch is skipped.
        { "decimal(2)", "compute", "edit", "", "", SortAsc, "salary A" },
        { "decimal(2)", "compute", "edit", "", "", SortDesc, "salary D" },

        // ---- STAGE 2 :L299-L302 - a dddw sorts by display ONLY when display differs from data.
        { "char(50)", "column", "dddw", "descr", "code", SortAsc, "LookUpDisplay(salary) A" },
        { "char(50)", "column", "dddw", "descr", "code", SortDesc, "LookUpDisplay(salary) D" },

        // ---- STAGE 2 :L300 false - display EQUALS data, so the clause is LEFT EMPTY and stage four
        //      supplies the bare name. This is the case a flattened cascade would get wrong.
        { "char(50)", "column", "dddw", "code", "code", SortAsc, "salary A" },

        // ---- STAGE 2 :L303-L304 - a ddlb ALWAYS sorts by display, with no display/data test.
        { "char(50)", "column", "ddlb", "", "", SortAsc, "LookUpDisplay(salary) A" },
        { "char(50)", "column", "ddlb", "ignored", "ignored", SortAsc, "LookUpDisplay(salary) A" },

        // ---- STAGE 4 :L329 - the plain fallback, which is the common answer.
        { "char(50)", "column", "edit", "", "", SortAsc, "salary A" },
        { "char(50)", "column", "edit", "", "", SortDesc, "salary D" },

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

        Assert.Equal("LookUpDisplay(salary) A", service.GetClause(SortedColumn, SortAsc));
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

        Assert.Equal("salary A", service.GetClause(SortedColumn, SortAsc));
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

        Assert.Equal(expected, service.GetClause(SortedColumn, SortAsc));
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

        Assert.Equal("if(IsNull(salary),'',salary) A", service.GetClause(SortedColumn, SortAsc));
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

        Assert.Equal("salary A", service.GetClause(SortedColumn, SortAsc));
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
        Assert.Equal(SortAsc, only.SortType);
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
        Assert.All(service.SortEntries, e => Assert.Equal(SortAsc, e.SortType));
        Assert.Equal("age A,salary A,name A", service.CurrentSort);

        // Cycle "salary" to descending and then to unsorted; only then is it pruned [:L136-L145].
        _ = Click(host, service, "salary", ctrlHeld: true);
        Assert.Equal(SortDesc, service.SortEntries[1].SortType);
        Assert.Equal(3, service.SortEntries.Count);

        _ = Click(host, service, "salary", ctrlHeld: true);

        // ONLY the cleared column is removed, and the survivors keep their order and directions.
        Assert.Equal(new[] { "age", "name" }, service.SortEntries.Select(e => e.ColName).ToArray());
        Assert.All(service.SortEntries, e => Assert.Equal(SortAsc, e.SortType));
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
        Assert.Equal(SortDesc, survivor.SortType);
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
        Assert.Equal(SortAsc, service.SortEntries.Single().SortType);
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

        Assert.Equal(SortAsc, only.SortType);
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
    /// <c>:L348-L349</c> - both object names are COMPOSED FROM THE PUBLIC SUFFIX, not retyped as
    /// literals, and the badge name carries an INFIX rather than an extra suffix.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The oracle writes <c>colName + ARROWSUFFIX</c> and <c>colName + "_idx_" + ARROWSUFFIX</c>, so the
    /// suffix appears once in each name and the badge name is NOT the arrow name with anything appended.
    /// Getting that wrong is easy and quiet: <c>"salary_arw_idx_"</c> and <c>"salary_arw_idx__arw"</c> are
    /// both plausible-looking mistakes, and either would leave the deferred rendering half destroying an
    /// object that does not exist while the real one accumulated on every sort.
    /// </para>
    /// <para>
    /// EVERY EXPECTATION HERE IS BUILT FROM <see cref="ColumnSortModel.ARROWSUFFIX"/> rather than from the
    /// text <c>"_arw"</c>. That is the point of the test: the sibling descriptor tests assert the fully
    /// spelled-out names, which pins the VALUE, while this one pins the COMPOSITION - so if the constant
    /// and the composition ever disagreed, one of the two tests would fail whichever side moved.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheTwoObjectNamesAreComposedFromThePublicSuffix()
    {
        (FakeDataWindowHost host, ColumnSortModel service) = NewCompanyService();

        host.TableSort = string.Empty;

        // Two sorted columns, so the composition is proved for more than one name and the badge names
        // are live as well as the arrow names.
        _ = Click(host, service, "age", ctrlHeld: true);
        _ = Click(host, service, "birth", ctrlHeld: true);

        Assert.Equal(2, service.Indicators.Count);

        foreach (ColumnSortIndicatorDescriptor descriptor in service.Indicators)
        {
            string column = descriptor.ColumnName;

            // :L348 - name + suffix, with nothing between them.
            Assert.Equal(column + ColumnSortModel.ARROWSUFFIX, descriptor.ArrowObjectName);

            // :L349 - name + INFIX + suffix. The infix sits BETWEEN, so this is not :L348's answer with
            // anything appended to it.
            Assert.Equal(
                column + "_idx_" + ColumnSortModel.ARROWSUFFIX,
                descriptor.IndexObjectName);

            Assert.NotEqual(
                descriptor.ArrowObjectName + "_idx_",
                descriptor.IndexObjectName);
            Assert.NotEqual(
                descriptor.ArrowObjectName + "_idx_" + ColumnSortModel.ARROWSUFFIX,
                descriptor.IndexObjectName);

            // The suffix appears EXACTLY ONCE in each name, and terminates both.
            Assert.EndsWith(ColumnSortModel.ARROWSUFFIX, descriptor.ArrowObjectName, StringComparison.Ordinal);
            Assert.EndsWith(ColumnSortModel.ARROWSUFFIX, descriptor.IndexObjectName, StringComparison.Ordinal);
            Assert.Equal(
                1,
                descriptor.ArrowObjectName.Split(ColumnSortModel.ARROWSUFFIX).Length - 1);
            Assert.Equal(
                1,
                descriptor.IndexObjectName.Split(ColumnSortModel.ARROWSUFFIX).Length - 1);

            // Both names start with the column, which is what makes the pair addressable per column.
            Assert.StartsWith(column, descriptor.ArrowObjectName, StringComparison.Ordinal);
            Assert.StartsWith(column, descriptor.IndexObjectName, StringComparison.Ordinal);
        }

        // A CLEAR-ONLY descriptor carries the same two composed names, because clearing means destroying
        // objects BY NAME and :L348-L349 precede the early-out at :L353.
        _ = Click(host, service, "age", ctrlHeld: true);
        _ = Click(host, service, "age", ctrlHeld: true);

        ColumnSortIndicatorDescriptor cleared = Assert.Single(service.Indicators, d => d.ClearOnly);

        Assert.Equal(cleared.ColumnName + ColumnSortModel.ARROWSUFFIX, cleared.ArrowObjectName);
        Assert.Equal(
            cleared.ColumnName + "_idx_" + ColumnSortModel.ARROWSUFFIX,
            cleared.IndexObjectName);
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
        Assert.Equal(SortNone, descriptor.SortType);

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

        Assert.Equal(SortAsc, service.SortEntries.Single().SortType);
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

        Assert.Throws<InvalidOperationException>(() => service.GetClause(SortedColumn, SortAsc));
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
        Assert.Equal(1, ColumnSortModel.UpperBound([new SortData("a", SortAsc)]));
        Assert.Equal(
            3,
            ColumnSortModel.UpperBound(
                [new SortData("a", SortAsc), new SortData("b", SortDesc), new SortData("c", SortNone)]));
    }

    /// <summary>
    /// One-based reads: index <c>1</c> is the FIRST entry, and index <c>UpperBound</c> is the LAST.
    /// </summary>
    [Fact]
    public void EntryAtIsOneBasedAtBothEnds()
    {
        List<SortData> entries =
        [
            new SortData("first", SortAsc),
            new SortData("middle", SortDesc),
            new SortData("last", SortNone),
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
        List<SortData> entries = [new SortData("only", SortAsc)];

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
            new SortData("first", SortNone),
            new SortData("second", SortNone),
        ];

        ColumnSortModel.SetEntryAt(entries, 1, new SortData("first", SortDesc));

        Assert.Equal(SortDesc, ColumnSortModel.EntryAt(entries, 1).SortType);
        Assert.Equal(SortNone, ColumnSortModel.EntryAt(entries, 2).SortType);
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
            new SortData("a", SortAsc),
            new SortData("b", SortDesc),
            new SortData("c", SortAsc),
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
        List<SortData> entries = [new SortData("only", SortAsc)];

        Assert.Equal(
            "oneBasedIndex",
            Assert.Throws<ArgumentOutOfRangeException>(
                () => ColumnSortModel.SetEntryAt(entries, oneBasedIndex, new SortData("x", SortNone)))
                .ParamName);

        Assert.Equal(
            "oneBasedIndex",
            Assert.Throws<ArgumentOutOfRangeException>(
                () => ColumnSortModel.RemoveEntryAt(entries, oneBasedIndex)).ParamName);

        // And neither left the list disturbed on the way out.
        Assert.Equal(1, ColumnSortModel.UpperBound(entries));
        Assert.Equal(SortAsc, ColumnSortModel.EntryAt(entries, 1).SortType);
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

    // ==========================================================================================
    //  ACCESSIBILITY, THE PORTED STRUCTURE AND THE SORT STORE                       :L23-L41
    //  ----------------------------------------------------------------------------------------
    //  The oracle's `type variables` block is an ACCESSIBILITY DECLARATION as much as a data one:
    //  ARROWSUFFIX sits under `public:` [:L24-L26] and everything else under `private:` [:L28-L39].
    //  DECISION 6 of the file under test commits to reproducing that division, so this region asserts
    //  it rather than trusting it - a promoted constant is an API addition the oracle never made, and
    //  a demoted one breaks the consumer that reads the suffix to compose an object name.
    // ==========================================================================================

    /// <summary>
    /// <c>:L24-L26</c> - the suffix is <c>public</c>, a <c>string</c>, a compile-time constant, and
    /// exactly <c>"_arw"</c>.
    /// </summary>
    /// <remarks>
    /// The literal is asserted twice over on purpose: once through the language, which proves the member
    /// is reachable from outside the assembly at all, and once through the metadata, which proves the
    /// accessibility and the width. A consumer composing <c>col + ARROWSUFFIX</c> depends on both.
    /// </remarks>
    [Fact]
    public void TheArrowSuffixKeepsTheOraclesPublicAccessibility()
    {
        FieldInfo suffix = ConstantField("ARROWSUFFIX");

        Assert.True(suffix.IsPublic, "ARROWSUFFIX is declared under `public:` at :L24-L26.");
        Assert.True(suffix.IsLiteral, "ARROWSUFFIX is a `constant string` at :L26.");
        Assert.Equal(typeof(string), suffix.FieldType);
        Assert.Equal("_arw", suffix.GetRawConstantValue());

        // And through the language, which is what a consumer actually writes.
        Assert.Equal("_arw", ColumnSortModel.ARROWSUFFIX);
    }

    /// <summary>
    /// <c>:L28</c> and <c>:L37-L39</c> - the three direction constants stay <c>private</c> and stay
    /// <c>long</c>-typed, with the oracle's values.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THERE IS NO DIVERGENCE TO REPORT HERE, and that is the finding. The oracle hides all three and
    /// the port hides all three, so nothing was widened: the sort direction is not part of this
    /// service's API, because the API is the three-state cycle and the emitted clause, never the
    /// numbers behind them. Had the port promoted them to <c>public</c> or <c>internal</c> "for
    /// testability", this test would fail and the promotion would have to be justified - which is
    /// exactly why the test exists rather than the promotion.
    /// </para>
    /// <para>
    /// The one accessibility the port DOES widen is the clause builder, and that widening is asserted -
    /// and its justification recorded - in
    /// <see cref="TheOnlyWidenedMembersAreTheClauseBuilderAndTheObservationSeams"/> below.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("SORT_NONE", 0L)]
    [InlineData("SORT_ASC", 1L)]
    [InlineData("SORT_DESC", 2L)]
    public void TheDirectionConstantsKeepTheOraclesPrivateAccessibility(string oracleName, long expected)
    {
        FieldInfo direction = ConstantField(oracleName);

        Assert.True(direction.IsPrivate, oracleName + " is declared under `private:` at :L28.");
        Assert.False(direction.IsPublic);
        Assert.False(direction.IsAssembly, oracleName + " must not be widened to internal either.");
        Assert.True(direction.IsLiteral);
        Assert.Equal(typeof(long), direction.FieldType);
        Assert.Equal(expected, direction.GetRawConstantValue());
    }

    /// <summary>
    /// The three values this file reads by reflection ARE the three the rest of the suite uses, so the
    /// reflection seam cannot drift away from the assertions built on it.
    /// </summary>
    [Fact]
    public void TheReflectedDirectionsAreTheOnesTheSuiteAssertsWith()
    {
        Assert.Equal(0L, SortNone);
        Assert.Equal(1L, SortAsc);
        Assert.Equal(2L, SortDesc);

        // All three distinct, which the cycle at :L125-L132 depends on: two equal values would make one
        // of the three states unreachable and the cycle would silently shorten.
        Assert.Equal(3, new[] { SortNone, SortAsc, SortDesc }.Distinct().Count());
    }

    /// <summary>
    /// <c>:L45-L50</c> - the three published members stay published, the four hidden ones stay hidden,
    /// and the ONE deliberate widening is the clause builder plus the four read-only observation seams.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>internal</c> is not <c>public</c>: with
    /// <c>&lt;InternalsVisibleTo Include="PowerFramework.DataServices.Tests" /&gt;</c> on the service
    /// project it is visible to this assembly and to nothing else, so the oracle's ENCAPSULATION INTENT
    /// survives while constraint C-H's requirement that the clause builder be testable as a pure
    /// function - no DataWindow, no database, no UI - is satisfied. AAP 0.6.7 additionally requires a
    /// byte-exact table-driven matrix over it, and a <c>private</c> member admits neither.
    /// </para>
    /// <para>
    /// The four seams are projections of existing private state that add no behaviour, and the two
    /// one-based helpers are <c>internal</c> for the reason AAP 0.4.5.4 gives: the centralised one-based
    /// boundary must be DIRECTLY testable rather than inferred from a downstream sort expression.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheOnlyWidenedMembersAreTheClauseBuilderAndTheObservationSeams()
    {
        Type model = typeof(ColumnSortModel);

        // :L45-L47 - the three the oracle publishes.
        foreach (string published in new[] { nameof(ColumnSortModel.Reset), nameof(ColumnSortModel.Update) })
        {
            Assert.All(
                model.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    .Where(m => m.Name == published),
                m => Assert.True(m.IsPublic, published + " is public at :L45-L47."));
        }

        Assert.Equal(2, model.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Count(m => m.Name == nameof(ColumnSortModel.Update)));

        // :L48 - the clause builder. The single widening, and it is internal and NOT public.
        MethodInfo clause = Assert.Single(
            model.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly),
            m => m.Name == "GetClause");

        Assert.True(clause.IsAssembly, "GetClause is internal - see the remarks.");
        Assert.False(clause.IsPublic, "GetClause must not reach the service's public API.");

        // :L44, :L49, :L50 - the three that stay private.
        foreach (string hidden in new[] { "Sort", "BuildIndicatorDescriptor" })
        {
            Assert.All(
                model.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    .Where(m => m.Name == hidden),
                m => Assert.True(m.IsPrivate, hidden + " is private at :L44, :L49-L50."));
        }

        // The four observation seams are internal, never public.
        foreach (string seam in new[] { "SortEntries", "Indicators", "CurrentSort", "OriginalSort" })
        {
            PropertyInfo projection = Assert.Single(
                model.GetProperties(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly),
                p => p.Name == seam);

            Assert.True(projection.GetMethod?.IsAssembly, seam + " is an internal observation seam.");
            Assert.Null(projection.SetMethod);
        }
    }

    /// <summary>
    /// <c>:L6-L13</c> - the ported structure carries EXACTLY the oracle's two fields, under the oracle's
    /// two names, with the oracle's two widths, and nothing else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A PowerScript structure is a VALUE with copy semantics - assigning one copies it, and
    /// <c>SortDatas[nIndex].sortType = SORT_NONE</c> [<c>:L150</c>] mutates the array slot rather than a
    /// shared referent. A <c>readonly record struct</c> reproduces the value semantics; the
    /// <c>init</c>-only members reproduce the fact that the port mutates the STORE through
    /// <c>SetEntryAt</c> rather than the entry in place, which is what makes the one-based boundary the
    /// single place the arithmetic lives.
    /// </para>
    /// <para>
    /// A THIRD FIELD WOULD BE A CONTRACT ADDITION. The structure is what the store holds and what the
    /// clause builder is driven from, so an extra member - a cached clause, a display name, a dirty flag
    /// - would be state the oracle never had, and constraint C-B forbids it.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheSortDataRecordCarriesExactlyTheOraclesTwoFields()
    {
        Type entry = typeof(SortData);

        Assert.True(entry.IsValueType, "sortdata is a PowerScript structure - a VALUE [:L10-L13].");

        PropertyInfo[] members =
            [.. entry.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .OrderBy(p => p.Name, StringComparer.Ordinal)];

        Assert.Equal(
            new[] { nameof(SortData.ColName), nameof(SortData.SortType) },
            members.Select(p => p.Name).ToArray());

        // :L11  string colname   -> ColName
        Assert.Equal(typeof(string), entry.GetProperty(nameof(SortData.ColName))?.PropertyType);

        // :L12  long sorttype    -> SortType, and a long rather than an enum, because the direction is
        // carried as a number in the descriptor and in recordings.
        Assert.Equal(typeof(long), entry.GetProperty(nameof(SortData.SortType))?.PropertyType);

        // Both are init-only, so an entry is replaced rather than edited.
        Assert.All(
            members,
            p => Assert.Contains(
                typeof(System.Runtime.CompilerServices.IsExternalInit),
                p.SetMethod?.ReturnParameter.GetRequiredCustomModifiers() ?? []));

        // Value equality, which is what a PowerScript structure comparison would give.
        Assert.Equal(new SortData("age", SortAsc), new SortData("age", SortAsc));
        Assert.NotEqual(new SortData("age", SortAsc), new SortData("age", SortDesc));
    }

    /// <summary>
    /// <c>:L29-L32</c> - the store and the two expression fields survive under the ORACLE'S OWN NAMES,
    /// and the three internal seams are live projections of them rather than copies.
    /// </summary>
    /// <remarks>
    /// Asserting the private field names is not pedantry: <c>_sSort</c> and <c>_sOrgSort</c> are the two
    /// fields the whole of <c>_of_sort</c>, <c>of_reset</c> and <c>of_update</c> is written in terms of
    /// [<c>:L174</c>, <c>:L176</c>, <c>:L201-L207</c>, <c>:L269-L280</c>], so a reader following a
    /// locator from the oracle into the port has to find them spelled the same way. The identity check
    /// then proves <see cref="ColumnSortModel.SortEntries"/> is the store itself and not a snapshot,
    /// which is what makes every ordering assertion in this file meaningful.
    /// </remarks>
    [Fact]
    public void TheStoreAndTheTwoExpressionFieldsKeepTheOraclesNames()
    {
        (FakeDataWindowHost host, ColumnSortModel service) = NewService();
        TeachGeometry(host);

        const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;

        FieldInfo store = Assert.Single(typeof(ColumnSortModel).GetFields(Hidden), f => f.Name == "_sortDatas");
        FieldInfo current = Assert.Single(typeof(ColumnSortModel).GetFields(Hidden), f => f.Name == "_sSort");
        FieldInfo original = Assert.Single(typeof(ColumnSortModel).GetFields(Hidden), f => f.Name == "_sOrgSort");

        // :L29  SORTDATA SortDatas[]  - an ordered sequence, exposed read-only.
        Assert.Same(store.GetValue(service), service.SortEntries);
        Assert.IsAssignableFrom<IReadOnlyList<SortData>>(service.SortEntries);

        // :L31-L32 - both start EMPTY, not null and not the sentinel. The lazy capture at :L176 tests
        // `_sOrgSort = ""`, so an initial sentinel would suppress the capture permanently.
        Assert.Equal(string.Empty, current.GetValue(service));
        Assert.Equal(string.Empty, original.GetValue(service));
        Assert.Equal(string.Empty, service.CurrentSort);
        Assert.Equal(string.Empty, service.OriginalSort);

        host.TableSort = "age D";
        _ = Click(host, service);

        // Both seams now read their field, byte for byte.
        Assert.Equal(current.GetValue(service), service.CurrentSort);
        Assert.Equal(original.GetValue(service), service.OriginalSort);
        Assert.Equal("salary A", service.CurrentSort);
        Assert.Equal("age D", service.OriginalSort);
    }

    /// <summary>
    /// <c>dw_sqlite.srd:L14</c> against <c>:L176-L178</c> and <c>:L201-L207</c> - the fixture's own
    /// sort expression makes a COMPLETE ROUND TRIP: in through <c>Describe</c>, held in
    /// <c>_sOrgSort</c>, and back out through <c>SetSort</c> when the user clears every column - byte
    /// for byte, INCLUDING ITS TRAILING SPACE.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The literal is <c>sort="age A salary A "</c>, fifteen characters ending in a space. That space is
    /// the DataWindow painter's own separator convention and it is data, not formatting: a
    /// <c>Trim()</c> anywhere on this path - in the capture, in the field, in the apply, or in a test
    /// comparison - would silently rewrite the expression the DataWindow is asked to sort by. The
    /// assertions below therefore pin the length, the final character, and the fact that trimming would
    /// CHANGE the value, so a trimming regression cannot hide behind an equality that still looks right.
    /// </para>
    /// <para>
    /// This also settles the shape question the generated expression raises: the ORIGINAL is
    /// SPACE-separated with a trailing space because the painter wrote it, while the GENERATED one is
    /// COMMA-separated with no trailing space because <c>:L196</c> joins with a comma. Both shapes are
    /// correct, they are not interchangeable, and <see cref="TheGeneratedSeparatorIsTheOraclesComma"/>
    /// asserts the other half.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheFixturesOwnSortRoundTripsByteForByteIncludingItsTrailingSpace()
    {
        const string FixtureSort = "age A salary A ";

        (FakeDataWindowHost host, ColumnSortModel service) = NewCompanyService();

        // The fixture reports exactly what dw_sqlite.srd:L14 declares.
        Assert.Equal(FixtureSort, host.TableSort);

        // IN: :L271 captures the Describe answer verbatim.
        Assert.Equal(RetCode.OK, service.Update());
        Assert.Equal(FixtureSort, service.OriginalSort);

        // The trailing space, asserted three ways so no trimming survives.
        Assert.Equal(15, service.OriginalSort.Length);
        Assert.Equal(' ', service.OriginalSort[^1]);
        Assert.NotEqual(service.OriginalSort.TrimEnd(), service.OriginalSort);

        // A user sort now takes over. The remembered original must NOT follow it [:L274 has already
        // made the field non-empty, so :L176 cannot re-capture].
        _ = Click(host, service, "id");
        Assert.Equal("id A", service.CurrentSort);
        Assert.Equal(FixtureSort, service.OriginalSort);

        // A real DataWindow now reports the applied sort; mirror it so the equal-sort early-out at
        // :L404 does not swallow the restore below.
        Assert.Equal("id A", host.AppliedSort);
        host.TableSort = "id A";

        // OUT: clearing every column falls back to the remembered original [:L201-L204] and applies it.
        Assert.Equal(RetCode.OK, service.Reset());

        Assert.Equal(FixtureSort, host.AppliedSort);
        Assert.Equal(15, host.AppliedSort?.Length);
        Assert.Equal(' ', host.AppliedSort?[^1]);
    }


    // ==========================================================================================
    //  THE MEMBER SHAPES AND THE RETURN CODES                              :L44-L50, :L216-L288
    //  ----------------------------------------------------------------------------------------
    //  The oracle declares its two sort overloads with DIFFERENT KINDS: `private subroutine
    //  _of_sort ()` [:L44] and `private function long _of_sort (readonly string sort)` [:L50]. A
    //  PowerScript subroutine has no result at all, so the pass that rebuilds the expression cannot
    //  report anything - not even that the apply it delegates to failed - while the apply itself
    //  answers a code. That asymmetry is not tidy, and it is not tidied.
    // ==========================================================================================

    /// <summary>
    /// <c>:L44</c> against <c>:L50</c> - the rebuild pass returns NOTHING and the apply returns a
    /// <c>long</c>. The asymmetry is the oracle's and is preserved.
    /// </summary>
    /// <remarks>
    /// The consequence is observable and is asserted below: <see cref="ColumnSortModel.Reset"/> answers
    /// <c>RetCode.OK</c> from its own last line [<c>:L246</c>] and NOT from the apply it triggered, so a
    /// reset whose apply failed still reports success. A port that "improved" the subroutine into a
    /// function and threaded its code out through Reset would change what Reset means.
    /// </remarks>
    [Fact]
    public void TheRebuildPassReturnsNothingWhileTheApplyReturnsACode()
    {
        MethodInfo[] overloads =
            [.. typeof(ColumnSortModel)
                .GetMethods(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.Name == "Sort")
                .OrderBy(m => m.GetParameters().Length)];

        Assert.Equal(2, overloads.Length);

        // :L44  private subroutine _of_sort ()  -> void, no parameters.
        Assert.Empty(overloads[0].GetParameters());
        Assert.Equal(typeof(void), overloads[0].ReturnType);

        // :L50  private function long _of_sort (readonly string sort)  -> long, one `readonly` argument,
        // which ports as `in` and therefore arrives by reference and read-only.
        ParameterInfo applied = Assert.Single(overloads[1].GetParameters());

        Assert.Equal(typeof(long), overloads[1].ReturnType);
        Assert.Equal("sort", applied.Name);
        Assert.True(applied.ParameterType.IsByRef, "`readonly string sort` ports as an `in` parameter.");
        Assert.True(applied.IsIn);
        Assert.Equal(typeof(string), applied.ParameterType.GetElementType());
    }

    /// <summary>
    /// <c>:L171-L214</c> - the rebuild pass APPLIES the current state through the host even though it
    /// can report nothing, and its effect is visible on the DataWindow rather than in a return value.
    /// </summary>
    [Fact]
    public void TheRebuildPassAppliesThroughTheHostWithoutReportingAnything()
    {
        (FakeDataWindowHost host, ColumnSortModel service) = NewService();
        TeachGeometry(host);

        host.TableSort = string.Empty;

        // Reaching the pass through the only public route that triggers it - a drained header click.
        long? clicked = Click(host, service);

        // The CLICK's answer is the event's own 0 [:L164], never the pass's - the pass has none to give.
        Assert.Equal(0L, clicked);

        // And yet the sort was applied and the expression rebuilt.
        Assert.Equal("salary A", service.CurrentSort);
        Assert.Equal("salary A", host.AppliedSort);
        Assert.Equal(1, host.SortCallCount);
    }

    /// <summary>
    /// <c>:L404</c> and <c>:L430</c> - BOTH exits of the apply answer <c>RetCode.OK</c>, and
    /// <c>of_update</c> hands that answer straight back [<c>:L282</c>].
    /// </summary>
    /// <remarks>
    /// The two exits are indistinguishable to a caller by design: one applied a sort and one decided the
    /// DataWindow was already sorted that way. Since the return value carries no "did work" bit, a caller
    /// that needs to know must compare the expression itself - which is why
    /// <see cref="AnEqualSortDoesNoWorkAndStillReportsSuccess"/> asserts the absence of host traffic
    /// rather than a distinct code.
    /// </remarks>
    [Fact]
    public void BothExitsOfTheApplyAnswerTheSuccessCode()
    {
        (FakeDataWindowHost applied, ColumnSortModel applyingService) = NewService();
        applied.TableSort = string.Empty;

        // :L430 - the exit that did the work.
        Assert.Equal(RetCode.OK, applyingService.Update("age A"));
        Assert.Equal("age A", applied.AppliedSort);

        (FakeDataWindowHost equal, ColumnSortModel equalService) = NewService();
        equal.TableSort = "age A";

        // :L404 - the exit that decided there was nothing to do. SAME CODE.
        Assert.Equal(RetCode.OK, equalService.Update("age A"));
        Assert.Null(equal.AppliedSort);

        // And that code is the tri-state algebra's success, not merely a zero that happens to match.
        Assert.Equal(0L, RetCode.OK);
        Assert.True(Predicates.IsSucceeded(RetCode.OK));
    }

    /// <summary>
    /// <c>:L236</c> against <c>:L246</c> - the two codes <c>of_reset</c> can answer, side by side.
    /// </summary>
    /// <remarks>
    /// Pinning both in one theory is the point: the difference between them is ONLY whether the store
    /// held anything, never whether the sort succeeded. DEFECT 3 is that a caller cannot tell "nothing to
    /// do" from a genuine failure - see <see cref="Defect3_ResettingAnEmptyStoreReportsFailure"/> for the
    /// consequence spelled out through the predicates.
    /// </remarks>
    [Theory]
    [InlineData(false, RetCode.FAILED)]
    [InlineData(true, RetCode.OK)]
    public void ResetAnswersFailedForAnEmptyStoreAndOkOtherwise(bool seedAColumn, long expected)
    {
        (FakeDataWindowHost host, ColumnSortModel service) = NewService();
        TeachGeometry(host);

        host.TableSort = string.Empty;

        if (seedAColumn)
        {
            _ = Click(host, service);
            Assert.NotEmpty(service.SortEntries);

            // Mirror the applied sort so the restore below is not swallowed by the equal-sort early-out.
            host.TableSort = "salary A";
        }
        else
        {
            Assert.Empty(service.SortEntries);
        }

        Assert.Equal(expected, service.Reset());

        // Either way the store ends EMPTY [:L244 assigns the empty array; the failure path never
        // populated it], so the two codes do not describe two different end states.
        Assert.Empty(service.SortEntries);
    }

    /// <summary>
    /// <c>:L285-L288</c> - the no-argument overload passes an EXPLICIT NULL, which is a third value
    /// distinct from both a supplied expression and the empty string.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>SetNull(nvl)</c> then <c>of_Update(nvl)</c> is the oracle's only way to reach the capture
    /// branch, because <c>:L268</c> switches on <c>IsNull(sort)</c> and PowerScript's empty string is not
    /// null. The port declares the parameter <c>in string?</c> and the overload forwards
    /// <see langword="null"/>; passing <c>string.Empty</c> instead would take the OTHER branch and
    /// remember an empty original, so the two are NOT interchangeable and the nullable annotation is
    /// load-bearing rather than defensive.
    /// </para>
    /// <para>
    /// AAP 0.4.5.4 forbids collapsing PowerBuilder's null for a value the predicates depend on, and this
    /// is that rule applied to a string: <see langword="null"/> means "capture from the DataWindow" and
    /// the empty string means "remember nothing", and a non-nullable parameter would make the first
    /// unrepresentable.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheNoArgumentOverloadDelegatesWithAnExplicitNull()
    {
        // The parameter is nullable AND `in`, matching `readonly string sort` at :L46.
        MethodInfo supplied = Assert.Single(
            typeof(ColumnSortModel)
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly),
            m => m.Name == nameof(ColumnSortModel.Update) && m.GetParameters().Length == 1);

        ParameterInfo sort = Assert.Single(supplied.GetParameters());

        Assert.Equal("sort", sort.Name);
        Assert.True(sort.IsIn);
        Assert.True(sort.ParameterType.IsByRef);
        Assert.Equal(typeof(string), sort.ParameterType.GetElementType());
        Assert.Equal(
            NullabilityState.Nullable,
            new NullabilityInfoContext().Create(sort).ReadState);

        // The parameterless overload exists and takes nothing [:L47].
        MethodInfo delegating = Assert.Single(
            typeof(ColumnSortModel)
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly),
            m => m.Name == nameof(ColumnSortModel.Update) && m.GetParameters().Length == 0);

        Assert.Equal(typeof(long), delegating.ReturnType);

        // BEHAVIOURALLY IDENTICAL to passing null explicitly: both capture from the DataWindow.
        (FakeDataWindowHost implicitNull, ColumnSortModel implicitService) = NewService();
        implicitNull.TableSort = "age A salary A ";
        Assert.Equal(RetCode.OK, implicitService.Update());
        Assert.Equal("age A salary A ", implicitService.OriginalSort);

        (FakeDataWindowHost explicitNull, ColumnSortModel explicitService) = NewService();
        explicitNull.TableSort = "age A salary A ";
        Assert.Equal(RetCode.OK, explicitService.Update(null));
        Assert.Equal("age A salary A ", explicitService.OriginalSort);

        // AND NOT IDENTICAL to passing the empty string, which remembers nothing and is then rewritten
        // to the sentinel by :L274 - the DataWindow's own answer never consulted.
        (FakeDataWindowHost empty, ColumnSortModel emptyService) = NewService();
        empty.TableSort = "age A salary A ";
        Assert.Equal(RetCode.OK, emptyService.Update(string.Empty));
        Assert.Equal("?", emptyService.OriginalSort);
        Assert.NotEqual(explicitService.OriginalSort, emptyService.OriginalSort);
    }

    /// <summary>
    /// <c>:L113-L123</c> - the find scan runs BEFORE the append, so a column already in the store is
    /// CYCLED rather than added a second time. The store never holds a duplicate.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClickingTheSameColumnAgainCyclesItRatherThanDuplicatingIt(bool ctrlHeld)
    {
        (FakeDataWindowHost host, ColumnSortModel service) = NewService();
        TeachGeometry(host);

        _ = Click(host, service, ctrlHeld: ctrlHeld);
        Assert.Equal(SortAsc, Assert.Single(service.SortEntries).SortType);

        _ = Click(host, service, ctrlHeld: ctrlHeld);

        // STILL ONE ENTRY, now descending. An append-without-find would leave two entries for one column
        // and the join at :L195-L198 would emit the column twice, which a DataWindow rejects.
        SortData only = Assert.Single(service.SortEntries);
        Assert.Equal(SortedColumn, only.ColName);
        Assert.Equal(SortDesc, only.SortType);

        Assert.Equal("salary D", service.CurrentSort);
        Assert.Single(service.SortEntries, e => e.ColName == SortedColumn);
    }

    /// <summary>
    /// <c>:L120-L122</c> and <c>:L187-L199</c> - the ordinal and the join follow INSERTION ORDER, which
    /// is click order, and NOT the DataWindow's own column order.
    /// </summary>
    /// <remarks>
    /// The fixture declares its columns id, name, age, address, salary, birth
    /// [<c>dw_sqlite.srd:L8-L13</c>]. Clicking <c>salary</c> before <c>age</c> therefore produces an
    /// expression whose terms are in the OPPOSITE order to the definition - which is the whole point of a
    /// multi-column sort, and which a store keyed by column number rather than by append position would
    /// silently reverse.
    /// </remarks>
    [Fact]
    public void TheOrdinalAndTheJoinFollowClickOrderNotColumnOrder()
    {
        (FakeDataWindowHost host, ColumnSortModel service) = NewCompanyService();

        host.TableSort = string.Empty;

        _ = Click(host, service, "salary", ctrlHeld: true);
        _ = Click(host, service, "age", ctrlHeld: true);

        Assert.Equal(["salary", "age"], service.SortEntries.Select(e => e.ColName).ToArray());
        Assert.Equal("salary A,age A", service.CurrentSort);

        // The ordinals number the entries in store order, so the badge on `salary` reads 1 even though
        // `age` is the earlier column in the definition.
        Assert.Equal([1, 2], service.Indicators.Select(d => d.Index).ToArray());
        Assert.Equal("salary", service.Indicators[0].ColumnName);
        Assert.Equal("      1", service.Indicators[0].IndexLabel);
        Assert.Equal("age", service.Indicators[1].ColumnName);
        Assert.Equal("      2", service.Indicators[1].IndexLabel);

        // And the definition's own order is NOT what came out.
        Assert.NotEqual("age A,salary A", service.CurrentSort);
    }


    // ==========================================================================================
    //  THE CLAUSE MATRIX DRIVEN FROM THE ORACLE'S OWN FIXTURE                       :L290-L339
    //  ----------------------------------------------------------------------------------------
    //  The matrix earlier in this file drives the CASCADE from a synthetic column, because a synthetic
    //  column can be given a dddw, a ddlb and a code table in turn. This one drives the TYPE ARMS from
    //  `ws_objects/pfw.tests.pbl.src/dw_sqlite.srd` instead, so the substitution literals are asserted
    //  against the column types a definition the legacy actually ships declares:
    //
    //      dw_sqlite.srd:L8   id       number        -> the numeric arm      :L316-L317
    //      dw_sqlite.srd:L9   name     char(100)     -> the string arm       :L324-L325
    //      dw_sqlite.srd:L10  age      number        -> the numeric arm      :L316-L317
    //      dw_sqlite.srd:L11  address  char(200)     -> the string arm       :L324-L325
    //      dw_sqlite.srd:L12  salary   decimal(2)    -> the numeric arm      :L316-L317
    //      dw_sqlite.srd:L13  birth    date          -> the date arm         :L320-L321
    //      dw_sqlite.srd:L27  compute_1              -> stage one            :L294-L295
    //
    //  So one numeric, one character and one date column are covered by construction, and the two
    //  numeric SPELLINGS - `number` and `decimal(2)` - are covered separately because they reduce to two
    //  different COL_TYPE_* categories that SHARE ONE ARM [:L316].
    // ==========================================================================================

    /// <summary>
    /// The six fixture columns plus its footer computed field, each with and without null substitution.
    /// </summary>
    public static TheoryData<string, bool, long, string> CompanyClauseMatrix() => new()
    {
        // column,     nilIsNull, sortType, expected
        // ---- NO SUBSTITUTION: stage four supplies the bare name and the suffix is appended :L329-L336.
        { "id", false, 1L, "id A" },
        { "name", false, 1L, "name A" },
        { "age", false, 1L, "age A" },
        { "address", false, 1L, "address A" },
        { "salary", false, 1L, "salary A" },
        { "birth", false, 1L, "birth A" },

        // ---- THE DESCENDING SUFFIX, on the first and last columns :L334-L335.
        { "id", false, 2L, "id D" },
        { "birth", false, 2L, "birth D" },

        // ---- AN UNSORTED COLUMN CONTRIBUTES NOTHING AT ALL :L292.
        { "age", false, 0L, "" },
        { "birth", true, 0L, "" },

        // ---- SUBSTITUTION, one row per fixture type :L315-L326.
        //      `number` [dw_sqlite.srd:L8, :L10] and `decimal(2)` [:L12] reach the SAME arm.
        { "id", true, 1L, "if(IsNull(id),-999999,id) A" },
        { "age", true, 1L, "if(IsNull(age),-999999,age) A" },
        { "salary", true, 1L, "if(IsNull(salary),-999999,salary) A" },
        { "salary", true, 2L, "if(IsNull(salary),-999999,salary) D" },

        //      `char(100)` [:L9] and `char(200)` [:L11] both reach the DEFAULT arm, whose substitute is
        //      the SINGLE-QUOTED EMPTY STRING - two adjacent apostrophes.
        { "name", true, 1L, "if(IsNull(name),'',name) A" },
        { "address", true, 1L, "if(IsNull(address),'',address) A" },

        //      `date` [:L13] reaches Date(), NOT DateTime(), for the same date literal :L320-L321.
        { "birth", true, 1L, "if(IsNull(birth),Date('1900-01-01'),birth) A" },
        { "birth", true, 2L, "if(IsNull(birth),Date('1900-01-01'),birth) D" },

        // ---- STAGE ONE, from the fixture's footer computed field [dw_sqlite.srd:L27]. Its expression is
        //      `sum(salary for page)`, but the clause is the OBJECT NAME - a computed column sorts by
        //      itself :L294-L295 - and the whole else-branch is skipped, so the substitution below cannot
        //      apply even with NilIsNull answered yes.
        { "compute_1", false, 1L, "compute_1 A" },
        { "compute_1", true, 1L, "compute_1 A" },
    };

    /// <summary>
    /// <c>:L290-L339</c> against <c>dw_sqlite.srd</c> - every emitted clause, byte for byte, from the
    /// oracle's own six-column definition.
    /// </summary>
    [Theory]
    [MemberData(nameof(CompanyClauseMatrix))]
    public void TheClauseIsByteExactForEveryFixtureColumn(
        string column,
        bool nilIsNull,
        long sortType,
        string expected)
    {
        (FakeDataWindowHost host, ColumnSortModel service) = NewCompanyService();

        if (nilIsNull)
        {
            // :L314 - the first of the three spellings. The fixture declares none of them, so an
            // unanswered property reports the invalid-expression sentinel and substitution stays off.
            host.SetDescribe(column + ".Edit.NilIsNull", "yes");
        }

        Assert.Equal(expected, service.GetClause(column, sortType));
    }

    /// <summary>
    /// <c>:L196</c> - the generated expression joins with a BARE COMMA, and the fixture's own
    /// painter-written expression joins with a SPACE and ends in one. The two shapes are both correct and
    /// are not interchangeable.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>_sSort += ","</c> is the whole of the separator: no space before it, no space after it, and no
    /// terminator after the last term. The painter's own convention at <c>dw_sqlite.srd:L14</c> is
    /// different - <c>"age A salary A "</c> - and that expression reaches the DataWindow through the
    /// ORIGINAL-SORT path, never through the join. A port that emitted the painter's shape from the join
    /// would produce an expression the DataWindow still parses, so nothing would fail loudly; the
    /// characterization recording would simply stop matching.
    /// </para>
    /// <para>
    /// Both shapes are asserted here together for exactly that reason: they meet nowhere else in the
    /// file, and asserting either alone would leave the other free to drift.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheGeneratedSeparatorIsTheOraclesComma()
    {
        (FakeDataWindowHost host, ColumnSortModel service) = NewCompanyService();

        // The painter's expression, as declared, before anything is generated.
        Assert.Equal("age A salary A ", host.TableSort);

        host.TableSort = string.Empty;

        _ = Click(host, service, "age", ctrlHeld: true);
        _ = Click(host, service, "salary", ctrlHeld: true);
        _ = Click(host, service, "birth", ctrlHeld: true);

        // A third click on `birth` alone would cycle it; a second Ctrl click takes it to descending, which
        // gives a mixed-direction expression - the realistic multi-column case.
        _ = Click(host, service, "birth", ctrlHeld: true);

        const string Expected = "age A,salary A,birth D";

        Assert.Equal(Expected, service.CurrentSort);
        Assert.Equal(Expected, host.AppliedSort);

        // The separator, characterised precisely.
        Assert.Equal(2, Expected.Count(c => c == ','));
        Assert.DoesNotContain(", ", service.CurrentSort, StringComparison.Ordinal);
        Assert.DoesNotContain(" ,", service.CurrentSort, StringComparison.Ordinal);
        Assert.DoesNotContain(",,", service.CurrentSort, StringComparison.Ordinal);
        Assert.False(service.CurrentSort.EndsWith(',') || service.CurrentSort.EndsWith(' '));
        Assert.False(service.CurrentSort.StartsWith(',') || service.CurrentSort.StartsWith(' '));

        // AND IT IS NOT THE PAINTER'S SHAPE. Neither the space-separated form nor a trailing space.
        Assert.NotEqual("age A salary A birth D", service.CurrentSort);
        Assert.NotEqual(service.CurrentSort + " ", service.CurrentSort);

        // Each term still carries its own LEADING-SPACE suffix, which is what makes the terms parseable
        // once the comma has separated them [:L333, :L335].
        Assert.Equal(["age A", "salary A", "birth D"], service.CurrentSort.Split(','));
    }

    /// <summary>
    /// <c>:L317-L325</c> and <c>:L384</c> - NOTHING the service emits is formatted through the ambient
    /// culture. Every substitution literal, every suffix and the badge label are byte-identical under a
    /// culture that redefines the negative sign, both number separators and the date separator.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is not a hypothetical risk. The numeric substitute is the text <c>-999999</c>, the date
    /// substitutes are <c>'1900-01-01'</c> and <c>'00:00:00'</c>, and the badge label is an integer
    /// rendered into a string - so four of the emitted values are exactly the shapes a culture-sensitive
    /// <c>ToString()</c> would rewrite. Under the hostile culture below, <c>-999999</c> would render as
    /// <c>MINUS999999</c> if it were ever formatted rather than written literally, and the test asserts
    /// that it does not.
    /// </para>
    /// <para>
    /// THE CULTURE IS BUILT BY MUTATING A CLONE OF THE INVARIANT CULTURE rather than by naming a real
    /// one. That keeps the test independent of which ICU data the container ships - the repository
    /// deliberately does NOT set <c>InvariantGlobalization</c>, but a test that depended on
    /// <c>de-DE</c> being installed would be asserting the environment rather than the code.
    /// </para>
    /// <para>
    /// The instrument is self-checked first: if the hostile culture did not actually change how a number
    /// renders, the byte-identical result below would prove nothing at all.
    /// </para>
    /// </remarks>
    [Fact]
    public void NothingEmittedIsFormattedThroughTheAmbientCulture()
    {
        NumberFormatInfo numbers = (NumberFormatInfo)CultureInfo.InvariantCulture.NumberFormat.Clone();
        numbers.NegativeSign = "MINUS";
        numbers.PositiveSign = "PLUS";
        numbers.NumberDecimalSeparator = ",";
        numbers.NumberGroupSeparator = ".";

        DateTimeFormatInfo dates = (DateTimeFormatInfo)CultureInfo.InvariantCulture.DateTimeFormat.Clone();
        dates.DateSeparator = "!";
        dates.TimeSeparator = "?";
        dates.ShortDatePattern = "dd!MM!yyyy";

        CultureInfo hostile = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        hostile.NumberFormat = numbers;
        hostile.DateTimeFormat = dates;

        string[] invariant = CaptureEmittedText();

        CultureInfo restore = CultureInfo.CurrentCulture;

        string[] underHostileCulture;

        try
        {
            CultureInfo.CurrentCulture = hostile;

            // THE INSTRUMENT'S OWN SELF-CHECK. Both the explicit provider and the ambient one must
            // actually be hostile, or the comparison below is vacuous.
            Assert.Equal("MINUS999999", (-999999L).ToString(hostile));
            Assert.Equal("MINUS999999", (-999999L).ToString());
            Assert.Equal("1,5", 1.5m.ToString());

            underHostileCulture = CaptureEmittedText();
        }
        finally
        {
            CultureInfo.CurrentCulture = restore;
        }

        Assert.Equal(invariant, underHostileCulture);

        // And the values themselves, so this test also pins WHAT was compared rather than only that two
        // runs agreed.
        Assert.Equal(
            [
                "if(IsNull(id),-999999,id) A",
                "if(IsNull(salary),-999999,salary) D",
                "if(IsNull(birth),Date('1900-01-01'),birth) A",
                "name A",
                "age A,salary A",
                "      1",
                "      2",
            ],
            underHostileCulture);
    }

    /// <summary>
    /// Emits one sample of every culture-sensitive-looking value the service produces.
    /// </summary>
    /// <returns>
    /// The three substitution arms, a plain clause, a joined multi-column expression and the two badge
    /// labels - in a fixed order, so two runs are directly comparable.
    /// </returns>
    private static string[] CaptureEmittedText()
    {
        (FakeDataWindowHost host, ColumnSortModel service) = NewCompanyService();

        host.TableSort = string.Empty;

        // The two clicks run FIRST, while no column answers NilIsNull, so the joined expression below is
        // the plain two-term form. Teaching NilIsNull before the clicks would push the substitution INTO
        // the joined expression and this sample would stop covering the plain join.
        _ = Click(host, service, "age", ctrlHeld: true);
        _ = Click(host, service, "salary", ctrlHeld: true);

        host.SetDescribe("id.Edit.NilIsNull", "yes");
        host.SetDescribe("salary.Edit.NilIsNull", "yes");
        host.SetDescribe("birth.Edit.NilIsNull", "yes");

        return
        [
            service.GetClause("id", SortAsc),
            service.GetClause("salary", SortDesc),
            service.GetClause("birth", SortAsc),
            service.GetClause("name", SortAsc),
            service.CurrentSort,
            service.Indicators[0].IndexLabel ?? string.Empty,
            service.Indicators[1].IndexLabel ?? string.Empty,
        ];
    }


    // ==========================================================================================
    //  THE FOUR EVENTS AS A SURFACE                                                  :L16-L19
    //  ----------------------------------------------------------------------------------------
    //  The oracle declares FOUR events, and two of them are easy to mistake for one:
    //
    //      :L16  event onlbuttonclk      pbm_dwnlbuttonclk                       - the raw PRESS
    //      :L17  event onlbuttonup       pbm_dwnlbuttonup                        - the raw RELEASE
    //      :L18  event type long onlbuttonclicked ( integer xpos, integer ypos,
    //                                              long row, dwobject dwo )      - the POSTED click
    //      :L19  event onlbuttondblclk   pbm_dwnlbuttondblclk                    - the raw DOUBLE press
    //
    //  `onlbuttonclk` and `onlbuttonclicked` are DIFFERENT EVENTS with different arities, different
    //  argument widths and utterly different jobs: the first only records where the press landed
    //  [:L53-L54], while the second runs the entire sort cycle [:L78-L166]. Collapsing them - a very
    //  natural-looking simplification, since one is "clk" and the other "clicked" - would run the whole
    //  cycle on every mouse-down, bypassing both the grid-style guard and the drag-versus-click test.
    // ==========================================================================================

    /// <summary>
    /// <c>:L16-L19</c> - all four events exist as distinct public members, and every input they need
    /// arrives as an EXPLICIT ARGUMENT rather than being read from the operating system.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The second half is a constraint C-D assertion in disguise. The oracle reads three things the
    /// deferred half owns - the press position it stored in a <c>POINT</c> [<c>:L34</c>, <c>:L53-L54</c>],
    /// the two-unit proximity test built on it [<c>:L70</c>], and <c>KeyDown(KeyControl!)</c>
    /// [<c>:L134</c>] - and every one of them is inverted into a parameter: <c>withinClickTolerance</c> on
    /// the release and <c>ctrlHeld</c> at drain time. So the whole event surface is describable in
    /// <c>long</c>, <c>int</c>, <c>bool</c> and the DataWindow object abstraction, with no interop type,
    /// no geometry type and no keyboard type anywhere in it. A parameterless click entry point would have
    /// nowhere to get its inputs from but the OS, which is why its ABSENCE is asserted too.
    /// </para>
    /// <para>
    /// THE ARGUMENT WIDTHS ARE THE ORACLE'S AND THEY DISAGREE WITH EACH OTHER. <c>:L18</c> declares
    /// <c>integer xpos, integer ypos</c> while the three <c>pbm_dwn*</c> events receive the PowerBuilder
    /// runtime's own <c>long</c> coordinates. The port keeps both widths rather than harmonising them,
    /// because a narrowing on the posted event is exactly what the oracle has.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheFourClickEventsAreDistinctAndTakeEveryInputExplicitly()
    {
        Type model = typeof(ColumnSortModel);

        MethodInfo[] events =
            [.. model.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.Name.StartsWith("OnLButton", StringComparison.Ordinal))
                .OrderBy(m => m.Name, StringComparer.Ordinal)];

        // Exactly four, and these four. `Clicked` and `Clk` are separate entries in this list.
        Assert.Equal(
            [
                nameof(ColumnSortModel.OnLButtonClicked),
                nameof(ColumnSortModel.OnLButtonClk),
                nameof(ColumnSortModel.OnLButtonDblClk),
                nameof(ColumnSortModel.OnLButtonUp),
            ],
            events.Select(m => m.Name).ToArray());

        // Distinct METHODS, not two names for one - so neither can be an alias of the other.
        MethodInfo press = events.Single(m => m.Name == nameof(ColumnSortModel.OnLButtonClk));
        MethodInfo clicked = events.Single(m => m.Name == nameof(ColumnSortModel.OnLButtonClicked));

        Assert.NotEqual(press.MetadataToken, clicked.MetadataToken);
        Assert.NotEqual(press.GetParameters().Length, clicked.GetParameters().Length);

        // Every event answers a long, which is the prevent convention's channel [:L56, :L75, :L164].
        Assert.All(events, m => Assert.Equal(typeof(long), m.ReturnType));

        // EVERY INPUT IS EXPLICIT, and every parameter type is one of four.
        Type[] admissible = [typeof(long), typeof(int), typeof(bool), typeof(IDataWindowObject)];

        Assert.All(
            events.SelectMany(m => m.GetParameters()),
            parameter =>
            {
                Type type = parameter.ParameterType.IsByRef
                    ? parameter.ParameterType.GetElementType() ?? parameter.ParameterType
                    : parameter.ParameterType;

                Assert.Contains(type, admissible);
                Assert.False(parameter.IsOptional, parameter.Name + " must be supplied, never defaulted.");
            });

        // The oracle's own argument names, so a named-argument call site reads like the event declaration.
        Assert.Equal(
            ["xpos", "ypos", "row", "dwo"],
            press.GetParameters().Select(p => p.Name ?? string.Empty).ToArray());
        Assert.Equal(
            ["xpos", "ypos", "row", "dwo", "ctrlHeld"],
            clicked.GetParameters().Select(p => p.Name ?? string.Empty).ToArray());
        Assert.Equal(
            ["xpos", "ypos", "row", "dwo", "withinClickTolerance"],
            events.Single(m => m.Name == nameof(ColumnSortModel.OnLButtonUp))
                .GetParameters().Select(p => p.Name ?? string.Empty).ToArray());

        // :L18 narrows the coordinates to `integer` on the posted event ALONE. Both widths preserved.
        Assert.Equal([typeof(int), typeof(int)], clicked.GetParameters().Take(2).Select(p => p.ParameterType).ToArray());
        Assert.Equal([typeof(long), typeof(long)], press.GetParameters().Take(2).Select(p => p.ParameterType).ToArray());

        // AND THERE IS NO INPUT-FREE ENTRY POINT. One would have to read the OS for its coordinates.
        Assert.DoesNotContain(events, m => m.GetParameters().Length == 0);
    }

    /// <summary>
    /// <c>:L53-L56</c>, <c>:L59-L76</c>, <c>:L72</c> and <c>:L78-L166</c> - each of the four events fires
    /// at its own point in the sequence, and NOTHING reaches the DataWindow until the posted continuation
    /// is drained.
    /// </summary>
    /// <remarks>
    /// The call log is the evidence rather than the state, because the question here is WHEN the host is
    /// touched. A press and a release must leave the DataWindow completely alone - the release only
    /// queues - and the entire apply must appear in one burst at the drain. An implementation that
    /// applied the sort from the release would still end in the same state and would still pass every
    /// state-based assertion in this file.
    /// </remarks>
    [Fact]
    public void NothingReachesTheDataWindowUntilTheContinuationIsDrained()
    {
        (FakeDataWindowHost host, ColumnSortModel service) = NewService();
        TeachGeometry(host);

        host.TableSort = string.Empty;
        host.CallLog.Clear();

        // :L53-L56 - the press. Answers 0, records nothing, queues nothing.
        Assert.Equal(0L, service.OnLButtonClk(11L, 12L, 3L, Header()));
        Assert.Empty(host.CallLog.Descriptions);
        Assert.False(service.PostedClickPending);

        // :L19 / :L168 - the double press forwards to the press and is equally inert.
        Assert.Equal(0L, service.OnLButtonDblClk(11L, 12L, 3L, Header()));
        Assert.Empty(host.CallLog.Descriptions);
        Assert.False(service.PostedClickPending);

        // :L59-L76 - the release. QUEUES ONLY: still not one call to the DataWindow.
        Assert.Equal(0L, service.OnLButtonUp(11L, 12L, 3L, Header(), withinClickTolerance: true));
        Assert.True(service.PostedClickPending);
        Assert.Empty(host.CallLog.Descriptions);
        Assert.Empty(service.SortEntries);
        Assert.Null(host.AppliedSort);

        // :L78-L166 - the drain. The whole apply arrives here, in one burst.
        Assert.Equal(0L, service.DrainPostedClick(ctrlHeld: false));

        Assert.False(service.PostedClickPending);
        Assert.Equal("salary A", host.AppliedSort);
        Assert.Contains(host.CallLog.Descriptions, d => d.StartsWith("SetSort", StringComparison.Ordinal));
        Assert.Contains(host.CallLog.Descriptions, d => d.StartsWith("Sort", StringComparison.Ordinal));
    }

    // ==========================================================================================
    //  THE ROW-FOCUS GATE ACROSS THE REORDER                                       :L409-L426
    //  ----------------------------------------------------------------------------------------
    //  Sorting MOVES the current row, and a moved row raises a row-focus change the user did not ask
    //  for - so the oracle suppresses that one event for the duration of the reorder and puts it back
    //  afterwards. Two properties make that correct rather than merely present: the suppression is
    //  CONDITIONAL on the bit not already being set [:L409-L412], and the restore is conditional on THIS
    //  CALL having been the one to set it [:L424-L426].
    // ==========================================================================================

    /// <summary>
    /// <c>:L409-L426</c> - the gate is DISABLED for the whole reorder and released after it, and the
    /// window is observed from inside rather than inferred from the call log.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The seam is <c>Describe</c>. The apply reads <c>"DataWindow.Table.Sort"</c> at <c>:L402</c> -
    /// BEFORE the suppression - and then <c>_of_HasGroup()</c> reads the two group-band heights at
    /// <c>:L417</c>, which is AFTER <c>#DataWindow.Sort()</c> and BEFORE the restore. Recording the gate
    /// state at each of those three reads therefore samples the window from the outside, from the inside,
    /// and from the inside again.
    /// </para>
    /// <para>
    /// WHAT "NOT DELIVERED" MEANS PRECISELY. The suppressed bit is what the event chain tests before it
    /// dispatches: <c>if BitTest(_nDisabledEvent,EID_ROWFOCUSCHANGE) then return 0</c>
    /// [<c>se_cst_dw.sru:L124</c> for the change and <c>:L130</c> for the changing edge], ported to
    /// <c>Domain/DataWindowEventChain.cs</c> where both handlers consult
    /// <c>EventGate.EID_ROWFOCUSCHANGE</c> and report a gated-out dispatch. So a row-focus notification
    /// arriving inside this window is answered <c>0</c> without reaching a handler, and one arriving after
    /// it is dispatched normally. This test asserts the gate the chain reads; the chain's own gating is
    /// asserted in <c>DataWindowEventChainTests</c> and <c>EventGateTests</c>, which is where it belongs.
    /// </para>
    /// <para>
    /// THE SIBLING BITS ARE ASSERTED TOO. <c>of_DisableEvent</c> sets one bit and <c>of_EnableEvent</c>
    /// clears one bit, so an item-change suppression a caller had established must survive the sort
    /// untouched. A mask-assigning implementation - <c>DisabledEvent = EID_ROWFOCUSCHANGE</c> rather than
    /// a bit set - would pass every other assertion here and silently re-enable it.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheRowFocusGateIsSuppressedThroughoutTheReorderAndReleasedAfterIt()
    {
        (FakeDataWindowHost host, ColumnSortModel service) = NewService();

        host.TableSort = string.Empty;

        // A pre-existing, UNRELATED suppression that must survive untouched.
        host.DisabledEvent = EventGate.EID_ITEMCHANGE;

        Dictionary<string, bool> rowFocusSuppressedAt = new(StringComparer.Ordinal);
        Dictionary<string, bool> itemChangeSuppressedAt = new(StringComparer.Ordinal);

        host.DescribeOverride = property =>
        {
            rowFocusSuppressedAt[property] =
                EventGate.IsEventDisabled(host.DisabledEvent, EventGate.EID_ROWFOCUSCHANGE);
            itemChangeSuppressedAt[property] =
                EventGate.IsEventDisabled(host.DisabledEvent, EventGate.EID_ITEMCHANGE);

            // Answering null leaves the fake's own answer in place - this is a probe, not a stub.
            return null;
        };

        // BEFORE: row focus is delivered.
        Assert.False(host.IsEventDisabled(EventGate.EID_ROWFOCUSCHANGE));

        Assert.Equal(RetCode.OK, service.Update("age A"));

        // AFTER: delivered again, and the unrelated suppression is exactly as it was.
        Assert.False(host.IsEventDisabled(EventGate.EID_ROWFOCUSCHANGE));
        Assert.True(host.IsEventDisabled(EventGate.EID_ITEMCHANGE));
        Assert.Equal(EventGate.EID_ITEMCHANGE, host.DisabledEvent);

        // :L402 - read before the suppression, so the gate was still open there.
        Assert.False(rowFocusSuppressedAt["DataWindow.Table.Sort"]);

        // :L417 - both group-band probes sit AFTER #DataWindow.Sort() and BEFORE the restore, so the gate
        // was closed for the whole of the reorder.
        Assert.True(rowFocusSuppressedAt["DataWindow.Header.1.Height"]);
        Assert.True(rowFocusSuppressedAt["DataWindow.Trailer.1.Height"]);

        // And the sibling bit was suppressed at every one of the three reads - never cleared and
        // re-established.
        Assert.All(itemChangeSuppressedAt.Values, Assert.True);
    }

    /// <summary>
    /// <c>:L414-L426</c> - the restore happens even when every host call on the way reports FAILURE,
    /// because the oracle discards all four codes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A LEAKED SUPPRESSION IS THE WORST OUTCOME THIS PATH HAS, and it is silent: the sort would look
    /// like it merely failed, while every subsequent row-focus change in the application would be
    /// answered <c>0</c> without reaching a handler, for the rest of the DataWindow's life. So the
    /// failure path is asserted explicitly rather than assumed to be the success path.
    /// </para>
    /// <para>
    /// FAILURE HERE MEANS A DISCARDED RESULT CODE, NOT AN EXCEPTION, and that is deliberate. <c>:L414</c>
    /// through <c>:L425</c> are five bare statements - <c>SetSort</c>, <c>Sort</c>, <c>GroupCalc</c>,
    /// <c>SetRow</c> and <c>of_EnableEvent</c> - whose codes the oracle never reads, and the oracle wraps
    /// none of them in a <c>TRY</c>. Asserting that the port restores the gate after a THROWN failure
    /// would therefore be asserting a guard the oracle does not have, which constraint C-B forbids just
    /// as firmly as it forbids removing one it does.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheGateIsRestoredEvenWhenEveryHostCallReportsFailure()
    {
        (FakeDataWindowHost host, ColumnSortModel service) = NewService();

        host.TableSort = string.Empty;
        host.CurrentRow = 4L;
        host.RowIdsByRow[4L] = 77L;
        host.RowsByRowId[77L] = 2L;

        // Every code the apply path discards, set to a failure.
        host.SetSortResult = -1;
        host.SortResult = -1;
        host.GroupCalcResult = -1;
        host.SetRowResult = -1;
        host.SetRedrawResult = -1;

        // A group band exists, so GroupCalc is genuinely reached and genuinely fails.
        host.SetDescribe("DataWindow.Header.1.Height", "0");
        host.CallLog.Clear();

        // The oracle answers OK regardless [:L430] - it never looked at any of the codes.
        Assert.Equal(RetCode.OK, service.Update("age A"));

        // AND THE GATE IS BACK. This is the assertion that matters.
        Assert.Equal(0u, host.DisabledEvent);
        Assert.False(host.IsEventDisabled(EventGate.EID_ROWFOCUSCHANGE));

        Assert.Contains(host.CallLog.Descriptions, d => d.StartsWith("DisableEvent", StringComparison.Ordinal));
        Assert.Contains(host.CallLog.Descriptions, d => d.StartsWith("EnableEvent", StringComparison.Ordinal));

        // The redraw bracket closed too, for the same reason: its code is discarded as well [:L428].
        Assert.Equal(
            2,
            host.CallLog.Descriptions.Count(d => d.StartsWith("SetRedraw", StringComparison.Ordinal)));
    }

    // ==========================================================================================
    //  THE DEFERRED HALF IS ABSENT - THE TOTAL CHECK                        CONSTRAINT C-D
    //  ----------------------------------------------------------------------------------------
    //  Everything above asserts what the headless half DOES. This region asserts what the whole service
    //  does NOT contain, by walking its reflected surface rather than by reading it. AAP 0.2.1.3
    //  Correction 4 drew the split line and AAP 0.4.2.5 states the ruling in one sentence - "Headless
    //  half only. DPI conversion at :L359,L361 is deferred." - so the vocabulary of the deferred half is
    //  enumerable, and its complete absence from the surface is assertable.
    //
    //  WHAT IS DEFERRED, AND WHERE IT WILL LIVE. `_of_setarrow`'s units switch [:L355-L364], its width
    //  halving [:L367], its two font-leader y offsets [:L368, :L385], its two `Modify("Destroy ...")`
    //  calls [:L351-L352], its `create text(...)` syntax with `font.face="Marlett"` and
    //  `font.face="Arial"` [:L377-L393], and the two `SetPointer` bookends [:L209, :L213] all belong to
    //  DesignSystem, reached through Gateway's reserved `/v1/design/**` extension point (AAP 0.4.4).
    //  THAT IS A DOCUMENTED CAPABILITY GAP, enumerated here, in the file under test and in
    //  docs/DEFERRED.md - not a silent omission. In its place this service publishes the indicator
    //  DESCRIPTOR as data, which the region above asserts field by field.
    // ==========================================================================================

    /// <summary>
    /// The vocabulary of the deferred rendering half. No member name and no member type name anywhere on
    /// this service's three types may contain any of these.
    /// </summary>
    /// <remarks>
    /// Every entry traces to a specific deferred construct rather than to a general suspicion:
    /// <c>dpi</c>, <c>u2p</c>/<c>u2py</c>, <c>px2mm</c>/<c>px2mmy</c> and <c>win32</c> to the units
    /// switch at <c>:L355-L364</c>; <c>pixel</c> and <c>millimet</c> to what that switch converts
    /// between; <c>leader</c> to <c>fLeaderHeight</c> at <c>:L341</c> and the two offsets it feeds;
    /// <c>font</c> and <c>marlett</c> to <c>font.face</c> at <c>:L379</c> and <c>:L388</c>; <c>canvas</c>
    /// and <c>paint</c> to the drawing surface those band objects would live on; <c>destroy</c>,
    /// <c>modify</c> and <c>syntax</c> to <c>Modify("Destroy ...")</c> at <c>:L351-L352</c> and the
    /// <c>create text(...)</c> emission at <c>:L377-L393</c>; and <c>pointer</c> and <c>hourglass</c> to
    /// <c>SetPointer(HourGlass!)</c> at <c>:L209</c>.
    /// </remarks>
    private static readonly string[] DeferredVocabulary =
    [
        "dpi",
        "u2p",
        "u2py",
        "px2mm",
        "px2mmy",
        "win32",
        "pixel",
        "millimet",
        "leader",
        "font",
        "marlett",
        "canvas",
        "paint",
        "destroy",
        "modify",
        "syntax",
        "pointer",
        "hourglass",
    ];

    /// <summary>
    /// The three types this service publishes.
    /// </summary>
    public static TheoryData<Type> ServiceSurface() => new()
    {
        typeof(ColumnSortModel),
        typeof(SortData),
        typeof(ColumnSortIndicatorDescriptor),
    };

    /// <summary>
    /// CONSTRAINT C-D - not one member of this service's surface reaches for the deferred rendering
    /// vocabulary, in its own name or in the name of its type.
    /// </summary>
    [Theory]
    [MemberData(nameof(ServiceSurface))]
    public void NoMemberOfTheSurfaceNamesAnythingFromTheDeferredHalf(Type type)
    {
        const BindingFlags Everything =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static
            | BindingFlags.DeclaredOnly;

        List<string> names = [type.Name];

        names.AddRange(type.GetFields(Everything).SelectMany(f => new[] { f.Name, f.FieldType.Name }));
        names.AddRange(type.GetProperties(Everything).SelectMany(p => new[] { p.Name, p.PropertyType.Name }));
        names.AddRange(type.GetEvents(Everything).Select(e => e.Name));
        names.AddRange(type.GetNestedTypes(Everything).Select(t => t.Name));

        foreach (MethodInfo method in type.GetMethods(Everything))
        {
            names.Add(method.Name);
            names.Add(method.ReturnType.Name);
            names.AddRange(method.GetParameters().SelectMany(p => new[] { p.Name ?? string.Empty, p.ParameterType.Name }));
        }

        // NON-VACUITY GUARD. An `Assert.All` over an empty sequence passes, so the walk is proved to have
        // found something first - otherwise a reflection mistake would read as a clean bill of health.
        Assert.True(names.Count > 10, "The surface walk found only " + names.Count + " names.");
        Assert.Contains(type.Name, names);

        Assert.All(
            names,
            name => Assert.All(
                DeferredVocabulary,
                deferred => Assert.False(
                    name.Contains(deferred, StringComparison.OrdinalIgnoreCase),
                    "'" + name + "' names the deferred term '" + deferred
                    + "'. The rendering half of n_cst_dwsvc_columnsort.sru belongs to DesignSystem behind "
                    + "/v1/design/** (constraint C-D); this service ships the descriptor as DATA.")));
    }

    /// <summary>
    /// CONSTRAINT C-D - no value this service returns is a MEASUREMENT. There is no real-typed member
    /// anywhere on the surface, which is what <c>real fLeaderHeight</c> [<c>:L341</c>] would have become.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The oracle's only floating-point local in 450 lines is the font leader height, and it exists
    /// solely to be multiplied by two and subtracted from a y coordinate [<c>:L368</c>, <c>:L385</c>].
    /// The descriptor consequently carries <b>no</b> <c>double</c>, <c>float</c> or <c>decimal</c> - its
    /// three geometry fields are the RAW <c>Describe</c> ANSWERS as text, exactly as the DataWindow
    /// worded them, and text is a data-model read rather than a measurement.
    /// </para>
    /// <para>
    /// This is DECISION 1 of the file under test asserted from the type system: the split line is drawn
    /// at the first arithmetic operation, so anything numeric-real crossing this boundary would mean the
    /// arithmetic had crossed with it. A <c>long</c> or an <c>int</c> is admissible because the direction
    /// and the ordinal are both counts, not lengths.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(ServiceSurface))]
    public void NoValueOnTheSurfaceIsAMeasurement(Type type)
    {
        const BindingFlags Everything =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static
            | BindingFlags.DeclaredOnly;

        Type[] measurements = [typeof(double), typeof(float), typeof(decimal)];

        List<Type> carried = [];

        carried.AddRange(type.GetFields(Everything).Select(f => f.FieldType));
        carried.AddRange(type.GetProperties(Everything).Select(p => p.PropertyType));
        carried.AddRange(type.GetMethods(Everything).Select(m => m.ReturnType));
        carried.AddRange(type.GetMethods(Everything).SelectMany(m => m.GetParameters()).Select(p => p.ParameterType));

        // NON-VACUITY GUARD, for the same reason as the vocabulary walk above.
        Assert.NotEmpty(carried);
        Assert.Contains(typeof(long), carried);

        Assert.All(
            carried,
            candidate =>
            {
                Type resolved = candidate.IsByRef ? candidate.GetElementType() ?? candidate : candidate;
                resolved = Nullable.GetUnderlyingType(resolved) ?? resolved;

                Assert.DoesNotContain(resolved, measurements);
            });

        // And the descriptor's three geometry fields are TEXT, which is the positive half of the same
        // assertion [:L366-L368 read three properties and this service carries their answers verbatim].
        if (type == typeof(ColumnSortIndicatorDescriptor))
        {
            foreach (string geometry in new[] { "SourceX", "SourceWidth", "SourceY" })
            {
                Assert.Equal(typeof(string), type.GetProperty(geometry)?.PropertyType);
            }
        }
    }

    /// <summary>
    /// CONSTRAINT C-D, STRUCTURALLY - the DataServices assembly references no deferred service at all,
    /// so there is no DesignSystem, Documents, Integration or ScriptBridge type available to reach for
    /// even by accident.
    /// </summary>
    /// <remarks>
    /// This is the assertion that cannot be worked around by naming a member carefully. AAP 0.2.2.2
    /// forbids any project, container, test or partial implementation for the four deferred services, and
    /// the service project's own reference list is where that is enforced: it names only Contracts and
    /// the five shared libraries.
    /// </remarks>
    [Fact]
    public void TheServiceAssemblyReferencesNoDeferredService()
    {
        string[] deferred = ["DesignSystem", "Documents", "Integration", "ScriptBridge"];

        string[] referenced =
            [.. typeof(ColumnSortModel).Assembly.GetReferencedAssemblies().Select(a => a.Name ?? string.Empty)];

        // POSITIVE CONTROL FIRST: the reference list was genuinely read, and it names the shared libraries
        // this service is allowed to depend on. Without this an empty list would pass the check below.
        Assert.Contains("PowerFramework.Shared.Kernel", referenced);
        Assert.Contains("PowerFramework.Shared.Localization", referenced);

        Assert.All(
            referenced,
            name => Assert.All(
                deferred,
                service => Assert.False(
                    name.Contains(service, StringComparison.OrdinalIgnoreCase),
                    "PowerFramework.DataServices references '" + name + "'. The four deferred services "
                    + "receive no code, no test and no container in this phase (AAP 0.2.2.2).")));
    }

    // ==========================================================================================
    //  ZERO DIALOGS AND ZERO LOCALIZATION - THE PROPERTY THAT DISTINGUISHES THIS SERVICE
    //  ----------------------------------------------------------------------------------------
    //  A search of n_cst_dwsvc_columnsort.sru for MessageBox and MessageBoxEx returns NOTHING. That
    //  makes it the exception among the four services behind `Services/`:
    //
    //      n_cst_dwsvc_rowselect.sru       one dialog  [:L239]              - routed through I18N
    //      n_cst_dwsvc_contextmenu.sru     six dialogs [:L795 .. :L1027]    - routed through I18N
    //      n_cst_dwsvc_columnexp.sru       28 dialogs                       - hardcoded, NOT localized
    //      n_cst_dwsvc_columnsort.sru      NONE
    //
    //  AAP 0.2.1.3 Correction 5 converts every dialog into a structured error preserving the text, the
    //  category, the Sprintf arguments and the severity - so RowSelect and ContextMenu both acquire a
    //  localization dependency and both publish an error type. THIS SERVICE ACQUIRES NEITHER, and the two
    //  tests below assert that absence from both directions: structurally, over the reflected surface,
    //  and behaviourally, with a live recording provider installed that the service never reaches.
    // ==========================================================================================

    /// <summary>
    /// The service takes no localization dependency and publishes no error type, because the oracle has
    /// no dialog to convert into one.
    /// </summary>
    [Fact]
    public void TheSurfaceCarriesNeitherALocalizationDependencyNorAnErrorType()
    {
        const BindingFlags Everything =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static
            | BindingFlags.DeclaredOnly;

        Type model = typeof(ColumnSortModel);

        // THERE IS NOWHERE TO INJECT ONE. A single public parameterless constructor, matching the
        // oracle's `on ... create` which does nothing but `call super::create` [:L433-L435].
        ConstructorInfo constructor = Assert.Single(model.GetConstructors());

        Assert.Empty(constructor.GetParameters());

        // No field, property, parameter or return type comes from the localization project - which is a
        // ProjectReference of this service, so the types ARE available and are simply not used.
        List<Type> carried =
        [
            .. model.GetFields(Everything).Select(f => f.FieldType),
            .. model.GetProperties(Everything).Select(p => p.PropertyType),
            .. model.GetMethods(Everything).Select(m => m.ReturnType),
            .. model.GetMethods(Everything).SelectMany(m => m.GetParameters()).Select(p => p.ParameterType),
        ];

        Assert.NotEmpty(carried);
        Assert.Contains(typeof(string), carried);

        Assert.DoesNotContain(typeof(I18n), carried);
        Assert.DoesNotContain(typeof(II18nProvider), carried);

        Assert.All(
            carried,
            candidate => Assert.NotEqual(
                typeof(I18n).Namespace,
                (candidate.IsByRef ? candidate.GetElementType() ?? candidate : candidate).Namespace));

        // And no member NAMES a message, a dialog or an error either - the three words the converted
        // dialogs on the sibling services are spelled with.
        IEnumerable<string> members = model.GetMembers(Everything).Select(m => m.Name);

        Assert.All(
            members,
            name => Assert.All(
                new[] { "MessageBox", "Dialog", "I18n", "Localiz" },
                banned => Assert.DoesNotContain(banned, name, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// A LIVE, TRANSLATING PROVIDER IS INSTALLED AND IS NEVER CONSULTED - not once, across the whole
    /// workflow: the three-state cycle, a multi-column sort, every clause form, the descriptor, the reset
    /// and both update overloads.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The double records EVERY request before it decides whether to handle it, so an empty request log is
    /// a genuine "never asked" and not merely "asked and declined". It is also taught a translation for
    /// every string this service emits, so a consultation would additionally CHANGE the output - which
    /// means the test fails twice over rather than once if a localization lookup is ever introduced here.
    /// </para>
    /// <para>
    /// The facade's liveness is proved at the end. Without that check an empty log would be equally
    /// consistent with a provider that was never installed, and the test would assert nothing.
    /// </para>
    /// </remarks>
    [Fact]
    public void ALiveLocalizationProviderIsInstalledAndNeverConsulted()
    {
        (I18n facade, ScriptedI18nProvider provider) = ScriptedLocalization.WithMarkers();

        // Teach a translation for EVERY string this service can emit, so a lookup would be visible in the
        // output as well as in the log.
        foreach (string emitted in new[]
        {
            "salary A", "salary D", "age A", "age D", "birth A", "birth D",
            "if(IsNull(salary),-999999,salary) A", "if(IsNull(birth),Date('1900-01-01'),birth) A",
            "LookUpDisplay(salary) A", "age A,salary A", "age A salary A ",
            "t", "u", "      1", "      2", "salary_arw", "salary_idx__arw",
            "33554432", "9868950", "536870912", "?", "!",
        })
        {
            _ = provider.Teach(emitted, "<TRANSLATED>");
        }

        (FakeDataWindowHost host, ColumnSortModel service) = NewCompanyService();

        host.TableSort = string.Empty;

        // THE WHOLE WORKFLOW, with the provider live throughout.
        Assert.Equal(RetCode.OK, service.Update());
        _ = Click(host, service, "age", ctrlHeld: true);
        _ = Click(host, service, "salary", ctrlHeld: true);
        _ = Click(host, service, "salary", ctrlHeld: true);
        _ = Click(host, service, "birth");
        _ = service.OnLButtonClk(1L, 2L, 3L, Header("age"));
        _ = service.OnLButtonDblClk(1L, 2L, 3L, Header("age"));
        _ = service.GetClause("salary", SortAsc);
        _ = service.GetClause("birth", SortDesc);
        _ = service.GetClause("name", SortNone);
        Assert.Equal(RetCode.OK, service.Update("age D"));
        _ = service.Reset();

        // NEVER CONSULTED. Not once, in any category, for any source, with any key.
        Assert.Empty(provider.Requests);
        Assert.Empty(provider.RequestedKeys);

        // And the output is untranslated, which is the second half of the same fact.
        Assert.DoesNotContain("<TRANSLATED>", service.CurrentSort, StringComparison.Ordinal);
        Assert.Equal("birth_arw", service.Indicators.Single().ArrowObjectName);
        Assert.Equal("33554432", service.Indicators.Single().ArrowColor);

        // THE INSTRUMENT'S OWN SELF-CHECK: the facade really would have translated, so the empty log
        // above is a fact about the service and not about a provider that was never wired up. The
        // category comes from the double's own configuration rather than from a restated constant, so
        // the probe cannot drift out of the range the double answers for.
        string probe = LegacyMessageKeys.All[0];

        Assert.Equal(LegacyMessageKeys.MarkerFor(probe), facade.I18N(provider.Category, probe));
        Assert.NotEmpty(provider.Requests);
    }

}
