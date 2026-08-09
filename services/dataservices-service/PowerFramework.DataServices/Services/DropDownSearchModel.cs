// ==============================================================================================
//  DropDownSearchModel - the managed port of the HEADLESS HALF of the DataWindow DROP-DOWN SEARCH
//  service: incremental filtering of a drop-down DataWindow as the user types, plus autocomplete.
//  --------------------------------------------------------------------------------------------
//  PORTED FROM
//      ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_dropdownsearch.sru   (516 lines)
//
//  READ AS REFERENCE, NOT PORTED HERE
//      n_cst_dwsvc.sru        (864 lines) the common service base - ported to
//                             Domain/DataWindowServiceHost.cs as DataWindowServiceBase, including
//                             the STYLE_*/COL_TYPE_* catalogues and the two protected helpers this
//                             file consumes: _of_getstyle [:L46] as GetPresentationStyle and
//                             _of_getdwobject [:L44] as GetDataWindowObject.
//      se_cst_dw.sru          (616 lines) the host. Supplies the three broker topics this service
//                             subscribes [:L52, :L74, :L76], the two semantic events it RAISES back
//                             at its host [:L13 onddsgetfilter, :L28 onddsfiltered], the direct
//                             OnEditChanged dispatch that stands in for the commented-out
//                             subscription [:L169-L171], and the creation and attachment order
//                             [:L573, :L578, :L587].
//      pinyinfirstletterlike.srf   the two `system library "pfw.dll"` prototypes behind the
//                             expression function this file NAMES AS TEXT. See THE PINYIN COUPLING.
//      retcode.sru            the return-code algebra, ported to Shared.Kernel RetCode. This file
//                             consumes RetCode.OK and RetCode.FAILED and declares neither.
//      enums.sru:L1147-L1149  PY_LIKE_IGNORE_CASE / IGNORE_WIDTH / FUZZY_SOUND, already ported to
//                             Shared.Kernel Enums and already summed into
//                             DataServicesOptions.DropDownSearch.PinyinMatchFlags. NOT referenced
//                             here - the flag value arrives through options.
//      replaceall.srf         the three `native "pfw.dll"` arities, ported to Shared.Kernel
//                             Text.ReplaceAll. This file consumes the FOUR-ARGUMENT match-case
//                             middle arity, which is the one the oracle calls at :L317.
//      bittest.srf            the `native "pfw.dll"` bit test, ported to Shared.Kernel Bits.BitTest.
//
//  ORACLE STATUS  Every `ws_objects/**` path above is READ ONLY (constraint C-C) and is the
//                 behavioural oracle for parity testing. Nothing else in the repository can
//                 adjudicate behaviour (AAP 0.1.4), so every behaviour reproduced below carries the
//                 line locator it came from. Bare `:Lnnn` locators refer to
//                 n_cst_dwsvc_dropdownsearch.sru; any other file is named.
//
//  ============================================================================================
//  THE SPLIT - THIS FILE IS ONE HALF OF n_cst_dwsvc_dropdownsearch.sru, THE OTHER IS DEFERRED
//  ============================================================================================
//  AAP 0.2.1.3 Correction 4 measured the five attached DataWindow services for presentational
//  dependencies and found this one IRREDUCIBLY PRESENTATIONAL IN PART, on the evidence of its
//  type-position use of four window-management calls: `Win32.ShowWindow` [:L256],
//  `Win32.GetWindowRect` [:L474], `Win32.OffsetRect` [:L475] and `Win32.SetWindowPos` [:L489].
//  AAP 0.4.2.5 states the ruling for this file in one line - "Headless half only - filter
//  construction and search state. Window positioning and IME input are deferred."
//
//  WHAT SHIPS HERE. The whole filter builder; the filter composition and apply path including both
//  semantic events; the edit-context state machine; the autocomplete prefix search over both edit
//  styles; the show-filtered-rows auto-determination and the buffer relocation it gates; HasFilter;
//  SetFilterType; SetShowFilteredRows; and all three UpdateDddwFilter arities.
//
//  WHAT IS DEFERRED, ITEM BY ITEM, EACH WITH THE REASON IT CANNOT CROSS THE BOUNDARY (C-K):
//      * :L251-L258  `_of_showdddw()` IN FULL - it exists only to make an OS window appear:
//                    a zero-handle early-out, a call to the height adjuster, an `IsWindowVisible`
//                    guard and `Win32.ShowWindow(hWnd,8) //SW_SHOWNA` [:L256]. There is no data in
//                    it. Its call site at :L413-L415 is annotated in ApplyFilter.
//      * :L461-L493  `_of_adjustdddwheight(name)` IN FULL - `Handle(dwc)` [:L469],
//                    `Win32.GetWindowRect` [:L474], `Win32.OffsetRect` [:L475], the four-term
//                    `UnitsToPixels(..., YUnitsToPixels!)` sum [:L480-L484], the `nWndHeight < 50`
//                    pixel floor [:L485] and `SWP_NOZORDER + SWP_NOMOVE + SWP_NOACTIVATE` with
//                    `Win32.SetWindowPos` [:L488-L489]. Every line is pixel geometry or a window
//                    handle, which AAP 0.4.4 assigns to DesignSystem.
//      * :L41-L44    the private external prototype
//                    `function boolean IsWindowVisible(ulong hwnd) library "user32.dll"`. AAP 0.6.5
//                    places the window and input-method prototypes OUT OF SCOPE as presentational,
//                    so it is NOT declared here in any form. Its two guards, at :L255 and :L385, are
//                    annotated where they would have stood.
//      * :L171, :L214, :L469  `Handle(...)` - obtaining an OS window handle for a DataWindowChild.
//                    Nothing headless can produce one and nothing headless may branch on one; see
//                    DddwData.Hwnd.
//      * :L399, :L404  the two `SetDetailHeight(...)` calls - ROW GEOMETRY. The relocation they
//                    bracket is a buffer operation and ships; the heights do not. See DECISION 3.
//      * :L128-L129  `#DataWindow.SetText(sValDisp)` and
//                    `#DataWindow.SelectText(nLenData + 1,Len(sValDisp))` - mutating a live edit
//                    control and its selection, which is the text-input/IME half. See DECISION 2.
//
//  HOW THE GAP IS SURFACED RATHER THAN DROPPED. Per AAP 0.8.1, where the choice is between a
//  partial implementation and a documented gap the DOCUMENTED GAP WINS. So each deferred item is
//  replaced by the DATA it would have consumed, published on this type:
//  DropDownSearchCompletion carries what SetText/SelectText would have applied, and
//  DropDownSearchFilterPartition carries the row ranges and heights the two SetDetailHeight calls
//  would have applied. The eventual consumer is the reserved Gateway extension point
//  `/v1/design/**` (AAP 0.4.4), which is where the rendering half will live. Nothing is silently
//  omitted and nothing is approximated.
//
//  CONSTRAINT C-D IS THE DOMINANT STRUCTURAL CONSTRAINT AND IS SATISFIED BY INSPECTION AND BY
//  GREP. There is no Win32 call, no ShowWindow, GetWindowRect, OffsetRect, SetWindowPos or
//  IsWindowVisible, no SetDetailHeight, no UnitsToPixels, no SWP_ flag, no SetText or SelectText,
//  no Handle(, no user32, no font or popup-menu type, no SetPointer, no dialog, no DPI conversion -
//  and, explicitly, NO `NotImplementedException` placeholder standing in for a deferred capability
//  under any name. It is also structurally impossible to breach: PowerFramework.DataServices.csproj
//  references only Contracts, Shared.Kernel, Shared.Eventful, Shared.Localization,
//  Shared.Containers and Shared.Diagnostics, so there is no DesignSystem type to reach for.
//
//  ============================================================================================
//  DECISION 1 - THE FIVE PRESERVED DEFECTS. CONSTRAINT C-B IS THE DOMINANT BEHAVIOURAL CONSTRAINT
//               FOR THIS FILE AND IT OUTRANKS EVERY INSTINCT TO REPAIR.
//  ============================================================================================
//  Each is annotated again at the exact site that reproduces it, and each is pinned by a test that
//  MUST FAIL if someone "fixes" it:
//
//      DEFECT 1  :L318-L332  THE LEADING " OR ". Only the FILTER_DISP clause ASSIGNS [:L319];
//                every later clause APPENDS with a leading " OR " [:L323, :L331, :L334]. Disable
//                FILTER_DISP - FilterType = FILTER_DISP_PY, that is 2 - and the emitted filter is
//                "( OR PinyinFirstLetterLike(...))", a syntactically broken expression. Observable
//                and reproduced exactly.
//      DEFECT 2  :L319, :L323, :L331, :L334  UNESCAPED INTERPOLATION. The display and data column
//                names, the %-substituted search text, the lower-cased search text and the RAW
//                search text are all spliced straight into expression text. A single quote in the
//                user's input breaks or injects into the filter. AAP 0.6.4 names this exact site
//                (:L323) as a filter-injection site and requires it be DOCUMENTED rather than
//                silently changed. NOT escaped, NOT quoted, NOT sanitised, NOT parameterised.
//      DEFECT 3  :L507  THE COMMENTED-OUT SUBSCRIPTION.
//                `//#DataWindow.of_On(#DataWindow.EVT_EDITCHANGED,this,"onEditChanged")` sits inert
//                above the three live subscriptions, so OnEditChanged is NOT broker-wired and the
//                host must drive it directly - which se_cst_dw.sru:L169-L171 does. Not revived.
//      DEFECT 4  :L304-L305  NO VALIDATION IN THE FILTER-TYPE SETTER. The body is two statements.
//                Zero and out-of-range values are accepted, and zero combined with DEFECT 1 yields
//                an empty filter with no diagnostic. No guard is added and E_INVALID_ARGUMENT is
//                never returned.
//      DEFECT 5  :L438-L441  THE GUARD RETURN-CODE DIVERGENCE. Three guards return RetCode.OK and
//                the fourth returns RetCode.FAILED, for no discernible reason. Observable through
//                the public return value. Not harmonised in either direction.
//
//  CONSTRAINT C-G READ TOGETHER WITH C-B, BECAUSE THE COMBINATION IS WHERE A REVIEWER GOES WRONG.
//  DEFECT 2 is a PRESERVED LEGACY DEFECT ON AN INTERNAL DATAWINDOW FILTER EXPRESSION, documented as
//  such - it is NOT a new attack surface, and it must not be hardened. AAP 0.7.2's parameterised-SQL
//  requirement does not reach it either: these are DataWindow filter expressions, not SQL, and
//  constraint C-E makes Persistence the only service that generates or executes SQL. This file holds
//  no key, no password, no token, no connection string and no certificate.
//
//  ============================================================================================
//  DECISION 2 - THE AUTOCOMPLETE SPLIT: THE MATCH IS DATA, APPLYING IT IS NOT.
//  ============================================================================================
//  :L127-L130 is `if bMatched then SetText(sValDisp); SelectText(nLenData + 1,Len(sValDisp))`. The
//  SEARCH that produced sValDisp is a buffer scan and ships whole; the two calls that push it into
//  a live edit control and select the completed tail are the text-input half. This file therefore
//  publishes DropDownSearchCompletion carrying the matched text, the ONE-BASED selection start
//  `nLenData + 1` and the selection length `Len(sValDisp)` - the three values the deferred half
//  needs and nothing more. Nothing is dropped.
//
//  ============================================================================================
//  DECISION 3 - THE SHOW-FILTERED-ROWS SPLIT: THE DECISION AND THE PARTITION SHIP, THE HEIGHTS DO
//               NOT.
//  ============================================================================================
//  :L396-L405 does three things. `_of_IsShowFilteredRows()` DECIDES [:L396] - pure predicate logic
//  over the column names, the presentation style, the row count and a DataWindow attribute, so it
//  ships. `RowsMove(1,FilteredCount(),Filter!,object,nRowCnt + 1,Primary!)` RELOCATES [:L402] - a
//  buffer operation whose result is data, so it ships. The two `SetDetailHeight` calls [:L399,
//  :L404] CONCEAL - row geometry, so they are deferred and their arguments are published on
//  DropDownSearchFilterPartition instead.
//
//  ============================================================================================
//  DECISION 4 - THE PINYIN COUPLING IS INDIRECT, AND THAT IS A HARD RULING.
//  ============================================================================================
//  :L323 does NOT call the pinyin matcher. It EMITS THE FUNCTION NAME AS TEXT inside a filter
//  expression, which the DataWindow expression evaluator resolves when the filter is applied -
//  Expressions/PinyinFirstLetterMatcher.cs is registered as an expression-callable function by
//  Expressions/DataWindowExpressionEvaluator.cs for exactly this reason. This file therefore takes
//  NO compile-time dependency on the matcher: it does not `using` it, does not call it and does not
//  inject it. AAP risk R1 - the lookup table exists only inside the closed `pfw.dll` and the flag
//  semantics are undocumented - and the BLOCKED result path that follows from it are that file's
//  responsibility, reached through the evaluator. Neither is duplicated or approximated here.
//
//  ============================================================================================
//  DECISION 5 - THE NUMERAL COINCIDENCE: FILTER_ALL IS 7 AND THE PINYIN FLAGS ARE ALSO 7, AND THEY
//               ARE UNRELATED. A READER HAS ALREADY BEEN MISLED BY THIS.
//  ============================================================================================
//  FILTER_ALL [:L53] is FILTER_DISP + FILTER_DISP_PY + FILTER_DATA = 1 + 2 + 4 = 7, a column-set
//  bitmask. The flags literal at :L323 is also 7, and decomposes per enums.sru:L1147-L1149 as
//  PY_LIKE_IGNORE_CASE 1 + PY_LIKE_IGNORE_WIDTH 2 + PY_LIKE_FUZZY_SOUND 4 - a pronunciation-matching
//  bitmask. Same numeral, different contract, different domain. Neither is derived from the other
//  anywhere in this file: FILTER_ALL is a computed sum of the three local constants, and the flags
//  value arrives from DataServicesOptions.DropDownSearch.PinyinMatchFlags. Note also that the
//  DEFAULT FilterType IS NOT FILTER_ALL - it is FILTER_DISP + FILTER_DISP_PY, that is 3, because the
//  oracle deliberately omits the data-column bit [:L57].
//
//  ============================================================================================
//  DECISION 6 - ONE-BASED INDEXING IS PRESERVED, NOT REBASED. AAP 0.4.5.4 / RISK R9.
//  ============================================================================================
//  AAP 0.4.5.4 names one-based to zero-based translation the single most dangerous mechanical hazard
//  in this refactor, so every index in this file stays in the oracle's numbering and every site is
//  audited individually rather than run through a rebasing helper that would have to be right at
//  every call. The four sites:
//      * :L108  `for nRow = 1 to nRowCnt` over the child's rows - reproduced as
//               `for (nRow = 1; nRow <= nRowCnt; nRow++)`, INCLUSIVE, because RowCount() is the
//               LAST VALID ROW NUMBER and not a length.
//      * :L116-L117  the ddlb walk pre-increments a zero-initialised local, so the FIRST index
//               passed to GetValue is 1. Reproduced with the same zero-initialised local and the
//               same pre-increment position, inside a do-while so the first probe always happens.
//      * :L129  the selection start is `nLenData + 1` - a ONE-BASED character position, published
//               verbatim on DropDownSearchCompletion.SelectionStart and never adjusted.
//      * :L402  `RowsMove(1, ..., nRowCnt + 1, ...)` - both are one-based buffer row numbers and
//               both are passed through unchanged.
//
//  ============================================================================================
//  DECISION 7 - THE THREE LOAD-BEARING NULLS. AAP 0.4.5.4: NEVER COLLAPSE NULL TO A DEFAULT.
//  ============================================================================================
//      * #ShowFilteredRows [:L58, :L503] is THREE-STATE - true, false, and null meaning
//        "auto-determine". Ported as `bool?` with a null default, and the null gates the entire
//        three-arm resolver at :L260-L263. A plain bool would delete that algorithm.
//      * the filter argument of the three-argument UpdateDddwFilter [:L443] - null selects the
//        Describe branch that reads the child's own current filter. The one-argument overload
//        [:L308-L310] exists solely to pass that null, so it passes null and never "".
//      * the edit data at :L90 - `if IsNull(data) then data = ""`. The host passes it through from a
//        raw pbm_dwnchanging argument, so null genuinely arrives.
//
//  ============================================================================================
//  DECISION 8 - ACCESSIBILITY: THE ORACLE'S PRIVATE HELPERS ARE `internal` HERE, NOT `public`.
//  ============================================================================================
//  `_of_getfilter`, `_of_filter`, `_of_initeditcontextinfo`, `_of_initctxdddwfilter` and
//  `_of_isshowfilteredrows` are all PRIVATE in the oracle, and the public surface is exactly the six
//  members it declares public [:L70-L77 plus the four events]. Constraint C-H nonetheless requires
//  the filter builder to be testable as a pure function with no DataWindow, no child, no database
//  and no UI, so those five are `internal` rather than `private`. The widening is unobservable
//  outside the assembly, the mechanism is the `<InternalsVisibleTo Include=
//  "PowerFramework.DataServices.Tests" />` item the application project already declares, and it is
//  the same judgement the Persistence tree already applies to its own legacy-private members. The
//  public members stay public and nothing private became public.
//
//  ============================================================================================
//  DECISION 9 - WHAT IS NOT REPRODUCED, AND WHY
//  ============================================================================================
//      * :L39  `global n_cst_dwsvc_dropdownsearch n_cst_dwsvc_dropdownsearch` - a global
//        auto-instance SHADOWING ITS OWN TYPE NAME, one of the occurrences AAP 0.4.5.1 records. Per
//        that section the type keeps the descriptive .NET name and the instance becomes an ordinary
//        dependency the host owns [se_cst_dw.sru:L83, created :L573], never a global and never a
//        static mutable. Nothing in this file is static and mutable, which is also what makes it
//        unit-testable without process-wide state.
//      * :L495-L501  the create and destroy clauses - bare `call super::create` / `call
//        super::destroy` with no body of their own. C# runs base constructors implicitly, this class
//        holds no unmanaged resource and the chain disposes a service only if it implements
//        IDisposable, so there is nothing to reproduce and no disposal to implement.
//      * :L266-L282, :L287-L303, :L347-L362, :L419-L437  the banner comment blocks in four method
//        bodies. Their CONTENT - description, author, date, copyright - is carried by the file
//        header and by XML documentation; the banners themselves are PowerBuilder editor furniture.
// ==============================================================================================

using System;
using System.Globalization;

using Microsoft.Extensions.Options;

using PowerFramework.DataServices.Configuration;
using PowerFramework.DataServices.Domain;
using PowerFramework.Shared.Kernel;

