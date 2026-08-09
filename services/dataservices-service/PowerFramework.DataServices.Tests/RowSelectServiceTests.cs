// ==============================================================================================
//  RowSelectServiceTests - characterization of Services/RowSelectService.cs against its oracle,
//  ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_rowselect.sru (288 lines, READ ONLY).
//  --------------------------------------------------------------------------------------------
//  These are characterization tests in Michael Feathers' sense: they assert what the legacy
//  ACTUALLY does, including the four defects the refactor is required to preserve, rather than what
//  it arguably should do. Where a test looks like it is asserting a bug, it is - and the assertion
//  carries the locator that proves the bug is the oracle's.
//
//  NO DataWindow, NO DATABASE AND NO UI IS INVOLVED. Everything runs against FakeDataWindowHost,
//  which satisfies the abstract host contract in memory, so the whole suite is deterministic and
//  needs no service, no container and no fixture file (constraint C-H).
//
//  THE FIVE ASSERTIONS THAT MATTER MOST, AND WHY THEY ARE HERE
//    1. Style == 3 reaches a THIRD behavioural path. The oracle's own test window sets exactly that
//       value [ws_objects/pfw.tests.pbl.src/w_test_dwsvc_rowselect.srw], and the implementation
//       discriminates by EQUALITY at all seven consumption sites. Without these tests a later
//       "cleanup" to a bitwise test would pass silently while changing behaviour for the one value
//       the fixture uses.
//    2. SetStyle validates AFTER its idempotent early-out, so the zero rejection is unreachable.
//    3. SetCurrentRow leaves its re-entrancy flag set when the host raises - no try/finally.
//    4. A REFUSED range propagation still answers 1, indistinguishably from a successful one.
//    5. The rejection message is composed from two separate localization lookups with Sprintf
//       applied to the TRANSLATION, and carries every composition input as data.
// ==============================================================================================

using Microsoft.Extensions.Options;

using PowerFramework.DataServices.Configuration;
using PowerFramework.DataServices.Domain;
using PowerFramework.DataServices.Services;
using PowerFramework.Shared.Eventful;
using PowerFramework.Shared.Kernel;
using PowerFramework.Shared.Localization;

using Xunit;

namespace PowerFramework.DataServices.Tests;

/// <summary>
/// A localization provider that records what it was asked to translate and answers from a table -
/// the port-side stand-in for a concrete <c>n_cst_i18n</c> subclass.
/// </summary>
internal sealed class RecordingI18nProvider : II18nProvider
{
    private readonly Dictionary<string, string> _table = new(StringComparer.Ordinal);

    public List<(long Source, long Category, string? Text)> Requests { get; } = [];

    public RecordingI18nProvider Teach(string key, string translation)
    {
        _table[key] = translation;
        return this;
    }

    public long OnTranslate(long source, long category, ref string? text)
    {
        Requests.Add((source, category, text));

        if (text is not null && _table.TryGetValue(text, out string? translation))
        {
            text = translation;
        }

        return RetCode.OK;
    }
}

/// <summary>
/// A host whose <c>SetRow</c> calls back into the service under test, reproducing the DataWindow
/// behaviour that makes <c>_inChangeRowFocus</c> necessary at all.
/// </summary>
/// <remarks>
/// A real DataWindow raises its row-change event from inside <c>SetRow</c>, and the row-select
/// service subscribes to that event - so moving the row on the service's own behalf would call the
/// service back and have it discard the selection it is mid-way through building. This double makes
/// that re-entrancy real so the guard at <c>n_cst_dwsvc_rowselect.sru:L88</c> can be observed rather
/// than assumed.
/// </remarks>
internal sealed class ReentrantFakeDataWindowHost : FakeDataWindowHost
{
    public RowSelectService? Service { get; set; }

    public long? ReentrantResult { get; private set; }

    public int CallCountSeenInsideCallback { get; private set; } = -1;

    public override int SetRow(long row)
    {
        int result = base.SetRow(row);

        if (Service is not null)
        {
            CallCountSeenInsideCallback = CallLog.Count;
            ReentrantResult = Service.OnRowFocusChanged(row);
        }

        return result;
    }
}

public sealed class RowSelectServiceTests
{
    private const string FlagColumn = "flag";
    private const string OnValue = "Y";
    private const string OffValue = "N";

    // ------------------------------------------------------------------------------------------
    //  ARRANGEMENT HELPERS
    // ------------------------------------------------------------------------------------------

    private static IOptions<DataServicesOptions> OptionsWithStyle(long style)
    {
        return Options.Create(new DataServicesOptions
        {
            RowSelect = new RowSelectOptions { Style = style },
        });
    }

    private static RowSelectService CreateService(
        FakeDataWindowHost host,
        long style = RowSelectService.RS_SINGLE,
        I18n? i18n = null)
    {
        RowSelectService service = new(i18n ?? new I18n(), OptionsWithStyle(style));
        service.OnInit(host);
        return service;
    }

    /// <summary>
    /// A host with four rows and one editable check-box column, arranged so that every one of the
    /// eight range-propagation preconditions passes.
    /// </summary>
    private static FakeDataWindowHost CreateCheckBoxHost(
        string colType = "char(1)",
        params object?[] values)
    {
        FakeDataWindowHost host = new();

        FakeDataWindowObjectDefinition column = host.AddColumn(FlagColumn, colType);

        // :L192 - IsColumnEditable requires a tab sequence that is neither "0" nor "32766"; the fake
        // defaults to "0", which would fail the guard.
        column.TabSequence = "10";

        // :L195
        column.EditStyle = "checkbox";

        // :L196, :L198
        column.CheckBoxOn = OnValue;
        column.CheckBoxOff = OffValue;

        // :L227 - IsItemVisible tests the property for the string "1"; an untaught property answers the
        // invalid-expression sentinel, which would skip every row.
        column.SetProperty("visible", "1");

        foreach (object? value in values)
        {
            host.AddRow(value);
        }

        host.CurrentRow = 1L;
        return host;
    }

    private static IDataWindowObject FlagObject(FakeDataWindowHost host)
    {
        return host.GetObjectAttribute(FlagColumn);
    }

    /// <summary>
    /// Arranges a fully-armed range propagation: four rows, the whole set selected, and the service
    /// already in the multi-selected state the propagation requires.
    /// </summary>
    private static (FakeDataWindowHost Host, RowSelectService Service) ArrangePropagation(
        string colType,
        object?[] values,
        long style = RowSelectService.RS_MULTIPLE,
        I18n? i18n = null)
    {
        FakeDataWindowHost host = CreateCheckBoxHost(colType, values);
        RowSelectService service = CreateService(host, style, i18n);

        // :L194 - the propagation refuses to run unless a modifier gesture produced the selection. A
        // Control-click on row 1 is the cheapest way to reach that state honestly.
        host.ArrangeSelectedRows();
        service.OnLButtonClk(0L, 0L, 1L, FlagObject(host), shiftHeld: false, ctrlHeld: true);

        // :L193 - and the clicked row must itself be selected.
        host.ArrangeSelectedRows(1L, 2L, 3L, 4L);
        host.CallLog.Clear();

        return (host, service);
    }

    // ==========================================================================================
    //  CONSTANTS, SHAPE AND CONSTRUCTION
    // ==========================================================================================

