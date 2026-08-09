// ==============================================================================================
//  DataWindowServiceHost - the abstract host contract DataServices implements against, so that
//  reproducing the DataWindow service layer requires no DesignSystem type at all.
//  --------------------------------------------------------------------------------------------
//  WHY THIS FILE EXISTS
//  `se_cst_dw` DERIVES FROM `se_cst_datawindow`
//  [ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L4 and :L10], and `se_cst_datawindow`
//  lives in `pfw.ui.controls.ext`, which is a DEFERRED DesignSystem library. That is a STRUCTURAL
//  INHERITANCE EDGE and not a call, so no amount of refactoring at a call site removes it. AAP
//  0.2.1.3 Correction 3 resolves it: DataServices declares its OWN abstract host contract carrying
//  only the members `se_cst_dw` actually consumes, implements against that contract, and records
//  `se_cst_datawindow` as REFERENCE-only. This file IS that resolution.
//
//  PORTED FROM
//      ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru     (616 lines) - the DataWindow
//          member surface its event chain consumes, and the eleven semantic events it raises.
//      ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc.sru   (864 lines) - the HOST-FACING
//          half of the common service base: the three attachment properties [:L36-L38], the
//          OnInit [:L9, :L85-L87] and OnEnable [:L10] hooks, `of_setenabled` [:L89-L95], the
//          STYLE_* [:L18-L24] and COL_TYPE_* [:L26-L32] catalogues, and the two `_of_getdwobject`
//          accessors [:L97, :L100-L125].
//
//  READ AS REFERENCE, DELIBERATELY NOT PORTED
//      se_cst_datawindow.sru       The structural parent. NOT ONE LINE of it is ported; see the
//                                  ruling immediately below.
//      retcode.sru:L38-L46         The return-code identifiers, already ported to
//                                  shared/PowerFramework.Shared.Kernel/RetCode.cs. This file
//                                  CONSUMES RetCode.OK and RetCode.FAILED and declares neither.
//      isvalidobject.srf:L11-L17   `if IsNull(object) then return false / return IsValid(object)`,
//                                  already ported as Predicates.IsValidObject. This file consumes
//                                  it at the one site the legacy consumes it
//                                  [n_cst_dwsvc.sru:L119].
//
//  ORACLE STATUS  Every `ws_objects/**` path above is READ ONLY (constraint C-C). Those files are
//                 the behavioural oracle for parity testing and are never an edit target. Outside
//                 them there is no other statement of intended behaviour to consult (AAP 0.1.4),
//                 so every behaviour reproduced below carries the line locator it came from.
//
//  ============================================================================================
//  RULING: THE PARENT IS 100% THEMING AND CONTRIBUTES ZERO CONSUMED MEMBERS
//  ============================================================================================
//  `ws_objects/pfw.ui.controls.ext.pbl.src/se_cst_datawindow.sru` is 91 lines and its ENTIRE
//  content is theming, verified line by line:
//
//      :L9-L11    three theme events - onthemeregistering (returns long), onthemeregistered and
//                 onthememgrnotify
//      :L15-L54   onthememgrnotify, a `ThemeManager().#Style` switch over WIN8 / WIN7 / XP / QQ
//                 setting background, border and scrollbar styles
//      :L64-L72   an ongetcolor override over `theme.CLR_TRANSPARENT` / `theme.CLR_BKGND`
//      :L74-L80   onthemechanged calling `Win32.RedrawWindow(#Handle, ...)`
//      :L82-L87   onpreconstructor performing `ThemeManager().of_RegisterControl(this)`, gated on
//                 `IsPrevented(Event OnThemeRegistering())`
//      :L89-L90   onpredestructor performing `of_UnregisterControl(this)`
//
//  It also touches #LockUpdate, theme, HSplitScroll, of_UpdatePoints(), of_Redraw(), #Transparent,
//  #Handle and Visible - every one a DesignSystem member - and it derives in turn from a FURTHER
//  DesignSystem ancestor, `s_cst_datawindow` [:L4, :L8].
//
//  CONSEQUENCE, WHICH IS THE POINT OF THE WHOLE FILE: this contract carries NO theming member
//  whatsoever. No theme event, no colour hook, no style switch, no redraw-by-handle, no DPI
//  conversion, no font measurement and no menu type. Constraint C-D forbids implementing a
//  deferred service even partially and even to stub it out, so there is also no
//  NotImplementedException-throwing placeholder standing in for a DesignSystem type. AAP 0.8.1
//  records the governing judgement: where the choice is between a partial implementation and a
//  documented gap, THE DOCUMENTED GAP WINS.
//
//  ZERO THEMING COSTS NOTHING HERE, AND THAT WAS MEASURED RATHER THAN HOPED. A search across both
//  ported sources for `Win32.`, `PX2MM`, `MM2PX`, `D2PX`, `U2P`, `n_cst_font`, `n_cst_popupmenu`,
//  `ThemeManager` and `theme.` returns 0 hits in se_cst_dw.sru and 0 hits in n_cst_dwsvc.sru. Both
//  sources are already free of every presentational primitive. A search for `native ` and
//  `external function` likewise returns 0 and 0, which independently confirms AAP 0.6.5: the whole
//  DataWindow service layer is pure PowerScript with zero PBNI bindings, so nothing in this file
//  needs a native substitution decision.
//
//  THE ONE THING THE DROPPED PARENT DID TAKE WITH IT, RECORDED AS A GAP AND NOT AS AN OMISSION.
//  `se_cst_dw`'s `call super::onpreconstructor` [se_cst_dw.sru:L570] and `call super::ondestructor`
//  [:L583] would, in the legacy, run the theme registration and unregistration above. In the .NET
//  port those super-calls have NO ANALOGUE AND ARE DROPPED. The drop is annotated here and again
//  where the lifecycle is reproduced (Domain/DataWindowEventChain.cs, which ports `se_cst_dw` and
//  derives from DataWindowServiceHost). It is a DOCUMENTED CAPABILITY GAP BELONGING TO THE
//  DEFERRED `/v1/design/**` GATEWAY EXTENSION POINT: AAP 0.4.4 reserves that route precisely so
//  this gap is legible from the gateway's contract rather than invisible. Nothing observable to a
//  headless service is lost, because control registration with a theme manager has no effect that
//  a gRPC or REST response can carry.
//
//  ============================================================================================
//  WHAT THIS FILE OWNS, AND WHAT IT DELIBERATELY DOES NOT
//  ============================================================================================
//  OWNS
//      * IDataWindowValueBuffer - the indexed `dwo.Primary[row]` view.
//      * IDataWindowObject      - the `dwobject` handle, exactly four members.
//      * IDataWindowChild       - the `datawindowchild` handle yielded by GetChild.
//      * DataWindowServiceHost  - the abstract stand-in for `se_cst_datawindow` and the ancestry
//                                 above it: the measured DataWindow member surface plus the
//                                 eleven semantic events `se_cst_dw` raises.
//      * DataWindowServiceBase  - the host-facing half of `n_cst_dwsvc`: attachment, enablement
//                                 and the two preserved constant catalogues.
//
//  DOES NOT OWN, AND MUST NOT ACQUIRE
//      * The EID_* event-gate bitmask                      -> Domain/EventGate.cs
//      * The {0,1,2,3} item-change alphabet                -> Domain/ItemChangeProtocol.cs
//      * The four cross-event mutable state fields         -> Domain/ValidationSession.cs
//      * The 22-event raw/semantic delegation chain, the
//        twelve EVT_* broker topics and the five attached
//        services declared at se_cst_dw.sru:L80-L84        -> Domain/DataWindowEventChain.cs
//      * Anything under Expressions/, Services/, Grpc/ or Endpoints/ - THEY CONSUME THIS FILE.
//        The dependency direction is one-way and this file must never reverse it.
//      * Any storage provider, connection, SQL text or EF Core type. Constraint C-E: Persistence
//        is the only service in the system that holds a storage provider, and the DataServices
//        project declares no EF Core, no Microsoft.Data.Sqlite and no SQLitePCLRaw package.
//      * Any key, password, token or credential literal (constraint C-F). Every configurable
//        value in this service arrives through Configuration/DataServicesOptions.cs.
//
//  This contract stays INSIDE PowerFramework.DataServices and is never promoted to shared/
//  (constraint C-A). The only permitted cross-service coupling is PowerFramework.Contracts, which
//  is a boundary definition and not a back door for shared behaviour.
//
//  ============================================================================================
//  THE MEMBER SURFACE IS MEASURED, NOT GUESSED
//  ============================================================================================
//  AAP 0.4.2.5 states the criterion exactly: carry "only the members actually consumed". Every
//  member below was counted by searching the two ported sources, and the counts are recorded so a
//  future reader can re-derive the set rather than trust it. Nothing was added because it "should"
//  be on a DataWindow, and nothing consumed was left off.
//
//      MEMBER                      USES  WHERE
//      Describe                    31+6  n_cst_dwsvc.sru (31) + se_cst_dw.sru (6). Dominates by an
//                                        order of magnitude: every property read in the service
//                                        layer funnels through it.
//      SetItem                        8  se_cst_dw.sru :L219 :L233 :L235 :L237 :L239 :L241 :L243
//                                        :L375 - seven overloads, one per value type the coercion
//                                        table at :L228-L243 produces plus the `any` restore path.
//      RowCount                       5  :L369 :L421 :L429 :L434 :L441
//      GetRow                         5  :L154 :L156 :L425 :L428 :L436
//      GetItemStatus                  2  :L190 :L335
//      SetItemStatus                  2  :L220 :L376
//      ObjectModel                    2  n_cst_dwsvc.sru :L119 :L122 (`#DataWindow.Object`)
//      GetValue                       2  n_cst_dwsvc.sru :L651 :L658
//      SetRow                         1  :L155 - FALLIBLE, see DECISION 4
//      AcceptText                     1  :L554 - -1 means failure
//      SetRedraw                      1  :L442
//      SetFocus                       1  :L555
//      GetFocusedObject               1  :L553 - `GetFocus() <> this`, see DECISION 3
//      Filter                         1  :L406 as `super::Filter()`   - success is 1
//      DeleteRow                      1  :L431 as `super::DeleteRow()` - success is 1
//      GetChild                       1  n_cst_dwsvc.sru :L592
//
//      dwobject accessors, measured on se_cst_dw.sru - EXACTLY FOUR, and no fifth:
//      ID                            12       Name                           6
//      Primary                        7       ColType                        2
//
//      datawindowchild accessors, measured on n_cst_dwsvc.sru - see DECISION 2:
//      Describe                       2       RowCount                       1
//      GetItemString                  2       GetItemDecimal                 2
//      GetItemNumber                  2       GetItemDateTime                2
//      GetItemDate                    2       GetItemTime                    2
//
//  ============================================================================================
//  DECISIONS (constraint C-K: every technology-specific and boundary-specific decision recorded)
//  ============================================================================================
//  DECISION 1 - DwBuffer and ItemStatus are CONSUMED FROM THE PUBLISHED CONTRACT, not re-declared.
//      `Primary!` appears at se_cst_dw.sru :L190, :L220 and :L376, and the `dwitemstatus` domain at
//      :L190, :L220, :L325, :L335 and :L376. Both already exist as published contract types in
//      shared/PowerFramework.Contracts/Proto/common.v1.proto, which this project already
//      references, so this file declares NO parallel enum. That is strictly stronger than the
//      value-for-value agreement test the folder brief anticipated: because the generated types are
//      consumed directly, agreement is enforced BY THE COMPILER and cannot drift at all.
//      `Delete!` and `Filter!` appear in NEITHER ported source - they belong to Persistence's
//      Buffers/ - so no member here consumes them, and DwBuffer is nonetheless carried whole
//      because it is one published enum.
//
//      WHY THE TWO TYPE ALIASES BELOW ARE MANDATORY AND NOT COSMETIC. A plain
//      `using PowerFramework.Contracts.Common.V1;` does not compile in this file. That namespace
//      also contains a GENERATED WRAPPER MESSAGE CLASS named `RetCode` - common.v1.proto nests each
//      legacy constant set as an enum named Value inside a thin wrapper message, so
//      `common.v1.RetCode` is a real type - and this file must reference
//      PowerFramework.Shared.Kernel.RetCode for RetCode.OK and RetCode.FAILED. Importing both
//      namespaces makes every mention of `RetCode` ambiguous (CS0104), and with warnings promoted
//      to errors that is a build failure. Aliasing the two types actually needed keeps the wrapper
//      message out of scope entirely and states the dependency precisely.
//
//  DECISION 2 - The six typed getters live on IDataWindowChild, because that is where they are
//      consumed. This is a MEASURED CORRECTION to the enumerated brief, which listed them without
//      saying which object carries them. All twelve call sites are on `dwc`, the
//      `datawindowchild` obtained from `#DataWindow.GetChild` - n_cst_dwsvc.sru :L605 :L607 :L609
//      :L611 :L613 :L615 :L621 :L623 :L625 :L627 :L629 :L631, all inside `_of_getcolumnvaluemap`.
//      There is not one `#DataWindow.GetItem*` call anywhere in either source. Putting them on the
//      host would have invented a surface the legacy does not have, which AAP 0.4.2.5's
//      consumption criterion forbids. `dwc` also carries its own Describe [:L596, :L597] and
//      RowCount [:L600], so IDataWindowChild carries those two as well.
//
//      ALL SIX RETURN NULLABLE TYPES, and that is contract rather than caution: the legacy tests
//      `if IsNull(sDispVal) then continue` [:L617] and `if IsNull(aVal) then continue` [:L633], so
//      a null item is an ordinary outcome the caller discriminates on. AAP 0.4.5.4 is explicit that
//      null must never be collapsed to zero, because doing so silently converts a skipped row into
//      a mapped one.
//
//  DECISION 3 - `GetFocus` is a genuine name collision and the FUNCTION is the one renamed.
//      Two unrelated things share the spelling: the SEMANTIC EVENT `Event GetFocus()` returning
//      long [se_cst_dw.sru:L400], and the call `GetFocus() <> this` [:L553]. C# forbids two members
//      that differ only in return type, so exactly one had to move. The event keeps the name and
//      the function becomes GetFocusedObject(), because :L553's `GetFocus()` is the PowerScript
//      SYSTEM function that returns the control currently holding focus - it was never a member of
//      the DataWindow at all, so its .NET name was always a modelling choice - whereas the eleven
//      event names ARE ancestry member names that Domain/DataWindowEventChain.cs reproduces
//      one-for-one. Renaming the one that was never a member name is the minimal resolution.
//
//  DECISION 4 - SetRow is modelled as fallible, and its return code is not trusted on its own.
//      se_cst_dw.sru:L154-L157 calls `SetRow(row)` and then RE-READS `GetRow()`, returning 1 when
//      the row still differs. The legacy therefore treats a successful return as insufficient
//      evidence that the cursor moved. The contract preserves that: SetRow returns the legacy
//      integer code AND callers are documented to re-read GetRow, exactly as the oracle does.
//
//  DECISION 5 - Filter and DeleteRow are virtual over protected abstract *Core operations.
//      `se_cst_dw` OVERRIDES both and calls the base implementation - `super::Filter()` [:L406] and
//      `super::DeleteRow(nRow)` [:L431]. A plain `public abstract int Filter()` here would make
//      that impossible, because C# cannot reach an abstract member through `base.`. The template
//      method pattern keeps both halves: FilterCore / DeleteRowCore are the built-in DataWindow
//      behaviour an adapter or test double supplies, and Filter / DeleteRow are the overridable
//      seam Domain/DataWindowEventChain.cs overrides and reaches with `base.Filter()`.
//      BOTH RETURN AN INTEGER WHERE SUCCESS IS 1, NOT 0 [:L407, :L432]. They are deliberately NOT
//      mapped onto RetCode, whose OK is 0 - conflating the two inverts every success test.
//
//  DECISION 6 - `Eventful` is DECLARED here and SUPPLIED by the derived chain.
//      The broker instance belongs to `se_cst_dw`, not to the parent: `eventful` is declared within
//      `se_cst_dw` [se_cst_dw.sru:L6-L7, :L33] and created in its constructor [:L562]. But the
//      service base reads it off the host - `#Eventful = dw.Eventful` [n_cst_dwsvc.sru:L86] - so
//      the host CONTRACT must expose it. It is therefore abstract here and implemented by
//      Domain/DataWindowEventChain.cs. That keeps the dependency direction one-way: the host
//      declares, the chain supplies, and this file never references the chain.
//
//  DECISION 7 - The self-shadowing globals become injected dependencies, never statics.
//      n_cst_dwsvc.sru:L12 declares `global n_cst_dwsvc n_cst_dwsvc` and se_cst_dw.sru:L35 declares
//      `global se_cst_dw se_cst_dw` - global auto-instances that shadow their own type names, an
//      artefact of PowerBuilder's single flat namespace (AAP 0.4.5.1). AAP 0.5.4.2 fixes the
//      resolution: the type keeps the descriptive .NET name and the instance becomes an injected
//      dependency. Neither shadow is reproduced. Nothing in this file is static and mutable, and
//      the host arrives through OnInit rather than through ambient global state - which is also
//      what makes the whole contract substitutable by a test double (constraint C-H).
//
//  DECISION 8 - No performance claim is made anywhere in this file. AAP 0.8.5: the repository
//      publishes no SLA, no latency budget, no throughput target and no availability commitment, so
//      no member here is described as fast or optimised and no shape is justified on performance
//      grounds. Every shape above is justified by a locator instead.
// ==============================================================================================

