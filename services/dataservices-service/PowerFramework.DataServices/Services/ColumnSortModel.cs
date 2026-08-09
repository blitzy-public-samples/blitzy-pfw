// ==============================================================================================
//  ColumnSortModel - the managed port of the HEADLESS HALF of the DataWindow COLUMN SORT service.
//  --------------------------------------------------------------------------------------------
//  PORTED FROM
//      ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_columnsort.sru   (450 lines)
//
//  READ AS REFERENCE, NOT PORTED HERE
//      n_cst_dwsvc.sru        (864 lines) the common service base - ported to
//                             Domain/DataWindowServiceHost.cs as DataWindowServiceBase, including
//                             the STYLE_*/COL_TYPE_* catalogues and the three protected helpers
//                             this file consumes: _of_getstyle [:L46] as GetPresentationStyle,
//                             _of_getcolumntype [:L72] as GetColumnType and _of_hasgroup [:L82] as
//                             HasGroup. The expansion of `style` to `PresentationStyle` follows the
//                             same clarity-expansion convention that maps _of_getcolumnprop onto
//                             GetColumnProperty there, and it additionally avoids colliding with
//                             Services/RowSelectService.cs's unrelated `Style` property, which names
//                             the ROW-SELECT style rather than the DataWindow presentation style.
//      se_cst_dw.sru          (616 lines) the host. Supplies the three broker topics this service
//                             subscribes [:L60, :L63, :L66], the suppressed-event mask and its
//                             three operations [:L88-L89, :L109-L111], and the creation and
//                             attachment sequence [:L572, :L579, :L586].
//      retcode.sru            the return-code algebra, ported to Shared.Kernel RetCode.
//      enums.sru              searched and found NOT to declare ARROWSUFFIX or any SORT_* value -
//                             they are declared on the sort service itself, so in the .NET tree
//                             they belong to this file.
//
//  ORACLE STATUS  Every `ws_objects/**` path above is READ ONLY (constraint C-C) and is the
//                 behavioural oracle for parity testing. Nothing else in the repository can
//                 adjudicate behaviour (AAP 0.1.4), so every behaviour reproduced below carries the
//                 line locator it came from.
//
//  ============================================================================================
//  THE SPLIT - THIS FILE IS ONE HALF OF n_cst_dwsvc_columnsort.sru, AND THE OTHER HALF IS DEFERRED
//  ============================================================================================
//  AAP 0.2.1.3 Correction 4 measured the five attached DataWindow services for presentational
//  dependencies and found this one IRREDUCIBLY PRESENTATIONAL IN PART, on the evidence of its
//  type-position use of the DPI conversion family: `Win32.PX2MMY(U2PY(10)) / 25.4 * 1000` at
//  :L359 and `Win32.PX2MMY(U2PY(10)) * 100` at :L361. AAP 0.4.2.5 states the ruling for this file
//  in one line - "Headless half only. DPI conversion at :L359,L361 is deferred."
//
//  WHAT SHIPS HERE. The three-state sort cycle; the sort-entry store with its find, append, rebuild
//  and collapse operations; the comma-joined sort expression; the lazily captured original sort and
//  its "?" and "!" sentinel handling; the clause builder in full; the reset and update entry points;
//  and the apply path with its event-gate save and restore.
//
//  WHAT IS DEFERRED, ITEM BY ITEM, EACH WITH THE REASON IT CANNOT CROSS THE BOUNDARY (constraint
//  C-K). All of it belongs to `_of_setarrow` [:L341-L395] and its two `SetPointer` bookends:
//      * :L355-L364  the `Describe("DataWindow.Units")` switch and its four arms - U2PY(10) at
//                    :L357, the 1/1000-inch conversion at :L359, the 1/1000-centimetre conversion
//                    at :L361 and the PBU literal 10 at :L363. Every arm converts to or from
//                    PIXELS, which is the DPI conversion family AAP 0.4.4 assigns to DesignSystem.
//      * :L367       `Long(width) / 2` - geometry arithmetic on a Describe'd width.
//      * :L368, :L385  the two font-leader y-offsets computed from that units conversion.
//      * :L351-L352  the two `Modify("Destroy " + ...)` calls, and :L377-L393 the `create text(...)`
//                    syntax construction and its `Modify(sSyntax)`. Emitting DataWindow syntax is
//                    rendering, and the two band objects it creates are presentation objects.
//      * :L379, :L388  `font.face="Marlett"` and `font.face="Arial"` - font handling.
//      * :L209, :L213  `SetPointer(HourGlass!)` and `SetPointer(Arrow!)` - a pointer shape.
//      * :L34, :L53-L54, :L65-L70  the `POINT _mouseDownPt` capture and the pixel-proximity test
//                    built on it. See DECISION 3.
//
//  HOW THE GAP IS SURFACED RATHER THAN DROPPED. Per AAP 0.8.1, where the choice is between a
//  partial implementation and a documented gap the DOCUMENTED GAP WINS - so in place of
//  `_of_setarrow`'s side effects this file produces a ColumnSortIndicatorDescriptor and publishes it
//  as DATA on Indicators. The eventual consumer is the reserved Gateway extension point
//  `/v1/design/**` (AAP 0.4.4), which is where the rendering half will live. Nothing is silently
//  omitted and nothing is approximated.
//
//  CONSTRAINT C-D IS THE DOMINANT CONSTRAINT FOR THIS FILE, AND IT IS SATISFIED BY INSPECTION AND
//  BY GREP. There is no DPI or unit conversion of any kind, no interop type, no pointer call, no
//  font or menu type, no emitted DataWindow syntax, no geometric point type, no live keyboard read,
//  no dialog, no generated contract type - and, explicitly, NO `NotImplementedException` placeholder
//  standing in for the deferred capability under any name. It is also structurally impossible to
//  breach: PowerFramework.DataServices.csproj references only Contracts, Shared.Kernel,
//  Shared.Eventful, Shared.Localization, Shared.Containers and Shared.Diagnostics, so there is no
//  DesignSystem type to reach for.
//
//  ============================================================================================
//  DECISION 1 - THE INDICATOR DESCRIPTOR CARRIES THE RAW `Describe` ANSWERS AND COMPUTES NOTHING
//               GEOMETRIC. THE SPLIT LINE IS DRAWN AT THE FIRST ARITHMETIC OPERATION, NOT AT THE
//               UNITS CONVERSION.
//  ============================================================================================
//  `_of_setarrow` reads three geometry properties [:L366-L368] and then transforms two of them: the
//  width is HALVED [:L367] and the y position is offset by twice, then once, a font leader height
//  derived from the units conversion [:L368, :L385].
//
//  The descriptor carries `Describe(colName + "_t.x")`, `Describe(colName + "_t.width")` and
//  `Describe(colName + "_t.y")` EXACTLY AS THE DATAWINDOW ANSWERED THEM, as text, and performs
//  neither transformation. Reading a property is a data-model read that the host contract already
//  publishes; halving a width and offsetting a y coordinate are geometry, and the y offset is not
//  even computable here because its operand is the deferred units conversion.
//
//  WHY NOT HALVE THE WIDTH, WHICH NEEDS NO UNITS CONVERSION? Because `String(Long(w) / 2)` is a
//  PowerScript division whose result type and rendering for an ODD width cannot be settled from the
//  repository - PowerScript `/` is real division, so the text `String()` produces for an odd operand
//  is a parity claim this refactor cannot verify without the oracle. AAP 0.1.5 requires a contract be
//  NARROWED WITH A DEFINED BEHAVIOUR rather than widened with a guess, so the raw answer travels and
//  the arithmetic stays with the half that also owns the units it is expressed in. That keeps the
//  deferred boundary at ONE place instead of two.
//
//  ============================================================================================
//  DECISION 2 - THE POSTED CALL AT :L72 BECOMES AN EXPLICITLY QUEUED CONTINUATION. THIS IS THE
//               REFACTOR'S SECOND POSTED-CALL SITE.
//  ============================================================================================
//  :L72 is `Post Event OnLButtonClicked(xpos,ypos,row,dwo)`. `Post` places the call on the WIN32
//  MESSAGE QUEUE so that it runs after the current event returns. AAP 0.6.5 records the message pump
//  as a DELIBERATE NON-PORT - a headless Linux container has no pump - and AAP 0.4.5.4 requires the
//  posted call to become an explicitly queued continuation.
//
//  THE MECHANISM IS THE ONE Domain/ValidationSession.cs ALREADY OWNS, NOT A SECOND ONE. The first
//  posted-call site in this refactor is `Post _of_PostAcceptText()` [se_cst_dw.sru:L389], and its
//  port established the discipline this file follows exactly: a Try-Queue member records the pending
//  work under a lock, a Drain member CLEARS THE SLOT BEFORE RUNNING THE BODY so the continuation
//  executes at most once even if the host drains twice, and THE HOST DRAINS IT - never a timer, never
//  a background task, never a thread-pool work item. Nothing in this file schedules anything.
//
//  WHAT DIFFERS FROM THE FIRST SITE, AND WHY. The deferred accept carries no arguments, so its slot
//  is a boolean. This one carries four [:L72], so the slot is a payload record. That is a difference
//  in what is queued, not in how.
//
//  ============================================================================================
//  DECISION 3 - THE CLICK-PROXIMITY TEST IS INVERTED INTO REQUEST DATA, BECAUSE ITS INPUT IS
//               DEFERRED PIXEL STATE THIS SERVICE CANNOT HOLD.
//  ============================================================================================
//  The oracle captures the button-DOWN position into `POINT _mouseDownPt` [:L34, :L53-L54], reads
//  and zeroes it on button-UP [:L65-L68], and admits the release as a CLICK only when both axes moved
//  by no more than two units [:L70]. The two commented alternatives at :L53-L54 -
//  `UnitsToPixels(xpos,XUnitsToPixels!)` and its y twin - show the author weighing whether to hold
//  the point in pixels; either way it is a hit test over device coordinates, which constraint C-D
//  places on the deferred side along with the rest of the geometry.
//
//  RESOLUTION: OnLButtonUp takes `withinClickTolerance` as an explicit parameter and the caller
//  states whether the release was a click. THIS IS AN INVERSION OF AN INPUT SOURCE, NOT A BEHAVIOUR
//  CHANGE - given the same answer the same branch is taken, because the branch itself is reproduced
//  unchanged. What moves is only WHO measures the two-unit tolerance. OnLButtonClk is kept so the
//  event surface stays complete and answers 0 exactly as the oracle does; with the capture deferred,
//  answering 0 IS the whole of its headless residue, and that is a complete implementation rather
//  than a stub.
//
//  ============================================================================================
//  DECISION 4 - THE CONTROL-KEY READ IS INVERTED TOO, AND IT IS SUPPLIED AT DRAIN TIME RATHER THAN
//               AT QUEUE TIME.
//  ============================================================================================
//  `KeyDown(KeyControl!)` [:L134] is a LIVE keyboard read, which a headless container cannot perform
//  and which constraint C-D forbids reaching for an interop type to satisfy. It therefore arrives as
//  an explicit `ctrlHeld` parameter, exactly as Services/RowSelectService.cs inverts its eight
//  modifier reads.
//
//  THE TIMING IS THE SUBTLE PART. The read sits INSIDE `onlbuttonclicked`, which is the POSTED event
//  - so in the oracle the keyboard is sampled when the continuation RUNS, after the button-up event
//  has already returned, and NOT when the button-up event queued it. The faithful inversion therefore
//  supplies the modifier to DrainPostedClick rather than to OnLButtonUp. Queueing the modifier at
//  button-up time would sample it one message earlier than the oracle does.
//
//  ============================================================================================
//  DECISION 5 - THE ONE-BASED ARITHMETIC IS CENTRALISED, NOT INLINED. THIS FILE IS THE DENSEST
//               SITE OF AAP 0.4.5.4 / RISK R9 IN THE SERVICE.
//  ============================================================================================
//  AAP 0.4.5.4 calls one-based to zero-based translation "the single most dangerous mechanical hazard
//  in this refactor" and requires every ported loop to go through a centralised helper or be
//  individually audited. The oracle has SIX one-based sites in 450 lines: the find scan [:L113-L118],
//  the append idiom `SortDatas[nCount + 1]` [:L120-L122], the running-ordinal scan [:L181-L185], the
//  descriptor-and-clause scan [:L187-L199], the omit-one rebuild idiom
//  `newDatas[UpperBound(newDatas) + 1]` [:L139-L143], the exclusive-clear scan [:L148-L152] and the
//  reset scan [:L238-L240].
//
//  Every one of them goes through UpperBound, EntryAt, SetEntryAt and RemoveEntryAt below, which put
//  the `- 1` in exactly one place each - so NO INLINE ONE-BASED CONVERSION SURVIVES ANYWHERE ELSE IN
//  THIS FILE. That is the stronger of the two options AAP 0.4.5.4 allows: centralised arithmetic
//  rather than arithmetic audited at each of seven sites. A silent off-by-one here would be
//  indistinguishable from a behavioural regression - it would sort by the WRONG COLUMN while still
//  producing a well-formed sort expression, which no schema check and no row count would catch.
//
//  ============================================================================================
//  DECISION 6 - THE ACCESSIBILITY OF EVERY PORTED MEMBER MATCHES THE ORACLE, WITH ONE DELIBERATE
//               WIDENING TO `internal` FOR TESTABILITY.
//  ============================================================================================
//  The oracle publishes THREE members and hides four: `of_reset` [:L45], `of_update(string)` [:L46]
//  and `of_update()` [:L47] are public; `_of_sort()` [:L44], `_of_getclause` [:L48], `_of_setarrow`
//  [:L49] and `_of_sort(string)` [:L50] are private. The four events are public, as PowerBuilder
//  events are.
//
//  GetClause IS `internal` RATHER THAN `private`, and that is the file's only accessibility
//  deviation. Constraint C-H requires it be testable as a pure function with no DataWindow, no
//  database and no UI, and AAP 0.6.7 requires a byte-exact table-driven parity matrix over it -
//  neither is reachable through a private member. `internal` plus the
//  `<InternalsVisibleTo Include="PowerFramework.DataServices.Tests" />` this project already declares
//  scopes it to the test assembly and to nothing else, so it is still absent from the service's
//  public API and the ENCAPSULATION INTENT of the oracle's `private` is preserved. The same reasoning
//  covers the four read-only observation seams (OriginalSort, CurrentSort, SortEntries, Indicators),
//  each of which projects existing private state and adds no behaviour, and the two one-based indexing
//  helpers - Domain/ValidationSession.cs's MidOneBased is `internal` for precisely the same reason,
//  because AAP 0.4.5.4 requires the centralised one-based boundary be DIRECTLY testable rather than
//  inferred from a downstream effect.
//
//  THE CONSTANTS KEEP THEIR ORACLE ACCESSIBILITY EXACTLY: ARROWSUFFIX public [:L24-L26], the three
//  SORT_* values private [:L28, :L37-L39]. They are NOT promoted to match each other.
//
//  ============================================================================================
//  DECISION 7 - THE THREE PRESERVED DEFECTS ARE LISTED HERE AND ANNOTATED AGAIN AT THEIR POINT OF
//               REPRODUCTION. CONSTRAINT C-B FORBIDS CORRECTING ANY OF THEM.
//  ============================================================================================
//      DEFECT 1  [:L188-L192]  With a single sorted column the running ordinal is never incremented,
//                              so 0 reaches every descriptor. The numeric badge is suppressed in the
//                              single-column case as a SIDE EFFECT of the `nSortCnt > 1` guard and
//                              not by any explicit test. See Sort().
//      DEFECT 2  [:L272]       `if sSort = "?" or _sOrgSort = "!"` reads the UNINITIALISED LOCAL
//                              declared at :L266, which is always the empty string - the author
//                              plainly meant `_sOrgSort = "?"`. A Describe answering "?" is therefore
//                              NOT normalised to empty; only "!" is. See Update(in string?).
//      DEFECT 3  [:L236]       Reset() answers RetCode.FAILED when there is nothing to reset, so a
//                              caller cannot tell "nothing to do" from a real failure. See Reset().
//
//  ============================================================================================
//  DECISION 8 - NO SIBLING MODEL IS IMPORTED, AND NO LOCALIZATION IS REFERENCED.
//  ============================================================================================
//  A cross-reference search across all four legacy objects behind Services/ finds ZERO live
//  references between them; the single hit is the commented FILTERSUFFIX line at :L98-L99, whose
//  anomaly is recorded at its point of reproduction. This file therefore imports no sibling.
//
//  n_cst_dwsvc_columnsort.sru contains ZERO MessageBox and zero MessageBoxEx sites - verified by
//  search - so unlike Services/RowSelectService.cs there is no dialog to convert into a structured
//  error, and this file deliberately references neither I18n nor Categories. A reader looking for a
//  localization dependency here will correctly find none.
// ==============================================================================================

