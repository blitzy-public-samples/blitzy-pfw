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
//  test is 1,165 of its 1,186 coverable lines - 98.2%, measured from the Cobertura report the
//  documented `dotnet test --collect:"XPlat Code Coverage"` command emits, against which the
//  PowerFramework.DataServices assembly as a whole scores 86.0% and clears constraint C-H's 80%
//  per-service gate. The remaining twenty-one lines are not untested behaviour - they are code that is
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
//
//  FOUR STRUCTURAL REGIONS SIT AFTER THE BEHAVIOURAL ONES, each asserting a property that no single
//  behavioural test can reach. They are the ones to read first when changing the boundary rather than
//  the behaviour:
//
//    * THE C-D BOUNDARY, ASSERTED STRUCTURALLY. Reflects the whole model - private members included -
//      and proves that none of the eleven deferred locators reaches it: no DPI or unit conversion, no
//      font or popup-menu OBJECT, no canvas, no window rectangle. Then proves the published surface is
//      built out of a CLOSED type set, which is the form of the assertion that cannot be evaded by
//      naming a member carefully, and which is also what discharges "no dialog, message-box or UI type
//      in an error payload". Note carefully why the ban names OPERATIONS and not the word "font":
//      TextMeasurementCandidate CARRIES FontFace, FontHeight and Bold as data, and that is the split
//      working correctly - measuring with them is what is deferred.
//    * THE COMPUTED LOGICAL WIDTHS, DRIVEN FROM THE FIXTURE CORPUS. The Len/LenA pair and the
//      Fill("A",n) + Fill("国",m) proxy, over the corpus's own column shapes - char(100) from
//      dw_test_dwsvc_contextmenu.srd:L11 in both its ASCII and its Han display sets, and char(200)
//      from dw_sqlite.srd:L11, the only char(200) in the estate. Includes the case where the two
//      maxima come from DIFFERENT rows, which is why the oracle wraps the subtraction in Abs, plus a
//      theory proving no measure moves under any of the four DataWindow unit systems.
//    * THE TEN DIALOGS: COMPLETENESS, BOTH SPRINTF GRAMMARS, AND THE LOCALIZATION CONTRAST. Locates
//      the four sites the AAP does not name, asserts the ten as an exact set, exercises Sprintf in
//      BOTH its index-omitted and its explicit-index grammar with emitted text for each, and - with a
//      live TRANSLATING provider installed - proves these ten really do route through I18n, which the
//      rest of the suite cannot show because the silent-passthrough fallback makes an absent lookup
//      indistinguishable from a performed one.
//    * THE ORDERED MAP SEAM AND THE INIT-BEFORE-SELECTION DATA DEPENDENCY. That the column-value map
//      is genuinely Shared.Containers.OrderedMap with insertion order and one-based positional access,
//      and that an identifier is meaningful to ApplySelection only because initialization put it in
//      the model first - the data dependency behind pattern (b), whose ORDERING is
//      EventOrderingPatternTests.cs's subject rather than this file's.
//
//  NO SCREAMING_SNAKE CONSTANT IS DECLARED HERE. The nine legacy command identifiers are referenced as
//  ContextMenuModel.MID_*, matching RowSelectServiceTests, EventGateTests and DropDownSearchModelTests,
//  and matching .editorconfig, which licenses those spellings for the named implementation files only.
// ==============================================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

using Microsoft.Extensions.Options;