using System.Globalization;

using PowerFramework.Shared.Eventful;
using PowerFramework.Shared.Kernel;

using DwBuffer = PowerFramework.Contracts.Common.V1.DwBuffer;
using ItemStatus = PowerFramework.Contracts.Common.V1.ItemStatus;

namespace PowerFramework.DataServices.Domain;

/// <summary>
/// The indexed view a <see cref="IDataWindowObject"/> exposes over one DataWindow buffer - the port
/// of the legacy <c>dwo.Primary[row]</c> spelling
/// (<c>ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L189</c>, <c>:L198</c>, <c>:L200</c>,
/// <c>:L206</c>, <c>:L334</c>, <c>:L372</c>).
/// </summary>
/// <remarks>
/// <para>
/// This interface exists so the ported call sites read exactly as the oracle reads. A single method
/// named <c>GetPrimaryValue(row)</c> would have been simpler to declare and harder to review against
/// a 616-line PowerScript source, and review against that source is the only way parity can be
/// established for this service (AAP 0.1.4: the legacy tree is the only specification).
/// </para>
/// <para>
/// Seven uses were measured on <c>se_cst_dw.sru</c>, and every one of them is a READ. Writes travel
/// through <see cref="DataWindowServiceHost.SetItem(long, long, object?)"/> and its typed siblings
/// instead, exactly as the legacy does at <c>:L219</c> and <c>:L375</c>, so the indexer is
/// deliberately get-only rather than a full accessor pair.
/// </para>
/// </remarks>
public interface IDataWindowValueBuffer
{
    /// <summary>
    /// The value held for <paramref name="row"/> in this buffer, or <see langword="null"/> when the
    /// item is null.
    /// </summary>
    /// <param name="row">
    /// The one-based DataWindow row number. One-based indexing is the legacy's own convention and is
    /// preserved here rather than shifted: AAP 0.4.5.4 names one-based to zero-based translation the
    /// single most dangerous mechanical hazard in this refactor, so row numbers cross this contract
    /// in the numbering the oracle uses and are never silently rebased.
    /// </param>
    /// <value>
    /// The item value, typed as <see cref="object"/> because the legacy member is an <c>any</c>
    /// (AAP 0.4.5.2 maps <c>any</c> to <c>object?</c>).
    /// </value>
    /// <remarks>
    /// NULL SUPPORT IS CONTRACT, NOT TOLERANCE. The equality test at <c>se_cst_dw.sru:L198-L202</c>
    /// carries an explicit <c>IsNull(aOrgValue) and IsNull(dwo.Primary[row])</c> arm whose whole
    /// purpose is to classify null-versus-null as equal. An implementation that substituted a
    /// default for a null item would make that arm unreachable and would convert "unchanged" into
    /// "changed" for every null column, which AAP 0.4.5.4 forbids in terms.
    /// </remarks>
    object? this[long row] { get; }
}

/// <summary>
/// The port of the PowerBuilder <c>dwobject</c> handle - the column or report object a DataWindow
/// event names as the target of the interaction.
/// </summary>
/// <remarks>
/// <para>
/// EXACTLY FOUR MEMBERS, AND NO FIFTH. Measured on
/// <c>ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru</c>: <c>dwo.ID</c> 12 uses,
/// <c>dwo.Primary</c> 7, <c>dwo.Name</c> 6, <c>dwo.ColType</c> 2. Nothing else is touched, and
/// <c>n_cst_dwsvc.sru</c> touches <c>dwo</c> not at all. AAP 0.4.2.5's criterion is consumption, so
/// the real <c>dwobject</c>'s far larger property set is deliberately absent: adding a member here
/// that no ported call site reads would be inventing surface, and every added member is one more
/// thing a test double must fabricate for no behavioural gain (constraint C-H).
/// </para>
/// <para>
/// This interface carries no geometry, no colour, no font and no handle, which is what keeps
/// constraint C-D satisfied at the level of the handle as well as at the level of the host.
/// </para>
/// </remarks>
public interface IDataWindowObject
{
    /// <summary>
    /// The DataWindow object identifier, in the untyped form the legacy exposes it in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// TYPED AS <c>object?</c> ON PURPOSE, AND THE CONVERSION IS MODELLED RATHER THAN ASSUMED. This
    /// member is NOT already numeric in the legacy: every one of the twelve call sites wraps it,
    /// as <c>Long(dwo.ID)</c> - <c>se_cst_dw.sru:L190</c>, <c>:L219</c>, <c>:L220</c>, <c>:L233</c>,
    /// <c>:L235</c>, <c>:L237</c>, <c>:L239</c>, <c>:L241</c>, <c>:L243</c>, <c>:L335</c>,
    /// <c>:L375</c>, <c>:L376</c>. Declaring it as a number here would quietly relocate a
    /// conversion the oracle performs explicitly, and would hide the fact that the conversion has
    /// its own null and unparseable behaviour. Use
    /// <see cref="DataWindowObjectExtensions.ColumnId(IDataWindowObject)"/> to perform it.
    /// </para>
    /// <para>
    /// AAP 0.4.5.2 maps the PowerScript <c>any</c> onto <c>object?</c>, which is what this is.
    /// </para>
    /// </remarks>
    object? ID { get; }

    /// <summary>
    /// The object's name, as used to build a property expression for
    /// <see cref="DataWindowServiceHost.Describe(string)"/>.
    /// </summary>
    /// <remarks>
    /// Six uses measured. The load-bearing one is <c>se_cst_dw.sru:L350</c>,
    /// <c>Describe(dwo.Name+".ValidationMsg")</c>, where the name is concatenated straight into a
    /// property expression; <c>:L278</c> assigns it to a local, and the remaining four are inside
    /// the commented-out drop-down edit checks at <c>:L217-L218</c> and <c>:L373-L374</c>, which are
    /// carried across as commented and inert rather than revived (constraint C-B).
    /// Non-nullable, because <c>:L350</c> concatenates it with no null guard.
    /// </remarks>
    string Name { get; }

    /// <summary>
    /// The column's database type, as the legacy raw string, for example <c>"char(50)"</c>,
    /// <c>"decimal(2)"</c>, <c>"long"</c>, <c>"datetime"</c>, <c>"date"</c> or <c>"time"</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// KEPT AS A STRING, DELIBERATELY NOT PRE-PARSED INTO AN ENUM. The coercion table at
    /// <c>se_cst_dw.sru:L231</c> switches on <c>Left(dwo.ColType,5)</c> - the FIRST FIVE CHARACTERS -
    /// across the six arms <c>"char"</c>/<c>"char("</c>, <c>"decim"</c>/<c>"real"</c>/<c>"numbe"</c>,
    /// <c>"long"</c>/<c>"ulong"</c>, <c>"datet"</c>, <c>"date"</c> and <c>"time"</c>. Parsing the
    /// value here would destroy that prefix match's input, and Domain/ItemChangeProtocol.cs would
    /// lose the six-arm dispatch it exists to reproduce. The commented-out length check at
    /// <c>:L282-L284</c> additionally parses the declared width back out of the same string, which
    /// is a second reason the raw text is the contract.
    /// </para>
    /// <para>
    /// Non-nullable, because <c>:L231</c> applies <c>Left</c> with no null guard. An implementation
    /// with no column type to report returns the empty string, which truncates to the empty string
    /// and falls through every arm to the default - the same outcome the legacy reaches for any
    /// unrecognised type.
    /// </para>
    /// </remarks>
    string ColType { get; }

    /// <summary>
    /// The primary buffer view, so that <c>dwo.Primary[row]</c> ports as
    /// <c>dwo.Primary[row]</c>.
    /// </summary>
    /// <remarks>
    /// Seven uses measured, at <c>se_cst_dw.sru:L189</c>, <c>:L198</c>, <c>:L200</c>, <c>:L206</c>,
    /// <c>:L334</c> and <c>:L372</c> (twice on that line). See
    /// <see cref="IDataWindowValueBuffer"/> for why the indexed shape is preserved.
    /// </remarks>
    IDataWindowValueBuffer Primary { get; }
}

/// <summary>
/// The explicit port of the legacy <c>Long(dwo.ID)</c> conversion that every one of the twelve
/// <see cref="IDataWindowObject.ID"/> call sites performs.
/// </summary>
/// <remarks>
/// <para>
/// This exists because <see cref="IDataWindowObject.ID"/> is an <c>any</c> and the oracle converts
/// it at the point of use rather than at the point of declaration. Giving the conversion a name and
/// a single implementation means the twelve ported sites cannot drift from one another, and means
/// the conversion's own null and unparseable behaviour is stated once and testable once instead of
/// being re-decided twelve times.
/// </para>
/// <para>
/// It is an extension class rather than an interface member so that
/// <see cref="IDataWindowObject"/> keeps to the measured four members exactly, and so a test double
/// inherits the conversion instead of reimplementing it.
/// </para>
/// </remarks>
public static class DataWindowObjectExtensions
{
    /// <summary>
    /// Converts <paramref name="dwo"/>'s identifier to the numeric column id the host member
    /// surface takes - the port of <c>Long(dwo.ID)</c>.
    /// </summary>
    /// <param name="dwo">The DataWindow object whose identifier is converted.</param>
    /// <returns>
    /// The numeric column id, or <see langword="null"/> when the identifier is null - see
    /// <see cref="ToColumnId(object?)"/> for the full conversion table.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="dwo"/> is <see langword="null"/>. The legacy would raise a null object
    /// reference error at the same point; a named exception says so precisely rather than surfacing
    /// as a <see cref="NullReferenceException"/> from inside the property read.
    /// </exception>
    public static long? ColumnId(this IDataWindowObject dwo)
    {
        ArgumentNullException.ThrowIfNull(dwo);

        return ToColumnId(dwo.ID);
    }