namespace PowerFramework.DataServices.Services;

/// <summary>
/// The autocomplete result the search produced: the data-shaped replacement for the two deferred
/// edit-control calls at
/// <c>ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_dropdownsearch.sru:L128-L129</c>.
/// </summary>
/// <remarks>
/// <para>
/// DECISION 2 in the file header. <c>#DataWindow.SetText(sValDisp)</c> and
/// <c>#DataWindow.SelectText(nLenData + 1,Len(sValDisp))</c> mutate a live edit control and its
/// selection, which is the text-input half of the drop-down search split and is deferred to the
/// reserved <c>/v1/design/**</c> Gateway extension point (AAP 0.4.4). The three values those calls
/// consume are published here instead, so the gap is enumerable rather than silent.
/// </para>
/// <para>
/// A PLAIN RECORD AND NOT A GENERATED CONTRACT TYPE (constraint C-A). Types stay internal to
/// DataServices; <c>Grpc/</c> and <c>Endpoints/</c> own the projection onto the published contract,
/// so this file references nothing from <c>PowerFramework.Contracts</c>.
/// </para>
/// </remarks>
public sealed record DropDownSearchCompletion
{
    /// <summary>
    /// The matched display value the deferred half would place in the edit control - the oracle's
    /// <c>sValDisp</c> (<c>:L128</c>).
    /// </summary>
    /// <remarks>
    /// MAY BE THE EMPTY STRING EVEN ON A MATCH, and that is legacy behaviour rather than a fault. On
    /// the <c>ddlb</c> path the display half is taken as <c>Left(sVal,Pos(sVal,"~t") - 1)</c>
    /// [<c>:L123</c>], and an item carrying no tab makes <c>Pos</c> answer <c>0</c>, so <c>Left</c>
    /// receives <c>-1</c> and PowerScript answers the empty string. The match still counts as a match
    /// [<c>:L127</c>].
    /// </remarks>
    public required string Text { get; init; }

    /// <summary>
    /// The ONE-BASED character position the selection starts at - the oracle's <c>nLenData + 1</c>
    /// (<c>:L129</c>), where <c>nLenData</c> is the length of what the user has typed.
    /// </summary>
    /// <remarks>
    /// ONE-BASED AND NEVER REBASED. AAP 0.4.5.4 names one-based to zero-based translation the single
    /// most dangerous mechanical hazard in this refactor, and this is one of the four sites DECISION 6
    /// enumerates. The value is the position of the first character of the COMPLETED TAIL - the part
    /// the user did not type - so a consumer that subtracted one would select the last character the
    /// user typed as well.
    /// </remarks>
    public required long SelectionStart { get; init; }

    /// <summary>
    /// How many characters the selection covers - the oracle's <c>Len(sValDisp)</c> (<c>:L129</c>).
    /// </summary>
    /// <remarks>
    /// THE FULL LENGTH OF THE MATCHED VALUE, NOT THE LENGTH OF THE TAIL. Combined with a one-based
    /// start of <c>nLenData + 1</c>, the range deliberately runs PAST the end of the matched text by
    /// <c>nLenData</c> characters; PowerBuilder clamps it. Carried verbatim because the oracle passes
    /// exactly this, and computing the tail length instead would be a correction (constraint C-B).
    /// </remarks>
    public required long SelectionLength { get; init; }
}

/// <summary>
/// How the applied filter partitioned the child DataWindow's rows, together with the row ranges and
/// heights the two deferred <c>SetDetailHeight</c> calls would have consumed
/// (<c>n_cst_dwsvc_dropdownsearch.sru:L392-L405</c>).
/// </summary>
/// <remarks>
/// <para>
/// DECISION 3 in the file header. The DECISION to relocate and the RELOCATION itself are data-model
/// operations and ship; the two height manipulations are row geometry and are deferred to
/// <c>/v1/design/**</c> (AAP 0.4.4). Every argument those two calls take is published here.
/// </para>
/// <para>
/// THE TWO COUNTS ARE PRE-MOVE VALUES AND THAT IS THE WHOLE POINT OF THEM. The oracle captures them
/// at <c>:L393-L394</c>, before any relocation, under a comment at <c>:L392</c> that exists solely to
/// say so and which it repeats at <c>:L407</c>. They are the same two values
/// <see cref="DataWindowServiceHost.OnDDSFiltered"/> carries.
/// </para>
/// </remarks>
public sealed record DropDownSearchFilterPartition
{
    /// <summary>
    /// The child's row count BEFORE any buffer move - the oracle's <c>nRowCnt</c> (<c>:L393</c>).
    /// </summary>
    public required long RowCount { get; init; }

    /// <summary>
    /// The child's filtered-row count BEFORE any buffer move - the oracle's <c>nFilteredCnt</c>
    /// (<c>:L394</c>).
    /// </summary>
    public required long FilteredCount { get; init; }

    /// <summary>
    /// Whether the filtered rows were relocated into the primary buffer - the answer
    /// <c>_of_IsShowFilteredRows()</c> gave at <c>:L396</c>.
    /// </summary>
    /// <remarks>
    /// When <see langword="false"/> the oracle performs neither height call and no move, so every
    /// range member below is an empty range and <see cref="RelocationSucceeded"/> is
    /// <see langword="false"/>.
    /// </remarks>
    public required bool Relocated { get; init; }

    /// <summary>
    /// Whether the relocation reported success - the oracle's <c>RowsMove(...) = 1</c> test
    /// (<c>:L402</c>).
    /// </summary>
    /// <remarks>
    /// THE TEST IS <c>= 1</c> AND NOTHING ELSE. The second height call at <c>:L404</c> is performed
    /// ONLY on success, so a failed move leaves the moved rows at their full height - which is why
    /// this flag is published separately from <see cref="Relocated"/> rather than folded into it.
    /// </remarks>
    public required bool RelocationSucceeded { get; init; }

    /// <summary>
    /// How many rows the relocation was asked to move - the LIVE <c>FilteredCount()</c> the oracle
    /// re-reads inside the call at <c>:L402</c>.
    /// </summary>
    /// <remarks>
    /// DELIBERATELY SEPARATE FROM <see cref="FilteredCount"/>, WHICH IS THE CAPTURED VALUE. The
    /// oracle captures a filtered count at <c>:L394</c> for the event and then re-reads it live at
    /// <c>:L402</c> for the move. Under the ported path the two agree, because nothing between them
    /// alters the filter - but they are two separate reads in the oracle and collapsing them would
    /// hide a divergence rather than prove it cannot happen.
    /// </remarks>
    public required long RelocatedRowCount { get; init; }

    /// <summary>
    /// The first row the deferred half would set to <see cref="VisibleRowHeight"/> - the literal
    /// <c>1</c> the oracle passes at <c>:L399</c>.
    /// </summary>
    public required long VisibleRowStart { get; init; }

    /// <summary>
    /// The last row the deferred half would set to <see cref="VisibleRowHeight"/> - the oracle's
    /// <c>nRowCnt</c> (<c>:L399</c>).
    /// </summary>
    /// <remarks>
    /// ZERO MEANS THE ORACLE SKIPPED THE CALL. <c>:L398</c> guards the first height call with
    /// <c>if nRowCnt &gt; 0</c>, so a filter that matched nothing leaves the visible range EMPTY -
    /// end below start - and the deferred half must make no call for it.
    /// </remarks>
    public required long VisibleRowEnd { get; init; }

    /// <summary>
    /// The height the deferred half would apply to the visible rows - the captured
    /// <c>_editCtx.dddw.rowHeight</c> (<c>:L399</c>), read once from the child at <c>:L215</c>.
    /// </summary>
    public required long VisibleRowHeight { get; init; }

    /// <summary>
    /// The first relocated row the deferred half would hide - the oracle's <c>nRowCnt + 1</c>
    /// (<c>:L404</c>). ONE-BASED.
    /// </summary>
    public required long HiddenRowStart { get; init; }

    /// <summary>
    /// The last relocated row the deferred half would hide - the child's row count read LIVE AFTER
    /// the move (<c>:L404</c>).
    /// </summary>
    /// <remarks>
    /// A POST-MOVE READ, unlike <see cref="RowCount"/>. The oracle calls
    /// <c>_editCtx.dddw.object.RowCount()</c> inside the second height call, by which point the
    /// relocated rows are in the primary buffer and the count has grown. Reusing the pre-move count
    /// here would under-report the hidden range by exactly the number of rows moved.
    /// </remarks>
    public required long HiddenRowEnd { get; init; }

    /// <summary>
    /// The height the deferred half would apply to the relocated rows - the literal <c>0</c> the
    /// oracle passes at <c>:L404</c>, which is how it conceals them.
    /// </summary>
    /// <remarks>
    /// Carried rather than assumed, so the deferred half reads its argument from data instead of
    /// re-deriving a magic number, and so a reader can see that concealment is a HEIGHT OF ZERO
    /// rather than a visibility flag.
    /// </remarks>
    public required long HiddenRowHeight { get; init; }
}

/// <summary>
/// The drop-down state of the column being edited - the port of the nested structure
/// <c>dddwdata</c> (<c>n_cst_dwsvc_dropdownsearch.sru:L22-L31</c>), all EIGHT fields in declaration
/// order.
/// </summary>
/// <remarks>
/// <para>
/// MUTABLE BY NECESSITY, AND SAFE BECAUSE THE ORACLE NEVER COPIES IT. PowerBuilder structures are
/// value types, so a naive reading would make this a struct - but the oracle assigns its fields
/// INDIVIDUALLY AND IN PLACE, thirteen of them in one run at <c>:L183-L196</c>, and never once
/// assigns or copies the structure as a whole. A search of the source for a whole-structure
/// assignment finds none. A mutable reference record therefore reproduces every observable operation,
/// whereas a mutable struct reached through a property would silently mutate a copy - the classic C#
/// trap this shape avoids.
/// </para>
/// <para>
/// <b>INTERNAL, NOT PUBLIC.</b> The oracle's single instance lives inside <c>private
/// EDITCONTEXTDATA _editCtx</c> [<c>:L62</c>], so this type is not part of anyone's public API.
/// DECISION 8 in the file header records why it is <c>internal</c> rather than nested-private: the
/// parity suite asserts the thirteen-field reset field by field, and it reaches this type through the
/// project's existing <c>InternalsVisibleTo</c> item.
/// </para>
/// <para>
/// <b>TWO OF THE EIGHT FIELDS SIT ON THE DEFERRED BOUNDARY</b> and are documented as such on each:
/// <see cref="Child"/> and <see cref="Hwnd"/>.
/// </para>
/// </remarks>
internal sealed record DddwData
{
    /// <summary>
    /// The child's display column - the port of <c>string dispcolname</c> (<c>:L23</c>), read from
    /// <c>Describe(name + ".DDDW.DisplayColumn")</c> at <c>:L207</c>.
    /// </summary>
    /// <remarks>
    /// Interpolated UNESCAPED into the filter expression at <c>:L319</c> and <c>:L323</c> - DEFECT 2.
    /// Also the column the prefix search reads [<c>:L109</c>] and the column the seeded sort orders by
    /// [<c>:L220</c>].
    /// </remarks>
    public string DispColName { get; set; } = string.Empty;

    /// <summary>
    /// The child's data column - the port of <c>string datacolname</c> (<c>:L24</c>), read from
    /// <c>Describe(name + ".DDDW.DataColumn")</c> at <c>:L209</c>.
    /// </summary>
    /// <remarks>
    /// Two live uses, both in the filter builder: the equality test that suppresses the data clause
    /// when it names the same column as the display [<c>:L327</c>], and the unescaped interpolation
    /// itself [<c>:L331</c>, <c>:L334</c>]. It is ALSO the first arm of the show-filtered-rows
    /// auto-determination [<c>:L261</c>].
    /// </remarks>
    public string DataColName { get; set; } = string.Empty;

    /// <summary>
    /// The filter the child already carried before the search touched it - the port of
    /// <c>string orgfilter</c> (<c>:L25</c>), captured at <c>:L216-L217</c>.
    /// </summary>
    /// <remarks>
    /// The <c>"?"</c> sentinel is normalised to the empty string on capture [<c>:L217</c>] and again
    /// when re-read [<c>:L445</c>], because <c>Describe</c> answers <c>"?"</c> for a value it cannot
    /// determine and the composition at <c>:L374-L380</c> must treat "no original filter" as empty
    /// rather than as the literal text <c>?</c>.
    /// </remarks>
    public string OrgFilter { get; set; } = string.Empty;

    /// <summary>
    /// The composed filter currently applied to the child - the port of <c>string filter</c>
    /// (<c>:L26</c>).
    /// </summary>
    /// <remarks>
    /// THE CHANGE DETECTOR, AND THE ONLY THING THAT STOPS EVERY KEYSTROKE RE-FILTERING. <c>:L382</c>
    /// compares the newly composed expression against this field and skips the entire apply path when
    /// they agree. Seeded from <see cref="OrgFilter"/> at <c>:L218</c> so that the first composition
    /// which reproduces the original filter is correctly recognised as no change.
    /// </remarks>
    public string Filter { get; set; } = string.Empty;

    /// <summary>
    /// The user-derived half of the filter, without the original filter conjoined - the port of
    /// <c>string inputfilter</c> (<c>:L27</c>), stored at <c>:L384</c>.
    /// </summary>
    /// <remarks>
    /// KEPT SEPARATE FROM <see cref="Filter"/> FOR TWO REASONS, BOTH LIVE. It is what
    /// <c>of_HasFilter</c> tests to report whether the user has typed a restriction [<c>:L365</c>],
    /// and it is what <c>of_UpdateDDDWFilter</c> re-applies after the ORIGINAL filter changes
    /// underneath it [<c>:L451</c>] - which only works because the user's half was never merged into
    /// the composed expression.
    /// </remarks>
    public string InputFilter { get; set; } = string.Empty;

    /// <summary>
    /// The child DataWindow itself - the port of <c>datawindowchild object</c> (<c>:L28</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// A BOUNDARY FIELD, MODELLED AS THE HOST'S CHILD ABSTRACTION AND NEVER AS A RAW POINTER. The
    /// oracle obtains it exclusively through <c>#DataWindow.GetChild(name, ref ...)</c> [<c>:L157</c>,
    /// <c>:L170</c>, <c>:L211</c>], and it is re-fetched on every context refresh because - as the
    /// oracle's own comments at <c>:L156</c> and <c>:L169</c> warn - the handle CAN GO STALE. That
    /// re-fetch is reproduced, comment and all.
    /// </para>
    /// <para>
    /// Nullable, because the reset path assigns an uninitialised <c>datawindowchild</c> to it
    /// [<c>:L152</c>, <c>:L189</c>] and because <c>GetChild</c> can fail [<c>:L211</c>]. Dereferencing
    /// it when it is null reproduces PowerBuilder's null-object fault as a fail-fast, which AAP 0.1.4
    /// requires be preserved as fail-fast rather than softened into graceful degradation.
    /// </para>
    /// </remarks>
    public IDataWindowChild? Child { get; set; }

    /// <summary>
    /// The child's detail-band height as the child reported it - the port of <c>long rowheight</c>
    /// (<c>:L29</c>), captured at <c>:L215</c> from
    /// <c>Long(dwc.Describe("DataWindow.Detail.Height"))</c>.
    /// </summary>
    /// <remarks>
    /// A CAPTURED PROPERTY READ, WHICH SHIPS - NOT A COMPUTED GEOMETRY, WHICH WOULD NOT. Reading a
    /// property is a data-model read and the value crosses the boundary as data on
    /// <see cref="DropDownSearchFilterPartition.VisibleRowHeight"/>. Its only consumer in the oracle
    /// is the deferred <c>SetDetailHeight</c> at <c>:L399</c>, so nothing here computes with it, scales
    /// it, or converts it between units - the split line is drawn at the first arithmetic operation,
    /// exactly as Services/ColumnSortModel.cs draws it.
    /// </remarks>
    public long RowHeight { get; set; }

    /// <summary>
    /// The child's OS window handle - the port of <c>unsignedlong hwnd</c> (<c>:L30</c>). ALWAYS
    /// ZERO HERE, DELIBERATELY.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A BOUNDARY FIELD CARRIED FOR STRUCTURAL FIDELITY AND NEVER POPULATED. The oracle assigns it
    /// from <c>Handle(_editCtx.dddw.object)</c> at <c>:L171</c> and <c>:L214</c> and clears it at
    /// <c>:L195</c>; its ONLY readers are the deferred window calls -
    /// <c>if _editCtx.dddw.hWnd = 0 then return</c> and
    /// <c>Win32.ShowWindow(_editCtx.dddw.hWnd,8)</c> [<c>:L251</c>, <c>:L256</c>] and the
    /// <c>IsWindowVisible</c> guard at <c>:L385</c>. Obtaining a window handle is precisely what a
    /// headless service cannot do, so the two assignments emit NOTHING and this field keeps its zero.
    /// </para>
    /// <para>
    /// NOTHING HEADLESS MAY BRANCH ON IT, and nothing does: the two assignment sites are annotated
    /// where they would have stood, and the one guard that read it [<c>:L385</c>] is annotated at its
    /// site in ApplyFilter as a narrowing rather than reproduced. Keeping the field visible - rather
    /// than deleting it - is what makes the eight-field structure verifiable against
    /// <c>:L22-L31</c> and what tells a reader of the deferred half which value it must supply.
    /// </para>
    /// <para>
    /// <c>uint</c> because PowerBuilder's <c>unsignedlong</c> is 32 bits wide (AAP 0.4.5.2), matching
    /// the fixed-width convention Shared.Kernel <c>Bits</c> already uses.
    /// </para>
    /// </remarks>
    public uint Hwnd { get; set; }
}

/// <summary>
/// The service's whole mutable state: the column currently being edited and everything derived from
/// it - the port of the nested structure <c>editcontextdata</c>
/// (<c>n_cst_dwsvc_dropdownsearch.sru:L12-L20</c>), all SEVEN fields in declaration order.
/// </summary>
/// <remarks>
/// <para>
/// ONE INSTANCE PER SERVICE, held in the port of <c>private EDITCONTEXTDATA _editCtx</c>
/// [<c>:L62</c>]. Mutable and <c>internal</c> for the reasons given on <see cref="DddwData"/>.
/// </para>
/// <para>
/// THREAD AFFINITY. One instance serves one DataWindow and, like the oracle, is not designed for
/// concurrent entry: the four event handlers carry no synchronisation because the oracle's own event
/// dispatch is serial and every one of them reads and writes this object. A server-held instance must
/// therefore be scoped to one session, exactly as Domain/ValidationSession.cs scopes the four
/// cross-event fields of <c>se_cst_dw</c>.
/// </para>
/// </remarks>
internal sealed record EditContextData
{
    /// <summary>
    /// Whether the context describes a searchable column - the port of <c>boolean valid</c>
    /// (<c>:L13</c>).
    /// </summary>
    /// <remarks>
    /// THE GATE ON EVERY ENTRY POINT: <c>:L88</c> (edit changed), <c>:L233</c> (filter seeding),
    /// <c>:L363</c> (has filter) and <c>:L439</c> (update filter) all abandon when it is false. Set
    /// true at exactly two places - <c>:L222</c> for a <c>dddw</c> column that passed every check and
    /// <c>:L227</c> for an editable <c>ddlb</c> column - and cleared at <c>:L160</c> and <c>:L183</c>.
    /// </remarks>
    public bool Valid { get; set; }

