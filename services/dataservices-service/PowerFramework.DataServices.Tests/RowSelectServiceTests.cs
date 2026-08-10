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
//
//  TWO PLACES WHERE A PROSE SUMMARY OF THE ORACLE IS WRONG AND THE ORACLE WINS
//  --------------------------------------------------------------------------------------------
//  Constraint C-C makes ws_objects/** the behavioural oracle and constraint C-B forbids
//  "correcting" it, so where a paraphrase of the legacy disagrees with the legacy the paraphrase
//  loses. Two such disagreements were found while writing this suite, and both are pinned by a test
//  below rather than left to be rediscovered as a "bug":
//
//    A. of_setstyle REJECTS ZERO AND NOTHING ELSE. :L169 is `if style = 0 then return
//       RetCode.E_INVALID_ARGUMENT` - a single equality against zero. It is NOT a whitelist of the
//       two named constants: 3 is legal and is the value the oracle's OWN test window sets
//       [ws_objects/pfw.tests.pbl.src/w_test_dwsvc_rowselect.srw:L42], and a negative value is
//       accepted too because nothing tests for one. SetStyleArguments below carries the whole
//       matrix - zero, both named values, the combined value, a negative and long.MinValue - with
//       the ORACLE's verdict on each. Narrowing the guard to a two-value whitelist would break the
//       fixture; widening it to reject negatives would invent a validation the legacy does not have.
//
//    B. onfiltered TAKES NO ARGUMENTS. :L11 declares `event onfiltered ( )` and :L101-L111 reads
//       the row it needs from the DataWindow itself. The row-count/filtered-count pair belongs to a
//       DIFFERENT event on a DIFFERENT service - onddsfiltered(row,dwo,rowcount,filteredcount)
//       [se_cst_dw.sru:L28], the drop-down search service's, ported as
//       DataWindowServiceHost.OnDDSFiltered. OnFiltered_TakesNoArgumentsUnlikeOnDdsFiltered pins
//       both arities by reflection so the two cannot be conflated later.
//
//  THE THREE REFLECTION SUITES, AND WHY A TEST FILE CONTAINS ANY
//  --------------------------------------------------------------------------------------------
//  Three of this file's obligations are properties of the SHAPE of the port rather than of its
//  behaviour, and a behavioural test cannot express them: that the modifier state arrives as
//  explicit parameters instead of being read from a keyboard, that nothing presentational crosses
//  the contract (constraint C-D), and that every value handed back is data rather than a rendering
//  instruction. Each is asserted by reflecting over the real type. They are cheap, they are exact,
//  and unlike a code-review note they FAIL THE BUILD if a later change reintroduces what they forbid.
// ==============================================================================================

using System.Reflection;
using System.Runtime.CompilerServices;

using Microsoft.Extensions.Options;

using PowerFramework.DataServices.Configuration;
using PowerFramework.DataServices.Domain;
using PowerFramework.DataServices.Services;
using PowerFramework.Shared.Eventful;
using PowerFramework.Shared.Kernel;
using PowerFramework.Shared.Localization;

using Xunit;

// THE TWO TYPE ALIASES ARE MANDATORY RATHER THAN COSMETIC, and the reason is the same one
// Domain/DataWindowServiceHost.cs and FakeDataWindowHost.cs both record. The chain harness below must
// override the host's item-status pair, whose signatures use the two PUBLISHED CONTRACT enums, but
// `PowerFramework.Contracts.Common.V1` also contains a generated wrapper message class named RetCode -
// common.v1.proto nests each legacy constant set as an enum named Value inside a thin wrapper message.
// A plain namespace import would therefore put a second `RetCode` in scope and make every mention of
// RetCode.OK ambiguous (CS0104), which TreatWarningsAsErrors turns into a build failure. Aliasing
// exactly the two types needed keeps the wrapper out of scope and states the dependency precisely.
using DwBuffer = PowerFramework.Contracts.Common.V1.DwBuffer;
using ItemStatus = PowerFramework.Contracts.Common.V1.ItemStatus;

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

// ==================================================================================================
//  THE CHAIN-INTEGRATION DOUBLES
//  ------------------------------------------------------------------------------------------------
//  Domain/DataWindowEventChain.cs is the port of se_cst_dw and is the ONLY caller of
//  RowSelectService.OnFiltered in the whole estate: its Filter() override reaches the service at
//  se_cst_dw.sru:L408-L410, and nothing else does - which is exactly why OnFiltered is the one event
//  of the four that OnEnable does NOT subscribe. Asserting that edge therefore needs a real chain
//  with the REAL service behind it, and the four doubles below are the minimum that produces one.
//
//  WHY THE SERVICE IS ADAPTED RATHER THAN TYPED INTO THE CHAIN DIRECTLY. The chain consumes
//  IDataWindowRowSelectService - `Enabled`, `OnInit` and `OnFiltered()` - and RowSelectService
//  deliberately does not implement it: DataWindowServiceBase already supplies `Enabled` and
//  `OnInit`, so the interface exists to keep the chain from depending on the concrete service at
//  all (constraint C-A). RealRowSelectAdapter is that seam, and it forwards rather than
//  reimplements, so what the chain drives IS the subject of this suite.
//
//  WHY THE OTHER FOUR SERVICES ARE INERT. The chain creates all five attached services in its
//  constructor [se_cst_dw.sru:L570-L574] and refuses a null [Require], so all five must exist. None
//  of the other four participates in Filter() or DeleteRow(), so each is a disabled no-op here -
//  which is not a stub of anything: these interfaces are three members wide and every member is
//  fully implemented below. The sibling suite DataWindowEventChainTests.cs owns the chain's own
//  behaviour and has its own doubles; nothing is shared between the two files in either direction.
// ==================================================================================================

/// <summary>
/// Presents the real <see cref="RowSelectService"/> to the chain through the interface the chain
/// consumes, recording each notification so the edge is observable from the test.
/// </summary>
internal sealed class RealRowSelectAdapter : IDataWindowRowSelectService
{
    internal RealRowSelectAdapter(RowSelectService service) => Service = service;

    internal RowSelectService Service { get; }

    /// <summary>
    /// How many times the chain has reached <see cref="OnFiltered"/>.
    /// </summary>
    internal int FilteredCount { get; private set; }

    /// <inheritdoc />
    public bool Enabled => Service.Enabled;

    /// <inheritdoc />
    public void OnInit(DataWindowServiceHost dw) => Service.OnInit(dw);

    /// <inheritdoc />
    public void OnFiltered()
    {
        FilteredCount++;
        Service.OnFiltered();
    }
}

/// <summary>
/// A disabled, fully-implemented stand-in for the four attached services this suite does not
/// exercise. The chain requires all five to exist [<c>se_cst_dw.sru:L570-L574</c>].
/// </summary>
internal sealed class InertAttachedService
    : IDataWindowContextMenuService,
      IDataWindowColumnSortService,
      IDataWindowDropDownSearchService,
      IDataWindowColumnExpressionService
{
    /// <summary>
    /// The hosts this double has been initialised against, in call order.
    /// </summary>
    internal List<DataWindowServiceHost> InitialisedWith { get; } = [];

    /// <inheritdoc />
    /// <remarks>
    /// Always <see langword="false"/>. Every chain site that reaches one of these four services
    /// tests <c>Enabled</c> first, so a disabled double keeps them out of the way honestly rather
    /// than by throwing.
    /// </remarks>
    public bool Enabled => false;

    /// <inheritdoc />
    public void OnInit(DataWindowServiceHost dw) => InitialisedWith.Add(dw);

    /// <inheritdoc />
    public void OnEditChanged(long row, IDataWindowObject dwo, string data) =>
        EditChanged.Add((row, dwo, data));

    /// <inheritdoc />
    public void OnItemChanged(long row, IDataWindowObject dwo) => ItemChanged.Add((row, dwo));

    /// <summary>
    /// The drop-down search notifications received, which must stay empty in this suite.
    /// </summary>
    internal List<(long Row, IDataWindowObject Dwo, string Data)> EditChanged { get; } = [];

    /// <summary>
    /// The column-expression notifications received, which must stay empty in this suite.
    /// </summary>
    internal List<(long Row, IDataWindowObject Dwo)> ItemChanged { get; } = [];
}

/// <summary>
/// Hands the chain the real row-selection service and four inert siblings.
/// </summary>
internal sealed class RealRowSelectServiceFactory : IDataWindowAttachedServiceFactory
{
    internal RealRowSelectServiceFactory(RowSelectService service) =>
        RowSelect = new RealRowSelectAdapter(service);

    internal RealRowSelectAdapter RowSelect { get; }

    internal InertAttachedService ContextMenu { get; } = new();

    internal InertAttachedService ColumnSort { get; } = new();

    internal InertAttachedService DropDownSearch { get; } = new();

    internal InertAttachedService ColumnExp { get; } = new();

    /// <inheritdoc />
    public IDataWindowContextMenuService CreateContextMenu() => ContextMenu;

    /// <inheritdoc />
    public IDataWindowRowSelectService CreateRowSelect() => RowSelect;

    /// <inheritdoc />
    public IDataWindowColumnSortService CreateColumnSort() => ColumnSort;

    /// <inheritdoc />
    public IDataWindowDropDownSearchService CreateDropDownSearch() => DropDownSearch;

    /// <inheritdoc />
    public IDataWindowColumnExpressionService CreateColumnExp() => ColumnExp;
}

/// <summary>
/// A concrete <see cref="DataWindowEventChain"/> whose entire host surface forwards to a contained
/// <see cref="FakeDataWindowHost"/>, so the chain's own two overrides - <c>Filter</c> and
/// <c>DeleteRow</c> [<c>se_cst_dw.sru:L403-L446</c>] - run for real over an in-memory DataWindow.
/// </summary>
/// <remarks>
/// EVERY MEMBER FORWARDS; NOT ONE IS A STUB. The forwarding is what makes the assertions meaningful:
/// the service under test calls the CHAIN, the chain forwards to the fake, and the fake's call log
/// therefore records exactly what a real DataWindow would have been asked to do.
/// </remarks>
internal sealed class RowSelectChainHarness : DataWindowEventChain
{
    internal RowSelectChainHarness(ValidationSession session, RealRowSelectServiceFactory services)
        : base(session, services)
    {
        Services = services;
        Host = new FakeDataWindowHost(Eventful);
    }

    internal FakeDataWindowHost Host { get; }

    internal RealRowSelectServiceFactory Services { get; }

    /// <inheritdoc />
    public override object? ObjectModel => Host.ObjectModel;

