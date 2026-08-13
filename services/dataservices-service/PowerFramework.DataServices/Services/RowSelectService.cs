// ==============================================================================================
//  RowSelectService - the managed port of the DataWindow ROW SELECTION service.
//  --------------------------------------------------------------------------------------------
//  PORTED FROM
//      ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_rowselect.sru   (288 lines)
//
//  READ AS REFERENCE, NOT PORTED HERE
//      n_cst_dwsvc.sru        (864 lines) the common service base - ported to
//                             Domain/DataWindowServiceHost.cs as DataWindowServiceBase, including
//                             the shared property-reading helper layer this file consumes.
//      se_cst_dw.sru          (616 lines) the host. Supplies the three broker topics this service
//                             subscribes [:L49, :L60, :L63], the two semantic events it raises
//                             [:L24, :L26], and the `Filter()` override [:L403-L414] that invokes
//                             OnFiltered directly.
//      ne_cst_i18n.sru:L17    CAT_DWSVC, ported to Shared.Localization Categories.
//      retcode.sru            the return-code algebra, ported to Shared.Kernel RetCode.
//      sprintf.srf / i18n.srf the message-composition primitives, ported to Shared.Kernel
//                             Formatting.Sprintf and Shared.Localization I18n.
//
//  ORACLE STATUS  Every `ws_objects/**` path above is READ ONLY (constraint C-C) and is the
//                 behavioural oracle for parity testing. Nothing else in the repository can
//                 adjudicate behaviour (AAP 0.1.4), so every behaviour reproduced below carries the
//                 line locator it came from.
//
//  ============================================================================================
//  THIS SERVICE IS HEADLESS IN FULL - THE ONE OF ITS FOUR SIBLINGS THAT IS
//  ============================================================================================
//  AAP 0.2.1.3 Correction 4 measured the five attached DataWindow services for presentational
//  dependencies. ColumnSort, ContextMenu and DropDownSearch are irreducibly presentational in part
//  and are therefore SPLIT - a headless half ships and a rendering half is deferred to the
//  `/v1/design/**` Gateway extension point. RowSelect was measured as having EXACTLY ONE
//  presentational reference, and that one is the dialog at :L239. There is consequently NO
//  rendering half to carve off this file: converting the dialog is the whole of the presentational
//  work, and it is done in BuildRejectionError below.
//
//  A verification of that measurement, repeated here rather than taken on trust: the same sweep
//  Domain/DataWindowServiceHost.cs documents for its own two sources - the interop, scale and unit
//  conversion families, the font and popup-menu types, the theme manager, and the window
//  positioning and pointer calls - was run over n_cst_dwsvc_rowselect.sru and returns ZERO hits.
//  The only presentational call in all 288 lines is the modal dialog at :L239, which DECISION 3
//  below converts. `SetRedraw` is not presentational in the deferred sense - see the ruling on
//  DataWindowServiceHost.SetRedraw, which is called from in-scope logic, carries no geometry and is
//  satisfied as a no-op by a headless host.
//
//  This file is consequently free of every prohibited construct by inspection AND by grep: there is
//  no interop type, no scale or DPI conversion, no font or menu type, no pointer or window call, no
//  live keyboard read, no dialog, no bitwise style test, no generated contract type, and no
//  unimplemented member anywhere in it.
//
//  ============================================================================================
//  DECISION 1 - `Style` IS COMPARED BY EQUALITY, NOT AS A BITMASK. THIS IS THE FILE'S CENTRAL
//               BEHAVIOURAL FACT AND THE ONE MOST LIKELY TO BE "TIDIED" INTO A REGRESSION.
//  ============================================================================================
//  The declaration comment at :L19 reads `//Row select style (支持组合)` - "supports combination" -
//  and of_SetStyle at :L169 rejects only zero, so a COMBINED VALUE OF 3 IS LEGAL AND IS ACTUALLY
//  USED: the oracle's own test window sets it, at
//  ws_objects/pfw.tests.pbl.src/w_test_dwsvc_rowselect.srw's open event,
//  `dw_1.RowSelect.of_SetStyle(dw_1.RowSelect.RS_SINGLE + dw_1.RowSelect.RS_MULTIPLE)`.
//
//  BUT EVERY CONSUMPTION SITE IS AN EQUALITY OR INEQUALITY, AND NOT ONE IS A BIT TEST. All seven,
//  verified individually:
//      :L45   `#Style <> RS_SINGLE`        :L92   `#Style <> RS_MULTIPLE`
//      :L49   `#Style = RS_SINGLE`         :L106  `#Style <> RS_MULTIPLE`
//      :L52   `#Style <> RS_MULTIPLE`      :L175  `#Style <> RS_MULTIPLE`
//                                          :L277  `#Style <> RS_MULTIPLE`
//
//  CONSEQUENCE, WHICH IS WHY THIS IS RECORDED AT THE TOP OF THE FILE: a Style of 3 satisfies
//  NEITHER equality, so it reaches A THIRD DISTINCT BEHAVIOURAL PATH IN EVERY BRANCH. It is not the
//  union of the two named styles. Concretely, with Style == 3 the service performs range check-box
//  propagation on a plain click like RS_MULTIPLE (:L45 admits it), AND re-selects the clicked row
//  like RS_SINGLE (:L52, :L92, :L106, :L175 and :L277 all admit it because 3 <> 2).
//
//  Constraint C-B requires the comparison OPERATORS be reproduced literally. Rewriting them as a
//  bitwise test - Shared.Kernel's bit-test helper being the obvious candidate - would change
//  observable behaviour for Style == 3, the one value the oracle's own fixture uses. There is
//  deliberately no bitwise operation of any kind anywhere in this file, and a unit test pins the
//  Style == 3 behaviour precisely so a later "cleanup" cannot pass silently.
//
//  ============================================================================================
//  DECISION 2 - THE KEYBOARD-MODIFIER READS ARE INVERTED INTO EXPLICIT PARAMETERS
//  ============================================================================================
//  The legacy applies PowerScript's live key-state predicate to the Shift and Control key codes at
//  EIGHT reads across four lines: :L45 (two - Shift then Control), :L49 (four - Shift, Control,
//  Shift, Control), :L61 (one - Shift) and :L76 (one - Control). A headless Linux container has no
//  keyboard and no message queue to ask, and constraint C-D forbids reaching for a user-interface,
//  interop or DesignSystem type to obtain the answer - there is no placeholder standing in for one
//  either.
//
//  RESOLUTION: the click entry points take `shiftHeld` and `ctrlHeld` as explicit parameters,
//  supplied by the caller and carried inward from the request. THIS IS AN INVERSION OF AN INPUT
//  SOURCE, NOT A BEHAVIOUR CHANGE: given the same modifier state the same three branch conditions
//  evaluate identically, because the conditions themselves are reproduced unchanged. What moves is
//  only WHO reads the keyboard.
//
//  It also composes correctly with the event broker, which was verified rather than assumed.
//  EventBroker.PassArguments pre-fills every declared handler parameter with its PowerScript initial
//  value and then copies `Min(declared - consumed, payload)` arguments, so this six-parameter
//  handler subscribed to a topic triggered with four arguments [se_cst_dw.sru:L148] receives
//  `shiftHeld = false, ctrlHeld = false`. A broker-dispatched click therefore carries no modifiers,
//  which is the correct reading: the broker payload has none to carry.
//
//  ============================================================================================
//  DECISION 3 - THE DIALOG BECOMES A STRUCTURED ERROR RESULT, AND ONLY THE CHANNEL CHANGES
//  ============================================================================================
//  :L239 is the file's single presentational statement. AAP 0.2.1.3 Correction 5 and AAP 0.3.4
//  require that only the DELIVERY CHANNEL change, so RowSelectRejectionError carries the composed
//  text, BOTH message keys verbatim, the localization category, the substitution argument as data,
//  and the severity. It is never thrown - the legacy shows a dialog and continues to a controlled
//  exit, it does not raise - and it is never merely logged, because the caller has to be able to
//  surface it.
//
//  WHY IT TRAVELS ON A PROPERTY RATHER THAN ON THE RETURN VALUE. OnLButtonClk must keep returning
//  a `long`, because the broker compares the dispatch result against 1
//  [`if Eventful.of_Trigger(EVT_CLICKED,...) = 1 then return 1`, se_cst_dw.sru:L148]. Returning a
//  result object would break that test and change the prevent semantics of every click. PendingError
//  is therefore cleared at the entry of every click and left set when a rejection occurred, so it can
//  neither be missed nor surfaced twice.
//
//  THESE TWO MESSAGES DO LOCALIZE, AND THAT IS THE OPPOSITE OF THE COLUMN-EXPRESSION ENGINE'S 28.
//  Expressions/ParseErrorFormatter.cs reproduces messages that are hardcoded Chinese and bypass
//  I18n; these two route through I18n exactly as :L239 does. That inconsistency is legacy behaviour
//  and AAP 0.2.1.3 Correction 5 requires it be reproduced on BOTH sides, so ParseErrorFormatter is
//  deliberately not reused here and these two are deliberately not routed through it.
//
//  ============================================================================================
//  DECISION 4 - FOUR DEFECTS AND QUIRKS ARE PRESERVED, EACH ANNOTATED WHERE IT IS REPRODUCED
//  ============================================================================================
//      1. Style compared by equality rather than as a bitmask, so 3 is a third state - DECISION 1
//         above, annotated again at each of the seven comparison sites.
//      2. SetStyle validates AFTER its idempotent early-out, making the zero-argument rejection
//         unreachable - annotated in SetStyle.
//      3. SetCurrentRow clears its re-entrancy flag on the linear path with no `try`/`finally`, so an
//         exception leaves the flag set - annotated in SetCurrentRow.
//      4. CheckSelectedRows returns 1 even after a rejection, making a refused propagation
//         indistinguishable from a successful one - annotated in CheckSelectedRows.
//  None of the four is corrected. Constraint C-B: legacy quirks are replicated and documented, never
//  corrected, and each is annotated at its point of reproduction so a future reader cannot mistake it
//  for an implementation error.
//
//  ============================================================================================
//  WHAT THIS FILE DELIBERATELY DOES NOT DO
//  ============================================================================================
//      * It references NO generated contract type. Constraint C-A: the wire projection of this
//        behaviour belongs to Grpc/ and Endpoints/, which consume the plain domain types below.
//      * It holds NO selection set of its own. Selection is row state on the host
//        (DataWindowServiceHost.SelectRow / IsSelected / GetSelectedRow), exactly as the oracle has
//        it, so this service stays stateless apart from the two flags the legacy declares.
//      * It performs NO authentication or authorization, and holds NO credential material of any
//        kind - no signing or verification material, no account details, no connection string
//        (constraints C-G and C-F). It adds NO package and NO project reference (constraint C-I):
//        every type it touches arrives through references PowerFramework.DataServices.csproj already
//        declares.
//      * It imports nothing from its three sibling files in Services/. A cross-reference search over
//        all four legacy service objects finds zero live references between them, so there is no
//        intra-folder dependency to reproduce.
//      * It asserts NO performance property. AAP 0.8.5: the repository publishes no SLA, no latency
//        budget and no throughput target, so nothing here is justified on performance grounds. Every
//        shape below is justified by a locator instead.
// ==============================================================================================