    /// <summary>
    /// The one-based row being edited - the port of <c>long row</c> (<c>:L14</c>).
    /// </summary>
    /// <remarks>
    /// ZERO IS A LIVE VALUE: the lose-focus reset stores row <c>0</c> [<c>:L148</c>, <c>:L162</c>].
    /// It is also the field the fast path at <c>:L173</c> compares to detect a row change within one
    /// column.
    /// </remarks>
    public long Row { get; set; }

    /// <summary>
    /// The column being edited - the port of <c>dwobject dwo</c> (<c>:L15</c>).
    /// </summary>
    /// <remarks>
    /// Nullable, because <c>:L145-L148</c> deliberately passes an UNINITIALISED <c>dwobject</c> to
    /// reset the context - the explicit-null idiom AAP 0.4.5.4 forbids replacing with a sentinel.
    /// Carried through to both semantic events [<c>:L342</c>, <c>:L408</c>], which is why their
    /// parameters are nullable too.
    /// </remarks>
    public IDataWindowObject? Dwo { get; set; }

    /// <summary>
    /// The column's name - the port of <c>string name</c> (<c>:L16</c>), taken from <c>dwo.name</c>
    /// at <c>:L187</c>.
    /// </summary>
    /// <remarks>
    /// STORED AS THE DATAWINDOW REPORTED IT AND NEVER CASE-FOLDED ON THE WAY IN, which is what makes
    /// the comparison at <c>:L440</c> asymmetric - see the remarks on
    /// <see cref="DropDownSearchModel.UpdateDddwFilter(in string, string?, in bool)"/>. It is also the
    /// key every <c>Describe</c> and <c>GetChild</c> call composes from.
    /// </remarks>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The column's edit style - the port of <c>string style</c> (<c>:L17</c>), read from
    /// <c>Describe(name + ".Edit.Style")</c> at <c>:L200</c>.
    /// </summary>
    /// <remarks>
    /// ONLY TWO VALUES ARE EVER ACTED ON, <c>"dddw"</c> and <c>"ddlb"</c> [<c>:L203-L228</c>], and the
    /// two behave differently in every path that branches on them: the drop-down filters and the list
    /// box does not [<c>:L94</c>], and the two prefix searches read from entirely different places
    /// [<c>:L105-L124</c>]. Any other style leaves the context invalid.
    /// </remarks>
    public string Style { get; set; } = string.Empty;

    /// <summary>
    /// The drop-down state - the port of <c>dddwdata dddw</c> (<c>:L18</c>).
    /// </summary>
    /// <remarks>
    /// POPULATED ONLY ON THE <c>dddw</c> PATH. A <c>ddlb</c> column leaves every field at its reset
    /// value, which is load bearing rather than incidental: the empty display and data column names
    /// then compare EQUAL, which is what makes the data-column clause at <c>:L327</c> and the first
    /// auto-determination arm at <c>:L261</c> both short-circuit for a list box.
    /// </remarks>
    public DddwData Dddw { get; } = new();

    /// <summary>
    /// What the user had typed on the previous keystroke - the port of <c>string lastdata</c>
    /// (<c>:L19</c>).
    /// </summary>
    /// <remarks>
    /// THE AUTOCOMPLETE RATCHET. <c>:L98-L101</c> abandons the search whenever the new input is no
    /// LONGER than this - so backspacing suppresses completion, which is what stops the completion
    /// from immediately re-typing the character the user just deleted. Note that the field is updated
    /// on BOTH branches [<c>:L99</c> and <c>:L102</c>], so a suppressed pass still ratchets the
    /// length down.
    /// </remarks>
    public string LastData { get; set; } = string.Empty;
}

/// <summary>
/// Incremental drop-down search: as the user types into a <c>dddw</c> or <c>ddlb</c> column this
/// service filters the drop-down's rows and offers an autocompletion - the HEADLESS HALF of
/// <c>ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_dropdownsearch.sru</c>.
/// </summary>
/// <remarks>
/// <para>
/// THE FILE HEADER IS THE SPECIFICATION FOR THIS TYPE and is not repeated here: the split ruling and
/// its item-by-item deferred inventory, the five preserved defects, and DECISIONS 1 to 9 all live
/// there.
/// </para>
/// <para>
/// THE INHERITANCE EDGE AT <c>:L4</c> AND <c>:L33</c> - <c>global type n_cst_dwsvc_dropdownsearch
/// from n_cst_dwsvc</c> - is the base class below, and it is a REAL edge rather than a convenience:
/// two of the base's protected helpers are consumed here, <c>_of_getstyle</c> as
/// <see cref="DataWindowServiceBase.GetPresentationStyle"/> and <c>_of_getdwobject</c> as
/// <see cref="DataWindowServiceBase.GetDataWindowObject(in string)"/>, along with the attachment
/// properties and the vetoable enablement hook. Unlike <c>se_cst_dw</c>, whose parent
/// <c>se_cst_datawindow</c> lives in the deferred DesignSystem library and is therefore
/// reference-only (AAP 0.2.1.3 Correction 3), this parent is fully in scope and fully ported.
/// </para>
/// <para>
/// <b>WHY THE CHAIN INTERFACE IS DECLARED.</b> <c>se_cst_dw</c>'s <c>ondwnchanging</c> calls
/// <c>DropdownSearch.Event OnEditChanged(row,dwo,data)</c> DIRECTLY [<c>se_cst_dw.sru:L169-L171</c>]
/// rather than through the broker, because this service's own subscription for that hook is commented
/// out - DEFECT 3. Declaring <see cref="IDataWindowDropDownSearchService"/> makes that binding
/// COMPILE-CHECKED instead of matched by reflection, so a signature drift becomes a build failure
/// rather than a silent no-op at runtime. Nothing is added to satisfy it: both
/// <see cref="DataWindowServiceBase.Enabled"/> and
/// <see cref="DataWindowServiceBase.OnInit(DataWindowServiceHost)"/> already exist on the base with
/// the required signatures, exactly as that interface's own remarks anticipate.
/// </para>
/// <para>
/// SEALED because nothing derives from it. The oracle has no descendant of this class -
/// <c>se_cst_dw</c> holds it by its concrete type [<c>se_cst_dw.sru:L83</c>] and creates it directly
/// [<c>:L573</c>] - so leaving it open would advertise an extension point the oracle does not have.
/// </para>
/// <para>
/// NO SIBLING UNDER Services/ IS IMPORTED, AND THAT IS MEASURED. A cross-reference search across the
/// four legacy service objects finds zero live references between them, so this file names neither
/// <c>ColumnSortModel</c> nor <c>RowSelectService</c> nor the context-menu model.
/// </para>
/// </remarks>
public sealed class DropDownSearchModel : DataWindowServiceBase, IDataWindowDropDownSearchService
{
    // ==========================================================================================
    //  CONSTANTS - SPELLINGS AND ACCESSIBILITY BOTH PRESERVED VERBATIM (AAP 0.4.5.3)
    //  ----------------------------------------------------------------------------------------
    //  n_cst_dwsvc_dropdownsearch.sru:L49 introduces the set with the comment
    //  `//DDDW过滤的类型（支持组合）` - "the type of DDDW filtering (SUPPORTS COMBINATION)" - and the
    //  three bits are indeed read independently, by three separate bit tests at :L318, :L321 and
    //  :L326, each guarding one clause of the composed expression.
    //
    //  These identifiers appear in serialized payloads, in log records and in characterization
    //  recordings, so a rename would not restyle a symbol - it would silently invalidate every stored
    //  comparison that mentions it. The root .editorconfig therefore carries a scoped CA1707/IDE1006
    //  suppression for THIS FILE; the correct response to a naming diagnostic here is the suppression,
    //  never a rename.
    //
    //  A search of ws_objects/pfw.shared.pbl.src/enums.sru for FILTER_DISP, FILTER_DISP_PY,
    //  FILTER_DATA and FILTER_ALL returns nothing: the oracle declares all four ON THIS SERVICE
    //  OBJECT rather than in the framework-wide catalogue, so in the .NET tree they belong to this
    //  file - the same finding the row-select service's RS_* constants and the sort service's SORT_*
    //  constants produced. Configuration/DataServicesOptions.cs deliberately spells the FilterType
    //  default as the numeric literal 3 for that reason and does not reference them.
    //
    //  `uint` throughout, matching the oracle's `constant ulong` exactly: PowerBuilder's
    //  `unsignedlong` is 32 bits wide (AAP 0.4.5.2).
    // ==========================================================================================

    /// <summary>
    /// Match the DISPLAY column - <c>1</c> (<c>n_cst_dwsvc_dropdownsearch.sru:L50</c>, commented
    /// <c>//Display列</c>).
    /// </summary>
    /// <remarks>
    /// THE ONLY BIT WHOSE CLAUSE ASSIGNS RATHER THAN APPENDS [<c>:L319</c>], which is the mechanism
    /// behind DEFECT 1: with this bit clear, whichever later clause runs first emits a leading
    /// <c>" OR "</c> with nothing to its left.
    /// </remarks>
    public const uint FILTER_DISP = 1;

    /// <summary>
    /// Match the DISPLAY column by the Pinyin first letters of the input - <c>2</c> (<c>:L51</c>,
    /// commented <c>//Display列以输入数据的拼音首字母过滤</c>).
    /// </summary>
    /// <remarks>
    /// Its clause is doubly guarded: this bit must be set [<c>:L321</c>] AND the input must contain an
    /// ASCII letter [<c>:L322</c>], since Pinyin first letters are Latin letters and Chinese input
    /// could never match them. DECISION 4 in the file header records that the clause is emitted as
    /// expression TEXT and that this file takes no dependency on the matcher.
    /// </remarks>
    public const uint FILTER_DISP_PY = 2;

    /// <summary>
    /// Match the DATA column - <c>4</c> (<c>:L52</c>, commented <c>//Data列</c>).
    /// </summary>
    /// <remarks>
    /// NOT IN THE DEFAULT [<c>:L57</c>], and its clause is additionally suppressed whenever the data
    /// column names the same column as the display column [<c>:L327</c>] - which for a <c>ddlb</c>
    /// context is always, both names being empty.
    /// </remarks>
    public const uint FILTER_DATA = 4;

    /// <summary>
    /// Match every column - the sum of the three bits above (<c>:L53</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// DECLARED AS THE COMPUTED SUM AND NOT AS THE LITERAL 7, because the oracle declares it as
    /// <c>FILTER_DISP + FILTER_DISP_PY + FILTER_DATA</c> and the derivation is the point: a reader can
    /// see which bits it covers without counting in binary, and adding a fourth bit later would
    /// require this line to be revisited deliberately rather than silently disagreeing with it.
    /// </para>
    /// <para>
    /// <b>IT IS NOT THE DEFAULT AND IT IS NOT THE PINYIN FLAGS.</b> See DECISION 5 in the file header:
    /// the default <see cref="FilterType"/> is 3, and the Pinyin flags value is a different 7
    /// belonging to a different domain.
    /// </para>
    /// </remarks>
    public const uint FILTER_ALL = FILTER_DISP + FILTER_DISP_PY + FILTER_DATA;

    // ------------------------------------------------------------------------------------------
    //  THE BROKER TOPICS THIS SERVICE SUBSCRIBES
    //  ----------------------------------------------------------------------------------------
    //  Values taken verbatim from se_cst_dw.sru, where they are constants on the HOST that the oracle
    //  reaches as `#DataWindow.EVT_*` [:L508-L510]: :L52 EVT_ITEMFOCUSCHANGED, :L74 EVT_GETFOCUS,
    //  :L76 EVT_LOSEFOCUS. Declared privately here for the same reason Services/ColumnSortModel.cs
    //  and Services/RowSelectService.cs declare theirs privately - a subscriber needs the topic NAME,
    //  not a dependency on the declaring object - and none of these three carries the lexical ordering
    //  prefix that AAP 0.6.1.2 decomposes for the item-changed and edit-changed topics.
    //
    //  THERE IS DELIBERATELY NO EVT_EDITCHANGED CONSTANT HERE. That fourth subscription is COMMENTED
    //  OUT in the oracle [:L507] - DEFECT 3 - so this service never names that topic. Declaring the
    //  constant would leave a loaded gun beside the trigger; the fact is recorded in OnEnable instead.
    // ------------------------------------------------------------------------------------------

    /// <summary>The item-focus topic - <c>"itemfocuschanged"</c> (<c>se_cst_dw.sru:L52</c>).</summary>
    private const string EVT_ITEMFOCUSCHANGED = "itemfocuschanged";

    /// <summary>The focus-gained topic - <c>"getfocus"</c> (<c>se_cst_dw.sru:L74</c>).</summary>
    private const string EVT_GETFOCUS = "getfocus";

    /// <summary>The focus-lost topic - <c>"losefocus"</c> (<c>se_cst_dw.sru:L76</c>).</summary>
    private const string EVT_LOSEFOCUS = "losefocus";

    // ------------------------------------------------------------------------------------------
    //  THE REMAINING ORACLE LITERALS, NAMED RATHER THAN INLINED
    //  ----------------------------------------------------------------------------------------
    //  Every one is compared or emitted BYTE FOR BYTE. Naming them puts each in one place and lets the
    //  parity matrix assert against the same symbol the implementation uses, which is what stops a
    //  "harmless" reformatting from silently changing a composed filter or a Describe key. PascalCase
    //  rather than screaming snake, because the oracle spells them as inline literals rather than as
    //  named constants - only the four FILTER_* values above are named constants in the oracle, and
    //  only they keep their legacy spelling.
    // ------------------------------------------------------------------------------------------

    /// <summary>The drop-down DataWindow edit style (<c>:L94</c>, <c>:L105</c>, and six more).</summary>
    private const string StyleDddw = "dddw";

    /// <summary>The drop-down list-box edit style (<c>:L114</c>, <c>:L224</c>).</summary>
    private const string StyleDdlb = "ddlb";

    /// <summary>
    /// The answer a DataWindow gives for an INVALID expression - <c>"!"</c> (<c>:L201</c>,
    /// <c>:L208</c>, <c>:L210</c>).
    /// </summary>
    private const string InvalidExpressionSentinel = "!";

    /// <summary>
    /// The answer a DataWindow gives for a value it CANNOT DETERMINE - <c>"?"</c> (<c>:L217</c>,
    /// <c>:L219</c>, <c>:L445</c>).
    /// </summary>
    /// <remarks>
    /// Three live readings, and all three treat it as "there is none" rather than as text: no original
    /// filter [<c>:L217</c>, <c>:L445</c>] and no sort [<c>:L219</c>].
    /// </remarks>
    private const string UndeterminableSentinel = "?";

    /// <summary>The affirmative a DataWindow property answers with (<c>:L199</c>).</summary>
    private const string AffirmativeAnswer = "yes";

    /// <summary>The negative a DataWindow property answers with (<c>:L206</c>, <c>:L226</c>).</summary>
    private const string NegativeAnswer = "no";

    /// <summary>
    /// The four-character column-type prefix a searchable text column must have (<c>:L213</c>,
    /// <c>:L330</c>).
    /// </summary>
    private const string CharColumnTypePrefix = "char";

    /// <summary>
    /// How many characters of a reported column type are compared - <c>4</c> (<c>:L213</c>,
    /// <c>:L329</c>).
    /// </summary>
    /// <remarks>
    /// A PREFIX COMPARISON AND NOT A FULL-STRING ONE, DELIBERATELY. <c>Describe(".ColType")</c> answers
    /// <c>char(50)</c>, <c>decimal(2)</c>, <c>number</c>, <c>long</c> and <c>ulong</c>, so four
    /// characters is exactly what discriminates them: <c>"deci"</c> matches <c>decimal(2)</c>,
    /// <c>"numb"</c> matches <c>number</c> and <c>"ulon"</c> matches <c>ulong</c>. Widening it to a
    /// full-string comparison would make every parameterised type miss its arm.
    /// </remarks>
    private const int ColumnTypePrefixLength = 4;

    /// <summary>The column-type property suffix (<c>:L212</c>, <c>:L328</c>).</summary>
    private const string ColumnTypePropertySuffix = ".ColType";

    /// <summary>The display-only property suffix (<c>:L198</c>).</summary>
    private const string DisplayOnlyPropertySuffix = ".Edit.DisplayOnly";

    /// <summary>The edit-style property suffix (<c>:L200</c>).</summary>
    private const string EditStylePropertySuffix = ".Edit.Style";

    /// <summary>The edit allow-edit property suffix, for a <c>ddlb</c> (<c>:L225</c>).</summary>
    private const string EditAllowEditPropertySuffix = ".Edit.AllowEdit";

    /// <summary>The drop-down allow-edit property suffix (<c>:L205</c>).</summary>
    private const string DddwAllowEditPropertySuffix = ".DDDW.AllowEdit";

    /// <summary>The drop-down display-column property suffix (<c>:L207</c>).</summary>
    private const string DddwDisplayColumnPropertySuffix = ".DDDW.DisplayColumn";

    /// <summary>The drop-down data-column property suffix (<c>:L209</c>).</summary>
    private const string DddwDataColumnPropertySuffix = ".DDDW.DataColumn";

    /// <summary>The child's detail-band height property (<c>:L215</c>).</summary>
    private const string DetailHeightProperty = "DataWindow.Detail.Height";

    /// <summary>The child's current filter property (<c>:L216</c>, <c>:L444</c>).</summary>
    private const string TableFilterProperty = "DataWindow.Table.Filter";

    /// <summary>The child's current sort property (<c>:L219</c>).</summary>
    private const string TableSortProperty = "DataWindow.Table.Sort";

    /// <summary>
    /// The host's vertical-scroll-bar attribute, read through <c>Describe</c> (<c>:L263</c>).
    /// </summary>
    /// <remarks>
    /// READ AS A DATAWINDOW ATTRIBUTE, NEVER FROM A UI FRAMEWORK TYPE. The oracle reads
    /// <c>#DataWindow.VScrollBar</c> - a typed boolean on the control - and the ported form is this
    /// property expression through the host contract's attribute accessor, which is the only route a
    /// headless service has and which keeps every DesignSystem type out of this file (constraint C-D).
    /// <see cref="DescribesAffirmative"/> records how the text answer is read as a boolean.
    /// </remarks>
    private const string VerticalScrollBarProperty = "DataWindow.VScrollBar";