using System;
using System.Collections.Generic;
using System.Globalization;

using PowerFramework.DataServices.Domain;
using PowerFramework.Shared.Kernel;

namespace PowerFramework.DataServices.Services;

/// <summary>
/// One column's contribution to the sort - the port of the nested <c>sortdata</c> structure
/// (<c>n_cst_dwsvc_columnsort.sru:L6-L13</c>).
/// </summary>
/// <param name="ColName">
/// The column name, as derived from the header text object's name by stripping its <c>"_t"</c>
/// suffix (<c>:L94-L95</c>).
/// </param>
/// <param name="SortType">
/// The sort direction: <c>0</c> none, <c>1</c> ascending, <c>2</c> descending. Declared
/// <see langword="long"/> because the oracle declares it <c>long</c>, and deliberately NOT an enum -
/// see the remarks.
/// </param>
/// <remarks>
/// <para>
/// EXACTLY TWO FIELDS, WHICH IS THE WHOLE STRUCTURE [<c>:L11-L12</c>]. There is no third field
/// anywhere in the oracle: the ordinal handed to the indicator is a LOOP-LOCAL running counter
/// [<c>:L171</c>, <c>:L190</c>] and is never stored, which is precisely why DEFECT 1 is invisible in
/// the store and observable only in the descriptor.
/// </para>
/// <para>
/// A <c>readonly record struct</c> per AAP 0.4.5.2, which maps a PowerBuilder structure onto a record
/// or readonly record struct. Immutability is not a change of behaviour: the oracle mutates
/// <c>SortDatas[n].sortType</c> in place [<c>:L127</c>, <c>:L150</c>, <c>:L239</c>] and this port
/// replaces the element with a <c>with</c> expression, which is observationally identical for a
/// value type held in a list and makes every state transition an explicit assignment.
/// </para>
/// <para>
/// <c>SortType</c> IS NOT AN ENUM. The oracle's three values are declared as <c>constant long</c>
/// [<c>:L37-L39</c>] and the dispatch at <c>:L125-L132</c> has no default arm, so an out-of-range
/// value falls through every case and is left UNCHANGED. An enum would invite an exhaustive switch,
/// and a cast of an out-of-range value would be silently legal - both of which would obscure that
/// fall-through. The clause builder has the same property: a direction outside the set yields a
/// clause with NO sort suffix [<c>:L331-L336</c>], which is a real and reachable answer.
/// </para>
/// </remarks>
public readonly record struct SortData(string ColName, long SortType);

/// <summary>
/// Everything the DEFERRED rendering half needs in order to draw, or clear, one column's sort
/// indicator - the data-shaped replacement for the side effects of <c>_of_setarrow</c>
/// (<c>n_cst_dwsvc_columnsort.sru:L341-L395</c>).
/// </summary>
/// <remarks>
/// <para>
/// THIS TYPE IS THE DOCUMENTED GAP, NOT A STUB OF IT. AAP 0.2.1.3 Correction 4 defers the indicator
/// geometry and rendering; AAP 0.8.1 requires the gap be documented rather than partially built; and
/// AAP 0.4.4 names <c>/v1/design/**</c> as the reserved Gateway extension point the eventual
/// DesignSystem service will answer on. Every field below is a value the oracle either reads or
/// composes on the HEADLESS side of that line, so a future rendering half can reconstruct
/// <c>_of_setarrow</c> from one of these without consulting this service again.
/// </para>
/// <para>
/// A PLAIN DOMAIN RECORD WITH NO GENERATED CONTRACT TYPE IN SIGHT (constraint C-A). Projecting it
/// onto a wire message belongs to <c>Grpc/</c> and <c>Endpoints/</c>; this file neither knows nor
/// needs to know the wire shape.
/// </para>
/// <para>
/// EVERY ENTRY IN THE STORE PRODUCES ONE OF THESE, INCLUDING THE UNSORTED ONES. <c>:L193</c> calls
/// <c>_of_SetArrow</c> unconditionally, and that routine DESTROYS BOTH BAND OBJECTS
/// [<c>:L351-L352</c>] BEFORE returning early on an unsorted column [<c>:L353</c>]. An unsorted
/// column therefore has real work to do - clearing - so its descriptor is a CLEAR INSTRUCTION rather
/// than an absence. See <see cref="ClearOnly"/>.
/// </para>
/// </remarks>
public sealed record ColumnSortIndicatorDescriptor
{
    /// <summary>The column the indicator belongs to.</summary>
    public required string ColumnName { get; init; }

    /// <summary>
    /// The name of the arrow band object - <c>colName + ARROWSUFFIX</c> (<c>:L348</c>).
    /// </summary>
    /// <remarks>
    /// Composed here rather than by the rendering half because it is the DESTROY target as well as
    /// the CREATE target [<c>:L351</c> and <c>:L379</c>], so a clear-only descriptor needs it too.
    /// </remarks>
    public required string ArrowObjectName { get; init; }

    /// <summary>
    /// The name of the ordinal-badge band object - <c>colName + "_idx_" + ARROWSUFFIX</c>
    /// (<c>:L349</c>).
    /// </summary>
    /// <remarks>
    /// NOTE THE INFIX. It is <c>"_idx_"</c> BETWEEN the column name and the suffix, so the composed
    /// name is not the arrow name with anything appended. Composing it as
    /// <c>ArrowObjectName + "_idx_"</c> would produce a different string and would leave a stale
    /// badge object behind on every re-sort.
    /// </remarks>
    public required string IndexObjectName { get; init; }

    /// <summary>The sort direction this descriptor was produced for (<c>:L341</c>).</summary>
    public required long SortType { get; init; }

    /// <summary>
    /// The running ordinal the oracle handed to <c>_of_setarrow</c> (<c>:L193</c>).
    /// </summary>
    /// <remarks>
    /// *** THIS IS WHERE DEFECT 1 IS OBSERVABLE. *** With a single sorted column the ordinal is never
    /// incremented [<c>:L188-L192</c>], so this is <c>0</c> and the badge is suppressed. With two or
    /// more it is the one-based position among the SORTED columns. An unsorted column receives
    /// whatever the counter currently holds - the previous sorted column's ordinal, NOT zero - which
    /// is harmless because such a descriptor clears rather than draws, but is reproduced exactly
    /// because it is what the oracle passes.
    /// </remarks>
    public required int Index { get; init; }

    /// <summary>
    /// <see langword="true"/> when the only action is to destroy both band objects - the port of the
    /// early return at <c>:L353</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see langword="true"/> exactly when <see cref="SortType"/> is the unsorted value. When it is
    /// set, <see cref="ArrowGlyph"/> is empty, <see cref="IndexLabel"/> is <see langword="null"/> and
    /// the three source values are <see langword="null"/> - because the oracle RETURNS before reading
    /// them, so this port must not read them either. A rendering half must destroy both objects and
    /// stop.
    /// </para>
    /// <para>
    /// THE DESTROY IS UNCONDITIONAL EVEN WHEN THIS IS <see langword="false"/> [<c>:L351-L352</c>
    /// precede <c>:L353</c>]. A draw is therefore always destroy-then-create, never create-in-place.
    /// </para>
    /// </remarks>
    public required bool ClearOnly { get; init; }

    /// <summary>
    /// The direction glyph - <c>"t"</c> ascending, <c>"u"</c> descending (<c>:L370-L375</c>), or the
    /// empty string when <see cref="ClearOnly"/> is set.
    /// </summary>
    /// <remarks>
    /// A SINGLE LATIN LETTER THAT IS NOT READ AS A LETTER. The oracle renders it in the Marlett
    /// symbol font [<c>:L379</c>], in which <c>t</c> is an up arrow and <c>u</c> a down arrow. The
    /// font selection is the deferred half's business - this file names no font - but the glyph
    /// characters travel because they are the oracle's chosen encoding of the direction and any
    /// substitute would render differently.
    /// </remarks>
    public string ArrowGlyph { get; init; } = string.Empty;

    /// <summary>
    /// The ordinal badge text, or <see langword="null"/> when the oracle emits no badge object at all
    /// (<c>:L383-L384</c>).
    /// </summary>
    /// <remarks>
    /// SIX LEADING SPACES, AND THEY ARE PART OF THE VALUE. <c>:L384</c> is
    /// <c>"      " + String(index)</c>, and the padding is how the oracle nudges the badge clear of
    /// the arrow inside a right-aligned text object - so trimming it would move the badge. It is
    /// <see langword="null"/> rather than empty when the ordinal is not positive, because
    /// <c>:L383</c> gates the ENTIRE second band object on <c>index &gt; 0</c>: there is no badge to
    /// draw, which is a different statement from a badge with no text. DEFECT 1 makes that the normal
    /// case for a single sorted column.
    /// </remarks>
    public string? IndexLabel { get; init; }

    /// <summary>
    /// The arrow colour, as the oracle's decimal text (<c>:L344</c>, <c>ARROWCOLOR</c>, the window
    /// text colour).
    /// </summary>
    public required string ArrowColor { get; init; }

    /// <summary>
    /// The badge colour, as the oracle's decimal text (<c>:L345</c>, <c>INDEXCOLOR</c>).
    /// </summary>
    public required string IndexColor { get; init; }

    /// <summary>
    /// The background colour both objects use, as the oracle's decimal text (<c>:L346</c>,
    /// <c>TRANSPARENT</c>, applied at <c>:L381</c> and <c>:L390</c>).
    /// </summary>
    public required string TransparentColor { get; init; }

    /// <summary>
    /// The RAW answer to <c>Describe(colName + "_t.x")</c> (<c>:L366</c>), or
    /// <see langword="null"/> when <see cref="ClearOnly"/> is set.
    /// </summary>
    /// <remarks>
    /// Untransformed text, per DECISION 1 in the file header. The oracle passes this one through
    /// unchanged too [<c>:L378</c>, <c>:L387</c>], so no transformation is owed to anyone here.
    /// </remarks>
    public string? SourceX { get; init; }

    /// <summary>
    /// The RAW answer to <c>Describe(colName + "_t.width")</c> (<c>:L367</c>), or
    /// <see langword="null"/> when <see cref="ClearOnly"/> is set.
    /// </summary>
    /// <remarks>
    /// *** NOT HALVED. *** The oracle emits <c>String(Long(this) / 2)</c>, and that halving is
    /// DEFERRED with the rest of the geometry for the reason DECISION 1 gives: the text PowerScript
    /// renders for an odd operand is a parity claim this refactor cannot settle from the repository.
    /// A rendering half must halve it; a reader must not mistake this value for the emitted width.
    /// </remarks>
    public string? SourceWidth { get; init; }

    /// <summary>
    /// The RAW answer to <c>Describe(colName + "_t.y")</c> (<c>:L368</c>, re-read at <c>:L385</c>),
    /// or <see langword="null"/> when <see cref="ClearOnly"/> is set.
    /// </summary>
    /// <remarks>
    /// *** NOT OFFSET. *** The oracle subtracts twice the font leader height for the arrow
    /// [<c>:L368</c>] and once for the badge [<c>:L385</c>], both derived from the DEFERRED units
    /// conversion at <c>:L355-L364</c>, so neither offset is computable on this side of the boundary.
    /// The single raw read serves both, exactly as the oracle re-reads the same property twice.
    /// </remarks>
    public string? SourceY { get; init; }
}

/// <summary>
/// The HEADLESS HALF of the DataWindow column-sort service - the managed port of
/// <c>n_cst_dwsvc_columnsort</c> (<c>ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_columnsort.sru</c>,
/// 450 lines). It builds and applies sort expressions and publishes an indicator DESCRIPTOR; it does
/// not, and must not, render anything.
/// </summary>
/// <remarks>
/// <para>
/// SCOPE. Read the file header first: the split between what ships here and what is deferred to the
/// reserved <c>/v1/design/**</c> Gateway extension point is enumerated item by item there, with the
/// legacy locator and the reason for each, and the three preserved defects are listed under
/// DECISION 7.
/// </para>
/// <para>
/// ATTACHMENT. Derives from <see cref="DataWindowServiceBase"/> and receives its host through the
/// inherited <see cref="DataWindowServiceBase.OnInit"/>, which is the oracle's own attachment
/// contract - <c>se_cst_dw.sru:L572</c> creates the service and <c>:L579</c> raises <c>OnInit</c> on
/// it. That indirection IS the injected abstraction constraint C-H asks for: a test attaches a
/// headless double and every path below becomes exercisable with no DataWindow, no database and no
/// user interface.
/// </para>
/// <para>
/// THE CONSTRUCTOR IS THE IMPLICIT ONE, AND THAT IS DELIBERATE. There is no <c>ColumnSort</c> group
/// in <c>Configuration/DataServicesOptions.cs</c> because the oracle has no configurable default for
/// this service to bind - unlike the row-select service, which defaults its style at
/// <c>n_cst_dwsvc_rowselect.sru:L25</c>. Inventing an options group would fabricate a setting
/// (constraint C-B).
/// </para>
/// <para>
/// THE GLOBAL AUTO-INSTANCE AT <c>:L21</c> IS NOT REPRODUCED. <c>global n_cst_dwsvc_columnsort
/// n_cst_dwsvc_columnsort</c> declares a global variable that SHADOWS ITS OWN TYPE NAME - one of the
/// two occurrences AAP 0.4.5.1 records. Per that section the type keeps the descriptive .NET name and
/// the instance becomes an ordinary dependency the host owns [<c>se_cst_dw.sru:L82</c>], never a
/// global and never a static mutable.
/// </para>
/// <para>
/// THREAD AFFINITY. One instance serves one DataWindow and, like the oracle, is not designed for
/// concurrent entry - the sort store and the two expression strings carry no synchronisation because
/// the oracle's own event dispatch is serial. The single exception is the posted-click slot, which
/// takes a lock because a server-held instance can be drained from a different request than the one
/// that queued it; that is the same narrow discipline
/// <c>Domain/ValidationSession.cs</c> applies to its own continuation slot.
/// </para>
/// <para>
/// THE INHERITANCE EDGE AT <c>:L4</c> AND <c>:L15</c> - <c>global type n_cst_dwsvc_columnsort from
/// n_cst_dwsvc</c> - is the base-class declaration above, and it is a REAL edge rather than a
/// convenience: three of the base's protected helpers are consumed here. Unlike <c>se_cst_dw</c>,
/// whose parent <c>se_cst_datawindow</c> lives in the deferred DesignSystem library and is therefore
/// reference-only (AAP 0.2.1.3 Correction 3), this parent is fully in scope and fully ported.
/// </para>
/// <para>
/// THE CONSTRUCTION AND DESTRUCTION CLAUSES AT <c>:L433-L439</c> ARE DELIBERATELY NOT PORTED. Both are
/// bare <c>call super::create</c> and <c>call super::destroy</c> with no body of their own - the
/// PowerBuilder object-lifecycle boilerplate every class in the estate carries. C# runs base
/// constructors and finalizers implicitly, so there is nothing to reproduce; this class needs no
/// constructor at all and holds no unmanaged resource, so it implements no disposal either. Adding an
/// empty constructor to mirror the clause would be ceremony, not parity.
/// </para>
/// </remarks>
public sealed class ColumnSortModel : DataWindowServiceBase
{
    // ==========================================================================================
    //  CONSTANTS - SPELLINGS AND ACCESSIBILITY BOTH PRESERVED VERBATIM (AAP 0.4.5.3, DECISION 6)
    //  ----------------------------------------------------------------------------------------
    //  These identifiers appear in serialized payloads, in log records and in characterization
    //  recordings, so a rename would not restyle a symbol - it would silently invalidate every
    //  stored comparison that mentions it. The root .editorconfig therefore carries a scoped
    //  CA1707/IDE1006 suppression for THIS FILE; the correct response to a naming diagnostic here is
    //  the suppression, never a rename.
    //
    //  A search of ws_objects/pfw.shared.pbl.src/enums.sru for ARROWSUFFIX and for any SORT_* name
    //  returns nothing: the oracle declares all four on the sort service itself, so in the .NET tree
    //  they belong to this file rather than to Shared.Kernel's constant catalogue. That is the same
    //  finding the row-select service's RS_* constants produced.
    // ==========================================================================================