using PowerFramework.DataServices.Configuration;
using PowerFramework.DataServices.Domain;
using PowerFramework.DataServices.Expressions;
using PowerFramework.DataServices.Services;
using PowerFramework.Shared.Containers;
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

    // THE NINE COMMAND IDENTIFIERS ARE REFERENCED, NEVER RE-DECLARED. Every use below reads
    // ContextMenuModel.MID_* directly, so this suite has no private copy of a legacy identifier that
    // could silently drift from the oracle's own [n_cst_dwsvc_contextmenu.sru:L37-L45]. The literal
    // VALUES are still pinned - by TheNineCommandIdentifiers and TheTwoTenThousandIdentifiersAreBoth
    // Declared, which compare the service's constants against bare literals - so a renumbering is
    // caught in exactly one place instead of being masked by a matching local edit.
    //
    // This also keeps the file inside the naming boundary the repository draws deliberately:
    // .editorconfig suppresses CA1707 and IDE1006 only for the named IMPLEMENTATION files that carry
    // the preserved SCREAMING_SNAKE spellings - Services/ContextMenuModel.cs among them - and for no
    // test file, matching RowSelectServiceTests, EventGateTests and DropDownSearchModelTests, none of
    // which declares one either.

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
    /// The nine command identifiers, one theory row per oracle line: the legacy SPELLING survives
    /// verbatim, the value is the oracle's, and the width is <c>uint</c> [<c>:L37-L45</c>].
    /// </summary>
    /// <param name="locator">The oracle line the row is taken from.</param>
    /// <param name="name">The legacy identifier, looked up BY NAME rather than referenced.</param>
    /// <param name="expected">The oracle's value, as a bare literal.</param>
    /// <remarks>
    /// <para>
    /// THE CONSTANT IS RESOLVED BY NAME, WHICH IS THE POINT. A test that wrote
    /// <c>Assert.Equal(10002u, ContextMenuModel.MID_COLCHECK)</c> would pin the VALUE and say nothing
    /// about the SPELLING - yet AAP 0.4.5.3 preserves these identifiers precisely because they appear in
    /// serialized payloads, log records and characterization recordings, so a rename is as breaking as a
    /// renumbering. Reflecting the field out by its legacy name fails if either changes, and it fails
    /// with the oracle line in the message.
    /// </para>
    /// <para>
    /// <c>IsLiteral</c> IS ASSERTED because a <c>static readonly</c> field would compile every call site
    /// against a memory read rather than an inlined constant, and would silently permit a value that
    /// differs between the assembly this suite ran against and the one a consumer links.
    /// </para>
    /// <para>
    /// THE WIDTH IS <c>uint</c>, NOT <c>ulong</c>. PowerScript's <c>unsignedlong</c> is 32 bits, so
    /// <c>constant ulong</c> maps to <c>uint</c> under AAP 0.4.5.2's fixed-width convention - the name is
    /// the false friend here, and a port that read "ulong" literally would double every id's wire width.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(CommandIdentifierRows))]
    public void TheNineCommandIdentifiers(string locator, string name, uint expected)
    {
        FieldInfo? field = typeof(ContextMenuModel).GetField(
            name,
            BindingFlags.Public | BindingFlags.Static);

        Assert.NotNull(field);
        Assert.True(
            field.IsLiteral,
            locator + ": " + name + " must be a compile-time constant, as the oracle declares it.");
        Assert.Equal(typeof(uint), field.FieldType);
        Assert.Equal(expected, Assert.IsType<uint>(field.GetRawConstantValue()));
    }

    /// <summary>
    /// One row per command identifier, each carrying its oracle line [<c>:L37-L45</c>].
    /// </summary>
    /// <returns>Locator, legacy identifier, oracle value.</returns>
    public static TheoryData<string, string, uint> CommandIdentifierRows() => new()
    {
        { "n_cst_dwsvc_contextmenu.sru:L37", "MID_RESERVED", 10000u },
        { "n_cst_dwsvc_contextmenu.sru:L38", "MID_COLAUTOWIDTH", 10000u },
        { "n_cst_dwsvc_contextmenu.sru:L39", "MID_COLAUTOWIDTH_ALL", 10001u },
        { "n_cst_dwsvc_contextmenu.sru:L40", "MID_COLCHECK", 10002u },
        { "n_cst_dwsvc_contextmenu.sru:L41", "MID_COLUNCHECK", 10003u },
        { "n_cst_dwsvc_contextmenu.sru:L42", "MID_COLREVERTCHECK", 10004u },
        { "n_cst_dwsvc_contextmenu.sru:L43", "MID_COLCOPY", 10005u },
        { "n_cst_dwsvc_contextmenu.sru:L44", "MID_COLPASTE", 10006u },
        { "n_cst_dwsvc_contextmenu.sru:L45", "MID_ITEMCOPY", 10007u },
    };

    /// <summary>
    /// The identifier an item CARRIES is the same <c>uint</c> the nine constants are
    /// [<c>:L17</c> versus <c>:L37-L45</c>].
    /// </summary>
    /// <remarks>
    /// TWO DECLARATIONS, ONE WIDTH. The oracle spells the structure field <c>unsignedlong id</c> and the
    /// nine constants <c>constant ulong</c>, so a port could plausibly widen one and not the other and
    /// still compile - every assignment would simply convert. This pins them equal, which is what makes
    /// <see cref="ContextMenuModel.MID_ITEMCOPY"/> assignable to <see cref="MenuItemData.Id"/> without a
    /// narrowing conversion that could ever be lossy.
    /// </remarks>
    [Fact]
    public void AnItemIdentifierIsTheSameWidthAsTheCommandIdentifiers()
    {
        PropertyInfo? id = typeof(MenuItemData).GetProperty(
            nameof(MenuItemData.Id),
            BindingFlags.Public | BindingFlags.Instance);

        Assert.NotNull(id);
        Assert.Equal(typeof(uint), id.PropertyType);
        Assert.Equal(
            typeof(uint),
            Assert.IsType<FieldInfo>(
                typeof(ContextMenuModel).GetField(
                    nameof(ContextMenuModel.MID_ITEMCOPY),
                    BindingFlags.Public | BindingFlags.Static),
                exactMatch: false).FieldType);
    }

    /// <summary>
    /// <c>ARROW_BTN_WIDTH</c> is 18 and is still FLOATING POINT, as the oracle's <c>real</c> declares
    /// [<c>:L63</c>].
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE NARROWING IS THE HAZARD, NOT THE VALUE. 18 is expressible as an <c>int</c>, so a port that
    /// typed this constant integrally would compile, would pass any equality test written against
    /// <c>18</c>, and would then TRUNCATE - because the allowance is added to a running
    /// <c>fMaxTextWidth</c> that the oracle also declares <c>real</c> [<c>:L1088</c>, <c>:L1263</c>] and
    /// that accumulates fractional text measurements. This asserts the declared type, which is the only
    /// thing an equality test cannot reach.
    /// </para>
    /// <para>
    /// IT IS PRIVATE, AND IS READ BY REFLECTION FOR THAT REASON. The oracle declares it under
    /// <c>private:</c> [<c>:L56</c>], the port keeps it private, and promoting it to satisfy a test would
    /// widen the published surface - so the test reaches in rather than the surface reaching out. Its
    /// OBSERVABLE projection is asserted alongside, through
    /// <see cref="ColumnWidthComputation.ArrowButtonWidth"/>, which is what a consumer actually sees.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheArrowButtonWidthIsEighteenAndIsNotNarrowedToAnInteger()
    {
        FieldInfo? field = typeof(ContextMenuModel).GetField(
            "ARROW_BTN_WIDTH",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(field);
        Assert.True(field.IsLiteral, ":L63 declares it `constant`, so the port must too.");
        Assert.Equal(typeof(double), field.FieldType);
        Assert.Equal(18d, Assert.IsType<double>(field.GetRawConstantValue()));

        // NOT ONE OF THE INTEGRAL TYPES a "tidy up" would reach for. Enumerated rather than implied,
        // because `Assert.Equal(typeof(double), ...)` above already fails on a narrowing - this states
        // WHY it must, so the intent survives a future edit.
        Assert.DoesNotContain(
            field.FieldType,
            new[] { typeof(int), typeof(long), typeof(short), typeof(uint), typeof(ulong) });

        // The observable projection carries the same type, so the allowance cannot be truncated on its
        // way out to a caller either.
        PropertyInfo? projected = typeof(ColumnWidthComputation).GetProperty(
            nameof(ColumnWidthComputation.ArrowButtonWidth),
            BindingFlags.Public | BindingFlags.Instance);

        Assert.NotNull(projected);
        Assert.Equal(typeof(double), projected.PropertyType);
    }

    /// <summary>
    /// <c>PRP_POPUPMENUCREATOR</c> is the literal <c>{DWSVC_POPUPMENU_CREATOR}</c>, byte for byte and
    /// brace for brace [<c>:L65</c>].
    /// </summary>
    /// <remarks>
    /// <para>
    /// IT IS A DataWindow PROPERTY NAME, NOT A MESSAGE. The oracle writes it into the DataWindow's own
    /// property bag to mark a popup menu the service CREATED, compares it on the way back in
    /// [<c>:L649</c>] and assigns it on the way out [<c>:L679</c>] - which is what makes
    /// <see cref="MenuItemData.MenuOwner"/> decidable across a transfer. Both braces are part of the key:
    /// the tag is deliberately spelled so it cannot collide with a DataWindow property, and dropping
    /// either brace would silently stop matching a value written by an earlier build.
    /// </para>
    /// <para>
    /// ASSERTED CHARACTER BY CHARACTER as well as whole, because a single-brace or curly-quote typo in a
    /// 24-character token is exactly the kind of edit an equality-only assertion reports unhelpfully.
    /// The comparison is ordinal throughout.
    /// </para>
    /// </remarks>
    [Fact]
    public void ThePopupMenuCreatorTagIsByteExact()
    {
        FieldInfo? field = typeof(ContextMenuModel).GetField(
            "PRP_POPUPMENUCREATOR",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(field);
        Assert.True(field.IsLiteral, ":L65 declares it `constant string`.");
        Assert.Equal(typeof(string), field.FieldType);

        string tag = Assert.IsType<string>(field.GetRawConstantValue());

        Assert.Equal("{DWSVC_POPUPMENU_CREATOR}", tag, StringComparer.Ordinal);

        // BOTH BRACES, EXPLICITLY - and exactly one of each, so a doubled brace is caught too.
        Assert.StartsWith("{", tag, StringComparison.Ordinal);
        Assert.EndsWith("}", tag, StringComparison.Ordinal);
        Assert.Equal(1, tag.Count(character => character == '{'));
        Assert.Equal(1, tag.Count(character => character == '}'));

        // The token between them, uppercase and underscore-separated exactly as the oracle spells it.
        Assert.Equal("DWSVC_POPUPMENU_CREATOR", tag[1..^1], StringComparer.Ordinal);
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
                (!colCheck && (id == ContextMenuModel.MID_COLCHECK || id == ContextMenuModel.MID_COLUNCHECK))
                || (!colCopy && id == ContextMenuModel.MID_COLCOPY)
                || (!colPaste && id == ContextMenuModel.MID_COLPASTE)
                || (!itemCopy && id == ContextMenuModel.MID_ITEMCOPY);

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
        ContextMenuModel.MID_COLAUTOWIDTH,
        ContextMenuModel.MID_COLAUTOWIDTH_ALL,
        ContextMenuModel.MID_COLCHECK,
        ContextMenuModel.MID_COLUNCHECK,
        ContextMenuModel.MID_COLREVERTCHECK,
        ContextMenuModel.MID_COLCOPY,
        ContextMenuModel.MID_COLPASTE,
        ContextMenuModel.MID_ITEMCOPY,
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

        _ = service.AddMenu("check", string.Empty, ContextMenuModel.MID_COLCHECK);
        _ = service.AddMenu("uncheck", string.Empty, ContextMenuModel.MID_COLUNCHECK);
        _ = service.AddMenu("revert", string.Empty, ContextMenuModel.MID_COLREVERTCHECK);

        service.ColCheck = false;

        Assert.Equal([ContextMenuModel.MID_COLREVERTCHECK], IdsOf(service.EmitVisibleItems(service.GetCount())));
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

        _ = service.AddMenu("auto", string.Empty, ContextMenuModel.MID_COLAUTOWIDTH);
        _ = service.AddMenu("all", string.Empty, ContextMenuModel.MID_COLAUTOWIDTH_ALL);

        List<uint> withToggleSet = IdsOf(service.EmitVisibleItems(service.GetCount()));

        service.ColAutoWidth = false;
        List<uint> withToggleCleared = IdsOf(service.EmitVisibleItems(service.GetCount()));

        Assert.Equal([ContextMenuModel.MID_COLAUTOWIDTH, ContextMenuModel.MID_COLAUTOWIDTH_ALL], withToggleSet);
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
        _ = service.AddSubmenuParent(
            "parent", "img", "tip", split: true, id: ContextMenuModel.MID_COLAUTOWIDTH);
        _ = service.AddSubmenuItem(
            ContextMenuModel.MID_COLAUTOWIDTH,
            "child",
            string.Empty,
            "ctip",
            ContextMenuModel.MID_COLAUTOWIDTH_ALL);

        MenuItemData parent = Assert.Single(service.EmitVisibleItems(service.GetCount()));

        Assert.True(parent.HasSubmenu);
        Assert.True(parent.Split);
        Assert.Equal(ContextMenuModel.MID_COLAUTOWIDTH_ALL, Assert.Single(parent.Submenu).Id);
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
        Assert.Equal(ContextMenuModel.MID_ITEMCOPY, item.Id);
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
        Assert.Equal(ContextMenuModel.MID_ITEMCOPY, item.Id);
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

        Assert.DoesNotContain(ContextMenuModel.MID_ITEMCOPY, IdsOf(service.StoreItems));
    }

    /// <summary>
    /// Clearing the item-copy toggle skips the whole block, early returns included [<c>:L240</c>].
    /// </summary>
    [Fact]
    public void ClearingItemCopySkipsBothEarlyReturns()
    {
        (_, ContextMenuModel service) = NewService(OptionsWith(itemCopy: false));

        _ = service.OnPrepare(3L, Dwo(Column, "column"), Pointer());

        Assert.DoesNotContain(ContextMenuModel.MID_ITEMCOPY, IdsOf(service.StoreItems));
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

        Assert.Equal([ContextMenuModel.MID_COLAUTOWIDTH_ALL], IdsOf(service.EmitVisibleItems(service.GetCount())));
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

        Assert.Equal([ContextMenuModel.MID_COLAUTOWIDTH_ALL], IdsOf(service.EmitVisibleItems(service.GetCount())));
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
        Assert.Equal(ContextMenuModel.MID_COLAUTOWIDTH, parent.Id);
        Assert.Equal("自动列宽", parent.Text);
        Assert.Equal("SizeHorizontal!", parent.Image);
        Assert.Equal("自动调整列宽度", parent.TipText);
        Assert.True(parent.Split);

        MenuItemData child = Assert.Single(parent.Submenu);
        Assert.Equal(ContextMenuModel.MID_COLAUTOWIDTH_ALL, child.Id);
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
        Assert.Equal(ContextMenuModel.MID_COLAUTOWIDTH_ALL, item.Id);
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
        Assert.Equal([ContextMenuModel.MID_COLAUTOWIDTH_ALL], IdsOf(service.EmitVisibleItems(service.GetCount())));
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

        Assert.DoesNotContain(ContextMenuModel.MID_COLAUTOWIDTH, IdsOf(service.StoreItems));
        Assert.DoesNotContain(ContextMenuModel.MID_COLAUTOWIDTH_ALL, IdsOf(service.StoreItems));
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

        Assert.DoesNotContain(ContextMenuModel.MID_COLAUTOWIDTH, IdsOf(service.StoreItems));
        Assert.DoesNotContain(ContextMenuModel.MID_COLAUTOWIDTH_ALL, IdsOf(service.StoreItems));
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

        Assert.Equal("勾选列", byId[ContextMenuModel.MID_COLCHECK].Text);
        Assert.Equal("勾选整列", byId[ContextMenuModel.MID_COLCHECK].TipText);
        Assert.Equal(string.Empty, byId[ContextMenuModel.MID_COLCHECK].Image);

        Assert.Equal("清除勾选列", byId[ContextMenuModel.MID_COLUNCHECK].Text);
        Assert.Equal("清除勾选整列", byId[ContextMenuModel.MID_COLUNCHECK].TipText);
        Assert.Equal(string.Empty, byId[ContextMenuModel.MID_COLUNCHECK].Image);

        Assert.Equal("反向勾选列", byId[ContextMenuModel.MID_COLREVERTCHECK].Text);
        Assert.Equal("反向勾选整列", byId[ContextMenuModel.MID_COLREVERTCHECK].TipText);
        Assert.Equal(string.Empty, byId[ContextMenuModel.MID_COLREVERTCHECK].Image);

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

        Assert.DoesNotContain(ContextMenuModel.MID_COLCHECK, IdsOf(service.StoreItems));
        Assert.DoesNotContain(ContextMenuModel.MID_COLUNCHECK, IdsOf(service.StoreItems));
        Assert.DoesNotContain(ContextMenuModel.MID_COLREVERTCHECK, IdsOf(service.StoreItems));
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

        MenuItemData item = service.StoreItems.Single(entry => entry.Id == ContextMenuModel.MID_COLCOPY);
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

        Assert.Equal(expected, IdsOf(service.StoreItems).Contains(ContextMenuModel.MID_COLPASTE));
        Assert.Equal(clipboard, service.CapturedClipboardText);

        if (expected)
        {
            MenuItemData item = service.StoreItems.Single(entry => entry.Id == ContextMenuModel.MID_COLPASTE);
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

        Assert.DoesNotContain(ContextMenuModel.MID_COLPASTE, IdsOf(service.StoreItems));
    }

    /// <summary>
    /// Builds a host whose check-box column satisfies the four-way guard.
    /// </summary>
    /// <param name="addRow">Whether the buffer has a row, which is the third guard.</param>
    /// <param name="colCheck">The check toggle, which is the first guard.</param>
    /// <param name="i18n">The localization facade, defaulting to one with no provider installed.</param>
    /// <returns>The host and the attached service.</returns>
    private static (FakeDataWindowHost Host, ContextMenuModel Service) ArrangeCheckBoxColumn(
        bool addRow = true,
        bool colCheck = true,
        I18n? i18n = null)
    {
        (FakeDataWindowHost host, ContextMenuModel service) =
            NewService(OptionsWith(colCheck: colCheck), i18n);

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

        Assert.Equal(RetCode.OK, service.ApplySelection(0L, Dwo(HeaderObject, "text"), ContextMenuModel.MID_COLCOPY));

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

        _ = service.ApplySelection(0L, Dwo(HeaderObject, "text"), ContextMenuModel.MID_COLCOPY);

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
        Assert.Equal(
            RetCode.FAILED,
            service.OnDefProc(1L, Dwo(CheckColumn, "column"), (long)ContextMenuModel.MID_COLAUTOWIDTH));
        ColumnAutoWidthPlan single = Assert.IsType<ColumnAutoWidthPlan>(service.PendingWidthPlan);
        Assert.Empty(single.Columns);

        // :L208  MID_COLAUTOWIDTH_ALL plans every eligible column - and the check-box column is NOT
        // eligible for the all-columns arity [:L1107-L1110], which is why it is absent from the plan.
        Assert.Equal(
            RetCode.OK,
            service.OnDefProc(1L, Dwo(CheckColumn, "column"), (long)ContextMenuModel.MID_COLAUTOWIDTH_ALL));
        ColumnAutoWidthPlan all = Assert.IsType<ColumnAutoWidthPlan>(service.PendingWidthPlan);
        Assert.DoesNotContain(CheckColumn, all.Columns.Select(column => column.ColName));

        // :L210  MID_COLCHECK writes the ON value.
        Assert.Equal(
            RetCode.OK,
            service.OnDefProc(1L, Dwo(CheckColumn, "column"), (long)ContextMenuModel.MID_COLCHECK));
        Assert.Equal("Y", host.GetItemString(1L, CheckColumn));

        // :L212  MID_COLUNCHECK writes the OFF value.
        Assert.Equal(
            RetCode.OK,
            service.OnDefProc(1L, Dwo(CheckColumn, "column"), (long)ContextMenuModel.MID_COLUNCHECK));
        Assert.Equal("N", host.GetItemString(1L, CheckColumn));

        // :L214  MID_COLREVERTCHECK flips it back.
        Assert.Equal(
            RetCode.OK,
            service.OnDefProc(1L, Dwo(CheckColumn, "column"), (long)ContextMenuModel.MID_COLREVERTCHECK));
        Assert.Equal("Y", host.GetItemString(1L, CheckColumn));

        // :L218  MID_COLCOPY produces text and writes no clipboard (DECISION 3).
        Assert.Equal(
            RetCode.OK,
            service.OnDefProc(1L, Dwo(CheckColumn, "column"), (long)ContextMenuModel.MID_COLCOPY));
        Assert.Equal("Y" + RowSeparator, service.PendingCopiedText);

        // :L216  MID_COLPASTE applies the captured clipboard text.
        _ = host.SetItem(1L, CheckColumn, "N");
        Assert.Equal(
            RetCode.OK,
            service.OnDefProc(1L, Dwo(CheckColumn, "column"), (long)ContextMenuModel.MID_COLPASTE));
        Assert.Equal("Y", host.GetItemString(1L, CheckColumn));

        // :L220  MID_ITEMCOPY produces one cell's text, taking the column from the OBJECT rather than
        // from the clicked column.
        Assert.Equal(
            RetCode.OK,
            service.OnDefProc(1L, Dwo(CheckColumn, "column"), (long)ContextMenuModel.MID_ITEMCOPY));
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

        Assert.Contains(ContextMenuModel.MID_COLAUTOWIDTH, IdsOf(service.StoreItems));

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

        Assert.Equal(RetCode.OK, service.OnDefProc(0L, Dwo(Column, "column"), (long)ContextMenuModel.MID_RESERVED));

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

        _ = service.OnDefProc(0L, Dwo(Column, "column"), (long)ContextMenuModel.MID_COLAUTOWIDTH);
        ColumnAutoWidthPlan single = Assert.IsType<ColumnAutoWidthPlan>(service.PendingWidthPlan);
        Assert.Equal(Column, Assert.Single(single.Columns).ColName);

        _ = service.OnDefProc(0L, Dwo(Column, "column"), (long)ContextMenuModel.MID_COLAUTOWIDTH_ALL);
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
    /// <param name="i18n">The localization facade, defaulting to one with no provider installed.</param>
    /// <returns>The host and the attached service.</returns>
    private static (FakeDataWindowHost Host, ContextMenuModel Service) ArrangeTypedColumn(
        string colType,
        I18n? i18n = null)
    {
        (FakeDataWindowHost host, ContextMenuModel service) = NewService(i18n: i18n);

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
    /// <param name="i18n">The localization facade, defaulting to one with no provider installed.</param>
    /// <returns>The host and the attached service.</returns>
    private static (FakeDataWindowHost Host, ContextMenuModel Service) ArrangeEnumeratedColumn(
        I18n? i18n = null)
    {
        (FakeDataWindowHost host, ContextMenuModel service) = NewService(i18n: i18n);

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
    /// <param name="siblingColType">
    /// The sibling column's declared type, defaulting to the <c>char(10)</c> every pre-existing caller
    /// uses. The FIXTURE-DRIVEN width theory passes the real fixture types through it -
    /// <c>char(100)</c> from <c>dw_test_dwsvc_contextmenu.srd:L11</c> and <c>char(200)</c> from
    /// <c>dw_sqlite.srd:L11</c> - so the proxy is measured over the corpus's own column shapes rather
    /// than over one invented width.
    /// </param>
    /// <returns>The host and the attached service.</returns>
    private static (FakeDataWindowHost Host, ContextMenuModel Service) ArrangeSiblingColumn(
        string siblingColType = "char(10)")
    {
        (FakeDataWindowHost host, ContextMenuModel service) = NewService();

        _ = host.AddColumn("host_col", "char(10)");
        _ = host.AddColumn("typed", siblingColType);

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
        _ = service.AddMenu("first", "", ContextMenuModel.MID_ITEMCOPY);

        int index = service.InsertMenu(1u, true, "inserted", "Copy!", "a tip", ContextMenuModel.MID_COLCOPY);

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

        Assert.Equal(
            0,
            service.AddSubmenuParent(string.Empty, "", "", true, ContextMenuModel.MID_COLAUTOWIDTH));
        Assert.Equal(
            0,
            service.AddSubmenuItem(
                ContextMenuModel.MID_COLAUTOWIDTH,
                string.Empty,
                "",
                "",
                ContextMenuModel.MID_COLAUTOWIDTH_ALL));
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

    // ==============================================================================================
    //  THE C-D BOUNDARY, ASSERTED STRUCTURALLY                       AAP 0.2.1.3 CORRECTION 4 / C-D
    //  --------------------------------------------------------------------------------------------
    //  Correction 4 splits n_cst_dwsvc_contextmenu.sru in two. THE HEADLESS HALF SHIPS: the complete
    //  menu item model - labels, ids, enabled and split flags - and the COMPUTED LOGICAL text widths.
    //  THE RENDERING HALF DOES NOT, and it is identified in the oracle by these eleven locators:
    //
    //      :L18                    n_cst_popupmenu submenu       the structure's own field type
    //      :L96-L106               n_cst_popupmenu insert API     the eight insert-submenu overloads
    //      :L187                   Win32.GetWindowRect            window geometry for the popup origin
    //      :L1092, :L1098          font = Create n_cst_font       font object, all-columns arity
    //      :L1266, :L1277          font = Create n_cst_font       font object, single-column arity
    //      :L1241, :L1243          Win32.PX2MMX(D2PX(...))        DPI and unit conversion, all columns
    //      :L1409, :L1411          Win32.PX2MMX(D2PX(...))        DPI and unit conversion, one column
    //
    //  Every one of those belongs to DesignSystem, which AAP 0.2.2.2 forbids implementing "even
    //  partially, even to 'stub them out'", and which is reachable only as the RESERVED Gateway route
    //  `/v1/design/**` (AAP 0.4.4) - a routing declaration, not a stub.
    //
    //  WHY THE BANNED SET NAMES OPERATIONS AND NOT THE WORD "FONT". TextMeasurementCandidate carries
    //  FontFace, FontHeight and Bold, and that is the split working correctly rather than a leak: those
    //  three are raw `Describe` answers - DATA about which font a renderer should use - exactly as
    //  ColumnWidth and ColumnXPosition are data about geometry. What is deferred is MEASURING with
    //  them, which needs a device context this service does not have and must never acquire. So the
    //  sweep bans the conversion and rendering VERBS (PX2MM, D2PX, GetWindowRect, Canvas, Render) and
    //  the legacy TYPE names (n_cst_font, n_cst_popupmenu), and asserts the descriptors are present as
    //  data. Banning "Font" outright would fail on a correct implementation and would then be "fixed"
    //  by deleting the descriptors - which is the actual regression.
    // ==============================================================================================

    /// <summary>
    /// Every type this model publishes, so the two sweeps below cover the whole reachable surface.
    /// </summary>
    private static IReadOnlyList<Type> PublishedTypes =>
    [
        typeof(ContextMenuModel),
        typeof(MenuItemData),
        typeof(ContextMenuLayout),
        typeof(ContextMenuPointerContext),
        typeof(ContextMenuError),
        typeof(ClipboardTextResult),
        typeof(TextMeasurementCandidate),
        typeof(ColumnWidthComputation),
        typeof(ColumnAutoWidthPlan),
        typeof(ContextMenuOutcome),
        typeof(ContextMenuErrorKind),
        typeof(ContextMenuMessageIcon),
        typeof(TextMeasurementMode),
    ];

    /// <summary>
    /// Unwraps a by-ref or generic type to the types it is actually built out of.
    /// </summary>
    /// <param name="type">The reflected type.</param>
    /// <returns><paramref name="type"/> and, recursively, its element and argument types.</returns>
    private static IEnumerable<Type> Flatten(Type type)
    {
        yield return type;

        if (type.HasElementType)
        {
            Type? element = type.GetElementType();
            if (element is not null)
            {
                foreach (Type inner in Flatten(element))
                {
                    yield return inner;
                }
            }
        }

        foreach (Type argument in type.GetGenericArguments())
        {
            foreach (Type inner in Flatten(argument))
            {
                yield return inner;
            }
        }
    }

    /// <summary>
    /// The whole reflected surface - every member name and every type name it touches, public and
    /// private alike.
    /// </summary>
    /// <returns>Member names, then the full names of every type carried.</returns>
    private static List<string> ReflectedVocabulary()
    {
        const BindingFlags Everything =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static
            | BindingFlags.DeclaredOnly;

        List<string> vocabulary = [];

        foreach (Type type in PublishedTypes)
        {
            vocabulary.Add(type.Name);

            foreach (MemberInfo member in type.GetMembers(Everything))
            {
                vocabulary.Add(member.Name);
            }

            IEnumerable<Type> carried =
            [
                .. type.GetFields(Everything).Select(field => field.FieldType),
                .. type.GetProperties(Everything).Select(property => property.PropertyType),
                .. type.GetMethods(Everything).Select(method => method.ReturnType),
                .. type.GetMethods(Everything)
                    .SelectMany(method => method.GetParameters())
                    .Select(parameter => parameter.ParameterType),
                .. type.GetConstructors(Everything)
                    .SelectMany(constructor => constructor.GetParameters())
                    .Select(parameter => parameter.ParameterType),
            ];

            foreach (Type candidate in carried.SelectMany(Flatten))
            {
                vocabulary.Add(candidate.FullName ?? candidate.Name);
            }
        }

        return vocabulary;
    }

    /// <summary>
    /// The eleven deferred locators reach NOTHING in this model - no member name and no carried type
    /// names a DPI conversion, a font object, a canvas, a popup menu or a window rectangle.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE SWEEP INCLUDES PRIVATE MEMBERS DELIBERATELY. A deferred capability smuggled in as a private
    /// helper is still implemented, and constraint C-D is about what the code DOES, not about what it
    /// publishes. The positive control below proves the vocabulary was genuinely read, so an empty
    /// reflection result cannot pass this test silently.
    /// </para>
    /// <para>
    /// <c>Popup</c> is banned over the PUBLISHED surface only, because the oracle's own property tag
    /// <c>PRP_POPUPMENUCREATOR</c> [<c>:L65</c>] is a private constant this port keeps - it is a STRING
    /// KEY that decides <see cref="MenuItemData.MenuOwner"/>, not a menu. The legacy TYPE name
    /// <c>n_cst_popupmenu</c> is banned everywhere, private members included, and that is the ban that
    /// actually forbids the deferred submenu API of <c>:L96-L106</c>.
    /// </para>
    /// </remarks>
    [Fact]
    public void NoDeferredGeometryFontOrMenuRenderingReachesThisModel()
    {
        string[] bannedEverywhere =
        [
            // Window geometry - :L187.
            "GetWindowRect", "WindowRect", "Win32",

            // DPI and unit conversion - :L1241, :L1243, :L1409, :L1411.
            "PX2MM", "D2PX", "D2PY", "D2UX", "P2DX", "P2DY", "U2PY", "Dpi", "Pixel", "Millimet",

            // Font and menu OBJECTS - :L1092, :L1098, :L1266, :L1277, :L18, :L96-L106.
            "n_cst_font", "n_cst_popupmenu",

            // Rendering.
            "Canvas", "Render", "Repaint", "Painter",
        ];

        List<string> vocabulary = ReflectedVocabulary();

        // POSITIVE CONTROL: the vocabulary really was read, and it contains the members the headless
        // half is REQUIRED to have. Without this, a reflection change that returned nothing would turn
        // every assertion below into a vacuous truth.
        Assert.Contains(nameof(ContextMenuModel.ColumnAutoWidth), vocabulary);
        Assert.Contains(nameof(MenuItemData.Split), vocabulary);
        Assert.Contains("PRP_POPUPMENUCREATOR", vocabulary);

        Assert.All(
            vocabulary,
            word => Assert.All(
                bannedEverywhere,
                banned => Assert.False(
                    word.Contains(banned, StringComparison.OrdinalIgnoreCase),
                    "'" + word + "' names the deferred half through '" + banned + "'. DPI conversion, "
                    + "font measurement and menu rendering belong to DesignSystem behind /v1/design/** "
                    + "(AAP 0.2.1.3 Correction 4, 0.2.2.2, 0.4.4).")));

        // AND NO POPUP MENU ON THE PUBLISHED SURFACE, where a consumer could bind to one.
        const BindingFlags Published =
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        IEnumerable<string> publishedNames = PublishedTypes
            .SelectMany(type => type.GetMembers(Published))
            .Select(member => member.Name);

        Assert.All(
            publishedNames,
            name => Assert.DoesNotContain("Popup", name, StringComparison.OrdinalIgnoreCase));

        // POSITIVELY: the font DESCRIPTORS are present, because carrying them IS the headless half. If
        // a later edit "cleans up" the deferred half by deleting these, the renderer loses the data it
        // needs and this fails - which is the opposite failure from the one above, and both matter.
        Assert.Contains(nameof(TextMeasurementCandidate.FontFace), vocabulary);
        Assert.Contains(nameof(TextMeasurementCandidate.FontHeight), vocabulary);
        Assert.Contains(nameof(TextMeasurementCandidate.Bold), vocabulary);
    }

    /// <summary>
    /// Every type carried on the PUBLISHED surface, normalized past <c>in</c>/<c>ref</c> and
    /// <c>Nullable&lt;&gt;</c> wrappers.
    /// </summary>
    /// <param name="everything">
    /// Whether to include private members as well as published ones.
    /// </param>
    /// <returns>The carried types.</returns>
    private static List<Type> CarriedTypes(bool everything)
    {
        BindingFlags flags =
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly
            | (everything ? BindingFlags.NonPublic : BindingFlags.Default);

        List<Type> carried = [];

        foreach (Type type in PublishedTypes)
        {
            carried.AddRange(type.GetFields(flags).Select(field => field.FieldType));
            carried.AddRange(type.GetProperties(flags).Select(property => property.PropertyType));
            carried.AddRange(type.GetMethods(flags).Select(method => method.ReturnType));
            carried.AddRange(
                type.GetMethods(flags)
                    .SelectMany(method => method.GetParameters())
                    .Select(parameter => parameter.ParameterType));
            carried.AddRange(
                type.GetConstructors(flags)
                    .SelectMany(constructor => constructor.GetParameters())
                    .Select(parameter => parameter.ParameterType));
        }

        return
        [
            .. carried
                .SelectMany(Flatten)
                .Select(type => type.IsByRef ? type.GetElementType() ?? type : type)
                .Select(type => Nullable.GetUnderlyingType(type) ?? type),
        ];
    }

    /// <summary>
    /// The PUBLISHED surface is built out of a CLOSED set of types: BCL scalars, its own records and
    /// enums, its three injected collaborators and the host handle. No UI type is reachable.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE STRONGEST FORM OF C-D AVAILABLE TO A TEST, AND THE FORM THAT DISCHARGES "NO DIALOG,
    /// MESSAGE-BOX OR UI TYPE IN AN ERROR PAYLOAD". A name ban can be evaded by naming a member
    /// carefully; a closed type set cannot, because a dialog, a font, a canvas or a popup menu would
    /// have to appear as a TYPE on the surface to be usable at all. The set is enumerated rather than
    /// pattern-matched, so widening it is a visible edit to this list rather than an accident.
    /// </para>
    /// <para>
    /// SCOPED TO THE PUBLISHED SURFACE ON PURPOSE. The private surface legitimately uses a much wider
    /// slice of the BCL - <see cref="DateTime"/>, <see cref="DateOnly"/>, <see cref="TimeOnly"/> and
    /// <see cref="decimal"/> for the six typed paste arms [<c>:L1013-L1049</c>], tuples and
    /// <see cref="List{T}"/> for the width walk - and enumerating that would assert nothing about the
    /// boundary. <see cref="NoTypeOnTheWholeSurfaceComesFromAUiAssembly"/> covers the private half at
    /// assembly granularity, which is the level at which a UI dependency actually shows up.
    /// </para>
    /// </remarks>
    [Fact]
    public void ThePublishedSurfaceIsBuiltOutOfAClosedTypeSet()
    {
        HashSet<Type> allowed =
        [
            // The BCL scalars the item model, the error, the layout and the width plan are made of.
            typeof(void), typeof(bool), typeof(int), typeof(long), typeof(uint), typeof(double),
            typeof(string), typeof(object),

            // Its own published records and enums.
            .. PublishedTypes,

            // The three constructor collaborators plus the two host handles the events take.
            typeof(I18n),
            typeof(DataWindowExpressionEvaluator),
            typeof(IOptions<DataServicesOptions>),
            typeof(DataServicesOptions),
            typeof(DataWindowServiceHost),
            typeof(IDataWindowObject),

            // The one collection shape the surface publishes.
            typeof(IReadOnlyList<MenuItemData>),
            typeof(IReadOnlyList<TextMeasurementCandidate>),
            typeof(IReadOnlyList<ColumnWidthComputation>),
        ];

        List<Type> carried = CarriedTypes(everything: false);

        // POSITIVE CONTROL: the surface really was reflected, and it carries the error type whose payload
        // this assertion is about.
        Assert.NotEmpty(carried);
        Assert.Contains(typeof(ContextMenuError), carried);

        Assert.All(
            carried,
            type => Assert.True(
                allowed.Contains(type),
                "'" + (type.FullName ?? type.Name) + "' is published on the context-menu surface and is "
                + "not in the closed set. A UI, dialog, message-box, font or menu-rendering type here "
                + "would breach constraint C-D."));
    }

    /// <summary>
    /// Nothing anywhere on the surface - published or private - comes from a user-interface assembly,
    /// and the service assembly references none.
    /// </summary>
    /// <remarks>
    /// ASSEMBLY GRANULARITY IS WHERE A UI DEPENDENCY ACTUALLY APPEARS. A font, a canvas, a window or a
    /// dialog cannot be conjured from the BCL slice this service links on Linux; it has to arrive as a
    /// package or project reference. So this asserts both halves: every carried type belongs to the
    /// framework, to this repository's own assemblies or to the options abstraction, AND the service
    /// assembly's own reference list names no drawing, forms, windows or deferred-service assembly.
    /// </remarks>
    [Fact]
    public void NoTypeOnTheWholeSurfaceComesFromAUiAssembly()
    {
        List<Type> carried = CarriedTypes(everything: true);

        Assert.NotEmpty(carried);

        Assert.All(
            carried,
            type =>
            {
                string assembly = type.Assembly.GetName().Name ?? string.Empty;

                Assert.True(
                    assembly.StartsWith("System.", StringComparison.Ordinal)
                    || assembly.StartsWith("PowerFramework.", StringComparison.Ordinal)
                    || assembly.StartsWith("Microsoft.Extensions.Options", StringComparison.Ordinal)
                    || string.Equals(assembly, "netstandard", StringComparison.Ordinal),
                    "'" + (type.FullName ?? type.Name) + "' comes from '" + assembly + "', which is "
                    + "outside the framework, this repository and the options abstraction.");
            });

        // AND THE REFERENCE LIST ITSELF, so a UI type is not merely unused but unavailable.
        string[] referenced =
        [
            .. typeof(ContextMenuModel).Assembly
                .GetReferencedAssemblies()
                .Select(reference => reference.Name ?? string.Empty),
        ];

        // POSITIVE CONTROL: the list was genuinely read and names a shared library this service does use.
        Assert.Contains("PowerFramework.Shared.Localization", referenced);

        string[] bannedAssemblies =
        [
            "System.Drawing", "System.Windows", "WindowsBase", "PresentationCore",
            "PresentationFramework", "System.Windows.Forms", "Avalonia", "SkiaSharp",
            "DesignSystem", "Documents", "Integration", "ScriptBridge",
        ];

        Assert.All(
            referenced,
            name => Assert.All(
                bannedAssemblies,
                banned => Assert.False(
                    name.Contains(banned, StringComparison.OrdinalIgnoreCase),
                    "PowerFramework.DataServices references '" + name + "'. Rendering belongs to the "
                    + "deferred DesignSystem service, which receives no code in this phase "
                    + "(AAP 0.2.2.2).")));
    }

    /// <summary>
    /// <c>MENUITEMDATA</c>'s eight legacy fields, one theory row each, mapped onto the port's members
    /// with the legacy declaration's own line [<c>:L12-L21</c>].
    /// </summary>
    /// <param name="locator">The oracle line the field is declared on.</param>
    /// <param name="legacyField">The legacy field name and type, as the structure spells it.</param>
    /// <param name="portMember">The port's member.</param>
    /// <param name="portType">The port's type for it.</param>
    /// <remarks>
    /// SEVEN MAP ONE TO ONE; THE EIGHTH SPLITS, AND THAT IS THE C-D SPLIT ITSELF. The legacy
    /// <c>n_cst_popupmenu submenu</c> [<c>:L18</c>] is an OBJECT POINTER to a live popup menu, and a
    /// pointer neither serializes nor exists without the deferred rendering half. The port carries what
    /// the oracle actually READS off it - whether a child is attached [<c>:L179</c>], the identifier the
    /// child is addressed by [<c>:L273</c>], and the child's items [<c>:L274</c>] - as
    /// <see cref="MenuItemData.HasSubmenu"/>, <see cref="MenuItemData.SubmenuHandle"/> and
    /// <see cref="MenuItemData.Submenu"/>. <see cref="TheSubmenuFieldIsDataAndNotAPopupMenu"/> asserts
    /// the negative side of that split.
    /// </remarks>
    [Theory]
    [MemberData(nameof(MenuItemFieldRows))]
    public void MenuItemDataCarriesTheLegacyEightFields(
        string locator,
        string legacyField,
        string portMember,
        Type portType)
    {
        PropertyInfo? property = typeof(MenuItemData).GetProperty(
            portMember,
            BindingFlags.Public | BindingFlags.Instance);

        Assert.NotNull(property);
        Assert.Equal(portType, property.PropertyType);
        Assert.True(
            property.CanRead,
            locator + ": " + legacyField + " must be readable through " + portMember + ".");
    }

    /// <summary>
    /// One row per legacy structure field [<c>:L12-L21</c>].
    /// </summary>
    /// <returns>Locator, legacy declaration, port member, port type.</returns>
    public static TheoryData<string, string, string, Type> MenuItemFieldRows() => new()
    {
        { "n_cst_dwsvc_contextmenu.sru:L13", "boolean enabled", nameof(MenuItemData.Enabled), typeof(bool) },
        { "n_cst_dwsvc_contextmenu.sru:L14", "string text", nameof(MenuItemData.Text), typeof(string) },
        { "n_cst_dwsvc_contextmenu.sru:L15", "string image", nameof(MenuItemData.Image), typeof(string) },
        { "n_cst_dwsvc_contextmenu.sru:L16", "string tiptext", nameof(MenuItemData.TipText), typeof(string) },
        { "n_cst_dwsvc_contextmenu.sru:L17", "unsignedlong id", nameof(MenuItemData.Id), typeof(uint) },
        {
            "n_cst_dwsvc_contextmenu.sru:L18",
            "n_cst_popupmenu submenu",
            nameof(MenuItemData.Submenu),
            typeof(IReadOnlyList<MenuItemData>)
        },
        {
            "n_cst_dwsvc_contextmenu.sru:L18",
            "n_cst_popupmenu submenu",
            nameof(MenuItemData.SubmenuHandle),
            typeof(uint)
        },
        {
            "n_cst_dwsvc_contextmenu.sru:L18",
            "n_cst_popupmenu submenu",
            nameof(MenuItemData.HasSubmenu),
            typeof(bool)
        },
        { "n_cst_dwsvc_contextmenu.sru:L19", "boolean split", nameof(MenuItemData.Split), typeof(bool) },
        {
            "n_cst_dwsvc_contextmenu.sru:L20",
            "boolean menuowner",
            nameof(MenuItemData.MenuOwner),
            typeof(bool)
        },
    };

    /// <summary>
    /// The submenu is a CHILD-ITEM COLLECTION plus a handle, never an <c>n_cst_popupmenu</c>-typed
    /// object [<c>:L18</c>, <c>:L96-L106</c>].
    /// </summary>
    /// <remarks>
    /// THE COLLECTION IS RECURSIVE AND HELD BY VALUE, so a child that itself has children is
    /// expressible - which the oracle supports and the default set uses to exactly one level
    /// [<c>:L272-L274</c>]. It is EMPTY, not null, when there is no child, so a consumer may enumerate
    /// unconditionally, and the emptiness is distinguishable from absence through
    /// <see cref="MenuItemData.HasSubmenu"/> - which matters because <c>:L272</c> attaches a valid but
    /// still-empty child menu before <c>:L274</c> puts anything in it.
    /// </remarks>
    [Fact]
    public void TheSubmenuFieldIsDataAndNotAPopupMenu()
    {
        PropertyInfo submenu = Assert.IsType<PropertyInfo>(
            typeof(MenuItemData).GetProperty(
                nameof(MenuItemData.Submenu),
                BindingFlags.Public | BindingFlags.Instance),
            exactMatch: false);

        Assert.Equal(typeof(IReadOnlyList<MenuItemData>), submenu.PropertyType);

        // RECURSIVE BY TYPE: the element type is MenuItemData itself.
        Assert.Equal(typeof(MenuItemData), Assert.Single(submenu.PropertyType.GetGenericArguments()));

        // EMPTY, NOT NULL, on a default item - and the flag says "no child" independently.
        MenuItemData bare = new() { Text = "x", Id = ContextMenuModel.MID_ITEMCOPY };

        Assert.NotNull(bare.Submenu);
        Assert.Empty(bare.Submenu);
        Assert.False(bare.HasSubmenu);
        Assert.Equal(0u, bare.SubmenuHandle);

        // AND THE FLAG IS INDEPENDENT OF THE COUNT, which is the state :L272 passes through.
        MenuItemData attachedButEmpty = bare with
        {
            HasSubmenu = true,
            SubmenuHandle = ContextMenuModel.MID_COLAUTOWIDTH,
        };

        Assert.True(attachedButEmpty.HasSubmenu);
        Assert.Empty(attachedButEmpty.Submenu);
    }


    // ==============================================================================================
    //  THE COMPUTED LOGICAL WIDTHS, DRIVEN FROM THE FIXTURE CORPUS       :L1132-L1135 / :L1298-L1301
    //  --------------------------------------------------------------------------------------------
    //  This is the headless half's SIGNATURE OUTPUT, and the oracle computes it in four statements:
    //
    //      nAsc2Cnt = Long(_of_Evaluate("Max(Len(LookUpDisplay(" + obj + ")))"))          the CHARACTER measure
    //      nChsCnt  = Abs(Long(_of_Evaluate("Max(LenA(LookUpDisplay(" + obj + ")))")) - nAsc2Cnt)   the DBCS BYTE excess
    //      nAsc2Cnt -= nChsCnt
    //      sText    = Fill("A",nAsc2Cnt) + Fill("国",nChsCnt)
    //
    //  THE PROXY IS THE SHAPE OF NO REAL VALUE, AND THAT IS DELIBERATE. Both aggregates are maxima
    //  taken INDEPENDENTLY over the buffer, so the widest-by-characters row and the widest-by-bytes row
    //  can be different rows - which is exactly why the oracle wraps the subtraction in Abs. The result
    //  is a synthetic worst case: the narrowest string that is simultaneously at least as long as the
    //  longest value and at least as many bytes as the byte-widest value.
    //
    //  THE ROWS BELOW ARE THE CORPUS'S OWN COLUMNS, not invented widths. char(100) with the fixture's
    //  own code-table displays comes from dw_test_dwsvc_contextmenu.srd:L11 - `type=char(100) ...
    //  name=s1 ... values="新建 NEW/确认 CFD/审核 ADT/"` - which supplies BOTH an ASCII display set and a
    //  Han display set. char(200) occurs exactly once in the whole DataWindow corpus, at
    //  dw_sqlite.srd:L11 (`type=char(200) ... name=address`), the AAP 0.6.3.1 golden-master fixture, and
    //  an address column is precisely where a mixed-script worst case arises in practice.
    // ==============================================================================================

    /// <summary>
    /// The two measures and the synthesized proxy, one theory row per fixture column shape.
    /// </summary>
    /// <param name="locator">The fixture the column shape is taken from.</param>
    /// <param name="colType">The column's declared type, verbatim from that fixture.</param>
    /// <param name="values">The seeded display values, semicolon-separated.</param>
    /// <param name="expectedAscii">The derived ASCII count - <c>Max(Len) - excess</c>.</param>
    /// <param name="expectedWide">The derived wide count - <c>Abs(Max(LenA) - Max(Len))</c>.</param>
    /// <param name="expectedProxy">The proxy string, byte for byte.</param>
    /// <remarks>
    /// <para>
    /// BOTH MEASURES ARE ASSERTED AND BOTH ARE EXPOSED, because a consumer that could see only the
    /// character count could not size a DBCS column and one that could see only the byte count could not
    /// size an ASCII one. They are separate members on
    /// <see cref="TextMeasurementCandidate"/> for that reason.
    /// </para>
    /// <para>
    /// THE HAN CHARACTER IS ASSERTED AS ITSELF, not as a code point or a length. The oracle fills with
    /// <c>国</c> specifically [<c>:L1135</c>, <c>:L1301</c>], and any other wide character - even another
    /// two-byte one - would measure differently under a real font, so the substitution is part of the
    /// contract rather than an implementation detail.
    /// </para>
    /// <para>
    /// THE AGGREGATES ARE COMPUTED BY THE REAL EVALUATOR over really seeded rows. Nothing here stubs
    /// <c>Max</c>, <c>Len</c>, <c>LenA</c> or <c>LookUpDisplay</c>, so the test exercises the same path
    /// production takes and would fail if any of the four drifted.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(FixtureColumnWidthRows))]
    public void TheLogicalWidthsAreDerivedFromTheFixtureColumns(
        string locator,
        string colType,
        string values,
        int expectedAscii,
        int expectedWide,
        string expectedProxy)
    {
        (FakeDataWindowHost host, ContextMenuModel service) = ArrangeSiblingColumn(colType);

        foreach (string value in values.Split(';'))
        {
            _ = host.AddRow(0m, "hc", value);
        }

        ColumnWidthComputation computation =
            Assert.Single(service.ColumnAutoWidth("host_col").Columns);

        TextMeasurementCandidate candidate = Assert.Single(computation.HeadingCandidates);

        Assert.True(
            candidate.IsSynthesizedProxy,
            locator + ": a " + colType + " column must measure through the synthesized proxy.");
        Assert.Equal(expectedAscii, candidate.AsciiCount);
        Assert.Equal(expectedWide, candidate.WideCount);
        Assert.Equal(expectedProxy, candidate.Text, StringComparer.Ordinal);

        // THE PROXY IS EXACTLY Fill("A",n) + Fill("国",m) - reconstructed here from the two measures the
        // candidate itself reports, so the assertion cannot pass by coincidence on a hand-written string.
        Assert.Equal(
            new string('A', candidate.AsciiCount) + new string('国', candidate.WideCount),
            candidate.Text,
            StringComparer.Ordinal);
    }

    /// <summary>
    /// One row per fixture column shape, each carrying the fixture it came from.
    /// </summary>
    /// <returns>Locator, column type, seeded values, ASCII count, wide count, proxy.</returns>
    public static TheoryData<string, string, string, int, int, string> FixtureColumnWidthRows() => new()
    {
        // dw_test_dwsvc_contextmenu.srd:L11 - s1's ASCII display set. Three rows of three ASCII
        // characters: Max(Len) = 3, Max(LenA) = 3, so the excess is 0 and the proxy is pure ASCII.
        { "dw_test_dwsvc_contextmenu.srd:L11", "char(100)", "NEW;CFD;ADT", 3, 0, "AAA" },

        // dw_test_dwsvc_contextmenu.srd:L11 - s1's HAN display set, the same column's other half. Three
        // rows of two Han characters: Max(Len) = 2, Max(LenA) = 4, so the excess is 2, the ASCII count
        // falls to 0, and the proxy is entirely wide. THE WIDE MEASURE IS LARGER THAN THE CHARACTER
        // MEASURE HERE AND SMALLER IN THE ROW ABOVE, which is the split this theory exists to show.
        { "dw_test_dwsvc_contextmenu.srd:L11", "char(100)", "新建;确认;审核", 0, 2, "国国" },

        // dw_sqlite.srd:L11 - the only char(200) in the corpus, holding a plain ASCII address.
        // Max(Len) = Max(LenA) = 13, so the proxy is thirteen ASCII characters.
        { "dw_sqlite.srd:L11", "char(200)", "1 Main Street", 13, 0, "AAAAAAAAAAAAA" },

        // dw_sqlite.srd:L11 - the same char(200) column with a MIXED-SCRIPT buffer, and the case that
        // proves the two maxima are taken independently. Row 1 is `深圳A` (3 characters, 5 bytes) and
        // row 2 is `ABCD` (4 characters, 4 bytes), so Max(Len) = 4 comes from row 2 while
        // Max(LenA) = 5 comes from row 1. The excess is 1, the ASCII count is 3, and the proxy `AAA国`
        // is the shape of NEITHER row - it is 4 characters like the longer and 5 bytes like the wider.
        { "dw_sqlite.srd:L11", "char(200)", "深圳A;ABCD", 3, 1, "AAA国" },
    };

    /// <summary>
    /// The widths are LOGICAL and stay logical: changing the DataWindow's unit system does not change
    /// one measure, because the conversion is the deferred half [<c>:L1239-L1247</c>,
    /// <c>:L1407-L1415</c>].
    /// </summary>
    /// <param name="units">
    /// The <c>DataWindow.Units</c> answer - <c>"1"</c> pixels, <c>"2"</c> thousandths of an inch,
    /// <c>"3"</c> thousandths of a centimetre, anything else PowerBuilder units.
    /// </param>
    /// <remarks>
    /// <para>
    /// THIS IS THE POSITIVE FORM OF THE C-D SPLIT, and it is the assertion that a later "helpful" unit
    /// conversion here would fail. The oracle's four-armed <c>choose case</c> on
    /// <c>DataWindow.Units</c> ends in <c>Win32.PX2MMX(D2PX(fMaxTextWidth + 4))</c> - a DPI-dependent,
    /// device-context-dependent conversion belonging to DesignSystem behind <c>/v1/design/**</c>. The
    /// port therefore PUBLISHES the unit token and the raw ingredients and converts nothing: the
    /// character count, the byte count, the proxy string, the arrow allowance and the padding are
    /// identical under all four unit systems, and only <see cref="ColumnAutoWidthPlan.Units"/> changes.
    /// </para>
    /// <para>
    /// THE PADDING IS PART OF THE EVIDENCE. <c>+ 4</c> appears INSIDE every arm of the oracle's
    /// conversion, so a port that converted would fold it in and it would no longer be separately
    /// visible. It is published as <see cref="ColumnWidthComputation.Padding"/> at its raw value
    /// instead, which is what lets the deferred half apply the oracle's arithmetic exactly.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("1")]
    [InlineData("2")]
    [InlineData("3")]
    [InlineData("0")]
    public void TheWidthsAreLogicalUnderEveryUnitSystem(string units)
    {
        (FakeDataWindowHost host, ContextMenuModel service) = ArrangeSiblingColumn("char(100)");
        host.SetDescribe("DataWindow.Units", units);
        _ = host.AddRow(0m, "hc", "深圳A");
        _ = host.AddRow(0m, "hc", "ABCD");

        ColumnAutoWidthPlan plan = service.ColumnAutoWidth("host_col");
        ColumnWidthComputation computation = Assert.Single(plan.Columns);
        TextMeasurementCandidate candidate = Assert.Single(computation.HeadingCandidates);

        // The unit token travels as the RAW Describe answer, so the deferred half can pick its arm.
        Assert.Equal(units, plan.Units, StringComparer.Ordinal);

        // AND NOTHING ELSE MOVED. Identical values to the mixed-script theory row above, unit system
        // notwithstanding.
        Assert.Equal(3, candidate.AsciiCount);
        Assert.Equal(1, candidate.WideCount);
        Assert.Equal("AAA国", candidate.Text, StringComparer.Ordinal);
        Assert.Equal(ContextMenuModel.WidthPadding, computation.Padding);
        Assert.Equal(0d, computation.ArrowButtonWidth);

        // NO CONVERTED WIDTH IS PUBLISHED AT ALL - the plan carries ingredients, never a result. Were a
        // converted total present it would have to appear as a member, and this is where it would show.
        Assert.DoesNotContain(
            "Converted",
            string.Join(
                ',',
                typeof(ColumnWidthComputation)
                    .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Select(property => property.Name)),
            StringComparison.Ordinal);
    }


    // ==============================================================================================
    //  THE TEN DIALOGS: COMPLETENESS, BOTH SPRINTF GRAMMARS, AND THE LOCALIZATION CONTRAST
    //  --------------------------------------------------------------------------------------------
    //  AAP 0.2.1.3 Correction 5 converts every live MessageBoxEx into a structured error, "only the
    //  delivery channel changes". A sweep of the oracle finds EXACTLY TEN, and the AAP names six of
    //  them - the remaining four are located here so nobody has to sweep again:
    //
    //      :L795   修改数据被拒绝    CheckColumn's item-change refusal          named in the AAP
    //      :L863   修改数据被拒绝    RevertCheckColumn's refusal                named in the AAP
    //      :L916   修改数据被拒绝    UncheckColumn's refusal                    named in the AAP
    //      :L1009  无效的值          the paste's enumerated-value rejection     named in the AAP
    //      :L1018  数据类型不匹配    the paste's long arm                       named in the AAP
    //      :L1027  数据类型不匹配    the paste's decimal arm                    named in the AAP
    //      :L1033  数据类型不匹配    the paste's datetime arm                   NOT named - located here
    //      :L1039  数据类型不匹配    the paste's date arm                       NOT named - located here
    //      :L1045  数据类型不匹配    the paste's time arm                       NOT named - located here
    //      :L1052  修改数据被拒绝    the paste's item-change refusal            NOT named - located here
    //
    //  ALL TEN ROUTE THROUGH I18N(ne_cst_i18n.CAT_DWSVC, ...), and that is the property that separates
    //  them from the twenty-eight hardcoded Chinese messages in n_cst_dwsvc_columnexp.sru, which do NOT.
    //  The suite proves the positive case here with a live translating provider;
    //  StructuredErrorParityTests.cs owns the cross-service comparison.
    //
    //  ALL TEN ALSO COMPOSE THEIR FIRST LINE WITH Sprintf, and this service uses BOTH of that
    //  function's grammars - the index-omitted `{}` for the row number and the explicit `{1}` for the
    //  three width-plan Find predicates. Both are asserted, with emitted text, below.
    // ==============================================================================================

    /// <summary>
    /// Drives every one of the ten sites and returns the locator each reported, in the order they were
    /// provoked.
    /// </summary>
    /// <returns>The ten locators.</returns>
    /// <remarks>
    /// EACH SITE IS PROVOKED THROUGH ITS OWN PUBLIC ENTRY POINT, never by constructing an error - so a
    /// site that became unreachable would show up as a missing locator rather than as a still-passing
    /// unit test over a record constructor.
    /// </remarks>
    private static List<string> ProvokeEveryConvertedDialog()
    {
        List<string> locators = [];

        // THE FOUR CHANGE-REFUSED SITES. All four need a host that VETOES the item change, and a row
        // that genuinely needs writing - a row already holding the target value is skipped and no error
        // is raised [:L786-L793].
        (FakeDataWindowHost host, ContextMenuModel service) = ArrangeCheckBoxColumn();
        host.DoItemChangeHandler = (_, _, _) => 1L;

        Assert.Equal(RetCode.FAILED, service.CheckColumn(CheckColumn));
        locators.Add(Assert.IsType<ContextMenuError>(service.PendingError).SourceLocator);

        Assert.Equal(RetCode.FAILED, service.RevertCheckColumn(CheckColumn));
        locators.Add(Assert.IsType<ContextMenuError>(service.PendingError).SourceLocator);

        _ = host.SetItem(1L, CheckColumn, "Y");
        Assert.Equal(RetCode.FAILED, service.UncheckColumn(CheckColumn));
        locators.Add(Assert.IsType<ContextMenuError>(service.PendingError).SourceLocator);

        _ = host.SetItem(1L, CheckColumn, "N");
        Assert.Equal(RetCode.FAILED, service.Paste2Column(CheckColumn, "Y"));
        locators.Add(Assert.IsType<ContextMenuError>(service.PendingError).SourceLocator);

        // THE INVALID-VALUE SITE - a pasted text that is not a key of an ENUMERATED column.
        (_, ContextMenuModel enumerated) = ArrangeEnumeratedColumn();

        Assert.Equal(RetCode.E_INVALID_ARGUMENT, enumerated.Paste2Column("grade", "Z"));
        locators.Add(Assert.IsType<ContextMenuError>(enumerated.PendingError).SourceLocator);

        // THE FIVE TYPE-MISMATCH SITES, one per typed paste arm.
        foreach (string colType in new[] { "long", "decimal(2)", "datetime", "date", "time" })
        {
            (_, ContextMenuModel typed) = ArrangeTypedColumn(colType);

            Assert.Equal(RetCode.E_INVALID_ARGUMENT, typed.Paste2Column("typed", "abc"));
            locators.Add(Assert.IsType<ContextMenuError>(typed.PendingError).SourceLocator);
        }

        return locators;
    }

    /// <summary>
    /// EXACTLY TEN sites, and they are exactly the oracle's ten - no site elided, no site invented.
    /// </summary>
    /// <remarks>
    /// THE SET IS ASSERTED IN BOTH DIRECTIONS. A subset check would pass if a site were dropped and an
    /// "all present" check would pass if a locator were duplicated, so the assertion is on the exact
    /// distinct set AND on the count of provocations. Every locator is a fully qualified
    /// <c>file:line</c> pair rather than a bare line number, because the four attached services all
    /// have their own <c>:L795</c>.
    /// </remarks>
    [Fact]
    public void TheTenConvertedDialogsAreExactlyTheOraclesTen()
    {
        string[] expected =
        [
            "n_cst_dwsvc_contextmenu.sru:L795",
            "n_cst_dwsvc_contextmenu.sru:L863",
            "n_cst_dwsvc_contextmenu.sru:L916",
            "n_cst_dwsvc_contextmenu.sru:L1009",
            "n_cst_dwsvc_contextmenu.sru:L1018",
            "n_cst_dwsvc_contextmenu.sru:L1027",
            "n_cst_dwsvc_contextmenu.sru:L1033",
            "n_cst_dwsvc_contextmenu.sru:L1039",
            "n_cst_dwsvc_contextmenu.sru:L1045",
            "n_cst_dwsvc_contextmenu.sru:L1052",
        ];

        List<string> observed = ProvokeEveryConvertedDialog();

        Assert.Equal(10, observed.Count);
        Assert.Equal(
            expected.OrderBy(locator => locator, StringComparer.Ordinal),
            observed.Distinct(StringComparer.Ordinal).OrderBy(locator => locator, StringComparer.Ordinal));
    }

    /// <summary>
    /// Every one of the ten carries the four fields Correction 5 requires: the exact text, the
    /// <c>CAT_DWSVC</c> category read from <see cref="Categories"/>, the <c>Sprintf</c> argument as
    /// DATA, and the legacy <c>StopSign!</c> severity.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE CATEGORY IS COMPARED AGAINST <see cref="Categories.CAT_DWSVC"/> AND NEVER AGAINST A NUMBER.
    /// Its value is a COMPUTED OFFSET - <c>ne_cst_i18n.sru:L17</c> declares it relative to
    /// <c>I18N_CAT_CUSTOM</c> - so a literal here would pin the arithmetic's current result rather than
    /// the identifier, and would silently stop tracking the base if it ever moved.
    /// </para>
    /// <para>
    /// THE ROW TRAVELS AS DATA AS WELL AS PRE-RENDERED. <see cref="ContextMenuError.Row"/> carries the
    /// <c>Sprintf</c> argument itself and <see cref="ContextMenuError.RowFormatTemplate"/> carries the
    /// format, so a consumer that wants to re-render in another locale can, which is the whole point of
    /// converting a dialog into a structured result instead of a formatted string.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryConvertedDialogCarriesTheCategoryTheArgumentAndTheSeverity()
    {
        // Re-provoked here rather than shared, because PendingError is per-service state and the
        // completeness test above deliberately reads only the locator.
        (FakeDataWindowHost host, ContextMenuModel service) = ArrangeCheckBoxColumn();
        host.DoItemChangeHandler = (_, _, _) => 1L;

        List<ContextMenuError> errors = [];

        Assert.Equal(RetCode.FAILED, service.CheckColumn(CheckColumn));
        errors.Add(Assert.IsType<ContextMenuError>(service.PendingError));

        (_, ContextMenuModel enumerated) = ArrangeEnumeratedColumn();
        Assert.Equal(RetCode.E_INVALID_ARGUMENT, enumerated.Paste2Column("grade", "Z"));
        errors.Add(Assert.IsType<ContextMenuError>(enumerated.PendingError));

        (_, ContextMenuModel typed) = ArrangeTypedColumn("long");
        Assert.Equal(RetCode.E_INVALID_ARGUMENT, typed.Paste2Column("typed", "abc"));
        errors.Add(Assert.IsType<ContextMenuError>(typed.PendingError));

        Assert.Equal(3, errors.Count);

        Assert.All(
            errors,
            error =>
            {
                Assert.Equal(Categories.CAT_DWSVC, error.LocalizationCategory);
                Assert.Equal(ContextMenuMessageIcon.StopSign, error.Icon);
                Assert.True(error.Localized);
                Assert.Equal(ContextMenuModel.RowNumberMessageKey, error.RowFormatTemplate, StringComparer.Ordinal);
                Assert.Equal(1L, error.Row);
                Assert.NotEqual(ContextMenuErrorKind.None, error.Kind);
                Assert.StartsWith("n_cst_dwsvc_contextmenu.sru:L", error.SourceLocator, StringComparison.Ordinal);

                // THE FIRST LINE IS THE Sprintf RESULT, recomputed here from the template and the
                // argument the error itself carries - so the composition is asserted, not just the text.
                Assert.StartsWith(
                    Formatting.Sprintf(error.RowFormatTemplate, error.Row),
                    error.Text,
                    StringComparison.Ordinal);
            });

        // The three DISTINCT detail keys, so this is not three readings of one path.
        Assert.Equal(
            new[]
            {
                ContextMenuModel.ChangeRejectedMessageKey,
                ContextMenuModel.InvalidValueMessageKey,
                ContextMenuModel.TypeMismatchMessageKey,
            },
            errors.Select(error => error.DetailMessageKey));
    }

    /// <summary>
    /// Both <c>Sprintf</c> grammars, with the emitted text asserted for one of each: the index-omitted
    /// <c>{}</c> that composes the row fragment [<c>:L795</c>] and the explicit <c>{1}</c> that builds
    /// the width plan's Find predicate [<c>:L1186</c>].
    /// </summary>
    /// <remarks>
    /// <para>
    /// ONE FUNCTION, TWO GRAMMARS, AND THE SERVICE USES BOTH. <c>Formatting.Sprintf</c> takes the next
    /// argument from a sequential cursor for a brace pair with no index and the n-th argument for
    /// <c>{n}</c>, one-based. The oracle mixes them across the file - <c>{}</c> at the ten dialog sites
    /// and <c>{1}</c> at <c>:L1183</c>, <c>:L1186</c>, <c>:L1188</c>, <c>:L1349</c>, <c>:L1352</c> and
    /// <c>:L1354</c> - so a port that implemented only one grammar would pass a test over the other and
    /// still be wrong. Both are exercised here against the SERVICE'S OWN emitted text rather than
    /// against <c>Sprintf</c> in isolation.
    /// </para>
    /// <para>
    /// THE INDEXED FORM SUBSTITUTES THE SAME ARGUMENT FOUR TIMES in this predicate, which is exactly
    /// what an explicit index is for and what a sequential cursor could not express with one argument.
    /// That asymmetry is why the oracle reaches for the two forms in different places.
    /// </para>
    /// </remarks>
    [Fact]
    public void BothSprintfGrammarsAreExercisedAndTheirEmittedTextIsExact()
    {
        // ---- GRAMMAR 1: THE INDEX-OMITTED `{}` -------------------------------------------------
        // The template is the service's own key, and the emitted text is asserted whole.
        Assert.Equal("第{}行", ContextMenuModel.RowNumberMessageKey, StringComparer.Ordinal);
        Assert.Equal(
            "第1行",
            Formatting.Sprintf(ContextMenuModel.RowNumberMessageKey, 1L),
            StringComparer.Ordinal);

        (FakeDataWindowHost host, ContextMenuModel service) = ArrangeCheckBoxColumn();
        host.DoItemChangeHandler = (_, _, _) => 1L;

        Assert.Equal(RetCode.FAILED, service.CheckColumn(CheckColumn));

        ContextMenuError error = Assert.IsType<ContextMenuError>(service.PendingError);

        // The service's whole first line IS the sequential-grammar result.
        Assert.Equal(
            Formatting.Sprintf(ContextMenuModel.RowNumberMessageKey, 1L)
                + ContextMenuModel.LineSeparator
                + ContextMenuModel.ChangeRejectedMessageKey
                + ContextMenuModel.DetailSuffix,
            error.Text,
            StringComparer.Ordinal);
        Assert.Equal("第1行\n修改数据被拒绝!", error.Text, StringComparer.Ordinal);

        // ---- GRAMMAR 2: THE EXPLICIT `{1}`, SUBSTITUTED FOUR TIMES -----------------------------
        const string IndexedTemplate = "if(IsNull({1}),'',{1}) <> if(IsNull({1}[1]),'',{1}[1])";

        string expectedPredicate = Formatting.Sprintf(IndexedTemplate, "typed");

        Assert.Equal(
            "if(IsNull(typed),'',typed) <> if(IsNull(typed[1]),'',typed[1])",
            expectedPredicate,
            StringComparer.Ordinal);

        (FakeDataWindowHost widthHost, ContextMenuModel widthService) = ArrangeTypedColumn("char(10)");
        List<string> predicates = [];
        widthHost.FindHandler = (expression, _, _) =>
        {
            predicates.Add(expression);
            return 0L;
        };

        _ = widthService.ColumnAutoWidth("typed");

        // The service emits exactly what the indexed grammar produces.
        Assert.Equal(expectedPredicate, Assert.Single(predicates), StringComparer.Ordinal);
    }

    /// <summary>
    /// The ten messages DO route through <see cref="I18n"/> - proved with a live translating provider
    /// whose translations then appear in the emitted text.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THIS IS THE CONTRAST, AND IT IS THE POINT OF THE TEST. AAP 0.2.1.3 Correction 5 records that the
    /// twenty-eight column-expression messages are hardcoded Chinese that do NOT reach <c>I18N</c>,
    /// while these ten do - and that the inconsistency is legacy behaviour to reproduce rather than
    /// harmonise. Asserting only the untranslated text, as the rest of this suite does, cannot tell the
    /// two apart: with no provider installed the silent-passthrough fallback returns the source string
    /// unchanged, so a service that never called <c>I18N</c> at all would produce identical output.
    /// Installing a provider that CHANGES the text is what makes the call observable.
    /// </para>
    /// <para>
    /// THE DOUBLE RECORDS EVERY REQUEST BEFORE DECIDING WHETHER TO HANDLE IT, so the request log
    /// distinguishes "never asked" from "asked and declined" - and the requests are asserted as well as
    /// the output, because a lookup whose result was discarded would still be a lookup.
    /// </para>
    /// <para>
    /// TRANSLATE FIRST, THEN FORMAT. The row template is translated and the RESULT is handed to
    /// <c>Sprintf</c> [<c>:L795</c>], never the other way round - so a translation may legitimately move
    /// the brace pair, and this test proves it by teaching a template whose <c>{}</c> sits at the END
    /// rather than in the middle.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheTenMessagesRouteThroughLocalization()
    {
        ScriptedI18nProvider provider = new();
        _ = provider.Teach(ContextMenuModel.RowNumberMessageKey, "row {}");
        _ = provider.Teach(ContextMenuModel.ChangeRejectedMessageKey, "change rejected");
        _ = provider.Teach(ContextMenuModel.InvalidValueMessageKey, "invalid value");
        _ = provider.Teach(ContextMenuModel.TypeMismatchMessageKey, "type mismatch");

        I18n facade = ScriptedLocalization.With(provider);

        // ---- THE CHANGE-REFUSED SHAPE -----------------------------------------------------------
        (FakeDataWindowHost host, ContextMenuModel service) = ArrangeCheckBoxColumn(i18n: facade);
        host.DoItemChangeHandler = (_, _, _) => 1L;

        Assert.Equal(RetCode.FAILED, service.CheckColumn(CheckColumn));

        ContextMenuError refused = Assert.IsType<ContextMenuError>(service.PendingError);

        // THE TRANSLATED TEXT APPEARS, and the brace pair was expanded AFTER translation - "row 1", not
        // "row {}" and not "第1行".
        Assert.Equal("row 1\nchange rejected!", refused.Text, StringComparer.Ordinal);

        // THE KEYS ARE STILL THE SOURCE STRINGS, untranslated, so a consumer can re-render.
        Assert.Equal(ContextMenuModel.RowNumberMessageKey, refused.RowFormatTemplate, StringComparer.Ordinal);
        Assert.Equal(
            ContextMenuModel.ChangeRejectedMessageKey,
            refused.DetailMessageKey,
            StringComparer.Ordinal);

        // ---- THE INVALID-VALUE SHAPE ------------------------------------------------------------
        (_, ContextMenuModel enumerated) = ArrangeEnumeratedColumn(facade);

        Assert.Equal(RetCode.E_INVALID_ARGUMENT, enumerated.Paste2Column("grade", "Z"));
        Assert.Equal(
            "row 1\ninvalid value!\nZ",
            Assert.IsType<ContextMenuError>(enumerated.PendingError).Text,
            StringComparer.Ordinal);

        // ---- THE TYPE-MISMATCH SHAPE ------------------------------------------------------------
        (_, ContextMenuModel typed) = ArrangeTypedColumn("long", facade);

        Assert.Equal(RetCode.E_INVALID_ARGUMENT, typed.Paste2Column("typed", "abc"));
        Assert.Equal(
            "row 1\ntype mismatch!\nabc",
            Assert.IsType<ContextMenuError>(typed.PendingError).Text,
            StringComparer.Ordinal);

        // ---- AND THE PROVIDER WAS GENUINELY CONSULTED, WITH THE RIGHT CATEGORY -------------------
        Assert.NotEmpty(provider.Requests);
        Assert.All(provider.Requests, request => Assert.Equal(Categories.CAT_DWSVC, request.Category));

        Assert.Contains(ContextMenuModel.RowNumberMessageKey, provider.RequestedKeys);
        Assert.Contains(ContextMenuModel.ChangeRejectedMessageKey, provider.RequestedKeys);
        Assert.Contains(ContextMenuModel.InvalidValueMessageKey, provider.RequestedKeys);
        Assert.Contains(ContextMenuModel.TypeMismatchMessageKey, provider.RequestedKeys);
    }


    // ==============================================================================================
    //  THE ORDERED MAP SEAM                                     AAP 0.2.1.3 CORRECTION 2 / :L986-L988
    //  --------------------------------------------------------------------------------------------
    //  Correction 2 splits pfw.utility.container: n_map is in scope as a SHARED container because BOTH
    //  in-scope services reach it, and this service is one of the two - `n_cst_dwsvc_contextmenu.sru`
    //  :L948 calls `_of_GetColumnValueMap`, whose base implementation is `n_cst_dwsvc.sru:L561-L664`
    //  and whose ported form is Domain/DataWindowServiceHost.cs's GetColumnValueMap. The paste path
    //  consults it at :L986-L988 and :L1006-L1007.
    //
    //  WHY THE TYPE MATTERS AND A Dictionary WOULD NOT DO. n_map's contract is ORDERED: `get(int)` and
    //  `getkey(int)` address entries POSITIONALLY, one-based, in insertion order. A .NET Dictionary
    //  enumerates in an unspecified order and has no positional accessor at all, so substituting one
    //  here would silently drop a capability the rest of the layer relies on - which is exactly the kind
    //  of narrowing constraint C-B forbids.
    // ==============================================================================================

    /// <summary>
    /// The column-value map really is <see cref="OrderedMap"/>, and the seam that produces it is the
    /// base helper this service calls [<c>:L948</c>].
    /// </summary>
    /// <remarks>
    /// ASSERTED AT THE SEAM, NOT AT THE CALL SITE. <c>GetColumnValueMap</c> is <c>protected</c> on
    /// <c>DataWindowServiceBase</c> - which is where the oracle puts it too, since
    /// <c>_of_GetColumnValueMap</c> is a private function on <c>n_cst_dwsvc</c>, the base every DataWindow
    /// service derives from - so the test reflects the declared return type rather than asking the port to
    /// publish it. Its BEHAVIOUR through the paste path is asserted separately by
    /// <see cref="AKnownDisplayValueIsTranslatedToItsDataValue"/>.
    /// </remarks>
    [Fact]
    public void TheColumnValueMapIsAnOrderedMap()
    {
        MethodInfo seam = Assert.IsType<MethodInfo>(
            typeof(DataWindowServiceBase).GetMethod(
                "GetColumnValueMap",
                BindingFlags.NonPublic | BindingFlags.Instance),
            exactMatch: false);

        Assert.Equal(typeof(OrderedMap), seam.ReturnType);

        // ONE PARAMETER, THE COLUMN NAME, passed `in` exactly as the oracle declares `readonly string`.
        ParameterInfo parameter = Assert.Single(seam.GetParameters());

        Assert.Equal(typeof(string), parameter.ParameterType.GetElementType() ?? parameter.ParameterType);
        Assert.True(parameter.ParameterType.IsByRef, "the oracle declares it `readonly string`.");
    }

    /// <summary>
    /// The map the seam actually builds for an enumerated column preserves the code table's INSERTION
    /// ORDER, and its positional accessors are ONE-BASED [<c>n_cst_dwsvc.sru:L561-L664</c>].
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE MAP IS OBTAINED FROM THE LIVE SEAM, not constructed by the test, so the order asserted is the
    /// order the service will see, and it is invoked on the SERVICE instance, which is what proves the
    /// inherited member is reachable from this service rather than merely declared somewhere. The code
    /// table is seeded 优 then 良 by
    /// <see cref="ArrangeEnumeratedColumn"/>, mirroring the fixture's own
    /// <c>values="新建 NEW/确认 CFD/审核 ADT/"</c> ordering at
    /// <c>dw_test_dwsvc_contextmenu.srd:L11</c> - a code table is an ORDERED list in the DataWindow
    /// syntax, and the display order is the order the entries are written.
    /// </para>
    /// <para>
    /// ONE-BASED WITH AN INCLUSIVE UPPER BOUND OF <c>Count()</c>, which is AAP 0.4.5.4's named hazard.
    /// Both ends are asserted - index 0 and index <c>Count() + 1</c> answer the out-of-range default
    /// rather than throwing, matching PowerScript, where an out-of-range read is not an exception.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheColumnValueMapPreservesInsertionOrderAndIsOneBased()
    {
        (_, ContextMenuModel service) = ArrangeEnumeratedColumn();

        MethodInfo seam = Assert.IsType<MethodInfo>(
            typeof(DataWindowServiceBase).GetMethod(
                "GetColumnValueMap",
                BindingFlags.NonPublic | BindingFlags.Instance),
            exactMatch: false);

        OrderedMap map = Assert.IsType<OrderedMap>(seam.Invoke(service, ["grade"]));

        Assert.Equal(2UL, map.Count());

        // BY KEY - the display-to-data translation the paste path uses.
        Assert.Equal("A", map.Get("优"));
        Assert.Equal("B", map.Get("良"));

        // POSITIONALLY, ONE-BASED, IN INSERTION ORDER.
        Assert.Equal("优", map.GetKey(1), StringComparer.Ordinal);
        Assert.Equal("良", map.GetKey(2), StringComparer.Ordinal);
        Assert.Equal("A", map.Get(1));
        Assert.Equal("B", map.Get(2));

        // BOTH ENDS OF THE RANGE, and neither throws - index 0 does not exist and Count()+1 is past the
        // last entry.
        Assert.Null(map.Get(0));
        Assert.Equal(string.Empty, map.GetKey(0));
        Assert.Null(map.Get(3));
        Assert.Equal(string.Empty, map.GetKey(3));
    }

    // ==============================================================================================
    //  THE INIT-BEFORE-SELECTION DATA DEPENDENCY                       :L147 versus :L194 / PATTERN (b)
    //  --------------------------------------------------------------------------------------------
    //  AAP 0.6.1.4 assigns the context-menu pair pattern (b), strictly synchronous with no reordering,
    //  and gives the reason in one line: "Initialization must complete before the menu identifier passed
    //  to the second event can be meaningful". The oracle makes that a straight-line dependency inside
    //  one event - `#DataWindow.Event OnInitContextMenu(row,dwo)` at :L147 populates the store, the
    //  popup runs at :L189, and `#DataWindow.Event OnContextMenu(row,dwo,rtCode)` at :L194 receives the
    //  identifier the popup answered.
    //
    //  ACROSS A BOUNDARY THAT ONE EVENT BECOMES TWO CALLS, because a headless service cannot show a menu
    //  and block: BuildMenu returns the model and ApplySelection receives the choice. The ORDERING of
    //  the two is EventOrderingPatternTests.cs's subject. What is asserted HERE is the DATA DEPENDENCY
    //  that makes the ordering necessary - that a mid is only meaningful because initialization put it
    //  in the model first.
    // ==============================================================================================

    /// <summary>
    /// An identifier is meaningful to <see cref="ContextMenuModel.ApplySelection"/> only because
    /// initialization put it in the model first [<c>:L147</c> before <c>:L194</c>].
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE SAME CALL, THE SAME IDENTIFIER, TWO DIFFERENT OUTCOMES - and the only difference is whether
    /// initialization ran. That is what "the mid is not meaningful until oninitcontextmenu completes"
    /// means operationally, and it is why the pair cannot be reordered or run concurrently.
    /// </para>
    /// <para>
    /// THE HOST'S INIT HANDLER IS WHERE AN APPLICATION ADDS ITS OWN ITEMS, so the identifier used here is
    /// an APPLICATION identifier below <c>MID_RESERVED</c> rather than one of the nine - which is the
    /// case that matters, since a framework identifier would dispatch on its own arm regardless of what
    /// the model holds.
    /// </para>
    /// </remarks>
    [Fact]
    public void AnIdentifierIsOnlyMeaningfulAfterInitializationHasRun()
    {
        const uint ApplicationId = 42u;

        (FakeDataWindowHost host, ContextMenuModel service) = NewService();
        _ = host.AddRow(1.00m);

        // The application adds its item during initialization, exactly as :L147 invites it to.
        host.InitContextMenuHandler = (_, _) =>
        {
            _ = service.AddMenu("application item", string.Empty, ApplicationId);
            return 0L;
        };

        List<long> claimed = [];
        host.ContextMenuHandler = (_, _, mid) =>
        {
            claimed.Add(mid);
            return 1L;
        };

        // ---- WITHOUT INITIALIZATION: the store is empty, so the identifier names nothing ----------
        Assert.Equal(0, service.GetCount());
        Assert.Null(service.LastLayout);

        // ---- WITH INITIALIZATION: the identifier is in the built model ---------------------------
        ContextMenuLayout layout = service.BuildMenu(0L, Dwo(HeaderObject, "text"), Pointer("header\t1"));

        Assert.Equal(ContextMenuOutcome.Menu, layout.Outcome);
        Assert.Contains(ApplicationId, IdsOf(layout.Items));
        Assert.True(layout.AwaitingSelection);

        // ...and only now does applying it reach the host's semantic event with that identifier.
        Assert.Equal(RetCode.OK, service.ApplySelection(0L, Dwo(HeaderObject, "text"), ApplicationId));
        Assert.Equal([(long)ApplicationId], claimed);
    }

    /// <summary>
    /// A selection for an identifier that was never in the built model is a DEFINED outcome - the
    /// no-matching-arm exit of <c>event ondefproc</c>, not an exception and not a guess
    /// [<c>:L196</c>, <c>:L204-L222</c>].
    /// </summary>
    /// <remarks>
    /// <para>
    /// DEFINED AS "NOTHING HAPPENED, SUCCESSFULLY". The oracle's dispatch is a <c>choose case</c> over
    /// the nine identifiers with no <c>case else</c>, so an unrecognised value falls out of the bottom
    /// and the event returns having done nothing. Reproduced verbatim: <c>RetCode.OK</c>, no host call,
    /// and none of the three pending results set - and specifically NOT an error, because the oracle has
    /// no error to raise here and inventing one would break an application that adds its own identifiers
    /// and handles them in <c>OnContextMenu</c> without the framework knowing.
    /// </para>
    /// <para>
    /// THE CLEANUP STILL RUNS, which is the observable half that would break if the unknown identifier
    /// were allowed to short-circuit: the oracle's <c>finally</c> at <c>:L197-L201</c> clears the clicked
    /// column, the captured clipboard text and the whole item store on EVERY exit. So after an unknown
    /// selection the model is empty and a second <c>ApplySelection</c> has nothing to find - asserted
    /// below, because a port that skipped the cleanup would leave the store alive across right-clicks and
    /// accumulate duplicate items.
    /// </para>
    /// </remarks>
    [Fact]
    public void ASelectionForAnIdentifierNeverInTheModelIsDefinedAndStillCleansUp()
    {
        const long NeverAdded = 999L;

        (FakeDataWindowHost host, ContextMenuModel service) = NewService();
        _ = host.AddRow(1.00m);

        ContextMenuLayout layout = service.BuildMenu(0L, Dwo(HeaderObject, "text"), Pointer("header\t1"));

        Assert.Equal(ContextMenuOutcome.Menu, layout.Outcome);
        Assert.DoesNotContain((uint)NeverAdded, IdsOf(layout.Items));

        host.CallLog.Clear();

        Assert.Equal(RetCode.OK, service.ApplySelection(0L, Dwo(HeaderObject, "text"), NeverAdded));

        // NOTHING WAS ATTEMPTED beyond the host's own veto event, and no result was produced.
        Assert.Equal(["Event OnContextMenu"], host.CallLog.Members);
        Assert.Null(service.PendingError);
        Assert.Null(service.PendingCopiedText);
        Assert.Null(service.PendingWidthPlan);

        // AND THE CLEANUP RAN, so the store is empty and the layout is released.
        Assert.Equal(0, service.GetCount());
        Assert.Null(service.LastLayout);
    }
}