using System.Globalization;

using Microsoft.Extensions.Options;

using PowerFramework.DataServices.Configuration;
using PowerFramework.DataServices.Domain;
using PowerFramework.Shared.Kernel;
using PowerFramework.Shared.Localization;

namespace PowerFramework.DataServices.Services;

/// <summary>
/// The severity a row-selection error carries - the port of the PowerScript <c>Icon</c> argument
/// passed to the modal dialog at
/// <c>ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_rowselect.sru:L239</c>.
/// </summary>
/// <remarks>
/// <para>
/// TWO MEMBERS ONLY, AND THE ABSENT ONES ARE ABSENT ON PURPOSE. PowerScript's <c>Icon</c> enumeration
/// also carries information, exclamation, question and none, but this service produces exactly one
/// value - <c>StopSign!</c> at <c>:L239</c>, its only dialog. Declaring the rest would fabricate a
/// taxonomy no ported call site reaches, which constraint C-B forbids.
/// </para>
/// <para>
/// <see cref="None"/> exists so the enumeration has a well-defined zero, which is what a default
/// <see langword="default"/> value binds to; it is never produced by this service.
/// </para>
/// </remarks>
public enum RowSelectMessageIcon
{
    /// <summary>
    /// No icon. The enumeration's zero value; never produced by this service.
    /// </summary>
    None = 0,

    /// <summary>
    /// The stop-sign icon - the port of PowerScript's <c>StopSign!</c>, the value passed at
    /// <c>n_cst_dwsvc_rowselect.sru:L239</c>.
    /// </summary>
    StopSign = 1,
}

/// <summary>
/// The structured replacement for the row-selection service's single dialog: a data change that the
/// host REFUSED while propagating a check-box value across the selected rows.
/// </summary>
/// <remarks>
/// <para>
/// PORTED FROM <c>n_cst_dwsvc_rowselect.sru:L239</c>, whose two arguments to the modal dialog are,
/// byte for byte, the message
/// <c>Sprintf(I18N(ne_cst_i18n.CAT_DWSVC,"第{}行"),nRow) + "~n" +
/// I18N(ne_cst_i18n.CAT_DWSVC,"修改数据被拒绝") + "!"</c>
/// and the severity <c>StopSign!</c>.
/// </para>
/// <para>
/// EVERY COMPOSITION INPUT IS CARRIED, NOT JUST THE RENDERED TEXT. <see cref="Text"/> alone would be
/// enough to show a user, but not enough to verify parity against the oracle or to re-render the
/// message under a different locale: the two lookup keys, the category and the substitution argument
/// are the inputs a characterization comparison actually asserts on, so all four travel as data. AAP
/// 0.3.4 requires exactly that - the original text, the localization category, the <c>Sprintf</c>
/// arguments and the severity.
/// </para>
/// <para>
/// NOTHING PRESENTATIONAL IS CARRIED. There is no window, owner, button set, caption, position or
/// modality here, because a structured error is not a dialog and constraint C-D keeps the rendering
/// half out. The severity is the one dialog attribute the legacy supplies and it is preserved as
/// data.
/// </para>
/// </remarks>
public sealed record RowSelectRejectionError
{
    /// <summary>
    /// The composed message, exactly as the legacy dialog would have displayed it.
    /// </summary>
    /// <remarks>
    /// Composed as <c>Sprintf(I18N(category, RowFormatTemplate), Row)</c> +
    /// <see cref="RowSelectService.LineSeparator"/> + <c>I18N(category, RejectionMessageKey)</c> +
    /// <see cref="RowSelectService.RejectionMessageSuffix"/>, in that order - the operand order of
    /// <c>:L239</c>. TWO SEPARATE I18n LOOKUPS, not one lookup of a joined string: the legacy
    /// translates each fragment independently, so a provider may translate one and pass the other
    /// through.
    /// </remarks>
    public required string Text { get; init; }

    /// <summary>
    /// The localization category both lookups were made under - always
    /// <c>Categories.CAT_DWSVC</c>.
    /// </summary>
    /// <remarks>
    /// Carried as data rather than implied, because a characterization recording of this error has to
    /// be comparable against the legacy's own, and the category selects which translation table
    /// answered. It is <c>Enums.I18N_CAT_CUSTOM + 2</c> - a COMPUTED OFFSET
    /// [<c>ne_cst_i18n.sru:L17</c>] - and is always consumed through the named constant, never as the
    /// literal it evaluates to.
    /// </remarks>
    public required long LocalizationCategory { get; init; }

    /// <summary>
    /// The first lookup key, verbatim: the row-number fragment, which is also a
    /// <c>Formatting.Sprintf</c> format string.
    /// </summary>
    /// <remarks>
    /// Always <see cref="RowSelectService.RowNumberMessageKey"/>. It is BOTH a lookup key and a format
    /// string, which is why the legacy translates it first and formats the translation second: a
    /// provider is expected to return a translation that still contains the placeholder.
    /// </remarks>
    public required string RowFormatTemplate { get; init; }

    /// <summary>
    /// The second lookup key, verbatim: the rejection fragment.
    /// </summary>
    /// <remarks>
    /// Always <see cref="RowSelectService.RejectionMessageKey"/>. Carries no placeholder and is not
    /// formatted.
    /// </remarks>
    public required string RejectionMessageKey { get; init; }

    /// <summary>
    /// The one-based row number the rejection applies to - the <c>Sprintf</c> substitution argument,
    /// preserved as data and not only pre-rendered into <see cref="Text"/>.
    /// </summary>
    /// <remarks>
    /// This is <c>nRow</c>, the row the inner propagation loop had reached
    /// [<c>n_cst_dwsvc_rowselect.sru:L224</c>, passed at <c>:L239</c>] - NOT the row that was clicked.
    /// The two differ whenever more than one row is selected, which is the only situation in which
    /// this error can arise at all.
    /// </remarks>
    public required long Row { get; init; }

    /// <summary>
    /// The severity, always <see cref="RowSelectMessageIcon.StopSign"/> - the legacy
    /// <c>StopSign!</c>.
    /// </summary>
    public required RowSelectMessageIcon Icon { get; init; }

    /// <summary>
    /// Whether the message text was produced through the localization facade. Always
    /// <see langword="true"/> for this error.
    /// </summary>
    /// <remarks>
    /// Recorded as data because the answer is NOT uniform across the DataWindow service layer, and the
    /// difference is a preserved legacy inconsistency rather than an accident: this service's two
    /// messages route through <c>I18N</c> [<c>:L239</c>], while the column-expression engine's 28
    /// messages are hardcoded Chinese that bypass it entirely. A consumer that assumed one answer for
    /// the whole layer would be wrong half the time, so each error states its own.
    /// </remarks>
    public required bool Localized { get; init; }
}

/// <summary>
/// The DataWindow row-selection service: single and multiple row selection driven by the left mouse
/// button with the Shift and Control modifiers, plus range check-box propagation across the selected
/// rows.
/// </summary>
/// <remarks>
/// <para>
/// PORTED FROM <c>ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_rowselect.sru</c> (288
/// lines), which derives from <c>n_cst_dwsvc</c> [<c>:L4</c>, <c>:L8</c>] - so this type derives from
/// <see cref="DataWindowServiceBase"/>, that object's port.
/// </para>
/// <para>
/// <c>SEALED</c> BECAUSE NOTHING DERIVES FROM IT. A search of the legacy library for
/// <c>from n_cst_dwsvc_rowselect</c> returns nothing: the oracle's own hierarchy stops here, and
/// <c>se_cst_dw</c> holds it as a concrete member rather than as a base
/// [<c>se_cst_dw.sru:L81</c>]. Sealing states that, and keeps the four preserved defects from being
/// silently overridden away by a subclass.
/// </para>
/// <para>
/// THE SELF-SHADOWING GLOBAL IS NOT REPRODUCED. <c>:L14</c> declares
/// <c>global n_cst_dwsvc_rowselect n_cst_dwsvc_rowselect</c>, a global auto-instance shadowing its own
/// type name - an artefact of PowerBuilder's single flat namespace (AAP 0.4.5.1). AAP 0.5.4.2 fixes
/// the resolution: the type keeps the descriptive .NET name and the instance becomes an injected
/// dependency. Nothing here is static and mutable, which is also what makes the whole service testable
/// against a host test double with no DataWindow, no database and no UI (constraint C-H).
/// </para>
/// <para>
/// NOT THREAD-SAFE, AND THE OBJECT IT PORTS WAS NOT EITHER. The two flags below and
/// <see cref="PendingError"/> are plain instance state mutated across a call sequence, exactly as
/// <c>:L29-L30</c> declares them. One instance belongs to one attached host and is driven by that
/// host's event chain in order; concurrent use of a single instance is outside the contract, as it is
/// in the oracle.
/// </para>
/// <para>
/// IT IMPLEMENTS <see cref="IDataWindowRowSelectService"/>, WHICH IS ONE OF THE EVENT CHAIN'S FIVE
/// ATTACHED-SERVICE CONTRACT. It adds one member beyond the two every attached service has -
/// OnFiltered, the port of `RowSelect.Event OnFiltered()` [se_cst_dw.sru:L408-L409] -
/// and this class already declares it public for exactly that raise, so satisfying the interface
/// costs nothing and declaring it removes the need for an adapter between this model and an event
/// chain.
/// </para>
/// </remarks>
public sealed class RowSelectService : DataWindowServiceBase, IDataWindowRowSelectService
{
    // ==========================================================================================
    //  CONSTANTS - :L17-L21, SPELLINGS PRESERVED VERBATIM
    //  ----------------------------------------------------------------------------------------
    //  The legacy comment above them, carried across because it is the source of DECISION 1 in the
    //  file header:
    //      :L19   //Row select style (支持组合)          "supports combination"
    //
    //  The SCREAMING_SNAKE spellings depart from C# convention deliberately (AAP 0.4.5.3): these
    //  values travel in serialized payloads, log records and characterization recordings, where a
    //  rename would silently invalidate every stored comparison. The root .editorconfig carries a
    //  scoped naming-analyzer suppression for this file, in the band that already covers RetCode.cs,
    //  Enums.cs, Categories.cs, EventGate.cs and ItemChangeProtocol.cs.
    // ==========================================================================================

    /// <summary>
    /// Single-row selection - the port of <c>constant long RS_SINGLE = 1</c>
    /// (<c>n_cst_dwsvc_rowselect.sru:L20</c>, legacy comment <c>//单选</c>, "single select").
    /// </summary>
    /// <remarks>
    /// The default <see cref="Style"/>. Compared with <c>=</c> at <c>:L49</c> and with <c>&lt;&gt;</c>
    /// at <c>:L45</c> - NEVER with a bit test. See DECISION 1 in the file header.
    /// </remarks>
    public const long RS_SINGLE = 1L;

    /// <summary>
    /// Multiple-row selection - the port of <c>constant long RS_MULTIPLE = 2</c>
    /// (<c>n_cst_dwsvc_rowselect.sru:L21</c>, legacy comment <c>//多选</c>, "multiple select").
    /// </summary>
    /// <remarks>
    /// Compared with <c>&lt;&gt;</c> at <c>:L52</c>, <c>:L92</c>, <c>:L106</c>, <c>:L175</c> and
    /// <c>:L277</c> - NEVER with a bit test. A <see cref="Style"/> of
    /// <c>RS_SINGLE + RS_MULTIPLE</c> equals neither constant and therefore behaves as a third state
    /// at all five sites. See DECISION 1 in the file header.
    /// </remarks>
    public const long RS_MULTIPLE = 2L;

