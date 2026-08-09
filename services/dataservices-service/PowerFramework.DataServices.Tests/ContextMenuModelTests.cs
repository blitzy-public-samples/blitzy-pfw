// ==============================================================================================
//  ContextMenuModelTests - characterization of Services/ContextMenuModel.cs against its oracle,
//  ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_contextmenu.sru (1,459 lines, READ ONLY).
//  --------------------------------------------------------------------------------------------
//  These are characterization tests in Michael Feathers' sense (AAP 0.3.3): they assert what the
//  legacy ACTUALLY does, including the ELEVEN defects the refactor is required to preserve, rather
//  than what it arguably should do. Where a test looks like it is asserting a bug, it is - and the
//  assertion carries the locator that proves the bug is the oracle's. EVERY DEFECT TEST FAILS IF THE
//  DEFECT IS "FIXED", which is the whole point of writing them.
//
//  NO DataWindow, NO FONT, NO CLIPBOARD, NO DATABASE AND NO UI IS INVOLVED. Everything runs against
//  FakeDataWindowHost, which satisfies the abstract host contract in memory, so the suite is
//  deterministic and needs no service, no container and no fixture file (constraint C-H).
//
//  THE ELEVEN DEFECTS AND THE TESTS THAT PIN THEM
//    1  MID_RESERVED and MID_COLAUTOWIDTH are BOTH 10000, so MID_RESERVED is unreachable as a
//       distinct dispatch case  ->  TheTwoTenThousandIdentifiersAreBothDeclared,
//                                   MidReservedDispatchesToTheAutoWidthArm
//    2  ColAutoWidth is absent from the emit filter, so clearing it after preparation changes
//       nothing  ->  ClearingColAutoWidthAfterPreparationChangesNothing
//    3  MID_COLREVERTCHECK escapes the ColCheck gate at emit time  ->
//       ClearingColCheckLeavesTheRevertItemVisible
//    4  the two item-copy paths RETURN IMMEDIATELY, yielding a one-item menu  ->
//       AnItemCopyContextYieldsAOneItemMenu, AComputeOutsideTheThreeBandsYieldsAOneItemMenu
//    5  of_GetId(index) reads an uninitialised local, so it can never answer a real id  ->
//       GetIdByIndexAlwaysAnswersZero
//    6  three lookups scan BACKWARD and therefore answer the LAST match  ->
//       GetIndexByTextAnswersTheLastMatch, GetIndexByIdAnswersTheLastMatch,
//       GetIdByTextAnswersTheLastMatch
//    7  UncheckColumn keys its arms on a five-character ColType prefix while its two siblings key
//       theirs on COL_TYPE_*  ->  UncheckColumnReadsAnIntColumnAsAString
//    8  the column copy producer appends the row separator after EVERY row, including the last  ->
//       CopyColumnToTextAlwaysEndsWithTheRowSeparator
//    9  an out-of-range insert answers E_OUT_OF_RANGE while an out-of-range setter answers
//       E_OUT_OF_BOUND  ->  TheTwoOutOfRangeCodesDiffer
//   10  the paste loop is bounded by the pasted line count and INSERTS rows to reach it  ->
//       PastingMoreLinesThanRowsInsertsRows
//   11  the datetime paste arm cannot tell an unconvertible value from 1900-01-01 00:00:00  ->
//       PastingTheUninitialisedDatetimeReportsATypeMismatch
//
//  THE TWENTY-ONE LINES THIS SUITE CANNOT REACH, AND WHY EACH ONE STAYS. Coverage of the file under
//  test is 98.1% of lines, and the remainder is not untested behaviour - it is code that is
//  UNREACHABLE BY CONSTRUCTION and retained because the oracle writes it. Enumerated so that nobody
//  has to rediscover the reason, and so that a later edit which makes one of them reachable is
//  recognised as a real change:
//    * BuildMenu's preparation veto - the oracle tests `Event OnPrepare(...) = 1` [:L146] because a
//      PowerBuilder event is overridable by any descendant; this port is sealed, matching its two
//      committed siblings, and preparation answers 0 on every path. Pinned by
//      PreparationAnswersZeroOnEveryPathSoItsVetoIsDormant.
//    * IsAllowPopup's IsValidObject exit [:L313] - PowerScript can hold a reference to a DESTROYED
//      object; .NET cannot, and every public entry point rejects null before reaching it.
//    * Paste2Column's zero-segment guard [:L953] - the empty-data rejection has already run and the
//      split keeps empties, so the count is never zero.
//    * ToText's non-string arms - the port of String(any) over a table whose every builder stores a
//      non-null string, so only the string arm runs. The others honour the OrderedMap contract's
//      object? value type rather than assuming it.
//    * ItemAt's, InsertItemAt's, PastedValueAt's and NameAt's range guards - private helpers whose
//      every caller range-checks first. They are defence in depth on an invariant, not dead branches
//      of a decision.
//
//  DEFERRED-HALF ASSERTIONS ARE POSITIVE, NOT ABSENT. Rather than merely not testing the deferred
//  geometry, the suite asserts that the auto-width plan carries the RAW Describe answers and the
//  synthesized proxy text untransformed (DECISION 8), that the copy producers RETURN their text and
//  write nothing, and that the ten dialogs arrive as structured errors carrying their keys - so a
//  later "helpful" measurement, clipboard write or message rewording fails a test instead of passing
//  silently.
// ==============================================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Microsoft.Extensions.Options;

using PowerFramework.DataServices.Configuration;
using PowerFramework.DataServices.Domain;
using PowerFramework.DataServices.Expressions;
using PowerFramework.DataServices.Services;
using PowerFramework.Shared.Eventful;
using PowerFramework.Shared.Kernel;
using PowerFramework.Shared.Localization;

using Xunit;

namespace PowerFramework.DataServices.Tests;

/// <summary>
/// Characterization tests for <see cref="ContextMenuModel"/>.
/// </summary>
public sealed class ContextMenuModelTests
{
    private const string Column = "salary";
    private const string HeaderObject = Column + "_t";
    private const string CheckColumn = "flag";

    // The nine command identifiers, restated from n_cst_dwsvc_contextmenu.sru:L37-L45 so that a change
    // on either side is caught rather than silently diverging. TheNineCommandIdentifiers below pins
    // them against the service's own constants.
    private const uint MID_RESERVED = 10000u;
    private const uint MID_COLAUTOWIDTH = 10000u;
    private const uint MID_COLAUTOWIDTH_ALL = 10001u;
    private const uint MID_COLCHECK = 10002u;
    private const uint MID_COLUNCHECK = 10003u;
    private const uint MID_COLREVERTCHECK = 10004u;
    private const uint MID_COLCOPY = 10005u;
    private const uint MID_COLPASTE = 10006u;
    private const uint MID_ITEMCOPY = 10007u;

    /// <summary>The separator marker, which IS the item's text [<c>:L511</c>].</summary>
    private const string Separator = "-";

    /// <summary>The row separator both the copy producer and the paste split use [<c>:L546</c>].</summary>
    private const string RowSeparator = "\r\n";

    // ==============================================================================================
    //  ARRANGEMENT
    // ==============================================================================================

    /// <summary>
    /// Builds the five-toggle options the service binds in its constructor.
    /// </summary>
    /// <param name="colAutoWidth">The auto-width toggle.</param>
    /// <param name="colCheck">The check toggle.</param>
    /// <param name="colCopy">The column-copy toggle.</param>
    /// <param name="colPaste">The paste toggle.</param>
    /// <param name="itemCopy">The item-copy toggle.</param>
    /// <returns>The options.</returns>
    private static IOptions<DataServicesOptions> OptionsWith(
        bool colAutoWidth = true,
        bool colCheck = true,
        bool colCopy = true,
        bool colPaste = true,
        bool itemCopy = true)
    {
        return Options.Create(new DataServicesOptions
        {
            ContextMenu = new ContextMenuOptions
            {
                ColAutoWidth = colAutoWidth,
                ColCheck = colCheck,
                ColCopy = colCopy,
                ColPaste = colPaste,
                ItemCopy = itemCopy,
            },
        });
    }

    /// <summary>
    /// Builds a host carrying one editable grid column plus its header text object, and one context-menu
    /// service attached to it.
    /// </summary>
    /// <param name="options">The toggles, defaulting to all five set.</param>
    /// <param name="i18n">The localization facade, defaulting to one with no provider installed.</param>
    /// <returns>The host and the attached service.</returns>
    /// <remarks>
    /// <c>Processing</c> is <c>"1"</c>, which is <c>STYLE_GRID</c> [<c>n_cst_dwsvc.sru:L145-L146</c>] -
    /// the only style the auto-width block admits [<c>:L266</c>]. It is set explicitly so the dependency
    /// is visible in the arrangement rather than inherited from the fake's default.
    /// </remarks>
    private static (FakeDataWindowHost Host, ContextMenuModel Service) NewService(
        IOptions<DataServicesOptions>? options = null,
        I18n? i18n = null)
    {
        FakeDataWindowHost host = new(new EventBroker());
        host.Processing = "1";

        FakeDataWindowObjectDefinition column = host.AddColumn(Column, "decimal(2)");

        // THE FAKE DEFAULTS TabSequence TO "0", which IsColumnEditable reads as non-editable
        // [n_cst_dwsvc.sru's _of_iscolumneditable]. A real editable column has a positive sequence, so
        // the arrangement sets one - otherwise the check and paste blocks are unreachable.
        column.TabSequence = "50";

        _ = host.AddTextObject(HeaderObject, "header");

        host.SetDescribe(Column + ".Band", "detail");
        host.SetDescribe(Column + ".Type", "column");
        host.SetDescribe(Column + ".edit.style", "edit");
        host.SetDescribe(Column + ".Protect", "0");
        host.SetDescribe(Column + ".visible", "1");
        host.SetDescribe(Column + ".protect", "0");
        host.SetDescribe("DataWindow.Header.Height", "100");

        // READ-ONLY IS SET THROUGH THE FAKE'S OWN PROPERTY AND NOT TAUGHT TO THE DESCRIBE TABLE, because
        // the table is consulted FIRST and a taught answer would then be unoverridable - which is exactly
        // what the tests that make the column non-editable need to be able to do. "no" is the fake's
        // default; it is restated here so the dependency is visible in the arrangement.
        host.ReadOnly = "no";

        ContextMenuModel service = new(
            i18n ?? new I18n(),
            new DataWindowExpressionEvaluator(host),
            options ?? OptionsWith());

        service.OnInit(host);

        return (host, service);
    }

    /// <summary>
    /// A standalone DataWindow-object handle, for the events that only read <c>Name</c> and
    /// <c>Type</c>.
    /// </summary>
    /// <param name="name">The object name.</param>
    /// <param name="type">The OBJECT type - <c>column</c>, <c>compute</c>, <c>text</c>.</param>
    /// <returns>The handle.</returns>
    private static FakeDataWindowObject Dwo(string name, string type)
    {
        return new FakeDataWindowObject(name, "decimal(2)") { Type = type };
    }

    /// <summary>
    /// The pointer state the oracle reads from the DataWindow and this port takes as request data
    /// (DECISION 4).
    /// </summary>
    /// <param name="band">The band token, tab-suffixed exactly as <c>GetBandAtPointer</c> answers it.</param>
    /// <param name="pointerY">The pointer's y position.</param>
    /// <param name="clipboard">The clipboard text the caller supplies (DECISION 3).</param>
    /// <returns>The context.</returns>
    private static ContextMenuPointerContext Pointer(
        string band = "detail\t3",
        long pointerY = 500L,
        string clipboard = "")
    {
        return new ContextMenuPointerContext
        {
            BandAtPointer = band,
            PointerY = pointerY,
            ClipboardText = clipboard,
        };
    }

    /// <summary>The ids of a menu, in order.</summary>
    /// <param name="items">The items.</param>
    /// <returns>The ids.</returns>
    private static List<uint> IdsOf(IReadOnlyList<MenuItemData> items) => [.. items.Select(item => item.Id)];

    /// <summary>The texts of a menu, in order.</summary>
    /// <param name="items">The items.</param>
    /// <returns>The texts.</returns>
    private static List<string> TextsOf(IReadOnlyList<MenuItemData> items) =>
        [.. items.Select(item => item.Text)];

    // ==============================================================================================
    //  THE CONSTANTS, AND DEFECT 1
    // ==============================================================================================

    /// <summary>
    /// The nine command identifiers carry the oracle's values [<c>:L37-L45</c>].
    /// </summary>
    [Fact]
    public void TheNineCommandIdentifiers()
    {
        Assert.Equal(MID_RESERVED, ContextMenuModel.MID_RESERVED);
        Assert.Equal(MID_COLAUTOWIDTH, ContextMenuModel.MID_COLAUTOWIDTH);
        Assert.Equal(MID_COLAUTOWIDTH_ALL, ContextMenuModel.MID_COLAUTOWIDTH_ALL);
        Assert.Equal(MID_COLCHECK, ContextMenuModel.MID_COLCHECK);
        Assert.Equal(MID_COLUNCHECK, ContextMenuModel.MID_COLUNCHECK);
        Assert.Equal(MID_COLREVERTCHECK, ContextMenuModel.MID_COLREVERTCHECK);
        Assert.Equal(MID_COLCOPY, ContextMenuModel.MID_COLCOPY);
        Assert.Equal(MID_COLPASTE, ContextMenuModel.MID_COLPASTE);
        Assert.Equal(MID_ITEMCOPY, ContextMenuModel.MID_ITEMCOPY);
    }

    /// <summary>
    /// DEFECT 1 - both ten-thousand identifiers exist and neither was elided [<c>:L37-L38</c>].
    /// </summary>
    /// <remarks>
    /// FAILS IF THE ALIAS IS "TIDIED AWAY". De-duplicating the pair, or moving the nine ids into a C#
    /// <c>enum</c> where two members of one value invite a cleanup, would delete a name the oracle
    /// declares - and menu ids travel in serialized payloads and characterization recordings.
    /// </remarks>
    [Fact]
    public void TheTwoTenThousandIdentifiersAreBothDeclared()
    {
        Assert.Equal(10000u, ContextMenuModel.MID_RESERVED);
        Assert.Equal(10000u, ContextMenuModel.MID_COLAUTOWIDTH);
        Assert.Equal(ContextMenuModel.MID_RESERVED, ContextMenuModel.MID_COLAUTOWIDTH);
    }

    /// <summary>
    /// The five toggles default to set, in the oracle [<c>:L49-L53</c>] and in the options.
    /// </summary>
    [Fact]
    public void TheFiveTogglesDefaultToSet()
    {
        (_, ContextMenuModel service) = NewService();

        Assert.True(service.ColAutoWidth);
        Assert.True(service.ColCheck);
        Assert.True(service.ColCopy);
        Assert.True(service.ColPaste);
        Assert.True(service.ItemCopy);
    }

    /// <summary>
    /// The five toggles are bound from configuration, not hardcoded a second time.
    /// </summary>
    [Fact]
    public void TheFiveTogglesComeFromOptions()
    {
        (_, ContextMenuModel service) = NewService(OptionsWith(false, false, false, false, false));

        Assert.False(service.ColAutoWidth);
        Assert.False(service.ColCheck);
        Assert.False(service.ColCopy);
        Assert.False(service.ColPaste);
        Assert.False(service.ItemCopy);
    }

    /// <summary>
    /// The constructor rejects each null collaborator.
    /// </summary>
    [Fact]
    public void TheConstructorRejectsNullCollaborators()
    {
        FakeDataWindowHost host = new(new EventBroker());
        DataWindowExpressionEvaluator evaluator = new(host);

        _ = Assert.Throws<ArgumentNullException>(
            () => new ContextMenuModel(null!, evaluator, OptionsWith()));
        _ = Assert.Throws<ArgumentNullException>(
            () => new ContextMenuModel(new I18n(), null!, OptionsWith()));
        _ = Assert.Throws<ArgumentNullException>(
            () => new ContextMenuModel(new I18n(), evaluator, null!));
    }

    // ==============================================================================================
    //  THE ITEM STORE - no host is touched by any test in this region
    // ==============================================================================================

    /// <summary>
    /// The long add stores all five supplied fields and answers the new one-based index
    /// [<c>:L478-L499</c>].
    /// </summary>
    [Fact]
    public void AddMenuStoresEveryField()
    {
        (_, ContextMenuModel service) = NewService();

        int index = service.AddMenu("text", "image", "tip", enabled: false, id: 42u);

        Assert.Equal(1, index);

        MenuItemData item = Assert.Single(service.StoreItems);
        Assert.Equal("text", item.Text);
        Assert.Equal("image", item.Image);
        Assert.Equal("tip", item.TipText);
        Assert.False(item.Enabled);
        Assert.Equal(42u, item.Id);

        // The three fields the plain add never sets, asserted so a later default cannot creep in.
        Assert.False(item.HasSubmenu);
        Assert.Empty(item.Submenu);
        Assert.False(item.Split);
        Assert.False(item.MenuOwner);
    }

    /// <summary>
    /// The three short adds apply the oracle's documented defaults [<c>:L502-L508</c>].
    /// </summary>
    /// <remarks>
    /// EVERY SHORT FORM DELEGATES, so the defaults are the oracle's and not this port's: the
    /// three-argument form defaults the tip to empty AND enabled to set; the four-argument enabled form
    /// defaults only the tip; the four-argument tip form defaults only enabled.
    /// </remarks>
    [Fact]
    public void TheShortAddsApplyTheDocumentedDefaults()
    {
        (_, ContextMenuModel service) = NewService();

        _ = service.AddMenu("a", "ia", 1u);
        _ = service.AddMenu("b", "ib", enabled: false, id: 2u);
        _ = service.AddMenu("c", "ic", "tc", 3u);

        Assert.Equal(string.Empty, service.StoreItems[0].TipText);
        Assert.True(service.StoreItems[0].Enabled);

        Assert.Equal(string.Empty, service.StoreItems[1].TipText);
        Assert.False(service.StoreItems[1].Enabled);

        Assert.Equal("tc", service.StoreItems[2].TipText);
        Assert.True(service.StoreItems[2].Enabled);
    }

    /// <summary>
    /// An empty label is refused with <c>0</c> and stores nothing [<c>:L480</c>].
    /// </summary>
    [Fact]
    public void AnEmptyLabelIsRefused()
    {
        (_, ContextMenuModel service) = NewService();

        Assert.Equal(0, service.AddMenu(string.Empty, "image", "tip", enabled: true, id: 1u));
        Assert.Empty(service.StoreItems);
    }

    /// <summary>
    /// The separator is an ordinary item whose TEXT is the hyphen [<c>:L511</c>].
    /// </summary>
    /// <remarks>
    /// THE LITERAL IS THE MARKER. The collapse pass recognises a separator by comparing the text
    /// [<c>:L169</c>], so introducing a boolean flag instead would leave the marker unreachable.
    /// </remarks>
    [Fact]
    public void AddSeparatorStoresTheHyphenMarker()
    {
        (_, ContextMenuModel service) = NewService();

        Assert.Equal(1, service.AddSeparator());

        MenuItemData item = Assert.Single(service.StoreItems);
        Assert.Equal(Separator, item.Text);
        Assert.Equal(0u, item.Id);
        Assert.True(item.Enabled);
    }

    /// <summary>
    /// Positional insertion is ONE-BASED, and both boundaries are legal [<c>:L486</c>].
    /// </summary>
    /// <param name="position">The one-based insert position.</param>
    /// <param name="expectedTexts">The store's texts afterwards.</param>
    [Theory]
    [InlineData(1u, "new,a,b,c")]
    [InlineData(2u, "a,new,b,c")]
    [InlineData(3u, "a,b,new,c")]
    [InlineData(4u, "a,b,c,new")]
    public void PositionalInsertionIsOneBased(uint position, string expectedTexts)
    {
        (_, ContextMenuModel service) = NewService();
        _ = service.AddMenu("a", string.Empty, 1u);
        _ = service.AddMenu("b", string.Empty, 2u);
        _ = service.AddMenu("c", string.Empty, 3u);

        int index = service.InsertMenu(position, byPosition: true, "new", string.Empty, 9u);

        Assert.Equal((int)position, index);
        Assert.Equal(expectedTexts.Split(','), TextsOf(service.StoreItems));
    }

    /// <summary>
    /// Position <c>0</c> and position <c>Count + 2</c> are both out of range [<c>:L486</c>].
    /// </summary>
    /// <remarks>
    /// THE UPPER BOUND IS <c>UpperBound() + 1</c>, NOT <c>UpperBound()</c>, which is what makes the
    /// append idiom every add uses legal - and it is why <c>Count + 1</c> succeeds above while
    /// <c>Count + 2</c> fails here.
    /// </remarks>
    [Fact]
    public void PositionalInsertionRejectsBothEnds()
    {
        (_, ContextMenuModel service) = NewService();
        _ = service.AddMenu("a", string.Empty, 1u);

        Assert.Equal(
            (int)RetCode.E_OUT_OF_RANGE,
            service.InsertMenu(0u, byPosition: true, "new", string.Empty, 9u));
        Assert.Equal(
            (int)RetCode.E_OUT_OF_RANGE,
            service.InsertMenu(3u, byPosition: true, "new", string.Empty, 9u));
        _ = Assert.Single(service.StoreItems);
    }

    /// <summary>
    /// Insertion by ID resolves the anchor through the backward lookup [<c>:L483</c>].
    /// </summary>
    [Fact]
    public void InsertionByIdResolvesTheAnchor()
    {
        (_, ContextMenuModel service) = NewService();
        _ = service.AddMenu("a", string.Empty, 1u);
        _ = service.AddMenu("b", string.Empty, 2u);

        int index = service.InsertMenu(2u, byPosition: false, "new", string.Empty, 9u);

        Assert.Equal(2, index);
        Assert.Equal(["a", "new", "b"], TextsOf(service.StoreItems));
    }

    /// <summary>
    /// An unknown anchor id resolves to index <c>0</c> and is therefore out of range
    /// [<c>:L483</c>, <c>:L486</c>].
    /// </summary>
    [Fact]
    public void AnUnknownAnchorIdIsOutOfRange()
    {
        (_, ContextMenuModel service) = NewService();
        _ = service.AddMenu("a", string.Empty, 1u);

        Assert.Equal(
            (int)RetCode.E_OUT_OF_RANGE,
            service.InsertMenu(77u, byPosition: false, "new", string.Empty, 9u));
    }

    /// <summary>
    /// The positional separator insert delegates to the menu insert with the hyphen [<c>:L511</c>].
    /// </summary>
    [Fact]
    public void InsertSeparatorInsertsTheHyphen()
    {
        (_, ContextMenuModel service) = NewService();
        _ = service.AddMenu("a", string.Empty, 1u);

        Assert.Equal(1, service.InsertSeparator(1u, byPosition: true));
        Assert.Equal([Separator, "a"], TextsOf(service.StoreItems));
    }

