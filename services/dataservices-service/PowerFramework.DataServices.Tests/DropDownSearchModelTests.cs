// ==============================================================================================
//  DropDownSearchModelTests - characterization of Services/DropDownSearchModel.cs against its
//  oracle, ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_dropdownsearch.sru (516 lines,
//  READ ONLY).
//  --------------------------------------------------------------------------------------------
//  These are characterization tests in Michael Feathers' sense (AAP 0.3.3): they assert what the
//  legacy ACTUALLY does, including the FIVE defects the refactor is required to preserve, rather
//  than what it arguably should do. Where a test looks like it is asserting a bug, it is - and the
//  assertion carries the locator that proves the bug is the oracle's. EVERY ONE OF THE FIVE DEFECT
//  TESTS FAILS IF THE DEFECT IS "FIXED", which is the whole point of writing them.
//
//  NO DataWindow, NO CHILD DATAWINDOW, NO DATABASE AND NO UI IS INVOLVED. Everything runs against
//  FakeDataWindowHost and FakeDataWindowChild, which satisfy the abstract host contract in memory,
//  so the suite is deterministic and needs no service, no container and no fixture file
//  (constraint C-H).
//
//  THE SEVEN ASSERTIONS THAT MATTER MOST, AND WHY THEY ARE HERE
//    1. GetFilter is BYTE-EXACT across the FULL FilterType POWER SET {0..7}. The output is a
//       DataWindow filter expression the DataWindow itself parses and it appears in characterization
//       recordings, so the matrix asserts whole strings and never substrings - down to the DOUBLE
//       parenthesis the single-clause cases produce, because the clause brings its own pair and
//       :L340 wraps the result in another.
//    2. DEFECT 1 - with the display bit clear the composed filter BEGINS WITH " OR ", because only
//       that clause assigns [:L319] and every later clause appends [:L323, :L331, :L334].
//    3. DEFECT 2 - a single quote, a percent and a close parenthesis all reach the expression
//       UNESCAPED. AAP 0.6.4 names :L323 as a filter-injection site and requires it be documented
//       rather than silently changed.
//    4. DEFECT 3 - OnEditChanged is NOT broker-subscribed [:L507], so triggering the edit-changed
//       topic must reach it not at all while the three live topics must.
//    5. DEFECT 4 - SetFilterType accepts zero and out-of-range values and answers OK [:L304-L305].
//    6. DEFECT 5 - three UpdateDddwFilter guards answer OK and the fourth answers FAILED
//       [:L438-L441].
//    7. The COUNT-AND-EVENT ORDERING: both counts are captured BEFORE the relocation [:L393-L394]
//       and the event is raised AFTER it [:L408] carrying those pre-move values. The child double
//       actually moves rows, so the pre-move and post-move numbers DIFFER and the assertion is
//       decisive rather than vacuous.
//
//  DEFERRED-HALF ASSERTIONS ARE POSITIVE, NOT ABSENT. Rather than merely not testing the deferred
//  window and geometry work, the suite asserts that the two data surfaces carry exactly what the
//  deferred calls would have consumed - DropDownSearchCompletion the SetText/SelectText arguments
//  including the ONE-BASED selection start, and DropDownSearchFilterPartition the two
//  SetDetailHeight row ranges and heights - so a later "helpful" rebasing or recomputation fails a
//  test instead of passing silently.
// ==============================================================================================

using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.Extensions.Options;

using PowerFramework.DataServices.Configuration;
using PowerFramework.DataServices.Domain;
using PowerFramework.DataServices.Services;
using PowerFramework.Shared.Eventful;
using PowerFramework.Shared.Kernel;

using Xunit;

namespace PowerFramework.DataServices.Tests;

/// <summary>
/// Characterization tests for <see cref="DropDownSearchModel"/>.
/// </summary>
public sealed class DropDownSearchModelTests
{
    private const string Column = "emp";
    private const string DispCol = "empname";
    private const string DataCol = "empid";
    private const string ChildName = "dw_emp_dddw";
    private const string OtherColumn = "dept";

    // ------------------------------------------------------------------------------------------
    //  ARRANGEMENT HELPERS
    // ------------------------------------------------------------------------------------------

    private static IOptions<DataServicesOptions> OptionsWith(
        uint filterType = 3,
        bool? showFilteredRows = null,
        long pinyinMatchFlags = 7)
    {
        return Options.Create(new DataServicesOptions
        {
            DropDownSearch = new DropDownSearchOptions
            {
                FilterType = filterType,
                ShowFilteredRows = showFilteredRows,
                PinyinMatchFlags = pinyinMatchFlags,
            },
        });
    }

    /// <summary>
    /// A host carrying one <c>dddw</c> column and its child, with every <c>Describe</c> answer the
    /// context builder reads [<c>:L198-L221</c>] taught explicitly.
    /// </summary>
    private static (FakeDataWindowHost Host, FakeDataWindowChild Child, DropDownSearchModel Service) NewDddw(
        uint filterType = 3,
        bool? showFilteredRows = null,
        long pinyinMatchFlags = 7,
        string dataColType = "char(20)",
        string dataCol = DataCol,
        string dispCol = DispCol,
        string childFilter = "?",
        string childSort = "?",
        string dispColType = "char(50)",
        string allowEdit = "yes",
        string displayOnly = "no",
        string editStyle = "dddw")
    {
        FakeDataWindowHost host = new(new EventBroker());
        _ = host.AddColumn(Column, "char(50)");
        _ = host.AddColumn(OtherColumn, "char(50)");

        host.SetDescribe(Column + ".Edit.DisplayOnly", displayOnly);
        host.SetDescribe(Column + ".Edit.Style", editStyle);
        host.SetDescribe(Column + ".DDDW.AllowEdit", allowEdit);
        host.SetDescribe(Column + ".DDDW.DisplayColumn", dispCol);
        host.SetDescribe(Column + ".DDDW.DataColumn", dataCol);

        FakeDataWindowChild child = new(ChildName);
        _ = child.AddColumn(dispCol, dispColType);
        if (!string.Equals(dataCol, dispCol, StringComparison.Ordinal))
        {
            _ = child.AddColumn(dataCol, dataColType);
        }

        child.SetDescribe("DataWindow.Detail.Height", "76");
        child.SetDescribe("DataWindow.Table.Filter", childFilter);
        child.SetDescribe("DataWindow.Table.Sort", childSort);

        // ONE INTERLEAVED SEQUENCE across host and child, which is what makes the count-and-event
        // ordering assertion possible at all.
        child.Calls = host.CallLog;

        host.SetChild(Column, child);

        DropDownSearchModel service = new(OptionsWith(filterType, showFilteredRows, pinyinMatchFlags));
        service.OnInit(host);

        return (host, child, service);
    }

    private static IDataWindowObject Dwo(FakeDataWindowHost host, string column = Column)
    {
        return host.GetObjectAttribute(column);
    }

    /// <summary>
    /// Builds the host and drives the item-focus event, so the edit context is valid and describing
    /// row one of the <c>dddw</c> column.
    /// </summary>
    private static (FakeDataWindowHost Host, FakeDataWindowChild Child, DropDownSearchModel Service) Focused(
        uint filterType = 3,
        bool? showFilteredRows = null,
        long pinyinMatchFlags = 7,
        string dataColType = "char(20)",
        string dataCol = DataCol,
        string childFilter = "?")
    {
        (FakeDataWindowHost host, FakeDataWindowChild child, DropDownSearchModel service) = NewDddw(
            filterType,
            showFilteredRows,
            pinyinMatchFlags,
            dataColType,
            dataCol,
            childFilter: childFilter);

        service.OnItemFocusChanged(1L, Dwo(host));
        host.CallLog.Clear();

        return (host, child, service);
    }

    // ==========================================================================================
    //  SHAPE - THE PORT'S SURFACE AGAINST THE ORACLE'S DECLARATIONS
    // ==========================================================================================

    /// <summary>
    /// <c>:L50-L53</c> - the four constants, and <see cref="DropDownSearchModel.FILTER_ALL"/> as the
    /// SUM of the other three rather than as an independent literal.
    /// </summary>
    [Fact]
    public void TheFourFilterConstantsAreTheOracleValues()
    {
        Assert.Equal(1u, DropDownSearchModel.FILTER_DISP);
        Assert.Equal(2u, DropDownSearchModel.FILTER_DISP_PY);
        Assert.Equal(4u, DropDownSearchModel.FILTER_DATA);

        // The derivation, asserted rather than the literal, so that changing any bit forces this line
        // to be revisited deliberately.
        Assert.Equal(
            DropDownSearchModel.FILTER_DISP
                + DropDownSearchModel.FILTER_DISP_PY
                + DropDownSearchModel.FILTER_DATA,
            DropDownSearchModel.FILTER_ALL);
        Assert.Equal(7u, DropDownSearchModel.FILTER_ALL);
    }

    /// <summary>
    /// <c>:L57</c> - the default is the TWO-TERM SUM, that is <c>3</c>, and emphatically NOT
    /// <see cref="DropDownSearchModel.FILTER_ALL"/>.
    /// </summary>
    /// <remarks>
    /// DECISION 5 of the file under test: <c>FILTER_ALL</c> is 7 and the Pinyin flags are also 7, and a
    /// reader has already been misled by the coincidence. This test pins the one that is NOT 7.
    /// </remarks>
    [Fact]
    public void TheDefaultFilterTypeIsTheTwoTermSumAndNotFilterAll()
    {
        DropDownSearchModel service = new(Options.Create(new DataServicesOptions()));

        Assert.Equal(
            DropDownSearchModel.FILTER_DISP + DropDownSearchModel.FILTER_DISP_PY,
            service.FilterType);
        Assert.Equal(3u, service.FilterType);
        Assert.NotEqual(DropDownSearchModel.FILTER_ALL, service.FilterType);
    }

    /// <summary>
    /// <c>:L58</c>, <c>:L503</c> - the three-state flag is constructed NULL, because the oracle's
    /// constructor is a super call plus <c>SetNull(#ShowFilteredRows)</c>.
    /// </summary>
    [Fact]
    public void ShowFilteredRowsIsConstructedNull()
    {
        DropDownSearchModel service = new(Options.Create(new DataServicesOptions()));

        Assert.Null(service.ShowFilteredRows);
    }

    /// <summary>
    /// The two data surfaces start empty, because nothing has happened yet.
    /// </summary>
    [Fact]
    public void TheTwoDataSurfacesStartEmpty()
    {
        DropDownSearchModel service = new(Options.Create(new DataServicesOptions()));

        Assert.Null(service.Completion);
        Assert.Null(service.Partition);
    }

    /// <summary>
    /// The constructor rejects a null options argument rather than deferring the failure.
    /// </summary>
    [Fact]
    public void TheConstructorRejectsNullOptions()
    {
        _ = Assert.Throws<ArgumentNullException>(() => new DropDownSearchModel(null!));
    }