    [Fact]
    public void Constants_PreserveTheOracleSpellingsAndValues()
    {
        // :L20, :L21
        Assert.Equal(1L, RowSelectService.RS_SINGLE);
        Assert.Equal(2L, RowSelectService.RS_MULTIPLE);

        // :L239 - `~n` is a BARE LINE FEED, not a carriage-return pair and not Environment.NewLine.
        Assert.Equal("\n", RowSelectService.LineSeparator);

        // :L239 - both lookup keys verbatim, plus the untranslated trailing literal.
        Assert.Equal("第{}行", RowSelectService.RowNumberMessageKey);
        Assert.Equal("修改数据被拒绝", RowSelectService.RejectionMessageKey);
        Assert.Equal("!", RowSelectService.RejectionMessageSuffix);
    }

    [Fact]
    public void Style_DefaultsToRsSingleBoundFromConfiguration()
    {
        // :L25 - `privatewrite long #Style = RS_SINGLE`, reproduced as the configured default.
        RowSelectService service = new(new I18n(), Options.Create(new DataServicesOptions()));

        Assert.Equal(RowSelectService.RS_SINGLE, service.Style);
    }

    [Fact]
    public void Style_HonoursAConfiguredCombinedValue()
    {
        // The value ws_objects/pfw.tests.pbl.src/w_test_dwsvc_rowselect.srw itself sets.
        RowSelectService service = new(
            new I18n(),
            OptionsWithStyle(RowSelectService.RS_SINGLE + RowSelectService.RS_MULTIPLE));

        Assert.Equal(3L, service.Style);
    }

    [Fact]
    public void Constructor_RejectsNullDependencies()
    {
        Assert.Throws<ArgumentNullException>(
            () => new RowSelectService(null!, Options.Create(new DataServicesOptions())));
        Assert.Throws<ArgumentNullException>(() => new RowSelectService(new I18n(), null!));
    }

    [Fact]
    public void HasMultiSelected_IsFalseBeforeAnyGestureAndNeedsNoHost()
    {
        // :L148 - and it is reachable with no host attached at all, which is what keeps it testable in
        // isolation.
        RowSelectService service = new(new I18n(), Options.Create(new DataServicesOptions()));

        Assert.False(service.HasMultiSelected());
    }

    [Fact]
    public void PendingError_IsNullBeforeAnyClick()
    {
        Assert.Null(CreateService(new FakeDataWindowHost()).PendingError);
    }

    // ==========================================================================================
    //  SetStyle - :L151-L182
    // ==========================================================================================

    [Fact]
    public void SetStyle_SameValue_AnswersOkAndTouchesNothing()
    {
        FakeDataWindowHost host = CreateCheckBoxHost(values: [OnValue]);
        RowSelectService service = CreateService(host, RowSelectService.RS_SINGLE);
        service.SetEnabled(true);
        host.CallLog.Clear();

        // :L168
        Assert.Equal(RetCode.OK, service.SetStyle(RowSelectService.RS_SINGLE));
        Assert.Empty(host.CallLog.Records);
    }

    [Fact]
    public void SetStyle_Zero_AnswersInvalidArgumentAndLeavesStyleAlone()
    {
        RowSelectService service = CreateService(new FakeDataWindowHost());

        // :L169
        Assert.Equal(RetCode.E_INVALID_ARGUMENT, service.SetStyle(0L));
        Assert.Equal(RowSelectService.RS_SINGLE, service.Style);
    }

    [Fact]
    public void SetStyle_ZeroRejection_StaysReachableBecauseZeroCanNeverBeStored()
    {
        // *** DEFECT/ORDERING PRESERVED - :L168 BEFORE :L169 ***
        // The idempotent early-out precedes the validation, so SetStyle(0) would answer OK if Style
        // were already 0. This test proves the arm is unreachable BY CONSTRUCTION rather than by
        // reordering: zero is refused every time, so it never becomes the stored value, so the
        // early-out never sees it.
        RowSelectService service = CreateService(new FakeDataWindowHost());

        Assert.Equal(RetCode.E_INVALID_ARGUMENT, service.SetStyle(0L));
        Assert.Equal(RetCode.E_INVALID_ARGUMENT, service.SetStyle(0L));
        Assert.NotEqual(0L, service.Style);
    }

    [Fact]
    public void SetStyle_WhileDisabled_RecordsTheValueAndTouchesNoHost()
    {
        FakeDataWindowHost host = CreateCheckBoxHost(values: [OnValue]);
        RowSelectService service = CreateService(host, RowSelectService.RS_SINGLE);
        host.CallLog.Clear();

        // :L173 - the side effects are gated on Enabled.
        Assert.Equal(RetCode.OK, service.SetStyle(RowSelectService.RS_MULTIPLE));
        Assert.Equal(RowSelectService.RS_MULTIPLE, service.Style);
        Assert.Empty(host.CallLog.Records);
    }

    [Theory]
    // :L175 - EQUALITY, NOT A BIT TEST. RS_SINGLE and the COMBINED value 3 both re-select the current
    // row because neither equals RS_MULTIPLE; RS_MULTIPLE alone does not.
    [InlineData(RowSelectService.RS_SINGLE, true)]
    [InlineData(RowSelectService.RS_MULTIPLE, false)]
    [InlineData(3L, true)]
    public void SetStyle_WhileEnabled_ReconcilesTheSelectionByEquality(long target, bool expectReselect)
    {
        FakeDataWindowHost host = CreateCheckBoxHost(values: [OnValue, OffValue]);
        host.CurrentRow = 2L;

        // Start from a style the target differs from, so the idempotent early-out does not fire.
        RowSelectService service = CreateService(host, target == 7L ? 3L : 7L);
        service.SetEnabled(true);
        host.CallLog.Clear();

        Assert.Equal(RetCode.OK, service.SetStyle(target));

        List<string> expected = ["SelectRow[0, false]"];
        if (expectReselect)
        {
            // :L176
            expected.Add("SelectRow[2, true]");
        }

        Assert.Equal(expected, host.CallLog.Descriptions);
        Assert.False(service.HasMultiSelected());
    }

    // ==========================================================================================
    //  Style == 3 - THE THIRD BEHAVIOURAL PATH
    // ==========================================================================================

    [Theory]
    // The five inequality sites, exercised through the three members that reach them. The COMBINED
    // value behaves like RS_SINGLE at every one of them and like RS_MULTIPLE at none - it is a third
    // state, not the union of two.
    [InlineData(RowSelectService.RS_SINGLE, true)]
    [InlineData(RowSelectService.RS_MULTIPLE, false)]
    [InlineData(3L, true)]
    public void OnRowFocusChanged_ReselectsByEquality(long style, bool expectReselect)
    {
        FakeDataWindowHost host = CreateCheckBoxHost(values: [OnValue, OffValue, OnValue]);
        RowSelectService service = CreateService(host, style);
        host.CallLog.Clear();

        // :L92
        Assert.Equal(0L, service.OnRowFocusChanged(3L));

        List<string> expected = ["SelectRow[0, false]"];
        if (expectReselect)
        {
            expected.Add("SelectRow[3, true]");
        }

        Assert.Equal(expected, host.CallLog.Descriptions);
        Assert.False(service.HasMultiSelected());
    }

    [Theory]
    [InlineData(RowSelectService.RS_SINGLE, true)]
    [InlineData(RowSelectService.RS_MULTIPLE, false)]
    [InlineData(3L, true)]
    public void OnFiltered_ReselectsByEquality(long style, bool expectReselect)
    {
        FakeDataWindowHost host = CreateCheckBoxHost(values: [OnValue, OffValue, OnValue]);
        host.CurrentRow = 2L;
        RowSelectService service = CreateService(host, style);
        host.CallLog.Clear();

        // :L101-L111 - no return value at all.
        service.OnFiltered();

        List<string> expected = ["SelectRow[0, false]"];
        if (expectReselect)
        {
            // :L106-L107
            expected.Add("SelectRow[2, true]");
        }

        Assert.Equal(expected, host.CallLog.Descriptions);
        Assert.False(service.HasMultiSelected());
    }