    /// <summary>The ascending sort suffix the child's seeded sort uses (<c>:L220</c>).</summary>
    private const string AscendingSortSuffix = " ASC";

    /// <summary>
    /// The DataWindow expression function the Pinyin clause NAMES AS TEXT - it is never called from
    /// here (<c>:L323</c>; declared
    /// <c>ws_objects/pfw.utility.pbl.src/pinyinfirstletterlike.srf</c>).
    /// </summary>
    /// <remarks>
    /// DECISION 4 in the file header is the ruling: this is a string that travels inside a filter
    /// expression, and Expressions/DataWindowExpressionEvaluator.cs resolves it when the filter is
    /// applied. The spelling must match what that evaluator registers, which is why it is a named
    /// constant rather than an inline literal.
    /// </remarks>
    private const string PinyinFunctionName = "PinyinFirstLetterLike";

    /// <summary>The tab that separates a <c>ddlb</c> item's display half from its data half (<c>:L123</c>).</summary>
    private const char DdlbValueSeparator = '\t';

    /// <summary>The space the search text's separators are substituted from (<c>:L317</c>).</summary>
    private const string SearchTextSeparator = " ";

    /// <summary>The wildcard the search text's separators are substituted to (<c>:L317</c>).</summary>
    private const string SearchTextWildcard = "%";

    // ==========================================================================================
    //  STATE                                            n_cst_dwsvc_dropdownsearch.sru:L62
    //  ----------------------------------------------------------------------------------------
    //  The oracle declares exactly ONE private field, and so does this class. Everything else it needs
    //  is either a constant above, an option below, or reachable through the base's host.
    // ==========================================================================================

    /// <summary>
    /// The edit context - the port of <c>private EDITCONTEXTDATA _editCtx</c> (<c>:L62</c>).
    /// </summary>
    private readonly EditContextData _editCtx = new();

    /// <summary>
    /// The Pinyin matching flags the composed clause emits - bound from
    /// <c>DataServices:DropDownSearch:PinyinMatchFlags</c>.
    /// </summary>
    /// <remarks>
    /// THE ORACLE HARDCODES THIS AS THE LITERAL <c>7</c> AT <c>:L323</c>. AAP 0.4.5.5 requires every
    /// value the legacy hardcodes to move into an options type, and
    /// <see cref="DropDownSearchOptions.PinyinMatchFlags"/> already carries it with that same default
    /// composed from <c>Enums.PY_LIKE_IGNORE_CASE | PY_LIKE_IGNORE_WIDTH | PY_LIKE_FUZZY_SOUND</c> -
    /// so the default is proved rather than restated, and this file references neither
    /// <c>Enums</c> nor a second copy of the literal. See DECISION 5 for why this 7 is unrelated to
    /// <see cref="FILTER_ALL"/>'s 7.
    /// </remarks>
    private readonly long _pinyinMatchFlags;

    /// <summary>
    /// Creates the service, binding the three settings the oracle initialises as declarations.
    /// </summary>
    /// <param name="options">The DataServices options. Never <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// THIS IS THE PORT OF <c>event constructor</c> [<c>:L503</c>], which is
    /// <c>call super::constructor;SetNull(#ShowFilteredRows)</c> - a super call and one deliberate
    /// null. The null survives as the absence of any initializer on
    /// <see cref="ShowFilteredRows"/>, seeded from an option whose own declared default is null; see
    /// DECISION 7.
    /// </para>
    /// <para>
    /// The other two assignments are the oracle's DECLARATION-SITE initialisers at <c>:L57</c> and
    /// <c>:L323</c>, which run at construction in PowerBuilder too, so binding them here reproduces the
    /// same moment. Nothing is hardcoded twice: each value comes from the option whose declared default
    /// already reproduces the legacy one.
    /// </para>
    /// </remarks>
    public DropDownSearchModel(IOptions<DataServicesOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        DropDownSearchOptions settings = options.Value.DropDownSearch;

        // :L57  privatewrite ulong #FilterType = FILTER_DISP + FILTER_DISP_PY
        //       The option's declared default is 3, which IS that two-term sum - see DECISION 5 for
        //       why it is emphatically not FILTER_ALL.
        FilterType = settings.FilterType;

        // :L58 declaration + :L503 SetNull(#ShowFilteredRows). Assigning the option's bool? carries
        //      the null through; there is no coalescing here and there must never be (DECISION 7).
        ShowFilteredRows = settings.ShowFilteredRows;

        // :L323  the flags argument, hardcoded as 7 by the oracle.
        _pinyinMatchFlags = settings.PinyinMatchFlags;
    }

    // ==========================================================================================
    //  PROPERTIES                                       n_cst_dwsvc_dropdownsearch.sru:L55-L59
    //  ----------------------------------------------------------------------------------------
    //  Both oracle properties are `privatewrite`: publicly readable, writable only from inside. That
    //  maps exactly onto a public getter with a private setter, and the two setters the oracle
    //  provides - of_SetFilterType and of_SetShowFilteredRows - are the only mutators, both preserved
    //  below with their legacy return contracts.
    // ==========================================================================================

    /// <summary>
    /// Which columns the composed filter matches against, as a combinable bitmask over
    /// <see cref="FILTER_DISP"/>, <see cref="FILTER_DISP_PY"/> and <see cref="FILTER_DATA"/> - the
    /// port of <c>privatewrite ulong #FilterType</c> (<c>n_cst_dwsvc_dropdownsearch.sru:L57</c>).
    /// </summary>
    /// <value>
    /// Defaults to <see cref="FILTER_DISP"/> + <see cref="FILTER_DISP_PY"/>, that is <c>3</c> - and
    /// deliberately NOT <see cref="FILTER_ALL"/>. Bound from
    /// <see cref="DropDownSearchOptions.FilterType"/>, whose declared default reproduces the same
    /// line.
    /// </value>
    /// <remarks>
    /// ANY VALUE IS ACCEPTED, INCLUDING ZERO AND VALUES WITH NO MEANING - see DEFECT 4 on
    /// <see cref="SetFilterType(in uint)"/>. Read by three independent bit tests, never by an equality
    /// comparison, so an unrecognised high bit is simply ignored rather than rejected.
    /// </remarks>
    public uint FilterType { get; private set; }

    /// <summary>
    /// Whether rows the filter removed are nonetheless kept in the drop-down, relocated to the end -
    /// the port of <c>privatewrite boolean #ShowFilteredRows</c>
    /// (<c>n_cst_dwsvc_dropdownsearch.sru:L58</c>). THREE-STATE.
    /// </summary>
    /// <value>
    /// <see langword="true"/> to relocate, <see langword="false"/> not to, and
    /// <see langword="null"/> - THE DEFAULT - to decide automatically.
    /// </value>
    /// <remarks>
    /// <para>
    /// The oracle's own two-line comment at <c>:L58-L59</c> explains both the mechanism and the
    /// default: the filtered rows are MOVED INTO THE PRIMARY BUFFER to solve the problem of filtering
    /// affecting the text OTHER ROWS DISPLAY, and a NULL value means the service determines it
    /// automatically, which is the default.
    /// </para>
    /// <para>
    /// THE NULL IS PROVEN BY CODE, NOT MERELY BY THAT COMMENT: the constructor at <c>:L503</c> is
    /// nothing but <c>call super::constructor</c> followed by <c>SetNull(#ShowFilteredRows)</c>. And it
    /// GATES A REAL THREE-ARM ALGORITHM - <see cref="IsShowFilteredRows"/> evaluates <c>:L261-L263</c>
    /// in order only when this is null - so collapsing it to a non-nullable <see cref="bool"/> would
    /// not change a default, it would DELETE that algorithm, because <see langword="false"/> and
    /// "decide for me" would become indistinguishable (DECISION 7).
    /// </para>
    /// </remarks>
    public bool? ShowFilteredRows { get; private set; }

    /// <summary>
    /// The autocompletion the most recent edit-changed pass produced, or <see langword="null"/> when
    /// it produced none - the data-shaped replacement for the deferred <c>SetText</c> and
    /// <c>SelectText</c> calls at <c>n_cst_dwsvc_dropdownsearch.sru:L128-L129</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// DECISION 2 in the file header. REPLACED WHOLE ON EVERY PASS AND NEVER CARRIED ACROSS ONE:
    /// <see cref="OnEditChanged(long, IDataWindowObject, string)"/> clears it on entry, before any
    /// guard, because the oracle's two calls either happen once during a pass or do not happen at all -
    /// there is no state that survives an unmatched keystroke, since the control then simply holds
    /// what the user typed. A stale completion surviving a pass would describe a substitution the
    /// oracle did not perform, which is the same discipline
    /// Services/ColumnSortModel.cs applies to its indicator descriptors.
    /// </para>
    /// <para>
    /// So <see langword="null"/> after a pass means any of: the context was invalid [<c>:L88</c>], the
    /// input did not grow so completion was suppressed [<c>:L98-L101</c>], or the search found no
    /// prefix match [<c>:L127</c>]. All three are the same fact for the deferred half - apply nothing.
    /// </para>
    /// </remarks>
    public DropDownSearchCompletion? Completion { get; private set; }

    /// <summary>
    /// How the most recently applied filter partitioned the child's rows, or <see langword="null"/>
    /// when no filter has yet been applied.
    /// </summary>
    /// <remarks>
    /// <para>
    /// DECISION 3 in the file header. SET ONLY INSIDE THE CHANGED BRANCH, and that asymmetry with
    /// <see cref="Completion"/> is deliberate rather than an oversight: <c>:L382</c> skips the ENTIRE
    /// apply path when the composed expression equals the one already applied, so on a no-change pass
    /// the oracle touches neither the child nor its buffers - and the previous partition therefore
    /// still describes the child's state accurately. Clearing it there would report a partition change
    /// that did not occur.
    /// </para>
    /// </remarks>
    public DropDownSearchFilterPartition? Partition { get; private set; }

    /// <summary>
    /// The edit context, exposed to the parity suite - the port of <c>_editCtx</c>
    /// (<c>n_cst_dwsvc_dropdownsearch.sru:L62</c>).
    /// </summary>
    /// <remarks>
    /// PRIVATE IN THE ORACLE AND <c>internal</c> HERE, FOR ASSERTION ONLY. DECISION 8 in the file
    /// header records the reasoning: the thirteen-field reset at <c>:L183-L196</c>, the fast path at
    /// <c>:L173-L178</c> and the display-only early return at <c>:L199</c> are all invisible from the
    /// public surface, and constraint C-H makes them testable rather than optional. The widening is
    /// unobservable outside the assembly and the mechanism is the project's existing
    /// <c>InternalsVisibleTo</c> item. No production code outside this class reads it.
    /// </remarks>
    internal EditContextData EditContext => _editCtx;

    // ==========================================================================================
    //  THE FOUR EVENTS                                  n_cst_dwsvc_dropdownsearch.sru:L34-L37
    //  ----------------------------------------------------------------------------------------
    //  Three are broker-wired by OnEnable; the fourth - OnEditChanged - is NOT, because its
    //  subscription is commented out in the oracle (DEFECT 3) and the host calls it directly. All four
    //  are public because the broker reaches its handlers by name and because the host calls one of
    //  them through IDataWindowDropDownSearchService.
    // ==========================================================================================

    /// <summary>
    /// The edit cursor moved to another cell - the port of
    /// <c>event onitemfocuschanged(long row, dwobject dwo)</c>
    /// (<c>n_cst_dwsvc_dropdownsearch.sru:L34</c>, body <c>:L81</c>).
    /// </summary>
    /// <param name="row">The ONE-BASED row now being edited.</param>
    /// <param name="dwo">
    /// The column now being edited. Nullable because the whole body forwards to a method whose reset
    /// path exists precisely to handle an invalid object [<c>:L154</c>].
    /// </param>
    /// <remarks>
    /// A ONE-LINE BODY IN THE ORACLE AND A ONE-LINE BODY HERE - <c>_of_InitEditContextInfo(row,dwo)</c>
    /// and nothing else. Subscribed to <c>EVT_ITEMFOCUSCHANGED</c> at <c>:L508</c>.
    /// </remarks>
    public void OnItemFocusChanged(long row, IDataWindowObject? dwo)
    {
        // :L81  _of_InitEditContextInfo(row,dwo)
        InitEditContextInfo(row, dwo);
    }

    /// <summary>
    /// The text in the edit control changed - the port of
    /// <c>event oneditchanged(long row, dwobject dwo, string data)</c>
    /// (<c>n_cst_dwsvc_dropdownsearch.sru:L35</c>, body <c>:L84-L131</c>). This is where the filter is
    /// recomposed and the autocompletion searched for.
    /// </summary>
    /// <param name="row">The ONE-BASED row being edited.</param>
    /// <param name="dwo">The column being edited.</param>
    /// <param name="data">
    /// The edit text, verbatim and UNESCAPED. MAY BE NULL AT RUNTIME despite the non-nullable
    /// declaration: the host forwards it from a raw <c>pbm_dwnchanging</c> argument and the oracle
    /// coerces it at <c>:L90</c>, so the coercion below is load bearing rather than defensive.
    /// </param>
    /// <remarks>
    /// <para>
    /// NOT BROKER-SUBSCRIBED - DEFECT 3. <c>:L507</c> carries
    /// <c>//#DataWindow.of_On(#DataWindow.EVT_EDITCHANGED,this,"onEditChanged")</c> COMMENTED OUT, so
    /// the host drives this hook directly instead: <c>se_cst_dw.sru:L169-L171</c> is
    /// <c>if DropdownSearch.#Enabled then DropdownSearch.Event OnEditChanged(row,dwo,data)</c>,
    /// reached from <c>ondwnchanging</c> AFTER the semantic <c>EditChanged</c> event and after the
    /// broker's own <c>EVT_EDITCHANGED</c> topic. The exclusion is corroborated a second time by the
    /// broker's argument-injection hook, which names this service explicitly:
    /// <c>se_cst_dw.sru:L602</c> is <c>if target = DropDownSearch then return 0</c>, so even were the
    /// subscription revived this service would not receive the injected source argument the other
    /// subscribers get. Not revived (constraint C-B).
    /// </para>
    /// <para>
    /// THE ORDER OF THE BODY IS CONTRACT. The filter is recomposed and applied FIRST [<c>:L94-L96</c>]
    /// and the autocomplete search runs SECOND [<c>:L104-L125</c>], which means the prefix scan reads
    /// the child AFTER it has been filtered - so a completion can only ever be offered from rows the
    /// filter kept. Reversing the two would offer completions the drop-down no longer shows.
    /// </para>
    /// </remarks>
    public void OnEditChanged(long row, IDataWindowObject dwo, string data)
    {
        // PowerScript locals, declared at :L84-L86 and initialised by the runtime: numerics to 0,
        // strings to "" and booleans to false. nIndex's zero initialisation is load bearing - the ddlb
        // walk pre-increments it, so the first index it passes to GetValue is 1 (DECISION 6).
        long nIndex = 0;
        long nRowCnt;
        string sVal = string.Empty;
        string sValDisp = string.Empty;
        bool bMatched = false;

        // The published completion belongs to THIS pass only; see the remarks on Completion.
        Completion = null;

        // :L88  if Not _editCtx.valid then return
        if (!_editCtx.Valid)
        {
            return;
        }

        // :L90  if IsNull(data) then data = ""
        //       The by-value parameter is REASSIGNED, exactly as the oracle reassigns it, and every
        //       later use reads the coerced value (AAP 0.4.5.2 keeps a mutated by-value parameter a
        //       plain by-value parameter for this reason).
        if (data is null)
        {
            data = string.Empty;
        }

        // :L92  nLenData = Len(data)
        long nLenData = Len(data);

        // :L94-L96  if _editCtx.Style = "dddw" then _of_Filter(row,dwo,_of_GetFilter(data),true)
        //           The `true` is the show flag, whose only effect - _of_ShowDDDW - is deferred; it is
        //           still passed so the argument the deferred half needs is not lost.
        if (_editCtx.Style == StyleDddw)
        {
            ApplyFilter(row, dwo, GetFilter(data), show: true);
        }

        // :L98-L101  THE AUTOCOMPLETE RATCHET. Completion advances only on GROWING input, so
        //            backspacing suppresses it - which is what stops the completion from instantly
        //            re-typing the character the user just deleted. NOTE the field is updated on BOTH
        //            branches [:L99 and :L102], so a suppressed pass still ratchets the length down.
        if (nLenData <= Len(_editCtx.LastData))
        {
            _editCtx.LastData = data;
            return;
        }

        // :L102  _editCtx.lastData = data
        _editCtx.LastData = data;

        // :L104-L125  choose case _editCtx.Style - the prefix search, one arm per edit style.
        switch (_editCtx.Style)
        {
            case StyleDddw:
            {
                // :L106  nRowCnt = _editCtx.dddw.object.RowCount()
                IDataWindowChild child = RequireChild();
                nRowCnt = child.RowCount();

                // :L107  if nRowCnt > 0 then
                if (nRowCnt > 0)
                {
                    // :L108  for nRow = 1 to nRowCnt   ONE-BASED AND INCLUSIVE (DECISION 6):
                    //        RowCount() is the LAST VALID ROW NUMBER, not a length.
                    for (long nRow = 1; nRow <= nRowCnt; nRow++)
                    {
                        // :L109  sValDisp = ...GetItemString(nRow,_editCtx.dddw.dispColName)
                        //        A null item is an ordinary outcome on that contract; PowerScript
                        //        would carry the null into Left/Lower and produce a null that
                        //        compares unequal, which the empty string reproduces here without
                        //        needing null-propagating comparison semantics.
                        sValDisp = child.GetItemString(nRow, _editCtx.Dddw.DispColName) ?? string.Empty;

                        // :L110  bMatched = (Lower(Left(sValDisp,nLenData)) = Lower(data))
                        bMatched = Lower(Left(sValDisp, nLenData)) == Lower(data);

                        // :L111  if bMatched then exit   - FIRST MATCH WINS, in row order, which
                        //        is why the seeded ascending sort at :L220 is observable.
                        if (bMatched)
                        {
                            break;
                        }
                    }
                }

                break;
            }

            case StyleDdlb:
            {
                DataWindowServiceHost host = RequireHost();

                // :L115-L121  do ... loop until(bMatched)   A DO-WHILE, so the first probe always
                //             happens; the walk ends on the first EMPTY value, which is how
                //             PowerBuilder reports "past the last item".
                do
                {
                    // :L116  nIndex ++   PRE-INCREMENTED, so the first index passed is 1
                    nIndex++;

                    // :L117  sVal = #DataWindow.GetValue(_editCtx.Name,nIndex)
                    sVal = host.GetValue(_editCtx.Name, nIndex);

                    // :L118  if sVal = "" then exit
                    if (sVal == string.Empty)
                    {
                        break;
                    }

                    // :L119-L120  //忽略大小写 - "ignore case"
                    bMatched = Lower(Left(sVal, nLenData)) == Lower(data);
                }
                while (!bMatched);

                // :L122-L124  if bMatched then sValDisp = Left(sVal,Pos(sVal,"~t") - 1)
                //             A ddlb item is "display<TAB>data". AN ITEM WITH NO TAB YIELDS THE
                //             EMPTY STRING - Pos answers 0, Left receives -1 - and the match still
                //             counts. Preserved exactly; see DropDownSearchCompletion.Text.
                if (bMatched)
                {
                    sValDisp = Left(sVal, Pos(sVal, DdlbValueSeparator) - 1);
                }

                break;
            }

            default:
                // NO DEFAULT ARM IN THE ORACLE [:L104-L125]. A style that is neither dddw nor ddlb
                // cannot reach here anyway, because :L203-L228 leaves the context invalid for it and
                // :L88 already returned. Present only because C# has no fall-out form for a switch
                // statement; it performs nothing, exactly as the absent arm performs nothing.
                break;
        }

        // :L127-L130  if bMatched then SetText(sValDisp); SelectText(nLenData + 1,Len(sValDisp))
        //             BOTH CALLS ARE DEFERRED - they mutate a live edit control and its selection,
        //             which is the text-input half (DECISION 2). The three values they consume are
        //             published instead, for the reserved /v1/design/** extension point (AAP 0.4.4).
        //             The selection start stays ONE-BASED (DECISION 6).
        if (bMatched)
        {
            Completion = new DropDownSearchCompletion
            {
                Text = sValDisp,
                SelectionStart = nLenData + 1,
                SelectionLength = Len(sValDisp),
            };
        }
    }