    /// <summary>
    /// The conversion itself, reproducing PowerScript's <c>Long</c> applied to an <c>any</c>.
    /// </summary>
    /// <param name="id">The raw identifier value, as read from <see cref="IDataWindowObject.ID"/>.</param>
    /// <returns>
    /// <see langword="null"/> when <paramref name="id"/> is <see langword="null"/>; the integral
    /// value when it holds one; the parsed value when it holds text that parses; and <c>0</c> when
    /// it holds text that does not.
    /// </returns>
    /// <remarks>
    /// <para>
    /// THE THREE ARMS ARE EACH A PRESERVED LEGACY BEHAVIOUR, NOT A DESIGN CHOICE.
    /// </para>
    /// <para>
    /// NULL PROPAGATES. PowerScript's <c>Long</c> of a null yields null, and AAP 0.4.5.4 forbids
    /// collapsing null to zero because doing so converts an absent identifier into column zero -
    /// which is a real column position, so the corruption would be silent. The nullable return is
    /// what makes the absence visible to the caller.
    /// </para>
    /// <para>
    /// UNPARSEABLE TEXT YIELDS ZERO, matching PowerScript's documented <c>Long(string)</c>
    /// behaviour. This is preserved rather than improved to an exception: constraint C-B forbids
    /// behaviour the legacy does not have, and the legacy has no throw here.
    /// </para>
    /// <para>
    /// Parsing is invariant-culture, because a DataWindow object id is structural data and not a
    /// localized number. That is a substitution the legacy cannot express - PowerScript's
    /// <c>Long</c> has no culture parameter - and it is unobservable for the digit strings this
    /// member actually carries.
    /// </para>
    /// </remarks>
    public static long? ToColumnId(object? id)
    {
        switch (id)
        {
            case null:
                // Null propagates. Never coerced to 0, which is a valid column position.
                return null;

            // The integral cases, ordered widest-first so no value is narrowed on the way through.
            case long value:
                return value;
            case int value:
                return value;
            case short value:
                return value;
            case byte value:
                return value;
            case uint value:
                return value;
            case ushort value:
                return value;
            case sbyte value:
                return value;

            // An unsigned 64-bit id cannot exist on a DataWindow, but the legacy `unsignedlong`
            // type can reach here through an `any`. Clamp rather than overflow: PowerScript's
            // conversions do not throw, and constraint C-B keeps it that way.
            case ulong value:
                return value <= long.MaxValue ? (long)value : long.MaxValue;

            case decimal value:
                return ToColumnIdFromDecimal(value);
            case double value:
                return ToColumnIdFromDouble(value);
            case float value:
                return ToColumnIdFromDouble(value);

            case string text:
                // PowerScript `Long(string)` yields 0 for text that is not a valid number.
                return long.TryParse(
                    text,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out long parsed)
                    ? parsed
                    : 0L;

            default:
                // Anything else is not a number and not text, so it converts the way unparseable
                // text converts.
                return 0L;
        }
    }

    /// <summary>
    /// Truncates a <see cref="decimal"/> toward zero into the column-id range without throwing.
    /// </summary>
    private static long ToColumnIdFromDecimal(decimal value)
    {
        if (value >= long.MaxValue)
        {
            return long.MaxValue;
        }

        if (value <= long.MinValue)
        {
            return long.MinValue;
        }

        return (long)decimal.Truncate(value);
    }

    /// <summary>
    /// Truncates a <see cref="double"/> toward zero into the column-id range without throwing.
    /// </summary>
    /// <remarks>
    /// NaN converts to <c>0</c> rather than throwing, which is the same outcome unparseable text
    /// reaches. PowerScript's numeric conversions do not raise, so neither does this
    /// (constraint C-B).
    /// </remarks>
    private static long ToColumnIdFromDouble(double value)
    {
        if (double.IsNaN(value))
        {
            return 0L;
        }

        if (value >= long.MaxValue)
        {
            return long.MaxValue;
        }

        if (value <= long.MinValue)
        {
            return long.MinValue;
        }

        return (long)Math.Truncate(value);
    }
}


/// <summary>
/// The port of the PowerBuilder <c>datawindowchild</c> handle - the drop-down DataWindow behind a
/// DDDW column, as obtained from
/// <see cref="DataWindowServiceHost.GetChild(string, ref IDataWindowChild?)"/>.
/// </summary>
/// <remarks>
/// <para>
/// WHY THIS IS A SEPARATE CONTRACT RATHER THAN MORE MEMBERS ON THE HOST. Every typed-getter call
/// site in both ported sources is on the child and not on the host. Measured on
/// <c>ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc.sru</c>, inside
/// <c>_of_getcolumnvaluemap</c>: <c>dwc.GetItemString</c> <c>:L605</c> <c>:L621</c>,
/// <c>dwc.GetItemDecimal</c> <c>:L607</c> <c>:L623</c>, <c>dwc.GetItemNumber</c> <c>:L609</c>
/// <c>:L625</c>, <c>dwc.GetItemDateTime</c> <c>:L611</c> <c>:L627</c>, <c>dwc.GetItemDate</c>
/// <c>:L613</c> <c>:L629</c>, <c>dwc.GetItemTime</c> <c>:L615</c> <c>:L631</c>, plus
/// <c>dwc.Describe</c> <c>:L596</c> <c>:L597</c> and <c>dwc.RowCount</c> <c>:L600</c>. There is not
/// one <c>#DataWindow.GetItem*</c> call anywhere in either source, so declaring the typed getters on
/// the host would have invented a surface the oracle does not have.
/// </para>
/// <para>
/// COLUMNS ARE ADDRESSED BY NAME HERE, NOT BY NUMBER, because that is how the oracle addresses them:
/// the display and data column names come out of <c>Describe</c> calls at <c>:L594</c> and
/// <c>:L595</c> and are passed straight through. The host's item accessors take a numeric column id
/// instead, and the asymmetry is the legacy's, preserved rather than harmonised.
/// </para>
/// </remarks>
public interface IDataWindowChild
{
    /// <summary>
    /// Reads a property of the child DataWindow - the port of <c>dwc.Describe(...)</c>.
    /// </summary>
    /// <param name="property">
    /// The property expression, for example <c>"lastname.coltype"</c>.
    /// </param>
    /// <returns>
    /// The property value. The legacy sentinels are part of the contract and are returned unaltered:
    /// <c>"!"</c> for an invalid expression and <c>"?"</c> for a value that cannot be determined,
    /// both of which the two call sites test for explicitly at <c>:L598</c> and <c>:L599</c>
    /// alongside the empty string.
    /// </returns>
    string Describe(string property);

    /// <summary>
    /// The number of rows in the child's primary buffer - the port of <c>dwc.RowCount()</c>
    /// (<c>n_cst_dwsvc.sru:L600</c>).
    /// </summary>
    /// <returns>
    /// The row count. The legacy loop at <c>:L601</c> iterates <c>1</c> to this value inclusive, so
    /// the value is the LAST VALID ROW NUMBER and not a zero-based length.
    /// </returns>
    long RowCount();

    /// <summary>
    /// Reads a <c>char</c>-typed item as text - the port of <c>dwc.GetItemString(row, column)</c>.
    /// </summary>
    /// <param name="row">The one-based row number.</param>
    /// <param name="column">The column name.</param>
    /// <returns>The item value, or <see langword="null"/> when the item is null.</returns>
    /// <remarks>
    /// Nullable by contract: <c>n_cst_dwsvc.sru:L617</c> and <c>:L633</c> both skip the row on a null
    /// result, so null is an ordinary outcome the caller discriminates on rather than an error.
    /// </remarks>
    string? GetItemString(long row, string column);

    /// <summary>
    /// Reads a <c>decimal</c>-typed item - the port of <c>dwc.GetItemDecimal(row, column)</c>.
    /// </summary>
    /// <param name="row">The one-based row number.</param>
    /// <param name="column">The column name.</param>
    /// <returns>The item value, or <see langword="null"/> when the item is null.</returns>
    /// <remarks>
    /// AAP 0.4.5.2 maps <c>dec</c> and <c>decimal(n)</c> onto <see cref="decimal"/>, which preserves
    /// the base-ten scale a DataWindow decimal column carries. Nullable for the reason given on
    /// <see cref="GetItemString(long, string)"/>.
    /// </remarks>
    decimal? GetItemDecimal(long row, string column);

    /// <summary>
    /// Reads a numeric item - the port of <c>dwc.GetItemNumber(row, column)</c>.
    /// </summary>
    /// <param name="row">The one-based row number.</param>
    /// <param name="column">The column name.</param>
    /// <returns>The item value, or <see langword="null"/> when the item is null.</returns>
    /// <remarks>
    /// <see cref="double"/> and NOT <see cref="decimal"/>, deliberately: PowerBuilder's
    /// <c>GetItemNumber</c> returns a <c>double</c>, and the oracle reaches it through the
    /// <c>COL_TYPE_INTEGER</c> arm at <c>:L609</c> and <c>:L625</c> while sending decimal columns
    /// down the separate <c>GetItemDecimal</c> path. Collapsing the two onto one .NET type would
    /// merge two distinct legacy conversions and change the text
    /// <see cref="object.ToString"/> produces for the value map's keys.
    /// </remarks>
    double? GetItemNumber(long row, string column);

    /// <summary>
    /// Reads a <c>datetime</c>-typed item - the port of <c>dwc.GetItemDateTime(row, column)</c>.
    /// </summary>
    /// <param name="row">The one-based row number.</param>
    /// <param name="column">The column name.</param>
    /// <returns>The item value, or <see langword="null"/> when the item is null.</returns>
    /// <remarks>AAP 0.4.5.2 maps <c>datetime</c> onto <see cref="DateTime"/>.</remarks>
    DateTime? GetItemDateTime(long row, string column);

    /// <summary>
    /// Reads a <c>date</c>-typed item - the port of <c>dwc.GetItemDate(row, column)</c>.
    /// </summary>
    /// <param name="row">The one-based row number.</param>
    /// <param name="column">The column name.</param>
    /// <returns>The item value, or <see langword="null"/> when the item is null.</returns>
    /// <remarks>
    /// AAP 0.4.5.2 maps <c>date</c> onto <see cref="DateOnly"/>, which keeps a date-only column from
    /// silently acquiring a time-of-day component the legacy type cannot hold.
    /// </remarks>
    DateOnly? GetItemDate(long row, string column);

    /// <summary>
    /// Reads a <c>time</c>-typed item - the port of <c>dwc.GetItemTime(row, column)</c>.
    /// </summary>
    /// <param name="row">The one-based row number.</param>
    /// <param name="column">The column name.</param>
    /// <returns>The item value, or <see langword="null"/> when the item is null.</returns>
    /// <remarks>AAP 0.4.5.2 maps <c>time</c> onto <see cref="TimeOnly"/>.</remarks>
    TimeOnly? GetItemTime(long row, string column);
}

/// <summary>
/// The abstract DataWindow host every DataServices component is written against: the stand-in for
/// <c>se_cst_datawindow</c> and the ancestry above it, carrying only the members
/// <c>se_cst_dw</c> actually consumes.
/// </summary>
/// <remarks>
/// <para>
/// AAP 0.2.1.3 Correction 3 in one sentence: <c>se_cst_dw</c> inherits from a DEFERRED DesignSystem
/// type, that edge is structural rather than a call, and the resolution is for DataServices to
/// declare this contract and implement against it. <c>se_cst_datawindow.sru</c> is REFERENCE-only
/// and not one line of it is ported - it is 100% theming and contributes zero consumed members, as
/// the file header establishes line by line.
/// </para>
/// <para>
/// WHAT DERIVES FROM THIS. Domain/DataWindowEventChain.cs ports <c>se_cst_dw</c> and derives from
/// this type, overriding <see cref="Filter"/> and <see cref="DeleteRow(long)"/> and supplying
/// <see cref="Eventful"/>. A test double also derives from it, which is the point: every sibling
/// under Domain/ must be drivable with no live DataWindow anywhere in the process
/// (constraint C-H). If any member below cannot be satisfied by a hand-written double, the
/// abstraction is wrong and must be reshaped rather than worked around.
/// </para>
/// <para>
/// WHY AN ABSTRACT CLASS RATHER THAN AN INTERFACE. Two of the members are OVERRIDDEN by
/// <c>se_cst_dw</c>, which then calls the base implementation - <c>super::Filter()</c>
/// [<c>se_cst_dw.sru:L406</c>] and <c>super::DeleteRow(nRow)</c> [<c>:L431</c>]. Reaching a base
/// implementation through <c>base.</c> requires a class, and the eleven semantic events additionally
/// need default bodies so that an unhandled event behaves as the PowerBuilder runtime behaves.
/// </para>
/// <para>
/// THE DROPPED SUPER-CALLS. <c>se_cst_dw</c>'s <c>call super::onpreconstructor</c>
/// [<c>se_cst_dw.sru:L570</c>] and <c>call super::ondestructor</c> [<c>:L583</c>] reached the parent's
/// theme registration and unregistration. This type declares no counterpart, so those calls have no
/// analogue and are dropped. That is a DOCUMENTED CAPABILITY GAP owned by the deferred
/// <c>/v1/design/**</c> Gateway extension point (AAP 0.4.4), recorded here and again at the site
/// that reproduces the lifecycle - never a silent omission, and never a stub (constraint C-D).
/// </para>
/// </remarks>
public abstract class DataWindowServiceHost
{
    /// <summary>
    /// The event broker this host owns, which every attached service lifts off it during
    /// attachment.
    /// </summary>
    /// <remarks>
    /// <para>
    /// DECLARED HERE, SUPPLIED BY THE DERIVED CHAIN, AND THAT SPLIT IS DELIBERATE. The broker
    /// instance belongs to <c>se_cst_dw</c> rather than to its parent: <c>eventful</c> is declared
    /// within <c>se_cst_dw</c> [<c>se_cst_dw.sru:L6-L7</c>, <c>:L33</c>] and created in its
    /// constructor [<c>:L562</c>]. But the service base reads it off the host -
    /// <c>#Eventful = dw.Eventful</c> [<c>n_cst_dwsvc.sru:L86</c>] - so the host CONTRACT has to
    /// expose it. Declaring it abstract here and implementing it in
    /// Domain/DataWindowEventChain.cs satisfies both facts and keeps the dependency one-way: this
    /// file never references the chain.
    /// </para>
    /// <para>
    /// Non-nullable, because the legacy creates the broker in the host's constructor and therefore
    /// has no window in which a live host lacks one.
    /// </para>
    /// </remarks>
    public abstract EventBroker Eventful { get; }