    // ==========================================================================================
    //  GetFilter - THE BYTE-EXACT PARITY MATRIX OVER THE FULL FilterType POWER SET  [:L313-L345]
    // ==========================================================================================

    /// <summary>
    /// Every subset of the three bits, with the exact expression each composes for the input
    /// <c>"ab"</c> over a <c>char</c> data column.
    /// </summary>
    /// <remarks>
    /// NOTE THE DOUBLE PARENTHESES throughout. Each clause brings its own pair and <c>:L340</c> wraps
    /// the whole composition in another, so a single-clause filter legitimately carries two - and a
    /// port that "tidied" one away would select the same rows while failing every stored comparison.
    /// </remarks>
    public static TheoryData<uint, string> FilterMatrix() => new()
    {
        // 0 - no bit set. DEFECT 4's companion: nothing composes, the wrap is skipped, and no
        // diagnostic is produced anywhere.
        { 0u, "" },

        // 1 - FILTER_DISP alone [:L319]
        { 1u, "((Lower(empname) LIKE '%ab%'))" },

        // 2 - FILTER_DISP_PY alone [:L323]. DEFECT 1: it BEGINS WITH " OR ".
        { 2u, "( OR PinyinFirstLetterLike(empname,'ab',7))" },

        // 3 - the DEFAULT [:L57]
        { 3u, "((Lower(empname) LIKE '%ab%') OR PinyinFirstLetterLike(empname,'ab',7))" },

        // 4 - FILTER_DATA alone [:L331]. DEFECT 1 again.
        { 4u, "( OR (Lower(empid) LIKE '%ab%'))" },

        // 5 - display plus data, skipping Pinyin
        { 5u, "((Lower(empname) LIKE '%ab%') OR (Lower(empid) LIKE '%ab%'))" },

        // 6 - Pinyin plus data, both appending. DEFECT 1 again.
        { 6u, "( OR PinyinFirstLetterLike(empname,'ab',7) OR (Lower(empid) LIKE '%ab%'))" },

        // 7 - FILTER_ALL: all three clauses, in declaration order, one wrapping pair
        {
            7u,
            "((Lower(empname) LIKE '%ab%') OR PinyinFirstLetterLike(empname,'ab',7)"
                + " OR (Lower(empid) LIKE '%ab%'))"
        },
    };

    /// <summary>
    /// <c>:L313-L345</c> - the composed expression is byte-exact for every subset of the three bits.
    /// </summary>
    [Theory]
    [MemberData(nameof(FilterMatrix))]
    public void TheComposedFilterIsByteExactAcrossTheWholePowerSet(uint filterType, string expected)
    {
        (_, _, DropDownSearchModel service) = Focused(filterType);

        Assert.Equal(expected, service.GetFilter("ab"));
    }