    /// <summary>
    /// The DataWindow gained focus - the port of <c>event ongetfocus()</c>
    /// (<c>n_cst_dwsvc_dropdownsearch.sru:L36</c>, body <c>:L133-L143</c>).
    /// </summary>
    /// <remarks>
    /// A FORCED REFRESH OF THE EDIT CONTEXT, as the oracle's own comment at <c>:L141</c> says. It is
    /// needed because focus can return to a cell without an intervening item-focus-changed event, which
    /// would otherwise leave the context describing whatever was current when focus was lost.
    /// Subscribed to <c>EVT_GETFOCUS</c> at <c>:L509</c>.
    /// </remarks>
    public void OnGetFocus()
    {
        DataWindowServiceHost host = RequireHost();

        // :L136-L137  nRow = #DataWindow.GetRow() / if nRow = 0 then return
        long nRow = host.GetRow();
        if (nRow == 0)
        {
            return;
        }

        // :L138-L139  sColName = #DataWindow.GetColumnName() / if sColName = "" then return
        //             THE ROW IS READ FIRST AND THE COLUMN SECOND, and the order matters: resolving a
        //             column object for a DataWindow with no current row is what the first guard
        //             prevents.
        string sColName = host.GetColumnName();
        if (sColName == string.Empty)
        {
            return;
        }

        // :L142  _of_InitEditContextInfo(nRow,_of_GetDWObject(sColName))
        //        GetDataWindowObject answers null when the DataWindow has no valid object model
        //        [n_cst_dwsvc.sru:L119], and the reset path handles that.
        InitEditContextInfo(nRow, GetDataWindowObject(sColName));
    }

    /// <summary>
    /// The DataWindow lost focus - the port of <c>event onlosefocus()</c>
    /// (<c>n_cst_dwsvc_dropdownsearch.sru:L37</c>, body <c>:L145-L149</c>).
    /// </summary>
    /// <remarks>
    /// THE EXPLICIT-NULL RESET IDIOM. The oracle declares an UNINITIALISED <c>dwobject dwoNvl</c>
    /// [<c>:L145</c>] purely to pass it, then calls <c>_of_InitEditContextInfo(0,dwoNvl)</c>
    /// [<c>:L148</c>] - both arguments chosen to fail the validity test at <c>:L154</c> and take the
    /// reset path, which restores the child's original filter before clearing the context. Null is
    /// passed as null; AAP 0.4.5.4 forbids substituting a sentinel. Subscribed to
    /// <c>EVT_LOSEFOCUS</c> at <c>:L510</c>.
    /// </remarks>
    public void OnLoseFocus()
    {
        // :L145  dwobject dwoNvl   - declared, never assigned
        IDataWindowObject? dwoNvl = null;

        // :L147-L148  //强制刷新编辑上下文 - "force-refresh the edit context"
        InitEditContextInfo(0, dwoNvl);
    }

    /// <summary>
    /// Subscribes or unsubscribes the three broker topics as the service is enabled or disabled - the
    /// port of <c>event onenable</c> (<c>n_cst_dwsvc_dropdownsearch.sru:L506-L515</c>).
    /// </summary>
    /// <param name="enabled">The new enablement state.</param>
    /// <returns>
    /// <c>0</c> ALWAYS, verbatim from <c>:L514</c>. The base's <c>of_setenabled</c> treats a
    /// prevention as a veto, so returning anything non-zero here would make enabling fail; the oracle
    /// never does.
    /// </returns>
    /// <remarks>
    /// <para>
    /// THE BASE CALL COMES FIRST - <c>call super::onenable</c> [<c>:L506</c>] - and its result is
    /// discarded, exactly as the oracle discards it.
    /// </para>
    /// <para>
    /// <b>DEFECT 3, AT ITS SITE.</b> The oracle's first subscription line is COMMENTED OUT:
    /// <c>//#DataWindow.of_On(#DataWindow.EVT_EDITCHANGED,this,"onEditChanged")</c> [<c>:L507</c>].
    /// It is reproduced as this comment and NOT as code. The consequence is that
    /// <see cref="OnEditChanged(long, IDataWindowObject, string)"/> is not broker-wired and must be
    /// driven directly by the host, which <c>se_cst_dw.sru:L169-L171</c> does - and which
    /// <c>se_cst_dw.sru:L602</c> corroborates by excluding this very service from the broker's
    /// argument injection. Reviving the subscription would double-dispatch every keystroke: once
    /// through the topic and once through the direct call.
    /// </para>
    /// <para>
    /// THE UNSUBSCRIBE IS BY TARGET, NOT BY TOPIC. <c>:L512</c> is <c>#DataWindow.of_Off(this)</c> -
    /// the single-argument object overload - so all three subscriptions go at once and no topic name is
    /// repeated on the disable path. <c>se_cst_dw.sru:L447</c> and <c>:L463</c> show
    /// <c>of_On</c>/<c>of_Off</c> to be one-line forwards to the broker, so reaching
    /// <c>Eventful</c> directly here is the same call with one less hop.
    /// </para>
    /// </remarks>
    protected override long OnEnable(bool enabled)
    {
        // :L506  call super::onenable
        _ = base.OnEnable(enabled);

        DataWindowServiceHost host = RequireHost();

        if (enabled)
        {
            // :L507  //#DataWindow.of_On(#DataWindow.EVT_EDITCHANGED,this,"onEditChanged")
            //        COMMENTED OUT IN THE ORACLE - DEFECT 3. Deliberately not code. See the remarks.

            // :L508  #DataWindow.of_On(#DataWindow.EVT_ITEMFOCUSCHANGED,this,"onItemFocusChanged")
            _ = host.Eventful.Subscribe(EVT_ITEMFOCUSCHANGED, this, nameof(OnItemFocusChanged));

            // :L509  #DataWindow.of_On(#DataWindow.EVT_GETFOCUS,this,"onGetFocus")
            _ = host.Eventful.Subscribe(EVT_GETFOCUS, this, nameof(OnGetFocus));

            // :L510  #DataWindow.of_On(#DataWindow.EVT_LOSEFOCUS,this,"onLoseFocus")
            _ = host.Eventful.Subscribe(EVT_LOSEFOCUS, this, nameof(OnLoseFocus));
        }
        else
        {
            // :L512  #DataWindow.of_Off(this)
            _ = host.Eventful.Unsubscribe(this);
        }

        // :L514  return 0
        return 0L;
    }

    // ==========================================================================================
    //  THE EDIT-CONTEXT STATE MACHINE            n_cst_dwsvc_dropdownsearch.sru:L151-L249
    // ==========================================================================================

    /// <summary>
    /// Rebuilds the edit context for one cell, or resets it - the port of
    /// <c>_of_initeditcontextinfo(readonly long row, readonly dwobject dwo)</c>
    /// (<c>n_cst_dwsvc_dropdownsearch.sru:L151-L229</c>). THE STATE MACHINE AT THE CENTRE OF THIS
    /// SERVICE.
    /// </summary>
    /// <param name="row">
    /// The ONE-BASED row to describe, or <c>0</c> to reset. Any non-positive value takes the reset
    /// path.
    /// </param>
    /// <param name="dwo">
    /// The column to describe, or <see langword="null"/> to reset. An invalid object takes the reset
    /// path just as a non-positive row does.
    /// </param>
    /// <remarks>
    /// <para>
    /// FOUR PATHS, IN THE ORDER THE ORACLE TESTS THEM: reset [<c>:L154-L165</c>], the row-change fast
    /// path [<c>:L173-L178</c>], the full rebuild [<c>:L183-L228</c>], and the two early returns that
    /// leave the context invalid but described [<c>:L199</c>, <c>:L201</c>].
    /// </para>
    /// <para>
    /// THE CHILD IS RE-FETCHED ON EVERY ENTRY THAT HAS ONE, and the oracle says why in its own comments
    /// at <c>:L156</c> and <c>:L169</c>: <c>//*重新获取DataWindowChild（可能会失效）</c> - "re-obtain the
    /// DataWindowChild (it may have become invalid)". The handle can go stale between events, so both
    /// paths re-fetch before they touch it. The result is discarded at both sites, exactly as the
    /// oracle discards it, but the <c>ref</c> argument is written back to the field because PowerScript
    /// passes the field itself by reference.
    /// </para>
    /// <para>
    /// <c>internal</c> rather than the oracle's <c>private</c>; see DECISION 8.
    /// </para>
    /// </remarks>
    internal void InitEditContextInfo(in long row, IDataWindowObject? dwo)
    {
        // :L152  datawindowchild dwcEmtpy   - declared, never assigned; the oracle's typo preserved in
        //        spirit by naming it for what it is rather than reproducing the misspelling.
        const IDataWindowChild? emptyChild = null;

        DataWindowServiceHost host = RequireHost();

        // :L154  if row <= 0 or Not IsValidObject(dwo) then
        //        The explicit null test restates the ported predicate's own first line -
        //        isvalidobject.srf:L11 is `if IsNull(object) then return false` - so it changes no
        //        behaviour and lets the compiler's flow analysis prove dwo non-null below.
        if (row <= 0 || dwo is null || !Predicates.IsValidObject(dwo))
        {
            // :L155  if _editCtx.valid then
            if (_editCtx.Valid)
            {
                // :L156-L157  re-fetch the child, which may have gone stale
                RefetchChild(host);

                // :L158  _of_Filter(_editCtx.row,_editCtx.dwo,"",false)
                //        AN EMPTY FILTER RESTORES THE CHILD'S ORIGINAL FILTER rather than clearing it
                //        outright - see the composition at :L378-L380. This is the whole reason the
                //        reset path exists: leaving focus must not leave the user's search restriction
                //        on a shared child DataWindow.
                ApplyFilter(_editCtx.Row, _editCtx.Dwo, string.Empty, show: false);
            }

            // :L160-L163
            _editCtx.Valid = false;
            _editCtx.LastData = string.Empty;
            _editCtx.Row = row;
            _editCtx.Dwo = dwo;

            // :L164  return
            return;
        }

        // :L166  //if _editCtx.row = row and _editCtx.name = dwo.name then return
        //        DORMANT, COMMENTED OUT IN THE ORACLE. It would have short-circuited a re-entry for the
        //        same cell - but doing so would also skip the child re-fetch immediately below, which
        //        is exactly what the comments at :L156 and :L169 warn must not be skipped. Carried as a
        //        comment and NOT revived (constraint C-B).

        // :L168  if _editCtx.valid then
        if (_editCtx.Valid)
        {
            // :L169-L170  re-fetch the child, which may have gone stale
            RefetchChild(host);

            // :L171  _editCtx.dddw.hWnd = Handle(_editCtx.dddw.object)
            //        DEFERRED - obtaining an OS window handle. Nothing is emitted and DddwData.Hwnd
            //        keeps its zero; its only readers were the deferred window calls at :L251, :L256
            //        and :L385.

            // :L172-L178  //如果只是行改变则只初始化一下DDDW过滤条件即可
            //             "if only the row changed, initialising the DDDW filter condition suffices"
            //             THE FAST PATH. All three conditions must hold: the SAME column, a DIFFERENT
            //             row, and a dddw style. It deliberately does NOT re-read any Describe property
            //             or rebuild the child state, because none of it can have changed - only the
            //             row's value has, so only the seeded filter needs recomputing.
            if (_editCtx.Name == dwo.Name && _editCtx.Row != row && _editCtx.Style == StyleDddw)
            {
                // :L174-L175
                _editCtx.Row = row;
                _editCtx.LastData = string.Empty;

                // :L176-L177
                InitCtxDddwFilter(row, dwo);
                return;
            }

            // :L179-L180  //还原DDDW的原始过滤条件 - "restore the DDDW's original filter condition"
            ApplyFilter(_editCtx.Row, _editCtx.Dwo, string.Empty, show: false);
        }

        // :L183-L196  THE THIRTEEN-FIELD RESET, field by field and in the oracle's order. Written out
        //             rather than replaced by a fresh structure so that the ORDER survives - and
        //             because the oracle mutates its structure in place and never assigns it whole
        //             (see the remarks on DddwData).
        _editCtx.Valid = false;
        _editCtx.LastData = string.Empty;
        _editCtx.Row = row;
        _editCtx.Dwo = dwo;
        _editCtx.Name = dwo.Name;
        _editCtx.Style = string.Empty;
        _editCtx.Dddw.Child = emptyChild;
        _editCtx.Dddw.DataColName = string.Empty;
        _editCtx.Dddw.DispColName = string.Empty;
        _editCtx.Dddw.OrgFilter = string.Empty;
        _editCtx.Dddw.Filter = string.Empty;
        _editCtx.Dddw.InputFilter = string.Empty;
        _editCtx.Dddw.Hwnd = 0;
        _editCtx.Dddw.RowHeight = 0;

        // :L198-L199  A DISPLAY-ONLY COLUMN IS INERT. The context keeps the row, the object and the
        //             name - so a later event can tell WHICH cell was inert - but stays invalid, and
        //             the style is deliberately left empty rather than read.
        string sProp = host.Describe(_editCtx.Name + DisplayOnlyPropertySuffix);
        if (sProp == AffirmativeAnswer)
        {
            return;
        }

        // :L200  _editCtx.style = #DataWindow.Describe(_editCtx.name + ".Edit.Style")
        _editCtx.Style = host.Describe(_editCtx.Name + EditStylePropertySuffix);

        // :L201  if _editCtx.style = "!" then return
        //        The INVALID-EXPRESSION sentinel, which means the column has no edit style to report.
        //        NOTE that the sentinel is LEFT IN THE FIELD rather than normalised away, so a later
        //        `Style <> "dddw"` test sees "!" and not "" - a distinction the oracle preserves and
        //        this port therefore preserves too.
        if (_editCtx.Style == InvalidExpressionSentinel)
        {
            return;
        }

        // :L203-L228  choose case _editCtx.style - only two styles are searchable.
        switch (_editCtx.Style)
        {
            case StyleDddw:
            {
                // :L205-L206  A NON-EDITABLE DROP-DOWN CANNOT BE SEARCHED - there is nowhere to
                //             type. Lower() is applied to the ANSWER here, unlike :L199's
                //             case-sensitive test of "yes"; the asymmetry is the oracle's.
                sProp = host.Describe(_editCtx.Name + DddwAllowEditPropertySuffix);
                if (Lower(sProp) == NegativeAnswer)
                {
                    return;
                }

                // :L207-L208
                _editCtx.Dddw.DispColName = host.Describe(_editCtx.Name + DddwDisplayColumnPropertySuffix);
                if (_editCtx.Dddw.DispColName == InvalidExpressionSentinel)
                {
                    return;
                }

                // :L209-L210
                _editCtx.Dddw.DataColName = host.Describe(_editCtx.Name + DddwDataColumnPropertySuffix);
                if (_editCtx.Dddw.DataColName == InvalidExpressionSentinel)
                {
                    return;
                }

                // :L211  if #DataWindow.GetChild(_editCtx.name,ref _editCtx.dddw.object) <> 1 then
                //        THE FIELD IS WRITTEN BEFORE THE RESULT IS TESTED, because PowerScript
                //        passes the field itself by reference and the callee has already written to
                //        it by the time the comparison runs.
                IDataWindowChild? child = _editCtx.Dddw.Child;
                int childResult = host.GetChild(_editCtx.Name, ref child);
                _editCtx.Dddw.Child = child;
                if (childResult != 1)
                {
                    return;
                }

                IDataWindowChild resolved = RequireChild();

                // :L212-L213  THE DISPLAY COLUMN MUST BE TEXT. A four-character prefix comparison,
                //             so char(50) matches; see ColumnTypePrefixLength.
                sProp = resolved.Describe(_editCtx.Dddw.DispColName + ColumnTypePropertySuffix);
                if (Left(sProp, ColumnTypePrefixLength) != CharColumnTypePrefix)
                {
                    return;
                }

                // :L214  _editCtx.dddw.hWnd = Handle(_editCtx.dddw.object)
                //        DEFERRED, as at :L171. Nothing emitted.

                // :L215  _editCtx.dddw.rowHeight = Long(...Describe("DataWindow.Detail.Height"))
                //        A captured property read, not a computed geometry; see DddwData.RowHeight.
                _editCtx.Dddw.RowHeight = ParseLegacyLong(resolved.Describe(DetailHeightProperty));

                // :L216-L217  capture the child's own filter, normalising the "?" sentinel
                _editCtx.Dddw.OrgFilter = resolved.Describe(TableFilterProperty);
                if (_editCtx.Dddw.OrgFilter == UndeterminableSentinel)
                {
                    _editCtx.Dddw.OrgFilter = string.Empty;
                }

                // :L218  _editCtx.dddw.filter = _editCtx.dddw.orgFilter
                //        SEEDING THE CHANGE DETECTOR, which is what makes the first composition
                //        that merely reproduces the original filter register as NO CHANGE at :L382.
                _editCtx.Dddw.Filter = _editCtx.Dddw.OrgFilter;

                // :L219-L221  A CHILD WITH NO SORT OF ITS OWN GETS ONE, ascending by display
                //             column - which is what makes "first match wins" at :L111 predictable.
                //             A child that already carries a sort keeps it.
                if (resolved.Describe(TableSortProperty) == UndeterminableSentinel)
                {
                    _ = resolved.SetSort(_editCtx.Dddw.DispColName + AscendingSortSuffix);
                }

                // :L222-L223
                _editCtx.Valid = true;
                InitCtxDddwFilter(row, dwo);

                break;
            }

            case StyleDdlb:
            {
                // :L225-L227  A ddlb needs nothing but editability: there is no child DataWindow,
                //             no display or data column and no filter, so every DddwData field
                //             stays at its reset value - which is load bearing, because the two
                //             empty column names then compare EQUAL and short-circuit both the
                //             data-column clause at :L327 and the first auto-determination arm at
                //             :L261.
                sProp = host.Describe(_editCtx.Name + EditAllowEditPropertySuffix);
                if (Lower(sProp) == NegativeAnswer)
                {
                    return;
                }

                _editCtx.Valid = true;

                break;
            }

            default:
                // NO DEFAULT ARM IN THE ORACLE [:L203-L228]. Any other style simply leaves the context
                // invalid, which the reset above already established. Present only because C# has no
                // fall-out form for a switch statement.
                break;
        }
    }