    /// <summary>
    /// The object model the DataWindow exposes its named objects through - the port of
    /// <c>#DataWindow.Object</c> (<c>n_cst_dwsvc.sru:L119</c>, <c>:L122</c>).
    /// </summary>
    /// <value>
    /// The object model, or <see langword="null"/> when the DataWindow has none - for instance
    /// before a data object has been assigned.
    /// </value>
    /// <remarks>
    /// Exposed as a validity-bearing handle rather than as a boolean so that
    /// <see cref="DataWindowServiceBase.GetDataWindowObject(string)"/> can reproduce
    /// <c>if Not IsValidObject(#DataWindow.Object)</c> [<c>:L119</c>] through the ported predicate
    /// <c>Predicates.IsValidObject</c> itself, rather than through a paraphrase of it. The predicate
    /// is <c>isvalidobject.srf:L11-L17</c>, <c>if IsNull(object) then return false / return
    /// IsValid(object)</c>.
    /// </remarks>
    public abstract object? ObjectModel { get; }

    /// <summary>
    /// Resolves a named object on the DataWindow's object model to a handle - the port of
    /// <c>#DataWindow.Object.__Get_Attribute(dwoName, false)</c> (<c>n_cst_dwsvc.sru:L122</c>).
    /// THROWS WHEN THE NAMED OBJECT DOES NOT EXIST.
    /// </summary>
    /// <param name="dwoName">
    /// The object name to resolve. The positional form <c>"#n"</c> is also valid and is what
    /// <see cref="DataWindowServiceBase.GetDataWindowObject(in long)"/> composes.
    /// </param>
    /// <returns>The handle for the named object.</returns>
    /// <remarks>
    /// <para>
    /// THE THROWING CONTRACT IS PRESERVED DELIBERATELY, AND IT IS THE POINT OF THIS MEMBER. The
    /// legacy carries its own warning immediately above the call, at <c>:L121</c>: an exception occurs
    /// when the object does not exist. An implementation must therefore raise for an unknown name and
    /// must NOT return a null or otherwise inert handle, because the caller
    /// <see cref="DataWindowServiceBase.GetDataWindowObject(in string)"/> already uses null to mean
    /// something else entirely - that the DataWindow has no valid object model at all
    /// [<c>:L119</c>]. Collapsing the two would erase a distinction the oracle makes.
    /// </para>
    /// <para>
    /// The return is non-nullable for exactly that reason: there is no success path that yields
    /// nothing. Either the handle is produced or the call raises.
    /// </para>
    /// <para>
    /// The legacy's second argument is the PowerBuilder-generated attribute accessor's own flag and
    /// carries no behaviour the ported call site varies - it is <c>false</c> at the only call site -
    /// so it is not surfaced as a parameter. Adding a parameter no caller can meaningfully vary would
    /// widen the contract past what AAP 0.4.2.5's consumption criterion admits.
    /// </para>
    /// </remarks>
    public abstract IDataWindowObject GetObjectAttribute(string dwoName);


    /// <summary>
    /// Reads a DataWindow property - the port of <c>Describe(...)</c>, and by a wide margin the most
    /// heavily used member on this contract.
    /// </summary>
    /// <param name="property">
    /// The property expression, for example <c>"DataWindow.Processing"</c>
    /// [<c>se_cst_dw.sru:L153</c>] or a per-object expression such as
    /// <c>dwo.Name + ".ValidationMsg"</c> [<c>:L350</c>].
    /// </param>
    /// <returns>
    /// The property value as text.
    /// </returns>
    /// <remarks>
    /// <para>
    /// 37 measured call sites - 31 as <c>#DataWindow.Describe</c> in <c>n_cst_dwsvc.sru</c> and 6 in
    /// <c>se_cst_dw.sru</c>. Every property read in the whole service layer funnels through here.
    /// </para>
    /// <para>
    /// THE SENTINEL RETURNS ARE CONTRACT AND MUST NOT BE NORMALISED. <c>"!"</c> means the expression
    /// is invalid and <c>"?"</c> means the value cannot be determined; the empty string is a third,
    /// distinct outcome. All three are tested for explicitly by ported callers -
    /// <c>n_cst_dwsvc.sru:L587</c>, <c>:L590</c>, <c>:L598</c>, <c>:L599</c> - so an implementation
    /// that mapped any of them onto null, onto each other, or onto an exception would break
    /// behaviour that the oracle branches on. The return is therefore non-nullable: the legacy has no
    /// null outcome here, it has three distinguished strings.
    /// </para>
    /// <para>
    /// One consumer additionally depends on the RAW TEXT rather than just its value:
    /// <c>se_cst_dw.sru:L350-L353</c> reads a validation message and then STRIPS THE OUTER TWO
    /// CHARACTERS with <c>Mid(sErrMsg,2,Len(sErrMsg) - 2)</c>. That stripping belongs to
    /// Domain/ValidationSession.cs, so this member must return the quoted text exactly as the
    /// DataWindow reports it and must not helpfully unquote it.
    /// </para>
    /// </remarks>
    public abstract string Describe(string property);

    /// <summary>
    /// The number of rows in the primary buffer - the port of <c>RowCount()</c>.
    /// </summary>
    /// <returns>
    /// The row count, which is also the LAST VALID ROW NUMBER because DataWindow rows are one-based.
    /// </returns>
    /// <remarks>
    /// Five measured uses, and two of them are bounds tests that depend on the one-based reading:
    /// <c>se_cst_dw.sru:L369</c> guards with <c>row &lt;= RowCount()</c> and <c>:L421</c> rejects
    /// with <c>r &gt; RowCount()</c>. AAP 0.4.5.4 names one-based to zero-based translation the
    /// single most dangerous mechanical hazard in this refactor, so the value crosses this contract
    /// in the legacy's numbering and is never rebased.
    /// </remarks>
    public abstract long RowCount();

    /// <summary>
    /// The current row - the port of <c>GetRow()</c>.
    /// </summary>
    /// <returns>The one-based current row number, or <c>0</c> when there is no current row.</returns>
    /// <remarks>
    /// Five measured uses. <c>se_cst_dw.sru:L424-L426</c> relies on the zero outcome specifically:
    /// a delete request for row <c>0</c> is redirected to the current row.
    /// </remarks>
    public abstract long GetRow();

    /// <summary>
    /// Moves the current row - the port of <c>SetRow(row)</c>. THE MOVE MAY NOT HAPPEN.
    /// </summary>
    /// <param name="row">The one-based row number to move to.</param>
    /// <returns>
    /// The legacy integer code, where <c>1</c> indicates success and <c>-1</c> indicates failure.
    /// </returns>
    /// <remarks>
    /// <para>
    /// DELIBERATELY NOT MODELLED AS INFALLIBLE, AND THE RETURN CODE ALONE IS NOT SUFFICIENT
    /// EVIDENCE. <c>se_cst_dw.sru:L154-L157</c> calls <c>SetRow(row)</c> and then RE-READS
    /// <c>GetRow()</c>, returning <c>1</c> - prevent - when the row still differs. The oracle
    /// therefore does not trust the call to have moved the cursor even on a successful return, and
    /// ported callers must re-read <see cref="GetRow"/> the same way rather than treating this
    /// value as proof.
    /// </para>
    /// <para>
    /// Returns the raw legacy integer and is NOT mapped onto <c>RetCode</c>: success here is
    /// <c>1</c>, whereas <c>RetCode.OK</c> is <c>0</c> and <c>RetCode.PREVENT</c> is <c>1</c>.
    /// Mapping them would invert the test.
    /// </para>
    /// </remarks>
    public abstract int SetRow(long row);

    /// <summary>
    /// Applies the text currently in the edit control to the buffer - the port of
    /// <c>AcceptText()</c>.
    /// </summary>
    /// <returns>
    /// The legacy integer code, where <c>-1</c> INDICATES FAILURE.
    /// </returns>
    /// <remarks>
    /// <para>
    /// One measured use, and it tests for the failure value rather than for success:
    /// <c>se_cst_dw.sru:L554</c> is <c>if AcceptText() = -1 then SetFocus()</c>, inside the deferred
    /// accept the kill-focus handler queues. Preserving <c>-1</c> as the discriminator is what keeps
    /// that branch reachable.
    /// </para>
    /// <para>
    /// Note the adjacent legacy comment at <c>:L230</c>, which explains why the item-change coercion
    /// table does NOT call this member: a control such as a check box has no edit text to accept.
    /// That is why <see cref="SetItem(long, long, string?)"/> and its siblings exist alongside it
    /// rather than being replaced by it.
    /// </para>
    /// </remarks>
    public abstract int AcceptText();

    /// <summary>
    /// Enables or suppresses repainting - the port of <c>SetRedraw(value)</c>.
    /// </summary>
    /// <param name="enable">
    /// <see langword="true"/> to resume redrawing, which is the only value the ported call site
    /// passes.
    /// </param>
    /// <returns>The legacy integer code, where <c>1</c> indicates success.</returns>
    /// <remarks>
    /// <para>
    /// THE ONE BORDERLINE-PRESENTATIONAL MEMBER ON THIS CONTRACT, AND IT STAYS. It is genuinely
    /// called from in-scope logic - <c>se_cst_dw.sru:L442</c>, inside the <c>DeleteRow</c> override,
    /// where deleting the last row requires a repaint so the detail band's colours refresh. It
    /// carries no geometry, no DPI conversion, no font and no window handle, so it does not breach
    /// constraint C-D; excluding it would instead have removed an observable step from a ported
    /// override, which constraint C-B forbids.
    /// </para>
    /// <para>
    /// A headless implementation satisfies it as a no-op returning success. That is not a stub: the
    /// call has no observable effect that a gRPC or REST response can carry, so there is nothing for
    /// it to do and nothing about it is deferred.
    /// </para>
    /// </remarks>
    public abstract int SetRedraw(bool enable);

    /// <summary>
    /// The object currently holding input focus - the port of the PowerScript system function
    /// <c>GetFocus()</c> as used at <c>se_cst_dw.sru:L553</c>.
    /// </summary>
    /// <returns>
    /// The focused object, or <see langword="null"/> when nothing holds focus. Comparable by
    /// reference against this host, which is exactly what the call site does.
    /// </returns>
    /// <remarks>
    /// <para>
    /// RENAMED FROM <c>GetFocus</c>, AND THIS IS THE MEMBER THAT MOVED. The name collides with the
    /// semantic event <see cref="GetFocus"/> [<c>:L400</c>], and C# forbids two members that differ
    /// only in return type. The event kept the name because the eleven event names are ancestry
    /// member names that Domain/DataWindowEventChain.cs reproduces one-for-one, whereas this is the
    /// PowerScript SYSTEM function - never a member of the DataWindow at all - so its .NET name was
    /// always a modelling choice. See DECISION 3 in the file header.
    /// </para>
    /// <para>
    /// Returns <c>object?</c> and not a boolean, because <c>:L553</c> is
    /// <c>if GetFocus() &lt;&gt; this then</c> - an identity comparison against the host, not a
    /// focus test. AAP 0.4.5.2 maps <c>powerobject</c> onto <c>object</c>.
    /// </para>
    /// </remarks>
    public abstract object? GetFocusedObject();

    /// <summary>
    /// Gives this host input focus - the port of <c>SetFocus()</c>
    /// (<c>se_cst_dw.sru:L555</c>).
    /// </summary>
    /// <returns>The legacy integer code, where <c>1</c> indicates success.</returns>
    /// <remarks>
    /// One measured use, reached only when <see cref="AcceptText"/> has failed - the deferred accept
    /// returns focus to the DataWindow so the invalid entry can be corrected.
    /// </remarks>
    public abstract int SetFocus();

    /// <summary>
    /// Reads the status of one item - the port of
    /// <c>GetItemStatus(row, column, buffer)</c>.
    /// </summary>
    /// <param name="row">The one-based row number.</param>
    /// <param name="columnId">
    /// The numeric column id, as produced by
    /// <see cref="DataWindowObjectExtensions.ColumnId(IDataWindowObject)"/> from
    /// <see cref="IDataWindowObject.ID"/> - the port of <c>Long(dwo.ID)</c>.
    /// </param>
    /// <param name="buffer">The buffer to read from.</param>
    /// <returns>The item status.</returns>
    /// <remarks>
    /// <para>
    /// Two measured uses, <c>se_cst_dw.sru:L190</c> and <c>:L335</c>, and BOTH ARE SNAPSHOTS TAKEN
    /// TO BE RESTORED LATER by the matching <see cref="SetItemStatus"/> calls at <c>:L220</c> and
    /// <c>:L376</c>. The pairing is the behaviour: an item-change that is rejected must leave both
    /// the value AND the status as they were, because a restored value with a modified status would
    /// mark a row dirty that the user never successfully edited.
    /// </para>
    /// <para>
    /// The status domain is the published <c>common.v1.ItemStatus</c>, consumed directly rather than
    /// re-declared - see DECISION 1 in the file header.
    /// </para>
    /// </remarks>
    public abstract ItemStatus GetItemStatus(long row, long columnId, DwBuffer buffer);