    /// <summary>
    /// DEFECT 9 - the insert family and the setter family answer DIFFERENT codes for one fault class
    /// [<c>:L486</c> versus <c>:L410</c>].
    /// </summary>
    /// <remarks>
    /// FAILS IF THE TWO ARE HARMONISED. An out-of-range insert answers <c>E_OUT_OF_RANGE</c> (-13) and
    /// an out-of-range setter answers <c>E_OUT_OF_BOUND</c> (-12). A caller that tests for one code sees
    /// the other from half the surface, and the oracle is the reason.
    /// </remarks>
    [Fact]
    public void TheTwoOutOfRangeCodesDiffer()
    {
        (_, ContextMenuModel service) = NewService();

        Assert.Equal(
            (int)RetCode.E_OUT_OF_RANGE,
            service.InsertMenu(5u, byPosition: true, "new", string.Empty, 9u));
        Assert.Equal(RetCode.E_OUT_OF_BOUND, service.SetText(5u, byPosition: true, "new"));
        Assert.NotEqual(RetCode.E_OUT_OF_RANGE, RetCode.E_OUT_OF_BOUND);
    }

    /// <summary>
    /// An empty label is refused by the insert with <c>0</c> BEFORE the range test [<c>:L480</c>].
    /// </summary>
    /// <remarks>
    /// THE ORDER MATTERS AND IS THE ORACLE'S: an empty label at an impossible position answers
    /// <c>0</c>, not <c>E_OUT_OF_RANGE</c>, because the label guard runs first.
    /// </remarks>
    [Fact]
    public void TheEmptyLabelGuardPrecedesTheRangeGuard()
    {
        (_, ContextMenuModel service) = NewService();

        Assert.Equal(0, service.InsertMenu(99u, byPosition: true, string.Empty, string.Empty, 9u));
    }

    /// <summary>
    /// DEFECT 6 - the by-text lookup scans BACKWARD and answers the LAST match [<c>:L357-L364</c>].
    /// </summary>
    /// <remarks>
    /// FAILS IF THE SCAN IS "CORRECTED" to run forward. The oracle's loop is
    /// <c>for nIndex = UpperBound(Items) to 1 step -1</c>, so with two items sharing a label the second
    /// one wins - and every caller that resolves an anchor or a target inherits that.
    /// </remarks>
    [Fact]
    public void GetIndexByTextAnswersTheLastMatch()
    {
        (_, ContextMenuModel service) = NewService();
        _ = service.AddMenu("same", string.Empty, 1u);
        _ = service.AddMenu("other", string.Empty, 2u);
        _ = service.AddMenu("same", string.Empty, 3u);

        Assert.Equal(3, service.GetIndex("same"));
    }

    /// <summary>
    /// DEFECT 6 - the by-id lookup scans BACKWARD too [<c>:L373-L380</c>].
    /// </summary>
    [Fact]
    public void GetIndexByIdAnswersTheLastMatch()
    {
        (_, ContextMenuModel service) = NewService();
        _ = service.AddMenu("a", string.Empty, 7u);
        _ = service.AddMenu("b", string.Empty, 7u);

        Assert.Equal(2, service.GetIndex(7u));
    }

    /// <summary>
    /// DEFECT 6 - and so does the by-text id lookup [<c>:L1421-L1428</c>].
    /// </summary>
    [Fact]
    public void GetIdByTextAnswersTheLastMatch()
    {
        (_, ContextMenuModel service) = NewService();
        _ = service.AddMenu("same", string.Empty, 11u);
        _ = service.AddMenu("same", string.Empty, 22u);

        Assert.Equal(22u, service.GetId("same"));
    }

    /// <summary>
    /// A miss answers <c>0</c> from every lookup [<c>:L363</c>, <c>:L379</c>, <c>:L1427</c>].
    /// </summary>
    [Fact]
    public void AMissAnswersZero()
    {
        (_, ContextMenuModel service) = NewService();
        _ = service.AddMenu("a", string.Empty, 1u);

        Assert.Equal(0, service.GetIndex("absent"));
        Assert.Equal(0, service.GetIndex(99u));
        Assert.Equal(0u, service.GetId("absent"));
    }

    /// <summary>
    /// DEFECT 5 - the by-index id lookup can NEVER answer a real id [<c>:L366-L371</c>].
    /// </summary>
    /// <remarks>
    /// FAILS IF THE PARAMETER IS "WIRED UP". The oracle validates its <c>index</c> argument and then
    /// indexes with the UNINITIALISED local <c>nIndex</c>, which PowerScript zero-initialises - so it
    /// reads <c>Items[0]</c> of a one-based array and yields the zero-valued initial structure. Every
    /// in-range call answers <c>0</c>; only the out-of-range guard is observable.
    /// </remarks>
    [Fact]
    public void GetIdByIndexAlwaysAnswersZero()
    {
        (_, ContextMenuModel service) = NewService();
        _ = service.AddMenu("a", string.Empty, 11u);
        _ = service.AddMenu("b", string.Empty, 22u);

        Assert.Equal(0u, service.GetId(1));
        Assert.Equal(0u, service.GetId(2));

        // Out of range answers zero as well, so the two outcomes are indistinguishable [:L369].
        Assert.Equal(0u, service.GetId(3));
        Assert.Equal(0u, service.GetId(0));
    }

    /// <summary>
    /// The count is the store's upper bound [<c>:L1430-L1434</c>].
    /// </summary>
    [Fact]
    public void GetCountCountsTheStore()
    {
        (_, ContextMenuModel service) = NewService();

        Assert.Equal(0, service.GetCount());

        _ = service.AddMenu("a", string.Empty, 1u);
        _ = service.AddSeparator();

        Assert.Equal(2, service.GetCount());
    }

    /// <summary>
    /// Removal works positionally and by id, and both answer <see cref="RetCode.OK"/>
    /// [<c>:L383-L397</c>].
    /// </summary>
    [Fact]
    public void RemovalWorksBothWays()
    {
        (_, ContextMenuModel service) = NewService();
        _ = service.AddMenu("a", string.Empty, 1u);
        _ = service.AddMenu("b", string.Empty, 2u);
        _ = service.AddMenu("c", string.Empty, 3u);

        Assert.Equal(RetCode.OK, service.Remove(2u, byPosition: true));
        Assert.Equal(["a", "c"], TextsOf(service.StoreItems));

        Assert.Equal(RetCode.OK, service.Remove(3u, byPosition: false));
        Assert.Equal(["a"], TextsOf(service.StoreItems));
    }

    /// <summary>
    /// Removing an absent target changes nothing and still answers <see cref="RetCode.OK"/>.
    /// </summary>
    /// <remarks>
    /// THE ORACLE HAS NO NOT-FOUND CODE ON THIS PATH, so a caller cannot tell a removal from a no-op.
    /// Preserved rather than improved (constraint C-B).
    /// </remarks>
    [Fact]
    public void RemovingAnAbsentTargetIsANoOp()
    {
        (_, ContextMenuModel service) = NewService();
        _ = service.AddMenu("a", string.Empty, 1u);

        Assert.Equal(RetCode.OK, service.Remove(99u, byPosition: false));
        _ = Assert.Single(service.StoreItems);
    }

    /// <summary>
    /// Removing everything empties the store [<c>:L444-L457</c>].
    /// </summary>
    [Fact]
    public void RemoveAllEmptiesTheStore()
    {
        (_, ContextMenuModel service) = NewService();
        _ = service.AddMenu("a", string.Empty, 1u);
        _ = service.AddSeparator();

        Assert.Equal(RetCode.OK, service.RemoveAll());
        Assert.Empty(service.StoreItems);
        Assert.Equal(0, service.GetCount());
    }

    /// <summary>
    /// The four setters and the four getters round-trip, addressed both ways
    /// [<c>:L404-L474</c>, <c>:L554-L600</c>].
    /// </summary>
    [Fact]
    public void TheSettersAndGettersRoundTrip()
    {
        (_, ContextMenuModel service) = NewService();
        _ = service.AddMenu("a", "ia", "ta", enabled: true, id: 5u);

        Assert.Equal(RetCode.OK, service.SetText(1u, byPosition: true, "A"));
        Assert.Equal(RetCode.OK, service.SetImage(5u, byPosition: false, "IA"));
        Assert.Equal(RetCode.OK, service.SetTipText(1u, byPosition: true, "TA"));
        Assert.Equal(RetCode.OK, service.Enable(5u, byPosition: false, enable: false));

        Assert.Equal("A", service.GetText(1u, byPosition: true));
        Assert.Equal("IA", service.GetImage(5u, byPosition: false));
        Assert.Equal("TA", service.GetTipText(1u, byPosition: true));
        Assert.False(service.IsEnabled(5u, byPosition: false));
    }

    /// <summary>
    /// The getters answer the EMPTY STRING and <see langword="false"/> out of range, never a raise
    /// [<c>:L561</c>, <c>:L573</c>, <c>:L585</c>, <c>:L597</c>].
    /// </summary>
    [Fact]
    public void TheGettersAnswerEmptyOutOfRange()
    {
        (_, ContextMenuModel service) = NewService();

        Assert.Equal(string.Empty, service.GetText(1u, byPosition: true));
        Assert.Equal(string.Empty, service.GetImage(1u, byPosition: true));
        Assert.Equal(string.Empty, service.GetTipText(1u, byPosition: true));
        Assert.False(service.IsEnabled(1u, byPosition: true));
    }

    /// <summary>
    /// The four setters answer <see cref="RetCode.E_OUT_OF_BOUND"/> out of range
    /// [<c>:L410</c>, <c>:L424</c>, <c>:L438</c>, <c>:L470</c>].
    /// </summary>
    [Fact]
    public void TheSettersAnswerOutOfBound()
    {
        (_, ContextMenuModel service) = NewService();

        Assert.Equal(RetCode.E_OUT_OF_BOUND, service.SetText(1u, byPosition: true, "x"));
        Assert.Equal(RetCode.E_OUT_OF_BOUND, service.SetImage(1u, byPosition: true, "x"));
        Assert.Equal(RetCode.E_OUT_OF_BOUND, service.SetTipText(1u, byPosition: true, "x"));
        Assert.Equal(RetCode.E_OUT_OF_BOUND, service.Enable(1u, byPosition: true, enable: true));
    }

    /// <summary>
    /// The hierarchical add stores the child ON the parent and marks the parent as owning a submenu
    /// (DECISION 1, from <c>:L272-L274</c>).
    /// </summary>
    /// <remarks>
    /// THE PARENT IS ONE ITEM, NOT TWO. The oracle adds the parent through the class-name submenu
    /// overload and then fetches the created menu to add the child to it; here the child travels inside
    /// the parent, so the emitted menu carries the same shape without a popup-menu type existing.
    /// </remarks>
    [Fact]
    public void TheHierarchicalAddNestsTheChild()
    {
        (_, ContextMenuModel service) = NewService();

        Assert.Equal(1, service.AddSubmenuParent("parent", "img", "tip", split: true, id: 100u));
        Assert.Equal(1, service.AddSubmenuItem(100u, "child", string.Empty, "ctip", 101u));

        MenuItemData parent = Assert.Single(service.StoreItems);
        Assert.True(parent.HasSubmenu);
        Assert.Equal(100u, parent.SubmenuHandle);
        Assert.True(parent.Split);
        Assert.True(parent.MenuOwner);

        MenuItemData child = Assert.Single(parent.Submenu);
        Assert.Equal("child", child.Text);
        Assert.Equal(101u, child.Id);
        Assert.False(child.HasSubmenu);
    }

    /// <summary>
    /// A child addressed at an absent parent answers <see cref="RetCode.E_OUT_OF_BOUND"/>.
    /// </summary>
    [Fact]
    public void AChildWithoutAParentIsOutOfBound()
    {
        (_, ContextMenuModel service) = NewService();

        Assert.Equal(
            (int)RetCode.E_OUT_OF_BOUND,
            service.AddSubmenuItem(100u, "child", string.Empty, "tip", 101u));
    }

    // ==============================================================================================
    //  THE EMIT PASS - the toggle filter and the separator collapse, and DEFECTS 2 AND 3
    // ==============================================================================================

    /// <summary>
    /// Every combination of the five toggles, asserting exactly which of the eight gated ids survives
    /// the emit filter [<c>:L156-L168</c>].
    /// </summary>
    /// <param name="colAutoWidth">The auto-width toggle.</param>
    /// <param name="colCheck">The check toggle.</param>
    /// <param name="colCopy">The column-copy toggle.</param>
    /// <param name="colPaste">The paste toggle.</param>
    /// <param name="itemCopy">The item-copy toggle.</param>
    /// <remarks>
    /// <para>
    /// THIRTY-TWO CASES, GENERATED RATHER THAN LISTED, because the interesting property is that only
    /// FOUR of the eight ids are gated at all: the two check items by one toggle, the copy item, the
    /// paste item and the item-copy item by one each. The auto-width pair and the revert item are
    /// UNGATED here - see the two defect tests below, which assert those two asymmetries on their own so
    /// that a regression names itself rather than hiding inside this matrix.
    /// </para>
    /// <para>
    /// THE STORE IS BUILT DIRECTLY rather than through preparation, so this matrix tests the filter and
    /// nothing else - preparation's own add-time gating is tested separately.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(AllToggleCombinations))]
    public void TheEmitFilterSuppressesExactlyFourIds(
        bool colAutoWidth,
        bool colCheck,
        bool colCopy,
        bool colPaste,
        bool itemCopy)
    {
        (_, ContextMenuModel service) = NewService(
            OptionsWith(colAutoWidth, colCheck, colCopy, colPaste, itemCopy));

        foreach (uint id in AllGatedIds)
        {
            _ = service.AddMenu("item" + id.ToString(CultureInfo.InvariantCulture), string.Empty, id);
        }

        List<uint> expected = [];
        foreach (uint id in AllGatedIds)
        {
            bool suppressed =
                (!colCheck && (id == MID_COLCHECK || id == MID_COLUNCHECK))
                || (!colCopy && id == MID_COLCOPY)
                || (!colPaste && id == MID_COLPASTE)
                || (!itemCopy && id == MID_ITEMCOPY);

            if (!suppressed)
            {
                expected.Add(id);
            }
        }

        Assert.Equal(expected, IdsOf(service.EmitVisibleItems(service.GetCount())));
    }

    /// <summary>
    /// The eight ids the emit filter sees, in the order preparation would have added them.
    /// </summary>
    private static IEnumerable<uint> AllGatedIds =>
    [
        MID_COLAUTOWIDTH,
        MID_COLAUTOWIDTH_ALL,
        MID_COLCHECK,
        MID_COLUNCHECK,
        MID_COLREVERTCHECK,
        MID_COLCOPY,
        MID_COLPASTE,
        MID_ITEMCOPY,
    ];

    /// <summary>All thirty-two combinations of the five toggles.</summary>
    /// <returns>The combinations.</returns>
    public static TheoryData<bool, bool, bool, bool, bool> AllToggleCombinations()
    {
        TheoryData<bool, bool, bool, bool, bool> data = [];

        for (int mask = 0; mask < 32; mask++)
        {
            data.Add(
                (mask & 1) != 0,
                (mask & 2) != 0,
                (mask & 4) != 0,
                (mask & 8) != 0,
                (mask & 16) != 0);
        }

        return data;
    }

    /// <summary>
    /// DEFECT 3 - clearing the check toggle leaves the REVERT item visible [<c>:L157</c>].
    /// </summary>
    /// <remarks>
    /// FAILS IF <c>MID_COLREVERTCHECK</c> IS ADDED TO THE FILTER. Preparation gates all three check
    /// items behind one toggle at add time [<c>:L283</c>], but the emit filter names only two of them -
    /// so a menu prepared with the toggle set and emitted with it cleared still offers the third.
    /// </remarks>
    [Fact]
    public void ClearingColCheckLeavesTheRevertItemVisible()
    {
        (_, ContextMenuModel service) = NewService();

        _ = service.AddMenu("check", string.Empty, MID_COLCHECK);
        _ = service.AddMenu("uncheck", string.Empty, MID_COLUNCHECK);
        _ = service.AddMenu("revert", string.Empty, MID_COLREVERTCHECK);

        service.ColCheck = false;

        Assert.Equal([MID_COLREVERTCHECK], IdsOf(service.EmitVisibleItems(service.GetCount())));
    }

    /// <summary>
    /// DEFECT 2 - clearing the auto-width toggle after preparation changes NOTHING [<c>:L156-L185</c>].
    /// </summary>
    /// <remarks>
    /// FAILS IF AN AUTO-WIDTH FILTER IS ADDED. The toggle is consulted only at add time [<c>:L266</c>],
    /// so neither auto-width id can be suppressed at emit time. The four other toggles can.
    /// </remarks>
    [Fact]
    public void ClearingColAutoWidthAfterPreparationChangesNothing()
    {
        (_, ContextMenuModel service) = NewService();

        _ = service.AddMenu("auto", string.Empty, MID_COLAUTOWIDTH);
        _ = service.AddMenu("all", string.Empty, MID_COLAUTOWIDTH_ALL);

        List<uint> withToggleSet = IdsOf(service.EmitVisibleItems(service.GetCount()));

        service.ColAutoWidth = false;
        List<uint> withToggleCleared = IdsOf(service.EmitVisibleItems(service.GetCount()));

        Assert.Equal([MID_COLAUTOWIDTH, MID_COLAUTOWIDTH_ALL], withToggleSet);
        Assert.Equal(withToggleSet, withToggleCleared);
    }

    /// <summary>
    /// The separator collapse, as a matrix [<c>:L169-L184</c>].
    /// </summary>
    /// <param name="stored">The stored texts, comma-separated.</param>
    /// <param name="expected">The emitted texts, comma-separated, or the empty string for none.</param>
    /// <remarks>
    /// THE THREE CONSEQUENCES ARE ALL HERE: a LEADING separator is suppressed because no real item has
    /// been emitted yet; CONSECUTIVE separators collapse because the pending flag is merely set again;
    /// and a TRAILING separator is dropped because the loop ends with the flag set and nothing after it.
    /// </remarks>
    [Theory]
    [InlineData("-,a", "a")]
    [InlineData("-,-,-,a", "a")]
    [InlineData("a,-", "a")]
    [InlineData("a,-,-", "a")]
    [InlineData("a,-,b", "a,-,b")]
    [InlineData("a,-,-,b", "a,-,b")]
    [InlineData("a,-,-,-,b", "a,-,b")]
    [InlineData("-,a,-,b,-", "a,-,b")]
    [InlineData("-", "")]
    [InlineData("-,-,-", "")]
    [InlineData("a,b", "a,b")]
    public void TheSeparatorCollapseMatrix(string stored, string expected)
    {
        (_, ContextMenuModel service) = NewService();

        uint nextId = 1u;
        foreach (string text in stored.Split(','))
        {
            if (string.Equals(text, Separator, StringComparison.Ordinal))
            {
                _ = service.AddSeparator();
            }
            else
            {
                _ = service.AddMenu(text, string.Empty, nextId++);
            }
        }

        List<string> emitted = TextsOf(service.EmitVisibleItems(service.GetCount()));

        Assert.Equal(
            expected.Length == 0 ? [] : expected.Split(','),
            emitted);
    }

    /// <summary>
    /// An emitted separator carries the marker text, no id and the enabled flag [<c>:L176</c>].
    /// </summary>
    [Fact]
    public void AnEmittedSeparatorIsTheMarkerItem()
    {
        (_, ContextMenuModel service) = NewService();
        _ = service.AddMenu("a", string.Empty, 1u);
        _ = service.AddSeparator();
        _ = service.AddMenu("b", string.Empty, 2u);

        MenuItemData separator = service.EmitVisibleItems(service.GetCount())[1];

        Assert.Equal(Separator, separator.Text);
        Assert.Equal(0u, separator.Id);
        Assert.True(separator.Enabled);
    }

    /// <summary>
    /// An item carrying a submenu is emitted WITH its children [<c>:L179-L181</c>].
    /// </summary>
    /// <remarks>
    /// THE ORACLE CHOOSES BETWEEN TWO ADDS on the child menu's validity; here one add serves both and the
    /// distinction rides on the item, so the emitted parent must still carry its child collection and its
    /// split flag intact.
    /// </remarks>
    [Fact]
    public void ASubmenuParentIsEmittedWithItsChildren()
    {
        (_, ContextMenuModel service) = NewService();
        _ = service.AddSubmenuParent("parent", "img", "tip", split: true, id: MID_COLAUTOWIDTH);
        _ = service.AddSubmenuItem(MID_COLAUTOWIDTH, "child", string.Empty, "ctip", MID_COLAUTOWIDTH_ALL);

        MenuItemData parent = Assert.Single(service.EmitVisibleItems(service.GetCount()));

        Assert.True(parent.HasSubmenu);
        Assert.True(parent.Split);
        Assert.Equal(MID_COLAUTOWIDTH_ALL, Assert.Single(parent.Submenu).Id);
    }

    // ==============================================================================================
    //  ONPREPARE - the default item set, and DEFECT 4
    // ==============================================================================================

    /// <summary>
    /// DEFECT 4 - an item-copy context yields a ONE-ITEM menu and nothing else [<c>:L241-L244</c>].
    /// </summary>
    /// <param name="objectType">The clicked object's type - both admitted types are tested.</param>
    /// <remarks>
    /// FAILS IF THE EARLY RETURN IS REMOVED. The oracle adds the cell-copy item and RETURNS, so none of
    /// the auto-width, check, copy or paste items is ever considered - not even the separator that would
    /// otherwise open the auto-width block.
    /// </remarks>
    [Theory]
    [InlineData("column")]
    [InlineData("compute")]
    public void AnItemCopyContextYieldsAOneItemMenu(string objectType)
    {
        (_, ContextMenuModel service) = NewService();

        Assert.Equal(0L, service.OnPrepare(3L, Dwo(Column, objectType), Pointer()));

        MenuItemData item = Assert.Single(service.StoreItems);
        Assert.Equal(MID_ITEMCOPY, item.Id);
        Assert.Equal("复制单元格", item.Text);
        Assert.Equal("Copy!", item.Image);
        Assert.Equal("复制单元格值", item.TipText);
    }

    /// <summary>
    /// DEFECT 4, second path - a COMPUTE outside the three named bands yields a one-item menu with the
    /// SHORTER labels [<c>:L245-L249</c>].
    /// </summary>
    /// <remarks>
    /// THE LABELS DIFFER FROM THE FIRST PATH'S and the difference is the oracle's: this arm says
    /// "复制" / "复制值" where the first says "复制单元格" / "复制单元格值". Row zero is what steers a
    /// compute here rather than into the first arm.
    /// </remarks>
    [Fact]
    public void AComputeOutsideTheThreeBandsYieldsAOneItemMenu()
    {
        (_, ContextMenuModel service) = NewService();

        Assert.Equal(0L, service.OnPrepare(0L, Dwo("total", "compute"), Pointer("summary\t1")));

        MenuItemData item = Assert.Single(service.StoreItems);
        Assert.Equal(MID_ITEMCOPY, item.Id);
        Assert.Equal("复制", item.Text);
        Assert.Equal("复制值", item.TipText);
    }

    /// <summary>
    /// A compute in one of the three named bands does NOT take the second early return [<c>:L246</c>].
    /// </summary>
    /// <param name="band">The band token.</param>
    [Theory]
    [InlineData("header\t1")]
    [InlineData("foreground\t1")]
    [InlineData("background\t1")]
    public void AComputeInsideTheThreeBandsFallsThrough(string band)
    {
        (_, ContextMenuModel service) = NewService();

        _ = service.OnPrepare(0L, Dwo("total", "compute"), Pointer(band, pointerY: 5000L));

        Assert.DoesNotContain(MID_ITEMCOPY, IdsOf(service.StoreItems));
    }