    /// <summary>
    /// The line separator joining the two fragments of the rejection message - the port of the
    /// PowerScript escape <c>"~n"</c> at <c>n_cst_dwsvc_rowselect.sru:L239</c>.
    /// </summary>
    /// <remarks>
    /// A BARE LINE FEED, NOT A CARRIAGE-RETURN PAIR. PowerScript spells a carriage return
    /// <c>~r</c> and a line feed <c>~n</c> separately, and <c>:L239</c> uses only <c>~n</c> - unlike
    /// the assert payload protocol at <c>ws_objects/pfw.pbl.src/pfw.sra:L114</c> - the framework
    /// application, not the same-named packager object - which splits on <c>~r~n</c>. Emitting
    /// <c>Environment.NewLine</c> here would make the composed text platform-dependent and would
    /// differ from the oracle on Windows, so the literal is fixed.
    /// </remarks>
    public const string LineSeparator = "\n";

    /// <summary>
    /// The first localization lookup key of the rejection message, verbatim - <c>"第{}行"</c>, "row
    /// {}" (<c>n_cst_dwsvc_rowselect.sru:L239</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// NOT TRANSLATED IN SOURCE, NOT NORMALISED, AND NOT MOVED TO A RESOURCE FILE. This IS the lookup
    /// key <c>I18n</c> resolves - the legacy's base locale is Simplified Chinese, so the Chinese text
    /// is the key rather than a translation of one. Rewriting it in any way would stop every
    /// translation table in <c>pfw.i18n.xml</c> from matching, and the localization would silently
    /// fall back to passthrough.
    /// </para>
    /// <para>
    /// It doubles as a <c>Formatting.Sprintf</c> format string in the SEQUENTIAL EMPTY-BRACE form:
    /// <c>{}</c> carries no index, so it takes the next argument. The indexed form <c>{1}</c> is a
    /// lookup that does not advance the cursor and is not used here.
    /// </para>
    /// </remarks>
    public const string RowNumberMessageKey = "第{}行";

    /// <summary>
    /// The second localization lookup key of the rejection message, verbatim -
    /// <c>"修改数据被拒绝"</c>, "the data change was refused"
    /// (<c>n_cst_dwsvc_rowselect.sru:L239</c>).
    /// </summary>
    /// <remarks>
    /// See <see cref="RowNumberMessageKey"/> for why the Chinese text is the key itself. This fragment
    /// carries no placeholder and is not passed through <c>Sprintf</c>.
    /// </remarks>
    public const string RejectionMessageKey = "修改数据被拒绝";

    /// <summary>
    /// The literal appended after the second fragment - <c>"!"</c>
    /// (<c>n_cst_dwsvc_rowselect.sru:L239</c>).
    /// </summary>
    /// <remarks>
    /// OUTSIDE THE LOOKUP AND THEREFORE NEVER TRANSLATED. <c>:L239</c> concatenates it after the
    /// <c>I18N</c> call rather than including it in the key, so a locale that punctuates differently
    /// still gets this exact character. Folding it into
    /// <see cref="RejectionMessageKey"/> would change the key and break every table match.
    /// </remarks>
    public const string RejectionMessageSuffix = "!";

    // ==========================================================================================
    //  BROKER TOPICS - THE THREE THIS SERVICE SUBSCRIBES, AND ONLY THOSE THREE
    //  ----------------------------------------------------------------------------------------
    //  Values taken verbatim from se_cst_dw.sru: :L60 EVT_CLICKED, :L63 EVT_DOUBLECLICKED,
    //  :L49 EVT_ROWFOCUSCHANGED.
    //
    //  DECLARED PRIVATELY HERE RATHER THAN CONSUMED FROM THE HOST CONTRACT, AND THAT IS A
    //  DELIBERATE OWNERSHIP DECISION. Domain/DataWindowServiceHost.cs explicitly assigns the twelve
    //  EVT_* topics to Domain/DataWindowEventChain.cs, the port of se_cst_dw that declares them.
    //  Promoting them onto the host contract would put a constant on a BASE that its DERIVED type
    //  also declares, which reports CS0108 and - with warnings promoted to errors repository wide -
    //  fails the build. Respecting the stated boundary keeps that from happening, and the cost is
    //  bounded: these are three string literals, not behaviour, and a unit test pins their values
    //  against the oracle so the duplication cannot drift unnoticed.
    //
    //  Only three of this service's four events are broker-wired - see OnEnable.
    // ==========================================================================================

    /// <summary>
    /// The click topic - <c>se_cst_dw.sru:L60</c>, subscribed at
    /// <c>n_cst_dwsvc_rowselect.sru:L274</c>.
    /// </summary>
    private const string EVT_CLICKED = "clicked";

    /// <summary>
    /// The double-click topic - <c>se_cst_dw.sru:L63</c>, subscribed at
    /// <c>n_cst_dwsvc_rowselect.sru:L275</c>.
    /// </summary>
    private const string EVT_DOUBLECLICKED = "doubleclicked";

    /// <summary>
    /// The row-focus-changed topic - <c>se_cst_dw.sru:L49</c>, subscribed at
    /// <c>n_cst_dwsvc_rowselect.sru:L276</c>.
    /// </summary>
    private const string EVT_ROWFOCUSCHANGED = "rowfocuschanged";

    /// <summary>
    /// The check-box property value that admits range propagation - <c>"checkbox"</c>
    /// (<c>n_cst_dwsvc_rowselect.sru:L195</c>).
    /// </summary>
    /// <remarks>
    /// Compared with <c>&lt;&gt;</c> and therefore CASE-SENSITIVELY, as PowerScript string comparison
    /// is. A column whose edit style Describe answers <c>"CheckBox"</c> would not match, and that is
    /// the oracle's behaviour rather than an oversight.
    /// </remarks>
    private const string CheckBoxEditStyle = "checkbox";

    /// <summary>
    /// The DataWindow's invalid-expression sentinel - <c>"!"</c>
    /// (<c>n_cst_dwsvc_rowselect.sru:L197</c>, <c>:L199</c>).
    /// </summary>
    private const string InvalidExpressionSentinel = "!";

    /// <summary>
    /// The DataWindow's undetermined-value sentinel - <c>"?"</c>
    /// (<c>n_cst_dwsvc_rowselect.sru:L197</c>, <c>:L199</c>).
    /// </summary>
    /// <remarks>
    /// Returned when a property cannot be answered for the current context - for example a property
    /// that differs across a selection. Both sentinels abandon the propagation, because neither is a
    /// usable check-box value.
    /// </remarks>
    private const string UndeterminedValueSentinel = "?";

    /// <summary>
    /// The localization facade, injected. The port of the <c>i18n.srf</c> global function family.
    /// </summary>
    /// <remarks>
    /// <c>I18n</c> IS A CLASS WITH AN INSTANCE PROVIDER SLOT, NOT A STATIC HELPER, so it is taken by
    /// dependency injection rather than reached statically. With no provider installed it returns the
    /// text unchanged - the SILENT PASSTHROUGH fallback of <c>i18n.srf</c>, which never throws, never
    /// logs and never marks the text untranslated - so this service composes a usable message whether
    /// or not a locale has been configured.
    /// </remarks>
    private readonly I18n _i18n;

    // ==========================================================================================
    //  IMPLEMENTATION STATE - :L29-L30, TWO PRIVATE FLAGS, NAMES KEPT TRACEABLE TO THE ORACLE
    // ==========================================================================================

    /// <summary>
    /// Set while this service is itself moving the current row - the port of
    /// <c>boolean _inChangeRowFocus</c> (<c>:L29</c>, legacy comment <c>//正在改变当前行</c>,
    /// "currently changing the current row").
    /// </summary>
    /// <remarks>
    /// A RE-ENTRANCY GUARD, AND THE ONLY THING THAT STOPS A FEEDBACK LOOP. Moving the row raises the
    /// host's row-focus-changed event, which is subscribed to
    /// <see cref="OnRowFocusChanged(long)"/>, which would clear and re-apply the selection this
    /// service is in the middle of computing. <c>:L88</c> reads the flag and returns immediately; only
    /// <see cref="SetCurrentRow(in long)"/> sets it.
    /// </remarks>
    private bool _inChangeRowFocus;

    /// <summary>
    /// Whether the current selection was produced by a multi-select gesture - the port of
    /// <c>boolean _bHasMultiSelected</c> (<c>:L30</c>, legacy comment
    /// <c>//当前是否有多行选择</c>, "whether there are currently multiple rows selected").
    /// </summary>
    /// <remarks>
    /// <para>
    /// NOT A ROW COUNT AND NOT DERIVED FROM ONE. It records that a Shift or Control gesture happened,
    /// and it is set to <see langword="true"/> unconditionally at <c>:L83</c> - reached even when the
    /// Control gesture DESELECTED a row and left nothing selected at all. Computing it from the
    /// selection instead would answer differently in exactly that case.
    /// </para>
    /// <para>
    /// It gates two things: the range check-box propagation, which <c>:L194</c> refuses unless the
    /// flag is set, and the "re-select the focus row first" arm of the Control gesture at
    /// <c>:L77</c>. It is cleared by every gesture that collapses the selection to one row -
    /// <c>:L55</c>, <c>:L96</c>, <c>:L110</c>, <c>:L178</c> and <c>:L283</c>.
    /// </para>
    /// </remarks>
    private bool _bHasMultiSelected;

    /// <summary>
    /// Creates the service.
    /// </summary>
    /// <param name="i18n">
    /// The localization facade used to compose the rejection message. See <see cref="_i18n"/>.
    /// </param>
    /// <param name="options">
    /// The service configuration. Only <c>DataServices:RowSelect:Style</c> is read, and only here.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="i18n"/> or <paramref name="options"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// THE DEFAULT STYLE IS BOUND FROM CONFIGURATION RATHER THAN HARDCODED A SECOND TIME.
    /// <c>:L25</c> declares <c>privatewrite long #Style = RS_SINGLE</c>, and
    /// Configuration/DataServicesOptions.cs already carries that value as
    /// <c>RowSelectOptions.Style</c> with a declared default of <c>1</c>, reproduced from the same
    /// line. Reading it here means the preserved legacy default lives in exactly one place: the
    /// defect being remediated is the HARDCODING, not the value, so the value is preserved as the
    /// default and made overridable (AAP 0.4.5.5).
    /// </para>
    /// <para>
    /// THE CONFIGURED VALUE IS NOT VALIDATED HERE, AND THAT IS NOT AN OVERSIGHT. <c>:L169</c>'s
    /// zero-rejection guard belongs to <see cref="SetStyle(in long)"/>, which is the legacy's setter;
    /// the initial value is an assignment at <c>:L25</c> and passes through no guard at all. Options
    /// validation at startup covers the configured value - the options type documents that its
    /// validator rejects zero - so adding a second guard here would either duplicate that or, worse,
    /// silently substitute a different default than the operator configured.
    /// </para>
    /// <para>
    /// The null guards are fail-fast substitutions rather than new behaviour: the legacy would fault
    /// on the first use of a null reference, and a named
    /// <see cref="ArgumentNullException"/> fails just as fast while saying which argument was wrong
    /// (AAP 0.1.4 requires the fail-fast posture be preserved AS fail-fast).
    /// </para>
    /// </remarks>
    public RowSelectService(I18n i18n, IOptions<DataServicesOptions> options)
    {
        ArgumentNullException.ThrowIfNull(i18n);
        ArgumentNullException.ThrowIfNull(options);

        _i18n = i18n;

        // n_cst_dwsvc_rowselect.sru:L25 - `privatewrite long #Style = RS_SINGLE`, bound from
        // DataServices:RowSelect:Style whose declared default reproduces that same line.
        Style = options.Value.RowSelect.Style;
    }