    /// <summary>
    /// The suffix both indicator band objects carry - <c>"_arw"</c>
    /// (<c>n_cst_dwsvc_columnsort.sru:L26</c>).
    /// </summary>
    /// <remarks>
    /// PUBLIC IN THE ORACLE [<c>:L24-L26</c>] AND PUBLIC HERE, unlike the three <c>SORT_*</c> values
    /// immediately below which are private in both. The asymmetry is the oracle's and is not
    /// levelled: the dormant code at <c>:L96-L97</c> shows the author intended external callers to
    /// recognise an indicator object by this suffix, which is exactly why it is published.
    /// </remarks>
    public const string ARROWSUFFIX = "_arw";

    /// <summary>The column is not sorted - <c>0</c> (<c>:L37</c>). Private in the oracle.</summary>
    private const long SORT_NONE = 0;

    /// <summary>Ascending - <c>1</c> (<c>:L38</c>). Private in the oracle.</summary>
    private const long SORT_ASC = 1;

    /// <summary>Descending - <c>2</c> (<c>:L39</c>). Private in the oracle.</summary>
    private const long SORT_DESC = 2;

    // ------------------------------------------------------------------------------------------
    //  THE BROKER TOPICS THIS SERVICE SUBSCRIBES
    //  ----------------------------------------------------------------------------------------
    //  Values taken verbatim from se_cst_dw.sru, where they are constants on the HOST that the
    //  oracle reaches as `#DataWindow.EVT_*` [:L442-L444]: :L60 EVT_CLICKED, :L63 EVT_DOUBLECLICKED,
    //  :L66 EVT_LBUTTONUP. Declared privately here for the same reason
    //  Services/RowSelectService.cs declares its three privately - a subscriber needs the topic
    //  NAME, not a dependency on the declaring object, and none of these three carries the lexical
    //  ordering prefix that AAP 0.6.1.2 decomposes for the item-changed and edit-changed topics.
    // ------------------------------------------------------------------------------------------

    /// <summary>The single-click topic - <c>"clicked"</c> (<c>se_cst_dw.sru:L60</c>).</summary>
    private const string EVT_CLICKED = "clicked";

    /// <summary>The double-click topic - <c>"doubleclicked"</c> (<c>se_cst_dw.sru:L63</c>).</summary>
    private const string EVT_DOUBLECLICKED = "doubleclicked";

    /// <summary>The button-release topic - <c>"lbuttonup"</c> (<c>se_cst_dw.sru:L66</c>).</summary>
    private const string EVT_LBUTTONUP = "lbuttonup";

    // ------------------------------------------------------------------------------------------
    //  THE INDICATOR LITERALS                                n_cst_dwsvc_columnsort.sru:L344-L346
    //  ----------------------------------------------------------------------------------------
    //  Declared `constant string` LOCAL TO `_of_setarrow` in the oracle, so they are private here -
    //  narrowest accessibility that reproduces the oracle's scope. Their VALUES travel to the
    //  deferred half as descriptor properties, which is how the gap is surfaced as data without
    //  publishing the constants themselves.
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// The arrow colour - <c>"33554432"</c>, the window text colour (<c>:L344</c>).
    /// </summary>
    /// <remarks>
    /// Carried as the oracle's DECIMAL TEXT rather than parsed into a colour value. It is destined for
    /// a <c>color="..."</c> attribute in generated DataWindow syntax, so text is its native form; and
    /// this file must not acquire a colour type, colour arithmetic or a theme lookup, all of which
    /// AAP 0.4.4 assigns to the deferred DesignSystem service.
    /// </remarks>
    private const string ARROWCOLOR = "33554432";

    /// <summary>
    /// The ordinal-badge colour - the oracle's <c>String(RGB(150,150,150))</c> (<c>:L345</c>).
    /// </summary>
    /// <remarks>
    /// *** THE VALUE IS CARRIED, NOT THE FUNCTION. *** PowerScript composes a colour as
    /// <c>red + green * 256 + blue * 65536</c>, so <c>RGB(150,150,150)</c> is
    /// <c>150 + 38400 + 9830400 = 9868950</c>. <c>RGB</c> itself is a COLOUR FUNCTION, and AAP 0.4.4
    /// assigns the colour functions to the deferred DesignSystem service - so porting it here would
    /// breach constraint C-D for a value that is fixed at compile time in the oracle too, where it is
    /// a <c>constant string</c>. The arithmetic is written out above so the literal is verifiable
    /// without the oracle, and a unit test re-derives it from the three components.
    /// </remarks>
    private const string INDEXCOLOR = "9868950";

    /// <summary>
    /// The background colour both objects use - <c>"536870912"</c>, transparent (<c>:L346</c>).
    /// </summary>
    private const string TRANSPARENT = "536870912";

    // ------------------------------------------------------------------------------------------
    //  THE REMAINING ORACLE LITERALS, NAMED RATHER THAN INLINED
    //  ----------------------------------------------------------------------------------------
    //  Every one is compared or emitted BYTE FOR BYTE. Naming them puts each in one place and makes
    //  the parity matrix assert against the same symbol the implementation uses, which is what stops
    //  a "harmless" reformatting from silently changing a generated sort clause.
    // ------------------------------------------------------------------------------------------

    /// <summary>The ascending suffix - a SINGLE LEADING SPACE then <c>A</c> (<c>:L333</c>).</summary>
    private const string AscendingSuffix = " A";

    /// <summary>The descending suffix - a SINGLE LEADING SPACE then <c>D</c> (<c>:L335</c>).</summary>
    private const string DescendingSuffix = " D";

    /// <summary>The separator between clauses in the composed sort expression (<c>:L196</c>).</summary>
    private const string ClauseSeparator = ",";

    /// <summary>
    /// The sentinel meaning "there was no original sort" - <c>"?"</c> (<c>:L274</c>).
    /// </summary>
    /// <remarks>
    /// DELIBERATELY OVERLOADED, AND BOTH READINGS ARE LIVE. It is also what PowerBuilder's
    /// <c>Describe</c> answers for a property it cannot determine, so the same text arrives from two
    /// sources: assigned by <c>:L274</c> to record that the DataWindow had no sort, and returned by
    /// the DataWindow itself. <c>:L202</c> and <c>:L278</c> test it as the FORMER, while <c>:L403</c>
    /// normalises it as the LATTER. The two uses must stay distinct - collapsing them would make an
    /// undeterminable Describe indistinguishable from a genuinely unsorted DataWindow.
    /// </remarks>
    private const string NoOriginalSortSentinel = "?";

    /// <summary>
    /// The answer a DataWindow gives for an invalid expression - <c>"!"</c> (<c>:L272</c>,
    /// <c>:L403</c>).
    /// </summary>
    private const string InvalidExpressionSentinel = "!";

    /// <summary>The header text object's name suffix - <c>"_t"</c> (<c>:L94</c>).</summary>
    private const string HeaderTextSuffix = "_t";

    /// <summary>The infix in the badge object's name (<c>:L349</c>).</summary>
    private const string IndexObjectInfix = "_idx_";

    /// <summary>
    /// The badge text's leading padding - SIX SPACES (<c>:L384</c>). Part of the value, not layout
    /// slack; see <see cref="ColumnSortIndicatorDescriptor.IndexLabel"/>.
    /// </summary>
    private const string IndexLabelPadding = "      ";

    /// <summary>The ascending glyph, rendered by the deferred half in a symbol font (<c>:L372</c>).</summary>
    private const string AscendingGlyph = "t";

    /// <summary>The descending glyph (<c>:L374</c>).</summary>
    private const string DescendingGlyph = "u";

    /// <summary>The band a sortable header text object must live in (<c>:L85</c>).</summary>
    private const string HeaderBand = "header";

    /// <summary>The band the column itself must live in (<c>:L105</c>).</summary>
    private const string DetailBand = "detail";

    /// <summary>The property carrying the DataWindow's current sort expression (<c>:L177</c>).</summary>
    private const string TableSortProperty = "DataWindow.Table.Sort";

    // ==========================================================================================
    //  STATE                                                  n_cst_dwsvc_columnsort.sru:L28-L34
    //  ----------------------------------------------------------------------------------------
    //  Three of the oracle's four private fields are reproduced. The fourth, `POINT _mouseDownPt`
    //  [:L34], is DEFERRED HIT-TEST STATE and is deliberately absent - there is no point-like type
    //  here, no pixel arithmetic and no geometric field of any kind. DECISION 3 in the file header
    //  records how the test it fed is satisfied instead.
    // ==========================================================================================

    /// <summary>
    /// The sort store - the port of <c>SORTDATA SortDatas[]</c> (<c>:L29</c>).
    /// </summary>
    /// <remarks>
    /// A <see cref="List{T}"/> because the oracle's array is grown by assignment past its upper bound
    /// [<c>:L120-L122</c>], rebuilt into a fresh array [<c>:L137-L144</c>], replaced wholesale by an
    /// empty one [<c>:L155-L156</c>] and replaced by a one-element one [<c>:L158-L160</c>]. All four
    /// are list operations. INDEXING IS ONE-BASED THROUGHOUT the ported logic and goes through
    /// <see cref="UpperBound"/> and <see cref="EntryAt"/> - see DECISION 5.
    /// </remarks>
    private readonly List<SortData> _sortDatas = [];

    /// <summary>
    /// The descriptors produced by the most recent pass - the data-shaped replacement for
    /// <c>_of_setarrow</c>'s side effects.
    /// </summary>
    /// <remarks>
    /// REBUILT WHOLE ON EVERY PASS, never appended to across passes, because the oracle's equivalent
    /// side effects are also complete on every pass: <c>:L187-L199</c> visits EVERY entry in the store
    /// and destroys both band objects for each [<c>:L351-L352</c>] before deciding whether to create
    /// anything. A stale descriptor surviving a pass would describe an indicator the oracle had
    /// already destroyed.
    /// </remarks>
    private readonly List<ColumnSortIndicatorDescriptor> _indicators = [];

    /// <summary>
    /// The sort expression this service built from the store - the port of <c>string _sSort</c>
    /// (<c>:L31</c>).
    /// </summary>
    /// <remarks>
    /// The empty string means the user has cleared every column, which is a MEANINGFUL state and the
    /// trigger for falling back to <see cref="_sOrgSort"/> [<c>:L201-L204</c>]. It is also the
    /// early-out guard in <see cref="Update(in string?)"/> [<c>:L276</c>].
    /// </remarks>
    private string _sSort = string.Empty;

    /// <summary>
    /// The DataWindow's original sort expression, captured lazily - the port of
    /// <c>string _sOrgSort</c> (<c>:L32</c>).
    /// </summary>
    /// <remarks>
    /// THREE DISTINCT STATES, NOT TWO: empty means "not captured yet" and is what triggers the lazy
    /// capture [<c>:L176-L178</c>]; <see cref="NoOriginalSortSentinel"/> means "captured, and there
    /// was none" [<c>:L274</c>]; anything else is the captured expression. Collapsing the first two
    /// would make the capture happen on every pass and would overwrite the original with whatever the
    /// user's sort had just set.
    /// </remarks>
    private string _sOrgSort = string.Empty;

    // ------------------------------------------------------------------------------------------
    //  THE POSTED-CLICK SLOT                                          :L72, and DECISION 2 above
    // ------------------------------------------------------------------------------------------

    /// <summary>Guards <see cref="_postedClick"/> only. See the class remarks on thread affinity.</summary>
    private readonly object _postedClickGate = new();

    /// <summary>
    /// The queued continuation's arguments, or <see langword="null"/> when nothing is queued.
    /// </summary>
    private PostedClickArgs? _postedClick;

    /// <summary>
    /// The four arguments <c>Post Event OnLButtonClicked(xpos,ypos,row,dwo)</c> carries
    /// (<c>:L72</c>).
    /// </summary>
    /// <param name="XPos">The pointer x position, NARROWED TO <see langword="int"/> - see the remarks.</param>
    /// <param name="YPos">The pointer y position, narrowed the same way.</param>
    /// <param name="Row">The row under the pointer.</param>
    /// <param name="Dwo">The object under the pointer.</param>
    /// <remarks>
    /// THE NARROWING IS THE ORACLE'S AND IS REPRODUCED RATHER THAN TIDIED. The button-up event
    /// receives <c>long</c> coordinates and the posted event declares them <c>integer</c>
    /// [<c>:L18</c>], so the post itself narrows. AAP 0.4.5.2 maps <c>integer</c> onto
    /// <see langword="int"/>, and the conversion is written <c>unchecked</c> at the queue site so it
    /// truncates rather than raising - which is what a PowerScript assignment does. It is unobservable
    /// in practice because the live path never reads either coordinate, but the declared signature is
    /// the contract and is preserved on that basis.
    /// </remarks>
    private readonly record struct PostedClickArgs(int XPos, int YPos, long Row, IDataWindowObject Dwo);

    // ==========================================================================================
    //  THE CENTRALISED ONE-BASED INDEXING HELPERS                          AAP 0.4.5.4 / RISK R9
    //  ----------------------------------------------------------------------------------------
    //  Two members, and every ported scan, append and rebuild below goes through them so the `- 1`
    //  exists in exactly one place. DECISION 5 in the file header enumerates the seven sites.
    // ==========================================================================================

    /// <summary>
    /// The LAST VALID ONE-BASED INDEX of a list - the port of PowerScript <c>UpperBound(array)</c>.
    /// </summary>
    /// <param name="entries">The list to measure.</param>
    /// <returns>
    /// The count, which for a one-based array IS the last valid index. <c>0</c> for an empty list, and
    /// that is not a sentinel - it is the honest answer, and it is what
    /// <c>if nCurrentIndex = 0</c> [<c>:L119</c>] and <c>if nCount = 0</c> [<c>:L236</c>] both rely
    /// on.
    /// </returns>
    /// <remarks>
    /// THE WHOLE HAZARD IN ONE SENTENCE: PowerScript's <c>UpperBound</c> answers the LAST INDEX while
    /// .NET's <c>Count</c> answers ONE PAST THE LAST INDEX - and for a one-based array those are the
    /// same number. So this looks like a pointless wrapper and is not one: it is the place the
    /// equivalence is asserted, named and tested, instead of being re-derived at seven call sites.
    /// </remarks>
    internal static int UpperBound(List<SortData> entries)
    {
        return entries.Count;
    }

    /// <summary>
    /// Reads one entry by its ONE-BASED index - the port of <c>SortDatas[n]</c>.
    /// </summary>
    /// <param name="entries">The list to read from.</param>
    /// <param name="oneBasedIndex">
    /// The one-based index. The first entry is <c>1</c>, not <c>0</c>.
    /// </param>
    /// <returns>The entry at that position.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="oneBasedIndex"/> is less than <c>1</c> or greater than
    /// <see cref="UpperBound"/>.
    /// </exception>
    /// <remarks>
    /// THE RANGE CHECK IS A FAIL-FAST SUBSTITUTION, NOT A NEW BEHAVIOUR. PowerScript raises on an
    /// out-of-range array read too; naming the argument makes the failure legible instead of surfacing
    /// as a bare index fault one frame deeper. No ported call site can reach it - every one is bounded
    /// by <see cref="UpperBound"/> - so it exists to catch a future editing mistake, which is exactly
    /// the mistake AAP 0.4.5.4 warns is indistinguishable from a behavioural regression.
    /// </remarks>
    internal static SortData EntryAt(List<SortData> entries, int oneBasedIndex)
    {
        if (oneBasedIndex < 1 || oneBasedIndex > UpperBound(entries))
        {
            throw new ArgumentOutOfRangeException(
                nameof(oneBasedIndex),
                oneBasedIndex,
                "A one-based sort-entry index must be between 1 and UpperBound inclusive.");
        }

        return entries[oneBasedIndex - 1];
    }