    /// <summary>
    /// <c>:L318-L332</c> - DEFECT 1. With the display bit clear the filter BEGINS WITH <c>" OR "</c>,
    /// which is a syntactically broken expression.
    /// </summary>
    /// <remarks>
    /// THIS TEST MUST FAIL IF ANYONE "FIXES" THE JOIN. Only <c>:L319</c> assigns; <c>:L323</c>,
    /// <c>:L331</c> and <c>:L334</c> all append with a leading <c>" OR "</c>, so the dangling operator
    /// is observable output and not a latent hazard.
    /// </remarks>
    [Theory]
    [InlineData(2u)]
    [InlineData(4u)]
    [InlineData(6u)]
    public void ADisabledDisplayBitLeavesADanglingLeadingOr(uint filterType)
    {
        (_, _, DropDownSearchModel service) = Focused(filterType);

        string filter = service.GetFilter("ab");

        Assert.StartsWith("( OR ", filter, StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>:L322</c> - the Pinyin clause is omitted entirely when the input carries no ASCII letter,
    /// because Pinyin first letters are Latin letters.
    /// </summary>
    [Theory]
    [InlineData("123")]
    [InlineData("张三")]
    [InlineData("%")]
    [InlineData("-")]
    public void ThePinyinClauseIsOmittedWithoutAnAsciiLetter(string data)
    {
        (_, _, DropDownSearchModel service) = Focused(DropDownSearchModel.FILTER_DISP_PY);

        // DEFECT 1 does not even get a chance to show: with the only enabled clause suppressed the
        // whole expression is empty and :L340's wrap is skipped.
        Assert.Equal(string.Empty, service.GetFilter(data));
    }

    /// <summary>
    /// <c>:L322</c> - one ASCII letter anywhere in the input is enough, because <c>Match</c> searches
    /// rather than anchors.
    /// </summary>
    [Theory]
    [InlineData("1a")]
    [InlineData("a1")]
    [InlineData("张A")]
    public void OneAsciiLetterAnywhereAdmitsThePinyinClause(string data)
    {
        (_, _, DropDownSearchModel service) = Focused(DropDownSearchModel.FILTER_DISP_PY);

        Assert.Contains("PinyinFirstLetterLike", service.GetFilter(data), StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>:L327</c> - the data clause is omitted when the data column IS the display column.
    /// </summary>
    [Fact]
    public void TheDataClauseIsOmittedWhenTheDataColumnIsTheDisplayColumn()
    {
        (_, _, DropDownSearchModel service) = Focused(
            DropDownSearchModel.FILTER_DATA,
            dataCol: DispCol);

        Assert.Equal(string.Empty, service.GetFilter("ab"));
    }

    /// <summary>
    /// <c>:L329-L336</c> - the four-character <c>ColType</c> prefix switch, including the ABSENCE of a
    /// default arm.
    /// </summary>
    /// <remarks>
    /// The parameterised spellings are the point: <c>char(20)</c> truncates to <c>"char"</c> and
    /// <c>decimal(2)</c> to <c>"deci"</c>, so widening the comparison to a full string would make both
    /// miss their arm. <c>date</c> and <c>datetime</c> both fall through to nothing at all - not an
    /// error, not a fallback clause, silence.
    /// </remarks>
    public static TheoryData<string, string, string> ColumnTypeMatrix() => new()
    {
        { "char(20)", "ab", "( OR (Lower(empid) LIKE '%ab%'))" },
        { "char", "ab", "( OR (Lower(empid) LIKE '%ab%'))" },
        { "number", "12", "( OR (empid = 12))" },
        { "decimal(2)", "12", "( OR (empid = 12))" },
        { "long", "12", "( OR (empid = 12))" },
        { "ulong", "12", "( OR (empid = 12))" },

        // NO DEFAULT ARM [:L329-L336] - a date, datetime, time or boolean column contributes nothing.
        { "date", "12", "" },
        { "datetime", "12", "" },
        { "time", "12", "" },
        { "boolean", "12", "" },
    };

    /// <summary>
    /// <c>:L329-L336</c> - one arm per column-type prefix, and nothing for an unmatched one.
    /// </summary>
    [Theory]
    [MemberData(nameof(ColumnTypeMatrix))]
    public void TheDataClauseDispatchesOnAFourCharacterColumnTypePrefix(
        string dataColType,
        string data,
        string expected)
    {
        (_, _, DropDownSearchModel service) = Focused(
            DropDownSearchModel.FILTER_DATA,
            dataColType: dataColType);

        Assert.Equal(expected, service.GetFilter(data));
    }

    /// <summary>
    /// <c>:L333</c> - the numeric arm is guarded by <c>IsNumber</c>, so non-numeric text produces no
    /// clause at all rather than an unparseable comparison.
    /// </summary>
    [Theory]
    [InlineData("ab")]
    [InlineData("12ab")]
    [InlineData("1,000")]
    public void TheNumericDataClauseIsOmittedForNonNumericInput(string data)
    {
        (_, _, DropDownSearchModel service) = Focused(
            DropDownSearchModel.FILTER_DATA,
            dataColType: "number");

        Assert.Equal(string.Empty, service.GetFilter(data));
    }

    /// <summary>
    /// <c>:L334</c> - the numeric clause emits the RAW argument, neither lower-cased, nor trimmed, nor
    /// space-substituted, nor quoted.
    /// </summary>
    [Theory]
    [InlineData("12", "( OR (empid = 12))")]
    [InlineData("-3.5", "( OR (empid = -3.5))")]
    [InlineData("1E2", "( OR (empid = 1E2))")]
    [InlineData("+7", "( OR (empid = +7))")]
    public void TheNumericDataClauseEmitsTheRawArgument(string data, string expected)
    {
        (_, _, DropDownSearchModel service) = Focused(
            DropDownSearchModel.FILTER_DATA,
            dataColType: "number");

        Assert.Equal(expected, service.GetFilter(data));
    }

    /// <summary>
    /// <c>:L316-L317</c> - the pipeline is <c>Lower</c>, THEN <c>Trim</c>, THEN space-to-<c>%</c>, so
    /// an interior space becomes a WILDCARD while surrounding spaces are removed.
    /// </summary>
    public static TheoryData<string, string> PipelineMatrix() => new()
    {
        { "ab", "%ab%" },
        { "AB", "%ab%" },
        { "ab cd", "%ab%cd%" },
        { "  ab  ", "%ab%" },
        { " AB CD ", "%ab%cd%" },

        // Repeated interior spaces become repeated wildcards - the substitution is per character.
        { "ab  cd", "%ab%%cd%" },

        // A tab is CONTENT to the oracle: PowerScript's Trim removes spaces, and the substitution
        // targets the space character only.
        { "ab\tcd", "%ab\tcd%" },
    };

    /// <summary>
    /// <c>:L316-L317</c>, <c>:L319</c> - the composed LIKE pattern for each input shape.
    /// </summary>
    [Theory]
    [MemberData(nameof(PipelineMatrix))]
    public void TheSearchTextPipelineLowersThenTrimsThenSubstitutesSpaces(string data, string pattern)
    {
        (_, _, DropDownSearchModel service) = Focused(DropDownSearchModel.FILTER_DISP);

        Assert.Equal("((Lower(empname) LIKE '" + pattern + "'))", service.GetFilter(data));
    }

    /// <summary>
    /// <c>:L317</c>, <c>:L323</c> - the Pinyin clause uses the merely LOWER-CASED text while the LIKE
    /// clauses use the SPACE-SUBSTITUTED one. The asymmetry is deliberate and observable.
    /// </summary>
    [Fact]
    public void ThePinyinClauseKeepsTheSpacesTheLikeClauseSubstitutes()
    {
        (_, _, DropDownSearchModel service) = Focused(
            DropDownSearchModel.FILTER_DISP + DropDownSearchModel.FILTER_DISP_PY);

        Assert.Equal(
            "((Lower(empname) LIKE '%zh%s%') OR PinyinFirstLetterLike(empname,'zh s',7))",
            service.GetFilter("ZH S"));
    }

    /// <summary>
    /// <c>:L323</c> - the flags value comes from configuration, whose declared default reproduces the
    /// oracle's hardcoded <c>7</c>.
    /// </summary>
    [Theory]
    [InlineData(7L, "( OR PinyinFirstLetterLike(empname,'ab',7))")]
    [InlineData(1L, "( OR PinyinFirstLetterLike(empname,'ab',1))")]
    [InlineData(0L, "( OR PinyinFirstLetterLike(empname,'ab',0))")]
    public void ThePinyinFlagsAreEmittedFromConfiguration(long flags, string expected)
    {
        (_, _, DropDownSearchModel service) = Focused(
            DropDownSearchModel.FILTER_DISP_PY,
            pinyinMatchFlags: flags);

        Assert.Equal(expected, service.GetFilter("ab"));
    }

    /// <summary>
    /// <c>:L319</c>, <c>:L323</c>, <c>:L331</c>, <c>:L334</c> - DEFECT 2. Every interpolated value
    /// reaches the expression UNESCAPED.
    /// </summary>
    /// <remarks>
    /// THIS TEST MUST FAIL IF ANYONE ADDS SANITISATION. AAP 0.6.4 names <c>:L323</c> explicitly as a
    /// filter-injection site and requires it be DOCUMENTED rather than silently changed; constraint C-B
    /// forbids the correction outright. Note that the apostrophe is the interesting one - it terminates
    /// the literal the expression is building - and that it survives into BOTH the LIKE clause and the
    /// Pinyin clause.
    /// </remarks>
    [Theory]
    [InlineData("o'brien", "%o'brien%", "o'brien")]
    [InlineData("a%b", "%a%b%", "a%b")]
    [InlineData("x)", "%x)%", "x)")]
    [InlineData("a' OR '1'='1", "%a'%or%'1'='1%", "a' or '1'='1")]
    public void InjectionShapedInputIsInterpolatedUnescaped(
        string data,
        string expectedPattern,
        string expectedPinyinLiteral)
    {
        (_, _, DropDownSearchModel service) = Focused(
            DropDownSearchModel.FILTER_DISP + DropDownSearchModel.FILTER_DISP_PY);

        string filter = service.GetFilter(data);

        Assert.Equal(
            "((Lower(empname) LIKE '" + expectedPattern + "')"
                + " OR PinyinFirstLetterLike(empname,'" + expectedPinyinLiteral + "',7))",
            filter);
    }

    /// <summary>
    /// <c>:L315</c>, <c>:L342</c> - empty data skips the ENTIRE clause builder but STILL RAISES the
    /// semantic event, which is the documented extension point for an initial filter.
    /// </summary>
    [Fact]
    public void EmptyDataSkipsTheBuilderButStillRaisesTheEvent()
    {
        (FakeDataWindowHost host, _, DropDownSearchModel service) = Focused(
            DropDownSearchModel.FILTER_ALL);

        List<string> seen = [];
        host.DdsGetFilterHandler = (row, dwo, data, filter) =>
        {
            seen.Add(data);
            return filter;
        };

        Assert.Equal(string.Empty, service.GetFilter(string.Empty));
        Assert.Equal([string.Empty], seen);
    }

    /// <summary>
    /// <c>:L342-L344</c> - whatever the handler leaves in the <c>ref</c> parameter is what
    /// <c>GetFilter</c> answers, replacing the composed expression entirely.
    /// </summary>
    [Fact]
    public void TheHandlerCanReplaceTheComposedFilter()
    {
        (FakeDataWindowHost host, _, DropDownSearchModel service) = Focused(
            DropDownSearchModel.FILTER_DISP);

        string? composed = null;
        host.DdsGetFilterHandler = (row, dwo, data, filter) =>
        {
            composed = filter;
            return "dept = 'X'";
        };

        Assert.Equal("dept = 'X'", service.GetFilter("ab"));

        // The handler saw the composition, so replacement is a choice rather than the only option.
        Assert.Equal("((Lower(empname) LIKE '%ab%'))", composed);
    }

    /// <summary>
    /// <c>:L342</c> - the event carries the edit context's own row and column, not the caller's.
    /// </summary>
    [Fact]
    public void TheGetFilterEventCarriesTheEditContextRowAndColumn()
    {
        (FakeDataWindowHost host, _, DropDownSearchModel service) = Focused();

        long seenRow = -1;
        string? seenName = null;
        host.DdsGetFilterHandler = (row, dwo, data, filter) =>
        {
            seenRow = row;
            seenName = dwo?.Name;
            return filter;
        };

        _ = service.GetFilter("ab");

        Assert.Equal(1L, seenRow);
        Assert.Equal(Column, seenName);
    }

    // ==========================================================================================
    //  SetFilterType - DEFECT 4                                                   [:L304-L305]
    // ==========================================================================================

    /// <summary>
    /// <c>:L304-L305</c> - DEFECT 4. The setter validates NOTHING: zero and out-of-range values are
    /// both accepted and both answer <c>RetCode.OK</c>.
    /// </summary>
    /// <remarks>
    /// THIS TEST MUST FAIL IF A GUARD IS ADDED. The exact inverse of the row-select service, whose own
    /// style setter DOES reject zero - and the asymmetry must not be harmonised in either direction.
    /// </remarks>
    [Theory]
    [InlineData(0u)]
    [InlineData(1u)]
    [InlineData(7u)]
    [InlineData(8u)]
    [InlineData(uint.MaxValue)]
    public void SetFilterTypeAcceptsEveryValueIncludingZeroAndOutOfRange(uint filterType)
    {
        (_, _, DropDownSearchModel service) = Focused();

        Assert.Equal(RetCode.OK, service.SetFilterType(filterType));
        Assert.Equal(filterType, service.FilterType);
    }

    /// <summary>
    /// <c>:L304-L305</c> with <c>:L340</c> - DEFECT 4 compounded with DEFECT 1: a zero filter type
    /// composes an EMPTY filter and reports success, with no diagnostic anywhere.
    /// </summary>
    [Fact]
    public void AZeroFilterTypeComposesAnEmptyFilterSilently()
    {
        (_, _, DropDownSearchModel service) = Focused();

        Assert.Equal(RetCode.OK, service.SetFilterType(0u));
        Assert.Equal(string.Empty, service.GetFilter("ab"));
    }

    /// <summary>
    /// <c>:L283-L284</c> - the show-filtered-rows setter answers <c>OK</c> and moves the property off
    /// its constructed null.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SetShowFilteredRowsAssignsAndAnswersOk(bool show)
    {
        (_, _, DropDownSearchModel service) = Focused();

        Assert.Null(service.ShowFilteredRows);
        Assert.Equal(RetCode.OK, service.SetShowFilteredRows(show));
        Assert.Equal(show, service.ShowFilteredRows);
    }

    // ==========================================================================================
    //  IsShowFilteredRows - THE THREE-STATE RESOLVER                              [:L260-L263]
    // ==========================================================================================

    /// <summary>
    /// <c>:L260</c> - an explicit <see langword="false"/> short-circuits the resolver even when the
    /// auto-determination would answer true.
    /// </summary>
    /// <remarks>
    /// The fixture is a GRID, so arm <c>:L262</c> would answer TRUE. Asserting false is therefore
    /// evidence of the short-circuit rather than a coincidence.
    /// </remarks>
    [Fact]
    public void AnExplicitFalseShortCircuitsAResolverThatWouldAnswerTrue()
    {
        (FakeDataWindowHost host, _, DropDownSearchModel service) = Focused(showFilteredRows: false);
        host.Processing = "1";

        Assert.False(service.IsShowFilteredRows());
    }

    /// <summary>
    /// <c>:L260</c> - an explicit <see langword="true"/> short-circuits the resolver even when the
    /// auto-determination would answer false.
    /// </summary>
    /// <remarks>
    /// The display and data columns are THE SAME, so arm <c>:L261</c> would answer FALSE and would do so
    /// BEFORE either later arm ran. Asserting true is therefore evidence of the short-circuit.
    /// </remarks>
    [Fact]
    public void AnExplicitTrueShortCircuitsAResolverThatWouldAnswerFalse()
    {
        (_, _, DropDownSearchModel service) = Focused(showFilteredRows: true, dataCol: DispCol);

        Assert.True(service.IsShowFilteredRows());
    }

    /// <summary>
    /// <c>:L261</c> - with the display and data columns the SAME column there is no second column
    /// whose text could be corrupted, so the answer is false.
    /// </summary>
    [Fact]
    public void TheResolverAnswersFalseWhenTheTwoColumnsAreTheSame()
    {
        (_, _, DropDownSearchModel service) = Focused(dataCol: DispCol);

        Assert.Null(service.ShowFilteredRows);
        Assert.False(service.IsShowFilteredRows());
    }

    /// <summary>
    /// <c>:L262</c> - a GRID presentation style answers true, because a grid shows every row's text.
    /// </summary>
    [Fact]
    public void TheResolverAnswersTrueForAGridPresentationStyle()
    {
        (FakeDataWindowHost host, _, DropDownSearchModel service) = Focused();
        host.Processing = "1";

        // Proven to be the GRID arm and not the row-count arm: the DataWindow has no rows at all, so
        // :L263's conjunction could not answer true.
        Assert.Equal(0L, host.RowCount());
        Assert.True(service.IsShowFilteredRows());
    }

    /// <summary>
    /// <c>:L263</c> - the final arm is the CONJUNCTION of more than one row and a vertical scroll bar,
    /// read as a DataWindow attribute through <c>Describe</c>.
    /// </summary>
    [Theory]
    [InlineData(2, "yes", true)]
    [InlineData(2, "1", true)]
    [InlineData(2, "no", false)]
    [InlineData(2, "0", false)]

    // A DataWindow that could not answer must not tip the decision towards relocating.
    [InlineData(2, "?", false)]
    [InlineData(2, "!", false)]

    // One row or fewer has nothing to corrupt, whatever the scroll bar says.
    [InlineData(1, "yes", false)]
    [InlineData(0, "yes", false)]
    public void TheFinalResolverArmIsRowCountAndScrollBar(int rows, string scrollBar, bool expected)
    {
        (FakeDataWindowHost host, _, DropDownSearchModel service) = Focused();

        // Anything but grid, so the third arm is reached.
        host.Processing = "0";
        host.SetDescribe("DataWindow.VScrollBar", scrollBar);
        for (int index = 0; index < rows; index++)
        {
            _ = host.AddRow("value");
        }

        Assert.Equal(expected, service.IsShowFilteredRows());
    }

    // ==========================================================================================
    //  ApplyFilter - COMPOSITION, THE CHANGE GUARD, AND THE COUNT-AND-EVENT ORDERING [:L368-L417]
    // ==========================================================================================

    /// <summary>
    /// <c>:L374-L380</c> - the three composition arms, byte-exact.
    /// </summary>
    /// <remarks>
    /// The third arm is the one that matters most: an EMPTY user filter RESTORES the child's original
    /// filter rather than clearing it, which is what makes the lose-focus reset a restore.
    /// </remarks>
    [Theory]

    // :L375-L377  a user filter WITH an original filter is CONJOINED, original first
    [InlineData("dept='X'", "age>30", "(dept='X') AND (age>30)")]

    // :L372  a user filter with NO original filter is used alone
    [InlineData("", "age>30", "age>30")]

    // :L379  an EMPTY user filter RESTORES the original - which on a freshly built context is already
    //        what is applied [:L218], so the change guard at :L382 suppresses the pass entirely and
    //        SetFilter is never reached. AnEmptyUserFilterRestoresTheOriginal proves the restore where
    //        it IS observable.
    [InlineData("dept='X'", "", null)]

    // :L379 again, with nothing on either side: still no change, still nothing done.
    [InlineData("", "", null)]
    public void TheThreeCompositionArmsAreByteExact(
        string originalFilter,
        string userFilter,
        string? expected)
    {
        (_, FakeDataWindowChild child, DropDownSearchModel service) = Focused(
            childFilter: originalFilter.Length == 0 ? "?" : originalFilter);

        service.ApplyFilter(1L, null, userFilter, show: false);

        Assert.Equal(expected, child.LastFilterSet);
    }

    /// <summary>
    /// <c>:L378-L380</c> - an empty user filter RESTORES the child's original filter rather than
    /// clearing the child, which is what makes the lose-focus reset at <c>:L158</c> a restore.
    /// </summary>
    [Fact]
    public void AnEmptyUserFilterRestoresTheOriginal()
    {
        (_, FakeDataWindowChild child, DropDownSearchModel service) = Focused(
            childFilter: "dept='X'");

        service.ApplyFilter(1L, null, "age>30", show: false);
        Assert.Equal("(dept='X') AND (age>30)", child.LastFilterSet);

        service.ApplyFilter(1L, null, string.Empty, show: false);

        // NOT the empty string - the ORIGINAL. A port that cleared the child here would strip a filter
        // the application, not the user, had put there.
        Assert.Equal("dept='X'", child.LastFilterSet);
        Assert.Equal(string.Empty, service.EditContext.Dddw.InputFilter);
    }

    /// <summary>
    /// <c>:L382</c> - the change guard suppresses EVERYTHING when the composition is unchanged: no
    /// redraw suppression, no filter, no sort, no move and no event.
    /// </summary>
    [Fact]
    public void AnUnchangedCompositionTouchesNothing()
    {
        (FakeDataWindowHost host, FakeDataWindowChild child, DropDownSearchModel service) = Focused();

        service.ApplyFilter(1L, null, "age>30", show: false);
        Assert.NotEmpty(host.CallLog.Members);

        host.CallLog.Clear();
        child.LastFilterSet = null;

        // The same user filter a second time - identical composition, so the guard holds.
        service.ApplyFilter(1L, null, "age>30", show: false);

        Assert.Empty(host.CallLog.Members);
        Assert.Null(child.LastFilterSet);
    }

    /// <summary>
    /// <c>:L385-L411</c> - the exact call sequence, with the redraw suppression bracketing everything
    /// and the semantic event raised between the relocation and the redraw restore.
    /// </summary>
    [Fact]
    public void TheApplySequenceIsExactlyTheOraclesOrder()
    {
        (FakeDataWindowHost host, FakeDataWindowChild child, DropDownSearchModel service) = Focused();
        child.FilteredRowCount = 3;
        _ = child.AddRow("a");
        _ = child.AddRow("b");

        service.ApplyFilter(1L, null, "age>30", show: false);

        Assert.Equal(
            [
                "SetRedraw",                 // :L387  suppression, applied unconditionally
                "SetFilter",                 // :L389
                "Filter",                    // :L390
                "Sort",                      // :L391
                "RowsMoveFilterToPrimary",   // :L402  the relocation
                "Event OnDDSFiltered",       // :L408  AFTER the move, with PRE-move counts
                "SetRedraw",                 // :L410  restore
            ],
            host.CallLog.Members);
    }

    /// <summary>
    /// <c>:L392-L394</c> with <c>:L407-L408</c> - THE ORDERING CONTRACT. Both counts are captured
    /// BEFORE the relocation and the event carries those values, not the post-move ones.
    /// </summary>
    /// <remarks>
    /// DECISIVE RATHER THAN VACUOUS: the child double actually moves rows on a successful relocation, so
    /// the post-move row count is 5 and the post-move filtered count is 0 - both different from the
    /// pre-move pair the event must carry. The oracle stresses the requirement twice, in identical
    /// comments at <c>:L392</c> and <c>:L407</c>.
    /// </remarks>
    [Fact]
    public void TheFilteredEventCarriesThePreMoveCounts()
    {
        (FakeDataWindowHost host, FakeDataWindowChild child, DropDownSearchModel service) = Focused();
        _ = child.AddRow("a");
        _ = child.AddRow("b");
        child.FilteredRowCount = 3;

        long seenRowCount = -1;
        long seenFilteredCount = -1;
        host.DdsFilteredHandler = (row, dwo, rowCount, filteredCount) =>
        {
            seenRowCount = rowCount;
            seenFilteredCount = filteredCount;
        };

        service.ApplyFilter(7L, null, "age>30", show: false);

        Assert.Equal(2L, seenRowCount);
        Assert.Equal(3L, seenFilteredCount);

        // Proof the distinction is real: after the move the child answers different numbers.
        Assert.Equal(5L, child.RowCount());
        Assert.Equal(0L, child.FilteredCount());
    }

    /// <summary>
    /// <c>:L408</c> - the event carries the row and column it was GIVEN, which are the edit context's on
    /// every ported path.
    /// </summary>
    [Fact]
    public void TheFilteredEventCarriesTheSuppliedRowAndColumn()
    {
        (FakeDataWindowHost host, _, DropDownSearchModel service) = Focused();

        long seenRow = -1;
        string? seenName = null;
        host.DdsFilteredHandler = (row, dwo, rowCount, filteredCount) =>
        {
            seenRow = row;
            seenName = dwo?.Name;
        };

        service.ApplyFilter(4L, Dwo(host), "age>30", show: false);

        Assert.Equal(4L, seenRow);
        Assert.Equal(Column, seenName);
    }

    /// <summary>
    /// <c>:L396-L405</c> - DECISION 3 of the file under test. The partition publishes exactly what the
    /// two deferred <c>SetDetailHeight</c> calls would have consumed.
    /// </summary>
    [Fact]
    public void TheRelocatingPartitionPublishesBothDeferredHeightRanges()
    {
        (_, FakeDataWindowChild child, DropDownSearchModel service) = Focused();
        _ = child.AddRow("a");
        _ = child.AddRow("b");
        child.FilteredRowCount = 3;

        service.ApplyFilter(1L, null, "age>30", show: false);

        DropDownSearchFilterPartition partition = Assert.IsType<DropDownSearchFilterPartition>(
            service.Partition);

        Assert.Equal(2L, partition.RowCount);
        Assert.Equal(3L, partition.FilteredCount);
        Assert.True(partition.Relocated);
        Assert.True(partition.RelocationSucceeded);
        Assert.Equal(3L, partition.RelocatedRowCount);

        // :L399  SetDetailHeight(1, nRowCnt, rowHeight) - the captured Describe answer, untransformed.
        Assert.Equal(1L, partition.VisibleRowStart);
        Assert.Equal(2L, partition.VisibleRowEnd);
        Assert.Equal(76L, partition.VisibleRowHeight);

        // :L404  SetDetailHeight(nRowCnt + 1, RowCount(), 0) - a ONE-BASED start and a POST-move end.
        Assert.Equal(3L, partition.HiddenRowStart);
        Assert.Equal(5L, partition.HiddenRowEnd);
        Assert.Equal(0L, partition.HiddenRowHeight);
    }

    /// <summary>
    /// <c>:L396</c> - with relocation declined, no move is attempted and every range is empty.
    /// </summary>
    [Fact]
    public void ADeclinedRelocationLeavesEveryRangeEmpty()
    {
        (FakeDataWindowHost host, FakeDataWindowChild child, DropDownSearchModel service) = Focused(
            showFilteredRows: false);
        child.FilteredRowCount = 3;
        _ = child.AddRow("a");

        service.ApplyFilter(1L, null, "age>30", show: false);

        DropDownSearchFilterPartition partition = Assert.IsType<DropDownSearchFilterPartition>(
            service.Partition);

        Assert.False(partition.Relocated);
        Assert.False(partition.RelocationSucceeded);
        Assert.Equal(0L, partition.RelocatedRowCount);
        Assert.Equal(0L, partition.VisibleRowEnd);
        Assert.Equal(0L, partition.HiddenRowStart);
        Assert.Equal(0L, partition.HiddenRowEnd);

        // The counts still travel, because the event is raised on every changed pass.
        Assert.Equal(1L, partition.RowCount);
        Assert.Equal(3L, partition.FilteredCount);
        Assert.DoesNotContain("RowsMoveFilterToPrimary", host.CallLog.Members);
    }

    /// <summary>
    /// <c>:L402-L405</c> - the second height call happens ONLY on a relocation that answered exactly
    /// <c>1</c>, so a failed move leaves the hidden range empty.
    /// </summary>
    [Fact]
    public void AFailedRelocationLeavesTheHiddenRangeEmpty()
    {
        (_, FakeDataWindowChild child, DropDownSearchModel service) = Focused();
        _ = child.AddRow("a");
        child.FilteredRowCount = 2;
        child.RowsMoveResult = -1;

        service.ApplyFilter(1L, null, "age>30", show: false);

        DropDownSearchFilterPartition partition = Assert.IsType<DropDownSearchFilterPartition>(
            service.Partition);

        Assert.True(partition.Relocated);
        Assert.False(partition.RelocationSucceeded);
        Assert.Equal(2L, partition.RelocatedRowCount);
        Assert.Equal(0L, partition.HiddenRowStart);
        Assert.Equal(0L, partition.HiddenRowEnd);

        // The visible range is still published, because :L399 precedes the move and is unconditional
        // beyond its own row-count guard.
        Assert.Equal(1L, partition.VisibleRowEnd);
    }

    /// <summary>
    /// <c>:L398</c> - the first height call is guarded by <c>nRowCnt &gt; 0</c>, so a filter that
    /// matched nothing publishes an EMPTY visible range for the deferred half to skip.
    /// </summary>
    [Fact]
    public void AFilterThatMatchedNothingPublishesAnEmptyVisibleRange()
    {
        (_, FakeDataWindowChild child, DropDownSearchModel service) = Focused();
        child.FilteredRowCount = 4;

        service.ApplyFilter(1L, null, "age>300", show: false);

        DropDownSearchFilterPartition partition = Assert.IsType<DropDownSearchFilterPartition>(
            service.Partition);

        Assert.Equal(0L, partition.RowCount);
        Assert.Equal(1L, partition.VisibleRowStart);
        Assert.Equal(0L, partition.VisibleRowEnd);
        Assert.True(partition.VisibleRowEnd < partition.VisibleRowStart);
    }

    /// <summary>
    /// <c>:L383-L384</c> - the composed filter and the USER'S half are stored separately, which is what
    /// makes <see cref="DropDownSearchModel.HasFilter"/> and the update path work.
    /// </summary>
    [Fact]
    public void TheUserHalfIsStoredSeparatelyFromTheComposedFilter()
    {
        (_, FakeDataWindowChild child, DropDownSearchModel service) = Focused(
            childFilter: "dept='X'");

        service.ApplyFilter(1L, null, "age>30", show: false);

        Assert.Equal("(dept='X') AND (age>30)", child.LastFilterSet);
        Assert.Equal("(dept='X') AND (age>30)", service.EditContext.Dddw.Filter);
        Assert.Equal("age>30", service.EditContext.Dddw.InputFilter);
    }

    // ==========================================================================================
    //  HasFilter                                                                  [:L347-L366]
    // ==========================================================================================

    /// <summary>
    /// <c>:L363-L365</c> - the three arms: an invalid context, a non-<c>dddw</c> style, and the user's
    /// own half.
    /// </summary>
    [Fact]
    public void HasFilterReportsTheUserHalfOnly()
    {
        (_, FakeDataWindowChild child, DropDownSearchModel service) = Focused(
            childFilter: "dept='X'");

        // A child that arrived carrying its own filter has a NON-EMPTY composed filter from the moment
        // the context was built [:L218], so testing that instead would report a user restriction where
        // there is none.
        Assert.Equal("dept='X'", service.EditContext.Dddw.Filter);
        Assert.False(service.HasFilter());

        service.ApplyFilter(1L, null, "age>30", show: false);
        Assert.True(service.HasFilter());

        // Restoring clears the user half.
        service.ApplyFilter(1L, null, string.Empty, show: false);
        Assert.False(service.HasFilter());
        Assert.Equal("dept='X'", child.LastFilterSet);
    }

    /// <summary>
    /// <c>:L363</c> - an invalid context answers false whatever else is true.
    /// </summary>
    [Fact]
    public void HasFilterAnswersFalseForAnInvalidContext()
    {
        (_, _, DropDownSearchModel service) = NewDddw();

        Assert.False(service.EditContext.Valid);
        Assert.False(service.HasFilter());
    }

    /// <summary>
    /// <c>:L364</c> - a <c>ddlb</c> context answers false because it has no drop-down to filter.
    /// </summary>
    [Fact]
    public void HasFilterAnswersFalseForADdlbContext()
    {
        (FakeDataWindowHost host, _, DropDownSearchModel service) = NewDddw(editStyle: "ddlb");
        host.SetDescribe(Column + ".Edit.AllowEdit", "yes");

        service.OnItemFocusChanged(1L, Dwo(host));

        Assert.True(service.EditContext.Valid);
        Assert.Equal("ddlb", service.EditContext.Style);
        Assert.False(service.HasFilter());
    }

    // ==========================================================================================
    //  OnEditChanged - THE AUTOCOMPLETE RATCHET AND THE TWO PREFIX SEARCHES        [:L84-L131]
    // ==========================================================================================

    /// <summary>
    /// <c>:L88</c> - an invalid context abandons the pass, so nothing is filtered and no completion is
    /// published.
    /// </summary>
    [Fact]
    public void AnInvalidContextAbandonsTheEditChangedPass()
    {
        (FakeDataWindowHost host, FakeDataWindowChild child, DropDownSearchModel service) = NewDddw();

        Assert.False(service.EditContext.Valid);

        service.OnEditChanged(1L, Dwo(host), "ab");

        Assert.Null(service.Completion);
        Assert.Null(child.LastFilterSet);
    }

    /// <summary>
    /// <c>:L90</c> - a null <c>data</c> is coerced to the empty string, which the by-value parameter then
    /// carries through every later use.
    /// </summary>
    /// <remarks>
    /// LOAD BEARING RATHER THAN DEFENSIVE: the host forwards this argument from a raw
    /// <c>pbm_dwnchanging</c> and calls the hook as <c>OnEditChanged(row, dwo, data!)</c>, so null
    /// genuinely arrives.
    /// </remarks>
    [Fact]
    public void ANullEditTextIsCoercedToTheEmptyString()
    {
        (FakeDataWindowHost host, _, DropDownSearchModel service) = Focused();

        List<string> seen = [];
        host.DdsGetFilterHandler = (row, dwo, data, filter) =>
        {
            seen.Add(data);
            return filter;
        };

        service.OnEditChanged(1L, Dwo(host), null!);

        // The builder was reached with "" - not skipped, and not handed a null.
        Assert.Equal([string.Empty], seen);
        Assert.Null(service.Completion);
    }

    /// <summary>
    /// <c>:L104-L112</c>, <c>:L127-L129</c> - the <c>dddw</c> prefix search takes the FIRST matching row
    /// in row order and publishes the completion as data.
    /// </summary>
    [Fact]
    public void TheDddwPrefixSearchPublishesTheFirstMatchAsData()
    {
        (FakeDataWindowHost host, FakeDataWindowChild child, DropDownSearchModel service) = Focused();
        _ = child.AddRow("Anderson", "1");
        _ = child.AddRow("ABBOTT", "2");
        _ = child.AddRow("Abrams", "3");

        service.OnEditChanged(1L, Dwo(host), "ab");

        DropDownSearchCompletion completion = Assert.IsType<DropDownSearchCompletion>(service.Completion);

        // ROW ORDER, NOT BEST MATCH: row 2 is the first whose lower-cased prefix equals the input, and
        // the seeded ascending sort at :L220 is what makes that order predictable.
        Assert.Equal("ABBOTT", completion.Text);

        // :L129  ONE-BASED start of the completed tail, and the FULL length of the match.
        Assert.Equal(3L, completion.SelectionStart);
        Assert.Equal(6L, completion.SelectionLength);
    }

    /// <summary>
    /// <c>:L110</c> - the prefix comparison is case-insensitive in BOTH directions.
    /// </summary>
    [Theory]
    [InlineData("ab")]
    [InlineData("AB")]
    [InlineData("aB")]
    public void TheDddwPrefixSearchIgnoresCase(string typed)
    {
        (FakeDataWindowHost host, FakeDataWindowChild child, DropDownSearchModel service) = Focused();
        _ = child.AddRow("ABBOTT", "2");

        service.OnEditChanged(1L, Dwo(host), typed);

        Assert.Equal("ABBOTT", Assert.IsType<DropDownSearchCompletion>(service.Completion).Text);
    }

    /// <summary>
    /// <c>:L127</c> - no prefix match publishes NO completion, which for the deferred half means apply
    /// nothing.
    /// </summary>
    [Fact]
    public void NoPrefixMatchPublishesNoCompletion()
    {
        (FakeDataWindowHost host, FakeDataWindowChild child, DropDownSearchModel service) = Focused();
        _ = child.AddRow("Zeta", "1");

        service.OnEditChanged(1L, Dwo(host), "ab");

        Assert.Null(service.Completion);
    }

    /// <summary>
    /// <c>:L98-L102</c> - THE AUTOCOMPLETE RATCHET. Completion advances only on GROWING input, so
    /// backspacing suppresses it - and <c>lastData</c> is updated on BOTH branches.
    /// </summary>
    [Fact]
    public void BackspacingSuppressesCompletionButStillRatchetsLastData()
    {
        (FakeDataWindowHost host, FakeDataWindowChild child, DropDownSearchModel service) = Focused();
        _ = child.AddRow("ABBOTT", "2");
        IDataWindowObject dwo = Dwo(host);

        // Growing: "ab" is longer than the initial "" so the search runs.
        service.OnEditChanged(1L, dwo, "ab");
        Assert.NotNull(service.Completion);
        Assert.Equal("ab", service.EditContext.LastData);

        // Backspace to "a" - SHORTER, so the search is suppressed even though "a" would match.
        service.OnEditChanged(1L, dwo, "a");
        Assert.Null(service.Completion);

        // :L99  but the field is still updated, so the ratchet moves DOWN
        Assert.Equal("a", service.EditContext.LastData);

        // Which means the very next "ab" is growing again and the search resumes.
        service.OnEditChanged(1L, dwo, "ab");
        Assert.NotNull(service.Completion);
    }

    /// <summary>
    /// <c>:L98</c> - equal length is NOT growing, so a same-length replacement is suppressed too.
    /// </summary>
    [Fact]
    public void ASameLengthReplacementIsSuppressed()
    {
        (FakeDataWindowHost host, FakeDataWindowChild child, DropDownSearchModel service) = Focused();
        _ = child.AddRow("ABBOTT", "2");
        _ = child.AddRow("ZZ", "3");
        IDataWindowObject dwo = Dwo(host);

        service.OnEditChanged(1L, dwo, "ab");
        Assert.NotNull(service.Completion);

        service.OnEditChanged(1L, dwo, "zz");

        // The guard is `<=`, so two characters after two characters never searches.
        Assert.Null(service.Completion);
        Assert.Equal("zz", service.EditContext.LastData);
    }

    /// <summary>
    /// <c>:L94-L96</c> then <c>:L104-L125</c> - the filter is applied BEFORE the prefix scan, so a
    /// completion can only be offered from rows the filter kept.
    /// </summary>
    [Fact]
    public void TheFilterIsAppliedBeforeThePrefixSearch()
    {
        (FakeDataWindowHost host, FakeDataWindowChild child, DropDownSearchModel service) = Focused();
        _ = child.AddRow("ABBOTT", "2");
        child.RecordsReads = true;

        service.OnEditChanged(1L, Dwo(host), "ab");

        IReadOnlyList<string> members = host.CallLog.Members;
        int filterIndex = members.ToList().IndexOf("Filter");
        int scanIndex = members.ToList().IndexOf("GetItemString");

        Assert.True(filterIndex >= 0, "the composed filter was never applied");

        // The scan reads the child through the value getters, which are reads and therefore recorded only
        // because RecordsReads is on. Whatever their index, it must be after the apply.
        if (scanIndex >= 0)
        {
            Assert.True(scanIndex > filterIndex, "the prefix scan ran before the filter was applied");
        }
    }

    /// <summary>
    /// <c>:L114-L124</c> - the <c>ddlb</c> walk starts at index ONE, stops on the first empty value, and
    /// splits the matched item at the TAB.
    /// </summary>
    [Fact]
    public void TheDdlbPrefixSearchWalksFromOneAndSplitsAtTheTab()
    {
        (FakeDataWindowHost host, _, DropDownSearchModel service) = NewDddw(editStyle: "ddlb");
        host.SetDescribe(Column + ".Edit.AllowEdit", "yes");
        host.SetValueList(Column, "Zeta\t9", "ABBOTT\t2");

        service.OnItemFocusChanged(1L, Dwo(host));
        service.OnEditChanged(1L, Dwo(host), "ab");

        DropDownSearchCompletion completion = Assert.IsType<DropDownSearchCompletion>(service.Completion);

        // The DISPLAY half only - Left(sVal, Pos(sVal, TAB) - 1).
        Assert.Equal("ABBOTT", completion.Text);
        Assert.Equal(3L, completion.SelectionStart);
        Assert.Equal(6L, completion.SelectionLength);
    }

    /// <summary>
    /// <c>:L123</c> - a <c>ddlb</c> item carrying NO TAB yields an EMPTY display value, and the match
    /// still counts as a match.
    /// </summary>
    /// <remarks>
    /// PRESERVED LEGACY BEHAVIOUR, NOT A FAULT: <c>Pos</c> answers <c>0</c>, so <c>Left</c> receives
    /// <c>-1</c> and PowerScript answers the empty string - while <c>:L127</c> still sees
    /// <c>bMatched</c> true. A port that guarded the missing tab would publish no completion where the
    /// oracle publishes an empty one.
    /// </remarks>
    [Fact]
    public void ADdlbItemWithNoTabYieldsAnEmptyCompletionThatStillCounts()
    {
        (FakeDataWindowHost host, _, DropDownSearchModel service) = NewDddw(editStyle: "ddlb");
        host.SetDescribe(Column + ".Edit.AllowEdit", "yes");
        host.SetValueList(Column, "ABBOTT");

        service.OnItemFocusChanged(1L, Dwo(host));
        service.OnEditChanged(1L, Dwo(host), "ab");

        DropDownSearchCompletion completion = Assert.IsType<DropDownSearchCompletion>(service.Completion);

        Assert.Equal(string.Empty, completion.Text);
        Assert.Equal(3L, completion.SelectionStart);
        Assert.Equal(0L, completion.SelectionLength);
    }

    /// <summary>
    /// <c>:L118</c> - <c>if sVal = "" then exit</c>. The <c>ddlb</c> walk EXHAUSTS the value list and
    /// leaves without a match, so NO completion is published.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the terminating arm of the <c>do ... loop until(bMatched)</c> at <c>:L115-L121</c>, and it
    /// is the ONLY way the walk can stop when nothing matches: PowerBuilder reports "past the last item"
    /// by answering the empty string from <c>GetValue</c>, so the empty answer is the list's end sentinel
    /// rather than an item whose text happens to be blank. Without this arm the loop would not terminate.
    /// </para>
    /// <para>
    /// The assertion that matters is the ABSENCE of a completion: <c>:L127</c> guards the publication on
    /// <c>bMatched</c>, so an exhausted walk must leave <see cref="DropDownSearchModel.Completion"/> null
    /// even though the pass ran to the end and updated <c>lastData</c>. A port that published a
    /// last-probed value here, or an empty completion, would offer the user a completion the oracle never
    /// offers.
    /// </para>
    /// </remarks>
    [Fact]
    public void ADdlbWalkThatExhaustsTheValueListPublishesNoCompletion()
    {
        (FakeDataWindowHost host, _, DropDownSearchModel service) = NewDddw(editStyle: "ddlb");
        host.SetDescribe(Column + ".Edit.AllowEdit", "yes");

        // Two items, NEITHER of which starts with the typed text, so the walk runs off the end.
        host.SetValueList(Column, "Zeta\t9", "Yankee\t8");

        service.OnItemFocusChanged(1L, Dwo(host));
        service.OnEditChanged(1L, Dwo(host), "ab");

        // :L127  if bMatched then ...   - it is false, so nothing is published.
        Assert.Null(service.Completion);

        // :L102  the pass still recorded the input, which is what gates the next keystroke.
        Assert.Equal("ab", service.EditContext.LastData);
    }


    /// <summary>
    /// <c>:L94</c> - a <c>ddlb</c> context composes and applies NO filter, because there is no child
    /// DataWindow to filter.
    /// </summary>
    [Fact]
    public void ADdlbContextAppliesNoFilter()
    {
        (FakeDataWindowHost host, FakeDataWindowChild child, DropDownSearchModel service) = NewDddw(
            editStyle: "ddlb");
        host.SetDescribe(Column + ".Edit.AllowEdit", "yes");
        host.SetValueList(Column, "ABBOTT\t2");

        service.OnItemFocusChanged(1L, Dwo(host));
        host.CallLog.Clear();

        service.OnEditChanged(1L, Dwo(host), "ab");

        Assert.Null(child.LastFilterSet);
        Assert.DoesNotContain("SetFilter", host.CallLog.Members);
    }

    /// <summary>
    /// The published completion belongs to ONE pass: it is cleared on entry, before any guard.
    /// </summary>
    [Fact]
    public void TheCompletionIsClearedAtTheStartOfEveryPass()
    {
        (FakeDataWindowHost host, FakeDataWindowChild child, DropDownSearchModel service) = Focused();
        _ = child.AddRow("ABBOTT", "2");
        IDataWindowObject dwo = Dwo(host);

        service.OnEditChanged(1L, dwo, "ab");
        Assert.NotNull(service.Completion);

        // A suppressed pass must not leave the previous completion standing, because the oracle applied
        // nothing to the control on it.
        service.OnEditChanged(1L, dwo, "a");
        Assert.Null(service.Completion);
    }

    // ==========================================================================================
    //  UpdateDddwFilter - THE FOUR GUARDS AND DEFECT 5                        [:L308, L419, L458]
    // ==========================================================================================

    /// <summary>
    /// <c>:L438</c> - the focus guard. With focus elsewhere the call answers <c>OK</c> and does nothing.
    /// </summary>
    /// <remarks>
    /// The focus fact arrives through the host contract - the port of PowerScript's <c>GetFocus</c>
    /// SYSTEM function - and never from an OS call.
    /// </remarks>
    [Fact]
    public void TheFocusGuardAnswersOkAndDoesNothing()
    {
        (FakeDataWindowHost host, FakeDataWindowChild child, DropDownSearchModel service) = Focused();
        host.FocusedObject = new object();

        Assert.Equal(RetCode.OK, service.UpdateDddwFilter(Column, "dept='Y'", true));
        Assert.Null(child.LastFilterSet);
        Assert.Equal(string.Empty, service.EditContext.Dddw.OrgFilter);
    }

    /// <summary>
    /// <c>:L439</c> - an invalid context answers <c>OK</c>.
    /// </summary>
    [Fact]
    public void AnInvalidContextAnswersOk()
    {
        (FakeDataWindowHost host, _, DropDownSearchModel service) = NewDddw();
        host.FocusedObject = host;

        Assert.False(service.EditContext.Valid);
        Assert.Equal(RetCode.OK, service.UpdateDddwFilter(Column, "dept='Y'", true));
    }

    /// <summary>
    /// <c>:L440</c> - a different column answers <c>OK</c>, and the comparison is ASYMMETRIC: the
    /// argument is lower-cased while the stored name is used as captured.
    /// </summary>
    [Theory]
    [InlineData(Column, true)]
    [InlineData("EMP", true)]
    [InlineData("Emp", true)]
    [InlineData(OtherColumn, false)]
    public void TheColumnComparisonLowerCasesTheArgumentOnly(string colName, bool matches)
    {
        (FakeDataWindowHost host, _, DropDownSearchModel service) = Focused();
        host.FocusedObject = host;

        Assert.Equal(RetCode.OK, service.UpdateDddwFilter(colName, "dept='Y'", false));

        // Only a matching column reaches the assignment.
        Assert.Equal(matches ? "dept='Y'" : string.Empty, service.EditContext.Dddw.OrgFilter);
    }

    /// <summary>
    /// <c>:L438-L441</c> - DEFECT 5. Three guards answer <c>RetCode.OK</c> and the fourth answers
    /// <c>RetCode.FAILED</c>, for no discernible reason.
    /// </summary>
    /// <remarks>
    /// THIS TEST MUST FAIL IF THE FOUR ARE HARMONISED IN EITHER DIRECTION. All four describe the same
    /// situation - the call does not apply to the current context - and the divergence is observable
    /// through the public return value. It compounds with the tri-state return algebra: a caller using
    /// <c>Predicates.IsSucceeded</c> sees silent success for a wrong-named column and a hard failure for
    /// a wrong-styled one.
    /// </remarks>
    [Fact]
    public void TheGuardReturnCodesDivergeAndTheDivergenceIsPreserved()
    {
        // Guard 1 - focus elsewhere: OK
        (FakeDataWindowHost focusHost, _, DropDownSearchModel focusService) = Focused();
        focusHost.FocusedObject = new object();
        Assert.Equal(RetCode.OK, focusService.UpdateDddwFilter(Column, "dept='Y'", false));

        // Guard 2 - invalid context: OK
        (FakeDataWindowHost invalidHost, _, DropDownSearchModel invalidService) = NewDddw();
        invalidHost.FocusedObject = invalidHost;
        Assert.Equal(RetCode.OK, invalidService.UpdateDddwFilter(Column, "dept='Y'", false));

        // Guard 3 - a different column: OK
        (FakeDataWindowHost nameHost, _, DropDownSearchModel nameService) = Focused();
        nameHost.FocusedObject = nameHost;
        Assert.Equal(RetCode.OK, nameService.UpdateDddwFilter(OtherColumn, "dept='Y'", false));

        // Guard 4 - a non-dddw style: FAILED. THE ODD ONE OUT.
        (FakeDataWindowHost styleHost, _, DropDownSearchModel styleService) = NewDddw(editStyle: "ddlb");
        styleHost.SetDescribe(Column + ".Edit.AllowEdit", "yes");
        styleHost.FocusedObject = styleHost;
        styleService.OnItemFocusChanged(1L, Dwo(styleHost));

        Assert.Equal(RetCode.FAILED, styleService.UpdateDddwFilter(Column, "dept='Y'", false));

        // And the two codes really are distinguishable through the ported algebra.
        Assert.True(Predicates.IsSucceeded(RetCode.OK));
        Assert.True(Predicates.IsFailed(RetCode.FAILED));
    }

    /// <summary>
    /// <c>:L443-L446</c> - a null filter re-reads the child's own, normalising the <c>"?"</c> sentinel to
    /// the empty string.
    /// </summary>
    [Theory]
    [InlineData("dept='Z'", "dept='Z'")]
    [InlineData("?", "")]
    public void ANullFilterRereadsTheChildAndNormalisesTheSentinel(string childAnswer, string expected)
    {
        (FakeDataWindowHost host, FakeDataWindowChild child, DropDownSearchModel service) = Focused();
        host.FocusedObject = host;
        child.SetDescribe("DataWindow.Table.Filter", childAnswer);

        Assert.Equal(RetCode.OK, service.UpdateDddwFilter(Column, null, false));
        Assert.Equal(expected, service.EditContext.Dddw.OrgFilter);
    }

    /// <summary>
    /// <c>:L308-L310</c> - the one-argument overload passes NULL, which is the whole point of it: the
    /// null selects the re-read branch, and the empty string would not.
    /// </summary>
    [Fact]
    public void TheOneArgumentOverloadPassesNullAndReachesTheRereadBranch()
    {
        (FakeDataWindowHost host, FakeDataWindowChild child, DropDownSearchModel service) = Focused();
        host.FocusedObject = host;
        child.SetDescribe("DataWindow.Table.Filter", "dept='Z'");

        Assert.Equal(RetCode.OK, service.UpdateDddwFilter(Column));

        // Had it passed "" the original filter would have been CLEARED instead of re-read.
        Assert.Equal("dept='Z'", service.EditContext.Dddw.OrgFilter);
    }

    /// <summary>
    /// <c>:L458</c> - the two-argument overload delegates with <c>doFilter</c> true, so it re-filters
    /// immediately.
    /// </summary>
    [Fact]
    public void TheTwoArgumentOverloadRefiltersImmediately()
    {
        (FakeDataWindowHost host, FakeDataWindowChild child, DropDownSearchModel service) = Focused();
        host.FocusedObject = host;

        // Give the user a restriction first, so the re-filter has something to re-apply.
        service.ApplyFilter(1L, null, "age>30", show: false);
        child.LastFilterSet = null;

        Assert.Equal(RetCode.OK, service.UpdateDddwFilter(Column, "dept='Z'"));

        // :L451  THE USER'S OWN HALF IS RE-APPLIED on top of the new original filter.
        Assert.Equal("(dept='Z') AND (age>30)", child.LastFilterSet);
    }

    /// <summary>
    /// <c>:L447-L453</c> - <c>doFilter</c> false updates the original filter WITHOUT re-filtering, and an
    /// UNCHANGED original filter does nothing at all.
    /// </summary>
    [Fact]
    public void DoFilterFalseUpdatesWithoutRefilteringAndAnUnchangedFilterDoesNothing()
    {
        (FakeDataWindowHost host, FakeDataWindowChild child, DropDownSearchModel service) = Focused();
        host.FocusedObject = host;
        service.ApplyFilter(1L, null, "age>30", show: false);
        child.LastFilterSet = null;

        Assert.Equal(RetCode.OK, service.UpdateDddwFilter(Column, "dept='Z'", false));
        Assert.Equal("dept='Z'", service.EditContext.Dddw.OrgFilter);
        Assert.Null(child.LastFilterSet);

        // :L447  the same value again changes nothing, so nothing is re-applied even with doFilter true.
        host.CallLog.Clear();
        Assert.Equal(RetCode.OK, service.UpdateDddwFilter(Column, "dept='Z'", true));
        Assert.Null(child.LastFilterSet);
    }

    // ==========================================================================================
    //  InitEditContextInfo - THE STATE MACHINE                                    [:L151-L229]
    // ==========================================================================================

    /// <summary>
    /// <c>:L198-L199</c> - a DISPLAY-ONLY column is inert: the context keeps the row, object and name so
    /// a later event can tell WHICH cell was inert, but stays invalid with an EMPTY style.
    /// </summary>
    [Fact]
    public void ADisplayOnlyColumnLeavesTheContextInertButNamed()
    {
        (FakeDataWindowHost host, _, DropDownSearchModel service) = NewDddw(displayOnly: "yes");

        service.OnItemFocusChanged(3L, Dwo(host));

        Assert.False(service.EditContext.Valid);
        Assert.Equal(3L, service.EditContext.Row);
        Assert.Equal(Column, service.EditContext.Name);

        // The style is never even read, so it stays at its reset value.
        Assert.Equal(string.Empty, service.EditContext.Style);
    }

    /// <summary>
    /// <c>:L201</c> - the INVALID-EXPRESSION sentinel is LEFT IN THE FIELD rather than normalised away,
    /// so a later style comparison sees <c>"!"</c> and not <c>""</c>.
    /// </summary>
    [Fact]
    public void AnUnreportableEditStyleLeavesTheSentinelInTheField()
    {
        (FakeDataWindowHost host, _, DropDownSearchModel service) = NewDddw(editStyle: "!");

        service.OnItemFocusChanged(1L, Dwo(host));

        Assert.False(service.EditContext.Valid);
        Assert.Equal("!", service.EditContext.Style);
    }

    /// <summary>
    /// <c>:L205-L213</c> - each precondition of the <c>dddw</c> arm leaves the context invalid on its own.
    /// </summary>
    [Theory]
    [InlineData("no", DispCol, DataCol, "char(50)")]        // :L206  a non-editable drop-down
    [InlineData("yes", "!", DataCol, "char(50)")]           // :L208  no display column
    [InlineData("yes", DispCol, "!", "char(50)")]           // :L210  no data column
    [InlineData("yes", DispCol, DataCol, "decimal(2)")]     // :L213  a non-text display column
    public void EachDddwPreconditionLeavesTheContextInvalid(
        string allowEdit,
        string dispCol,
        string dataCol,
        string dispColType)
    {
        (FakeDataWindowHost host, _, DropDownSearchModel service) = NewDddw(
            dataCol: dataCol,
            dispCol: dispCol == "!" ? DispCol : dispCol,
            dispColType: dispColType,
            allowEdit: allowEdit);

        if (dispCol == "!")
        {
            host.SetDescribe(Column + ".DDDW.DisplayColumn", "!");
        }

        if (dataCol == "!")
        {
            host.SetDescribe(Column + ".DDDW.DataColumn", "!");
        }

        service.OnItemFocusChanged(1L, Dwo(host));

        Assert.False(service.EditContext.Valid);
    }

    /// <summary>
    /// <c>:L211</c> - a child that cannot be obtained leaves the context invalid.
    /// </summary>
    [Fact]
    public void AMissingChildLeavesTheContextInvalid()
    {
        (FakeDataWindowHost host, _, DropDownSearchModel service) = NewDddw();
        host.SetChild(Column, null);

        service.OnItemFocusChanged(1L, Dwo(host));

        Assert.False(service.EditContext.Valid);
        Assert.Null(service.EditContext.Dddw.Child);
    }

    /// <summary>
    /// <c>:L215-L221</c> - a successful build captures the height, normalises the filter sentinel, seeds
    /// the change detector and marks the context valid.
    /// </summary>
    [Fact]
    public void ASuccessfulBuildCapturesEveryDerivedField()
    {
        (_, _, DropDownSearchModel service) = Focused(childFilter: "dept='X'");

        Assert.True(service.EditContext.Valid);
        Assert.Equal("dddw", service.EditContext.Style);
        Assert.Equal(DispCol, service.EditContext.Dddw.DispColName);
        Assert.Equal(DataCol, service.EditContext.Dddw.DataColName);
        Assert.Equal(76L, service.EditContext.Dddw.RowHeight);
        Assert.Equal("dept='X'", service.EditContext.Dddw.OrgFilter);

        // :L218  the change detector is SEEDED from the original filter.
        Assert.Equal("dept='X'", service.EditContext.Dddw.Filter);

        // THE WINDOW HANDLE IS NEVER POPULATED - :L214 is deferred, so it keeps its zero.
        Assert.Equal(0u, service.EditContext.Dddw.Hwnd);
    }

    /// <summary>
    /// <c>:L216-L217</c> - the <c>"?"</c> sentinel becomes the empty string, so "no original filter"
    /// never travels as the literal text <c>?</c>.
    /// </summary>
    [Fact]
    public void TheUndeterminableFilterSentinelBecomesEmpty()
    {
        (_, _, DropDownSearchModel service) = Focused();

        Assert.Equal(string.Empty, service.EditContext.Dddw.OrgFilter);
        Assert.Equal(string.Empty, service.EditContext.Dddw.Filter);
    }

    /// <summary>
    /// <c>:L219-L221</c> - a child with NO sort of its own is seeded ascending by display column; one
    /// that already has a sort keeps it.
    /// </summary>
    [Theory]
    [InlineData("?", "empname ASC")]
    [InlineData("age A", null)]
    public void AChildWithNoSortIsSeededAscendingByDisplayColumn(string childSort, string? expected)
    {
        (FakeDataWindowHost host, FakeDataWindowChild child, DropDownSearchModel service) = NewDddw(
            childSort: childSort);

        service.OnItemFocusChanged(1L, Dwo(host));

        Assert.Equal(expected, child.LastSortSet);
    }

    /// <summary>
    /// <c>:L154-L164</c> - the reset path restores the child's original filter, then clears the context
    /// while STORING the row and object it was given.
    /// </summary>
    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void ANonPositiveRowTakesTheResetPath(long row)
    {
        (FakeDataWindowHost host, FakeDataWindowChild child, DropDownSearchModel service) = Focused(
            childFilter: "dept='X'");
        service.ApplyFilter(1L, Dwo(host), "age>30", show: false);
        Assert.Equal("(dept='X') AND (age>30)", child.LastFilterSet);

        service.OnItemFocusChanged(row, Dwo(host));

        // :L158  the ORIGINAL filter is restored, not cleared.
        Assert.Equal("dept='X'", child.LastFilterSet);
        Assert.False(service.EditContext.Valid);
        Assert.Equal(string.Empty, service.EditContext.LastData);
        Assert.Equal(row, service.EditContext.Row);
    }

    /// <summary>
    /// <c>:L154</c> - an invalid object takes the reset path just as a non-positive row does, which is
    /// exactly what <c>onlosefocus</c> relies on.
    /// </summary>
    [Fact]
    public void LoseFocusResetsThroughTheExplicitNullIdiom()
    {
        (FakeDataWindowHost host, FakeDataWindowChild child, DropDownSearchModel service) = Focused(
            childFilter: "dept='X'");
        service.ApplyFilter(1L, Dwo(host), "age>30", show: false);

        service.OnLoseFocus();

        Assert.Equal("dept='X'", child.LastFilterSet);
        Assert.False(service.EditContext.Valid);
        Assert.Equal(0L, service.EditContext.Row);
        Assert.Null(service.EditContext.Dwo);
    }

    /// <summary>
    /// <c>:L172-L178</c> - THE FAST PATH. The same column with a DIFFERENT row re-seeds the filter
    /// without rebuilding any of the described state.
    /// </summary>
    [Fact]
    public void TheFastPathReSeedsWithoutRebuildingDescribedState()
    {
        (FakeDataWindowHost host, _, DropDownSearchModel service) = Focused();
        IDataWindowObject dwo = Dwo(host);
        service.OnEditChanged(1L, dwo, "ab");
        Assert.Equal("ab", service.EditContext.LastData);

        // Poison every Describe answer the full rebuild would read. If the fast path were skipped the
        // context would come back invalid with an empty display column.
        host.SetDescribe(Column + ".Edit.Style", "edit");
        host.SetDescribe(Column + ".DDDW.DisplayColumn", "!");

        service.OnItemFocusChanged(2L, dwo);

        Assert.True(service.EditContext.Valid);
        Assert.Equal("dddw", service.EditContext.Style);
        Assert.Equal(DispCol, service.EditContext.Dddw.DispColName);

        // :L174-L175  the row moves and the ratchet is reset, so the next keystroke can complete again.
        Assert.Equal(2L, service.EditContext.Row);
        Assert.Equal(string.Empty, service.EditContext.LastData);
    }

    /// <summary>
    /// <c>:L173</c> - the fast path requires all three conditions, so a DIFFERENT COLUMN forces the full
    /// rebuild.
    /// </summary>
    [Fact]
    public void ADifferentColumnForcesTheFullRebuild()
    {
        (FakeDataWindowHost host, _, DropDownSearchModel service) = Focused();

        // The other column has no edit style taught at all, so the rebuild must leave it invalid.
        service.OnItemFocusChanged(1L, Dwo(host, OtherColumn));

        Assert.False(service.EditContext.Valid);
        Assert.Equal(OtherColumn, service.EditContext.Name);
        Assert.Equal(string.Empty, service.EditContext.Dddw.DispColName);
        Assert.Equal(0L, service.EditContext.Dddw.RowHeight);
    }

    /// <summary>
    /// <c>:L183-L196</c> - the thirteen-field reset clears every derived field before the rebuild reads
    /// anything.
    /// </summary>
    [Fact]
    public void TheThirteenFieldResetClearsEveryDerivedField()
    {
        (FakeDataWindowHost host, _, DropDownSearchModel service) = Focused(childFilter: "dept='X'");
        service.ApplyFilter(1L, Dwo(host), "age>30", show: false);

        // A different column with nothing taught, so the rebuild returns at the display-only probe and
        // the assertions see the reset state itself rather than a rebuilt one.
        host.SetDescribe(OtherColumn + ".Edit.DisplayOnly", "yes");
        service.OnItemFocusChanged(5L, Dwo(host, OtherColumn));

        EditContextData context = service.EditContext;
        Assert.False(context.Valid);
        Assert.Equal(string.Empty, context.LastData);
        Assert.Equal(5L, context.Row);
        Assert.NotNull(context.Dwo);
        Assert.Equal(OtherColumn, context.Name);
        Assert.Equal(string.Empty, context.Style);
        Assert.Null(context.Dddw.Child);
        Assert.Equal(string.Empty, context.Dddw.DataColName);
        Assert.Equal(string.Empty, context.Dddw.DispColName);
        Assert.Equal(string.Empty, context.Dddw.OrgFilter);
        Assert.Equal(string.Empty, context.Dddw.Filter);
        Assert.Equal(string.Empty, context.Dddw.InputFilter);
        Assert.Equal(0u, context.Dddw.Hwnd);
        Assert.Equal(0L, context.Dddw.RowHeight);
    }

    /// <summary>
    /// <c>:L225-L227</c> - a <c>ddlb</c> column needs nothing but editability, and every drop-down field
    /// stays at its reset value.
    /// </summary>
    [Theory]
    [InlineData("yes", true)]
    [InlineData("no", false)]
    public void ADdlbColumnNeedsOnlyEditability(string allowEdit, bool expectedValid)
    {
        (FakeDataWindowHost host, _, DropDownSearchModel service) = NewDddw(editStyle: "ddlb");
        host.SetDescribe(Column + ".Edit.AllowEdit", allowEdit);

        service.OnItemFocusChanged(1L, Dwo(host));

        Assert.Equal(expectedValid, service.EditContext.Valid);

        // LOAD BEARING: the two empty column names compare EQUAL, which short-circuits both the data
        // clause at :L327 and the first auto-determination arm at :L261.
        Assert.Equal(service.EditContext.Dddw.DispColName, service.EditContext.Dddw.DataColName);
    }

    /// <summary>
    /// <c>:L247</c> - context initialisation composes with EMPTY data, which raises the semantic event and
    /// is how an application supplies an initial filter.
    /// </summary>
    [Fact]
    public void ContextInitialisationRaisesTheGetFilterEventWithEmptyData()
    {
        (FakeDataWindowHost host, FakeDataWindowChild child, DropDownSearchModel service) = NewDddw();

        List<string> seen = [];
        host.DdsGetFilterHandler = (row, dwo, data, filter) =>
        {
            seen.Add(data);
            return "dept='SEEDED'";
        };

        service.OnItemFocusChanged(1L, Dwo(host));

        Assert.Equal([string.Empty], seen);

        // And the supplied filter really is applied, which is the point of the extension point.
        Assert.Equal("dept='SEEDED'", child.LastFilterSet);
    }

    /// <summary>
    /// <c>:L233-L234</c> - the filter-seeding routine's own two guards. Both are DEFENSIVE: no ported
    /// path can reach either, because the only caller has already established both conditions.
    /// </summary>
    /// <remarks>
    /// ASSERTED ANYWAY, AND DELIBERATELY. They are two statements of the oracle and a port that dropped
    /// them because "the caller already checks" would be relying on a caller-side invariant the oracle
    /// does not rely on - so the guards are reproduced, and these two cases prove they behave as guards
    /// rather than as dead code that happens to compile.
    /// </remarks>
    [Fact]
    public void TheFilterSeedingGuardsAbandonAnUnsuitableContext()
    {
        // :L233  an invalid context
        (FakeDataWindowHost invalidHost, FakeDataWindowChild invalidChild, DropDownSearchModel invalidService) =
            NewDddw();
        Assert.False(invalidService.EditContext.Valid);

        invalidService.InitCtxDddwFilter(1L, Dwo(invalidHost));

        Assert.Null(invalidChild.LastFilterSet);
        Assert.Null(invalidService.Partition);

        // :L234  a valid context that is not a drop-down
        (FakeDataWindowHost ddlbHost, FakeDataWindowChild ddlbChild, DropDownSearchModel ddlbService) =
            NewDddw(editStyle: "ddlb");
        ddlbHost.SetDescribe(Column + ".Edit.AllowEdit", "yes");
        ddlbService.OnItemFocusChanged(1L, Dwo(ddlbHost));
        Assert.True(ddlbService.EditContext.Valid);

        ddlbService.InitCtxDddwFilter(1L, Dwo(ddlbHost));

        Assert.Null(ddlbChild.LastFilterSet);
        Assert.Null(ddlbService.Partition);
    }

    // ==========================================================================================
    //  OnEnable - THE THREE SUBSCRIPTIONS AND DEFECT 3                            [:L506-L515]
    // ==========================================================================================

    /// <summary>
    /// <c>:L508-L510</c> - enabling subscribes exactly three topics, each reachable through the broker.
    /// </summary>
    [Fact]
    public void EnablingSubscribesTheThreeLiveTopics()
    {
        (FakeDataWindowHost host, _, DropDownSearchModel service) = NewDddw();

        Assert.Equal(RetCode.OK, service.SetEnabled(true));
        Assert.True(service.Enabled);

        // itemfocuschanged builds the context.
        _ = host.Eventful.Trigger("itemfocuschanged", 1L, Dwo(host));
        Assert.True(service.EditContext.Valid);

        // losefocus resets it.
        _ = host.Eventful.Trigger("losefocus");
        Assert.False(service.EditContext.Valid);

        // getfocus rebuilds it from the host's own current row and column.
        host.CurrentRow = 1L;
        host.CurrentColumnName = Column;
        _ = host.Eventful.Trigger("getfocus");
        Assert.True(service.EditContext.Valid);
    }

    /// <summary>
    /// <c>:L507</c> - DEFECT 3. The edit-changed subscription is COMMENTED OUT, so the broker topic must
    /// NOT reach <c>OnEditChanged</c>.
    /// </summary>
    /// <remarks>
    /// THIS TEST MUST FAIL IF THE SUBSCRIPTION IS REVIVED. Reviving it would double-dispatch every
    /// keystroke, once through the topic and once through the host's direct call at
    /// <c>se_cst_dw.sru:L169-L171</c> - and the broker's own argument-injection hook excludes this
    /// service anyway [<c>se_cst_dw.sru:L602</c>], so the revived path would not even receive the same
    /// arguments.
    /// </remarks>
    [Fact]
    public void TheEditChangedTopicIsNotSubscribed()
    {
        (FakeDataWindowHost host, FakeDataWindowChild child, DropDownSearchModel service) = NewDddw();
        _ = service.SetEnabled(true);
        _ = host.Eventful.Trigger("itemfocuschanged", 1L, Dwo(host));
        _ = child.AddRow("ABBOTT", "2");
        host.CallLog.Clear();
        child.LastFilterSet = null;

        // The oracle's topic name, lexical ordering prefix and all [se_cst_dw.sru:L57].
        _ = host.Eventful.Trigger("1-editchanged", 1L, Dwo(host), "ab");

        Assert.Null(service.Completion);
        Assert.Null(child.LastFilterSet);

        // The direct call is the only route, and it works.
        service.OnEditChanged(1L, Dwo(host), "ab");
        Assert.NotNull(service.Completion);
    }

    /// <summary>
    /// <c>:L512</c> - disabling unsubscribes BY TARGET, so all three topics go at once.
    /// </summary>
    [Fact]
    public void DisablingUnsubscribesEveryTopicAtOnce()
    {
        (FakeDataWindowHost host, _, DropDownSearchModel service) = NewDddw();
        _ = service.SetEnabled(true);
        _ = host.Eventful.Trigger("itemfocuschanged", 1L, Dwo(host));
        Assert.True(service.EditContext.Valid);

        Assert.Equal(RetCode.OK, service.SetEnabled(false));
        Assert.False(service.Enabled);

        // The reset the disable itself did not perform must not arrive through the broker either.
        _ = host.Eventful.Trigger("losefocus");
        Assert.True(service.EditContext.Valid);
    }

    // ==========================================================================================
    //  OnGetFocus                                                                 [:L133-L143]
    // ==========================================================================================

    /// <summary>
    /// <c>:L136-L139</c> - the row is read FIRST and the column SECOND, and either guard abandons the
    /// refresh.
    /// </summary>
    [Theory]
    [InlineData(0L, Column, false)]
    [InlineData(1L, "", false)]
    [InlineData(1L, Column, true)]
    public void TheFocusRefreshNeedsBothARowAndAColumn(long row, string column, bool expectedValid)
    {
        (FakeDataWindowHost host, _, DropDownSearchModel service) = NewDddw();
        host.CurrentRow = row;
        host.CurrentColumnName = column;

        service.OnGetFocus();

        Assert.Equal(expectedValid, service.EditContext.Valid);
    }
}