    [Fact]
    public void OnRowFocusChanged_WithNoCurrentRow_ClearsWithoutReselecting()
    {
        // :L92's `currentRow > 0` guard is a real case: an emptied DataWindow reports row 0.
        FakeDataWindowHost host = CreateCheckBoxHost(values: [OnValue]);
        RowSelectService service = CreateService(host, RowSelectService.RS_SINGLE);
        host.CallLog.Clear();

        Assert.Equal(0L, service.OnRowFocusChanged(0L));

        Assert.Equal(["SelectRow[0, false]"], host.CallLog.Descriptions);
    }

    [Fact]
    public void OnFiltered_WithNoCurrentRow_ClearsWithoutReselecting()
    {
        FakeDataWindowHost host = CreateCheckBoxHost(values: [OnValue]);
        host.CurrentRow = 0L;
        RowSelectService service = CreateService(host, RowSelectService.RS_SINGLE);
        host.CallLog.Clear();

        // :L106's `nRow > 0` guard.
        service.OnFiltered();

        Assert.Equal(["SelectRow[0, false]"], host.CallLog.Descriptions);
    }

    // ==========================================================================================
    //  OnLButtonClk - :L40-L86
    // ==========================================================================================

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void OnLButtonClk_WithNoRow_AnswersZeroAndTouchesNothing(long row)
    {
        // :L42 - and it touches neither the host nor dwo, which is why both guards in the port sit
        // AFTER this early-out. A null dwo therefore does not raise here.
        FakeDataWindowHost host = CreateCheckBoxHost(values: [OnValue]);
        RowSelectService service = CreateService(host, RowSelectService.RS_MULTIPLE);
        host.CallLog.Clear();

        Assert.Equal(0L, service.OnLButtonClk(1L, 2L, row, null!, shiftHeld: true, ctrlHeld: true));
        Assert.Empty(host.CallLog.Records);
    }

    [Theory]
    // The truth table of :L45, :L49 and :L61/:L76 across the three styles and the four modifier
    // combinations. 0 means the DataWindow's own click handling proceeds; 1 means it is prevented.
    [InlineData(RowSelectService.RS_SINGLE, false, false, 0L)]
    [InlineData(RowSelectService.RS_SINGLE, true, false, 0L)]
    [InlineData(RowSelectService.RS_SINGLE, false, true, 0L)]
    [InlineData(RowSelectService.RS_SINGLE, true, true, 0L)]
    [InlineData(RowSelectService.RS_MULTIPLE, false, false, 0L)]
    [InlineData(RowSelectService.RS_MULTIPLE, true, false, 1L)]
    [InlineData(RowSelectService.RS_MULTIPLE, false, true, 1L)]
    [InlineData(RowSelectService.RS_MULTIPLE, true, true, 0L)]
    [InlineData(3L, false, false, 0L)]
    [InlineData(3L, true, false, 1L)]
    [InlineData(3L, false, true, 1L)]
    [InlineData(3L, true, true, 0L)]
    public void OnLButtonClk_TruthTable(long style, bool shift, bool ctrl, long expected)
    {
        // No check-box column is declared, so the range propagation at :L45 always answers 0 and the
        // table isolates the modifier logic itself.
        FakeDataWindowHost host = new();
        host.AddColumn(FlagColumn, "char(1)");
        host.AddRow(OnValue);
        host.AddRow(OffValue);
        host.AddRow(OnValue);
        host.CurrentRow = 2L;

        RowSelectService service = CreateService(host, style);

        Assert.Equal(
            expected,
            service.OnLButtonClk(0L, 0L, 3L, FlagObject(host), shift, ctrl));
    }

    [Theory]
    // :L52 - the plain-click arm's re-select, again by EQUALITY.
    [InlineData(RowSelectService.RS_SINGLE, true)]
    [InlineData(RowSelectService.RS_MULTIPLE, false)]
    [InlineData(3L, true)]
    public void OnLButtonClk_PlainClick_CollapsesTheSelection(long style, bool expectReselect)
    {
        FakeDataWindowHost host = new();
        host.AddColumn(FlagColumn, "char(1)");
        host.AddRow(OnValue);
        host.AddRow(OffValue);
        host.AddRow(OnValue);
        host.CurrentRow = 1L;

        RowSelectService service = CreateService(host, style);
        host.CallLog.Clear();

        Assert.Equal(
            0L,
            service.OnLButtonClk(0L, 0L, 3L, FlagObject(host), shiftHeld: false, ctrlHeld: false));

        List<string> expected = ["SetRow[3]", "SelectRow[0, false]"];
        if (expectReselect)
        {
            expected.Add("SelectRow[3, true]");
        }

        Assert.Equal(expected, host.CallLog.Descriptions);
        Assert.False(service.HasMultiSelected());
    }

    [Fact]
    public void OnLButtonClk_BothModifiers_BehavesExactlyAsAPlainClick()
    {
        // :L49's third disjunct. The two call sequences must be identical, not merely similar.
        static List<string> Click(bool shift, bool ctrl)
        {
            FakeDataWindowHost host = new();
            host.AddColumn(FlagColumn, "char(1)");
            host.AddRow(OnValue);
            host.AddRow(OffValue);
            host.AddRow(OnValue);
            host.CurrentRow = 1L;

            RowSelectService service = CreateService(host, RowSelectService.RS_MULTIPLE);
            host.CallLog.Clear();
            service.OnLButtonClk(0L, 0L, 3L, FlagObject(host), shift, ctrl);
            return [.. host.CallLog.Descriptions];
        }

        Assert.Equal(Click(shift: false, ctrl: false), Click(shift: true, ctrl: true));
    }

    [Fact]
    public void OnLButtonClk_WhenTheRowMoveIsRefused_PreventsAndLeavesTheSelectionAlone()
    {
        // :L50 - and the refusal is detected by RE-READING GetRow(), not by trusting SetRow's code.
        FakeDataWindowHost host = new();
        host.AddColumn(FlagColumn, "char(1)");
        host.AddRow(OnValue);
        host.AddRow(OffValue);
        host.CurrentRow = 1L;
        host.SetRowMovesCurrentRow = false;

        RowSelectService service = CreateService(host, RowSelectService.RS_SINGLE);
        host.CallLog.Clear();

        Assert.Equal(
            1L,
            service.OnLButtonClk(0L, 0L, 2L, FlagObject(host), shiftHeld: false, ctrlHeld: false));

        // Only the attempted move was recorded; no selection call followed it.
        Assert.Equal(["SetRow[2]"], host.CallLog.Descriptions);
    }

    [Fact]
    public void OnLButtonClk_WhenAlreadyOnTheClickedRow_DoesNotCallSetRow()
    {
        // :L121-L126 - the already-there arm answers true without touching the host, because an
        // unnecessary move would raise the row-change event this service subscribes to.
        FakeDataWindowHost host = new();
        host.AddColumn(FlagColumn, "char(1)");
        host.AddRow(OnValue);
        host.AddRow(OffValue);
        host.CurrentRow = 2L;

        RowSelectService service = CreateService(host, RowSelectService.RS_SINGLE);
        host.CallLog.Clear();

        Assert.Equal(
            0L,
            service.OnLButtonClk(0L, 0L, 2L, FlagObject(host), shiftHeld: false, ctrlHeld: false));

        Assert.DoesNotContain("SetRow", host.CallLog.Members);
    }

    // ==========================================================================================
    //  THE SHIFT RANGE GESTURE - :L61-L75
    // ==========================================================================================