    /// <summary>
    /// The row-selection style - the port of <c>privatewrite long #Style</c>
    /// (<c>n_cst_dwsvc_rowselect.sru:L25</c>, legacy comment <c>//选择风格</c>, "selection style").
    /// </summary>
    /// <value>
    /// <see cref="RS_SINGLE"/>, <see cref="RS_MULTIPLE"/>, or any other non-zero value - most
    /// meaningfully <c>RS_SINGLE + RS_MULTIPLE</c>, which the oracle's own test window sets and which
    /// behaves as a THIRD STATE rather than as the union of the two. See DECISION 1 in the file
    /// header.
    /// </value>
    /// <remarks>
    /// <para>
    /// EXTERNALLY READABLE, WRITABLE ONLY THROUGH <see cref="SetStyle(in long)"/>. The private setter
    /// is the port of PowerScript's <c>privatewrite</c> and it is load-bearing: <c>SetStyle</c> carries
    /// side effects on the current selection [<c>:L173-L179</c>], so a settable property would let a
    /// caller change the style without them and leave the selection inconsistent with it.
    /// </para>
    /// <para>
    /// Typed <see cref="long"/>, matching the legacy <c>long</c> declaration, and NOT a two-member
    /// enumeration - a closed enumeration could not represent the combined value the oracle uses (AAP
    /// 0.4.5.2 maps <c>long</c> onto <c>long</c>).
    /// </para>
    /// </remarks>
    public long Style { get; private set; }

    /// <summary>
    /// The structured error produced by the most recent click, or <see langword="null"/> when that
    /// click produced none - the replacement for the dialog at
    /// <c>n_cst_dwsvc_rowselect.sru:L239</c>.
    /// </summary>
    /// <value>
    /// The rejection error, or <see langword="null"/>.
    /// </value>
    /// <remarks>
    /// <para>
    /// CLEARED AT THE ENTRY OF EVERY CLICK, INCLUDING CLICKS THAT CANNOT PRODUCE ONE. That is what
    /// makes the value unambiguous: it is always the outcome of the most recent call to
    /// <see cref="OnLButtonClk(long, long, long, IDataWindowObject, bool, bool)"/> or
    /// <see cref="OnLButtonDblClk(long, long, long, IDataWindowObject, bool, bool)"/>, never a stale
    /// error from an earlier one, and it can therefore neither be missed nor surfaced twice.
    /// </para>
    /// <para>
    /// It travels on a property rather than on the return value because the click handlers must keep
    /// returning a <see cref="long"/> for the broker's <c>= 1</c> prevent test
    /// [<c>se_cst_dw.sru:L148</c>]. See DECISION 3 in the file header.
    /// </para>
    /// <para>
    /// The other two events do not touch it: neither <see cref="OnRowFocusChanged(long)"/> nor
    /// <see cref="OnFiltered"/> can reach the propagation path, so neither clears nor sets it.
    /// </para>
    /// </remarks>
    public RowSelectRejectionError? PendingError { get; private set; }

    // ==========================================================================================
    //  THE FOUR EVENTS - :L9-L12 DECLARED, :L40-L114 IMPLEMENTED
    //  ----------------------------------------------------------------------------------------
    //      onlbuttonclk       pbm_dwnlbuttonclk       :L9    body :L40-L86
    //      onrowfocuschanged  pbm_dwnrowchange        :L10   body :L88-L99
    //      onfiltered()       NO pbm MAPPING          :L11   body :L101-L111
    //      onlbuttondblclk    pbm_dwnlbuttondblclk    :L12   body :L113
    //
    //  Three of the four are broker-wired; onfiltered is invoked directly by the host - see OnEnable.
    // ==========================================================================================

    /// <summary>
    /// Handles a left-button click - the port of <c>event onlbuttonclk</c>
    /// (<c>n_cst_dwsvc_rowselect.sru:L9</c>, body <c>:L40-L86</c>), mapped in the legacy to
    /// <c>pbm_dwnlbuttonclk</c>.
    /// </summary>
    /// <param name="xpos">The pointer x position, carried and never converted.</param>
    /// <param name="ypos">The pointer y position, carried and never converted.</param>
    /// <param name="row">The one-based row under the pointer, or <c>0</c> when none.</param>
    /// <param name="dwo">The column under the pointer.</param>
    /// <param name="shiftHeld">
    /// Whether Shift was held - the inversion of the legacy live Shift key-state read at <c>:L45</c>,
    /// <c>:L49</c> and <c>:L61</c>. See DECISION 2 in the file header.
    /// </param>
    /// <param name="ctrlHeld">
    /// Whether Control was held - the inversion of the legacy live Control key-state read at
    /// <c>:L45</c>, <c>:L49</c> and <c>:L76</c>.
    /// </param>
    /// <returns>
    /// <c>1</c> to PREVENT the DataWindow's own click handling, <c>0</c> to allow it. This is the
    /// prevent convention, not the return-code algebra: <c>0</c> here means "continue", whereas
    /// <c>RetCode.OK</c> also happens to be <c>0</c> and means success. The two are not the same
    /// domain.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// This service has not been attached to a host yet.
    /// </exception>
    /// <remarks>
    /// <para>
    /// FOUR MUTUALLY EXCLUSIVE OUTCOMES, IN THE ORACLE'S ORDER. Reordering them changes behaviour,
    /// because the second arm's condition is only correct given that the first has already run:
    /// </para>
    /// <para>
    /// 1. <c>:L42</c> no row under the pointer - return <c>0</c> and do nothing.
    /// </para>
    /// <para>
    /// 2. <c>:L45-L47</c> a PLAIN click while not in single-select style - attempt range check-box
    /// propagation, and if it happened, return <c>1</c> and stop. Note the propagation runs BEFORE the
    /// selection is collapsed by arm 3, which is why it can still see the multi-row selection it
    /// propagates across.
    /// </para>
    /// <para>
    /// 3. <c>:L49-L57</c> single-select style, OR no modifier, OR BOTH MODIFIERS - collapse the
    /// selection to the clicked row and return <c>0</c>. THE BOTH-MODIFIERS CASE DELIBERATELY
    /// COLLAPSES INTO THE PLAIN-CLICK CASE: <c>:L49</c>'s third disjunct is
    /// the conjunction of the two live key-state reads, so Shift+Control behaves as a plain click
    /// rather than as either gesture. That is preserved.
    /// </para>
    /// <para>
    /// 4. <c>:L59-L85</c> exactly one modifier, and not single-select style - the range gesture
    /// (<paramref name="shiftHeld"/>) or the toggle gesture (<paramref name="ctrlHeld"/>), then mark
    /// the selection multi and return <c>1</c>.
    /// </para>
    /// <para>
    /// STYLE IS TESTED BY EQUALITY THROUGHOUT. A <see cref="Style"/> of <c>3</c> takes arm 2 like
    /// <see cref="RS_MULTIPLE"/> and the re-select at <c>:L53</c> like <see cref="RS_SINGLE"/> - the
    /// third state of DECISION 1.
    /// </para>
    /// </remarks>
    public long OnLButtonClk(
        long xpos,
        long ypos,
        long row,
        IDataWindowObject dwo,
        bool shiftHeld,
        bool ctrlHeld)
    {
        // Cleared on entry so PendingError is always the outcome of THIS click and never a stale
        // error from an earlier one. See DECISION 3 in the file header. Cleared before the :L42
        // early-out as well, because a click that does nothing has produced no error either.
        PendingError = null;

        // n_cst_dwsvc_rowselect.sru:L42
        if (row <= 0L)
        {
            return 0L;
        }

        // BOTH GUARDS SIT AFTER THE :L42 EARLY-OUT, DELIBERATELY. The oracle's no-row path touches
        // neither `#DataWindow` nor `dwo` [:L42], so it cannot fault on either - and a click landing
        // outside the rows, on a header or in the empty space below the last row, is an ORDINARY
        // event rather than an error. Validating before the early-out would turn that no-op into an
        // exception for an unattached service, which constraint C-B forbids. From here on both are
        // genuinely required, and failing fast with a named cause is the posture AAP 0.1.4 requires
        // be preserved AS fail-fast.
        ArgumentNullException.ThrowIfNull(dwo);
        DataWindowServiceHost host = RequireHost();

        // :L44 legacy comment: "开启多行选择服务时，提供范围勾选的功能" - when the multi-row
        // selection service is on, provide range check-box selection.
        // :L45 - EQUALITY, NOT A BIT TEST (DECISION 1): Style == 3 satisfies `<> RS_SINGLE` and so
        // reaches the propagation, exactly as RS_MULTIPLE does.
        if (Style != RS_SINGLE && !shiftHeld && !ctrlHeld)
        {
            // :L46 - only a propagation that actually happened suppresses the DataWindow's own
            // handling. See CheckSelectedRows for why a REFUSED propagation also answers 1.
            if (CheckSelectedRows(row, dwo) == 1L)
            {
                return 1L;
            }
        }

        // :L49 - three disjuncts. The third, `(shiftHeld && ctrlHeld)`, is what makes Shift+Control
        // behave as a plain click; it is preserved rather than folded away.
        if (Style == RS_SINGLE || (!shiftHeld && !ctrlHeld) || (shiftHeld && ctrlHeld))
        {
            // :L50 - a refused row move PREVENTS the click outright, leaving the selection untouched.
            if (!SetCurrentRow(row))
            {
                return 1L;
            }

            // :L51 - row 0 means EVERY row: clear the whole selection.
            host.SelectRow(0L, false);

            // :L52 - EQUALITY, NOT A BIT TEST (DECISION 1): Style == 3 satisfies `<> RS_MULTIPLE`, so
            // the clicked row IS re-selected for the combined style and is NOT for RS_MULTIPLE.
            if (Style != RS_MULTIPLE)
            {
                // :L53
                host.SelectRow(row, true);
            }

            // :L55
            _bHasMultiSelected = false;

            // :L56
            return 0L;
        }

        // :L59 - the row that held focus BEFORE this click, which is the anchor of the range gesture.
        // Read after arm 3 has returned, so it is never the row just moved to.
        long nFocusRow = host.GetRow();

        // :L61 legacy comment "范围选" - range select.
        if (shiftHeld)
        {
            // :L62 - repaint suppressed across the whole walk, and restored at :L75 on both paths.
            host.SetRedraw(false);

            // :L63
            host.SelectRow(0L, false);

            // THE LEGACY MUTATES ITS OWN `row` PARAMETER as the walk cursor [:L67, :L72]. A C#
            // parameter would be mutable too, but mutating it would leave `row` meaning something
            // different after the branch than before it - and :L80 in the sibling branch still needs
            // the original. A local cursor states the intent and keeps the two branches independent.
            long walkRow = row;

            // :L64 - the direction test is on the ORIGINAL clicked row against the anchor.
            if (walkRow > nFocusRow)
            {
                // :L65-L68 - downward-to-upward walk, INCLUSIVE OF BOTH ENDS. The condition is
                // `>=`, so the anchor row itself is selected on the final iteration.
                while (walkRow >= nFocusRow)
                {
                    host.SelectRow(walkRow, true);
                    walkRow--;
                }
            }
            else
            {
                // :L70-L73 - upward-to-downward walk, also INCLUSIVE OF BOTH ENDS. Reached when the
                // clicked row equals the anchor as well, in which case the single row is selected once.
                while (walkRow <= nFocusRow)
                {
                    host.SelectRow(walkRow, true);
                    walkRow++;
                }
            }

            // :L75
            host.SetRedraw(true);
        }
        else if (ctrlHeld)
        {
            // :L76 legacy comment "单选" - individual (toggle) select.
            // :L77 - THE ANCHOR IS RE-SELECTED FIRST, BUT ONLY ON THE FIRST CONTROL-CLICK OF A RUN.
            // Without this the anchor row would be silently dropped from the selection when the user
            // begins a Control gesture, because the DataWindow's own highlight of the current row is
            // not a selection. `_bHasMultiSelected` is what distinguishes the first Control-click from
            // the rest, and the `nFocusRow != row` test stops the anchor being selected and then
            // immediately toggled by the same click.
            if (!_bHasMultiSelected && nFocusRow != row)
            {
                // :L78
                host.SelectRow(nFocusRow, true);
            }

            // :L80 - the toggle itself. Reads the CURRENT selected state, so this both selects and
            // deselects; a Control-click that deselects still falls through to :L83 below.
            host.SelectRow(row, !host.IsSelected(row));
        }

        // :L83 - UNCONDITIONAL, and reached even when the Control gesture left nothing selected. See
        // the remarks on _bHasMultiSelected for why this is not derived from the selection.
        _bHasMultiSelected = true;

        // :L85
        return 1L;
    }