    /// <summary>
    /// Sets the status of one item - the port of
    /// <c>SetItemStatus(row, column, buffer, status)</c>.
    /// </summary>
    /// <param name="row">The one-based row number.</param>
    /// <param name="columnId">The numeric column id.</param>
    /// <param name="buffer">The buffer to write to.</param>
    /// <param name="status">The status to apply.</param>
    /// <returns>The legacy integer code, where <c>1</c> indicates success.</returns>
    /// <remarks>
    /// Two measured uses, <c>se_cst_dw.sru:L220</c> and <c>:L376</c>, each restoring a status
    /// snapshotted by <see cref="GetItemStatus"/>. Both are guarded by an equality test that the
    /// buffer value has not moved in the meantime - <c>:L216</c> and <c>:L372</c> - because, as the
    /// legacy comment at <c>:L215</c> and <c>:L371</c> states, the buffer may already have been
    /// changed and must not be overwritten.
    /// </remarks>
    public abstract int SetItemStatus(long row, long columnId, DwBuffer buffer, ItemStatus status);


    // ------------------------------------------------------------------------------------------
    //  SetItem - SEVEN OVERLOADS, ONE PER VALUE TYPE THE COERCION TABLE PRODUCES
    //  ----------------------------------------------------------------------------------------
    //  Eight measured call sites, and the overload set is derived from them rather than from what a
    //  DataWindow can hold. `se_cst_dw.sru:L228-L243` performs manual type-directed coercion by the
    //  first five characters of the column type and calls SetItem with the coerced value:
    //
    //      :L233   "char" / "char("                 SetItem(row, Long(dwo.ID), data)
    //      :L235   "decim" / "real" / "numbe"       SetItem(row, Long(dwo.ID), Dec(data))
    //      :L237   "long" / "ulong"                 SetItem(row, Long(dwo.ID), Long(data))
    //      :L239   "datet"                          SetItem(row, Long(dwo.ID), DateTime(data))
    //      :L241   "date"                           SetItem(row, Long(dwo.ID), Date(data))
    //      :L243   "time"                           SetItem(row, Long(dwo.ID), Time(data))
    //
    //  and the remaining two sites, :L219 and :L375, restore the snapshotted ORIGINAL value, which
    //  is held in an `any` - so a seventh overload taking `object?` is required and is not a
    //  convenience.
    //
    //  EVERY VALUE PARAMETER IS NULLABLE. The restore path carries a value read from
    //  `dwo.Primary[row]`, which the oracle explicitly handles as possibly null at :L198-L202, and
    //  the coercion path carries the result of a PowerScript conversion, which yields null for a
    //  null input. AAP 0.4.5.4 forbids collapsing null to zero: doing so would write 0 into a column
    //  the user cleared, which is a silent data change rather than a restored value.
    //
    //  OVERLOAD RESOLUTION IS UNAMBIGUOUS FOR EVERY PORTED CALL SITE, because each passes a
    //  statically typed value. An integer literal binds to the `long?` overload rather than the
    //  `decimal?` one, because long converts implicitly to decimal and not the reverse, which makes
    //  long the better target.
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// Writes a text value into one item - the port of the <c>"char"</c> / <c>"char("</c> coercion
    /// arm at <c>se_cst_dw.sru:L233</c>.
    /// </summary>
    /// <param name="row">The one-based row number.</param>
    /// <param name="columnId">The numeric column id.</param>
    /// <param name="value">The value to write, or <see langword="null"/> to null the item.</param>
    /// <returns>The legacy integer code, where <c>1</c> indicates success.</returns>
    public abstract int SetItem(long row, long columnId, string? value);

    /// <summary>
    /// Writes a decimal value into one item - the port of the <c>"decim"</c> / <c>"real"</c> /
    /// <c>"numbe"</c> coercion arm at <c>se_cst_dw.sru:L235</c>, whose value is
    /// <c>Dec(data)</c>.
    /// </summary>
    /// <param name="row">The one-based row number.</param>
    /// <param name="columnId">The numeric column id.</param>
    /// <param name="value">The value to write, or <see langword="null"/> to null the item.</param>
    /// <returns>The legacy integer code, where <c>1</c> indicates success.</returns>
    public abstract int SetItem(long row, long columnId, decimal? value);

    /// <summary>
    /// Writes an integral value into one item - the port of the <c>"long"</c> / <c>"ulong"</c>
    /// coercion arm at <c>se_cst_dw.sru:L237</c>, whose value is <c>Long(data)</c>.
    /// </summary>
    /// <param name="row">The one-based row number.</param>
    /// <param name="columnId">The numeric column id.</param>
    /// <param name="value">The value to write, or <see langword="null"/> to null the item.</param>
    /// <returns>The legacy integer code, where <c>1</c> indicates success.</returns>
    public abstract int SetItem(long row, long columnId, long? value);

    /// <summary>
    /// Writes a date-and-time value into one item - the port of the <c>"datet"</c> coercion arm at
    /// <c>se_cst_dw.sru:L239</c>.
    /// </summary>
    /// <param name="row">The one-based row number.</param>
    /// <param name="columnId">The numeric column id.</param>
    /// <param name="value">The value to write, or <see langword="null"/> to null the item.</param>
    /// <returns>The legacy integer code, where <c>1</c> indicates success.</returns>
    public abstract int SetItem(long row, long columnId, DateTime? value);

    /// <summary>
    /// Writes a date value into one item - the port of the <c>"date"</c> coercion arm at
    /// <c>se_cst_dw.sru:L241</c>.
    /// </summary>
    /// <param name="row">The one-based row number.</param>
    /// <param name="columnId">The numeric column id.</param>
    /// <param name="value">The value to write, or <see langword="null"/> to null the item.</param>
    /// <returns>The legacy integer code, where <c>1</c> indicates success.</returns>
    /// <remarks>
    /// Kept distinct from the <see cref="DateTime"/> overload because the coercion table keeps them
    /// distinct: <c>"datet"</c> and <c>"date"</c> are separate arms reached by a five-character
    /// prefix match, and merging them would change which conversion a <c>date</c> column receives.
    /// </remarks>
    public abstract int SetItem(long row, long columnId, DateOnly? value);

    /// <summary>
    /// Writes a time-of-day value into one item - the port of the <c>"time"</c> coercion arm at
    /// <c>se_cst_dw.sru:L243</c>.
    /// </summary>
    /// <param name="row">The one-based row number.</param>
    /// <param name="columnId">The numeric column id.</param>
    /// <param name="value">The value to write, or <see langword="null"/> to null the item.</param>
    /// <returns>The legacy integer code, where <c>1</c> indicates success.</returns>
    public abstract int SetItem(long row, long columnId, TimeOnly? value);

    /// <summary>
    /// Writes an untyped value into one item - the port of the RESTORE path at
    /// <c>se_cst_dw.sru:L219</c> and <c>:L375</c>, which carries the snapshotted original value.
    /// </summary>
    /// <param name="row">The one-based row number.</param>
    /// <param name="columnId">The numeric column id.</param>
    /// <param name="value">
    /// The value to write, typed as <see cref="object"/> because the legacy local is an <c>any</c>
    /// read from <see cref="IDataWindowValueBuffer"/>, and <see langword="null"/> when the original
    /// item was null.
    /// </param>
    /// <returns>The legacy integer code, where <c>1</c> indicates success.</returns>
    /// <remarks>
    /// This overload is what makes the two restore sites portable at all. Both take the value from
    /// <c>dwo.Primary[row]</c> without knowing the column's type, so no typed overload can express
    /// them. An implementation dispatches on the runtime type and must accept
    /// <see langword="null"/>, because restoring a null original is the common case for a cleared
    /// column.
    /// </remarks>
    public abstract int SetItem(long row, long columnId, object? value);

    /// <summary>
    /// Reads one entry of a column's code table - the port of
    /// <c>#DataWindow.GetValue(column, index)</c> (<c>n_cst_dwsvc.sru:L651</c>, <c>:L658</c>).
    /// </summary>
    /// <param name="column">The column name.</param>
    /// <param name="index">The one-based code-table entry number.</param>
    /// <returns>
    /// The entry as <c>display~tvalue</c>, tab-separated, or the EMPTY STRING once the index is past
    /// the last entry.
    /// </returns>
    /// <remarks>
    /// <para>
    /// ADDED ON MEASURED EVIDENCE. This member is absent from the enumerated member list in the
    /// folder brief, and it is genuinely consumed - twice, at <c>n_cst_dwsvc.sru:L651</c> and
    /// <c>:L658</c>. AAP 0.4.2.5's criterion is consumption, so it belongs on the contract; leaving
    /// it off would have made <c>_of_getcolumnvaluemap</c>'s code-table branch unportable.
    /// </para>
    /// <para>
    /// THE EMPTY-STRING TERMINATOR IS THE LOOP CONDITION AND MUST BE PRESERVED EXACTLY. The oracle
    /// at <c>:L650-L659</c> starts at index <c>1</c>, reads, and loops <c>do while(sVal &lt;&gt; "")</c>,
    /// incrementing. An implementation that threw, or that returned null, for an index past the end
    /// would turn a normal termination into a fault. Splitting on the tab is the caller's job -
    /// <c>:L653-L656</c> does it with <c>Pos</c> and <c>Mid</c> - so this member returns the joined
    /// form unaltered.
    /// </para>
    /// </remarks>
    public abstract string GetValue(string column, long index);

    /// <summary>
    /// Obtains the drop-down DataWindow behind a DDDW column - the port of
    /// <c>#DataWindow.GetChild(column, ref dwc)</c> (<c>n_cst_dwsvc.sru:L592</c>).
    /// </summary>
    /// <param name="column">The column name.</param>
    /// <param name="child">
    /// Receives the child handle. Passed by reference rather than as an output parameter, and left
    /// UNTOUCHED when the column has no child - see the remarks.
    /// </param>
    /// <returns>The legacy integer code, where <c>1</c> indicates success and <c>-1</c> failure.</returns>
    /// <remarks>
    /// <para>
    /// ADDED ON MEASURED EVIDENCE, like <see cref="GetValue"/>: one call site, and without it the
    /// DDDW branch of <c>_of_getcolumnvaluemap</c> cannot be ported.
    /// </para>
    /// <para>
    /// <c>ref</c> AND NOT <c>out</c>, FOR TWO INDEPENDENT REASONS. AAP 0.4.5.2 maps a legacy
    /// <c>ref</c> parameter onto a C# <c>ref</c> parameter mechanically, and <c>ref</c> is also the
    /// faithful shape: PowerBuilder leaves the variable unset on failure, whereas <c>out</c> would
    /// oblige every implementation to assign before returning and so would erase the distinction
    /// between "assigned an invalid handle" and "not assigned at all".
    /// </para>
    /// <para>
    /// THE RETURN CODE IS NOT WHAT THE ORACLE TESTS. <c>:L592</c> DISCARDS it and establishes
    /// validity on the next line instead, with <c>IsValidObject(dwc)</c> [<c>:L593</c>] - the ported
    /// <c>Predicates.IsValidObject</c>. Ported callers must do the same rather than branching on the
    /// integer, because that is the check the legacy actually performs.
    /// </para>
    /// </remarks>
    public abstract int GetChild(string column, ref IDataWindowChild? child);

    // ------------------------------------------------------------------------------------------
    //  Filter and DeleteRow - THE TWO OVERRIDABLE BASE OPERATIONS
    //  ----------------------------------------------------------------------------------------
    //  `se_cst_dw` overrides both and calls the base implementation from inside the override:
    //  `rtCode = super::Filter()` [se_cst_dw.sru:L406] and `rtCode = super::DeleteRow(nRow)`
    //  [:L431]. A plain abstract member cannot be reached through `base.` in C#, so each is
    //  declared virtual over a protected abstract *Core operation - the template method pattern.
    //  Domain/DataWindowEventChain.cs overrides Filter and DeleteRow and reaches the built-in
    //  behaviour with `base.Filter()` and `base.DeleteRow(row)`, which is exactly the legacy shape.
    //
    //  SUCCESS IS 1, NOT 0, FOR BOTH. `:L407` is `if rtCode = 1 then` and `:L432` is
    //  `if rtCode <> 1 then return rtCode`. Neither is mapped onto RetCode, whose OK is 0 - the two
    //  numbering schemes are incompatible and conflating them inverts every success test.
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// Applies the current filter - the overridable seam whose base behaviour
    /// <c>se_cst_dw.sru:L406</c> reaches as <c>super::Filter()</c>.
    /// </summary>
    /// <returns>The legacy integer code, where <c>1</c> INDICATES SUCCESS.</returns>
    /// <remarks>
    /// Override this to reproduce <c>se_cst_dw</c>'s <c>filter</c> override
    /// [<c>se_cst_dw.sru:L403-L414</c>], which calls the base implementation and, on success, raises
    /// the row-select service's filtered notification. Call <c>base.Filter()</c> from the override to
    /// reach the built-in behaviour supplied by <see cref="FilterCore"/>.
    /// </remarks>
    public virtual int Filter()
    {
        return FilterCore();
    }