    /// <summary>
    /// Replaces one entry by its ONE-BASED index - the port of an assignment to
    /// <c>SortDatas[n].sortType</c>.
    /// </summary>
    /// <param name="entries">The list to write to.</param>
    /// <param name="oneBasedIndex">The one-based index. The first entry is <c>1</c>, not <c>0</c>.</param>
    /// <param name="entry">The replacement entry.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="oneBasedIndex"/> is out of range.
    /// </exception>
    /// <remarks>
    /// A WHOLE-ELEMENT REPLACEMENT WHERE THE ORACLE MUTATES ONE FIELD IN PLACE
    /// [<c>:L127</c>, <c>:L150</c>, <c>:L239</c>]. That is observationally identical for a value type
    /// held in a list, and it makes every state transition an explicit assignment rather than a hidden
    /// field write. It exists so that NO inline <c>- 1</c> survives anywhere outside these three
    /// helpers, which is the stronger of the two options AAP 0.4.5.4 allows - centralised arithmetic
    /// rather than arithmetic audited at each site.
    /// </remarks>
    internal static void SetEntryAt(List<SortData> entries, int oneBasedIndex, SortData entry)
    {
        // The range check is EntryAt's, reused rather than duplicated, so both directions of the
        // boundary are defined in exactly one place.
        _ = EntryAt(entries, oneBasedIndex);

        entries[oneBasedIndex - 1] = entry;
    }

    /// <summary>
    /// Removes one entry by its ONE-BASED index - the port of the omit-one rebuild at
    /// <c>:L137-L144</c>.
    /// </summary>
    /// <param name="entries">The list to remove from.</param>
    /// <param name="oneBasedIndex">The one-based index. The first entry is <c>1</c>, not <c>0</c>.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="oneBasedIndex"/> is out of range.
    /// </exception>
    /// <remarks>
    /// The oracle allocates a fresh array and appends every entry whose POSITION differs from the one
    /// being dropped, using the <c>newDatas[UpperBound(newDatas) + 1]</c> idiom. Removing in place is
    /// the same operation and preserves the surviving entries' relative order, which the composed sort
    /// expression depends on. The comparison is by POSITION and not by column name, exactly as at
    /// <c>:L140</c>.
    /// </remarks>
    internal static void RemoveEntryAt(List<SortData> entries, int oneBasedIndex)
    {
        _ = EntryAt(entries, oneBasedIndex);

        entries.RemoveAt(oneBasedIndex - 1);
    }

    // ==========================================================================================
    //  OBSERVATION SEAMS - READ-ONLY PROJECTIONS OF PRIVATE STATE, ADDING NO BEHAVIOUR
    //  ----------------------------------------------------------------------------------------
    //  `internal` per DECISION 6: the InternalsVisibleTo this project declares scopes them to the
    //  test assembly, so they are absent from the service's public API and the oracle's `private`
    //  encapsulation intent survives. Constraint C-H needs them - two of the three defect-pinning
    //  tests assert on state the oracle keeps private, and a test that could only infer that state
    //  from a downstream effect would keep passing after the defect was "fixed".
    // ==========================================================================================

    /// <summary>
    /// The sort store, in order - a read-only view of <c>SortDatas[]</c> (<c>:L29</c>).
    /// </summary>
    /// <remarks>
    /// ZERO-BASED, because it is an ordinary .NET sequence handed to a caller rather than a ported
    /// PowerScript array. The one-based discipline of DECISION 5 governs the PORTED LOGIC inside this
    /// class; exporting a one-based view would push that convention onto every consumer and invite
    /// exactly the off-by-one it exists to prevent.
    /// </remarks>
    internal IReadOnlyList<SortData> SortEntries => _sortDatas;

    /// <summary>
    /// The descriptors the most recent pass produced, one per store entry, in store order.
    /// </summary>
    /// <remarks>
    /// This is the surface the DEFERRED rendering half consumes, and the reason the gap is a documented
    /// hand-off rather than an omission. Empty until the first pass; see <see cref="_indicators"/> for
    /// why it is rebuilt whole each time.
    /// </remarks>
    internal IReadOnlyList<ColumnSortIndicatorDescriptor> Indicators => _indicators;

    /// <summary>
    /// The sort expression built from the store - <c>_sSort</c> (<c>:L31</c>).
    /// </summary>
    internal string CurrentSort => _sSort;

    /// <summary>
    /// The lazily captured original sort - <c>_sOrgSort</c> (<c>:L32</c>).
    /// </summary>
    /// <remarks>
    /// *** THE OBSERVATION POINT FOR DEFECT 2. *** A test drives <see cref="Update()"/> with a
    /// <c>Describe</c> that answers <see cref="NoOriginalSortSentinel"/> and asserts this is still that
    /// sentinel rather than empty, which pins the uninitialised-local read at <c>:L272</c>. Remove the
    /// defect and that assertion fails, which is the point.
    /// </remarks>
    internal string OriginalSort => _sOrgSort;

    /// <summary>
    /// Whether a posted click is waiting to be drained - see DECISION 2.
    /// </summary>
    /// <remarks>
    /// The analogue of <c>ValidationSession.DeferredAcceptPending</c>, and public for the same reason:
    /// outstanding continuation work must be OBSERVABLE rather than invisible, because a server-held
    /// instance can be torn down while the oracle's control never could be.
    /// </remarks>
    public bool PostedClickPending
    {
        get
        {
            lock (_postedClickGate)
            {
                return _postedClick is not null;
            }
        }
    }

    // ==========================================================================================
    //  THE FOUR EVENTS                                        n_cst_dwsvc_columnsort.sru:L16-L19
    //  ----------------------------------------------------------------------------------------
    //  THREE ARE BROKER-WIRED AND ONE IS NOT, WHICH IS THE FILE'S MOST LOAD-BEARING STRUCTURAL FACT
    //  after the split itself. `onenable` subscribes onLButtonClk, onLButtonDblClk and onLButtonUp
    //  [:L442-L444] and DELIBERATELY DOES NOT SUBSCRIBE onlbuttonclicked - it is reached ONLY through
    //  the posted continuation at :L72. A reader searching for a fourth subscription will correctly
    //  find none, and adding one would fire the whole sort cycle on every raw click, bypassing both
    //  the grid-style guard and the click-proximity test.
    //
    //  All four answer `long` and all four answer 0 on every path, because the raw pbm_dwn* events
    //  they port are consumed by se_cst_dw's `= 1 then return 1` prevent convention. None of them
    //  prevents anything, ever - the oracle has no `return 1` anywhere in this file.
    // ==========================================================================================

    /// <summary>
    /// The raw left-button-down handler - the port of <c>event onlbuttonclk pbm_dwnlbuttonclk</c>
    /// (<c>:L16</c>, body <c>:L53-L57</c>). Answers <c>0</c>.
    /// </summary>
    /// <param name="xpos">The pointer x position, in the units the DataWindow reports.</param>
    /// <param name="ypos">The pointer y position, in the units the DataWindow reports.</param>
    /// <param name="row">The one-based row under the pointer, or <c>0</c> when none.</param>
    /// <param name="dwo">The object under the pointer.</param>
    /// <returns><c>0</c>, always - the oracle's only exit [<c>:L56</c>].</returns>
    /// <remarks>
    /// <para>
    /// THE BODY IS THE DEFERRED HIT-TEST CAPTURE, AND WHAT REMAINS IS COMPLETE RATHER THAN STUBBED.
    /// The oracle's entire body is two assignments into <c>POINT _mouseDownPt</c> [<c>:L53-L54</c>]
    /// followed by <c>return 0</c> [<c>:L56</c>]. Constraint C-D places that point - and the pixel
    /// proximity test it feeds - on the deferred side, so this port holds no pixel state and performs
    /// no coordinate arithmetic. Answering <c>0</c> IS the whole of the headless residue. There is
    /// deliberately no exception, no placeholder and no <c>NotImplementedException</c> here under any
    /// name.
    /// </para>
    /// <para>
    /// THE MEMBER IS KEPT SO THE EVENT SURFACE STAYS COMPLETE. It is one of the three broker-wired
    /// handlers [<c>:L442</c>] and <see cref="OnLButtonDblClk"/> forwards to it [<c>:L168</c>], so
    /// removing it would break both a subscription and a forward. See DECISION 3.
    /// </para>
    /// <para>
    /// THE TWO DORMANT ALTERNATIVES ARE CARRIED AS COMMENTS AND NOT REVIVED. The oracle's own trailing
    /// comments at <c>:L53-L54</c> read <c>//UnitsToPixels(xpos,XUnitsToPixels!)</c> and
    /// <c>//UnitsToPixels(ypos,YUnitsToPixels!)</c> - the author weighing whether to hold the point in
    /// device pixels instead of DataWindow units. Either form is a unit conversion, so both are
    /// deferred; they are recorded because they are evidence that the capture is geometry rather than
    /// bookkeeping.
    /// </para>
    /// </remarks>
    public long OnLButtonClk(long xpos, long ypos, long row, IDataWindowObject dwo)
    {
        // n_cst_dwsvc_columnsort.sru:L53  _mouseDownPt.x = xpos //UnitsToPixels(xpos,XUnitsToPixels!)
        // n_cst_dwsvc_columnsort.sru:L54  _mouseDownPt.y = ypos //UnitsToPixels(ypos,YUnitsToPixels!)
        //
        // BOTH ASSIGNMENTS ARE DEFERRED, together with the commented pixel-conversion alternatives.
        // The proximity decision they exist to support arrives as request data instead - see
        // OnLButtonUp's `withinClickTolerance` and DECISION 3.

        // :L56
        return 0L;
    }

    /// <summary>
    /// The raw left-button-release handler, and the gatekeeper for the whole sort cycle - the port of
    /// <c>event onlbuttonup pbm_dwnlbuttonup</c> (<c>:L17</c>, body <c>:L59-L76</c>).
    /// </summary>
    /// <param name="xpos">The pointer x position at release.</param>
    /// <param name="ypos">The pointer y position at release.</param>
    /// <param name="row">The one-based row under the pointer, or <c>0</c> when none.</param>
    /// <param name="dwo">The object under the pointer.</param>
    /// <param name="withinClickTolerance">
    /// Whether the release counts as a CLICK rather than a drag - the caller's answer to the oracle's
    /// two-unit test at <c>:L70</c>. See DECISION 3 for why this is a parameter.
    /// </param>
    /// <returns><c>0</c> on every path.</returns>
    /// <remarks>
    /// <para>
    /// FOUR GUARDS IN A FIXED ORDER, AND THE ORDER IS OBSERVABLE because each is cheaper than the next
    /// and the last one can throw:
    /// </para>
    /// <para>
    /// 1. NOT A GRID, SO NOTHING TO SORT [<c>:L61</c>]. Column sorting is a grid-only affordance, and
    /// the test is an INEQUALITY against the grid style - so an unreadable
    /// <c>"DataWindow.Processing"</c> answers <c>STYLE_DEFAULT</c> and correctly declines to sort
    /// rather than assuming a grid.
    /// </para>
    /// <para>
    /// 2. GROUPED DATAWINDOWS DO NOT SUPPORT SORTING [<c>:L63</c>], per the oracle's own comment at
    /// <c>:L62</c>. THE ORACLE WRITES A BARE <c>return</c> HERE, with no value, while every other exit
    /// in the body writes <c>return 0</c> - and in a PowerBuilder event with a declared return type a
    /// bare return yields the type's default, which is <c>0</c>. The OBSERVABLE value is therefore
    /// identical and this port writes <c>0L</c>, which C# requires; the difference is textual and is
    /// recorded so nobody reads the bare return as a distinct outcome.
    /// </para>
    /// <para>
    /// 3. THE RELEASE MUST BE A CLICK, NOT A DRAG [<c>:L70</c>]. The oracle reads the captured point,
    /// ZEROES IT, and then compares both axes against a two-unit tolerance. The read-and-zero is not
    /// bookkeeping - it makes the captured point single-use, so a release that arrives without a
    /// matching press compares against zero and is almost certainly rejected. That whole mechanism is
    /// DEFERRED (DECISION 3): the caller measures and states the answer, and there is no point to
    /// zero because there is no point.
    /// </para>
    /// <para>
    /// 4. THE OBJECT MODEL MUST BE VALID [<c>:L71</c>]. Reproduced with the ported
    /// <c>Predicates.IsValidObject</c> against the host's object model, rather than paraphrased - the
    /// guard exists because the posted continuation reads <c>dwo.Name</c> and describes properties on a
    /// DataWindow that may have been discarded in between.
    /// </para>
    /// <para>
    /// AND THEN THE POST [<c>:L72</c>], which is the one thing this member does when every guard
    /// passes. It QUEUES; it does not dispatch. See DECISION 2.
    /// </para>
    /// </remarks>
    public long OnLButtonUp(
        long xpos,
        long ypos,
        long row,
        IDataWindowObject dwo,
        bool withinClickTolerance)
    {
        // The oracle would fault on a null object reference at :L72 when it reads dwo inside the
        // posted event; naming the argument fails just as fast and says which one was wrong, which is
        // the fail-fast posture AAP 0.1.4 requires be preserved rather than softened.
        ArgumentNullException.ThrowIfNull(dwo);

        // :L61  if _of_GetStyle() <> STYLE_GRID then return 0
        if (GetPresentationStyle() != STYLE_GRID)
        {
            return 0L;
        }

        // :L62  //*有分组的DW不支持排序   ("a grouped DataWindow does not support sorting")
        // :L63  if _of_HasGroup() then return
        //
        // A BARE `return` in the oracle, whose observable value is the default 0. See the remarks.
        if (HasGroup())
        {
            return 0L;
        }

        // :L65-L68  nXPos = _mouseDownPt.x / nYPos = _mouseDownPt.y / then both zeroed
        //
        // DEFERRED. There is no captured point to read and none to zero, because holding it would be
        // holding pixel state (constraint C-D, DECISION 3).
        //
        // :L70  if Abs(xpos - nXPos) <= 2 and Abs(ypos - nYPos) <= 2 then
        //
        // The two-unit tolerance test is the caller's to make; its ANSWER arrives as the parameter.
        // The branch itself is reproduced unchanged, so given the same answer the same path is taken.
        if (withinClickTolerance)
        {
            // :L71  if Not IsValidObject(#DataWindow.Object) then return 0
            if (!Predicates.IsValidObject(RequireHost().ObjectModel))
            {
                return 0L;
            }

            // :L72  Post Event OnLButtonClicked(xpos,ypos,row,dwo)
            //
            // QUEUED, NEVER CALLED HERE (DECISION 2). The narrowing to `int` is the oracle's own -
            // the posted event declares its coordinates `integer` at :L18 - and is written
            // `unchecked` so it truncates rather than raising, as a PowerScript assignment does.
            lock (_postedClickGate)
            {
                _postedClick = new PostedClickArgs(
                    unchecked((int)xpos),
                    unchecked((int)ypos),
                    row,
                    dwo);
            }
        }

        // :L75
        return 0L;
    }