    /// <summary>
    /// Clearing the item-copy toggle skips the whole block, early returns included [<c>:L240</c>].
    /// </summary>
    [Fact]
    public void ClearingItemCopySkipsBothEarlyReturns()
    {
        (_, ContextMenuModel service) = NewService(OptionsWith(itemCopy: false));

        _ = service.OnPrepare(3L, Dwo(Column, "column"), Pointer());

        Assert.DoesNotContain(MID_ITEMCOPY, IdsOf(service.StoreItems));
    }

    /// <summary>
    /// The band token is split at the tab, so only the band NAME is compared [<c>:L233</c>].
    /// </summary>
    [Fact]
    public void TheBandTokenIsSplitAtTheTab()
    {
        (_, ContextMenuModel service) = NewService();

        // "header\t1" must behave exactly as "header" does: the header-clicked arm adds the flat
        // auto-width item for an object whose name does not end in the header suffix.
        _ = service.OnPrepare(0L, Dwo("anything", "text"), Pointer("header\t1"));

        Assert.Equal([MID_COLAUTOWIDTH_ALL], IdsOf(service.EmitVisibleItems(service.GetCount())));
    }

    /// <summary>
    /// A background or foreground click ABOVE the header height is promoted to the header band
    /// [<c>:L234-L238</c>].
    /// </summary>
    /// <param name="band">The band token before promotion.</param>
    [Theory]
    [InlineData("background\t0")]
    [InlineData("foreground\t0")]
    public void ABandIsPromotedToHeaderAboveTheHeaderHeight(string band)
    {
        (_, ContextMenuModel service) = NewService();

        // The header height is 100; a pointer at 50 is inside it.
        _ = service.OnPrepare(0L, Dwo("anything", "text"), Pointer(band, pointerY: 50L));

        Assert.Equal([MID_COLAUTOWIDTH_ALL], IdsOf(service.EmitVisibleItems(service.GetCount())));
    }

    /// <summary>
    /// The same click BELOW the header height is not promoted, so no header arm runs [<c>:L236</c>].
    /// </summary>
    [Fact]
    public void ABandIsNotPromotedBelowTheHeaderHeight()
    {
        (_, ContextMenuModel service) = NewService();

        _ = service.OnPrepare(0L, Dwo("anything", "text"), Pointer("background\t0", pointerY: 5000L));

        Assert.Empty(service.EmitVisibleItems(service.GetCount()));
    }

    /// <summary>
    /// A header click on a <c>_t</c> object over a real column produces the auto-width PARENT and its
    /// CHILD [<c>:L269-L274</c>].
    /// </summary>
    /// <remarks>
    /// THE CHILD'S ID IS <c>MID_COLAUTOWIDTH_ALL</c> AND IT LIVES UNDER THE PARENT, which is DECISION 1
    /// reproducing the oracle's create-then-fetch-then-add sequence as data.
    /// </remarks>
    [Fact]
    public void AHeaderClickOverAColumnProducesTheAutoWidthHierarchy()
    {
        (FakeDataWindowHost host, ContextMenuModel service) = NewService();
        host.ReadOnly = "no";

        _ = service.OnPrepare(0L, Dwo(HeaderObject, "text"), Pointer("header\t1"));

        Assert.Equal(Column, service.ClickedColumn);

        MenuItemData parent = service.EmitVisibleItems(service.GetCount())[0];
        Assert.Equal(MID_COLAUTOWIDTH, parent.Id);
        Assert.Equal("自动列宽", parent.Text);
        Assert.Equal("SizeHorizontal!", parent.Image);
        Assert.Equal("自动调整列宽度", parent.TipText);
        Assert.True(parent.Split);

        MenuItemData child = Assert.Single(parent.Submenu);
        Assert.Equal(MID_COLAUTOWIDTH_ALL, child.Id);
        Assert.Equal("所有列", child.Text);
        Assert.Equal(string.Empty, child.Image);
        Assert.Equal("自动调整所有列宽度", child.TipText);
    }

    /// <summary>
    /// The FLAT header arm carries the "all columns" tip, not the "this column" one [<c>:L276</c>].
    /// </summary>
    /// <remarks>
    /// THE TWO ARMS SHARE A LABEL AND DIFFER IN THEIR TIP, which is easy to lose in a port: the flat form
    /// says 自动调整所有列宽度 where the hierarchical parent says 自动调整列宽度.
    /// </remarks>
    [Fact]
    public void TheFlatHeaderArmCarriesTheAllColumnsTip()
    {
        (_, ContextMenuModel service) = NewService();

        _ = service.OnPrepare(0L, Dwo("plain_header", "text"), Pointer("header\t1"));

        MenuItemData item = Assert.Single(service.EmitVisibleItems(service.GetCount()));
        Assert.Equal(MID_COLAUTOWIDTH_ALL, item.Id);
        Assert.Equal("自动列宽", item.Text);
        Assert.Equal("SizeHorizontal!", item.Image);
        Assert.Equal("自动调整所有列宽度", item.TipText);
        Assert.False(item.HasSubmenu);
    }

    /// <summary>
    /// A <c>_t</c> object whose derived name is NOT a column or compute clears the clicked column
    /// [<c>:L262</c>].
    /// </summary>
    /// <remarks>
    /// THE DEFAULT ARM WRITES THE EMPTY STRING BACK, which is what makes the flat auto-width arm run
    /// instead of the hierarchical one - the field is set BEFORE the type is checked.
    /// </remarks>
    [Fact]
    public void ANonColumnDerivedNameClearsTheClickedColumn()
    {
        (FakeDataWindowHost host, ContextMenuModel service) = NewService();
        host.SetDescribe("caption.Type", "text");

        _ = service.OnPrepare(0L, Dwo("caption_t", "text"), Pointer("header\t1"));

        Assert.Equal(string.Empty, service.ClickedColumn);
        Assert.Equal([MID_COLAUTOWIDTH_ALL], IdsOf(service.EmitVisibleItems(service.GetCount())));
    }

    /// <summary>
    /// Clearing the auto-width toggle suppresses the block AT ADD TIME, separator included
    /// [<c>:L266</c>].
    /// </summary>
    [Fact]
    public void ClearingColAutoWidthSuppressesTheBlockAtAddTime()
    {
        (_, ContextMenuModel service) = NewService(OptionsWith(colAutoWidth: false));

        _ = service.OnPrepare(0L, Dwo(HeaderObject, "text"), Pointer("header\t1"));

        Assert.DoesNotContain(MID_COLAUTOWIDTH, IdsOf(service.StoreItems));
        Assert.DoesNotContain(MID_COLAUTOWIDTH_ALL, IdsOf(service.StoreItems));
    }

    /// <summary>
    /// A non-grid presentation style suppresses the auto-width block [<c>:L266</c>].
    /// </summary>
    [Fact]
    public void ANonGridStyleSuppressesTheAutoWidthBlock()
    {
        (FakeDataWindowHost host, ContextMenuModel service) = NewService();

        // "0" is STYLE_FREEFORM [n_cst_dwsvc.sru:L144].
        host.Processing = "0";

        _ = service.OnPrepare(0L, Dwo(HeaderObject, "text"), Pointer("header\t1"));

        Assert.DoesNotContain(MID_COLAUTOWIDTH, IdsOf(service.StoreItems));
        Assert.DoesNotContain(MID_COLAUTOWIDTH_ALL, IdsOf(service.StoreItems));
    }

    /// <summary>
    /// The check block's four-way guard, satisfied - three items with EMPTY images [<c>:L283-L287</c>].
    /// </summary>
    [Fact]
    public void TheCheckBlockAddsThreeItemsWithEmptyImages()
    {
        (FakeDataWindowHost host, ContextMenuModel service) = ArrangeCheckBoxColumn();

        _ = service.OnPrepare(0L, Dwo(CheckColumn + "_t", "text"), Pointer("header\t1"));

        Dictionary<uint, MenuItemData> byId = service.StoreItems
            .Where(item => item.Id != 0u)
            .ToDictionary(item => item.Id);

        Assert.Equal("勾选列", byId[MID_COLCHECK].Text);
        Assert.Equal("勾选整列", byId[MID_COLCHECK].TipText);
        Assert.Equal(string.Empty, byId[MID_COLCHECK].Image);

        Assert.Equal("清除勾选列", byId[MID_COLUNCHECK].Text);
        Assert.Equal("清除勾选整列", byId[MID_COLUNCHECK].TipText);
        Assert.Equal(string.Empty, byId[MID_COLUNCHECK].Image);

        Assert.Equal("反向勾选列", byId[MID_COLREVERTCHECK].Text);
        Assert.Equal("反向勾选整列", byId[MID_COLREVERTCHECK].TipText);
        Assert.Equal(string.Empty, byId[MID_COLREVERTCHECK].Image);

        _ = Assert.IsType<FakeDataWindowHost>(host);
    }