    /// <summary>
    /// Seeds the child's filter for a freshly focused cell - the port of
    /// <c>_of_initctxdddwfilter(readonly long row, readonly dwobject dwo)</c>
    /// (<c>n_cst_dwsvc_dropdownsearch.sru:L231-L249</c>).
    /// </summary>
    /// <param name="row">The ONE-BASED row being edited.</param>
    /// <param name="dwo">The column being edited.</param>
    /// <remarks>
    /// <para>
    /// THE LIVE BODY IS TWO STATEMENTS, and the comment above them says what they are for:
    /// <c>//始终还原为初始的过滤条件</c> [<c>:L236</c>] - "always restore to the initial filter
    /// condition".
    /// </para>
    /// <para>
    /// <b>THE SUBTLETY THAT MUST SURVIVE.</b> <c>_of_GetFilter("")</c> [<c>:L247</c>] skips the ENTIRE
    /// clause builder, because the builder is wrapped in <c>if data &lt;&gt; ""</c> [<c>:L315</c>] - but
    /// it STILL RAISES <c>onddsgetfilter</c> with empty data [<c>:L342</c>]. That is not incidental: it
    /// is precisely how an application supplies an INITIAL filter for the cell, and dropping the call
    /// on the empty path would remove a documented extension point while leaving every test that only
    /// exercises typed input passing.
    /// </para>
    /// <para>
    /// <c>internal</c> rather than the oracle's <c>private</c>; see DECISION 8.
    /// </para>
    /// </remarks>
    internal void InitCtxDddwFilter(in long row, IDataWindowObject? dwo)
    {
        // :L233  if Not _editCtx.valid then return
        if (!_editCtx.Valid)
        {
            return;
        }

        // :L234  if _editCtx.Style <> "dddw" then return
        if (_editCtx.Style != StyleDddw)
        {
            return;
        }

        // :L237-L246  DORMANT, COMMENTED OUT IN THE ORACLE and dated 2017-09-01. Reproduced as a
        //             comment and NOT revived (constraint C-B). It would have SEEDED THE FILTER FROM
        //             THE CELL'S CURRENT VALUE:
        //
        //                 sVal = _of_LookupDisplay(row,_editCtx.Name)
        //                 if sVal <> "" and #DataWindow.GetItemStatus(row,_editCtx.Name,Primary!) =
        //                                   DataModified! then
        //                     //如果当前有值并且这个值在下拉列表中没有完全匹配的项,则使用该值做为模糊过滤的初始条件
        //                     if _editCtx.dddw.object.Find(_editCtx.dddw.dispColName + " = '" + sVal +
        //                                                  "'",1,_editCtx.dddw.object.RowCount()) = 0 then
        //                         sFilter = _of_GetFilter(sVal)
        //                     end if
        //                 end if
        //
        //             - "if there is a current value and that value has no exactly matching item in the
        //             drop-down list, use it as the initial condition for fuzzy filtering". Note what
        //             its revival would drag in: _of_LookupDisplay (the expression evaluator), an item
        //             status read against the Primary buffer, and the child's Find - none of which the
        //             live path needs, and NONE OF WHICH IS DECLARED ON THE HOST CONTRACT for this
        //             service's sake. That absence is deliberate.

        // :L247  sFilter = _of_GetFilter("")
        string sFilter = GetFilter(string.Empty);

        // :L248  _of_Filter(row,dwo,sFilter,false)
        //        show is FALSE here - a newly focused cell does not pop its drop-down open.
        ApplyFilter(row, dwo, sFilter, show: false);
    }

    /// <summary>
    /// Decides whether the rows this filter removed should nonetheless be shown, relocated to the end -
    /// the port of <c>_of_isshowfilteredrows()</c>
    /// (<c>n_cst_dwsvc_dropdownsearch.sru:L260-L264</c>).
    /// </summary>
    /// <returns>
    /// <see cref="ShowFilteredRows"/> when it has an explicit value; otherwise the auto-determined
    /// answer.
    /// </returns>
    /// <remarks>
    /// <para>
    /// FOUR ARMS EVALUATED IN THE ORACLE'S ORDER, AND THE ORDER IS BEHAVIOUR because the arms disagree:
    /// </para>
    /// <list type="number">
    /// <item><description>
    /// <c>:L260</c> an explicit setting short-circuits everything, true or false alike.
    /// </description></item>
    /// <item><description>
    /// <c>:L261</c> when the display and data columns are THE SAME COLUMN, false - there is no second
    /// column whose text could be corrupted, which is the problem the relocation exists to solve. This
    /// arm is also what makes a <c>ddlb</c> context answer false, both names being empty.
    /// </description></item>
    /// <item><description>
    /// <c>:L262</c> a GRID presentation style, true - a grid shows every row's text, so filtering it is
    /// the case that visibly misbehaves.
    /// </description></item>
    /// <item><description>
    /// <c>:L263</c> otherwise, more than one row AND a vertical scroll bar. A single-row DataWindow has
    /// nothing to corrupt and one without a scroll bar cannot show the relocated rows anyway.
    /// </description></item>
    /// </list>
    /// <para>
    /// The scroll-bar term is read as a DATAWINDOW ATTRIBUTE through the host contract; see
    /// <see cref="VerticalScrollBarProperty"/> and <see cref="DescribesAffirmative"/>. The conjunction
    /// short-circuits, so the attribute is not read at all for a DataWindow with one row or fewer -
    /// which matches PowerScript's own evaluation of <c>and</c>.
    /// </para>
    /// <para>
    /// <c>internal</c> rather than the oracle's <c>private</c>; see DECISION 8.
    /// </para>
    /// </remarks>
    internal bool IsShowFilteredRows()
    {
        // :L260  if Not IsNull(#ShowFilteredRows) then return #ShowFilteredRows
        if (ShowFilteredRows is not null)
        {
            return ShowFilteredRows.Value;
        }

        // :L261  if _editCtx.dddw.dispColName = _editCtx.dddw.dataColName then return false
        if (_editCtx.Dddw.DispColName == _editCtx.Dddw.DataColName)
        {
            return false;
        }

        // :L262  if _of_GetStyle() = STYLE_GRID then return true
        if (GetPresentationStyle() == STYLE_GRID)
        {
            return true;
        }

        // :L263  return (#DataWindow.RowCount() > 1 and #DataWindow.VScrollBar)
        DataWindowServiceHost host = RequireHost();
        return host.RowCount() > 1 && DescribesAffirmative(host.Describe(VerticalScrollBarProperty));
    }

    /// <summary>
    /// Sets whether filtered rows are shown - the port of
    /// <c>of_setshowfilteredrows(readonly boolean show)</c>
    /// (<c>n_cst_dwsvc_dropdownsearch.sru:L266-L285</c>).
    /// </summary>
    /// <param name="show">
    /// <see langword="true"/> to relocate filtered rows into the primary buffer,
    /// <see langword="false"/> not to.
    /// </param>
    /// <returns><c>RetCode.OK</c>, always (<c>:L284</c>).</returns>
    /// <remarks>
    /// <para>
    /// TWO STATEMENTS IN THE ORACLE AND TWO HERE [<c>:L283-L284</c>]: assign, return success. It takes
    /// a NON-NULLABLE bool because <c>readonly boolean show</c> is what the oracle declares - so this
    /// setter can only ever move the property OUT of its three-state null and never back into it. That
    /// is the oracle's own limitation, faithfully reproduced: the null exists only as the constructed
    /// default [<c>:L503</c>], and there is no <c>SetNull</c> path exposed to a caller.
    /// </para>
    /// <para>
    /// It takes effect on the NEXT filter application rather than immediately, because
    /// <see cref="IsShowFilteredRows"/> is consulted inside the apply path [<c>:L396</c>] and this
    /// setter triggers nothing.
    /// </para>
    /// </remarks>
    public long SetShowFilteredRows(in bool show)
    {
        // :L283  #ShowFilteredRows = show
        ShowFilteredRows = show;

        // :L284  return RetCode.OK
        return RetCode.OK;
    }

    /// <summary>
    /// Sets which columns the filter matches against - the port of
    /// <c>of_setfiltertype(readonly unsignedlong ntype)</c>
    /// (<c>n_cst_dwsvc_dropdownsearch.sru:L287-L306</c>).
    /// </summary>
    /// <param name="nType">
    /// A combinable bitmask over <see cref="FILTER_DISP"/>, <see cref="FILTER_DISP_PY"/> and
    /// <see cref="FILTER_DATA"/>. ACCEPTED WHATEVER IT IS - see the remarks.
    /// </param>
    /// <returns><c>RetCode.OK</c>, always (<c>:L305</c>).</returns>
    /// <remarks>
    /// <para>
    /// <b>DEFECT 4, PRESERVED.</b> The oracle's body is EXACTLY two statements - <c>#FilterType =
    /// nType</c> then <c>return RetCode.OK</c> [<c>:L304-L305</c>] - with NO VALIDATION WHATSOEVER.
    /// Zero is accepted, and so is any value with bits nothing reads. Zero is the interesting one,
    /// because combined with DEFECT 1 it produces an EMPTY filter with no diagnostic anywhere: all
    /// three bit tests fail, the wrap at <c>:L340</c> is skipped because the expression is empty, and
    /// the caller receives success. No guard is added here and <c>E_INVALID_ARGUMENT</c> is never
    /// returned (constraint C-B).
    /// </para>
    /// <para>
    /// THIS IS THE EXACT INVERSE OF THE ROW-SELECT SERVICE, whose own style setter DOES reject zero
    /// [<c>n_cst_dwsvc_rowselect.sru</c>], and the asymmetry between the two must not be harmonised in
    /// either direction. Configuration/DataServicesOptions.cs records the same asymmetry by validating
    /// <c>RowSelect.Style</c> and deliberately not validating <c>DropDownSearch.FilterType</c>.
    /// </para>
    /// <para>
    /// It takes effect on the NEXT composition, because <see cref="GetFilter(in string)"/> reads the
    /// property when it runs and this setter triggers no recomposition.
    /// </para>
    /// </remarks>
    public long SetFilterType(in uint nType)
    {
        // :L304  #FilterType = nType
        FilterType = nType;

        // :L305  return RetCode.OK
        return RetCode.OK;
    }

    // ==========================================================================================
    //  THE FILTER BUILDER - THE CENTRAL DELIVERABLE OF THIS FILE
    //                                            n_cst_dwsvc_dropdownsearch.sru:L313-L345
    //  ----------------------------------------------------------------------------------------
    //  PARITY IS BYTE-EXACT EMITTED EXPRESSION TEXT. Not "an equivalent expression", not "a filter
    //  that selects the same rows" - the same characters in the same order, because the composed text
    //  is what a characterization recording captures and what the DataWindow expression evaluator
    //  parses. Every literal fragment below is written inline rather than composed from named parts,
    //  deliberately, so that the shape of the emitted string is readable at the site that emits it.
    // ==========================================================================================

    /// <summary>
    /// Composes the drop-down filter expression for one search input, then gives the application the
    /// chance to replace it - the port of <c>_of_getfilter(readonly string data)</c>
    /// (<c>n_cst_dwsvc_dropdownsearch.sru:L313-L345</c>).
    /// </summary>
    /// <param name="data">
    /// What the user has typed. THE EMPTY STRING IS A LIVE AND MEANINGFUL INPUT: it skips the whole
    /// clause builder but still raises the semantic event, which is how an application supplies an
    /// initial filter - see the remarks on <see cref="InitCtxDddwFilter(in long, IDataWindowObject?)"/>.
    /// </param>
    /// <returns>
    /// The filter expression, or the empty string when no clause was produced and no handler supplied
    /// one. Whatever the handler left in the <c>ref</c> parameter is returned verbatim [<c>:L344</c>].
    /// </returns>
    /// <remarks>
    /// <para>
    /// THE PIPELINE, IN THE ORACLE'S ORDER, AND THE ORDER IS OBSERVABLE. <c>Lower</c> first
    /// [<c>:L316</c>], THEN <c>Trim</c>, THEN the space-to-<c>%</c> substitution [<c>:L317</c>]. So a
    /// user typing <c>"ab cd"</c> produces the LIKE pattern <c>%ab%cd%</c> - INTERIOR SPACES BECOME
    /// WILDCARDS, which is a feature and not a bug, and repeated spaces become repeated wildcards.
    /// </para>
    /// <para>
    /// THE TWO SEARCH TEXTS ARE DELIBERATELY DIFFERENT AND THE ASYMMETRY IS OBSERVABLE IN OUTPUT. The
    /// two LIKE clauses use the %-substituted text; the Pinyin clause uses the merely lower-cased text
    /// with its spaces intact [<c>:L323</c>]; and the numeric clause uses the RAW argument, neither
    /// lower-cased nor trimmed nor substituted [<c>:L334</c>]. Three different strings from one input.
    /// </para>
    /// <para>
    /// <b>DEFECT 1, AT ITS SITE.</b> Only the display clause ASSIGNS [<c>:L319</c>]; every later clause
    /// APPENDS with a leading <c>" OR "</c>. With the display bit clear the result therefore begins
    /// with a dangling operator - <c>FilterType == FILTER_DISP_PY</c> yields
    /// <c>"( OR PinyinFirstLetterLike(...))"</c>, a syntactically broken expression. Reproduced exactly.
    /// </para>
    /// <para>
    /// <b>DEFECT 2, AT ITS SITE.</b> Five values are spliced into expression text with no escaping of
    /// any kind: both column names, the %-substituted text, the lower-cased text and the raw argument.
    /// A single quote in the input breaks or injects into the filter. AAP 0.6.4 names <c>:L323</c>
    /// explicitly as a filter-injection site and requires it be DOCUMENTED rather than silently
    /// changed, and constraint C-B forbids the correction outright. NOT escaped, NOT quoted, NOT
    /// sanitised, NOT parameterised - and note that AAP 0.7.2's parameterised-SQL requirement does not
    /// reach here at all: these are DataWindow filter expressions, not SQL.
    /// </para>
    /// <para>
    /// STRICTLY SYNCHRONOUS. The semantic event's result travels through a <c>ref string</c>, which has
    /// no asynchronous representation, so AAP 0.6.1.4 assigns this capability area pattern (b) - request
    /// and response with no reordering permitted. The caller blocks on the produced filter.
    /// </para>
    /// <para>
    /// <c>internal</c> rather than the oracle's <c>private</c>, so that constraint C-H's requirement -
    /// that this be testable as a pure function with no DataWindow, no child, no database and no UI -
    /// is met with a faked host; see DECISION 8.
    /// </para>
    /// </remarks>
    internal string GetFilter(in string data)
    {
        // PowerScript locals [:L313], strings initialised by the runtime to "".
        string sFilter = string.Empty;

        // :L315  if data <> "" then
        if (data != string.Empty)
        {
            // :L316  sData = Lower(data)
            string sData = Lower(data);

            // :L317  sDataLike = ReplaceAll(Trim(sData)," ","%",true)
            //        The FOUR-ARGUMENT match-case arity of replaceall.srf; `true` is matchCase, which is
            //        immaterial for a space but is passed because the oracle passes it.
            string sDataLike = Text.ReplaceAll(Trim(sData), SearchTextSeparator, SearchTextWildcard, true);

            // :L318-L320  THE DISPLAY CLAUSE - THE ONLY ONE THAT ASSIGNS. See DEFECT 1.
            if (Bits.BitTest(FilterType, FILTER_DISP))
            {
                // :L319
                sFilter = "(Lower(" + _editCtx.Dddw.DispColName + ") LIKE '%" + sDataLike + "%')";
            }

            // :L321-L325  THE PINYIN CLAUSE, DOUBLY GUARDED.
            if (Bits.BitTest(FilterType, FILTER_DISP_PY))
            {
                // :L322  if Match(data,"[a-zA-Z]") then
                //        Tested against the RAW argument, not against either derived text. Pinyin first
                //        letters are Latin letters, so an input with none of them could never match and
                //        the clause is omitted entirely rather than emitted and left to fail.
                if (MatchesAsciiLetter(data))
                {
                    // :L323  sFilter += " OR PinyinFirstLetterLike(" + dispColName + ",'" + sData + "',7)"
                    //        THE FUNCTION NAME IS EMITTED AS TEXT AND NOTHING IS CALLED - DECISION 4.
                    //        The flags value comes from options, whose declared default reproduces the
                    //        oracle's hardcoded 7; DECISION 5 records why that 7 is unrelated to
                    //        FILTER_ALL's 7.
                    sFilter += " OR " + PinyinFunctionName + "(" + _editCtx.Dddw.DispColName + ",'" + sData
                        + "'," + _pinyinMatchFlags.ToString(CultureInfo.InvariantCulture) + ")";
                }
            }

            // :L326-L338  THE DATA CLAUSE, TYPE-DIRECTED.
            if (Bits.BitTest(FilterType, FILTER_DATA))
            {
                // :L327  if _editCtx.dddw.dataColName <> _editCtx.dddw.dispColName then
                //        A drop-down whose data column IS its display column has nothing extra to match,
                //        and a ddlb context - both names empty - short-circuits here too.
                if (_editCtx.Dddw.DataColName != _editCtx.Dddw.DispColName)
                {
                    // :L328  sProp = _editCtx.dddw.object.Describe(dataColName + ".ColType")
                    IDataWindowChild child = RequireChild();
                    string sProp = child.Describe(_editCtx.Dddw.DataColName + ColumnTypePropertySuffix);

                    // :L329  choose case Left(sProp,4)
                    //        A FOUR-CHARACTER PREFIX and a CASE-SENSITIVE comparison, both as the oracle
                    //        has them; see ColumnTypePrefixLength.
                    switch (Left(sProp, ColumnTypePrefixLength))
                    {
                        case CharColumnTypePrefix:
                            // :L331  a text data column matches like the display column does
                            sFilter += " OR (Lower(" + _editCtx.Dddw.DataColName + ") LIKE '%" + sDataLike + "%')";
                            break;

                        case "numb":
                        case "deci":
                        case "long":
                        case "ulon":
                            // :L333  if IsNumber(data) then
                            //        Without this guard the emitted expression would compare a numeric
                            //        column against unquoted text and fail to parse.
                            if (IsNumber(data))
                            {
                                // :L334  EQUALITY, NOT LIKE, and the RAW argument - neither lower-cased
                                //        nor quoted. The one place a caller's text enters the expression
                                //        completely untransformed.
                                sFilter += " OR (" + _editCtx.Dddw.DataColName + " = " + data + ")";
                            }

                            break;

                        default:
                            // NO DEFAULT ARM IN THE ORACLE [:L329-L336]. A date, time or boolean data
                            // column therefore contributes NOTHING - not an error, not a fallback
                            // clause, silence. Present only because C# has no fall-out form for a
                            // switch statement.
                            break;
                    }
                }
            }
        }

        // :L340  if sFilter <> "" then sFilter = "(" + sFilter + ")"
        //        EXACTLY ONE WRAPPING PAIR around whatever was composed - including around a string that
        //        begins with the dangling " OR " of DEFECT 1, which is what makes that defect visible in
        //        the output rather than merely latent.
        if (sFilter != string.Empty)
        {
            sFilter = "(" + sFilter + ")";
        }

        // :L342  #DataWindow.Event OnDDSGetFilter(_editCtx.row,_editCtx.dwo,data,ref sFilter)
        //        RAISED UNCONDITIONALLY - including on the empty-data path, which is the documented
        //        extension point described on InitCtxDddwFilter. The event is declared
        //        se_cst_dw.sru:L13 with a `ref string` out-parameter and no return type.
        RequireHost().OnDDSGetFilter(_editCtx.Row, _editCtx.Dwo, data, ref sFilter);

        // :L344  return sFilter
        return sFilter;
    }