    [Fact]
    public void Shift_SelectsDownwardRangeInclusiveOfBothEnds()
    {
        // :L64-L68 - clicked row BELOW the anchor, walked upward by decrement.
        FakeDataWindowHost host = new();
        host.AddColumn(FlagColumn, "char(1)");
        for (int index = 0; index < 6; index++)
        {
            host.AddRow(OnValue);
        }

        host.CurrentRow = 2L;
        RowSelectService service = CreateService(host, RowSelectService.RS_MULTIPLE);
        host.CallLog.Clear();

        Assert.Equal(
            1L,
            service.OnLButtonClk(0L, 0L, 5L, FlagObject(host), shiftHeld: true, ctrlHeld: false));

        Assert.Equal(
            [
                "SetRedraw[false]",
                "SelectRow[0, false]",
                "SelectRow[5, true]",
                "SelectRow[4, true]",
                "SelectRow[3, true]",
                "SelectRow[2, true]",
                "SetRedraw[true]",
            ],
            host.CallLog.Descriptions);

        Assert.Equal([2L, 3L, 4L, 5L], host.SelectedRows);
        Assert.True(service.HasMultiSelected());
    }

    [Fact]
    public void Shift_SelectsUpwardRangeInclusiveOfBothEnds()
    {
        // :L70-L73 - clicked row ABOVE the anchor, walked downward by increment.
        FakeDataWindowHost host = new();
        host.AddColumn(FlagColumn, "char(1)");
        for (int index = 0; index < 6; index++)
        {
            host.AddRow(OnValue);
        }

        host.CurrentRow = 5L;
        RowSelectService service = CreateService(host, RowSelectService.RS_MULTIPLE);
        host.CallLog.Clear();

        Assert.Equal(
            1L,
            service.OnLButtonClk(0L, 0L, 2L, FlagObject(host), shiftHeld: true, ctrlHeld: false));

        Assert.Equal(
            [
                "SetRedraw[false]",
                "SelectRow[0, false]",
                "SelectRow[2, true]",
                "SelectRow[3, true]",
                "SelectRow[4, true]",
                "SelectRow[5, true]",
                "SetRedraw[true]",
            ],
            host.CallLog.Descriptions);
    }

    [Fact]
    public void Shift_OnTheAnchorRowItself_SelectsThatOneRowOnce()
    {
        // :L64's `row > nFocusRow` is false when the two are equal, so the else arm runs and its
        // `row <= nFocusRow` condition admits exactly one iteration.
        FakeDataWindowHost host = new();
        host.AddColumn(FlagColumn, "char(1)");
        host.AddRow(OnValue);
        host.AddRow(OffValue);
        host.AddRow(OnValue);
        host.CurrentRow = 2L;

        RowSelectService service = CreateService(host, RowSelectService.RS_MULTIPLE);
        host.CallLog.Clear();

        Assert.Equal(
            1L,
            service.OnLButtonClk(0L, 0L, 2L, FlagObject(host), shiftHeld: true, ctrlHeld: false));

        Assert.Equal(
            ["SetRedraw[false]", "SelectRow[0, false]", "SelectRow[2, true]", "SetRedraw[true]"],
            host.CallLog.Descriptions);
    }

    // ==========================================================================================
    //  THE CONTROL TOGGLE GESTURE - :L76-L81
    // ==========================================================================================

    [Fact]
    public void Ctrl_FirstClick_ReselectsTheAnchorBeforeTogglingTheClickedRow()
    {
        // :L77-L78 - without this the anchor row would silently drop out of the selection, because a
        // DataWindow's highlight of the current row is not a selection.
        FakeDataWindowHost host = new();
        host.AddColumn(FlagColumn, "char(1)");
        host.AddRow(OnValue);
        host.AddRow(OffValue);
        host.AddRow(OnValue);
        host.CurrentRow = 1L;

        RowSelectService service = CreateService(host, RowSelectService.RS_MULTIPLE);
        host.CallLog.Clear();

        Assert.Equal(
            1L,
            service.OnLButtonClk(0L, 0L, 3L, FlagObject(host), shiftHeld: false, ctrlHeld: true));

        Assert.Equal(
            ["SelectRow[1, true]", "SelectRow[3, true]"],
            host.CallLog.Descriptions);
        Assert.Equal([1L, 3L], host.SelectedRows);
        Assert.True(service.HasMultiSelected());
    }

    [Fact]
    public void Ctrl_OnTheAnchorRow_DoesNotReselectTheAnchor()
    {
        // :L77's `nFocusRow <> row` guard stops the anchor being selected and then toggled off by the
        // same click.
        FakeDataWindowHost host = new();
        host.AddColumn(FlagColumn, "char(1)");
        host.AddRow(OnValue);
        host.AddRow(OffValue);
        host.CurrentRow = 2L;

        RowSelectService service = CreateService(host, RowSelectService.RS_MULTIPLE);
        host.CallLog.Clear();

        Assert.Equal(
            1L,
            service.OnLButtonClk(0L, 0L, 2L, FlagObject(host), shiftHeld: false, ctrlHeld: true));

        Assert.Equal(["SelectRow[2, true]"], host.CallLog.Descriptions);
    }

    [Fact]
    public void Ctrl_SecondClick_DoesNotReselectTheAnchorAgain()
    {
        // :L77's `Not _bHasMultiSelected` half - the anchor is re-selected on the FIRST Control-click
        // of a run only.
        FakeDataWindowHost host = new();
        host.AddColumn(FlagColumn, "char(1)");
        for (int index = 0; index < 4; index++)
        {
            host.AddRow(OnValue);
        }

        host.CurrentRow = 1L;
        RowSelectService service = CreateService(host, RowSelectService.RS_MULTIPLE);

        service.OnLButtonClk(0L, 0L, 2L, FlagObject(host), shiftHeld: false, ctrlHeld: true);
        Assert.True(service.HasMultiSelected());
        host.CallLog.Clear();

        service.OnLButtonClk(0L, 0L, 4L, FlagObject(host), shiftHeld: false, ctrlHeld: true);

        Assert.Equal(["SelectRow[4, true]"], host.CallLog.Descriptions);
    }

    [Fact]
    public void Ctrl_DeselectsAnAlreadySelectedRowAndStillMarksTheSelectionMulti()
    {
        // :L80 reads the CURRENT state, so the gesture both selects and deselects; :L83 is
        // UNCONDITIONAL, so a Control-click that emptied the selection still sets the flag.
        FakeDataWindowHost host = new();
        host.AddColumn(FlagColumn, "char(1)");
        host.AddRow(OnValue);
        host.AddRow(OffValue);
        host.CurrentRow = 2L;
        host.ArrangeSelectedRows(2L);

        RowSelectService service = CreateService(host, RowSelectService.RS_MULTIPLE);
        host.CallLog.Clear();

        Assert.Equal(
            1L,
            service.OnLButtonClk(0L, 0L, 2L, FlagObject(host), shiftHeld: false, ctrlHeld: true));

        Assert.Equal(["SelectRow[2, false]"], host.CallLog.Descriptions);
        Assert.Empty(host.SelectedRows);
        Assert.True(service.HasMultiSelected());
    }

    // ==========================================================================================
    //  RE-ENTRANCY - :L88 AND :L120-L127
    // ==========================================================================================