    /// <summary>
    /// The built-in filter behaviour an implementation or test double supplies.
    /// </summary>
    /// <returns>The legacy integer code, where <c>1</c> indicates success.</returns>
    /// <remarks>
    /// Separated from <see cref="Filter"/> solely so that an override of <see cref="Filter"/> can
    /// still reach this behaviour through <c>base.Filter()</c>. It carries no behaviour of its own
    /// and is never called directly by a ported call site.
    /// </remarks>
    protected abstract int FilterCore();

    /// <summary>
    /// Deletes one row - the overridable seam whose base behaviour <c>se_cst_dw.sru:L431</c> reaches
    /// as <c>super::DeleteRow(nRow)</c>.
    /// </summary>
    /// <param name="row">The one-based row number to delete.</param>
    /// <returns>The legacy integer code, where <c>1</c> INDICATES SUCCESS.</returns>
    /// <remarks>
    /// Override this to reproduce <c>se_cst_dw</c>'s <c>deleterow</c> override
    /// [<c>se_cst_dw.sru:L416-L446</c>], which rejects an out-of-range request with <c>-1</c>
    /// [<c>:L421</c>], redirects row <c>0</c> to the current row [<c>:L424-L426</c>], calls the base
    /// implementation, re-raises the row-change event, and repaints when the last row went
    /// [<c>:L441-L443</c>]. Call <c>base.DeleteRow(row)</c> from the override to reach the built-in
    /// behaviour supplied by <see cref="DeleteRowCore(long)"/>.
    /// </remarks>
    public virtual int DeleteRow(long row)
    {
        return DeleteRowCore(row);
    }

    /// <summary>
    /// The built-in row-deletion behaviour an implementation or test double supplies.
    /// </summary>
    /// <param name="row">The one-based row number to delete.</param>
    /// <returns>The legacy integer code, where <c>1</c> indicates success.</returns>
    /// <remarks>
    /// Separated from <see cref="DeleteRow(long)"/> for the same reason
    /// <see cref="FilterCore"/> is separated from <see cref="Filter"/>: so an override can reach it
    /// through <c>base.</c>.
    /// </remarks>
    protected abstract int DeleteRowCore(long row);


    // ==========================================================================================
    //  THE ELEVEN SEMANTIC DATAWINDOW EVENTS
    //  ----------------------------------------------------------------------------------------
    //  These come from FURTHER UP THE ANCESTRY CHAIN than se_cst_datawindow, which supplies none of
    //  them - which is precisely why a theming-free host contract loses no behaviour at all. Every
    //  raise site below was verified by search on
    //  ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:
    //
    //      RButtonDown        :L115        ItemFocusChanged   :L177
    //      RowFocusChanged    :L125        ItemChanged        :L292
    //      RowFocusChanging   :L131        ItemError          :L343
    //      DoubleClicked      :L136        LoseFocus          :L392
    //      Clicked            :L145        GetFocus           :L400
    //      EditChanged        :L164
    //
    //  THE LIST IS COMPLETE AND CLOSED, NOT A SAMPLE. Two raw pbm_dwn* events raise NO semantic
    //  counterpart and reach the event broker only: `ondwnrbuttonup` [:L120] triggers
    //  EVT_RBUTTONUP and nothing else, and `ondwnlbuttonup` [:L395] triggers EVT_LBUTTONUP and
    //  nothing else. There is therefore deliberately no RButtonUp and no LButtonUp member here.
    //  Inventing either would fabricate a hook the oracle does not have (constraint C-B) and would
    //  give Domain/DataWindowEventChain.cs a twelfth and thirteenth event to route that nothing
    //  raises.
    //
    //  WHY THE DEFAULT BODIES RETURN 0 (AND ONE RETURNS NULL). Every one of the eleven is compared
    //  against, or propagated as, a numeric code, and ten of the eleven are consumed by the
    //  `= 1 then return 1` prevent convention - so an unhandled event must yield a value that means
    //  "continue". 0 is that value. ItemError is the single exception and is nullable; see its own
    //  remarks.
    //
    //  These are virtual methods rather than C# `event` members deliberately. A PowerBuilder event
    //  raised with the `Event` keyword RETURNS A VALUE to its single raiser, which a C# event -
    //  multicast, returning void - cannot express. Using `event` here would silently discard every
    //  prevent and every item-change code, which are the two most load-bearing values in this
    //  service. The broker in shared/PowerFramework.Shared.Eventful carries the genuinely multicast
    //  half of the legacy model, and it is a separate mechanism from these eleven.
    // ==========================================================================================

    /// <summary>
    /// Raised when the right mouse button goes down - the port of
    /// <c>Event RButtonDown(xpos,ypos,row,dwo)</c> (<c>se_cst_dw.sru:L115</c>).
    /// </summary>
    /// <param name="xpos">The pointer x position, in the units the DataWindow reports.</param>
    /// <param name="ypos">The pointer y position, in the units the DataWindow reports.</param>
    /// <param name="row">The one-based row under the pointer, or <c>0</c> when none.</param>
    /// <param name="dwo">The object under the pointer.</param>
    /// <returns><c>1</c> to prevent; any other value to continue.</returns>
    /// <remarks>
    /// The raw handler returns <c>1</c> immediately when this returns <c>1</c> and only then
    /// consults the broker [<c>:L115-L117</c>], so a prevention here suppresses the broker
    /// notification as well. The positions are carried as plain numbers and are NEVER converted -
    /// no DPI or unit conversion appears anywhere in this contract (constraint C-D); conversion
    /// belongs to the deferred <c>/v1/design/**</c> capability.
    /// </remarks>
    public virtual long RButtonDown(long xpos, long ypos, long row, IDataWindowObject dwo)
    {
        return 0L;
    }

    /// <summary>
    /// Raised after the current row has changed - the port of
    /// <c>Event RowFocusChanged(currentRow)</c> (<c>se_cst_dw.sru:L125</c>).
    /// </summary>
    /// <param name="currentRow">The new current row.</param>
    /// <returns><c>1</c> to prevent; any other value to continue.</returns>
    /// <remarks>
    /// Gated by <c>EID_ROWFOCUSCHANGE</c> at <c>:L124</c>: when that bit is disabled the raw handler
    /// returns <c>0</c> without raising this at all. The gate itself belongs to
    /// Domain/EventGate.cs, not here.
    /// </remarks>
    public virtual long RowFocusChanged(long currentRow)
    {
        return 0L;
    }

    /// <summary>
    /// Raised before the current row changes - the port of
    /// <c>Event RowFocusChanging(currentrow,newrow)</c> (<c>se_cst_dw.sru:L131</c>).
    /// </summary>
    /// <param name="currentRow">The row focus is leaving.</param>
    /// <param name="newRow">The row focus is moving to.</param>
    /// <returns><c>1</c> to prevent the move; any other value to allow it.</returns>
    /// <remarks>
    /// Gated by <c>EID_ROWFOCUSCHANGE</c> at <c>:L130</c>. Unlike its <c>Changed</c> counterpart the
    /// broker trigger that follows it is ALSO vetoable [<c>:L132</c>], so either can stop the move.
    /// </remarks>
    public virtual long RowFocusChanging(long currentRow, long newRow)
    {
        return 0L;
    }

    /// <summary>
    /// Raised on a double click - the port of
    /// <c>Event DoubleClicked(xpos,ypos,row,dwo)</c> (<c>se_cst_dw.sru:L136</c>).
    /// </summary>
    /// <param name="xpos">The pointer x position.</param>
    /// <param name="ypos">The pointer y position.</param>
    /// <param name="row">The one-based row under the pointer, or <c>0</c> when none.</param>
    /// <param name="dwo">The object under the pointer.</param>
    /// <returns><c>1</c> to prevent; any other value to continue.</returns>
    /// <remarks>
    /// The raw handler re-checks that the host is still valid before notifying the broker
    /// [<c>:L138</c>], because a handler for this event may legitimately have destroyed the control.
    /// That liveness guard belongs to Domain/DataWindowEventChain.cs.
    /// </remarks>
    public virtual long DoubleClicked(long xpos, long ypos, long row, IDataWindowObject dwo)
    {
        return 0L;
    }

    /// <summary>
    /// Raised on a single left click - the port of
    /// <c>Event Clicked(xpos,ypos,row,dwo)</c> (<c>se_cst_dw.sru:L145</c>).
    /// </summary>
    /// <param name="xpos">The pointer x position.</param>
    /// <param name="ypos">The pointer y position.</param>
    /// <param name="row">The one-based row under the pointer, or <c>0</c> when none.</param>
    /// <param name="dwo">The object under the pointer.</param>
    /// <returns><c>1</c> to prevent; any other value to continue.</returns>
    /// <remarks>
    /// The raw handler carries the same liveness guard as the double-click [<c>:L147</c>] and then
    /// performs the focusless row move at <c>:L152-L159</c> that
    /// <see cref="SetRow(long)"/>'s remarks describe.
    /// </remarks>
    public virtual long Clicked(long xpos, long ypos, long row, IDataWindowObject dwo)
    {
        return 0L;
    }

    /// <summary>
    /// Raised as the edit text changes, before any value reaches the buffer - the port of
    /// <c>Event EditChanged(row,dwo,data)</c> (<c>se_cst_dw.sru:L164</c>).
    /// </summary>
    /// <param name="row">The one-based row being edited.</param>
    /// <param name="dwo">The column being edited.</param>
    /// <param name="data">The current edit text.</param>
    /// <returns><c>1</c> to prevent; any other value to continue.</returns>
    /// <remarks>
    /// Also re-raised from inside the item-change protocol at <c>:L207</c> when the value in the
    /// buffer turns out to have moved, which is the one place a semantic event is raised from
    /// another semantic event's handler. That nesting belongs to
    /// Domain/ItemChangeProtocol.cs.
    /// </remarks>
    public virtual long EditChanged(long row, IDataWindowObject dwo, string data)
    {
        return 0L;
    }

    /// <summary>
    /// Raised when focus moves between columns - the port of
    /// <c>Event ItemFocusChanged(row,dwo)</c> (<c>se_cst_dw.sru:L177</c>).
    /// </summary>
    /// <param name="row">The one-based row now holding focus.</param>
    /// <param name="dwo">The column now holding focus.</param>
    /// <returns><c>1</c> to prevent; any other value to continue.</returns>
    /// <remarks>Gated by <c>EID_ITEMFOCUSCHANGE</c> at <c>:L176</c>.</remarks>
    public virtual long ItemFocusChanged(long row, IDataWindowObject dwo)
    {
        return 0L;
    }

    /// <summary>
    /// Raised to validate a pending item change, BEFORE the value reaches the buffer - the port of
    /// <c>Event ItemChanged(row,dwo,data)</c> (<c>se_cst_dw.sru:L292</c>).
    /// </summary>
    /// <param name="row">The one-based row being changed.</param>
    /// <param name="dwo">The column being changed.</param>
    /// <param name="data">The entered text, not yet written to the buffer.</param>
    /// <returns>
    /// A value in the FOUR-VALUE ITEM-CHANGE ALPHABET <c>{0, 1, 2, 3}</c> - NOT a
    /// <c>RetCode</c>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// THIS RETURN VALUE IS THE MOST CONSEQUENTIAL ON THE WHOLE CONTRACT. It is propagated verbatim
    /// out of <c>ondoitemchange</c> [<c>:L292</c>] into the item-change micro-protocol, where
    /// <c>:L211-L251</c> dispatches on it: <c>case 1</c> FALLS THROUGH to <c>case 2</c>;
    /// <c>case 3</c> keeps the value, does not move focus and REWRITES the result to <c>1</c>; and
    /// the default arm coerces by column-type prefix and then FORCIBLY RETURNS <c>2</c>. It is also
    /// stashed for the validation-error event to consume [<c>:L195</c>], where <c>1</c> or
    /// <c>3</c> pre-sets that event's own result [<c>:L338-L340</c>].
    /// </para>
    /// <para>
    /// It must therefore never be mapped onto the return-code algebra. <c>RetCode.OK</c> is
    /// <c>0</c> and <c>RetCode.PREVENT</c> is <c>1</c>, which coincide by accident for two of the
    /// four values and diverge completely for the other two. The alphabet is modelled as its own
    /// domain in Domain/ItemChangeProtocol.cs; this member's job is only to carry the raw number
    /// out of the handler without reinterpreting it.
    /// </para>
    /// <para>
    /// The dormant validation path at <c>:L280-L290</c> - a commented-out byte-length check with its
    /// own dialog - is carried across as commented and inert by the ported caller and is NOT revived
    /// (constraint C-B).
    /// </para>
    /// </remarks>
    public virtual long ItemChanged(long row, IDataWindowObject dwo, string data)
    {
        return 0L;
    }

