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
using System.Reflection;

using Microsoft.Extensions.Options;

using PowerFramework.DataServices.Configuration;
using PowerFramework.DataServices.Domain;
using PowerFramework.DataServices.Expressions;
using PowerFramework.DataServices.Services;
using PowerFramework.Shared.Eventful;
using PowerFramework.Shared.Kernel;

using Xunit;

// The two DataWindow alphabets the abstract host contract is written in live on the published wire
// contract, and the chain harness at the foot of this file has to restate their member signatures
// verbatim to override them. Aliased rather than spelled out at each of the eight sites, which is the
// convention the sibling chain doubles already follow.
using DwBuffer = PowerFramework.Contracts.Common.V1.DwBuffer;
using ItemStatus = PowerFramework.Contracts.Common.V1.ItemStatus;

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

    /// <summary>
    /// PowerBuilder's "this expression is invalid" answer from <c>Describe</c>, which the oracle tests for
    /// at <c>:L201</c>, <c>:L208</c> and <c>:L210</c> and - in all three cases - leaves in the field.
    /// </summary>
    private const string InvalidExpression = "!";

    /// <summary>
    /// The four filter constants BY NAME, so the reflection assertions read them off the type rather
    /// than restating their spellings a second time.
    /// </summary>
    /// <remarks>
    /// <c>nameof</c> throughout, deliberately. AAP 0.4.5.3 preserves the oracle's SCREAMING_SNAKE
    /// identifiers verbatim because they appear in serialized payloads, log records and characterization
    /// recordings - so this suite must never DECLARE one of its own, and reading them through
    /// <c>nameof</c> keeps the spelling owned by the file under test.
    /// </remarks>
    private static readonly string[] FilterConstantNames =
    [
        nameof(DropDownSearchModel.FILTER_DISP),      // :L50
        nameof(DropDownSearchModel.FILTER_DISP_PY),   // :L51
        nameof(DropDownSearchModel.FILTER_DATA),      // :L52
        nameof(DropDownSearchModel.FILTER_ALL),       // :L53
    ];

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
    /// <c>:L198-L223</c> - the builder reached DIRECTLY: it populates both records from the host's
    /// <c>Describe</c> probes and its <c>GetChild</c>, in the oracle's order, and then seeds the filter.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE MEMBER'S OWN CONTRACT, NOT ITS CALLER'S. Every other test in this section drives the builder
    /// through <c>onitemfocuschanged</c>, which is its only caller in the oracle [<c>:L81</c>]. Calling it
    /// directly here pins what the builder itself promises, which is what DECISION 8 of the file under
    /// test widened its accessibility for.
    /// </para>
    /// <para>
    /// THE PROBE ORDER IS A CONTRACT AND NOT AN IMPLEMENTATION DETAIL, because each answer GATES the next.
    /// A port that read the display column before the <c>AllowEdit</c> answer would interrogate a
    /// drop-down it had already been told not to edit; one that called <c>GetChild</c> before resolving
    /// both column names would obtain a child it could not describe. The oracle's order is therefore
    /// asserted as a sequence rather than as a set, and only the HOST-side reads appear - the four
    /// probes the builder makes against the CHILD [<c>:L212</c>, <c>:L215</c>, <c>:L216</c>,
    /// <c>:L219</c>] are asserted through the values they produced instead.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheContextBuilderPopulatesBothRecordsFromDescribeAndGetChild()
    {
        (FakeDataWindowHost host, FakeDataWindowChild child, DropDownSearchModel service) = NewDddw(
            childFilter: "dept='X'");

        // Reads are off by default, because most assertions in this suite are about MUTATIONS. This one
        // is about the interrogation itself, so they are turned on for it.
        host.RecordsReads = true;
        host.CallLog.Clear();

        service.InitEditContextInfo(1L, Dwo(host));

        // :L198, :L200, :L205, :L207, :L209, :L211 - five gated probes, then the child.
        Assert.Equal(
            [
                Column + ".Edit.DisplayOnly",    // :L198  answered "no", so the builder continues
                Column + ".Edit.Style",          // :L200  answered "dddw", so the drop-down arm runs
                Column + ".DDDW.AllowEdit",      // :L205  answered "yes"
                Column + ".DDDW.DisplayColumn",  // :L207  answered the display column
                Column + ".DDDW.DataColumn",     // :L209  answered the data column
                Column,                          // :L211  GetChild, LAST of the six
            ],
            host.CallLog.Records
                .Where(record =>
                    string.Equals(record.Member, "Describe", StringComparison.Ordinal)
                    || string.Equals(record.Member, "GetChild", StringComparison.Ordinal))
                .Select(record => (string)record.Arguments[0]!));

        // :L211  and it really is GetChild that closes the sequence, not a sixth Describe.
        Assert.Equal(
            "GetChild",
            host.CallLog.Records
                .Last(record =>
                    string.Equals(record.Member, "Describe", StringComparison.Ordinal)
                    || string.Equals(record.Member, "GetChild", StringComparison.Ordinal))
                .Member);

        // THE OUTER RECORD, populated  [:L185-L187, :L200, :L222]
        EditContextData context = service.EditContext;
        Assert.True(context.Valid);
        Assert.Equal(1L, context.Row);
        Assert.Equal(Column, context.Name);
        Assert.Equal("dddw", context.Style);
        Assert.Equal(string.Empty, context.LastData);

        // THE NESTED RECORD, populated from the two host probes and the four child probes
        DddwData dddw = context.Dddw;
        Assert.Equal(DispCol, dddw.DispColName);          // :L207  host
        Assert.Equal(DataCol, dddw.DataColName);          // :L209  host
        Assert.Same(child, dddw.Child);                   // :L211  host - THE SAME INSTANCE, not a copy
        Assert.Equal(76L, dddw.RowHeight);                // :L215  child  Detail.Height
        Assert.Equal("dept='X'", dddw.OrgFilter);         // :L216  child  Table.Filter
        Assert.Equal("dept='X'", dddw.Filter);            // :L218  seeded from the original
        Assert.Equal(0u, dddw.Hwnd);                      // :L214  DEFERRED, so it stays zero

        // :L219-L221  the child had no sort of its own, so one was seeded by display column.
        Assert.Equal(DispCol + " ASC", child.LastSortSet);

        // :L223  and the builder handed straight on to the filter seeder - which, on a FRESHLY BUILT
        // context, deliberately applies NOTHING. The seeder composes with empty data [:L247], which
        // :L315 short-circuits to the empty string, which :L379 resolves to the ORIGINAL filter - and
        // :L218 has already made that the applied filter, so the change guard at :L382 holds and
        // SetFilter is never reached. `LastFilterSet` is therefore null BY DESIGN: a port that applied
        // here would re-filter the child on every focus change for no reason.
        Assert.Null(child.LastFilterSet);
        Assert.Equal(string.Empty, dddw.InputFilter);

        // The seeder DID run, though - proven by the sort it seeded above and by the fact that the same
        // arrangement WITH a handler supplying an expression does reach SetFilter, which
        // ContextInitialisationRaisesTheGetFilterEventWithEmptyData asserts.
        Assert.Equal(dddw.OrgFilter, dddw.Filter);
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

    // ==========================================================================================
    //  THE CONSTANTS' WIDTH, AND WHERE THE TWO DEFAULTS ACTUALLY COME FROM      [:L50-L53, :L57]
    // ==========================================================================================

    /// <summary>
    /// <c>:L50-L53</c> - all four constants are declared <c>constant ulong</c>, and the port must carry
    /// the SAME WIDTH the shared bit test consumes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHY WIDTH IS A PARITY FACT AND NOT A STYLE CHOICE. PowerBuilder's <c>ulong</c> is 32-bit
    /// unsigned, and <c>ws_objects/pfw.common.pbl.src/bittest.srf</c> declares
    /// <c>bittest(readonly ulong num, readonly ulong bits)</c> - so the oracle's three bit tests at
    /// <c>:L318</c>, <c>:L321</c> and <c>:L326</c> are 32-bit unsigned operations. The shared port of
    /// that function is <c>Bits.BitTest(uint, uint)</c>, so <c>uint</c> is the width, and the property
    /// the constants are tested against must match or the call site would need a conversion the oracle
    /// does not have.
    /// </para>
    /// <para>
    /// The convention is PROVED rather than restated: the two-parameter <c>uint</c> overload is looked up
    /// by signature, so a change to either side breaks this test rather than silently inserting a widening
    /// conversion. Note that a signed port would be observably wrong and not merely untidy - the
    /// high-bit value in
    /// <see cref="SetFilterTypeAcceptsEveryValueIncludingZeroAndOutOfRange(uint)"/> is accepted by the
    /// oracle's unvalidated setter and would not survive a signed round trip.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheFilterConstantsCarryTheWidthTheSharedBitTestConsumes()
    {
        Type model = typeof(DropDownSearchModel);

        foreach (string name in FilterConstantNames)
        {
            FieldInfo? constant = model.GetField(name, BindingFlags.Public | BindingFlags.Static);

            Assert.NotNull(constant);

            // `constant ulong` - a compile-time literal of the oracle's own width.
            Assert.True(constant.IsLiteral);
            Assert.Equal(typeof(uint), constant.FieldType);
        }

        // :L57  privatewrite ulong #FilterType - the same width as the bits it is tested against.
        PropertyInfo? filterType = model.GetProperty(
            nameof(DropDownSearchModel.FilterType),
            BindingFlags.Public | BindingFlags.Instance);

        Assert.NotNull(filterType);
        Assert.Equal(typeof(uint), filterType.PropertyType);

        // bittest.srf, ported as Bits.BitTest. The lookup BY SIGNATURE is the assertion: this overload
        // exists at exactly (uint, uint), which is what makes the three call sites conversion-free.
        Assert.NotNull(typeof(Bits).GetMethod(
            nameof(Bits.BitTest),
            BindingFlags.Public | BindingFlags.Static,
            [typeof(uint), typeof(uint)]));

        // And the constants really do flow through it untouched, which is the whole point of the width.
        Assert.True(Bits.BitTest(DropDownSearchModel.FILTER_ALL, DropDownSearchModel.FILTER_DISP));
        Assert.True(Bits.BitTest(DropDownSearchModel.FILTER_ALL, DropDownSearchModel.FILTER_DISP_PY));
        Assert.True(Bits.BitTest(DropDownSearchModel.FILTER_ALL, DropDownSearchModel.FILTER_DATA));
        Assert.False(Bits.BitTest(
            DropDownSearchModel.FILTER_DISP + DropDownSearchModel.FILTER_DISP_PY,
            DropDownSearchModel.FILTER_DATA));
    }

    /// <summary>
    /// <c>:L57</c> - the default arrives from configuration. Whatever
    /// <c>DataServices:DropDownSearch:FilterType</c> carries is what the constructed service reports, so
    /// the value is BOUND rather than hardcoded in the model.
    /// </summary>
    /// <remarks>
    /// THE ZERO ROW AND THE HIGH-BIT ROW ARE THE DECISIVE ONES. A model that hardcoded <c>3</c> and
    /// merely happened to agree with the option's declared default would pass a test that only checked
    /// the default; it cannot pass a row that configures something else. Zero additionally proves the
    /// binding is not guarded on its way in - see DEFECT 4.
    /// </remarks>
    [Theory]
    [InlineData(0u)]
    [InlineData(1u)]
    [InlineData(3u)]
    [InlineData(5u)]
    [InlineData(7u)]
    [InlineData(4096u)]
    public void TheConstructedFilterTypeIsWhateverConfigurationSupplied(uint configured)
    {
        DropDownSearchModel service = new(OptionsWith(filterType: configured));

        Assert.Equal(configured, service.FilterType);
    }

    /// <summary>
    /// <c>:L57</c> - the <c>3</c> is declared ONCE, on
    /// <see cref="DropDownSearchOptions.FilterType"/>, and the model reproduces the oracle's default only
    /// by binding it (AAP 0.4.5.5).
    /// </summary>
    [Fact]
    public void TheFilterTypeDefaultBelongsToTheOptionsTypeAndNotToTheModel()
    {
        DropDownSearchOptions declared = new();

        // The option carries the oracle's declaration ...
        Assert.Equal(
            DropDownSearchModel.FILTER_DISP + DropDownSearchModel.FILTER_DISP_PY,
            declared.FilterType);

        // ... and the model, constructed from a bare options instance, simply agrees with it.
        DropDownSearchModel service = new(Options.Create(new DataServicesOptions()));
        Assert.Equal(declared.FilterType, service.FilterType);

        // The group hangs off the bound section, so the configuration key is
        // DataServices:DropDownSearch:FilterType.
        Assert.Equal("DataServices", DataServicesOptions.SectionName);
        Assert.Equal(declared.FilterType, new DataServicesOptions().DropDownSearch.FilterType);

        // :L58, :L503  the sibling default, declared on the same options type as the null it must be.
        Assert.Null(declared.ShowFilteredRows);
        Assert.Null(new DataServicesOptions().DropDownSearch.ShowFilteredRows);
    }

    /// <summary>
    /// <c>:L58</c>, <c>:L260</c>, <c>:L266-L285</c>, <c>:L503</c> - the three-state flag reaches ALL THREE
    /// states through its public setter, including BACK TO NULL, and the resolver behaves differently in
    /// each.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE NULL IS A REACHABLE STATE AND NOT MERELY A CONSTRUCTED ONE. The oracle declares
    /// <c>of_setshowfilteredrows(readonly boolean show)</c> [<c>:L266</c>] and assigns the argument
    /// straight through [<c>:L283</c>]; PowerScript value types carry null, so
    /// <c>boolean b; SetNull(b); of_SetShowFilteredRows(b)</c> restores auto-determination at any time.
    /// That is exactly why <c>:L260</c> re-tests <c>IsNull</c> on every resolution instead of trusting the
    /// one <c>SetNull</c> in the constructor [<c>:L503</c>]. AAP 0.4.5.4 forbids collapsing such a null, so
    /// a non-nullable setter would be a one-way door and would delete the third state.
    /// </para>
    /// <para>
    /// THE FIXTURE MAKES THE THREE STATES DISTINGUISHABLE, which a naive arrangement would not. The host
    /// is a GRID with two distinct drop-down columns, so the auto-determination answers TRUE by arm
    /// <c>:L262</c>. The sequence therefore drives the resolver's answer true, then FALSE while the
    /// explicit value stands, then true again once the null is restored - and the restoration is
    /// observable in the resolved behaviour and not only in the property.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheThreeStateFlagRoundTripsThroughAllThreeStatesIncludingBackToNull()
    {
        (FakeDataWindowHost host, _, DropDownSearchModel service) = Focused();

        // :L262  a grid, so the auto-determination answers true.
        host.Processing = "1";

        // STATE 1 - null, the constructed default [:L503]. The resolver runs [:L260 falls through].
        Assert.Null(service.ShowFilteredRows);
        Assert.True(service.IsShowFilteredRows());

        // STATE 2 - an explicit false. :L260 short-circuits AGAINST the auto answer.
        Assert.Equal(RetCode.OK, service.SetShowFilteredRows(false));
        Assert.False(service.ShowFilteredRows);
        Assert.False(service.IsShowFilteredRows());

        // STATE 3 - BACK TO NULL. The resolver runs again and answers true, so the auto-determination
        // was genuinely restored rather than merely recorded.
        Assert.Equal(RetCode.OK, service.SetShowFilteredRows(null));
        Assert.Null(service.ShowFilteredRows);
        Assert.True(service.IsShowFilteredRows());

        // STATE 4 - an explicit true. Same resolved answer as state 3, DIFFERENT property value, which
        // is what proves the two are distinct states rather than one.
        Assert.Equal(RetCode.OK, service.SetShowFilteredRows(true));
        Assert.True(service.ShowFilteredRows);
        Assert.True(service.IsShowFilteredRows());

        // And the round trip closes: null once more, from an explicit true this time.
        Assert.Equal(RetCode.OK, service.SetShowFilteredRows(null));
        Assert.Null(service.ShowFilteredRows);
    }

    /// <summary>
    /// <c>:L266</c> - the setter's parameter is NULLABLE and passed by read-only reference, matching
    /// <c>readonly boolean</c>, and it answers the oracle's <c>long</c>.
    /// </summary>
    /// <remarks>
    /// THIS TEST MUST FAIL IF THE PARAMETER IS EVER NARROWED TO A PLAIN <c>bool</c>. That narrowing is
    /// the exact mistake AAP 0.4.5.4 warns about, and it would silently make
    /// <see cref="TheThreeStateFlagRoundTripsThroughAllThreeStatesIncludingBackToNull"/> impossible to
    /// express rather than merely fail - so the shape is pinned here as well as exercised there.
    /// </remarks>
    [Fact]
    public void TheShowFilteredRowsSetterIsShapedLikeTheOraclesDeclaration()
    {
        MethodInfo? setter = typeof(DropDownSearchModel).GetMethod(
            nameof(DropDownSearchModel.SetShowFilteredRows),
            BindingFlags.Public | BindingFlags.Instance);

        Assert.NotNull(setter);

        // long, because every oracle setter answers a return code [:L284].
        Assert.Equal(typeof(long), setter.ReturnType);

        ParameterInfo parameter = Assert.Single(setter.GetParameters());

        // `in`, the port of `readonly` (AAP 0.4.5.2).
        Assert.True(parameter.ParameterType.IsByRef);
        Assert.True(parameter.IsIn);
        Assert.Equal(typeof(bool?), parameter.ParameterType.GetElementType());

        // The property it writes carries the same three states.
        PropertyInfo? property = typeof(DropDownSearchModel).GetProperty(
            nameof(DropDownSearchModel.ShowFilteredRows),
            BindingFlags.Public | BindingFlags.Instance);

        Assert.NotNull(property);
        Assert.Equal(typeof(bool?), property.PropertyType);
    }

    // ==========================================================================================
    //  THE TWO NESTED STRUCTURES, FIELD BY FIELD                                    [:L12-L31]
    // ==========================================================================================

    /// <summary>
    /// <c>:L12-L20</c> - <c>editcontextdata</c>, all SEVEN fields with the type each is declared with.
    /// </summary>
    /// <remarks>
    /// A PowerBuilder structure has no behaviour, so its field list IS its contract; a port that dropped
    /// one, renamed one or widened one would be a different structure wearing the same name. The
    /// <c>dwobject</c> row is the interesting translation: it becomes the host's own object abstraction
    /// rather than a pointer, per AAP 0.4.5.2.
    /// </remarks>
    public static TheoryData<string, Type> EditContextFieldMatrix() => new()
    {
        { nameof(EditContextData.Valid), typeof(bool) },                    // :L13  boolean   valid
        { nameof(EditContextData.Row), typeof(long) },                      // :L14  long      row
        { nameof(EditContextData.Dwo), typeof(IDataWindowObject) },         // :L15  dwobject  dwo
        { nameof(EditContextData.Name), typeof(string) },                   // :L16  string    name
        { nameof(EditContextData.Style), typeof(string) },                  // :L17  string    style
        { nameof(EditContextData.Dddw), typeof(DddwData) },                 // :L18  dddwdata  dddw
        { nameof(EditContextData.LastData), typeof(string) },               // :L19  string    lastdata
    };

    /// <summary>
    /// <c>:L22-L31</c> - <c>dddwdata</c>, all EIGHT fields with the type each is declared with.
    /// </summary>
    /// <remarks>
    /// THE EIGHTH FIELD IS THE WINDOW HANDLE, AND IT IS PRESENT ON PURPOSE. Dropping it would make the
    /// port carry seven fields where the oracle carries eight, so the structure ships whole and the
    /// handle is instead asserted to be permanently unpopulated by
    /// <see cref="TheOneWindowHandleFieldIsStructuralAndIsNeverPopulated"/> - which is how constraint C-D
    /// is honoured without falsifying <c>:L22-L31</c>.
    /// </remarks>
    public static TheoryData<string, Type> DddwFieldMatrix() => new()
    {
        { nameof(DddwData.DispColName), typeof(string) },                   // :L23  string          dispcolname
        { nameof(DddwData.DataColName), typeof(string) },                   // :L24  string          datacolname
        { nameof(DddwData.OrgFilter), typeof(string) },                     // :L25  string          orgfilter
        { nameof(DddwData.Filter), typeof(string) },                        // :L26  string          filter
        { nameof(DddwData.InputFilter), typeof(string) },                   // :L27  string          inputfilter
        { nameof(DddwData.Child), typeof(IDataWindowChild) },               // :L28  datawindowchild object
        { nameof(DddwData.RowHeight), typeof(long) },                       // :L29  long            rowheight
        { nameof(DddwData.Hwnd), typeof(uint) },                            // :L30  unsignedlong    hwnd
    };

    /// <summary>
    /// <c>:L12-L20</c> - each of the seven fields exists with the declared type.
    /// </summary>
    [Theory]
    [MemberData(nameof(EditContextFieldMatrix))]
    public void EachEditContextFieldMatchesTheOraclesDeclaration(string field, Type declared)
    {
        PropertyInfo? member = typeof(EditContextData).GetProperty(
            field,
            BindingFlags.Public | BindingFlags.Instance);

        Assert.NotNull(member);
        Assert.Equal(declared, member.PropertyType);
    }

    /// <summary>
    /// <c>:L22-L31</c> - each of the eight fields exists with the declared type.
    /// </summary>
    [Theory]
    [MemberData(nameof(DddwFieldMatrix))]
    public void EachDddwFieldMatchesTheOraclesDeclaration(string field, Type declared)
    {
        PropertyInfo? member = typeof(DddwData).GetProperty(
            field,
            BindingFlags.Public | BindingFlags.Instance);

        Assert.NotNull(member);
        Assert.Equal(declared, member.PropertyType);
    }

    /// <summary>
    /// <c>:L12-L31</c> - SEVEN fields and EIGHT fields, and not one more in either.
    /// </summary>
    /// <remarks>
    /// THE COUNT IS ASSERTED SEPARATELY FROM THE NAMES, because the two matrices above would still pass
    /// if a port had added a field of its own invention. Nothing may be added: the structures cross the
    /// service boundary as messages, and an extra field would appear in every characterization recording
    /// as a value the oracle never emitted. The names are compared as a SET rather than in order, because
    /// reflection's member order is an implementation detail of the compiler and not a contract - the
    /// declaration order is instead documented by the locators on the two matrices.
    /// </remarks>
    [Fact]
    public void TheTwoStructuresCarryExactlySevenAndEightFields()
    {
        PropertyInfo[] editContext = typeof(EditContextData).GetProperties(
            BindingFlags.Public | BindingFlags.Instance);
        PropertyInfo[] dddw = typeof(DddwData).GetProperties(
            BindingFlags.Public | BindingFlags.Instance);

        Assert.Equal(7, editContext.Length);
        Assert.Equal(8, dddw.Length);

        Assert.Equal(
            [
                nameof(EditContextData.Dddw),
                nameof(EditContextData.Dwo),
                nameof(EditContextData.LastData),
                nameof(EditContextData.Name),
                nameof(EditContextData.Row),
                nameof(EditContextData.Style),
                nameof(EditContextData.Valid),
            ],
            editContext.Select(member => member.Name).Order(StringComparer.Ordinal));

        Assert.Equal(
            [
                nameof(DddwData.Child),
                nameof(DddwData.DataColName),
                nameof(DddwData.DispColName),
                nameof(DddwData.Filter),
                nameof(DddwData.Hwnd),
                nameof(DddwData.InputFilter),
                nameof(DddwData.OrgFilter),
                nameof(DddwData.RowHeight),
            ],
            dddw.Select(member => member.Name).Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// The five ways <c>_of_initeditcontextinfo</c> can decide a cell has no searchable drop-down, each
    /// with the two column names the port must be left holding.
    /// </summary>
    /// <remarks>
    /// THE LAST TWO ROWS CARRY A SURPRISE AND IT IS THE ORACLE'S. Rows one to three exit BEFORE either
    /// column name is read, so both stay cleared. Row four reads the display column at <c>:L207</c> and
    /// exits at <c>:L208</c>, so the sentinel is STILL IN THE FIELD. Row five gets a real display column
    /// and then trips on the data column at <c>:L209-L210</c>, so the port is left holding one valid name
    /// and one sentinel. Every one of those five states is observable, so every one is pinned.
    /// </remarks>
    public static TheoryData<string, string, string, string, string, string, string>
        UnresolvableDropDownMatrix() => new()
    {
        // displayOnly, editStyle, allowEdit, dispCol, dataCol, expected dispColName, expected dataColName

        // :L199  a display-only column - the earliest exit, taken before the style is even read
        { "yes", "dddw", "yes", DispCol, DataCol, "", "" },

        // :L201  an edit style the DataWindow could not report
        { "no", InvalidExpression, "yes", DispCol, DataCol, "", "" },

        // :L206  a drop-down that refuses editing
        { "no", "dddw", "no", DispCol, DataCol, "", "" },

        // :L207-L208  no display column - THE SENTINEL IS ASSIGNED, then tested, then returned on
        { "no", "dddw", "yes", InvalidExpression, DataCol, InvalidExpression, "" },

        // :L209-L210  a real display column and no data column - one valid name and one sentinel
        { "no", "dddw", "yes", DispCol, InvalidExpression, DispCol, InvalidExpression },
    };

    /// <summary>
    /// <c>:L198-L213</c> - a drop-down the context builder CANNOT resolve leaves a DEFINED, INSPECTABLE
    /// state behind, never a null record and never a half-built one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHY THIS IS ASSERTED AS A CONTRACT RATHER THAN AS AN IMPLEMENTATION DETAIL. Every one of the
    /// oracle's failure exits is a bare <c>return</c> out of a <c>subroutine</c> [<c>:L199</c>,
    /// <c>:L201</c>, <c>:L206</c>, <c>:L208</c>, <c>:L210</c>, <c>:L211</c>, <c>:L213</c>], reached AFTER
    /// the thirteen-field reset at <c>:L183-L196</c> - so the structure is always present and always
    /// cleared, and <c>_editCtx.valid</c> stays false as the single machine-readable statement of "this
    /// cell has no searchable drop-down". A port that answered null instead would force every caller to
    /// null-check, and a caller that forgot would fault where the oracle simply does nothing.
    /// </para>
    /// <para>
    /// The public surface is exercised on the failed context too: both entry points a caller can reach
    /// answer a DEFINED return value rather than throwing, which is what makes the invalid state a
    /// reportable outcome instead of a latent fault.
    /// </para>
    /// </remarks>
    /// <seealso cref="UnresolvableDropDownMatrix"/>
    [Theory]
    [MemberData(nameof(UnresolvableDropDownMatrix))]
    public void AnUnresolvableDropDownYieldsADefinedInvalidContextRatherThanANullRecord(
        string displayOnly,
        string editStyle,
        string allowEdit,
        string dispCol,
        string dataCol,
        string expectedDispColName,
        string expectedDataColName)
    {
        (FakeDataWindowHost host, FakeDataWindowChild child, DropDownSearchModel service) = NewDddw(
            displayOnly: displayOnly,
            editStyle: editStyle,
            allowEdit: allowEdit);

        // The child keeps its real column names; only the HOST's Describe answer is made unresolvable,
        // which is what the oracle actually reads at :L207 and :L209.
        if (dispCol == InvalidExpression)
        {
            host.SetDescribe(Column + ".DDDW.DisplayColumn", InvalidExpression);
        }

        if (dataCol == InvalidExpression)
        {
            host.SetDescribe(Column + ".DDDW.DataColumn", InvalidExpression);
        }

        host.FocusedObject = host;
        service.OnItemFocusChanged(1L, Dwo(host));

        // The record is THERE, and so is its nested structure. Neither is ever null.
        EditContextData context = Assert.IsType<EditContextData>(service.EditContext);
        DddwData dddw = Assert.IsType<DddwData>(context.Dddw);

        // :L183  the defined error state, and the ONLY statement of it.
        Assert.False(context.Valid);

        // :L207, :L209  THE SENTINEL IS LEFT IN THE FIELD, exactly as :L201 leaves it in `style`. Both
        // reads ASSIGN and only then test, so a column the DataWindow could not report is recorded as the
        // literal "!" rather than normalised away - and a later comparison sees "!" and not "". Preserved,
        // not tidied (constraint C-B).
        Assert.Equal(expectedDispColName, dddw.DispColName);
        Assert.Equal(expectedDataColName, dddw.DataColName);

        // :L189-L196  every field the exit did not reach sits at its cleared value, so nothing
        // half-built leaks out.
        Assert.Null(dddw.Child);
        Assert.Equal(string.Empty, dddw.OrgFilter);
        Assert.Equal(string.Empty, dddw.Filter);
        Assert.Equal(string.Empty, dddw.InputFilter);
        Assert.Equal(0L, dddw.RowHeight);
        Assert.Equal(0u, dddw.Hwnd);

        // :L162-L163  the row and the object it was GIVEN are retained, so a later event can still say
        // WHICH cell was unresolvable.
        Assert.Equal(1L, context.Row);
        Assert.NotNull(context.Dwo);

        // The public surface answers rather than throws. :L363 for the predicate, :L439 for the guard.
        Assert.False(service.HasFilter());
        Assert.Equal(RetCode.OK, service.UpdateDddwFilter(Column, "dept='Y'", true));

        // Nothing was applied to the child, because nothing was resolved.
        Assert.Null(child.LastFilterSet);
    }

    // ==========================================================================================
    //  THE HOST'S HAND-OFF - ondwnchanging REACHES OnEditChanged ONLY WHEN #Enabled
    //                                                                   se_cst_dw.sru:L164-L174
    // ==========================================================================================

    /// <summary>
    /// <c>se_cst_dw.sru:L169-L171</c> - the raw <c>pbm_dwnchanging</c> event hands the keystroke to this
    /// service ONLY when the service is enabled, and the gate is the service's own <c>#Enabled</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHY THIS EDGE IS ASSERTED HERE AND NOT LEFT TO THE CHAIN'S OWN SUITE. DEFECT 3 is that
    /// <c>:L507</c> leaves the edit-changed subscription COMMENTED OUT, so
    /// <see cref="TheEditChangedTopicIsNotSubscribed"/> proves the broker never delivers a keystroke -
    /// which means this direct call is the ONLY route into <see cref="DropDownSearchModel.OnEditChanged"/>
    /// in the whole estate. An assertion that the route exists, and that its guard is the one the oracle
    /// writes, therefore belongs with the service it feeds: without it the two halves of the story could
    /// both pass while the service was in fact unreachable.
    /// </para>
    /// <para>
    /// THE CHAIN IS REAL AND THE SERVICE BEHIND IT IS REAL. <see cref="DropDownSearchChainHarness"/>
    /// supplies the abstract host contract by forwarding to a <see cref="FakeDataWindowHost"/> and adds
    /// nothing, and <see cref="DropDownSearchChainServiceFactory"/> hands the chain THIS model rather
    /// than a double - so what runs is the production dispatch path, and the evidence is the model's own
    /// observable state.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheChainHandsTheKeystrokeToTheServiceOnlyWhenItIsEnabled()
    {
        (DropDownSearchChainHarness chain, DropDownSearchModel service, FakeDataWindowChild child) =
            NewChain();

        // The chain's constructor already ran OnInit [se_cst_dw.sru:L578], so the service is attached but
        // DISABLED - n_cst_dwsvc.sru starts every attached service off.
        Assert.False(service.Enabled);

        service.OnItemFocusChanged(1L, chain.Host.GetObjectAttribute(Column));
        Assert.True(service.EditContext.Valid);
        _ = child.AddRow("ABBOTT", "2");

        // DISABLED - :L169's test fails, so the hand-off does not happen at all.
        Assert.Equal(RetCode.OK, chain.OnDwnChanging(1L, chain.Host.GetObjectAttribute(Column), "ab"));
        Assert.Null(service.Completion);
        Assert.Null(child.LastFilterSet);
        Assert.Equal(string.Empty, service.EditContext.LastData);

        // ENABLED - the same call now reaches the service, and the whole edit-changed pass runs.
        Assert.Equal(RetCode.OK, service.SetEnabled(true));
        Assert.True(service.Enabled);

        Assert.Equal(RetCode.OK, chain.OnDwnChanging(1L, chain.Host.GetObjectAttribute(Column), "ab"));

        // :L94-L96  the filter went in ...
        Assert.NotNull(child.LastFilterSet);

        // ... and :L127-L129 produced the completion, published as data.
        DropDownSearchCompletion completion = Assert.IsType<DropDownSearchCompletion>(service.Completion);
        Assert.Equal("ABBOTT", completion.Text);

        // :L102  and the ratchet moved, which only the service itself does.
        Assert.Equal("ab", service.EditContext.LastData);

        // DISABLED AGAIN - the gate is re-read on every dispatch, not latched at attachment.
        service.OnEditChanged(1L, chain.Host.GetObjectAttribute(Column), string.Empty);
        Assert.Equal(RetCode.OK, service.SetEnabled(false));
        child.LastFilterSet = null;

        Assert.Equal(RetCode.OK, chain.OnDwnChanging(1L, chain.Host.GetObjectAttribute(Column), "abb"));
        Assert.Null(child.LastFilterSet);
    }

    /// <summary>
    /// <c>se_cst_dw.sru:L170</c> - the hand-off passes the RAW event arguments straight through, including
    /// the null the raw <c>pbm_dwnchanging</c> argument can carry.
    /// </summary>
    /// <remarks>
    /// THE NULL IS THE POINT. <c>:L90</c> coerces it to the empty string INSIDE the service, which is only
    /// reachable if the chain forwards the null untransformed - so a chain that substituted <c>""</c> on
    /// the way in would make <c>:L90</c> dead code and would hide the coercion from every characterization
    /// recording. AAP 0.4.5.4 forbids exactly that substitution.
    /// </remarks>
    [Fact]
    public void TheChainHandOffCarriesTheRawArgumentsThroughUntransformed()
    {
        (DropDownSearchChainHarness chain, DropDownSearchModel service, FakeDataWindowChild child) =
            NewChain();

        IDataWindowObject dwo = chain.Host.GetObjectAttribute(Column);
        _ = service.SetEnabled(true);
        service.OnItemFocusChanged(4L, dwo);
        _ = child.AddRow("ABBOTT", "2");

        List<(long Row, string? Name, string Data)> seen = [];
        chain.Host.DdsGetFilterHandler = (row, filterDwo, data, filter) =>
        {
            seen.Add((row, filterDwo?.Name, data));
            return filter;
        };

        // A NULL edit text, exactly as the raw event can deliver it.
        Assert.Equal(RetCode.OK, chain.OnDwnChanging(4L, dwo, null));

        // :L90 coerced it, so the builder saw "" and the context's own row travelled with it [:L342].
        Assert.Equal([(4L, Column, string.Empty)], seen);

        // A non-null text on the same context reaches the search.
        Assert.Equal(RetCode.OK, chain.OnDwnChanging(4L, dwo, "ab"));
        Assert.Equal("ABBOTT", Assert.IsType<DropDownSearchCompletion>(service.Completion).Text);
    }

    /// <summary>
    /// Builds a real <see cref="DataWindowEventChain"/> holding the real
    /// <see cref="DropDownSearchModel"/>, with one <c>dddw</c> column and its child taught on the
    /// forwarded host.
    /// </summary>
    /// <remarks>
    /// The column setup happens AFTER construction, which is safe because the chain's constructor reads
    /// no DataWindow property - it creates its five attached services [<c>se_cst_dw.sru:L570-L574</c>] and
    /// initialises three of them [<c>:L576-L580</c>], and initialisation only lifts the host and its
    /// broker (<c>n_cst_dwsvc.sru:L85-L86</c>).
    /// </remarks>
    private static (DropDownSearchChainHarness Chain, DropDownSearchModel Service, FakeDataWindowChild Child)
        NewChain()
    {
        DropDownSearchModel service = new(OptionsWith());
        DropDownSearchChainHarness chain = new(
            new ValidationSession("dds-chain"),
            new DropDownSearchChainServiceFactory(service));

        _ = chain.Host.AddColumn(Column, "char(50)");
        chain.Host.SetDescribe(Column + ".Edit.DisplayOnly", "no");
        chain.Host.SetDescribe(Column + ".Edit.Style", "dddw");
        chain.Host.SetDescribe(Column + ".DDDW.AllowEdit", "yes");
        chain.Host.SetDescribe(Column + ".DDDW.DisplayColumn", DispCol);
        chain.Host.SetDescribe(Column + ".DDDW.DataColumn", DataCol);

        FakeDataWindowChild child = new(ChildName);
        _ = child.AddColumn(DispCol, "char(50)");
        _ = child.AddColumn(DataCol, "char(20)");
        child.SetDescribe("DataWindow.Detail.Height", "76");
        child.SetDescribe("DataWindow.Table.Filter", "?");
        child.SetDescribe("DataWindow.Table.Sort", "?");
        child.Calls = chain.Host.CallLog;
        chain.Host.SetChild(Column, child);

        // :L438  the focus guard tests the focused object against THE HOST THE SERVICE WAS ATTACHED TO,
        // which through a chain is the CHAIN and not the fake it forwards to - so the chain is what has
        // to hold focus for the harness to behave like a focused control.
        chain.Host.FocusedObject = chain;

        return (chain, service, child);
    }

    // ==========================================================================================
    //  THE `ref string` OUT-PARAMETER, BOTH WAYS                     [:L342, se_cst_dw.sru:L13]
    // ==========================================================================================

    /// <summary>
    /// <c>:L342-L344</c> with <c>se_cst_dw.sru:L13</c> - a handler that WRITES NOTHING leaves the composed
    /// expression exactly as the builder left it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THIS IS THE OTHER HALF OF <see cref="TheHandlerCanReplaceTheComposedFilter"/> AND IT IS THE COMMON
    /// CASE. The semantic event is declared <c>event onddsgetfilter(long row, dwobject dwo, string data,
    /// REF STRING filter)</c> [<c>se_cst_dw.sru:L13</c>], and the oracle passes its own local by reference
    /// and then returns that same local [<c>:L344</c>]. A PowerScript handler that never assigns the
    /// parameter therefore leaves the composition intact - it is an OPTIONAL override, not a required
    /// producer. The distinction matters because a port that treated "the handler returned nothing" as
    /// "the filter is empty" would silently disable filtering for every application that does not
    /// implement the event, which is most of them.
    /// </para>
    /// <para>
    /// The handler is genuinely REACHED - the call count proves it - so this is not the no-handler case
    /// tested by the byte-exact matrix. Both must hold, and they are different situations.
    /// </para>
    /// </remarks>
    [Fact]
    public void AHandlerThatWritesNothingLeavesTheComposedFilterInPlace()
    {
        (FakeDataWindowHost host, _, DropDownSearchModel service) = Focused(
            DropDownSearchModel.FILTER_DISP + DropDownSearchModel.FILTER_DISP_PY);

        int calls = 0;
        host.DdsGetFilterHandler = (row, dwo, data, filter) =>
        {
            calls++;

            // The `ref` parameter, returned exactly as it arrived - the port of a handler body that
            // never assigns `filter`.
            return filter;
        };

        Assert.Equal(
            "((Lower(empname) LIKE '%ab%') OR PinyinFirstLetterLike(empname,'ab',7))",
            service.GetFilter("ab"));

        // Reached, not skipped: this is the write-nothing case and not the no-handler case.
        Assert.Equal(1, calls);
    }

    /// <summary>
    /// <c>w_test_dwsvc_dropdownsearch.srw:L59-L64</c> - the oracle's OWN handler, which replaces the
    /// expression for exactly one cell and leaves every other composition untouched.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE FIXTURE IS THE LEGACY TEST WINDOW'S HANDLER, NOT AN INVENTED ONE. It reads
    /// <c>if row = 2 and dwo.name = "s2" then filter = "dat = '" + data + "'"</c> - a conditional
    /// assignment with no <c>else</c>, so the pass-through and the replacement are the SAME handler taking
    /// different branches. That is precisely the shape a port can get wrong in a way no single-branch test
    /// would catch: whether the composition survives depends on the handler's control flow and not on
    /// whether a handler is installed.
    /// </para>
    /// <para>
    /// Note that the replacement expression interpolates <c>data</c> UNESCAPED as well [DEFECT 2], and that
    /// it is the RAW typed text rather than the lower-cased or <c>%</c>-substituted form - the handler sees
    /// the third argument, which <c>:L342</c> passes through untransformed.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheOracleHandlerReplacesOneCellAndLeavesEveryOtherComposition()
    {
        (FakeDataWindowHost host, _, DropDownSearchModel service) = Focused(
            DropDownSearchModel.FILTER_DISP);

        host.DdsGetFilterHandler = (row, dwo, data, filter) =>
            row == 2L && string.Equals(dwo?.Name, Column, StringComparison.Ordinal)
                ? "dat = '" + data + "'"
                : filter;

        // Row 1 - the condition fails, so the composed expression is what comes back. Note the pattern is
        // the LOWER-CASED text [:L316] even though the handler was handed the raw text.
        Assert.Equal("((Lower(empname) LIKE '%ab%'))", service.GetFilter("Ab"));

        // Move to row 2 through the fast path [:L172-L178], which re-seeds with empty data and so takes
        // the handler's replacement branch straight away.
        service.OnItemFocusChanged(2L, Dwo(host));
        Assert.Equal("dat = ''", service.EditContext.Dddw.InputFilter);

        // Row 2 - the condition holds, and the RAW typed text is what the handler received.
        Assert.Equal("dat = 'Ab'", service.GetFilter("Ab"));
    }

    // ==========================================================================================
    //  REDRAW SUPPRESSION - THE VALUES, NOT JUST THE ORDER               [:L385-L388, :L409-L411]
    // ==========================================================================================

    /// <summary>
    /// <c>:L387</c> then <c>:L410</c> - suppression is <c>SetRedraw(FALSE)</c> and the restore is
    /// <c>SetRedraw(TRUE)</c>, in that order, bracketing the whole apply path.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE ARGUMENTS ARE THE ASSERTION, NOT THE CALL COUNT.
    /// <see cref="TheApplySequenceIsExactlyTheOraclesOrder"/> pins WHERE the two calls sit; a port could
    /// satisfy that and still pass the same value twice, which would either leave the drop-down frozen or
    /// never suppress the flicker at all. Both are observable through the recorded arguments, so both are
    /// checked.
    /// </para>
    /// <para>
    /// SUPPRESSION IS UNCONDITIONAL HERE AND CONDITIONAL IN THE ORACLE, and the difference is deferred
    /// rather than lost: <c>:L385</c> guards it with <c>IsWindowVisible(_editCtx.dddw.hWnd)</c>, a
    /// <c>user32.dll</c> prototype [<c>:L43</c>] that AAP 0.6.5 places out of scope as presentational
    /// (constraint C-D). With no window to interrogate, the guard has no headless meaning; suppressing
    /// always is the behaviour-preserving choice because the redraw state is restored on every path.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheRedrawIsSuppressedFalseThenRestoredTrue()
    {
        (FakeDataWindowHost host, FakeDataWindowChild child, DropDownSearchModel service) = Focused();
        _ = child.AddRow("a");
        child.FilteredRowCount = 2;

        service.ApplyFilter(1L, null, "age>30", show: false);

        Assert.Equal([false, true], RecordedRedraws(host));
    }

    /// <summary>
    /// <c>:L409-L411</c> - the restore happens on every path the suppression was applied on, including a
    /// FAILED relocation and a relocation that was never attempted.
    /// </summary>
    /// <remarks>
    /// A DROP-DOWN LEFT WITH REDRAW OFF IS A FROZEN DROP-DOWN, so the restore is the one call in the apply
    /// path that must not be conditional on anything the relocation did. Both branches are exercised
    /// because they are structurally different: <c>:L402</c> answering anything but <c>1</c> skips only the
    /// second height call, whereas <c>:L396</c> answering false skips the whole relocation block.
    /// </remarks>
    [Theory]

    // A relocation that reports failure  [:L402  RowsMove(...) <> 1]
    [InlineData(true, -1)]

    // A relocation that is never attempted  [:L396  _of_IsShowFilteredRows() = false]
    [InlineData(false, 1)]
    public void TheRedrawIsRestoredWhateverTheRelocationDid(bool showFilteredRows, int rowsMoveResult)
    {
        (FakeDataWindowHost host, FakeDataWindowChild child, DropDownSearchModel service) = Focused(
            showFilteredRows: showFilteredRows);
        _ = child.AddRow("a");
        child.FilteredRowCount = 2;
        child.RowsMoveResult = rowsMoveResult;

        service.ApplyFilter(1L, null, "age>30", show: false);

        Assert.Equal([false, true], RecordedRedraws(host));
    }

    /// <summary>
    /// <c>:L382</c> - the change guard skips the apply path entirely, so an unchanged composition never
    /// touches the redraw state at all.
    /// </summary>
    /// <remarks>
    /// The complement of the two tests above: suppression that is never applied must never be restored
    /// either, because a stray restore would re-enable redraw on a control the service did not suppress.
    /// </remarks>
    [Fact]
    public void AnUnchangedCompositionTouchesTheRedrawStateNotAtAll()
    {
        (FakeDataWindowHost host, _, DropDownSearchModel service) = Focused();

        service.ApplyFilter(1L, null, "age>30", show: false);
        Assert.Equal([false, true], RecordedRedraws(host));

        host.CallLog.Clear();
        service.ApplyFilter(1L, null, "age>30", show: false);

        Assert.Empty(RecordedRedraws(host));
    }

    /// <summary>
    /// Every <c>SetRedraw</c> argument the interleaved call log recorded, in call order.
    /// </summary>
    private static List<bool> RecordedRedraws(FakeDataWindowHost host)
    {
        return
        [
            .. host.CallLog.Records
                .Where(record => string.Equals(record.Member, "SetRedraw", StringComparison.Ordinal))
                .Select(record => (bool)record.Arguments[0]!)
        ];
    }

    // ==========================================================================================
    //  CONSTRAINT C-D - THE DEFERRED HALF IS ABSENT, AND THE ABSENCE IS ASSERTED POSITIVELY
    //                       [:L43, :L128-L129, :L171, :L214, :L251-L258, :L399, :L404, :L461-L493]
    // ==========================================================================================

    /// <summary>
    /// The vocabulary of the DEFERRED half, each token paired with the oracle site that would have
    /// introduced it. No member of the headless half may name any of them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// AAP 0.2.1.3 Correction 4 splits this one legacy service in two on the evidence of its
    /// type-position use of four window-management calls, and AAP 0.4.2.5 rules that only the headless
    /// half ships. AAP 0.4.4 names <c>/v1/design/**</c> as the reserved Gateway extension point behind
    /// which the rendering half will eventually live - and constraint C-D forbids implementing it now,
    /// EVEN AS A STUB. So the boundary is not merely untested here, it is asserted.
    /// </para>
    /// <para>
    /// THE TWO ACRONYMS ARE MATCHED CASE-SENSITIVELY AND THE REST ARE NOT, deliberately. Every other
    /// token is a distinctive multi-word identifier that cannot collide with anything legitimate, so
    /// case-insensitivity costs nothing and catches a renamed variant. <c>DPI</c> and <c>IME</c> are
    /// three-letter acronyms that DO occur inside ordinary words - a case-insensitive <c>IME</c> would
    /// match <c>GetItemTime</c> and <c>SelectionLength</c>'s neighbours on any future temporal member -
    /// so those two are matched as the upper-case acronyms they actually are. A false positive there
    /// would force the assertion to be weakened, which is worse than matching it precisely.
    /// </para>
    /// </remarks>
    public static TheoryData<string, bool> DeferredVocabulary() => new()
    {
        // The OS window handle itself - :L30 declares it, :L171 and :L214 would fill it, :L255 and
        // :L385 branch on it, :L469 obtains another.
        { "hWnd", false },
        { "WindowHandle", false },
        { "Handle(", false },

        // :L256  Win32.ShowWindow(_editCtx.dddw.hWnd,8) //SW_SHOWNA
        { "ShowWindow", false },

        // :L474, :L475, :L489  the height adjuster's three geometry calls
        { "GetWindowRect", false },
        { "OffsetRect", false },
        { "SetWindowPos", false },

        // :L488  SWP_NOZORDER + SWP_NOMOVE + SWP_NOACTIVATE
        { "SWP_", false },

        // :L43  the private external prototype, and the library it is bound to
        { "IsWindowVisible", false },
        { "user32", false },
        { "Win32", false },

        // :L399, :L404  the two row-geometry calls DECISION 3 defers
        { "SetDetailHeight", false },

        // :L480-L484  UnitsToPixels(..., YUnitsToPixels!)
        { "UnitsToPixels", false },
        { "PixelsToUnits", false },

        // :L128-L129  the two edit-control mutations DECISION 2 defers
        { "SetText", false },
        { "SelectText", false },

        // The input-method half of the split, which has no oracle site because the oracle relies on the
        // control's own IME and the port has no control.
        { "InputMethod", false },
        { "IME", true },
        { "DPI", true },
    };

    /// <summary>
    /// Constraint C-D - reflection over the headless half finds NO member, parameter, field or declared
    /// type that names a deferred window, geometry or input concern.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE SCAN COVERS THE MODEL AND ITS TWO PUBLISHED DATA SURFACES, which together are everything this
    /// file offers the world. <c>DddwData</c> is deliberately NOT in the scan and that is not an
    /// exemption: its eighth field is the oracle's own <c>unsignedlong hwnd</c> [<c>:L30</c>], carried so
    /// that the structure has eight fields rather than seven, and
    /// <see cref="TheOneWindowHandleFieldIsStructuralAndIsNeverPopulated"/> discharges it separately by
    /// proving it is permanently zero. Excluding it here and asserting it there is the honest split -
    /// weakening this scan to accommodate it would have blunted every other token.
    /// </para>
    /// <para>
    /// PRIVATE MEMBERS ARE SCANNED TOO, because a deferred capability reached through a private helper
    /// would breach C-D exactly as much as a public one. Compiler-generated members are scanned as well;
    /// they are named after the members they back, so they add no false positives.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(DeferredVocabulary))]
    public void NoMemberOfTheHeadlessHalfNamesADeferredWindowOrInputConcern(string token, bool caseSensitive)
    {
        StringComparison comparison = caseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        int scanned = 0;

        foreach (string name in HeadlessSurfaceVocabulary())
        {
            scanned++;
            Assert.DoesNotContain(token, name, comparison);
        }

        // NON-VACUITY, ASSERTED RATHER THAN ASSUMED. A reflection scan that found nothing would satisfy
        // every "does not contain" above and report a clean boundary while checking no boundary at all -
        // which is the one way this test could lie. The floor is deliberately far below the real figure so
        // it pins the scan being ALIVE rather than the exact member roster, which the other tests own.
        Assert.True(scanned > 100, "the reflection scan found nothing to check: " + scanned + " names");
    }

    /// <summary>
    /// <c>:L30</c> with <c>:L171</c>, <c>:L214</c> and <c>:L469</c> - the one window-handle field the port
    /// carries is STRUCTURAL, and it never holds a handle on any path.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The three oracle sites that could fill it are all <c>Handle(...)</c> calls on a
    /// <c>datawindowchild</c>, and all three are deferred (constraint C-D) - nothing headless can produce
    /// an OS window handle and nothing headless may branch on one. The field is therefore asserted to be
    /// zero after every operation that touches the context, not merely after construction: a port that
    /// quietly synthesised a non-zero token here would re-enable the two guards at <c>:L255</c> and
    /// <c>:L385</c> and would change what the apply path does.
    /// </para>
    /// <para>
    /// <c>show: true</c> is passed on the apply below on purpose. It is the argument that reaches
    /// <c>:L413-L415</c> and thence <c>_of_ShowDDDW</c>, the routine that is deferred IN FULL - so the one
    /// call site that would have needed the handle is exercised and still leaves it zero.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheOneWindowHandleFieldIsStructuralAndIsNeverPopulated()
    {
        PropertyInfo? hwnd = typeof(DddwData).GetProperty(
            nameof(DddwData.Hwnd),
            BindingFlags.Public | BindingFlags.Instance);

        Assert.NotNull(hwnd);
        Assert.Equal(typeof(uint), hwnd.PropertyType);

        (FakeDataWindowHost host, FakeDataWindowChild child, DropDownSearchModel service) = Focused(
            DropDownSearchModel.FILTER_ALL,
            childFilter: "dept='X'");
        _ = child.AddRow("ABBOTT", "2");
        child.FilteredRowCount = 2;
        IDataWindowObject dwo = Dwo(host);

        // :L214  a freshly built context - the one site that reads Handle() on the way in.
        Assert.True(service.EditContext.Valid);
        Assert.Equal(0u, service.EditContext.Dddw.Hwnd);

        // :L385, :L413-L415  the apply path, asked to SHOW the drop-down.
        service.ApplyFilter(1L, dwo, "age>30", show: true);
        Assert.Equal(0u, service.EditContext.Dddw.Hwnd);

        // :L104-L130  a full edit-changed pass.
        service.OnEditChanged(1L, dwo, "ab");
        Assert.Equal(0u, service.EditContext.Dddw.Hwnd);

        // :L170-L171  the fast path, which re-fetches the child and would re-read Handle().
        service.OnItemFocusChanged(2L, dwo);
        Assert.Equal(0u, service.EditContext.Dddw.Hwnd);

        // :L157-L158  and the reset path.
        service.OnLoseFocus();
        Assert.Equal(0u, service.EditContext.Dddw.Hwnd);

        // Nor was the child ever asked to do anything geometric: the two deferred height calls are
        // absent from the interleaved log, and their arguments travelled as data instead.
        Assert.DoesNotContain("SetDetailHeight", host.CallLog.Members);
        Assert.DoesNotContain("SetRowHeight", host.CallLog.Members);
    }

    /// <summary>
    /// Every value the model publishes is DATA - a return code, a count, a flag, expression text, or a
    /// record of those - and never an instruction to position, size or paint anything.
    /// </summary>
    /// <remarks>
    /// <para>
    /// AAP 0.8.1 rules that where the choice is between a partial implementation and a documented gap, the
    /// DOCUMENTED GAP WINS. This test states the positive half of that ruling as a shape: the deferred
    /// half is served by publishing the values it would have consumed, so the surface must consist of
    /// nothing but those values. A member answering a delegate, a handle, a Win32 structure or a
    /// command object would mean the rendering half had started to leak back in, which is exactly what
    /// constraint C-D forbids.
    /// </para>
    /// <para>
    /// The scan is over the PUBLISHED accessibility only - public and internal - because a private helper's
    /// return type is an implementation detail and not something the world can observe. <c>internal</c> is
    /// included rather than excluded because the parity suite reaches the builder and the apply path
    /// through it (DECISION 8 of the file under test), so those returns are observable to a consumer and
    /// must satisfy the same rule.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryValueTheModelPublishesIsData()
    {
        // The complete set of shapes the oracle's own surface produces: PowerScript's return code, its
        // long/boolean/ulong/string scalars, its three-state boolean, the two published records, and the
        // edit-context structure the parity suite reads.
        Type[] dataShapes =
        [
            typeof(void),
            typeof(bool),
            typeof(bool?),
            typeof(long),
            typeof(uint),
            typeof(string),
            typeof(DropDownSearchCompletion),
            typeof(DropDownSearchFilterPartition),
            typeof(EditContextData),
        ];

        int published = 0;

        foreach (MethodInfo method in typeof(DropDownSearchModel).GetMethods(AllDeclared))
        {
            // Public or internal: what the model PUBLISHES. Private and protected helpers are excluded
            // because nothing outside the type can observe their shapes.
            if (!method.IsPublic && !method.IsAssembly)
            {
                continue;
            }

            published++;
            Assert.Contains(method.ReturnType, dataShapes);
        }

        // NON-VACUITY. The whole published surface of the oracle is six members plus four events plus the
        // two property getters, so a scan that found fewer than a dozen has stopped seeing the type and the
        // loop above would pass while checking nothing.
        Assert.True(published > 12, "the published surface scan found only " + published + " members");

        // And the two published records are primitives all the way down - no nested object, no handle,
        // no callback, so a consumer can serialize either without reaching for anything else.
        Type[] primitives = [typeof(bool), typeof(long), typeof(string)];

        foreach (Type surface in (Type[])[
            typeof(DropDownSearchCompletion),
            typeof(DropDownSearchFilterPartition)])
        {
            PropertyInfo[] carried = surface.GetProperties(
                BindingFlags.Public | BindingFlags.Instance);

            Assert.NotEmpty(carried);

            foreach (PropertyInfo property in carried)
            {
                Assert.Contains(property.PropertyType, primitives);
            }
        }
    }

    // ==========================================================================================
    //  THE PINYIN COUPLING IS INDIRECT - EXPRESSION TEXT, NEVER A CALL           [:L321-L324]
    // ==========================================================================================

    /// <summary>
    /// <c>:L323</c> - the model NAMES the Pinyin function inside expression text, using exactly the
    /// spelling the matcher publishes and exactly the spelling the expression evaluator resolves.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE THREE SPELLINGS ARE ASSERTED TO BE ONE SPELLING, WHICH IS THE WHOLE OF THE COUPLING. The oracle
    /// writes the function name as a literal inside a filter expression, and the DataWindow's own
    /// expression evaluator resolves it when the filter is applied - so the model, the matcher and the
    /// evaluator are joined by a STRING and by nothing else. If any one of the three drifted, the emitted
    /// filter would name a function the evaluator does not know and the drop-down would simply stop
    /// matching, with no compile error anywhere to catch it.
    /// </para>
    /// <para>
    /// NO MATCH OUTCOME IS ASSERTED HERE, DELIBERATELY AND BY INSTRUCTION. AAP 0.6.5 records Pinyin
    /// first-letter matching as the single genuine parity risk in the in-scope set - the lookup table
    /// exists only inside the closed <c>pfw.dll</c> and the <c>flags</c> semantics are undocumented - and
    /// AAP risk R1 requires it be reported BLOCKED rather than approximated. That gate belongs to
    /// PinyinFirstLetterMatcherTests.cs, which owns both the blocked-outcome assertions and the proof that
    /// the function is registered whether or not it can answer. This file asserts the TEXT and stops.
    /// </para>
    /// </remarks>
    [Fact]
    public void ThePinyinCouplingIsIndirectThroughTheExpressionEvaluator()
    {
        (FakeDataWindowHost host, _, DropDownSearchModel service) = Focused(
            DropDownSearchModel.FILTER_DISP_PY);

        string filter = service.GetFilter("ab");

        // The emitted clause names the function the matcher publishes - the same constant, not a copy.
        Assert.Equal(
            " OR " + PinyinFirstLetterMatcher.ExpressionFunctionName + "(empname,'ab',7)",
            filter[1..^1]);

        // And the evaluator that will parse this expression resolves that same name.
        DataWindowExpressionEvaluator evaluator = new(host);
        Assert.True(evaluator.IsFunctionRegistered(PinyinFirstLetterMatcher.ExpressionFunctionName));
        Assert.Contains(PinyinFirstLetterMatcher.ExpressionFunctionName, evaluator.FunctionNames);
    }

    /// <summary>
    /// DECISION 4 of the file under test - the model takes NO compile-time dependency on the Pinyin
    /// matcher: it cannot call it, cannot hold one and is not injected with one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A DIRECT CALL WOULD BE A REAL DEFECT AND NOT A STYLE PREFERENCE. The oracle does not evaluate the
    /// Pinyin predicate itself; it hands the DataWindow a filter expression that mentions the function, and
    /// the DataWindow evaluates it once per row while filtering. A port that called the matcher from here
    /// would have to decide WHICH rows to call it for, which is a filtering algorithm the oracle does not
    /// contain - and it would drag the BLOCKED table gap (AAP risk R1) out of the evaluator and into this
    /// service, where it would surface as a fault instead of as a structured expression error.
    /// </para>
    /// <para>
    /// Every declared member is scanned, private ones included, along with field types, property types,
    /// method return types and every parameter type of every method and constructor - so an injected
    /// dependency, a cached instance and a static helper are all covered.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheModelTakesNoCompileTimeDependencyOnThePinyinMatcher()
    {
        Type[] forbidden =
        [
            typeof(PinyinFirstLetterMatcher),
            typeof(PinyinMatchConfiguration),
            typeof(IPinyinFirstLetterTable),
            typeof(PinyinMatchOutcome),
            typeof(PinyinMatchUnavailableReason),
        ];

        int scanned = 0;

        foreach (Type referenced in DeclaredTypeReferences(typeof(DropDownSearchModel)))
        {
            scanned++;
            Assert.DoesNotContain(referenced, forbidden);
        }

        // NON-VACUITY, for the same reason the C-D scan asserts it: a scan that reached no type would
        // report no dependency while proving nothing. The floor also confirms the scan really does reach
        // PRIVATE members, since the public surface alone is well under it.
        Assert.True(scanned > 40, "the type-reference scan reached only " + scanned + " types");

        // The constructor takes the options and nothing else, so there is no seam to inject one through.
        ConstructorInfo constructor = Assert.Single(typeof(DropDownSearchModel).GetConstructors());
        ParameterInfo parameter = Assert.Single(constructor.GetParameters());
        Assert.Equal(typeof(IOptions<DataServicesOptions>), parameter.ParameterType);
    }

    /// <summary>
    /// <c>:L320-L324</c> - the flag argument is emitted as configuration and the two sevens in this file
    /// are UNRELATED, which DECISION 5 of the file under test exists to prevent a reader conflating.
    /// </summary>
    /// <remarks>
    /// <c>FILTER_ALL</c> is a COLUMN-SET bitmask summing to <c>1 + 2 + 4</c> from <c>:L50-L53</c>. The
    /// literal <c>7</c> at <c>:L323</c> is a PRONUNCIATION-MATCHING bitmask summing to the three flags at
    /// <c>ws_objects/pfw.shared.pbl.src/enums.sru:L1147-L1149</c> - ignore case, ignore full-width versus
    /// half-width, match fuzzy pronunciation. Same numeral, different contract, different domain, and
    /// neither derived from the other: the columns come from <see cref="DropDownSearchModel.FilterType"/>
    /// and the flags from <see cref="DropDownSearchOptions.PinyinMatchFlags"/>. This test moves ONE of
    /// them and asserts the other does not follow.
    /// </remarks>
    [Fact]
    public void TheTwoSevensAreIndependentOfEachOther()
    {
        // The declared default of each really is 7 - hence the trap.
        Assert.Equal(7u, DropDownSearchModel.FILTER_ALL);
        Assert.Equal(7L, new DropDownSearchOptions().PinyinMatchFlags);

        // Move the FLAGS and leave the column set at FILTER_ALL: only the third argument changes.
        (_, _, DropDownSearchModel flagsMoved) = Focused(
            DropDownSearchModel.FILTER_ALL,
            pinyinMatchFlags: 1L);

        Assert.Equal(
            "((Lower(empname) LIKE '%ab%') OR PinyinFirstLetterLike(empname,'ab',1)"
                + " OR (Lower(empid) LIKE '%ab%'))",
            flagsMoved.GetFilter("ab"));

        // Move the COLUMN SET and leave the flags at 7: only the clause roster changes.
        (_, _, DropDownSearchModel columnsMoved) = Focused(
            DropDownSearchModel.FILTER_DISP_PY,
            pinyinMatchFlags: 7L);

        Assert.Equal("( OR PinyinFirstLetterLike(empname,'ab',7))", columnsMoved.GetFilter("ab"));
    }

    // ==========================================================================================
    //  THE AUTOCOMPLETE RANGE IS ONE-BASED                        [:L129, AAP 0.4.5.4 / RISK R9]
    // ==========================================================================================

    /// <summary>
    /// <c>:L129</c> - one row per partial input, with the ONE-BASED selection start and the length the
    /// oracle passes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE START IS <c>nLenData + 1</c> AND THE LENGTH IS <c>Len(sValDisp)</c>, and neither is what a
    /// zero-based reading would produce. The start is the position of the first character the user did NOT
    /// type, so a consumer that subtracted one would re-select the last character they did. The length is
    /// the FULL length of the match rather than the length of the completed tail, so for every row below
    /// the range deliberately runs past the end of the matched text - PowerBuilder clamps it, and
    /// computing the tail length instead would be a correction (constraint C-B).
    /// </para>
    /// <para>
    /// The last row is the boundary case: with the whole value typed, the start is one PAST the end of a
    /// six-character value and the length is still six.
    /// </para>
    /// </remarks>
    public static TheoryData<string, long, long> CompletionRangeMatrix() => new()
    {
        // typed, one-based selection start (:L129  nLenData + 1), selection length (Len(sValDisp))
        { "a", 2L, 6L },
        { "ab", 3L, 6L },
        { "abb", 4L, 6L },
        { "abbo", 5L, 6L },
        { "abbot", 6L, 6L },

        // The whole value typed: the range starts PAST the end and the length is unchanged.
        { "abbott", 7L, 6L },
    };

    /// <summary>
    /// <c>:L127-L129</c> - the completion the deferred half would apply, published as data with a
    /// ONE-BASED range.
    /// </summary>
    [Theory]
    [MemberData(nameof(CompletionRangeMatrix))]
    public void TheCompletionRangeIsOneBasedAcrossEveryPartialInput(
        string typed,
        long expectedStart,
        long expectedLength)
    {
        (FakeDataWindowHost host, FakeDataWindowChild child, DropDownSearchModel service) = Focused();
        _ = child.AddRow("ABBOTT", "2");

        service.OnEditChanged(1L, Dwo(host), typed);

        DropDownSearchCompletion completion = Assert.IsType<DropDownSearchCompletion>(service.Completion);

        Assert.Equal("ABBOTT", completion.Text);
        Assert.Equal(expectedStart, completion.SelectionStart);
        Assert.Equal(expectedLength, completion.SelectionLength);

        // The one-based relationship, stated as the arithmetic the oracle writes rather than as a constant.
        Assert.Equal(typed.Length + 1, completion.SelectionStart);
        Assert.Equal(completion.Text.Length, completion.SelectionLength);
    }

    // ==========================================================================================
    //  REFLECTION HELPERS
    // ==========================================================================================

    /// <summary>
    /// Every identifier the headless half exposes: the three type names, and for each declared member its
    /// own name plus the names of the types it produces or consumes.
    /// </summary>
    private static IEnumerable<string> HeadlessSurfaceVocabulary()
    {
        foreach (Type surface in HeadlessSurfaceTypes)
        {
            yield return surface.Name;

            foreach (MemberInfo member in surface.GetMembers(AllDeclared))
            {
                yield return member.Name;

                if (member is MethodBase callable)
                {
                    foreach (ParameterInfo parameter in callable.GetParameters())
                    {
                        yield return parameter.Name ?? string.Empty;
                        yield return SimpleName(parameter.ParameterType);
                    }
                }

                yield return member switch
                {
                    MethodInfo method => SimpleName(method.ReturnType),
                    PropertyInfo property => SimpleName(property.PropertyType),
                    FieldInfo field => SimpleName(field.FieldType),
                    _ => string.Empty,
                };
            }
        }
    }

    /// <summary>
    /// Every type <paramref name="surface"/> reaches through a declared member, private members included.
    /// </summary>
    private static IEnumerable<Type> DeclaredTypeReferences(Type surface)
    {
        foreach (MemberInfo member in surface.GetMembers(AllDeclared))
        {
            if (member is MethodBase callable)
            {
                foreach (ParameterInfo parameter in callable.GetParameters())
                {
                    yield return Unwrap(parameter.ParameterType);
                }
            }

            switch (member)
            {
                case MethodInfo method:
                    yield return Unwrap(method.ReturnType);
                    break;
                case PropertyInfo property:
                    yield return Unwrap(property.PropertyType);
                    break;
                case FieldInfo field:
                    yield return Unwrap(field.FieldType);
                    break;
                default:
                    break;
            }
        }
    }

    /// <summary>The three types that together are everything this file offers the world.</summary>
    private static Type[] HeadlessSurfaceTypes =>
    [
        typeof(DropDownSearchModel),
        typeof(DropDownSearchCompletion),
        typeof(DropDownSearchFilterPartition),
    ];

    /// <summary>Declared members only, at every accessibility, instance and static alike.</summary>
    private const BindingFlags AllDeclared = BindingFlags.Public | BindingFlags.NonPublic
        | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    /// <summary>The type's own name, with any <c>in</c>/<c>ref</c> wrapper removed.</summary>
    private static string SimpleName(Type type) => Unwrap(type).Name;

    /// <summary>Strips the by-reference wrapper an <c>in</c> or <c>ref</c> parameter carries.</summary>
    private static Type Unwrap(Type type) =>
        type.IsByRef ? type.GetElementType() ?? type : type;

    // ==========================================================================================
    //  THE LEGACY TEST WINDOW'S OWN FIXTURE SHAPE
    //          w_test_dwsvc_dropdownsearch.srw + dw_test_dwsvc_dddw.srd / dw_svc_sample_dddw.srd
    // ==========================================================================================

    /// <summary>
    /// Builds the drop-down the legacy test window actually shows: display column <c>dsp</c>, data column
    /// <c>dat</c>, both <c>char(100)</c>, detail height <c>96</c>, and the three rows the fixture ships.
    /// </summary>
    /// <remarks>
    /// <para>
    /// TAKEN FROM THE FIXTURE, NOT INVENTED. <c>dw_test_dwsvc_dddw.srd:L8-L9</c> and its twin
    /// <c>dw_svc_sample_dddw.srd:L8-L9</c> declare the two columns; <c>:L11</c> of each carries the data
    /// <c>("新建","NEW"), ("确认","CFD"), ("审核","ADT")</c>; <c>:L7</c> gives the detail band height
    /// <c>96</c>. The window itself enables the service and then widens the filter to every column with
    /// <c>of_SetFilterType(FILTER_ALL)</c> [<c>w_test_dwsvc_dropdownsearch.srw:L41-L43</c>], which is why
    /// this arrangement uses <see cref="DropDownSearchModel.FILTER_ALL"/> rather than the default.
    /// </para>
    /// <para>
    /// WHY THE SHAPE MATTERS RATHER THAN JUST THE VALUES. The fixture's display column holds Han text and
    /// its data column holds Latin codes, which is the exact arrangement the three-clause filter was
    /// designed for and the reason the Pinyin clause exists at all: a user types Latin first letters and
    /// expects Han rows back. It also makes the <c>Match(data,"[a-zA-Z]")</c> guard at <c>:L322</c>
    /// load-bearing rather than incidental, because both kinds of input are realistic here.
    /// </para>
    /// <para>
    /// NOTHING IS PORTED FROM THE WINDOW. Its <c>open</c> event is read as a SCENARIO - which switches to
    /// throw and which filter type to choose - and its <c>onddsgetfilter</c> handler is reproduced
    /// separately by <see cref="TheOracleHandlerReplacesOneCellAndLeavesEveryOtherComposition"/>. The
    /// legacy tree is the behavioural oracle and is never an edit target (constraint C-C).
    /// </para>
    /// </remarks>
    private static (FakeDataWindowHost Host, FakeDataWindowChild Child, DropDownSearchModel Service)
        FixtureDropDown()
    {
        (FakeDataWindowHost host, FakeDataWindowChild child, DropDownSearchModel service) = NewDddw(
            filterType: DropDownSearchModel.FILTER_ALL,
            dataColType: "char(100)",
            dataCol: "dat",
            dispCol: "dsp",
            dispColType: "char(100)");

        // dw_test_dwsvc_dddw.srd:L7  detail(height=96)
        child.SetDescribe("DataWindow.Detail.Height", "96");

        // dw_test_dwsvc_dddw.srd:L11  the three rows, display half then data half.
        _ = child.AddRow("新建", "NEW");
        _ = child.AddRow("确认", "CFD");
        _ = child.AddRow("审核", "ADT");

        service.OnItemFocusChanged(1L, Dwo(host));
        host.CallLog.Clear();

        return (host, child, service);
    }

    /// <summary>
    /// <c>:L313-L345</c> over the fixture's own columns - a LATIN input reaches all three clauses because
    /// the data column carries Latin codes.
    /// </summary>
    [Fact]
    public void TheFixtureShapeComposesAllThreeClausesForALatinInput()
    {
        (_, _, DropDownSearchModel service) = FixtureDropDown();

        // :L319 display LIKE, :L323 Pinyin over the LOWER-CASED text, :L331 data LIKE - in that order,
        // joined by the appending " OR " and wrapped once by :L340.
        Assert.Equal(
            "((Lower(dsp) LIKE '%new%') OR PinyinFirstLetterLike(dsp,'new',7)"
                + " OR (Lower(dat) LIKE '%new%'))",
            service.GetFilter("NEW"));
    }

    /// <summary>
    /// <c>:L322</c> over the fixture's own columns - a HAN input drops the Pinyin clause and NOTHING else,
    /// so the remaining two still compose and still join with the appending operator.
    /// </summary>
    /// <remarks>
    /// This is the case the fixture exists to make realistic: the display column is Han, so typing Han is
    /// the ordinary way to use this drop-down, and it must not silently lose the display or data clause
    /// along with the Pinyin one. Note the data clause still appends with a leading <c>" OR "</c> even
    /// though the clause before it is now the display clause rather than the Pinyin clause - the operator
    /// belongs to the appending clause, not to the pair.
    /// </remarks>
    [Fact]
    public void TheFixtureShapeDropsOnlyThePinyinClauseForAHanInput()
    {
        (_, _, DropDownSearchModel service) = FixtureDropDown();

        Assert.Equal(
            "((Lower(dsp) LIKE '%新%') OR (Lower(dat) LIKE '%新%'))",
            service.GetFilter("新"));
    }

    /// <summary>
    /// <c>:L104-L112</c>, <c>:L127-L129</c> over the fixture's own rows - the prefix search reads the
    /// DISPLAY column, so a Han prefix completes to a Han value with a ONE-BASED range over characters.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE RANGE IS IN CHARACTERS AND NOT BYTES, which is the whole reason this case is worth having
    /// alongside the Latin theory. PowerScript's <c>Len</c> over a string counts characters, so a
    /// two-character Han value has length two and a one-character prefix puts the selection start at two -
    /// exactly as it would for Latin text. A port that reached for a byte length would put the start at
    /// four here and would still pass every Latin row.
    /// </para>
    /// <para>
    /// Row order is what selects the match [<c>:L108</c>], and the seeded ascending sort at <c>:L220</c> is
    /// what makes that order predictable - the fixture ships no sort of its own
    /// [<c>dw_test_dwsvc_dddw.srd</c> declares none], so the service supplies one.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheFixtureShapeCompletesTheDisplayColumnWithAOneBasedCharacterRange()
    {
        (FakeDataWindowHost host, FakeDataWindowChild child, DropDownSearchModel service) =
            FixtureDropDown();

        // :L219-L221  the fixture carries no sort, so the service seeded one by display column.
        Assert.Equal("dsp ASC", child.LastSortSet);

        service.OnEditChanged(1L, Dwo(host), "新");

        DropDownSearchCompletion completion = Assert.IsType<DropDownSearchCompletion>(service.Completion);

        Assert.Equal("新建", completion.Text);

        // :L129  nLenData + 1 in CHARACTERS, and Len(sValDisp) in characters.
        Assert.Equal(2L, completion.SelectionStart);
        Assert.Equal(2L, completion.SelectionLength);
    }

    /// <summary>
    /// <c>:L215</c>, <c>:L399</c> over the fixture's own detail band - the captured row height travels to
    /// the deferred half as DATA, at the value the fixture declares and untransformed.
    /// </summary>
    /// <remarks>
    /// <c>dw_test_dwsvc_dddw.srd:L7</c> declares <c>detail(height=96)</c> in PowerBuilder units. The port
    /// captures the <c>Describe</c> answer at <c>:L215</c> and publishes it on the partition, and it must
    /// arrive as <c>96</c> - not converted to pixels, not scaled for DPI, not rounded. Converting it would
    /// be the first arithmetic operation on a geometry value, which is exactly where the headless/deferred
    /// split is drawn (constraint C-D).
    /// </remarks>
    [Fact]
    public void TheFixtureRowHeightTravelsAsDataAtItsDeclaredValue()
    {
        (_, FakeDataWindowChild child, DropDownSearchModel service) = FixtureDropDown();
        child.FilteredRowCount = 2;

        Assert.Equal(96L, service.EditContext.Dddw.RowHeight);

        service.ApplyFilter(1L, null, "dsp LIKE '%新%'", show: false);

        DropDownSearchFilterPartition partition = Assert.IsType<DropDownSearchFilterPartition>(
            service.Partition);

        // :L399  SetDetailHeight(1, nRowCnt, _editCtx.dddw.rowHeight) - the third argument, verbatim.
        Assert.Equal(96L, partition.VisibleRowHeight);

        // :L404  and the concealing height, which is a literal zero rather than a visibility flag.
        Assert.Equal(0L, partition.HiddenRowHeight);
    }

}

// ==================================================================================================
//  THE THREE HAND-WRITTEN DOUBLES THAT PRODUCE A REAL CHAIN AROUND THE REAL SERVICE
//  ------------------------------------------------------------------------------------------------
//  Domain/DataWindowEventChain.cs is the port of se_cst_dw and is the ONLY route into
//  DropDownSearchModel.OnEditChanged in the whole estate: DEFECT 3 leaves the edit-changed
//  subscription commented out at n_cst_dwsvc_dropdownsearch.sru:L507, so the broker never delivers a
//  keystroke and se_cst_dw.sru:L169-L171 has to. Asserting that edge - and its #Enabled gate - needs a
//  real chain with the REAL service behind it, and the three doubles below are the minimum that
//  produces one.
//
//  NOTHING HERE IS A STUB. The chain refuses a null service and creates all five of them in its
//  constructor [se_cst_dw.sru:L570-L574], so all five must exist; the four this suite does not exercise
//  are DISABLED and fully implemented, which keeps them out of the way honestly rather than by
//  throwing. The four interfaces they satisfy are two or three members wide and every member has a
//  real body.
//
//  WHY THESE ARE DECLARED HERE RATHER THAN BORROWED. The sibling suites each own a chain double of
//  their own, and each is sealed behind a factory type of its own that hands the chain a DOUBLE
//  drop-down service - which is precisely what this suite must not use. Nothing is shared between the
//  files in either direction, and the file-schema dependency set for this suite names
//  FakeDataWindowHost.cs and TestDoubles.cs only.
// ==================================================================================================

/// <summary>
/// Hands the chain the REAL <see cref="DropDownSearchModel"/> and four inert siblings.
/// </summary>
/// <remarks>
/// The drop-down service is handed over as the interface the chain consumes,
/// <see cref="IDataWindowDropDownSearchService"/>, which <see cref="DropDownSearchModel"/> implements
/// directly - so no adapter is needed and what the chain drives IS the subject of this suite.
/// </remarks>
internal sealed class DropDownSearchChainServiceFactory : IDataWindowAttachedServiceFactory
{
    /// <summary>Builds the factory over the service the chain is to hold.</summary>
    /// <param name="service">The real drop-down search service.</param>
    internal DropDownSearchChainServiceFactory(DropDownSearchModel service)
    {
        ArgumentNullException.ThrowIfNull(service);

        DropDownSearch = service;
    }

    /// <summary>The real service the chain holds.</summary>
    internal DropDownSearchModel DropDownSearch { get; }

    /// <summary>The context-menu stand-in the chain holds.</summary>
    internal InertChainService ContextMenu { get; } = new();

    /// <summary>The row-select stand-in the chain holds.</summary>
    internal InertChainService RowSelect { get; } = new();

    /// <summary>The column-sort stand-in the chain holds.</summary>
    internal InertChainService ColumnSort { get; } = new();

    /// <summary>The column-expression stand-in the chain holds.</summary>
    internal InertChainService ColumnExp { get; } = new();

    /// <inheritdoc/>
    public IDataWindowContextMenuService CreateContextMenu() => ContextMenu;

    /// <inheritdoc/>
    public IDataWindowRowSelectService CreateRowSelect() => RowSelect;

    /// <inheritdoc/>
    public IDataWindowColumnSortService CreateColumnSort() => ColumnSort;

    /// <inheritdoc/>
    public IDataWindowDropDownSearchService CreateDropDownSearch() => DropDownSearch;

    /// <inheritdoc/>
    public IDataWindowColumnExpressionService CreateColumnExp() => ColumnExp;
}

/// <summary>
/// A disabled, fully implemented stand-in for the four attached services this suite does not exercise.
/// </summary>
/// <remarks>
/// Always disabled. Every chain site that reaches one of these four tests <c>Enabled</c> first
/// [<c>se_cst_dw.sru:L169</c>, <c>:L313</c>, <c>:L409</c>], so a disabled double keeps them inert while
/// still recording what it was asked to do - which is what makes the EMPTY lists below evidence rather
/// than an absence of evidence.
/// </remarks>
internal sealed class InertChainService
    : IDataWindowContextMenuService,
      IDataWindowRowSelectService,
      IDataWindowColumnSortService,
      IDataWindowColumnExpressionService
{
    /// <summary>The hosts this double was initialised against, in call order.</summary>
    internal List<DataWindowServiceHost> InitialisedWith { get; } = [];

    /// <summary>The filter notifications received - <c>se_cst_dw.sru:L409</c>. Must stay empty here.</summary>
    internal List<int> Filtered { get; } = [];

    /// <summary>The item-changed notifications received - <c>se_cst_dw.sru:L313</c>. Must stay empty here.</summary>
    internal List<(long Row, string Column)> ItemChanged { get; } = [];

    /// <inheritdoc/>
    public bool Enabled => false;

    /// <inheritdoc/>
    public void OnInit(DataWindowServiceHost dw)
    {
        ArgumentNullException.ThrowIfNull(dw);

        InitialisedWith.Add(dw);
    }

    /// <inheritdoc/>
    public void OnFiltered() => Filtered.Add(Filtered.Count + 1);

    /// <inheritdoc/>
    public void OnItemChanged(long row, IDataWindowObject dwo)
    {
        ArgumentNullException.ThrowIfNull(dwo);

        ItemChanged.Add((row, dwo.Name));
    }
}

/// <summary>
/// A concrete <see cref="DataWindowEventChain"/> whose entire host surface forwards to a composed
/// <see cref="FakeDataWindowHost"/>, so the chain's real dispatch runs over an in-memory DataWindow.
/// </summary>
/// <remarks>
/// <para>
/// EVERY MEMBER BELOW IS A PURE FORWARD, DELIBERATELY. The subject is the chain's own delegation, so the
/// double must add no behaviour of its own - any decision taken here would be a decision the assertions
/// then measure instead of the chain's. The two semantic events the drop-down search RAISES BACK at its
/// host, <c>onddsgetfilter</c> [<c>se_cst_dw.sru:L13</c>] and <c>onddsfiltered</c> [<c>:L28</c>], are
/// forwarded too, because otherwise the handler seams on the fake would be unreachable through a chain.
/// </para>
/// <para>
/// THE HOST IS BUILT INSIDE THE CONSTRUCTOR, AFTER THE BASE HAS RUN, and that ordering is safe rather
/// than lucky: the base constructor creates the five attached services and initialises three of them
/// [<c>se_cst_dw.sru:L570-L580</c>], and initialisation reads nothing from the DataWindow - it lifts the
/// host reference and the host's broker and stops [<c>n_cst_dwsvc.sru:L85-L86</c>]. The broker it lifts
/// is the CHAIN's own, so one registry serves both halves of the double and a subscription made through
/// the chain is visible to anything reading the fake's broker.
/// </para>
/// </remarks>
internal sealed class DropDownSearchChainHarness : DataWindowEventChain
{
    /// <summary>Builds the double over a session and the factory holding the real service.</summary>
    /// <param name="session">The validation session the chain holds for its whole life.</param>
    /// <param name="services">The factory the chain creates its five attached services from.</param>
    internal DropDownSearchChainHarness(
        ValidationSession session,
        DropDownSearchChainServiceFactory services)
        : base(session, services)
    {
        Services = services;
        Host = new FakeDataWindowHost(Eventful);
    }

    /// <summary>The composed host every forwarded member reaches, and whose call log is the evidence.</summary>
    internal FakeDataWindowHost Host { get; }

    /// <summary>The factory, so an assertion reaches the instances the chain is actually holding.</summary>
    internal DropDownSearchChainServiceFactory Services { get; }

    // ---------------------------------------------------------------------------------------------
    //  THE HOST CONTRACT - every abstract member, forwarded
    // ---------------------------------------------------------------------------------------------

    /// <inheritdoc/>
    public override object? ObjectModel => Host.ObjectModel;

    /// <inheritdoc/>
    public override IDataWindowObject GetObjectAttribute(string dwoName) =>
        Host.GetObjectAttribute(dwoName);

    /// <inheritdoc/>
    public override string Describe(string property) => Host.Describe(property);

    /// <inheritdoc/>
    public override long RowCount() => Host.RowCount();

    /// <inheritdoc/>
    public override long GetRow() => Host.GetRow();

    /// <inheritdoc/>
    public override int SetRow(long row) => Host.SetRow(row);

    /// <inheritdoc/>
    public override string GetColumnName() => Host.GetColumnName();

    /// <inheritdoc/>
    public override int AcceptText() => Host.AcceptText();

    /// <inheritdoc/>
    public override int SetRedraw(bool enable) => Host.SetRedraw(enable);

    /// <inheritdoc/>
    public override long GetRowIDFromRow(long row) => Host.GetRowIDFromRow(row);

    /// <inheritdoc/>
    public override long GetRowFromRowID(long rowId) => Host.GetRowFromRowID(rowId);

    /// <inheritdoc/>
    public override int SetSort(string sort) => Host.SetSort(sort);

    /// <inheritdoc/>
    public override int Sort() => Host.Sort();

    /// <inheritdoc/>
    public override int GroupCalc() => Host.GroupCalc();

    /// <inheritdoc/>
    public override bool IsEventDisabled(uint evt) => Host.IsEventDisabled(evt);

    /// <inheritdoc/>
    public override long DisableEvent(uint evt) => Host.DisableEvent(evt);

    /// <inheritdoc/>
    public override int EnableEvent(uint evt) => Host.EnableEvent(evt);

    /// <inheritdoc/>
    public override int SelectRow(long row, bool select) => Host.SelectRow(row, select);

    /// <inheritdoc/>
    public override bool IsSelected(long row) => Host.IsSelected(row);

    /// <inheritdoc/>
    public override long GetSelectedRow(long startRow) => Host.GetSelectedRow(startRow);

    /// <inheritdoc/>
    public override object? GetFocusedObject() => Host.GetFocusedObject();

    /// <inheritdoc/>
    public override int SetFocus() => Host.SetFocus();

    /// <inheritdoc/>
    public override ItemStatus GetItemStatus(long row, long columnId, DwBuffer buffer) =>
        Host.GetItemStatus(row, columnId, buffer);

    /// <inheritdoc/>
    public override int SetItemStatus(long row, long columnId, DwBuffer buffer, ItemStatus status) =>
        Host.SetItemStatus(row, columnId, buffer, status);

    /// <inheritdoc/>
    public override int SetItem(long row, long columnId, string? value) =>
        Host.SetItem(row, columnId, value);

    /// <inheritdoc/>
    public override int SetItem(long row, long columnId, decimal? value) =>
        Host.SetItem(row, columnId, value);

    /// <inheritdoc/>
    public override int SetItem(long row, long columnId, long? value) =>
        Host.SetItem(row, columnId, value);

    /// <inheritdoc/>
    public override int SetItem(long row, long columnId, DateTime? value) =>
        Host.SetItem(row, columnId, value);

    /// <inheritdoc/>
    public override int SetItem(long row, long columnId, DateOnly? value) =>
        Host.SetItem(row, columnId, value);

    /// <inheritdoc/>
    public override int SetItem(long row, long columnId, TimeOnly? value) =>
        Host.SetItem(row, columnId, value);

    /// <inheritdoc/>
    public override int SetItem(long row, long columnId, object? value) =>
        Host.SetItem(row, columnId, value);

    /// <inheritdoc/>
    public override string? GetItemString(long row, string column) => Host.GetItemString(row, column);

    /// <inheritdoc/>
    public override decimal? GetItemDecimal(long row, string column) =>
        Host.GetItemDecimal(row, column);

    /// <inheritdoc/>
    public override double? GetItemNumber(long row, string column) => Host.GetItemNumber(row, column);

    /// <inheritdoc/>
    public override int SetItem(long row, string column, string? value) =>
        Host.SetItem(row, column, value);

    /// <inheritdoc/>
    public override int SetItem(long row, string column, decimal? value) =>
        Host.SetItem(row, column, value);

    /// <inheritdoc/>
    public override int SetItem(long row, string column, long? value) =>
        Host.SetItem(row, column, value);

    /// <inheritdoc/>
    public override DateTime? GetItemDateTime(long row, string column) =>
        Host.GetItemDateTime(row, column);

    /// <inheritdoc/>
    public override DateOnly? GetItemDate(long row, string column) => Host.GetItemDate(row, column);

    /// <inheritdoc/>
    public override TimeOnly? GetItemTime(long row, string column) => Host.GetItemTime(row, column);

    /// <inheritdoc/>
    public override int SetItem(long row, string column, DateTime? value) =>
        Host.SetItem(row, column, value);

    /// <inheritdoc/>
    public override int SetItem(long row, string column, DateOnly? value) =>
        Host.SetItem(row, column, value);

    /// <inheritdoc/>
    public override int SetItem(long row, string column, TimeOnly? value) =>
        Host.SetItem(row, column, value);

    /// <inheritdoc/>
    public override long Find(string expression, long start, long end) =>
        Host.Find(expression, start, end);

    /// <inheritdoc/>
    public override long InsertRow(long row) => Host.InsertRow(row);

    /// <inheritdoc/>
    public override string GetValue(string column, long index) => Host.GetValue(column, index);

    /// <inheritdoc/>
    public override int GetChild(string column, ref IDataWindowChild? child) =>
        Host.GetChild(column, ref child);

    /// <inheritdoc/>
    protected override int FilterCore() => Host.Filter();

    /// <inheritdoc/>
    protected override int DeleteRowCore(long row) => Host.DeleteRow(row);

    // ---------------------------------------------------------------------------------------------
    //  THE TWO SEMANTIC EVENTS THE DROP-DOWN SEARCH RAISES BACK AT ITS HOST
    //                                                        se_cst_dw.sru:L13 and se_cst_dw.sru:L28
    // ---------------------------------------------------------------------------------------------

    /// <inheritdoc/>
    /// <remarks>
    /// FORWARDED SO THE `ref` PARAMETER SURVIVES THE HOP. <c>se_cst_dw.sru:L13</c> declares the event
    /// with <c>ref string filter</c>, so the fake's handler seam has to receive the same reference the
    /// service passed - which is why this forwards by <c>ref</c> rather than by value.
    /// </remarks>
    public override void OnDDSGetFilter(long row, IDataWindowObject? dwo, string data, ref string filter) =>
        Host.OnDDSGetFilter(row, dwo, data, ref filter);

    /// <inheritdoc/>
    public override void OnDDSFiltered(long row, IDataWindowObject? dwo, long rowCount, long filteredCount) =>
        Host.OnDDSFiltered(row, dwo, rowCount, filteredCount);
}