    [Fact]
    public void OnRowFocusChanged_RaisedFromInsideTheRowMove_ReturnsZeroAndTouchesNothing()
    {
        // :L88 - the guard is observed rather than assumed: the host calls the service back from inside
        // SetRow, exactly as a DataWindow does, and the re-entrant call must do nothing at all.
        ReentrantFakeDataWindowHost host = new();
        host.AddColumn(FlagColumn, "char(1)");
        host.AddRow(OnValue);
        host.AddRow(OffValue);
        host.CurrentRow = 1L;

        RowSelectService service = CreateService(host, RowSelectService.RS_SINGLE);
        host.Service = service;
        host.CallLog.Clear();

        Assert.Equal(
            0L,
            service.OnLButtonClk(0L, 0L, 2L, FlagObject(host), shiftHeld: false, ctrlHeld: false));

        // The callback happened...
        Assert.Equal(0L, host.ReentrantResult);

        // ...and added NOTHING to the call log: the log length seen inside the callback is the same as
        // the length after SetRow returned, so the guarded path made no host call.
        Assert.Equal(host.CallCountSeenInsideCallback, host.CallLog.IndexOf("SelectRow") switch
        {
            < 0 => host.CallCountSeenInsideCallback,
            int index => index,
        });

        // The outer click's own work still happened, in order, after the re-entrant no-op.
        Assert.Equal(
            ["SetRow[2]", "SelectRow[0, false]", "SelectRow[2, true]"],
            host.CallLog.Descriptions);
    }

    [Fact]
    public void OnRowFocusChanged_AfterTheRowMoveHasFinished_IsNoLongerGuarded()
    {
        // The flag is cleared on the linear path at :L127, so an ordinary row-change AFTER the move is
        // processed normally. This is the companion of the test above and is what proves the guard is
        // scoped to the move rather than latched permanently.
        FakeDataWindowHost host = CreateCheckBoxHost(values: [OnValue, OffValue]);
        RowSelectService service = CreateService(host, RowSelectService.RS_SINGLE);

        service.OnLButtonClk(0L, 0L, 2L, FlagObject(host), shiftHeld: false, ctrlHeld: false);
        host.CallLog.Clear();

        Assert.Equal(0L, service.OnRowFocusChanged(1L));
        Assert.Equal(
            ["SelectRow[0, false]", "SelectRow[1, true]"],
            host.CallLog.Descriptions);
    }

    // ==========================================================================================
    //  THE DOUBLE CLICK - :L113
    // ==========================================================================================

    [Fact]
    public void OnLButtonDblClk_ForwardsToTheClickHandlerVerbatim()
    {
        static (long Result, List<string> Calls) Invoke(bool doubleClick)
        {
            FakeDataWindowHost host = new();
            host.AddColumn(FlagColumn, "char(1)");
            host.AddRow(OnValue);
            host.AddRow(OffValue);
            host.AddRow(OnValue);
            host.CurrentRow = 1L;

            RowSelectService service = CreateService(host, RowSelectService.RS_MULTIPLE);
            host.CallLog.Clear();

            IDataWindowObject dwo = FlagObject(host);
            long result = doubleClick
                ? service.OnLButtonDblClk(7L, 8L, 3L, dwo, shiftHeld: false, ctrlHeld: true)
                : service.OnLButtonClk(7L, 8L, 3L, dwo, shiftHeld: false, ctrlHeld: true);

            return (result, [.. host.CallLog.Descriptions]);
        }

        (long Result, List<string> Calls) single = Invoke(doubleClick: false);
        (long Result, List<string> Calls) doubled = Invoke(doubleClick: true);

        Assert.Equal(single.Result, doubled.Result);
        Assert.Equal(single.Calls, doubled.Calls);
    }

    // ==========================================================================================
    //  THE RANGE CHECK-BOX PROPAGATION - :L184-L263
    //  EIGHT PRECONDITIONS, IN THE ORACLE'S ORDER
    // ==========================================================================================

    public static TheoryData<string, Action<FakeDataWindowHost>> FailingGuards =>
        new()
        {
            // :L191 - the DataWindow itself must be editable.
            { "IsEditable", host => host.ReadOnly = "yes" },

            // :L192 - via the tab sequence, one of IsColumnEditable's four guards.
            {
                "IsColumnEditable",
                host => host.Column(FlagColumn).TabSequence = "32766"
            },

            // :L193 - the clicked row must itself be selected.
            { "IsSelected", host => host.ArrangeSelectedRows(2L, 3L, 4L) },

            // :L195 - the edit style must be a check box, compared CASE-SENSITIVELY.
            { "EditStyleIsCheckBox", host => host.Column(FlagColumn).EditStyle = "CheckBox" },

            // :L197 - the invalid-expression sentinel for the "on" value.
            { "CheckBoxOnSentinelBang", host => host.Column(FlagColumn).CheckBoxOn = "!" },

            // :L197 - and the undetermined sentinel.
            { "CheckBoxOnSentinelQuery", host => host.Column(FlagColumn).CheckBoxOn = "?" },

            // :L199 - both sentinels again, for the "off" value.
            { "CheckBoxOffSentinelBang", host => host.Column(FlagColumn).CheckBoxOff = "!" },
            { "CheckBoxOffSentinelQuery", host => host.Column(FlagColumn).CheckBoxOff = "?" },

            // :L201 - the clicked CELL must not be protected, which is a DIFFERENT test from the column
            // one inside guard 2. Reaching guard 8 requires a protect property that is NOT protected at
            // COLUMN level [n_cst_dwsvc.sru:L391, evaluated at row 0] but IS at ITEM level [:L241,
            // evaluated at the row] - otherwise guard 2 short-circuits first and guard 8 is never
            // reached. A per-row property EXPRESSION is exactly that, and arranging one here also
            // exercises the tab-split composition: the taught value carries the CLOSING quote and the
            // implementation prepends only the OPENING one, so the composed call is
            // `Evaluate("protectexp",<row>)` rather than `Evaluate("protectexp"",<row>)`.
            {
                "IsItemProtected",
                host =>
                {
                    host.Column(FlagColumn).SetProperty("protect", "0\tprotectexp\"");
                    host.DescribeOverride = property => property switch
                    {
                        "Evaluate(\"protectexp\",0)" => "0",
                        "Evaluate(\"protectexp\",1)" => "1",
                        _ => null,
                    };
                }
            },
        };

    [Theory]
    [MemberData(nameof(FailingGuards))]
    public void Propagation_WhenAnyPreconditionFails_DoesNothingAndFallsThroughToThePlainClick(
        string guard,
        Action<FakeDataWindowHost> arrangeFailure)
    {
        Assert.False(string.IsNullOrEmpty(guard));

        (FakeDataWindowHost host, RowSelectService service) = ArrangePropagation(
            "char(1)",
            [OnValue, OnValue, OnValue, OnValue]);

        arrangeFailure(host);
        host.CallLog.Clear();

        // :L46 answers 0, so the click falls through to arm 3 and reports 0 rather than 1.
        Assert.Equal(
            0L,
            service.OnLButtonClk(0L, 0L, 1L, FlagObject(host), shiftHeld: false, ctrlHeld: false));

        Assert.DoesNotContain("SetItem", host.CallLog.Members);
        Assert.DoesNotContain("SetRedraw", host.CallLog.Members);
        Assert.Null(service.PendingError);
    }

    [Fact]
    public void Propagation_WhenNoModifierGestureProducedTheSelection_DoesNothing()
    {
        // :L194 - HasMultiSelected() is the fourth guard, and it cannot be arranged through the host,
        // so it gets its own test: a service that has performed no gesture refuses the propagation even
        // with everything else in place.
        FakeDataWindowHost host = CreateCheckBoxHost("char(1)", OnValue, OnValue, OnValue, OnValue);
        RowSelectService service = CreateService(host, RowSelectService.RS_MULTIPLE);
        host.ArrangeSelectedRows(1L, 2L, 3L, 4L);
        host.CallLog.Clear();

        Assert.False(service.HasMultiSelected());
        Assert.Equal(
            0L,
            service.OnLButtonClk(0L, 0L, 1L, FlagObject(host), shiftHeld: false, ctrlHeld: false));

        Assert.DoesNotContain("SetItem", host.CallLog.Members);
    }