    /// <summary>
    /// Runs the queued posted click, if one is outstanding - the continuation half of the port of
    /// <c>Post Event OnLButtonClicked(...)</c> (<c>:L72</c>).
    /// </summary>
    /// <param name="ctrlHeld">
    /// Whether the Control key is held AT THIS MOMENT - the caller's answer to
    /// <c>KeyDown(KeyControl!)</c> at <c>:L134</c>. Supplied here rather than at queue time because
    /// the oracle samples the keyboard inside the posted event; see DECISION 4.
    /// </param>
    /// <returns>
    /// What <see cref="OnLButtonClicked"/> answered, or <see langword="null"/> when nothing was
    /// queued - which is not an error and is how a caller tells "drained" from "nothing to drain".
    /// </returns>
    /// <remarks>
    /// <para>
    /// CALLED BY THE HOST, AFTER the event that queued it has answered. The slot is cleared BEFORE the
    /// body runs, so the continuation executes at most once even if the host drains twice and so a
    /// failure inside the body cannot leave the same work queued forever. That is
    /// <c>Domain/ValidationSession.cs</c>'s discipline for the refactor's first posted-call site,
    /// applied unchanged to its second.
    /// </para>
    /// <para>
    /// NOTHING HERE SCHEDULES ANYTHING - no timer, no <c>Task.Run</c>, no thread-pool work item. The
    /// deferral is expressed by the queue and by the host's decision of when to drain it, which is
    /// exactly what the Win32 message queue expressed in the oracle.
    /// </para>
    /// </remarks>
    public long? DrainPostedClick(in bool ctrlHeld)
    {
        PostedClickArgs queued;

        lock (_postedClickGate)
        {
            if (_postedClick is null)
            {
                return null;
            }

            queued = _postedClick.Value;

            // Cleared BEFORE the body runs - see the remarks.
            _postedClick = null;
        }

        return OnLButtonClicked(queued.XPos, queued.YPos, queued.Row, queued.Dwo, ctrlHeld);
    }

    /// <summary>
    /// The whole sort cycle for one header click - the port of
    /// <c>event type long onlbuttonclicked(integer xpos, integer ypos, long row, dwobject dwo)</c>
    /// (<c>:L18</c>, body <c>:L78-L166</c>). Answers <c>0</c> on every path.
    /// </summary>
    /// <param name="xpos">
    /// The pointer x position. UNUSED ON THE LIVE PATH - see the remarks.
    /// </param>
    /// <param name="ypos">The pointer y position. Unused on the live path.</param>
    /// <param name="row">The row under the pointer. Unused on the live path.</param>
    /// <param name="dwo">
    /// The object clicked. THE ONLY ARGUMENT THE LIVE PATH READS, and only for its
    /// <see cref="IDataWindowObject.Name"/> [<c>:L83</c>].
    /// </param>
    /// <param name="ctrlHeld">
    /// Whether the Control key is held - the inverted <c>KeyDown(KeyControl!)</c> read at
    /// <c>:L134</c>. See DECISION 4.
    /// </param>
    /// <returns><c>0</c> on every path - the oracle has five exits and all five answer <c>0</c>.</returns>
    /// <remarks>
    /// <para>
    /// NOT BROKER-SUBSCRIBED. It is reached only through <see cref="DrainPostedClick"/>, and it is
    /// public for the same reason the oracle declares it a custom <c>event type long</c> rather than a
    /// private function: it is a dispatch target. See the region banner above the four events.
    /// </para>
    /// <para>
    /// THREE OF THE FIVE PARAMETERS ARE UNUSED, AND THEY ARE KEPT DELIBERATELY. <c>xpos</c> is read
    /// only inside the DORMANT grid-lines guard at <c>:L90</c>, and <c>ypos</c> and <c>row</c> are read
    /// nowhere at all - the oracle declares them because <c>:L72</c> posts them. Dropping them would
    /// change the signature the post site targets and would silently discard the arguments the dormant
    /// guard would need if it were ever revived, so the declared shape is preserved exactly.
    /// </para>
    /// <para>
    /// FOUR ELIGIBILITY GUARDS, THEN THE STORE, THEN THE CYCLE, THEN ONE OF TWO BRANCHES. The two
    /// branches differ in a way that is easy to blur: the Control branch ACCUMULATES a multi-column
    /// sort and prunes only the column just cleared, while the plain branch is EXCLUSIVE - it clears
    /// every other column first and then collapses the store. Crucially BOTH call the sort pass BEFORE
    /// touching the store, because the pass is what clears the indicator objects of the columns about
    /// to be removed [<c>:L193</c> reaching <c>:L351-L353</c>]. Reordering the surgery ahead of the
    /// pass would leave orphaned indicators on screen - which is the one way this headless half can
    /// still produce a visible defect.
    /// </para>
    /// </remarks>
    public long OnLButtonClicked(
        int xpos,
        int ypos,
        long row,
        IDataWindowObject dwo,
        bool ctrlHeld)
    {
        // :L83 reads dwo.Name unconditionally, so a null would fault there. Fail fast and name it.
        ArgumentNullException.ThrowIfNull(dwo);

        DataWindowServiceHost host = RequireHost();

        // :L81  //if _of_GetStyle() <> STYLE_GRID then return 0
        //
        // DORMANT IN THE ORACLE and carried as a comment: the grid-style test lives on the button-up
        // path [:L61] instead, so reviving it here would only duplicate a guard already passed.

        // :L83  sName = dwo.Name
        string sName = dwo.Name;

        // :L84-L85  sProp = #DataWindow.Describe(sName + ".Band") / if sProp <> "header" then return 0
        string sProp = host.Describe(sName + ".Band");
        if (!string.Equals(sProp, HeaderBand, StringComparison.Ordinal))
        {
            return 0L;
        }

        // ------------------------------------------------------------------------------------------
        // :L86-L92 - DORMANT, CARRIED AS A COMMENT, NOT REVIVED (constraint C-B).
        //
        //      sProp = #DataWindow.Describe("DataWindow.Grid.Lines")
        //      if sProp = "0" or sProp = "2" then
        //          //允许拖动列大小的情况下防止被排序     ("prevent sorting while column resize is allowed")
        //          if xpos - UnitsToPixels(Long(#DataWindow.Describe(sName + ".x")),XUnitsToPixels!) < 8 then return 0
        //      end if
        //
        // It would suppress the sort when the click landed within eight PIXELS of the column's left
        // edge, where a grid drag handle sits. Two independent reasons it stays commented: it is dormant
        // in the oracle, so reviving it would ADD behaviour (constraint C-B); and its operand is
        // `UnitsToPixels`, a unit conversion that constraint C-D places on the deferred side - it is in
        // fact the third pixel conversion in this file, after the two at :L359 and :L361.
        // ------------------------------------------------------------------------------------------

        // :L93  //取得列名   ("obtain the column name")
        string sColName;

        // :L94-L95  if Right(sName,2) = "_t" then sColName = Left(sName,Len(sName) - 2)
        //
        // The header text object's name is the column's name with "_t" appended, so stripping the
        // suffix recovers the column. `Right(s,2)` answers the whole string when it is shorter than
        // two characters, which never equals "_t", so EndsWith is exact here.
        if (sName.EndsWith(HeaderTextSuffix, StringComparison.Ordinal))
        {
            sColName = sName[..^HeaderTextSuffix.Length];
        }

        // ------------------------------------------------------------------------------------------
        // :L96-L99 - DORMANT, CARRIED AS A COMMENT, NOT REVIVED.
        //
        //      elseif Right(sName,Len(ARROWSUFFIX)) = ARROWSUFFIX then
        //          sColName = Left(sName,Pos(sName,ARROWSUFFIX) - 1)
        //      elseif Right(sName,Len(n_cst_dwsvc_contextmenu.FILTERSUFFIX)) = n_cst_dwsvc_contextmenu.FILTERSUFFIX then
        //          sColName = Left(sName,Pos(sName,n_cst_dwsvc_contextmenu.FILTERSUFFIX) - 1)
        //
        // These would let a click on an INDICATOR object, or on a filter object, re-sort the column it
        // belongs to.
        //
        // *** A VERIFIED ANOMALY, AND THE REASON THE CODE COULD NOT SIMPLY BE UNCOMMENTED: ***
        // the second arm names `n_cst_dwsvc_contextmenu.FILTERSUFFIX`, and a repository-wide search for
        // FILTERSUFFIX returns EXACTLY THESE TWO LINES and nothing else. The constant does not exist on
        // n_cst_dwsvc_contextmenu or anywhere in the 544-object estate, so the dormant code would not
        // COMPILE if revived - it refers to a member that was never written, or was removed without
        // these lines being updated. That is why it stays commented, and it is also why a downstream
        // agent must not treat the reference as evidence of a real cross-service dependency: AAP's
        // finding that the four files behind Services/ have no live cross-references stands, and this is
        // the single dead hit that proves the rule rather than breaking it.
        // ------------------------------------------------------------------------------------------

        // :L100-L102  else return 0
        else
        {
            return 0L;
        }

        // :L104-L105  sProp = #DataWindow.Describe(sColName + ".Band") / if sProp <> "detail" then return 0
        //
        // The name stripped of "_t" must resolve to a real DETAIL-band column. This is what rejects a
        // header text object that is a caption rather than a column heading: strip "_t" from a
        // decorative label and the remainder describes no column at all, so the answer is neither
        // "detail" nor anything else useful.
        sProp = host.Describe(sColName + ".Band");
        if (!string.Equals(sProp, DetailBand, StringComparison.Ordinal))
        {
            return 0L;
        }

        // :L107-L110  choose case #DataWindow.Describe(sColName + ".edit.style") case "checkbox","radiobutton" return 0
        //
        // NOTE THE LOWER-CASE PROPERTY SPELLING - `.edit.style` here, against `.Edit.Style` at :L297
        // inside the clause builder. Both are the oracle's own and both are carried verbatim; Describe
        // is case-insensitive about property names, so the inconsistency is cosmetic in the oracle and
        // is preserved rather than harmonised (constraint C-B).
        //
        // Two edit styles are excluded because sorting by them is meaningless: a check box and a radio
        // button render a value the user toggles, not one they order by. The VALUES compared are
        // machine tokens, so the comparison is ordinal.
        string editStyle = host.Describe(sColName + ".edit.style");
        if (string.Equals(editStyle, "checkbox", StringComparison.Ordinal)
            || string.Equals(editStyle, "radiobutton", StringComparison.Ordinal))
        {
            return 0L;
        }

        // ------------------------------------------------------------------------------------------
        // :L112-L118 - FIND the column in the store. A one-based scan (DECISION 5).
        // ------------------------------------------------------------------------------------------
        int nCount = UpperBound(_sortDatas);
        int nCurrentIndex = 0;

        for (int nIndex = 1; nIndex <= nCount; nIndex++)
        {
            // :L114 - `if SortDatas[nIndex].colName = sColName then`. Ordinal, because a column name is
            // a DataWindow identifier rather than user text.
            if (string.Equals(EntryAt(_sortDatas, nIndex).ColName, sColName, StringComparison.Ordinal))
            {
                // :L115-L116  nCurrentIndex = nIndex / exit
                nCurrentIndex = nIndex;
                break;
            }
        }

        // ------------------------------------------------------------------------------------------
        // :L119-L123 - APPEND when it was not found. The oracle's `SortDatas[nCount + 1]` idiom grows
        // the array by assigning one past its upper bound, which AAP 0.4.5.4 names as an R9 hazard
        // site: `nCount + 1` is the NEXT ONE-BASED INDEX, and Add() places the entry exactly there.
        //
        // THE NEW ENTRY IS SEEDED SORT_NONE [:L121], NOT SORT_ASC, so the three-state cycle below is
        // what promotes a first click to ascending. Seeding it ascending would collapse the cycle's
        // first step and make a first click and a third click indistinguishable.
        // ------------------------------------------------------------------------------------------
        if (nCurrentIndex == 0)
        {
            // :L120-L121
            _sortDatas.Add(new SortData(sColName, SORT_NONE));

            // :L122
            nCurrentIndex = nCount + 1;
        }

        // ------------------------------------------------------------------------------------------
        // :L125-L132 - THE THREE-STATE CYCLE: none -> ascending -> descending -> none.
        //
        // THERE IS NO DEFAULT ARM, so a direction outside the set is left UNCHANGED rather than reset.
        // That is reproduced: the switch below assigns only on the three known values. It is also why
        // SortData.SortType is not an enum - see that type's remarks.
        // ------------------------------------------------------------------------------------------
        SortData current = EntryAt(_sortDatas, nCurrentIndex);

        long nextSortType = current.SortType switch
        {
            SORT_NONE => SORT_ASC,   // :L126-L127
            SORT_ASC => SORT_DESC,   // :L128-L129
            SORT_DESC => SORT_NONE,  // :L130-L131
            // No default arm in the oracle, so an out-of-range direction is LEFT UNCHANGED. This arm
            // is STRUCTURALLY UNREACHABLE through the public surface - the store is private, the only
            // writer is this cycle, and it only ever produces 0, 1 or 2 - but C# requires a switch
            // expression be exhaustive, and removing it would not compile. Its coverage gap is
            // therefore honest rather than an untested branch; the equivalent out-of-range behaviour
            // that IS reachable is GetClause's missing suffix arm, and the parity matrix covers that.
            _ => current.SortType,
        };

        SetEntryAt(_sortDatas, nCurrentIndex, current with { SortType = nextSortType });

        // ------------------------------------------------------------------------------------------
        // :L134  if KeyDown(KeyControl!) then
        //
        // The live keyboard read, inverted into the `ctrlHeld` parameter (DECISION 4).
        // ------------------------------------------------------------------------------------------
        if (ctrlHeld)
        {
            // :L135  _of_Sort()
            //
            // BEFORE the store surgery, so the pass clears the indicator of the column just cleared.
            Sort();

            // :L136  if SortDatas[nCurrentIndex].sortType = SORT_NONE then
            if (EntryAt(_sortDatas, nCurrentIndex).SortType == SORT_NONE)
            {
                // :L137-L144 - REBUILD omitting exactly one index. The oracle allocates a fresh array
                // and appends with `newDatas[UpperBound(newDatas) + 1]`, the second R9 hazard site;
                // RemoveAt with the one-based-to-zero-based conversion is the same operation with the
                // arithmetic in one place. The COMPARISON is `nIndex <> nCurrentIndex` [:L140] - by
                // POSITION, not by column name - so a store that somehow held the same column twice
                // would lose only the one clicked, exactly as here.
                //
                // :L138 re-reads UpperBound before the scan; the re-read is redundant in the oracle
                // because nothing changed the store since :L112, and it is not reproduced as a separate
                // statement because RemoveAt needs no bound.
                RemoveEntryAt(_sortDatas, nCurrentIndex);
            }
        }
        else
        {
            // :L147  nCount = UpperBound(SortDatas)
            //
            // RE-READ, and the re-read matters: the append above may have grown the store, so this
            // count INCLUDES a newly added entry. It is the value :L157 tests below, which is why it is
            // captured here rather than reusing the :L112 value.
            nCount = UpperBound(_sortDatas);

            // :L148-L152 - EXCLUSIVE: every OTHER column is cleared first, so a plain click replaces
            // the sort instead of adding to it. Again by POSITION [:L149].
            for (int nIndex = 1; nIndex <= nCount; nIndex++)
            {
                if (nIndex != nCurrentIndex)
                {
                    // :L150
                    SetEntryAt(_sortDatas, nIndex, EntryAt(_sortDatas, nIndex) with { SortType = SORT_NONE });
                }
            }

            // :L153  _of_Sort()
            //
            // Again BEFORE the surgery, and here it matters more: every other column was just cleared,
            // so this is the pass that clears all of their indicators.
            Sort();

            // :L154  if SortDatas[nCurrentIndex].sortType = SORT_NONE then
            if (EntryAt(_sortDatas, nCurrentIndex).SortType == SORT_NONE)
            {
                // :L155-L156  SORTDATA emptyDatas[] / SortDatas = emptyDatas
                //
                // The clicked column completed its cycle and no other column is sorted, so the store
                // empties entirely - which is what makes the NEXT click on any column a first click.
                _sortDatas.Clear();
            }
            else if (nCount > 1)
            {
                // :L157-L160  SORTDATA newData[1] / newData[1] = SortDatas[nCurrentIndex] / SortDatas = newData
                //
                // COLLAPSE TO ONE ELEMENT. Guarded on `nCount > 1` purely as an optimisation - with a
                // single entry the store already holds exactly this - and the guard is reproduced
                // because it is the difference between allocating and not, which a call-counting test
                // can see.
                SortData survivor = EntryAt(_sortDatas, nCurrentIndex);
                _sortDatas.Clear();
                _sortDatas.Add(survivor);
            }
        }

        // :L164
        return 0L;
    }