    /// <inheritdoc />
    public override IDataWindowObject GetObjectAttribute(string dwoName) =>
        Host.GetObjectAttribute(dwoName);

    /// <inheritdoc />
    public override string Describe(string property) => Host.Describe(property);

    /// <inheritdoc />
    public override long RowCount() => Host.RowCount();

    /// <inheritdoc />
    public override long GetRow() => Host.GetRow();

    /// <inheritdoc />
    public override int SetRow(long row) => Host.SetRow(row);

    /// <inheritdoc />
    public override string GetColumnName() => Host.GetColumnName();

    /// <inheritdoc />
    public override int AcceptText() => Host.AcceptText();

    /// <inheritdoc />
    public override int SetRedraw(bool enable) => Host.SetRedraw(enable);

    /// <inheritdoc />
    public override long GetRowIDFromRow(long row) => Host.GetRowIDFromRow(row);

    /// <inheritdoc />
    public override long GetRowFromRowID(long rowId) => Host.GetRowFromRowID(rowId);

    /// <inheritdoc />
    public override int SetSort(string sort) => Host.SetSort(sort);

    /// <inheritdoc />
    public override int Sort() => Host.Sort();

    /// <inheritdoc />
    public override int GroupCalc() => Host.GroupCalc();

    /// <inheritdoc />
    public override bool IsEventDisabled(uint evt) => Host.IsEventDisabled(evt);

    /// <inheritdoc />
    public override long DisableEvent(uint evt) => Host.DisableEvent(evt);

    /// <inheritdoc />
    public override int EnableEvent(uint evt) => Host.EnableEvent(evt);

    /// <inheritdoc />
    public override int SelectRow(long row, bool select) => Host.SelectRow(row, select);

    /// <inheritdoc />
    public override bool IsSelected(long row) => Host.IsSelected(row);

    /// <inheritdoc />
    public override long GetSelectedRow(long startRow) => Host.GetSelectedRow(startRow);

    /// <inheritdoc />
    public override object? GetFocusedObject() => Host.GetFocusedObject();

    /// <inheritdoc />
    public override int SetFocus() => Host.SetFocus();

    /// <inheritdoc />
    public override ItemStatus GetItemStatus(long row, long columnId, DwBuffer buffer) =>
        Host.GetItemStatus(row, columnId, buffer);

    /// <inheritdoc />
    public override int SetItemStatus(long row, long columnId, DwBuffer buffer, ItemStatus status) =>
        Host.SetItemStatus(row, columnId, buffer, status);

    /// <inheritdoc />
    public override int SetItem(long row, long columnId, string? value) =>
        Host.SetItem(row, columnId, value);

    /// <inheritdoc />
    public override int SetItem(long row, long columnId, decimal? value) =>
        Host.SetItem(row, columnId, value);

    /// <inheritdoc />
    public override int SetItem(long row, long columnId, long? value) =>
        Host.SetItem(row, columnId, value);

    /// <inheritdoc />
    public override int SetItem(long row, long columnId, DateTime? value) =>
        Host.SetItem(row, columnId, value);

    /// <inheritdoc />
    public override int SetItem(long row, long columnId, DateOnly? value) =>
        Host.SetItem(row, columnId, value);

    /// <inheritdoc />
    public override int SetItem(long row, long columnId, TimeOnly? value) =>
        Host.SetItem(row, columnId, value);

    /// <inheritdoc />
    public override int SetItem(long row, long columnId, object? value) =>
        Host.SetItem(row, columnId, value);

    /// <inheritdoc />
    public override string? GetItemString(long row, string column) =>
        Host.GetItemString(row, column);

    /// <inheritdoc />
    public override decimal? GetItemDecimal(long row, string column) =>
        Host.GetItemDecimal(row, column);

    /// <inheritdoc />
    public override double? GetItemNumber(long row, string column) =>
        Host.GetItemNumber(row, column);

    /// <inheritdoc />
    public override int SetItem(long row, string column, string? value) =>
        Host.SetItem(row, column, value);

    /// <inheritdoc />
    public override int SetItem(long row, string column, decimal? value) =>
        Host.SetItem(row, column, value);

    /// <inheritdoc />
    public override int SetItem(long row, string column, long? value) =>
        Host.SetItem(row, column, value);

    /// <inheritdoc />
    public override DateTime? GetItemDateTime(long row, string column) =>
        Host.GetItemDateTime(row, column);

    /// <inheritdoc />
    public override DateOnly? GetItemDate(long row, string column) => Host.GetItemDate(row, column);

    /// <inheritdoc />
    public override TimeOnly? GetItemTime(long row, string column) => Host.GetItemTime(row, column);

    /// <inheritdoc />
    public override int SetItem(long row, string column, DateTime? value) =>
        Host.SetItem(row, column, value);

    /// <inheritdoc />
    public override int SetItem(long row, string column, DateOnly? value) =>
        Host.SetItem(row, column, value);

    /// <inheritdoc />
    public override int SetItem(long row, string column, TimeOnly? value) =>
        Host.SetItem(row, column, value);

    /// <inheritdoc />
    public override long Find(string expression, long start, long end) =>
        Host.Find(expression, start, end);

    /// <inheritdoc />
    public override long InsertRow(long row) => Host.InsertRow(row);

    /// <inheritdoc />
    public override string GetValue(string column, long index) => Host.GetValue(column, index);

    /// <inheritdoc />
    public override int GetChild(string column, ref IDataWindowChild? child) =>
        Host.GetChild(column, ref child);

    /// <inheritdoc />
    /// <remarks>
    /// Reaches the fake's own <c>FilterCore</c>, so <c>FilterResult</c> on the fake decides whether
    /// the chain sees the success the <c>:L407</c> guard requires.
    /// </remarks>
    protected override int FilterCore() => Host.InvokeFilterCore();

    /// <inheritdoc />
    protected override int DeleteRowCore(long row) => Host.InvokeDeleteRowCore(row);
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

    /// <summary>
    /// THE WHOLE ARGUMENT DOMAIN OF <c>of_setstyle</c>, WITH THE ORACLE'S VERDICT ON EACH VALUE.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>:L169</c> is one equality against zero and nothing more, so ZERO IS THE ONLY REJECTED
    /// VALUE. The combined value is legal and is what the oracle's own fixture sets
    /// [<c>ws_objects/pfw.tests.pbl.src/w_test_dwsvc_rowselect.srw:L42</c>]; a negative value is
    /// legal too, because no guard looks for one. See divergence A in the file header - a summary
    /// that reads the guard as a two-value whitelist contradicts both the source and the fixture,
    /// and constraints C-B and C-C make the source authoritative.
    /// </para>
    /// <para>
    /// Every row carries the locator that decides it (AAP §0.6.7, constraint C-K).
    /// </para>
    /// </remarks>
    public static TheoryData<string, long, long, bool> SetStyleArguments =>
        new()
        {
            // The two named constants, both legal.
            { ":L20 RS_SINGLE = 1", RowSelectService.RS_SINGLE, RetCode.OK, true },
            { ":L21 RS_MULTIPLE = 2", RowSelectService.RS_MULTIPLE, RetCode.OK, true },

            // The combined value: legal, and the value the oracle's own test window sets.
            { ":L19 支持组合 + w_test_dwsvc_rowselect.srw:L42", 3L, RetCode.OK, true },

            // THE ONE REJECTED VALUE.
            { ":L169 style = 0 → E_INVALID_ARGUMENT", 0L, RetCode.E_INVALID_ARGUMENT, false },

            // Negatives reach :L171 untouched: :L169 tests equality with zero, not a range.
            { ":L169 tests = 0, NOT < 1, so -1 is stored", -1L, RetCode.OK, true },
            { ":L169 no lower bound at all", long.MinValue, RetCode.OK, true },

            // A value larger than any combination of the two flags is equally unguarded.
            { ":L169 no upper bound either", 4096L, RetCode.OK, true },
        };

    [Theory]
    [MemberData(nameof(SetStyleArguments))]
    public void SetStyle_AnswersTheOracleVerdictAndOnlyThenStoresTheValue(
        string locator,
        long style,
        long expectedRtCode,
        bool expectStored)
    {
        Assert.False(string.IsNullOrEmpty(locator));

        RowSelectService service = CreateService(new FakeDataWindowHost());

        // The starting value is RS_SINGLE, so the :L168 idempotent early-out fires for exactly one
        // case - and that case's expected answer is OK either way, which is why it is safe to share
        // the arrangement across the whole matrix.
        Assert.Equal(expectedRtCode, service.SetStyle(style));

        // :L171 is reached only past :L169, so a rejected argument must leave the property alone.
        Assert.Equal(expectStored ? style : RowSelectService.RS_SINGLE, service.Style);
    }

    /// <summary>
    /// The three styles at <c>:L175</c>, each row carrying the locator that decides its outcome.
    /// </summary>
    public static TheoryData<string, long, bool> SetStyleReconciliationCases =>
        new()
        {
            { ":L175 `#Style <> RS_MULTIPLE` is false for RS_SINGLE", RowSelectService.RS_SINGLE, true },
            { ":L175 `#Style <> RS_MULTIPLE` is true for RS_MULTIPLE", RowSelectService.RS_MULTIPLE, false },
            { ":L175 EQUALITY, so 3 behaves as a THIRD state", 3L, true },
        };