    /// <summary>
    /// Each of the four guard conditions suppresses the check block on its own [<c>:L283</c>].
    /// </summary>
    /// <param name="breakToggle">Clear the check toggle.</param>
    /// <param name="breakEditable">Make the column non-editable.</param>
    /// <param name="breakRows">Empty the buffer.</param>
    /// <param name="breakStyle">Make the edit style something other than a check box.</param>
    [Theory]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, true, false)]
    [InlineData(false, false, false, true)]
    public void EachCheckGuardSuppressesTheBlockAlone(
        bool breakToggle,
        bool breakEditable,
        bool breakRows,
        bool breakStyle)
    {
        (FakeDataWindowHost host, ContextMenuModel service) = ArrangeCheckBoxColumn(
            addRow: !breakRows,
            colCheck: !breakToggle);

        if (breakEditable)
        {
            host.ReadOnly = "yes";
        }

        if (breakStyle)
        {
            host.SetDescribe(CheckColumn + ".edit.style", "edit");
        }

        _ = service.OnPrepare(0L, Dwo(CheckColumn + "_t", "text"), Pointer("header\t1"));

        Assert.DoesNotContain(MID_COLCHECK, IdsOf(service.StoreItems));
        Assert.DoesNotContain(MID_COLUNCHECK, IdsOf(service.StoreItems));
        Assert.DoesNotContain(MID_COLREVERTCHECK, IdsOf(service.StoreItems));
    }

    /// <summary>
    /// The SECOND separator is emitted UNCONDITIONALLY, even when the check block added nothing
    /// [<c>:L289</c>].
    /// </summary>
    /// <remarks>
    /// FAILS IF THE SEPARATOR IS MADE CONDITIONAL. Two adjacent separators are exactly what the collapse
    /// pass exists to absorb, which is why the unconditional add is harmless in the oracle and why a
    /// "tidy" that guards it would change the store while leaving the emitted menu identical - the store
    /// is what characterization recordings carry.
    /// </remarks>
    [Fact]
    public void TheSecondSeparatorIsUnconditional()
    {
        (_, ContextMenuModel service) = NewService(OptionsWith(colCheck: false));

        _ = service.OnPrepare(0L, Dwo(HeaderObject, "text"), Pointer("header\t1"));

        // THREE separators in the STORE - :L267 opens the auto-width block, :L282 opens the
        // clicked-column block and :L289 is the unconditional one - with the check items absent between
        // the last two. Counted from the store rather than the menu because the store is what a
        // characterization recording carries.
        Assert.Equal(
            3,
            service.StoreItems.Count(item => string.Equals(item.Text, Separator, StringComparison.Ordinal)));

        // And ONE in the emitted menu, because the collapse absorbs the pair.
        Assert.Equal(
            1,
            service.EmitVisibleItems(service.GetCount())
                .Count(item => string.Equals(item.Text, Separator, StringComparison.Ordinal)));
    }

    /// <summary>
    /// The column-copy item carries the copy image [<c>:L292</c>].
    /// </summary>
    [Fact]
    public void TheColumnCopyItemCarriesTheCopyImage()
    {
        (_, ContextMenuModel service) = NewService();

        _ = service.OnPrepare(0L, Dwo(HeaderObject, "text"), Pointer("header\t1"));

        MenuItemData item = service.StoreItems.Single(entry => entry.Id == MID_COLCOPY);
        Assert.Equal("复制列", item.Text);
        Assert.Equal("Copy!", item.Image);
        Assert.Equal("复制整列的数据", item.TipText);
    }

    /// <summary>
    /// The paste item appears ONLY when the supplied clipboard text is non-empty
    /// [<c>:L299-L306</c>].
    /// </summary>
    /// <param name="clipboard">The clipboard text the caller supplies.</param>
    /// <param name="expected">Whether the paste item is offered.</param>
    [Theory]
    [InlineData("", false)]
    [InlineData("42", true)]
    public void ThePasteItemFollowsTheSuppliedClipboardText(string clipboard, bool expected)
    {
        (_, ContextMenuModel service) = NewService();

        _ = service.OnPrepare(0L, Dwo(HeaderObject, "text"), Pointer("header\t1", clipboard: clipboard));

        Assert.Equal(expected, IdsOf(service.StoreItems).Contains(MID_COLPASTE));
        Assert.Equal(clipboard, service.CapturedClipboardText);

        if (expected)
        {
            MenuItemData item = service.StoreItems.Single(entry => entry.Id == MID_COLPASTE);
            Assert.Equal("粘贴列", item.Text);
            Assert.Equal("Paste!", item.Image);
            Assert.Equal("拷贝粘帖板数据覆盖到整列", item.TipText);
        }
    }

    /// <summary>
    /// A non-editable column offers no paste item even with clipboard text [<c>:L298</c>].
    /// </summary>
    [Fact]
    public void ANonEditableColumnOffersNoPasteItem()
    {
        (FakeDataWindowHost host, ContextMenuModel service) = NewService();
        host.ReadOnly = "yes";

        _ = service.OnPrepare(0L, Dwo(HeaderObject, "text"), Pointer("header\t1", clipboard: "42"));

        Assert.DoesNotContain(MID_COLPASTE, IdsOf(service.StoreItems));
    }

    /// <summary>
    /// Builds a host whose check-box column satisfies the four-way guard.
    /// </summary>
    /// <param name="addRow">Whether the buffer has a row, which is the third guard.</param>
    /// <param name="colCheck">The check toggle, which is the first guard.</param>
    /// <returns>The host and the attached service.</returns>
    private static (FakeDataWindowHost Host, ContextMenuModel Service) ArrangeCheckBoxColumn(
        bool addRow = true,
        bool colCheck = true)
    {
        (FakeDataWindowHost host, ContextMenuModel service) = NewService(OptionsWith(colCheck: colCheck));

        FakeDataWindowObjectDefinition checkColumn = host.AddColumn(CheckColumn, "char(1)");
        checkColumn.TabSequence = "60";

        _ = host.AddTextObject(CheckColumn + "_t", "header");

        host.SetDescribe(CheckColumn + ".Type", "column");
        host.SetDescribe(CheckColumn + ".Band", "detail");
        host.SetDescribe(CheckColumn + ".edit.style", "checkbox");
        host.SetDescribe(CheckColumn + ".checkbox.on", "Y");
        host.SetDescribe(CheckColumn + ".checkbox.off", "N");

        // THE PER-ROW VISIBILITY AND PROTECTION PROBES ARE PROPERTY READS, and every column operation
        // skips a row whose cell is invisible or protected [:L784-L785]. The fake answers "!" for an
        // undeclared property, which reads as NOT visible - so both are taught here, otherwise every
        // operation walks the buffer and writes nothing.
        host.SetDescribe(CheckColumn + ".visible", "1");
        host.SetDescribe(CheckColumn + ".protect", "0");

        if (addRow)
        {
            _ = host.AddRow(1.00m, "N");
        }

        return (host, service);
    }

    // ==============================================================================================
    //  THE FIVE EVENTS - the press/release gate, the two vetoes, the dispatch and the cleanup
    // ==============================================================================================

    /// <summary>
    /// The press handler records the row and answers <c>0</c> [<c>:L132-L134</c>].
    /// </summary>
    [Fact]
    public void ThePressHandlerRecordsTheRow()
    {
        (_, ContextMenuModel service) = NewService();

        Assert.Equal(0L, service.OnRButtonDown(7L));
        Assert.Equal(7L, service.PressedRow);
    }

    /// <summary>
    /// The release handler builds a menu ONLY when the row matches the press, and clears the press row
    /// either way [<c>:L124-L130</c>].
    /// </summary>
    /// <param name="pressedRow">The row the press recorded.</param>
    /// <param name="releasedRow">The row the release reports.</param>
    /// <param name="expectMenu">Whether a layout is produced.</param>
    [Theory]
    [InlineData(3L, 3L, true)]
    [InlineData(3L, 4L, false)]
    [InlineData(0L, 3L, false)]
    public void TheReleaseHandlerGatesOnTheRowMatch(long pressedRow, long releasedRow, bool expectMenu)
    {
        (_, ContextMenuModel service) = NewService();
        _ = service.OnRButtonDown(pressedRow);

        Assert.Equal(0L, service.OnRButtonUp(releasedRow, Dwo(HeaderObject, "text"), Pointer("header\t1")));

        Assert.Equal(expectMenu, service.LastLayout is not null);

        // :L127 - cleared UNCONDITIONALLY, so a release that did not match still ends the gesture.
        Assert.Equal(0L, service.PressedRow);
    }

    /// <summary>
    /// A row of zero always allows the popup [<c>:L312</c>].
    /// </summary>
    [Fact]
    public void ARowOfZeroAllowsThePopup()
    {
        (_, ContextMenuModel service) = NewService();

        ContextMenuLayout layout = service.BuildMenu(0L, Dwo(HeaderObject, "text"), Pointer("header\t1"));

        Assert.Equal(ContextMenuOutcome.Menu, layout.Outcome);
    }

    /// <summary>
    /// An editable edit-style cell under the caret REFUSES the popup [<c>:L335</c>].
    /// </summary>
    /// <remarks>
    /// THE REFUSAL IS NARROW BY DESIGN: it needs the row to be current, the caret to be in this very
    /// column, the DataWindow and the column to be editable, and the edit style to be one of the four the
    /// switch names - so that a text cursor's own context menu is left to the DataWindow.
    /// </remarks>
    [Fact]
    public void AnEditableCellUnderTheCaretRefusesThePopup()
    {
        (FakeDataWindowHost host, ContextMenuModel service) = NewService();
        _ = host.AddRow(1.00m);
        host.CurrentRow = 1L;
        host.CurrentColumnName = Column;

        ContextMenuLayout layout = service.BuildMenu(1L, Dwo(Column, "column"), Pointer());

        Assert.Equal(ContextMenuOutcome.NotAllowed, layout.Outcome);
        Assert.Empty(layout.Items);
    }

    /// <summary>
    /// The same cell allows the popup once the caret is elsewhere [<c>:L322</c>].
    /// </summary>
    [Fact]
    public void TheSameCellAllowsThePopupWhenTheCaretIsElsewhere()
    {
        (FakeDataWindowHost host, ContextMenuModel service) = NewService();
        _ = host.AddRow(1.00m);
        host.CurrentRow = 1L;
        host.CurrentColumnName = "other";

        ContextMenuLayout layout = service.BuildMenu(1L, Dwo(Column, "column"), Pointer());

        Assert.NotEqual(ContextMenuOutcome.NotAllowed, layout.Outcome);
    }

    /// <summary>
    /// Both vetoes stop the build, and the cleanup still runs [<c>:L147</c>, <c>:L197-L201</c>].
    /// </summary>
    /// <remarks>
    /// THE CLEANUP ON A VETO IS THE POINT. The oracle's <c>finally</c> clears the clicked column, the
    /// clipboard text AND the whole store on every early exit, so a vetoed right-click leaves nothing
    /// behind for the next one to accumulate onto (DECISION 6).
    /// </remarks>
    [Fact]
    public void AHostVetoStopsTheBuildAndStillCleansUp()
    {
        (FakeDataWindowHost host, ContextMenuModel service) = NewService();
        host.InitContextMenuHandler = (_, _) => 1L;

        ContextMenuLayout layout = service.BuildMenu(0L, Dwo(HeaderObject, "text"), Pointer("header\t1"));

        Assert.Equal(ContextMenuOutcome.HostPrevented, layout.Outcome);
        Assert.Empty(layout.Items);
        Assert.Empty(service.StoreItems);
        Assert.Equal(string.Empty, service.ClickedColumn);
        Assert.Equal(string.Empty, service.CapturedClipboardText);
    }

    /// <summary>
    /// A prepared store of zero items answers the empty outcome [<c>:L149-L150</c>].
    /// </summary>
    /// <remarks>
    /// THE COUNT IS TAKEN BEFORE THE FILTER, so this outcome means "preparation added nothing" and not
    /// "the filter removed everything" - a store the filter empties still answers
    /// <see cref="ContextMenuOutcome.Menu"/> with no items.
    /// </remarks>
    [Fact]
    public void APreparationThatAddsNothingAnswersEmpty()
    {
        (_, ContextMenuModel service) = NewService(OptionsWith(colAutoWidth: false));

        ContextMenuLayout layout = service.BuildMenu(0L, Dwo("anything", "text"), Pointer("detail\t1"));

        Assert.Equal(ContextMenuOutcome.Empty, layout.Outcome);
    }

    /// <summary>
    /// A successful build HANDS OFF: the store, the clicked column and the clipboard text all survive
    /// until the selection is applied (DECISION 6).
    /// </summary>
    [Fact]
    public void ASuccessfulBuildHandsOffTheStore()
    {
        (_, ContextMenuModel service) = NewService();

        ContextMenuLayout layout = service.BuildMenu(
            0L,
            Dwo(HeaderObject, "text"),
            Pointer("header\t1", clipboard: "42"));

        Assert.Equal(ContextMenuOutcome.Menu, layout.Outcome);
        Assert.True(layout.AwaitingSelection);
        Assert.NotEmpty(service.StoreItems);
        Assert.Equal(Column, service.ClickedColumn);
        Assert.Equal("42", service.CapturedClipboardText);
    }

    /// <summary>
    /// Applying a selection of zero cleans up and dispatches nothing [<c>:L193</c>].
    /// </summary>
    [Fact]
    public void AZeroSelectionCleansUpAndDispatchesNothing()
    {
        (FakeDataWindowHost host, ContextMenuModel service) = NewService();
        _ = service.BuildMenu(0L, Dwo(HeaderObject, "text"), Pointer("header\t1"));
        host.CallLog.Clear();

        Assert.Equal(RetCode.OK, service.ApplySelection(0L, Dwo(HeaderObject, "text"), 0L));

        Assert.Empty(service.StoreItems);
        Assert.Equal(string.Empty, service.ClickedColumn);
        Assert.DoesNotContain("Event OnContextMenu", host.CallLog.Members);
    }

    /// <summary>
    /// The host's second veto stops the dispatch, and the cleanup still runs [<c>:L194</c>].
    /// </summary>
    [Fact]
    public void TheSecondVetoStopsTheDispatch()
    {
        (FakeDataWindowHost host, ContextMenuModel service) = NewService();
        host.ContextMenuHandler = (_, _, _) => 1L;
        _ = service.BuildMenu(0L, Dwo(HeaderObject, "text"), Pointer("header\t1"));

        Assert.Equal(RetCode.OK, service.ApplySelection(0L, Dwo(HeaderObject, "text"), MID_COLCOPY));

        Assert.Empty(service.StoreItems);
        Assert.Null(service.PendingCopiedText);
    }

    /// <summary>
    /// The selection is applied in ORDER: the host's veto first, then the default dispatch
    /// [<c>:L194-L196</c>].
    /// </summary>
    /// <remarks>
    /// AAP 0.6.1.4 assigns this area pattern (b) - strictly synchronous, no reordering - precisely
    /// because initialization must complete before the identifier passed to the second event can be
    /// meaningful.
    /// </remarks>
    [Fact]
    public void TheSelectionRaisesTheHostEventBeforeDispatching()
    {
        (FakeDataWindowHost host, ContextMenuModel service) = NewService();
        _ = host.AddRow(1.00m);
        _ = service.BuildMenu(0L, Dwo(HeaderObject, "text"), Pointer("header\t1"));
        host.CallLog.Clear();

        _ = service.ApplySelection(0L, Dwo(HeaderObject, "text"), MID_COLCOPY);

        List<string> members = [.. host.CallLog.Members];
        int contextMenu = members.IndexOf("Event OnContextMenu");
        int acceptText = members.IndexOf("AcceptText");

        Assert.True(contextMenu >= 0, "the host's veto event must be raised");
        Assert.True(acceptText > contextMenu, "the dispatch must follow the veto event");
    }

    /// <summary>
    /// Discarding an un-selected menu performs the cleanup the oracle's <c>finally</c> would have
    /// (DECISION 6).
    /// </summary>
    [Fact]
    public void DiscardCleansUpAnUnselectedMenu()
    {
        (_, ContextMenuModel service) = NewService();
        _ = service.BuildMenu(0L, Dwo(HeaderObject, "text"), Pointer("header\t1", clipboard: "42"));

        service.Discard();

        Assert.Empty(service.StoreItems);
        Assert.Equal(string.Empty, service.ClickedColumn);
        Assert.Equal(string.Empty, service.CapturedClipboardText);
    }

    /// <summary>
    /// The store is REBUILT on every right-click rather than accumulated (DECISION 6, <c>:L197-L201</c>).
    /// </summary>
    /// <remarks>
    /// FAILS IF THE STORE IS MODELLED AS PERSISTENT STATE. Three consecutive right-clicks must produce
    /// three identical menus; a port that skipped the cleanup would produce menus of growing length.
    /// </remarks>
    [Fact]
    public void TheStoreIsRebuiltOnEveryRightClick()
    {
        (_, ContextMenuModel service) = NewService();

        List<uint> first = IdsOf(
            service.BuildMenu(0L, Dwo(HeaderObject, "text"), Pointer("header\t1")).Items);

        service.Discard();

        List<uint> second = IdsOf(
            service.BuildMenu(0L, Dwo(HeaderObject, "text"), Pointer("header\t1")).Items);

        service.Discard();

        List<uint> third = IdsOf(
            service.BuildMenu(0L, Dwo(HeaderObject, "text"), Pointer("header\t1")).Items);

        Assert.Equal(first, second);
        Assert.Equal(first, third);
    }

    /// <summary>
    /// The dispatch has EIGHT arms, and each is asserted through its OWN observable effect
    /// [<c>:L204-L222</c>].
    /// </summary>
    /// <remarks>
    /// <para>
    /// ONE ARRANGEMENT, EIGHT EFFECTS, IN THE ORACLE'S ARM ORDER. Asserting an effect rather than a
    /// recorded call is what makes the test discriminate: three of the eight arms are distinguishable
    /// only by what they WRITE, since all three read the same properties.
    /// </para>
    /// <para>
    /// THE CLEANUP DOES NOT RUN BETWEEN THE CALLS, because it belongs to the selection operation and not
    /// to the dispatch [<c>:L197-L201</c>] - so the clicked column and the clipboard text survive all
    /// eight, which is what lets one arrangement serve them.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheDispatchReachesTheEightArms()
    {
        (FakeDataWindowHost host, ContextMenuModel service) = ArrangeCheckBoxColumn();
        _ = service.OnPrepare(0L, Dwo(CheckColumn + "_t", "text"), Pointer("header\t1", clipboard: "Y"));

        Assert.Equal(CheckColumn, service.ClickedColumn);
        Assert.Equal("Y", service.CapturedClipboardText);

        // :L206  MID_COLAUTOWIDTH reaches the single-column arity, which REFUSES a check-box column
        // [:L1272-L1275] - see ACheckBoxColumnIsOfferedAnAutoWidthItThenRefuses for why that is the
        // oracle's behaviour and not a broken arrangement.
        Assert.Equal(RetCode.FAILED, service.OnDefProc(1L, Dwo(CheckColumn, "column"), (long)MID_COLAUTOWIDTH));
        ColumnAutoWidthPlan single = Assert.IsType<ColumnAutoWidthPlan>(service.PendingWidthPlan);
        Assert.Empty(single.Columns);

        // :L208  MID_COLAUTOWIDTH_ALL plans every eligible column - and the check-box column is NOT
        // eligible for the all-columns arity [:L1107-L1110], which is why it is absent from the plan.
        Assert.Equal(RetCode.OK, service.OnDefProc(1L, Dwo(CheckColumn, "column"), (long)MID_COLAUTOWIDTH_ALL));
        ColumnAutoWidthPlan all = Assert.IsType<ColumnAutoWidthPlan>(service.PendingWidthPlan);
        Assert.DoesNotContain(CheckColumn, all.Columns.Select(column => column.ColName));

        // :L210  MID_COLCHECK writes the ON value.
        Assert.Equal(RetCode.OK, service.OnDefProc(1L, Dwo(CheckColumn, "column"), (long)MID_COLCHECK));
        Assert.Equal("Y", host.GetItemString(1L, CheckColumn));

        // :L212  MID_COLUNCHECK writes the OFF value.
        Assert.Equal(RetCode.OK, service.OnDefProc(1L, Dwo(CheckColumn, "column"), (long)MID_COLUNCHECK));
        Assert.Equal("N", host.GetItemString(1L, CheckColumn));

        // :L214  MID_COLREVERTCHECK flips it back.
        Assert.Equal(RetCode.OK, service.OnDefProc(1L, Dwo(CheckColumn, "column"), (long)MID_COLREVERTCHECK));
        Assert.Equal("Y", host.GetItemString(1L, CheckColumn));

        // :L218  MID_COLCOPY produces text and writes no clipboard (DECISION 3).
        Assert.Equal(RetCode.OK, service.OnDefProc(1L, Dwo(CheckColumn, "column"), (long)MID_COLCOPY));
        Assert.Equal("Y" + RowSeparator, service.PendingCopiedText);

        // :L216  MID_COLPASTE applies the captured clipboard text.
        _ = host.SetItem(1L, CheckColumn, "N");
        Assert.Equal(RetCode.OK, service.OnDefProc(1L, Dwo(CheckColumn, "column"), (long)MID_COLPASTE));
        Assert.Equal("Y", host.GetItemString(1L, CheckColumn));

        // :L220  MID_ITEMCOPY produces one cell's text, taking the column from the OBJECT rather than
        // from the clicked column.
        Assert.Equal(RetCode.OK, service.OnDefProc(1L, Dwo(CheckColumn, "column"), (long)MID_ITEMCOPY));
        Assert.Equal("Y", service.PendingCopiedText);
    }

    /// <summary>
    /// A check-box column IS OFFERED an auto-width item and the operation then REFUSES it
    /// [<c>:L266-L277</c> versus <c>:L1272-L1275</c>].
    /// </summary>
    /// <remarks>
    /// AN ORACLE INCONSISTENCY, PRESERVED. Preparation gates the auto-width item on the presentation
    /// style and the clicked column alone - it never asks about the edit style - while the single-column
    /// auto-width arity refuses a check box outright. So the menu offers a command that cannot succeed,
    /// and selecting it answers <see cref="RetCode.FAILED"/> with an empty plan. Neither half is
    /// "corrected" (constraint C-B).
    /// </remarks>
    [Fact]
    public void ACheckBoxColumnIsOfferedAnAutoWidthItThenRefuses()
    {
        (_, ContextMenuModel service) = ArrangeCheckBoxColumn();

        _ = service.OnPrepare(0L, Dwo(CheckColumn + "_t", "text"), Pointer("header\t1"));

        Assert.Contains(MID_COLAUTOWIDTH, IdsOf(service.StoreItems));

        ColumnAutoWidthPlan plan = service.ColumnAutoWidth(CheckColumn);

        Assert.Equal(RetCode.FAILED, plan.Code);
        Assert.Empty(plan.Columns);
    }

    /// <summary>
    /// DEFECT 1's observable consequence - <c>MID_RESERVED</c> lands on the AUTO-WIDTH arm
    /// [<c>:L206</c>].
    /// </summary>
    /// <remarks>
    /// FAILS IF THE PAIR IS DE-DUPLICATED. Because both identifiers are 10000 and the dispatch names the
    /// auto-width one, the "reserved start id" is unreachable as a distinct case - selecting it plans an
    /// auto-width instead of doing nothing.
    /// </remarks>
    [Fact]
    public void MidReservedDispatchesToTheAutoWidthArm()
    {
        (_, ContextMenuModel service) = NewService();

        // Preparation is what sets the clicked column the single-column arm needs.
        _ = service.OnPrepare(0L, Dwo(HeaderObject, "text"), Pointer("header\t1"));

        Assert.Equal(RetCode.OK, service.OnDefProc(0L, Dwo(Column, "column"), (long)MID_RESERVED));

        ColumnAutoWidthPlan plan = Assert.IsType<ColumnAutoWidthPlan>(service.PendingWidthPlan);
        Assert.Equal(Column, Assert.Single(plan.Columns).ColName);
    }

    /// <summary>
    /// An unknown identifier does nothing, because the oracle's <c>choose case</c> has no
    /// <c>case else</c> [<c>:L221</c>].
    /// </summary>
    [Fact]
    public void AnUnknownIdentifierDoesNothing()
    {
        (FakeDataWindowHost host, ContextMenuModel service) = NewService();
        host.CallLog.Clear();

        Assert.Equal(0L, service.OnDefProc(0L, Dwo(Column, "column"), 999L));

        Assert.Empty(host.CallLog.Members);
        Assert.Null(service.PendingWidthPlan);
        Assert.Null(service.PendingCopiedText);
        Assert.Null(service.PendingError);
    }

    /// <summary>
    /// The single-column auto-width arm uses the CLICKED column, and the all-columns arm does not
    /// [<c>:L206</c>, <c>:L208</c>].
    /// </summary>
    [Fact]
    public void TheTwoAutoWidthArmsDifferInScope()
    {
        (_, ContextMenuModel service) = NewService();
        _ = service.OnPrepare(0L, Dwo(HeaderObject, "text"), Pointer("header\t1"));

        _ = service.OnDefProc(0L, Dwo(Column, "column"), (long)MID_COLAUTOWIDTH);
        ColumnAutoWidthPlan single = Assert.IsType<ColumnAutoWidthPlan>(service.PendingWidthPlan);
        Assert.Equal(Column, Assert.Single(single.Columns).ColName);

        _ = service.OnDefProc(0L, Dwo(Column, "column"), (long)MID_COLAUTOWIDTH_ALL);
        ColumnAutoWidthPlan all = Assert.IsType<ColumnAutoWidthPlan>(service.PendingWidthPlan);
        Assert.Equal(Column, Assert.Single(all.Columns).ColName);
    }

    // ==============================================================================================
    //  THE TEN DIALOGS, CONVERTED - three shapes, four keys, and DEFECT 7
    // ==============================================================================================

    /// <summary>
    /// The four change-refused sites compose the same message and differ only in their locator
    /// [<c>:L795</c>, <c>:L863</c>, <c>:L916</c>, <c>:L1052</c>].
    /// </summary>
    /// <remarks>
    /// EVERY FIELD IS ASSERTED, NOT JUST THE TEXT: the two message KEYS travel verbatim as the oracle's
    /// own source strings, the category is <c>CAT_DWSVC</c>, the row travels as DATA and not only
    /// pre-rendered, the severity is the legacy <c>StopSign!</c>, and <c>Localized</c> is set - which is
    /// what distinguishes these ten from the column-expression engine's twenty-eight hardcoded messages
    /// (AAP 0.2.1.3 Correction 5).
    /// </remarks>
    [Fact]
    public void TheChangeRefusedErrorCarriesEveryField()
    {
        (FakeDataWindowHost host, ContextMenuModel service) = ArrangeCheckBoxColumn();
        host.DoItemChangeHandler = (_, _, _) => 1L;

        Assert.Equal(RetCode.FAILED, service.CheckColumn(CheckColumn));

        ContextMenuError error = Assert.IsType<ContextMenuError>(service.PendingError);
        Assert.Equal("第1行\n修改数据被拒绝!", error.Text);
        Assert.Equal(Categories.CAT_DWSVC, error.LocalizationCategory);
        Assert.Equal("第{}行", error.RowFormatTemplate);
        Assert.Equal("修改数据被拒绝", error.DetailMessageKey);
        Assert.Equal(1L, error.Row);
        Assert.Null(error.OffendingValue);
        Assert.Equal(ContextMenuErrorKind.ChangeRejected, error.Kind);
        Assert.Equal(ContextMenuMessageIcon.StopSign, error.Icon);
        Assert.True(error.Localized);
        Assert.Equal("n_cst_dwsvc_contextmenu.sru:L795", error.SourceLocator);
    }

    /// <summary>
    /// The three check-box operations each surface the refusal from their own locator
    /// [<c>:L795</c>, <c>:L916</c>, <c>:L863</c>].
    /// </summary>
    [Fact]
    public void EachCheckBoxOperationCarriesItsOwnLocator()
    {
        (FakeDataWindowHost host, ContextMenuModel service) = ArrangeCheckBoxColumn();
        host.DoItemChangeHandler = (_, _, _) => 1L;

        // THE ROW MUST GENUINELY NEED THE CHANGE, or the operation skips it and answers OK [:L786-L793].
        // The seeded value is the OFF value, so the check operation has work to do.
        Assert.Equal(RetCode.FAILED, service.CheckColumn(CheckColumn));
        Assert.Equal("n_cst_dwsvc_contextmenu.sru:L795", service.PendingError?.SourceLocator);

        _ = host.SetItem(1L, CheckColumn, "Y");
        Assert.Equal(RetCode.FAILED, service.UncheckColumn(CheckColumn));
        Assert.Equal("n_cst_dwsvc_contextmenu.sru:L916", service.PendingError?.SourceLocator);

        Assert.Equal(RetCode.FAILED, service.RevertCheckColumn(CheckColumn));
        Assert.Equal("n_cst_dwsvc_contextmenu.sru:L863", service.PendingError?.SourceLocator);
    }

    /// <summary>
    /// The paste path's refusal is the fourth change-refused site [<c>:L1052</c>].
    /// </summary>
    [Fact]
    public void ThePasteRefusalIsTheFourthSite()
    {
        (FakeDataWindowHost host, ContextMenuModel service) = ArrangeCheckBoxColumn();
        host.DoItemChangeHandler = (_, _, _) => 1L;

        Assert.Equal(RetCode.FAILED, service.Paste2Column(CheckColumn, "Y"));

        ContextMenuError error = Assert.IsType<ContextMenuError>(service.PendingError);
        Assert.Equal(ContextMenuErrorKind.ChangeRejected, error.Kind);
        Assert.Equal("n_cst_dwsvc_contextmenu.sru:L1052", error.SourceLocator);
    }

    /// <summary>
    /// The invalid-value message appends the offending value and carries it as data [<c>:L1009</c>].
    /// </summary>
    /// <remarks>
    /// THE SHAPE DIFFERS FROM THE REFUSAL'S by a second line separator and the value itself - the oracle
    /// writes <c>"!~n" + sVal</c> as one literal followed by the value.
    /// </remarks>
    [Fact]
    public void TheInvalidValueErrorAppendsTheOffendingValue()
    {
        (_, ContextMenuModel service) = ArrangeEnumeratedColumn();

        Assert.Equal(RetCode.E_INVALID_ARGUMENT, service.Paste2Column("grade", "Z"));

        ContextMenuError error = Assert.IsType<ContextMenuError>(service.PendingError);
        Assert.Equal("第1行\n无效的值!\nZ", error.Text);
        Assert.Equal("无效的值", error.DetailMessageKey);
        Assert.Equal("Z", error.OffendingValue);
        Assert.Equal(ContextMenuErrorKind.InvalidValue, error.Kind);
        Assert.Equal(ContextMenuMessageIcon.StopSign, error.Icon);
        Assert.Equal("n_cst_dwsvc_contextmenu.sru:L1009", error.SourceLocator);
    }

    /// <summary>
    /// A value the enumerated column DOES know is translated through rather than rejected
    /// [<c>:L1006-L1007</c>].
    /// </summary>
    /// <remarks>
    /// THE MAP IS A DISPLAY-TO-DATA TRANSLATION, so pasting the DISPLAY text writes the DATA value - and
    /// the map is <c>OrderedMap</c> rather than a dictionary because its insertion order is observable
    /// elsewhere in the layer (AAP 0.2.1.3 Correction 2).
    /// </remarks>
    [Fact]
    public void AKnownDisplayValueIsTranslatedToItsDataValue()
    {
        (FakeDataWindowHost host, ContextMenuModel service) = ArrangeEnumeratedColumn();

        Assert.Equal(RetCode.OK, service.Paste2Column("grade", "优"));

        Assert.Equal("A", host.GetItemString(1L, "grade"));
        Assert.Null(service.PendingError);
    }

    /// <summary>
    /// The five type-mismatch sites, one per typed paste arm
    /// [<c>:L1018</c>, <c>:L1027</c>, <c>:L1033</c>, <c>:L1039</c>, <c>:L1045</c>].
    /// </summary>
    /// <param name="colType">The column's raw type, which selects the arm.</param>
    /// <param name="pasted">Text that cannot convert to it.</param>
    /// <param name="locator">The oracle site that reports it.</param>
    [Theory]
    [InlineData("long", "abc", "n_cst_dwsvc_contextmenu.sru:L1018")]
    [InlineData("decimal(2)", "abc", "n_cst_dwsvc_contextmenu.sru:L1027")]
    [InlineData("datetime", "abc", "n_cst_dwsvc_contextmenu.sru:L1033")]
    [InlineData("date", "abc", "n_cst_dwsvc_contextmenu.sru:L1039")]
    [InlineData("time", "abc", "n_cst_dwsvc_contextmenu.sru:L1045")]
    public void TheFiveTypeMismatchSites(string colType, string pasted, string locator)
    {
        (_, ContextMenuModel service) = ArrangeTypedColumn(colType);

        Assert.Equal(RetCode.E_INVALID_ARGUMENT, service.Paste2Column("typed", pasted));

        ContextMenuError error = Assert.IsType<ContextMenuError>(service.PendingError);
        Assert.Equal("第1行\n数据类型不匹配!\n" + pasted, error.Text);
        Assert.Equal("数据类型不匹配", error.DetailMessageKey);
        Assert.Equal(pasted, error.OffendingValue);
        Assert.Equal(ContextMenuErrorKind.TypeMismatch, error.Kind);
        Assert.Equal(locator, error.SourceLocator);
    }

    /// <summary>
    /// DEFECT 11 - the datetime arm rejects the instant <c>1900-01-01 00:00:00</c> as a type mismatch
    /// [<c>:L1032</c>].
    /// </summary>
    /// <remarks>
    /// FAILS IF THE TEST IS "CORRECTED" to a conversion-failure test. The oracle compares the conversion
    /// against an UNINITIALISED <c>datetime</c> local [<c>:L945</c>], whose PowerScript initial value is
    /// that very instant - which is also what a failed conversion answers. So a perfectly convertible
    /// value is reported as unconvertible.
    /// </remarks>
    [Fact]
    public void PastingTheUninitialisedDatetimeReportsATypeMismatch()
    {
        (_, ContextMenuModel service) = ArrangeTypedColumn("datetime");

        Assert.Equal(RetCode.E_INVALID_ARGUMENT, service.Paste2Column("typed", "1900-01-01 00:00:00"));

        ContextMenuError error = Assert.IsType<ContextMenuError>(service.PendingError);
        Assert.Equal(ContextMenuErrorKind.TypeMismatch, error.Kind);
        Assert.Equal("1900-01-01 00:00:00", error.OffendingValue);
    }

    /// <summary>
    /// A convertible datetime OUTSIDE that instant is accepted [<c>:L1036</c>, <c>:L1067</c>].
    /// </summary>
    [Fact]
    public void AConvertibleDatetimeIsAccepted()
    {
        (FakeDataWindowHost host, ContextMenuModel service) = ArrangeTypedColumn("datetime");

        Assert.Equal(RetCode.OK, service.Paste2Column("typed", "2024-03-04 05:06:07"));

        Assert.Equal(new DateTime(2024, 3, 4, 5, 6, 7), host.GetItemDateTime(1L, "typed"));
    }

    // ==============================================================================================
    //  THE PASTE - and DEFECTS 10 AND 7
    // ==============================================================================================

    /// <summary>
    /// Both empty arguments are refused [<c>:L950</c>].
    /// </summary>
    /// <param name="colName">The column.</param>
    /// <param name="data">The text.</param>
    [Theory]
    [InlineData("", "42")]
    [InlineData("typed", "")]
    public void ThePasteRefusesEmptyArguments(string colName, string data)
    {
        (_, ContextMenuModel service) = ArrangeTypedColumn("long");

        Assert.Equal(RetCode.E_INVALID_ARGUMENT, service.Paste2Column(colName, data));
    }

    /// <summary>
    /// An unknown column type fails [<c>:L957</c>].
    /// </summary>
    [Fact]
    public void AnUnknownColumnTypeFailsThePaste()
    {
        (FakeDataWindowHost host, ContextMenuModel service) = ArrangeTypedColumn("long");
        host.SetDescribe("typed.ColType", "widget");

        Assert.Equal(RetCode.FAILED, service.Paste2Column("typed", "42"));
    }

    /// <summary>
    /// DEFECT 10 - pasting more lines than there are rows INSERTS rows [<c>:L994-L1001</c>].
    /// </summary>
    /// <remarks>
    /// FAILS IF THE LOOP IS BOUNDED BY THE ROW COUNT. The oracle's loop runs to the PASTED LINE COUNT and
    /// grows the buffer to reach each line, which makes this the only column operation in the file that
    /// can change the row count.
    /// </remarks>
    [Fact]
    public void PastingMoreLinesThanRowsInsertsRows()
    {
        (FakeDataWindowHost host, ContextMenuModel service) = ArrangeTypedColumn("long");

        Assert.Equal(1L, host.RowCount());
        Assert.Equal(RetCode.OK, service.Paste2Column("typed", "1" + RowSeparator + "2" + RowSeparator + "3"));

        Assert.Equal(3L, host.RowCount());
        Assert.Equal(1d, host.GetItemNumber(1L, "typed"));
        Assert.Equal(2d, host.GetItemNumber(2L, "typed"));
        Assert.Equal(3d, host.GetItemNumber(3L, "typed"));
    }

    /// <summary>
    /// A refused insert fails the whole paste [<c>:L999</c>].
    /// </summary>
    [Fact]
    public void ARefusedInsertFailsThePaste()
    {
        (FakeDataWindowHost host, ContextMenuModel service) = ArrangeTypedColumn("long");
        host.InsertRowHandler = _ => 0L;

        Assert.Equal(RetCode.FAILED, service.Paste2Column("typed", "1" + RowSeparator + "2"));
    }

    /// <summary>
    /// The split KEEPS interior empty lines, so a blank cell lands on the row it came from
    /// [<c>:L952</c>].
    /// </summary>
    /// <remarks>
    /// FAILS IF <c>ignoreEmpty</c> IS FLIPPED. Dropping the empty line would shift row 3's value up onto
    /// row 2 and write the wrong cells for the whole remainder of the block.
    /// </remarks>
    [Fact]
    public void ThePasteKeepsInteriorEmptyLines()
    {
        (FakeDataWindowHost host, ContextMenuModel service) = ArrangeTypedColumn("char(10)");
        _ = host.AddRow(0m, "b");
        _ = host.AddRow(0m, "c");

        Assert.Equal(RetCode.OK, service.Paste2Column("typed", "x" + RowSeparator + RowSeparator + "z"));

        Assert.Equal("x", host.GetItemString(1L, "typed"));
        Assert.Equal(string.Empty, host.GetItemString(2L, "typed"));
        Assert.Equal("z", host.GetItemString(3L, "typed"));
        Assert.Equal(3L, host.RowCount());
    }

    /// <summary>
    /// A trailing separator produces NO surplus line, so a copy-then-paste round trip does not grow the
    /// buffer [<c>n_cst_dwsvc.sru:L473-L475</c>].
    /// </summary>
    [Fact]
    public void ATrailingSeparatorProducesNoSurplusLine()
    {
        (FakeDataWindowHost host, ContextMenuModel service) = ArrangeTypedColumn("char(10)");

        Assert.Equal(RetCode.OK, service.Paste2Column("typed", "x" + RowSeparator));

        Assert.Equal(1L, host.RowCount());
        Assert.Equal("x", host.GetItemString(1L, "typed"));
    }

    /// <summary>
    /// A trailing percent sign is a division by 100, applied to the value that is then written
    /// [<c>:L1023-L1025</c>].
    /// </summary>
    [Fact]
    public void ATrailingPercentDividesByOneHundred()
    {
        (FakeDataWindowHost host, ContextMenuModel service) = ArrangeTypedColumn("decimal(2)");

        Assert.Equal(RetCode.OK, service.Paste2Column("typed", "50%"));

        Assert.Equal(0.5m, host.GetItemDecimal(1L, "typed"));
    }

    /// <summary>
    /// An empty cell in a <c>NilIsNull</c> column writes a REAL NULL, and the item-change event sees that
    /// null [<c>:L1050-L1051</c>].
    /// </summary>
    /// <remarks>
    /// FAILS IF THE NULL IS COLLAPSED TO AN EMPTY STRING (AAP 0.4.5.4). The event's parameter is nullable
    /// for this path alone - see Domain/DataWindowServiceHost.cs's note on <c>OnDoItemChange</c>.
    /// </remarks>
    [Fact]
    public void AnEmptyCellInANilIsNullColumnWritesANull()
    {
        (FakeDataWindowHost host, ContextMenuModel service) = ArrangeTypedColumn("char(10)");
        host.SetDescribe("typed.edit.NilIsNull", "yes");
        _ = host.SetItem(1L, "typed", "was");

        List<string?> seen = [];
        host.DoItemChangeHandler = (_, _, data) =>
        {
            seen.Add(data);
            return 0L;
        };

        Assert.Equal(RetCode.OK, service.Paste2Column("typed", " " + RowSeparator));

        // One line only, and it is a space rather than an empty string, so the null path is reached by
        // the SECOND arrangement below rather than by this one.
        Assert.Equal([" "], seen);

        host.SetDescribe("typed.edit.NilIsNull", "yes");
        seen.Clear();
        Assert.Equal(RetCode.OK, service.Paste2Column("typed", "a" + RowSeparator + RowSeparator + "c"));

        Assert.Equal(["a", null, "c"], seen);
        Assert.Null(host.GetItemString(2L, "typed"));
    }

    /// <summary>
    /// An empty cell in a column that does NOT declare <c>NilIsNull</c> writes the empty string
    /// [<c>:L1050</c>].
    /// </summary>
    [Fact]
    public void AnEmptyCellWithoutNilIsNullWritesTheEmptyString()
    {
        (FakeDataWindowHost host, ContextMenuModel service) = ArrangeTypedColumn("char(10)");
        _ = host.AddRow(0m, "b");

        Assert.Equal(RetCode.OK, service.Paste2Column("typed", "a" + RowSeparator + RowSeparator + "c"));

        Assert.Equal(string.Empty, host.GetItemString(2L, "typed"));
    }

    /// <summary>
    /// A row already holding the pasted value is skipped without raising anything
    /// [<c>:L1015</c>].
    /// </summary>
    [Fact]
    public void ARowAlreadyHoldingTheValueIsSkipped()
    {
        (FakeDataWindowHost host, ContextMenuModel service) = ArrangeTypedColumn("char(10)");
        _ = host.SetItem(1L, "typed", "same");

        int raised = 0;
        host.DoItemChangeHandler = (_, _, _) =>
        {
            raised++;
            return 0L;
        };

        Assert.Equal(RetCode.OK, service.Paste2Column("typed", "same"));

        Assert.Equal(0, raised);
    }

    /// <summary>
    /// A NULL item and a NULL pasted value are NOT equal, so the row is written
    /// [PowerScript's three-valued comparison].
    /// </summary>
    /// <remarks>
    /// FAILS IF C# <c>==</c> SEMANTICS ARE USED. Two nulls compare equal in C# and would skip the row;
    /// in PowerScript <c>null = null</c> is null and an <c>if</c> does not take a null branch, so the
    /// oracle falls through and writes - raising the item-change event and marking the row modified.
    /// </remarks>
    [Fact]
    public void TwoNullsAreNotEqualAndTheRowIsWritten()
    {
        (FakeDataWindowHost host, ContextMenuModel service) = ArrangeTypedColumn("char(10)");
        host.SetDescribe("typed.edit.NilIsNull", "yes");
        _ = host.SetItem(1L, "typed", (string?)null);

        int raised = 0;
        host.DoItemChangeHandler = (_, _, _) =>
        {
            raised++;
            return 0L;
        };

        // One empty line: the item is already null and the pasted value becomes null, so a C# equality
        // would skip the row.
        Assert.Equal(RetCode.OK, service.Paste2Column("typed", RowSeparator + "x"));

        Assert.Equal(2, raised);
    }

    /// <summary>
    /// DEFECT 7 - the un-check operation reads an <c>int</c> column through its STRING arm while its two
    /// siblings read it through their INTEGER arm [<c>:L900</c> versus <c>:L779</c>].
    /// </summary>
    /// <remarks>
    /// FAILS IF THE TWO MECHANISMS ARE UNIFIED. <c>_of_UncheckColumn</c> keys its arms on the first five
    /// characters of the raw <c>.ColType</c> and its lists name only <c>"decim"</c>/<c>"real"</c> and
    /// <c>"numbe"</c>/<c>"long"</c>/<c>"ulong"</c> - <c>"int"</c> appears in neither - while the shared
    /// classifier its two siblings use folds <c>int</c> onto the integer arm. The observable difference is
    /// the TEXT each one compares: an item holding <c>0</c> reads as <c>"0"</c> through both arms here, so
    /// the divergence is asserted through the WRITE, which stores a string rather than a number.
    /// </remarks>
    [Fact]
    public void UncheckColumnReadsAnIntColumnAsAString()
    {
        (FakeDataWindowHost host, ContextMenuModel service) = ArrangeTypedColumn("int");
        host.SetDescribe("typed.checkbox.on", "1");
        host.SetDescribe("typed.checkbox.off", "0");
        _ = host.SetItem(1L, "typed", "9");

        Assert.Equal(RetCode.OK, service.UncheckColumn("typed"));

        // The STRING arm wrote text, so the raw item is the string "0" and not the number 0.
        Assert.Equal("0", host.GetItemString(1L, "typed"));

        // And the INTEGER arm of the sibling writes a number for the same column.
        Assert.Equal(RetCode.OK, service.CheckColumn("typed"));
        Assert.Equal(1d, host.GetItemNumber(1L, "typed"));
    }

    // ==============================================================================================
    //  THE TWO COPY PRODUCERS - and DEFECT 8
    // ==============================================================================================

    /// <summary>
    /// DEFECT 8 - the column producer appends the row separator after EVERY row, the last included
    /// [<c>:L546</c>].
    /// </summary>
    /// <remarks>
    /// FAILS IF THE COMPOSITION IS CHANGED TO A JOIN. A one-row column copies as <c>"value\r\n"</c>, and
    /// characterization recordings carry the composed text byte for byte.
    /// </remarks>
    [Fact]
    public void CopyColumnToTextAlwaysEndsWithTheRowSeparator()
    {
        (FakeDataWindowHost host, ContextMenuModel service) = ArrangeTypedColumn("char(10)");
        _ = host.SetItem(1L, "typed", "one");

        ClipboardTextResult result = service.CopyColumnToText("typed");

        Assert.Equal(RetCode.OK, result.Code);
        Assert.Equal("one" + RowSeparator, result.Text);
        Assert.Equal(result.Text, service.PendingCopiedText);
    }

    /// <summary>
    /// The cell producer emits its value BARE, with no separator [<c>:L629</c>].
    /// </summary>
    [Fact]
    public void CopyItemToTextEmitsTheValueBare()
    {
        (FakeDataWindowHost host, ContextMenuModel service) = ArrangeTypedColumn("char(10)");
        _ = host.SetItem(1L, "typed", "one");

        ClipboardTextResult result = service.CopyItemToText(1L, "typed");

        Assert.Equal(RetCode.OK, result.Code);
        Assert.Equal("one", result.Text);
    }

    /// <summary>
    /// A check-box column copies as <c>Y</c> or <c>N</c>, and an unrecognised value copies VERBATIM
    /// [<c>:L535-L539</c>].
    /// </summary>
    /// <remarks>
    /// THE SUBSTITUTION IS NOT EXHAUSTIVE, which is the oracle's behaviour for a third state: a value
    /// matching neither the ON nor the OFF text is copied as itself rather than mapped or blanked.
    /// </remarks>
    [Fact]
    public void ACheckBoxColumnCopiesAsYOrNOrVerbatim()
    {
        (FakeDataWindowHost host, ContextMenuModel service) = ArrangeCheckBoxColumn();
        _ = host.AddRow(2.00m, "Y");
        _ = host.AddRow(3.00m, "?");

        ClipboardTextResult result = service.CopyColumnToText(CheckColumn);

        Assert.Equal(
            "N" + RowSeparator + "Y" + RowSeparator + "?" + RowSeparator,
            result.Text);
    }

    /// <summary>
    /// An invisible cell copies as the EMPTY STRING rather than being skipped, so row alignment survives
    /// [<c>:L530-L531</c>].
    /// </summary>
    [Fact]
    public void AnInvisibleCellCopiesAsTheEmptyString()
    {
        (FakeDataWindowHost host, ContextMenuModel service) = ArrangeTypedColumn("char(10)");
        _ = host.SetItem(1L, "typed", "one");
        host.SetDescribe("typed.visible", "0");

        ClipboardTextResult result = service.CopyColumnToText("typed");

        Assert.Equal(RowSeparator, result.Text);
    }

    /// <summary>
    /// Both producers refuse an empty column and a failed accept, and neither publishes text
    /// [<c>:L518-L519</c>, <c>:L605-L607</c>].
    /// </summary>
    [Fact]
    public void BothProducersRefuseTheirGuards()
    {
        (FakeDataWindowHost host, ContextMenuModel service) = ArrangeTypedColumn("char(10)");

        Assert.Equal(RetCode.E_INVALID_ARGUMENT, service.CopyColumnToText(string.Empty).Code);
        Assert.Equal(RetCode.E_INVALID_ARGUMENT, service.CopyItemToText(0L, "typed").Code);
        Assert.Equal(RetCode.E_INVALID_ARGUMENT, service.CopyItemToText(1L, string.Empty).Code);
        Assert.Null(service.PendingCopiedText);

        host.AcceptTextResult = -1;
        Assert.Equal(RetCode.FAILED, service.CopyColumnToText("typed").Code);
        Assert.Equal(RetCode.FAILED, service.CopyItemToText(1L, "typed").Code);
        Assert.Null(service.PendingCopiedText);
    }

    // ==============================================================================================
    //  THE AUTO-WIDTH PLAN - the headless half of the measurement, and the three Find predicates
    // ==============================================================================================

    /// <summary>
    /// The single-column arity refuses an empty name and a check-box column
    /// [<c>:L1270</c>, <c>:L1272-L1275</c>].
    /// </summary>
    [Fact]
    public void TheSingleColumnArityRefusesItsTwoGuards()
    {
        (_, ContextMenuModel service) = ArrangeCheckBoxColumn();

        Assert.Equal(RetCode.E_INVALID_ARGUMENT, service.ColumnAutoWidth(string.Empty).Code);
        Assert.Equal(RetCode.FAILED, service.ColumnAutoWidth(CheckColumn).Code);
    }

    /// <summary>
    /// The plan carries the column's geometry, the arrow allowance and the padding as DATA, and performs
    /// no measurement (DECISION 8).
    /// </summary>
    /// <remarks>
    /// EVERY VALUE IS THE RAW <c>Describe</c> ANSWER, UNCONVERTED. A later "helpful" DPI conversion or
    /// unit scaling here would fail this test, which is the point: the conversion at <c>:L1407-L1413</c>
    /// belongs to the deferred half under <c>/v1/design/**</c>.
    /// </remarks>
    [Fact]
    public void ThePlanCarriesTheRawGeometry()
    {
        (FakeDataWindowHost host, ContextMenuModel service) = ArrangeTypedColumn("char(10)");
        host.SetDescribe("typed.X", "137");
        host.SetDescribe("typed.Width", "400");
        host.SetDescribe("DataWindow.Units", "2");

        ColumnWidthComputation computation = Assert.Single(service.ColumnAutoWidth("typed").Columns);

        Assert.Equal("typed", computation.ColName);
        Assert.False(computation.IsCompute);
        Assert.False(computation.AutoHeight);
        Assert.Equal(137L, computation.ColumnXPosition);
        Assert.Equal(400L, computation.ColumnWidth);
        Assert.Equal(0d, computation.ArrowButtonWidth);
        Assert.Equal(4d, computation.Padding);
        Assert.Equal("2", service.PendingWidthPlan?.Units);
    }

    /// <summary>
    /// A drop-down column earns the arrow allowance, and only through the FIRST matching arm
    /// [<c>:L1394-L1403</c>].
    /// </summary>
    /// <param name="editStyle">The edit style.</param>
    /// <param name="expected">The allowance.</param>
    [Theory]
    [InlineData("dddw", 18d)]
    [InlineData("ddlb", 18d)]
    [InlineData("edit", 0d)]
    [InlineData("editmask", 0d)]
    public void TheArrowAllowanceFollowsTheEditStyle(string editStyle, double expected)
    {
        (FakeDataWindowHost host, ContextMenuModel service) = ArrangeTypedColumn("char(10)");
        host.SetDescribe("typed.Edit.Style", editStyle);

        ColumnWidthComputation computation = Assert.Single(service.ColumnAutoWidth("typed").Columns);

        Assert.Equal(expected, computation.ArrowButtonWidth);
    }

    /// <summary>
    /// The three <c>Find</c> predicates are BYTE-EXACT, and the <c>{1}</c> counts are two, four and four
    /// [<c>:L1183</c>, <c>:L1186</c>, <c>:L1188</c>].
    /// </summary>
    /// <remarks>
    /// <para>
    /// THESE ARE DATAWINDOW EXPRESSION TEXT, NOT C# FORMAT STRINGS. The doubled quotes inside the compute
    /// predicate are the oracle's <c>~"</c> escapes and become part of the expression the DataWindow
    /// parses, where the inner <c>String(GetRow() + 1)</c> and the <c>+</c> operators are evaluated BY THE
    /// DATAWINDOW. A port that pre-computed the row number would select different rows and therefore
    /// compute a different width.
    /// </para>
    /// <para>
    /// THE COUNTS WERE MEASURED against the oracle and the third one CORRECTS this file's brief, which
    /// states six.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheThreeFindPredicatesAreByteExact()
    {
        (FakeDataWindowHost host, ContextMenuModel service) = ArrangeTypedColumn("char(10)");
        List<string> predicates = [];
        host.FindHandler = (expression, _, _) =>
        {
            predicates.Add(expression);
            return 0L;
        };

        // A STRING column takes the null-normalising predicate [:L1186] - {1} four times.
        _ = service.ColumnAutoWidth("typed");
        Assert.Equal(
            "if(IsNull(typed),'',typed) <> if(IsNull(typed[1]),'',typed[1])",
            Assert.Single(predicates));

        // A NON-STRING column takes the value-or-nullness predicate [:L1188] - {1} four times.
        predicates.Clear();
        host.SetDescribe("typed.ColType", "long");
        _ = service.ColumnAutoWidth("typed");
        Assert.Equal(
            "(typed <> typed[1]) or (if(IsNull(typed),1,0) <> if(IsNull(typed[1]),1,0))",
            Assert.Single(predicates));

        // A COMPUTE takes the nested-Describe predicate [:L1183] - {1} twice.
        predicates.Clear();
        host.SetDescribe("typed.Type", "compute");
        _ = service.ColumnAutoWidth("typed");
        Assert.Equal(
            "String(typed) <> if(GetRow() < RowCount(),Describe(\"Evaluate('typed',\" + String(GetRow() + 1) + \")\"),'')",
            Assert.Single(predicates));
    }

    /// <summary>
    /// An empty buffer plans no rows and no predicate [<c>:L1347</c>].
    /// </summary>
    [Fact]
    public void AnEmptyBufferPlansNoRows()
    {
        (FakeDataWindowHost host, ContextMenuModel service) = ArrangeTypedColumn("char(10)");
        _ = host.RemoveRow(1L);

        ColumnWidthComputation computation = Assert.Single(service.ColumnAutoWidth("typed").Columns);

        Assert.Empty(computation.RowCandidates);
        Assert.Equal(string.Empty, computation.DistinctValueFindExpression);
    }

    /// <summary>
    /// The row walk follows <c>Find</c> and stops on the LAST ROW rather than on a failed search
    /// [<c>:L1387-L1388</c>].
    /// </summary>
    [Fact]
    public void TheRowWalkFollowsFindAndStopsOnTheLastRow()
    {
        (FakeDataWindowHost host, ContextMenuModel service) = ArrangeTypedColumn("char(10)");
        _ = host.SetItem(1L, "typed", "one");
        _ = host.AddRow(0m, "two");
        _ = host.AddRow(0m, "three");

        // A NON-COMPUTE ROW READS ITS DISPLAY VALUE THROUGH Describe("Evaluate('LookUpDisplay(col)',n)")
        // [:L1371], so the fake is taught those three properties - it carries no expression engine of its
        // own and would otherwise answer the invalid-property sentinel.
        TeachLookUpDisplay(host, "typed", "one", "two", "three");

        List<long> starts = [];
        host.FindHandler = (_, start, _) =>
        {
            starts.Add(start);
            return start > 3L ? 0L : start;
        };

        ColumnWidthComputation computation = Assert.Single(service.ColumnAutoWidth("typed").Columns);

        // Rows 1, 2 and 3 are walked, and the search is NOT restarted after the last row.
        Assert.Equal([1L, 2L, 3L], starts);
        Assert.Equal(["one", "two", "three"], computation.RowCandidates.Select(candidate => candidate.Text));
    }

    /// <summary>
    /// A row candidate carries the COLUMN's font and the single-line measurement mode
    /// [<c>:L1342-L1344</c>, <c>:L1381</c>].
    /// </summary>
    [Fact]
    public void ARowCandidateCarriesTheColumnFont()
    {
        (FakeDataWindowHost host, ContextMenuModel service) = ArrangeTypedColumn("char(10)");
        _ = host.SetItem(1L, "typed", "one");
        TeachLookUpDisplay(host, "typed", "one");
        host.SetDescribe("typed.Font.Face", "Segoe UI");
        host.SetDescribe("typed.Font.Height", "-12");
        host.SetDescribe("typed.Font.Weight", "700");
        host.FindHandler = (_, start, _) => start == 1L ? 1L : 0L;

        TextMeasurementCandidate candidate = Assert.Single(
            Assert.Single(service.ColumnAutoWidth("typed").Columns).RowCandidates);

        Assert.Equal("Segoe UI", candidate.FontFace);
        Assert.Equal(-12L, candidate.FontHeight);
        Assert.True(candidate.Bold);
        Assert.Equal(TextMeasurementMode.SingleLine, candidate.MeasurementMode);
        Assert.Equal(0, candidate.WrapWidth);
        Assert.Equal(string.Empty, candidate.FormatMask);
        Assert.False(candidate.IsSynthesizedProxy);
    }

    /// <summary>
    /// An auto-height column measures as an EDIT CONTROL with the 1024 wrap width
    /// [<c>:L1374-L1376</c>].
    /// </summary>
    [Fact]
    public void AnAutoHeightColumnMeasuresAsAnEditControl()
    {
        (FakeDataWindowHost host, ContextMenuModel service) = ArrangeTypedColumn("char(10)");
        _ = host.SetItem(1L, "typed", "one");
        TeachLookUpDisplay(host, "typed", "one");
        host.SetDescribe("typed.Height.AutoSize", "yes");
        host.FindHandler = (_, start, _) => start == 1L ? 1L : 0L;

        ColumnWidthComputation computation = Assert.Single(service.ColumnAutoWidth("typed").Columns);

        Assert.True(computation.AutoHeight);

        TextMeasurementCandidate candidate = Assert.Single(computation.RowCandidates);
        Assert.Equal(TextMeasurementMode.EditControl, candidate.MeasurementMode);
        Assert.Equal(1024, candidate.WrapWidth);
    }

    /// <summary>
    /// The ASCII/wide split synthesizes a worst-case PROXY string [<c>:L1298-L1301</c>].
    /// </summary>
    /// <param name="values">The column's values, semicolon-separated.</param>
    /// <param name="expectedAscii">The ASCII count.</param>
    /// <param name="expectedWide">The wide count.</param>
    /// <param name="expectedText">The proxy text.</param>
    /// <remarks>
    /// <para>
    /// THE PROXY IS NOT ANY REAL VALUE. <c>Len</c> counts characters and <c>LenA</c> counts DBCS bytes,
    /// so their difference is the wide-character count, and the oracle builds a string of that SHAPE out
    /// of <c>"A"</c> and <c>"国"</c> to measure instead of measuring a value.
    /// </para>
    /// <para>
    /// THE TWO AGGREGATES ARE COMPUTED BY THE REAL EXPRESSION ENGINE over seeded rows, not stubbed, so
    /// the test exercises the same path the service uses in production - Expressions/
    /// DataWindowExpressionEvaluator.cs implements <c>Max</c>, <c>Len</c>, <c>LenA</c> and
    /// <c>LookUpDisplay</c> natively.
    /// </para>
    /// <para>
    /// THE LAST CASE IS WHY THE <c>Abs</c> EXISTS: the widest-by-characters and the widest-by-bytes values
    /// are DIFFERENT rows, so the proxy is the shape of no single value in the column - it is a synthetic
    /// worst case, which is the intent. A purely wide column drives the ASCII count to zero, and
    /// <c>Fill</c> of a non-positive count is the empty string.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("abc;ab", 3, 0, "AAA")]
    [InlineData("国国", 0, 2, "国国")]
    [InlineData("a国", 1, 1, "A国")]
    [InlineData("abc;国国国", 0, 3, "国国国")]
    public void TheProxyStringIsSynthesizedFromTheTwoAggregates(
        string values,
        int expectedAscii,
        int expectedWide,
        string expectedText)
    {
        (FakeDataWindowHost host, ContextMenuModel service) = ArrangeSiblingColumn();

        foreach (string value in values.Split(';'))
        {
            _ = host.AddRow(0m, "hc", value);
        }

        TextMeasurementCandidate candidate = Assert.Single(
            Assert.Single(service.ColumnAutoWidth("host_col").Columns).HeadingCandidates);

        Assert.True(candidate.IsSynthesizedProxy);
        Assert.Equal(expectedAscii, candidate.AsciiCount);
        Assert.Equal(expectedWide, candidate.WideCount);
        Assert.Equal(expectedText, candidate.Text);
    }

    /// <summary>
    /// An unevaluable aggregate drops the candidate entirely rather than measuring zero
    /// [<c>:L1141</c>].
    /// </summary>
    /// <remarks>
    /// POWERSCRIPT'S NULL ARITHMETIC IS THE MECHANISM: <c>Long("!")</c> is null, null arithmetic is null,
    /// <c>Fill</c> of a null count is null, and the <c>sText &lt;&gt; ""</c> guard does not fire on a
    /// null - so the candidate never reaches the measurer.
    /// </remarks>
    [Fact]
    public void AnUnevaluableAggregateDropsTheCandidate()
    {
        (FakeDataWindowHost host, ContextMenuModel service) = ArrangeSiblingColumn();

        // No rows, so the two aggregates have nothing to aggregate and answer a sentinel rather than a
        // number - which is the ordinary way this arises.
        Assert.Equal(0L, host.RowCount());

        Assert.Empty(Assert.Single(service.ColumnAutoWidth("host_col").Columns).HeadingCandidates);
    }

    /// <summary>
    /// A sibling in an EXACT <c>header</c> band wraps; one in a numbered group header does NOT
    /// [<c>:L1317</c>].
    /// </summary>
    /// <param name="band">The sibling's band.</param>
    /// <param name="expectedMode">The measurement mode.</param>
    /// <param name="expectedWrap">The wrap width.</param>
    /// <remarks>
    /// ADMITTED BY A PREFIX, MEASURED BY AN EQUALITY. The band guard accepts <c>header</c> and
    /// <c>header.1</c> alike [<c>:L1287</c>] while the mode test compares for exact equality - so a group
    /// header is measured single-line. Easy to "tidy" into one rule, and it must not be.
    /// </remarks>
    [Theory]
    [InlineData("header", TextMeasurementMode.WordBreak, 1024)]
    [InlineData("header.1", TextMeasurementMode.SingleLine, 0)]
    [InlineData("footer", TextMeasurementMode.SingleLine, 0)]
    [InlineData("summary", TextMeasurementMode.SingleLine, 0)]
    [InlineData("trailer.1", TextMeasurementMode.SingleLine, 0)]
    public void OnlyTheExactHeaderBandWraps(
        string band,
        TextMeasurementMode expectedMode,
        int expectedWrap)
    {
        (FakeDataWindowHost host, ContextMenuModel service) = ArrangeSiblingText(band);

        TextMeasurementCandidate candidate = Assert.Single(
            Assert.Single(service.ColumnAutoWidth("host_col").Columns).HeadingCandidates);

        Assert.Equal(expectedMode, candidate.MeasurementMode);
        Assert.Equal(expectedWrap, candidate.WrapWidth);
    }

    /// <summary>
    /// A sibling outside the four admitted bands is skipped [<c>:L1287</c>].
    /// </summary>
    [Fact]
    public void ASiblingOutsideTheFourBandsIsSkipped()
    {
        (_, ContextMenuModel service) = ArrangeSiblingText("detail");

        Assert.Empty(Assert.Single(service.ColumnAutoWidth("host_col").Columns).HeadingCandidates);
    }

    /// <summary>
    /// The sibling overlap test allows 8 units of slack on the LEFT and none on the right
    /// [<c>:L1290</c>].
    /// </summary>
    /// <param name="siblingX">The sibling's x position.</param>
    /// <param name="expected">Whether it contributes a candidate.</param>
    /// <remarks>
    /// THE ASYMMETRY IS THE ORACLE'S and the <c>8</c> is a bare literal with no explanation anywhere in
    /// it. Only the sibling's LEFT edge is tested, so a wide object starting just right of the column's
    /// right edge is excluded however far left it extends.
    /// </remarks>
    [Theory]
    [InlineData("91", false)]
    [InlineData("92", true)]
    [InlineData("100", true)]
    [InlineData("500", true)]
    [InlineData("501", false)]
    public void TheSiblingOverlapTestIsAsymmetric(string siblingX, bool expected)
    {
        (FakeDataWindowHost host, ContextMenuModel service) = ArrangeSiblingText("footer");

        // The column sits at 100 and is 400 wide, so the admitted span is [92, 500].
        host.SetDescribe("host_col.X", "100");
        host.SetDescribe("host_col.Width", "400");
        host.SetDescribe("sibling.X", siblingX);

        List<TextMeasurementCandidate> candidates =
            [.. Assert.Single(service.ColumnAutoWidth("host_col").Columns).HeadingCandidates];

        Assert.Equal(expected, candidates.Count == 1);
    }

    /// <summary>
    /// An invisible sibling is skipped, and the test is <c>= "0"</c> rather than <c>&lt;&gt; "1"</c>
    /// [<c>:L1288</c>].
    /// </summary>
    [Fact]
    public void AnInvisibleSiblingIsSkipped()
    {
        (FakeDataWindowHost host, ContextMenuModel service) = ArrangeSiblingText("footer");
        host.SetDescribe("sibling.Visible", "0");

        Assert.Empty(Assert.Single(service.ColumnAutoWidth("host_col").Columns).HeadingCandidates);
    }

    /// <summary>
    /// A sibling's format mask is RESOLVED and carried, never applied (DECISION 8), and
    /// <c>[general]</c> normalises to none [<c>:L1309</c>].
    /// </summary>
    /// <param name="described">The raw <c>Format</c> answer.</param>
    /// <param name="expectedMask">The mask carried on the candidate.</param>
    [Theory]
    [InlineData("#,##0.00", "#,##0.00")]
    [InlineData("[general]", "")]
    [InlineData("[GENERAL]", "")]
    [InlineData("!", "")]
    [InlineData("?", "")]
    public void ASiblingFormatMaskIsResolvedAndCarried(string described, string expectedMask)
    {
        (FakeDataWindowHost host, ContextMenuModel service) = ArrangeSiblingText("footer");
        host.SetDescribe("sibling.Format", described);

        TextMeasurementCandidate candidate = Assert.Single(
            Assert.Single(service.ColumnAutoWidth("host_col").Columns).HeadingCandidates);

        Assert.Equal(expectedMask, candidate.FormatMask);

        // The text is UNCHANGED by the mask - applying it belongs to the deferred measurer.
        Assert.Equal("heading", candidate.Text);
    }

    /// <summary>
    /// The all-columns arity applies its four column guards [<c>:L1103-L1110</c>].
    /// </summary>
    /// <param name="property">The property to break.</param>
    /// <param name="value">The value that breaks it.</param>
    [Theory]
    [InlineData("typed.Band", "header")]
    [InlineData("typed.Visible", "0")]
    [InlineData("typed.Type", "text")]
    [InlineData("typed.edit.style", "checkbox")]
    [InlineData("typed.edit.style", "radiobuttons")]
    public void TheAllColumnsArityAppliesItsFourGuards(string property, string value)
    {
        (FakeDataWindowHost host, ContextMenuModel service) = ArrangeTypedColumn("char(10)");
        host.SetDescribe(property, value);

        Assert.DoesNotContain(
            "typed",
            service.ColumnAutoWidth().Columns.Select(column => column.ColName));
    }

    /// <summary>
    /// The all-columns arity always answers <see cref="RetCode.OK"/>, because every rejection is a
    /// per-column skip [<c>:L1257</c>].
    /// </summary>
    [Fact]
    public void TheAllColumnsArityAlwaysSucceeds()
    {
        (FakeDataWindowHost host, ContextMenuModel service) = ArrangeTypedColumn("char(10)");
        host.SetDescribe("typed.Visible", "0");

        ColumnAutoWidthPlan plan = service.ColumnAutoWidth();

        Assert.Equal(RetCode.OK, plan.Code);
        Assert.DoesNotContain("typed", plan.Columns.Select(column => column.ColName));
    }

    // ==============================================================================================
    //  ARRANGEMENTS for the paste, the producers and the width plan
    // ==============================================================================================

    /// <summary>
    /// Teaches the fake the <c>Evaluate('LookUpDisplay(col)',n)</c> properties one per row, which is how
    /// a non-compute row candidate reads its display value [<c>:L1371</c>].
    /// </summary>
    /// <param name="host">The host to teach.</param>
    /// <param name="colName">The column.</param>
    /// <param name="displays">The display values, row 1 onward.</param>
    /// <remarks>
    /// THE PROPERTY TEXT IS COMPOSED EXACTLY AS THE BASE HELPER COMPOSES IT, so a change to either side
    /// shows up as a sentinel rather than as a silently different value.
    /// </remarks>
    private static void TeachLookUpDisplay(
        FakeDataWindowHost host,
        string colName,
        params string[] displays)
    {
        for (int index = 0; index < displays.Length; index++)
        {
            host.SetDescribe(
                "Evaluate('LookUpDisplay("
                    + colName
                    + ")',"
                    + (index + 1).ToString(CultureInfo.InvariantCulture)
                    + ")",
                displays[index]);
        }
    }

    /// <summary>
    /// A host carrying one editable, visible column of the requested type with a single row.
    /// </summary>
    /// <param name="colType">The column's raw <c>ColType</c>.</param>
    /// <returns>The host and the attached service.</returns>
    private static (FakeDataWindowHost Host, ContextMenuModel Service) ArrangeTypedColumn(string colType)
    {
        (FakeDataWindowHost host, ContextMenuModel service) = NewService();

        FakeDataWindowObjectDefinition typed = host.AddColumn("typed", colType);
        typed.TabSequence = "70";

        host.SetDescribe("typed.Band", "detail");
        host.SetDescribe("typed.Type", "column");
        host.SetDescribe("typed.edit.style", "edit");
        host.SetDescribe("typed.visible", "1");
        host.SetDescribe("typed.protect", "0");
        host.SetDescribe("typed.Visible", "1");

        _ = host.AddRow(0m, null);

        return (host, service);
    }

    /// <summary>
    /// A host carrying one radio-button column with a code table, which makes it BOTH translating and
    /// enumerated [<c>:L979-L983</c>].
    /// </summary>
    /// <returns>The host and the attached service.</returns>
    private static (FakeDataWindowHost Host, ContextMenuModel Service) ArrangeEnumeratedColumn()
    {
        (FakeDataWindowHost host, ContextMenuModel service) = NewService();

        FakeDataWindowObjectDefinition grade = host.AddColumn("grade", "char(1)");
        grade.TabSequence = "80";
        _ = grade.AddCodeTableEntry("优", "A");
        _ = grade.AddCodeTableEntry("良", "B");

        host.SetDescribe("grade.Band", "detail");
        host.SetDescribe("grade.Type", "column");
        host.SetDescribe("grade.edit.style", "radiobuttons");
        host.SetDescribe("grade.visible", "1");
        host.SetDescribe("grade.protect", "0");

        _ = host.AddRow(0m, null);

        return (host, service);
    }

    /// <summary>
    /// A host carrying one detail column plus one sibling COLUMN object over it, which is what drives the
    /// synthesized proxy [<c>:L1297-L1301</c>].
    /// </summary>
    /// <returns>The host and the attached service.</returns>
    private static (FakeDataWindowHost Host, ContextMenuModel Service) ArrangeSiblingColumn()
    {
        (FakeDataWindowHost host, ContextMenuModel service) = NewService();

        _ = host.AddColumn("host_col", "char(10)");
        _ = host.AddColumn("typed", "char(10)");

        host.SetDescribe("host_col.Band", "detail");
        host.SetDescribe("host_col.Type", "column");
        host.SetDescribe("host_col.edit.style", "edit");
        host.SetDescribe("host_col.X", "100");
        host.SetDescribe("host_col.Width", "400");

        // The sibling is a COLUMN in the footer band, so it takes the proxy arm.
        host.SetDescribe("typed.Band", "footer");
        host.SetDescribe("typed.Type", "column");
        host.SetDescribe("typed.X", "100");
        host.SetDescribe("typed.Visible", "1");

        return (host, service);
    }

    /// <summary>
    /// A host carrying one detail column plus one sibling TEXT object over it in the requested band.
    /// </summary>
    /// <param name="band">The sibling's band.</param>
    /// <returns>The host and the attached service.</returns>
    private static (FakeDataWindowHost Host, ContextMenuModel Service) ArrangeSiblingText(string band)
    {
        (FakeDataWindowHost host, ContextMenuModel service) = NewService();

        _ = host.AddColumn("host_col", "char(10)");
        FakeDataWindowObjectDefinition sibling = host.AddTextObject("sibling", band);
        _ = sibling.SetProperty("text", "heading");

        host.SetDescribe("host_col.Band", "detail");
        host.SetDescribe("host_col.Type", "column");
        host.SetDescribe("host_col.edit.style", "edit");
        host.SetDescribe("host_col.X", "100");
        host.SetDescribe("host_col.Width", "400");

        host.SetDescribe("sibling.Band", band);
        host.SetDescribe("sibling.Type", "text");
        host.SetDescribe("sibling.X", "100");
        host.SetDescribe("sibling.Visible", "1");
        host.SetDescribe("sibling.text", "heading");

        return (host, service);
    }

    // ==============================================================================================
    //  _of_IsAllowPopup - THE SUPPRESSION GATE, GUARD BY GUARD                          :L310-L340
    //  --------------------------------------------------------------------------------------------
    //  Its real job is the opposite of its name: it suppresses THIS framework menu over a cell that is
    //  being edited so the edit control's own native menu appears. Seven of its ten exits therefore
    //  answer "allow". Each test below drives exactly one guard, and every one of them has to get past
    //  the guards above it - which is why the arrangement sets the current row, the current column and a
    //  positive tab sequence rather than relying on defaults.
    // ==============================================================================================

    /// <summary>
    /// A right-click on a row that is not the current row is ALLOWED [<c>:L320</c>].
    /// </summary>
    [Fact]
    public void ARightClickAwayFromTheCurrentRowAllowsTheFrameworkMenu()
    {
        (FakeDataWindowHost host, ContextMenuModel service, FakeDataWindowObject dwo) =
            ArrangeCurrentCell("edit");

        _ = host.AddRow(2.00m, "N");
        _ = host.SetRow(1L);

        // Row 2 is not the current row, so the cell under the pointer cannot be the one being edited.
        ContextMenuLayout layout = service.BuildMenu(2L, dwo, Pointer());

        Assert.NotEqual(ContextMenuOutcome.NotAllowed, layout.Outcome);
    }

    /// <summary>
    /// A right-click on the current row of a READ-ONLY DataWindow is ALLOWED [<c>:L322</c>].
    /// </summary>
    [Fact]
    public void AReadOnlyDataWindowAllowsTheFrameworkMenu()
    {
        (FakeDataWindowHost host, ContextMenuModel service, FakeDataWindowObject dwo) =
            ArrangeCurrentCell("edit");

        // Nothing can be under edit when the whole DataWindow is read-only, so the framework menu wins.
        host.ReadOnly = "yes";

        ContextMenuLayout layout = service.BuildMenu(1L, dwo, Pointer());

        Assert.NotEqual(ContextMenuOutcome.NotAllowed, layout.Outcome);
    }

    /// <summary>
    /// Both tab-sequence sentinels ALLOW the framework menu [<c>:L324</c>].
    /// </summary>
    /// <param name="tabSequence">The sentinel.</param>
    /// <remarks>
    /// <c>"0"</c> means the column is skipped in the tab order and <c>"32766"</c> is PowerBuilder's own
    /// marker for a column the user cannot reach. Either way the cell is not being edited. Both literals
    /// are the oracle's and are asserted rather than folded into one "not editable" notion.
    /// </remarks>
    [Theory]
    [InlineData("0")]
    [InlineData("32766")]
    public void EitherTabSequenceSentinelAllowsTheFrameworkMenu(string tabSequence)
    {
        (FakeDataWindowHost host, ContextMenuModel service, FakeDataWindowObject dwo) =
            ArrangeCurrentCell("edit");

        host.SetDescribe(CurrentColumn + ".TabSequence", tabSequence);

        ContextMenuLayout layout = service.BuildMenu(1L, dwo, Pointer());

        Assert.NotEqual(ContextMenuOutcome.NotAllowed, layout.Outcome);
    }

    /// <summary>
    /// A PROTECTED cell allows the framework menu [<c>:L325</c>].
    /// </summary>
    [Fact]
    public void AProtectedCellAllowsTheFrameworkMenu()
    {
        (FakeDataWindowHost host, ContextMenuModel service, FakeDataWindowObject dwo) =
            ArrangeCurrentCell("edit");

        host.SetDescribe(CurrentColumn + ".protect", "1");

        ContextMenuLayout layout = service.BuildMenu(1L, dwo, Pointer());

        Assert.NotEqual(ContextMenuOutcome.NotAllowed, layout.Outcome);
    }

    /// <summary>
    /// A plain edit and an edit mask both SUPPRESS the framework menu [<c>:L329-L330</c>].
    /// </summary>
    /// <param name="editStyle">The edit style.</param>
    [Theory]
    [InlineData("edit")]
    [InlineData("editmask")]
    public void AnEditControlSuppressesTheFrameworkMenu(string editStyle)
    {
        (_, ContextMenuModel service, FakeDataWindowObject dwo) = ArrangeCurrentCell(editStyle);

        ContextMenuLayout layout = service.BuildMenu(1L, dwo, Pointer());

        Assert.Equal(ContextMenuOutcome.NotAllowed, layout.Outcome);
        Assert.Empty(layout.Items);
    }

    /// <summary>
    /// A drop-down that ALLOWS typing suppresses the framework menu; one that does not, allows it
    /// [<c>:L331-L336</c>].
    /// </summary>
    /// <param name="editStyle">The drop-down edit style.</param>
    /// <param name="allowEdit">The <c>allowedit</c> answer.</param>
    /// <param name="suppressed">Whether the framework menu is suppressed.</param>
    /// <remarks>
    /// THE TEST IS AN INEQUALITY AGAINST <c>"yes"</c>, so any other answer - including the two
    /// unreadable sentinels - allows the framework menu. The sentinel rows below are what pin that: a
    /// column with no <c>allowedit</c> property at all reads back <c>"!"</c> and is therefore ALLOWED,
    /// which a <c>== "no"</c> port would get right by accident and a <c>== "no" or "!"</c> port would
    /// get right for the wrong reason.
    /// </remarks>
    [Theory]
    [InlineData("dddw", "yes", true)]
    [InlineData("dddw", "no", false)]
    [InlineData("dddw", "!", false)]
    [InlineData("ddlb", "yes", true)]
    [InlineData("ddlb", "no", false)]
    [InlineData("ddlb", "!", false)]
    public void ADropDownSuppressesTheFrameworkMenuOnlyWhenItAllowsTyping(
        string editStyle,
        string allowEdit,
        bool suppressed)
    {
        (FakeDataWindowHost host, ContextMenuModel service, FakeDataWindowObject dwo) =
            ArrangeCurrentCell(editStyle);

        host.SetDescribe(CurrentColumn + "." + editStyle + ".allowedit", allowEdit);

        ContextMenuLayout layout = service.BuildMenu(1L, dwo, Pointer());

        Assert.Equal(suppressed, layout.Outcome == ContextMenuOutcome.NotAllowed);
    }

    /// <summary>
    /// Every other edit style ALLOWS the framework menu [<c>:L339</c>].
    /// </summary>
    /// <param name="editStyle">The edit style.</param>
    /// <remarks>
    /// None of these puts a text editor under the pointer, so there is no native menu to defer to.
    /// </remarks>
    [Theory]
    [InlineData("checkbox")]
    [InlineData("radiobuttons")]
    [InlineData("")]
    public void AnEditStyleWithNoTextEditorAllowsTheFrameworkMenu(string editStyle)
    {
        (_, ContextMenuModel service, FakeDataWindowObject dwo) = ArrangeCurrentCell(editStyle);

        ContextMenuLayout layout = service.BuildMenu(1L, dwo, Pointer());

        Assert.NotEqual(ContextMenuOutcome.NotAllowed, layout.Outcome);
    }

    // ==============================================================================================
    //  event onenable - THE TWO SUBSCRIPTIONS                                        :L1440-L1447
    // ==============================================================================================

    /// <summary>
    /// Enabling subscribes the press and release topics AND NOTHING ELSE [<c>:L1441-L1442</c>].
    /// </summary>
    /// <remarks>
    /// THE THREE OTHER EVENTS ARE DELIBERATELY NOT SUBSCRIBED: the click event is raised by the release
    /// handler, the dispatch by the click and the preparation by the click, so subscribing any of them
    /// would run the whole menu cycle twice. The negative assertions are what stop a later "completeness"
    /// edit from adding them.
    /// </remarks>
    [Fact]
    public void EnablingSubscribesExactlyTheTwoRightButtonTopics()
    {
        (FakeDataWindowHost host, ContextMenuModel service) = NewService();

        Assert.Equal(RetCode.OK, service.SetEnabled(true));

        Assert.True(host.Eventful.IsSubscribed("rbuttondown"));
        Assert.True(host.Eventful.IsSubscribed("rbuttonup"));

        Assert.False(host.Eventful.IsSubscribed("initcontextmenu"));
        Assert.False(host.Eventful.IsSubscribed("contextmenu"));
        Assert.False(host.Eventful.IsSubscribed("lbuttonup"));
    }

    /// <summary>
    /// Disabling unsubscribes BY TARGET, so both topics go in one call [<c>:L1444</c>].
    /// </summary>
    [Fact]
    public void DisablingUnsubscribesEveryTopicThisServiceHolds()
    {
        (FakeDataWindowHost host, ContextMenuModel service) = NewService();
        Assert.Equal(RetCode.OK, service.SetEnabled(true));

        Assert.Equal(RetCode.OK, service.SetEnabled(false));

        Assert.False(host.Eventful.IsSubscribed("rbuttondown"));
        Assert.False(host.Eventful.IsSubscribed("rbuttonup"));
    }

    /// <summary>
    /// This service never vetoes its own enablement - the event answers <c>0</c> on both paths
    /// [<c>:L1446</c>].
    /// </summary>
    [Fact]
    public void NeitherEnablingNorDisablingIsEverVetoed()
    {
        (_, ContextMenuModel service) = NewService();

        Assert.Equal(RetCode.OK, service.SetEnabled(true));
        Assert.Equal(RetCode.OK, service.SetEnabled(false));
        Assert.Equal(RetCode.OK, service.SetEnabled(true));
    }

    // ==============================================================================================
    //  THE ARROW-BUTTON ALLOWANCE                                     :L1226-L1235 / :L1394-L1403
    //  --------------------------------------------------------------------------------------------
    //  A drop-down paints an arrow button inside the column, so the measured text width alone would
    //  clip it. The allowance is a HEADLESS number carried on the plan; applying it is the deferred
    //  half's job (DECISION 8).
    // ==============================================================================================

    /// <summary>
    /// A column that is not a drop-down gets NO allowance [<c>:L1227</c>].
    /// </summary>
    [Fact]
    public void APlainColumnCarriesNoArrowButtonAllowance()
    {
        (_, ContextMenuModel service) = ArrangeDropDownColumn("edit", editable: true);

        ColumnAutoWidthPlan plan = service.ColumnAutoWidth(DropDownColumn);

        Assert.Equal(RetCode.OK, plan.Code);
        Assert.Equal(0d, Assert.Single(plan.Columns).ArrowButtonWidth);
    }

    /// <summary>
    /// An EDITABLE drop-down always gets the allowance [<c>:L1228</c>].
    /// </summary>
    /// <param name="editStyle">The drop-down edit style.</param>
    [Theory]
    [InlineData("dddw")]
    [InlineData("ddlb")]
    public void AnEditableDropDownCarriesTheArrowButtonAllowance(string editStyle)
    {
        (_, ContextMenuModel service) = ArrangeDropDownColumn(editStyle, editable: true);

        ColumnAutoWidthPlan plan = service.ColumnAutoWidth(DropDownColumn);

        // ARROW_BTN_WIDTH is 18 and is PRIVATE [:L60], so the value is restated here rather than read
        // back off the service - which is the point: a change to the constant must fail this test.
        Assert.Equal(18d, Assert.Single(plan.Columns).ArrowButtonWidth);
    }

    /// <summary>
    /// A NON-editable drop-down gets the allowance only when it paints the arrow as its border
    /// [<c>:L1230</c>, <c>:L1232</c>].
    /// </summary>
    /// <param name="dddwUseAsBorder">The <c>DDDW.UseAsBorder</c> answer.</param>
    /// <param name="ddlbUseAsBorder">The <c>DDLB.UseAsBorder</c> answer.</param>
    /// <param name="expected">The allowance.</param>
    /// <remarks>
    /// BOTH PROPERTIES ARE READ REGARDLESS OF WHICH DROP-DOWN THIS IS, because the oracle asks the two
    /// questions in sequence without first deciding which family the column belongs to - a column
    /// declared <c>dddw</c> that somehow answers <c>DDLB.UseAsBorder</c> yes therefore gets the
    /// allowance. Reproduced, not tidied.
    /// </remarks>
    [Theory]
    [InlineData("yes", "no", 18d)]
    [InlineData("no", "yes", 18d)]
    [InlineData("no", "no", 0d)]
    [InlineData("!", "!", 0d)]
    public void ANonEditableDropDownCarriesTheAllowanceOnlyWhenTheArrowIsItsBorder(
        string dddwUseAsBorder,
        string ddlbUseAsBorder,
        double expected)
    {
        (FakeDataWindowHost host, ContextMenuModel service) =
            ArrangeDropDownColumn("dddw", editable: false);

        host.SetDescribe(DropDownColumn + ".DDDW.UseAsBorder", dddwUseAsBorder);
        host.SetDescribe(DropDownColumn + ".DDLB.UseAsBorder", ddlbUseAsBorder);

        ColumnAutoWidthPlan plan = service.ColumnAutoWidth(DropDownColumn);

        Assert.Equal(expected, Assert.Single(plan.Columns).ArrowButtonWidth);
    }

    // ==============================================================================================
    //  THE TWO TYPED ITEM MECHANISMS - DEFECT 7 SEEN FROM BOTH SIDES              :L786-L809 / :L907-L930
    // ==============================================================================================

    /// <summary>
    /// <see cref="ContextMenuModel.CheckColumn"/> reads and writes a DECIMAL column through the decimal
    /// arm [<c>:L788</c>, <c>:L804</c>].
    /// </summary>
    [Fact]
    public void CheckColumnReadsAndWritesADecimalColumnAsADecimal()
    {
        (FakeDataWindowHost host, ContextMenuModel service) = NewService();
        host.SetDescribe(Column + ".checkbox.on", "9.50");

        // ONE value, because NewService declares exactly one DATA column - the header text object is
        // not one. The fake rejects a longer row, which is what keeps a fixture honest about its shape.
        _ = host.AddRow(1.00m);

        Assert.Equal(RetCode.OK, service.CheckColumn(Column));

        // Written through Dec(sVal), so the buffer holds a decimal and not the text.
        Assert.Equal(9.50m, host.GetItemDecimal(1L, Column));
    }

    /// <summary>
    /// <see cref="ContextMenuModel.CheckColumn"/> skips a decimal row that already holds the ON value,
    /// and the comparison is made ON TEXT [<c>:L787</c>].
    /// </summary>
    [Fact]
    public void CheckColumnSkipsADecimalRowThatAlreadyHoldsTheOnValue()
    {
        (FakeDataWindowHost host, ContextMenuModel service) = NewService();

        // String(1.00) is "1.00", which is what the ON text has to be for the row to be skipped - the
        // oracle compares the STRINGIFIED item against the ON text, not the numbers.
        host.SetDescribe(Column + ".checkbox.on", "1.00");
        _ = host.AddRow(1.00m);
        host.CallLog.Clear();
        host.RecordsReads = true;

        Assert.Equal(RetCode.OK, service.CheckColumn(Column));

        // No change was proposed, so the host was never asked.
        Assert.False(host.CallLog.Contains("OnDoItemChange"));
    }

    /// <summary>
    /// <see cref="ContextMenuModel.CheckColumn"/> reads and writes a NUMBER column through the integer
    /// arm [<c>:L790</c>, <c>:L806</c>].
    /// </summary>
    [Fact]
    public void CheckColumnReadsAndWritesANumberColumnAsANumber()
    {
        (FakeDataWindowHost host, ContextMenuModel service) = ArrangeSecondColumn("number");
        host.SetDescribe(Second + ".checkbox.on", "7");

        Assert.Equal(RetCode.OK, service.CheckColumn(Second));

        // Written through Long(sVal) - so 7, and not "7".
        Assert.Equal(7d, host.GetItemNumber(1L, Second));
    }

    /// <summary>
    /// <see cref="ContextMenuModel.UncheckColumn"/> reads and writes a DECIMAL column through its OWN
    /// prefix-keyed decimal arm [<c>:L908-L909</c>, <c>:L924-L925</c>].
    /// </summary>
    [Fact]
    public void UncheckColumnReadsAndWritesADecimalColumnThroughThePrefixArm()
    {
        (FakeDataWindowHost host, ContextMenuModel service) = NewService();

        // "decimal(2)" truncated to five characters is "decim", which is the oracle's own arm literal.
        host.SetDescribe(Column + ".checkbox.off", "0.25");
        _ = host.AddRow(1.00m);

        Assert.Equal(RetCode.OK, service.UncheckColumn(Column));

        Assert.Equal(0.25m, host.GetItemDecimal(1L, Column));
    }

    /// <summary>
    /// <see cref="ContextMenuModel.UncheckColumn"/> reads and writes a NUMBER column through its own
    /// prefix-keyed number arm [<c>:L910-L911</c>, <c>:L926-L927</c>].
    /// </summary>
    /// <param name="colType">A raw column type whose five-character prefix is a number arm literal.</param>
    /// <remarks>
    /// <c>"ulong"</c> is EXACTLY five characters and <c>"number"</c> truncates to <c>"numbe"</c>, which
    /// is why the oracle's arm list mixes whole words with a truncation [<c>:L910</c>].
    /// </remarks>
    [Theory]
    [InlineData("number")]
    [InlineData("long")]
    [InlineData("ulong")]
    public void UncheckColumnReadsAndWritesANumberColumnThroughThePrefixArm(string colType)
    {
        (FakeDataWindowHost host, ContextMenuModel service) = ArrangeSecondColumn(colType);
        host.SetDescribe(Second + ".checkbox.off", "0");

        Assert.Equal(RetCode.OK, service.UncheckColumn(Second));

        Assert.Equal(0d, host.GetItemNumber(1L, Second));
    }

    /// <summary>
    /// <see cref="ContextMenuModel.UncheckColumn"/> sends a <c>real</c> column to the DECIMAL arm, which
    /// its two siblings also do - the one type where the two mechanisms of DEFECT 7 agree
    /// [<c>:L908</c>].
    /// </summary>
    [Fact]
    public void UncheckColumnSendsARealColumnToTheDecimalArm()
    {
        (FakeDataWindowHost host, ContextMenuModel service) = ArrangeSecondColumn("real");
        host.SetDescribe(Second + ".checkbox.off", "2.5");

        Assert.Equal(RetCode.OK, service.UncheckColumn(Second));

        Assert.Equal(2.5m, host.GetItemDecimal(1L, Second));
    }

    // ==============================================================================================
    //  THE THREE COLUMN OPERATIONS' ARGUMENT AND SENTINEL GUARDS      :L773-L776 / :L827-L832 / :L894-L897
    // ==============================================================================================

    /// <summary>
    /// An empty column name is rejected by all three check-box operations
    /// [<c>:L773</c>, <c>:L827</c>, <c>:L894</c>].
    /// </summary>
    /// <param name="operation">Which operation to drive.</param>
    [Theory]
    [InlineData("check")]
    [InlineData("uncheck")]
    [InlineData("revert")]
    public void AnEmptyColumnNameIsRejectedByEveryCheckBoxOperation(string operation)
    {
        (_, ContextMenuModel service) = NewService();

        long code = operation switch
        {
            "check" => service.CheckColumn(string.Empty),
            "uncheck" => service.UncheckColumn(string.Empty),
            _ => service.RevertCheckColumn(string.Empty),
        };

        Assert.Equal(RetCode.E_INVALID_ARGUMENT, code);
    }

    /// <summary>
    /// A column with no check-box text at all FAILS, because the sentinel guard catches the unreadable
    /// property [<c>:L776</c>, <c>:L830</c>, <c>:L897</c>].
    /// </summary>
    /// <param name="operation">Which operation to drive.</param>
    [Theory]
    [InlineData("check")]
    [InlineData("uncheck")]
    [InlineData("revert")]
    public void ANonCheckBoxColumnFailsEveryCheckBoxOperation(string operation)
    {
        // NewService's column declares no checkbox.on or checkbox.off, so both read back as the
        // unreadable sentinel.
        (_, ContextMenuModel service) = NewService();

        long code = operation switch
        {
            "check" => service.CheckColumn(Column),
            "uncheck" => service.UncheckColumn(Column),
            _ => service.RevertCheckColumn(Column),
        };

        Assert.Equal(RetCode.FAILED, code);
    }

    /// <summary>
    /// <see cref="ContextMenuModel.RevertCheckColumn"/> reads the ON and OFF texts through TWO SEPARATE
    /// guards, so a column carrying only the ON text still fails [<c>:L831-L832</c>].
    /// </summary>
    [Fact]
    public void RevertCheckColumnFailsWhenOnlyTheOnTextIsReadable()
    {
        (FakeDataWindowHost host, ContextMenuModel service) = NewService();
        host.SetDescribe(Column + ".checkbox.on", "Y");

        Assert.Equal(RetCode.FAILED, service.RevertCheckColumn(Column));
    }

    // ==============================================================================================
    //  THE TWO CLIPBOARD PRODUCERS' REMAINING ARMS                          :L514-L552 / :L602-L632
    // ==============================================================================================

    /// <summary>
    /// The column producer evaluates a COMPUTED field directly rather than through
    /// <c>LookUpDisplay</c> [<c>:L541</c>].
    /// </summary>
    [Fact]
    public void CopyColumnToTextEvaluatesAComputedFieldDirectly()
    {
        (_, ContextMenuModel service) = ArrangeComputedField();

        ClipboardTextResult result = service.CopyColumnToText(Computed);

        Assert.Equal(RetCode.OK, result.Code);

        // The compute is sum(salary for all) over a single row holding 1.00, so the expression engine
        // answers it directly - no LookUpDisplay is involved, which is the whole point of the arm.
        // DEFECT 8 - the row separator follows every row, including the last.
        Assert.Equal("1.00" + RowSeparator, result.Text);
    }

    /// <summary>
    /// The item producer maps a check-box cell holding the OFF text onto <c>"N"</c> [<c>:L621</c>].
    /// </summary>
    /// <remarks>
    /// THE MAPPING IS TO A FIXED PAIR OF ASCII LETTERS, not to the column's own texts: whatever the
    /// definition spells them, a copied check-box cell arrives as <c>"Y"</c> or <c>"N"</c>. That is what
    /// makes a copy from one DataWindow pastable into another whose check-box texts differ, and it is
    /// asserted here so a later "preserve the display text" change fails.
    /// </remarks>
    [Fact]
    public void CopyItemToTextMapsAnUncheckedCheckBoxCellOntoN()
    {
        (FakeDataWindowHost host, ContextMenuModel service) = ArrangeCheckBoxColumn();
        host.SetDescribe("Evaluate('LookUpDisplay(" + CheckColumn + ")',1)", "N");

        ClipboardTextResult result = service.CopyItemToText(1L, CheckColumn);

        Assert.Equal(RetCode.OK, result.Code);
        Assert.Equal("N", result.Text);
    }

    /// <summary>
    /// The item producer evaluates a COMPUTED field directly [<c>:L624</c>].
    /// </summary>
    [Fact]
    public void CopyItemToTextEvaluatesAComputedFieldDirectly()
    {
        (_, ContextMenuModel service) = ArrangeComputedField();

        ClipboardTextResult result = service.CopyItemToText(1L, Computed);

        Assert.Equal(RetCode.OK, result.Code);

        // No row separator on the single-cell producer - the asymmetry against the column producer is
        // the oracle's [:L546 versus :L629].
        Assert.Equal("1.00", result.Text);
    }

    // ==============================================================================================
    //  THE WIDTH PLAN'S REMAINING ARMS                                :L1130-L1141 / :L1194-L1207
    // ==============================================================================================

    /// <summary>
    /// A COMPUTED sibling over the column contributes a heading candidate evaluated at ROW ZERO
    /// [<c>:L1130</c>].
    /// </summary>
    [Fact]
    public void AComputedSiblingContributesAHeadingCandidateEvaluatedOutsideAnyRow()
    {
        (FakeDataWindowHost host, ContextMenuModel service) = NewService();

        _ = host.AddColumn("host_col", "char(10)");
        host.SetDescribe("host_col.Band", "detail");
        host.SetDescribe("host_col.Type", "column");
        host.SetDescribe("host_col.edit.style", "edit");
        host.SetDescribe("host_col.X", "100");
        host.SetDescribe("host_col.Width", "400");

        // A NUMERIC expression, because a heading compute is evaluated OUTSIDE any row [:L1130] and a
        // text literal has no row to be a literal in - the engine answers the empty string for one,
        // which the drop guard at :L1141 would then remove and the test would prove nothing.
        _ = host.AddComputedField("hdr", "header", "1234");
        host.SetDescribe("hdr.Band", "header");
        host.SetDescribe("hdr.Type", "compute");
        host.SetDescribe("hdr.X", "100");
        host.SetDescribe("hdr.Visible", "1");

        ColumnAutoWidthPlan plan = service.ColumnAutoWidth("host_col");

        TextMeasurementCandidate heading = Assert.Single(
            Assert.Single(plan.Columns).HeadingCandidates);

        Assert.Equal("hdr", heading.ObjectName);
        Assert.Equal("compute", heading.ObjectType);
        Assert.Equal("1234", heading.Text);

        // A compute contributes its VALUE, not a synthesized proxy - only a column does that.
        Assert.False(heading.IsSynthesizedProxy);
    }

    /// <summary>
    /// A sibling of any OTHER type contributes nothing, because its text is the empty string and the
    /// drop guard removes it [<c>:L1139-L1141</c>].
    /// </summary>
    [Fact]
    public void ASiblingOfAnUnhandledTypeContributesNoHeadingCandidate()
    {
        (FakeDataWindowHost host, ContextMenuModel service) = ArrangeSiblingText("header");

        // A bitmap is neither compute, column nor text, so it lands on the case-else arm.
        host.SetDescribe("sibling.Type", "bitmap");

        ColumnAutoWidthPlan plan = service.ColumnAutoWidth("host_col");

        Assert.Empty(Assert.Single(plan.Columns).HeadingCandidates);
    }

    /// <summary>
    /// A COMPUTED column's row candidates are evaluated AT EACH ROW, and the invalid-expression sentinel
    /// is rewritten to the empty string and then dropped [<c>:L1194-L1195</c>].
    /// </summary>
    [Fact]
    public void AComputedColumnsRowCandidateIsEvaluatedAtTheRow()
    {
        (_, ContextMenuModel service) = ArrangeComputedField();

        ColumnAutoWidthPlan plan = service.ColumnAutoWidth(Computed);

        ColumnWidthComputation computation = Assert.Single(plan.Columns);
        Assert.True(computation.IsCompute);

        TextMeasurementCandidate row = Assert.Single(computation.RowCandidates);
        Assert.Equal("1.00", row.Text);
        Assert.Equal("compute", row.ObjectType);
        Assert.Equal("detail", row.Band);
        Assert.False(row.IsSynthesizedProxy);
    }

    /// <summary>
    /// A computed row whose expression answers the invalid sentinel contributes NO candidate
    /// [<c>:L1195</c>, <c>:L1207</c>].
    /// </summary>
    [Fact]
    public void AComputedRowAnsweringTheInvalidSentinelContributesNoCandidate()
    {
        // "1 +" is a syntax error, which the engine reports as the invalid-expression sentinel.
        (_, ContextMenuModel service) = ArrangeComputedField("1 +");

        ColumnAutoWidthPlan plan = service.ColumnAutoWidth(Computed);

        Assert.Empty(Assert.Single(plan.Columns).RowCandidates);
    }

    /// <summary>
    /// A computed column's STATIC format mask rides along on every row candidate [<c>:L1199</c>].
    /// </summary>
    /// <remarks>
    /// THE MASK IS CARRIED, NOT APPLIED. PowerBuilder format masks have no .NET equivalent and no port
    /// exists in this repository, so the headless half RESOLVES the mask and hands it to the deferred
    /// measurer alongside the text (DECISION 8). <c>FormatAppliesToNumber</c> records whether the text
    /// is numeric, which is the one thing the mask's interpretation depends on.
    /// </remarks>
    [Fact]
    public void AComputedColumnsStaticFormatMaskRidesAlongOnTheRowCandidate()
    {
        (FakeDataWindowHost host, ContextMenuModel service) = ArrangeComputedField();
        host.SetDescribe(Computed + ".Format", "#,##0.00");

        ColumnAutoWidthPlan plan = service.ColumnAutoWidth(Computed);

        TextMeasurementCandidate row = Assert.Single(
            Assert.Single(plan.Columns).RowCandidates);

        Assert.Equal("#,##0.00", row.FormatMask);
        Assert.True(row.FormatAppliesToNumber);
    }

    /// <summary>
    /// A format that is itself an EXPRESSION is stripped to its expression text and then evaluated PER
    /// ROW [<c>:L1168-L1171</c>, <c>:L1197</c>].
    /// </summary>
    /// <remarks>
    /// THE TAB FORM LOSES ITS LAST CHARACTER, which is the oracle's own arithmetic:
    /// <c>Mid(prop,nPos + 1,Len(prop) - nPos - 1)</c> takes one character fewer than the remainder. The
    /// trailing <c>"Z"</c> below exists to make that visible - the expression the port evaluates is
    /// <c>"anymask"</c> and not <c>"anymaskZ"</c>.
    /// </remarks>
    [Fact]
    public void AComputedColumnsExpressionFormatIsStrippedAndEvaluatedPerRow()
    {
        (FakeDataWindowHost host, ContextMenuModel service) = ArrangeComputedField();

        // The tab form is `value~texpression`, and the trailing "Z" is dropped by the oracle's own
        // arithmetic - so the expression evaluated per row is the quoted literal and not the literal
        // plus a stray letter.
        host.SetDescribe(Computed + ".Format", "#,##0.00\t'0.000'Z");

        ColumnAutoWidthPlan plan = service.ColumnAutoWidth(Computed);

        TextMeasurementCandidate row = Assert.Single(
            Assert.Single(plan.Columns).RowCandidates);

        Assert.Equal("0.000", row.FormatMask);
    }

    /// <summary>
    /// A non-numeric computed value carries its mask with the numeric flag CLEAR [<c>:L1201</c>].
    /// </summary>
    [Fact]
    public void ANonNumericComputedValueCarriesItsMaskWithTheNumericFlagClear()
    {
        (FakeDataWindowHost host, ContextMenuModel service) =
            ArrangeComputedField("'abc' + 'def'");
        host.SetDescribe(Computed + ".Format", "@@@");

        ColumnAutoWidthPlan plan = service.ColumnAutoWidth(Computed);

        TextMeasurementCandidate row = Assert.Single(
            Assert.Single(plan.Columns).RowCandidates);

        Assert.Equal("abcdef", row.Text);
        Assert.Equal("@@@", row.FormatMask);
        Assert.False(row.FormatAppliesToNumber);
    }

    // ==============================================================================================
    //  THE PASTE PATH'S REMAINING ARMS                                            :L967-L1071
    // ==============================================================================================

    /// <summary>
    /// A drop-down DataWindow translates only when its display and data columns DIFFER [<c>:L969</c>].
    /// </summary>
    /// <param name="dataColumn">The <c>dddw.datacolumn</c> answer.</param>
    /// <param name="expected">The value the buffer ends up holding.</param>
    /// <remarks>
    /// WITH DISPLAY AND DATA THE SAME COLUMN there is nothing to translate, so the oracle does not even
    /// build the table - and the pasted display text is written through unchanged. That is the whole
    /// observable difference between the two arms, which is why the second row asserts the text arriving
    /// verbatim rather than asserting an empty table.
    /// </remarks>
    [Theory]
    [InlineData("dat", "NEW")]
    [InlineData("dsp", "\u65b0\u5efa")]
    public void ADropDownDataWindowTranslatesOnlyWhenDisplayAndDataDiffer(
        string dataColumn,
        string expected)
    {
        (FakeDataWindowHost host, ContextMenuModel service) = ArrangeDropDownDataWindowColumn();
        host.Column(DropDown).DropDownDataColumn = dataColumn;

        // Free text allowed, so the column is NOT enumerated and an untranslatable value is written
        // as-is rather than rejected.
        host.Column(DropDown).DropDownAllowEdit = "yes";

        Assert.Equal(RetCode.OK, service.Paste2Column(DropDown, "\u65b0\u5efa"));

        Assert.Equal(expected, host.GetItemString(1L, DropDown));
    }

    /// <summary>
    /// A drop-down DataWindow that FORBIDS typing is enumerated, so a value outside its list is
    /// rejected [<c>:L970-L971</c>, <c>:L1009-L1010</c>].
    /// </summary>
    [Fact]
    public void ADropDownDataWindowThatForbidsTypingRejectsAValueOutsideItsList()
    {
        (FakeDataWindowHost host, ContextMenuModel service) = ArrangeDropDownDataWindowColumn();
        host.Column(DropDown).DropDownAllowEdit = "no";

        Assert.Equal(RetCode.E_INVALID_ARGUMENT, service.Paste2Column(DropDown, "\u4e0d\u5b58\u5728"));

        ContextMenuError error = Assert.IsType<ContextMenuError>(service.PendingError);
        Assert.Equal(ContextMenuErrorKind.InvalidValue, error.Kind);
        Assert.Equal("\u4e0d\u5b58\u5728", error.OffendingValue);

        // Nothing was written - the operation gave up on the offending row rather than part-applying.
        Assert.Null(host.GetItemString(1L, DropDown));
    }

    /// <summary>
    /// A drop-down LIST BOX is detected by a property that has nothing to do with translation, and it
    /// translates UNCONDITIONALLY [<c>:L973-L977</c>].
    /// </summary>
    /// <remarks>
    /// THE PROBE IS <c>.ddlb.case</c> PURELY BECAUSE IT READS BACK ONLY FOR A DDLB - the oracle is using
    /// a text-casing property as a type test. Reproduced as written; substituting a more obvious probe
    /// would change which columns are detected.
    /// </remarks>
    [Fact]
    public void ADropDownListBoxIsDetectedByItsCasingPropertyAndAlwaysTranslates()
    {
        (FakeDataWindowHost host, ContextMenuModel service) = ArrangeTranslatingColumn();
        host.SetDescribe("grade.ddlb.case", "any");
        host.SetDescribe("grade.ddlb.allowedit", "yes");

        Assert.Equal(RetCode.OK, service.Paste2Column("grade", "优"));

        Assert.Equal("A", host.GetItemString(1L, "grade"));
    }

    /// <summary>
    /// A drop-down list box that forbids typing is enumerated too [<c>:L976-L977</c>].
    /// </summary>
    [Fact]
    public void ADropDownListBoxThatForbidsTypingRejectsAValueOutsideItsList()
    {
        (FakeDataWindowHost host, ContextMenuModel service) = ArrangeTranslatingColumn();
        host.SetDescribe("grade.ddlb.case", "any");
        host.SetDescribe("grade.ddlb.allowedit", "no");

        Assert.Equal(RetCode.E_INVALID_ARGUMENT, service.Paste2Column("grade", "不存在"));
    }

    /// <summary>
    /// Pasting one line into an EMPTY buffer inserts the row and suppresses repainting first
    /// [<c>:L994-L1001</c>].
    /// </summary>
    /// <remarks>
    /// The repaint suppression is LAZY and shared with the write path, so this is the one arrangement
    /// that reaches it through the GROWTH branch rather than through the first write.
    /// </remarks>
    [Fact]
    public void PastingIntoAnEmptyBufferInsertsTheRowAndSuppressesRepaintingFirst()
    {
        (FakeDataWindowHost host, ContextMenuModel service) = ArrangeSecondColumn("char(10)", addRow: false);
        host.CallLog.Clear();

        Assert.Equal(RetCode.OK, service.Paste2Column(Second, "abc"));

        Assert.Equal(1L, host.RowCount());
        Assert.Equal("abc", host.GetItemString(1L, Second));
        Assert.True(host.CallLog.Contains("SetRedraw"));
    }

    /// <summary>
    /// A DATE column validates with <c>IsDate</c>, skips a row already holding the value and writes
    /// through <c>Date()</c> [<c>:L1040-L1042</c>, <c>:L1069</c>].
    /// </summary>
    /// <param name="pasted">The pasted text.</param>
    /// <param name="expectedCode">The expected answer.</param>
    /// <param name="expectedYear">The year the buffer ends up holding.</param>
    [Theory]
    [InlineData("2001-02-03", RetCode.OK, 2001)]
    [InlineData("not a date", RetCode.E_INVALID_ARGUMENT, 1990)]
    public void PastingIntoADateColumnValidatesThenConverts(
        string pasted,
        long expectedCode,
        int expectedYear)
    {
        (FakeDataWindowHost host, ContextMenuModel service) = ArrangeSecondColumn("date");
        host.SetItem(1L, Second, new DateOnly(1990, 1, 1));

        long code = service.Paste2Column(Second, pasted);

        Assert.Equal(expectedCode, code);
        Assert.Equal(expectedYear, host.GetItemDate(1L, Second)?.Year);
    }

    /// <summary>
    /// A date row that already holds the pasted value is SKIPPED, so the host is never asked
    /// [<c>:L1041</c>].
    /// </summary>
    [Fact]
    public void PastingADateAlreadyHeldSkipsTheRow()
    {
        (FakeDataWindowHost host, ContextMenuModel service) = ArrangeSecondColumn("date");
        host.SetItem(1L, Second, new DateOnly(2001, 2, 3));
        host.CallLog.Clear();

        Assert.Equal(RetCode.OK, service.Paste2Column(Second, "2001-02-03"));

        Assert.False(host.CallLog.Contains("OnDoItemChange"));
    }

    /// <summary>
    /// A TIME column validates with <c>IsTime</c> and writes through <c>Time()</c>
    /// [<c>:L1044-L1048</c>, <c>:L1071</c>].
    /// </summary>
    /// <param name="pasted">The pasted text.</param>
    /// <param name="expectedCode">The expected answer.</param>
    /// <param name="expectedHour">The hour the buffer ends up holding.</param>
    [Theory]
    [InlineData("13:45:00", RetCode.OK, 13)]
    [InlineData("not a time", RetCode.E_INVALID_ARGUMENT, 8)]
    public void PastingIntoATimeColumnValidatesThenConverts(
        string pasted,
        long expectedCode,
        int expectedHour)
    {
        (FakeDataWindowHost host, ContextMenuModel service) = ArrangeSecondColumn("time");
        host.SetItem(1L, Second, new TimeOnly(8, 0, 0));

        long code = service.Paste2Column(Second, pasted);

        Assert.Equal(expectedCode, code);
        Assert.Equal(expectedHour, host.GetItemTime(1L, Second)?.Hour);
    }

    /// <summary>
    /// A time row that already holds the pasted value is SKIPPED [<c>:L1045</c>].
    /// </summary>
    [Fact]
    public void PastingATimeAlreadyHeldSkipsTheRow()
    {
        (FakeDataWindowHost host, ContextMenuModel service) = ArrangeSecondColumn("time");
        host.SetItem(1L, Second, new TimeOnly(13, 45, 0));
        host.CallLog.Clear();

        Assert.Equal(RetCode.OK, service.Paste2Column(Second, "13:45:00"));

        Assert.False(host.CallLog.Contains("OnDoItemChange"));
    }

    /// <summary>
    /// A DATETIME row that already holds the pasted value is SKIPPED [<c>:L1033</c>].
    /// </summary>
    [Fact]
    public void PastingADatetimeAlreadyHeldSkipsTheRow()
    {
        (FakeDataWindowHost host, ContextMenuModel service) = ArrangeSecondColumn("datetime");
        host.SetItem(1L, Second, new DateTime(2001, 2, 3, 4, 5, 6, DateTimeKind.Unspecified));
        host.CallLog.Clear();

        Assert.Equal(RetCode.OK, service.Paste2Column(Second, "2001-02-03 04:05:06"));

        Assert.False(host.CallLog.Contains("OnDoItemChange"));
    }

    /// <summary>
    /// An INTEGER row that already holds the pasted value is SKIPPED [<c>:L1021</c>].
    /// </summary>
    [Fact]
    public void PastingANumberAlreadyHeldSkipsTheRow()
    {
        (FakeDataWindowHost host, ContextMenuModel service) = ArrangeSecondColumn("number");
        // The long overload, because the number column's buffer value is read back through
        // GetItemNumber - which widens whatever is stored - and there is no double-typed setter.
        host.SetItem(1L, Second, 7L);
        host.CallLog.Clear();

        Assert.Equal(RetCode.OK, service.Paste2Column(Second, "7"));

        Assert.False(host.CallLog.Contains("OnDoItemChange"));
    }

    /// <summary>
    /// A DECIMAL row that already holds the pasted value is SKIPPED [<c>:L1027</c>].
    /// </summary>
    [Fact]
    public void PastingADecimalAlreadyHeldSkipsTheRow()
    {
        (FakeDataWindowHost host, ContextMenuModel service) = NewService();
        _ = host.AddRow(1.25m);
        host.CallLog.Clear();

        Assert.Equal(RetCode.OK, service.Paste2Column(Column, "1.25"));

        Assert.False(host.CallLog.Contains("OnDoItemChange"));
    }

    // ==============================================================================================
    //  THE REMAINING STORE ARITIES AND THE HIERARCHICAL ADDS
    // ==============================================================================================

    /// <summary>
    /// The six-argument <c>of_InsertMenu</c> inserts an ENABLED item with a tooltip [<c>:L505</c>].
    /// </summary>
    [Fact]
    public void TheSixArgumentInsertAddsAnEnabledItemWithATooltip()
    {
        (_, ContextMenuModel service) = NewService();
        _ = service.AddMenu("first", "", MID_ITEMCOPY);

        int index = service.InsertMenu(1u, true, "inserted", "Copy!", "a tip", MID_COLCOPY);

        Assert.Equal(1, index);
        Assert.Equal("inserted", service.GetText(1u, true));
        Assert.Equal("a tip", service.GetTipText(1u, true));
        Assert.Equal("Copy!", service.GetImage(1u, true));
        Assert.True(service.IsEnabled(1u, true));
    }

    /// <summary>
    /// Both hierarchical adds reject an empty label with <c>0</c>, exactly as the plain add does
    /// [<c>:L480</c>, <c>:L637</c>].
    /// </summary>
    [Fact]
    public void BothHierarchicalAddsRejectAnEmptyLabel()
    {
        (_, ContextMenuModel service) = NewService();

        Assert.Equal(0, service.AddSubmenuParent(string.Empty, "", "", true, MID_COLAUTOWIDTH));
        Assert.Equal(0, service.AddSubmenuItem(MID_COLAUTOWIDTH, string.Empty, "", "", MID_COLAUTOWIDTH_ALL));
        Assert.Equal(0, service.GetCount());
    }

    /// <summary>
    /// <c>event onprepare</c> answers <c>0</c> ON EVERY PATH [<c>:L226-L307</c>], which is why
    /// <see cref="ContextMenuModel.BuildMenu"/>'s preparation veto is unreachable in this sealed shape.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THIS TEST EXISTS TO DOCUMENT AN UNREACHABLE BRANCH RATHER THAN TO DELETE IT. <c>:L146</c> is
    /// <c>if Event OnPrepare(row,dwo) = 1 then return</c> - an equality test the oracle performs because
    /// a PowerBuilder event is overridable by any descendant object, so a subclass CAN veto there. This
    /// port is <see langword="sealed"/>, matching its two committed siblings
    /// <c>Services/ColumnSortModel.cs</c> and <c>Services/RowSelectService.cs</c>, so no descendant
    /// exists and the guard cannot fire.
    /// </para>
    /// <para>
    /// The guard is KEPT because dropping it would silently narrow the oracle's contract (constraint
    /// C-B), and this test pins the invariant that makes it dormant: every <c>return</c> in the
    /// preparation event answers <c>0</c>, including the two early exits of DEFECT 4. If a later edit
    /// ever makes preparation answer <c>1</c>, this test fails and the branch comes alive together with
    /// it.
    /// </para>
    /// </remarks>
    [Fact]
    public void PreparationAnswersZeroOnEveryPathSoItsVetoIsDormant()
    {
        (FakeDataWindowHost host, ContextMenuModel service) = ArrangeCheckBoxColumn();

        // The ordinary detail-cell path.
        Assert.Equal(0L, service.OnPrepare(1L, Dwo(CheckColumn, "column"), Pointer()));

        // DEFECT 4's first early exit - a column cell over a row.
        Assert.Equal(0L, service.OnPrepare(1L, Dwo(CheckColumn, "column"), Pointer("detail\t1")));

        // DEFECT 4's second early exit - a compute outside the three bands.
        Assert.Equal(0L, service.OnPrepare(0L, Dwo("total", "compute"), Pointer("summary\t1")));

        // The header path, which builds the auto-width item instead.
        Assert.Equal(0L, service.OnPrepare(0L, Dwo(CheckColumn + "_t", "text"), Pointer("header\t1")));

        // And the background path, promoted to header by the pointer's y position.
        Assert.Equal(0L, service.OnPrepare(0L, Dwo("dw_1", "datawindow"), Pointer("background\t0", 50L)));

        Assert.NotNull(host);
    }

    // ==============================================================================================
    //  ARRANGEMENTS for the suppression gate, the arrow allowance and the remaining typed arms
    // ==============================================================================================

    /// <summary>The column the suppression-gate tests treat as the one under edit.</summary>
    private const string CurrentColumn = "edited";

    /// <summary>The column the arrow-allowance tests measure.</summary>
    private const string DropDownColumn = "picker";

    /// <summary>A second, freely typed column for the typed-arm and paste tests.</summary>
    private const string Second = "second";

    /// <summary>A computed field, for the compute arms of the producers and the width plan.</summary>
    private const string Computed = "total";

    /// <summary>A drop-down DataWindow column, whose translation table lives in a child.</summary>
    private const string DropDown = "picked";

    /// <summary>
    /// Arranges a column that passes every guard above the edit-style switch, so a test can drive
    /// exactly one guard.
    /// </summary>
    /// <param name="editStyle">The column's <c>edit.style</c>.</param>
    /// <returns>The host, the attached service and the object under the pointer.</returns>
    /// <remarks>
    /// THE LOWER-CASE <c>".type"</c> SPELLING IS THE ONE THE GATE READS [<c>:L317</c>], against
    /// <c>".Type"</c> elsewhere in the same file. Both are taught, because the gate and the preparation
    /// event ask differently and a fixture that teaches only one silently sends every test down the
    /// non-column arm at <c>:L319</c>.
    /// </remarks>
    private static (FakeDataWindowHost Host, ContextMenuModel Service, FakeDataWindowObject Dwo)
        ArrangeCurrentCell(string editStyle)
    {
        (FakeDataWindowHost host, ContextMenuModel service) = NewService();

        FakeDataWindowObjectDefinition edited = host.AddColumn(CurrentColumn, "char(10)");
        edited.TabSequence = "50";

        host.SetDescribe(CurrentColumn + ".type", "column");
        host.SetDescribe(CurrentColumn + ".Type", "column");
        host.SetDescribe(CurrentColumn + ".Band", "detail");
        host.SetDescribe(CurrentColumn + ".TabSequence", "50");
        host.SetDescribe(CurrentColumn + ".protect", "0");
        host.SetDescribe(CurrentColumn + ".visible", "1");
        host.SetDescribe(CurrentColumn + ".edit.style", editStyle);

        _ = host.AddRow(1.00m, "N");
        _ = host.SetRow(1L);

        // The gate compares the pointer's object against the CURRENT column, so the fixture has to
        // declare which column the caret is in [:L321].
        host.CurrentColumnName = CurrentColumn;

        return (host, service, Dwo(CurrentColumn, "column"));
    }

    /// <summary>
    /// Arranges one drop-down column for the arrow-button allowance.
    /// </summary>
    /// <param name="editStyle">The edit style.</param>
    /// <param name="editable">Whether the column is editable.</param>
    /// <returns>The host and the attached service.</returns>
    /// <remarks>
    /// EDITABILITY IS EXPRESSED THROUGH THE TAB SEQUENCE because that is what
    /// <c>_of_IsColumnEditable</c> reads [<c>n_cst_dwsvc.sru</c>]: <c>"0"</c> takes the column out of the
    /// tab order and therefore out of editability, which is the branch the two <c>UseAsBorder</c> arms
    /// sit behind.
    /// </remarks>
    private static (FakeDataWindowHost Host, ContextMenuModel Service) ArrangeDropDownColumn(
        string editStyle,
        bool editable)
    {
        (FakeDataWindowHost host, ContextMenuModel service) = NewService();

        FakeDataWindowObjectDefinition picker = host.AddColumn(DropDownColumn, "char(10)");
        picker.TabSequence = editable ? "50" : "0";

        host.SetDescribe(DropDownColumn + ".Band", "detail");
        host.SetDescribe(DropDownColumn + ".Type", "column");
        host.SetDescribe(DropDownColumn + ".TabSequence", editable ? "50" : "0");
        host.SetDescribe(DropDownColumn + ".edit.style", editStyle);
        host.SetDescribe(DropDownColumn + ".Edit.Style", editStyle);
        host.SetDescribe(DropDownColumn + ".visible", "1");
        host.SetDescribe(DropDownColumn + ".protect", "0");
        host.SetDescribe(DropDownColumn + ".X", "100");
        host.SetDescribe(DropDownColumn + ".Width", "400");

        return (host, service);
    }

    /// <summary>
    /// Arranges one freely typed column of the requested raw type, with one row.
    /// </summary>
    /// <param name="colType">The raw <c>ColType</c>.</param>
    /// <param name="addRow">Whether to seed a row.</param>
    /// <returns>The host and the attached service.</returns>
    private static (FakeDataWindowHost Host, ContextMenuModel Service) ArrangeSecondColumn(
        string colType,
        bool addRow = true)
    {
        (FakeDataWindowHost host, ContextMenuModel service) = NewService();

        FakeDataWindowObjectDefinition second = host.AddColumn(Second, colType);
        second.TabSequence = "60";

        host.SetDescribe(Second + ".Band", "detail");
        host.SetDescribe(Second + ".Type", "column");
        host.SetDescribe(Second + ".ColType", colType);
        host.SetDescribe(Second + ".edit.style", "edit");
        host.SetDescribe(Second + ".visible", "1");
        host.SetDescribe(Second + ".protect", "0");

        if (addRow)
        {
            // Two values for the two data columns - salary from NewService, then this one, left null so
            // the paste path has something to write.
            _ = host.AddRow(0m, null);
        }

        return (host, service);
    }

    /// <summary>
    /// Arranges one column carrying a code table, which is what makes the paste path translate.
    /// </summary>
    /// <returns>The host and the attached service.</returns>
    private static (FakeDataWindowHost Host, ContextMenuModel Service) ArrangeTranslatingColumn()
    {
        (FakeDataWindowHost host, ContextMenuModel service) = NewService();

        FakeDataWindowObjectDefinition grade = host.AddColumn("grade", "char(1)");
        grade.TabSequence = "80";
        _ = grade.AddCodeTableEntry("优", "A");
        _ = grade.AddCodeTableEntry("良", "B");

        host.SetDescribe("grade.Band", "detail");
        host.SetDescribe("grade.Type", "column");
        host.SetDescribe("grade.ColType", "char(1)");

        // A DROP-DOWN LIST BOX, whose translation table IS the column's code table - a drop-down
        // DataWindow reads its table out of a child instead, which ArrangeDropDownDataWindowColumn
        // arranges.
        host.SetDescribe("grade.edit.style", "ddlb");
        host.SetDescribe("grade.visible", "1");
        host.SetDescribe("grade.protect", "0");

        _ = host.AddRow(0m, null);

        return (host, service);
    }

    /// <summary>
    /// Arranges one computed field in the detail band with one row behind it.
    /// </summary>
    /// <returns>The host and the attached service.</returns>
    /// <remarks>
    /// <c>Find</c> is scripted rather than evaluated: the oracle uses it as an adjacent-difference scan
    /// so that only rows whose display value differs from the row before need measuring
    /// [<c>:L1191</c>], and a fixture with a single row needs it to answer that row exactly once.
    /// </remarks>
    private static (FakeDataWindowHost Host, ContextMenuModel Service) ArrangeComputedField(
        string? expression = null)
    {
        (FakeDataWindowHost host, ContextMenuModel service) = NewService();

        _ = host.AddComputedField(
            Computed,
            "detail",
            expression ?? "sum(" + Column + " for all)");

        host.SetDescribe(Computed + ".Band", "detail");
        host.SetDescribe(Computed + ".Type", "compute");
        host.SetDescribe(Computed + ".X", "100");
        host.SetDescribe(Computed + ".Width", "400");
        host.SetDescribe(Computed + ".visible", "1");
        host.SetDescribe(Computed + ".Visible", "1");
        host.SetDescribe(Computed + ".protect", "0");

        // A computed field is not a DATA column, so the row carries only salary.
        _ = host.AddRow(1.00m);

        host.FindHandler = (_, start, _) => start <= 1L ? 1L : 0L;

        return (host, service);
    }

    /// <summary>
    /// Arranges one drop-down DataWindow column with a materialised child, which is the only shape whose
    /// translation table the base helper can actually build [<c>n_cst_dwsvc.sru:L592-L635</c>].
    /// </summary>
    /// <returns>The host and the attached service.</returns>
    /// <remarks>
    /// THE CHILD IS ATTACHED SEPARATELY FROM THE DECLARATION because the oracle distinguishes the two: a
    /// column can name a child that never materialises, and <c>IsValidObject(dwc)</c> is what guards
    /// that state. The seeded child displays <c>dsp</c> and stores <c>dat</c> over three rows, so
    /// pasting a display text is translatable and pasting anything else is not.
    /// </remarks>
    private static (FakeDataWindowHost Host, ContextMenuModel Service) ArrangeDropDownDataWindowColumn()
    {
        (FakeDataWindowHost host, ContextMenuModel service) = NewService();

        FakeDataWindowObjectDefinition picked = host.AddColumn(DropDown, "char(100)");
        picked.TabSequence = "90";
        picked.EditStyle = "dddw";
        picked.DropDownDataWindowName = "dw_test_dwsvc_dddw";
        picked.DropDownDisplayColumn = "dsp";
        picked.DropDownDataColumn = "dat";
        picked.DropDownAllowEdit = "no";

        host.SetDescribe(DropDown + ".Band", "detail");
        host.SetDescribe(DropDown + ".Type", "column");
        host.SetDescribe(DropDown + ".ColType", "char(100)");
        host.SetDescribe(DropDown + ".visible", "1");
        host.SetDescribe(DropDown + ".protect", "0");

        _ = FakeDataWindowFixtures.AttachDropDownChild(host, DropDown);

        _ = host.AddRow(0m, null);

        return (host, service);
    }

}
