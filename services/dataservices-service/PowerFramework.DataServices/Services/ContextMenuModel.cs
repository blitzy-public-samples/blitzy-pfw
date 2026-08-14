// ==============================================================================================
//  ContextMenuModel - the HEADLESS HALF of the DataWindow context-menu service.
//  --------------------------------------------------------------------------------------------
//  ORACLE: ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_contextmenu.sru (1,459 lines,
//  READ ONLY, the largest object in its folder). Every behavioural claim below carries a `:Lnnn`
//  locator into it, because AAP 0.1.4 establishes that nothing else in the repository can
//  adjudicate behaviour: logfile.md stops at 2022 while the history runs to 2026, and the two
//  PowerBuilder build definitions contradict each other and both name a library that does not
//  exist.
//
//  THE SPLIT, WHICH IS THE WHOLE REASON THIS FILE EXISTS
//  AAP 0.2.1.3 Correction 4 finds this one of three attached DataWindow services that are
//  IRREDUCIBLY PRESENTATIONAL, on measured type-position evidence: it reads a window rectangle at
//  :L187, converts device units at :L1241/:L1243/:L1409/:L1411, creates a font object at
//  :L1092/:L1098/:L1266/:L1277, and publishes a submenu API typed on the popup-menu class. AAP
//  0.4.2.5 resolves it for this file in exactly these terms:
//
//      SHIPS HERE  - the complete menu ITEM model: labels, ids, enabled and split flags, and
//                    computed logical text widths.
//      DEFERRED    - window geometry, DPI-to-pixel conversion, font measurement, and rendering.
//
//  Per AAP 0.8.1, where the choice is a partial implementation versus a documented gap, THE
//  DOCUMENTED GAP WINS. So each deferred capability is surfaced as DATA over the contract and named
//  as a reserved Gateway extension point under /v1/design/** (AAP 0.4.4). Nothing is silently
//  dropped, and nothing deferred is half-built.
//
//  GOVERNING CONSTRAINTS, WITH THE RULING FOR THIS FILE
//  There are NO user rules: review_rules answers "No user rules provided", which AAP 0.7.1 records
//  as a finding rather than as latitude. The enterprise baseline of AAP 0.7.2 applies in their
//  place - nullable reference types on with warnings promoted to errors, no secret in source, and a
//  test project per shippable project with the coverage gate per service. The twelve binding
//  non-rule constraints of AAP 0.7.3 govern; these five reach this file hardest.
//
//  C-D  THE FOUR DEFERRED SERVICES GET NO CODE, NOT EVEN A STUB. This file has the largest
//       deferred surface in its folder and holds NONE of it: no popup-menu class (its NINETEEN
//       methods are inventoried in DECISION 2), no menu theme, no font object, no window
//       interop, no unit conversion in any direction, no pointer-shape change, no measurement
//       geometry types, and no width application. AND EXPLICITLY: not one
//       NotImplementedException-throwing placeholder standing in for any of them, under any name.
//       The prohibition is also structural - PowerFramework.DataServices.csproj references only
//       Contracts, Shared.Kernel, Shared.Eventful, Shared.Localization, Shared.Containers and
//       Shared.Diagnostics, so no such type exists to call.
//  C-B  NO NEW FEATURES, NO IMPROVEMENTS, DEFECTS REPLICATED VERBATIM. Ten preserved defects are
//       annotated at their point of reproduction, each with its locator: DEFECT 1 the 10000 alias
//       collapse; DEFECT 2 ColAutoWidth's absence from the emit filter; DEFECT 3
//       MID_COLREVERTCHECK escaping the ColCheck gate; DEFECT 4 the two item-copy early returns;
//       DEFECT 5 GetId(index) reading an uninitialised local; DEFECT 6 three lookups that answer
//       the LAST match; DEFECT 7 UncheckColumn's divergent type mechanism; DEFECT 8 the copy
//       producer's trailing separator; DEFECT 9 two different out-of-range codes; DEFECT 10 the
//       paste loop bounded by the PASTED LINE COUNT, which inserts rows to reach it; DEFECT 11 the
//       datetime arm rejecting a genuine 1900-01-01. Each has a test that FAILS if the defect is
//       "fixed", and the numbering here is the numbering
//       PowerFramework.DataServices.Tests/ContextMenuModelTests.cs uses - one roster, not two.
//  C-A  THE CONTRACTS PROJECT IS THE ONLY CROSS-SERVICE COUPLING. There is not one reference to
//       PowerFramework.Contracts here, and that is deliberate rather than incidental:
//       dataservices.v1.proto's C-03 surface declares NO menu-item message, so the model below is
//       a plain domain record tree and the projection onto the wire belongs to Grpc/ and
//       Endpoints/.
//  C-H  80% LINE COVERAGE PER SERVICE. The host and the expression evaluator are injected
//       abstractions, so the item store, the emit filter, the separator collapse, the item-copy
//       early returns, the ten error conversions and the ASCII/wide split are all exercised with
//       no DataWindow, no font and no UI.
//  C-K  EVERY BOUNDARY DECISION IS DOCUMENTED. The nine DECISION blocks below are that record.
//
//  DECISION 1 - THE ITEM MODEL IS HIERARCHICAL, AND IT HAD TO BE.
//      The DEFAULT item set needs a submenu: :L272 adds the auto-width parent through the
//      class-name submenu overload and :L273-L274 then fetch it and add "所有列" beneath it. So the
//      submenu could not simply be dropped. The legacy field is a live OBJECT POINTER [:L18], which
//      decomposes into three independent facts - WHETHER there is a child menu, WHICH one, and WHAT
//      is in it - carried by MenuItemData.HasSubmenu, SubmenuHandle and Submenu respectively. The
//      observable structure is preserved exactly while the popup-menu TYPE stays deferred.
//  DECISION 2 - THE PUBLIC SUBMENU SURFACE IS NOT PORTED, AND THE GAP IS INVENTORIED.
//      Nineteen deferred methods, counted from the forward prototypes: EIGHT insert-submenu
//      overloads (:L96, :L99-:L101 taking the menu by reference; :L102-:L105 taking a class name),
//      EIGHT add-submenu overloads (:L106-:L109 by reference; :L110-:L113 by class name), one
//      get-submenu (:L114) and TWO create-popup-menu helpers (:L97, :L98). Two of the sixteen carry
//      an ARGUMENT-ORDER IRREGULARITY worth recording because a future port will trip on it: at
//      :L107 and :L111 the enabled flag precedes the split flag, while every sibling puts split
//      first. All nineteen belong to /v1/design/**. Only the INTERNAL hierarchical add that the
//      default set needs exists here.
//  DECISION 3 - A HEADLESS SERVICE HAS NO SYSTEM CLIPBOARD, SO THE FLOW IS INVERTED.
//      Three sites. The READ at :L299 becomes request data - ContextMenuPointerContext.ClipboardText
//      - which is where the legacy was already heading, since the paste entry point already takes
//      its text as a parameter [:L942]. The two WRITES at :L549 and :L629 become RETURNED TEXT
//      (ClipboardTextResult), composed byte for byte including the row separator.
//  DECISION 4 - POINTER STATE ARRIVES AS REQUEST DATA.
//      The band under the pointer [:L233] and the pointer's y position [:L236] are reads a headless
//      service cannot perform. Both arrive on ContextMenuPointerContext and the promotion logic that
//      consumes them is reproduced verbatim against those inputs.
//  DECISION 5 - ONE RIGHT-CLICK EVENT BECOMES TWO OPERATIONS.
//      :L136-L202 is one event only because the popup is MODAL: it prepares, shows, dispatches and
//      cleans up in one stack frame. Across a boundary that becomes BuildMenu - prepare, both
//      vetoes, the emit filter and the collapse - and then ApplySelection - the chosen identifier,
//      the host's second veto and the default dispatch. AAP 0.6.1.4 assigns the context-menu area
//      pattern (b), STRICTLY SYNCHRONOUS WITH NO REORDERING, for precisely this reason:
//      initialisation must COMPLETE before the identifier passed to the second operation can mean
//      anything.
//  DECISION 6 - THE STORE IS PER-INVOCATION, AND THE CLEANUP FOLLOWS THE SPLIT.
//      The single most load-bearing fact in the file: the finally at :L197-L201 clears the clicked
//      column, clears the clipboard text AND empties the item store, so the menu is REBUILT ON
//      EVERY RIGHT-CLICK AND DISCARDED AFTERWARDS. A reader who models it as persistent will
//      accumulate duplicates. The try/finally shape is preserved; when BuildMenu hands a menu back,
//      ownership of the cleanup transfers to ApplySelection - or to Discard when no selection ever
//      arrives - and it is skipped on NO other path.
//  DECISION 7 - THE TEN DIALOGS BECOME LOCALIZED STRUCTURED ERRORS, RETURNED AND NEVER THROWN.
//      Ten live modal dialogs, at :L795, :L863, :L916, :L1009, :L1018, :L1027, :L1033, :L1039,
//      :L1045 and :L1052 - four more than AAP 0.2.1.3 Correction 5 states, verified by reading every
//      one. All ten route through the localization facade under CAT_DWSVC, which is the EXACT
//      OPPOSITE of Expressions/ParseErrorFormatter.cs, whose 28 column-expression messages are
//      hardcoded Chinese that bypass it. Correction 5 requires that inconsistency be reproduced on
//      BOTH sides, so nothing here goes near that formatter.
//  DECISION 8 - THE WIDTH PASS SHIPS AS A MEASUREMENT PLAN.
//      Both auto-width arities compute WHAT would be measured - the candidate texts, the synthesized
//      worst-case proxy, the font each would be measured in, the measurement mode, the drop-down
//      arrow allowance and the four-unit padding - and return it as data. Performing the measurement
//      and applying the width are the deferred half.
//  DECISION 9 - NO PERFORMANCE CLAIM IS MADE ANYWHERE IN THIS FILE.
//      AAP 0.8.5: the repository publishes no SLA, no latency budget and no throughput target, so no
//      shape here is justified on performance grounds. Every shape is justified by a locator.
//
//  ONE-BASED DISCIPLINE (AAP 0.4.5.4, risk R9). This is the densest file in its folder for the
//  refactor's most dangerous mechanical hazard: every store scan is `for n = 1 to UpperBound`, the
//  append idiom is `UpperBound + 1`, POSITIONAL ADDRESSING IS ONE-BASED THROUGHOUT the get/set/
//  remove/insert surface, the paste array is read one-based, and OrderedMap's positional overloads
//  are one-based with an INCLUSIVE upper bound of Count(). No inline `- 1` survives outside the four
//  helpers in the ONE-BASED STORE ACCESS region, which is the stronger of the two options AAP
//  0.4.5.4 allows.
//
//  NO INTRA-FOLDER DEPENDENCY. A cross-reference search over all four legacy service objects finds
//  zero live references between them, so nothing here imports a sibling in Services/. The one
//  apparent exception - a FILTERSUFFIX constant referenced from COMMENTED code in the column-sort
//  source - names a constant that exists nowhere in the repository, so it is not declared.
// ==============================================================================================

using System.Globalization;
using System.Text;

using Microsoft.Extensions.Options;

using PowerFramework.DataServices.Configuration;
using PowerFramework.DataServices.Domain;
using PowerFramework.DataServices.Expressions;
using PowerFramework.DataServices.Validators;
using PowerFramework.Shared.Containers;
using PowerFramework.Shared.Kernel;
using PowerFramework.Shared.Localization;

namespace PowerFramework.DataServices.Services;

/// <summary>
/// One context-menu item - the port of the nested <c>menuitemdata</c> structure
/// (<c>ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_contextmenu.sru:L12-L21</c>).
/// </summary>
/// <remarks>
/// <para>
/// ALL EIGHT LEGACY FIELDS ARE CARRIED, IN THE ORACLE'S DECLARATION ORDER, and the order is preserved
/// because a structure's field order is observable wherever the structure is serialized or recorded.
/// The oracle declares, at <c>:L13-L20</c> in this sequence: <c>enabled</c>, <c>text</c>,
/// <c>image</c>, <c>tiptext</c>, <c>id</c>, <c>submenu</c>, <c>split</c>, <c>menuowner</c>.
/// </para>
/// <para>
/// THE SIXTH FIELD IS THE ONE THAT CHANGES SHAPE, AND IT DECOMPOSES RATHER THAN DISAPPEARING
/// (DECISION 1). <c>:L18</c> declares it as a live object pointer to a popup menu, which cannot cross
/// a process boundary. A pointer to a child menu answers three independent questions, so three
/// members replace it: <see cref="HasSubmenu"/> is the validity test the emit filter performs at
/// <c>:L179</c>, <see cref="SubmenuHandle"/> is the identifier by which the oracle addresses the
/// child at <c>:L273</c>, and <see cref="Submenu"/> is the child items themselves. Together they
/// preserve the observable structure exactly.
/// </para>
/// <para>
/// <c>menuowner</c> IS NOT DEAD WEIGHT AND IS NOT DROPPED. It is written at <c>:L647</c> and
/// <c>:L650</c> and read at <c>:L389</c> and <c>:L450</c>, where it decides whether removing an item
/// also destroys the child menu it points at. In a data model there is no object to release, but the
/// flag still records WHO OWNS the child - which is the fact the deferred rendering half needs in
/// order to reproduce the lifetime, and which is why constraint C-B keeps it.
/// </para>
/// <para>
/// A <c>record</c> per AAP 0.4.5.2's mapping of a PowerScript structure, and IMMUTABLE so that the
/// store's one-based helpers are the only way state changes - a mutable item would let a caller edit
/// the store through a value it merely read.
/// </para>
/// </remarks>
public sealed record MenuItemData
{
    /// <summary>
    /// Whether the item is selectable - <c>boolean enabled</c> (<c>:L13</c>).
    /// </summary>
    /// <remarks>
    /// DEFAULTS TO <see langword="false"/>, WHICH IS THE PowerScript DEFAULT AND NOT A CHOICE: a
    /// freshly declared structure has a false boolean, and the insert path at <c>:L488</c> assigns it
    /// explicitly from its argument. Every ported add overload supplies it, so the default is reachable
    /// only by constructing an item directly.
    /// </remarks>
    public bool Enabled { get; init; }

    /// <summary>
    /// The item's label - <c>string text</c> (<c>:L14</c>).
    /// </summary>
    /// <remarks>
    /// ALSO THE SEPARATOR MARKER. An item whose text is exactly
    /// <see cref="ContextMenuModel.SeparatorText"/> IS a separator [<c>:L511</c>, tested at
    /// <c>:L169</c>]; there is no separate flag and none may be introduced. It is additionally the
    /// REJECTION TEST on the insert path - an empty label answers <c>0</c> and inserts nothing
    /// [<c>:L480</c>].
    /// </remarks>
    public string Text { get; init; } = string.Empty;

    /// <summary>
    /// The item's image name, as the legacy stock-image token - <c>string image</c> (<c>:L15</c>).
    /// </summary>
    /// <remarks>
    /// CARRIED AS DATA AND NOT RESOLVED. The default set uses exactly three values - <c>"Copy!"</c>,
    /// <c>"Paste!"</c> and <c>"SizeHorizontal!"</c> - plus the empty string for the three check items
    /// and the auto-width child. Turning a token into an image is rendering, which is deferred; the
    /// token itself is part of the item model and is exact.
    /// </remarks>
    public string Image { get; init; } = string.Empty;

    /// <summary>
    /// The item's tooltip text - <c>string tiptext</c> (<c>:L16</c>).
    /// </summary>
    /// <remarks>
    /// The short add and insert overloads default it to the EMPTY STRING rather than to the label
    /// [<c>:L345</c>, <c>:L348</c>, <c>:L502</c>, <c>:L508</c>], so an item added through them has no
    /// tip at all.
    /// </remarks>
    public string TipText { get; init; } = string.Empty;

    /// <summary>
    /// The item's identifier - <c>unsignedlong id</c> (<c>:L17</c>).
    /// </summary>
    /// <remarks>
    /// <c>uint</c> per AAP 0.4.5.2: PowerScript's <c>unsignedlong</c> is 32 bits, matching
    /// Shared.Kernel's fixed-width convention. THIS VALUE CROSSES THE WIRE ON EVERY SELECTION and
    /// appears in characterization recordings, which is why the nine reserved values keep their legacy
    /// spellings verbatim (AAP 0.4.5.3). A separator carries <c>0</c> [<c>:L511</c>].
    /// </remarks>
    public uint Id { get; init; }

    /// <summary>
    /// Whether a child menu is attached - the port of the validity test
    /// <c>IsValidObject(Items[n].subMenu)</c> (<c>:L179</c>).
    /// </summary>
    /// <remarks>
    /// AN EXPLICIT FLAG RATHER THAN <c>Submenu.Count &gt; 0</c>, because the two differ in a state the
    /// oracle genuinely passes through: <c>:L272</c> attaches a NEWLY CREATED and therefore EMPTY child
    /// menu, and only <c>:L274</c> puts an item in it. Between those two statements the legacy has a
    /// valid but empty submenu, and deriving this from the child count would report it as having none.
    /// </remarks>
    public bool HasSubmenu { get; init; }

    /// <summary>
    /// The identifier by which the child menu is addressed - the port of the <c>id</c> argument to
    /// <c>of_GetSubMenu</c> (<c>:L273</c>).
    /// </summary>
    /// <remarks>
    /// IN EVERY LEGACY USE THIS EQUALS THE OWNING ITEM'S OWN <see cref="Id"/>, because the oracle
    /// addresses a child menu by its parent item's identifier and never by any other handle. It is a
    /// separate member all the same: the identifier is how the legacy NAMES the child, while
    /// <see cref="Id"/> is how the menu names the ITEM, and collapsing them would assert that a child
    /// menu can never be addressed independently - which the eight deferred get/insert overloads of
    /// DECISION 2 do not support. <c>0</c> when there is no child.
    /// </remarks>
    public uint SubmenuHandle { get; init; }

    /// <summary>
    /// The child items - the contents of the attached child menu (<c>:L18</c>, populated at
    /// <c>:L274</c>).
    /// </summary>
    /// <remarks>
    /// EMPTY, NOT NULL, WHEN THERE IS NO CHILD, so a consumer may enumerate unconditionally.
    /// Recursive by type, because the legacy child is itself a popup menu that can hold further
    /// children; the default set nests exactly one level deep.
    /// </remarks>
    public IReadOnlyList<MenuItemData> Submenu { get; init; } = [];

    /// <summary>
    /// Whether the item is a SPLIT item - one whose label and whose drop-down arrow are separately
    /// clickable - <c>boolean split</c> (<c>:L19</c>).
    /// </summary>
    /// <remarks>
    /// SET ONLY BY THE SUBMENU PATH [<c>:L660</c>], never by a plain add - which is consistent, since
    /// splitting a label from an arrow is meaningless without a child menu to open. The default set
    /// sets it <see langword="true"/> exactly once, on the auto-width parent at <c>:L272</c>, so
    /// clicking the label runs the single-column operation while the arrow opens the all-columns child.
    /// The ARROW ITSELF is rendering and is deferred; the flag is data and ships.
    /// </remarks>
    public bool Split { get; init; }

    /// <summary>
    /// Whether this item OWNS the child menu it points at - <c>boolean menuowner</c> (<c>:L20</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// TRUE IN EXACTLY TWO SITUATIONS, both on the deferred submenu path: the service CREATED the child
    /// itself [<c>:L646-L647</c>], or the caller supplied one still carrying the creator tag
    /// [<c>:L649-L650</c>], in which case the tag is CLEARED so the next transfer cannot claim
    /// ownership twice. Consumed at <c>:L389</c> and <c>:L450</c>.
    /// </para>
    /// <para>
    /// NOTHING IS RELEASED WHEN AN OWNED ITEM IS REMOVED HERE, and that is not a leak. The child is
    /// DATA held by value, so removing the parent removes the children with it; the flag survives
    /// because it records a lifetime relationship the deferred rendering half must reproduce, and
    /// because dropping a field the oracle writes and reads would be exactly the silent narrowing
    /// constraint C-B forbids.
    /// </para>
    /// </remarks>
    public bool MenuOwner { get; init; }
}

/// <summary>
/// Why <see cref="ContextMenuModel.BuildMenu"/> answered as it did - the four early exits of
/// <c>event onrbuttonclicked</c> plus the one path that produces a menu
/// (<c>n_cst_dwsvc_contextmenu.sru:L136-L202</c>).
/// </summary>
/// <remarks>
/// THE ORACLE HAS NO SUCH VALUE, AND THAT IS THE POINT OF ADDING ONE. All five paths are a bare
/// <c>return</c> from a <c>void</c> event, so in-process they are INDISTINGUISHABLE - which is
/// tolerable when the next thing that happens is a modal popup on the same stack frame, and is not
/// tolerable across a boundary where the caller has to know whether to expect a selection. This
/// enumeration reports which path was taken and adds no behaviour: the items answered are exactly the
/// items the oracle would have added to the popup, and no path is reachable that the oracle cannot
/// reach.
/// </remarks>
public enum ContextMenuOutcome
{
    /// <summary>
    /// A menu was produced and a selection is expected - the oracle's popup at <c>:L189</c>.
    /// </summary>
    /// <remarks>
    /// THE ONLY OUTCOME THAT LEAVES THE PER-INVOCATION STORE ALIVE (DECISION 6). Ownership of the
    /// cleanup passes to <see cref="ContextMenuModel.ApplySelection"/>, or to
    /// <see cref="ContextMenuModel.Discard"/> if no selection ever arrives.
    /// </remarks>
    Menu = 0,

    /// <summary>
    /// The popup is not allowed here - <c>if Not _of_IsAllowPopup(row,dwo) then return</c>
    /// (<c>:L143</c>).
    /// </summary>
    /// <remarks>
    /// THE ONE EARLY EXIT THAT PERFORMS NO CLEANUP, because <c>:L143</c> sits OUTSIDE the try block
    /// that opens at <c>:L145</c>. Nothing has been built at that point, so there is nothing to clean -
    /// but the asymmetry is real and is reproduced rather than levelled.
    /// </remarks>
    NotAllowed = 1,

    /// <summary>
    /// The service's own preparation vetoed the menu -
    /// <c>if Event OnPrepare(row,dwo) = 1 then return</c> (<c>:L146</c>).
    /// </summary>
    /// <remarks>
    /// UNREACHABLE FROM THE PORTED PREPARATION AS IT STANDS, and that is a fact about the oracle rather
    /// than a gap: <see cref="ContextMenuModel.OnPrepare"/> answers <c>0</c> on every one of its four
    /// exits [<c>:L244</c>, <c>:L248</c>, <c>:L307</c>]. The test is preserved because the event is
    /// <c>virtual</c> in PowerBuilder and five test windows in ws_objects/pfw.tests.pbl.src extend
    /// these events with <c>call super::</c>, so an application CAN return 1 - and the veto is an
    /// EQUALITY test, so 2 does not veto.
    /// </remarks>
    PreparePrevented = 2,

    /// <summary>
    /// The host application vetoed the menu -
    /// <c>if #DataWindow.Event OnInitContextMenu(row,dwo) = 1 then return</c> (<c>:L147</c>).
    /// </summary>
    /// <remarks>
    /// The C-03 <c>oninitcontextmenu</c> semantic event [<c>se_cst_dw.sru:L11</c>], raised on the HOST
    /// and not on this service. The cleanup still runs.
    /// </remarks>
    HostPrevented = 3,

    /// <summary>
    /// Preparation produced no items at all - <c>if nCount = 0 then return</c> (<c>:L150</c>).
    /// </summary>
    /// <remarks>
    /// COUNTED BEFORE THE EMIT FILTER, NOT AFTER [<c>:L149-L150</c>]. So a store that holds only items
    /// the filter will suppress is NOT empty by this test: it reaches the filter, emits nothing, and
    /// still answers <see cref="Menu"/> with an empty list. Both emptinesses exist in the oracle and
    /// they are not merged here.
    /// </remarks>
    Empty = 4,
}

/// <summary>
/// The menu <see cref="ContextMenuModel.BuildMenu"/> produced, or the reason it produced none.
/// </summary>
/// <remarks>
/// <para>
/// THIS IS THE HEADLESS DELIVERABLE OF THE WHOLE FILE. The oracle's equivalent is the sequence of
/// <c>of_AddMenu</c>, <c>of_AddSeparator</c> and add-submenu calls it makes on a real popup menu at
/// <c>:L176-L183</c>; here that same sequence is returned as data for the deferred rendering half
/// under /v1/design/** to draw.
/// </para>
/// <para>
/// SEPARATORS TRAVEL AS ITEMS WHOSE TEXT IS THE MARKER, exactly as they are stored - see
/// <see cref="MenuItemData.Text"/>. Introducing a separate kind here would create a distinction the
/// oracle does not have and would have to be invented on the wire as well.
/// </para>
/// </remarks>
public sealed record ContextMenuLayout
{
    /// <summary>Which path <see cref="ContextMenuModel.BuildMenu"/> took.</summary>
    public required ContextMenuOutcome Outcome { get; init; }

    /// <summary>
    /// The items to show, in order, after the toggle filter and the separator collapse. EMPTY on every
    /// outcome other than <see cref="ContextMenuOutcome.Menu"/>, and possibly empty on that one too.
    /// </summary>
    public required IReadOnlyList<MenuItemData> Items { get; init; }

    /// <summary>
    /// Whether the per-invocation state is still alive and a selection is expected - see DECISION 6.
    /// </summary>
    /// <remarks>
    /// When this is <see langword="true"/> the caller MUST eventually call either
    /// <see cref="ContextMenuModel.ApplySelection"/> or <see cref="ContextMenuModel.Discard"/>;
    /// otherwise the clicked column, the supplied clipboard text and the item store stay populated,
    /// which is the one way a ported call sequence can diverge from the oracle's, whose modal popup
    /// made the pairing unavoidable.
    /// </remarks>
    public bool AwaitingSelection => Outcome == ContextMenuOutcome.Menu;
}

/// <summary>
/// The pointer-derived and clipboard-derived facts a headless service cannot read for itself, supplied
/// by the caller - see DECISION 4 and DECISION 3.
/// </summary>
/// <remarks>
/// THREE INPUTS, THREE LOCATORS, AND NOTHING ELSE. Only what the oracle actually reads from outside the
/// data model is here: the band under the pointer [<c>:L233</c>], the pointer's y position
/// [<c>:L236</c>] and the clipboard text [<c>:L299</c>]. The pointer's x and y in DEVICE terms, the
/// window rectangle and the popup position [<c>:L187-L189</c>] are NOT here, because they exist only to
/// place the popup and placing it is deferred.
/// </remarks>
public sealed record ContextMenuPointerContext
{
    /// <summary>
    /// The RAW band-at-pointer answer, in the legacy's <c>band~trow</c> form - the value
    /// <c>#DataWindow.GetBandAtPointer()</c> returns (<c>:L233</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// RAW AND NOT PRE-SPLIT, DELIBERATELY. <c>:L234</c> splits it with
    /// <c>Left(sBand,Pos(sBand,"~t") - 1)</c> and that arithmetic has an observable QUIRK worth
    /// keeping where the oracle put it: when the answer contains NO tab, <c>Pos</c> answers <c>0</c> and
    /// <c>Left(s,-1)</c> yields the EMPTY STRING - so an answer of <c>"detail"</c> with no row suffix
    /// becomes empty and matches no band arm. Splitting before the boundary would hide that.
    /// </para>
    /// <para>
    /// Defaults to the empty string, which the split leaves empty and which therefore selects no band -
    /// the same outcome as a pointer that is not over the DataWindow at all.
    /// </para>
    /// </remarks>
    public string BandAtPointer { get; init; } = string.Empty;

    /// <summary>
    /// The pointer's y position in the DataWindow's own units - the value
    /// <c>#DataWindow.PointerY()</c> returns (<c>:L236</c>).
    /// </summary>
    /// <remarks>
    /// USED FOR ONE COMPARISON ONLY, against the header height, to promote a background or foreground
    /// band to <c>"header"</c>. It is NOT a device coordinate and needs no conversion, which is why it
    /// can cross the boundary as a plain number while the popup position cannot.
    /// </remarks>
    public long PointerY { get; init; }

    /// <summary>
    /// The caller's clipboard text - the value <c>Clipboard()</c> returns (<c>:L299</c>).
    /// </summary>
    /// <remarks>
    /// THE PASTE ITEM APPEARS ONLY IF THIS IS NON-EMPTY [<c>:L300</c>], so supplying nothing correctly
    /// suppresses it. The text is stored for the duration of one invocation and cleared by the cleanup
    /// [<c>:L199</c>]; it is never logged, and there is nothing secret about it by contract - but the
    /// same one-invocation lifetime is what keeps it from lingering.
    /// </remarks>
    public string ClipboardText { get; init; } = string.Empty;
}

/// <summary>
/// The severity a context-menu error carries - the port of the <c>Icon</c> argument passed to all ten
/// modal dialogs (<c>:L795</c>, <c>:L863</c>, <c>:L916</c>, <c>:L1009</c>, <c>:L1018</c>, <c>:L1027</c>,
/// <c>:L1033</c>, <c>:L1039</c>, <c>:L1045</c>, <c>:L1052</c>).
/// </summary>
/// <remarks>
/// TWO MEMBERS, BECAUSE THE ORACLE PRODUCES ONE VALUE. All ten sites pass <c>StopSign!</c> and no
/// other icon appears anywhere in the file, so declaring PowerScript's information, exclamation,
/// question and none arms would fabricate a taxonomy no ported call site reaches (constraint C-B).
/// <see cref="None"/> exists so the enumeration has a well-defined zero for a
/// <see langword="default"/> value; this service never produces it.
/// </remarks>
public enum ContextMenuMessageIcon
{
    /// <summary>No icon. The enumeration's zero; never produced by this service.</summary>
    None = 0,

    /// <summary>The stop-sign icon - the legacy <c>StopSign!</c>, passed at all ten sites.</summary>
    StopSign = 1,
}

/// <summary>
/// Which of the three message shapes an error carries - the second operand of the ten composed dialog
/// messages.
/// </summary>
/// <remarks>
/// THREE KINDS ACROSS TEN SITES, and the grouping is the oracle's own text rather than a category
/// invented here: four sites share the rejection wording, one is the invalid-value wording, and five
/// are the type-mismatch wording. The kind is what a consumer branches on; the exact site is recorded
/// separately on <see cref="ContextMenuError.SourceLocator"/>.
/// </remarks>
public enum ContextMenuErrorKind
{
    /// <summary>No error. The enumeration's zero; never produced.</summary>
    None = 0,

    /// <summary>
    /// The host REFUSED the data change - <c>修改数据被拒绝</c> at <c>:L795</c>, <c>:L863</c>,
    /// <c>:L916</c> and <c>:L1052</c>.
    /// </summary>
    /// <remarks>
    /// Raised when the item-change event answers anything other than <c>0</c> - the test is
    /// <c>&lt;&gt; 0</c>, so this is a two-state accept-or-reject read and NOT the four-value
    /// item-change alphabet. Carries no offending value.
    /// </remarks>
    ChangeRejected = 1,

    /// <summary>
    /// The pasted text is not a member of the column's value set - <c>无效的值</c> at <c>:L1009</c>.
    /// </summary>
    /// <remarks>
    /// Reachable ONLY when the column enumerates its values - a non-editable drop-down, a radio-button
    /// group or a check box - because that is what sets the enumerated-value flag [<c>:L971</c>,
    /// <c>:L977</c>, <c>:L982</c>]. A translating-but-editable column falls through and pastes the text
    /// as typed.
    /// </remarks>
    InvalidValue = 2,

    /// <summary>
    /// The pasted text will not convert to the column's type - <c>数据类型不匹配</c> at <c>:L1018</c>,
    /// <c>:L1027</c>, <c>:L1033</c>, <c>:L1039</c> and <c>:L1045</c>.
    /// </summary>
    /// <remarks>
    /// FIVE SITES, ONE PER TYPED ARM - integer, decimal, datetime, date and time. The string arm has no
    /// such test because every text converts to text.
    /// </remarks>
    TypeMismatch = 3,
}


/// <summary>
/// The structured replacement for one of the ten modal dialogs - see DECISION 7.
/// </summary>
/// <remarks>
/// <para>
/// ONLY THE DELIVERY CHANNEL CHANGES (AAP 0.2.1.3 Correction 5, AAP 0.3.4). Every composition input
/// travels, not just the rendered text: the two lookup keys, the category, the substitution arguments
/// and the severity are what a characterization comparison asserts on, and text alone could not be
/// re-rendered under another locale or verified against the oracle.
/// </para>
/// <para>
/// NOTHING PRESENTATIONAL TRAVELS. There is no window, owner, button set, caption, position or
/// modality here, because a structured error is not a dialog (constraint C-D). The severity is the one
/// dialog attribute the legacy supplies and it is preserved as data.
/// </para>
/// <para>
/// RETURNED, NEVER THROWN, AND NEVER SWALLOWED. Each producing operation abandons its work exactly
/// where the oracle abandons it and answers the oracle's return code, while this record is published on
/// <see cref="ContextMenuModel.PendingError"/> for the caller to surface.
/// </para>
/// </remarks>
public sealed record ContextMenuError
{
    /// <summary>
    /// The composed message, exactly as the legacy dialog would have displayed it.
    /// </summary>
    /// <remarks>
    /// COMPOSED IN THE ORACLE'S OPERAND ORDER, which is
    /// <c>Sprintf(I18N(CAT_DWSVC,"第{}行"),nRow) + "~n" + I18N(CAT_DWSVC,&lt;detail&gt;) + "!"</c> and, on
    /// the two value-carrying shapes, <c>+ "~n" + sVal</c> - note that the exclamation mark is part of
    /// the literal <c>"!~n"</c> in the oracle, so it precedes the second separator rather than following
    /// the value. TWO SEPARATE LOOKUPS, not one lookup of a joined string: the legacy translates each
    /// fragment independently, so a provider may translate one and pass the other through.
    /// </remarks>
    public required string Text { get; init; }

    /// <summary>
    /// The localization category both lookups were made under - always
    /// <c>Categories.CAT_DWSVC</c>.
    /// </summary>
    /// <remarks>
    /// Carried as data rather than implied, because the category selects WHICH translation table
    /// answered and a recording of this error has to be comparable against the legacy's own. It is
    /// <c>Enums.I18N_CAT_CUSTOM + 2</c> - a COMPUTED OFFSET [<c>ne_cst_i18n.sru:L17</c>] - and is always
    /// consumed through the named constant, never as the literal it evaluates to.
    /// </remarks>
    public required long LocalizationCategory { get; init; }

    /// <summary>
    /// The first lookup key, verbatim: the row-number fragment, which is ALSO a
    /// <c>Formatting.Sprintf</c> format string.
    /// </summary>
    /// <remarks>
    /// Always <see cref="ContextMenuModel.RowNumberMessageKey"/>. It is BOTH a key and a format string,
    /// which is why the legacy translates it FIRST and formats the translation SECOND: a provider is
    /// expected to return a translation that still contains the placeholder. It uses the SEQUENTIAL
    /// empty-brace form, unlike the three indexed format strings the width pass builds.
    /// </remarks>
    public required string RowFormatTemplate { get; init; }

    /// <summary>
    /// The second lookup key, verbatim: the detail fragment, one of the three legacy message strings.
    /// </summary>
    /// <remarks>
    /// One of <see cref="ContextMenuModel.ChangeRejectedMessageKey"/>,
    /// <see cref="ContextMenuModel.InvalidValueMessageKey"/> or
    /// <see cref="ContextMenuModel.TypeMismatchMessageKey"/>. Carries no placeholder and is not
    /// formatted.
    /// </remarks>
    public required string DetailMessageKey { get; init; }

    /// <summary>
    /// The one-based row the error applies to - the <c>Sprintf</c> substitution argument, preserved as
    /// data and not only pre-rendered into <see cref="Text"/>.
    /// </summary>
    /// <remarks>
    /// THE LOOP CURSOR, WHICH IS NOT ALWAYS A BUFFER ROW. On the three check-column paths it is a row of
    /// the primary buffer, but on the paste path the cursor counts CLIPBOARD LINES [<c>:L993</c>] and
    /// the buffer is grown to match [<c>:L994-L1001</c>] - so early in a paste into an empty
    /// DataWindow the two coincide only because rows are being inserted as it goes.
    /// </remarks>
    public required long Row { get; init; }

    /// <summary>
    /// The offending value, for the two shapes that append one - <see langword="null"/> for
    /// <see cref="ContextMenuErrorKind.ChangeRejected"/>, which appends none.
    /// </summary>
    /// <remarks>
    /// THE VALUE AS IT STOOD AT THE POINT OF FAILURE, WHICH MAY ALREADY HAVE BEEN REWRITTEN. Two
    /// rewrites can precede it: a display-to-data translation through the value map [<c>:L1007</c>] and
    /// the percent-to-fraction conversion on the decimal arm [<c>:L1023-L1025</c>]. The oracle appends
    /// the CURRENT local, so this is the rewritten text and not the pasted text, and a test asserting
    /// otherwise would be asserting a behaviour the oracle does not have.
    /// </remarks>
    public string? OffendingValue { get; init; }

    /// <summary>Which of the three message shapes this is.</summary>
    public required ContextMenuErrorKind Kind { get; init; }

    /// <summary>
    /// The severity, always <see cref="ContextMenuMessageIcon.StopSign"/> - the legacy
    /// <c>StopSign!</c>.
    /// </summary>
    public required ContextMenuMessageIcon Icon { get; init; }

    /// <summary>
    /// Whether the message text was produced through the localization facade. Always
    /// <see langword="true"/> for every error this service produces.
    /// </summary>
    /// <remarks>
    /// RECORDED AS DATA BECAUSE THE ANSWER IS NOT UNIFORM ACROSS THE DATAWINDOW SERVICE LAYER, and the
    /// difference is a preserved legacy inconsistency rather than an accident: all ten messages here
    /// route through the facade, while the column-expression engine's 28 messages are hardcoded Chinese
    /// that bypass it entirely (AAP 0.2.1.3 Correction 5 requires BOTH sides of that inconsistency be
    /// reproduced). A consumer that assumed one answer for the whole layer would be wrong half the time,
    /// so each error states its own.
    /// </remarks>
    public required bool Localized { get; init; }

    /// <summary>
    /// The oracle site this error reproduces, as a <c>file:Lnnn</c> locator.
    /// </summary>
    /// <remarks>
    /// CHARACTERIZATION METADATA, NOT PART OF THE LEGACY PAYLOAD, and it earns its place: five of the
    /// ten sites share the same kind, the same keys and the same category, so without the locator the
    /// datetime arm's failure and the time arm's failure are indistinguishable in a recording. It
    /// influences no behaviour and no branch.
    /// </remarks>
    public required string SourceLocator { get; init; }
}

/// <summary>
/// What one of the two copy operations produced - see DECISION 3.
/// </summary>
/// <remarks>
/// BOTH HALVES TRAVEL. <see cref="Code"/> is the oracle's own <c>long</c> return value, unchanged, so a
/// caller sees the same success and failure algebra; <see cref="Text"/> is what the oracle would have
/// handed to the system clipboard at <c>:L549</c> or <c>:L629</c>. A shape that returned only the text
/// would lose the three distinct failure codes, and one that returned only the code would lose the whole
/// point of the operation.
/// </remarks>
public sealed record ClipboardTextResult
{
    /// <summary>
    /// The legacy return code - <c>RetCode.OK</c>, <c>RetCode.E_INVALID_ARGUMENT</c> or
    /// <c>RetCode.FAILED</c>.
    /// </summary>
    /// <remarks>
    /// THE FAILURE CODES ARE THE ORACLE'S AND ARE NOT NORMALISED. An empty column name answers
    /// <c>E_INVALID_ARGUMENT</c> [<c>:L518</c>, <c>:L606</c>], a row at or below zero answers the same
    /// on the item path [<c>:L605</c>], and a refused accept-text answers <c>FAILED</c> [<c>:L519</c>,
    /// <c>:L607</c>]. Note the ORDER on the item path: the row is checked BEFORE the column name.
    /// </remarks>
    public required long Code { get; init; }

    /// <summary>
    /// The composed text, or <see langword="null"/> when the operation failed before composing any.
    /// </summary>
    /// <remarks>
    /// NULL AND EMPTY MEAN DIFFERENT THINGS HERE, and AAP 0.4.5.4 forbids collapsing them: null is "no
    /// composition happened", while empty is a real composition over a column whose every cell was
    /// invisible. The column form is NEVER empty on success - it always ends with the row separator
    /// (see the trailing-separator note on <see cref="ContextMenuModel.CopyColumnToText"/>).
    /// </remarks>
    public required string? Text { get; init; }
}

/// <summary>
/// How the deferred half should measure one candidate text - the port of the choice between the
/// legacy's three text-measuring calls.
/// </summary>
/// <remarks>
/// THE CHOICE IS DATA, THE MEASUREMENT IS NOT (DECISION 8). Which mode applies is decided by the
/// oracle's own branches and is therefore part of the plan this service produces; performing the
/// measurement needs a font and a device context, which constraint C-D places under /v1/design/**.
/// </remarks>
public enum TextMeasurementMode
{
    /// <summary>
    /// Single-line measurement, taking the text's own width - the oracle's unwrapped measurement at
    /// <c>:L1158</c>, <c>:L1215</c>, <c>:L1324</c> and <c>:L1381</c>.
    /// </summary>
    SingleLine = 0,

    /// <summary>
    /// Word-wrapped measurement inside <see cref="TextMeasurementCandidate.WrapWidth"/> - the oracle's
    /// wrapped measurement for a HEADER-band candidate at <c>:L1152-L1153</c> and <c>:L1318-L1319</c>.
    /// </summary>
    /// <remarks>
    /// SELECTED BY BAND, NOT BY OBJECT KIND: the test is <c>if sBand = "header"</c>, an EXACT match, so a
    /// group header band such as <c>"header.1"</c> takes the single-line arm even though the candidate
    /// survived the band filter by PREFIX. That asymmetry is the oracle's - the filter tests
    /// <c>Left(sBand,6) &lt;&gt; "header"</c> while this tests equality - and it is preserved.
    /// </remarks>
    WordBreak = 1,

    /// <summary>
    /// Edit-control measurement with tab expansion and external leading, inside
    /// <see cref="TextMeasurementCandidate.WrapWidth"/> - the oracle's measurement for a row candidate in
    /// an auto-sizing column at <c>:L1209-L1210</c> and <c>:L1375-L1376</c>.
    /// </summary>
    /// <remarks>
    /// SELECTED BY THE COLUMN'S OWN AUTO-HEIGHT PROPERTY [<c>:L1113</c>, <c>:L1280</c>], not by the
    /// candidate's band, and it applies ONLY to per-row candidates. The three composed flags are recorded
    /// as one mode rather than as three booleans because the oracle passes them as one summed argument
    /// and never varies the combination.
    /// </remarks>
    EditControl = 2,
}


/// <summary>
/// One text whose rendered width would contribute to a column's automatic width, together with
/// everything needed to measure it - see DECISION 8.
/// </summary>
/// <remarks>
/// <para>
/// THE UNIT OF THE MEASUREMENT PLAN. The oracle measures a text the instant it derives it and keeps only
/// the running maximum [<c>:L1154-L1161</c>, <c>:L1211-L1218</c>]; this service derives the same texts in
/// the same order and returns them all, because taking the maximum requires the measurement that is
/// deferred. The candidate list is therefore LONGER-LIVED than the oracle's single local, and that is the
/// whole shape of the hand-off.
/// </para>
/// <para>
/// THE FONT TRAVELS AS THREE RAW <c>Describe</c> ANSWERS, UNTRANSFORMED. The oracle reads the face, the
/// height and the weight and hands them straight to a font object [<c>:L1125-L1127</c>], negating the
/// height and testing the weight against <c>"700"</c> as it goes. Those two transformations are recorded
/// here as data - <see cref="FontHeight"/> is the value as read and <see cref="Bold"/> is the comparison
/// already performed - because the comparison is a pure string test that belongs on the headless side
/// while the negation is a font-construction convention that belongs on the deferred side.
/// </para>
/// </remarks>
public sealed record TextMeasurementCandidate
{
    /// <summary>The DataWindow object whose text this is.</summary>
    public required string ObjectName { get; init; }

    /// <summary>
    /// The band the object sits in, as the raw <c>Describe</c> answer - <c>"header"</c>,
    /// <c>"header.1"</c>, <c>"summary"</c>, <c>"footer"</c>, <c>"trailer.1"</c> or, for a per-row
    /// candidate, <c>"detail"</c>.
    /// </summary>
    public required string Band { get; init; }

    /// <summary>
    /// The object's kind, as the raw <c>Describe</c> answer - <c>"column"</c>, <c>"compute"</c> or
    /// <c>"text"</c> (<c>:L1128</c>, <c>:L1294</c>).
    /// </summary>
    /// <remarks>
    /// CARRIED BECAUSE IT EXPLAINS WHERE <see cref="Text"/> CAME FROM, which a consumer cannot otherwise
    /// tell: a compute candidate's text is an evaluated expression, a column candidate's text is the
    /// SYNTHESIZED PROXY, and a text candidate's text is the object's own label. The three arms are at
    /// <c>:L1129-L1137</c> and the default arm answers the empty string [<c>:L1139</c>].
    /// </remarks>
    public required string ObjectType { get; init; }

    /// <summary>
    /// The text to measure, after the format has been applied.
    /// </summary>
    /// <remarks>
    /// ALREADY FORMATTED, BECAUSE FORMATTING IS A STRING OPERATION AND THEREFORE HEADLESS. The oracle
    /// applies the object's format before measuring [<c>:L1142-L1150</c>, <c>:L1196-L1203</c>], choosing a
    /// numeric or a text format call by whether the value is numeric, and normalising the two sentinels
    /// and the general-format marker to "no format" first.
    /// </remarks>
    public required string Text { get; init; }

    /// <summary>
    /// For a COLUMN candidate, the count of single-width characters in the synthesized proxy - the
    /// oracle's <c>nAsc2Cnt</c> AFTER its adjustment (<c>:L1134</c>, <c>:L1300</c>). Zero for every other
    /// kind.
    /// </summary>
    /// <remarks>
    /// THE ARITHMETIC IS THE HEADLESS DELIVERABLE AND IT IS NOT OBVIOUS, SO IT IS SPELLED OUT HERE. The
    /// oracle asks the expression engine for two maxima over the column's DISPLAY values: the longest in
    /// CHARACTERS and the longest in DBCS BYTES [<c>:L1132-L1133</c>]. Their difference is the number of
    /// WIDE characters, because a wide character costs two bytes and one character. The character count is
    /// then REDUCED by that difference [<c>:L1134</c>], leaving the number of single-width characters.
    /// See <c>ProxyText</c> for what the pair is for.
    /// </remarks>
    public int AsciiCount { get; init; }

    /// <summary>
    /// For a COLUMN candidate, the count of wide characters in the synthesized proxy - the oracle's
    /// <c>nChsCnt</c> (<c>:L1133</c>, <c>:L1299</c>). Zero for every other kind.
    /// </summary>
    /// <remarks>
    /// COMPUTED THROUGH AN ABSOLUTE VALUE, WHICH MATTERS: <c>Abs(byteMax - charMax)</c> means a byte
    /// maximum SMALLER than the character maximum - which cannot happen for well-formed text but can
    /// happen when either evaluation fails and yields a sentinel that converts to zero - produces a
    /// POSITIVE count rather than a negative one. Preserved exactly, including the consequence that
    /// <see cref="AsciiCount"/> can then go negative.
    /// </remarks>
    public int WideCount { get; init; }

    /// <summary>
    /// Whether <see cref="Text"/> is the SYNTHESIZED WORST-CASE PROXY rather than a real value.
    /// </summary>
    /// <remarks>
    /// TRUE ONLY ON THE COLUMN ARM. The proxy is built as <c>Fill("A",asciiCount) + Fill("国",wideCount)</c>
    /// [<c>:L1135</c>, <c>:L1301</c>] - a string of the right SHAPE rather than any real value - because
    /// measuring the widest single value would not bound a column that holds both a long narrow value and
    /// a short wide one. Recording the flag is what stops a consumer from displaying the proxy to a user.
    /// </remarks>
    public bool IsSynthesizedProxy { get; init; }

    /// <summary>The font face, as the raw <c>Describe</c> answer (<c>:L1126</c>, <c>:L1292</c>).</summary>
    public required string FontFace { get; init; }

    /// <summary>
    /// The font height, as the raw <c>Describe</c> answer converted to a number (<c>:L1125</c>,
    /// <c>:L1291</c>).
    /// </summary>
    /// <remarks>
    /// POSITIVE AS READ. The oracle NEGATES it when constructing the font, which is the platform
    /// convention for "this height is in units, not in points"; the negation is a font-construction detail
    /// and stays on the deferred side, so the value here is the DataWindow's own.
    /// </remarks>
    public long FontHeight { get; init; }

    /// <summary>
    /// Whether the font is bold - the oracle's <c>Describe(...".Font.Weight") = "700"</c> comparison,
    /// already performed (<c>:L1127</c>, <c>:L1293</c>).
    /// </summary>
    /// <remarks>
    /// AN EXACT STRING COMPARISON AGAINST <c>"700"</c>, NOT A NUMERIC THRESHOLD. A weight of <c>"701"</c>
    /// or <c>"bold"</c> answers <see langword="false"/>. Reproduced as written: reading it as
    /// "weight &gt;= 700" would bold fonts the legacy leaves unbolded.
    /// </remarks>
    public bool Bold { get; init; }

    /// <summary>How the deferred half should measure this candidate.</summary>
    public required TextMeasurementMode MeasurementMode { get; init; }

    /// <summary>
    /// The wrapping width for the two wrapped modes - the oracle's <c>rcText.right = 1024</c>
    /// (<c>:L1152</c>, <c>:L1209</c>, <c>:L1318</c>, <c>:L1375</c>). Zero for
    /// <see cref="TextMeasurementMode.SingleLine"/>.
    /// </summary>
    /// <remarks>
    /// A BARE LITERAL IN THE ORACLE, SET IMMEDIATELY BEFORE EACH WRAPPED MEASUREMENT AND NEVER EXPLAINED.
    /// It is carried rather than rationalised: it is not derived from the column's width, from the
    /// DataWindow's width or from anything else readable, so no substitute could be justified.
    /// </remarks>
    public int WrapWidth { get; init; }

    /// <summary>
    /// The DataWindow format mask that must be applied to <see cref="Text"/> BEFORE it is measured, or
    /// the empty string when none applies (<c>:L1142-L1150</c>, <c>:L1196-L1203</c>,
    /// <c>:L1308-L1316</c>, <c>:L1362-L1369</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// CARRIED AS DATA RATHER THAN APPLIED, and this is a deliberate boundary decision. The oracle
    /// applies the mask with <c>String(value, mask)</c> - PowerScript's format-mask engine - and there
    /// is no such engine anywhere in this repository's .NET tree and no BCL equivalent: a DataWindow
    /// mask is not a .NET format string. Implementing one here would be a new capability, forbidden by
    /// constraint C-B, and it would belong to the deferred half in any case because its ONLY purpose
    /// here is to decide how wide the text renders. So the headless half resolves the mask - including
    /// evaluating a per-row expression mask, which it CAN do - and hands mask plus text to the measurer.
    /// </para>
    /// <para>
    /// ALREADY NORMALISED. The three values the oracle discards - <c>"!"</c>, <c>"?"</c> and, folded
    /// case-insensitively, <c>"[general]"</c> - are collapsed to the empty string here
    /// [<c>:L1143</c>, <c>:L1168</c>, <c>:L1309</c>, <c>:L1334</c>], so a measurer never has to know
    /// about them.
    /// </para>
    /// </remarks>
    public string FormatMask { get; init; } = string.Empty;

    /// <summary>
    /// Which <c>String</c> overload the mask must be applied through: <see langword="true"/> for
    /// <c>String(Dec(text), mask)</c> and <see langword="false"/> for <c>String(text, mask)</c>
    /// (<c>:L1145-L1149</c>, <c>:L1198-L1202</c>, <c>:L1311-L1315</c>, <c>:L1364-L1368</c>).
    /// </summary>
    /// <remarks>
    /// THE DECISION IS MADE HERE, NOT DEFERRED, because it is <c>IsNumber(sText)</c> - a predicate this
    /// half already owns - and because getting it wrong changes the rendered text rather than merely its
    /// width. It is meaningful only when <see cref="FormatMask"/> is non-empty.
    /// </remarks>
    public bool FormatAppliesToNumber { get; init; }
}

/// <summary>
/// The measurement plan for ONE column - everything the oracle would have measured, in the order it would
/// have measured it, without the measurement.
/// </summary>
/// <remarks>
/// TWO CANDIDATE LISTS, BECAUSE THE ORACLE HAS TWO LOOPS AND THEY DIFFER IN MORE THAN THEIR SOURCE.
/// <see cref="HeadingCandidates"/> comes from the sibling-object sweep [<c>:L1119-L1164</c>,
/// <c>:L1285-L1330</c>], which walks EVERY object in the DataWindow and keeps those in a heading-like band
/// that overlap this column horizontally. <see cref="RowCandidates"/> comes from the distinct-value scan
/// [<c>:L1180-L1224</c>, <c>:L1346-L1390</c>], which walks only rows whose display value differs from row
/// one's. Merging them would lose the distinction between a heading that must fit and a value that must
/// fit, and they are measured in different fonts.
/// </remarks>
public sealed record ColumnWidthComputation
{
    /// <summary>The column this plan is for.</summary>
    public required string ColName { get; init; }

    /// <summary>
    /// Whether the column is a computed field - <c>bIsCompute</c> (<c>:L1112</c>, <c>:L1279</c>).
    /// </summary>
    /// <remarks>
    /// SELECTS BOTH THE PER-ROW TEXT SOURCE AND THE DISTINCT-VALUE PREDICATE. A computed field's rows are
    /// evaluated one at a time [<c>:L1194</c>] and scanned with the compute predicate [<c>:L1183</c>]; a
    /// data column's rows are read through the display lookup [<c>:L1205</c>] and scanned with one of the
    /// two column predicates [<c>:L1186</c>, <c>:L1188</c>].
    /// </remarks>
    public required bool IsCompute { get; init; }

    /// <summary>
    /// Whether the column auto-sizes its height - <c>bAutoHeight</c> (<c>:L1113</c>, <c>:L1280</c>).
    /// </summary>
    /// <remarks>
    /// Decides the per-row measurement mode, and nothing else. Read as an EXACT <c>"yes"</c> comparison.
    /// </remarks>
    public required bool AutoHeight { get; init; }

    /// <summary>
    /// The column's horizontal position, as the raw <c>Describe</c> answer converted to a number
    /// (<c>:L1117</c>, <c>:L1282</c>).
    /// </summary>
    /// <remarks>
    /// AN OVERLAP TEST INPUT, NOT A GEOMETRY OUTPUT. It is used only to decide WHICH sibling objects belong
    /// to this column, which is a comparison of two numbers the DataWindow itself reports and involves no
    /// unit conversion - so it is headless. It travels because the deferred half needs to know which column
    /// each measured heading belongs to.
    /// </remarks>
    public required long ColumnXPosition { get; init; }

    /// <summary>
    /// The column's current width, as the raw <c>Describe</c> answer converted to a number (<c>:L1118</c>,
    /// <c>:L1283</c>).
    /// </summary>
    /// <remarks>
    /// The right-hand bound of the overlap test. NOT the value being computed - the computed width is what
    /// the deferred half produces from the candidates, the arrow allowance and the padding.
    /// </remarks>
    public required long ColumnWidth { get; init; }

    /// <summary>The heading-band candidates, in the order the oracle derives them.</summary>
    public required IReadOnlyList<TextMeasurementCandidate> HeadingCandidates { get; init; }

    /// <summary>The per-row candidates, in the order the distinct-value scan reaches them.</summary>
    public required IReadOnlyList<TextMeasurementCandidate> RowCandidates { get; init; }

    /// <summary>
    /// The DataWindow expression the distinct-value scan searched with, or the EMPTY STRING when the buffer
    /// was empty and no scan ran (<c>:L1181</c>, <c>:L1347</c>).
    /// </summary>
    /// <remarks>
    /// CARRIED BYTE-EXACT BECAUSE PARITY IS ASSERTED ON IT. It is one of three format strings the oracle
    /// builds with the INDEXED <c>Sprintf</c> form at <c>:L1183</c>, <c>:L1186</c> and <c>:L1188</c>,
    /// repeating the column name two, four and six times respectively - and which one is chosen is itself
    /// observable, being decided by whether the column computes and, if not, whether it is a string column.
    /// </remarks>
    public required string DistinctValueFindExpression { get; init; }

    /// <summary>
    /// The drop-down arrow allowance to add to the measured width, in DataWindow units - the oracle's
    /// <c>ARROW_BTN_WIDTH</c> when it applies, otherwise zero (<c>:L1226-L1235</c>, <c>:L1394-L1403</c>).
    /// </summary>
    /// <remarks>
    /// THE THREE CONDITIONS ARE AN ORDERED <c>elseif</c> CHAIN AND THE ALLOWANCE IS ADDED AT MOST ONCE:
    /// the column is editable, OR its drop-down DataWindow is used as its border, OR its drop-down list box
    /// is. The chain runs only for a drop-down edit style. The addition itself is arithmetic on a measured
    /// value, so this service reports the ALLOWANCE and the deferred half adds it.
    /// </remarks>
    public required double ArrowButtonWidth { get; init; }

    /// <summary>
    /// The constant padding the oracle adds before converting units - always
    /// <see cref="ContextMenuModel.WidthPadding"/> (<c>:L1239</c>, <c>:L1407</c>).
    /// </summary>
    /// <remarks>
    /// FOUR UNITS, ADDED INSIDE THE CONVERSION ARGUMENT AND THEREFORE BEFORE IT, in all four arms of the
    /// unit switch. Carried as data so the deferred half cannot get the ORDER wrong - padding then
    /// converting is not the same as converting then padding.
    /// </remarks>
    public required double Padding { get; init; }
}

/// <summary>
/// What one automatic-width pass produced - the port of both <c>_of_columnautowidth</c> arities, reduced
/// to computation (<c>:L1085</c>, <c>:L1260</c>).
/// </summary>
/// <remarks>
/// <para>
/// THE DEFERRED HALF IS NAMED, NOT SILENTLY OMITTED. What this record does NOT carry is the measured width,
/// its unit conversion and its application to the column - the oracle's own final three steps
/// [<c>:L1237-L1248</c>, <c>:L1405-L1416</c>]. All three need a font, a device context or a unit
/// conversion, so constraint C-D places them under /v1/design/**; <see cref="Units"/> travels so the
/// deferred half knows which conversion the oracle would have chosen.
/// </para>
/// <para>
/// THE ORACLE'S OWN RETURN VALUE IS PRESERVED ON <see cref="Code"/>. The all-columns arity has exactly one
/// exit and always answers success [<c>:L1257</c>]; the single-column arity has three, answering
/// <c>E_INVALID_ARGUMENT</c> for an empty name [<c>:L1270</c>] and <c>FAILED</c> for a check-box or
/// radio-button column [<c>:L1272-L1275</c>].
/// </para>
/// </remarks>
public sealed record ColumnAutoWidthPlan
{
    /// <summary>The legacy return code.</summary>
    public required long Code { get; init; }

    /// <summary>
    /// One plan per column that survived the scan guards, in the order the oracle reaches them. EMPTY when
    /// <see cref="Code"/> reports a failure, and legitimately empty on success when every column was
    /// filtered out.
    /// </summary>
    public required IReadOnlyList<ColumnWidthComputation> Columns { get; init; }

    /// <summary>
    /// The DataWindow's unit setting, as the raw <c>Describe("DataWindow.Units")</c> answer
    /// (<c>:L1237</c>, <c>:L1405</c>).
    /// </summary>
    /// <remarks>
    /// THE RAW TOKEN, NOT AN ENUMERATION, because the oracle switches on the STRING and its default arm
    /// catches every unrecognised value including both sentinels - so an unreadable setting takes the
    /// PowerBuilder-unit arm rather than failing. Its four arms are <c>"1"</c> pixels, <c>"2"</c>
    /// thousandths of an inch, <c>"3"</c> thousandths of a centimetre and anything else PowerBuilder units.
    /// Mapping it to an enumeration here would have to invent a member for "anything else" and would lose
    /// the raw value a recording compares.
    /// </remarks>
    public required string Units { get; init; }
}


/// <summary>
/// The DataWindow context-menu service, headless half: the item store and its full editing surface, the
/// default item set, the emit filter and separator collapse, the four column operations, the two copy
/// producers and both automatic-width computations.
/// </summary>
/// <remarks>
/// <para>
/// PORTED FROM <c>ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_contextmenu.sru</c> (1,459
/// lines), which derives from <c>n_cst_dwsvc</c> [<c>:L4</c>, <c>:L23</c>] - so this type derives from
/// <see cref="DataWindowServiceBase"/>, that object's port.
/// </para>
/// <para>
/// <c>SEALED</c> BECAUSE NOTHING DERIVES FROM IT. A search of the legacy library for
/// <c>from n_cst_dwsvc_contextmenu</c> returns nothing, and <c>se_cst_dw</c> holds it as a concrete member
/// rather than as a base [<c>se_cst_dw.sru:L80</c>, created at <c>:L570</c>]. Sealing states that, and
/// keeps the ten preserved defects from being silently overridden away by a subclass.
/// </para>
/// <para>
/// THE SELF-SHADOWING GLOBAL IS NOT REPRODUCED. <c>:L31</c> declares
/// <c>global n_cst_dwsvc_contextmenu n_cst_dwsvc_contextmenu</c>, a global auto-instance shadowing its own
/// type name - an artefact of PowerBuilder's single flat namespace (AAP 0.4.5.1). AAP 0.5.4.2 fixes the
/// resolution: the type keeps the descriptive .NET name and the instance becomes an INJECTED DEPENDENCY.
/// Nothing here is static and mutable, which is also what makes the whole service testable against a host
/// test double with no DataWindow, no database and no UI (constraint C-H).
/// </para>
/// <para>
/// THE NESTED MENU THEME IS DEFERRED IN FULL AND HAS NO PLACEHOLDER OF ANY KIND. The oracle declares a
/// nested type deriving from the popup-menu theme class at <c>:L8</c>, holds an instance of it at
/// <c>:L29</c>, creates that instance in its constructor [<c>:L1432</c>] and defines the nested type at
/// <c>:L1449-L1450</c>; it is consumed at exactly one place, where the theme is copied onto the popup menu
/// before it is shown [<c>:L154</c>]. Theming is presentation from end to end, so constraint C-D excludes
/// all five sites: there is no theme type here, no theme member, and no theme-shaped placeholder. The
/// capability belongs to /v1/design/**.
/// </para>
/// <para>
/// NOT THREAD-SAFE, AND THE OBJECT IT PORTS WAS NOT EITHER. The item store, the clicked column, the
/// supplied clipboard text and the press row are plain instance state mutated across a call sequence,
/// exactly as <c>:L56-L61</c> declares them. One instance belongs to one attached host and is driven by
/// that host's event chain in order; concurrent use of a single instance is outside the contract, as it is
/// in the oracle. DECISION 5 makes the sequence explicit rather than implicit, which is the only reason a
/// caller can now get it wrong at all - hence <see cref="ContextMenuLayout.AwaitingSelection"/>.
/// </para>
/// <para>
/// IT IMPLEMENTS <see cref="IDataWindowContextMenuService"/>, WHICH IS ONE OF THE EVENT CHAIN'S
/// FIVE ATTACHED-SERVICE CONTRACTS. It declares NO member of its own beyond the two every attached
/// service has, so satisfying it costs nothing here - and DECLARING it is what lets the productive
/// attached-service factory hand this model to an event chain without an adapter in between. An
/// adapter would have been a type whose every member was a forward.
/// </para>
/// </remarks>
public sealed class ContextMenuModel : DataWindowServiceBase, IDataWindowContextMenuService
{
    // ==========================================================================================
    //  THE NINE RESERVED MENU IDENTIFIERS                     n_cst_dwsvc_contextmenu.sru:L37-L45
    //  ----------------------------------------------------------------------------------------
    //  SPELLINGS AND VALUES BOTH PRESERVED VERBATIM, per AAP 0.4.5.3: a menu identifier CROSSES THE
    //  WIRE ON EVERY SELECTION, appears in log records and appears in characterization recordings, so
    //  a rename would not restyle a symbol - it would silently invalidate every stored comparison
    //  that mentions it. The trailing comments are the oracle's own Chinese, carried across so the
    //  mapping from identifier to capability survives without a second lookup.
    //
    //  *** PLAIN `const uint` FIELDS, NEVER A C# ENUM, AND THE REASON IS DEFECT 1 BELOW. ***
    //  An enumeration with two members of one value either collapses them or forces an explicit-alias
    //  workaround, and either outcome changes the emitted metadata and invites a later "cleanup" that
    //  deletes one of the two names. Constants cannot collapse.
    //
    //  `uint` per AAP 0.4.5.2's mapping of PowerScript's 32-bit `unsignedlong`, matching
    //  Shared.Kernel Bits' fixed-width convention.
    // ==========================================================================================

    /// <summary>
    /// The first identifier reserved by the framework - 保留的开始ID, "the reserved start id"
    /// (<c>:L37</c>).
    /// </summary>
    /// <remarks>
    /// *** DEFECT 1, THE ALIAS COLLAPSE - PRESERVED VERBATIM. ***
    /// THIS CONSTANT AND <see cref="MID_COLAUTOWIDTH"/> ARE BOTH 10000. The oracle declares them on
    /// consecutive lines with the same value [<c>:L37-L38</c>] - the same aliasing pattern as
    /// <c>RetCode.OK</c>/<c>SUCCESS</c>/<c>ALLOW</c>, which are all zero. Both names are declared here
    /// and NEITHER is elided, NEITHER is expressed in terms of the other in a way that erases a name,
    /// and they are NOT in an enumeration.
    /// <para>
    /// THE OBSERVABLE CONSEQUENCE: <see cref="OnDefProc"/>'s dispatch has a
    /// <see cref="MID_COLAUTOWIDTH"/> arm [<c>:L205</c>], so a selection of 10000 ALWAYS runs the
    /// single-column automatic width and this identifier is UNREACHABLE as a distinct case. The
    /// boundary it was meant to mark - "application identifiers must stay below 10000" - therefore has
    /// no enforcement anywhere, and an application that used 10000 as its own identifier would silently
    /// trigger the framework's operation instead. That is the oracle's behaviour and it is reproduced;
    /// a test pins it.
    /// </para>
    /// </remarks>
    public const uint MID_RESERVED = 10000;

    /// <summary>Automatic column width for the clicked column - 自动列宽 (<c>:L38</c>).</summary>
    /// <remarks>
    /// 10000, THE SAME VALUE AS <see cref="MID_RESERVED"/> - see DEFECT 1 there. This is the identifier
    /// the dispatch actually recognises.
    /// </remarks>
    public const uint MID_COLAUTOWIDTH = 10000;

    /// <summary>
    /// Automatic column width for every column - 自动列宽（所有列）, "automatic column width (all
    /// columns)" (<c>:L39</c>).
    /// </summary>
    /// <remarks>
    /// The identifier of the auto-width parent's CHILD item [<c>:L274</c>], and of the FLAT item the
    /// header-clicked arm adds instead [<c>:L276</c>] - so the same operation is reachable from two
    /// different menu shapes.
    /// </remarks>
    public const uint MID_COLAUTOWIDTH_ALL = 10001;

    /// <summary>Tick every cell in the column - 勾选整列 (<c>:L40</c>).</summary>
    public const uint MID_COLCHECK = 10002;

    /// <summary>Clear the tick in every cell in the column - 清除勾选整列 (<c>:L41</c>).</summary>
    public const uint MID_COLUNCHECK = 10003;

    /// <summary>Invert the tick in every cell in the column - 反勾选整列 (<c>:L42</c>).</summary>
    /// <remarks>
    /// *** DEFECT 3 LIVES ON THIS IDENTIFIER *** - it is the one check item the emit filter does NOT
    /// suppress when the check capability is switched off. See <see cref="BuildMenu"/>.
    /// </remarks>
    public const uint MID_COLREVERTCHECK = 10004;

    /// <summary>Copy every value in the column - 复制整列值 (<c>:L43</c>).</summary>
    public const uint MID_COLCOPY = 10005;

    /// <summary>
    /// Overwrite the whole column from the clipboard - 拷贝粘帖板数据覆盖到整列 (<c>:L44</c>).
    /// </summary>
    public const uint MID_COLPASTE = 10006;

    /// <summary>Copy the clicked cell's value - 复制单元格值 (<c>:L45</c>).</summary>
    /// <remarks>
    /// *** DEFECT 4 LIVES ON THIS IDENTIFIER *** - the two paths that add it both RETURN IMMEDIATELY,
    /// so a menu that offers it offers nothing else. See <see cref="OnPrepare"/>.
    /// </remarks>
    public const uint MID_ITEMCOPY = 10007;

    // ==========================================================================================
    //  THE MESSAGE KEYS AND THE COMPOSITION LITERALS                              DECISION 7
    //  ----------------------------------------------------------------------------------------
    //  PUBLIC AND PascalCase, DELIBERATELY UNLIKE THE MID_* SET ABOVE. These are not legacy
    //  identifiers - the oracle writes them as INLINE STRING LITERALS at each of the ten dialog sites,
    //  with no constant name at all - so AAP 0.4.5.3, which preserves legacy constant SPELLINGS, has
    //  nothing to preserve here. What must be preserved is the string VALUE, byte for byte, because it
    //  is the localization LOOKUP KEY: a provider resolves the Chinese source text, so altering,
    //  normalising or relocating it to a resource file would silently stop every translation from
    //  matching. They are public so a test can assert the exact key without restating it.
    // ==========================================================================================

    /// <summary>
    /// The row-number fragment, which is BOTH a localization key and a <c>Sprintf</c> format string -
    /// 第{}行, "row {}" (all ten sites).
    /// </summary>
    /// <remarks>
    /// THE SEQUENTIAL EMPTY-BRACE FORM. <c>Formatting.Sprintf</c> serves one grammar in which an omitted
    /// index means "the next argument", so this key needs no index - while the three distinct-value
    /// predicates the width pass builds use the INDEXED <c>{1}</c> form of the same method. One
    /// formatter, two grammars; no second formatter is introduced.
    /// </remarks>
    public const string RowNumberMessageKey = "第{}行";

    /// <summary>
    /// The rejection fragment - 修改数据被拒绝, "the data change was refused" (<c>:L795</c>,
    /// <c>:L863</c>, <c>:L916</c>, <c>:L1052</c>).
    /// </summary>
    public const string ChangeRejectedMessageKey = "修改数据被拒绝";

    /// <summary>The invalid-value fragment - 无效的值, "invalid value" (<c>:L1009</c>).</summary>
    public const string InvalidValueMessageKey = "无效的值";

    /// <summary>
    /// The type-mismatch fragment - 数据类型不匹配, "the data type does not match" (<c>:L1018</c>,
    /// <c>:L1027</c>, <c>:L1033</c>, <c>:L1039</c>, <c>:L1045</c>).
    /// </summary>
    public const string TypeMismatchMessageKey = "数据类型不匹配";

    /// <summary>
    /// The line separator between the row fragment and the detail fragment - the legacy <c>"~n"</c>.
    /// </summary>
    /// <remarks>
    /// A BARE LINE FEED, NOT A CARRIAGE-RETURN PAIR. PowerScript's <c>~n</c> is one character, and the
    /// oracle uses the PAIR <c>~r~n</c> elsewhere in this very file - as the copy producer's row
    /// separator [<c>:L546</c>] and as the paste splitter's delimiter [<c>:L952</c>]. The two are
    /// distinct and are not interchangeable; see <see cref="RowSeparator"/>.
    /// </remarks>
    public const string LineSeparator = "\n";

    /// <summary>
    /// The exclamation mark every one of the ten messages ends its detail fragment with - the legacy
    /// <c>"!"</c> and <c>"!~n"</c>.
    /// </summary>
    /// <remarks>
    /// IT PRECEDES THE SECOND SEPARATOR, NOT THE VALUE. The oracle writes the four rejection sites as
    /// <c>... + "!"</c> and the six value-carrying sites as <c>... + "!~n" + sVal</c>, so the mark is
    /// attached to the DETAIL fragment in both shapes and the value follows a separator. Composing it
    /// after the value would produce different text.
    /// </remarks>
    public const string DetailSuffix = "!";

    // ==========================================================================================
    //  THE STORE AND SCAN LITERALS
    // ==========================================================================================

    /// <summary>
    /// The text that MAKES an item a separator - the legacy <c>"-"</c> (written at <c>:L511</c>, tested
    /// at <c>:L169</c>).
    /// </summary>
    /// <remarks>
    /// THE MARKER IS THE TEXT, AND THERE IS NO FLAG. The oracle inserts a separator by inserting an
    /// ordinary item whose label is a single hyphen and whose identifier is zero, and the emit filter
    /// recognises it by comparing that label. Introducing a boolean would create state the oracle does
    /// not have, would have to be invented on the wire too, and would let an item be a separator and
    /// carry a label at the same time - which is unrepresentable in the oracle.
    /// </remarks>
    public const string SeparatorText = "-";

    /// <summary>
    /// The row separator the column copy producer writes after EVERY value - the legacy <c>"~r~n"</c>
    /// (<c>:L546</c>), and the delimiter the paste splitter splits on (<c>:L952</c>).
    /// </summary>
    /// <remarks>
    /// A CARRIAGE-RETURN AND LINE-FEED PAIR, WHICH IS WHAT MAKES COPY AND PASTE EACH OTHER'S INVERSE.
    /// It is deliberately NOT <see cref="LineSeparator"/>: the dialog messages use a bare line feed and
    /// these two use the pair, and the difference is the oracle's.
    /// </remarks>
    public const string RowSeparator = "\r\n";

    /// <summary>
    /// The constant padding added to a measured width before the unit conversion - the legacy literal
    /// <c>4</c> (<c>:L1239</c>, <c>:L1407</c>).
    /// </summary>
    public const double WidthPadding = 4d;

    /// <summary>
    /// The drop-down arrow allowance, in DataWindow units - <c>constant real ARROW_BTN_WIDTH = 18</c>
    /// (<c>:L63</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// PRIVATE, EXACTLY AS THE ORACLE DECLARES IT - it sits under the <c>private:</c> block opened at
    /// <c>:L56</c>. It is NOT promoted to public merely because the plan record publishes its VALUE
    /// through <see cref="ColumnWidthComputation.ArrowButtonWidth"/>: publishing a computed allowance is
    /// not the same as publishing the constant, and the oracle's accessibility is part of what is being
    /// preserved (constraint C-B). Being private is also why it needs no naming-analyzer suppression of
    /// its own - CA1707 reports externally visible declarations only - although this file carries one
    /// anyway for the nine <c>MID_*</c> values.
    /// </para>
    /// <para>
    /// <c>double</c> per AAP 0.4.5.2's mapping of PowerScript's <c>real</c>. The width it is added to is
    /// also a real in the oracle, so keeping both floating point preserves the arithmetic exactly.
    /// </para>
    /// </remarks>
    private const double ARROW_BTN_WIDTH = 18d;

    /// <summary>
    /// The tag a submenu carries while its ownership is being transferred -
    /// <c>constant string PRP_POPUPMENUCREATOR = "{DWSVC_POPUPMENU_CREATOR}"</c> (<c>:L65</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// PRIVATE, EXACTLY AS THE ORACLE DECLARES IT, and for the same reason as
    /// <see cref="ARROW_BTN_WIDTH"/>: it is under the same <c>private:</c> block and is not promoted.
    /// </para>
    /// <para>
    /// ITS THREE CONSUMERS ARE ALL DEFERRED (DECISION 2). The create helper stamps a newly created child
    /// menu with it [<c>:L679</c>], and the insert path reads it to decide whether it has just been
    /// handed ownership, CLEARING it as it does so [<c>:L649-L651</c>] so a second transfer cannot claim
    /// ownership twice. The tag lives on a popup-menu object, which is a type constraint C-D excludes -
    /// so the VALUE is preserved here, verbatim including its braces, while the protocol that reads it
    /// belongs to /v1/design/**. It is preserved rather than dropped because it is the mechanism behind
    /// <see cref="MenuItemData.MenuOwner"/>, which does ship, and because a recording of a transferred
    /// child menu contains this exact string.
    /// </para>
    /// </remarks>
    private const string PRP_POPUPMENUCREATOR = "{DWSVC_POPUPMENU_CREATOR}";

    // ------------------------------------------------------------------------------------------
    //  THE TWO BROKER TOPICS THIS SERVICE SUBSCRIBES TO                      se_cst_dw.sru:L69, L72
    //  ----------------------------------------------------------------------------------------
    //  Private constants restating two of se_cst_dw's public EVT_* catalogue, following the same
    //  choice Services/ColumnSortModel.cs made for its three: the port of se_cst_dw is the event
    //  chain, whose constants are not published on the host contract, and a service needs only the
    //  topics it subscribes to. The SCREAMING_SNAKE spellings are the oracle's; being private they
    //  raise no naming diagnostic.
    // ------------------------------------------------------------------------------------------

    /// <summary>The right-button-press topic - <c>EVT_RBUTTONDOWN</c> (<c>se_cst_dw.sru:L69</c>).</summary>
    private const string EVT_RBUTTONDOWN = "rbuttondown";

    /// <summary>The right-button-release topic - <c>EVT_RBUTTONUP</c> (<c>se_cst_dw.sru:L72</c>).</summary>
    private const string EVT_RBUTTONUP = "rbuttonup";

    // ------------------------------------------------------------------------------------------
    //  THE BAND, TYPE AND PROPERTY TOKENS THE ORACLE COMPARES AS RAW STRINGS
    //  ----------------------------------------------------------------------------------------
    //  Named once each so that a comparison cannot drift between two sites that must agree, and so
    //  that the EXACTNESS of each comparison is visible at the point of declaration. Every one is an
    //  ordinal, case-sensitive comparison in the oracle, and several are PREFIX tests rather than
    //  equality tests - the difference is called out where it matters.
    // ------------------------------------------------------------------------------------------

    /// <summary>The header band token (<c>:L237</c>, <c>:L246</c>, <c>:L253</c>, <c>:L1151</c>).</summary>
    private const string HeaderBand = "header";

    /// <summary>The foreground band token (<c>:L235</c>, <c>:L246</c>).</summary>
    private const string ForegroundBand = "foreground";

    /// <summary>The background band token (<c>:L235</c>, <c>:L246</c>).</summary>
    private const string BackgroundBand = "background";

    /// <summary>The detail band token (<c>:L1103</c>).</summary>
    private const string DetailBand = "detail";

    /// <summary>The summary band token (<c>:L1121</c>, <c>:L1287</c>).</summary>
    private const string SummaryBand = "summary";

    /// <summary>The footer band token (<c>:L1121</c>, <c>:L1287</c>).</summary>
    private const string FooterBand = "footer";

    /// <summary>
    /// The trailer band PREFIX - tested as <c>Left(sBand,7)</c> (<c>:L1121</c>, <c>:L1287</c>).
    /// </summary>
    private const string TrailerBandPrefix = "trailer";

    /// <summary>The column object type token (<c>:L242</c>, <c>:L259</c>, <c>:L319</c>).</summary>
    private const string ColumnObjectType = "column";

    /// <summary>The computed-field object type token (<c>:L242</c>, <c>:L245</c>, <c>:L259</c>).</summary>
    private const string ComputeObjectType = "compute";

    /// <summary>The text object type token (<c>:L1136</c>, <c>:L1302</c>).</summary>
    private const string TextObjectType = "text";

    /// <summary>The check-box edit style token (<c>:L286</c>, <c>:L521</c>, <c>:L1108</c>).</summary>
    private const string CheckBoxEditStyle = "checkbox";

    /// <summary>
    /// The radio-button edit style token AS THIS FILE SPELLS IT - PLURAL (<c>:L1108</c>,
    /// <c>:L1273</c>).
    /// </summary>
    /// <remarks>
    /// *** A GENUINE CROSS-FILE INCONSISTENCY IN THE LEGACY, PRESERVED PER FILE. ***
    /// This file spells it <c>"radiobuttons"</c> while <c>n_cst_dwsvc_columnsort.sru:L108</c> spells the
    /// same DataWindow property answer <c>"radiobutton"</c>, SINGULAR. One of the two cannot be matching
    /// anything, but which one depends on what the runtime actually reports and the repository does not
    /// say - so neither is corrected and neither is harmonised with the other (constraint C-B). Each
    /// file keeps its own spelling; a future characterization run against the oracle decides which
    /// branch is live, and that decision must be recorded rather than guessed.
    /// </remarks>
    private const string RadioButtonsEditStyle = "radiobuttons";

    /// <summary>The header-object name suffix - <c>"_t"</c> (<c>:L256</c>).</summary>
    private const string HeaderTextSuffix = "_t";

    /// <summary>The affirmative answer PowerBuilder gives for a boolean property (<c>:L959</c>).</summary>
    private const string YesAnswer = "yes";

    /// <summary>The negative answer PowerBuilder gives for a boolean property (<c>:L971</c>).</summary>
    private const string NoAnswer = "no";

    /// <summary>The invisible answer PowerBuilder gives for a visibility property (<c>:L1104</c>).</summary>
    private const string HiddenAnswer = "0";

    /// <summary>
    /// The DataWindow's header-height property, read to promote a band (<c>:L236</c>).
    /// </summary>
    private const string HeaderHeightProperty = "DataWindow.Header.Height";

    /// <summary>The DataWindow's unit-setting property (<c>:L1237</c>, <c>:L1405</c>).</summary>
    private const string UnitsProperty = "DataWindow.Units";

    /// <summary>
    /// The <c>Format</c> property name, read through the DWO-property helper for a heading candidate
    /// (<c>:L1142</c>, <c>:L1308</c>).
    /// </summary>
    /// <remarks>
    /// READ TWO DIFFERENT WAYS IN THE ORACLE, DELIBERATELY PRESERVED. A heading candidate reads it
    /// through <c>_of_GetDWOProp</c>, which RESOLVES an expression-form property by evaluating it at row
    /// zero; the column's own format is read with a RAW <c>Describe</c> [<c>:L1167</c>, <c>:L1333</c>]
    /// and its expression form is then detected and evaluated PER ROW. So a heading gets one
    /// unconditional mask and a computed column can get a different mask on every row.
    /// </remarks>
    private const string FormatProperty = "Format";

    /// <summary>
    /// The font weight the oracle compares against to decide bold - the literal <c>"700"</c>
    /// (<c>:L1127</c>, <c>:L1178</c>, <c>:L1293</c>, <c>:L1344</c>).
    /// </summary>
    /// <remarks>
    /// AN EXACT STRING COMPARISON, NOT A NUMERIC THRESHOLD, at all four sites. See
    /// <see cref="TextMeasurementCandidate.Bold"/> for why that is preserved rather than widened to a
    /// numeric test.
    /// </remarks>
    private const string BoldFontWeight = "700";

    /// <summary>
    /// The wrapping width the oracle sets before every wrapped measurement - the bare literal
    /// <c>1024</c> (<c>:L1152</c>, <c>:L1209</c>, <c>:L1318</c>, <c>:L1375</c>).
    /// </summary>
    /// <remarks>
    /// UNEXPLAINED IN THE ORACLE AND THEREFORE CARRIED RATHER THAN DERIVED. It is not the column's
    /// width, not the DataWindow's and not the band's; naming it here at least makes the four sites'
    /// agreement visible. It travels to the deferred measurer as
    /// <see cref="TextMeasurementCandidate.WrapWidth"/>.
    /// </remarks>
    private const int WrapWidthLimit = 1024;

    /// <summary>
    /// The drop-down DataWindow edit style - <c>"dddw"</c> (<c>:L1227</c>, <c>:L1395</c>).
    /// </summary>
    private const string DropDownDataWindowEditStyle = "dddw";

    /// <summary>
    /// The drop-down list-box edit style - <c>"ddlb"</c> (<c>:L1227</c>, <c>:L1395</c>).
    /// </summary>
    private const string DropDownListBoxEditStyle = "ddlb";

    /// <summary>
    /// The invalid-expression sentinel a <c>Describe</c> or an evaluation answers - <c>"!"</c>.
    /// </summary>
    /// <remarks>
    /// SINGLE-SOURCED FROM THE EVALUATOR so that the sentinel this service tests for and the sentinel the
    /// evaluator produces cannot drift apart. The oracle tests it at <c>:L776</c>, <c>:L830</c>,
    /// <c>:L832</c>, <c>:L897</c>, <c>:L968</c>, <c>:L974</c>, <c>:L1141</c>, <c>:L1143</c>,
    /// <c>:L1195</c>, <c>:L1309</c>, <c>:L1334</c> and <c>:L1361</c>.
    /// </remarks>
    private const string InvalidExpressionSentinel = DataWindowExpressionEvaluator.InvalidExpressionSentinel;

    /// <summary>
    /// The undetermined-value sentinel a <c>Describe</c> answers - <c>"?"</c>. Single-sourced from the
    /// evaluator for the same reason as <see cref="InvalidExpressionSentinel"/>.
    /// </summary>
    private const string UndeterminedValueSentinel = DataWindowExpressionEvaluator.UndeterminedValueSentinel;

    /// <summary>
    /// The general-format marker, which the oracle treats as "no format" - <c>"[general]"</c>, compared
    /// CASE-INSENSITIVELY (<c>:L1143</c>, <c>:L1168</c>, <c>:L1309</c>, <c>:L1334</c>).
    /// </summary>
    /// <remarks>
    /// THE ONLY CASE-INSENSITIVE COMPARISON IN THE ENTIRE FILE, and the oracle achieves it by lowering
    /// the value before comparing. Every other token comparison here is ordinal and case-sensitive, so
    /// the exception is called out rather than left to be noticed.
    /// </remarks>
    private const string GeneralFormatMarker = "[general]";

    // ==========================================================================================
    //  PRIVATE STATE                                          n_cst_dwsvc_contextmenu.sru:L56-L61
    //  ----------------------------------------------------------------------------------------
    //  FOUR FIELDS, ALL FOUR PRIVATE IN THE ORACLE TOO, AND THREE OF THEM PER-INVOCATION.
    //  The store, the clicked column and the supplied clipboard text are all cleared by the cleanup
    //  at :L198-L201 (DECISION 6); only the press row survives across invocations, and it is cleared
    //  unconditionally by the release handler [:L127].
    // ==========================================================================================

    /// <summary>
    /// The item store - <c>MENUITEMDATA Items[]</c> (<c>:L57</c>), the oracle's own comment being
    /// 添加的自定义菜单项, "the custom menu items that have been added".
    /// </summary>
    /// <remarks>
    /// A <see cref="List{T}"/> STANDING IN FOR A ONE-BASED POWERSCRIPT ARRAY. Every ported access goes
    /// through the four helpers in the ONE-BASED STORE ACCESS region, so the index translation exists in
    /// exactly four places and nowhere else in this file (AAP 0.4.5.4).
    /// </remarks>
    private readonly List<MenuItemData> _items = [];

    /// <summary>
    /// The row the right button went down on - <c>long _nRowRButtonDown</c> (<c>:L58</c>), the oracle's
    /// comment being 右键按下的行（用于产生RButtonClicked事件）, "the row the right button was pressed on
    /// (used to raise the RButtonClicked event)".
    /// </summary>
    /// <remarks>
    /// A PRESS-AND-RELEASE MATCH GATE, NOT A SELECTION. It exists so that a press on one row and a
    /// release on another produces no menu at all - see <see cref="OnRButtonUp"/>.
    /// </remarks>
    private long _nRowRButtonDown;

    /// <summary>
    /// The column the header click resolved to - <c>string _sColClicked</c> (<c>:L60</c>).
    /// </summary>
    /// <remarks>
    /// SET BY PREPARATION AND READ BY THE DISPATCH, WHICH IS WHY THE CLEANUP CANNOT RUN BETWEEN THEM.
    /// Six of the eight dispatch arms pass it as their operand [<c>:L206</c>, <c>:L210</c>, <c>:L212</c>,
    /// <c>:L214</c>, <c>:L216</c>, <c>:L218</c>], and it is EMPTY unless a header object resolved to a
    /// column or computed field [<c>:L257-L264</c>] - so those arms can legitimately receive an empty
    /// name and each answers its own argument error for it.
    /// </remarks>
    private string _sColClicked = string.Empty;

    /// <summary>
    /// The clipboard text supplied for this invocation - <c>string _sClipText</c> (<c>:L61</c>).
    /// </summary>
    /// <remarks>
    /// THE INVERTED READ OF DECISION 3. The oracle fills it from the system clipboard at <c>:L299</c>
    /// while building the paste item; here it is copied out of
    /// <see cref="ContextMenuPointerContext.ClipboardText"/> at the same point in the same sequence, so
    /// the ORDER of the read relative to the item add is unchanged. The dispatch reads it at
    /// <c>:L216</c>.
    /// </remarks>
    private string _sClipText = string.Empty;

    // ------------------------------------------------------------------------------------------
    //  INJECTED COLLABORATORS
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// The localization facade - the port of the global <c>I18N</c> function
    /// (<c>ws_objects/pfw.ui.pbl.src/i18n.srf</c>), reached 20 times in the oracle.
    /// </summary>
    /// <remarks>
    /// AN INJECTED INSTANCE, NOT A STATIC, because its provider slot is an instance field: the legacy's
    /// provider is the global auto-instance the function tests for validity, and AAP 0.4.5.1 resolves a
    /// global auto-instance into an injected dependency. It is what makes a test able to install a
    /// provider for one service without affecting another.
    /// </remarks>
    private readonly I18n _i18n;

    /// <summary>
    /// The DataWindow expression evaluator - the port of the base's <c>_of_Evaluate</c>
    /// (<c>n_cst_dwsvc.sru:L49-L50</c>), reached at TEN sites in the oracle.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE TEN SITES, EXACTLY: <c>:L1130</c>, <c>:L1132</c>, <c>:L1133</c>, <c>:L1194</c>, <c>:L1197</c>
    /// in the all-columns pass and <c>:L1296</c>, <c>:L1298</c>, <c>:L1299</c>, <c>:L1360</c>,
    /// <c>:L1363</c> in the single-column pass - five each, the two passes being near-duplicates of one
    /// another in the oracle.
    /// </para>
    /// <para>
    /// INJECTED RATHER THAN CONSTRUCTED so that a test can answer a fixed value for a fixed expression
    /// with no DataWindow behind it (constraint C-H), and so that the pinyin matcher and page resolver it
    /// carries are the ones the rest of the service uses. AAP 0.4.2.5 records that
    /// <c>Describe("Evaluate(...)")</c> is the largest net-new obligation in the whole refactor; this
    /// service is one of its consumers and none of that obligation is discharged here.
    /// </para>
    /// </remarks>
    private readonly DataWindowExpressionEvaluator _evaluator;

    /// <summary>
    /// Creates the service.
    /// </summary>
    /// <param name="i18n">The localization facade.</param>
    /// <param name="evaluator">The DataWindow expression evaluator.</param>
    /// <param name="options">
    /// The service options, whose <c>ContextMenu</c> section supplies the five capability toggles.
    /// </param>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// THE FIVE TOGGLES ARE BOUND HERE AND NOT DUPLICATED. <c>:L49-L53</c> declares them as public
    /// instance fields defaulting to <see langword="true"/>, and
    /// <c>Configuration/DataServicesOptions.cs</c> already declares the same five with the same defaults
    /// - so binding rather than restating keeps ONE authoritative default per toggle. AAP 0.4.5.5
    /// requires every value the legacy hardcodes to move to configuration; a toggle whose legacy default
    /// is <see langword="true"/> keeps that default and becomes overridable, which is the same treatment
    /// the hardcoded locale receives.
    /// </para>
    /// <para>
    /// NO HOST IS TAKEN HERE. The oracle is created by <c>se_cst_dw</c> before the DataWindow exists
    /// [<c>se_cst_dw.sru:L570</c>] and attached afterwards [<c>:L576-L580</c>], and
    /// <see cref="DataWindowServiceBase.OnInit"/> is that attachment - so a host argument here would
    /// invert the legacy lifetime.
    /// </para>
    /// </remarks>
    public ContextMenuModel(
        I18n i18n,
        DataWindowExpressionEvaluator evaluator,
        IOptions<DataServicesOptions> options)
    {
        ArgumentNullException.ThrowIfNull(i18n);
        ArgumentNullException.ThrowIfNull(evaluator);
        ArgumentNullException.ThrowIfNull(options);

        _i18n = i18n;
        _evaluator = evaluator;

        ContextMenuOptions configured = options.Value.ContextMenu;

        // :L49-L53 - the five public toggles, each defaulting to true in the oracle and in the options.
        ColAutoWidth = configured.ColAutoWidth;
        ColCheck = configured.ColCheck;
        ColCopy = configured.ColCopy;
        ColPaste = configured.ColPaste;
        ItemCopy = configured.ItemCopy;
    }

    // ==========================================================================================
    //  THE FIVE CAPABILITY TOGGLES                            n_cst_dwsvc_contextmenu.sru:L49-L53
    //  ----------------------------------------------------------------------------------------
    //  PUBLIC AND SETTABLE, exactly as the oracle declares them - plain public instance fields under
    //  the `public:` block opened at :L48, with no accessor and no validation. A caller may flip one
    //  between invocations and the next menu reflects it, which is what the oracle allows.
    //
    //  NAMED WITHOUT THE LEGACY '#' PREFIX. PowerBuilder spells a public property `#ColAutoWidth`;
    //  the '#' is its own sigil for a framework property and is not part of any wire payload, log
    //  record or characterization recording, so AAP 0.4.5.3 does not reach it - unlike the MID_*
    //  values, whose spellings it does reach.
    //
    //  *** THEY ARE NOT ALL CONSULTED IN THE SAME PLACE, AND THAT ASYMMETRY IS DEFECT 2. ***
    //  Four of the five are consulted TWICE - once while the default set is built and again by the
    //  emit filter - while ColAutoWidth is consulted ONLY while the set is built. See BuildMenu.
    // ==========================================================================================

    /// <summary>
    /// Whether the automatic-column-width items are offered - <c>#ColAutoWidth</c> (<c>:L49</c>).
    /// </summary>
    /// <remarks>
    /// *** DEFECT 2: THE EMIT FILTER DOES NOT CONSULT THIS TOGGLE. *** It gates only at ADD time
    /// [<c>:L268</c>]. So clearing it after the default set has been built leaves both auto-width items
    /// in the emitted menu, whereas clearing any of the other four suppresses their items at emit time
    /// even when they are already in the store. See <see cref="BuildMenu"/>, where the absence is
    /// annotated at the point where the other four appear.
    /// </remarks>
    public bool ColAutoWidth { get; set; }

    /// <summary>
    /// Whether the tick, clear-tick and invert-tick items are offered - <c>#ColCheck</c> (<c>:L50</c>).
    /// </summary>
    /// <remarks>
    /// *** DEFECT 3: IT SUPPRESSES ONLY TWO OF THE THREE ITEMS IT ADDS. *** At add time it gates all
    /// three [<c>:L283</c>], but the emit filter tests only the tick and clear-tick identifiers
    /// [<c>:L157-L159</c>] - so an already-built menu keeps its invert-tick item when this is cleared.
    /// See <see cref="BuildMenu"/>.
    /// </remarks>
    public bool ColCheck { get; set; }

    /// <summary>Whether the copy-column item is offered - <c>#ColCopy</c> (<c>:L51</c>).</summary>
    public bool ColCopy { get; set; }

    /// <summary>Whether the paste-column item is offered - <c>#ColPaste</c> (<c>:L52</c>).</summary>
    public bool ColPaste { get; set; }

    /// <summary>Whether the copy-cell item is offered - <c>#ItemCopy</c> (<c>:L53</c>).</summary>
    public bool ItemCopy { get; set; }

    // ==========================================================================================
    //  OBSERVATION SEAMS AND PENDING RESULTS
    //  ----------------------------------------------------------------------------------------
    //  The three Pending* members exist because the oracle's dispatch event is VOID and DISCARDS every
    //  result: :L204-L222 calls eight operations and keeps none of their return values, which is
    //  tolerable when each one either mutated the DataWindow or put text on the system clipboard, and
    //  is not tolerable when the DataWindow is remote and there is no clipboard. They are how a caller
    //  recovers what the oracle threw away, and they add no behaviour - each is written exactly where
    //  the oracle produced the value.
    //
    //  The four `internal` seams are read-only projections of private state, scoped to the test
    //  assembly by the InternalsVisibleTo this project declares, so the oracle's `private`
    //  encapsulation intent survives while the defect-pinning tests can still see the state they must
    //  assert on (constraint C-H).
    // ==========================================================================================

    /// <summary>
    /// The structured error the most recent operation produced, or <see langword="null"/> - the
    /// replacement for whichever of the ten dialogs would have been shown (DECISION 7).
    /// </summary>
    /// <remarks>
    /// CLEARED AT THE START OF EVERY OPERATION THAT CAN SET IT, so a stale error from a previous call can
    /// never be mistaken for a fresh one. At most one is ever pending: every producing site returns
    /// immediately after setting it, exactly where the oracle returns after showing the dialog.
    /// </remarks>
    public ContextMenuError? PendingError { get; private set; }

    /// <summary>
    /// The text the most recent copy operation composed, or <see langword="null"/> - what the oracle
    /// would have written to the system clipboard (DECISION 3).
    /// </summary>
    /// <remarks>
    /// SET BY THE DISPATCH, WHICH DISCARDS THE COPY OPERATION'S RESULT ENTIRELY [<c>:L218</c>,
    /// <c>:L220</c>]. A caller invoking <see cref="CopyColumnToText"/> or <see cref="CopyItemToText"/>
    /// directly receives the same text as a return value and does not need this.
    /// </remarks>
    public string? PendingCopiedText { get; private set; }

    /// <summary>
    /// The measurement plan the most recent automatic-width operation produced, or
    /// <see langword="null"/> (DECISION 8).
    /// </summary>
    /// <remarks>
    /// SET BY THE DISPATCH for the same reason as <see cref="PendingCopiedText"/> - <c>:L206</c> and
    /// <c>:L208</c> discard the result. This is the hand-off surface the deferred rendering half under
    /// /v1/design/** consumes.
    /// </remarks>
    public ColumnAutoWidthPlan? PendingWidthPlan { get; private set; }

    /// <summary>
    /// The item store, in order - a read-only view of <c>Items[]</c> (<c>:L57</c>).
    /// </summary>
    /// <remarks>
    /// ZERO-BASED, because it is an ordinary .NET sequence handed to a caller rather than a ported
    /// PowerScript array. The one-based discipline governs the PORTED LOGIC inside this class; exporting
    /// a one-based view would push that convention onto every consumer and invite exactly the off-by-one
    /// it exists to prevent.
    /// </remarks>
    internal IReadOnlyList<MenuItemData> StoreItems => _items;

    /// <summary>
    /// The column the header click resolved to - <c>_sColClicked</c> (<c>:L60</c>).
    /// </summary>
    /// <remarks>
    /// THE OBSERVATION POINT FOR THE DEFAULT-ARM CLEAR at <c>:L263</c>: a test drives preparation with a
    /// header object whose underlying object is neither a column nor a computed field and asserts this is
    /// EMPTY rather than the derived name, which pins the fact that the oracle resolves the name first
    /// and unresolves it afterwards.
    /// </remarks>
    internal string ClickedColumn => _sColClicked;

    /// <summary>
    /// The clipboard text captured for this invocation - <c>_sClipText</c> (<c>:L61</c>).
    /// </summary>
    internal string CapturedClipboardText => _sClipText;

    /// <summary>
    /// The row the right button went down on - <c>_nRowRButtonDown</c> (<c>:L58</c>).
    /// </summary>
    internal long PressedRow => _nRowRButtonDown;

    // ==========================================================================================
    //  ONE-BASED STORE ACCESS - THE ONLY FOUR PLACES AN INDEX IS TRANSLATED
    //  ----------------------------------------------------------------------------------------
    //  AAP 0.4.5.4 names one-based-to-zero-based translation the single most dangerous mechanical
    //  hazard in this refactor, and risk R9 records why: a silent off-by-one is indistinguishable from
    //  a behavioural regression. This file is the densest in its folder for it - every store scan is
    //  `for nIndex = 1 to UpperBound(Items)`, the append idiom is `UpperBound(Items) + 1`, and the
    //  whole positional addressing surface is one-based.
    //
    //  THE MITIGATION IS CENTRALISATION, WHICH IS THE STRONGER OF THE TWO OPTIONS AAP 0.4.5.4 ALLOWS.
    //  No `- 1` appears anywhere in this file outside these four members. Anything that looks like a
    //  fifth translation is a bug.
    // ==========================================================================================

    /// <summary>
    /// The store's upper bound - the port of <c>UpperBound(Items)</c>.
    /// </summary>
    /// <returns>
    /// The count, which for a one-based array IS the last valid index. <c>0</c> for an empty store, and
    /// that is not a sentinel - it is the honest answer, and it is what <c>if nCount = 0</c>
    /// [<c>:L150</c>] relies on.
    /// </returns>
    /// <remarks>
    /// THE WHOLE HAZARD IN ONE SENTENCE: PowerScript's <c>UpperBound</c> answers the LAST INDEX while
    /// .NET's <c>Count</c> answers ONE PAST THE LAST INDEX - and for a one-based array those are the same
    /// number. So this looks like a pointless wrapper and is not one: it is the place the equivalence is
    /// asserted, named and tested, instead of being re-derived at a dozen call sites.
    /// </remarks>
    private int UpperBound()
    {
        return _items.Count;
    }

    /// <summary>
    /// Reads one item by its ONE-BASED index - the port of <c>Items[n]</c>.
    /// </summary>
    /// <param name="oneBasedIndex">The one-based index. The first item is <c>1</c>, not <c>0</c>.</param>
    /// <returns>The item at that position.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="oneBasedIndex"/> is less than <c>1</c> or greater than <see cref="UpperBound"/>.
    /// </exception>
    /// <remarks>
    /// THE RANGE CHECK IS A FAIL-FAST SUBSTITUTION, NOT A NEW BEHAVIOUR. PowerScript raises on an
    /// out-of-range array read too; naming the argument makes the failure legible instead of surfacing as
    /// a bare index fault one frame deeper. Every ported call site is bounded by the oracle's own guard
    /// BEFORE reaching here - with ONE deliberate exception, <c>GetId(int)</c>, whose preserved
    /// defect reads position zero and whose port therefore never calls this at all.
    /// </remarks>
    private MenuItemData ItemAt(int oneBasedIndex)
    {
        if (oneBasedIndex < 1 || oneBasedIndex > UpperBound())
        {
            throw new ArgumentOutOfRangeException(
                nameof(oneBasedIndex),
                oneBasedIndex,
                "A one-based menu-item index must be between 1 and UpperBound inclusive.");
        }

        return _items[oneBasedIndex - 1];
    }

    /// <summary>
    /// Replaces one item by its ONE-BASED index - the port of an assignment to a field of
    /// <c>Items[n]</c>.
    /// </summary>
    /// <param name="oneBasedIndex">The one-based index.</param>
    /// <param name="item">The replacement item.</param>
    /// <exception cref="ArgumentOutOfRangeException">The index is out of range.</exception>
    /// <remarks>
    /// A WHOLE-ELEMENT REPLACEMENT WHERE THE ORACLE MUTATES ONE FIELD IN PLACE [<c>:L412</c>,
    /// <c>:L426</c>, <c>:L440</c>, <c>:L472</c>]. That is observationally identical for an immutable
    /// record held in a list, and it makes every state transition an explicit assignment rather than a
    /// hidden field write.
    /// </remarks>
    private void SetItemAt(int oneBasedIndex, MenuItemData item)
    {
        // The range check is ItemAt's, reused rather than duplicated, so both directions of the boundary
        // are defined in exactly one place.
        _ = ItemAt(oneBasedIndex);

        _items[oneBasedIndex - 1] = item;
    }

    /// <summary>
    /// Inserts one item at a ONE-BASED index, shifting the rest up - the port of the shift loop and the
    /// assignment at <c>:L494-L497</c>.
    /// </summary>
    /// <param name="oneBasedIndex">
    /// The one-based position to occupy, between <c>1</c> and <see cref="UpperBound"/> PLUS ONE
    /// inclusive - the append case being the plus one.
    /// </param>
    /// <param name="item">The item to insert.</param>
    /// <exception cref="ArgumentOutOfRangeException">The index is out of range.</exception>
    /// <remarks>
    /// <para>
    /// THE ORACLE'S SHIFT LOOP RUNS BACKWARDS AND THAT IS NOT A CLUE ABOUT ORDER - IT IS HOW AN IN-PLACE
    /// SHIFT UP MUST RUN. <c>for i = UpperBound(Items) + 1 to nIndex + 1 step -1</c> copies each element
    /// to the slot above it, starting from the top so nothing is overwritten before it is read, and then
    /// <c>Items[nIndex] = newItem</c> fills the hole. A forward loop would smear the last element over
    /// the whole tail. <see cref="List{T}.Insert"/> performs exactly this and preserves the relative order
    /// of everything at or above the position, so the two are observationally identical.
    /// </para>
    /// <para>
    /// THE APPEND CASE IS THE SAME CODE PATH, NOT A SPECIAL ONE. Every add overload reaches insertion with
    /// <c>UpperBound(Items) + 1</c> [<c>:L342</c>, <c>:L351</c>], at which the loop body executes zero
    /// times and the assignment lands one past the end - which for a PowerScript variable-length array
    /// extends it. That is why the range check admits <see cref="UpperBound"/> plus one.
    /// </para>
    /// </remarks>
    private void InsertItemAt(int oneBasedIndex, MenuItemData item)
    {
        if (oneBasedIndex < 1 || oneBasedIndex > UpperBound() + 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(oneBasedIndex),
                oneBasedIndex,
                "A one-based menu-item insert position must be between 1 and UpperBound + 1 inclusive.");
        }

        _items.Insert(oneBasedIndex - 1, item);
    }



    // ==========================================================================================
    //  THE ITEM STORE'S EDITING SURFACE          n_cst_dwsvc_contextmenu.sru forward prototypes
    //  ----------------------------------------------------------------------------------------
    //  EVERY MEMBER'S RETURN TYPE AND FAILURE VALUE IS THE ORACLE'S, AND THEY ARE NOT NORMALISED ONTO
    //  ONE RESULT TYPE. The add, insert and index family answers `integer`; the mutators answer `long`
    //  return codes; the identifier lookups answer `unsignedlong`; the readers answer `string` or
    //  `boolean`. That inconsistency is the oracle's public surface and flattening it would change what
    //  a caller can distinguish - most sharply on the insert path, which answers a POSITION on success
    //  and a NEGATIVE RETURN CODE on failure through the same `integer`.
    //
    //  *** DEFECT 9: TWO DIFFERENT OUT-OF-RANGE CODES FOR THE SAME CLASS OF FAULT. *** The insert path
    //  answers E_OUT_OF_RANGE [:L486, :L643] while every setter and reader answers E_OUT_OF_BOUND
    //  [:L410, :L424, :L438, :L470, :L756]. Both are preserved exactly where the oracle puts them.
    //
    //  `byPosition` IS ONE-BASED THROUGHOUT. When it is set, the identifier argument IS the position -
    //  the oracle assigns `nIndex = id` directly [:L406, :L420, :L434, :L466, :L482, :L557, :L569,
    //  :L581, :L593, :L639, :L752] - so a caller passing 0 addresses nothing and a caller passing
    //  GetCount() addresses the last item.
    // ==========================================================================================

    /// <summary>
    /// Resolves an identifier or a position to a ONE-BASED store index - the port of the
    /// <c>if byPosition then nIndex = id else nIndex = of_GetIndex(id)</c> preamble that opens eleven
    /// members.
    /// </summary>
    /// <param name="id">The identifier, or the ONE-BASED position when <paramref name="byPosition"/>.</param>
    /// <param name="byPosition">Whether <paramref name="id"/> is a position rather than an identifier.</param>
    /// <returns>
    /// The one-based index, which may be OUT OF RANGE - the oracle checks the range at each call site
    /// AFTER this resolution, and each site answers its own code for it.
    /// </returns>
    /// <remarks>
    /// THE CAST IS THE WHOLE REASON THIS IS A NAMED HELPER. PowerScript assigns an <c>unsignedlong</c>
    /// straight into an <c>integer</c> local, which is a NARROWING it performs silently; C# will not, so
    /// the conversion has to be written - and writing it once, here, is what stops eleven sites from each
    /// choosing their own. An identifier above <see cref="int.MaxValue"/> narrows to a negative index,
    /// which every caller's range check then rejects; that is the same outcome the oracle reaches.
    /// </remarks>
    private int ResolveIndex(uint id, bool byPosition)
    {
        return byPosition ? unchecked((int)id) : GetIndex(id);
    }

    /// <summary>
    /// Appends an item - the port of the five-argument <c>of_AddMenu</c> (<c>:L342</c>).
    /// </summary>
    /// <param name="text">The label. An EMPTY label appends nothing and answers <c>0</c> [<c>:L480</c>].</param>
    /// <param name="image">The image token.</param>
    /// <param name="tipText">The tooltip text.</param>
    /// <param name="enabled">Whether the item is selectable.</param>
    /// <param name="id">The identifier.</param>
    /// <returns>The one-based position it landed at, or a failure value.</returns>
    /// <remarks>
    /// THE LONG FORM, AND THE ONLY ONE THAT REACHES THE INSERT PATH DIRECTLY. It appends by passing
    /// <c>UpperBound(Items) + 1</c> as the position with <c>byPosition</c> set - so "append" is not a
    /// distinct operation in the oracle, it is an insert one past the end.
    /// </remarks>
    public int AddMenu(in string text, in string image, in string tipText, in bool enabled, in uint id)
    {
        // :L342  return of_InsertMenu(UpperBound(Items) + 1,true,text,image,tipText,enabled,id)
        return InsertMenu(unchecked((uint)(UpperBound() + 1)), true, text, image, tipText, enabled, id);
    }

    /// <summary>
    /// Appends an enabled item with no tooltip - the port of the three-argument <c>of_AddMenu</c>
    /// (<c>:L345</c>).
    /// </summary>
    /// <param name="text">The label.</param>
    /// <param name="image">The image token.</param>
    /// <param name="id">The identifier.</param>
    /// <returns>The one-based position it landed at, or a failure value.</returns>
    /// <remarks>
    /// DEFAULTS THE TOOLTIP TO THE EMPTY STRING AND NOT TO THE LABEL, and defaults enabled to
    /// <see langword="true"/> - the oracle's own argument order being <c>(text,image,"",true,id)</c>.
    /// </remarks>
    public int AddMenu(in string text, in string image, in uint id)
    {
        // :L345  return of_AddMenu(text,image,"",true,id)
        return AddMenu(text, image, string.Empty, true, id);
    }

    /// <summary>
    /// Appends an item with no tooltip - the port of the four-argument <c>of_AddMenu</c> that takes the
    /// enabled flag (<c>:L348</c>).
    /// </summary>
    /// <param name="text">The label.</param>
    /// <param name="image">The image token.</param>
    /// <param name="enabled">Whether the item is selectable.</param>
    /// <param name="id">The identifier.</param>
    /// <returns>The one-based position it landed at, or a failure value.</returns>
    public int AddMenu(in string text, in string image, in bool enabled, in uint id)
    {
        // :L348  return of_AddMenu(text,image,"",enabled,id)
        return AddMenu(text, image, string.Empty, enabled, id);
    }

    /// <summary>
    /// Appends an enabled item with a tooltip - the port of the four-argument <c>of_AddMenu</c> that takes
    /// the tooltip (<c>:L354</c>).
    /// </summary>
    /// <param name="text">The label.</param>
    /// <param name="image">The image token.</param>
    /// <param name="tipText">The tooltip text.</param>
    /// <param name="id">The identifier.</param>
    /// <returns>The one-based position it landed at, or a failure value.</returns>
    /// <remarks>
    /// THE OVERLOAD THE DEFAULT ITEM SET USES for all seven of its flat items [<c>:L243</c>,
    /// <c>:L247</c>, <c>:L276</c>, <c>:L287-L289</c>, <c>:L295</c>, <c>:L301</c>].
    /// </remarks>
    public int AddMenu(in string text, in string image, in string tipText, in uint id)
    {
        // :L354  return of_AddMenu(text,image,tipText,true,id)
        return AddMenu(text, image, tipText, true, id);
    }

    /// <summary>
    /// Appends a separator - the port of <c>of_AddSeparator</c> (<c>:L351</c>).
    /// </summary>
    /// <returns>The one-based position it landed at, or a failure value.</returns>
    /// <remarks>
    /// A SEPARATOR IS AN ORDINARY ITEM WHOSE LABEL IS THE MARKER, so it takes the same insert path and is
    /// subject to the same empty-label rejection - which it survives, the marker being non-empty. See
    /// <see cref="SeparatorText"/>.
    /// </remarks>
    public int AddSeparator()
    {
        // :L351  return of_InsertSeparator(UpperBound(Items) + 1,true)
        return InsertSeparator(unchecked((uint)(UpperBound() + 1)), true);
    }

    /// <summary>
    /// Inserts an item - the port of the seven-argument <c>of_InsertMenu</c>, THE ONE MEMBER EVERY OTHER
    /// ADD AND INSERT OVERLOAD FUNNELS THROUGH (<c>:L477-L500</c>).
    /// </summary>
    /// <param name="idInsert">
    /// The identifier to insert BEFORE, or the ONE-BASED position when <paramref name="byPosition"/>.
    /// </param>
    /// <param name="byPosition">Whether <paramref name="idInsert"/> is a position.</param>
    /// <param name="text">The label.</param>
    /// <param name="image">The image token.</param>
    /// <param name="tipText">The tooltip text.</param>
    /// <param name="enabled">Whether the item is selectable.</param>
    /// <param name="id">The identifier.</param>
    /// <returns>
    /// The one-based position on success; <c>0</c> for an EMPTY LABEL [<c>:L480</c>];
    /// <c>RetCode.E_OUT_OF_RANGE</c> for a position outside <c>1</c>..<see cref="UpperBound"/> plus one
    /// [<c>:L486</c>].
    /// </returns>
    /// <remarks>
    /// <para>
    /// *** THE EMPTY-LABEL REJECTION ANSWERS ZERO, NOT AN ARGUMENT ERROR - DEFECT 9's SECOND HALF. ***
    /// <c>:L480</c> is <c>if text = "" then return 0</c>, and zero is ALSO a legitimate-looking position
    /// in a one-based world, so a caller who tests <c>&lt;= 0</c> conflates it with the range failure
    /// while a caller who tests <c>= 0</c> distinguishes them. Both codes are preserved.
    /// </para>
    /// <para>
    /// THE NEW ITEM CARRIES NO SUBMENU, NO SPLIT FLAG AND NO OWNERSHIP. <c>:L488-L492</c> assigns exactly
    /// five of the eight fields, leaving the submenu null, the split flag false and the ownership flag
    /// false - which is what distinguishes a plain item from one added through the deferred submenu path
    /// [<c>:L654-L660</c>], where all eight are assigned.
    /// </para>
    /// </remarks>
    public int InsertMenu(
        in uint idInsert,
        in bool byPosition,
        in string text,
        in string image,
        in string tipText,
        in bool enabled,
        in uint id)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(tipText);

        // :L480  if text = "" then return 0
        if (text.Length == 0)
        {
            return 0;
        }

        // :L481-L485  the position-or-identifier resolution.
        int index = ResolveIndex(idInsert, byPosition);

        // :L486  if nIndex < 1 or nIndex > UpperBound(Items) + 1 then return RetCode.E_OUT_OF_RANGE
        //
        // The int cast is safe and is checked, not assumed: E_OUT_OF_RANGE is -13, which is inside int,
        // and the oracle's own return type for this member is `integer`.
        if (index < 1 || index > UpperBound() + 1)
        {
            return (int)RetCode.E_OUT_OF_RANGE;
        }

        // :L488-L492  five of the eight fields.
        MenuItemData newItem = new()
        {
            Enabled = enabled,
            Text = text,
            Image = image,
            TipText = tipText,
            Id = id,
        };

        // :L494-L497  the shift-up and the fill.
        InsertItemAt(index, newItem);

        // :L499
        return index;
    }

    /// <summary>
    /// Inserts an item with no tooltip - the port of the six-argument <c>of_InsertMenu</c> that takes the
    /// enabled flag (<c>:L502</c>).
    /// </summary>
    /// <param name="idInsert">The identifier to insert before, or the one-based position.</param>
    /// <param name="byPosition">Whether <paramref name="idInsert"/> is a position.</param>
    /// <param name="text">The label.</param>
    /// <param name="image">The image token.</param>
    /// <param name="enabled">Whether the item is selectable.</param>
    /// <param name="id">The identifier.</param>
    /// <returns>The one-based position, or a failure value.</returns>
    public int InsertMenu(
        in uint idInsert,
        in bool byPosition,
        in string text,
        in string image,
        in bool enabled,
        in uint id)
    {
        // :L502  return of_InsertMenu(idInsert,byPosition,text,image,"",enabled,id)
        return InsertMenu(idInsert, byPosition, text, image, string.Empty, enabled, id);
    }

    /// <summary>
    /// Inserts an enabled item with a tooltip - the port of the six-argument <c>of_InsertMenu</c> that
    /// takes the tooltip (<c>:L505</c>).
    /// </summary>
    /// <param name="idInsert">The identifier to insert before, or the one-based position.</param>
    /// <param name="byPosition">Whether <paramref name="idInsert"/> is a position.</param>
    /// <param name="text">The label.</param>
    /// <param name="image">The image token.</param>
    /// <param name="tipText">The tooltip text.</param>
    /// <param name="id">The identifier.</param>
    /// <returns>The one-based position, or a failure value.</returns>
    public int InsertMenu(
        in uint idInsert,
        in bool byPosition,
        in string text,
        in string image,
        in string tipText,
        in uint id)
    {
        // :L505  return of_InsertMenu(idInsert,byPosition,text,image,tipText,true,id)
        return InsertMenu(idInsert, byPosition, text, image, tipText, true, id);
    }

    /// <summary>
    /// Inserts an enabled item with no tooltip - the port of the five-argument <c>of_InsertMenu</c>
    /// (<c>:L508</c>).
    /// </summary>
    /// <param name="idInsert">The identifier to insert before, or the one-based position.</param>
    /// <param name="byPosition">Whether <paramref name="idInsert"/> is a position.</param>
    /// <param name="text">The label.</param>
    /// <param name="image">The image token.</param>
    /// <param name="id">The identifier.</param>
    /// <returns>The one-based position, or a failure value.</returns>
    public int InsertMenu(in uint idInsert, in bool byPosition, in string text, in string image, in uint id)
    {
        // :L508  return of_InsertMenu(idInsert,byPosition,text,image,"",true,id)
        return InsertMenu(idInsert, byPosition, text, image, string.Empty, true, id);
    }

    /// <summary>
    /// Inserts a separator - the port of <c>of_InsertSeparator</c> (<c>:L511</c>).
    /// </summary>
    /// <param name="idInsert">The identifier to insert before, or the one-based position.</param>
    /// <param name="byPosition">Whether <paramref name="idInsert"/> is a position.</param>
    /// <returns>The one-based position, or a failure value.</returns>
    /// <remarks>
    /// IDENTIFIER ZERO, IMAGE EMPTY, ENABLED TRUE - the oracle's own argument list is
    /// <c>(idInsert,byPosition,"-","",true,0)</c>, so a separator is ENABLED, which is meaningless for a
    /// separator and is preserved anyway.
    /// </remarks>
    public int InsertSeparator(in uint idInsert, in bool byPosition)
    {
        // :L511  return of_InsertMenu(idInsert,byPosition,"-","",true,0)
        return InsertMenu(idInsert, byPosition, SeparatorText, string.Empty, true, 0u);
    }

    /// <summary>
    /// Finds an item's ONE-BASED index by its label - the port of <c>of_GetIndex(text)</c>
    /// (<c>:L357-L364</c>).
    /// </summary>
    /// <param name="text">The label to find.</param>
    /// <returns>The one-based index, or <c>0</c> when no item carries that label.</returns>
    /// <remarks>
    /// *** DEFECT 6: THE SCAN RUNS BACKWARDS, SO IT ANSWERS THE LAST MATCH AND NOT THE FIRST. ***
    /// <c>:L359</c> is <c>for nIndex = UpperBound(Items) to 1 step -1</c>. With a store that holds two
    /// separators - which is the NORMAL case for the default set, and three separators when the check
    /// block is skipped - a search for the separator marker answers the LAST one. The direction is
    /// preserved: "correcting" it to a forward scan would silently change which item every by-identifier
    /// mutator addresses, and a row-count assertion would not notice.
    /// </remarks>
    public int GetIndex(in string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        // :L359-L361  backwards, deliberately.
        for (int index = UpperBound(); index >= 1; index--)
        {
            if (string.Equals(ItemAt(index).Text, text, StringComparison.Ordinal))
            {
                return index;
            }
        }

        // :L363
        return 0;
    }

    /// <summary>
    /// Finds an item's ONE-BASED index by its identifier - the port of <c>of_GetIndex(id)</c>
    /// (<c>:L373-L380</c>).
    /// </summary>
    /// <param name="id">The identifier to find.</param>
    /// <returns>The one-based index, or <c>0</c> when no item carries that identifier.</returns>
    /// <remarks>
    /// *** DEFECT 6 AGAIN: BACKWARDS, SO THE LAST MATCH WINS *** [<c>:L375</c>]. This is the resolution
    /// every by-identifier mutator and reader performs [<c>:L408</c>, <c>:L422</c>, <c>:L436</c>,
    /// <c>:L468</c>, <c>:L484</c>, <c>:L559</c>, <c>:L571</c>, <c>:L583</c>, <c>:L595</c>, <c>:L641</c>,
    /// <c>:L754</c>], so the direction is load-bearing for all eleven of them: every separator carries
    /// identifier ZERO, so a mutator addressed by identifier <c>0</c> reaches the LAST separator.
    /// </remarks>
    public int GetIndex(in uint id)
    {
        // :L375-L377  backwards, deliberately.
        for (int index = UpperBound(); index >= 1; index--)
        {
            if (ItemAt(index).Id == id)
            {
                return index;
            }
        }

        // :L379
        return 0;
    }

    /// <summary>
    /// Reads an item's identifier by its ONE-BASED index - the port of <c>of_GetId(index)</c>
    /// (<c>:L366-L371</c>).
    /// </summary>
    /// <param name="index">The one-based index.</param>
    /// <returns>
    /// ALWAYS <c>0</c>. See the remarks - this is a preserved defect, not an implementation shortcut.
    /// </returns>
    /// <remarks>
    /// <para>
    /// *** DEFECT 5: THE ORACLE READS AN UNINITIALISED LOCAL INSTEAD OF ITS OWN ARGUMENT, SO THIS MEMBER
    /// CANNOT ANSWER A REAL IDENTIFIER. *** The whole body is three statements
    /// [<c>:L366-L370</c>]: declare a local <c>nIndex</c>, validate the ARGUMENT <c>index</c> against the
    /// store bounds, then <c>return Items[nIndex].id</c> - reading the LOCAL, which PowerScript
    /// initialises to <c>0</c>, and never assigning it from the argument. Position <c>0</c> does not exist
    /// in a one-based array.
    /// </para>
    /// <para>
    /// THE PORT ANSWERS THE MEMBER'S OWN DECLARED FAILURE VALUE, <c>0</c>, WHICH IS THE NARROWEST HONEST
    /// REPRODUCTION. The oracle's read of a non-existent element cannot be reproduced literally - .NET
    /// would raise where PowerScript's behaviour for an out-of-range structure-array read is not
    /// determinable from this repository - so AAP 0.1.5's rule applies: where a legacy behaviour cannot be
    /// reproduced exactly, the contract is NARROWED WITH A DEFINED VALUE and never widened with a guess.
    /// The defined value is the one the member already answers for a rejected argument [<c>:L368</c>], so
    /// no new outcome is introduced. What is NOT done, and must never be done, is the obvious "fix" of
    /// returning the identifier at <paramref name="index"/>: that would give this member a behaviour the
    /// oracle does not have, and a test pins the current one.
    /// </para>
    /// <para>
    /// THE GUARD IS STILL EVALUATED, because it is observable through nothing here but is part of the
    /// member's shape, and because a future characterization run may show PowerScript raising on the read -
    /// in which case only the line after the guard changes.
    /// </para>
    /// </remarks>
    public uint GetId(in int index)
    {
        // :L368  if index < 1 or index > UpperBound(Items) then return 0
        if (index < 1 || index > UpperBound())
        {
            return 0u;
        }

        // :L370  return Items[nIndex].id - `nIndex` is the UNINITIALISED LOCAL, not `index`. Position 0
        // is outside a one-based array, so no identifier can be produced. See the remarks; do not
        // "repair" this to ItemAt(index).Id.
        return 0u;
    }

    /// <summary>
    /// Reads an item's identifier by its label - the port of <c>of_GetId(text)</c> (<c>:L1421-L1428</c>).
    /// </summary>
    /// <param name="text">The label to find.</param>
    /// <returns>The identifier, or <c>0</c> when no item carries that label.</returns>
    /// <remarks>
    /// *** DEFECT 6 A THIRD TIME: BACKWARDS, SO THE LAST MATCH WINS *** [<c>:L1423</c>]. This overload,
    /// unlike <c>GetId(int)</c>, is CORRECT - it returns the identifier of the item it found. The
    /// two share a name and only one of them works, which is exactly the kind of thing a characterization
    /// suite exists to record.
    /// <para>
    /// AMBIGUOUS ON FAILURE, AND THE ORACLE IS TOO: <c>0</c> is both "not found" [<c>:L1427</c>] and the
    /// legitimate identifier of every separator [<c>:L511</c>], so a search for the separator marker
    /// answers <c>0</c> whether it found one or not.
    /// </para>
    /// </remarks>
    public uint GetId(in string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        // :L1423-L1425  backwards, deliberately.
        for (int index = UpperBound(); index >= 1; index--)
        {
            MenuItemData item = ItemAt(index);
            if (string.Equals(item.Text, text, StringComparison.Ordinal))
            {
                return item.Id;
            }
        }

        // :L1427
        return 0u;
    }

    /// <summary>
    /// The number of items in the store - the port of <c>of_GetCount</c> (<c>:L460</c>).
    /// </summary>
    /// <returns>
    /// The count, which for a one-based store is also the LAST VALID POSITION - so it is the largest legal
    /// argument to every <c>byPosition</c> member.
    /// </returns>
    /// <remarks>
    /// COUNTS SEPARATORS, because they are ordinary items. The number a caller sees is therefore NOT the
    /// number of selectable entries, and it is not the number the emitted menu will contain either - the
    /// emit filter suppresses and collapses.
    /// </remarks>
    public int GetCount()
    {
        // :L460  return UpperBound(Items)
        return UpperBound();
    }

    /// <summary>
    /// Removes items by identifier or by position - the port of <c>of_Remove</c> (<c>:L382-L401</c>).
    /// </summary>
    /// <param name="id">The identifier, or the ONE-BASED position when <paramref name="byPosition"/>.</param>
    /// <param name="byPosition">Whether <paramref name="id"/> is a position.</param>
    /// <returns><c>RetCode.OK</c>, unconditionally - including when nothing matched.</returns>
    /// <remarks>
    /// <para>
    /// *** IT REMOVES EVERY MATCH, NOT THE FIRST, AND THAT IS OBSERVABLE. *** The oracle rebuilds the
    /// whole array and <c>continue</c>s past EVERY item that matches [<c>:L386-L396</c>], so removing by
    /// an identifier that two items share removes both - and removing by identifier <c>0</c> removes EVERY
    /// SEPARATOR at once, since every separator carries zero. Only the <c>byPosition</c> form can match
    /// once, because only one item occupies a position.
    /// </para>
    /// <para>
    /// IT ALWAYS ANSWERS SUCCESS [<c>:L400</c>], so a caller cannot tell a removal from a no-op. That is
    /// the oracle's and is preserved; <see cref="GetCount"/> before and after is the only way to tell.
    /// </para>
    /// <para>
    /// THE OWNED-CHILD DESTRUCTION AT <c>:L389-L391</c> HAS NO ANALOGUE AND NEEDS NONE. The oracle
    /// releases the child menu OBJECT when the removed item owned it; here the children are DATA held by
    /// value and are removed with their parent, so there is nothing to release. See
    /// <see cref="MenuItemData.MenuOwner"/> for why the flag is still carried.
    /// </para>
    /// <para>
    /// ITERATES FORWARD AND REBUILDS, WHICH IS WHY REMOVING SEVERAL AT ONCE IS SAFE HERE. Removing in
    /// place while walking forward would skip an item after each removal; the oracle's rebuild does not,
    /// and this port keeps the rebuild rather than converting it into an in-place removal loop.
    /// </para>
    /// </remarks>
    public long Remove(in uint id, in bool byPosition)
    {
        List<MenuItemData> survivors = [];

        // :L385-L396  the rebuild.
        int count = UpperBound();
        for (int index = 1; index <= count; index++)
        {
            MenuItemData item = ItemAt(index);

            // :L387-L388  the two match forms. The position comparison is between the ONE-BASED index
            // and the argument, which is why a caller must pass 1 for the first item.
            bool matched = byPosition
                ? index == ResolveIndex(id, true)
                : item.Id == id;

            if (matched)
            {
                // :L389-L391  the owned-child destruction, which has no analogue - see the remarks.
                // :L392  continue - the item is dropped by NOT being appended.
                continue;
            }

            // :L394  newItems[UpperBound(newItems) + 1] = Items[nIndex]
            survivors.Add(item);
        }

        // :L398  Items = newItems
        _items.Clear();
        _items.AddRange(survivors);

        // :L400
        return RetCode.OK;
    }

    /// <summary>
    /// Empties the store - the port of <c>of_RemoveAll</c> (<c>:L445-L458</c>).
    /// </summary>
    /// <returns><c>RetCode.OK</c>, unconditionally.</returns>
    /// <remarks>
    /// <para>
    /// THE CLEANUP'S THIRD ACT (DECISION 6). It is called from the finally at <c>:L200</c> on every
    /// invocation, which is what makes the store per-invocation, and it is public because the oracle
    /// publishes it.
    /// </para>
    /// <para>
    /// THE ORACLE WALKS THE STORE BEFORE EMPTYING IT, purely to destroy the child menus it owns
    /// [<c>:L448-L453</c>]. That walk has no analogue here for the reason given on <see cref="Remove"/>,
    /// so the port empties the store directly - and the walk's ABSENCE is the only difference, the
    /// resulting state being identical.
    /// </para>
    /// </remarks>
    public long RemoveAll()
    {
        // :L448-L453  the owned-child walk, which has no analogue - see the remarks.
        // :L455  Items = emptyItems
        _items.Clear();

        // :L457
        return RetCode.OK;
    }


    /// <summary>
    /// Replaces one item's label - the port of <c>of_SetText</c> (<c>:L417-L429</c>).
    /// </summary>
    /// <param name="id">The identifier, or the ONE-BASED position when <paramref name="byPosition"/>.</param>
    /// <param name="byPosition">Whether <paramref name="id"/> is a position.</param>
    /// <param name="text">The new label.</param>
    /// <returns><c>RetCode.OK</c>, or <c>RetCode.E_OUT_OF_BOUND</c> when nothing was addressed.</returns>
    /// <remarks>
    /// NO EMPTY-LABEL REJECTION HERE, unlike the insert path [<c>:L480</c>] - so an item CAN be emptied
    /// after the fact, and an item whose label is set to the separator marker BECOMES a separator as far as
    /// the emit filter is concerned. Both are the oracle's and neither is guarded against.
    /// </remarks>
    public long SetText(in uint id, in bool byPosition, in string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        // :L419-L423
        int index = ResolveIndex(id, byPosition);

        // :L424
        if (index < 1 || index > UpperBound())
        {
            return RetCode.E_OUT_OF_BOUND;
        }

        // :L426  Items[nIndex].text = text
        SetItemAt(index, ItemAt(index) with { Text = text });

        // :L428
        return RetCode.OK;
    }

    /// <summary>
    /// Replaces one item's image token - the port of <c>of_SetImage</c> (<c>:L403-L415</c>).
    /// </summary>
    /// <param name="id">The identifier, or the ONE-BASED position when <paramref name="byPosition"/>.</param>
    /// <param name="byPosition">Whether <paramref name="id"/> is a position.</param>
    /// <param name="image">The new image token.</param>
    /// <returns><c>RetCode.OK</c>, or <c>RetCode.E_OUT_OF_BOUND</c> when nothing was addressed.</returns>
    public long SetImage(in uint id, in bool byPosition, in string image)
    {
        ArgumentNullException.ThrowIfNull(image);

        // :L405-L409
        int index = ResolveIndex(id, byPosition);

        // :L410
        if (index < 1 || index > UpperBound())
        {
            return RetCode.E_OUT_OF_BOUND;
        }

        // :L412  Items[nIndex].image = image
        SetItemAt(index, ItemAt(index) with { Image = image });

        // :L414
        return RetCode.OK;
    }

    /// <summary>
    /// Replaces one item's tooltip text - the port of <c>of_SetTipText</c> (<c>:L431-L443</c>).
    /// </summary>
    /// <param name="id">The identifier, or the ONE-BASED position when <paramref name="byPosition"/>.</param>
    /// <param name="byPosition">Whether <paramref name="id"/> is a position.</param>
    /// <param name="tipText">The new tooltip text.</param>
    /// <returns><c>RetCode.OK</c>, or <c>RetCode.E_OUT_OF_BOUND</c> when nothing was addressed.</returns>
    public long SetTipText(in uint id, in bool byPosition, in string tipText)
    {
        ArgumentNullException.ThrowIfNull(tipText);

        // :L433-L437
        int index = ResolveIndex(id, byPosition);

        // :L438
        if (index < 1 || index > UpperBound())
        {
            return RetCode.E_OUT_OF_BOUND;
        }

        // :L440  Items[nIndex].tipText = tipText
        SetItemAt(index, ItemAt(index) with { TipText = tipText });

        // :L442
        return RetCode.OK;
    }

    /// <summary>
    /// Enables or disables one item - the port of <c>of_Enable</c> (<c>:L463-L475</c>).
    /// </summary>
    /// <param name="id">The identifier, or the ONE-BASED position when <paramref name="byPosition"/>.</param>
    /// <param name="byPosition">Whether <paramref name="id"/> is a position.</param>
    /// <param name="enable">The new enabled state.</param>
    /// <returns><c>RetCode.OK</c>, or <c>RetCode.E_OUT_OF_BOUND</c> when nothing was addressed.</returns>
    /// <remarks>
    /// NAMED <c>Enable</c> AND NOT <c>SetEnabled</c>, because <see cref="DataWindowServiceBase.SetEnabled"/>
    /// already means something entirely different on the base - it enables the SERVICE, with a veto that
    /// answers <c>RetCode.FAILED</c> rather than <c>PREVENT</c>. The oracle's own name is <c>of_Enable</c>,
    /// so the shorter name is also the faithful one.
    /// </remarks>
    public long Enable(in uint id, in bool byPosition, in bool enable)
    {
        // :L465-L469
        int index = ResolveIndex(id, byPosition);

        // :L470
        if (index < 1 || index > UpperBound())
        {
            return RetCode.E_OUT_OF_BOUND;
        }

        // :L472  Items[nIndex].enabled = enable
        SetItemAt(index, ItemAt(index) with { Enabled = enable });

        // :L474
        return RetCode.OK;
    }

    /// <summary>
    /// Reads one item's label - the port of <c>of_GetText</c> (<c>:L554-L564</c>).
    /// </summary>
    /// <param name="id">The identifier, or the ONE-BASED position when <paramref name="byPosition"/>.</param>
    /// <param name="byPosition">Whether <paramref name="id"/> is a position.</param>
    /// <returns>The label, or the EMPTY STRING when nothing was addressed [<c>:L561</c>].</returns>
    /// <remarks>
    /// THE READERS ANSWER A VALUE AND NOT A CODE, so a miss is indistinguishable from an item whose label
    /// is genuinely empty - which <see cref="SetText"/> permits. The oracle has the same ambiguity and it is
    /// not resolved here.
    /// </remarks>
    public string GetText(in uint id, in bool byPosition)
    {
        // :L556-L560
        int index = ResolveIndex(id, byPosition);

        // :L561
        if (index < 1 || index > UpperBound())
        {
            return string.Empty;
        }

        // :L563
        return ItemAt(index).Text;
    }

    /// <summary>
    /// Reads one item's tooltip text - the port of <c>of_GetTipText</c> (<c>:L566-L576</c>).
    /// </summary>
    /// <param name="id">The identifier, or the ONE-BASED position when <paramref name="byPosition"/>.</param>
    /// <param name="byPosition">Whether <paramref name="id"/> is a position.</param>
    /// <returns>The tooltip text, or the EMPTY STRING when nothing was addressed [<c>:L573</c>].</returns>
    public string GetTipText(in uint id, in bool byPosition)
    {
        // :L568-L572
        int index = ResolveIndex(id, byPosition);

        // :L573
        if (index < 1 || index > UpperBound())
        {
            return string.Empty;
        }

        // :L575
        return ItemAt(index).TipText;
    }

    /// <summary>
    /// Reads one item's image token - the port of <c>of_GetImage</c> (<c>:L578-L588</c>).
    /// </summary>
    /// <param name="id">The identifier, or the ONE-BASED position when <paramref name="byPosition"/>.</param>
    /// <param name="byPosition">Whether <paramref name="id"/> is a position.</param>
    /// <returns>The image token, or the EMPTY STRING when nothing was addressed [<c>:L585</c>].</returns>
    public string GetImage(in uint id, in bool byPosition)
    {
        // :L580-L584
        int index = ResolveIndex(id, byPosition);

        // :L585
        if (index < 1 || index > UpperBound())
        {
            return string.Empty;
        }

        // :L587
        return ItemAt(index).Image;
    }

    /// <summary>
    /// Reads whether one item is enabled - the port of <c>of_IsEnabled</c> (<c>:L590-L600</c>).
    /// </summary>
    /// <param name="id">The identifier, or the ONE-BASED position when <paramref name="byPosition"/>.</param>
    /// <param name="byPosition">Whether <paramref name="id"/> is a position.</param>
    /// <returns>
    /// The enabled state, or <see langword="false"/> when nothing was addressed [<c>:L597</c>] - which is
    /// indistinguishable from an item that is genuinely disabled.
    /// </returns>
    public bool IsEnabled(in uint id, in bool byPosition)
    {
        // :L592-L596
        int index = ResolveIndex(id, byPosition);

        // :L597
        if (index < 1 || index > UpperBound())
        {
            return false;
        }

        // :L599
        return ItemAt(index).Enabled;
    }

    /// <summary>
    /// Attaches a child item beneath an existing item, creating the child menu if there is none - the
    /// HEADLESS RESIDUE of the deferred submenu surface, and the only part of it that ships (DECISION 1,
    /// DECISION 2).
    /// </summary>
    /// <param name="parentId">
    /// The parent item's identifier. Resolved through <see cref="GetIndex(in uint)"/>, so the LAST item
    /// carrying it wins - see DEFECT 6.
    /// </param>
    /// <param name="text">The child's label.</param>
    /// <param name="image">The child's image token.</param>
    /// <param name="tipText">The child's tooltip text.</param>
    /// <param name="id">The child's identifier.</param>
    /// <returns>
    /// The one-based position of the child WITHIN ITS PARENT on success, or
    /// <c>RetCode.E_OUT_OF_BOUND</c> when no item carries <paramref name="parentId"/>, or <c>0</c> for an
    /// empty label.
    /// </returns>
    /// <remarks>
    /// <para>
    /// INTERNAL, NOT PUBLIC, AND THAT IS THE WHOLE POINT. The oracle publishes SIXTEEN add-and-insert
    /// submenu overloads plus a getter and two creation helpers - nineteen members, inventoried in
    /// DECISION 2 - and every one of them is typed on the popup-menu class that constraint C-D excludes.
    /// None of the nineteen is ported. What the DEFAULT ITEM SET actually needs is one operation: attach a
    /// child under the auto-width parent [<c>:L272-L274</c>]. This is that operation, and it is
    /// <see langword="internal"/> so it cannot become a back door to the deferred surface.
    /// </para>
    /// <para>
    /// IT REPRODUCES THE ORACLE'S TWO-STEP SEQUENCE FAITHFULLY, WHICH IS WHY THE VALIDITY FLAG IS
    /// SEPARATE. <c>:L272</c> adds the parent WITH a newly created and therefore EMPTY child menu -
    /// setting the ownership flag [<c>:L647</c>] and the split flag [<c>:L660</c>] as it goes - and only
    /// <c>:L273-L274</c> fetch that child and put an item in it. So the parent is created first by
    /// <see cref="AddSubmenuParent"/> and populated afterwards by this member, and between the two calls
    /// <see cref="MenuItemData.HasSubmenu"/> is <see langword="true"/> while
    /// <see cref="MenuItemData.Submenu"/> is empty - a state the oracle passes through and a derived
    /// "has children" test could not represent.
    /// </para>
    /// <para>
    /// THE CHILD IS APPENDED, matching the child menu's own add semantics [<c>:L274</c> calls the plain
    /// four-argument add on the child], and the child is created ENABLED with no submenu of its own.
    /// </para>
    /// </remarks>
    internal int AddSubmenuItem(
        uint parentId,
        string text,
        string image,
        string tipText,
        uint id)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(tipText);

        // The child menu's own of_AddMenu rejects an empty label exactly as this service's does [:L480].
        if (text.Length == 0)
        {
            return 0;
        }

        // :L273  of_GetSubMenu(MID_COLAUTOWIDTH,false,ref pmSub) - BY IDENTIFIER, not by position, which
        // is why the backwards resolution of DEFECT 6 applies here too.
        int parentIndex = GetIndex(parentId);
        if (parentIndex < 1 || parentIndex > UpperBound())
        {
            // :L756  the getter's own out-of-range answer, reproduced for the composite operation.
            return (int)RetCode.E_OUT_OF_BOUND;
        }

        MenuItemData parent = ItemAt(parentIndex);

        // :L274  pmSub.of_AddMenu(text,image,tipText,id) - appended to the child menu.
        List<MenuItemData> children = [.. parent.Submenu];
        children.Add(new MenuItemData
        {
            Enabled = true,
            Text = text,
            Image = image,
            TipText = tipText,
            Id = id,
        });

        SetItemAt(parentIndex, parent with { Submenu = children });

        return children.Count;
    }

    /// <summary>
    /// Appends an item that OWNS an initially empty child menu - the headless residue of the class-name
    /// add-submenu overload the default item set uses (<c>:L272</c>, reaching <c>:L634-L668</c> through
    /// <c>:L737</c>).
    /// </summary>
    /// <param name="text">The parent's label.</param>
    /// <param name="image">The parent's image token.</param>
    /// <param name="tipText">The parent's tooltip text.</param>
    /// <param name="split">Whether the parent is a split item.</param>
    /// <param name="id">The parent's identifier, which is also its child menu's handle.</param>
    /// <returns>The one-based position it landed at, or a failure value.</returns>
    /// <remarks>
    /// <para>
    /// INTERNAL FOR THE SAME REASON AS <see cref="AddSubmenuItem"/>. The oracle's route to this state is
    /// the class-name overload at <c>:L737</c>, which CREATES the child menu from a class name
    /// [<c>:L705</c>], hands it to the by-reference insert [<c>:L707</c>], and destroys it again if the
    /// insert failed [<c>:L709-L711</c>]. All three steps operate on the excluded type; what survives is
    /// the item they produce.
    /// </para>
    /// <para>
    /// OWNERSHIP IS SET, BECAUSE THE ORACLE SETS IT ON EXACTLY THIS PATH. <c>:L645-L647</c> sets it when
    /// the service created the child itself, which is what the class-name overload always does - so the
    /// default set's auto-width parent is an OWNER, and that is the state
    /// <see cref="MenuItemData.MenuOwner"/> records.
    /// </para>
    /// <para>
    /// THE ARGUMENT ORDER HERE IS THE REGULAR ONE - split before the identifier - and it is worth saying so
    /// because two of the sixteen deferred overloads are irregular: at <c>:L107</c> and <c>:L111</c> the
    /// enabled flag precedes the split flag while every sibling puts split first. Nothing here inherits
    /// that irregularity, and DECISION 2 records it so a future port of the sixteen does not smooth it
    /// over.
    /// </para>
    /// </remarks>
    internal int AddSubmenuParent(
        string text,
        string image,
        string tipText,
        bool split,
        uint id)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(tipText);

        // :L637  if text = "" then return 0 - the submenu insert path has the same rejection as the plain
        // one, and answers the same 0.
        if (text.Length == 0)
        {
            return 0;
        }

        // :L725/:L737  of_InsertSubMenu(UpperBound(Items) + 1,true,...) - append, as every add does.
        int index = UpperBound() + 1;

        // :L654-L660  ALL EIGHT fields are assigned on this path, unlike the five of the plain insert.
        MenuItemData newItem = new()
        {
            // :L654  the by-reference overloads take an enabled flag; the default set reaches this through
            // :L272, whose overload [:L110-L113] defaults it to true [:L746].
            Enabled = true,
            Text = text,
            Image = image,
            TipText = tipText,
            Id = id,

            // :L659  newItem.subMenu = PopupMenu - decomposed per DECISION 1. The child exists and is
            // EMPTY; the handle is the parent's own identifier, which is how :L273 addresses it.
            HasSubmenu = true,
            SubmenuHandle = id,
            Submenu = [],

            // :L660
            Split = split,

            // :L647  the service created the child, so it owns it.
            MenuOwner = true,
        };

        // :L662-L665
        InsertItemAt(index, newItem);

        // :L667
        return index;
    }


    // ==========================================================================================
    //  THE FIVE EVENTS                                        n_cst_dwsvc_contextmenu.sru:L24-L28
    //  ----------------------------------------------------------------------------------------
    //  TWO ARE BROKER-WIRED AND THREE ARE NOT, which is the file's most load-bearing structural fact
    //  after the split itself. `onenable` subscribes onRButtonDown and onRButtonUp [:L1441-L1442] and
    //  DELIBERATELY DOES NOT subscribe the other three: onrbuttonclicked is reached only by
    //  onrbuttonup raising it [:L125], ondefproc only by onrbuttonclicked dispatching it [:L196], and
    //  onprepare only by onrbuttonclicked calling it [:L146]. A reader searching for five
    //  subscriptions will correctly find two.
    //
    //  ALL FIVE ANSWER `long` OR NOTHING, AND NONE OF THEM EVER PREVENTS. The two raw handlers answer
    //  0 unconditionally [:L129, :L133]; onprepare answers 0 on all four exits; onrbuttonclicked and
    //  ondefproc are void. The oracle has no `return 1` anywhere in this file - so this service never
    //  vetoes anything, it only OBSERVES vetoes from its host and from its own preparation.
    // ==========================================================================================

    /// <summary>
    /// The right-button press handler - the port of <c>event onrbuttondown pbm_dwnrbuttondown</c>
    /// (<c>:L25</c>, body <c>:L132-L134</c>).
    /// </summary>
    /// <param name="row">The one-based row under the pointer, or <c>0</c> when none.</param>
    /// <returns><c>0</c>, always - the oracle's only exit [<c>:L133</c>].</returns>
    /// <remarks>
    /// <para>
    /// THE WHOLE BODY IS ONE ASSIGNMENT, AND IT IS COMPLETE RATHER THAN STUBBED. <c>:L132</c> records the
    /// row for <see cref="OnRButtonUp"/> to match against; there is nothing else to do on a press.
    /// </para>
    /// <para>
    /// THE POINTER POSITION IS NOT RECORDED, and its absence is the oracle's rather than a deferral. The
    /// legacy event receives the position as an event argument and IGNORES it, unlike the column-sort
    /// service's press handler which captures it for a proximity test. So no coordinate is dropped here -
    /// there was never one to drop.
    /// </para>
    /// </remarks>
    public long OnRButtonDown(long row)
    {
        // :L132  _nRowRButtonDown = row
        _nRowRButtonDown = row;

        // :L133
        return 0L;
    }

    /// <summary>
    /// The right-button release handler, and the gate on the whole menu cycle - the port of
    /// <c>event onrbuttonup pbm_dwnrbuttonup</c> (<c>:L24</c>, body <c>:L124-L130</c>).
    /// </summary>
    /// <param name="row">The one-based row under the pointer, or <c>0</c> when none.</param>
    /// <param name="dwo">The object under the pointer.</param>
    /// <param name="pointer">
    /// The pointer-derived and clipboard-derived facts this service cannot read for itself - see
    /// DECISION 4.
    /// </param>
    /// <returns><c>0</c>, always - the oracle's only exit [<c>:L129</c>].</returns>
    /// <remarks>
    /// <para>
    /// A PRESS-AND-RELEASE ROW MATCH, AND IT IS THE ONLY GATE. <c>:L124</c> raises the click event only
    /// when the release row equals the press row, so a press on one row and a release on another produces
    /// NO MENU AT ALL - and neither does a release with no press before it, the press row being <c>0</c>
    /// and a release over a row being non-zero. A release over NO row after a press over no row DOES match,
    /// both being <c>0</c>, which is how a right-click on the DataWindow background reaches the menu.
    /// </para>
    /// <para>
    /// THE PRESS ROW IS CLEARED UNCONDITIONALLY AND OUTSIDE THE MATCH [<c>:L127</c>], so it is cleared
    /// even when the rows did not match. That makes the press single-use: a second release without a
    /// second press cannot match. The clearing happens AFTER the click event has been raised, which matters
    /// because the click event's own cleanup does not touch this field.
    /// </para>
    /// <para>
    /// THE LAYOUT IS RETURNED RATHER THAN DISCARDED, which is the one place this member's shape differs
    /// from the oracle's. The legacy raises a VOID event whose side effect is a modal popup; here the
    /// caller needs the menu, so the layout the click produced is available on
    /// <see cref="LastLayout"/> - the return value stays the oracle's <c>0</c> so the broker's prevent
    /// convention is unchanged.
    /// </para>
    /// </remarks>
    public long OnRButtonUp(long row, IDataWindowObject dwo, ContextMenuPointerContext pointer)
    {
        ArgumentNullException.ThrowIfNull(dwo);
        ArgumentNullException.ThrowIfNull(pointer);

        // :L124-L126  the row match, and the click event it gates.
        if (row == _nRowRButtonDown)
        {
            // :L125  Event OnRButtonClicked(xpos,ypos,row,dwo) - the two coordinates are DEFERRED with
            // the popup they position (DECISION 4); the pointer context carries what survives.
            LastLayout = BuildMenu(row, dwo, pointer);
        }

        // :L127  _nRowRButtonDown = 0 - unconditional, and after the raise.
        _nRowRButtonDown = 0L;

        // :L129
        return 0L;
    }

    /// <summary>
    /// The layout the most recent <see cref="OnRButtonUp"/> produced, or <see langword="null"/> when the
    /// press and release rows did not match.
    /// </summary>
    /// <remarks>
    /// EXISTS ONLY BECAUSE THE LEGACY EVENT IS VOID. It is the same value <see cref="BuildMenu"/> returns
    /// to a caller that drives the two operations directly, published here so that driving the service
    /// through its broker-wired handlers - which is how <c>se_cst_dw</c> drives it [<c>:L1441-L1442</c>] -
    /// does not lose it.
    /// </remarks>
    public ContextMenuLayout? LastLayout { get; private set; }

    /// <summary>
    /// Builds the menu: preparation, both vetoes, the toggle filter and the separator collapse - the
    /// FIRST of the two operations <c>event onrbuttonclicked</c> becomes (<c>:L136-L202</c>, DECISION 5).
    /// </summary>
    /// <param name="row">The one-based row the menu was requested over, or <c>0</c> when none.</param>
    /// <param name="dwo">The object the menu was requested over.</param>
    /// <param name="pointer">The pointer-derived and clipboard-derived facts - see DECISION 4.</param>
    /// <returns>
    /// The items to show and the reason, if any, that there are none. When
    /// <see cref="ContextMenuLayout.AwaitingSelection"/> is <see langword="true"/> the caller MUST follow
    /// with <see cref="ApplySelection"/> or <see cref="Discard"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// THE ORACLE'S ORDER IS PRESERVED EXACTLY, AND EVERY STEP IS OBSERVABLE: the popup guard
    /// [<c>:L143</c>], then preparation [<c>:L146</c>], then the host's veto [<c>:L147</c>], then the
    /// pre-filter emptiness test [<c>:L149-L150</c>], then the filter-and-collapse pass
    /// [<c>:L156-L185</c>]. Reordering any two changes what a host sees: preparation ADDS the items the
    /// emptiness test then counts, and the host's veto runs AFTER preparation has already populated the
    /// store - which is why a vetoing host still causes the cleanup to run.
    /// </para>
    /// <para>
    /// THE TRY/FINALLY SHAPE IS PRESERVED, AND THE ONE-SIDED HAND-OFF IS THE SPLIT (DECISION 6). The
    /// oracle's finally [<c>:L197-L201</c>] runs on EVERY path through the try, because the popup, the
    /// dispatch and the cleanup all happen inside it. Here the cleanup still runs on every early exit and
    /// on any exception, but when a menu is produced its ownership transfers to
    /// <see cref="ApplySelection"/> or <see cref="Discard"/> - because clearing the clicked column and the
    /// clipboard text before the selection arrives would leave six of the eight dispatch arms with nothing
    /// to operate on.
    /// </para>
    /// <para>
    /// *** DEFECT 2 AND DEFECT 3 BOTH LIVE IN THE FILTER PASS *** and are annotated at the exact lines
    /// that carry them. Two tests fail if either is "fixed".
    /// </para>
    /// </remarks>
    public ContextMenuLayout BuildMenu(long row, IDataWindowObject dwo, ContextMenuPointerContext pointer)
    {
        ArgumentNullException.ThrowIfNull(dwo);
        ArgumentNullException.ThrowIfNull(pointer);

        // :L143  if Not _of_IsAllowPopup(row,dwo) then return
        //
        // OUTSIDE THE TRY, so this is the one exit that performs no cleanup - see
        // ContextMenuOutcome.NotAllowed.
        if (!IsAllowPopup(row, dwo))
        {
            return new ContextMenuLayout
            {
                Outcome = ContextMenuOutcome.NotAllowed,
                Items = [],
            };
        }

        // The hand-off flag. False means "the cleanup is mine"; true means it now belongs to
        // ApplySelection or Discard.
        bool handOff = false;

        // :L145  try
        try
        {
            // :L146  if Event OnPrepare(row,dwo) = 1 then return - an EQUALITY test, so 2 does not veto.
            //
            // DORMANT IN THIS SEALED SHAPE, DELIBERATELY KEPT. OnPrepare answers 0 on every one of its
            // paths [:L226-L307], including DEFECT 4's two early exits, so this arm cannot fire. The
            // oracle still tests the value because a PowerBuilder event is overridable by any descendant
            // object, and this port is sealed - matching Services/ColumnSortModel.cs and
            // Services/RowSelectService.cs - so there is no descendant to veto from. The guard is
            // retained because removing it would narrow the oracle's contract (constraint C-B), and the
            // invariant that makes it dormant is pinned by
            // PreparationAnswersZeroOnEveryPathSoItsVetoIsDormant.
            if (OnPrepare(row, dwo, pointer) == 1L)
            {
                return new ContextMenuLayout
                {
                    Outcome = ContextMenuOutcome.PreparePrevented,
                    Items = [],
                };
            }

            // :L147  if #DataWindow.Event OnInitContextMenu(row,dwo) = 1 then return - the C-03 semantic
            // event, raised on the HOST. Also an equality test.
            if (RequireHost().OnInitContextMenu(row, dwo) == 1L)
            {
                return new ContextMenuLayout
                {
                    Outcome = ContextMenuOutcome.HostPrevented,
                    Items = [],
                };
            }

            // :L149-L150  nCount = UpperBound(Items) / if nCount = 0 then return
            //
            // COUNTED BEFORE THE FILTER, so a store that the filter will empty is NOT empty by this test.
            int count = UpperBound();
            if (count == 0)
            {
                return new ContextMenuLayout
                {
                    Outcome = ContextMenuOutcome.Empty,
                    Items = [],
                };
            }

            // :L152-L186  the popup menu's creation, its tooltip mode and its theme are DEFERRED
            // (constraint C-D); the SEQUENCE OF ADDS the oracle performs on it is captured as data by the
            // emit pass, which is factored out so the toggle filter and the separator collapse can be
            // exercised with no host at all (constraint C-H).
            IReadOnlyList<MenuItemData> emitted = EmitVisibleItems(count);

            // :L187-L191  the window rectangle, the coordinate conversions and the modal popup itself are
            // DEFERRED (constraint C-D). The identifier the popup would have returned arrives instead as
            // ApplySelection's argument, which is DECISION 5.

            // The store, the clicked column and the clipboard text must all survive until the selection is
            // applied, so the cleanup is not run here.
            handOff = true;

            return new ContextMenuLayout
            {
                Outcome = ContextMenuOutcome.Menu,
                Items = emitted,
            };
        }
        finally
        {
            // :L197-L201  finally / _sColClicked = "" / _sClipText = "" / of_RemoveAll()
            //
            // Runs on every early exit above and on any exception, and is skipped ONLY when a menu was
            // produced - see DECISION 6 and Discard.
            if (!handOff)
            {
                Cleanup();
            }
        }
    }

    /// <summary>
    /// Applies the capability toggles and the separator collapse to the store, answering the items the
    /// oracle would have added to the popup menu - the port of <c>:L152-L186</c>.
    /// </summary>
    /// <param name="count">
    /// The store's upper bound, TAKEN BEFORE THE PASS by the caller [<c>:L149</c>] and passed in rather
    /// than re-read, because the oracle's loop bound is that earlier count and nothing adds from inside
    /// the loop.
    /// </param>
    /// <returns>The emitted items, in order.</returns>
    /// <remarks>
    /// <para>
    /// FACTORED OUT OF <see cref="BuildMenu"/> FOR TESTABILITY, NOT FOR STRUCTURE. The oracle writes this
    /// pass inline inside <c>event onrbuttonclicked</c>, where reaching it requires a host, a row, a
    /// DataWindow object and two vetoes that all have to pass first. Constraint C-H requires the toggle
    /// filter and the separator collapse to be exercisable with no DataWindow and no UI, so the pass is a
    /// member of its own and is <see langword="internal"/> for the test assembly. Its behaviour is
    /// identical either way: it reads only the store and the five toggles.
    /// </para>
    /// <para>
    /// THE TWO DEFECTS THAT LIVE IN THIS PASS ARE ANNOTATED AT THEIR EXACT LINES BELOW - DEFECT 3 on the
    /// check filter and DEFECT 2 in the blank space where a fifth filter is NOT.
    /// </para>
    /// </remarks>
    internal IReadOnlyList<MenuItemData> EmitVisibleItems(int count)
    {
        // :L152-L154  the popup menu is created, given a tooltip mode and given this service's theme.
        // All three are DEFERRED (constraint C-D); what follows is the sequence of adds the oracle
        // performs on it, captured as data instead.
        List<MenuItemData> emitted = [];

        // :L138  boolean bLastIsSeparator,bHasMenu - both start false.
        bool lastIsSeparator = false;
        bool hasMenu = false;

        // :L156  for nIndex = 1 to nCount - ONE-BASED, and bounded by the count taken BEFORE the loop
        // so an add performed from inside it could not be reached (nothing adds from inside it).
        for (int index = 1; index <= count; index++)
        {
            MenuItemData item = ItemAt(index);

            // :L157-L159  the check capability suppresses TWO of the three items it adds.
            //
            // *** DEFECT 3 - PRESERVED VERBATIM. *** MID_COLREVERTCHECK is absent from this test even
            // though :L283 gates all three check items behind the same toggle at ADD time. So a menu
            // built with the toggle set and emitted with it cleared still offers "反向勾选列". Adding
            // the third identifier here would "fix" a defect the refactor is required to keep, and a
            // test asserts the current behaviour.
            if (!ColCheck && (item.Id == MID_COLCHECK || item.Id == MID_COLUNCHECK))
            {
                continue;
            }

            // :L160-L162
            if (!ColCopy && item.Id == MID_COLCOPY)
            {
                continue;
            }

            // :L163-L165
            if (!ColPaste && item.Id == MID_COLPASTE)
            {
                continue;
            }

            // :L166-L168
            if (!ItemCopy && item.Id == MID_ITEMCOPY)
            {
                continue;
            }

            // *** DEFECT 2 - PRESERVED BY OMISSION. ***
            // THERE IS NO ColAutoWidth TEST HERE, and there is none in the oracle either: the toggle
            // is consulted ONLY at add time [:L268]. Neither MID_COLAUTOWIDTH nor
            // MID_COLAUTOWIDTH_ALL can be suppressed at emit time, so clearing the toggle after
            // preparation has run has NO EFFECT on the emitted menu. This comment is the defect's
            // annotation and the blank space below it is the defect; a test asserts that clearing the
            // toggle changes nothing here.

            // :L169-L172  a separator is REMEMBERED and SKIPPED, never emitted directly.
            if (string.Equals(item.Text, SeparatorText, StringComparison.Ordinal))
            {
                lastIsSeparator = true;
                continue;
            }

            // :L173-L178  the pending separator is emitted before the next REAL item, and only if a
            // real item has already been emitted.
            //
            // THE THREE CONSEQUENCES, ALL REPRODUCED: a LEADING separator is suppressed because
            // bHasMenu is still false; CONSECUTIVE separators collapse to one because the flag is
            // merely set again; and a TRAILING separator is dropped entirely because the loop ends
            // with the flag set and nothing after it to trigger the emit.
            if (lastIsSeparator)
            {
                lastIsSeparator = false;
                if (hasMenu)
                {
                    // :L176  pmMenu.of_AddSeparator()
                    emitted.Add(new MenuItemData
                    {
                        Text = SeparatorText,
                        Enabled = true,
                        Id = 0u,
                    });
                }
            }

            // :L179-L183  the oracle chooses the submenu add when the item's child menu is valid and
            // the plain add otherwise. Here the item travels WITH its child collection and its
            // validity flag, so one add serves both arms and the distinction is preserved on the item
            // rather than in the call. The argument lists the two arms pass are otherwise identical
            // apart from the split flag, which only the submenu arm forwards [:L180].
            emitted.Add(item);

            // :L184
            hasMenu = true;
        }
        return emitted;
    }

    /// <summary>
    /// Applies the user's choice: the host's second veto and the default dispatch - the SECOND of the two
    /// operations <c>event onrbuttonclicked</c> becomes (<c>:L193-L201</c>, DECISION 5).
    /// </summary>
    /// <param name="row">The one-based row the menu was shown over.</param>
    /// <param name="dwo">The object the menu was shown over.</param>
    /// <param name="mid">
    /// The chosen item's identifier, or <c>0</c> for "nothing was chosen" - which is exactly what the
    /// legacy modal popup answers when it is dismissed [<c>:L193</c>].
    /// </param>
    /// <returns>
    /// The dispatched operation's own return code, or <c>RetCode.OK</c> when nothing was dispatched -
    /// which covers a dismissal, a host veto and an unrecognised identifier alike. The ORACLE DISCARDS
    /// this value; it is surfaced because a remote caller cannot see the side effect that replaced it.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <c>long</c> AND NOT <c>uint</c> FOR THE IDENTIFIER, because that is what the oracle declares:
    /// <c>event ondefproc ( long row, dwobject dwo, long mid )</c> [<c>:L27</c>] and the popup's own answer
    /// is a <c>long</c> [<c>:L136</c>]. The nine reserved identifiers are <c>unsignedlong</c> constants
    /// compared against it, and PowerScript widens silently where C# must be told to - so the comparison
    /// is written out rather than hidden behind a cast of the argument.
    /// </para>
    /// <para>
    /// THE CLEANUP RUNS ON EVERY PATH THROUGH THIS MEMBER, including the dismissal and the veto, because
    /// the oracle's finally covers all three [<c>:L193-L196</c> are all inside the try]. That is why it is
    /// safe for <see cref="BuildMenu"/> to hand ownership over: the only way to reach here without cleaning
    /// up is to throw before entering, which the argument guards make impossible.
    /// </para>
    /// <para>
    /// THE THREE PENDING RESULTS ARE CLEARED FIRST, so a caller cannot mistake a previous invocation's
    /// error, copied text or measurement plan for this one's.
    /// </para>
    /// </remarks>
    public long ApplySelection(long row, IDataWindowObject dwo, long mid)
    {
        ArgumentNullException.ThrowIfNull(dwo);

        PendingError = null;
        PendingCopiedText = null;
        PendingWidthPlan = null;

        try
        {
            // :L193  if rtCode = 0 then return - the popup was dismissed without a choice.
            if (mid == 0L)
            {
                return RetCode.OK;
            }

            // :L194  if #DataWindow.Event OnContextMenu(row,dwo,rtCode) = 1 then return - the C-03
            // semantic event, raised on the HOST, and an EQUALITY test again. This is the application's
            // chance to claim an identifier it added itself; see DataWindowServiceHost.OnContextMenu for
            // why an unclaimed application identifier is harmless.
            if (RequireHost().OnContextMenu(row, dwo, mid) == 1L)
            {
                return RetCode.OK;
            }

            // :L196  Event OnDefProc(row,dwo,rtCode)
            return OnDefProc(row, dwo, mid);
        }
        finally
        {
            // :L197-L201  the cleanup, now owned by this operation.
            Cleanup();
        }
    }

    /// <summary>
    /// Performs the per-invocation cleanup when no selection will arrive - the same three statements the
    /// oracle's finally performs (<c>:L198-L200</c>, DECISION 6).
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE ORACLE HAS NO EQUIVALENT MEMBER BECAUSE IT HAS NO EQUIVALENT SITUATION: a modal popup always
    /// returns, so the finally always runs. Splitting the event created a path the oracle does not have -
    /// a menu built and then abandoned - and this member closes it. It adds no behaviour: it runs exactly
    /// the three statements the finally runs, and running it twice is harmless.
    /// </para>
    /// <para>
    /// A CALLER THAT NEVER CALLS THIS OR <see cref="ApplySelection"/> LEAVES THE STORE POPULATED, which is
    /// the one way a ported call sequence can diverge from the oracle's.
    /// <see cref="ContextMenuLayout.AwaitingSelection"/> is how a caller knows it owes one of the two.
    /// </para>
    /// </remarks>
    public void Discard()
    {
        Cleanup();
    }

    /// <summary>
    /// The three statements of the oracle's finally block (<c>:L198-L200</c>).
    /// </summary>
    /// <remarks>
    /// IN THE ORACLE'S ORDER, though the order is not observable here - the two fields are independent and
    /// emptying the store touches neither. It is preserved anyway, because an order that is not observable
    /// today is still the order a recording of the ported call sequence will show.
    /// </remarks>
    private void Cleanup()
    {
        // :L198  _sColClicked = ""
        _sColClicked = string.Empty;

        // :L199  _sClipText = ""
        _sClipText = string.Empty;

        // :L200  of_RemoveAll()
        _ = RemoveAll();
    }

    /// <summary>
    /// Dispatches a chosen identifier to the operation it names - the port of
    /// <c>event ondefproc</c> (<c>:L27</c>, body <c>:L204-L222</c>).
    /// </summary>
    /// <param name="row">The one-based row the menu was shown over.</param>
    /// <param name="dwo">The object the menu was shown over.</param>
    /// <param name="mid">The chosen identifier.</param>
    /// <returns>
    /// The dispatched operation's return code, or <c>RetCode.OK</c> when no arm matched. THE ORACLE
    /// DISCARDS every one of these; see <see cref="ApplySelection"/> for why they are surfaced.
    /// </returns>
    /// <remarks>
    /// <para>
    /// EIGHT ARMS AND NO DEFAULT ARM [<c>:L204-L221</c>], so an unrecognised identifier does NOTHING and
    /// reports nothing. That is what makes it safe for an application to add its own items with its own
    /// identifiers: they fall through this dispatch untouched. Adding a default arm - even one that only
    /// logged - would change that.
    /// </para>
    /// <para>
    /// *** THE FIRST ARM IS WHY <see cref="MID_RESERVED"/> IS UNREACHABLE (DEFECT 1). *** <c>:L205</c>
    /// matches <see cref="MID_COLAUTOWIDTH"/>, and the two constants are the same value, so a selection of
    /// 10000 always runs the single-column width computation. There is no arm for the reserved marker and
    /// there cannot be one: two arms of one value do not compile, which is itself a useful demonstration
    /// that the collapse is real.
    /// </para>
    /// <para>
    /// THE OPERAND OF SIX ARMS IS THE CLICKED COLUMN, WHICH THE CLEANUP HAS NOT YET CLEARED. That is the
    /// whole reason DECISION 6 moves the cleanup to the end of <see cref="ApplySelection"/>: dispatching
    /// after a cleanup would pass every one of them an empty column name.
    /// </para>
    /// <para>
    /// THE LAST ARM READS THE OBJECT'S NAME RATHER THAN THE CLICKED COLUMN [<c>:L220</c>], because a cell
    /// copy applies to the cell the pointer was over and not to the header that was clicked. The oracle
    /// wraps it in <c>String(dwo.name)</c> - a conversion that is a no-op for a name that is already text
    /// and is not reproduced as a conversion here.
    /// </para>
    /// </remarks>
    public long OnDefProc(long row, IDataWindowObject dwo, long mid)
    {
        ArgumentNullException.ThrowIfNull(dwo);

        // :L204  choose case mid
        switch (mid)
        {
            // :L205-L206  case MID_COLAUTOWIDTH - and therefore also MID_RESERVED, which is the same
            // value. See DEFECT 1.
            case MID_COLAUTOWIDTH:
            {
                ColumnAutoWidthPlan plan = ColumnAutoWidth(_sColClicked);
                PendingWidthPlan = plan;
                return plan.Code;
            }

            // :L207-L208  case MID_COLAUTOWIDTH_ALL
            case MID_COLAUTOWIDTH_ALL:
            {
                ColumnAutoWidthPlan plan = ColumnAutoWidth();
                PendingWidthPlan = plan;
                return plan.Code;
            }

            // :L209-L210  case MID_COLCHECK
            case MID_COLCHECK:
                return CheckColumn(_sColClicked);

            // :L211-L212  case MID_COLUNCHECK
            case MID_COLUNCHECK:
                return UncheckColumn(_sColClicked);

            // :L213-L214  case MID_COLREVERTCHECK
            case MID_COLREVERTCHECK:
                return RevertCheckColumn(_sColClicked);

            // :L215-L216  case MID_COLPASTE - the ONLY arm that consumes the captured clipboard text.
            case MID_COLPASTE:
                return Paste2Column(_sColClicked, _sClipText);

            // :L217-L218  case MID_COLCOPY
            case MID_COLCOPY:
            {
                ClipboardTextResult result = CopyColumnToText(_sColClicked);
                PendingCopiedText = result.Text;
                return result.Code;
            }

            // :L219-L220  case MID_ITEMCOPY - operates on the object's own name, not on the clicked
            // column.
            case MID_ITEMCOPY:
            {
                ClipboardTextResult result = CopyItemToText(row, dwo.Name);
                PendingCopiedText = result.Text;
                return result.Code;
            }

            // :L221  end choose - NO `case else`. An unrecognised identifier does nothing at all.
            default:
                return RetCode.OK;
        }
    }


    /// <summary>
    /// Builds the DEFAULT item set - the port of <c>event type long onprepare</c> (<c>:L28</c>, body
    /// <c>:L224-L308</c>).
    /// </summary>
    /// <param name="row">The one-based row the menu was requested over, or <c>0</c> when none.</param>
    /// <param name="dwo">The object the menu was requested over.</param>
    /// <param name="pointer">The pointer-derived and clipboard-derived facts - see DECISION 4.</param>
    /// <returns>
    /// <c>0</c> on every path - the oracle has three exits [<c>:L244</c>, <c>:L248</c>, <c>:L307</c>] and
    /// all three answer zero, so this member never vetoes its own menu. The return value exists because
    /// <c>:L146</c> tests it, and an application overriding the event CAN answer <c>1</c>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// THE ORDER OF THE FIVE BLOCKS IS OBSERVABLE, BECAUSE EACH ONE ADDS ITEMS THE NEXT MAY SEPARATE FROM:
    /// the band promotion, then the item-copy block WITH ITS TWO EARLY RETURNS, then the header column
    /// resolution, then the automatic-width block, then the column block. The separators the last two emit
    /// are what the collapse pass in <see cref="BuildMenu"/> then normalises.
    /// </para>
    /// <para>
    /// *** DEFECT 4 - THE TWO ITEM-COPY EARLY RETURNS - PRESERVED VERBATIM. *** Both add a single
    /// cell-copy item and RETURN IMMEDIATELY [<c>:L244</c>, <c>:L248</c>], so a right-click that reaches
    /// either one produces a ONE-ITEM MENU and nothing else: no automatic width, no check items, no column
    /// copy or paste, and no separator. Whether that was intended cannot be established from the
    /// repository, and constraint C-B forbids deciding: the returns stay, and two tests assert the
    /// one-item outcome.
    /// </para>
    /// <para>
    /// THE PRESENTATION STYLE IS READ FIRST AND USED LAST [<c>:L231</c>, tested at <c>:L269</c>], so a
    /// host that changes style mid-invocation is read once. Preserved as written.
    /// </para>
    /// </remarks>
    public long OnPrepare(long row, IDataWindowObject dwo, ContextMenuPointerContext pointer)
    {
        ArgumentNullException.ThrowIfNull(dwo);
        ArgumentNullException.ThrowIfNull(pointer);

        DataWindowServiceHost host = RequireHost();

        // :L231  nStyle = _of_GetStyle()
        long style = GetPresentationStyle();

        // :L233-L234  sBand = #DataWindow.GetBandAtPointer() then Left(sBand,Pos(sBand,"~t") - 1)
        //
        // The raw answer arrives as request data (DECISION 4); the SPLIT is reproduced here, including its
        // quirk - see ContextMenuPointerContext.BandAtPointer.
        string band = SplitBandToken(pointer.BandAtPointer);

        // :L235-L239  a background or foreground band ABOVE the header's height is treated as the header.
        //
        // THE COMPARISON IS AGAINST A CONVERTED Describe ANSWER, AND AN UNCONVERTIBLE ONE DOES NOT PROMOTE.
        // PowerScript's Long of a sentinel yields null, and a comparison against null is null, which an
        // `if` treats as false - so an unreadable header height leaves the band alone rather than promoting
        // it. Reproduced by requiring the parse to succeed.
        if (string.Equals(band, BackgroundBand, StringComparison.Ordinal)
            || string.Equals(band, ForegroundBand, StringComparison.Ordinal))
        {
            long? headerHeight = ParseLong(host.Describe(HeaderHeightProperty));
            if (headerHeight is long height && pointer.PointerY < height)
            {
                // :L237
                band = HeaderBand;
            }
        }

        // :L241-L251  the item-copy block, and DEFECT 4.
        if (ItemCopy)
        {
            // :L242  row > 0 and (dwo.Type = "column" or dwo.Type = "compute")
            if (row > 0L
                && (string.Equals(dwo.Type, ColumnObjectType, StringComparison.Ordinal)
                    || string.Equals(dwo.Type, ComputeObjectType, StringComparison.Ordinal)))
            {
                // :L243  复制单元格 / 复制单元格值 - "copy cell" / "copy the cell's value".
                _ = AddMenu(
                    Translate("复制单元格"),
                    "Copy!",
                    Translate("复制单元格值"),
                    MID_ITEMCOPY);

                // :L244  return 0 - DEFECT 4: nothing else is added.
                return 0L;
            }

            // :L245  elseif dwo.Type = "compute"
            if (string.Equals(dwo.Type, ComputeObjectType, StringComparison.Ordinal))
            {
                // :L246  and the band is none of header, foreground or background - so a computed field in
                // the DETAIL, SUMMARY, FOOTER or a TRAILER band, which is where a computed value the user
                // might want to copy actually appears.
                if (!string.Equals(band, HeaderBand, StringComparison.Ordinal)
                    && !string.Equals(band, ForegroundBand, StringComparison.Ordinal)
                    && !string.Equals(band, BackgroundBand, StringComparison.Ordinal))
                {
                    // :L247  复制 / 复制值 - "copy" / "copy the value". A DIFFERENT label pair from the
                    // cell form above, and the identifier is the SAME - so the dispatch cannot tell them
                    // apart, and does not need to.
                    _ = AddMenu(
                        Translate("复制"),
                        "Copy!",
                        Translate("复制值"),
                        MID_ITEMCOPY);

                    // :L248  return 0 - DEFECT 4 again.
                    return 0L;
                }
            }
        }

        // :L253  bHeaderClicked = (sBand = "header")
        bool headerClicked = string.Equals(band, HeaderBand, StringComparison.Ordinal);

        bool colClicked = false;
        bool colEditable = false;

        // :L254-L266  resolve the clicked header object back to the column it heads.
        if (headerClicked)
        {
            // :L255  the oracle's comment 按标题取列名 - "take the column name from the heading".
            // :L256  if Right(String(dwo.name),2) = "_t"
            //
            // A SUFFIX CONVENTION, NOT A PROPERTY LOOKUP: the DataWindow painter names a column's heading
            // object after the column with "_t" appended, and the oracle relies on that convention rather
            // than asking the DataWindow which column a heading belongs to. A heading renamed by hand
            // therefore resolves to nothing, which is the oracle's behaviour.
            if (dwo.Name.EndsWith(HeaderTextSuffix, StringComparison.Ordinal))
            {
                // :L257  _sColClicked = Left(dwo.name,Len(String(dwo.name)) - 2)
                _sColClicked = dwo.Name[..^HeaderTextSuffix.Length];

                // :L258-L264  and then CONFIRM that the derived name really is a column or a computed
                // field, UNRESOLVING it when it is not.
                string derivedType = host.Describe(_sColClicked + ".Type");
                if (string.Equals(derivedType, ColumnObjectType, StringComparison.Ordinal)
                    || string.Equals(derivedType, ComputeObjectType, StringComparison.Ordinal))
                {
                    // :L260-L261
                    colClicked = true;
                    colEditable = IsEditable() && IsColumnEditable(_sColClicked);
                }
                else
                {
                    // :L263  _sColClicked = "" - the derived name is DISCARDED. This is the observation
                    // point ClickedColumn exists for: the field is written and then unwritten, so a test
                    // that only checked the successful path would not see the unresolve at all.
                    _sColClicked = string.Empty;
                }
            }
        }

        // :L268-L279  the automatic-width block. GRID PRESENTATION ONLY, and note the toggle is tested
        // OUTSIDE the style test so the style is not consulted when the capability is off.
        //
        // *** THIS IS THE ONLY PLACE ColAutoWidth IS CONSULTED - DEFECT 2. *** The emit filter in
        // BuildMenu has no test for it.
        if (ColAutoWidth && style == STYLE_GRID)
        {
            // :L270  of_AddSeparator() - UNCONDITIONAL, so it is emitted even when neither arm below adds
            // anything, which happens for a grid click that resolved no column and no header. The collapse
            // pass then drops it.
            _ = AddSeparator();

            if (colClicked)
            {
                // :L272-L274  the HIERARCHICAL arm (DECISION 1): a parent that owns an initially empty
                // child menu, and then one child inside it. 自动列宽 / 自动调整列宽度 - "automatic column
                // width" / "automatically adjust the column's width". The parent is a SPLIT item, so its
                // label runs the single-column operation and its arrow opens the child.
                _ = AddSubmenuParent(
                    Translate("自动列宽"),
                    "SizeHorizontal!",
                    Translate("自动调整列宽度"),
                    true,
                    MID_COLAUTOWIDTH);

                // :L273-L274  所有列 / 自动调整所有列宽度 - "all columns" / "automatically adjust every
                // column's width". EMPTY IMAGE, unlike its parent.
                _ = AddSubmenuItem(
                    MID_COLAUTOWIDTH,
                    Translate("所有列"),
                    string.Empty,
                    Translate("自动调整所有列宽度"),
                    MID_COLAUTOWIDTH_ALL);
            }
            else if (headerClicked)
            {
                // :L276  the FLAT arm - one item, no child menu, and the ALL-COLUMNS identifier.
                //
                // NOTE THE TIP TEXT DIFFERS FROM THE HIERARCHICAL PARENT'S: the label is the same
                // 自动列宽 but the tip is 自动调整所有列宽度, the ALL-columns wording, because this item
                // does the all-columns operation. Preserved exactly; using the parent's tip here would
                // mislabel the item.
                _ = AddMenu(
                    Translate("自动列宽"),
                    "SizeHorizontal!",
                    Translate("自动调整所有列宽度"),
                    MID_COLAUTOWIDTH_ALL);
            }
        }

        // :L281  if /*nStyle = STYLE_GRID and */bColClicked then
        //
        // THE DORMANT STYLE TEST IS CARRIED AS A COMMENT AND NOT REVIVED (constraint C-B). The author
        // commented out a grid-only restriction on this whole block, so the column items appear for ANY
        // presentation style - which is why a form-style DataWindow can offer "copy column" while it cannot
        // offer automatic width.
        if (colClicked)
        {
            // :L282
            _ = AddSeparator();

            // :L283-L292  the check items, behind FOUR conditions in this order: the capability, the
            // column being editable, the buffer being non-empty, and the column's edit style being a check
            // box. The buffer test is here and NOT on the paste block below, where its equivalent is
            // commented out - see :L298.
            if (ColCheck
                && colEditable
                && host.RowCount() > 0L
                && string.Equals(
                    host.Describe(_sColClicked + ".edit.style"),
                    CheckBoxEditStyle,
                    StringComparison.Ordinal))
            {
                // :L287  勾选列 / 勾选整列 - "tick column" / "tick the whole column". EMPTY IMAGES on all
                // three, unlike the copy and paste items.
                _ = AddMenu(Translate("勾选列"), string.Empty, Translate("勾选整列"), MID_COLCHECK);

                // :L288  清除勾选列 / 清除勾选整列 - "clear the column's ticks" / "clear the whole
                // column's ticks".
                _ = AddMenu(
                    Translate("清除勾选列"),
                    string.Empty,
                    Translate("清除勾选整列"),
                    MID_COLUNCHECK);

                // :L289  反向勾选列 / 反向勾选整列 - "invert the column's ticks" / "invert the whole
                // column's ticks". THE ITEM DEFECT 3 LETS THROUGH THE EMIT FILTER.
                _ = AddMenu(
                    Translate("反向勾选列"),
                    string.Empty,
                    Translate("反向勾选整列"),
                    MID_COLREVERTCHECK);
            }

            // :L293  of_AddSeparator() - UNCONDITIONAL, AND OUTSIDE THE CHECK BLOCK.
            //
            // *** IT IS EMITTED EVEN WHEN THE CHECK BLOCK ADDED NOTHING, *** which is the common case: a
            // non-check-box column, a read-only column or an empty buffer all skip the three items and
            // still get this separator. That is precisely why the collapse pass matters - without it the
            // menu would show two adjacent separators, or a leading one. Preserved as written.
            _ = AddSeparator();

            // :L294-L296  复制列 / 复制整列的数据 - "copy column" / "copy the whole column's data".
            if (ColCopy)
            {
                _ = AddMenu(Translate("复制列"), "Copy!", Translate("复制整列的数据"), MID_COLCOPY);
            }

            // :L297-L304  the paste item.
            //
            // THE DORMANT ROW-COUNT TEST IS CARRIED AS A COMMENT AND NOT REVIVED: :L298 reads
            // `if bColEditable /*and #DataWindow.RowCount() > 0*/ then`. So pasting IS offered into an
            // EMPTY DataWindow, and the paste operation grows the buffer to fit [:L994-L1001] - the two
            // facts are consistent, and reviving the test would forbid a paste the operation supports.
            if (ColPaste && colEditable)
            {
                // :L299  _sClipText = Clipboard() - INVERTED per DECISION 3: the text arrives as request
                // data and is captured here, at the same point in the same sequence.
                _sClipText = pointer.ClipboardText;

                // :L300  and the item appears ONLY if there is something to paste.
                if (_sClipText.Length > 0)
                {
                    // :L301  粘贴列 / 拷贝粘帖板数据覆盖到整列 - "paste column" / "overwrite the whole
                    // column with the clipboard's data".
                    _ = AddMenu(
                        Translate("粘贴列"),
                        "Paste!",
                        Translate("拷贝粘帖板数据覆盖到整列"),
                        MID_COLPASTE);
                }
            }
        }

        // :L307
        return 0L;
    }

    /// <summary>
    /// Whether a right-click at this row and object should raise a menu at all - the port of
    /// <c>_of_IsAllowPopup</c> (<c>:L310-L340</c>).
    /// </summary>
    /// <param name="row">The one-based row, or <c>0</c> when the click was not over a row.</param>
    /// <param name="dwo">The object under the pointer.</param>
    /// <returns><see langword="true"/> to allow the menu.</returns>
    /// <remarks>
    /// <para>
    /// IT ANSWERS "ALLOW" FOR ALMOST EVERYTHING, AND ITS REAL JOB IS THE OPPOSITE OF WHAT ITS NAME
    /// SUGGESTS: it exists to SUPPRESS this framework menu over a cell that is being EDITED, so that the
    /// edit control's own native context menu - cut, copy, paste, select all - is what appears. That is why
    /// seven of its ten exits answer <see langword="true"/> and only three answer
    /// <see langword="false"/>.
    /// </para>
    /// <para>
    /// THE GUARD ORDER IS OBSERVABLE, each test being cheaper or broader than the next: no row at all
    /// [<c>:L312</c>], an invalid object [<c>:L313</c>], a non-column object [<c>:L319</c>], not the
    /// current row [<c>:L320</c>], not the current column [<c>:L321</c>], a read-only DataWindow
    /// [<c>:L322</c>], a column outside the tab order [<c>:L324</c>], a protected cell [<c>:L325</c>], and
    /// only then the edit style [<c>:L328-L337</c>].
    /// </para>
    /// <para>
    /// THE DORMANT DATAWINDOW TEST IS CARRIED AS A COMMENT AND NOT REVIVED (constraint C-B): <c>:L318</c>
    /// reads <c>//if sType = "datawindow" then return false</c>, an abandoned attempt to suppress the menu
    /// over the DataWindow itself. It is dormant, so a right-click on the background DOES raise the menu -
    /// which is what makes the header promotion at <c>:L235-L239</c> reachable.
    /// </para>
    /// <para>
    /// THE TWO TAB-SEQUENCE SENTINELS ARE BOTH LITERAL AND BOTH PRESERVED [<c>:L324</c>]: <c>"0"</c> means
    /// the column is skipped in the tab order and <c>"32766"</c> is PowerBuilder's own marker for a column
    /// the user cannot reach. Either one means the cell is not being edited, so the framework menu is
    /// allowed.
    /// </para>
    /// </remarks>
    private bool IsAllowPopup(long row, IDataWindowObject dwo)
    {
        // :L312  if row <= 0 then return true - a click that is not over a row is not a click on a cell
        // being edited.
        if (row <= 0L)
        {
            return true;
        }

        // :L313  if Not IsValidObject(dwo) then return false - the ONE guard that suppresses the menu for a
        // reason other than an active editor, and the only exit that answers false before any Describe.
        //
        // ALSO DORMANT, FOR A TYPE-SYSTEM REASON. PowerScript can hold a reference to a DESTROYED object
        // and this is the test for it; .NET cannot, so IsValidObject reduces to a null test
        // [shared/PowerFramework.Shared.Kernel/Predicates.cs] and every public entry point here rejects a
        // null argument before reaching this far. Kept as the faithful port of :L313.
        if (!Predicates.IsValidObject(dwo))
        {
            return false;
        }

        DataWindowServiceHost host = RequireHost();

        // :L315  sName = dwo.Name
        string name = dwo.Name;

        // :L317  sType = #DataWindow.Describe(sName+".type") - note the LOWER-CASE property spelling here,
        // against ".Type" at :L258 and :L1105. Describe is case-insensitive about property names, so both
        // work; the spellings are preserved as written.
        string type = host.Describe(name + ".type");

        // :L318  //if sType = "datawindow" then return false - DORMANT, see the remarks.

        // :L319  if sType <> "column" then return true
        if (!string.Equals(type, ColumnObjectType, StringComparison.Ordinal))
        {
            return true;
        }

        // :L320  if #DataWindow.GetRow() <> row then return true
        if (host.GetRow() != row)
        {
            return true;
        }

        // :L321  if #DataWindow.GetColumnName() <> sName then return true
        if (!string.Equals(host.GetColumnName(), name, StringComparison.Ordinal))
        {
            return true;
        }

        // :L322  if Not _of_IsEditable() then return true
        if (!IsEditable())
        {
            return true;
        }

        // :L323-L324  sProp = Describe(sName + ".TabSequence") / if sProp = "0" or sProp = "32766"
        string tabSequence = host.Describe(name + ".TabSequence");
        if (string.Equals(tabSequence, "0", StringComparison.Ordinal)
            || string.Equals(tabSequence, "32766", StringComparison.Ordinal))
        {
            return true;
        }

        // :L325  if _of_IsColumnProtected(sName) then return true
        if (IsColumnProtected(name))
        {
            return true;
        }

        // :L327-L337  the edit style decides the last three exits.
        string editStyle = host.Describe(name + ".edit.style");
        switch (editStyle)
        {
            // :L329-L330  a plain edit or an edit mask IS an editor, so the framework menu is suppressed
            // and the editor's own menu appears.
            case "edit":
            case "editmask":
                return false;

            // :L331-L333  a drop-down DataWindow suppresses the framework menu ONLY when it allows typing -
            // the test being an INEQUALITY against "yes", so any answer other than "yes", INCLUDING BOTH
            // SENTINELS, allows the framework menu.
            case "dddw":
                return !string.Equals(
                    host.Describe(name + ".dddw.allowedit"),
                    YesAnswer,
                    StringComparison.Ordinal);

            // :L334-L336  the same for a drop-down list box.
            case "ddlb":
                return !string.Equals(
                    host.Describe(name + ".ddlb.allowedit"),
                    YesAnswer,
                    StringComparison.Ordinal);

            default:
                // :L339  every other edit style - radio buttons, a check box, none at all - allows the
                // framework menu, because none of them puts a text editor under the pointer.
                return true;
        }
    }

    /// <summary>
    /// Subscribes to or unsubscribes from the two right-button topics - the port of
    /// <c>event onenable</c> (<c>:L1440-L1447</c>).
    /// </summary>
    /// <param name="enabled">The new enablement state.</param>
    /// <returns><c>0</c>, always [<c>:L1446</c>] - so enabling this service can never be vetoed by it.</returns>
    /// <remarks>
    /// <para>
    /// THE BASE CALL COMES FIRST AND ITS RESULT IS DISCARDED [<c>:L1440</c> is
    /// <c>event onenable;call super::onenable;</c>], which is the PowerBuilder idiom for extending an
    /// ancestor event rather than replacing it.
    /// </para>
    /// <para>
    /// TWO SUBSCRIPTIONS ON, ONE UNSUBSCRIBE OFF, AND THE ASYMMETRY IS DELIBERATE. Enabling subscribes the
    /// press and release handlers by name [<c>:L1441-L1442</c>]; disabling removes them BY TARGET
    /// [<c>:L1444</c>], which drops every subscription this service holds in one call rather than naming
    /// the two. So a subscription added by some other route is also dropped - which is the oracle's
    /// behaviour and is not narrowed here.
    /// </para>
    /// <para>
    /// THE THREE OTHER EVENTS ARE NOT SUBSCRIBED, and a reader should expect to find no subscription for
    /// them: the click event is raised by the release handler, the dispatch by the click, and preparation by
    /// the click. Subscribing any of them would fire the whole menu cycle twice.
    /// </para>
    /// </remarks>
    protected override long OnEnable(bool enabled)
    {
        // :L1440  call super::onenable - first, and discarded.
        _ = base.OnEnable(enabled);

        DataWindowServiceHost host = RequireHost();

        if (enabled)
        {
            // :L1441  #DataWindow.of_On(#DataWindow.EVT_RBUTTONDOWN,this,"onRButtonDown")
            _ = host.Eventful.Subscribe(EVT_RBUTTONDOWN, this, nameof(OnRButtonDown));

            // :L1442  #DataWindow.of_On(#DataWindow.EVT_RBUTTONUP,this,"onRButtonUp")
            _ = host.Eventful.Subscribe(EVT_RBUTTONUP, this, nameof(OnRButtonUp));
        }
        else
        {
            // :L1444  #DataWindow.of_Off(this) - by TARGET, so every subscription goes at once.
            _ = host.Eventful.Unsubscribe(this);
        }

        // :L1446
        return 0L;
    }

    /// <summary>
    /// Extracts the band name from a raw band-at-pointer answer - the port of
    /// <c>Left(sBand,Pos(sBand,"~t") - 1)</c> (<c>:L234</c>).
    /// </summary>
    /// <param name="rawBand">The raw answer, in the <c>band~trow</c> form.</param>
    /// <returns>
    /// The band name, or the EMPTY STRING when the answer carries no tab - see the remarks, this is not a
    /// defensive case but the oracle's arithmetic.
    /// </returns>
    /// <remarks>
    /// THE NO-TAB CASE YIELDS EMPTY, NOT THE WHOLE ANSWER. PowerScript's <c>Pos</c> answers <c>0</c> when
    /// the tab is absent, so the expression becomes <c>Left(sBand,-1)</c> and <c>Left</c> with a
    /// non-positive length yields the empty string. An answer of <c>"detail"</c> with no row suffix
    /// therefore matches NO band arm - it is neither a header nor a background - and the click behaves as
    /// though the pointer were nowhere. Reproducing the arithmetic rather than "fixing" it is the whole
    /// point (constraint C-B).
    /// </remarks>
    private static string SplitBandToken(string rawBand)
    {
        int tab = rawBand.IndexOf('\t', StringComparison.Ordinal);

        // Pos answers 0 for no match, making the length -1 and the result empty.
        return tab < 0 ? string.Empty : rawBand[..tab];
    }

    /// <summary>
    /// Translates one of the oracle's Chinese source strings under the DataWindow-service category - the
    /// port of <c>I18N(ne_cst_i18n.CAT_DWSVC, "...")</c>, which the oracle performs 20 times.
    /// </summary>
    /// <param name="key">The Chinese source string, verbatim, which is also the lookup key.</param>
    /// <returns>The translation, or the key unchanged when no provider is installed.</returns>
    /// <remarks>
    /// <para>
    /// THE FACADE'S DOCUMENTED BEHAVIOUR IS SILENT PASSTHROUGH: with no provider installed it returns the
    /// text unchanged, never throwing, never logging and never marking the string untranslated
    /// [<c>ws_objects/pfw.ui.pbl.src/i18n.srf</c>]. So the Chinese source string IS the fallback, and that
    /// is why the keys are reproduced verbatim in this file rather than moved to a resource: moving them
    /// would change what an untranslated build displays.
    /// </para>
    /// <para>
    /// A NULL ANSWER IS SUBSTITUTED WITH THE EMPTY STRING, WHICH IS A NARROWING AND IS RECORDED AS ONE.
    /// The facade translates by MUTATING a reference, so a provider that assigns null answers null. In
    /// PowerScript that null would flow into the item's label, where the empty-label guard
    /// <c>if text = ""</c> [<c>:L480</c>] evaluates to null and therefore does NOT fire, storing an item
    /// whose label is null and which compares unequal to the separator marker. That behaviour cannot be
    /// reproduced in a non-nullable string without spreading nullability through the whole store, and AAP
    /// 0.1.5 requires narrowing with a DEFINED value rather than widening with a guess - so the defined
    /// value is the empty string, which the guard then rejects. No shipped provider nulls its argument and
    /// every key here is a non-empty literal, so the substitution is unreachable in practice; it is
    /// documented because it is a real, if pathological, divergence.
    /// </para>
    /// </remarks>
    private string Translate(string key)
    {
        return _i18n.I18N(Categories.CAT_DWSVC, key) ?? string.Empty;
    }

    /// <summary>
    /// The port of PowerScript's <c>Long(string)</c> - a total conversion that answers
    /// <see langword="null"/> rather than raising for text that is not an integer.
    /// </summary>
    /// <param name="text">The text to convert.</param>
    /// <returns>The value, or <see langword="null"/> when the text does not convert.</returns>
    /// <remarks>
    /// SINGLE-SOURCED THROUGH THE NUMBER VALIDATOR so that this file's conversion and the one the
    /// validation path applies to a pasted number cannot drift apart - both are
    /// <c>Validators/NumberValidator.cs</c>'s, which pins the culture and the permitted styles in one
    /// place. NULL IS PROPAGATED, NEVER COLLAPSED TO ZERO (AAP 0.4.5.4): every consumer here treats a null
    /// as "the comparison does not fire", which is what PowerScript does with a null operand.
    /// </remarks>
    private static long? ParseLong(string? text)
    {
        return NumberValidator.CoerceToLong(text);
    }


    // ==========================================================================================
    //  THE FOUR COLUMN OPERATIONS                            n_cst_dwsvc_contextmenu.sru:L767-L1083
    //  ----------------------------------------------------------------------------------------
    //  THREE CHECK-BOX OPERATIONS AND ONE PASTE, all four sharing one shape: guard the arguments, read
    //  the column's shape, then walk the buffer raising the host's item-change events and writing what
    //  it accepts, with repainting suppressed LAZILY on the first row actually written and restored in a
    //  finally.
    //
    //  *** DEFECT 7: THE THREE CHECK OPERATIONS DO NOT AGREE ON HOW TO READ A COLUMN'S TYPE. ***
    //  CheckColumn [:L779] and RevertCheckColumn [:L835] use the base's converted COL_TYPE_* value,
    //  while UncheckColumn [:L900] uses the FIRST FIVE CHARACTERS of the raw column type with its own
    //  five-token arm list. The two mechanisms disagree for at least one real type - a `real` column is
    //  COL_TYPE_DECIMAL to the first two but matches "real" in the third's decimal arm, which happens to
    //  agree, while a `char(n)` column is COL_TYPE_STRING to the first two and falls to the third's
    //  default, which also agrees - so the divergence is latent rather than active on the types the
    //  primary fixture carries. It is preserved per operation regardless: harmonising them would be a
    //  correction, and a latent divergence that becomes active on some other schema is exactly the kind
    //  of behaviour a characterization suite has to be able to reproduce.
    //
    //  THE PUBLIC NAMES DROP THE ORACLE'S `_of_` PRIVATE-CONVENTION PREFIX AND BECOME PUBLIC, because
    //  the dispatch is the only legacy caller and that dispatch is split across a boundary
    //  (DECISION 5) - so an internal detail of one event becomes a callable operation. The
    //  prefix is not part of any payload, log record or recording, so AAP 0.4.5.3 does not reach it.
    // ==========================================================================================

    /// <summary>
    /// Sets every visible, unprotected cell in a check-box column to its ON value - the port of
    /// <c>_of_CheckColumn</c> (<c>:L767-L819</c>).
    /// </summary>
    /// <param name="colName">The column name.</param>
    /// <returns>
    /// <c>RetCode.OK</c>; <c>RetCode.E_INVALID_ARGUMENT</c> for an empty name [<c>:L773</c>];
    /// <c>RetCode.FAILED</c> when the check box's ON value is unreadable [<c>:L776</c>] or when the host
    /// REFUSED a change [<c>:L796</c>], in which case <see cref="PendingError"/> carries the message.
    /// </returns>
    /// <remarks>
    /// <para>
    /// IT STOPS AT THE FIRST REFUSAL AND LEAVES EARLIER ROWS WRITTEN [<c>:L794-L797</c>], so a refusal
    /// half-way through is a PARTIAL application and the operation reports failure for the whole. That is
    /// the oracle's behaviour and there is no rollback anywhere in it.
    /// </para>
    /// <para>
    /// REPAINTING IS SUPPRESSED LAZILY AND RESTORED IN A FINALLY [<c>:L798-L801</c>, <c>:L812-L816</c>], so
    /// an operation that changed nothing never touches repainting at all - and one that threw still
    /// restores it. The oracle's own <c>try</c> opens at <c>:L781</c>, BEFORE the loop and AFTER the
    /// argument guards, so a rejected argument likewise never touches repainting.
    /// </para>
    /// <para>
    /// A ROW ALREADY HOLDING THE ON VALUE IS SKIPPED WITHOUT RAISING ANYTHING [<c>:L786-L793</c>], which
    /// is what keeps the change event from firing for rows that are not changing.
    /// </para>
    /// </remarks>
    public long CheckColumn(in string colName)
    {
        ArgumentNullException.ThrowIfNull(colName);

        PendingError = null;

        // :L773  if colName = "" then return RetCode.E_INVALID_ARGUMENT
        if (colName.Length == 0)
        {
            return RetCode.E_INVALID_ARGUMENT;
        }

        DataWindowServiceHost host = RequireHost();

        // :L775-L776  the ON value, and the sentinel guard that makes a non-check-box column fail.
        string value = host.Describe(colName + ".checkbox.on");
        if (IsUnreadableAnswer(value))
        {
            return RetCode.FAILED;
        }

        // :L778-L779
        IDataWindowObject dwo = RequireColumnObject(colName);
        long colType = GetColumnType(colName);

        bool redrawSuppressed = false;

        // :L781  try
        try
        {
            // :L782-L783  the row count is read ONCE, before the walk.
            long rowCount = host.RowCount();
            for (long row = 1L; row <= rowCount; row++)
            {
                // :L784  the oracle's comment on the sibling operation is 忽略不可见的单元格 - "ignore
                // invisible cells".
                if (!IsItemVisible(row, colName))
                {
                    continue;
                }

                // :L785  and protected ones.
                if (IsItemProtected(row, colName))
                {
                    continue;
                }

                // :L786-L793  skip a row that already holds the target value. THE COMPARISON IS ON TEXT in
                // every arm, including the two numeric ones, which stringify first.
                if (string.Equals(ReadItemByColumnType(host, row, colName, colType), value, StringComparison.Ordinal))
                {
                    continue;
                }

                // :L794  ANY non-zero answer is a refusal - the test is `<> 0`, not the four-value
                // item-change alphabet and not the return-code algebra.
                if (host.OnDoItemChange(row, dwo, value) != 0L)
                {
                    // :L795  the dialog, converted (DECISION 7).
                    PendingError = BuildChangeRejectedError(row, "n_cst_dwsvc_contextmenu.sru:L795");

                    // :L796
                    return RetCode.FAILED;
                }

                // :L798-L801  lazily, on the first row actually written.
                if (!redrawSuppressed)
                {
                    redrawSuppressed = true;
                    _ = host.SetRedraw(false);
                }

                // :L802-L809  the write, whose arms mirror the read's.
                WriteItemByColumnType(host, row, colName, colType, value);

                // :L810  the after-change notification, per row, carrying no return value.
                host.OnDoItemChanged(row, dwo);
            }
        }
        finally
        {
            // :L812-L815  restore repainting only if it was suppressed.
            if (redrawSuppressed)
            {
                _ = host.SetRedraw(true);
            }
        }

        // :L818
        return RetCode.OK;
    }

    /// <summary>
    /// Clears every visible, unprotected cell in a check-box column to its OFF value - the port of
    /// <c>_of_UncheckColumn</c> (<c>:L889-L940</c>).
    /// </summary>
    /// <param name="colName">The column name.</param>
    /// <returns>
    /// <c>RetCode.OK</c>; <c>RetCode.E_INVALID_ARGUMENT</c> for an empty name [<c>:L894</c>];
    /// <c>RetCode.FAILED</c> when the OFF value is unreadable [<c>:L897</c>] or when the host refused a
    /// change [<c>:L917</c>].
    /// </returns>
    /// <remarks>
    /// *** DEFECT 7 LIVES HERE. *** This operation is otherwise a mirror image of
    /// <see cref="CheckColumn"/>, but it reads the column's type by a DIFFERENT MECHANISM: <c>:L900</c> is
    /// <c>sColType = Left(#DataWindow.Describe(colName+".ColType"),5)</c> and its arms are the raw tokens
    /// <c>"decim"</c>/<c>"real"</c> and <c>"numbe"</c>/<c>"long"</c>/<c>"ulong"</c> [<c>:L907-L914</c>,
    /// <c>:L923-L930</c>], where its two siblings use the base's converted <c>COL_TYPE_*</c> value. The two
    /// are NOT harmonised - see the banner above this region for why.
    /// </remarks>
    public long UncheckColumn(in string colName)
    {
        ArgumentNullException.ThrowIfNull(colName);

        PendingError = null;

        // :L894
        if (colName.Length == 0)
        {
            return RetCode.E_INVALID_ARGUMENT;
        }

        DataWindowServiceHost host = RequireHost();

        // :L896-L897  the OFF value, and the same sentinel guard.
        string value = host.Describe(colName + ".checkbox.off");
        if (IsUnreadableAnswer(value))
        {
            return RetCode.FAILED;
        }

        // :L899
        IDataWindowObject dwo = RequireColumnObject(colName);

        // :L900  *** DEFECT 7: the raw five-character type prefix, not the converted COL_TYPE_* value. ***
        string colTypePrefix = TruncateColumnType(host.Describe(colName + ".ColType"));

        bool redrawSuppressed = false;

        // :L902
        try
        {
            // :L903-L904
            long rowCount = host.RowCount();
            for (long row = 1L; row <= rowCount; row++)
            {
                // :L905
                if (!IsItemVisible(row, colName))
                {
                    continue;
                }

                // :L906
                if (IsItemProtected(row, colName))
                {
                    continue;
                }

                // :L907-L914  the prefix-keyed read.
                if (string.Equals(
                        ReadItemByColumnTypePrefix(host, row, colName, colTypePrefix),
                        value,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                // :L915
                if (host.OnDoItemChange(row, dwo, value) != 0L)
                {
                    // :L916
                    PendingError = BuildChangeRejectedError(row, "n_cst_dwsvc_contextmenu.sru:L916");

                    // :L917
                    return RetCode.FAILED;
                }

                // :L919-L922
                if (!redrawSuppressed)
                {
                    redrawSuppressed = true;
                    _ = host.SetRedraw(false);
                }

                // :L923-L930  the prefix-keyed write.
                WriteItemByColumnTypePrefix(host, row, colName, colTypePrefix, value);

                // :L931
                host.OnDoItemChanged(row, dwo);
            }
        }
        finally
        {
            // :L933-L936
            if (redrawSuppressed)
            {
                _ = host.SetRedraw(true);
            }
        }

        // :L939
        return RetCode.OK;
    }

    /// <summary>
    /// Inverts every visible, unprotected cell in a check-box column - the port of
    /// <c>_of_RevertCheckColumn</c> (<c>:L821-L887</c>).
    /// </summary>
    /// <param name="colName">The column name.</param>
    /// <returns>
    /// <c>RetCode.OK</c>; <c>RetCode.E_INVALID_ARGUMENT</c> for an empty name [<c>:L827</c>];
    /// <c>RetCode.FAILED</c> when EITHER the ON or the OFF value is unreadable [<c>:L830</c>,
    /// <c>:L832</c>] or when the host refused a change [<c>:L864</c>].
    /// </returns>
    /// <remarks>
    /// <para>
    /// IT READS BOTH VALUES AND GUARDS BOTH, which its two siblings do not - each of them needs only one.
    /// The two guards are SEPARATE statements in the oracle, so an unreadable ON value fails before the OFF
    /// value is even read.
    /// </para>
    /// <para>
    /// THERE IS NO SKIP-IF-EQUAL TEST HERE, AND THERE CANNOT BE ONE: the target value is computed per row
    /// as the OPPOSITE of what the row holds [<c>:L842-L861</c>], so it always differs and every visible
    /// unprotected row fires the change event. That is why this operation is the most expensive of the
    /// three and why a refusal is most likely to be seen here.
    /// </para>
    /// <para>
    /// THE INVERSION IS AGAINST THE ON VALUE ONLY: a cell holding NEITHER value - a null, or a third value
    /// left by some other route - compares unequal to ON and is therefore set to ON. So the first invert of
    /// a column with unset cells TICKS them rather than leaving them alone. Preserved as written.
    /// </para>
    /// </remarks>
    public long RevertCheckColumn(in string colName)
    {
        ArgumentNullException.ThrowIfNull(colName);

        PendingError = null;

        // :L827
        if (colName.Length == 0)
        {
            return RetCode.E_INVALID_ARGUMENT;
        }

        DataWindowServiceHost host = RequireHost();

        // :L829-L830  the ON value and its guard.
        string on = host.Describe(colName + ".checkbox.on");
        if (IsUnreadableAnswer(on))
        {
            return RetCode.FAILED;
        }

        // :L831-L832  the OFF value and its own, separate guard.
        string off = host.Describe(colName + ".checkbox.off");
        if (IsUnreadableAnswer(off))
        {
            return RetCode.FAILED;
        }

        // :L834-L835
        IDataWindowObject dwo = RequireColumnObject(colName);
        long colType = GetColumnType(colName);

        bool redrawSuppressed = false;

        // :L837
        try
        {
            // :L838-L839
            long rowCount = host.RowCount();
            for (long row = 1L; row <= rowCount; row++)
            {
                // :L840
                if (!IsItemVisible(row, colName))
                {
                    continue;
                }

                // :L841
                if (IsItemProtected(row, colName))
                {
                    continue;
                }

                // :L842-L861  the per-row inversion, whose three arms differ from the siblings' only in
                // what they do with the comparison rather than in how they read.
                string value = string.Equals(
                    ReadItemByColumnType(host, row, colName, colType),
                    on,
                    StringComparison.Ordinal)
                    ? off
                    : on;

                // :L862
                if (host.OnDoItemChange(row, dwo, value) != 0L)
                {
                    // :L863
                    PendingError = BuildChangeRejectedError(row, "n_cst_dwsvc_contextmenu.sru:L863");

                    // :L864
                    return RetCode.FAILED;
                }

                // :L866-L869
                if (!redrawSuppressed)
                {
                    redrawSuppressed = true;
                    _ = host.SetRedraw(false);
                }

                // :L870-L877
                WriteItemByColumnType(host, row, colName, colType, value);

                // :L878
                host.OnDoItemChanged(row, dwo);
            }
        }
        finally
        {
            // :L880-L883
            if (redrawSuppressed)
            {
                _ = host.SetRedraw(true);
            }
        }

        // :L886
        return RetCode.OK;
    }


    /// <summary>
    /// Writes clipboard text down one column, one pasted line per row - the port of
    /// <c>_of_Paste2Column</c> (<c>n_cst_dwsvc_contextmenu.sru:L942-L1083</c>).
    /// </summary>
    /// <param name="colName">The column to write. The empty string is rejected [<c>:L950</c>].</param>
    /// <param name="data">
    /// The text to write, one row per CRLF-delimited line. THE CALLER SUPPLIES IT - a headless service
    /// cannot read a clipboard, which is DECISION 3's inversion; the oracle reads it once into
    /// <c>_sClipText</c> at <c>:L299</c> and hands it to this operation from the dispatch at
    /// <c>:L216</c>. The empty string is rejected [<c>:L950</c>].
    /// </param>
    /// <returns>
    /// <see cref="RetCode.OK"/> when every line was applied;
    /// <see cref="RetCode.E_INVALID_ARGUMENT"/> for an empty argument [<c>:L950</c>], a zero split
    /// count [<c>:L953</c>], a value outside an enumerated column's value set [<c>:L1010</c>] or text
    /// whose type does not match the column [<c>:L1019</c>, <c>:L1028</c>, <c>:L1034</c>,
    /// <c>:L1040</c>, <c>:L1046</c>]; <see cref="RetCode.FAILED"/> for an unknown column type
    /// [<c>:L957</c>], a refused row insert [<c>:L999</c>] or a refused change [<c>:L1053</c>].
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="colName"/> or <paramref name="data"/> is <see langword="null"/>. Structural
    /// guards; the oracle's arguments are PowerScript strings that its own dispatch never nulls.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// No host is attached, or the attached host cannot resolve <paramref name="colName"/> to a
    /// DataWindow object. See <see cref="RequireColumnObject"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// DEFECT 10 TO PRESERVE - THE LOOP IS BOUNDED BY THE PASTED LINE COUNT, NOT BY THE ROW COUNT
    /// [<c>:L993</c>], and a line
    /// past the last row INSERTS one [<c>:L994-L1001</c>]. Pasting more lines than there are rows
    /// therefore GROWS the buffer, one row per surplus line, and a refused insert fails the whole
    /// operation [<c>:L999</c>]. This is the only column operation in the file that can change the row
    /// count.
    /// </para>
    /// <para>
    /// THE SPLIT KEEPS EMPTY LINES [<c>:L952</c>, <c>ignoreEmpty</c> false], which is load-bearing
    /// rather than incidental: a blank cell in the middle of the pasted block must land on the row it
    /// was copied from. Dropping empties would shift every subsequent row up by one and silently
    /// write the wrong cells. The trailing delimiter that
    /// <see cref="CopyColumnToText"/> always emits does NOT produce a surplus line, because the split
    /// appends its tail only when the tail is non-empty [<c>n_cst_dwsvc.sru:L473-L475</c>] - so a
    /// copy-then-paste round trip over the same column does not grow the buffer.
    /// </para>
    /// <para>
    /// TRANSLATION AND ENUMERATION ARE TWO INDEPENDENT FLAGS DISCOVERED FROM THE EDITOR
    /// [<c>:L967-L985</c>], and only their disjunction builds the value map [<c>:L986-L988</c>]. A
    /// drop-down DataWindow translates when its display and data columns differ and enumerates when it
    /// forbids editing; a drop-down list ALWAYS translates and enumerates only when it forbids editing;
    /// radio buttons and a check box both translate AND enumerate unconditionally. When the pasted text
    /// is not a key of an ENUMERATED column the operation stops with an invalid-value error
    /// [<c>:L1008-L1011</c>] - but when the column merely translates, an unknown value is written
    /// THROUGH UNCHANGED, which is how a free-text drop-down accepts a value outside its list.
    /// </para>
    /// <para>
    /// THE MAP IS <see cref="OrderedMap"/> AND NOT A DICTIONARY (AAP 0.2.1.3 Correction 2). Its
    /// insertion order is observable through <c>Get(int)</c> / <c>GetKey(int)</c>, whose positional
    /// overloads are ONE-BASED with an inclusive upper bound of <c>Count()</c>; this operation reads it
    /// only by key, but the type is the one the base helper returns and substituting a dictionary here
    /// would let a caller lose the ordering the rest of the layer relies on.
    /// </para>
    /// <para>
    /// THE PER-TYPE ARMS EACH DO THREE THINGS IN ONE ORDER [<c>:L1013-L1049</c>]: validate the text
    /// against the column's type, skip the row when the value is already what would be written, and -
    /// for the decimal arm ONLY - rewrite a trailing-percent value by dividing it by 100
    /// [<c>:L1023-L1025</c>]. The string arm validates nothing, because any text is a legal string.
    /// </para>
    /// <para>
    /// DEFECT 11 TO PRESERVE - THE DATETIME ARM REJECTS A LEGITIMATE VALUE
    /// [<c>:L1032</c>]. Its test is <c>DateTime(sVal) = dttEmpty</c> against an UNINITIALISED
    /// <c>datetime</c> local [declared <c>:L945</c>], whose PowerScript initial value is
    /// <c>1900-01-01 00:00:00</c> - which is also what a failed <c>DateTime()</c> conversion answers.
    /// The arm therefore cannot distinguish "not a datetime" from "the datetime 1900-01-01 00:00:00",
    /// and pasting that instant reports a type mismatch. Reproduced, not corrected (constraint C-B).
    /// </para>
    /// <para>
    /// DEFECT TO PRESERVE - A REFUSAL LEAVES EARLIER ROWS WRITTEN [<c>:L1051-L1054</c>], exactly as the
    /// three check-box operations do. There is no rollback anywhere in the oracle.
    /// </para>
    /// <para>
    /// NULL IS WRITTEN AS NULL, NEVER AS AN EMPTY STRING [<c>:L1050</c>]. An empty pasted cell in a
    /// column that declares <c>NilIsNull = "yes"</c> becomes a null through <c>SetNull(sVal)</c>, and
    /// that null is then handed BOTH to the host's item-change event [<c>:L1051</c>] and to the write
    /// [<c>:L1059-L1072</c>]. Domain/DataWindowServiceHost.cs's event parameter is nullable for this
    /// path and this path alone.
    /// </para>
    /// <para>
    /// POWERSCRIPT'S THREE-VALUED COMPARISON IS REPRODUCED, NOT NORMALISED. Every skip test here goes
    /// through a <c>PbEquals</c> overload that answers false when EITHER side is null, because in
    /// PowerScript <c>null = null</c> is null and an <c>if</c> does not take a null branch. A C#
    /// <c>==</c> on two nulls would answer true and skip a row the oracle writes - see
    /// <see cref="PbEquals(decimal?, decimal?)"/>.
    /// </para>
    /// </remarks>
    public long Paste2Column(in string colName, in string data)
    {
        ArgumentNullException.ThrowIfNull(colName);
        ArgumentNullException.ThrowIfNull(data);

        PendingError = null;

        // :L950  if colName = "" or data = "" then return RetCode.E_INVALID_ARGUMENT
        if (colName.Length == 0 || data.Length == 0)
        {
            return RetCode.E_INVALID_ARGUMENT;
        }

        // :L952  nCount = _of_SplitString(data,"~r~n",ref sVals,false) - EMPTIES KEPT.
        string[] values = [];
        int count = SplitString(data, RowSeparator, ref values, false);

        // :L953
        //
        // UNREACHABLE AS WRITTEN AND KEPT ANYWAY. The empty-data rejection above has already run, and the
        // split keeps empty segments, so a non-empty subject always yields at least one. The oracle tests
        // the count regardless and dropping the test would narrow its contract (constraint C-B), so it
        // stays - annotated rather than deleted, and rather than left to look like an oversight.
        if (count == 0)
        {
            return RetCode.E_INVALID_ARGUMENT;
        }

        DataWindowServiceHost host = RequireHost();

        // :L956-L957  the type guard. THE ORACLE RESOLVES THE DATAWINDOW OBJECT FIRST, at :L955, but the
        // resolution has no observable effect, so it is performed after this guard here - otherwise a
        // column the host cannot resolve would raise a structural fault instead of returning the FAILED
        // the oracle returns for an unknown column type.
        long colType = GetColumnType(colName);
        if (colType == COL_TYPE_UNKNOWN)
        {
            return RetCode.FAILED;
        }

        // :L955
        IDataWindowObject dwo = RequireColumnObject(colName);

        // :L959-L965  NilIsNull across the three editors, FIRST MATCH WINS - the oracle's elseif chain
        // stops at the first "yes", so a column that declares it on more than one editor reads it once.
        bool emptyStringIsNull =
            string.Equals(host.Describe(colName + ".edit.NilIsNull"), YesAnswer, StringComparison.Ordinal)
            || string.Equals(host.Describe(colName + ".dddw.NilIsNull"), YesAnswer, StringComparison.Ordinal)
            || string.Equals(host.Describe(colName + ".ddlb.NilIsNull"), YesAnswer, StringComparison.Ordinal);

        bool translation = false;
        bool enumValue = false;

        // :L967  sProp = Describe(colName+".dddw.name")
        string prop = host.Describe(colName + ".dddw.name");
        if (!IsUnreadableAnswer(prop))
        {
            // :L969  a drop-down DataWindow translates only when display and data differ.
            translation = !string.Equals(
                host.Describe(colName + ".dddw.displaycolumn"),
                host.Describe(colName + ".dddw.datacolumn"),
                StringComparison.Ordinal);

            // :L970-L971
            prop = host.Describe(colName + ".dddw.allowedit");
            enumValue = string.Equals(prop, NoAnswer, StringComparison.Ordinal);
        }
        else
        {
            // :L973-L974  a drop-down list is detected by a property that has nothing to do with
            // translation - `.ddlb.case` - purely because it reads back only for a DDLB.
            prop = host.Describe(colName + ".ddlb.case");
            if (!IsUnreadableAnswer(prop))
            {
                // :L975  ALWAYS translates, unconditionally.
                translation = true;

                // :L976-L977
                prop = host.Describe(colName + ".ddlb.allowedit");
                enumValue = string.Equals(prop, NoAnswer, StringComparison.Ordinal);
            }
            else
            {
                // :L979-L983  radio buttons and a check box both translate AND enumerate. NOTE THE
                // PLURAL "radiobuttons", which is this file's spelling; Services/ColumnSortModel.cs
                // preserves the SINGULAR "radiobutton" of its own oracle
                // [n_cst_dwsvc_columnsort.sru:L108]. The inconsistency is legacy and is not harmonised.
                prop = host.Describe(colName + ".edit.style");
                if (string.Equals(prop, RadioButtonsEditStyle, StringComparison.Ordinal)
                    || string.Equals(prop, CheckBoxEditStyle, StringComparison.Ordinal))
                {
                    translation = true;
                    enumValue = true;
                }
            }
        }

        // :L986-L988  the map is built only when one of the two flags is set.
        OrderedMap? valueMap = null;
        if (translation || enumValue)
        {
            valueMap = GetColumnValueMap(colName);
        }

        // :L990  read ONCE, then incremented by each insert.
        long rowCount = host.RowCount();

        bool redrawSuppressed = false;

        // :L992  try
        try
        {
            // :L993  DEFECT 10 - bounded by the PASTED LINE COUNT, so more lines than rows GROWS the
            // buffer rather than discarding the surplus.
            for (long row = 1L; row <= count; row++)
            {
                // :L994-L1001  grow the buffer to reach the pasted line.
                if (row > rowCount)
                {
                    if (!redrawSuppressed)
                    {
                        redrawSuppressed = true;
                        _ = host.SetRedraw(false);
                    }

                    // :L999  InsertRow(0) appends; a non-positive answer fails the whole operation.
                    if (host.InsertRow(0L) <= 0L)
                    {
                        return RetCode.FAILED;
                    }

                    // :L1000
                    rowCount++;
                }

                // :L1002-L1003  the same two per-row filters the check-box operations apply, and they
                // run AFTER the insert, so a row this operation just created is tested like any other.
                if (!IsItemVisible(row, colName))
                {
                    continue;
                }

                if (IsItemProtected(row, colName))
                {
                    continue;
                }

                // :L1004  sVal = sVals[nRow] - a ONE-BASED read of the split array (AAP 0.4.5.4 / R9).
                string? value = PastedValueAt(values, row);

                // :L1005-L1012  display value to data value.
                if (translation || enumValue)
                {
                    if (valueMap is not null && value is not null && valueMap.Exists(value))
                    {
                        // :L1007  sVal = String(mapValue.Get(sVal))
                        value = ToText(valueMap.Get(value));
                    }
                    else if (enumValue)
                    {
                        // :L1009  the dialog, converted (DECISION 7).
                        PendingError = BuildInvalidValueError(
                            row,
                            value,
                            "n_cst_dwsvc_contextmenu.sru:L1009");

                        // :L1010
                        return RetCode.E_INVALID_ARGUMENT;
                    }
                }

                // :L1013-L1049  validate, then skip a row that already holds the value.
                switch (colType)
                {
                    case COL_TYPE_STRING:
                        // :L1015  no validation - any text is a legal string.
                        if (PbEquals(host.GetItemString(row, colName), value))
                        {
                            continue;
                        }

                        break;

                    case COL_TYPE_INTEGER:
                        // :L1017-L1020
                        if (HasText(value) && !IsNumber(value))
                        {
                            PendingError = BuildTypeMismatchError(
                                row,
                                value,
                                "n_cst_dwsvc_contextmenu.sru:L1018");

                            return RetCode.E_INVALID_ARGUMENT;
                        }

                        // :L1021  GetItemNumber answers a double even in the integer arm; the oracle
                        // compares it against Long(sVal), so the comparison widens to the double.
                        if (PbEquals(host.GetItemNumber(row, colName), ParseLong(value)))
                        {
                            continue;
                        }

                        break;

                    case COL_TYPE_DECIMAL:
                        // :L1023-L1025  A TRAILING PERCENT SIGN IS A DIVISION BY 100, applied BEFORE the
                        // numeric validation and mutating the value that is later written. "50%" writes
                        // 0.5. The rewritten text is what every subsequent step sees.
                        if (value is not null && value.EndsWith('%'))
                        {
                            decimal? scaled = ParseDecimal(value[..^1]);
                            value = scaled is null
                                ? null
                                : (scaled.Value / 100m).ToString(CultureInfo.InvariantCulture);
                        }

                        // :L1026-L1029
                        if (HasText(value) && !IsNumber(value))
                        {
                            PendingError = BuildTypeMismatchError(
                                row,
                                value,
                                "n_cst_dwsvc_contextmenu.sru:L1027");

                            return RetCode.E_INVALID_ARGUMENT;
                        }

                        // :L1030
                        if (PbEquals(host.GetItemDecimal(row, colName), ParseDecimal(value)))
                        {
                            continue;
                        }

                        break;

                    case COL_TYPE_DATETIME:
                        // :L1032-L1035  the uninitialised-local test, WITH its false positive on the
                        // instant 1900-01-01 00:00:00 - see the remarks.
                        if (HasText(value) && ParseDateTimeLikePowerScript(value) == DateTimeEmpty)
                        {
                            PendingError = BuildTypeMismatchError(
                                row,
                                value,
                                "n_cst_dwsvc_contextmenu.sru:L1033");

                            return RetCode.E_INVALID_ARGUMENT;
                        }

                        // :L1036
                        if (PbEquals(host.GetItemDateTime(row, colName), ParseDateTime(value)))
                        {
                            continue;
                        }

                        break;

                    case COL_TYPE_DATE:
                        // :L1038-L1041
                        if (HasText(value) && !IsDateText(value))
                        {
                            PendingError = BuildTypeMismatchError(
                                row,
                                value,
                                "n_cst_dwsvc_contextmenu.sru:L1039");

                            return RetCode.E_INVALID_ARGUMENT;
                        }

                        // :L1042
                        if (PbEquals(host.GetItemDate(row, colName), ParseDate(value)))
                        {
                            continue;
                        }

                        break;

                    case COL_TYPE_TIME:
                        // :L1044-L1047
                        if (HasText(value) && !IsTimeText(value))
                        {
                            PendingError = BuildTypeMismatchError(
                                row,
                                value,
                                "n_cst_dwsvc_contextmenu.sru:L1045");

                            return RetCode.E_INVALID_ARGUMENT;
                        }

                        // :L1048
                        if (PbEquals(host.GetItemTime(row, colName), ParseTime(value)))
                        {
                            continue;
                        }

                        break;

                    default:
                        // NO ARM FOR COL_TYPE_UNKNOWN OR ANY OTHER VALUE, and none is needed: the
                        // oracle's `choose case` has no `case else` [:L1049] and the type was already
                        // guarded at :L957. A value that reached here would simply fall through to the
                        // write, which is exactly what PowerScript does.
                        break;
                }

                // :L1050  SetNull(sVal) - the empty pasted cell becomes a real null.
                if (value is not null && value.Length == 0 && emptyStringIsNull)
                {
                    value = null;
                }

                // :L1051  ANY non-zero answer is a refusal.
                if (host.OnDoItemChange(row, dwo, value) != 0L)
                {
                    // :L1052  the dialog, converted (DECISION 7).
                    PendingError = BuildChangeRejectedError(row, "n_cst_dwsvc_contextmenu.sru:L1052");

                    // :L1053
                    return RetCode.FAILED;
                }

                // :L1055-L1058  lazily, on the first row actually written - and note a row INSERT above
                // has already suppressed repainting, so this is a second, idempotent chance at it.
                if (!redrawSuppressed)
                {
                    redrawSuppressed = true;
                    _ = host.SetRedraw(false);
                }

                // :L1059-L1072  the write, one arm per column type, each converting the text the arm
                // above validated. A null value writes a null item in every arm.
                switch (colType)
                {
                    case COL_TYPE_STRING:
                        // :L1061
                        _ = host.SetItem(row, colName, value);
                        break;

                    case COL_TYPE_INTEGER:
                        // :L1063  SetItem(...,Long(sVal))
                        _ = host.SetItem(row, colName, ParseLong(value));
                        break;

                    case COL_TYPE_DECIMAL:
                        // :L1065  SetItem(...,Dec(sVal))
                        _ = host.SetItem(row, colName, ParseDecimal(value));
                        break;

                    case COL_TYPE_DATETIME:
                        // :L1067  SetItem(...,DateTime(sVal))
                        _ = host.SetItem(row, colName, ParseDateTime(value));
                        break;

                    case COL_TYPE_DATE:
                        // :L1069  SetItem(...,Date(sVal))
                        _ = host.SetItem(row, colName, ParseDate(value));
                        break;

                    case COL_TYPE_TIME:
                        // :L1071  SetItem(...,Time(sVal))
                        _ = host.SetItem(row, colName, ParseTime(value));
                        break;

                    default:
                        // As above: the oracle has no `case else` here either [:L1072], so a type it
                        // does not name writes NOTHING while still raising the after-change event below.
                        break;
                }

                // :L1073  the after-change notification, per row.
                host.OnDoItemChanged(row, dwo);
            }
        }
        finally
        {
            // :L1076  if IsValidObject(mapValue) then Destroy mapValue. THE DESTROY HAS NO ANALOGUE -
            // the map is a managed object with no unmanaged handle and no IDisposable surface, so it is
            // collected rather than released. Purging it would be a no-op on a local that is about to
            // go out of scope, and emitting one would imply a lifetime contract OrderedMap does not
            // have. The local is simply dropped.
            _ = valueMap;

            // :L1077-L1079  restore repainting only if it was suppressed.
            if (redrawSuppressed)
            {
                _ = host.SetRedraw(true);
            }
        }

        // :L1082
        return RetCode.OK;
    }


    // ==========================================================================================
    //  THE TWO CLIPBOARD PRODUCERS                            n_cst_dwsvc_contextmenu.sru:L514-L632
    //  ----------------------------------------------------------------------------------------
    //  DECISION 3 INVERTED. The oracle WRITES the system clipboard - `Clipboard(sValString)` at :L549
    //  and `Clipboard(sVal)` at :L629 - and a headless service in a container has no clipboard to
    //  write. Both operations therefore compose their text exactly as the oracle composes it and
    //  RETURN it as data over the contract, alongside the return code the oracle produced. Nothing
    //  is dropped and nothing is invented: the composition, the per-row value selection, the
    //  check-box Y/N substitution and the row separator are all byte-identical to the oracle's.
    //
    //  The READ half of the same inversion is Domain-side: the oracle's `_sClipText = Clipboard()`
    //  at :L299 becomes ContextMenuPointerContext.ClipboardText, supplied by the caller.
    //
    //  BOTH PRODUCERS EVALUATE THROUGH Describe("Evaluate('...',row)"), and the property string is
    //  composed here verbatim rather than short-circuited to a direct evaluation, because the
    //  property text itself is observable: a characterization recording of the ported call sequence
    //  has to match the oracle's. Expressions/DataWindowExpressionEvaluator.cs unwraps the wrapper.
    // ==========================================================================================

    /// <summary>
    /// Composes the text of one whole column, one row per line - the port of
    /// <c>_of_CopyColumn2Clipboard</c> (<c>n_cst_dwsvc_contextmenu.sru:L514-L552</c>), with the
    /// clipboard write at <c>:L549</c> replaced by returning the text (DECISION 3).
    /// </summary>
    /// <param name="colName">The column to copy. The empty string is rejected [<c>:L518</c>].</param>
    /// <returns>
    /// <see cref="RetCode.OK"/> and the composed text; or
    /// <see cref="RetCode.E_INVALID_ARGUMENT"/> [<c>:L518</c>] or <see cref="RetCode.FAILED"/>
    /// [<c>:L519</c>] with a <see langword="null"/> text, because neither failing path reaches the
    /// composition.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="colName"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">No host is attached.</exception>
    /// <remarks>
    /// <para>
    /// PENDING TEXT IS ALSO PUBLISHED ON <see cref="PendingCopiedText"/> so that the dispatch at
    /// <c>:L217</c> - which discards this operation's return value entirely - still has somewhere to
    /// leave the text it produced. The oracle's dispatch can afford to discard it because the text has
    /// already reached the clipboard by then; ours cannot.
    /// </para>
    /// <para>
    /// ACCEPTTEXT RUNS FIRST AND CAN FAIL THE WHOLE OPERATION [<c>:L519</c>], because the value being
    /// copied must include an edit in progress. Its failure code is <c>-1</c> and nothing else.
    /// </para>
    /// <para>
    /// AN INVISIBLE CELL COPIES AS THE EMPTY STRING RATHER THAN BEING SKIPPED [<c>:L530-L531</c>] - the
    /// line is still emitted, so row alignment survives a hidden cell. That is what makes the output
    /// safe to paste straight back through <see cref="Paste2Column"/>.
    /// </para>
    /// <para>
    /// THREE VALUE SOURCES, CHOSEN IN THIS ORDER [<c>:L533-L544</c>]: a check-box column evaluates the
    /// column and then substitutes <c>"Y"</c> for its ON text and <c>"N"</c> for its OFF text; a
    /// computed field evaluates its own name; everything else evaluates
    /// <c>LookUpDisplay(<paramref name="colName"/>)</c> so that a code table copies as what the user
    /// sees. THE SUBSTITUTION IS NOT EXHAUSTIVE: a check-box value that matches neither the ON nor the
    /// OFF text copies verbatim, which is the oracle's behaviour for a third state.
    /// </para>
    /// <para>
    /// DEFECT TO PRESERVE - EVERY LINE ENDS WITH THE SEPARATOR, INCLUDING THE LAST [<c>:L546</c>]. The
    /// oracle appends <c>sVal + "~r~n"</c> unconditionally inside the loop rather than joining, so the
    /// composed text ALWAYS ends in CRLF and a one-row column copies as <c>"value\r\n"</c> and not
    /// <c>"value"</c>. Reproduced, not corrected (constraint C-B). It is harmless on the round trip
    /// only because the split at <c>:L952</c> drops an empty tail
    /// [<c>n_cst_dwsvc.sru:L473-L475</c>] - a fact worth knowing before anyone "tidies" either end.
    /// </para>
    /// </remarks>
    public ClipboardTextResult CopyColumnToText(in string colName)
    {
        ArgumentNullException.ThrowIfNull(colName);

        PendingCopiedText = null;

        // :L518
        if (colName.Length == 0)
        {
            return new ClipboardTextResult { Code = RetCode.E_INVALID_ARGUMENT, Text = null };
        }

        DataWindowServiceHost host = RequireHost();

        // :L519  if #DataWindow.AcceptText() = -1 then return RetCode.FAILED
        if (host.AcceptText() == -1)
        {
            return new ClipboardTextResult { Code = RetCode.FAILED, Text = null };
        }

        // :L521-L525  the check-box shape, read once for the whole column.
        bool checkBox = false;
        string valueOn = string.Empty;
        string valueOff = string.Empty;
        if (string.Equals(host.Describe(colName + ".edit.style"), CheckBoxEditStyle, StringComparison.Ordinal))
        {
            checkBox = true;
            valueOn = host.Describe(colName + ".checkbox.on");
            valueOff = host.Describe(colName + ".checkbox.off");
        }

        // :L526
        bool compute = string.Equals(
            host.Describe(colName + ".type"),
            ComputeObjectType,
            StringComparison.Ordinal);

        // :L528
        long rowCount = host.RowCount();

        StringBuilder composed = new();

        // :L529
        for (long row = 1L; row <= rowCount; row++)
        {
            string value;

            // :L530-L531
            if (!IsItemVisible(row, colName))
            {
                value = string.Empty;
            }
            else if (checkBox)
            {
                // :L534  Describe("Evaluate('"+colName+"',"+String(nRow)+")")
                value = EvaluateAtRow(colName, row);

                // :L535-L539  the two-way substitution, deliberately non-exhaustive.
                if (string.Equals(value, valueOn, StringComparison.Ordinal))
                {
                    value = "Y";
                }
                else if (string.Equals(value, valueOff, StringComparison.Ordinal))
                {
                    value = "N";
                }
            }
            else if (compute)
            {
                // :L541
                value = EvaluateAtRow(colName, row);
            }
            else
            {
                // :L543  Evaluate('LookUpDisplay(col)',row) - the DISPLAY value, not the data value.
                value = EvaluateAtRow("LookUpDisplay(" + colName + ")", row);
            }

            // :L546  sValString += sVal + "~r~n" - unconditionally, hence the trailing separator.
            _ = composed.Append(value).Append(RowSeparator);
        }

        // :L549 becomes a return rather than a clipboard write.
        string text = composed.ToString();
        PendingCopiedText = text;

        // :L551
        return new ClipboardTextResult { Code = RetCode.OK, Text = text };
    }

    /// <summary>
    /// Composes the text of one cell - the port of <c>_of_CopyItem2Clipboard</c>
    /// (<c>n_cst_dwsvc_contextmenu.sru:L602-L632</c>), with the clipboard write at <c>:L629</c>
    /// replaced by returning the text (DECISION 3).
    /// </summary>
    /// <param name="row">The ONE-BASED row. A non-positive row is rejected [<c>:L605</c>].</param>
    /// <param name="colName">The column. The empty string is rejected [<c>:L606</c>].</param>
    /// <returns>
    /// <see cref="RetCode.OK"/> and the cell's text; or
    /// <see cref="RetCode.E_INVALID_ARGUMENT"/> [<c>:L605</c>, <c>:L606</c>] or
    /// <see cref="RetCode.FAILED"/> [<c>:L607</c>] with a <see langword="null"/> text.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="colName"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">No host is attached.</exception>
    /// <remarks>
    /// <para>
    /// THE VALUE SELECTION IS THE COLUMN PRODUCER'S, ARM FOR ARM [<c>:L609-L627</c>] - the same
    /// check-box read with the same non-exhaustive Y/N substitution, the same computed-field arm and
    /// the same <c>LookUpDisplay</c> default.
    /// </para>
    /// <para>
    /// ASYMMETRY TO PRESERVE - THERE IS NO VISIBILITY TEST HERE. The column producer replaces an
    /// invisible cell with the empty string [<c>:L530-L531</c>]; this one copies whatever the
    /// expression answers for the cell the user right-clicked, visible or not. The oracle's two
    /// operations genuinely differ, and the difference is not obviously deliberate - it is reproduced
    /// rather than harmonised (constraint C-B).
    /// </para>
    /// <para>
    /// NO TRAILING SEPARATOR HERE, unlike the column producer: a single cell is emitted bare
    /// [<c>:L629</c>].
    /// </para>
    /// </remarks>
    public ClipboardTextResult CopyItemToText(long row, in string colName)
    {
        ArgumentNullException.ThrowIfNull(colName);

        PendingCopiedText = null;

        // :L605
        if (row <= 0L)
        {
            return new ClipboardTextResult { Code = RetCode.E_INVALID_ARGUMENT, Text = null };
        }

        // :L606
        if (colName.Length == 0)
        {
            return new ClipboardTextResult { Code = RetCode.E_INVALID_ARGUMENT, Text = null };
        }

        DataWindowServiceHost host = RequireHost();

        // :L607
        if (host.AcceptText() == -1)
        {
            return new ClipboardTextResult { Code = RetCode.FAILED, Text = null };
        }

        // :L609-L613
        bool checkBox = false;
        string valueOn = string.Empty;
        string valueOff = string.Empty;
        if (string.Equals(host.Describe(colName + ".edit.style"), CheckBoxEditStyle, StringComparison.Ordinal))
        {
            checkBox = true;
            valueOn = host.Describe(colName + ".checkbox.on");
            valueOff = host.Describe(colName + ".checkbox.off");
        }

        // :L614
        bool compute = string.Equals(
            host.Describe(colName + ".type"),
            ComputeObjectType,
            StringComparison.Ordinal);

        string value;

        if (checkBox)
        {
            // :L617
            value = EvaluateAtRow(colName, row);

            // :L618-L622
            if (string.Equals(value, valueOn, StringComparison.Ordinal))
            {
                value = "Y";
            }
            else if (string.Equals(value, valueOff, StringComparison.Ordinal))
            {
                value = "N";
            }
        }
        else if (compute)
        {
            // :L624
            value = EvaluateAtRow(colName, row);
        }
        else
        {
            // :L626
            value = EvaluateAtRow("LookUpDisplay(" + colName + ")", row);
        }

        // :L629 becomes a return rather than a clipboard write.
        PendingCopiedText = value;

        // :L631
        return new ClipboardTextResult { Code = RetCode.OK, Text = value };
    }


    // ==========================================================================================
    //  PRIVATE - THE TYPED ITEM ARMS                          n_cst_dwsvc_contextmenu.sru:L786-L930
    //  ----------------------------------------------------------------------------------------
    //  TWO MECHANISMS FOR ONE JOB, AND THAT DUPLICATION IS DEFECT 7. `_of_CheckColumn` [:L779] and
    //  `_of_RevertCheckColumn` [:L835] key their arms on `_of_GetColumnType`'s COL_TYPE_* code, while
    //  `_of_UncheckColumn` [:L900] keys its arms on the FIRST FIVE CHARACTERS of the raw `.ColType`
    //  string. The two disagree observably - `_of_GetColumnType` folds `char(n)` onto the string arm
    //  and `number` onto the integer arm through one shared classifier, whereas the prefix form names
    //  only "decim"/"real" and "numbe"/"long"/"ulong" and sends EVERYTHING else, `char` included, to
    //  the string arm. Both are reproduced, each keyed the way its own operation keys it, and neither
    //  is unified onto the other (constraint C-B).
    // ==========================================================================================

    /// <summary>
    /// Reads one item as text, with the arm chosen by <c>COL_TYPE_*</c> - the port of the read
    /// <c>choose case</c> at <c>n_cst_dwsvc_contextmenu.sru:L786-L793</c> and <c>:L842-L861</c>.
    /// </summary>
    /// <param name="host">The attached host.</param>
    /// <param name="row">The ONE-BASED row.</param>
    /// <param name="colName">The column.</param>
    /// <param name="colType">The <c>COL_TYPE_*</c> code from <c>GetColumnType</c>.</param>
    /// <returns>The item as text, or <see langword="null"/> when the item is null.</returns>
    /// <remarks>
    /// THE COMPARISON THE CALLERS MAKE IS ON TEXT IN EVERY ARM, including both numeric ones, which the
    /// oracle stringifies with <c>String(...)</c> before comparing against a check box's ON or OFF text
    /// [<c>:L788</c>, <c>:L790</c>]. Stringification is invariant-culture here for the same reason the
    /// numeric conversions are: the check-box texts are DataWindow definition literals, not localized
    /// display text, so a culture-sensitive read would compare <c>"1,5"</c> against <c>"1.5"</c> on a
    /// comma-decimal machine and answer "not equal" for a row that is equal.
    /// </remarks>
    private static string? ReadItemByColumnType(
        DataWindowServiceHost host,
        long row,
        in string colName,
        long colType)
    {
        switch (colType)
        {
            case COL_TYPE_DECIMAL:
                // :L788  String(#DataWindow.GetItemDecimal(nRow,colName))
                return host.GetItemDecimal(row, colName)?.ToString(CultureInfo.InvariantCulture);

            case COL_TYPE_INTEGER:
                // :L790  String(#DataWindow.GetItemNumber(nRow,colName)) - a double, despite the arm.
                return host.GetItemNumber(row, colName)?.ToString(CultureInfo.InvariantCulture);

            default:
                // :L792  case else - and it really is every other type, string, date and time alike.
                return host.GetItemString(row, colName);
        }
    }

    /// <summary>
    /// Writes one item from text, with the arm chosen by <c>COL_TYPE_*</c> - the port of the write
    /// <c>choose case</c> at <c>n_cst_dwsvc_contextmenu.sru:L802-L809</c> and <c>:L870-L877</c>.
    /// </summary>
    /// <param name="host">The attached host.</param>
    /// <param name="row">The ONE-BASED row.</param>
    /// <param name="colName">The column.</param>
    /// <param name="colType">The <c>COL_TYPE_*</c> code from <c>GetColumnType</c>.</param>
    /// <param name="value">The text to write.</param>
    /// <remarks>
    /// THE WRITE'S ARMS MIRROR THE READ'S EXACTLY, which matters because a mismatch between them would
    /// make a row that compares equal on read write a different value - the row would flip on every
    /// invocation. The <c>SetItem</c> answer is discarded exactly as the oracle discards it.
    /// </remarks>
    private static void WriteItemByColumnType(
        DataWindowServiceHost host,
        long row,
        in string colName,
        long colType,
        string? value)
    {
        switch (colType)
        {
            case COL_TYPE_DECIMAL:
                // :L804  SetItem(nRow,colName,Dec(sVal))
                _ = host.SetItem(row, colName, ParseDecimal(value));
                break;

            case COL_TYPE_INTEGER:
                // :L806  SetItem(nRow,colName,Long(sVal))
                _ = host.SetItem(row, colName, ParseLong(value));
                break;

            default:
                // :L808  SetItem(nRow,colName,sVal)
                _ = host.SetItem(row, colName, value);
                break;
        }
    }

    /// <summary>
    /// The port of <c>Left(#DataWindow.Describe(colName+".ColType"),5)</c>
    /// (<c>n_cst_dwsvc_contextmenu.sru:L900</c>) - the raw column type truncated to five characters.
    /// </summary>
    /// <param name="colType">The raw <c>.ColType</c> answer.</param>
    /// <returns>Its first five characters, or the whole string when it is shorter.</returns>
    /// <remarks>
    /// FIVE IS NOT ARBITRARY: it is the shortest prefix that separates <c>decimal(n)</c> from
    /// <c>date</c> and <c>datetime</c> - <c>"decim"</c> versus <c>"date"</c> - while keeping
    /// <c>char(n)</c> distinguishable as <c>"char("</c>. It is also why <c>ulong</c> appears in the arm
    /// list as a WHOLE word while <c>number</c> appears truncated to <c>"numbe"</c>: PowerScript
    /// compares the truncated subject against each literal, so a five-character subject can only ever
    /// equal a literal of five characters or fewer.
    /// </remarks>
    private static string TruncateColumnType(string colType)
    {
        ArgumentNullException.ThrowIfNull(colType);

        return colType.Length <= 5 ? colType : colType[..5];
    }

    /// <summary>
    /// Reads one item as text, with the arm chosen by the TRUNCATED RAW COLUMN TYPE - the port of the
    /// read <c>choose case</c> at <c>n_cst_dwsvc_contextmenu.sru:L907-L914</c>. DEFECT 7's second
    /// mechanism.
    /// </summary>
    /// <param name="host">The attached host.</param>
    /// <param name="row">The ONE-BASED row.</param>
    /// <param name="colName">The column.</param>
    /// <param name="colTypePrefix">The five-character prefix from <see cref="TruncateColumnType"/>.</param>
    /// <returns>The item as text, or <see langword="null"/> when the item is null.</returns>
    /// <remarks>
    /// THE ARM LISTS ARE THE ORACLE'S, VERBATIM AND UNEXTENDED [<c>:L908</c>, <c>:L910</c>]:
    /// <c>"decim"</c> and <c>"real"</c> read a decimal, <c>"numbe"</c>, <c>"long"</c> and
    /// <c>"ulong"</c> read a number, and everything else - including <c>"char("</c>, <c>"date"</c>,
    /// <c>"datet"</c>, <c>"time"</c> and <c>"strin"</c> - reads a string. Note what is MISSING against
    /// the shared classifier: <c>"int"</c> and <c>"uint"</c> are absent, so an <c>int</c> column
    /// un-checks through the STRING arm here while it checks through the INTEGER arm in
    /// <see cref="ReadItemByColumnType"/>. That divergence is the observable face of DEFECT 7.
    /// </remarks>
    private static string? ReadItemByColumnTypePrefix(
        DataWindowServiceHost host,
        long row,
        in string colName,
        string colTypePrefix)
    {
        // :L908  case "decim","real"
        if (string.Equals(colTypePrefix, "decim", StringComparison.Ordinal)
            || string.Equals(colTypePrefix, "real", StringComparison.Ordinal))
        {
            // :L909
            return host.GetItemDecimal(row, colName)?.ToString(CultureInfo.InvariantCulture);
        }

        // :L910  case "numbe","long","ulong"
        if (string.Equals(colTypePrefix, "numbe", StringComparison.Ordinal)
            || string.Equals(colTypePrefix, "long", StringComparison.Ordinal)
            || string.Equals(colTypePrefix, "ulong", StringComparison.Ordinal))
        {
            // :L911
            return host.GetItemNumber(row, colName)?.ToString(CultureInfo.InvariantCulture);
        }

        // :L913  case else
        return host.GetItemString(row, colName);
    }

    /// <summary>
    /// Writes one item from text, with the arm chosen by the TRUNCATED RAW COLUMN TYPE - the port of
    /// the write <c>choose case</c> at <c>n_cst_dwsvc_contextmenu.sru:L923-L930</c>. DEFECT 7's second
    /// mechanism.
    /// </summary>
    /// <param name="host">The attached host.</param>
    /// <param name="row">The ONE-BASED row.</param>
    /// <param name="colName">The column.</param>
    /// <param name="colTypePrefix">The five-character prefix from <see cref="TruncateColumnType"/>.</param>
    /// <param name="value">The text to write.</param>
    /// <remarks>
    /// THE ARM LISTS ARE IDENTICAL TO THE READ'S, and are kept as two separate switches rather than
    /// hoisted into one classifier precisely so that a future edit to one cannot silently change the
    /// other - which is how the oracle's own two mechanisms drifted apart in the first place.
    /// </remarks>
    private static void WriteItemByColumnTypePrefix(
        DataWindowServiceHost host,
        long row,
        in string colName,
        string colTypePrefix,
        string? value)
    {
        // :L924  case "decim","real"
        if (string.Equals(colTypePrefix, "decim", StringComparison.Ordinal)
            || string.Equals(colTypePrefix, "real", StringComparison.Ordinal))
        {
            // :L925
            _ = host.SetItem(row, colName, ParseDecimal(value));
            return;
        }

        // :L926  case "numbe","long","ulong"
        if (string.Equals(colTypePrefix, "numbe", StringComparison.Ordinal)
            || string.Equals(colTypePrefix, "long", StringComparison.Ordinal)
            || string.Equals(colTypePrefix, "ulong", StringComparison.Ordinal))
        {
            // :L927
            _ = host.SetItem(row, colName, ParseLong(value));
            return;
        }

        // :L929  case else
        _ = host.SetItem(row, colName, value);
    }

    // ==========================================================================================
    //  PRIVATE - POWERSCRIPT SEMANTICS PORTED, NOT APPROXIMATED
    //  ----------------------------------------------------------------------------------------
    //  Four families live here, and each exists because a PowerScript construct has no C# operator
    //  that behaves the same way:
    //
    //    1. THE SENTINEL PAIR. `Describe` answers "!" for an invalid property and "?" for one whose
    //       value cannot be determined, and the oracle tests for both as a pair at :L776, :L830,
    //       :L832, :L897, :L968, :L974 and inside the width pass. One predicate, one meaning.
    //
    //    2. THE CONVERSIONS. `Dec`, `Long`, `Date`, `Time` and `DateTime` are TOTAL functions that
    //       answer null - not an exception - for text that does not convert. They are single-sourced
    //       through Validators/ so that a value pasted into a column and a value typed into it
    //       coerce identically; drift between those two would be a parity defect no unit test of
    //       either file alone would catch.
    //
    //       ON THE IMPORT ITSELF, STATED SO IT IS NOT MISTAKEN FOR AN UNVETTED DEPENDENCY. Validators/
    //       is INSIDE this project and this assembly - the same compilation unit, not a new package,
    //       project reference or service edge - and the five validators are named as in-scope files of
    //       this same service by the plan, which also records that the item-change coercion switch is
    //       aligned to them. Each member consumed here was checked to exist with the signature used:
    //       DateValidator.TryCoerce/Coerce, TimeValidator.TryCoerce/Coerce, DateTimeValidator.Coerce
    //       and NumberValidator.CoerceToDecimal/CoerceToLong. The alternative - hand-rolling five
    //       conversions locally - would duplicate behaviour that is REQUIRED to match, so the
    //       reference is the choice that preserves behaviour rather than the one that risks it.
    //
    //    3. THE THREE-VALUED COMPARISON. `a = b` with a null operand is NULL, and an `if` does not
    //       take a null branch, so every skip test that could see a null goes through PbEquals.
    //
    //    4. THE ONE-BASED ARRAY READ. `sVals[nRow]` (:L1004) indexes from 1. Routed through one
    //       helper for the same reason the item store's four accessors are (AAP 0.4.5.4 / R9).
    // ==========================================================================================

    /// <summary>
    /// The uninitialised PowerScript <c>datetime</c> - <c>1900-01-01 00:00:00</c>, the value of the
    /// <c>dttEmpty</c> local declared at <c>n_cst_dwsvc_contextmenu.sru:L945</c> and never assigned.
    /// </summary>
    /// <remarks>
    /// IT DOUBLES AS THE FAILED-CONVERSION ANSWER OF <c>DateTime(string)</c>, which is precisely why
    /// the datetime paste arm cannot tell an unconvertible value from this instant - see
    /// <see cref="Paste2Column"/>'s remarks.
    /// </remarks>
    private static readonly DateTime DateTimeEmpty = new(1900, 1, 1, 0, 0, 0);

    /// <summary>
    /// The two <c>Describe</c> sentinels, tested as the pair the oracle always tests them as.
    /// </summary>
    /// <param name="answer">A <c>Describe</c> answer.</param>
    /// <returns>
    /// <see langword="true"/> when the answer is <c>"!"</c> (invalid property) or <c>"?"</c>
    /// (indeterminate value).
    /// </returns>
    /// <remarks>
    /// THE PAIR IS NEVER SPLIT. Every oracle site tests both - <c>if sVal = "!" or sVal = "?"</c> - and
    /// treats them identically, so this file has no member that distinguishes them. That is a genuine
    /// property of the oracle rather than a simplification: an invalid property and an indeterminate
    /// value are equally unusable to a caller that wanted a value.
    /// </remarks>
    private static bool IsUnreadableAnswer(string? answer)
    {
        return string.Equals(answer, InvalidExpressionSentinel, StringComparison.Ordinal)
            || string.Equals(answer, UndeterminedValueSentinel, StringComparison.Ordinal);
    }

    /// <summary>
    /// The port of <c>sVal &lt;&gt; ""</c> against a value that may be null.
    /// </summary>
    /// <param name="value">The value to test.</param>
    /// <returns><see langword="true"/> only when the value is non-null AND non-empty.</returns>
    /// <remarks>
    /// A NULL ANSWERS FALSE, WHICH IS THE POINT. In PowerScript <c>null &lt;&gt; ""</c> is NULL, and an
    /// <c>if</c> whose condition is null does not run its body - so every validation guard the paste
    /// path writes as <c>if sVal &lt;&gt; "" and Not IsSomething(sVal)</c> is SKIPPED for a null value
    /// rather than tripped by it. Reading that as "null is empty, so the guard fires" would reject
    /// values the oracle accepts; reading it as "null is text, so validate it" would reject them too.
    /// </remarks>
    private static bool HasText(string? value)
    {
        return value is not null && value.Length != 0;
    }

    /// <summary>
    /// Resolves a column name to its DataWindow object, treating an unresolvable name as a structural
    /// fault - the port of <c>dwo = _of_GetDWObject(colName)</c> (<c>:L778</c>, <c>:L834</c>,
    /// <c>:L899</c>, <c>:L955</c>).
    /// </summary>
    /// <param name="colName">The column name.</param>
    /// <returns>The resolved object, never <see langword="null"/>.</returns>
    /// <exception cref="InvalidOperationException">The host cannot resolve the name.</exception>
    /// <remarks>
    /// <para>
    /// FAIL FAST RATHER THAN CARRY A NULL (AAP 0.1.4). PowerScript assigns an INVALID <c>dwobject</c>
    /// here and carries it into the item-change event, where the first member access raises a runtime
    /// error - so an unresolvable column is already fatal in the oracle, just later and less legibly.
    /// The four call sites all guard something else first that makes this unreachable in practice: the
    /// three check-box operations require a readable <c>.checkbox.on</c> or <c>.checkbox.off</c>
    /// [<c>:L776</c>, <c>:L830</c>, <c>:L897</c>], and the paste requires a known column type
    /// [<c>:L957</c>]. It fires only for a host whose <c>Describe</c> answers for a column its object
    /// model does not publish, which is a mis-wired host and not a data condition.
    /// </para>
    /// <para>
    /// IT IS NOT A RETURN CODE, deliberately. Every code this file returns corresponds to one the
    /// oracle returns; inventing a new one for a fault the oracle expresses as a crash would widen the
    /// contract with a guess (AAP 0.1.5).
    /// </para>
    /// </remarks>
    private IDataWindowObject RequireColumnObject(in string colName)
    {
        return GetDataWindowObject(colName) ?? throw new InvalidOperationException(
            "The attached host cannot resolve the column '" + colName + "' to a DataWindow object. "
            + "The context-menu service reaches this only after the column has answered a readable "
            + "Describe, so the host's object model and its Describe answers disagree "
            + "(ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_contextmenu.sru:L778).");
    }

    /// <summary>
    /// The ONE-BASED read of the split clipboard lines - the port of <c>sVals[nRow]</c>
    /// (<c>n_cst_dwsvc_contextmenu.sru:L1004</c>).
    /// </summary>
    /// <param name="values">The split lines, zero-based as every C# array is.</param>
    /// <param name="oneBasedRow">The oracle's <c>nRow</c>, counting from 1.</param>
    /// <returns>The line for that row, or the empty string when the row is past the last line.</returns>
    /// <remarks>
    /// <para>
    /// THE FIFTH AND LAST INDEX TRANSLATION IN THIS FILE, alongside the item store's four accessors.
    /// Every other loop here walks rows or reads through those helpers, so no bare <c>- 1</c> appears
    /// anywhere else - which is the whole point of centralising it (AAP 0.4.5.4, risk R9).
    /// </para>
    /// <para>
    /// THE OUT-OF-RANGE ANSWER IS THE EMPTY STRING AND NOT AN EXCEPTION. The paste loop is bounded by
    /// the split count, so it cannot ask for a row past the last line; the guard exists because
    /// PowerScript's unbounded array answers an EMPTY value for an index past its upper bound rather
    /// than raising, and a port that raised would turn an unreachable branch into a crash if a future
    /// caller ever changed the bound.
    /// </para>
    /// </remarks>
    private static string PastedValueAt(string[] values, long oneBasedRow)
    {
        ArgumentNullException.ThrowIfNull(values);

        if (oneBasedRow < 1L || oneBasedRow > values.Length)
        {
            return string.Empty;
        }

        return values[(int)(oneBasedRow - 1L)];
    }

    /// <summary>
    /// The port of PowerScript's <c>Dec(string)</c> - a total conversion answering
    /// <see langword="null"/> for text that is not a number.
    /// </summary>
    /// <param name="text">The text to convert.</param>
    /// <returns>The value, or <see langword="null"/> when the text does not convert.</returns>
    /// <remarks>
    /// SINGLE-SOURCED THROUGH <c>Validators/NumberValidator.cs</c> for the same reason
    /// <see cref="ParseLong"/> is: the decimal a user PASTES and the decimal a user TYPES must coerce
    /// identically, and that is only guaranteed while both go through one implementation that pins the
    /// culture and the permitted styles.
    /// </remarks>
    private static decimal? ParseDecimal(string? text)
    {
        return NumberValidator.CoerceToDecimal(text);
    }

    /// <summary>
    /// The port of PowerScript's <c>IsNumber(string)</c> (<c>:L1017</c>, <c>:L1026</c>).
    /// </summary>
    /// <param name="text">The text to test.</param>
    /// <returns><see langword="true"/> when the text converts to a number.</returns>
    /// <remarks>
    /// DEFINED AS "THE CONVERSION SUCCEEDS", which is what PowerScript guarantees: <c>IsNumber</c> and
    /// <c>Dec</c> share one grammar there, so a value that passes the test always converts and a value
    /// that fails it never does. Deriving the predicate from the conversion rather than writing a second
    /// grammar is what keeps that guarantee true here - two independent implementations would eventually
    /// disagree on some edge, and the paste path would then validate a value it cannot write.
    /// </remarks>
    private static bool IsNumber(string? text)
    {
        return ParseDecimal(text) is not null;
    }

    /// <summary>
    /// The port of PowerScript's <c>IsDate(string)</c> (<c>:L1038</c>).
    /// </summary>
    /// <param name="text">The text to test.</param>
    /// <returns><see langword="true"/> when the text converts to a date.</returns>
    /// <remarks>
    /// <para>
    /// THE REPORTING COERCION AND NOT THE PLAIN ONE, and the distinction is not cosmetic. PowerScript
    /// draws the same line the validators do: <c>Date(string)</c> is a total CONVERSION that answers a
    /// fallback for text it cannot read, while <c>IsDate(string)</c> is a PREDICATE that answers false -
    /// and the oracle uses both, the predicate to validate [<c>:L1038</c>] and the conversion to write
    /// [<c>:L1069</c>]. Deriving the predicate from the conversion would make it answer true for
    /// everything the conversion tolerates, which for the time validator is literally every string.
    /// </para>
    /// <para>
    /// The accepted grammar is <c>Validators/DateValidator.cs</c>'s, which is this service's single
    /// answer to "what text is a date" and is invariant-culture by decision there rather than by accident
    /// here.
    /// </para>
    /// </remarks>
    private static bool IsDateText(string? text)
    {
        return DateValidator.TryCoerce(text, out _) == RetCode.OK;
    }

    /// <summary>
    /// The port of PowerScript's <c>Date(string)</c> (<c>:L1042</c>, <c>:L1069</c>).
    /// </summary>
    /// <param name="text">The text to convert.</param>
    /// <returns>The value, or <see langword="null"/> when the text does not convert.</returns>
    private static DateOnly? ParseDate(string? text)
    {
        return DateValidator.Coerce(text);
    }

    /// <summary>
    /// The port of PowerScript's <c>IsTime(string)</c> (<c>:L1044</c>).
    /// </summary>
    /// <param name="text">The text to test.</param>
    /// <returns><see langword="true"/> when the text converts to a time.</returns>
    /// <remarks>
    /// THE REPORTING COERCION FOR THE REASON <see cref="IsDateText"/> USES IT, and here the reason is
    /// decisive rather than merely correct: <c>TimeValidator.Coerce</c> answers MIDNIGHT for text it
    /// cannot read, so a predicate derived from it would accept every string and the type-mismatch arm at
    /// <c>:L1044</c> would be unreachable.
    /// </remarks>
    private static bool IsTimeText(string? text)
    {
        return TimeValidator.TryCoerce(text, out _) == RetCode.OK;
    }

    /// <summary>
    /// The port of PowerScript's <c>Time(string)</c> (<c>:L1048</c>, <c>:L1071</c>).
    /// </summary>
    /// <param name="text">The text to convert.</param>
    /// <returns>
    /// The value; MIDNIGHT - not null - for text that does not convert, which is what PowerScript's
    /// <c>Time</c> answers.
    /// </returns>
    /// <remarks>
    /// THE FALLBACK IS UNOBSERVABLE ON THIS FILE'S PATHS, because every call is guarded by
    /// <see cref="IsTimeText"/> first [<c>:L1044</c>], so unconvertible text never reaches the
    /// conversion. It is documented rather than "fixed" because the validator's posture is the oracle's
    /// and the guard is what makes it safe.
    /// </remarks>
    private static TimeOnly? ParseTime(string? text)
    {
        return TimeValidator.Coerce(text);
    }

    /// <summary>
    /// The port of PowerScript's <c>DateTime(string)</c> as the paste path's WRITE uses it
    /// (<c>:L1036</c>, <c>:L1067</c>) - null in, null out, and null for text that does not convert.
    /// </summary>
    /// <param name="text">The text to convert.</param>
    /// <returns>The value, or <see langword="null"/> when the text does not convert.</returns>
    /// <remarks>
    /// THE WRITE AND THE VALIDATION USE THE SAME CONVERSION THROUGH DIFFERENT FACES. This one answers
    /// null for unconvertible text, which is what the write wants - a null item.
    /// <see cref="ParseDateTimeLikePowerScript"/> answers <see cref="DateTimeEmpty"/> for the same
    /// input, which is what the VALIDATION compares against. Keeping them as two members makes the
    /// oracle's conflation of "failed" with "1900-01-01 00:00:00" visible instead of hiding it inside
    /// one ambiguous return.
    /// </remarks>
    private static DateTime? ParseDateTime(string? text)
    {
        return DateTimeValidator.Coerce(text);
    }

    /// <summary>
    /// The port of PowerScript's <c>DateTime(string)</c> as the paste path's VALIDATION uses it
    /// (<c>:L1032</c>) - answering <see cref="DateTimeEmpty"/> rather than null for text that does not
    /// convert.
    /// </summary>
    /// <param name="text">The text to convert. Never null at the one call site.</param>
    /// <returns>The value, or <see cref="DateTimeEmpty"/> when the text does not convert.</returns>
    /// <remarks>
    /// THIS IS THE DEFECT'S MECHANISM, ISOLATED IN ONE MEMBER. PowerScript's <c>DateTime</c> answers
    /// <c>1900-01-01 00:00:00</c> for unconvertible text, and the oracle compares that answer against
    /// an uninitialised <c>datetime</c> local holding the very same instant - so the test means "did
    /// not convert OR converted to exactly 1900-01-01 00:00:00". Reproduced verbatim (constraint C-B).
    /// </remarks>
    private static DateTime ParseDateTimeLikePowerScript(string? text)
    {
        return ParseDateTime(text) ?? DateTimeEmpty;
    }

    /// <summary>
    /// The port of PowerScript's <c>String(any)</c> as applied to a value read out of the column value
    /// map (<c>n_cst_dwsvc_contextmenu.sru:L1007</c>).
    /// </summary>
    /// <param name="value">The mapped value.</param>
    /// <returns>Its text, or <see langword="null"/> when the value is null.</returns>
    /// <remarks>
    /// <para>
    /// EVERY PATH THAT BUILDS THE MAP STORES TEXT - the code-table walk and the drop-down DataWindow
    /// walk both read their data values as strings [<c>n_cst_dwsvc.sru</c>'s
    /// <c>GetColumnValueMap</c>] - so the string arm is the one that runs. The remaining arms exist
    /// because the map's value type is <c>object?</c> and a host is free to populate it otherwise;
    /// they answer what <c>String()</c> answers rather than what <c>ToString()</c> happens to.
    /// </para>
    /// <para>
    /// A NULL MAPPED VALUE STAYS NULL rather than becoming the empty string, and the paste path then
    /// carries that null all the way to the write - which is correct: a code table that maps a display
    /// text onto a null data value is asking for a null to be stored.
    /// </para>
    /// </remarks>
    private static string? ToText(object? value)
    {
        return value switch
        {
            null => null,
            string text => text,

            // PowerScript's String(boolean) is lower-case "true"/"false", not .NET's "True"/"False".
            bool flag => flag ? "true" : "false",

            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString()
        };
    }

    /// <summary>
    /// PowerScript's three-valued text comparison: false whenever either side is null.
    /// </summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> only when both are non-null and equal.</returns>
    /// <remarks>
    /// <para>
    /// C# <c>==</c> ON TWO NULLS ANSWERS TRUE AND POWERSCRIPT'S ANSWERS NULL. The difference is
    /// observable at every skip test in <see cref="Paste2Column"/>: a null item and a null pasted value
    /// would make C# skip the row, while the oracle falls through and WRITES it - raising the
    /// item-change event, marking the row modified and firing the after-change event. Answering false
    /// for a null operand reproduces the fall-through.
    /// </para>
    /// <para>
    /// THIS IS NOT THE SAME RULE AS THE ITEM-CHANGE CHAIN'S. Domain/ItemChangeProtocol.cs's equality
    /// test has an EXPLICIT null-and-null arm [<c>se_cst_dw.sru:L198-L202</c>] that makes two nulls
    /// EQUAL, because the oracle wrote one there. No such arm exists on this file's paths, so the raw
    /// three-valued rule applies here. Two files, two rules, both faithful.
    /// </para>
    /// </remarks>
    private static bool PbEquals(string? left, string? right)
    {
        return left is not null && right is not null && string.Equals(left, right, StringComparison.Ordinal);
    }

    /// <summary>PowerScript's three-valued decimal comparison. See <see cref="PbEquals(string?, string?)"/>.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> only when both have a value and the values are equal.</returns>
    private static bool PbEquals(decimal? left, decimal? right)
    {
        return left.HasValue && right.HasValue && left.Value == right.Value;
    }

    /// <summary>
    /// PowerScript's three-valued numeric comparison across the <c>number</c> / <c>long</c> boundary -
    /// the port of <c>#DataWindow.GetItemNumber(nRow,colName) = Long(sVal)</c> (<c>:L1021</c>).
    /// </summary>
    /// <param name="left">The item's value, read as the <c>number</c> the DataWindow answers.</param>
    /// <param name="right">The converted pasted value, a <c>long</c>.</param>
    /// <returns><see langword="true"/> only when both have a value and the values are equal.</returns>
    /// <remarks>
    /// THE COMPARISON WIDENS TO THE DOUBLE, not the other way round, because that is the direction
    /// PowerScript promotes in. Narrowing the double to a long first would make <c>1.5</c> equal
    /// <c>Long("1")</c> and skip a row the oracle writes.
    /// </remarks>
    private static bool PbEquals(double? left, long? right)
    {
        return left.HasValue && right.HasValue && left.Value == right.Value;
    }

    /// <summary>PowerScript's three-valued datetime comparison. See <see cref="PbEquals(string?, string?)"/>.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> only when both have a value and the values are equal.</returns>
    private static bool PbEquals(DateTime? left, DateTime? right)
    {
        return left.HasValue && right.HasValue && left.Value == right.Value;
    }

    /// <summary>PowerScript's three-valued date comparison. See <see cref="PbEquals(string?, string?)"/>.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> only when both have a value and the values are equal.</returns>
    private static bool PbEquals(DateOnly? left, DateOnly? right)
    {
        return left.HasValue && right.HasValue && left.Value == right.Value;
    }

    /// <summary>PowerScript's three-valued time comparison. See <see cref="PbEquals(string?, string?)"/>.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> only when both have a value and the values are equal.</returns>
    private static bool PbEquals(TimeOnly? left, TimeOnly? right)
    {
        return left.HasValue && right.HasValue && left.Value == right.Value;
    }

    /// <summary>
    /// Evaluates an expression at one row through the DataWindow's own
    /// <c>Describe("Evaluate('...',row)")</c> form.
    /// </summary>
    /// <param name="expression">The expression text, WITHOUT the wrapper.</param>
    /// <param name="row">The ONE-BASED row to evaluate at.</param>
    /// <returns>
    /// The evaluated text, or one of the evaluator's sentinels - <c>"!"</c> for an invalid expression,
    /// <c>"?"</c> for an indeterminate value, or the empty string for an abort.
    /// </returns>
    /// <remarks>
    /// <para>
    /// THE WRAPPER IS COMPOSED HERE AND NOT SHORT-CIRCUITED, because the property string the oracle
    /// hands to <c>Describe</c> is itself observable - a recording of the ported call sequence has to
    /// match the oracle's, and Expressions/DataWindowExpressionEvaluator.cs is built to unwrap exactly
    /// this shape [<c>TryUnwrapEvaluateProperty</c>]. The row is formatted invariantly, matching
    /// PowerScript's <c>String(long)</c>.
    /// </para>
    /// <para>
    /// THE ORACLE SPELLS THE CONVERSION BOTH WAYS - <c>String(nRow)</c> at <c>:L534</c> and
    /// <c>:L541</c>, <c>string(nRow)</c> at <c>:L543</c>. PowerScript is case-insensitive, so the two
    /// are one function and the difference is cosmetic; it is recorded because a reader comparing the
    /// two files will otherwise wonder which one this port followed.
    /// </para>
    /// </remarks>
    private string EvaluateAtRow(string expression, long row)
    {
        return _evaluator.Describe(
            "Evaluate('" + expression + "'," + row.ToString(CultureInfo.InvariantCulture) + ")");
    }


    // ==========================================================================================
    //  PRIVATE - THE TEN DIALOGS, CONVERTED         n_cst_dwsvc_contextmenu.sru:L795, L863, L916,
    //                                                      L1009, L1018, L1027, L1033, L1039,
    //                                                      L1045, L1052
    //  ----------------------------------------------------------------------------------------
    //  DECISION 7. TEN live MessageBoxEx sites, every one of them `StopSign!`, every one of them
    //  routed through I18N(ne_cst_i18n.CAT_DWSVC, ...). AAP 0.2.1.3 Correction 5 counts six for this
    //  object; the count was re-measured against the oracle and it is TEN. They collapse onto THREE
    //  distinct message shapes and FOUR message keys:
    //
    //    CHANGE REJECTED   :L795, :L863, :L916, :L1052   prefix + "\n" + 修改数据被拒绝 + "!"
    //    INVALID VALUE     :L1009                        prefix + "\n" + 无效的值      + "!\n" + value
    //    TYPE MISMATCH     :L1018, :L1027, :L1033,       prefix + "\n" + 数据类型不匹配 + "!\n" + value
    //                      :L1039, :L1045
    //
    //  ONLY THE DELIVERY CHANNEL CHANGES (AAP 0.3.4). Each error carries the composed text, both
    //  message KEYS verbatim as the oracle's own source strings, the localization category, the
    //  Sprintf arguments as DATA rather than only pre-rendered, the severity, and the locator of the
    //  site that produced it. Nothing is thrown for them and nothing is logged-and-swallowed: the
    //  operation returns its return code and the caller surfaces PendingError.
    //
    //  THESE TEN DO LOCALIZE - the exact opposite of Expressions/ParseErrorFormatter.cs, whose 28
    //  column-expression messages are hardcoded Chinese that bypass I18n. AAP 0.2.1.3 Correction 5
    //  requires that inconsistency be reproduced on BOTH sides, so these are NOT routed through that
    //  formatter and that formatter is NOT reused here.
    // ==========================================================================================

    /// <summary>
    /// Composes the row-number fragment every one of the ten messages opens with - the port of
    /// <c>Sprintf(I18N(ne_cst_i18n.CAT_DWSVC,"第{}行"),nRow)</c>.
    /// </summary>
    /// <param name="row">The ONE-BASED row the message names.</param>
    /// <returns>The formatted, translated fragment.</returns>
    /// <remarks>
    /// <para>
    /// TRANSLATE FIRST, THEN FORMAT. <c>Sprintf</c> is applied to the LOOKUP RESULT and never to the
    /// key, so a provider that re-words or re-orders the fragment still gets its placeholder filled.
    /// The form is the SEQUENTIAL EMPTY-BRACE one - <c>{}</c> takes the next argument - which is the
    /// same single <c>Formatting.Sprintf</c> method this file calls with the INDEXED <c>{1}</c> form for
    /// the three distinct-value predicates. One formatter, two grammars, no second implementation.
    /// </para>
    /// <para>
    /// THE LOOKUP CANNOT FAIL AND CANNOT THROW. With no provider installed <c>I18n</c> answers the text
    /// unchanged - the silent-passthrough fallback of <c>i18n.srf</c> - so the composed message is the
    /// oracle's Chinese source text, which is exactly what the oracle itself produces in that situation.
    /// </para>
    /// </remarks>
    private string ComposeRowFragment(long row)
    {
        string? template = _i18n.I18N(Categories.CAT_DWSVC, RowNumberMessageKey);

        return Formatting.Sprintf(template, row);
    }

    /// <summary>
    /// Builds the structured replacement for the change-refused dialog raised at <c>:L795</c>,
    /// <c>:L863</c>, <c>:L916</c> and <c>:L1052</c>.
    /// </summary>
    /// <param name="row">The row whose change was refused.</param>
    /// <param name="sourceLocator">
    /// The oracle locator of the site that produced it, so a caller can tell the four apart. The four
    /// sites compose an IDENTICAL message, which is why the locator is carried as data rather than
    /// inferred from the text.
    /// </param>
    /// <returns>The error, ready to publish on <see cref="PendingError"/>.</returns>
    private ContextMenuError BuildChangeRejectedError(long row, string sourceLocator)
    {
        // first operand - the row fragment.
        string rowFragment = ComposeRowFragment(row);

        // third operand - translated, carries no placeholder.
        string? detail = _i18n.I18N(Categories.CAT_DWSVC, ChangeRejectedMessageKey);

        // the concatenation, in the oracle's operand order. A null lookup contributes the empty string,
        // which is how PowerScript concatenates a null too.
        string text = rowFragment + LineSeparator + detail + DetailSuffix;

        return new ContextMenuError
        {
            Text = text,
            LocalizationCategory = Categories.CAT_DWSVC,
            RowFormatTemplate = RowNumberMessageKey,
            DetailMessageKey = ChangeRejectedMessageKey,
            Row = row,

            // THE SHAPE CARRIES NO OFFENDING VALUE - the oracle's message for this fault ends at the
            // exclamation mark. Null here means "the message has no value operand", not "the value was
            // null".
            OffendingValue = null,
            Kind = ContextMenuErrorKind.ChangeRejected,
            Icon = ContextMenuMessageIcon.StopSign,
            Localized = true,
            SourceLocator = sourceLocator
        };
    }

    /// <summary>
    /// Builds the structured replacement for the invalid-value dialog raised at <c>:L1009</c> - the
    /// paste of a value that is not a key of an ENUMERATED column's value set.
    /// </summary>
    /// <param name="row">The row whose pasted value was rejected.</param>
    /// <param name="offendingValue">
    /// The pasted text, appended to the message by the oracle [<c>:L1009</c>] AND carried separately as
    /// data so a caller can surface it without re-parsing the composed text.
    /// </param>
    /// <param name="sourceLocator">The oracle locator, <c>:L1009</c>.</param>
    /// <returns>The error, ready to publish on <see cref="PendingError"/>.</returns>
    /// <remarks>
    /// THE ORACLE WOULD PRODUCE A NULL MESSAGE FOR A NULL VALUE, because PowerScript concatenation with
    /// a null operand yields null for the WHOLE expression and <c>MessageBoxEx</c> would be handed that
    /// null. It is unreachable: this site fires only for a value that came straight out of the split
    /// array, which is never null. The port narrows it to a DEFINED value - the null renders as nothing
    /// and <see cref="ContextMenuError.OffendingValue"/> stays null - rather than widening the record's
    /// required text to nullable for a branch the oracle cannot take (AAP 0.1.5).
    /// </remarks>
    private ContextMenuError BuildInvalidValueError(long row, string? offendingValue, string sourceLocator)
    {
        string rowFragment = ComposeRowFragment(row);
        string? detail = _i18n.I18N(Categories.CAT_DWSVC, InvalidValueMessageKey);

        // :L1009  ... + "~n" + I18N(...) + "!~n" + sVal - the exclamation mark and the second line
        // separator are ONE literal in the oracle and are kept adjacent here for the same reason.
        string text = rowFragment
            + LineSeparator
            + detail
            + DetailSuffix
            + LineSeparator
            + (offendingValue ?? string.Empty);

        return new ContextMenuError
        {
            Text = text,
            LocalizationCategory = Categories.CAT_DWSVC,
            RowFormatTemplate = RowNumberMessageKey,
            DetailMessageKey = InvalidValueMessageKey,
            Row = row,
            OffendingValue = offendingValue,
            Kind = ContextMenuErrorKind.InvalidValue,
            Icon = ContextMenuMessageIcon.StopSign,
            Localized = true,
            SourceLocator = sourceLocator
        };
    }

    /// <summary>
    /// Builds the structured replacement for the type-mismatch dialog raised at <c>:L1018</c>,
    /// <c>:L1027</c>, <c>:L1033</c>, <c>:L1039</c> and <c>:L1045</c> - one per typed paste arm.
    /// </summary>
    /// <param name="row">The row whose pasted value was rejected.</param>
    /// <param name="offendingValue">
    /// The pasted text, appended to the message by the oracle AND carried separately as data. For the
    /// decimal arm this is the value AFTER the trailing-percent rewrite [<c>:L1023-L1025</c>], because
    /// the oracle validates and reports the rewritten value, not the text the user pasted.
    /// </param>
    /// <param name="sourceLocator">
    /// The oracle locator, which is the ONLY thing distinguishing the five sites - their messages are
    /// identical, so the arm that failed is recoverable from this field alone.
    /// </param>
    /// <returns>The error, ready to publish on <see cref="PendingError"/>.</returns>
    private ContextMenuError BuildTypeMismatchError(long row, string? offendingValue, string sourceLocator)
    {
        string rowFragment = ComposeRowFragment(row);
        string? detail = _i18n.I18N(Categories.CAT_DWSVC, TypeMismatchMessageKey);

        string text = rowFragment
            + LineSeparator
            + detail
            + DetailSuffix
            + LineSeparator
            + (offendingValue ?? string.Empty);

        return new ContextMenuError
        {
            Text = text,
            LocalizationCategory = Categories.CAT_DWSVC,
            RowFormatTemplate = RowNumberMessageKey,
            DetailMessageKey = TypeMismatchMessageKey,
            Row = row,
            OffendingValue = offendingValue,
            Kind = ContextMenuErrorKind.TypeMismatch,
            Icon = ContextMenuMessageIcon.StopSign,
            Localized = true,
            SourceLocator = sourceLocator
        };
    }


    // ==========================================================================================
    //  THE TWO AUTO-WIDTH ARITIES              n_cst_dwsvc_contextmenu.sru:L1085-L1258, L1260-L1419
    //  ----------------------------------------------------------------------------------------
    //  DECISION 8. The oracle's two auto-width functions do four things in sequence: (1) decide WHICH
    //  columns and WHICH texts participate, (2) MEASURE each text with a font, (3) convert the widest
    //  measurement into the DataWindow's unit system, and (4) write the result back with `Modify`.
    //
    //  ONE AND ONLY ONE OF THOSE FOUR IS HEADLESS, AND IT IS THE FIRST. Steps 2, 3 and 4 need
    //  `n_cst_font` [:L1092, :L1098, :L1266, :L1277], `Painter.GetFontTextSize` /
    //  `Painter.CalcFontTextSize` [:L1153, :L1158, :L1319, :L1324], the SIZEF/RECTF measurement types
    //  [:L1093-L1094, :L1267-L1268], the DPI conversions D2PX / D2UX / Win32.PX2MMX [:L1239-L1245,
    //  :L1407-L1413] and a `Modify` of the computed widths [:L1253, :L1416]. Every one of those is
    //  DesignSystem's, deferred whole, and reachable eventually under /v1/design/**.
    //
    //  SO THESE MEMBERS RETURN A MEASUREMENT PLAN. For each participating column they carry: which
    //  texts to measure, in the oracle's order; the font face, raw height and bold flag each text must
    //  be measured with; the measurement MODE - single-line, word-break or edit-control - and the
    //  1024 wrap width the two wrapped modes use; the format mask to apply first and which String
    //  overload to apply it through; the arrow-button allowance the oracle adds for a drop-down
    //  [:L1229-L1233, :L1397-L1401]; the +4 padding it adds before converting [:L1239, :L1407]; and
    //  the DataWindow's unit system so the measurer knows which conversion to run.
    //
    //  NOTHING IS SILENTLY DROPPED, WHICH IS THE WHOLE POINT (AAP 0.8.1). A plan that named the
    //  columns but not their fonts, or the texts but not their modes, would be a partial
    //  implementation masquerading as a boundary. This one is complete enough that the deferred half
    //  is a pure function of it.
    //
    //  THE ASCII/WIDE SPLIT IS THE SUBTLEST PIECE AND IT IS ENTIRELY HEADLESS. See
    //  BuildColumnProxyCandidate: `Len` counts characters and `LenA` counts DBCS bytes, so their
    //  difference is the wide-character count, and the oracle then SYNTHESIZES a worst-case proxy
    //  string of that shape rather than measuring any real value.
    // ==========================================================================================

    /// <summary>
    /// Plans the auto-width of EVERY eligible detail column - the port of <c>_of_ColumnAutoWidth()</c>
    /// (<c>n_cst_dwsvc_contextmenu.sru:L1085-L1258</c>), stopping at the measurement boundary
    /// (DECISION 8).
    /// </summary>
    /// <returns>
    /// The plan, always <see cref="RetCode.OK"/> because the oracle's all-columns arity has no failing
    /// path: every rejection is a per-column <c>continue</c> [<c>:L1103-L1110</c>].
    /// </returns>
    /// <exception cref="InvalidOperationException">No host is attached.</exception>
    /// <remarks>
    /// <para>
    /// FOUR COLUMN-SELECTION GUARDS, IN THIS ORDER [<c>:L1103-L1110</c>]: the object must be in the
    /// <c>detail</c> band; it must not be invisible; its type must be <c>column</c> or <c>compute</c>;
    /// and its edit style must not be <c>checkbox</c> or <c>radiobuttons</c>. NOTE THE PLURAL
    /// <c>"radiobuttons"</c> - Services/ColumnSortModel.cs preserves the SINGULAR
    /// <c>"radiobutton"</c> of its own oracle [<c>n_cst_dwsvc_columnsort.sru:L108</c>]. The two files
    /// genuinely disagree and are NOT harmonised.
    /// </para>
    /// <para>
    /// THE VISIBILITY TEST IS <c>= "0"</c> AND NOT <c>&lt;&gt; "1"</c> [<c>:L1104</c>], so a column
    /// whose <c>Visible</c> is a per-row EXPRESSION - which describes back as the expression text, not
    /// as <c>"0"</c> - participates. Reproduced as written.
    /// </para>
    /// <para>
    /// THE UNIT SYSTEM IS READ ONCE PER COLUMN INSIDE THE LOOP [<c>:L1237</c>], not once for the
    /// operation, and each column's own read drives its own conversion. The plan carries ONE
    /// <see cref="ColumnAutoWidthPlan.Units"/> because the conversion is deferred and a measurer needs
    /// one answer; the last read wins. A host whose <c>Describe</c> answers stably - every real
    /// DataWindow - cannot observe the difference, and the per-column read is still performed so the
    /// ported call sequence matches the oracle's.
    /// </para>
    /// <para>
    /// THE HOUR-GLASS POINTER AT <c>:L1096</c> AND ITS RESTORE AT <c>:L1255</c> ARE DEFERRED. They are
    /// DesignSystem's and a headless service has no pointer. Note the single-column arity sets no
    /// pointer at all - an asymmetry in the oracle, not in this port.
    /// </para>
    /// </remarks>
    public ColumnAutoWidthPlan ColumnAutoWidth()
    {
        DataWindowServiceHost host = RequireHost();

        // :L1096  SetPointer(HourGlass!) - DEFERRED. :L1098  Create n_cst_font - DEFERRED.

        // :L1100  nCount = _of_GetObjectNames(ref sObjNames) - UNFILTERED; the same array is walked
        // again per column for the sibling scan, so it is enumerated exactly once.
        string[] objectNames = [];
        int count = GetObjectNames(ref objectNames);

        List<ColumnWidthComputation> columns = [];
        string units = string.Empty;

        // :L1101
        for (int index = 1; index <= count; index++)
        {
            string colName = NameAt(objectNames, index);

            // :L1103
            if (!string.Equals(host.Describe(colName + ".Band"), DetailBand, StringComparison.Ordinal))
            {
                continue;
            }

            // :L1104
            if (string.Equals(host.Describe(colName + ".Visible"), "0", StringComparison.Ordinal))
            {
                continue;
            }

            // :L1105-L1106
            string objectType = host.Describe(colName + ".Type");
            if (!string.Equals(objectType, ColumnObjectType, StringComparison.Ordinal)
                && !string.Equals(objectType, ComputeObjectType, StringComparison.Ordinal))
            {
                continue;
            }

            // :L1107-L1110  a check box and a radio-button group have nothing measurable.
            if (IsUnsizableEditStyle(host.Describe(colName + ".edit.style")))
            {
                continue;
            }

            // :L1112-L1246  everything else is shared with the single-column arity.
            columns.Add(BuildColumnWidthComputation(host, colName, objectType, objectNames, count));

            // :L1237  read per column, deliberately.
            units = host.Describe(UnitsProperty);

            // :L1248  sSyntax += sColName + ".Width = '" + String(fMaxTextWidth) + "'~t" - DEFERRED,
            // and note the trailing tab that makes the syntax accumulable. :L1253  Modify(sSyntax) -
            // DEFERRED. Emitting either would require the measured width this half does not have.
        }

        // :L1251  Destroy font - DEFERRED. :L1255  SetPointer(Arrow!) - DEFERRED.

        ColumnAutoWidthPlan plan = new()
        {
            // :L1257
            Code = RetCode.OK,
            Columns = columns,
            Units = units
        };

        PendingWidthPlan = plan;

        return plan;
    }

    /// <summary>
    /// Plans the auto-width of ONE named column - the port of
    /// <c>_of_ColumnAutoWidth(readonly string colname)</c>
    /// (<c>n_cst_dwsvc_contextmenu.sru:L1260-L1419</c>), stopping at the measurement boundary
    /// (DECISION 8).
    /// </summary>
    /// <param name="colName">The column to plan. The empty string is rejected [<c>:L1270</c>].</param>
    /// <returns>
    /// The plan; <see cref="RetCode.E_INVALID_ARGUMENT"/> with no columns for an empty name
    /// [<c>:L1270</c>], <see cref="RetCode.FAILED"/> with no columns for a check-box or radio-button
    /// column [<c>:L1272-L1275</c>], otherwise <see cref="RetCode.OK"/> with exactly one column.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="colName"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">No host is attached.</exception>
    /// <remarks>
    /// <para>
    /// THE TWO ARITIES DISAGREE ABOUT ELIGIBILITY, AND THE DIFFERENCE IS THE ORACLE'S. This one applies
    /// only ONE of the all-columns arity's four guards - the edit-style one, and it FAILS rather than
    /// skipping [<c>:L1274</c>]. It does not require the <c>detail</c> band, does not test visibility
    /// and does not test the object type, so it will happily plan a header-band text object or an
    /// invisible column that the all-columns arity refuses. Reproduced, not reconciled (constraint
    /// C-B); the reason is legible from the call sites - this arity is reached from the context menu
    /// with a column the user right-clicked, which the menu has already vetted.
    /// </para>
    /// <para>
    /// AN INELIGIBLE EDIT STYLE FAILS HERE AND IS MERELY SKIPPED THERE. Same test, two different
    /// dispositions, because a single-column request that cannot be honoured has to report something.
    /// </para>
    /// </remarks>
    public ColumnAutoWidthPlan ColumnAutoWidth(in string colName)
    {
        ArgumentNullException.ThrowIfNull(colName);

        // :L1270
        if (colName.Length == 0)
        {
            return PublishPlan(RetCode.E_INVALID_ARGUMENT, [], string.Empty);
        }

        DataWindowServiceHost host = RequireHost();

        // :L1272-L1275  the ONLY guard this arity applies, and it FAILS rather than skipping.
        if (IsUnsizableEditStyle(host.Describe(colName + ".edit.style")))
        {
            return PublishPlan(RetCode.FAILED, [], string.Empty);
        }

        // :L1277  Create n_cst_font - DEFERRED.

        // :L1279  the type is read for the compute flag ONLY - it is not a guard here.
        string objectType = host.Describe(colName + ".Type");

        // :L1284  the enumeration happens AFTER the column's own geometry is read in this arity and
        // BEFORE it in the other [:L1100]; the order has no observable effect on either.
        string[] objectNames = [];
        int count = GetObjectNames(ref objectNames);

        // :L1279-L1390  shared with the all-columns arity.
        ColumnWidthComputation computation =
            BuildColumnWidthComputation(host, colName, objectType, objectNames, count);

        // :L1392  Destroy font - DEFERRED, and note it happens BEFORE the arrow-button and unit blocks
        // here while the other arity destroys after its whole loop [:L1251].

        // :L1405  read once in this arity.
        string units = host.Describe(UnitsProperty);

        // :L1416  Modify(colName + ".Width = '" + String(fMaxTextWidth) + "'") - DEFERRED, and note it
        // carries NO trailing tab, unlike the accumulating form at :L1248.

        // :L1418
        return PublishPlan(RetCode.OK, [computation], units);
    }

    /// <summary>
    /// The port of the two arities' shared body (<c>:L1112-L1235</c> and <c>:L1279-L1403</c>) - the
    /// sibling heading scan, the per-row scan and the arrow-button allowance for one column.
    /// </summary>
    /// <param name="host">The attached host.</param>
    /// <param name="colName">The column being planned.</param>
    /// <param name="objectType">
    /// The column's already-read <c>.Type</c>. Passed in rather than re-read because the all-columns
    /// arity reads it as a GUARD [<c>:L1105</c>] and then reuses that same answer for the compute flag
    /// [<c>:L1112</c>] - re-reading here would add a <c>Describe</c> the oracle does not make.
    /// </param>
    /// <param name="objectNames">The unfiltered object-name array, ONE-BASED in the oracle.</param>
    /// <param name="count">Its upper bound, which for a one-based array is its length.</param>
    /// <returns>The measurement plan for this one column.</returns>
    /// <remarks>
    /// <para>
    /// THE TWO ARITIES' BODIES ARE THE SAME CODE TWICE IN THE ORACLE, line for line, differing only in
    /// whether the column name comes from a loop variable or a parameter. They are unified here because
    /// a divergence between them would be a port defect rather than a preserved one - and every place
    /// where the oracle's two copies genuinely differ is handled by the CALLERS, not here.
    /// </para>
    /// <para>
    /// THE HEADING SCAN WALKS EVERY OBJECT AND KEEPS THE ONES OVER THIS COLUMN
    /// [<c>:L1119-L1164</c>, <c>:L1285-L1330</c>], which is how a header text, a footer total and a
    /// summary line all widen the column they sit above.
    /// </para>
    /// </remarks>
    private ColumnWidthComputation BuildColumnWidthComputation(
        DataWindowServiceHost host,
        string colName,
        string objectType,
        string[] objectNames,
        int count)
    {
        // :L1112 / :L1279
        bool isCompute = string.Equals(objectType, ComputeObjectType, StringComparison.Ordinal);

        // :L1113 / :L1280  the flag that selects the edit-control measurement mode for row texts.
        bool autoHeight = string.Equals(
            host.Describe(colName + ".Height.AutoSize"),
            YesAnswer,
            StringComparison.Ordinal);

        // :L1115  fMaxTextWidth = 0 - the accumulator, which lives in the DEFERRED half.

        // :L1117-L1118 / :L1282-L1283
        long colXPos = ParseLong(host.Describe(colName + ".X")) ?? 0L;
        long colWidth = ParseLong(host.Describe(colName + ".Width")) ?? 0L;

        List<TextMeasurementCandidate> headings =
            BuildHeadingCandidates(host, objectNames, count, colXPos, colWidth);

        // :L1166-L1175 / :L1332-L1341  the compute column's own format, resolved ONCE for the column.
        string columnFormat = string.Empty;
        bool formatIsExpression = false;
        if (isCompute)
        {
            // :L1167 / :L1333  RAW Describe here, NOT _of_GetDWOProp - so an expression form is not
            // resolved by the read and is instead detected by the tab test below.
            columnFormat = NormaliseFormat(host.Describe(colName + ".Format"));

            // :L1169 / :L1335  bFmtIsExp = (Pos(sFormat,"~t") > 0)
            formatIsExpression = columnFormat.Contains(PropertyExpressionSeparator, StringComparison.Ordinal);
            if (formatIsExpression)
            {
                // :L1171 / :L1337  strip the tab form down to the expression text; it is evaluated PER
                // ROW below, which is the one piece of format handling this half can complete.
                columnFormat = GetPropertyExpression(columnFormat);
            }
        }

        // :L1176-L1178 / :L1342-L1344  the column's own font, applied to every ROW candidate.
        long fontHeight = ParseLong(host.Describe(colName + ".Font.Height")) ?? 0L;
        string fontFace = host.Describe(colName + ".Font.Face");
        bool bold = string.Equals(
            host.Describe(colName + ".Font.Weight"),
            BoldFontWeight,
            StringComparison.Ordinal);

        (List<TextMeasurementCandidate> rows, string findExpression) = BuildRowCandidates(
            host,
            colName,
            isCompute,
            autoHeight,
            columnFormat,
            formatIsExpression,
            fontFace,
            fontHeight,
            bold);

        // :L1226-L1235 / :L1394-L1403  the drop-down arrow allowance.
        double arrowButtonWidth = ResolveArrowButtonWidth(host, colName);

        return new ColumnWidthComputation
        {
            ColName = colName,
            IsCompute = isCompute,
            AutoHeight = autoHeight,
            ColumnXPosition = colXPos,
            ColumnWidth = colWidth,
            HeadingCandidates = headings,
            RowCandidates = rows,
            DistinctValueFindExpression = findExpression,
            ArrowButtonWidth = arrowButtonWidth,

            // :L1239-L1245 / :L1407-L1413  the +4 the oracle adds INSIDE every unit conversion. It is
            // carried rather than applied because it is added to a MEASURED width this half does not
            // have; the conversion that consumes it is DesignSystem's.
            Padding = WidthPadding
        };
    }

    /// <summary>
    /// Builds the measurement candidates contributed by the objects sitting OVER one column - the port
    /// of the sibling scan (<c>:L1119-L1164</c>, <c>:L1285-L1330</c>).
    /// </summary>
    /// <param name="host">The attached host.</param>
    /// <param name="objectNames">The unfiltered object-name array, ONE-BASED in the oracle.</param>
    /// <param name="count">Its upper bound.</param>
    /// <param name="colXPos">The column's <c>X</c>.</param>
    /// <param name="colWidth">The column's <c>Width</c>.</param>
    /// <returns>The candidates, in the oracle's enumeration order.</returns>
    /// <remarks>
    /// <para>
    /// THREE GUARDS, IN THIS ORDER. The band must be <c>summary</c>, <c>footer</c>, or prefixed
    /// <c>header</c> or <c>trailer</c> [<c>:L1121</c>] - the two prefixes admit the numbered group
    /// bands, <c>header.1</c> and <c>trailer.1</c>, which is why they are prefix tests and the other
    /// two are equality tests. The object must not be invisible [<c>:L1122</c>]. And its <c>X</c> must
    /// overlap the column's span, tested as <c>nXPos &lt; nColXPos - 8 or nXPos &gt; nColXPos +
    /// nColWidth</c> [<c>:L1124</c>].
    /// </para>
    /// <para>
    /// THE OVERLAP TEST IS ASYMMETRIC AND THE ASYMMETRY IS THE ORACLE'S. It allows 8 units of slack on
    /// the LEFT and none on the right, and it tests only the sibling's LEFT edge - a wide text object
    /// starting just left of the column still counts, while one starting just right of the column's
    /// right edge does not, however far left it extends. Reproduced verbatim; the 8 is a bare literal
    /// with no explanation anywhere in the oracle.
    /// </para>
    /// <para>
    /// THE MEASUREMENT MODE IS CHOSEN BY AN EXACT BAND MATCH, NOT A PREFIX [<c>:L1151</c>]. Only the
    /// band spelled exactly <c>"header"</c> measures with word-break wrapping; a numbered group header
    /// admitted by the prefix guard measures SINGLE-LINE. That combination - admitted by a prefix,
    /// measured by an equality - is easy to "tidy" into one rule and must not be.
    /// </para>
    /// <para>
    /// FOUR TEXT SOURCES [<c>:L1128-L1140</c>]: a computed field evaluates itself; a COLUMN produces
    /// the synthesized proxy string of <see cref="BuildColumnProxyCandidate"/>; a text object reads its
    /// <c>text</c> property; anything else contributes the empty string and is therefore dropped by the
    /// guard below.
    /// </para>
    /// <para>
    /// THE DROP GUARD REJECTS TWO VALUES AND ONLY TWO [<c>:L1141</c>]: the empty string and <c>"!"</c>.
    /// <c>"?"</c> IS NOT REJECTED, so an indeterminate value is measured as the literal question mark -
    /// a one-character candidate. That is an inconsistency with every other sentinel test in the file,
    /// where the two are always tested as a pair, and it is preserved.
    /// </para>
    /// </remarks>
    private List<TextMeasurementCandidate> BuildHeadingCandidates(
        DataWindowServiceHost host,
        string[] objectNames,
        int count,
        long colXPos,
        long colWidth)
    {
        List<TextMeasurementCandidate> candidates = [];

        // :L1119 / :L1285
        for (int index = 1; index <= count; index++)
        {
            string objectName = NameAt(objectNames, index);

            // :L1120-L1121
            string band = host.Describe(objectName + ".Band");
            if (!string.Equals(band, SummaryBand, StringComparison.Ordinal)
                && !string.Equals(band, FooterBand, StringComparison.Ordinal)
                && !band.StartsWith(HeaderBand, StringComparison.Ordinal)
                && !band.StartsWith(TrailerBandPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            // :L1122
            if (string.Equals(host.Describe(objectName + ".Visible"), "0", StringComparison.Ordinal))
            {
                continue;
            }

            // :L1123-L1124  the asymmetric overlap test.
            long xPos = ParseLong(host.Describe(objectName + ".X")) ?? 0L;
            if (xPos < colXPos - 8L || xPos > colXPos + colWidth)
            {
                continue;
            }

            // :L1125-L1127  the sibling's OWN font - each candidate carries its own, because a header
            // and a footer over one column may be set in different faces.
            long fontHeight = ParseLong(host.Describe(objectName + ".Font.Height")) ?? 0L;
            string fontFace = host.Describe(objectName + ".Font.Face");
            bool bold = string.Equals(
                host.Describe(objectName + ".Font.Weight"),
                BoldFontWeight,
                StringComparison.Ordinal);

            // :L1128-L1140  the four text sources.
            string objectType = host.Describe(objectName + ".Type");
            string text;
            int asciiCount = 0;
            int wideCount = 0;
            bool isProxy = false;

            if (string.Equals(objectType, ComputeObjectType, StringComparison.Ordinal))
            {
                // :L1130 / :L1296  _of_Evaluate(name) - the ROW-ZERO form, outside any row context.
                text = _evaluator.Evaluate(objectName);
            }
            else if (string.Equals(objectType, ColumnObjectType, StringComparison.Ordinal))
            {
                // :L1132-L1135 / :L1298-L1301
                (text, asciiCount, wideCount) = BuildColumnProxyCandidate(objectName);
                isProxy = true;
            }
            else if (string.Equals(objectType, TextObjectType, StringComparison.Ordinal))
            {
                // :L1137 / :L1303
                text = GetDataWindowObjectText(objectName);
            }
            else
            {
                // :L1139 / :L1305  case else
                text = string.Empty;
            }

            // :L1141 / :L1307  the empty string and "!" are dropped; "?" is NOT.
            if (text.Length == 0 || string.Equals(text, InvalidExpressionSentinel, StringComparison.Ordinal))
            {
                continue;
            }

            // :L1142-L1150 / :L1308-L1316  the mask, resolved but NOT applied (DECISION 8).
            string mask = NormaliseFormat(GetDataWindowObjectProperty(objectName, FormatProperty));

            candidates.Add(new TextMeasurementCandidate
            {
                ObjectName = objectName,
                Band = band,
                ObjectType = objectType,
                Text = text,
                AsciiCount = asciiCount,
                WideCount = wideCount,
                IsSynthesizedProxy = isProxy,
                FontFace = fontFace,
                FontHeight = fontHeight,
                Bold = bold,

                // :L1151-L1162 / :L1317-L1328  an EXACT "header" wraps; everything else does not.
                MeasurementMode = string.Equals(band, HeaderBand, StringComparison.Ordinal)
                    ? TextMeasurementMode.WordBreak
                    : TextMeasurementMode.SingleLine,
                WrapWidth = string.Equals(band, HeaderBand, StringComparison.Ordinal)
                    ? WrapWidthLimit
                    : 0,
                FormatMask = mask,

                // :L1145 / :L1311  IsNumber(sText) chooses the String overload.
                FormatAppliesToNumber = mask.Length != 0 && IsNumber(text)
            });
        }

        return candidates;
    }

    /// <summary>
    /// Builds the SYNTHESIZED worst-case proxy text for a column sibling - the port of
    /// <c>:L1132-L1135</c> and <c>:L1298-L1301</c>, and the subtlest headless behaviour in the file.
    /// </summary>
    /// <param name="objectName">The column object whose values are being sized.</param>
    /// <returns>
    /// The proxy text, the ASCII character count and the wide character count. All three are zero and
    /// empty when either evaluation fails, which drops the candidate.
    /// </returns>
    /// <remarks>
    /// <para>
    /// IT DOES NOT MEASURE ANY REAL VALUE. Two aggregate evaluations give the widest value's CHARACTER
    /// count and the widest value's DBCS BYTE count - <c>Max(Len(LookUpDisplay(col)))</c> and
    /// <c>Max(LenA(LookUpDisplay(col)))</c> - and their difference is the number of double-byte
    /// characters, because a DBCS character counts one in <c>Len</c> and two in <c>LenA</c>. The oracle
    /// then builds a string of that shape out of <c>"A"</c> and <c>"国"</c> and measures THAT. The
    /// proxy is what makes one measurement stand in for a whole column.
    /// </para>
    /// <para>
    /// THE ARITHMETIC IS EXACTLY <c>A</c>, <c>|B - A|</c>, <c>A - |B - A|</c> [<c>:L1132-L1134</c>] and
    /// the <c>Abs</c> is load-bearing: it is what keeps the wide count non-negative if the two
    /// aggregates disagree, which they can when the widest-by-characters and the widest-by-bytes values
    /// are DIFFERENT rows. In that case the proxy is not the shape of any single value in the column -
    /// it is a synthetic worst case, which is the intent.
    /// </para>
    /// <para>
    /// BOTH FILL LITERALS ARE EXACT AND NEITHER IS INCIDENTAL. <c>"A"</c> is a representative
    /// single-byte glyph; <c>"国"</c> (U+56FD) is a representative full-width one. Substituting any
    /// other character would change every measured width, so they are carried verbatim rather than
    /// abstracted into a constant with a friendlier name.
    /// </para>
    /// <para>
    /// A FAILED EVALUATION PROPAGATES AS NOTHING, NOT AS ZERO. PowerScript's <c>Long("!")</c> is null,
    /// null arithmetic is null, and <c>Fill</c> of a null count is null - so the whole proxy is null and
    /// the <c>sText &lt;&gt; ""</c> guard at <c>:L1141</c> does not fire, dropping the candidate. The
    /// port reproduces that by answering the empty string, which the same guard drops.
    /// </para>
    /// <para>
    /// A NEGATIVE OR ZERO COUNT FILLS NOTHING, matching <c>Fill</c>'s behaviour for a non-positive
    /// count. A column of purely wide text makes the ASCII count zero or negative - two aggregates of
    /// <c>n</c> and <c>2n</c> give <c>A - |B - A| = 0</c> - so the proxy is all <c>"国"</c>, which is
    /// correct.
    /// </para>
    /// </remarks>
    private (string Text, int AsciiCount, int WideCount) BuildColumnProxyCandidate(string objectName)
    {
        // :L1132  nAsc2Cnt = Long(_of_Evaluate("Max(Len(LookUpDisplay(" + name + ")))"))
        long? characters = ParseLong(
            _evaluator.Evaluate("Max(Len(LookUpDisplay(" + objectName + ")))"));

        // :L1133  nChsCnt = Abs(Long(_of_Evaluate("Max(LenA(...))")) - nAsc2Cnt)
        long? bytes = ParseLong(
            _evaluator.Evaluate("Max(LenA(LookUpDisplay(" + objectName + ")))"));

        if (characters is null || bytes is null)
        {
            return (string.Empty, 0, 0);
        }

        long wide = Math.Abs(bytes.Value - characters.Value);

        // :L1134  nAsc2Cnt -= nChsCnt
        long ascii = characters.Value - wide;

        // :L1135  sText = Fill("A",nAsc2Cnt) + Fill("国",nChsCnt)
        string text = Fill('A', ascii) + Fill('国', wide);

        return (text, ClampToInt(ascii), ClampToInt(wide));
    }

    /// <summary>
    /// Builds the measurement candidates contributed by the column's own ROWS, plus the predicate that
    /// selected them - the port of the row scan (<c>:L1180-L1224</c>, <c>:L1346-L1390</c>).
    /// </summary>
    /// <param name="host">The attached host.</param>
    /// <param name="colName">The column being planned.</param>
    /// <param name="isCompute">Whether the column is a computed field.</param>
    /// <param name="autoHeight">Whether the column auto-sizes its height.</param>
    /// <param name="columnFormat">
    /// The column's normalised format - a mask, or an EXPRESSION when
    /// <paramref name="formatIsExpression"/> is set.
    /// </param>
    /// <param name="formatIsExpression">Whether the format must be evaluated per row.</param>
    /// <param name="fontFace">The column's font face.</param>
    /// <param name="fontHeight">The column's raw <c>Font.Height</c>.</param>
    /// <param name="bold">Whether the column's font is bold.</param>
    /// <returns>The candidates and the <c>Find</c> predicate that produced them.</returns>
    /// <remarks>
    /// <para>
    /// THE SCAN IS AN ADJACENT-DIFFERENCE SCAN, AND THAT IS WHY IT IS EXACT RATHER THAN APPROXIMATE.
    /// The predicate compares each row against the NEXT one - <c>col[1]</c> is a row OFFSET of one, and
    /// the compute variant spells it out with <c>String(GetRow() + 1)</c> [<c>:L1183</c>] - so
    /// <c>Find</c> answers the LAST row of every run of equal adjacent values. Every distinct value in
    /// the column belongs to at least one such run, so every distinct value is measured exactly once per
    /// run and the widest can never be missed. Replacing it with "measure every row" would produce the
    /// same width more slowly; replacing it with "measure the first row" would produce a WRONG width.
    /// </para>
    /// <para>
    /// THREE PREDICATES, CHOSEN BY COLUMN SHAPE, AND ALL THREE ARE BYTE-EXACT [<c>:L1183</c>,
    /// <c>:L1186</c>, <c>:L1188</c>]. A computed field compares its own stringified value against a
    /// NESTED <c>Describe("Evaluate(...)")</c> of the next row, guarded by
    /// <c>GetRow() &lt; RowCount()</c> so the last row compares against the empty string. A STRING
    /// column normalises both sides' nulls to the empty string. Everything else tests value inequality
    /// OR null-ness inequality, which is how it distinguishes a null from a zero.
    /// </para>
    /// <para>
    /// THE <c>{1}</c> REPETITION COUNTS ARE TWO, FOUR AND FOUR - measured from the oracle, and the
    /// third one CORRECTS this file's brief, which states six. All three go to
    /// <see cref="Formatting.Sprintf"/> in its INDEXED form with the column name as the single
    /// argument; the ten error messages in this same file use the SEQUENTIAL empty-brace form of the
    /// same method. One formatter, two grammars.
    /// </para>
    /// <para>
    /// THE LOOP TERMINATES ON THE LAST ROW AND NOT ON A FAILED <c>Find</c> [<c>:L1221</c>], which
    /// matters because <c>Find</c> starting past the last row is not well defined. The last row is
    /// therefore ALWAYS measured when it matched, and the search never restarts from row 1.
    /// </para>
    /// <para>
    /// A COMPUTED FIELD'S <c>"!"</c> BECOMES THE EMPTY STRING [<c>:L1195</c>] and is then dropped by
    /// the <c>sText &lt;&gt; ""</c> guard [<c>:L1207</c>] - so an unevaluable row contributes nothing,
    /// while a <c>"?"</c> once again survives as a literal question mark.
    /// </para>
    /// <para>
    /// A NON-COMPUTE ROW READS ITS DISPLAY VALUE [<c>:L1205</c>], not its data value, so a code table
    /// is sized by what the user sees. That read carries the column's own format already, which is why
    /// no mask is attached to a non-compute row candidate.
    /// </para>
    /// </remarks>
    private (List<TextMeasurementCandidate> Candidates, string FindExpression) BuildRowCandidates(
        DataWindowServiceHost host,
        string colName,
        bool isCompute,
        bool autoHeight,
        string columnFormat,
        bool formatIsExpression,
        string fontFace,
        long fontHeight,
        bool bold)
    {
        List<TextMeasurementCandidate> candidates = [];

        // :L1180 / :L1346
        long rowCount = host.RowCount();

        // :L1181 / :L1347  no rows means no row candidates AND no predicate.
        if (rowCount <= 0L)
        {
            return (candidates, string.Empty);
        }

        string findExpression = BuildFindExpression(colName, isCompute);

        // :L1191 / :L1357
        long row = host.Find(findExpression, 1L, rowCount);

        // :L1192 / :L1358
        while (row > 0L)
        {
            string text;
            string mask = string.Empty;

            if (isCompute)
            {
                // :L1194 / :L1360  _of_Evaluate(colName,nRow) - the AT-A-ROW form.
                text = _evaluator.Evaluate(colName, row);

                // :L1195 / :L1361  if sText = "!" then sText = ""
                if (string.Equals(text, InvalidExpressionSentinel, StringComparison.Ordinal))
                {
                    text = string.Empty;
                }

                // :L1196-L1203 / :L1362-L1369  the mask, which may itself be a per-row expression.
                if (text.Length != 0 && columnFormat.Length != 0)
                {
                    // :L1197 / :L1363  if bFmtIsExp then sFormatVal = _of_Evaluate(sFormat,nRow)
                    mask = formatIsExpression
                        ? _evaluator.Evaluate(columnFormat, row)
                        : columnFormat;
                }
            }
            else
            {
                // :L1205 / :L1371  the DISPLAY value.
                text = LookupDisplay(row, colName);
            }

            // :L1207 / :L1373
            if (text.Length != 0)
            {
                candidates.Add(new TextMeasurementCandidate
                {
                    ObjectName = colName,
                    Band = DetailBand,
                    ObjectType = isCompute ? ComputeObjectType : ColumnObjectType,
                    Text = text,
                    AsciiCount = 0,
                    WideCount = 0,
                    IsSynthesizedProxy = false,
                    FontFace = fontFace,
                    FontHeight = fontHeight,
                    Bold = bold,

                    // :L1208-L1219 / :L1374-L1385  an auto-height column measures as an EDIT CONTROL
                    // with tab expansion and external leading; everything else measures single-line.
                    MeasurementMode = autoHeight
                        ? TextMeasurementMode.EditControl
                        : TextMeasurementMode.SingleLine,
                    WrapWidth = autoHeight ? WrapWidthLimit : 0,
                    FormatMask = mask,

                    // :L1198 / :L1364
                    FormatAppliesToNumber = mask.Length != 0 && IsNumber(text)
                });
            }

            // :L1221 / :L1387  if nRow = nRowCnt then exit - the ONLY exit besides a non-positive Find.
            if (row == rowCount)
            {
                break;
            }

            // :L1222 / :L1388  resume from the row AFTER the match.
            row = host.Find(findExpression, row + 1L, rowCount);
        }

        return (candidates, findExpression);
    }

    /// <summary>
    /// Builds the <c>Find</c> predicate that selects the rows worth measuring - the port of
    /// <c>:L1182-L1190</c> and <c>:L1348-L1356</c>.
    /// </summary>
    /// <param name="colName">The column, substituted for every <c>{1}</c>.</param>
    /// <param name="isCompute">Whether the column is a computed field.</param>
    /// <returns>The predicate, as a DataWindow expression.</returns>
    /// <remarks>
    /// EVERY FORMAT STRING HERE IS BYTE-EXACT AND MUST STAY THAT WAY. They are DataWindow expression
    /// text, not C# format strings: the doubled quotes inside the compute predicate are PowerScript's
    /// <c>~"</c> escapes and become part of the expression the DataWindow parses, where the inner
    /// <c>String(GetRow() + 1)</c> and the <c>+</c> operators are evaluated BY THE DATAWINDOW rather
    /// than here. A port that "simplified" the nesting or pre-computed the row number would build a
    /// predicate that selects different rows and therefore computes a different width.
    /// </remarks>
    private string BuildFindExpression(string colName, bool isCompute)
    {
        if (isCompute)
        {
            // :L1183 / :L1349  {1} TWICE. The nested Describe evaluates the compute at the NEXT row,
            // and the GetRow() < RowCount() guard makes the last row compare against ''.
            return Formatting.Sprintf(
                "String({1}) <> if(GetRow() < RowCount(),Describe(\"Evaluate('{1}',\" + String(GetRow() + 1) + \")\"),'')",
                colName);
        }

        // :L1185 / :L1351
        if (GetColumnType(colName) == COL_TYPE_STRING)
        {
            // :L1186 / :L1352  {1} FOUR TIMES - both sides' nulls normalised to ''.
            return Formatting.Sprintf(
                "if(IsNull({1}),'',{1}) <> if(IsNull({1}[1]),'',{1}[1])",
                colName);
        }

        // :L1188 / :L1354  {1} FOUR TIMES - value inequality OR null-ness inequality. The brief's
        // "six" was measured against the oracle and is four.
        return Formatting.Sprintf(
            "({1} <> {1}[1]) or (if(IsNull({1}),1,0) <> if(IsNull({1}[1]),1,0))",
            colName);
    }

    /// <summary>
    /// Decides the drop-down arrow allowance for one column - the port of <c>:L1226-L1235</c> and
    /// <c>:L1394-L1403</c>.
    /// </summary>
    /// <param name="host">The attached host.</param>
    /// <param name="colName">The column.</param>
    /// <returns>
    /// <c>ARROW_BTN_WIDTH</c> when the column needs room for a drop-down arrow, otherwise zero.
    /// </returns>
    /// <remarks>
    /// <para>
    /// A PURELY HEADLESS DECISION, WHICH IS WHY IT IS MADE HERE. It is three <c>Describe</c> reads and
    /// an editability test, none of which needs a font or a pixel; only the ADDITION of the allowance to
    /// a measured width is deferred, and the allowance travels as
    /// <see cref="ColumnWidthComputation.ArrowButtonWidth"/>.
    /// </para>
    /// <para>
    /// THE THREE CONDITIONS ARE AN ELSEIF CHAIN AND THE FIRST MATCH WINS [<c>:L1228-L1234</c>], so an
    /// editable drop-down never reads either <c>UseAsBorder</c> property. The allowance is the same
    /// value in all three arms - the oracle adds <c>ARROW_BTN_WIDTH</c> once, never twice - so the
    /// chain's only observable effect is the number of <c>Describe</c> calls it makes.
    /// </para>
    /// <para>
    /// THE EDIT-STYLE GUARD IS <c>dddw</c> OR <c>ddlb</c> AND NOTHING ELSE [<c>:L1227</c>]. An
    /// <c>editmask</c> with a spin control gets no allowance, which is the oracle's behaviour and not an
    /// oversight this port corrects.
    /// </para>
    /// </remarks>
    private double ResolveArrowButtonWidth(DataWindowServiceHost host, string colName)
    {
        // :L1226-L1227 / :L1394-L1395  note the differing capitalisation of the property between the
        // two arities' guards - ".Edit.Style" here and ".edit.style" at :L1272; PowerScript's Describe
        // is case-insensitive on property names, so the two are one read spelled two ways.
        string editStyle = host.Describe(colName + ".Edit.Style");
        if (!string.Equals(editStyle, DropDownDataWindowEditStyle, StringComparison.Ordinal)
            && !string.Equals(editStyle, DropDownListBoxEditStyle, StringComparison.Ordinal))
        {
            return 0d;
        }

        // :L1228 / :L1396
        if (IsColumnEditable(colName))
        {
            return ARROW_BTN_WIDTH;
        }

        // :L1230 / :L1398
        if (string.Equals(
                host.Describe(colName + ".DDDW.UseAsBorder"),
                YesAnswer,
                StringComparison.Ordinal))
        {
            return ARROW_BTN_WIDTH;
        }

        // :L1232 / :L1400
        if (string.Equals(
                host.Describe(colName + ".DDLB.UseAsBorder"),
                YesAnswer,
                StringComparison.Ordinal))
        {
            return ARROW_BTN_WIDTH;
        }

        // :L1235 / :L1403  no arm matched - the allowance is not added.
        return 0d;
    }

    /// <summary>
    /// The port of the two edit styles that make a column unsizable - <c>checkbox</c> and
    /// <c>radiobuttons</c> (<c>:L1107-L1110</c>, <c>:L1272-L1275</c>).
    /// </summary>
    /// <param name="editStyle">The column's <c>edit.style</c>.</param>
    /// <returns><see langword="true"/> when the column has no measurable text.</returns>
    /// <remarks>
    /// THE PLURAL SPELLING IS THIS FILE'S AND IS NOT SHARED. <c>Services/ColumnSortModel.cs</c> tests
    /// the SINGULAR <c>"radiobutton"</c> because that is what its own oracle writes
    /// [<c>n_cst_dwsvc_columnsort.sru:L108</c>]. Two files, two spellings, one legacy inconsistency -
    /// harmonising them would change which columns each operation accepts.
    /// </remarks>
    private static bool IsUnsizableEditStyle(string editStyle)
    {
        return string.Equals(editStyle, CheckBoxEditStyle, StringComparison.Ordinal)
            || string.Equals(editStyle, RadioButtonsEditStyle, StringComparison.Ordinal);
    }

    /// <summary>
    /// Normalises a format property to "a mask or nothing" - the port of the three-way discard at
    /// <c>:L1143</c>, <c>:L1168</c>, <c>:L1309</c> and <c>:L1334</c>.
    /// </summary>
    /// <param name="format">The raw format property.</param>
    /// <returns>The mask, or the empty string when there is effectively none.</returns>
    /// <remarks>
    /// THE <c>[general]</c> TEST IS CASE-FOLDED AND THE SENTINEL TESTS ARE NOT [<c>:L1143</c>]. The
    /// oracle writes <c>Lower(sFormat) = "[general]"</c> against bare <c>=</c> comparisons for
    /// <c>"!"</c> and <c>"?"</c>, so <c>"[GENERAL]"</c> is discarded while a sentinel must match
    /// exactly. Reproduced with an ordinal-ignore-case comparison for the first and ordinal for the
    /// other two.
    /// </remarks>
    private static string NormaliseFormat(string format)
    {
        if (IsUnreadableAnswer(format)
            || string.Equals(format, GeneralFormatMarker, StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return format;
    }

    /// <summary>
    /// The ONE-BASED read of the object-name array - the port of <c>sObjNames[nIndex]</c>
    /// (<c>:L1102</c>, <c>:L1120</c>, <c>:L1286</c>).
    /// </summary>
    /// <param name="names">The names, zero-based as every C# array is.</param>
    /// <param name="oneBasedIndex">The oracle's index, counting from 1.</param>
    /// <returns>The name, or the empty string for an index past the last.</returns>
    /// <remarks>
    /// ROUTED THROUGH A HELPER FOR THE SAME REASON THE ITEM STORE'S ACCESSORS ARE (AAP 0.4.5.4, risk
    /// R9). The two auto-width arities index this array five times between them, and each is a place a
    /// bare <c>- 1</c> could go missing.
    /// </remarks>
    private static string NameAt(string[] names, int oneBasedIndex)
    {
        ArgumentNullException.ThrowIfNull(names);

        if (oneBasedIndex < 1 || oneBasedIndex > names.Length)
        {
            return string.Empty;
        }

        return names[oneBasedIndex - 1];
    }

    /// <summary>
    /// The port of PowerScript's <c>Fill(character, count)</c> as the proxy builder uses it
    /// (<c>:L1135</c>, <c>:L1301</c>).
    /// </summary>
    /// <param name="character">The character to repeat.</param>
    /// <param name="count">How many times. A non-positive count fills nothing.</param>
    /// <returns>The filled string.</returns>
    /// <remarks>
    /// A NON-POSITIVE COUNT ANSWERS THE EMPTY STRING rather than raising, which is what <c>Fill</c>
    /// does and what the proxy arithmetic needs: a column of purely wide text drives the ASCII count to
    /// zero, and one whose two aggregates disagree can drive it negative.
    /// </remarks>
    private static string Fill(char character, long count)
    {
        if (count <= 0L)
        {
            return string.Empty;
        }

        return new string(character, ClampToInt(count));
    }

    /// <summary>
    /// Narrows a PowerScript <c>long</c> count to the <see langword="int"/> the record and
    /// <see cref="string(char, int)"/> take, saturating rather than overflowing.
    /// </summary>
    /// <param name="value">The count.</param>
    /// <returns>The value clamped to the non-negative <see langword="int"/> range.</returns>
    /// <remarks>
    /// SATURATION RATHER THAN A CHECKED CAST, because the counts come from a DataWindow aggregate over
    /// user data and a hostile or corrupt answer must not turn a width computation into an overflow
    /// exception. No real column can reach the bound; the clamp exists so that the failure mode of one
    /// that somehow did is a large proxy rather than a crash.
    /// </remarks>
    private static int ClampToInt(long value)
    {
        if (value <= 0L)
        {
            return 0;
        }

        return value >= int.MaxValue ? int.MaxValue : (int)value;
    }

    /// <summary>
    /// Publishes a plan on <see cref="PendingWidthPlan"/> and returns it.
    /// </summary>
    /// <param name="code">The return code the oracle produced.</param>
    /// <param name="columns">The planned columns.</param>
    /// <param name="units">The DataWindow's unit system, as described.</param>
    /// <returns>The plan.</returns>
    /// <remarks>
    /// EXISTS SO THAT EVERY EXIT OF THE SINGLE-COLUMN ARITY PUBLISHES, including the two that fail
    /// before any column is planned. A caller that read only the return value would otherwise see a
    /// stale plan from a previous invocation.
    /// </remarks>
    private ColumnAutoWidthPlan PublishPlan(
        long code,
        IReadOnlyList<ColumnWidthComputation> columns,
        string units)
    {
        ColumnAutoWidthPlan plan = new()
        {
            Code = code,
            Columns = columns,
            Units = units
        };

        PendingWidthPlan = plan;

        return plan;
    }
}