    // ==========================================================================================
    //  THE RANGE CHECK-BOX PROPAGATION - THE THREE TYPE ARMS AND THE WALK
    // ==========================================================================================

    [Fact]
    public void Propagation_DefaultArm_TurnsTheWholeSelectionOffAndSkipsWhatAlreadyMatches()
    {
        // :L217 the clicked cell reads "Y", so :L220 picks the OFF value as the target; :L236 then skips
        // row 3, which already holds it. A char column takes the DEFAULT arm at :L216 and :L251.
        (FakeDataWindowHost host, RowSelectService service) = ArrangePropagation(
            "char(1)",
            [OnValue, OnValue, OffValue, OnValue]);

        Assert.Equal(
            1L,
            service.OnLButtonClk(0L, 0L, 1L, FlagObject(host), shiftHeld: false, ctrlHeld: false));

        Assert.Equal(
            [
                // :L242-L245 - suppressed LAZILY, on the first row actually written, and once only.
                "Event OnDoItemChange[1, dwo:flag, \"N\"]",
                "SetRedraw[false]",
                "SetItem(string?)[1, \"flag\", \"N\"]",
                "Event OnDoItemChanged[1, dwo:flag]",
                "Event OnDoItemChange[2, dwo:flag, \"N\"]",
                "SetItem(string?)[2, \"flag\", \"N\"]",
                "Event OnDoItemChanged[2, dwo:flag]",

                // Row 3 already holds "N" and is skipped at :L236 - no event, no write.
                "Event OnDoItemChange[4, dwo:flag, \"N\"]",
                "SetItem(string?)[4, \"flag\", \"N\"]",
                "Event OnDoItemChanged[4, dwo:flag]",

                // :L256-L258
                "SetRedraw[true]",
            ],
            host.CallLog.Descriptions);

        Assert.Null(service.PendingError);
    }

    [Fact]
    public void Propagation_DefaultArm_TurnsTheSelectionOnWhenTheClickedCellIsOff()
    {
        // The direction is decided by the CLICKED row's current value [:L217-L221], so an "off" cell
        // turns the whole selection on.
        (FakeDataWindowHost host, RowSelectService service) = ArrangePropagation(
            "char(1)",
            [OffValue, OffValue, OffValue, OffValue]);

        Assert.Equal(
            1L,
            service.OnLButtonClk(0L, 0L, 1L, FlagObject(host), shiftHeld: false, ctrlHeld: false));

        Assert.Equal(4, host.CallLog.CountOf("SetItem(string?)"));
        Assert.Contains("SetItem(string?)[1, \"flag\", \"Y\"]", host.CallLog.Descriptions);
    }

    [Fact]
    public void Propagation_NullClickedCell_TurnsTheSelectionOn()
    {
        // :L217's shape is `if <value> = sOn then ... else sVal = sOn`, and a null is not equal to the
        // "on" value - so a cell that is neither on nor off is turned ON. AAP 0.4.5.4 forbids collapsing
        // the null, and this is the site where it decides the outcome.
        (FakeDataWindowHost host, RowSelectService service) = ArrangePropagation(
            "char(1)",
            [null, OffValue, OffValue, OffValue]);

        Assert.Equal(
            1L,
            service.OnLButtonClk(0L, 0L, 1L, FlagObject(host), shiftHeld: false, ctrlHeld: false));

        Assert.Contains("SetItem(string?)[1, \"flag\", \"Y\"]", host.CallLog.Descriptions);
    }

    [Fact]
    public void Propagation_DecimalArm_ReadsAndWritesADecimal()
    {
        // :L204-L209 for the read and :L247-L248 for the write - `Dec(sVal)`. The column type reaches the
        // arm through the ported ConvertColumnType's five-character prefix match on "decim".
        FakeDataWindowHost host = CreateCheckBoxHost("decimal(2)", 1m, 1m, 0m, 1m);
        host.Column(FlagColumn).CheckBoxOn = "1";
        host.Column(FlagColumn).CheckBoxOff = "0";

        RowSelectService service = CreateService(host, RowSelectService.RS_MULTIPLE);
        host.ArrangeSelectedRows();
        service.OnLButtonClk(0L, 0L, 1L, FlagObject(host), shiftHeld: false, ctrlHeld: true);
        host.ArrangeSelectedRows(1L, 2L, 3L, 4L);
        host.CallLog.Clear();

        Assert.Equal(
            1L,
            service.OnLButtonClk(0L, 0L, 1L, FlagObject(host), shiftHeld: false, ctrlHeld: false));

        // Rows 1, 2 and 4 held 1 and are written; row 3 already held 0 and is skipped.
        Assert.Equal(3, host.CallLog.CountOf("SetItem(decimal?)"));
        Assert.Equal(0, host.CallLog.CountOf("SetItem(string?)"));
        Assert.Contains("SetItem(decimal?)[1, \"flag\", 0]", host.CallLog.Descriptions);
    }

    [Fact]
    public void Propagation_IntegerArm_ReadsANumberAndWritesALong()
    {
        // :L210-L215 reads GetItemNumber - A DOUBLE - while :L249-L250 writes `Long(sVal)`. The width
        // mismatch on one arm is the legacy's and is reproduced; this test is what pins it.
        FakeDataWindowHost host = CreateCheckBoxHost("long", 1L, 1L, 0L, 1L);
        host.Column(FlagColumn).CheckBoxOn = "1";
        host.Column(FlagColumn).CheckBoxOff = "0";

        RowSelectService service = CreateService(host, RowSelectService.RS_MULTIPLE);
        host.ArrangeSelectedRows();
        service.OnLButtonClk(0L, 0L, 1L, FlagObject(host), shiftHeld: false, ctrlHeld: true);
        host.ArrangeSelectedRows(1L, 2L, 3L, 4L);
        host.CallLog.Clear();

        Assert.Equal(
            1L,
            service.OnLButtonClk(0L, 0L, 1L, FlagObject(host), shiftHeld: false, ctrlHeld: false));

        Assert.Equal(3, host.CallLog.CountOf("SetItem(long?)"));
        Assert.Contains("SetItem(long?)[1, \"flag\", 0]", host.CallLog.Descriptions);
    }

    [Fact]
    public void Propagation_SkipsInvisibleAndProtectedCells()
    {
        // :L227 and :L229 - both are per-ITEM tests, so a per-row property expression can exclude one row
        // from a propagation the rest of the selection takes.
        (FakeDataWindowHost host, RowSelectService service) = ArrangePropagation(
            "char(1)",
            [OnValue, OnValue, OnValue, OnValue]);

        // The fake resolves both properties per column, which is enough to prove the two guards are
        // consulted inside the walk rather than only for the clicked row.
        host.Column(FlagColumn).SetProperty("visible", "0");
        host.CallLog.Clear();

        Assert.Equal(
            1L,
            service.OnLButtonClk(0L, 0L, 1L, FlagObject(host), shiftHeld: false, ctrlHeld: false));

        // Every row was skipped, so nothing was written and redraw was never suppressed - but the method
        // still answers 1 because it got past all eight preconditions.
        Assert.DoesNotContain("SetItem", host.CallLog.Members);
        Assert.DoesNotContain("SetRedraw", host.CallLog.Members);
    }