    /// <summary>
    /// Raised when the DataWindow rejects an entered value - the port of
    /// <c>Event ItemError(row,dwo,data)</c> (<c>se_cst_dw.sru:L343</c>).
    /// </summary>
    /// <param name="row">The one-based row whose entry was rejected.</param>
    /// <param name="dwo">The column whose entry was rejected.</param>
    /// <param name="data">The rejected text.</param>
    /// <returns>
    /// The handler's code, or <see langword="null"/> WHEN NO HANDLER PRODUCED ONE.
    /// </returns>
    /// <remarks>
    /// <para>
    /// THE ONLY NULLABLE RETURN AMONG THE ELEVEN, AND IT IS NULLABLE BECAUSE THE ORACLE PROVES A
    /// NULL CAN ARRIVE. <c>:L343</c> assigns the result and <c>:L344</c> immediately coerces it:
    /// <c>if IsNull(rtCode) then rtCode = 0</c>. No other one of the eleven carries such a guard, so
    /// this is the one place a non-null guarantee would be a promise the legacy never made.
    /// </para>
    /// <para>
    /// The coercion itself is deliberately NOT performed here. It belongs to
    /// Domain/ValidationSession.cs, which reproduces <c>:L344</c> at the point the oracle performs
    /// it. Defaulting to <c>0</c> in this member instead would make the ported line unreachable and
    /// would remove the ability to distinguish "handler returned zero" from "no handler ran" -
    /// which are the same outcome downstream, but only because <c>:L344</c> makes them the same, and
    /// that step has to be visible in the port.
    /// </para>
    /// <para>
    /// The default body returns <see langword="null"/> rather than <c>0</c> for exactly that
    /// reason: an unhandled PowerBuilder event is the case <c>:L344</c> exists to absorb.
    /// </para>
    /// </remarks>
    public virtual long? ItemError(long row, IDataWindowObject dwo, string data)
    {
        return null;
    }

    /// <summary>
    /// Raised when the DataWindow loses focus - the port of <c>Event LoseFocus()</c>
    /// (<c>se_cst_dw.sru:L392</c>).
    /// </summary>
    /// <returns>The handler's code, which the raw handler returns verbatim.</returns>
    /// <remarks>
    /// The raw handler queues the deferred accept first and triggers the broker second, then returns
    /// this value [<c>:L387-L392</c>]. The queued continuation replaces a PowerBuilder
    /// <c>Post</c> to the Win32 message queue, which a headless Linux container has no equivalent of
    /// (AAP 0.4.5.4); the queueing belongs to Domain/ValidationSession.cs.
    /// </remarks>
    public virtual long LoseFocus()
    {
        return 0L;
    }

    /// <summary>
    /// Raised when the DataWindow gains focus - the port of <c>Event GetFocus()</c>
    /// (<c>se_cst_dw.sru:L400</c>).
    /// </summary>
    /// <returns>The handler's code, which the raw handler returns verbatim.</returns>
    /// <remarks>
    /// NOT to be confused with <see cref="GetFocusedObject"/>, which is the PowerScript SYSTEM
    /// function used at <c>:L553</c> to ask which object holds focus. The two share a spelling in
    /// PowerScript, where the <c>Event</c> keyword disambiguates them, and C# has no such keyword -
    /// so the function was renamed and this event kept the name. See DECISION 3 in the file header.
    /// </remarks>
    public virtual long GetFocus()
    {
        return 0L;
    }
}


/// <summary>
/// The host-facing half of <c>n_cst_dwsvc</c>: the common base every DataWindow service derives
/// from, carrying attachment to a <see cref="DataWindowServiceHost"/>, the enablement protocol, and
/// the two constant catalogues whose legacy spellings are preserved verbatim.
/// </summary>
/// <remarks>
/// <para>
/// PORTED FROM <c>ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc.sru</c> (864 lines), which
/// is the common base of the five services <c>se_cst_dw</c> attaches
/// [<c>se_cst_dw.sru:L80-L84</c>]. Only the HOST-FACING half belongs here. The other half - the
/// <c>_of_evaluate</c>, <c>_of_lookupdisplay</c>, <c>_of_getitemprop</c>, <c>_of_getcolumnvaluemap</c>
/// and object-enumeration helpers declared at <c>:L46-L82</c> - belongs to Expressions/ and
/// Services/, which consume this type rather than being consumed by it.
/// </para>
/// <para>
/// THE ATTACHMENT PARAMETER IS DELIBERATELY GENERALISED. The legacy hook is
/// <c>event oninit ( se_cst_dw dw )</c> [<c>:L9</c>] - it names the DERIVED type. Taking
/// <see cref="DataWindowServiceHost"/> instead is exactly what AAP 0.2.1.3 Correction 3 requires:
/// binding to the base contract is what stops DataServices from inheriting the DesignSystem
/// inheritance edge. Nothing is lost, because Domain/DataWindowEventChain.cs - the port of
/// <c>se_cst_dw</c> - derives from that contract and so is still accepted here.
/// </para>
/// <para>
/// NOTHING HERE IS STATIC AND MUTABLE. <c>n_cst_dwsvc.sru:L12</c> declares
/// <c>global n_cst_dwsvc n_cst_dwsvc</c>, a global auto-instance shadowing its own type name, and
/// <c>se_cst_dw.sru:L35</c> does the same for its type. Both are artefacts of PowerBuilder's single
/// flat namespace (AAP 0.4.5.1), and AAP 0.5.4.2 fixes the resolution: the type keeps the descriptive
/// .NET name and the instance becomes an injected dependency. Neither shadow is reproduced, which is
/// also what makes this type unit-testable without process-wide state (constraint C-H).
/// </para>
/// </remarks>
public abstract class DataWindowServiceBase
{
    // ==========================================================================================
    //  STYLE_* - THE DATA SOURCE PRESENTATION STYLES [n_cst_dwsvc.sru:L18-L24]
    //  ----------------------------------------------------------------------------------------
    //  SPELLINGS PRESERVED VERBATIM, INCLUDING THE SCREAMING_SNAKE FORM, PER AAP 0.4.5.3: these
    //  identifiers appear in serialized payloads, in log records and in characterization
    //  recordings, so a rename would not restyle a symbol - it would silently invalidate every
    //  stored comparison that mentions it.
    //
    //  *** VALUE 6 IS DELIBERATELY ABSENT AND MUST STAY ABSENT ***
    //  The legacy sequence is 0, 1, 2, 3, 4, 5, then 7. There is no constant for 6 anywhere in
    //  n_cst_dwsvc.sru, and STYLE_RICHTEXT is 7. That gap is the DataWindow presentation-style
    //  numbering as PowerBuilder defines it, so:
    //      * do NOT renumber STYLE_RICHTEXT to 6 to close the gap;
    //      * do NOT insert a placeholder constant at 6;
    //      * do NOT convert this set to a C# enum, which invites exactly those two "tidy-ups" and
    //        would additionally make an out-of-range cast silently legal.
    //  A `long` constant set is what the legacy declares [constant long], and it keeps the gap
    //  visible at the point of declaration. Constraint C-B forbids closing it: a style read of 6
    //  must remain unmatched here exactly as it is unmatched in the oracle.
    //
    //  The trailing comments are the legacy's own, carried across verbatim so the mapping from
    //  constant to PowerBuilder presentation style survives without a second lookup.
    // ==========================================================================================

    /// <summary>Form, group, query, or tabular. Legacy value <c>0</c> (<c>n_cst_dwsvc.sru:L18</c>).</summary>
    /// <remarks>
    /// The default arm covers FOUR PowerBuilder presentation styles at one value, which is why it is
    /// named for none of them. A reader looking for a distinct "tabular" constant will not find one,
    /// and must not add one.
    /// </remarks>
    public const long STYLE_DEFAULT = 0;

    /// <summary>Grid. Legacy value <c>1</c> (<c>n_cst_dwsvc.sru:L19</c>).</summary>
    public const long STYLE_GRID = 1;

    /// <summary>Label. Legacy value <c>2</c> (<c>n_cst_dwsvc.sru:L20</c>).</summary>
    public const long STYLE_LABEL = 2;

    /// <summary>Graph. Legacy value <c>3</c> (<c>n_cst_dwsvc.sru:L21</c>).</summary>
    public const long STYLE_GRAPH = 3;

    /// <summary>Crosstab. Legacy value <c>4</c> (<c>n_cst_dwsvc.sru:L22</c>).</summary>
    /// <remarks>
    /// The crosstab style is also the one the cross-thread full-state codec crashes on when the
    /// crosstab has too many columns - a defect recorded in AAP 0.6.3.7 and preserved on the
    /// Persistence side. Nothing about that defect is reachable from this file; the note is here only
    /// so the constant is not mistaken for unused.
    /// </remarks>
    public const long STYLE_CROSSTAB = 4;

    /// <summary>Composite. Legacy value <c>5</c> (<c>n_cst_dwsvc.sru:L23</c>).</summary>
    public const long STYLE_COMPOSITE = 5;

    /// <summary>
    /// RichText. Legacy value <c>7</c> (<c>n_cst_dwsvc.sru:L24</c>) - SEVEN, NOT SIX. See the gap
    /// ruling above this constant block.
    /// </summary>
    public const long STYLE_RICHTEXT = 7;

    // ==========================================================================================
    //  COL_TYPE_* - THE COLUMN TYPE CATEGORIES [n_cst_dwsvc.sru:L26-L32]
    //  ----------------------------------------------------------------------------------------
    //  Contiguous 0 through 6 with NO gap, which is worth stating explicitly precisely because the
    //  STYLE_* set immediately above does have one - the two sets look alike and behave differently.
    //  Spellings preserved verbatim per AAP 0.4.5.3, for the same serialization and recording
    //  reasons.
    //
    //  These are the CATEGORIES the raw `dwo.ColType` string is reduced to, not the raw string
    //  itself. The reduction is `_of_convertcoltype` [:L503-L518], which belongs to the half of
    //  n_cst_dwsvc that is not ported here; the categories are declared here because the legacy
    //  declares them here, and because the child-DataWindow value-map path branches on them at
    //  n_cst_dwsvc.sru:L603 and :L619.
    // ==========================================================================================

    /// <summary>An unrecognised or undeterminable column type. Legacy value <c>0</c> (<c>:L26</c>).</summary>
    /// <remarks>
    /// This is the value the value-map path leaves a display or data column at when
    /// <see cref="IDataWindowChild.Describe(string)"/> answers with the empty string, <c>"?"</c> or
    /// <c>"!"</c> - the three outcomes tested at <c>n_cst_dwsvc.sru:L598-L599</c>. It is a real
    /// category and not an error code.
    /// </remarks>
    public const long COL_TYPE_UNKNOWN = 0;

    /// <summary>A character column. Legacy value <c>1</c> (<c>n_cst_dwsvc.sru:L27</c>).</summary>
    public const long COL_TYPE_STRING = 1;

    /// <summary>An integral column. Legacy value <c>2</c> (<c>n_cst_dwsvc.sru:L28</c>).</summary>
    /// <remarks>
    /// Reached through <see cref="IDataWindowChild.GetItemNumber(long, string)"/>, which returns a
    /// <see cref="double"/> - the legacy category name says INTEGER while the accessor it selects is
    /// PowerBuilder's <c>GetItemNumber</c>. That mismatch is the legacy's and is preserved
    /// (constraint C-B); the constant is named for what the oracle names it, not for what its
    /// accessor returns.
    /// </remarks>
    public const long COL_TYPE_INTEGER = 2;

    /// <summary>A decimal or real column. Legacy value <c>3</c> (<c>n_cst_dwsvc.sru:L29</c>).</summary>
    public const long COL_TYPE_DECIMAL = 3;

    /// <summary>A date-and-time column. Legacy value <c>4</c> (<c>n_cst_dwsvc.sru:L30</c>).</summary>
    public const long COL_TYPE_DATETIME = 4;

    /// <summary>A date column. Legacy value <c>5</c> (<c>n_cst_dwsvc.sru:L31</c>).</summary>
    public const long COL_TYPE_DATE = 5;

    /// <summary>A time-of-day column. Legacy value <c>6</c> (<c>n_cst_dwsvc.sru:L32</c>).</summary>
    /// <remarks>
    /// The last member of the set, and the reason the set is contiguous where <c>STYLE_*</c> is not:
    /// <c>6</c> is a declared column-type category even though it is not a declared presentation
    /// style.
    /// </remarks>
    public const long COL_TYPE_TIME = 6;

    /// <summary>
    /// The host this service is attached to - the port of <c>privatewrite se_cst_dw #DataWindow</c>
    /// (<c>n_cst_dwsvc.sru:L36</c>).
    /// </summary>
    /// <value>
    /// The attached host, or <see langword="null"/> BEFORE <see cref="OnInit"/> has run.
    /// </value>
    /// <remarks>
    /// <para>
    /// THE ACCESS ASYMMETRY IS REPRODUCED WITH C# ACCESSIBILITY. <c>privatewrite</c> means
    /// externally readable but writable only by the declaring type, which is a public getter with a
    /// private setter. Only <see cref="OnInit"/> assigns it, exactly as only <c>oninit</c> assigns it
    /// in the legacy [<c>:L85</c>].
    /// </para>
    /// <para>
    /// NULLABLE, AND THAT IS FAITHFUL RATHER THAN DEFENSIVE. A PowerBuilder instance variable of
    /// object type is unset until assigned, so between construction and <c>oninit</c> the legacy
    /// member holds an invalid object reference that <c>IsValidObject</c> reports as false. Declaring
    /// it non-nullable would assert an invariant the legacy does not hold, and would hide the
    /// not-yet-attached state from every consumer.
    /// </para>
    /// </remarks>
    public DataWindowServiceHost? DataWindow { get; private set; }

    /// <summary>
    /// The broker lifted off the host during attachment - the port of
    /// <c>privatewrite n_cst_eventful #Eventful</c> (<c>n_cst_dwsvc.sru:L37</c>).
    /// </summary>
    /// <value>
    /// The host's broker, or <see langword="null"/> before <see cref="OnInit"/> has run.
    /// </value>
    /// <remarks>
    /// Held as its own field rather than read through <see cref="DataWindow"/> on demand because the
    /// legacy holds it as its own field [<c>:L86</c>]. That is not a redundant cache: it makes the
    /// binding a one-time act performed by <see cref="OnInit"/>, which is what the attachment
    /// contract is.
    /// </remarks>
    public EventBroker? Eventful { get; private set; }