    /// <summary>
    /// The raw double-click handler - the port of
    /// <c>event onlbuttondblclk pbm_dwnlbuttondblclk</c> (<c>:L19</c>, body <c>:L168</c>).
    /// </summary>
    /// <param name="xpos">The pointer x position.</param>
    /// <param name="ypos">The pointer y position.</param>
    /// <param name="row">The one-based row under the pointer, or <c>0</c> when none.</param>
    /// <param name="dwo">The object under the pointer.</param>
    /// <returns>Whatever <see cref="OnLButtonClk"/> answered - <c>0</c>.</returns>
    /// <remarks>
    /// A ONE-LINE FORWARD, KEPT AS A DISTINCT MEMBER. <c>:L168</c> is
    /// <c>return Event OnLButtonClk(xpos,ypos,row,dwo)</c> and nothing else: the second click of a
    /// double click must re-arm the press capture, or a double click would leave the captured point
    /// stale and the release after it would be rejected as a drag. It is separately broker-subscribed
    /// [<c>:L443</c>], so it cannot be folded into its target even though the bodies now coincide -
    /// and it RETURNS the forwarded value rather than <c>0</c>, which is the shape to preserve if the
    /// target ever stops answering <c>0</c>.
    /// </remarks>
    public long OnLButtonDblClk(long xpos, long ypos, long row, IDataWindowObject dwo)
    {
        // :L168  return Event OnLButtonClk(xpos,ypos,row,dwo)
        return OnLButtonClk(xpos, ypos, row, dwo);
    }

    // ==========================================================================================
    //  THE TWO `_of_sort` MEMBERS ARE DIFFERENT MEMBERS WITH DIFFERENT JOBS, AND CONFLATING THEM
    //  DESTROYS THE LAZY CAPTURE.
    //  ----------------------------------------------------------------------------------------
    //  The oracle declares BOTH under one name [:L44 and :L50], distinguished only by arity and
    //  return type: `private subroutine _of_sort()` and `private function long _of_sort(readonly
    //  string sort)`. They coexist in C# by arity, and the void-versus-long difference is preserved
    //  exactly rather than harmonised.
    //
    //      Sort()          REBUILDS the sort expression from the store, produces one descriptor per
    //                      entry, performs the lazy capture of the original sort, and then delegates.
    //      Sort(in string) APPLIES a given expression to the DataWindow, with the event gate saved and
    //                      restored around it and the current row carried across the reorder.
    //
    //  The void one is the only caller of the lazy capture [:L176-L178]. Merging the two - or having
    //  Reset or Update call the string overload where the oracle calls the void one - would skip the
    //  capture and the fallback to the original sort would silently stop working.
    // ==========================================================================================

    /// <summary>
    /// Rebuilds the sort expression from the store, publishes one indicator descriptor per entry and
    /// applies the result - the port of <c>private subroutine _of_sort()</c> (<c>:L44</c>, body
    /// <c>:L171-L214</c>).
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// This service has not been attached to a host yet.
    /// </exception>
    /// <remarks>
    /// <para>
    /// VOID, LIKE THE ORACLE'S SUBROUTINE. It discards the code the apply path answers [<c>:L211</c>],
    /// so a failure to apply is not observable to its callers - and its callers are
    /// <see cref="OnLButtonClicked"/> [<c>:L135</c>, <c>:L153</c>] and <see cref="Reset"/>
    /// [<c>:L242</c>], neither of which checks anything. Returning a code from here would invite a
    /// caller to start checking one, which is a behaviour the oracle does not have.
    /// </para>
    /// <para>
    /// *** DEFECT 1, PRESERVED [<c>:L188-L192</c>]. *** The running ordinal is incremented ONLY when
    /// two or more columns are sorted, because the increment sits inside <c>if nSortCnt &gt; 1</c>. With
    /// exactly one sorted column the counter is therefore never touched and <c>0</c> reaches every
    /// descriptor, which suppresses the ordinal badge [<c>:L383</c> gates the badge on
    /// <c>index &gt; 0</c>]. Suppressing the badge for a single sort is almost certainly what the author
    /// wanted, but it is achieved as a SIDE EFFECT of the count guard rather than by any explicit test -
    /// so a reader who "simplifies" the guard by hoisting the increment out of it will start rendering a
    /// "1" badge on every single-column sort. Constraint C-B forbids correcting it and a unit test pins
    /// the ordinal at <c>0</c> for the single-column case.
    /// </para>
    /// <para>
    /// A SECOND CONSEQUENCE OF THE SAME LINES, WORTH STATING BECAUSE IT LOOKS LIKE A BUG AND IS
    /// REPRODUCED ANYWAY: when two or more columns ARE sorted, an UNSORTED entry does not increment the
    /// counter but is still handed its current value [<c>:L193</c> is outside the
    /// <c>if</c>] - so it receives the PREVIOUS sorted column's ordinal rather than <c>0</c>. It is
    /// harmless because such a descriptor clears rather than draws, and it is preserved because it is
    /// what the oracle passes.
    /// </para>
    /// <para>
    /// EVERY ENTRY GETS A DESCRIPTOR, INCLUDING THE UNSORTED ONES. <c>:L193</c> calls
    /// <c>_of_SetArrow</c> unconditionally and that routine destroys both band objects before returning
    /// early on an unsorted column - so an unsorted entry has real work to do, and its descriptor is a
    /// clear instruction. Skipping it would orphan the indicator of a column the user has just
    /// unsorted.
    /// </para>
    /// <para>
    /// THE TWO <c>SetPointer</c> CALLS BRACKETING THE APPLY [<c>:L209</c> and <c>:L213</c>] ARE
    /// DEFERRED AND EMIT NOTHING. An hourglass is a pointer shape, which constraint C-D places with the
    /// rest of the presentational surface, and it has no observable effect a service response can
    /// carry.
    /// </para>
    /// </remarks>
    private void Sort()
    {
        DataWindowServiceHost host = RequireHost();

        // :L174  _sSort = ""
        _sSort = string.Empty;

        // :L176-L178  if _sOrgSort = "" then _sOrgSort = #DataWindow.Describe("DataWindow.Table.Sort")
        //
        // THE LAZY CAPTURE, and the reason this overload exists. It fires ONCE - on the first pass -
        // because :L274 subsequently guarantees the field is never empty again, so the original sort is
        // captured BEFORE any user sort has been applied over it. Reading it eagerly at attachment time
        // would be wrong in the other direction: se_cst_dw raises OnInit during its own construction
        // [se_cst_dw.sru:L579], before the DataWindow has a data object at all.
        if (_sOrgSort.Length == 0)
        {
            _sOrgSort = host.Describe(TableSortProperty);
        }

        // :L180-L185 - count the SORTED entries. This count exists only to decide whether ordinals are
        // numbered at all; see DEFECT 1 in the remarks.
        int nCount = UpperBound(_sortDatas);
        int nSortCnt = 0;

        for (int nIndex = 1; nIndex <= nCount; nIndex++)
        {
            // :L182
            if (EntryAt(_sortDatas, nIndex).SortType != SORT_NONE)
            {
                // :L183  nSortCnt ++
                nSortCnt++;
            }
        }

        // :L187-L199 - the single pass that produces every descriptor and every clause, in store order.
        _indicators.Clear();

        int nSortIdex = 0;

        for (int nIndex = 1; nIndex <= nCount; nIndex++)
        {
            SortData entry = EntryAt(_sortDatas, nIndex);

            // :L188-L192 - *** DEFECT 1 *** - the increment is INSIDE the count guard.
            if (nSortCnt > 1)
            {
                // :L189
                if (entry.SortType != SORT_NONE)
                {
                    // :L190  nSortIdex ++
                    nSortIdex++;
                }
            }

            // :L193  _of_SetArrow(SortDatas[nIndex].colName,SortDatas[nIndex].sortType,nSortIdex)
            //
            // UNCONDITIONAL, and in the headless half it produces a descriptor instead of mutating the
            // DataWindow. Note that nSortIdex is handed over whatever its current value - see the
            // second consequence described in the remarks.
            _indicators.Add(BuildIndicatorDescriptor(entry.ColName, entry.SortType, nSortIdex));

            // :L194  sClause = _of_GetClause(SortDatas[nIndex].colName,SortDatas[nIndex].sortType)
            string sClause = GetClause(entry.ColName, entry.SortType);

            // :L195-L198 - join, skipping empty clauses. An unsorted entry yields the empty string
            // [:L292], so the separator is never emitted for one - which is why the composed expression
            // has no stray leading or doubled commas even though the pass visits unsorted entries too.
            if (sClause.Length != 0)
            {
                // :L196
                if (_sSort.Length != 0)
                {
                    _sSort += ClauseSeparator;
                }

                // :L197
                _sSort += sClause;
            }
        }

        // ------------------------------------------------------------------------------------------
        // :L201-L207 - CHOOSE WHAT TO APPLY.
        //
        // `sSort` is the oracle's local, which starts as the empty string [:L172]. The three outcomes:
        //      * a user sort exists              -> apply it                              [:L206]
        //      * no user sort, an original exists -> restore the original                  [:L203]
        //      * no user sort, no original       -> apply the EMPTY STRING, clearing the sort
        //
        // The third is reached because the guard at :L202 is `_sOrgSort <> "?"`: when the sentinel says
        // there was no original sort the assignment is SKIPPED and the local keeps its empty value, so
        // the empty string is what gets applied. Writing this as `_sOrgSort` unconditionally would apply
        // the literal text "?" as a sort expression.
        // ------------------------------------------------------------------------------------------
        string sSort = string.Empty;

        if (_sSort.Length == 0)
        {
            // :L202-L204
            if (!string.Equals(_sOrgSort, NoOriginalSortSentinel, StringComparison.Ordinal))
            {
                sSort = _sOrgSort;
            }
        }
        else
        {
            // :L206
            sSort = _sSort;
        }

        // :L209  SetPointer(HourGlass!)  - DEFERRED, emits nothing.

        // :L211  _of_Sort(sSort)   - the code is DISCARDED, exactly as the oracle discards it.
        _ = Sort(sSort);

        // :L213  SetPointer(Arrow!)     - DEFERRED, emits nothing.
    }

    /// <summary>
    /// Clears every column's sort and empties the store - the port of <c>of_reset()</c>
    /// (<c>:L45</c>, body <c>:L216-L247</c>).
    /// </summary>
    /// <returns>
    /// <c>RetCode.OK</c> when there was something to reset, and <c>RetCode.FAILED</c> when the store
    /// was already empty. SEE DEFECT 3.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// This service has not been attached to a host yet.
    /// </exception>
    /// <remarks>
    /// <para>
    /// *** DEFECT 3, PRESERVED [<c>:L236</c>]. *** <c>if nCount = 0 then return RetCode.FAILED</c> -
    /// an EMPTY STORE IS REPORTED AS A FAILURE. A caller therefore cannot distinguish "there was
    /// nothing to reset", which is a perfectly ordinary state after the store empties itself at
    /// <c>:L155-L156</c>, from a genuine failure to reset. It compounds with the return-code algebra
    /// AAP 0.8.2 records: <c>RetCode.FAILED</c> is <c>-1</c>, so <c>Predicates.IsFailed</c> answers TRUE
    /// for a no-op and a caller that branches on it will report an error to a user who did nothing
    /// wrong. Constraint C-B forbids softening it to <c>RetCode.OK</c> and a unit test pins it.
    /// </para>
    /// <para>
    /// THE STORE IS CLEARED AFTER THE PASS, NOT BEFORE, and the ordering is the whole reason the method
    /// works. <c>:L238-L240</c> sets every entry unsorted, <c>:L242</c> runs the pass - which is what
    /// clears every indicator and applies the fallback sort - and only then does <c>:L244</c> empty the
    /// store. Emptying first would leave the pass with nothing to visit, so no indicator would be
    /// cleared and every arrow would be orphaned on screen.
    /// </para>
    /// <para>
    /// IT DOES NOT CLEAR <see cref="_sOrgSort"/>, which is what makes reset RESTORE the DataWindow's
    /// original sort rather than leave it unsorted: the pass finds no user clauses, falls back to the
    /// captured original [<c>:L201-L204</c>] and applies that.
    /// </para>
    /// </remarks>
    public long Reset()
    {
        // :L235  nCount = UpperBound(SortDatas)
        int nCount = UpperBound(_sortDatas);

        // :L236  *** DEFECT 3 *** - a no-op reports FAILED, not OK.
        if (nCount == 0)
        {
            return RetCode.FAILED;
        }

        // :L238-L240 - clear every direction, one-based (DECISION 5).
        for (int nIndex = 1; nIndex <= nCount; nIndex++)
        {
            // :L239
            SetEntryAt(_sortDatas, nIndex, EntryAt(_sortDatas, nIndex) with { SortType = SORT_NONE });
        }

        // :L242  _of_Sort()  - BEFORE the store is emptied. See the remarks.
        Sort();

        // :L233, :L244  SORTDATA emptyDatas[] / SortDatas = emptyDatas
        _sortDatas.Clear();

        // :L246
        return RetCode.OK;
    }

    /// <summary>
    /// Updates the remembered ORIGINAL sort and, when no user sort is active, applies it - the port of
    /// <c>of_update(readonly string sort)</c> (<c>:L46</c>, body <c>:L249-L283</c>).
    /// </summary>
    /// <param name="sort">
    /// The expression to remember, or <see langword="null"/> to capture the DataWindow's current sort
    /// instead. THE NULL IS MEANINGFUL AND SELECTS A DIFFERENT BRANCH - see the remarks.
    /// </param>
    /// <returns>
    /// <c>RetCode.OK</c> when a user sort is active and nothing was applied, otherwise whatever the
    /// apply path answered.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// This service has not been attached to a host yet.
    /// </exception>
    /// <remarks>
    /// <para>
    /// NULLABLE BECAUSE <c>IsNull(sort)</c> AT <c>:L268</c> IS LOAD-BEARING. AAP 0.4.5.4 forbids
    /// collapsing a PowerScript null onto a default: a supplied expression is REMEMBERED
    /// [<c>:L269</c>], while a null CAPTURES from the DataWindow [<c>:L271</c>] and then runs the
    /// sentinel normalisation. Passing the empty string instead of null takes the first branch and
    /// remembers an empty original - a different outcome entirely.
    /// </para>
    /// <para>
    /// *** DEFECT 2, PRESERVED VERBATIM [<c>:L272</c>]. *** The line is
    /// <c>if sSort = "?" or _sOrgSort = "!" then _sOrgSort = ""</c>, and its FIRST TEST READS
    /// <c>sSort</c> - the local declared at <c>:L266</c> and never assigned before this point, so
    /// always the empty string. The author plainly meant <c>_sOrgSort = "?"</c>: the two sentinels are
    /// handled together everywhere else in the file, at <c>:L403</c>. THE OBSERVABLE CONSEQUENCE is
    /// that a <c>Describe</c> answering the undetermined sentinel is NOT normalised to empty here, only
    /// the invalid-expression sentinel is - so the field keeps the literal text <c>"?"</c>, which then
    /// survives the empty test at <c>:L274</c> and is subsequently treated by <c>:L278</c> as "there
    /// was no original sort". The end state is coincidentally reasonable, which is exactly why the
    /// defect has never been noticed. It is reproduced by comparing against the always-empty local, and
    /// a unit test pins it. DO NOT REPAIR IT (constraint C-B).
    /// </para>
    /// <para>
    /// THE EARLY-OUT AT <c>:L276</c> IS WHY THIS IS SAFE TO CALL AT ANY TIME. While a user sort is
    /// active the remembered original is updated and NOTHING IS APPLIED, so a retrieve that re-reports
    /// its sort cannot stamp on the sort the user is looking at.
    /// </para>
    /// </remarks>
    public long Update(in string? sort)
    {
        DataWindowServiceHost host = RequireHost();

        // :L266  string sSort
        //
        // Declared here, at the top, exactly where the oracle declares it - because DEFECT 2 depends on
        // it being READ before it is ever assigned. Moving the declaration down to its first real use
        // would make the defect unreproducible.
        string sSort = string.Empty;

        // :L268  if Not IsNull(sort) then
        if (sort is not null)
        {
            // :L269
            _sOrgSort = sort;
        }
        else
        {
            // :L271
            _sOrgSort = host.Describe(TableSortProperty);

            // :L272  *** DEFECT 2 *** - the first comparison reads the always-empty local `sSort`
            // rather than `_sOrgSort`. Reproduced verbatim; see the remarks.
            if (string.Equals(sSort, NoOriginalSortSentinel, StringComparison.Ordinal)
                || string.Equals(_sOrgSort, InvalidExpressionSentinel, StringComparison.Ordinal))
            {
                _sOrgSort = string.Empty;
            }
        }

        // :L274  if _sOrgSort = "" then _sOrgSort = "?"
        //
        // The sentinel assignment, and the reason the field's empty state means "not captured yet"
        // rather than "captured nothing". After this line the field is never empty again, which is what
        // makes the lazy capture in Sort() fire exactly once.
        if (_sOrgSort.Length == 0)
        {
            _sOrgSort = NoOriginalSortSentinel;
        }

        // :L276  if _sSort <> "" then return RetCode.OK
        if (_sSort.Length != 0)
        {
            return RetCode.OK;
        }

        // :L278-L280 - as at :L202-L204, the sentinel means "leave the local empty", so an absent
        // original clears the sort rather than applying the text "?".
        if (!string.Equals(_sOrgSort, NoOriginalSortSentinel, StringComparison.Ordinal))
        {
            sSort = _sOrgSort;
        }

        // :L282
        return Sort(sSort);
    }