    [Fact]
    public void Propagation_SuppressesRedrawLazilyAndBracketsItExactlyOnce()
    {
        // :L242-L245's laziness and :L256-L258's matching guard. Only the FIRST row actually written
        // suppresses repainting, and the restore happens once however many rows follow.
        (FakeDataWindowHost host, RowSelectService service) = ArrangePropagation(
            "char(1)",
            [OnValue, OffValue, OffValue, OffValue]);

        Assert.Equal(
            1L,
            service.OnLButtonClk(0L, 0L, 1L, FlagObject(host), shiftHeld: false, ctrlHeld: false));

        // Only row 1 differs from the target "N", so exactly one write happened...
        Assert.Equal(1, host.CallLog.CountOf("SetItem(string?)"));

        // ...and redraw was bracketed exactly once, in order.
        Assert.Equal(
            [
                "Event OnDoItemChange[1, dwo:flag, \"N\"]",
                "SetRedraw[false]",
                "SetItem(string?)[1, \"flag\", \"N\"]",
                "Event OnDoItemChanged[1, dwo:flag]",
                "SetRedraw[true]",
            ],
            host.CallLog.Descriptions);
    }

    // ==========================================================================================
    //  THE REFUSAL PATH AND THE STRUCTURED ERROR - :L238-L241
    // ==========================================================================================

    [Fact]
    public void Propagation_WhenRefused_StopsTheWalkButStillAnswersOne()
    {
        // *** DEFECT PRESERVED - :L240 BREAKS, :L259 STILL RETURNS 1 ***
        // The refusal path and the exhausted-loop path land on the SAME return, so a refused propagation
        // is indistinguishable from a successful one at the call site - and the click therefore still
        // prevents the DataWindow's own handling, which means THE CLICKED CELL IS NOT TOGGLED EITHER.
        (FakeDataWindowHost host, RowSelectService service) = ArrangePropagation(
            "char(1)",
            [OnValue, OnValue, OnValue, OnValue]);

        // Accept row 1, refuse row 2.
        host.DoItemChangeHandler = (row, _, _) => row >= 2L ? 1L : 0L;

        Assert.Equal(
            1L,
            service.OnLButtonClk(0L, 0L, 1L, FlagObject(host), shiftHeld: false, ctrlHeld: false));

        Assert.Equal(
            [
                "Event OnDoItemChange[1, dwo:flag, \"N\"]",
                "SetRedraw[false]",
                "SetItem(string?)[1, \"flag\", \"N\"]",
                "Event OnDoItemChanged[1, dwo:flag]",

                // Refused here. The walk stops, so rows 3 and 4 are never visited.
                "Event OnDoItemChange[2, dwo:flag, \"N\"]",

                // :L256-L258 still restores repainting, because a write did happen before the refusal.
                "SetRedraw[true]",
            ],
            host.CallLog.Descriptions);
    }

    [Fact]
    public void Propagation_WhenRefused_EmitsTheStructuredErrorWithEveryCompositionInput()
    {
        // :L239, with no localization provider installed - the SILENT PASSTHROUGH fallback, so the
        // composed text is the oracle's own Chinese source text.
        (FakeDataWindowHost host, RowSelectService service) = ArrangePropagation(
            "char(1)",
            [OnValue, OnValue, OnValue, OnValue]);

        host.DoItemChangeHandler = (row, _, _) => row == 3L ? 42L : 0L;

        Assert.Equal(
            1L,
            service.OnLButtonClk(0L, 0L, 1L, FlagObject(host), shiftHeld: false, ctrlHeld: false));

        RowSelectRejectionError error = Assert.IsType<RowSelectRejectionError>(service.PendingError);

        // The exact message: Sprintf over the FIRST lookup, "\n", the SECOND lookup, then "!".
        Assert.Equal("第3行\n修改数据被拒绝!", error.Text);

        // Both keys verbatim - they are the lookup keys, not translations of them.
        Assert.Equal("第{}行", error.RowFormatTemplate);
        Assert.Equal("修改数据被拒绝", error.RejectionMessageKey);

        // The category, as the NAMED constant's value rather than the literal it evaluates to.
        Assert.Equal(Categories.CAT_DWSVC, error.LocalizationCategory);

        // The Sprintf argument, preserved as data. It is the row the WALK had reached [:L224], not the
        // row that was clicked - and the two differ here, which is what makes the assertion meaningful.
        Assert.Equal(3L, error.Row);

        // The severity, the legacy StopSign!.
        Assert.Equal(RowSelectMessageIcon.StopSign, error.Icon);

        // These two messages DO route through localization, unlike the column-expression engine's 28.
        Assert.True(error.Localized);
    }

    [Fact]
    public void Propagation_WhenRefused_ComposesFromTwoSeparateLocalizationLookups()
    {
        // The two fragments are translated INDEPENDENTLY [:L239], and Sprintf is applied to the
        // TRANSLATION rather than to the key - so a provider whose translation moves the placeholder
        // still gets it filled in the right place.
        RecordingI18nProvider provider = new RecordingI18nProvider()
            .Teach("第{}行", "row {} of the grid")
            .Teach("修改数据被拒绝", "the change was refused");

        I18n i18n = new();
        Assert.Equal(RetCode.OK, i18n.I18N(provider));

        (FakeDataWindowHost host, RowSelectService service) = ArrangePropagation(
            "char(1)",
            [OnValue, OnValue, OnValue, OnValue],
            RowSelectService.RS_MULTIPLE,
            i18n);

        host.DoItemChangeHandler = (row, _, _) => row == 2L ? 1L : 0L;
        provider.Requests.Clear();

        service.OnLButtonClk(0L, 0L, 1L, FlagObject(host), shiftHeld: false, ctrlHeld: false);

        RowSelectRejectionError error = Assert.IsType<RowSelectRejectionError>(service.PendingError);
        Assert.Equal("row 2 of the grid\nthe change was refused!", error.Text);

        // Exactly two lookups, in the oracle's order, both under CAT_DWSVC.
        Assert.Equal(2, provider.Requests.Count);
        Assert.Equal("第{}行", provider.Requests[0].Text);
        Assert.Equal("修改数据被拒绝", provider.Requests[1].Text);
        Assert.All(provider.Requests, request => Assert.Equal(Categories.CAT_DWSVC, request.Category));

        // The keys travel as data untouched by the translation.
        Assert.Equal("第{}行", error.RowFormatTemplate);
        Assert.Equal("修改数据被拒绝", error.RejectionMessageKey);
    }

    [Fact]
    public void PendingError_IsClearedByTheNextClick()
    {
        (FakeDataWindowHost host, RowSelectService service) = ArrangePropagation(
            "char(1)",
            [OnValue, OnValue, OnValue, OnValue]);

        host.DoItemChangeHandler = (row, _, _) => row == 2L ? 1L : 0L;
        service.OnLButtonClk(0L, 0L, 1L, FlagObject(host), shiftHeld: false, ctrlHeld: false);
        Assert.NotNull(service.PendingError);

        // A click that cannot produce an error must not leave the old one visible - not even the
        // no-row early-out at :L42, which is cleared before it returns.
        Assert.Equal(0L, service.OnLButtonClk(0L, 0L, 0L, null!, shiftHeld: false, ctrlHeld: false));
        Assert.Null(service.PendingError);
    }