    /// <summary>
    /// Handles a change of the current row - the port of <c>event onrowfocuschanged</c>
    /// (<c>n_cst_dwsvc_rowselect.sru:L10</c>, body <c>:L88-L99</c>), mapped in the legacy to
    /// <c>pbm_dwnrowchange</c>.
    /// </summary>
    /// <param name="currentRow">The new current row, one-based, or <c>0</c> when there is none.</param>
    /// <returns>
    /// Always <c>0</c> - this handler never prevents. Both exits return it: the re-entrancy guard at
    /// <c>:L88</c> and the normal path at <c>:L98</c>.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// This service has not been attached to a host yet, on the non-guarded path.
    /// </exception>
    /// <remarks>
    /// <para>
    /// KEEPS THE SELECTION IN STEP WITH THE CURRENT ROW when the row changes by any means other than a
    /// click - a keyboard arrow, a scroll, a programmatic move, or a delete that shifts the cursor
    /// [<c>se_cst_dw.sru:L430-L437</c> raises the raw row-change event after a delete].
    /// </para>
    /// <para>
    /// THE RE-ENTRANCY GUARD IS THE FIRST STATEMENT AND MUST STAY THERE. <c>:L88</c> returns
    /// immediately while <see cref="SetCurrentRow(in long)"/> is moving the row on this service's own
    /// behalf. Without it, arm 3 of the click handler would move the row, be called back here, have its
    /// selection cleared and rebuilt from the new current row, and the click's own selection work would
    /// be discarded. IT ALSO TOUCHES NOTHING BEFORE RETURNING - not the host, not the flag - which is
    /// what makes the guarded path observable as "did nothing at all".
    /// </para>
    /// <para>
    /// <c>currentRow &gt; 0</c> at <c>:L92</c> is a genuine case rather than defensive: an emptied
    /// DataWindow raises this event with <c>0</c> [<c>se_cst_dw.sru:L430-L431</c>], and the selection is
    /// then cleared with nothing re-selected.
    /// </para>
    /// </remarks>
    public long OnRowFocusChanged(long currentRow)
    {
        // n_cst_dwsvc_rowselect.sru:L88 - re-entrancy guard. Nothing else runs.
        if (_inChangeRowFocus)
        {
            return 0L;
        }

        DataWindowServiceHost host = RequireHost();

        // :L90
        host.SelectRow(0L, false);

        // :L92 - EQUALITY, NOT A BIT TEST (DECISION 1): Style == 3 re-selects the new current row.
        if (currentRow > 0L && Style != RS_MULTIPLE)
        {
            // :L93
            host.SelectRow(currentRow, true);
        }

        // :L96
        _bHasMultiSelected = false;

        // :L98
        return 0L;
    }

    /// <summary>
    /// Re-establishes the selection after the DataWindow has been filtered - the port of
    /// <c>event onfiltered()</c> (<c>n_cst_dwsvc_rowselect.sru:L11</c>, body <c>:L101-L111</c>).
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// This service has not been attached to a host yet.
    /// </exception>
    /// <remarks>
    /// <para>
    /// RETURNS NOTHING, AND IS NOT BROKER-WIRED. Two facts distinguish it from its three siblings, and
    /// both are the oracle's. <c>:L11</c> declares it as <c>event onfiltered ( )</c> - a CUSTOM event
    /// with NO <c>pbm</c> mapping and no <c>type</c> clause, so there is no code to return and no
    /// DataWindow message that raises it. And it is absent from the three subscriptions at
    /// <c>:L274-L276</c>, so no broker topic reaches it.
    /// </para>
    /// <para>
    /// ITS ONLY CALLER IS THE HOST'S <c>Filter()</c> OVERRIDE, WHICH INVOKES IT DIRECTLY:
    /// <c>se_cst_dw.sru:L403-L414</c> calls <c>super::Filter()</c>, and on success - which is
    /// <c>rtCode = 1</c>, NOT <c>0</c> [<c>:L407</c>] - checks <c>RowSelect.#Enabled</c> and raises
    /// <c>RowSelect.Event OnFiltered()</c> [<c>:L408-L409</c>]. It is therefore public so
    /// Domain/DataWindowEventChain.cs can invoke it the same way. The enabled check belongs to the
    /// caller, exactly as in the oracle, so this method does not repeat it.
    /// </para>
    /// <para>
    /// WHY IT IS NEEDED AT ALL: filtering moves rows out of the primary buffer, which can silently
    /// invalidate the selection and change which row is current without raising a row-change event. The
    /// body is the row-focus-changed body minus the guard, reading the current row from the host
    /// [<c>:L105</c>] instead of receiving it.
    /// </para>
    /// </remarks>
    public void OnFiltered()
    {
        DataWindowServiceHost host = RequireHost();

        // n_cst_dwsvc_rowselect.sru:L103
        host.SelectRow(0L, false);

        // :L105 - read from the host rather than received, because a filter reports no row.
        long nRow = host.GetRow();

        // :L106 - EQUALITY, NOT A BIT TEST (DECISION 1).
        if (nRow > 0L && Style != RS_MULTIPLE)
        {
            // :L107
            host.SelectRow(nRow, true);
        }

        // :L110 - no return statement in the legacy; the event has no return type.
        _bHasMultiSelected = false;
    }

    /// <summary>
    /// Handles a left-button double click - the port of <c>event onlbuttondblclk</c>
    /// (<c>n_cst_dwsvc_rowselect.sru:L12</c>, body <c>:L113</c>), mapped in the legacy to
    /// <c>pbm_dwnlbuttondblclk</c>.
    /// </summary>
    /// <param name="xpos">The pointer x position.</param>
    /// <param name="ypos">The pointer y position.</param>
    /// <param name="row">The one-based row under the pointer, or <c>0</c> when none.</param>
    /// <param name="dwo">The column under the pointer.</param>
    /// <param name="shiftHeld">Whether Shift was held.</param>
    /// <param name="ctrlHeld">Whether Control was held.</param>
    /// <returns>
    /// Whatever <see cref="OnLButtonClk(long, long, long, IDataWindowObject, bool, bool)"/> returns,
    /// verbatim.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// This service has not been attached to a host yet.
    /// </exception>
    /// <remarks>
    /// <para>
    /// A ONE-LINE FORWARD, KEPT AS A DISTINCT MEMBER RATHER THAN ALIASED AWAY. <c>:L113</c> is
    /// <c>return Event OnLButtonClk(xpos,ypos,row,dwo)</c> and nothing else - the second click of a
    /// double click is treated as an ordinary click, so a double click never escapes the selection
    /// model.
    /// </para>
    /// <para>
    /// IT MUST REMAIN A SEPARATE MEMBER BECAUSE IT IS A SEPARATE SUBSCRIPTION.
    /// <c>:L275</c> subscribes it to <see cref="EVT_DOUBLECLICKED"/> by NAME, independently of the
    /// click subscription at <c>:L274</c>. Collapsing the two would leave the double-click topic with
    /// no handler to resolve, and the broker answers <c>RetCode.E_EVENT_NOT_FOUND</c> for a name it
    /// cannot find rather than failing loudly at that point.
    /// </para>
    /// <para>
    /// The modifier parameters are forwarded rather than dropped, so a Shift+double-click behaves as a
    /// Shift+click - which is what the legacy does, since the forwarded event re-reads the same live
    /// keyboard state.
    /// </para>
    /// </remarks>
    public long OnLButtonDblClk(
        long xpos,
        long ypos,
        long row,
        IDataWindowObject dwo,
        bool shiftHeld,
        bool ctrlHeld)
    {
        // n_cst_dwsvc_rowselect.sru:L113
        return OnLButtonClk(xpos, ypos, row, dwo, shiftHeld, ctrlHeld);
    }

    // ==========================================================================================
    //  THE FOUR METHODS - :L34-L37 DECLARED, :L116-L263 IMPLEMENTED
    //  ----------------------------------------------------------------------------------------
    //      private  boolean _of_setcurrentrow (readonly long row)                :L34  :L116-L130
    //      public   boolean of_hasmultiselected ()                              :L35  :L132-L149
    //      public   long    of_setstyle (readonly long style)                    :L36  :L151-L182
    //      private  long    _of_checkselectedrows (readonly long, readonly dwobject)
    //                                                                           :L37  :L184-L263
    //  The two `_of_` prefixed ones are PRIVATE in the legacy and are private here; the two `of_`
    //  prefixed ones are public. The visibility split is the oracle's and is reproduced exactly.
    //  `in` parameters carry the legacy `readonly` annotation across per AAP 0.4.5.2.
    // ==========================================================================================