    /// <summary>
    /// Whether the user has typed a search restriction that is currently applied - the port of
    /// <c>of_hasfilter()</c> (<c>n_cst_dwsvc_dropdownsearch.sru:L347-L366</c>).
    /// </summary>
    /// <returns>
    /// <see langword="true"/> only when the context is valid, the style is <c>dddw</c>, and the
    /// user-derived half of the filter is non-empty.
    /// </returns>
    /// <remarks>
    /// IT TESTS THE INPUT FILTER AND NOT THE COMPOSED ONE [<c>:L365</c>], which is the whole reason the
    /// two are stored separately. A child that arrived carrying its own filter has a non-empty COMPOSED
    /// filter from the moment the context is built [<c>:L218</c>], so testing that instead would report
    /// a user restriction where there is none. A <c>ddlb</c> context always answers
    /// <see langword="false"/> [<c>:L364</c>] because it has no drop-down to filter.
    /// </remarks>
    public bool HasFilter()
    {
        // :L363  if Not _editCtx.valid then return false
        if (!_editCtx.Valid)
        {
            return false;
        }

        // :L364  if _editCtx.style <> "dddw" then return false
        if (_editCtx.Style != StyleDddw)
        {
            return false;
        }

        // :L365  return _editCtx.dddw.inputFilter <> ""
        return _editCtx.Dddw.InputFilter != string.Empty;
    }

    /// <summary>
    /// Composes the effective filter, applies it to the child, partitions the rows and notifies the
    /// host - the port of
    /// <c>_of_filter(readonly long row, readonly dwobject dwo, readonly string filter, readonly boolean show)</c>
    /// (<c>n_cst_dwsvc_dropdownsearch.sru:L368-L417</c>).
    /// </summary>
    /// <param name="row">The ONE-BASED row being edited, forwarded to the semantic event.</param>
    /// <param name="dwo">The column being edited, forwarded to the semantic event. May be null.</param>
    /// <param name="filter">
    /// The USER-DERIVED filter half, which the empty string clears. Stored as the input filter and
    /// conjoined with the child's original filter to form what is actually applied.
    /// </param>
    /// <param name="show">
    /// Whether to pop the drop-down open afterwards. CARRIED BUT NOT ACTED ON - its only effect,
    /// <c>_of_ShowDDDW</c>, is deferred; see the annotation at the end of the body.
    /// </param>
    /// <remarks>
    /// <para>
    /// THE COMPOSITION HAS THREE ARMS [<c>:L372-L380</c>] and they are not symmetrical: a non-empty
    /// filter with an original filter present is CONJOINED as
    /// <c>"(" + orgFilter + ") AND (" + filter + ")"</c>; a non-empty filter with no original filter is
    /// used alone; and an EMPTY filter RESTORES the original filter rather than clearing the child. That
    /// third arm is what makes the reset path at <c>:L158</c> and <c>:L180</c> a restore rather than a
    /// wipe.
    /// </para>
    /// <para>
    /// EVERYTHING ELSE IS INSIDE A CHANGE GUARD [<c>:L382</c>]. When the composed expression equals the
    /// one already applied, the oracle touches NOTHING - no redraw, no filter, no sort, no move, no
    /// event - which is what keeps a keystroke that does not alter the restriction from re-filtering the
    /// child. <see cref="Partition"/> is therefore left alone on that path too; see its remarks.
    /// </para>
    /// <para>
    /// <b>THE COUNT-AND-EVENT ORDERING IS CONTRACT, NOT INCIDENTAL.</b> Both counts are captured BEFORE
    /// the relocation [<c>:L393-L394</c>] and the semantic event is raised AFTER it [<c>:L408</c>],
    /// carrying those pre-move values. The oracle states the requirement twice, in identical comments at
    /// <c>:L392</c> and <c>:L407</c>: <c>//*保证行数是被移动缓冲区之前的值</c> - "ensure the row count is
    /// the value from before the buffer move". Re-reading either count at raise time would report the
    /// post-move totals, which for the relocating branch is a different pair of numbers entirely.
    /// </para>
    /// <para>
    /// <b>ONE NARROWING, DOCUMENTED (constraint C-K).</b> The oracle suppresses redrawing only when the
    /// child's window is visible - <c>bRedraw = true</c> under <c>if IsWindowVisible(_editCtx.dddw.hWnd)</c>
    /// [<c>:L385-L388</c>], restored under the same flag at <c>:L409-L411</c>. That precondition is a
    /// <c>user32.dll</c> prototype [<c>:L41-L44</c>] which AAP 0.6.5 places out of scope as
    /// presentational, and a headless service has no window to interrogate. The suppression is therefore
    /// applied UNCONDITIONALLY inside the changed branch and the <c>bRedraw</c> flag disappears with its
    /// precondition. The narrowing is safe in the direction that matters: suppressing repainting for a
    /// child that is not on screen has no observable effect, whereas NOT suppressing it for one that is
    /// would flicker.
    /// </para>
    /// <para>
    /// <c>internal</c> rather than the oracle's <c>private</c>; see DECISION 8.
    /// </para>
    /// </remarks>
    internal void ApplyFilter(in long row, IDataWindowObject? dwo, in string filter, in bool show)
    {
        // :L372  sFilter = filter
        string sFilter = filter;

        // :L374-L380  the three composition arms
        if (filter != string.Empty)
        {
            // :L375-L377
            if (_editCtx.Dddw.OrgFilter != string.Empty)
            {
                sFilter = "(" + _editCtx.Dddw.OrgFilter + ") AND (" + filter + ")";
            }
        }
        else
        {
            // :L379  AN EMPTY FILTER RESTORES THE ORIGINAL, it does not clear the child.
            sFilter = _editCtx.Dddw.OrgFilter;
        }

        // :L382  if sFilter <> _editCtx.dddw.filter then   - THE CHANGE GUARD
        if (sFilter == _editCtx.Dddw.Filter)
        {
            return;
        }

        // :L383  _editCtx.dddw.filter = sFilter
        _editCtx.Dddw.Filter = sFilter;

        // :L384  _editCtx.dddw.inputFilter = filter   //保存当前录入的过滤条件
        //        "save the filter condition currently entered" - the USER'S half, not the composed one.
        _editCtx.Dddw.InputFilter = filter;

        IDataWindowChild child = RequireChild();

        // :L385-L388  SetRedraw(false), applied unconditionally; the IsWindowVisible precondition is
        //             deferred and the bRedraw flag it fed goes with it. See the remarks.
        _ = child.SetRedraw(false);

        // :L389-L391
        _ = child.SetFilter(sFilter);
        _ = child.Filter();
        _ = child.Sort();

        // :L392-L394  //*保证行数是被移动缓冲区之前的值
        //             CAPTURED BEFORE ANY MOVE. These two values are what the event carries.
        long nRowCnt = child.RowCount();
        long nFilteredCnt = child.FilteredCount();

        // :L395-L406  //处理过滤数据的显示状态 - "handle the display state of the filtered data"
        bool relocated = IsShowFilteredRows();
        bool relocationSucceeded = false;
        long relocatedRowCount = 0;
        long hiddenRowEnd = 0;

        if (relocated)
        {
            // :L397-L400  //显示没被过滤的行 - "show the rows that were not filtered out"
            //             if nRowCnt > 0 then SetDetailHeight(1,nRowCnt,_editCtx.dddw.rowHeight)
            //             DEFERRED - ROW GEOMETRY (DECISION 3). Nothing is emitted; the three arguments
            //             travel as VisibleRowStart, VisibleRowEnd and VisibleRowHeight, and the
            //             `nRowCnt > 0` guard is expressed by an EMPTY visible range.

            // :L401-L402  //将被过滤的数据放在末尾(否则会影响其它行显示的文本)
            //             "put the filtered data at the end (otherwise it affects the text other rows
            //             display)" - THE RELOCATION ITSELF, a buffer operation, so it ships. The
            //             filtered count is RE-READ LIVE here, exactly as the oracle re-reads it, rather
            //             than reusing the captured value.
            relocatedRowCount = child.FilteredCount();
            relocationSucceeded = child.RowsMoveFilterToPrimary(1, relocatedRowCount, nRowCnt + 1) == 1;

            if (relocationSucceeded)
            {
                // :L403-L404  //隐藏被过滤的行 - "hide the filtered rows"
                //             SetDetailHeight(nRowCnt + 1,_editCtx.dddw.object.RowCount(),0)
                //             DEFERRED - ROW GEOMETRY (DECISION 3). The row count is read LIVE here and
                //             is a POST-MOVE value, which is why it is captured separately from nRowCnt.
                hiddenRowEnd = child.RowCount();
            }
        }

        Partition = new DropDownSearchFilterPartition
        {
            RowCount = nRowCnt,
            FilteredCount = nFilteredCnt,
            Relocated = relocated,
            RelocationSucceeded = relocationSucceeded,
            RelocatedRowCount = relocatedRowCount,
            VisibleRowStart = relocated ? 1 : 0,
            VisibleRowEnd = relocated ? nRowCnt : 0,
            VisibleRowHeight = relocated ? _editCtx.Dddw.RowHeight : 0,
            HiddenRowStart = relocationSucceeded ? nRowCnt + 1 : 0,
            HiddenRowEnd = hiddenRowEnd,
            HiddenRowHeight = 0,
        };

        // :L407-L408  //*保证行数是被移动缓冲区之前的值
        //             #DataWindow.Event OnDDSFiltered(row,dwo,nRowCnt,nFilteredCnt)
        //             RAISED AFTER THE MOVE, CARRYING THE PRE-MOVE COUNTS. See the remarks.
        RequireHost().OnDDSFiltered(row, dwo, nRowCnt, nFilteredCnt);

        // :L409-L411  if bRedraw then SetRedraw(true)   - restored unconditionally, as above.
        _ = child.SetRedraw(true);

        // :L412-L415  //显示DDDW - "show the DDDW"
        //             if show then _of_ShowDDDW()
        //             DEFERRED IN FULL. _of_showdddw [:L251-L258] adjusts an OS window's height and
        //             calls Win32.ShowWindow(hWnd,8) //SW_SHOWNA - there is no data in it, only window
        //             management, which AAP 0.4.4 assigns to the reserved /v1/design/** extension
        //             point. The `show` argument is still accepted and still means what it meant, so the
        //             deferred half receives the intent rather than having to re-derive it; nothing is
        //             emitted here and no placeholder stands in for it (constraint C-D).
        _ = show;
    }

    // ==========================================================================================
    //  THE THREE of_updatedddwfilter ARITIES     n_cst_dwsvc_dropdownsearch.sru:L308, L419, L458
    //  ----------------------------------------------------------------------------------------
    //  The application calls these when the ORIGINAL filter of a shared child DataWindow changes
    //  underneath an active edit - for instance when a master column's value re-scopes a detail
    //  drop-down. The user's own search restriction survives, because it was never merged into the
    //  original filter [:L384].
    // ==========================================================================================

    /// <summary>
    /// Updates the original filter of the column currently being edited, re-reading it from the child
    /// when none is given - the port of
    /// <c>of_updatedddwfilter(readonly string colname, string filter, readonly boolean dofilter)</c>
    /// (<c>n_cst_dwsvc_dropdownsearch.sru:L419-L456</c>). THE PRIMARY ARITY; the other two delegate here.
    /// </summary>
    /// <param name="colName">
    /// The column whose drop-down filter changed. COMPARED CASE-INSENSITIVELY IN ONE DIRECTION ONLY -
    /// see the remarks.
    /// </param>
    /// <param name="filter">
    /// The new original filter, or <see langword="null"/> to re-read the child's current one. THE NULL
    /// IS MEANINGFUL and selects a different branch [<c>:L443</c>]; the empty string does not.
    /// </param>
    /// <param name="doFilter">
    /// Whether to re-apply the user's search restriction on top of the new original filter immediately.
    /// </param>
    /// <returns>
    /// <c>RetCode.OK</c> from three of the four guards and from the success path;
    /// <c>RetCode.FAILED</c> from the fourth. See DEFECT 5.
    /// </returns>
    /// <remarks>
    /// <para>
    /// THE FOCUS GUARD [<c>:L438</c>] is <c>if GetFocus() &lt;&gt; #DataWindow then return RetCode.OK</c> -
    /// PowerScript's <c>GetFocus</c> SYSTEM FUNCTION, which answers WHICH OBJECT holds focus, not the
    /// host's identically spelled event. It is ported as
    /// <c>!ReferenceEquals(host.GetFocusedObject(), host)</c>, which is the same identity comparison
    /// against the same host. THE FOCUS FACT THEREFORE ARRIVES THROUGH THE HOST CONTRACT AND NEVER FROM
    /// AN OS CALL - the same route <c>se_cst_dw.sru:L553</c> already uses.
    /// </para>
    /// <para>
    /// <b>THE COLUMN COMPARISON IS ASYMMETRIC ON PURPOSE</b> [<c>:L440</c>]: <c>_editCtx.name &lt;&gt;
    /// Lower(colName)</c> lower-cases the ARGUMENT but uses the STORED name as it was captured from
    /// <c>dwo.name</c> [<c>:L187</c>]. So a caller may pass any casing, but a DataWindow that reports a
    /// mixed-case column name will never match. Preserved exactly; making it symmetric would be a
    /// correction (constraint C-B).
    /// </para>
    /// <para>
    /// <b>DEFECT 5, AT ITS SITE.</b> Four guards, three returning <c>RetCode.OK</c> and the fourth -
    /// the non-<c>dddw</c> style test at <c>:L441</c> - returning <c>RetCode.FAILED</c>, for no
    /// discernible reason: all four describe the same situation, that this call does not apply to the
    /// current context. The divergence is observable through the public return value and is reproduced
    /// verbatim. Note how it compounds with the tri-state return algebra: <c>Predicates.IsSucceeded</c>
    /// accepts <c>OK</c> and rejects <c>FAILED</c>, so a caller testing the result sees a hard failure
    /// for a wrong-style column and silent success for a wrong-named one.
    /// </para>
    /// </remarks>
    public long UpdateDddwFilter(in string colName, string? filter, in bool doFilter)
    {
        DataWindowServiceHost host = RequireHost();

        // :L438  if GetFocus() <> #DataWindow then return RetCode.OK
        if (!ReferenceEquals(host.GetFocusedObject(), host))
        {
            return RetCode.OK;
        }

        // :L439  if Not _editCtx.valid then return RetCode.OK
        if (!_editCtx.Valid)
        {
            return RetCode.OK;
        }

        // :L440  if _editCtx.name <> Lower(colName) then return RetCode.OK   - ASYMMETRIC, see remarks
        if (_editCtx.Name != Lower(colName))
        {
            return RetCode.OK;
        }

        // :L441  if _editCtx.Style <> "dddw" then return RetCode.FAILED   - DEFECT 5
        if (_editCtx.Style != StyleDddw)
        {
            return RetCode.FAILED;
        }

        // :L443-L446  if IsNull(filter) then read the child's own filter, normalising "?"
        //             The by-value parameter is REASSIGNED, exactly as the oracle reassigns it.
        if (filter is null)
        {
            IDataWindowChild child = RequireChild();
            filter = child.Describe(TableFilterProperty);
            if (filter == UndeterminableSentinel)
            {
                filter = string.Empty;
            }
        }

        // :L447-L453  only a CHANGED original filter does anything
        if (_editCtx.Dddw.OrgFilter != filter)
        {
            // :L448
            _editCtx.Dddw.OrgFilter = filter;

            // :L449-L452  //重新过滤数据 - "re-filter the data"
            //             RE-APPLIES THE USER'S OWN HALF, which is why input filter and composed filter
            //             are stored separately. show is false: an application-driven re-scope must not
            //             pop the drop-down open.
            if (doFilter)
            {
                ApplyFilter(_editCtx.Row, _editCtx.Dwo, _editCtx.Dddw.InputFilter, show: false);
            }
        }

        // :L455  return RetCode.OK
        return RetCode.OK;
    }