    /// <summary>
    /// Captures the DataWindow's current sort as the remembered original - the port of
    /// <c>of_update()</c> (<c>:L47</c>, body <c>:L285-L288</c>).
    /// </summary>
    /// <returns>What <see cref="Update(in string?)"/> answered.</returns>
    /// <exception cref="InvalidOperationException">
    /// This service has not been attached to a host yet.
    /// </exception>
    /// <remarks>
    /// THE NULL IS THE ARGUMENT, NOT THE ABSENCE OF ONE. The oracle declares a string, calls
    /// <c>SetNull</c> on it and delegates [<c>:L286-L287</c>] - a three-line body whose only purpose is
    /// to pass an explicit null, because that is what selects the capture-from-the-DataWindow branch at
    /// <c>:L268</c>. Passing the empty string here would take the other branch and remember an empty
    /// original, so this overload is not sugar and the <see langword="null"/> is not a shortcut.
    /// </remarks>
    public long Update()
    {
        // :L286-L287  string nvl / SetNull(nvl) / return of_Update(nvl)
        return Update(null);
    }

    /// <summary>
    /// Builds ONE column's sort clause - the port of <c>_of_getclause(colName, sortType)</c>
    /// (<c>:L48</c>, body <c>:L290-L339</c>). THE PURE-HEADLESS CORE OF THIS FILE.
    /// </summary>
    /// <param name="colName">The column name.</param>
    /// <param name="sortType">The direction - one of the three <c>SORT_*</c> values.</param>
    /// <returns>
    /// The clause, or the EMPTY STRING when the column is not sorted. Never
    /// <see langword="null"/>.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// This service has not been attached to a host yet.
    /// </exception>
    /// <remarks>
    /// <para>
    /// PARITY IS BYTE-EXACT OUTPUT. Every literal below - the function name and its parentheses, the
    /// <c>-999999</c> sentinel, the two <c>1900-01-01</c> dates, the <c>00:00:00</c> time, the single
    /// quotes around the empty string, and the SINGLE LEADING SPACE in each direction suffix - is
    /// compared character for character by the parity matrix. None may be reformatted, parameterised or
    /// "improved": the output is a DataWindow sort expression that the DataWindow itself parses, and it
    /// also appears in characterization recordings where any difference invalidates the comparison.
    /// </para>
    /// <para>
    /// FOUR STAGES, AND THE CASCADE BETWEEN THEM IS THE MECHANISM. Each stage runs only if the previous
    /// left the clause empty, and the empty string is therefore doing double duty as "not decided yet"
    /// and as the final answer for an unsorted column. That is the oracle's structure and it is
    /// preserved literally rather than restructured into a chain of returns, because the stage-two
    /// display-column test can DELIBERATELY LEAVE THE CLAUSE EMPTY and fall through to stage three.
    /// </para>
    /// <para>
    /// STAGE 1 [<c>:L294-L295</c>] - A COMPUTED COLUMN SORTS BY ITSELF. Its <c>.Type</c> is
    /// <c>"compute"</c>, it has no edit style and no null handling to consider, so the clause is the
    /// bare name and the entire else-branch is skipped.
    /// </para>
    /// <para>
    /// STAGE 2 [<c>:L297-L311</c>] - SORT BY WHAT THE USER SEES, WHEN THAT DIFFERS FROM WHAT IS STORED.
    /// A drop-down data window sorts by its DISPLAY column only when display and data differ
    /// [<c>:L300</c>] - if they are the same, sorting by the display value would be identical work for a
    /// more expensive expression, so the clause is LEFT EMPTY and stage three gets its chance. A
    /// drop-down list box always sorts by display [<c>:L304</c>], with no such test, because a list box
    /// has no data column to compare against. Any other edit style falls to the code-table test
    /// [<c>:L307-L310</c>]. Note that the code-table test is in the ELSE of the dddw/ddlb test, so a
    /// dddw whose display equals its data is NEVER code-table-tested - which is the oracle's structure
    /// and would be lost by flattening the three tests into one chain.
    /// </para>
    /// <para>
    /// STAGE 3 [<c>:L313-L328</c>] - MAKE NULLS SORT CONSISTENTLY. Reached only when stage two produced
    /// nothing. THREE PROPERTY SPELLINGS ARE TRIED, joined by <c>or</c> so any one of them triggers:
    /// the plain edit style's, the drop-down data window's and the drop-down list box's. They are
    /// distinct properties on the same column and the DataWindow answers only the one matching the
    /// column's actual edit style, which is why all three must be asked. PowerScript's <c>or</c>
    /// short-circuits and so does C#'s <c>||</c>, so the number of Describe calls matches too. The
    /// substitute values are LOW SENTINELS chosen so a null sorts first ascending - and the default arm
    /// covers strings and every unrecognised type with the empty string.
    /// </para>
    /// <para>
    /// STAGE 4 [<c>:L329</c>] - the bare column name, for every column that reached here without a
    /// special case. This is the common answer.
    /// </para>
    /// <para>
    /// THE DIRECTION SUFFIX [<c>:L331-L336</c>] HAS NO DEFAULT ARM, so a direction outside the set
    /// yields a clause with NO suffix at all - a reachable answer, because
    /// <see cref="SortData.SortType"/> is a <c>long</c> and the cycle at <c>:L125-L132</c> leaves an
    /// out-of-range value unchanged. Preserved as-is.
    /// </para>
    /// <para>
    /// `internal` RATHER THAN `private` - see DECISION 6. It is the one accessibility deviation in the
    /// file, it is scoped to the test assembly by the project's InternalsVisibleTo, and it exists so the
    /// byte-exact matrix can address this member directly.
    /// </para>
    /// </remarks>
    internal string GetClause(in string colName, in long sortType)
    {
        DataWindowServiceHost host = RequireHost();

        // :L292  if sortType = SORT_NONE then return ""
        //
        // FIRST, so an unsorted column costs no Describe call at all - and so the join at :L195 sees the
        // empty string and emits no separator for it.
        if (sortType == SORT_NONE)
        {
            return string.Empty;
        }

        // :L290  string sClause,sProp   - both start empty, and sClause's emptiness drives the cascade.
        string sClause = string.Empty;

        // ------------------------------------------------------------------------------------------
        // STAGE 1 :L294-L295 - a computed column sorts by itself.
        // ------------------------------------------------------------------------------------------
        if (string.Equals(host.Describe(colName + ".Type"), "compute", StringComparison.Ordinal))
        {
            // :L295
            sClause = colName;
        }
        else
        {
            // --------------------------------------------------------------------------------------
            // STAGE 2 :L297-L311 - sort by the DISPLAY value where the edit style provides one.
            // --------------------------------------------------------------------------------------

            // :L297  sProp = #DataWindow.Describe(colName + ".Edit.Style")
            //
            // NOTE THE MIXED-CASE SPELLING here against the lower-case `.edit.style` at :L107. Both are
            // the oracle's and both are carried verbatim; Describe is case-insensitive about property
            // names so the inconsistency is cosmetic, and harmonising it would be an unrequested edit.
            string sProp = host.Describe(colName + ".Edit.Style");

            // :L298  if sProp = "dddw" or sProp = "ddlb" then
            if (string.Equals(sProp, "dddw", StringComparison.Ordinal)
                || string.Equals(sProp, "ddlb", StringComparison.Ordinal))
            {
                // :L299  if sProp = "dddw" then
                if (string.Equals(sProp, "dddw", StringComparison.Ordinal))
                {
                    // :L300 - display and data compared to EACH OTHER, not against any fixed value, so
                    // two Describe calls are needed and neither answer is inspected on its own.
                    if (!string.Equals(
                            host.Describe(colName + ".DDDW.DisplayColumn"),
                            host.Describe(colName + ".DDDW.DataColumn"),
                            StringComparison.Ordinal))
                    {
                        // :L301
                        sClause = "LookUpDisplay(" + colName + ")";
                    }

                    // :L302 - NO else. When they match the clause stays EMPTY and stage three runs.
                }
                else
                {
                    // :L303-L304 - a drop-down list box always sorts by display.
                    sClause = "LookUpDisplay(" + colName + ")";
                }
            }
            else
            {
                // :L307  sProp = #DataWindow.Describe(colName + ".Edit.CodeTable")
                sProp = host.Describe(colName + ".Edit.CodeTable");

                // :L308-L309
                if (string.Equals(sProp, "yes", StringComparison.Ordinal))
                {
                    sClause = "LookUpDisplay(" + colName + ")";
                }
            }
        }

        // ------------------------------------------------------------------------------------------
        // STAGE 3 :L313-L328 - null substitution, so nulls sort predictably rather than by the
        // DataWindow's own null ordering.
        // ------------------------------------------------------------------------------------------
        if (sClause.Length == 0)
        {
            // :L314 - three distinct properties, `or`-joined and short-circuiting.
            if (string.Equals(host.Describe(colName + ".Edit.NilIsNull"), "yes", StringComparison.Ordinal)
                || string.Equals(host.Describe(colName + ".DDDW.NilIsNull"), "yes", StringComparison.Ordinal)
                || string.Equals(host.Describe(colName + ".DDLB.NilIsNull"), "yes", StringComparison.Ordinal))
            {
                // :L315  choose case _of_GetColumnType(colName)
                //
                // GetColumnType is the base-class helper that reduces the raw `.ColType` answer to one
                // of the COL_TYPE_* categories [n_cst_dwsvc.sru:L72, body :L539].
                long nColType = GetColumnType(colName);

                sClause = nColType switch
                {
                    // :L316-L317 - TWO CATEGORIES SHARE ONE ARM, both substituting the numeric
                    // sentinel. -999999 is a magic number and stays one: it is the oracle's chosen
                    // low value and any other would reorder nulls against real data.
                    COL_TYPE_INTEGER or COL_TYPE_DECIMAL =>
                        "if(IsNull(" + colName + "),-999999," + colName + ")",

                    // :L318-L319
                    COL_TYPE_DATETIME =>
                        "if(IsNull(" + colName + "),DateTime('1900-01-01')," + colName + ")",

                    // :L320-L321 - Date(), not DateTime(), for the same date literal. The two arms are
                    // deliberately distinct: the substitute's TYPE must match the column's or the
                    // DataWindow rejects the expression.
                    COL_TYPE_DATE =>
                        "if(IsNull(" + colName + "),Date('1900-01-01')," + colName + ")",

                    // :L322-L323
                    COL_TYPE_TIME =>
                        "if(IsNull(" + colName + "),Time('00:00:00')," + colName + ")",

                    // :L324-L325 - the default arm, which covers COL_TYPE_STRING, COL_TYPE_UNKNOWN and
                    // anything unrecognised. The substitute is the SINGLE-QUOTED EMPTY STRING, so the
                    // clause contains two adjacent apostrophes and that is correct.
                    _ => "if(IsNull(" + colName + "),''," + colName + ")",
                };
            }
        }

        // ------------------------------------------------------------------------------------------
        // STAGE 4 :L329  if sClause = "" then sClause = colName
        // ------------------------------------------------------------------------------------------
        if (sClause.Length == 0)
        {
            sClause = colName;
        }

        // :L331-L336 - the direction suffix. ONE LEADING SPACE each, and no default arm.
        if (sortType == SORT_ASC)
        {
            // :L332-L333
            sClause += AscendingSuffix;
        }
        else if (sortType == SORT_DESC)
        {
            // :L334-L335
            sClause += DescendingSuffix;
        }

        // :L338
        return sClause;
    }