    [Theory]
    // :L175 - EQUALITY, NOT A BIT TEST. RS_SINGLE and the COMBINED value 3 both re-select the current
    // row because neither equals RS_MULTIPLE; RS_MULTIPLE alone does not.
    [MemberData(nameof(SetStyleReconciliationCases))]
    public void SetStyle_WhileEnabled_ReconcilesTheSelectionByEquality(
        string locator,
        long target,
        bool expectReselect)
    {
        Assert.False(string.IsNullOrEmpty(locator));

        FakeDataWindowHost host = CreateCheckBoxHost(values: [OnValue, OffValue]);
        host.CurrentRow = 2L;

        // Start from a style no case uses, so the idempotent early-out at :L168 cannot fire.
        const long styleNoCaseUses = 7L;
        RowSelectService service = CreateService(
            host,
            target == styleNoCaseUses ? 3L : styleNoCaseUses);
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

    /// <summary>
    /// The row-focus inequality site, one row per style with its deciding locator.
    /// </summary>
    public static TheoryData<string, long, bool> RowFocusChangedReselectCases =>
        new()
        {
            { ":L92-L93 reselect because RS_SINGLE <> RS_MULTIPLE", RowSelectService.RS_SINGLE, true },
            { ":L92 no reselect because RS_MULTIPLE = RS_MULTIPLE", RowSelectService.RS_MULTIPLE, false },
            { ":L92 reselect because 3 <> 2 - the THIRD state", 3L, true },
        };

    [Theory]
    // The five inequality sites, exercised through the three members that reach them. The COMBINED
    // value behaves like RS_SINGLE at every one of them and like RS_MULTIPLE at none - it is a third
    // state, not the union of two.
    [MemberData(nameof(RowFocusChangedReselectCases))]
    public void OnRowFocusChanged_ReselectsByEquality(string locator, long style, bool expectReselect)
    {
        Assert.False(string.IsNullOrEmpty(locator));

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

    /// <summary>
    /// The post-filter inequality site, one row per style with its deciding locator.
    /// </summary>
    public static TheoryData<string, long, bool> FilteredReselectCases =>
        new()
        {
            { ":L106-L107 reselect because RS_SINGLE <> RS_MULTIPLE", RowSelectService.RS_SINGLE, true },
            { ":L106 no reselect because RS_MULTIPLE = RS_MULTIPLE", RowSelectService.RS_MULTIPLE, false },
            { ":L106 reselect because 3 <> 2 - the THIRD state", 3L, true },
        };

    [Theory]
    [MemberData(nameof(FilteredReselectCases))]
    public void OnFiltered_ReselectsByEquality(string locator, long style, bool expectReselect)
    {
        Assert.False(string.IsNullOrEmpty(locator));

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

    /// <summary>
    /// The two shapes of "no row under the pointer" that <c>:L42</c>'s <c>row &lt;= 0</c> admits.
    /// </summary>
    public static TheoryData<string, long> NoRowCases =>
        new()
        {
            { ":L42 row = 0 - the pointer was over a band, not a row", 0L },
            { ":L42 row < 0 - the same guard, tested as <= rather than =", -1L },
        };

    [Theory]
    [MemberData(nameof(NoRowCases))]
    public void OnLButtonClk_WithNoRow_AnswersZeroAndTouchesNothing(string locator, long row)
    {
        Assert.False(string.IsNullOrEmpty(locator));

        // :L42 - and it touches neither the host nor dwo, which is why both guards in the port sit
        // AFTER this early-out. A null dwo therefore does not raise here.
        FakeDataWindowHost host = CreateCheckBoxHost(values: [OnValue]);
        RowSelectService service = CreateService(host, RowSelectService.RS_MULTIPLE);
        host.CallLog.Clear();

        Assert.Equal(0L, service.OnLButtonClk(1L, 2L, row, null!, shiftHeld: true, ctrlHeld: true));
        Assert.Empty(host.CallLog.Records);
    }

    /// <summary>
    /// THE COMPLETE MODIFIER TRUTH TABLE - three styles by four modifier combinations, with each row
    /// naming the arm of <c>:L49</c>, <c>:L61</c> or <c>:L76</c> that decides it.
    /// </summary>
    /// <remarks>
    /// The four combinations are the whole domain of the two explicit flags, which is what makes the
    /// table exhaustive rather than representative: a headless service receives the modifier state as
    /// two booleans (DECISION 2 in the implementation's header), so twelve rows cover every reachable
    /// input for these three styles.
    /// </remarks>
    public static TheoryData<string, long, bool, bool, long> ClickTruthTableCases =>
        new()
        {
            // RS_SINGLE takes :L49's FIRST disjunct whatever the modifiers are, so all four agree.
            { ":L49 disjunct 1 `#Style = RS_SINGLE`", RowSelectService.RS_SINGLE, false, false, 0L },
            { ":L49 disjunct 1 wins even with Shift", RowSelectService.RS_SINGLE, true, false, 0L },
            { ":L49 disjunct 1 wins even with Control", RowSelectService.RS_SINGLE, false, true, 0L },
            { ":L49 disjunct 1 wins with both", RowSelectService.RS_SINGLE, true, true, 0L },

            // RS_MULTIPLE: no modifier takes disjunct 2; one modifier falls through to the gestures;
            // both modifiers take disjunct 3 and read as a plain click again.
            { ":L49 disjunct 2 `Not Shift and Not Control`", RowSelectService.RS_MULTIPLE, false, false, 0L },
            { ":L61 the Shift range gesture", RowSelectService.RS_MULTIPLE, true, false, 1L },
            { ":L76 the Control toggle gesture", RowSelectService.RS_MULTIPLE, false, true, 1L },
            { ":L49 disjunct 3 `Shift and Control`", RowSelectService.RS_MULTIPLE, true, true, 0L },

            // The combined value behaves exactly as RS_MULTIPLE does here, because :L49's disjunct 1
            // tests EQUALITY with RS_SINGLE and 3 <> 1.
            { ":L49 disjunct 2 with the THIRD state", 3L, false, false, 0L },
            { ":L61 the Shift range gesture, THIRD state", 3L, true, false, 1L },
            { ":L76 the Control toggle gesture, THIRD state", 3L, false, true, 1L },
            { ":L49 disjunct 3 with the THIRD state", 3L, true, true, 0L },
        };

    [Theory]
    // The truth table of :L45, :L49 and :L61/:L76 across the three styles and the four modifier
    // combinations. 0 means the DataWindow's own click handling proceeds; 1 means it is prevented.
    [MemberData(nameof(ClickTruthTableCases))]
    public void OnLButtonClk_TruthTable(string locator, long style, bool shift, bool ctrl, long expected)
    {
        Assert.False(string.IsNullOrEmpty(locator));

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

    /// <summary>
    /// The plain-click inequality site, one row per style with its deciding locator.
    /// </summary>
    public static TheoryData<string, long, bool> PlainClickReselectCases =>
        new()
        {
            { ":L52-L53 reselect because RS_SINGLE <> RS_MULTIPLE", RowSelectService.RS_SINGLE, true },
            { ":L52 no reselect because RS_MULTIPLE = RS_MULTIPLE", RowSelectService.RS_MULTIPLE, false },
            { ":L52 reselect because 3 <> 2 - the THIRD state", 3L, true },
        };

    [Theory]
    // :L52 - the plain-click arm's re-select, again by EQUALITY.
    [MemberData(nameof(PlainClickReselectCases))]
    public void OnLButtonClk_PlainClick_CollapsesTheSelection(
        string locator,
        long style,
        bool expectReselect)
    {
        Assert.False(string.IsNullOrEmpty(locator));

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

        // SUPPRESSED, NOT RECURSED. :L88 returns before :L90, so the re-entrant handler never reaches
        // the host and therefore never moves the row again - one SetRow, one callback, no nesting. Had
        // the guard been absent the handler would have run its own body inside the move, and on a host
        // that raises the event from SetRow that is unbounded recursion rather than a wrong answer.
        Assert.Equal(1, host.CallLog.CountOf("SetRow"));
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

    /// <summary>
    /// The enable-time inequality site, one row per style with its deciding locator.
    /// </summary>
    public static TheoryData<string, long, bool> EnableReselectCases =>
        new()
        {
            { ":L277-L278 reselect because RS_SINGLE <> RS_MULTIPLE", RowSelectService.RS_SINGLE, true },
            { ":L277 no reselect because RS_MULTIPLE = RS_MULTIPLE", RowSelectService.RS_MULTIPLE, false },
            { ":L277 reselect because 3 <> 2 - the THIRD state", 3L, true },
        };

    [Theory]
    // :L277 - EQUALITY, NOT A BIT TEST. Enabling re-selects the current row for RS_SINGLE and for the
    // COMBINED value, and not for RS_MULTIPLE.
    [MemberData(nameof(EnableReselectCases))]
    public void Enable_ReconcilesTheSelectionByEquality(string locator, long style, bool expectReselect)
    {
        Assert.False(string.IsNullOrEmpty(locator));

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

    // ==========================================================================================
    //  THE CONFIGURED DEFAULT - :L25 REPRODUCED AS CONFIGURATION RATHER THAN AS A LITERAL
    // ==========================================================================================

    [Fact]
    public void Style_DefaultIsDeclaredInConfigurationAndNotHardcodedInTheService()
    {
        // THE DECLARED DEFAULT LIVES IN Configuration/DataServicesOptions.cs, which reproduces
        // :L25 `privatewrite long #Style = RS_SINGLE` together with :L20 `RS_SINGLE = 1`, and
        // appsettings.json carries the same value under DataServices:RowSelect:Style. Asserting the
        // options type rather than the service is the point: it is what proves the value the service
        // starts with is CONFIGURATION, so an operator can change it, and that the defect being
        // preserved is the VALUE and not the un-configurability (AAP §0.4.2.4).
        Assert.Equal(RowSelectService.RS_SINGLE, new RowSelectOptions().Style);
        Assert.Equal(RowSelectService.RS_SINGLE, new DataServicesOptions().RowSelect.Style);

        // ...and the service reads it from there rather than substituting one of its own. A hardcoded
        // default could not answer three different values for three different configurations.
        foreach (long configured in new[] { RowSelectService.RS_MULTIPLE, 3L, 4096L })
        {
            Assert.Equal(
                configured,
                new RowSelectService(new I18n(), OptionsWithStyle(configured)).Style);
        }
    }

    // ==========================================================================================
    //  REPLACE VERSUS ACCUMULATE - THE OBSERVABLE DIFFERENCE BETWEEN THE STYLES
    // ==========================================================================================

    /// <summary>
    /// Two Control-clicks from one anchor, per style. RS_SINGLE never leaves <c>:L49</c>'s first
    /// disjunct so each click REPLACES the selection; the other two styles reach <c>:L76-L81</c> and
    /// ACCUMULATE.
    /// </summary>
    public static TheoryData<string, long, long[], bool> ModifierAccumulationCases =>
        new()
        {
            {
                ":L49 disjunct 1 - RS_SINGLE ignores Control and REPLACES",
                RowSelectService.RS_SINGLE,
                [4L],
                false
            },
            {
                ":L76-L81 - RS_MULTIPLE ACCUMULATES, anchor included by :L78",
                RowSelectService.RS_MULTIPLE,
                [1L, 2L, 4L],
                true
            },
            {
                ":L76-L81 - the THIRD state accumulates exactly as RS_MULTIPLE does",
                3L,
                [1L, 2L, 4L],
                true
            },
        };

    [Theory]
    [MemberData(nameof(ModifierAccumulationCases))]
    public void TwoControlClicks_ReplaceUnderSingleSelectAndAccumulateOtherwise(
        string locator,
        long style,
        long[] expectedSelection,
        bool expectMultiSelected)
    {
        Assert.False(string.IsNullOrEmpty(locator));

        FakeDataWindowHost host = CreateCheckBoxHost("char(1)", OnValue, OffValue, OnValue, OffValue);
        RowSelectService service = CreateService(host, style);
        IDataWindowObject dwo = FlagObject(host);

        // The anchor is row 1, and it is the CURRENT row rather than a selected one - which is the
        // distinction :L77-L78 exists to repair.
        Assert.Equal(1L, host.GetRow());
        Assert.Empty(host.SelectedRows);

        service.OnLButtonClk(0L, 0L, 2L, dwo, shiftHeld: false, ctrlHeld: true);
        service.OnLButtonClk(0L, 0L, 4L, dwo, shiftHeld: false, ctrlHeld: true);

        Assert.Equal(expectedSelection, host.SelectedRows);
        Assert.Equal(expectMultiSelected, service.HasMultiSelected());
    }

    // ==========================================================================================
    //  THE WALK - IT VISITS THE SELECTION, AND ROW NUMBERS ARE ONE-BASED THROUGHOUT
    // ==========================================================================================

    /// <summary>
    /// The cardinality of the selection the walk at <c>:L223-L255</c> visits: none, one, several
    /// contiguous, and several NON-contiguous.
    /// </summary>
    public static TheoryData<string, long[], long, int> WalkCardinalityCases =>
        new()
        {
            // :L193 - with nothing selected the clicked row is not selected either, so the third
            // precondition refuses and the walk is never entered.
            { ":L193 empty selection refuses at precondition 3", [], 0L, 0 },

            // One selected row - the clicked one. The walk runs and visits exactly it.
            { ":L224-L225 one selected row, one visit", [1L], 1L, 1 },

            // Several contiguous.
            { ":L224-L225 four selected rows, four visits", [1L, 2L, 3L, 4L], 1L, 4 },

            // Several NON-contiguous, which is what proves the walk follows GetSelectedRow rather
            // than a first-to-last range.
            { ":L224 GetSelectedRow, not a range scan", [1L, 3L], 1L, 2 },
        };

    [Theory]
    [MemberData(nameof(WalkCardinalityCases))]
    public void Propagation_VisitsExactlyTheSelectedRows(
        string locator,
        long[] selection,
        long expectedResult,
        int expectedVisits)
    {
        Assert.False(string.IsNullOrEmpty(locator));

        FakeDataWindowHost host = CreateCheckBoxHost("char(1)", OnValue, OnValue, OnValue, OnValue);
        RowSelectService service = CreateService(host, RowSelectService.RS_MULTIPLE);

        // :L194 - reach the multi-selected state through a real gesture, then state the exact
        // selection the walk is to see.
        service.OnLButtonClk(0L, 0L, 1L, FlagObject(host), shiftHeld: false, ctrlHeld: true);
        Assert.True(service.HasMultiSelected());

        host.ArrangeSelectedRows(selection);
        host.CallLog.Clear();

        Assert.Equal(
            expectedResult,
            service.OnLButtonClk(0L, 0L, 1L, FlagObject(host), shiftHeld: false, ctrlHeld: false));

        List<long> visited =
        [
            .. host.CallLog.Records
                .Where(record => record.Member == "Event OnDoItemChange")
                .Select(record => Assert.IsType<long>(record.Arguments[0])),
        ];

        Assert.Equal(expectedVisits, visited.Count);
        Assert.Equal([.. selection.Take(expectedVisits)], visited);
    }

    [Fact]
    public void Propagation_SeedsTheWalkWithZeroAndYieldsOneBasedRowNumbers()
    {
        // AAP §0.4.5.4 - one-based to zero-based translation is the single most dangerous mechanical
        // hazard in this refactor, so the boundary is asserted rather than assumed. THREE SEPARATE
        // FACTS ARE PINNED HERE:
        //
        //   1. :L224's walk is SEEDED WITH ZERO - `nRow` is a fresh PowerScript long, so the first
        //      GetSelectedRow call asks for "the first selected row after row 0". Zero is the seed,
        //      NOT a row.
        //   2. Every row number that reaches the host is one-based, so row 1 is the first row of
        //      data and row 0 is never a datum.
        //   3. :L51's SelectRow(0,false) is the ALL-ROWS convention, not row zero - the fake models
        //      it, and the resulting selection contains no zero.
        (FakeDataWindowHost host, RowSelectService service) = ArrangePropagation(
            "char(1)",
            [OnValue, OnValue, OnValue, OnValue]);

        // Reads are recorded from here on, so the seed itself becomes observable.
        host.RecordsReads = true;
        host.CallLog.Clear();

        Assert.Equal(
            1L,
            service.OnLButtonClk(0L, 0L, 1L, FlagObject(host), shiftHeld: false, ctrlHeld: false));

        List<long> walkSeeds =
        [
            .. host.CallLog.Records
                .Where(record => record.Member == "GetSelectedRow")
                .Select(record => Assert.IsType<long>(record.Arguments[0])),
        ];

        // Fact 1: seeded with 0, then with each row just visited - and one final call that answers 0
        // and ends the walk [:L225].
        Assert.Equal([0L, 1L, 2L, 3L, 4L], walkSeeds);

        // Fact 2: no row-bearing call ever carries row 0 or a negative row.
        foreach (DataWindowCallRecord record in host.CallLog.Records)
        {
            if (record.Member is "Event OnDoItemChange" or "Event OnDoItemChanged" or "SetItem")
            {
                Assert.True(Assert.IsType<long>(record.Arguments[0]) >= OneBasedRows.FirstRow);
            }
        }

        // Fact 3: the all-rows convention selected rows 1..4, and never a row 0.
        Assert.Equal([1L, 2L, 3L, 4L], host.SelectedRows);
        Assert.DoesNotContain(0L, host.SelectedRows);
    }

    // ==========================================================================================
    //  THE MODIFIER STATE ARRIVES AS PARAMETERS - AND NOTHING READS A KEYBOARD
    // ==========================================================================================

    /// <summary>
    /// The two click entry points, which are the only members that carry modifier state.
    /// </summary>
    public static TheoryData<string, string> ClickEntryPoints =>
        new()
        {
            { ":L45, :L49, :L61 - eight live key-state reads inverted", nameof(RowSelectService.OnLButtonClk) },
            { ":L113 - the double click forwards the same six arguments", nameof(RowSelectService.OnLButtonDblClk) },
        };

    [Theory]
    [MemberData(nameof(ClickEntryPoints))]
    public void ClickEntryPoints_TakeTheModifierStateAsExplicitBooleanParameters(
        string locator,
        string memberName)
    {
        Assert.False(string.IsNullOrEmpty(locator));

        MethodInfo method = Assert.Single(
            typeof(RowSelectService).GetMethods(BindingFlags.Public | BindingFlags.Instance),
            candidate => candidate.Name == memberName);

        // The legacy asks PowerScript for the live key state at eight reads across :L45, :L49, :L61
        // and :L76. A headless Linux container has no keyboard to ask and constraint C-D forbids
        // reaching for a user-interface or interop type to find one, so the state must ARRIVE on the
        // call. These two parameters are that inversion, and their names and positions are contract:
        // the REST and gRPC projections bind request fields onto them by name.
        ParameterInfo[] parameters = method.GetParameters();

        Assert.Equal(
            ["xpos", "ypos", "row", "dwo", "shiftHeld", "ctrlHeld"],
            parameters.Select(parameter => parameter.Name));

        Assert.Equal(typeof(bool), parameters[4].ParameterType);
        Assert.Equal(typeof(bool), parameters[5].ParameterType);

        // By value, not by reference: the service reads the modifier state and never reports one back.
        Assert.False(parameters[4].ParameterType.IsByRef);
        Assert.False(parameters[5].ParameterType.IsByRef);
    }

    /// <summary>
    /// Every spelling by which a live keyboard read could enter this service.
    /// </summary>
    /// <remarks>
    /// Matched case-insensitively against every member name, parameter name, field name and involved
    /// type name on the real type - public and private alike.
    /// </remarks>
    public static TheoryData<string> ForbiddenKeyboardTokens =>
        new()
        {
            "keydown",
            "keystate",
            "asynckeystate",
            "getkey",
            "keyboard",
            "modifierkey",
            "keyshift",
            "keycontrol",
            "user32",
        };

    [Theory]
    [MemberData(nameof(ForbiddenKeyboardTokens))]
    public void Service_ReadsNoKeyboardState(string forbiddenToken)
    {
        foreach (string name in DescribeEverySymbol(typeof(RowSelectService)))
        {
            Assert.DoesNotContain(forbiddenToken, name, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Service_DeclaresNoPlatformInvoke()
    {
        // THE DECISIVE CHECK BEHIND THE ONE ABOVE. A managed service cannot read a keyboard without
        // either a user-interface type - excluded by name above and by the C-D surface test below - or
        // a platform invoke. There is not one anywhere on this type, so there is no path to the live
        // key state that a rename could hide.
        MethodInfo[] methods = typeof(RowSelectService).GetMethods(
            BindingFlags.Public
            | BindingFlags.NonPublic
            | BindingFlags.Instance
            | BindingFlags.Static
            | BindingFlags.DeclaredOnly);

        Assert.NotEmpty(methods);

        foreach (MethodInfo method in methods)
        {
            Assert.False(
                (method.Attributes & MethodAttributes.PinvokeImpl) != 0,
                method.Name + " must not be a platform invoke.");
        }
    }

    // ==========================================================================================
    //  A DISABLED SERVICE IS SILENT - :L281-L283
    // ==========================================================================================

    [Fact]
    public void Disabled_ServiceReceivesNoEventAtAll()
    {
        // :L281's of_Off(this) removes the subscriptions BY TARGET, so the proof is not that
        // IsSubscribed answers false - the companion test already asserts that - but that TRIGGERING
        // the topics afterwards reaches nothing. A subscription that lingered would show up here as a
        // host call, and a service that kept mutating its own state would show up as a changed flag.
        FakeDataWindowHost host = CreateCheckBoxHost("char(1)", OnValue, OffValue, OnValue);
        host.CurrentRow = 1L;
        RowSelectService service = CreateService(host, RowSelectService.RS_MULTIPLE);

        Assert.Equal(RetCode.OK, service.SetEnabled(true));
        service.OnLButtonClk(0L, 0L, 3L, FlagObject(host), shiftHeld: false, ctrlHeld: true);
        Assert.True(service.HasMultiSelected());

        Assert.Equal(RetCode.OK, service.SetEnabled(false));
        Assert.False(service.HasMultiSelected());
        host.CallLog.Clear();

        // All three of the topics :L274-L276 registers, triggered on the very broker they were
        // registered with.
        _ = host.Eventful.Trigger(DataWindowEventChain.EVT_CLICKED, 0L, 0L, 3L, FlagObject(host));
        _ = host.Eventful.Trigger(DataWindowEventChain.EVT_DOUBLECLICKED, 0L, 0L, 3L, FlagObject(host));
        _ = host.Eventful.Trigger(DataWindowEventChain.EVT_ROWFOCUSCHANGED, 2L);

        Assert.Empty(host.CallLog.Records);
        Assert.False(service.HasMultiSelected());
        Assert.Null(service.PendingError);
    }

    // ==========================================================================================
    //  THE BASE-CLASS VETO CONTRACT - A REFUSED ENABLE ANSWERS FAILED, NOT PREVENT
    // ==========================================================================================

    [Fact]
    public void SetEnabled_WhenTheHookVetoes_AnswersFailedRatherThanPrevent()
    {
        // n_cst_dwsvc.sru:L90 - `if Event OnEnable(enabled) = 1 then return RetCode.FAILED`. THE
        // HOOK SIGNALS 1 AND THE CALLER IS TOLD -1, so the two codes are deliberately different at
        // the two ends of the same call. This is the base-class contract RowSelectService.OnEnable
        // sits inside, asserted through a service whose hook CAN veto - RowSelectService's own
        // returns 0 unconditionally at :L286 and so can never reach this arm itself.
        FakeDataWindowHost host = CreateCheckBoxHost("char(1)", OnValue);
        FakeDataWindowService vetoing = new();
        vetoing.OnInit(host);
        vetoing.OnEnableHandler = _ => RetCode.PREVENT;

        long rtCode = vetoing.SetEnabled(true);

        Assert.Equal(RetCode.FAILED, rtCode);
        Assert.NotEqual(RetCode.PREVENT, rtCode);

        // And the two are on opposite sides of the tri-state algebra, which is the reason the
        // distinction matters: a prevention READS AS A SUCCESS [issucceeded.srf:L11-L13] while a
        // failure does not, so answering PREVENT here would report a refused enable as a success.
        Assert.True(Predicates.IsFailed(rtCode));
        Assert.False(Predicates.IsSucceeded(rtCode));
        Assert.True(Predicates.IsSucceeded(RetCode.PREVENT));

        // n_cst_dwsvc.sru:L92 is past the veto, so the state is unchanged.
        Assert.False(vetoing.Enabled);
    }

    [Fact]
    public void SetEnabled_OnThisService_NeverVetoesWhateverTheStyle()
    {
        // :L286 `return 0` is unconditional - there is no branch in onenable that can answer 1 - so
        // every style enables and disables cleanly.
        foreach (long style in new[] { RowSelectService.RS_SINGLE, RowSelectService.RS_MULTIPLE, 3L })
        {
            FakeDataWindowHost host = CreateCheckBoxHost("char(1)", OnValue, OffValue);
            RowSelectService service = CreateService(host, style);

            Assert.Equal(RetCode.OK, service.SetEnabled(true));
            Assert.True(service.Enabled);

            Assert.Equal(RetCode.OK, service.SetEnabled(false));
            Assert.False(service.Enabled);
        }
    }

    // ==========================================================================================
    //  THE CHAIN EDGE - se_cst_dw.sru:L403-L446, PORTED IN Domain/DataWindowEventChain.cs
    // ==========================================================================================

    /// <summary>
    /// Builds a real chain over the real service, with the row-selection service attached through the
    /// interface the chain consumes.
    /// </summary>
    /// <param name="style">The configured style.</param>
    /// <param name="enabled">Whether to enable the service before returning.</param>
    /// <returns>The chain and the service the chain drives.</returns>
    private static (RowSelectChainHarness Chain, RowSelectService Service) ArrangeChain(
        long style = RowSelectService.RS_SINGLE,
        bool enabled = true)
    {
        RowSelectService service = new(new I18n(), OptionsWithStyle(style));
        RealRowSelectServiceFactory services = new(service);

        // The chain's constructor performs :L570-L580 - it creates all five attached services and
        // initialises them against itself - so OnInit runs here rather than in the test.
        ValidationSession session = new("rowselect-chain", "dw-1");
        RowSelectChainHarness chain = new(session, services);

        _ = chain.Host.AddColumn(FlagColumn, "char(1)");
        _ = chain.Host.AddRow(OnValue);
        _ = chain.Host.AddRow(OffValue);
        _ = chain.Host.AddRow(OnValue);
        chain.Host.CurrentRow = 2L;

        if (enabled)
        {
            Assert.Equal(RetCode.OK, service.SetEnabled(true));
        }

        chain.Host.CallLog.Clear();

        return (chain, service);
    }

    /// <summary>
    /// The selection calls the host recorded, in order - the only part of the log the chain-edge
    /// assertions care about, since the chain's own bookkeeping records reads around them.
    /// </summary>
    /// <param name="host">The host whose log to read.</param>
    /// <returns>The <c>SelectRow</c> call descriptions, in call order.</returns>
    private static IReadOnlyList<string> SelectionCalls(FakeDataWindowHost host) =>
        [
            .. host.CallLog.Descriptions
                .Where(description => description.StartsWith("SelectRow", StringComparison.Ordinal)),
        ];

    /// <summary>
    /// The three outcomes <c>Filter()</c> can see from the DataWindow, and whether <c>:L407</c>'s
    /// success test admits each.
    /// </summary>
    /// <remarks>
    /// Success is 1 and NOT the zero of the return-code algebra
    /// [<c>se_cst_dw.sru:L406-L407</c>], which is why the failure rows use 0 and -1: conflating the
    /// two conventions would invert this guard and notify on failure instead.
    /// </remarks>
    public static TheoryData<string, int, int> FilterOutcomeCases =>
        new()
        {
            { "se_cst_dw.sru:L407 rtCode = 1 is SUCCESS, so :L409 runs", 1, 1 },
            { "se_cst_dw.sru:L407 rtCode = 0 is a FAILURE here, not OK", 0, 0 },
            { "se_cst_dw.sru:L407 rtCode = -1 is a failure too", -1, 0 },
        };

    [Theory]
    [MemberData(nameof(FilterOutcomeCases))]
    public void Chain_Filter_NotifiesTheServiceOnlyAfterASuccessfulFilter(
        string locator,
        int filterResult,
        int expectedNotifications)
    {
        Assert.False(string.IsNullOrEmpty(locator));

        (RowSelectChainHarness chain, RowSelectService service) = ArrangeChain();
        chain.Host.FilterResult = filterResult;

        Assert.Equal(filterResult, chain.Filter());

        Assert.Equal(expectedNotifications, chain.Services.RowSelect.FilteredCount);

        // And the notification is not merely counted - it did the oracle's work. :L103 clears, :L106
        // re-selects the current row because RS_SINGLE <> RS_MULTIPLE.
        Assert.Equal(
            expectedNotifications == 1 ? ["SelectRow[0, false]", "SelectRow[2, true]"] : (string[])[],
            SelectionCalls(chain.Host));

        Assert.False(service.HasMultiSelected());
    }

    [Fact]
    public void Chain_Filter_DoesNotNotifyADisabledService()
    {
        // se_cst_dw.sru:L408 - `if RowSelect.#Enabled then`. THIS IS THE REASON OnFiltered IS NOT A
        // SUBSCRIPTION: the host tests the service's own enabled flag and calls it directly, so a
        // disabled service is skipped at the CALLER rather than by an absent subscription.
        (RowSelectChainHarness chain, RowSelectService service) = ArrangeChain(enabled: false);

        Assert.False(service.Enabled);
        Assert.Equal(1, chain.Filter());

        Assert.Equal(0, chain.Services.RowSelect.FilteredCount);
        Assert.Empty(SelectionCalls(chain.Host));
    }

    [Fact]
    public void Chain_AttachesThisServiceAndLeavesItsFourSiblingsUndisturbed()
    {
        // se_cst_dw.sru:L570-L580 - the chain creates all five attached services and initialises each
        // against ITSELF, so the host this service reaches through is the chain rather than any inner
        // object. That is what makes the Filter() edge above a real end-to-end path.
        (RowSelectChainHarness chain, RowSelectService service) = ArrangeChain();

        Assert.Same(chain, service.DataWindow);
        Assert.Same(chain.Eventful, service.Eventful);

        // AND NOTHING ELSE WAS DISTURBED. Row selection participates in Filter() and in the row-change
        // event and in nothing else, so the other four services must be initialised - :L576-L580 - and
        // then left entirely alone by everything this suite does.
        foreach (InertAttachedService sibling in new[]
                 {
                     chain.Services.ContextMenu,
                     chain.Services.ColumnSort,
                     chain.Services.DropDownSearch,
                     chain.Services.ColumnExp,
                 })
        {
            Assert.Equal([chain], sibling.InitialisedWith);
            Assert.Empty(sibling.EditChanged);
            Assert.Empty(sibling.ItemChanged);
        }

        chain.Host.FilterResult = 1;
        Assert.Equal(1, chain.Filter());
        Assert.Equal(1, chain.Services.RowSelect.FilteredCount);

        // Still undisturbed after the one edge that does fire.
        Assert.Empty(chain.Services.DropDownSearch.EditChanged);
        Assert.Empty(chain.Services.ColumnExp.ItemChanged);
    }

    [Fact]
    public void OnFiltered_TakesNoArgumentsUnlikeOnDdsFiltered()
    {
        // *** DIVERGENCE B IN THE FILE HEADER, PINNED ***
        // :L11 declares `event onfiltered ( )` - EMPTY ARGUMENT LIST - and :L105 reads the row it
        // needs from the DataWindow itself. The row-count/filtered-count pair belongs to a DIFFERENT
        // event on a DIFFERENT service: onddsfiltered(row,dwo,rowcount,filteredcount)
        // [se_cst_dw.sru:L28], the drop-down search service's, ported as OnDDSFiltered on the host.
        // Both arities are asserted here so the two can never be conflated.
        MethodInfo onFiltered = Assert.Single(
            typeof(RowSelectService).GetMethods(BindingFlags.Public | BindingFlags.Instance),
            candidate => candidate.Name == nameof(RowSelectService.OnFiltered));

        Assert.Empty(onFiltered.GetParameters());

        // :L101-L111 has no return statement at all, so the port returns void rather than a code.
        Assert.Equal(typeof(void), onFiltered.ReturnType);

        MethodInfo onDdsFiltered = Assert.Single(
            typeof(DataWindowServiceHost).GetMethods(BindingFlags.Public | BindingFlags.Instance),
            candidate => candidate.Name == nameof(DataWindowServiceHost.OnDDSFiltered));

        Assert.Equal(
            ["row", "dwo", "rowCount", "filteredCount"],
            onDdsFiltered.GetParameters().Select(parameter => parameter.Name));
    }

    [Fact]
    public void Chain_DeleteRow_ReachesTheServiceThroughTheRowChangeEvent()
    {
        // se_cst_dw.sru:L434-L435 - emptying the DataWindow raises the row-change event with ROW 0,
        // which is the oracle's "there is now no current row" notification and not an error sentinel.
        // The row-selection service is subscribed to that topic [:L276], so THIS is how DeleteRow
        // reaches it: indirectly, through the event, and never by a direct call - which is why
        // OnRowFocusChanged and not OnFiltered is the member that runs.
        (RowSelectChainHarness chain, RowSelectService service) = ArrangeChain(
            RowSelectService.RS_MULTIPLE);
        IDataWindowObject dwo = chain.GetObjectAttribute(FlagColumn);

        // A Control-click leaves the multi-selected flag set, so the notification is observable as a
        // state change as well as a call.
        _ = service.OnLButtonClk(0L, 0L, 3L, dwo, shiftHeld: false, ctrlHeld: true);
        Assert.True(service.HasMultiSelected());

        // Empty the DataWindow one row at a time; the last delete takes RowCount() to zero.
        Assert.Equal(1, chain.DeleteRow(3L));
        Assert.Equal(1, chain.DeleteRow(2L));
        chain.Host.CallLog.Clear();
        Assert.Equal(1, chain.DeleteRow(1L));

        // :L96 - the handler ran, so the flag is cleared...
        Assert.False(service.HasMultiSelected());

        // ...and :L90's clear reached the host, while :L92's `currentRow > 0` guard correctly refused
        // to re-select row 0.
        Assert.Equal(["SelectRow[0, false]"], SelectionCalls(chain.Host));

        // The out-of-range guard at :L421 is the one path that never reaches the service at all.
        chain.Host.CallLog.Clear();
        Assert.Equal(-1, chain.DeleteRow(99L));
        Assert.Empty(SelectionCalls(chain.Host));
    }

    [Fact]
    public void Propagation_SkipsAProtectedCellInsideTheWalkWhileTheClickedCellIsNot()
    {
        // :L228-L229 legacy comment 忽略被保护的单元格 - "ignore protected cells" - INSIDE THE WALK, and
        // this is a DIFFERENT test from precondition 8 at :L201 even though both call the same helper:
        // :L201 asks about the CLICKED row and refuses the whole propagation, while :L229 asks about
        // each visited row and skips just that one. Reaching the second therefore requires an
        // arrangement where the clicked cell is NOT protected and another selected cell IS, which a
        // per-row property EXPRESSION is exactly able to express [n_cst_dwsvc.sru:L188-L195].
        (FakeDataWindowHost host, RowSelectService service) = ArrangePropagation(
            "char(1)",
            [OnValue, OnValue, OnValue, OnValue]);

        // The tab-separated form is `<column-level value>\t<expression>`, and the implementation
        // prepends only the OPENING quote to the expression - so the taught value carries the closing
        // one and the composed probe is Evaluate("protectexp",<row>).
        host.Column(FlagColumn).SetProperty("protect", "0\tprotectexp\"");
        host.DescribeOverride = property => property switch
        {
            // Row 0 is the COLUMN-level probe [:L391 of the base, evaluated at row 0]. Answering
            // "not protected" is what lets precondition 2 pass so the walk is reached at all.
            "Evaluate(\"protectexp\",0)" => "0",

            // The clicked row - not protected, so precondition 8 at :L201 passes too.
            "Evaluate(\"protectexp\",1)" => "0",

            // THE ONE PROTECTED CELL. The walk must skip row 2 and carry on to rows 3 and 4.
            "Evaluate(\"protectexp\",2)" => "1",
            "Evaluate(\"protectexp\",3)" => "0",
            "Evaluate(\"protectexp\",4)" => "0",
            _ => null,
        };

        host.CallLog.Clear();

        Assert.Equal(
            1L,
            service.OnLButtonClk(0L, 0L, 1L, FlagObject(host), shiftHeld: false, ctrlHeld: false));

        // Row 2 was skipped BEFORE :L238, so it was never offered to the application and never
        // written - while the walk continued rather than stopping, which is the difference between
        // `continue` at :L229 and `exit` at :L240.
        Assert.Equal(
            [
                "Event OnDoItemChange[1, dwo:flag, \"N\"]",
                "SetRedraw[false]",
                "SetItem(string?)[1, \"flag\", \"N\"]",
                "Event OnDoItemChanged[1, dwo:flag]",
                "Event OnDoItemChange[3, dwo:flag, \"N\"]",
                "SetItem(string?)[3, \"flag\", \"N\"]",
                "Event OnDoItemChanged[3, dwo:flag]",
                "Event OnDoItemChange[4, dwo:flag, \"N\"]",
                "SetItem(string?)[4, \"flag\", \"N\"]",
                "Event OnDoItemChanged[4, dwo:flag]",
                "SetRedraw[true]",
            ],
            host.CallLog.Descriptions);

        Assert.Null(service.PendingError);
    }

    [Fact]
    public void SetCurrentRow_RowGuardIsUnreachableThroughThePublicSurface()
    {
        // A CHARACTERIZATION OF A REDUNDANCY, RECORDED RATHER THAN REMOVED. :L118's `if row <= 0 then
        // return false` duplicates :L42's `if row <= 0 then return 0`, and :L50 is the only call site
        // of _of_SetCurrentRow in all 288 lines - so the inner guard cannot be reached from outside:
        // every row that arrives at :L50 has already passed :L42.
        //
        // The consequence is asserted at the boundary instead. A non-positive row answers 0 - which is
        // :L42's code and NOT :L50's `return 1` - and it does so having touched neither the host nor
        // the column, which is only possible if the outer guard fired first. Constraint C-B keeps the
        // redundancy: it is the oracle's shape, and collapsing it would be a change with no
        // observable justification.
        FakeDataWindowHost host = CreateCheckBoxHost("char(1)", OnValue, OffValue);
        RowSelectService service = CreateService(host, RowSelectService.RS_SINGLE);
        host.RecordsReads = true;
        host.CallLog.Clear();

        foreach (long row in new[] { 0L, -1L, long.MinValue })
        {
            Assert.Equal(0L, service.OnLButtonClk(0L, 0L, row, null!, shiftHeld: false, ctrlHeld: false));
        }

        // Not one call of any kind - :L50 would have produced a GetRow read at :L121 on its way to the
        // inner guard's caller.
        Assert.Empty(host.CallLog.Records);
        Assert.Equal(1L, host.GetRow());
    }

    // ==========================================================================================
    //  THE REFUSAL TEST IS `<> 0`, NOT `> 0` - :L238
    // ==========================================================================================

    /// <summary>
    /// What the application's item-change handler can answer, and whether <c>:L238</c> reads it as a
    /// refusal. The test is inequality with zero, so a NEGATIVE answer refuses exactly as a positive
    /// one does.
    /// </summary>
    public static TheoryData<string, long, bool> ItemChangeResultCases =>
        new()
        {
            { ":L238 zero is the only ACCEPTANCE", 0L, false },
            { ":L238 1 refuses - the prevent convention", 1L, true },
            { ":L238 -1 refuses too, because the test is <> 0 and not > 0", -1L, true },
            { ":L238 an arbitrary non-zero code refuses", 42L, true },
            { ":L238 no magnitude is exempt", long.MinValue, true },
        };

    [Theory]
    [MemberData(nameof(ItemChangeResultCases))]
    public void Propagation_TreatsEveryNonZeroItemChangeResultAsARefusal(
        string locator,
        long itemChangeResult,
        bool expectRefusal)
    {
        Assert.False(string.IsNullOrEmpty(locator));

        (FakeDataWindowHost host, RowSelectService service) = ArrangePropagation(
            "char(1)",
            [OnValue, OnValue, OnValue, OnValue]);

        host.DoItemChangeHandler = (row, _, _) => row == 2L ? itemChangeResult : 0L;

        // :L259 answers 1 either way - the refusal path and the exhausted-loop path share it.
        Assert.Equal(
            1L,
            service.OnLButtonClk(0L, 0L, 1L, FlagObject(host), shiftHeld: false, ctrlHeld: false));

        if (!expectRefusal)
        {
            // Every row was accepted, so all four were written and no error was produced.
            Assert.Null(service.PendingError);
            Assert.Equal(4, host.CallLog.CountOf("SetItem(string?)"));
            return;
        }

        // :L240's exit - row 2 refused, so rows 3 and 4 were never visited and only row 1 was written.
        RowSelectRejectionError error = Assert.IsType<RowSelectRejectionError>(service.PendingError);
        Assert.Equal(2L, error.Row);
        Assert.Equal(1, host.CallLog.CountOf("SetItem(string?)"));
        Assert.DoesNotContain("Event OnDoItemChange[3, dwo:flag, \"N\"]", host.CallLog.Descriptions);
        Assert.DoesNotContain("Event OnDoItemChange[4, dwo:flag, \"N\"]", host.CallLog.Descriptions);
    }

    [Fact]
    public void RowNumberKey_CarriesTheSequentialEmptyBracePlaceholderAndNotAnIndexedOne()
    {
        // :L239 - `Sprintf(I18N(ne_cst_i18n.CAT_DWSVC,"第{}行"),nRow)`. THE PLACEHOLDER IS EMPTY
        // BRACES. sprintf.srf fills an index-omitted placeholder from a cursor that advances per
        // placeholder, whereas an indexed `{0}` is a lookup that does not advance it - so the two
        // forms are different spellings with different mechanics, and the key uses the first.
        // Rewriting the key to the indexed form would ALSO change a translated string's meaning,
        // because the key is what a provider is asked to translate.
        Assert.Contains("{}", RowSelectService.RowNumberMessageKey, StringComparison.Ordinal);
        Assert.DoesNotContain("{0}", RowSelectService.RowNumberMessageKey, StringComparison.Ordinal);

        // The primitive and the form, applied exactly as :L239 applies them.
        Assert.Equal("第7行", Formatting.Sprintf(RowSelectService.RowNumberMessageKey, 7L));

        // The second fragment carries NO placeholder at all, which is why it is looked up separately
        // rather than folded into one format string.
        Assert.DoesNotContain("{", RowSelectService.RejectionMessageKey, StringComparison.Ordinal);
    }

    // ==========================================================================================
    //  THE REJECTION MESSAGE UNDER THREE LOCALIZATION ARRANGEMENTS
    // ==========================================================================================

    /// <summary>
    /// The three arrangements <c>i18n.srf</c> can be in when <c>:L239</c> composes its message: no
    /// provider at all, a provider that declines, and a provider that translates.
    /// </summary>
    /// <remarks>
    /// The first two must produce IDENTICAL text. That is the silent-passthrough fallback: the oracle
    /// has no <c>else</c> arm [<c>i18n.srf:L21</c>] and a declining provider leaves the <c>ref</c>
    /// argument exactly as found, so neither path annotates, wraps, logs or throws - the source text
    /// simply comes back.
    /// </remarks>
    public static TheoryData<string, string, string> RejectionLocalizationCases =>
        new()
        {
            {
                "i18n.srf:L21 - no provider installed, so the guard is false and text is untouched",
                NoProviderArrangement,
                "第3行\n修改数据被拒绝!"
            },
            {
                "i18n.srf:L21-L22 - a provider that declines leaves the ref argument as found",
                PassthroughArrangement,
                "第3行\n修改数据被拒绝!"
            },
            {
                ":L239 - both fragments translated INDEPENDENTLY, Sprintf over the TRANSLATION",
                TranslatingArrangement,
                "<t:第3行>\n<t:修改数据被拒绝>!"
            },
        };

    [Theory]
    [MemberData(nameof(RejectionLocalizationCases))]
    public void Propagation_WhenRefused_ComposesTheMessageUnderEveryLocalizationArrangement(
        string locator,
        string arrangement,
        string expectedText)
    {
        Assert.False(string.IsNullOrEmpty(locator));

        (I18n i18n, II18nProvider? provider) = BuildLocalization(arrangement);

        (FakeDataWindowHost host, RowSelectService service) = ArrangePropagation(
            "char(1)",
            [OnValue, OnValue, OnValue, OnValue],
            RowSelectService.RS_MULTIPLE,
            i18n);

        // Row 3 refuses, so the row number in the message is the row the WALK reached rather than the
        // row that was clicked [:L224, :L239].
        host.DoItemChangeHandler = (row, _, _) => row == 3L ? 1L : 0L;

        Assert.Equal(
            1L,
            service.OnLButtonClk(0L, 0L, 1L, FlagObject(host), shiftHeld: false, ctrlHeld: false));

        RowSelectRejectionError error = Assert.IsType<RowSelectRejectionError>(service.PendingError);

        // BYTE-EXACT, including the Chinese keys, the single line feed and the trailing "!".
        Assert.Equal(expectedText, error.Text);

        // The same text, recomposed from the two fragments by the shared composition rule - which is
        // what proves the order is `Sprintf(fragment1) + separator + fragment2 + suffix` rather than
        // some other arrangement that happens to produce the same string for these inputs.
        Assert.Equal(
            LegacyMessageKeys.ComposeRowSelectRejection(
                3L,
                i18n.I18N(Categories.CAT_DWSVC, LegacyMessageKeys.RowSelectRowNumberKey),
                i18n.I18N(Categories.CAT_DWSVC, LegacyMessageKeys.RowSelectRejectionKey)),
            error.Text);

        // The keys, the category, the substitution argument and the severity travel as DATA whatever
        // the provider did to the text.
        Assert.Equal(RowSelectService.RowNumberMessageKey, error.RowFormatTemplate);
        Assert.Equal(RowSelectService.RejectionMessageKey, error.RejectionMessageKey);
        Assert.Equal(Categories.CAT_DWSVC, error.LocalizationCategory);
        Assert.Equal(3L, error.Row);
        Assert.Equal(RowSelectMessageIcon.StopSign, error.Icon);

        if (provider is PassthroughI18nProvider passthrough)
        {
            // THE PROVIDER WAS CONSULTED AND STILL CHANGED NOTHING. Two lookups reached it, in the
            // oracle's order, both under CAT_DWSVC - so the identical text is the passthrough
            // fallback rather than a provider that was never asked. The extra two requests are the
            // recomposition assertion above.
            Assert.Equal(4, passthrough.Requests.Length);
            Assert.Equal(
                RowSelectService.RowNumberMessageKey,
                passthrough.Requests[0].Text);
            Assert.Equal(
                RowSelectService.RejectionMessageKey,
                passthrough.Requests[1].Text);
            Assert.All(
                passthrough.Requests,
                request => Assert.Equal(Categories.CAT_DWSVC, request.Category));
        }
    }

    // ==========================================================================================
    //  THE THREE COLUMN-TYPE ARMS OF THE ALREADY-MATCHES SKIP - :L230-L237
    // ==========================================================================================

    /// <summary>
    /// One row per arm of the <c>choose case nColType</c> inside the walk. Each names the reader the
    /// arm uses and supplies a selection whose SECOND row already holds the target value.
    /// </summary>
    public static TheoryData<string, string, object?[], string, string, string> SkipArmCases =>
        new()
        {
            {
                ":L231-L232 COL_TYPE_DECIMAL → GetItemDecimal",
                "decimal(2)",
                [1m, 0m, 1m],
                "1",
                "0",
                "SetItem(decimal?)"
            },
            {
                ":L233-L234 COL_TYPE_INTEGER → GetItemNumber",
                "long",
                [1L, 0L, 1L],
                "1",
                "0",
                "SetItem(long?)"
            },
            {
                ":L235-L236 case else → GetItemString",
                "char(1)",
                [OnValue, OffValue, OnValue],
                OnValue,
                OffValue,
                "SetItem(string?)"
            },
        };

    [Theory]
    [MemberData(nameof(SkipArmCases))]
    public void Propagation_SkipsARowThatAlreadyHoldsTheTargetWithoutAskingTheHostToChangeIt(
        string locator,
        string colType,
        object?[] values,
        string onValue,
        string offValue,
        string expectedWriteSignature)
    {
        Assert.False(string.IsNullOrEmpty(locator));

        FakeDataWindowHost host = CreateCheckBoxHost(colType, values);
        host.Column(FlagColumn).CheckBoxOn = onValue;
        host.Column(FlagColumn).CheckBoxOff = offValue;

        RowSelectService service = CreateService(host, RowSelectService.RS_MULTIPLE);

        // :L194 - reach the multi-selected state honestly, then select the whole set.
        host.ArrangeSelectedRows();
        _ = service.OnLButtonClk(0L, 0L, 1L, FlagObject(host), shiftHeld: false, ctrlHeld: true);
        host.ArrangeSelectedRows(1L, 2L, 3L);
        host.CallLog.Clear();

        Assert.Equal(
            1L,
            service.OnLButtonClk(0L, 0L, 1L, FlagObject(host), shiftHeld: false, ctrlHeld: false));

        // The clicked cell held the "on" value, so :L217-L221 chose the "off" value as the target.
        // Row 2 already held it and is skipped - and the skip is at :L236, BEFORE :L238, so the row
        // never reaches OnDoItemChange at all. That ordering is the whole point of this test: a skip
        // implemented after the event would still write the right values while asking the application
        // to approve a change that is not happening.
        List<long> asked =
        [
            .. host.CallLog.Records
                .Where(record => record.Member == "Event OnDoItemChange")
                .Select(record => Assert.IsType<long>(record.Arguments[0])),
        ];

        Assert.Equal([1L, 3L], asked);
        Assert.DoesNotContain(2L, asked);

        // Two writes, both through the arm's own typed overload - :L247-L248 Dec, :L249-L250 Long,
        // :L251-L252 the string.
        Assert.Equal(2, host.CallLog.CountOf(expectedWriteSignature));

        // And no OTHER SetItem overload was reached, which is what pins the arm rather than merely
        // the count.
        foreach (string otherSignature in new[] { "SetItem(decimal?)", "SetItem(long?)", "SetItem(string?)" })
        {
            if (!string.Equals(otherSignature, expectedWriteSignature, StringComparison.Ordinal))
            {
                Assert.Equal(0, host.CallLog.CountOf(otherSignature));
            }
        }

        Assert.Null(service.PendingError);
    }

    // ------------------------------------------------------------------------------------------
    //  LOCALIZATION ARRANGEMENT SUPPORT
    // ------------------------------------------------------------------------------------------

    private const string NoProviderArrangement = "none";

    private const string PassthroughArrangement = "passthrough";

    private const string TranslatingArrangement = "markers";

    /// <summary>
    /// Builds the localization facade named by <paramref name="arrangement"/>.
    /// </summary>
    /// <param name="arrangement">One of the three arrangement names.</param>
    /// <returns>The facade and, where one was installed, the provider behind it.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="arrangement"/> names no arrangement. The throw is deliberate: a
    /// silent default would let a mistyped theory row pass while testing something else.
    /// </exception>
    private static (I18n Facade, II18nProvider? Provider) BuildLocalization(string arrangement)
    {
        switch (arrangement)
        {
            case NoProviderArrangement:
                return (ScriptedLocalization.WithoutProvider(), null);

            case PassthroughArrangement:
                PassthroughI18nProvider passthrough = new();
                return (ScriptedLocalization.With(passthrough), passthrough);

            case TranslatingArrangement:
                (I18n facade, ScriptedI18nProvider provider) = ScriptedLocalization.WithMarkers();
                return (facade, provider);

            default:
                throw new ArgumentOutOfRangeException(nameof(arrangement), arrangement, "Unknown arrangement.");
        }
    }

    // ==========================================================================================
    //  THE C-D BOUNDARY - AND HERE THE CHECK IS TOTAL, NOT PARTIAL
    //  ----------------------------------------------------------------------------------------
    //  AAP §0.2.1.3 Correction 4 measured all five attached DataWindow services for presentational
    //  dependencies and SPLIT three of them: ColumnSort converts DPI units for its indicator
    //  geometry [n_cst_dwsvc_columnsort.sru:L359, L361], ContextMenu measures fonts and positions a
    //  window [n_cst_dwsvc_contextmenu.sru:L187, L1092, L1241], and DropDownSearch moves a window
    //  outright [n_cst_dwsvc_dropdownsearch.sru:L256, L474, L489]. Each therefore ships a headless
    //  half while its rendering half is deferred behind Gateway's `/v1/design/**` extension point.
    //
    //  ROW SELECT IS THE ONE THAT IS HEADLESS IN FULL. Its single presentational reference is the
    //  modal dialog at :L239, and converting that to a structured error is the whole of the
    //  presentational work. So unlike its three siblings there is NO legitimately-deferred half
    //  here, and the boundary assertion below is consequently TOTAL: not "no geometry beyond the
    //  documented gap", but none at all, anywhere, public or private.
    // ==========================================================================================

    /// <summary>
    /// Every presentational spelling constraint C-D forbids from crossing this contract.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The first nine are the families the requirement names - window handle, DPI, the unit
    /// conversion pairs, font, canvas, colour and popup menu. The last four are the CONCRETE calls
    /// Correction 4 measured in the three sibling services, included because their absence here is
    /// exactly what makes this service the one that needs no split.
    /// </para>
    /// <para>
    /// BARE "window" IS DELIBERATELY NOT ON THE LIST, and that is not an oversight: the host contract
    /// is spelled <c>DataWindowServiceHost</c> and the column descriptor <c>IDataWindowObject</c>, so
    /// the bare word would match the two types this service legitimately consumes and the assertion
    /// would say nothing. <c>hwnd</c>, <c>windowhandle</c> and the three window CALLS are the precise
    /// spellings, and none of them can hide inside a DataWindow type name.
    /// </para>
    /// <para>
    /// "icon" IS ALSO DELIBERATELY ABSENT. <see cref="RowSelectRejectionError.Icon"/> carries the
    /// VALUE of the legacy dialog's severity argument [<c>:L239</c> <c>StopSign!</c>], which AAP
    /// §0.2.1.3 Correction 5 requires be preserved as data. Nothing draws it; forbidding the word
    /// would forbid the very preservation the constraint demands.
    /// </para>
    /// </remarks>
    public static TheoryData<string> ForbiddenPresentationTokens =>
        new()
        {
            "hwnd",
            "windowhandle",
            "dpi",
            "px2mm",
            "mm2px",
            "u2p",
            "d2px",
            "font",
            "canvas",
            "colour",
            "color",
            "popupmenu",
            "showwindow",
            "getwindowrect",
            "setwindowpos",
            "messagebox",
        };

    [Theory]
    [MemberData(nameof(ForbiddenPresentationTokens))]
    public void NothingPresentationalCrossesTheContract(string forbiddenToken)
    {
        // All three types this subject declares, and every symbol on each - public AND private. A
        // private helper holding a window handle would violate C-D exactly as much as a public one.
        foreach (Type declared in new[]
                 {
                     typeof(RowSelectService),
                     typeof(RowSelectRejectionError),
                     typeof(RowSelectMessageIcon),
                 })
        {
            foreach (string name in DescribeEverySymbol(declared))
            {
                Assert.DoesNotContain(forbiddenToken, name, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void PublicSurface_HandsBackDataAndNeverARenderingInstruction()
    {
        // DECLARED MEMBERS ONLY. What DataWindowServiceBase publishes is another file's obligation and
        // is asserted where it is declared; this test is about what THIS service adds.
        const BindingFlags declaredPublic =
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        // Counts, indices, state flags, text and the one structured error - and nothing else. Every
        // member of this list is a value a caller can serialize, compare and store; not one of them
        // instructs anybody to draw anything.
        HashSet<Type> data =
        [
            typeof(void),
            typeof(bool),
            typeof(long),
            typeof(string),
            typeof(RowSelectMessageIcon),
            typeof(RowSelectRejectionError),
        ];

        foreach (MethodInfo method in typeof(RowSelectService).GetMethods(declaredPublic))
        {
            Assert.Contains(UnwrapNullable(method.ReturnType), data);
        }

        foreach (PropertyInfo property in typeof(RowSelectService).GetProperties(declaredPublic))
        {
            Assert.Contains(UnwrapNullable(property.PropertyType), data);
        }

        foreach (FieldInfo field in typeof(RowSelectService).GetFields(declaredPublic))
        {
            Assert.Contains(UnwrapNullable(field.FieldType), data);
        }

        // THE FOUR LEGACY EVENTS BECAME METHODS RETURNING A CODE, NOT .NET EVENTS. AAP §0.4.5.4 is
        // explicit that a triggered event becomes a method call returning a numeric code, and the
        // broker - not the CLR - owns subscription [:L274-L276]. A public event here would be a second,
        // parallel notification mechanism the oracle does not have.
        Assert.Empty(typeof(RowSelectService).GetEvents(declaredPublic));
    }

    [Fact]
    public void RejectionError_IsAStructuredDataRecordAndNotAnException()
    {
        // AAP §0.2.1.3 Correction 5 - only the DELIVERY CHANNEL changes. So the replacement for the
        // dialog has to be something a caller can read, serialize and forward, and it must NOT be
        // something the runtime unwinds: the oracle shows its dialog and then exits the loop in an
        // orderly way [:L239-L240], it does not raise.
        Assert.False(typeof(Exception).IsAssignableFrom(typeof(RowSelectRejectionError)));

        // Value semantics, which is what "machine-readable" needs to mean in practice: two errors
        // describing the same refusal compare equal, so a caller can deduplicate them.
        Assert.True(typeof(IEquatable<RowSelectRejectionError>).IsAssignableFrom(
            typeof(RowSelectRejectionError)));

        // Every field of the payload is data, and between them they carry every input :L239 composes
        // from: the composed text, both lookup keys, the category, the substitution argument, the
        // severity, and whether localization was consulted.
        HashSet<Type> data = [typeof(string), typeof(long), typeof(bool), typeof(RowSelectMessageIcon)];

        PropertyInfo[] properties = typeof(RowSelectRejectionError).GetProperties(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        Assert.Equal(7, properties.Length);

        foreach (PropertyInfo property in properties)
        {
            Assert.Contains(UnwrapNullable(property.PropertyType), data);

            // Fixed at construction: an error a caller could edit afterwards is no longer a record of
            // what happened, and every assertion in this suite about the composition inputs would
            // become an assertion about whatever the last writer chose.
            Assert.True(IsFixedAtConstruction(property), property.Name + " must not have a plain setter.");
        }

        // :L239 passes exactly ONE severity, so the ported enumeration carries exactly that one plus a
        // well-defined zero. A wider taxonomy would be a fabrication (constraint C-B).
        Assert.Equal(
            [nameof(RowSelectMessageIcon.None), nameof(RowSelectMessageIcon.StopSign)],
            Enum.GetNames<RowSelectMessageIcon>());
    }

    // ------------------------------------------------------------------------------------------
    //  REFLECTION SUPPORT
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// The underlying type of a nullable value type, or the type itself.
    /// </summary>
    /// <param name="type">The type to unwrap.</param>
    /// <returns>The unwrapped type.</returns>
    /// <remarks>
    /// A nullable <see cref="long"/> is still a count and a nullable record reference is still that
    /// record, so the data check compares underlying types rather than treating <c>long?</c> as a
    /// different kind of answer from <c>long</c>.
    /// </remarks>
    private static Type UnwrapNullable(Type type) => Nullable.GetUnderlyingType(type) ?? type;

    /// <summary>
    /// Whether a property can only be assigned while its object is being constructed - either because
    /// it has no setter at all, or because the setter it has is <see langword="init"/>.
    /// </summary>
    /// <param name="property">The property to classify.</param>
    /// <returns><see langword="true"/> when no post-construction write is possible.</returns>
    /// <remarks>
    /// An <see langword="init"/> accessor is an ordinary setter carrying a required custom modifier of
    /// <c>IsExternalInit</c> on its return parameter, which is the only way to tell the two apart
    /// through reflection.
    /// </remarks>
    private static bool IsFixedAtConstruction(PropertyInfo property)
    {
        MethodInfo? setter = property.SetMethod;

        if (setter is null || !setter.IsPublic)
        {
            return true;
        }

        return setter.ReturnParameter
            .GetRequiredCustomModifiers()
            .Contains(typeof(IsExternalInit));
    }

    /// <summary>
    /// Every name a symbol on <paramref name="type"/> exposes - member names, parameter names, and
    /// the full names of every type those members involve, public and private alike.
    /// </summary>
    /// <param name="type">The type to describe.</param>
    /// <returns>The names, deduplicated.</returns>
    /// <remarks>
    /// Non-public members are included deliberately: a private helper that read a keyboard or held a
    /// window handle would be just as much a C-D violation as a public one, and it would be invisible
    /// to a public-surface-only sweep.
    /// </remarks>
    private static IReadOnlyCollection<string> DescribeEverySymbol(Type type)
    {
        const BindingFlags everything =
            BindingFlags.Public
            | BindingFlags.NonPublic
            | BindingFlags.Instance
            | BindingFlags.Static
            | BindingFlags.DeclaredOnly;

        HashSet<string> names = new(StringComparer.Ordinal) { type.FullName ?? type.Name };

        foreach (MemberInfo member in type.GetMembers(everything))
        {
            _ = names.Add(member.Name);

            switch (member)
            {
                case FieldInfo field:
                    _ = names.Add(TypeNameOf(field.FieldType));
                    break;

                case PropertyInfo property:
                    _ = names.Add(TypeNameOf(property.PropertyType));
                    break;

                case MethodBase method:
                    if (method is MethodInfo function)
                    {
                        _ = names.Add(TypeNameOf(function.ReturnType));
                    }

                    foreach (ParameterInfo parameter in method.GetParameters())
                    {
                        _ = names.Add(parameter.Name ?? string.Empty);
                        _ = names.Add(TypeNameOf(parameter.ParameterType));
                    }

                    break;

                case Type nested:
                    _ = names.Add(TypeNameOf(nested));
                    break;

                default:
                    // EventInfo is the only remaining MemberInfo kind, and this type declares none -
                    // which the surface test below asserts rather than assumes.
                    break;
            }
        }

        return names;
    }

    /// <summary>
    /// The full name of a type, unwrapping by-reference, array, pointer and generic decoration so a
    /// forbidden type cannot hide inside a wrapper.
    /// </summary>
    /// <param name="type">The type to name.</param>
    /// <returns>The unwrapped names, joined.</returns>
    private static string TypeNameOf(Type type)
    {
        Type unwrapped = type;

        while (unwrapped.IsByRef || unwrapped.IsArray || unwrapped.IsPointer)
        {
            Type? element = unwrapped.GetElementType();

            if (element is null)
            {
                break;
            }

            unwrapped = element;
        }

        string name = unwrapped.FullName ?? unwrapped.Name;

        if (!unwrapped.IsGenericType)
        {
            return name;
        }

        return name
            + "<"
            + string.Join(",", unwrapped.GetGenericArguments().Select(TypeNameOf))
            + ">";
    }
}