    /// <summary>
    /// Moves the host's current row, suppressing this service's own row-change reaction while it
    /// happens - the port of <c>_of_setcurrentrow(row)</c>
    /// (<c>n_cst_dwsvc_rowselect.sru:L34</c>, body <c>:L116-L130</c>).
    /// </summary>
    /// <param name="row">The one-based row to move to.</param>
    /// <returns>
    /// <see langword="true"/> when the current row is <paramref name="row"/> afterwards;
    /// <see langword="false"/> when <paramref name="row"/> was not positive or the move did not take
    /// effect.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// This service has not been attached to a host yet, on the non-guarded path.
    /// </exception>
    /// <remarks>
    /// <para>
    /// THE MOVE'S RETURN CODE IS DISCARDED AND THE POSITION IS RE-READ INSTEAD. <c>:L122</c> calls
    /// <c>SetRow(row)</c> and <c>:L123</c> then asserts <c>(#DataWindow.GetRow() = row)</c>. The oracle
    /// does not trust the call to have moved the cursor even on a successful return - a DataWindow can
    /// refuse a row move, for instance while an edit is pending validation - and the ported
    /// <see cref="DataWindowServiceHost.SetRow(long)"/> documents the same reading. Trusting the return
    /// code instead would report success for a refused move and let the click proceed against the wrong
    /// row.
    /// </para>
    /// <para>
    /// THE ALREADY-THERE CASE IS TRUE WITHOUT CALLING THE HOST AT ALL [<c>:L124-L126</c>]. That is
    /// observable, not merely an optimisation: an unnecessary <c>SetRow</c> to the current row would
    /// raise the host's row-change event, and this service subscribes to it.
    /// </para>
    /// <para>
    /// THE NON-POSITIVE GUARD AT <c>:L118</c> IS UNREACHABLE THROUGH THE PUBLIC SURFACE, AND IS
    /// PRESERVED ANYWAY. The only caller is arm 3 of
    /// <see cref="OnLButtonClk(long, long, long, IDataWindowObject, bool, bool)"/>, which cannot be
    /// reached with a non-positive row because <c>:L42</c> has already returned for one. The oracle
    /// carries the guard, so the port carries it: deleting dead code that the behavioural oracle has is
    /// a change to the thing being characterized, not a tidy-up (constraint C-B). It is the one line in
    /// this file that no test can drive, and it is called out here so its coverage gap reads as
    /// deliberate rather than as an oversight.
    /// </para>
    /// <para>
    /// *** DEFECT/QUIRK PRESERVED - THE FLAG RESET HAS NO <c>try</c>/<c>finally</c> ***
    /// <c>:L127</c> clears <c>_inChangeRowFocus</c> on the LINEAR PATH, after the branch. If the host
    /// raises - and <see cref="DataWindowServiceHost.SetRow(long)"/> and
    /// <see cref="DataWindowServiceHost.GetRow"/> both can, since an implementation is free to - the
    /// legacy LEAVES THE FLAG SET, and <see cref="OnRowFocusChanged(long)"/> then returns immediately
    /// for the remaining life of the service, silently disabling selection maintenance. A
    /// <c>finally</c> here would be a behaviour change dressed as robustness: it would make the
    /// service recover where the legacy does not, so constraint C-B forbids it. This annotation exists
    /// so a future reader does not read the absence as an oversight and "fix" it.
    /// </para>
    /// </remarks>
    private bool SetCurrentRow(in long row)
    {
        // n_cst_dwsvc_rowselect.sru:L116 - `boolean rt`, PowerScript-initialised to false.
        bool rt;

        // :L118 - tested BEFORE the flag is set, so a rejected row leaves the guard untouched.
        if (row <= 0L)
        {
            return false;
        }

        DataWindowServiceHost host = RequireHost();

        // :L120 - suppress this service's own reaction to the move it is about to make.
        _inChangeRowFocus = true;

        // :L121
        if (host.GetRow() != row)
        {
            // :L122 - the return code is DELIBERATELY DISCARDED; see the remarks.
            host.SetRow(row);

            // :L123 - the position is re-read, and that answer is the result.
            rt = host.GetRow() == row;
        }
        else
        {
            // :L125 - already there. The host is not called.
            rt = true;
        }

        // :L127 - DEFECT PRESERVED: no try/finally. On an exception the flag stays set. See remarks.
        _inChangeRowFocus = false;

        // :L129
        return rt;
    }

    /// <summary>
    /// Whether the current selection was produced by a multi-select gesture - the port of
    /// <c>of_hasmultiselected()</c> (<c>n_cst_dwsvc_rowselect.sru:L35</c>, body <c>:L132-L149</c>).
    /// </summary>
    /// <returns><see langword="true"/> when a Shift or Control gesture produced the selection.</returns>
    /// <remarks>
    /// <para>
    /// The legacy's own description at <c>:L135</c> is "判断当前是否有多行选择" - "determine whether
    /// there are currently multiple rows selected". The body is one line, <c>:L148</c>.
    /// </para>
    /// <para>
    /// KEPT AS A METHOD RATHER THAN CONVERTED TO A PROPERTY, matching the legacy function it ports. It
    /// is part of the service's published surface - <c>:L194</c> calls it through the public name even
    /// from inside the class, and an external caller reads it to decide whether an operation applies to
    /// one row or to many - so its shape is contract rather than style.
    /// </para>
    /// <para>
    /// IT DOES NOT COUNT ROWS, and the name is therefore slightly misleading in the oracle as well as
    /// here. See the remarks on <see cref="_bHasMultiSelected"/>: the flag is set unconditionally by
    /// every modifier gesture, including a Control-click that deselected the last remaining row. It
    /// needs no host and works before attachment, which is also what makes it reachable in a unit test
    /// with no test double at all (constraint C-H).
    /// </para>
    /// </remarks>
    public bool HasMultiSelected()
    {
        // n_cst_dwsvc_rowselect.sru:L148
        return _bHasMultiSelected;
    }

    /// <summary>
    /// Sets the row-selection style and reconciles the current selection with it - the port of
    /// <c>of_setstyle(style)</c> (<c>n_cst_dwsvc_rowselect.sru:L36</c>, body <c>:L151-L182</c>).
    /// </summary>
    /// <param name="style">
    /// The style to move to: <see cref="RS_SINGLE"/>, <see cref="RS_MULTIPLE"/>, or any other non-zero
    /// value - <c>RS_SINGLE + RS_MULTIPLE</c> being the combined value the oracle's own test window
    /// uses.
    /// </param>
    /// <returns>
    /// <c>RetCode.OK</c> when the style already matched or the change was applied, and
    /// <c>RetCode.E_INVALID_ARGUMENT</c> when <paramref name="style"/> was zero.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// This service has not been attached to a host yet AND it is currently enabled - the selection
    /// reconciliation needs the host. A style change while disabled touches no host and cannot raise.
    /// </exception>
    /// <remarks>
    /// <para>
    /// ANY NON-ZERO VALUE IS ACCEPTED. There is no membership check against the two named constants
    /// [<c>:L169</c> rejects only zero], which is what makes the combined value legal - and, with the
    /// equality comparisons of DECISION 1, what makes it a third behavioural state rather than a union.
    /// Adding a membership check would reject the value the oracle's own fixture sets.
    /// </para>
    /// <para>
    /// *** DEFECT/ORDERING PRESERVED - VALIDATION COMES AFTER THE IDEMPOTENT EARLY-OUT ***
    /// <c>:L168</c> is <c>if #Style = style then return RetCode.OK</c> and <c>:L169</c> is
    /// <c>if style = 0 then return RetCode.E_INVALID_ARGUMENT</c>, IN THAT ORDER. So
    /// <c>SetStyle(0)</c> would answer <c>OK</c> rather than <c>E_INVALID_ARGUMENT</c> if
    /// <see cref="Style"/> were already zero. IT NEVER IS, WHICH IS WHY THE ARM IS UNREACHABLE: the
    /// initial value is <see cref="RS_SINGLE"/> [<c>:L25</c>] and zero can never be stored, because
    /// the only writer is this method and it refuses zero before assigning. The order is nonetheless
    /// preserved exactly - swapping it would be an unobservable change today and a behavioural change
    /// the moment any other writer appeared, and constraint C-B does not distinguish between the two.
    /// </para>
    /// <para>
    /// THE SIDE EFFECTS ARE GATED ON <c>Enabled</c> [<c>:L173</c>], so a style change while the
    /// service is disabled records the value and touches NOTHING. That is why the enable path re-applies
    /// the selection itself [<c>:L277-L279</c>]: between the two, every route into the enabled state
    /// leaves the selection consistent with the style.
    /// </para>
    /// </remarks>
    public long SetStyle(in long style)
    {
        // n_cst_dwsvc_rowselect.sru:L168 - idempotent early-out, FIRST. See the defect note above.
        if (Style == style)
        {
            return RetCode.OK;
        }

        // :L169 - validation, SECOND, and therefore unreachable for a Style that is already zero.
        if (style == 0L)
        {
            return RetCode.E_INVALID_ARGUMENT;
        }

        // :L171
        Style = style;

        // :L173 - side effects only while enabled.
        if (Enabled)
        {
            DataWindowServiceHost host = RequireHost();

            // :L174
            host.SelectRow(0L, false);

            // :L175 - EQUALITY, NOT A BIT TEST (DECISION 1): moving to the combined style 3 RE-SELECTS
            // the current row, whereas moving to RS_MULTIPLE leaves the selection empty.
            if (Style != RS_MULTIPLE)
            {
                // :L176 - GetRow() may be 0 on an empty DataWindow, and row 0 means EVERY row - so
                // this selects everything rather than nothing in that case. That is the oracle's
                // behaviour: :L176 has no `> 0` guard, unlike :L92 and :L106 which do. Preserved.
                host.SelectRow(host.GetRow(), true);
            }

            // :L178
            _bHasMultiSelected = false;
        }

        // :L181
        return RetCode.OK;
    }