    [Fact]
    public void PendingError_IsClearedByADoubleClickToo()
    {
        (FakeDataWindowHost host, RowSelectService service) = ArrangePropagation(
            "char(1)",
            [OnValue, OnValue, OnValue, OnValue]);

        host.DoItemChangeHandler = (row, _, _) => row == 2L ? 1L : 0L;
        service.OnLButtonClk(0L, 0L, 1L, FlagObject(host), shiftHeld: false, ctrlHeld: false);
        Assert.NotNull(service.PendingError);

        // :L113 forwards, so the clearing travels with it.
        service.OnLButtonDblClk(0L, 0L, 0L, null!, shiftHeld: false, ctrlHeld: false);
        Assert.Null(service.PendingError);
    }

    // ==========================================================================================
    //  OnEnable - :L273-L287
    // ==========================================================================================

    [Fact]
    public void Enable_SubscribesExactlyTheThreeOracleTopics()
    {
        // :L274-L276. THE FOURTH EVENT IS NOT SUBSCRIBED: OnFiltered is invoked directly by the host's
        // Filter() override [se_cst_dw.sru:L403-L414], so a fourth subscription would be wrong.
        EventBroker broker = new();
        FakeDataWindowHost host = CreateCheckBoxHost("char(1)", OnValue, OffValue);
        RowSelectService service = CreateService(host, RowSelectService.RS_SINGLE);

        Assert.Equal(RetCode.OK, service.SetEnabled(true));

        Assert.True(host.Eventful.IsSubscribed("clicked"));
        Assert.True(host.Eventful.IsSubscribed("doubleclicked"));
        Assert.True(host.Eventful.IsSubscribed("rowfocuschanged"));

        // None of the other nine se_cst_dw topics is touched.
        foreach (string untouched in new[]
                 {
                     "rowfocuschanging",
                     "itemfocuschanged",
                     "0-itemchanged",
                     "1-editchanged",
                     "lbuttonup",
                     "rbuttondown",
                     "rbuttonup",
                     "getfocus",
                     "losefocus",
                 })
        {
            Assert.False(host.Eventful.IsSubscribed(untouched));
        }

        Assert.False(broker.IsSubscribed("clicked"));
    }

    [Fact]
    public void Enable_WiresTheClickHandlerSoTheBrokerCanDispatchIt()
    {
        // The subscription is resolved BY NAME, so this is what proves the handler name still matches -
        // the broker answers E_EVENT_NOT_FOUND for a name it cannot resolve rather than failing loudly.
        FakeDataWindowHost host = CreateCheckBoxHost("char(1)", OnValue, OffValue, OnValue);
        host.CurrentRow = 1L;
        RowSelectService service = CreateService(host, RowSelectService.RS_SINGLE);
        service.SetEnabled(true);
        host.CallLog.Clear();

        // se_cst_dw.sru:L148 triggers with FOUR arguments; the ported handler declares six, so the two
        // modifier parameters arrive as their PowerScript initial value - false - and the dispatch reads
        // as a plain click. That is the correct reading: the broker payload carries no modifier state.
        object? dispatched = host.Eventful.Trigger("clicked", 0L, 0L, 3L, FlagObject(host));

        Assert.Equal(0L, Assert.IsType<long>(dispatched));
        Assert.Equal(
            ["SetRow[3]", "SelectRow[0, false]", "SelectRow[3, true]"],
            host.CallLog.Descriptions);
    }

    [Fact]
    public void Enable_WiresTheDoubleClickAndRowFocusHandlersToo()
    {
        FakeDataWindowHost host = CreateCheckBoxHost("char(1)", OnValue, OffValue, OnValue);
        host.CurrentRow = 1L;
        RowSelectService service = CreateService(host, RowSelectService.RS_SINGLE);
        service.SetEnabled(true);

        host.CallLog.Clear();
        host.Eventful.Trigger("doubleclicked", 0L, 0L, 2L, FlagObject(host));
        Assert.Equal(
            ["SetRow[2]", "SelectRow[0, false]", "SelectRow[2, true]"],
            host.CallLog.Descriptions);

        host.CallLog.Clear();
        host.Eventful.Trigger("rowfocuschanged", 3L);
        Assert.Equal(
            ["SelectRow[0, false]", "SelectRow[3, true]"],
            host.CallLog.Descriptions);
    }

    [Theory]
    // :L277 - EQUALITY, NOT A BIT TEST. Enabling re-selects the current row for RS_SINGLE and for the
    // COMBINED value, and not for RS_MULTIPLE.
    [InlineData(RowSelectService.RS_SINGLE, true)]
    [InlineData(RowSelectService.RS_MULTIPLE, false)]
    [InlineData(3L, true)]
    public void Enable_ReconcilesTheSelectionByEquality(long style, bool expectReselect)
    {
        FakeDataWindowHost host = CreateCheckBoxHost("char(1)", OnValue, OffValue, OnValue);
        host.CurrentRow = 3L;
        RowSelectService service = CreateService(host, style);
        host.CallLog.Clear();

        Assert.Equal(RetCode.OK, service.SetEnabled(true));

        Assert.Equal(
            expectReselect ? ["SelectRow[3, true]"] : (string[])[],
            host.CallLog.Descriptions);
    }

    [Fact]
    public void Enable_OnAnEmptyDataWindow_SelectsEveryRow()
    {
        // :L278 has NO `> 0` guard on GetRow(), unlike :L92 and :L106 which do - so an empty DataWindow
        // reports row 0 and row 0 means EVERY row. Preserved rather than guarded (constraint C-B).
        FakeDataWindowHost host = new();
        host.AddColumn(FlagColumn, "char(1)");
        host.CurrentRow = 0L;

        RowSelectService service = CreateService(host, RowSelectService.RS_SINGLE);
        host.CallLog.Clear();

        Assert.Equal(RetCode.OK, service.SetEnabled(true));
        Assert.Equal(["SelectRow[0, true]"], host.CallLog.Descriptions);
    }

    [Fact]
    public void Disable_UnsubscribesEverything_ClearsTheSelectionAndResetsTheFlag()
    {
        // :L281-L283. The unsubscribe is BY TARGET, so all three topics go at once.
        FakeDataWindowHost host = CreateCheckBoxHost("char(1)", OnValue, OffValue, OnValue);
        host.CurrentRow = 1L;
        RowSelectService service = CreateService(host, RowSelectService.RS_MULTIPLE);
        service.SetEnabled(true);

        service.OnLButtonClk(0L, 0L, 3L, FlagObject(host), shiftHeld: false, ctrlHeld: true);
        Assert.True(service.HasMultiSelected());
        host.CallLog.Clear();

        Assert.Equal(RetCode.OK, service.SetEnabled(false));

        Assert.Equal(["SelectRow[0, false]"], host.CallLog.Descriptions);
        Assert.False(service.HasMultiSelected());
        Assert.Empty(host.SelectedRows);

        Assert.False(host.Eventful.IsSubscribed("clicked"));
        Assert.False(host.Eventful.IsSubscribed("doubleclicked"));
        Assert.False(host.Eventful.IsSubscribed("rowfocuschanged"));
    }

    [Fact]
    public void SetEnabled_IsIdempotentAndThisServiceNeverVetoes()
    {
        // n_cst_dwsvc.sru:L89 - the idempotent early-out answers OK WITHOUT raising OnEnable, so a
        // repeated enable performs no subscription and no selection work; :L286 never vetoes, so the
        // first enable answers OK rather than FAILED.
        FakeDataWindowHost host = CreateCheckBoxHost("char(1)", OnValue);
        RowSelectService service = CreateService(host, RowSelectService.RS_SINGLE);

        Assert.Equal(RetCode.OK, service.SetEnabled(true));
        host.CallLog.Clear();

        Assert.Equal(RetCode.OK, service.SetEnabled(true));
        Assert.Empty(host.CallLog.Records);
        Assert.True(service.Enabled);
    }
}