    /// <summary>
    /// Whether this service is active - the port of <c>protectedwrite boolean #Enabled</c>
    /// (<c>n_cst_dwsvc.sru:L38</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>protectedwrite</c> means externally readable and writable by derived types, so the setter
    /// is protected rather than private. That distinction from
    /// <see cref="DataWindow"/> and <see cref="Eventful"/> is the legacy's own and is reproduced
    /// exactly.
    /// </para>
    /// <para>
    /// The public read is load-bearing: <c>se_cst_dw</c> gates work on other services' flags -
    /// <c>DropdownSearch.#Enabled</c> [<c>se_cst_dw.sru:L169</c>], <c>ColumnExp.#Enabled</c>
    /// [<c>:L313</c>] and <c>RowSelect.#Enabled</c> [<c>:L408</c>] - so this must be readable from
    /// outside the service that owns it.
    /// </para>
    /// <para>
    /// Defaults to <see langword="false"/>, matching a PowerBuilder <c>boolean</c> instance variable,
    /// which initialises false. A service is therefore inert until
    /// <see cref="SetEnabled(in bool)"/> turns it on.
    /// </para>
    /// </remarks>
    public bool Enabled { get; protected set; }

    /// <summary>
    /// Attaches this service to a host - the port of <c>event oninit ( se_cst_dw dw )</c>
    /// (<c>n_cst_dwsvc.sru:L9</c>, body <c>:L85-L87</c>).
    /// </summary>
    /// <param name="dw">The host to attach to.</param>
    /// <exception cref="ArgumentNullException"><paramref name="dw"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// THE TWO-LINE BODY IS THE WHOLE ATTACHMENT CONTRACT: <c>#DataWindow = dw</c> [<c>:L85</c>] and
    /// <c>#Eventful = dw.Eventful</c> [<c>:L86</c>]. The service receives the host and lifts the
    /// broker off it, in that order, in one call. Both are bound together or neither is - there is no
    /// partially attached state, and a caller cannot supply one without the other.
    /// </para>
    /// <para>
    /// Raised on each of the five attached services from the host's own construction
    /// [<c>se_cst_dw.sru:L576-L580</c>], which is why it is public and virtual rather than protected:
    /// the host triggers it from outside, and a derived service overrides it to add its own
    /// initialisation and then calls <c>base.OnInit(dw)</c>.
    /// </para>
    /// <para>
    /// THE NULL GUARD IS A FAIL-FAST SUBSTITUTION, NOT A NEW BEHAVIOUR. The legacy has no guard, so a
    /// null host would assign null and then fault on <c>dw.Eventful</c> one line later with a
    /// PowerBuilder null object reference error. A named
    /// <see cref="ArgumentNullException"/> fails in the same place for the same reason and says which
    /// argument was wrong, which is the fail-fast posture AAP 0.1.4 requires be preserved AS
    /// fail-fast and never softened into warning-and-continue.
    /// </para>
    /// </remarks>
    public virtual void OnInit(DataWindowServiceHost dw)
    {
        ArgumentNullException.ThrowIfNull(dw);

        // n_cst_dwsvc.sru:L85 - the host itself.
        DataWindow = dw;

        // n_cst_dwsvc.sru:L86 - and its broker, lifted off it rather than created here.
        Eventful = dw.Eventful;
    }

    /// <summary>
    /// The enablement hook a derived service overrides to accept or VETO a state change - the port of
    /// <c>event type long onenable ( boolean enabled )</c> (<c>n_cst_dwsvc.sru:L10</c>).
    /// </summary>
    /// <param name="enabled">The state being requested.</param>
    /// <returns>
    /// <c>1</c> TO VETO the change; any other value to allow it.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The veto signal is the bare numeral <c>1</c> because that is what <c>of_setenabled</c> tests
    /// for [<c>:L90</c>]. It is NOT <c>RetCode.PREVENT</c>, even though that constant also happens to
    /// be <c>1</c>: see <see cref="SetEnabled(in bool)"/>, which explains why the two must not be
    /// conflated and what the caller actually observes.
    /// </para>
    /// <para>
    /// The default returns <c>0</c>, which allows the change. A PowerBuilder event with no script
    /// attached yields no veto, so a service that does not care about enablement inherits exactly
    /// that.
    /// </para>
    /// </remarks>
    protected virtual long OnEnable(bool enabled)
    {
        return 0L;
    }

    /// <summary>
    /// Enables or disables this service - the port of <c>of_setenabled</c>
    /// (<c>n_cst_dwsvc.sru:L89-L95</c>), reproduced statement for statement.
    /// </summary>
    /// <param name="enabled">The state to move to.</param>
    /// <returns>
    /// <c>RetCode.OK</c> when the state already matched or the change was applied, and
    /// <c>RetCode.FAILED</c> when <see cref="OnEnable(bool)"/> vetoed it.
    /// </returns>
    /// <remarks>
    /// <para>
    /// THE IDEMPOTENT EARLY-OUT IS OBSERVABLE AND MUST NOT BE SIMPLIFIED AWAY. <c>:L89</c> is
    /// <c>if #Enabled = bEnabled then return RetCode.OK</c>, which returns success WITHOUT RAISING
    /// <see cref="OnEnable(bool)"/> AT ALL. A derived service therefore cannot rely on being asked
    /// about a no-op change, and a reordering that raised the hook first would introduce a call the
    /// legacy never makes.
    /// </para>
    /// <para>
    /// *** THE VETO MAPS TO RetCode.FAILED (-1), NOT RetCode.PREVENT (1). *** <c>:L90</c> is
    /// <c>if Event OnEnable(bEnabled) = 1 then return RetCode.FAILED</c>. The handler SIGNALS with
    /// the numeral <c>1</c> and this function TRANSLATES that signal into <c>-1</c>. The observable
    /// consequence, which is the whole reason this is called out: for a vetoed change,
    /// <c>Predicates.IsPrevented(result)</c> is FALSE and <c>Predicates.IsFailed(result)</c> is TRUE.
    /// A caller checking for prevention will not see the veto. That is preserved exactly under
    /// constraint C-B - it is a legacy quirk, not an implementation error, and correcting it would
    /// change the branch every existing caller takes.
    /// </para>
    /// <para>
    /// This is the FIFTH distinct meaning the numeral <c>1</c> carries in this refactor, and keeping
    /// them separate is the point. The others are <c>RetCode.PREVENT</c>, the broker's
    /// <c>VetoResult.PreventOnce</c>, the broker's exception-capture signal, and the item-change
    /// alphabet's <c>1</c> from <see cref="DataWindowServiceHost.ItemChanged"/>. All five are the
    /// same number and none is interchangeable with another.
    /// </para>
    /// <para>
    /// NOT VIRTUAL, because the legacy declares it as a function rather than an event: the
    /// overridable part is <see cref="OnEnable(bool)"/>, and the sequencing around it is fixed. The
    /// <c>in</c> modifier carries the legacy <c>readonly</c> parameter annotation across per
    /// AAP 0.4.5.2, which maps a <c>readonly</c> parameter onto an <c>in</c> parameter mechanically.
    /// </para>
    /// </remarks>
    public long SetEnabled(in bool enabled)
    {
        // n_cst_dwsvc.sru:L89 - idempotent early-out. Success, and OnEnable is NOT raised.
        if (Enabled == enabled)
        {
            return RetCode.OK;
        }

        // n_cst_dwsvc.sru:L90 - the veto test. The handler signals 1; this returns -1.
        if (OnEnable(enabled) == 1L)
        {
            return RetCode.FAILED;
        }

        // n_cst_dwsvc.sru:L92 - reached only when the hook allowed the change.
        Enabled = enabled;

        // n_cst_dwsvc.sru:L94
        return RetCode.OK;
    }

    /// <summary>
    /// Resolves a named DataWindow object to a handle - the port of
    /// <c>_of_getdwobject(readonly string dwoname)</c>
    /// (<c>n_cst_dwsvc.sru:L100-L125</c>). THROWS WHEN THE NAMED OBJECT DOES NOT EXIST.
    /// </summary>
    /// <param name="dwoName">The object name to resolve.</param>
    /// <returns>
    /// The handle, or <see langword="null"/> when the host has no valid object model.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// This service has not been attached to a host yet - see the remarks.
    /// </exception>
    /// <remarks>
    /// <para>
    /// TWO DISTINCT FAILURE MODES, AND THEY ARE NOT INTERCHANGEABLE. This is the single most
    /// easily-flattened behaviour in the file, so both are stated:
    /// </para>
    /// <para>
    /// 1. NO VALID OBJECT MODEL YIELDS NULL, QUIETLY. <c>:L119</c> is
    /// <c>if Not IsValidObject(#DataWindow.Object) then return dwo</c>, where <c>dwo</c> is the
    /// UNSET local declared at <c>:L117</c>. The legacy returns an unset handle and does not raise.
    /// The predicate is the ported <c>Predicates.IsValidObject</c>
    /// (<c>isvalidobject.srf:L11-L17</c>), used here rather than paraphrased.
    /// </para>
    /// <para>
    /// 2. A MISSING NAME THROWS. <c>:L121</c> carries the legacy's own warning that an exception
    /// occurs when the object does not exist, and <c>:L122</c> then performs the attribute lookup
    /// that raises it. This method therefore does NOT catch, does NOT translate and does NOT
    /// substitute a null sentinel for that case: it lets the exception propagate, because a caller
    /// that received null could not tell "the DataWindow has no object model" from "you asked for a
    /// column that is not there", and the legacy can. Constraint C-B forbids widening the contract
    /// with a guess; AAP 0.1.5 requires narrowing with a defined error instead, and here the legacy
    /// error IS the defined error.
    /// </para>
    /// <para>
    /// THE THIRD FAILURE MODE IS NEW, AND IT IS STRUCTURAL. Calling this before
    /// <see cref="OnInit"/> has attached a host is a programming fault rather than a data condition:
    /// the legacy would fault on a null object reference at <c>:L119</c>. An
    /// <see cref="InvalidOperationException"/> fails just as fast and names the cause, which is the
    /// fail-fast posture AAP 0.1.4 requires be preserved rather than softened.
    /// </para>
    /// </remarks>
    protected IDataWindowObject? GetDataWindowObject(in string dwoName)
    {
        DataWindowServiceHost host = RequireHost();

        // n_cst_dwsvc.sru:L117 - the unset local this method returns when the guard below trips.
        IDataWindowObject? dwo = null;

        // n_cst_dwsvc.sru:L119 - no valid object model, so return the unset handle and do not raise.
        if (!Predicates.IsValidObject(host.ObjectModel))
        {
            return dwo;
        }

        // n_cst_dwsvc.sru:L122 - the attribute lookup. Per the legacy comment at :L121 this RAISES
        // when dwoName does not exist, and that exception is deliberately allowed to propagate.
        dwo = host.GetObjectAttribute(dwoName);

        // n_cst_dwsvc.sru:L124
        return dwo;
    }

    /// <summary>
    /// Resolves a column by NUMBER to a handle - the port of
    /// <c>_of_getdwobject(readonly long colnum)</c> (<c>n_cst_dwsvc.sru:L97</c>).
    /// </summary>
    /// <param name="columnNumber">The one-based column number.</param>
    /// <returns>
    /// The handle, or <see langword="null"/> when the host has no valid object model.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// This service has not been attached to a host yet.
    /// </exception>
    /// <remarks>
    /// <para>
    /// THE DELEGATION IS THE BEHAVIOUR. <c>:L97</c> is a one-line body:
    /// <c>return _of_GetDWObject("#" + String(colNum))</c>. The <c>"#"</c> prefix is PowerBuilder's
    /// own convention for addressing a column positionally, and the same convention appears again at
    /// <c>:L666</c> as <c>"#" + String(colNum) + ".Name"</c>. Reimplementing this against a numeric
    /// lookup instead of composing the string would diverge from the oracle for every name that
    /// happens to collide with the <c>#n</c> form.
    /// </para>
    /// <para>
    /// Formatting is invariant-culture, which PowerScript's <c>String(long)</c> has no parameter for
    /// and does implicitly. It matters: a culture with digit-group separators would compose
    /// <c>"#1,234"</c> and resolve nothing. This is a substitution the legacy cannot express and is
    /// unobservable for every value it actually receives.
    /// </para>
    /// <para>
    /// It inherits both failure modes of the string overload verbatim, including the throw on a
    /// column number that does not exist.
    /// </para>
    /// </remarks>
    protected IDataWindowObject? GetDataWindowObject(in long columnNumber)
    {
        return GetDataWindowObject("#" + columnNumber.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Returns the attached host, or fails fast when this service has not been attached yet.
    /// </summary>
    /// <returns>The attached <see cref="DataWindowServiceHost"/>.</returns>
    /// <exception cref="InvalidOperationException">
    /// <see cref="OnInit"/> has not run, so there is no host to work against.
    /// </exception>
    /// <remarks>
    /// Exists so the not-attached fault is reported once, in one wording, from every member that
    /// needs the host - rather than surfacing as a <see cref="NullReferenceException"/> from whichever
    /// property read happened to touch it first. It is protected so derived services in
    /// Services/ and Expressions/ reach the same guard instead of re-deriving it.
    /// </remarks>
    protected DataWindowServiceHost RequireHost()
    {
        return DataWindow ?? throw new InvalidOperationException(
            "This DataWindow service is not attached to a host. OnInit must be called with a "
            + "DataWindowServiceHost before any host-facing member is used, exactly as "
            + "se_cst_dw raises OnInit on each attached service during its own construction "
            + "(ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L576-L580).");
    }
}