    /// <summary>
    /// Propagates a check-box toggle from the clicked row across every selected row - the port of
    /// <c>_of_checkselectedrows(row, dwo)</c> (<c>n_cst_dwsvc_rowselect.sru:L37</c>, body
    /// <c>:L184-L263</c>).
    /// </summary>
    /// <param name="row">The one-based row that was clicked.</param>
    /// <param name="dwo">The column that was clicked.</param>
    /// <returns>
    /// <c>1</c> when the propagation ran - INCLUDING WHEN IT WAS REFUSED PART-WAY - and <c>0</c> when
    /// any of the eight preconditions failed, so nothing was attempted.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// This service has not been attached to a host yet.
    /// </exception>
    /// <remarks>
    /// <para>
    /// WHAT IT IS FOR: clicking a check box in one of several selected rows sets that same value in all
    /// of them, which is the whole point of the multi-row selection service being on. The clicked
    /// row's CURRENT value decides the direction - clicking an "on" cell turns the whole selection off,
    /// and vice versa [<c>:L203-L222</c>].
    /// </para>
    /// <para>
    /// THE OUTER <c>do ... loop while false</c> [<c>:L190</c>, <c>:L260</c>] IS A GOTO SUBSTITUTE, NOT
    /// A LOOP. PowerScript has no early <c>return</c> out of the middle of a block, so the oracle wraps
    /// the preconditions in a single-pass block and uses <c>exit</c> to jump past its end to the
    /// <c>return 0</c> at <c>:L262</c>. It is ported as an ordinary method body with early returns.
    /// Emitting a one-iteration C# loop would reproduce the syntax and lose the meaning, and would read
    /// as an accident.
    /// </para>
    /// <para>
    /// EIGHT PRECONDITIONS, IN THE ORACLE'S ORDER, EACH ANSWERING <c>0</c>: the DataWindow is editable
    /// [<c>:L191</c>]; the column is editable [<c>:L192</c>]; THE CLICKED ROW IS ITSELF SELECTED
    /// [<c>:L193</c>]; a multi-select gesture produced the selection [<c>:L194</c>]; the column's edit
    /// style is a check box [<c>:L195</c>]; its "on" value is neither DataWindow sentinel
    /// [<c>:L196-L197</c>]; its "off" value likewise [<c>:L198-L199</c>]; and the clicked cell is not
    /// protected [<c>:L201</c>]. The order is preserved because it is also a cost order - the cheap
    /// state tests come before the Describe round trips - and because two of them are decisions a
    /// caller can observe through the return value.
    /// </para>
    /// <para>
    /// *** DEFECT PRESERVED - A REFUSED PROPAGATION STILL ANSWERS 1 *** When the host refuses a change
    /// [<c>:L238</c>], the inner loop stops [<c>:L240</c>] but control lands on the SAME
    /// <c>return 1</c> at <c>:L259</c> as a fully successful run. The call site at <c>:L46</c>
    /// therefore treats a refusal as "handled" and returns <c>1</c> to prevent the DataWindow's own
    /// click handling - so THE CLICKED CELL IS NOT TOGGLED EITHER, and a user who is refused on row 5
    /// sees rows 1 to 4 changed and nothing else. Distinguishing the two outcomes would change which
    /// value the click handler returns, so it is preserved; <see cref="PendingError"/> is how a caller
    /// learns the difference without the return value changing.
    /// </para>
    /// <para>
    /// PARTIAL APPLICATION IS NOT ROLLED BACK, which follows from the same lines. There is no
    /// transaction here and none is introduced: the rows already written stay written.
    /// </para>
    /// </remarks>
    private long CheckSelectedRows(in long row, IDataWindowObject dwo)
    {
        DataWindowServiceHost host = RequireHost();

        // n_cst_dwsvc_rowselect.sru:L186 - `boolean bRedraw`, PowerScript-initialised to false.
        bool bRedraw = false;

        // :L188 - the column NAME is taken once and threaded through every read and write below,
        // which is why the host's item members are name-keyed rather than id-keyed.
        string sColName = dwo.Name;

        // ---- :L190 `do` : the single-pass block. Eight preconditions, each an `exit` in the
        // ---- oracle and an early `return 0` here.

        // :L191
        if (!IsEditable())
        {
            return 0L;
        }

        // :L192
        if (!IsColumnEditable(sColName))
        {
            return 0L;
        }

        // :L193 - the clicked row must be part of the selection. Clicking OUTSIDE the selection is an
        // ordinary click and must fall through to arm 3 of the click handler, which collapses the
        // selection - so this guard is what keeps a plain click from propagating across a selection
        // the user has just left.
        if (!host.IsSelected(row))
        {
            return 0L;
        }

        // :L194 - called through the public name, exactly as the oracle does.
        if (!HasMultiSelected())
        {
            return 0L;
        }

        // :L195 - case-sensitive, as PowerScript `<>` on strings is.
        if (!string.Equals(
                host.Describe(sColName + ".edit.style"),
                CheckBoxEditStyle,
                StringComparison.Ordinal))
        {
            return 0L;
        }

        // :L196
        string sOn = host.Describe(sColName + ".checkbox.on");

        // :L197 - both DataWindow sentinels abandon the propagation, because neither is a usable value.
        if (IsDescribeSentinel(sOn))
        {
            return 0L;
        }

        // :L198
        string sOff = host.Describe(sColName + ".checkbox.off");

        // :L199
        if (IsDescribeSentinel(sOff))
        {
            return 0L;
        }

        // :L200 legacy comment "忽略被保护的单元格" - ignore protected cells.
        // :L201 - the ITEM, not the column: a protect expression can answer differently per row.
        if (IsItemProtected(row, sColName))
        {
            return 0L;
        }

        // :L202
        long nColType = GetColumnType(sColName);

        // :L203-L222 - the target value. THREE ARMS ONLY, and the direction is decided by the clicked
        // row's current value: equal to the "on" value picks "off", anything else picks "on". Note the
        // asymmetry - a cell that is neither on nor off, including a NULL cell, is turned ON. That
        // follows from the `if ... = sOn` shape and is preserved.
        string sVal = string.Equals(ReadItemAsText(host, row, sColName, nColType), sOn, StringComparison.Ordinal)
            ? sOff
            : sOn;

        // ---- :L223 `do` : the INNER loop, which is a real loop. Its cursor is both the argument and
        // ---- the result of GetSelectedRow, so it starts at the PowerScript initial value 0.
        long nRow = 0L;

        while (true)
        {
            // :L224 - searches strictly AFTER nRow, which is what terminates the walk.
            nRow = host.GetSelectedRow(nRow);

            // :L225 - no further selected row.
            if (nRow <= 0L)
            {
                break;
            }

            // :L226 legacy comment "忽略不可见的单元格" - ignore invisible cells.
            // :L227
            if (!IsItemVisible(nRow, sColName))
            {
                continue;
            }

            // :L228 legacy comment "忽略被保护的单元格" - ignore protected cells.
            // :L229
            if (IsItemProtected(nRow, sColName))
            {
                continue;
            }

            // :L230-L237 - skip a row that already holds the target value. The same three-arm read as
            // :L203-L222, against sVal rather than sOn. This is what keeps the change event from firing
            // for rows that are not changing.
            if (string.Equals(ReadItemAsText(host, nRow, sColName, nColType), sVal, StringComparison.Ordinal))
            {
                continue;
            }

            // :L238 - ANY non-zero answer is a refusal. Not the four-value item-change alphabet and not
            // the return-code algebra: the oracle's test is `<> 0`.
            if (host.OnDoItemChange(nRow, dwo, sVal) != 0L)
            {
                // :L239 - the file's ONLY presentational statement, converted to a structured error
                // (DECISION 3). Not thrown, and not merely logged.
                PendingError = BuildRejectionError(nRow);

                // :L240 - stop propagating. Control reaches the `return 1` below all the same, which is
                // the preserved defect described in the remarks.
                break;
            }

            // :L242-L245 - repaint suppressed LAZILY, on the first row actually written and never
            // again. A propagation that changed nothing therefore never touches redraw at all, and the
            // matching restore at :L256-L258 is gated on the same flag.
            if (!bRedraw)
            {
                bRedraw = true;
                host.SetRedraw(false);
            }

            // :L246-L253 - the write. Three arms, matching the read arms, but note the WIDTHS DIFFER
            // FROM THE READS on the integer arm: the read is GetItemNumber, a double, while the write
            // is Long(sVal). That mismatch is the legacy's and is reproduced.
            switch (nColType)
            {
                case COL_TYPE_DECIMAL:
                    // :L248 - `Dec(sVal)`. PowerScript's Dec yields null for text that is not a
                    // number, and null is passed through rather than coerced to zero (AAP 0.4.5.4).
                    host.SetItem(nRow, sColName, ParseDecimal(sVal));
                    break;

                case COL_TYPE_INTEGER:
                    // :L250 - `Long(sVal)`, with the same null-on-unparseable behaviour.
                    host.SetItem(nRow, sColName, ParseLong(sVal));
                    break;

                default:
                    // :L252 - the text as it stands.
                    host.SetItem(nRow, sColName, sVal);
                    break;
            }

            // :L254 - the after-change notification, raised per row and carrying no return value.
            host.OnDoItemChanged(nRow, dwo);
        }

        // :L256-L258 - restore repainting only if it was suppressed.
        if (bRedraw)
        {
            host.SetRedraw(true);
        }

        // :L259 - DEFECT PRESERVED: reached by BOTH the exhausted-loop path and the refusal path. See
        // the remarks.
        return 1L;

        // :L260 `loop while false` / :L262 `return 0` - the eight early returns above are that exit.
    }

    /// <summary>
    /// Whether a <c>Describe</c> answer is one of the two DataWindow sentinels - the port of the pair of
    /// tests at <c>n_cst_dwsvc_rowselect.sru:L197</c> and <c>:L199</c>.
    /// </summary>
    /// <param name="describeAnswer">The answer to test.</param>
    /// <returns>
    /// <see langword="true"/> when the answer is <c>"!"</c> or <c>"?"</c>.
    /// </returns>
    /// <remarks>
    /// The oracle spells the same two-value test twice, once per property [<c>:L197</c>, <c>:L199</c>].
    /// Naming it once keeps the two sites from drifting and gives the pair a single place to be tested.
    /// The comparison is ordinal, matching PowerScript string equality.
    /// </remarks>
    private static bool IsDescribeSentinel(string describeAnswer)
    {
        // n_cst_dwsvc_rowselect.sru:L197, :L199
        return string.Equals(describeAnswer, InvalidExpressionSentinel, StringComparison.Ordinal)
            || string.Equals(describeAnswer, UndeterminedValueSentinel, StringComparison.Ordinal);
    }

    /// <summary>
    /// Reads one item as the text the check-box comparison uses - the port of the three-arm
    /// <c>choose case nColType</c> read that <c>n_cst_dwsvc_rowselect.sru</c> spells twice, at
    /// <c>:L203-L222</c> and again at <c>:L230-L237</c>.
    /// </summary>
    /// <param name="host">The attached host.</param>
    /// <param name="row">The one-based row to read.</param>
    /// <param name="colName">The column name.</param>
    /// <param name="colType">The column's <c>COL_TYPE_*</c> classification.</param>
    /// <returns>
    /// The item's text, or <see langword="null"/> when the item is null.
    /// </returns>
    /// <remarks>
    /// <para>
    /// THREE ARMS AND NO MORE, matching the oracle exactly: <c>COL_TYPE_DECIMAL</c> reads a decimal,
    /// <c>COL_TYPE_INTEGER</c> reads a number, and EVERYTHING ELSE reads text. Date, datetime and time
    /// columns fall through the default arm and are read as strings - which is the oracle's behaviour
    /// and not an omission, since a check box over a date column is not something the legacy
    /// contemplates.
    /// </para>
    /// <para>
    /// NULL IS RETURNED AS NULL AND NEVER SUBSTITUTED. PowerScript's <c>String(null)</c> yields a null
    /// string, and comparing a null with <c>=</c> yields null, which <c>if</c> treats as FALSE - so at
    /// <c>:L205</c>/<c>:L211</c>/<c>:L217</c> a null cell takes the else branch and is turned ON, and
    /// at <c>:L232</c>/<c>:L234</c>/<c>:L236</c> a null cell is NOT skipped. Returning the empty string
    /// instead would make a null cell equal to an empty "off" value and silently skip it. AAP 0.4.5.4
    /// forbids collapsing null in either direction, and this is the site where it would matter.
    /// </para>
    /// <para>
    /// FORMATTED WITH THE INVARIANT CULTURE, which PowerScript's <c>String()</c> has no parameter for
    /// and does implicitly. It is observable: a culture using a comma as the decimal separator would
    /// render <c>1.5</c> as <c>"1,5"</c> and stop matching a check-box value of <c>"1.5"</c>.
    /// </para>
    /// </remarks>
    private static string? ReadItemAsText(
        DataWindowServiceHost host,
        long row,
        string colName,
        long colType)
    {
        switch (colType)
        {
            case COL_TYPE_DECIMAL:
                // n_cst_dwsvc_rowselect.sru:L205, :L232 - `String(#DataWindow.GetItemDecimal(...))`.
                return host.GetItemDecimal(row, colName)?.ToString(CultureInfo.InvariantCulture);

            case COL_TYPE_INTEGER:
                // :L211, :L234 - `String(#DataWindow.GetItemNumber(...))`. A double, despite the arm's
                // name; see DataWindowServiceHost.GetItemNumber for why the width is preserved.
                return host.GetItemNumber(row, colName)?.ToString(CultureInfo.InvariantCulture);

            default:
                // :L217, :L236 - `#DataWindow.GetItemString(...)`, with no String() wrapper because it
                // is already text.
                return host.GetItemString(row, colName);
        }
    }