    /// <summary>
    /// Updates the original filter of the column currently being edited and re-filters immediately -
    /// the port of <c>of_updatedddwfilter(readonly string colname, readonly string filter)</c>
    /// (<c>n_cst_dwsvc_dropdownsearch.sru:L458-L459</c>).
    /// </summary>
    /// <param name="colName">The column whose drop-down filter changed.</param>
    /// <param name="filter">
    /// The new original filter. NON-NULLABLE HERE BY DESIGN: the oracle declares it <c>readonly</c> and
    /// never inspects it for null on this arity, and the caller who wants the re-read-from-the-child
    /// behaviour has the one-argument overload for exactly that. Passing null here is a caller error the
    /// compiler reports.
    /// </param>
    /// <returns>Whatever the primary arity returns.</returns>
    /// <remarks>
    /// A ONE-LINE FORWARD IN THE ORACLE AND A ONE-LINE FORWARD HERE, with <c>doFilter</c> defaulted to
    /// <see langword="true"/> - which the oracle's own argument comment at <c>:L427</c> documents as the
    /// default.
    /// </remarks>
    public long UpdateDddwFilter(in string colName, in string filter)
    {
        // :L458  return of_UpdateDDDWFilter(colName,filter,true)
        return UpdateDddwFilter(colName, filter, true);
    }

    /// <summary>
    /// Re-reads the original filter of the column currently being edited from the child itself, and
    /// re-filters immediately - the port of
    /// <c>of_updatedddwfilter(readonly string colname)</c>
    /// (<c>n_cst_dwsvc_dropdownsearch.sru:L308-L311</c>).
    /// </summary>
    /// <param name="colName">The column whose drop-down filter changed.</param>
    /// <returns>Whatever the primary arity returns.</returns>
    /// <remarks>
    /// THE NULL IS THE WHOLE POINT OF THIS OVERLOAD. The oracle declares a local, calls
    /// <c>SetNull(nvl)</c> on it and passes it [<c>:L308-L310</c>] - three lines whose only purpose is
    /// to reach the branch at <c>:L443</c> that reads the child's CURRENT filter. Passing the empty
    /// string instead would skip that branch and clear the original filter, which is a different
    /// behaviour entirely (AAP 0.4.5.4, DECISION 7).
    /// </remarks>
    public long UpdateDddwFilter(in string colName)
    {
        // :L308-L309  string nvl / SetNull(nvl)
        string? nvl = null;

        // :L310  return of_UpdateDDDWFilter(colName,nvl,true)
        return UpdateDddwFilter(colName, nvl, true);
    }

    // ==========================================================================================
    //  PRIVATE HELPERS
    //  ----------------------------------------------------------------------------------------
    //  Two groups, and the distinction matters. The first two reproduce the oracle's own repeated
    //  idioms - the stale-child re-fetch and the null-object fault - so that each appears once rather
    //  than at every site. The rest port POWERSCRIPT INTRINSICS that Shared.Kernel does not carry;
    //  each states its translation decision explicitly, because an intrinsic ported by guesswork is a
    //  silent behavioural change (constraint C-K). Nothing here invents behaviour: every helper has an
    //  oracle call site named in its documentation.
    // ==========================================================================================

    /// <summary>
    /// Re-obtains the child DataWindow into the edit context, discarding the result - the port of the
    /// idiom the oracle repeats at
    /// <c>n_cst_dwsvc_dropdownsearch.sru:L156-L157</c> and <c>:L169-L170</c>.
    /// </summary>
    /// <param name="host">The host to fetch through.</param>
    /// <remarks>
    /// THE ORACLE'S OWN COMMENT IS THE JUSTIFICATION, and it appears above both call sites:
    /// <c>//*重新获取DataWindowChild（可能会失效）</c> - "re-obtain the DataWindowChild (it may have
    /// become invalid)". The result is DISCARDED at both sites, so a failed fetch leaves whatever the
    /// callee wrote into the field - which is faithful, because PowerScript passes the field itself by
    /// reference and the callee has already written to it by the time the caller could test anything.
    /// The third fetch, at <c>:L211</c>, is NOT routed through here because that one DOES test the
    /// result and returns on failure.
    /// </remarks>
    private void RefetchChild(DataWindowServiceHost host)
    {
        IDataWindowChild? child = _editCtx.Dddw.Child;
        _ = host.GetChild(_editCtx.Name, ref child);
        _editCtx.Dddw.Child = child;
    }

    /// <summary>
    /// The child DataWindow of the column being edited, faulting when there is none.
    /// </summary>
    /// <returns>The child. Never <see langword="null"/>.</returns>
    /// <exception cref="InvalidOperationException">
    /// The edit context holds no child DataWindow.
    /// </exception>
    /// <remarks>
    /// <para>
    /// THIS REPRODUCES A POWERBUILDER FAULT AS A FAIL-FAST, WHICH AAP 0.1.4 REQUIRES BE PRESERVED AS
    /// FAIL-FAST RATHER THAN SOFTENED. Every oracle site this stands in for dereferences
    /// <c>_editCtx.dddw.object</c> unguarded - <c>:L106</c>, <c>:L109</c>, <c>:L328</c>, <c>:L387</c>,
    /// <c>:L389-L391</c>, <c>:L393-L394</c>, <c>:L399</c>, <c>:L402</c>, <c>:L404</c>, <c>:L410</c>,
    /// <c>:L444</c> - so a null there is a null-object error in PowerBuilder too, not a quiet
    /// no-result. Returning an inert child or an empty string instead would convert a structural fault
    /// into a wrong answer.
    /// </para>
    /// <para>
    /// IT IS UNREACHABLE THROUGH THE PORTED PATHS, and that is the point of stating the invariant.
    /// Every one of those sites is gated - directly or transitively - on <c>_editCtx.valid</c> together
    /// with a <c>dddw</c> style, and the only path that sets both [<c>:L211-L222</c>] returns early
    /// unless <c>GetChild</c> answered <c>1</c>.
    /// </para>
    /// </remarks>
    private IDataWindowChild RequireChild()
    {
        return _editCtx.Dddw.Child
            ?? throw new InvalidOperationException(
                "The drop-down search edit context has no child DataWindow. The context is only marked "
                + "valid for a dddw column after GetChild succeeded "
                + "(n_cst_dwsvc_dropdownsearch.sru:L211-L222), so reaching this state indicates the "
                + "context was mutated outside InitEditContextInfo.");
    }

    /// <summary>
    /// The port of PowerScript's <c>Lower(string)</c>
    /// (<c>n_cst_dwsvc_dropdownsearch.sru:L110</c>, <c>:L120</c>, <c>:L206</c>, <c>:L226</c>,
    /// <c>:L316</c>, <c>:L440</c>).
    /// </summary>
    /// <param name="value">The text to lower-case.</param>
    /// <returns>The lower-cased text.</returns>
    /// <remarks>
    /// <b>INVARIANT CULTURE, DELIBERATELY, AND THE CHOICE IS OBSERVABLE.</b> PowerScript's
    /// <c>Lower</c> folds according to the running machine's locale, so on a Turkish locale it maps
    /// <c>I</c> to a dotless <c>ı</c> and the composed filter text would differ from the same input on
    /// any other locale. Invariant folding is chosen because the output of this function reaches
    /// EMITTED EXPRESSION TEXT [<c>:L319</c>, <c>:L323</c>, <c>:L331</c>] and characterization
    /// recordings compare that text byte for byte - a locale-dependent fold would make a recording
    /// captured in one container incomparable with one captured in another, which is a NEW source of
    /// non-determinism rather than a preserved behaviour (AAP 0.6.7 requires the non-deterministic
    /// sources be seamed, not multiplied). For the ASCII column names and search text the two agree
    /// exactly.
    /// </remarks>
    private static string Lower(string value)
    {
        return value.ToLowerInvariant();
    }

    /// <summary>
    /// The port of PowerScript's <c>Trim(string)</c> (<c>n_cst_dwsvc_dropdownsearch.sru:L317</c>).
    /// </summary>
    /// <param name="value">The text to trim.</param>
    /// <returns>The text without leading or trailing SPACES.</returns>
    /// <remarks>
    /// <b>SPACES ONLY, NOT ALL WHITESPACE, AND THAT IS THE DOCUMENTED POWERSCRIPT BEHAVIOUR.</b>
    /// PowerBuilder's <c>Trim</c> removes leading and trailing SPACE characters; .NET's parameterless
    /// <see cref="string.Trim()"/> additionally removes tabs, newlines and every other Unicode
    /// whitespace character, so using it would silently strip characters the oracle keeps. The choice
    /// is also internally consistent with the very next operation, which substitutes the SPACE
    /// character and nothing else [<c>:L317</c>]: a tab in the search text is content to the oracle,
    /// both here and there.
    /// </remarks>
    private static string Trim(string value)
    {
        return value.Trim(' ');
    }

    /// <summary>
    /// The port of PowerScript's <c>Len(string)</c>
    /// (<c>n_cst_dwsvc_dropdownsearch.sru:L92</c>, <c>:L98</c>, <c>:L129</c>).
    /// </summary>
    /// <param name="value">The text to measure.</param>
    /// <returns>The number of characters.</returns>
    /// <remarks>
    /// PowerBuilder 10 and later hold strings as UTF-16 and <c>Len</c> answers UTF-16 code units, which
    /// is exactly what <see cref="string.Length"/> answers - so the two agree even for the Chinese text
    /// this service is built to search. Returned as <see cref="long"/> because every consumer is a
    /// PowerScript <c>long</c>.
    /// </remarks>
    private static long Len(string value)
    {
        return value.Length;
    }

    /// <summary>
    /// The port of PowerScript's <c>Left(string, long)</c>
    /// (<c>n_cst_dwsvc_dropdownsearch.sru:L110</c>, <c>:L120</c>, <c>:L123</c>, <c>:L213</c>,
    /// <c>:L329</c>).
    /// </summary>
    /// <param name="value">The text to take from.</param>
    /// <param name="count">How many characters to take.</param>
    /// <returns>
    /// The leftmost <paramref name="count"/> characters; the WHOLE STRING when it is shorter than that,
    /// and THE EMPTY STRING when <paramref name="count"/> is zero or negative.
    /// </returns>
    /// <remarks>
    /// <para>
    /// BOTH EDGE CASES ARE LIVE IN THIS FILE AND NEITHER MAY THROW. The shorter-than-requested case is
    /// how a two-character column type still reaches the four-character prefix comparison at
    /// <c>:L213</c> and <c>:L329</c>, and how a display value shorter than what the user typed simply
    /// fails to match at <c>:L110</c>. The non-positive case is how a <c>ddlb</c> item carrying no tab
    /// yields an empty display value at <c>:L123</c>, where <c>Pos</c> answered <c>0</c> and
    /// <c>Left</c> therefore receives <c>-1</c>.
    /// </para>
    /// <para>
    /// <see cref="string.Substring(int, int)"/> would throw for both, which is why this is a helper and
    /// not an inline call.
    /// </para>
    /// </remarks>
    private static string Left(string value, long count)
    {
        if (count <= 0)
        {
            return string.Empty;
        }

        return count >= value.Length ? value : value[..(int)count];
    }

    /// <summary>
    /// The port of PowerScript's <c>Pos(string, string)</c>
    /// (<c>n_cst_dwsvc_dropdownsearch.sru:L123</c>).
    /// </summary>
    /// <param name="value">The text to search.</param>
    /// <param name="sought">The character to find.</param>
    /// <returns>
    /// The ONE-BASED position of the first occurrence, or <c>0</c> when there is none.
    /// </returns>
    /// <remarks>
    /// ONE-BASED, AND THE ZERO IS LOAD BEARING. <c>:L123</c> is
    /// <c>Left(sVal,Pos(sVal,"~t") - 1)</c>, so the one-based result minus one is the length of the
    /// display half - and a missing tab produces <c>0 - 1 = -1</c>, which <see cref="Left"/> answers as
    /// the empty string. Rebasing this to zero would turn a missing tab into a one-character truncation
    /// and, worse, would silently drop the last character of every item that DOES have one
    /// (DECISION 6).
    /// </remarks>
    private static long Pos(string value, char sought)
    {
        return value.IndexOf(sought, StringComparison.Ordinal) + 1;
    }

    /// <summary>
    /// The port of PowerScript's <c>IsNumber(string)</c>
    /// (<c>n_cst_dwsvc_dropdownsearch.sru:L333</c>).
    /// </summary>
    /// <param name="value">The text to test.</param>
    /// <returns><see langword="true"/> when the text is a number.</returns>
    /// <remarks>
    /// <para>
    /// THE GUARD THAT KEEPS AN UNQUOTED VALUE OUT OF A NUMERIC COMPARISON. Its one call site emits
    /// <c>" OR (" + dataColName + " = " + data + ")"</c> [<c>:L334</c>] with the raw argument and no
    /// quoting whatsoever, so without this test the composed expression would compare a numeric column
    /// against bare text and fail to parse.
    /// </para>
    /// <para>
    /// <see cref="NumberStyles.Float"/> AND <see cref="CultureInfo.InvariantCulture"/>, chosen to match
    /// PowerScript's own acceptance set: <c>Float</c> admits a leading sign, a decimal point, an
    /// exponent and surrounding whitespace - which is what PowerBuilder accepts - while deliberately
    /// EXCLUDING thousands separators, which it does not. Invariant culture for the same reason
    /// <see cref="Lower"/> uses it: the accepted text goes straight into emitted expression text, and a
    /// culture that reads <c>","</c> as a decimal point would accept an input the DataWindow expression
    /// parser then rejects.
    /// </para>
    /// <para>
    /// The empty string is not a number, which matters because it makes the numeric clause impossible on
    /// the empty-data path - though that path never reaches here, the whole builder being wrapped in
    /// <c>if data &lt;&gt; ""</c> [<c>:L315</c>].
    /// </para>
    /// </remarks>
    private static bool IsNumber(string value)
    {
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
    }

    /// <summary>
    /// The port of PowerScript's <c>Match(data, "[a-zA-Z]")</c>
    /// (<c>n_cst_dwsvc_dropdownsearch.sru:L322</c>).
    /// </summary>
    /// <param name="value">The text to test.</param>
    /// <returns><see langword="true"/> when the text contains at least one ASCII letter.</returns>
    /// <remarks>
    /// <para>
    /// <b>A CHARACTER SCAN RATHER THAN A REGULAR EXPRESSION, AND THE EQUIVALENCE IS EXACT RATHER THAN
    /// APPROXIMATE.</b> The oracle's pattern is a SINGLE CHARACTER CLASS with no quantifier, no anchor,
    /// no alternation, no group and no backreference, and <c>Match</c> searches rather than anchors - so
    /// the pattern asks precisely "does any character of the input satisfy A-Z or a-z", which
    /// <see cref="char.IsAsciiLetter(char)"/> decides per character. Introducing a regular-expression
    /// engine would add a dependency on PowerBuilder's own pattern dialect agreeing with .NET's for a
    /// question that has no dialect-sensitive part.
    /// </para>
    /// <para>
    /// The empty string answers <see langword="false"/>, matching <c>Match</c>. The call site cannot pass
    /// it anyway, being inside <c>if data &lt;&gt; ""</c> [<c>:L315</c>].
    /// </para>
    /// <para>
    /// WHY THE GUARD EXISTS AT ALL: Pinyin first letters are Latin letters, so an input consisting only
    /// of Chinese characters, digits or punctuation could never match one - and the oracle omits the
    /// clause entirely rather than emitting one that cannot succeed.
    /// </para>
    /// </remarks>
    private static bool MatchesAsciiLetter(string value)
    {
        foreach (char candidate in value)
        {
            if (char.IsAsciiLetter(candidate))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The port of PowerScript's <c>Long(string)</c> over a <c>Describe</c> answer
    /// (<c>n_cst_dwsvc_dropdownsearch.sru:L215</c>).
    /// </summary>
    /// <param name="value">The property answer to convert.</param>
    /// <returns>The value, or <c>0</c> when the text is not a whole number.</returns>
    /// <remarks>
    /// ZERO FOR A NON-NUMBER IS POWERSCRIPT'S DOCUMENTED CONVERSION BEHAVIOUR, and it is reachable here:
    /// <c>Describe</c> answers <c>"!"</c> for an invalid expression and <c>"?"</c> for a value it cannot
    /// determine, and the oracle wraps the call in <c>Long(...)</c> with no sentinel test of its own, so
    /// either answer becomes <c>0</c>. Throwing or returning a nullable instead would introduce a
    /// failure mode the oracle does not have. <see cref="NumberStyles.Integer"/> admits a leading sign
    /// and surrounding whitespace but not a decimal point, which is what a band height is;
    /// <see cref="CultureInfo.InvariantCulture"/> because the answer is machine-generated property text
    /// and never localised.
    /// </remarks>
    private static long ParseLegacyLong(string value)
    {
        return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed)
            ? parsed
            : 0L;
    }

    /// <summary>
    /// Reads a DataWindow attribute's text answer as the boolean the oracle read from a typed property -
    /// the port of the <c>#DataWindow.VScrollBar</c> term of
    /// <c>n_cst_dwsvc_dropdownsearch.sru:L263</c>.
    /// </summary>
    /// <param name="value">The answer <see cref="DataWindowServiceHost.Describe"/> gave.</param>
    /// <returns>
    /// <see langword="true"/> for <c>"yes"</c> and for <c>"1"</c>, case-insensitively;
    /// <see langword="false"/> for everything else, INCLUDING both sentinels.
    /// </returns>
    /// <remarks>
    /// <para>
    /// A DOCUMENTED TRANSLATION DECISION AT A BOUNDARY, NOT A GUESS (constraint C-K). The oracle reads a
    /// TYPED BOOLEAN off the control - <c>#DataWindow.VScrollBar</c> - and a headless service has no
    /// control to read, so the value must arrive as the DataWindow-object property of the same name,
    /// through the host contract's attribute accessor. That accessor answers TEXT, and the DataWindow
    /// object model spells this particular property's affirmative as <c>"yes"</c> while several sibling
    /// boolean properties spell theirs as <c>"1"</c>. BOTH are accepted, which cannot change behaviour
    /// for either canonical answer and removes a version-dependent failure mode; nothing else is
    /// accepted.
    /// </para>
    /// <para>
    /// THE TWO SENTINELS READ AS FALSE, deliberately. <c>"!"</c> and <c>"?"</c> both mean the property
    /// could not be answered, and false is the conservative reading: it makes the auto-determination
    /// decline to relocate rows rather than relocate them on the strength of an answer the DataWindow
    /// could not give.
    /// </para>
    /// </remarks>
    private static bool DescribesAffirmative(string value)
    {
        return string.Equals(value, AffirmativeAnswer, StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "1", StringComparison.Ordinal);
    }
}