    /// <summary>
    /// Produces the descriptor that REPLACES <c>_of_setarrow</c>'s side effects - the headless half of
    /// <c>_of_setarrow(colName, sortType, index)</c> (<c>:L49</c>, body <c>:L341-L395</c>).
    /// </summary>
    /// <param name="colName">The column the indicator belongs to.</param>
    /// <param name="sortType">The direction, which decides whether the indicator is drawn or cleared.</param>
    /// <param name="index">
    /// The running ordinal, straight from <see cref="Sort"/>. May be <c>0</c> - see DEFECT 1.
    /// </param>
    /// <returns>A descriptor the deferred rendering half can act on.</returns>
    /// <exception cref="InvalidOperationException">
    /// This service has not been attached to a host yet.
    /// </exception>
    /// <remarks>
    /// <para>
    /// THIS MEMBER IS THE SPLIT LINE MADE CONCRETE. It reproduces every part of <c>_of_setarrow</c> that
    /// composes a NAME, selects a GLYPH, formats a LABEL or reads a PROPERTY, and it reproduces none of
    /// the parts that convert units, do geometry arithmetic, select a font or emit DataWindow syntax.
    /// The file header enumerates the deferred parts with their locators; the eventual consumer is the
    /// reserved <c>/v1/design/**</c> extension point (AAP 0.4.4).
    /// </para>
    /// <para>
    /// THE THREE GEOMETRY READS HAPPEN AFTER THE UNSORTED EARLY-OUT, AND THE ORDER IS OBSERVABLE. The
    /// oracle destroys both objects [<c>:L351-L352</c>] and only THEN returns for an unsorted column
    /// [<c>:L353</c>], so <c>:L366-L368</c> are never reached in that case. A clear-only descriptor
    /// therefore leaves the three source values <see langword="null"/> and this method performs NO
    /// Describe call for one - which a call-counting test can verify, and which matters because a
    /// Describe against a column whose header object has gone would answer a sentinel.
    /// </para>
    /// <para>
    /// THE TWO NAMES ARE COMPOSED HERE RATHER THAN BY THE RENDERING HALF because the oracle composes
    /// them BEFORE the early-out [<c>:L348-L349</c> precede <c>:L353</c>] - a clear-only descriptor
    /// needs them, since clearing means destroying objects by name.
    /// </para>
    /// </remarks>
    private ColumnSortIndicatorDescriptor BuildIndicatorDescriptor(
        in string colName,
        in long sortType,
        in int index)
    {
        // :L348  sObjName = colName + ARROWSUFFIX
        string sObjName = colName + ARROWSUFFIX;

        // :L349  sIdxObjName = colName + "_idx_" + ARROWSUFFIX
        //
        // The infix sits BETWEEN the name and the suffix, so this is not sObjName with anything
        // appended. See ColumnSortIndicatorDescriptor.IndexObjectName.
        string sIdxObjName = colName + IndexObjectInfix + ARROWSUFFIX;

        // :L351-L352  #DataWindow.Modify("Destroy " + sObjName) / ... + sIdxObjName
        //
        // DEFERRED - emitting DataWindow syntax is rendering (constraint C-D). The two names above ARE
        // the destroy instruction, carried as data; the descriptor's ClearOnly flag and the
        // destroy-then-create discipline are documented on that property.

        // :L353  if sortType = SORT_NONE then return
        //
        // The early-out, reproduced as a complete clear-only descriptor rather than as nothing - because
        // the two destroys above it have already happened by this point in the oracle.
        if (sortType == SORT_NONE)
        {
            return new ColumnSortIndicatorDescriptor
            {
                ColumnName = colName,
                ArrowObjectName = sObjName,
                IndexObjectName = sIdxObjName,
                SortType = sortType,
                Index = index,
                ClearOnly = true,
                ArrowColor = ARROWCOLOR,
                IndexColor = INDEXCOLOR,
                TransparentColor = TRANSPARENT,
            };
        }

        // :L355-L364  choose case #DataWindow.Describe("DataWindow.Units") ...
        //
        // DEFERRED IN ITS ENTIRETY - all four arms convert to or from pixels: U2PY(10) at :L357,
        // Win32.PX2MMY(U2PY(10)) / 25.4 * 1000 at :L359, Win32.PX2MMY(U2PY(10)) * 100 at :L361 and the
        // PBU literal 10 at :L363. This is the DPI conversion family AAP 0.4.4 assigns to DesignSystem,
        // and it is the specific evidence on which AAP 0.2.1.3 Correction 4 split this service.
        // Consequently `fLeaderHeight` has no counterpart here and neither y offset is computed.

        DataWindowServiceHost host = RequireHost();

        // :L366  sXPos = #DataWindow.Describe(colName + "_t.x")
        string sXPos = host.Describe(colName + HeaderTextSuffix + ".x");

        // :L367  sWidth = String(Long(#DataWindow.Describe(colName + "_t.width")) / 2)
        //
        // THE RAW ANSWER IS CARRIED AND THE HALVING IS DEFERRED - see DECISION 1 in the file header for
        // why the division travels with the geometry rather than being performed here.
        string sWidth = host.Describe(colName + HeaderTextSuffix + ".width");

        // :L368  sYPos = String(Long(#DataWindow.Describe(colName + "_t.y")) - fLeaderHeight * 2)
        //         //偏移font leader空间   ("offset by the font leader space")
        // :L385  the same property re-read, offset by ONE leader height for the badge.
        //
        // The raw answer serves both; neither offset is computable here because its operand is the
        // deferred units conversion.
        string sYPos = host.Describe(colName + HeaderTextSuffix + ".y");

        // :L370-L375  choose case sortType case SORT_ASC sText = "t" case SORT_DESC sText = "u"
        //
        // NO DEFAULT ARM, so a direction outside the set leaves the glyph EMPTY - reachable for the same
        // reason the missing suffix arm in GetClause is, and preserved the same way.
        string arrowGlyph = string.Empty;

        if (sortType == SORT_ASC)
        {
            arrowGlyph = AscendingGlyph;
        }
        else if (sortType == SORT_DESC)
        {
            arrowGlyph = DescendingGlyph;
        }

        // :L377-L381  the arrow's `create text(...)` syntax, and :L386-L390 the badge's.
        // :L393       #DataWindow.Modify(sSyntax)
        //
        // BOTH DEFERRED. No syntax string is built here, no font is named, and nothing is emitted -
        // the descriptor's fields are the inputs that syntax was composed from.

        // :L383-L384  if index > 0 then sText = "      " + String(index)
        //
        // THE ENTIRE SECOND BAND OBJECT is gated on a positive ordinal, so a non-positive one means
        // there is no badge AT ALL rather than a badge with blank text - hence null and not empty.
        // DEFECT 1 makes 0 the normal value for a single sorted column.
        string? indexLabel = index > 0
            ? IndexLabelPadding + index.ToString(CultureInfo.InvariantCulture)
            : null;

        return new ColumnSortIndicatorDescriptor
        {
            ColumnName = colName,
            ArrowObjectName = sObjName,
            IndexObjectName = sIdxObjName,
            SortType = sortType,
            Index = index,
            ClearOnly = false,
            ArrowGlyph = arrowGlyph,
            IndexLabel = indexLabel,
            ArrowColor = ARROWCOLOR,
            IndexColor = INDEXCOLOR,
            TransparentColor = TRANSPARENT,
            SourceX = sXPos,
            SourceWidth = sWidth,
            SourceY = sYPos,
        };
    }

    /// <summary>
    /// Applies a sort expression to the DataWindow, carrying the current row across the reorder - the
    /// port of <c>private function long _of_sort(readonly string sort)</c> (<c>:L50</c>, body
    /// <c>:L397-L431</c>).
    /// </summary>
    /// <param name="sort">
    /// The expression to apply. THE EMPTY STRING IS LEGAL and clears the sort.
    /// </param>
    /// <returns><c>RetCode.OK</c> - both exits answer it.</returns>
    /// <exception cref="InvalidOperationException">
    /// This service has not been attached to a host yet.
    /// </exception>
    /// <remarks>
    /// <para>
    /// THE ONE-ARGUMENT OVERLOAD, WHICH IS A DIFFERENT MEMBER FROM <see cref="Sort()"/> - see the region
    /// banner above that method. This one APPLIES; that one BUILDS and then delegates here. The
    /// <c>long</c> return against the other's <c>void</c> is the oracle's own distinction and is
    /// preserved.
    /// </para>
    /// <para>
    /// THE EQUAL-SORT EARLY-OUT DOES NO WORK AT ALL [<c>:L402-L404</c>], and "no work" is the load-bearing
    /// part: no redraw suppression, no row capture, no gate change, no <c>SetSort</c>, no <c>Sort</c>, no
    /// <c>GroupCalc</c>. It answers <c>RetCode.OK</c>, so a caller cannot distinguish it from a sort that
    /// was applied - which is what makes <see cref="Update(in string?)"/> cheap to call repeatedly. BOTH
    /// SENTINELS ARE NORMALISED TO THE EMPTY STRING FIRST [<c>:L403</c>], so a DataWindow reporting either
    /// one compares equal to an empty target and correctly does nothing. This is the ONE place in the file
    /// that handles both sentinels together, and DEFECT 2 is precisely the place that fails to.
    /// </para>
    /// <para>
    /// THE EVENT-GATE SAVE AND RESTORE IS CONDITIONAL IN BOTH DIRECTIONS [<c>:L409-L412</c> and
    /// <c>:L424-L426</c>], which is the whole subtlety. The row-focus-change event is suppressed ONLY IF
    /// IT WAS NOT ALREADY SUPPRESSED, and re-enabled ONLY IF THIS CALL WAS THE ONE THAT SUPPRESSED IT. A
    /// caller that had deliberately suppressed it around a larger operation therefore keeps its
    /// suppression, and an unconditional re-enable would silently break that caller. The reason for
    /// suppressing it here is that restoring the current row [<c>:L422</c>] moves the row focus, which
    /// would otherwise fire a spurious row-focus-change for a row the user never navigated to.
    /// </para>
    /// <para>
    /// THE RETURN-TYPE ASYMMETRY AT THE TWO CALL SITES IS DELIBERATE AND IS NOT HARMONISED.
    /// <c>DisableEvent</c> answers <see langword="long"/> and <c>EnableEvent</c> answers
    /// <see langword="int"/> for identical arguments [<c>se_cst_dw.sru:L110-L111</c>]. Both results are
    /// DISCARDED here exactly as the oracle discards them, and both discards are written explicitly so
    /// the asymmetry stays visible at the call site.
    /// </para>
    /// <para>
    /// <c>SetRedraw</c> STAYS, and it is not a constraint-C-D breach. It is change-notification batching
    /// on the data model - it carries no geometry, no DPI conversion, no font and no window handle - and
    /// the host contract already publishes it for a ported call site in <c>se_cst_dw</c>. A headless host
    /// satisfies it as a no-op returning success, which is not a stub because there is nothing for it to
    /// do. Both codes are discarded, as the oracle discards them.
    /// </para>
    /// <para>
    /// THE ROW-IDENTITY ROUND TRIP IS WHY A RE-SORT DOES NOT MOVE THE CARET. A row NUMBER is positional
    /// and a sort invalidates it, so the identifier is captured before [<c>:L408</c>] and resolved after
    /// [<c>:L422</c>]. THE GUARD IS <c>&gt; 0</c> AND NOT <c>&lt;&gt; 0</c> [<c>:L421</c>], so a negative
    /// identifier is skipped as well as a zero one; and the resolved number is passed to
    /// <c>SetRow</c> UNCHECKED, so a row that no longer exists is <c>SetRow</c>'s problem exactly as in
    /// the oracle.
    /// </para>
    /// <para>
    /// THE FOUR UNUSED LOCALS AT <c>:L397-L398</c> - <c>nIndex</c>, <c>nCount</c> and the rest - are
    /// declared and never read by the oracle, and are simply absent here. Omitting a declaration that is
    /// never used changes nothing observable.
    /// </para>
    /// </remarks>
    private long Sort(in string sort)
    {
        DataWindowServiceHost host = RequireHost();

        // :L402  sCurrSort = #DataWindow.Describe("DataWindow.Table.Sort")
        string sCurrSort = host.Describe(TableSortProperty);

        // :L403  if sCurrSort = "?" or sCurrSort = "!" then sCurrSort = ""
        //
        // BOTH sentinels together - the only place in the file that gets this right. Compare DEFECT 2.
        if (string.Equals(sCurrSort, NoOriginalSortSentinel, StringComparison.Ordinal)
            || string.Equals(sCurrSort, InvalidExpressionSentinel, StringComparison.Ordinal))
        {
            sCurrSort = string.Empty;
        }

        // :L404  if sCurrSort = sort then return RetCode.OK
        //
        // NO WORK on this path - see the remarks.
        if (string.Equals(sCurrSort, sort, StringComparison.Ordinal))
        {
            return RetCode.OK;
        }

        // :L406  #DataWindow.SetRedraw(false)
        _ = host.SetRedraw(false);

        // :L408  nCurrentRowID = #DataWindow.GetRowIDFromRow(#DataWindow.GetRow())
        //
        // Captured BEFORE the reorder, which is the only moment the number and the identifier agree.
        long nCurrentRowID = host.GetRowIDFromRow(host.GetRow());

        // :L409-L412 - suppress the row-focus-change event, but only if it is not already suppressed,
        // and remember whether we were the one who did it.
        bool bEnableEvt = false;

        if (!host.IsEventDisabled(EventGate.EID_ROWFOCUSCHANGE))
        {
            // :L410
            bEnableEvt = true;

            // :L411 - returns `long`; the code is discarded exactly as the oracle discards it.
            _ = host.DisableEvent(EventGate.EID_ROWFOCUSCHANGE);
        }

        // :L414  #DataWindow.SetSort(sort)
        _ = host.SetSort(sort);

        // :L415  #DataWindow.Sort()
        //
        // The HOST's Sort, not this class's - setting the expression and applying it are two calls in the
        // oracle and stay two here.
        _ = host.Sort();

        // :L417-L419  if _of_HasGroup() then #DataWindow.GroupCalc()
        //
        // Reordering the rows moves the group breaks, so the group aggregates must be recomputed. Reached
        // only through Reset or Update, since a header click on a grouped DataWindow is refused at :L63.
        if (HasGroup())
        {
            _ = host.GroupCalc();
        }

        // :L421-L423 - restore the caret to the same DATA row, not the same position.
        if (nCurrentRowID > 0)
        {
            // :L422 - the resolved number is passed straight through, unchecked, as in the oracle.
            _ = host.SetRow(host.GetRowFromRowID(nCurrentRowID));
        }

        // :L424-L426 - re-enable ONLY if this call suppressed it.
        if (bEnableEvt)
        {
            // :L425 - returns `int` where DisableEvent returned `long`. Asymmetry preserved; code
            // discarded as the oracle discards it.
            _ = host.EnableEvent(EventGate.EID_ROWFOCUSCHANGE);
        }

        // :L428  #DataWindow.SetRedraw(true)
        _ = host.SetRedraw(true);

        // :L430
        return RetCode.OK;
    }

    /// <summary>
    /// Wires or unwires this service's three broker subscriptions - the port of
    /// <c>event onenable</c> (<c>:L441-L449</c>).
    /// </summary>
    /// <param name="enabled">The state being moved to.</param>
    /// <returns>
    /// <c>0</c>, which ALLOWS the change. This service never vetoes.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// This service has not been attached to a host yet.
    /// </exception>
    /// <remarks>
    /// <para>
    /// THE BASE CALL COMES FIRST, exactly as <c>call super::onenable</c> does at <c>:L441</c>, and its
    /// result is discarded because a PowerScript <c>call super::</c> statement has no result to consume.
    /// The vetoable sequencing around this hook belongs to
    /// <see cref="DataWindowServiceBase.SetEnabled(in bool)"/> and is not re-implemented here; note in
    /// particular that a veto there maps to <c>RetCode.FAILED</c> (<c>-1</c>) and NOT to
    /// <c>RetCode.PREVENT</c> (<c>1</c>), which is why answering <c>0</c> is the only correct answer for a
    /// service with nothing to object to.
    /// </para>
    /// <para>
    /// THREE OF THE FOUR EVENTS ARE BROKER-WIRED [<c>:L442-L444</c>], and the fourth is deliberately not.
    /// <see cref="OnLButtonClicked"/> IS NOT SUBSCRIBED - it is reached only through the posted
    /// continuation queued at <c>:L72</c> and run by <see cref="DrainPostedClick"/>. Subscribing it would
    /// run the whole sort cycle on every raw click, bypassing both the grid-style guard and the
    /// click-proximity test, so a reader searching for a fourth subscription must find none.
    /// </para>
    /// <para>
    /// SUBSCRIPTION GOES THROUGH THE HOST'S BROKER, WHICH IS WHAT THE ORACLE DOES TOO. <c>:L442</c> calls
    /// <c>#DataWindow.of_On(...)</c>, and <c>se_cst_dw.sru:L448</c> shows that member to be a one-line
    /// forward to <c>Eventful.of_On(...)</c>; likewise <c>of_Off(obj)</c> at <c>:L463</c>. Reaching the
    /// broker directly removes the forwarder without changing behaviour. Handler names are passed with
    /// <c>nameof</c> so a rename cannot leave a subscription pointing at a name that no longer resolves.
    /// </para>
    /// <para>
    /// THE DISABLE PATH UNSUBSCRIBES BY TARGET, NOT BY TOPIC. <c>:L446</c> is
    /// <c>#DataWindow.of_Off(this)</c> - the single-argument object overload - so ALL of this service's
    /// subscriptions go at once, including any a future one would add. Removing the three topics
    /// individually would be equivalent today and would silently leak a fourth later.
    /// </para>
    /// <para>
    /// IT LEAVES NO SORT APPLIED AND CLEARS NO STATE, unlike the row-select service which re-applies its
    /// selection on enable and clears it on disable. That asymmetry is the oracle's: disabling this
    /// service stops it responding to clicks and leaves the DataWindow sorted exactly as it was, with the
    /// store, the two expressions and any queued click intact. Adding a reset here would discard a user's
    /// sort on a state change the oracle treats as inert.
    /// </para>
    /// </remarks>
    protected override long OnEnable(bool enabled)
    {
        // :L441  call super::onenable - FIRST, and the result is discarded.
        _ = base.OnEnable(enabled);

        DataWindowServiceHost host = RequireHost();

        if (enabled)
        {
            // :L442  #DataWindow.of_On(#DataWindow.EVT_CLICKED,this,"onLButtonClk")
            _ = host.Eventful.Subscribe(EVT_CLICKED, this, nameof(OnLButtonClk));

            // :L443  #DataWindow.of_On(#DataWindow.EVT_DOUBLECLICKED,this,"onLButtonDblClk")
            _ = host.Eventful.Subscribe(EVT_DOUBLECLICKED, this, nameof(OnLButtonDblClk));

            // :L444  #DataWindow.of_On(#DataWindow.EVT_LBUTTONUP,this,"onLButtonUp")
            _ = host.Eventful.Subscribe(EVT_LBUTTONUP, this, nameof(OnLButtonUp));

            // onlbuttonclicked is DELIBERATELY NOT SUBSCRIBED - see the remarks.
        }
        else
        {
            // :L446  #DataWindow.of_Off(this) - by TARGET, so every subscription goes at once.
            _ = host.Eventful.Unsubscribe(this);
        }

        // :L448
        return 0L;
    }
}