    /// <summary>
    /// The port of PowerScript's <c>Dec(string)</c> as used at
    /// <c>n_cst_dwsvc_rowselect.sru:L248</c>.
    /// </summary>
    /// <param name="text">The text to convert.</param>
    /// <returns>
    /// The value, or <see langword="null"/> when <paramref name="text"/> is not a number.
    /// </returns>
    /// <remarks>
    /// NULL RATHER THAN ZERO FOR UNPARSEABLE TEXT, which is what PowerScript's <c>Dec</c> yields. The
    /// distinction is observable at the write: a null writes a null item, while zero would write a real
    /// value. The parse is invariant-culture for the same reason the read is formatted that way.
    /// </remarks>
    private static decimal? ParseDecimal(string text)
    {
        return decimal.TryParse(
            text,
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out decimal value)
            ? value
            : null;
    }

    /// <summary>
    /// The port of PowerScript's <c>Long(string)</c> as used at
    /// <c>n_cst_dwsvc_rowselect.sru:L250</c>.
    /// </summary>
    /// <param name="text">The text to convert.</param>
    /// <returns>
    /// The value, or <see langword="null"/> when <paramref name="text"/> is not an integer.
    /// </returns>
    /// <remarks>
    /// NULL RATHER THAN ZERO FOR UNPARSEABLE TEXT, matching PowerScript's <c>Long</c>. The accepted
    /// number style is deliberately narrower than <see cref="ParseDecimal(string)"/>'s: an integer
    /// conversion of <c>"1.5"</c> fails and yields null rather than truncating, because truncation
    /// would invent a value the oracle does not produce.
    /// </remarks>
    private static long? ParseLong(string text)
    {
        return long.TryParse(
            text,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out long value)
            ? value
            : null;
    }

    /// <summary>
    /// Composes the structured replacement for the dialog at
    /// <c>n_cst_dwsvc_rowselect.sru:L239</c>.
    /// </summary>
    /// <param name="rejectedRow">
    /// The one-based row the host refused - <c>nRow</c>, the row the inner loop had reached, NOT the row
    /// that was clicked.
    /// </param>
    /// <returns>The error, ready to be surfaced by the caller.</returns>
    /// <remarks>
    /// <para>
    /// THE LEGACY STATEMENT'S TWO ARGUMENTS, BYTE FOR BYTE - the message
    /// <c>Sprintf(I18N(ne_cst_i18n.CAT_DWSVC,"第{}行"),nRow) + "~n" +
    /// I18N(ne_cst_i18n.CAT_DWSVC,"修改数据被拒绝") + "!"</c>
    /// and the severity <c>StopSign!</c>, handed to the modal dialog this method replaces.
    /// </para>
    /// <para>
    /// TWO SEPARATE LOOKUPS, NOT ONE LOOKUP OF A JOINED STRING, and the composition order is the
    /// oracle's: translate the row fragment, format the TRANSLATION with the row number, join with
    /// <see cref="LineSeparator"/>, append the translated rejection fragment, then append the untranslated
    /// <see cref="RejectionMessageSuffix"/>. Translating a pre-joined string would need a key that does
    /// not exist in any table, and formatting before translating would substitute into the key rather
    /// than into its translation.
    /// </para>
    /// <para>
    /// <c>Sprintf</c> IS CALLED WITH THE FORMAT STRING UNCHANGED, in the SEQUENTIAL EMPTY-BRACE form:
    /// <c>{}</c> takes the next argument. It is applied to the LOOKUP RESULT and not to the key, so a
    /// provider that reorders or re-words the fragment still gets its placeholder filled.
    /// </para>
    /// <para>
    /// THE LOOKUP CANNOT FAIL AND CANNOT THROW. With no provider installed, <c>I18n</c> returns the text
    /// unchanged - the silent-passthrough fallback of <c>i18n.srf</c> - so the composed message is the
    /// legacy's Chinese source text, which is exactly what the oracle produces in the same situation.
    /// </para>
    /// </remarks>
    private RowSelectRejectionError BuildRejectionError(long rejectedRow)
    {
        // :L239, first operand - translate, THEN format the translation with the row number.
        string? rowFragmentTemplate = _i18n.I18N(Categories.CAT_DWSVC, RowNumberMessageKey);
        string rowFragment = Formatting.Sprintf(rowFragmentTemplate, rejectedRow);

        // :L239, third operand - translated, carries no placeholder.
        string? rejectionFragment = _i18n.I18N(Categories.CAT_DWSVC, RejectionMessageKey);

        // :L239 - the concatenation, in the oracle's operand order. String concatenation with a null
        // operand yields the empty string for that operand, which is the passthrough of a provider that
        // answered null; the legacy would concatenate a null the same way.
        string text = rowFragment + LineSeparator + rejectionFragment + RejectionMessageSuffix;

        return new RowSelectRejectionError
        {
            Text = text,
            LocalizationCategory = Categories.CAT_DWSVC,
            RowFormatTemplate = RowNumberMessageKey,
            RejectionMessageKey = RejectionMessageKey,
            Row = rejectedRow,

            // :L239 - the legacy `StopSign!`.
            Icon = RowSelectMessageIcon.StopSign,

            // These two messages DO route through I18n, unlike the column-expression engine's 28.
            Localized = true,
        };
    }

    /// <summary>
    /// Wires or unwires the service and reconciles the selection - the port of
    /// <c>event onenable</c> (<c>n_cst_dwsvc_rowselect.sru:L273-L287</c>).
    /// </summary>
    /// <param name="enabled">The state being moved to.</param>
    /// <returns>
    /// Always <c>0</c> [<c>:L286</c>], which ALLOWS the change - this service never vetoes its own
    /// enablement.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// This service has not been attached to a host yet.
    /// </exception>
    /// <remarks>
    /// <para>
    /// THE BASE IS CALLED FIRST AND ITS RESULT IS DISCARDED, exactly as <c>:L273</c>'s
    /// <c>call super::onenable</c> does - a PowerScript <c>call super::</c> statement has no result to
    /// consume. The vetoable sequencing around this hook belongs to
    /// <see cref="DataWindowServiceBase.SetEnabled(in bool)"/> and is NOT re-implemented here; note in
    /// particular that a veto there maps to <c>RetCode.FAILED</c> (-1) and NOT to
    /// <c>RetCode.PREVENT</c> (1), which is why returning <c>0</c> from here is the only correct answer
    /// for a service with nothing to object to.
    /// </para>
    /// <para>
    /// ONLY THREE OF THE FOUR EVENTS ARE BROKER-WIRED [<c>:L274-L276</c>]:
    /// <see cref="EVT_CLICKED"/> to <see cref="OnLButtonClk(long, long, long, IDataWindowObject, bool, bool)"/>,
    /// <see cref="EVT_DOUBLECLICKED"/> to
    /// <see cref="OnLButtonDblClk(long, long, long, IDataWindowObject, bool, bool)"/>, and
    /// <see cref="EVT_ROWFOCUSCHANGED"/> to <see cref="OnRowFocusChanged(long)"/>.
    /// <see cref="OnFiltered"/> IS NOT SUBSCRIBED - the host's <c>Filter()</c> override invokes it
    /// directly [<c>se_cst_dw.sru:L403-L414</c>] - so a search for a fourth subscription here will
    /// correctly find none.
    /// </para>
    /// <para>
    /// SUBSCRIPTION GOES THROUGH THE HOST'S BROKER, WHICH IS WHAT THE ORACLE DOES TOO. <c>:L274</c>
    /// calls <c>#DataWindow.of_On(...)</c>, and <c>se_cst_dw.sru:L448</c> shows that member to be a
    /// one-line forward to <c>Eventful.of_On(...)</c>; likewise <c>of_Off(obj)</c> at <c>:L463</c>.
    /// Reaching the broker directly removes the forwarder without changing behaviour. Handler names are
    /// passed with <c>nameof</c> so a rename cannot leave a subscription pointing at a name that no
    /// longer resolves - the broker answers <c>RetCode.E_EVENT_NOT_FOUND</c> for an unresolvable name
    /// rather than failing at that point, which is precisely the kind of silent breakage
    /// <c>nameof</c> prevents.
    /// </para>
    /// <para>
    /// THE DISABLE PATH UNSUBSCRIBES BY TARGET, NOT BY TOPIC. <c>:L281</c> is
    /// <c>#DataWindow.of_Off(this)</c> - the single-argument object overload - so ALL of this service's
    /// subscriptions go at once, including any a future subscription here would add. Unsubscribing the
    /// three topics individually would be equivalent today and would silently leak a fourth later.
    /// </para>
    /// <para>
    /// THE ENABLE PATH RE-APPLIES THE SELECTION [<c>:L277-L279</c>] because
    /// <see cref="SetStyle(in long)"/> deliberately does nothing while disabled. Between the two, every
    /// route into the enabled state leaves the selection consistent with <see cref="Style"/>. The
    /// EQUALITY test at <c>:L277</c> is DECISION 1 again: the combined style 3 re-selects, RS_MULTIPLE
    /// does not.
    /// </para>
    /// </remarks>
    protected override long OnEnable(bool enabled)
    {
        // n_cst_dwsvc_rowselect.sru:L273 - `call super::onenable`, result discarded as PowerScript
        // discards it. Called BEFORE the body, which is the statement order at :L273.
        _ = base.OnEnable(enabled);

        DataWindowServiceHost host = RequireHost();

        if (enabled)
        {
            // :L274 - `#DataWindow.of_On(#DataWindow.EVT_CLICKED,this,"onLButtonClk")`
            host.Eventful.Subscribe(EVT_CLICKED, this, nameof(OnLButtonClk));

            // :L275
            host.Eventful.Subscribe(EVT_DOUBLECLICKED, this, nameof(OnLButtonDblClk));

            // :L276
            host.Eventful.Subscribe(EVT_ROWFOCUSCHANGED, this, nameof(OnRowFocusChanged));

            // :L277 - EQUALITY, NOT A BIT TEST (DECISION 1).
            if (Style != RS_MULTIPLE)
            {
                // :L278 - no `> 0` guard on GetRow(), so an empty DataWindow selects EVERY row here,
                // exactly as at :L176. Preserved.
                host.SelectRow(host.GetRow(), true);
            }
        }
        else
        {
            // :L281 - by TARGET, so every subscription this service holds is removed at once.
            host.Eventful.Unsubscribe(this);

            // :L282
            host.SelectRow(0L, false);

            // :L283
            _bHasMultiSelected = false;
        }

        // :L286
        return 0L;
    }
}
